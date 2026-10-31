using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

// Runs the duel on screen: pick -> each player records their secret attack and defense in a row
// (A attack, A defense, B attack, B defense). A gesture: Detect pen -> 3-2-1 holding the pen in the
// square -> drag it up, down, left or right -> the stroke is shown -> Redo / Confirm.
// Then the FIGHT button plays the clash (with each attacker's recorded war cry) and the result.
[RequireComponent(typeof(CardWeaponManager))]
public class DuelController : MonoBehaviour
{
    [Tooltip("Seconds of 3-2-1 while the pen is held in the square.")]
    public int countdownSeconds = 3;
    [Tooltip("Seconds to wait for the pen tip to reach the square before giving up.")]
    public float penWaitSeconds = 10f;
    [Tooltip("Seconds to drag the pen out of the square.")]
    public float captureSeconds = 4f;
    [Tooltip("Side of the square, as a fraction of the shorter screen side.")]
    [Range(0.1f, 0.4f)] public float squareSize = 0.18f;
    [Tooltip("Travel that counts as a gesture, as a fraction of the shorter screen side.")]
    [Range(0.08f, 0.4f)] public float strokeDistance = 0.15f;
    [Tooltip("How much the main axis must exceed the other one.")]
    [Range(1.1f, 3f)] public float strokeDominance = 1.3f;
    [Tooltip("Seconds without the pen before the capture is cancelled.")]
    public float lostPenTimeout = 0.3f;

    private enum Capture { None, Countdown, Armed, Review }

    public DuelMatch Match { get; private set; }
    // The pen tracker only reads the camera while this is true
    public bool WantsPen => capture == Capture.Countdown || capture == Capture.Armed;
    // Weapons are equipped and the cards stay on the table: players keep their last pose when covered
    public bool HoldsCards => Match != null && !manager.SetupInProgress && Match.Phase != DuelPhase.Pick && Match.Phase != DuelPhase.GameOver;
    private CardWeaponManager manager;
    private ColorGestureTracker pen;
    private ClashDirector clash;
    private readonly int[] shownHealth = { 100, 100 };

    private Capture capture;
    private AttackDirection pendingDirection;
    private Vector2 origin;
    private float captureEnd, reviewStart, lastPenSeen, penWaitEnd;
    private string notice;
    private WarCryMicrophone warCry;
    private float pendingCryPower;
    // The cry of the gesture under review, and each player's confirmed cry for this round's attack
    private AudioClip pendingCry;
    private readonly AudioClip[] cryClips = new AudioClip[2];
    private AudioSource cryAudio;
    // After the stroke the microphone keeps recording a moment, so a cry that goes on is not cut short
    private float cryTailEnd = -1f;
    private int lastCountdownNumber;
    private float nextComputerAction;
    private DuelPhase previousPhase;

    private GameObject hud;
    private Text prompt, subPrompt, bigText, fightText, roundText, banner, bannerUnder;
    private Coroutine bannerRoutine;
    // When the knockout blow landed this match (-1: none yet)
    private float knockOutTime = -1f;
    private GameObject promptBox;
    private readonly Button[] buttons = new Button[2];
    private readonly Text[] buttonTexts = new Text[2];
    private readonly System.Action[] buttonActions = new System.Action[2];
    private Button fightButton;
    private Image flash;
    private Coroutine flashRoutine;
    private Texture2D dotTexture, roundedTexture;
    private GUIStyle arrowStyle;

    private const int Left = 0, Right = 1;
    private const float CryTail = 1.5f;
    // The victory music plays this long, then fades out
    private const float VictorySeconds = 2.5f;
    private static readonly Color FightColor = new Color(0.98f, 0.74f, 0.25f);
    private static readonly Color FightTextColor = new Color(0.25f, 0.12f, 0.03f);
    private static readonly Color KnockOutColor = new Color(1f, 0.2f, 0.12f);
    private static readonly Color WinnerColor = new Color(1f, 0.82f, 0.25f);
    private static readonly Color PromptColor = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color ButtonColor = new Color(0.5f, 0.36f, 0.06f, 0.92f);
    private static readonly Color PrimaryColor = new Color(0.12f, 0.55f, 0.25f, 0.95f);
    private static readonly Color TraceColor = new Color(1f, 0.85f, 0.2f);
    private static readonly Color SubColor = new Color(0.96f, 0.96f, 0.96f);
    private static readonly Color NoticeColor = new Color(1f, 0.7f, 0.3f);

    private static string L(string english, string portuguese) => Localization.Get(english, portuguese);

    void Start()
    {
        warCry = GetComponent<WarCryMicrophone>();
        if (warCry == null) warCry = gameObject.AddComponent<WarCryMicrophone>();
        // The recording must hold the countdown, the whole drag and the tail after it
        warCry.recordSeconds = Mathf.Max(warCry.recordSeconds, countdownSeconds + Mathf.CeilToInt(penWaitSeconds + captureSeconds + CryTail) + 2);
        cryAudio = gameObject.AddComponent<AudioSource>();
        cryAudio.playOnAwake = false;
        cryAudio.spatialBlend = 0f;
        manager = GetComponent<CardWeaponManager>();
        pen = GetComponent<ColorGestureTracker>();
        clash = GetComponent<ClashDirector>();
        if (clash == null) clash = gameObject.AddComponent<ClashDirector>();
        BuildHUD();
        hud.SetActive(false);
    }

