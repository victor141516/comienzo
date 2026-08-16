namespace Comienzo.Services;

internal sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public TrayService(Action show, Action exit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Comienzo", null, (_, _) => show());
        var startup = new System.Windows.Forms.ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupService.IsEnabled()
        };
        startup.CheckedChanged += (_, _) =>
        {
            try { StartupService.SetEnabled(startup.Checked); }
            catch (Exception exception)
            {
                startup.Checked = StartupService.IsEnabled();
                System.Windows.Forms.MessageBox.Show(exception.Message, "Comienzo",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            }
        };
        menu.Items.Add(startup);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Comienzo — alternative Start menu",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => show();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
