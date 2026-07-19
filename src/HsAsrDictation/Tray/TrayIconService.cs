using System.Drawing;
using System.IO;
using System.Windows.Forms;
using HsAsrDictation.Logging;
using HsAsrDictation.Notifications;
using HsAsrDictation.Services;

namespace HsAsrDictation.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly LocalLogService _logger;
    private readonly PrivilegeMode _privilegeMode;

    public TrayIconService(NotificationService notificationService, LocalLogService logger, PrivilegeMode privilegeMode)
    {
        _logger = logger;
        _privilegeMode = privilegeMode;

        _statusItem = new ToolStripMenuItem("状态：就绪")
        {
            Enabled = false
        };

        var toggleRecordingItem = new ToolStripMenuItem("开始/停止听写");
        toggleRecordingItem.Click += (_, _) => ToggleRecordingRequested?.Invoke(this, EventArgs.Empty);

        var settingsItem = new ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var modelItem = new ToolStripMenuItem("下载/重载模型");
        modelItem.Click += (_, _) => ModelDownloadRequested?.Invoke(this, EventArgs.Empty);

        ToolStripItem elevationItem = privilegeMode == PrivilegeMode.Administrator
            ? new ToolStripMenuItem("当前已是管理员模式")
            {
                Enabled = false
            }
            : new ToolStripMenuItem("以管理员模式重启");

        if (elevationItem is ToolStripMenuItem elevationMenuItem && privilegeMode != PrivilegeMode.Administrator)
        {
            elevationMenuItem.Click += (_, _) => RestartAsAdministratorRequested?.Invoke(this, EventArgs.Empty);
        }

        var logItem = new ToolStripMenuItem("打开日志目录");
        logItem.Click += (_, _) =>
        {
            System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(_logger.LogFilePath)!);
        };

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            _statusItem,
            new ToolStripSeparator(),
            toggleRecordingItem,
            settingsItem,
            modelItem,
            elevationItem,
            logItem,
            new ToolStripSeparator(),
            exitItem
        ]);

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "HsAsrDictation",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        notificationService.NotificationRaised += (_, message) =>
        {
            if (message.Icon == ToolTipIcon.Info)
            {
                return;
            }

            RunOnUiThread(() => _notifyIcon.ShowBalloonTip(3000, message.Title, message.Message, message.Icon));
        };
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? ModelDownloadRequested;

    public event EventHandler? ToggleRecordingRequested;

    public event EventHandler? RestartAsAdministratorRequested;

    public event EventHandler? ExitRequested;

    public void SetStatus(string statusText)
    {
        RunOnUiThread(() =>
        {
            _statusItem.Text = $"状态：{statusText}（{_privilegeMode.ToDisplayText()}）";
            _notifyIcon.Text = BuildNotifyIconText(statusText);
        });
    }

    public void Dispose()
    {
        RunOnUiThread(() =>
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private string BuildNotifyIconText(string statusText)
    {
        var text = $"HsAsrDictation - {statusText} - {_privilegeMode.ToShortDisplayText()}";
        return text.Length <= 63 ? text : text[..63];
    }
}
