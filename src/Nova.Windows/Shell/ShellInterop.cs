using System.IO;
using System.Runtime.InteropServices;
using Nova.Windows.Interop;

namespace Nova.Windows.Shell;

// Classic COM interop definitions for the documented Windows Shell interfaces used by NOVA.
// Method order MUST match the vtable order in ShObjIdl_core.h exactly.

[StructLayout(LayoutKind.Sequential)]
public struct FOLDERSETTINGS
{
    public uint ViewMode;
    public uint fFlags;
}

public enum SIGDN : uint
{
    NORMALDISPLAY = 0,
    PARENTRELATIVEPARSING = 0x80018001,
    DESKTOPABSOLUTEPARSING = 0x80028000,
    PARENTRELATIVEEDITING = 0x80031001,
    DESKTOPABSOLUTEEDITING = 0x8004c000,
    FILESYSPATH = 0x80058000,
    URL = 0x80068000,
    PARENTRELATIVEFORADDRESSBAR = 0x8007c001,
    PARENTRELATIVE = 0x80080001,
}

[Flags]
public enum ExplorerBrowserOptions : uint
{
    None = 0,
    NavigateOnce = 0x1,
    ShowFrames = 0x2,
    AlwaysNavigate = 0x4,
    NoTravelLog = 0x8,
    NoWrapperWindow = 0x10,
    HtmlSharepointView = 0x20,
    NoBorder = 0x40,
    NoPersistViewState = 0x80,
}

[ComImport, Guid("DFD3B6B5-C10C-4BE9-85F6-A66969F402F6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerBrowser
{
    void Initialize(IntPtr hwndParent, [In] ref RECT prc, [In] ref FOLDERSETTINGS pfs);
    void Destroy();
    void SetRect(IntPtr phdwp, RECT rcBrowser);
    void SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string pszPropertyBag);
    void SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string pszEmptyText);
    void SetFolderSettings([In] ref FOLDERSETTINGS pfs);
    void Advise(IExplorerBrowserEvents psbe, out uint pdwCookie);
    void Unadvise(uint dwCookie);
    void SetOptions(ExplorerBrowserOptions dwFlag);
    void GetOptions(out ExplorerBrowserOptions pdwFlag);
    void BrowseToIDList(IntPtr pidl, uint uFlags);
    void BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object punk, uint uFlags);
    void FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, int dwFlags);
    void RemoveAll();
    [PreserveSig] int GetCurrentView([In] ref Guid riid, out IntPtr ppv);
}

