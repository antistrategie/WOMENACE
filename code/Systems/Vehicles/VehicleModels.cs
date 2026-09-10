namespace WOMENACE.Code;

internal static class VehicleModels
{
    public const string Sinner = "player_vehicle.koleda_car";
    public const string Sinbreaker = "player_vehicle.voymastina_mech";
    public const string SinbreakerErwin = "player_vehicle.voymastina_mech_erwin";
    public const string SinbreakerConstruct = "construct.voymastina_mech";
    public const string SinbreakerErwinConstruct = "construct.voymastina_mech_erwin";

    public static string AssetFor(string templateId) => templateId switch
    {
        Sinbreaker or SinbreakerConstruct => "voymastina_mech/default/main",
        SinbreakerErwin or SinbreakerErwinConstruct => "voymastina_mech/erwin/main",
        Sinner => "koleda_car/default/main",
        _ => null,
    };
}
