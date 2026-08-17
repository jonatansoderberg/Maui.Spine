# -*- coding: utf-8 -*-
"""Bygger testkörningsrapporten som PDF."""

import os
from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.platypus import (BaseDocTemplate, Frame, Image, KeepTogether, PageBreak,
                                PageTemplate, Paragraph, Spacer, Table, TableStyle)

import manifest

HERE = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.join(HERE, "shots")
OUT = os.path.join(HERE, "Orientera-testkorning-2026-08-17.pdf")

INK = colors.HexColor("#1A1A1A")
MUTED = colors.HexColor("#6B6B6B")
LINE = colors.HexColor("#DDDAD5")
BRAND = colors.HexColor("#C24A0C")

TAGS = {
    "BRA": colors.HexColor("#1E7A3C"),
    "LOGISKT": colors.HexColor("#1B5FA8"),
    "OLOGISKT": colors.HexColor("#C0392B"),
    "FÖRSLAG": colors.HexColor("#B4530A"),
}

MARGIN = 16 * mm
PAGE_W, PAGE_H = A4
BODY_W = PAGE_W - 2 * MARGIN

S = {
    "title": ParagraphStyle("title", fontName="Helvetica-Bold", fontSize=30, leading=34,
                            textColor=INK),
    "subtitle": ParagraphStyle("subtitle", fontName="Helvetica", fontSize=13, leading=18,
                               textColor=MUTED),
    "h1": ParagraphStyle("h1", fontName="Helvetica-Bold", fontSize=17, leading=21, textColor=INK,
                         spaceAfter=2),
    "h2": ParagraphStyle("h2", fontName="Helvetica-Bold", fontSize=12.5, leading=16,
                         textColor=INK, spaceBefore=8, spaceAfter=3),
    "where": ParagraphStyle("where", fontName="Helvetica", fontSize=8.5, leading=11,
                            textColor=MUTED, spaceAfter=6),
    "body": ParagraphStyle("body", fontName="Helvetica", fontSize=9.5, leading=13.5,
                           textColor=INK, alignment=TA_LEFT),
    "lead": ParagraphStyle("lead", fontName="Helvetica", fontSize=10.5, leading=15.5,
                           textColor=INK, spaceAfter=7),
    "tag": ParagraphStyle("tag", fontName="Helvetica-Bold", fontSize=6.6, leading=8.4,
                          textColor=colors.white, alignment=1),
    "cap": ParagraphStyle("cap", fontName="Helvetica", fontSize=5.6, leading=6.8,
                          textColor=MUTED, alignment=1),
    "foot": ParagraphStyle("foot", fontName="Helvetica", fontSize=8, leading=11, textColor=MUTED),
}


def shot(name):
    return os.path.join(SHOTS, name)


def phone(name, width_mm):
    """Skärmdumpen skalad till angiven bredd, med ram."""
    w = width_mm * mm
    h = w * 2622.0 / 1206.0
    img = Image(shot(name), width=w, height=h)
    t = Table([[img]], colWidths=[w], rowHeights=[h])
    t.setStyle(TableStyle([
        ("BOX", (0, 0), (-1, -1), 0.6, LINE),
        ("LEFTPADDING", (0, 0), (-1, -1), 0),
        ("RIGHTPADDING", (0, 0), (-1, -1), 0),
        ("TOPPADDING", (0, 0), (-1, -1), 0),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
    ]))
    return t


