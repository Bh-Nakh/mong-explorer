// 메뉴 줄 빈 곳의 드라이브 용량: 드라이브마다 "C:" + 사용량 막대 + "사용/전체" 숫자. 누르면 활성 칸에서 그 드라이브를 연다
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NewExplorer
{
    class DriveUsageBar
    {
        readonly MenuStrip menu;
        readonly ToolStripItem anchor;          // 이 항목 바로 뒤에 드라이브 칸을 넣음 (도움말 메뉴)
        readonly Action<string> open;
        readonly List<DriveUsageItem> items = new List<DriveUsageItem>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
        int busy;

        public DriveUsageBar(MenuStrip menu, ToolStripItem anchor, Action<string> open)
        {
            this.menu = menu; this.anchor = anchor; this.open = open;
            timer.Tick += delegate { Refresh(); };
            timer.Start();
            menu.SizeChanged += delegate { Fit(); };
        }

        struct Info { public string Root, Label; public long Total, Free; }

        /// <summary>용량 다시 읽기 (네트워크·CD 처럼 느린 드라이브가 화면을 멈추지 않게 백그라운드에서)</summary>
        public void Refresh()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var list = new List<Info>();
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                        try
                        {
                            if (!d.IsReady || d.TotalSize <= 0) continue;
                            list.Add(new Info { Root = d.Name, Label = d.VolumeLabel, Total = d.TotalSize, Free = d.TotalFreeSpace });
                        }
                        catch { }
                    }
                }
                catch { }
                try { if (menu.IsHandleCreated && !menu.IsDisposed) menu.BeginInvoke(new Action(() => Apply(list))); }
                catch { }
                Interlocked.Exchange(ref busy, 0);
            });
        }

        void Apply(List<Info> list)
        {
            bool same = list.Count == items.Count;
            for (int i = 0; same && i < list.Count; i++) same = items[i].Root == list[i].Root;
            if (!same)
            {
                menu.SuspendLayout();
                foreach (var it in items) { menu.Items.Remove(it); it.Dispose(); }
                items.Clear();
                int at = menu.Items.IndexOf(anchor) + 1;
                foreach (var d in list)
                {
                    var it = new DriveUsageItem(d.Root);
                    it.Click += delegate { open(it.Root); };
                    items.Add(it);
                    menu.Items.Insert(at++, it);
                }
                if (items.Count > 0) items[0].Margin = new Padding(24, 0, 0, 0);   // 메뉴와 조금 띄움
                menu.ResumeLayout();
            }
            for (int i = 0; i < list.Count; i++) items[i].SetUsage(list[i].Label, list[i].Total, list[i].Free);
            Fit();
        }

        /// <summary>칸 배치·버전 칸을 밀어내지 않게: 넘치면 숫자를 빼고(막대만), 그래도 넘치면 뒤 드라이브부터 숨김</summary>
        void Fit()
        {
            if (items.Count == 0) return;
            int room = menu.DisplayRectangle.Width - 8;
            foreach (ToolStripItem it in menu.Items)
                if (!(it is DriveUsageItem)) room -= it.GetPreferredSize(Size.Empty).Width + it.Margin.Horizontal;
            bool compact = Width(false) > room;
            int used = 0;
            menu.SuspendLayout();
            foreach (var it in items)
            {
                it.Compact = compact;
                used += it.GetPreferredSize(Size.Empty).Width + it.Margin.Horizontal;
                it.Available = used <= room;
            }
            menu.ResumeLayout();
        }

        int Width(bool compact)
        {
            int w = 0;
            foreach (var it in items) { bool c = it.Compact; it.Compact = compact; w += it.GetPreferredSize(Size.Empty).Width + it.Margin.Horizontal; it.Compact = c; }
            return w;
        }
    }

    class DriveUsageItem : ToolStripItem
    {
        public readonly string Root;
        long total, free;
        bool hot, compact;
        const int BarW = 56, BarH = 8, Pad = 6, Gap = 4;
        static readonly Font BoldFont = new Font(UI.Font, FontStyle.Bold);

        public DriveUsageItem(string root)
        {
            Root = root;
            Margin = new Padding(2, 0, 0, 0);
            AutoSize = true;
        }

        string Letter { get { return Root.TrimEnd('\\'); } }

        /// <summary>공간이 모자랄 때: 숫자 없이 글자+막대만 (숫자는 툴팁에)</summary>
        public bool Compact
        {
            get { return compact; }
            set { if (compact != value) { compact = value; Invalidate(); } }
        }

        public void SetUsage(string label, long total, long free)
        {
            bool resize = UsageText() != UsageText(total, free);
            this.total = total; this.free = free;
            long used = total - free;
            ToolTipText = Letter + " " + (string.IsNullOrEmpty(label) ? "로컬 디스크" : label)
                + "\n사용 " + Bytes(used) + " / 전체 " + Bytes(total) + " (" + Percent() + "%)"
                + "\n남은 공간 " + Bytes(free)
                + "\n누르면 활성 칸에서 엽니다";
            if (resize && Owner != null) Owner.PerformLayout();
            Invalidate();
        }

        int Percent() { return total <= 0 ? 0 : (int)Math.Round((total - free) * 100.0 / total); }

        string UsageText() { return UsageText(total, free); }

        /// <summary>"312/476GB", "1.2/3.6TB" — 전체 용량 기준 단위 하나로</summary>
        static string UsageText(long total, long free)
        {
            if (total <= 0) return "";
            double tb = 1024.0 * 1024 * 1024 * 1024, gb = 1024.0 * 1024 * 1024;
            long used = total - free;
            if (total >= tb) return (used / tb).ToString("0.0") + "/" + (total / tb).ToString("0.0") + "TB";
            return (used / gb).ToString("0") + "/" + (total / gb).ToString("0") + "GB";
        }

        static string Bytes(long n)
        {
            double tb = 1024.0 * 1024 * 1024 * 1024, gb = 1024.0 * 1024 * 1024, mb = 1024.0 * 1024;
            if (n >= tb) return (n / tb).ToString("0.00") + " TB";
            if (n >= gb) return (n / gb).ToString("0.0") + " GB";
            return (n / mb).ToString("0") + " MB";
        }

        public override Size GetPreferredSize(Size constrainingSize)
        {
            int w = Pad + TextRenderer.MeasureText(Letter, BoldFont, Size.Empty, TextFormatFlags.NoPadding).Width
                + Gap + BarW + Pad;
            if (!compact) w += Gap + 1 + TextRenderer.MeasureText(UsageText(), UI.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            return new Size(w, 22);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var T = Theme.Current;
            var g = e.Graphics;
            var r = new Rectangle(Point.Empty, Size);
            if (hot)
            {
                using (var b = new SolidBrush(T.Hover)) g.FillRectangle(b, r);
                using (var pen = new Pen(T.HoverBorder)) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            }
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine;
            int x = Pad;
            int lw = TextRenderer.MeasureText(Letter, BoldFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, Letter, BoldFont, new Rectangle(x, 0, lw + 2, r.Height), T.Text, flags);
            x += lw + Gap;

            // 막대: 바탕(테두리색) 위에 사용한 만큼 채움. 90% 넘으면 빨강 (윈도우 탐색기처럼)
            var bar = new Rectangle(x, (r.Height - BarH) / 2, BarW, BarH);
            using (var b = new SolidBrush(T.Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(222, 222, 222))) g.FillRectangle(b, bar);
            int pct = Percent();
            int fw = (int)Math.Round(BarW * pct / 100.0);
            if (fw > 0)
            {
                var fill = pct >= 90 ? Color.FromArgb(218, 38, 38) : T.Accent;
                using (var b = new SolidBrush(fill)) g.FillRectangle(b, bar.X, bar.Y, fw, BarH);
            }
            using (var pen = new Pen(T.Border)) g.DrawRectangle(pen, bar.X, bar.Y, BarW - 1, BarH - 1);
            if (compact) return;
            x += BarW + Gap + 1;

            TextRenderer.DrawText(g, UsageText(), UI.Font, new Rectangle(x, 0, r.Width - x, r.Height), T.SubText, flags);
        }
    }
}
