# JG TacRelay - Handbuch

| | |
| --- | --- |
| Fassung | 0.13.2 (Client 0.13.2 und Server 0.13.2; 0.13.1 war die erste öffentliche Fassung, Quelltext unter der Apache-Lizenz 2.0; belegt ist das Handbuch am Code-Stand `95aa78b` der Fassung 0.5.0, was seitdem neu ist, steht in 1.9) |
| Datum | 25.11.2026 |
| Maintainer und Rechteinhaber | Sarevoc |
| Lizenz des Programms | Apache-2.0 (1.10, F1) |

JG TacRelay ist ein Sprechfunk für Star-Citizen-Gruppen und andere Spielergruppen: ein Client für Windows, mit dem die Mitglieder funken, und
ein Server unter Linux, über den jeder Funkspruch läuft. Dieses Handbuch beschreibt beides, für alle, die damit zu tun
haben: vom Mitglied, das zum ersten Mal verbindet, bis zu dem, der den Server betreibt, prüft oder rechtlich einordnet.

## Für wen welcher Teil ist

| Du bist | Lies zuerst | Bei Bedarf |
| --- | --- | --- |
| Mitglied, das funken will | Kapitel 1, A1 bis A11 | E2 (Klang und Sprachaktivierung in Alltagssprache), Glossar |
| Einsatzleitung an einem Abend | A12 (Phasen: A12.5 und A12.6) | A7, B15 (Auftrag an der Konsole), C13 (Rechte) |
| Admin, der an einem Abend nur mitfunken will | A12.7 (Heute als Funker) | B10.7, C13 (Rechte) |
| Admin, der Mitglieder freischaltet und die Flotte pflegt | A17, A18 | B10 bis B15 (Phasen anlegen: B14.3), C10 und C11 (warum Freigabe und Admin-Nachweis so gebaut sind) |
| Betreiber des Servers, ohne IT-Ausbildung | A13 bis A22 | B3 oder B4, B18 bis B21 |
| IT, Prüfer, künftiger Betreiber | Kapitel 1, Teil B, Teil C | Teil D (Last), E3 (Technik des Klangs), F11 (Datenschutz) |
| Gruppe, die JG TacRelay mit eigenem Server betreiben will | B0.4 (Eigene Gruppe: eigener Server, eigene Schlüssel) | A13 bis A22, Teil B, Teil C, F1 |
| Eigentümer des Programms, Sarevoc | 1.9, 1.10, Offene Punkte | B8 (Serverprofil), B20 (Umzug), Teil F |

Die Teile bauen nicht aufeinander auf. Teil A erklärt, was zu tun ist, Teil B jede Einstellung und jeden Befehl, Teil C
warum etwas so gebaut ist, Teil D was es an Leitung und Rechenzeit kostet, Teil E wie der Klang entsteht und Teil F was
rechtlich gilt.

## Inhalt

