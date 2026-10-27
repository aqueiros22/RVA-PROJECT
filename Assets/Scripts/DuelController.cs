using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Runs the duel on screen: pick -> the players take turns recording one secret gesture each
// (A attack, B defense, B attack, A defense). A gesture: Detect pen -> 3-2-1 holding the pen in the
// square -> drag it up, down, left or right -> the stroke is shown -> Redo / Confirm.
// Then the FIGHT button plays the clash and the result.
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
    private CardWeaponManager manager;
    private ColorGestureTracker pen;
    private ClashDirector clash;
    private readonly int[] shownHealth = { 100, 100 };

    private Capture capture;
    private AttackDirection pendingDirection;
    private Vector2 origin;
    private float captureEnd, reviewStart, lastPenSeen;
    private string notice;
    private string announcement;

    private GameObject hud;
    private Text prompt, bigText, fightText, roundText, legendHint;
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
    private static readonly Color PromptColor = new Color(0f, 0f, 0f, 0.45f);
    private static readonly Color ButtonColor = new Color(0.5f, 0.36f, 0.06f, 0.92f);
    private static readonly Color PrimaryColor = new Color(0.12f, 0.55f, 0.25f, 0.95f);
    private static readonly Color TraceColor = new Color(1f, 0.85f, 0.2f);

    private static string L(string english, string portuguese) => Localization.Get(english, portuguese);

    void Start()
    {
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
            if (!PenInSquare())
            {
                capture = Capture.None;
                notice = L("The pen was not in the square - try again.", "A caneta não estava no quadrado - tenta outra vez.");
                return;
            }
            origin = pen.Tip;
            lastPenSeen = now;
            capture = Capture.Armed;
            captureEnd = now + captureSeconds;
        }
        else if (capture == Capture.Armed)
        {
            if (pen != null && pen.TipVisible)
            {
                lastPenSeen = now;
                var direction = PenStroke.Classify(origin.x, origin.y, pen.Tip.x, pen.Tip.y,
                    (float)Screen.width / Screen.height, strokeDistance, strokeDominance);
                if (direction.HasValue) { Review(direction.Value); return; }
            }
            if (now - lastPenSeen > lostPenTimeout)
            {
                capture = Capture.None;
                notice = L("Lost the pen - try again.", "A caneta perdeu-se - tenta outra vez.");
            }
            else if (now >= captureEnd)
            {
                capture = Capture.None;
                notice = L("No gesture detected - try again.", "Nenhum gesto detetado - tenta outra vez.");
            }
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
        pendingDirection = direction;
        capture = Capture.Review;
        reviewStart = Time.unscaledTime;
        notice = null;
    }

    private void ConfirmGesture()
    {
        if (capture != Capture.Review) return;
        Match.Record(Match.CurrentPlayer, Match.CurrentSlot, pendingDirection);
        Match.ConfirmStep();
        capture = Capture.None;
    }

    private void CancelCapture()
    {
        capture = Capture.None;
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
                // Green when blocked, red when hit
                Flash(exchange.blocked ? new Color(0.15f, 0.9f, 0.3f, 0.32f) : new Color(0.95f, 0.15f, 0.15f, 0.35f));
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
        if (flash != null) flash.color = Color.clear;
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
        string label = L($"Player {(i == 0 ? "A" : "B")}", $"Jogador {(i == 0 ? "A" : "B")}");
        return string.IsNullOrWhiteSpace(name) ? label : $"{label} ({name})";
    }

    private string SlotName(GestureSlot slot) => slot == GestureSlot.Attack ? L("attack", "ataque") : L("defense", "defesa");

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
        bigText.text = announcement ?? "";
        roundText.text = $"{L("ROUND", "RONDA")} {Match.Round}/3";
        string text = "";

        switch (phase)
        {
            case DuelPhase.Pick:
                legendHint.text = L("Each class beats the next one.\nPoint the camera at your cards: green = beats, red = loses to.",
                                    "Cada classe vence a seguinte.\nAponta a câmara às tuas cartas: verde = vence, vermelho = perde contra.");
                text = L("Reveal one card each and slide it next to your player.\nPlayer A: regular cards. Player B: II cards.",
                         "Revelem uma carta cada um e encostem-na ao vosso jogador.\nJogador A: cartas normais. Jogador B: cartas II.");
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
                SetButton(Right, last ? L("See the winner", "Ver o vencedor") : L("Next round", "Próxima ronda"), Advance);
                break;
            case DuelPhase.GameOver:
                text = Match.Winner < 0 ? L("Game over - Draw!", "Fim do jogo - Empate!") :
                    L($"Game over - {PlayerName(Match.Winner)} wins!", $"Fim do jogo - {PlayerName(Match.Winner)} ganha!");
                bigText.text = Match.Winner < 0 ? L("DRAW", "EMPATE") : L($"{PlayerName(Match.Winner)} WINS", $"{PlayerName(Match.Winner)} GANHA");
                SetButton(Right, L("Play again", "Jogar outra vez"), Restart);
                break;
        }
        if (!string.IsNullOrEmpty(notice)) text += "\n" + notice;
        prompt.text = text;
        promptBox.SetActive(text.Length > 0);
    }

    private string RecordPrompt()
    {
        int me = Match.CurrentPlayer, other = 1 - me;
        string slot = SlotName(Match.CurrentSlot);
        switch (capture)
        {
            case Capture.Countdown:
                bigText.text = Mathf.CeilToInt(captureEnd - Time.unscaledTime).ToString();
                SetButton(Left, L("Cancel", "Cancelar"), CancelCapture);
                return PenInSquare()
                    ? L("Pen found - keep it in the square...", "Caneta encontrada - mantém-na no quadrado...")
                    : L("Hold the pen tip inside the square.", "Põe a ponta da caneta dentro do quadrado.");
            case Capture.Armed:
                bigText.text = L("DRAG!", "ARRASTA!");
                SetButton(Left, L("Cancel", "Cancelar"), CancelCapture);
                return L($"Drag the pen up, down, left or right ({Mathf.CeilToInt(captureEnd - Time.unscaledTime)} s).",
                         $"Arrasta a caneta para cima, baixo, esquerda ou direita ({Mathf.CeilToInt(captureEnd - Time.unscaledTime)} s).");
            case Capture.Review:
                SetButton(Left, L("Redo", "Repetir"), DetectPen);
                SetButton(Right, L("Confirm", "Confirmar"), ConfirmGesture);
                return L($"Your {slot}: {Arrow(pendingDirection)} {DirectionName(pendingDirection)}. Redo it or confirm.",
                         $"A tua {slot}: {Arrow(pendingDirection)} {DirectionName(pendingDirection)}. Repete ou confirma.");
        }

        SetButton(Right, L("Detect pen", "Detetar caneta"), DetectPen);
        string handover = Match.Step > 0 ? L($"Pass the pen to {PlayerName(me)}. ", $"Passa a caneta a {PlayerName(me)}. ") : "";
        return handover + L($"{PlayerName(me)}: record your {slot}. {PlayerName(other)}, don't look!",
                            $"{PlayerName(me)}: grava a tua {slot}. {PlayerName(other)}, não olhes!");
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

        // Countdown, "DRAG!", hit results and the winner
        bigText = Label(hud.transform, 64);
        Rect(bigText.rectTransform, new Vector2(0.05f, 0.66f), new Vector2(0.95f, 0.84f), Vector2.zero, Vector2.zero);
        bigText.fontStyle = FontStyle.Bold;
        Shadow(bigText);

        fightButton = MakeButton(hud.transform, "Fight", new Vector2(0.3f, 0.42f), new Vector2(0.7f, 0.6f), Vector2.zero, Vector2.zero, out fightText, 54);
        fightButton.GetComponent<Image>().color = new Color(0.75f, 0.12f, 0.08f, 0.95f);
        fightText.fontStyle = FontStyle.Bold;
        fightButton.onClick.AddListener(Fight);

        // Bottom row: secondary button on the left, text in the middle, main button on the right
        var box = Box(hud.transform, "Turn Prompt", new Vector2(0.21f, 0), new Vector2(0.79f, 0), new Vector2(0, 10), new Vector2(0, 110));
        box.color = PromptColor;
        box.raycastTarget = false;
        promptBox = box.gameObject;
        prompt = Label(box.transform, 22);
        Rect(prompt.rectTransform, Vector2.zero, Vector2.one, new Vector2(10, 4), new Vector2(-10, -4));
        Shadow(prompt);

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
        var panel = Box(hud.transform, "Class Legend", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-260, -128), new Vector2(260, -10));
        panel.color = PromptColor;
        panel.raycastTarget = false;
        Sprite[] icons = { manager.lightIcon, manager.heavyIcon, manager.defenseIcon, manager.lightIcon };
        for (int i = 0; i < icons.Length; i++)
        {
            float x = -165 + i * 110;
            var icon = Box(panel.transform, "Class Icon", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(x - 26, -62), new Vector2(x + 26, -10));
            icon.sprite = icons[i];
            icon.preserveAspect = true;
            icon.color = icons[i] != null ? Color.white : Color.clear;
            icon.raycastTarget = false;
            if (i < icons.Length - 1)
            {
                var arrow = Label(panel.transform, 30);
                Rect(arrow.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(x + 30, -62), new Vector2(x + 80, -10));
                arrow.text = "►";
            }
        }
        legendHint = Label(panel.transform, 18);
        Rect(legendHint.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(8, 4), new Vector2(-8, 54));
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
