// 자동 업데이트 — 몽 시리즈 공통 모듈(MongUpdate.cs)로 공개 배포 저장소(Bh-Nakh/mong-downloads)의 Release 에서 받는다. 토큰이 필요 없다.
//   mongexplorer-latest.json 의 버전과 이 exe 버전을 비교 → 새 버전이면 몽탐색기.exe 를 받아(해시 확인) 자기 자신을 교체하고 다시 실행
//   (화면 흐름 — 버전 칸·메뉴·안내창·탭 복원 — 은 MainForm.Features.cs 가 그대로 담당한다)
using System;
using System.Diagnostics;
using System.IO;

namespace NewExplorer
{
    public class UpdateError : Exception
    {
        public UpdateError(string msg) : base(msg) { }
    }

    public class UpdateInfo
    {
        public string Latest;       // 공개 Release 의 최신 버전
        public string Note;         // 그 버전 변경 내용
        public bool Newer;
    }

    public static class Updater
    {
        public const string AppId = "mongexplorer";
        public const string AppFileName = "몽탐색기.exe";
        const string LegacyFileName = "NewExplorer.exe";   // 이 이름으로 돌던 옛 설치본은 업데이트하면서 새 이름으로 갈아탄다

        static MongUpdate.UpdateInfo pending;               // 마지막 확인에서 찾은 새 버전

        public static bool IsNewer(string remote, string local) { return MongUpdate.UpdateService.Compare(remote, local) > 0; }

        /// <summary>최신 버전 확인 (네트워크, 백그라운드 스레드에서 부를 것)</summary>
        public static UpdateInfo Check()
        {
            MongUpdate.UpdateInfo mi;
            if (!MongUpdate.UpdateService.TryCheck(AppId, Program.Version, out mi))
                throw new UpdateError(MongUpdate.UpdateService.LastError ?? "업데이트 정보를 읽지 못했습니다.");
            pending = mi;
            return new UpdateInfo
            {
                Latest = mi != null ? mi.Version : Program.Version,
                Note = mi != null ? mi.Note : null,
                Newer = mi != null
            };
        }

        /// <summary>새 exe 를 받아 자기 자신과 바꿈. 성공하면 다시 실행할 경로를 돌려줌</summary>
        public static string DownloadAndReplace()
        {
            if (pending == null) Check();
            if (pending == null) throw new UpdateError("받을 새 버전이 없습니다.");
            if (pending.Kind != "exe") throw new UpdateError("업데이트 정보의 종류가 맞지 않습니다 (" + pending.Kind + ").");

            string file;
            try { file = MongUpdate.UpdateService.Download(pending, null); }       // 해시까지 확인
            catch (Exception ex) { throw new UpdateError("새 버전을 받지 못했습니다: " + ex.Message); }
            using (var fs = File.OpenRead(file))
                if (fs.Length < 50000 || fs.ReadByte() != 'M' || fs.ReadByte() != 'Z')
                    throw new UpdateError("받은 파일이 실행 파일이 아닙니다 (" + fs.Length + "바이트).");

            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string dir = Path.GetDirectoryName(exe);
            // 옛 이름(NewExplorer.exe)으로 돌고 있으면 새 이름(몽탐색기.exe)으로 갈아탄다
            string target = string.Equals(Path.GetFileName(exe), LegacyFileName, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(dir, AppFileName) : exe;
            string fresh = Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + ".new");
            string old = Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + ".old");
            string targetOld = target + ".prev";
            try
            {
                File.Copy(file, fresh, true);
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);          // 실행 중인 exe 도 이름은 바꿀 수 있음
                if (!string.Equals(target, exe, StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                {
                    if (File.Exists(targetOld)) File.Delete(targetOld);
                    File.Move(target, targetOld);   // 새 이름 파일이 이미 있으면 옆으로 치움
                }
                File.Move(fresh, target);
            }
            catch (Exception ex)
            {
                try { if (!File.Exists(exe) && File.Exists(old)) File.Move(old, exe); } catch { }
                try { if (!File.Exists(target) && File.Exists(targetOld)) File.Move(targetOld, target); } catch { }
                try { if (File.Exists(fresh)) File.Delete(fresh); } catch { }
                throw new UpdateError("exe 를 바꾸지 못했습니다: " + ex.Message + "\n(exe 폴더에 쓰기 권한이 있는지 확인하세요)");
            }
            return target;
        }

        /// <summary>시작할 때: 지난 업데이트에서 남은 .old 지우기</summary>
        public static void Cleanup()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string old = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + ".old");
                if (File.Exists(old)) File.Delete(old);
                string dir = Path.GetDirectoryName(exe);
                foreach (var n in new[] { "NewExplorer.old", AppFileName + ".prev" })   // 이름을 갈아탄 업데이트가 남긴 것
                {
                    string p = Path.Combine(dir, n);
                    if (File.Exists(p)) File.Delete(p);
                }
            }
            catch { }
        }
    }
}
