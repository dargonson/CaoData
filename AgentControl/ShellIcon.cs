using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System;
using System.Drawing;
using System.IO;

public static class ShellIcon
{
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


    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const int SHIL_SMALL = 0;
    private const int SHIL_LARGE = 1;
    private const int SHIL_EXTRALARGE = 2;
    private const int SHIL_JUMBO = 4;
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct IMAGELISTDRAWPARAMS
    {
        public int cbSize;
        public IntPtr himl;
        public int i;
        public IntPtr hdcDst;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public int xBitmap;
        public int yBitmap;
        public int rgbBk;
        public int rgbFg;
        public int fStyle;
        public int dwRop;
        public int fState;
        public int Frame;
        public int crEffect;
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig]
        int GetIcon(int i, int flags, out IntPtr picon);
    }

    [DllImport("Shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(
    int iImageList,
    ref Guid riid,
    out IImageList ppv);


    [DllImport("Shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("User32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon GetSmallIcon(string path)
    {
        SHFILEINFO shinfo = new SHFILEINFO();

        uint flags = SHGFI_ICON | SHGFI_SMALLICON;

        if (Directory.Exists(path))
        {
            SHGetFileInfo(
                path,
                0,
                ref shinfo,
                (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                flags);
        }
        else
        {
            SHGetFileInfo(
                path,
                0x80,
                ref shinfo,
                (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                flags | SHGFI_USEFILEATTRIBUTES);
        }

        if (shinfo.hIcon == IntPtr.Zero)
            return SystemIcons.WinLogo;

        Icon icon = (Icon)Icon.FromHandle(shinfo.hIcon).Clone();
        DestroyIcon(shinfo.hIcon);

        return icon;
    }

    public static Icon GetSmallIcon(string path, bool isDirectory)
    {
        SHFILEINFO shinfo = new SHFILEINFO();
        uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;

        SHGetFileInfo(
            path,
            attributes,
            ref shinfo,
            (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
            flags);

        if (shinfo.hIcon == IntPtr.Zero)
            return SystemIcons.WinLogo;

        Icon icon = (Icon)Icon.FromHandle(shinfo.hIcon).Clone();
        DestroyIcon(shinfo.hIcon);

        return icon;
    }
}
