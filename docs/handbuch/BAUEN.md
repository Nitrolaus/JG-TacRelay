# Handbuch als PDF bauen

Aus den Kapiteldateien dieses Ordners entsteht ein A4-PDF. Diese Datei beschreibt, womit, wohin und was dabei geprüft
wird. Sie gehört selbst nicht zum Handbuch und kommt nicht ins PDF.

## Was gelesen wird

Nur Dateien mit zwei Ziffern vorn, in dieser Reihenfolge: `00-titel-und-inhalt.md` bis `95-offene-punkte.md`. Eine neue
Kapiteldatei kommt also mit passender Nummer von selbst dazu; `BAUEN.md` und andere Dateien ohne Ziffern bleiben draußen.

Aus `00-titel-und-inhalt.md` kommen auch die Angaben für Dateiname, Fußzeile und Metadaten:

| Angabe | Quelle |
| --- | --- |
| Titel | die Überschrift `# JG TacRelay - Handbuch` |
| Fassung | erstes Wort der Zeile **Fassung** der Kopftabelle (heute `0.13.0`) |
| Datum | Zeile **Datum** (heute `19.10.2026`) |
| Autor | Zeile **Maintainer und Rechteinhaber** (heute `Sarevoc`, Anf. A59) |

Fehlt eine davon, bricht das Skript ab.

## Was im PDF steht

- Titelseite: Überschrift, Kopftabelle und erster Absatz aus `00-titel-und-inhalt.md`.
- Inhaltsverzeichnis mit Seitenzahlen in drei Stufen: Kapitel, die erste Ebene darunter und, nur in Teil E, die zweite
  (E2.1 bis E3.17); Titel und Seitenzahl springen zum Abschnitt.
- Die Dateinamen der Kapitel stehen nirgends im PDF, auch nicht im Inhaltsverzeichnis. Andere Dateinamen wie
  `PROJEKT.md` bleiben stehen, wo der Text sie als Beleg nennt.
- Jeder Teil ist ein Kapitel und beginnt auf einer neuen Seite: Kapitel 1, Teil A bis F, Glossar, Offene Punkte. Die
  Abschnitte des Titelteils (Für wen welcher Teil ist, Inhalt, Lesehilfe) sind ebenfalls Kapitel, nur das erste beginnt
  auf einer neuen Seite.
- Unter einem Kapitel zählen nur die Stufen, die dort vorkommen: In Kapitel 1 ist `### 1.1` die erste Ebene, in Teil A
  ist es `## A1`.
- Kopfzeile mit dem Kapitel, Fußzeile mit Titel, Fassung, Datum und `Seite N`; die Titelseite hat beides nicht.
- Lesezeichen für alle Kapitel und Abschnitte bis zur dritten Ebene, anfangs zugeklappt.
- Metadaten: Titel, Autor `Sarevoc`, Thema mit Fassung und Datum, Sprache `de`.

## Schriften

Nur die PDF-Standardschriften Helvetica (normal, fett, kursiv) und Courier. Sie werden nicht eingebettet: Das PDF nennt
nur ihren Namen, das Anzeigeprogramm nimmt seine eigene Entsprechung. Damit steckt keine fremde Schriftdatei im PDF.

Umlaute, `ß`, `§` und `µ` gehen über die Kodierung WinAnsi. Zeichen außerhalb davon (Pfeile, Kästchenzeichen,
Gedankenstriche) können diese Schriften nicht darstellen. Die Kapitel enthalten heute keine: Am 01.10.2026 (Fassung
0.4.1) und am 02.10.2026 (Arbeitsstand der Fassung 0.5.0) lagen alle Zeichen der neun Dateien in WinAnsi (cp1252);
vor jedem Bau ist das erneut zu prüfen (am Code des Skripts geprüft: Es prüft die Zeichen nicht eigens).

## Werkzeug

| Was | Wo |
| --- | --- |
| Skript | `tools\handbuch-pdf.py` im Repository; bis zum 01.10.2026 lag es als `handbuch_pdf.py` in der Arbeitsablage |
| Python | `C:\Claude\tools\office\.venv\Scripts\python.exe` |
| reportlab 5.0.1 | in derselben Umgebung, Lizenz BSD, von PyPI; setzt das PDF |
| charset-normalizer 3.5.2 | Abhängigkeit von reportlab, Lizenz MIT, von PyPI |
| pypdf 6.15.0 | in derselben Umgebung; liest das PDF für die Prüfung zurück |

Die Bibliotheken bauen das PDF, sie stecken nicht darin. Das Repository bringt sie nicht mit; gebaut wird mit der
Umgebung oben.

## Aufruf

Im Wurzelordner des Repositorys oder Worktrees, in PowerShell, die lange Ausgabe gleich in eine Datei:

```
C:\Claude\tools\office\.venv\Scripts\python.exe tools\handbuch-pdf.py *> C:\Claude\tmp\handbuch-pdf.txt
```

