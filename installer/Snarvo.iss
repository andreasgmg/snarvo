; Copyright (c) 2026 Andreas Glans
; SPDX-License-Identifier: EUPL-1.2
;
; Installer for Snarvo, built with Inno Setup 6 (https://jrsoftware.org/isinfo.php).
; It installs for the current user only, under %LOCALAPPDATA%\Programs\Snarvo, so it never
; asks for administrator rights.
;
;   ISCC /DAppVersion=1.0.0 /DPlatform=x64 /DSourceDir=..\publish installer\Snarvo.iss
;
; SourceDir is the output of dotnet publish. Platform is x64.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef Platform
  #define Platform "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
; Never change AppId: Windows uses it to recognise an update of an earlier installation.
AppId={{387C4055-04F2-4303-944D-D23D6689BCBC}
AppName=Snarvo
AppVersion={#AppVersion}
AppVerName=Snarvo {#AppVersion}
AppPublisher=Andreas Glans
AppPublisherURL=https://andreasgmg.github.io/snarvo/
AppSupportURL=https://github.com/andreasgmg/snarvo/issues
AppUpdatesURL=https://github.com/andreasgmg/snarvo/releases
AppCopyright=© 2026 Andreas Glans
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Snarvo Setup
; Swedish on a Swedish Windows, English otherwise.
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

PrivilegesRequired=lowest
DefaultDirName={autopf}\Snarvo
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
UsedUserAreasWarning=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputDir={#OutputDir}
OutputBaseFilename=Snarvo-setup-{#LowerCase(Platform)}
SetupIconFile=..\Snarvo\Assets\Snarvo.ico
UninstallDisplayIcon={app}\Snarvo.exe
UninstallDisplayName=Snarvo
WizardStyle=modern
WizardImageFile=stor-164.bmp,stor-246.bmp,stor-328.bmp
WizardSmallImageFile=liten-58.bmp,liten-87.bmp,liten-116.bmp
Compression=lzma2/max
SolidCompression=yes
; Closing Snarvo is handled in [Code], with the app's own --exit.
CloseApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "sv"; MessagesFile: "compiler:Languages\Swedish.isl"

[Messages]
sv.WizardSelectTasks=Inställningar
sv.SelectTasksDesc=Hur vill du använda Snarvo?
sv.SelectTasksLabel2=Du kan ändra det här senare, under Inställningar i Snarvo.
en.WizardSelectTasks=Settings
en.SelectTasksDesc=How would you like to use Snarvo?
en.SelectTasksLabel2=You can change this later, under Settings in Snarvo.
sv.FinishedHeadingLabel=Snarvo är installerat
sv.FinishedLabel=Du hittar det i Start-menyn, och när det körs ligger det i systemfältet nere till höger.%n%nTa en skärmdump med Win+Shift+S som vanligt, eller med Ctrl+Alt+S.
en.FinishedHeadingLabel=Snarvo is installed
en.FinishedLabel=You will find it in the Start menu, and while it runs it sits in the notification area at the bottom right.%n%nTake a screenshot with Win+Shift+S as usual, or with Ctrl+Alt+S.

[CustomMessages]
sv.Autostart=Starta Snarvo när jag loggar in i Windows
en.Autostart=Start Snarvo when I sign in to Windows
sv.DesktopIcon=Lägg en genväg på skrivbordet
en.DesktopIcon=Put a shortcut on the desktop
sv.StartNow=Starta Snarvo nu
en.StartNow=Start Snarvo now
sv.ShortcutComment=Markera, dölj och dela skärmdumpar
en.ShortcutComment=Annotate, hide and share screenshots
sv.StillRunning=Snarvo körs fortfarande.%n%nAvsluta Snarvo genom att högerklicka på ikonen i systemfältet och välja "Avsluta Snarvo". Klicka sedan på OK.
en.StillRunning=Snarvo is still running.%n%nExit Snarvo by right-clicking its icon in the notification area and choosing "Exit Snarvo". Then click OK.
sv.StillRunningAbort=Snarvo körs fortfarande. Avsluta Snarvo och kör installationen igen.
en.StillRunningAbort=Snarvo is still running. Exit Snarvo and run the installer again.
sv.DeleteData=Vill du också radera dina sparade skärmdumpar och inställningar?%n%nDe ligger i %1. Om du väljer Nej finns de kvar nästa gång du installerar Snarvo.
en.DeleteData=Do you also want to delete your saved screenshots and settings?%n%nThey are in %1. If you choose No, they will still be there the next time you install Snarvo.

[Tasks]
Name: "autostart"; Description: "{cm:Autostart}"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[InstallDelete]
; Files from an earlier version that the new one no longer has.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\SAKERHET.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Snarvo"; Filename: "{app}\Snarvo.exe"; Comment: "{cm:ShortcutComment}"
Name: "{autodesktop}\Snarvo"; Filename: "{app}\Snarvo.exe"; Tasks: desktopicon

[Registry]
; The same value that "Starta med Windows" in the app's menu writes, so the two stay in step.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Snarvo"; ValueData: """{app}\Snarvo.exe"" --background"; Tasks: autostart

[Run]
Filename: "{app}\Snarvo.exe"; Description: "{cm:StartNow}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunningMutex = 'Snarvo.Running';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

var
  WasRunning: Boolean;

{ Asks a running Snarvo to save its work and quit. If that does not work (for example a copy
  started from a zip file somewhere else), the user is asked to quit it from the tray. }
function CloseSnarvo(Exe: String): Boolean;
var
  ResultCode, Waited: Integer;
begin
  Result := True;
  if not CheckForMutexes(RunningMutex) then Exit;
  WasRunning := True;

  if FileExists(Exe) then
  begin
    Exec(Exe, '--exit', '', SW_HIDE, ewNoWait, ResultCode);
    Waited := 0;
    while CheckForMutexes(RunningMutex) and (Waited < 10000) do
    begin
      Sleep(250);
      Waited := Waited + 250;
    end;
  end;

  while CheckForMutexes(RunningMutex) do
    if SuppressibleMsgBox(CustomMessage('StillRunning'), mbInformation, MB_OKCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { The settings page is the only page, so its button starts the installation. }
  if CurPageID = wpSelectTasks then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CloseSnarvo(ExpandConstant('{app}\Snarvo.exe')) then
    Result := CustomMessage('StillRunningAbort');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  { A silent update (for example from IT) starts Snarvo again if it was running before. }
  if (CurStep = ssPostInstall) and WasRunning and WizardSilent then
    Exec(ExpandConstant('{app}\Snarvo.exe'), '--background', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

function InitializeUninstall: Boolean;
begin
  Result := CloseSnarvo(ExpandConstant('{app}\Snarvo.exe'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value, DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Autostart may have been switched on from the app's menu, so it is removed here rather
      than through [Registry]. Only an entry that points to this installation is touched. }
    if RegQueryStringValue(HKCU, RunKey, 'Snarvo', Value) and
       (Pos(Lowercase(ExpandConstant('{app}\Snarvo.exe')), Lowercase(Value)) > 0) then
      RegDeleteValue(HKCU, RunKey, 'Snarvo');
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { Images dragged out to other programs; Snarvo normally removes them after a day. }
    DelTree(GetEnv('TEMP') + '\Snarvo', True, True, True);

    { The screenshots are the user's own files, so they stay unless the user says otherwise. }
    DataDir := ExpandConstant('{localappdata}\Snarvo');
    if DirExists(DataDir) and not UninstallSilent then
      if MsgBox(FmtMessage(CustomMessage('DeleteData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
