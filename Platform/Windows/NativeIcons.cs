using System;
using System.Runtime.InteropServices;

namespace StreamDak.Platform.Windows;

/// <summary>
/// Icône d'un fichier (programme, raccourci, document) dans sa plus grande taille : liste d'images système « jumbo » (256 px), sinon « très grande » (48 px).
/// <c>ExtractAssociatedIcon</c> ne rend que 32 px, flou une fois agrandi sur une touche. Un raccourci donne l'icône de sa cible, sans la flèche.
/// </summary>
internal static class NativeIcons
{
    const uint SHGFI_SYSICONINDEX = 0x4000;
    const int SHIL_EXTRALARGE = 2, SHIL_JUMBO = 4;
    const int ILD_TRANSPARENT = 0x1;
    static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IImageList
    {
        // Ordre de la vtable de commctrl.h : seules les entrées avant GetIcon sont déclarées, sans être appelées
        [PreserveSig] int Add(IntPtr image, IntPtr mask, out int index);
        [PreserveSig] int ReplaceIcon(int i, IntPtr icon, out int index);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int i, IntPtr image, IntPtr mask);
        [PreserveSig] int AddMasked(IntPtr image, int mask, out int index);
        [PreserveSig] int Draw(IntPtr parameters);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr icon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll")] static extern int SHGetImageList(int list, ref Guid riid, out IImageList images);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);

    /// <summary>Icône la plus grande du fichier (HICON à libérer par <see cref="Destroy"/>), ou <see cref="IntPtr.Zero"/>.</summary>
    public static IntPtr Largest(string path)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX) == IntPtr.Zero) return IntPtr.Zero;
        foreach (var list in new[] { SHIL_JUMBO, SHIL_EXTRALARGE })
        {
            var iid = IID_IImageList;
            if (SHGetImageList(list, ref iid, out var images) != 0 || images == null) continue;
            try
            {
                if (images.GetIcon(info.iIcon, ILD_TRANSPARENT, out var icon) == 0 && icon != IntPtr.Zero) return icon;
            }
            finally { Marshal.ReleaseComObject(images); }
        }
        return IntPtr.Zero;
    }

    /// <summary>Libère une icône rendue par <see cref="Largest"/>.</summary>
    public static void Destroy(IntPtr icon)
    {
        if (icon != IntPtr.Zero) DestroyIcon(icon);
    }
}
