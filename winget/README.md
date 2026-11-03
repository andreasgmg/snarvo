# Snarvo i winget

[winget](https://learn.microsoft.com/windows/package-manager/) är Windows egen pakethanterare.
När Snarvo finns i winget-katalogen kan vem som helst installera och uppdatera det med

```powershell
winget install AndreasGlans.Snarvo
winget upgrade AndreasGlans.Snarvo
```

och IT kan distribuera det med Intune eller andra verktyg som använder winget.

## Lägga till en ny version

1. Släpp versionen på GitHub som vanligt (tagga `vX.Y.Z`). Vänta tills releasen har
   `Snarvo-setup-x64.exe` och `SHA256SUMS.txt`.
2. Skapa manifesten:

   ```powershell
   powershell -ExecutionPolicy Bypass -File winget\New-WingetManifest.ps1 -Version X.Y.Z
   ```

3. Kontrollera dem lokalt (kräver att winget får använda lokala manifest, en gång som administratör:
   `winget settings --enable LocalManifestFiles`):

   ```powershell
   winget validate winget\manifests\a\AndreasGlans\Snarvo\X.Y.Z
   winget install --manifest winget\manifests\a\AndreasGlans\Snarvo\X.Y.Z
   ```

4. Skicka in dem till Microsofts katalog. Enklast med
   [wingetcreate](https://github.com/microsoft/winget-create):

   ```powershell
   winget install Microsoft.WingetCreate
   wingetcreate submit --token <GitHub-token> winget\manifests\a\AndreasGlans\Snarvo\X.Y.Z
   ```

   Det öppnar en pull request mot `microsoft/winget-pkgs`. Microsofts automatiska kontroller
   installerar programmet och söker efter skadlig kod; sedan granskas den av en människa.
   Första gången tar det oftast några dagar.

Nästa versioner går snabbare med `wingetcreate update AndreasGlans.Snarvo --version X.Y.Z --urls <installerarens URL> --submit`.

Manifesten skapas lokalt och checkas inte in (de ligger i `winget/manifests/`, som git ignorerar).
