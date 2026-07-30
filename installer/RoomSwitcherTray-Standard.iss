#define AppName "Room Switcher Tray"
#define AppExeName "RoomSwitcherTray.exe"
#define AppPublisher "sol669"
#define AppURL "https://github.com/sol669/Room-Switcher-Tray"

#ifndef AppVersion
  #define AppVersion "0.1.1"
#endif

#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{F8D6C9A8-E58A-4C3F-A5C9-85A5A86C7D31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppPublisher}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\release
OutputBaseFilename=Room-Switcher-Tray-Setup-v{#AppVersion}
UninstallDisplayIcon={app}\{#AppExeName}
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AppMutex=sol669.RoomSwitcherTray.Singleton
MinVersion=10.0.17763
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "https://aka.ms/dotnet/8.0/dotnet-runtime-win-x64.exe"; DestDir: "{tmp}"; DestName: "dotnet-runtime-8-win-x64.exe"; ExternalSize: 30000000; Flags: external download ignoreversion; Check: not IsDotNet8Installed
Source: "https://aka.ms/windowsappsdk/2.3/latest/windowsappruntimeinstall-x64.exe"; DestDir: "{tmp}"; DestName: "windowsappruntimeinstall-x64.exe"; ExternalSize: 120000000; Flags: external download ignoreversion; Check: not IsWindowsAppRuntimeInstalled

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\dotnet-runtime-8-win-x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Microsoft .NET 8 Runtime..."; Flags: waituntilterminated; Check: not IsDotNet8Installed
Filename: "{tmp}\windowsappruntimeinstall-x64.exe"; Parameters: "--quiet"; StatusMsg: "Installing Windows App Runtime..."; Flags: waituntilterminated; Check: not IsWindowsAppRuntimeInstalled
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet8Installed: Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{autopf}\dotnet\shared\Microsoft.NETCore.App\8.*'), FindRec);
  if Result then FindClose(FindRec);
end;

function IsWindowsAppRuntimeInstalled: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "if (Get-AppxPackage -Name ''Microsoft.WindowsAppRuntime.2.3'' -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;
