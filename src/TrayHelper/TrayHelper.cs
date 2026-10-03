using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CloudRedirect.TrayHelper;

public sealed class WinFormsTrayHelper : IDisposable
{
    private NotifyIcon? _notifyIcon;

    public bool Initialize(
        string tooltip,
        string? iconPath,
        Action onOpen,
        Action onExit,
        Action onToggleOrRestore)
    {
        try
        {
            Icon? icon = null;
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try { icon = new Icon(iconPath); } catch { }
            }
            if (icon == null)
            {
                try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            }
            if (icon == null)
            {
                var launcher = Environment.GetEnvironmentVariable("CLOUDREDIRECT_LAUNCHER_PATH");
                if (!string.IsNullOrEmpty(launcher) && File.Exists(launcher))
                {
                    try { icon = Icon.ExtractAssociatedIcon(launcher); } catch { }
                }
            }
            if (icon == null)
            {
                icon = SystemIcons.Application;
            }

            var menu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Open CloudRedirect", null, (s, e) => onOpen());
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => onExit());

            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip,
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    onToggleOrRestore();
                }
            };
            _notifyIcon.DoubleClick += (s, e) => onOpen();

            return _notifyIcon.Visible;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("WinFormsTrayHelper failed: " + ex);
            return false;
        }
    }


    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
