# -*- coding: utf-8 -*-
# JG TacRelay - Maintainer: Sarevoc
r"""
Baut das Handbuch JG TacRelay als A4-PDF aus den Kapiteldateien docs\handbuch\NN-*.md.

Gelesen werden nur Dateien mit zwei Ziffern vorn (00-..., 10-..., 95-...), in dieser Reihenfolge.
BAUEN.md und andere Dateien ohne Ziffernpraefix gehoeren nicht zum Handbuch.

Schriften: nur die PDF-Standardschriften Helvetica und Courier, nicht eingebettet (WinAnsi,
damit Umlaute und scharfes s stimmen).

Aufruf (Interpreter der Office-venv, reportlab und pypdf liegen dort), aus dem Worktree:

    C:\Claude\tools\office\.venv\Scripts\python.exe tools\handbuch-pdf.py
        [--quelle <ordner docs\handbuch>] [--ziel <ausgabeordner>] [--zeitstempel JJJJMMTT_HHMM]
        [--ueberschreiben] [--ohne-pruefung] [--struktur <textdatei>]

Ohne --quelle gilt docs\handbuch neben diesem tools-Ordner, ohne --ziel C:\Claude\output\test.
Ziel und Strukturdatei muessen unter C:\Claude liegen. Ohne --zeitstempel gilt die aktuelle
Uhrzeit. Mit demselben Zeitstempel und denselben Quellen entsteht byte-gleich dieselbe Datei
(reportlab invariant, Erstelldatum = Zeitstempel).
Danach liest die Pruefung die Datei mit pypdf zurueck und meldet Seiten, Ueberschriften,
Inhaltsverzeichnis, Umlaute, Schriften, Metadaten, Kapiteldateinamen und SHA-256. Exitcode 0 nur
ohne Befund. Beschreibung: docs\handbuch\BAUEN.md.
"""

import argparse
import datetime
import glob
import hashlib
import os
import re
import sys
import time

import reportlab
from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT, TA_RIGHT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase.pdfmetrics import stringWidth
from reportlab.platypus import (CondPageBreak, Flowable, Frame, NextPageTemplate, PageBreak, Paragraph, Spacer,
                                Table, TableStyle)
from reportlab.platypus.doctemplate import BaseDocTemplate, Indenter, PageTemplate
from reportlab.platypus.tableofcontents import TableOfContents

WURZEL = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
QUELLE = os.path.join(WURZEL, 'docs', 'handbuch')
ZIEL = r'C:\Claude\output\test'
ZONE = 'C:\\Claude\\'

SCHRIFT = 'Helvetica'
FETT = 'Helvetica-Bold'
CODE = 'Courier'
CODEFAKTOR = 0.93          # Courier im Fliesstext etwas kleiner, sonst wirkt er groesser als Helvetica

FARBE_TEXT = colors.HexColor('#1A1A1A')
FARBE_UEBER = colors.HexColor('#1F3A5F')
FARBE_LINIE = colors.HexColor('#8A96A3')
FARBE_KOPF = colors.HexColor('#E4E9F0')
FARBE_CODE = colors.HexColor('#F2F3F5')
FARBE_GRAU = colors.HexColor('#5A6573')

RAND_LINKS = 20 * mm
RAND_RECHTS = 20 * mm
RAND_OBEN = 24 * mm
RAND_UNTEN = 22 * mm
BEDARF = {0: 160, 1: 110, 2: 85, 3: 70}   # Platz in Punkt, den eine Ueberschrift unter sich braucht


# ---------------------------------------------------------------------------
# Markdown: Bloecke
# ---------------------------------------------------------------------------

ZAUN = re.compile(r'^( *)(`{3,})(.*)$')
UEBER = re.compile(r'^(#{1,6})[ \t]+(.*?)[ \t]*$')
LISTE = re.compile(r'^( *)(-|\d{1,9}\.)( +)(\S.*)$')
TRENNER = re.compile(r'^ *\|? *:?-+:? *(\| *:?-+:? *)*\|? *$')


def einzug(z):
    return len(z) - len(z.lstrip(' '))


def ist_tabelle(zeilen, i):
    return (zeilen[i].lstrip().startswith('|') and i + 1 < len(zeilen)
            and '|' in zeilen[i + 1] and TRENNER.match(zeilen[i + 1]) is not None)


def unterbricht_absatz(zeilen, i):
    z = zeilen[i]
    if not z.strip():
        return True
    if einzug(z) < 4 and (ZAUN.match(z.lstrip(' ')) or UEBER.match(z.lstrip(' '))):
        return True
    if z.lstrip().startswith('>'):
        return True
    m = LISTE.match(z)
    if m and m.group(2) in ('-', '1.'):
        return True
    return ist_tabelle(zeilen, i)


def zellen(z):
    s = z.strip()
    if s.startswith('|'):
        s = s[1:]
    if s.endswith('|') and not s.endswith('\\|'):
        s = s[:-1]
    return [t.strip().replace('\\|', '|') for t in re.split(r'(?<!\\)\|', s)]


def ausrichtung(z):
    erg = []
    for t in zellen(z):
        t = t.strip()
        if t.startswith(':') and t.endswith(':'):
            erg.append(TA_CENTER)
        elif t.endswith(':'):
            erg.append(TA_RIGHT)
        else:
            erg.append(TA_LEFT)
    return erg


def im_code(zeilen):
    return sum(1 for z in zeilen if z.lstrip(' ').startswith('```')) % 2 == 1


def liste(zeilen, i):
    n = len(zeilen)
    m = LISTE.match(zeilen[i])
    basis = len(m.group(1))
    geordnet = m.group(2) != '-'
    start = int(m.group(2)[:-1]) if geordnet else 1
    punkte = []
    while i < n:
        m = LISTE.match(zeilen[i])
        if not m or len(m.group(1)) != basis or (m.group(2) != '-') != geordnet:
            break
        abstand = len(m.group(3)) if len(m.group(3)) <= 4 else 1
        inhalt = len(m.group(1)) + len(m.group(2)) + abstand
        punkt = [m.group(4)]
        i += 1
        while i < n:
            z = zeilen[i]
            if not z.strip():
                j = i
                while j < n and not zeilen[j].strip():
                    j += 1
                if j < n and einzug(zeilen[j]) >= inhalt:
                    punkt.extend([''] * (j - i))
                    i = j
                    continue
                break
            if einzug(z) >= inhalt:
                punkt.append(z[inhalt:])
                i += 1
                continue
            if LISTE.match(z):
                break
            if punkt[-1].strip() and not im_code(punkt) and not unterbricht_absatz(zeilen, i):
                punkt.append(z.strip())      # faule Fortsetzung eines Absatzes
                i += 1
                continue
            break
        punkte.append(bloecke(punkt))
        j = i
        while j < n and not zeilen[j].strip():
            j += 1
        if j < n:
            m3 = LISTE.match(zeilen[j])
            if m3 and len(m3.group(1)) == basis and (m3.group(2) != '-') == geordnet:
                i = j
                continue
        break
    return ('liste', geordnet, start, punkte), i


def bloecke(zeilen):
    erg = []
    i = 0
    n = len(zeilen)
    while i < n:
        z = zeilen[i]
        if not z.strip():
            i += 1
            continue
        m = ZAUN.match(z)
        if m and einzug(z) < 4:
            ein = len(m.group(1))
            zaun = m.group(2)
            code = []
            i += 1
            while i < n:
                zz = zeilen[i]
                s = zz.strip()
                if einzug(zz) < 4 and s.startswith(zaun) and set(s) == {'`'}:
                    i += 1
                    break
                k = 0
                while k < ein and k < len(zz) and zz[k] == ' ':
                    k += 1
                code.append(zz[k:])
                i += 1
            erg.append(('code', code, m.group(3).strip()))
            continue
        m = UEBER.match(z.lstrip(' ')) if einzug(z) < 4 else None
        if m:
            text = re.sub(r'[ \t]+#+[ \t]*$', '', m.group(2))
            erg.append(('ueber', len(m.group(1)), text))
            i += 1
            continue
        if ist_tabelle(zeilen, i):
            kopf = zellen(zeilen[i])
            ausr = ausrichtung(zeilen[i + 1])
            i += 2
            reihen = []
            while i < n and zeilen[i].lstrip().startswith('|'):
                reihen.append(zellen(zeilen[i]))
                i += 1
            erg.append(('tabelle', kopf, ausr, reihen))
            continue
        if z.lstrip().startswith('>'):
            innen = []
            while i < n:
                zz = zeilen[i]
                if zz.lstrip().startswith('>'):
                    s = zz.lstrip()[1:]
                    if s.startswith(' '):
                        s = s[1:]
                    innen.append(s)
                    i += 1
                elif zz.strip() and innen and innen[-1].strip() and not unterbricht_absatz(zeilen, i):
                    innen.append(zz)
                    i += 1
                else:
                    break
            erg.append(('zitat', bloecke(innen)))
            continue
        if LISTE.match(z):
            blk, i = liste(zeilen, i)
            erg.append(blk)
            continue
        teile = [z.strip()]
        i += 1
        while i < n and not unterbricht_absatz(zeilen, i):
            teile.append(zeilen[i].strip())
            i += 1
        erg.append(('absatz', ' '.join(teile)))
    return erg


