namespace WOMENACE.Code;

// Dev hooks for O.C.I. (code/Dev/Diorama.Dev.cs). *.Dev.cs files stay out of release builds.
public sealed partial class StrategyDiorama
{
    internal static string OciOrbitFromDev(float yaw, float distance)
    {
        var d = _instance;
        if (d == null || !d._oci.On)
            return "O.C.I. is not showing";
        var start = d._oci.Start;
        (d._oci.YawTarget, d._oci.DistanceTarget) = (Math.Clamp(yaw, start.Yaw - OciTurn, start.Yaw + OciTurn), Math.Clamp(distance, OciNearest, start.Distance));
        return $"orbit to {d._oci.YawTarget:F0}, {d._oci.DistanceTarget:F1}";
    }
}
