using UnityEngine;
using UnityEngine.UI;
using Vuforia;

public enum AttackDirection { Up, Down, Left, Right }

// Screen-space color tracking supporting both WebCamTexture (Linux/Editor) and Vuforia (Android/Device),
// with 1-click calibration for ANY object (pen, marker, fingertip, card, or any colored object).
[RequireComponent(typeof(CardWeaponManager))]
public class ColorGestureTracker : MonoBehaviour
{
    [Tooltip("Extra clockwise rotation of the camera image, for device calibration.")]
    [Range(0, 3)] public int cameraQuarterTurns;
    [Min(0)] public int playerIndex;
    public bool trackingEnabled = true;
    public bool showOverlay = true;

    [Header("Color Calibration")]
    [Tooltip("Target Hue in range [0, 1]. Red ~0.0 or 1.0, Yellow ~0.16, Green ~0.33, Cyan ~0.5, Blue ~0.66, Magenta ~0.83.")]
    [Range(0f, 1f)] public float targetHue = 0.0f;
    [Range(0.01f, 0.3f)] public float hueTolerance = 0.08f;
    [Range(0f, 1f)] public float minimumSaturation = 0.4f;
    [Range(0f, 1f)] public float minimumBrightness = 0.18f;
    public Color calibratedColor = Color.red;

    [Header("Detection Grid")]
    [Range(80, 320)] public int sampleWidth = 160;
    [Min(2)] public int minimumBlobPixels = 4;
    [Range(0.001f, 0.5f)] public float maximumBlobFraction = 0.15f;
    public bool flipCameraY;

    [Header("Tracking Stability & Motion Filtering")]
    [Tooltip("Smoothing factor for position (0.1 = slow/smooth, 1.0 = raw/jittery).")]
    [Range(0.1f, 1f)] public float positionSmoothing = 0.65f;
    [Tooltip("Maximum allowed jump distance per frame. Prevents snapping to other objects across the room.")]
    [Range(0.1f, 0.8f)] public float maxJumpDistance = 0.35f;
    [Tooltip("Prioritizes moving objects and suppresses static background clutter.")]
    public bool filterStaticBackground = true;

    [Header("Gestures (fractions of the shorter screen side)")]
    [Tooltip("Minimum distance traveled in screen fractions for a swipe.")]
    [Range(0.05f, 0.5f)] public float gestureDistance = 0.22f;
    [Tooltip("Maximum duration in seconds for a fast swipe gesture.")]
    [Range(0.08f, 0.4f)] public float gestureWindow = 0.16f;
    [Tooltip("Minimum speed (screen fractions per second) required to trigger a swipe.")]
    [Range(0.5f, 5.0f)] public float minSwipeSpeed = 2.0f;
    [Tooltip("Dominance factor between primary and secondary axis for clear direction.")]
    [Range(1.2f, 3.0f)] public float axisDominance = 1.6f;
    [Tooltip("Cooldown in seconds after an attack before another can trigger.")]
    [Min(0.1f)] public float attackCooldown = 0.5f;
    [Tooltip("Settling time in seconds after tracker enters view before gestures can be registered.")]
    [Range(0.1f, 0.8f)] public float appearanceSettlingTime = 0.40f;

    [Header("Visual Feedback")]
    [Tooltip("Enables tracked position marker dot on screen.")]
    public bool showMarker = true;
    [Tooltip("Enables glowing trail behind moving object.")]
    public bool enableMotionTrail = true;
    [Tooltip("Duration in seconds for the trail to fade out.")]
    [Range(0.1f, 1.0f)] public float trailDuration = 0.35f;
    [Tooltip("Maximum points kept in the trail buffer.")]
    [Range(5, 50)] public int maxTrailPoints = 30;

    private struct TrailPoint
    {
        public Vector2 position;
        public float time;
    }
    private readonly System.Collections.Generic.List<TrailPoint> trailPoints = new System.Collections.Generic.List<TrailPoint>();

    private CardWeaponManager manager;
    private PlayerAvatar target;
    private bool formatRegistered, hasTip, hasAnchor;
    private VuforiaBehaviour vuforia;
    private Vector2 tip, anchor;
    private float nextSample, anchorTime, nextAttack, lastSeen, visibleSinceTime;
    private bool[] mask;
    private int[] queue;
    private int gridWidth, gridHeight;
    private ScreenOrientation orientation;
    private enum TrackerStatusKey
    {
        Initializing,
        NotFound,
        ActiveWait,
        Paused,
        Searching,
        Detected,
        VuforiaUnavailable
    }
    private TrackerStatusKey currentStatus = TrackerStatusKey.Initializing;
    private AttackDirection? lastAttackDirection = null;
    private bool showSettingsWindow = false;
    private string customStatusMessage = null;
    private float customStatusEndTime = 0f;

    private void SetCustomStatus(string message, float duration = 3.5f)
    {
        customStatusMessage = message;
        customStatusEndTime = Time.unscaledTime + duration;
    }

    // Hysteresis / Anti-flicker state
    private int consecutiveDetected = 0;
    private int consecutiveLost = 0;
    public bool isVisible { get; private set; }

    // WebCam fallback for Linux Editor
    private WebCamTexture webcamTexture;
    private Color32[] webcamPixels;
    private GameObject backgroundCanvasObject;
    private RawImage backgroundRawImage;
    private Texture2D colorSwatchTexture;
    private Texture2D circleTexture;
    private byte[] prevGridBrightness;
    private byte[] motionGrid;
    private PlayerSetupUI playerSetupUI;

