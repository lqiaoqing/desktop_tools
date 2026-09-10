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

        // 设计要求：窗口显示时读取内存信息。
        if (!_service.IsCleaning)
        {
            _service.RefreshSnapshot();
        }
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

        // UAC 对话框可能超过 Helper 自身 30s 预算，退出需等待本次选择/执行完成。
        const int waitBudgetMs = 120000;
        int waited = 0;
        while (_service.IsCleaning && waited < waitBudgetMs)
        {
            System.Threading.Thread.Sleep(100);
            waited += 100;
        }

        if (_service.IsCleaning)
        {
            // 设计要求：Helper 仍未退出时不得声称已完整退出。
            Log.Error("Exit aborted: cleanup/helper still running after wait budget");
            _notifyIcon.Text = "Desktop Tools — 无法完成退出";
            System.Windows.MessageBox.Show(
                "无法完成退出：清理或提升的 Helper 仍未结束。请稍后再试，或等待 Helper 自行退出。",
                "Desktop Tools",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        System.Windows.Application.Current?.Shutdown();
    }

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // 状态发布统一回到 UI 线程，避免在后台事件里直接触碰 NotifyIcon。
        void Apply()
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

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.Invoke(Apply);
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