# ---------------------------------------------------------------------------
# Markdown: Zeilen-Auszeichnung (Code, Fett, Kursiv, Backslash)
# ---------------------------------------------------------------------------

SATZZEICHEN = set('!"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~')
FETT_RE = re.compile(r'\*\*(?=\S)(.+?)(?<=\S)\*\*')
KURSIV_RE = re.compile(r'(?<![\w*])\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?![\w*])')
PLATZ_RE = re.compile('\x01(\\d+)\x02')


def xml(s):
    return s.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def zerlegen(text):
    """Teilt eine Zeile in ('t', text), ('e', escaptes zeichen) und ('c', code)."""
    teile = []
    puffer = []
    i, n = 0, len(text)

    def leeren():
        if puffer:
            teile.append(('t', ''.join(puffer)))
            del puffer[:]

    while i < n:
        c = text[i]
        if c == '\\' and i + 1 < n and text[i + 1] in SATZZEICHEN:
            leeren()
            teile.append(('e', text[i + 1]))
            i += 2
            continue
        if c == '`':
            j = i
            while j < n and text[j] == '`':
                j += 1
            lauf = j - i
            k = j
            ende = -1
            while k < n:
                if text[k] == '`':
                    e = k
                    while e < n and text[e] == '`':
                        e += 1
                    if e - k == lauf:
                        ende = k
                        break
                    k = e
                else:
                    k += 1
            if ende < 0:
                puffer.append('`' * lauf)
                i = j
                continue
            inhalt = text[j:ende]
            if len(inhalt) >= 2 and inhalt[0] == ' ' and inhalt[-1] == ' ' and inhalt.strip():
                inhalt = inhalt[1:-1]
            leeren()
            teile.append(('c', inhalt))
            i = ende + lauf
            continue
        puffer.append(c)
        i += 1
    leeren()
    return teile


def inline(text, groesse):
    platz = []
    s = []
    for art, wert in zerlegen(text):
        if art == 't':
            s.append(wert)
        else:
            platz.append((art, wert))
            s.append('\x01%d\x02' % (len(platz) - 1))
    s = xml(''.join(s))
    s = FETT_RE.sub(r'<b>\1</b>', s)
    s = KURSIV_RE.sub(r'<i>\1</i>', s)

    def zurueck(m):
        art, wert = platz[int(m.group(1))]
        if art == 'e':
            return xml(wert)
        return '<font face="%s" size="%.2f">%s</font>' % (CODE, groesse * CODEFAKTOR, xml(wert))
    return PLATZ_RE.sub(zurueck, s)


def schlicht(text):
    s = re.sub(r'<[^>]+>', '', inline(text, 10))
    return s.replace('&lt;', '<').replace('&gt;', '>').replace('&amp;', '&')


TRENNER_CODE = '/\\._-'     # Code in Tabellenzellen bricht nur hinter diesen Zeichen um (Pfadtrenner, Punkt, - und _)


def segmente(code):
    """Teilt ein Stueck Code ohne Leerzeichen hinter jedem erlaubten Trennzeichen; ''.join ergibt das Original."""
    erg = []
    anfang = 0
    for i, c in enumerate(code):
        if c in TRENNER_CODE:
            erg.append(code[anfang:i + 1])
            anfang = i + 1
    if anfang < len(code):
        erg.append(code[anfang:])
    return erg or ['']


def messen(text, groesse, fett=False):
    """Breite in Punkt: ganze Zelle in einer Zeile, laengste unteilbare Einheit (Wort, bei Code das laengste Stueck
    zwischen erlaubten Trennstellen) und Breite des Fliesstexts ohne Code."""
    gesamt = 0.0
    prosa = 0.0
    wort = 0.0
    wort_max = 0.0
    stuecke = []
    fett_an = False
    for art, wert in zerlegen(text):
        if art == 't':
            # **fett** im Text: Breite in Helvetica-Bold messen
            teile = wert.split('**')
            for k, t in enumerate(teile):
                if k > 0:
                    fett_an = not fett_an
                if t:
                    stuecke.append(('t', t, fett or fett_an))
        else:
            stuecke.append((art, wert, fett or fett_an))
    for art, wert, ist_fett in stuecke:
        if art == 'c':
            f, g = CODE, groesse * CODEFAKTOR
        else:
            f, g = (FETT if ist_fett else SCHRIFT), groesse
        for teil in re.split(r'(\s+)', wert):
            if not teil:
                continue
            if teil.isspace():
                gesamt += stringWidth(' ', f, g)
                wort_max = max(wort_max, wort)
                wort = 0.0
            elif art == 'c':
                for k, s in enumerate(segmente(teil)):
                    w = stringWidth(s, f, g)
                    gesamt += w
                    if k == 0:
                        wort += w
                    else:
                        wort_max = max(wort_max, wort)
                        wort = w
            else:
                w = stringWidth(teil, f, g)
                gesamt += w
                prosa += w
                wort += w
    return gesamt, max(wort_max, wort), prosa


def code_brechen(wert, schrift, groesse, grenze, vor=0.0, nach=0.0):
    """Code, der breiter ist als die Zelle, an erlaubten Trennstellen in Zeilen bis zur Grenze teilen (gierig).
    vor und nach sind die Breiten des Texts, der ohne Leerzeichen davor oder dahinter klebt (Klammer, Komma); sie
    zaehlen zum ersten und zum letzten Stueck. Ein einzelnes Stueck kann breiter sein, wenn es keine Trennstelle gibt."""
    segs = segmente(wert)
    stuecke = []
    aktuell = ''
    for i, s in enumerate(segs):
        extra = (vor if not stuecke else 0.0) + (nach if i == len(segs) - 1 else 0.0)
        if aktuell and stringWidth(aktuell + s, schrift, groesse) + extra > grenze:
            stuecke.append(aktuell)
            aktuell = s
        else:
            aktuell += s
    if aktuell:
        stuecke.append(aktuell)
    return stuecke


def klebt(text, vorn):
    """Der Teil von text, der ohne Leerraum an einem Nachbarn klebt: vorn die ersten, sonst die letzten Zeichen."""
    if vorn:
        m = re.match(r'\S*', text)
    else:
        m = re.search(r'\S*$', text)
    return m.group(0) if m else ''


def inline_zelle(text, groesse, innen):
    """Wie inline, aber Code, der nicht in die Zellbreite passt, bekommt Zeilenumbrueche nur hinter / \\ . - _."""
    platz = []
    s = []
    gc = groesse * CODEFAKTOR
    frags = zerlegen(text)
    for k, (art, wert) in enumerate(frags):
        if art == 't':
            s.append(wert)
            continue
        if art == 'c':
            # was ohne Leerraum davor und dahinter klebt (nur Text und Zeichen mit Backslash, kein weiterer Code)
            vor = klebt(frags[k - 1][1], False).replace('**', '') if k > 0 and frags[k - 1][0] != 'c' else ''
            nach = klebt(frags[k + 1][1], True).replace('**', '') if k + 1 < len(frags) and frags[k + 1][0] != 'c' else ''
            w_vor = stringWidth(vor, FETT, groesse)
            w_nach = stringWidth(nach, FETT, groesse)
            teile = []
            stuecke = re.split(r'(\s+)', wert)
            for j, teil in enumerate(stuecke):
                if not teil or teil.isspace():
                    teile.append(('ganz', teil))
                    continue
                v = w_vor if j == 0 else 0.0
                n = w_nach if j == len(stuecke) - 1 else 0.0
                if v + stringWidth(teil, CODE, gc) + n > innen - 0.5:
                    teile.append(('brechen', code_brechen(teil, CODE, gc, innen - 0.5, v, n)))
                else:
                    teile.append(('ganz', teil))
            if any(a == 'brechen' for a, _ in teile):
                platz.append(('cb', teile))
                s.append('\x01%d\x02' % (len(platz) - 1))
                continue
        platz.append((art, wert))
        s.append('\x01%d\x02' % (len(platz) - 1))
    s = xml(''.join(s))
    s = FETT_RE.sub(r'<b>\1</b>', s)
    s = KURSIV_RE.sub(r'<i>\1</i>', s)

    def font(t):
        return '<font face="%s" size="%.2f">%s</font>' % (CODE, gc, xml(t))

    def zurueck(m):
        art, wert = platz[int(m.group(1))]
        if art == 'e':
            return xml(wert)
        if art == 'c':
            return font(wert)
        aus = []
        for a, w in wert:
            if a == 'ganz':
                aus.append(font(w) if w else '')
            else:
                aus.append('<br/>'.join(font(x) for x in w))
        return ''.join(aus)
    return PLATZ_RE.sub(zurueck, s)