    void Start()
    {
        // Enforce strict gesture thresholds to prevent old serialized scene values from allowing loose positioning triggers
        if (gestureDistance < 0.18f) gestureDistance = 0.22f;
        if (gestureWindow > 0.20f) gestureWindow = 0.16f;
        if (minSwipeSpeed < 1.8f) minSwipeSpeed = 2.0f;
        if (axisDominance < 1.5f) axisDominance = 1.6f;
        if (appearanceSettlingTime < 0.3f) appearanceSettlingTime = 0.40f;

        manager = GetComponent<CardWeaponManager>();
        colorSwatchTexture = new Texture2D(1, 1);
        UpdateSwatchTexture();

#if UNITY_EDITOR_LINUX
        StartWebcam();
#else
        vuforia = VuforiaBehaviour.Instance;
        if (vuforia != null)
        {
            VuforiaApplication.Instance.OnVuforiaStarted += RegisterFormat;
            VuforiaApplication.Instance.OnVuforiaStopped += OnStopped;
            vuforia.World.OnStateUpdated += OnFrame;
            RegisterFormat();
        }
        else
        {
            StartWebcam();
        }
#endif
    }

    void StartWebcam()
    {
        if (WebCamTexture.devices.Length > 0)
        {
            webcamTexture = new WebCamTexture(WebCamTexture.devices[0].name, 640, 480, 30);
            webcamTexture.Play();
            SetupWebcamBackground();
            currentStatus = TrackerStatusKey.ActiveWait;
        }
        else
        {
            currentStatus = TrackerStatusKey.NotFound;
        }
    }

    void SetupWebcamBackground()
    {
        if (backgroundCanvasObject != null) return;

        backgroundCanvasObject = new GameObject("WebCam Background Canvas", typeof(Canvas), typeof(CanvasScaler));
        var canvas = backgroundCanvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0; // Behind Player Setup Canvas (sortingOrder = 10)

        var scaler = backgroundCanvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);

        var rawGo = new GameObject("WebCam Image", typeof(RectTransform), typeof(RawImage));
        rawGo.transform.SetParent(backgroundCanvasObject.transform, false);
        backgroundRawImage = rawGo.GetComponent<RawImage>();
        backgroundRawImage.texture = webcamTexture;
        backgroundRawImage.uvRect = new Rect(1, 0, -1, 1); // Mirrored horizontally

        var rect = rawGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
        if (webcamTexture != null && webcamTexture.isPlaying)
            webcamTexture.Stop();

        if (backgroundCanvasObject != null)
            Destroy(backgroundCanvasObject);

        if (colorSwatchTexture != null)
            Destroy(colorSwatchTexture);

