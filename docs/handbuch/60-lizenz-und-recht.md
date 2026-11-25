## Teil F: Lizenz und Recht

> **Keine Rechtsberatung.** Dieser Teil erklärt, wie deutsches und EU-Recht auf JG TacRelay trifft, damit
> Entscheidungen gut vorbereitet sind. Es ist keine Rechtsberatung. Für alles, was verbindlich sein soll (Lizenzwahl,
> Nutzungsbedingungen, Datenschutzhinweis, Vereinbarungen mit einem Betreiber oder Mitarbeiter), frag einen Fachanwalt für IT-Recht.
>
> **Stand der Rechtsangaben.** Gesetze, Urteile und Lizenztexte sind aus dem Kenntnisstand des Verfassers (Claude,
> Stand Juni 2026) wiedergegeben und in diesem Auftrag nicht gegen die amtlichen Texte gelesen worden; es gab keinen
> Netzzugriff. Vor jeder Entscheidung gegenlesen, etwa auf gesetze-im-internet.de, eur-lex.europa.eu und bei den
> Lizenzgebern. Was das Programm betrifft (Dateien, Felder, Fristen, Befehle, Zählungen), ist dagegen am Code, an der
> Doku oder durch eine Messung am 01. oder 02.10.2026 belegt (Stand `95aa78b`, Fassung 0.5.0); die Fundstelle steht
> jeweils dabei.
>
> **Name und Lizenz.** Eigentümer des Programms ist Sarevoc; alle Rechte liegen bei ihm (Anf. A41, Sarevoc
> 19.10.2026, Q23). Unter diesem Namen steht er als Maintainer und Rechteinhaber im Handbuch und in den Quelldateien; das
> ist entschieden (Anf. A59, F3.3). Seit 0.13.1 (25.11.2026) steht JG TacRelay unter der
> Apache-Lizenz 2.0 (F1); F8 zeigt die Wege, die zur Wahl standen.

## F1 Lizenz und Mitwirkende

**Lizenz.** Seit 0.13.1 (25.11.2026) steht JG TacRelay unter der Apache License, Version 2.0 (Sarevoc, 25.11.2026). Der
Lizenztext liegt als `LICENSE` im Wurzelordner des Repositorys. In Kurzform, ohne den Text zu ersetzen:

- Jeder darf JG TacRelay nutzen, ändern und weitergeben, auch als Teil eines eigenen Programms, und zahlt dafür nichts.
- Wer es oder Teile davon weitergibt, legt eine Kopie der Lizenz bei, lässt die Hinweise zu Urheber und Lizenz in den
  Dateien stehen, kennzeichnet Dateien, die er geändert hat, und gibt den Inhalt von `NOTICE` mit (Abschnitt 4 der
  Lizenz).
- Die Lizenz enthält eine Patentlizenz der Mitwirkenden; wer selbst mit der Behauptung klagt, JG TacRelay verletze ein
  Patent, verliert sie (Abschnitt 3).
- Sie erlaubt nicht, Namen oder Marken des Projekts für eigene Zwecke zu benutzen (Abschnitt 6). Gewährleistung und
  Haftung sind ausgeschlossen, soweit das Recht es zulässt (Abschnitte 7 und 8).