GRUPPE_RE = re.compile(r'^\*\*[^*].*\*\*$')


def spaltenbreiten(mins, maxs, masse, verfuegbar):
    """Breiten zwischen Minimum und Maximum, den Rest nach Textmenge verteilt (Wasserfuellung)."""
    n = len(mins)
    if sum(maxs) <= verfuegbar:
        return list(maxs)
    if sum(mins) >= verfuegbar:
        return [m * verfuegbar / sum(mins) for m in mins]
    fest = {}
    while True:
        frei = [c for c in range(n) if c not in fest]
        if not frei:
            breiten = [fest[c] for c in range(n)]
            break
        rest = verfuegbar - sum(fest.values())
        summe = sum(max(masse[c], 1.0) for c in frei)
        anteil = {c: rest * max(masse[c], 1.0) / summe for c in frei}
        unter = [c for c in frei if anteil[c] < mins[c]]
        if unter:
            for c in unter:
                fest[c] = mins[c]
            continue
        ueber = [c for c in frei if anteil[c] > maxs[c]]
        if ueber:
            for c in ueber:
                fest[c] = maxs[c]
            continue
        breiten = [fest[c] if c in fest else anteil[c] for c in range(n)]
        break
    # Summe genau auf die verfuegbare Breite bringen, ohne ein Minimum zu unterschreiten
    diff = sum(breiten) - verfuegbar
    if diff > 0.01:
        luft = sum(b - m for b, m in zip(breiten, mins))
        breiten = [b - (b - m) * diff / luft for b, m in zip(breiten, mins)]
    elif diff < -0.01:
        luft = sum(m2 - b for b, m2 in zip(breiten, maxs))
        if luft > 0:
            breiten = [b + (m2 - b) * (-diff) / luft for b, m2 in zip(breiten, maxs)]
    return breiten


# ---------------------------------------------------------------------------
# Flowables
# ---------------------------------------------------------------------------

class Ueberschrift(Paragraph):
    """Absatz mit Eintrag fuer Lesezeichen und Inhaltsverzeichnis (Attribut kf)."""
    kf = None

    def split(self, breite, hoehe):
        teile = Paragraph.split(self, breite, hoehe)
        if teile:
            teile[0].kf = self.kf
        return teile


class ZitatAbsatz(Paragraph):
    """Absatz in einem Zitat: zeichnet links einen senkrechten Balken."""
    balken_x = -7.0
    balken_unten = 0.0

    def split(self, breite, hoehe):
        teile = Paragraph.split(self, breite, hoehe)
        for k, t in enumerate(teile):
            t.balken_x = self.balken_x
            t.balken_unten = self.balken_unten if k == len(teile) - 1 else 0.0
        return teile

    def draw(self):
        c = self.canv
        c.saveState()
        c.setStrokeColor(FARBE_LINIE)
        c.setLineWidth(1.8)
        c.line(self.balken_x, -self.balken_unten, self.balken_x, self.height)
        c.restoreState()
        Paragraph.draw(self)


class Tabelle(Table):
    """Tabelle, die ihre Fortsetzungsteile kennzeichnet: Dort steht die Kopfzeile ein zweites Mal,
    was die Pruefung beim Textvergleich beruecksichtigt."""
    kopftext = ''
    fortsetzung = False

    def split(self, breite, hoehe):
        teile = Table.split(self, breite, hoehe)
        for k, t in enumerate(teile):
            t.kopftext = self.kopftext
            t.fortsetzung = self.fortsetzung if k == 0 else True
        return teile


class Codeblock(Flowable):
    """Codeblock in Courier mit Umbruch. Fortsetzungszeilen sind eingerueckt und links mit einem
    gezeichneten Haken markiert (kein zusaetzliches Zeichen im Text)."""

    GROESSE = 7.5
    ZEILE = 9.2
    PAD_L = 9.0
    PAD_R = 5.0
    PAD_O = 4.0
    PAD_U = 4.0

    def __init__(self, zeilen, fertig=None):
        Flowable.__init__(self)
        self.zeilen = zeilen
        self.fertig = fertig
        self._breite = None
        self.spaceBefore = 2
        self.spaceAfter = 7

    def _brechen(self, breite):
        zw = stringWidth('M', CODE, self.GROESSE)
        maxz = max(20, int((breite - self.PAD_L - self.PAD_R) / zw))
        erg = []
        for z in self.zeilen:
            z = z.expandtabs(4).rstrip()
            if len(z) <= maxz:
                erg.append((z, False))
                continue
            rest = z
            erste = True
            while rest:
                grenze = maxz if erste else maxz - 2
                if len(rest) <= grenze:
                    erg.append((rest, not erste))
                    break
                schnitt = rest.rfind(' ', int(grenze * 0.6), grenze)
                schnitt = grenze if schnitt <= 0 else schnitt + 1
                erg.append((rest[:schnitt], not erste))
                rest = rest[schnitt:]
                erste = False
        return erg

    def wrap(self, breite, hoehe):
        if self.fertig is None or (self._breite is not None and abs(self._breite - breite) > 0.5):
            if self.zeilen is not None:
                self.fertig = self._brechen(breite)
        self._breite = breite
        self.width = breite
        self.height = len(self.fertig) * self.ZEILE + self.PAD_O + self.PAD_U
        return self.width, self.height

    def split(self, breite, hoehe):
        self.wrap(breite, hoehe)
        if self.height <= hoehe:
            return [self]
        passt = int((hoehe - self.PAD_O - self.PAD_U) / self.ZEILE)
        if passt < 3 or len(self.fertig) - passt < 2:
            passt = min(passt, len(self.fertig) - 2)
        if passt < 3:
            return []
        a = Codeblock(None, self.fertig[:passt])
        b = Codeblock(None, self.fertig[passt:])
        a._breite = b._breite = breite
        a.spaceAfter = 0
        b.spaceBefore = 0
        return [a, b]

    def draw(self):
        c = self.canv
        c.saveState()
        c.setFillColor(FARBE_CODE)
        c.rect(0, 0, self.width, self.height, stroke=0, fill=1)
        c.setStrokeColor(FARBE_LINIE)
        c.setLineWidth(1.2)
        c.line(0.6, 0, 0.6, self.height)
        zw = stringWidth('M', CODE, self.GROESSE)
        y = self.height - self.PAD_O - self.GROESSE
        c.setFont(CODE, self.GROESSE)
        for text, weiter in self.fertig:
            if weiter:
                c.setStrokeColor(FARBE_GRAU)
                c.setLineWidth(0.6)
                x0 = self.PAD_L - 5.0
                c.line(x0, y + self.GROESSE * 0.75, x0, y + self.GROESSE * 0.3)
                c.line(x0, y + self.GROESSE * 0.3, x0 + 3.2, y + self.GROESSE * 0.3)
                c.setFillColor(FARBE_TEXT)
                c.drawString(self.PAD_L + 2 * zw, y, text)
            else:
                c.setFillColor(FARBE_TEXT)
                c.drawString(self.PAD_L, y, text)
            y -= self.ZEILE
        c.restoreState()


# ---------------------------------------------------------------------------
# Stile
# ---------------------------------------------------------------------------

