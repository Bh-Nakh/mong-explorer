// 탭 아래 경로 표시줄: 드라이브·폴더 이름 클릭 → 이동, › 클릭 → 하위 폴더 메뉴, 빈 곳 클릭 → 주소 입력
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NewExplorer
{
    public class Breadcrumb : Control
    {
        class Seg
        {
            public string Text, Path;
            public Rectangle TextRect, ArrowRect;
            public bool Visible;
        }

        readonly List<Seg> segs = new List<Seg>();
        string path;
        Rectangle overflowRect = Rectangle.Empty;    // « (앞부분 숨김)
        Rectangle rootArrowRect;                     // 맨 앞 › (드라이브 목록)
        int hoverText = -1, hoverArrow = -1;         // -2 = overflow, -3 = root
        readonly TextBox edit;
        public event Action<string> NavigateRequested;

        public Breadcrumb()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = UI.Font;
            Height = 26;
            edit = new TextBox
            {
                Visible = false,
                BorderStyle = BorderStyle.None,
                Font = Font,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.FileSystemDirectories
            };
            edit.KeyDown += EditKeyDown;
            edit.LostFocus += delegate { EndEdit(); };
            Controls.Add(edit);
        }

        public string PathValue
        {
            get { return path; }
            set { path = value; Build(); Invalidate(); }
        }

        public bool Editing { get { return edit.Visible; } }

        void Build()
        {
            segs.Clear();
            if (string.IsNullOrEmpty(path)) return;
            if (Shell.IsFileSystemPath(path))
            {
                string p = path.TrimEnd('\\');
                if (p.StartsWith(@"\\"))
                {
                    var parts = p.Substring(2).Split('\\');
                    string acc = @"\\" + parts[0];
                    segs.Add(new Seg { Text = acc, Path = acc });
                    for (int i = 1; i < parts.Length; i++) { acc += @"\" + parts[i]; segs.Add(new Seg { Text = parts[i], Path = acc }); }
                }
                else
                {
                    string drive = p.Substring(0, 2);
                    segs.Add(new Seg { Text = drive, Path = drive + @"\" });
                    string acc = drive;
                    if (p.Length > 3)
                        foreach (var part in p.Substring(3).Split('\\'))
                        {
                            if (part.Length == 0) continue;
                            acc += @"\" + part;
                            segs.Add(new Seg { Text = part, Path = acc });
                        }
                }
            }
            else
            {
                // 가상 폴더: 부모를 거슬러 올라가며 이름 모음 (바탕 화면 제외)
                var item = Shell.ItemFromPath(path);
                var chain = new List<Seg>();
                while (item != null)
                {
                    string pn = Shell.ParsingName(item);
                    string dn = Shell.Name(item, SIGDN.NORMALDISPLAY);
                    IShellItem parent;
                    bool hasParent = item.GetParent(out parent) == 0 && parent != null;
                    Marshal.ReleaseComObject(item);
                    if (!hasParent) break;      // 맨 위(바탕 화면)는 넣지 않음
                    chain.Insert(0, new Seg { Text = dn ?? pn, Path = pn });
                    item = parent;
                }
                segs.AddRange(chain);
                if (segs.Count == 0) segs.Add(new Seg { Text = Shell.DisplayName(path), Path = path });
            }
        }

        void Layout2(Graphics g)
        {
            const int arrowW = 16, pad = 6;
            rootArrowRect = new Rectangle(2, 2, arrowW, Height - 4);
            int x = rootArrowRect.Right;
            int[] tw = new int[segs.Count];
            int total = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                tw[i] = TextRenderer.MeasureText(g, segs[i].Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + pad;
                total += tw[i] + arrowW;
            }
            int avail = Width - x - 40;
            int first = 0;
            overflowRect = Rectangle.Empty;
            if (total > avail)
            {
                int ow = 22;
                avail -= ow;
                while (first < segs.Count - 1 && total > avail) { total -= tw[first] + arrowW; first++; }
                overflowRect = new Rectangle(x, 2, ow, Height - 4);
                x += ow;
            }
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                s.Visible = i >= first;
                if (!s.Visible) continue;
                int w = Math.Min(tw[i], Math.Max(30, Width - x - arrowW - 40));
                s.TextRect = new Rectangle(x, 2, w, Height - 4); x += w;
                s.ArrowRect = new Rectangle(x, 2, arrowW, Height - 4); x += arrowW;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var T = Theme.Current;
            g.Clear(T.Content);
            using (var pen = new Pen(Focused || Editing ? T.Accent : T.Border))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            if (Editing) return;
            Layout2(g);
            var hoverBack = T.Hover;
            if (hoverArrow == -3) using (var b = new SolidBrush(hoverBack)) g.FillRectangle(b, rootArrowRect);
            DrawArrow(g, rootArrowRect);
            if (!overflowRect.IsEmpty)
            {
                if (hoverArrow == -2) using (var b = new SolidBrush(hoverBack)) g.FillRectangle(b, overflowRect);
                TextRenderer.DrawText(g, "«", Font, overflowRect, T.SubText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (!s.Visible) continue;
                if (i == hoverText || i == hoverArrow)
                {
                    using (var b = new SolidBrush(hoverBack)) { g.FillRectangle(b, s.TextRect); g.FillRectangle(b, s.ArrowRect); }
                    using (var pen = new Pen(T.HoverBorder)) { g.DrawRectangle(pen, Rectangle.Union(s.TextRect, s.ArrowRect)); g.DrawLine(pen, s.ArrowRect.Left, s.TextRect.Top, s.ArrowRect.Left, s.TextRect.Bottom); }
                }
                TextRenderer.DrawText(g, s.Text, Font, s.TextRect, T.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                DrawArrow(g, s.ArrowRect);
            }
        }

        static void DrawArrow(Graphics g, Rectangle r)
        {
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            using (var pen = new Pen(Theme.Current.SubText, 1.2f))
            {
                g.DrawLine(pen, cx - 2, cy - 4, cx + 2, cy);
                g.DrawLine(pen, cx + 2, cy, cx - 2, cy + 4);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int ht = -1, ha = -1;
            if (rootArrowRect.Contains(e.Location)) ha = -3;
            else if (overflowRect.Contains(e.Location)) ha = -2;
            else
                for (int i = 0; i < segs.Count; i++)
                {
                    if (!segs[i].Visible) continue;
                    if (segs[i].TextRect.Contains(e.Location)) ht = i;
                    else if (segs[i].ArrowRect.Contains(e.Location)) ha = i;
                }
            if (ht != hoverText || ha != hoverArrow) { hoverText = ht; hoverArrow = ha; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverText = hoverArrow = -1; Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { ShowCopyMenu(e.Location); return; }
            if (e.Button == MouseButtons.Middle)
            {
                for (int i = 0; i < segs.Count; i++)
                    if (segs[i].Visible && segs[i].TextRect.Contains(e.Location) && MiddleClicked != null) MiddleClicked(segs[i].Path);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (rootArrowRect.Contains(e.Location)) { ShowDrivesMenu(rootArrowRect); return; }
            if (overflowRect.Contains(e.Location)) { ShowOverflowMenu(); return; }
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                if (!s.Visible) continue;
                if (s.TextRect.Contains(e.Location)) { Go(s.Path); return; }
                if (s.ArrowRect.Contains(e.Location)) { ShowSubMenu(s.Path, s.ArrowRect, i + 1 < segs.Count ? segs[i + 1].Path : null); return; }
            }
            BeginEdit();
        }

        public event Action<string> MiddleClicked;

        void Go(string p) { if (NavigateRequested != null) NavigateRequested(p); }

        void ShowMenu(ContextMenuStrip m, Rectangle under)
        {
            m.Show(this, new Point(under.Left, Height));
        }

        ToolStripMenuItem MenuItemFor(string p, string text, bool bold)
        {
            var mi = new ToolStripMenuItem(text, Shell.SmallIcon(p));
            if (bold) mi.Font = new Font(mi.Font, FontStyle.Bold);
            mi.Click += delegate { Go(p); };
            mi.MouseUp += (s, ev) => { if (ev.Button == MouseButtons.Middle && MiddleClicked != null) MiddleClicked(p); };
            return mi;
        }

        static ContextMenuStrip NewMenu()
        {
            var m = new ContextMenuStrip { ShowImageMargin = true, MaximumSize = new Size(600, 700) };
            m.Closed += (s, e) => BeginInvokeDispose((ContextMenuStrip)s);
            return m;
        }

        static void BeginInvokeDispose(ContextMenuStrip m)
        {
            var t = new Timer { Interval = 50 };
            t.Tick += delegate { t.Dispose(); m.Dispose(); };
            t.Start();
        }

        void ShowSubMenu(string parent, Rectangle under, string current)
        {
            var subs = Shell.SubFolders(parent);
            if (!Shell.IsFileSystemPath(parent)) { subs = ShellChildren(parent); }
            var m = NewMenu();
            if (subs.Count == 0) m.Items.Add(new ToolStripMenuItem("(하위 폴더 없음)") { Enabled = false });
            foreach (var p in subs)
            {
                string name = Shell.IsFileSystemPath(p) ? Path.GetFileName(p.TrimEnd('\\')) : Shell.DisplayName(p);
                if (string.IsNullOrEmpty(name)) name = p;
                m.Items.Add(MenuItemFor(p, name, Shell.SamePath(p, current)));
            }
            ShowMenu(m, under);
        }

        static List<string> ShellChildren(string parent)
        {
            // 내 PC 등 가상 폴더: 드라이브 목록만 보여 줌
            var list = new List<string>();
            if (string.Equals(parent, Shell.ThisPC, StringComparison.OrdinalIgnoreCase))
                foreach (var d in DriveInfo.GetDrives()) list.Add(d.Name);
            return list;
        }

        void ShowDrivesMenu(Rectangle under)
        {
            var m = NewMenu();
            m.Items.Add(MenuItemFor(Shell.ThisPC, Shell.DisplayName(Shell.ThisPC), false));
            m.Items.Add(new ToolStripSeparator());
            foreach (var d in DriveInfo.GetDrives())
                m.Items.Add(MenuItemFor(d.Name, Shell.DisplayName(d.Name), path != null && path.StartsWith(d.Name.Substring(0, 2), StringComparison.OrdinalIgnoreCase)));
            ShowMenu(m, under);
        }

        void ShowOverflowMenu()
        {
            var m = NewMenu();
            for (int i = segs.Count - 1; i >= 0; i--)
                if (!segs[i].Visible) m.Items.Add(MenuItemFor(segs[i].Path, segs[i].Text, false));
            ShowMenu(m, overflowRect);
        }

        void ShowCopyMenu(Point pt)
        {
            var m = NewMenu();
            var copy = new ToolStripMenuItem("주소 복사");
            copy.Click += delegate { try { Clipboard.SetText(path ?? ""); } catch { } };
            var editItem = new ToolStripMenuItem("주소 편집");
            editItem.Click += delegate { BeginEdit(); };
            m.Items.Add(copy); m.Items.Add(editItem);
            m.Show(this, pt);
        }

        // ───── 주소 직접 입력 ─────
        public void BeginEdit()
        {
            edit.Text = Shell.IsFileSystemPath(path) ? path : Shell.DisplayName(path);
            edit.SetBounds(6, (Height - edit.PreferredHeight) / 2 + 1, Width - 12, edit.PreferredHeight);
            edit.BackColor = Theme.Current.Content;
            edit.ForeColor = Theme.Current.Text;
            edit.Visible = true;
            edit.Focus();
            edit.SelectAll();
            Invalidate();
        }

        void EndEdit()
        {
            if (!edit.Visible) return;
            edit.Visible = false;
            Invalidate();
        }

        void EditKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                string t = Environment.ExpandEnvironmentVariables(edit.Text.Trim().Trim('"'));
                EndEdit();
                if (t.Length == 0) return;
                if (t.Length == 2 && t[1] == ':') t += @"\";
                if (Shell.IsFileSystemPath(t) && File.Exists(t))
                {
                    // 파일 주소 → 파일 열기
                    try { System.Diagnostics.Process.Start(t); } catch { }
                    return;
                }
                if (Shell.IsFileSystemPath(t) && !Directory.Exists(t))
                {
                    MessageBox.Show(FindForm(), "'" + t + "'을(를) 찾을 수 없습니다.", "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                IntPtr chk = Shell.IsFileSystemPath(t) ? IntPtr.Zero : Shell.PidlFromPath(t);
                if (chk != IntPtr.Zero) Native.ILFree(chk);
                if (!Shell.IsFileSystemPath(t) && chk == IntPtr.Zero)
                {
                    // 셸이 모르는 이름 → 실행 시도 (예: notepad, https://...)
                    try { System.Diagnostics.Process.Start(t); } catch { MessageBox.Show(FindForm(), "'" + t + "'을(를) 찾을 수 없습니다.", "몽탐색기"); }
                    return;
                }
                Go(t);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                EndEdit();
                if (EditCancelled != null) EditCancelled();
            }
        }

        public event Action EditCancelled;
    }
}
