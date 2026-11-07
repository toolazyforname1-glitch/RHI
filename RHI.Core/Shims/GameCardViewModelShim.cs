// Headless stand-in for RenoDXCommander.ViewModels.GameCardViewModel.
//
// The real class is the game-card view model: ~400 members, many of them WinUI
// Visibility/brush bindings. The service layer only ever touches the subset
// below, so RHI.Core compiles against this minimal shape. The CLI builds these
// objects from detection results and passes them to the same services the GUI
// uses.
//
// IMPORTANT: this is a *copy* of the member surface, not of the logic. If a
// service starts using a new member, the RHI.Core build breaks — which is the
// early warning we want.

using Microsoft.UI.Dispatching;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

public partial class GameCardViewModel
{
    // ── Identity / detection ────────────────────────────────────────────────
    public string GameName { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public string Source { get; set; } = "";
    public string Maintainer { get; set; } = "";
    public bool Is32Bit { get; set; }
    public bool IsHidden { get; set; }
    public bool IsRunning { get; set; }
    public bool IsExternalOnly { get; set; }
    public bool IsDualApiGame { get; set; }
    public bool IsVulkanOnly => GraphicsApi == GraphicsApiType.Vulkan && !IsDualApiGame;
    public bool IsDxvkBlacklisted { get; set; }
    public bool HasDxvkApiOverride { get; set; }
    public bool IsNativeHdrGame { get; set; }
    public GraphicsApiType GraphicsApi { get; set; } = GraphicsApiType.Unknown;
    public HashSet<GraphicsApiType> DetectedApis { get; set; } = new();
    public DetectedGame? DetectedGame { get; set; }
    public string EngineHint { get; set; } = "";
    public string? EngineIniProjectOverride { get; set; }
    public string VulkanRenderingPath { get; set; } = "";
    public bool RequiresVulkanInstall => IsVulkanOnly;

    // ── UI thread (always "current" in the CLI) ─────────────────────────────
    public DispatcherQueue? DispatcherQueue { get; set; }

    // ── Status / progress ───────────────────────────────────────────────────
    public GameStatus Status { get; set; } = GameStatus.NotInstalled;
    public bool IsInstalling { get; set; }
    public double InstallProgress { get; set; }
    public string ActionMessage { get; set; } = "";
    public string WikiStatus { get; set; } = "";

    // ── ReShade ─────────────────────────────────────────────────────────────
    public GameStatus RsStatus { get; set; } = GameStatus.NotInstalled;
    public string? RsInstalledFile { get; set; }
    public string? RsInstalledVersion { get; set; }
    public bool RsIsInstalling { get; set; }
    public double RsProgress { get; set; }
    public string RsActionMessage { get; set; } = "";
    public AuxInstalledRecord? RsRecord { get; set; }
    public bool UseNormalReShade { get; set; }
    public string? ShaderModeOverride { get; set; }

    // ── RenoDX ──────────────────────────────────────────────────────────────
    public string? RdxInstalledVersion { get; set; }
    public string? InstalledAddonFileName { get; set; }
    public InstalledModRecord? InstalledRecord { get; set; }
    public GameMod? Mod { get; set; }
    public bool UseUeExtended { get; set; }
    public bool ExcludeFromUpdateAllRenoDx { get; set; }
    public bool ExcludeFromUpdateAllReShade { get; set; }
    public string? ExternalLabel { get; set; }
    public string? ExternalUrl { get; set; }
    public string? NameUrl { get; set; }
    public string? Notes { get; set; }
    public string? NotesUrl { get; set; }
    public string? NotesUrlLabel { get; set; }
    public string? DiscordUrl { get; set; }

    // ── Luma ────────────────────────────────────────────────────────────────
    public GameStatus LumaStatus { get; set; } = GameStatus.NotInstalled;
    public LumaMod? LumaMod { get; set; }
    public LumaInstalledRecord? LumaRecord { get; set; }
    public string? LumaNotes { get; set; }
    public string? LumaNotesUrl { get; set; }
    public string? LumaNotesUrlLabel { get; set; }

    // ── OptiScaler ──────────────────────────────────────────────────────────
    public GameStatus OsStatus { get; set; } = GameStatus.NotInstalled;
    public string? OsInstalledFile { get; set; }
    public string? OsInstalledVersion { get; set; }
    public bool OsIsInstalling { get; set; }
    public double OsProgress { get; set; }
    public bool IsOsInstalled => OsStatus == GameStatus.Installed;

    // ── DXVK ────────────────────────────────────────────────────────────────
    public GameStatus DxvkStatus { get; set; } = GameStatus.NotInstalled;
    public string? DxvkInstalledVersion { get; set; }
    public DxvkInstalledRecord? DxvkRecord { get; set; }

    // ── Display Commander / DU ──────────────────────────────────────────────
    public string? DcInstalledFile { get; set; }
    public string? DcInstalledVersion { get; set; }
    public bool DllOverrideEnabled { get; set; }

    // ── RE Framework ────────────────────────────────────────────────────────
    public GameStatus RefStatus { get; set; } = GameStatus.NotInstalled;
    public string? RefInstalledVersion { get; set; }
    public bool RefIsInstalling { get; set; }
    public double RefProgress { get; set; }
    public string RefActionMessage { get; set; } = "";
    public REFrameworkInstalledRecord? RefRecord { get; set; }

    // ── DOF Fix ─────────────────────────────────────────────────────────────
    public GameStatus DofFixStatus { get; set; } = GameStatus.NotInstalled;
    public string? DofFixInstalledVersion { get; set; }
    public bool DofFixIsInstalling { get; set; }
    public double DofFixProgress { get; set; }
    public string DofFixActionMessage { get; set; } = "";

    // ── UI notifications (no-ops without bindings) ──────────────────────────
    public void NotifyAll() { }
    public void RefreshBackupState() { }
    public void FadeMessage(Action<string> setter, string message, int delayMs = 4000, string? key = null) { }
}
