# 12. Icke-funktionella krav

| Område | Krav |
|--------|------|
| Prestanda | Snabb första render, virtualiserade listor, ingen onödig polling, cachead data. |
| Robusthet | Dålig mobiltäckning får inte slå ut kritisk tävlingsinformation. |
| Säkerhet | API-hemligheter aldrig i mobilklient; minimera känsliga personuppgifter. |
| Integritet | Följda personer och GPS hanteras transparent; endast data användaren har rätt till. |
| Tillgänglighet | Dynamisk text där rimligt, god kontrast, tydliga touch targets, VoiceOver/TalkBack på kärnflöden. |
| Tema | Light + Dark, systemtema som default. |
| Språk | Svenska initialt; resurser struktureras så lokalisering kan läggas till senare. |
| Testbarhet | Domänlogik för relevans, grouping, prediction och analys ska vara unit-testbar utan UI. |
| Observability | Strukturerad loggning/telemetri för integrationer, cache, parsing och fel. |
| Fallback | Om en integration saknas ska appen degradera till länk/deep-link i stället för att blockera flödet. |
