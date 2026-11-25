# Lasttest Funk-Server (Loopback, 29.09.2026)

Szenarien A bis D nach server-sicherheit.md, Befund B20, je 30 Sekunden, gemessen mit `funk-testclient` gegen
`funk-testserver` auf diesem Rechner. Stand der Arbeitskopie: Branch `modul/server`.

**Loopback misst die Leitung nicht.** Server und Clients liefen auf demselben Rechner über 127.0.0.x. Es gab also
keinen Paketverlust, keine Warteschlange im Router, keine DSL-Laufzeit und keinen Jitter einer echten Strecke. Gemessen
sind die Leistung von Server und Client-Bibliothek sowie die Bytes, die der Server hinausschickt. Was das für die
Heimleitung (50 Mbit/s Upload) heißt, ist aus diesen Bytes hochgerechnet (Abschnitt "Upload-Bedarf").

## Aufbau

- Rechner: AMD Ryzen 9 5950X (16 Kerne, 32 logische), 64 GB RAM, Windows 11 Pro for Workstations, .NET 10.0.12,
  Release-Build.
- Server: `funk-testserver`, der unveränderte Dienst `Funk.Server.Dienst` in einem eigenen Prozess, dazu ein Testzugang
  auf Loopback. Statt TLS beweist der Client dort den Besitz seines Geräteschlüssels per Signatur über einen
  Zufallswert; danach läuft die Verbindung denselben Weg wie nach einem TLS-Handshake. Grund: Unter Windows legt
  Schannel für jeden echten Handshake Schlüsseldateien im Benutzerprofil an. TLS selbst ist hier also nicht belastet.
- Clients: `funk-testclient` mit 40 simulierten Funkern in einem Prozess, jeder mit eigenem Geräteschlüssel (nur im
  Speicher), eigener Adresse 127.0.20.x, eigenem Steuerkanal, UDP-Socket, Sende- und Empfangs-Thread (Funk.ClientKern).
