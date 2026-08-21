"""Solens läge, och årstiden, ur tävlingens datum och plats.

Tävlingsbilden ska visa det ljus löparna faktiskt får. En närtävling 18:30 i slutet
av augusti på 60° nord har solen några grader över horisonten — det är gyllene timme
på riktigt, inte som stilval.
"""
import datetime as dt
import math
import re

MONTHS = {m: i + 1 for i, m in enumerate(
    ["januari", "februari", "mars", "april", "maj", "juni",
     "juli", "augusti", "september", "oktober", "november", "december"])}


def parse_when(text, night=False):
    """'måndag 24 augusti 2026, 18:30' -> datetime.

    Saknas klockslaget antas middag — utom för nattävlingar, som får 21:00. Det är
    en gissning, men en betydligt bättre än 12:00 för ett lopp vars hela idé är
    att det är mörkt.
    """
    m = re.search(r"(\d{1,2})\s+([a-zåäö]+)\s+(\d{4})(?:.*?(\d{1,2}):(\d{2}))?", text.lower())
    if not m or m.group(2) not in MONTHS:
        return None
    d, mon, y = int(m.group(1)), MONTHS[m.group(2)], int(m.group(3))
    hh = int(m.group(4)) if m.group(4) else (21 if night else 12)
    mm = int(m.group(5)) if m.group(5) else 0
    return dt.datetime(y, mon, d, hh, mm)


def _last_sunday(year, month):
    d = dt.date(year, month, 31 if month != 4 else 30)
    while d.month != month:
        d -= dt.timedelta(days=1)
    return d - dt.timedelta(days=(d.weekday() + 1) % 7)


def utc_offset(when):
    """Svensk normaltid eller sommartid. Sommartid gäller sista söndagen i mars till
    sista söndagen i oktober."""
    start = _last_sunday(when.year, 3)
    end = _last_sunday(when.year, 10)
    return 2 if start <= when.date() < end else 1


def solar_position(lat, lon, when):
    """Solhöjd och azimut i grader. NOAA:s algoritm, förenklad men trogen på
    bågminuten — mer än nog för att sätta ljuset i en bild."""
    utc = when - dt.timedelta(hours=utc_offset(when))
    jd = (utc - dt.datetime(2000, 1, 1, 12)).total_seconds() / 86400.0
    t = jd / 36525.0

    L = math.radians((280.46646 + t * (36000.76983 + t * 0.0003032)) % 360)
    M = math.radians((357.52911 + t * (35999.05029 - 0.0001537 * t)) % 360)
    C = math.radians(math.sin(M) * (1.914602 - t * (0.004817 + 0.000014 * t))
                     + math.sin(2 * M) * (0.019993 - 0.000101 * t)
                     + math.sin(3 * M) * 0.000289)
    true_long = L + C
    omega = math.radians(125.04 - 1934.136 * t)
    app_long = true_long - math.radians(0.00569 + 0.00478 * math.sin(omega))
    eps = math.radians(23.0 + (26.0 + (21.448 - t * 46.815) / 60.0) / 60.0
                       + 0.00256 * math.cos(omega))
    decl = math.asin(math.sin(eps) * math.sin(app_long))

    y = math.tan(eps / 2) ** 2
    eot = 4 * math.degrees(
        y * math.sin(2 * L) - 2 * 0.016708634 * math.sin(M)
        + 4 * 0.016708634 * y * math.sin(M) * math.cos(2 * L)
        - 0.5 * y * y * math.sin(4 * L) - 1.25 * 0.016708634 ** 2 * math.sin(2 * M))

    mins = utc.hour * 60 + utc.minute + utc.second / 60.0
    ha = math.radians(((mins + eot + 4 * lon) / 4.0 - 180.0))

    la = math.radians(lat)
    alt = math.asin(math.sin(la) * math.sin(decl) + math.cos(la) * math.cos(decl) * math.cos(ha))
    az = math.atan2(math.sin(ha),
                    math.cos(ha) * math.sin(la) - math.tan(decl) * math.cos(la))
    return math.degrees(alt), (math.degrees(az) + 180.0) % 360.0


def season(when):
    """Årstid efter månad. Snö är en grov approximation — mars i Skåne och mars i
    Jämtland är inte samma sak, men datumet är allt tävlingen ger oss."""
    return {12: "vinter", 1: "vinter", 2: "vinter", 3: "vinter",
            4: "var", 5: "sommar", 6: "sommar", 7: "sommar", 8: "sommar",
            9: "host", 10: "host", 11: "host"}[when.month]


def format_when(when):
    """'3 mars 2027, 13:00' — samma form som Eventor använder."""
    names = list(MONTHS)
    return f"{when.day} {names[when.month - 1]} {when.year}, {when:%H:%M}"