| Teil | Abschnitte |
| --- | --- |
| Titel, Inhalt, Lesehilfe | |
| Kapitel 1: Was JG TacRelay ist | 1.1 Kurz gesagt, 1.2 Wofür es gebaut ist, 1.3 Die Teile, 1.4 Wie es funktioniert, 1.5 Begriffe, 1.6 Was JG TacRelay kann, 1.7 Was JG TacRelay bewusst nicht tut, 1.8 Was noch fehlt, 1.9 Stand und Fassungen, 1.10 Rechte und Lizenz |
| Teil A: Anleitung für Spieler und Betreiber | Für alle: A1 Den Client installieren, A2 Zum ersten Mal verbinden, A3 Start: Sprechen und Hören, A4 Sprechtaste und Joystick, A5 Sprachaktivierung, einfach erklärt, A6 Das Funkfeld, A7 Netze und Rollen, A8 Wer ist da, A9 Ich: Namen, Rollen und Zeichen, A10 Discord gleichzeitig nutzen, A11 Wenn als Spieler etwas nicht klappt. Für die Einsatzleitung: A12 Die Funkfeld-Zuteilung (bis 0.11.1 das Brett). Für den Betreiber: A13 Den Server betreiben: was er tut, A14 Was du brauchst, A15 Einrichten mit einem Befehl, A16 Das Profil vom Eigentümer, A17 Der erste Admin, A18 Nach dem Einrichten, A19 Updates, A20 Sicherung, A21 Wenn am Server etwas nicht klappt, A22 Ausblick: der Umzug auf einen gemieteten Server |
| Teil B: Handbuch für Admins und IT | B0 Lesehilfe (darin B0.4 Eigene Gruppe: eigener Server, eigene Schlüssel), B1 Architektur, B2 Voraussetzungen, B3 Einrichtung im Proxmox-LXC, B4 Einrichtung auf einem gemieteten Server, B5 funk-einrichten.sh im Detail, B6 server.json, B7 systemd-Units, B8 Serverprofil und Pins, B9 funk-admin: Referenz, B10 Admins und Eigentümer, B11 Mitglieder, Anträge und Geräte, B12 Netze, B13 Rollen (Funkrollen), B14 Flotte, B15 Auftrag (Einsatzleitung für einen Abend), B16 Sperrliste und fail2ban, B17 Härtung, B18 Sicherung und Zurückspielen, B19 Updates, B20 Umzug, B21 Fehlersuche, B22 Rechte, Lizenz und Datenschutz im Betrieb, B23 Belege |
| Teil C: Sicherheitskonzept | C1 Auf einen Blick, C2 Schutzziele, C3 Bedrohungsmodell, C4 Grundsätze, C5 Schlüssel und Geheimnisse, C6 Geräteschlüssel, C7 Serverprofil, Pins und Profilschlüssel, C8 Steuerkanal (TLS), C9 Sprachkanal, C10 Anmeldung: Antrag und Freigabe, C11 Admin-Nachweis, C12 Eigentümer und Konsole, C13 Rechte, Stufen und Auftrag, C14 Sperrliste und Grenzen, C15 Datensparsamkeit, C16 Härtung des Hosts, C17 Was bewusst nicht abgedeckt ist, C18 Prüfstand und offene Punkte, C19 Im Ernstfall |
| Teil D: Was 20 oder 40 Leute brauchen | D1 Wie die Angaben gekennzeichnet sind, D2 Das Ergebnis vorab, D3 Bausteine aus dem Code, D4 Wie der Server weiterleitet, D5 Annahmen für das Rechenbeispiel, D6 Formeln, D7 Server: Upload und Download, D8 Je Client, D9 Grenzfälle, D10 Vergleich mit dem Heimanschluss, D11 Vergleich mit einem gemieteten Server, D12 Rechenzeit und Speicher, D13 Latenz, D14 Selbst nachmessen, D15 Was noch nicht gemessen ist, D16 Quellen |
| Teil E: Unser Klang und das Schwellsystem | E1 Stand und Belege. E2 Für alle: E2.1 Was JG TacRelay mit deiner Stimme macht, E2.2 Headset oder Lautsprecher, E2.3 Sprachaktivierung oder Sprechtaste, E2.4 Die Schwelle in drei Stellungen, E2.5 Was an unserer Schwelle besonders ist, E2.6 Was du hörst, E2.7 Die Sprechtaste, E2.8 So stellst du die Sprachaktivierung ein, E2.9 Hörprobe und Mithören, E2.10 Wenn etwas nicht passt, E2.11 Klangfarbe (seit 0.9.3). E3 Technisch: E3.1 Die Kette im Überblick bis E3.17 Quellen, darunter E3.6 Das Sprach-Gate, E3.7 Die Führung, E3.11 Hörprobe und Mithören, E3.13 Messwerte aus den Tests, E3.14 Messungen selbst wiederholen |
| Teil F: Lizenz und Recht | F1 Lizenz und Mitwirkende, F2 Grundlagen dieses Teils, F3 Wer Rechteinhaber ist, F4 Urheberrecht an Software, F5 Code mit KI-Unterstützung, F6 Neubau ohne SRS-Code, F7 Fremdbestandteile, F8 Lizenz: die Wege und die Wahl, F9 Kopfzeilen in den Quelldateien, F10 Mitarbeit von Claude, F11 Datenschutz (DSGVO), darin F11.9 Mitschnitte, Fernmeldegeheimnis und Genehmigung, F12 Marken, F13 Repository auf GitHub, F14 Befunde und offene Punkte, F15 Rechtsquellen |
| Glossar | alle Begriffe von A bis Z, mit Verweis auf den Abschnitt |
| Offene Punkte für Sarevoc | Entscheidungen, Angaben ohne Beleg, Befunde, Hinweise der Redaktion |

