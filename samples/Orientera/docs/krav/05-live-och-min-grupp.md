# 5. Live och Min grupp

## Live

LiveResults har ett **publikt JSON-API utan autentisering, med hashstöd och 15 sekunders cache** [K2]. Det gör tjänsten väl lämpad som live-källa, förutsatt att Orientera matchar rätt Eventor-event mot rätt LiveResults-tävling (spike SP-04).

- **Följ mig** som default.
- Växla till **Min grupp, min klass, klubb eller alla**.
- Favoritmarkera person direkt från startlista/resultat/live.
- **Live nu lyfts automatiskt till Hem** när något relevant pågår.
- Polling respekterar 15-sekunders cache och använder hash för att minimera datatrafik.

## Min grupp

Min grupp är **inte ett socialt nätverk**. Det är en lokal/personlig lista över personer vars orientering användaren vill följa: barn, familj, vänner, klubbkompisar eller andra favoriter.

| Datatyp | Användning |
|---------|------------|
| Starttid | Samlad familje-/gruppvy före lopp. |
| Live | Flera klasser kan följas samtidigt. |
| Resultat | Senaste resultat från favoriter kan dyka upp på Hem. |
| Tävling | Event kan få högre relevans om någon i gruppen är anmäld. |
| Notiser | Opt-in per person eller grupp. |
