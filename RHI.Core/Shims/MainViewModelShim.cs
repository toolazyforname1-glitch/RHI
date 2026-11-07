// Headless stand-in for the bits of MainViewModel that the service layer calls.
//
// Static helpers are mirrored exactly; the one instance member used by a service
// (DlssEnabler's auto-update pass) returns null, because the CLI never runs that
// UI-driven pass. If another instance member starts being used, RHI.Core stops
// compiling — which is the intended early warning.
namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private const string DcFileName64 = "zzz_display_commander.addon64";
    private const string DcFileName32 = "zzz_display_commander.addon32";
    private const string LegacyDcFileName64 = "zzz_display_commander_lite.addon64";
    private const string LegacyDcFileName32 = "zzz_display_commander_lite.addon32";

    internal static string GetDcFileName(bool is32Bit) => is32Bit ? DcFileName32 : DcFileName64;

    internal static string GetLegacyDcFileName(bool is32Bit) => is32Bit ? LegacyDcFileName32 : LegacyDcFileName64;

    public string? GetDeInstalledAs(string gameName, string store = "") => null;
}