def notes_table(notes, width):
    """Kommentarerna som taggad lista."""
    rows, style = [], [
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0),
        ("RIGHTPADDING", (0, 0), (0, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ]
    for i, (tag, text) in enumerate(notes):
        chip = Table([[Paragraph(tag, S["tag"])]], colWidths=[19 * mm], rowHeights=[7.2 * mm])
        chip.setStyle(TableStyle([
            ("BACKGROUND", (0, 0), (-1, -1), TAGS[tag]),
            ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
            ("LEFTPADDING", (0, 0), (-1, -1), 1),
            ("RIGHTPADDING", (0, 0), (-1, -1), 1),
            ("TOPPADDING", (0, 0), (-1, -1), 0),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
            ("ROUNDEDCORNERS", [2, 2, 2, 2]),
        ]))
        rows.append([chip, Paragraph(text, S["body"])])
        if i:
            style.append(("LINEABOVE", (0, i), (-1, i), 0.4, LINE))
    t = Table(rows, colWidths=[19 * mm, width - 19 * mm])
    t.setStyle(TableStyle(style))
    return t


def view_page(filename, heading, where, notes):
    img_w = 80
    col = BODY_W - img_w * mm - 6 * mm
    right = [Paragraph(heading, S["h1"]), Paragraph(where, S["where"]),
             notes_table(notes, col)]
    t = Table([[phone(filename, img_w), right]],
              colWidths=[img_w * mm + 1 * mm, col])
    t.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0),
        ("RIGHTPADDING", (0, 0), (0, -1), 6 * mm),
        ("RIGHTPADDING", (1, 0), (1, -1), 0),
        ("TOPPADDING", (0, 0), (-1, -1), 0),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 0),
    ]))
    return t


def contact_sheet(views, cols=6, rows=4):
    """Vykartan: alla vyer som miniatyrer."""
    pages, cell = [], []
    w = (BODY_W - (cols - 1) * 3 * mm) / cols
    for filename, heading, _where, _notes in views:
        img_h = w * 2622.0 / 1206.0
        img = Image(shot(filename), width=w, height=img_h)
        c = Table([[img], [Paragraph(heading, S["cap"])]], colWidths=[w])
        c.setStyle(TableStyle([
            ("BOX", (0, 0), (0, 0), 0.5, LINE),
            ("LEFTPADDING", (0, 0), (-1, -1), 0),
            ("RIGHTPADDING", (0, 0), (-1, -1), 0),
            ("TOPPADDING", (0, 0), (0, 0), 0),
            ("BOTTOMPADDING", (0, 0), (0, 0), 0),
            ("TOPPADDING", (0, 1), (0, 1), 2),
            ("BOTTOMPADDING", (0, 1), (0, 1), 0),
        ]))
        cell.append(c)

    per_page = cols * rows
    for start in range(0, len(cell), per_page):
        chunk = cell[start:start + per_page]
        grid = [chunk[i:i + cols] for i in range(0, len(chunk), cols)]
        grid[-1] += [""] * (cols - len(grid[-1]))
        t = Table(grid, colWidths=[w + 3 * mm] * cols)
        t.setStyle(TableStyle([
            ("VALIGN", (0, 0), (-1, -1), "TOP"),
            ("LEFTPADDING", (0, 0), (-1, -1), 0),
            ("RIGHTPADDING", (0, 0), (-1, -1), 3 * mm),
            ("TOPPADDING", (0, 0), (-1, -1), 0),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 5 * mm),
        ]))
        pages.append(t)
    return pages


def decorate(canvas, doc):
    canvas.saveState()
    if doc.page > 1:
        canvas.setFont("Helvetica", 7.5)
        canvas.setFillColor(MUTED)
        canvas.drawString(MARGIN, 10 * mm, "Orientera — testkörning 17 augusti 2026")
        canvas.drawRightString(PAGE_W - MARGIN, 10 * mm, str(doc.page))
        canvas.setStrokeColor(LINE)
        canvas.setLineWidth(0.4)
        canvas.line(MARGIN, 13 * mm, PAGE_W - MARGIN, 13 * mm)
    canvas.restoreState()


