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

    /// <summary>Double-clic ou « Ouvrir » : restaurer la fenêtre principale.</summary>
    public event Action? OpenRequested;

    /// <summary>« Quitter » depuis le menu de l'icône (vraie fermeture, même en mode tray).</summary>
    public event Action? ExitRequested;

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

        var menu = new ContextMenuStrip();
        menu.Items.Add("Ouvrir FoxholeLogiHub", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "FoxholeLogiHub",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
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
