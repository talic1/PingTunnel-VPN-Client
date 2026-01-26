#define AppName "PingTunnel VPN Client"
#define AppPublisher "DrSaeedHub"
#define AppURL "https://github.com/DrSaeedHub/PingTunnel-VPN-Client"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef AppExeName
  #define AppExeName "PingTunnelVPN.exe"
#endif

#ifndef SourceDir
  #define SourceDir "..\\artifacts\\portable-selfcontained"
#endif

[Setup]
AppId={{7B9A8B0E-4E1F-4B2B-8F5D-59C3D3D00E6E}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=PingTunnelVPN-Setup-{#AppVersion}
SetupIconFile=..\icon\icon.ico
UninstallDisplayIcon={app}\Resources\icon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
