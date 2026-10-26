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
    private RedTipAttackTracker pen;
    private GameObject hud;
    private Text prompt, actionText;
    private readonly Text[] playerTexts = new Text[2];
    private readonly Image[] healthBars = new Image[2];
    private readonly Image[] playerPanels = new Image[2];
    private Button action;
    private float inputReady, resultReady;

    void Start()
    {
        manager = GetComponent<CardWeaponManager>();
        pen = GetComponent<RedTipAttackTracker>();
        BuildHUD();
        hud.SetActive(false);
    }

    void Update()
    {
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
        var avatar = manager.GetAvatar(defender);
        if (avatar == null) yield break;
        if (dead) avatar.PlayDeath();
        else if (blocked) avatar.PlayDefense(direction);
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
        return $"Player {(i == 0 ? "A" : "B")} ({i + 1})" + (string.IsNullOrWhiteSpace(name) ? "" : $" - {name}");
    }

    private void RefreshHUD()
    {
        bool gesturePhase = Match.Phase == DuelPhase.Defense || Match.Phase == DuelPhase.Attack;
        for (int i = 0; i < 2; i++)
        {
            string role = Match.Phase == DuelPhase.Pick ? (Match.Equipped(i) == null ? "PICK A CARD" : "READY") :
                Match.Phase == DuelPhase.GameOver ? "" : i == Match.Attacker ? "ATTACKER" : "DEFENDER";
            string weapon = Match.Equipped(i)?.id.ToUpperInvariant() ?? "No weapon";
            playerTexts[i].text = $"{PlayerName(i)}  |  {role}\n{Match.Health(i)} / 100 HP  |  Blocks: {Match.Blocks(i)}  |  {weapon}";
            healthBars[i].rectTransform.anchorMax = new Vector2(Match.Health(i) / 100f, 1);
            healthBars[i].color = Match.Health(i) <= 25 ? Color.red : new Color(0.25f, 0.85f, 0.4f);
            playerPanels[i].color = gesturePhase && Match.InputPlayer == i ? new Color(0.35f, 0.25f, 0.06f, 0.95f) : new Color(0.04f, 0.06f, 0.09f, 0.9f);
        }
        string a = Match.Attacker == 0 ? "Player A" : "Player B";
        string d = Match.Defender == 0 ? "Player A" : "Player B";
        switch (Match.Phase)
        {
            case DuelPhase.Pick:
                prompt.text = "PICK: reveal one card each, then slide it next to your own player.\nPlayer A: regular cards. Player B: II cards. Used cards cannot be played again.";
                break;
            case DuelPhase.Defense:
                prompt.text = $"{d}: choose your defense. {a}: don't look!\nMove the pen up, down, left or right.";
                break;
            case DuelPhase.DefenseChosen:
                prompt.text = $"Defense chosen.\nPass the pen to {a}, then press Ready to attack.";
                break;
            case DuelPhase.Attack:
                prompt.text = $"{a}: attack!\nMove the pen up, down, left or right.";
                break;
            case DuelPhase.Result:
                prompt.text = $"{(Match.LastBlocked ? "Blocked!" : "Hit!")} {d} takes {Match.LastDamage} damage.\n{d}: {Match.Health(Match.Defender)} / 100 HP";
                break;
            case DuelPhase.GameOver:
                prompt.text = Match.Winner < 0 ? "Game over - Draw!" : $"Game over - {PlayerName(Match.Winner)} wins!";
                break;
        }
        if (gesturePhase && !PlayersVisible) prompt.text += "\nKeep both PLAYER cards visible to continue.";
        prompt.text = $"ROUND {Match.Round} / 3\n" + prompt.text;
        action.gameObject.SetActive(Match.Phase == DuelPhase.DefenseChosen || Match.Phase == DuelPhase.Result || Match.Phase == DuelPhase.GameOver);
        action.interactable = Match.Phase != DuelPhase.Result || Time.unscaledTime >= resultReady;
        actionText.text = Match.Phase == DuelPhase.GameOver ? "Play again" : Match.Phase == DuelPhase.DefenseChosen ? "Ready to attack" : "Continue";
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
