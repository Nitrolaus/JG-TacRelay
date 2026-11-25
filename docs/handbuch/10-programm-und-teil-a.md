## 1 Was JG TacRelay ist

Stand 02.10.2026. Beschrieben ist die Fassung 0.5.0: Client und Server 0.5.0, so wie sie im Code am Stand `95aa78b`
stehen (Abschnitt 1.9). Wo etwas noch nicht gebaut oder noch nicht am echten Gerät ausprobiert ist, steht das dabei. Wie
das Handbuch aufgebaut ist und wie es zitiert, steht im Titelteil vor diesem Kapitel.

### 1.1 Kurz gesagt

JG TacRelay ist ein Sprechfunk für Star-Citizen-Gruppen und andere Spielergruppen. Er besteht aus zwei Teilen:

- dem **Client**, einem Programm für Windows, mit dem die Mitglieder funken, und
- dem **Server**, einem Dienst auf einem Linux-Rechner, über den jeder Funkspruch läuft und der weiß, wer welche Netze
  hören und in welche er sprechen darf.

Beides ist eigener Code. Aus SRS, aus dem früheren Funkprogramm der Gruppe (einem Ableger von SRS) oder aus Mumble ist kein
Code übernommen.

Funken heißt hier: Du hörst mehrere **Netze** zugleich, etwa die Crew deines Schiffs, das Kommando der Flotte und das
Sondernetz Global, und sprichst gezielt in eines davon. Welche Netze du hast, ergibt sich aus der **Einheit** (etwa einem
Schiff) und der **Rolle** (etwa Pilot), die du vor dem Einsatz wählst oder die dir die Einsatzleitung zuteilt.

### 1.2 Wofür es gebaut ist

Die Anforderungen stammen aus Gesprächen mit dem Eigentümer des Programms, Sarevoc, und stehen als Anforderungen A1 bis
A65 in `docs\entwurf\ENTSCHEIDUNGEN.md`; das Handbuch zitiert sie als "Anf. A41". Der Rahmen:

- Nutzer sind Gruppen in Star Citizen und andere Spielergruppen. Gerechnet wird mit 5 bis 40 Leuten gleichzeitig auf dem Server, verteilt
  auf mehrere Netze.
- Einsätze sind meist Schiffsschlachten. Vorher wird festgelegt, wer wo sitzt; im Einsatz stellt die Einsatzleitung
  live um.
- Nicht alle tragen ein Headset. Eine Echounterdrückung ist deshalb Pflicht, damit die anderen sich nicht selbst aus
  deinen Lautsprechern hören.
- Bester Klang ohne Funkverzerrung und ohne Stimmfilter. Erlaubt ist eine Rauschunterdrückung, dazu ein kleiner,
  selbst erzeugter Funk-Klick, den nur du hörst: beim Drücken und Loslassen deiner Sprechtaste und, wenn du es willst,
  wenn andere zu sprechen beginnen und aufhören. Seit 0.5.0 stellst du ihn selbst ein (A5.5).

JG TacRelay entstand als Ersatz für das frühere Funkprogramm der Gruppe. Was dort schlecht war und was JG TacRelay anders
macht:

| Beim früheren Funkprogramm | Bei JG TacRelay |
| --- | --- |
| Passwörter im Klartext | keine Passwörter; jeder PC weist sich mit einem eigenen Schlüssel aus, wo möglich im TPM |
| ein öffentlich lesbarer Zugangsschlüssel für alle | kein gemeinsames Geheimnis; jedes Gerät gibt ein Admin einzeln frei |
| Updates ohne Unterschrift | das Serverprofil gilt nur mit Unterschrift des Eigentümers; der Server lässt zu alte Clients nicht herein |
| offene Ports auf allen Netzwerkkarten | genau zwei Ports, einer für TCP und einer für UDP, nur auf der eingestellten Adresse |
| Telemetrie | keine; Messwerte bleiben auf dem eigenen Server |
| Ton mit 16 kHz, 40-ms-Paketen und festem Puffer (Knistern) | 48 kHz, 20-ms-Pakete, ein Puffer, der sich der Leitung anpasst, weicher Begrenzer statt Übersteuerung |

Ein Port ist eine Nummer, unter der ein Programm auf einem Rechner von außen erreichbar ist. TCP und UDP sind zwei Arten,
Daten über das Netz zu schicken; was sie unterscheidet, steht in 1.4.

Ein ehrlicher Zusatz zu den Updates: Neue Fassungen des Clients kommen heute als neues Setup. Das Setup ist noch nicht
digital signiert; eine Codesignatur ist geplant. Unterschrieben ist heute das Serverprofil (Abschnitt 1.5), und darauf
beruht, dass dein Client nur mit dem echten Server spricht.

**Woraus beide gebaut sind.** Das frühere Funkprogramm ist ein Ableger (Fork) von SRS, dem Funkprogramm DCS-SimpleRadio-Standalone: ein
Programm in C# auf .NET 9 mit einer Oberfläche in WPF, dazu fertige Bibliotheken in C für Opus, SpeexDSP, MP3 und die
Spracherkennung (`opus.dll`, `speexdsp.dll`, `libmp3lame.dll`, `WebRtcVad.dll`). Python steckt im ausgelieferten Programm
nicht: Weder unter den 90 Dateien des Programmordners noch unter den 228 Dateien, die in den drei gebündelten Programmen
seiner Fassung 2.3.3.8 stecken (Programm, Installer, AutoUpdater), ist eine Python-Datei oder eine Python-Laufzeit (nach
Dateinamen geprüft am 01.10.2026, an der Kopie für die Analyse). Ob beim Bauen dort Python als Hilfsmittel
im Spiel war, lässt sich daran nicht ablesen. JG TacRelay ist ebenfalls C# mit WPF, aber auf .NET 10, und bringt eigene
Bauten seiner Bibliotheken in C und C++ mit (Opus, RNNoise, der Resampler aus SpeexDSP, die Audioverarbeitung von WebRTC);
aus dem früheren Funkprogramm ist kein Code übernommen (F6).

### 1.3 Die Teile

| Teil | Läuft auf | Wofür |
| --- | --- | --- |
| JG TacRelay (Client) | Windows 10 oder 11, 64 Bit | funken: Hauptfenster, Funkfeld und ein Symbol im Infobereich der Taskleiste |
| Admin-Setup | Windows, nur für Admins | derselbe Client, dazu der Verwaltungsteil (`Funk.Verwaltung.dll`) |
| JG TacRelay Probefunk | Windows, kommt mit jedem Setup | ein Testserver mit Echo-Funker nur auf deinem PC, zum Ausprobieren |
| `funk-server` | Linux | der Server, als Dienst |
| `funk-admin` | Linux, auch auf dem Stick des Eigentümers | Kommandozeile zum Einrichten und Pflegen |
| `funk-echo` | Linux | der Echo-Funker: spricht jeden Satz nach, zum Prüfen |
| `funk-einrichten.sh` | Linux | richtet den Server mit einem Befehl ein |

Ausgeliefert wird in diesen Paketen (`<zeit>` ist ein Zeitstempel, `<fassung>` die Fassung, etwa 0.5.0):

| Paket | Für wen |
| --- | --- |
| `<zeit>_JG-TacRelay-Setup-<fassung>.exe` | alle Mitglieder |
| `<zeit>_JG-TacRelay-Admin-Setup-<fassung>.exe` | Admins |
| `<zeit>_JG-TacRelay-Server-<fassung>.zip` mit einer Datei `.sha256` daneben | wer den Server betreibt |

Das normale Setup enthält keinen Verwaltungscode. Die beiden Setups sind bis auf die Datei
`verwaltung\Funk.Verwaltung.dll` gleich.

### 1.4 Wie es funktioniert

```
Windows-PC (Client)                              Linux-Rechner (Server)
+------------------------------+                 +--------------------------------+
| Mikrofon                     |   TCP mit TLS   | funk-server                    |
|  -> Echounterdrückung        |<--------------->|  Steuerkanal: Anmeldung,       |
|  -> Rauschunterdrückung      |                 |  Konten, Rechte, Netze, Plätze |
|  -> Sprachaktivierung oder   |                 |                                |
|     Sprechtaste              |   UDP mit       |  Sprache: prüfen, ob du senden |
|  -> Opus (48 kHz, 20 ms)     |   AES-256-GCM   |  darfst, Absender und Netz     |
|                              |<--------------->|  stempeln, an alle Hörer       |
| Puffer je Sprecher           |                 |  weiterreichen                 |
|  -> Mischer, Pegel, Ohr      |                 |                                |
|  -> Kopfhörer, Lautsprecher  |                 | funk-admin (nur am Server)     |
+------------------------------+                 +--------------------------------+
```

In Worten:

- Dein Client baut zum Server zwei Wege auf. Der **Steuerkanal** läuft über TCP und ist mit TLS verschlüsselt. TCP ist
  die Art, Daten zu schicken, bei der jedes Stück bestätigt und notfalls wiederholt wird; so kommt alles vollständig und
  in der richtigen Reihenfolge an. TLS ist die Verschlüsselung darüber, dieselbe wie bei einer sicheren Webseite im
  Browser. Über den Steuerkanal meldest du dich an, und über ihn erfährst du, welche Netze du hast. Die **Sprache** läuft
  über UDP, ebenfalls verschlüsselt (AES-256-GCM). UDP ist die schnelle Art, Pakete zu schicken, ohne auf Bestätigungen zu
  warten; für Sprache ist das richtig, denn ein verspätetes Stück Ton nützt nichts mehr.
- Kommt UDP nicht durch, etwa weil ein Netz es sperrt, läuft die Sprache im Steuerkanal mit. Der Client zeigt dann
  `Verbunden (Tunnel)`; das geht, hat aber etwas mehr Verzögerung.
- Der Server entschlüsselt, prüft und verschlüsselt jedes Paket neu, aber er dekodiert, mischt und verändert den Ton nicht.
  Was dein Mikrofon liefert, kommt in derselben Qualität bei den anderen an: Opus mit 48 kHz, 20-ms-Paketen und
  48 kbit/s.
- Der Server stempelt jedes Paket mit Absender und Netz. Deshalb kann niemand vortäuschen, er spräche als jemand anderes
  oder in einem Netz, in dem er nicht ist. Hören darf ein Netz nur, wer Mitglied ist; das erzwingt der Server.
- Beim Hören sammelt dein Client die Pakete je Sprecher in einem Puffer, der kleine Schwankungen der Leitung ausgleicht,
  mischt alle Sprecher mit deinem Pegel je Person und deiner Lautstärke je Netz und gibt sie auf dem gewählten Ohr aus.

### 1.5 Begriffe

Diese Wörter kommen im ganzen Handbuch vor. Alle Begriffe, auch die technischen, erklärt das Glossar am Ende des
Handbuchs.

| Begriff | Bedeutung |
| --- | --- |
| Netz | ein Funkkanal, etwa "Kommando" oder "Crew". Im früheren Funkprogramm hieß das "Kanal". |
| Global | ein Sondernetz für Ansagen der Führung. Alle hören es, abschalten lässt es sich nicht. Senden darf darin nur, wer das Recht dazu hat, und ab Werk nur mit der Sprechtaste. |
| Feld | ein Platz im Funkfeld, auf dem ein Netz liegt. Es gibt bis zu 16 Felder je Konto; das Funkfeld zeigt 5 oder 10 davon. |
| Funkfeld | das kleine Fenster mit den Feldern, das immer im Vordergrund bleibt, auch über Star Citizen. Seit 0.5.0 lässt es sich in den Infobereich der Taskleiste ablegen und zurückholen (A6.6). |
| Hören | ob du ein Netz gerade hörst. Je Feld abschaltbar (außer Global). |
| Sendenetz | das Netz, in das du gerade sprichst; das Funkfeld zeigt es grün mit "SENDET" |
| VA / Taste | Sprachaktivierung (VA, vom Englischen "voice activation": senden, sobald du sprichst) oder Sprechtaste (senden, solange du eine Taste hältst). Stellst du je Netz ein. Für neue Netze ist VA seit 0.5.0 die Voreinstellung (A5.2). |
| Einheit | ein Schiff, ein Trupp (Infanterie) oder eine Orga-Einheit. Eine Einheit bringt eigene Netze mit, etwa Crew und Brücke. |
| Rolle | deine Aufgabe auf einer Einheit, etwa Pilot oder Gunner. Die Rolle bestimmt, welche Netze in deinen Feldern liegen. |
| Platz | Einheit und Rolle eines Mitglieds, etwa "Schiff 2, Pilot". Je Einheit und Rolle gibt es eine feste Zahl an Plätzen (Kapazität). |
| Einsteigen | Einheit und Rolle selbst wählen; danach stellt sich dein Funkfeld um. Seit 0.11.2 ohne eigene Oberfläche: Die Karte **Einsteigen** auf Start ist weg, eine Rolle wählst du im Funkfeld (A7.1). |
| Flotte | wie Einheiten, Rollen, Plätze und Netze aussehen; das pflegt ein Admin |
| Stufe | Funker, Führung oder Admin. Legt fest, was ein Konto ab Werk darf. An der Konsole heißen die Stufen FUNKER, RAIDLEITER und ADMIN. |
| Führung | die Stufe für Ansagen: darf in Global senden, Durchsagen machen und in mehrere Netze zugleich sprechen. Teilt aber nicht ein. |
| Einsatzleitung | wer an einem Abend andere einteilt: ein Admin oder jemand mit einem Auftrag für den Abend. Keine Stufe; die Stufe RAIDLEITER ist die Führung und teilt nicht ein. |
| Auftrag | das befristete Recht, an einem Abend einzuteilen. Ohne Angabe endet er um 04:00 Uhr, höchstens nach 24 Stunden. |
| Phase (Einsatzphase) | ein Netzbild für die ganze Flotte, etwa "2 Entern": Der Admin legt vorher fest, welche Netze eine Rolle in dieser Phase in ihren Feldern hat, und die Einsatzleitung schaltet im Einsatz um, seit 0.11.2 mit zwei Tastendrücken (bis 0.11.1 auch am Brett). Plätze und Rollen ändern sich dabei nicht. Phase 1 ist die Grundstellung. Neu in 0.5.0 (A12.5, A12.6). |
| Heute als Funker | ein Admin spielt einen Abend wie ein gewöhnlicher Funker, damit die Einsatzleitung ihn einteilen kann. Neu in 0.5.0 (A12.7). |
| Melde-Taste | eine zweite Sprechtaste: Solange du sie hältst, sprichst du ins Meldenetz (ab Werk Kommando), und dein Sendenetz bleibt, wie es ist (A4.4) |
| Meldezeile | ein kleines Fenster an einem festen Platz des Bildschirms (seit 0.11.1, ab Werk oben Mitte auf dem Monitor von Star Citizen; bis 0.11.0 am Funkfeld), das kurze Meldungen zeigt, auch wenn das Funkfeld im Infobereich liegt; seit 0.11.0 eine Meldung in einer Zeile, fest 640 Einheiten breit und mit dem Funkfeld skaliert (bis 0.10.0 höchstens drei); seit 0.11.2 nur noch, was die Leitung tut, dein Melden und die Probe aus den Einstellungen (A6.7) |
| Sprecheranzeige | seit 0.11.9 ein kleines Fenster über dem Spiel, das zeigt, wen du gerade hörst: den Namen fett, dahinter den Kanal mit Ch., weiße Schrift mit Schatten, ohne Fläche; ab Werk oben links, eine Zeile, nur solange jemand spricht (A6.8) |
| Meldungen | seit 0.12.0 Grafiken, die die Leitung an alle schickt, die online sind: Befehle (8 s groß), Zustände (klein, bis aufgehoben), eine Ampel, ein Countdown und die Bereit-Abfrage. Beim Funker stehen sie in einem eigenen Fenster über dem Spiel, ab Werk oben in der Mitte (A6.9); senden dürfen nur, die ein Admin dafür ernannt hat (A12.8) |
| Infobereich | der Bereich der Windows-Taskleiste rechts neben der Uhr, in dem kleine Symbole laufender Programme sitzen. Windows legt neue Symbole oft hinter einen kleinen Pfeil (^). Ein Funkfeld, das du "ablegst", liegt dort als Symbol. Der Ordner der Einstellungen (seit 0.10.0 unter **Einstellungen**, **Fehlersuche**, **Erweitert**; bis 0.9.3 die Karte **Ablage**) hat damit nichts zu tun: Er zeigt nur, wo deine Einstellungsdatei liegt. |
| Funk-Klick | das kurze Geräusch wie an einem Funkgerät, das JG TacRelay selbst erzeugt und nur du hörst; seit 0.5.0 einstellbar (A5.5) |
| Admin | darf alles, dazu Mitglieder freischalten und die Flotte pflegen. Verwalten geht nur mit eigenem Admin-Schlüssel und einem Code vom Telefon. |
| Eigentümer des Servers | genau ein Admin-Konto auf einem Server, das im Client niemand anfassen kann. Wer einen neuen Server einrichtet, wird es mit seinem ersten Admin-Konto. |
| Eigentümer des Programms | Sarevoc. Er allein unterschreibt Serverprofile, mit einem Schlüssel, der nur auf seinem Stick liegt. |
| Antrag | was dein Client beim ersten Verbinden stellt: "dieses Gerät gehört zu diesen Namen". Ein Admin prüft und gibt frei. |
| Gerätekennung | die Kennung deines PCs in der Form `XXXX-XXXX-XXXX-XXXX`. Kein Geheimnis; der Admin vergleicht sie, bevor er freigibt. |
| Serverprofil | eine kleine Datei, die mit dem Setup kommt: Name und Port des Servers und die Fingerabdrücke seiner Schlüssel, unterschrieben vom Eigentümer des Programms |
| Pin | der Fingerabdruck eines Serverschlüssels. Dein Client spricht nur mit einem Server, dessen Schlüssel zu einem Pin im Profil passt. |
| Untergilde | gibt es seit 0.11.8 nicht mehr (Sarevoc: "Untergilden entfallen ganz, überall"); bis 0.11.7 eine feste Gruppe innerhalb der Org mit Kürzel |
| Rollenkatalog, Lieblingsrollen | was jemand im Spiel gut kann oder gern macht, etwa Medium Fighter Pilot. Das ist eine Angabe für die Einsatzleitung, nicht dasselbe wie die Rolle im Funkfeld (die Funkrolle). |
| Zeichen | kleine Symbole neben Namen: Lorbeerkranz für Admins, Stern für die Einsatzleitung; seit 0.11.7 dazu die Abzeichen (Ränge und Tätigkeiten) |
| Durchsage | Sprechen an mehrere oder alle Netze zugleich; das Recht dazu haben Führung, Einsatzleitung und Admins. Im Client gibt es dafür noch keine Bedienung (Abschnitt 1.8). |

### 1.6 Was JG TacRelay kann

**Funkfeld.** Ein kleines Fenster mit 5 oder 10 Feldern, wie man es vom früheren Funkprogramm kennt, wahlweise als Raster (2 Reihen zu
5) oder als schmale Liste für den Bildschirmrand. Es bleibt im Vordergrund, auch über Star Citizen im Modus "Randloses
Fenster", und lässt sich bis 85 % durchsichtig machen. Jedes Feld zeigt den Netznamen, ob du hörst oder sendest (Farbe,
Symbol und Text), wer gerade spricht, einen Pegel und die Lautstärke des Netzes. Seit 0.5.0 lässt es sich mit einer
Taste, einem Joystickknopf oder einem Klick auf sein Symbol in den Infobereich der Taskleiste legen und wieder holen,
ohne dass Star Citizen den Fokus verliert (A6.6); eine Meldezeile an einem festen Platz des Bildschirms zeigt kurze
Meldungen (A6.7). Seit 0.11.9 steht über dem Spiel, wer gerade spricht und in welchem Kanal: die **Sprecheranzeige**,
nur mit den Leuten, die du wirklich hörst (A6.8). Seit 0.12.0 schickt die Leitung **Meldungen** als Grafik an alle, die
online sind: Befehle wie **Sammeln** oder **Stopp**, Zustände wie **Pause**, eine Ampel, einen Countdown und die Frage,
wer bereit ist; sie stehen oben in der Mitte über dem Spiel, mit kurzem Ton bei Befehlen (A6.9, A12.8).

**Start und Einstellungen (seit 0.10.0).** Was du zum Funken brauchst, steht auf einer Seite, **Start**: Verbinden,
Mikrofon und Sprechtaste, Senden mit Stimme, Ausgabe, eine **Funk-Lautstärke** für den ganzen Funk, Headset oder
Lautsprecher und eine Hörprobe, die auch verbunden geht. Beim ersten Start fragt Start nach Headset oder Lautsprecher und
wartet auf deine Sprechtaste, mit **Später**. Seit 0.11.2 steht auf Start nur das Audio: unten **Senden** mit dem
Pegelbalken und seinen Griffen, **Einmessen** und den Schaltern der Klangmodule. Unten zeigt die Statuszeile **Mikrofon**
auf jeder Seite, ob du sendest. Alles Übrige steht unter **Einstellungen** in Abschnitten mit dem Kasten **Läuft von
selbst** und dem zugeklappten Bereich **Erweitert:**, den du mit **Zurück auf Werkseinstellung** zurückstellst; unter
**Senden** stehen die Schalter der Klangmodule und die Funkschwelle seit 0.11.1 offen, ohne Aufklappen (A2.5, A3).

**Netze und Rollen.** Eine Rolle wählst du oben links im Funkfeld; sie legt ihre Netze in deine Felder. In welche Kanäle
du an einem Abend gehörst, teilt die Einsatzleitung ein. Bis 0.11.1 gab es dazu die Karte **Einsteigen** für Einheit und
Rolle in einem Schritt und **Wie letztes Mal**; seit 0.11.2 nicht mehr (A7.1). Je Netz stellst du ein: hören an oder
aus, links, beide Ohren oder rechts, Lautstärke, Sprachaktivierung oder Sprechtaste; neue Netze starten seit 0.5.0 auf
Sprachaktivierung (A5.2). Je Person stellst du einen eigenen Pegel ein, der in allen Netzen gilt und am Konto gespeichert
wird.

**Einsatzleitung und Funkfeld-Zuteilung.** Die Einsatzleitung teilt auf der **Funkfeld-Zuteilung** ein (bis 0.11.1 das **Brett**): links
alle Namen, rechts ein Baum wie ein Stammbaum mit allen Kanälen, ihren Ch. und den Leuten darin. Einen Namen auf einen
Ch. ziehen heißt: Die Person ist in diesem Kanal und kann sofort funken. Jede Ablage wirkt sofort. Einteilen darf ein
Admin oder wer von einem Admin einen Auftrag für den Abend bekommen hat. Wie die Flotte aussieht, legt ein Admin im
Verwaltungsteil fest; als Start gibt es die Vorlage **Star Citizen** mit zehn Kanälen und 14 Rollen. Dazu kann er bis zu vier
**Phasen** anlegen, etwa "Anflug", "Entern" und "Rückzug": Die Einsatzleitung schaltet die ganze Flotte mit zwei
Tastendrücken (auch am Joystick) von einer Phase zur nächsten, und alle, die es betrifft, bekommen die Netze der neuen
Phase auf einmal (A12.5, A12.6).

**Wer ist da.** Ein Knopf oben im Hauptfenster zeigt alle, die gerade verbunden sind, eine Zeile je Person, mit Suche.
Hinter jedem Namen stehen ein Mikrofon, mit dem die Einsatzleitung jemanden stumm schaltet, eine Statusgrafik (grün: in
Kanälen eingeteilt, gelb: in keinem Kanal) und eine Wippe, mit der du jede Person für dich leiser oder lauter stellst. Mit
der Einsatzleitung öffnet ein Klick auf einen Namen die Funkfeld-Zuteilung (A8).

**Zeichen und Abzeichen.** Kleine, selbst gezeichnete Symbole neben den Namen: ein goldener Lorbeerkranz für Admins und ein
kleiner Stern für die Einsatzleitung mit laufendem Auftrag. Seit 0.11.7 kommen die **Abzeichen** dazu: Ränge in drei
Laufbahnen (Marine, Navy, Orga) und Tätigkeiten wie Gunner M oder Sanitäter, die ein Admin vergibt (A9.2). Ein Abzeichen
zu haben ist eine Sache, es zu zeigen eine eigene Entscheidung: Jeder wählt selbst, welches vorn steht, und blendet jedes
einzeln aus (A9.1). Die zwei silbernen Winkel der Führung (ein Platzhalter) zeichnet der Client seit 0.11.7 nicht mehr.

**Heute als Funker.** Ein Admin, der an einem Abend nur mitfunken will, während jemand anderes leitet, schaltet das für
sich ein (bis 04:00, ohne Code). Dann kann die Einsatzleitung ihn einteilen wie jeden anderen (A12.7).

**Tasten, Maus und Joystick.** Eine Sprechtaste, eine Melde-Taste für kurze Meldungen an die Leitung, das Weiterschalten
des Sendenetzes, das Ablegen des Funkfelds und, für die Einsatzleitung, das Schalten der Phasen lassen sich frei auf
Tasten, Maustasten und Joystickknöpfe legen (Kapitel A4). Der Funk-Klick ist einstellbar (Kapitel A5.5).

**Verwaltung.** Admins bekommen mit dem Admin-Setup einen Verwaltungsteil: Anträge freigeben, Mitglieder mit ihren
Rollen pflegen, Rollenkatalog und Abzeichen, Aufträge für die Einsatzleitung, die Flotte mit ihren Phasen. Verwalten geht nur in einer
Admin-Sitzung: mit einem Admin-Schlüssel, der im TPM des Admin-PCs liegt und sich nicht kopieren lässt, und einem Code
aus der Authenticator-App auf dem Telefon.

**Schutz.** Kein Passwort, kein Zugangscode, den man weitersagen könnte. Jedes neue Gerät braucht die Freigabe eines
Admins nach einem Vergleich der Gerätekennung. Der Server sperrt Adressen, die sich wie Scanner oder Angreifer
benehmen, selbst und befristet (Sperrliste); Adressen, von denen in den letzten 14 Tagen ein Mitglied verbunden war,
sperrt er dabei nicht.

### 1.7 Was JG TacRelay bewusst nicht tut

Diese Grenzen sind gewollt, nicht vergessen.

- **Kein Eingriff in Star Citizen.** Der Client öffnet den Spielprozess nicht, liest seinen Speicher nicht und legt
  nichts in den Spielordner. Tastatur und Maus liest er über die normale Schnittstelle von Windows mit, den Joystick so,
  wie ein Spiel ihn liest; das Spiel bekommt jede Taste trotzdem. Das ist wichtig wegen Easy Anti-Cheat.
- **Keine Passwörter, keine Anmeldung über Discord.** Dein Discord-Name dient nur dazu, dass der Admin dich erkennt.
- **Keine Weboberfläche.** Der Server hat keine Webseite, keinen Admin-Port und kein HTTP. Verwaltet wird im Client oder
  an der Kommandozeile auf dem Server selbst.
- **Kein Text-Chat, keine Dateiübertragung, keine Aufzeichnung, keine Positionsdaten.**
- **Keine Ende-zu-Ende-Verschlüsselung.** Die Sprache ist auf dem Weg verschlüsselt, der Server selbst hört sie aber im
  Klartext, so wie bei Mumble. Wer den Server betreibt, muss also vertrauenswürdig sein.
- **Kein Schutz gegen Schadsoftware auf deinem PC.** Ein TPM verhindert nur, dass jemand deinen Schlüssel wegkopiert,
  nicht, dass ein Schadprogramm auf deinem PC ihn benutzt.
- **Nur Windows.** Kein Client für Linux, Android oder den Browser, kein Mumble-Client, kein zweiter Server im Verbund.
- **Ein Gerät zur Zeit.** Je Konto gibt es höchstens eine Verbindung. Meldest du dich an einem zweiten PC an, fliegt der
  erste heraus.
- **Feste Obergrenzen:** 16 Felder je Konto, 128 Netze, 64 Einheiten, 64 Rollen, 500 Konten und 3 Geräte je Konto, dazu
  höchstens 4 Phasen über der Grundstellung. Außerdem ab Werk höchstens 100 gleichzeitige Sitzungen; diese Zahl stellt
  der Betreiber ein (`grenzen.maxSitzungen`, B6.2).
- **Keine Telemetrie.** Nichts geht an Dritte.

### 1.8 Was noch fehlt

Stand Client und Server 0.5.0. Diese Dinge sind vorgesehen, aber noch nicht gebaut:

- die Bedienung für Durchsagen im Client (der Server kann sie schon),
- einzelne Personen auf ein eigenes Ausgabegerät legen,
- auf der Funkfeld-Zuteilung: eine eigene Planung vor dem Abend (wer offline ist, lässt sich seit 0.11.2 schon vorab in Kanäle
  ziehen, A12.3), das Einfügen einer Tabelle, das Einlesen aus Raid-Helper, Reiter für Gruppen und Fraktionen,
- Phasen an der Konsole des Servers: Es gibt keinen `funk-admin`-Befehl dafür (am Code geprüft, Hilfe und
  Befehlstabelle); Phasen legt ein Admin im Verwaltungsteil an und schaltet die Einsatzleitung per Taste,
- ein eigener Klick für die Melde-Taste: Sie klickt nicht noch einmal, wenn die Sprechtaste schon gehalten ist,
- ein Discord-Bot; die Meldung an einen Admin-Kanal ist heute nur eine Zeile im Logbuch des Servers,
- automatische Updates und die Codesignatur des Setups.

Was 0.5.0 gebracht hat, steht in 1.9; die Vorgaben A60 bis A65 sind damit gebaut und stehen nicht mehr in dieser Liste.

Noch nicht am echten Gerät ausprobiert sind (im Code und im Bildlauf der Oberfläche geprüft, am echten PC nicht):

- die Funkfeld-Zuteilung von 0.11.2 mit dem Baum: das Ziehen der Namen auf einen Ch., im Baum und zurück nach links, das
  Aufklappen beim Verweilen und das Aufklappen eines Kanals, in den jemand kommt; geprüft im Modell und im Bildlauf, am
  echten PC noch nicht (gefahrlos ausprobieren lässt sich alles im Probefunk, Abschnitt A2.1). Das Ziehen der Karten auf
  dem Brett von 0.11.0 hat Sarevoc am 12.10.2026 am echten PC ausprobiert,
- die Meldezeile an ihrem festen Platz (0.11.1): ob sie oben in der Mitte des Monitors landet, auf dem Star Citizen läuft
  (die Erkennung des Spielfensters ist nur am Code geprüft, im Testlauf läuft kein Spiel), ob sie dort bleibt, wenn das
  Spiel kurz nicht zu sehen ist (0.11.2), das Raster und die Probe unter Einstellungen und die Wahl des Monitors mit zwei
  Bildschirmen (A6.7),
- die Sprecheranzeige (0.11.9) über Star Citizen: ob sie im Modus "Randloses Fenster" über dem Spiel steht, ohne ihm den
  Fokus zu nehmen, ob sie auf dem Monitor des Spiels landet, ob die Probezeile beim Schieben der Regler flüssig mitwandert,
  ob der Hinweis zum exklusiven Vollbild kommt, wie Schrift und
  Schatten über dem echten Spielbild wirken und ob sie die Bildrate merklich kostet (mit PresentMon messen: ohne Anzeige,
  verborgen, sichtbar); geprüft im Modell, im Bildlauf und mit einer Uhr der Tests, am echten PC noch nicht (A6.8),
- die Meldungen (0.12.0) über Star Citizen: ob sie im Modus "Randloses Fenster" oben in der Mitte über dem Spiel stehen, ohne
  ihm den Fokus zu nehmen, auf dem Monitor des Spiels, wie Bilder und Zeiten in der Sanduhr über dem echten Spielbild wirken,
  ob doppelt und dreifach auf echten Monitoren passen, ob der Ton bei Befehlen und in den letzten 10 s gut hörbar, aber nicht
  störend ist, die Taste **Bereit** an Tastatur und Joystick, während das Spiel vorn ist, wie gleich der Countdown bei
  mehreren Funkern läuft (Uhrabgleich über Ping, Ziel etwa 0,1 s) und ob die Anzeige die Bildrate kostet (PresentMon);
  geprüft im Modell, mit einer Uhr der Tests, im Bildlauf und im Zusammenspiel mit dem Server, am echten PC noch nicht
  (A6.9, A12.8),
- das Symbol im Infobereich der Taskleiste (wohin Windows 11 neue Symbole legt und wie du es anheftest, hängt von deinen
  Einstellungen ab), das Ablegen und Holen des Funkfelds per Taste und per Joystick, solange Star Citizen im Modus
  "Randloses Fenster" vorn ist (das Spiel darf dabei den Fokus nicht verlieren), und die Meldezeile über dem Spiel (A6.6,
  A6.7),
- die Melde-Taste an Kommando und die Phasentasten an einem HOTAS (Schubhebel und Steuerknüppel zusammen; A4.4, A12.6),
- der Nachlauf der Sprechtaste (seit 0.9.3) mit echtem Mikrofon und echter Taste: gemessen nur im Prüfstand, ohne den
  Aufnahmepuffer des Geräts; dafür steckt in den 100 ms ab Werk eine Reserve (A4.5),
- die Klangfarbe (seit 0.9.3) am Ohr und mit echten Mikrofonen: gemessen an Tönen und an der synthetischen Sprache des
  Klangkorpus; ob die Regler in der Praxis passend greifen, sagt erst ein Hörtest (A3.6),
- der Funk-Klick am Ohr: Die Voreinstellung (eigene Taste -12 dB, andere Sprecher -15 dB) ist gerechnet, nicht gehört; ob
  bei Lautsprechern etwas zurückkommt, ist offen (A5.5),
- die neue Oberfläche von 0.10.0 (Start mit Sprechen und Hören, der erste Start mit der Sprechtaste, die Statuszeile
  **Mikrofon**, die Einstellungen mit den Klappbereichen **Erweitert:**): geprüft im Bildlauf der Oberfläche, gezeichnet ohne
  Bildschirm, bei 100 und 175 %; am echten PC noch nicht. Ebenso noch nicht am echten PC: die Funk-Lautstärke am Ohr, das
  Warten auf die Sprechtaste beim ersten Start mit echter Taste und echtem Joystick, die Geräteliste beim Ein- und Ausstecken
  eines echten Headsets und **Mikro stumm** im Menü des Symbols (A3, A4.1, A6.6),
- die offene Ansicht **Senden** von 0.11.1 (Schalter und Funkschwelle ohne Aufklappen): geprüft im Bildlauf bei 640, 900
  und 1100 Breite und bei 175 %, am echten PC noch nicht; ebenso das Ziehen der Griffe dort mit der Maus (A3.1),
- Start von 0.11.2 mit **Senden** unten (Pegelbalken mit Griffen, Einmessen, Schalter) und die flachen Knöpfe: geprüft im
  Bildlauf, am echten PC noch nicht; in Klein 900 x 900 rollt Start nur beim ersten Start und mit einer Gerätewarnung (A3),
- der Verwaltungsteil nach 0.11.1 (Admin-Setup): Liste und Karte rutschen in schmalen Fenstern untereinander, der Knopf
  neben einer Überschrift (etwa **Mitglied vormerken**) darunter, Beschriftungen und Listenzeilen brechen um statt mit "..."
  zu enden; geprüft im Bildlauf für jede Seite bei 760, 960 und 1100 Breite und bei 175 %, am echten PC noch nicht (dort
  war in 0.11.0 bei 175 % der Knopf über der Überschrift und der Suchtext abgeschnitten),
- die Netzlast eines Phasenwechsels bei 40 Leuten: nicht gemessen (Teil D),
- das Einrichtungsskript `funk-einrichten.sh` auf echtem Linux (Abschnitt A15). Bevor es auf einen Server mit anderen
  Diensten kommt, läuft es einmal in einem Wegwerf-Container.

### 1.9 Stand und Fassungen

Diese Tabelle ist der Stand für das ganze Handbuch; die anderen Teile verweisen hierher.

