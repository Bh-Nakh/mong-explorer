// 셸 위치(경로·가상 폴더) 다루기: 이름, 줄임 표기, 아이콘, pidl 변환
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;

namespace NewExplorer
{
    public static class Shell
    {
        public const string ThisPC = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";

        public static IShellItem ItemFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            IShellItem item;
            int hr = Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref Native.IID_IShellItem, out item);
            // 빠른 실행(즐겨찾기) 같은 일부 가상 폴더는 ::{GUID} 로는 다시 해석되지 않고 shell:::{GUID} 로만 열림
            if (hr != 0 && path.StartsWith("::{"))
                hr = Native.SHCreateItemFromParsingName("shell:" + path, IntPtr.Zero, ref Native.IID_IShellItem, out item);
            return hr == 0 ? item : null;
        }

        public static IShellItem ItemFromPidl(IntPtr pidl)
        {
            IShellItem item;
            int hr = Native.SHCreateItemFromIDList(pidl, ref Native.IID_IShellItem, out item);
            return hr == 0 ? item : null;
        }

        /// <summary>pidl 생성 (호출한 쪽이 ILFree). 실패하면 IntPtr.Zero</summary>
        public static IntPtr PidlFromPath(string path)
        {
            IntPtr pidl; uint outAttr;
            int hr = Native.SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out outAttr);
            if (hr != 0 && path.StartsWith("::{"))   // 위 ItemFromPath 와 같은 이유
                hr = Native.SHParseDisplayName("shell:" + path, IntPtr.Zero, out pidl, 0, out outAttr);
            return hr == 0 ? pidl : IntPtr.Zero;
        }

        public static IntPtr PidlFromItem(IShellItem item)
        {
            IntPtr pidl;
            return Native.SHGetIDListFromObject(item, out pidl) == 0 ? pidl : IntPtr.Zero;
        }

        public static string Name(IShellItem item, uint sigdn)
        {
            if (item == null) return null;
            string s;
            return item.GetDisplayName(sigdn, out s) == 0 ? s : null;
        }

        /// <summary>위치 문자열(되돌려 열 수 있는 이름): 파일 시스템이면 경로, 아니면 ::{GUID} 형태</summary>
        public static string ParsingName(IShellItem item)
        {
            return Name(item, SIGDN.DESKTOPABSOLUTEPARSING);
        }

        public static string PathOfPidl(IntPtr pidl)
        {
            var item = ItemFromPidl(pidl);
            if (item == null) return null;
            try { return ParsingName(item); } finally { Marshal.ReleaseComObject(item); }
        }

        public static bool IsFileSystemPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith(@"\\")) return true;
            return path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0]);
        }

        public static string DisplayName(string path)
        {
            var item = ItemFromPath(path);
            if (item == null) return path;
            try { return Name(item, SIGDN.NORMALDISPLAY) ?? path; } finally { Marshal.ReleaseComObject(item); }
        }

        /// <summary>
        /// 줄임 표기: D:\작업\몽탐색기 → D:\작..\몽탐색기
        /// 드라이브 + 시작 폴더 앞부분 + .. + 끝 폴더
        /// </summary>
        public static string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (!IsFileSystemPath(path))
            {
                if (path.StartsWith("search-ms:", StringComparison.OrdinalIgnoreCase)) return "검색 결과";
                return DisplayName(path);
            }
            string root, rest;
            if (path.StartsWith(@"\\"))
            {
                // \\서버\공유\... → 루트 = \\서버\공유\
                var p = path.TrimEnd('\\').Substring(2).Split('\\');
                if (p.Length <= 2) return path.TrimEnd('\\');
                root = @"\\" + p[0] + @"\" + p[1] + @"\";
                rest = string.Join(@"\", p, 2, p.Length - 2);
            }
            else
            {
                root = path.Substring(0, 2) + @"\";
                rest = path.Length > 3 ? path.Substring(3).TrimEnd('\\') : "";
            }
            if (rest.Length == 0) return root;
            var parts = rest.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return root + parts[0];
            int n = Math.Max(1, Settings.Current.ShortChars);
            string first = parts[0].Length <= n ? parts[0] : parts[0].Substring(0, n);
            if (parts.Length == 2 && parts[0].Length <= n) return root + parts[0] + @"\" + parts[1];
            return root + first + @"..\" + parts[parts.Length - 1];
        }

        public static bool SamePath(string a, string b)
        {
            if (a == null || b == null) return false;
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        // ───── 아이콘 (작은 크기, 캐시) ─────
        static readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static Bitmap SmallIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            // 일반 폴더는 하나로 캐시 (드라이브·특수 폴더는 경로별)
            string key = path;
            bool plainDir = IsFileSystemPath(path) && path.TrimEnd('\\').Length > 2 && !path.StartsWith(@"\\");
            if (plainDir)
            {
                try
                {
                    // desktop.ini 가 있으면 고유 아이콘일 수 있음
                    if (!File.Exists(Path.Combine(path, "desktop.ini"))) key = "<folder>";
                }
                catch { key = "<folder>"; }
            }
            Bitmap bmp;
            if (iconCache.TryGetValue(key, out bmp)) return bmp;
            IntPtr pidl = PidlFromPath(path);
            if (pidl == IntPtr.Zero) return null;
            try
            {
                var info = new SHFILEINFO();
                Native.SHGetFileInfo(pidl, 0, ref info, Marshal.SizeOf(info), Native.SHGFI_PIDL | Native.SHGFI_ICON | Native.SHGFI_SMALLICON);
                if (info.hIcon == IntPtr.Zero) return null;
                using (var ic = Icon.FromHandle(info.hIcon)) bmp = ic.ToBitmap();
                Native.DestroyIcon(info.hIcon);
                iconCache[key] = bmp;
                return bmp;
            }
            finally { Native.ILFree(pidl); }
        }

        // ───── 윈도우 탐색기 '빠른 실행'(즐겨찾기) 고정 폴더 읽기·고정·해제 ─────
        // 윈도우 10·11 모두에서 쓰도록: 값 형식·언어·반영 지연에 기대지 않고, 여러 방법을 차례로 시도하며 진단 기록을 남긴다.
        const string QuickAccessId = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";

        /// <summary>진단 기록 (%TEMP%\mong-favorites.log). 즐겨찾기가 윈도우와 안 맞을 때 원인을 보려고 남김</summary>
        public static void FavLog(string s)
        {
            try
            {
                string p = Path.Combine(Path.GetTempPath(), "mong-favorites.log");
                var fi = new FileInfo(p);
                if (fi.Exists && fi.Length > 200000) fi.Delete();
                File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff ") + s + "\r\n");
            }
            catch { }
        }

        static object Com(object o, string name, BindingFlags f, params object[] args)
        {
            return o.GetType().InvokeMember(name, f, null, o, args == null || args.Length == 0 ? null : args);
        }

        static bool Truthy(object v)
        {
            if (v == null) return false;
            if (v is bool) return (bool)v;
            if (v is int) return (int)v != 0;
            if (v is short) return (short)v != 0;
            if (v is byte) return (byte)v != 0;
            if (v is long) return (long)v != 0;
            string t = v as string;
            if (t != null) { t = t.Trim().ToLowerInvariant(); return t == "true" || t == "1" || t == "-1" || t == "yes" || t == "예"; }
            return false;
        }

        static bool loggedFirstRead;
        static int lastPinCount = -1;

        /// <summary>윈도우 탐색기 '빠른 실행'에 고정된 폴더 위치 (파일 시스템이면 경로, 아니면 ::{GUID}). 읽지 못하면 빈 목록</summary>
        public static List<string> PinnedQuickAccess()
        {
            var list = new List<string>();
            var dump = new System.Text.StringBuilder();
            bool anyKnown = false;
            var all = new List<string>();
            object sh = null, ns = null, items = null;
            try
            {
                var t = Type.GetTypeFromProgID("Shell.Application");
                if (t == null) { FavLog("Shell.Application 을 만들 수 없음"); return list; }
                sh = Activator.CreateInstance(t);
                ns = Com(sh, "NameSpace", BindingFlags.InvokeMethod, QuickAccessId);
                if (ns == null) { FavLog("빠른 실행 폴더를 열 수 없음"); return list; }
                items = Com(ns, "Items", BindingFlags.InvokeMethod);
                int count = (int)Com(items, "Count", BindingFlags.GetProperty);
                for (int i = 0; i < count; i++)
                {
                    object it = null;
                    try
                    {
                        it = Com(items, "Item", BindingFlags.InvokeMethod, i);
                        object pinned = null;
                        try { pinned = Com(it, "ExtendedProperty", BindingFlags.InvokeMethod, "System.Home.IsPinned"); } catch { }
                        object isFolder = null;
                        try { isFolder = Com(it, "IsFolder", BindingFlags.GetProperty); } catch { }
                        string path = null;
                        try { path = Com(it, "Path", BindingFlags.GetProperty) as string; } catch { }
                        dump.Append("\r\n    [" + i + "] " + path + " | 고정=" + (pinned == null ? "(없음)" : pinned.GetType().Name + ":" + pinned) + " | 폴더=" + isFolder);
                        if (string.IsNullOrEmpty(path)) continue;
                        if (isFolder is bool && !(bool)isFolder) continue;
                        all.Add(path);
                        if (pinned != null) anyKnown = true;
                        if (Truthy(pinned)) list.Add(path);
                    }
                    catch (Exception ex) { dump.Append("\r\n    [" + i + "] 오류 " + ex.Message); }
                    finally { if (it != null) Marshal.ReleaseComObject(it); }
                }
            }
            catch (Exception ex) { FavLog("빠른 실행 읽기 오류: " + ex.Message); }
            finally
            {
                if (items != null) Marshal.ReleaseComObject(items);
                if (ns != null) Marshal.ReleaseComObject(ns);
                if (sh != null) Marshal.ReleaseComObject(sh);
            }
            // '고정됨' 값을 아예 못 읽는 윈도우면 빠른 실행의 폴더 항목을 모두 고정으로 간주
            if (!anyKnown && all.Count > 0) { list = all; dump.Append("\r\n    ('고정됨' 값을 읽지 못해 모든 항목을 고정으로 간주)"); }
            if (!loggedFirstRead || list.Count != lastPinCount)
            {
                FavLog((loggedFirstRead ? "고정 폴더 수 변경 " : "처음 읽음 (" + Environment.OSVersion.VersionString + ") ") + list.Count + "개" + dump);
                loggedFirstRead = true; lastPinCount = list.Count;
            }
            return list;
        }

        static bool PinnedContains(string path)
        {
            foreach (var p in PinnedQuickAccess()) if (SamePath(p, path)) return true;
            return false;
        }

        /// <summary>고정 상태가 present 가 될 때까지 최대 ms 동안 기다림 (윈도우가 반영하는 데 걸리는 시간)</summary>
        static bool WaitFor(string path, bool present, int ms)
        {
            int waited = 0;
            while (true)
            {
                if (PinnedContains(path) == present) return true;
                if (waited >= ms) return false;
                System.Threading.Thread.Sleep(150);
                waited += 150;
            }
        }

        /// <summary>폴더 자체에 셸 명령(pintohome / unpinfromhome)을 보냄. 보냈으면 true</summary>
        static bool InvokeFolderVerb(string path, string verb)
        {
            object sh = null, ns = null, self = null;
            try
            {
                var t = Type.GetTypeFromProgID("Shell.Application");
                if (t == null) return false;
                sh = Activator.CreateInstance(t);
                ns = Com(sh, "NameSpace", BindingFlags.InvokeMethod, path);
                if (ns == null) return false;
                self = Com(ns, "Self", BindingFlags.GetProperty);
                Com(self, "InvokeVerb", BindingFlags.InvokeMethod, verb);
                return true;
            }
            catch (Exception ex) { FavLog("명령 " + verb + " 오류: " + ex.Message); return false; }
            finally
            {
                if (self != null) Marshal.ReleaseComObject(self);
                if (ns != null) Marshal.ReleaseComObject(ns);
                if (sh != null) Marshal.ReleaseComObject(sh);
            }
        }

        /// <summary>빠른 실행 목록 안의 그 항목에 대해: 이름으로 고른 우클릭 메뉴(byName) 또는 지정한 명령을 보냄</summary>
        static bool InvokeQuickAccessItem(string path, string verb, bool byName)
        {
            object sh = null, ns = null, items = null;
            try
            {
                var t = Type.GetTypeFromProgID("Shell.Application");
                if (t == null) return false;
                sh = Activator.CreateInstance(t);
                ns = Com(sh, "NameSpace", BindingFlags.InvokeMethod, QuickAccessId);
                if (ns == null) return false;
                items = Com(ns, "Items", BindingFlags.InvokeMethod);
                int count = (int)Com(items, "Count", BindingFlags.GetProperty);
                for (int i = 0; i < count; i++)
                {
                    object it = null, verbs = null;
                    try
                    {
                        it = Com(items, "Item", BindingFlags.InvokeMethod, i);
                        string p = Com(it, "Path", BindingFlags.GetProperty) as string;
                        if (!SamePath(p, path)) continue;
                        if (!byName) { Com(it, "InvokeVerb", BindingFlags.InvokeMethod, verb); return true; }
                        // 메뉴 이름은 언어·윈도우 버전마다 달라 '제거/해제/Remove/Unpin' 이 들어간 것을 고름
                        verbs = Com(it, "Verbs", BindingFlags.InvokeMethod);
                        int vc = (int)Com(verbs, "Count", BindingFlags.GetProperty);
                        var names = new System.Text.StringBuilder();
                        object chosen = null;
                        for (int k = 0; k < vc; k++)
                        {
                            object v = Com(verbs, "Item", BindingFlags.InvokeMethod, k);
                            string name = ((Com(v, "Name", BindingFlags.GetProperty) as string) ?? "").Replace("&", "");
                            names.Append(name + " | ");
                            bool hit = chosen == null && (name.IndexOf("제거", StringComparison.Ordinal) >= 0 || name.IndexOf("해제", StringComparison.Ordinal) >= 0 ||
                                name.IndexOf("Unpin", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Remove from", StringComparison.OrdinalIgnoreCase) >= 0);
                            if (hit) chosen = v; else Marshal.ReleaseComObject(v);
                        }
                        FavLog("항목 메뉴 목록: " + names);
                        if (chosen == null) return false;
                        try { Com(chosen, "DoIt", BindingFlags.InvokeMethod); return true; }
                        finally { Marshal.ReleaseComObject(chosen); }
                    }
                    catch (Exception ex) { FavLog("항목 메뉴 오류: " + ex.Message); }
                    finally { if (verbs != null) Marshal.ReleaseComObject(verbs); if (it != null) Marshal.ReleaseComObject(it); }
                }
            }
            catch (Exception ex) { FavLog("빠른 실행 항목 오류: " + ex.Message); }
            finally
            {
                if (items != null) Marshal.ReleaseComObject(items);
                if (ns != null) Marshal.ReleaseComObject(ns);
                if (sh != null) Marshal.ReleaseComObject(sh);
            }
            return false;
        }

        /// <summary>폴더를 윈도우 빠른 실행에 고정 (탐색기 우클릭 '즐겨찾기에 고정'과 같음). 명령을 보냈으면 true (반영은 잠시 늦을 수 있음)</summary>
        public static bool PinQuickAccess(string path)
        {
            if (!IsFileSystemPath(path) || !System.IO.Directory.Exists(path)) { FavLog("고정 불가(폴더 아님): " + path); return false; }
            bool sent = InvokeFolderVerb(path, "pintohome");
            bool seen = sent && WaitFor(path, true, 1500);
            FavLog("고정 " + path + " → 명령=" + sent + ", 목록에 보임=" + seen);
            return sent;
        }

        /// <summary>윈도우 빠른 실행에서 이 폴더의 고정을 해제. 여러 방법을 차례로 시도해 실제로 사라졌으면 true</summary>
        public static bool UnpinQuickAccess(string path)
        {
            bool fs = IsFileSystemPath(path);
            for (int step = 0; step < 4; step++)
            {
                bool sent;
                if (step == 0) sent = InvokeQuickAccessItem(path, null, true);                    // 빠른 실행 항목의 우클릭 '제거' 메뉴 (윈도우 10 에서 확인된 방법)
                else if (step == 1) sent = InvokeQuickAccessItem(path, "removefromhome", false);  // 빠른 실행 항목에 '제거' 명령
                else if (step == 2) sent = InvokeQuickAccessItem(path, "unpinfromhome", false);   // 빠른 실행 항목에 '고정 해제' 명령 (윈도우 11 대비)
                else sent = fs && InvokeFolderVerb(path, "unpinfromhome");                        // 폴더 자체에 '고정 해제' 명령 (윈도우 11 대비)
                if (!sent) { FavLog("해제 방법 " + (step + 1) + ": 명령 못 보냄 " + path); continue; }
                bool gone = WaitFor(path, false, 1500);
                FavLog("해제 방법 " + (step + 1) + ": " + path + " → 사라짐=" + gone);
                if (gone) return true;
            }
            return !PinnedContains(path);
        }

        /// <summary>경로 아래 하위 폴더 (경로 표시줄 ▸ 메뉴용)</summary>
        public static List<string> SubFolders(string path)
        {
            var list = new List<string>();
            if (!IsFileSystemPath(path)) return list;
            try
            {
                bool showHidden = Settings.ExplorerShowsHidden();
                foreach (var d in new DirectoryInfo(path.EndsWith(@"\") ? path : path + @"\").GetDirectories())
                {
                    var a = d.Attributes;
                    if (!showHidden && (a & FileAttributes.Hidden) != 0) continue;
                    if ((a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0) continue;
                    list.Add(d.FullName);
                }
            }
            catch { }
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            return list;
        }

        public static string Parent(string path)
        {
            if (!IsFileSystemPath(path)) return null;
            var t = path.TrimEnd('\\');
            if (t.Length <= 2) return ThisPC;
            int i = t.LastIndexOf('\\');
            if (i < 0) return null;
            if (t.StartsWith(@"\\") && t.IndexOf('\\', 2) == i) return null;
            var p = t.Substring(0, i);
            return p.Length == 2 ? p + @"\" : p;
        }
    }
}
