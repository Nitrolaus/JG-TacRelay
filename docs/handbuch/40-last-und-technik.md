## Teil D: Was 20 oder 40 Leute brauchen

Dieser Teil rechnet vor, wie viel Leitung, Rechenzeit und Speicher ein Funkabend mit 20 oder 40 Leuten braucht
und wie lange eine Stimme vom Mund bis zum Ohr unterwegs ist. Jede Zahl lässt sich auf den Quelltext oder auf eine
Messung zurückführen; wo beides fehlt, steht ausdrücklich Annahme dabei. Mit den Formeln aus diesem Teil kannst
du eigene Zahlen einsetzen, etwa wenn eure Besatzungen größer sind als hier angenommen.

Beschrieben ist die Fassung 0.5.0 (Code-Stand `95aa78b`, 02.10.2026). Die Zahlen stammen aber aus Berechnungen am
Code von 0.4.0 und aus dem Lasttest vom 29.09.2026 gegen den Server 0.4.0; sie sind für 0.5.0 nicht neu gemessen. Sie
gelten trotzdem unverändert, denn am Code geprüft ist: `Funk.Protokoll\Sprache` und `Funk.Server\Sprachpfad` sind gegenüber
0.4.0 bis auf die neue Kopfzeile in jeder Quelldatei Byte für Byte dieselben, ebenso die in D16 genannten Dateien für Opus,
Gate, Jitterpuffer, Mischer, Relais, Pacer, Keepalive, Ping und Bericht. Von den in D16 genannten Dateien haben sich nur
`Steuerbremse.cs`, `Grenzen.cs`, `Anmeldung.cs`, `Fristen.cs` und `Sitzung.cs` um Einträge für Phasen und Funker-Modus
erweitert und `Hilfe.cs`, `Einrichtung.cs` und `Konfigschreiber.cs` in Texten (D14.1 bleibt gültig). Auf dem Heimserver von Sarevoc läuft nach Angabe des Eigentümers
noch der Server 0.4.0 (1.9; nicht am Server nachgesehen). Was 0.5.0 gegenüber 0.4.0 und 0.4.1 verändert und diesen Teil
berührt, ist klein: Die Phasen vergrößern die Netzliste im ungünstigsten Fall und bringen einen neuen Push (D3.5); die
Hörprobe aus 0.4.1 läuft auf einem eigenen Mischer-Netz im Client und schickt nichts über die Leitung; der Funk-Klick
(einstellbar ab 0.5.0) und die Melde-Taste erzeugen keinen neuen Pakettyp. Der Klick entsteht im Client und geht nie durch
die Leitung, und die Melde-Taste sendet dieselben Sprachpakete wie die Sprechtaste, nur in das Meldenetz. Ihre Rechenzeit
und die des neuen Prüfwegs je Sprachpaket am Server (ein Zeitvergleich für den Funker-Modus, Protokoll 6.15) sind nicht
gemessen (D15).

## D1 Wie die Angaben gekennzeichnet sind

| Kennzeichen | Bedeutung |
| --- | --- |
| Code | steht so im Quelltext von 0.4.0 und unverändert in 0.5.0 (Stand `95aa78b`, siehe oben); Datei und Stelle in D16 |
| gemessen | gemessen, mit Datum und Bericht (meist `docs\LASTTEST.md` oder ein Modulbericht) |
| gerechnet | aus Code-Werten und Messungen mit den Formeln dieses Teils ermittelt, nicht gemessen |
| Annahme | gesetzt, damit sich rechnen lässt; ersetze sie durch eure eigenen Zahlen |
| Ziel | Vorgabe der Spezifikation `docs\FUNKPROTOKOLL-v1.md`, kein Messwert |

Die Kennzeichen sind dieselben wie in der Lesehilfe im Titelteil; "Ziel" kommt nur in diesem Teil vor. Zahlen schreibt
dieser Teil deutsch: Komma vor den Nachkommastellen, Punkt als Tausendertrenner. In den Codeblöcken stehen sie ohne
Tausendertrenner.

## D2 Das Ergebnis vorab

| Größe | 20 Leute | 40 Leute | Kennzeichen |
| --- | --- | --- | --- |
| Upload des Servers, Normalfall, 1 / 3 / 5 Sprecher | 1,68 / 2,39 / 3,09 Mbit/s | 3,45 / 4,15 / 4,86 Mbit/s | gerechnet |
| Upload des Servers, schlechter Fall, 1 / 3 / 5 Sprecher | 1,98 / 5,95 / 9,92 Mbit/s | 4,07 / 12,21 / 20,36 Mbit/s | gerechnet |
| Download des Servers, 1 / 3 / 5 Sprecher | 0,09 bis 0,10 / 0,26 bis 0,31 / 0,43 bis 0,51 Mbit/s | gleich | gerechnet |
| Ausgangsbudget daheim (`grenzen.ausgangMbit`) | 35 Mbit/s | 35 Mbit/s | Code |
| Download je Client | 0,09 bis 0,52 Mbit/s | gleich | gerechnet |
| Upload je Client beim Sprechen | 0,09 bis 0,10 Mbit/s | gleich | gerechnet |
| Rechenzeit des Servers bei 39 bis 180 Strömen | 3 bis 21 % eines Kerns im Mittel | gleich | gemessen (Entwicklungsrechner) |
| Arbeitsspeicher des Servers | rund 100 MB, davon 41 bis 48 MB privat | gleich | gemessen (Entwicklungsrechner) |

- Der Heimanschluss trägt jeden dieser Fälle. Der teuerste, 40 Leute mit 5 Sprechern, die alle hören, belegt 58 % des
  Budgets von 35 Mbit/s.
- Die Grenze liegt bei gleichzeitigen Sprechern, die alle hören: bei 40 Leuten passen 8 (schlechter Fall) bis 10
  (Normalfall) solcher Sprecher gleichzeitig ins Budget, bei 20 Leuten 17 bis 20.
- Sprechen alle 40 zugleich zu allen, braucht der Server 138 bis 163 Mbit/s. Das geht daheim nicht, auf einem
  gemieteten Server mit 500 Mbit/s schon.
- Für die Clients ist die Last klein: Sie hängt nicht davon ab, ob 20 oder 40 Leute online sind, sondern nur davon,
  wie viele Sprecher einer gleichzeitig hört.

Normalfall und schlechter Fall sind in D5 festgelegt.

## D3 Bausteine aus dem Code

### D3.1 Opus: Bitrate und Rahmen

| Wert | Größe | Kennzeichen | Fundstelle |
| --- | --- | --- | --- |
| Abtastrate, Kanäle | 48 kHz, mono | Code | `OpusKodierer.Abtastrate`, `KodiererErstellen(Abtastrate, 1, ...)` |
| Rahmenlänge | 20 ms = 960 Samples | Code | `OpusKodierer.RahmenSamples` |
| Pakete je Sekunde und Sprecher | 50 | gerechnet aus Code | 1.000 ms / 20 ms |
| Zielbitrate | 48.000 bit/s, VBR (constrained), Komplexität 10 | Code | `OpusKodierer.StandardBitrate`, Konstruktor |
| FEC, DTX | In-band-FEC an, DTX aus | Code | `SetInbandFec 1`, `SetDtx 0` |
| Bitrate im Client | immer der Standardwert; `SetzeBitrate` ruft in 0.5.0 niemand auf (wie in 0.4.0) | Code | `KlangMaschinenOptionen.Bitrate` |
| Opus-Byte je Rahmen, rechnerisch | 48.000 / 8 / 50 = 120 Byte | gerechnet | |
| Opus-Byte je Rahmen, mit synthetischer Sprache | 41 bis 44 kbit/s, also 103 bis 110 Byte im Mittel | gemessen (29.09.2026) | `docs\MODULBERICHT-klang.md` |
| Opus-Byte je Rahmen, zweite Messung | 2 s synthetische Sprache: 55 bis 153 Byte, Median 126 Byte | gemessen (01.10.2026) | Testausgabe `KodiererZielTests` (E3.8) |
| Spanne laut Code | "bei 48 kbit/s ... 120 bis 160" Byte | Code (Kommentar) | `OpusKodierer.Kodieren` |
| Schätzung der Spezifikation mit FEC | etwa 140 Byte | Ziel | Protokoll 7.4 |
| Höchstlänge je Rahmen | 400 Byte | Code | `Sprachkanal.OpusMax`, `Sendeaufbereitung.MaxPaketBytes` |