| Option | Wirkung |
| --- | --- |
| `--quelle <ordner>` | Ordner mit den Kapiteldateien; ab Werk `docs\handbuch` neben dem Ordner `tools`, in dem das Skript liegt |
| `--ziel <ordner>` | Ausgabeordner; ab Werk `C:\Claude\output\test` |
| `--zeitstempel JJJJMMTT_HHMM` | Zeitstempel für Dateiname und Erstelldatum; ohne Angabe die aktuelle Uhrzeit |
| `--ueberschreiben` | eine vorhandene Datei gleichen Namens ersetzen; ohne die Option bricht das Skript dann ab |
| `--ohne-pruefung` | nur bauen, nicht zurücklesen |
| `--struktur <datei>` | die erkannten Markdown-Blöcke als Text ausgeben, zum Nachsehen |

Ziel und Strukturdatei müssen unter `C:\Claude\` liegen, sonst bricht das Skript ab, bevor es etwas schreibt.

Die Ausgabe heißt `<ziel>\JJJJMMTT_HHMM_JG-TacRelay-Handbuch-<fassung>.pdf`, also etwa
`C:\Claude\output\test\20261002_1200_JG-TacRelay-Handbuch-0.5.0.pdf`.

Derselbe Zeitstempel und dieselben Kapiteldateien ergeben byte-gleich dieselbe Datei. Das Erstelldatum im PDF ist der
Zeitstempel, in UTC.

## Prüfung

Nach dem Bauen liest das Skript das PDF mit pypdf zurück. Exitcode 0 heißt: keine Abweichung (der Leerraum wird nur
gemeldet).

| Prüfung | Erwartet |
| --- | --- |
| Metadaten | Titel, Autor und Sprache wie oben; der Autor ist nicht leer und kein Platzhalter in eckigen Klammern |
| Schriften | auf allen Seiten nur Helvetica und Courier als Type1, ohne FontDescriptor und ohne Schriftdatei; in den Rohdaten kein `/FontFile`, `/FontFile2`, `/FontFile3`, `/FontDescriptor`, `/CIDFontType`, `/TrueType` |
| Lesezeichen | eins je Überschrift, in Reihenfolge und Ebene |
| Überschriften | jede steht auf der Seite ihres Lesezeichens, ohne die Kopfzeile mitzuzählen |
| Kopf- und Fußzeile | auf jeder Seite außer der Titelseite, mit dem richtigen Kapitel und der richtigen Seitenzahl |
| Inhaltsverzeichnis | jeder Eintrag mit derselben Seite wie sein Lesezeichen; zwei Sprünge je Eintrag, jeder mit Ziel; die Einträge je Stufe werden gezählt, die dritte Stufe gibt es nur in Teil E und dort mindestens einmal |
| Dateinamen | keiner der Kapiteldateinamen (ohne `.md`) im Text irgendeiner Seite; die übrigen `.md`-Namen im Text werden aufgezählt, sie sind Belege |
| Gruppenzeilen | ihre Zahl wird gemeldet |
| Text | ohne Leerraum Zeichen für Zeichen gleich dem sichtbaren Text der Quellen, also auch jedes `ä`, `ö`, `ü`, `Ä`, `Ö`, `Ü`, `ß`, `š`, `§`, `µ`; `**`, Backticks und `\|` so oft wie im sichtbaren Text (heute nie) |
| Leerraum | gemeldet wird jede Seite, die zu mehr als einem Drittel leer bleibt, ohne dass danach ein Kapitel beginnt |
| Tabellenzellen | keine Zelle, in der reportlab ein Wort oder ein Stück Code mitten durch brechen musste (Zähler `_splitLongWordCount` je Zellabsatz); jede solche Zelle wird mit Spalte, Breite und Minimum genannt und ist ein Befund |
| Absätze | dasselbe für Absätze außerhalb von Tabellen, nur gemeldet |
| SHA-256 | wird ausgegeben |

Die Ausgabe ist lang; am besten in eine Datei umleiten und dort nachlesen.

## Was das Skript an Markdown versteht

Überschriften `#` bis `####`, Absätze (umbrochene Zeilen werden zusammengezogen), Listen mit `-` und `1.`, auch
verschachtelt und mit Codeblock im Listenpunkt, Codeblöcke mit drei Backticks, Tabellen mit `|` (ein `\|` in einer Zelle
ist ein senkrechter Strich), Zitate mit `>`, dazu `Code`, **fett**, *kursiv* und Backslash vor einem Satzzeichen.

Nicht umgesetzt, weil die Kapitel es nicht brauchen: Links, Bilder, HTML, Überschriften mit Unterstreichung. Ein `_` ist
immer ein Schriftzeichen, nie eine Auszeichnung.

Ein Codeblock muss unter seinem Listenpunkt eingerückt sein, sonst endet die Liste vor ihm (so auch auf GitHub).

