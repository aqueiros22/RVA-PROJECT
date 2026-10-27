using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Vuforia;

// Screen-space color tracking supporting both WebCamTexture (Linux/Editor) and Vuforia (Android/Device),
// with 1-click calibration for ANY object (pen, marker, fingertip, card, or any colored object).
// Includes motion detection, static background filtering, smoothing, and gesture recognition
// integrated directly with turn-based combat (attacks and defenses).
[RequireComponent(typeof(CardWeaponManager))]
public class ColorGestureTracker : MonoBehaviour
{
    [Tooltip("Open the settings window automatically when the game launches so the user can calibrate the pen.")]
    public bool openSettingsOnStart = true;

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
    [Tooltip("Mirrors horizontal tracking so moving right in real life moves the marker right on screen.")]
    public bool mirrorX = true;
    [Tooltip("Flips vertical tracking so moving down in real life moves the marker down on screen.")]
    public bool flipY = true;

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
    [Tooltip("Cooldown in seconds after an attack/defense before another can trigger.")]
    [Min(0.1f)] public float attackCooldown = 0.5f;
    [Tooltip("Settling time in seconds after tracker enters view before gestures can be registered.")]
    [Range(0.1f, 0.8f)] public float appearanceSettlingTime = 0.40f;

    [Header("Visual Feedback")]
    [Tooltip("Enables tracked position marker dot on screen.")]
    public bool showMarker = true;
    [Tooltip("Enables glowing trail behind moving object.")]
    public bool enableMotionTrail = true;
    [Tooltip("Shows an animated directional arrow on screen when an attack/defense gesture is recognized.")]
    public bool showGestureFeedback = true;
    [Range(0.2f, 1.0f)] public float gestureFeedbackDuration = 0.40f;
    [Tooltip("Duration in seconds for the trail to fade out.")]
    [Range(0.1f, 1.0f)] public float trailDuration = 0.35f;
    [Tooltip("Maximum points kept in the trail buffer.")]
    [Range(5, 50)] public int maxTrailPoints = 30;

    private AttackDirection? activeFeedbackDir = null;
    private float feedbackStartTime = 0f;

    private struct TrailPoint
    {
        public Vector2 position; // Normalized viewport coords [0, 1]
        public float time;
    }
    private readonly List<TrailPoint> trailPoints = new List<TrailPoint>();

    private CardWeaponManager manager;
    private PlayerAvatar target;
    private DuelController duel;
    private bool formatRegistered, hasTip, hasAnchor;
    private VuforiaBehaviour vuforia;
    private Vector2 tip, anchor; // Normalized viewport coordinates [0, 1] (0 = left, 1 = right, 0 = bottom, 1 = top)
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
    private string lastActionLabel = "-";
    private bool showSettingsWindow = false;
    public bool ShowSettings
    {
        get => showSettingsWindow;
        set
        {
            showSettingsWindow = value;
            Time.timeScale = showSettingsWindow ? 0f : 1f;
        }
    }
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
    // Smoothed pen position in viewport coordinates (0-1, y up), mirror/flip already applied
    public Vector2 Tip => tip;
    public bool TipVisible => isVisible && hasTip;

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

    // Latest Vuforia camera image cache for calibration sampling on Android
    private byte[] lastVuforiaPixels;
    private int lastVuforiaWidth;
    private int lastVuforiaHeight;
    private int lastVuforiaStride;

    private const string PrefHasConfig = "Tracker_HasConfig";
    private const string PrefTargetHue = "Tracker_TargetHue";
    private const string PrefHueTolerance = "Tracker_HueTolerance";
    private const string PrefMinSaturation = "Tracker_MinSaturation";
    private const string PrefMinBrightness = "Tracker_MinBrightness";
    private const string PrefColorR = "Tracker_ColorR";
    private const string PrefColorG = "Tracker_ColorG";
    private const string PrefColorB = "Tracker_ColorB";
    private const string PrefMirrorX = "Tracker_MirrorX";
    private const string PrefFlipY = "Tracker_FlipY";
    private const string PrefShowMarker = "Tracker_ShowMarker";
    private const string PrefEnableTrail = "Tracker_EnableTrail";
    private const string PrefShowFeedback = "Tracker_ShowFeedback";
    private bool hasConfigSaved;

    void Awake()
    {
        LoadConfiguration();
    }