    void OnDestroy()
    {
        if (dotTexture != null) Destroy(dotTexture);
        if (roundedTexture != null) Destroy(roundedTexture);
        ClearCries();
    }

    void Update()
    {
        if (manager.SetupInProgress)
        {
            hud.SetActive(false);
            CancelCapture();
            return;
        }
        if (Match == null) Restart();
        hud.SetActive(true);
        UpdateComputers();
        UpdateCapture();
        UpdateWarCryAura();
        RefreshHUD();
    }

    public bool CanEquip(int player, string card) => Match != null && Match.CanEquip(player, card);

    private void UpdateComputers()
    {
        if (Time.timeScale == 0f) return;
        if (Match.Phase != previousPhase)
        {
            previousPhase = Match.Phase;
            nextComputerAction = Time.unscaledTime + (Match.Phase == DuelPhase.Result ? 2.5f : 0.8f);
        }
        if (Time.unscaledTime < nextComputerAction) return;
        if (Match.Phase == DuelPhase.Pick)
        {
            for (int i = 0; i < 2; i++)
                if (ArenaMenu.IsComputer(i) && Match.Equipped(i) == null) manager.EquipRandomComputerWeapon(i);
        }
        else if (Match.Phase == DuelPhase.Record && ArenaMenu.IsComputer(Match.CurrentPlayer))
        {
            // Simulate a microphone level with the same threshold and damage curve as humans.
            float simulatedPower = Match.CurrentSlot == GestureSlot.Attack
                ? WarCryRules.Power(Random.Range(-45f, -5f), warCry.thresholdDb, warCry.fullPowerDb) : 0;
            Match.Record(Match.CurrentPlayer, Match.CurrentSlot, (AttackDirection)Random.Range(0, 4), simulatedPower);
            Match.ConfirmStep();
            CancelCapture();
        }
        else if (ArenaMenu.Mode == ArenaMode.ComputerVsComputer)
        {
            if (Match.Phase == DuelPhase.Fight && PlayersReady) Fight();
            else if (Match.Phase == DuelPhase.Result) Advance();
        }
        nextComputerAction = Time.unscaledTime + 0.8f;
    }
    public bool IsUsed(string card) => Match != null && Match.IsUsed(card);
    // Health as the players see it: it only drops when the blade connects in the clash
    public int ShownHealth(int player) => shownHealth[player];

    public bool Equip(int player, string id, WeaponStats stats)
    {
        if (stats == null || Match == null) return false;
        return Match.Equip(player, new DuelMatch.Card(id, stats.weaponClass, stats.baseDamage));
    }

    // Editor keyboard: an arrow fills the gesture being captured, like a pen stroke
    public void KeyboardGesture(AttackDirection direction)
    {
        if (capture != Capture.Countdown && capture != Capture.Armed) return;
        Review(direction);
    }

    // ---------- Recording ----------

    private void DetectPen()
    {
        if (Match.Phase != DuelPhase.Record) return;
        DropPendingCry();
        if (Match.CurrentSlot == GestureSlot.Attack) warCry.Begin();
        capture = Capture.Countdown;
        captureEnd = Time.unscaledTime + countdownSeconds;
        penWaitEnd = Time.unscaledTime + penWaitSeconds;
        // The button's own click is the "3"
        lastCountdownNumber = countdownSeconds;
        notice = null;
    }

    private void UpdateCapture()
    {
        if (Match.Phase != DuelPhase.Record) { CancelCapture(); return; }
        if (Time.timeScale == 0f) { CancelCapture(); return; }
        float now = Time.unscaledTime;
        if (capture == Capture.Countdown && !PenInSquare())
        {
            // The 3-2-1 only runs with the pen tip in the square: it starts over when the pen leaves
            captureEnd = now + countdownSeconds;
            lastCountdownNumber = countdownSeconds;
            if (now >= penWaitEnd)
            {
                CancelCapture();
                notice = L("Pen not found - try again.", "Caneta não encontrada - tenta outra vez.");
            }
        }
        else if (capture == Capture.Countdown && now < captureEnd)
        {
            // A click for each number of the 3-2-1
            int number = Mathf.CeilToInt(captureEnd - now);
            if (number != lastCountdownNumber) GameAudio.Play("click");
            lastCountdownNumber = number;
        }
        else if (capture == Capture.Countdown && now >= captureEnd)
        {
            origin = pen.Tip;
            lastPenSeen = now;
            GameAudio.Play("go");
            capture = Capture.Armed;
            captureEnd = now + captureSeconds;
        }
        else if (capture == Capture.Armed)
        {
            warCry.SetMeasuring(Match.CurrentSlot == GestureSlot.Attack && Time.timeScale > 0);
            warCry.Sample();
            if (pen != null && pen.TipVisible)
            {
                lastPenSeen = now;
                var direction = PenStroke.Classify(origin.x, origin.y, pen.Tip.x, pen.Tip.y,
                    (float)Screen.width / Screen.height, strokeDistance, strokeDominance);
                if (direction.HasValue) { Review(direction.Value); return; }
            }
            if (now - lastPenSeen > lostPenTimeout)
            {
                CancelCapture();
                notice = L("Lost the pen - try again.", "A caneta perdeu-se - tenta outra vez.");
            }
            else if (now >= captureEnd)
            {
                CancelCapture();
                notice = L("No gesture detected - try again.", "Nenhum gesto detetado - tenta outra vez.");
            }
        }
        else if (capture == Capture.Review && cryTailEnd >= 0f && now >= cryTailEnd)
        {
            FinishCry();
        }
    }

