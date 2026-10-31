using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Nova.App.Platform;

/// <summary>Notification-area icon: NOVA keeps running (hotkeys, detection) when its window is hidden.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(Action show, Action exit)
    {
        _icon = new Forms.NotifyIcon
        {
            Text = "NOVA Workspace",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Items.Add("Show NOVA", null, (_, _) => show());
        _icon.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _icon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => exit());
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) show(); };
    }

    public void Notify(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(2500);
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/nova.ico"));
            if (info is not null)
            {
                using Stream s = info.Stream;
                return new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (IOException) { }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