    void Start()
    {
        // Enforce strict gesture thresholds to prevent loose positioning triggers
        if (gestureDistance < 0.18f) gestureDistance = 0.22f;
        if (gestureWindow > 0.20f) gestureWindow = 0.16f;
        if (minSwipeSpeed < 1.8f) minSwipeSpeed = 2.0f;
        if (axisDominance < 1.5f) axisDominance = 1.6f;
        if (appearanceSettlingTime < 0.3f) appearanceSettlingTime = 0.40f;

        manager = GetComponent<CardWeaponManager>();
        duel = GetComponent<DuelController>();
        colorSwatchTexture = new Texture2D(1, 1);
        UpdateSwatchTexture();

        // Automatically open Settings on initial startup so user can calibrate pen/marker
        if (openSettingsOnStart)
        {
            ShowSettings = true;
        }

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
        canvas.sortingOrder = -100;

        var scaler = backgroundCanvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);

        var rawGo = new GameObject("WebCam Image", typeof(RectTransform), typeof(RawImage));
        rawGo.transform.SetParent(backgroundCanvasObject.transform, false);
        backgroundRawImage = rawGo.GetComponent<RawImage>();
        backgroundRawImage.texture = webcamTexture;

        var rect = (RectTransform)rawGo.transform;
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
        hasTip = hasAnchor = isVisible = false;
        consecutiveDetected = consecutiveLost = 0;
        trailPoints.Clear();
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;
        VuforiaApplication.Instance.OnVuforiaStarted -= RegisterFormat;
        VuforiaApplication.Instance.OnVuforiaStopped -= OnStopped;
        if (vuforia != null)
        {
            vuforia.World.OnStateUpdated -= OnFrame;
            if (formatRegistered && VuforiaApplication.Instance.IsRunning)
                vuforia.CameraDevice.SetFrameFormat(PixelFormat.RGB888, false);
        }

