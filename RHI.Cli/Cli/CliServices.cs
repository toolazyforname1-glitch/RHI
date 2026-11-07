using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Cli;

/// <summary>
/// DI graph mirroring App.xaml.cs, minus every UI-only registration.
/// Services are lazy: building the provider does not construct them, so a
/// command only pays for what it actually resolves.
/// </summary>
internal static class CliServices
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // ── Shared HttpClient (same policy as the app) ───────────────────────
        services.AddSingleton<HttpClient>(_ =>
        {
            var handler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                MaxConnectionsPerServer = 16,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                InitialHttp2StreamWindowSize = 1024 * 1024,
            };
            GitHubTokenHandler.SetToken(DevUnlockService.GitHubApiToken);
            var client = new HttpClient(new GitHubTokenHandler(handler));
            client.DefaultRequestHeaders.Add("User-Agent", "rhi-cli/0.1");
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestVersion = new Version(2, 0);
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            return client;
        });

        services.AddSingleton<GitHubETagCache>();

        // ── Services (same list as App.xaml.cs) ──────────────────────────────
        services.AddSingleton<IModInstallService, ModInstallService>();
        services.AddSingleton<IAuxInstallService, AuxInstallService>();
        services.AddSingleton<IWikiService, WikiService>();
        services.AddSingleton<IManifestService, ManifestService>();
        services.AddSingleton<IGameLibraryService, GameLibraryService>();
        services.AddSingleton<IReShadeUpdateService, ReShadeUpdateService>();
        services.AddSingleton<INormalReShadeUpdateService, NormalReShadeUpdateService>();
        services.AddSingleton<ReShadeNightlyService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ILumaService, LumaService>();
        services.AddSingleton<IShaderPackService, ShaderPackService>();
        services.AddSingleton<IGameDetectionService, GameDetectionService>();
        services.AddSingleton<IPeHeaderService, PeHeaderService>();
        services.AddSingleton<ICrashReporter, CrashReporterService>();
        services.AddSingleton<IAuxFileService>(sp =>
            sp.GetRequiredService<IAuxInstallService>() as AuxInstallService
            ?? throw new InvalidOperationException("IAuxInstallService must be AuxInstallService"));
        services.AddSingleton<IREFrameworkService, REFrameworkService>();
        services.AddSingleton<INexusModsService, NexusModsService>();
        services.AddSingleton<INexusUpdateService, NexusUpdateService>();
        services.AddSingleton<ISteamAppIdResolver, SteamAppIdResolver>();
        services.AddSingleton<IPcgwService, PcgwService>();
        services.AddSingleton<IUltrawideFixService, UltrawideFixService>();
        services.AddSingleton<IUltraPlusService, UltraPlusService>();

        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<IDllOverrideService, DllOverrideService>();
        services.AddSingleton<IGameNameService, GameNameService>();
        services.AddSingleton<IGameInitializationService, GameInitializationService>();
        services.AddSingleton<ISevenZipExtractor, ReShadeExtractor>();
        services.AddSingleton<IOptiScalerService, OptiScalerService>();
        services.AddSingleton<IOptiScalerWikiService, OptiScalerWikiService>();
        services.AddSingleton<IHdrDatabaseService, HdrDatabaseService>();
        services.AddSingleton<IDxvkService, DxvkService>();
        services.AddSingleton<Lazy<IDxvkService>>(sp =>
            new Lazy<IDxvkService>(() => sp.GetRequiredService<IDxvkService>()));
        services.AddSingleton<IDlssStreamlineService, DlssStreamlineService>();
        services.AddSingleton<DlssPresetService>();
        services.AddSingleton<DofFixService>();
        services.AddSingleton<UltimateAsiLoaderService>();
        services.AddSingleton<DlssNrCostScalerService>();
        services.AddSingleton<Rtx40MfgService>();
        services.AddSingleton<Dlssg20_30Service>();
        services.AddSingleton<DlssEnablerService>();
        services.AddSingleton<DgVoodooService>();
        services.AddSingleton<Renodx5AddonService>();
        services.AddSingleton<CustomReShadeHashService>();
        services.AddSingleton<SeenWikiModsService>();
        services.AddSingleton<SeenUltraPlusModsService>();
        services.AddSingleton<SeenLumaModsService>();
        services.AddSingleton<IUpdateLogService, UpdateLogService>();
        services.AddSingleton<IRenoDXDbService, RenoDXDbService>();
        services.AddSingleton<NexusDownloadService>();
        services.AddSingleton<NexusSsoService>();
        services.AddSingleton<GitHubAuthService>();
        services.AddSingleton<IDldsrService, DldsrService>();
        services.AddSingleton<IAutoHdrService, AutoHdrService>();
        services.AddSingleton<RtxEncoreService>();
        services.AddSingleton<Lazy<IDlssStreamlineService>>(sp =>
            new Lazy<IDlssStreamlineService>(() => sp.GetRequiredService<IDlssStreamlineService>()));

        var provider = services.BuildServiceProvider();

        // Some services use App.Services as a static locator (same pattern as the app).
        App.Services = provider;
        return provider;
    }
}
