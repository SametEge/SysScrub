; SysScrub kurulum betiği — Inno Setup 6
;
; build/publish.ps1 tarafından şu parametrelerle çağrılır:
;   /DAppVersion=<sürüm>  /DSourceDir=<yayınlanmış dosyaların yolu>  /O<çıktı klasörü>

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\build\out\fdd"
#endif

#define AppName        "SysScrub"
#define AppPublisher   "Samet Ege"
#define AppUrl         "https://github.com/SametEge/SysScrub"
#define AppExe         "SysScrub.exe"

[Setup]
AppId={{7B4E5F2C-9A31-4D8E-B6C7-1F0A2D5E8C43}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes

; Program Files'a yazıyoruz ve uygulama yönetici hakkı istiyor.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

OutputBaseFilename=SysScrub-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\SysScrub.App\Assets\SysScrub.ico
WizardImageFile=assets\wizard-large.bmp
WizardSmallImageFile=assets\wizard-small.bmp
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
turkish.CreateDesktopIcon=Masaüstü kısayolu oluştur
turkish.LaunchApp={#AppName} uygulamasını başlat
turkish.DotNetMissing=SysScrub için .NET 8 Masaüstü Çalışma Zamanı gerekiyor.%n%nİndirme sayfası açılsın mı?
english.CreateDesktopIcon=Create a desktop shortcut
english.LaunchApp=Launch {#AppName}
english.DotNetMissing=SysScrub requires the .NET 8 Desktop Runtime.%n%nOpen the download page?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Kaldırırken kendi artıklarımızı bırakmıyoruz — bir temizlik uygulamasının kirli çıkması olmaz.
Type: filesandordirs; Name: "{commonappdata}\SysScrub\logs"
Type: dirifempty; Name: "{commonappdata}\SysScrub"

[Code]
{ .NET 8 Masaüstü Çalışma Zamanı kurulu mu? Framework-dependent sürüm bunsuz açılmıyor. }
function IsDotNetDesktopInstalled: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;

  if RegGetSubkeyNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx', Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
      if CompareText(Names[I], 'Microsoft.WindowsDesktop.App') = 0 then
      begin
        Result := True;
        Exit;
      end;
  end;

  { Kayıt defteri okunamazsa dosya sisteminden doğrula. }
  if DirExists(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App')) then
    Result := True;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not IsDotNetDesktopInstalled then
  begin
    if MsgBox(ExpandConstant('{cm:DotNetMissing}'), mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0/runtime?cid=getdotnetcore',
                '', '', SW_SHOW, ewNoWait, ErrorCode);
  end;
end;
