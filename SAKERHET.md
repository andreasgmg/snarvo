# Snarvo – beskrivning för IT och informationssäkerhet

Version 1.1.0. Det här dokumentet beskriver vad programmet gör, vilka data det hanterar och vilka
behörigheter det använder. Syftet är att underlätta en bedömning innan programmet används i en
organisation. Källkoden följer med och kan granskas i sin helhet: cirka 7 000 rader C# och XAML, som du
hittar i `Snarvo\`.

## Vad programmet är

Snarvo är ett tillägg till Windows inbyggda **Skärmklippverktyg** (Snipping Tool). Det körs som
en ikon i systemfältet. Själva skärmdumpen tas av Skärmklippverktyget. Snarvo tar emot bilden via
urklippet och används sedan för att:

- markera med pilar, text och numrerade steg
- dölja känslig information genom pixling
- dela bilden
- göra steg-för-steg-guider i PDF eller Word

## Sammanfattning

| Fråga | Svar |
|---|---|
| Nätverk | **Ingen nätverkskod.** Programmet skickar och hämtar ingenting över nätet. Det finns ingen telemetri och inga uppdateringskontroller. |
| Behörighet | Körs med användarens egna rättigheter (`asInvoker`) och ber aldrig om administratörsbehörighet. |
| Installation | Installationsprogram som bara installerar för den egna användaren, i `%LOCALAPPDATA%\Programs\Snarvo`. Det ber aldrig om administratörsbehörighet och skriver inget i systemmappar. Samma app finns också som zip-fil utan installation. |
| Text i bilderna | Textigenkänning (OCR) görs lokalt av Windows (`Windows.Media.Ocr`). Inga bilder lämnar datorn. |
| Tredjepartskod | Bara två paket, båda från Microsoft: Windows App SDK 1.8 och Win2D 1.3.2. Licenstexterna finns i NuGet-paketen. |
| Signering | Exe-filen och installationsprogrammet är **inte** kodsignerade. Windows SmartScreen visar därför "Okänd utgivare". |
| Kryptering | Sparade skärmdumpar är **inte** krypterade av programmet. Skyddet är Windows-kontot och diskkryptering (BitLocker). |

## Vad programmet gör på datorn

### Urklippet
Programmet registrerar sig med `AddClipboardFormatListener` för att få veta när urklippet ändras.

- **Text läses aldrig.** Programmet läser bara innehåll som är en bild.
- **Standard är bara Skärmklippverktyget.** Programmet läser bara bilder som Skärmklippverktyget har
  lagt på urklippet. Det kontrolleras med vilken process som äger urklippet.
- **Inställningen kan slås av.** Om "Fånga bara bilder från Snipping Tool" stängs av, tas även bilder
  som kopierats från andra program emot.

### Kortkommando
`Ctrl+Alt+S` registreras med `RegisterHotKey`. Det är **ingen tangentbordskrok**: programmet ser
bara när just den kombinationen trycks, aldrig något annat som skrivs.

### Register
Programmet skriver bara i registret om användaren själv slår på "Starta med Windows". Då skrivs ett
värde under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, som bara gäller den egna
användaren.

Installationsprogrammet skriver dessutom:
- samma autostartvärde, om rutan "Starta Snarvo när jag loggar in i Windows" är ikryssad
- avinstallationsuppgifterna under `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall`
- en genväg i den egna Start-menyn, och på skrivbordet om användaren väljer det

### Installation utan frågor
`Snarvo-setup-x64.exe /VERYSILENT /TASKS="autostart"` installerar utan dialogrutor. Utelämna
`/TASKS` för att inte slå på autostart. En körande Snarvo avslutas först (den sparar öppna bilder)
och startas igen efter uppdateringen. Installationsprogrammet är byggt med Inno Setup, och skriptet
finns i `installer\Snarvo.iss`.

### Andra program som startas
- Skärmklippverktyget, via `ms-screenclip:`
- Utforskaren, när användaren väljer "Visa i Utforskaren"
- Standardprogrammet för en sparad PDF eller Word-fil, när användaren väljer "Öppna"

### Filer som skrivs
| Plats | Innehåll |
|---|---|
| `%LOCALAPPDATA%\Snarvo\Library\` | Skärmdumparna (PNG), annoteringar (JSON) och den inlästa texten (`.txt`, används för sökning). |
| `%LOCALAPPDATA%\Snarvo\settings.json` | Inställningar. |
| `%LOCALAPPDATA%\Snarvo\error.log` | Felmeddelanden med teknisk information. Innehåller inte bildinnehåll. |
| `%TEMP%\Snarvo\` | Bilder som användaren drar ut till andra program eller kopierar som fil. De raderas automatiskt efter ett dygn. |
| Valfri mapp | När användaren själv exporterar, sparar till en vald mapp eller sparar en guide. |

Miljövariabeln `SNARVO_DATA` kan peka om datamappen till en annan plats.

## Personuppgifter och sekretess

Skärmdumpar kan innehålla personuppgifter och sekretessbelagd information. **Som standard sparas
varje skärmdump i biblioteket tills användaren tar bort den.** Det betyder att en samling bilder
byggs upp på datorn. För att begränsa det finns två inställningar:

- **Privat läge** (högerklicka på ikonen i systemfältet). Nya skärmdumpar sparas inte i
  biblioteket.
  - De finns på disken bara medan de är öppna i editorn och raderas när fönstret stängs.
  - De läses inte in för sökning.
  - Om programmet kraschar raderas kvarlämnade bilder vid nästa start.
  - Användaren kan uttryckligen spara en enskild bild med "Behåll i biblioteket".
- **Automatisk rensning** (klockknappen i biblioteket). Skärmdumpar som är äldre än 1, 7 eller
  30 dagar raderas.
  - Rensningen körs vid start och en gång i timmen.
  - Filerna raderas direkt och hamnar inte i papperskorgen.

"Dölj känsligt" och "Dölj ord eller namn" använder OCR för att hitta och pixla till exempel
personnummer och namn. Det är ett **hjälpmedel, inte en garanti**. OCR missar ibland text, till
exempel kortnummer. Användaren behöver alltid själv kontrollera bilden innan den delas.

### Rekommenderade inställningar i en organisation
1. Slå på **Privat läge**.
2. Om biblioteket ska användas: slå på **automatisk rensning**, 1 eller 7 dagar.
3. Behåll "Fånga bara bilder från Snipping Tool" påslagen (standard).
4. Kontrollera att datorn har diskkryptering (BitLocker).

## Begränsningar och risker att känna till

- **Ingen oberoende granskning.** Programmet är framtaget med hjälp av en AI-assistent (Claude) och
  har inte granskats av någon utomstående säkerhetsexpert. Därför följer källkoden med.
- **Ingen kodsignering.** Exe-filen är inte signerad, så det går inte att verifiera vem som byggt
  den. Ett alternativ är att IT bygger programmet själv från källkoden, enligt nedan, och jämför.
- **Inga automatiska uppdateringar.** Nya versioner måste distribueras manuellt.
- **Inga centrala policyer.** Inställningarna ligger per användare i `settings.json`. Det finns inget
  stöd för grupprinciper (GPO) eller Intune-policyer.
- **Komponenter som inte används följer med.** Paketet innehåller hela Windows App SDK, cirka
  140 MB, även delar som programmet inte använder.

## Bygga själv från källkoden

Kräver Windows 10 eller 11 och .NET 10 SDK.

```powershell
dotnet publish Snarvo\Snarvo.csproj -c Release -p:Platform=x64 -o publish
```

Paketen hämtas från nuget.org vid första bygget. SHA-256-summor för de färdiga zip-filerna finns
i `SHA256SUMS.txt`.

## Avinstallera

**Installerad version:** avinstallera under Inställningar › Appar. Programmet, genvägarna,
autostarten och `%TEMP%\Snarvo\` tas bort. Därefter får användaren frågan om även skärmdumparna
och inställningarna i `%LOCALAPPDATA%\Snarvo\` ska raderas. Vid tyst avinstallation
(`unins000.exe /VERYSILENT`) ligger de kvar.

**Zip-versionen:**
1. Högerklicka på ikonen i systemfältet. Stäng av "Starta med Windows" om den är påslagen, och välj
   sedan "Avsluta Snarvo".
2. Radera programmappen.
3. Radera `%LOCALAPPDATA%\Snarvo\` (skärmdumpar och inställningar) och `%TEMP%\Snarvo\`.
