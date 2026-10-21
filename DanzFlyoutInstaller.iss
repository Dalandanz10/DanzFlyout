
; DanzFlyout Installer
; Built with Inno Setup 6

#define MyAppName "DanzFlyout"
#define MyAppVersion "1.0.5"
#define MyAppPublisher "DanzFlyout"
#define MyAppExeName "DanzFlyout.exe"

[Setup]
AppId={{A7D3F5E1-7B8C-4F21-9D64-DANZFLYOUT1000}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}

DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

OutputDir=.
OutputBaseFilename=DanzFlyout_Setup_{#MyAppVersion}

Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest

CloseApplications=yes
CloseApplicationsFilter=*.exe

UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=Assets\DanzFlyout.ico

[Files]
; Application publish output
Source: "Publish_{#MyAppVersion}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
; Startup is managed by SettingsConfig.cs and AutoStartWithWindows.
; These entries are only removed during uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: binary; ValueName: "{#MyAppName}"; Flags: uninsdeletevalue

[Run]
; Allow the user to launch DanzFlyout after installation.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close DanzFlyout before uninstalling.
Filename: "{cmd}"; Parameters: "/C taskkill /F /IM ""{#MyAppExeName}"""; Flags: runhidden waituntilterminated; RunOnceId: "TerminateDanzFlyout"
