using System.Windows.Forms;

namespace FoxholeLogiHub.App.Services;

/// <summary>
/// Notifications Windows (toasts natifs Win 10/11 via NotifyIcon). L'icône de zone de
/// notification reste visible tant que l'app tourne — c'est elle qui porte les bulles.
/// </summary>
public sealed class Notifier : IDisposable
{
    private readonly NotifyIcon _icon;

    public bool Enabled { get; set; } = true;

    public Notifier()
    {
        System.Drawing.Icon icon;
        try
        {
            icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                ?? System.Drawing.SystemIcons.Application;
        }
        catch
        {
            icon = System.Drawing.SystemIcons.Application;
        }

        _icon = new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "FoxholeLogiHub",
        };
    }

    /// <summary>Affiche un toast (meilleur effort, jamais bloquant).</summary>
    public void Show(string title, string message)
    {
        if (!Enabled)
            return;
        try
        {
            _icon.ShowBalloonTip(6000, title, message.Length > 200 ? message[..200] + "…" : message,
                ToolTipIcon.Info);
        }
        catch
        {
            // un toast raté ne doit jamais gêner l'app
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
