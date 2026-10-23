using UnityEngine;
using Vuforia;

public enum AttackDirection { Up, Down, Left, Right }

// Screen-space colour tracking using the same Vuforia camera as the cards.
[RequireComponent(typeof(CardWeaponManager))]
public class RedTipAttackTracker : MonoBehaviour
{
    [Tooltip("Extra clockwise rotation of the camera image, for device calibration.")]
    [Range(0, 3)] public int cameraQuarterTurns;
    [Min(0)] public int playerIndex;
    public bool trackingEnabled = true;
    public bool showOverlay = true;
    [Header("Detection")]
    [Range(80, 320)] public int sampleWidth = 160;
    [Range(0f, 1f)] public float minimumSaturation = 0.6f;
    [Range(0f, 1f)] public float minimumBrightness = 0.25f;
    [Range(0.01f, 0.15f)] public float redHueTolerance = 0.055f;
    [Min(2)] public int minimumBlobPixels = 5;
    [Range(0.001f, 0.5f)] public float maximumBlobFraction = 0.08f;
    public bool flipCameraY;
    [Header("Gestures (fractions of the shorter screen side)")]
    [Range(0.02f, 0.5f)] public float gestureDistance = 0.12f;
    [Min(0.1f)] public float gestureWindow = 0.45f;
    [Min(0.1f)] public float attackCooldown = 0.65f;
    [Range(1f, 3f)] public float axisDominance = 1.3f;

    private CardWeaponManager manager;
    private PlayerAvatar target;
    private bool formatRegistered, hasTip, hasAnchor;
    private VuforiaBehaviour vuforia;
    private Vector2 tip, anchor;
    private float nextSample, anchorTime, nextAttack, lastSeen;
    private bool[] mask;
    private int[] queue;
    private int gridWidth, gridHeight;
    private ScreenOrientation orientation;
    private string status = "Equipa uma arma para comecar";
    private string lastAttack = "-";

    void Start()
    {
        manager = GetComponent<CardWeaponManager>();
        vuforia = VuforiaBehaviour.Instance;
        VuforiaApplication.Instance.OnVuforiaStarted += RegisterFormat;
        VuforiaApplication.Instance.OnVuforiaStopped += OnStopped;
        if (vuforia != null) vuforia.World.OnStateUpdated += OnFrame;
        RegisterFormat();
    }

    void RegisterFormat()
    {
        if (vuforia != null && VuforiaApplication.Instance.IsRunning)
            formatRegistered = vuforia.CameraDevice.SetFrameFormat(PixelFormat.RGB888, true);
    }

    void OnStopped()
    {
        formatRegistered = false;
        hasTip = hasAnchor = false;
    }

    void OnDestroy()
    {
        VuforiaApplication.Instance.OnVuforiaStarted -= RegisterFormat;
        VuforiaApplication.Instance.OnVuforiaStopped -= OnStopped;
        if (vuforia != null)
        {
            vuforia.World.OnStateUpdated -= OnFrame;
            if (formatRegistered && VuforiaApplication.Instance.IsRunning)
                vuforia.CameraDevice.SetFrameFormat(PixelFormat.RGB888, false);
        }
    }

    void Update()
    {
        if (hasTip && Time.unscaledTime - lastSeen > 0.2f) hasTip = hasAnchor = false;
    }

    void OnFrame()
    {
        if (!isActiveAndEnabled) return;
        if (orientation != Screen.orientation)
        {
            orientation = Screen.orientation;
            hasTip = hasAnchor = false;
        }
        var player = manager.GetAttackPlayer(playerIndex);
        if (target != player) { target = player; hasAnchor = false; }
        if (!trackingEnabled || target == null)
        {
            hasTip = hasAnchor = false;
            status = trackingEnabled ? "Equipa uma arma e mantem o jogador visivel" : "Tracking em pausa";
            return;
        }
        float now = Time.unscaledTime;
        if (now < nextSample) return;
        if (!formatRegistered) { status = "Vuforia: imagem RGB indisponivel"; return; }
        nextSample = now + 1f / 20f;
        var image = vuforia.CameraDevice.GetCameraImage(PixelFormat.RGB888);
        if (image == null || image.Pixels == null) return;
        hasTip = FindTip(image.Pixels, image.Width, image.Height, image.Stride, out tip);
        if (!hasTip) { hasAnchor = false; status = "Ponta vermelha nao encontrada"; return; }
        lastSeen = now;
        status = "Ponta detetada: move a caneta";
        if (now < nextAttack) { hasAnchor = false; return; }
        if (!hasAnchor || now - anchorTime > gestureWindow)
        {
            anchor = tip; anchorTime = now; hasAnchor = true;
            return;
        }
        Vector2 delta = tip - anchor;
        delta = Vector2.Scale(delta, new Vector2(Screen.width, Screen.height)) / Mathf.Min(Screen.width, Screen.height);
        float x = Mathf.Abs(delta.x), y = Mathf.Abs(delta.y);
        if (Mathf.Max(x, y) < gestureDistance || Mathf.Max(x, y) < Mathf.Min(x, y) * axisDominance) return;
        AttackDirection direction = x > y ? (delta.x > 0 ? AttackDirection.Right : AttackDirection.Left)
            : (delta.y > 0 ? AttackDirection.Up : AttackDirection.Down);
        if (target.TryAttack(direction))
        {
            lastAttack = DirectionLabel(direction);
            nextAttack = now + attackCooldown;
        }
        hasAnchor = false;
    }

