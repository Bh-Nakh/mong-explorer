// 왼쪽 위 폴더 트리 = 윈도우 셸의 NameSpaceTreeControl (탐색기 왼쪽 트리와 같은 부품)
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NewExplorer
{
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public class TreeEvents : INameSpaceTreeControlEvents
    {
        readonly FolderTree tree;
        public TreeEvents(FolderTree t) { tree = t; }

        public int OnItemClick(IShellItem psi, int hit, int click) { FolderTree.Dbg("OnItemClick hit=" + hit + " click=" + click); return tree.ItemClick(psi, hit, click); }
        public int OnPropertyItemCommit(IShellItem psi) { return Native.S_FALSE; }
        public int OnItemStateChanging(IShellItem psi, uint m, uint s) { return Native.S_OK; }
        public int OnItemStateChanged(IShellItem psi, uint m, uint s) { FolderTree.Dbg("OnItemStateChanged m=" + m + " s=" + s); return Native.S_OK; }
        public int OnSelectionChanged(IShellItemArray sel) { FolderTree.Dbg("OnSelectionChanged"); tree.SelectionChanged(sel); return Native.S_OK; }
        public int OnKeyboardInput(uint uMsg, IntPtr w, IntPtr l) { return Native.S_FALSE; }
        public int OnBeforeExpand(IShellItem psi) { return Native.S_OK; }
        public int OnAfterExpand(IShellItem psi) { return Native.S_OK; }
        public int OnBeginLabelEdit(IShellItem psi) { return Native.S_OK; }
        public int OnEndLabelEdit(IShellItem psi) { return Native.S_OK; }
        public int OnGetToolTip(IShellItem psi, IntPtr pszTip, int cchTip) { return Native.E_NOTIMPL; }
        public int OnBeforeItemDelete(IShellItem psi) { return Native.E_NOTIMPL; }
        public int OnItemAdded(IShellItem psi, bool fIsRoot) { return Native.E_NOTIMPL; }
        public int OnItemDeleted(IShellItem psi, bool fIsRoot) { return Native.E_NOTIMPL; }
        public int OnBeforeContextMenu(IShellItem psi, ref Guid riid, out IntPtr ppv) { ppv = IntPtr.Zero; return Native.E_NOTIMPL; }
        public int OnAfterContextMenu(IShellItem psi, IntPtr pcmIn, ref Guid riid, out IntPtr ppv) { ppv = IntPtr.Zero; return Native.E_NOTIMPL; }
        public int OnBeforeStateImageChange(IShellItem psi) { return Native.S_OK; }
        public int OnGetDefaultIconIndex(IShellItem psi, out int a, out int b) { a = b = 0; return Native.E_NOTIMPL; }
    }

    public class FolderTree : Control
    {
        INameSpaceTreeControl nstc;
        TreeEvents events;
        uint cookie;
        IntPtr treeHwnd;
        bool suppress;
        string selectedPath;
        readonly Timer navTimer = new Timer { Interval = 120 };
        readonly Timer revealTimer = new Timer { Interval = 250 };
        int revealTries;
        string revealFrom;      // 칸 폴더를 고르기 직전 트리 선택 (이게 바뀌면 사용자가 고른 것)
        string pendingNav;
        // 트리 이벤트 연결(TreeAdvise)이 이 환경에서 실패(E_FAIL)하므로, 선택을 짧게 확인해 사용자 선택을 알아냄
        readonly Timer pollTimer = new Timer { Interval = 150 };
        string lastSeen;

        /// <summary>사용자가 트리에서 폴더를 고름 (경로, 가운데 클릭이면 true)</summary>
        public event Action<string, bool> FolderPicked;
        /// <summary>트리에서 붙여넣기 시작 직전 / 끝난 직후 (붙여넣을 폴더 경로)</summary>
        public event Action<string> PasteStarting, PasteFinished;

        // 윈도우 탐색기처럼: 트리에서 폴더를 고르면 포커스는 트리에 남는다 (보기가 이동하며 포커스를 가져가면 되돌림)
        readonly Timer guardTimer = new Timer { Interval = 50 };
        int guardTicks;

        void KeepFocusInTree()
        {
            guardTicks = 0;
            guardTimer.Stop(); guardTimer.Start();
        }

        void GuardTick()
        {
            if (++guardTicks > 30 || Native.KeyDown(0x01) || Native.KeyDown(0x02)) { guardTimer.Stop(); return; }   // 1.5초 또는 사용자가 다른 곳을 누름
            var f = Native.GetFocus();
            if (f == IntPtr.Zero || IsTreeWindow(f)) return;
            if (FindForm() == null || !Native.IsChild(FindForm().Handle, f)) return;      // 다른 프로그램으로 넘어간 경우는 건드리지 않음
            Native.SetFocus(treeHwnd);
        }

        public FolderTree()
        {
            SetStyle(ControlStyles.Selectable, false);
            pollTimer.Tick += delegate { PollSelection(); };
            pollTimer.Start();
            guardTimer.Tick += delegate { GuardTick(); };
            // 하위 폴더 읽기가 비동기라 펼친 뒤 조금 있다가 다시 보이게 함
            revealTimer.Tick += delegate
            {
                if (++revealTries >= 4) revealTimer.Stop();
                Ensure(selectedPath);
            };
            navTimer.Tick += delegate
            {
                navTimer.Stop();
                var p = pendingNav; pendingNav = null;
                if (p != null && FolderPicked != null) { FolderPicked(p, false); KeepFocusInTree(); }
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            nstc = (INameSpaceTreeControl)Activator.CreateInstance(Type.GetTypeFromCLSID(Native.CLSID_NameSpaceTreeControl));
            var rc = new RECT(0, 0, Math.Max(1, Width), Math.Max(1, Height));
            uint style = Native.NSTCS_HASEXPANDOS | Native.NSTCS_ROOTHASEXPANDO | Native.NSTCS_FADEINOUTEXPANDOS |
                         Native.NSTCS_SHOWSELECTIONALWAYS | Native.NSTCS_AUTOHSCROLL | Native.NSTCS_TABSTOP |
                         Native.NSTCS_NOINFOTIP | Native.NSTCS_EVENHEIGHT;
            events = new TreeEvents(this);
            int hrAdv = nstc.TreeAdvise(events, out cookie);
            nstc.Initialize(Handle, ref rc, style);
            if (hrAdv != 0) hrAdv = nstc.TreeAdvise(events, out cookie);
            Dbg("TreeAdvise hr=" + hrAdv.ToString("X") + " cookie=" + cookie);
            nstc.SetTheme(Theme.Current.Dark ? "DarkMode_Explorer" : "Explorer");
            var ow = nstc as IOleWindow;
            if (ow != null) ow.GetWindow(out treeHwnd);
            Theme.ApplyToWindow(treeHwnd);
            ApplyTreeColors();
            LoadRoots();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (treeHwnd != IntPtr.Zero) Native.SetWindowPos(treeHwnd, IntPtr.Zero, 0, 0, Width, Height, 0x14 /* NOZORDER|NOACTIVATE */);
        }

        protected override void Dispose(bool disposing)
        {
            if (nstc != null)
            {
                try { if (cookie != 0) nstc.TreeUnadvise(cookie); nstc.RemoveAllRoots(); } catch { }
                Marshal.ReleaseComObject(nstc);
                nstc = null;
            }
            base.Dispose(disposing);
        }

        public IntPtr TreeWindow { get { return treeHwnd; } }

        /// <summary>
        /// 트리의 실제 선택이 바뀌었는데 앱이 고른 폴더(selectedPath)가 아니면 → 사용자가 고른 것 → 활성 칸 이동
        /// </summary>
        void PollSelection()
        {
            if (nstc == null || suppress) return;
            if (cutShown.Count > 0 && GetClipboardSequenceNumber() != cutSeq) ClearCutMarks();   // 다른 곳에서 복사·잘라냄·ESC → 트리 흐림 해제
            string sel = SelectedNow();
            if (sel == null || Shell.SamePath(sel, lastSeen)) return;
            lastSeen = sel;
            if (SelectedCount() > 1) return;      // 여러 개 고르는 중에는 칸 이동 안 함
            if (Shell.SamePath(sel, selectedPath)) return;       // 앱이 칸에 맞춰 고른 것
            Dbg("사용자 선택 " + sel);
            revealTimer.Stop();   // 칸 폴더로 되돌리는 재조정 중지
            selectedPath = sel;
            pendingNav = sel;
            navTimer.Stop(); navTimer.Start();
        }

        /// <summary>마우스 위치(화면 좌표)의 트리 항목 경로</summary>
        public string PathAt(Point screen)
        {
            if (nstc == null) return null;
            IShellItem item;
            var pt = new POINT { X = screen.X, Y = screen.Y };
            if (nstc.HitTest(ref pt, out item) != 0 || item == null)
            {
                // 트리 기준 좌표를 받는 경우
                var tv = FindTreeView(treeHwnd);
                pt = new POINT { X = screen.X, Y = screen.Y };
                Native.ScreenToClient(tv, ref pt);
                if (nstc.HitTest(ref pt, out item) != 0 || item == null) return null;
            }
            try { return Shell.ParsingName(item); }
            finally { Marshal.ReleaseComObject(item); }
        }

        /// <summary>트리 안의 창인지 (MainForm 이 마우스 처리에 씀)</summary>
        public bool IsTreeWindow(IntPtr hwnd)
        {
            return treeHwnd != IntPtr.Zero && (hwnd == treeHwnd || Native.IsChild(treeHwnd, hwnd));
        }

        /// <summary>
        /// 트리에서 마우스를 누름 (트리가 끌기 감지로 버튼 떼기를 직접 가져가므로 누를 때 처리).
        /// 이미 선택된 폴더를 다시 누르면 선택 변화가 없어 감시로 못 잡으므로 조금 뒤 직접 확인해 활성 칸 이동
        /// </summary>
        public void OnMouseDownInTree(Point screen)
        {
            var p = PathAt(screen);
            if (p == null) return;
            var t = new Timer { Interval = 200 };
            t.Tick += delegate
            {
                t.Stop(); t.Dispose();
                if (SelectedCount() > 1 || Native.KeyDown(0x11) || Native.KeyDown(0x10)) return;   // 다중 선택 중
                if (!Shell.SamePath(p, SelectedNow())) return;   // 펼침 단추를 눌렀거나 선택이 안 바뀜
                selectedPath = p; lastSeen = p;
                if (FolderPicked != null) { FolderPicked(p, false); KeepFocusInTree(); }
            };
            t.Start();
        }

        /// <summary>가운데 클릭 → 새 탭</summary>
        public void OnMiddleClickInTree(Point screen)
        {
            var p = PathAt(screen);
            if (p != null && FolderPicked != null) FolderPicked(p, true);
        }
        /// <summary>시험용: 사용자가 고른 것처럼 트리 선택만 바꿈</summary>
        public void DebugSelect(string path)
        {
            var item = Shell.ItemFromPath(path);
            if (item == null || nstc == null) return;
            nstc.SetItemState(item, Native.NSTCIS_SELECTED, Native.NSTCIS_SELECTED);
            Marshal.ReleaseComObject(item);
        }

        /// <summary>시험용 기록</summary>
        public static Action<string> DebugLog;
        internal static void Dbg(string s) { if (DebugLog != null) DebugLog(s); }

        /// <summary>트리에서 지금 선택된 폴더</summary>
        public string SelectedNow()
        {
            IShellItemArray a;
            if (nstc == null || nstc.GetSelectedItems(out a) != 0 || a == null) return null;
            try
            {
                uint n; IShellItem it;
                if (a.GetCount(out n) != 0 || n == 0 || a.GetItemAt(0, out it) != 0 || it == null) return null;
                try { return Shell.ParsingName(it); } finally { Marshal.ReleaseComObject(it); }
            }
            finally { Marshal.ReleaseComObject(a); }
        }

        /// <summary>트리에서 지금 선택된 파일 시스템 폴더들 (다중 선택)</summary>
        public System.Collections.Generic.List<string> SelectedPaths()
        {
            var list = new System.Collections.Generic.List<string>();
            IShellItemArray a;
            if (nstc == null || nstc.GetSelectedItems(out a) != 0 || a == null) return list;
            try
            {
                uint n;
                if (a.GetCount(out n) != 0) return list;
                for (uint i = 0; i < n; i++)
                {
                    IShellItem it;
                    if (a.GetItemAt(i, out it) != 0 || it == null) continue;
                    try
                    {
                        var p = Shell.ParsingName(it);
                        if (p != null && Shell.IsFileSystemPath(p) && Directory.Exists(p)) list.Add(p);
                    }
                    finally { Marshal.ReleaseComObject(it); }
                }
            }
            finally { Marshal.ReleaseComObject(a); }
            return list;
        }

        int SelectedCount()
        {
            IShellItemArray a;
            if (nstc == null || nstc.GetSelectedItems(out a) != 0 || a == null) return 0;
            try { uint n; return a.GetCount(out n) == 0 ? (int)n : 0; }
            finally { Marshal.ReleaseComObject(a); }
        }

        /// <summary>트리에 포커스가 있을 때 Ctrl+C / X / V, Delete 처리. 처리했으면 true</summary>
        public bool HandleKey(Keys key, bool ctrl, bool shift)
        {
            if (nstc == null) return false;
            if (ctrl && !shift && (key == Keys.C || key == Keys.X))
            {
                var sel = SelectedPaths();
                if (sel.Count == 0) return false;
                bool cut = key == Keys.X;
                // 셸 데이터 개체로 담아야 윈도우가 잘라낸 폴더를 흐리게(고스트) 표시한다
                if (!SetShellClipboard(cut ? 2 : 1))
                {
                    var list = new System.Collections.Specialized.StringCollection();
                    foreach (var s in sel) list.Add(s);
                    var data = new DataObject();
                    data.SetFileDropList(list);
                    data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
                    try { Clipboard.SetDataObject(data, true); } catch { }
                }
                // 이전에 잘라냈던 항목은 흐림을 풀고, 이번에 잘라낸 항목은 흐리게 다시 그림
                ClearCutMarks();
                if (cut)
                {
                    foreach (var p in sel) { var h = FindItemHandle(p); if (h != IntPtr.Zero) { SetCutState(h, true); cutShown.Add(h); } }
                    cutSeq = GetClipboardSequenceNumber();
                }
                return true;
            }
            if (ctrl && !shift && key == Keys.V) { PasteIntoSelected(); return true; }
            if (!ctrl && !shift && key == Keys.Delete) { DeleteSelectedFolders(); return true; }
            return false;
        }

        readonly System.Collections.Generic.List<IntPtr> cutShown = new System.Collections.Generic.List<IntPtr>();
        uint cutSeq;      // 잘라내기를 담았을 때의 클립보드 번호 (바뀌면 다른 곳에서 복사·잘라낸 것)

        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

        [StructLayout(LayoutKind.Sequential)]
        struct TVITEM
        {
            public uint mask; public IntPtr hItem; public uint state, stateMask;
            public IntPtr pszText; public int cchTextMax, iImage, iSelectedImage, cChildren; public IntPtr lParam;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct TVHITTESTINFO { public POINT pt; public uint flags; public IntPtr hItem; }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMsgStruct(IntPtr hwnd, int msg, IntPtr w, ref TVHITTESTINFO l);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMsgItem(IntPtr hwnd, int msg, IntPtr w, ref TVITEM l);

        /// <summary>그 폴더의 트리 항목 핸들 (화면에 보여야 찾을 수 있음)</summary>
        IntPtr FindItemHandle(string path)
        {
            var tv = FindTreeView(treeHwnd);
            if (tv == IntPtr.Zero || nstc == null) return IntPtr.Zero;
            var item = Shell.ItemFromPath(path);
            if (item == null) return IntPtr.Zero;
            try
            {
                RECT r;
                if (nstc.GetItemRect(item, out r) != 0) return IntPtr.Zero;          // 화면에 안 보이면 못 찾음
                var pt = new POINT { X = r.Left + 20, Y = (r.Top + r.Bottom) / 2 };
                Native.ScreenToClient(tv, ref pt);
                var hit = new TVHITTESTINFO { pt = pt };
                SendMsgStruct(tv, 0x1100 + 17 /* TVM_HITTEST */, IntPtr.Zero, ref hit);
                return hit.hItem;
            }
            catch { return IntPtr.Zero; }
            finally { Marshal.ReleaseComObject(item); }
        }

        void SetCutState(IntPtr hItem, bool cut)
        {
            var tv = FindTreeView(treeHwnd);
            if (tv == IntPtr.Zero || hItem == IntPtr.Zero) return;
            var ti = new TVITEM { mask = 0x8 /* TVIF_STATE */, hItem = hItem, state = cut ? 4u : 0u, stateMask = 4 /* TVIS_CUT */ };
            SendMsgItem(tv, 0x1100 + 63 /* TVM_SETITEMW */, IntPtr.Zero, ref ti);
        }

        /// <summary>시험용: 그 폴더가 지금 잘라내기 상태로 보이는지</summary>
        public bool DebugCutMarked(string path)
        {
            var tv = FindTreeView(treeHwnd);
            var h = FindItemHandle(path);
            if (tv == IntPtr.Zero || h == IntPtr.Zero) return false;
            var ti = new TVITEM { mask = 0x8, hItem = h, stateMask = 4 };
            SendMsgItem(tv, 0x1100 + 62 /* TVM_GETITEMW */, IntPtr.Zero, ref ti);
            return (ti.state & 4) != 0;
        }

        [DllImport("ole32.dll")] static extern int OleSetClipboard(IntPtr pDataObj);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);

        /// <summary>트리에서 고른 항목의 셸 데이터 개체를 클립보드에 담음 (effect 1=복사, 2=잘라내기)</summary>
        bool SetShellClipboard(int effect)
        {
            IShellItemArray a;
            if (nstc == null || nstc.GetSelectedItems(out a) != 0 || a == null) return false;
            IntPtr pdo = IntPtr.Zero;
            try
            {
                var bhid = new Guid("B8C0BD9F-ED24-455c-83E6-D5390C4FE8C4");   // BHID_DataObject
                var iid = new Guid("0000010e-0000-0000-C000-000000000046");    // IDataObject
                if (a.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out pdo) != 0 || pdo == IntPtr.Zero) return false;
                var dobj = (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(pdo);
                var h = Marshal.AllocHGlobal(4);
                Marshal.WriteInt32(h, effect);
                var fmt = new System.Runtime.InteropServices.ComTypes.FORMATETC
                {
                    cfFormat = (short)RegisterClipboardFormat("Preferred DropEffect"), ptd = IntPtr.Zero,
                    dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1,
                    tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
                };
                var med = new System.Runtime.InteropServices.ComTypes.STGMEDIUM
                {
                    tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL, unionmember = h, pUnkForRelease = null
                };
                dobj.SetData(ref fmt, ref med, true);
                return OleSetClipboard(pdo) == 0;
            }
            catch { return false; }
            finally
            {
                if (pdo != IntPtr.Zero) Marshal.Release(pdo);
                Marshal.ReleaseComObject(a);
            }
        }

        void ClearCutMarks()
        {
            foreach (var h in cutShown) SetCutState(h, false);
            cutShown.Clear();
        }

        /// <summary>클립보드에 '잘라내기'가 들어 있는지 (트리·보기 어디서 잘랐든)</summary>
        static bool CutOnClipboard()
        {
            try
            {
                if (!Clipboard.ContainsFileDropList()) return false;
                var d = Clipboard.GetDataObject();
                var ms = d == null ? null : d.GetData("Preferred DropEffect") as MemoryStream;
                return ms != null && ms.Length >= 4 && (BitConverter.ToInt32(ms.ToArray(), 0) & 2) != 0;
            }
            catch { return false; }
        }

        /// <summary>ESC: 잘라내기 취소 (윈도우 탐색기와 같음). 클립보드를 비우고 트리의 흐림도 푼다</summary>
        public void CancelCut()
        {
            if (CutOnClipboard()) { try { Clipboard.Clear(); } catch { } }
            ClearCutMarks();
        }

        void PasteIntoSelected()
        {
            var sel = SelectedPaths();
            if (sel.Count == 0) return;
            string dest = sel[0];
            System.Collections.Specialized.StringCollection src;
            bool move = false;
            try
            {
                if (!Clipboard.ContainsFileDropList()) return;
                src = Clipboard.GetFileDropList();
                var d = Clipboard.GetDataObject();
                var ms = d == null ? null : d.GetData("Preferred DropEffect") as MemoryStream;
                if (ms != null && ms.Length >= 4) move = (BitConverter.ToInt32(ms.ToArray(), 0) & 2) != 0;
            }
            catch { return; }
            var owner = FindForm();
            if (PasteStarting != null) PasteStarting(dest);
            foreach (var s0 in src)
            {
                string s = s0.TrimEnd('\\');
                bool isDir = Directory.Exists(s);
                if (!isDir && !File.Exists(s)) continue;
                string name = Path.GetFileName(s);
                string target = Path.Combine(dest, name);
                if (isDir && (Shell.SamePath(s, dest) || dest.StartsWith(s + "\\", StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(owner, "'" + name + "' 폴더를 자기 자신 안으로는 옮기거나 복사할 수 없습니다.", "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                bool sameParent = Shell.SamePath(Path.GetDirectoryName(s), dest);
                if (move && sameParent) continue;
                if (!move && sameParent) target = UniqueCopyName(dest, name, isDir);
                try
                {
                    var ui = Microsoft.VisualBasic.FileIO.UIOption.AllDialogs;
                    if (isDir)
                    {
                        if (move) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(s, target, ui);
                        else Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(s, target, ui);
                    }
                    else
                    {
                        if (move) Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(s, target, ui);
                        else Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(s, target, ui);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, (move ? "이동" : "복사") + "하지 못했습니다.\n" + ex.Message, "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                }
            }
            if (move) try { Clipboard.Clear(); } catch { }
            ClearCutMarks();
            if (PasteFinished != null) PasteFinished(dest);
        }

        static string UniqueCopyName(string dir, string name, bool isDir)
        {
            string stem = isDir ? name : Path.GetFileNameWithoutExtension(name);
            string ext = isDir ? "" : Path.GetExtension(name);
            string t = Path.Combine(dir, stem + " - 복사본" + ext);
            for (int i = 2; File.Exists(t) || Directory.Exists(t); i++) t = Path.Combine(dir, stem + " - 복사본 (" + i + ")" + ext);
            return t;
        }

        void DeleteSelectedFolders()
        {
            var sel = SelectedPaths();
            if (sel.Count == 0) return;
            var owner = FindForm();
            string ask = sel.Count == 1
                ? "'" + Path.GetFileName(sel[0].TrimEnd('\\')) + "' 폴더를 휴지통으로 보낼까요?"
                : "선택한 폴더 " + sel.Count + "개를 휴지통으로 보낼까요?";
            if (MessageBox.Show(owner, ask, "폴더 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var p in sel)
            {
                if (Path.GetPathRoot(p).TrimEnd('\\').Equals(p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;   // 드라이브 뿌리는 지우지 않음
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, "삭제하지 못했습니다.\n" + ex.Message, "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                }
            }
        }

        /// <summary>시험용: 그 폴더 항목을 보이게 한 뒤 진짜 마우스 클릭 메시지를 보냄</summary>
        public string DebugClick(string path)
        {
            var item = Shell.ItemFromPath(path);
            if (item == null || nstc == null) return "항목 없음";
            try
            {
                nstc.EnsureItemVisible(item);
                RECT r;
                if (nstc.GetItemRect(item, out r) != 0) return "GetItemRect 실패";
                var tv = FindTreeView(treeHwnd);
                var pt = new POINT { X = r.Left + 30, Y = (r.Top + r.Bottom) / 2 };
                Native.ScreenToClient(tv, ref pt);    // GetItemRect 는 화면 좌표
                int x = pt.X, y = pt.Y;
                var lp = (IntPtr)((y << 16) | (x & 0xFFFF));
                Native.PostMessage(tv, Native.WM_LBUTTONDOWN, (IntPtr)1, lp);
                Native.PostMessage(tv, Native.WM_LBUTTONUP, IntPtr.Zero, lp);
                return "클릭 " + x + "," + y + " 선택=" + SelectedNow() + " tv=" + Native.ClassName(tv) + (tv == treeHwnd ? "(같은 창)" : "(자식 창)");
            }
            finally { Marshal.ReleaseComObject(item); }
        }

        const int TVM_SETBKCOLOR = 0x1100 + 29, TVM_SETTEXTCOLOR = 0x1100 + 30;

        /// <summary>안쪽 트리 창(SysTreeView32)의 바탕·글자색 (다크 테마가 바탕을 다 바꾸지 못할 때 보완)</summary>
        void ApplyTreeColors()
        {
            var tv = FindTreeView(treeHwnd);
            if (tv == IntPtr.Zero) return;
            Theme.ApplyToWindow(tv);
            var T = Theme.Current;
            int bk = T.Dark ? ColorTranslator.ToWin32(T.Content) : -1;
            int fg = T.Dark ? ColorTranslator.ToWin32(T.Text) : -1;
            Native.SendMessage(tv, TVM_SETBKCOLOR, IntPtr.Zero, (IntPtr)bk);
            Native.SendMessage(tv, TVM_SETTEXTCOLOR, IntPtr.Zero, (IntPtr)fg);
        }

        static IntPtr FindTreeView(IntPtr h)
        {
            if (h == IntPtr.Zero) return IntPtr.Zero;
            if (Native.ClassName(h) == "SysTreeView32") return h;
            return Native.FindWindowEx(h, IntPtr.Zero, "SysTreeView32", null);
        }

        /// <summary>테마가 바뀌면 트리를 새로 만듦 (선택 폴더 유지)</summary>
        public void Rebuild()
        {
            if (nstc == null) return;
            var keep = selectedPath;
            try { if (cookie != 0) nstc.TreeUnadvise(cookie); nstc.RemoveAllRoots(); } catch { }
            if (treeHwnd != IntPtr.Zero) Native.DestroyWindow(treeHwnd);
            Marshal.ReleaseComObject(nstc);
            nstc = null; cookie = 0; treeHwnd = IntPtr.Zero; cutShown.Clear();
            selectedPath = keep;
            OnHandleCreated(EventArgs.Empty);
            OnResize(EventArgs.Empty);
        }

        /// <summary>트리 뿌리: 드라이브만 / 탐색기처럼 전체(즐겨찾기·바탕 화면·내 PC·네트워크 ...)</summary>
        public void LoadRoots()
        {
            if (nstc == null) return;
            suppress = true;
            try
            {
                nstc.RemoveAllRoots();
                uint flags = Native.SHCONTF_FOLDERS | Native.SHCONTF_NAVIGATION_ENUM;
                if (Settings.ExplorerShowsHidden()) flags |= Native.SHCONTF_INCLUDEHIDDEN;
                if (Settings.Current.TreeDrivesOnly)
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        var item = Shell.ItemFromPath(d.Name);
                        if (item == null) continue;
                        nstc.AppendRoot(item, flags, Native.NSTCRS_VISIBLE, IntPtr.Zero);
                        Marshal.ReleaseComObject(item);
                    }
                }
                else
                {
                    string[] roots =
                    {
                        "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}",   // 즐겨찾기(빠른 실행)
                        "shell:Desktop",
                        "shell:Libraries",
                        Shell.ThisPC,
                        "shell:::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"    // 네트워크
                    };
                    foreach (var r in roots)
                    {
                        var item = Shell.ItemFromPath(r);
                        if (item == null) continue;
                        nstc.AppendRoot(item, flags, r == Shell.ThisPC ? Native.NSTCRS_EXPANDED : Native.NSTCRS_VISIBLE, IntPtr.Zero);
                        Marshal.ReleaseComObject(item);
                    }
                }
            }
            catch { }
            finally { suppress = false; }
            var sp = selectedPath; selectedPath = null;
            if (sp != null) SelectPath(sp);
        }

        /// <summary>칸이 바뀌거나 이동하면 그 폴더를 선택하고 펼침</summary>
        public void SelectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (nstc == null) { selectedPath = path; return; }   // 트리가 만들어지면 LoadRoots 에서 선택
            if (Shell.SamePath(path, selectedPath)) return;
            selectedPath = path;
            revealFrom = SelectedNow();
            var item = Shell.ItemFromPath(path);
            if (item == null) return;
            suppress = true;
            try
            {
                nstc.EnsureItemVisible(item);
                nstc.SetItemState(item, Native.NSTCIS_SELECTED | Native.NSTCIS_EXPANDED, Native.NSTCIS_SELECTED | Native.NSTCIS_EXPANDED);
                nstc.EnsureItemVisible(item);
            }
            catch { }
            finally { suppress = false; Marshal.ReleaseComObject(item); }
            revealTries = 0;
            revealTimer.Stop(); revealTimer.Start();
        }

        void Ensure(string path)
        {
            if (nstc == null || path == null) return;
            // 그 사이 사용자가 다른 폴더를 골랐으면 되돌리지 않음
            var cur = SelectedNow();
            if (cur != null && !Shell.SamePath(cur, path) && !Shell.SamePath(cur, revealFrom)) { revealTimer.Stop(); return; }
            var item = Shell.ItemFromPath(path);
            if (item == null) return;
            suppress = true;
            try
            {
                uint st;
                if (nstc.GetItemState(item, Native.NSTCIS_SELECTED, out st) == 0 && (st & Native.NSTCIS_SELECTED) == 0)
                    nstc.SetItemState(item, Native.NSTCIS_SELECTED | Native.NSTCIS_EXPANDED, Native.NSTCIS_SELECTED | Native.NSTCIS_EXPANDED);
                nstc.EnsureItemVisible(item);
            }
            catch { }
            finally { suppress = false; Marshal.ReleaseComObject(item); }
        }

        internal void SelectionChanged(IShellItemArray sel)
        {
            if (suppress || sel == null) return;
            uint n;
            if (sel.GetCount(out n) != 0 || n == 0) return;
            IShellItem item;
            if (sel.GetItemAt(0, out item) != 0 || item == null) return;
            var p = Shell.ParsingName(item);
            Marshal.ReleaseComObject(item);
            if (p == null) return;
            selectedPath = p;
            pendingNav = p;
            navTimer.Stop(); navTimer.Start();
        }

        internal int ItemClick(IShellItem psi, int hit, int click)
        {
            const int ONITEM = 0x46, MBUTTON = 2, LBUTTON = 1, BUTTONMASK = 3;
            if ((hit & ONITEM) == 0 || psi == null) return Native.S_FALSE;
            var p = Shell.ParsingName(psi);
            if (p == null) return Native.S_FALSE;
            int btn = click & BUTTONMASK;
            if (btn == MBUTTON)
            {
                // 가운데 클릭 → 새 탭
                if (FolderPicked != null) FolderPicked(p, true);
                return Native.S_OK;
            }
            uint st;
            if (btn == LBUTTON && nstc.GetItemState(psi, Native.NSTCIS_SELECTED, out st) == 0 && (st & Native.NSTCIS_SELECTED) != 0)
            {
                // 이미 선택된 항목을 다시 누름 → 선택 변경 이벤트가 없으므로 직접 이동
                pendingNav = p;
                navTimer.Stop(); navTimer.Start();
            }
            return Native.S_FALSE;
        }

        /// <summary>선택 항목이 보이게 다시 스크롤 (창 크기가 정해진 뒤)</summary>
        public void Reveal()
        {
            var p = selectedPath; selectedPath = null;
            if (p != null) SelectPath(p);
        }

        public void FocusTree() { if (treeHwnd != IntPtr.Zero) Native.SetFocus(treeHwnd); }
    }
}