        if (circleTexture != null)
            Destroy(circleTexture);

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
        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            ProcessWebcamFrame();
        }

        if (isVisible && Time.unscaledTime - lastSeen > 0.35f)
        {
            isVisible = false;
            hasTip = hasAnchor = false;
        }

        // Expire older motion trail points
        float now = Time.unscaledTime;
        for (int i = trailPoints.Count - 1; i >= 0; i--)
        {
            if (now - trailPoints[i].time > trailDuration)
            {
                trailPoints.RemoveAt(i);
            }
        }
    }

    private bool IsCustomizeOpen()
    {
        if (playerSetupUI == null) playerSetupUI = FindFirstObjectByType<PlayerSetupUI>();
        return playerSetupUI != null && playerSetupUI.IsPanelOpen;
    }

    void ProcessWebcamFrame()
    {
        if (!trackingEnabled || IsCustomizeOpen())
        {
            isVisible = false;
            hasTip = hasAnchor = false;
            trailPoints.Clear();
            currentStatus = TrackerStatusKey.Paused;
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextSample) return;
        nextSample = now + 1f / 30f;

        int w = webcamTexture.width;
        int h = webcamTexture.height;
        if (w <= 16 || h <= 16) return;

        if (webcamPixels == null || webcamPixels.Length != w * h)
            webcamPixels = new Color32[w * h];

        webcamTexture.GetPixels32(webcamPixels);

        Vector2 rawPos;
        bool found = FindTipFromColor32(webcamPixels, w, h, out rawPos);
        UpdateDetectionState(found, rawPos, now);
        HandleTipMovement(now);
    }

    void OnFrame()
    {
        if (!isActiveAndEnabled || webcamTexture != null) return;
        if (orientation != Screen.orientation)
        {
            orientation = Screen.orientation;
            hasTip = hasAnchor = false;
        }

        if (!trackingEnabled || IsCustomizeOpen())
        {
            isVisible = false;
            hasTip = hasAnchor = false;
            trailPoints.Clear();
            currentStatus = TrackerStatusKey.Paused;
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextSample) return;
        if (!formatRegistered) { currentStatus = TrackerStatusKey.VuforiaUnavailable; return; }
        nextSample = now + 1f / 30f;

        var image = vuforia.CameraDevice.GetCameraImage(PixelFormat.RGB888);
        if (image == null || image.Pixels == null) return;

        Vector2 rawPos;
        bool found = FindTip(image.Pixels, image.Width, image.Height, image.Stride, out rawPos);
        UpdateDetectionState(found, rawPos, now);
        HandleTipMovement(now);
    }

    void UpdateDetectionState(bool found, Vector2 rawPos, float now)
    {
        // Screen edge bounds check: if on screen perimeter, it has left the view
        if (found && (rawPos.x < 0.02f || rawPos.x > 0.98f || rawPos.y < 0.02f || rawPos.y > 0.98f))
        {
            found = false;
        }

        if (found)
        {
            consecutiveLost = 0;
            consecutiveDetected++;

            // Require 2 consecutive frames to confirm object presence (eliminates noise spikes)
            if (consecutiveDetected >= 2)
            {
                if (!isVisible)
                {
                    tip = rawPos;
                    isVisible = true;
                    visibleSinceTime = now;
                }
                else
                {
                    // Smooth tracking
                    tip = Vector2.Lerp(tip, rawPos, positionSmoothing);
                }
                hasTip = true;
                lastSeen = now;

                if (enableMotionTrail)
                {
                    if (trailPoints.Count == 0 || Vector2.Distance(trailPoints[trailPoints.Count - 1].position, tip) > 0.002f)
                    {
                        trailPoints.Add(new TrailPoint { position = tip, time = now });
                        if (trailPoints.Count > maxTrailPoints)
                        {
                            trailPoints.RemoveAt(0);
                        }
                    }
                }
            }
        }
        else
        {
            consecutiveDetected = 0;
            consecutiveLost++;

            // Debounce loss: must be missing for 4 consecutive frames (~130ms) before declaring lost
            // This prevents rapid 1-frame flickering
            if (consecutiveLost >= 4)
            {
                isVisible = false;
                hasTip = false;
                hasAnchor = false;
            }
        }
    }

    void HandleTipMovement(float now)
    {
        if (!isVisible)
        {
            hasAnchor = false;
            currentStatus = TrackerStatusKey.Searching;
            return;
        }

        currentStatus = TrackerStatusKey.Detected;

        if (now < nextAttack) { hasAnchor = false; return; }

        // 1. Initial grace period when object first enters camera view
        // Allows user to position tracker into view without triggering an accidental swipe
        if (now - visibleSinceTime < appearanceSettlingTime)
        {
            anchor = tip;
            anchorTime = now;
            hasAnchor = true;
            return;
        }

        if (!hasAnchor || now - anchorTime > gestureWindow)
        {
            anchor = tip;
            anchorTime = now;
            hasAnchor = true;
            return;
        }

        Vector2 delta = tip - anchor;
        Vector2 screenDelta = Vector2.Scale(delta, new Vector2(Screen.width, Screen.height)) / Mathf.Min(Screen.width, Screen.height);
        float dist = screenDelta.magnitude;
        float dt = Mathf.Max(0.001f, now - anchorTime);
        float speed = dist / dt;

        // 2. Slow movement filtering: while user is gently positioning or holding steady,
        // continuously slide anchor along so distance never accumulates
        if (speed < minSwipeSpeed)
        {
            anchor = tip;
            anchorTime = now;
            return;
        }

        float x = Mathf.Abs(screenDelta.x);
        float y = Mathf.Abs(screenDelta.y);

        // 3. Require minimum distance and clear directional dominance
        if (Mathf.Max(x, y) < gestureDistance || Mathf.Max(x, y) < Mathf.Min(x, y) * axisDominance)
            return;

        AttackDirection direction = x > y ? (screenDelta.x > 0 ? AttackDirection.Right : AttackDirection.Left)
                                          : (screenDelta.y > 0 ? AttackDirection.Down : AttackDirection.Up);

        lastAttackDirection = direction;
        nextAttack = now + attackCooldown;
        hasAnchor = false;

        if (manager != null)
        {
            var player = manager.GetAttackPlayer(playerIndex);
            if (player != null)
            {
                player.TryAttack(direction);
            }
        }
    }

    // Color matching: handles color circular wrap-around (e.g. red is at both 0.0 and 1.0)
    public bool IsMatchingColor(float hue, float sat, float val)
    {
        if (sat < minimumSaturation || val < minimumBrightness) return false;
        float diff = Mathf.Abs(hue - targetHue);
        if (diff > 0.5f) diff = 1f - diff;
        return diff <= hueTolerance;
    }

    bool FindTipFromColor32(Color32[] pixels, int width, int height, out Vector2 position)
    {
        position = default;
        gridWidth = Mathf.Clamp(sampleWidth, 80, 320);
        gridHeight = Mathf.Clamp(Mathf.RoundToInt(gridWidth * (float)height / width), 40, 640);
        int count = gridWidth * gridHeight;
        if (mask == null || mask.Length != count)
        {
            mask = new bool[count];
            queue = new int[count];
            prevGridBrightness = new byte[count];
            motionGrid = new byte[count];
        }

        for (int y = 0; y < gridHeight; y++)
        {
            int imgY = Mathf.Min(height - 1, (int)(((float)(gridHeight - 1 - y) / gridHeight) * height));
            int rowOffset = imgY * width;
            int cellRowOffset = y * gridWidth;

            for (int x = 0; x < gridWidth; x++)
            {
                int imgX = Mathf.Min(width - 1, (int)(((float)(gridWidth - 1 - x) / gridWidth) * width));
                Color32 c = pixels[rowOffset + imgX];
                Color.RGBToHSV(c, out float hue, out float saturation, out float value);

                int cell = cellRowOffset + x;
                mask[cell] = IsMatchingColor(hue, saturation, value);

                // Compute frame-to-frame pixel motion
                byte lum = (byte)((c.r * 54 + c.g * 183 + c.b * 19) >> 8);
                motionGrid[cell] = (byte)Mathf.Abs(lum - prevGridBrightness[cell]);
                prevGridBrightness[cell] = lum;
            }
        }

        return FindCentroid(count, out position);
    }

    bool FindTip(byte[] rgb, int width, int height, int stride, out Vector2 position)
    {
        position = default;
        gridWidth = Mathf.Clamp(sampleWidth, 80, 320);
        gridHeight = Mathf.Clamp(Mathf.RoundToInt(gridWidth * (float)Screen.height / Screen.width), 40, 640);
        int count = gridWidth * gridHeight;
        if (mask == null || mask.Length != count)
        {
            mask = new bool[count];
            queue = new int[count];
            prevGridBrightness = new byte[count];
            motionGrid = new byte[count];
        }

        for (int y = 0; y < gridHeight; y++)
        for (int x = 0; x < gridWidth; x++)
        {
            float u = (x + 0.5f) / gridWidth, v = (y + 0.5f) / gridHeight;
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

            byte r = rgb[pixel], g = rgb[pixel + 1], b = rgb[pixel + 2];
            Color.RGBToHSV(new Color(r / 255f, g / 255f, b / 255f), out float hue, out float saturation, out float value);
            mask[cell] = IsMatchingColor(hue, saturation, value);

            byte lum = (byte)((r * 54 + g * 183 + b * 19) >> 8);
            motionGrid[cell] = (byte)Mathf.Abs(lum - prevGridBrightness[cell]);
            prevGridBrightness[cell] = lum;
        }

        return FindCentroid(count, out position);
    }

    bool FindCentroid(int count, out Vector2 position)
    {
        position = default;
        float bestScore = 0f;
        Vector2 bestPos = Vector2.zero;

        for (int i = 0; i < count; i++)
        {
            if (!mask[i]) continue;
            int head = 0, tail = 1;
            queue[0] = i; mask[i] = false;
            float sumX = 0, sumY = 0;
            float sumMotion = 0;

            while (head < tail)
            {
                int cell = queue[head++], x = cell % gridWidth, y = cell / gridWidth;
                sumX += x + 0.5f; sumY += y + 0.5f;
                if (motionGrid != null) sumMotion += motionGrid[cell];

                if (x > 0) Enqueue(cell - 1, ref tail);
                if (x + 1 < gridWidth) Enqueue(cell + 1, ref tail);
                if (y > 0) Enqueue(cell - gridWidth, ref tail);
                if (y + 1 < gridHeight) Enqueue(cell + gridWidth, ref tail);
            }

            if (tail >= minimumBlobPixels && tail <= count * maximumBlobFraction)
            {
                Vector2 candidatePos = new Vector2(sumX / tail / gridWidth, sumY / tail / gridHeight);
                float avgMotion = sumMotion / tail;

                // 1. Spatial continuity: If we are already tracking an object, prevent instant teleportation across screen
                float dist = hasTip ? Vector2.Distance(candidatePos, tip) : 0f;
                if (hasTip && dist > maxJumpDistance)
                {
                    // Ignore blobs that teleport too far while actively tracking
                    continue;
                }

                float score = tail;

                // 2. Motion prioritization: only penalize static objects when acquiring a new target
                if (filterStaticBackground && !hasTip)
                {
                    float motionFactor = Mathf.Clamp(avgMotion / 10f, 0.25f, 3.5f);
                    score *= motionFactor;
                }

                // 3. Proximity bonus: prefer candidates close to the last tracked location
                if (hasTip)
                {
                    score /= (1f + dist * 5f);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPos = candidatePos;
                }
            }
        }

        if (bestScore > 0f)
        {
            position = bestPos;
            return true;
        }

        return false;
    }

    void Enqueue(int cell, ref int tail)
    {
        if (!mask[cell]) return;
        mask[cell] = false; queue[tail++] = cell;
    }

    // Draw a modern bordered UI panel without relying on default GUI skins
    private void DrawUIPanel(Rect rect, Color bg, Color border, float borderWidth = 1f)
    {
        Color prev = GUI.color;
        if (border.a > 0f && borderWidth > 0f)
        {
            GUI.color = border;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }
        GUI.color = bg;
        GUI.DrawTexture(new Rect(rect.x + borderWidth, rect.y + borderWidth, Mathf.Max(0, rect.width - borderWidth * 2), Mathf.Max(0, rect.height - borderWidth * 2)), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    public Color SampleColorAtUV(Vector2 uv)
    {
        if (webcamPixels == null || webcamTexture == null || !webcamTexture.isPlaying)
            return calibratedColor;

        int w = webcamTexture.width;
        int h = webcamTexture.height;
        if (w <= 0 || h <= 0) return calibratedColor;

        int px = Mathf.Clamp((int)((1f - uv.x) * w), 0, w - 1);
        int py = Mathf.Clamp((int)((1f - uv.y) * h), 0, h - 1);
        Color32 c = webcamPixels[py * w + px];
        return new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
    }

    // High-precision sampling with outlier rejection and saturation weighting
    public void CalibrateAtUV(Vector2 uv)
    {
        if (webcamTexture == null || webcamPixels == null) return;
        int w = webcamTexture.width;
        int h = webcamTexture.height;
        if (w <= 0 || h <= 0) return;

        // Account for horizontal camera mirror and vertical screen-to-texture mapping
        int centerX = Mathf.Clamp((int)((1f - uv.x) * w), 0, w - 1);
        int centerY = Mathf.Clamp((int)((1f - uv.y) * h), 0, h - 1);

        int radius = 3; // 7x7 patch around sampled point
        var validColors = new System.Collections.Generic.List<Color>();

        for (int dy = -radius; dy <= radius; dy++)
        {
            int py = Mathf.Clamp(centerY + dy, 0, h - 1);
            int row = py * w;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int px = Mathf.Clamp(centerX + dx, 0, w - 1);
                Color32 c = webcamPixels[row + px];
                Color col = new Color(c.r / 255f, c.g / 255f, c.b / 255f);
                Color.RGBToHSV(col, out _, out float s, out float v);

                // Discard extremely dark shadows or washed out gray/white background
                if (v >= 0.12f && s >= 0.15f)
                {
                    validColors.Add(col);
                }
            }
        }

        Color finalColor;
        if (validColors.Count > 0)
        {
            // Sort by saturation descending to isolate the pure object pigment from background bleeding
            validColors.Sort((a, b) =>
            {
                Color.RGBToHSV(a, out _, out float sa, out _);
                Color.RGBToHSV(b, out _, out float sb, out _);
                return sb.CompareTo(sa);
            });

            // Average top 40% most saturated pixels
            int countToUse = Mathf.Max(1, Mathf.RoundToInt(validColors.Count * 0.4f));
            float rSum = 0, gSum = 0, bSum = 0;
            for (int i = 0; i < countToUse; i++)
            {
                rSum += validColors[i].r;
                gSum += validColors[i].g;
                bSum += validColors[i].b;
            }
            finalColor = new Color(rSum / countToUse, gSum / countToUse, bSum / countToUse);
        }
        else
        {
            Color32 c = webcamPixels[centerY * w + centerX];
            finalColor = new Color(c.r / 255f, c.g / 255f, c.b / 255f);
        }

        Color.RGBToHSV(finalColor, out float h_val, out float s_val, out float v_val);

        targetHue = h_val;
        hueTolerance = 0.08f;
        minimumSaturation = Mathf.Max(0.22f, s_val * 0.5f);
        minimumBrightness = Mathf.Max(0.15f, v_val * 0.45f);
        calibratedColor = finalColor;
        UpdateSwatchTexture();

        string calibMsg = Localization.Get(
            $"Calibrated! Hue: {targetHue:F2}, Sat: {s_val:F2}, Val: {v_val:F2}",
            $"Calibrado! Matiz: {targetHue:F2}, Sat: {s_val:F2}, Val: {v_val:F2}");
        SetCustomStatus(calibMsg, 3.5f);
        Debug.Log($"[ColorTracker] {calibMsg}");
    }

    public void SetColorPreset(float hue, Color previewColor, string name)
    {
        targetHue = hue;
        hueTolerance = 0.08f;
        minimumSaturation = 0.35f;
        minimumBrightness = 0.18f;
        calibratedColor = previewColor;
        UpdateSwatchTexture();
        string presetMsg = Localization.Get($"Preset: {name}", $"Predefinição: {name}");
        SetCustomStatus(presetMsg, 3f);
    }

    void UpdateSwatchTexture()
    {
        if (colorSwatchTexture == null)
            colorSwatchTexture = new Texture2D(1, 1);
        colorSwatchTexture.SetPixel(0, 0, calibratedColor);
        colorSwatchTexture.Apply();
    }

    private string DirectionLabel(AttackDirection direction)
    {
        switch (direction)
        {
            case AttackDirection.Up: return Localization.Get("Up", "Cima");
            case AttackDirection.Down: return Localization.Get("Down", "Baixo");
            case AttackDirection.Left: return Localization.Get("Left", "Esquerda");
            case AttackDirection.Right: return Localization.Get("Right", "Direita");
            default: return Localization.Get("Center", "Centro");
        }
    }

    private string GetStatusText()
    {
        if (IsCustomizeOpen())
        {
            return Localization.Get("Customization open (Tracking paused).", "Personalização aberta (Rastreamento pausado).");
        }

        if (!string.IsNullOrEmpty(customStatusMessage) && Time.unscaledTime < customStatusEndTime)
        {
            return customStatusMessage;
        }

        switch (currentStatus)
        {
            case TrackerStatusKey.Initializing:
                return Localization.Get("Initializing camera...", "Inicializando câmera...");
            case TrackerStatusKey.NotFound:
                return Localization.Get("Webcam not found!", "Câmera não encontrada!");
            case TrackerStatusKey.ActiveWait:
                return Localization.Get("Camera active. Hold object in view or open Settings to calibrate.", "Câmera ativa. Segure o objeto ou abra Configurações para calibrar.");
            case TrackerStatusKey.Paused:
                return Localization.Get("Object tracking paused.", "Rastreamento pausado.");
            case TrackerStatusKey.Searching:
                return Localization.Get("Waiting for calibrated object in camera view...", "Aguardando objeto calibrado no campo de visão...");
            case TrackerStatusKey.Detected:
                return Localization.Get("Object detected! Make a quick gesture in the air.", "Objeto detetado! Faça um movimento rápido no ar.");
            case TrackerStatusKey.VuforiaUnavailable:
                return Localization.Get("Vuforia: RGB image unavailable.", "Vuforia: Imagem RGB indisponível.");
            default:
                return "";
        }
    }

    void OnGUI()
    {
        if (!showOverlay) return;

        float scale = Mathf.Max(1f, Screen.height / 900f);
        var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(15 * scale) };

        if (circleTexture == null)
        {
            GenerateCircleTexture();
        }

        // 1. Top-Right "Settings" toggle button (No emojis)
        float btnW = 140 * scale;
        float btnH = 42 * scale;
        float btnX = Screen.width - btnW - 16;
        float btnY = 16;
        string settingsBtnText = Localization.Get("Settings", "Config");
        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), settingsBtnText, btnStyle))
        {
            showSettingsWindow = !showSettingsWindow;
        }

        // Top-Right "Customize" toggle button (directly below Settings, matching style)
        float custBtnY = btnY + btnH + 8 * scale;
        string custBtnText = Localization.Get("Customize", "Personalizar");
        if (GUI.Button(new Rect(btnX, custBtnY, btnW, btnH), custBtnText, btnStyle))
        {
            if (playerSetupUI == null) playerSetupUI = FindFirstObjectByType<PlayerSetupUI>();
            if (playerSetupUI != null) playerSetupUI.TogglePanel();
        }

        bool customizeOpen = IsCustomizeOpen();

        // 2. Settings Window (Centered on screen)
        if (showSettingsWindow)
        {
            float winW = 360 * scale;
            float headerH = 40 * scale;
            float cardPad = 12 * scale;
            float cardW = winW - cardPad * 2;

            float langCardH = 68 * scale;
            float calibCardH = 148 * scale;
            float optCardH = 44 * scale;
            float doneBtnH = 36 * scale;
            float paddingBottom = 16 * scale;

            float winH = (headerH + 10 * scale) + (langCardH + 10 * scale) + (calibCardH + 10 * scale) + (optCardH + 12 * scale) + doneBtnH + paddingBottom;

            float winX = (Screen.width - winW) * 0.5f;
            float winY = (Screen.height - winH) * 0.5f;

            // Panel background (Semi-transparent dark glass so the camera and tracker behind are clearly visible)
            DrawUIPanel(new Rect(winX - 4, winY - 4, winW + 8, winH + 8), new Color(0, 0, 0, 0.25f), Color.clear, 0);
            DrawUIPanel(new Rect(winX, winY, winW, winH), new Color(0.08f, 0.10f, 0.15f, 0.55f), new Color(0.32f, 0.44f, 0.65f, 0.85f), 2f * scale);

            // Header bar
            DrawUIPanel(new Rect(winX, winY, winW, headerH), new Color(0.12f, 0.16f, 0.24f, 0.75f), new Color(0.32f, 0.44f, 0.65f, 0.5f), 1f);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(15 * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.95f, 0.96f, 1f) }
            };
            GUI.Label(new Rect(winX + 16 * scale, winY, winW - 50 * scale, headerH),
                Localization.Get("SETTINGS", "CONFIGURAÇÕES"), titleStyle);

            var xBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14 * scale), fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(winX + winW - 36 * scale, winY + 6 * scale, 28 * scale, 28 * scale), "X", xBtnStyle))
            {
                showSettingsWindow = false;
            }

            float curY = winY + headerH + 10 * scale;

            var secHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(12 * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.65f, 0.78f, 0.95f) }
            };

            // Section 1: Language
            DrawUIPanel(new Rect(winX + cardPad, curY, cardW, langCardH), new Color(0.10f, 0.13f, 0.18f, 0.50f), new Color(0.25f, 0.35f, 0.50f, 0.5f), 1f);
            GUI.Label(new Rect(winX + cardPad + 10 * scale, curY + 6 * scale, cardW - 20 * scale, 18 * scale),
                Localization.Get("LANGUAGE", "IDIOMA"), secHeaderStyle);

            bool isEN = Localization.CurrentLanguage == GameLanguage.English;
            bool isPT = Localization.CurrentLanguage == GameLanguage.Portuguese;

            float pillW = (cardW - 28 * scale) * 0.5f;
            float pillH = 32 * scale;
            float pillY = curY + 26 * scale;

            Color prevGUI = GUI.color;
            GUI.color = isEN ? new Color(0.35f, 0.9f, 0.55f) : new Color(0.85f, 0.88f, 0.92f);
            if (GUI.Button(new Rect(winX + cardPad + 10 * scale, pillY, pillW, pillH), "English", btnStyle))
            {
                Localization.SetLanguage(GameLanguage.English);
            }

            GUI.color = isPT ? new Color(0.35f, 0.9f, 0.55f) : new Color(0.85f, 0.88f, 0.92f);
            if (GUI.Button(new Rect(winX + cardPad + 10 * scale + pillW + 8 * scale, pillY, pillW, pillH), "Português", btnStyle))
            {
                Localization.SetLanguage(GameLanguage.Portuguese);
            }
            GUI.color = prevGUI;

            // Section 2: Calibration (Tracker positioned directly in center of settings popup)
            curY += langCardH + 10 * scale;
            DrawUIPanel(new Rect(winX + cardPad, curY, cardW, calibCardH), new Color(0.08f, 0.11f, 0.16f, 0.40f), new Color(0.28f, 0.40f, 0.60f, 0.6f), 1f);
            GUI.Label(new Rect(winX + cardPad + 10 * scale, curY + 6 * scale, cardW - 20 * scale, 18 * scale),
                Localization.Get("COLOR CALIBRATION", "CALIBRAÇÃO DE COR"), secHeaderStyle);

            // Center reticle right inside the settings popup
            float reticleX = winX + winW * 0.5f;
            float reticleY = curY + 46 * scale;
            Vector2 reticleUV = new Vector2(reticleX / Screen.width, reticleY / Screen.height);
            string reticleLabel = Localization.Get("Position Tracker in Center", "Posicione o Rastreador no Centro");
            DrawReticleAt(reticleX, reticleY, reticleUV, scale, reticleLabel);

            // Row below reticle: Active Color swatch & Calibrate button
            float rowY = curY + 98 * scale;
            var chipLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(12 * scale),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.9f, 0.9f, 0.95f) }
            };
            GUI.Label(new Rect(winX + cardPad + 12 * scale, rowY + 5 * scale, 45 * scale, 24 * scale),
                Localization.Get("Color:", "Cor:"), chipLabelStyle);

            if (colorSwatchTexture != null)
            {
                GUI.DrawTexture(new Rect(winX + cardPad + 58 * scale, rowY + 5 * scale, 24 * scale, 24 * scale), colorSwatchTexture);
            }

            // Calibrate Button
            GUI.color = new Color(0.32f, 0.72f, 1f);
            float calibBtnW = cardW - 105 * scale;
            if (GUI.Button(new Rect(winX + cardPad + 92 * scale, rowY, calibBtnW, 34 * scale),
                Localization.Get("Calibrate Color", "Calibrar Cor"), btnStyle))
            {
                CalibrateAtUV(reticleUV);
            }
            GUI.color = prevGUI;

            // Section 3: Visual Options (Independent Marker & Trail toggles)
            curY += calibCardH + 10 * scale;
            DrawUIPanel(new Rect(winX + cardPad, curY, cardW, optCardH), new Color(0.10f, 0.13f, 0.18f, 0.50f), new Color(0.25f, 0.35f, 0.50f, 0.5f), 1f);

            float halfW = (cardW - 24 * scale) * 0.5f;

            // Toggle Marker
            string markerText = showMarker
                ? Localization.Get("Marker: ON", "Marcador: ATIVADO")
                : Localization.Get("Marker: OFF", "Marcador: DESATIVADO");
            GUI.color = showMarker ? new Color(0.4f, 0.95f, 0.6f) : new Color(0.75f, 0.75f, 0.8f);
            if (GUI.Button(new Rect(winX + cardPad + 8 * scale, curY + 5 * scale, halfW, 34 * scale), markerText, btnStyle))
            {
                showMarker = !showMarker;
            }

            // Toggle Trail
            string trailText = enableMotionTrail
                ? Localization.Get("Trail: ON", "Rastro: ATIVADO")
                : Localization.Get("Trail: OFF", "Rastro: DESATIVADO");
            GUI.color = enableMotionTrail ? new Color(0.4f, 0.95f, 0.6f) : new Color(0.75f, 0.75f, 0.8f);
            if (GUI.Button(new Rect(winX + cardPad + 16 * scale + halfW, curY + 5 * scale, halfW, 34 * scale), trailText, btnStyle))
            {
                enableMotionTrail = !enableMotionTrail;
                if (!enableMotionTrail) trailPoints.Clear();
            }
            GUI.color = prevGUI;

            // Done button with generous bottom padding
            curY += optCardH + 12 * scale;
            GUI.color = new Color(0.92f, 0.78f, 0.28f);
            if (GUI.Button(new Rect(winX + (winW - 150 * scale) * 0.5f, curY, 150 * scale, doneBtnH),
                Localization.Get("Done", "Pronto"), btnStyle))
            {
                showSettingsWindow = false;
            }
            GUI.color = prevGUI;
        }

        // 3. Bottom tracker controls and visual feedback (Disabled / Hidden while Customize is open)
        if (!customizeOpen)
        {
            var boxStyle = new GUIStyle(GUI.skin.box) { fontSize = Mathf.RoundToInt(17 * scale), alignment = TextAnchor.MiddleLeft };
            float h = 42 * scale;

            string playerTitle = Localization.Get($"Action Tracking — Player {playerIndex + 1}", $"Rastreamento de Ação — Jogador {playerIndex + 1}");
            string statusLabel = Localization.Get("Status", "Estado");
            string gestureLabel = Localization.Get("Last Gesture", "Último Gesto");
            string displayedGesture = lastAttackDirection.HasValue ? DirectionLabel(lastAttackDirection.Value) : "-";

            GUI.Box(new Rect(12, Screen.height - h * 3 - 12, Screen.width - 24, h * 3),
                $"{playerTitle}\n{statusLabel}: {GetStatusText()}\n{gestureLabel}: {displayedGesture}", boxStyle);

            if (GUI.Button(new Rect(12, Screen.height - h * 4 - 20, 160 * scale, h), Localization.Get("Switch Player", "Trocar Jogador"), btnStyle))
            {
                playerIndex = (playerIndex + 1) % 2;
                hasTip = hasAnchor = false;
            }

            string toggleTrackerText = trackingEnabled ? Localization.Get("Pause Tracker", "Pausar Rastreador") : Localization.Get("Enable Tracker", "Ativar Rastreador");
            if (GUI.Button(new Rect(180 * scale, Screen.height - h * 4 - 20, 160 * scale, h), toggleTrackerText, btnStyle))
            {
                trackingEnabled = !trackingEnabled;
                hasTip = hasAnchor = false;
                if (!trackingEnabled) trailPoints.Clear();
            }

            // Draw motion trail behind object (independent of marker)
            DrawMotionTrail(scale);

            // Draw tracked tip marker only when enabled and visibly present (independent of trail)
            if (showMarker && isVisible && hasTip)
            {
                float dotSize = 28 * scale;
                float px = tip.x * Screen.width;
                float py = tip.y * Screen.height;

                Color previous = GUI.color;

                // Outer dark ring for contrast against any background
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                float outlineSize = dotSize + 6 * scale;
                GUI.DrawTexture(new Rect(px - outlineSize * 0.5f, py - outlineSize * 0.5f, outlineSize, outlineSize), circleTexture);

                // Calibrated color circle body
                GUI.color = calibratedColor;
                GUI.DrawTexture(new Rect(px - dotSize * 0.5f, py - dotSize * 0.5f, dotSize, dotSize), circleTexture);

                // Inner white dot for precision centering
                GUI.color = Color.white;
                float centerDotSize = 8 * scale;
                GUI.DrawTexture(new Rect(px - centerDotSize * 0.5f, py - centerDotSize * 0.5f, centerDotSize, centerDotSize), circleTexture);

                GUI.color = previous;
            }
        }
    }

    private void DrawReticleAt(float cx, float cy, Vector2 uv, float scale, string labelText = null)
    {
        Color liveCenterColor = SampleColorAtUV(uv);

        float outerRadius = 22 * scale;
        float innerRadius = 9 * scale;

        Color prev = GUI.color;

        // Shadow ring
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(cx - outerRadius - 2, cy - outerRadius - 2, (outerRadius + 2) * 2, (outerRadius + 2) * 2), circleTexture);

        // White target circle
        GUI.color = new Color(1f, 1f, 1f, 0.95f);
        GUI.DrawTexture(new Rect(cx - outerRadius, cy - outerRadius, outerRadius * 2, outerRadius * 2), circleTexture);

        // Live camera color inside the circle
        GUI.color = liveCenterColor;
        GUI.DrawTexture(new Rect(cx - innerRadius, cy - innerRadius, innerRadius * 2, innerRadius * 2), circleTexture);

        // Crosshairs ticks
        GUI.color = Color.white;
        float tickLen = 10 * scale;
        float tickThick = 2 * scale;
        GUI.DrawTexture(new Rect(cx - outerRadius - tickLen, cy - tickThick * 0.5f, tickLen, tickThick), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx + outerRadius, cy - tickThick * 0.5f, tickLen, tickThick), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - tickThick * 0.5f, cy - outerRadius - tickLen, tickThick, tickLen), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - tickThick * 0.5f, cy + outerRadius, tickThick, tickLen), Texture2D.whiteTexture);

        if (!string.IsNullOrEmpty(labelText))
        {
            var hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(11 * scale),
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.9f, 0.95f, 1f, 0.9f) }
            };
            GUI.Label(new Rect(cx - 150 * scale, cy + outerRadius + 6 * scale, 300 * scale, 18 * scale), labelText, hintStyle);
        }

        GUI.color = prev;
    }

    private void DrawMotionTrail(float scale)
    {
        if (!enableMotionTrail || trailPoints.Count < 2 || circleTexture == null) return;

        float now = Time.unscaledTime;
        float baseDotSize = 22 * scale;
        Color previous = GUI.color;

        for (int i = 0; i < trailPoints.Count - 1; i++)
        {
            var pA = trailPoints[i];
            var pB = trailPoints[i + 1];

            float ageA = Mathf.Clamp01((now - pA.time) / trailDuration);
            float ageB = Mathf.Clamp01((now - pB.time) / trailDuration);

            float alphaA = 1f - ageA;
            float alphaB = 1f - ageB;

            Vector2 screenA = new Vector2(pA.position.x * Screen.width, pA.position.y * Screen.height);
            Vector2 screenB = new Vector2(pB.position.x * Screen.width, pB.position.y * Screen.height);

            float segDist = Vector2.Distance(screenA, screenB);
            int steps = Mathf.Max(1, Mathf.RoundToInt(segDist / (4f * scale)));

            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                Vector2 pos = Vector2.Lerp(screenA, screenB, t);
                float alpha = Mathf.Lerp(alphaA, alphaB, t);
                float curSize = baseDotSize * (0.2f + 0.8f * alpha);

                Color c = calibratedColor;
                c.a = alpha * 0.75f;
                GUI.color = c;

                GUI.DrawTexture(new Rect(pos.x - curSize * 0.5f, pos.y - curSize * 0.5f, curSize, curSize), circleTexture);
            }
        }

        GUI.color = previous;
    }

    private void GenerateCircleTexture(int size = 64)
    {
        circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        circleTexture.filterMode = FilterMode.Bilinear;
        Color[] cols = new Color[size * size];
        float radius = (size - 2) * 0.5f;
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                cols[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        circleTexture.SetPixels(cols);
        circleTexture.Apply();
    }
}
