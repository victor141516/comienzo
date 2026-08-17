using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Comienzo.Models;

namespace Comienzo.Services;

internal static class IconService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static long _cacheMissCount;

    internal static long CacheMissCount => Interlocked.Read(ref _cacheMissCount);

    public static Task PrewarmAsync(IEnumerable<CatalogItem> items) => Task.Run(() =>
    {
        foreach (CatalogItem item in items) _ = Get(item);
    });

    public static ImageSource? Get(CatalogItem item)
    {
        bool unresolvedShortcut = item.IconSource.Length == 0 &&
            (item.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
             item.Target.EndsWith(".url", StringComparison.OrdinalIgnoreCase));
        string key = item.IconSource.Length > 0 ? item.IconSource : unresolvedShortcut ? "" : item.Target;
        string cacheKey = $"{key}|{item.IconIndex}";
        return Cache.GetOrAdd(cacheKey, _ =>
        {
            Interlocked.Increment(ref _cacheMissCount);
            return item.Kind switch
            {
                ItemKind.Setting => CreateGlyph("\uE713", System.Windows.Media.Color.FromRgb(96, 205, 255)),
                ItemKind.Calculator => CreateGlyph("\uE8EF", System.Windows.Media.Color.FromRgb(123, 231, 135)),
                _ => ExtractShellIcon(key, item.IconIndex)
            };
        });
    }

    private static ImageSource? ExtractShellIcon(string path, int iconIndex)
    {
        if (string.IsNullOrWhiteSpace(path)) return CreateGlyph("\uE7C3", Colors.White);
        if (!path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && path.Contains('!') && !File.Exists(path))
            path = $"shell:AppsFolder\\{path}";
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            ImageSource? shellItemIcon = ExtractShellItemIcon(path);
            if (shellItemIcon is not null) return shellItemIcon;
        }
        // An explicit icon location commonly uses index 0. SHGetFileInfo would return the file
        // association icon for standalone .ico files instead of the image stored in the file.
        if (File.Exists(path))
        {
            ImageSource? fileIcon = ExtractFileIcon(path, iconIndex);
            if (fileIcon is not null) return fileIcon;
        }
        SHFILEINFO info = default;
        uint flags = SHGFI_ICON | SHGFI_LARGEICON;
        if (!path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !File.Exists(path))
            flags |= SHGFI_USEFILEATTRIBUTES;
        IntPtr result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return CreateGlyph("\uE7C3", Colors.White);
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    internal static ImageSource? ExtractFileIcon(string path, int iconIndex)
    {
        var largeIcons = new IntPtr[1];
        uint count = ExtractIconEx(path, iconIndex, largeIcons, null, 1);
        if (count == 0 || largeIcons[0] == IntPtr.Zero) return null;
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(largeIcons[0], Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(largeIcons[0]);
        }
    }

    private static ImageSource? ExtractShellItemIcon(string parsingName)
    {
        IShellItemImageFactory? factory = null;
        try
        {
            Guid interfaceId = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref interfaceId, out factory);
            int result = factory.GetImage(new NativeSize { Width = 32, Height = 32 },
                ShellItemImageFlags.IconOnly | ShellItemImageFlags.BiggerSizeOk, out IntPtr bitmap);
            if (result < 0 || bitmap == IntPtr.Zero) return null;
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero,
                    Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (factory is not null && Marshal.IsComObject(factory)) Marshal.FinalReleaseComObject(factory);
        }
    }

    private static ImageSource CreateGlyph(string glyph, System.Windows.Media.Color color)
    {
        var group = new DrawingGroup();
        using (DrawingContext context = group.Open())
        {
            var text = new FormattedText(glyph, System.Globalization.CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe Fluent Icons"), 24,
                new SolidColorBrush(color), 1.0);
            context.DrawText(text, new System.Windows.Point((32 - text.Width) / 2, (32 - text.Height) / 2));
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;

    [Flags]
    private enum ShellItemImageFlags
    {
        BiggerSizeOk = 0x1,
        IconOnly = 0x4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, ShellItemImageFlags flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi,
        uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory shellItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int iconIndex, IntPtr[] largeIcons,
        IntPtr[]? smallIcons, uint iconCount);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);
}
