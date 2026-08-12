<#
.SYNOPSIS
    dist/ klasörünü üretir: taşınabilir exe + kurulum paketi.

.DESCRIPTION
    İki farklı çıktı üretilir:

      framework-dependent  ~10 MB, .NET 8 Desktop Runtime kurulu makinelerde çalışır.
                           dist/SysScrub.exe olarak yayınlanır — hızlı deneme için.

      self-contained       ~80 MB, hiçbir ön koşul istemez. GitHub Releases'a giden sürüm.

    Kurulum paketi Inno Setup ile üretilir. Inno kurulu değilse bu adım atlanır ve
    uyarı verilir; taşınabilir çıktılar yine de üretilmiş olur.

.PARAMETER SelfContained
    Self-contained sürümü de üret (varsayılan: sadece framework-dependent).

.PARAMETER SkipInstaller
    Kurulum paketi üretimini atla.

.EXAMPLE
    ./build/publish.ps1
    ./build/publish.ps1 -SelfContained
#>

[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$SkipInstaller,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$distDir = Join-Path $repoRoot 'dist'
$stageDir = Join-Path $repoRoot 'build/out'
$appProject = Join-Path $repoRoot 'src/SysScrub.App/SysScrub.App.csproj'
$cliProject = Join-Path $repoRoot 'src/SysScrub.Cli/SysScrub.Cli.csproj'

function Write-Step($message) {
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Get-Version {
    [xml]$props = Get-Content (Join-Path $repoRoot 'build/version.props')
    $group = $props.Project.PropertyGroup
    $version = $group.SysScrubVersion
    $suffix = $group.SysScrubVersionSuffix

    if ([string]::IsNullOrWhiteSpace($suffix)) { return $version }
    return "$version-$suffix"
}

$version = Get-Version
Write-Step "SysScrub $version paketleniyor"

# Temiz başlangıç: eski çıktılar yeni sürümle karışmasın.
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

# ---------------------------------------------------------------- taşınabilir (küçük)
Write-Step 'Framework-dependent sürüm derleniyor'
$fddDir = Join-Path $stageDir 'fdd'

dotnet publish $appProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    --output $fddDir

dotnet publish $cliProject `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    --output $fddDir

foreach ($name in 'SysScrub.exe', 'sysscrub-cli.exe') {
    $source = Join-Path $fddDir $name
    Copy-Item $source $distDir -Force
    Write-Host "    dist/$name  ($([math]::Round((Get-Item $source).Length / 1MB, 1)) MB)"
}

# ---------------------------------------------------------------- taşınabilir (bağımsız)
if ($SelfContained) {
    Write-Step 'Self-contained sürüm derleniyor'
    $scdDir = Join-Path $stageDir 'scd'

    dotnet publish $appProject `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        --output $scdDir

    $zip = Join-Path $stageDir "SysScrub-$version-portable-x64.zip"
    Compress-Archive -Path (Join-Path $scdDir '*') -DestinationPath $zip -Force
    Write-Host "    $zip"
}

# ---------------------------------------------------------------- kurulum paketi
if (-not $SkipInstaller) {
    Write-Step 'Kurulum paketi üretiliyor'

    # winget makineye da kullanıcı kapsamına da kurabiliyor; üçünü de deniyoruz.
    $isccCandidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $iscc) {
        Write-Warning 'Inno Setup bulunamadı, kurulum paketi atlandı.'
        Write-Warning 'Kurmak için:  winget install JRSoftware.InnoSetup'
    }
    else {
        $iss = Join-Path $repoRoot 'installer/SysScrub.iss'
        & $iscc "/DAppVersion=$version" "/DSourceDir=$fddDir" "/O$distDir" $iss | Out-Null

        $setup = Get-ChildItem $distDir -Filter 'SysScrub-Setup-*.exe' |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1

        if ($setup) {
            Write-Host "    dist/$($setup.Name)  ($([math]::Round($setup.Length / 1MB, 1)) MB)"
        }
    }
}

Write-Step 'Tamamlandı'
Get-ChildItem $distDir -File | Format-Table Name, @{ N = 'MB'; E = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
