using System;

// Digital microphone level (dBFS), not calibrated sound pressure (dB SPL).
public static class WarCryRules
{
    public const float ThresholdDb = -35f;
    public const float FullPowerDb = -8f;
    // A full-power cry adds this fraction of the damage (x1.5)
    public const float MaxBonus = 0.5f;
    public static float Power(float db, float threshold = ThresholdDb, float fullPower = FullPowerDb)
    {
        if (float.IsNaN(db)) return 0;
        return Math.Max(0f, Math.Min(1f, (db - threshold) / Math.Max(1f, fullPower - threshold)));
    }
    public static double Multiplier(float power) => 1d + MaxBonus * Math.Max(0f, Math.Min(1f, float.IsNaN(power) ? 0f : power));
}
