using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli;

/// <summary>
/// Loads the persisted user state (settings.json + name mappings + per-game
/// overrides) into the same objects the GUI uses. Only mutating commands call
/// this: loading can trigger the app's legacy migrations, which write.
/// </summary>
internal static class CliState
{
    private static bool _initialized;

    public static void EnsureInitialized(IServiceProvider sp)
    {
        if (_initialized) return;

        var settings = sp.GetRequiredService<SettingsViewModel>();
        var names = sp.GetRequiredService<IGameNameService>();
        var dllOverrides = sp.GetRequiredService<IDllOverrideService>();

        names.LoadNameMappings(
            dllOverrides,
            settings,
            _ => { },
            _ => { },
            _ => { });

        _initialized = true;
    }

    public static void Save(IServiceProvider sp)
    {
        var names = sp.GetRequiredService<IGameNameService>();
        names.SaveNameMappings(
            sp.GetRequiredService<IDllOverrideService>(),
            sp.GetRequiredService<SettingsViewModel>(),
            default,
            false,
            "",
            new List<RenoDXCommander.Models.CustomFilter>());
        names.FlushPendingSave();
    }
}
