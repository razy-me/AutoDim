using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoDim.App.Shell;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly Icon _activeIcon;
    private readonly Icon _pausedIcon;
    private bool _disposed;

    public event Action? TogglePauseRequested;
    public event Action? OpenSettingsRequested;
    public event Action? OpenZonePickerRequested;
    public event Action? ExitRequested;

    public TrayController()
    {
        _activeIcon = CreateTrayIcon(active: true);
        _pausedIcon = CreateTrayIcon(active: false);

        _toggleItem = new ToolStripMenuItem("AutoDim aktiv") { Checked = true };
        _toggleItem.Click += (_, _) => TogglePauseRequested?.Invoke();

        var settingsItem = new ToolStripMenuItem("Einstellungen...");
        settingsItem.Click += (_, _) => OpenSettingsRequested?.Invoke();

        var zonePickerItem = new ToolStripMenuItem("Zone visuell hinzufügen...");
        zonePickerItem.Click += (_, _) => OpenZonePickerRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("Beenden");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(zonePickerItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _activeIcon,
            Text = "AutoDim — Aktiv",
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke();
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Middle)
            {
                TogglePauseRequested?.Invoke();
            }
        };
    }

    public void UpdatePauseState(bool isPaused)
    {
        if (_disposed) return;
        _toggleItem.Checked = !isPaused;
        _toggleItem.Text = isPaused ? "Pausiert (Klicken zum Aktivieren)" : "AutoDim aktiv";
        _notifyIcon.Icon = isPaused ? _pausedIcon : _activeIcon;
        _notifyIcon.Text = isPaused ? "AutoDim — Pausiert" : "AutoDim — Aktiv";
    }

    private static Icon CreateTrayIcon(bool active)
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var bgBrush = new SolidBrush(active ? Color.FromArgb(24, 24, 30) : Color.FromArgb(65, 65, 72));
        using var borderPen = new Pen(active ? Color.FromArgb(110, 180, 255) : Color.FromArgb(150, 150, 155), 2f);
        g.FillEllipse(bgBrush, 2, 2, 28, 28);
        g.DrawEllipse(borderPen, 2, 2, 28, 28);

        using var moonBrush = new SolidBrush(active ? Color.FromArgb(220, 235, 255) : Color.FromArgb(165, 165, 170));
        g.FillEllipse(moonBrush, 8, 8, 16, 16);
        using var cutoutBrush = new SolidBrush(active ? Color.FromArgb(24, 24, 30) : Color.FromArgb(65, 65, 72));
        g.FillEllipse(cutoutBrush, 13, 6, 14, 14);

        nint hIcon = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _activeIcon.Dispose();
        _pausedIcon.Dispose();
    }
}
