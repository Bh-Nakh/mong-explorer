// 왼쪽 아래 즐겨찾기: 윈도우 탐색기 '빠른 실행'에 고정한 폴더와 항상 같은 목록 (추가·삭제도 윈도우 쪽에 반영). 표시: J:\클..\몽탐색기
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NewExplorer
{
    public class FavoritesList : ListBox
    {
        readonly ToolTip tip = new ToolTip();
        int tipIndex = -1, pressIndex = -1, hoverIndex = -1;
        readonly Timer settleTimer = new Timer { Interval = 700 };   // 고정·해제가 윈도우에 반영되길 기다렸다 다시 읽음 (여러 번)
        int settleStep = 3;
        static readonly int[] SettleMs = { 700, 2000, 4000 };
        FileSystemWatcher watcher;                                   // 윈도우가 빠른 실행 고정 목록을 저장하는 파일을 지켜봄

        /// <summary>즐겨찾기를 누름 (경로, 새 탭이면 true)</summary>
        public event Action<string, bool> Picked;
        public event Action Changed;
        public Func<string> CurrentFolder;

        public FavoritesList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 24;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            AllowDrop = true;
            Font = UI.Font;
            SelectionMode = SelectionMode.One;
            settleTimer.Tick += delegate
            {
                settleTimer.Stop();
                RefreshPinned();
                if (++settleStep < SettleMs.Length) { settleTimer.Interval = SettleMs[settleStep]; settleTimer.Start(); }
            };
            ApplyTheme();
            Reload();
            WatchWindowsPins();
        }

        /// <summary>추가·삭제 뒤 반영이 늦는 윈도우를 위해 0.7초·2초·4초 뒤에 다시 읽음</summary>
        void Settle()
        {
            settleStep = 0;
            settleTimer.Stop();
            settleTimer.Interval = SettleMs[0];
            settleTimer.Start();
        }

        /// <summary>윈도우 탐색기에서 고정/해제하면 그 목록 파일이 바뀐다 → 바로 다시 읽음 (윈도우 10·11 공통)</summary>
        void WatchWindowsPins()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent\AutomaticDestinations");
                if (!Directory.Exists(dir)) return;
                watcher = new FileSystemWatcher(dir, "f01b4d95cf55d32a*") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                FileSystemEventHandler h = delegate
                {
                    try { if (IsHandleCreated) BeginInvoke(new Action(() => { settleStep = SettleMs.Length - 1; settleTimer.Stop(); settleTimer.Interval = 500; settleTimer.Start(); })); } catch { }
                };
                watcher.Changed += h; watcher.Created += h; watcher.Deleted += h;
                watcher.Renamed += delegate { h(null, null); };
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Shell.FavLog("고정 파일 감시 시작 실패: " + ex.Message); }
        }

        public void ApplyTheme()
        {
            BackColor = Theme.Current.Content;
            ForeColor = Theme.Current.Text;
            if (IsHandleCreated) Theme.ApplyToWindow(Handle);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyToWindow(Handle);   // 스크롤 막대도 테마에 맞춤
        }

        /// <summary>처음 불러올 때: 예전 버전이 따로 저장해 둔 몽탐색기 즐겨찾기는 윈도우 빠른 실행에 고정으로 옮기고 비운다 (이제 윈도우와 한 목록)</summary>
        public void Reload()
        {
            var old = Settings.Current.Favorites;
            if (old != null && old.Count > 0)
            {
                var pins = Shell.PinnedQuickAccess();
                foreach (var f in old)
                    if (Directory.Exists(f) && !ContainsPath(pins, f)) Shell.PinQuickAccess(f);
                old.Clear();
                if (Changed != null) Changed();
            }
            RefreshPinned();
        }

        /// <summary>윈도우 빠른 실행의 고정 폴더를 다시 읽어 바뀐 경우에만 목록을 갱신 (창이 다시 활성화될 때도 부름)</summary>
        public void RefreshPinned()
        {
            var pins = Shell.PinnedQuickAccess();
            bool same = pins.Count == Items.Count;
            for (int i = 0; same && i < pins.Count; i++) if (!Shell.SamePath(pins[i], (string)Items[i])) same = false;
            if (same) return;
            BeginUpdate();
            Items.Clear();
            foreach (var p in pins) Items.Add(p);
            EndUpdate();
        }

        static bool ContainsPath(List<string> l, string p) { foreach (var s in l) if (Shell.SamePath(s, p)) return true; return false; }

        /// <summary>폴더를 윈도우 빠른 실행에 고정 (이미 있으면 그대로)</summary>
        public void AddFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            foreach (string s in Items) if (Shell.SamePath(s, path)) return;
            if (!Shell.PinQuickAccess(path))
            {
                MessageBox.Show(FindForm(), "즐겨찾기에 추가하지 못했습니다.\n(폴더가 아니거나 윈도우 빠른 실행에 고정할 수 없는 위치입니다)", "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RefreshPinned();
            Settle();
        }

        /// <summary>윈도우 빠른 실행에서 고정 해제</summary>
        public void RemoveFolder(string path)
        {
            if (!Shell.UnpinQuickAccess(path))
                MessageBox.Show(FindForm(), "즐겨찾기에서 삭제하지 못했습니다.\n윈도우 탐색기에서 해제해 주세요.\n(원인 기록: " + System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mong-favorites.log") + ")", "몽탐색기", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshPinned();
            Settle();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var path = (string)Items[e.Index];
            var g = e.Graphics;
            var T = Theme.Current;
            var back = e.Index == hoverIndex ? T.Hover : T.Content;
            using (var b = new SolidBrush(back)) g.FillRectangle(b, e.Bounds);
            var icon = Shell.SmallIcon(path);
            int x = e.Bounds.Left + 8;
            if (icon != null) g.DrawImage(icon, x, e.Bounds.Top + (e.Bounds.Height - 16) / 2, 16, 16);
            x += 22;
            bool exists = !Shell.IsFileSystemPath(path) || Directory.Exists(path);
            TextRenderer.DrawText(g, Shell.ShortName(path), Font, new Rectangle(x, e.Bounds.Top, e.Bounds.Right - x - 4, e.Bounds.Height),
                exists ? T.Text : T.DimText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexFromPoint(e.Location);
            if (i >= 0 && !GetItemRectangle(i).Contains(e.Location)) i = -1;
            if (i != hoverIndex) { int old = hoverIndex; hoverIndex = i; InvalidateItem(old); InvalidateItem(i); }
            if (i != tipIndex) { tipIndex = i; tip.SetToolTip(this, i >= 0 ? (string)Items[i] : ""); }
        }

        void InvalidateItem(int i) { if (i >= 0 && i < Items.Count) Invalidate(GetItemRectangle(i)); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); int old = hoverIndex; hoverIndex = -1; InvalidateItem(old); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = IndexFromPoint(e.Location);
            if (i >= 0 && !GetItemRectangle(i).Contains(e.Location)) i = -1;
            pressIndex = e.Button == MouseButtons.Left ? i : -1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = IndexFromPoint(e.Location);
            if (i >= 0 && !GetItemRectangle(i).Contains(e.Location)) i = -1;
            if (e.Button == MouseButtons.Left && i >= 0 && pressIndex == i && Picked != null) Picked((string)Items[i], false);
            else if (e.Button == MouseButtons.Middle && i >= 0 && Picked != null) Picked((string)Items[i], true);
            else if (e.Button == MouseButtons.Right) ShowMenu(i, e.Location);
            pressIndex = -1;
            ClearSelected();
        }

        void ShowMenu(int i, Point pt)
        {
            var m = new ContextMenuStrip();
            if (i >= 0)
            {
                string path = (string)Items[i];
                m.Items.Add("열기", null, delegate { if (Picked != null) Picked(path, false); });
                m.Items.Add("새 탭에서 열기", null, delegate { if (Picked != null) Picked(path, true); });
                m.Items.Add("경로 복사", null, delegate { try { Clipboard.SetText(path); } catch { } });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("즐겨찾기에서 삭제", null, delegate { RemoveFolder(path); });
            }
            else
            {
                var add = m.Items.Add("현재 폴더 추가", null, delegate { if (CurrentFolder != null) AddFolder(CurrentFolder()); });
                add.Enabled = CurrentFolder != null && CurrentFolder() != null;
            }
            m.Closed += delegate { BeginInvoke(new Action(m.Dispose)); };
            m.Show(this, pt);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                e.Effect = files != null && Array.Exists(files, Directory.Exists) ? DragDropEffects.Link : DragDropEffects.None;
            }
            else e.Effect = DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (var f in files) if (Directory.Exists(f)) AddFolder(f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { settleTimer.Dispose(); if (watcher != null) watcher.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
