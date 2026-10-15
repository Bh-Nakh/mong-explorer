// 메인 창 추가 기능: 보기 방식 메뉴, 칸 위치 바꾸기, 테마, 업데이트
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NewExplorer
{
    public class ViewModeDef
    {
        public string Name;
        public int Key;      // Ctrl+Shift+숫자
        public int Mode;     // FOLDERVIEWMODE
        public int Size;     // 아이콘 크기
    }

    public partial class MainForm
    {
        // ───────── 보기 방식 (탐색기 '보기' 탭과 같음) ─────────
        static readonly ViewModeDef[] ViewModes =
        {
            new ViewModeDef { Name = "아주 큰 아이콘", Key = 1, Mode = Native.FVM_ICON, Size = 256 },
            new ViewModeDef { Name = "큰 아이콘", Key = 2, Mode = Native.FVM_ICON, Size = 96 },
            new ViewModeDef { Name = "보통 아이콘", Key = 3, Mode = Native.FVM_ICON, Size = 48 },
            new ViewModeDef { Name = "작은 아이콘", Key = 4, Mode = Native.FVM_SMALLICON, Size = 16 },
            new ViewModeDef { Name = "목록", Key = 5, Mode = Native.FVM_LIST, Size = 16 },
            new ViewModeDef { Name = "자세히", Key = 6, Mode = Native.FVM_DETAILS, Size = 16 },
            new ViewModeDef { Name = "타일", Key = 7, Mode = Native.FVM_TILE, Size = 48 },
            new ViewModeDef { Name = "내용", Key = 8, Mode = Native.FVM_CONTENT, Size = 32 },
        };

        void SetViewMode(PaneControl p, ViewModeDef v)
        {
            var h = p == null ? null : p.ActiveHost;
            if (h != null) h.SetViewMode(v.Mode, v.Size);
        }

        /// <summary>지금 보기가 ViewModes 중 몇 번째인지 (아이콘 크기는 가장 가까운 단계)</summary>
        static int CurrentViewModeIndex(ExplorerHost h)
        {
            if (h == null) return -1;
            var fv = h.GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return -1;
            int mode, size;
            try { if (fv.GetViewModeAndIconSize(out mode, out size) != 0) return -1; }
            finally { Marshal.ReleaseComObject(fv); }
            if (mode == Native.FVM_THUMBNAIL) mode = Native.FVM_ICON;
            if (mode == Native.FVM_ICON)
            {
                if (size >= 160) return 0;
                if (size >= 72) return 1;
                if (size >= 32) return 2;
                return 3;
            }
            for (int i = 3; i < ViewModes.Length; i++) if (ViewModes[i].Mode == mode) return i;
            return -1;
        }

        void UpdateViewModeChecks()
        {
            int cur = CurrentViewModeIndex(Active.ActiveHost);
            for (int i = 0; i < viewModeMenus.Count; i++) viewModeMenus[i].Checked = i == cur;
        }

        // ───────── 칸 위치 바꾸기 ─────────
        /// <summary>배치에 따른 칸 위치 이름</summary>
        string PositionName(int index)
        {
            switch (Settings.Current.Layout)
            {
                case "1x2": return index == 0 ? "왼쪽 칸" : "오른쪽 칸";
                case "2x1": return index == 0 ? "위 칸" : "아래 칸";
                case "2x2": return new[] { "왼쪽 위 칸", "오른쪽 위 칸", "왼쪽 아래 칸", "오른쪽 아래 칸" }[index];
                default: return "칸";
            }
        }

        void FillSwapMenu(ToolStripMenuItem parent, PaneControl pane)
        {
            parent.DropDownItems.Clear();
            for (int i = 0; i < VisibleCount(); i++)
            {
                if (panes[i] == pane) continue;
                var target = panes[i];
                parent.DropDownItems.Add(Item(PositionName(i) + "과(와) 바꾸기", null, () => SwapPanes(pane, target)));
            }
            parent.Enabled = parent.DropDownItems.Count > 0;
            parent.ToolTipText = "탭 줄의 빈 곳을 잡고 다른 칸으로 끌어도 바뀝니다";
        }

        void UpdateSwapMenu()
        {
            if (miSwap != null) FillSwapMenu(miSwap, Active);
        }

        /// <summary>두 칸의 화면 위치를 맞바꿈 (탭·기록·보기는 그대로)</summary>
        void SwapPanes(PaneControl a, PaneControl b)
        {
            if (a == null || b == null || a == b) return;
            int ia = Array.IndexOf(panes, a), ib = Array.IndexOf(panes, b);
            if (ia < 0 || ib < 0) return;
            panes[ia] = b; panes[ib] = a;
            b.Index = ia; a.Index = ib;
            ApplyLayout(Settings.Current.Layout);
            if (active != null) Settings.Current.ActivePane = active.Index;
            SetActive(a);
            Settings.Save();
        }

        // ───────── 테마 ─────────
        void SetTheme(string mode)
        {
            Settings.Current.Theme = mode;
            foreach (var mi in themeMenus) mi.Checked = (string)mi.Tag == mode;
            Settings.Save();
            Theme.Set(mode);
        }

        void OnThemeChanged()
        {
            ApplyThemeColors();
            // 윈도우 부품(탐색기 보기·트리)은 새로 만들어야 테마가 바뀜
            tree.Rebuild();
            foreach (var p in panes)
                foreach (var t in p.Tabs)
                    if (t.Host.IsHandleCreated) t.Host.Recreate();
            Theme.BroadcastToChildren(Handle);
            // 보기 창이 비동기로 만들어지므로 조금 뒤 한 번 더
            var later = new System.Windows.Forms.Timer { Interval = 400 };
            later.Tick += delegate { later.Stop(); later.Dispose(); Theme.BroadcastToChildren(Handle); };
            later.Start();
        }

        void ApplyThemeColors()
        {
            var T = Theme.Current;
            BackColor = T.Window;
            menu.BackColor = T.Panel;
            menu.ForeColor = T.Text;
            mainSplit.BackColor = leftSplit.BackColor = grid.BackColor = T.Window;
            leftSplit.Panel1.BackColor = leftSplit.Panel2.BackColor = T.Content;
            foreach (var sc in new[] { splitA, splitB, splitC }) if (sc != null) sc.BackColor = T.Window;
            foreach (var h in headers)
            {
                h.BackColor = T.Panel;
                foreach (Control c in h.Controls) { c.ForeColor = T.SubText; c.BackColor = Color.Transparent; }
                h.Invalidate();
            }
            foreach (var p in panes) p.ApplyTheme();
            favs.ApplyTheme();
            foreach (var kv in layoutButtons) kv.Value.Image = LayoutIcon(kv.Key, kv.Key == Settings.Current.Layout);
            foreach (var kv in layoutMenus) kv.Value.Image = LayoutIcon(kv.Key, false);
            UpdateVersionButton();
            menu.Invalidate();
            if (IsHandleCreated) Theme.ApplyTitleBar(Handle);
        }

        // ───────── 업데이트 ─────────
        void UpdateVersionButton()
        {
            if (versionButton == null) return;
            bool newer = updateInfo != null && updateInfo.Newer;
            versionButton.Text = newer ? "v" + Program.Version + " → v" + updateInfo.Latest + " 업데이트" : "v" + Program.Version;
            versionButton.BackColor = newer ? Color.FromArgb(46, 160, 67) : Color.Transparent;
            versionButton.ForeColor = newer ? Color.White : Theme.Current.SubText;
            versionButton.Font = newer ? new Font(UI.Font, FontStyle.Bold) : UI.Font;
            versionButton.ToolTipText = newer
                ? "새 버전 v" + updateInfo.Latest + (string.IsNullOrEmpty(updateInfo.Note) ? "" : "\n" + updateInfo.Note) + "\n눌러서 업데이트"
                : "몽탐색기 v" + Program.Version + " — 눌러서 업데이트 확인";
        }

        /// <summary>업데이트 확인. manual = 사용자가 누름 (결과를 항상 알려 줌, 새 버전이면 바로 물어봄)</summary>
        void CheckUpdate(bool manual)
        {
            if (updateBusy) return;
            if (manual && updateInfo != null && updateInfo.Newer) { AskUpdate(); return; }
            updateBusy = true;
            if (manual) { versionButton.Text = "확인 중…"; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null; string err = null;
                try { info = Updater.Check(); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        updateBusy = false;
                        if (info != null) updateInfo = info;
                        UpdateVersionButton();
                        if (!manual) return;
                        if (err != null) MessageBox.Show(this, "업데이트를 확인하지 못했습니다.\n\n" + err, "몽탐색기 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        else if (info.Newer) AskUpdate();
                        else MessageBox.Show(this, "최신 버전입니다. (v" + Program.Version + ")", "몽탐색기 업데이트");
                    }));
                }
                catch { }
            });
        }

        void AskUpdate()
        {
            var info = updateInfo;
            if (info == null || !info.Newer) return;
            var msg = "새 버전이 있습니다.\n\n지금: v" + Program.Version + "\n새 버전: v" + info.Latest +
                      (string.IsNullOrEmpty(info.Note) ? "" : "\n\n" + info.Note) +
                      "\n\n지금 업데이트할까요? (받은 뒤 몽탐색기가 다시 시작되고, 열린 탭은 그대로 복원됩니다)";
            if (MessageBox.Show(this, msg, "몽탐색기 업데이트", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            updateBusy = true;
            versionButton.Text = "받는 중…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string exe = null, err = null;
                try { exe = Updater.DownloadAndReplace(); }
                catch (Exception ex) { err = ex.Message; }
                BeginInvoke(new Action(() =>
                {
                    updateBusy = false;
                    if (err != null)
                    {
                        UpdateVersionButton();
                        MessageBox.Show(this, "업데이트하지 못했습니다.\n\n" + err, "몽탐색기 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    // 새 exe 를 실행 (이 프로세스가 설정을 저장하고 끝날 때까지 기다리게 함)
                    try { Process.Start(exe, "--wait-pid " + Process.GetCurrentProcess().Id); }
                    catch (Exception ex) { MessageBox.Show(this, "새 버전을 받았지만 다시 실행하지 못했습니다. 직접 실행해 주세요.\n" + ex.Message, "몽탐색기 업데이트"); }
                    Close();
                }));
            });
        }
    }
}
