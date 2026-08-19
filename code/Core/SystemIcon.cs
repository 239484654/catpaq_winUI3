// 系统图标提取（照抄 Files 4.2.3 的 Win32Helper.GetIcon）：
//  1. 优先 IShellItemImageFactory.GetImage(指定尺寸) —— 拿高清、正确的真实图标
//     （此电脑用 Files 的 "Shell:MyComputerFolder" 路径，取到的才是"此电脑"专用图标）
//  2. 回退 SHGetFileInfo + GetDIBits（Files 同样有 SHGetFileInfo fallback）
// 全程后台 STA 线程执行（Files 用 STATask），UI 线程只做 SoftwareBitmap→PNG→BitmapImage。
using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace Catpaq.Core;

public static class SystemIcon
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, ref BITMAP p);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines,
        [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr h);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint DIB_RGB_COLORS = 0;

    // 此电脑用 Files 的 Constants.MyComputerPath，IShellItemImageFactory 才能取到正确的专用图标
    public const string ThisPcShellPath = "Shell:MyComputerFolder";
    // 快速访问（frequent places）
    public const string QuickAccessShellPath = "::{22877A6D-37A1-461A-91B0-DBDA5AAEBC99}";

    /// <summary>
    /// 提取系统图标并转为 BitmapImage（失败返回 null，不抛异常）。
    /// 照抄 Files：IShellItemImageFactory 按指定尺寸取高清图标，失败回退 SHGetFileInfo。
    /// small 为 true 时取 16x16，否则取 48x48（显示端 20x20 由大图缩小，细节更清晰，
    /// 尤其 C 盘系统盘这类细节密集图标不会发虚）。
    /// </summary>
    public static async Task<BitmapImage?> GetIconAsync(string shellPath, bool isFolder, bool small = false)
    {
        try
        {
            var size = small ? 16 : 48;

            // 后台 STA 线程里提取图标像素（照抄 Files：GetIcon 在 STATask 中执行）
            var pixels = await StatTask.Run(() => ExtractIconPixels(shellPath, isFolder, size));
            if (pixels is null)
                return null;

            var (w, h, bgra) = pixels.Value;
            return await PixelsToBitmapAsync(w, h, bgra);
        }
        catch
        {
            return null;
        }
    }

    // 照抄 Files 的 Win32Helper.GetIcon 主路径：IShellItemImageFactory 优先，SHGetFileInfo 兜底。
    // 返回 (宽, 高, BGRA 像素)；全部失败返回 null。
    private static (int w, int h, byte[] bgra)? ExtractIconPixels(string path, bool isFolder, int size)
    {
        // 规范化 shell 路径：裸 CLSID（::{...}）补上 shell: 前缀，
        // IShellItemImageFactory 和 SHGetFileInfo 都需要完整形式才能解析
        if (path.StartsWith("::{", StringComparison.Ordinal))
            path = $"shell:{path}";

        // --- 1) IShellItemImageFactory.GetImage（高清、路径正确则图标正确） ---
        try
        {
            var shellItem = GetShellItemFromPath(path);
            if (shellItem?.IShellItem is Shell32.IShellItemImageFactory factory)
            {
                var flags = Shell32.SIIGBF.SIIGBF_BIGGERSIZEOK | Shell32.SIIGBF.SIIGBF_ICONONLY;
                var hr = factory.GetImage(new SIZE(size, size), flags, out Vanara.PInvoke.Gdi32.SafeHBITMAP hbitmap);
                if (hr == HRESULT.S_OK && !hbitmap.IsNull)
                {
                    try
                    {
                        var px = HBitmapToPixels(hbitmap.DangerousGetHandle());
                        if (px is not null)
                            return px;
                    }
                    finally
                    {
                        hbitmap.Dispose();
                    }
                }
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            // 照抄 Files：失败走 SHGetFileInfo fallback
        }

        // --- 2) SHGetFileInfo fallback（Files 同款逻辑） ---
        try
        {
            var hIcon = ExtractHIcon(path, isFolder, size <= 16);
            if (hIcon == IntPtr.Zero)
                return null;
            try
            {
                if (GetIconInfo(hIcon, out var ii))
                {
                    IntPtr hbm = ii.hbmColor != IntPtr.Zero ? ii.hbmColor : ii.hbmMask;
                    try
                    {
                        return HBitmapToPixels(hbm);
                    }
                    finally
                    {
                        if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                        if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                    }
                }
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
        catch
        {
        }
        return null;
    }

    // 照抄 Files 的 ShellFolderExtensions.GetShellItemFromPathOrPIDL（:: 开头补 shell: 前缀）
    private static ShellItem? GetShellItemFromPath(string path)
    {
        if (path.StartsWith("::{", StringComparison.Ordinal))
            path = $"shell:{path}";
        return Safety(() => ShellItem.Open(path));
    }

    private static IntPtr ExtractHIcon(string path, bool isFolder, bool small)
    {
        var shfi = new SHFILEINFO();
        var flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | (small ? SHGFI_SMALLICON : 0);
        var attrs = isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        SHGetFileInfo(path, attrs, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        return shfi.hIcon;
    }

    // HBITMAP → BGRA 像素（自上而下，32bpp）
    private static (int w, int h, byte[] bgra)? HBitmapToPixels(IntPtr hbm)
    {
        var bmp = new BITMAP();
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), ref bmp) == 0)
            return null;
        int w = bmp.bmWidth, h = bmp.bmHeight;
        if (w <= 0 || h <= 0)
            return null;

        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,   // 自上而下的 DIB
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };
        var pixels = new byte[w * h * 4];

        var hdc = CreateCompatibleDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            return null;
        try
        {
            var old = SelectObject(hdc, hbm);
            GetDIBits(hdc, hbm, 0, (uint)h, pixels, ref bmi, DIB_RGB_COLORS);
            SelectObject(hdc, old);
        }
        finally
        {
            DeleteDC(hdc);
        }
        return (w, h, pixels);
    }

    // BGRA 像素 → SoftwareBitmap → PNG 字节 → BitmapImage（UI 线程）
    private static async Task<BitmapImage> PixelsToBitmapAsync(int w, int h, byte[] bgra)
    {
        var sw = new SoftwareBitmap(BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        sw.CopyFromBuffer(bgra.AsBuffer());

        var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(sw);
        await encoder.FlushAsync();
        stream.Seek(0);

        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static T? Safety<T>(Func<T?> action) where T : class
    {
        try { return action(); }
        catch { return null; }
    }
}
