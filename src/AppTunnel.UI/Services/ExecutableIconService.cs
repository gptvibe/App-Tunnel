using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AppTunnel.UI.Services;

public sealed class ExecutableIconService
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? GetIcon(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        var cacheKey = executablePath;
        if (_cache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        ImageSource? iconSource = null;

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is not null)
            {
                using var bitmap = icon.ToBitmap();
                var bitmapHandle = bitmap.GetHbitmap();

                try
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        bitmapHandle,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(32, 32));
                    bitmapSource.Freeze();
                    iconSource = bitmapSource;
                }
                finally
                {
                    _ = DeleteObject(bitmapHandle);
                }
            }
        }
        catch
        {
            iconSource = null;
        }

        _cache[cacheKey] = iconSource;
        return iconSource;
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr objectHandle);
}