using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CardWeaponManager))]
public class DuelController : MonoBehaviour
{
    public DuelMatch Match { get; private set; }
    public bool AcceptsGesture => Match != null && !manager.SetupInProgress && Time.unscaledTime >= inputReady &&
        (Match.Phase == DuelPhase.Defense || Match.Phase == DuelPhase.Attack) && PlayersVisible;
    private bool PlayersVisible => manager.GetAttackPlayer(0) != null && manager.GetAttackPlayer(1) != null;
    private CardWeaponManager manager;
    private ColorGestureTracker pen;
    private GameObject hud;
    private Text prompt, actionText;
    private readonly Text[] playerTexts = new Text[2];
    private readonly Image[] healthBars = new Image[2];
    private readonly Image[] playerPanels = new Image[2];
    private Button action;
    private float inputReady, resultReady;
    private Image screenFlashOverlay;
    private Coroutine flashRoutine;

    void Start()
    {
        Localization.OnLanguageChanged += RefreshHUD;
        manager = GetComponent<CardWeaponManager>();
        pen = GetComponent<ColorGestureTracker>();
        BuildHUD();
        hud.SetActive(false);
    }

    void OnDestroy()
    {
        Localization.OnLanguageChanged -= RefreshHUD;
    }

    void Update()
    {
        if (Time.timeScale == 0f) return;
        if (manager.SetupInProgress) { hud.SetActive(false); return; }
        if (Match == null) Restart();
        hud.SetActive(true);
        RefreshHUD();
    }

    public bool CanEquip(int player, string card) => Match != null && Match.CanEquip(player, card);
    public bool IsUsed(string card) => Match != null && Match.IsUsed(card);

    public bool Equip(int player, string id, WeaponStats stats)
    {
        if (stats == null || Match == null) return false;
        bool accepted = Match.Equip(player, new DuelMatch.Card(id, stats.weaponClass, stats.baseDamage, stats.blockPassThrough));
        if (accepted) ResetInput();
        return accepted;
    }

    public bool SubmitGesture(AttackDirection direction)
    {
        if (!AcceptsGesture) return false;
        bool attacking = Match.Phase == DuelPhase.Attack;
        int attacker = Match.Attacker, defender = Match.Defender;
        var avatar = manager.GetAttackPlayer(attacker);
        if (attacking && (avatar == null || !avatar.TryAttack(direction))) return false;
        if (!Match.Submit(Match.InputPlayer, direction)) return false;
        if (attacking)
        {
            StartCoroutine(React(defender, direction, Match.LastBlocked, Match.Health(defender) == 0));
            resultReady = Time.unscaledTime + 1.5f;
        }
        ResetInput();
        return true;
    }

    private IEnumerator React(int defender, AttackDirection direction, bool blocked, bool dead)
    {
        yield return new WaitForSecondsRealtime(0.35f);

        // Flash green if blocked, red if hit
        if (blocked)
        {
            TriggerScreenFlash(new Color(0.15f, 0.90f, 0.30f, 0.32f), 0.85f);
        }
        else
        {
            TriggerScreenFlash(new Color(0.95f, 0.15f, 0.15f, 0.35f), 0.85f);
        }

        var avatar = manager.GetAvatar(defender);
        if (avatar == null) yield break;
        if (dead) avatar.PlayDeath();
        else if (blocked) avatar.PlayDefense(direction);
    }

    public void TriggerScreenFlash(Color color, float duration = 0.85f)
    {
        if (screenFlashOverlay == null) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(AnimateScreenFlash(color, duration));
    }

    private IEnumerator AnimateScreenFlash(Color color, float duration)
    {
        float elapsed = 0f;
        float attackTime = 0.06f; // Rapid flash in
        float maxAlpha = color.a;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float currentAlpha;
            if (elapsed < attackTime)
            {
                currentAlpha = Mathf.Lerp(0f, maxAlpha, elapsed / attackTime);
            }
            else
            {
                float decayProgress = (elapsed - attackTime) / (duration - attackTime);
                currentAlpha = Mathf.Lerp(maxAlpha, 0f, decayProgress);
            }

            color.a = currentAlpha;
            screenFlashOverlay.color = color;
            yield return null;
        }

