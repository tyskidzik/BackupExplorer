using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BackupExplorer.App.Services;

public static class ShellIconService
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _folderIcon;
    private static ImageSource? _driveIcon;

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static ImageSource GetFolderIcon()
    {
        if (_folderIcon != null) return _folderIcon;

        var shinfo = new SHFILEINFO();
        IntPtr res = SHGetFileInfo("", FILE_ATTRIBUTE_DIRECTORY, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
        if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
        {
            try
            {
                _folderIcon = CreateFrozenBitmap(shinfo.hIcon);
                return _folderIcon;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
        return CreateFallbackFolderIcon();
    }

    public static ImageSource GetDriveIcon(string driveRoot = "C:\\")
    {
        if (_driveIcon != null) return _driveIcon;

        var shinfo = new SHFILEINFO();
        IntPtr res = SHGetFileInfo(driveRoot, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_SMALLICON);
        if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
        {
            try
            {
                _driveIcon = CreateFrozenBitmap(shinfo.hIcon);
                return _driveIcon;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
        return GetFolderIcon();
    }

    public static ImageSource GetFileIcon(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        bool isSpecialExt = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase);

        // For common extensions (.txt, .pdf, .cs, .png, etc.), cache by extension.
        // For .exe / .ico, cache by full path.
        string cacheKey = isSpecialExt ? filePath : ext;
        if (string.IsNullOrEmpty(cacheKey)) cacheKey = "__no_ext__";

        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var shinfo = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;
        uint attrs = FILE_ATTRIBUTE_NORMAL;

        if (!isSpecialExt)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        IntPtr res = SHGetFileInfo(filePath, attrs, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
        if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
        {
            try
            {
                var iconSource = CreateFrozenBitmap(shinfo.hIcon);
                Cache[cacheKey] = iconSource;
                return iconSource;
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }

        return GetFolderIcon();
    }

    private static ImageSource CreateFrozenBitmap(IntPtr hIcon)
    {
        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
            hIcon,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        bitmapSource.Freeze();
        return bitmapSource;
    }

    private static ImageSource CreateFallbackFolderIcon()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.Goldenrod, null, new Rect(0, 0, 16, 16));
        }
        var rtb = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
