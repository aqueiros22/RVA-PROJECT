using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Runs the duel on screen: pick -> each player secretly records attack + defense with the pen
// (Draw -> 3-2-1 -> swipe -> Redo / Confirm) -> FIGHT button -> clash -> result.
[RequireComponent(typeof(CardWeaponManager))]
public class DuelController : MonoBehaviour
{
    [Tooltip("Seconds of 3-2-1 before the pen is armed.")]
    public int countdownSeconds = 3;
    [Tooltip("Seconds the pen stays armed waiting for a swipe.")]
    public float captureSeconds = 3f;

    private enum Capture { None, Countdown, Armed, Review }

    public DuelMatch Match { get; private set; }
    private CardWeaponManager manager;
    private ColorGestureTracker pen;
    private ClashDirector clash;
    private readonly int[] shownHealth = { 100, 100 };

    private Capture capture;
    private GestureSlot captureSlot;
    private AttackDirection pendingDirection;
    private float captureEnd;
    private string notice;
    private string announcement;

    private GameObject hud;
    private Text prompt, bigText, fightText;
    private readonly Text[] playerTexts = new Text[2];
    private readonly Image[] healthBars = new Image[2];
    private readonly Image[] playerPanels = new Image[2];
    private readonly Button[] buttons = new Button[3];
    private readonly Text[] buttonTexts = new Text[3];
    private readonly System.Action[] buttonActions = new System.Action[3];
    private Button fightButton, settingsButton;
    private Text settingsText;

