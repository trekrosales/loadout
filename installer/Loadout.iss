; Loadout installer script.
; Requires Inno Setup 6.3 or later (needed for the x64compatible setting).
; Build it with build\publish.ps1, or open this file in the Inno Setup Compiler.
; The install is per user: no administrator password, except for the optional firewall rule.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
; This ID must never change. It is how Windows recognizes Loadout across versions.
AppId={{8E7C7F75-4A67-4D16-8C41-686AABEE4F62}
AppName=Loadout
AppVersion={#AppVersion}
AppPublisher=trekrosales
AppPublisherURL=https://github.com/trekrosales/loadout
DefaultDirName={localappdata}\Programs\Loadout
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=Loadout-{#AppVersion}-setup
SetupIconFile=..\src\Loadout\Assets\loadout.ico
UninstallDisplayIcon={app}\Loadout.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Setup asks the user to close Loadout if it is running. The app creates the mutex
; "Local\Loadout.6b1f0c2e.Instance" (see src\Loadout\Core\SingleInstance.cs), so the same name is used here.
AppMutex=Local\Loadout.6b1f0c2e.Instance
CloseApplications=yes
; Don't start Loadout again automatically after an update. The final page has its own "Launch Loadout" option.
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start Loadout when Windows starts"; GroupDescription: "Startup:"; Flags: checkedonce
Name: "firewall"; Description: "Block Loadout from the internet with a Windows Firewall rule (asks for administrator permission)"; GroupDescription: "Extra protection:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\Loadout.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\Loadout"; Filename: "{app}\Loadout.exe"
Name: "{userdesktop}\Loadout"; Filename: "{app}\Loadout.exe"; Tasks: desktopicon

[Registry]
; Start with Windows. The value format must match IStartupRegistration: the exe path in double quotes.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Loadout"; ValueData: """{app}\Loadout.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
; Final page: a checkbox to start Loadout right away.
Filename: "{app}\Loadout.exe"; Description: "Launch Loadout"; Flags: nowait postinstall skipifsilent

[Code]
const
  FirewallRuleName = 'Loadout - block internet';

// True if the installer added the firewall rule. The flag is written by CurStepChanged below.
function FirewallWasAdded(): Boolean;
var
  Flag: string;
begin
  Result := RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Loadout', 'FirewallRuleAdded', Flag) and (Flag = '1');
end;

// During install: if the user ticked the firewall option, add the rule with administrator permission.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Added: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('firewall') then
    begin
      Added := ShellExec('runas', ExpandConstant('{sys}\netsh.exe'),
        'advfirewall firewall add rule name="' + FirewallRuleName + '" dir=out action=block program="' +
        ExpandConstant('{app}\Loadout.exe') + '" enable=yes',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if Added and (ResultCode = 0) then
      begin
        RegWriteStringValue(HKEY_CURRENT_USER, 'Software\Loadout', 'FirewallRuleAdded', '1');
      end
      else
      begin
        MsgBox('The firewall rule was not added (administrator permission was declined or failed). ' +
          'Loadout is installed and works normally. You can run setup again to try again.',
          mbInformation, MB_OK);
      end;
    end;
  end;
end;

// During uninstall: remove the firewall rule, if the installer added it. Then, after uninstalling,
// ask whether to keep the user's settings. Silent uninstalls keep them.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Removed: Boolean;
  KeepSettings: Boolean;
begin
  if CurUninstallStep = usUninstall then
  begin
    if FirewallWasAdded() then
    begin
      Removed := ShellExec('runas', ExpandConstant('{sys}\netsh.exe'),
        'advfirewall firewall delete rule name="' + FirewallRuleName + '"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if not (Removed and (ResultCode = 0)) then
      begin
        MsgBox('Loadout could not remove its firewall rule. To remove it yourself, open Command Prompt ' +
          'as administrator and run this command:' + #13#10 + #13#10 +
          'netsh advfirewall firewall delete rule name="' + FirewallRuleName + '"',
          mbError, MB_OK);
      end;
    end;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    KeepSettings := True;
    if not UninstallSilent then
    begin
      KeepSettings := MsgBox(
        'Keep your Loadout settings (your shortcuts and preferences)?' + #13#10 + #13#10 +
        'Yes: keep them. They will still be there if you install Loadout again.' + #13#10 +
        'No: delete them permanently.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES;
    end;
    if not KeepSettings then
    begin
      // Settings and shortcuts (roaming folder), then the icon cache and log (local folder).
      DelTree(ExpandConstant('{userappdata}\Loadout'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\Loadout'), True, True, True);
    end;
    // Our own small registry flag is no longer needed.
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Loadout');
  end;
end;