DTX ist aus. Solange eine Sprechphase offen ist, gehen deshalb auch in kurzen Pausen volle 50 Pakete je Sekunde
hinaus. Zwischen zwei Phasen geht keine Sprache hinaus: bei der Sprechtaste, sobald sie losgelassen ist, bei der
Sprachaktivierung nach 300 ms Nachlauf (`SprachGate`, `NachlaufMs = 300`, Code).

Für die Rechnung gilt: Normalfall 120 Byte Opus je Rahmen, schlechter Fall 160 Byte. Mit 160 Byte lief auch der
Lasttest. Die Messungen mit synthetischer Sprache ergaben im Mittel 103 bis 110 Byte und in einem zweiten Lauf einen
Median von 126 Byte; der Normalfall mit 120 Byte liegt also im gemessenen Bereich, der schlechte Fall darüber.

### D3.2 Größe eines Sprachpakets

Jedes Sprachpaket ist ein UDP-Datagramm aus Kopf, verschlüsseltem Klartext und Prüfsumme (Tag) von AES-256-GCM.
Hoch heißt vom Client zum Server, runter vom Server zum Client.

| Teil | hoch | runter | Kennzeichen | Fundstelle |
| --- | ---: | ---: | --- | --- |
| Kopf: Version, Typ, Epoche, Sitzungs-ID, Zähler | 16 | 16 | Code | `Sprachkanal.KopfLaenge` |
| fester Teil des Klartexts | 9 | 13 | Code | `SpracheHoch.FesterTeil`, `SpracheRunter.FesterTeil` |
| Netz-IDs | 2 je Zielnetz (n) | 2 je Netz, in dem der Empfänger hört (k) | Code | `SpracheHoch`, `SpracheRunter` |
| Opus | m | m | Code | |
| Tag | 16 | 16 | Code | `Sprachkanal.TagLaenge` |
| **UDP-Nutzlast** | **41 + 2n + m** | **45 + 2k + m** | Code | |
| IPv4 und UDP | + 28 | + 28 | Code | `Ausgang.Senden` (IPv6: + 48) |
| Zuschlag für PPPoE und Ethernet | + 26 | + 26 | Code | `grenzen.leitungsZuschlag` |

Hoch trägt der feste Teil Frame-Nummer, Sprechphase, Kennzeichen, Zielart und die Zahl der Ziele. Runter kommt der
Absender dazu, den allein der Server einträgt (Stempel). Bei der Durchsage an alle ist n = 0 und k = 0.

Mit dem echten Code von 0.4.0 nachgemessen (`SpracheHoch.Schreiben`, `SpracheRunter.Schreiben`,
`Datagramm.Laenge`; Messhilfe in D14.3):

| Opus m (Byte) | hoch, 1 Zielnetz | runter, k = 1 | runter, Durchsage an alle (k = 0) | runter, k = 2 |
| ---: | ---: | ---: | ---: | ---: |
| 103 | 146 | 150 | 148 | 152 |
| 120 | 163 | 167 | 165 | 169 |
| 160 | 203 | 207 | 205 | 209 |
| 400 | 443 | 447 | 445 | 449 |
| ENDE ohne Opus | - | 47 | 45 | 49 |

Daraus je Strom, also je Sprecher und Hörer, bei 50 Paketen je Sekunde (gerechnet):

| Richtung, Opus | UDP-Nutzlast | auf IP-Ebene (+ 28) | auf der Leitung (+ 28 + 26) |
| --- | ---: | ---: | ---: |
| runter, m = 120, k = 1 | 66,8 kbit/s | 78,0 kbit/s | **88,4 kbit/s** |
| runter, m = 160, k = 1 | 82,8 kbit/s | 94,0 kbit/s | **104,4 kbit/s** |
| hoch, m = 120, n = 1 | 65,2 kbit/s | 76,4 kbit/s | **86,8 kbit/s** |
| hoch, m = 160, n = 1 | 81,2 kbit/s | 92,4 kbit/s | **102,8 kbit/s** |

Der Lasttest bestätigt die Formel: Bei 160 Byte Opus und Durchsage an alle (205 Byte) maß er 82 kbit/s UDP-Nutzlast
je Empfänger (Szenario C, gemessen 29.09.2026); gerechnet sind es 205 x 50 x 8 = 82,0 kbit/s.

Dieser Teil rechnet überall mit der Spalte "auf der Leitung", auch für die Clients. So zählt auch der Server
selbst, wenn er sein Budget einteilt. Für einen Client ohne PPPoE ist das etwas zu viel; die Tabellen liegen damit
auf der sicheren Seite.

### D3.3 Pakete je Sekunde und was sonst noch mitläuft

| Was | Menge | Kennzeichen | Fundstelle |
| --- | --- | --- | --- |
| Sprache hoch | 50 Pakete/s je Sprecher | Code | 20-ms-Rahmen |
| Sprache runter | 50 Pakete/s je Strom | Code | |
| ENDE am Phasenende | 3 Pakete im Abstand von 20 ms, ohne Opus; der Server reicht jede Kopie an die Hörer der Phase weiter | Code | `SprechphasenGeber.EndeWiederholungen`, `Weiterleiter.Verteilen` |
| Vorlauf (nur Sprachaktivierung) | bis zu 7 Rahmen (140 ms) auf einmal am Phasenbeginn | Code | `SprechphasenGeber.MaxPreroll` |
| Grenze je Sender am Server | 60 Pakete/s (Stoß 20) und 16.000 Byte Opus je Sekunde (Stoß 4.000) | Code | `Fristen.PaketeJeSekunde`, `Fristen.OpusBytesJeSekunde` |

Was über die Grenze je Sender hinausgeht, verwirft der Server und meldet `RATE` an alle mit dem Recht `ZUWEISEN`
(`Weiterleiter.Verarbeiten`, `RelaisBruecke.Rate`, Code). Der Vorlauf kommt nur bei Sprachaktivierung: Das Gate
schickt beim Öffnen die Rahmen ab 30 ms vor dem Anlauf hinterher, damit der Anlaut nicht fehlt (`SprachGate`,
`VorlaufMs = 30`, Code).

### D3.4 Keepalive und Steuerkanal

Neben der Sprache laufen zwei Dinge ständig mit: das Keepalive auf dem UDP-Weg und der Steuerkanal über TLS auf
TCP 8443.

| Was | Richtung | Takt | Größe | Kennzeichen |
| --- | --- | --- | --- | --- |
| KEEPALIVE | Client zum Server | alle 2 s | 48 Byte Datagramm | Code (`Sprachweg.KeepaliveAbstandMs`, `Sprachkanal.FestesDatagramm`) |
| KEEPALIVE_ECHO | Server zum Client | auf jedes Keepalive | 48 Byte Datagramm | Code (`Relais.Verarbeiten`) |
| Ping | Client zum Server | alle 2 s ab Hello | Rahmen 20 bis 29 Byte | Code (`Grenzen.PingAbstandMs`), Größe gemessen |
| Ping-Antwort mit UDP-Zählern | Server zum Client | auf jeden Ping | Rahmen 84 bis 97 Byte | Größe gemessen |
| EmpfangsBericht | Client zum Server | alle 5 s | Rahmen 208 bis 244 Byte | Code (`FunkClientOptionen.BerichtAbstand`), Größe gemessen |
| DurchsageStart, DurchsageEnde | Server zum Client | je Durchsage- oder Global-Phase und Hörer | Rahmen 74 und 47 Byte | Größe gemessen |
| Sprechanzeige | Server zur Führung mit Abo | nur bei Änderung, höchstens alle 200 ms | Rahmen 24 (leer) bis 233 Byte (5 Sprecher) | Code (`Relais.SprechanzeigeTakt`), Größe gemessen |
| neue Sprachschlüssel (VoiceKeys) | Server zum Client | 60 min nach Epochenbeginn oder ab 2^20 Paketen in einer Richtung | selten | Code (`Rotationsgrenzen`) |

Ein Steuerrahmen besteht aus 12 Byte Kopf und dem JSON-Body (`Rahmen.KopfLaenge`, Code). Die Größen in der Tabelle
sind mit dem Serialisierer von 0.4.0 gemessen (`SteuerJson.Serialisieren`), mit kleinen und mit großen Zahlenwerten,
daher die Spannen. Für TLS, TCP und IP rechnet dieser Teil 60 Byte je Rahmen; das ist dieselbe Pauschale, mit der
die Spezifikation in Protokoll 7.4 Tunnelrahmen zählt (Annahme). Reine TCP-Bestätigungen sind nicht mitgezählt.

Grundlast je Client, ohne dass jemand spricht (gerechnet, Keepalive mit Zuschlag 26 Byte):

