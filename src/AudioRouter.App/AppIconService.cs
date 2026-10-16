using System.Collections.Concurrent;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AudioRouter.App;

/// <summary>Ícone do executável como ImageSource congelado, em cache por caminho.</summary>
internal static class AppIconService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return Cache.GetOrAdd(path, Load);
    }

    private static ImageSource? Load(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch { return null; }
    }
}