    private bool PenInSquare()
    {
        if (pen == null || !pen.TipVisible) return false;
        float half = squareSize * 0.5f * Mathf.Min(Screen.width, Screen.height);
        Vector2 tip = pen.Tip;
        return Mathf.Abs(tip.x - 0.5f) * Screen.width <= half && Mathf.Abs(tip.y - 0.5f) * Screen.height <= half;
    }

    private void Review(AttackDirection direction)
    {
        DropPendingCry();
        if (Match.CurrentSlot == GestureSlot.Attack)
        {
            // The bonus is set by the drag; the recording goes on a moment for the end of the shout
            pendingCryPower = warCry.PeakPower;
            warCry.SetMeasuring(false);
            if (pendingCryPower > 0) cryTailEnd = Time.unscaledTime + CryTail;
            else warCry.End();
        }
        else warCry.End();
        pendingDirection = direction;
        capture = Capture.Review;
        reviewStart = Time.unscaledTime;
        notice = null;
    }

    private void ConfirmGesture()
    {
        if (capture != Capture.Review) return;
        int player = Match.CurrentPlayer;
        if (Match.CurrentSlot == GestureSlot.Attack)
        {
            FinishCry();
            if (cryClips[player] != null) Destroy(cryClips[player]);
            cryClips[player] = pendingCry;
            pendingCry = null;
        }
        Match.Record(player, Match.CurrentSlot, pendingDirection, pendingCryPower);
        Match.ConfirmStep();
        DropPendingCry();
        capture = Capture.None;
    }

    private void CancelCapture()
    {
        if (warCry != null) warCry.End();
        DropPendingCry();
        capture = Capture.None;
    }

    // Cuts the shout out of the recording once its tail is in
    private void FinishCry()
    {
        if (cryTailEnd < 0f) return;
        cryTailEnd = -1f;
        if (pendingCry != null) Destroy(pendingCry);
        pendingCry = warCry.TakeCry();
        warCry.End();
    }

    private void DropPendingCry()
    {
        if (pendingCry != null) Destroy(pendingCry);
        pendingCry = null;
        pendingCryPower = 0;
        cryTailEnd = -1f;
    }

    private void ClearCries()
    {
        DropPendingCry();
        for (int i = 0; i < cryClips.Length; i++)
        {
            if (cryClips[i] != null) Destroy(cryClips[i]);
            cryClips[i] = null;
        }
    }

    // The attacker's own recorded shout, louder with a stronger cry
    private void PlayCry(int attacker)
    {
        float power = Match.WarCryPower(attacker);
        if (cryClips[attacker] == null || power <= 0f || cryAudio == null || !GameAudio.EffectsEnabled) return;
        cryAudio.PlayOneShot(cryClips[attacker], 0.6f + 0.4f * power);
    }

    private void UpdateWarCryAura()
    {
        for (int i = 0; i < 2; i++)
        {
            float power = Match.WarCryPower(i);
            if (Match.Phase == DuelPhase.Record && Match.CurrentPlayer == i && Match.CurrentSlot == GestureSlot.Attack)
                power = capture == Capture.Armed ? warCry.PeakPower : capture == Capture.Review ? pendingCryPower : 0;
            if (Match.Phase == DuelPhase.Result || Match.Phase == DuelPhase.GameOver) power = 0;
            manager.GetAvatar(i)?.SetWarCryPower(power);
        }
    }

    // Headline while drawing an attack
    private string CryFeedback()
    {
        if (!warCry.Ready) return L("No microphone - normal damage", "Sem microfone - dano normal");
        return L($"Shout for more damage! {Times(warCry.PeakPower)}", $"Grita para mais dano! {Times(warCry.PeakPower)}");
    }

    // The cry's damage multiplier, always written "x1.25" (never "x1,25"), in gold
    private static string Times(float power) =>
        $"<color=#FFCB71>x{WarCryRules.Multiplier(power).ToString("0.00", CultureInfo.InvariantCulture)}</color>";

    // ---------- Fight ----------