[ComImport, Guid("361bbdc7-e6ee-4e13-be58-58e2240c810f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerBrowserEvents
{
    [PreserveSig] int OnNavigationPending(IntPtr pidlFolder);
    [PreserveSig] int OnViewCreated([MarshalAs(UnmanagedType.IUnknown)] object psv);
    [PreserveSig] int OnNavigationComplete(IntPtr pidlFolder);
    [PreserveSig] int OnNavigationFailed(IntPtr pidlFolder);
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellView
{
    // IOleWindow
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
    // IShellView
    [PreserveSig] int TranslateAccelerator(ref MSG pmsg);
    [PreserveSig] int EnableModeless(bool fEnable);
    [PreserveSig] int UIActivate(uint uState);
    [PreserveSig] int Refresh();
    [PreserveSig] int CreateViewWindow(IntPtr psvPrevious, ref FOLDERSETTINGS pfs, IntPtr psb, ref RECT prcView, out IntPtr phWnd);
    [PreserveSig] int DestroyViewWindow();
    [PreserveSig] int GetCurrentInfo(out FOLDERSETTINGS pfs);
    [PreserveSig] int AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
    [PreserveSig] int SaveViewState();
    [PreserveSig] int SelectItem(IntPtr pidlItem, uint uFlags);
    [PreserveSig] int GetItemObject(uint uItem, [In] ref Guid riid, out IntPtr ppv);
}

[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItem
{
    void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem ppsi);
    void GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppvOut);
    void GetPropertyStore(int flags, [In] ref Guid riid, out IntPtr ppv);
    void GetPropertyDescriptionList(IntPtr keyType, [In] ref Guid riid, out IntPtr ppv);
    void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
    void GetCount(out uint pdwNumItems);
    void GetItemAt(uint dwIndex, out IShellItem ppsi);
    void EnumItems(out IntPtr ppenumShellItems);
}

[StructLayout(LayoutKind.Sequential)]
public struct CMINVOKECOMMANDINFOEX
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public IntPtr lpVerb;
    public IntPtr lpParameters;
    public IntPtr lpDirectory;
    public int nShow;
    public uint dwHotKey;
    public IntPtr hIcon;
    public IntPtr lpTitle;
    public IntPtr lpVerbW;
    public IntPtr lpParametersW;
    public IntPtr lpDirectoryW;
    public IntPtr lpTitleW;
    public POINT ptInvoke;
}

[ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IContextMenu
{
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
}

[ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFileOperation
{
    void Advise(IntPtr pfops, out uint pdwCookie);
    void Unadvise(uint dwCookie);
    void SetOperationFlags(uint dwOperationFlags);
    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
    void SetProgressDialog(IntPtr popd);
    void SetProperties(IntPtr pproparray);
    void SetOwnerWindow(IntPtr hwndOwner);
    void ApplyPropertiesToItem(IShellItem psiItem);
    void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
    void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
    void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
    void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr pfopsItem);
    void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
    void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IntPtr pfopsItem);
    void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
    void DeleteItem(IShellItem psiItem, IntPtr pfopsItem);
    void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
    void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IntPtr pfopsItem);
    void PerformOperations();
    [return: MarshalAs(UnmanagedType.Bool)] bool GetAnyOperationsAborted();
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct SHFILEINFO
{
    public IntPtr hIcon;
    public int iIcon;
    public uint dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
}

public static class ShellNative
{
    public static readonly Guid CLSID_ExplorerBrowser = new("71F96385-DDD6-48D3-A0C1-AE06E8B055FB");
    public static readonly Guid CLSID_FileOperation = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    public static Guid IID_IShellView = new("000214E3-0000-0000-C000-000000000046");
    public static Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    public static Guid IID_IShellItemArray = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
    public static Guid IID_IDataObject = new("0000010E-0000-0000-C000-000000000046");
    public static Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    public static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    public const uint SBSP_ABSOLUTE = 0x0;
    public const uint FVM_DETAILS = 4;
    public const uint SVGIO_BACKGROUND = 0, SVGIO_SELECTION = 1;
    public const uint SVUIA_ACTIVATE_FOCUS = 2;
    public const uint SFGAO_FOLDER = 0x20000000, SFGAO_FILESYSTEM = 0x40000000, SFGAO_STREAM = 0x00400000;
    public const uint CMF_NORMAL = 0, CMF_EXPLORE = 0x4;
    public const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
    public const uint FOF_NOCONFIRMMKDIR = 0x0200, FOF_ALLOWUNDO = 0x0040, FOFX_ADDUNDORECORD = 0x20000000, FOFX_RECYCLEONDELETE = 0x00080000;
    public const int COPYENGINE_E_USER_CANCELLED = unchecked((int)0x80270000);
    public const int HRESULT_CANCELLED = unchecked((int)0x800704C7);
    public const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    public static extern int SHGetNameFromIDList(IntPtr pidl, SIGDN sigdnName, out IntPtr ppszName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("shell32.dll")]
    public static extern int SHCreateShellItemArrayFromDataObject(IntPtr pdo, [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppv);

    [DllImport("shell32.dll")]
    public static extern int SHGetKnownFolderPath([In] ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll")] public static extern void ILFree(IntPtr pidl);

    public static string? TakeString(IntPtr p)
    {
        if (p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    public static string? GetName(IntPtr pidl, SIGDN kind) =>
        SHGetNameFromIDList(pidl, kind, out var p) >= 0 ? TakeString(p) : null;

    public static string? GetName(IShellItem item, SIGDN kind)
    {
        try
        {
            item.GetDisplayName(kind, out var p);
            return TakeString(p);
        }
        catch (COMException) { return null; }
    }

    public static IShellItem CreateItem(string path)
    {
        var iid = IID_IShellItem;
        var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return item;
    }

    public static string? GetKnownFolder(Guid id)
    {
        var g = id;
        return SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out var p) >= 0 ? TakeString(p) : null;
    }

    public static string DownloadsFolder =>
        GetKnownFolder(FOLDERID_Downloads) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}
