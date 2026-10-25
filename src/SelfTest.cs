// --selftest <로그 파일>: 마우스·포커스 없이 주요 동작을 차례로 실행하고 결과를 기록 (개발 시험용)
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NewExplorer
{
    public partial class MainForm
    {
        string selfTestLog;

        void Log(string s) { File.AppendAllText(selfTestLog, DateTime.Now.ToString("HH:mm:ss.fff ") + s + "\r\n"); }

        void Shot(string name)
        {
            try
            {
                var b = Bounds;
                using (var bmp = new System.Drawing.Bitmap(b.Width, b.Height))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, System.Drawing.Point.Empty, b.Size);
                    bmp.Save(Path.Combine(Path.GetDirectoryName(selfTestLog), name + ".png"));
                }
                Log("화면 저장 " + name);
            }
            catch (Exception ex) { Log("화면 저장 실패 " + ex.Message); }
        }

        void Check(string what, bool ok, string detail) { Log((ok ? "OK   " : "FAIL ") + what + (detail != null ? "  [" + detail + "]" : "")); }

        int ViewMode(ExplorerHost h)
        {
            var fv = h.GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return -99;
            try { int m, s; fv.GetViewModeAndIconSize(out m, out s); return m * 1000 + s; }
            finally { Marshal.ReleaseComObject(fv); }
        }

        string SelectedNames(ExplorerHost h)
        {
            var names = new List<string>();
            var fv = h.GetView<IFolderView2>(Native.IID_IFolderView2);
            if (fv == null) return "";
            try
            {
                IShellItemArray arr;
                if (fv.GetSelection(false, out arr) != 0 || arr == null) return "";
                uint cnt; arr.GetCount(out cnt);
                for (uint k = 0; k < cnt; k++)
                {
                    IShellItem it;
                    if (arr.GetItemAt(k, out it) == 0) { names.Add(Shell.Name(it, SIGDN.PARENTRELATIVEPARSING)); Marshal.ReleaseComObject(it); }
                }
                Marshal.ReleaseComObject(arr);
            }
            finally { Marshal.ReleaseComObject(fv); }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", names);
        }

        bool cutTest;

        // --cuttest <로그>: 트리에서 폴더 선택 → 잘라내기 → 흐림(고스트) 표시 → 붙여넣기를 시험 (마우스 없이)
        void StartCutTest()
        {
            string root = @"C:\mong_cuttest";
            string a = Path.Combine(root, "A_cut"), b = Path.Combine(root, "B_dest"), c = Path.Combine(root, "C_other");
            var steps = new List<Action>();
            steps.Add(() =>
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
                Directory.CreateDirectory(a); Directory.CreateDirectory(b); Directory.CreateDirectory(c);
                File.WriteAllText(Path.Combine(a, "x.txt"), "x");
                ApplyLayout("1"); SetActive(panes[0]); panes[0].Navigate(root);
            });
            steps.Add(() => { });
            steps.Add(() => { tree.SelectPath(a); });
            steps.Add(() => { });
            steps.Add(() => { });
            steps.Add(() => { });
            steps.Add(() =>
            {
                Check("트리에서 폴더 선택 → 칸 이동", Shell.SamePath(panes[0].ActivePath, a), panes[0].ActivePath);
                Log("   포커스 창=" + Native.ClassName(Native.GetFocus()) + " 트리 안=" + tree.IsTreeWindow(Native.GetFocus()));
                bool ok = tree.HandleKey(Keys.X, true, false);
                Check("트리 Ctrl+X 처리", ok, null);
            });
            steps.Add(() => { });
            steps.Add(() => { Check("트리 잘라내기 → 흐림 표시", tree.DebugCutMarked(a), null); Clipboard.SetText("다른 곳에서 복사"); });
            steps.Add(() => { });
            steps.Add(() => { Check("다른 곳에서 복사하면(폴더 안 잘라내기 포함) 트리 흐림 해제", !tree.DebugCutMarked(a), null); tree.HandleKey(Keys.X, true, false); });
            steps.Add(() => { });
            steps.Add(() =>
            {
                Check("다시 잘라내기 → 흐림", tree.DebugCutMarked(a), null);
                var m = new Message { HWnd = tree.TreeWindow, Msg = Native.WM_KEYDOWN, WParam = (IntPtr)(int)Keys.Escape, LParam = IntPtr.Zero };
                PreFilterMessage(ref m);      // 실제 키 처리 경로로 ESC
            });
            steps.Add(() => { });
            steps.Add(() => { Check("ESC → 흐림 해제", !tree.DebugCutMarked(a), null); Check("ESC → 클립보드 비움", !Clipboard.ContainsFileDropList(), null); tree.HandleKey(Keys.X, true, false); });
            steps.Add(() => { });
            steps.Add(() =>
            {
                var item = Shell.ItemFromPath(a);
                uint at = 0; item.GetAttributes(0x00008000 /* SFGAO_GHOSTED */, out at);
                Check("잘라낸 폴더가 흐림(SFGAO_GHOSTED)", (at & 0x8000) != 0, "attr=" + at.ToString("X"));
                bool hasEffect = false; int eff = 0;
                try { var d = Clipboard.GetDataObject(); var ms = d.GetData("Preferred DropEffect") as MemoryStream; if (ms != null) { hasEffect = true; eff = BitConverter.ToInt32(ms.ToArray(), 0); } } catch { }
                Check("클립보드 이동 표시", hasEffect && eff == 2, "effect=" + eff);
                Check("클립보드 폴더 목록", Clipboard.ContainsFileDropList(), null);
                Shot("c1_cut");
                tree.SelectPath(b);
            });
            steps.Add(() => { });
            steps.Add(() => { tree.HandleKey(Keys.V, true, false); });
            steps.Add(() => { });
            steps.Add(() =>
            {
                Check("붙여넣기로 이동됨", Directory.Exists(Path.Combine(b, "A_cut", "")) && !Directory.Exists(a) && File.Exists(Path.Combine(b, "A_cut", "x.txt")),
                    "선택=" + string.Join("|", tree.SelectedPaths()) + " 원본 있음=" + Directory.Exists(a) + " 대상 있음=" + Directory.Exists(Path.Combine(b, "A_cut")));
                Shot("c2_pasted");
            });
            steps.Add(() => { Log("끝"); });
            int i = 0;
            var timer = new Timer { Interval = 900 };
            timer.Tick += delegate
            {
                if (i >= steps.Count) { timer.Stop(); return; }
                try { steps[i++](); } catch (Exception ex) { Log("ERROR step " + i + ": " + ex.Message); }
            };
            timer.Start();
        }

        string newFolderTestVerb;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr parent, EnumChildProc proc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        struct CMICI
        {
            public int cbSize, fMask; public IntPtr hwnd, lpVerb, lpParameters, lpDirectory; public int nShow, dwHotKey; public IntPtr hIcon;
            public IntPtr lpTitle, lpVerbW, lpParametersW, lpDirectoryW, lpTitleW; public int x, y;
        }

        [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IContextMenuById
        {
            [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
            [PreserveSig] int InvokeCommand(ref CMICI pici);
            [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        }

        [ComImport, Guid("FC4801A3-2BA9-11CF-A229-00AA003D7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IObjectWithSiteT
        {
            void SetSite([MarshalAs(UnmanagedType.IUnknown)] object site);
            void GetSite(ref Guid riid, out IntPtr site);
        }

        [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr hmenu);
        [DllImport("user32.dll")] static extern uint GetMenuItemID(IntPtr hmenu, int pos);

        /// <summary>실제 우클릭처럼 빈 공간 메뉴를 만들고, 동작 이름이 verb 인 항목을 메뉴 번호로 실행. 결과 설명</summary>
        string InvokeBackgroundItem(ExplorerHost h, string verb)
        {
            var sv = h.GetView<IShellView>(Native.IID_IShellView);
            if (sv == null) return "보기 없음";
            IntPtr pcm = IntPtr.Zero, hmenu = IntPtr.Zero, buf = Marshal.AllocHGlobal(1024);
            try
            {
                Guid iid = Native.IID_IContextMenu;
                if (sv.GetItemObject(Native.SVGIO_BACKGROUND, ref iid, out pcm) != 0 || pcm == IntPtr.Zero) return "메뉴 없음";
                var cm = (IContextMenuById)Marshal.GetObjectForIUnknown(pcm);
                var site = cm as IObjectWithSiteT;           // 실제 보기처럼 메뉴에 보기를 사이트로 연결
                if (site != null) site.SetSite(sv);
                hmenu = Native.CreatePopupMenu();
                cm.QueryContextMenu(hmenu, 0, 1, 0x7FFF, 0);
                int n = GetMenuItemCount(hmenu);
                for (int i = 0; i < n; i++)
                {
                    uint id = GetMenuItemID(hmenu, i);
                    if (id == 0xFFFFFFFF || id == 0) continue;
                    Marshal.WriteInt16(buf, 0, 0);
                    if (cm.GetCommandString((UIntPtr)(id - 1), 4 /* GCS_VERBW */, IntPtr.Zero, buf, 512) != 0) continue;
                    if (Marshal.PtrToStringUni(buf) != verb) continue;
                    IntPtr dirA = Marshal.StringToHGlobalAnsi(h.CurrentPath), dirW = Marshal.StringToHGlobalUni(h.CurrentPath);
                    var ici = new CMICI
                    {
                        cbSize = Marshal.SizeOf(typeof(CMICI)), fMask = 0x4000 /* CMIC_MASK_UNICODE */, hwnd = Handle,
                        lpVerb = (IntPtr)(id - 1), lpVerbW = (IntPtr)(id - 1), lpDirectory = dirA, lpDirectoryW = dirW, nShow = 1
                    };
                    int hr = cm.InvokeCommand(ref ici);
                    Marshal.FreeHGlobal(dirA); Marshal.FreeHGlobal(dirW);
                    return "메뉴 " + (i + 1) + "번째 항목 실행 hr=0x" + hr.ToString("X");
                }
                return "메뉴에 '" + verb + "' 없음 (항목 " + n + "개)";
            }
            catch (Exception ex) { return "오류 " + ex.Message; }
            finally
            {
                Marshal.FreeHGlobal(buf);
                if (hmenu != IntPtr.Zero) Native.DestroyMenu(hmenu);
                if (pcm != IntPtr.Zero) Marshal.Release(pcm);
                Marshal.ReleaseComObject(sv);
            }
        }

        static int ExplorerWindowCount()
        {
            int n = 0;
            for (IntPtr h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "CabinetWClass", null); h != IntPtr.Zero; h = FindWindowEx(IntPtr.Zero, h, "CabinetWClass", null)) n++;
            return n;
        }

        // --newfoldertest <로그> <동작 이름>: 빈 공간 우클릭 메뉴의 동작(몽집 [새 폴더 만들기])을 실행해
        // 윈도우 탐색기 창이 새로 뜨지 않고 이 창 안에서 새 폴더가 이름 바꾸기 상태가 되는지 시험
        void StartNewFolderTest()
        {
            string tdir = @"C:\mong_nftest";
            int before = 0;
            var steps = new List<Action>();
            steps.Add(() =>
            {
                if (Directory.Exists(tdir)) Directory.Delete(tdir, true);
                Directory.CreateDirectory(tdir);
                File.WriteAllText(Path.Combine(tdir, "a.txt"), "a");
                ApplyLayout("1"); SetActive(panes[0]); panes[0].AddTab(tdir, true);
            });
            steps.Add(() => { });
            steps.Add(() => { });
            steps.Add(() =>
            {
                before = ExplorerWindowCount();
                Log("탐색기 창 수(전) = " + before + ", 동작 = " + newFolderTestVerb + ", 보는 폴더 = " + panes[0].ActivePath);
                if (!Shell.SamePath(panes[0].ActivePath, tdir)) { Log("FAIL 시험 폴더로 이동하지 못함 → 동작 실행 안 함"); return; }
                // 입력기를 전각으로 바꿔 둠 → 이름 바꾸기 칸이 반각으로 돌아오는지 확인
                Log("전각으로 = " + Native.SetFullShape(panes[0].ActiveHost.ViewWindow()) + ", 상태 = " + Native.IsFullShape(panes[0].ActiveHost.ViewWindow()));
                if (newFolderTestVerb == "ctrlshiftn") { panes[0].FocusView(); panes[0].ActiveHost.CreateNewFolder(); Log("CreateNewFolder 실행"); }
                else Log("우클릭 메뉴 항목 실행 = " + InvokeBackgroundItem(panes[0].ActiveHost, newFolderTestVerb));
            });
            steps.Add(() => { });
            steps.Add(() => { });
            steps.Add(() =>
            {
                int after = ExplorerWindowCount();
                Check("새 폴더가 만들어짐", Directory.Exists(Path.Combine(tdir, "새 폴더")), null);
                Check("윈도우 탐색기 창이 새로 뜨지 않음", after <= before, "전 " + before + " / 후 " + after);
                Check("몽탐색기 안에서 새 폴더 선택", SelectedNames(panes[0].ActiveHost) == "새 폴더", SelectedNames(panes[0].ActiveHost));
                IntPtr edit = IntPtr.Zero;
                EnumChildWindows(panes[0].ActiveHost.ViewWindow(), (h, l) => { if (Native.ClassName(h) == "Edit" && IsWindowVisible(h)) edit = h; return edit == IntPtr.Zero; }, IntPtr.Zero);
                Check("이름 바꾸기 상태", edit != IntPtr.Zero, null);
                if (edit != IntPtr.Zero) { var fs = Native.IsFullShape(edit); Check("이름 바꾸기 칸은 반각", fs == false, fs == null ? "입력기 없음" : fs.ToString()); }
                Shot("n1_newfolder");
            });
            steps.Add(() => { Log("끝"); });
            int i = 0;
            var timer = new Timer { Interval = 900 };
            timer.Tick += delegate
            {
                if (i >= steps.Count) { timer.Stop(); return; }
                try { steps[i++](); } catch (Exception ex) { Log("ERROR step " + i + ": " + ex.Message); }
            };
            timer.Start();
        }

        bool viewTest;

        // --viewtest <로그>: 폴더별 보기 설정 기억 시험 (A 폴더를 큰 아이콘·수정한 이름 내림차순으로 → B 폴더 → 다시 A)
        void StartViewTest()
        {
            string root = @"C:\mong_viewtest" + DateTime.Now.ToString("HHmmss");
            string a = Path.Combine(root, "A"), b = Path.Combine(root, "B");
            int origDir = 0;
            var steps = new List<Action>();
            steps.Add(() =>
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
                Directory.CreateDirectory(a); Directory.CreateDirectory(b);
                File.WriteAllText(Path.Combine(a, "x.txt"), "x"); File.WriteAllText(Path.Combine(b, "y.txt"), "y");
                ApplyLayout("1"); SetActive(panes[0]); panes[0].Navigate(a);
            });
            steps.Add(() => { });
            steps.Add(() => { });
            steps.Add(() =>
            {
                var h = panes[0].ActiveHost;
                Log("A 처음 보기 = " + ViewMode(h));
                var fv = h.GetView<IFolderView2>(Native.IID_IFolderView2);
                var cur = h.CaptureTemplate(); origDir = cur.Sort[0].Width;
                foreach (var c in cur.Sort) Log("현재 정렬 " + c.Fmtid + "/" + c.Pid + " 방향 " + c.Width);
                var cols = new[] { new SORTCOLUMN { propkey = new PROPERTYKEY(new Guid(cur.Sort[0].Fmtid), cur.Sort[0].Pid), direction = -cur.Sort[0].Width } };
                Log("정렬 지정 hr=" + fv.SetSortColumns(cols, 1).ToString("X"));
                Marshal.ReleaseComObject(fv);
                h.SetViewMode(Native.FVM_ICON, 96);
            });
            steps.Add(() => { }); steps.Add(() => { }); steps.Add(() => { });
            steps.Add(() =>
            {
                Check("A 변경이 폴더 설정으로 저장됨", Settings.Current.FolderViews.Count == 1, Settings.Current.FolderViews.Count + "개");
                panes[0].Navigate(b);
            });
            steps.Add(() => { }); steps.Add(() => { });
            steps.Add(() =>
            {
                int m = ViewMode(panes[0].ActiveHost);
                Check("B 는 A 의 설정을 따라가지 않음", m / 1000 != Native.FVM_ICON, m.ToString());
                Check("B 는 건드리지 않아 저장 안 됨", Settings.Current.FolderViews.Count == 1, Settings.Current.FolderViews.Count + "개");
                panes[0].Navigate(a);
            });
            steps.Add(() => { }); steps.Add(() => { });
            steps.Add(() =>
            {
                var h = panes[0].ActiveHost;
                int m = ViewMode(h);
                var t = h.CaptureTemplate();
                bool sortOk = t != null && t.Sort.Count > 0 && t.Sort[0].Width == -origDir;
                Check("A 다시 열면 큰 아이콘 복원", m == Native.FVM_ICON * 1000 + 96, m.ToString());
                Check("A 다시 열면 이름 내림차순 복원", sortOk, t == null ? "null" : (t.Sort.Count > 0 ? t.Sort[0].Pid + "/" + t.Sort[0].Width : "정렬 없음"));
                Shot("v1_restored");
            });
            steps.Add(() => { Log("끝"); });
            int i = 0;
            var timer = new Timer { Interval = 900 };
            timer.Tick += delegate
            {
                if (i >= steps.Count) { timer.Stop(); return; }
                try { steps[i++](); } catch (Exception ex) { Log("ERROR step " + i + ": " + ex.Message); }
            };
            timer.Start();
        }

        void StartSelfTest()
        {
            if (cutTest) { StartCutTest(); return; }
            if (viewTest) { StartViewTest(); return; }
            if (newFolderTestVerb != null) { StartNewFolderTest(); return; }

            var steps = new List<Action>();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            // 트리에서 사용자가 폴더를 고름 → 활성 칸의 활성 탭이 그 폴더로 (다른 칸·다른 탭은 그대로)
            TabItem tab0 = null; string p1Before = null;
            steps.Add(() => { ApplyLayout("1x2"); SetActive(panes[0]); tab0 = panes[0].ActiveTab; });
            steps.Add(() => { p1Before = panes[1].ActivePath; tree.DebugSelect(@"C:\"); });
            steps.Add(() => { });
            steps.Add(() =>
            {
                Check("트리 선택 → 칸1 활성 탭 이동", Shell.SamePath(panes[0].ActivePath, @"C:\") && panes[0].ActiveTab == tab0, panes[0].ActivePath);
                Check("다른 칸은 그대로", Shell.SamePath(panes[1].ActivePath, p1Before), panes[1].ActivePath);
                SetActive(panes[1]);
            });
            steps.Add(() => { });
            steps.Add(() => { Check("칸 바꿔도 이동 없음 (트리만 따라감)", Shell.SamePath(panes[0].ActivePath, @"C:\") && Shell.SamePath(panes[1].ActivePath, p1Before), panes[0].ActivePath + " / " + panes[1].ActivePath + " / 트리=" + tree.SelectedNow()); tree.DebugSelect(@"D:\"); });
            steps.Add(() => { });
            steps.Add(() => { Check("트리 선택 → 칸2 활성 탭 이동", Shell.SamePath(panes[1].ActivePath, @"D:\") && Shell.SamePath(panes[0].ActivePath, @"C:\"), panes[1].ActivePath); });
            steps.Add(() => { ApplyLayout("2x2");Check("2x2 배치", splitA != null && splitB != null && splitC != null && panes[3].Parent != parking, null); });
            steps.Add(() => { SetActive(panes[1]); panes[1].Navigate(win); });
            steps.Add(() => { Check("칸2 이동", Shell.SamePath(panes[1].ActivePath, win), panes[1].ActivePath); panes[1].Back(); });
            steps.Add(() => { Check("뒤로", !Shell.SamePath(panes[1].ActivePath, win), panes[1].ActivePath); panes[1].Forward(); });
            steps.Add(() => { Check("앞으로", Shell.SamePath(panes[1].ActivePath, win), panes[1].ActivePath); panes[1].Up(); });
            steps.Add(() => { Check("위로", Shell.SamePath(panes[1].ActivePath, Path.GetPathRoot(win)), panes[1].ActivePath); OnTreePicked(users, false); });
            steps.Add(() => { Check("트리 선택 → 활성 칸 이동", Shell.SamePath(panes[1].ActivePath, users), panes[1].ActivePath); OnTreePicked(win, true); });
            steps.Add(() => { Check("트리 가운데 클릭 → 새 탭", panes[1].Tabs.Count >= 2 && Shell.SamePath(panes[1].Tabs[panes[1].ActiveIndex + 1].Host.CurrentPath, win), panes[1].Tabs.Count + "개"); panes[2].AddTab(users, true); });
            steps.Add(() =>
            {
                int before2 = panes[2].Tabs.Count, before3 = panes[3].Tabs.Count;
                var tab = panes[2].ActiveTab;
                panes[2].GiveTabTo(tab, panes[3], 0);
                Check("탭을 다른 칸으로", panes[2].Tabs.Count == before2 - 1 && panes[3].Tabs.Count == before3 + 1 && panes[3].Tabs[0] == tab, before2 + "→" + panes[2].Tabs.Count + ", " + before3 + "→" + panes[3].Tabs.Count);
            });
            steps.Add(() => { SetActive(panes[3]); Check("활성 칸 → 트리", true, panes[3].ActivePath); });
            steps.Add(() => { panes[0].Navigate(win); });
            steps.Add(() =>
            {
                panes[0].ActiveHost.SetViewMode(Native.FVM_DETAILS, 16);
                var d = panes[0].ActiveHost.CaptureTemplate();
                Check("자세히 보기 설정 읽기", d != null && d.Columns.Count > 0 && d.Sort.Count > 0, d == null ? "null" : "cols " + d.Columns.Count + " sort " + d.Sort.Count + " group " + d.GroupFmtid + "/" + d.GroupPid);
                Log("   " + ExplorerHost.LastCaptureInfo);
                if (d != null) foreach (var c in d.Columns) Log("   열 " + c.Fmtid + "/" + c.Pid + " 너비 " + c.Width);
            });
            steps.Add(() => { panes[0].ActiveHost.SetViewMode(Native.FVM_ICON, 96); });
            steps.Add(() =>
            {
                Log("칸1 보기 = " + ViewMode(panes[0].ActiveHost) + ", 칸2 보기(전) = " + ViewMode(panes[1].ActiveHost));
                ApplyToAll(panes[0].ActiveHost);
                var t = Settings.Current.Template;
                Check("보기 설정 읽기", t != null && t.ViewMode == Native.FVM_ICON, t == null ? "null" : "mode " + t.ViewMode + " size " + t.IconSize + " cols " + t.Columns.Count + " sort " + t.Sort.Count);
            });
            steps.Add(() =>
            {
                Check("모든 폴더에 적용 (다른 칸)", ViewMode(panes[1].ActiveHost) / 1000 == Native.FVM_ICON, ViewMode(panes[1].ActiveHost).ToString());
                panes[1].Navigate(win);
            });
            steps.Add(() => { Check("적용 후 새로 연 폴더", ViewMode(panes[1].ActiveHost) / 1000 == Native.FVM_ICON, ViewMode(panes[1].ActiveHost).ToString()); TogglePreview(); });
            steps.Add(() => { Check("미리 보기 켬", Settings.Current.PreviewPane, null); });
            steps.Add(() => { });
            steps.Add(() => { Shot("t1_light"); });
            // 보기 방식 8가지
            for (int k = 0; k < ViewModes.Length; k++)
            {
                int kk = k;
                steps.Add(() => { SetActive(panes[1]); SetViewMode(panes[1], ViewModes[kk]); });
                steps.Add(() => { int got = CurrentViewModeIndex(panes[1].ActiveHost); Check("보기: " + ViewModes[kk].Name, got == kk, "읽은 값 " + got + " / " + ViewMode(panes[1].ActiveHost)); });
            }
            steps.Add(() => { SetViewMode(panes[1], ViewModes[5]); });
            // 칸 위치 바꾸기
            PaneControl pa = null, pb = null; string pathA = null, pathB = null;
            steps.Add(() =>
            {
                pa = panes[0]; pb = panes[3];
                pathA = pa.ActivePath; pathB = pb.ActivePath;
                SwapPanes(pa, pb);
                Check("칸 위치 바꾸기", panes[0] == pb && panes[3] == pa && pa.Index == 3 && pb.Index == 0 && Shell.SamePath(panes[0].ActivePath, pathB) && Shell.SamePath(panes[3].ActivePath, pathA),
                    "왼쪽 위=" + panes[0].ActivePath + ", 오른쪽 아래=" + panes[3].ActivePath);
                Check("바꾼 뒤 배치 유지", splitB.Panel1.Contains(panes[0]) && splitC.Panel2.Contains(panes[3]), null);
            });
            steps.Add(() => { Shot("t2_swapped"); SwapPanes(panes[0], panes[3]); });
            steps.Add(() => { ApplyLayout("1x2"); SwapPanes(panes[0], panes[1]); Check("1x2 칸 바꾸기", panes[0].Index == 0 && panes[1].Index == 1 && panes[0].Parent == splitA.Panel1, null); });
            steps.Add(() => { SwapPanes(panes[0], panes[1]); ApplyLayout("2x2"); });
            // 다크 모드
            steps.Add(() => { SetTheme("dark"); Check("다크 모드", Theme.Current.Dark, null); });
            steps.Add(() => { });
            steps.Add(() => { });
            steps.Add(() => { Shot("t3_dark"); });
            steps.Add(() => { SetTheme("light"); Check("밝게", !Theme.Current.Dark, null); });
            steps.Add(() => { });
            steps.Add(() => { });
            steps.Add(() => { Shot("t4_light_again"); });
            // 새 폴더(Ctrl+Shift+N) · 붙여넣은 항목 선택 (시험 폴더는 로그 옆)
            string tdir = Path.Combine(Path.GetDirectoryName(selfTestLog), "paste_test");
            steps.Add(() =>
            {
                if (Directory.Exists(tdir)) Directory.Delete(tdir, true);
                Directory.CreateDirectory(tdir);
                File.WriteAllText(Path.Combine(tdir, "a.txt"), "a");
                File.WriteAllText(Path.Combine(tdir, "b.txt"), "b");
                SetActive(panes[0]); panes[0].Navigate(tdir);
            });
            steps.Add(() => { panes[0].ActiveHost.CreateNewFolder(); });
            steps.Add(() =>
            {
                Check("새 폴더 만들기 + 선택", Directory.Exists(Path.Combine(tdir, "새 폴더")) && SelectedNames(panes[0].ActiveHost) == "새 폴더", SelectedNames(panes[0].ActiveHost));
                Shot("t5_newfolder");
                panes[0].ActiveHost.RefreshView();   // 이름 바꾸기 상태 끝내기
            });
            steps.Add(() =>
            {
                panes[0].ActiveHost.SelectPastedSoon(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "c.txt", "d.txt" });
                File.WriteAllText(Path.Combine(tdir, "c.txt"), "c");
                File.WriteAllText(Path.Combine(tdir, "d.txt"), "d");
            });
            steps.Add(() => { });
            steps.Add(() =>
            {
                Check("붙여넣은 항목 선택", SelectedNames(panes[0].ActiveHost) == "c.txt,d.txt", SelectedNames(panes[0].ActiveHost));
                Shot("t6_pasted");
            });
            // 검색: 몽탐색기 안에서 하위 폴더까지 찾아 결과 목록을 보여 주는지
            steps.Add(() =>
            {
                Directory.CreateDirectory(Path.Combine(tdir, "깊은", "더깊은"));
                File.WriteAllText(Path.Combine(tdir, "깊은", "더깊은", "my_needle.txt"), "n");
                File.WriteAllText(Path.Combine(tdir, "깊은", "Needle2.doc"), "n");
                panes[0].RunSearch("needle");
            });
            steps.Add(() => { });
            steps.Add(() =>
            {
                var sv = panes[0].DebugSearchView;
                Check("검색: 결과 보기가 뜸", sv != null && panes[0].SearchActive, null);
                Check("검색: 하위 폴더까지 2개 찾음", sv != null && sv.Finished && sv.Count == 2, sv == null ? "null" : sv.Count + "개, 끝=" + sv.Finished);
                Shot("t7_search");
                panes[0].RunSearch("*.txt");
            });
            steps.Add(() => { });
            steps.Add(() =>
            {
                var sv = panes[0].DebugSearchView;
                Check("검색: 와일드카드 *.txt (a,b,c,d,my_needle = 5)", sv != null && sv.Finished && sv.Count == 5, sv == null ? "null" : sv.Count + "개");
                panes[0].Back();
            });
            steps.Add(() => { Check("검색: 뒤로 → 결과 보기 닫힘", !panes[0].SearchActive, null); Shot("t8_after_search"); });
            steps.Add(() => { ApplyLayout("1"); SetActive(panes[0]); panes[0].Navigate(win); });
            steps.Add(() => { panes[0].RunSearch("notepad"); });
            steps.Add(() => { });
            steps.Add(() => { var sv = panes[0].DebugSearchView; Check("검색: Windows 폴더에서 notepad", sv != null && sv.Count > 0, sv == null ? "null" : sv.Count + "개 / " + sv.FirstName); });
            steps.Add(() => { bool ok = panes[0].ActiveHost.Navigate("::{679F85CB-0220-4080-B29B-5540CC05AAB6}"); Check("즐겨찾기(빠른 실행) 이동", ok, panes[0].ActivePath); });
            steps.Add(() => { });
            steps.Add(() =>
            {
                var pins = Shell.PinnedQuickAccess();
                Check("윈도우 빠른 실행 고정 폴더 읽기", pins.Count > 0, pins.Count + "개, 첫 항목 " + (pins.Count > 0 ? pins[0] : "-"));
                Check("즐겨찾기 칸 = 윈도우 고정 폴더", favs.Items.Count == pins.Count, "칸 " + favs.Items.Count + " / 윈도우 " + pins.Count);
            });
            // 즐겨찾기 = 윈도우 빠른 실행: 시험용 임시 폴더를 추가했다가 삭제 (사용자의 기존 고정 폴더는 건드리지 않음)
            string pinDir = Path.Combine(Path.GetDirectoryName(selfTestLog), "pin_test");
            steps.Add(() => { Directory.CreateDirectory(pinDir); favs.AddFolder(pinDir); });
            steps.Add(() => { });
            steps.Add(() =>
            {
                bool inWin = Shell.PinnedQuickAccess().Exists(x => Shell.SamePath(x, pinDir));
                bool inList = false; foreach (string x in favs.Items) if (Shell.SamePath(x, pinDir)) inList = true;
                Check("즐겨찾기 추가 → 윈도우 빠른 실행에 고정", inWin, null);
                Check("즐겨찾기 칸에 보임", inList, null);
                favs.RemoveFolder(pinDir);
            });
            steps.Add(() => { });
            steps.Add(() =>
            {
                bool inWin = Shell.PinnedQuickAccess().Exists(x => Shell.SamePath(x, pinDir));
                bool inList = false; foreach (string x in favs.Items) if (Shell.SamePath(x, pinDir)) inList = true;
                Check("즐겨찾기 삭제 → 윈도우 고정 해제", !inWin && !inList, "윈도우=" + inWin + " 칸=" + inList);
            });
            steps.Add(() => { SetTheme("dark"); });
            steps.Add(() => { Log("화면 확인 대기"); });
            steps.Add(() => { Log("끝"); });

            int i = 0;
            var timer = new Timer { Interval = 900 };
            timer.Tick += delegate
            {
                if (i >= steps.Count) { timer.Stop(); return; }
                try { steps[i++](); } catch (Exception ex) { Log("ERROR step " + i + ": " + ex.Message); }
            };
            timer.Start();
        }
    }
}
