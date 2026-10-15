// 칸 하나: 탭 줄 + 이동 버튼·경로 표시줄·검색 + 탐색기 보기 + 상태 표시줄
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NewExplorer
{
    /// <summary>그림으로 그리는 작은 버튼 (뒤로·앞으로·위로·새로 고침·보기 방식)</summary>
    public class GlyphButton : Control
    {
        public string Glyph;            // back, fwd, up, refresh, details, icons, preview
        bool hover, down;
        public bool Checked;
        readonly ToolTip tip = new ToolTip();

        public GlyphButton(string glyph, string tooltip)
        {
            Glyph = glyph;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            Size = new Size(28, 26);
            tip.SetToolTip(this, tooltip);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var T = Theme.Current;
            g.Clear(Parent != null ? Parent.BackColor : T.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Enabled && (hover || Checked))
                using (var b = new SolidBrush(down || Checked ? T.Pressed : T.Hover))
                    g.FillRectangle(b, 1, 1, Width - 2, Height - 2);
            var col = Enabled ? T.Text : T.DimText;
            float cx = Width / 2f, cy = Height / 2f;
            using (var pen = new Pen(col, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (Glyph)
                {
                    case "back":
                        g.DrawLine(pen, cx - 6, cy, cx + 6, cy);
                        g.DrawLines(pen, new[] { new PointF(cx - 1, cy - 5), new PointF(cx - 6, cy), new PointF(cx - 1, cy + 5) });
                        break;
                    case "fwd":
                        g.DrawLine(pen, cx - 6, cy, cx + 6, cy);
                        g.DrawLines(pen, new[] { new PointF(cx + 1, cy - 5), new PointF(cx + 6, cy), new PointF(cx + 1, cy + 5) });
                        break;
                    case "up":
                        g.DrawLine(pen, cx, cy - 6, cx, cy + 6);
                        g.DrawLines(pen, new[] { new PointF(cx - 5, cy - 1), new PointF(cx, cy - 6), new PointF(cx + 5, cy - 1) });
                        break;
                    case "refresh":
                        g.DrawArc(pen, cx - 6, cy - 6, 12, 12, -60, 300);
                        g.DrawLines(pen, new[] { new PointF(cx + 1, cy - 9), new PointF(cx + 4, cy - 5.5f), new PointF(cx, cy - 3) });
                        break;
                    case "details":
                        for (int i = -1; i <= 1; i++)
                        {
                            g.DrawLine(pen, cx - 6, cy + i * 4, cx - 4, cy + i * 4);
                            g.DrawLine(pen, cx - 1, cy + i * 4, cx + 6, cy + i * 4);
                        }
                        break;
                    case "icons":
                        g.DrawRectangle(pen, cx - 6, cy - 6, 5, 5); g.DrawRectangle(pen, cx + 1, cy - 6, 5, 5);
                        g.DrawRectangle(pen, cx - 6, cy + 1, 5, 5); g.DrawRectangle(pen, cx + 1, cy + 1, 5, 5);
                        break;
                    case "preview":
                        g.DrawRectangle(pen, cx - 7, cy - 6, 14, 12);
                        g.DrawLine(pen, cx + 1, cy - 6, cx + 1, cy + 6);
                        break;
                }
            }
        }
    }

    public class PaneControl : Control
    {
        public int Index;          // 화면 위치 순서 (칸 위치 바꾸기로 바뀜)
        public readonly List<TabItem> Tabs = new List<TabItem>();
        public int ActiveIndex = -1;
        public readonly TabStrip Strip;
        readonly Panel navBar;
        readonly GlyphButton btnBack, btnFwd, btnUp, btnRefresh;
        public readonly Breadcrumb Crumb;
        readonly TextBox search;
        readonly Panel content;
        readonly GlyphButton btnPreview;
        readonly Panel searchWrap;
        readonly FlowLayoutPanel btnFlow;
        bool isActive;

        public event EventHandler ActivateRequested;
        public event EventHandler ActivePathChanged;
        public event Action<PaneControl, int> TabMenuRequested;
        public event Action<PaneControl> TabsChanged;

        public PaneControl(int index)
        {
            Index = index;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Padding = new Padding(1);

            content = new Panel { Dock = DockStyle.Fill };

            navBar = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4, 5, 6, 5) };
            btnBack = new GlyphButton("back", "뒤로 (Alt+왼쪽 화살표) — 오른쪽 클릭: 기록");
            btnFwd = new GlyphButton("fwd", "앞으로 (Alt+오른쪽 화살표) — 오른쪽 클릭: 기록");
            btnUp = new GlyphButton("up", "위로 (Alt+위쪽 화살표)");
            btnRefresh = new GlyphButton("refresh", "새로 고침 (F5)");
            Crumb = new Breadcrumb { Dock = DockStyle.Fill };
            search = new TextBox { Dock = DockStyle.Right, Width = 170, BorderStyle = BorderStyle.FixedSingle, Font = UI.Font };
            searchWrap = new Panel { Dock = DockStyle.Right, Width = 178, Padding = new Padding(8, 1, 0, 0) };
            btnPreview = new GlyphButton("preview", "미리 보기 창 (Alt+P)") { Dock = DockStyle.Right };
            search.Dock = DockStyle.Fill;
            searchWrap.Controls.Add(search);
            btnFlow = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 4 * 28 + 6, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            foreach (var b in new[] { btnBack, btnFwd, btnUp, btnRefresh }) { b.Margin = new Padding(0); btnFlow.Controls.Add(b); }
            navBar.Controls.Add(Crumb);
            navBar.Controls.Add(btnPreview);
            navBar.Controls.Add(searchWrap);
            navBar.Controls.Add(btnFlow);

            Strip = new TabStrip(this) { Dock = DockStyle.Top };
            Strip.TabMenu += i => { if (TabMenuRequested != null) TabMenuRequested(this, i); };

            Controls.Add(content);
            Controls.Add(navBar);
            Controls.Add(Strip);

            btnBack.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) Back(); else if (e.Button == MouseButtons.Right) HistoryMenu(btnBack, true); };
            btnFwd.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) Forward(); else if (e.Button == MouseButtons.Right) HistoryMenu(btnFwd, false); };
            btnUp.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) Up(); };
            btnRefresh.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left && ActiveHost != null) ActiveHost.RefreshView(); };
            btnPreview.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left && PreviewToggleRequested != null) PreviewToggleRequested(); };
            Crumb.NavigateRequested += p => { RequestActivate(); Navigate(p); };
            Crumb.MiddleClicked += p => { RequestActivate(); AddTab(p, false); };
            Crumb.EditCancelled += FocusView;
            search.KeyDown += SearchKeyDown;
            foreach (Control c in new Control[] { navBar, btnFlow, searchWrap, Crumb, search })
                c.MouseDown += delegate { RequestActivate(); };
            ApplyTheme();
        }

        /// <summary>테마 색 다시 칠하기 (이동 줄은 고른 탭과 같은 색이라 탭이 이어져 보임)</summary>
        public void ApplyTheme()
        {
            var T = Theme.Current;
            BackColor = isActive ? T.Accent : T.Border;
            content.BackColor = T.Content;
            navBar.BackColor = btnFlow.BackColor = searchWrap.BackColor = T.TabActive;
            search.BackColor = T.Content;
            search.ForeColor = T.Text;
            Crumb.Invalidate();
            Strip.Invalidate();
            foreach (Control c in btnFlow.Controls) c.Invalidate();
            btnPreview.Invalidate();
            foreach (var t in Tabs) if (t.Search != null) t.Search.ApplyTheme();
            if (search.IsHandleCreated) Theme.ApplyToWindow(search.Handle);
        }

        /// <summary>다른 칸을 이 칸 위로 끌어 놓음 → 두 칸 위치 바꾸기</summary>
        public event Action<PaneControl, PaneControl> SwapRequested;
        public void RequestSwap(PaneControl from) { if (SwapRequested != null) SwapRequested(from, this); }

        public event Action PreviewToggleRequested;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SendMessageStr(search.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "검색");
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 칸이 좁으면 검색 칸을 줄여 경로 표시줄에 자리를 줌
            if (searchWrap != null) searchWrap.Width = Math.Max(110, Math.Min(260, Width / 5));
        }

        public bool IsActivePane
        {
            get { return isActive; }
            set
            {
                if (isActive == value) return;
                isActive = value;
                BackColor = value ? Theme.Current.Accent : Theme.Current.Border;
                Strip.Invalidate();
            }
        }

        public void RequestActivate() { if (ActivateRequested != null) ActivateRequested(this, EventArgs.Empty); }

        public TabItem ActiveTab { get { return ActiveIndex >= 0 && ActiveIndex < Tabs.Count ? Tabs[ActiveIndex] : null; } }
        public ExplorerHost ActiveHost { get { var t = ActiveTab; return t == null ? null : t.Host; } }
        public string ActivePath { get { var h = ActiveHost; return h == null ? null : h.CurrentPath; } }

        // ───── 탭 ─────
        public TabItem AddTab(string path, bool activate)
        {
            return InsertTab(ActiveIndex >= 0 ? ActiveIndex + 1 : Tabs.Count, path, activate);
        }

        public TabItem InsertTab(int at, string path, bool activate)
        {
            if (string.IsNullOrEmpty(path)) path = Shell.ThisPC;
            var host = new ExplorerHost(path) { Dock = DockStyle.Fill, Visible = false };
            var tab = new TabItem { Host = host };
            UpdateTabTitle(tab, path);
            Hook(tab);
            at = Math.Max(0, Math.Min(at, Tabs.Count));
            Tabs.Insert(at, tab);
            content.Controls.Add(host);
            if (ActiveIndex >= at && ActiveIndex >= 0 && !activate) ActiveIndex++;
            if (activate || ActiveIndex < 0) SelectTab(at);   // 백그라운드 탭은 처음 볼 때 연다
            Strip.Invalidate();
            if (TabsChanged != null) TabsChanged(this);
            return tab;
        }

        void Hook(TabItem tab)
        {
            tab.Host.Navigated += OnHostNavigated;
        }

        void Unhook(TabItem tab)
        {
            tab.Host.Navigated -= OnHostNavigated;
        }

        void OnHostNavigated(object sender, EventArgs e)
        {
            var host = (ExplorerHost)sender;
            var tab = Tabs.Find(t => t.Host == host);
            if (tab == null) return;
            // 다른 폴더로 이동하면 검색 결과 보기는 닫음 (테마·미리 보기 창 변경으로 같은 폴더를 다시 여는 것은 유지)
            if (tab.Search != null && !Shell.SamePath(host.CurrentPath, tab.SearchLocation)) CloseSearch(tab);
            if (tab.Search == null) UpdateTabTitle(tab, host.CurrentPath);   // 검색 결과를 보는 중이면 탭 이름은 '검색: …' 유지
            Strip.Invalidate();
            if (tab == ActiveTab)
            {
                UpdateNav();
                if (ActivePathChanged != null) ActivePathChanged(this, EventArgs.Empty);
            }
            if (TabsChanged != null) TabsChanged(this);
        }

        static void UpdateTabTitle(TabItem tab, string path)
        {
            tab.Title = Shell.ShortName(path);
            tab.Icon = Shell.SmallIcon(path);
        }

        public void RefreshTitles()
        {
            foreach (var t in Tabs) UpdateTabTitle(t, t.Host.CurrentPath);
            Strip.Invalidate();
        }

        public void SelectTab(int i, bool focusView = false)
        {
            if (i < 0 || i >= Tabs.Count) return;
            var old = ActiveTab;
            ActiveIndex = i;
            var cur = Tabs[i];
            cur.Host.Visible = true;
            cur.Host.BringToFront();
            if (old != null && old != cur) { old.Host.Visible = false; if (old.Search != null) old.Search.Visible = false; }
            if (cur.Search != null) { cur.Search.Visible = true; cur.Search.BringToFront(); }
            Strip.Invalidate();
            UpdateNav();
            if (old != cur && ActivePathChanged != null) ActivePathChanged(this, EventArgs.Empty);
            // 탭을 누르면 그 탭이 마지막으로 선택했던 항목이 있는 파일 영역에 포커스를 줌 (바로 붙여넣기 등 가능)
            if (focusView && IsHandleCreated) BeginInvoke(new Action(FocusView));
        }

        public void CloseTab(int i)
        {
            if (i < 0 || i >= Tabs.Count) return;
            if (Tabs.Count == 1)
            {
                // 마지막 탭은 닫지 않고 내 PC 로
                Tabs[0].Host.Navigate(Shell.ThisPC);
                return;
            }
            var tab = Tabs[i];
            CloseSearch(tab);
            bool wasActive = i == ActiveIndex;
            Tabs.RemoveAt(i);
            Unhook(tab);
            content.Controls.Remove(tab.Host);
            tab.Host.Dispose();
            if (wasActive) { ActiveIndex = -1; SelectTab(Math.Min(i, Tabs.Count - 1)); }
            else if (ActiveIndex > i) ActiveIndex--;
            Strip.Invalidate();
            if (TabsChanged != null) TabsChanged(this);
        }

        public void CloseOtherTabs(int keep)
        {
            var k = Tabs[keep];
            for (int i = Tabs.Count - 1; i >= 0; i--) if (Tabs[i] != k) CloseTab(i);
        }

        public void CloseTabsRight(int from)
        {
            for (int i = Tabs.Count - 1; i > from; i--) CloseTab(i);
        }

        public void MoveTab(int from, int to)
        {
            if (from == to || from < 0 || to < 0 || from >= Tabs.Count || to >= Tabs.Count) return;
            var active = ActiveTab;
            var t = Tabs[from];
            Tabs.RemoveAt(from);
            Tabs.Insert(to, t);
            ActiveIndex = Tabs.IndexOf(active);
            Strip.Invalidate();
            if (TabsChanged != null) TabsChanged(this);
        }

        /// <summary>탭을 다른 칸으로 옮김 (보기·기록 그대로)</summary>
        public void GiveTabTo(TabItem tab, PaneControl target, int at)
        {
            int i = Tabs.IndexOf(tab);
            if (i < 0) return;
            if (Tabs.Count == 1)
            {
                // 마지막 탭이면 옮기지 않고 복제
                target.InsertTab(at, tab.Host.CurrentPath, true);
                return;
            }
            CloseSearch(tab);
            bool wasActive = i == ActiveIndex;
            Tabs.RemoveAt(i);
            Unhook(tab);
            content.Controls.Remove(tab.Host);
            if (wasActive) { ActiveIndex = -1; SelectTab(Math.Min(i, Tabs.Count - 1)); }
            else if (ActiveIndex > i) ActiveIndex--;
            Strip.Invalidate();
            if (TabsChanged != null) TabsChanged(this);
            target.AcceptTab(tab, at);
        }

        void AcceptTab(TabItem tab, int at)
        {
            at = Math.Max(0, Math.Min(at, Tabs.Count));
            tab.Host.Visible = false;
            Hook(tab);
            Tabs.Insert(at, tab);
            content.Controls.Add(tab.Host);
            if (ActiveIndex >= at) ActiveIndex++;
            SelectTab(at);
            if (TabsChanged != null) TabsChanged(this);
        }

        public void NextTab(int dir)
        {
            if (Tabs.Count < 2) return;
            SelectTab((ActiveIndex + dir + Tabs.Count) % Tabs.Count);
            FocusView();
        }

        // ───── 이동 ─────
        public void Navigate(string path)
        {
            var h = ActiveHost;
            if (h == null) { AddTab(path, true); return; }
            CloseSearch(ActiveTab);
            if (!h.Navigate(path))
                MessageBox.Show(FindForm(), "'" + path + "'(으)로 이동할 수 없습니다.", "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void NavigateItem(IShellItem item)
        {
            var h = ActiveHost;
            if (h == null) return;
            CloseSearch(ActiveTab);
            h.NavigateItem(item);
        }

        // 검색 결과를 보고 있을 때 '뒤로'는 검색 전 폴더로 돌아오는 것 (기록은 건드리지 않음)
        public void Back() { if (SearchActive) { CloseSearch(ActiveTab); return; } var h = ActiveHost; if (h != null) h.Back(); }
        public void Forward() { CloseSearch(ActiveTab); var h = ActiveHost; if (h != null) h.Forward(); }
        public void Up() { CloseSearch(ActiveTab); var h = ActiveHost; if (h != null) h.Up(); }

        void HistoryMenu(Control under, bool back)
        {
            var h = ActiveHost;
            if (h == null) return;
            var m = new ContextMenuStrip();
            if (back)
                for (int i = h.BackList.Count - 1, n = 1; i >= 0 && n <= 20; i--, n++)
                {
                    int steps = n;
                    var p = h.BackList[i];
                    m.Items.Add(Shell.ShortName(p), Shell.SmallIcon(p), delegate { h.Back(steps); });
                }
            else
                for (int i = 0; i < h.ForwardList.Count && i < 20; i++)
                {
                    int steps = i + 1;
                    var p = h.ForwardList[i];
                    m.Items.Add(Shell.ShortName(p), Shell.SmallIcon(p), delegate { h.Forward(steps); });
                }
            if (m.Items.Count == 0) { m.Dispose(); return; }
            m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
            m.Show(under, new Point(0, under.Height));
        }

        void UpdateNav()
        {
            var h = ActiveHost;
            btnBack.Enabled = h != null && h.CanBack;
            btnFwd.Enabled = h != null && h.CanForward;
            btnUp.Enabled = h != null && h.CurrentPath != null && !Shell.SamePath(h.CurrentPath, Shell.ThisPC);
            Crumb.PathValue = h == null ? null : h.CurrentPath;
            var t = ActiveTab;
            search.Text = t != null && t.SearchQuery != null ? t.SearchQuery : "";
        }

        public void FocusView()
        {
            var t = ActiveTab;
            if (t != null && t.Search != null) { t.Search.FocusList(); return; }
            var h = ActiveHost; if (h != null) h.FocusView();
        }
        public void FocusAddress() { RequestActivate(); Crumb.BeginEdit(); }
        public void FocusSearch() { RequestActivate(); search.Focus(); search.SelectAll(); }

        // ───── 검색 (몽탐색기 안에서 직접 찾아 결과 목록을 보여 줌) ─────
        public bool SearchActive { get { var t = ActiveTab; return t != null && t.Search != null; } }

        void SearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                var q = search.Text.Trim();
                var tab = ActiveTab;
                if (tab == null) return;
                if (q.Length == 0) { CloseSearch(tab); FocusView(); return; }
                RunSearch(q);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                search.Text = "";
                if (SearchActive) CloseSearch(ActiveTab);
                FocusView();
            }
        }

        /// <summary>활성 탭의 현재 폴더(내 PC 면 모든 드라이브)에서 q 를 찾아 결과 보기를 띄움. 검색 결과 위에서 다시 검색하면 원래 폴더 기준</summary>
        public void RunSearch(string q)
        {
            var tab = ActiveTab;
            if (tab == null) return;
            {
                string loc = tab.Host.CurrentPath;
                string[] roots = SearchRoots(loc);
                if (roots == null)
                {
                    MessageBox.Show(FindForm(), "이 위치에서는 검색할 수 없습니다.\n폴더나 '내 PC' 에서 검색해 주세요.", "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (tab.Search == null)
                {
                    var sv = new SearchView { Dock = DockStyle.Fill };
                    sv.OpenFolderRequested += p => { var t = Tabs.Find(x => x.Search == sv); if (t != null) { CloseSearch(t); Navigate(p); } };
                    sv.ShowInFolderRequested += (dir, name) => { var t = Tabs.Find(x => x.Search == sv); if (t != null) { CloseSearch(t); t.Host.NavigateAndSelect(dir, name); } };
                    sv.NewTabRequested += dir => AddTab(dir, false);
                    sv.CloseRequested += () => { var t = Tabs.Find(x => x.Search == sv); if (t != null) { CloseSearch(t); FocusView(); } };
                    content.Controls.Add(sv);
                    tab.Search = sv;
                }
                tab.SearchQuery = q;
                tab.SearchLocation = loc;
                search.Text = q;
                tab.Title = "검색: " + q;
                Strip.Invalidate();
                tab.Search.Visible = true;
                tab.Search.BringToFront();
                tab.Search.Start(q, roots, Shell.SamePath(loc, Shell.ThisPC) ? "내 PC" : loc);
                tab.Search.FocusList();
            }
        }

        public SearchView DebugSearchView { get { var t = ActiveTab; return t == null ? null : t.Search; } }

        /// <summary>검색할 폴더 목록: 폴더면 그 폴더, 내 PC 면 모든 드라이브, 그 밖의 가상 위치는 null</summary>
        static string[] SearchRoots(string loc)
        {
            if (Shell.IsFileSystemPath(loc)) return new[] { loc };
            if (Shell.SamePath(loc, Shell.ThisPC))
            {
                var l = new List<string>();
                foreach (var d in System.IO.DriveInfo.GetDrives())
                    try { if (d.IsReady && (d.DriveType == System.IO.DriveType.Fixed || d.DriveType == System.IO.DriveType.Removable)) l.Add(d.RootDirectory.FullName); } catch { }
                return l.Count > 0 ? l.ToArray() : null;
            }
            return null;
        }

        /// <summary>탭의 검색 결과 보기를 닫고 원래 탐색기 보기로 돌아옴</summary>
        void CloseSearch(TabItem tab)
        {
            if (tab == null || tab.Search == null) return;
            var sv = tab.Search;
            tab.Search = null;
            tab.SearchQuery = null;
            tab.SearchLocation = null;
            content.Controls.Remove(sv);
            sv.Dispose();
            UpdateTabTitle(tab, tab.Host.CurrentPath);
            Strip.Invalidate();
            if (tab == ActiveTab) search.Text = "";
        }

        public void SetPreviewChecked(bool on) { btnPreview.Checked = on; btnPreview.Invalidate(); }

        public bool ContainsWindow(IntPtr hwnd)
        {
            return IsHandleCreated && (hwnd == Handle || Native.IsChild(Handle, hwnd));
        }

        public bool IsInView(IntPtr hwnd)
        {
            var h = ActiveHost;
            return h != null && h.IsHandleCreated && Native.IsChild(h.Handle, hwnd);
        }

        // ───── 저장·복원 ─────
        public PaneState GetState()
        {
            var st = new PaneState { Active = ActiveIndex };
            foreach (var t in Tabs)
                st.Tabs.Add(new TabState
                {
                    Path = t.Host.CurrentPath,
                    Back = new List<string>(t.Host.BackList.Count > 30 ? t.Host.BackList.GetRange(t.Host.BackList.Count - 30, 30) : t.Host.BackList),
                    Forward = new List<string>(t.Host.ForwardList.Count > 30 ? t.Host.ForwardList.GetRange(0, 30) : t.Host.ForwardList)
                });
            return st;
        }

        public void LoadState(PaneState st)
        {
            if (st == null || st.Tabs == null || st.Tabs.Count == 0) { AddTab(Shell.ThisPC, true); return; }
            foreach (var ts in st.Tabs)
            {
                var tab = InsertTab(Tabs.Count, ts.Path, false);
                if (ts.Back != null) tab.Host.BackList.AddRange(ts.Back);
                if (ts.Forward != null) tab.Host.ForwardList.AddRange(ts.Forward);
            }
            SelectTab(Math.Max(0, Math.Min(st.Active, Tabs.Count - 1)));
        }
    }
}
