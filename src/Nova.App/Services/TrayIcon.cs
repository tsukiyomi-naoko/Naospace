using Forms = System.Windows.Forms;

namespace Nova.App.Services;

/// <summary>Notification-area icon so NOVA can stay available all day without occupying the taskbar.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(Action toggle, Action show, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show NOVA Workspace", null, (_, _) => show());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        System.Drawing.Icon? appIcon = null;
        try
        {
            if (Environment.ProcessPath != null) appIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        }
        catch { }

        _icon = new Forms.NotifyIcon
        {
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            Text = "NOVA Workspace",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) toggle(); };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