        if (webcamTexture != null && webcamTexture.isPlaying)
        {
            webcamTexture.Stop();
            Destroy(webcamTexture);
        }
        if (backgroundCanvasObject != null)
        {
            Destroy(backgroundCanvasObject);
        }
        if (colorSwatchTexture != null)
        {
            Destroy(colorSwatchTexture);
        }
        if (circleTexture != null)
        {
            Destroy(circleTexture);
        }
    }

    void OnDisable()
    {
        if (hasConfigSaved)
        {
            SaveConfiguration();
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && hasConfigSaved)
        {
            SaveConfiguration();
        }
    }

    void Update()
    {
        if (orientation != Screen.orientation)
        {
            orientation = Screen.orientation;
            hasTip = hasAnchor = isVisible = false;
            consecutiveDetected = consecutiveLost = 0;
            trailPoints.Clear();
        }

        if (hasTip && Time.unscaledTime - lastSeen > 0.25f)
        {
            hasTip = hasAnchor = isVisible = false;
            consecutiveDetected = 0;
        }

        // Clean up expired trail points
        if (enableMotionTrail && trailPoints.Count > 0)
        {
            float now = Time.unscaledTime;
            trailPoints.RemoveAll(p => now - p.time > trailDuration);
        }

        // Webcam fallback update loop
        if (webcamTexture != null && webcamTexture.isPlaying && webcamTexture.didUpdateThisFrame)
        {
            ProcessWebcamFrame();
        }
    }

    private bool IsCustomizeOpen()
    {
        if (playerSetupUI == null) playerSetupUI = FindFirstObjectByType<PlayerSetupUI>();
        return playerSetupUI != null && playerSetupUI.IsCustomizing;
    }

    void ProcessWebcamFrame()
    {
        if (!isActiveAndEnabled) return;

        if (duel == null && manager != null)
        {
            var player = manager.GetAttackPlayer(playerIndex);
            if (target != player) { target = player; hasAnchor = false; }
        }

        // In a duel the camera image is only read while the duel waits for the pen or while calibrating
        if (!trackingEnabled || IsCustomizeOpen() || (duel != null && !duel.WantsPen && !showSettingsWindow))
        {
            hasTip = hasAnchor = isVisible = false;
            consecutiveDetected = consecutiveLost = 0;
            trailPoints.Clear();
            currentStatus = TrackerStatusKey.Paused;
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextSample) return;
        nextSample = now + 1f / 30f;

        if (webcamPixels == null || webcamPixels.Length != webcamTexture.width * webcamTexture.height)
        {
            webcamPixels = new Color32[webcamTexture.width * webcamTexture.height];
        }
        webcamTexture.GetPixels32(webcamPixels);

        Vector2 rawPos;
        bool found = FindTipFromColor32(webcamPixels, webcamTexture.width, webcamTexture.height, out rawPos);
        UpdateDetectionState(found, rawPos, now);
        HandleTipMovement(now);
    }

    void OnFrame()
    {
        if (!isActiveAndEnabled) return;

        if (duel == null && manager != null)
        {
            var player = manager.GetAttackPlayer(playerIndex);
            if (target != player) { target = player; hasAnchor = false; }
        }

        // In a duel the camera image is only read while the duel waits for the pen or while calibrating
        if (!trackingEnabled || IsCustomizeOpen() || (duel != null && !duel.WantsPen && !showSettingsWindow))
        {
            hasTip = hasAnchor = isVisible = false;
            consecutiveDetected = consecutiveLost = 0;
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

        // Cache for calibration at ANY time during the game
        if (lastVuforiaPixels == null || lastVuforiaPixels.Length != image.Pixels.Length)
        {
            lastVuforiaPixels = new byte[image.Pixels.Length];
        }
        Array.Copy(image.Pixels, lastVuforiaPixels, image.Pixels.Length);
        lastVuforiaWidth = image.Width;
        lastVuforiaHeight = image.Height;
        lastVuforiaStride = image.Stride;

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

            // Require 2 consecutive frames to confirm object presence (eliminates single-frame noise spikes)
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

            // Debounce loss: must be missing for 4 consecutive frames before declaring lost
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

        // In a duel the DuelController reads Tip itself (pen held in the square, then dragged)
        bool isAwaitingCombatGesture = duel == null && !showSettingsWindow && !IsCustomizeOpen();
        if (!isAwaitingCombatGesture)
        {
            hasAnchor = false;
            return;
        }

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

        // delta.x > 0 means moved Right, delta.x < 0 means moved Left
        // delta.y > 0 means moved Up, delta.y < 0 means moved Down
        AttackDirection direction = x > y ? (screenDelta.x > 0 ? AttackDirection.Right : AttackDirection.Left)
                                          : (screenDelta.y > 0 ? AttackDirection.Up : AttackDirection.Down);

        bool accepted = false;
        if (manager != null)
        {
            var player = manager.GetAttackPlayer(playerIndex);
            if (player != null)
            {
                accepted = player.TryAttack(direction);
            }
        }

        if (accepted)
        {
            lastAttackDirection = direction;
            lastActionLabel = DirectionLabel(direction);
            TriggerGestureFeedback(direction);
            nextAttack = now + attackCooldown;
        }
        hasAnchor = false;
    }

    // Color matching: handles color circular wrap-around
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
            int imgY = Mathf.Clamp((int)(((float)y / gridHeight) * height), 0, height - 1);
            int rowOffset = imgY * width;
            int cellRowOffset = y * gridWidth;

            for (int x = 0; x < gridWidth; x++)
            {
                int imgX = Mathf.Clamp((int)(((float)(gridWidth - 1 - x) / gridWidth) * width), 0, width - 1);

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
                // Apply mirror/flip transforms directly so candidatePos and tip share the exact same coordinate space
                if (mirrorX) candidatePos.x = 1f - candidatePos.x;
                if (flipY) candidatePos.y = 1f - candidatePos.y;
                float avgMotion = sumMotion / tail;

                // 1. Spatial continuity: If we are already tracking an object, prevent instant teleportation
                float dist = hasTip ? Vector2.Distance(candidatePos, tip) : 0f;
                if (hasTip && dist > maxJumpDistance)
                {
                    continue;
                }

                float score = tail;

                // 2. Motion prioritization: suppress static objects when acquiring a new target
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

    // Unified coordinate conversion:
    // Viewport: (0,0) is bottom-left, (1,1) is top-right.
    // GUI Screen: (0,0) is top-left, (Screen.width, Screen.height) is bottom-right.
    public Vector2 ViewportToScreen(Vector2 vp)
    {
        return new Vector2(vp.x * Screen.width, (1f - vp.y) * Screen.height);
    }

    public Vector2 ScreenToViewport(Vector2 screen)
    {
        return new Vector2(screen.x / Screen.width, 1f - (screen.y / Screen.height));
    }

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

    public Color SampleColorAtViewport(Vector2 vp)
    {
        // Always try to fetch fresh live Vuforia frame if running
        if (vuforia != null && VuforiaApplication.Instance.IsRunning)
        {
            if (!formatRegistered) RegisterFormat();
            var img = vuforia.CameraDevice.GetCameraImage(PixelFormat.RGB888);
            if (img != null && img.Pixels != null && img.Pixels.Length > 0)
            {
                if (lastVuforiaPixels == null || lastVuforiaPixels.Length != img.Pixels.Length)
                {
                    lastVuforiaPixels = new byte[img.Pixels.Length];
                }
                Array.Copy(img.Pixels, lastVuforiaPixels, img.Pixels.Length);
                lastVuforiaWidth = img.Width;
                lastVuforiaHeight = img.Height;
                lastVuforiaStride = img.Stride;
            }
        }

        float sampleU = mirrorX ? (1f - vp.x) : vp.x;
        float sampleV = flipY ? (1f - vp.y) : vp.y;

        // 1. Check WebCam
        if (webcamPixels != null && webcamTexture != null && webcamTexture.isPlaying)
        {
            int w = webcamTexture.width;
            int h = webcamTexture.height;
            if (w > 0 && h > 0)
            {
                int px = Mathf.Clamp((int)((1f - sampleU) * w), 0, w - 1);
                int py = Mathf.Clamp((int)(sampleV * h), 0, h - 1);
                Color32 c = webcamPixels[py * w + px];
                return new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
            }
        }

        // 2. Check Vuforia cache
        if (lastVuforiaPixels != null && lastVuforiaWidth > 0 && lastVuforiaHeight > 0)
        {
            float u = sampleU, v = sampleV;
            int turns = cameraQuarterTurns;
#if !UNITY_EDITOR
            if (Screen.orientation == ScreenOrientation.Portrait) turns += 1;
            else if (Screen.orientation == ScreenOrientation.PortraitUpsideDown) turns += 3;
            else if (Screen.orientation == ScreenOrientation.LandscapeRight) turns += 2;
#endif
            turns %= 4;
            float imageAspect = (turns % 2 == 0) ? (float)lastVuforiaWidth / lastVuforiaHeight : (float)lastVuforiaHeight / lastVuforiaWidth;
            float screenAspect = (float)Screen.width / Screen.height;
            if (imageAspect > screenAspect) u = (u - 0.5f) * screenAspect / imageAspect + 0.5f;
            else v = (v - 0.5f) * imageAspect / screenAspect + 0.5f;

            float tx = u, ty = 1f - v;
            if (turns == 1) { tx = v; ty = u; }
            else if (turns == 2) { tx = 1f - u; ty = v; }
            else if (turns == 3) { tx = 1f - v; ty = 1f - u; }

            int pixel = Mathf.Min(lastVuforiaHeight - 1, Mathf.Max(0, (int)(ty * lastVuforiaHeight))) * lastVuforiaStride +
                        Mathf.Min(lastVuforiaWidth - 1, Mathf.Max(0, (int)(tx * lastVuforiaWidth))) * 3;
            if (pixel + 2 < lastVuforiaPixels.Length)
            {
                return new Color(lastVuforiaPixels[pixel] / 255f, lastVuforiaPixels[pixel + 1] / 255f, lastVuforiaPixels[pixel + 2] / 255f, 1f);
            }
        }

        return calibratedColor;
    }

    public void CalibrateAtViewport(Vector2 vp)
    {
        trackingEnabled = true;
        var validColors = new List<Color>();
        int radius = 4;

        // Sample a 9x9 neighborhood around the viewport coordinate for reliable outlier rejection
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                Vector2 sampleVP = vp + new Vector2((float)dx / sampleWidth, (float)dy / sampleWidth);
                Color col = SampleColorAtViewport(sampleVP);
                Color.RGBToHSV(col, out _, out float s, out float v);
                if (v >= 0.10f && s >= 0.12f) validColors.Add(col);
            }
        }

        Color finalColor;
        if (validColors.Count > 0)
        {
            // Pick most saturated pixels to lock on the true pigment
            validColors.Sort((a, b) =>
            {
                Color.RGBToHSV(a, out _, out float sa, out _);
                Color.RGBToHSV(b, out _, out float sb, out _);
                return sb.CompareTo(sa);
            });

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
            finalColor = SampleColorAtViewport(vp);
        }

        Color.RGBToHSV(finalColor, out float h_val, out float s_val, out float v_val);
        targetHue = h_val;
        hueTolerance = 0.08f;
        minimumSaturation = Mathf.Max(0.20f, s_val * 0.5f);
        minimumBrightness = Mathf.Max(0.12f, v_val * 0.45f);
        calibratedColor = finalColor;
        UpdateSwatchTexture();
        SaveConfiguration();

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
        SaveConfiguration();
        string presetMsg = Localization.Get($"Preset: {name}", $"Predefinição: {name}");
        SetCustomStatus(presetMsg, 3f);
    }

    public void SaveConfiguration()
    {
        hasConfigSaved = true;
        PlayerPrefs.SetInt(PrefHasConfig, 1);
        PlayerPrefs.SetFloat(PrefTargetHue, targetHue);
        PlayerPrefs.SetFloat(PrefHueTolerance, hueTolerance);
        PlayerPrefs.SetFloat(PrefMinSaturation, minimumSaturation);
        PlayerPrefs.SetFloat(PrefMinBrightness, minimumBrightness);
        PlayerPrefs.SetFloat(PrefColorR, calibratedColor.r);
        PlayerPrefs.SetFloat(PrefColorG, calibratedColor.g);
        PlayerPrefs.SetFloat(PrefColorB, calibratedColor.b);
        PlayerPrefs.SetInt(PrefMirrorX, mirrorX ? 1 : 0);
        PlayerPrefs.SetInt(PrefFlipY, flipY ? 1 : 0);
        PlayerPrefs.SetInt(PrefShowMarker, showMarker ? 1 : 0);
        PlayerPrefs.SetInt(PrefEnableTrail, enableMotionTrail ? 1 : 0);
        PlayerPrefs.SetInt(PrefShowFeedback, showGestureFeedback ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log("[ColorTracker] Configuration saved to PlayerPrefs.");
    }

    public void LoadConfiguration()
    {
        if (PlayerPrefs.GetInt(PrefHasConfig, 0) == 1)
        {
            hasConfigSaved = true;
            targetHue = PlayerPrefs.GetFloat(PrefTargetHue, targetHue);
            hueTolerance = PlayerPrefs.GetFloat(PrefHueTolerance, hueTolerance);
            minimumSaturation = PlayerPrefs.GetFloat(PrefMinSaturation, minimumSaturation);
            minimumBrightness = PlayerPrefs.GetFloat(PrefMinBrightness, minimumBrightness);
            float r = PlayerPrefs.GetFloat(PrefColorR, calibratedColor.r);
            float g = PlayerPrefs.GetFloat(PrefColorG, calibratedColor.g);
            float b = PlayerPrefs.GetFloat(PrefColorB, calibratedColor.b);
            calibratedColor = new Color(r, g, b, 1f);

            mirrorX = PlayerPrefs.GetInt(PrefMirrorX, mirrorX ? 1 : 0) == 1;
            flipY = PlayerPrefs.GetInt(PrefFlipY, flipY ? 1 : 0) == 1;
            showMarker = PlayerPrefs.GetInt(PrefShowMarker, showMarker ? 1 : 0) == 1;
            enableMotionTrail = PlayerPrefs.GetInt(PrefEnableTrail, enableMotionTrail ? 1 : 0) == 1;
            showGestureFeedback = PlayerPrefs.GetInt(PrefShowFeedback, showGestureFeedback ? 1 : 0) == 1;

            openSettingsOnStart = false;
            Debug.Log($"[ColorTracker] Configuration loaded from PlayerPrefs. Calibrated color: {calibratedColor}, MirrorX: {mirrorX}, FlipY: {flipY}");
        }
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
        if (showSettingsWindow)
        {
            return Localization.Get("Settings open. Move pen to test tracking.", "Configurações abertas. Mova o objeto para testar.");
        }

        if (IsCustomizeOpen())
        {
            return Localization.Get("Setup open (Tracking paused).", "Configuração aberta (Rastreamento pausado).");
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
                return Localization.Get("Camera not found!", "Câmera não encontrada!");
            case TrackerStatusKey.ActiveWait:
                return Localization.Get("Camera active. Hold object in view or open Settings to calibrate.", "Câmera ativa. Segure o objeto ou abra Configurações para calibrar.");
            case TrackerStatusKey.Paused:
                return Localization.Get("Tracking paused.", "Rastreamento pausado.");
            case TrackerStatusKey.Searching:
                return Localization.Get("Waiting for object in view...", "Aguardando objeto no campo de visão...");
            case TrackerStatusKey.Detected:
                return Localization.Get("Object detected! Make a quick swipe to attack/defend.", "Objeto detetado! Faça um movimento rápido para atacar/defender.");
            case TrackerStatusKey.VuforiaUnavailable:
                return Localization.Get("Vuforia: RGB image unavailable.", "Vuforia: Imagem RGB indisponível.");
            default:
                return "";
        }
    }

    public void ResetGesture()
    {
        hasTip = hasAnchor = isVisible = false;
        consecutiveDetected = consecutiveLost = 0;
        target = null;
        nextAttack = Time.unscaledTime + attackCooldown;
        lastActionLabel = "-";
        trailPoints.Clear();
    }

    void OnGUI()
    {
        if (!showOverlay || IsCustomizeOpen()) return;
        GUI.depth = -100;

        float scale = Mathf.Max(1f, Screen.height / 900f);
        var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(15 * scale) };

        if (circleTexture == null)
        {
            GenerateCircleTexture();
        }

        // Small settings toggle in the top right corner
        float btnW = 110 * scale;
        float btnH = 34 * scale;
        float btnX = Screen.width - btnW - 12 * scale;
        float btnY = 12 * scale;
        string settingsBtnText = Localization.Get("Settings", "Config");
        if (GUI.Button(new Rect(btnX, btnY, btnW, btnH), settingsBtnText, btnStyle))
        {
            ShowSettings = !ShowSettings;
        }

        // 1. Semi-transparent Settings Window (Centered modal dialog)
        if (showSettingsWindow)
        {
            float winW = 390 * scale;
            float headerH = 40 * scale;
            float cardPad = 12 * scale;
            float cardW = winW - cardPad * 2;

            float langCardH = 68 * scale;
            float calibCardH = 160 * scale;
            float optCardH = 88 * scale;
            float doneBtnH = 38 * scale;
            float paddingBottom = 16 * scale;

            float winH = (headerH + 10 * scale) + (langCardH + 10 * scale) + (calibCardH + 10 * scale) + (optCardH + 12 * scale) + doneBtnH + paddingBottom;
            float winX = (Screen.width - winW) * 0.5f;
            float winY = (Screen.height - winH) * 0.5f;

            // Translucent glass panels so user can clearly see through to the real-world pen/target
            DrawUIPanel(new Rect(winX - 4, winY - 4, winW + 8, winH + 8), new Color(0, 0, 0, 0.20f), Color.clear, 0);
            DrawUIPanel(new Rect(winX, winY, winW, winH), new Color(0.04f, 0.07f, 0.12f, 0.42f), new Color(0.35f, 0.55f, 0.85f, 0.80f), 2f * scale);

            // Header bar
            DrawUIPanel(new Rect(winX, winY, winW, headerH), new Color(0.08f, 0.12f, 0.20f, 0.55f), new Color(0.35f, 0.55f, 0.85f, 0.50f), 1f);

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(15 * scale),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.95f, 0.96f, 1f) }
            };
            GUI.Label(new Rect(winX + 16 * scale, winY, winW - 50 * scale, headerH),
                Localization.Get("TRACKING SETTINGS", "CONFIGURAÇÕES DE RASTREAMENTO"), titleStyle);

            var xBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(14 * scale), fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(winX + winW - 36 * scale, winY + 6 * scale, 28 * scale, 28 * scale), "X", xBtnStyle))
            {
                SaveConfiguration();
                ShowSettings = false;
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
            DrawUIPanel(new Rect(winX + cardPad, curY, cardW, langCardH), new Color(0.06f, 0.09f, 0.15f, 0.35f), new Color(0.25f, 0.35f, 0.55f, 0.50f), 1f);
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

            // Section 2: Color Calibration with reticle
            curY += langCardH + 10 * scale;
            DrawUIPanel(new Rect(winX + cardPad, curY, cardW, calibCardH), new Color(0.06f, 0.09f, 0.15f, 0.35f), new Color(0.25f, 0.35f, 0.55f, 0.50f), 1f);
            GUI.Label(new Rect(winX + cardPad + 10 * scale, curY + 6 * scale, cardW - 20 * scale, 18 * scale),
                Localization.Get("COLOR CALIBRATION", "CALIBRAÇÃO DE COR"), secHeaderStyle);

            float reticleX = winX + winW * 0.5f;
            float reticleY = curY + 54 * scale;
            Vector2 reticleVP = ScreenToViewport(new Vector2(reticleX, reticleY));
            string reticleLabel = Localization.Get("Position Pen / Object in Center", "Posicione a Caneta / Objeto no Centro");
            DrawReticleAt(reticleX, reticleY, reticleVP, scale, reticleLabel);

            float rowY = curY + 112 * scale;
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

            GUI.color = new Color(0.32f, 0.72f, 1f);
            float calibBtnW = cardW - 105 * scale;
            if (GUI.Button(new Rect(winX + cardPad + 92 * scale, rowY, calibBtnW, 34 * scale),
                Localization.Get("Calibrate Color", "Calibrar Cor"), btnStyle))
            {
                CalibrateAtViewport(reticleVP);
            }
            GUI.color = prevGUI;

            // Section 3: Visual Options (Marker, Trail, Mirror X, Flip Y)
            curY += calibCardH + 10 * scale;
            DrawUIPanel(new Rect(winX + cardPad, curY, cardW, optCardH), new Color(0.06f, 0.09f, 0.15f, 0.35f), new Color(0.25f, 0.35f, 0.55f, 0.50f), 1f);
            float halfW = (cardW - 24 * scale) * 0.5f;

            // Row 1: Marker toggle & Trail toggle
            string markerText = showMarker
                ? Localization.Get("Marker: ON", "Marcador: ATIVADO")
                : Localization.Get("Marker: OFF", "Marcador: DESATIVADO");
            GUI.color = showMarker ? new Color(0.4f, 0.95f, 0.6f) : new Color(0.75f, 0.75f, 0.8f);
            if (GUI.Button(new Rect(winX + cardPad + 8 * scale, curY + 6 * scale, halfW, 34 * scale), markerText, btnStyle))
            {
                showMarker = !showMarker;
                SaveConfiguration();
            }

            string trailText = enableMotionTrail
                ? Localization.Get("Trail: ON", "Rastro: ATIVADO")
                : Localization.Get("Trail: OFF", "Rastro: DESATIVADO");
            GUI.color = enableMotionTrail ? new Color(0.4f, 0.95f, 0.6f) : new Color(0.75f, 0.75f, 0.8f);
            if (GUI.Button(new Rect(winX + cardPad + 16 * scale + halfW, curY + 6 * scale, halfW, 34 * scale), trailText, btnStyle))
            {
                enableMotionTrail = !enableMotionTrail;
                if (!enableMotionTrail) trailPoints.Clear();
                SaveConfiguration();
            }

            // Row 2: Mirror X & Flip Y
            float row2Y = curY + 46 * scale;
            string mirrorXText = mirrorX
                ? Localization.Get("Mirror X: ON", "Espelhar X: SIM")
                : Localization.Get("Mirror X: OFF", "Espelhar X: NÃO");
            GUI.color = mirrorX ? new Color(0.95f, 0.85f, 0.35f) : new Color(0.75f, 0.75f, 0.8f);
            if (GUI.Button(new Rect(winX + cardPad + 8 * scale, row2Y, halfW, 34 * scale), mirrorXText, btnStyle))
            {
                mirrorX = !mirrorX;
                hasTip = hasAnchor = false;
                trailPoints.Clear();
                SaveConfiguration();
            }

            string flipYText = flipY
                ? Localization.Get("Flip Y: ON", "Inverter Y: SIM")
                : Localization.Get("Flip Y: OFF", "Inverter Y: NÃO");
            GUI.color = flipY ? new Color(0.95f, 0.85f, 0.35f) : new Color(0.75f, 0.75f, 0.8f);
            if (GUI.Button(new Rect(winX + cardPad + 16 * scale + halfW, row2Y, halfW, 34 * scale), flipYText, btnStyle))
            {
                flipY = !flipY;
                hasTip = hasAnchor = false;
                trailPoints.Clear();
                SaveConfiguration();
            }
            GUI.color = prevGUI;

            // Done button
            curY += optCardH + 12 * scale;
            GUI.color = new Color(0.92f, 0.78f, 0.28f);
            if (GUI.Button(new Rect(winX + (winW - 150 * scale) * 0.5f, curY, 150 * scale, doneBtnH),
                Localization.Get("Done", "Pronto"), btnStyle))
            {
                SaveConfiguration();
                ShowSettings = false;
            }
            GUI.color = prevGUI;
        }

        // In a duel the pen is only drawn while the duel uses it (or while calibrating)
        if (duel != null && !duel.WantsPen && !showSettingsWindow) return;

        // Render Motion Trail if enabled (rendered using unified ViewportToScreen coordinates)
        if (enableMotionTrail)
        {
            DrawMotionTrail(scale);
        }

        // Render Marker dot if enabled (rendered using unified ViewportToScreen coordinates)
        if (showMarker && isVisible && hasTip)
        {
            Vector2 screenPos = ViewportToScreen(tip);
            float dotSize = 24 * scale;

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(screenPos.x - dotSize * 0.5f - 1, screenPos.y - dotSize * 0.5f - 1, dotSize + 2, dotSize + 2), circleTexture);

            GUI.color = calibratedColor;
            GUI.DrawTexture(new Rect(screenPos.x - dotSize * 0.5f, screenPos.y - dotSize * 0.5f, dotSize, dotSize), circleTexture);

            GUI.color = Color.white;
            float centerDotSize = 8 * scale;
            GUI.DrawTexture(new Rect(screenPos.x - centerDotSize * 0.5f, screenPos.y - centerDotSize * 0.5f, centerDotSize, centerDotSize), circleTexture);
            GUI.color = previous;
        }

        // Render Directional Arrow gesture feedback
        DrawGestureFeedback(scale);
    }

    private void TriggerGestureFeedback(AttackDirection dir)
    {
        activeFeedbackDir = dir;
        feedbackStartTime = Time.unscaledTime;
    }

    private void DrawGestureFeedback(float scale)
    {
        if (!showGestureFeedback || !activeFeedbackDir.HasValue || circleTexture == null) return;

        float elapsed = Time.unscaledTime - feedbackStartTime;
        if (elapsed > gestureFeedbackDuration)
        {
            activeFeedbackDir = null;
            return;
        }

        float progress = elapsed / gestureFeedbackDuration;
        float alpha = progress < 0.2f ? (progress / 0.2f) : (1f - (progress - 0.2f) / 0.8f);
        alpha = Mathf.Clamp01(alpha);

        AttackDirection dir = activeFeedbackDir.Value;
        string arrowSymbol = "";
        Vector2 floatDir = Vector2.zero;

        switch (dir)
        {
            case AttackDirection.Up:
                arrowSymbol = "▲";
                floatDir = new Vector2(0f, -1f);
                break;
            case AttackDirection.Down:
                arrowSymbol = "▼";
                floatDir = new Vector2(0f, 1f);
                break;
            case AttackDirection.Left:
                arrowSymbol = "◀";
                floatDir = new Vector2(-1f, 0f);
                break;
            case AttackDirection.Right:
                arrowSymbol = "▶";
                floatDir = new Vector2(1f, 0f);
                break;
        }

        float floatDistance = 45f * scale * progress;
        float cx = Screen.width * 0.5f + floatDir.x * floatDistance;
        float cy = Screen.height * 0.5f + floatDir.y * floatDistance;

        float badgeRadius = (52f + 14f * progress) * scale;
        float badgeDiameter = badgeRadius * 2f;

        Color prevColor = GUI.color;

        // 1. Outer glowing aura
        GUI.color = new Color(0.2f, 0.7f, 1f, 0.40f * alpha);
        GUI.DrawTexture(new Rect(cx - badgeRadius * 1.18f, cy - badgeRadius * 1.18f, badgeDiameter * 1.18f, badgeDiameter * 1.18f), circleTexture);

        // 2. Dark translucent circular plate
        GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.88f * alpha);
        GUI.DrawTexture(new Rect(cx - badgeRadius, cy - badgeRadius, badgeDiameter, badgeDiameter), circleTexture);

        // 3. Calibrated color glow ring
        Color ringCol = calibratedColor;
        ringCol.a = 0.85f * alpha;
        GUI.color = ringCol;
        GUI.DrawTexture(new Rect(cx - badgeRadius * 0.95f, cy - badgeRadius * 0.95f, badgeDiameter * 0.95f, badgeDiameter * 0.95f), circleTexture);

        // Inner plate to form hollow ring
        GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.92f * alpha);
        GUI.DrawTexture(new Rect(cx - badgeRadius * 0.88f, cy - badgeRadius * 0.88f, badgeDiameter * 0.88f, badgeDiameter * 0.88f), circleTexture);

        // 4. Large Direction Arrow
        var arrowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(44 * scale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 0.94f, 0.35f, alpha) }
        };
        GUI.color = Color.white;
        GUI.Label(new Rect(cx - badgeRadius, cy - badgeRadius + 4 * scale, badgeDiameter, badgeDiameter * 0.65f), arrowSymbol, arrowStyle);

        // 5. Localized Label
        var labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(13 * scale),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.95f, 0.96f, 1f, 0.95f * alpha) }
        };
        string textLabel = DirectionLabel(dir).ToUpperInvariant();
        GUI.Label(new Rect(cx - badgeRadius, cy + 12 * scale, badgeDiameter, badgeRadius * 0.65f), textLabel, labelStyle);

        GUI.color = prevColor;
    }

    private void DrawReticleAt(float cx, float cy, Vector2 vp, float scale, string labelText = null)
    {
        Color liveCenterColor = SampleColorAtViewport(vp);
        float outerRadius = 22 * scale;
        float innerRadius = 9 * scale;

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(cx - outerRadius - 2, cy - outerRadius - 2, (outerRadius + 2) * 2, (outerRadius + 2) * 2), circleTexture);

        GUI.color = new Color(1f, 1f, 1f, 0.95f);
        GUI.DrawTexture(new Rect(cx - outerRadius, cy - outerRadius, outerRadius * 2, outerRadius * 2), circleTexture);

        GUI.color = liveCenterColor;
        GUI.DrawTexture(new Rect(cx - innerRadius, cy - innerRadius, innerRadius * 2, innerRadius * 2), circleTexture);

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

            // Using unified ViewportToScreen for 100% alignment with marker dot
            Vector2 screenA = ViewportToScreen(pA.position);
            Vector2 screenB = ViewportToScreen(pB.position);

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