    bool FindTip(byte[] rgb, int width, int height, int stride, out Vector2 position)
    {
        position = default;
        gridWidth = Mathf.Clamp(sampleWidth, 80, 320);
        gridHeight = Mathf.Clamp(Mathf.RoundToInt(gridWidth * (float)Screen.height / Screen.width), 40, 640);
        int count = gridWidth * gridHeight;
        if (mask == null || mask.Length != count) { mask = new bool[count]; queue = new int[count]; }
        for (int y = 0; y < gridHeight; y++)
        for (int x = 0; x < gridWidth; x++)
        {
            float u = (x + 0.5f) / gridWidth, v = (y + 0.5f) / gridHeight;
            // Vuforia camera buffers are in sensor orientation. Rotate then apply
            // the aspect-fill crop used by the camera background.
            int turns = cameraQuarterTurns;
#if !UNITY_EDITOR
            if (Screen.orientation == ScreenOrientation.Portrait) turns += 1;
            else if (Screen.orientation == ScreenOrientation.PortraitUpsideDown) turns += 3;
            else if (Screen.orientation == ScreenOrientation.LandscapeRight) turns += 2;
#endif
            turns %= 4;
            float imageAspect = (turns % 2 == 0) ? (float)width / height : (float)height / width;
            float screenAspect = (float)Screen.width / Screen.height;
            if (imageAspect > screenAspect) u = (u - 0.5f) * screenAspect / imageAspect + 0.5f;
            else v = (v - 0.5f) * imageAspect / screenAspect + 0.5f;
            float tx = u, ty = 1f - v;
            if (turns == 1) { tx = v; ty = u; }
            else if (turns == 2) { tx = 1f - u; ty = v; }
            else if (turns == 3) { tx = 1f - v; ty = 1f - u; }
            if (flipCameraY) ty = 1f - ty;
            int cell = y * gridWidth + x;
            mask[cell] = false;
            if (tx < 0 || tx >= 1 || ty < 0 || ty >= 1) continue;
            int pixel = Mathf.Min(height - 1, (int)(ty * height)) * stride + Mathf.Min(width - 1, (int)(tx * width)) * 3;
            if (pixel + 2 >= rgb.Length) continue;
            Color.RGBToHSV(new Color(rgb[pixel] / 255f, rgb[pixel + 1] / 255f, rgb[pixel + 2] / 255f), out float hue, out float saturation, out float value);
            mask[cell] = (hue <= redHueTolerance || hue >= 1f - redHueTolerance) && saturation >= minimumSaturation && value >= minimumBrightness;
        }
        int best = 0;
        // Connected components prevent unrelated red objects from averaging into a false tip.
        for (int i = 0; i < count; i++)
        {
            if (!mask[i]) continue;
            int head = 0, tail = 1;
            queue[0] = i; mask[i] = false;
            float sumX = 0, sumY = 0;
            while (head < tail)
            {
                int cell = queue[head++], x = cell % gridWidth, y = cell / gridWidth;
                sumX += x + 0.5f; sumY += y + 0.5f;
                if (x > 0) Enqueue(cell - 1, ref tail);
                if (x + 1 < gridWidth) Enqueue(cell + 1, ref tail);
                if (y > 0) Enqueue(cell - gridWidth, ref tail);
                if (y + 1 < gridHeight) Enqueue(cell + gridWidth, ref tail);
            }
            if (tail >= minimumBlobPixels && tail <= count * maximumBlobFraction && tail > best)
            {
                best = tail;
                position = new Vector2(sumX / tail / gridWidth, sumY / tail / gridHeight);
            }
        }
        return best > 0;
    }

    void Enqueue(int cell, ref int tail)
    {
        if (!mask[cell]) return;
        mask[cell] = false; queue[tail++] = cell;
    }

    static string DirectionLabel(AttackDirection direction)
    {
        switch (direction)
        {
            case AttackDirection.Up: return "Cima";
            case AttackDirection.Down: return "Baixo";
            case AttackDirection.Left: return "Esquerda";
            case AttackDirection.Right: return "Direita";
            default: return "Centro";
        }
    }

    void OnGUI()
    {
        if (!showOverlay) return;
        float scale = Mathf.Max(1f, Screen.height / 900f);
        var style = new GUIStyle(GUI.skin.box) { fontSize = Mathf.RoundToInt(18 * scale), alignment = TextAnchor.MiddleLeft };
        float h = 40 * scale;
        GUI.Box(new Rect(12, Screen.height - h * 3 - 12, Screen.width - 24, h * 3),
            $"Caneta - Jogador {playerIndex + 1}\n{status}\nUltimo ataque: {lastAttack}", style);
        var buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(16 * scale) };
        if (GUI.Button(new Rect(12, Screen.height - h * 4 - 20, 150 * scale, h), "Trocar jogador", buttonStyle))
        { playerIndex = (playerIndex + 1) % 2; hasTip = hasAnchor = false; }
        if (GUI.Button(new Rect(170 * scale, Screen.height - h * 4 - 20, 140 * scale, h), trackingEnabled ? "Pausar caneta" : "Ativar caneta", buttonStyle))
        { trackingEnabled = !trackingEnabled; hasTip = hasAnchor = false; }
        if (hasTip)
        {
            Color previous = GUI.color;
            GUI.color = Color.green;
            GUI.DrawTexture(new Rect(tip.x * Screen.width - 8, (1f - tip.y) * Screen.height - 8, 16, 16), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
