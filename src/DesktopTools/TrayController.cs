using System.Drawing;
using System.Windows.Forms;

namespace DesktopTools;

public sealed class TrayController : IDisposable
{
    private readonly MemoryCleanupService _service;
    private readonly NotifyIcon _notifyIcon;
    private MainWindow? _mainWindow;
    private bool _disposed;

    public TrayController(MemoryCleanupService service)
    {
        _service = service;

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => ShowMainWindow());
        menu.Items.Add("一键清理", null, (_, _) => TriggerCleanup());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Desktop Tools — 内存清理",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        _service.StateChanged += OnServiceStateChanged;
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow(_service, this);
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void TriggerCleanup()
    {
        if (_service.IsCleaning)
        {
            return;
        }

        if (!_service.TryBeginCleanup(out _))
        {
            ShowMainWindow();
        }
    }

    private void ExitApplication()
    {
        _service.RequestExit();
        _notifyIcon.Text = "Desktop Tools — 正在退出…";
        System.Windows.Forms.Application.DoEvents();

        // 等待正在执行的清理收尾（最多 35 秒，略高于 Helper 30 秒预算）。
        int waited = 0;
        while (_service.IsCleaning && waited < 35000)
        {
            System.Threading.Thread.Sleep(100);
            waited += 100;
        }

        if (_service.IsCleaning)
        {
            Log.Error("Exit requested while cleanup still running after wait budget");
        }

        System.Windows.Application.Current.Shutdown();
    }

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_service.IsCleaning)
            {
                _notifyIcon.Text = "Desktop Tools — 正在清理…";
            }
            else if (_service.ExitRequested)
            {
                _notifyIcon.Text = "Desktop Tools — 正在退出…";
            }
            else if (_service.LastCleanupResult is { } result)
            {
                string status = result.OverallStatus switch
                {
                    CleanupOverallStatus.Success => "清理成功",
                    CleanupOverallStatus.PartialSuccess => "部分成功",
                    CleanupOverallStatus.Failed => "清理失败",
                    _ => "内存清理"
                };
                _notifyIcon.Text = $"Desktop Tools — {status}";
            }
        }
        catch
        {
            // 托盘文案更新失败不影响清理结果。
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _service.StateChanged -= OnServiceStateChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