def stile():
    st = {}
    # allowWidows=0: die letzte Zeile eines Absatzes steht nie allein oben auf einer Seite (ab Werk erlaubt
    # reportlab das); allowOrphans=0 (Werk): die erste Zeile steht nie allein unten. Erbt auch 'liste'.
    st['text'] = ParagraphStyle('text', fontName=SCHRIFT, fontSize=9.6, leading=13.2, textColor=FARBE_TEXT,
                                spaceAfter=5.5, alignment=TA_LEFT, allowWidows=0, allowOrphans=0)
    st['liste'] = ParagraphStyle('liste', parent=st['text'], spaceAfter=2.5, bulletFontName=SCHRIFT,
                                 bulletFontSize=9.6)
    st['titel'] = ParagraphStyle('titel', fontName=FETT, fontSize=28, leading=34, textColor=FARBE_UEBER,
                                 spaceAfter=14)
    st['h0'] = ParagraphStyle('h0', fontName=FETT, fontSize=19, leading=23, textColor=FARBE_UEBER,
                              spaceBefore=16, spaceAfter=12)
    st['h1'] = ParagraphStyle('h1', fontName=FETT, fontSize=13.5, leading=17, textColor=FARBE_UEBER,
                              spaceBefore=13, spaceAfter=6)
    st['h2'] = ParagraphStyle('h2', fontName=FETT, fontSize=11, leading=14, textColor=FARBE_UEBER,
                              spaceBefore=10, spaceAfter=4)
    st['h3'] = ParagraphStyle('h3', fontName=FETT, fontSize=9.8, leading=12.5, textColor=FARBE_TEXT,
                              spaceBefore=8, spaceAfter=3)
    st['zelle'] = ParagraphStyle('zelle', fontName=SCHRIFT, fontSize=8.4, leading=10.4, textColor=FARBE_TEXT)
    st['kopfzelle'] = ParagraphStyle('kopfzelle', parent=st['zelle'], fontName=FETT)
    st['toc0'] = ParagraphStyle('toc0', fontName=FETT, fontSize=10, leading=13, textColor=FARBE_UEBER,
                                spaceBefore=6, leftIndent=0, firstLineIndent=0)
    st['toc1'] = ParagraphStyle('toc1', fontName=SCHRIFT, fontSize=8.8, leading=11.2, textColor=FARBE_TEXT,
                                leftIndent=14, firstLineIndent=0)
    st['toc2'] = ParagraphStyle('toc2', fontName=SCHRIFT, fontSize=8.2, leading=10.4, textColor=FARBE_TEXT,
                                leftIndent=28, firstLineIndent=0)
    return st


# ---------------------------------------------------------------------------
# Story bauen
# ---------------------------------------------------------------------------