def build():
    doc = BaseDocTemplate(OUT, pagesize=A4,
                          leftMargin=MARGIN, rightMargin=MARGIN,
                          topMargin=MARGIN, bottomMargin=18 * mm,
                          title="Orientera — testkörning 17 augusti 2026",
                          author="Testkörning i iOS-simulator")
    frame = Frame(MARGIN, 18 * mm, BODY_W, PAGE_H - MARGIN - 18 * mm, id="body",
                  leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
    doc.addPageTemplates([PageTemplate(id="all", frames=[frame], onPage=decorate)])

    story = []

    # ------------------------------------------------------------ Titelsida
    story += [
        Spacer(1, 30 * mm),
        Paragraph("Orientera", S["title"]),
        Paragraph("Testkörning genom hela appen", S["subtitle"]),
        Spacer(1, 8 * mm),
    ]
    facts = [
        ["Datum", "17 augusti 2026"],
        ["Enhet", "iPhone 17 Pro, iOS 26.2 (simulator)"],
        ["Byggnation", "net10.0-ios, Debug, gren issue/123-own-session"],
        ["Datakälla", "Orientera.Backend på http://localhost:7071/api/"],
        ["Läge", "Inloggad på Eventor som Jonatan Söderberg, Gävle OK"],
        ["Omfattning", "%d vyer, båda teman" % len(manifest.VIEWS)],
    ]
    ft = Table(facts, colWidths=[30 * mm, BODY_W - 30 * mm])
    ft.setStyle(TableStyle([
        ("FONT", (0, 0), (0, -1), "Helvetica-Bold", 9.5),
        ("FONT", (1, 0), (1, -1), "Helvetica", 9.5),
        ("TEXTCOLOR", (0, 0), (0, -1), MUTED),
        ("TEXTCOLOR", (1, 0), (1, -1), INK),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0),
        ("TOPPADDING", (0, 0), (-1, -1), 3),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
        ("LINEBELOW", (0, 0), (-1, -2), 0.4, LINE),
    ]))
    story += [ft, Spacer(1, 10 * mm)]
    legend = Table([[Paragraph(t, S["tag"]), Paragraph(d, S["body"])] for t, d in [
        ("BRA", "fungerar och är värt att behålla"),
        ("LOGISKT", "följer hur en löpare faktiskt tänker"),
        ("OLOGISKT", "säger emot sig självt, vilseleder eller går sönder"),
        ("FÖRSLAG", "konkret förbättring"),
    ]], colWidths=[19 * mm, BODY_W - 19 * mm])
    legend.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (0, 0), TAGS["BRA"]),
        ("BACKGROUND", (0, 1), (0, 1), TAGS["LOGISKT"]),
        ("BACKGROUND", (0, 2), (0, 2), TAGS["OLOGISKT"]),
        ("BACKGROUND", (0, 3), (0, 3), TAGS["FÖRSLAG"]),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("LEFTPADDING", (0, 0), (0, -1), 1),
        ("RIGHTPADDING", (0, 0), (0, -1), 1),
        ("LEFTPADDING", (1, 0), (1, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
    ]))
    story += [Paragraph("Så läses kommentarerna", S["h2"]), legend, PageBreak()]

    # ------------------------------------------------------------ Sammanfattning
    story.append(Paragraph("Sammanfattning", S["h1"]))
    story.append(Spacer(1, 3 * mm))
    for p in SUMMARY_LEAD:
        story.append(Paragraph(p, S["lead"]))

    story.append(Paragraph("Det som väger tyngst", S["h2"]))
    story.append(notes_table(TOP_FINDINGS, BODY_W))
    story.append(PageBreak())

    story.append(Paragraph("Det som redan bär", S["h1"]))
    story.append(Spacer(1, 3 * mm))
    story.append(notes_table(STRENGTHS, BODY_W))
    story.append(PageBreak())

    # ------------------------------------------------------------ Vykarta
    story.append(Paragraph("Vykarta", S["h1"]))
    story.append(Paragraph("Alla vyer testkörningen passerade, i den ordning de gås igenom.",
                           S["where"]))
    for i, sheet in enumerate(contact_sheet(manifest.VIEWS)):
        if i:
            story.append(PageBreak())
        story.append(sheet)
    story.append(PageBreak())

    # ------------------------------------------------------------ Vy för vy
    for filename, heading, where, notes in manifest.VIEWS:
        story.append(view_page(filename, heading, where, notes))
        story.append(PageBreak())

    # ------------------------------------------------------------ Ej nådda vyer
    story.append(Paragraph("Vyer som inte gick att nå", S["h1"]))
    story.append(Paragraph("Fyra vyer finns i koden men gick inte att öppna under körningen. "
                           "Att de inte kunde ses är i sig ett resultat.", S["where"]))
    rows = [[Paragraph("<b>%s</b>" % n, S["body"]), Paragraph(w, S["body"])]
            for n, w in manifest.NOT_REACHED]
    t = Table(rows, colWidths=[58 * mm, BODY_W - 58 * mm])
    t.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0),
        ("RIGHTPADDING", (0, 0), (0, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 6),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
        ("LINEBELOW", (0, 0), (-1, -2), 0.4, LINE),
    ]))
    story.append(t)

    story.append(Spacer(1, 10 * mm))
    story.append(Paragraph("Så gjordes körningen", S["h2"]))
    for p in METHOD:
        story.append(Paragraph(p, S["body"]))
        story.append(Spacer(1, 2 * mm))

    doc.build(story)
    print("Skrev", OUT)


