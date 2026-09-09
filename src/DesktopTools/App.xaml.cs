using System.Threading;
using System.Windows;

namespace DesktopTools;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private TrayController? _tray;
    private MemoryCleanupService? _cleanupService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\DesktopTools.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _cleanupService = new MemoryCleanupService();
        _cleanupService.RefreshSnapshot();

        _tray = new TrayController(_cleanupService);
        _tray.ShowMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
