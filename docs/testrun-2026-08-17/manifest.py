# -*- coding: utf-8 -*-
"""Vad testkörningen såg, vy för vy. Läses av build_pdf.py."""

# ("fil", "rubrik", "var i appen", [("BRA"|"LOGISKT"|"OLOGISKT"|"FÖRSLAG", "text"), ...])

VIEWS = [

    # ---------------------------------------------------------------- Start

    ("40-valkommen.png", "Välkomstvyn", "Första start · WelcomeSheet", [
        ("BRA", "Texten säger rakt ut var uppgifterna hamnar: ”i telefonens säkra lager … "
                "aldrig på någon server”. Det är ovanligt ärligt för en inloggningsruta och "
                "tar udden av den vanligaste invändningen innan den hinner ställas."),
        ("LOGISKT", "Två vägar, en tydlig huvudväg. ”Hoppa över” är inte gömd, och "
                    "undertexten förklarar vad man går miste om utan att skrämmas."),
        ("OLOGISKT", "Halva skärmen är tom. Rubriken börjar först en tredjedel ner och ovanför "
                     "den finns ingenting alls — varken logotyp, bild eller symbol."),
        ("OLOGISKT", "Krysset uppe till höger är en tredje väg ut som inte förklaras. Vad händer "
                     "med ”har du svarat”-flaggan om man kryssar bort rutan i stället "
                     "för att välja?"),
        ("FÖRSLAG", "Fyll toppen med appikonen eller en kartbild, och ta bort krysset så att "
                    "valet är ett av två. Överväg att hoppa över hela vyn när en giltig "
                    "Eventor-session redan finns i webbvylagret — i dag styrs den bara av "
                    "first-run-flaggan."),
    ]),

    ("01-start.png", "Hem", "Fliken Hem", [
        ("BRA", "Tre block, inte en instrumentpanel. ”Hej Jonatan / måndag 17 augusti” "
                "sätter tid och person på en rad, och varje block har exakt en knapp."),
        ("BRA", "Sektionsetiketterna är formulerade som svar, inte som kategorier: ”Kan vara "
                "något för dig” säger varför kortet finns där."),
        ("LOGISKT", "Ordningen senaste resultat → kommande tävling → utveckling följer hur en "
                    "löpare tänker efter en tävlingshelg."),
        ("OLOGISKT", "Kortet säger ”Anmälan stänger ons 26 aug.” men knappen heter "
                     "”Visa tävling”. Appen vet att anmälan är öppen och erbjuder ändå "
                     "inte att anmäla."),
        ("OLOGISKT", "”Tid” uppe till höger är utvecklarläge (tidsmaskinen) och står "
                     "på appens mest synliga plats, i systemblått i stället för i varumärkets "
                     "orange."),
        ("FÖRSLAG", "Låt knappen bli ”Anmäl dig” när deadline ligger framåt, och flytta "
                    "”Tid” till Jag → Utvecklingsläge där tidsmaskinen redan bor."),
    ]),

    ("38-hem-morkt.png", "Hem i mörkt läge", "Fliken Hem · mörkt tema", [
        ("BRA", "Mörkt läge är genomfört, inte inverterat: korten ligger som svagt upphöjda ytor "
                "mot en nästan svart sida, och orangen är uppljusad så den fortfarande syns."),
        ("LOGISKT", "Samma informationshierarki i båda temana — inget hoppar och ingenting "
                    "försvinner."),
        ("OLOGISKT", "”Tid” blir lila mot svart, den svagaste kontrasten på hela sidan."),
        ("FÖRSLAG", "Lägg länkfärgen på samma AccentAction-token som knapparna använder, så "
                    "följer den temat automatiskt."),
    ]),

    # ---------------------------------------------------------------- Tävlingar

    ("12-tavlingar-lista.png", "Tävlingar", "Fliken Tävlingar", [
        ("BRA", "Korten bär arrangörsklubbens märke, avstånd, disciplin och nivå — tillräckligt "
                "för att välja utan att öppna."),
        ("BRA", "Snabbfiltren är förvalda frågor (”För dig”, ”Nära”, "
                "”Gästrikland”), inte en filtermatris."),
        ("OLOGISKT", "Under rubriken MEST RELEVANT står lör 29 aug, sön 30 aug, fre 4 sep — och "
                     "sedan mån 24 aug. Att relevans slår datum är ett medvetet val, men "
                     "datumrubrikerna får det att se ut som en bugg."),
        ("OLOGISKT", "Alla kort bär samma märke: UPPTÄCKT. Ordet förklaras ingenstans och säger "
                     "inget som listan inte redan visar."),
        ("OLOGISKT", "”Filter” uppe till höger är systemblå medan hela resten av appen är "
                     "orange."),
        ("FÖRSLAG", "Byt UPPTÄCKT mot något som ändrar ett beslut — ANMÄLAN ÖPPEN, "
                    "ANMÄLD, SISTA DAGEN — och sätt en rubrik som ”Sorterat efter "
                    "relevans” över listan i stället för datumrubriker när sorteringen inte "
                    "är kronologisk."),
    ]),

    ("13-tavlingar-scroll.png", "Tävlingar, längre ner", "Fliken Tävlingar · rullad", [
        ("BRA", "ANMÄLAN ÖPPEN i orange skiljer sig från UPPTÄCKT och pekar på nästa handling."),
        ("OLOGISKT", "DM medel 29 aug har bara UPPTÄCKT, trots att Hem samtidigt säger att "
                     "anmälan stänger 26 aug. Två vyer, samma tävling, olika besked."),
        ("OLOGISKT", "Sektionen MEST RELEVANT innehåller IGÅR — en tävling som redan sprungits "
                     "i en flik som handlar om att planera framåt."),
        ("FÖRSLAG", "Låt märket härledas ur samma deadline-beräkning som Hem använder, så kan de "
                    "inte säga emot varandra. Lägg passerade tävlingar under snabbfiltret "
                    "”Tidigare”, som redan finns."),
    ]),

    ("14-filter.png", "Filter", "Tävlingar → Filter · halvhög panel", [
        ("BRA", "Distrikten är chips, inte en rullgardin, så flera kan väljas och man ser vad man "
                "valt."),
        ("BRA", "”Inget valt betyder alla distrikt” förklarar tomt läge i stället för att "
                "låta det gissas."),
        ("OLOGISKT", "Krysset ligger ovanpå texten under DISTRIKT. Samma kollision återkommer i "
                     "fem andra paneler — det är ett mönsterfel, inte ett stavfel."),
        ("OLOGISKT", "Knappen ”Visa tävlingar” ligger dikt an mot fältet TÄVLINGSNIVÅ "
                     "utan luft, och täcker innehåll i halvhögt läge."),
        ("FÖRSLAG", "Lägg krysset i panelens egen rubrikrad ovanför innehållet och res knappen på "
                    "en egen botten-yta med skugga, så vet man alltid vad som är knapp och vad "
                    "som är innehåll."),
    ]),

    ("15-filter-full.png", "Filter, helskärm", "Tävlingar → Filter · fullhöjd", [
        ("BRA", "Hela filtret får plats: tidsintervall, avstånd, nivå och distrikt utan att något "
                "göms bakom en flik."),
        ("OLOGISKT", "Längst ner står ”Datumintervall, distrikt, restid och serie kommer "
                     "när” — meningen är avhuggen mitt i, och tre av de fyra sakerna den "
                     "lovar finns redan på samma skärm."),
        ("OLOGISKT", "Knappen täcker meningens andra rad, så texten går inte ens att läsa klart."),
        ("FÖRSLAG", "Ta bort den kvarglömda färdplansraden. Om något faktiskt saknas, skriv bara "
                    "det som saknas."),
    ]),

    ("16-tavlingsdetalj.png", "Tävlingssidan", "Tävlingar → Trimtex Cup #4", [
        ("BRA", "Kartan först. En tävling är en plats innan den är något annat, och arenan är "
                "utmärkt med orienteringsflaggan."),
        ("BRA", "”Resa ~20 min” i lila — samma lila som betyder ”modellerat, ej "
                "observerat” i designsystemet. Uppskattningar ser ut som uppskattningar."),
        ("OLOGISKT", "Kartans nedre halva är vit. Rutorna renderas inte klart, och felet syns "
                     "kvar efter att sidan rullats."),
        ("OLOGISKT", "”torsdag 20:e aug” — ordningstalsformen används inte om datum "
                     "på svenska. Det heter torsdag 20 augusti."),
        ("FÖRSLAG", "Rita om kartan när vyn fått sin slutliga höjd (Mapsui behöver en "
                    "storleksändring innan den ritar färdigt). Byt datumformateringen till "
                    "”torsdag 20 aug” överallt."),
    ]),

    ("17-tavlingsdetalj-2.png", "Tävlingssidan, längre ner", "Tävlingar → Trimtex Cup #4 · rullad", [
        ("BRA", "Snabbhandlingarna gråas ut när de inte går att göra — Live och Resultat finns "
                "inte för en tävling som inte sprungits."),
        ("BRA", "PM ligger under DOKUMENT med publiceringsdatum och en pil som säger att det öppnas "
                "utanför appen."),
        ("OLOGISKT", "Ingenting säger *varför* Live och Resultat är släckta. En grå knapp utan "
                     "förklaring läser som en trasig knapp."),
        ("OLOGISKT", "Kartan är fortfarande halvt vit efter rullning — samma fel som ovan, nu "
                     "bekräftat som ihållande och inte ett laddningsögonblick."),
        ("FÖRSLAG", "Skriv ut villkoret under de släckta knapparna: ”finns på tävlingsdagen”."),
    ]),

    ("41-dm-medel.png", "Tävlingssidan för ett mästerskap", "Hem → DM, medel, Gästrikland", [
        ("BRA", "Här ritas kartan helt — arena, parkering och vägnamn. Felet på förra sidan är "
                "alltså inte konstant utan uppträder ibland, vilket gör det svårare att upptäcka."),
        ("BRA", "Rubrikraden MEDEL · MÄSTERSKAP · GÄSTRIKLAND ger disciplin, status och distrikt "
                "innan man läst namnet."),
        ("OLOGISKT", "”första start 00:00”. Starttiden är inte satt än, och saknad tid "
                     "renderas som midnatt."),
        ("OLOGISKT", "Huvudknappen heter ”Visa tävling” — på tävlingens egen sida. "
                     "Kortet ovanför säger samtidigt att anmälan stänger om 9 dagar."),
        ("FÖRSLAG", "Dölj starttiden tills den finns (”starttid ej satt”), och låt knappen "
                    "spegla nästa steg: ”Anmäl dig” före deadline, annars ingen knapp alls."),
    ]),

    ("42-dm-medel-faltet.png", "Fältet enligt Sverigelistan", "DM, medel, Gästrikland · rullad", [
        ("BRA", "”2 anmälda i H21. Startlistan är inte lottad än.” — ett tomt läge som "
                "säger både hur mycket data som finns och varför det inte finns mer."),
        ("OLOGISKT", "Sektionen heter FÄLTET ENLIGT SVERIGELISTAN och innehåller två löpare som "
                     "båda står ”utan ranking”, med tankstreck i båda sifferkolumnerna. "
                     "Rubriken lovar precis det som saknas."),
        ("FÖRSLAG", "Byt rubrik till ”Anmälda i din klass” när ingen i fältet har ranking, "
                    "och lägg tillbaka rankingrubriken först när det finns en siffra att sortera på."),
    ]),

    ("18-valj-klass.png", "Välj klass", "Tävlingssidan → Klass", [
        ("BRA", "Klassvalet ligger på tävlingen, inte i profilen — man anmäler sig i andra "
                "klasser än sin normala, och appen tvingar inte in en."),
        ("OLOGISKT", "Listan börjar på D16 och räknar nedåt. Varken den valda klassen (Blå 3,5) "
                     "eller löparens egna Eventor-klasser (H21, H40) står överst."),
        ("OLOGISKT", "Ingen rad är markerad som vald, trots att kortet bakom panelen visar "
                     "”BLÅ 3,5”."),
        ("OLOGISKT", "Den orange knappen bakom panelen lyser igenom det genomskinliga huvudet och "
                     "lägger ett suddigt band bakom förklaringstexten."),
        ("FÖRSLAG", "Sortera med vald klass först, därefter löparens Eventor-klasser, därefter "
                    "resten; markera den valda med bock."),
    ]),

    ("19-valj-klass-scroll.png", "Välj klass, längst ner", "Tävlingssidan → Klass · rullad", [
        ("OLOGISKT", "”Blå 3,5” — klassen löparen faktiskt har — ligger sist i "
                     "listan och kräver rullning för att ens ses."),
        ("LOGISKT", "Att banklasser (Blå/Svart) och åldersklasser ligger i samma lista stämmer med "
                    "hur närtävlingar faktiskt är indelade."),
        ("FÖRSLAG", "Dela listan i två rubriker — Åldersklasser och Banor — med den valda "
                    "överst i sin grupp."),
    ]),

    ("20-anmalan.png", "Anmälan öppnas", "Tävlingssidan → Anmäl dig", [
        ("BRA", "Tekniskt fungerar det: sidan öppnas redan inloggad som Jonatan, i appens eget "
                "webbvylager, utan att be om lösenordet igen."),
        ("OLOGISKT", "Det användaren ser är en annonsbanner, en husannons, Eventors meny och en "
                     "kakruta. Appens formspråk försvinner helt vid det mest avgörande steget."),
        ("OLOGISKT", "Själva formuläret ligger långt under vikningen. Man måste rulla förbi "
                     "reklamen för att komma till det man tryckte på knappen för."),
        ("FÖRSLAG", "Injicera CSS i webbvyn som döljer sidhuvud, meny och annonsytor, och rulla "
                    "direkt till formulärets ankare när sidan laddat. Rubriken ”Anmälan” "
                    "finns redan — låt innehållet under den bara vara anmälan."),
    ]),

    ("22-anmalan-form.png", "Anmälningsformuläret", "Anmälan · formuläret", [
        ("BRA", "Namn, klubb och bricknummer är redan ifyllda ur Eventor-kontot. Inget skrivs om."),
        ("OLOGISKT", "Klassrutan står på ”Insk. 2,0” — den första i Eventors lista. "
                     "Klassen appen just visade (Blå 3,5) följer inte med in i formuläret."),
        ("OLOGISKT", "Formuläret är bredare än skärmen; bricknumret är avskuret i högerkanten."),
        ("FÖRSLAG", "Skicka med den valda klassen i anmälnings-URL:en eller förvälj den i "
                    "rullgardinen via skript. Annars är klassvalet i appen ett steg som inte "
                    "leder någonstans."),
    ]),

    ("21-anmalan-formular.png", "Nedanför anmälan", "Anmälan · rullad förbi formuläret", [
        ("OLOGISKT", "Under formuläret ligger fem annonsblock i rad. Det är sista intrycket av "
                     "appens viktigaste flöde."),
        ("LOGISKT", "Att låna Eventors eget formulär är rätt val: appen slipper hantera "
                    "lösenord, avgifter och regeländringar, och anmälan blir alltid giltig."),
        ("FÖRSLAG", "Stäng webbvyn automatiskt när Eventor bekräftat anmälan, och visa "
                    "bekräftelsen i appens egen form. Då slutar flödet i Orientera i stället för "
                    "i en annonsvägg."),
    ]),

    # ---------------------------------------------------------------- Live

    ("23-live.png", "Live laddar", "Fliken Live", [
        ("OLOGISKT", "Snurran ritas ovanpå tävlingsnamnet — ”Motionsorientering Tuve” "
                     "får en snurra mitt i ordet."),
        ("LOGISKT", "Att tävlingen, färskheten och urvalet (Favoriter / Min klass) står överst är "
                    "rätt ordning för en live-vy."),
        ("FÖRSLAG", "Ge snurran en egen rad, eller visa skelettrader i tabellen i stället."),
    ]),

    ("24-live-laddad.png", "Live utan data", "Fliken Live · efter laddning", [
        ("OLOGISKT", "”Ingen anslutning. Live behöver nätverk” — men appen är online. "
                     "Alla andra flikar hämtade data från backend under samma minut. Felet "
                     "beskrivs som ett nätverksfel oavsett vad som faktiskt gick fel."),
        ("OLOGISKT", "Tävlingsväljaren och urvalschipsen försvinner i tomt läge (de är bundna till "
                     "HasLive). Just när man skulle vilja byta tävling går det inte."),
        ("BRA", "Meningen berättar vad som fortfarande fungerar — starttider finns sparade på "
                "tävlingssidan — i stället för att bara konstatera ett fel."),
        ("FÖRSLAG", "Skilj på tre lägen: ingen tävling vald, ingen tävling pågår, och anslutningen "
                    "svarar inte. Låt rubrikraden med tävlingsväljaren stå kvar i alla tre."),
    ]),

    # ---------------------------------------------------------------- Resultat

    ("25-resultat.png", "Resultat laddar", "Fliken Resultat", [
        ("OLOGISKT", "Fliken stod tom med enbart en snurra i över fyra sekunder. Inget säger vad "
                     "som hämtas eller hur mycket som är kvar."),
        ("FÖRSLAG", "Visa skelettkort i listans form medan den hämtas — då byter sidan inte "
                    "utseende när datan kommer, den fylls bara i."),
    ]),

    ("26-resultat-laddad.png", "Resultatlistan", "Fliken Resultat · laddad", [
        ("BRA", "Placeringen är radens största element och tiden ligger högerställd i tabulära "
                "siffror — kolumnerna står still mellan raderna."),
        ("BRA", "Disciplinsymbolen till vänster om datumet skiljer sprint, medel och lång utan ord."),
        ("OLOGISKT", "Varje differens är röd, också +0:55 för en andraplats. Rött betyder här "
                     "bara ”inte vinnare”."),
        ("OLOGISKT", "Ingen gruppering på säsong eller år — en enda lång lista från augusti "
                     "2026 bakåt."),
        ("FÖRSLAG", "Låt rött börja först vid en verklig förlust och håll små tapp neutrala. "
                    "Lägg in årsrubriker, och en säsongssammanfattning överst."),
    ]),

    ("27-resultat-scroll.png", "Resultatlistan, längre ner", "Fliken Resultat · rullad", [
        ("OLOGISKT", "Klasskolumnen blandar klasser och banor: H21, H45, ”Svår”, "
                     "”Blå 4 km”. Fyra begrepp i samma position."),
        ("OLOGISKT", "Löparen har sprungit både H21 och H45 samma säsong utan att listan "
                     "kommenterar det — samma sak dyker upp igen i profilen."),
        ("FÖRSLAG", "Normalisera fältet till klass när klass finns och till bana annars, och märk "
                    "banorna som banor (”bana: Blå 4 km”)."),
    ]),

    ("04-resultatdetalj-laddning.png", "Resultatsidan medan den laddar", "Hem → Mitt resultat", [
        ("OLOGISKT", "Under laddningen står ”Inget resultat ännu” — tomt läge visas "
                     "innan appen vet om det finns data. Fyra sekunder senare fanns hela resultatet."),
        ("OLOGISKT", "Snurran ritas ovanpå den texten, så de två motsäger varandra samtidigt."),
        ("FÖRSLAG", "Visa aldrig tomt läge medan en hämtning pågår. Tomt läge är ett svar, inte "
                    "ett väntrum."),
    ]),

    ("05-resultatdetalj.png", "Resultatsidan", "Hem → Mitt resultat · Översikt", [
        ("BRA", "Tre flikar för tre frågor: vad blev det, hur gick det per sträcka, varför blev "
                "det så."),
        ("BRA", "”Placering 33 / 34” och ”Efter vinnaren +50:28” står som "
                "faktarader, utan värdering."),
        ("LOGISKT", "Att tävlingens namn ligger i rubrikraden och flikarna direkt under gör att "
                    "man aldrig tappar vilken tävling man tittar på."),
        ("FÖRSLAG", "Lägg klassen (H21) bredvid placeringen — 33 av 34 betyder olika saker i "
                    "olika klasser."),
    ]),

    ("06-resultat-strackor.png", "Sträckor", "Resultatsidan → Sträckor", [
        ("BRA", "Sträcktabellen är den mest informationstäta vyn i appen och håller ändå ihop: "
                "kontroll, tid, placering, totalt och tapp på en rad."),
        ("BRA", "”trolig bom” i lila markerar tolkade sträckor, samma lila som all annan "
                "modellerad data."),
        ("OLOGISKT", "Kolumnrubrikerna STR, TOT och TAPP förklaras ingenstans."),
        ("FÖRSLAG", "Skriv ut rubrikerna eller lägg en informationsknapp som förklarar dem en gång."),
    ]),

    ("07-resultat-analys.png", "Analys", "Resultatsidan → Analys", [
        ("BRA", "Sammanfattningen är märkt ”Sammanfattad av AI” och texten säger själv "
                "att bomtiden är beräknad, inte uppmätt. Det är rätt sätt att presentera modellerad "
                "data."),
        ("OLOGISKT", "Siffrorna går isär: Översikt säger 33 / 34, analystexten säger ”33:e "
                     "plats av 38 startande” och avslutar med att löparen ”gled ner till "
                     "34:e plats i mål”. Tre olika fältstorlekar och två olika placeringar för "
                     "samma lopp."),
        ("OLOGISKT", "”Stabilitet 0,36” — ett tal utan enhet, skala eller riktning."),
        ("FÖRSLAG", "Mata sammanfattningen med samma siffror som Översikt hämtar, och skriv "
                    "stabilitet som ord eller med skala (”jämn / ojämn”, eller "
                    "”0,36 av 1”)."),
    ]),

    ("08-resultat-analys-scroll.png", "Analys, längre ner", "Resultatsidan → Analys · rullad", [
        ("BRA", "”Bomtiden är beräknad, inte uppmätt” står som brödtext, inte som "
                "finstilt — den viktigaste reservationen får den plats den förtjänar."),
        ("LOGISKT", "Att jämförelseknappen ligger sist, efter analysen, följer läsordningen: först "
                    "förstå sitt eget lopp, sedan jämföra."),
        ("FÖRSLAG", "Knappen ser ut som en knapp men är en väljare som öppnar en lista. Ge den ett "
                    "chevron eller kalla den ”Välj någon att jämföra med”."),
    ]),

    ("09-jamfor.png", "Jämför löpare", "Analys → Jämför", [
        ("BRA", "Fältet är sorterat på placering med tider, så man kan välja jämförelse på "
                "verkliga tal i stället för på namn."),
        ("OLOGISKT", "Krysset ligger ovanpå förklaringstexten — samma kollision som i filtret, "
                     "klassvalet, notiserna och identitetsrutan."),
        ("FÖRSLAG", "Åtgärda krysset en gång i panelmallen, så försvinner felet från sex vyer "
                    "samtidigt."),
    ]),

    ("10-jamfor-resultat.png", "Efter valet", "Analys · vald jämförelse", [
        ("LOGISKT", "Valet stannar kvar som knappens etikett — ”Jämför med Kent "
                    "Ohlsson” — så man ser vem jämförelsen gäller."),
        ("OLOGISKT", "Knappen såg likadan ut före valet, då den hette samma sak (vinnaren var "
                     "förvald). Ingenting säger att ett val faktiskt registrerades."),
        ("FÖRSLAG", "Lägg jämförelsens namn som rubrik över tabellen i stället för i knappen."),
    ]),

    ("11-jamforelse.png", "Jämförelsen", "Analys · sträcka för sträcka", [
        ("BRA", "Sträcka för sträcka med differens per kontroll är precis den analys en löpare gör "
                "på parkeringen efteråt."),
        ("OLOGISKT", "Tabellen har inga kolumnrubriker. Tre tidskolumner utan förklaring — "
                     "vilken är min och vilken är hans?"),
        ("FÖRSLAG", "Sätt en rubrikrad med ”Jonatan / Kent / Diff”, gärna klistrad överst "
                    "vid rullning."),
    ]),

    # ---------------------------------------------------------------- Jag

    ("28-jag.png", "Jag", "Fliken Jag", [
        ("BRA", "Sverigelistan visas med poäng, tre placeringar och per gren — tätt utan att "
                "bli trångt."),
        ("BRA", "”Ett räknande resultat faller ur 19 sep.” är en varning som faktiskt går "
                "att agera på."),
        ("OLOGISKT", "Rutan säger ”Inloggad som Jonatan Söderberg” och knappen under "
                     "heter ”Logga in igen”. Det finns ingen väg ut — ingen "
                     "utloggning någonstans i appen."),
        ("OLOGISKT", "KLASS står på H21 medan Sverigelistan på samma skärm säger ”204:e i "
                     "H45”. Två klasser för samma person, en skärmhöjd isär."),
        ("OLOGISKT", "Knappen ”Logga in igen” ger inget synligt svar när man trycker "
                     "— inloggningspanelen öppnas och stängs direkt, eftersom Eventor "
                     "fortfarande känner igen sessionen. Testat tre gånger."),
        ("FÖRSLAG", "Lägg till ”Logga ut”. Visa vilken klass som är löparens Eventor-klass "
                    "och vilken som är appens val, eller använd bara en. Och låt ”Logga in "
                    "igen” säga ”Inloggningen gäller fortfarande” i stället för att "
                    "blinka förbi."),
    ]),

    ("30-jag-mitten.png", "Räknande resultat och klubbaktiviteter", "Fliken Jag · rullad", [
        ("BRA", "De sex resultat som räknas in i snittet listas med poäng var för sig — "
                "rankingen blir förklarad i stället för bara påstådd."),
        ("BRA", "”faller ur 19 sep.” står på raden det gäller, inte bara som en varning "
                "högre upp."),
        ("OLOGISKT", "Klubbaktiviteterna använder tre datumformat på fyra rader: ”stänger "
                     "söndag”, ”sön 30 aug.”, ”ons 10 feb. 2027”."),
        ("OLOGISKT", "Raderna går inte att trycka på och leder inte till tävlingen."),
        ("FÖRSLAG", "Ett format: veckodag + datum, år bara när det inte är innevarande. Gör raderna "
                    "till länkar till respektive tävling."),
    ]),

    ("29-jag-scroll.png", "Jag, längst ner", "Fliken Jag · botten", [
        ("BRA", "Notisinställningar, favoriter och utvecklingsläge ligger var för sig med tydliga "
                "rubriker."),
        ("OLOGISKT", "UTVECKLINGSLÄGE med Tidsmaskin och Designsystem ligger i den vanliga "
                     "profilvyn, tillsammans med en rad om vilken backend appen kör mot."),
        ("LOGISKT", "Att raden om backend står där är rätt så länge appen är i test — den "
                    "svarar på frågan ”varför ser jag den här datan”."),
        ("FÖRSLAG", "Sätt hela avsnittet bakom #if DEBUG innan appen släpps, så kan det ligga kvar "
                    "utan att någon glömmer det."),
    ]),

    ("39-vem-ar-du.png", "Vem är du?", "Jag → Klass → Välj", [
        ("BRA", "”Namn och klubb kommer från din Eventor-inloggning och uppdateras därifrån. "
                "Klassen väljer du själv.” — tre rader som förklarar hela "
                "ägarskapsmodellen."),
        ("OLOGISKT", "Klassen är ett fritextfält. Ingen lista, ingen validering — ”h21” "
                     "eller ”H 21” matchar sannolikt ingenting."),
        ("OLOGISKT", "Eventor känner redan löparens förvalda klasser (H21 och H40 enligt "
                     "MyPages/Settings), men de erbjuds inte här."),
        ("FÖRSLAG", "Byt fritexten mot en lista som börjar med Eventors förvalda klasser."),
    ]),

    ("31-folj-lopare.png", "Följ löpare", "Jag → Följ någon", [
        ("OLOGISKT", "Sökfältet renderas som en svart låt i ljust tema — en ostilad SearchBar "
                     "som inte följer temat."),
        ("OLOGISKT", "Fältet tog inte emot fokus: tre tryck, inget tangentbord, ingen text. Sök "
                     "gick inte att använda."),
        ("OLOGISKT", "Förslagen är tre löpare ur Falkenbergs OK i bokstavsordning — varken "
                     "löparens egen klubb, distrikt eller senaste motståndare."),
        ("BRA", "”Favoriter är din egen lista. Ingen får veta att du följer dem.” svarar "
                "på integritetsfrågan innan den ställs."),
        ("FÖRSLAG", "Ge SearchBar samma stil som resten av appen och kontrollera fokus på iOS. "
                    "Föreslå klubbkamrater och löpare ur senaste resultatlistor först."),
    ]),

    ("32-notiser.png", "Notiser", "Jag → Vad du vill bli notifierad om", [
        ("BRA", "”Notiserna ligger i telefonen och behöver ingen inloggning” — en "
                "teknisk sanning som också är ett integritetslöfte."),
        ("BRA", "Varje växel har en mening som förklarar exakt när notisen kommer."),
        ("OLOGISKT", "Krysset ligger ovanpå sista ordet i den meningen."),
        ("FÖRSLAG", "Samma panelrubrik-fix som på övriga paneler."),
    ]),

    ("33-notiser-full.png", "Notiser, alla sex", "Notiser · fullhöjd", [
        ("BRA", "Sex notistyper, alla avstängda från start. Ingen slås på åt användaren."),
        ("LOGISKT", "Ordningen följer tävlingens gång: anmälan → PM → starttid → avfärd → live → "
                    "resultat."),
        ("OLOGISKT", "Ingenting nämner iOS egen notisbehörighet. Slår man på en växel utan att ha "
                     "gett appen tillstånd händer ingenting, utan förklaring."),
        ("FÖRSLAG", "Be om systemtillståndet vid första påslaget, och visa en rad överst om "
                    "tillståndet saknas."),
    ]),

    # ---------------------------------------------------------------- Utvecklingsläge

    ("02-tidsmaskin.png", "Tidsmaskinen", "Jag → Tid", [
        ("BRA", "Att kunna flytta appens ”nu” gör hela tävlingens livscykel testbar utan "
                "att vänta på kalendern."),
        ("OLOGISKT", "Panelen säger OFFLINE och ”Tävlingen kunde inte hämtas” medan appen "
                     "är online — knappen bredvid heter ”Simulera offline”, vilket "
                     "bevisar att offlineläget är avstängt."),
        ("OLOGISKT", "Orsaken är att panelen är fast knuten till en tävling ur demodatat "
                     "(FakeDataset.NmLongId) som inte finns i den riktiga backenden. Ett saknat "
                     "id rapporteras som ett nätverksfel."),
        ("FÖRSLAG", "Låt tidsmaskinen följa den tävling som är närmast i tiden i den aktuella "
                    "datakällan, och skilj ”hittades inte” från ”offline”."),
    ]),

    ("03-tidsmaskin-full.png", "Tidsmaskinen, helskärm", "Jag → Tid · fullhöjd", [
        ("OLOGISKT", "Rubriken TÄVLINGSRESAN har ingenting under sig — en tom sektion utan "
                     "tomt läge, eftersom hållplatserna byggs ur tävlingen som inte kunde hämtas."),
        ("BRA", "Knapparna ”Simulera offline” och ”Tillbaka till nu” är tydligt "
                "namngivna och går att ångra."),
        ("FÖRSLAG", "Skriv ut varför resan är tom, eller dölj rubriken när det inte finns "
                    "hållplatser."),
    ]),

    ("34-designsystem.png", "Designsystemet", "Jag → Designsystem", [
        ("BRA", "Appen har ett dokumenterat teckenspråk för färg: AccentAction för handling, "
                "PositiveDelta/NegativeDelta för vinst och tapp, EstimateInk för ”modellerat, "
                "ej observerat”, MapInk för kartidentitet."),
        ("BRA", "Att lila = modellerat hålls konsekvent i restid, bomtid och trolig bom är den "
                "starkaste designidén i hela appen."),
        ("LOGISKT", "Att sidan finns i appen och inte bara i en fil gör att tokens kan granskas i "
                    "verkligt ljus, på verklig skärm."),
        ("FÖRSLAG", "Lägg till länk- och rubrikfärg i tabellen — det är just de som i dag "
                    "hamnat på systemets blå i stället för på en egen token."),
    ]),

    ("35-designsystem-2.png", "Komponenter", "Designsystem · rullad", [
        ("BRA", "Demonstrationen av tabulära siffror — fel till vänster, rätt till höger — "
                "visar varför valet gjordes, inte bara att det gjordes."),
        ("OLOGISKT", "Statusmärkena här är ANMÄLD, LIVE, PM och GRUPP. I tävlingslistan heter de "
                     "UPPTÄCKT och ANMÄLAN ÖPPEN. Designsystemet och appen talar olika språk."),
        ("FÖRSLAG", "Ta in listans faktiska märken i designsystemet, eller ta bort dem ur listan. "
                    "En av de två uppsättningarna är övertalig."),
    ]),

    ("36-designsystem-morkt.png", "Designsystemet i mörkt läge", "Designsystem · mörkt tema", [
        ("BRA", "Varje token har ett eget mörkt värde — orangen ljusnar, grönt ljusnar, ytorna "
                "skiktas i stället för att bara bli grå."),
        ("OLOGISKT", "PM-märket blir orange text på mörkbrunt. Det är den svagaste kontrasten i "
                     "hela paletten och klarar sannolikt inte 4,5:1."),
        ("FÖRSLAG", "Mät PM-märket mot WCAG AA och ljusa upp texten eller mörka ner plattan."),
    ]),
]

# Vyer som inte gick att nå under körningen.
NOT_REACHED = [
    ("Välj tävling (Live)", "Väljaren är bunden till HasLive och göms i exakt det läge där man "
                            "skulle vilja byta tävling. Gick inte att öppna."),
    ("Om prognosen", "Panelen öppnas bara när appen räknat fram en prognos. Ingen av tävlingarna i "
                     "datat hade en."),
    ("Logga in med Eventor-konto (AppLoginSheet)", "Panelen finns i koden men OpenAppLoginCommand "
                                                   "är inte bunden till någon knapp i något XAML. "
                                                   "Den går inte att nå från appen — och kan "
                                                   "därmed inte utvärderas mot den andra "
                                                   "inloggningsvägen, vilket var hela syftet med "
                                                   "att behålla båda."),
    ("Logga in på Eventor (EventorLoginSheet)", "Öppnades och stängdes omedelbart både från "
                                                "profilen och från välkomstvyn, eftersom Eventor "
                                                "hälsar den sparade sessionen vid namn och panelen "
                                                "stänger sig när den ser hälsningen."),
]