SUMMARY_LEAD = [
    "Appen är längre kommen än den ser ut i en kravlista. Den har ett eget formspråk som håller "
    "genom fem flikar och två teman, ett dokumenterat färgspråk där lila konsekvent betyder "
    "”beräknat, inte uppmätt”, och en texthållning som säger obekväma sanningar rakt ut i "
    "stället för att gömma dem. Sträcktabellen, analysen och Sverigelistan är på en nivå där de "
    "tål att jämföras med det en löpare annars använder.",

    "Det som skaver är nästan aldrig utseendet. Det är att två vyer säger olika saker om samma "
    "tävling, att ett tomt läge visas medan datan fortfarande hämtas, att ett fel som inte är ett "
    "nätverksfel ändå kallas ”ingen anslutning”, och att det klassval man gör i appen inte "
    "följer med in i anmälan. Fyra av de fem tyngsta fynden är sådant: appen vet rätt sak men "
    "säger fel sak.",

    "Ett fel återkommer i sex olika vyer och är därmed värt att laga på ett ställe: panelernas "
    "kryss ligger ovanpå deras egen förklaringstext.",
]

TOP_FINDINGS = [
    ("OLOGISKT", "<b>Klassvalet dör i anmälan.</b> Appen visar ”BLÅ 3,5”, låter dig byta "
                 "klass — och Eventor-formuläret öppnas ändå på ”Insk. 2,0”, första "
                 "posten i listan. Hela klassflödet i appen leder ingenstans."),
    ("OLOGISKT", "<b>Live kallar allt för nätverksfel.</b> ”Ingen anslutning. Live behöver "
                 "nätverk” visades medan alla andra flikar hämtade data från samma backend. "
                 "Samtidigt göms tävlingsväljaren i just det läget, så man kan inte byta till en "
                 "tävling som fungerar."),
    ("OLOGISKT", "<b>Anmälan blir en annonsvägg.</b> Knappen ”Anmäl dig” öppnar Eventors "
                 "sida med banner, husannons och meny; formuläret ligger under vikningen och "
                 "under det ytterligare fem annonsblock. Inloggningen fungerar — men appens "
                 "viktigaste flöde ser inte ut som appen."),
    ("OLOGISKT", "<b>Siffrorna säger emot varandra.</b> Översikt: 33 / 34. Analystexten: "
                 "”33:e plats av 38 startande” och ”gled ner till 34:e plats i mål”. "
                 "Samma lopp, tre uppgifter. På Jag står KLASS H21 medan Sverigelistan strax under "
                 "säger ”204:e i H45”."),
    ("OLOGISKT", "<b>Tomt läge under laddning.</b> Resultatsidan visar ”Inget resultat "
                 "ännu” — med snurran ovanpå texten — i fyra sekunder innan resultatet "
                 "kommer. Samma mönster i Live och på Resultatfliken."),
    ("OLOGISKT", "<b>Det går inte att logga ut.</b> Profilen säger ”Inloggad som Jonatan "
                 "Söderberg” och erbjuder bara ”Logga in igen”, som dessutom inte ger "
                 "något synligt svar när sessionen fortfarande gäller."),
    ("OLOGISKT", "<b>Sökfältet i Följ löpare är trasigt.</b> Det renderas som en svart låda i "
                 "ljust tema och tog inte emot fokus vid tre försök — en ostilad SearchBar."),
    ("FÖRSLAG", "<b>Laga panelhuvudet en gång.</b> Krysset ligger ovanpå texten i Filter, Välj "
                "klass, Jämför löpare, Notiser, Vem är du? och Välkommen. Ett fix i panelmallen "
                "tar bort felet ur sex vyer."),
]

