// 메인 창: 메뉴·칸 배치, 왼쪽(폴더 트리 / 즐겨찾기), 오른쪽 칸(1, 1×2, 2×1, 2×2)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace NewExplorer
{
    public partial class MainForm : Form, IMessageFilter
    {
        readonly PaneControl[] panes = new PaneControl[4];
        PaneControl active;
        readonly FolderTree tree;
        readonly FavoritesList favs;
        readonly SplitContainer mainSplit, leftSplit;
        readonly Panel grid, parking;
        SplitContainer splitA, splitB, splitC;   // 1x2/2x1/2x2 의 나누기
        readonly MenuStrip menu;
        readonly Dictionary<string, ToolStripButton> layoutButtons = new Dictionary<string, ToolStripButton>();
        readonly Dictionary<string, ToolStripMenuItem> layoutMenus = new Dictionary<string, ToolStripMenuItem>();
        ToolStripMenuItem miPreview, miDetails, miDrivesOnly;
        readonly List<Control> headers = new List<Control>();
        readonly List<ToolStripMenuItem> viewModeMenus = new List<ToolStripMenuItem>();
        readonly List<ToolStripMenuItem> themeMenus = new List<ToolStripMenuItem>();
        ToolStripMenuItem miSwap;
        ToolStripButton versionButton;
        UpdateInfo updateInfo;
        bool updateBusy;
        readonly Timer updateTimer = new Timer { Interval = 30 * 60 * 1000 };
        readonly Timer driveTimer = new Timer { Interval = 1500 };
        readonly bool noActivate;
        bool loaded;
        readonly Timer saveTimer = new Timer { Interval = 2000 };

        public MainForm(string[] args)
        {
            Text = "몽탐색기";
            Icon = AppIcon.Load();
            Font = UI.Font;
            KeyPreview = false;
            var s = Settings.Current;

            // ── 메뉴 ──
            menu = new MenuStrip { Dock = DockStyle.Top, Padding = new Padding(4, 2, 4, 2) };
            BuildMenu();

            // ── 왼쪽 ──
            tree = new FolderTree { Dock = DockStyle.Fill };
            tree.FolderPicked += OnTreePicked;
            // 트리에서 붙여넣으면 폴더 안으로 들어간 것이므로, 붙여넣은 항목을 선택하고 포커스도 폴더 안(보기)으로
            tree.PasteStarting += d => { if (Shell.SamePath(d, Active.ActivePath) && Active.ActiveHost != null) Active.ActiveHost.SelectPastedSoon(); };
            tree.PasteFinished += d => { if (Shell.SamePath(d, Active.ActivePath)) Active.FocusView(); };
            favs = new FavoritesList { Dock = DockStyle.Fill };
            favs.Picked += (p, newTab) => { if (newTab) Active.AddTab(p, false); else Active.Navigate(p); };
            favs.Changed += Settings.Save;
            Activated += delegate { favs.RefreshPinned(); };   // 윈도우 탐색기에서 고정한 폴더가 바로 보이게
            favs.CurrentFolder = () => Active.ActivePath;

            leftSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 5 };
            leftSplit.Panel1.Controls.Add(tree);
            leftSplit.Panel1.Controls.Add(Header("폴더", null));
            leftSplit.Panel2.Controls.Add(favs);
            leftSplit.Panel2.Controls.Add(Header("즐겨찾기", () => favs.AddFolder(Active.ActivePath)));

            // ── 오른쪽 칸 ──
            grid = new Panel { Dock = DockStyle.Fill };
            parking = new Panel { Visible = false };
            for (int i = 0; i < 4; i++)
            {
                var p = new PaneControl(i) { Dock = DockStyle.Fill };
                p.ActivateRequested += (o, e) => SetActive((PaneControl)o);
                p.ActivePathChanged += OnPanePathChanged;
                p.TabMenuRequested += ShowTabMenu;
                p.TabsChanged += pc => { if (pc == active) UpdateTitle(); RequestSave(); };
                p.PreviewToggleRequested += TogglePreview;
                p.SwapRequested += (from, to) => SwapPanes(from, to);
                panes[i] = p;
                parking.Controls.Add(p);
            }

            mainSplit = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 5 };
            mainSplit.Panel1.Controls.Add(leftSplit);
            mainSplit.Panel2.Controls.Add(grid);

            Controls.Add(mainSplit);
            Controls.Add(menu);
            Controls.Add(parking);
            MainMenuStrip = menu;

            // ── 창 위치 (시험용 --pos x,y[,w,h] --noactivate) ──
            StartPosition = FormStartPosition.Manual;
            var b = new Rectangle(s.WinX, s.WinY, Math.Max(800, s.WinW), Math.Max(500, s.WinH));
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--pos" && i + 1 < args.Length)
                {
                    var v = args[i + 1].Split(',');
                    b.X = int.Parse(v[0]); b.Y = int.Parse(v[1]);
                    if (v.Length >= 4) { b.Width = int.Parse(v[2]); b.Height = int.Parse(v[3]); }
                }
                if (args[i] == "--noactivate") noActivate = true;
                if (args[i] == "--cuttest" && i + 1 < args.Length) { cutTest = true; selfTestLog = args[i + 1]; Settings.NoSave = true; File.WriteAllText(selfTestLog, ""); }
                if (args[i] == "--viewtest" && i + 1 < args.Length) { viewTest = true; selfTestLog = args[i + 1]; Settings.NoSave = true; File.WriteAllText(selfTestLog, ""); }
                if (args[i] == "--selftest" && i + 1 < args.Length) { selfTestLog = args[i + 1]; Settings.NoSave = true; File.WriteAllText(selfTestLog, ""); FolderTree.DebugLog = x => Log("   [트리] " + x); }
            }
            if (b.X == -1 && b.Y == -1 || !OnScreen(b)) { var wa = Screen.PrimaryScreen.WorkingArea; b.X = wa.Left + (wa.Width - b.Width) / 2; b.Y = wa.Top + (wa.Height - b.Height) / 2; }
            Bounds = b;
            if (s.WinMax && !noActivate) WindowState = FormWindowState.Maximized;

            // ── 칸 내용 복원 ──
            for (int i = 0; i < 4; i++) panes[i].LoadState(i < s.Panes.Count ? s.Panes[i] : null);
            foreach (var a in args)
                if (!a.StartsWith("--") && (Directory.Exists(a) || a.StartsWith("::") || a.StartsWith("shell:")))
                {
                    int ai = Math.Max(0, Math.Min(3, s.ActivePane));
                    panes[ai].AddTab(a, true);
                }

            saveTimer.Tick += delegate { saveTimer.Stop(); Settings.Save(); };
            Settings.BeforeSave = CaptureState;
            driveTimer.Tick += delegate { driveTimer.Stop(); tree.LoadRoots(); };
            updateTimer.Tick += delegate { CheckUpdate(false); };
            Theme.Changed += OnThemeChanged;
            Settings.SaveFailedFirst += () => BeginInvoke(new Action(() => MessageBox.Show(this,
                "exe 가 있는 폴더에 설정을 저장할 수 없습니다.\n(" + Settings.FilePath + ")\n\n쓰기가 가능한 폴더(예: 문서, 바탕 화면, D:\\도구)로 exe 를 옮겨 주세요.",
                "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
            ApplyThemeColors();
            Application.AddMessageFilter(this);
        }

        static bool OnScreen(Rectangle r)
        {
            foreach (var sc in Screen.AllScreens) if (sc.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, 100, 20))) return true;
            return false;
        }

        protected override bool ShowWithoutActivation { get { return noActivate; } }

        PaneControl Active { get { return active ?? panes[0]; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var s = Settings.Current;
            mainSplit.SplitterDistance = Math.Max(120, Math.Min(600, s.LeftWidth));
            leftSplit.SplitterDistance = (int)(leftSplit.Height * Math.Max(0.15, Math.Min(0.9, s.TreeRatio)));
            ApplyLayout(s.Layout);
            SetActive(panes[Math.Max(0, Math.Min(VisibleCount() - 1, s.ActivePane))]);
            UpdatePreviewChecks();
            loaded = true;
            if (selfTestLog != null) StartSelfTest();
            else
            {
                // 시작 10초 뒤 한 번, 그 뒤 30분마다 업데이트 확인
                var first = new Timer { Interval = 10000 };
                first.Tick += delegate { first.Stop(); first.Dispose(); MongUpdate.UpdateUi.CheckAtStartup(this, "mongexplorer", "몽탐색기", Program.Version, null); updateTimer.Start(); };
                first.Start();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!noActivate) Active.FocusView();
            BeginInvoke(new Action(() => tree.Reveal()));
        }

        Control Header(string title, Action add)
        {
            var h = new Panel { Dock = DockStyle.Top, Height = 28 };
            var lbl = new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), Font = new Font(UI.Font, FontStyle.Bold) };
            h.Controls.Add(lbl);
            if (add != null)
            {
                var btn = new Label { Text = "+", Dock = DockStyle.Right, Width = 28, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12f), Cursor = Cursors.Hand };
                new ToolTip().SetToolTip(btn, "현재 폴더를 즐겨찾기에 추가 (Ctrl+D)");
                btn.MouseEnter += delegate { btn.BackColor = Theme.Current.Hover; };
                btn.MouseLeave += delegate { btn.BackColor = Color.Transparent; };
                btn.Click += delegate { add(); };
                h.Controls.Add(btn);
            }
            h.Paint += (o, e) => { using (var pen = new Pen(Theme.Current.Border)) e.Graphics.DrawLine(pen, 0, h.Height - 1, h.Width, h.Height - 1); };
            headers.Add(h);
            return h;
        }

        // ───────── 메뉴 ─────────
        void BuildMenu()
        {
            var file = new ToolStripMenuItem("파일(&F)");
            file.DropDownItems.Add(Item("새 탭", "Ctrl+T", () => NewTab()));
            file.DropDownItems.Add(Item("탭 닫기", "Ctrl+W", () => Active.CloseTab(Active.ActiveIndex)));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("윈도우 탐색기로 열기", null, () => OpenInExplorer(Active.ActivePath)));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("끝내기", "Alt+F4", Close));

            var view = new ToolStripMenuItem("보기(&V)");
            foreach (var vm in ViewModes)
            {
                var v = vm;
                var mi = Item(v.Name, "Ctrl+Shift+" + v.Key, () => SetViewMode(Active, v));
                mi.Tag = v;
                viewModeMenus.Add(mi);
                view.DropDownItems.Add(mi);
            }
            view.DropDownOpening += delegate { UpdateViewModeChecks(); UpdateSwapMenu(); };
            view.DropDownItems.Add(new ToolStripSeparator());
            var layout = new ToolStripMenuItem("칸 배치");
            foreach (var l in new[] { "1", "1x2", "2x1", "2x2" })
            {
                string lay = l;
                var mi = new ToolStripMenuItem(LayoutLabel(l), LayoutIcon(l, false), delegate { ApplyLayout(lay); });
                layoutMenus[l] = mi;
                layout.DropDownItems.Add(mi);
            }
            view.DropDownItems.Add(layout);
            miSwap = new ToolStripMenuItem("칸 위치 바꾸기 (활성 칸)");
            miSwap.DropDownItems.Add(new ToolStripMenuItem("-"));
            view.DropDownItems.Add(miSwap);
            view.DropDownItems.Add(new ToolStripSeparator());
            miPreview = Item("미리 보기 창", "Alt+P", TogglePreview);
            miDetails = Item("세부 정보 창", "Alt+Shift+P", ToggleDetails);
            view.DropDownItems.Add(miPreview);
            view.DropDownItems.Add(miDetails);
            view.DropDownItems.Add(new ToolStripSeparator());
            miDrivesOnly = Item("폴더 트리에 드라이브만 표시", null, () =>
            {
                Settings.Current.TreeDrivesOnly = !Settings.Current.TreeDrivesOnly;
                miDrivesOnly.Checked = Settings.Current.TreeDrivesOnly;
                tree.LoadRoots();
                Settings.Save();
            });
            miDrivesOnly.Checked = Settings.Current.TreeDrivesOnly;
            view.DropDownItems.Add(miDrivesOnly);
            var chars = new ToolStripMenuItem("탭·즐겨찾기 이름: 시작 폴더 글자 수");
            for (int n = 1; n <= 4; n++)
            {
                int nn = n;
                var mi = new ToolStripMenuItem(n + "글자 (예: " + ShortExample(n) + ")") { Checked = Settings.Current.ShortChars == n };
                mi.Click += delegate
                {
                    Settings.Current.ShortChars = nn;
                    foreach (ToolStripMenuItem x in chars.DropDownItems) x.Checked = x == mi;
                    foreach (var p in panes) p.RefreshTitles();
                    favs.Invalidate();
                    UpdateTitle();
                    Settings.Save();
                };
                chars.DropDownItems.Add(mi);
            }
            view.DropDownItems.Add(chars);
            var theme = new ToolStripMenuItem("테마");
            foreach (var kv in new[] { new[] { "system", "윈도우 설정 따름" }, new[] { "light", "밝게" }, new[] { "dark", "어둡게 (다크 모드)" } })
            {
                string mode = kv[0];
                var mi = Item(kv[1], null, () => SetTheme(mode));
                mi.Tag = mode;
                mi.Checked = Settings.Current.Theme == mode;
                themeMenus.Add(mi);
                theme.DropDownItems.Add(mi);
            }
            view.DropDownItems.Add(theme);
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Item("모든 폴더 보기 설정 해제", null, ClearTemplate));
            view.DropDownItems.Add(Item("폴더 옵션...", null, () => Run("rundll32.exe", "shell32.dll,Options_RunDLL 0")));

            var fav = new ToolStripMenuItem("즐겨찾기(&B)");
            fav.DropDownItems.Add(Item("현재 폴더 추가", "Ctrl+D", () => favs.AddFolder(Active.ActivePath)));

            var help = new ToolStripMenuItem("도움말(&H)");
            help.DropDownItems.Add(Item("단축키", null, ShowShortcuts));
            help.DropDownItems.Add(Item("업데이트 확인", null, () => CheckUpdate(true)));
            var miShell = new ToolStripMenuItem("폴더 우클릭 메뉴에 \"몽탐색기로 열기\" 등록") { Checked = Settings.Current.ShellMenu && ShellRegistry.IsRegistered() };
            miShell.Click += delegate
            {
                Settings.Current.ShellMenu = !miShell.Checked;
                if (Settings.Current.ShellMenu) ShellRegistry.Register(); else ShellRegistry.Unregister();
                miShell.Checked = Settings.Current.ShellMenu;
                Settings.Save();
            };
            help.DropDownItems.Add(miShell);
            help.DropDownItems.Add(Item("몽탐색기 정보", null, () => MessageBox.Show(this, "몽탐색기 v" + Program.Version + "\n\n윈도우 탐색기 여러 개를 한 창에 칸으로 나누어 쓰는 앱", "몽탐색기")));

            menu.Items.Add(file);
            menu.Items.Add(view);
            menu.Items.Add(fav);
            menu.Items.Add(help);

            // 오른쪽: 칸 배치 그림 버튼
            foreach (var l in new[] { "2x2", "2x1", "1x2", "1" })
            {
                string lay = l;
                var btn = new ToolStripButton(LayoutIcon(l, false))
                {
                    Alignment = ToolStripItemAlignment.Right,
                    ToolTipText = "칸 배치: " + LayoutLabel(l),
                    ImageScaling = ToolStripItemImageScaling.None,
                    DisplayStyle = ToolStripItemDisplayStyle.Image,
                    Padding = new Padding(2)
                };
                btn.Click += delegate { ApplyLayout(lay); };
                layoutButtons[l] = btn;
                menu.Items.Add(btn);
            }
            menu.Items.Add(new ToolStripLabel("칸 배치") { Alignment = ToolStripItemAlignment.Right });
            menu.Items.Add(new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right });
            // 버전 칸: 새 버전이면 초록색 → 누르면 업데이트
            versionButton = new ToolStripButton("v" + Program.Version)
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "몽탐색기 v" + Program.Version + " — 눌러서 업데이트 확인",
                Margin = new Padding(4, 1, 4, 2)
            };
            versionButton.Click += delegate { CheckUpdate(true); };
            menu.Items.Add(versionButton);
        }

        static string ShortExample(int n)
        {
            string first = "클로드워크";
            return @"J:\" + first.Substring(0, n) + @"..\몽탐색기";
        }

        static string LayoutLabel(string l)
        {
            switch (l)
            {
                case "1": return "1칸";
                case "1x2": return "1×2 (좌우)";
                case "2x1": return "2×1 (위아래)";
                default: return "2×2 (4칸)";
            }
        }

        /// <summary>칸 배치 그림 (그려서 만듦)</summary>
        static Bitmap LayoutIcon(string l, bool on)
        {
            var bmp = new Bitmap(26, 20);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None;
                var T = Theme.Current;
                var fill = on ? T.Accent : T.SubText;
                var back = on ? T.Pressed : T.Content;
                var r = new Rectangle(1, 2, 23, 16);
                using (var b = new SolidBrush(back)) g.FillRectangle(b, r);
                using (var pen = new Pen(fill, 1.6f))
                {
                    g.DrawRectangle(pen, r);
                    if (l == "1x2" || l == "2x2") g.DrawLine(pen, 12.5f, 2, 12.5f, 18);
                    if (l == "2x1" || l == "2x2") g.DrawLine(pen, 1, 10, 24, 10);
                }
            }
            return bmp;
        }

        static ToolStripMenuItem Item(string text, string keys, Action act)
        {
            var mi = new ToolStripMenuItem(text);
            if (keys != null) mi.ShortcutKeyDisplayString = keys;
            mi.Click += delegate { act(); };
            return mi;
        }

        static void Run(string exe, string args)
        {
            try { System.Diagnostics.Process.Start(exe, args); } catch { }
        }

        static void OpenInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Run("explorer.exe", "\"" + (path.StartsWith("::") ? "shell:" + path : path) + "\"");
        }

        void ShowShortcuts()
        {
            MessageBox.Show(this,
                "Ctrl+T\t새 탭\nCtrl+W\t탭 닫기\nCtrl+Tab\t다음 탭 (Shift: 이전)\nAlt+1~4\t칸 고르기\n" +
                "Alt+←/→\t뒤로/앞으로 (Backspace: 뒤로)\nAlt+↑\t위 폴더\nF5\t새로 고침\n" +
                "Ctrl+L, Alt+D, F4\t주소 입력\nCtrl+E, Ctrl+F\t검색\nAlt+P\t미리 보기 창\nAlt+Shift+P\t세부 정보 창\n" +
                "Ctrl+Shift+1~8\t아주 큰 아이콘·큰 아이콘·보통 아이콘·작은 아이콘·목록·자세히·타일·내용\n" +
                "Ctrl+D\t즐겨찾기에 추가\nCtrl+Enter\t선택한 폴더를 새 탭으로\n가운데 클릭\t폴더를 새 탭으로 (트리·경로·즐겨찾기도)\n" +
                "마우스 뒤로/앞으로 버튼\t뒤로/앞으로\n\n탭을 끌어 다른 칸으로 옮기거나 폴더를 탭 줄에 놓아 새 탭으로 열 수 있습니다.\n" +
                "탭 줄의 빈 곳을 잡고 다른 칸으로 끌면 두 칸의 위치가 바뀝니다.\n" +
                "탭 우클릭 → '모든 폴더에 적용': 그 탭의 보기(방식·아이콘 크기·정렬·열·열 너비·그룹)를 모든 폴더에 적용",
                "단축키");
        }

        // ───────── 칸 배치 ─────────
        int VisibleCount()
        {
            switch (Settings.Current.Layout) { case "1": return 1; case "2x2": return 4; default: return 2; }
        }

        SplitContainer NewSplit(Orientation o, Control a, Control b)
        {
            var sc = new SplitContainer { Dock = DockStyle.Fill, Orientation = o, SplitterWidth = 5, BackColor = Theme.Current.Window };
            sc.Panel1.Controls.Add(a);
            sc.Panel2.Controls.Add(b);
            sc.SplitterMoved += delegate { SaveSplits(); RequestSave(); };
            return sc;
        }

        void ApplyLayout(string l)
        {
            if (l != "1" && l != "1x2" && l != "2x1" && l != "2x2") l = "1x2";
            SaveSplits();
            Settings.Current.Layout = l;
            grid.SuspendLayout();
            foreach (var p in panes) { p.Parent = parking; }
            foreach (var sc in new[] { splitA, splitB, splitC }) if (sc != null) sc.Dispose();
            splitA = splitB = splitC = null;
            grid.Controls.Clear();
            var s = Settings.Current;
            if (l == "1") grid.Controls.Add(panes[0]);
            else if (l == "1x2") { splitA = NewSplit(Orientation.Vertical, panes[0], panes[1]); grid.Controls.Add(splitA); }
            else if (l == "2x1") { splitA = NewSplit(Orientation.Horizontal, panes[0], panes[1]); grid.Controls.Add(splitA); }
            else
            {
                splitB = NewSplit(Orientation.Vertical, panes[0], panes[1]);
                splitC = NewSplit(Orientation.Vertical, panes[2], panes[3]);
                splitA = NewSplit(Orientation.Horizontal, splitB, splitC);
                grid.Controls.Add(splitA);
            }
            grid.ResumeLayout(true);
            SetRatio(splitA, l == "2x1" || l == "2x2" ? s.SplitH : s.SplitV);
            SetRatio(splitB, s.SplitV);
            SetRatio(splitC, s.SplitV2);
            foreach (var p in panes) p.Visible = true;
            foreach (var kv in layoutButtons) kv.Value.Image = LayoutIcon(kv.Key, kv.Key == l);
            foreach (var kv in layoutMenus) kv.Value.Checked = kv.Key == l;
            if (active != null && active.Index >= VisibleCount()) SetActive(panes[0]);
            if (loaded) Settings.Save();
        }

        static void SetRatio(SplitContainer sc, double r)
        {
            if (sc == null) return;
            int size = sc.Orientation == Orientation.Vertical ? sc.Width : sc.Height;
            if (size <= 50) return;
            r = Math.Max(0.1, Math.Min(0.9, r));
            try { sc.SplitterDistance = (int)(size * r); } catch { }
        }

        static double Ratio(SplitContainer sc)
        {
            int size = sc.Orientation == Orientation.Vertical ? sc.Width : sc.Height;
            return size <= 50 ? 0.5 : (double)sc.SplitterDistance / size;
        }

        void SaveSplits()
        {
            var s = Settings.Current;
            if (splitA == null) return;
            if (s.Layout == "1x2") s.SplitV = Ratio(splitA);
            else if (s.Layout == "2x1") s.SplitH = Ratio(splitA);
            else if (s.Layout == "2x2") { s.SplitH = Ratio(splitA); s.SplitV = Ratio(splitB); s.SplitV2 = Ratio(splitC); }
        }

        // ───────── 활성 칸 ─────────
        void SetActive(PaneControl p)
        {
            if (p == null) return;
            if (active != p)
            {
                if (active != null) active.IsActivePane = false;
                active = p;
                p.IsActivePane = true;
                Settings.Current.ActivePane = p.Index;
            }
            tree.SelectPath(p.ActivePath);
            UpdateTitle();
        }

        void OnPanePathChanged(object sender, EventArgs e)
        {
            if (sender == active)
            {
                tree.SelectPath(active.ActivePath);
                UpdateTitle();
            }
        }

        void OnTreePicked(string path, bool newTab)
        {
            if (newTab) { Active.AddTab(path, false); return; }
            if (Shell.SamePath(path, Active.ActivePath) && !Active.SearchActive) return;
            Active.Navigate(path);
        }

        void UpdateTitle()
        {
            var p = Active.ActivePath;
            Text = p == null ? "몽탐색기" : Shell.ShortName(p) + " - 몽탐색기";
        }

        void NewTab()
        {
            Active.AddTab(Active.ActivePath, true);
            Active.FocusView();
        }

        // ───────── 미리 보기 / 세부 정보 창 ─────────
        void TogglePreview()
        {
            Settings.Current.PreviewPane = !Settings.Current.PreviewPane;
            if (Settings.Current.PreviewPane) Settings.Current.DetailsPane = false;
            ReloadAllViews();
        }

        void ToggleDetails()
        {
            Settings.Current.DetailsPane = !Settings.Current.DetailsPane;
            if (Settings.Current.DetailsPane) Settings.Current.PreviewPane = false;
            ReloadAllViews();
        }

        void ReloadAllViews()
        {
            UpdatePreviewChecks();
            foreach (var p in panes)
                foreach (var t in p.Tabs)
                    if (t.Host.IsHandleCreated) t.Host.Recreate();
            Settings.Save();
        }

        void UpdatePreviewChecks()
        {
            miPreview.Checked = Settings.Current.PreviewPane;
            miDetails.Checked = Settings.Current.DetailsPane;
            foreach (var p in panes) p.SetPreviewChecked(Settings.Current.PreviewPane);
        }

        // ───────── 탭 우클릭 메뉴 ─────────
        void ShowTabMenu(PaneControl pane, int index)
        {
            SetActive(pane);
            var m = new ContextMenuStrip();
            var tab = index >= 0 && index < pane.Tabs.Count ? pane.Tabs[index] : null;
            m.Items.Add(Item("새 탭", "Ctrl+T", () => { pane.AddTab(pane.ActivePath, true); }));
            if (tab == null)
            {
                var swapItem = new ToolStripMenuItem("이 칸 위치 바꾸기");
                FillSwapMenu(swapItem, pane);
                m.Items.Add(swapItem);
            }
            if (tab != null)
            {
                string path = tab.Host.CurrentPath;
                m.Items.Add(Item("탭 복제", null, () => pane.InsertTab(index + 1, path, true)));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(Item("탭 닫기", "Ctrl+W", () => pane.CloseTab(pane.Tabs.IndexOf(tab))));
                var other = Item("다른 탭 모두 닫기", null, () => pane.CloseOtherTabs(pane.Tabs.IndexOf(tab)));
                other.Enabled = pane.Tabs.Count > 1;
                m.Items.Add(other);
                var right = Item("오른쪽 탭 닫기", null, () => pane.CloseTabsRight(pane.Tabs.IndexOf(tab)));
                right.Enabled = index < pane.Tabs.Count - 1;
                m.Items.Add(right);
                m.Items.Add(new ToolStripSeparator());
                var move = new ToolStripMenuItem("다른 칸으로 옮기기");
                for (int i = 0; i < VisibleCount(); i++)
                {
                    if (panes[i] == pane) continue;
                    var target = panes[i];
                    move.DropDownItems.Add(Item((i + 1) + "번 칸", null, () => pane.GiveTabTo(tab, target, target.Tabs.Count)));
                }
                move.Enabled = move.DropDownItems.Count > 0;
                m.Items.Add(move);
                var swapItem = new ToolStripMenuItem("이 칸 위치 바꾸기");
                FillSwapMenu(swapItem, pane);
                m.Items.Add(swapItem);
                var viewItem = new ToolStripMenuItem("보기");
                int cur = CurrentViewModeIndex(tab.Host);
                for (int vi = 0; vi < ViewModes.Length; vi++)
                {
                    var v = ViewModes[vi];
                    var h = tab.Host;
                    var mi = Item(v.Name, "Ctrl+Shift+" + v.Key, () => h.SetViewMode(v.Mode, v.Size));
                    mi.Checked = vi == cur;
                    viewItem.DropDownItems.Add(mi);
                }
                m.Items.Add(viewItem);
                m.Items.Add(Item("즐겨찾기에 추가", "Ctrl+D", () => favs.AddFolder(path)));
                m.Items.Add(Item("경로 복사", null, () => { try { Clipboard.SetText(path); } catch { } }));
                m.Items.Add(Item("윈도우 탐색기로 열기", null, () => OpenInExplorer(path)));
                m.Items.Add(new ToolStripSeparator());
                var apply = Item("모든 폴더에 적용", null, () => ApplyToAll(tab.Host));
                apply.Enabled = Shell.IsFileSystemPath(path);
                apply.Font = new Font(apply.Font, FontStyle.Bold);
                apply.ToolTipText = "이 탭의 보기 방식·아이콘 크기·정렬·열·열 너비·그룹을 모든 칸, 모든 탭, 앞으로 여는 모든 폴더에 적용";
                m.Items.Add(apply);
                var clear = Item("모든 폴더 보기 설정 해제", null, ClearTemplate);
                clear.Enabled = Settings.Current.Template != null;
                m.Items.Add(clear);
            }
            m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
            m.Show(Cursor.Position);
        }

        void ApplyToAll(ExplorerHost from)
        {
            var t = from.CaptureTemplate();
            if (t == null) { MessageBox.Show(this, "이 탭의 보기 설정을 읽지 못했습니다.", "몽탐색기"); return; }
            Settings.Current.Template = t;
            foreach (var p in panes)
                foreach (var tab in p.Tabs)
                    if (tab.Host != from && tab.Host.IsHandleCreated && Shell.IsFileSystemPath(tab.Host.CurrentPath)) tab.Host.ApplyTemplate(t);
            Settings.Save();
        }

        void ClearTemplate()
        {
            Settings.Current.Template = null;
            Settings.Save();
            MessageBox.Show(this, "'모든 폴더에 적용' 설정을 해제했습니다.\n이제 폴더마다 보기 설정을 따로 기억합니다.", "몽탐색기");
        }

        // ───────── 마우스·키보드 ─────────
        PaneControl PaneOf(IntPtr hwnd)
        {
            for (int i = 0; i < VisibleCount(); i++) if (panes[i].ContainsWindow(hwnd)) return panes[i];
            return null;
        }

        public bool PreFilterMessage(ref Message m)
        {
            int msg = m.Msg;
            // 왼쪽 폴더 트리: 누른 폴더 → 활성 칸의 활성 탭 이동, 가운데 클릭 → 새 탭
            if ((msg == Native.WM_LBUTTONDOWN || msg == Native.WM_MBUTTONDOWN || msg == Native.WM_MBUTTONUP) && tree.IsTreeWindow(m.HWnd))
            {
                if (msg == Native.WM_LBUTTONDOWN) { tree.OnMouseDownInTree(Cursor.Position); return false; }
                if (msg == Native.WM_MBUTTONUP) tree.OnMiddleClickInTree(Cursor.Position);
                return true;
            }
            if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN || msg == Native.WM_LBUTTONDBLCLK)
            {
                var p = PaneOf(m.HWnd);
                if (p != null && p != active) SetActive(p);
                return false;
            }
            if (msg == Native.WM_MBUTTONUP)
            {
                var p = PaneOf(m.HWnd);
                if (p != null && p.IsInView(m.HWnd)) { MiddleClickInView(p, m.HWnd, m.LParam); return true; }
                return false;
            }
            if (msg == Native.WM_XBUTTONDOWN || msg == Native.WM_XBUTTONUP)
            {
                var p = PaneOf(m.HWnd);
                if (p == null) return false;
                if (msg == Native.WM_XBUTTONUP)
                {
                    int btn = (int)(((long)m.WParam >> 16) & 0xFFFF);
                    SetActive(p);
                    if (btn == 1) p.Back(); else if (btn == 2) p.Forward();
                }
                return true;
            }
            if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                return HandleKey(ref m);
            return false;
        }

        void MiddleClickInView(PaneControl p, IntPtr hwnd, IntPtr lParam)
        {
            // 가운데 클릭한 자리의 항목을 선택해 보고, 폴더면 새 탭으로
            Native.SendMessage(hwnd, Native.WM_LBUTTONDOWN, (IntPtr)1, lParam);
            Native.SendMessage(hwnd, Native.WM_LBUTTONUP, IntPtr.Zero, lParam);
            var h = p.ActiveHost;
            if (h == null) return;
            var folder = h.SingleSelectedFolder();
            if (folder != null) p.AddTab(folder, false);
        }

        bool HandleKey(ref Message m)
        {
            var key = (Keys)(int)m.WParam & Keys.KeyCode;
            bool ctrl = Native.KeyDown(0x11), shift = Native.KeyDown(0x10), alt = Native.KeyDown(0x12);
            IntPtr focus = m.HWnd;
            bool inEdit = Native.ClassName(focus).IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0;
            var pane = PaneOf(focus) ?? Active;
            if (PaneOf(focus) != null && pane != active) SetActive(pane);
            var A = Active;

            // ESC: 어디서 잘랐든 잘라내기 취소 (이름 바꾸기·주소·검색 입력 중이면 그쪽 ESC 가 우선)
            if (key == Keys.Escape && !ctrl && !alt && !shift && !inEdit && (tree.IsTreeWindow(focus) || PaneOf(focus) != null)) tree.CancelCut();

            // 폴더 트리에 포커스가 있으면 복사·잘라내기·붙여넣기·삭제를 여러 폴더에 적용
            if (!alt && !inEdit && tree.IsTreeWindow(focus) && tree.HandleKey(key, ctrl, shift)) return true;

            if (ctrl && shift && !alt && key >= Keys.D1 && key <= Keys.D8)
            {
                SetViewMode(A, ViewModes[key - Keys.D1]);
                return true;
            }
            if (ctrl && !alt)
            {
                switch (key)
                {
                    case Keys.T: NewTab(); return true;
                    case Keys.W: case Keys.F4: A.CloseTab(A.ActiveIndex); return true;
                    case Keys.Tab: A.NextTab(shift ? -1 : 1); return true;
                    case Keys.L: A.FocusAddress(); return true;
                    case Keys.E: case Keys.F: if (!shift) { A.FocusSearch(); return true; } break;
                    case Keys.D: if (!shift) { favs.AddFolder(A.ActivePath); return true; } break;
                    case Keys.N:
                        // 새 폴더: 보기에 포커스가 없어도(트리·탭 등) 활성 칸의 현재 폴더에 만든다
                        if (shift && !inEdit && A.ActiveHost != null) { A.FocusView(); A.ActiveHost.CreateNewFolder(); return true; }
                        break;
                    case Keys.V:
                        // 붙여넣기는 탐색기 보기가 하고, 끝나면 붙여넣은 항목을 선택
                        if (!shift && A.IsInView(focus) && A.ActiveHost != null) A.ActiveHost.SelectPastedSoon();
                        break;
                    case Keys.Enter:
                        if (A.IsInView(focus))
                        {
                            var f = A.ActiveHost.SingleSelectedFolder();
                            if (f != null) { A.AddTab(f, false); return true; }
                        }
                        break;
                }
            }
            if (alt && !ctrl)
            {
                switch (key)
                {
                    case Keys.Left: A.Back(); return true;
                    case Keys.Right: A.Forward(); return true;
                    case Keys.Up: A.Up(); return true;
                    case Keys.D: A.FocusAddress(); return true;
                    case Keys.P: if (shift) ToggleDetails(); else TogglePreview(); return true;
                    case Keys.D1: case Keys.D2: case Keys.D3: case Keys.D4:
                        {
                            int i = key - Keys.D1;
                            if (i < VisibleCount()) { SetActive(panes[i]); panes[i].FocusView(); }
                            return true;
                        }
                }
            }
            if (!ctrl && !alt)
            {
                switch (key)
                {
                    case Keys.F4: A.FocusAddress(); return true;
                    case Keys.F3: A.FocusSearch(); return true;
                    case Keys.F5: if (!inEdit) { A.ActiveHost.RefreshView(); return true; } break;
                    case Keys.Back: if (!inEdit && A.IsInView(focus)) { A.Back(); return true; } break;
                }
            }
            // 나머지는 탐색기 보기에 넘김 (F2 이름 바꾸기, Delete, Ctrl+C/V/X/Z/A, Ctrl+Shift+N ...)
            if (A.IsInView(focus) && A.ActiveHost != null)
            {
                var msg = new MSG { hwnd = m.HWnd, message = m.Msg, wParam = m.WParam, lParam = m.LParam };
                if (A.ActiveHost.TranslateAccel(ref msg)) return true;
            }
            return false;
        }

        // ───────── 드라이브 연결·해제 → 트리 다시 ─────────
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x001A /* WM_SETTINGCHANGE */ && Settings.Current.Theme == "system" && m.LParam != IntPtr.Zero)
            {
                string what = null;
                try { what = System.Runtime.InteropServices.Marshal.PtrToStringUni(m.LParam); } catch { }
                if (what == "ImmersiveColorSet") BeginInvoke(new Action(() => Theme.Set("system")));
            }
            if (m.Msg == Native.WM_DEVICECHANGE)
            {
                int ev = (int)m.WParam;
                if (ev == 0x8000 || ev == 0x8004) { driveTimer.Stop(); driveTimer.Start(); }
            }
        }

        // ───────── 저장 ─────────
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            Application.RemoveMessageFilter(this);
            if (selfTestLog != null) return;     // 시험 실행은 설정을 저장하지 않음
            saveTimer.Stop();
            Settings.Save();                     // 저장 직전에 현재 상태를 반영 (CaptureState)
            Application.RemoveMessageFilter(this);
        }

        // ───────── 저장 시점 ─────────
        // 탭 구성·폴더 이동·창 위치/크기·분할 비율이 바뀌면 잠시 뒤 한 번에 저장 (비정상 종료에도 덜 잃음)
        void RequestSave()
        {
            if (!loaded || selfTestLog != null || Settings.NoSave) return;
            saveTimer.Stop();
            saveTimer.Start();
        }

        /// <summary>지금 화면 상태(창·분할·칸·탭)를 설정 객체에 반영</summary>
        void CaptureState()
        {
            if (!loaded) return;     // 아직 복원 전이면 읽어 둔 설정을 지우지 않음
            var s = Settings.Current;
            SaveSplits();
            s.WinMax = WindowState == FormWindowState.Maximized;
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (!noActivate) { s.WinX = b.X; s.WinY = b.Y; s.WinW = b.Width; s.WinH = b.Height; }
            s.LeftWidth = mainSplit.SplitterDistance;
            s.TreeRatio = leftSplit.Height > 0 ? (double)leftSplit.SplitterDistance / leftSplit.Height : 0.62;
            s.Panes.Clear();
            foreach (var p in panes) s.Panes.Add(p.GetState());
        }

        protected override void OnMove(EventArgs e) { base.OnMove(e); RequestSave(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); RequestSave(); }
    }
}
