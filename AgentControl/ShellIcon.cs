using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

public static class ShellIcon
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

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("User32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon GetSmallIcon(string path)
    {
        SHFILEINFO shinfo = new();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;

        if (Directory.Exists(path))
        {
            SHGetFileInfo(
                path,
                0,
                ref shinfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                flags);
        }
        else
        {
            SHGetFileInfo(
                path,
                FILE_ATTRIBUTE_NORMAL,
                ref shinfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                flags | SHGFI_USEFILEATTRIBUTES);
        }

        return CloneAndReleaseIcon(shinfo.hIcon);
    }

    public static Icon GetSmallIcon(string path, bool isDirectory)
    {
        SHFILEINFO shinfo = new();
        uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;

        SHGetFileInfo(
            path,
            attributes,
            ref shinfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);

        return CloneAndReleaseIcon(shinfo.hIcon);
    }

    private static Icon CloneAndReleaseIcon(IntPtr iconHandle)
    {
        if (iconHandle == IntPtr.Zero)
        {
            return SystemIcons.WinLogo;
        }

        Icon icon = (Icon)Icon.FromHandle(iconHandle).Clone();
        DestroyIcon(iconHandle);
        return icon;
    }
}