        screenFlashOverlay.color = Color.clear;
        flashRoutine = null;
    }

    private void ResetInput()
    {
        inputReady = Time.unscaledTime + 0.8f;
        if (pen != null) pen.ResetGesture();
    }

    private void Advance()
    {
        if (Match == null) return;
        if (Match.Phase == DuelPhase.GameOver) { Restart(); return; }
        if (Match.Phase == DuelPhase.Result && Time.unscaledTime < resultReady) return;
        int round = Match.Round;
        Match.Continue();
        if (Match.Round != round || Match.Phase == DuelPhase.GameOver) manager.ClearRoundWeapons();
        ResetInput();
    }

    private void Restart()
    {
        StopAllCoroutines();
        if (screenFlashOverlay != null) screenFlashOverlay.color = Color.clear;
        flashRoutine = null;
        Match = new DuelMatch();
        manager.ClearRoundWeapons();
        for (int i = 0; i < 2; i++)
        {
            var avatar = manager.GetAvatar(i);
            if (avatar != null) avatar.ResetCombat();
        }
        ResetInput();
    }

    private string PlayerName(int i)
    {
        string name = PlayerProfiles.Instance != null ? PlayerProfiles.Instance.Get(i).name : "";
        string prefix = Localization.Get("Player", "Jogador");
        return $"{prefix} {(i == 0 ? "A" : "B")} ({i + 1})" + (string.IsNullOrWhiteSpace(name) ? "" : $" - {name}");
    }

    private void RefreshHUD()
    {
        bool gesturePhase = Match.Phase == DuelPhase.Defense || Match.Phase == DuelPhase.Attack;
        for (int i = 0; i < 2; i++)
        {
            string role = Match.Phase == DuelPhase.Pick 
                ? (Match.Equipped(i) == null 
                    ? Localization.Get("PICK A CARD", "ESCOLHA UMA CARTA") 
                    : Localization.Get("READY", "PRONTO"))
                : Match.Phase == DuelPhase.GameOver ? "" 
                : i == Match.Attacker 
                    ? Localization.Get("ATTACKER", "ATACANTE") 
                    : Localization.Get("DEFENDER", "DEFENSOR");

            string noWeaponStr = Localization.Get("No weapon", "Sem arma");
            string weapon = Match.Equipped(i)?.id.ToUpperInvariant() ?? noWeaponStr;
            string blocksLabel = Localization.Get("Blocks", "Defesas");
            playerTexts[i].text = $"{PlayerName(i)}  |  {role}\n{Match.Health(i)} / 100 HP  |  {blocksLabel}: {Match.Blocks(i)}  |  {weapon}";
            healthBars[i].rectTransform.anchorMax = new Vector2(Match.Health(i) / 100f, 1);
            healthBars[i].color = Match.Health(i) <= 25 ? Color.red : new Color(0.25f, 0.85f, 0.4f);
            playerPanels[i].color = gesturePhase && Match.InputPlayer == i ? new Color(0.35f, 0.25f, 0.06f, 0.95f) : new Color(0.04f, 0.06f, 0.09f, 0.9f);
        }
        string a = Match.Attacker == 0 
            ? Localization.Get("Player A", "Jogador A") 
            : Localization.Get("Player B", "Jogador B");
        string d = Match.Defender == 0 
            ? Localization.Get("Player A", "Jogador A") 
            : Localization.Get("Player B", "Jogador B");

        switch (Match.Phase)
        {
            case DuelPhase.Pick:
                prompt.text = Localization.Get(
                    "PICK: reveal one card each, then slide it next to your own player.\nPlayer A: regular cards. Player B: II cards. Used cards cannot be played again.",
                    "ESCOLHA: revele uma carta cada, depois deslize-a para junto do seu jogador.\nJogador A: cartas normais. Jogador B: cartas II. Cartas usadas não podem ser jogadas novamente."
                );
                break;
            case DuelPhase.Defense:
                prompt.text = Localization.Get(
                    $"{d}: choose your defense. {a}: don't look!\nMove the pen up, down, left or right.",
                    $"{d}: escolha a sua defesa. {a}: não olhe!\nMova a caneta para cima, baixo, esquerda ou direita."
                );
                break;
            case DuelPhase.DefenseChosen:
                prompt.text = Localization.Get(
                    $"Defense chosen.\nPass the pen to {a}, then press Ready to attack.",
                    $"Defesa escolhida.\nPasse a caneta para {a}, depois prima Pronto para atacar."
                );
                break;
            case DuelPhase.Attack:
                prompt.text = Localization.Get(
                    $"{a}: attack!\nMove the pen up, down, left or right.",
                    $"{a}: ataque!\nMova a caneta para cima, baixo, esquerda ou direita."
                );
                break;
            case DuelPhase.Result:
                string hitOrBlocked = Match.LastBlocked 
                    ? Localization.Get("Blocked!", "Bloqueado!") 
                    : Localization.Get("Hit!", "Acertou!");
                string takesDamage = Localization.Get(
                    $"{d} takes {Match.LastDamage} damage.",
                    $"{d} sofre {Match.LastDamage} de dano."
                );
                prompt.text = $"{hitOrBlocked} {takesDamage}\n{d}: {Match.Health(Match.Defender)} / 100 HP";
                break;
            case DuelPhase.GameOver:
                prompt.text = Match.Winner < 0 
                    ? Localization.Get("Game over - Draw!", "Fim de jogo - Empate!") 
                    : Localization.Get($"Game over - {PlayerName(Match.Winner)} wins!", $"Fim de jogo - {PlayerName(Match.Winner)} venceu!");
                break;
        }

        if (gesturePhase && !PlayersVisible)
        {
            prompt.text += "\n" + Localization.Get(
                "Keep both PLAYER cards visible to continue.",
                "Mantenha ambas as cartas de JOGADOR visíveis para continuar."
            );
        }

        string roundLabel = Localization.Get("ROUND", "RONDA");
        prompt.text = $"{roundLabel} {Match.Round} / 3\n" + prompt.text;

        action.gameObject.SetActive(Match.Phase == DuelPhase.DefenseChosen || Match.Phase == DuelPhase.Result || Match.Phase == DuelPhase.GameOver);
        action.interactable = Match.Phase != DuelPhase.Result || Time.unscaledTime >= resultReady;

        actionText.text = Match.Phase == DuelPhase.GameOver 
            ? Localization.Get("Play again", "Jogar novamente") 
            : Match.Phase == DuelPhase.DefenseChosen 
                ? Localization.Get("Ready to attack", "Pronto para atacar") 
                : Localization.Get("Continue", "Continuar");
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
        var bottom = Box(hud.transform, "Turn Prompt", Vector2.zero, new Vector2(1, 0), new Vector2(12, 12), new Vector2(-12, 210));
        prompt = Label(bottom.transform, 25);
        Rect(prompt.rectTransform, Vector2.zero, Vector2.one, new Vector2(16, 65), new Vector2(-16, -10));
        var buttonImage = Box(bottom.transform, "Continue", new Vector2(0.3f, 0), new Vector2(0.7f, 0), new Vector2(0, 10), new Vector2(0, 55));
        buttonImage.color = new Color(0.5f, 0.36f, 0.06f);
        action = buttonImage.gameObject.AddComponent<Button>();
        action.onClick.AddListener(Advance);
        actionText = Label(action.transform, 24);
        Rect(actionText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // Fullscreen flash overlay for combat feedback (red on hit, green on block)
        var flashObj = new GameObject("Screen Flash Overlay", typeof(RectTransform), typeof(Image));
        flashObj.transform.SetParent(hud.transform, false);
        var flashRect = flashObj.GetComponent<RectTransform>();
        Rect(flashRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        screenFlashOverlay = flashObj.GetComponent<Image>();
        screenFlashOverlay.color = Color.clear;
        screenFlashOverlay.raycastTarget = false;
    }

    private static Image Box(Transform parent, string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        Rect((RectTransform)obj.transform, min, max, offsetMin, offsetMax);
        var image = obj.GetComponent<Image>();
        image.color = new Color(0.04f, 0.06f, 0.09f, 0.9f);
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
