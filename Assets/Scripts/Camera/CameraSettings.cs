using UnityEngine;

// Persists per-machine camera follow offsets between sessions and scenes.
// Matches the SoundManager / MatchSessionConfig pattern used elsewhere.
public static class CameraSettings
{
    public const string HeightOffsetKey = "CameraHeightOffset";
    public const string DepthOffsetKey  = "CameraDepthOffset";

    public const float DefaultHeightOffset = 11f;
    public const float DefaultDepthOffset  = 8f;

    public const float MinHeightOffset = 5f;
    public const float MaxHeightOffset = 20f;
    public const float MinDepthOffset  = 3f;
    public const float MaxDepthOffset  = 15f;

    public static float LoadHeightOffset()
    {
        return Mathf.Clamp(
            PlayerPrefs.GetFloat(HeightOffsetKey, DefaultHeightOffset),
            MinHeightOffset, MaxHeightOffset);
    }

    public static float LoadDepthOffset()
    {
        return Mathf.Clamp(
            PlayerPrefs.GetFloat(DepthOffsetKey, DefaultDepthOffset),
            MinDepthOffset, MaxDepthOffset);
    }

    public static void SaveHeightOffset(float value)
    {
        PlayerPrefs.SetFloat(HeightOffsetKey, Mathf.Clamp(value, MinHeightOffset, MaxHeightOffset));
        PlayerPrefs.Save();
    }

    public static void SaveDepthOffset(float value)
    {
        PlayerPrefs.SetFloat(DepthOffsetKey, Mathf.Clamp(value, MinDepthOffset, MaxDepthOffset));
        PlayerPrefs.Save();
    }
}