STRENGTHS = [
    ("BRA", "<b>Lila betyder modellerat.</b> Restid, bomtid och ”trolig bom” bär samma "
            "EstimateInk. Designsystemsidan dokumenterar det, och appen håller det. Få appar "
            "skiljer mätt från gissat alls, och ännu färre gör det med färg."),
    ("BRA", "<b>Texterna ljuger inte.</b> ”Bomtiden är beräknad, inte uppmätt”. "
            "”Sammanfattad av AI”. ”Uppgifterna sparas i telefonens säkra lager "
            "— aldrig på någon server”. ”Ingen får veta att du följer dem”. "
            "Reservationerna står i brödtext, inte i finstilt."),
    ("BRA", "<b>Hem är tre block, inte en instrumentpanel.</b> Senaste resultat, en kommande "
            "tävling, utvecklingen. Varje block har en knapp. Ingen konfiguration, ingen "
            "widgetsallad."),
    ("BRA", "<b>Sträcktabellen och analysen håller.</b> Kontroll, tid, sträckplacering, "
            "totalplacering och tapp på en rad — appens tätaste vy, och ändå läsbar. Tabulära "
            "siffror gör att kolumnerna står still mellan raderna."),
    ("BRA", "<b>Sverigelistan är förklarad, inte bara påstådd.</b> Poängen, de tre placeringarna, "
            "grenvärdena och de sex resultat som faktiskt räknas — med raden som snart faller ur "
            "utmärkt."),
    ("BRA", "<b>Mörkt läge är genomfört.</b> Egna värden per token, inte en invertering. Ytor "
            "skiktas, orangen ljusnar, hierarkin är densamma i båda temana."),
    ("BRA", "<b>Tomma lägen förklarar sig.</b> ”2 anmälda i H21. Startlistan är inte lottad "
            "än.” säger både hur mycket data som finns och varför det inte finns mer."),
    ("LOGISKT", "<b>Anmälan lånar Eventors eget formulär.</b> Appen har aldrig ett fält för "
                "lösenordet, och anmälan blir alltid giltig även när regler och avgifter ändras. "
                "Rätt beslut — det är inramningen som behöver arbete, inte valet."),
]

METHOD = [
    "Appen byggdes från grenen issue/123-own-session och installerades på en iPhone 17 Pro i "
    "simulatorn. Backenden kördes lokalt på port 7071 och svarade under hela körningen, vilket "
    "är vad som gör ”ingen anslutning”-meddelandet i Live till ett fynd och inte till ett "
    "väntat läge.",

    "Eventor-sessionen från en tidigare körning låg kvar i webbvylagret, så appen kördes som "
    "inloggad löpare — det läge där mest av appen är synlig. Inga uppgifter skrevs in någonstans "
    "och ingen anmälan skickades: anmälningsformuläret öppnades, lästes och stängdes.",

    "Välkomstvyn nåddes genom att tillfälligt ta bort first-run.json ur appens katalog och starta "
    "om; filen lades tillbaka direkt efteråt. Eventor-sessionen rördes inte.",

    "Skärmdumparna är tagna med simctl i full upplösning (1206 × 2622) och ligger sparade i "
    "docs/testrun-2026-08-17/shots/, i samma ordning som rapporten går igenom dem.",
]

if __name__ == "__main__":
    build()
