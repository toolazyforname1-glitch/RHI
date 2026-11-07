// Headless stand-ins for the only two WinUI types CrashReporter.Register touches.
// The CLI never calls Register (there is no XAML dispatcher to hook), but the
// method has to compile. Kept deliberately tiny so any new WinUI dependency in
// CrashReporter surfaces immediately as a build error.
namespace Microsoft.UI.Xaml;

public delegate void UnhandledExceptionEventHandler(object sender, UnhandledExceptionEventArgs e);

public class UnhandledExceptionEventArgs
{
    public Exception? Exception { get; set; }
    public string Message { get; set; } = "";
    public bool Handled { get; set; }
}

public class Application
{
    public event UnhandledExceptionEventHandler? UnhandledException;
}