class Bauer:
    def __init__(self, breite):
        self.breite = breite
        self.st = stile()
        self.f = []
        self.einzug = 0.0
        self.zitat = None            # Einrueckung seit Zitatbeginn, None ausserhalb
        self.nr = 0
        self.ueberschriften = []     # (ebene, text, key, im_inhaltsverzeichnis)
        self.sichtbar = []           # sichtbarer Text des Rumpfs, fuer die Umlautpruefung
        self._listenstile = {}
        self.zellen = []             # Absaetze in Tabellenzellen, fuer die Pruefung auf gebrochene Woerter
        self.gruppenzeilen = 0       # Gruppenzeilen ueber alle Spalten, fuer die Ausgabe der Pruefung
        self.kapitel = ''            # Titel des laufenden Kapitels (Ebene 0)

    # -- einfache Bloecke
    def listenstil(self, ein, marke):
        key = (round(ein, 2), marke == '-')
        if key not in self._listenstile:
            self._listenstile[key] = ParagraphStyle('liste_%d' % len(self._listenstile), parent=self.st['liste'],
                                                    leftIndent=ein, bulletIndent=1.5 if marke == '-' else 0)
        return self._listenstile[key]

    def absatz(self, text, marke=None, ein=0.0, stil=None):
        if stil is None:
            stil = self.st['text'] if marke is None else self.listenstil(ein, marke)
        markup = inline(text, stil.fontSize)
        if marke:
            self.sichtbar.append(marke)
        self.sichtbar.append(schlicht(text))
        if self.zitat is not None:
            p = ZitatAbsatz(markup, stil, bulletText=marke)
            p.balken_x = -(7.0 + self.zitat)
        else:
            p = Paragraph(markup, stil, bulletText=marke)
        self.f.append(p)
        return p

    def ueberschrift(self, ebene, text, umbruch=False, toc=True, key=None, danach=0.0):
        stil = self.st['h%d' % min(ebene, 3)]
        if umbruch:
            self.f.append(PageBreak())
        else:
            # Statt keepWithNext (das haengt Ueberschrift und ganze Tabelle zusammen und laesst
            # halbe Seiten leer): neue Seite nur, wenn unter der Ueberschrift kaum Platz bleibt. Folgt eine Tabelle
            # oder ein Codeblock (wahlweise nach einem Absatz), muss auch deren Anfang noch passen (danach).
            kopf = stil.spaceBefore + stil.leading + stil.spaceAfter
            bedarf = BEDARF[min(ebene, 3)]
            if danach and kopf + danach < 360:
                bedarf = max(bedarf, kopf + danach)
            self.f.append(CondPageBreak(bedarf))
        p = Ueberschrift(inline(text, stil.fontSize), stil)
        self.nr += 1
        key = key or 'k%d' % self.nr
        if ebene == 0:
            self.kapitel = schlicht(text)
        # Inhaltsverzeichnis: Kapitel und erste Ebene, in Teil E auch die zweite (E2.1, E3.11 ...)
        im_toc = ebene <= 1 or (ebene == 2 and self.kapitel.startswith('Teil E'))
        p.kf = (ebene, schlicht(text), key, toc and im_toc)
        self.ueberschriften.append(p.kf)
        self.sichtbar.append(schlicht(text))
        self.f.append(p)

    def code(self, zeilen):
        self.sichtbar.extend(zeilen)
        self.f.append(Codeblock(zeilen))

    def tabelle(self, kopf, ausr, reihen, nur_hoehe=False):
        nsp = len(kopf)
        reihen = [(r + [''] * nsp)[:nsp] for r in reihen]
        ausr = (ausr + [TA_LEFT] * nsp)[:nsp]
        hat_kopf = any(c.strip() for c in kopf)
        alle = ([kopf] if hat_kopf else []) + reihen
        verfuegbar = self.breite - self.einzug
        g = self.st['zelle'].fontSize
        pad = 9.0
        # Gruppenzeilen: nur die erste Zelle, ganz fett, die uebrigen leer; sie gehen ueber alle Spalten und bleiben
        # mit der folgenden Zeile zusammen
        gruppen = set()
        for ri, r in enumerate(alle):
            if (ri > 0 or not hat_kopf) and GRUPPE_RE.match(r[0].strip()) and not any(x.strip() for x in r[1:]):
                gruppen.add(ri)
        mins = [0.0] * nsp
        maxs = [0.0] * nsp
        masse = [0.0] * nsp
        prosa = [0.0] * nsp
        for ri, r in enumerate(alle):
            if ri in gruppen:
                continue
            for c in range(nsp):
                ges, wort, pr = messen(r[c], g, fett=(hat_kopf and ri == 0))
                mins[c] = max(mins[c], wort + pad)
                maxs[c] = max(maxs[c], ges + pad)
                masse[c] += ges
                prosa[c] += pr
        roh = [max(24.0, min(m, verfuegbar * 0.4)) for m in mins]
        maxs = [max(a, b) for a, b in zip(maxs, roh)]
        # Mindestbreite fuer Textspalten: ueberwiegend Fliesstext und im Mittel mehr als eine Zeile von 120 Punkt
        zeilen = max(1, len(alle) - len(gruppen))
        boden = [min(maxs[c], max(roh[c], 0.2 * verfuegbar))
                 if masse[c] > 0 and prosa[c] >= 0.6 * masse[c] and masse[c] / zeilen > 120.0 else roh[c]
                 for c in range(nsp)]
        if sum(boden) > verfuegbar:
            # Boden so weit senken, dass er passt (nie unter das Minimum aus Woertern und Codestuecken)
            unten, oben = 0.0, 1.0
            for _ in range(40):
                mitte = (unten + oben) / 2
                if sum(r + (b - r) * mitte for r, b in zip(roh, boden)) > verfuegbar:
                    oben = mitte
                else:
                    unten = mitte
            boden = [r + (b - r) * unten for r, b in zip(roh, boden)]
        breiten = spaltenbreiten(boden, maxs, masse, verfuegbar)
        if nur_hoehe:
            # Hoehe von Kopf und erster Zeile (eine Gruppenzeile zaehlt mit der Zeile danach), ohne Nebenwirkung
            bis = (1 if hat_kopf else 0) + 1
            if bis - 1 in gruppen:
                bis += 1
            hoehe = 3.0
            for ri, r in enumerate(alle[:bis]):
                zeilenhoehe = 0.0
                for c in range(nsp if ri not in gruppen else 1):
                    basis = self.st['kopfzelle'] if (hat_kopf and ri == 0) else self.st['zelle']
                    innen = (verfuegbar if ri in gruppen else breiten[c]) - 8.0
                    p = Paragraph(inline_zelle(r[c], basis.fontSize, innen), basis)
                    zeilenhoehe = max(zeilenhoehe, p.wrap(innen, 1e6)[1])
                hoehe += zeilenhoehe + 6.0
            return hoehe
        daten = []
        for ri, r in enumerate(alle):
            zeile = []
            for c in range(nsp):
                basis = self.st['kopfzelle'] if (hat_kopf and ri == 0) else self.st['zelle']
                stil = basis if ausr[c] == TA_LEFT else ParagraphStyle(basis.name + '_a', parent=basis,
                                                                       alignment=ausr[c])
                self.sichtbar.append(schlicht(r[c]))
                innen = (verfuegbar if ri in gruppen else breiten[c]) - 8.0
                p = Paragraph(inline_zelle(r[c], stil.fontSize, innen), stil)
                p.tr_zelle = r[c]
                p.tr_spalte = 'Spalte %d von %d: Breite %.0f, Minimum %.0f, Boden %.0f, Summe Minima %.0f von %.0f' % (
                    c + 1, nsp, breiten[c], roh[c], boden[c], sum(roh), verfuegbar)
                self.zellen.append(p)
                zeile.append(p)
            daten.append(zeile)
        stilliste = [('GRID', (0, 0), (-1, -1), 0.4, FARBE_LINIE),
                     ('VALIGN', (0, 0), (-1, -1), 'TOP'),
                     ('LEFTPADDING', (0, 0), (-1, -1), 4),
                     ('RIGHTPADDING', (0, 0), (-1, -1), 4),
                     ('TOPPADDING', (0, 0), (-1, -1), 2.5),
                     ('BOTTOMPADDING', (0, 0), (-1, -1), 3.5)]
        if hat_kopf:
            stilliste.append(('BACKGROUND', (0, 0), (-1, 0), FARBE_KOPF))
        self.gruppenzeilen += len(gruppen)
        for ri in sorted(gruppen):
            stilliste.append(('SPAN', (0, ri), (-1, ri)))
            stilliste.append(('BACKGROUND', (0, ri), (-1, ri), FARBE_CODE))
            if ri + 1 < len(alle):
                stilliste.append(('NOSPLIT', (0, ri), (-1, ri + 1)))
        t = Tabelle(daten, colWidths=breiten, repeatRows=1 if hat_kopf else 0, hAlign='LEFT',
                    spaceBefore=3, spaceAfter=8)
        t.setStyle(TableStyle(stilliste))
        t.kopftext = ''.join(schlicht(c) for c in kopf) if hat_kopf else ''
        self.f.append(t)

    def zitat_block(self, innen):
        ein = 12.0
        self.f.append(Indenter(left=ein))
        self.einzug += ein
        vorher_zitat = self.zitat
        self.zitat = 0.0
        anfang = len(self.f)
        self.rendern(innen)
        absaetze = [x for x in self.f[anfang:] if isinstance(x, ZitatAbsatz)]
        for k, p in enumerate(absaetze):
            p.balken_unten = 0.0 if k == len(absaetze) - 1 else p.style.spaceAfter
        self.zitat = vorher_zitat
        self.einzug -= ein
        self.f.append(Indenter(left=-ein))
        self.f.append(Spacer(1, 4))

    def liste_block(self, blk):
        _, geordnet, start, punkte = blk
        if geordnet:
            ein = max(15.0, stringWidth('%d.' % (start + len(punkte) - 1), SCHRIFT, 9.6) + 5.5)
        else:
            ein = 10.5
        for k, bl in enumerate(punkte):
            marke = '%d.' % (start + k) if geordnet else '-'
            if bl and bl[0][0] == 'absatz':
                self.absatz(bl[0][1], marke=marke, ein=ein)
                rest = bl[1:]
            else:
                self.absatz('', marke=marke, ein=ein)
                rest = bl
            if rest:
                self.f.append(Indenter(left=ein))
                self.einzug += ein
                if self.zitat is not None:
                    self.zitat += ein
                self.rendern(rest)
                if self.zitat is not None:
                    self.zitat -= ein
                self.einzug -= ein
                self.f.append(Indenter(left=-ein))
        if self.zitat is None:
            self.f.append(Spacer(1, 3.5))

    def vorschau(self, bl, i):
        """Platz, den der Anfang der Bloecke nach der Ueberschrift bl[i] braucht: ein Absatz davor, dann Kopf und
        erste Zeile einer Tabelle oder drei Zeilen eines Codeblocks. 0, wenn danach anderes kommt."""
        j = i + 1
        hoehe = 0.0
        breite = self.breite - self.einzug
        if j + 1 < len(bl) and bl[j][0] == 'absatz' and bl[j + 1][0] in ('tabelle', 'code'):
            stil = self.st['text']
            hoehe += Paragraph(inline(bl[j][1], stil.fontSize), stil).wrap(breite, 1e6)[1] + stil.spaceAfter
            j += 1
        if j < len(bl) and bl[j][0] == 'tabelle':
            return hoehe + self.tabelle(bl[j][1], bl[j][2], bl[j][3], nur_hoehe=True)
        if j < len(bl) and bl[j][0] == 'code':
            return hoehe + min(len(bl[j][1]), 3) * Codeblock.ZEILE + Codeblock.PAD_O + Codeblock.PAD_U + 2
        return 0.0

    def rendern(self, bl, zuordnung=None):
        for i, b in enumerate(bl):
            art = b[0]
            if art == 'absatz':
                self.absatz(b[1])
            elif art == 'ueber':
                ebene, umbruch = (zuordnung or {}).get(id(b), (min(b[1], 3), False))
                self.ueberschrift(ebene, b[2], umbruch=umbruch, danach=self.vorschau(bl, i))
            elif art == 'code':
                self.code(b[1])
            elif art == 'tabelle':
                self.tabelle(b[1], b[2], b[3])
            elif art == 'zitat':
                self.zitat_block(b[1])
            elif art == 'liste':
                self.liste_block(b)

    # -- das ganze Handbuch
    def story(self, quellen, meta):
        name0, bl0 = quellen[0]
        # Titelseite: alles aus 00 vor der ersten ##-Ueberschrift
        erste = next((k for k, b in enumerate(bl0) if b[0] == 'ueber' and b[1] >= 2), len(bl0))
        self.f.append(Spacer(1, 55 * mm))
        for b in bl0[:erste]:
            if b[0] == 'ueber':
                p = Ueberschrift(inline(b[2], self.st['titel'].fontSize), self.st['titel'])
                p.kf = (0, schlicht(b[2]), 'titel', False)
                self.ueberschriften.append(p.kf)
                self.sichtbar.append(schlicht(b[2]))
                self.f.append(p)
                self.f.append(Spacer(1, 6 * mm))
            else:
                self.rendern([b])
        self.f.append(NextPageTemplate('inhalt'))
        self.f.append(PageBreak())
        # Inhaltsverzeichnis (nicht Teil der Quellen, daher nicht in sichtbar)
        p = Ueberschrift('Inhaltsverzeichnis', self.st['h0'])
        p.kf = (0, 'Inhaltsverzeichnis', 'inhaltsverzeichnis', False)
        self.f.append(p)
        toc = TableOfContents(dotsMinLevel=0)
        toc.levelStyles = [self.st['toc0'], self.st['toc1'], self.st['toc2']]
        toc.tableStyle = TableStyle([('VALIGN', (0, 0), (-1, -1), 'TOP'),
                                     ('LEFTPADDING', (0, 0), (-1, -1), 0),
                                     ('RIGHTPADDING', (0, 0), (-1, -1), 0),
                                     ('TOPPADDING', (0, 0), (-1, -1), 0.5),
                                     ('BOTTOMPADDING', (0, 0), (-1, -1), 1.5)])
        self.f.append(toc)

        # 00: jede ## ist ein Kapitel, nur das erste beginnt eine neue Seite
        self.rendern(bl0[erste:], ebenen_zuordnen(bl0[erste:], titelteil=True))
        # uebrige Dateien: die erste Ueberschrift und jede "## Teil X:" ist ein Kapitel mit neuer Seite
        for name, bl in quellen[1:]:
            self.rendern(bl, ebenen_zuordnen(bl, titelteil=False))
        return self.f


def ebenen_zuordnen(bl, titelteil):
    """Ebene je Ueberschrift: 0 = Kapitel; darunter zaehlen nur die im Kapitel benutzten Stufen,
    damit Kapitel 1 (### 1.1) und Teil A (## A1) beide Unterabschnitte der Ebene 1 haben."""
    zuordnung = {}
    kapitel = []
    for k, b in enumerate(x for x in bl if x[0] == 'ueber'):
        if titelteil:
            ist_kapitel = b[1] <= 2
        else:
            ist_kapitel = b[1] == 1 or (b[1] == 2 and (k == 0 or re.match(r'^Teil [A-Z]\b', b[2]) is not None))
        if ist_kapitel or not kapitel:
            kapitel.append((b if ist_kapitel else None, []))
        if not ist_kapitel:
            kapitel[-1][1].append(b)
    for n, (kopf, unter) in enumerate(kapitel):
        if kopf is not None:
            zuordnung[id(kopf)] = (0, not titelteil or n == 0)
        stufen = sorted(set(b[1] for b in unter))
        for b in unter:
            zuordnung[id(b)] = (min(stufen.index(b[1]) + 1, 3), False)
    return zuordnung


