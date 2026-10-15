; 몽탐색기 설치 스크립트 (Inno Setup 6). release.bat 이 /DSrcDir=<몽탐색기.exe 가 있는 폴더> /DAppVersion=<VERSION 파일 내용> 으로 실행한다.
#ifndef SrcDir
  #define SrcDir "..\release"
#endif
#ifndef AppVersion
  #define AppVersion "0.0"
#endif
#define AppName "몽탐색기"
#define AppExe "몽탐색기.exe"
#define ProtoId "mongexplorer"

[Setup]
AppId={{9D3E5C71-2B8A-4F06-A4D9-71C0E2B63A58}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=NoAd
; 설정이 exe 옆에 저장되고 자체 업데이트가 exe 를 덮어쓰므로 쓰기 가능한 사용자 폴더(%LOCALAPPDATA%\Programs)에만 설치한다.
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\setup
OutputBaseFilename=몽탐색기-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0.0
VersionInfoProductName={#AppName}
VersionInfoCompany=NoAd

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SrcDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; 홈페이지(설치 확인 페이지)의 [실행] 버튼이 쓰는 프로토콜. 제거할 때 함께 지운다.
Root: HKA; Subkey: "Software\Classes\{#ProtoId}"; ValueType: string; ValueName: ""; ValueData: "URL:{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProtoId}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\{#ProtoId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"""

[InstallDelete]
; 이름이 NewExplorer 였던 옛 실행 파일이 같은 폴더에 남아 있으면 정리한다.
Type: files; Name: "{app}\NewExplorer.exe"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// 우클릭 메뉴("몽탐색기로 열기")는 앱이 시작할 때 스스로 등록한다. 제거할 때 그 항목도 함께 지운다.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\NewExplorer');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Drive\shell\NewExplorer');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\Background\shell\NewExplorer');
  end;
end;