## Lesehilfe

### Schreibweisen

- **Fett** steht, was du auf dem Bildschirm findest: Knöpfe, Bereiche, Überschriften.
- `So` steht, was du tippst oder was ein Programm genau so ausgibt.
- `<so>` ist ein Platzhalter: Du setzt deinen eigenen Wert ein, ohne die spitzen Klammern.
- `XXXX-XXXX-XXXX-XXXX` steht für eine Kennung (Gerätekennung, Admin-Kennung oder Kennung eines Pins).
- **Das siehst du** beschreibt, woran du erkennst, dass ein Schritt geklappt hat.
- Weitere Platzhalter für den Server (Konto-ID, Containernummer, Beispielnamen und Beispieladressen) erklärt B0.2.
- Im PDF bricht eine Befehlszeile, die breiter ist als die Seite, um. Die Fortsetzung ist eingerückt und links mit einem
  kleinen Haken markiert. Zeilen mit Haken gehören zur Zeile darüber: Gib sie zusammen als eine Zeile ein, ohne
  Umbruch.
- In Tabellen bricht ein langer Pfad oder Name nur hinter `/`, einem Backslash, `.`, `-` oder `_` um; auch dann gehört
  alles zusammen und wird ohne Umbruch eingegeben.

### Sarevoc und der Eigentümer

Sarevoc ist der Eigentümer des Programms, der es pflegt und die Rechte daran hat (Anf. A41). Unter diesem Namen steht er
als Maintainer im Handbuch und in den Quelldateien; das hat er so festgelegt (Anf. A59, mehr in F3.3). In den Meldungen
der Programme heißt es seit 0.5.0 "der Eigentümer" (Anf. A65), etwa `unterschrieben vom Eigentümer` oder `Schick dem
Eigentümer die Zeile oben`. Gemeint ist, wer den Profilschlüssel hält und die Serverprofile unterschreibt: für Sarevocs
eigenen Server Sarevoc, für den Server einer anderen Gruppe deren Verwalter (B0.4). Solche Meldungen zitiert das Handbuch
wörtlich nach dem Stand `95aa78b`, damit du sie am Bildschirm wiedererkennst. Bis 0.4.1 stand dort ein Rufname; wer noch
einen Client oder ein `funk-admin` der Fassung 0.4.x vor sich hat, sieht diese ältere Fassung der Meldungen.

Nicht zu verwechseln: Der "Eigentümer des Servers" ist ein Konto am Server (Glossar), nicht der Eigentümer des Programms.

### Verweise

