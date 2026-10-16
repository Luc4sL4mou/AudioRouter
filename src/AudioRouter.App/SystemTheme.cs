using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AudioRouter.App;

internal static class SystemTheme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static bool? _lastAppliedLight;

    /// <summary>Força um tema (true = claro) em vez de ler o do sistema. Usado só em testes (--theme=).</summary>
    public static bool? Override { get; set; }

    public static bool AppsUseLightTheme => Override ?? ReadFlag("AppsUseLightTheme");
    public static bool SystemUsesLightTheme => Override ?? ReadFlag("SystemUsesLightTheme");

    /// <summary>
    /// Aplica o tema Fluent (claro/escuro) do sistema ao aplicativo. Chamado a cada abertura do flyout,
    /// então uma troca de tema do Windows aparece na próxima vez que ele abrir.
    /// </summary>
    public static void ApplyToApplication()
    {
        bool light = AppsUseLightTheme;
        if (_lastAppliedLight == light) return;
        _lastAppliedLight = light;

        var app = System.Windows.Application.Current;
        app.ThemeMode = light ? ThemeMode.Light : ThemeMode.Dark;

        // Película sobre o acrílico do DWM: dá contraste ao texto sobre qualquer papel de parede.
        // Sem suporte a backdrop (antes do Windows 11 22H2) ela fica opaca, senão a janela seria transparente.
        byte alpha = SupportsSystemBackdrop ? (byte)0xB8 : (byte)0xFF;
        var tint = light ? Color.FromArgb(alpha, 0xF3, 0xF3, 0xF3) : Color.FromArgb(alpha, 0x20, 0x20, 0x20);
        SetBrush(app, "FlyoutTintBrush", tint);

        // Realces de hover/pressionado (translúcidos, para funcionarem sobre o acrílico).
        SetBrush(app, "SubtleHoverBrush", light ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        SetBrush(app, "SubtlePressedBrush", light ? Color.FromArgb(0x08, 0, 0, 0) : Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
    }

    private static void SetBrush(System.Windows.Application app, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        app.Resources[key] = brush;
    }

    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE existe a partir do Windows 11 22H2 (build 22621).</summary>
    public static bool SupportsSystemBackdrop => Environment.OSVersion.Version.Build >= 22621;

    private static bool ReadFlag(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) is int v && v != 0;
        }
        catch { return false; }
    }
}
