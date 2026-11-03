# Snarvo – att göra

En sak i taget. Varje punkt byggs, testas och bockas av innan nästa påbörjas.

- [x] **1. Dölj känsligt:** OCR hittar e-post, telefonnummer, personnummer, kortnummer, IBAN,
      IP-adresser, API-nycklar/tokens och värden efter "Lösenord:" m.fl. och pixlar dem automatiskt.
      *Känd begränsning: Windows OCR hoppar ofta över rader med kortnummer helt, så de hittas inte.*
- [x] **2. Färgpipett:** Alt+klick i bilden kopierar hex-färgen och gör den till aktuell färg.
- [x] **3. Kom ihåg stilar + mallar:** verktygens färg/tjocklek/storlek och effekter sparas mellan
      sessioner; förinställningar går att spara och välja.
- [x] **4. Skala vid export:** välj maxbredd eller procent när bilden kopieras/exporteras.
- [x] **6. Sök på text i biblioteket:** OCR i bakgrunden på alla skärmdumpar + sökruta i biblioteket.
- [x] **7. Kombinera bilder:** markera flera i biblioteket och sätt ihop bredvid eller under varandra.
- [x] **9. Snabbdelning:** spara direkt till en vald mapp (t.ex. OneDrive) och "Kopiera som fil".
- [x] **10. Video → GIF:** öppna en inspelning från Snipping Tool, klipp den och spara som GIF.
- [x] **11. Mjuk penna:** utjämnade penndrag, och håll still i slutet för perfekt cirkel/ellips/rektangel/linje.

## Omgång 2

- [x] **12. Kopiera vid stängning:** när editorn stängs kopieras bilden (med annoteringar) till urklipp. Går att slå av.
- [x] **13. Steg-för-steg-guide:** markera flera skärmdumpar i biblioteket → dokument med rubrik och numrerade steg
      (bild + beskrivning per steg), exportera som PDF och Word.
- [x] **14. Förenkla:** byt ut text i bilden mot grå block (OCR hittar raderna), så bara det viktiga syns.
- [x] **15. Dölj ett ord överallt:** skriv ett ord/namn, alla ställen där det står i bilden pixlas.
- [x] **16. Auto-beskär:** ta bort enfärgade kanter runt bilden med ett klick.

## Omgång 3 – inför användning på jobbet

- [x] **17. Git:** versionshantering med en första commit, så alla ändringar syns och går att ångra.
- [x] **18. Privat läge:** skärmdumpar sparas inte i biblioteket – de finns bara medan editorn är öppen
      och raderas när den stängs (och vid nästa start om appen kraschat).
- [x] **19. Automatisk rensning:** radera skärmdumpar äldre än 1, 7 eller 30 dagar (standard: aldrig).
- [x] **20. Paket till IT:** säkerhetsbeskrivning, versionsnummer, zip med källkod och zip med appen + SHA-256.

## Omgång 4 – publicering som öppen källkod

- [x] **21. Nytt namn: Snarvo.** Projekt, exe, texter, ikonnamn. Befintligt bibliotek och inställningar flyttas
      automatiskt från den gamla datamappen till `%LOCALAPPDATA%\Snarvo` vid första start; autostart flyttas också.
- [x] **22. Licens EUPL-1.2** med Andreas Glans som upphovsperson (LICENSE, licensrad i källfilerna, i appen).
- [x] **23. Git-historiken:** slogs ihop till en första commit (utan det gamla namnet), författare Andreas Glans
      med GitHubs noreply-adress i stället för gmail.
- [x] **24. GitHub Actions:** bygg vid varje push; vid en versionstagg (v1.0.0) skapas en release med zip + SHA-256.
- [x] **25. Hemsida (GitHub Pages):** beskrivning, riktiga skärmbilder, nedladdningsknapp. Lugn, personlig stil.
- [x] **26. Kodsignering via SignPath Foundation:** kodsigneringspolicy, integritetspolicy, förberett byggsteg
      och färdig ansökningstext.
- [x] **27. Publicera:** github.com/andreasgmg/snarvo, hemsida på andreasgmg.github.io/snarvo, release v1.0.0.

## Omgång 5

- [x] **28. Installationsprogram (v1.1.0):** Inno Setup, bara för den egna användaren, utan admin.
      Start-meny, valfri autostart och skrivbordsgenväg, avinstallation under Appar (frågar om
      skärmdumparna ska raderas). Stänger en körande Snarvo med `Snarvo.exe --exit` före uppdatering.
      Byggs i GitHub Actions för x64 och ARM64. Ny appikon som matchar hemsidan.

## Omgång 6 – snyggare, säkrare och fler funktioner

- [x] **29. Bort med ARM64:** bara x64 i bygge, installationsprogram, hemsida och dokument.
- [x] **30. Snyggare grafik:** tydligare pil med vit kant, text som etikett med bakgrund, lättare
      skugga, skarpare sifferbrickor, rundare pratbubblor, en färdig färgpalett som matchar hemsidan.
- [x] **31. Säkrare döljning:** nytt verktyg "Täck över" (heltäckande ruta). "Dölj känsligt" täcker
      över som standard, med val för pixla/oskärpa. Personnummer måste ha ett rimligt datum (inte kontrollsiffra – en felläst siffra får inte lämna ett riktigt nummer synligt). E-post hittas även när OCR tappar punkten.
- [x] **32. Lugnare verktygsfält:** färre knappar; spara, exportera och storlek under "Dela",
      Förenkla och Läs text under "Mer".
- [ ] **33. Inställningsfönster:** Allmänt, Integritet och Utseende samlat i ett fönster.
- [ ] **34. Klicka på text för att markera:** ett klick (utan att dra) med rektangel, överstrykning
      eller täck över på ett ord markerar exakt det ordet.
- [ ] **35. Stegregistrering:** Starta, klicka runt som vanligt, Stoppa → färdig guide med en bild
      per klick, en ring där du klickade och en färdig text ("Klicka på Spara").
- [ ] **36. Guider:** egen logga och färg, export till webbsida (HTML), alternativtext på bilderna.
- [ ] **37. Centrala inställningar för IT:** grupprinciper (ADMX) och register under
      `HKLM\Software\Policies\Snarvo`; låsta inställningar syns som "styrs av din organisation".
- [ ] **38. winget:** manifest och skript så att Snarvo kan installeras med `winget install`.
- [ ] **39. Engelsk version:** appen följer Windows språk (svenska, annars engelska).
- [ ] **40. Automatiska tester:** känsliga uppgifter, privat läge och rensning; körs i GitHub Actions.