| Was | Stand 02.10.2026 |
| --- | --- |
| Server | Fassung 0.5.0 ist gebaut und getestet (Code-Stand `95aa78b`), das Serverpaket 0.5.0 ist am 02.10.2026 ebenfalls gebaut und geprüft, aber nie ausgeführt. Das Update eines Servers auf 0.5.0 beschreibt `HEIMSERVER-PROXMOX.md`, Abschnitt 17. Ein Serverpaket 0.4.1 gab es nie: Server- und Protokollcode von 0.4.1 waren die von 0.4.0. |
| Client | Fassung 0.5.0 steht im Code (Stand `95aa78b`, am 02.10.2026 in `main`); das Setup 0.5.0 und das Admin-Setup 0.5.0 sind am 02.10.2026 gebaut und mit innounp geprüft, aber nie ausgeführt (`PROJEKT.md`, Standzeile vom 02.10.2026 zu den Paketen 0.5.0). Dieses Handbuch beschreibt den Code; was am echten PC anders aussieht, steht in 1.8. Davor gebaut sind die Setups 0.4.1 vom 01.10.2026: `20261001_1955_JG-TacRelay-Setup-0.4.1.exe` und `20261001_1954_JG-TacRelay-Admin-Setup-0.4.1.exe`, beide geprüft (`PROJEKT.md`, Stand 01.10.2026 20:00). Am Abend des 01.10.2026 hat Sarevoc sie bekommen; `PROJEKT.md` nennt die Übergabe noch offen. |
| Neu in 0.5.0 | Einsatzphasen (Anf. A60; A12.5, A12.6), "Heute als Funker" für Admins (Anf. A61; A12.7), das Funkfeld in der Ablage der Taskleiste mit Meldezeile und Melde-Taste (A4.4, A6.6, A6.7), der Joystick für alle Tasten-Funktionen (A4.2), Sprachaktivierung als Voreinstellung für neue Netze, Command und Global auf Taste (Anf. A62, A64; A5.2), der einstellbare Funk-Klick (Anf. A63; A5.5), Max Mustermann als Beispiel und "der Eigentümer" statt "Sarevoc" in den Meldungen der Programme (Anf. A65; A1.3, A15.3), die Maintainer-Kopfzeile in den Quelldateien (Anf. A58, A59; 1.10), die Prüfung aller Schieberegler (Pegel je Funker, Schiebeschalter 5/10) und die Hilfe von `funk-admin einrichten` mit sechs Fragen (A15.3). Die Testsuite lief am Stand `95aa78b` mit 1312 von 1312 Tests (`PROJEKT.md`, Standzeile 02.10.2026 01:00). |
| Neu in 0.4.1 | die Hörprobe als Aufnahme mit Wiedergabe und das Mithören beim Senden nur mit Headset (A3.4, E2.9), die Vorprüfung für Admin-PCs (A1.4, C11.2, Anf. A56). |
| Neu in 0.10.0 (Stand 12.10.2026, Zweig `modul/einstellungen`, gebaut und getestet, noch nicht gemergt) | Einstellungen einfach: Start mit **Sprechen** und **Hören**, die **Funk-Lautstärke**, eine Hörprobe auch verbunden, der erste Start mit Sprechtaste und **Später** (A2.2, A3, A4.1); die Statuszeile **Mikrofon** statt der aufklappbaren Leiste Sprachaktivierung, mit zwei neuen Sätzen (A2.5); Einstellungen in Abschnitten mit **Läuft von selbst** und zugeklappten Bereichen (seit 0.11.1 **Erweitert:**), die Bereiche Audio und Meine Kanäle aufgelöst, die Echounterdrückung in drei Stufen (A2.5, A3.1, A3.5); das Hauptfenster auf Start mindestens 900 x 900, mit **Wer ist da** breiter oder die Spalte als Schublade über Start (A2.5); **Beim Start verbinden** nach der Freigabe einmal an (A2.3); die Geräteliste folgt dem Ein- und Ausstecken (A3.2); **Mikro stumm** im Menü des Symbols (A6.6). Am Server ändert 0.10.0 nichts. Dieses Handbuch beschreibt die Wege im Client nach 0.10.0; der Rest von 1.9 ist Stand 0.5.0. |
| Neu in 0.11.0 (Stand 12.10.2026, Zweig `modul/zuordnung`, gebaut und getestet, noch nicht gemergt) | Zuordnung: Personen als **Kacheln** in **Wer ist da** und links im Brett, mit Punkt für angemeldet und Ring für offline, **auch offline** für die Einsatzleitung (A8, A12.2); **Ch.** je Feld im Detail des Bretts (A12.3); die Ansage **Eingeteilt von ...** beim Login und die Rolle des Platzes oben links im Funkfeld (A6.7, A7.3); die selbst gewählte Rolle bleibt gemerkt, wenn jemand anders einteilt (A7.3); die **Meldezeile einzeilig**, je Meldung eine Zeile, höchstens so breit wie das Funkfeld, kürzere Sätze (A6.7). Am Server: Eine Einteilung kommt beim Login als VERSCHIEBEN mit dem Namen der Leitung zurück (nach einer eigenen Änderung als "selbst"), der Ch. der Leitung auf einem Feld auf Ruhe wird Sendeziel, "zurück auf Standard" hält keinen Platz mehr, ein nie angemeldetes Konto belegt keinen vergebenen Platz ein zweites Mal, und Untergilden behalten Stammeinheit und Ansprechpartner (B, `BETRIEB.md`, "Update auf 0.11.0"). |
| Neu in 0.11.1 (Stand 12.10.2026, Zweig `modul/erweitert`, gebaut und getestet, noch nicht gemergt) | Nach dem ersten Test von 0.11.0 am echten PC: Unter **Einstellungen**, **Senden** stehen die Schiebeschalter wieder offen, ohne Aufklappen, wie bis 0.9.3 auf der Seite Audio: Echounterdrückung (Automatisch, An, Aus), Echoschutz, Rauschunterdrückung mit Standard und Stark, automatische Lautstärke, Sprachaktivierung und Mindest-Funklautstärke, je mit ihrem kurzen Satz, darunter die **Funkschwelle** mit Pegelbalken, Griffen, **Einmessen** und **Genau einstellen ...** (A3.1). Die Klappbereiche heißen jetzt **Erweitert:** (das Wort fett), die Kopfzeile etwa `Erweitert: (1 selbst gestellt) Feste Verstärkung, Mithören, Klangfarbe`; unter **Senden** stehen dort nur noch feste Verstärkung, Mithören und Klangfarbe (A2.5). Dazu zwei Fehler im Brett aus Sarevocs Test: Eine Karte, die du auf einen Block zogst, landete auf dem besten Platz der ganzen Spalte (in der alten Kanalvorlage auf Rolle 1, Standard), jetzt auf dem Block; unter jedem Netz rechts stehen die Personen darin als kleine Kacheln, ziehbar zum Umziehen, mit Kreuz oder nach links zum Entfernen des Zusatznetzes, und eine Person ohne Platz geht mit ihren Zusatznetzen nach links auf Standard (A12.3). Beim Ziehen steht die Person gedämpft im ersten freien Platz des Ziels (**Vorschau**; in einer Rolle ohne Platzgrenze in einer Zeile nur dafür), und beim Loslassen gleitet die Karte in etwa 150 ms an ihren Platz (A12.3). Die **Meldezeile** steht an einem festen Platz des Bildschirms, ab Werk oben in der Mitte auf dem Monitor von Star Citizen, einstellbar mit einem Raster aus neun Feldern und der Wahl des Monitors (mit Probe), fest 640 Einheiten breit; ein Hinweis, der von selbst entsteht, erscheint höchstens einmal in fünf Minuten, was der Server meldet und jeder Fehler jedes Mal, eigene Aktionen (auch eine selbst geschaltete Phase) melden nichts (A6.7). Im **Verwaltungsteil** (Admin-Setup) lag bei 175 % der Knopf **Mitglied vormerken** über der Überschrift, und der Suchtext war abgeschnitten: Jetzt stehen Liste und Karte in schmalen Fenstern untereinander, ein Knopf neben einer Überschrift rutscht bei Platzmangel darunter, Beschriftungen und Listenzeilen brechen um statt mit "..." zu enden, und ein Wächter prüft jede Seite bei 760, 960 und 1100 Breite (1.8). Am Server ändert 0.11.1 nichts. |
| Neu in 0.11.2 (Stand 13.10.2026, gebaut und an Sarevoc geliefert, in `main` mit `be4dd33`; dazu die Namenslänge in `5d3f5bf` auf dem Zweig `modul/namen22`, noch nicht gemergt) | Nach den Vorgaben von Sarevoc vom 13.10.2026: Die **Funkfeld-Zuteilung** (Strg+5, bis 0.11.1 das Brett) hat oben nur **LIVE - wirkt sofort** mit dem Auftrag, **Rückgängig** und **Abend beenden ...**; links stehen alle Namen (live oben nach Rang, offline unter einer Linie) mit der Zahl der Kanäle, in denen die Person sitzt; rechts ein Baum wie ein Stammbaum mit allen Kanälen ohne Global und Einheitsnetze, je Kanal **Ruhe** und die besetzten Ch., die leeren hinter **N weitere Ch.**. Einen Namen auf einen Ch. ziehen teilt genau diesen Kanal ein, im Baum umziehen setzt um, nach links ziehen nimmt heraus, Strg+Z nimmt jeden Zug zurück. Plätze und Einheiten, **hört** und **hört + sendet**, Detailfeld, Ch.-Raster, Phasenleiste, die Sperren, **Wie zuletzt**, Verlauf, das Menü an den Namen und Stumm sind von dieser Seite weg (A12). Die **Meldezeile** zeigt nur noch, was die Leitung tut, dein Melden und die Probe, fest breit; bei **Automatisch** bleibt sie auf dem Monitor, auf dem das Spiel zuletzt zu sehen war (A6.7). Die Sätze `Ruhe: es wird nichts gesendet`, `Stummgeschaltet: ...` und `Nicht verbunden: ...` gibt es nirgends mehr; von den Sätzen, warum nichts hinausgeht, bleibt nur `Kanal braucht die Sprechtaste - keine belegt`, dazu wie seit 0.10.0 `Stimme unter der Mindest-Funklautstärke` (A2.5). Alle Knöpfe sind flach und neutral, die Hauptaktion **Verbinden** flach weinrot (A2.5). **Start** zeigt nur das Audio, unten **Senden** mit Pegelbalken, Griffen, **Einmessen** und Schaltern; die Karte **Einsteigen** und `Mikrofon und Wiedergabe sind zu.` sind weg, **Audio starten** steht unter **Einstellungen**, **Fehlersuche** (A3, A3.2, A7.1). Der SC-Name nimmt bei der Eingabe höchstens 22 Zeichen (A1.3). Am Server: Wer offline ist und vorab nur in Kanäle eingeteilt wurde, steht 12 Stunden lang mit in der Präsenz, damit ihn die Funkfeld-Zuteilung im Baum zeigt (B0.1). |
| Neu in 0.11.3 (Stand 13.10.2026) | Die **Funkfeld-Zuteilung** hat oben eine Zeile **Reiter**: ganz links die **Rollenwahl** wie im Funkfeld (etwa **Standard (Gast)**, **Flottenführung** ...), daneben die ersten fünf Kanäle der Rolle; darunter der Stammbaum des offenen Kanals, immer aufgeklappt. Die Reiter haben runde Ecken, überlappen sich wie Karteireiter, der offene trägt einen weinroten, leicht glühenden Strich (A12.2). Der SC-Name nimmt bei der Eingabe höchstens 22 Zeichen (A1.3). |
| Neu in 0.11.4 (Stand 14.10.2026) | Unter **Einstellungen** ist jeder Bereich **Erweitert:** eine umrandete Karte über die ganze Breite wie in Windows 11: oben fett **Erweitert:**, darunter Stand und Inhalt, rechts der Pfeil; aufgeklappt steht der Inhalt in derselben Karte unter einer Trennlinie (A7). |
| Neu in 0.11.5 (Stand 14.10.2026, Zweig `modul/weristda`, gebaut und getestet, noch nicht gemergt) | **Wer ist da** ist eine flache Liste, eine Zeile je Person, alle Namen gleich breit und nie gekürzt (ein breiter Name steht in kleinerer Schrift), online zuerst, dann Admins, Führung, Einsatzleitung, alle anderen; keine Kacheln, keine Gruppen nach Untergilde oder Einheit, kein gelbes "!" und keine Karte mehr. Hinter dem Namen: das **Mikrofon** (die Einsatzleitung schaltet stumm, bis zum zweiten Klick, bis **Abend beenden** oder höchstens eine Stunde; stumm ist es blutrot), der **Status** (grün in Kanälen der Funkfeld-Zuteilung, gelb in keinem; als eingerichteter Admin im Admin-Setup ein Sprung in die Verwaltung zu diesem Mitglied) und die **Wippe** (links leiser, rechts lauter, je 2 dB, nur für dich). Mit der Einsatzleitung öffnet der Name die Funkfeld-Zuteilung (A8). Ein verstellter Pegel je Person springt beim nächsten Funkspruch nicht mehr auf den Wert der Anmeldung zurück (A3.5). **Abend beenden** hebt die eigenen Stummschaltungen auf (A12.4). Am Server ändert 0.11.5 nichts. |
| Neu in 0.11.7 (Stand 15.10.2026, Zweig `modul/abzeichen`) | **Abzeichen** (Sarevoc 14./15.10.2026): Ränge in drei Laufbahnen (Marine 10, Navy 10, Orga 8 Stufen; je Laufbahn ein Rang) und 22 Tätigkeiten (höchstens 6 je Person), selbst gezeichnet und mit dem Programm ausgeliefert. Vergeben nur Admins in der Admin-Sitzung auf der neuen Seite **Abzeichen** ganz unten im Hauptmenü (A9.2). Unter **Einstellungen**, **Ich**, **Erweitert** steht **Meine Abzeichen**: je Abzeichen die Wahl **vorn** und der Haken **zeigen**, auch für Kranz und Stern (A9.1). **Wer ist da** und die Namen der **Funkfeld-Zuteilung** zeigen das Abzeichen vorn, der Hilfetext nennt alle sichtbaren (A8). Die Winkel der Führung werden nicht mehr gezeichnet. Am Server: die neue Anfrage **Abzeichen**, die Abzeichen liegen in `mitglieder.json`; Client und Server gemeinsam aktualisieren (B11.7). |
| Neu in 0.11.6 (Stand 14.10.2026) | Die Seite der Einsatzleitung heißt **Funkfeld-Zuteilung** (Strg+5; Sarevoc 14.10.2026: "wir nennen die Seite nun Funkfeld Zuteilung"), bis 0.11.5 **Raid**, bis 0.11.1 **das Brett**. Hilfetexte, die noch "Brett" sagten, sind berichtigt; der Hovertext am Suchfeld der Seite sagt richtig, dass das Kürzel nur ganz gefunden wird. |
| Neu in 0.11.8 (Stand 15.10.2026, Zweig `modul/ohne-untergilden`) | **Untergilden gibt es nicht mehr** (Sarevoc: "Untergilden entfallen ganz, überall"): kein Kürzel in **Wer ist da** und auf der **Funkfeld-Zuteilung**, die Suche findet Name, SC-Name und Discord-Name; in der Verwaltung keine Seite **Untergilden** und keine Auswahl auf der Karte eines Mitglieds. **Meine Angaben in Wer ist da zeigen** nennt nur noch Zeichen und Abzeichen. Bei **Liste einfügen** bleibt die dritte Spalte leer (`SC;Discord;;Rollen`); steht dort etwas, wird es übergangen, und ein Hinweis sagt es. Gespeicherte Untergilden löscht der Server nicht, er zeigt sie nur nicht mehr (B11.8). |
| Neu in 0.11.9 (Stand 15.10.2026, Zweig `modul/sprecheranzeige`) | Die **Sprecheranzeige** (Sarevoc 11.10. und 15.10.2026): Solange jemand zu hören ist, steht über dem Spiel sein Name fett, dahinter der Kanal mit Ch. (`Brücke (Schiff 1)`), eine Durchsage als `Durchsage an alle` oder `Durchsage: Kanal (Ch.)`, Global als `Global`; weiße Schrift mit leichtem Schatten, ohne Fläche, Rahmen, Farben oder Zeichen. Gezeigt wird nur, wen du wirklich hörst: abgeschaltete Kanäle und Funker, die du am gelben "!" stumm geschaltet hast, nicht. Ab Werk an, oben links, eine Zeile, 1,0 s Nachhalten, 0,5 s Ausblenden, die eigene Sendung aus. Unter **Einstellungen** der neue Abschnitt **Sprecheranzeige** zwischen **Funkfeld und Darstellung** und **Fehlersuche**: offen der Schalter und unter **Wo im Bild** die Regler **Waagerecht** und **Senkrecht**, je 0 bis 100 % des Monitors (Sarevoc: "die Position mit Schieberegler einstellen, nicht fest vorgeschlagen"; beim Schieben steht eine Probezeile dort über dem Spiel, noch 3 s danach), unter **Erweitert** Größe, Durchsichtigkeit, Zeilen (1 bis 4), Nachhalten, Ausblenden, eigene Sendung, Monitor und eine Vorschau (A6.8). Am Server ändert 0.11.9 nichts; Star Citizen muss randlos laufen. |
| Neu in 0.12.0 (Stand 16.10.2026, Zweig `modul/meldesystem`) | Das **Meldesystem** (Sarevoc 11.10. und 16.10.2026): Die Leitung schickt Grafiken an alle, die online sind, über das neue Feld **Meldungen** auf der **Funkfeld-Zuteilung** (A12.8): Befehle (Sammeln, Sammelpunkt, Stopp, Rückzug, Start frei, Med. Hilfe, Los, Stopfen; 8 s groß, ein neuer ersetzt den alten), Zustände (Einsatz oder Event noch nicht gestartet, Position halten, Funkdisziplin, Pause; klein, bis aufgehoben), die Ampel rot, gelb, grün nur von Hand, einen Countdown als **Start um** oder **Restzeit** und die **Bereit-Abfrage** mit `n von m bereit` und wer fehlt; dazu Größe normal, doppelt, dreifach und eine Zeile bis 60 Zeichen. Dazu die **Textmeldung** (Sarevoc 16.10.2026 abends, M24): nur Text bis 120 Zeichen, ohne Grafik, in Weiß, Gelb, Orange, Rot, Grün oder Blau, 8 s oder bis aufgehoben, beim Funker unter dem Befehlsplatz in höchstens zwei Zeilen. Die Xeno-Hände im Stopp-Schild und in der Ampel haben seit der Korrektur v4 drei Finger (M25). Senden dürfen nur Führung, Admins und die Einsatzleitung mit laufendem Auftrag, die ein Admin auf der Mitgliederkarte mit dem Haken **darf Meldungen senden** ernannt hat (B). Beim Funker stehen die Meldungen über dem Spiel, ab Werk oben in der Mitte, mit kurzem Ton bei Befehlen und in den letzten 10 s des Countdowns; Lage, Grundgröße, Ausführung (Schlicht, Metall, Nur Schrift), Bild beim Stopp und Ton stellt jeder unter **Einstellungen**, **Meldungen** (A6.9). Bereit melden geht mit der neuen Taste **Bereit** oder dem Knopf **Bereit melden** in der Titelleiste (A4.6). Client und Server gemeinsam aktualisieren; ältere Clients funken mit einem Server 0.12.0 weiter, sehen aber keine Meldungen (B). |
| Neu in 0.13.0 (Stand 19.10.2026, Zweig `umbau/tacrelay`) | Die erste Fassung als **JG TacRelay** (Sarevoc 18./19.10.2026); bis 0.12.0 hieß das Programm anders. Neu sind der Name in Programm, Setup, Paketen und Doku (Programmdatei `TacRelay.exe`, Datenordner `%LOCALAPPDATA%\JG-TacRelay`), die Farbstellung **Anthrazit** mit Grautönen und dem Akzent Petrol statt Weinrot, ohne Farbwahl unter **Einstellungen**; Grün für Senden, Gelb und Rot für Warnungen bleiben (A6.5). Dazu das neue Symbol, ein Kopfhörer im Sechseck in Petrol auf Anthrazit, und die Kanalvorlage **Star Citizen** mit zehn Kanälen mit Ch. und 14 Rollen statt der früheren Vorlagen (B14.2). Das **Spielprofil** steht unter **Einstellungen**, **Sprecheranzeige**, **Erweitert:**: ab Werk **Star Citizen**, sonst ein anderes Spiel über seine Programmdatei; Sprecheranzeige, Meldungen und Meldezeile suchen dann dessen Monitor. Alle Kennungen sind neu (ALPN `tacrelay/1`, Signatur- und HKDF-Präfixe, AppId, TPM-Schlüssel, Umzugspaket `.tru`), einen Übergang von 0.12.0 gibt es nicht: Server und Clients werden gemeinsam neu eingerichtet, Geräte neu freigegeben, Admin-Nachweis und Zweitfaktor (TOTP) neu eingerichtet und das Serverprofil neu signiert. Ein Server-Stand bis 0.12.0 wird nicht übernommen. |
| Neu in 0.13.1 (Stand 25.11.2026) | Die erste öffentliche Fassung: Der Quelltext steht unter der Apache-Lizenz 2.0 auf GitHub, mit `LICENSE`, `NOTICE` und `README.md` (1.10, F1). Das Stoppschild mit der dreifingrigen Alien-Hand heißt jetzt **Xeno**; Bild und Wahl unter **Einstellungen**, **Meldungen** bleiben, wie sie waren (A6.9). Beispielnamen und Beispieladressen in Doku und Tests sind neutral, und Teil B hat den neuen Abschnitt B0.4 für Gruppen mit eigenem Server. Am Server und im Protokoll ändert sich nichts. |
| Neu in 0.13.2 (Stand 25.11.2026) | Setup und Admin-Setup bringen die **.NET Desktop Runtime 10** von Microsoft mit (heute 10.0.15) und installieren sie, wenn sie auf dem PC fehlt; Windows fragt dafür einmal nach Administratorrechten, das Setup selbst braucht weiter keine. Aktuell hält die Runtime danach Windows Update (A1.2). Am Programm, am Server und im Protokoll ändert sich nichts; das Probefunk-Paket als ZIP bleibt ohne Runtime. |
| Später | Ein Umzug auf einen gemieteten Server (Hetzner, OVH oder ein anderer Anbieter, Debian 13 oder Ubuntu 24.04) ist vorbereitet, erst nach Version 1 und nach einem Test mit ein paar Leuten (Anf. A55). Dafür braucht niemand ein neues Setup, nur mindestens Fassung 0.4.0 (Abschnitt A22, B20). |
| Eigentum und Lizenz | Rechteinhaber ist Sarevoc (Anf. A41); seit 0.13.1 steht der Quelltext unter der Apache-Lizenz 2.0 (1.10). |

### 1.10 Rechte und Lizenz

Rechteinhaber ist Sarevoc (Anf. A41, A58). Unter diesem Namen steht er als Maintainer im Handbuch und in den
Quelldateien; ein Pseudonym genügt dafür, den bürgerlichen Namen braucht es nur, wo das Recht ihn verlangt (Anf. A59,
F3.3). Seit 0.5.0 steht in jeder eigenen Quelldatei die Kopfzeile "JG TacRelay - Maintainer: Sarevoc", ohne Rechtsaussage
und ohne bürgerlichen Namen; ausgenommen sind `QrCode.cs` und `WeichesClipping.cs`, die den Vermerk ihrer fremden
Rechteinhaber tragen. Auch das Setup nennt ihn als Herausgeber (F9).

Seit 0.13.1 (25.11.2026) ist JG TacRelay quelloffen, unter der **Apache-Lizenz 2.0** (`LICENSE` im Repository). Jeder
darf das Programm nutzen, verändern und weitergeben, auch als Teil eines eigenen Programms. Wer es oder Teile davon
weitergibt, legt die Lizenz bei, kennzeichnet geänderte Dateien und gibt den Inhalt der Datei `NOTICE` mit; dort stehen
Sarevoc und ein Hinweis auf sein Projekt Joystick Guardian. Code, Tests und Doku schreibt Claude, ein KI-Modell von
Anthropic, nach den Vorgaben von Sarevoc; das steht einmal zentral in `README.md` und in F1. Es gilt deutsches und
EU-Recht; was die Lizenz bedeutet und warum es diese ist, steht in F1 und F8.

Die fremden Bestandteile (Opus, RNNoise, SpeexDSP, WebRTC-Audioverarbeitung, NAudio,
System.Numerics.Tensors, CommunityToolkit.Mvvm, gong-wpf-dragdrop, die .NET-Programmstarter, Inno Setup, der
QR-Code-Erzeuger nach Project Nayuki und eine Übertragung aus Opus) stehen mit ihren Lizenzen in
`THIRD-PARTY-NOTICES.txt`, und ihre Lizenztexte liegen den Paketen im Ordner `lizenzen` bei. Alle diese Lizenzen sind
freizügig und vertragen sich mit der Apache-Lizenz 2.0 (F7). Offen ist noch, unter welcher Lizenz die einkompilierten
KI-Gewichte in `opus.dll` und `rnnoise.dll` stehen; sie sind als UNGEKLÄRT vermerkt (F7.1). Schriften, Symbole und der
Funk-Klick sind Eigenbau; ein Gildenlogo ist nicht enthalten, nur der eigene Platzhalter "Hier könnte dein Logo sein".


## Teil A: Anleitung für Spieler und Betreiber

Dieser Teil ist für alle, die JG TacRelay benutzen, ohne IT-Ausbildung. Die Abschnitte A1 bis A11 sind für jeden
Spieler, A12 für die Einsatzleitung. Die Abschnitte A13 bis A22 sind für den, der den Server betreibt und schon einmal
einen Server eingerichtet hat, aber kein IT-Mensch ist. Fachwörter sind beim ersten Vorkommen in Kapitel 1 oder in
diesem Teil erklärt und stehen alle im Glossar; tiefer gehen Teil B (Server und Befehle), Teil C (Sicherheit), Teil D (Last), Teil E (Klang und
Sprachaktivierung) und die Anleitungen im Serverpaket.

Schreibweisen wie in der Lesehilfe im Titelteil: **Fett** ist, was du auf dem Bildschirm findest, `So` was du tippst oder
ein Programm genau so ausgibt, `<so>` ein Platzhalter für deinen eigenen Wert, und **Das siehst du** beschreibt, woran du
erkennst, dass ein Schritt geklappt hat.


## A1 Den Client installieren

### A1.1 Was du brauchst

- einen PC mit Windows 10 oder 11, 64 Bit,
- die **.NET Desktop Runtime 10 (x64)** von Microsoft. Das ist die Laufzeitumgebung, auf der JG TacRelay läuft. Seit
  0.13.2 bringt das Setup sie mit und installiert sie, wenn sie fehlt; sonst gibt es sie unter
  `https://dotnet.microsoft.com/download/dotnet/10.0`, dort unter ".NET Desktop Runtime" den Installer für Windows x64,
- ein Mikrofon und ein Headset, Kopfhörer oder Lautsprecher,
- das Setup `<zeit>_JG-TacRelay-Setup-<fassung>.exe` von einem Admin,
- deinen Namen in Star Citizen und deinen Namen auf dem Discord-Server deiner Gruppe.

Administratorrechte brauchst du für JG TacRelay nicht; es installiert sich nur für dein Windows-Konto. Nur wenn die .NET Desktop Runtime fehlt, fragt Windows einmal danach, denn die Runtime von Microsoft gilt für den ganzen PC.

### A1.2 Setup ausführen

