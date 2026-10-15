// 설정 저장/불러오기: exe 옆 몽탐색기.settings.json (예전 이름 NewExplorer.settings.json 도 읽음) (exe 폴더째 옮기면 설정도 함께)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace NewExplorer
{
    public class TabState
    {
        public string Path;
        public List<string> Back = new List<string>();
        public List<string> Forward = new List<string>();
    }

    public class PaneState
    {
        public List<TabState> Tabs = new List<TabState>();
        public int Active;
    }

    public class ColumnSpec
    {
        public string Fmtid;
        public uint Pid;
        public int Width;
    }

    /// <summary>탭 우클릭 → '모든 폴더에 적용'으로 저장되는 보기 설정</summary>
    public class ViewTemplate
    {
        public int ViewMode;
        public int IconSize;
        public List<ColumnSpec> Columns = new List<ColumnSpec>();
        public List<ColumnSpec> Sort = new List<ColumnSpec>();   // Width = 방향 (1 오름, -1 내림)
        public string GroupFmtid;
        public uint GroupPid;
        public bool GroupAscending = true;
    }

    public class Settings
    {
        public string Layout = "1x2";          // 1, 1x2, 2x1, 2x2
        public int ActivePane;
        public List<PaneState> Panes = new List<PaneState>();
        public List<string> Favorites = new List<string>();
        public bool TreeDrivesOnly = true;
        public bool PreviewPane;
        public bool DetailsPane;
        public int ShortChars = 1;            // 줄임 표기에서 시작 폴더 글자 수
        public ViewTemplate Template;
        public Dictionary<string, ViewTemplate> FolderViews = new Dictionary<string, ViewTemplate>();   // 폴더별 마지막 보기 설정 (키: 소문자 경로)
        public int WinX = -1, WinY = -1, WinW = 1400, WinH = 850;
        public bool WinMax;
        public int LeftWidth = 260;
        public double TreeRatio = 0.62;
        public double SplitV = 0.5, SplitH = 0.5, SplitV2 = 0.5;
        public string Theme = "system";       // system, light, dark
        public bool ShellMenu = true;          // 폴더 우클릭 메뉴에 "몽탐색기로 열기" 자동 등록
        public string SkipUpdateVersion;      // '이 버전 건너뛰기'

        public static Settings Current = new Settings();

        /// <summary>exe 가 있는 폴더</summary>
        public static string AppDir { get { return Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath); } }
        public static string FilePath { get { return Path.Combine(AppDir, "몽탐색기.settings.json"); } }
        static string LegacyFilePath { get { return Path.Combine(AppDir, "NewExplorer.settings.json"); } }
        static string OldFilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"NewExplorer\settings.json"); } }

        /// <summary>저장 실패 (exe 폴더에 쓸 수 없음). 처음 실패 때 한 번 안내</summary>
        public static bool SaveFailed;
        public static event Action SaveFailedFirst;

        public static void Load()
        {
            try
            {
                // exe 옆에 설정이 없으면 예전 위치(%APPDATA%)의 설정을 가져옴 (다음 저장부터 exe 옆)
                string src = File.Exists(FilePath) ? FilePath : File.Exists(LegacyFilePath) ? LegacyFilePath : File.Exists(OldFilePath) ? OldFilePath : null;
                if (src != null)
                {
                    var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(src, Encoding.UTF8));
                    if (s != null) Current = s;
                }
            }
            catch { }
            if (Current.Panes == null) Current.Panes = new List<PaneState>();
            if (Current.Favorites == null) Current.Favorites = new List<string>();
            if (Current.FolderViews == null) Current.FolderViews = new Dictionary<string, ViewTemplate>();
        }

        public static bool NoSave;   // 시험 실행
        public static Action BeforeSave;   // 저장 직전에 현재 화면 상태를 Current 에 반영

        public static void Save()
        {
            if (NoSave) return;
            try
            {
                if (BeforeSave != null) BeforeSave();
                var json = new JavaScriptSerializer().Serialize(Current);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
                SaveFailed = false;
            }
            catch
            {
                bool first = !SaveFailed;
                SaveFailed = true;
                if (first && SaveFailedFirst != null) SaveFailedFirst();
            }
        }

        /// <summary>윈도우 탐색기 설정 '숨긴 항목 표시'</summary>
        public static bool ExplorerShowsHidden()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    var v = k == null ? null : k.GetValue("Hidden");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }
    }
}
