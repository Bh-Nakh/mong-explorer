// 탐색기 우클릭 메뉴 등록: 폴더·드라이브·폴더 빈 곳을 우클릭 → "몽탐색기로 열기" (HKCU 라서 관리자 권한 불필요)
using System;
using System.IO;
using Microsoft.Win32;

namespace NewExplorer
{
    static class ShellRegistry
    {
        const string Name = "NewExplorer";
        const string Label = "몽탐색기로 열기";
        // 폴더 / 드라이브 / 폴더 안 빈 곳 (%V = 현재 폴더)
        static readonly string[][] Targets =
        {
            new[] { @"Software\Classes\Directory\shell\" + Name, "%1" },
            new[] { @"Software\Classes\Drive\shell\" + Name, "%1" },
            new[] { @"Software\Classes\Directory\Background\shell\" + Name, "%V" },
        };

        static string Exe { get { return System.Windows.Forms.Application.ExecutablePath; } }

        public static bool IsRegistered()
        {
            try
            {
                using (var m = Registry.CurrentUser.OpenSubKey(Targets[0][0]))
                    if (m == null || !string.Equals((string)m.GetValue(""), Label, StringComparison.Ordinal)) return false;   // 이름표가 옛 이름이면 다시 등록
                using (var k = Registry.CurrentUser.OpenSubKey(Targets[0][0] + @"\command"))
                    return k != null && string.Equals((string)k.GetValue(""), Command(Targets[0][1]), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static string Command(string arg) { return "\"" + Exe + "\" \"" + arg + "\""; }

        public static void Register()
        {
            foreach (var t in Targets)
                using (var k = Registry.CurrentUser.CreateSubKey(t[0]))
                {
                    k.SetValue("", Label);
                    k.SetValue("Icon", "\"" + Exe + "\",0");
                    using (var c = k.CreateSubKey("command")) c.SetValue("", Command(t[1]));
                }
        }

        public static void Unregister()
        {
            foreach (var t in Targets)
                try { Registry.CurrentUser.DeleteSubKeyTree(t[0], false); } catch { }
        }

        /// <summary>시작할 때 호출: 켜져 있으면 현재 exe 경로로 등록(옮겼거나 업데이트됐으면 갱신). 시험용 폴더(test)·시험 실행은 건너뜀</summary>
        public static void Sync()
        {
            try
            {
                if (Settings.NoSave || !Settings.Current.ShellMenu) return;
                if (string.Equals(new DirectoryInfo(Settings.AppDir).Name, "test", StringComparison.OrdinalIgnoreCase)) return;
                if (!IsRegistered()) Register();
            }
            catch (Exception ex) { Program.LogError(ex); }
        }
    }
}
