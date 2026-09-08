namespace WOMENACE.Code;

public static class CalibrationCores
{
    public const string StandardId = "commodity.wmgfl_calibration_core";
    public const string AdvancedId = "commodity.wmgfl_advanced_calibration_core";
    public static IReadOnlyList<string> All { get; } = new[] { StandardId, AdvancedId };

    public static bool Contains(string id) => id == StandardId || id == AdvancedId;
    public static string ForWeapon(string baseWeaponId)
        => baseWeaponId.EndsWith("_ssr", StringComparison.Ordinal) ? AdvancedId : StandardId;
    public static string BlueprintId(string coreId) => coreId.Replace("commodity.", "blueprint.");
}
