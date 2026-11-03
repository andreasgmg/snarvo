# Builds the release and the packages for IT in dist\:
#   Snarvo-<version>-setup-x64.exe – installer (only if Inno Setup 6 is installed)
#   Snarvo-<version>-x64.zip       – the app (unzip and run Snarvo.exe)
#   Snarvo-<version>-kallkod.zip   – the source code, exactly as committed in git
#   SHA256SUMS.txt                 – checksums of the files above
# Run from the repository root: powershell -ExecutionPolicy Bypass -File build-release.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (git status --porcelain) { throw 'Det finns ändringar som inte är committade. Committa först, så att källkoden i zip-filen stämmer med appen.' }

$version = ([xml](Get-Content Snarvo\Snarvo.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$commit = git rev-parse --short HEAD
$dist = Join-Path $PSScriptRoot 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

$publish = Join-Path $PSScriptRoot 'publish'
if (Get-Process Snarvo -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$publish\*" }) {
    throw 'Snarvo körs från publish-mappen. Avsluta den först (högerklicka på ikonen > Avsluta Snarvo).'
}
# A clean folder, so no files from older builds end up in the zip.
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish Snarvo\Snarvo.csproj -c Release -p:Platform=x64 -o publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Bygget misslyckades.' }

$app = Join-Path $dist "Snarvo-$version-x64.zip"
$source = Join-Path $dist "Snarvo-$version-kallkod.zip"
Compress-Archive -Path publish\*, SAKERHET.md, README.md, LICENSE, THIRD-PARTY-NOTICES.md -DestinationPath $app
git archive --format=zip --prefix="Snarvo-$version/" -o $source HEAD
$files = @($app, $source)

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc /Qp "/DAppVersion=$version" /DPlatform=x64 "/DSourceDir=$publish" "/DOutputDir=$dist" installer\Snarvo.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installationsprogrammet kunde inte byggas.' }
    $setup = Join-Path $dist "Snarvo-$version-setup-x64.exe"
    Move-Item (Join-Path $dist 'Snarvo-setup-x64.exe') $setup
    $files = @($setup) + $files
} else {
    Write-Warning 'Inno Setup 6 hittades inte, så inget installationsprogram byggdes (winget install JRSoftware.InnoSetup).'
}

$lines = foreach ($file in $files) {
    '{0}  {1}' -f (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $file -Leaf)
}
$lines += "", "Byggd från git-commit $commit, $(Get-Date -Format 'yyyy-MM-dd HH:mm')."
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $lines)
Copy-Item SAKERHET.md $dist

Get-ChildItem $dist | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