- Admin-Schritte automatisch über `funk-admin` in einem frischen Zustandsverzeichnis unter `tr-last\<zeit>\` außerhalb der Arbeitskopie:
  Serverschlüssel, Reserve-Pin, Betriebsgeheimnis, Profilschlüssel und signiertes Profil offline erzeugt, dann je Funker
  `verknuepfen` (der Client bestätigt die Rückfrage) und `freigeben`, dazu `netz-setzen` für die Netze 10 bis 21 und 30.
  Das Verzeichnis ist nach jedem Lauf gelöscht. Anmeldung aller 40 bis AKTIV/UDP: 4,3 bis 4,6 s.
- Felder setzt jeder Funker selbst per `FeldSetzen` (offene Netze). Sprecher senden im 20-ms-Takt synthetische
  Opus-Rahmen mit 160 Byte (64 kbit/s): TOC `0xF8` (CELT, 20 ms, mono, nach 5.13 gültig), dahinter ein Testrahmen mit
  Sprecher, laufender Nummer und Sendezeitpunkt. Daraus rechnet der Empfänger Verlust, Laufzeit und Jitter.
- Vor den Szenarien 3 s Aufwärmen mit dem Verkehr von B (JIT, Pools), nicht gewertet.
- Pacer (7.4) für die Hauptläufe auf 10.000 Mbit/s gestellt, damit Loopback die Leistung des Servers zeigt und nicht
  das Heimbudget. Ein Ergänzungslauf mit dem Heimbudget 35 Mbit/s steht weiter unten.

Szenarien bei 40 Funkern:

- A: 5 Netze, Sprecher 0 bis 4 je in ihrem Netz, je 12 Hörer aus den übrigen 35 (60 Ströme).
- B: 12 Netze, Sprecher 0 bis 11 je in ihrem Netz, je 15 Hörer aus allen anderen (180 Ströme).
- C: Funker 0 (RAIDLEITER) mit Durchsage an alle, 39 Hörer (39 Ströme).
- D: alle 40 im Netz 30, alle sprechen zugleich (1560 Ströme).

Gemessen je Empfänger und zusammengefasst:

- Verlust: erwartete Pakete (gesendete Frames der Sprecher, die er hören muss) gegen empfangene.
- Laufzeit Mund-Server-Ohr: vom Übergeben des Frames an die Client-Bibliothek bis zum geprüften, entschlüsselten
  Paket beim Hörer (Sendeschlange, Versiegeln, UDP, Relais, Stempeln, Pacer, UDP, Entschlüsseln).
- Jitter nach RFC 3550 je Strom, mit dem echten Sendezeitpunkt als S (Transportjitter ohne den Takt des Testclients).
- Pakete/s und kbit/s je Empfänger (Datagramm wie auf dem Draht: Kopf 16 + Stempel 13 + 2 je Netz + Opus + Tag 16).
- Am Server: CPU (Prozesszeit je Sekunde, 100 % = ein Kern), Arbeitsspeicher und privater Speicher des Prozesses,
  jede Sekunde; Verarbeitungszeit im Relais (p50/p99 je 1-s-Fenster aus `messwerte`), Verwürfe, Laststufe.

## Ergebnis: alle Sprecher im selben 20-ms-Takt (schlimmster Fall)

Alle Sprecher senden im selben Augenblick; der Server bekommt je 20 ms einen Stoß und fächert ihn auf einmal auf.

| Wert | A | B | C | D |
| --- | ---: | ---: | ---: | ---: |
| Funker / Sprecher / Ströme | 40 / 5 / 60 | 40 / 12 / 180 | 40 / 1 / 39 | 40 / 40 / 1560 |
| Dauer (s) | 30,0 | 30,0 | 30,0 | 30,0 |
| Frames gesendet (hoch) | 7.500 | 18.000 | 1.500 | 60.000 |
| Pakete erwartet / empfangen (runter) | 90.000 / 90.000 | 270.000 / 270.000 | 58.500 / 58.500 | 2.340.000 / 2.340.000 |
| Verlust gesamt / schlechtester Empfänger | 0 % / 0 % | 0 % / 0 % | 0 % / 0 % | 0 % / 0 % |
| Laufzeit Mund-Server-Ohr p50 / p95 / p99 (ms) | 0,40 / 0,63 / 0,75 | 0,86 / 1,45 / 1,66 | 0,32 / 0,49 / 0,59 | 5,91 / 10,71 / 11,48 |
| Laufzeit max (ms) | 8,7 | 2,5 | 1,1 | 18,6 |
| Jitter je Strom, Mittel / max (ms) | 0,18 / 0,25 | 0,22 / 0,30 | 0,03 / 0,04 | 0,55 / 0,78 |
| Pakete/s je Empfänger, Mittel / max | 86 / 100 | 225 / 250 | 50 / 50 | 1.951 / 1.951 |
| kbit/s je Empfänger (UDP-Nutzlast), Mittel / max | 142 / 166 | 373 / 414 | 82 / 82 | 3.231 / 3.231 |
| Server-Ausgang Pakete/s | 3.002 | 9.006 | 1.951 | 78.051 |
| Server-Ausgang Mbit/s (UDP-Nutzlast) | 4,97 | 14,91 | 3,20 | 129,25 |
| Server-CPU Mittel / max (% eines Kerns) | 5 / 14 | 8 / 12 | 5 / 15 | 60 / 66 |
| Server-Arbeitsspeicher max / privat max (MB) | 100 / 48 | 97 / 45 | 99 / 46 | 94 / 41 |
| Relais-Verarbeitung p50 Mittel / p99 max (ms) | 0,065 / 1,47 | 0,103 / 0,24 | 0,068 / 0,19 | 0,531 / 1,44 |
| Relais verworfen / Pacer-Altersverwurf | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 17 |
| Laststufe max (7.4) | 0 | 0 | 0 | 0 |

Die 17 Altersverwürfe in D trafen keine gewerteten Audio-Frames (alle 2.340.000 kamen an); es waren Pakete am Rand des
Laufs (Keepalive-Echo bzw. ENDE nach dem Messfenster). Die Laufzeit p50 von 5,9 ms in D heißt: Ein Paket des Stoßes von
1560 Paketen je 20 ms wartet im Mittel rund 5 ms, bis es an der Reihe ist. In einem früheren Lauf mit demselben Aufbau
kam dabei kurz Laststufe 1 (mittlere Pacer-Wartezeit über 5 ms).

## Ergebnis: Sprecher über die 20 ms verteilt (10 Sendegruppen)

Wirklichkeitsnäher: Jeder Client hat seine eigene Audio-Uhr, die Sprecher senden also nicht im selben Augenblick. Hier
in 10 Gruppen, um je 2 ms versetzt; jeder Sprecher sendet weiter genau alle 20 ms.

| Wert | A | B | C | D |
| --- | ---: | ---: | ---: | ---: |
| Pakete erwartet / empfangen (runter) | 90.000 / 90.000 | 270.000 / 270.000 | 58.500 / 58.500 | 2.340.000 / 2.340.000 |
| Verlust gesamt / schlechtester Empfänger | 0 % / 0 % | 0 % / 0 % | 0 % / 0 % | 0 % / 0 % |
| Laufzeit Mund-Server-Ohr p50 / p95 / p99 (ms) | 0,18 / 0,28 / 0,36 | 0,19 / 0,37 / 0,57 | 0,37 / 0,60 / 0,83 | 0,73 / 1,34 / 2,31 |
| Laufzeit max (ms) | 5,0 | 21,9 | 1,8 | 14,0 |
| Jitter je Strom, Mittel / max (ms) | 0,04 / 0,09 | 0,04 / 0,08 | 0,04 / 0,05 | 0,26 / 0,37 |
| Server-Ausgang Pakete/s | 3.000 | 9.001 | 1.950 | 78.004 |
| Server-Ausgang Mbit/s (UDP-Nutzlast) | 4,97 | 14,90 | 3,20 | 129,18 |
| Server-CPU Mittel / max (% eines Kerns) | 8 / 17 | 21 / 31 | 3 / 12 | 74 / 93 |
| Server-Arbeitsspeicher max / privat max (MB) | 100 / 48 | 98 / 46 | 100 / 48 | 94 / 42 |
| Relais-Verarbeitung p50 Mittel / p99 max (ms) | 0,042 / 0,27 | 0,040 / 10,88 | 0,079 / 0,90 | 0,075 / 0,18 |
| Relais verworfen / Pacer-Altersverwurf | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| Laststufe max (7.4) | 0 | 0 | 0 | 0 |

C hat nur einen Sprecher und ändert sich deshalb nicht. Die Laufzeit in D von p50 5,9 ms im gleichphasigen Lauf ist
also fast ganz das Abarbeiten des Stoßes; verteilt liegt sie bei p50 0,7 ms und p99 2,3 ms. Einzelne Ausreißer bis
22 ms (B: Laufzeit max 21,9 ms, ein 1-s-Fenster mit Relais-p99 10,9 ms) sind Aussetzer auf dem Rechner (Speicherbereinigung
oder Thread-Planung bei über 100 Threads im Testclient), kein Verlust.

## Upload-Bedarf der Heimleitung

Aus den gemessenen Bytes hochgerechnet: Jedes Paket runter ist als UDP-Nutzlast 207 Byte groß (16 Kopf, 13 Stempel,
2 je Netz, 160 Opus, 16 Tag; bei der Durchsage an alle 205 Byte ohne Netz-ID). Auf der Leitung kommen 28 Byte IPv4 und
UDP sowie der Zuschlag von 26 Byte für PPPoE und Ethernet dazu (7.4), zusammen 261 Byte, bei 50 Paketen/s also
104,4 kbit/s je Strom. Der Upload des Heimanschlusses trägt den Ausgang des Servers.

| Szenario | Ströme | Upload-Bedarf (Mbit/s) | Anteil an 50 Mbit/s brutto | Anteil am Pacer-Budget 35 Mbit/s | B20 geschätzt (Mbit/s) |
| --- | ---: | ---: | ---: | ---: | ---: |
| A | 60 | 6,27 | 13 % | 18 % | 5,3 |
| B | 180 | 18,80 | 38 % | 54 % | 15,8 |
| C | 39 | 4,04 | 8 % | 12 % | 3,4 |
| D | 1560 | 162,97 | 326 % | 466 % | 137 |

- A, B und C passen gut in die Heimleitung, B mit gut der Hälfte des Pacer-Budgets. D braucht mehr als das
  Dreifache des ganzen Uploads und geht zu Hause nicht; das hatte B20 schon als unrealistisch eingestuft.
- Die Messung liegt rund 19 % über der Schätzung aus B20. Dort fehlen der Stempel des Servers (13 Byte) und die Netz-ID
  (2 Byte), und B20 rechnet ohne den Zuschlag von 26 Byte für PPPoE und Ethernet.
- Mit IPv6 kämen je Paket 20 Byte dazu (+8 %). Mit Opus 48 kbit/s (120 Byte) wären es 221 Byte auf der Leitung, also
  88,4 kbit/s je Strom: B 15,9 Mbit/s, D 138 Mbit/s.
- Das Pacer-Budget von 35 Mbit/s reicht bei 64 kbit/s für etwa 335 gleichzeitige Ströme, bei 48 kbit/s für etwa 395.
- Download am Server (Sprecher hoch, mit Zuschlag): A 0,51, B 1,23, C 0,10, D 4,11 Mbit/s.
- Je Hörer kommen in D 1.950 Pakete/s an, 4,1 Mbit/s auf der Leitung; das muss auch der Download jedes Clients tragen.

## Ergänzungslauf: Heimbudget 35 Mbit/s im Pacer

Derselbe Aufbau, 10 Sendegruppen, Pacer auf 35 Mbit/s wie in der Heimphase.

| Wert | B | D |
| --- | ---: | ---: |
| Pakete erwartet / empfangen | 270.000 / 270.000 | 2.340.000 / 503.029 |
| Verlust gesamt / schlechtester Empfänger | 0 % / 0 % | 78,5 % / 99,0 % |
| Laufzeit p50 / p99 (ms) | 0,18 / 0,37 | 19,2 / 20,2 |
| Server-Ausgang auf der Leitung (Mbit/s) | 18,79 | 35,01 |
| Server-CPU Mittel / max (% eines Kerns) | 12 / 18 | 115 / 124 |
| Pacer-Altersverwurf | 0 | 1.839.843 |
| Laststufe max | 0 | 1 |

- B läuft mit dem Heimbudget ohne Verlust.
- D wird auf genau 35 Mbit/s begrenzt; 78,5 % fallen im Pacer als zu alt heraus. Die Pakete, die durchkommen, haben
  fast die ganzen 20 ms Wartezeit hinter sich.
- Der Verwurf trifft die Hörer sehr ungleich: 33 Hörer verlieren 73 bis 81 %, 7 Hörer 94 bis 99 %. Die Hörer am Ende
  der Auffächerung gehen fast leer aus. Der gezielte Strom-Abwurf der Überlaststufe 1 (7.4) ist im Server noch nicht
  gebaut (offener Punkt des Server-Moduls); dieser Lauf zeigt, warum er gebraucht wird.
- Der Pacer dreht unter Windows bei Fehlbeträgen unter 2 ms, statt zu schlafen; daher über 100 % CPU in D.

## Einordnung und Grenzen

- Auf diesem Rechner trägt der Server alle vier Szenarien ohne Verlust, D mit 78.000 Paketen/s bei 60 bis 75 %
  eines Kerns. Die Verarbeitung im Relais bleibt im p99 je 1-s-Fenster unter 1,5 ms (Z2: unter 2 ms), bis auf einzelne
  Ausreißerfenster (10,9 ms in B verteilt); Speicher rund 100 MB Arbeitsspeicher, 41 bis 48 MB privat.
- Clients und Server teilen sich denselben Rechner; die Clients brauchen in D selbst mehrere Kerne (78.000
  Entschlüsselungen/s). Auf dem Ziel-LXC mit weniger Kernen sieht das anders aus: P-30 (B für 1 h) und P-31 (D hinter
  einem Shaper) auf dem LXC stehen weiter aus.
- Windows statt Linux: Socket-Verhalten, Zeitgeber und Pacer unterscheiden sich.
- Kein TLS im Test (Testzugang), kein Audio (synthetische Rahmen), keine echte Strecke mit Verlust, Umordnung und
  Jitter (dafür P-21 mit UDP-Proxy), nur 30 s je Szenario.
- Der Testclient wartet die 20 ms bis zum nächsten Takt zuletzt aktiv ab, weil `Thread.Sleep(1)` unter Windows bis zu
  15,6 ms dauert; die Zeitgeberauflösung des Systems bleibt unverändert. Seine Verspätung lag im p99 unter 0,01 ms.

## Nachvollziehen

```
. C:\Claude\tools\dev-umgebung.ps1
dotnet build C:\Claude\work\JG-TacRelay-wt\server\TacRelay.slnx -c Release
dotnet run --project C:\Claude\work\JG-TacRelay-wt\server\tests\Funk.Testclient -c Release --no-build -- last --funker 40 --sekunden 30 --szenarien ABCD
dotnet run --project C:\Claude\work\JG-TacRelay-wt\server\tests\Funk.Testclient -c Release --no-build -- last --funker 40 --sekunden 30 --szenarien ABCD --gruppen 10
dotnet run --project C:\Claude\work\JG-TacRelay-wt\server\tests\Funk.Testclient -c Release --no-build -- last --funker 40 --sekunden 30 --szenarien BD --gruppen 10 --ausgang-mbit 35
```

`--aus <datei.json>` schreibt alle Werte je Empfänger, `--tabelle <datei.md>` die Tabelle. Gegen einen laufenden
Server (etwa den LXC): `lauf --profil ... --adresse ... --port ... --zugang tls --admin ... --socket ...`, siehe
`funk-testclient hilfe` und docs\BETRIEB.md.
