using System.Runtime.InteropServices;

namespace Loadout.Icons;

/// <summary>Win32 and COM declarations used to pull icons out of the shell.</summary>
internal static partial class IconsNative
{
    public const int SiigbfBiggerSizeOk = 0x1;
    public const int SiigbfIconOnly = 0x4;

    private const int BiRgb = 0;
    private const uint DibRgbColors = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Size
    {
        public int Cx;
        public int Cy;

        public Size(int cx, int cy)
        {
            Cx = cx;
            Cy = cy;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Bitmap
    {
        public int BmType;
        public int BmWidth;
        public int BmHeight;
        public int BmWidthBytes;
        public ushort BmPlanes;
        public ushort BmBitsPixel;
        public IntPtr BmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint BiSize;
        public int BiWidth;
        public int BiHeight;
        public ushort BiPlanes;
        public ushort BiBitCount;
        public uint BiCompression;
        public uint BiSizeImage;
        public int BiXPelsPerMeter;
        public int BiYPelsPerMeter;
        public uint BiClrUsed;
        public uint BiClrImportant;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
#pragma warning disable SYSLIB1096 // ComImport is the simplest correct option for this single shell interface.
    internal interface IShellItemImageFactory
#pragma warning restore SYSLIB1096
    {
        [PreserveSig]
        int GetImage(Size size, int flags, out IntPtr phbm);
    }

    internal static readonly Guid IidShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [LibraryImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, IntPtr bindContext, in Guid riid, out IntPtr ppv);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static partial int GetObject(IntPtr handle, int count, out Bitmap bitmap);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte* bits, ref BitmapInfoHeader info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    /// <summary>
    /// Asks the shell for an icon image. Returns top-down 32-bit BGRA pixels (straight, not premultiplied, alpha in practice),
    /// or null when the shell can't make one. Releases every COM object and the HBITMAP.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height)? GetShellImage(string parsingName, int px)
    {
        var unknown = IntPtr.Zero;
        object? comObject = null;
        var hbitmap = IntPtr.Zero;
        try
        {
            var iid = IidShellItemImageFactory;
            if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, in iid, out unknown) < 0 || unknown == IntPtr.Zero)
            {
                return null;
            }

            comObject = Marshal.GetObjectForIUnknown(unknown);
            if (comObject is not IShellItemImageFactory factory)
            {
                return null;
            }

            var hr = factory.GetImage(new Size(px, px), SiigbfIconOnly | SiigbfBiggerSizeOk, out hbitmap);
            if (hr < 0 || hbitmap == IntPtr.Zero)
            {
                return null;
            }

            return ReadPixels(hbitmap);
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
            {
                DeleteObject(hbitmap);
            }

            if (comObject is not null)
            {
                Marshal.ReleaseComObject(comObject);
            }

            if (unknown != IntPtr.Zero)
            {
                Marshal.Release(unknown);
            }
        }
    }

    private static unsafe (byte[] Pixels, int Width, int Height)? ReadPixels(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, sizeof(Bitmap), out var bmp) == 0 || bmp.BmWidth <= 0 || bmp.BmHeight <= 0)
        {
            return null;
        }

        var header = new BitmapInfoHeader
        {
            BiSize = (uint)sizeof(BitmapInfoHeader),
            BiWidth = bmp.BmWidth,
            BiHeight = -bmp.BmHeight, // negative = top-down, so no row flipping is needed
            BiPlanes = 1,
            BiBitCount = 32,
            BiCompression = BiRgb,
        };

        var pixels = new byte[checked(bmp.BmWidth * bmp.BmHeight * 4)];
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            int lines;
            fixed (byte* p = pixels)
            {
                lines = GetDIBits(dc, hbitmap, 0, (uint)bmp.BmHeight, p, ref header, DibRgbColors);
            }

            return lines == 0 ? null : (pixels, bmp.BmWidth, bmp.BmHeight);
        }
        finally
        {
            DeleteDC(dc);
        }
    }
}