    // The microphone is recording a war cry: GameAudio lowers the music so the cry is not the music played back
    public bool Listening => warCry != null && warCry.Recording;

    private bool PlayersReady => manager.GetAttackPlayer(0) != null && manager.GetAttackPlayer(1) != null;

    private void Fight()
    {
        if (Match.Phase != DuelPhase.Fight) return;
        if (!PlayersReady)
        {
            notice = L("Keep both PLAYER cards (with their weapons) in view.", "Mantém as duas cartas PLAYER (com as armas) visíveis.");
            return;
        }
        notice = null;
        StartCoroutine(RunClash());
    }

    private IEnumerator RunClash()
    {
        knockOutTime = -1f;
        StartCoroutine(RoundBanner());
        var exchanges = Match.Fight();
        yield return clash.Play(exchanges, null,
            exchange => PlayCry(exchange.attacker),
            exchange =>
            {
                shownHealth[exchange.Defender] = exchange.defenderHealth;
                ShowHit(exchange);
                if (exchange.blocked) GameAudio.Play("block");
                else if (exchange.classMultiplier > 1) GameAudio.Play("hit_critical", "hit");
                else GameAudio.Play("hit");
                if (exchange.defenderHealth == 0)
                {
                    knockOutTime = Time.unscaledTime;
                    // Only the punchy start of the K.O. sound: the victory music takes over below
                    GameAudio.PlayLong("ko");
                    StartCoroutine(PlayLater("ko_fall", 0.4f));
                    // A K.O. means the other player won: both at once, no waiting
                    ShowBanner("K.O.!", KnockOutColor, L($"{ColoredName(exchange.attacker)} wins!", $"{ColoredName(exchange.attacker)} venceu!"));
                    Flash(new Color(0.95f, 0.1f, 0.05f, 0.45f));
                }
                // Red only when hit: a block costs nothing, so no flash
                else if (!exchange.blocked) Flash(new Color(0.95f, 0.15f, 0.15f, 0.2f));
            });
        Match.FinishClash();

        // A K.O. or the last round: straight to the winner, no result screen
        bool knockedOut = Match.Health(0) == 0 || Match.Health(1) == 0;
        if (knockedOut)
        {
            // 0.7 s after the blow the K.O. sound fades into the victory music, then the buttons come
            yield return WaitUntilAfterKnockOut(0.7f);
            GameAudio.StopLong(0.2f);
            GameAudio.PlayLong("victory", VictorySeconds);
            yield return WaitUntilAfterKnockOut(1f);
        }
        if (knockedOut || Match.Round == 3)
        {
            Match.Continue();
            ClearCries();
            if (!knockedOut)
            {
                ShowBanner(WinnerText(), WinnerColor);
                GameAudio.PlayLong("victory", VictorySeconds);
            }
        }
    }

    private IEnumerator WaitUntilAfterKnockOut(float seconds)
    {
        while (Time.unscaledTime < knockOutTime + seconds) yield return null;
    }

    private static IEnumerator PlayLater(string sound, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        GameAudio.Play(sound);
    }

    private string WinnerText() => Match.Winner < 0 ? L("Draw!", "Empate!") :
        L($"{ColoredName(Match.Winner)} wins!", $"{ColoredName(Match.Winner)} venceu!");

    // Big centred text that pops in and stays: K.O.! (with the winner under it) or the winner
    private void ShowBanner(string text, Color color, string under = "")
    {
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        banner.text = text;
        banner.color = color;
        bannerUnder.text = under;
        bannerRoutine = StartCoroutine(PopBanner());
    }

    // "ROUND 2" for a moment when FIGHT is pressed
    private IEnumerator RoundBanner()
    {
        ShowBanner(L($"ROUND {Match.Round}", $"RONDA {Match.Round}"), Color.white);
        yield return new WaitForSecondsRealtime(0.9f);
        // Unless a K.O. replaced it meanwhile
        if (knockOutTime < 0f) { banner.text = ""; bannerUnder.text = ""; }
    }

