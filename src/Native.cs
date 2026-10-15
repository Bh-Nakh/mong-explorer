// Windows 셸 COM 인터페이스·API 선언 (ExplorerBrowser, NameSpaceTreeControl, IFolderView2 ...)
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NewExplorer
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FOLDERSETTINGS
    {
        public int ViewMode;
        public int fFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
        public PROPERTYKEY(Guid f, uint p) { fmtid = f; pid = p; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SORTCOLUMN
    {
        public PROPERTYKEY propkey;
        public int direction;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CM_COLUMNINFO
    {
        public int cbSize;
        public int dwMask;
        public int dwState;
        public int uWidth;
        public int uDefaultWidth;
        public int uIdealWidth;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string wszName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPStr)] public string lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpTitleW;
        public POINT ptInvoke;
    }

    public static class SIGDN
    {
        public const uint NORMALDISPLAY = 0;
        public const uint PARENTRELATIVEPARSING = 0x80018001;
        public const uint DESKTOPABSOLUTEPARSING = 0x80028000;
        public const uint PARENTRELATIVEEDITING = 0x80031001;
        public const uint DESKTOPABSOLUTEEDITING = 0x8004c000;
        public const uint FILESYSPATH = 0x80058000;
        public const uint PARENTRELATIVEFORADDRESSBAR = 0x8007c001;
    }

    // ───────── IShellItem ─────────
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
        [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyDescriptionList(ref PROPERTYKEY keyType, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributes(int AttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int GetCount(out uint pdwNumItems);
        [PreserveSig] int GetItemAt(uint dwIndex, out IShellItem ppsi);
        [PreserveSig] int EnumItems(out IntPtr ppenumShellItems);
    }

    // ───────── ExplorerBrowser ─────────
    [ComImport, Guid("dfd3b6b5-c10c-4be9-85f6-a66969f402f6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExplorerBrowser
    {
        [PreserveSig] int Initialize(IntPtr hwndParent, ref RECT prc, ref FOLDERSETTINGS pfs);
        [PreserveSig] int Destroy();
        [PreserveSig] int SetRect(IntPtr phdwp, RECT rcBrowser);
        [PreserveSig] int SetPropertyBag([MarshalAs(UnmanagedType.LPWStr)] string pszPropertyBag);
        [PreserveSig] int SetEmptyText([MarshalAs(UnmanagedType.LPWStr)] string pszEmptyText);
        [PreserveSig] int SetFolderSettings(ref FOLDERSETTINGS pfs);
        [PreserveSig] int Advise(IExplorerBrowserEvents psbe, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOptions(int dwFlag);
        [PreserveSig] int GetOptions(out int pdwFlag);
        [PreserveSig] int BrowseToIDList(IntPtr pidl, uint uFlags);
        [PreserveSig] int BrowseToObject([MarshalAs(UnmanagedType.IUnknown)] object punk, uint uFlags);
        [PreserveSig] int FillFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, int dwFlags);
        [PreserveSig] int RemoveAll();
        [PreserveSig] int GetCurrentView(ref Guid riid, out IntPtr ppv);
    }

    [ComImport, Guid("361bbdc7-e6ee-4e13-be58-58e2240c810f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExplorerBrowserEvents
    {
        [PreserveSig] int OnNavigationPending(IntPtr pidlFolder);
        [PreserveSig] int OnViewCreated([MarshalAs(UnmanagedType.IUnknown)] object psv);
        [PreserveSig] int OnNavigationComplete(IntPtr pidlFolder);
        [PreserveSig] int OnNavigationFailed(IntPtr pidlFolder);
    }

    [ComImport, Guid("e07010ec-bc17-44c0-97b0-46c7c95b9edc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExplorerPaneVisibility
    {
        [PreserveSig] int GetPaneState(ref Guid ep, out int peps);
    }

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IObjectWithSite
    {
        [PreserveSig] int SetSite([MarshalAs(UnmanagedType.IUnknown)] object pUnkSite);
        [PreserveSig] int GetSite(ref Guid riid, out IntPtr ppvSite);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleWindow
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellView
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
        [PreserveSig] int TranslateAccelerator(ref MSG pmsg);
        [PreserveSig] int EnableModeless(bool fEnable);
        [PreserveSig] int UIActivate(uint uState);
        [PreserveSig] int Refresh();
        [PreserveSig] int CreateViewWindow(IntPtr psvPrevious, IntPtr pfs, IntPtr psb, IntPtr prcView, out IntPtr phWnd);
        [PreserveSig] int DestroyViewWindow();
        [PreserveSig] int GetCurrentInfo(out FOLDERSETTINGS pfs);
        [PreserveSig] int AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
        [PreserveSig] int SaveViewState();
        [PreserveSig] int SelectItem(IntPtr pidlItem, uint uFlags);
        [PreserveSig] int GetItemObject(uint uItem, ref Guid riid, out IntPtr ppv);
    }

    [ComImport, Guid("1af3a467-214f-4298-908e-06b03e0b39f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFolderView2
    {
        // IFolderView
        [PreserveSig] int GetCurrentViewMode(out int pViewMode);
        [PreserveSig] int SetCurrentViewMode(int ViewMode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
        [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
        [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int piItem);
        [PreserveSig] int GetFocusedItem(out int piItem);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT ppt);
        [PreserveSig] int GetSpacing(ref POINT ppt);
        [PreserveSig] int GetDefaultSpacing(out POINT ppt);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int iItem, uint dwFlags);
        [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
        // IFolderView2
        [PreserveSig] int SetGroupBy(ref PROPERTYKEY key, bool fAscending);
        [PreserveSig] int GetGroupBy(out PROPERTYKEY pkey, out bool pfAscending);
        [PreserveSig] int SetViewProperty(IntPtr pidl, ref PROPERTYKEY propkey, IntPtr propvar);
        [PreserveSig] int GetViewProperty(IntPtr pidl, ref PROPERTYKEY propkey, IntPtr ppropvar);
        [PreserveSig] int SetTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);
        [PreserveSig] int SetExtendedTileViewProperties(IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszPropList);
        [PreserveSig] int SetText(int iType, [MarshalAs(UnmanagedType.LPWStr)] string pwszText);
        [PreserveSig] int SetCurrentFolderFlags(uint dwMask, uint dwFlags);
        [PreserveSig] int GetCurrentFolderFlags(out uint pdwFlags);
        [PreserveSig] int GetSortColumnCount(out int pcColumns);
        [PreserveSig] int SetSortColumns([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] SORTCOLUMN[] rgSortColumns, int cColumns);
        [PreserveSig] int GetSortColumns([In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] SORTCOLUMN[] rgSortColumns, int cColumns);
        [PreserveSig] int GetItem(int iItem, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetVisibleItem(int iStart, bool fPrevious, out int piItem);
        [PreserveSig] int GetSelectedItem(int iStart, out int piItem);
        [PreserveSig] int GetSelection(bool fNoneImpliesFolder, out IShellItemArray ppsia);
        [PreserveSig] int GetSelectionState(IntPtr pidl, out uint pdwFlags);
        [PreserveSig] int InvokeVerbOnSelection([MarshalAs(UnmanagedType.LPStr)] string pszVerb);
        [PreserveSig] int SetViewModeAndIconSize(int uViewMode, int iImageSize);
        [PreserveSig] int GetViewModeAndIconSize(out int puViewMode, out int piImageSize);
        [PreserveSig] int SetGroupSubsetCount(uint cVisibleRows);
        [PreserveSig] int GetGroupSubsetCount(out uint pcVisibleRows);
        [PreserveSig] int SetRedraw(bool fRedrawOn);
        [PreserveSig] int IsMoveInSameFolder();
        [PreserveSig] int DoRename();
    }

    [ComImport, Guid("d8ec27bb-3f3b-4042-b10a-4acfd924d453"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IColumnManager
    {
        [PreserveSig] int SetColumnInfo(ref PROPERTYKEY propkey, ref CM_COLUMNINFO pcmci);
        [PreserveSig] int GetColumnInfo(ref PROPERTYKEY propkey, ref CM_COLUMNINFO pcmci);
        [PreserveSig] int GetColumnCount(int dwFlags, out int puCount);
        [PreserveSig] int GetColumns(int dwFlags, [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] PROPERTYKEY[] rgkeyOrder, int cColumns);
        [PreserveSig] int SetColumns([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] PROPERTYKEY[] rgkeyOrder, int cVisible);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    // ───────── NameSpaceTreeControl ─────────
    [ComImport, Guid("028212A3-B627-47e9-8856-C14265554E4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INameSpaceTreeControl
    {
        [PreserveSig] int Initialize(IntPtr hwndParent, ref RECT prc, uint nsctsFlags);
        [PreserveSig] int TreeAdvise([MarshalAs(UnmanagedType.IUnknown)] object punk, out uint pdwCookie);
        [PreserveSig] int TreeUnadvise(uint dwCookie);
        [PreserveSig] int AppendRoot(IShellItem psiRoot, uint grfEnumFlags, uint grfRootStyle, IntPtr pif);
        [PreserveSig] int InsertRoot(int iIndex, IShellItem psiRoot, uint grfEnumFlags, uint grfRootStyle, IntPtr pif);
        [PreserveSig] int RemoveRoot(IShellItem psiRoot);
        [PreserveSig] int RemoveAllRoots();
        [PreserveSig] int GetRootItems(out IShellItemArray ppsiaRootItems);
        [PreserveSig] int SetItemState(IShellItem psi, uint nstcisMask, uint nstcisFlags);
        [PreserveSig] int GetItemState(IShellItem psi, uint nstcisMask, out uint pnstcisFlags);
        [PreserveSig] int GetSelectedItems(out IShellItemArray psiaItems);
        [PreserveSig] int GetItemCustomState(IShellItem psi, out int piStateNumber);
        [PreserveSig] int SetItemCustomState(IShellItem psi, int iStateNumber);
        [PreserveSig] int EnsureItemVisible(IShellItem psi);
        [PreserveSig] int SetTheme([MarshalAs(UnmanagedType.LPWStr)] string pszTheme);
        [PreserveSig] int GetNextItem(IShellItem psi, int nstcgi, out IShellItem ppsiNext);
        [PreserveSig] int HitTest(ref POINT ppt, out IShellItem ppsiOut);
        [PreserveSig] int GetItemRect(IShellItem psi, out RECT prect);
        [PreserveSig] int CollapseAll();
    }

    [ComImport, Guid("93D77985-B3D9-4484-8318-672CDDA002CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface INameSpaceTreeControlEvents
    {
        [PreserveSig] int OnItemClick(IShellItem psi, int nstceHitTest, int nstceClickType);
        [PreserveSig] int OnPropertyItemCommit(IShellItem psi);
        [PreserveSig] int OnItemStateChanging(IShellItem psi, uint nstcisMask, uint nstcisState);
        [PreserveSig] int OnItemStateChanged(IShellItem psi, uint nstcisMask, uint nstcisState);
        [PreserveSig] int OnSelectionChanged(IShellItemArray psiaSelection);
        [PreserveSig] int OnKeyboardInput(uint uMsg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int OnBeforeExpand(IShellItem psi);
        [PreserveSig] int OnAfterExpand(IShellItem psi);
        [PreserveSig] int OnBeginLabelEdit(IShellItem psi);
        [PreserveSig] int OnEndLabelEdit(IShellItem psi);
        [PreserveSig] int OnGetToolTip(IShellItem psi, IntPtr pszTip, int cchTip);
        [PreserveSig] int OnBeforeItemDelete(IShellItem psi);
        [PreserveSig] int OnItemAdded(IShellItem psi, bool fIsRoot);
        [PreserveSig] int OnItemDeleted(IShellItem psi, bool fIsRoot);
        [PreserveSig] int OnBeforeContextMenu(IShellItem psi, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int OnAfterContextMenu(IShellItem psi, IntPtr pcmIn, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int OnBeforeStateImageChange(IShellItem psi);
        [PreserveSig] int OnGetDefaultIconIndex(IShellItem psi, out int piDefaultIcon, out int piOpenIcon);
    }

    public static class Native
    {
        public static readonly Guid CLSID_ExplorerBrowser = new Guid("71f96385-ddd6-48d3-a0c1-ae06e8b055fb");
        public static readonly Guid CLSID_NameSpaceTreeControl = new Guid("AE054212-3535-4430-83ED-D501AA6680E6");
        public static Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        public static Guid IID_IShellView = new Guid("000214E3-0000-0000-C000-000000000046");
        public static Guid IID_IFolderView2 = new Guid("1af3a467-214f-4298-908e-06b03e0b39f9");
        public static Guid IID_IColumnManager = new Guid("d8ec27bb-3f3b-4042-b10a-4acfd924d453");
        public static Guid IID_IContextMenu = new Guid("000214e4-0000-0000-c000-000000000046");
        public static Guid IID_IExplorerPaneVisibility = new Guid("e07010ec-bc17-44c0-97b0-46c7c95b9edc");
        public static Guid SID_ExplorerPaneVisibility = new Guid("e07010ec-bc17-44c0-97b0-46c7c95b9edc");

        public static Guid EP_NavPane = new Guid("cb316b22-25f7-42b8-8a09-540d23a43c2f");
        public static Guid EP_Commands = new Guid("d9745868-ca5f-4a76-91cd-f5a129fbb076");
        public static Guid EP_Commands_Organize = new Guid("72e81700-e3ec-4660-bf24-3c3b7b648806");
        public static Guid EP_Commands_View = new Guid("21f7c32d-eeaa-439b-bb51-37b96fd6a943");
        public static Guid EP_DetailsPane = new Guid("43abf98b-89b8-472d-b9ce-e69b8229f019");
        public static Guid EP_PreviewPane = new Guid("893c63d1-45c8-4d17-be19-223be71be365");
        public static Guid EP_QueryPane = new Guid("65bcde4f-4f07-4f27-83a7-1afca4df7ddd");
        public static Guid EP_AdvQueryPane = new Guid("b4e9db8b-34ba-4c39-b5cc-16a1bd2c411c");
        public static Guid EP_StatusBar = new Guid("65fe56ce-5cfe-4bc4-ad8a-7ae3fe7e8f7c");
        public static Guid EP_Ribbon = new Guid("D27524A8-C9F2-4834-A106-DF8889FD4F37");

        public const int EPS_DONTCARE = 0, EPS_DEFAULT_ON = 1, EPS_DEFAULT_OFF = 2, EPS_FORCE = 0x20000;

        public const int EBO_SHOWFRAMES = 0x2, EBO_ALWAYSNAVIGATE = 0x4, EBO_NOTRAVELLOG = 0x8, EBO_NOBORDER = 0x40;
        public const uint SBSP_ABSOLUTE = 0x0;

        public const int FVM_AUTO = -1, FVM_ICON = 1, FVM_SMALLICON = 2, FVM_LIST = 3, FVM_DETAILS = 4,
                         FVM_THUMBNAIL = 5, FVM_TILE = 6, FVM_CONTENT = 8;

        public const uint SVGIO_BACKGROUND = 0, SVGIO_SELECTION = 1, SVGIO_ALLVIEW = 2;

        public const uint NSTCS_HASEXPANDOS = 0x1, NSTCS_SINGLECLICKEXPAND = 0x4, NSTCS_FULLROWSELECT = 0x8,
            NSTCS_HORIZONTALSCROLL = 0x20, NSTCS_ROOTHASEXPANDO = 0x40, NSTCS_SHOWSELECTIONALWAYS = 0x80,
            NSTCS_NOINFOTIP = 0x200, NSTCS_EVENHEIGHT = 0x400, NSTCS_TABSTOP = 0x20000,
            NSTCS_AUTOHSCROLL = 0x100000, NSTCS_FADEINOUTEXPANDOS = 0x200000;
        public const uint NSTCRS_VISIBLE = 0, NSTCRS_EXPANDED = 2;
        public const uint NSTCIS_SELECTED = 1, NSTCIS_EXPANDED = 2;
        public const uint SHCONTF_FOLDERS = 0x20, SHCONTF_INCLUDEHIDDEN = 0x80, SHCONTF_NAVIGATION_ENUM = 0x1000;

        public const uint SFGAO_FOLDER = 0x20000000, SFGAO_FILESYSTEM = 0x40000000, SFGAO_STREAM = 0x00400000;

        public const int S_OK = 0, S_FALSE = 1;
        public const int E_NOTIMPL = unchecked((int)0x80004001);
        public const int E_NOINTERFACE = unchecked((int)0x80004002);

        public const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_CHAR = 0x102, WM_SYSKEYDOWN = 0x104,
            WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONDOWN = 0x204,
            WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_XBUTTONUP = 0x20C, WM_XBUTTONDOWN = 0x20B,
            WM_MOUSEWHEEL = 0x20A, WM_DEVICECHANGE = 0x219;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

        [DllImport("shell32.dll", PreserveSig = true)]
        public static extern int SHCreateItemFromIDList(IntPtr pidl, ref Guid riid, out IShellItem ppv);

        [DllImport("shell32.dll", PreserveSig = true)]
        public static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object punk, out IntPtr ppidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll")]
        public static extern void ILFree(IntPtr pidl);

        // 탐색기 보기가 바로 알아차리도록 '폴더 생김' 알림 (SHCNE_MKDIR, SHCNF_PATHW)
        public const int SHCNE_MKDIR = 0x8, SHCNF_PATHW = 0x5;
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern void SHChangeNotify(int wEventId, int uFlags, string dwItem1, IntPtr dwItem2);

        [DllImport("shell32.dll")]
        public static extern IntPtr ILClone(IntPtr pidl);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, int cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
        public static extern IntPtr SHGetFileInfoPath(string path, uint dwFileAttributes, ref SHFILEINFO psfi, int cbFileInfo, uint uFlags);

        public const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_PIDL = 0x8;

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessageStr(IntPtr hWnd, int Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        public static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("ole32.dll")]
        public static extern void CoTaskMemFree(IntPtr pv);

        public static string ClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool KeyDown(int vk) { return (GetKeyState(vk) & 0x8000) != 0; }
    }
}
