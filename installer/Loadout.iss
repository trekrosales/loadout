; Loadout installer script (Inno Setup 6).
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
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
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
; Remembers that the firewall rule was added, so the uninstaller knows to remove it.
Root: HKCU; Subkey: "Software\Loadout"; ValueType: string; ValueName: "FirewallRuleAdded"; ValueData: "1"; Tasks: firewall; Flags: uninsdeletekey

[Run]
; Firewall rule: blocks Loadout from sending anything out to the internet. Windows asks for administrator permission.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Loadout - block internet"" dir=out action=block program=""{app}\Loadout.exe"" enable=yes"; Tasks: firewall; StatusMsg: "Adding the firewall rule..."; Verb: "runas"; Flags: runhidden waituntilterminated shellexec
; Final page: a checkbox to start Loadout right away.
Filename: "{app}\Loadout.exe"; Description: "Launch Loadout"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Removes the firewall rule, but only if the installer added it (see FirewallRuleWasAdded below).
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Loadout - block internet"""; Check: FirewallRuleWasAdded; Verb: "runas"; Flags: runhidden waituntilterminated shellexec; RunOnceId: "RemoveFirewallRule"

[Code]
// True if the installer added the firewall rule (the flag is written by the [Registry] section above).
function FirewallRuleWasAdded(): Boolean;
var
  Flag: string;
begin
  Result := RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Loadout', 'FirewallRuleAdded', Flag) and (Flag = '1');
end;

// After uninstalling, ask whether to keep the user's settings. Silent uninstalls keep them.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  KeepSettings: Boolean;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    KeepSettings := True;
    if not UninstallSilent then
    begin
      KeepSettings := MsgBox(
        'Keep your Loadout settings (your shortcuts and preferences)?' + #13#10 + #13#10 +
        'Yes: keep them. They will still be there if you install Loadout again.' + #13#10 +
        'No: delete them permanently.',
        MB_YESNO or MB_ICONQUESTION or MB_DEFBUTTON1) = IDYES;
    end;
    if not KeepSettings then
    begin
      // Settings and shortcuts (roaming folder), then the icon cache and log (local folder).
      DelTree(ExpandConstant('{userappdata}\Loadout'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\Loadout'), True, True, True);
    end;
  end;
end;
