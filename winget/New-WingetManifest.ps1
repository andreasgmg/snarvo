# Copyright (c) 2026 Andreas Glans
# SPDX-License-Identifier: EUPL-1.2
#
# Writes the winget manifests for a released version of Snarvo, so it can be installed with
#   winget install AndreasGlans.Snarvo
# The installer's SHA-256 is read from the release's SHA256SUMS.txt on GitHub.
#
#   powershell -ExecutionPolicy Bypass -File winget\New-WingetManifest.ps1 -Version 1.2.0
#
# The result lands in winget\manifests\a\AndreasGlans\Snarvo\<version>\ – the same layout as the
# microsoft/winget-pkgs repository. See winget\README.md for how to submit it.
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'

$repo = 'https://github.com/andreasgmg/snarvo'
$installer = "$repo/releases/download/v$Version/Snarvo-setup-x64.exe"
$sums = (Invoke-WebRequest "$repo/releases/download/v$Version/SHA256SUMS.txt" -UseBasicParsing).Content
if ($sums -is [byte[]]) { $sums = [Text.Encoding]::UTF8.GetString($sums) }
$line = ($sums -split "`n") | Where-Object { $_ -match 'Snarvo-setup-x64\.exe' } | Select-Object -First 1
if (-not $line) { throw "Hittade inte Snarvo-setup-x64.exe i SHA256SUMS.txt för v$Version." }
$sha = ($line -split '\s+')[0].ToUpperInvariant()

$id = 'AndreasGlans.Snarvo'
$folder = Join-Path $PSScriptRoot "manifests\a\AndreasGlans\Snarvo\$Version"
New-Item -ItemType Directory -Force $folder | Out-Null
$schema = '1.6.0'

function Write-Manifest($name, $text) {
    # winget-pkgs wants UTF-8 without BOM.
    [IO.File]::WriteAllText((Join-Path $folder $name), $text.Trim() + "`n", (New-Object Text.UTF8Encoding $false))
}

Write-Manifest "$id.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@

Write-Manifest "$id.installer.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: inno
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '{387C4055-04F2-4303-944D-D23D6689BCBC}_is1'
MinimumOSVersion: 10.0.17763.0
Installers:
- Architecture: x64
  InstallerUrl: $installer
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $schema
"@

Write-Manifest "$id.locale.en-US.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Andreas Glans
PublisherUrl: https://github.com/andreasgmg
PublisherSupportUrl: $repo/issues
Author: Andreas Glans
PackageName: Snarvo
PackageUrl: https://andreasgmg.github.io/snarvo/
License: EUPL-1.2
LicenseUrl: $repo/blob/main/LICENSE
Copyright: (c) 2026 Andreas Glans
ShortDescription: Companion app for the Windows Snipping Tool - annotate, hide personal data and make step-by-step guides.
Description: |-
  Snarvo works next to the Snipping Tool in Windows. Take a screenshot as usual and add arrows,
  numbered steps and labels, hide personal identity numbers, e-mail addresses and passwords
  automatically, record step-by-step guides and save them as PDF, Word or web page.
  No account, no cloud and no network code at all.
Moniker: snarvo
Tags:
- screenshot
- snipping-tool
- annotation
- redaction
- guide
- privacy
ReleaseNotesUrl: $repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@

Write-Manifest "$id.locale.sv-SE.yaml" @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$schema.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: sv-SE
Publisher: Andreas Glans
PackageName: Snarvo
License: EUPL-1.2
ShortDescription: Tillägg till Skärmklippverktyget i Windows - markera, dölj personuppgifter och gör steg-för-steg-guider.
Description: |-
  Snarvo fungerar bredvid Skärmklippverktyget i Windows. Ta en skärmdump som vanligt och sätt ut
  pilar, numrerade steg och etiketter, dölj personnummer, e-post och lösenord automatiskt, spela in
  steg-för-steg-guider och spara dem som PDF, Word eller webbsida. Inget konto, inget moln och
  ingen nätverkskod.
ManifestType: locale
ManifestVersion: $schema
"@

Write-Host "Manifest för $id $Version skrivna till $folder"
Get-ChildItem $folder | Select-Object -ExpandProperty Name
