// Headless stand-in for the WinUI App class.
//
// RHI's services use App.Services as a static service locator in a handful of
// places (e.g. resolving a service lazily to break a DI cycle). In the CLI the
// locator simply points at the CLI's own ServiceProvider, which is registered
// with the same service graph.
namespace RenoDXCommander;

public static class App
{
    public static IServiceProvider Services { get; set; } = null!;
}