**NOTICE.** Die Datei `NOTICE` nennt JG TacRelay, Sarevoc als Maintainer und Rechteinhaber und sein Projekt Joystick
Guardian (https://github.com/Nitrolaus/JoystickGuardian, künftig https://www.joystickguardian.com). Wer Code von
JG TacRelay weitergibt, gibt diesen Hinweis mit. Wer nur Ideen übernimmt und selbst neu schreibt, muss das nach der Lizenz
nicht; über eine Nennung freut sich Sarevoc trotzdem.

**Mitwirkende.** Anforderungen, Entscheidungen, die Tests am echten PC und die Abnahme kommen von Sarevoc. Code, Tests und
Doku schreibt Claude, ein KI-Modell von Anthropic, nach seinen Vorgaben; Claude ist weder Urheber noch Rechteinhaber (F5,
F10). Das steht einmal zentral in `README.md`. Die Meldungsbilder hat Sarevoc mit ChatGPT erstellt (F7.2); die fremden
Bibliotheken stehen mit ihren Lizenzen in `THIRD-PARTY-NOTICES.txt` (F7).

### F1.1 Auf einen Blick

| Frage | Stand 25.11.2026 (Fassung 0.13.1; Angaben zum Code am Stand `95aa78b`) | Abschnitt |
| --- | --- | --- |
| Wem gehört das Programm? | Sarevoc; alle Rechte liegen bei ihm, eine Übergabe an andere ist nicht vorgesehen (Anf. A41, Q23 vom 19.10.2026). Als Maintainer steht er unter diesem Pseudonym (Anf. A59). | F3 |
| Ist der Code urheberrechtlich geschützt? | Offen. Den Code schreibt Claude nach den Vorgaben von Sarevoc; geschützt ist nur, was eine menschliche Schöpfung ist. Das Projekt sollte nicht darauf bauen. | F5 |
| Steckt Code aus SRS oder dem früheren Funkprogramm darin? | Soweit geprüft nein: Historie, Bezeichner, Zeichenketten und native Bibliotheken verglichen. | F6 |
| Welche Fremdbestandteile? | 12 Einträge in `THIRD-PARTY-NOTICES.txt`, alle unter freizügigen Lizenzen. Offen: die Modellgewichte in `opus.dll` und `rnnoise.dll`, dazu ein bisher nicht vermerkter Fall in `webrtc_apm.dll`. | F7 |
| Welche Lizenz? | Apache-2.0, seit 0.13.1 (25.11.2026). Zur Wahl standen sechs Wege: geschlossen, GPL-3.0, AGPL-3.0, MIT, Apache-2.0, EUPL-1.2. | F1, F8 |
| Kopfzeilen in den Quelldateien? | Seit 0.5.0 trägt jede eigene Quelldatei die Zeile "JG TacRelay - Maintainer: Sarevoc" (563 Dateien am Stand `95aa78b`; C#, XAML, PowerShell, Python, Shell, Unit, Setup-Skript, C, C++), ohne Copyright und ohne Lizenz. Ausgenommen sind die zwei Übertragungen. Die Lizenzzeile (Vorschlag mit SPDX) steht noch aus. | F9 |
| Wird Claude genannt? | Ja, einmal zentral in `README.md` und in F1 (Weg B in F10); im öffentlichen Repository nicht als Mitwirkender in den Commits. | F10 |
| Datenschutz? | Der Server hält personenbezogene Daten; Fristen sind eingebaut, einige fehlen. Datenschutzhinweis als Entwurf. | F11 |
| Braucht der Betrieb eine Genehmigung? | Nach bisheriger Einschätzung nein: keine Funkfrequenz, und eine Meldung nach dem Telekommunikationsgesetz trifft nur öffentlich zugängliche Dienste. Die DSGVO ist trotzdem zu beachten. Kein Rechtsrat. | F11.9 |
| Marken? | Star Citizen, Discord und weitere nur beschreibend, ohne fremde Logos. | F12 |
| Repository? | Öffentlich auf GitHub seit 0.13.1; gearbeitet wird in einem privaten Repository, öffentlich geht je Fassung ein bereinigter Stand ohne die privaten Projektunterlagen. | F13 |

## F2 Grundlagen dieses Teils

| Quelle | Daraus entnommen |
| --- | --- |
| `docs\entwurf\ENTSCHEIDUNGEN.md` | Die Anforderungen von Sarevoc, besonders Anf. A41 (Eigentum), Anf. A49 und A53 (Grafiken selbst gezeichnet), Anf. A56 (Admin-PC), Anf. A58 (Recht, Kopfzeilen, Claude, Lizenz, GitHub), Anf. A59 (Name des Maintainers), Anf. A65 (Beispielnamen), dazu Q23 (19.10.2026: alle Rechte bei Sarevoc, Lizenz in eigener Runde) |
| ebenda, aus dem ersten Entwurf | Frage E5 (Open Source?), Sicherheit S7 (keine Telemetrie), Hinweis auf die Datenschutzerklärung für einen Discord-Bot |
| Analyse des früheren Funkprogramms (Arbeitsablage, nicht im Repository) | Herkunft des früheren Funkprogramms (Fork von SRS, GPL-3.0), Befund S8, Stand der Analyse |
| `THIRD-PARTY-NOTICES.txt`, `native\BAUBERICHT.md` | Fremdbestandteile, Lizenzen, Herkunft, ungeklärte Gewichte |
| `docs\BETRIEB.md`, `docs\FUNKPROTOKOLL-v1.md` | Zustandsdateien, Fristen, Journal, Logbuch, Sichtbarkeit von Angaben |
| Quellcode, Stand `95aa78b` (Fassung 0.5.0, 02.10.2026); Zählungen und Messungen, die an `d45e33f` (Fassung 0.4.0) oder `8c73864` (Fassung 0.4.1) gemacht sind, tragen ihren Stand | Felder eines Kontos und Geräts, Fristen, Schwärzung im Logbuch, Gerätename, Kopfzeilen, Phasen und Funker-Modus am Konto |
| Messungen am 01. und 02.10.2026 (Arbeitsablage, nicht im Repository) | Vergleich mit SRS und dem früheren Funkprogramm, Dateizählungen, `funk-admin hilfe`, Optionsprüfung aller Befehle dieses Teils |

Die Befehle für `funk-admin` in diesem Teil sind am gebauten Werkzeug aus dem Stand `d45e33f` geprüft: Jeder kommt
durch die Optionsprüfung (ohne Server Exit 1 wegen der fehlenden Verbindung, nie Exit 2), zwei absichtlich vertippte
Gegenproben enden mit Exit 2 und "Nichts geändert". Für 0.5.0 sind sie mit der Optionstabelle
`src\Funk.Admin\Befehlsoptionen.cs` des Stands `95aa78b` abgeglichen (am Code geprüft, nicht neu am gebauten Werkzeug).
Am laufenden Heimserver sind sie nicht ausgeführt worden.

## F3 Wer Rechteinhaber ist

Die Vorgaben von Sarevoc dazu:

- Anf. A41 (30.09.2026), bekräftigt am 19.10.2026 (Q23): Das Programm gehört Sarevoc; er ist Admin. Alle Rechte liegen
  bei ihm; eine Übergabe an andere Personen ist nicht vorgesehen.
- Anf. A58 (01.10.2026): Es gilt deutsches und EU-Recht, alles muss sauber sein. In jeder Quelldatei steht, dass er
  Maintainer ist. Ob die Mitarbeit von Claude genannt wird, ist noch zu besprechen. Offen oder geschlossen wird noch
  entschieden, in einer eigenen Runde (Q23). Das Repository kommt zunächst privat auf GitHub, Test und Push erst nach
  Version 1; seit dem 19.10.2026 neu auf Forgejo und GitHub mit frischem Verlauf (Q3).
- Anf. A59 (01.10.2026): Maintainer in jeder Quelldatei und im Handbuch ist "Sarevoc". Den bürgerlichen Namen nur, wenn
  es rechtlich nötig ist; er hält sich ans Recht, mehr aber auch nicht. Hintergrund sind die Kennzeichnung von KI und
  Vorbehalte gegen Code, der mit KI entsteht; er will nicht angreifbar sein.

### F3.1 Begriffe

| Begriff | Bedeutung | Bei JG TacRelay |
| --- | --- | --- |
| Urheber | Wer das Werk schafft; immer ein Mensch (§ 7 UrhG). | Soweit am Programm eine menschliche Schöpfung besteht: Sarevoc. Claude kann nie Urheber sein. |
| Nutzungsrecht | Erlaubnis, ein Werk zu nutzen, einfach oder ausschließlich (§ 31 UrhG). Eine Lizenz ist die Einräumung solcher Rechte. | Bekommen die Mitglieder (Lizenz oder Nutzungsbedingungen), ein Betreiber für den Betrieb, wer mitarbeitet für seine Beiträge. |
| Rechteinhaber | Wer Urheberrechte oder ausschließliche Nutzungsrechte hält und deshalb Lizenzen erteilen darf. | Sarevoc |
| Maintainer | Eine Rolle, kein Recht: pflegt das Projekt, entscheidet über Änderungen und Fassungen. | Sarevoc (Anf. A58, A59) |
| Eigentümer (im Programm) | Eine technische Kontoeigenschaft auf dem Server (Protokoll 3.10): genau ein ADMIN-Konto, nur an der Konsole gesetzt. | Das Konto von Sarevoc auf dem Heimserver. Hat mit dem Recht am Programm nichts zu tun. |

### F3.2 Was daraus folgt

- Das Urheberrecht entsteht von selbst mit der Schöpfung. Es gibt kein Register, und ein Vermerk wie "(c)" ist keine
  Voraussetzung.
- Es ist zu Lebzeiten nicht übertragbar (§ 29 Abs. 1 UrhG). Andere können nur Nutzungsrechte bekommen, einfache oder
  ausschließliche (§ 29 Abs. 2, § 31 Abs. 3 UrhG); Sarevoc bliebe immer Urheber dessen, was er geschaffen hat. Eine
  Übergabe ist nicht vorgesehen (Q23).
- Nutzungsrechte schriftlich und genau beschreiben. Nach der Zweckübertragungsregel (§ 31 Abs. 5 UrhG) gilt im Zweifel
  nur als eingeräumt, was der Zweck des Vertrags verlangt.
- Eine Spielergruppe ohne Rechtsform (etwa ohne eingetragenen Verein) kann nicht Urheber sein; ob eine Gruppe
  Nutzungsrechte halten kann, hängt von ihrer Rechtsform ab.

**Erledigt mit 0.5.0** (am Code geprüft am Stand `95aa78b`): `THIRD-PARTY-NOTICES.txt` nannte als Inhaber des eigenen Codes
den Namen der früheren Gilde, und `Directory.Build.props` trug ihn als `<Company>` in jede DLL; das passte nicht zu Anf. A41.
Nach Anf. A59 steht dort jetzt "Sarevoc": in den Notices als Maintainer, in `Directory.Build.props` als `Company`,
`Authors` und `Copyright` ("Maintainer: Sarevoc"), im Setup als `AppPublisher` und `AppCopyright` (F9.1).

### F3.3 Der Name: Sarevoc

**Entschieden** (Anf. A59, 01.10.2026): Maintainer und Rechteinhaber steht unter dem Namen "Sarevoc", seinem Namen in
Star Citizen, im Handbuch und in den Quelldateien (in den Quelldateien seit 0.5.0, F9.1). Den bürgerlichen Namen nimmt
das Projekt nur auf, wenn das Recht ihn an einer Stelle verlangt; in dieses Handbuch gehört er nicht, und ein Wächter
(`MaintainerKopfzeileTests`) prüft, dass er in Code, Tests, Werkzeugen, Setup, Konfiguration und Doku nicht vorkommt.

Warum ein Pseudonym genügt (Einschätzung, kein Rechtsrat):

- Wer auf Vervielfältigungsstücken in der üblichen Weise als Urheber bezeichnet ist, gilt bis zum Beweis des Gegenteils
  als Urheber. Das gilt ausdrücklich auch für einen Decknamen, der als Name des Urhebers bekannt ist (§ 10 Abs. 1 UrhG).
  In Star Citizen und in seiner Gruppe ist "Sarevoc" sein bekannter Name.
- Der Urheber bestimmt selbst, ob und mit welcher Bezeichnung er genannt wird (§ 13 UrhG).
- Für den privaten Betrieb in einer Gruppe verlangt nach Einschätzung kein Gesetz den bürgerlichen Namen im Programm oder
  im Handbuch. Wo er doch nötig werden kann, zeigt die Tabelle.

Was das Pseudonym kostet und wann der bürgerliche Name doch nötig werden kann:

| Lage | Folge |
| --- | --- |
| Streit um Rechte am Code | Die Person hinter dem Namen ist erst nachzuweisen. Dafür helfen die Belege aus F5.5 (Anforderungen, Projektverlauf, Historie). |
| Schutzdauer | Für Werke unter einem unbekannten Decknamen rechnet sie anders, 70 Jahre ab Veröffentlichung, solange die Person nicht bekannt wird (§ 66 UrhG). Für JG TacRelay ohne praktische Bedeutung. |
| Verträge | Eine Vereinbarung mit einem Betreiber oder einem Mitarbeiter (F8.9, Punkt 5) schließt die Person; im Vertrag selbst steht dann der bürgerliche Name, im Programm darf es beim Pseudonym bleiben. |
| Datenschutzhinweis | Er nennt Namen und Kontaktdaten des Verantwortlichen (Art. 13 Abs. 1 lit. a DSGVO, Entwurf in F11.10). Ob dort das Pseudonym reicht, ist offen; das ist die Stelle, an der der bürgerliche Name am ehesten nötig werden kann. Vor dem Ausgeben des Hinweises klären lassen; ins Handbuch kommt er auch dann nicht. |
| Veröffentlichung, Spenden oder Verkauf | Andere Pflichten zur Anbieterkennzeichnung können dann greifen; vor einem solchen Schritt prüfen lassen. |

Der Name einer Gilde wäre keine natürliche Person, also kein Urheber, und als Rechteinhaber nur mit passender Rechtsform
denkbar.

### F3.4 Wenn weitere Menschen mitarbeiten

Wer Code beiträgt, ist an seinem Beitrag Urheber, als Miturheber (§ 8 UrhG) oder als Bearbeiter (§ 3 UrhG).
Miturheber dürfen das gemeinsame Werk nur gemeinsam verwerten (§ 8 Abs. 2 UrhG). Deshalb vor dem ersten Beitrag von
außen eine der folgenden Regeln festlegen:

| Regel | Wie | Folge |
| --- | --- | --- |
| Beitrag unter der Projektlizenz | Jeder Beitrag steht unter derselben Lizenz wie das Projekt. Üblich bei offenen Projekten. | Einfach. Ein späterer Lizenzwechsel braucht die Zustimmung aller, die beigetragen haben. Bei "geschlossen" nicht möglich. |
| Rechteeinräumung an Sarevoc | Schriftliche Vereinbarung (Contributor License Agreement): ausschließliches oder umfassendes einfaches Nutzungsrecht an Sarevoc. | Sarevoc behält die Freiheit, Lizenz oder Weg zu wechseln. Mehr Aufwand, verlangt Vertrauen. |
| Herkunftsbestätigung (Developer Certificate of Origin) | Je Commit eine Zeile, dass der Beitrag eingebracht werden darf. | Bestätigt nur die Herkunft, räumt keine weiteren Rechte ein. Ergänzt eine der beiden Regeln darüber. |

## F4 Urheberrecht an Software (§§ 69a ff. UrhG)

Computerprogramme sind Sprachwerke (§ 2 Abs. 1 Nr. 1 UrhG). Für sie gelten die Sonderregeln der §§ 69a bis 69g UrhG, die
Umsetzung der Richtlinie 2009/24/EG über den Rechtsschutz von Computerprogrammen.

| Norm | Inhalt | Bedeutung für JG TacRelay |
| --- | --- | --- |
| § 69a Abs. 1 | Programme in jeder Gestalt, einschließlich Entwurfsmaterial. | Quellcode und Programmdateien. Die Spezifikation `FUNKPROTOKOLL-v1.md` ist als Text ein Sprachwerk, soweit sie schöpferisch ist (Einschätzung). |
| § 69a Abs. 2 | Geschützt sind alle Ausdrucksformen; Ideen und Grundsätze, auch die der Schnittstellen, sind es nicht. | Funktionen sind frei: Sprachaktivierung, das kompakte 2x5-Feld, Grün für Senden und Orange für Hören. Der EuGH (SAS Institute, C-406/10) sieht auch Funktionalität, Programmiersprache und Dateiformate nicht als geschützt. |
| § 69a Abs. 3 | Geschützt, wenn das Programm die eigene geistige Schöpfung seines Urhebers ist; keine weiteren Kriterien, auch keine ästhetischen. | Die Schwelle ist niedrig, verlangt aber die Schöpfung eines Menschen (F5). |
| § 69b | Programme aus Arbeits- und Dienstverhältnissen. | Nicht einschlägig; niemand arbeitet hier als Angestellter. |
| § 69c | Vervielfältigen, Umarbeiten, Verbreiten und öffentlich Zugänglichmachen brauchen die Zustimmung des Rechteinhabers. | Grundlage jeder Lizenz und jeder Nutzungsbedingung. |
| § 69d | Ohne Zustimmung erlaubt: bestimmungsgemäße Benutzung samt Fehlerberichtigung, Sicherungskopie, Beobachten, Untersuchen und Testen. | Gilt für jeden berechtigten Nutzer, auch bei "geschlossen". |
| § 69e | Dekompilieren, soweit für die Herstellung der Interoperabilität unerlässlich. | Wie § 69d Abs. 2 und 3 vertraglich nicht abdingbar (§ 69g Abs. 2). |
| § 69f | Vernichtung rechtswidrig hergestellter Kopien. | Rechtsfolge bei Verletzung. |
| § 64 | Schutzdauer 70 Jahre nach dem Tod des Urhebers. | - |

Die Oberfläche ist nach dem EuGH (BSA, C-393/09) kein Computerprogramm im Sinne der Richtlinie, kann aber als eigenes
Werk geschützt sein. JG TacRelay zeichnet seine Oberfläche, die Symbole und das Programmsymbol selbst; fremde
Grafiken, Schriften und Klänge sind nicht enthalten (`THIRD-PARTY-NOTICES.txt`, Abschnitt "Schriften, Grafiken, Klänge").

## F5 Code mit KI-Unterstützung

### F5.1 Wie JG TacRelay entsteht

- Sarevoc gibt die Anforderungen vor (Anf. A1 bis A65 in `NEUBAU.md`), entscheidet zwischen Varianten (etwa Anf. A29
  Ansicht "Raster", Anf. A31 Farbstellung P2, Anf. A53 Kranzform "Klassisch", Anf. A64 Command auf Taste), prüft und nimmt ab (Hörtest am 29.09.2026,
  Paket am Arbeits-PC am 30.09.2026, `PROJEKT.md`).
- Code, Tests und Doku schreibt Claude, ein KI-Modell von Anthropic. Im privaten Arbeitsrepository nennt jeder Commit
  Claude als Mitwirkenden; im öffentlichen Repository steht das einmal zentral in `README.md` (F10).

### F5.2 Was das Recht verlangt

- Urheber kann nur ein Mensch sein: § 7 UrhG (Schöpfer), § 2 Abs. 2 UrhG ("persönliche geistige Schöpfungen"), § 69a
  Abs. 3 UrhG ("eigene geistige Schöpfung ihres Urhebers"). Ein KI-Modell ist keine Person; es kann weder Urheber noch
  Miturheber noch Rechteinhaber sein.
- Der EuGH misst Originalität an freien, kreativen Entscheidungen des Urhebers, in denen sich seine Persönlichkeit zeigt
  (Infopaq, C-5/08; Cofemel, C-683/17). Was durch technische Vorgaben bestimmt ist, reicht nicht (Brompton, C-833/18).
- In der deutschen Literatur verbreitet: Reine KI-Erzeugnisse ohne prägenden menschlichen Beitrag sind nicht geschützt.
  KI als Werkzeug schadet nicht, wenn der Mensch die schöpferischen Entscheidungen selbst trifft. Eine Anweisung an das
  Modell (Prompt) reicht nach überwiegender Ansicht meist nicht. Eigene Auswahl, Anordnung und Überarbeitung können
  Schutz begründen, dann aber nur für diesen Anteil.
- Eine Entscheidung des BGH oder des EuGH, die das für Code mit KI-Anteil abschließend klärt, ist dem Verfasser nicht
  bekannt (Stand Juni 2026). Im Ausland zeigen die bekannten Entscheidungen in dieselbe Richtung: Das Stadtgericht Prag
  sprach 2023 einem KI-Bild ohne menschlichen Urheber den Schutz ab. In den USA verlangt das Berufungsgericht des D.C.
  Circuit im Fall Thaler v. Perlmutter (März 2025) einen menschlichen Urheber, und das US Copyright Office hält in
  seinem Bericht "Copyright and Artificial Intelligence, Part 2: Copyrightability" (Januar 2025) Prompts allein für
  nicht ausreichend, menschliche Auswahl, Anordnung und Änderungen dagegen für schutzfähig. Auf deutsches Recht ist das
  nicht übertragbar, zeigt aber die Linie.
- Anthropic überträgt nach Kenntnisstand des Verfassers in seinen Nutzungsbedingungen eigene Rechte an Ausgaben, soweit
  welche bestehen ("if any"), auf den Nutzer. Ein Urheberrecht, das es nicht gibt, entsteht dadurch nicht. Welche
  Bedingungen für das Konto von Sarevoc bei Anthropic gelten, ist hier nicht geprüft.

### F5.3 Einschätzung für JG TacRelay

| Beitrag | Beispiel | Schutz (Einschätzung) |
| --- | --- | --- |
| Anforderung, Idee | Anf. A2 Sprachaktivierung wie bei Discord, Anf. A14 Echounterdrückung | nicht geschützt (Idee, § 69a Abs. 2 UrhG) |
| Auswahl, Abnahme | Anf. A29, A31, A53 | für den Code eher nicht; allenfalls für eine Gestaltung, die Sarevoc selbst bestimmt hat |
| Eigene Texte von Sarevoc | ausformulierte Vorgaben und Nachrichten | als Sprachwerk nur bei genügend Eigenart; kurze Vorgaben kaum |
| Code, Tests und Doku von Claude | 330 C#-Dateien unter `src`, 194 unter `tests` (Stand `95aa78b`) | ohne prägenden menschlichen Anteil wohl nicht geschützt |
| Code, den ein Mensch selbst schreibt oder wesentlich umschreibt | künftig etwa Beiträge von außen | geschützt, soweit schöpferisch |
| Fremder Code | `QrCode.cs`, `WeichesClipping.cs`, die DLLs | die Rechte der Fremden bestehen unabhängig davon voll |

**Ergebnis:** Ob und wie weit am Code von JG TacRelay ein Urheberrecht besteht, ist offen und eher zweifelhaft. Das
Projekt sollte so aufgestellt sein, dass es auch ohne dieses Recht sauber ist.

### F5.4 Folgen

| Bereich | Folge |
| --- | --- |
| Geschlossene Lizenz | "Alle Rechte vorbehalten" verbietet Kopien nur, soweit ein Recht besteht. Tragend sind dann die Nutzungsbedingungen, die aber nur binden, wer zugestimmt hat, und dass der Quellcode nicht herausgegeben wird. Schutz als Geschäftsgeheimnis (§ 2 Nr. 1 GeschGehG) setzt angemessene Geheimhaltung voraus; ausgelieferte Programme darf ein rechtmäßiger Besitzer untersuchen, sofern ihn kein Vertrag bindet, das zu unterlassen (§ 3 Abs. 1 Nr. 2 GeschGehG), und .NET-Programme lassen sich leicht zurücklesen, wie die Analyse des früheren Funkprogramms gezeigt hat. |
| Copyleft (GPL, AGPL, EUPL) | Die Bedingungen der Lizenz hängen am Urheberrecht. Wo keins besteht, kann sie niemand durchsetzen; ein verschlossener Ableger wäre dann nicht zu verhindern. |
| Freizügige Lizenz (MIT, Apache) | Kaum ein Unterschied, die Lizenz erlaubt ohnehin fast alles. |
| Fremdbestandteile | Unabhängig davon geschützt. Ihre Lizenzpflichten gelten immer. |
| Wiedergabe von Trainingsdaten | KI-Modelle können Teile ihrer Trainingsdaten wiedergeben; das LG München I hat im Verfahren GEMA gegen OpenAI (Urteil vom 11.11.2025, Liedtexte) darin Vervielfältigungen gesehen. Gegenmittel im Projekt: `UrheberrechtTests` (sucht in `src` nach Merkmalen einer Übertragung) und die Prüfung in F6. |

### F5.5 Was das für Hinweise im Code bedeutet

- Eine Kopfzeile mit Maintainer und Lizenz ist in jedem Fall richtig. Sie sagt, wer verantwortlich ist und unter welchen
  Bedingungen das Projekt die Datei weitergibt. Ein Urheberrecht erzeugt sie nicht. Die Kopfzeile von 0.5.0 nennt nur den
  Maintainer, ohne Copyright- und ohne Lizenzzeile, und behauptet damit keinen Rechtsstand (F9.1); die Lizenzzeile kommt mit
  der Entscheidung über die Lizenz. Damit auch die zentrale Lizenzdatei keinen Schutz behauptet, den es nicht gibt, gehört
  dort ein Satz hinein wie: "Soweit an JG TacRelay Rechte bestehen, liegen sie bei Sarevoc."
- Claude nie als Urheber oder Rechteinhaber eintragen, höchstens als Mitarbeit (F10).
- Die menschlichen Anteile belegbar halten: `docs\entwurf\ENTSCHEIDUNGEN.md` mit den Anforderungen und Entscheidungen
  von Sarevoc, `PROJEKT.md` und die Git-Historie nicht umschreiben. Im Streit hilft diese Dokumentation; eine falsche Angabe zur
  Urheberschaft schadet.
- Fremdes weiter so kennzeichnen wie bisher: Herkunft und Lizenz in der Datei und in `THIRD-PARTY-NOTICES.txt`, geprüft
  von `UrheberrechtTests`.

## F6 Neubau ohne SRS-Code: was geprüft ist

Das frühere Funkprogramm der Gruppe ist ein Fork von DCS-SimpleRadio-Standalone (SRS) von ciribob, das unter GPL-3.0 steht
(Analyse des früheren Funkprogramms, Arbeitsablage). JG TacRelay ist als Neubau angelegt: eigener Client und eigener
Server, kein Code aus SRS, dem früheren Funkprogramm oder Mumble (`PROJEKT.md`, Kopf; ebenso
`docs\entwurf\ENTSCHEIDUNGEN.md` und `THIRD-PARTY-NOTICES.txt`).

Das ist mehr als eine Formsache. Wäre SRS-Code übernommen, wäre JG TacRelay eine Bearbeitung eines GPL-3.0-Werks und
dürfte nur unter GPL-3.0 weitergegeben werden. Die Wege "geschlossen", MIT, Apache-2.0 und EUPL-1.2 wären dann
versperrt.

### F6.1 Geprüft

| Nr | Prüfung | Gegenstand | Ergebnis |
| --- | --- | --- | --- |
| 1 | Historie | Alle 178 Commits aller Zweige am Stand `95aa78b` (02.10.2026, wiederholt; am 01.10.2026 um 21:13 waren es 168; erster Commit `21627b8` vom 29.09.2026 07:13), gesucht mit `git log --all -S` nach zwölf Kennwörtern aus SRS und dem früheren Funkprogramm: Ciribob, SimpleRadio, Caliburn, Concentus, MahApps, NLog, SharpDX, Octokit, HandshakeToken, CoalitionBase, LotATC, VAICOM | Treffer nur in zwei Commits dieses Handbuchs (`d9e2900`, `6fd9138`), weil das Handbuch die Wörter selbst nennt (1.2, F6). Ohne `docs/handbuch` 0 Commits: Außerhalb des Handbuchs stand keines der Wörter je in einer Fassung einer Datei. |
| 2 | Textsuche im Stand `d45e33f` | `GPL`: 0 Dateien. `DCS`: 0. `SRS`: `PROJEKT.md` und `THIRD-PARTY-NOTICES.txt` (als Abgrenzung), dazu zufällige Bytes in zwei PNG-Bildern. Der Name des früheren Funkprogramms in `src`: 18 Zeilen, Kommentare zu den Vorgaben von Sarevoc ("kompakt wie" und "Grün und Orange wie" dort) sowie Name, Knopf und Aufruf der damaligen Startvorlage der Flotte; seit dem Umbau zu JG TacRelay steht er nirgends mehr im Repository. `Mumble` in `src`: eine Zeile in `SprachGate.cs`, die Idee, den Pegel um die Verstärkung der automatischen Pegelregelung zu bereinigen. | Nur Verweise, keine Übernahme. |
| 3 | Bezeichner und Zeichenketten gegen das frühere Funkprogramm 2.3.3.8 | Metadaten seiner vier Assemblies (Client, Common, AutoUpdater, Installer; 537 Typen, 966 Methodennamen, 1089 Zeichenketten ab 12 Zeichen) gegen die 482 C#- und XAML-Dateien von JG TacRelay (1236 Typnamen, 5917 Methoden- und Aufrufnamen, 14271 Zeichenketten ab 12 Zeichen) | Gleiche Typnamen: nur `App`, der Pflichtname jeder WPF-Anwendung. Gleiche Methodennamen: nur allgemeine Namen aus .NET, WPF, NAudio, DirectInput und Windows (etwa `Dispose`, `ToString`, `InitializeComponent`, `StartRecording`, `GetWindowRect`, `Unacquire`). Gleiche Zeichenketten: keine. |
| 4 | Dasselbe gegen den SRS-Server 2.3.3.3 | `SRS-Server.dll` und `SRS-Common.dll` aus dem Original-Release (Arbeitsablage, nicht im Repository; 354 Typen, 636 Methodennamen, 278 Zeichenketten) | Gleiche Typnamen: nur `App`. Methodennamen wie bei Nr. 3. Gleiche Zeichenketten: keine. |
| 5 | Native Bibliotheken | `opus.dll` und `speexdsp.dll` aus SRS 2.3.3.3 gegen die eigenen Bauten (`native\BAUBERICHT.md`) | Verschieden. SRS: `opus.dll` 454.656 Byte, SHA-256 `e5c10ef6...0b6f`; `speexdsp.dll` `b19049b2...99f7`. JG TacRelay: `opus.dll` 4.451.840 Byte, `8c3ef1e8...15e3`, gebaut aus dem Xiph-Archiv; vom speexdsp nur der Resampler, `speexdsp_resampler.dll` `c7bf98cb...4fb7`. |
| 6 | `UrheberrechtTests` | Sucht in `src` nach Merkmalen einer Übertragung ("in C# nachgebaut", "übertragen nach C#", "Copyright (c)" und weitere). | Findet nur `QrCode.cs` und `WeichesClipping.cs`; beide stehen in `THIRD-PARTY-NOTICES.txt` (Nr. 11 und 12). Dieselbe Suche von Hand am Stand `95aa78b` findet dieselben zwei Dateien. |

Für die Fassung 0.5.0 sind Nr. 1 (am 02.10.2026 wiederholt) und Nr. 6 (läuft mit jeder Suite, zuletzt 1312 von 1312
bestanden am 02.10.2026) erneuert; Nr. 2 bis 5 stammen vom 01.10.2026 und sind für den neuen Code von 0.5.0 (Phasen,
Funker-Modus, Ablage, Klick) nicht wiederholt.

Nachprüfen lässt sich Nr. 1 so, im Wurzelordner des Repositorys in PowerShell; der Zusatz am Ende lässt das Handbuch
aus, und keine Ausgabe heißt, das Wort stand außerhalb davon nie in einer Datei:

```
git log --all --format=%h -S Ciribob -- . ':!docs/handbuch'
git log --all --format=%h -S SimpleRadio -- . ':!docs/handbuch'
```

Nr. 3 und 4 hat ein Vergleichsskript vom 01.10.2026 gemacht. Es liegt mit den gelesenen Metadaten in der Arbeitsablage,
nicht im Repository; ob es nach `tools\` übernommen wird, steht in den Offenen Punkten am Ende des Handbuchs. Die
Metadaten von SRS stammen aus dem entpackten Bundle `SRS-Server.exe`, gelesen mit den Analysewerkzeugen aus der
Untersuchung des früheren Funkprogramms (`bundle-entpacken.ps1`, `metadaten-lesen.ps1`).

### F6.2 Nicht geprüft

- Den SRS-Quelltext selbst. Er liegt nicht in der Zone; verglichen wurden die Metadaten der Programmdateien (Namen und
  Zeichenketten) aus SRS 2.3.3.3 und aus dem früheren Funkprogramm 2.3.3.8, das auf dem SRS-Stand 2.3.3.3 aufbaut. Andere SRS-Fassungen
  (aktuell 2.4.1.0) nicht.
- Übernommene Abläufe mit anderen Namen. Ein Namensvergleich findet keinen Ablauf, der mit deutschen Bezeichnern neu
  geschrieben wurde; dafür bräuchte es einen Strukturvergleich, der nicht gemacht ist.
- Den Quelltext des früheren Funkprogramms. Er liegt nicht vor, ebenso wenig sein Server.

### F6.3 Was aus dem früheren Funkprogramm kommt, und warum das kein Code ist

- Messwerte und Beobachtungen aus der Analyse (16 kHz, 40-ms-Pakete, fester 50-ms-Puffer; Analyse, Abschnitt
  "Audioweg") als Begründung für eigene, andere Entscheidungen.
- Bedienideen nach den Vorgaben von Sarevoc: kompaktes Feld 2x5 (Anf. A29), Orange für Hören und Grün für Senden (Anf. A7),
  Rollen-Voreinstellungen (Anf. A22).
- Bis 0.12.0 Kanalnamen und Aufteilung der früheren Gruppe als Startvorlagen, abgelesen aus dem früheren Funkprogramm
  2.3.3.8 bei Sarevoc. Seit dem Umbau zu JG TacRelay (19.10.2026) sind diese Vorlagen entfernt; die Vorlage Star Citizen
  (`src\Funk.Protokoll\Steuerung\Flottenvorlagen.cs`) hat Sarevoc selbst festgelegt (Q26). Kurze Namen wie "Command" oder
  "Schiff 1" sind nach Einschätzung ohnehin nicht schutzfähig.

Ideen, Funktionen und Daten sind keine geschützte Ausdrucksform (§ 69a Abs. 2 UrhG, EuGH SAS Institute). Die
Untersuchung des früheren Funkprogramms erlaubt die GPL-3.0 ohnehin, dazu § 69d Abs. 3 UrhG.

**Ergebnis:** Soweit geprüft, enthält JG TacRelay keinen Code aus SRS oder dem früheren Funkprogramm. Die GPL-3.0 von SRS
bindet JG TacRelay nicht; jeder der Lizenzwege bleibt offen. Den Befund S8 am früheren Funkprogramm (Quellcode nicht
veröffentlicht, Lizenzhinweis entfernt) müssen dessen Betreiber lösen; JG TacRelay berührt er nicht.

## F7 Fremdbestandteile

Alle Fremdbestandteile stehen mit Lizenz, Herkunft und Ort des Lizenztexts in `THIRD-PARTY-NOTICES.txt`. Die Lizenztexte
liegen im Paket unter `lizenzen\`; `tools\paket-bauen.ps1` und `tools\setup-bauen.ps1` prüfen beim Bauen, dass jede
ausgelieferte DLL dort steht (Kopf der Notices). Die Notices samt Nr. 12 liegen in Setup, Gesamtpaket und Serverpaket
0.4.0 (`PROJEKT.md`, Stand 01.10.2026 15:50); die Notices der Setups und Pakete 0.5.0 (gebaut am 02.10.2026) nennen sie ebenfalls (am Paketinhalt nachgesehen). Der Absatz "Eigener Code"
nennt seit 02.10.2026 Sarevoc als Maintainer und vermerkt, dass die Lizenz noch nicht entschieden ist.

| Nr | Bestandteil | Datei | Lizenz | Pflicht beim Weitergeben | Offen |
| --- | --- | --- | --- | --- | --- |
| 1 | libopus 1.6.1 | `opus.dll` | BSD-3-Clause, dazu lizenzfreie Patentlizenzen (Xiph.Org, Microsoft, Broadcom) | Copyright, Bedingungen und Haftungsausschluss mitgeben; Namen der Urheber nicht zur Werbung | Modellgewichte |
| 2 | RNNoise (Zweig main) | `rnnoise.dll` | BSD-3-Clause | wie Nr. 1 | Modellgewichte |
| 3 | SpeexDSP 1.2.1, nur Resampler | `speexdsp_resampler.dll` | BSD-3-Clause | wie Nr. 1 | - |
| 4 | webrtc-audio-processing 2.1 mit eigener C-Schicht | `webrtc_apm.dll` | BSD-3-Clause und Patentzusage (PATENTS); darin abseil-cpp (Apache-2.0), pffft (FFTPACK, BSD-artig), Ooura FFT, fft, spl_sqrt_floor, rnnoise-Teile (BSD-3-Clause) | alle Texte aus `lizenzen\native\webrtc-apm\` mitgeben; für abseil den Apache-2.0-Text | Gewichte der Sprachkennung, siehe unten |
| 5 | NAudio.Core und NAudio.Wasapi 3.1.0 | `NAudio.Core.dll`, `NAudio.Wasapi.dll` | MIT | Copyright und Erlaubnistext mitgeben | - |
| 6 | System.Numerics.Tensors 9.0.0 | `System.Numerics.Tensors.dll` | MIT | wie Nr. 5 | - |
| 7 | CommunityToolkit.Mvvm 8.4.2 | `CommunityToolkit.Mvvm.dll` | MIT | wie Nr. 5 | - |
| 8 | gong-wpf-dragdrop 4.0.0 | `GongSolutions.WPF.DragDrop.dll` | BSD-3-Clause | wie Nr. 1 | - |
| 9 | .NET-Programmstarter (apphost) | `TacRelay.exe`, `funk-testserver.exe`, `funk-testclient.exe` | MIT | wie Nr. 5 | - |
| 10 | Inno Setup 6.7.3 | das Setup und `unins000.exe` | Inno Setup License (freie Nutzung und Weitergabe, Hinweis auf den Ursprung) | Lizenztext im Setup (`lizenzen\innosetup\`) | - |
| 11 | QR Code generator library (Project Nayuki), nach C# übertragen | in `Funk.Verwaltung.dll` (`src\Funk.Verwaltung\QrCode.cs`) | MIT | Copyright und Erlaubnistext; stehen in der Datei und in den Notices | - |
| 12 | `opus_pcm_soft_clip` aus libopus 1.6.1, nach C# übertragen | in `Funk.Klang.dll` (`src\Funk.Klang\Verarbeitung\WeichesClipping.cs`) | BSD, Text aus dem Kopf von `src/opus.c` | Copyright, Bedingungen und Haftungsausschluss; stehen in den Notices | - |

Weiter gilt:

- Die .NET-Laufzeit liegt nicht im Paket; alle Programme sind framework-abhängig und nutzen die Laufzeit, die auf dem
  Rechner installiert ist (Notices Nr. 9).
- Programme, die nur neben JG TacRelay laufen (systemd, nginx, fail2ban, Proxmox, Debian), sind keine Bestandteile.
  Ihre Lizenzen berühren JG TacRelay nicht. Die Dateien unter `konfig\fail2ban\` sind eigene Konfiguration.
- Alle Lizenzen der Bestandteile sind freizügig. Jeder der Lizenzwege unten ist mit ihnen vereinbar, auch "geschlossen".
  Der Apache-2.0-Anteil (abseil) verträgt sich mit GPL-3.0, AGPL-3.0 und EUPL-1.2, nicht mit GPL-2.0, die hier keine
  Rolle spielt.
- Die Patentzusagen von webrtc und die Patentlizenzen zu Opus können enden, wenn der Lizenznehmer seinerseits wegen
  Patenten klagt (Einschätzung nach dem üblichen Aufbau solcher Zusagen; Texte in `lizenzen\`). Für eine Gruppe ohne
  praktische Bedeutung.
- Offen aus `PROJEKT.md` (Nächste Schritte 2): eine eigene THIRD-PARTY-NOTICES für das Serverpaket.
- Die Meldungsbilder (seit 0.12.0) sind keine Fremdbestandteile, sondern eigene Grafik aus dem Maintainer-Umfeld (F7.2).

### F7.1 Die offene Frage der Modellgewichte

**Worum es geht.** Drei der nativen Bibliotheken enthalten Gewichte neuronaler Netze, also große Zahlentabellen aus
einem Training:

| Wo | Was | Lizenzlage | Wofür JG TacRelay es nutzt |
| --- | --- | --- | --- |
| `opus.dll` | DNN-Modelldaten `dnn\*_data.c` für Deep PLC, FARGAN, PitchDNN, LACE, NoLACE, BBWENet, zusammen 19,8 MB C-Quelltext, fest einkompiliert (`BAUBERICHT.md`) | Kein eigener Lizenzkopf; sie liegen unverändert im Release-Tarball, für den `COPYING` (BSD-3-Clause) gilt. Eine ausdrückliche Aussage zu den Gewichten fehlt (Notices Nr. 1). | Decoder-Komplexität 7 (Deep PLC ab 5, NoLACE ab 7) und Bandbreitenerweiterung (`OpusDekodierer.cs`): verdeckt Paketverluste und verbessert den Klang. |
| `rnnoise.dll` | Standardmodell `rnnoise_data 0a8755f8...8b37`, eigenes Archiv | Das Archiv hat keine Lizenzdatei. Verteilt vom RNNoise-Projekt zusammen mit dem BSD-Code, laut README trainiert mit öffentlichen Datensätzen (Notices Nr. 2, `HERKUNFT.txt`, `datasets.txt`). Laut der Recherche vom 29.09.2026 (`recherche\audio-dsp.md`, Arbeitsablage, nicht im Repository) stehen darunter OpenSLR 32 und 83 unter CC BY-SA 4.0. | Rauschunterdrückung und Sprachwahrscheinlichkeit für die Sprachaktivierung (Anf. A2, A11, `SprachGate`). |
| `webrtc_apm.dll` | `webrtc/third_party/rnnoise/src/rnn_vad_weights.cc`: int8-Gewichte der Sprachkennung, am 01.10.2026 im Quellarchiv `webrtc-audio-processing-2.1.tar.xz` nachgesehen | Ohne eigenen Lizenzkopf, im Ordner mit `COPYING` (BSD-3-Clause); `meson.build` daneben übersetzt die Datei. Ob sie nach dem Linken in der DLL steht, ist nicht geprüft. Die Notices führen sie unter "rnnoise-Teile in webrtc: BSD-3-Clause", ohne Vermerk. | wie in webrtc vorgesehen (Pegelregelung), sofern gelinkt |

**Die Rechtsfragen.**

- Sind Gewichte überhaupt geschützt? Sie entstehen durch ein Training, nicht durch schöpferische Entscheidungen eines
  Menschen. Ein Werk im Sinne des Urheberrechts sind sie nach Einschätzung eher nicht; diskutiert werden das
  Datenbankherstellerrecht (§ 87a UrhG) und der Geheimnisschutz, der bei veröffentlichten Gewichten ausscheidet. Geklärt
  ist das nicht.
- Falls sie geschützt sind: Die Projekte verteilen sie zusammen mit dem BSD-Code, im selben Archiv und für genau diesen
  Gebrauch. Das spricht stark für eine stillschweigende Erlaubnis (Einschätzung).
- Schlägt die Share-Alike-Bedingung der Trainingsdaten (CC BY-SA 4.0) auf die Gewichte durch? Offen. Eine verbreitete
  Ansicht sieht in einem Modell keine Bearbeitung einzelner Aufnahmen, entschieden ist das nicht.
- Das Risiko für eine Gruppe schätzt der Verfasser als gering ein: Anspruch hätten allenfalls Xiph.Org oder die Urheber
  der Datensätze, und Xiph verteilt die Gewichte selbst frei. Für eine öffentliche Weitergabe wiegt die Frage schwerer
  als für den Gebrauch in einer Gruppe.

**Die Wege (Sarevoc entscheidet, `PROJEKT.md`, Nächste Schritte 2):**

| Weg | Was | Folge |
| --- | --- | --- |
| A | So lassen, wie es ist, in den Notices als UNGEKLÄRT vermerkt (Stand heute). Den Fall `webrtc_apm.dll` dort ergänzen. | Kein Aufwand, Restrisiko offen benannt. |
| B | Bei Xiph.Org um eine Klarstellung bitten, etwa über das Projekt auf gitlab.xiph.org. | Bringt Sicherheit, wenn eine Antwort kommt. Kontakt nimmt nur Sarevoc auf. |
| C | `opus.dll` ohne OSCE und ohne Deep PLC bauen. | Annahme, nicht gebaut: Dann stecken keine DNN-Gewichte in `opus.dll`. Verdecken bei Paketverlust und Bandbreitenerweiterung fallen weg, der Klang bei schlechter Leitung wird schlechter. |
| D | RNNoise weglassen oder ersetzen. | Ohne Ersatz fallen Rauschunterdrückung und Sprachwahrscheinlichkeit weg; die Sprachaktivierung nach Anf. A2 und A11 verliert ihre Grundlage. |

### F7.2 Eigene Grafik: die Meldungsbilder (seit 0.12.0)

Die Bilder des Meldesystems (Befehle, Zustände, Ampel, Countdown, Bereit-Abfrage; 40 Dateien in den Ausführungen
Schlicht, Metall und Nur Schrift, eingebettet in `TacRelay.dll`) hat Sarevoc aus dem Maintainer-Umfeld selbst mit ChatGPT
(GPT Pro) erstellt: den Grafiksatz v3 (17 Motive in Metall und Schlicht), die neuen Symbole v1 (Los, Stopfen, Pause in
Schlicht und Nur Schrift) und die Korrektur v4 vom 16.10.2026 (M25: Xeno-Hände mit drei Fingern statt vier im
Xeno-Stopp und in den drei Ampelbildern, je Metall und Schlicht; sie ersetzt die gleichnamigen Bilder aus v3). Die Originale
liegen nicht im Repository; ins Programm kommen sie mit ImageMagick auf höchstens
600 Bildpunkte verkleinert. So steht es auch in `THIRD-PARTY-NOTICES.txt`
(Absatz "Eigene Grafik: die Meldungsbilder").

- Nach den Nutzungsbedingungen von OpenAI gehören die Rechte an der Ausgabe dem Nutzer, der sie erzeugt hat (wie in den
  Notices vermerkt). Wie weit an einer mit KI erzeugten Grafik überhaupt ein Urheberrecht entsteht, ist dieselbe Frage wie
  beim Code mit KI-Unterstützung (F5); für den Gebrauch in einer Gruppe ändert sie nichts.
- Das Xeno-Motiv (das Stoppschild mit einer dreifingrigen Alien-Hand) ist wie die anderen Bilder selbst erstellt und
  trägt seit 0.13.1 einen eigenen Namen. Der Funker wählt selbst, ob beim Stopp der Handschuh (ab Werk) oder das
  Xeno-Schild erscheint (A6.9). Fremde Bilddateien, Schriften oder Spielgrafik sind nicht darin.
- Die Zeiten im Countdown, die Zeile der Leitung, die Textmeldung und die Kacheln zeichnet das Programm selbst, in der Systemschrift Segoe UI;
  eingebettet ist keine Schrift. Der Signalton bei Befehlen und im Countdown ist im Programm gerechnet, keine Tondatei.

## F8 Lizenz: die Wege und die Wahl

**Gewählt** (Sarevoc, 25.11.2026): Apache-2.0 (F8.6), mit einer Datei `NOTICE`, die Sarevoc und Joystick Guardian nennt.
Ausschlaggebend: JG TacRelay soll quelloffen und kostenlos bereitstehen, andere Gruppen sollen Teile auch in eigene
Programme übernehmen dürfen, und wer Code weitergibt, soll die Nennung mitgeben. Mit der GPL-3.0 müsste jedes Programm,
das Code übernimmt, selbst unter die GPL; die MIT-Lizenz kennt keine `NOTICE`, die mitgehen muss. Die Abschnitte unten
sind die Vorbereitung dieser Wahl.

Entschieden ist nichts; die Lizenz kommt in einer eigenen Runde (Sarevoc, 19.10.2026, Q23). Der Entwurf vom 29.09.2026 empfahl
auf die Entwurfsfrage E5 ("Wird das Programm Open Source, z. B. MIT?") ein Ja, mit dem Hinweis auf kostenlose
Code-Signierung über SignPath oder günstige über Certum (49 EUR), auf Mitarbeit von außen und eine Prüfung des Codes durch
die Gruppe (Entwurfsfrage E5, `docs\entwurf\ENTSCHEIDUNGEN.md`). Sarevoc hat das offen gelassen (Anf. A58). Die Preise
und Bedingungen dieser Anbieter sind hier nicht nachgeprüft.

### F8.1 Überblick

| Weg | Art | Weitergabe auf die eigene Gruppe begrenzbar | Quellcode mitgeben | Gilt für Serverbetrieb über das Netz | Patentklausel | Amtliche deutsche Fassung |
| --- | --- | --- | --- | --- | --- | --- |
| Geschlossen | eigene Nutzungsbedingungen, "alle Rechte vorbehalten" | ja, vertraglich | nein | - | - | eigener Text |
| GPL-3.0 | starkes Copyleft | nein | ja, bei jeder Weitergabe | nein | ja | nein, nur der englische Text gilt |
| AGPL-3.0 | starkes Copyleft mit Netzklausel | nein | ja, auch beim Betrieb einer geänderten Fassung über das Netz | ja (§ 13 AGPL) | ja | nein |
| MIT | freizügig | nein | nein | nein | nein | nein |
| Apache-2.0 | freizügig mit Patentklausel | nein | nein | nein | ja | nein |
| EUPL-1.2 | Copyleft mit Verträglichkeitsliste | nein | ja, auch bei Bereitstellung über das Netz | ja | ja | ja, alle Amtssprachen der EU gleichwertig |

Jede offene Lizenz erlaubt jedem Empfänger die Weitergabe. Eine Beschränkung auf eine Gruppe gibt es dann nicht, auch
nicht durch einen Zusatz: GPL und AGPL verbieten weitere Einschränkungen ausdrücklich. Wer das Programm nur bestimmten
Gruppen geben will, muss "geschlossen" wählen.

### F8.2 Geschlossen: alle Rechte vorbehalten

| Wer | Folge |
| --- | --- |
| Mitglieder | Bekommen ein einfaches Nutzungsrecht nach Nutzungsbedingungen: installieren und in der Gruppe nutzen, nicht weitergeben. § 69d Abs. 2 und 3 und § 69e UrhG bleiben ihnen auch gegen einen Vertrag (§ 69g Abs. 2 UrhG): Sicherungskopie, Beobachten, Untersuchen und Testen, Dekompilieren für die Interoperabilität. |
| Betreiber | Wer einen Server betreibt, braucht dafür und für das Serverpaket ein ausdrückliches, schriftliches Nutzungsrecht. |
| Mitarbeit von außen | Nur mit Vereinbarung, die die Rechte an Beiträgen bei Sarevoc bündelt; sonst entsteht Miturheberschaft, und Sarevoc könnte nicht mehr allein entscheiden. |
| Dritte | Dürfen nichts. Weil das Urheberrecht am Code zweifelhaft ist (oben), schützt vor allem der Vertrag; Dritte, die eine Kopie ohne Vertrag bekommen, bindet er nicht. |
| Code-Signierung | Die günstigen Wege aus der Entwurfsfrage E5 setzen nach dem Entwurf Open Source voraus; für "geschlossen" kostet ein Zertifikat mehr (Kosten nicht erhoben). |
| Haftung | Bei unentgeltlicher Überlassung wendet eine verbreitete Auffassung Schenkungsrecht an: Haftung nur für Vorsatz und grobe Fahrlässigkeit (§ 521 BGB), für Mängel nur bei Arglist (§§ 523, 524 BGB). |

Eckpunkte für Nutzungsbedingungen (kein fertiger Text; den sollte ein Fachanwalt für IT-Recht prüfen, denn gegenüber
Mitgliedern als Verbrauchern gelten die Regeln für Allgemeine Geschäftsbedingungen, §§ 305 ff. BGB):

| Punkt | Inhalt |
| --- | --- |
| Gegenstand | JG TacRelay (Client, Verwaltungsteil, Probefunk) in der jeweils ausgelieferten Fassung |
| Berechtigte | Mitglieder der Gruppe mit freigegebenem Konto |
| Erlaubt | installieren, nutzen, Sicherungskopie; Untersuchen und Dekompilieren nur so weit, wie § 69d Abs. 2 und 3 und § 69e UrhG es ohnehin erlauben |
| Nicht erlaubt | weitergeben, verkaufen, einen eigenen Server für Dritte betreiben, Hinweise auf Rechte und Lizenzen entfernen |
| Ende | Austritt aus der Gruppe, Sperre des Kontos; dann deinstallieren |
| Haftung | nach Gesetz, unentgeltliche Überlassung |
| Datenschutz | Verweis auf den Datenschutzhinweis |
| Mitschnitte | keine Aufnahmen fremder Stimmen ohne Einverständnis (F11.9) |
| Zustimmung | im Setup anzeigen und annehmen lassen; Inno Setup kann dafür eine Lizenzseite zeigen (Direktive `LicenseFile`), die `installer\TacRelay.iss` heute nicht hat |

### F8.3 GPL-3.0

| Wer | Folge |
| --- | --- |
| Mitglieder | Dürfen Quellcode verlangen, ändern und weitergeben, auch außerhalb der Gruppe. Wer Programmdateien weitergibt, muss den Quellcode mitgeben oder schriftlich anbieten. |
| Betreiber | Darf den Server betreiben und ändern. Änderungen, die er nur selbst betreibt und nicht weitergibt, muss er nicht veröffentlichen. |
| Mitarbeit von außen | Beiträge stehen unter GPL-3.0; wer beiträgt, darf einen eigenen Ableger machen, wieder unter GPL-3.0. |
| Dritte | Jede andere Gruppe darf es nutzen, ändern und weitergeben, unter GPL-3.0. Einen verschlossenen Ableger verbietet die Lizenz, soweit ein Urheberrecht besteht. |
| Sarevoc | Ein späterer Wechsel der Lizenz braucht die Zustimmung aller, die beigetragen haben, oder eine Rechteeinräumung nach F3.4. |
| SRS | Code aus SRS (GPL-3.0) ließe sich künftig übernehmen, mit Hinweisen. Dann bliebe JG TacRelay für immer GPL. |
| Pflichten beim Ausliefern | Lizenztext, Hinweise, vollständiger Quellcode samt der Skripte zum Bauen; dazu gehören nach dem Wortlaut der GPL auch die Quellen der mitgelieferten Bibliotheken, auf die das Programm eigens ausgelegt ist, hier etwa `opus.dll` oder NAudio (Einschätzung). Die Quellarchive der nativen Bibliotheken liegen schon bereit, aber außerhalb des Repositorys in der Arbeitsumgebung. |

Kennung `GPL-3.0-only` oder `GPL-3.0-or-later`. "or-later" erlaubt Empfängern, eine spätere Fassung der GPL zu wählen.

### F8.4 AGPL-3.0

Wie GPL-3.0, dazu § 13 AGPL: Wer eine geänderte Fassung über ein Netz anbietet, muss allen, die damit über das Netz
arbeiten, den Quellcode dieser Fassung anbieten, deutlich sichtbar.

| Wer | Folge |
| --- | --- |
| Mitglieder | wie GPL-3.0 |
| Betreiber | Ändert er den Server und betreibt ihn für die Mitglieder, muss er ihnen den geänderten Quellcode anbieten. Den unveränderten Server betreibt er ohne Zusatzpflicht. |
| Mitarbeit von außen | wie GPL-3.0 |
| Dritte | Eine andere Gruppe, die einen geänderten Server betreibt, muss ihren Mitgliedern den Quellcode anbieten. Das schließt die Lücke der GPL beim reinen Serverbetrieb. |
| Technik | Für eine geänderte Fassung bräuchte der Server einen Weg, den Quellcode anzubieten (etwa ein Hinweis beim Verbinden). Heute gibt es den nicht; nötig wird er erst mit einer Änderung. |
| Verträglichkeit | GPL-3.0 und AGPL-3.0 erlauben die Verbindung beider in einem Werk (jeweils § 13). |

### F8.5 MIT

| Wer | Folge |
| --- | --- |
| Mitglieder, Betreiber, Mitarbeit von außen | Dürfen alles: nutzen, ändern, weitergeben, verkaufen. Einzige Pflicht: Copyright- und Erlaubnistext beilegen. |
| Dritte | Dürfen auch verschlossene oder kostenpflichtige Ableger bauen. |
| Sarevoc | Kann für eigenen Code jederzeit zusätzlich anders lizenzieren. Beiträge anderer stehen unter MIT und bleiben dort. |
| Haftung | Der englische Haftungsausschluss ist nach deutschem Recht nur so weit wirksam, wie das Gesetz es zulässt (§ 309 Nr. 7 BGB); bei unentgeltlicher Überlassung siehe "Geschlossen". |

Kurz und überall bekannt; keine Patentklausel.

### F8.6 Apache-2.0

Wie MIT, dazu: ausdrückliche Patentlizenz aller Beitragenden, die endet, wenn der Empfänger wegen Patenten klagt;
geänderte Dateien sind zu kennzeichnen; eine `NOTICE`-Datei, falls vorhanden, ist mitzugeben. Mit GPL-3.0 vereinbar (in
dieser Richtung), nicht mit GPL-2.0. Für ein Hobbyprojekt ohne eigene Patente bringt die Patentklausel wenig, schadet aber
nicht.

### F8.7 EUPL-1.2

Die Lizenz der Europäischen Kommission.

| Merkmal | Bedeutung |
| --- | --- |
| Sprache | In allen Amtssprachen der EU, alle Fassungen gleich verbindlich; es gibt also einen amtlichen deutschen Text. |
| Copyleft | Abgeleitete Werke nur unter EUPL oder einer Lizenz aus ihrer Verträglichkeitsliste (darunter GPL-2.0, GPL-3.0, AGPL-3.0, MPL-2.0, LGPL). |
| Netz | "Verbreitung oder Mitteilung" umfasst auch den Zugang zu den wesentlichen Funktionen über ein Netz. Ein geänderter Server bei einem anderen Betreiber löst also wie bei der AGPL die Pflicht aus, den Quellcode bereitzustellen. |
| Recht und Gericht | Es gilt das Recht des Mitgliedstaats, in dem der Lizenzgeber wohnt oder seinen Sitz hat, hier also deutsches Recht; Gerichtsstand am Sitz des Lizenzgebers. |
| Haftung | an EU-Recht angepasst, mit Ausnahmen für Vorsatz und Schäden an natürlichen Personen |
| Nachteil | Die Verträglichkeitsliste erlaubt, verbundene Werke unter GPL oder AGPL weiterzugeben; das Copyleft kann so in eine andere Lizenz wandern. Code aus SRS (GPL-3.0) lässt sich in ein EUPL-Werk nicht übernehmen. In Spielergemeinschaften wenig bekannt. |

### F8.8 Fragen, die die Wahl entscheiden

| Frage an Sarevoc | Wenn ja, passt eher |
| --- | --- |
| Soll das Programm nur in bestimmten Gruppen bleiben? | geschlossen |
| Soll jeder es nutzen dürfen, aber niemand einen verschlossenen Ableger bauen, auch nicht über einen geänderten Server? | AGPL-3.0 oder EUPL-1.2 |
| Sind ein amtlicher deutscher Text und deutsches Recht wichtig? | EUPL-1.2 |
| Soll es so einfach wie möglich sein, jeder darf alles? | MIT; mit Patentklausel Apache-2.0 |
| Soll Code aus SRS künftig übernommen werden können? | GPL-3.0 oder AGPL-3.0 |
| Soll die Code-Signierung günstig sein (Entwurfsfrage E5)? | ein offener Weg |
| Sollen andere einfach mitarbeiten können? | ein offener Weg mit der Regel "Beitrag unter der Projektlizenz" |

### F8.9 Was in jedem Fall dazugehört

1. Der Name ist festgelegt (Sarevoc, Anf. A59) und seit 0.5.0 eingetragen: in den Notices, in `Directory.Build.props`
   (`Company`, `Authors`, `Copyright` "Maintainer: Sarevoc") und im Setup (`AppPublisher`, `AppCopyright`).
2. Eine Lizenzdatei im Wurzelordner (bei "geschlossen" die Nutzungsbedingungen) und die Lizenzzeile in den Kopfzeilen
   nach F9; die Maintainer-Zeile steht seit 0.5.0 schon.
3. Im Setup Lizenz oder Nutzungsbedingungen zeigen; `THIRD-PARTY-NOTICES.txt` und `lizenzen\` bleiben, wie sie sind.
4. Die Frage der Modellgewichte entscheiden (Weg A bis D).
5. Mit einem anderen Betreiber (Betrieb) und mit jedem, der beiträgt (Beiträge), schriftlich festhalten, was gilt; die Rechte
   bleiben dabei bei Sarevoc (Q23).
6. Den Datenschutzhinweis fertigstellen und die fehlenden Fristen festlegen.
7. Vor jeder Veröffentlichung den Programmnamen prüfen (F12).

## F9 Kopfzeilen in den Quelldateien

### F9.1 Stand

Anforderung A58 verlangt, dass in jeder Quelldatei steht, dass Sarevoc Maintainer ist (Name nach Anf. A59). Mit der
Fassung 0.5.0 ist das umgesetzt (Standzeile in `PROJEKT.md` vom 02.10.2026 01:00; am Code geprüft am Stand `95aa78b`):

- **Die Zeile.** Jede eigene Quelldatei trägt `JG TacRelay - Maintainer: Sarevoc` als erste Zeile, im Kommentarstil ihrer
  Sprache: `// ...` in C#, C und C++, `<!-- ... -->` in XAML, `# ...` in PowerShell, Python, Shell und Unit-Dateien, `; ...`
  im Setup-Skript. In Shell-Skripten steht sie hinter `#!` und in Python hinter der Kodierungszeile, also in Zeile 2.
  Byte-Reihenfolge-Marke und Zeilenenden jeder Datei sind unverändert.
- **Wie viele.** 563 Dateien (am Stand `95aa78b` gezählt: 565 eigene Quelldateien nach Endung, abzüglich der zwei
  Ausnahmen unten). Die Zählung der Standzeile nennt 561 (Befund 17 in den Offenen Punkten). Die Zeile enthält weder
  Copyright noch Lizenz noch den bürgerlichen Namen: Die Lizenz ist offen (F8), und der Maintainer ist eine Rolle, kein
  Recht (F3.1). Eine SPDX-Zeile enthält keine Datei.
- **Ausnahmen.** `QrCode.cs` und `WeichesClipping.cs` (Übertragungen; sie tragen den Vermerk ihrer Rechteinhaber, Notices
  Nr. 11 und 12 mit Fundstelle in der Datei); die `.cmd`-Paketdateien (Doppelklick-Dateien; `Profil erneuern.cmd` darf
  nach `EinrichtenSkriptTests` nur drei Zeilen haben); Bauvorlagen (`.csproj`, CMake, meson, `.def`, `.manifest`);
  `native\quellen`, `native\out`, `native\bau`, die Bauordner `bin` und `obj` sowie die Testdaten (ihre Erzeugungsskripte
  heißen `*.cs.txt`).
- **Metadaten.** `Directory.Build.props` setzt `Company` und `Authors` auf "Sarevoc" und `Copyright` auf "Maintainer:
  Sarevoc" (ohne Jahr und ohne Aussage über den Umfang von Rechten). Das Setup nennt Sarevoc als Herausgeber (`AppPublisher`,
  `VersionInfoCompany`) und trägt "Maintainer: Sarevoc" als `AppCopyright` und `VersionInfoCopyright` ein.
  `THIRD-PARTY-NOTICES.txt` führt unter "Eigener Code" den Maintainer und vermerkt, dass die Lizenz noch nicht entschieden
  ist.
- **Wächter.** `MaintainerKopfzeileTests` (fünf Tests) prüft, dass die Zeile in jeder eigenen Quelldatei an der richtigen
  Stelle steht, keine Rechtsbehauptung enthält, dass die Ausnahmen den fremden Vermerk tragen und in den Notices stehen,
  dass die Metadaten stimmen und dass der bürgerliche Name in Code, Tests, Werkzeugen, Setup, Konfiguration und Doku nicht vorkommt.
- **Falle.** Eine Zeilenkommentar-Zeile unmittelbar vor dem Hilfeblock `<# ... #>` macht ihn für `Get-Help` unsichtbar
  (gemessen an `opus.ps1`: die Synopsis fehlte). In PowerShell-Skripten steht deshalb eine Leerzeile dazwischen; der Wächter
  prüft das.

Davor hatte keine Quelldatei einen eigenen Kopf. Gemessen am 01.10.2026, Stand `d45e33f`: 592 versionierte Dateien,
darunter 508 Quelldateien in C#, XAML, PowerShell, Shell, C und C++; am Stand `8c73864` (Fassung 0.4.1) 600 versionierte
Dateien und 516 Quelldateien. Am Stand `95aa78b` sind es 665 versionierte Dateien. Keine Datei enthält eine
SPDX-Zeile (am Stand `95aa78b` mit `git grep` gezählt: 0 Dateien außerhalb des Handbuchs). Fremde Vermerke tragen nur
`QrCode.cs` (Kopf nach Project Nayuki) und `WeichesClipping.cs` (in der Beschreibung); in `native\eigen` steht keiner
(Stand `d45e33f`).

### F9.2 Vorschlag: SPDX-Kennungen

SPDX ist eine verbreitete, maschinenlesbare Form für Lizenzangaben; die REUSE-Spezifikation der Free Software
Foundation Europe legt fest, wie sie in Dateien stehen. Sobald die Lizenz feststeht (Entscheidung 2 in den Offenen
Punkten), kämen zwei Zeilen hinter die Maintainer-Zeile von 0.5.0. Bis dahin behauptet keine Kopfzeile ein Recht (F5.5).
Vorschlag, nicht umgesetzt:

Geschlossen:

```
// JG TacRelay - Maintainer: Sarevoc
// SPDX-FileCopyrightText: 2026 Sarevoc
// SPDX-License-Identifier: LicenseRef-JG-TacRelay-Nutzungsbedingungen
```

Offen, hier mit GPL-3.0 als Beispiel:

```
// JG TacRelay - Maintainer: Sarevoc
// SPDX-FileCopyrightText: 2026 Sarevoc
// SPDX-License-Identifier: GPL-3.0-or-later
```

Die Kennungen der anderen Wege: `GPL-3.0-only`, `AGPL-3.0-only`, `AGPL-3.0-or-later`, `MIT`, `Apache-2.0`, `EUPL-1.2`.
Eine eigene Lizenz trägt eine Kennung mit `LicenseRef-`, ihr Text liegt dann in `LICENSES\LicenseRef-...txt`.

Der Wächter `MaintainerKopfzeileTests` liest nur die Maintainer-Zeile und ihre Stelle (am Code geprüft); die SPDX-Zeilen
darunter stören ihn nicht, in PowerShell-Dateien braucht auch die letzte Kopfzeile eine Leerzeile vor dem Hilfeblock.

Übertragungen tragen heute keine Maintainer-Zeile, sondern den Vermerk ihrer Rechteinhaber (F9.1). Mit SPDX behielten
sie ihre Lizenz und ihren Kopf; die eigenen Zeilen kämen davor:

```
// SPDX-FileCopyrightText: Project Nayuki
// SPDX-FileCopyrightText: 2026 Sarevoc
// SPDX-License-Identifier: MIT
// JG TacRelay. Maintainer: Sarevoc. Übertragung nach C#, THIRD-PARTY-NOTICES.txt Nr. 11.
```

```
// SPDX-FileCopyrightText: 2011 Xiph.Org Foundation, Skype Limited
// SPDX-FileCopyrightText: 2024 Arm Limited
// SPDX-FileCopyrightText: 2026 Sarevoc
// SPDX-License-Identifier: BSD-2-Clause
// JG TacRelay. Maintainer: Sarevoc. Übertragung nach C#, THIRD-PARTY-NOTICES.txt Nr. 12.
```

Der Kopf von `src/opus.c` hat zwei Bedingungen, also die Form der BSD-2-Clause; libopus als Ganzes steht unter
BSD-3-Clause (`COPYING`). Den Wortlaut vor dem Eintrag mit der SPDX-Liste vergleichen.

Zentral dazu: die Lizenzdatei (oder `LICENSES\<Kennung>.txt`), `THIRD-PARTY-NOTICES.txt` und der Satz "Soweit an
JG TacRelay Rechte bestehen, liegen sie bei Sarevoc." (F5.5).

### F9.3 Je Dateiart

Anzahl gemessen mit `git ls-files` am Stand `95aa78b`. "Maintainer-Zeile" nennt, was seit 0.5.0 in der Datei steht;
"Lizenzzeile" ist der Vorschlag für die Zeit nach der Lizenzentscheidung.

| Dateiart | Anzahl | Maintainer-Zeile (0.5.0) | Lizenzzeile (Vorschlag) | Achtung |
| --- | --- | --- | --- | --- |
| `.cs` | 524 (330 in `src`, 194 in `tests`) | ja, `// ...` in Zeile 1 | `// SPDX-...` hinter der Maintainer-Zeile | `QrCode.cs` und `WeichesClipping.cs` wie oben ohne die Zeile |
| `.xaml` | 11 | ja, `<!-- ... -->` vor dem Wurzelelement | `<!-- SPDX-... -->` | nach dem Einfügen bauen und Bildtests laufen lassen |
| `.ps1` | 17 | ja, `# ...` mit einer Leerzeile vor dem Hilfeblock | `# SPDX-...` | Eine Zeilenkommentar-Zeile unmittelbar vor `<#` macht den Hilfeblock für `Get-Help` unsichtbar (gemessen). |
| `.py` | 1 (`tools\handbuch-pdf.py`) | ja, in Zeile 2 hinter der Kodierungszeile | `# SPDX-...` | - |
| `.sh` | 1 (`funk-einrichten.sh`) | ja, in Zeile 2 hinter `#!/usr/bin/env bash` | `# SPDX-...` | Zeilenenden LF bleiben (`EinrichtenSkriptTests`). |
| `.c`, `.h`, `.cpp` | 4, 3, 1 (nur `native\eigen`) | ja, `// ...` | `/* SPDX-... */` | Die Quellen der Fremdbibliotheken bleiben unverändert und liegen nicht im Git. |
| `.csproj`, `.props`, `.slnx`, `.config`, `.manifest` | 11, 1, 1, 1, 1 | nein (Bauvorlagen; `Directory.Build.props` trägt den Maintainer in `Company`, `Authors`, `Copyright`) | `<!-- SPDX-... -->` | nach einer `<?xml ...?>`-Zeile, falls vorhanden |
| `.iss`, `.def` | 1, 2 | `.iss` ja (`; ...`), `.def` nein | `; SPDX-...` | - |
| `.service`, `.conf`, `meson.build`, `CMakeLists.txt` | 2, 2, 1, 2 | `.service` ja (`# ...`), die übrigen nein | `# SPDX-...` | - |
| `.cmd` | 4 | nein | `@REM SPDX-...` oder Begleitdatei | `Profil erneuern.cmd` muss genau drei Zeilen und nur ASCII haben (`EinrichtenSkriptTests.Doppelklick_DreiZeilenCrlfUndBekannteOptionen`); hier nur eine Begleitdatei. |
| `.json` | 21 | nein | JSON kennt keine Kommentare: Begleitdatei oder Sammelangabe | `konfig\profil.json` ist signiert; die Testdaten unter `tests\Funk.Tests\Daten\` haben die Programme selbst geschrieben und bleiben bytegleich. |
| `.md` | 27 | nein | `<!-- SPDX-... -->` in der ersten Zeile | - |
| übrige `.txt` | 10 | nein | je nach Datei | `konfig\profilschluessel.txt` ist eine Zeile, die Werkzeuge lesen: Begleitdatei. |
| `.png`, `.ico` | 13, 1 | nein | Begleitdatei | - |

"Begleitdatei" heißt nach REUSE eine Datei `<name>.license` mit den SPDX-Zeilen neben der Datei; eine Sammelangabe für
viele Dateien erlaubt die REUSE-Spezifikation in einer Datei `REUSE.toml`. Das Werkzeug `reuse` der FSFE prüft das, ist
aber hier nicht installiert und nicht erprobt.

Einige Tests prüfen Dateien wörtlich. Nach dem Einfügen der Maintainer-Zeile lief die ganze Suite (1312 von 1312
bestanden am 02.10.2026, `PROJEKT.md`, Standzeile 01:00); nach dem Einfügen der Lizenzzeilen ist sie wieder laufen zu
lassen, im Wurzelordner des Repositorys:

```
pwsh -NoProfile -File tests\testlauf.ps1
```

Welche Quelldateien keine Maintainer-Zeile haben, zeigt diese Prüfung im Wurzelordner des Repositorys. Am Stand `95aa78b`
meldet sie genau die beiden Übertragungen `QrCode.cs` und `WeichesClipping.cs` (am 02.10.2026 gemessen: 565 Dateien
geprüft, 2 ohne die Zeile):

```
$r = (Get-Location).Path
git -C $r ls-files -- '*.cs' '*.xaml' '*.ps1' '*.py' '*.sh' '*.service' '*.iss' '*.c' '*.h' '*.cpp' |
    Where-Object { -not (Select-String -LiteralPath (Join-Path $r $_) -Pattern 'Maintainer: Sarevoc' -SimpleMatch -Quiet) }
```

Dieselbe Prüfung mit dem Muster `SPDX-License-Identifier` statt `Maintainer: Sarevoc` zeigt, welche Dateien noch keine
Lizenzzeile haben (am Stand `95aa78b`: alle).

## F10 Mitarbeit von Claude

**Entschieden** (Sarevoc, 25.11.2026): Weg B, einmal zentral. `README.md` sagt im Abschnitt "Coding", dass Code, Tests und
Doku von Claude stammen, nach den Vorgaben von Sarevoc; F1 sagt es im Handbuch. Im öffentlichen Repository steht Claude
nicht als Mitwirkender in den Commits; im privaten Arbeitsrepository nennt jeder Commit Claude weiter (Weg A). Die
Maintainer-Kopfzeile der Quelldateien (F9.1) nennt Claude nicht und behauptet keinen Rechtsstand.

Rechtlicher Rahmen, soweit dem Verfasser bekannt:

- Eine gesetzliche Pflicht, KI-Mitarbeit im Quellcode zu kennzeichnen, ist dem Verfasser nicht bekannt. Die
  Transparenzpflichten der KI-Verordnung (EU) 2024/1689, Art. 50, betreffen Anbieter und Betreiber von KI-Systemen bei
  künstlich erzeugten Bildern, Tönen, Videos und Texten zur Information der Öffentlichkeit, nicht Quellcode eines
  Hobbyprojekts (Einschätzung).
- Eine Pflicht zur Namensnennung aus Anthropics Bedingungen ist dem Verfasser nicht bekannt; nachlesen.
- Claude ist weder Urheber noch Rechteinhaber. Eine Nennung darf das nicht anders aussehen lassen: "Mitarbeit" oder
  "erstellt mit", nie "Copyright" oder "Autor".

| Weg | Wie | Für | Gegen |
| --- | --- | --- | --- |
| A: nur die Historie (Stand heute) | `Co-Authored-By` in jedem Commit, sonst nichts | Kein Aufwand. In jedem Repository mit Historie sichtbar. | Wer nur Setup oder Quelltext ohne Historie bekommt, erfährt nichts. Fällt es später auf, kann es wie verschwiegen wirken. |
| B: einmal zentral | Ein Absatz in Lizenzdatei, `NOTICE` oder Liesmich und im Handbuch, etwa: "JG TacRelay entsteht mit Claude, einem KI-Modell von Anthropic, als Werkzeug, unter Anleitung und Verantwortung von Sarevoc. Claude ist weder Urheber noch Rechteinhaber." | Ehrlich, an einem Ort, Quelldateien bleiben schlank. Passt zur Rechtslage oben und zur Historie. | Wer den Schutz des Codes bestreiten will, kann sich darauf berufen; das folgt allerdings schon aus der Historie. |
| C: in jeder Datei | Eine weitere Kopfzeile neben der Maintainer-Zeile, etwa `// Mitarbeit: Claude (KI-Modell von Anthropic), ohne eigene Rechte.` SPDX kennt auch ein Feld für Mitwirkende (`SPDX-FileContributor`). | Klar je Datei, auch wenn eine Datei herausgelöst wird. | 563 Dateien mehr Text. Muss für Dateien, die ein Mensch schreibt (künftig bei Mitarbeit von außen), gepflegt werden. Ein Feld "Contributor" liest sich leicht wie Miturheber. |
| D: gar nicht, Historie bereinigen | Commits umschreiben, die Zeile entfernen | Kein Hinweis mehr sichtbar. | Zerstört den Beleg, wer was beigetragen hat, auch den der menschlichen Anteile; ändert alle Commit-Kennungen. Und es stimmt dann nicht mehr. |

Wenn genannt wird, dann als "Claude, ein KI-Modell von Anthropic", ohne Fassungsnummer; die Modelle wechseln (die
Historie nennt schon zwei Fassungen).

## F11 Datenschutz (DSGVO)

### F11.1 Gilt die DSGVO?

Für rein persönliche oder familiäre Tätigkeiten gilt sie nicht (Art. 2 Abs. 2 lit. c DSGVO, Haushaltsausnahme). Der
EuGH legt diese Ausnahme eng aus (Lindqvist, C-101/01; Ryneš, C-212/13; Zeugen Jehovas, C-25/17). Ein Server für eine Org
mit 5 bis 40 gleichzeitigen Teilnehmern, Admins, Rollen und einem Verwaltungsprotokoll geht über den Familienkreis
hinaus. Ob die Ausnahme greift, ist zweifelhaft. Sicher ist, so zu handeln, als gelte die DSGVO; das Folgende geht davon
aus.

### F11.2 Welche personenbezogenen Daten der Server hält

Stand Server 0.5.0 (Zustandsdateien von 0.4.0 lädt er ohne Verlust). Die Zustandsdateien liegen in `/var/lib/funk` (Ordner
0700, `funk-server.service`). Der Server
schreibt sie unter Linux mit Modus 0600, die Unit setzt dazu `UMask=0077` (`Dateiablage.cs`, Voreinstellung
`nurBesitzer`; `BETRIEB.md`, "Was läuft"). Das steht so im Code; unter Linux gemessen ist der Modus noch nicht
(`PROJEKT.md`).

| Datei | Inhalt mit Personenbezug | Löschung, Frist | Quelle |
| --- | --- | --- | --- |
| `konten.json` | Je Konto: Konto-ID, Discord-ID (nur bei Konten, die ein Admin an der Konsole mit `--discord` angelegt oder verknüpft hat; Konten aus einem Antrag haben keine), Discord-Benutzername, Anzeigename, 1 bis 3 SC-Namen, Discord-Name, Stufe, Rechte, Status mit Sperrgrund, Eigentümer-Kennzeichen, Zeit des letzten Geräteereignisses; offene Anträge mit SC-Name, Discord-Name und Zeitpunkt. Je Gerät: Fingerabdruck des Geräteschlüssels, Gerätename, Schlüsselart, Status, verknüpft, freigegeben, zuletzt verbunden. Fingerabdrücke dauerhaft gesperrter Geräte. | Gerät in WARTET nach 24 h gelöscht. Freigegebenes Gerät nach 90 Tagen ohne Sitzung zurück auf WARTET. Konto ohne freigegebenes Gerät 30 Tage nach dem letzten Geräteereignis gelöscht, nie das des Eigentümers und nie der letzte aktive Admin. Ein Antrag ohne wartendes Gerät verschwindet sofort. Gesperrte Fingerabdrücke bleiben als Hashwerte. | `Modell.cs`, `Kontenbuch.cs` Z. 207 bis 231, Protokoll 3.3 und 3.6 |
| `lage.json` | Je Konto: Felder, Platz, Aufstellungen, ab 0.3.0 Aufträge für einen Abend mit Zeiten, Urheber und Grund, ab 0.5.0 `alsFunkerBis` (bis wann ein Admin "Heute als Funker" spielt). Dazu im Lagezustand die laufende Phase mit Nummer, Zeit und Konto-ID des Schaltenden. Die Phasen selbst (Netzbild je Rolle) haben keinen Personenbezug. | mit dem Konto. `alsFunkerBis` endet spätestens nach 24 h, die laufende Phase fällt 12 h nach dem letzten Schalten auf die Grundstellung; `funk-admin sicherung` lässt Aufträge und `alsFunkerBis` weg, Phasen und laufende Phase bleiben | `BETRIEB.md`, Protokoll 6.14 und 6.15, `Zustandsablage.cs` |
| `komfort.json` | Pegel, die ein Konto für andere eingestellt hat (höchstens 2000 Einträge je Konto), gespeicherte Sätze, letzte Sendeauswahl | mit dem Konto, samt der Pegel anderer für dieses Konto | `Komfortablage.cs` |
| `mitglieder.json` | Mitgliederrollen, bis zu 8 Lieblingsrollen, Abzeichen; Vormerkungen über Mitglieder, die sich noch nicht angemeldet haben: SC-Name, Discord-Name, Rollen, freie Notiz; dazu Untergilden aus der Zeit vor 0.11.8 (Name, Kürzel, Konto- und Vormerkungsnummern), seit 0.11.8 weder gezeigt noch gelesen | Mitgliedsangaben mit dem Konto. Die alten Untergilden bleiben, damit eine ältere Serverfassung den Stand laden kann; die Nummer eines gelöschten Kontos oder einer verschwundenen Vormerkung fällt darin heraus, sonst ändert sich nichts. Eine Vormerkung verschwindet, wenn sie bei einer Freigabe übernommen wird oder ein Admin sie löscht; eine Frist gibt es nicht. | `Mitgliederbuch.cs` Z. 343, 625 und 641, Protokoll 3.5 und 6.13 |
| `admins.json` | Admin-Schlüssel (nur öffentlicher Teil), TOTP-Geheimnis verschlüsselt (AES-256-GCM), Prüfwerte der Notfallcodes (HMAC), Fehlversuche, Geräte mit Admin-Sitzung | mit dem Konto | Protokoll 3.9, `Zentrale.cs` Z. 571 |
| `journal.json` | Verwaltungsprotokoll mit Klarnamen: Zeit, Art, Urheber, betroffenes Konto, Text bis 200 Zeichen; ab 0.5.0 auch `FUNKER_MODUS_EIN` und `FUNKER_MODUS_AUS` | 90 Tage, höchstens 50.000 Einträge. Einträge zu einem gelöschten Konto bleiben bis zum Ablauf der 90 Tage. | `Journal.cs`, `Zentrale.Raidleitung.cs` |
| `vorrangspur.json` | Präfixe, von denen eine Sitzung kam (IPv4-Adresse oder IPv6-/64), nur als HMAC-SHA256-Werte, höchstens 2000 | 14 Tage | `Vorrangspur.cs` |
| `sperrliste.json` | Gesperrte Präfixe nur als HMAC-Werte und Kennung | Sperre höchstens 24 h, die Stufe 48 h nach dem Ende der letzten Sperre | `BETRIEB.md`, "Sperrliste" |
| `sperren.log` (nur wenn `sperrliste.klartextDatei` gesetzt ist, ab Werk aus) | Je neuer Sperre eine Zeile mit der IP-Adresse im Klartext, für fail2ban | keine Frist (offen, `PROJEKT.md`) | `BETRIEB.md`, Konfiguration |
| Logbuch (systemd-Journal, wahlweise `log.datei`) | IP-Adressen, Präfixe und Konto-IDs nur als gekürzter HMAC-Wert (`ip#...`, `k#...`, 10 Hexziffern); alle 10 s eine Messwertzeile je Sitzung; ab 0.5.0 je Phasenwechsel eine Zeile mit den Nummern, der Zahl der Umgestellten und Ausgelassenen und dem Schaltenden als `k#...` | nach den Einstellungen von systemd-journald; das Projekt legt nichts fest | `Logbuch.cs`, `BETRIEB.md`, `Zentrale.cs` |
| Sicherungen (`funk-admin sicherung`, Container-Sicherungen mit vzdump) | Kopien der Zustandsdateien samt Journal mit Klarnamen | keine Frist festgelegt | `BETRIEB.md`, "Laufender Betrieb" |

Nur im Arbeitsspeicher, solange eine Verbindung besteht: IP-Adresse und Port, Messwerte je Sitzung (Verlust, Jitter,
Laufzeit, Güte; `Relais.cs`), wer gerade online ist und wer in welchem Netz spricht.

**Nicht gespeichert:**

- Sprache. Der Server sieht die Opus-Nutzlast im Klartext, denn verschlüsselt wird von Station zu Station, nicht von Ende
  zu Ende (Protokoll 1.2, Nicht-Ziele). Er leitet sie nur weiter; im Sprachpfad (`src\Funk.Server\Sprachpfad`) gibt es
  keinen Dateizugriff, und das Logbuch nimmt Opus nie auf (`Logbuch.cs`).
- Klartext-IP in Dateien und Logbuch, außer in der wahlweisen `sperren.log`.
- Codes, Geheimnisse und Notfallcodes im Journal (Protokoll 3.9).
- Telemetrie und Absturzberichte: keine (`ENTWURF.md`, Sicherheit S7).

**IP als HMAC heißt pseudonym, nicht anonym.** Ohne das Betriebsgeheimnis lässt sich aus dem Wert keine Adresse ablesen.
Wer das Geheimnis hat, kann für eine bekannte Adresse den Wert ausrechnen und vergleichen, und bei IPv4 sind es nur rund
4,3 Milliarden Adressen (Rechnung). Pseudonyme Daten bleiben personenbezogene Daten (Art. 4 Nr. 5 DSGVO);
IP-Adressen können es nach dem EuGH schon für sich sein (Breyer, C-582/14).

**Auf den Rechnern der Mitglieder** speichert der Client Einstellungen, die Namen aus dem Setup (`anmeldung.json`), das
Serverprofil und sein Protokoll unter `%LOCALAPPDATA%\JG-TacRelay` (`EinstellungsSpeicher.cs` Z. 60); der
Geräteschlüssel liegt im TPM oder im Schlüsselspeicher von Windows (Protokoll 3.2). Das verarbeitet nicht der
Betreiber, sondern das Programm auf dem eigenen Rechner. Für das Ablegen auf dem Endgerät gilt § 25 TDDDG; was für den
gewünschten Dienst unbedingt nötig ist, braucht keine Einwilligung (§ 25 Abs. 2 Nr. 2 TDDDG, Einschätzung).

### F11.3 Wer was sieht

Nach Protokoll 6.13 und 3.9:

| Wer | Sieht |
| --- | --- |
| alle, die im Funk sind | Hauptname und Discord-Name der Anwesenden; Zeichen und Abzeichen, solange der Inhaber seine Angaben freigibt (`karteFreigeben`, ab Werk an); Rollen, Wünsche und erste Wahl mit Freigabe und solange `mitgliedskarte.rollenFuerAlle` gilt (ab Werk ja) |
| Einsatzleitung (Recht `ZUWEISEN`) und ADMIN-Konten | zusätzlich weitere SC-Namen, Verbindung, Güte und letzten Platz, unabhängig von der Freigabe; ab 0.5.0 auch, ob ein Admin heute als Funker spielt (`alsFunker` in den Präsenz-Details, Protokoll 6.15) |
| Admins in einer Admin-Sitzung und die Konsole | Geräte und Anträge (mit Gerätename und Kennung), Vormerkungen, das Journal |
| das eigene Konto | alles über sich, auch die weiteren SC-Namen |

Karten anderer bekommt nur, wer gerade online ist oder einen Platz hat; über die Karte lässt sich nicht die ganze
Mitgliederliste abfragen (6.13).

### F11.4 Wer verantwortlich ist

| Phase | Betrieb | Verantwortlicher (Art. 4 Nr. 7 DSGVO) | Weitere Beteiligte |
| --- | --- | --- | --- |
| Heimserver (ein Container daheim) | wer den Server betreibt | wer über Zweck und Mittel entscheidet, meist der Betreiber | keine; der Internetanbieter transportiert nur |
| Nach einem Umzug auf einen gemieteten Server (erst nach Version 1 und einem Test mit ein paar Leuten, Anf. A55) | ein anderer Betreiber, bei Hetzner, OVH oder anderswo | Wer über Zweck und Mittel entscheidet: der Betreiber allein, oder Sarevoc und der Betreiber gemeinsam. Gemeinsam Verantwortliche brauchen eine Vereinbarung (Art. 26 DSGVO). | Das Rechenzentrum verarbeitet als Auftragsverarbeiter (Art. 28 DSGVO) und braucht einen Vertrag dazu; große Anbieter stellen ihn nach Kenntnisstand bereit. Steht der Server außerhalb der EU, gelten Art. 44 ff. DSGVO. |
| Admins (vom Eigentümer ernannt, Anf. A44) | - | keine eigenen Verantwortlichen; sie handeln für den Verantwortlichen | auf Vertraulichkeit verpflichten |

In der Heimphase sehen alle Mitglieder die Heim-IP des Servers (Protokoll, Offener Punkt 6). Das betrifft die
eigenen Daten von Sarevoc; die Mitglieder sehen ihre Adressen gegenseitig nicht, weil alles über den Server läuft
(aus dem Aufbau abgeleitet).

### F11.5 Rechtsgrundlage

Festlegen muss sie der Verantwortliche. In Frage kommen:

| Verarbeitung | Grundlage | Bemerkung |
| --- | --- | --- |
| Konto, Geräte, Netze, Plätze, Anwesenheit | Art. 6 Abs. 1 lit. b DSGVO, sonst lit. f | lit. b setzt einen Nutzungsvertrag voraus, etwa über Nutzungsbedingungen; ist das Funken eine bloße Gefälligkeit unter Mitgliedern, eher lit. f |
| Sperrliste, Vorrangspur, Logbuch, Journal | lit. f, Sicherheit und Nachvollziehbarkeit | Erwägungsgrund 49 nennt Netz- und Informationssicherheit als berechtigtes Interesse |
| Lieblingsrollen, Freigabe der Karte | lit. f mit der Möglichkeit, es abzuschalten, oder lit. a | Die Freigabe ist ab Werk an. |
| Vormerkungen vor der Anmeldung | lit. f | Diese Daten stammen nicht vom Mitglied selbst: Information nach Art. 14 DSGVO, spätestens bei der ersten Mitteilung an das Mitglied. Die freie Notiz sparsam führen. |

### F11.6 Fristen: was eingebaut ist und was fehlt

Eingebaut sind die Fristen in der Tabelle oben. Aus dem Code abgeleitet, nicht als Ablauf gemessen: Ein Mitglied, das
nicht mehr kommt, verliert nach 90 Tagen die Freigabe seines Geräts, und weil auch dieser Rückfall als Geräteereignis
zählt (`MODULBERICHT-protokoll.md`), wird sein Konto etwa 30 Tage später gelöscht, also rund 120 Tage nach der letzten
Verbindung.

Es fehlen:

| Lücke | Vorschlag |
| --- | --- |
| Logbuch im systemd-Journal ohne Frist des Projekts | Frist für journald festlegen (gilt für alle Dienste im Container). Beispiel unten; der Wert 30 Tage ist eine Annahme. |
| `sperren.log` ohne Frist | nur einschalten, wenn fail2ban wirklich läuft; sonst eine Frist mit logrotate (Beispiel unten) |
| Sicherungen ohne Frist | eine Frist festlegen und alte Sicherungen löschen; Container-Sicherungen nur verschlüsselt ablegen (`BETRIEB.md`, T14) |
| Journal nach Löschung eines Kontos | bleibt bis zu 90 Tage; im Datenschutzhinweis nennen oder im Code ein Löschen je Konto ergänzen |
| Vormerkungen ohne Frist | Vormerkungen, die nie zu einer Anmeldung führen, von Zeit zu Zeit löschen, etwa nach jedem Einsatz durchsehen |
| Pegel und Sätze eines Kontos nicht abfragbar | Für eine Auskunft gibt es keinen Befehl; die Daten stehen in `/var/lib/funk/komfort.json` unter der Konto-ID. |

Beispiel für journald (Vorschlag, am Heimserver nicht gemessen), in `/etc/systemd/journald.conf` unter `[Journal]`:

```
MaxRetentionSec=30d
```

danach

```
systemctl restart systemd-journald
```

Beispiel für logrotate (Vorschlag, nicht gemessen), Datei `/etc/logrotate.d/funk-sperren`. Der Server öffnet die Datei
für jede Zeile neu (`Zentrale.Sperrliste.cs`); `create` legt sie nach dem Drehen gleich wieder an, weil fail2ban ohne die
Datei nicht startet (`BETRIEB.md`):

```
/var/log/funk/sperren.log {
    weekly
    rotate 4
    missingok
    notifempty
    create 0600 funk funk
}
```

### F11.7 Rechte der Mitglieder und wie du sie erfüllst

Auskunft (Art. 15), Berichtigung (Art. 16), Löschung (Art. 17), Einschränkung (Art. 18), Datenübertragbarkeit (Art. 20),
Widerspruch (Art. 21). Am Server, als root oder als Benutzer `funk`:

```
# Auskunft: Konten, Geräte eines Kontos, Mitgliedsangaben, Journal zu einem Konto
dotnet /opt/funk/funk-admin.dll konten --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll geraete --konto <id> --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll mitglieder --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll journal --konto <id> --anzahl 200 --konfig /etc/funk/server.json

# Maschinenlesbar (Art. 20): dieselben Befehle mit --json-aus
dotnet /opt/funk/funk-admin.dll konten --json-aus --konfig /etc/funk/server.json

# Berichtigung
dotnet /opt/funk/funk-admin.dll scnamen --konto <id> --namen <Name1>,<Name2> --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll discordname --konto <id> --name "<Discord-Name>" --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll name --konto <id> --name <Anzeigename> --konfig /etc/funk/server.json

# Löschung: Konto, Geräte, Pegel, Sätze, Plätze, Mitgliedsangaben, Admin-Nachweis
dotnet /opt/funk/funk-admin.dll konto-loeschen --konto <id> --konfig /etc/funk/server.json
```

- `konten` und `mitglieder` zeigen alle Konten; die Zeilen des Mitglieds heraussuchen und nur diese herausgeben.
- Das Konto des Eigentümers löscht `konto-loeschen` nicht: Erst mit `eigentuemer --konto <id>` ein anderes ADMIN-Konto
  zum Eigentümer machen (`BETRIEB.md`).
- Was nach dem Löschen bleibt: der Fingerabdruck eines Geräts, das ein Admin bewusst dauerhaft gesperrt hat (`sperren`), als
  Hashwert (seit 0.7.0 sperren Ablehnen und Löschen nichts), Journaleinträge bis zum
  Ablauf der 90 Tage, Sicherungen bis zu ihrer Löschung, HMAC-Werte der Vorrangspur bis 14 Tage.
- Im Client löscht ein Admin in der Admin-Sitzung mit frischer Bestätigung (Protokoll 3.6, 3.9).

### F11.8 Sicherheit der Verarbeitung (Art. 32)

| Maßnahme | Quelle |
| --- | --- |
| Steuerkanal TLS 1.3 oder 1.2 mit Gerätezertifikat, Server über Pins geprüft | Protokoll 3.1, 3.2, 4.1 |
| Sprache über UDP mit AES-256-GCM verschlüsselt, Schlüssel je Sitzung (von Station zu Station) | Protokoll 1 (Übersicht) und 1.2 |
| Zustandsordner 0700, Zustandsdateien 0600 (aus dem Code), Dienst als eigener Benutzer mit Härtung in der Unit | `konfig\funk-server.service`, `Dateiablage.cs`, `BETRIEB.md` |
| TOTP-Geheimnisse verschlüsselt, Notfallcodes nur als HMAC, Schlüssel per `LoadCredential`, nie in einer Sicherung | Protokoll 3.9, `BETRIEB.md` |
| Admin nur mit eigenem Schlüssel im TPM und Zweitfaktor; ab Client 0.4.1 TPM 2.0 Pflicht, Windows-Konto mit PIN oder Kennwort als Richtlinie, BitLocker empfohlen | Protokoll 3.9, Anf. A56 |
| Logbuch pseudonymisiert, Sperrliste gegen Scanner und Durchprobieren | `Logbuch.cs`, `BETRIEB.md` |
| Container-Sicherungen nur verschlüsselt | `BETRIEB.md`, T14 |
| keine Telemetrie | `ENTWURF.md`, S7 |

Weiter gilt:

- Ein Verzeichnis der Verarbeitungstätigkeiten (Art. 30 DSGVO) lohnt sich. Die Ausnahme für kleine Einrichtungen
  (Art. 30 Abs. 5) greift nicht, wenn die Verarbeitung nicht nur gelegentlich erfolgt, und ein Funkserver läuft ständig.
  Die Tabellen dieses Abschnitts sind die Grundlage.
- Eine Datenschutz-Folgenabschätzung (Art. 35) hält der Verfasser nicht für nötig: keine besonderen Kategorien, keine
  Überwachung, kein Profiling (Einschätzung).
- Datenpanne: Wird der Server übernommen oder eine Sicherung gestohlen (Protokoll T14, T15), ist das binnen 72
  Stunden der Aufsichtsbehörde zu melden, außer es führt voraussichtlich zu keinem Risiko (Art. 33); bei hohem Risiko
  sind die Mitglieder zu benachrichtigen (Art. 34). Das Journal enthält Klarnamen.
- Kommt der Discord-Bot (Protokoll 3.7, 3.8, heute nicht gebaut), braucht er nach Discords Regeln eine
  Datenschutzerklärung (`ENTWURF.md`), und der Hinweis unten ist um Discord-ID, Abgleich und Raid-Helper zu ergänzen.

### F11.9 Mitschnitte, Fernmeldegeheimnis und Genehmigung

- Wer das nicht öffentlich gesprochene Wort eines anderen unbefugt aufnimmt, macht sich strafbar (§ 201 StGB). Ein
  Mitschnitt oder ein Stream mit den Stimmen anderer aus dem Funk gehört deshalb in die Regeln der Gruppe: nur mit
  Einverständnis. JG TacRelay selbst nimmt nichts auf.
- Ob der Betreiber unter das Fernmeldegeheimnis fällt (§ 3 TDDDG), hängt davon ab, ob der Funk als geschäftsmäßig
  angebotener Telekommunikationsdienst gilt; für den Server einer Spielergruppe ist das ungeklärt. Unabhängig davon: nicht mithören,
  nicht aufzeichnen. Der Server hat dafür keine Funktion.
- **Genehmigung.** Nach bisheriger Einschätzung braucht der Betrieb keine Genehmigung und keine Meldung. JG TacRelay
  nutzt keine Funkfrequenzen, sondern das Internet; eine Frequenzzuteilung entfällt also. Für Telekommunikationsdienste
  kennt das Telekommunikationsgesetz keine Genehmigung, sondern eine Meldepflicht bei der Bundesnetzagentur (§ 5 TKG), und
  die trifft nach Kenntnisstand nur, wer ein öffentliches Telekommunikationsnetz betreibt oder einen öffentlich
  zugänglichen Telekommunikationsdienst erbringt. Ein Funkserver, in den nur Mitglieder der Gruppe nach einzelner Freigabe
  durch einen Admin kommen (C10), ist nach Einschätzung nicht öffentlich zugänglich. Das kann sich ändern, wenn der Funk
  jedem offensteht, etwa ohne Freigabe oder als Angebot an andere Gruppen. Die DSGVO ist unabhängig davon zu beachten:
  Sicher ist, so zu handeln, als gelte sie (F11.1). Kein Rechtsrat: Verbindlich klärt das ein Fachanwalt für IT-Recht.

### F11.10 Datenschutzhinweis für Mitglieder (Entwurf)

Stand Fassung 0.5.0, für die Heimphase. Die Platzhalter in eckigen Klammern füllt der Verantwortliche aus; die
Rechtsgrundlage legt er fest. Vor dem Gebrauch prüfen lassen.

> **Datenschutzhinweis für JG TacRelay**
>
> **Wer ist verantwortlich?** Sarevoc [falls das Pseudonym hier nicht reicht, der bürgerliche Name, F3.3], erreichbar
> über [KONTAKT]. Sarevoc betreibt den Funkserver der Gruppe
> [NAME DER GRUPPE].
>
> **Wozu?** Damit du mit deiner Gruppe funken kannst: Anmeldung und Freigabe deines Geräts, Netze und Plätze, Einteilung für
> Einsätze, Verwaltung der Mitglieder und Schutz des Servers vor Angriffen.
>
> **Was gespeichert wird**
>
> - Deine Namen: SC-Namen (bis zu drei), Discord-Name, Anzeigename. Hat ein Admin dein Konto mit Discord verknüpft, auch
>   deine Discord-ID und deinen Discord-Benutzernamen.
> - Deine Geräte: eine Kennung (Fingerabdruck des Geräteschlüssels), der Gerätename (vorgegeben ist der Name deines
>   Windows-Rechners; du kannst ihn vor der Anmeldung im Programm ändern, A2.2), Schlüsselart, Status, wann verknüpft, freigegeben und zuletzt
>   verbunden.
> - Deine Rolle in der Gruppe: Stufe, Rechte, Netze, Platz, Aufstellungen, Mitgliederrollen, Lieblingsrollen, Abzeichen;
>   bei Admins, ob und bis wann sie heute als Funker spielen.
>   Was ein Admin vor deiner Anmeldung über dich vorgemerkt hat: Namen, Rollen, eine Notiz. Aus der Zeit vor 0.11.8
>   kann noch eine Untergilde gespeichert sein; sie wird nicht mehr gezeigt.
> - Deine Einstellungen am Server: Pegel, die du für andere eingestellt hast, deine gespeicherten Sätze, deine
>   Sendeauswahl.
> - Ein Verwaltungsprotokoll (Journal) mit Namen: wer wann was verwaltet hat, etwa Freigaben, Sperren und Änderungen an
>   Konten, bei Admins auch das Ein- und Ausschalten von "Heute als Funker".
> - Deine IP-Adresse nur im Arbeitsspeicher, solange du verbunden bist. Auf der Platte und im Logbuch steht sie nur als
>   Schlüsselwert, aus dem man die Adresse nicht ablesen, mit dem Schlüssel des Servers aber für eine bekannte Adresse
>   nachrechnen kann. [Nur wenn eingeschaltet: Bei einer Sperre wegen Angriffsverdacht steht die Adresse im Klartext in
>   einer Sperrdatei für fail2ban, Aufbewahrung [FRIST].]
>
> **Was nicht gespeichert wird:** deine Sprache. Der Server leitet sie nur weiter; dazu entschlüsselt er sie, wertet den
> Ton aber nicht aus und schreibt ihn nirgends hin. Keine Telemetrie, keine Absturzberichte, keine Werbung.
>
> **Wer was sieht**
>
> - Alle, die gerade im Funk sind: deinen Hauptnamen und Discord-Namen, dazu Zeichen, Abzeichen, Rollen und Wünsche,
>   solange du sie freigibst (Einstellung im Programm, ab Werk an).
> - Die Einsatzleitung und die Admins: zusätzlich deine weiteren SC-Namen, deine Verbindung und Güte und deinen letzten
>   Platz.
> - Nur die Admins: Geräte und Anträge (deine eigenen Geräte siehst du auch selbst), Vormerkungen und das Journal.
> - Niemand sonst. Die Daten gehen an keine Dritten. [Nach einem Umzug: Der Server läuft bei [ANBIETER] in [LAND]; der
>   Anbieter verarbeitet die Daten nur in unserem Auftrag.]
>
> **Rechtsgrundlage:** [vom Verantwortlichen festzulegen, etwa Art. 6 Abs. 1 lit. b DSGVO (Nutzung des Funks nach den
> Nutzungsbedingungen) und lit. f (berechtigtes Interesse am sicheren Betrieb: Sperrliste, Logbuch, Journal)].
>
> **Wie lange**
>
> - Ein Gerät, das nicht freigegeben wird: 24 Stunden.
> - Ein Gerät, das 90 Tage nicht verbunden war, braucht eine neue Freigabe.
> - Ein Konto ohne freigegebenes Gerät wird 30 Tage nach dem letzten Geräteereignis gelöscht, also rund 120 Tage nach
>   deiner letzten Verbindung, wenn du nicht mehr kommst.
> - Journal: 90 Tage. Adresswerte der Vorrangspur: 14 Tage. Sperren: höchstens 24 Stunden, ihre Stufe 48 Stunden danach.
> - Eine Vormerkung: bis zu deiner Anmeldung, dann geht sie in dein Konto über, oder bis ein Admin sie löscht.
> - "Heute als Funker" (nur Admins): bis zum nächsten 04:00 Uhr, höchstens 24 Stunden.
> - Logbuch: [FRIST]. Sicherungen: [FRIST].
> - Auf Wunsch löscht ein Admin dein Konto sofort, mit Geräten, Pegeln, Sätzen, Plätzen und Mitgliedsangaben. Danach
>   bleiben die Kennung eines dauerhaft gesperrten Geräts als Hashwert und Journaleinträge bis zum Ablauf der 90 Tage.
>
> **Deine Rechte:** Auskunft, Berichtigung, Löschung, Einschränkung, Datenübertragbarkeit und Widerspruch (Art. 15 bis 21
> DSGVO). Melde dich bei [KONTAKT]. Du kannst dich auch bei einer Aufsichtsbehörde beschweren, etwa bei
> [AUFSICHTSBEHÖRDE].
>
> **Auf deinem Rechner** speichert JG TacRelay Einstellungen, deine Namen aus dem Setup und das Serverprofil unter
> `%LOCALAPPDATA%\JG-TacRelay`; der Geräteschlüssel liegt im TPM oder im Schlüsselspeicher von Windows. Davon geht nur
> an den Server, was oben steht.
>
> **Mitschnitte:** Nimm die Stimmen anderer aus dem Funk nur mit ihrem Einverständnis auf, auch nicht in einem Stream.

## F12 Marken

| Name | Inhaber (Kenntnisstand, im Register prüfen) | Wo JG TacRelay ihn benutzt | Wie |
| --- | --- | --- | --- |
| Star Citizen | Cloud Imperium (Unternehmensgruppe um Cloud Imperium Games und Roberts Space Industries) | Doku, Anforderungen, Hinweise wie "auch über Star Citizen" | beschreibend. Keine Spielgrafik und kein Logo (`THIRD-PARTY-NOTICES.txt`: keine fremden Grafiken); der Lorbeerkranz "im SC-Stil" ist selbst gezeichnet (Anf. A49, A53). Das Programm greift nicht in den Spielprozess ein (`PROJEKT.md`, Grenzen). |
| Discord | Discord Inc. | Feld "Discord-Name", Discord-ID, Doku; ein Bot ist geplant, nicht gebaut | beschreibend, kein Logo. Ein Bot unterläge Discords Entwicklerbedingungen. |
| weitere in der Doku: Windows, BitLocker, FRITZ!Box, Proxmox, Debian, Ubuntu, Hetzner, OVH, Raid-Helper, TeamSpeak, Mumble, DCS, Foundry VTT | jeweils im Register nachsehen | Anleitungen und Vergleiche | nur zur Bezeichnung |

Regeln:

- Markenrechte greifen bei einer Benutzung im geschäftlichen Verkehr (§ 14 Abs. 2 MarkenG). Ein unentgeltliches Werkzeug
  für eine Gruppe ist das nach Einschätzung eher nicht; eine öffentliche Veröffentlichung, Spenden oder Verkauf können das
  ändern.
- Auch dann darf ein fremder Name genannt werden, um etwas zu bezeichnen oder darauf zu verweisen, wenn das den
  anständigen Gepflogenheiten entspricht (§ 23 Abs. 1 Nr. 3, Abs. 2 MarkenG; Art. 14 Abs. 1 lit. c, Abs. 2 der
  Unionsmarkenverordnung (EU) 2017/1001).
- Praktisch: keine fremden Logos, keine fremde Marke im Programmnamen (also nicht "Star Citizen Funk"), kein Eindruck
  einer Verbindung zu den Unternehmen. Auch ein Gildenlogo ist nicht enthalten, nur der eigene Platzhalter
  (`THIRD-PARTY-NOTICES.txt`).
- Hinweiszeile (seit 0.13.1 in `README.md`): "Star Citizen ist eine Marke von Cloud Imperium. Discord ist eine Marke
  von Discord Inc. JG TacRelay ist mit keinem dieser Unternehmen verbunden."

Zweitfaktor: Seit 0.6.4 nennt JG TacRelay keine bestimmte Authenticator-App mehr, weder als Pflicht noch als Beispiel; jede
TOTP-App nach RFC 6238 geht (Protokoll 3.9), die Wahl trifft jeder Admin selbst (Sarevoc, 04.10.2026). Bis 0.6.3 empfahl das
Programm eine App (Anf. A44, A52).

Der eigene Name: "JG TacRelay" ist seit dem 19.10.2026 der Programmname (Sarevoc, Q1, Q9), das ALPN-Kennzeichen ist
`tacrelay/1`. Eine Suche in TMview am 19.10.2026 fand keine aktive Marke "TacRelay"; die ebenfalls erwogenen Namen
Echogrid und VoxForge waren belegt. Vor einer Veröffentlichung in DPMAregister, EUIPO eSearch und TMview noch einmal nach
ähnlichen Marken für Software und Kommunikation suchen (Nizza-Klassen 9, 38 und 42). Auch ein Programmname kann als Werktitel geschützt sein (§ 5 Abs. 3
MarkenG), in beide Richtungen.

## F13 Repository auf GitHub

Seit 0.13.1 (25.11.2026) ist das Repository öffentlich: https://github.com/Nitrolaus/JG-TacRelay. Gearbeitet wird in
einem privaten Repository mit vollem Verlauf; auf GitHub kommt je Fassung ein bereinigter Stand als eigener Commit.

| Öffentlich | Privat |
| --- | --- |
| Quelltext, Tests, Werkzeuge zum Bauen, die Anleitungen unter `docs\` mit dem Handbuch, `LICENSE`, `NOTICE`, `README.md`, `THIRD-PARTY-NOTICES.txt` | das Entscheidungsprotokoll unter `docs\entwurf\`, die Modulberichte, der Projektstand `PROJEKT.md`, das signierte Serverprofil von Sarevocs Server und das Werkzeug, das nur mit seinen Originalbildern läuft |
| Beispielnamen (Max Mustermann) und Beispieladressen (`funk.example.org`, `192.168.1.250`, `203.0.113.10`) | echte Adressen und Rufnamen, alle Schlüssel und Geheimnisse |

Vor jedem Stand, der öffentlich wird:

1. Den Arbeitsordner auf Geheimnisse prüfen, mit dem Geheimnisprüfer der Arbeitsumgebung (nicht im Repository).
2. Den bereinigten Stand auf private Angaben prüfen, auch in Base64-kodierten Zeichenketten. Das macht das Werkzeug, das
   den Stand schneidet; bei einem Fund bricht es ab.
3. Den Stand aus einem frischen Klon bauen und alle Tests laufen lassen.

GitHub speichert den Inhalt, und ein öffentliches Repository kann jeder abspalten: Was einmal öffentlich war, lässt sich
nicht zurückholen. Ein Schlüssel, der je dort stand, gilt als verbrannt und wird ersetzt.

Die Quellen der Fremdbibliotheken liegen nicht im Git (`native\quellen`); die Apache-Lizenz verlangt nicht, sie
mitzuliefern. Ihre Lizenztexte liegen den Paketen im Ordner `lizenzen` bei (F7).

## F14 Befunde und offene Punkte

| Nr | Befund oder Frage | Fundstelle | Wer |
| --- | --- | --- | --- |
| 1 | Name des Maintainers: entschieden, "Sarevoc"; der bürgerliche Name nur, wo das Recht ihn verlangt (F3.3) | Anf. A41, A58, A59 | entschieden und mit 0.5.0 umgesetzt (Nr. 3 und 4) |
| 2 | **Entschieden** (25.11.2026): Apache-2.0 mit `NOTICE` (F1, F8) | Anf. A58, Entwurfsfrage E5, Q23 | erledigt mit 0.13.1 |
| 3 | **Behoben** mit 0.5.0: Notices, `Directory.Build.props` und Setup nennen Sarevoc als Maintainer (vorher der Name der früheren Gilde als Inhaber und als `<Company>`, kein `<Copyright>`, kein `AppCopyright`) | `THIRD-PARTY-NOTICES.txt`, `Directory.Build.props`, `installer\TacRelay.iss` | erledigt |
| 4 | **Behoben** mit 0.5.0: Jede eigene Quelldatei trägt die Maintainer-Zeile (563 am Stand `95aa78b`). Die Lizenzzeile (SPDX) fehlt weiter; die Lizenz gilt über `LICENSE` für das ganze Repository. | F9 | wahlweise später |
| 5 | Nennung von Claude: **entschieden**, Weg B (`README.md`, F1) | F10 | erledigt mit 0.13.1 |
| 6 | Modellgewichte in `opus.dll` und `rnnoise.dll` ungeklärt; dazu die Gewichte der Sprachkennung in `webrtc_apm.dll`, in den Notices nicht vermerkt (ob gelinkt, ungeprüft) | Notices Nr. 1, 2, 4; `rnn_vad_weights.cc` im Quellarchiv | Sarevoc |
| 7 | `Profil erneuern.cmd` darf keine Kopfzeile bekommen (Test verlangt genau drei Zeilen); der Wächter nimmt die `.cmd`-Dateien aus | `EinrichtenSkriptTests.cs` Z. 134 bis 149 | bei den Kopfzeilen |
| 8 | Gerätename ist ab Werk der Name des Windows-Rechners; er kann einen Klarnamen enthalten und ist für Admins sichtbar | `ClientEinstellungen.cs` Z. 360 | im Datenschutzhinweis nennen oder neutralen Vorschlag wählen |
| 9 | Fristen fehlen für Logbuch (journald), `sperren.log`, Sicherungen und Vormerkungen | `PROJEKT.md` (Sperrliste), `BETRIEB.md`, `Mitgliederbuch.cs` | Sarevoc, ein künftiger Betreiber |
| 10 | Journaleinträge zu gelöschten Konten bleiben bis zu 90 Tage; Pegel und Sätze eines Kontos sind nicht per Befehl abfragbar | `Journal.cs`, Hilfe von `funk-admin` | im Hinweis nennen oder bauen |
| 11 | Datenschutzerklärung fertigstellen (Offener Punkt 9 im Funkprotokoll); Entwurf in diesem Teil | Protokoll 9 | Sarevoc |
| 12 | `LICENSE` und `NOTICE` auch in Setup und Paketen beilegen; eine Lizenzseite zum Zustimmen braucht die Apache-Lizenz nicht | `tools\setup-bauen.ps1`, `tools\paket-bauen.ps1` | offen |
| 13 | Vereinbarungen mit einem künftigen Betreiber (Betrieb) und mit jedem, der beiträgt (Beiträge); die Rechte bleiben bei Sarevoc | Anf. A41, Q23 | nach Version 1 |
| 14 | Private Angaben im Repository: **behoben** mit 0.13.1; Beispielnamen und Beispieladressen statt echter, der öffentliche Stand wird vor jeder Fassung geprüft | F13 | erledigt |
| 15 | Markenprüfung: TMview am 19.10.2026 ohne aktive Marke "TacRelay"; vor Version 1 in DPMAregister und EUIPO wiederholen | F12 | Sarevoc |
| 16 | Eigene THIRD-PARTY-NOTICES für das Serverpaket | `PROJEKT.md`, Nächste Schritte 2 | Bau |

## F15 Rechtsquellen

Alle aus dem Kenntnisstand des Verfassers, in diesem Auftrag nicht gegen die amtlichen Texte gelesen. Aufgeführt ist nur,
was dieser Teil oder ein anderer Teil des Handbuchs zitiert.

**Gesetze und Verordnungen**

- Urheberrechtsgesetz (UrhG): §§ 2, 3, 7, 8, 10, 13, 29, 31, 64, 66, 69a bis 69g, 87a
- Richtlinie 2009/24/EG über den Rechtsschutz von Computerprogrammen
- Datenschutz-Grundverordnung (EU) 2016/679: Art. 2, 4, 6, 13, 14, 15 bis 21, 26, 28, 30, 32 bis 35, 44 ff.;
  Erwägungsgrund 49
- Telekommunikation-Digitale-Dienste-Datenschutz-Gesetz (TDDDG): §§ 3, 25
- Telekommunikationsgesetz (TKG): § 5
- Markengesetz (MarkenG): §§ 5, 14, 23; Unionsmarkenverordnung (EU) 2017/1001, Art. 14
- Gesetz zum Schutz von Geschäftsgeheimnissen (GeschGehG): §§ 2, 3
- Bürgerliches Gesetzbuch (BGB): §§ 305 ff., 309 Nr. 7, 521, 523, 524
- Strafgesetzbuch (StGB): § 201
- KI-Verordnung (EU) 2024/1689: Art. 50

**Entscheidungen und Berichte**

- EuGH: Lindqvist, C-101/01 (2003); Infopaq, C-5/08 (2009); BSA, C-393/09 (2010); SAS Institute, C-406/10 (2012);
  Ryneš, C-212/13 (2014); Breyer, C-582/14 (2016); Zeugen Jehovas, C-25/17 (2018); Cofemel, C-683/17 (2019); Brompton,
  C-833/18 (2020)
- LG München I, GEMA gegen OpenAI, Urteil vom 11.11.2025
- Stadtgericht Prag, 2023, KI-Bild ohne menschlichen Urheber
- USA: Thaler v. Perlmutter, Court of Appeals for the D.C. Circuit, März 2025; US Copyright Office, "Copyright and
  Artificial Intelligence, Part 2: Copyrightability", Januar 2025

**Lizenzen** (Texte beim jeweiligen Lizenzgeber; Kennungen nach der SPDX-Liste)

- GNU General Public License 3.0 und GNU Affero General Public License 3.0 (Free Software Foundation)
- MIT License, Apache License 2.0 (Apache Software Foundation)
- European Union Public Licence 1.2 (Europäische Kommission)
- REUSE-Spezifikation (Free Software Foundation Europe)