1. Doppelklick auf das Setup. JG TacRelay ist noch nicht signiert; Windows SmartScreen kann deshalb warnen ("Der
   Computer wurde durch Windows geschützt"). Dann auf **Weitere Informationen** und **Trotzdem ausführen** klicken.
2. Fehlt die .NET Desktop Runtime 10, sagt es die Seite **Bereit**, und beim **Installieren** startet das Setup zuerst
   den Installer von Microsoft. Windows fragt dabei einmal nach Administratorrechten; das Fenster von Microsoft zeigt
   nur den Fortschritt. Sagst du dort **Nein** oder scheitert es, installiert das Setup JG TacRelay trotzdem und nennt
   die Seite von Microsoft; JG TacRelay startet dann erst, wenn die Runtime da ist. Ihre Sicherheitsupdates bringt danach
   Windows Update, wenn unter **Einstellungen**, **Windows Update**, **Erweiterte Optionen** der Schalter **Updates für
   andere Microsoft-Produkte erhalten** an ist.
3. Der Ordner: Vorgeschlagen ist `%LOCALAPPDATA%\Programs\JG-TacRelay` (in deinem Benutzerordner unter
   `AppData\Local\Programs`). Du kannst einen anderen wählen, in den du ohne Administratorrechte schreiben darfst.
4. Die Seite **Deine Namen** (nächster Abschnitt).
5. Wahlweise ein Symbol auf dem Desktop. Dann **Installieren**.

**Das siehst du:** Am Ende steht "JG TacRelay ist eingerichtet", mit dem Hinweis auf die Einträge im Startmenü. Dort
liegt ein Ordner **JG TacRelay** mit drei Einträgen: JG TacRelay, JG TacRelay Probefunk und dem Deinstallieren. Das
Häkchen **JG TacRelay jetzt starten** ist ab Werk aus, weil JG TacRelay beim Start Mikrofon und Wiedergabe öffnet.

### A1.3 Deine Namen

Bei einer neuen Installation fragt das Setup:

- **Dein Name in Star Citizen, so wie im Spiel**, etwa `Mustermann`,
- **Dein Discord-Name, wie er auf deinem Discord-Server steht**, etwa `Mustermann/Max`.

Die Beispiele in diesem Handbuch gehören zu einem erfundenen Mitglied, Max Mustermann; der Anmeldename im Discord ist
`Mustermann/Max`. Damit erkennt dich der Admin, bevor er dich freischaltet. Ein Passwort gibt es nicht. Regeln:

- Beide Namen oder keinen. Lässt du beide leer, fragt JG TacRelay beim ersten Verbinden danach.
- Jeder Name hat 2 bis 32 Zeichen. Der SC-Name besteht aus Buchstaben, Ziffern, Leerzeichen, `_`, `-` und Punkt, so wie im
  Spiel. Der Discord-Name darf keine unsichtbaren Zeichen und keine doppelten Leerzeichen enthalten.
- Seit 0.11.2 nehmen die Felder für den SC-Namen im Programm höchstens 22 Zeichen an: beim ersten Verbinden (A2.2), unter
  **Einstellungen**, **Ich** (A9) und in der Verwaltung. Der Hovertext sagt es, etwa `Wie im Spiel, etwa Mustermann,
  höchstens 22 Zeichen`. So steht ein SC-Name auf der Funkfeld-Zuteilung ganz.
  Das Setup und der Server nehmen weiter bis 32; ein längerer Name von früher bleibt gültig. **Wer ist da** zeigt den
  Discord-Namen (bis 32 Zeichen) und kürzt seit 0.11.5 keinen Namen: Ein breiter Name steht dort in kleinerer Schrift (A8).
- Beim Discord-Namen ist die Form frei, solange der Admin weiß, wer du bist: SC- oder Discord-Name, dahinter Rufname
  oder Spitzname, etwa `Mustermann/Max`.
- Beide Namen sind auf dem Server eindeutig, auch gegen andere Groß- und Kleinschreibung. Trägt jemand anderes schon
  deinen Discord-Namen, kann der Admin dich so nicht freigeben; einen schon vergebenen SC-Namen übernimmt die Freigabe
  nicht. In beiden Fällen sprecht ihr euch ab.
- Den Discord-Namen ändert nach der Freigabe nur ein Admin. So kann sich niemand als jemand anderes ausgeben.

Die Namen landen in `%LOCALAPPDATA%\JG-TacRelay\anmeldung.json`. Geheim ist daran nichts. Die Seite kommt nur bei einer
neuen Installation; liegen schon Einstellungen von JG TacRelay auf dem PC, fragt der Client selbst.

### A1.4 Normales Setup und Admin-Setup

Alle Mitglieder bekommen das normale Setup. Admins bekommen das **Admin-Setup**: derselbe Client, dazu der
Verwaltungsteil. Das Admin-Setup sagt beim Installieren: "Dieses Setup enthält den Verwaltungsteil. Er bleibt gesperrt,
bis der Eigentümer dich zum Admin ernennt." Wer nicht Admin ist, sieht vom Verwaltungsteil nichts.

Für einen Admin-PC gelten ab Fassung 0.4.1:

| Voraussetzung | Art |
| --- | --- |
| TPM 2.0 | Pflicht. Der Admin-Schlüssel entsteht im TPM, lässt sich nicht exportieren und wird nie als Datei gespeichert oder kopiert. Ohne TPM 2.0 gibt es auf diesem PC keine Verwaltung. |
| Windows-Konto mit PIN oder Kennwort | Richtlinie: Der Admin sorgt selbst dafür. Ein Microsoft-Konto ist nicht nötig, ein lokales Konto reicht. |
| BitLocker oder Geräteverschlüsselung | empfohlen, keine Pflicht. Fehlt sie, warnt der Client: "BitLocker/Geräteverschlüsselung wird empfohlen, um Daten bei Offline-Zugriff auf die Festplatte zu schützen." |
| Authenticator-App auf dem Telefon | für den Zweitfaktor (Abschnitt A17) |

Ein TPM (Trusted Platform Module) ist ein Sicherheitschip im PC. Ein Schlüssel darin lässt sich benutzen, aber nicht
herauskopieren. BitLocker ist die Laufwerksverschlüsselung von Windows; die Geräteverschlüsselung ist ihre Form, die
Windows auf manchen PCs von selbst einschaltet. Bis Fassung 0.4.0 nannte der Verwaltungsteil BitLocker noch als Pflicht;
in 0.4.1 ist der Satz fort, und es gilt die Tabelle, so wie Sarevoc es entschieden hat (Anf. A56).

Bevor der Admin-Schlüssel entsteht, sieht der Verwaltungsteil nach, was der PC mitbringt, ohne einen Schlüssel
anzulegen. Auf der Karte **Verwaltung einrichten** steht unter **Vorprüfung auf diesem PC** je Voraussetzung eine Zeile:

| Zeile | Was darunter steht |
| --- | --- |
| **TPM 2.0 (technisch Pflicht)** | mit TPM 2.0 `TPM 2.0 gefunden.` und ein Haken. Findet Windows kein TPM, steckt ein TPM 1.2 im PC oder lässt sich der Microsoft Platform Crypto Provider nicht öffnen, über den der Schlüssel ins TPM kommt, sagt die Zeile das in Rot, etwa `Windows findet auf diesem PC kein TPM (...). Ohne TPM 2.0 gibt es hier keine Verwaltung. Ist eins eingebaut, kann es im BIOS/UEFI abgeschaltet sein.`, und der Knopf **Admin-Schlüssel anlegen und beantragen** bleibt aus. Lässt sich die Fassung nicht feststellen, steht das da, und versuchen darfst du es trotzdem. |
| **Windows-Konto mit PIN oder Kennwort (Richtlinie)** | `Ein lokales Konto genügt, ein Microsoft-Konto ist nicht nötig. Das verlangt die Gilde, nicht das TPM; der Client prüft es nicht.` |
| **BitLocker oder Geräteverschlüsselung (empfohlen)** | ist sie an, etwa `BitLocker/Geräteverschlüsselung ist für das Systemlaufwerk C: eingeschaltet.` Ist sie aus, groß und mit orangem Rand `BitLocker/Geräteverschlüsselung wird empfohlen, um Daten bei Offline-Zugriff auf die Festplatte zu schützen.` und darunter `Keine Voraussetzung: Das TPM braucht für den Admin-Schlüssel weder BitLocker noch eine Windows-PIN.` Lässt es sich nicht feststellen, sagt die Zeile auch das. |

**Erneut prüfen** sieht noch einmal nach, ebenfalls ohne Schlüssel. Warum es diese Voraussetzungen gibt, steht in C11.2.

Admins nehmen immer das Admin-Setup **derselben Fassung** wie den Client. Passt der Verwaltungsteil nicht zur Fassung,
lädt der Client ihn nicht und meldet `Verwaltungsteil passt nicht zu dieser Fassung - das Admin-Setup dieser Fassung
ausführen.`

### A1.5 Aktualisieren, Reparieren, Deinstallieren

Startest du ein Setup, während JG TacRelay schon installiert ist, erkennt es die Installation und bietet an:

- **Aktualisieren:** die neue Fassung in den bisherigen Ordner,
- **Reparieren:** alle Dateien neu schreiben, etwa wenn JG TacRelay nicht mehr startet,
- **Neu installieren:** erst entfernen, dann den Ordner frei wählen.

Bringt das Setup eine ältere Fassung als die installierte, fragt es ausdrücklich, ob du zurückgehen willst. Läuft
JG TacRelay noch, bittet das Setup dich, es zu schließen; der Probefunk geht dann von selbst zu.

Deinstallieren geht über das Startmenü oder Windows "Apps". Deine Einstellungen in
`%LOCALAPPDATA%\JG-TacRelay` bleiben dabei stehen, damit eine neue Installation alles wiederfindet. Brauchst du sie nicht
mehr, löschst du den Ordner selbst. Dein Geräteschlüssel liegt nicht in diesem Ordner, sondern im Schlüsselspeicher von
Windows.

JG TacRelay läuft immer nur einmal. Startest du es ein zweites Mal, zeigt es das schon laufende Hauptfenster.


## A2 Zum ersten Mal verbinden

### A2.1 Erst ausprobieren: der Probefunk

Wer zuerst üben will, ohne den echten Server: Im Startmenü **JG TacRelay Probefunk**. Es geht ein schwarzes Fenster
auf, das ist ein Testserver nur auf deinem PC (`127.0.0.1`, von außen nicht erreichbar), dazu startet JG TacRelay und
verbindet sich von selbst.

- Du bist im Netz **Echo** (dort sendest du) und in **Kommando**.
- Sprich einen Satz ins Netz Echo. Eine halbe Sekunde nachdem du aufgehört hast, kommt er zurück, über Server, Puffer und
  Mischer, so wie dich andere hören würden. Ein Satz darf bis zu 30 Sekunden lang sein. Nach der Wiedergabe wartet der
  Echo-Funker eine Sekunde, bevor er wieder zuhört.
- Rollen probierst du oben links im Funkfeld aus (A7.1). Die Probekonfiguration hat Schiff 1 bis 3; einen Platz darauf
  wählst du seit 0.11.2 nicht mehr selbst, die Karte **Einsteigen** gibt es nicht mehr.
- Im Probefunk bist du Admin und siehst deshalb auch die Funkfeld-Zuteilung der Einsatzleitung (Strg+5). Dort kannst du das
  Ziehen mit der Maus gefahrlos ausprobieren. Auch "Heute als Funker" (A12.7) zeigt der Probefunk dir, denn sein
  Testserver ist derselbe Servercode wie der echte (am Code geprüft). Phasen legt die Probekonfiguration nicht an; die
  Phasentasten (A12.6) antworten dort deshalb mit `Keine Phasen angelegt (Flotte > Phasen)`.
- Zum Beenden schließt du JG TacRelay; das schwarze Fenster geht dann von selbst zu.

Der Probefunk hat eigene Einstellungen unter `%LOCALAPPDATA%\JG-TacRelay\probe` und lässt deine normalen in Ruhe. Er darf
neben dem normalen JG TacRelay laufen; dann reagieren aber beide auf deine Sprechtaste.

### A2.2 Verbinden und Antrag stellen

1. **JG TacRelay** aus dem Startmenü starten. Es öffnet sich auf der Seite **Start** (Strg+1).
2. Beim allerersten Start steht in der Startkarte an der Stelle von **Verbinden** ein Satz:
   `Willkommen. Zwei Angaben fehlen noch, dann kommt hier Verbinden.` Die zwei Angaben sind darunter umrandet: **Ich höre
   mit** (Headset oder Lautsprecher, A3.1) und **Sprechtaste** mit `Drück jetzt deine Sprechtaste`. JG TacRelay wartet
   dort schon auf deine Taste; drück sie, oder klick **Später** (A4.1). Mikrofon und Ausgabe stehen auf dem
   Windows-Standard, alles andere regelt die Automatik. Erst wenn beide Angaben erledigt sind, steht dort **Verbinden**.
   Seit 0.10.0; bis 0.9.3 fragte eine Karte nur nach Headset oder Lautsprecher.
3. **Verbinden** klicken. Eingeben musst du dafür nichts: Serverprofil und Profilschlüssel kommen mit dem Setup.
4. Eine Karte über der Startkarte fragt: `Anmelden als <SC-Name> (Discord: <Discord-Name>) - stimmt das?` Stimmen die
   Namen, klickst du **So anmelden und verbinden**. Fehlen sie, trägst du sie hier ein (den SC-Namen seit 0.11.2 mit
   höchstens 22 Zeichen, A1.3). Unter den Namen steht, wie der
   Gerätename entsteht: Er wird aus der Gerätekennung gebildet, nicht aus dem Namen deines Windows-PCs.
5. Oben in der Titelleiste steht erst `Verbinde...`, dann `Wartet auf Freigabe`.

**Das siehst du:** In der Startkarte steht unter deinem Namen `Wartet auf Freigabe`, darunter deine Gerätekennung groß,
`XXXX-XXXX-XXXX-XXXX`, daneben **Kopieren**, und der Satz: "Schick dem Admin in Discord deinen SC-Namen und diese Kennung.
Die Kennung ist kein Geheimnis. Nach Codes oder Passwörtern fragt dich niemand."

### A2.3 Freigabe durch den Admin

1. Kopier die Kennung (**Kopieren**, dann in Discord mit Strg+V einfügen) und schick sie mit deinem SC-Namen an einen
   Admin.
2. Der Admin vergleicht die Kennung mit dem Antrag, den er sieht, und gibt dein Gerät frei. Du musst dafür nichts tun;
   der Client bleibt verbunden und wartet.
3. Nach der Freigabe steht oben `Verbunden`, und in der Startkarte `Gerät freigegeben` mit einem Haken.

Seit 0.10.0 geht dabei **Beim Start verbinden** einmal von selbst an: Ab dem nächsten Start verbindet sich JG TacRelay
ohne Klick. Das gilt auch für ein Gerät, das schon vor dem Update freigegeben war, selbst wenn du den Schalter früher aus
hattest; er geht nach dem Update genau einmal an. Wer das nicht will, schaltet ihn unter **Einstellungen** (Strg+2),
**Funkfeld und Darstellung**, **Erweitert**, Gruppe **Beim Start** wieder aus; danach bleibt er aus.

Wichtig:

- **Nach Codes oder Passwörtern fragt dich niemand.** Wer das tut, ist nicht der Admin.
- **Profildateien oder Schlüssel nimmst du von niemandem an.** Ein neueres Serverprofil holt sich der Client selbst vom
  Server, und nur mit der Unterschrift des Eigentümers. Wer dir eine Profildatei schickt, will dich auf einen falschen
  Server locken.
- Ein neues Konto wird immer als **Funker** freigegeben. Mehr Rechte gibt ein Admin danach.

Mit dem Admin-Setup steht unter **Einstellungen**, unter den Abschnitten, die Karte **Erweitert: Details der Verbindung
(nur im Admin-Setup)**: das Serverprofil, etwa `Meine Staffel: funk.example.org (nur IPv4), TCP-Port 8443`, mit Signatur und
Herkunft, dazu dein Server, dein Konto, deine **Stufe** (Funker, Führung oder Admin) und dein Gerät. IPv4 ist die übliche
Internetadresse aus vier Zahlen wie `203.0.113.10` (A14.2). Hinter dem Gerät steht `(TPM)`, wenn der Schlüssel im
Sicherheitschip liegt, sonst `(Software)`. Ohne TPM geht es auch, nur ist der Schlüssel dann leichter zu stehlen. Mit dem
normalen Setup gibt es diese Karte nicht; Funker brauchen nichts davon.

Das Feld **Adresse** in dieser Karte bleibt leer. Nur wer im selben Heimnetz wie der Server sitzt und über den
Namen nicht durchkommt, trägt dort die LAN-Adresse des Servers ein, also seine Adresse im Heimnetz hinter dem Router, etwa
`192.168.1.250`; geprüft wird trotzdem der Pin aus dem Profil.

### A2.4 Was oben im Fenster steht

| Anzeige | Bedeutung |
| --- | --- |
| `Getrennt` | Nicht verbunden. **Verbinden** klicken. |
| `Verbinde...` | Die Verbindung wird aufgebaut. |
| `Gerät anmelden` | Der Server kennt dieses Gerät noch nicht; mit deinen Namen stellt der Client einen Antrag. |
| `Wartet auf Freigabe` | Dein Antrag ist da; ein Admin muss freigeben. |
| `Abgleich` | Angemeldet, der Stand wird abgeglichen. |
| `Verbunden` | Alles in Ordnung, Sprache über UDP. |
| `Verbunden (Tunnel)` | Verbunden, aber UDP kommt nicht durch; die Sprache läuft mit etwas mehr Verzögerung im Steuerkanal mit. Bleibt das so, sag es dem Betreiber des Servers. |

Neben dem Zustand zeigt die Titelleiste, wer gerade spricht (`spricht: ...`), rechts die Knöpfe **Wer ist da** (mit der
Zahl, etwa `Wer ist da (6)`) und **Funkfeld**. Ist das Fenster schmaler als 900, zeigen die beiden Knöpfe nur ihr Zeichen
und die Zahl; unter 700 fällt `spricht: ...` weg. Der Name steht dann im Hilfetext, wenn die Maus darauf ruht.

**Beim Start verbinden** ist ab Werk aus, bis dein Gerät freigegeben ist; dann geht es einmal von selbst an (A2.3). Du
findest es unter **Einstellungen** (Strg+2), **Funkfeld und Darstellung**, **Erweitert**, Gruppe **Beim Start**.

### A2.5 Das Hauptfenster im Überblick

Seit 0.10.0 hat das Hauptfenster links nur noch zwei Seiten, dazu, wenn es sie für dich gibt, Raid und Verwaltung. Die
früheren Bereiche **Verbindung**, **Audio** und **Meine Kanäle** (in älteren Fassungen **Netze und Rollen**) gibt es nicht
mehr: Was man zum Funken braucht, steht auf **Start**, alles Übrige unter **Einstellungen**: unter **Senden** offen, sonst
im zugeklappten Bereich **Erweitert:**.

| Seite | Taste | Was darin ist |
| --- | --- | --- |
| Start | Strg+1 | Verbinden und Trennen, der Antrag, Server und Sprache als Stecker; darunter **Sprechen** (Mikrofon, Sprechtaste, Senden mit Stimme) und **Hören** (Ausgabe, Funk-Lautstärke, Headset oder Lautsprecher, die Hörprobe), unten **Senden** (Pegelbalken mit Griffen, **Einmessen**, die Schalter der Klangmodule, **Erweitert: alle Einstellungen zum Senden**). Seit 0.11.2 nur das Audio; die Karte **Einsteigen** (bis 0.11.1 mit Einheiten in der Flotte) gibt es nicht mehr (A3, A4, A7.1) |
| Einstellungen | Strg+2 | oben **Ich** (Namen, Rollenwünsche, für Admins "Heute als Funker"), darunter die Abschnitte **Tasten**, **Empfangen**, **Senden**, **Funkfeld und Darstellung**, **Sprecheranzeige** (seit 0.11.9), **Meldungen** (seit 0.12.0) und **Fehlersuche**, je mit dem zugeklappten Bereich **Erweitert:**; unter **Senden** stehen die Schalter und die Funkschwelle offen darüber (A9 und die folgenden Absätze) |
| Sprachaktivierung | Strg+T | Unterpunkt unter Einstellungen, wenn ein Kanal gerade mit Sprachaktivierung sendet oder du die Seite mit Strg+T geöffnet hast: die Grafik mit Schwelle und Reglern (A5.2) |
| Raid | Strg+5 | die Funkfeld-Zuteilung (bis 0.11.1 das Brett), für die Einsatzleitung (A12); seit 0.12.0 auch für alle, die Meldungen senden dürfen, ohne Auftrag dann nur mit dem Feld **Meldungen** (A12.8) |
| Verwaltung | Strg+9 | nur mit dem Admin-Setup und nur für Admins |

Weitere Tasten: Strg+6 öffnet und schließt **Wer ist da**, Strg+F holt das Funkfeld (auch aus dem Infobereich, A6.6).
Seit 0.12.0 steht rechts in der Titelleiste, solange die Leitung fragt, wer bereit ist, der grüne Knopf **Bereit melden**;
nach dem Klick steht dort `gemeldet` (A4.6, A6.9).
Strg+3 und Strg+4 bewirken seit 0.10.0 nichts mehr, und Strg+T klappt nichts mehr auf, sondern öffnet die Seite
Sprachaktivierung, solange **Senden mit Stimme** auf Start an ist, auch wenn gerade kein Kanal mit Sprachaktivierung sendet
(etwa vor dem Verbinden). Dann steht der Unterpunkt da, bis du die Seite verlässt. Ist **Senden mit Stimme** aus, tut Strg+T
nichts.

**Knöpfe.** Seit 0.11.2 sind alle Knöpfe flach und neutral, ohne Glas (von 0.6.0 bis 0.11.1 waren die meisten aus Glas).
Nur die Hauptaktion einer Karte, etwa **Verbinden**, ist flach in der Leitfarbe (dunkles Petrol), und ein gewählter
Umschalter, etwa **Headset** oder **Lautsprecher**, steht gedämpft in der Leitfarbe (bis 0.12.0 war beides weinrot). Fährt
die Maus darüber, wird ein Knopf etwas heller, gedrückt etwas dunkler.

**Die Abschnitte der Einstellungen.** Jeder Abschnitt hat ein Zeichen, einen Namen und den Kasten **Läuft von selbst**
mit dem, was die Automatik dort ohne dein Zutun erledigt, etwa unter **Senden** `Läuft von selbst: Deine Stimme kommt
gleich laut an, leises Reden im Raum bleibt draußen.` Nur die **Fehlersuche** hat keinen Kasten, sondern den Satz `Nur
nötig, wenn etwas nicht geht.`; darunter steht seit 0.11.2 offen der Knopf **Audio starten** (bis 0.11.1 auf Start, A3.2),
daneben der Grund, wenn sich das Audio nicht starten ließ. Darunter liegt zugeklappt der Bereich **Erweitert:** mit dem,
was selten jemand anfasst.
Seit 0.11.4 ist er eine umrandete Karte über die ganze Breite, wie die ausklappbaren Einstellungen in Windows 11: oben
links fett **Erweitert:**, darunter in Grau der Stand in Klammern und der Inhalt, etwa `(ab Werk) Feste Verstärkung,
Mithören, Klangfarbe` oder `(1 selbst gestellt) ...`, rechts ein Pfeil (zu nach unten, offen nach oben). Ein Klick auf
die Karte klappt sie auf; der Inhalt steht dann in derselben Karte unter einer Trennlinie, der Rahmen wird heller. Bis
0.11.3 war es eine kleine graue Zeile mit Pfeil.
Darin stehen die Einstellungen unter Zwischenüberschriften und ganz unten der Knopf **Zurück auf
Werkseinstellung**, daneben ein Satz, was du dort selbst gestellt hast, etwa `1 selbst gestellt: Wärme +2 dB. Alles andere
steht ab Werk.` Der Knopf stellt nur zurück, was unter diesem **Erweitert:** steht, danach laufen dort alle Automatiken
wieder. Was JG TacRelay gelernt hat (das Einmessen, die gelernte Stimme) und was an deinem Konto oder am Server steht
(hören je Kanal, Lautstärke je Person, deine Zeichen) bleibt dabei, wie es ist. Ob ein Abschnitt auf- oder zugeklappt
ist, merkt sich JG TacRelay.

**Senden** ist seit 0.11.1 anders gebaut als die übrigen Abschnitte: Die Schiebeschalter der Klangmodule stehen offen,
ohne Aufklappen, wie bis 0.9.3 auf der Seite Audio, je mit ihrem kurzen Satz. Links stehen **Echounterdrückung**
(Automatisch, An, Aus), **Echoschutz** und **Rauschunterdrückung** mit **Standard** und **Stark**, rechts
**Automatische Lautstärke**, **Sprachaktivierung** und **Mindest-Funklautstärke**. Darunter über die ganze Breite steht
die **Funkschwelle**: der Pegelbalken mit seinen Griffen für Mindest-Funklautstärke und Obergrenze, daneben **Einmessen**,
darunter **Genau einstellen ...** für die Seite Sprachaktivierung (A5.2). Diese Schalter zeigen ihren Stand selbst; sie
zählen nicht in der Kopfzeile von **Erweitert:**, und **Zurück auf Werkseinstellung** lässt sie, wie sie stehen. Gezählt
und zurückgestellt werden dort Mithören und die Klangfarbe. Die Seite Sprachaktivierung hat ihren eigenen Knopf
**Standardwerte** (A5.3): Was du dort genau eingestellt hast, zählt unter **Erweitert:** nicht mit, und **Zurück auf
Werkseinstellung** fasst es nicht an (seit 0.11.1, Q117; der Hovertext am Knopf sagt es). Seit 0.11.2 stehen der
Pegelbalken mit den Griffen, **Einmessen** und vier dieser Schalter auch unten auf Start (A3); beide Stellen schalten
dasselbe.

| Abschnitt | Unter Erweitert |
| --- | --- |
| **Ich** | Zeichen und Sichtbarkeit, für Führung und Einsatzleitung das Ergänzen des Rollenkatalogs (A9) |
| **Tasten** | Sprechtaste mit Nachlauf, Kanal vor und zurück, Melde-Taste, weitere Tasten (Funkfeld ablegen/holen, Dauersender stumm, Mikro stumm, seit 0.12.0 Bereit), Einsatzleitung (nur mit Auftrag), Funk-Klick (A4, A5.5) |
| **Empfangen** | je Kanal hören, Hören auf links, beide oder rechts und Lautstärke, die Global-Zeile, die Lautstärke je Person, Dauersender und Pegelausgleich (A3.5, A7.4) |
| **Senden** | **Feste Verstärkung einmessen** (nur ohne automatische Lautstärke nötig), Mithören, Klangfarbe; offen darüber Echounterdrückung, Echoschutz, Rauschunterdrückung, automatische Lautstärke, Sprachaktivierung, Mindest-Funklautstärke und die Funkschwelle mit **Einmessen** und **Genau einstellen ...** (A3, A5) |
| **Funkfeld und Darstellung** | Durchsichtigkeit, Skalierung, Funkfeld beim Start, Größe nach Inhalt, Meldezeile (Stelle im Raster, Monitor, Probe; seit 0.11.1), Tooltips, Audio beim Start öffnen, Beim Start verbinden (A6) |
| **Sprecheranzeige** (seit 0.11.9) | Größe, Durchsichtigkeit der Schrift, Zeilen, Nachhalten, Ausblenden, eigene Sendung, Monitor, die Vorschau **So sieht sie aus**; offen darüber der Schalter **Sprecheranzeige an** und unter **Wo im Bild** die Regler **Waagerecht** und **Senkrecht** (A6.8) |
| **Meldungen** (seit 0.12.0) | Grundgröße, Ausführung (Schlicht, Metall, Nur Schrift), Bild beim Stopp (Handschuh, Xeno), Ton; offen darüber unter **Wo im Bild** die Regler **Waagerecht** und **Senkrecht**; einen Schalter zum Abschalten gibt es nicht (A6.9) |
| **Fehlersuche** | der Klangtreiber (etwa `Klang: WASAPI`) und Warnungen beim Start, Klangprotokoll, der Ordner der Einstellungen, Stimmproben |

**Die Statuszeile Mikrofon.** Ganz unten, über der Fußzeile, steht auf jeder Seite die Zeile **Mikrofon**: rechts der
Pegel deines Mikrofons mit der Linie der Sprachaktivierung und eine Marke. Das ist seit 0.10.0 die eine Stelle, an der du
siehst, ob du sendest; bis 0.9.3 stand das in der Leiste **Sprachaktivierung**, die sich unten aufklappen ließ.

| Marke | Bedeutung |
| --- | --- |
| `SENDET` (grün) | Was du sagst, geht gerade hinaus. |
| `Ruhe` | Verbunden, aber gerade geht nichts hinaus. |
| `Getrennt` | Nicht verbunden; es geht nichts hinaus, egal was der Pegel zeigt. |

Ruht die Maus auf der Zeile, erklärt ein Hilfetext, was Farben, Linie und Marke heißen. Die Linie steht dort, wo die
Sprachaktivierung sie hat, auch solange noch kein Audio läuft.

Sprichst du und es geht nichts hinaus, steht seit 0.11.2 nur noch in zwei Fällen links neben dem Pegel ein Satz, warum
(Tabelle). Die Sätze `Ruhe: es wird nichts gesendet`, `Stummgeschaltet: es wird nichts gesendet` und `Nicht verbunden: es
wird nichts gesendet` gibt es nicht mehr, weder hier noch am Pegelbalken noch über dem Spiel (Vorgabe vom 13.10.2026). Dass
nichts hinausgeht, zeigt der Balken: Er wird nicht grün, und wo er senden würde, wird er gelb. Ist kein Kanal zum Senden
gewählt oder nur einer auf Ruhe, bist du getrennt oder stummgeschaltet, sagen es die Marke (`Ruhe` oder `Getrennt`) und
dieses Gelb; einen Kanal zum Senden wählst du im Funkfeld, verbunden wird auf Start. In der Meldezeile über dem Spiel
steht keiner dieser Sätze (A6.7).

| Satz | Bedeutung | Was du tust |
| --- | --- | --- |
| `Kanal braucht die Sprechtaste - keine belegt` | Dein Sendekanal sendet nur mit der Sprechtaste, aber du hast keine belegt. Neu in 0.10.0. | Sprechtaste belegen (A4.1) |
| `Stimme unter der Mindest-Funklautstärke` | Du sprichst leiser als die Mindest-Funklautstärke; leises Reden im Raum bleibt absichtlich draußen. Neu in 0.10.0. | lauter oder näher am Mikrofon sprechen, oder unter **Einstellungen**, **Senden**, **Funkschwelle** neu einmessen (A5.2) |

Gelb ist der Pegel, wenn du über der Linie sprichst und es trotzdem nicht hinausgeht; grau, wenn du zu leise bist.

**Die Fußzeile** nennt rechts die Tasten, die gerade gelten, etwa `Strg+1 Start  Strg+2 Einstellungen  Strg+6 Wer ist
da  Strg+F Funkfeld`, dazu Strg+5 und Strg+9, wenn es Raid und Verwaltung für dich gibt. Links stehen Warnungen beim
Start, etwa wenn die globalen Tasten nicht eingerichtet werden konnten, die letzte Fehlermeldung der Klangmaschine und,
während "Heute als Funker" läuft, `als Funker bis 04:00`. Der Klangtreiber (`Klang: WASAPI`) steht seit 0.10.0 nicht
mehr dort, sondern unter **Einstellungen**, **Fehlersuche**, **Erweitert**.

**Fenstergröße.** Auf Start ist das Hauptfenster seit 0.10.0 mindestens 900 x 900 groß, damit Start ohne Rollen ins
Fenster passt (bis 0.11.1 auch der erste Start). Seit 0.11.2 passt Start mit **Senden** unten in diese Größe; rollen muss
es dort nur beim ersten Start (mit dem Schritt Sprechtaste) und mit einer Gerätewarnung. Auf einem Bildschirm, der dafür
zu niedrig ist (etwa ein Laptop mit 150 % Skalierung oder Full HD mit 125 %), nimmt es die Höhe, die da ist, und Start
rollt. Die Einstellungen dürfen rollen; in der Größe ab Werk (960 x 700) siehst du dort unter **Ich** noch **Tasten** und
**Empfangen**.

**Wer ist da auf Start.** Öffnest du auf Start die Spalte **Wer ist da** (A8), während das Fenster schmaler als 1240 ist
(also in der kleinsten Größe, 900 breit), wird das Fenster um die Spalte breiter, nach rechts; Start bleibt dabei so breit
wie vorher, mit Sprechen und Hören nebeneinander, ohne Rollen. Ist rechts auf dem Bildschirm kein Platz, rückt das Fenster
so weit nach links wie nötig, bleibt aber auf seinem Bildschirm, dem, auf dem es zum größten Teil steht. Ist der Bildschirm
auch dafür zu schmal (etwa Full HD mit 175 % oder ein Bildschirm hochkant), bleibt das Fenster, wie es ist, und die Spalte
legt sich wie eine Schublade rechts über Start, mit einem Schatten an ihrer linken Kante; Start darunter behält seine
Breite. Was unter der Schublade liegt, die Spalte **Hören**, erreichst du dann nicht mit der Tastatur; lag der Fokus dort,
springt er in das Suchfeld der Schublade. Schließt du die Spalte, geht das Fenster auf die Breite und an die Stelle von
vorher zurück, aber nur, wenn du es in der Zwischenzeit nicht selbst verschoben oder größer oder kleiner gezogen hast; dann
bleibt es, wie du es hast. Maximieren und Wiederherstellen zählen dabei nicht als Änderung. Maximiert steht die Spalte wie
bisher neben dem Inhalt; öffnest oder schließt du sie dort, gilt das für die Größe, die beim Wiederherstellen kommt: Das
Fenster kommt dann gleich verbreitert mit der Spalte daneben oder wieder in der Breite von vorher. Gemerkt wird die Größe
ohne die Spalte, wenn das Fenster für sie breiter wurde, der nächste Start beginnt also nicht breit; ziehst du es danach
schmaler als 1240, liegt die Spalte als Schublade darüber und nimmt keine Breite, dann gilt die Breite, wie sie ist. Auf
den übrigen Seiten und wenn du das Fenster selbst 1240 oder breiter gezogen hast, steht die Spalte wie bisher neben dem
Inhalt, und gemerkt wird deine Größe. Bis zu dieser Änderung (Q93 a) blieben Start in der kleinsten Größe neben der Spalte
nur gut 260 Punkte Breite, Sprechen und Hören standen untereinander, und Start rollte.


## A3 Start: Sprechen und Hören

Seit 0.10.0 steht alles, was du zum Funken brauchst, auf **Start** (Strg+1): unter Verbinden und den zwei Steckern in
zwei Spalten links **Sprechen**, rechts **Hören**. Bleibt für Start weniger als etwa 560 Punkte Breite, steht **Hören**
unter **Sprechen**: ohne die Spalte **Wer ist da** unter etwa 850 Fensterbreite. Öffnest du die Spalte, wird das Fenster
breiter oder sie legt sich als Schublade über Start (A2.5), Start bleibt in zwei Spalten; nur maximiert auf einem Bildschirm,
der schmaler als etwa 1200 ist, steht **Hören** dann unter **Sprechen**. Darunter steht seit 0.11.2 über die ganze Breite
**Senden**. Bis 0.9.3 lag das meiste davon im Bereich **Audio**, den es nicht mehr gibt.

Auf Start steht seit 0.11.2 nur das Audio. Weg sind die Karte **Einsteigen** (A7.1), die Meldung
`Mikrofon und Wiedergabe sind zu.` mit **Audio starten** (der Knopf steht jetzt unter **Einstellungen**, **Fehlersuche**,
A3.2) und der Satz `Pegel und SENDET stehen unten in der Zeile Mikrofon.`

| Spalte | Was darin steht |
| --- | --- |
| **Sprechen** | **Mikrofon** (A3.2), **Sprechtaste** mit dem Namen deiner Taste und **Ändern** (A4.1), **Senden mit Stimme** (A5.2) |
| **Hören** | **Ausgabe** (A3.2), **Funk-Lautstärke** (A3.3), **Ich höre mit** mit **Headset** und **Lautsprecher** (A3.1), **So hören dich die anderen** mit der **Hörprobe** (A3.4) |
| **Senden** (unten, seit 0.11.2) | der Pegelbalken, live, mit zwei Griffen, die du mit der Maus ziehst (links die Mindest-Funklautstärke, rechts die Obergrenze), daneben **Einmessen**; darunter die Schalter **Rauschunterdrückung**, **Echoschutz**, **Automatische Lautstärke** und **Mindest-Funklautstärke** und in derselben Reihe der Knopf **Erweitert: alle Einstellungen zum Senden**, der die Einstellungen öffnet (Strg+2) (A2.5, A5.2) |

Unter **Senden** stehen die Schalter nur mit ihrem Namen; was sie tun, sagt der Hovertext. Es sind dieselben wie unter
**Einstellungen**, **Senden** (A2.5): Was du hier schaltest, steht dort genauso. Die Sprachaktivierung steht nicht noch
einmal da, sie ist **Senden mit Stimme** in der Spalte **Sprechen**; die Echounterdrückung folgt **Headset** oder
**Lautsprecher** (A3.1). Der Satz, warum nichts hinausgeht, steht nicht im Balken auf Start, nur in seinem Hovertext und
in der Statuszeile **Mikrofon** (A2.5). Was es zu Senden und Empfangen sonst einzustellen gibt (Rauschfilter Stark,
Echounterdrückung von Hand, **Genau einstellen ...**, Lautstärke je Kanal und je Person, Klangfarbe), steht unter
**Einstellungen** (A2.5).

### A3.1 Headset oder Lautsprecher

**Ich höre mit** auf Start entscheidet über die **Echounterdrückung**. Das ist die Technik, die herausrechnet, was aus
deinen Lautsprechern zurück ins Mikrofon kommt. Beim ersten Start ist noch nichts gewählt; dann ist der Rahmen umrandet,
und darunter steht `Headset: Echounterdrückung aus. Lautsprecher: an, sonst hören die anderen sich selbst.` Verbinden
kommt erst, wenn du gewählt hast (A2.2). Danach stellst du es auf Start jederzeit um.

| Du hörst mit | Echounterdrückung | Warum |
| --- | --- | --- |
| Headset oder Kopfhörer | aus | Es gibt kein Echo. Ohne die Unterdrückung kommen deine Wortanfänge auch bei laufendem Spielton sauber durch. |
| Lautsprechern | an | Sonst hören die anderen sich selbst aus deinem Mikrofon. Ohne Headset ist sie Pflicht. |

Seit 0.10.0 hat die Echounterdrückung drei Stufen: **Einstellungen** (Strg+2), **Senden**, oben links
**Echounterdrückung** mit **Automatisch**, **An** und **Aus** (seit 0.11.1 offen, ohne Aufklappen). **Automatisch** (ab Werk) folgt der Wahl auf
Start wie in der Tabelle, ohne Wahl ist sie an. **An** und **Aus** gelten, egal was auf Start steht; ein Wechsel zwischen
Headset und Lautsprecher ändert dann nichts. Der Satz darunter sagt, was gerade gilt, etwa `Automatisch folgt sie
Headset oder Lautsprecher auf Start: mit Headset aus, mit Lautsprechern an. Gerade an.` Hattest du vor dem Update die
Echounterdrückung gegen deine Wahl umgestellt, etwa mit Headset an, steht sie nach dem Update auf **An** beziehungsweise
**Aus**, damit deine Umstellung nicht verloren geht.

### A3.2 Mikrofon und Ausgabe

Auf Start wählst du in der Spalte **Sprechen** das **Mikrofon** und in der Spalte **Hören** die **Ausgabe**. Wählst du
nichts, nimmt JG TacRelay die Geräte, die Windows als Standard für Kommunikation führt.

- Steckst du ein Headset ein oder ab, liest JG TacRelay die Geräte seit 0.10.0 von selbst neu; den Knopf **Geräte neu
  lesen** gibt es nicht mehr. Deine Wahl bleibt gemerkt, auch wenn das Gerät gerade fehlt: Die Auswahl zeigt dann den
  Windows-Standard, ohne deine Wahl zu überschreiben.
- Ab Werk öffnet JG TacRelay Mikrofon und Wiedergabe beim Start (**Einstellungen**, **Funkfeld und Darstellung**,
  **Erweitert**, **Audio beim Start öffnen**). Ist das aus, startest du das Audio seit 0.11.2 unter **Einstellungen**,
  **Fehlersuche** mit dem Knopf **Audio starten**; er steht dort offen über **Erweitert:** und ist gesperrt, solange das
  Audio läuft. Auf Start steht dazu nichts mehr (bis 0.11.1 in der Spalte **Hören** `Mikrofon und Wiedergabe sind zu.`
  mit **Audio starten**). Lässt sich Audio nicht starten, steht der Grund neben dem Knopf, etwa `Audio ließ sich nicht
  starten: ...` mit dem ersten Satz der Meldung von Windows; ganz steht sie im Hilfetext, wenn die Maus darauf ruht, und
  unter **Fehlersuche**, **Erweitert**.

Lässt sich ein Gerät nicht öffnen, steht in der Spalte **Hören** eine Warnung mit **Erneut versuchen**, etwa
`Mikrofon aus: ... Die Kanäle sind trotzdem zu hören; gesendet wird erst mit Mikrofon.` Ein möglicher Grund: Windows
sperrt in seinen Datenschutzeinstellungen den Mikrofonzugriff für Programme.

Unter **Einstellungen**, **Senden** (seit 0.11.1 offen, ohne Aufklappen; seit 0.11.2 der Schalter auch auf Start unter
**Senden**): **Rauschunterdrückung** (RNNoise, ab Werk an) nimmt Hintergrundgeräusche wie Lüfter heraus. Unter den
Knöpfen **Standard** und **Stark** steht, was gerade wirkt, ab Werk `Der Standardfilter wirkt.`. Effekte gibt es keine;
die Klangfarbe deiner Stimme stellst du seit 0.9.3 selbst ein, ab Werk ist sie neutral (A3.6).

### A3.3 Funk-Lautstärke

Seit 0.10.0 steht auf Start in der Spalte **Hören** der Regler **Funk-Lautstärke**, daneben der Wert, etwa `80 %`. Er
stellt ein, wie laut du den ganzen Funk hörst: alle Kanäle, die Klicks und die Hörprobe zusammen. Windows und das Spiel
bleiben, wie sie sind; so stellst du den Funk gegen das Spiel ein, ohne den Mixer von Windows. Der Regler geht in
Schritten zu 5 %. Ab Werk steht er ganz rechts auf 100 %, so laut wie bis 0.9.3; ganz links ist der Funk stumm. Die Hälfte
ist etwa 12 dB leiser. Einzelne Kanäle und Personen stellst du unter **Einstellungen**, **Empfangen**, **Erweitert** ein
(A3.5).

### A3.4 Hörprobe

Die Hörprobe steht seit 0.10.0 nur noch an einer Stelle: auf Start in der Spalte **Hören** unter **So hören dich die
anderen**. Bis 0.9.3 stand sie im Bereich **Audio** und noch einmal in der Leiste **Sprachaktivierung**, und sie ging nur
ohne Verbindung; jetzt geht sie auch verbunden.

**Hörprobe.** Klick auf **Hörprobe** und sprich 5 Sekunden lang. Danach spielt JG TacRelay dir die Aufnahme vor, so wie
die anderen dich empfangen: nach Echounterdrückung, Rauschunterdrückung und Opus, dekodiert und gepuffert wie bei einem
Hörer. Neben dem Knopf steht vorher `Sprich fünf Sekunden, dann hörst du dich. Geht auch verbunden.`, dann, was gerade
läuft, etwa `Aufnahme läuft, sprich jetzt. Noch 4 s.` und danach `So hören dich die anderen. Noch 5 s.`; solange heißt der
Knopf **Abbrechen**.

- Die Aufnahme läuft ohne Schwelle: Du hörst deinen Klang, auch wenn die Sprachaktivierung gar nicht öffnen würde, und
  egal, ob dein Sendenetz auf VA oder Taste steht. Ob deine Schwelle passt, zeigt die Hörprobe deshalb nicht (dazu unten).
- Sie geht auch mit Lautsprechern. Aufnahme und Wiedergabe laufen nacheinander, nie zugleich; die Wiedergabe wird nicht
  wieder aufgenommen.
- Gesendet wird dabei nur, was du mit der Sprechtaste sprichst. Ab dem Klick bis eine halbe Sekunde nach dem Ende der
  Wiedergabe beginnt die Sprachaktivierung keinen neuen Spruch. Brichst du schon während der Aufnahme ab, arbeitet sie
  sofort wieder, denn ohne Wiedergabe gibt es kein Echo. Ein Spruch, der beim Klick schon läuft, geht zu Ende.
- Mikrofon und Wiedergabe müssen laufen. Sonst steht beim Knopf, was fehlt: Ist das Audio gestoppt, seit 0.11.2 `Geht
  erst, wenn das Audio läuft.` (bis 0.11.1 mit dem Verweis auf **Audio starten**, das damals auf Start stand; heute steht
  der Knopf unter **Einstellungen**, **Fehlersuche**, A3.2).
- Das läuft nur auf deinem PC, nicht über den Server.

**Beim Senden mithören.** Seit 0.10.0 steht der Schalter nicht mehr neben der Hörprobe, sondern unter **Einstellungen**
(Strg+2), **Senden**, **Erweitert**, Gruppe **Kontrolle**. Er spielt dir mit, was du gerade wirklich sendest: nur bei
gehaltener Sprechtaste oder wenn die Sprachaktivierung öffnet, und erst ab deinem nächsten Satz, nie mitten in einem. Er
gilt nur, bis du JG TacRelay beendest. Das geht nur mit Headset. Hast du **Lautsprecher** gewählt oder auf Start bei **Ich
höre mit** noch nichts gewählt (A3.1), ist der Schalter gesperrt, denn über Lautsprecher käme deine Stimme verzögert
zurück ins Mikrofon; wechselst du zu Lautsprechern, geht er aus. Unter dem Schalter steht, was gilt:

| Lage | Text unter dem Schalter |
| --- | --- |
| Lautsprecher oder Frage offen | `Nur mit Headset. Über Lautsprecher käme deine Stimme verzögert zurück ins Mikrofon; nimm dafür die Hörprobe.` |
| Headset, Schalter aus | `Aus. Eingeschaltet hörst du mit, was du sendest, gerade nur mit der Sprechtaste.` (oder `... gerade per Sprachaktivierung.`) |
| Headset, Schalter an, Sendenetz auf Taste | `Du hörst mit, was du sendest. Gerade nur mit der Sprechtaste: Halte sie, um dich zu hören.` |
| Headset, Schalter an, Sendenetz auf VA | `Du hörst mit, was du sendest. Gerade per Sprachaktivierung: sobald du über der Schwelle sprichst.` |

Während einer Hörprobe ruht das Mithören.

**Ob deine Schwelle passt**, prüfst du mit dem Headset über **Beim Senden mithören**, oder über das Netz Echo: im
Probefunk (A2.1) oder, wenn der Betreiber es eingerichtet hat, auf dem echten Server. Das Netz Echo zeigt dazu den ganzen
Weg über den Server.

Bis Fassung 0.4.0 war die Hörprobe nur das Mithören, ohne die Sperre für Lautsprecher. Sie spielte also nur, wenn
wirklich gesendet wurde: Stand das Sendenetz auf Taste (A5.2) oder gab es ohne Verbindung gar kein Sendenetz, und hielt
man die Sprechtaste nicht, blieb es still. So erklärt sich der Fehlerbericht von Sarevoc vom 01.10.2026 ("ich höre mich
bei Aktivierung nicht"); welche Auslösung sein Sendenetz damals hatte, ist nicht bekannt. Wie Hörprobe und Mithören technisch arbeiten und was gemessen ist, steht in
E2.9 und E3.11.

### A3.5 Lautstärke je Kanal und je Person

Seit 0.10.0 stehen diese Einstellungen unter **Einstellungen** (Strg+2), **Empfangen**, **Erweitert**; bis 0.9.3 lagen
sie im Bereich **Meine Kanäle**.

**Je Kanal** steht jedes deiner Felder als Zeile: Nummer, Kanal, `SENDET` beim Sendekanal oder sein Zustand, darunter
**hören** zum An- und Abschalten, **Hören auf** mit **Links**, **Beide** und **Rechts** und die Lautstärke. Ab Werk
spielen alle Kanäle auf beiden Ohren. Darunter steht **Global** mit "hört immer", ebenfalls mit **Hören auf** und
Lautstärke. Ob ein Kanal mit Sprachaktivierung oder mit der Taste sendet, stellst du nicht hier ein, sondern im Funkfeld
(A6.2). **hören** steht am Server und gilt also auch an einem anderen PC; Seite und Lautstärke gelten für diesen PC. Die
Lautstärke eines Kanals stellst du auch auf seinem Feld im Funkfeld ein. Den ganzen Funk auf einmal stellst du mit der
Funk-Lautstärke auf Start (A3.3).

**Je Person:** Ist dir jemand zu laut oder zu leise, stellst du das an seiner Zeile ein, von -30 bis +12 dB. Das gilt in
allen Kanälen und wird an deinem Konto gespeichert, also auch auf einem anderen PC. Seit 0.11.5 geht dasselbe in **Wer ist
da** mit der Wippe hinter dem Namen (A8), je Klick 2 dB; Regler und Wippe zeigen denselben Wert. Bis 0.11.4 sprang ein eben
verstellter Wert beim nächsten Funkspruch auf den Stand der Anmeldung zurück, wenn für diese Person schon einer gespeichert war.

Darunter stehen **Dauersender** (Auto Dauersend-Dämpfung mit der Stufe, wie weit andere Dauersender bei dir höchstens
leiser werden) und **Pegelausgleich**; beide sind ab Werk an. Den Schalter **Leises Reden aussperren** gibt es seit 0.10.0
nicht mehr; die Funktion ist aus.

### A3.6 Klangfarbe

Seit 0.9.3 kannst du den Klang deiner Stimme etwas färben. Verstellen musst du nichts: Ab Werk ist die Klangfarbe neutral, und
deine Stimme geht so hinaus, wie sie aus Mikrofon und Rauschunterdrückung kommt.

Wer es doch will: **Einstellungen** (Strg+2), **Senden**, **Erweitert**, rechts die Gruppe **Klangfarbe** (seit 0.11.1;
0.10.0 und 0.11.0 rechts unter der Lautstärke im Klappbereich, bis 0.9.3 im Bereich **Audio** unter der automatischen
Lautstärke). Dort gibt es drei Regler,
je von -6 bis +6 dB in ganzen Schritten, der Name darüber, der Wert daneben. Neutral steht der Knauf in der Mitte, an der
kleinen Marke; die Spur ist nicht eingefärbt, denn links heißt hier nicht "aus", sondern weniger:

| Regler | Was er tut | Mehr, wenn | Weniger, wenn |
| --- | --- | --- | --- |
| **Wärme** | macht die Stimme voller und runder, nicht tiefer | du über ein Headset dünn oder blechern klingst | es nah am Mikrofon dröhnt |
| **Klarheit** | hebt die Mitlaute, also das, was Wörter verständlich macht | dich die anderen im Gefecht schlecht verstehen | deine Stimme spitz oder anstrengend klingt |
| **Schärfe** | betont oder dämpft die Zischlaute (s, sch, z) | du dumpf klingst | dein Mikrofon zischt |

- Alle drei ändern nur die Klangfarbe, nicht die Lautstärke. Die Sprachaktivierung und die Pegelanzeige sehen deine Stimme wie
  vorher.
- Die Hörprobe auf Start (A3.4) hört die Klangfarbe mit. Stell immer nur einen Regler um und hör dann nach.
- **Zurück auf neutral** stellt nur die drei Regler auf 0. Ist etwas verstellt, zählt es die Kopfzeile von **Erweitert:**
  mit, etwa `Erweitert: (1 selbst gestellt) ...`, und der Satz neben **Zurück auf Werkseinstellung** nennt es, etwa
  `1 selbst gestellt: Wärme +2 dB. Alles andere steht ab Werk.` **Zurück auf Werkseinstellung** stellt alles unter
  **Erweitert:** von Senden zurück, die Klangfarbe mit; die offenen Schalter darüber bleiben, wie sie stehen (A2.5).

Wie die Klangfarbe technisch arbeitet und was gemessen ist, steht in E2.11 und E3.4. Am echten Mikrofon ist sie noch nicht
ausprobiert (1.8).


## A4 Sprechtaste und Joystick

### A4.1 Taste belegen

Ab Werk ist **keine Sprechtaste** belegt. Seit 0.10.0 belegst du sie auf **Start** (Strg+1), in der Spalte **Sprechen**:

- **Beim ersten Start** ist der Rahmen **Sprechtaste** umrandet, darin `Drück jetzt deine Sprechtaste` und ein Feld
  `Wartet auf deine Taste ...`. JG TacRelay wartet dort schon, ohne Frist, solange Start vorn und das Fenster aktiv ist.
  Drück die gewünschte Taste: eine Taste der Tastatur, eine Maustaste oder einen Knopf oder Hut an deinem Joystick. Weil
  der Schritt von selbst wartet, zählt dabei nicht, womit du das Fenster bedienst: Tab, die Pfeile, Eingabe, Leertaste,
  Pos1, Ende, Bild auf und Bild ab und ein Klick mit der linken, rechten oder mittleren Maustaste. Strg, Alt, Umschalt und
  die Windows-Taste zählen nur allein, und zwar beim Loslassen; mit einer anderen Taste zusammen (etwa Alt+Tab oder Strg+2)
  ist es ein Kürzel und zählt gar nicht. Sonst würde Alt beim Wechsel zu Discord zur Sprechtaste. Willst du eine dieser
  Tasten, belegst du sie danach mit **Ändern** oder unter **Einstellungen**. Esc bricht ab; dann steht dort **Belegen**,
  und ein Klick darauf wartet wieder. **Später** lässt die Sprechtaste weg; Start erinnert dich danach daran (unten).
- **Mit Sprechtaste** steht die Taste als kleine Marke mit ihrem Namen da, etwa `Maustaste 4 (zurück)`; mit dem Kreuz
  daran entfernst du sie. **Ändern** wartet auf eine neue Taste und ersetzt damit alle bisherigen Sprechtasten; Esc oder
  **Abbrechen** lässt alles, wie es ist. **Ändern** und **Belegen** in der Warnung nehmen die erste Taste, die du drückst,
  und warten 10 Sekunden, auch wenn du dafür in ein anderes Fenster klickst; so belegst du etwa die rechte Maustaste mit
  einem Klick außerhalb von JG TacRelay. Verlässt du Start, endet das Warten.
- **Ohne Sprechtaste** steht dort `Noch keine Sprechtaste: In Kanälen auf Taste, etwa Global, geht nichts hinaus.` mit
  **Belegen**.

Mehrere Sprechtasten, etwa eine am Stick und eine an der Tastatur, belegst du unter **Einstellungen** (Strg+2),
**Tasten**, **Erweitert**, Gruppe **Sprechtaste**: bei **Sprechtaste** auf **Belegen**, dann die Taste drücken. Dort
bricht das Warten nach 10 Sekunden ohne Taste von selbst ab. Bis 0.9.3 stand das auf der Karte **Globale Tasten**.

Gut zu wissen:

- "Global" heißt: Die Taste wirkt auch, wenn Star Citizen vorne ist. Das Spiel bekommt die Taste trotzdem mit. Nimm also
  eine Taste oder einen Knopf, der im Spiel nichts tut, oder eine, deren Doppelbelegung dich nicht stört.
- Linke, rechte und mittlere Maustaste zählen nur außerhalb von JG TacRelay; ein Klick im Programm bedient die
  Oberfläche. Maustaste 4 und 5 lassen sich auch im Fenster belegen. Bis 0.9.3 galt das nur für die linke.
- Mehrere Tasten je Funktion sind erlaubt.
- Eine Taste gehört nur einer Funktion. Belegst du sie woanders, wandert sie dorthin.
- Sprichst du in einen Kanal auf Taste und ist keine Sprechtaste belegt, sagt es die Statuszeile: `Kanal braucht die
  Sprechtaste - keine belegt` (A2.5).
- Den Knopf **Sprechtaste halten** zum Ausprobieren gibt es seit 0.10.0 nicht mehr.

Beim Drücken und Loslassen der Sprechtaste hörst du einen kurzen Funk-Klick. Er entsteht in deinem Client und geht nicht
zu den anderen. Seit 0.5.0 ist er ab Werk lauter als zuvor und lässt sich einstellen (A5.5). Der Klick beim Loslassen kommt
seit 0.9.3 erst nach dem Nachlauf der Sprechtaste (A4.5).

Die übrigen Tasten belegst du genauso unter **Einstellungen** (Strg+2), **Tasten**, **Erweitert**, in Gruppen: **Kanal
und Melde-Taste** mit **Sendekanal vor**, **Sendekanal zurück** (A4.3) und **Melde-Taste** (A4.4), **Weitere Tasten** mit
**Funkfeld ablegen/holen** (A6.6), **Dauersender stumm**, **Mikro stumm** und seit 0.12.0 **Bereit** (A4.6), und, nur solange du die Einsatzleitung hast,
**Einsatzleitung** mit **Phase vor** und **Phase zurück** (A12.6). Wie die Sprechtaste sieht Star Citizen jede Taste, die
du hier belegst, ebenfalls; JG TacRelay fängt nichts ab. Nimm deshalb Knöpfe, die im Spiel frei sind, etwa eine
Shift-Ebene der Joystick-Software. Die Tastenbelegungen zählen nicht als "selbst gestellt" (A2.5), und **Zurück auf
Werkseinstellung** lässt sie stehen.

### A4.2 Joystick

JG TacRelay liest Joysticks nur mit, so wie ein Spiel: nicht exklusiv, nichts wird verstellt, HidHide bleibt
unberührt. HidHide ist ein Treiber für Windows, mit dem manche Spieler Joysticks vor bestimmten Programmen verstecken;
JG TacRelay richtet daran nichts ein und ändert dort nichts. Ist ein Stick beim Start nicht eingesteckt, findet
JG TacRelay ihn nach dem Anstecken wieder. Belegt wird wie oben: **Belegen**, dann den Knopf am Stick drücken.

Seit 0.5.0 kann der Joystick alle Tasten-Funktionen, nicht nur die Sprechtaste: sprechen und melden
(solange du den Knopf hältst), das Sendenetz weiterschalten, das Funkfeld ablegen und holen und, für die
Einsatzleitung, die Phasen schalten. Hutrichtungen zählen wie Knöpfe. Bei den Funktionen mit einem einzelnen Druck
(Sendenetz, Funkfeld, Phasen) zählen zwei Drücke desselben Knopfes erst, wenn mindestens 150 Millisekunden dazwischen
liegen; so löst ein prellender Knopf nicht doppelt aus. Ein Knopf oder Schalter, der schon beim Belegen, beim Anstecken
oder beim Start gedrückt war, löst nichts aus, bis du ihn einmal losgelassen hast. Das ist am Code geprüft und mit
Attrappen der Geräteschicht getestet; das Ablegen und Holen des Funkfelds und die Phasen am echten Joystick sind am PC
noch nicht ausprobiert (1.8).

### A4.3 Sendenetz weiterschalten

Unter **Einstellungen** (Strg+2), **Tasten**, **Erweitert**, Gruppe **Kanal und Melde-Taste** gibt es **Sendekanal vor**
und **Sendekanal zurück** (bis 0.9.3 auf der Karte **Globale Tasten** als **Sendenetz vor** und **Sendenetz zurück**). Ab
Werk liegen dort Numpad + und Numpad -. Sie schalten dein Sendenetz zum nächsten oder vorigen deiner Netze weiter, auch
aus dem Spiel heraus. Auch dafür kannst du Knöpfe am Stick belegen.

Hältst du die Sprechtaste, während du weiterschaltest, beginnt der Spruch im neuen Netz erst, wenn die Wahl eine
Viertelsekunde (250 ms) stillsteht. So landen beim Durchschalten keine Wortfetzen in Netzen dazwischen.

Das Funkfeld muss dafür nicht offen sein. Liegt es im Infobereich der Taskleiste (A6.6), nennt der Tooltip des Symbols
dort das Sendenetz, etwa `JG TacRelay - Funkfeld abgelegt - Senden: Crew 3`. Die Meldezeile (A6.7) sagt dazu seit 0.11.2
nichts mehr; bis 0.11.1 nannte sie das neue Sendenetz zwei Sekunden lang, etwa `Sendekanal: Crew 3`.

### A4.4 Melde-Taste

Die Melde-Taste ist eine zweite Sprechtaste für kurze Meldungen, etwa an die Leitung. Solange du sie hältst, sprichst du
in das **Meldenetz**, und dein Sendenetz bleibt, wie es ist: Du musst es weder umschalten noch danach zurückschalten. Die
Sprachaktivierung ruht, solange du die Melde-Taste hältst. Ab Werk ist keine Melde-Taste belegt.

1. **Einstellungen** (Strg+2), **Tasten**, **Erweitert**, Gruppe **Kanal und Melde-Taste**: bei **Melde-Taste** auf
   **Belegen** und die Taste oder den Knopf drücken, wie in A4.1.
2. Darunter steht **Melde-Taste spricht in** mit einer Auswahl. Ohne Wahl ist es das Netz **Kommando**, wenn deine
   Flotte eines hat (die Auswahl heißt dann `ab Werk: Kommando`); sonst wählst du ein Netz der Flotte, nur nicht Global.

Es gibt zwei Voraussetzungen: Du musst verbunden sein, und das Meldenetz muss in einem deiner Felder liegen, in dem du
senden darfst. Fehlt etwas, geht nichts hinaus, und die Meldezeile (A6.7) sagt es drei Sekunden lang in Rot:

| Meldezeile | Bedeutung |
| --- | --- |
| `Melden geht nicht: nicht verbunden` | keine Verbindung zum Server |
| `Melden: kein Meldekanal (Tasten > Erweitert)` | die Flotte hat kein Kommando, und du hast kein anderes Netz gewählt; wählen unter Einstellungen > Tasten > Erweitert (bis 0.10.0 ein längerer Satz mit demselben Weg, bis 0.9.3 `... in den Einstellungen unter Globale Tasten wählen`) |
| `Melden geht nicht: kein Senderecht in <Netz>` | das Meldenetz liegt in keinem deiner Felder, oder du darfst dort nicht senden |
| `Melden geht nicht: <Netz> steht auf Ruhe` | das Meldenetz liegt in deinen Feldern nur auf Ruhe |

Liegt das Funkfeld im Infobereich, steht während der Meldung `Melden: an Kommando` in der Meldezeile. Der Funk-Klick (A5.5)
kommt auch bei der Melde-Taste; hältst du die Sprechtaste schon, klickt die Melde-Taste nicht noch einmal. Lässt du eine
der beiden Tasten los und drückst gleich die andere, noch im Nachlauf (A4.5), endet der alte Spruch an seinem Ziel, der neue
beginnt am anderen, und es klickt für beide, in beiden Richtungen: von der Sprechtaste zur Melde-Taste wie von der
Melde-Taste zur Sprechtaste. Am Code
geprüft und in Tests mit Attrappen; die Melde-Taste an Kommando am echten PC ist noch nicht ausprobiert (1.8).

### A4.5 Nachlauf der Sprechtaste

Seit 0.9.3 sendet JG TacRelay nach dem Loslassen der Sprechtaste noch einen Augenblick weiter, ab Werk 100 Millisekunden,
so als hieltest du die Taste. Deine Stimme braucht durch Echounterdrückung, Rauschunterdrückung und Funkkodierung etwas
Zeit. Bis 0.9.2 hörte das Senden beim Loslassen sofort auf, und wer genau am Wortende losließ, dem fehlte bei den anderen
das Ende der letzten Silbe: im Prüfstand gemessen bis 25 Millisekunden mit der Rauschunterdrückung Standard und bis 35 mit
Stark.

- Der Nachlauf gilt auch für die Melde-Taste (A4.4); die Meldung bleibt dabei im Meldenetz.
- Drückst du im Nachlauf dieselbe Taste wieder, geht derselbe Spruch nahtlos weiter, ohne neuen Klick.
- Drückst du im Nachlauf die andere Taste, also nach der Sprechtaste die Melde-Taste oder nach der Melde-Taste die
  Sprechtaste, endet der Spruch sofort an seinem Ziel, und der neue beginnt am anderen. Du hörst dabei beide Klicks, den
  beim Loslassen und den beim Drücken. Was du vorher gesagt hast, geht ganz an das alte Ziel, nichts davon ins andere Netz.
- **Mikro stumm** beendet den Nachlauf sofort, ebenso ein Mikrofon, das ausfällt oder wechselt.
- Der Funk-Klick beim Loslassen kommt erst nach dem Nachlauf.
- Die Sprachaktivierung hat ihren eigenen Nachlauf (A5.1) und bleibt davon unberührt.

Verstellen musst du nichts. Wer es doch will: **Einstellungen** (Strg+2), **Tasten**, **Erweitert**, Gruppe
**Sprechtaste**. Unter der Zeile **Sprechtaste** steht der Regler **Nachlauf der Sprechtaste** von 0 bis 1000
Millisekunden in Schritten zu 10, der Name darüber, der Wert daneben. Ist er verstellt, zählt ihn die Kopfzeile von
**Erweitert:** mit (`Erweitert: (1 selbst gestellt) ...`), und **Zurück auf Werkseinstellung** unten unter **Erweitert:**
stellt ihn mit dem Funk-Klick zurück (A2.5). Bis 0.9.3 stand der Regler in einem eigenen Klappbereich unter der Zeile
Sprechtaste auf der Karte **Globale Tasten**.

Mehr Nachlauf, wenn dir die anderen sagen, dass Wortenden fehlen, oder wenn du die Taste gern schon beim letzten Wort
loslässt. Weniger, wenn nach dem Loslassen noch Geräusche hinterhergehen, etwa ein Atmer oder das Klacken der Taste. Im
Prüfstand gemessen und mit Tests geprüft; am echten PC ist der Nachlauf noch nicht ausprobiert (1.8).

### A4.6 Die Taste Bereit

Fragt die Leitung, wer bereit ist (die **Bereit-Abfrage**, A12.8), steht über dem Spiel das Bild **Bereit melden** (A6.9),
und du meldest dich auf einem von zwei Wegen bereit:

- mit der Taste **Bereit**: **Einstellungen** (Strg+2), **Tasten**, **Erweitert**, Gruppe **Weitere Tasten**, Zeile
  **Bereit**, **Belegen**. Ab Werk ist sie unbelegt. Sie wirkt wie die anderen Tasten auch, wenn Star Citizen vorn ist, und
  geht auf Tastatur, Maus und Joystick (A4.2). Ohne offene Abfrage tut sie nichts;
- mit dem grünen Knopf **Bereit melden** rechts in der Titelleiste des Hauptfensters. Er steht nur da, solange eine
  Abfrage offen ist.

Danach steht auf dem Knopf `gemeldet`, und über dem Spiel steht `gemeldet` unter dem Bild. Zweimal ist wie einmal. Wer die
Abfrage selbst gestartet hat, bekommt keinen Knopf und zählt nicht mit. Die Abfrage endet, wenn die Leitung sie schließt,
spätestens nach 2 Minuten; dann ist der Knopf fort. Eine neue Abfrage fragt neu.


## A5 Sprachaktivierung, einfach erklärt

### A5.1 Wie sie entscheidet

Sprachaktivierung heißt: JG TacRelay sendet von selbst, sobald du sprichst, und hört auf, wenn du still bist. Stell dir
eine Tür vor, die nur aufgeht, wenn zwei Dinge zugleich stimmen:

1. **Es ist laut genug.** Dein Mikrofon liefert einen **Pegel**, gemessen in dBFS (Dezibel unter Vollaussteuerung):
   0 dBFS ist das Lauteste, was das Mikrofon liefern kann, je negativer, desto leiser. Hinter der Verarbeitung liegt
   Sprache etwa zwischen -35 und -15 dBFS (Angabe aus einem Kommentar im Code, nicht gemessen). Die Tür öffnet erst über
   einer **Schwelle**, im Bild eine waagerechte Linie.
2. **Es klingt nach Sprache.** Eine Spracherkennung (RNNoise) schätzt für jedes Stück Ton, wie sicher es Sprache ist,
   von 0 bis 100 %. Ein Tastenanschlag oder das Knistern einer Chipstüte ist laut, klingt aber nicht nach Stimme.

Damit kein einzelnes Geräusch die Tür aufstößt, muss das in 4 von 10 aufeinanderfolgenden Stücken zu je 10 ms
stimmen. Damit dein erstes Wort trotzdem nicht fehlt, schickt JG TacRelay 30 ms mit, die vor dem Öffnen lagen
(**Vorlauf**). Und damit die Tür nicht zwischen zwei Wörtern zuschlägt, bleibt sie nach dem letzten Laut noch 300 ms offen
(**Nachlauf**).

Wie laut es bei dir ohne Sprache ist, misst JG TacRelay laufend: Das ist der **Rauschboden**, etwa Lüfter und
Raumgeräusch.

### A5.2 Wo du sie einstellst

Seit 0.10.0 gibt es vier Stellen; die Leiste **Sprachaktivierung**, die sich bis 0.9.3 unten im Hauptfenster
aufklappen ließ, gibt es nicht mehr.

- **Start**, Spalte **Sprechen**: der Schalter **Senden mit Stimme** (`Sendet, sobald du sprichst. Aus: nur mit der
  Sprechtaste.`), ab Werk an. Aus heißt: Jeder Kanal sendet nur mit der Sprechtaste. Seit 0.11.2 steht unten auf Start
  unter **Senden** außerdem derselbe Pegelbalken mit Griffen und **Einmessen** wie unter Einstellungen (A3).
- **Die Statuszeile Mikrofon** unten im Hauptfenster (A2.5): dein Pegel mit der Linie, `SENDET` oder `Ruhe` und in zwei
  Fällen ein Satz, warum nichts hinausgeht.
- **Einstellungen** (Strg+2), **Senden**, seit 0.11.1 offen, ohne Aufklappen: rechts die Schalter **Automatische
  Lautstärke**, **Sprachaktivierung** (derselbe wie **Senden mit Stimme** auf Start) und **Mindest-Funklautstärke**,
  darunter über die ganze Breite die **Funkschwelle**: der Pegelbalken, dessen Griffe sich ziehen lassen, daneben
  **Einmessen**, darunter der Knopf **Genau einstellen ...**, der die Seite Sprachaktivierung öffnet, auch vor dem
  Verbinden. **Feste Verstärkung einmessen** steht unter **Erweitert:** (nur ohne Automatik nötig und mit Automatik
  gesperrt, darunter `Nur ohne Automatik nötig.`). Gesperrt ist er nur, wenn **Senden mit Stimme** aus ist, mit dem Grund `Senden mit Stimme ist aus; schalte es
  auf Start an, dann lässt sich die Sprachaktivierung hier genau einstellen.`
- **Die Seite Sprachaktivierung**: ein Unterpunkt unter **Einstellungen** in der Leiste links, solange ein Kanal mit
  Sprachaktivierung sendet oder die Seite offen ist; Strg+T und **Genau einstellen ...** öffnen sie, solange **Senden mit
  Stimme** an ist. Schaltest du **Senden mit Stimme** aus, während die Seite vorn ist, geht es zu Einstellungen. Auf der
  Seite siehst du:

  - eine Grafik der letzten 10 Sekunden mit **Pegel**, **Rauschboden** (gestrichelt), **Schwelle** (die Linie) und grün
    hinterlegt, was **gesendet** wurde,
  - darunter die Werte gerade jetzt: Pegel, Boden, Schwelle und Sprache in Prozent,
  - daneben die Schalter und Regler (A5.3) und **Mindestlautstärke einmessen**.

  Hörprobe und Mithören stehen seit 0.10.0 nicht mehr auf dieser Seite (A3.4).

**Wirken kann die Seite nur in Netzen auf VA.** Ob ein Netz mit Sprachaktivierung oder mit der Sprechtaste sendet,
stellst du je Netz ein (A6.2). Solange du das nicht getan hast, gilt die Voreinstellung, die der Admin für das Netz
festgelegt hat. Seit 0.5.0 ist das für neue Netze VA, ob der Admin sie im Verwaltungsteil oder an der Konsole anlegt
(`funk-admin netz-setzen`): Ohne weitere Angabe steht ein neues Netz auf VA, nur mit `--taste` oder `--nur-taste` auf
Taste (Anf. A62). Ausnahme bleibt Global, das immer auf Taste steht. Die Vorlage **Star Citizen** legt alle zehn Kanäle mit
Sprachaktivierung an, auch Kommando. Auf Taste steht ein Kanal nur, wo der Admin es so einstellt oder ihn an der Konsole mit
`--taste` anlegt, wie im Beispielbefehl der Anleitungen (B12.1; bis 0.9.0 stand das damalige Netz Command nach Anf. A64 auf
Taste).

**Bestehende Netze bleiben, wie sie sind.** Ein Netz, das vor 0.5.0 an der Konsole ohne `--va` angelegt wurde, steht nach
dem Update weiter auf Taste, und deine eigene Wahl je Netz bleibt ebenfalls. Wer dort VA will, stellt es je Feld im
Funkfeld um (A6.2), oder der Admin legt das Netz mit `netz-setzen` noch einmal an. Der Befehl ersetzt das Netz als Ganzes;
der Admin gibt dabei alle Angaben des Netzes wieder mit (B9, B12.1).

Sendest du in mehrere Netze zugleich, gilt VA nur, wenn alle auf VA stehen; ohne Verbindung gibt es kein Sendenetz und
damit keine Sprachaktivierung. Steht dein Sendenetz auf Taste, sendet die Sprachaktivierung nicht, auch wenn du über der
Linie sprichst; gesendet wird dann nur mit der Sprechtaste. Das war der Grund, warum die Hörprobe in 0.4.0 stumm bleiben
konnte (A3.4).

### A5.3 Die drei Arten

| Einstellung | Was passiert | Wann sie passt |
| --- | --- | --- |
| **Automatik** an (ab Werk) | Die Schwelle folgt deinem Rauschboden mit festem Abstand, ab Werk 9 dB darüber, und justiert sich laufend nach. In einem ruhigen Raum liegt sie mit dem Abstand ab Werk bei -60 dBFS und nicht tiefer. | als Voreinstellung; damit fängst du an |
| **Automatik** an, **Lernen** an | Wie Automatik, dazu lernt sie still: Wird es im Hintergrund dauerhaft laut, etwa weil Kinder schreien, steigt die Schwelle, danach sinkt sie wieder. Sie bleibt dabei immer in einem **Korridor**, ab Werk von -55 bis -30 dBFS. Seltene Klicks heben sie kaum. | wenn es bei dir zeitweise laut wird |
| **Automatik** aus | Die Linie steht fest, wo du sie hinziehst (Startwert -45 dBFS). Gesendet wird, was lauter ist und nach Sprache klingt. | wenn du es ganz genau selbst bestimmen willst |

Die Regler rechts:

| Regler | Bedeutung | Ab Werk |
| --- | --- | --- |
| Abstand zum Rauschboden | wie weit über dem Grundrauschen die Schwelle liegt (nur mit Automatik) | 9 dB |
| Schwelle | die feste Linie (nur ohne Automatik) | -45 dBFS |
| Korridor von, Korridor bis | die Grenzen, in denen sich die lernende Schwelle bewegen darf (nur mit Lernen) | -55 und -30 dBFS |
| Anstieg, Rückkehr | wie schnell die lernende Schwelle steigt und wieder sinkt, je 10 dB (nur mit Lernen) | 2 s und 5 s |
| Sprache ab | wie sicher die Spracherkennung sein muss. Mit Automatik ist das die Untergrenze; bei sprachähnlichen Geräuschen in deiner Umgebung hebt JG TacRelay sie selbst an, höchstens auf 80 %, und bleibt dabei mindestens bei 40 %. | 50 % |

Die Linie kannst du in der Grafik auch mit der Maus ziehen oder mit Pfeil hoch und runter verschieben (mit Strg in
Schritten zu 5 dB). **Standardwerte** setzt die Regler auf die Werte ab Werk zurück; die Schalter **Automatik** und
**Lernen** bleiben, wie sie sind. Knöpfe wie "das war kein Sprechen" gibt es bewusst nicht; gelernt wird still.

### A5.4 Wenn es nicht passt

| Was passiert | Was du tust |
| --- | --- |
| Tastatur, Maus oder Chipstüte lösen aus | **Sprache ab** etwas höher, oder den Abstand größer. Bei lauter Umgebung **Lernen** einschalten. |
| Deine Wortanfänge fehlen, oder leise Sätze kommen nicht durch | den Abstand kleiner, oder **Sprache ab** etwas niedriger. Mit Headset über **Beim Senden mithören** nachhören, sonst im Netz Echo (A3.4). Die **Hörprobe** nimmt ohne Schwelle auf und zeigt das nicht. |
| Es sendet dauernd | In der Grafik nachsehen, ob dein Pegel auch ohne Sprechen über der Linie liegt; dann den Abstand größer. Mit Lautsprechern prüfen, ob die Echounterdrückung an ist. |
| Du sprichst, aber es geht nichts hinaus, und in der Statuszeile steht nie `SENDET` | Sieh auf Marke und Balken (A2.5): Steht dort `Ruhe` oder `Getrennt`, oder wird der Balken gelb statt grün, geht es nirgends hin; dann im Funkfeld einen Kanal zum Senden wählen oder verbinden. Steht dein Sendenetz auf **Taste** (A5.2)? Dann das Netz auf **VA** stellen (A6.2) oder die Sprechtaste halten. Steht dort `Stimme unter der Mindest-Funklautstärke`, sprichst du leiser als die Mindest-Funklautstärke: lauter sprechen oder unter **Einstellungen**, **Senden**, **Funkschwelle** neu **Einmessen**. |
| Du willst gar keine Sprachaktivierung | auf Start **Senden mit Stimme** ausschalten, oder nur einzelne Netze auf **Taste** stellen (A6.2). |

Sprachaktivierung oder Taste stellst du **je Netz** ein (A6.2), für alle Netze zugleich mit **Senden mit Stimme** auf
Start. Die Einstellungen der Seite Sprachaktivierung gelten für dich, in allen Netzen mit VA. Eine
Schritt-für-Schritt-Einstellung, die Messwerte und die Technik dahinter stehen in Teil E (E2.8 und E3).

### A5.5 Funk-Klick einstellen

Der Funk-Klick ist das kurze Geräusch wie an einem Funkgerät. JG TacRelay erzeugt es selbst, mischt es in deinem Client
zum Ton und schickt es nie zu den anderen. Seit 0.10.0 stellst du ihn unter **Einstellungen** (Strg+2), **Tasten**,
**Erweitert**, Gruppe **Funk-Klick** ein (von 0.5.0 bis 0.9.3 in der Leiste **Sprachaktivierung** beziehungsweise im
Bereich **Audio**). Ein Satz steht dort über allem: "Ein kurzes Rauschen wie am Funkgerät. Die Lautstärke ist auf eine
Stimme bezogen: -12 dB heißt 12 dB leiser als Sprache." Das heißt: Je näher an -6, desto lauter; je näher an -30, desto
leiser gegenüber deiner Stimme.

| Einstellung | Was sie macht | Ab Werk |
| --- | --- | --- |
| **Bei deiner Taste** | Klick, wenn du Sprechtaste oder Melde-Taste drückst und loslässt, auch am Joystick. Darunter der Regler **Lautstärke**, von -30 bis -6 dB. Über Lautsprecher hörst du deinen eigenen Klick nicht; das steht dann darunter. | an, -12 dB |
| **Bei anderen Sprechern** | Klick, wenn jemand anderes zu sprechen beginnt und aufhört. Darunter ein eigener Regler **Lautstärke**, von -30 bis -6 dB. | an, -15 dB |
| **Bei Sprachaktivierung dezent** | gilt für andere Sprecher: Spricht jemand per Sprachaktivierung, klickt es 18 dB leiser; ausgeschaltet gar nicht. Nur wählbar, solange **Bei anderen Sprechern** an ist. | an |
| **Anhören** | spielt dir zur Probe den Klick mit deinen jetzigen Einstellungen vor. Nur bei laufender Wiedergabe: sonst steht neben dem Knopf `Zum Anhören muss die Wiedergabe laufen; starte Audio auf Start.` Der Satz nennt noch Start; **Audio starten** steht seit 0.11.2 unter **Einstellungen**, **Fehlersuche** (A3.2). | |

Die Wahl zwischen Klick, Doppelklick und Piep aus 0.5.0 gibt es seit 0.6.0 nicht mehr; der Klick ist ein kurzes Rauschen.
Die Werte des Funk-Klicks zählen unter **Tasten** zu **Erweitert:**; **Zurück auf Werkseinstellung** dort stellt sie zurück
(A2.5).

Jede Änderung wirkt sofort und wird auf deinem PC gespeichert (in `client-einstellungen.json` unter `klick`, im Ordner
`%LOCALAPPDATA%\JG-TacRelay`); an deinem Konto auf dem Server liegt nichts davon.

Zwei Dinge dazu. Ab Werk ist der Klick lauter als bis Fassung 0.4.1, wo er bei -24 dBFS Spitze fest eingebaut und ohne
Einstellung war: Er lag gemessen gut 22 dB unter der Sprache und war vermutlich zu leise. Ob die neue Voreinstellung am
Ohr passt, ist noch nicht gehört worden (1.8); stell sie nach deinem Geschmack ein. Mit Lautsprechern und
Echounterdrückung lösten im Test 28 Klicks anderer Sprecher die eigene Sprachaktivierung auch bei lautem Klick nicht aus
(gemessen am 02.10.2026, mehr dazu in Teil E).


## A6 Das Funkfeld

### A6.1 Aufbau

Das Funkfeld ist ein eigenes kleines Fenster. Es bleibt immer im Vordergrund und lässt sich weder minimieren noch
maximieren. Das X in der Titelzeile schließt es nicht, sondern legt es in den Infobereich der Taskleiste (A6.6); Strg+F im
Hauptfenster, der Knopf **Funkfeld** in der Titelleiste oder die Taste und das Symbol dort holen es zurück. Über Star Citizen ist es nur im Modus **Randloses Fenster** sichtbar, nicht im exklusiven Vollbild.

Zwei Ansichten, umschaltbar in der Titelzeile des Funkfelds (seit 0.10.0 nur dort; bis 0.9.3 auch unter
**Einstellungen**):

| Ansicht | Aussehen | Auf den Feldern |
| --- | --- | --- |
| **Raster** (ab Werk) | 2 Reihen zu 5 Feldern | Pegel, Lautstärke, VA/Taste und Hören an/aus |
| **Liste** | schmal, senkrecht, für den Bildschirmrand | Name, Zustand und Sprecher; die Lautstärke je Kanal stellst du dann unter **Einstellungen**, **Empfangen**, **Erweitert** ein (A3.5), VA/Taste im Raster |

Der Schiebeschalter **5/10** in der Titelzeile wählt, ob 5 oder 10 Felder zu sehen sind; ab Werk 10. Auch ihn gibt es
seit 0.10.0 nur noch dort; die Knöpfe **Funkfeld 5** und **Funkfeld 10** auf der früheren Startseite sind weg, das
Funkfeld holt der Knopf **Funkfeld** in der Titelleiste des Hauptfensters.

Jedes Feld zeigt seinen Zustand mit Farbe, Symbol und Text, nicht nur mit Farbe:

| Zustand | Aussehen | Bedeutung |
| --- | --- | --- |
| SENDET | grün, dicker Rahmen | dein Sendenetz: hier geht hinein, was du sprichst |
| HÖRT | orange | du hörst dieses Netz |
| AUS | gedimmt, nur Rand | Hören abgeschaltet |
| FREI | blass | auf diesem Feld liegt kein Netz |

Global hat ein eigenes Feld in der Leitfarbe (dunkles Petrol). Es hört immer; senden darf darin nur, wer das Recht dazu hat.

Auf einem Feld steht außerdem, wer gerade spricht, sonst "Ruhe"; sprichst du selbst, steht dort "Du sprichst".

### A6.2 Bedienen

- **Klick auf ein Feld:** wählt es als Sendenetz.
- **Strg+Klick:** nimmt ein weiteres Sendenetz dazu. Das geht nur mit dem Recht, in mehrere Netze zugleich zu sprechen
  (Führung, Admins, Einsatzleitung); sonst kommt ein Hinweis.
- **Ziffern 1 bis 9 und 0**, wenn das Funkfeld den Fokus hat: wählen das Sendenetz im Feld; 0 ist Global.
- **+ und -**, wenn das Funkfeld den Fokus hat: schalten das Sendenetz weiter, solange keine globale Netztaste belegt ist.
- **VA/Taste** auf dem Feld: Sprachaktivierung oder Sprechtaste für dieses Netz. Ab Werk gilt, was der Admin für das
  Netz voreingestellt hat: seit 0.5.0 bei neuen Netzen VA (auch alle Kanäle der Vorlage Star Citizen), bei Global Taste, bei
  einem Netz, das der Admin mit `--taste` oder vor 0.5.0 an der Konsole ohne `--va` angelegt hat, ebenfalls Taste (A5.2). Deine Wahl
  merkt sich der Client je Netz. Manche Netze legt die Flotte auf "nur Taste" fest, etwa Global; dort lässt es sich nicht
  umstellen.
- **Aus/An** auf dem Feld: Hören ab- und anschalten. Ein Feld, das die Einsatzleitung fest gesetzt hat, kannst du nicht
  leeren oder überschreiben, das Hören aber trotzdem abschalten.
- Der kleine Regler auf dem Feld ist die Lautstärke dieses Netzes.

### A6.3 Größe und Lage

Das Funkfeld startet gerade so groß, dass alles zu sehen ist, und merkt sich je Ansicht Lage und Größe, so wie du es
hinschiebst und am Rand ziehst, auch für den nächsten Start. **Größe nach Inhalt** unter **Einstellungen**, **Funkfeld und
Darstellung**, **Erweitert**, Gruppe **Funkfeld** setzt die Größe zurück auf die beste; die Lage bleibt. **Funkfeld beim
Start zeigen** in derselben Gruppe ist ab Werk an. Dort steht auch **Skalierung (Größe)**, die das ganze Funkfeld kleiner
oder größer macht.

### A6.4 Durchsichtigkeit

Damit das Funkfeld über dem Spiel nicht stört, lässt es sich durchsichtig machen, von 0 % (deckend, ab Werk) bis 85 %, in
Schritten zu 5 %.

- Am schnellsten mit dem kleinen Regler in der Titelzeile des Funkfelds, links neben den Fensterknöpfen. Im Raster steht
  er direkt da, in der schmalen Liste klappst du ihn über sein Symbol auf. Pfeiltasten ändern um 5 %, Bild auf und ab um
  10 %.
- Oder unter **Einstellungen**, **Funkfeld und Darstellung**, **Erweitert**, **Durchsichtigkeit**. Beide Regler stellen
  dieselbe Einstellung.

Jeder Wert wirkt sofort, auch beim Ziehen. Ab Werk wird das ganze Funkfeld gleichmäßig durchsichtig, Schrift und
Zustandsfarben mit. Mit **Schrift und Symbole deckend halten** werden nur die Lücken und die Titelzeile durchsichtig;
Felder, Schrift und Zustandsfarben bleiben so deckend, dass sie auch über hellem Spielbild lesbar bleiben.

### A6.5 Farben

Das Programm hat eine Farbstellung, **Anthrazit** (seit 19.10.2026): Flächen in Anthrazit und Grau, neutral und ruhig,
Titelleiste, Global und Gewähltes in dunklem Petrol, Rahmen, Linien und Fokus im gedämpften Akzent Petrol. Grün steht für
Senden, Orange für Hören, Gelb und Rot für Warnungen. Eine Wahl unter Einstellungen gibt es nicht mehr (bis 18.10.2026 drei
Farbstellungen P1 bis P3); eine früher gespeicherte Wahl wird still zu Anthrazit.

### A6.6 Das Funkfeld in den Infobereich legen

Seit 0.5.0 kann sich das Funkfeld in den **Infobereich** der Windows-Taskleiste zurückziehen, den Bereich rechts neben
der Uhr, in dem kleine Symbole laufender Programme sitzen. Das Funkfeld ist dann abgelegt, nicht geschlossen: Lage,
Größe, Ansicht und Durchsichtigkeit bleiben, und es spricht und hört weiter wie bisher; nur das Fenster ist weg. Das
Symbol von JG TacRelay liegt dort, solange das Programm läuft. Windows legt neue Symbole oft hinter einen kleinen Pfeil
(^); bis 0.11.1 sagte das auch ein Hinweis beim ersten Ablegen in der Meldezeile, seit 0.11.2 gibt es ihn nicht mehr
(A6.7). Ob und wie du das Symbol daraus in die sichtbare Leiste holst, ist eine Einstellung von Windows.

| Du willst | So geht es |
| --- | --- |
| ablegen | die Taste **Funkfeld ablegen/holen** (A4.1), ein Linksklick auf das Symbol im Infobereich, das X in der Titelzeile des Funkfelds, Alt+F4 im Funkfeld oder im Menü des Symbols (Rechtsklick) **Funkfeld ablegen** |
| holen | dieselbe Taste, ein Linksklick auf das Symbol, im Menü des Symbols **Funkfeld zeigen**, oder im Hauptfenster Strg+F beziehungsweise der Knopf **Funkfeld** |

Taste und Symbol zeigen das Funkfeld, ohne dass Star Citizen den Fokus verliert: Das Fenster erscheint ohne Aktivierung
und bleibt oben. Strg+F und der Knopf im Hauptfenster holen es nach vorn, denn dort liegt der Fokus ohnehin bei
JG TacRelay. Das X ist also nicht mehr "Schließen"; sein Tooltip nennt, womit du es zurückholst, etwa `Funkfeld ablegen.
Zurück mit Num * oder dem Symbol im Infobereich.` (mit deiner Taste an der Stelle von `Num *`; ohne belegte Taste steht
dort `Zurück mit dem Symbol im Infobereich oder Strg+F im Hauptfenster.`). Wirklich geschlossen wird das Funkfeld erst
mit dem Programm.

Der Tooltip des Symbols nennt Stand und Sendenetz, etwa `JG TacRelay - Funkfeld abgelegt - Senden: Crew 3`, und ändert
sich höchstens einmal je Sekunde. Das Menü des Symbols (Rechtsklick) hat vier oder fünf Einträge:

| Eintrag | Wirkung |
| --- | --- |
| **Funkfeld zeigen** oder **Funkfeld ablegen** | je nachdem, was gerade gilt |
| **Mikro stumm** oder **Mikro wieder an** | schaltet dein Mikrofon stumm und wieder an, so wie die Taste **Mikro stumm** (A4.1) und der Knopf in der Titelzeile des Funkfelds; bis 0.11.1 sagte es die Meldezeile, seit 0.11.2 zeigen es der Knopf im Funkfeld und der Pegelbalken mit dem Wort `stumm`. Neu in 0.10.0. |
| **Hauptfenster** | holt das Hauptfenster |
| **Heute als Funker** oder **Wieder Admin** | nur für Admins an einem Server ab 0.5.0 (A12.7); **Heute als Funker** öffnet im Hauptfenster die Rückfrage, **Wieder Admin** gilt sofort |
| **Beenden** | beendet JG TacRelay |

Unter **Einstellungen**, **Funkfeld und Darstellung**, **Erweitert** schaltet **Funkfeld beim Start zeigen** (ab Werk
an), ob das Funkfeld beim Start sichtbar ist. Aus heißt: Es liegt beim Start im Infobereich, und die Taste, das Symbol oder Strg+F holen es.

Am Code geprüft und im Bildlauf der Oberfläche; am echten PC noch nicht ausprobiert sind das Symbol im Infobereich, das
Ablegen und Holen mit der Taste und mit dem Joystick, während Star Citizen im Modus **Randloses Fenster** vorn ist, und
das Menü des Symbols (1.8).

### A6.7 Die Meldezeile

Die Meldezeile ist ein kleines Fenster mit einer Meldung (bis 0.10.0 höchstens drei untereinander). Seit 0.11.1 steht
es an einem **festen Platz** des Bildschirms, nicht mehr am Funkfeld (bis 0.11.0 über dem Funkfeld, linksbündig, bei
abgelegtem Funkfeld an dessen letztem Platz): ab Werk oben in der Mitte auf dem Monitor, auf dem Star Citizen läuft,
sonst auf dem Hauptmonitor. Wo, stellst du unter **Einstellungen**, **Funkfeld und Darstellung**, **Erweitert**, Gruppe
**Meldezeile** ein: ein kleiner Monitor mit neun Feldern (oben, Mitte, unten mal links, Mitte, rechts); ein Klick auf ein
Feld setzt die Stelle und zeigt die Meldezeile dort drei Sekunden lang als Probe (`Hier steht die Meldezeile`), ein Klick
auf das schon gewählte Feld zeigt die Probe noch einmal. Daneben
wählst du den **Monitor**: **Automatisch** (Star Citizen, sonst Hauptmonitor), **Hauptmonitor** oder **Monitor 1**,
**Monitor 2**, ... in der Reihenfolge, in der Windows sie zählt; ein gewählter Monitor, der gerade nicht angeschlossen
ist, heißt Hauptmonitor. Bei **Automatisch** bleibt sie seit 0.11.2 auf dem Monitor, auf dem das Spiel zuletzt zu sehen
war, auch wenn JG TacRelay das Spielfenster kurz nicht findet; bis 0.11.1 sprang sie dann auf den Hauptmonitor und
zurück. **Probe zeigen** zeigt sie jederzeit. Das Fenster zeigt nur kurz etwas an und verschwindet dann von
selbst. Es nimmt nie den Fokus, lässt Mausklicks hindurch und erscheint weder in Alt+Tab noch in der Taskleiste;
Hovertexte hat es deshalb keine. Fehler stehen in der roten Schrift deiner Farbstellung. Über Star Citizen ist sie, wie
das Funkfeld, nur im Modus **Randloses Fenster** sichtbar (am Code geprüft, am echten PC nicht ausprobiert; ebenso die
Erkennung des Monitors, auf dem das Spiel läuft).

Was dort erscheint (seit 0.11.2): nur noch, was die Leitung tut (du wurdest eingeteilt, deine Kanäle wurden geändert,
eine Phase hat dich umgestellt, die Ansage beim Login, der Entzug des Senderechts, die Phasentasten der Leitung), dein
eigenes Funken mit der Melde-Taste und die Probe aus den Einstellungen. Alles andere fällt still weg: **Mikro stumm**,
**Dauersender stumm**, der Sendekanal nach Kanal vor und zurück, das Urteil des Wächters mit dem gelben "!" (es steht
weiter am "!" im Funkfeld), `Rauschfilter: Standard statt Stark` (der Grund steht weiter unter Einstellungen, Senden), der
Hinweis beim ersten Ablegen des Funkfelds und die Sätze, dass nichts gesendet wird (A2.5). Bis 0.11.1 kamen sie mit, je
Satz höchstens einmal in fünf Minuten; was jetzt noch erscheint, erscheint jedes Mal. Eigene Aktionen erzeugen keine
Meldung: deine Rollenwahl, dein Ch., dein eigenes Einteilen auf der Funkfeld-Zuteilung und eine Phase, die du selbst geschaltet
hast (seit 0.11.1; die Phasentaste sagt `Phase ... läuft`, das Funkfeld zeigt es). Beim Start und beim Zeigen des
Funkfelds erscheint nichts, außer es ist wirklich neu: eine Einteilung durch andere, eine Phase, ein Fehler.

Seit 0.11.0 steht immer nur **eine Meldung** in **einer Zeile** da. Kommen mehrere, gilt der Rang: Fehler vor den Meldungen
der Phasen, diese vor den einfachen Hinweisen. Eine neue Meldung gleichen oder höheren Rangs ersetzt die stehende, und die
ersetzte kommt nicht wieder. Eine Meldung niedrigeren Rangs wartet, bis die stehende abgelaufen ist, und erscheint dann für
den Rest ihrer Zeit; ist ihre Zeit bis dahin um, erscheint sie gar nicht. Warten mehrere, kommt die ranghöchste zuerst. Eine
neue Meldung derselben Art (etwa eine zweite Umstellung durch die Leitung) ersetzt die alte, auch eine wartende.
`Melden: an Kommando` steht, solange du die Melde-Taste hältst: Verdrängt sie eine andere Meldung, kommt sie danach wieder.

Die Zeile bricht nie um, und die Meldezeile ist seit 0.11.1 fest 640 Einheiten breit bei 0 % (mit der Skalierung 448
bis 832; bis 0.11.0 höchstens so breit wie das Funkfeld, über der 5er-Liste nur 288). Seit 0.11.2 ist der Rahmen immer so
breit, auch bei einem kurzen Satz; bis dahin wurde er mit dem Satz schmaler und wanderte an den Stellen in der Mitte und
rechts mit jeder Meldung. Was nicht hineinpasst, endet mit
"..."; die üblichen Sätze passen ganz. Die Sätze sind seit 0.11.0 kürzer: Was die Kacheln des Funkfelds ohnehin zeigen
(deine Kanäle und wohin du sendest), steht nicht mehr in jeder Meldung, der Urheber steht in Klammern. Schrift, Innenrand
und Höhe der Meldezeile wachsen und schrumpfen mit der **Skalierung** des Funkfelds (-30 bis +30 %, A6.3): Was bei 0 %
ganz hineinpasst, passt bei jeder Skalierung ganz hinein.

| Anlass | Beispiel | Wie lange |
| --- | --- | --- |
| Die Einsatzleitung macht eine Phase scharf (A12.6) | `Phase 2/3 ENTERN? Nochmal drücken - 9 Funker, ausgelassen: Nordwind (Admin)` | 5 s |
| Die Einsatzleitung hat geschaltet | `Phase 2 Entern läuft - 9 umgestellt` | 4 s |
| Die Einsatzleitung hat nicht geschaltet (rot) | `Nicht geschaltet: abgebrochen` | 5 s |
| Eine Phase hat dich umgestellt | `Phase Entern (Nordwind): sendest in Crew 3`, `... hörst Kommando` oder `... Kanäle umgestellt` (bis 0.10.0 `du sendest jetzt in Crew 3`); nicht, wenn du die Phase selbst geschaltet hast (seit 0.11.1) | 6 s |
| Die Einsatzleitung hat dich eingeteilt | `Eingeteilt von Nordwind: Schiff 3, Gunner` (bis 0.10.0 mit `Du sendest in Crew 3.`); steht noch die Ansage beim Login, ersetzt diese Zeile sie | 6 s |
| Beim Login: Die Einsatzleitung hat dich eingeteilt, während du offline warst (seit 0.11.0, A7.3) | `Eingeteilt von Nordwind: Schiff 3, Gunner`, ohne Platz `Kanäle eingeteilt von Nordwind` | 12 s |
| Die Einsatzleitung hat deine Kanäle geändert | `Kanäle geändert von Nordwind: hörst Kommando` oder `...: sendest in Crew 3`; ist nur etwas umgestellt, `Kanäle geändert von Nordwind` | 6 s |
| Du hast das Senderecht verloren (rot) | `Nicht gesendet: kein Senderecht mehr in Kommando (Nordwind)` (bis 0.10.0 `... - verschoben durch Nordwind`) | 5 s |
| Melde-Taste gehalten (nur bei abgelegtem Funkfeld) | `Melden: an Kommando` | solange gehalten |
| Melden geht nicht (rot, A4.4) | `Melden geht nicht: kein Senderecht in Kommando` | 3 s |
| Die Probe aus den Einstellungen (seit 0.11.1) | `Hier steht die Meldezeile`, nach einem Klick ins Raster, der Wahl des Monitors oder auf **Probe zeigen** | 3 s |

Die Namen in den Beispielen sind erfunden. Ein Satz wie "sendest in Crew 3" nennt das Sendenetz, das für dich gilt,
nachdem die Leitung etwas verändert hat. Die Sätze der Statuszeile **Mikrofon** (A2.5) stehen seit 0.11.2 nicht mehr in
der Meldezeile, auch `Kanal braucht die Sprechtaste - keine belegt` nicht; bis 0.11.1 kamen sie mit, je Satz einmal in
fünf Minuten. Bis 0.11.1 standen außerdem in dieser Tabelle der Sendekanal nach Kanal vor und zurück
(`Sendekanal: Crew 3`), der Hinweis beim ersten Ablegen (`Funkfeld abgelegt: Taste oder Symbol (^) holt es`),
**Mikro stumm** (`Mikro stumm: nichts wird gesendet`), **Dauersender stumm**, das Urteil des Wächters mit "!" und
`Rauschfilter: Standard statt Stark`.

### A6.8 Die Sprecheranzeige

Seit 0.11.9 steht über dem Spiel, wen du gerade hörst: je Sprecher eine Zeile, der Name fett (der Name, den auch **Wer
ist da** zeigt), dahinter normal der Kanal mit Ch., so wie im Funkfeld:

| Was du hörst | Zeile |
| --- | --- |
| jemand in einem Kanal | **Kobold** Brücke (Schiff 3) |
| eine Durchsage an alle | **Nordlicht/Anna** Durchsage an alle |
| eine Durchsage an Kanäle | **Falke/Tom** Durchsage: Brücke (Schiff 3) |
| Global (Orgaweit) | **Sturmvogel** Global |
| du selbst, nur mit **Eigene Sendung zeigen** | **Du** -> Bodentrupp (Trupp 2) |

Die Namen sind erfunden. Die Schrift ist weiß mit einem weichen schwarzen Schatten, ohne Fläche, ohne Rahmen, ohne Farben
und ohne Zeichen; Durchsage und Global stehen als Wort da. Gezeigt wird nur, wen du wirklich hörst: In Kanälen zählen
nur deine Felder, die hören (ein abgeschalteter Kanal, eine Ruhe oder ein gesperrter Kanal nicht), und ein Funker, den
du am gelben "!" stumm geschaltet hast, steht nie da. Eine Durchsage hörst du auch in einem abgeschalteten Kanal, sie
steht also da. Die Namen kommen aus dem, was der Server ohnehin an jedes Sprachpaket stempelt (wer spricht, welche Art,
in welchen deiner Kanäle), und aus der Anzeige der Klangmaschine, wer gerade zu hören ist; dafür braucht es kein Recht und
keinen neuen Server. Fremde Kanäle siehst du so nicht.

**Wann.** Die Zeile erscheint, sobald der Ton kommt, bleibt nach dem letzten Wort noch 1,0 Sekunden stehen und blendet
dann in 0,5 Sekunden aus; beides stellst du unter **Erweitert** ein (Nachhalten 0 bis 5 s, Ausblenden 0 bis 2 s). Sprechen
mehrere, steht oben, wer zuerst sprach. Ab Werk steht genau **eine** Zeile da (Sarevoc: mehr als einem gleichzeitig zuzuhören
ist Mist): Hört der Oberste auf, rückt nach dem Nachhalten der nächste nach. Stellst du 2 bis 4 Zeilen ein und sprechen
mehr, sagt die letzte Zeile `+2 weitere`. Ist niemand zu hören, ist das Fenster ganz fort, nicht leer.

**Einstellungen.** Unter **Einstellungen**, **Sprecheranzeige** (zwischen **Funkfeld und Darstellung** und
**Fehlersuche**) stehen offen der Schalter **Sprecheranzeige an** (ab Werk an) und unter **Wo im Bild** zwei Regler:
**Waagerecht** (0 % links bis 100 % rechts) und **Senkrecht** (0 % oben bis 100 % unten), je in Prozent des Monitors, mit
dem Wert daneben (etwa `12 %`). Ab Werk stehen beide auf 0 %, die Anzeige also ganz oben links, 24 Punkte vom Rand. Die
linke obere Ecke der Schrift folgt den Reglern; ganz rechts und ganz unten bleibt die Anzeige so weit innen, dass nichts
aus dem Monitor ragt, auch mit mehreren Zeilen. Solange du schiebst, steht eine Probezeile genau dort über dem Spiel
(`Probe So steht hier, wer spricht`), und noch drei Sekunden nach dem letzten Schieben; einen eigenen Knopf dafür gibt es
nicht. Mit der Tastatur gehen die Regler wie die anderen: Pfeiltasten in ganzen Prozent, Bild auf und ab in Zehnern. Bis
zu Sarevocs Änderung vom 15.10.2026 ("die Position mit Schieberegler einstellen, nicht fest vorgeschlagen") stand hier ein Raster
mit neun festen Stellen; eine dort gewählte Stelle wird beim ersten Start in die Regler umgerechnet. Unter **Erweitert:** stehen:

| Einstellung | Ab Werk | Was sie tut |
| --- | --- | --- |
| Größe | 100 % | Schrift und Schatten kleiner oder größer, 60 bis 200 % |
| Durchsichtigkeit der Schrift | 0 % | Schrift und Schatten scheinen durch, bis 80 % |
| Höchstens so viele Zeilen | 1 | 1 bis 4; bei mehr Sprechern `+n weitere` in der letzten Zeile |
| Nachhalten | 1,0 s | so lange bleibt ein Name nach dem letzten Wort |
| Ausblenden | 0,5 s | so lange blendet er danach aus |
| Eigene Sendung zeigen | aus | solange du sendest, eine Zeile `Du -> Kanal (Ch.)` |
| Monitor | Automatisch | der Monitor, auf dem das Spiel (Zeile **Spiel**) vorn läuft, sonst der zuletzt bekannte, sonst der Hauptmonitor; oder fest **Hauptmonitor**, **Monitor 1**, ... |
| Spiel | Star Citizen | **Star Citizen (ab Werk)** oder **Anderes Spiel** mit dem Namen seiner Programmdatei, eingetippt (etwa `Spiel.exe`, auch mit Pfad) oder über **Programmdatei wählen ...**; gemerkt wird der Name ohne Pfad und ohne `.exe`. Sprecheranzeige, Meldungen (A6.9) und Meldezeile (A6.7) suchen damit den Monitor, auf dem das Spiel läuft; JG TacRelay liest nichts aus dem Spiel und schickt ihm nichts. Darunter steht, wonach gesucht wird, ab Werk `Gesucht wird Star Citizen (StarCitizen.exe, ohne den Launcher).`; ohne gültigen Namen gilt weiter Star Citizen |

Daneben zeigt **So sieht sie aus** die Anzeige mit derselben Schrift und demselben Schatten auf einem dunklen Grund, so
viele Zeilen, wie eingestellt sind. **Zurück auf Werkseinstellung** stellt diese acht zurück (das Spiel auf Star
Citizen) und die Regler **Waagerecht** und **Senkrecht** auf 0 % (sie zählen darum in der Kopfzeile mit); der Schalter
steht offen und bleibt, wie er ist.

**Über dem Spiel.** Die Anzeige ist ein eigenes kleines Fenster wie die Meldezeile: Sie nimmt nie den Fokus, lässt
Mausklicks hindurch, steht weder in Alt+Tab noch in der Taskleiste und greift nicht in Star Citizen ein (kein Einhaken,
kein Lesen des Spiels, keine Eingaben); gerechnet wird im ganzen Monitor, auch über der Taskleiste.
Sichtbar ist sie nur, wenn das Spiel (ab Werk Star Citizen) im Modus **Randloses Fenster** läuft. Läuft etwas im
exklusiven Vollbild, sagt das der Kasten oben im Abschnitt:
`Das Spiel läuft im Vollbild; die Anzeige erscheint nur bei Randlos`. Eine Meldung über dem Spiel gibt es dafür nicht. Solange die Anzeige zu sehen ist, setzt Windows das Bild zusammen; das kann einen Frame
kosten, darum ist sie klein und nur da, wenn jemand spricht (am echten PC noch nicht gemessen, 1.8).

### A6.9 Die Meldungen der Leitung

Seit 0.12.0 kann die Leitung Grafiken und Text an alle schicken, die online sind (A12.8). Bei dir erscheinen sie in einem eigenen
kleinen Fenster über dem Spiel, ab Werk oben in der Mitte, nebeneinander und oben bündig:

| Platz | Was | Wie lange |
| --- | --- | --- |
| groß | ein **Befehl**: Sammeln, Sammelpunkt X, Stopp, Rückzug, Start freigegeben, Medizinische Hilfe, Los, Stopfen | 8 Sekunden ab dem Eingang, mit einem kurzen Doppelton; ein neuer Befehl ersetzt den alten, gleich von wem |
| groß | die **Bereit-Abfrage** (Bild **Bereit melden**), solange kein Befehl steht | bis die Leitung sie schließt, höchstens 2 Minuten; nach deinem Melden steht `gemeldet` darunter (A4.6) |
| klein daneben | ein **Zustand**: Einsatz noch nicht gestartet, Event noch nicht gestartet, Position halten, Funkdisziplin, Pause | bis die Leitung ihn aufhebt oder einen anderen schickt; kein Ton |
| eigener Platz | die **Ampel** rot, gelb oder grün | bis die Leitung sie umschaltet oder ausmacht; sie läuft nicht von selbst durch |
| eigener Platz | der **Countdown**: **Start um** mit der Uhrzeit (in deiner Ortszeit) und der Zeit bis dahin, oder **Restzeit** mit der Zeit bis dahin | bis 0, dann ist er fort; in den letzten 10 Sekunden je Sekunde ein kurzer Ton |
| darunter | eine **Textmeldung** (seit 0.12.0, M24): nur Text der Leitung, ohne Grafik, linksbündig unter dem Befehlsplatz | 8 Sekunden ab dem Eingang oder, wenn die Leitung **bleibt stehen** gewählt hat, bis sie sie aufhebt oder einen neuen Text schickt; mit demselben kurzen Doppelton wie ein Befehl |

Die Zeiten stehen als weiße Schrift mit dem weichen Schatten der Sprecheranzeige in den freien Feldern der Sanduhr, als
`04:52`, ab einer Stunde `1:04:52`. Gerechnet wird mit der Uhr des Servers, die JG TacRelay über die Verbindung laufend
abgleicht (auf etwa eine Zehntelsekunde); eine falsch gehende Uhr an deinem PC stört nicht. Schreibt die Leitung eine Zeile
dazu (höchstens 60 Zeichen, etwa `Hangar 2`), steht sie weiß unter der Grafik. Ist nichts da, ist das Fenster ganz fort.
Wer später einsteigt, bekommt den laufenden Zustand, die Ampel, den Countdown, eine offene Abfrage und eine stehende
Textmeldung, aber keine vergangenen Befehle und keinen Text mit 8 Sekunden.

**Textmeldung.** Die Leitung kann auch nur Text schicken, bis 120 Zeichen. Er steht in der Farbe, die sie gewählt hat
(Weiß, Gelb, Orange, Rot, Grün oder Blau), fett in Segoe UI mit demselben weichen schwarzen Schatten wie die Sprecheranzeige,
ohne Fläche und ohne Rahmen. Die Schrift ist bei normal 26 Punkte wie die Sprecheranzeige (bei Grundgröße 180; sie wächst
und schrumpft mit deiner Grundgröße), doppelt 52, dreifach 78. Was in eine Zeile von etwa 60 Zeichen passt, steht in einer
Zeile, sonst bricht der Text in zwei etwa gleich lange um, nie in mehr. Breiter als dein Monitor wird er nie: Reicht die
Breite für zwei Zeilen nicht, wird die Schrift kleiner, bis sie reicht.

**Größe.** Jede Grafik ist so groß wie deine **Grundgröße** (ab Werk 180 Punkte für die längste Seite), mal dem, was die
Leitung beim Senden wählt: normal, doppelt oder dreifach. Ein Zustand steht immer halb so groß, der Countdown ist ein
Querformat mit 0,6 dieser Höhe. Nie wird eine Meldung größer als dein Monitor; passt eine Reihe nicht in die Breite, wird
sie als Ganzes kleiner.

**Einstellungen.** Unter **Einstellungen**, **Meldungen** (nach **Sprecheranzeige**) stehen offen unter **Wo im Bild** die
Regler **Waagerecht** (0 % links, 50 % Mitte, 100 % rechts) und **Senkrecht** (0 % oben bis 100 % unten), ab Werk 50 % und
0 %, oben in der Mitte, 24 Punkte vom Rand. Anders als bei der Sprecheranzeige wandert der ganze Block mit den Reglern und
bleibt immer im Monitor; bei 50 % steht er mittig. Solange du schiebst, steht eine Probe genau dort über dem Spiel (das Bild
Sammeln mit `Probe: hier stehen die Meldungen`), noch drei Sekunden danach. Einen Schalter zum ganz Abschalten gibt es
nicht. Unter **Erweitert:** stehen:

| Einstellung | Ab Werk | Was sie tut |
| --- | --- | --- |
| Grundgröße | 180 | längste Seite einer Meldung bei normal, 100 bis 400 |
| Ausführung | Schlicht | Schlicht, Metall (mit Rahmen) oder Nur Schrift; gibt es ein Bild in deiner Wahl nicht (Metall fehlt für Los, Stopfen und Pause, Nur Schrift gibt es nur für diese drei), kommt es in Schlicht |
| Bild beim Stopp | Handschuh | der Handschuh oder das Xeno-Stoppschild |
| Ton bei Befehlen, Texten und Countdown | an | der kurze Doppelton bei jedem Befehl und jeder Textmeldung und je Sekunde ein kurzer Ton in den letzten 10 Sekunden des Countdowns, über JG TacRelay mit der Funk-Lautstärke (A3.3); selbst erzeugt, nicht aus einer Datei |

Jede Wahl unter **Erweitert** außer dem Ton zeigt ebenfalls die Probe (beim Stopp-Bild den Stopp). **Zurück auf Werkseinstellung** stellt
diese vier und die Lage zurück.

**Über dem Spiel.** Wie die Sprecheranzeige (A6.8): ein eigenes Fenster, das nie den Fokus nimmt, Mausklicks hindurchlässt,
weder in Alt+Tab noch in der Taskleiste steht und nicht in Star Citizen eingreift. Es steht auf dem Monitor, auf dem Star
Citizen vorn läuft, sonst auf dem zuletzt bekannten, sonst auf dem Hauptmonitor; eine eigene Wahl des Monitors gibt es
nicht. Sichtbar ist es nur, wenn Star Citizen im Modus **Randloses Fenster** läuft. Am echten PC noch nicht ausprobiert (1.8).


## A7 Netze und Rollen

### A7.1 Einsteigen

Seit 0.11.2 hat Einsteigen keine eigene Oberfläche mehr: Auf Start steht nur das Audio (A3), die Karte **Einsteigen** ist
weg. Eine **Rolle** wählst du oben links im Funkfeld unter **Rolle wählen** (A7.3); sie legt ihre Kanäle in deine Felder,
ein Platz auf einer Einheit entsteht dabei nicht. In welche Kanäle du an einem Abend gehörst, teilt die Einsatzleitung auf
der Funkfeld-Zuteilung ein (A12.3); dein Funkfeld stellt sich dann von selbst um, und die Meldezeile sagt, wer es war (A6.7).

Bis 0.11.1 stand auf Start, wenn die Flotte Einheiten hatte, die Karte **Einsteigen: Einheit und Rolle in einem Schritt**
(bis 0.9.3 im Bereich **Meine Kanäle**): Reiter **Schiffe**, **Infanterie**, **Orga** und **Ohne Einheit**, die Listen
**Einheit (Belegung)** und **Rolle (Belegung)** mit **voll** und **dein Platz**, die Netze der Rolle und der Knopf
**Einsteigen**, dazu **Wie letztes Mal: <Einheit>, <Rolle>** für den Platz vom letzten Mal.

### A7.2 Wenn die Einsatzleitung sperrt

Bis 0.11.1 konnte die Einsatzleitung auf dem Brett das Selbstwählen sperren, für höchstens 12 Stunden: **Wechsel
sperren** (wer einen Platz hat, wechselt nicht mehr selbst; Nachzügler ohne Platz steigen noch ein) und **Ganz sperren**
(niemand wählt mehr selbst). Seit 0.11.2 hat die Funkfeld-Zuteilung diese Knöpfe nicht mehr. Sperren lässt sich das Selbstwählen
nur noch an der Konsole des Servers (`funk-admin einstellung --selbstwahl aus`, B12.3), ebenfalls für 12 Stunden;
**Abend beenden** hebt eine solche Sperre auf (A12.4). Die Einsatzleitung kann dich trotzdem jederzeit umsetzen. Setzt sie
dich um, siehst du es im Funkfeld.

### A7.3 Nach einer Unterbrechung

- Bist du innerhalb von 10 Minuten wieder da, etwa nach einem Absturz, stellt der Server deinen Platz still wieder her.
- Einen Platz oder Kanäle, die dir die Einsatzleitung gegeben hat, hält der Server 12 Stunden für dich, auch wenn du
  offline bist. Seit 0.11.2 teilt die Funkfeld-Zuteilung nur noch Kanäle ein, keine Plätze (A12.3). Seit 0.11.0 sagt dir die
  Meldezeile beim Verbinden 12 Sekunden lang in einer Zeile, wer dich eingeteilt hat und auf welchen Platz:
  `Eingeteilt von Nordwind: Schiff 3, Gunner`; ohne Platz `Kanäle eingeteilt von Nordwind`. Deine Kanäle
  und wohin du sendest, zeigen die Kacheln des Funkfelds darunter (in der ersten Fassung von 0.11.0 standen sie mit in der
  Ansage, die dabei auf zwei Zeilen umbrach). In einem Programmlauf kommt dieselbe
  Einteilung (wer, Einheit und Platz) nur einmal, und eine, die du online schon gesehen hast, gar nicht: auch nicht nach
  einer kurzen Trennung, nach einem Phasenwechsel oder wenn du selbst einen Ch. gewählt hast. Nach einem Neustart des
  Programms innerhalb der 12 Stunden kommt sie noch einmal, solange du nichts selbst geändert hast; dann gilt dein Stand als
  selbst gewählt.
- Oben links im Funkfeld steht dann die Rolle deines Platzes, etwa **3 Gunner**, statt **Rolle wählen**. Der Hilfetext
  sagt "Dein Platz: ... (eingeteilt)". Wählst du selbst eine Rolle, legt sie ihre Kanäle in deine Felder; dein Platz
  bleibt. Klappst du die Liste auf, ist die Rolle deines Platzes markiert, und Mausrad oder Pfeile gehen von ihr aus
  einen Schritt weiter.
- Deine selbst gewählte Rolle merkt sich der Client auch dann, wenn dich jemand anders einteilt (seit 0.11.0; bis 0.10.0
  vergaß er sie dabei). Bist du beim nächsten Login nach der Frist von 10 Minuten nicht eingeteilt, stellt er sie wieder
  her. "Zurück auf Standard" durch die Einsatzleitung, auch über **Abend beenden**, ist keine Einteilung: Am nächsten Abend
  bist du wieder auf deiner Rolle.
- Sonst beginnst du mit den Standardnetzen der Flotte (bis 0.11.1 bot der Client dann **Wie letztes Mal** an).

### A7.4 Meine Felder

Deine Felder stehen seit 0.10.0 unter **Einstellungen** (Strg+2), **Empfangen**, **Erweitert**, Gruppe **Je Kanal**:
jedes als Zeile mit Nummer, Netz, `SENDET` beim Sendenetz, **hören** zum An- und Abschalten, **Hören auf** mit **Links**,
**Beide** und **Rechts** und der Lautstärke. Global steht darunter mit "hört immer". VA/Taste stellst du im Funkfeld ein,
nicht mehr hier (A3.5, A6.2). Bis 0.9.3 war das die Karte **Meine Felder** im Bereich **Meine Kanäle**.

Verschwindet die Funkfeld-Zuteilung, während sie vorn ist, etwa weil dein Auftrag als Einsatzleitung endet, springt das
Hauptfenster seit 0.10.0 auf Start (bis 0.9.3 auf **Meine Kanäle**).

### A7.5 Was die Stufen dürfen

| Stufe | Darf |
| --- | --- |
| Funker | in den eigenen Netzen hören und senden, Einheit und Rolle selbst wählen, wenn das erlaubt ist (im Client seit 0.11.2 nur noch die Rolle im Funkfeld, A7.1) |
| Führung | dazu: in Global senden, Durchsagen, in mehrere Netze zugleich senden, alle Sprecher in allen Netzen sehen. Einteilen darf die Führung nicht. |
| Einsatzleitung (Auftrag für einen Abend) | dazu: andere einteilen, stummschalten, das Selbstwählen sperren, die Funkfeld-Zuteilung benutzen, Phasen schalten. Stummschalten und Sperren erlaubt der Server weiter; der Client hat dafür seit 0.11.2 keine Knöpfe mehr (A12.2). |
| Admin | alles, dazu Geräte freigeben, Konten und Netze verwalten (nur in einer Admin-Sitzung) |

Ein Admin-Konto teilt nur ein Admin um oder schaltet es stumm. Eine Ausnahme gibt es seit 0.5.0: Spielt ein Admin heute
als Funker (A12.7), teilt ihn die Einsatzleitung ein wie jeden anderen; stummschalten darf ihn weiter nur ein Admin. Phasen
schaltet, wer einteilen darf (A12.5). Meldungen an alle (seit 0.12.0, A12.8) senden Führung, Admins und die Einsatzleitung
mit laufendem Auftrag, aber nur, wenn ein Admin auf ihrer Mitgliederkarte den Haken **darf Meldungen senden** gesetzt hat;
**Heute als Funker** sendet nie. An der Konsole heißen die Stufen FUNKER, RAIDLEITER
(Führung) und ADMIN; die Einsatzleitung ist keine Stufe, sondern ein Auftrag für einen Abend (B11.4, B15). Die Rechte
im Einzelnen stehen in C13.


## A8 Wer ist da

Der Knopf **Wer ist da** oben im Hauptfenster (oder Strg+6) öffnet rechts eine Spalte mit allen, die gerade verbunden
sind. Auf dem Knopf steht, wie viele es sind, etwa `Wer ist da (23)`. Auf Start in der kleinsten Größe macht die Spalte
das Fenster breiter, oder sie legt sich, wenn der Bildschirm dafür zu schmal ist, wie eine Schublade über Start; beim
Schließen geht das Fenster zurück, solange du es nicht selbst verändert hast (A2.5, **Wer ist da auf Start**).

Seit 0.11.5 steht jede Person in einer eigenen Zeile, alle untereinander, eine feste Spalte, die rollt, wenn die Liste zu
lang wird. Oben stehen alle, die angemeldet sind, nach Rang: Admins, dann Führung, dann Einsatzleitung, dann alle anderen,
je nach Namen. Den Rang kennt die Einsatzleitung aus der Liste aller Mitglieder, die Wer ist da für sie beim Öffnen holt;
alle anderen sehen ihn nur am Zeichen, wer sein Zeichen nicht zeigt, steht für sie unter "alle anderen". Abzeichen ändern den
Rang hier nicht. In jeder Zeile
stehen von links nach rechts genau fünf Knöpfe (Sarevoc 14.10.2026: "erst Abzeichen, dann Name"):

| Knopf | Was er zeigt | Was ein Klick tut |
| --- | --- | --- |
| **Abzeichen** | Seit 0.11.7 das Abzeichen, das die Person vorn zeigt: ihre Wahl unter **Meine Abzeichen** (A9.1), sonst ihr erstes sichtbares (Kranz, Stern, dann Ränge in der Folge Marine, Navy, Orga, dann Tätigkeiten); ohne Abzeichen der Anfangsbuchstabe im Kreis. Dazu ein grüner Punkt, wenn sie angemeldet ist. Der Hilfetext nennt alle sichtbaren Abzeichen, etwa `Distel: Marine: Leutnant, Sanitäter, Ausbilder. Online.`; was die Person ausgeblendet hat, steht nirgends. | Wie der Status: nur im Admin-Setup und nur als eingerichteter Admin öffnet ein Klick die **Verwaltung**, Seite **Mitglieder**, mit genau diesem Mitglied. Abzeichen vergibt ein Admin auf der Seite **Abzeichen** (A9.2). Sonst nur Anzeige. |
| **Name** | Der Name. Alle Namen sind gleich breit, keiner wird gekürzt: Ein Name wie "Superschütze/Alexander" (22 Zeichen) steht in voller Schrift; ein breiterer, etwa 22 Großbuchstaben oder ein Discord-Name bis 32 Zeichen, steht in kleinerer Schrift, aber ganz. Den SC-Namen, den Platz und "online" oder "offline" nennt der Hilfetext. | Mit der Einsatzleitung öffnet er die Funkfeld-Zuteilung (A12). Sonst ist er nur Anzeige. |
| **Mikrofon** | Ist die Person stumm geschaltet, ist das Mikrofon durchgestrichen und blutrot; der Hilfetext sagt, bis wann. Läuft die Zeit ab, wird es von selbst wieder normal. Ob andere stumm sind, sieht nur die Einsatzleitung. | Nur für die Einsatzleitung (Admin oder Auftrag für den Abend): Klick schaltet die Person in allen Kanälen stumm, ein zweiter Klick hebt es auf. Es gilt, bis du es aufhebst, bis **Abend beenden** (A12.4) oder höchstens eine Stunde, länger erlaubt der Server nicht. Einen Admin schaltet nur ein Admin stumm, dich selbst gar nicht. Hat ein anderer die Person stumm geschaltet, hebst du es nur auf, wenn du Admin bist oder eine höhere Stufe hast als er; sonst sagt der Hilfetext, wer es gesetzt hat. Kennt Wer ist da den Rang einer Person noch nicht (sie kam eben erst dazu), ist das Mikrofon so lange nur Anzeige; die Liste holt ihn von selbst, **neu** sofort. Für alle anderen ist es nur Anzeige. |
| **Status** | Ein grüner Kreis mit einer Zahl (Sarevoc 14.10.2026): in so vielen Kanälen sitzt die Person, `1` heißt in einem, höchstens `9`; gezählt wie auf der Funkfeld-Zuteilung. Grün heißt: die Person sitzt in mindestens einem Kanal der Funkfeld-Zuteilung (nicht Global, kein Netz einer Einheit), wie die Zahl an ihrem Namen dort (A12.2). Gelb: in keinem solchen Kanal, auch wenn sie auf einer Einheit sitzt. Ein grauer Ring: das weiß Wer ist da nicht (die Kanäle anderer sieht nur die Einsatzleitung, und nur bei Mitgliedern mit Sitzung oder Platz). | Nur im Admin-Setup und nur als eingerichteter Admin: Klick öffnet den Bereich **Verwaltung** (Strg+9), Seite **Mitglieder**, mit genau diesem Mitglied. Ist die Admin-Sitzung noch zu, beginnt die Verwaltung nach der Anmeldung bei **Mitglieder** mit seiner Karte. Wer als Admin erst vorgesehen, halb eingerichtet oder gesperrt ist, sieht den Status nur als Anzeige. |
| **Wippe** | links ein Minus, rechts ein Plus | Links leiser, rechts lauter, je Klick 2 dB, von -30 bis +12 dB; der Hilfetext nennt den Wert. Es ist dein eigenes Hören, dasselbe wie unter **Empfangen** (A3.5), und gilt nur für dich. In deiner eigenen Zeile gibt es keine Wippe. |

- **Suchen:** über Name, SC-Name und Discord-Name, auch Teile: "max" findet "Mustermann/Max". Untergilden und ihre Kürzel
  gibt es seit 0.11.8 nicht mehr.
- **lösen** (oben neben **neu**, seit 0.11.5): Wer ist da geht als eigenes Fenster neben das Hauptfenster, das du frei hinschieben
  kannst; die Spalte im Hauptfenster ist dann weg. **andocken** oder das Fenster schließen legt die Liste zurück in die Spalte. Auf Start
  und Einstellungen steht die Spalte seit 0.11.5 direkt neben dem Inhalt, ohne Lücke.
- **auch offline** (nur für die Einsatzleitung, seit 0.11.0): zeigt zusätzlich alle, die gerade nicht verbunden sind, als
  hohlen Ring mit gedämpftem Namen, unter den Angemeldeten. Die Suche gilt auch für sie. Der Schalter merkt sich nichts und
  geht aus, wenn dein Auftrag endet. Die Zahl auf dem Knopf zählt weiter nur die Angemeldeten. Klappt das Holen der
  Mitgliederliste einmal nicht, steht "Auch offline: ..." da, bis es wieder klappt oder du den Schalter ausmachst.
- **neu** baut die Liste neu auf, mit der Einsatzleitung holt es auch die Liste der Mitglieder frisch vom Server (für
  **auch offline** und den Rang).
- Geht ein Klick schief (etwa ohne Recht), steht der Grund oben, bis ein Klick gelingt.

Bis 0.11.4 standen die Personen als Kacheln, drei je Reihe, nach Untergilde oder Einheit gruppiert, mit dem gelben "!" an
Dauersendern (das steht weiter im Funkfeld und in der Titelleiste), und ein Klick öffnete unten die Karte mit SC-Namen,
Untergilde, Platz, Verbindung und Rollen. Die Karte gibt es in Wer ist da nicht mehr. Der Schalter **Meine Angaben in Wer
ist da zeigen** unter **Ich** (A9) wirkt weiter: Ohne ihn sehen alle außer Einsatzleitung und Admins dein Zeichen und deine
Abzeichen nicht (auch nicht im Funkfeld); Rollen zeigt Wer ist da keinem mehr.


## A9 Ich: Namen, Rollen und Zeichen

Ganz oben unter **Einstellungen** (Strg+2) steht offen die Karte **Ich** mit deinen eigenen Angaben. Namen, Rollen und
die Sichtbarkeit liegen an deinem Konto auf dem Server, nicht auf dem PC; ändern kannst du sie deshalb erst, wenn du
verbunden und freigegeben bist. Vor der Freigabe stehen die Namen auf Start (A2.2). Seit 0.10.0 ist das eine Karte mit
einem Knopf **Speichern**; bis 0.9.3 waren es drei Karten mit **Namen speichern** und **Wünsche speichern**.

**Namen**

- **Mein SC-Name:** dein Name in Star Citizen, seit 0.11.2 höchstens 22 Zeichen (A1.3).
- **Weitere SC-Namen:** bis zu zwei Zweitkonten im Spiel, durch Komma getrennt.
- **Mein Discord-Name:** wird nur angezeigt, darunter `Den ändert nur ein Admin.`; stimmt er nicht, sag es einem Admin.

SC- und Discord-Namen sind eindeutig. Ist einer schon vergeben, sag es dem Admin.

**Rollen**

- Unter **Rollenwünsche; die erste Wahl trägt den Stern** steht jede Rolle des Rollenkatalogs als Schalter, nebeneinander
  und umbrechend. Ein Klick wünscht dir die Rolle (der Schalter leuchtet), ein zweiter nimmt den Wunsch zurück; bis zu 8
  **Lieblingsrollen**. Die Gruppe der Rolle nennt der Hilfetext.
- Rollen, die ein Admin für dich eingetragen hat, stehen an ihrer Stelle mit Schloss, eingeschaltet und gesperrt: Sie
  gelten sofort und lassen sich nicht abwählen. Bis zur Prüfung von 0.10.0 standen sie zusätzlich als eigene Liste
  **Vom Admin eingetragen** darüber.
- Neben jeder gewählten und jeder eingetragenen Rolle steht ein kleiner Stern. Ein Klick darauf macht sie zu deiner **ersten
  Wahl**; der Stern ist dann gefüllt, und es gibt genau eine.
- Gibt es noch keinen Rollenkatalog, steht das da; ein Admin legt ihn an. Führung und Einsatzleitung können Rollen zum
  Katalog hinzufügen, seit der Prüfung von 0.10.0 unter **Ich**, **Erweitert**, Gruppe **Rollenkatalog**: **Rolle zum
  Katalog hinzufügen**, Name und Gruppe (etwa Flug, Geschütz, Technik, Boden, Orga), dann **Hinzufügen**; höchstens 64
  Rollen.

**Speichern** speichert Namen und Wünsche zusammen, jeweils nur, was du geändert hast. Ist ein Name ungültig, werden die
Wünsche trotzdem gespeichert; die Fehler stehen getrennt da. Ohne Änderung steht dort `Nichts geändert.` Ohne Verbindung
ist der Knopf gesperrt.

Bist du Admin und der Server ist ab 0.5.0, steht in derselben Karte darunter **Heute als Funker (bis 04:00)**. Wozu das
dient, steht in A12.7.

**Zeichen und Sichtbarkeit** steht unter **Ich**, **Erweitert**:

| Schalter | Wirkung | Ab Werk |
| --- | --- | --- |
| Mein Zeichen anderen zeigen | ob dein Zeichen (etwa der Lorbeerkranz) und deine Abzeichen neben deinem Namen erscheinen. Seit 0.11.7 heißt aus: alle aus, egal was unter **Meine Abzeichen** steht | an |
| Meine Angaben in Wer ist da zeigen | Zeichen und Abzeichen für alle. Aus heißt: Andere sehen nur deinen Namen, ohne Zeichen und Abzeichen (in Wer ist da und im Funkfeld); die Einsatzleitung und Admins sehen weiter alles. Bis 0.11.7 auch die Untergilde. Bis 0.11.4 auch Rollen und Wünsche auf der Karte in Wer ist da; die Karte gibt es seit 0.11.5 nicht mehr (A8). | an |
| Zeichen neben Namen sehen | ob du die Zeichen der anderen siehst | an |
| Zeichen auch im Funkfeld | ein kleines Zeichen auch auf den Feldern | aus |

Die ersten beiden liegen am Konto, die beiden letzten gelten für deinen PC. Nur die beiden letzten zählen als "selbst
gestellt" und stellt **Zurück auf Werkseinstellung** zurück; was am Konto liegt, lässt der Knopf stehen (A2.5).

### A9.1 Meine Abzeichen

Seit 0.11.7 steht unter **Ich**, **Erweitert**, gleich unter **Mein Zeichen anderen zeigen**, die Liste **Meine Abzeichen**
(Sarevoc: "ein Abzeichen haben ist eine Sache, es zeigen eine eigene Entscheidung"). Darin steht jedes Abzeichen, das du hast:
der Kranz, wenn du eingerichteter Admin bist, der Stern, solange dein Auftrag läuft, dann deine Ränge und Tätigkeiten.
Vergeben hat sie ein Admin (A9.2); selbst nehmen oder umbenennen kannst du keins. Je Zeile:

| Teil | Was er tut |
| --- | --- |
| Bild und Titel | etwa `Marine: Sergeant` für einen Rang oder `Gunner M` für eine Tätigkeit |
| **vorn** | dieses Abzeichen steht vor deinem Namen in Wer ist da und auf der Funkfeld-Zuteilung. Es gibt eine Wahl: Schaltest du ein anderes ein, geht das bisherige aus. Ein zweiter Klick nimmt die Wahl zurück; dann steht dein erstes sichtbares vorn. Ist das gewählte gerade ausgeblendet oder nicht mehr deins, steht ebenfalls das erste sichtbare vorn. |
| **zeigen** | ohne Haken sehen die anderen dieses Abzeichen nicht, weder vorn noch im Hilfetext. Das geht für jedes einzeln, auch für Kranz und Stern. Rechte ändert es nie: Ein Admin bleibt Admin, auch ohne Kranz. |

Jede Änderung geht sofort an den Server und liegt an deinem Konto, nicht auf dem PC; ohne Verbindung steht
`Das gilt erst mit Verbindung; es steht am Konto, nicht auf diesem PC.` darunter. Hast du noch keine Abzeichen, steht dort
`Du hast noch keine Abzeichen. Ränge und Tätigkeiten vergeben die Admins.` Der Schalter **Mein Zeichen anderen zeigen**
darüber ist der Schalter für alle: Aus heißt alle aus. Admins sehen auf der Seite Abzeichen trotzdem alles, was du hast.

### A9.2 Die Seite Abzeichen (Admins)

Abzeichen vergeben nur Admins, in der Admin-Sitzung (Recht `KONTEN_VERWALTEN`), nie die Einsatzleitung. Dafür gibt es im
Admin-Setup ganz unten im linken Menü den Eintrag **Abzeichen**. Er erscheint erst, wenn du unter **Verwaltung** mit dem
Code angemeldet bist, und verschwindet, sobald die Admin-Sitzung endet; war die Seite gerade offen, geht es auf Start.
Ein Tastenkürzel hat sie nicht, mit den Pfeiltasten erreichst du sie in der Leiste wie die anderen Bereiche.

- **Links** stehen alle Mitglieder mit Konto (keine Vormerkungen, keine offenen Anträge) nach SC-Namen, darunter je
  Mitglied klein alle seine Abzeichen, auch die, die es selbst ausgeblendet hat. **Suchen** findet SC-Namen, Discord-Namen
  und Namen, auch Teile. Ein Klick wählt das Mitglied.
- **Rechts** steht das gewählte Mitglied. Unter **Ränge (einer je Laufbahn)** je Laufbahn eine Zeile mit Bild, Rang und
  Stufe, etwa `Marine  Sergeant (Stufe 4 von 10)`, und den Knöpfen **befördern** (eine Stufe höher), **zurückstufen** (eine
  tiefer) und **entfernen** (den Rang ganz weg). Ohne Rang steht dort **aufnehmen**: das gibt den ersten Rang der
  Laufbahn. Am Ende der Leiter ist der Knopf gesperrt, der Hilfetext sagt warum, etwa `Oberst ist der höchste Rang der
  Marine; höher geht es nicht.` Jeder Mensch hat in jeder Laufbahn höchstens einen Rang; befördern ersetzt ihn.
- Darunter **Tätigkeiten (n von höchstens 6)**: alle 22 nach Gruppe (Piloten, Waffen, Technik und Versorgung, Erkundung und
  Gewerbe) als Schalter mit Bild. Ein Klick vergibt die Tätigkeit, ein zweiter nimmt sie weg. Bei 6 ist Schluss: Der
  Schalter springt zurück, und dort steht `Höchstens 6 Tätigkeiten je Mitglied. Nimm erst eine weg, dann geht eine neue.`

Jede Änderung wirkt sofort, ohne Speichern; über der Liste steht, was geschehen ist, etwa `Falke ist jetzt Navy: Petty
Officer.` Lehnt der Server ab, steht der Grund in Rot, und die Karte zeigt wieder den gespeicherten Stand. Bilder und Namen
der Abzeichen kommen mit dem Programm; anlegen oder umbenennen lässt sich im Programm nichts (wie ein neues Abzeichen
entsteht, steht in B11.7). Was ein Mitglied zeigt, entscheidet es selbst (A9.1).


## A10 Discord gleichzeitig nutzen

Viele haben neben JG TacRelay auch Discord offen. Das geht, wenn ein paar Dinge stimmen.

**Ein Mikrofon für beide.** JG TacRelay öffnet das Mikrofon im geteilten Modus von Windows. In diesem Modus können
mehrere Programme dasselbe Mikrofon zugleich benutzen; JG TacRelay nimmt es Discord nicht weg. JG TacRelay verarbeitet
den Ton dabei für sich, unabhängig von den Einstellungen in Discord.

**Verschiedene Tasten.** JG TacRelay lässt jede Taste durch: Discord und Star Citizen bekommen sie ebenfalls. Liegt die
Sprechtaste von Discord auf derselben Taste wie die von JG TacRelay, sprichst du in beide zugleich. Nimm also zwei
verschiedene Tasten. Steht Discord auf Sprachaktivierung, geht alles, was du sagst, auch dorthin; im Einsatz ist Discord
deshalb besser auf Sprechtaste oder stumm.

**Abschwächung in Discord auf 0 %.** Discord kann andere Programme leiser stellen, solange jemand in Discord spricht
(Einstellung "Abschwächung" in den Sprach-Einstellungen von Discord). Dann würde der Funk mitten im Einsatz leise. Stell
die Abschwächung auf 0 %.

**Windows: Kommunikation "Nichts unternehmen".** Windows selbst kann alle anderen Töne dämpfen, sobald ein Programm wie
Discord ein Gespräch öffnet. Das stellst du in der Systemsteuerung unter **Sound**, Reiter **Kommunikation** ab: **Nichts
unternehmen**. JG TacRelay selbst meldet sich bei Windows als Spiel-Chat und so, dass Windows seinetwegen nichts leiser
stellt; Star Citizen wird durch JG TacRelay also nicht leiser. Was Discord auslöst, regelt aber nur diese Einstellung.

Die genauen Menünamen hängen von der Fassung von Discord und Windows ab; sie sind hier nicht am Gerät nachgesehen.

**Geräte fest wählen.** Ohne eigene Wahl nimmt JG TacRelay das Standardgerät für Kommunikation, Discord eventuell ein
anderes. Wähle in beiden Programmen Mikrofon und Ausgabe ausdrücklich, dann weißt du, was wohin geht.


## A11 Wenn als Spieler etwas nicht klappt

| Meldung oder Lage | Bedeutung | Was du tust |
| --- | --- | --- |
| `Server nicht erreichbar (...)` | Der Server antwortet nicht. | Internet prüfen. Der Client versucht es von selbst weiter. Bleibt es so, Betreiber fragen. |
| `Unter der Adresse antwortet kein JG-TacRelay-Server ...` | Port oder Portfreigabe stimmt nicht. Eine Portfreigabe ist die Weiterleitung im Router, die Anfragen von außen auf einem Port an den Server im Heimnetz gibt. | Betreiber fragen. |
| `Server nicht vertrauenswürdig: ...` oder `Unter der Adresse antwortet ein anderer Server ...` | Der Schlüssel des Servers passt zu keinem Pin im Profil. | Nichts selbst ändern, keine Profildatei von anderen annehmen. Betreiber fragen; vielleicht braucht es ein neues Setup. |
| `Verbunden (Tunnel)` bleibt stehen | UDP kommt nicht durch. | Es geht trotzdem, mit etwas mehr Verzögerung. Dem Betreiber sagen; er prüft die UDP-Freigabe. |
| `Diese Version von JG TacRelay ist zu alt für den Server. Bitte aktualisieren.` | Der Server verlangt eine neuere Fassung. | Neues Setup vom Admin, **Aktualisieren**. |
| `Dein Konto hat sich auf einem anderen Gerät angemeldet. ...` | Du bist woanders verbunden; je Konto geht nur eine Verbindung. | **Verbinden** holt die Verbindung hierher zurück. |
| `Zu oft zwischen Geräten gewechselt. ...` | zu viele Wechsel zwischen deinen Geräten | Ein Admin muss das freigeben. |
| `Das Konto ist gesperrt.` oder `Dieses Gerät ist gesperrt ...` | gesperrt | an einen Admin wenden |
| `Dieses Gerät wurde vom Funk abgemeldet (entfernt). ...` | entfernt oder Konto gelöscht | verbindet nicht von selbst; Verbinden stellt einen neuen Antrag |
| `Dein Antrag ist nicht mehr offen: ...` | Ein Admin hat den Antrag abgelehnt, oder er ist nach 24 Stunden verfallen (seit 0.7.0). Gesperrt ist nichts. | mit einem Admin sprechen, dann **Verbinden**; das stellt einen neuen Antrag |
| `Der Server wird gewartet. Neuer Versuch in etwa ...` | Neustart oder Update | warten; der Client kommt von selbst wieder |
| `Mikrofon aus: ...` | Mikrofon lässt sich nicht öffnen | Gerät prüfen, Mikrofonzugriff in Windows erlauben, auf Start **Erneut versuchen** |
| Neben der Hörprobe steht `Geht erst, wenn das Audio läuft.` | Audio ist nicht offen, etwa weil **Audio beim Start öffnen** aus ist. Auf Start steht dazu seit 0.11.2 nichts weiter (bis 0.11.1 `Mikrofon und Wiedergabe sind zu.` mit **Audio starten**). | **Einstellungen**, **Fehlersuche**, **Audio starten** (A3.2). Ließ es sich nicht starten, steht daneben `Audio ließ sich nicht starten: ...` mit dem Grund (ganz im Hilfetext), etwa ein Gerät, das ein anderes Programm belegt. |
| Niemand hört dich | | Sieh in die Statuszeile **Mikrofon** unten: Steht dort `SENDET`, wenn du sprichst? Zeigt die Marke `Ruhe` oder `Getrennt`, oder wird der Balken gelb statt grün, geht es nirgends hin (A2.5). Ist ein Sendenetz grün? Steht es auf **Taste** (bei Global ist das immer die Voreinstellung, sonst bei Netzen, die der Admin mit `--taste` angelegt hat oder die vor 0.5.0 an der Konsole angelegt wurden, A5.2), und ist eine Sprechtaste belegt? Deinen Klang prüfst du mit der **Hörprobe** auf Start, das Senden mit dem Headset über **Beim Senden mithören** oder im Netz Echo (A3.4). |
| Ein neues Headset fehlt in der Auswahl | | Seit 0.10.0 liest JG TacRelay die Geräte von selbst neu, wenn Windows ein Gerät meldet. Fehlt es trotzdem, prüf, ob Windows es kennt (Sound-Einstellungen). |
| Etwas geht nicht, und du weißt nicht, warum | | **Einstellungen**, **Fehlersuche**, **Erweitert**: dort stehen der Klangtreiber, Warnungen beim Start und die letzte Meldung der Klangmaschine; **Klangprotokoll schreiben** und **Ordner öffnen** helfen, wenn ein Admin oder Sarevoc nachsehen soll. |
| Der Funk wird leise, sobald in Discord jemand spricht | Discord oder Windows dämpft andere Programme. | In Discord die Abschwächung auf 0 %, in Windows unter **Sound**, **Kommunikation** **Nichts unternehmen** (Kapitel A10). |
| Funkfeld fehlt über dem Spiel | | Hast du es abgelegt (X, Taste oder Symbol)? Dann holt es die Taste **Funkfeld ablegen/holen**, ein Klick auf das Symbol im Infobereich (vielleicht hinter dem Pfeil ^) oder Strg+F im Hauptfenster (A6.6). Sonst Star Citizen auf **Randloses Fenster** stellen. |
| `Melden geht nicht: ...` in der Meldezeile | Die Melde-Taste hat kein Meldenetz oder kein Senderecht dort. | A4.4: Meldenetz wählen, Netz in ein Feld mit Senderecht legen lassen. |
| `Nicht gesendet: kein Senderecht mehr in ...` | Die Einsatzleitung oder eine Phase hat dir das Senderecht im Netz genommen. | Bei der Einsatzleitung nachfragen; die Meldezeile nennt, wer es war (A6.7). |
| Die Funkfeld-Zuteilung (Strg+5) ist verschwunden, obwohl du Admin bist | Du spielst heute als Funker, und dabei ruht die Funkfeld-Zuteilung bis 04:00. | In **Einstellungen**, Karte **Ich** auf **Wieder Admin** (A12.7). |
| Strg+3 oder Strg+4 tun nichts | Seit 0.10.0 gibt es die Bereiche Audio und Meine Kanäle nicht mehr. | Strg+1 Start, Strg+2 Einstellungen (A2.5). |
| Neuer PC | | Neuer PC heißt neuer Antrag und seit 0.7.0 ein eigenes Konto (ein PC, ein Konto). Sag dem Admin, dass es dein PC ist, und nenn ihm die Gerätekennung. Ist der alte PC weg, kann er dessen Konto löschen. |


## A12 Für die Einsatzleitung: die Funkfeld-Zuteilung

### A12.1 Wer die Funkfeld-Zuteilung sieht

Die **Funkfeld-Zuteilung** (Bereich **Funkfeld-Zuteilung**, Strg+5, bis 0.11.5 **Raid**; bis 0.11.1 hieß sie in diesem Handbuch **das Brett**) sieht nur, wer gerade
einteilen darf: ein Admin oder wer einen **Auftrag** für den Abend hat. Ein Admin, der heute als Funker spielt (A12.7),
sieht sie bis 04:00 nicht. Sie erscheint und verschwindet ohne Neustart. Den Auftrag vergibt ein Admin, im
Verwaltungsteil unter **Einsatzleitung** oder an der Konsole des Servers (B15). Er gilt ab Werk bis 04:00 Uhr, höchstens
24 Stunden, und kann bis 14 Tage im Voraus geplant werden. Er endet auch, wenn der Admin ihn entzieht oder du ihn abgibst
(seit 0.11.2 mit **Abend beenden ...** und **Auftrag danach abgeben**, A12.4). Seit 0.12.0 sieht die Seite auch, wer
Meldungen senden darf (A12.8): Ohne Auftrag, etwa als Führung, steht dann nur das Feld **Meldungen** darauf, ohne Namen
und Baum.

Oben links steht **LIVE - wirkt sofort**, daneben, wofür du einteilst, etwa
`Einsatzleitung bis 04:00 (von <Name>): <Grund>` oder `Einsatzleitung als Admin`. 15 Minuten vor dem Ende warnt der Kopf:
"Noch 12 Minuten, dann endet die Einsatzleitung." Jede Ablage gilt sofort. Eine eigene Planung im Voraus gibt es noch
nicht; wer offline ist, lässt sich aber schon vorab in Kanäle ziehen (A12.3).

### A12.2 Aufbau

| Wo | Was |
| --- | --- |
| oben, im Kopf | **LIVE - wirkt sofort** mit dem Auftrag, rechts **Rückgängig** (Strg+Z) und **Abend beenden ...** (A12.4); sonst nichts |
| links | **Teilnehmer**: **Suchen** (Strg+E), darunter alle Namen, wer live ist oben, wer offline ist unter einer Linie |
| rechts | der **Baum**: alle Kanäle mit ihren Ch. und den Leuten darin |
| unten | eine Zeile, nur wenn es etwas zu sagen gibt, etwa warum eine Ablage nicht ging |

**Links** steht jeder Name als Knopf über die ganze Breite, wie ein Name in **Wer ist da** (A8): Abzeichen oder
Anfangsbuchstabe, grüner Punkt für live, hohler Ring für offline, der Name ganz. Oben stehen alle, die live sind, nach
Rang: Admins, dann Führung, dann Einsatzleitung, dann alle anderen, je nach Namen. Unter einer Linie folgen alle, die
offline sind, ebenso sortiert; sie kannst du vorab einteilen. Rechts im Namen steht die Zahl der Kanäle, in denen die
Person sitzt: `1` heißt in einem Kanal. **Ruhe** zählt mit, Global und die Netze einer Einheit nicht; ohne Kanal steht
keine Zahl. Was sonst über die Person zu sagen ist (Discord-Name, Platz, online oder offline, ob Admin, zuletzt),
steht im Hilfetext. Die Suche findet Name, SC-Name und Discord-Name auch in Teilen (ein Kürzel der Untergilde gibt es seit
0.11.8 nicht mehr); wer nicht passt, wird blass und bleibt stehen. Ein
Name, der auf den Server wartet, ist gedimmt.

**Rechts oben** steht seit 0.11.3 eine Zeile **Reiter** wie Karteireiter, mit runden Ecken und sichtbar überlappend:

- Der erste Reiter ganz links ist die **Rollenwahl** wie im Funkfeld: Ein Klick klappt die Rollen auf, in der Vorlage
  Star Citizen beginnend mit **Standard (Gast)**, dann **Flottenführung** und so weiter.
- Rechts daneben stehen als Reiter die ersten fünf Kanäle der gewählten Rolle, in der Folge ihrer Felder, ohne Global
  und ohne die Netze der Einheiten. Bei **Kapitän** etwa Brücke, Crew, Flotte, Kommando, Sanität.
- Ein Klick auf einen Kanal-Reiter öffnet ihn; der offene ist dunkel wie die Fläche darunter, fett, mit einem leicht
  glühenden Strich in Petrol (bis 0.12.0 weinrot). Hältst du beim Ziehen einen Namen über einen Reiter, öffnet er sich.

**Darunter** steht der **Baum** des offenen Kanals. Er wächst wie ein Stammbaum nur nach rechts und unten:

- Links steht der Kanal, immer aufgeklappt.
- Rechts daneben stehen bei einem Kanal mit Ch. **Ruhe** (immer, kursiv) und die Ch., in denen jemand sitzt. Die leeren
  stehen hinter **N weitere Ch.**: Ein Klick zeigt sie (der Eintrag heißt dann **leere Ch. zu**), und beim Ziehen klappen
  sie auf, wenn du kurz darauf bleibst.
- Ganz rechts stehen die Namen der Leute in diesem Ch., bei einem Kanal ohne Ch. gleich rechts neben dem Kanal.
- Nur drei Farben: Kanäle und ihre Linien zu den Ch. sind orange, die Linien vom Ch. zu den Namen und der Rahmen eines
  besetzten Ch. weiß, leere Ch. grau. Die Linien laufen gerade, mit runden Ecken.
- Der Baum folgt dem Server, auch wenn die Person selbst im Funkfeld wechselt.

In 0.11.2 gab es keine Reiter, alle Kanäle standen untereinander in einem Baum und ließen sich mit einem Pfeil auf- und
zuklappen.

Seit 0.11.2 nicht mehr auf dieser Seite: die Einheiten mit ihren Plätzen in der Mitte (samt Ampel und Belegung), die
Netze rechts mit **hört** und **hört + sendet**, das Detailfeld mit dem **Ch.**-Raster, die Phasenleiste (Phasen schaltest
du mit den Tasten, A12.6), **Wechsel sperren** und **Ganz sperren** (A7.2), das Sperren einzelner Kanäle für eine
Person, **Wie zuletzt für alle ohne Platz**, der **Verlauf**, das Menü an einem Namen (rechte Maustaste) und **Stumm**.
Bis 0.11.1 standen links die Listen **Neu im Funk**, **Ohne Platz** und **Alle Mitglieder**, und jede Person auf einem
Platz war eine **Karte**; ein Admin trug ein Schloss.

### A12.3 Einteilen

Du ziehst Namen mit der Maus. Jeder Zug ist genau ein Kanal:

| Du ziehst | Wirkung |
| --- | --- |
| einen Namen von links auf einen Ch. | Die Person ist in diesem Kanal auf diesem Ch. und kann sofort funken: Sie hört und sendet dort, und das Feld ist fest. Hat sie in diesem Kanal schon einen Ch., auch **Ruhe**, geht genau dieses Feld auf den neuen Ch.; zwei Ch. im selben Kanal bekommt sie nicht. |
| einen Namen auf **Ruhe** | Die Person ist im Kanal eingeteilt und hört dort, sendet aber nicht: Ruhe ist ein Platz ohne Sendefunktion. |
| einen Namen auf einen Kanal ohne Ch. | wie auf einen Ch.: hört und sendet dort, fest |
| einen Namen auf einen Kanal mit Ch. selbst | nichts; zieh auf einen seiner Ch. Über einem Kanal-Reiter öffnet sich der Reiter; bleibst du über **N weitere Ch.** kurz stehen, etwa 0,7 Sekunden, zeigen sich die leeren Ch. |
| einen Namen im Baum auf einen anderen Ch. | umsetzen: Die Person verlässt ihren Ch. und ist im neuen, im selben Kanal wie in einem anderen; ein zweites Feld entsteht nicht. |
| einen Namen aus dem Baum nach links | heraus aus diesem Kanal; das geht auch bei einem Kanal, der aus der Rolle der Person kommt, dann für diesen Abend |
| einen Namen von links nach links | nichts |

Beim Ziehen färbt sich der Rand des Ziels: grün, wenn es passt, rot, wenn es nicht geht. Links in der Liste steht beim
Herausziehen der Satz dazu, etwa `<Name> aus <Kanal> heraus`; geht eine Ablage nicht, steht der Grund nach dem Loslassen
unten, etwa `<Name> ist Admin - teilt sich selbst ein` oder `<Name> ist in <Kanal> gesperrt`. Ist die Person schon auf
diesem Ch., geschieht nichts. Was nur fürs Ziehen aufgeklappt wurde, klappt danach wieder zu; ein Kanal nur, wenn niemand
darin sitzt. Bis der Server antwortet, ist der Name gedimmt. Hat jemand anderes die Person inzwischen umgestellt, sagt
es die Zeile unten, statt seine Umstellung zu überschreiben; stellt gerade eine Phase um, steht dort `Inzwischen durch
Phase 2 Entern umgestellt - bitte noch einmal ziehen.`

**Rückgängig** oder Strg+Z nimmt jeden deiner Züge zurück, einen nach dem anderen: Die Person hat danach wieder die Felder
von vorher. Hat sie jemand danach umgestellt, bleibt dessen Stand, und unten steht `Nicht zurückgenommen: <Name> wurde
danach umgestellt.`

**Vorab einteilen.** Ziehst du jemanden, der offline ist (unter der Linie), auf einen Ch., gilt das wie eine Reservierung
12 Stunden lang. Mit einem Server ab 0.11.2 steht er dann im Baum, obwohl er offline ist und keinen Platz hat; ein älterer
Server zeigt ihn dort nicht. Meldet er sich an, liest er in seiner Meldezeile, dass und von wem er eingeteilt ist
(`Kanäle eingeteilt von <Name>`, A7.3).

**Admins.** Einen Admin, der nicht heute als Funker spielt, setzt nur ein Admin um; sonst ist die Ablage rot. Spielt er
heute als Funker, ziehst du ihn wie jeden anderen (A12.7). Ob jemand Admin ist, sagt der Hilfetext seines Namens.

**Ohne Maus** gibt es seit 0.11.2 keinen Weg zum Einteilen: Detailfeld, Menü und Ch.-Raster sind weg. Plätze auf Einheiten
setzt die Funkfeld-Zuteilung nicht mehr; wer von früher einen Platz hat, behält ihn.

Bis 0.11.1 zog man auf dem Brett Karten auf Plätze, Spaltenköpfe und Netze, mit **hört** und **hört + sendet**, einem
Ch.-Raster, kleinen Kacheln unter den Netzen und einer Vorschau beim Ziehen; **Rückgängig** nahm nur Plätze zurück,
keinen Ch. Das Ziehen der Karten von 0.11.0 hat Sarevoc am 12.10.2026 am echten PC ausprobiert. Die Funkfeld-Zuteilung von 0.11.2
ist im Modell und im Bildlauf geprüft, am echten PC noch nicht (1.8). Probier Neues zuerst im Probefunk (A2.1).

### A12.4 Weitere Knöpfe

| Knopf | Wirkung |
| --- | --- |
| **Rückgängig** (Strg+Z) | nimmt deine letzte Umstellung zurück, Zug für Zug, nicht aber, was jemand danach geändert hat (A12.3) |
| **Abend beenden ...** | schaltet, wenn eine Phase über der Grundstellung läuft, zuerst auf Phase 1 zurück; gibt alle Plätze frei, die niemand mehr nutzt (auch Reservierungen), hebt eine Sperre auf, wahlweise **Eingeteilte auf Standard setzen** und **Auftrag danach abgeben**; dann **Jetzt beenden**. Admins im Funker-Modus nimmt es dabei mit. Seit 0.11.5 hebt es vor dem Abgeben die Stummschaltungen auf, die du in **Wer ist da** gesetzt hast (als Admin alle außer deiner eigenen, A8). |

Seit 0.11.2 gibt es auf der Funkfeld-Zuteilung nur noch diese zwei. Weg sind **Wechsel sperren** und **Ganz sperren** (A7.2),
**Wie zuletzt für alle ohne Platz**, **Stumm in allen Netzen** (seit 0.11.5 das Mikrofon in **Wer ist da**, A8) und der eigene Knopf **Auftrag abgeben**; den Auftrag gibst
du mit **Abend beenden ...** und **Auftrag danach abgeben** zurück.

Admins teilen sich selbst ein. Ein Admin-Konto setzt nur ein Admin um, es sei denn, der Admin spielt heute als Funker
(A12.7). Auch dann schaltet ihn weiter nur ein Admin stumm.


### A12.5 Phasen

Eine **Phase** ist ein Netzbild für die ganze Flotte, keine Liste von Personen. Der Admin legt vor dem Abend fest, welche
Netze eine Rolle in der Phase "2 Entern" in ihren Feldern hat (A18.1), und die Einsatzleitung schaltet im Einsatz mit
zwei Tastendrücken (A12.6) von einer Phase zur nächsten; bis 0.11.1 ging das auch mit einem Klick am Brett. **Phase 1**
ist die Grundstellung: die Felder der Rollen, wie sie im Verwaltungsteil stehen. Dazu kann es bis zu vier weitere Phasen
geben (Nummer 2 bis 5).

Was ein Wechsel tut:

- Er ändert nur das Netzbild. Plätze, Rollen, Reservierungen und die Belegung bleiben; niemand wird verdrängt.
- Er stellt nur die um, die einen Platz haben und deren Rolle sich in der alten oder in der neuen Phase ändert. Auch wer
  nur reserviert ist und gerade nicht verbunden, bekommt beim Verbinden gleich die richtigen Felder. Alle anderen fasst er
  nicht an.
- Er lässt stehen, was du selbst gewählt hast: dein Sendenetz, wenn du es selbst gewechselt hast und noch darfst, Zusatznetze,
  die die Einsatzleitung dir gegeben hat, und ein abgeschaltetes Hören bei Netzen, die vorher und nachher da sind (außer das
  neue Feld ist fest).
- Er lässt Admins aus, die der Schaltende nach der Rangregel nicht einteilen darf: Ein Admin wird nur durch einen Admin
  umgestellt, es sei denn, er spielt heute als Funker (A12.7). Die Meldungen nennen die Ausgelassenen, etwa `ausgelassen:
  Nordwind (Admin)`.
- Wer später dazukommt oder einen Platz bekommt, bekommt das Netzbild der laufenden Phase von selbst.
- Zwölf Stunden nach dem letzten Schalten fällt die Flotte von selbst auf die Grundstellung zurück, auch nach einem Neustart
  des Servers, mit denselben Folgen wie ein Wechsel. **Abend beenden** (A12.4) schaltet zuerst auf Phase 1.
- Der Server bremst das Schalten: höchstens eine Schaltung je 2 Sekunden, bei kurzen Stößen bis zu zwei hintereinander.
  Wer mehr schickt, bekommt eine Fehlermeldung, an der Taste `Nicht geschaltet: zu schnell, kurz warten` (bis 0.10.0
  `... zu schnell hintereinander, kurz warten`); getrennt wird deswegen niemand.

Seit 0.11.2 hat die Funkfeld-Zuteilung keine Phasenleiste und keinen Verlauf mehr; geschaltet wird nur noch mit den Tasten
(A12.6), und was geschah, sagt die Meldezeile. Bis 0.11.1 stand im Kopf des Bretts, wenn es Phasen über der
Grundstellung gab, die Leiste **Phase** mit einem Knopf je Phase und einer Rückfrage vor dem Schalten, und der **Verlauf**
nannte jeden Wechsel. Die Zahl, die die Taste beim ersten Druck nennt, ist eine Vorschau aus den Daten, die der Client
hat; die genaue Zahl nennt danach der Server (er zählt nur die, deren Felder sich wirklich ändern). Ziehst du einen
Namen, während eine Phase umstellt, steht unten auf der Funkfeld-Zuteilung `Inzwischen durch Phase 2 Entern umgestellt - bitte
noch einmal ziehen.`

Nicht gemessen ist die Netzlast eines Phasenwechsels bei 40 Leuten (Teil D). Geprüft ist alles andere am Code und in Tests
der Suite (1312 von 1312 am Stand `95aa78b`), dazu in einem Zusammenspiel über den Testzugang gegen den Server. Bekannt
und nicht behoben: Die Komfortablage des Servers (`komfort.json`) behält die letzte Sendeauswahl über Phasenwechsel und
setzt sie nach einem Neustart wieder; prüf nach einem Serverneustart mitten in einer Phase dein Sendenetz.

### A12.6 Phasen mit Tasten und Joystick

Seit 0.11.2 schaltest du die Flotte nur noch so um (bis 0.11.1 auch am Brett), mit zwei Tasten, die du wie in A4.1
belegst: **Einstellungen** (Strg+2), **Tasten**, **Erweitert**, Gruppe **Einsatzleitung**: **Phase vor** und **Phase
zurück**. Das geht an Tastatur, Maus und
Joystick (A4.2). Die Gruppe steht seit 0.10.0 nur da, solange du die Einsatzleitung hast (A12.1); belegte Tasten bleiben
gemerkt, auch wenn sie nicht zu sehen sind. Ohne Einsatzleitung bleibt ein Druck ohne Folge; einmal je Abend, der um
04:00 beginnt, steht dann `Phasen schaltet die Einsatzleitung` in der Meldezeile.

Weil ein Druck im Einsatz leicht danebengeht, schaltet es **in zwei Stufen**:

1. **Erster Druck, Phase scharf.** Die Taste macht die nächste Phase scharf (bei **Phase zurück** die vorige). An den Server
   geht noch nichts. Die Meldezeile (A6.7) nennt Phase, Zahl und Ausgelassene: `Phase 2/3 ENTERN? Nochmal drücken - 9
   Funker, ausgelassen: Nordwind (Admin)`. ENTERN ist der Kurzname der Phase.
2. **Zweiter Druck, schalten.** Derselbe Knopf noch einmal, frühestens 0,3 Sekunden und spätestens 5 Sekunden nach dem
   ersten, schaltet. Ein schnellerer Druck zählt nicht (prellender Knopf, Doppeltipp); die Phase bleibt scharf.
3. Die Gegenrichtung bricht ab: `Nicht geschaltet: abgebrochen`. Nach 5 Sekunden ohne zweiten Druck verfällt die Wahl still.

Danach sagt die Meldezeile, was geschah:

| Meldezeile | Bedeutung |
| --- | --- |
| `Phase 2 Entern: schalte ...` | Die Anfrage ist beim Server. |
| `Phase 2 Entern läuft - 9 umgestellt` | geschaltet; bei Ausgelassenen steht dahinter `, ausgelassen: Nordwind (Admin)` |
| `Keine Antwort - Stand folgt` | Der Server hat nach 3 Sekunden nicht geantwortet; der Stand kommt, sobald die Verbindung ihn bringt. |
| `Nicht geschaltet: Nordwind war schneller, 3 Rückzug läuft` | Ein zweiter Leiter hat vorher geschaltet; dein Druck änderte nichts. |
| `Nicht geschaltet: inzwischen läuft 3 Rückzug` | Die Phase hat inzwischen gewechselt, ohne dass ein Name dabei ist (Konsole oder Rückfall nach zwölf Stunden); bis 0.10.0 `Nicht geschaltet: die Phase hat inzwischen gewechselt, 3 Rückzug läuft`. |
| `Nicht geschaltet: dafür fehlt die Einsatzleitung` | Dein Auftrag ist inzwischen zu Ende. |
| `Nicht geschaltet: keine Verbindung` | Du bist nicht verbunden. |
| `Letzte Phase: 3 Rückzug läuft` oder `Erste Phase: 1 Anflug läuft` | In dieser Richtung gibt es keine weitere Phase. |
| `Keine Phasen angelegt (Flotte > Phasen)` | Es gibt noch keine Phase über der Grundstellung, oder der Server kennt sie nicht (vor 0.5.0); der Admin legt sie in Flotte > Phasen an. Bis 0.10.0 `Keine Phasen - der Admin legt sie in Flotte > Phasen an`. |

Einen Verlauf, der die Wechsel nennt, gibt es seit 0.11.2 nicht mehr (A12.5). Geprüft am Code und in Tests mit
Attrappen; die Phasentasten am HOTAS sind am echten PC noch nicht ausprobiert (1.8).

### A12.7 Heute als Funker

Ein Admin teilt sich selbst ein, und ein Admin-Konto setzt sonst nur ein Admin um. Das stört, wenn an einem Abend jemand
anderes leitet und der Admin einfach mitfunken will, etwa als Gunner auf einem Schiff. Dafür gibt es **Heute als Funker**:
Du schaltest es für dich selbst ein, und die Einsatzleitung teilt dich ein wie jeden anderen.

- **Wer:** nur ein Admin, nur für sich selbst, ohne Admin-Sitzung und ohne Code, nur an einem Server ab 0.5.0. Niemand
  kann einen anderen in diesen Modus setzen oder ihn für ihn beenden.
- **Wie lange:** bis zum nächsten 04:00 Uhr (Europe/Berlin), höchstens 24 Stunden. Es endet früher mit **Wieder Admin**,
  bei einem Wechsel der Stufe und wenn das Konto gesperrt wird. Ein neues Verbinden und ein Neustart des Servers beenden
  es nicht.
- **Was ruht:** Global senden, Durchsagen, das Senden in mehrere Netze zugleich, die Funkfeld-Zuteilung und damit das Einteilen
  und die Phasen. Der Bereich Raid verschwindet ohne Neustart. **Was bleibt:** die Verwaltung mit Admin-Schlüssel und Code, und
  alle Netze, die du hörst.

So schaltest du es ein:

1. **Einstellungen** (Strg+2), Karte **Ich**: Unter den Rollenwünschen steht **Heute als Funker (bis 04:00)** mit dem
   Satz "Die Einsatzleitung kann dich einteilen. Global, Durchsagen und das Brett ruhen bis dahin. Die Verwaltung mit Code
   bleibt dir." Klick auf **Als Funker**. Dieselbe Rückfrage öffnet der Menüeintrag **Heute als Funker** am Symbol im
   Infobereich (A6.6). Bis 0.9.3 stand das als eigene Karte ganz oben unter Einstellungen.
2. Es kommt erst eine Rückfrage mit den Folgen, etwa: `Heute als Funker bis 04:00: Die Einsatzleitung teilt dich ein wie
   jeden anderen. Global, Durchsagen und das Brett ruhen bis dahin.` Dazu, wenn es zutrifft, `Deine Sperre der Selbstwahl
   endet jetzt und kommt nicht zurück.`, immer `Hast du eine Aufstellung angewandt, wird sie nicht mehr nachgezogen.`,
   dann, wer einteilen kann (`Einteilen können dann: Nordwind, Distel.`) oder `Gerade teilt sonst niemand ein: Gib zuerst
   jemandem den Auftrag.`, und zuletzt `Zurück geht es jederzeit mit "Wieder Admin", ohne Code.`
3. **Ja, als Funker** schaltet ein, **Abbrechen** nicht.

**Das siehst du:** In der Karte steht `Läuft: als Funker bis 04:00` (liegt das Ende mehr als 20 Stunden voraus, dann mit
Wochentag und Datum), der Knopf heißt jetzt **Wieder Admin**, und in der Fußzeile des Hauptfensters steht `als Funker bis
04:00`. Die Funkfeld-Zuteilung ist verschwunden (die Sätze der Karte sagen dazu noch "das Brett"). **Wieder Admin** gilt sofort
und ohne Code, auch im Menü des Symbols im Infobereich; die Rechte kommen zurück, die gesperrte Selbstwahl und das
Nachziehen der Aufstellung aber nicht.

Auf der Funkfeld-Zuteilung der anderen nennt der Hilfetext deines Namens `Admin, heute als Funker` (sonst `Admin, teilt sich
selbst ein`; bis 0.11.1 trug deine Karte auf dem Brett statt des Schlosses die Marke **als Funker**), und die Leitung kann
dich ziehen wie jeden anderen. **Abend beenden** nimmt Admins im Funker-Modus mit. Den Knopf **Wie zuletzt für alle ohne
Platz**, der geschützte Admins nannte, gibt es seit 0.11.2 nicht mehr.

Dazu das Häkchen **Beim Verbinden als Funker** darunter. Es schaltet den Modus nach jedem Verbinden ein, einmal je
Verbindung; ab Werk ist es aus, und es ist auf diesem PC gespeichert. Hast du an dem Abend **Wieder Admin** gewählt,
schaltet es nicht noch einmal ein, damit du mitten im Raid nicht beim nächsten Wiederverbinden die Leitung verlierst; erst
nach 04:00 wieder.

An einem Server 0.4.0 fehlt die Karte. Clients 0.4.x sehen den Modus nicht und funken mit einem Server 0.5.0 weiter wie
bisher. Geprüft am Code und in Tests, dazu im Zusammenspiel über den Testzugang gegen den Server (Admin als Funker); das
Menü am Symbol im Infobereich ist am echten PC noch nicht ausprobiert (1.8).

### A12.8 Meldungen an alle

Seit 0.12.0 steht auf der **Funkfeld-Zuteilung** unter dem Baum das Feld **Meldungen**. Damit schickst du Grafiken und
Text an alle, die gerade online sind; sie sehen sie über dem Spiel (A6.9). Eine Auswahl nach Kanal gibt es nicht.

**Wer senden darf.** Nur wer von einem Admin ernannt ist (Haken **darf Meldungen senden** auf der Mitgliederkarte, B) und
gerade Führung, Admin oder Einsatzleitung mit laufendem Auftrag ist; **Heute als Funker** sendet nicht. Bei einem Funker
bleibt der Haken stehen, wirkt aber nicht. Nur dann gibt es das Feld; ohne Auftrag (etwa als Führung) zeigt die Seite nur
das Feld. Entscheidend ist der Server, und das Feld gibt es erst mit einem Server ab 0.12.0.

**Aufbau.** Oben `Meldungen`, an wen (`an alle, die online sind (22)`) und rechts **Größe:** **normal**, **doppelt**,
**dreifach**. Die Größe gilt für Befehle, Ampel, Countdown und die Bereit-Abfrage, Zustände bleiben klein; sie bleibt
stehen, bis du sie änderst. Darunter Kacheln mit den Bildern (in deiner eigenen Ausführung, A6.9) und kurzen Namen; ein
Klick auf eine Kachel sendet. Was gerade läuft, hat einen Rand im Akzent.

| Gruppe | Kacheln und Knöpfe |
| --- | --- |
| **Befehle (8 s)** | Sammeln, Sammelpunkt, Stopp, Rückzug, Start frei, Med. Hilfe, Los, Stopfen |
| **Zustände (bis aufgehoben)** | Einsatz n. gest., Event n. gest., Pos. halten, Funkdisziplin, Pause, dazu **aufheben** |
| **Ampel** | rot, gelb, grün, dazu **aus**; nur von Hand, eine Startfolge gibt es nicht |
| **Countdown** | die Form **Start um** oder **Restzeit** (die Kachel wählt, sie sendet nicht), das Feld für die Zeit, **Start**, **abbrechen** |
| **Bereit-Abfrage** | **Bereit melden** startet sie; daneben `18 von 22 bereit`, `es fehlen: ...` und **schließen** |
| **Text** (seit 0.12.0, M24) | das Feld für den Text (bis 120 Zeichen, mit `noch 120 Zeichen`), sechs Farbknöpfe (Weiß, Gelb, Orange, Rot, Grün, Blau), **8 s** oder **bleibt stehen**, **Senden**, **aufheben** |

**Countdown.** Ins Feld kommt eine Uhrzeit wie `21:30` (die nächste, die kommt, auch nach Mitternacht) oder eine Dauer wie
`5 min`, `90 s` oder `1 h 30 min`; eine Zahl allein sind Minuten, höchstens 24 Stunden. Darunter steht, was es ergibt,
etwa `Start um 21:30, in 5 min`. Die Funker sehen die Zeit in ihrer Ortszeit, gerechnet mit der Uhr des Servers.

**Bereit-Abfrage.** Alle, die online sind, sehen groß **Bereit melden** und melden mit ihrer Taste **Bereit** oder dem
Knopf in der Titelleiste (A4.6). Du siehst `n von m bereit`, wobei m alle zählt, die online sind, außer dir (wer fragt,
zählt nicht mit), und darunter, wer noch fehlt. Sie endet mit **schließen** oder nach 2 Minuten von selbst; das Ergebnis
bleibt als `zu: 18 von 22 bereit` stehen. Eine neue Abfrage ersetzt die alte.

**Zeile.** Unter **Zeile dazu (freiwillig, bis 60 Zeichen)** schreibst du, was unter der Grafik stehen soll, etwa `Hangar 2`.
Mehr als 60 Zeichen nimmt das Feld nicht an. Die Zeile geht mit jeder Meldung hinaus, die eine Grafik zeigt, und bleibt stehen,
bis du sie änderst; **aufheben**, **aus**, **abbrechen**, **schließen** und der Text tragen keine.

**Text.** Neben der Zeile (oder darunter, wenn das Fenster schmal ist) steht die Gruppe **Text**: Damit schickst du nur Text,
ohne Grafik, an alle, die online sind. Ins Feld kommen bis 120 Zeichen; mehr nimmt es nicht an, daneben steht, wie viele noch
gehen. Die Farbe wählst du mit einem der sechs Knöpfe (ab Werk Weiß), die Größe mit **Größe:** oben (normal, doppelt,
dreifach), die Dauer mit **8 s** (ab Werk) oder **bleibt stehen**. **Senden** schickt den Text; die Funker hören dazu den
kurzen Ton wie bei einem Befehl und sehen ihn unter dem Befehlsplatz (A6.9). Ein neuer Text ersetzt den laufenden, gleich
von wem; **aufheben** nimmt ihn bei allen weg. Ein stehender Text erreicht auch, wer später einsteigt. Text, Farbe und Dauer
bleiben hier stehen, bis du sie änderst. Wer senden darf und die 2 Sekunden zwischen zwei Meldungen gelten wie für alle.

**Höchstens eine Meldung in 2 Sekunden.** Nach jeder Meldung sind die Kacheln 2 Sekunden lang gesperrt, gezählt ab der
Antwort des Servers; darunter steht `Nächste Meldung in 1,4 s`. Scheitert eine Meldung, steht der Grund darunter, etwa
`Die Zielzeit ist schon vorbei.`


## A13 Den Server betreiben: was er tut

Ab hier geht es um den Server. Du brauchst Grundkenntnisse: eine Linux-Kommandozeile öffnen, Befehle abtippen,
eine Datei mit `nano` bearbeiten, dem einfachen Texteditor der Kommandozeile (Strg+O und Eingabetaste speichern, Strg+X
schließt). Den Rest erklärt dieser Teil. Jede Einstellung, jeden Befehl und jede Meldung im
Einzelnen beschreibt Teil B; warum der Server so gebaut ist, Teil C.

### A13.1 Aufgaben des Servers

- **Ein Dienst.** `funk-server` läuft als systemd-Dienst unter einem eigenen Benutzer `funk`. systemd ist der Teil von
  Linux, der Dienste startet, überwacht und nach einem Neustart wieder hochfährt.
- **Zwei Ports nach außen, sonst nichts.** Ein Port ist eine Nummer, unter der ein Dienst auf einem Rechner erreichbar ist.
  Der Funk braucht genau einen TCP-Port für den Steuerkanal (TLS) und einen UDP-Port für die Sprache. Keine Webseite,
  kein HTTP, kein Admin-Port.
- **Verwaltung nur am Rechner selbst oder im Client.** `funk-admin` redet mit dem laufenden Dienst über eine Datei (einen
  UNIX-Socket), nicht über das Netz. Wer an der Konsole verwalten will, ist auf dem Server angemeldet, als root oder mit
  sudo. root ist das Konto mit allen Rechten auf einem Linux-Rechner; `sudo` vor einem Befehl führt nur diesen einen
  Befehl mit diesen Rechten aus. Im Client verwaltet ein Admin nur in einer Admin-Sitzung.
- **Er merkt sich:** Konten, Geräte, Anträge, Netze, Einheiten, Rollen, Phasen, Plätze, Aufträge, Pegel und die Admin-Nachweise,
  in `/var/lib/funk`. Das alles übersteht einen Neustart. Die laufenden Verbindungen enden dabei; die Clients bekommen
  vorher Bescheid und kommen nach etwa 10 Sekunden von selbst wieder.
- **Er leitet weiter, ohne zu mischen.** Jedes Sprachpaket wird geprüft, gestempelt und an alle Hörer weitergereicht.
- **Er schützt sich selbst.** Die eingebaute **Sperrliste** sperrt Adressen, die sich wie Scanner, Botnetze oder
  Durchprobierer benehmen, befristet: erst 5 Minuten, bei Wiederholung länger, höchstens 24 Stunden. Dafür ist nichts
  einzurichten. Adressen, von denen in den letzten 14 Tagen ein Mitglied verbunden war, sperrt sie nicht (B16, C14).
- **Er schreibt ein Logbuch** ins Journal von systemd. IP-Adressen und Konten stehen darin nur als gekürzter Prüfwert,
  nie im Klartext.

### A13.2 Wo was liegt

Nach dem Einrichten mit dem Skript:

| Ort | Inhalt |
| --- | --- |
| `/opt/funk` | die Programme, Besitzer root |
| `/etc/funk/server.json` | die Einstellungen |
| `/etc/funk/profil.json` | das Serverprofil vom Eigentümer des Programms |
| `/etc/funk/credentials` | die Schlüssel: Serverschlüssel und Zertifikat, Betriebsgeheimnis, TOTP-Schlüssel. Nur für root lesbar. |
| `/var/lib/funk` | der Zustand: Konten, Geräte, Netze, Plätze, Admins, Mitglieder, Journal |
| `/usr/local/sbin/funk-admin` | ein Kurzbefehl: `sudo funk-admin konten` statt des langen Aufrufs |

Was in `/etc/funk/credentials` liegt, kurz erklärt:

- Der **Serverschlüssel** weist den Server aus. Das **Zertifikat** ist die Datei mit dem öffentlichen Teil dieses
  Schlüssels, die der Server beim Verbinden vorzeigt; hier stellt er es sich selbst aus, die Clients prüfen es über den Pin
  im Profil (A16.1).
- Das **Betriebsgeheimnis** ist eine Zufallszahl. Aus ihr leitet der Server die Schlüssel für die gekürzten Prüfwerte im
  Logbuch, für die Sperrliste und für die Liste der Mitgliederadressen ab, die nie gesperrt werden.
- Der **TOTP-Schlüssel** verschlüsselt auf dem Server die Geheimnisse der Admins, aus denen ihre App die Codes für den
  Zweitfaktor rechnet. TOTP ist das Verfahren dieser Codes: sechs Ziffern, die alle 30 Sekunden wechseln (Kapitel A17).

Die Dateien in `/etc/funk/credentials` sind Geheimnisse. Sie gehören nie in einen Chat, nie in eine E-Mail und nie in ein
Repository. Alle Dateien mit Besitzer und Rechten listet B1.4, die Schlüssel im Einzelnen B1.6.

### A13.3 Genehmigung und Datenschutz

JG TacRelay sendet nicht über Funkwellen, sondern über das Internet; eine Frequenz oder Funklizenz braucht es nicht. Für
den Betrieb des Servers brauchst du nach bisheriger Einschätzung auch sonst keine Genehmigung und keine Meldung bei einer
Behörde: Das Telekommunikationsgesetz verlangt eine Meldung nur von dem, der ein öffentliches Netz betreibt oder einen
öffentlich zugänglichen Dienst anbietet, und ein Funk, in den nur einzeln freigegebene Mitglieder der Gilde kommen, ist
das nach Einschätzung nicht (F11.9). Die Datenschutz-Grundverordnung (DSGVO) beachtest du trotzdem, denn der Server hält
Namen, Geräte und ein Verwaltungsprotokoll mit Klarnamen; ob die Ausnahme für rein Persönliches greift, ist zweifelhaft,
und sicher ist, so zu handeln, als gelte die DSGVO (F11). Das ist kein Rechtsrat; verbindlich klärt es ein Fachanwalt
für IT-Recht.


## A14 Was du brauchst

| Was | Genau | Hinweis |
| --- | --- | --- |
| Rechner | ein gemieteter Server (Hetzner, OVH oder anderswo) oder daheim ein Container auf Proxmox | Beides geht. Daheim sehen alle Mitglieder deine Heimadresse, und gegen einen Angriff, der den Anschluss mit Daten überflutet, hilft nur ein Umzug. Für den Regelbetrieb ist ein gemieteter Server gedacht. |
| System | Debian 13 (Empfehlung für einen neuen Server), Debian 12 oder Ubuntu 24.04; x86_64 (normale 64-Bit-PC-Technik); mit systemd | Debian 13 ist die aktuelle stabile Fassung; mit Debian 12 und Ubuntu 24.04 geht es ebenso. Das Skript prüft, ob es als root läuft, ob der Rechner Debian oder Ubuntu ist, x86_64 hat und mit systemd läuft, und sagt, was fehlt; die Nummer der Fassung prüft es nicht. Auf einem anderen System richtet es nur ein, wenn libicu und die .NET-Laufzeit 10 schon da sind; was das ist, steht in A15.3. |
| Rechte | root oder ein Benutzer mit sudo | |
| Größe | im Beispiel für Proxmox 2 Kerne, 1 GB Arbeitsspeicher, 8 GB Platte | Im Lasttest mit 40 Funkern brauchte der Server bei 5 und bei 12 gleichzeitigen Sprechern im Mittel 8 bis 21 % eines Kerns, wenn alle 40 zugleich zu allen sprechen 74 % (D12.1). Gemessen auf einem Ryzen 9 5950X; auf einem kleinen gemieteten Server ist der Anteil eine Schätzung. Leitung, Rechenzeit und Speicher für 20 und 40 Leute rechnet Teil D vor. |
| Name im DNS | etwa `funk.example.org`, mit einem A-Eintrag auf die öffentliche IPv4-Adresse des Servers | DNS ist das Telefonbuch des Internets. Ein A-Eintrag ordnet einem Namen eine IPv4-Adresse zu, die übliche Internetadresse aus vier Zahlen wie `203.0.113.10`. Die TTL (wie lange andere die Adresse zwischenspeichern) sollte kurz sein: höchstens 300 Sekunden, bei einer wechselnden Heimadresse besser 60. |
| Zwei freie Ports | TCP 8443 (auf einem gemieteten Server ginge auch 443, aber für einen Umzug gilt 8443, Abschnitt A22) und UDP 47443 | UDP nicht auf 443: Manche Netze behandeln UDP 443 besonders und sperren es. |
| Das Serverpaket | `<zeit>_JG-TacRelay-Server-<fassung>.zip` und die Datei mit der Endung `.sha256` daneben | vom Eigentümer des Programms |
| Telefon | einmal zwei Minuten mit dem Eigentümer des Programms | für den Vergleich der Kennung (Abschnitt A16) |
| Für den ersten Admin | ein Windows-PC mit TPM 2.0, das Admin-Setup, ein Telefon mit einer Authenticator-App | Abschnitt A17 |

### A14.1 Ein gemieteter Server

- Nimm ein Abbild mit Debian 13 oder Ubuntu 24.04 und melde dich als root oder mit sudo an.
- Hat dein Anbieter eine eigene Firewall vor dem Server (bei Hetzner etwa die Cloud Firewall), gibst du dort dieselben zwei
  Ports frei: TCP 8443 und UDP 47443. Das Skript sagt dir das am Ende noch einmal.
- Ein Router mit Portfreigabe sitzt nicht davor; auf die Frage danach antwortest du `n`.
- Laut einer Recherche vom 29.09.2026 galt bei Hetzner seit Juni 2026 eine Kapazitätssperre für Neukunden; netcup und OVH
  waren bestellbar. Das kann sich geändert haben, also vorher nachsehen.

### A14.2 Daheim im Proxmox-Container

Ein Container (LXC) ist ein abgeschotteter Linux-Rechner im Rechner, leichter als eine ganze virtuelle Maschine. Er muss
**unprivilegiert** sein und **nesting=1** haben. Ohne nesting startet der Dienst nicht, weil seine Schutzregeln eigene
Bereiche im System anlegen; unter Debian 13 fallen dann laut Proxmox-Forum sogar Dienste des Systems aus (hier nicht
gemessen).

Angelegt wird er auf dem Proxmox-Host, Schritt für Schritt in `HEIMSERVER-PROXMOX.md`, Abschnitt 2. Das Beispiel dort,
mit Container-Nummer 150, fester Adresse `192.168.1.250` und dem Router auf `192.168.1.1`:

```
pveam update
pveam available --section system | grep debian
pveam download local <vorlage aus der liste>
pct create 150 local:vztmpl/<vorlage aus der liste> \
  --hostname funk --unprivileged 1 --features nesting=1 \
  --cores 2 --memory 1024 --swap 512 --rootfs local-lvm:8 \
  --net0 name=eth0,bridge=vmbr0,ip=192.168.1.250/24,gw=192.168.1.1,firewall=1 \
  --nameserver 192.168.1.1 --onboot 1
pct start 150
pct enter 150
```

Nummer, Adressen und Speicher (`local-lvm`) ersetzt du durch deine. Die feste Adresse liegt außerhalb des Bereichs, den
dein Router selbst vergibt.

Daheim brauchst du außerdem:

- **Portfreigaben im Router**, genau zwei: TCP 8443 und UDP 47443 auf die Adresse des Containers. Der Router leitet dann,
  was von außen auf diesen Ports ankommt, an den Container weiter. Bei einer FRITZ!Box unter Internet, Freigaben,
  Portfreigaben.
- **DynDNS**: Dein Anschluss hat eine wechselnde Adresse. Ein DynDNS-Dienst trägt die neue Adresse nach jedem Wechsel
  unter deinem Namen ein. Nur ein A-Eintrag, kein AAAA-Eintrag für IPv6. IPv4 und IPv6 sind die zwei Arten von
  Internetadressen: die ältere mit vier Zahlen wie `203.0.113.10` und die neuere, viel längere. Ein AAAA-Eintrag ordnet
  einem Namen eine IPv6-Adresse zu; die FRITZ!Box würde dort ihre eigene melden, nicht die des Containers (B3.5). Nach
  einem Wechsel der Adresse verbinden die Clients selbst neu.
- die Proxmox-Firewall mit genau den zwei Ports nach innen (`HEIMSERVER-PROXMOX.md`, Abschnitt 7). Das Skript zeigt dir die
  zwei Regeln am Ende.

Die Härtung des übrigen Rechners (FRITZ!Box, Proxmox-Weboberfläche, SSH nur mit Schlüssel, automatische
Sicherheitsupdates) steht als Checkliste in `HEIMSERVER-HAERTUNG.md`. SSH ist die verschlüsselte Anmeldung an einem
entfernten Rechner über die Kommandozeile (A15.1).


## A15 Einrichten mit einem Befehl

Das Skript `funk-einrichten.sh` richtet den ganzen Server ein. Beim ersten Mal stellt es sechs Fragen (hinter einem
Router zwei mehr), danach lässt es sich beliebig oft wiederholen und ändert nur, was fehlt. Fremde Dienste, Units und
Dateien fasst es nicht an.

**Stand 0.5.0, wie schon in 0.4.0: Das Skript ist auf echtem Linux noch nicht gelaufen.** Es ist auf Windows gebaut und so
weit geprüft, wie es ohne Linux geht; gegenüber 0.4.0 haben sich nur Texte geändert (am Code geprüft). Bevor es auf
einen Server mit anderen Diensten kommt, läuft es einmal in einem Wegwerf-Container (`EINRICHTEN.md`, letzter
Abschnitt).

Bist du schon root (etwa im Container nach `pct enter`), lässt du `sudo` in allen Befehlen weg.

### A15.1 Paket auf den Server bringen

Von deinem Windows-PC aus, in PowerShell, im Ordner mit dem ZIP. `scp` kopiert eine Datei über SSH, die verschlüsselte
Anmeldung an einem entfernten Rechner, und ist in Windows 10 und 11 dabei:

```
scp <zeit>_JG-TacRelay-Server-<fassung>.zip <benutzer>@<server>:
```

Daheim mit Proxmox schiebst du es auf den Host und von dort mit `pct push <nummer> <datei> <ziel>` in den Container, wie in
`HEIMSERVER-PROXMOX.md`, Abschnitt 5.

### A15.2 Paket prüfen und auspacken

Auf dem Server, im Ordner, in dem das ZIP liegt. `apt` ist die Paketverwaltung von Debian und Ubuntu; hier holt sie das
Programm `unzip` zum Auspacken:

```
sha256sum <zeit>_JG-TacRelay-Server-<fassung>.zip
sudo apt install -y unzip
unzip <zeit>_JG-TacRelay-Server-<fassung>.zip
cd <zeit>_JG-TacRelay-Server-<fassung>/linux
```

**Das siehst du:** `sha256sum` zeigt dieselbe lange Zahl wie die Datei `.sha256` vom Eigentümer. Weicht sie ab, ist das
Paket beschädigt oder verändert: nicht weitermachen, neues Paket holen. Im Ordner `linux` liegen `funk-einrichten.sh`, die
Programme (`funk-server.dll`, `funk-admin.dll` und weitere) und der Ordner `konfig`.

Die Datei `.sha256` ist eine Textzeile: vorn die Prüfsumme, dahinter der Name des ZIP. Unter Windows öffnest du sie mit
dem Editor; dort kannst du die Zahl auch selbst nachrechnen, in PowerShell im Ordner mit dem ZIP:

```
Get-FileHash <zeit>_JG-TacRelay-Server-<fassung>.zip -Algorithm SHA256
```

`Get-FileHash` schreibt die Zahl in Großbuchstaben, `sha256sum` und die Datei `.sha256` in Kleinbuchstaben; gemeint ist
dieselbe Zahl.

### A15.3 Erster Lauf

```
sudo bash funk-einrichten.sh
```

Zuerst prüft das Skript root, systemd, x86_64 und das System. Dann bringt es die .NET-Laufzeit 10 und libicu auf den
Server: die Laufzeitumgebung der Programme und eine Bibliothek, die der Server zum Vergleichen von Namen braucht. Gibt es
die Laufzeit in deinen Paketquellen nicht, fragt es:

- `Die .NET-Laufzeit 10 fehlt in den Paketquellen. Paketquelle von Microsoft eintragen (...)? (j/n) [j]`
- falls das nicht geht: `Laufzeit mit dem Installationsskript von Microsoft nach /usr/share/dotnet legen (...)? (j/n) [j]`

Mit der Paketquelle kommen spätere Updates der Laufzeit mit `apt`. Danach legt es die Programme nach `/opt/funk` und
fragt. Die Eingabetaste nimmt jeweils den Vorschlag in eckigen Klammern:

| Frage | Vorschlag | Was du antwortest |
| --- | --- | --- |
| Öffentlicher Name, unter dem die Clients den Server finden | aus `hostname -f` | der Name im DNS, etwa `funk.example.org` |
| Adresse dieses Rechners, an die der Server bindet | die Adresse, über die der Rechner ins Internet geht | meist Eingabetaste |
| Sitzt ein Router mit Portfreigabe davor (FRITZ!Box, Heimnetz)? | `j` bei einer Adresse wie 192.168... oder 10... | gemieteter Server: `n`; daheim: `j` |
| TCP-Port am Server | 443, wenn frei und kein Router, sonst 8443 | `8443` (gilt auch für einen späteren Umzug) |
| TCP-Port außen am Router (nur mit Router) | wie innen | Eingabetaste, wenn der Router denselben Port weiterleitet |
| UDP-Port am Server | 47443 | Eingabetaste |
| UDP-Port außen am Router (nur mit Router) | wie innen | Eingabetaste |
| Echo-Funker einrichten (spricht im Netz Echo nach, zum Prüfen)? | `j` | `j` |

Nach dem Namen sagt das Skript, wohin der Name im DNS zeigt, oder dass er dort noch fehlt. Ist ein Port belegt, sagt es
das, mit dem Programm, das ihn hält, und fragt noch einmal. Den fremden Dienst fasst es nicht an.

**Das siehst du:** je Schritt eine Zeile, die mit einem dieser Wörter beginnt:

| Wort | Bedeutung |
| --- | --- |
| `OK` | war schon richtig |
| `NEU` | eben angelegt |
| `BLEIBT` | etwas Vorhandenes bleibt, wie es ist |
| `FEHLT` | etwas fehlt; dahinter steht `Abhilfe:` mit dem, was zu tun ist |

Etwa `NEU    Benutzer funk` oder `NEU    Serverschlüssel und Zertifikat in /etc/funk/credentials, Pin XXXX-XXXX-XXXX-XXXX`.
Dann ein Block **Firewall und Router**. Das Skript ändert dort nichts von selbst, es zeigt nur, was nötig ist:

- hinter einem Router die zwei Portfreigaben, mit Adresse und Ports,
- im Proxmox-Container die zwei Regeln für die Proxmox-Firewall,
- mit aktivem ufw (einer einfachen Firewall unter Linux) die zwei Regeln; eingetragen werden sie nur, wenn du ausdrücklich
  `j` oder `ja` sagst,
- mit nftables, dem Paketfilter, der in Linux selbst steckt und ohne ufw von Hand mit Regeln bedient wird, die Regeln zum
  Selbsteintragen,
- ohne Firewall: "Filtert der Anbieter davor, dort TCP ... und UDP ... freigeben."

Am Ende:

```
WARTET AUF PROFIL
Der Server startet erst mit einem Profil vom Eigentümer, in dem sein Pin und sein Name stehen. Schick dem Eigentümer diese Zeile,
egal auf welchem Weg; sie ist nicht geheim und liegt auch in /etc/funk/profilanfrage.txt:

    TR-PROFILANFRAGE v1 funk.example.org 8443 <pin>

Lies ihm am Telefon die Kennung vor: XXXX-XXXX-XXXX-XXXX, dazu Name und Port: funk.example.org, 8443
Er vergleicht alle drei in profil-erneuern; so weiß er, dass die Zeile von dir kommt und unterwegs nicht verändert wurde.
Danach, mit seinem profil.json in diesem Ordner: sudo bash funk-einrichten.sh --profil profil.json
```

Das ist richtig so. Der Server startet erst, wenn das Profil da ist. "Der Eigentümer" ist hier der Eigentümer des
Profilschlüssels, der die Serverprofile unterschreibt (bei einer eigenen Gruppe ihr Verwalter, B0.4); bis Fassung 0.4.1 stand dort ein Rufname. Alle Fragen und Meldungen des
Skripts erklärt B5.


## A16 Das Profil vom Eigentümer

### A16.1 Was das Profil ist

Das **Serverprofil** sagt jedem Client: So heißt der Server, unter diesem Port ist er erreichbar, und diese Schlüssel darf
er haben. Die Schlüssel stehen darin als **Pins**, das sind Fingerabdrücke: kurze Prüfwerte, die genau zu einem Schlüssel
passen. Dein Client baut eine Verbindung nur auf, wenn der Server einen Schlüssel zeigt, dessen Fingerabdruck im Profil
steht. Ein falscher Server, der sich als eurer ausgibt, fällt so sofort auf.

Damit niemand ein falsches Profil unterschieben kann, ist es **unterschrieben**. Unterschreiben kann nur der Eigentümer
des Programms, Sarevoc, mit einem Schlüssel, der nur auf seinem Stick liegt und nie auf einem Server. Den passenden
öffentlichen Schlüssel (den **Profilschlüssel**) kennt jeder Client schon aus dem Setup, und das Serverpaket bringt ihn in
`konfig/profilschluessel.txt` mit. So prüfen Client und Skript jede Unterschrift selbst.

Ein Profil gilt ab Werk 365 Tage. Läuft es ab, verbinden die Clients weiter, warnen aber; der Eigentümer verlängert es dann.
Aufbau und Werkzeuge des Profils beschreibt B8, den Schutz dahinter C7.

### A16.2 Ablauf

1. **Die Zeile schicken.** Schick dem Eigentümer die Zeile, die mit `TR-PROFILANFRAGE` beginnt, auf beliebigem Weg,
   Discord geht. Sie ist nicht geheim.
2. **Anrufen.** Lies ihm am Telefon die Kennung vor, dazu den Namen und den Port. Er vergleicht alle drei mit dem, was
   sein Werkzeug zeigt. So weiß er, dass die Zeile wirklich von dir kommt und unterwegs niemand Name oder Port verändert
   hat. Die Kennung allein deckt nur den Schlüssel deines Servers, deshalb gehören Name und Port dazu.
3. **Profil einspielen.** Er schickt dir `profil.json`. Leg die Datei in den Ordner `linux` und ruf auf:

```
sudo bash funk-einrichten.sh --profil profil.json
```

Das Skript kopiert nur die Programme nach `/opt/funk`; was du daneben in den Ordner legst, bleibt dort.

**Das siehst du:** keine Frage mehr, dafür etwa

```
NEU    Serverprofil Version 2 nach /etc/funk/profil.json (unterschrieben vom Eigentümer, XXXX-XXXX-XXXX-XXXX; Pin, Name und Port passen)
NEU    funk-server eingeschaltet und gestartet (systemctl enable --now)
OK     TCP 8443 und UDP 47443 hören (ss)

==== Funk-Server 0.5.0 bereit ====
Name:        ...
Ports:       ...
Pin:         XXXX-XXXX-XXXX-XXXX
Stand:       ...
Eigentümer:  noch keiner
Schutz:      eingebaute Sperrliste gegen Scanner und Fluten (...), ansehen mit sudo funk-admin sperrliste
Weiter:      JG TacRelay installieren, verbinden und auf die Freigabe warten (Antrag); dann hier: sudo funk-admin erster-admin
```

Lehnt das Skript das Profil ab, sagt es warum, und eingespielt wird nichts:

| Meldung beginnt mit | Bedeutung |
| --- | --- |
| `Das Profil ist nicht mit dem Profilschlüssel des Eigentümers unterschrieben` | nicht vom Eigentümer; kein Client würde es annehmen |
| `Der Pin dieses Servers (Kennung ...) steht nicht im Profil` | das Profil ist für einen anderen Schlüssel geschrieben |
| `Das Profil nennt TCP-Port ...` | der Port im Profil passt nicht zu deinen Einstellungen |
| `Der Name ... steht nicht im Profil` | der Name passt nicht |
| `Das Profil hat Version ..., eingespielt ist schon Version ...` | ein älteres Profil; die Clients nehmen nur höhere Versionen an |

Ein späteres, neueres Profil spielst du genauso ein; das Skript lädt es dann im laufenden Dienst neu.


## A17 Der erste Admin

Den ersten Admin braucht nur ein frischer Server. Bei einem Umzug kommen Konten, Admins und Eigentümer mit.

1. **Client installieren.** Auf deinem Admin-PC (Voraussetzungen in A1.4) das Admin-Setup installieren, das dir der
   Eigentümer des Programms gibt; es kennt das Profil deines Servers. Namen eintragen, verbinden. Der Client wartet auf die
   Freigabe und zeigt die Gerätekennung groß (A2.2).
2. **Auf dem Server:**

```
sudo funk-admin erster-admin
```

3. Es zeigt die offenen Anträge und fragt `Kennung von der Startseite deines Clients (XXXX-XXXX-XXXX-XXXX):` (bis 0.9.3
   nannte es noch den Bereich Verbindung). Tipp die Kennung ab, die dein Client auf Start zeigt. Dann gibt es dein Gerät frei, macht dein Konto zum Admin und zum Eigentümer des
   Servers und sieht es als Admin vor.

**Das siehst du:**

```
OK: Konto <id> freigegeben.
OK: Konto <id> Stufe ADMIN.
OK: Konto <id> Eigentümer.
OK: Konto <id> als Admin vorgesehen.
```

4. Danach fragt es `Kennung des Admin-Schlüssels (leer: später mit admin-ernennen):`. Lass das Fenster so stehen und geh
   an den Client: Links erscheint **Verwaltung** (Strg+9), ohne Neustart. Dort **Admin-Schlüssel anlegen und beantragen**.
   Der Client legt den Admin-Schlüssel im TPM an und zeigt dessen Kennung unter **Admin-Kennung zum Vorlesen am Telefon**.
   Tipp sie auf dem Server ein. Kommt `Noch kein Antrag vom Client da`, war der Klick im Client noch nicht durch; noch
   einmal eingeben.
5. **Zweitfaktor einrichten.** Im Client wechselt die Verwaltung zu **Zweitfaktor mit einer Authenticator-App**. Nach dem
   Ernennen hast du 10 Minuten, um das Geheimnis abzuholen, und danach 10 Minuten für den ersten Code.
   - **Geheimnis abholen (genau einmal)**. Es erscheinen ein QR-Code und darunter der Schlüssel in Vierergruppen.
   - In der Authenticator-App auf dem **Telefon** einen neuen Eintrag anlegen und den QR-Code scannen. Nicht in der
     Authenticator-App auf demselben PC: Lägen Admin-Schlüssel und Code auf demselben Rechner, fängt ein Schadprogramm dort
     beides. Kein Bildschirmfoto: Wer den QR-Code hat, hat das Geheimnis.
   - Den Code aus der App unter **Erster Code aus der App** eintippen, **Einrichtung abschließen**.
   - Es erscheinen **Deine zehn Notfallcodes**, nur dieses eine Mal. Getrennt vom Telefon aufbewahren, etwa ausgedruckt an
     einem sicheren Ort oder als sichere Notiz in einem Passwortmanager, nie in einem Chat. Jeder gilt einmal, wenn das Telefon fehlt.
     Dann **Aufgeschrieben**.
   - Warten, bis die App einen neuen Code zeigt (derselbe gilt nicht zweimal), eintippen, **Verwaltung öffnen**.

**Das siehst du:** Der Client zeigt `Admin-Sitzung offen bis <uhrzeit> Uhr (oder bis du die Verwaltung schließt oder die Verbindung trennst).` und die Bereiche **Anträge**, **Mitglieder** und **Rollenkatalog** (bis 0.11.7 auch **Untergilden**). Auf dem Server zeigt
`sudo funk-admin konten` dein Konto mit `ADMIN` und `EIGENTÜMER`, `sudo funk-admin admins` deinen Admin-Nachweis mit
`AKTIV` und `Notfallcodes 10`.

Zu langsam gewesen? Mit `sudo funk-admin admin-einrichtung-neu --konto <id>` gibt es ein neues Geheimnis; dann im Client
wieder **Geheimnis abholen**. Eine Admin-Sitzung endet nach 12 Stunden und bei
jedem Neustart des Servers; neu öffnen geht mit dem nächsten Code aus der App.

Falsche Codes: Nach 5 falschen in 15 Minuten pausiert die Anmeldung 15 Minuten, nach 10 in 24 Stunden ist der Zweitfaktor
gesperrt, bis du ihn an der Konsole entsperrst (`sudo funk-admin admin-entsperren --konto <id>`). Meldet der Client `Der
Code stimmt nicht oder war schon benutzt. Nimm den nächsten, den die App zeigt, und prüf die Uhr des Telefons.`, dann
genau das: den nächsten Code nehmen und nachsehen, ob die Uhrzeit des Telefons auf automatisch steht.

Weitere Admins, Sperren und Verlust beschreibt B10, warum der Admin-Nachweis so gebaut ist, C11.


## A18 Nach dem Einrichten

### A18.1 Netze und Flotte

Das Netz **Global** legt der Server selbst an. Alles andere ist die **Flotte**: Einheiten, Rollen, Plätze und Netze. Sie
pflegt ein Admin im Verwaltungsteil unter **Flotte**. Als Start gibt es die Vorlage **Star Citizen**: zehn Kanäle mit Ch.
(Kommando, Flotte, Brücke und Crew mit Schiff 1 bis 12, Jäger mit Staffel 1 bis 4, Bodentrupp mit Trupp 1 bis 4, Logistik,
Bergung und Bergbau, Sanität, Allgemein) und 14 Rollen, etwa Kapitän, Pilot, Staffelführer oder Funker (Leitstand). Sie legt
sich neben die vorhandene Flotte, nur solange es außer Global noch keinen Kanal mit Ch. gibt:

```
sudo funk-admin flotte-vorlage --name starcitizen
```

Danach nennt sie sechs Annahmen, die du vor dem ersten Abend prüfst; in der Vorlage im Verwaltungsteil (**Werkzeuge**,
**Vorlagen und Sicherung**) sind sie gelb markiert. Ab dann kommen nur noch Clients ab 0.6.0 herein (Versionstor).

Ein neues Netz steht seit 0.5.0 ohne weitere Angabe auf Sprachaktivierung, im Verwaltungsteil wie an der Konsole. Global
steht immer auf Taste; die Kanäle der Vorlage Star Citizen stehen alle auf Sprachaktivierung, und schon angelegte Netze
behalten ihren Wert (A5.2).

**Phasen** legst du im Verwaltungsteil unter **Flotte** im Reiter **Phasen** an. Den Reiter gibt es nur an einem Server ab
0.5.0. Eine Phase ist ein Netzbild: Welche Felder hat eine Rolle in "2 Entern", wenn die Einsatzleitung sie schaltet
(A12.5)? Die Grundstellung, Phase 1, sind die Felder, die du im Reiter **Rollen** festgelegt hast; dort steht nur ihr Name.

1. **Neue Phase** (höchstens vier über der Grundstellung). Trag einen **Namen** ein (bis 24 Zeichen, etwa Entern) und einen
   **Kurznamen** (bis 12 Zeichen, etwa ENTERN); der Kurzname steht in der Meldezeile (bis 0.11.1 auch in der
   Phasenleiste am Brett).
2. Unter **Rollen in dieser Phase** steht für jede Rolle entweder, dass sie wie in der Grundstellung bleibt, oder eine
   eigene Variante. **Variante anlegen** nimmt die Felder der Grundstellung als Ausgangspunkt, die du in einem Editor mit
   zwei Reihen zu fünf Feldern und einer Vorschau änderst; **wie Grundstellung** nimmt die Variante wieder weg. Eine Variante
   ohne belegtes Feld gibt es nicht: Sie fällt weg, und es gilt die Grundstellung.
3. Unten steht **Wen sie trifft**: die Rollen mit eigener Variante, etwa `Kapitän (alle Schiffe)`.
4. **Übernehmen** schickt, wie bei der übrigen Flotte, alles zusammen an den Server und braucht einen frischen Code aus
   der Authenticator-App. Läuft die Phase, die du änderst, gerade, fragt der Verwaltungsteil nach, und der Server stellt beim
   Übernehmen sofort um. **Phase löschen** löscht eine Phase; läuft sie, fällt die Flotte auf die Grundstellung zurück. Die
   Grundstellung selbst löscht niemand.

Jede Einzelheit des Reiters steht in B12 und B14. Am Code geprüft und in Tests; an der Konsole gibt es für Phasen keinen
Befehl (1.8).

### A18.2 Echo-Funker

Hast du beim ersten Lauf `j` gesagt, zeigt der Abschluss die Befehle. Erst das Netz Echo anlegen, dann den Echo-Funker
einmal von Hand starten (`<adresse>` ist die Bindadresse aus der Einrichtung):

```
sudo funk-admin netz-setzen --id 90 --name Echo --offen HOEREN_SENDEN --va
sudo runuser -u funk-echo -- /usr/bin/dotnet /opt/funk/funk-echo.dll --profil /etc/funk/profil.json --identitaet /var/lib/funk-echo/geraet.pem --adresse <adresse>
```

Er zeigt einen Registrierungscode und die zwei Befehle zum Freigeben. In einer zweiten Sitzung auf dem Server, mit dem
Code aus der ersten:

```
sudo funk-admin verknuepfen --code <code> --discord 100000000000000001 --benutzername echo-funker --name Echo
sudo funk-admin freigeben <anzeigeform>
```

Die Zahl hinter `--discord` ist hier nur eine eindeutige Zahl; der Echo-Funker hat kein Discord-Konto. Dann in der ersten
Sitzung Strg+C und noch einmal `sudo bash funk-einrichten.sh`; das startet ihn als Dienst.

**Das siehst du:** `sudo journalctl -u funk-echo -n 20 --no-pager` zeigt `Bereit im Netz 'Echo' (ID 90)`. Im Client
belegst du ein Feld mit dem Netz Echo (als Admin etwa, indem du auf der Funkfeld-Zuteilung deinen Namen von links auf den Kanal
Echo im Baum ziehst; bis 0.11.1 deine Karte auf dem Brett auf das Netz Echo) und sprichst einen Satz hinein; eine halbe
Sekunde später kommt er zurück. Von außen geprüft (etwa
über den Hotspot eines Handys), ist damit der ganze Weg über Router und DNS belegt. Einrichtung von Hand und eine
Echo-Probe ohne Client stehen in B5.9.

### A18.3 Mitglieder freischalten

Neue Mitglieder installieren das normale Setup, verbinden und schicken dir in Discord ihren SC-Namen und ihre
Gerätekennung. Zwei Wege:

- **Im Client:** **Verwaltung**, **Anträge**. Freigeben braucht dort einen Code aus der Authenticator-App, der höchstens 5
  Minuten alt ist; sonst fragt der Client danach.
- **An der Konsole:**

```
sudo funk-admin antraege
sudo funk-admin freigeben <gerätekennung>
```

Vergleiche immer zuerst die Kennung im Antrag mit der, die dir das Mitglied geschickt hat. Die Namen in einem Antrag kann
jeder eintippen; nur die Kennung zeigt, dass der Antrag von diesem PC kommt.

Was `antraege` außerdem nennt:

| Hinweis | Was du tust |
| --- | --- |
| `passt zu Vormerkung <id> <SC-Name>` | `sudo funk-admin freigeben <gerätekennung> --vormerkung <id>` übernimmt die Vormerkung |
| `BESTEHENDES KONTO? ...` | Seit 0.7.0 bekommt jeder PC sein eigenes Konto (ein PC, ein Konto): einfach freigeben. Nur den PC eines Admins mit neuem Geräteschlüssel hängst du an sein Konto, wenn er dieselbe Kennung zeigt: `sudo funk-admin freigeben <gerätekennung> --an-konto <id>` |
| `DISCORD-NAME VERGEBEN (...)` | Seit 0.7.0 geht Freigeben trotzdem; das neue Konto bekommt den Discord-Namen nur nicht. Ist es derselbe Mensch mit einem weiteren PC, passt das so |

Ein Konto hat höchstens 3 aktive Geräte.

### A18.4 Die wichtigsten Befehle

Der Kurzbefehl `funk-admin` ergänzt die Einstellungsdatei selbst. Alle Befehle zeigt `sudo funk-admin hilfe`.

| Befehl | Wofür |
| --- | --- |
| `sudo funk-admin status` | Verbindungen, Sitzungen, Konten, Geräte, Netze |
| `sudo funk-admin konten` | alle Konten mit Nummer, Stufe, Status und Namen |
| `sudo funk-admin geraete` | alle Geräte mit Kennung und Zustand |
| `sudo funk-admin admins` | die Admin-Nachweise |
| `sudo funk-admin antraege` | offene Anträge |
| `sudo funk-admin freigeben <gerätekennung>` | ein Gerät freigeben |
| `sudo funk-admin auftrag --konto <id>` | Auftrag für einen Abend, ohne `--bis` bis 04:00 Uhr |
| `sudo funk-admin sperrliste` | laufende Sperren |
| `sudo funk-admin entsperren --ip <adresse>` | die Sperre einer Adresse aufheben |
| `sudo funk-admin sicherung` | Sicherung des Zustands (Kapitel A20) |
| `sudo funk-admin pruefen --vollstaendig` | prüft Einstellungen, Schlüssel, Profil, DNS, Uhr und Ports; schreibt nichts |
| `sudo journalctl -u funk-server -n 30 --no-pager` | die letzten 30 Zeilen des Logbuchs |

Eine Option, die ein Befehl nicht kennt oder die doppelt ist, bricht ab, bevor etwas geschieht (`Nichts geändert.`).
Werte mit Leerzeichen gehören in Anführungszeichen. Jeden Befehl mit allen Optionen beschreibt B9. Für Phasen und für
"Heute als Funker" gibt es an der Konsole keinen Befehl; beides geht im Verwaltungsteil und im Client.


## A19 Updates

### A19.1 Reihenfolge

**Erst der Server, dann die Clients.** Ein neuer Client kann den Server nach Dingen fragen, die ein älterer Server nicht
kennt. Vor jedem Update des Servers eine Sicherung (Kapitel A20).

Fassung 0.5.0 bringt Client und Server. Für dich als Betreiber heißt das:

- Der Server 0.5.0 liest den Zustand von 0.4.0 ohne Verlust; Phasen, die laufende Phase und "Heute als Funker" kommen neu
  dazu. Gemessen mit Programmen von 0.4.0 und Testdaten, die sie selbst geschrieben haben (Windows, unter Linux nicht
  gemessen). `server.json` und die Units brauchen nichts Neues; in den Units steht nur eine Kommentarzeile mehr.
- Clients 0.4.x funken weiter mit dem Server 0.5.0, sehen aber keine Phasen und kein "Heute als Funker". Ein Client 0.5.0
  an einem Server 0.4.0 blendet beides aus; sonst geht alles.
- Zurück auf 0.4.0 ist möglich: Der Server 0.4.0 startet auf dem Zustand von 0.5.0. Beim ersten Schreiben gehen aber die
  Phasen, die laufende Phase und "Heute als Funker" (`alsFunkerBis` im Konto) verloren; alles andere bleibt.
- Das Serverpaket 0.5.0 bringt `HEIMSERVER-PROXMOX.md` mit dem Abschnitt 17 mit (1.9).

Was jede Fassung am Server verlangt und wie ein Update von Hand geht, steht in B19; für den von Hand eingerichteten
Heimserver in `HEIMSERVER-PROXMOX.md`, Abschnitt 17.

### A19.2 Den Server

Ein Server, den das Skript eingerichtet hat, wird mit demselben Befehl aktualisiert, im Ordner `linux` des neuen Pakets
(auspacken wie in A15.2):

```
sudo funk-admin sicherung
sudo bash funk-einrichten.sh
```

Das Skript fragt, ob es den laufenden Dienst kurz anhalten darf (`funk-server läuft mit ... Für die Programme ... kurz
anhalten? (j/n) [j]`). Die alten Programme legt es nach `/opt/funk.<alte fassung>`, dann startet der Dienst mit den neuen.
Alle Clients bekommen dabei die Meldung, dass der Server gewartet wird, und kommen nach etwa 10 Sekunden von selbst
wieder. Was eine Fassung sonst noch braucht, steht in den Hinweisen zum Paket (`LIESMICH.txt` im Serverpaket).

Ein Server, der von Hand eingerichtet wurde, wie der heutige Heimserver, wird nach `HEIMSERVER-PROXMOX.md` aktualisiert:
Abschnitt 13 allgemein, Abschnitt 16 für den Schritt auf 0.4.0, Abschnitt 17 für den Schritt auf 0.5.0.

Mach Updates nicht kurz vor oder während eines Einsatzes.

### A19.3 Die Clients

Jedes Mitglied startet das neue Setup und wählt **Aktualisieren**. Admins nehmen das Admin-Setup derselben Fassung.

### A19.4 System und .NET

Etwa monatlich, wenn die Laufzeit über `apt` kam:

```
sudo apt update && sudo apt full-upgrade -y
sudo systemctl restart funk-server funk-echo
```

Hat das Skript die Laufzeit mit dem Installationsskript von Microsoft gelegt, holst du sie mit demselben Skript neu
(`dotnet-install.sh --channel 10.0 --runtime dotnet --install-dir /usr/share/dotnet`).

Sicherheitsupdates des Systems kann Debian von selbst einspielen (`unattended-upgrades`, `HEIMSERVER-HAERTUNG.md`, Punkt
4). Die .NET-Laufzeit aktualisierst du trotzdem selbst mit Neustart des Dienstes: Ein stilles Update mitten im Raid wäre
schlechter als eines, das du planst.


## A20 Sicherung

### A20.1 Der Zustand

Regelmäßig und vor jedem Update:

```
sudo funk-admin sicherung
```

Die Sicherung landet unter `/var/lib/funk/sicherungen/<zeit>`: Konten, Geräte, Netze, Einheiten, Phasen, Aufstellungen,
Mitglieder, Admin-Nachweise und das Journal der Verwaltung. Kopier den Ordner danach vom Server weg. Das Journal enthält
Klarnamen, also so sorgfältig aufbewahren wie die Schlüssel. Schlüssel und Betriebsgeheimnis kommen nie in diese
Sicherung, die Sperrliste auch nicht, und laufende Aufträge der Einsatzleitung nimmt sie ebenfalls nicht mit, ebenso
wenig "Heute als Funker": Beim Zurückspielen verfallen beide. Die Phasen und die laufende Phase bleiben.

Zurückspielen ist selten. Es steht Schritt für Schritt in B18.5 und in `HEIMSERVER-PROXMOX.md`, Abschnitt 12 (dort im
Container als root); der Funk ist dabei aus.

### A20.2 Die Schlüssel

Die wichtigsten Dateien sind `server.key`, `server.crt` und `funk-geheimnis` in `/etc/funk/credentials`. Geht der
Serverschlüssel verloren, gibt es einen neuen Pin, und jeder Client braucht ein neues Profil. Leg diese drei deshalb
einmal auf einen verschlüsselten Stick, nie in eine Cloud, nie in einen Chat. Verschlüsseln kannst du ihn mit BitLocker
To Go, der Verschlüsselung von Windows für USB-Sticks und andere Wechseldatenträger; ohne Kennwort ist der Inhalt dann
nicht lesbar. Wie du sie aus dem
Container holst, steht in B18.3.

Den TOTP-Schlüssel `funk-totp-schluessel` holst du nicht heraus; er bleibt nur auf dem Server. Geht er verloren, richtet
der Eigentümer des Servers den Zweitfaktor jedes Admins an der Konsole neu ein (`admin-einrichtung-neu`).

### A20.3 Den ganzen Rechner

Eine Sicherung des ganzen Containers (Proxmox `vzdump`) oder ein Abbild beim Anbieter enthält auch
`/etc/funk/credentials` und das Journal. Lege solche Sicherungen nur verschlüsselt ab.

### A20.4 Notfallcodes

Die zehn Notfallcodes aus der Einrichtung des Zweitfaktors bewahrst du getrennt vom Telefon auf. Jeder gilt einmal. Neue
gibt es nur an der Konsole (`sudo funk-admin notfallcodes --konto <id>`), dann gelten die alten nicht mehr.


## A21 Wenn am Server etwas nicht klappt

| Meldung oder Lage | Was zu tun ist |
| --- | --- |
| `status=226/NAMESPACE` | nur im Proxmox-Container: auf dem Host `pct set <id> --features nesting=1`, Container neu starten |
| `status=243/CREDENTIALS` | Nennt das Skript eine fehlende Schlüsseldatei: noch einmal aufrufen, es legt fehlende an (Achtung, siehe unter der Tabelle). Fehlt keine und ist es ein Proxmox-Container: wie bei 226 |
| `Der Server lehnt die Konfiguration ab` | `/etc/funk/server.json` berichtigen; `sudo funk-admin pruefen --vollstaendig` nennt jeden Befund |
| `Die Uhr ist nicht synchronisiert` | `sudo apt install systemd-timesyncd`. Der Zweitfaktor verträgt nur 30 Sekunden Abweichung. |
| `... gibt es schon, stammt aber nicht von JG TacRelay` | eine fremde Datei; das Skript lässt sie liegen, du entscheidest |
| `/usr/bin/dotnet gibt es schon, aber ohne Laufzeit 10` | Ein vorhandenes .NET gehört womöglich einem anderen Dienst. Die Laufzeit 10 auf demselben Weg dazulegen, auf dem es kam, dann noch einmal aufrufen. |
| Das Skript soll neu fragen | Es überschreibt `/etc/funk/server.json` nie. Zur Seite legen (`sudo mv /etc/funk/server.json /etc/funk/server.json.alt`), noch einmal aufrufen. |
| Clients zeigen `Verbunden (Tunnel)` | UDP kommt nicht durch: UDP-Freigabe im Router oder in der Firewall des Anbieters prüfen |
| Clients erreichen den Server nicht | Zeigt der Name im DNS auf die richtige Adresse? Stimmen die Freigaben? Von außen (Hotspot) in PowerShell: `Test-NetConnection <name> -Port 8443` muss `TcpTestSucceeded : True` zeigen. |
| Ein Mitglied kommt nicht herein, obwohl freigegeben | Vielleicht teilt es seine Adresse gerade mit einem Angreifer (Mobilfunk). `sudo funk-admin sperrliste`, dann `sudo funk-admin entsperren --ip <adresse des mitglieds>`; die Adresse nennt dir das Mitglied. |
| Ein Mitglied meldet `Zu oft zwischen Geräten gewechselt` | `sudo funk-admin verdraengung-freigeben --konto <id>` |
| Ein Mitglied hat einen neuen PC | neuer Antrag und seit 0.7.0 ein eigenes Konto (ein PC, ein Konto); ist der alte PC weg, sein Konto löschen (A18.3) |
| Ein Gerät ist verloren oder gestohlen | `sudo funk-admin sperren <gerätekennung>`; die laufende Sitzung endet sofort |
| Ein Gerät ist zu Unrecht gesperrt | seit 0.8.0 `sudo funk-admin sperre-aufheben <gerätekennung>` oder in der Verwaltung auf der Karte des Mitglieds unter Geräte; danach stellt der PC einen neuen Antrag |
| Admin hat sein Telefon verloren | einen Notfallcode nehmen; danach an der Konsole `sudo funk-admin admin-einrichtung-neu --konto <id>` und in der App neu einrichten |
| Zweitfaktor gesperrt | `sudo funk-admin admin-entsperren --konto <id>` |
| Ein Admin-PC ist verloren | `sudo funk-admin admin-schluessel-entfernen --konto <id> --kennung <kennung>` und das Gerät sperren |

**Achtung beim TOTP-Schlüssel:** Fehlt `funk-totp-schluessel` auf einem Server, der schon Admins hat, legt das Skript in
Fassung 0.5.0 (wie in 0.4.0) ohne Warnung einen neuen an (nur die Zeile `NEU    TOTP-Schlüssel ...` zeigt es). Danach lässt sich der
Zweitfaktor keines Admins mehr prüfen, und jeder muss ihn an der Konsole neu einrichten
(`sudo funk-admin admin-einrichtung-neu --konto <id>`). Fehlt die Datei auf einem Server mit Admins,
hol sie deshalb zuerst aus einer Sicherung des ganzen Rechners zurück, bevor du das Skript noch einmal aufrufst.

Läuft der Server und du willst nur nachsehen: `sudo funk-admin pruefen --vollstaendig` schreibt nichts und meldet je
Befund `OK`, `HINWEIS`, `WARNUNG` oder `FEHLER`, am Ende `Ergebnis: n Fehler, m Warnungen.` Weitere Meldungen mit Ursache
und Abhilfe stehen in B21, was im Ernstfall zu sperren ist, in C19.


## A22 Ausblick: der Umzug auf einen gemieteten Server

Läuft der Funk auf einem Heimserver, ist ein späterer Umzug auf einen gemieteten Server bei einem anderen Betreiber
vorbereitet. Der Umzug ist so gebaut, dass niemand ein
neues Setup braucht und keine Konten, Geräte, Netze, Mitglieder, Admins oder Eigentümer verloren gehen. Die genaue Anleitung
steht in `UMZUG.md` im Serverpaket und als Ablauf in zwölf Schritten in B20. Das Wichtigste vorweg:

- **Alle Clients ab 0.4.0.** Erst diese Fassung übernimmt ein neues Profil vom Server. Vor dem Umzug bekommen alle das
  Setup 0.4.0 oder neuer, und erst dann lässt der alte Server ältere Clients nicht mehr herein.
- **Derselbe TCP-Port auf beiden Servern: 8443.** Ein Profil kennt nur einen Port für alle Server darin, und im Übergang
  stehen beide darin. Der neue Betreiber antwortet im Skript also mit 8443, auch wenn 443 frei wäre.
- **Kurze TTL** für den neuen Namen, höchstens 300 Sekunden.
- **Der neue Server bekommt einen eigenen Schlüssel.** Der neue Betreiber richtet ihn nach A15 ein, schickt die Anfragezeile und liest am
  Telefon Kennung, Name und Port vor. Der Eigentümer des Programms schreibt daraus ein Übergangsprofil mit beiden Servern.
  Der alte Server reicht es an die Clients weiter; so lernen sie den neuen kennen.
- **Zustand per Umzugspaket.** Konten, Geräte und Admins kommen verschlüsselt mit einer Passphrase von 26 Zeichen mit
  (`umzug-packen` auf dem alten, `funk-einrichten.sh --umzug` auf dem neuen Server). Der erste Admin entfällt dabei.