| Schreibweise | Bedeutung |
| --- | --- |
| 1.9 | Abschnitt 1.9 in Kapitel 1 |
| A12, B9.5, C11.2, D5, E2.8, F8.9 | Abschnitt in Teil A bis F |
| Anf. A41 | Anforderung A41 von Sarevoc. Die Anforderungen A1 bis A65 stehen mit Sarevocs Entscheidungen in `docs\entwurf\ENTSCHEIDUNGEN.md` (Anhang A). "Anf." unterscheidet sie von den Abschnitten A1 bis A22. |
| Protokoll 3.1 | Abschnitt 3.1 der Spezifikation `docs/FUNKPROTOKOLL-v1.md`; dort stehen auch die Ziele (Z), Bedrohungen (T), Prüfungen (P-50) und die Offenen Punkte |
| Entwurfsfrage E5 | Frage E5 aus dem ersten Entwurf, heute in `docs\entwurf\ENTSCHEIDUNGEN.md` (Anhang B) |
| `BETRIEB.md`, `UMZUG.md` und andere | Anleitungen unter `docs\` im Repository; sie liegen auch dem Serverpaket bei |

### Wie belegt ist, was hier steht

Jede Zahl und jeder Befehl stammt aus dem Code des Stands `95aa78b` (Fassung 0.5.0, 02.10.2026) oder aus gemessener Doku
im Repository. Wo eine Zählung oder Messung an einem älteren Stand gemacht ist, steht dieser Stand dabei (`8c73864` für
die Fassung 0.4.1, `d45e33f` für die Fassung 0.4.0); sie wurde dann nicht wiederholt. Wo etwas nicht aus Code oder Messung
stammt, steht ein Kennzeichen dabei:

| Kennzeichen | Bedeutung |
| --- | --- |
| Code | steht so im Quelltext, mit Datei und Stelle |
| gemessen | mit den gebauten Programmen ausprobiert, mit Datum und Bericht; meist auf dem Entwicklungsrechner unter Windows |
| gerechnet | aus Code-Werten und Messungen ermittelt, nicht gemessen |
| am Code geprüft | aus dem Quelltext abgelesen, nicht mit dem laufenden Programm ausprobiert |
| unter Linux nicht gemessen | stammt aus dem Code oder aus einer Messung unter Windows; unter Linux noch nicht ausprobiert |
| Annahme, Schätzung | gesetzt, damit sich rechnen oder planen lässt; durch eigene Zahlen ersetzen |
| Ziel | Vorgabe der Spezifikation, kein Messwert (nur in Teil D) |

Die `funk-admin`-Befehle des Handbuchs sind am Stand `95aa78b` mit der Optionstabelle des Werkzeugs
(`src\Funk.Admin\Befehlsoptionen.cs`) abgeglichen: am Code geprüft. Der Lauf gegen die echte Optionsprüfung des gebauten
Werkzeugs stammt vom 01.10.2026 (Fassung 0.4.1). Was unbelegt ist oder Sarevoc entscheiden muss, sammeln die Offenen
Punkte am Ende.

### Stand

Der Stand für das ganze Handbuch steht in 1.9: Das Handbuch gehört zur Fassung 0.13.2; 0.13.1 war die erste öffentliche
(25.11.2026). Belegt ist es am Code-Stand `95aa78b` der Fassung 0.5.0 (02.10.2026); was seitdem dazukam, steht in 1.9 in
den Zeilen "Neu in ...". Mit 0.13.0 werden Server und Clients neu eingerichtet, ein Stand bis 0.12.0 wird nicht
übernommen (1.9, "Neu in 0.13.0"). JG TacRelay ist noch nicht fertig: Ein Abend mit vielen Leuten und die Anzeigen über
dem laufenden Spiel sind noch nicht erprobt (1.8). Ein Umzug auf einen gemieteten Server ist vorbereitet (B20).

Die Wege im Client (wo du was einstellst) beschreibt Teil A seit dem 12.10.2026 nach der Fassung 0.10.0 (Einstellungen
einfach: Start mit Sprechen und Hören, Statuszeile Mikrofon, Einstellungen mit zugeklappten Bereichen; gebaut und getestet
auf dem Zweig `modul/einstellungen`, noch nicht gemergt), nach 0.11.1 (unter Senden die Schalter und die Funkschwelle
wieder offen, die Klappbereiche heißen **Erweitert:**) und seit dem 13.10.2026 nach 0.11.2 (Funkfeld-Zuteilung mit Baum statt
Brett, Meldezeile nur für die Leitung, Start nur mit dem Audio, flache Knöpfe). Was sich damit geändert hat, steht in
1.9, Zeilen "Neu in 0.10.0", "Neu in 0.11.1" und "Neu in 0.11.2"; wo ein Satz den früheren Weg nennt, steht "bis 0.9.3"
oder "bis 0.11.1" dabei. Am Server ändern 0.10.0 und 0.11.1 nichts; 0.11.2 ändert dort, wer in der Präsenz steht
(B0.1).

### Kein Rechtsrat

Teil F bereitet Entscheidungen vor und ist keine Rechtsberatung. Für alles, was verbindlich sein soll, gehört ein
Fachanwalt für IT-Recht dazu.
