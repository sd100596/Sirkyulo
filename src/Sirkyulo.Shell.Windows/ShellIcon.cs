using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sirkyulo.Shell.Windows;

/// <summary>
/// Extracts high-resolution icons through IShellItemImageFactory.
///
/// Deliberately not SHGetFileInfo: that tops out at the 32px system image list, which looks
/// like mush at the 64px the ring draws at. IShellItemImageFactory reaches the 256px assets
/// that modern app shortcuts actually ship.
/// </summary>
public static class ShellIcon
{
    private const int SiigbfBiggerSizeOk = 0x00000001;
    private const int SiigbfIconOnly = 0x00000004;

    private static readonly Guid IidShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// Returns a frozen, alpha-correct bitmap for any shell-addressable path, or null if the
    /// shell has nothing to offer (dead target, unreadable file, unsupported location).
    /// </summary>
    public static BitmapSource? Extract(string path, int size = 96)
    {
        object? factoryObj = null;
        var hbitmap = nint.Zero;
        try
        {
            SHCreateItemFromParsingName(path, nint.Zero, IidShellItemImageFactory, out factoryObj);
            if (factoryObj is not IShellItemImageFactory factory) return null;

            factory.GetImage(new Size(size, size), SiigbfIconOnly | SiigbfBiggerSizeOk, out hbitmap);
            return hbitmap == nint.Zero ? null : FromHBitmap(hbitmap);
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        finally
        {
            if (hbitmap != nint.Zero) DeleteObject(hbitmap);
            if (factoryObj is not null) Marshal.FinalReleaseComObject(factoryObj);
        }
    }

    /// <summary>
    /// Copies the DIB bits out by hand rather than using Imaging.CreateBitmapSourceFromHBitmap.
    /// That helper reports the surface as Bgr32 and throws the alpha channel away, which leaves
    /// every rounded or transparent icon sitting on a black square.
    /// </summary>
    private static BitmapSource? FromHBitmap(nint hbitmap)
    {
        // Ask for the full DIBSECTION, not just the BITMAP. The BITMAP struct reports height
        // as an unsigned magnitude and hides row order entirely; only the BITMAPINFOHEADER
        // inside DIBSECTION carries the sign that says which way up the rows are stored.
        var isDibSection = GetObject(hbitmap, Marshal.SizeOf<DibSection>(), out DibSection dib)
                           == Marshal.SizeOf<DibSection>();

        var bm = isDibSection ? dib.dsBm : default;
        if (!isDibSection && GetObject(hbitmap, Marshal.SizeOf<Bitmap>(), out bm) == 0) return null;

        if (bm.bmBitsPixel != 32 || bm.bmBits == nint.Zero)
        {
            // Not the premultiplied DIB section we expect; fall back rather than misread memory.
            var fallback = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hbitmap, nint.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }

        var stride = bm.bmWidthBytes;
        var buffer = new byte[stride * bm.bmHeight];
        Marshal.Copy(bm.bmBits, buffer, 0, buffer.Length);

        // A positive biHeight means the DIB is bottom-up: row 0 in memory is the *last* row
        // on screen. Copying it straight into a top-down BitmapSource renders the icon
        // upside down. Most shell icons come back top-down, which is why this only shows on
        // some of them.
        if (isDibSection && dib.dsBmih.biHeight > 0)
            buffer = FlipRows(buffer, stride, bm.bmHeight);

        var source = BitmapSource.Create(
            bm.bmWidth, bm.bmHeight, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
        source.Freeze();
        return source;
    }

    private static byte[] FlipRows(byte[] buffer, int stride, int height)
    {
        var flipped = new byte[buffer.Length];
        for (var row = 0; row < height; row++)
            Buffer.BlockCopy(buffer, row * stride, flipped, (height - 1 - row) * stride, stride);

        return flipped;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int cx;
        public int cy;
        public Size(int w, int h) { cx = w; cy = h; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;

        /// <summary>Positive means bottom-up row order; negative means top-down.</summary>
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

    [StructLayout(LayoutKind.Sequential)]
    private struct DibSection
    {
        public Bitmap dsBm;
        public BitmapInfoHeader dsBmih;
        public uint dsBitfields0;
        public uint dsBitfields1;
        public uint dsBitfields2;
        public nint dshSection;
        public uint dsOffset;
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        void GetImage(Size size, int flags, out nint phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        nint pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(nint hgdiobj, int cbBuffer, out Bitmap lpvObject);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(nint hgdiobj, int cbBuffer, out DibSection lpvObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint hObject);
}
