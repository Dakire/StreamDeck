; Installateur de StreamDak (Inno Setup 6). Lancé par build.ps1, qui passe ces définitions :
;   /DAppVersion=1.0.0            version (SemVer, Directory.Build.props)
;   /DSourceDir=<dossier>         sortie de « dotnet publish » (win-x64)
;   /DFlavor=self-contained|framework-dependent   (texte du nom du fichier)
;   /DFrameworkDependent=1        (variante sans runtime : le runtime .NET Desktop 10 doit être installé)
;   /DOutputDir=<dossier>         où écrire le Setup.exe
;   /DRedistDir=<dossier>         (facultatif) contient MicrosoftEdgeWebview2Setup.exe, vérifié par build.ps1
;   /DSignTool=1                  (facultatif) signe l'installateur et le désinstallateur avec l'outil « signtool » défini par /S
; Les données de l'utilisateur (profils, réglages, secrets, profil WebView2, journal) vivent dans %AppData%\StreamDak : ce script n'y écrit jamais,
; sauf pour proposer de les supprimer à la désinstallation (jamais en mode silencieux, jamais par défaut).

#ifndef AppVersion
  #error AppVersion est obligatoire (/DAppVersion=x.y.z)
#endif
#ifndef SourceDir
  #error SourceDir est obligatoire (/DSourceDir=...)
#endif
#ifndef Flavor
  #define Flavor "self-contained"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "StreamDak"
#define AppExe "StreamDak.exe"
#define AppId "{{7C1D5F0A-3B6E-4E52-9A1B-5D2F8C4E9A71}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=StreamDak
AppPublisherURL=https://github.com/Dakire/StreamDeck
AppSupportURL=https://github.com/Dakire/StreamDeck/issues
AppUpdatesURL=https://github.com/Dakire/StreamDeck/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=Installation de {#AppName}

; Installation pour l'utilisateur courant par défaut (aucun droit administrateur), pour tous les utilisateurs si l'utilisateur le choisit
DefaultDirName={autopf}\{#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousAppDir=yes
UsePreviousTasks=yes
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

; Mise à jour sur place : même AppId, même dossier ; l'application en cours est fermée proprement (gestionnaire de redémarrage de Windows)
CloseApplications=yes
CloseApplicationsFilter={#AppExe}
RestartApplications=no

OutputDir={#OutputDir}
OutputBaseFilename=StreamDak-{#AppVersion}-{#Flavor}-Setup
SetupIconFile=..\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra
SolidCompression=yes
#ifdef SignTool
SignTool=signtool
SignedUninstaller=yes
#endif

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: unchecked
Name: "autostart"; Description: "Lancer {#AppName} à l'ouverture de session Windows"; GroupDescription: "Démarrage :"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef RedistDir
Source: "{#RedistDir}\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not WebView2Installed
#endif

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Même valeur que « Paramètres › Démarrer avec Windows » (AutoStart.cs) : le registre fait foi
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
#ifdef RedistDir
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installation du composant Microsoft WebView2…"; Flags: waituntilterminated; Check: not WebView2Installed
#endif
Filename: "{app}\{#AppExe}"; Description: "Lancer {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Seulement ce que l'installateur a posé : jamais %AppData%
Type: filesandordirs; Name: "{app}"

[Code]
const
  WebView2Guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  DotNetDesktopKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

function WebView2Installed(): Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\' + WebView2Guid, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function DotNetDesktop10Installed(): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  Found: TFindRec;
begin
  Result := False;
  // Installation habituelle : inscrite au registre
  if RegGetValueNames(HKLM64, DotNetDesktopKey, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 3) = '10.' then Result := True;
  // Runtime posé sans inscription (SDK, archive) : le dossier standard de l'hôte .NET
  if (not Result) and FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), Found) then
  begin
    Result := True;
    FindClose(Found);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
#ifdef FrameworkDependent
  // Variante sans le runtime : il doit déjà être là (aucun téléchargement n'est lancé sans que l'utilisateur le décide)
  if not DotNetDesktop10Installed() then
  begin
    SuppressibleMsgBox('Cette variante de {#AppName} a besoin du runtime « .NET Desktop 10 » (x64), introuvable sur cet ordinateur.' + #13#10#13#10 +
      'Installe-le depuis https://dotnet.microsoft.com/download/dotnet/10.0 ou utilise l''installateur « self-contained », qui l''embarque.',
      mbCriticalError, MB_OK, IDOK);
    Result := False;
  end;
#endif
end;

#ifndef RedistDir
procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Installateur construit sans le programme d'installation de WebView2 (hors ligne) : on prévient plutôt que de laisser des cadres vides
  if (CurStep = ssPostInstall) and (not WebView2Installed()) then
    SuppressibleMsgBox('Le composant Microsoft WebView2 est introuvable : les pages web et les badges de notifications ne s''afficheront pas.' + #13#10 +
      'Installe-le depuis https://developer.microsoft.com/microsoft-edge/webview2/ (Windows 11 l''inclut).', mbInformation, MB_OK, IDOK);
end;
#endif

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  DataDir := ExpandConstant('{userappdata}\{#AppName}');
  // Jamais en mode silencieux (une mise à jour passe par l'installateur, pas par le désinstallateur) et jamais sans demander
  if (not UninstallSilent()) and DirExists(DataDir) then
    if MsgBox('Supprimer aussi tes profils, réglages et secrets enregistrés ?' + #13#10#13#10 + DataDir + #13#10#13#10 +
        'Choisis « Non » pour les garder (une réinstallation les retrouvera).', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DataDir, True, True, True);
end;
