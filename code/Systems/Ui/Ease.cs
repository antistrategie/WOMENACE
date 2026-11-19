namespace WOMENACE.Code;

// Easing curves for the mod's own motion (camera moves, bars, panels), over t in 0..1.
internal static class Ease
{
    // Travel: gathers speed, then settles.
    internal static float InOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }

    // Travel with anticipation and overshoot: draws back a little before it goes, passes the
    // target a little and settles. The value leaves 0..1 at both ends by design.
    internal static float InOutBack(float t, float amount = 1.70158f)
    {
        t = Math.Clamp(t, 0f, 1f);
        var c = amount * 1.525f;
        return t < 0.5f
            ? MathF.Pow(2f * t, 2f) * ((c + 1f) * 2f * t - c) / 2f
            : (MathF.Pow(2f * t - 2f, 2f) * ((c + 1f) * (t * 2f - 2f) + c) + 2f) / 2f;
    }

    // Where along 0..1 InOutCubic reaches a value: for a thing that must start as a front moving
    // on that curve passes it.
    internal static float InOutCubicInverse(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value < 0.5f ? MathF.Cbrt(value / 4f) : 1f - MathF.Cbrt(2f * (1f - value)) / 2f;
    }

    // How far a value has come between two others, 0 to 1, starting and ending gently.
    internal static float SmoothStep(float from, float to, float at)
    {
        var t = Math.Clamp((at - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // Arrival with overshoot: runs past the target a little and settles back onto it.
    internal static float OutBack(float t, float amount = 1.70158f)
    {
        t = Math.Clamp(t, 0f, 1f) - 1f;
        return 1f + (amount + 1f) * t * t * t + amount * t * t;
    }

    // Arrival: fast in, long settle.
    internal static float OutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return 1f - MathF.Pow(1f - t, 3f);
    }
}
