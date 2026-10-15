// 밝게 / 어둡게 테마: 색 모음, 메뉴 그리기, 윈도우 부품(탐색기 보기·트리·제목 줄) 다크 모드
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NewExplorer
{
    public class Theme
    {
        public bool Dark;
        public Color Window;        // 칸 사이·분할선
        public Color Panel;         // 메뉴·머리글
        public Color Content;       // 목록 바탕 (즐겨찾기, 주소 칸)
        public Color Text, SubText, DimText;
        public Color Border;
        public Color Hover, HoverBorder, Pressed;
        public Color Accent;
        public Color StripBack;     // 탭 줄 바탕
        public Color TabInactive, TabHover, TabActive;   // TabActive = 이동 줄(경로 표시줄 줄) 색
        public Color TabBorder, TabText, TabTextActive;
        public Color ActiveTopIdle; // 활성 칸이 아닐 때 고른 탭 위 띠
        public Color TabSel, TabSelIdle;   // 고른 탭 바탕 (활성 칸 / 비활성 칸) - 글씨는 흰색

        public static readonly Theme Light = new Theme
        {
            Dark = false,
            Window = Color.FromArgb(218, 223, 230),
            Panel = Color.FromArgb(243, 245, 248),
            Content = Color.White,
            Text = Color.FromArgb(20, 20, 20),
            SubText = Color.FromArgb(70, 70, 70),
            DimText = Color.FromArgb(150, 150, 150),
            Border = Color.FromArgb(200, 206, 215),
            Hover = Color.FromArgb(229, 243, 255),
            HoverBorder = Color.FromArgb(204, 232, 255),
            Pressed = Color.FromArgb(204, 228, 247),
            Accent = Color.FromArgb(0, 103, 192),
            StripBack = Color.FromArgb(206, 213, 223),
            TabInactive = Color.FromArgb(226, 231, 238),
            TabHover = Color.FromArgb(238, 242, 247),
            TabActive = Color.FromArgb(252, 252, 253),
            TabBorder = Color.FromArgb(168, 178, 192),
            TabText = Color.FromArgb(70, 76, 86),
            TabTextActive = Color.Black,
            ActiveTopIdle = Color.FromArgb(150, 160, 175),
            TabSel = Color.FromArgb(0, 103, 192),
            TabSelIdle = Color.FromArgb(92, 130, 175),
        };

        public static readonly Theme DarkTheme = new Theme
        {
            Dark = true,
            Window = Color.FromArgb(18, 18, 18),
            Panel = Color.FromArgb(38, 38, 38),
            Content = Color.FromArgb(25, 25, 25),
            Text = Color.FromArgb(238, 238, 238),
            SubText = Color.FromArgb(190, 190, 190),
            DimText = Color.FromArgb(120, 120, 120),
            Border = Color.FromArgb(70, 70, 70),
            Hover = Color.FromArgb(55, 60, 68),
            HoverBorder = Color.FromArgb(85, 95, 110),
            Pressed = Color.FromArgb(40, 70, 110),
            Accent = Color.FromArgb(76, 160, 255),
            StripBack = Color.FromArgb(22, 28, 38),
            TabInactive = Color.FromArgb(40, 48, 62),
            TabHover = Color.FromArgb(54, 64, 82),
            TabActive = Color.FromArgb(56, 56, 56),
            TabBorder = Color.FromArgb(78, 78, 78),
            TabText = Color.FromArgb(175, 175, 175),
            TabTextActive = Color.White,
            ActiveTopIdle = Color.FromArgb(110, 110, 110),
            TabSel = Color.FromArgb(30, 111, 207),
            TabSelIdle = Color.FromArgb(52, 78, 110),
        };

        public static Theme Current = Light;
        public static event Action Changed;

        /// <summary>설정 값(system/light/dark)으로 실제 테마 결정</summary>
        public static bool WantDark(string mode)
        {
            if (mode == "dark") return true;
            if (mode == "light") return false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>프로그램 시작 때 (창 만들기 전) 한 번</summary>
        public static void Init(string mode)
        {
            Current = WantDark(mode) ? DarkTheme : Light;
            SetAppMode(Current.Dark);
            ToolStripManager.Renderer = new ThemeRenderer();
        }

        public static void Set(string mode)
        {
            var t = WantDark(mode) ? DarkTheme : Light;
            bool changed = t != Current;
            Current = t;
            SetAppMode(t.Dark);
            ToolStripManager.Renderer = new ThemeRenderer();
            if (changed && Changed != null) Changed();
        }

        // ───── 윈도우 10 1809+ 다크 모드 (uxtheme 숨은 함수, 없으면 조용히 넘어감) ─────
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SetPreferredAppModeFn(int mode);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate bool AllowDarkModeForWindowFn(IntPtr hwnd, bool allow);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void VoidFn();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr h, IntPtr ordinal);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd, IntPtr rc, IntPtr rgn, uint flags);

        static IntPtr ux;
        static T Ord<T>(int n) where T : class
        {
            try
            {
                if (ux == IntPtr.Zero) ux = LoadLibrary("uxtheme.dll");
                if (ux == IntPtr.Zero) return null;
                var p = GetProcAddress(ux, (IntPtr)n);
                return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
            }
            catch { return null; }
        }

        static bool SupportsDark { get { var v = Environment.OSVersion.Version; return v.Major >= 10 && v.Build >= 17763; } }

        static void SetAppMode(bool dark)
        {
            if (!SupportsDark) return;
            try
            {
                var set = Ord<SetPreferredAppModeFn>(135);
                if (set != null) set(dark ? 2 /* ForceDark */ : 3 /* ForceLight */);
                var refresh = Ord<VoidFn>(104);       // RefreshImmersiveColorPolicyState
                if (refresh != null) refresh();
                var flush = Ord<VoidFn>(136);         // FlushMenuThemes
                if (flush != null) flush();
            }
            catch { }
        }

        /// <summary>창 하나(와 스크롤 막대)를 테마에 맞춤</summary>
        public static void ApplyToWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !SupportsDark) return;
            try
            {
                var allow = Ord<AllowDarkModeForWindowFn>(133);
                if (allow != null) allow(hwnd, Current.Dark);
                SetWindowTheme(hwnd, Current.Dark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch { }
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        delegate bool EnumProc(IntPtr hwnd, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);

        /// <summary>제목 줄 어둡게/밝게</summary>
        public static void ApplyTitleBar(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !SupportsDark) return;
            int v = Current.Dark ? 1 : 0;
            try
            {
                var allow = Ord<AllowDarkModeForWindowFn>(133);
                if (allow != null) allow(hwnd, Current.Dark);
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref v, 4);
                // 제목 줄은 활성 상태가 바뀔 때 다시 그려짐 → 지금 상태로 한 번 알림
                bool fg = GetForegroundWindow() == hwnd;
                SendMessage(hwnd, 0x0086 /* WM_NCACTIVATE */, (IntPtr)(fg ? 0 : 1), IntPtr.Zero);
                SendMessage(hwnd, 0x0086, (IntPtr)(fg ? 1 : 0), IntPtr.Zero);
                RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0400 | 0x0080 /* INVALIDATE|FRAME|ALLCHILDREN */);
            }
            catch { }
        }

        /// <summary>
        /// 안쪽 모든 창에 '테마 바뀜' 알림 (탐색기 보기가 다크/밝게 상태를 다시 읽게 함).
        /// 윈도우가 테마를 바꿀 때 보내는 것과 같은 WM_SETTINGCHANGE("ImmersiveColorSet") + WM_THEMECHANGED
        /// </summary>
        public static void BroadcastToChildren(IntPtr root)
        {
            if (root == IntPtr.Zero) return;
            IntPtr str = Marshal.StringToHGlobalUni("ImmersiveColorSet");
            try
            {
                EnumProc cb = (h, l) =>
                {
                    SendMessage(h, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, str);
                    SendMessage(h, 0x031A /* WM_THEMECHANGED */, IntPtr.Zero, IntPtr.Zero);
                    return true;
                };
                EnumChildWindows(root, cb, IntPtr.Zero);
                GC.KeepAlive(cb);
            }
            catch { }
            finally { Marshal.FreeHGlobal(str); }
        }
    }

    /// <summary>메뉴·우클릭 메뉴 색</summary>
    public class ThemeColors : ProfessionalColorTable
    {
        Theme T { get { return Theme.Current; } }
        public ThemeColors() { UseSystemColors = false; }
        public override Color MenuStripGradientBegin { get { return T.Panel; } }
        public override Color MenuStripGradientEnd { get { return T.Panel; } }
        public override Color ToolStripDropDownBackground { get { return T.Dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249); } }
        public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        public override Color MenuBorder { get { return T.Border; } }
        public override Color MenuItemBorder { get { return T.HoverBorder; } }
        public override Color MenuItemSelected { get { return T.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return T.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return T.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return T.Pressed; } }
        public override Color MenuItemPressedGradientMiddle { get { return T.Pressed; } }
        public override Color MenuItemPressedGradientEnd { get { return T.Pressed; } }
        public override Color SeparatorDark { get { return T.Border; } }
        public override Color SeparatorLight { get { return T.Border; } }
        public override Color ButtonSelectedHighlight { get { return T.Hover; } }
        public override Color ButtonSelectedGradientBegin { get { return T.Hover; } }
        public override Color ButtonSelectedGradientMiddle { get { return T.Hover; } }
        public override Color ButtonSelectedGradientEnd { get { return T.Hover; } }
        public override Color ButtonSelectedBorder { get { return T.HoverBorder; } }
        public override Color ButtonPressedGradientBegin { get { return T.Pressed; } }
        public override Color ButtonPressedGradientMiddle { get { return T.Pressed; } }
        public override Color ButtonPressedGradientEnd { get { return T.Pressed; } }
        public override Color ButtonCheckedGradientBegin { get { return T.Pressed; } }
        public override Color ButtonCheckedGradientMiddle { get { return T.Pressed; } }
        public override Color ButtonCheckedGradientEnd { get { return T.Pressed; } }
        public override Color CheckBackground { get { return T.Pressed; } }
        public override Color CheckSelectedBackground { get { return T.Pressed; } }
        public override Color CheckPressedBackground { get { return T.Pressed; } }
    }

    public class ThemeRenderer : ToolStripProfessionalRenderer
    {
        public ThemeRenderer() : base(new ThemeColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Theme.Current.DimText : e.Item is ToolStripLabel ? Theme.Current.SubText : Theme.Current.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Current.SubText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // 체크 표시를 테마 글자색으로
            var r = e.ImageRectangle;
            using (var b = new SolidBrush(Theme.Current.Pressed)) e.Graphics.FillRectangle(b, r.Left - 2, r.Top - 2, r.Width + 4, r.Height + 4);
            using (var pen = new Pen(Theme.Current.Text, 1.6f))
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new[] { new PointF(r.Left + 3, r.Top + r.Height / 2f), new PointF(r.Left + r.Width / 2.6f, r.Bottom - 4), new PointF(r.Right - 3, r.Top + 4) });
            }
        }
    }
}
