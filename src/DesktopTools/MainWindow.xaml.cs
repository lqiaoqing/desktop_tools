using System.ComponentModel;
using System.Text;
using System.Windows;

namespace DesktopTools;

public partial class MainWindow : Window
{
    private readonly MemoryCleanupService _service;
    private readonly TrayController? _tray;

    public MainWindow(MemoryCleanupService service, TrayController? tray)
    {
        InitializeComponent();
        _service = service;
        _tray = tray;
        _service.StateChanged += OnServiceStateChanged;
        UpdateFromState();
        RefreshMemoryDisplay();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _service.StateChanged -= OnServiceStateChanged;
        base.OnClosed(e);
    }

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_service.TryBeginCleanup(out _))
        {
            return;
        }

        UpdateFromState();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service.IsCleaning)
        {
            return;
        }

        _service.RefreshSnapshot();
        RefreshMemoryDisplay();
    }

    private void OnServiceStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateFromState();
            RefreshMemoryDisplay();
        });
    }

    private void RefreshMemoryDisplay()
    {
        var snapshot = _service.CurrentSnapshot;
        if (snapshot is null || !snapshot.IsAvailable)
        {
            TotalMemoryText.Text = "总内存：不可用";
            AvailableMemoryText.Text = "可用内存：不可用";
            MemoryLoadText.Text = $"内存负载：不可用{(snapshot?.QueryError is { } err ? $"（{err}）" : string.Empty)}";
            return;
        }

        TotalMemoryText.Text = $"总内存：{FormatBytes(snapshot.TotalPhysicalMemory)}";
        AvailableMemoryText.Text = $"可用内存：{FormatBytes(snapshot.AvailablePhysicalMemory)}";
        MemoryLoadText.Text = $"内存负载：{snapshot.MemoryLoad}%";
    }

    private void UpdateFromState()
    {
        bool cleaning = _service.IsCleaning;
        CleanButton.IsEnabled = !cleaning && !_service.ExitRequested;
        RefreshButton.IsEnabled = !cleaning && !_service.ExitRequested;
        CleaningIndicator.Text = cleaning ? "正在清理…" : string.Empty;

        if (_service.LastCleanupResult is { } result)
        {
            StatusText.Text = BuildStatusText(result);
        }
    }

    private static string BuildStatusText(CleanupResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"状态：{DescribeOverall(result.OverallStatus)}");
        sb.AppendLine($"自身 Working Set：{DescribeOp(result.CurrentProcessWorkingSet)}");
        sb.AppendLine($"System File Cache：{DescribeOp(result.SystemFileCache)}");

        if (result.BeforeQueryError is { } beforeErr)
        {
            sb.AppendLine($"清理前查询错误：{beforeErr}");
        }

        if (result.AfterQueryError is { } afterErr)
        {
            sb.AppendLine($"清理后查询错误：{afterErr}（清理后内存数据不可用）");
        }
        else if (result.BeforeSnapshot is { } before && result.AfterSnapshot is { } after)
        {
            sb.AppendLine($"清理前可用：{FormatBytes(before.AvailablePhysicalMemory)}");
            sb.AppendLine($"清理后可用：{FormatBytes(after.AvailablePhysicalMemory)}");
            long delta = (long)after.AvailablePhysicalMemory - (long)before.AvailablePhysicalMemory;
            sb.AppendLine($"系统可用内存变化：{FormatDelta(delta)}（非本工具精确释放量）");
        }

        if (result.FailedStage is { } stage)
        {
            sb.AppendLine($"失败阶段：{stage}");
        }

        sb.Append($"完成时间：{result.CompletedAt:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    private static string DescribeOverall(CleanupOverallStatus status) => status switch
    {
        CleanupOverallStatus.Success => "成功",
        CleanupOverallStatus.PartialSuccess => "部分成功",
        CleanupOverallStatus.Failed => "失败",
        _ => status.ToString()
    };

    private static string DescribeOp(OperationResult op)
    {
        return op.Status switch
        {
            OperationStatus.Success => "成功",
            OperationStatus.Failed => $"失败（{op.Error}）",
            OperationStatus.Skipped => $"已跳过（{op.Error}）",
            _ => op.Status.ToString()
        };
    }

    private static string FormatBytes(ulong bytes)
    {
        double gb = bytes / (1024.0 * 1024.0 * 1024.0);
        if (gb >= 1)
        {
            return $"{gb:F2} GB";
        }

        return $"{bytes / (1024.0 * 1024.0):F0} MB";
    }

    private static string FormatDelta(long delta)
    {
        if (delta == 0)
        {
            return "0";
        }

        double abs = Math.Abs(delta);
        string sign = delta > 0 ? "+" : "-";
        if (abs >= 1024L * 1024 * 1024)
        {
            return $"{sign}{abs / (1024.0 * 1024 * 1024):F2} GB";
        }

        return $"{sign}{abs / (1024.0 * 1024):F0} MB";
    }
}
