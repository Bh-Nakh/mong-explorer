// 몽탐색기 시작점
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NewExplorer
{
    static class Program
    {
        /// <summary>앱 버전 = 저장소 VERSION (build.bat 이 src\Version.cs 로 만들어 넣음)</summary>
        public static string Version { get { return AppVersion.Value; } }

        [STAThread]
        static void Main(string[] args)
        {
            // 업데이트 뒤 다시 실행: 예전 프로세스가 설정을 다 저장하고 끝날 때까지 기다림
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--wait-pid")
                {
                    int pid;
                    if (int.TryParse(args[i + 1], out pid))
                        try { Process.GetProcessById(pid).WaitForExit(15000); } catch { }
                }
            // 진단: 화면 없이 업데이트 확인만 하고 결과를 파일에 씀 (--check-update <결과 파일>)
            if (args.Length >= 2 && args[0] == "--check-update")
            {
                string r;
                try
                {
                    var info = Updater.Check();
                    r = "OK latest=" + info.Latest + " newer=" + info.Newer + " note=" + info.Note;
                }
                catch (Exception ex) { r = "ERROR " + ex.Message; }
                File.WriteAllText(args[1], "v" + Version + " " + r);
                return;
            }
            // 진단: 화면 없이 새 버전을 받아 exe 를 바꿈 (--update-now <결과 파일>, 다시 실행은 안 함)
            if (args.Length >= 2 && args[0] == "--update-now")
            {
                string r;
                try
                {
                    var info = Updater.Check();
                    r = info.Newer ? "REPLACED " + Updater.DownloadAndReplace() + " → v" + info.Latest : "SKIP 최신 버전 (v" + info.Latest + ")";
                }
                catch (Exception ex) { r = "ERROR " + ex.Message; }
                File.WriteAllText(args[1], "v" + Version + " " + r);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => LogError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);
            Settings.Load();
            Theme.Init(Settings.Current.Theme);
            Updater.Cleanup();
            ShellRegistry.Sync();
            MongWebCheck.WebCheck.Start("mongexplorer", "몽탐색기", new int[] { 47101, 47102, 47103 }, AppVersion.Value);   // 웹페이지 설치 확인용
            Application.Run(new MainForm(args));
        }

        public static void LogError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                File.AppendAllText(Path.Combine(Settings.AppDir, "몽탐색기.error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  v" + Version + "\r\n" + ex + "\r\n\r\n");
            }
            catch { }
        }
    }

    static class UI
    {
        public static readonly Font Font = new Font("Segoe UI", 9f);
    }

    static class AppIcon
    {
        public static Icon Load()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { return null; }
        }
    }
}
