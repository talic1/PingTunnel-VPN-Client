#define AppName "PingTunnel VPN Client"
#define AppPublisher "DrSaeedHub"
#define AppURL "https://github.com/DrSaeedHub/PingTunnel-VPN-Client"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef AppExeName
  #define AppExeName "PingTunnel - VPN Client.exe"
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

[Code]
function IsAppRunning(const AppName: string): Boolean;
var
  WbemLocator, WMIService, ProcessList: Variant;
begin
  Result := False;
  try
    WbemLocator := CreateOleObject('WbemScripting.SWbemLocator');
    WMIService := WbemLocator.ConnectServer('localhost', 'root\CIMV2');
    ProcessList := WMIService.ExecQuery('SELECT * FROM Win32_Process WHERE Name="' + AppName + '"');
    Result := ProcessList.Count > 0;
  except
    Result := False;
  end;
end;

function KillProcess(const ProcessName: string): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill', '/F /T /IM "' + ProcessName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500); // Wait for process to fully terminate
  Result := not IsAppRunning(ProcessName);
end;

function KillAllProcesses(): Boolean;
var
  Processes: array[0..2] of string;
  i: Integer;
  AllKilled: Boolean;
  RetryCount: Integer;
  FailedProcess: string;
begin
  Result := True;
  FailedProcess := '';
  
  Processes[0] := '{#AppExeName}';
  Processes[1] := 'pingtunnel.exe';
  Processes[2] := 'tun2socks.exe';
  
  for i := 0 to 2 do
  begin
    if IsAppRunning(Processes[i]) then
    begin
      RetryCount := 0;
      AllKilled := False;
      
      while (RetryCount < 3) and (not AllKilled) do
      begin
        AllKilled := KillProcess(Processes[i]);
        RetryCount := RetryCount + 1;
        if not AllKilled then
          Sleep(1000);
      end;
      
      if not AllKilled then
      begin
        FailedProcess := Processes[i];
        Result := False;
        Break;
      end;
    end;
  end;
  
  if not Result then
  begin
    MsgBox('Unable to close ' + FailedProcess + '.' + #13#10 + #13#10 + 
           'Please close PingTunnel VPN Client completely and try again.' + #13#10 + #13#10 +
           'If the application appears to be closed, try using Task Manager to end any remaining processes.',
           mbError, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := KillAllProcesses();
end;

function InitializeUninstall(): Boolean;
begin
  Result := KillAllProcesses();
end;

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{cmd}"; Parameters: "/C powershell -NoProfile -ExecutionPolicy Bypass -Command ""Start-Process -FilePath ''{app}\{#AppExeName}'' -WorkingDirectory ''{app}'' -Verb RunAs"""; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runhidden; StatusMsg: "Requesting administrator permission for {#AppName}..."
