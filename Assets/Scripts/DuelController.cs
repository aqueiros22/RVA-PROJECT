using System.Collections;
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
    private float captureEnd, reviewStart, lastPenSeen;
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
    private Text prompt, subPrompt, bigText, fightText, roundText, legendHint, banner;
    private Coroutine bannerRoutine;
    private GameObject promptBox, legend;
    private readonly Button[] buttons = new Button[2];
    private readonly Text[] buttonTexts = new Text[2];
    private readonly System.Action[] buttonActions = new System.Action[2];
    private Button fightButton;
    private Image flash;
    private Coroutine flashRoutine;
    private Texture2D dotTexture;
    private GUIStyle arrowStyle;

    private const int Left = 0, Right = 1;
    private const float CryTail = 1.5f;
    private static readonly Color KnockOutColor = new Color(1f, 0.2f, 0.12f);
    private static readonly Color WinnerColor = new Color(1f, 0.82f, 0.25f);
    private static readonly Color PromptColor = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Color ButtonColor = new Color(0.5f, 0.36f, 0.06f, 0.92f);
    private static readonly Color PrimaryColor = new Color(0.12f, 0.55f, 0.25f, 0.95f);
    private static readonly Color TraceColor = new Color(1f, 0.85f, 0.2f);
    private static readonly Color SubColor = new Color(0.85f, 0.85f, 0.85f);
    private static readonly Color NoticeColor = new Color(1f, 0.7f, 0.3f);

    private static string L(string english, string portuguese) => Localization.Get(english, portuguese);

    void Start()
    {
        warCry = GetComponent<WarCryMicrophone>();
        if (warCry == null) warCry = gameObject.AddComponent<WarCryMicrophone>();
        // The recording must hold the countdown, the whole drag and the tail after it
        warCry.recordSeconds = Mathf.Max(warCry.recordSeconds, countdownSeconds + Mathf.CeilToInt(captureSeconds + CryTail) + 2);
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
        return Match.Equip(player, new DuelMatch.Card(id, stats.weaponClass, stats.baseDamage, stats.blockPassThrough));
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
        // The button's own click is the "3"
        lastCountdownNumber = countdownSeconds;
        notice = null;
    }

    private void UpdateCapture()
    {
        if (Match.Phase != DuelPhase.Record) { CancelCapture(); return; }
        if (Time.timeScale == 0f) { CancelCapture(); return; }
        float now = Time.unscaledTime;
        if (capture == Capture.Countdown && now < captureEnd)
        {
            // A click for each number of the 3-2-1
            int number = Mathf.CeilToInt(captureEnd - now);
            if (number != lastCountdownNumber) GameAudio.Play("click");
            lastCountdownNumber = number;
        }
        else if (capture == Capture.Countdown && now >= captureEnd)
        {
            if (!PenInSquare())
            {
                CancelCapture();
                notice = L("The pen was not in the square - try again.", "A caneta não estava no quadrado - tenta outra vez.");
                return;
            }
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

    // Small line under the attack prompt while drawing
    private string CryFeedback()
    {
        if (!warCry.Ready) return L("No microphone - normal damage", "Sem microfone - dano normal");
        return L($"Shout for more damage! x{WarCryRules.Multiplier(warCry.PeakPower):F2}",
                 $"Grita para mais dano! x{WarCryRules.Multiplier(warCry.PeakPower):F2}");
    }

    // ---------- Fight ----------

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
        var exchanges = Match.Fight();
        yield return clash.Play(exchanges,
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
                    GameAudio.Play("ko");
                    StartCoroutine(PlayLater("ko_fall", 0.4f));
                    ShowBanner("K.O.!", KnockOutColor);
                    Flash(new Color(0.95f, 0.1f, 0.05f, 0.45f));
                }
                // Green when blocked, red when hit
                else Flash(exchange.blocked ? new Color(0.15f, 0.9f, 0.3f, 0.18f) : new Color(0.95f, 0.15f, 0.15f, 0.2f));
            });
        Match.FinishClash();

        // A K.O. or the last round: straight to the winner, no result screen
        bool knockedOut = Match.Health(0) == 0 || Match.Health(1) == 0;
        if (knockedOut || Match.Round == 3)
        {
            Match.Continue();
            ClearCries();
            if (!knockedOut) ShowBanner(WinnerText(), WinnerColor);
            GameAudio.Play("victory");
        }
    }

    private static IEnumerator PlayLater(string sound, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        GameAudio.Play(sound);
    }

    private string WinnerText() => Match.Winner < 0 ? L("Draw!", "Empate!") :
        L($"{PlayerName(Match.Winner)} wins!", $"{PlayerName(Match.Winner)} venceu!");

    // Big centred text that pops in and stays: K.O.! or the winner
    private void ShowBanner(string text, Color color)
    {
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        banner.text = text;
        banner.color = color;
        bannerRoutine = StartCoroutine(PopBanner());
    }

    private IEnumerator PopBanner()
    {
        const float duration = 0.25f;
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            banner.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, 1f - (1f - t / duration) * (1f - t / duration));
            yield return null;
        }
        banner.rectTransform.localScale = Vector3.one;
        bannerRoutine = null;
    }

    // Damage floating up from the defender, coloured by how much it hurt
    private void ShowHit(DuelMatch.Exchange exchange)
    {
        var defender = manager.GetAvatar(exchange.Defender);
        if (defender == null) return;
        string tag = "";
        Color tagColor = Color.white;
        if (exchange.defenderHealth == 0) { tag = "K.O.!"; tagColor = new Color(1f, 0.25f, 0.2f); }
        else if (exchange.blocked) { tag = L("BLOCKED", "BLOQUEADO"); tagColor = new Color(0.55f, 0.85f, 1f); }
        else if (exchange.classMultiplier > 1) { tag = L("CRITICAL!", "CRÍTICO!"); tagColor = new Color(1f, 0.82f, 0.2f); }
        else if (exchange.classMultiplier < 1) { tag = L("Not very effective...", "Pouco efetivo..."); tagColor = new Color(0.75f, 0.75f, 0.75f); }
        FloatingCombatText.Spawn(hud.transform, defender.HitPoint, $"-{exchange.damage}", DamageColor(exchange.damage), tag, tagColor);
    }

    // Yellow for a scratch, orange, then red for a heavy blow
    private static Color DamageColor(int damage)
    {
        Color low = new Color(1f, 0.95f, 0.35f), mid = new Color(1f, 0.55f, 0.1f), high = new Color(1f, 0.15f, 0.1f);
        float t = Mathf.InverseLerp(10f, 60f, damage);
        return t < 0.5f ? Color.Lerp(low, mid, t * 2f) : Color.Lerp(mid, high, (t - 0.5f) * 2f);
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
        bannerRoutine = flashRoutine = null;
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
        legend.SetActive(phase == DuelPhase.Pick);
        bigText.text = "";
        roundText.text = $"{L("ROUND", "RONDA")} {Match.Round}/3";
        // A short headline and a small line under it
        string text = "", sub = "";

        switch (phase)
        {
            case DuelPhase.Pick:
                legendHint.text = L("On the cards: green = beats · red = loses to", "Nas cartas: verde = vence · vermelho = perde");
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
                    text = L($"{PlayerName(0)}: pick a weapon", $"{PlayerName(0)}: escolhe uma arma");
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
                fightText.text = L($"ROUND {Match.Round}\nFIGHT!", $"RONDA {Match.Round}\nLUTA!");
                break;
            case DuelPhase.Clash:
                break;
            case DuelPhase.Result:
                // Only between rounds: the last one goes straight to the winner (RunClash)
                SetButton(Right, L("Next round", "Próxima ronda"), Advance);
                break;
            case DuelPhase.GameOver:
                // After a K.O. the banner says K.O.! and the winner goes here; otherwise the banner names the winner
                if (Match.Health(0) == 0 || Match.Health(1) == 0) text = WinnerText();
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
                string seconds = $"{Mathf.CeilToInt(captureEnd - Time.unscaledTime)} s";
                sub = attack ? $"{CryFeedback()}  ·  {seconds}" : seconds;
                return L("Drag the pen up, down, left or right", "Arrasta a caneta para cima, baixo, esquerda ou direita");
            case Capture.Review:
                SetButton(Left, L("Redo", "Repetir"), DetectPen);
                SetButton(Right, L("Confirm", "Confirmar"), ConfirmGesture);
                sub = L("Redo it or confirm", "Repete ou confirma");
                if (attack && pendingCryPower > 0)
                    sub = L($"War cry x{WarCryRules.Multiplier(pendingCryPower):F2}  ·  ", $"Grito de guerra x{WarCryRules.Multiplier(pendingCryPower):F2}  ·  ") + sub;
                string slot = attack ? L("Attack", "Ataque") : L("Defense", "Defesa");
                return $"{slot}: {Arrow(pendingDirection)} {DirectionName(pendingDirection)}";
        }

        SetButton(Right, L("Detect pen", "Detetar caneta"), DetectPen);
        if (!ArenaMenu.IsComputer(other)) sub = L($"{PlayerName(other)}, don't look!", $"{PlayerName(other)}, não olhes!");
        // The pen changes hands once, after the first player's defense
        if (Match.Step == 2) sub = L($"Pass the pen to {PlayerName(me)}", $"Passa a caneta a {PlayerName(me)}") + (sub.Length > 0 ? "  ·  " + sub : "");
        return attack ? L($"{PlayerName(me)}: draw your attack", $"{PlayerName(me)}: desenha o ataque")
                      : L($"{PlayerName(me)}: draw your defense", $"{PlayerName(me)}: desenha a defesa");
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

        legend = BuildLegend();

        // Countdown and "DRAG!"
        bigText = Label(hud.transform, 64);
        Rect(bigText.rectTransform, new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.84f), Vector2.zero, Vector2.zero);
        bigText.fontStyle = FontStyle.Bold;
        Shadow(bigText);

        // K.O.! and the winner, in the middle of the screen
        banner = Label(hud.transform, 130);
        Rect(banner.rectTransform, new Vector2(0.05f, 0.4f), new Vector2(0.95f, 0.75f), Vector2.zero, Vector2.zero);
        banner.fontStyle = FontStyle.Bold;
        Shadow(banner);
        var thick = banner.gameObject.AddComponent<Outline>();
        thick.effectColor = new Color(0, 0, 0, 0.6f);
        thick.effectDistance = new Vector2(5, -5);

        fightButton = MakeButton(hud.transform, "Fight", new Vector2(0.3f, 0.42f), new Vector2(0.7f, 0.6f), Vector2.zero, Vector2.zero, out fightText, 54);
        fightButton.GetComponent<Image>().color = new Color(0.75f, 0.12f, 0.08f, 0.95f);
        fightText.fontStyle = FontStyle.Bold;
        fightButton.onClick.AddListener(Fight);

        // Bottom row: secondary button on the left, text in the middle, main button on the right
        var box = Box(hud.transform, "Turn Prompt", new Vector2(0.21f, 0), new Vector2(0.79f, 0), new Vector2(0, 10), new Vector2(0, 110));
        box.color = PromptColor;
        box.raycastTarget = false;
        promptBox = box.gameObject;
        prompt = Label(box.transform, 30);
        Rect(prompt.rectTransform, new Vector2(0, 0.42f), Vector2.one, new Vector2(10, 0), new Vector2(-10, -6));
        prompt.fontStyle = FontStyle.Bold;
        Shadow(prompt);
        subPrompt = Label(box.transform, 18);
        Rect(subPrompt.rectTransform, Vector2.zero, new Vector2(1, 0.42f), new Vector2(10, 6), new Vector2(-10, 0));
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

    // Pick phase: Light > Heavy > Defense > Light, and what the green/red icons on the cards mean
    private GameObject BuildLegend()
    {
        // Small, in the top-left corner under the round counter, clear of the characters
        var panel = Box(hud.transform, "Class Legend", new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -126), new Vector2(272, -50));
        panel.color = PromptColor;
        panel.raycastTarget = false;
        Sprite[] icons = { manager.lightIcon, manager.heavyIcon, manager.defenseIcon, manager.lightIcon };
        for (int i = 0; i < icons.Length; i++)
        {
            float x = -99 + i * 66;
            var icon = Box(panel.transform, "Class Icon", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(x - 15, -40), new Vector2(x + 15, -6));
            icon.sprite = icons[i];
            icon.preserveAspect = true;
            icon.color = icons[i] != null ? Color.white : Color.clear;
            icon.raycastTarget = false;
            if (i < icons.Length - 1)
            {
                var arrow = Label(panel.transform, 18);
                Rect(arrow.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(x + 15, -40), new Vector2(x + 51, -6));
                arrow.text = "►";
            }
        }
        legendHint = Label(panel.transform, 13);
        Rect(legendHint.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(6, 2), new Vector2(-6, 32));
        return panel.gameObject;
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