    private IEnumerator PopBanner()
    {
        const float duration = 0.25f;
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            var scale = Vector3.one * Mathf.Lerp(1.8f, 1f, 1f - (1f - t / duration) * (1f - t / duration));
            banner.rectTransform.localScale = bannerUnder.rectTransform.localScale = scale;
            yield return null;
        }
        banner.rectTransform.localScale = bannerUnder.rectTransform.localScale = Vector3.one;
        bannerRoutine = null;
    }

    // Damage floating up from the defender, coloured by how much it hurt
    private void ShowHit(DuelMatch.Exchange exchange)
    {
        var defender = manager.GetAvatar(exchange.Defender);
        if (defender == null) return;
        string tag = "";
        Color tagColor = Color.white;
        // A K.O. needs no tag: the big K.O.! banner says it
        if (exchange.defenderHealth == 0) { }
        // White, not blue: cyan is player A's colour
        else if (exchange.blocked) { tag = L("BLOCKED", "BLOQUEADO"); tagColor = Color.white; }
        else if (exchange.classMultiplier > 1) { tag = L("CRITICAL!", "CRÍTICO!"); tagColor = new Color(1f, 0.82f, 0.2f); }
        else if (exchange.classMultiplier < 1) { tag = L("Not very effective...", "Pouco efetivo..."); tagColor = new Color(0.75f, 0.75f, 0.75f); }
        // A block stops all the damage: no "-0", only the tag
        string value = exchange.blocked ? "" : $"-{exchange.damage}";
        // Floats away from the attacker, beside the defender rather than over their name
        var attacker = manager.GetAvatar(exchange.attacker);
        float side = 1f;
        var camera = Camera.main;
        if (attacker != null && camera != null)
            side = camera.WorldToScreenPoint(defender.HitPoint).x >= camera.WorldToScreenPoint(attacker.HitPoint).x ? 1f : -1f;
        FloatingCombatText.Spawn(hud.transform, defender.HitPoint, value, DamageColor(exchange.damage), tag, tagColor, side);
    }

    // Yellow for a scratch to orange for a heavy blow; red is kept for the K.O.
    private static Color DamageColor(int damage)
    {
        Color low = new Color(1f, 0.95f, 0.35f), high = new Color(1f, 0.55f, 0.1f);
        return Color.Lerp(low, high, Mathf.InverseLerp(10f, 60f, damage));
    }

    private void Advance()
    {
        if (Match.Phase == DuelPhase.GameOver) { Restart(); return; }
        if (Match.Phase != DuelPhase.Result) return;
        Match.Continue();
        ClearCries();
        manager.ClearRoundWeapons();
        for (int i = 0; i < 2; i++) shownHealth[i] = Match.Health(i);
    }

    private void Restart()
    {
        StopAllCoroutines();
        Match = new DuelMatch();
        shownHealth[0] = shownHealth[1] = 100;
        CancelCapture();
        ClearCries();
        notice = null;
        if (flash != null) flash.color = Color.clear;
        if (banner != null) { banner.text = ""; banner.rectTransform.localScale = Vector3.one; }
        if (bannerUnder != null) bannerUnder.text = "";
        bannerRoutine = flashRoutine = null;
        knockOutTime = -1f;
        GameAudio.StopLong();
        manager.ClearRoundWeapons();
        for (int i = 0; i < 2; i++)
        {
            var avatar = manager.GetAvatar(i);
            if (avatar != null) avatar.ResetCombat();
        }
    }

    private void Flash(Color color)
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(AnimateFlash(color, 0.85f));
    }

    private IEnumerator AnimateFlash(Color color, float duration)
    {
        const float rise = 0.06f;
        float peak = color.a;
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            color.a = t < rise ? Mathf.Lerp(0, peak, t / rise) : Mathf.Lerp(peak, 0, (t - rise) / (duration - rise));
            flash.color = color;
            yield return null;
        }
        flash.color = Color.clear;
        flashRoutine = null;
    }

    // ---------- HUD ----------

    private string PlayerName(int i)
    {
        string name = PlayerProfiles.Instance != null ? PlayerProfiles.Instance.Get(i).name : "";
        return string.IsNullOrWhiteSpace(name) ? L($"Player {(i == 0 ? "A" : "B")}", $"Jogador {(i == 0 ? "A" : "B")}") : name;
    }

    // The name in the player's colour, for the prompts and the banner (a typed "<" cannot open a tag)
    private string ColoredName(int i) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(PlayerAvatar.ColorOf(i))}>{PlayerName(i).Replace("<", "‹")}</color>";

    private static string Arrow(AttackDirection direction)
    {
        switch (direction)
        {
            case AttackDirection.Up: return "▲";
            case AttackDirection.Down: return "▼";
            case AttackDirection.Left: return "◄";
            default: return "►";
        }
    }

    private static string DirectionName(AttackDirection direction)
    {
        switch (direction)
        {
            case AttackDirection.Up: return L("Up", "Cima");
            case AttackDirection.Down: return L("Down", "Baixo");
            case AttackDirection.Left: return L("Left", "Esquerda");
            default: return L("Right", "Direita");
        }
    }

    private void RefreshHUD()
    {
        DuelPhase phase = Match.Phase;
        SetButton(Left, null, null);
        SetButton(Right, null, null);
        fightButton.gameObject.SetActive(false);
        bigText.text = "";
        roundText.text = $"{L("ROUND", "RONDA")} {Match.Round}/3";
        // A short headline and a small line under it
        string text = "", sub = "";

        switch (phase)
        {
            case DuelPhase.Pick:
                text = L("Pick a weapon each", "Escolham uma arma cada um");
                sub = L("Slide a card next to your player · Player A: regular cards · Player B: II cards",
                        "Encostem uma carta ao vosso jogador · Jogador A: cartas normais · Jogador B: cartas II");
                if (ArenaMenu.Mode == ArenaMode.ComputerVsComputer)
                {
                    text = L("Computer vs computer", "Computador contra computador");
                    sub = L("Keep both PLAYER cards in view", "Mantém as duas cartas PLAYER visíveis");
                }
                else if (ArenaMenu.Mode == ArenaMode.PlayerVsComputer)
                {
                    text = L($"{ColoredName(0)}: pick a weapon", $"{ColoredName(0)}: escolhe uma arma");
                    sub = L("Slide a regular card next to your player · Player B is the computer",
                            "Encosta uma carta normal ao teu jogador · O jogador B é o computador");
                }
                break;
            case DuelPhase.Record:
                text = RecordPrompt(out sub);
                break;
            case DuelPhase.Fight:
                text = L("Ready? Press FIGHT!", "Prontos? Carreguem em LUTA!");
                sub = L("Put the pen away and keep both PLAYER cards in view", "Afastem a caneta e mantenham as cartas PLAYER visíveis");
                fightButton.gameObject.SetActive(true);
                fightText.text = L("FIGHT!", "LUTA!");
                // A slow pulse while it waits to be pressed
                fightButton.transform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.unscaledTime * 4f));
                break;
            case DuelPhase.Clash:
                break;
            case DuelPhase.Result:
                // Only between rounds: the last one goes straight to the winner (RunClash)
                SetButton(Right, L("Next round", "Próxima ronda"), Advance);
                break;
            case DuelPhase.GameOver:
                // The banner names the winner (under the K.O.! after a knockout)
                SetButton(Right, L("Play again", "Jogar outra vez"), Restart);
                SetButton(Left, L("Main menu", "Menu principal"), () =>
                {
                    Restart();
                    FindFirstObjectByType<PlayerSetupUI>()?.ResetSetup();
                    GetComponent<ArenaMenu>().Open();
                });
                break;
        }
        bool hasNotice = !string.IsNullOrEmpty(notice);
        if (hasNotice) sub = notice;
        prompt.text = text;
        subPrompt.text = sub;
        subPrompt.color = hasNotice ? NoticeColor : SubColor;
        promptBox.SetActive(text.Length > 0 || sub.Length > 0);
    }

    // Headline: who records what; sub: who must look away, the cry, the time left
    private string RecordPrompt(out string sub)
    {
        sub = "";
        if (ArenaMenu.IsComputer(Match.CurrentPlayer)) return L("The computer is choosing in secret...", "O computador está a escolher em segredo...");
        int me = Match.CurrentPlayer, other = 1 - me;
        bool attack = Match.CurrentSlot == GestureSlot.Attack;
        switch (capture)
        {
            case Capture.Countdown:
                bigText.text = Mathf.CeilToInt(captureEnd - Time.unscaledTime).ToString();
                SetButton(Left, L("Cancel", "Cancelar"), CancelCapture);
                if (attack) sub = L("Get ready to shout!", "Prepara-te para gritar!");
                return PenInSquare()
                    ? L("Pen found - hold it there", "Caneta encontrada - mantém-na aí")
                    : L("Put the pen tip in the square", "Põe a ponta da caneta no quadrado");
            case Capture.Armed:
                bigText.text = L("DRAG!", "ARRASTA!");
                SetButton(Left, L("Cancel", "Cancelar"), CancelCapture);
                // The cry and the time left on the big line, how to drag under it
                string seconds = $"{Mathf.CeilToInt(captureEnd - Time.unscaledTime)} s";
                string drag = L("Drag the pen up, down, left or right", "Arrasta a caneta para cima, baixo, esquerda ou direita");
                if (!attack) return $"{drag}  ·  {seconds}";
                sub = drag;
                return $"{CryFeedback()}  ·  {seconds}";
            case Capture.Review:
                SetButton(Left, L("Redo", "Repetir"), DetectPen);
                SetButton(Right, L("Confirm", "Confirmar"), ConfirmGesture);
                sub = L("Redo it or confirm", "Repete ou confirma");
                string slot = attack ? L("Attack", "Ataque") : L("Defense", "Defesa");
                string cry = attack && pendingCryPower > 0 ? L($"  ·  War cry {Times(pendingCryPower)}", $"  ·  Grito {Times(pendingCryPower)}") : "";
                return $"{slot}: {Arrow(pendingDirection)} {DirectionName(pendingDirection)}{cry}";
        }

        SetButton(Right, L("Detect pen", "Detetar caneta"), DetectPen);
        if (!ArenaMenu.IsComputer(other)) sub = L($"{ColoredName(other)}, don't look!", $"{ColoredName(other)}, não olhes!");
        // The pen changes hands once, after the first player's defense
        if (Match.Step == 2) sub = L($"Pass the pen to {ColoredName(me)}", $"Passa a caneta a {ColoredName(me)}") + (sub.Length > 0 ? "  ·  " + sub : "");
        return attack ? L($"{ColoredName(me)}: draw your attack", $"{ColoredName(me)}: desenha o ataque")
                      : L($"{ColoredName(me)}: draw your defense", $"{ColoredName(me)}: desenha a defesa");
    }

    private void SetButton(int slot, string label, System.Action action)
    {
        buttonActions[slot] = action;
        buttons[slot].gameObject.SetActive(action != null);
        if (action != null) buttonTexts[slot].text = label;
    }

    // ---------- Pen square, live stroke and recorded trace (screen pixels) ----------

    void OnGUI()
    {
        if (Match == null || Match.Phase != DuelPhase.Record || manager.SetupInProgress || capture == Capture.None) return;
        if (pen != null && pen.ShowSettings) return;
        if (dotTexture == null) dotTexture = MakeDot(32);
        float side = Mathf.Min(Screen.width, Screen.height);
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Color previous = GUI.color;

        if (capture == Capture.Countdown)
        {
            float half = squareSize * 0.5f * side, thickness = Mathf.Max(3f, side * 0.008f);
            GUI.color = PenInSquare() ? new Color(0.2f, 1f, 0.35f) : Color.white;
            GUI.DrawTexture(new Rect(center.x - half, center.y - half, half * 2, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(center.x - half, center.y + half - thickness, half * 2, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(center.x - half, center.y - half, thickness, half * 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(center.x + half - thickness, center.y - half, thickness, half * 2), Texture2D.whiteTexture);
        }
        else if (capture == Capture.Armed && pen != null && pen.TipVisible)
        {
            // Live line from where the pen started to where it is now
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            DrawDots(ToGui(origin), ToGui(pen.Tip), side * 0.025f, side * 0.018f, 1f);
        }
        else if (capture == Capture.Review)
        {
            // Dotted trace from the middle towards the recorded direction, lighting up from the centre outwards
            Vector2 dir = pendingDirection == AttackDirection.Up ? Vector2.down : pendingDirection == AttackDirection.Down ? Vector2.up :
                pendingDirection == AttackDirection.Left ? Vector2.left : Vector2.right;
            Vector2 end = center + dir * side * 0.3f;
            float progress = Mathf.Repeat((Time.unscaledTime - reviewStart) / 1.1f, 1f);
            GUI.color = TraceColor;
            DrawDots(center, end, side * 0.035f, side * 0.022f, Mathf.Min(1f, progress * 1.4f));
            if (arrowStyle == null) arrowStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            arrowStyle.fontSize = Mathf.RoundToInt(side * 0.09f);
            arrowStyle.normal.textColor = TraceColor;
            float size = side * 0.14f;
            GUI.Label(new Rect(end.x - size * 0.5f, end.y - size * 0.5f, size, size), Arrow(pendingDirection), arrowStyle);
        }
        GUI.color = previous;
    }

    // Viewport (y up) to GUI pixels (y down)
    private static Vector2 ToGui(Vector2 viewport) => new Vector2(viewport.x * Screen.width, (1f - viewport.y) * Screen.height);

    // Dots from a to b, drawn up to `fill` (0-1) of the way
    private void DrawDots(Vector2 a, Vector2 b, float spacing, float dotSize, float fill)
    {
        float length = Vector2.Distance(a, b);
        int count = Mathf.Max(1, Mathf.FloorToInt(length / spacing));
        int shown = Mathf.CeilToInt(count * fill);
        for (int i = 0; i <= shown && i <= count; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, (float)i / count);
            GUI.DrawTexture(new Rect(p.x - dotSize * 0.5f, p.y - dotSize * 0.5f, dotSize, dotSize), dotTexture);
        }
    }

    private static Texture2D MakeDot(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size * 0.5f - 1;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
            texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - distance + 0.5f)));
        }
        texture.Apply();
        return texture;
    }

    // ---------- HUD layout ----------

    private void BuildHUD()
    {
        hud = new GameObject("Duel HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        hud.transform.SetParent(transform, false);
        var canvas = hud.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = hud.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;

        // Full screen colour flash on impact, under everything else
        flash = Box(hud.transform, "Impact Flash", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        flash.color = Color.clear;
        flash.raycastTarget = false;

        roundText = Label(hud.transform, 22);
        Rect(roundText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -46), new Vector2(200, -10));
        roundText.alignment = TextAnchor.MiddleLeft;
        Shadow(roundText);

        // Countdown and "DRAG!"
        bigText = Label(hud.transform, 64);
        Rect(bigText.rectTransform, new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.84f), Vector2.zero, Vector2.zero);
        bigText.fontStyle = FontStyle.Bold;
        Shadow(bigText);

        // K.O.! and the winner, in the middle of the screen
        banner = Label(hud.transform, 130);
        Rect(banner.rectTransform, new Vector2(0.05f, 0.4f), new Vector2(0.95f, 0.75f), Vector2.zero, Vector2.zero);
        banner.fontStyle = FontStyle.Bold;
        // The winner's name is in their colour
        banner.supportRichText = true;
        Shadow(banner);
        var thick = banner.gameObject.AddComponent<Outline>();
        thick.effectColor = new Color(0, 0, 0, 0.6f);
        thick.effectDistance = new Vector2(5, -5);
        // The winner, right under the K.O.!
        bannerUnder = Label(hud.transform, 60);
        Rect(bannerUnder.rectTransform, new Vector2(0.05f, 0.28f), new Vector2(0.95f, 0.42f), Vector2.zero, Vector2.zero);
        bannerUnder.fontStyle = FontStyle.Bold;
        bannerUnder.supportRichText = true;
        Shadow(bannerUnder);

        // FIGHT: a rounded gold button above the prompt, clear of the characters
        fightButton = MakeButton(hud.transform, "Fight", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-150, 144), new Vector2(150, 230), out fightText, 46);
        var fightImage = fightButton.GetComponent<Image>();
        fightImage.sprite = RoundedSprite();
        fightImage.type = Image.Type.Sliced;
        fightImage.color = FightColor;
        var fightShadow = fightButton.gameObject.AddComponent<Shadow>();
        fightShadow.effectColor = new Color(0, 0, 0, 0.55f);
        fightShadow.effectDistance = new Vector2(0, -6);
        fightText.fontStyle = FontStyle.Bold;
        fightText.color = FightTextColor;
        Rect(fightText.rectTransform, Vector2.zero, Vector2.one, new Vector2(70, 2), new Vector2(-16, -2));
        // Two crossed swords before the word
        for (int i = 0; i < 2; i++)
        {
            var sword = Box(fightButton.transform, "Sword", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(28, -24), new Vector2(76, 24));
            sword.sprite = manager.lightIcon;
            sword.preserveAspect = true;
            sword.color = manager.lightIcon != null ? FightTextColor : Color.clear;
            sword.raycastTarget = false;
            sword.rectTransform.localScale = new Vector3(i == 0 ? 1f : -1f, 1f, 1f);
        }
        fightButton.onClick.AddListener(Fight);

        // Bottom row: secondary button on the left, text in the middle, main button on the right
        var box = Box(hud.transform, "Turn Prompt", new Vector2(0.21f, 0), new Vector2(0.79f, 0), new Vector2(0, 10), new Vector2(0, 130));
        box.color = PromptColor;
        box.raycastTarget = false;
        promptBox = box.gameObject;
        prompt = Label(box.transform, 30);
        Rect(prompt.rectTransform, new Vector2(0, 0.46f), Vector2.one, new Vector2(10, 0), new Vector2(-10, -6));
        prompt.fontStyle = FontStyle.Bold;
        // Player names are in their colour
        prompt.supportRichText = true;
        Shadow(prompt);
        // Readable at arm's length: big, near white, never shrunk below 18
        subPrompt = Label(box.transform, 24);
        subPrompt.resizeTextMinSize = 18;
        subPrompt.supportRichText = true;
        Rect(subPrompt.rectTransform, Vector2.zero, new Vector2(1, 0.46f), new Vector2(10, 6), new Vector2(-10, 0));
        subPrompt.color = SubColor;
        Shadow(subPrompt);

        buttons[Left] = MakeButton(hud.transform, "Left Action", new Vector2(0.01f, 0), new Vector2(0.2f, 0), new Vector2(0, 14), new Vector2(0, 74), out buttonTexts[Left], 24);
        buttons[Right] = MakeButton(hud.transform, "Right Action", new Vector2(0.8f, 0), new Vector2(0.99f, 0), new Vector2(0, 14), new Vector2(0, 74), out buttonTexts[Right], 24);
        buttons[Right].GetComponent<Image>().color = PrimaryColor;
        for (int i = 0; i < buttons.Length; i++)
        {
            int slot = i;
            buttons[i].onClick.AddListener(() => buttonActions[slot]?.Invoke());
        }
    }

    // White rounded rectangle, sliced so the corners keep their size on any button
    private Sprite RoundedSprite()
    {
        const int size = 64, radius = 22;
        roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Distance into the nearest corner's circle; anti-aliased edge
            float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
            float dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
            float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            roundedTexture.SetPixel(x, y, new Color(1, 1, 1, alpha));
        }
        roundedTexture.Apply();
        return Sprite.Create(roundedTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    private static void Shadow(Text text)
    {
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0.85f);
        outline.effectDistance = new Vector2(2, -2);
    }

    private static Button MakeButton(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, out Text label, int fontSize)
    {
        var image = Box(parent, name, min, max, offsetMin, offsetMax);
        image.color = ButtonColor;
        var button = image.gameObject.AddComponent<Button>();
        button.onClick.AddListener(() => GameAudio.Play("click"));
        label = Label(image.transform, fontSize);
        Rect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(6, 2), new Vector2(-6, -2));
        return button;
    }

    private static Image Box(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        Rect((RectTransform)obj.transform, min, max, offsetMin, offsetMax);
        return obj.GetComponent<Image>();
    }

    private static Text Label(Transform parent, int size)
    {
        var obj = new GameObject("Text", typeof(RectTransform), typeof(Text));
        obj.transform.SetParent(parent, false);
        var text = obj.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 12;
        text.resizeTextMaxSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.supportRichText = false;
        text.raycastTarget = false;
        return text;
    }

    private static void Rect(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
    }
}
