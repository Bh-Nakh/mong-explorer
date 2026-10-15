// 칸 위쪽의 탭 줄 (직접 그림)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace NewExplorer
{
    public class TabItem
    {
        public ExplorerHost Host;
        public string Title = "";
        public Bitmap Icon;
        public SearchView Search;      // 검색 결과 보기 (검색 중일 때만, 탐색기 보기 위를 덮음)
        public string SearchQuery;
        public string SearchLocation;   // 검색을 시작한 폴더 (다른 폴더로 이동하면 결과 보기를 닫음)
    }

    /// <summary>다른 칸으로 탭을 끌어 옮길 때 쓰는 데이터</summary>
    public class TabDragData
    {
        public PaneControl Pane;
        public TabItem Tab;
    }

    /// <summary>칸 위치 바꾸기로 끄는 칸</summary>
    public class PaneDragData
    {
        public PaneControl Pane;
    }

    public class TabStrip : Control
    {
        readonly PaneControl pane;
        readonly List<Rectangle> tabRects = new List<Rectangle>();
        Rectangle plusRect;
        int hover = -1, hoverClose = -1, pressIndex = -1;
        bool hoverPlus;
        Point pressPt;
        bool dragging;
        int dropMarker = -1;
        readonly ToolTip tip = new ToolTip();
        int tipIndex = -2;

        public event Action<int> TabMenu;

        static readonly Font BoldFont = new Font(UI.Font, FontStyle.Bold);
        const int Gap = 3;

        bool pressEmpty;          // 탭 줄 빈 곳을 누름 → 끌면 칸 위치 바꾸기
        bool swapHighlight;       // 다른 칸이 이 칸 위로 끌려 옴

        public TabStrip(PaneControl p)
        {
            pane = p;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Height = 34;
            Font = UI.Font;
            AllowDrop = true;
        }

        // 탭이 넘칠 때: 양옆 화살표로 보이는 탭 목록을 한 칸씩 민다 (고른 탭은 그대로)
        const int ArrowW = 20, ListW = 46;
        Rectangle listRect;
        ContextMenuStrip tabListMenu;
        bool overflow;
        int firstTab, maxFirst, lastActive = -2;
        int viewL = 6, viewR;
        Rectangle leftArrow, rightArrow;
        int hoverArrow;           // -1 왼쪽, 1 오른쪽, 0 없음
        bool hoverList;
        int plusLeft;

        void ScrollTabs(int dir)
        {
            int n = Math.Max(0, Math.Min(maxFirst, firstTab + dir));
            if (n != firstTab) { firstTab = n; Invalidate(); }
        }

        void Layout2()
        {
            tabRects.Clear();
            var g = CreateGraphics();
            int avail = Width - 6 - 36;
            var widths = new List<int>();
            int total = 0;
            foreach (var t in pane.Tabs)
            {
                int w = TextRenderer.MeasureText(g, t.Title, BoldFont, Size.Empty, TextFormatFlags.NoPrefix).Width + 10 + 20 + 26 + 6;
                w = Math.Max(80, Math.Min(260, w));
                widths.Add(w); total += w + Gap;
            }
            g.Dispose();
            // 넘치면 고르게 줄임 (최소 100, 그래도 넘치면 마우스 휠로 넘겨 봄)
            if (total > avail && pane.Tabs.Count > 0)
            {
                int each = Math.Max(100, avail / pane.Tabs.Count - Gap);
                for (int i = 0; i < widths.Count; i++) widths[i] = Math.Min(widths[i], each);
            }
            int sum = 0;
            foreach (var w in widths) sum += w + Gap;
            overflow = sum > avail;
            int n = widths.Count;
            if (!overflow)
            {
                firstTab = 0; maxFirst = 0; viewL = 6; viewR = Width - 36;
                leftArrow = rightArrow = listRect = Rectangle.Empty;
            }
            else
            {
                // 탭 줄 오른쪽 끝에 [+] [◀] [▶] [전체 탭 목록] 순서로 모은다
                listRect = new Rectangle(Width - 6 - ListW, 8, ListW, Height - 12);
                rightArrow = new Rectangle(listRect.Left - 2 - ArrowW, 8, ArrowW, Height - 12);
                leftArrow = new Rectangle(rightArrow.Left - ArrowW, 8, ArrowW, Height - 12);
                plusLeft = leftArrow.Left - 4 - 28;
                viewL = 6; viewR = plusLeft - 4;
                int vw = viewR - viewL;
                // 끝까지 밀었을 때 첫 탭: 거기서부터 끝 탭까지가 보이는 폭 안에 들어오는 가장 앞 탭
                maxFirst = n - 1;
                int acc = 0;
                for (int i = n - 1; i >= 0; i--)
                {
                    acc += widths[i] + Gap;
                    if (acc > vw) break;
                    maxFirst = i;
                }
                // 고른 탭이 바뀐 경우(새 탭·키보드 이동 등)에만 보이게 맞춘다. 화살표로 미는 것은 선택을 바꾸지 않음
                if (pane.ActiveIndex != lastActive && pane.ActiveIndex >= 0 && pane.ActiveIndex < n)
                {
                    int a = pane.ActiveIndex;
                    if (a < firstTab) firstTab = a;
                    else
                    {
                        int used = 0;
                        for (int i = firstTab; i <= a; i++) used += widths[i] + Gap;
                        while (used > vw && firstTab < a) { used -= widths[firstTab] + Gap; firstTab++; }
                    }
                }
                firstTab = Math.Max(0, Math.Min(maxFirst, firstTab));
            }
            lastActive = pane.ActiveIndex;
            int x = overflow ? viewL : 6;
            if (overflow) for (int i = 0; i < firstTab; i++) x -= widths[i] + Gap;
            for (int i = 0; i < n; i++)
            {
                tabRects.Add(new Rectangle(x, 4, widths[i], Height - 4));
                x += widths[i] + Gap;
            }
            plusRect = new Rectangle(overflow ? plusLeft : Math.Min(x + 1, Width - 33), 8, 28, Height - 12);
        }

        bool InView(Point pt) { return pt.X >= viewL && pt.X < viewR; }

        bool OnOverflowButtons(Point pt)
        {
            return overflow && (leftArrow.Contains(pt) || rightArrow.Contains(pt) || listRect.Contains(pt));
        }

        // 모든 탭 목록 (넘칠 때 나오는 버튼): 고르면 그 탭으로 이동
        void ShowTabList()
        {
            // 메뉴가 닫히는 중에 바로 Dispose 하면, 메뉴 밖(헤더 등)을 누른 마우스 처리가 해제된 메뉴를 건드려 앱이 죽는다.
            // 그래서 닫을 때가 아니라 다음에 열 때 이전 메뉴를 해제한다.
            if (tabListMenu != null) { tabListMenu.Dispose(); tabListMenu = null; }
            var menu = tabListMenu = new ContextMenuStrip();
            for (int i = 0; i < pane.Tabs.Count; i++)
            {
                int idx = i;
                var t = pane.Tabs[i];
                var mi = new ToolStripMenuItem((i + 1) + ". " + t.Title.Replace("&", "&&")) { Checked = i == pane.ActiveIndex, ToolTipText = t.Host.CurrentPath };
                if (t.Icon != null) mi.Image = t.Icon;
                mi.Click += delegate { pane.RequestActivate(); pane.SelectTab(idx, true); };
                menu.Items.Add(mi);
            }
            menu.Show(this, new Point(listRect.Left, Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Layout2();
            var T = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(swapHighlight ? Blend(T.StripBack, T.Accent, 0.35) : T.StripBack);
            if (pane.IsActivePane)
                using (var b = new SolidBrush(T.Accent)) g.FillRectangle(b, 0, 0, Width, 2);
            // 탭 줄 아래 경계선 (고른 탭 아래는 탭이 덮어서 끊김 → 아래 이동 줄과 이어짐)
            using (var pen = new Pen(T.TabBorder)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

            g.SetClip(new Rectangle(0, 0, Math.Max(0, viewR), Height));
            int activeI = -1;
            for (int i = 0; i < pane.Tabs.Count; i++)
            {
                if (i == pane.ActiveIndex) { activeI = i; continue; }
                DrawTab(g, i, false);
            }
            if (activeI >= 0) DrawTab(g, activeI, true);

            if (dropMarker >= 0)
            {
                int mx = dropMarker < tabRects.Count ? tabRects[dropMarker].Left - 2 : (tabRects.Count > 0 ? tabRects[tabRects.Count - 1].Right + 1 : 4);
                using (var b = new SolidBrush(T.Accent)) g.FillRectangle(b, mx - 1, 4, 3, Height - 6);
            }
            g.ResetClip();
            if (overflow)
            {
                // 탭 목록 버튼: 전체 탭 개수 + ▾
                if (hoverList) using (var b = new SolidBrush(T.TabHover)) g.FillRectangle(b, listRect);
                using (var pen = new Pen(T.TabBorder)) g.DrawRectangle(pen, listRect.Left, listRect.Top, listRect.Width - 1, listRect.Height - 1);
                TextRenderer.DrawText(g, pane.Tabs.Count.ToString(), BoldFont, new Rectangle(listRect.Left, listRect.Top, listRect.Width - 16, listRect.Height), T.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                using (var b = new SolidBrush(T.SubText))
                {
                    int cx = listRect.Right - 11, cy = listRect.Top + listRect.Height / 2;
                    g.FillPolygon(b, new[] { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 3) });
                }
                DrawArrow(g, leftArrow, -1, firstTab > 0);
                DrawArrow(g, rightArrow, 1, firstTab < maxFirst);
            }
            // + 버튼
            if (hoverPlus) using (var b = new SolidBrush(T.TabHover)) g.FillRectangle(b, plusRect);
            using (var pen = new Pen(T.SubText, 1.4f))
            {
                int cx = plusRect.Left + plusRect.Width / 2, cy = plusRect.Top + plusRect.Height / 2;
                g.DrawLine(pen, cx - 6, cy, cx + 6, cy);
                g.DrawLine(pen, cx, cy - 6, cx, cy + 6);
            }
            if (swapHighlight)
                TextRenderer.DrawText(g, "여기에 놓으면 칸 위치를 바꿉니다", BoldFont, new Rectangle(0, 0, Width - 8, Height), T.TabTextActive,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        void DrawArrow(Graphics g, Rectangle r, int dir, bool enabled)
        {
            var T = Theme.Current;
            if (enabled && hoverArrow == dir) using (var b = new SolidBrush(T.TabHover)) g.FillRectangle(b, r);
            using (var pen = new Pen(enabled ? T.SubText : T.DimText, 1.8f))
            {
                int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
                g.DrawLine(pen, cx - dir * 2, cy - 5, cx + dir * 3, cy);
                g.DrawLine(pen, cx + dir * 3, cy, cx - dir * 2, cy + 5);
            }
        }

        static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        void DrawTab(Graphics g, int i, bool active)
        {
            var T = Theme.Current;
            var full = tabRects[i];
            if (full.Right < viewL || full.Left > viewR) return;
            // 고른 탭은 위로 더 올라오고 아래 경계선을 덮음, 안 고른 탭은 낮게
            var r = active ? new Rectangle(full.Left, 3, full.Width, Height - 3) : new Rectangle(full.Left, 7, full.Width, Height - 8);
            var back = active ? (pane.IsActivePane ? T.TabSel : T.TabSelIdle) : (i == hover ? T.TabHover : T.TabInactive);
            using (var path = RoundTop(r, 6))
            {
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                using (var pen = new Pen(T.TabBorder)) g.DrawPath(pen, path);
            }
            var t = pane.Tabs[i];
            int tx = r.Left + 10;
            if (t.Icon != null) { g.DrawImage(t.Icon, tx, r.Top + (r.Height - 16) / 2 + (active ? 1 : 0), 16, 16); tx += 20; }
            // TextRenderer 는 클립 영역을 무시하므로 보이는 범위(viewR) 안으로 직접 자른다
            var textRect = new Rectangle(tx, r.Top + (active ? 1 : 0), Math.Min(r.Right - 26, viewR - 4) - tx, r.Height);
            if (textRect.Width > 8)
                TextRenderer.DrawText(g, t.Title, active ? BoldFont : Font, textRect, active ? Color.White : T.TabText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if ((active || i == hover) && r.Right <= viewR)
            {
                var cr = CloseRect(r);
                if (i == hoverClose) using (var b = new SolidBrush(T.Hover)) g.FillEllipse(b, cr);
                using (var pen = new Pen(active ? Color.White : T.SubText, 1.3f))
                {
                    int m = 6;
                    g.DrawLine(pen, cr.Left + m, cr.Top + m, cr.Right - m, cr.Bottom - m);
                    g.DrawLine(pen, cr.Right - m, cr.Top + m, cr.Left + m, cr.Bottom - m);
                }
            }
        }

        static Rectangle CloseRect(Rectangle r) { return new Rectangle(r.Right - 24, r.Top + (r.Height - 20) / 2, 20, 20); }

        static GraphicsPath RoundTop(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            p.AddLine(r.Left, r.Bottom, r.Left, r.Top + rad);
            p.AddArc(r.Left, r.Top, rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad * 2, r.Top, rad * 2, rad * 2, 270, 90);
            p.AddLine(r.Right, r.Top + rad, r.Right, r.Bottom);
            return p;
        }

        int HitTab(Point pt)
        {
            for (int i = 0; i < tabRects.Count; i++) if (tabRects[i].Contains(pt) && InView(pt)) return i;
            return -1;
        }

        int InsertIndex(Point pt)
        {
            for (int i = 0; i < tabRects.Count; i++)
                if (pt.X < tabRects[i].Left + tabRects[i].Width / 2) return i;
            return tabRects.Count;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (pressEmpty && e.Button == MouseButtons.Left && (Math.Abs(e.X - pressPt.X) > 8 || Math.Abs(e.Y - pressPt.Y) > 8))
            {
                pressEmpty = false;
                DoDragDrop(new PaneDragData { Pane = pane }, DragDropEffects.Move);
                return;
            }
            if (pressIndex >= 0 && e.Button == MouseButtons.Left)
            {
                if (!dragging && (Math.Abs(e.X - pressPt.X) > 6 || Math.Abs(e.Y - pressPt.Y) > 6)) dragging = true;
                if (dragging)
                {
                    if (e.Y < -20 || e.Y > Height + 20)
                    {
                        // 탭 줄 밖으로 → 다른 칸으로 옮기기
                        var tab = pane.Tabs[pressIndex];
                        pressIndex = -1; dragging = false;
                        DoDragDrop(new TabDragData { Pane = pane, Tab = tab }, DragDropEffects.Move);
                        return;
                    }
                    int target = HitTab(e.Location);
                    if (target >= 0 && target != pressIndex)
                    {
                        pane.MoveTab(pressIndex, target);
                        pressIndex = target;
                    }
                    return;
                }
            }
            int h = HitTab(e.Location);
            int hc = h >= 0 && CloseRect(tabRects[h]).Contains(e.Location) ? h : -1;
            bool hp = plusRect.Contains(e.Location);
            int ha = overflow ? (leftArrow.Contains(e.Location) ? -1 : rightArrow.Contains(e.Location) ? 1 : 0) : 0;
            bool hl = overflow && listRect.Contains(e.Location);
            if (h != hover || hc != hoverClose || hp != hoverPlus || ha != hoverArrow || hl != hoverList) { hover = h; hoverClose = hc; hoverPlus = hp; hoverArrow = ha; hoverList = hl; Invalidate(); }
            if (h != tipIndex)
            {
                tipIndex = h;
                if (h >= 0) tip.SetToolTip(this, pane.Tabs[h].Host.CurrentPath);
                else tip.SetToolTip(this, hp ? "새 탭 (Ctrl+T)" : "빈 곳을 잡고 다른 칸으로 끌면 칸 위치를 바꿉니다");
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = hoverClose = -1; hoverPlus = false; hoverArrow = 0; hoverList = false; tipIndex = -2; Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            pane.RequestActivate();
            int h = HitTab(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                if (overflow && listRect.Contains(e.Location)) { ShowTabList(); return; }
                if (overflow && leftArrow.Contains(e.Location)) { ScrollTabs(-1); return; }
                if (overflow && rightArrow.Contains(e.Location)) { ScrollTabs(1); return; }
                if (h >= 0 && CloseRect(tabRects[h]).Contains(e.Location)) return; // 놓을 때 닫음
                if (h >= 0) { pane.SelectTab(h, true); pressIndex = h; pressPt = e.Location; dragging = false; }
                else if (!plusRect.Contains(e.Location)) { pressEmpty = true; pressPt = e.Location; }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = HitTab(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                if (!dragging && h >= 0 && CloseRect(tabRects[h]).Contains(e.Location)) pane.CloseTab(h);
                else if (!dragging && plusRect.Contains(e.Location)) pane.AddTab(pane.ActiveHost != null ? pane.ActiveHost.CurrentPath : null, true);
            }
            else if (e.Button == MouseButtons.Middle && h >= 0) pane.CloseTab(h);
            else if (e.Button == MouseButtons.Right && TabMenu != null)
            {
                if (h >= 0) pane.SelectTab(h);
                TabMenu(h);
            }
            pressIndex = -1; dragging = false; pressEmpty = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            // 화살표·목록 버튼을 빠르게 연속 클릭하면 더블클릭으로 잡히므로 새 탭을 만들지 않는다
            if (e.Button == MouseButtons.Left && HitTab(e.Location) < 0 && !plusRect.Contains(e.Location) && !OnOverflowButtons(e.Location))
                pane.AddTab(pane.ActiveHost != null ? pane.ActiveHost.CurrentPath : null, true);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (overflow) ScrollTabs(e.Delta > 0 ? -1 : 1);
        }

        // ───── 끌어다 놓기: 다른 칸의 탭, 또는 폴더 → 새 탭 ─────
        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            var pt = PointToClient(new Point(e.X, e.Y));
            var pd = e.Data.GetData(typeof(PaneDragData)) as PaneDragData;
            if (pd != null)
            {
                bool other = pd.Pane != pane;
                e.Effect = other ? DragDropEffects.Move : DragDropEffects.None;
                SetSwapHighlight(other);
                return;
            }
            if (e.Data.GetDataPresent(typeof(TabDragData)))
            {
                e.Effect = DragDropEffects.Move;
                SetMarker(InsertIndex(pt));
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                bool anyDir = files != null && Array.Exists(files, Directory.Exists);
                e.Effect = anyDir ? DragDropEffects.Link : DragDropEffects.None;
                SetMarker(anyDir ? InsertIndex(pt) : -1);
            }
            else e.Effect = DragDropEffects.None;
        }

        protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); SetMarker(-1); SetSwapHighlight(false); }

        void SetSwapHighlight(bool on) { if (on != swapHighlight) { swapHighlight = on; Invalidate(); } }

        void SetMarker(int m) { if (m != dropMarker) { dropMarker = m; Invalidate(); } }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            int at = dropMarker >= 0 ? dropMarker : pane.Tabs.Count;
            SetMarker(-1);
            SetSwapHighlight(false);
            var pd = e.Data.GetData(typeof(PaneDragData)) as PaneDragData;
            if (pd != null) { if (pd.Pane != pane) pane.RequestSwap(pd.Pane); return; }
            var td = e.Data.GetData(typeof(TabDragData)) as TabDragData;
            if (td != null)
            {
                if (td.Pane == pane)
                {
                    int from = pane.Tabs.IndexOf(td.Tab);
                    if (from >= 0) pane.MoveTab(from, Math.Min(at > from ? at - 1 : at, pane.Tabs.Count - 1));
                }
                else td.Pane.GiveTabTo(td.Tab, pane, at);
                pane.RequestActivate();
                return;
            }
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (var f in files)
                if (Directory.Exists(f)) { pane.InsertTab(at++, f, true); }
            pane.RequestActivate();
        }
    }
}