    private static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.09f, 0.9f);
    private static readonly Color ActivePanelColor = new Color(0.35f, 0.25f, 0.06f, 0.95f);
    private static readonly Color ButtonColor = new Color(0.5f, 0.36f, 0.06f);

    private static string L(string english, string portuguese) => Localization.Get(english, portuguese);

    void Start()
    {
        manager = GetComponent<CardWeaponManager>();
        pen = GetComponent<ColorGestureTracker>();
        if (pen != null) pen.GestureCaptured += OnGesture;
        clash = GetComponent<ClashDirector>();
        if (clash == null) clash = gameObject.AddComponent<ClashDirector>();
        BuildHUD();
        hud.SetActive(false);
    }

    void OnDestroy()
    {
        if (pen != null) pen.GestureCaptured -= OnGesture;
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
        UpdateCapture();
        RefreshHUD();
    }

    public bool CanEquip(int player, string card) => Match != null && Match.CanEquip(player, card);
    public bool IsUsed(string card) => Match != null && Match.IsUsed(card);
    // Health as the players see it: it only drops when the blade connects in the clash
    public int ShownHealth(int player) => shownHealth[player];

    public bool Equip(int player, string id, WeaponStats stats)
    {
        if (stats == null || Match == null) return false;
        return Match.Equip(player, new DuelMatch.Card(id, stats.weaponClass, stats.baseDamage, stats.blockPassThrough));
    }

    // Editor keyboard: an arrow fills the gesture being drawn, like a pen swipe
    public void KeyboardGesture(AttackDirection direction)
    {
        if (capture != Capture.Countdown && capture != Capture.Armed) return;
        if (pen != null) pen.Disarm();
        capture = Capture.Armed;
        OnGesture(direction);
    }

    // ---------- Recording ----------

    private void BeginDraw(GestureSlot slot)
    {
        if (Match.Phase != DuelPhase.Record) return;
        captureSlot = slot;
        capture = Capture.Countdown;
        captureEnd = Time.unscaledTime + countdownSeconds;
        notice = null;
    }

    private void UpdateCapture()
    {
        if (Match.Phase != DuelPhase.Record) { CancelCapture(); return; }
        float now = Time.unscaledTime;
        if (capture == Capture.Countdown && now >= captureEnd)
        {
            capture = Capture.Armed;
            captureEnd = now + captureSeconds;
            if (pen != null) pen.Arm(captureSeconds);
        }
        else if (capture == Capture.Armed && now >= captureEnd)
        {
            CancelCapture();
            notice = L("No gesture detected - try again.", "Nenhum gesto detetado - tenta outra vez.");
        }
    }

    private void OnGesture(AttackDirection direction)
    {
        if (capture != Capture.Armed) return;
        pendingDirection = direction;
        capture = Capture.Review;
    }

    private void ConfirmGesture()
    {
        if (capture != Capture.Review) return;
        Match.Record(Match.Recorder, captureSlot, pendingDirection);
        capture = Capture.None;
    }

    private void CancelCapture()
    {
        if (capture == Capture.None) return;
        capture = Capture.None;
        if (pen != null) pen.Disarm();
    }

    private void FinishRecording()
    {
        if (!Match.FinishRecording()) return;
        CancelCapture();
        notice = null;
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
            exchange => announcement = L($"{PlayerName(exchange.attacker)} attacks!", $"{PlayerName(exchange.attacker)} ataca!"),
            exchange =>
            {
                shownHealth[exchange.Defender] = exchange.defenderHealth;
                announcement = exchange.defenderHealth == 0 ? "K.O.!" :
                    exchange.blocked ? L($"BLOCKED!  -{exchange.damage}", $"BLOQUEADO!  -{exchange.damage}") :
                    L($"HIT!  -{exchange.damage}", $"ACERTOU!  -{exchange.damage}");
            });
        announcement = null;
        Match.FinishClash();
    }

    private void Advance()
    {
        if (Match.Phase == DuelPhase.GameOver) { Restart(); return; }
        if (Match.Phase != DuelPhase.Result) return;
        Match.Continue();
        manager.ClearRoundWeapons();
        for (int i = 0; i < 2; i++) shownHealth[i] = Match.Health(i);
    }

    private void Restart()
    {
        StopAllCoroutines();
        Match = new DuelMatch();
        shownHealth[0] = shownHealth[1] = 100;
        CancelCapture();
        notice = announcement = null;
        manager.ClearRoundWeapons();
        for (int i = 0; i < 2; i++)
        {
            var avatar = manager.GetAvatar(i);
            if (avatar != null) avatar.ResetCombat();
        }
    }

    private void ToggleSettings()
    {
        if (pen != null) pen.ShowSettings = !pen.ShowSettings;
    }

    // ---------- HUD ----------

    private string PlayerName(int i)
    {
        string name = PlayerProfiles.Instance != null ? PlayerProfiles.Instance.Get(i).name : "";
        string label = L($"Player {(i == 0 ? "A" : "B")}", $"Jogador {(i == 0 ? "A" : "B")}");
        return string.IsNullOrWhiteSpace(name) ? label : $"{label} ({name})";
    }

    private string SlotName(GestureSlot slot) => slot == GestureSlot.Attack ? L("attack", "ataque") : L("defense", "defesa");

    private void RefreshHUD()
    {
        DuelPhase phase = Match.Phase;
        for (int i = 0; i < 2; i++)
        {
            string role =
                phase == DuelPhase.Pick ? (Match.Equipped(i) == null ? L("PICK A CARD", "ESCOLHE UMA CARTA") : L("READY", "PRONTO")) :
                phase == DuelPhase.Record ? (i == Match.Recorder ? L("RECORDING", "A GRAVAR") : L("LOOK AWAY", "NÃO OLHES")) :
                phase == DuelPhase.Fight || phase == DuelPhase.Clash ? L("FIGHT", "LUTA") : "";
            string weapon = Match.Equipped(i)?.id.ToUpperInvariant() ?? L("No weapon", "Sem arma");
            playerTexts[i].text = $"{PlayerName(i)}  |  {role}\n{shownHealth[i]} / 100 HP  |  {L("Blocks", "Bloqueios")}: {Match.Blocks(i)}  |  {weapon}";
            healthBars[i].rectTransform.anchorMax = new Vector2(shownHealth[i] / 100f, 1);
            healthBars[i].color = shownHealth[i] <= 25 ? Color.red : new Color(0.25f, 0.85f, 0.4f);
            playerPanels[i].color = phase == DuelPhase.Record && Match.Recorder == i ? ActivePanelColor : PanelColor;
        }

        for (int i = 0; i < buttons.Length; i++) SetButton(i, null, null);
        fightButton.gameObject.SetActive(false);
        bigText.text = announcement ?? "";
        string text = "";

        switch (phase)
        {
            case DuelPhase.Pick:
                text = L("PICK: reveal one card each, then slide it next to your own player.\nPlayer A: regular cards. Player B: II cards. Used cards cannot be played again.",
                         "ESCOLHA: revelem uma carta cada um e encostem-na ao vosso jogador.\nJogador A: cartas normais. Jogador B: cartas II. Cartas usadas não voltam a jogo.");
                break;
            case DuelPhase.Record:
                text = RecordPrompt();
                break;
            case DuelPhase.Fight:
                text = L("Both players are ready. Put the pen away and press FIGHT!",
                         "Os dois jogadores estão prontos. Afastem a caneta e carreguem em LUTA!");
                fightButton.gameObject.SetActive(true);
                fightText.text = L($"ROUND {Match.Round}\nFIGHT!", $"RONDA {Match.Round}\nLUTA!");
                break;
            case DuelPhase.Clash:
                text = L("Fight!", "Luta!");
                break;
            case DuelPhase.Result:
                text = ResultText();
                bool last = Match.Health(0) == 0 || Match.Health(1) == 0 || Match.Round == 3;
                SetButton(1, last ? L("See the winner", "Ver o vencedor") : L("Next round", "Próxima ronda"), Advance);
                break;
            case DuelPhase.GameOver:
                text = Match.Winner < 0 ? L("Game over - Draw!", "Fim do jogo - Empate!") :
                    L($"Game over - {PlayerName(Match.Winner)} wins!", $"Fim do jogo - {PlayerName(Match.Winner)} ganha!");
                bigText.text = Match.Winner < 0 ? L("DRAW", "EMPATE") : L($"{PlayerName(Match.Winner)} WINS", $"{PlayerName(Match.Winner)} GANHA");
                SetButton(1, L("Play again", "Jogar outra vez"), Restart);
                break;
        }
        if (!string.IsNullOrEmpty(notice)) text += "\n" + notice;
        prompt.text = $"{L("ROUND", "RONDA")} {Match.Round} / 3\n{text}";
        settingsText.text = L("Settings", "Config");
    }

    private string RecordPrompt()
    {
        int me = Match.Recorder, other = 1 - me;
        string slot = SlotName(captureSlot);
        switch (capture)
        {
            case Capture.Countdown:
                bigText.text = Mathf.CeilToInt(captureEnd - Time.unscaledTime).ToString();
                SetButton(1, L("Cancel", "Cancelar"), CancelCapture);
                return L($"{PlayerName(me)}: get the pen ready to draw your {slot}...", $"{PlayerName(me)}: prepara a caneta para desenhar a tua {slot}...");
            case Capture.Armed:
                bigText.text = L("DRAW NOW!", "DESENHA AGORA!");
                SetButton(1, L("Cancel", "Cancelar"), CancelCapture);
                return L($"Swipe the pen up, down, left or right ({Mathf.CeilToInt(captureEnd - Time.unscaledTime)} s).",
                         $"Faz um movimento com a caneta para cima, baixo, esquerda ou direita ({Mathf.CeilToInt(captureEnd - Time.unscaledTime)} s).");
            case Capture.Review:
                bigText.text = L("Recorded", "Gravado");
                SetButton(0, L("Redo", "Repetir"), () => BeginDraw(captureSlot));
                SetButton(2, L("Confirm", "Confirmar"), ConfirmGesture);
                // The direction is never shown: the other player could see it
                return L($"Your {slot} was recorded. Redo it or confirm.", $"A tua {slot} foi gravada. Repete ou confirma.");
        }

        bool attackDone = Match.HasGesture(me, GestureSlot.Attack);
        bool defenseDone = Match.HasGesture(me, GestureSlot.Defense);
        SetButton(0, attackDone ? L("Redo attack", "Repetir ataque") : L("Draw attack", "Desenhar ataque"), () => BeginDraw(GestureSlot.Attack));
        SetButton(1, defenseDone ? L("Redo defense", "Repetir defesa") : L("Draw defense", "Desenhar defesa"), () => BeginDraw(GestureSlot.Defense));
        if (attackDone && defenseDone)
            SetButton(2, me == Match.FirstAttacker ? L($"Done - pass to {PlayerName(other)}", $"Feito - passa a {PlayerName(other)}") : L("Done - ready to fight", "Feito - prontos para lutar"), FinishRecording);
        string done = L("recorded", "gravado"), missing = L("missing", "em falta");
        return L($"{PlayerName(me)}: record your attack and your defense. {PlayerName(other)}, don't look!",
                 $"{PlayerName(me)}: grava o teu ataque e a tua defesa. {PlayerName(other)}, não olhes!") +
               $"\n{L("Attack", "Ataque")}: {(attackDone ? done : missing)}   |   {L("Defense", "Defesa")}: {(defenseDone ? done : missing)}";
    }

    private string ResultText()
    {
        var lines = new System.Text.StringBuilder();
        foreach (var exchange in Match.Exchanges)
        {
            string outcome = exchange.blocked ? L("blocked", "bloqueado") : L("hit", "acertou");
            lines.AppendLine(L($"{PlayerName(exchange.attacker)} -> {PlayerName(exchange.Defender)}: {outcome}, {exchange.damage} damage",
                               $"{PlayerName(exchange.attacker)} -> {PlayerName(exchange.Defender)}: {outcome}, {exchange.damage} de dano"));
        }
        return lines.ToString().TrimEnd();
    }

    private void SetButton(int slot, string label, System.Action action)
    {
        buttonActions[slot] = action;
        buttons[slot].gameObject.SetActive(action != null);
        if (action != null) buttonTexts[slot].text = label;
    }

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
        for (int i = 0; i < 2; i++)
        {
            var box = Box(hud.transform, "Player Status", new Vector2(i * 0.5f, 1), new Vector2((i + 1) * 0.5f, 1), new Vector2(12, -112), new Vector2(-12, -12));
            playerPanels[i] = box;
            playerTexts[i] = Label(box.transform, 22);
            Rect(playerTexts[i].rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 30), new Vector2(-12, -5));
            var track = Box(box.transform, "Health Track", Vector2.zero, new Vector2(1, 0), new Vector2(12, 10), new Vector2(-12, 24));
            track.color = new Color(0.3f, 0.12f, 0.12f);
            healthBars[i] = Box(track.transform, "Health", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        settingsButton = MakeButton(hud.transform, "Settings", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-170, -170), new Vector2(-12, -120), out settingsText, 20);
        settingsButton.GetComponent<Image>().color = PanelColor;
        settingsButton.onClick.AddListener(ToggleSettings);

        // Countdown, "DRAW NOW!", hit results and the winner, in the middle of the screen
        bigText = Label(hud.transform, 64);
        Rect(bigText.rectTransform, new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.8f), Vector2.zero, Vector2.zero);
        bigText.fontStyle = FontStyle.Bold;
        var outline = bigText.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0.85f);
        outline.effectDistance = new Vector2(3, -3);

        fightButton = MakeButton(hud.transform, "Fight", new Vector2(0.3f, 0.42f), new Vector2(0.7f, 0.6f), Vector2.zero, Vector2.zero, out fightText, 54);
        fightButton.GetComponent<Image>().color = new Color(0.75f, 0.12f, 0.08f, 0.95f);
        fightText.fontStyle = FontStyle.Bold;
        fightButton.onClick.AddListener(Fight);

        var bottom = Box(hud.transform, "Turn Prompt", Vector2.zero, new Vector2(1, 0), new Vector2(12, 12), new Vector2(-12, 230));
        prompt = Label(bottom.transform, 24);
        Rect(prompt.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 70), new Vector2(-16, -8));
        for (int i = 0; i < buttons.Length; i++)
        {
            int slot = i;
            float x0 = 0.02f + i * 0.33f;
            buttons[i] = MakeButton(bottom.transform, "Action", new Vector2(x0, 0), new Vector2(x0 + 0.3f, 0), new Vector2(0, 10), new Vector2(0, 60), out buttonTexts[i], 24);
            buttons[i].onClick.AddListener(() => buttonActions[slot]?.Invoke());
        }
    }

    private static Button MakeButton(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, out Text label, int fontSize)
    {
        var image = Box(parent, name, min, max, offsetMin, offsetMax);
        image.color = ButtonColor;
        var button = image.gameObject.AddComponent<Button>();
        label = Label(image.transform, fontSize);
        Rect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(6, 2), new Vector2(-6, -2));
        return button;
    }

    private static Image Box(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        Rect((RectTransform)obj.transform, min, max, offsetMin, offsetMax);
        var image = obj.GetComponent<Image>();
        image.color = PanelColor;
        return image;
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
