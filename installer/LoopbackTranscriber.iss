#define MyAppName "LoopbackTranscriber"
#define MyAppPublisher "pgomur"
#define MyAppURL "https://github.com/pgomur"
#define MyAppExeName "LoopbackTranscriber.exe"
#define MyPublishDir "publish"

#define AppExePath AddBackslash(SourcePath) + MyPublishDir + "\" + MyAppExeName
#if !FileExists(AppExePath)
  #error "No se encuentra publish\LoopbackTranscriber.exe: publica la app antes de compilar el instalador."
#endif

#define VerMajor
#define VerMinor
#define VerRev
#define VerBuild
#expr GetVersionComponents(AppExePath, VerMajor, VerMinor, VerRev, VerBuild)
#define MyAppVersion Str(VerMajor) + "." + Str(VerMinor) + "." + Str(VerRev)
#define MyFileVersion MyAppVersion + "." + Str(VerBuild)

#if FileExists(AddBackslash(SourcePath) + "redist\vc_redist.x64.exe")
  #define BundleVCRedist
#endif

[Setup]
AppId={{B6E1F6C4-6E8B-4A6E-9C2A-6C7B9A9E9C3D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
AppCopyright=Copyright (C) 2026 {#MyAppPublisher}
VersionInfoVersion={#MyFileVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename={#MyAppName}Setup-{#MyAppVersion}
SetupIconFile=..\LoopbackTranscriber\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
ShowLanguageDialog=auto
UsePreviousLanguage=yes
SetupMutex={#MyAppName}SetupMutex
CloseApplications=yes
AppMutex=Local\LoopbackTranscriber-B6E1F6C4-6E8B-4A6E-9C2A-6C7B9A9E9C3D
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
spanish.InstallingVCRedist=Instalando Microsoft Visual C++ Redistributable...
spanish.VCRedistDownloadFailed=No se pudo descargar Microsoft Visual C++ Redistributable, necesario para la transcripción y los resúmenes con IA.%n%nLa instalación continuará. Si esas funciones no arrancan, instálalo desde:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
spanish.VCRedistInstallFailed=No se pudo instalar Microsoft Visual C++ Redistributable (código %1), necesario para la transcripción y los resúmenes con IA.%n%nSi esas funciones no arrancan, instálalo desde:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
spanish.RemoveUserData=¿Quieres eliminar también los datos de {#MyAppName} guardados en la carpeta de la aplicación (ajustes, transcripciones, modelos descargados y grabaciones pendientes de procesar)?%n%nLas carpetas que hayas cambiado en Configuración no se eliminan. Si eliges No, se conservará todo para una futura instalación.
english.InstallingVCRedist=Installing Microsoft Visual C++ Redistributable...
english.VCRedistDownloadFailed=Microsoft Visual C++ Redistributable, which is required for transcription and AI summaries, could not be downloaded.%n%nSetup will continue. If those features do not start, install it from:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
english.VCRedistInstallFailed=Microsoft Visual C++ Redistributable, which is required for transcription and AI summaries, could not be installed (code %1).%n%nIf those features do not start, install it from:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
english.RemoveUserData=Do you also want to delete the {#MyAppName} data stored in the application folder (settings, transcripts, downloaded models and recordings waiting to be processed)?%n%nFolders you changed in Settings are not deleted. If you choose No, everything will be kept for a future installation.
brazilianportuguese.InstallingVCRedist=Instalando o Microsoft Visual C++ Redistributable...
brazilianportuguese.VCRedistDownloadFailed=Não foi possível baixar o Microsoft Visual C++ Redistributable, necessário para a transcrição e os resumos com IA.%n%nA instalação continuará. Se esses recursos não iniciarem, instale-o a partir de:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
brazilianportuguese.VCRedistInstallFailed=Não foi possível instalar o Microsoft Visual C++ Redistributable (código %1), necessário para a transcrição e os resumos com IA.%n%nSe esses recursos não iniciarem, instale-o a partir de:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
brazilianportuguese.RemoveUserData=Deseja também excluir os dados do {#MyAppName} armazenados na pasta do aplicativo (configurações, transcrições, modelos baixados e gravações pendentes de processamento)?%n%nAs pastas que você alterou nas Configurações não são excluídas. Se escolher Não, tudo será mantido para uma futura instalação.
german.InstallingVCRedist=Microsoft Visual C++ Redistributable wird installiert...
german.VCRedistDownloadFailed=Microsoft Visual C++ Redistributable, das für die Transkription und KI-Zusammenfassungen erforderlich ist, konnte nicht heruntergeladen werden.%n%nDie Installation wird fortgesetzt. Falls diese Funktionen nicht starten, installieren Sie es von:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
german.VCRedistInstallFailed=Microsoft Visual C++ Redistributable, das für die Transkription und KI-Zusammenfassungen erforderlich ist, konnte nicht installiert werden (Code %1).%n%nFalls diese Funktionen nicht starten, installieren Sie es von:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
german.RemoveUserData=Möchten Sie auch die im Anwendungsordner gespeicherten {#MyAppName}-Daten löschen (Einstellungen, Transkripte, heruntergeladene Modelle und noch nicht verarbeitete Aufnahmen)?%n%nOrdner, die Sie in den Einstellungen geändert haben, werden nicht gelöscht. Bei Nein bleibt alles für eine spätere Installation erhalten.
russian.InstallingVCRedist=Установка Microsoft Visual C++ Redistributable...
russian.VCRedistDownloadFailed=Не удалось скачать Microsoft Visual C++ Redistributable, необходимый для расшифровки и резюме с ИИ.%n%nУстановка будет продолжена. Если эти функции не запустятся, установите его по адресу:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
russian.VCRedistInstallFailed=Не удалось установить Microsoft Visual C++ Redistributable (код %1), необходимый для расшифровки и резюме с ИИ.%n%nЕсли эти функции не запустятся, установите его по адресу:%nhttps://aka.ms/vs/17/release/vc_redist.x64.exe
russian.RemoveUserData=Удалить также данные {#MyAppName} из папки приложения (настройки, расшифровки, загруженные модели и ещё не обработанные записи)?%n%nПапки, изменённые в настройках, не удаляются. Если выбрать «Нет», всё будет сохранено для будущей установки.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
Type: filesandordirs; Name: "{app}\runtimes\linux-x64"
Type: filesandordirs; Name: "{app}\runtimes\vulkan\linux-x64"
Type: filesandordirs; Name: "{app}\runtimes\win-arm64"
Type: filesandordirs; Name: "{app}\runtimes\win-x86"
Type: filesandordirs; Name: "{app}\cs"
Type: filesandordirs; Name: "{app}\fr"
Type: filesandordirs; Name: "{app}\it"
Type: filesandordirs; Name: "{app}\ja"
Type: filesandordirs; Name: "{app}\ko"
Type: filesandordirs; Name: "{app}\pl"
Type: filesandordirs; Name: "{app}\tr"
Type: filesandordirs; Name: "{app}\zh-Hans"
Type: filesandordirs; Name: "{app}\zh-Hant"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "runtimes\linux-x64\*,runtimes\vulkan\linux-x64\*,runtimes\win-arm64\*,runtimes\win-x86\*,cs\*,fr\*,it\*,ja\*,ko\*,pl\*,tr\*,zh-Hans\*,zh-Hant\*"

#ifdef BundleVCRedist
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: dontcopy
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;
  VCRedistPath: String;
  VCRedistNeedsRestart: Boolean;

function VCRedistMissing(): Boolean;
var
  Installed, Minor: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and (Installed = 1) then
  begin
    if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Minor', Minor) and (Minor >= 30) then
      Result := False;
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID <> wpReady) or not VCRedistMissing() then
    Exit;

#ifdef BundleVCRedist
  ExtractTemporaryFile('vc_redist.x64.exe');
  VCRedistPath := ExpandConstant('{tmp}\vc_redist.x64.exe');
#else
  DownloadPage.Clear;
  DownloadPage.Add('https://aka.ms/vs/17/release/vc_redist.x64.exe', 'vc_redist.x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      VCRedistPath := ExpandConstant('{tmp}\vc_redist.x64.exe');
    except
      VCRedistPath := '';
      if not DownloadPage.AbortedByUser then
        SuppressibleMsgBox(CustomMessage('VCRedistDownloadFailed'), mbInformation, MB_OK, IDOK);
    end;
  finally
    DownloadPage.Hide;
  end;
#endif
end;

procedure InstallVCRedist;
var
  ResultCode: Integer;
begin
  if (VCRedistPath = '') or not FileExists(VCRedistPath) then
    Exit;

  WizardForm.StatusLabel.Caption := CustomMessage('InstallingVCRedist');
  if not Exec(VCRedistPath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;

  Log(Format('vc_redist.x64.exe finished with code %d', [ResultCode]));

  if (ResultCode = 1641) or (ResultCode = 3010) then
    VCRedistNeedsRestart := True
  else if (ResultCode <> 0) and (ResultCode <> 1638) then
    SuppressibleMsgBox(FmtMessage(CustomMessage('VCRedistInstallFailed'), [IntToStr(ResultCode)]), mbError, MB_OK, IDOK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallVCRedist;
end;

function NeedRestart(): Boolean;
begin
  Result := VCRedistNeedsRestart;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\{#MyAppName}');
    if DirExists(DataDir) and (not UninstallSilent) then
    begin
      if MsgBox(CustomMessage('RemoveUserData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
        DelTree(ExpandConstant('{%TEMP}\{#MyAppName}'), True, True, True);
      end;
    end;
  end;
end;
