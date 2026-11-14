using Il2CppMenace.Audio;
using Il2CppStem;
using Jiangyu.Sdk;

namespace WOMENACE.Code;

// The holotable's sounds, in wmgfl_table_soundbank. The diorama plays one where an animation of
// its own starts. What vanilla already sounds for (a trade, a module installed or unlocked, an
// item assembled) keeps vanilla's own call, and its audio config names the table's sound.
public sealed class TableSounds : JiangyuSystem
{
    internal const string Appear = "appear";
    internal const string Vanish = "vanish";
    internal const string Travel = "travel";
    internal const string Cards = "cards";
    internal const string Select = "select";
    internal const string TokenLift = "token_lift";
    internal const string TokenPlace = "token_place";
    internal const string TokenRefused = "token_refused";
    internal const string Launch = "launch";
    internal const string Count = "count";
    internal const string ProjectorOn = "projector_on";
    internal const string ProjectorOff = "projector_off";
    internal const string Room = "room";

    private const string BankId = "wmgfl_table_soundbank";
    private static readonly int Bank = SoundIds.FromName(BankId);

    private static ID Id(string name) => new(Bank, SoundIds.FromName(name));

    internal static SoundInstance Play(string name) => Id(name).Play(1f, 1f);

    public override void OnTemplatesApplied()
    {
        var config = Templates.ById<AudioConfig>("audio_config", message => Context.Log.Warn($"table sounds: {message}"));
        if (config == null)
            return;
        // A sound that is missing keeps vanilla's.
        if (Named("trade") is { } trade)
            config.SoundBlackMarketTrade = trade;
        if (Named("installed") is { } installed)
            config.SoundShipUpgradeInstalled = installed;
        if (Named("unlocked") is { } unlocked)
            config.SoundOnShipUpgradeUnlock = unlocked;
        if (Named("assembled") is { } assembled)
            config.SoundWorkshopAssemble = assembled;
    }

    private ID? Named(string name)
    {
        var id = Id(name);
        if (SoundManager.GetSound(id) != null)
            return id;
        Context.Log.Warn($"table sounds: no {name} sound in {BankId}, keeping vanilla's");
        return null;
    }
}