| Richtung | Anteile | Summe |
| --- | --- | ---: |
| Client zum Server | Keepalive 0,41 + Ping 0,36 + EmpfangsBericht 0,49 kbit/s | 1,25 kbit/s |
| Server zum Client | Keepalive-Echo 0,41 + Ping-Antwort 0,63 kbit/s | 1,04 kbit/s |

Am Server sind das bei 20 Leuten 25 kbit/s herein und 21 kbit/s hinaus, bei 40 Leuten 50 und 41 kbit/s (gerechnet).
Neben einem einzigen Sprachstrom (88 kbit/s) fällt das kaum ins Gewicht. Die Sprechanzeige kostet einen Abonnenten
der Führung höchstens 11,7 kbit/s, und das nur, wenn sich fünfmal je Sekunde etwas ändert (gerechnet).

Unregelmäßig kommen Anmeldung, Aufstellungen und Lagebilder dazu. Ihre Größe hängt an der Flotte; was seit 0.5.0 die
Phasen dazugeben, steht in D3.5. Was ein Client
dem Server schicken darf, begrenzt der Server auf 30 Rahmen und 256 KiB je Sekunde und Sitzung, die Tunnel-Sprache
nicht mitgezählt (`Ratengrenzen.RahmenGesamt`, `Ratengrenzen.BytesGesamt`, Code). Das ist eine Obergrenze gegen
Missbrauch, nicht der Normalbetrieb.

### D3.5 Was 0.5.0 am Steuerkanal ändert

Die Einsatzphasen (Teil C, C13) bringen eine neue Nachricht in den Betrieb und vergrößern eine bestehende. Beides ist klein
gegen die Sprache, aber es ist neu und deshalb hier ausgerechnet. Die Kennzeichen sagen, was Code, Messung oder Rechnung ist.

| Was | Richtung | Wann | Größe | Kennzeichen |
| --- | --- | --- | --- | --- |
| PhaseGeschaltet (`0x4004`) | Server zu allen angemeldeten Sitzungen | bei jedem Phasenwechsel und einmal nach `Welcome`, sobald die Flotte Phasen hat | Rahmen 80 Byte mit einem Namen wie "Entern"; höchstens 227 Byte (Name 24 und Kurzname 12 Zeichen aus lauter Vier-Byte-Zeichen, die größten Zahlenwerte) | gerechnet aus den Feldern und Grenzen (`PhaseGeschaltet`, `Grenzen.PhaseNameMax`, `Grenzen.PhaseKurzMax`), Rahmenkopf 12 Byte |
| PhaseSchalten (`0x003D`) | Client zum Server | auf Tastendruck oder Klick der Einsatzleitung, höchstens 1 je 2 s (Stoß 2) | Rahmen 33 Byte (`{"nr":2,"erwartet":1}` plus Kopf) | gerechnet; Bremse Code (`Steuerbremse`) |
| Funkermodus (`0x003C`) | Client zum Server | einmal je Ein- oder Ausschalten, höchstens 1 je Sekunde (Stoß 2) | Rahmen 23 Byte (`{"an":true}` plus Kopf) | gerechnet; Bremse Code (`Steuerbremse`) |
| Mitgliedschaften | Server zu jedem umgestellten Konto | bei einem Phasenwechsel, vor `PhaseGeschaltet` | hängt an den Feldern des Kontos; nicht gemessen | - |
| NetzListe | Server zu allen | bei jeder Verwaltungsänderung, auch beim Anlegen, Ändern und Löschen einer Phase; ein Phasenwechsel selbst ändert sie nicht (Protokoll 6.14) | ungünstigster Fall 323 KB (Stufe 2 ohne Phasen 257 KB, die Phasen legen 66 KB dazu), typisch 20 bis 40 KB | gemessen am 01.10.2026 (Protokoll 4.10, Standzeile 20:15), Faktor 3,25 unter der Rahmengrenze von 1 MiB |

