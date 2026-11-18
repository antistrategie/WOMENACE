namespace WOMENACE.Code;

// Each Doll's body model, by her character tag, as her GFL2 profile gives it (GunCharacterData's
// body model line). A Doll's dummy links run on the same body, so their names are drawn from it
// (DummyLinkNameSystem). A Doll without an entry keeps vanilla names on her squaddies.
internal static class BodyModels
{
    private static readonly Dictionary<string, string> ByCharacter = new(StringComparer.Ordinal)
    {
        ["wmgfl_alva"] = "CSD-09B4M",
        ["wmgfl_asteria"] = "SST-05A3 ST",
        ["wmgfl_cheyanne"] = "SSD-62D",
        ["wmgfl_helen"] = "SSD-62D",
        ["wmgfl_jiangyu"] = "SSD-62D",
        ["wmgfl_klukai"] = "SST-05",
        ["wmgfl_koleda"] = "GOL-Loki-00",
        ["wmgfl_lenna"] = "SSD-62D",
        ["wmgfl_leva"] = "DUAL DSI-8",
        ["wmgfl_lewis"] = "SSD-62D",
        ["wmgfl_makiatto"] = "SSD-62D",
        ["wmgfl_mechty"] = "SSD-62D",
        ["wmgfl_nemesis"] = "OBSRV-PRT-00",
        ["wmgfl_ots14"] = "SST-05A2-X",
        ["wmgfl_papasha"] = "SSD-62G",
        ["wmgfl_robella"] = "PPD-02",
        ["wmgfl_sextans"] = "STR-01A247",
        ["wmgfl_soppo"] = "SST-05A3 SEP",
        ["wmgfl_springfield"] = "SSD-62D",
        ["wmgfl_suomi"] = "SSD-62D",
        ["wmgfl_vector"] = "SSD-62D",
        ["wmgfl_voymastina"] = "CSD-09B4M",
        ["wmgfl_welrod"] = "SSD-62D",
    };

    public static string For(string characterTag)
        => characterTag != null ? ByCharacter.GetValueOrDefault(characterTag) : null;
}
