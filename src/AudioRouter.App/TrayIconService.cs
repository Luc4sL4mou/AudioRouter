using System.Drawing;
using System.Windows.Forms;

namespace AudioRouter.App;

/// <summary>Ícone na bandeja: clique esquerdo dispara LeftClick, menu de contexto tem "Iniciar com o Windows" e "Sair".</summary>
internal sealed class TrayIconService : IDisposable
{
    private static readonly Uri IconUri = new("pack://application:,,,/Assets/AudioRouter.ico");

    private readonly NotifyIcon _icon;

    public event Action? LeftClick;
    public event Action? ExitRequested;

    /// <summary>Lido ao abrir o menu, para o item refletir o estado atual.</summary>
    public Func<bool>? GetStartWithWindows { get; init; }
    public event Action<bool>? StartWithWindowsChanged;

    public TrayIconService()
    {
        var menu = new ContextMenuStrip();
        var startItem = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true };
        // CheckOnClick inverte Checked antes do Click, então Checked já é o novo estado.
        startItem.Click += (_, _) => StartWithWindowsChanged?.Invoke(startItem.Checked);
        menu.Opening += (_, _) => startItem.Checked = GetStartWithWindows?.Invoke() ?? false;
        menu.Items.Add(startItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "AudioRouter",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) LeftClick?.Invoke();
        };
    }

    /// <summary>
    /// Carrega o ícone oficial no tamanho que a bandeja usa neste DPI (16/20/24/32 px). O .ico tem uma imagem
    /// para cada um, então não há redimensionamento (e, portanto, nem borrão).
    /// </summary>
    private static Icon LoadIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(IconUri);
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch { /* recurso ausente/corrompido: usa o ícone genérico abaixo */ }
        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Icon?.Dispose();
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