Zur NetzListe, gerechnet auf 40 Leute: Der ungünstigste Fall sind 40 mal 323 KB, also rund 12,9 MB oder 103 Mbit; bei den 35 Mbit/s
des Ausgangsbudgets daheim dauert das knapp 3 Sekunden, wenn nichts anderes den Ausgang belegt. Der typische Fall mit
40 KB sind 1,6 MB oder 13 Mbit und gut 0,4 Sekunden. Der Fall tritt bei Änderungen an der Flotte ein (Verwaltung, Sperre der Selbstwahl), nicht bei
einem Phasenwechsel im Raid. Die NetzListe geht über TCP und zählt nicht ins Budget des Pacers (D4.2); ob sie die Sprache
während dieser Sekunden stört, ist nicht gemessen. Die Phasenwechsel selbst, also `PhaseGeschaltet` plus je ein
`Mitgliedschaften` für jedes umgestellte Konto, sind für 40 Leute nicht gemessen (Standzeile 01.10.2026 23:05, "Netzlast eines
Phasenwechsels bei 40 Leuten nicht gemessen"). Nur der Push allein ist überschaubar: 40 mal 287 Byte mit der Pauschale von 60
Byte für TLS, TCP und IP sind im ungünstigsten Fall 11,5 KB, mit einem Namen wie "Entern" 40 mal 140 Byte, 5,6 KB (gerechnet,
Pauschale ist Annahme).

## D4 Wie der Server weiterleitet

Der Server dekodiert keine Sprache. Ein einziger Thread, das Relais, nimmt jedes Datagramm, prüft Länge, Sitzung,
Zähler und Tag, bestimmt die Empfänger, stempelt Absender, Art und Netze ein und versiegelt das Paket für jeden
Empfänger neu mit dessen Schlüssel (`Relais`, `Weiterleiter`, `SpracheRunter.Stempeln`, Code). Daraus folgen drei
Dinge, die die ganze Rechnung tragen:

- Jeder Empfänger bekommt von einem Sprecher je Rahmen höchstens ein Paket, auch wenn er ihn in mehreren Netzen
  hört. Die Netze stehen im Paket, 2 Byte je Netz (`Weiterleiter.Verteilen`, `Schnitt`, Code).
- Die Arbeit des Servers wächst mit der Zahl der Ströme, also Sprecher mal Hörer: Jeder Strom kostet ein Versiegeln
  und einen Sendeaufruf.
- Der Sprecher bekommt sein eigenes Paket nicht zurück (`Verteilen`: `if (r == si) continue;`, Code).

Wer ein Paket bekommt:

| Art des Spruchs | Empfänger | Recht des Sprechers | Fundstelle (Code) |
| --- | --- | --- | --- |
| Netz | jede andere Sitzung, die Mitglied des Netzes ist und es hört | Senderecht im Netz | `Schnitt(..., hoerenNoetig: true, ...)` |
| Mehrfachsenden, bis 16 Netze | wer eines der Netze hört; je Empfänger ein Paket mit allen Treffern | `MEHRFACH_SENDEN` (Führung) | `Sprachkanal.NetzZieleMax`, `Verteilen` |
| Durchsage an Netze, bis 64 | jedes Mitglied dieser Netze, auch wer sie gerade nicht hört | `DURCHSAGE` (Führung) | `Schnitt(..., hoerenNoetig: false, ...)` |
| Durchsage an alle | jede andere aktive Sitzung | `DURCHSAGE_ALLE` (Führung) | `anAlle` in `Verteilen` |
| Global (Netz-ID 1) | jede andere aktive Sitzung, ohne Mitgliedschaft | `GLOBAL_SENDEN` (Führung) | `Schnitt`: Global zählt für jeden |

Zugestellt wird nur an Sitzungen, die aktiv sind und deren UDP-Adresse gebunden ist oder die im Tunnel laufen
(`Relais.Zustellbar`, Code).

### D4.1 Der Pacer

Hinter dem Relais sitzt ein Sende-Thread mit einem Token-Topf, der Pacer (`Ausgang`, Code):

- Er zählt Leitungsbytes: UDP-Nutzlast plus 28 Byte (IPv4) oder 48 Byte (IPv6) plus `grenzen.leitungsZuschlag`.
- Er lässt höchstens `grenzen.ausgangMbit` durch, Voreinstellung 35 Mbit/s. Ein Stoß darf 5 ms Leitungszeit lang
  sein; bei 35 Mbit/s sind das 21.875 Byte.
- Er sendet in drei Rängen: zuerst Pfadprüfungen und Keepalive-Echos, dann Global und Durchsagen, zuletzt Sprache
  in Netzen.
- Ein Paket, das länger als 20 ms wartet, verwirft er, statt es verspätet zu senden.

Warum es ihn gibt: Daheim staut sich zu viel Verkehr in der Upload-Warteschlange von Router und DSL-Modem, nicht im
Server. Ohne Pacer gäbe der Server alles sofort an die Netzwerkkarte, und seine Rangfolge bliebe wirkungslos
(Protokoll 7.4).

Überlast stellt das Relais einmal je Sekunde fest (`Relais.LastAuswerten`, Code):

- Stufe 1: mittlere Wartezeit im Pacer über 5 ms oder Pakete, die zu alt wurden. In 0.4.0 und 0.5.0 geht dann nur die Meldung
  `ServerLast` an alle mit `ZUWEISEN`. Der gezielte Abwurf ganzer Netz-Ströme, den Protokoll 7.4 vorsieht, ist noch nicht
  gebaut; bis dahin trifft der Verwurf die Netz-Sprache ohne Auswahl.
- Stufe 2: Median der Verarbeitungszeit über 10 ms. Dann nimmt der Server keine neuen Registrierungen und keine
  unfreigegebenen Verbindungen an; freigegebene Geräte kommen weiter herein.

Was ohne Strom-Abwurf passiert, zeigt der Lasttest mit Budget 35 Mbit/s und 40 Sprechern, die alle hören: 78,5 %
Verlust, und zwar ungleich verteilt; 33 Hörer verloren 73 bis 81 %, 7 Hörer 94 bis 99 % (gemessen 29.09.2026,
`LASTTEST.md`, Ergänzungslauf).

### D4.2 Der Tunnel

Kommt UDP nicht durch, läuft die Sprache als `SpracheTunnel` über die TLS-Verbindung. Runter geht sie dann nicht
durch den Pacer, sondern in die Schreibschleife der Verbindung (`Relais.SendenRunter`, `Verbindung.TunnelSenden`,
Code). Dort gehen Steuerrahmen vor, ein Tunnelrahmen darf höchstens 60 ms alt werden, und je Absender warten
höchstens 3 (`Sendewarteschlange`, Code). Das weicht von Protokoll 7.4 ab, wo Tunnelrahmen im Pacer mitzählen sollen: In
0.4.0 und 0.5.0 (Code unverändert) sieht das Budget Tunnelverkehr nicht.

Ein Tunnelrahmen ist 12 Byte Rahmenkopf plus das Datagramm; mit der Pauschale von 60 Byte sind das bei 120 Byte Opus
239 Byte, also 95,6 kbit/s je Strom (gerechnet, Pauschale ist Annahme).

## D5 Annahmen für das Rechenbeispiel

Wie viele Leute in einem Raid gleichzeitig sprechen und wer wen hört, ist nicht gemessen; zum Raidabend im Kleinen
aus dem Prüfplan (P-50) gibt es noch keinen Bericht. Deshalb rechnet dieser Teil mit offen genannten Annahmen.

| Nr | Annahme | Woher |
| --- | --- | --- |
| N1 | 20 oder 40 Leute sind online. | NEUBAU.md: "5 bis 40 Leute gleichzeitig auf dem Server" |
| N2 | 1, 3 oder 5 sprechen gleichzeitig, im Mittel über die betrachtete Zeit. | Annahme |
| N3 | Netzaufbau wie in der alten Startvorlage (bis 0.12.0): ein Netz für die ganze Flotte (damals Command), je Schiff Durchsagen, Crew und Brücke, dazu Global. Die heutige Vorlage Star Citizen hat denselben Kern: Kommando und Flotte für alle, Brücke und Crew mit einem Ch. je Schiff. | Code bis 0.12.0 (alte Startvorlage, mit dem Umbau entfallen); heute `Flottenvorlagen.StarCitizen` |
| N4 | 5 Leute je Schiff, also 4 Hörer je Schiffsnetz. | Annahme |
| N5 | Normalfall: Ein Sprecher wird von allen gehört (Kommando, Global oder Durchsage an alle); die übrigen sprechen in Schiffsnetzen verschiedener Schiffe. Opus 120 Byte. | Annahme |
| N6 | Schlechter Fall: Jeder Sprecher wird von allen gehört. Opus 160 Byte. | Annahme, obere Schranke |
| N7 | Ein Client hört im Normalfall 1 Sprecher (bei S = 1) oder 2 (Kommando und das eigene Schiff); im schlechten Fall alle S. | folgt aus N4 bis N6 |
| N8 | IPv4, Sprache über UDP, kein Tunnel, Zuschlag 26 Byte auch für Clients. | IPv6 kommt daheim erst nach Klärung der Router-Freigabe (BETRIEB.md) |

Hast du andere Zahlen, etwa 10 Leute je Schiff, setz sie in die Formeln ein. Faustregel (gerechnet): 10 Ströme
kosten 0,88 Mbit/s im Normalfall und 1,04 Mbit/s im schlechten Fall.

## D6 Formeln

```
f  = 50                      Pakete je Sekunde und Sprecher (20-ms-Rahmen)
m  = Opus-Byte je Rahmen     Normalfall 120, schlechter Fall 160
n  = Zielnetze im Paket hoch, k = Netze im Paket runter (meist 1)

Lr = 45 + 2*k + m + 28 + 26  Leitungsbytes je Paket runter (IPv4, mit Zuschlag)
Lh = 41 + 2*n + m + 28 + 26  Leitungsbytes je Paket hoch
rS = f * Lr * 8              bit/s je Strom runter: 88400 (m = 120), 104400 (m = 160)
rH = f * Lh * 8              bit/s je Sprecher hoch: 86800 (m = 120), 102800 (m = 160)

Ströme           = Summe über alle Sprecher: Zahl seiner Hörer
Server-Upload    = Ströme * rS
Server-Download  = Sprecher * rH
Client-Download  = gehörte Sprecher * rS
Client-Upload    = rH, solange er spricht
Pakete/s raus    = Ströme * 50
Pakete/s herein  = Sprecher * 50

Normalfall:        Ströme = (N - 1) + (S - 1) * 4
Schlechter Fall:   Ströme = S * (N - 1)
```

Dazu kommt immer die Grundlast aus Keepalive und Steuerkanal (vorige Tabelle).

## D7 Server: Upload und Download

### D7.1 20 Leute

| Sprecher | Normalfall: Ströme | Upload | Pakete/s raus | schlechter Fall: Ströme | Upload | Pakete/s raus |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 19 | 1,68 Mbit/s | 950 | 19 | 1,98 Mbit/s | 950 |
| 3 | 27 | 2,39 Mbit/s | 1.350 | 57 | 5,95 Mbit/s | 2.850 |
| 5 | 35 | 3,09 Mbit/s | 1.750 | 95 | 9,92 Mbit/s | 4.750 |

### D7.2 40 Leute

| Sprecher | Normalfall: Ströme | Upload | Pakete/s raus | schlechter Fall: Ströme | Upload | Pakete/s raus |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 39 | 3,45 Mbit/s | 1.950 | 39 | 4,07 Mbit/s | 1.950 |
| 3 | 47 | 4,15 Mbit/s | 2.350 | 117 | 12,21 Mbit/s | 5.850 |
| 5 | 55 | 4,86 Mbit/s | 2.750 | 195 | 20,36 Mbit/s | 9.750 |

Alle Werte gerechnet. Dazu kommen 21 kbit/s (20 Leute) bzw. 41 kbit/s (40 Leute) Grundlast und je Client ein
Keepalive-Echo alle 2 s, also 10 bzw. 20 Pakete je Sekunde.

### D7.3 Download des Servers

Was der Server herunterlädt, hängt nur an den Sprechern, nicht daran, wie viele zuhören:

| Sprecher | Normalfall | schlechter Fall | Pakete/s herein |
| ---: | ---: | ---: | ---: |
| 1 | 0,09 Mbit/s | 0,10 Mbit/s | 50 |
| 3 | 0,26 Mbit/s | 0,31 Mbit/s | 150 |
| 5 | 0,43 Mbit/s | 0,51 Mbit/s | 250 |

Dazu die Grundlast von 25 kbit/s (20 Leute) bzw. 50 kbit/s (40 Leute) und je Client ein Keepalive alle 2 s.

### D7.4 Abgleich mit dem Lasttest

Die Formel trifft die Bytes, die der Lasttest am Server gezählt hat (Opus 160 Byte, gemessen 29.09.2026, Upload in
`LASTTEST.md` aus den gemessenen Bytes hochgerechnet):

| Szenario | Ströme | Lasttest: Upload | Lasttest: Pakete/s raus | Formel: Upload | Formel: Pakete/s |
| --- | ---: | ---: | ---: | ---: | ---: |
| C: 1 Sprecher, Durchsage an 39 | 39 | 4,04 Mbit/s | 1.951 | 4,04 Mbit/s (k = 0) | 1.950 |
| A: 5 Sprecher, je 12 Hörer | 60 | 6,27 Mbit/s | 3.002 | 6,26 Mbit/s | 3.000 |
| B: 12 Sprecher, je 15 Hörer | 180 | 18,80 Mbit/s | 9.006 | 18,79 Mbit/s | 9.000 |

## D8 Je Client

### D8.1 Download

Der Download eines Clients hängt nur daran, wie viele Sprecher er gleichzeitig hört. Ob 20 oder 40 Leute online
sind, ändert daran nichts.

| Sprecher gesamt | Normalfall: hört | Download | schlechter Fall: hört | Download | Pakete/s herein |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 1 | 88 kbit/s | 1 | 104 kbit/s | 50 |
| 3 | 2 | 177 kbit/s | 3 | 313 kbit/s | 100 bis 150 |
| 5 | 2 | 177 kbit/s | 5 | 522 kbit/s | 100 bis 250 |

Alle Werte gerechnet, dazu 1,04 kbit/s Grundlast.

Die obere Grenze ist ein Hörer, der 39 gleichzeitige Sprecher hört: 3,45 Mbit/s (Opus 120 Byte) bis 4,07 Mbit/s
(Opus 160 Byte), 1.950 Pakete je Sekunde (gerechnet). Der Lasttest kam in Szenario D auf 1.951 Pakete je Sekunde und
4,1 Mbit/s je Hörer (gemessen bzw. aus den gemessenen Bytes hochgerechnet).

### D8.2 Upload

| Lage | Upload | Kennzeichen |
| --- | ---: | --- |
| spricht, Opus 120 Byte | 86,8 kbit/s + 1,25 kbit/s Grundlast | gerechnet |
| spricht, Opus 160 Byte | 102,8 kbit/s + 1,25 kbit/s Grundlast | gerechnet |
| spricht nicht | 1,25 kbit/s | gerechnet |
| Vorlauf bei Sprachaktivierung | bis zu 7 Pakete auf einmal, bei 120 Byte Opus 7 x 217 = 1.519 Byte | gerechnet |

Wer in mehrere Netze zugleich sendet (Mehrfachsenden), schickt trotzdem nur ein Paket je Rahmen; jedes weitere
Zielnetz kostet 2 Byte (`SpracheHoch`, Code). Jeder heutige DSL-, Kabel- oder Glasfaseranschluss trägt das.

## D9 Grenzfälle

| Fall | Bedarf | Kennzeichen |
| --- | --- | --- |
| 20 Leute sprechen alle zugleich zu allen | 380 Ströme, 33,6 Mbit/s (120 Byte) bis 39,7 Mbit/s (160 Byte), 19.000 Pakete/s | gerechnet |
| 40 Leute sprechen alle zugleich zu allen | 1.560 Ströme, 137,9 bis 162,9 Mbit/s, 78.000 Pakete/s | gerechnet; Lasttest D: 162,97 Mbit/s aus gemessenen Bytes |
| Ein Störer schöpft die Grenze je Sender aus | je Strom bis 176,5 kbit/s; bei 39 Hörern 6,88 Mbit/s | gerechnet; Protokoll 7.4 nennt "etwa 6-7 Mbit/s" |
| Vorlauf-Stoß, 39 Hörer | 273 Pakete auf einmal, 60.333 Byte (120 Byte Opus) bzw. 71.253 Byte (160 Byte); bei 35 Mbit/s 13,8 bzw. 16,3 ms Sendezeit | gerechnet, nicht gemessen |
| IPv6 statt IPv4 | 20 Byte mehr je Paket, etwa + 8 % | gerechnet in `LASTTEST.md` |
| Tunnel statt UDP | 95,6 kbit/s je Strom (120 Byte Opus), nicht im Budget des Pacers | gerechnet, Pauschale Annahme |

Zum Vorlauf-Stoß: Ein einzelner passt unter das Höchstalter von 20 ms im Pacer, wenn sonst nichts wartet. Öffnen
zwei Sprecher mit je 39 Hörern im selben Augenblick per Sprachaktivierung, reicht die Zeit rechnerisch nicht mehr,
und die letzten Netz-Pakete fallen heraus. Global und Durchsagen gehen im Pacer vor. Gemessen ist dieser Fall nicht.

## D10 Vergleich mit dem Heimanschluss

Als Beispiel dient ein Heimserver in einem Proxmox-LXC auf einem Kleinrechner hinter einer FRITZ!Box, erreichbar auf
TCP 8443 und UDP 47443, an einem DSL-Anschluss 300/50 mit echter, aber wechselnder IPv4. Der
Upload des Anschlusses trägt den Upload des Servers.

| Wert | Größe | Kennzeichen |
| --- | --- | --- |
| Upload brutto | 50 Mbit/s | NEUBAU.md, Protokoll 1.4 |
| Netto-Upload | noch nicht gemessen | offen (BETRIEB.md, Protokoll Offene Punkte) |
| Regel für das Budget | höchstens 80 % des gemessenen Netto-Uploads | BETRIEB.md, Protokoll 7.4 |
| Budget heute | 35 Mbit/s (`grenzen.ausgangMbit`), 70 % von 50 brutto | Code, `server.beispiel.json` |
| Ströme im Budget | 395 (Opus 120 Byte) bzw. 335 (Opus 160 Byte) | gerechnet; `LASTTEST.md` nennt dieselben Zahlen |

Bedarf gegen Budget (gerechnet):

| Sprecher | Fall | 20 Leute | Anteil an 35 | Anteil an 50 | 40 Leute | Anteil an 35 | Anteil an 50 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Normalfall | 1,68 Mbit/s | 5 % | 3 % | 3,45 Mbit/s | 10 % | 7 % |
| 1 | schlechter Fall | 1,98 Mbit/s | 6 % | 4 % | 4,07 Mbit/s | 12 % | 8 % |
| 3 | Normalfall | 2,39 Mbit/s | 7 % | 5 % | 4,15 Mbit/s | 12 % | 8 % |
| 3 | schlechter Fall | 5,95 Mbit/s | 17 % | 12 % | 12,21 Mbit/s | 35 % | 24 % |
| 5 | Normalfall | 3,09 Mbit/s | 9 % | 6 % | 4,86 Mbit/s | 14 % | 10 % |
| 5 | schlechter Fall | 9,92 Mbit/s | 28 % | 20 % | 20,36 Mbit/s | 58 % | 41 % |

Wo die Grenze liegt (gerechnet): Ein Sprecher, den alle hören, kostet bei 40 Leuten 39 Ströme. In 395 bzw. 335
Ströme passen davon 10 (Normalfall) bzw. 8 (schlechter Fall) gleichzeitig. Bei 20 Leuten sind es 20 bzw. 17; mit
120 Byte Opus könnten dort sogar alle 20 zugleich zu allen sprechen (33,6 Mbit/s).

Was gemessen ist: Szenario B (12 Sprecher, je 15 Hörer, 180 Ströme) lief mit dem Heimbudget von 35 Mbit/s ohne
Verlust; Szenario D (alle 40 zu allen) verlor 78,5 % (LASTTEST.md, Ergänzungslauf, gemessen 29.09.2026 auf
Loopback, ohne echte DSL-Strecke).

Worauf es daheim ankommt:

- Die Leitung teilt sich der Funk mit allem anderen im Haus, auch mit nginx und Foundry, denen dort Port 443 gehört
  (PROJEKT.md). Der Pacer sieht diesen Verkehr nicht; deshalb die Regel mit 80 %.
- In der FRITZ!Box den LXC als Echtzeitanwendung priorisieren und die Zwangstrennung auf eine Uhrzeit ohne Einsätze
  legen (BETRIEB.md, Protokoll 7.6). Wie gut das unter vollem Upload wirkt, soll P-26 zeigen; dazu gibt es
  noch keine Messung.
- Der Download des Anschlusses (300 Mbit/s) spielt keine Rolle: Der Server lädt höchstens rund 0,5 Mbit/s plus
  Grundlast herunter.

Ist der Netto-Upload gemessen, setzt du das Budget in `/etc/funk/server.json` auf höchstens 80 % davon. Im
Container:

```
nano /etc/funk/server.json
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen && systemctl restart funk-server
```

In der Datei ändert sich nur eine Zahl, hier mit einem Beispielwert für einen gemessenen Netto-Upload von 45 Mbit/s
(Annahme, 80 % davon sind 36):

```
"grenzen": {
  "ausgangMbit": 36,
  ...
}
```

`pruefen` liest Konfiguration und Profil und meldet Fehler; nur wenn es nichts findet, startet der Dienst neu (so steht
der Befehl auch in `UMZUG.md`). Zwei Stolperstellen aus dem Code: Nach dem letzten Eintrag eines Abschnitts darf kein
Komma stehen, und unbekannte Feldnamen lehnt der Server ab (`ServerKonfiguration.Optionen`:
`AllowTrailingCommas = false`, `UnmappedMemberHandling.Disallow`).

## D11 Vergleich mit einem gemieteten Server

Ein Umzug auf einen gemieteten Server ist vorbereitet, erst nach Version 1 und einem Test mit ein paar Leuten (Anf. A55);
welcher Anbieter, ist offen. Die Recherche vom 29.09.2026 (Arbeitsablage, nicht im Repository) nennt unter anderem diese
Angebote; Preise und Verfügbarkeit ändern sich, vor dem Mieten nachsehen:

| Angebot laut Recherche | Port | Traffic |
| --- | --- | --- |
| OVH VPS-1 (Reihe 2027), Limburg | 500 Mbit/s | unbegrenzt |
| netcup VPS 500 G12.5, Nürnberg | 2,5 Gbit/s | Flatrate |
| Hetzner Cloud (CX23 oder CPX22) | je Instanz nicht zugesichert; 300 bis 500 Mbit/s nur aus einer Zweitquelle | 20 TB inklusive |
| Contabo Cloud VPS 4, Scaleway DEV1-S | 200 Mbit/s | unbegrenzt bzw. im Preis |

Was daraus folgt (gerechnet):

- Jeder Fall aus den Tabellen passt auf jeden dieser Ports. Selbst "alle 40 zu allen" (bis 163 Mbit/s) passt auf
  500 Mbit/s; auf 200 Mbit/s bleibt dafür wenig Luft.
- Das Budget von 35 Mbit/s gilt auch dort, bis jemand es ändert. `funk-admin konfig-schreiben` und `einrichten`
  schreiben keinen Abschnitt `grenzen` in die `server.json`, also gelten die Voreinstellungen 35 Mbit/s und 26 Byte
  (`Konfigschreiber.Json`, `GrenzenKonfiguration`, Code). Fehlt der Abschnitt, füge ihn hinzu; Felder, die du nicht
  nennst, behalten ihre Voreinstellung. Eine eigene Regel für Mietserver nennt die Doku nicht. Naheliegend ist
  dieselbe wie daheim: höchstens 80 % dessen, was der Anbieter an Bandbreite tatsächlich liefert (Vorschlag).
- Der Zuschlag von 26 Byte ist für PPPoE und Ethernet gedacht (`GrenzenKonfiguration.LeitungsZuschlag`,
  Protokoll 7.4). Im Rechenzentrum gibt es kein PPPoE; mit 26 rechnet der Pacer dort etwas zu vorsichtig. Das
  schadet nicht.
- Traffic je Abend bei 3 Stunden Funkbetrieb (Annahme: die Sprecherzahl gilt die ganze Zeit, was zu hoch gegriffen
  ist): 40 Leute mit 5 Sprechern 6,6 GB im Normalfall und 27,5 GB im schlechten Fall, 20 Leute mit 5 Sprechern im
  schlechten Fall 13,4 GB. Auch 30 solcher Abende bleiben unter 1 TB, weit unter den 20 TB bei Hetzner.
- Die Rechenzeit ist auf einem VPS mit 2 vCores nicht gemessen. Das Relais ist ein einziger Thread; mehr Kerne helfen
  ihm nicht unmittelbar, ein schneller Kern schon.

## D12 Rechenzeit und Speicher

### D12.1 Server: gemessen im Lasttest

Gemessen am 29.09.2026 mit `funk-testclient` gegen `funk-testserver` auf dem Entwicklungsrechner: AMD Ryzen 9 5950X,
Windows 11, .NET 10.0.12, Release-Build, 40 simulierte Funker, je Szenario 30 s, Opus 160 Byte, Pacer auf 10.000
Mbit/s, damit die Leistung des Servers sichtbar wird (`docs\LASTTEST.md`). "Verteilt" heißt: die Sprecher senden in
10 Gruppen um je 2 ms versetzt, wie es mit echten Audio-Uhren zu erwarten ist; "gleichphasig" ist der schlimmste
Fall, alle im selben Augenblick.

| Szenario | Sprecher / Ströme | Pakete/s raus | CPU verteilt, Mittel / max | CPU gleichphasig, Mittel / max | Arbeitsspeicher / privat, max | Relais p99 max (verteilt) |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| C: Durchsage an alle | 1 / 39 | 1.950 | 3 / 12 % | 5 / 15 % | 100 / 48 MB | 0,90 ms |
| A: 5 Netze | 5 / 60 | 3.000 | 8 / 17 % | 5 / 14 % | 100 / 48 MB | 0,27 ms |
| B: 12 Netze | 12 / 180 | 9.001 | 21 / 31 % | 8 / 12 % | 98 / 46 MB | 10,88 ms (ein Ausreißerfenster) |
| D: alle zu allen | 40 / 1.560 | 78.004 | 74 / 93 % | 60 / 66 % | 94 / 42 MB | 0,18 ms |

CPU in Prozent eines Kerns. Verlust in allen vier Szenarien 0 %. Mit dem Heimbudget von 35 Mbit/s kam B auf 12 / 18 %;
D auf 115 / 124 %, weil der Pacer unter Windows bei kleinen Fehlbeträgen dreht, statt zu schlafen (gemessen,
Ergänzungslauf). Die Anmeldung aller 40 bis zum fertigen UDP-Weg dauerte 4,3 bis 4,6 s, ohne TLS (gemessen).

Einordnung der Rechenbeispiele (keine eigene Messung): Der Normalfall mit 40 Leuten und 5 Sprechern hat 55 Ströme und
2.750 Pakete je Sekunde; das liegt zwischen C und A. Der schlechte Fall mit 195 Strömen und 9.750 Paketen je Sekunde
liegt bei B. Der Speicher lag in allen Szenarien bei rund 100 MB; er hängt kaum an der Last.

Was diese Messung nicht zeigt:

- Sie lief unter Windows auf dem Entwicklungsrechner, nicht unter Linux im LXC eines Heimservers. Die Läufe auf dem LXC (P-30: B eine Stunde lang; P-31: D hinter einem
  Shaper) stehen aus.
- Ohne TLS (Testzugang), ohne Audio (synthetische Rahmen), ohne echte Strecke mit Verlust, Umordnung und Jitter.
- Clients und Server teilten sich denselben Rechner.

Der Container in der Anleitung hat 2 Kerne und 1 GB Arbeitsspeicher; die Anleitung begründet das mit diesem Lasttest
(`HEIMSERVER-PROXMOX.md`, Abschnitt 2). Grenzen in der Konfiguration, die mit der Last zu tun haben: höchstens 100
Sitzungen (`grenzen.maxSitzungen`), 1 MiB UDP-Empfangspuffer (`grenzen.udpEmpfangspuffer`), 4.096 Plätze in der
Warteschlange vor dem Relais (`grenzen.relaisKapazitaet`); alles Code, Voreinstellungen.

### D12.2 Client

Gemessen auf dem Ryzen 9 5950X (`docs\MODULBERICHT-klang.md`; RNNoise und APM im Debug-Testlauf mit Release-Bibliotheken,
Mischer im Release-Build), umgerechnet in Prozent eines Kerns (gerechnet):

| Baustein | Rechenzeit, gemessen | Anteil eines Kerns |
| --- | --- | ---: |
| RNNoise, je 10 ms | Mittel 0,097 ms | 0,97 % |
| APM mit AEC3, Hochpass und AGC2, je 10 ms | Mittel 0,072 ms | 0,72 % |
| Opus kodieren, je 20 ms (nur beim Sprechen) | Mittel 0,21 ms | 1,05 % |
| Opus dekodieren, je 20 ms und gehörtem Sprecher | Mittel 0,11 ms | 0,55 % je Sprecher |
| Mischer mit 16 Quellen, je 10 ms | p50 71 µs | 0,71 % |

Mittelwerte und p99 der Bausteine stehen in E3.12. Ein sprechender Client, der 2 Sprecher hört, kommt damit auf rund 4,6 % eines Kerns, bei 5 gehörten Sprechern auf rund
6,2 % (gerechnet). Star Citizen läuft daneben; wie sich beides unter Last verträgt, ist nicht gemessen.

Nicht in der Tabelle: der Funk-Klick. Er entsteht beim Start und bei jeder Änderung seiner Einstellung neu (alle Klänge im Konstruktor, Code `FunkKlick`), und im Betrieb mischt der Mischer ihn nur ein; die Mischfunktion arbeitet ohne Speicheranforderung (Test `Mischen_MitAnhoerenUndKlicks_AllokiertNicht`). Seine Rechenzeit ist nicht gemessen und dürfte neben den Zeilen oben klein sein (Annahme, nicht gemessen).

## D13 Latenz

Eine Stimme durchläuft Aufnahme, Aufbereitung, Kodierer, Netz, Server, Netz, Jitterpuffer und Ausgabe. Die Bausteine
sind einzeln gemessen oder aus dem Code bekannt; die ganze Kette mit echtem Netz ist noch nicht gemessen.

| Stufe | Wert | Kennzeichen | Quelle |
| --- | --- | --- | --- |
| Aufnahmerahmen | 10 ms | Ziel | Protokoll 7.7 |
| APM (AEC3, Hochpass) | 9,0 ms | gemessen | MODULBERICHT-klang (Kreuzkorrelation) |
| RNNoise | 20 ms | gemessen | MODULBERICHT-klang |
| Opus-Rahmen sammeln | 20 ms | Code | `OpusKodierer.RahmenSamples` |
| Opus-Vorlauf (Lookahead) | 312 Samples = 6,5 ms | gemessen | MODULBERICHT-klang |
| Kodieren, Dekodieren | 0,21 ms, 0,11 ms | gemessen | MODULBERICHT-klang |
| Weg zum Server | halbe Umlaufzeit, Beispiel 10 ms | gerechnet | Ping vom PC von Sarevoc zum Server des früheren Funkprogramms bei OVH: Mittel 19,8 ms (gemessen am 29.09.2026; Arbeitsablage, nicht im Repository) |
| Server: Prüfen, Stempeln, Versiegeln, Senden | p50 0,18 bis 0,73 ms, p99 bis 2,31 ms | gemessen | LASTTEST.md, verteilt, Loopback, mit Client-Bibliothek |
| Warteschlange im Pacer | höchstens 20 ms, sonst verworfen | Code | `Ausgang.HoechstalterTakte` |
| Weg zum Hörer | halbe Umlaufzeit, Beispiel 10 ms | gerechnet | |
| Jitterpuffer | Ziel = 20 ms + 95-%-Quantil der Laufzeitschwankung, mindestens 40 ms (mit FEC), höchstens 200 ms, Startwert 80 ms | Code | `JitterpufferEinstellungen` |
| Mischer | 10-ms-Blöcke | Code | `Mischer.Blocklaenge` |
| Ausgabepuffer (WASAPI) | 30 ms | Code | `KlangMaschinenOptionen.AusgabeLatenzMs` |

Zum Jitterpuffer gibt es eine Simulation mit 30 ms Netzlaufzeit (gemessen, MODULBERICHT-klang; dieselben Läufe mit p95 und Unterläufen in E3.13). Die Verzögerung ist
dort ab dem Senden gemessen, die 30 ms Netz stecken also darin:

| Netz in der Simulation | Ziel des Puffers | Verzögerung ab Senden | davon Puffer (gerechnet) |
| --- | ---: | ---: | ---: |
| gleichmäßig, erste Phase (Startwert) | 80 ms | 93 ms | 63 ms |
| gleichmäßig, ab der zweiten Phase | 40 ms | 53 ms | 23 ms |
| Schwankung gleichverteilt 0 bis 60 ms | 80 ms | 103 ms | 73 ms |
| Bündel alle 100 ms | 105 ms | 133 ms | 103 ms |

Das Ziel übernimmt der Puffer am Anfang jeder Sprechphase, während der Phase nur, wenn es um mehr als 20 ms steigt
(Code). Es gibt einen Puffer je Sprecher; solange er für einen Sprecher noch keine Laufzeiten gesammelt hat, gilt der
Startwert von 80 ms (`Jitterpuffer.ZielBerechnet`, Code).

Überschlag für die Sprechtaste, ruhiges Netz mit 20 ms Umlaufzeit (gerechnet aus den Zeilen oben): 10 (Aufnahme) + 29
(APM und RNNoise) + 20 (Rahmen) + 6,5 (Vorlauf des Kodierers) + 10 (hin) + 1 (Server) + 10 (zurück) + 23 (Puffer ab
der zweiten Phase) + 10 (Mischblock) + 30 (Ausgabepuffer) = rund 150 ms. Das Ziel der Spezifikation ist p95 höchstens
150 ms bei 20 ms Umlaufzeit (Protokoll Z2 und 7.7). Die Stufen sind nicht als Kette gemessen; Überlappungen,
etwa zwischen Rahmen und Puffertiefe, sind möglich. Der Überschlag zeigt die Größenordnung, nicht mehr.

Was bisher von Ende zu Ende gemessen ist, lief am Prüfstand ohne echtes Netz, und die Berichte nennen nicht, welche
Stufen jeweils dabei waren; mit dem Überschlag ist es deshalb nicht direkt vergleichbar:

- Mithören Mund-zu-Ohr: 105 ms mit Rauschunterdrückung, 86 ms ohne (MODULBERICHT-client, 29.09.2026, synthetisch); bei
  der Nachmessung am 01.10.2026 105 und 85 ms (E3.11). Gemessen ist das Mithören beim Senden, das bis 0.4.0
  "Hörprobe" hieß, im Stand 0.4.0; der Bericht kennzeichnet die Zeile seit 0.4.1 so. Mit 0.5.0 ist sie nicht neu gelaufen;
  laut Kommentar im Test liegt der Klick der Taste vor dem Vokaleinsatz, den der Test sucht, der lautere Klick von 0.5.0 (E3.9)
  verschiebt die Zahl also voraussichtlich nicht (am Testcode gelesen, nicht gemessen). Die Hörprobe als Aufnahme von 0.4.1
  spielt erst nach 5 Sekunden ab, eine Latenz misst sie nicht.
- Zwei Clients mit echter Kette über den Testzugang: Mikrofon A bis Ausgang B 50 bis 80 ms (MODULBERICHT-client).

Was die Laufzeit verlängert:

- Schwankung auf der Strecke: Der Puffer wächst mit, in der Simulation bei 0 bis 60 ms Schwankung um 50 ms gegenüber
  dem ruhigen Netz.
- Sprachaktivierung: Das Gate öffnet im Mittel 35 ms (Sprache) bzw. 39 ms (Sprache mit Tastatur) nach dem Anlaut
  (gemessen, MODULBERICHT-klang). Abgeschnitten wurde in dieser Messung kein Anlaut (0 von 68), weil der Vorlauf als
  Stoß hinterherkommt; der Hörer baut den Überhang binnen 1 s auf höchstens Ziel plus 20 ms ab (gemessen in der
  Simulation).
- Last am Server: Wartezeit im Pacer bis 20 ms. Bei allen 40 gleichphasig lag die Laufzeit durch den Server bei p50
  5,9 ms; mit Budget 35 und Überlast bei p50 19,2 ms (gemessen, LASTTEST.md).
- Tunnel: TCP statt UDP, Tunnelrahmen dürfen bis 60 ms in der Schreibschleife warten (Code).

## D14 Selbst nachmessen

### D14.1 Am laufenden Server

Während eines Funkabends zeigt `messwerte`, was der Server gerade leistet. Auf dem Heimserver nach
`HEIMSERVER-PROXMOX.md` im Container:

```
dotnet /opt/funk/funk-admin.dll messwerte --konfig /etc/funk/server.json
```

Auf einem Server, der mit `einrichten` aufgesetzt wurde, gibt es den Kurzbefehl:

```
sudo funk-admin messwerte
```

Die Antwort ist eine Zeile in dieser Form (`Zentrale.MesswerteAsync`, Code):

```
<n> Sitzungen, verarbeitet <x>, verworfen <a>+<b>, Ausgang <pakete> (<mbit> Mbit/s), p50 <ms> ms, p99 <ms> ms, Laststufe <s>
```

- `Ausgang ... Mbit/s` ist die Senderate des Pacers im letzten Messfenster von einer Sekunde, in Leitungsbytes, also
  dieselbe Größe wie der Upload in den Tabellen dieses Teils. Tunnelverkehr ist darin nicht enthalten.
- `p50` und `p99` sind die Verarbeitungszeiten im Relais, `Laststufe` die Überlaststufe 0 bis 2.
- Mit `--json-aus` kommen die Werte je Sitzung dazu: Verlust, Jitter, Umlaufzeit, Tunnelanteil, Güte.

Den Netto-Upload misst das Programm nicht; wie er gemessen wird, legt die Doku nicht fest.

### D14.2 Lasttest auf dem Entwicklungsrechner

Die Läufe aus `LASTTEST.md`, im Repository-Ordner unter Windows. `tools\dotnet-umgebung.ps1` legt dabei die
Zwischenstände von SDK, NuGet und MSBuild in die Arbeitsumgebung des Projekts statt ins Benutzerprofil; das Skript ist fest
auf `C:\Claude` eingestellt. Der Lasttest startet einen eigenen `funk-testserver` auf Loopback und löscht sein
Zustandsverzeichnis am Ende wieder; er öffnet kein Fenster und kein Audiogerät.

```
. tools\dotnet-umgebung.ps1
dotnet build TacRelay.slnx -c Release --disable-build-servers
dotnet run --project tests\Funk.Testclient -c Release --no-build -- last --funker 40 --sekunden 30 --szenarien ABCD --gruppen 10
dotnet run --project tests\Funk.Testclient -c Release --no-build -- last --funker 40 --sekunden 30 --szenarien BD --gruppen 10 --ausgang-mbit 35
```

Mit `--opus-bytes 120` lässt sich derselbe Lauf mit der Paketgröße des Normalfalls wiederholen; die Option steht in
der Hilfe von `funk-testclient`, gelaufen ist das noch nicht. `--aus <datei.json>` schreibt alle Werte je Empfänger,
`--tabelle <datei.md>` die Tabelle.

### D14.3 Paketgrößen

Die Größen der Datagramme und Steuerrahmen in diesem Teil stammen aus einer einmaligen Messhilfe am 01.10.2026, die
die Klassen aus `Funk.Protokoll` von 0.4.0 aufruft (`SpracheHoch.Schreiben`, `SpracheRunter.Schreiben`,
`Datagramm.Laenge`, `SteuerJson.Serialisieren`). Sie gehört nicht zum Repository. Zum Nachrechnen reichen die
Formeln in D6; die Datagrammgrößen ergeben sich daraus genau.

## D15 Was noch nicht gemessen ist

- Ein echter Raidabend (P-50): wie viele gleichzeitig sprechen, wer wen hört, Verlust, Jitter und Umlaufzeit über
  echte Anschlüsse. Bis dahin gelten die Annahmen N2 bis N7.
- Mund-zu-Ohr über echtes Netz mit der Klick-Schleife (P-20, P-50).
- Der Lasttest und die Rechenzeit des Servers mit 0.5.0: Die Messungen stammen aus 0.4.0; der Sprachpfad ist unverändert (oben), aber neu gelaufen ist der Lasttest nicht. Dazu gehört der kleine Zusatz je Sprachpaket für den Funker-Modus (ein Zeitvergleich im Heißpfad, `Schnappschuss.RechteAn`).
- Die Netzlast eines Phasenwechsels bei 40 Leuten (D3.5) und die Rechenzeit des Funk-Klicks im Client (D12.2).
- Das Symbol im Infobereich, die Meldezeile über Star Citizen und das Ablegen und Holen des Funkfelds mit dem Joystick: nur im Code und im Bildlauf geprüft, nicht an einem echten PC (Standzeile 01.10.2026, 22:21).
- Der Netto-Upload des Heimanschlusses und das Verhalten bei vollem Upload aus dem Heimnetz (P-26).
- Rechenzeit und Speicher auf einem LXC daheim und auf einem gemieteten VPS (P-30, P-31).
- Der Lasttest mit Opus 120 Byte und mit TLS statt Testzugang.
- Der gezielte Strom-Abwurf bei Überlast ist in 0.5.0 (wie in 0.4.0) nicht gebaut; der Tunnel zählt nicht ins Budget des Pacers.

## D16 Quellen

Quelltext (Fassung 0.5.0, Stand `95aa78b`; die Zahlen dieses Teils stammen aus 0.4.0, und die genannten Dateien sind bis auf die in der Einleitung aufgezählten Ergänzungen unverändert; Pfade ab `src\`):

- `Funk.Klang\Kern\OpusKodierer.cs`: Abtastrate, Rahmen, Bitrate, VBR, FEC, DTX, Höchstlänge.
- `Funk.Klang\Maschine\KlangMaschinenOptionen.cs`: Bitrate im Client, Ausgabepuffer 30 ms.
- `Funk.Klang\Verarbeitung\Sendeaufbereitung.cs`, `SprachGate.cs`: 10-ms-Rahmen, Vorlauf 30 ms, Nachlauf 300 ms.
- `Funk.Klang\Verarbeitung\Jitterpuffer.cs`, `Mischer.cs`: Start, Minimum, Maximum, Quantil des Jitterpuffers;
  10-ms-Blöcke des Mischers.
- `Funk.Protokoll\Sprache\Sprachkanal.cs`, `SprachKopf.cs`, `SpracheHoch.cs`, `SpracheRunter.cs`, `Datagramm.cs`,
  `Steuerpakete.cs`, `SprechphasenGeber.cs`, `Rotationsgrenzen.cs`: Paketaufbau, Größen, ENDE, Vorlauf, Rotation.
- `Funk.Protokoll\Steuerung\Rahmen.cs`, `Steuerbremse.cs`, `Grenzen.cs`, `Sendewarteschlange.cs`,
  `Nachrichten\Anmeldung.cs`, `Nachrichten\Betrieb.cs`, `Flottenvorlagen.cs`: Steuerrahmen, Ping, EmpfangsBericht,
  Ratengrenzen, Tunnel-Warteschlange, Vorlage Star Citizen (bis 0.12.0 die alte Startvorlage).
- `Funk.ClientKern\Sprachweg.cs`, `Sitzung.cs`, `FunkClientOptionen.cs`: Keepalive, Ping, Bericht.
- `Funk.Server\Sprachpfad\Relais.cs`, `Ausgang.cs`, `RelaisTypen.cs`: Relais, Pacer, Rangfolge, Überlaststufen.
- `Funk.Server\Lage\Weiterleiter.cs`, `Fristen.cs`: Empfänger je Spruchart, Grenzen je Sender.
- `Funk.Server\Kern\RelaisBruecke.cs`, `Zentrale.Verwaltung.cs`, `Funk.Server\Steuer\Verbindung.cs`: Tunnel,
  Durchsagen, Sprechanzeige, `messwerte`.
- `Funk.Server\Betrieb\Konfiguration.cs`, `Funk.Admin\Konfigschreiber.cs`, `Funk.Admin\Einrichtung.cs`: `grenzen`,
  Voreinstellungen, was `konfig-schreiben` und `einrichten` schreiben.
- `Funk.Protokoll\Steuerung\Nachrichten\Phasen.cs`, `Funk.Server\Kern\Zentrale.cs`: PhaseGeschaltet, PhaseSchalten und
  Funkermodus (0.5.0), Verteilung des Pushes (D3.5).
- `Funk.Klang\Verarbeitung\FunkKlick.cs`: Funk-Klick (0.5.0, einstellbar).
- `Funk.Server\Program.cs`, `Funk.Admin\Hilfe.cs`, `tests\Funk.Testclient\Program.cs` (Pfad ab Repository): Aufruf
  und Hilfe von `funk-server`, `funk-admin` und `funk-testclient`.

Dokumente:

- `docs\LASTTEST.md`: Lasttest vom 29.09.2026 mit allen Messwerten zu Server, Verlust, Laufzeit und Upload-Bedarf.
- `docs\MODULBERICHT-klang.md`, `docs\MODULBERICHT-client.md`: Rechenzeiten, Verzögerungen, Jitterpuffer, Gate,
  Mithören (bis 0.4.0 "Hörprobe").
- `docs\FUNKPROTOKOLL-v1.md`: 1.4 (Anschluss), 4.10 (Größen im ungünstigsten Fall, mit der NetzListe von 0.5.0), 6.14 und
  6.15 (Phasen, Funker-Modus), 7.4 (Überlast und Ausgangsbudget), 7.6 (Heimbetrieb), 7.7 (Latenzbudget), Prüfplan P-20 bis P-50.
- `docs\BETRIEB.md`, `docs\HEIMSERVER-PROXMOX.md`, `konfig\server.beispiel.json`: Budget, 80-%-Regel, Container,
  Befehle.
- `PROJEKT.md`: Standzeilen vom 01.10.2026, 20:15 (S2 Server, NetzListe 323 KB) und 23:05 (Gegenprüfung S2, Netzlast eines
  Phasenwechsels nicht gemessen).
- `docs\entwurf\ENTSCHEIDUNGEN.md`: Anforderungen A55 und A57; der Rahmen (5 bis 40 Leute, DSL 300/50, Ping 19,8 ms) aus
  denselben Gesprächen vom 29.09.2026.
- Recherche Mietserver, Stand 29.09.2026 (Arbeitsablage, nicht im Repository).
