# Snarvo

Ett tillägg till Skärmklippverktyget (Snipping Tool) i Windows 10 och 11. Med det kan du markera,
dölja och dela skärmdumpar snabbare.

Snipping Tool har inget plugin-API, så Snarvo körs bredvid som en egen app i systemfältet.
Snipping Tool tar själva skärmdumpen, och Snarvo fångar upp den via urklippet. Där kan du
annotera, dölja känslig information och dela bilden.

## Installera

Ladda ner `Snarvo-setup-x64.exe` (eller `Snarvo-setup-arm64.exe`) från
[Releases](https://github.com/andreasgmg/snarvo/releases/latest) och kör det. Installationen gäller
bara din användare, hamnar i `%LOCALAPPDATA%\Programs\Snarvo` och kräver ingen
administratörsbehörighet. Snarvo hamnar i Start-menyn och kan avinstalleras under
Inställningar › Appar.

Vill du hellre slippa installera finns samma app som zip-fil: packa upp och starta `Snarvo.exe`.

För IT: `Snarvo-setup-x64.exe /VERYSILENT /TASKS="autostart"` installerar utan frågor. Avinstallera
tyst med `unins000.exe /VERYSILENT` i programmappen.

## Funktioner

- **Fånga:** `Ctrl+Alt+S` eller klick på ikonen i systemfältet startar Snipping Tool. Resultatet
  öppnas direkt i Snarvo. Vanliga `Win+Shift+S`-klipp hamnar också i biblioteket.
- **Pilar** (avsmalnande, Snagit-stil), linjer, rektanglar, ellipser och frihandspenna.
- **Mjuka penndrag:** pennan jämnar ut darrig musrörelse. Håll musen still en halv sekund i slutet
  av ett drag, som på iPad, så blir en skakig cirkel, ellips, rektangel eller linje perfekt.
- **Numrerade steg** (1, 2, 3 …) som numreras om automatiskt.
- **Pratbubblor** med pekare och **text** med kontrastkant.
- **Överstrykning** som behåller texten läsbar.
- **Oskärpa** och **pixling** för att dölja lösenord, nycklar och e-postadresser.
- **Förstoringsglas** och **strålkastare** (tonar ner allt utom det viktiga).
- **Beskär** och **klipp ut remsa**: ta bort en rad- eller kolumnremsa ur mitten och foga ihop resten.
- **Beskär kanter automatiskt** (`Ctrl+Shift+K`, eller knappen som visas när Beskär är valt): tar
  bort enfärgade kanter runt bilden och lämnar en liten marginal. Annoteringar beskärs aldrig bort.
- **Effekter:** ram, rundade hörn och skugga runt hela bilden.
- **Textigenkänning (OCR):** kopiera texten i bilden med Windows inbyggda OCR.
- **Dölj känsligt** (`Ctrl+H`): hittar e-post, telefonnummer, personnummer, IBAN, IP-adresser,
  API-nycklar och värden efter "Lösenord:" med OCR och pixlar dem. Windows OCR hoppar ofta över
  kortnummer helt, så kontrollera alltid bilden själv.
- **Förenkla:** byter ut all text i bilden mot rundade block i bildens egna färger, så att det du
  vill visa sticker ut. Markera ett block och tryck `Delete` för att visa just den texten igen.
- **Dölj ord eller namn** (`Ctrl+Shift+H`, eller pilen på "Dölj känsligt"): skriv ett eller flera ord,
  åtskilda med komma, så pixlas varje ställe i bilden där de står. Stora och små bokstäver spelar
  ingen roll, och å, ä och ö matchar även a och o eftersom OCR ibland tappar prickarna.
- **Färgpipett:** `Alt`+klick i bilden (eller knappen i färgmenyn) väljer färgen och kopierar hex-koden.
- **Mallar:** verktygens färger och storlekar sparas automatiskt. Spara stil och effekter som en
  namngiven mall, och välj en standardmall som nya skärmdumpar får.
- **Storlek vid delning:** 100 %, 75 %, 50 % eller max 1920/1280/800 px bred. Gäller kopiering,
  export och dra ut. Biblioteket behåller alltid full storlek.
- **Dela:** spara direkt i en vald mapp (t.ex. OneDrive) eller kopiera som fil (`Ctrl+Shift+C`).
- **Dra ut:** dra handtaget "Dra ut" i editorn, eller en miniatyr i biblioteket, direkt till Teams,
  Outlook, Slack, Utforskaren med flera.
- **Bibliotek** med alla skärmdumpar. Annoteringarna sparas separat, så de går att ändra senare.
  Klick markerar, dubbelklick eller `Enter` öppnar.
- **Automatisk rensning** (klockknappen i biblioteket): radera skärmdumpar som är äldre än 1, 7 eller
  30 dagar. Standard är att allt sparas. När du slår på rensningen visas hur många bilder som raderas
  direkt, och du måste bekräfta. Sedan körs rensningen vid start och en gång i timmen. Bilder som är
  öppna i editorn raderas först efter att de har stängts.
- **Sök på text:** all text i bilderna läses in i bakgrunden, så att sökrutan i biblioteket hittar
  skärmdumpar på innehåll.
- **Kombinera:** markera flera bilder (`Ctrl`+klick) och sätt ihop dem bredvid eller under varandra.
- **Steg-för-steg-guide:** markera skärmdumpar i biblioteket och välj "Guide". Skriv en rubrik och
  en beskrivning per steg, ändra ordning, och spara som PDF eller Word. Stegen hålls ihop på samma
  sida.
- **Video → GIF:** öppna en skärminspelning från Snipping Tool, klipp start och slut, välj
  bildfrekvens och bredd, och spara som GIF som loopar.
- **Kopiera vid stängning:** stäng editorn, så ligger den redigerade bilden på urklippet. Det händer
  bara om du har ändrat något sedan du senast kopierade eller delade bilden. Går att slå av i
  menyn Dela eller i systemfältet.
- Ångra/gör om, kopiera till urklipp, export till PNG/JPEG.

## Kortkommandon i editorn

| Tangent | Verktyg | Tangent | Verktyg |
|---|---|---|---|
| V | Markera | T | Text |
| A | Pil | C | Pratbubbla |
| L | Linje | N | Numrerade steg |
| R | Rektangel | B | Oskärpa |
| E | Ellips | X | Pixla |
| H | Överstrykning | M | Förstoringsglas |
| P | Penna | S | Strålkastare |
| K | Beskär | U | Klipp ut remsa |

- `Ctrl+Z` / `Ctrl+Y`: ångra / gör om
- `Ctrl+C`: kopiera bild
- `Ctrl+Shift+C`: kopiera som fil
- `Ctrl+H`: dölj känsligt
- `Ctrl+Shift+H`: dölj ord eller namn
- `Ctrl+Shift+K`: beskär kanter automatiskt
- `Alt`+klick: färgpipett
- `Ctrl+S`: spara i biblioteket
- `Ctrl+Shift+S`: exportera
- `Ctrl+0` / `Ctrl+1`: anpassa / 100 %
- `Delete`: ta bort markerad
- Piltangenter: knuffa (håll `Shift` för 10 px)
- `Shift` medan du ritar: raka vinklar eller kvadrat/cirkel

## Bygga och köra

Kräver .NET 10 SDK.

```powershell
dotnet build Snarvo\Snarvo.csproj -p:Platform=x64
.\Snarvo\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\Snarvo.exe
```

Fristående version i en mapp:

```powershell
dotnet publish Snarvo\Snarvo.csproj -c Release -p:Platform=x64 -o publish
```

Paket att dela eller lämna till IT: `powershell -ExecutionPolicy Bypass -File build-release.ps1`.
Det skapar mappen `dist\` med installationsprogrammet (om [Inno Setup 6](https://jrsoftware.org/isinfo.php)
finns), appen som zip, källkoden som zip, SHA-256-summor och
[SAKERHET.md](SAKERHET.md), en beskrivning för IT och informationssäkerhet. Skriptet kräver att
alla ändringar är committade, så att källkoden stämmer med appen.

`Snarvo.exe bild.png` öppnar en bild direkt i editorn, och `Snarvo.exe inspelning.mp4` öppnar
GIF-verktyget. Det fungerar även när Snarvo redan körs.

## Inställningar (högerklicka på ikonen i systemfältet)

- **Privat läge – spara inte skärmdumpar:** nya skärmdumpar öppnas alltid i editorn och raderas från
  disken när du stänger den. De syns inte i biblioteket och läses inte in för sökning. Vill du
  spara en viss bild väljer du **Dela → Behåll i biblioteket**. Om appen kraschar raderas
  kvarlämnade bilder vid nästa start. Skärmdumpar som redan finns i biblioteket påverkas inte.
- **Öppna editorn efter varje snip:** öppnar editorn även för `Win+Shift+S`-klipp. Annars visas
  en notis som du kan klicka på.
- **Kopiera bilden när editorn stängs.**
- **Fånga bara bilder från Snipping Tool:** ignorerar bilder som andra program kopierar.
- **Starta med Windows.**

Tips: om både Snipping Tools eget fönster och Snarvo öppnas efter en skärmdump kan du stänga av
"Öppna Skärmklipp automatiskt" eller liknande i Snipping Tools inställningar.

## Data

Allt sparas i `%LOCALAPPDATA%\Snarvo\`:

- `Library\`: skärmdumparna, inklusive den inlästa texten (`.txt`) som används för sökning
- `settings.json`: inställningarna
- `error.log`: fel, om något går snett

Bilder som dras ut eller kopieras som fil skrivs till `%TEMP%\Snarvo\`, eftersom programmet du
släpper dem i läser dem därifrån. De raderas automatiskt när de är äldre än ett dygn.

Miljövariabeln `SNARVO_DATA` flyttar datamappen, till exempel för att testa utan att röra det
riktiga biblioteket.

## Licens

Copyright © 2026 Andreas Glans.

Snarvo är fri programvara med öppen källkod. Licensed under the EUPL, version 1.2: European Union
Public Licence, se [LICENSE](LICENSE). Licensen finns i officiell översättning till svenska och
alla andra EU-språk på [joinup.ec.europa.eu](https://joinup.ec.europa.eu/collection/eupl/eupl-text-eupl-12).
Komponenter från andra listas i [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
