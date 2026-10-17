// 탭 하나 = 윈도우 셸의 ExplorerBrowser 하나 (탐색기와 같은 파일 보기)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NewExplorer
{
    /// <summary>ExplorerBrowser 가 물어보는 창 구성(미리 보기·세부 정보 창 표시 여부)에 답한다</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class EbSite : IServiceProvider, IExplorerPaneVisibility
    {
        public int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (guidService == Native.SID_ExplorerPaneVisibility && riid == Native.IID_IExplorerPaneVisibility)
            {
                ppvObject = Marshal.GetComInterfaceForObject(this, typeof(IExplorerPaneVisibility));
                return Native.S_OK;
            }
            return Native.E_NOINTERFACE;
        }

        public int GetPaneState(ref Guid ep, out int peps)
        {
            if (ep == Native.EP_PreviewPane)
                peps = (Settings.Current.PreviewPane ? Native.EPS_DEFAULT_ON : Native.EPS_DEFAULT_OFF) | Native.EPS_FORCE;
            else if (ep == Native.EP_DetailsPane)
                peps = (Settings.Current.DetailsPane ? Native.EPS_DEFAULT_ON : Native.EPS_DEFAULT_OFF) | Native.EPS_FORCE;
            else
                peps = Native.EPS_DEFAULT_OFF | Native.EPS_FORCE;   // 탐색 창(트리)·명령 모음 등은 끔
            return Native.S_OK;
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class EbEvents : IExplorerBrowserEvents
    {
        readonly ExplorerHost host;
        public EbEvents(ExplorerHost h) { host = h; }
        public int OnNavigationPending(IntPtr pidlFolder) { return Native.S_OK; }
        public int OnViewCreated(object psv) { return Native.S_OK; }
        public int OnNavigationComplete(IntPtr pidlFolder) { host.NavComplete(pidlFolder); return Native.S_OK; }
        public int OnNavigationFailed(IntPtr pidlFolder) { host.NavFailed(); return Native.S_OK; }
    }

    public class ExplorerHost : Control
    {
        IExplorerBrowser eb;
        EbEvents events;
        EbSite site;
        uint cookie;
        string pendingPath;
        bool historyNav;
        bool keepSelection;   // 같은 폴더 다시 열기(Reload·Recreate)는 첫 항목 선택을 하지 않음
        int selectStamp;
        string selectAfterNav;   // 이동이 끝나면 선택할 항목 이름

        public string CurrentPath { get; private set; }
        public readonly List<string> BackList = new List<string>();
        public readonly List<string> ForwardList = new List<string>();
        public event EventHandler Navigated;

        public ExplorerHost(string path)
        {
            pendingPath = path;
            CurrentPath = path;
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Current.Content;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (viewWatch == null)
            {
                viewWatch = new Timer { Interval = 800 };
                viewWatch.Tick += delegate { WatchView(); };
                viewWatch.Start();
            }
            if (eb != null) return;
            var p = pendingPath ?? Shell.ThisPC;
            pendingPath = null;
            CreateBrowser();
            if (!NavigateRaw(p)) { CurrentPath = null; NavigateRaw(Shell.ThisPC); }
        }

        void CreateBrowser()
        {
            eb = (IExplorerBrowser)Activator.CreateInstance(Type.GetTypeFromCLSID(Native.CLSID_ExplorerBrowser));
            site = new EbSite();
            ((IObjectWithSite)eb).SetSite(site);
            eb.SetOptions(Native.EBO_SHOWFRAMES | Native.EBO_ALWAYSNAVIGATE | Native.EBO_NOBORDER | Native.EBO_NOTRAVELLOG);
            var rc = new RECT(0, 0, Math.Max(1, Width), Math.Max(1, Height));
            var fs = new FOLDERSETTINGS { ViewMode = Native.FVM_DETAILS, fFlags = 0 };
            eb.Initialize(Handle, ref rc, ref fs);
            eb.SetPropertyBag("NewExplorer");
            events = new EbEvents(this);
            eb.Advise(events, out cookie);
        }

        // 윈도우 쪽 보기가 닫히는 중에 Destroy 하면 AccessViolationException 이 날 수 있다 (종료 때 강제 종료되던 원인).
        // .NET 4 는 이 예외를 기본으로는 잡지 못하므로 속성을 붙여 아래 catch 에서 삼킨다.
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions, System.Security.SecurityCritical]
        void DestroyBrowser()
        {
            if (eb == null) return;
            try
            {
                if (cookie != 0) eb.Unadvise(cookie);
                cookie = 0;
                ((IObjectWithSite)eb).SetSite(null);
                eb.Destroy();
            }
            catch { }
            Marshal.ReleaseComObject(eb);
            eb = null;
        }

        /// <summary>창 구성(미리 보기·세부 정보 창)이 바뀌면 보기를 새로 만들어 같은 폴더를 다시 연다. 기록은 그대로</summary>
        public void Recreate()
        {
            if (!IsHandleCreated || eb == null) return;
            DestroyBrowser();
            CreateBrowser();
            historyNav = true; keepSelection = true;
            if (!NavigateRaw(CurrentPath ?? Shell.ThisPC)) historyNav = keepSelection = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (eb != null) eb.SetRect(IntPtr.Zero, new RECT(0, 0, Width, Height));
        }

        protected override void Dispose(bool disposing)
        {
            if (viewWatch != null) { viewWatch.Stop(); viewWatch.Dispose(); viewWatch = null; }
            viewReady = false;
            DestroyBrowser();
            base.Dispose(disposing);
        }

        // ───── 이동 ─────
        bool NavigateRaw(string path)
        {
            if (eb == null) { pendingPath = path; return true; }
            IntPtr pidl = Shell.PidlFromPath(path);
            if (pidl == IntPtr.Zero) return false;
            viewReady = false;
            try { return eb.BrowseToIDList(pidl, Native.SBSP_ABSOLUTE) == 0; }
            finally { Native.ILFree(pidl); }
        }

        public bool Navigate(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (eb == null) { pendingPath = path; CurrentPath = path; return true; }
            return NavigateRaw(path);
        }

        public bool NavigateItem(IShellItem item)
        {
            if (eb == null) { pendingPath = Shell.ParsingName(item); return true; }
            IntPtr pidl = Shell.PidlFromItem(item);
            if (pidl == IntPtr.Zero) return false;
            viewReady = false;
            try { return eb.BrowseToIDList(pidl, Native.SBSP_ABSOLUTE) == 0; }
            finally { Native.ILFree(pidl); }
        }

        public bool CanBack { get { return BackList.Count > 0; } }
        public bool CanForward { get { return ForwardList.Count > 0; } }

        public void Back(int steps = 1)
        {
            if (BackList.Count < steps) return;
            for (int i = 0; i < steps; i++)
            {
                ForwardList.Insert(0, CurrentPath);
                CurrentPath = BackList[BackList.Count - 1];
                BackList.RemoveAt(BackList.Count - 1);
            }
            historyNav = true;
            if (!NavigateRaw(CurrentPath)) historyNav = false;
        }

        public void Forward(int steps = 1)
        {
            if (ForwardList.Count < steps) return;
            for (int i = 0; i < steps; i++)
            {
                BackList.Add(CurrentPath);
                CurrentPath = ForwardList[0];
                ForwardList.RemoveAt(0);
            }
            historyNav = true;
            if (!NavigateRaw(CurrentPath)) historyNav = false;
        }

        public void Up()
        {
            string parent = null;
            if (Shell.IsFileSystemPath(CurrentPath)) parent = Shell.Parent(CurrentPath);
            else
            {
                var item = Shell.ItemFromPath(CurrentPath);
                if (item != null)
                {
                    IShellItem p;
                    if (item.GetParent(out p) == 0 && p != null) { parent = Shell.ParsingName(p); Marshal.ReleaseComObject(p); }
                    Marshal.ReleaseComObject(item);
                }
            }
            if (!string.IsNullOrEmpty(parent)) Navigate(parent);
        }

        /// <summary>같은 폴더를 다시 연다 (창 구성 바뀜 반영). 기록은 남기지 않음</summary>
        public void Reload()
        {
            if (CurrentPath == null) return;
            historyNav = true; keepSelection = true;
            if (!NavigateRaw(CurrentPath)) historyNav = keepSelection = false;
        }

        public void RefreshView()
        {
            var sv = GetView<IShellView>(Native.IID_IShellView);
            if (sv != null) { sv.Refresh(); Marshal.ReleaseComObject(sv); }
        }

        internal void NavComplete(IntPtr pidl)
        {
            string path = Shell.PathOfPidl(pidl);
            if (path == null) return;
            bool keep = keepSelection;
            keepSelection = false;
            if (selectAfterNav != null) { var nm = selectAfterNav; selectAfterNav = null; SelectNameSoon(nm); }
            else if (!keep) SelectFirstItemSoon();
            if (historyNav) historyNav = false;
            else if (CurrentPath != null && !Shell.SamePath(CurrentPath, path))
            {
                BackList.Add(CurrentPath);
                if (BackList.Count > 100) BackList.RemoveAt(0);
                ForwardList.Clear();
            }
            CurrentPath = path;
            // '모든 폴더에 적용'은 일반 폴더에만 (내 PC·검색 결과 등은 열 구성이 달라 제외)
            if (Shell.IsFileSystemPath(path))
            {
                // 그 폴더에서 마지막으로 쓴 보기 설정이 있으면 그것을, 없으면 '모든 폴더에 적용' 설정을
                ViewTemplate saved;
                if (Settings.Current.FolderViews.TryGetValue(FolderKey(path), out saved)) ApplyTemplate(saved);
                else if (Settings.Current.Template != null) ApplyTemplate(Settings.Current.Template);
            }
            baselineSig = null; viewReady = true;
            if (Navigated != null) Navigated(this, EventArgs.Empty);
        }

        /// <summary>새 폴더에 들어가면 목록 첫 항목을 선택 (항목이 늦게 채워지므로 잠시 반복 시도)</summary>
        void SelectFirstItemSoon()
        {
            if (!IsHandleCreated) return;
            int stamp = ++selectStamp;
            int tries = 0;
            var timer = new Timer { Interval = 60 };
            timer.Tick += delegate
            {
                bool done = stamp != selectStamp || ++tries > 25 || TrySelectFirst();
                if (done) { timer.Stop(); timer.Dispose(); }
            };
            timer.Start();
        }

        /// <summary>폴더를 열고 그 안의 name 항목을 선택 (검색 결과의 '포함 폴더 열기')</summary>
        public bool NavigateAndSelect(string dir, string name)
        {
            if (Shell.SamePath(CurrentPath, dir)) { SelectNameSoon(name); return true; }   // 이미 그 폴더면 이동 없이 선택만
            selectAfterNav = name;
            if (Navigate(dir)) return true;
            selectAfterNav = null;
            return false;
        }

        void SelectNameSoon(string name)
        {
            if (!IsHandleCreated) return;
            int stamp = ++selectStamp;
            int tries = 0;
            var names = new[] { name };
            var timer = new Timer { Interval = 60 };
            timer.Tick += delegate
            {
                bool ok = stamp == selectStamp && SelectNames(names, false) > 0;
                bool done = stamp != selectStamp || ok || ++tries > 25;
                if (ok) FocusView();
                if (done) { timer.Stop(); timer.Dispose(); }
            };
            timer.Start();
        }

        bool TrySelectFirst()
        {
            var fv = GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return false;
            try
            {
                int n;
                if (fv.ItemCount(0 /* SVGIO_ALLVIEW */, out n) != 0 || n <= 0) return false;
                // SELECT | DESELECTOTHERS | ENSUREVISIBLE | FOCUSED
                return fv.SelectItem(0, 0x1 | 0x4 | 0x8 | 0x10) == 0;
            }
            catch { return false; }
            finally { Marshal.ReleaseComObject(fv); }
        }

        // ───── 붙여넣은 항목 선택 · 새 폴더 ─────
        /// <summary>보기에 있는 항목 중 이름이 names 에 든 것을 선택 (edit 이면 첫 항목을 이름 바꾸기 상태로). 선택한 개수</summary>
        int SelectNames(ICollection<string> names, bool edit)
        {
            var fv = GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return 0;
            int found = 0;
            try
            {
                int n;
                if (fv.ItemCount(Native.SVGIO_ALLVIEW, out n) != 0) return 0;
                var want = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < n && found < want.Count; i++)
                {
                    IntPtr p;
                    Guid iid = Native.IID_IShellItem;
                    if (fv.GetItem(i, ref iid, out p) != 0 || p == IntPtr.Zero) continue;
                    var item = (IShellItem)Marshal.GetObjectForIUnknown(p);
                    Marshal.Release(p);
                    string name = Shell.Name(item, SIGDN.PARENTRELATIVEPARSING);
                    Marshal.ReleaseComObject(item);
                    if (name == null || !want.Contains(name)) continue;
                    // 첫 항목: SELECT(EDIT) | DESELECTOTHERS | ENSUREVISIBLE | FOCUSED, 나머지: SELECT
                    fv.SelectItem(i, found == 0 ? ((edit ? 0x3u : 0x1u) | 0x4 | 0x8 | 0x10) : 0x1u);
                    found++;
                }
            }
            catch { }
            finally { Marshal.ReleaseComObject(fv); }
            return found;
        }

        int SelectionCount()
        {
            var fv = GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return -1;
            try { int n; return fv.ItemCount(Native.SVGIO_SELECTION, out n) == 0 ? n : -1; }
            catch { return -1; }
            finally { Marshal.ReleaseComObject(fv); }
        }

        static Dictionary<string, DateTime> ListFolder(string dir)
        {
            var d = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                    d[e.Name] = e.LastWriteTimeUtc;
            }
            catch { }
            return d;
        }

        /// <summary>붙여넣기(Ctrl+V) 직전에 부른다. 붙여넣기가 끝나 가는 동안 새로 생긴(또는 덮어쓴) 항목을 선택한다</summary>
        public void SelectPastedSoon()
        {
            string dir = CurrentPath;
            if (!IsHandleCreated || !Shell.IsFileSystemPath(dir)) return;
            var clip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Clipboard.ContainsFileDropList()) return;
                foreach (var f in Clipboard.GetFileDropList()) clip.Add(Path.GetFileName(f.TrimEnd('\\')));
            }
            catch { return; }
            SelectPastedSoon(clip);
        }

        /// <summary>clip: 클립보드에 든 항목 이름 (같은 이름으로 덮어쓴 경우를 알아보는 데 씀)</summary>
        internal void SelectPastedSoon(HashSet<string> clip)
        {
            string dir = CurrentPath;
            if (!IsHandleCreated || !Shell.IsFileSystemPath(dir)) return;
            var before = ListFolder(dir);
            int stamp = ++selectStamp;
            int ticks = 0, stable = 0, mySel = -1, got0 = 0;
            var timer = new Timer { Interval = 200 };
            timer.Tick += delegate
            {
                bool stop = stamp != selectStamp || ++ticks > 300;   // 다른 폴더로 이동했거나 1분 지남
                // 사용자가 그 사이 선택을 바꿨으면 손대지 않음
                if (!stop && mySel >= 0 && SelectionCount() != mySel) stop = true;
                if (!stop)
                {
                    var fresh = new List<string>();
                    foreach (var kv in ListFolder(dir))
                    {
                        DateTime old;
                        if (!before.TryGetValue(kv.Key, out old)) fresh.Add(kv.Key);              // 새로 생김
                        else if (old != kv.Value && clip.Contains(kv.Key)) fresh.Add(kv.Key);     // 덮어씀
                    }
                    int got = fresh.Count > got0 ? SelectNames(fresh, false) : got0;
                    if (got > got0) { got0 = got; mySel = SelectionCount(); stable = 0; }
                    else if (mySel >= 0 && ++stable > 15) stop = true;      // 3초 동안 더 보이는 게 없으면 끝
                }
                if (stop) { timer.Stop(); timer.Dispose(); }
            };
            timer.Start();
        }

        /// <summary>새 폴더를 만들고 이름 바꾸기 상태로 둔다 (Ctrl+Shift+N). 일반 폴더가 아니면 탐색기 명령에 맡김</summary>
        public void CreateNewFolder()
        {
            string dir = CurrentPath;
            if (!Shell.IsFileSystemPath(dir)) { InvokeVerb("NewFolder"); return; }
            string name = "새 폴더";
            for (int i = 2; Directory.Exists(Path.Combine(dir, name)) || File.Exists(Path.Combine(dir, name)); i++)
                name = "새 폴더 (" + i + ")";
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, name));
                Native.SHChangeNotify(Native.SHCNE_MKDIR, Native.SHCNF_PATHW, Path.Combine(dir, name), IntPtr.Zero);
            }
            catch (Exception ex)
            {
                MessageBox.Show(FindForm(), "새 폴더를 만들지 못했습니다.\n" + ex.Message, "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            RenameSoon(name);
        }

        /// <summary>보기에 name 항목이 나타나면 선택해 이름 바꾸기 상태로 둔다 (항목이 늦게 보이므로 잠시 반복 시도)</summary>
        public void RenameSoon(string name)
        {
            int stamp = ++selectStamp;
            int tries = 0;
            var names = new[] { name };
            var timer = new Timer { Interval = 60 };
            timer.Tick += delegate
            {
                bool done = stamp != selectStamp || ++tries > 80 || SelectNames(names, true) > 0;
                if (done) { timer.Stop(); timer.Dispose(); }
            };
            timer.Start();
        }

        // ───── 폴더별 보기 설정 기억 ─────
        Timer viewWatch;
        bool viewReady;        // 현재 폴더 보기가 준비돼 설정 복원까지 끝남
        string baselineSig;    // 복원 직후의 보기 상태 (이것과 달라지면 사용자가 바꾼 것)

        static string FolderKey(string path) { return path.TrimEnd('\\').ToLowerInvariant(); }

        static string Sig(ViewTemplate t)
        {
            var sb = new StringBuilder();
            sb.Append(t.ViewMode).Append('/').Append(t.IconSize).Append('|');
            foreach (var c in t.Columns) sb.Append(c.Fmtid).Append(':').Append(c.Pid).Append(':').Append(c.Width).Append(',');
            sb.Append('|');
            foreach (var c in t.Sort) sb.Append(c.Fmtid).Append(':').Append(c.Pid).Append(':').Append(c.Width).Append(',');
            sb.Append('|').Append(t.GroupFmtid).Append(':').Append(t.GroupPid).Append(':').Append(t.GroupAscending);
            return sb.ToString();
        }

        /// <summary>주기적으로 보기 상태를 보고, 사용자가 바꿨으면 그 폴더의 설정으로 즉시 저장</summary>
        void WatchView()
        {
            if (!viewReady || eb == null || CurrentPath == null || !Shell.IsFileSystemPath(CurrentPath)) return;
            ViewTemplate t;
            try { t = CaptureTemplate(); } catch { return; }
            if (t == null) return;
            string sig = Sig(t);
            if (baselineSig == null) { baselineSig = sig; return; }
            if (sig == baselineSig) return;
            baselineSig = sig;
            var fvs = Settings.Current.FolderViews;
            if (fvs.Count > 3000) fvs.Clear();
            fvs[FolderKey(CurrentPath)] = t;
            Settings.Save();
        }

        internal void NavFailed()
        {
            viewReady = true;
            historyNav = false;
            if (Navigated != null) Navigated(this, EventArgs.Empty);
        }

        // ───── 보기 ─────
        public T GetView<T>(Guid iid) where T : class
        {
            if (eb == null) return null;
            IntPtr p;
            if (eb.GetCurrentView(ref iid, out p) == 0 && p != IntPtr.Zero)
            {
                try { return Marshal.GetObjectForIUnknown(p) as T; }
                finally { Marshal.Release(p); }
            }
            // ExplorerBrowser 가 직접 주지 않는 인터페이스(IColumnManager 등)는 보기 객체에 물어봄
            Guid sv = Native.IID_IShellView;
            if (eb.GetCurrentView(ref sv, out p) != 0 || p == IntPtr.Zero) return null;
            try
            {
                IntPtr q;
                if (Marshal.QueryInterface(p, ref iid, out q) != 0 || q == IntPtr.Zero) return null;
                try { return Marshal.GetObjectForIUnknown(q) as T; }
                finally { Marshal.Release(q); }
            }
            finally { Marshal.Release(p); }
        }

        public IntPtr ViewWindow()
        {
            var sv = GetView<IShellView>(Native.IID_IShellView);
            if (sv == null) return IntPtr.Zero;
            try { IntPtr h; return sv.GetWindow(out h) == 0 ? h : IntPtr.Zero; }
            finally { Marshal.ReleaseComObject(sv); }
        }

        public bool TranslateAccel(ref MSG msg)
        {
            var sv = GetView<IShellView>(Native.IID_IShellView);
            if (sv == null) return false;
            try { return sv.TranslateAccelerator(ref msg) == Native.S_OK; }
            finally { Marshal.ReleaseComObject(sv); }
        }

        public void FocusView()
        {
            var sv = GetView<IShellView>(Native.IID_IShellView);
            if (sv == null) return;
            try { sv.UIActivate(2 /* SVUIA_ACTIVATE_FOCUS */); IntPtr h; if (sv.GetWindow(out h) == 0) Native.SetFocus(h); }
            finally { Marshal.ReleaseComObject(sv); }
        }

        public void SetViewMode(int mode, int iconSize)
        {
            var fv = GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return;
            try { fv.SetViewModeAndIconSize(mode, iconSize); }
            finally { Marshal.ReleaseComObject(fv); }
        }

        public void InvokeVerb(string verb)
        {
            var sv = GetView<IShellView>(Native.IID_IShellView);
            if (sv == null) return;
            IntPtr pcm = IntPtr.Zero, hmenu = IntPtr.Zero;
            try
            {
                Guid iid = Native.IID_IContextMenu;
                if (sv.GetItemObject(Native.SVGIO_BACKGROUND, ref iid, out pcm) != 0 || pcm == IntPtr.Zero) return;
                var cm = (IContextMenu)Marshal.GetObjectForIUnknown(pcm);
                hmenu = Native.CreatePopupMenu();
                cm.QueryContextMenu(hmenu, 0, 1, 0x7FFF, 0);
                var ici = new CMINVOKECOMMANDINFOEX();
                ici.cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFOEX));
                ici.hwnd = Handle;
                ici.lpVerb = verb;
                ici.nShow = 1;
                cm.InvokeCommand(ref ici);
                Marshal.ReleaseComObject(cm);
            }
            catch { }
            finally
            {
                if (hmenu != IntPtr.Zero) Native.DestroyMenu(hmenu);
                if (pcm != IntPtr.Zero) Marshal.Release(pcm);
                Marshal.ReleaseComObject(sv);
            }
        }

        /// <summary>항목 하나만 선택돼 있고 그게 폴더면 그 경로 (Ctrl+Enter, 가운데 클릭 → 새 탭)</summary>
        public string SingleSelectedFolder()
        {
            var fv = GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return null;
            try
            {
                int sel;
                if (fv.ItemCount(Native.SVGIO_SELECTION, out sel) != 0 || sel != 1) return null;
                IShellItemArray arr;
                if (fv.GetSelection(false, out arr) != 0 || arr == null) return null;
                IShellItem item;
                int hr = arr.GetItemAt(0, out item);
                Marshal.ReleaseComObject(arr);
                if (hr != 0 || item == null) return null;
                try
                {
                    uint attr;
                    item.GetAttributes(Native.SFGAO_FOLDER | Native.SFGAO_STREAM, out attr);
                    if ((attr & Native.SFGAO_FOLDER) == 0 || (attr & Native.SFGAO_STREAM) != 0) return null; // zip 등 제외
                    return Shell.ParsingName(item);
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            catch { return null; }
            finally { Marshal.ReleaseComObject(fv); }
        }

        // ───── 모든 폴더에 적용 ─────
        public static string LastCaptureInfo = "";

        public ViewTemplate CaptureTemplate()
        {
            LastCaptureInfo = "";
            var fv = GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return null;
            var t = new ViewTemplate();
            try
            {
                int mode, size;
                fv.GetViewModeAndIconSize(out mode, out size);
                t.ViewMode = mode; t.IconSize = size;

                int nSort;
                int hrs = fv.GetSortColumnCount(out nSort);
                LastCaptureInfo += "sortcount hr=" + hrs.ToString("X") + " n=" + nSort + "; ";
                if (hrs == 0 && nSort > 0)
                {
                    var cols = new SORTCOLUMN[nSort];
                    if (fv.GetSortColumns(cols, nSort) == 0)
                        foreach (var c in cols)
                            t.Sort.Add(new ColumnSpec { Fmtid = c.propkey.fmtid.ToString(), Pid = c.propkey.pid, Width = c.direction });
                }
                PROPERTYKEY gk; bool asc;
                int hrg = fv.GetGroupBy(out gk, out asc);
                LastCaptureInfo += "group hr=" + hrg.ToString("X") + "; ";
                if (hrg == 0)
                {
                    t.GroupFmtid = gk.fmtid.ToString(); t.GroupPid = gk.pid; t.GroupAscending = asc;
                }
            }
            catch (Exception ex) { LastCaptureInfo += "EX1 " + ex.Message + "; "; }
            finally { Marshal.ReleaseComObject(fv); }

            var cm = GetView<IColumnManager>(Native.IID_IColumnManager);
            LastCaptureInfo += "cm=" + (cm != null) + "; ";
            if (cm != null)
            {
                try
                {
                    int n;
                    int hrc = cm.GetColumnCount(2 /* CM_ENUM_VISIBLE */, out n);
                    LastCaptureInfo += "colcount hr=" + hrc.ToString("X") + " n=" + n + "; ";
                    if (hrc == 0 && n > 0)
                    {
                        var keys = new PROPERTYKEY[n];
                        if (cm.GetColumns(2, keys, n) == 0)
                            foreach (var k in keys)
                            {
                                var key = k;
                                var ci = new CM_COLUMNINFO { cbSize = Marshal.SizeOf(typeof(CM_COLUMNINFO)), dwMask = 1 /* WIDTH */ };
                                int w = cm.GetColumnInfo(ref key, ref ci) == 0 ? ci.uWidth : 0;
                                t.Columns.Add(new ColumnSpec { Fmtid = k.fmtid.ToString(), Pid = k.pid, Width = w });
                            }
                    }
                }
                catch (Exception ex) { LastCaptureInfo += "EX2 " + ex.Message + "; "; }
                finally { Marshal.ReleaseComObject(cm); }
            }
            return t;
        }

        public void ApplyTemplate(ViewTemplate t)
        {
            if (t == null) return;
            var fv = GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return;
            try
            {
                fv.SetRedraw(false);
                fv.SetViewModeAndIconSize(t.ViewMode, t.IconSize);
                var cm = GetView<IColumnManager>(Native.IID_IColumnManager);
                if (cm != null && t.Columns.Count > 0)
                {
                    try
                    {
                        var keys = new PROPERTYKEY[t.Columns.Count];
                        for (int i = 0; i < keys.Length; i++)
                            keys[i] = new PROPERTYKEY(new Guid(t.Columns[i].Fmtid), t.Columns[i].Pid);
                        cm.SetColumns(keys, keys.Length);
                        for (int i = 0; i < keys.Length; i++)
                        {
                            if (t.Columns[i].Width <= 0) continue;
                            var ci = new CM_COLUMNINFO { cbSize = Marshal.SizeOf(typeof(CM_COLUMNINFO)), dwMask = 1, uWidth = t.Columns[i].Width };
                            cm.SetColumnInfo(ref keys[i], ref ci);
                        }
                    }
                    catch { }
                    finally { Marshal.ReleaseComObject(cm); }
                }
                if (t.Sort.Count > 0)
                {
                    var cols = new SORTCOLUMN[t.Sort.Count];
                    for (int i = 0; i < cols.Length; i++)
                        cols[i] = new SORTCOLUMN { propkey = new PROPERTYKEY(new Guid(t.Sort[i].Fmtid), t.Sort[i].Pid), direction = t.Sort[i].Width };
                    fv.SetSortColumns(cols, cols.Length);
                }
                if (t.GroupFmtid != null)
                {
                    var gk = new PROPERTYKEY(new Guid(t.GroupFmtid), t.GroupPid);
                    fv.SetGroupBy(ref gk, t.GroupAscending);
                }
            }
            catch { }
            finally
            {
                try { fv.SetRedraw(true); } catch { }
                Marshal.ReleaseComObject(fv);
            }
        }
    }
}