# ---------------------------------------------------------------------------
# Dokument
# ---------------------------------------------------------------------------

def kopfzeile(kapitel, breite):
    kap = kapitel
    while stringWidth(kap, SCHRIFT, 8) > breite and len(kap) > 8:
        kap = kap[:-8].rstrip() + ' ...'
    return kap


def fusszeile(meta):
    return '%s, Fassung %s, %s' % (meta['titel'], meta['fassung'], meta['datum'])


class Dokument(BaseDocTemplate):
    def __init__(self, pfad, meta):
        BaseDocTemplate.__init__(
            self, pfad, pagesize=A4,
            leftMargin=RAND_LINKS, rightMargin=RAND_RECHTS, topMargin=RAND_OBEN, bottomMargin=RAND_UNTEN,
            title=meta['titel'], author=meta['autor'],
            subject='%s, Fassung %s vom %s' % (meta['titel'], meta['fassung'], meta['datum']),
            creator='tools/handbuch-pdf.py (reportlab %s)' % reportlab.Version,
            keywords=['JG TacRelay', 'Handbuch', meta['fassung']],
            lang='de', invariant=1, displayDocTitle=1)
        self.meta = meta
        r1 = Frame(self.leftMargin, self.bottomMargin, self.width, self.height, id='titel',
                   leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
        r2 = Frame(self.leftMargin, self.bottomMargin, self.width, self.height, id='inhalt',
                   leftPadding=0, rightPadding=0, topPadding=0, bottomPadding=0)
        self.addPageTemplates([PageTemplate(id='titel', frames=[r1]),
                               PageTemplate(id='inhalt', frames=[r2], onPageEnd=self.kopf_fuss)])
        self.kapitel = ''
        self._letzte = -1
        self._umriss = False
        self.wiederholt = []
        self.rest = {}

    def beforeDocument(self):
        self.kapitel = ''
        self._letzte = -1
        self._umriss = False
        self.wiederholt = []         # (seite, kopftext) je Fortsetzung einer Tabelle, nur letzter Lauf
        self.rest = {}

    def kopf_fuss(self, c, doc):
        c.saveState()
        b, h = A4
        links = self.leftMargin
        rechts = self.leftMargin + self.width
        y_kopf = h - self.topMargin + 7 * mm
        y_fuss = self.bottomMargin - 11 * mm
        c.setFont(SCHRIFT, 8)
        c.setFillColor(FARBE_GRAU)
        c.setStrokeColor(FARBE_LINIE)
        c.setLineWidth(0.5)
        c.drawString(links, y_kopf, kopfzeile(self.kapitel, self.width))
        c.line(links, y_kopf - 2.5 * mm, rechts, y_kopf - 2.5 * mm)
        c.line(links, y_fuss + 4 * mm, rechts, y_fuss + 4 * mm)
        c.drawString(links, y_fuss, fusszeile(self.meta))
        c.drawRightString(rechts, y_fuss, 'Seite %d' % doc.page)
        c.restoreState()

    def afterFlowable(self, f):
        if self.frame is not None:
            self.rest[self.page] = self.frame._y - self.frame._y1p     # freier Platz unten, letzter Stand
        if isinstance(f, Tabelle) and f.fortsetzung and f.kopftext:
            self.wiederholt.append((self.page, f.kopftext))
        kf = getattr(f, 'kf', None)
        if not kf:
            return
        ebene, text, key, toc = kf
        ebene = min(ebene, self._letzte + 1)
        self._letzte = ebene
        c = self.canv
        if not self._umriss:
            c.showOutline()
            self._umriss = True
        oben = self.frame._y + f.getSpaceAfter() + f.height + 4
        c.bookmarkHorizontalAbsolute(key, oben)
        c.addOutlineEntry(text, key, level=ebene, closed=True)
        if ebene == 0:
            self.kapitel = text
        if toc:
            # mit key verknuepft reportlab Titel und Seitenzahl des Eintrags mit dem Abschnitt
            self.notify('TOCEntry', (ebene, xml(text), self.page, key))


# ---------------------------------------------------------------------------
# Quellen und Metadaten
# ---------------------------------------------------------------------------

def lesen(pfad):
    with open(pfad, encoding='utf-8-sig') as d:
        return d.read().replace('\r\n', '\n').replace('\r', '\n').split('\n')


def metadaten(bl0):
    titel = next((b[2] for b in bl0 if b[0] == 'ueber' and b[1] == 1), None)
    tab = next((b for b in bl0 if b[0] == 'tabelle'), None)
    if not titel or not tab:
        raise SystemExit('00-Datei: Titel (#) oder Kopftabelle fehlt')
    werte = {}
    for r in [tab[1]] + tab[3]:
        if len(r) >= 2 and r[0].strip():
            werte[r[0].strip()] = r[1].strip()
    try:
        fassung = re.match(r'\S+', werte['Fassung']).group(0)
        datum = werte['Datum']
        autor = schlicht(werte['Maintainer und Rechteinhaber'])
    except (KeyError, AttributeError):
        raise SystemExit('00-Datei: Fassung, Datum oder Maintainer fehlt in der Kopftabelle')
    return {'titel': schlicht(titel), 'fassung': fassung, 'datum': datum, 'autor': autor}


def struktur_schreiben(quellen, pfad):
    with open(pfad, 'w', encoding='utf-8') as aus:
        def gib(bl, tiefe):
            for b in bl:
                pre = '  ' * tiefe
                if b[0] == 'absatz':
                    aus.write('%sABSATZ %s\n' % (pre, b[1][:90]))
                elif b[0] == 'ueber':
                    aus.write('%sUEBER%d %s\n' % (pre, b[1], b[2]))
                elif b[0] == 'code':
                    aus.write('%sCODE %d Zeilen: %s\n' % (pre, len(b[1]), (b[1][0] if b[1] else '')[:80]))
                elif b[0] == 'tabelle':
                    aus.write('%sTABELLE %d Spalten, %d Zeilen: %s\n' % (pre, len(b[1]), len(b[3]),
                                                                       ' | '.join(b[1])[:80]))
                elif b[0] == 'zitat':
                    aus.write('%sZITAT\n' % pre)
                    gib(b[1], tiefe + 1)
                elif b[0] == 'liste':
                    aus.write('%sLISTE %s ab %d, %d Punkte\n' % (pre, 'geordnet' if b[1] else '-', b[2], len(b[3])))
                    for k, p in enumerate(b[3]):
                        aus.write('%s  PUNKT %d\n' % (pre, k + 1))
                        gib(p, tiefe + 2)
        for name, bl in quellen:
            aus.write('=== %s\n' % name)
            gib(bl, 0)


# ---------------------------------------------------------------------------
# Rundweg: das PDF mit pypdf zuruecklesen
# ---------------------------------------------------------------------------

BASIS14 = {'/Helvetica', '/Helvetica-Bold', '/Helvetica-Oblique', '/Helvetica-BoldOblique',
           '/Courier', '/Courier-Bold', '/Courier-Oblique', '/Courier-BoldOblique'}
ZEICHEN = 'äöüÄÖÜßš§µ'


def ohne_leer(s):
    return re.sub(r'\s+', '', s)


def pruefen(pfad, bauer, meta, doc, dateinamen):
    from pypdf import PdfReader
    befunde = []
    r = PdfReader(pfad)
    seiten = len(r.pages)
    print('Seiten: %d' % seiten)

    info = r.metadata
    lang = r.trailer['/Root'].get('/Lang')
    print('Metadaten: Titel=%r Autor=%r Thema=%r Sprache=%r Erstellt=%r Programm=%r'
          % (info.title, info.author, info.subject, lang, info.get('/CreationDate'), info.get('/Creator')))
    if info.title != meta['titel'] or info.author != meta['autor'] or str(lang) != 'de':
        befunde.append('Metadaten weichen ab')
    if not meta['autor'] or '[' in meta['autor']:
        befunde.append('Autor ist leer oder ein Platzhalter: %r' % meta['autor'])

    # Schriften
    gefunden = {}
    for nr, s in enumerate(r.pages, 1):
        res = s.get('/Resources')
        fonts = res.get_object().get('/Font') if res is not None else None
        if not fonts:
            continue
        for _, ref in fonts.get_object().items():
            fo = ref.get_object()
            base = str(fo.get('/BaseFont'))
            eintrag = gefunden.setdefault(base, {'subtype': str(fo.get('/Subtype')), 'deskriptor': False,
                                                 'datei': False, 'kodierung': str(fo.get('/Encoding')),
                                                 'seiten': 0})
            eintrag['seiten'] += 1
            if '/FontDescriptor' in fo:
                eintrag['deskriptor'] = True
                fd = fo['/FontDescriptor'].get_object()
                if any(k in fd for k in ('/FontFile', '/FontFile2', '/FontFile3')):
                    eintrag['datei'] = True
    for base, e in sorted(gefunden.items()):
        print('Schrift %s: %s, Kodierung %s, FontDescriptor %s, eingebettete Datei %s, auf %d Seiten'
              % (base, e['subtype'], e['kodierung'], 'ja' if e['deskriptor'] else 'nein',
                 'ja' if e['datei'] else 'nein', e['seiten']))
        if base not in BASIS14 or e['deskriptor'] or e['datei'] or e['subtype'] != '/Type1':
            befunde.append('Schrift %s ist keine nicht eingebettete Standardschrift' % base)
    roh = open(pfad, 'rb').read()
    for marke in (b'/FontFile', b'/FontFile2', b'/FontFile3', b'/FontDescriptor', b'/CIDFontType', b'/TrueType'):
        anzahl = roh.count(marke)
        print('Rohdaten: %s %d mal' % (marke.decode(), anzahl))
        if anzahl:
            befunde.append('Rohdaten enthalten %s' % marke.decode())

    # Lesezeichen gegen Ueberschriften (gleiche Kappung wie beim Bau: keine Ebene ueberspringen)
    umriss = []

    def flach(liste_, tiefe):
        for e in liste_:
            if isinstance(e, list):
                flach(e, tiefe + 1)
            else:
                umriss.append((tiefe, e.title, r.get_destination_page_number(e)))
    flach(r.outline, 0)
    erwartet = []
    letzte = -1
    for kf in bauer.ueberschriften[:1] + [(0, 'Inhaltsverzeichnis', None, False)] + bauer.ueberschriften[1:]:
        e = min(kf[0], letzte + 1)
        letzte = e
        erwartet.append((e, kf[1]))
    print('Lesezeichen: %d, erwartet %d; oberste Ebene %d, Ebene 1: %d, Ebene 2: %d, Ebene 3: %d'
          % (len(umriss), len(erwartet), *[sum(1 for u in umriss if u[0] == x) for x in range(4)]))
    if [(u[0], u[1]) for u in umriss] != erwartet:
        befunde.append('Lesezeichen passen nicht zu den Ueberschriften')
        for a, b in zip([(u[0], u[1]) for u in umriss], erwartet):
            if a != b:
                print('  erste Abweichung: PDF %r, erwartet %r' % (a, b))
                break

    # Text je Seite. Kopf- und Fusszeile zeichnet onPageEnd zuletzt; sie stehen also am Ende des
    # Seitentexts und werden dort erwartet und abgezogen. Vergleiche laufen ohne Leerraum.
    breite = A4[0] - RAND_LINKS - RAND_RECHTS
    texte = []
    ganz = []                    # Text jeder Seite samt Kopf, Fuss und Inhaltsverzeichnis, wie ausgelesen
    ohne_kopf = []
    for p, s in enumerate(r.pages):
        ganz.append(s.extract_text() or '')
        t = ohne_leer(ganz[-1])
        if p == 0:
            texte.append(t)
            continue
        kap = [tt for (e, tt, ss) in umriss if e == 0 and ss <= p][-1]
        schwanz = ohne_leer(kopfzeile(kap, breite) + fusszeile(meta) + 'Seite %d' % (p + 1))
        if t.endswith(schwanz):
            texte.append(t[:-len(schwanz)])
        else:
            ohne_kopf.append(p + 1)
            texte.append(t)
    print('Kopf- und Fusszeile (Kapitel; Titel, Fassung, Datum; Seite N) gefunden auf %d von %d Seiten'
          ' (Titelseite ohne)' % (seiten - 1 - len(ohne_kopf), seiten - 1))
    if ohne_kopf:
        befunde.append('Kopf- oder Fusszeile fehlt oder weicht ab auf Seiten %s' % ohne_kopf[:20])

    # jede Ueberschrift auf ihrer Seite, ohne Kopfzeile
    fehlt = []
    for tiefe, titel, seite in umriss:
        if ohne_leer(titel) not in texte[seite]:
            fehlt.append((titel, seite + 1))
    print('Kapitel (oberste Ebene) mit Seite:')
    for (e, t, s) in umriss:
        if e == 0:
            print('  S. %3d  %s' % (s + 1, t))
    print('Ueberschriften im Text ihrer Seite (ohne Kopfzeile) gefunden: %d von %d'
          % (len(umriss) - len(fehlt), len(umriss)))
    for t, s in fehlt:
        print('  FEHLT: %r auf Seite %d' % (t, s))
    if fehlt:
        befunde.append('%d Ueberschriften nicht auf ihrer Seite gefunden' % len(fehlt))

    # Inhaltsverzeichnis: Titel und Seitenzahl je Zeile (Layout-Auszug) gegen die Lesezeichen
    s_inh = next(s for (e, t, s) in umriss if t == 'Inhaltsverzeichnis')
    s_nach = min(s for (e, t, s) in umriss if e == 0 and s > s_inh)
    umriss_ohne = umriss[:1] + umriss[2:]
    soll = [(ohne_leer(kf[1]), u[2] + 1) for kf, u in zip(bauer.ueberschriften, umriss_ohne) if kf[3]]
    ist = []
    fuss = ohne_leer(fusszeile(meta))
    for p in range(s_inh, s_nach):
        puffer = ''
        for zeile in r.pages[p].extract_text(extraction_mode='layout').split('\n'):
            z = zeile.strip()
            if not z or z == 'Inhaltsverzeichnis' or fuss in ohne_leer(z):
                continue
            m = re.match(r'^(.*?)\s*(?:\.\s*){2,}(\d+)$', z)
            if m:
                ist.append((ohne_leer(puffer + m.group(1)), int(m.group(2))))
                puffer = ''
            else:
                puffer += z
    falsch = [(a, b) for a, b in zip(ist, soll) if a != b]
    print('Inhaltsverzeichnis: Seiten %d bis %d, %d Eintraege gelesen, %d erwartet, %d abweichend'
          % (s_inh + 1, s_nach, len(ist), len(soll), len(falsch) + abs(len(ist) - len(soll))))
    for a, b in falsch[:10]:
        print('  gelesen %r, erwartet %r' % (a, b))
    if falsch or len(ist) != len(soll):
        befunde.append('Inhaltsverzeichnis stimmt nicht mit den Lesezeichen ueberein')
    namen = set(str(n) for n in r.named_destinations)
    links = 0
    ohne_ziel = 0
    for p in range(s_inh, s_nach):
        for a in r.pages[p].get('/Annots') or []:
            a = a.get_object()
            if a.get('/Subtype') != '/Link':
                continue
            links += 1
            d = a.get('/Dest')
            if d is None and '/A' in a:
                d = a['/A'].get_object().get('/D')
            if d is None or (not isinstance(d, list) and str(d) not in namen):
                ohne_ziel += 1
    print('Spruenge im Inhaltsverzeichnis: %d (erwartet %d, Titel und Seitenzahl je Eintrag), ohne Ziel: %d'
          % (links, 2 * len(soll), ohne_ziel))
    if links != 2 * len(soll) or ohne_ziel:
        befunde.append('Spruenge im Inhaltsverzeichnis: %d statt %d, %d ohne Ziel' % (links, 2 * len(soll), ohne_ziel))
    # Ebenen des Inhaltsverzeichnisses: Kapitel, erste Ebene, und die zweite nur in Teil E (E2.1 ... E3.17)
    je_ebene = [0, 0, 0]
    ausserhalb_e = []
    kap = ''
    for kf in bauer.ueberschriften:
        if kf[0] == 0:
            kap = kf[1]
        if kf[3]:
            je_ebene[min(kf[0], 2)] += 1
            if kf[0] >= 2 and not kap.startswith('Teil E'):
                ausserhalb_e.append(kf[1])
    print('Inhaltsverzeichnis nach Ebenen: Kapitel %d, erste Ebene %d, zweite Ebene %d (nur Teil E)' % tuple(je_ebene))
    if ausserhalb_e or not je_ebene[2]:
        befunde.append('Zweite Ebene im Inhaltsverzeichnis ausserhalb von Teil E oder gar nicht: %s' % ausserhalb_e[:5])

    # Dateinamen der Kapitel stehen nirgends im PDF (auch nicht im Inhaltsverzeichnis); andere .md-Namen sind Belege
    alles = ohne_leer(''.join(ganz))
    for name in dateinamen:
        stamm = os.path.splitext(name)[0]
        if ohne_leer(stamm) in alles:
            befunde.append('Kapiteldatei %s im PDF genannt' % name)
    andere = sorted(set(re.findall(r'[\w\-]+\.md\b', '\n'.join(ganz))))
    print('Dateinamen der Kapitel im PDF: %d (gesucht %d Namen ohne .md); andere .md-Namen im Text (Belege): %s'
          % (sum(1 for n in dateinamen if ohne_leer(os.path.splitext(n)[0]) in alles), len(dateinamen),
             ', '.join(andere)))
    print('Gruppenzeilen in Tabellen (ueber alle Spalten, mit der Folgezeile zusammen): %d' % bauer.gruppenzeilen)

    # Fortsetzungen von Tabellen beginnen oben auf der Seite mit der wiederholten Kopfzeile
    nicht_oben = []
    wiederholt = doc.wiederholt
    for seite, kopf in wiederholt:
        k = ohne_leer(kopf)
        if texte[seite - 1].startswith(k):
            texte[seite - 1] = texte[seite - 1][len(k):]
        else:
            nicht_oben.append(seite)
    print('Wiederholte Tabellenkoepfe auf Folgeseiten: %d, fuer den Textvergleich abgezogen' % len(wiederholt))
    if nicht_oben:
        befunde.append('Wiederholter Tabellenkopf nicht oben auf Seite %s' % nicht_oben)

    # Zeichen: sichtbarer Text der Quelle gegen den Text aller Seiten ausser dem Inhaltsverzeichnis
    rumpf =''.join(t for k, t in enumerate(texte) if not (s_inh <= k < s_nach))
    quelle = ohne_leer('\n'.join(bauer.sichtbar))
    print('Zeichen: Quelle / PDF')
    for z in ZEICHEN:
        a, b = quelle.count(z), rumpf.count(z)
        print('  %s U+%04X: %d / %d%s' % (z, ord(z), a, b, '' if a == b else '  ABWEICHUNG'))
        if a != b:
            befunde.append('Zeichen %s: Quelle %d, PDF %d' % (z, a, b))
    for marke in ('**', '`', '\\|'):
        a, b = quelle.count(marke), rumpf.count(marke)
        print('  Markdown-Rest %r: Quelle sichtbar %d / PDF %d' % (marke, a, b))
        if a != b:
            befunde.append('Markdown-Rest %r: %d gegen %d' % (marke, a, b))
    print('Zeichen ohne Leerraum: Quelle %d, PDF %d' % (len(quelle), len(rumpf)))
    if quelle != rumpf:
        befunde.append('Text ohne Leerraum weicht ab: Quelle %d, PDF %d Zeichen' % (len(quelle), len(rumpf)))
        k = next((i for i, (x, y) in enumerate(zip(quelle, rumpf)) if x != y), min(len(quelle), len(rumpf)))
        print('  erste Abweichung bei Zeichen %d:' % k)
        print('    Quelle: %r' % quelle[max(0, k - 60):k + 60])
        print('    PDF:    %r' % rumpf[max(0, k - 60):k + 60])
    else:
        print('  Text ohne Leerraum: Quelle und PDF sind Zeichen fuer Zeichen gleich')

    # Leerraum: Seiten, die zu mehr als einem Drittel leer bleiben, ohne dass danach ein Kapitel beginnt
    kapitelstart = set(s + 1 for (e, t, s) in umriss if e == 0)
    luecken = [(p, int(100 * rest / doc.height)) for p, rest in sorted(doc.rest.items())
               if rest > doc.height / 3 and (p + 1) not in kapitelstart and p < seiten]
    print('Seiten mit mehr als einem Drittel Leerraum ohne folgenden Kapitelanfang: %d %s'
          % (len(luecken), ['S. %d (%d %%)' % x for x in luecken]))

    # Tabellenzellen, in denen reportlab ein Wort oder ein Codestueck mitten durch brechen musste (splitLongWords)
    gebrochen = [p for p in bauer.zellen if getattr(p, '_splitLongWordCount', 0)]
    print('Tabellenzellen mit mitten durch gebrochenem Wort oder Codestueck: %d von %d'
          % (len(gebrochen), len(bauer.zellen)))
    for p in gebrochen[:40]:
        print('  %d x in %r (%s)' % (p._splitLongWordCount, p.tr_zelle[:100], p.tr_spalte))
    if gebrochen:
        befunde.append('%d Tabellenzellen brechen ein Wort oder Code mitten durch' % len(gebrochen))
    absaetze = [x for x in bauer.f if isinstance(x, Paragraph) and not isinstance(x, Ueberschrift)
                and getattr(x, '_splitLongWordCount', 0)]
    print('Absaetze ausserhalb von Tabellen mit gebrochenem Wort: %d' % len(absaetze))
    for p in absaetze[:5]:
        print('  %r' % p.text[:100])

    leer = [k + 1 for k, t in enumerate(texte) if not t]
    if leer:
        print('Seiten ohne Text: %s' % leer)
        befunde.append('Seiten ohne Text: %s' % leer)

    h = hashlib.sha256(roh).hexdigest()
    print('SHA-256: %s' % h)
    print('Groesse: %d Byte' % len(roh))
    print('Befunde: %d' % len(befunde))
    for b in befunde:
        print('  - %s' % b)
    return befunde, h, seiten


# ---------------------------------------------------------------------------

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser(description='Handbuch JG TacRelay als PDF bauen')
    ap.add_argument('--quelle', default=QUELLE, help='Ordner docs\\handbuch')
    ap.add_argument('--ziel', default=ZIEL, help='Ausgabeordner')
    ap.add_argument('--zeitstempel', help='JJJJMMTT_HHMM; ohne Angabe die aktuelle Uhrzeit')
    ap.add_argument('--ohne-pruefung', action='store_true', help='kein Rundweg mit pypdf')
    ap.add_argument('--struktur', help='erkannte Markdown-Bloecke in diese Textdatei schreiben')
    ap.add_argument('--ueberschreiben', action='store_true', help='vorhandene Ausgabe ersetzen')
    a = ap.parse_args()

    dateien = sorted(glob.glob(os.path.join(a.quelle, '[0-9][0-9]-*.md')))
    if not dateien or not os.path.basename(dateien[0]).startswith('00-'):
        raise SystemExit('Keine Kapiteldateien oder keine 00-Datei in %s' % a.quelle)
    for pfad in [a.ziel] + ([a.struktur] if a.struktur else []):
        if not os.path.abspath(pfad).lower().startswith(ZONE.lower()):
            raise SystemExit('Nur unter %s: %s' % (ZONE, os.path.abspath(pfad)))
    stempel = a.zeitstempel or datetime.datetime.now().strftime('%Y%m%d_%H%M')
    try:
        zeit = datetime.datetime.strptime(stempel, '%Y%m%d_%H%M')
    except ValueError:
        raise SystemExit('Zeitstempel bitte als JJJJMMTT_HHMM')
    os.environ['SOURCE_DATE_EPOCH'] = str(int(time.mktime(zeit.timetuple())))

    quellen = [(os.path.basename(p), bloecke(lesen(p))) for p in dateien]
    if a.struktur:
        struktur_schreiben(quellen, a.struktur)
    meta = metadaten(quellen[0][1])
    ziel = os.path.join(a.ziel, '%s_JG-TacRelay-Handbuch-%s.pdf' % (stempel, meta['fassung']))
    if os.path.exists(ziel) and not a.ueberschreiben:
        raise SystemExit('Gibt es schon: %s (--ueberschreiben)' % ziel)
    os.makedirs(a.ziel, exist_ok=True)

    print('Quellen: %s' % ', '.join(os.path.basename(p) for p in dateien))
    print('Titel %r, Fassung %s, Datum %s, Autor %r' % (meta['titel'], meta['fassung'], meta['datum'], meta['autor']))
    doc = Dokument(ziel, meta)
    bauer = Bauer(doc.width)
    story = bauer.story(quellen, meta)
    doc.multiBuild(story)
    print('Geschrieben: %s' % ziel)
    if a.ohne_pruefung:
        print('SHA-256: %s' % hashlib.sha256(open(ziel, 'rb').read()).hexdigest())
        return 0
    befunde, _, _ = pruefen(ziel, bauer, meta, doc, [os.path.basename(p) for p in dateien])
    return 1 if befunde else 0


if __name__ == '__main__':
    sys.exit(main())