## Darstellung

- Codezeilen, die breiter sind als die Seite, brechen um. Die Fortsetzung ist eingerückt und links mit einem kleinen
  Haken markiert. Der Haken ist gezeichnet, kein Zeichen, und kommt beim Kopieren nicht mit; der Umbruch selbst
  schon.
- Tabellen, Spaltenbreiten: Jede Spalte bekommt mindestens die Breite ihrer längsten unteilbaren Einheit, also des
  längsten Worts oder, bei Code, des längsten Stücks zwischen zwei erlaubten Trennstellen, samt der Satzzeichen, die
  daran kleben (höchstens 40 % der Breite). Spalten mit überwiegend Fließtext und im Mittel mehr als 120 Punkt Text je
  Zeile bekommen mindestens ein Fünftel der Breite, soweit das neben den anderen Minima passt. Den Rest verteilt das
  Skript nach der Textmenge der Spalten; keine Spalte wird breiter, als ihr längster Eintrag in einer Zeile braucht.
- Tabellen, Code in Zellen: Passt ein Stück Code nicht in seine Zelle, bricht es nur hinter `/`, Backslash, `.`, `-` und
  `_` um (gesetzter Zeilenumbruch, kein zusätzliches Zeichen). Ein Wort oder Codestück ohne solche Stelle, das breiter
  ist als die Spalte, meldet die Prüfung.
- Tabellen, Gruppenzeilen: Eine Zeile, deren erste Zelle ganz fett ist und deren übrige Zellen leer sind (etwa
  "Umzugsabend ..." in B20.2), geht über alle Spalten, ist grau hinterlegt und bleibt mit der Zeile danach auf einer
  Seite (`NOSPLIT`).
- Tabellen: Die Kopfzeile wiederholt sich auf Folgeseiten.
- Eine Überschrift der ersten Ebene unter einem Kapitel beginnt eine neue Seite, wenn unter ihr weniger als 110 Punkt
  (etwa 3,9 cm) Platz bleiben; bei der zweiten Ebene 85, bei der dritten 70 Punkt. Folgt auf die Überschrift eine
  Tabelle oder ein Codeblock, wahlweise nach einem Absatz, muss auch deren Anfang noch passen (Kopf und erste Zeile der
  Tabelle, drei Zeilen Code), solange das zusammen unter 360 Punkt bleibt; sonst stünde die Überschrift allein am
  Seitenende.
- Keine Silbentrennung; Absätze sind linksbündig.
- Absätze und Listenpunkte: Die letzte Zeile steht nie allein oben auf einer Seite und die erste nie allein unten
  (`allowWidows=0`, `allowOrphans=0`); dann wandert eine Zeile mehr auf die Folgeseite. Seit dem 01.10.2026, vorher
  stand oben auf S. 152 "(E3.11)." allein.

## Letzter Bau

Wird nach jedem Bau, der weitergegeben wird, nachgetragen. Die Tabelle unten ist der Bau der Fassung 0.13.0 vom 19.10.2026,
der ersten als JG TacRelay.

| Angabe | Wert |
| --- | --- |
| Datei | `C:\Claude\output\test\20261019_1405_JG-TacRelay-Handbuch-0.13.0.pdf`, 1.135.343 Byte |
| Seiten | 261, davon 4 Seiten Inhaltsverzeichnis mit 159 Einträgen (12 Kapitel, 119 der ersten und 28 der zweiten Ebene); 14 Lesezeichen oberster Ebene, 461 insgesamt |
| SHA-256 | `94210b7821411ccc613789415b560193310e134de8eca1bb33de255c0998186c` |
| Prüfung | 19.10.2026 14:05, Exitcode 0, keine Abweichung; 735.346 Zeichen ohne Leerraum gleich den Quellen; alle 461 Überschriften auf der Seite ihres Lesezeichens; 0 von 6.223 Tabellenzellen und 0 Absätze mit mitten durch gebrochenem Wort; kein Kapiteldateiname im PDF; 3 Gruppenzeilen; keine Seite zu mehr als einem Drittel leer; nur Helvetica und Courier, ohne FontDescriptor und ohne eingebettete Datei |
| Nachbau | derselbe Zeitstempel (`--zeitstempel 20261019_1405`) in einen anderen Ordner ergibt dieselbe SHA-256 (am 19.10.2026 nachgebaut, gleich) |
| Quellen | die neun Kapiteldateien und `tools\handbuch-pdf.py` im Stand des Commits, der diesen Abschnitt eingetragen hat |
| Vorher | `20261002_0202_JG-TacRelay-Handbuch-0.5.0.pdf` (232 Seiten, SHA-256 `7e4f80e9...87c2`), die Fassung 0.5.0, gebaut unter dem früheren Namen des Programms; ein Bau dazwischen ist hier nicht eingetragen |

## Offen

- Querverweise wie "B9.5" sind im PDF Text, keine Sprünge.
