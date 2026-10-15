// 검색 결과 보기: 현재 폴더(하위 폴더 포함)에서 이름으로 찾아 몽탐색기 안에 목록으로 보여 줌 (윈도우 탐색기를 열지 않음)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace NewExplorer
{
    public class SearchHit
    {
        public string Path, Name, Dir, Type;
        public bool IsDir;
        public DateTime Modified;
        public long Size;
    }

    /// <summary>이름 검색어 해석: 공백으로 나눈 낱말이 모두 들어 있어야 맞음. * ? 가 있으면 이름 전체 패턴, 큰따옴표로 묶으면 한 낱말</summary>
    public class SearchMatcher
    {
        readonly List<string> subs = new List<string>();
        readonly List<Regex> pats = new List<Regex>();

        public SearchMatcher(string query)
        {
            var terms = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            foreach (char c in query)
            {
                if (c == '"') { quoted = !quoted; continue; }
                if (char.IsWhiteSpace(c) && !quoted) { if (cur.Length > 0) { terms.Add(cur.ToString()); cur.Length = 0; } continue; }
                cur.Append(c);
            }
            if (cur.Length > 0) terms.Add(cur.ToString());
            foreach (var t in terms)
            {
                if (t.IndexOfAny(new[] { '*', '?' }) >= 0)
                    pats.Add(new Regex("^" + Regex.Escape(t).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));
                else subs.Add(t);
            }
        }

        public bool IsEmpty { get { return subs.Count == 0 && pats.Count == 0; } }

        public bool Match(string name)
        {
            foreach (var s in subs) if (name.IndexOf(s, StringComparison.OrdinalIgnoreCase) < 0) return false;
            foreach (var p in pats) if (!p.IsMatch(name)) return false;
            return true;
        }
    }

    public class SearchView : Panel
    {
        class Run
        {
            public volatile bool Cancel, Done;
            public volatile string Current;
            public int Found;
        }

        const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_TYPENAME = 0x400, SHGFI_USEFILEATTRIBUTES = 0x10;
        const uint ATTR_DIR = 0x10, ATTR_NORMAL = 0x80;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool SHObjectProperties(IntPtr hwnd, uint type, string name, string page);

        readonly ListView lv;
        readonly Label status;
        readonly ImageList icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
        readonly Dictionary<string, int> iconIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> typeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<SearchHit> hits = new List<SearchHit>();
        readonly List<SearchHit> pending = new List<SearchHit>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 150 };
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        Run run;
        string query = "", where = "";
        int sortCol = -1;
        bool sortDesc;

        public event Action<string> OpenFolderRequested;               // 폴더 결과를 열기
        public event Action<string, string> ShowInFolderRequested;     // (폴더, 이름) 포함 폴더로 가서 선택
        public event Action<string> NewTabRequested;                   // 포함 폴더를 새 탭에서
        public event Action CloseRequested;                            // 검색 결과 닫기 (Backspace, Esc)

        public SearchView()
        {
            lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, VirtualMode = true, FullRowSelect = true,
                HideSelection = false, MultiSelect = true, BorderStyle = BorderStyle.None, Font = UI.Font,
                SmallImageList = icons, ShowItemToolTips = false
            };
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(lv, true, null);
            lv.Columns.Add("이름", 330);
            lv.Columns.Add("위치", 260);
            lv.Columns.Add("수정한 날짜", 150);
            lv.Columns.Add("유형", 110);
            lv.Columns.Add("크기", 80, HorizontalAlignment.Right);
            status = new Label { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), Font = UI.Font, AutoEllipsis = true };
            Controls.Add(lv);
            Controls.Add(status);

            // 열 머리글은 직접 그려 테마 글자색을 확실히 적용 (항목은 기본 그리기)
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += OnDrawHeader;
            lv.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
            lv.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
            lv.RetrieveVirtualItem += OnRetrieve;
            lv.ColumnClick += OnColumnClick;
            lv.ItemActivate += delegate { OpenSelected(); };
            lv.KeyDown += OnKeyDown;
            lv.ItemDrag += OnItemDrag;
            lv.SelectedIndexChanged += delegate { UpdateStatus(); };
            lv.VirtualItemsSelectionRangeChanged += delegate { UpdateStatus(); };
            lv.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) ShowMenu(); };
            lv.HandleCreated += delegate { ApplyListTheme(); };
            timer.Tick += delegate { Flush(); };
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            var T = Theme.Current;
            BackColor = T.Content;
            lv.BackColor = T.Content;
            lv.ForeColor = T.Text;
            status.BackColor = T.Panel;
            status.ForeColor = T.SubText;
            if (lv.IsHandleCreated) ApplyListTheme();
            lv.Invalidate();
        }

        // 목록은 탐색기 테마, 열 머리글은 다크일 때 별도 테마(ItemsView)를 줘야 어둡게 그려짐
        void ApplyListTheme()
        {
            Theme.ApplyToWindow(lv.Handle);
            if (Theme.Current.Dark) SetWindowTheme(lv.Handle, "DarkMode_ItemsView", null);   // 윈도우 탐색기 목록과 같은 어두운 테마
            try
            {
                IntPtr hdr = Native.SendMessage(lv.Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero);
                if (hdr != IntPtr.Zero) SetWindowTheme(hdr, Theme.Current.Dark ? "DarkMode_ItemsView" : "Explorer", null);
            }
            catch { }
        }

        public int Count { get { return hits.Count; } }
        public bool Finished { get { return run != null && (run.Done || run.Cancel) && !timer.Enabled; } }
        public string FirstName { get { return hits.Count > 0 ? hits[0].Path : null; } }

        public void FocusList() { lv.Focus(); if (lv.SelectedIndices.Count == 0 && hits.Count > 0) { lv.SelectedIndices.Add(0); } }

        // ───── 검색 ─────
        /// <summary>roots 아래를 모두 훑어 이름이 맞는 항목을 모은다. 앞선 검색은 멈춘다</summary>
        public void Start(string q, string[] roots, string whereLabel)
        {
            Stop();
            query = q;
            where = whereLabel;
            hits.Clear();
            lock (pending) pending.Clear();
            lv.VirtualListSize = 0;
            var r = run = new Run();
            var matcher = new SearchMatcher(q);
            bool showHidden = Settings.ExplorerShowsHidden();
            var t = new Thread(delegate() { Scan(r, roots, matcher, showHidden); }) { IsBackground = true, Name = "몽탐색기 검색" };
            t.Start();
            timer.Start();
            UpdateStatus();
        }

        public void Stop()
        {
            timer.Stop();
            if (run != null) run.Cancel = true;
        }

        void Scan(Run r, string[] roots, SearchMatcher m, bool showHidden)
        {
            try
            {
                var stack = new Stack<string>();
                for (int i = roots.Length - 1; i >= 0; i--) stack.Push(roots[i]);
                while (stack.Count > 0 && !r.Cancel)
                {
                    string dir = stack.Pop();
                    r.Current = dir;
                    var subdirs = new List<string>();
                    try
                    {
                        foreach (var fi in new DirectoryInfo(dir.EndsWith(@"\") ? dir : dir + @"\").EnumerateFileSystemInfos())
                        {
                            if (r.Cancel) return;
                            var a = fi.Attributes;
                            bool hidden = (a & FileAttributes.Hidden) != 0;
                            if (hidden && (!showHidden || (a & FileAttributes.System) != 0)) continue;
                            bool isDir = (a & FileAttributes.Directory) != 0;
                            if (!m.IsEmpty && m.Match(fi.Name))
                            {
                                var hit = new SearchHit { Path = fi.FullName, Name = fi.Name, Dir = dir, IsDir = isDir, Modified = fi.LastWriteTime };
                                if (!isDir) { try { hit.Size = ((FileInfo)fi).Length; } catch { } }
                                lock (pending) pending.Add(hit);
                                r.Found++;
                            }
                            if (isDir && (a & FileAttributes.ReparsePoint) == 0) subdirs.Add(fi.FullName);   // 바로 가기 폴더(링크)는 들어가지 않음 (무한 반복 방지)
                        }
                    }
                    catch { }      // 권한 없음·경로 너무 김 등은 건너뜀
                    for (int i = subdirs.Count - 1; i >= 0; i--) stack.Push(subdirs[i]);
                }
            }
            catch { }
            finally { r.Done = true; }
        }

        void Flush()
        {
            var r = run;
            bool changed = false;
            lock (pending)
            {
                if (pending.Count > 0) { hits.AddRange(pending); pending.Clear(); changed = true; }
            }
            if (changed) lv.VirtualListSize = hits.Count;
            if (r != null && (r.Done || r.Cancel))
            {
                // 일이 끝난 뒤 남은 것까지 한 번 더 가져옴
                lock (pending)
                {
                    if (pending.Count > 0) { hits.AddRange(pending); pending.Clear(); lv.VirtualListSize = hits.Count; }
                }
                timer.Stop();
                if (sortCol >= 0) ApplySort();
            }
            UpdateStatus();
        }

        void UpdateStatus()
        {
            var r = run;
            if (r == null) { status.Text = ""; return; }
            if (!r.Done && !r.Cancel)
                status.Text = "검색 중…  " + hits.Count.ToString("N0") + "개 찾음  |  " + r.Current;
            else
            {
                int sel = lv.SelectedIndices.Count;
                status.Text = hits.Count.ToString("N0") + "개 항목" + (sel > 0 ? "  |  " + sel.ToString("N0") + "개 항목 선택함" : "") +
                              "  |  '" + query + "' 검색 결과 (위치: " + where + ")";
            }
        }

        void OnDrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            var T = Theme.Current;
            using (var bg = new SolidBrush(T.Panel)) e.Graphics.FillRectangle(bg, e.Bounds);
            using (var pen = new Pen(T.Border))
            {
                e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 3, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            string text = e.Header.Text;
            if (e.ColumnIndex == sortCol) text += sortDesc ? "  ▼" : "  ▲";
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                        (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
            var r = Rectangle.Inflate(e.Bounds, -6, 0);
            TextRenderer.DrawText(e.Graphics, text, lv.Font, r, T.Text, flags);
        }

        // ───── 목록 그리기 ─────
        void OnRetrieve(object sender, RetrieveVirtualItemEventArgs e)
        {
            var h = e.ItemIndex >= 0 && e.ItemIndex < hits.Count ? hits[e.ItemIndex] : null;
            if (h == null) { e.Item = new ListViewItem(new string[5]); return; }
            if (h.Type == null) h.Type = TypeName(h);
            string size = h.IsDir ? "" : ((h.Size + 1023) / 1024).ToString("N0") + "KB";
            var it = new ListViewItem(new[] { h.Name, h.Dir, h.Modified.ToString("yy-MM-dd (ddd) tt h:mm"), h.Type, size });
            it.ImageIndex = IconIndex(h);
            e.Item = it;
        }

        static string ExtOf(SearchHit h)
        {
            if (h.IsDir) return "<dir>";
            string ext = Path.GetExtension(h.Name);
            return string.IsNullOrEmpty(ext) ? "<none>" : ext.ToLowerInvariant();
        }

        string TypeName(SearchHit h)
        {
            string key = ExtOf(h);
            string t;
            if (typeNames.TryGetValue(key, out t)) return t;
            var info = new SHFILEINFO();
            Native.SHGetFileInfoPath(h.IsDir ? "folder" : h.Name, h.IsDir ? ATTR_DIR : ATTR_NORMAL, ref info, Marshal.SizeOf(info), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES);
            t = string.IsNullOrEmpty(info.szTypeName) ? (h.IsDir ? "파일 폴더" : "파일") : info.szTypeName;
            typeNames[key] = t;
            return t;
        }

        int IconIndex(SearchHit h)
        {
            string ext = ExtOf(h);
            // 파일마다 아이콘이 다른 형식은 실제 파일에서, 나머지는 확장자별로 하나만 가져옴
            bool perFile = ext == ".exe" || ext == ".lnk" || ext == ".ico" || ext == ".url" || ext == ".scr" || ext == ".msi" || ext == ".cur";
            string key = perFile ? h.Path : ext;
            int idx;
            if (iconIndex.TryGetValue(key, out idx)) return idx;
            idx = -1;
            try
            {
                var info = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_SMALLICON | (perFile ? 0 : SHGFI_USEFILEATTRIBUTES);
                Native.SHGetFileInfoPath(perFile ? h.Path : (h.IsDir ? "folder" : h.Name), h.IsDir ? ATTR_DIR : ATTR_NORMAL, ref info, Marshal.SizeOf(info), flags);
                if (info.hIcon != IntPtr.Zero)
                {
                    using (var ic = Icon.FromHandle(info.hIcon)) icons.Images.Add(ic.ToBitmap());
                    Native.DestroyIcon(info.hIcon);
                    idx = icons.Images.Count - 1;
                }
            }
            catch { }
            iconIndex[key] = idx;
            return idx;
        }

        // ───── 정렬 ─────
        void OnColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (sortCol == e.Column) sortDesc = !sortDesc; else { sortCol = e.Column; sortDesc = false; }
            ApplySort();
        }

        void ApplySort()
        {
            int col = sortCol; bool desc = sortDesc;
            Comparison<SearchHit> cmp = delegate(SearchHit a, SearchHit b)
            {
                int c;
                switch (col)
                {
                    case 1: c = string.Compare(a.Dir, b.Dir, StringComparison.CurrentCultureIgnoreCase); break;
                    case 2: c = a.Modified.CompareTo(b.Modified); break;
                    case 3: c = string.Compare(a.Type ?? TypeName(a), b.Type ?? TypeName(b), StringComparison.CurrentCultureIgnoreCase); break;
                    case 4: c = a.Size.CompareTo(b.Size); break;
                    default: c = (b.IsDir ? 1 : 0) - (a.IsDir ? 1 : 0); if (c == 0) c = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); break;
                }
                return desc ? -c : c;
            };
            hits.Sort(cmp);
            lv.SelectedIndices.Clear();
            lv.Invalidate();
            lv.Refresh();
        }

        // ───── 항목 다루기 ─────
        List<SearchHit> Selected()
        {
            var l = new List<SearchHit>();
            foreach (int i in lv.SelectedIndices) if (i >= 0 && i < hits.Count) l.Add(hits[i]);
            return l;
        }

        void OpenSelected()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            foreach (var h in sel)
            {
                if (h.IsDir) { if (OpenFolderRequested != null) OpenFolderRequested(h.Path); return; }
                try { Process.Start(new ProcessStartInfo(h.Path) { UseShellExecute = true, WorkingDirectory = h.Dir }); }
                catch (Exception ex) { MessageBox.Show(FindForm(), "열 수 없습니다.\n" + ex.Message, "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }

        void ShowInFolder(bool newTab)
        {
            var sel = Selected();
            if (sel.Count != 1) return;
            if (newTab) { if (NewTabRequested != null) NewTabRequested(sel[0].Dir); }
            else if (ShowInFolderRequested != null) ShowInFolderRequested(sel[0].Dir, sel[0].Name);
        }

        void CopySelected()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            var list = new System.Collections.Specialized.StringCollection();
            foreach (var h in sel) list.Add(h.Path);
            try { Clipboard.SetFileDropList(list); } catch { }
        }

        void CopyPathText()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            var sb = new StringBuilder();
            foreach (var h in sel) { if (sb.Length > 0) sb.Append("\r\n"); sb.Append(h.Path); }
            try { Clipboard.SetText(sb.ToString()); } catch { }
        }

        void DeleteSelected()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            foreach (var h in sel)
            {
                try
                {
                    if (h.IsDir)
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(h.Path, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                    else
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(h.Path, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                    hits.Remove(h);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "삭제하지 못했습니다.\n" + ex.Message, "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                }
            }
            lv.SelectedIndices.Clear();
            lv.VirtualListSize = hits.Count;
            lv.Invalidate();
            UpdateStatus();
        }

        void ShowProperties()
        {
            var sel = Selected();
            if (sel.Count == 1) SHObjectProperties(Handle, 2 /* SHOP_FILEPATH */, sel[0].Path, null);
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Control) { e.SuppressKeyPress = true; OpenSelected(); }
            else if (e.KeyCode == Keys.Delete) { e.SuppressKeyPress = true; DeleteSelected(); }
            else if (e.Control && e.KeyCode == Keys.C) { e.SuppressKeyPress = true; CopySelected(); }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                e.SuppressKeyPress = true;
                for (int i = 0; i < lv.VirtualListSize; i++) lv.SelectedIndices.Add(i);
            }
            else if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                if (CloseRequested != null) CloseRequested();
            }
            else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; }
        }

        void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            var paths = new string[sel.Count];
            for (int i = 0; i < paths.Length; i++) paths[i] = sel[i].Path;
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, paths);
            lv.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }

        void ShowMenu()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            menu.Items.Clear();
            menu.Items.Add("열기", null, delegate { OpenSelected(); });
            var show = menu.Items.Add("포함 폴더 열기", null, delegate { ShowInFolder(false); });
            var tab = menu.Items.Add("포함 폴더를 새 탭에서 열기", null, delegate { ShowInFolder(true); });
            show.Enabled = tab.Enabled = sel.Count == 1;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("복사\tCtrl+C", null, delegate { CopySelected(); });
            menu.Items.Add("경로 복사", null, delegate { CopyPathText(); });
            menu.Items.Add("삭제\tDel", null, delegate { DeleteSelected(); });
            menu.Items.Add(new ToolStripSeparator());
            var prop = menu.Items.Add("속성", null, delegate { ShowProperties(); });
            prop.Enabled = sel.Count == 1;
            menu.Show(Cursor.Position);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
                timer.Dispose();
                menu.Dispose();
                icons.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
