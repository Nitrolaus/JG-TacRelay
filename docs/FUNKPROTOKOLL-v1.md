# Funkprotokoll v1

Entwurf 2 vom 29.09.2026, noch nicht abgenommen. Er ersetzt Entwurf 1 und arbeitet die Befunde dreier Prüfberichte ein: Kryptografie und Protokollsicherheit, Netzwerk und Echtzeit, Funkbetrieb und Bedienung. Gilt für den eigenen Client (C#, .NET 10, WPF, Windows) und den eigenen Server (C#, .NET 10, Linux, zuerst Proxmox-LXC hinter Telekom-DSL mit DynDNS, später bei einem Anbieter). Die Grundlagen sind die Anforderungen A1-A23 und der Grundentwurf (Weg A, Raid-Helper), heute in `docs\entwurf\ENTSCHEIDUNGEN.md` (Anhang A und Kapitel "Server und Betrieb"), und die Recherchen server-sicherheit.md, anmeldung.md (schlanke Variante), codec-ausgabe.md, audio-dsp.md, raidhelper.md und architektur-urteil.md (Arbeitsablage, nicht im Repository).

Stufe 1 vom 30.09.2026 ist eingearbeitet (Grundlage: die Entwürfe zu Anmeldung und Sicherheit und zur Raidleitung, Fassung 3, heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Rechte und Verwaltung"): Anmeldung mit SC-Name und Discord-Name ohne Bot (3.4), Admin-Nachweis mit eigenem Admin-Schlüssel und Zweitfaktor nach RFC 6238 (3.9), Eigentümer (3.10), Mitglieder, Rollenkatalog, Untergilden und Mitgliedskarte (6.13). Stufe 1, Teil 2 (Client, Sarevocs Antworten 3 und 4) ist ebenfalls eingearbeitet, dazu Sarevocs Entscheidung A53 vom 30.09.2026 (Discord-Name eindeutig, nur vom Admin änderbar) und die Fassung 0.2.0. Die Liste der Änderungen und Abweichungen steht in FUNKPROTOKOLL-v1-aenderungen.md, Abschnitte *Stufe 1*, *Stufe 1, Teil 2* und *Nachtrag A53 und Fassung 0.2.0*.

Stufe 2, Teil Server und Protokoll (Fassung 0.3.0, 01.10.2026), ist eingearbeitet (Grundlage: der Entwurf zur Raidleitung, Fassung 3, Bauplan S1, und die Anforderungen A36-A40, A43, A46-A50, A53, A54, heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Zuteilung" und Anhang A): Die Stufe RAIDLEITER ("Führung") teilt nicht mehr ein, einteilen darf ein ADMIN oder wer einen Auftrag für einen Abend hat (6.1, 6.10, 6.12, Nachricht 0x0017); Einheiten mit Art (Schiff, Trupp, Orga) und Mindestbesetzung, das Verwaltungspaket für eine ganze Flotte, die Sperre der Selbsteinteilung mit Urheber, Frist und Nachzüglern, fremde Aufstellungen geschützt, die Belegung als Push (0x4003) und das Kontenverzeichnis (0x0038). Clients 0.2.0 bleiben bedienbar (4.7). Abschnitt *Stufe 2, Server und Protokoll (Fassung 0.3.0)* in FUNKPROTOKOLL-v1-aenderungen.md.

Ausbaustufe S2, Teil Server und Protokoll (Fassung 0.5.0, 01.10.2026), ist eingearbeitet (Grundlage: der Entwurf zur Ausbaustufe S2 und die Anforderungen A60 und A61, heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Zuteilung" und Anhang A): Einsatzphasen als Netzbild der Flotte, die die Einsatzleitung zweistufig per Taste oder Joystick schaltet (6.14, Nachrichten 0x003D und 0x4004, `NetzListe.phasen`, `Verwaltung PHASE_SETZEN` und `PHASE_LOESCHEN`), und "Heute als Funker" für ADMIN-Konten (6.15, 0x003C): Solange er läuft, ruhen die Rechte der Einsatzleitung, und die Einsatzleitung teilt den Admin ein wie jeden Funker. Clients 0.4.x bleiben verbunden (4.7). Abschnitt *Ausbaustufe S2, Server und Protokoll (Fassung 0.5.0)* in FUNKPROTOKOLL-v1-aenderungen.md.

Fassung 0.5.0, Vorgaben A62 bis A65 (01.10.2026), ohne Änderung am Draht: Die Konsole legt neue Netze ohne Schalter mit Sprachaktivierung an, Taste nur mit `--taste`, Global immer Taste (4.5); der Funk-Klick ist im Client einstellbar; Beispiele heißen Max Mustermann. Abschnitt *Fassung 0.5.0, Vorgaben A62 bis A65* in FUNKPROTOKOLL-v1-aenderungen.md.

Stufe 3, Schritte 3.0 und 3.1 (Fassung 0.6.0, 03.10.2026), ist eingearbeitet (Grundlage: der Entwurf zu Stufe 3, Variante C, mit Sarevocs Antworten, heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Funkfeld", dazu die Ch.-Listen und Presets der alten Kanalvorlage, mit ihr entfallen): Kanäle wie im früheren Funkprogramm der Gruppe, einem Ableger (Fork) von SRS. Ein Netz kann ein Kanalnetz mit einer Liste von Ch.-Bezeichnungen sein; ein Kanal ist das Paar (Kanalnetz, Ch.) mit einer berechneten Kanal-ID, Ch. 0 heißt "Ruhe" und ist ab Werk eingestellt (6.16). `FeldSetzen` nimmt Kanal-IDs, neu ist `RolleWaehlen` (0x0039, Rolle wie ein Preset des früheren Funkprogramms, der Platz bleibt). Sobald es ein Kanalnetz gibt, lässt der Server nur Clients ab 0.6.0 zu (4.7). Schritt 3.2 baut die Kanalsperren (6.17, 0x003A `KanalSperre`): Die Einsatzleitung oder ein Admin sperrt einen Kanal, ein Kanalnetz oder ein Netz für ein Konto oder für alle ohne `ZUWEISEN`, bis 04:00. Schritt 3.3 bringt eine Kanalvorlage (bis 0.12.0 die alte Kanalvorlage: Global mit Ch., 30 Kanalnetze, 21 Rollen; seit 0.13.0 die Vorlage Star Citizen: 10 Kanalnetze, 14 Rollen, 6.16), den Reiter Kanäle im Verwaltungsteil und `netz-setzen --kanaele` an der Konsole; `nurTaste` von Global gilt seitdem nur für Orgaweit. Abschnitte *Stufe 3, Schritte 3.0 und 3.1 (Fassung 0.6.0)*, *Stufe 3, Schritt 3.2* und *Stufe 3, Schritt 3.3* in FUNKPROTOKOLL-v1-aenderungen.md.

Zur Herkunft der Zahlen: Die RFC-Stellen stammen aus den Recherchen, den Prüfberichten und eigener Kenntnis. Ein Abruf von RFC 9000 über das Zusammenfassungswerkzeug lieferte widersprüchliche Antworten und gilt deshalb nicht als Beleg (Offene Punkte). TV-A bis TV-F in 8.3 sind unverändert. Sie sind mit `System.Security.Cryptography.AesGcm` unter .NET 10.0.12 gerechnet, nachdem das Werkzeug die GCM-Referenzfälle 13 und 14 (McGrew/Viega) bestanden hatte, und ein Prüfer hat sie mit derselben Bibliothek nachgerechnet. Neu gerechnet sind die Steuerrahmen in 8.4 und die Profilsignatur in 8.11, mit PowerShell 7.6.6 unter .NET 10.0.12, nur im Speicher. Eine Gegenrechnung mit einer zweiten, unabhängigen Implementierung steht weiter aus (P-16).

Das Programm heißt seit 19.10.2026 JG TacRelay. Hier heißt es Platzhalter-artig "Funk", das ALPN-Kennzeichen `tacrelay/1`.

## 0. Konventionen und Überblick

- MUSS, DARF NICHT, SOLL, SOLL NICHT und KANN stehen für MUST, MUST NOT, SHOULD, SHOULD NOT und MAY nach RFC 2119.
- Alle Ganzzahlen in Binärstrukturen sind vorzeichenlos und in Netzwerk-Byte-Reihenfolge (big-endian). Die Schreibweise `u8`, `u16`, `u32` und `u64` gibt die Breite an.
- Zeiten im Steuerkanal sind Millisekunden seit 1970-01-01 UTC (`i64`). Wo eine monotone Uhr gemeint ist, steht das ausdrücklich da.
- Hexadezimale Byte-Folgen stehen ohne Trennzeichen, Kleinbuchstaben.
- "hoch" heißt Client -> Server (C->S), "runter" Server -> Client (S->C).
- KiB sind 1024 Byte, MiB 1.048.576 Byte.
- "Adresse" heißt im Sprachkanal IP-Adresse plus Port. "Präfix" heißt bei IPv4 die einzelne Adresse, bei IPv6 das /64. Alle Grenzen "je Quelle" zählen je Präfix. IPv4-Adressen in IPv6-Form (`::ffff:a.b.c.d`) werden vor jedem Vergleich und jeder Zählung auf IPv4 zurückgeführt.

```
Client (Windows, WPF)                         Server (Linux, LXC)
+-----------------------+  TCP, TLS 1.3/1.2    +-----------------------------+
| Steuerkanal           |<-------------------->| Steuerkanal (SslStream)     |
|  Geraetezertifikat    |  ALPN tacrelay/1|  Konten, Geraete, Rechte,   |
|  signiertes Profil    |                      |  Netze, Plaetze, Aufstellung|
|                       |                      |  Ausgabe der UDP-Schluessel |
| Sprachkanal           |  UDP, AES-256-GCM    | Relais (ein Thread):        |
|  1 Strom hoch         |<-------------------->|  pruefen, Senderecht,       |
|  n Stroeme runter     |  (Rueckfall: Tunnel  |  stempeln, neu versiegeln,  |
|  Sende-/Empfangsthread|   im Steuerkanal)    |  Pacer fuer den Ausgang     |
+-----------------------+                      +-----------------------------+
                                                    | UNIX-Sockets (lokal)
                                                 +-----------------------------+
                                                 | Bot-Dienst: Discord-Gateway,|
                                                 |  nur ausgehend, eigener User|
                                                 | Admin-Werkzeug (Kommandozeile)
                                                 +-----------------------------+
```

Nach außen gibt es genau einen TCP-Port und einen UDP-Port. Vorschlag: 443/TCP und 47443/UDP. UDP bewusst nicht auf 443, denn manche Firmen-, Hotel- und Uni-Netze behandeln UDP 443 als QUIC und verwerfen oder sperren es. Beide Ports sind frei konfigurierbar, das Serverprofil trägt den TCP-Port, Welcome den UDP-Port. Die Konfiguration trennt je Port den Bindport im LXC und den öffentlichen Port, eine Weiterleitung wie 443 -> 8443 am Router ist also zulässig. Welcome nennt immer den öffentlichen Port, und der Server protokolliert beim Start beide. Der Server hat keine Web-Oberfläche, keinen HTTP-Endpunkt, keinen Discord-Interactions-Endpunkt und keinen Admin-Port.

## 1. Ziele, Nicht-Ziele, Bedrohungsmodell

### 1.1 Ziele

- Z1 Klang (A1): Opus mit 48 kHz und 20-ms-Frames wird unverändert durchgereicht. Der Server decodiert, mischt und transkodiert nicht.
- Z2 Latenz: Der Server braucht je Paket unter 2 ms (p99) für Prüfung, Stempel und Neuversiegeln. Kein Paket liegt länger als 20 ms in einer Sendewarteschlange des Servers. Von Mund zu Ohr gilt p95 höchstens 150 ms bei 20 ms Netz-RTT und Sprechtaste (Budget in 7.7).
- Z3 Funkmodell im Server (A5-A8, A15, A17, A20, A23): Netze, Hören, Senden, Durchsage, Global, Plätze und Aufstellung sind Serverzustand. Das Senderecht wird je Paket geprüft, das Hören je Empfänger erzwungen.
- Z4 Absender und Netz sind nicht fälschbar (A4, A9, A18): Der Server stempelt je Empfänger Absender-Konto, Art, Netz(e) und den Beginn einer Sprechphase.
- Z5 Sicherheit (A12): Es gibt keine Passwörter, keine gemeinsamen Geheimnisse und keine Inhaber-Token. Jedes Gerät hat einen eigenen, möglichst nicht exportierbaren Schlüssel. Die Sprache ist per AEAD geschützt, Replays werden verworfen. Einzige Ausnahme ist das TOTP-Geheimnis der Admins (3.9): nur für ADMIN-Konten, nur als zweiter Faktor neben dem Admin-Schlüssel und nie allein ausreichend; am Server liegt es nur verschlüsselt.
- Z6 Kleine Angriffsfläche: Auf Unauthentisiertes wird über UDP nie geantwortet. Es gibt keine Verstärkung (Amplification) und keinen öffentlichen Webdienst. Vor der Anmeldung gibt es keine gemeinsame Grenze, über die Unbekannte freigegebene Geräte aussperren können (4.8).
- Z7 Robustheit: Die Verbindung kommt nach einem Adresswechsel des Heimanschlusses, nach einem Neustart oder einer UDP-Sperre in einer oder beiden Richtungen ohne Nutzereingriff wieder. Ein UDP-Ausfall wird in jeder Richtung binnen 3 s erkannt, auch bei einem reinen Hörer.
- Z8 Prüfbarkeit und Messbarkeit: Die Binärformate sind kanonisch und die Prüfungen deterministisch. Die Testvektoren stehen in Abschnitt 8. Server und Client messen Verlust, Jitter, RTT und Pfadwechsel je Sitzung (7.8).

### 1.2 Nicht-Ziele (v1 kann das bewusst nicht)

- Keine Ende-zu-Ende-Verschlüsselung: Der Server sieht die Opus-Nutzlast im Klartext (Hop-by-Hop, wie Mumble).
- Kein Schutz gegen Verkehrsanalyse: Paketgrößen (VBR), Sprechzeiten und wer mit dem Server spricht, bleiben sichtbar (RFC 6562, bewusst hingenommen).
- Kein Schutz gegen einen kompromittierten Server oder Admin und keiner gegen Schadcode auf dem Client-Rechner. Ein TPM verhindert dort nur das Wegkopieren des Schlüssels, nicht seine Benutzung.
- Kein vollständiger Schutz gegen einen Beobachter mit schnellerem Netzweg, der echte Pakete kopiert und zugleich die Originale unterdrückt (5.10). Er kann den Sprachpfad stören, aber weder mitlesen noch einsprechen.
- Keine anderen Clients: kein Linux, kein Android, kein Web, kein Mumble. Keine Föderation und kein Mehrserverbetrieb.
- Kein Text-Chat, keine Dateiübertragung, keine Aufzeichnung, keine Positionsdaten.
- Kein QUIC und kein DTLS (server-sicherheit.md B1-B3).
- Keine Discord-Anmeldung im Client, kein OAuth, kein OpenIddict. Discord dient nur einmal zur Verknüpfung per Einmalcode und danach zum Abgleich der Gildenmitgliedschaft (3.7).
- Keine Freigabe von Geräten über Discord.
- Keine Raid-Helper-Webhooks (brauchen Premium und einen öffentlichen Endpunkt), und es wird nie zu Raid-Helper zurückgeschrieben.
- Je Konto gibt es höchstens eine aktive Sitzung. Eine neue Anmeldung verdrängt die alte (4.6).
- Höchstens 16 Felder je Konto, 16 Ziel-Netze je NETZ-Paket, 64 Ziel-Netze je Durchsage, 128 Netze (davon höchstens 64 Kanalnetze mit je höchstens 31 Ch.-Einträgen, 6.16), 64 Schiffe (je höchstens 16 Plätze und 8 Schiffsnetz-Kennungen), 64 Rollen, 500 Konten, 3 aktive Geräte je Konto und 100 gleichzeitig aktive Sitzungen.
- Das Update-Verfahren (Velopack plus offline signiertes Manifest) ist nicht Teil dieses Protokolls. Das Protokoll erzwingt nur eine Mindestversion.

### 1.3 Schutzgüter

| Nr | Schutzgut | Warum |
| --- | --- | --- |
| G1 | Verfügbarkeit während eines Einsatzes | ein Ausfall mitten in der Schlacht ist der größte reale Schaden |
| G2 | Vertraulichkeit der Sprache, vor allem Kommando | Rivalen sollen nicht mithören |
| G3 | Lagebild: Aufstellung, wer auf welchem Schiff sitzt, wer welche Netze hört | für eine rivalisierende Org wertvoll (Lehre aus dem Mumble-Gastzugang) |
| G4 | Echtheit von Absender und Netz | Pegel je Funker, Anzeige, Befehlskette |
| G5 | Konten und Rechte | Kontoübernahme gibt Zugang zu G2 und G3 |
| G6 | Heimanschluss und IP von Sarevoc | Heimphase: DoS und Privatsphäre |

### 1.4 Angreifer

| Nr | Angreifer | Fähigkeiten | Gegenmaßnahme in v1 | Restrisiko |
| --- | --- | --- | --- | --- |
| T1 | Neugieriger Mitspieler mit gültigem Konto | eigener, eventuell veränderter Client; kennt den Server | Hören nur als Mitglied (serverseitig), Senderecht je Paket, Stempel durch den Server, Praesenz ohne fremde Netze für Funker, Rangregel für ZUWEISEN (6.10) | sieht, was jedes Mitglied sehen darf |
| T2 | Rivalisierende Org ohne Konto | Installer vielleicht von einem Mitglied, kennt Host und Port | nichts ohne freigegebenes Gerät; Registrierung und Warteraum verraten nichts über Netze, Konten und Aufstellung; ein abgeschriebener Einmalcode verschafft keinen Zugang (3.4) | Registrierungen nur gedrosselt, neue Geräte nur nach Admin-Freigabe; DoS siehe T4 |
| T3 | Gelegenheitsangreifer, Scanner | Portscans, TLS-Proben, zufällige Pakete, Fuzzing; erzeugt beliebige selbstsignierte Zertifikate | Das Pflicht-Client-Zertifikat filtert nur einfache Scanner, denn jeder kann ein P-256-Zertifikat erzeugen. Dahinter: ALPN, kurze Fristen (TLS 5 s, Hello 3 s), Grenzen je Präfix, UDP ohne jede Antwort auf Ungültiges, harte Längengrenzen vor jeder Allokation | TLS-Stack sowie Hello-, Registrierungs-, JSON- und Rahmen-Parser sind für jeden erreichbar (Fuzzing Pflicht, P-42) |
| T4 | Störer während eines Einsatzes (DoS) | Handshake-Fluten, Belegen der Plätze für Unbekannte, UDP-Müll, volumetrisch gegen DSL (50 Mbit/s hoch, 300 Mbit/s runter) | Grenzen je Präfix; Vorrangspur für Präfixe mit AKTIV-Sitzung in den letzten 14 Tagen; freigegebene Geräte zählen nie gegen Grenzen für Unbekannte; globale Töpfe nur als CPU-Schutz; Paket- und Byte-Topf je Sitzung; Budget je Sitzung getrennt nach gebundener und fremder Adresse; Strom-Abwurf mit Vorrang für Global und Durchsage | volumetrisch gegen den Heimanschluss nicht abwehrbar (Umzug zum Anbieter, Offener Punkt); neue Registrierungen können unter Angriff zeitweise scheitern |
| T5 | Übernahme eines Discord-Kontos | kann `/funk verknuepfen` auslösen und Codes eingeben | nur ein Gerät, das selbst einen Code anzeigt, lässt sich verknüpfen; jede Verknüpfung braucht die Freigabe durch einen Admin; der Inhaber bekommt eine DM und eine Anzeige auf seinen Geräten; privilegierte Konten nur über das Admin-Werkzeug (3.5) | wenn Admin und Opfer unaufmerksam sind |
| T6 | Infostealer auf einem Mitglieds-PC | liest Dateien und Speicher des Nutzers | Schlüssel im TPM nicht exportierbar, keine Tokens und Passwörter auf der Platte; ein kopierter Schlüssel kann andere Geräte weder freigeben noch dauerhaft sperren (3.6); häufiges gegenseitiges Verdrängen löst Alarm aus (4.6) | bei Software-Schlüssel kopierbar; Schlüsselart für den Admin sichtbar, aber ungeprüft |
| T7 | Passiver Mithörer im Netz (WLAN, Provider) | liest mit | TLS, AES-256-GCM | Metadaten (1.2); bei TLS 1.2 ist der öffentliche Geräteschlüssel lesbar |
| T8 | Aktiver Angreifer auf dem Netzweg, auch ein Beobachter mit schnellerem Weg (gleiches WLAN) | verwerfen, verzögern, umleiten, echte Pakete kopieren | AEAD, Replay-Fenster, Pfadprüfung mit Gegenprobe und Kopierprüfung (5.10), gepinnter Serverschlüssel, signiertes Profil | kann den Dienst stören, aber nicht mitlesen oder einsprechen |
| T9 | Troll im eigenen Kreis | spricht dauernd, flutet | Paket- und Byte-Topf je Sitzung, Hinweis an die Einsatzleitung mit Stummschalt-Knopf, Stummschalten, Sperre durch den Admin | - |
| T10 | Kompromittierter Bot-Dienst (etwa durch einen Fehler in der Discord-Bibliothek) | meldet beliebige Discord-IDs an den Server | Grenzen im Server, unabhängig vom Bot (3.8): Verknüpfungen enden nur in WARTET, nie für privilegierte Konten; höchstens 3 automatische Sperren je Stunde, nie für ADMIN-Konten; keine Freigabe über den Bot | lästige Einträge im Warteraum, einzelne Sperren, bis ein Admin eingreift |
| T11 | Übernahme eines Admin-PCs oder seines Geräteschlüssels | benutzt Geräteschlüssel und Admin-Schlüssel, liest mit, was der Admin tippt | Verwaltungsrechte nur in einer Admin-Sitzung mit Admin-Schlüssel und Code aus der App (3.9), heikle Handlungen nur mit frischem Code; jeder Code gilt einmal; Warnung an jedes ADMIN-Konto bei einer Admin-Sitzung von einem neuen Gerät; ADMIN-Konten im Client nur sperren, Konto des Eigentümers gar nicht (3.10); Stufe ADMIN und Verwaltungsrechte nur an der Konsole; Journal | Schaden in den Minuten, in denen der Admin selbst angemeldet ist und bestätigt; ein neuer Zweitfaktor entsteht nur an der Konsole |
| T12 | Code-Phishing ("schick mir mal deinen Code") | bekommt einen gültigen Code aus der App | ohne den Admin-Schlüssel im TPM des Admins nutzlos; die Unterschrift bindet Sitzung, Gerät, Nonce und Zweck (3.9) | - |
| T13 | Namensbesetzer: fremdes Discord-Konto oder Antrag mit dem Namen eines Mitglieds | beantragt mit fremdem SC- oder Discord-Namen, oder ändert als Mitglied den eigenen Discord-Namen auf den eines anderen | offene Anträge besetzen keinen Namen; bei der Freigabe kommen nur freie SC-Namen in den Namensraum, vergebene zeigt die Freigabeansicht; ein vergebener Discord-Name verhindert die Freigabe (A53); SC- und Discord-Namen serverweit eindeutig über Konten und Vormerkungen (NFKC, Großschreibung); den Discord-Namen ändert nur ein Admin in der Admin-Sitzung oder die Konsole, `ProfilSetzen` nimmt keinen anderen an; Freigabe nur nach Kennungsvergleich durch einen Admin (3.3 bis 3.5) | Admin gibt trotz Warnung frei; Verwechsler nach UTS #39 (Punkt 26) |
| T14 | Diebstahl einer Sicherung, auch als Container-Sicherung des LXC | liest `admins.json`, `konten.json`, `journal.json` | TOTP-Geheimnisse nur mit AES-256-GCM verschlüsselt, Schlüssel per `LoadCredential` außerhalb der Sicherung; Notfallcodes nur als HMAC-Werte (3.9) | wer Credential und Sicherung zugleich hat, kann Codes erzeugen, braucht aber weiter den Admin-Schlüssel; Klarnamen im Journal |
| T15 | Übernahme des Dienstbenutzers auf dem Server | liest den Speicher des Dienstes, ruft die Konsole auf | außerhalb dessen, was das Protokoll verhindern kann (1.2); Konsole nur für root und den Dienstbenutzer, jede Konsolenhandlung im Journal | vollständig |

Außerhalb des Modells: Geheimdienste, Angriffe auf Discord oder Raid-Helper selbst, physischer Zugriff auf den Server und Hintertüren in .NET oder OpenSSL.

### 1.5 Lehren aus dem früheren Funkprogramm

| Früheres Funkprogramm | Funk v1 |
| --- | --- |
| Klartext-Passwörter (S3) | Keine Passwörter. Geräteschlüssel ECDSA P-256, im TPM nicht exportierbar. Einmalcodes liegen nur als SHA-256-Wert vor, einmalig, 5 min gültig |
| Öffentlich lesbarer Handshake-Token (S4) | Kein gemeinsames Geheimnis und kein Inhaber-Token. Der Einmalcode verbindet nur das Gerät, das ihn anzeigt, mit dem Discord-Konto, das ihn eingibt, und führt nie direkt zu FREIGEGEBEN. Die Sitzungs-ID ist bewusst öffentlich und ohne Schlüssel wertlos. UDP-Schlüssel gehen nur durch TLS und sind je Sitzung und Epoche neu und zufällig |
| Unsignierte Updates (S1) | Ist nicht Teil dieses Protokolls (signiertes Manifest). Der Server erzwingt `mindestClientVersion`. Das Serverprofil wird nur offline signiert angenommen, mit steigendem Versionszähler (3.1) |
| Offene Ports auf allen Schnittstellen (S6) | Der Server bindet nur ausdrücklich konfigurierte Adressen, genau 1 TCP- und 1 UDP-Port. Der Client nutzt einen verbundenen UDP-Socket auf einem Ephemeral-Port. Das Admin-Werkzeug läuft nur über einen UNIX-Socket |
| Telemetrie (S7) | keine; die Messwerte aus 7.8 bleiben auf dem eigenen Server |

### 1.6 Umgang mit dem Eigenbau-Risiko

architektur-urteil.md nennt als Hauptrisiko von Weg A, dass sicherheitskritischer Eigenbau ohne fremde Durchsicht entsteht. v1 begegnet dem so:

- Es gibt keine eigenen Krypto-Primitive, nur `SslStream`, `AesGcm`, `ECDsa`, `SHA256`, `HMACSHA256`, `HKDF`, `RandomNumberGenerator` und `CryptographicOperations.FixedTimeEquals` und `ZeroMemory`, dazu `HMACSHA1` ausschließlich für TOTP nach RFC 6238/4226 (3.9), weil jede App diese Standardwerte versteht; die Kollisionsschwäche von SHA-1 betrifft HMAC nicht.
- Keine öffentliche Web-Oberfläche: Die schlanke Anmeldung ersetzt OpenIddict, mTLS-Alias und den zweiten Hostnamen.
- Der Protokollkern ist eine eigene kleine Bibliothek ohne Abhängigkeiten. Parsen, Versiegeln, Replay-Fenster, Rechteprüfung, Phasenbuch und Empfängerbestimmung sind darin reine Funktionen, getrennt vom Netzwerk-I/O. So lassen sie sich testen, fuzzen und durchsehen.
- Der Sprachpfad des Servers läuft in genau einem Relay-Thread (5.14). So gibt es keine Nebenläufigkeit auf Zählern, Replay-Fenstern und `AesGcm`-Instanzen.
- Review-Paket: diese Spezifikation, der Protokollkern und die Testvektoren, zusammen unter etwa 4000 Zeilen. Es ist für eine fremde Durchsicht gedacht (Offener Punkt: wer).
- Die Testvektoren aus Abschnitt 8 rechnet eine zweite, unabhängige Implementierung nach, etwa Python `cryptography` oder OpenSSL, am besten von jemand anderem. Dann bleiben symmetrische Fehler von Client und Server nicht unentdeckt.
- Es gibt keinen "unsicheren Modus", keinen Klick auf "trotzdem verbinden" und keine Abschaltung der Prüfungen im Release.
- Für einen Einsatz steht ein Notbetrieb bereit: Fällt der Server aus, weicht die Org auf Discord-Voice aus. Das ist organisatorisch, nicht technisch.

## 2. Begriffe

| Begriff | Bedeutung |
| --- | --- |
| Netz | Logischer Funkkanal im Server mit fester Netz-ID (`u16`, 1-65534). Ein Name ohne Sortiernummer, die Reihenfolge liegt getrennt (A16). Im früheren Funkprogramm und in der Oberfläche "Kanal"; im Protokoll und im Code bleibt "Netz". Seit 0.6.0 sind die IDs ab 0x8000 den Kanälen vorbehalten (6.16). |
| Global | Sondernetz mit der festen Netz-ID 1 (A23). Alle aktiven Sitzungen hören es. Das Hören lässt sich nicht abschalten, gesendet wird nur mit dem Recht `GLOBAL_SENDEN` (Führung) und standardmäßig nur per Taste. Global ist kein Feld. Ist Global ein Kanalnetz (seit 0.6.0), ist sein Ch. 1 "Orgaweit" dieses Netz 1 selbst; die übrigen Ch. sind gewöhnliche Kanäle (6.16). |
| Kanalnetz | Netz mit einer Liste von Ch.-Bezeichnungen (`kanaele`, 2 bis 31 Einträge, seit 0.6.0), etwa "Brücke" mit "Ruhe", "Schiff 1" bis "Schiff 12" und "Alle". Seine Netz-ID (1 bis 1022) steht nie in einem Feld. |
| Ch. | Index in der Liste eines Kanalnetzes, 0 bis 30. Ch. 0 heißt "Ruhe": Das Feld trägt das Kanalnetz, hört und sendet aber nichts; ab Werk steht jedes Feld eines Kanalnetzes auf Ruhe. |
| Kanal | Ein Kanalnetz mit einem Ch. (Ch. 1 und höher), etwa "Brücke, Schiff 3". Auf dem Draht steht er als Kanal-ID wie ein Netz. |
| Kanal-ID | `0x8000 + Netz-ID x 32 + Ch.` (6.16): die Netz-ID eines Kanals oder (bei Ch. 0) der Ruhe eines Kanalnetzes. Sie wird berechnet, nie angelegt. |
| Orgaweit | Ch. 1 von Global, wenn Global ein Kanalnetz ist: das feste Global-Feld, also Netz 1. Seine berechnete Kanal-ID 0x8021 gilt nirgends. |
| Gewählte Rolle | Die Rolle, deren Felder ein Konto über `RolleWaehlen` (0x0039) gerade hat; nicht der Platz (6.16). |
| Feld | Platz 0-15 in der Funkfeld-Ansicht des Nutzers (5er- oder 10er-Kacheln). Eine Mitgliedschaft ist (Feld, Netz, hören, senden, fest); das Netz ist ein gewöhnliches Netz oder eine Kanal-ID. Je Konto steht jedes Netz und jeder Kanal (auch die Ruhe eines Kanalnetzes) in höchstens einem Feld. |
| Festes Feld | Feld, das die Einsatzleitung oder eine Aufstellung gesetzt hat (`fest = true`). Der Funker kann es nicht leeren oder überschreiben, nur das Hören abschalten (6.9). |
| Hörnetz | Netz, in dem das Konto Mitglied mit `hoeren = true` ist. Nur dorthin leitet der Server normale Sprache weiter. `hoeren = false` ist der Aus-Schalter je Feld (A17). |
| Sendeauswahl | Die Felder (und gegebenenfalls Global), in die der Nutzer gerade senden will (Numpad +/-, A7). Der Client hält sie als Feldnummern und setzt sie beim Senden in Netz-IDs um. Das Sprachpaket nennt die Netze ausdrücklich. `SendeauswahlSetzen` ist nur Anzeige- und Speicherzustand. |
| Senderecht | Mitgliedschaft mit `senden = true`, nicht stummgeschaltet. Für Global das Recht `GLOBAL_SENDEN`, für mehrere Ziele zugleich `MEHRFACH_SENDEN`. |
| Durchsage | Senden mit Zielart DURCHSAGE an ausgewählte Netze (bis 64) oder an alle (A8), Recht `DURCHSAGE` bzw. `DURCHSAGE_ALLE`. Sie erreicht alle Mitglieder der Zielnetze, auch bei ausgeschaltetem Hören. Die Empfänger senken NETZ-Ströme ab. |
| Schiff | Einheit mit Schiffsnetzen (z. B. "Schiff 3" mit Durchsagen, Crew, Brücke). Die Schiffsnetze sind über Kennungen wie `CREW` erreichbar. |
| Einheit | Schiff, Trupp (Infanterie) oder Orga-Einheit (A39), auf dem Draht weiter `schiffe[]` mit `art`. Alles außer Art und Typ ist für alle drei gleich: Einheitsnetze, Plätze je Rolle mit Kapazität und Mindestbesetzung, Belegung, Selbsteinteilung, Verschieben, Aufstellung. |
| Flotte | Was ein Admin pflegt: Einheiten, Netze, Rollen, Plätze, Mindestbesetzung. Nur mit `NETZE_VERWALTEN` in einer Admin-Sitzung änderbar; eine ganze Flotte geht als Verwaltungspaket (N5) in einem Schritt. |
| Belegung | Je Platz, wie viele Konten ihn BELEGT oder RESERVIERT halten, dazu wie viele online sind und wie viele davon einen Platz haben (Push 0x4003). |
| Rolle | Funkrolle wie "Kapitän" (A22): eine Vorlage für Felder, jeweils mit festem Netz oder Schiffsnetz-Kennung, Standard für hören und senden, Sendevorschlag und Festlegung. Seit 0.6.0 auch das, was das frühere Funkprogramm "Preset" nannte: Ein Rollenfeld kann ein Kanalnetz nennen, der Ch. wird dann aufgelöst (6.16). |
| Platz | (Schiff, Rolle) eines Kontos. Zustand FREI, BELEGT oder RESERVIERT (6.9). Kapazität je Schiff und Rolle, die Belegung ist sichtbar (A15). |
| Aufstellung | Benannte, gespeicherte Liste von Plätzen und Feldern für viele Konten (A5, A13), die in einem Zug angewendet wird. |
| STANDARD | Die Felder, die ein Konto ohne Platz und ohne Vorgabe hat (6.9). |
| Stufe | ADMIN, RAIDLEITER oder FUNKER. Legt Standardrechte und Rang fest (6.1, 6.10). Nicht verwechseln mit Rolle. |
| Funker | Konto der Stufe FUNKER: hört und sendet in den eigenen Feldern und wählt selbst Rolle und Schiff, wenn das erlaubt ist. |
| Raidleiter | Stufe RAIDLEITER, in der Oberfläche "Führung": Global, Durchsagen, Mehrfachsenden, Sprechanzeige aller Netze. Seit 0.3.0 ohne `ZUWEISEN` (A37); auf dem Draht bleibt der Name RAIDLEITER. |
| Einsatzleitung | Wer an einem Abend einteilt: ein ADMIN (auch ohne Admin-Sitzung) oder ein Konto mit laufendem Auftrag, als Ausnahme mit dem Einzelrecht `ZUWEISEN`. Technisch jedes Konto mit `ZUWEISEN` zum Zeitpunkt der Anfrage. Das ist, was Sarevoc "Raidleiter" nennt. |
| Admin | Stufe mit allen Rechten, dazu Konten, Geräte und Verwaltung der Netze. |
| Privilegiertes Konto | Konto der Stufe ADMIN oder mit einem der Rechte `GERAETE_FREIGEBEN`, `KONTEN_VERWALTEN`, `NETZE_VERWALTEN`. Geräte dafür nur über das Admin-Werkzeug. |
| Führung | Alle Konten mit `GLOBAL_SENDEN`. |
| Gerät | Eine Client-Installation mit eigenem Geräteschlüssel (ECDSA P-256). Kennung ist der SPKI-Fingerabdruck (SHA-256 über SubjectPublicKeyInfo). |
| Anzeigeform | Die ersten 8 Byte des Fingerabdrucks, hexadezimal, groß, in 4er-Gruppen, z. B. `5CD2-52FB-0CE8-9324`. |
| Konto | Eine Person. Fester Schlüssel ist die Konto-ID; die Discord-ID ist eindeutig, fehlt aber bei Konten aus einem Antrag ohne Code (3.4). Dazu Discord-Benutzername, Anzeigename, SC-Namen, Discord-Name, Stufe, Rechte und bis zu 3 aktive Geräte. |
| Antrag | Anmeldung ohne Bot: Das Gerät meldet SC-Name und Discord-Name aus dem Setup, es entsteht ein Antragskonto mit dem Gerät in WARTET (3.4). Die Namen sind Angaben, keine Beweise. |
| SC-Name | Name in Star Citizen, 1 bis 3 je Konto, der erste ist der Hauptname. Serverweit eindeutig über Konten und Vormerkungen (NFKC, Großschreibung). |
| Discord-Name | Name in Discord, wie eingetippt ("Mustermann/Max"), ungeprüft. Nicht verwechseln mit dem geprüften Discord-Benutzernamen aus dem Bot. |
| Admin-Schlüssel | Eigener ECDSA-P-256-Schlüssel eines Admins, getrennt vom Geräteschlüssel, im TPM ohne Rückfall auf Software (3.9). Anzeigeform heißt Admin-Kennung. |
| Admin-Sitzung | Zustand einer AKTIV-Sitzung eines ADMIN-Kontos, nachdem es sich mit Admin-Schlüssel und Code ausgewiesen hat. Nur darin wirken die Verwaltungsrechte (3.9). |
| Schritt-Bestätigung | Frischer Code (höchstens 5 min alt) für heikle Handlungen in einer Admin-Sitzung, angefordert mit `SCHRITT_BESTAETIGEN` (3.9). |
| Eigentümer | Genau ein ADMIN-Konto (Sarevoc), gesetzt nur an der Konsole; ernennt Admins, richtet den Zweitfaktor ein, ist im Client unantastbar (3.10). |
| Vormerkung | Mitglied, das sich noch nicht angemeldet hat: SC-Name, Discord-Name, Rollen, Notiz. Belegt den SC-Namen und wird bei der Freigabe übernommen (6.13). |
| Rollenkatalog | Liste der Mitgliederrollen (Pilot, Sanitäter und so weiter), gepflegt vom Admin (6.13). Nicht verwechseln mit der Funkrolle (Rolle). |
| Lieblingsrolle | Wunschrolle eines Mitglieds aus dem Rollenkatalog, bis zu 8, eine davon als erste Wahl (6.13). |
| Untergilde | Entfallen seit 0.11.8 (Sarevoc: "Untergilden entfallen ganz, überall"). Bis 0.11.7 eine Gruppe innerhalb der Org mit Kürzel; 0x001D bleibt als Hülle, gespeicherte Untergilden reicht der Server unverändert durch (6.13). |
| Zeichen | Kennung neben dem Namen (A49): `ADMIN` (Lorbeer), `FUEHRUNG`, `EINSATZLEITUNG` (laufender Auftrag). Der Server schickt nur die Kennung, die Grafik zeichnet der Client. Seit 0.11.7 zeichnet der Client `FUEHRUNG` nicht mehr; `ADMIN` und `EINSATZLEITUNG` lassen sich wie Abzeichen ausblenden. |
| Abzeichen | Seit 0.11.7: was jemand spielt, vom Admin vergeben (6.13). Ränge in drei Laufbahnen (Marine 10 Stufen, Navy 10, Orga 8, je Laufbahn höchstens einer) und Tätigkeiten (22 im Katalog, höchstens 6 je Person). Kennungen und Namen stehen im festen Abzeichenkatalog (`abzeichen.json` im Programm), die Bilder bringt der Client mit. Nicht verwechseln mit den automatischen Zeichen. |
| Journal | Protokoll der Verwaltung mit Klarnamen, 90 Tage, nur für Admin-Sitzungen und die Konsole (3.9). |
| Beauftragung | "Auftrag für einen Abend" (A37): befristete Einsatzleitung eines Kontos durch einen Admin, mit Beginn (bis 14 Tage voraus), Ende (höchstens 24 h später, Voreinstellung 04:00 Uhr), Rechten und Anlass (6.12, Nachricht 0x0017). In der Oberfläche "Auftrag"; `Auftrag` heißen im Zustandsschreiber die Einträge der Warteschlange (6.2). |
| Beauftragter | Konto mit laufendem oder geplantem Auftrag. |
| Sitzung | Eine authentisierte TLS-Verbindung eines freigegebenen Geräts samt UDP-Schlüsseln. Kennung ist die Sitzungs-ID (`u32`, zufällig, ungleich 0, öffentlich). |
| Epoche | Generation der UDP-Schlüssel innerhalb einer Sitzung (`u8`, beginnt bei 0). |
| Sprechphase | Zusammenhängendes Senden an eine feste Zielmenge mit festem Auslöser, von Beginn bis ENDE. Kennung `u16`, vom Sender vergeben. |
| Frame-Nr | Nummer des codierten 20-ms-Frames (`u32`). Innerhalb einer Sprechphase steigt sie je Frame um genau 1. Beim Beginn einer Sprechphase wird sie an die Sitzungsuhr angelegt, damit der Empfänger Pausenlängen erkennt (5.6). |
| Vorrangspur | Liste von Präfixen, von denen in den letzten 14 Tagen eine AKTIV-Sitzung kam. Sie gehen an den globalen Töpfen vorbei (4.8). |
| Kandidat | Neue UDP-Quelladresse einer Sitzung, die noch geprüft wird (5.10). |

## 3. Identität und Anmeldung

### 3.1 Server-Identität und Serverprofil

- Der Server hat einen langlebigen Identitätsschlüssel (ECDSA P-256) und ein selbstsigniertes Zertifikat dazu. Der Schlüssel kommt per systemd `LoadCredential` in den Prozess, nie aus einer lesbaren Konfigurationsdatei.
- Der Client prüft den Server allein über SPKI-Pins: SHA-256 über das DER-kodierte SubjectPublicKeyInfo des Blattzertifikats. Name, Kette und Gültigkeit prüft er nicht. Das ersetzt Let's Encrypt. Es braucht keinen Port 80, es passt zu DynDNS, und ein späterer Umzug zum Anbieter geht ohne Zertifizierungsstelle (Abweichung von server-sicherheit.md, siehe Anhang).
- Das Serverprofil ist ein signiertes Objekt `{daten: b64, signatur: b64}`.
  - `daten` ist UTF-8-JSON: `{name: str(32), profilVersion: u32, ausgestellt: i64, gueltigBis: i64, hosts: [{name: str(253), familien: ["IPv4" und/oder "IPv6"]}] 1..4, tcpPort: u16, pins: [b64] 2..3, nameserver?: [str(253)] 0..4}`. `pins` enthält den aktuellen Schlüssel und mindestens einen Reserveschlüssel, der offline erzeugt und gesichert ist. `nameserver` nennt optional die autoritativen Nameserver der DynDNS-Zone (7.1).
  - `signatur` ist ECDSA P-256 mit SHA-256 im Format IEEE P1363 (r und s je 32 Byte, zusammen 64 Byte) über die Bytes `"tacrelay profil v1"` (ASCII) || `0x00` || `daten`, genau so, wie `daten` übertragen wird. Es gibt keine Kanonisierung, geprüft wird über die empfangenen Bytes (Vektor 8.11).
  - Signiert wird offline mit dem Profilschlüssel. Dessen öffentliche Schlüssel (aktuell und Reserve) sind fest im Client eingebaut und wechseln nur über ein signiertes Update. Der Profilschlüssel liegt nie auf dem Server.
- Der Client übernimmt ein Profil nur, wenn die Signatur mit einem eingebauten Schlüssel gilt, der Inhalt dem Schema entspricht und `profilVersion` größer ist als die höchste bekannte. Ältere, gleiche, unsignierte, fremd signierte und unlesbare Profile verwirft er und protokolliert das. Nach `gueltigBis` bleibt das Profil in Gebrauch, der Client zeigt eine Warnung.
- Verteilt wird das Profil mit dem Installer (`profil.json` im Programmordner) und nach der Freigabe über den Server: Er reicht das offline signierte Objekt in `Welcome.profil` nur durch, etwa mit neuen Hosts beim Umzug oder neuen Pins. Unsignierte Änderungen gibt es nicht, auch nicht für Hosts. Wer den laufenden Serverschlüssel erbeutet, kann damit weder Pins noch Hosts der Clients ändern.
- Übernahme aus Welcome (F1, seit dem Paket "Einrichten und Umzug"): Nach `Welcome{FREIGEGEBEN}` prüft der Client `Welcome.profil` nach der Regel oben. Die höchste bekannte Version ist die höchste gültige aus Installer-Profil und Ablage. Das Protokoll liegt als `serverprofil-protokoll.txt` neben den Einstellungen; dieselbe Meldung steht dort nicht zweimal hintereinander, sonst käme bei jedem Verbinden "nicht neuer" dazu. Im Testzugang und in der Vorführung übernimmt der Client nichts.
- Ablage: Ein übernommenes Profil schreibt der Client als `{daten, signatur}`, so wie es kam, atomar nach `serverprofil.json` neben seine Einstellungen (erst eine Nachbardatei, dann ersetzen). Bei jedem Gebrauch liest er Installer-Profil und Ablage neu, prüft beide wie ein angebotenes Profil und nimmt das gültige mit der höheren `profilVersion`, bei Gleichstand das des Installers. Fehlt die Ablage, ist sie unlesbar oder fremd signiert, gilt das Installer-Profil, ohne Meldung; das ist ein älterer Stand, kein Fehler.
- Wirksam wird ein übernommenes Profil ab dem nächsten Verbindungsaufbau, auch beim automatischen Wiederverbinden (7.1); die laufende Verbindung bleibt, wie sie ist. Die Profilkarte zeigt "Vom Server übernommen, Version N".
- Pin des liefernden Servers (Gegenprüfung 01.10.2026): Ein Profil aus `Welcome.profil` übernimmt der Client nur, wenn darin der Pin steht, den der Server dieser Verbindung im Handshake gezeigt hat; sonst verwirft und protokolliert er es ("ohne Pin dieses Servers"). Ohne diese Regel übernähme jeder Client, der sich mit einem Server verbindet, auf den der Betreiber aus Versehen ein echt signiertes Profil ohne dessen Pin gelegt hat (etwa das Abschlussprofil eines Umzugs auf den alten Server), dieses Profil und fände danach keinen Server mehr, bis ein neues Setup kommt. Beim Umzug ist die Regel immer erfüllt: Das Übergangsprofil enthält den alten und den neuen Pin, das Abschlussprofil den neuen, und mit dem Reserveschlüssel steht die Reserve darin.
- Was das schützt und was nicht: Ein Server mit gültigem Pin, auch einer mit erbeutetem Serverschlüssel, kann nur durchreichen, was offline signiert ist; ein älteres, echt signiertes Profil (zurück auf alte Hosts oder Pins) scheitert an der Versionsregel. Wer auf dem Rechner des Mitglieds die Ablage ändern kann, kann sie löschen oder gegen ein älteres echt signiertes Profil tauschen, nie unter die Version des Installers; gegen jemanden mit diesem Zugriff schützt das Profil ohnehin nicht.

### 3.2 Geräteschlüssel und Gerätezertifikat

- Der Schlüssel ist ECDSA P-256. Er wird zuerst über den Microsoft Platform Crypto Provider (TPM 2.0) mit `ExportPolicy = None` erzeugt, persistiert und im Benutzerkontext. Fehlt ein TPM oder scheitert das, nimmt der Client den Microsoft Software Key Storage Provider, ebenfalls `ExportPolicy = None`, und meldet `schluesselArt = "SOFTWARE"`.
- Das Zertifikat ist selbstsigniert: Subject `CN=Funk-Geraet` ohne persönliche Angaben, denn bei TLS 1.2 wird es unverschlüsselt übertragen. Seriennummer 16 Zufallsbytes, NotBefore = jetzt - 1 h, NotAfter = jetzt + 20 Jahre. KeyUsage `digitalSignature` (kritisch), EKU `clientAuth` (1.3.6.1.5.5.7.3.2), Signatur ECDSA mit SHA-256.
- Der Server akzeptiert genau ein Blattzertifikat mit einem P-256-Schlüssel und ignoriert Gültigkeit und Kette. Den Besitz des Schlüssels beweist der TLS-Handshake (CertificateVerify). Die Identität des Geräts ist der SPKI-Fingerabdruck (32 Byte). Anzeigeform nach Vektor 8.5.
- Die Schlüsselart meldet der Client selbst, der Server kann sie ohne Attestation nicht prüfen. Der Admin sieht sie in der Freigabe als "laut Client, ungeprüft". TPM-Attestation kommt frühestens in v2.
- Gesperrt (`GERAET_GESPERRT`) ist ein Gerät seit 0.7.0 nur, wenn ein Admin es bewusst dauerhaft sperrt (3.6). Ein abgelehntes oder gelöschtes Gerät ist danach unbekannt und darf neu beantragen (3.5); einen neuen Geräteschlüssel braucht es dafür nicht. Ein neuer Schlüssel hebt auch keine Sperre auf, denn jeder neue Schlüssel braucht wieder eine Freigabe. Bis 0.6.x sah dieser Abschnitt vor, dass der Client nach `GERAET_GESPERRT` einen neuen Schlüssel anbietet; gebaut wurde das nie.

### 3.3 Konto

| Feld | Typ | Regel |
| --- | --- | --- |
| kontoId | u32 | vom Server vergeben, 1 bis 2^31-1, nicht geheim |
| discordId | Zeichenkette (Snowflake), optional | eindeutig, unveränderlich. Fehlt bei Konten aus einem Antrag ohne Code (3.4, solange es keinen Bot gibt); solche Konten nehmen am Abgleich (3.7) nicht teil |
| benutzername | Zeichenkette, höchstens 32 | eindeutiger Discord-Benutzername (ASCII), vom Bot geliefert, beim Abgleich (3.7) aktualisiert. Wird in Freigabe-, Verwaltungs- und Sprecheranzeige zusätzlich gezeigt (in der Sprecheranzeige als Zweitzeile oder Tooltip). Ohne Bot leer; wo der Draht ihn als Pflichtfeld trägt (Welcome, GeraetStatus, GeraeteListe, Praesenz), steht dann `-` |
| scNamen | 1 bis 3 Zeichenketten, je 2 bis 32 | SC-Namen, der erste ist der Hauptname. Buchstaben (auch Umlaute), Ziffern, Leerzeichen, `_`, `-` und `.`, aber keine Hangul-Füller und nichts sonst aus Default_Ignorable_Code_Point (sie gelten als Buchstaben und zeigen sich nicht); Leerzeichen vorn und hinten fallen weg. Serverweit eindeutig nach NFKC und `ToUpperInvariant` (das große ẞ zählt wie ß), über alle Konten und Vormerkungen (6.13). Setzt das Konto selbst (`ProfilSetzen`) oder ein Admin (`KontoVerwaltung SCNAMEN`) |
| discordName | Zeichenkette, 2 bis 32, optional | Name in Discord wie eingetippt ("Mustermann/Max"; der Schrägstrich trennt nichts). Seit Antwort 3 für alle sichtbar ("Wer ist da", 6.13), darum ohne täuschende Zeichen: nichts aus den Unicode-Klassen Cf (etwa U+200B, U+202E, U+FEFF, U+00AD), Zl, Zp, Co und Cn, kein Default_Ignorable_Code_Point (auch die aus Mn und Lo: Verbinder U+034F, Variantenwähler U+FE00-FE0F und U+E0100-E01EF, Hangul-Füller U+3164, U+115F, U+1160, U+FFA0) und kein leeres Braillemuster U+2800, als Leerzeichen nur U+0020 und nie zwei hintereinander, sonst `UNGUELTIG`. Seit A53 serverweit eindeutig nach NFKC und `ToUpperInvariant` (das große ẞ zählt wie ß) wie die SC-Namen, über alle Konten und Vormerkungen (6.13), sonst `NAME_VERGEBEN`. Setzt ihn nur ein Konto mit `KONTEN_VERWALTEN` in einer Admin-Sitzung (`KontoVerwaltung DISCORDNAME`) oder die Konsole (`discordname`); andere ADMIN-Konten und den Eigentümer nur die Konsole, das eigene Konto der Admin selbst. `ProfilSetzen` ändert ihn nicht (4.5). Aus dem Antrag kommt er mit der Freigabe, wenn er frei ist (3.5). Clients zeigen daneben weiter den Hauptnamen (`scNamen[0]`) |
| eigentuemer | bool | genau ein ADMIN-Konto, nur an der Konsole gesetzt (3.10) |
| name | Zeichenkette, höchstens 32 | Vorschlag beim Verknüpfen aus Discord (Server-Nick, sonst global_name), vom Admin änderbar. Serverweit eindeutig nach NFKC-Normalisierung und `ToUpperInvariant` (das große ẞ zählt wie ß; gespeicherte Schlüssel zieht das Laden nach); bei einer Kollision hängt der Server " (2)", " (3)" usw. an |
| stufe | ADMIN, RAIDLEITER, FUNKER | Standard FUNKER |
| rechte | Menge (6.1) | Standard aus der Stufe; Einzelrechte als sichtbare Abweichung `plus`/`minus` (6.10) |
| status | AKTIV, GESPERRT | GESPERRT mit Grund `ADMIN` oder `ABGLEICH` |
| geraete | höchstens 3 in WARTET oder FREIGEGEBEN | Status je Gerät: WARTET, FREIGEGEBEN, ENTFERNT, ENTZOGEN. ABGELEHNT gibt es seit 0.7.0 nur noch als Meldung (`GeraetStatus`), das Gerät ist danach gelöscht |

Konten ohne freigegebenes Gerät werden 30 Tage nach dem letzten Geräteereignis gelöscht, samt Pegeln, Ablage und Plätzen. Das Konto des Eigentümers ist davon ausgenommen.

Ein Antragskonto (3.4, Weg B) ist ein Konto im Zustand ANTRAG: Stufe FUNKER, keine Rechte, keine Discord-ID, dazu der Antrag `{scName, discordName, gestellt}`. Es steht nicht im Namensraum, nicht im Lagezustand (6.2) und in keiner Praesenz. Es verschwindet, sobald sein Gerät verfallen, entfernt oder abgelehnt ist (seit 0.7.0 bleibt dabei nichts gesperrt; bis 0.6.x blieb ein abgelehnter Fingerabdruck gesperrt), und wird mit der Freigabe zum gewöhnlichen Konto oder geht in einem vorhandenen Konto auf (3.5).

### 3.4 Registrierung (Einmalcode, vom Client angezeigt, in Discord eingegeben)

Gewählte Richtung: Der Client zeigt den Code, der Nutzer tippt ihn in Discord. Damit ist Offener Punkt 22 aus Entwurf 1 entschieden (Abweichung von raidhelper.md B15, siehe Anhang).

1. Der Client erzeugt beim ersten Start Geräteschlüssel und Zertifikat (3.2), verbindet sich (4.1) und sendet `Hello`. Der Server kennt den Fingerabdruck nicht und antwortet mit `Welcome{status: "UNBEKANNT"}`. Zustand REGISTRIERUNG.
2. Der Client sendet `Registrierung{geraetename}`. Der Server erzeugt einen Code: 12 Zeichen Crockford-Base32 (Alphabet `0123456789ABCDEFGHJKMNPQRSTVWXYZ`), also 60 bit aus `RandomNumberGenerator`, angezeigt als `XXXX-XXXX-XXXX`. Der Code ist an diese Verbindung und ihren Fingerabdruck gebunden, 5 min gültig und einmalig. Der Server speichert nur SHA-256 über die normalisierte Form (ASCII). Eine neue Anfrage auf derselben Verbindung ersetzt den offenen Code. Der Client zeigt: "Tippe in Discord: /funk verknuepfen XXXX-XXXX-XXXX. Der Code verbindet diesen Rechner mit deinem Discord-Konto. Tippe ihn nur selbst ein und zeig ihn nicht im Stream."
3. Der Nutzer tippt in Discord `/funk verknuepfen <code>`. Der Bot-Dienst prüft, dass die Interaktion aus der konfigurierten Gilde kommt und das Mitglied eine der freigeschalteten Discord-Rollen hat. Er antwortet ephemer mit dem Warntext "Mit diesem Code bekommt der Rechner, der ihn anzeigt, Zugang zu deinem Funkkonto. Gib nur Codes ein, die dein eigenes Funkprogramm dir gerade zeigt. Niemand aus der Führung fragt dich nach Codes." und reicht `Verknuepfen{discordId, benutzername, name, code}` über den UNIX-Socket an den Server.
4. Der Server normalisiert: Leerraum und `-` entfernen, Großbuchstaben, `O`->`0`, `I`->`1`, `L`->`1`. Danach müssen es genau 12 Zeichen aus dem Alphabet sein. Er sucht über den Hashwert. Falsch, abgelaufen und verbraucht ergeben einheitlich `CODE_UNGUELTIG`. Zusätzlich gelten die Grenzen aus 3.8. Gehört die Discord-ID zu einem privilegierten Konto, lautet das Ergebnis `NUR_ADMIN_WERKZEUG`, bei einem gesperrten Konto `KONTO_GESPERRT`, bei mehr als 3 Geräten in WARTET oder FREIGEGEBEN `GERAETE_LIMIT`. In diesen Fällen ist der Code verbraucht, der Bot meldet den Grund, und die Verbindung bekommt einen `Fehler`-Push mit demselben Code (ohne Trennung, der Client kann einen neuen Code anfordern).
5. Bei einem Treffer wird der Code sofort verbraucht. Der Server fragt die Verbindung per `VerknuepfungBestaetigen{konto: {name, benutzername, neu}}` (Frist 120 s). Der Client zeigt "Dieses Gerät wird mit dem Discord-Konto <benutzername> (<name>) verknüpft. Bist du das?". Bei nein oder Fristablauf wird nichts angelegt, der Bot meldet "vom Gerät abgelehnt".
6. Bei ja legt der Server das Konto zur Discord-ID an (Stufe FUNKER) oder findet es, legt das Gerät mit Fingerabdruck, Name, Schlüsselart und Status WARTET an, sendet `GeraetStatus{WARTET}` an die Verbindung und wechselt nach WARTERAUM.
7. Benachrichtigung: `GeraetStatus{WARTET}` geht an alle aktiven Sitzungen mit `GERAETE_FREIGEBEN` und an aktive Sitzungen desselben Kontos. Der Bot meldet es im Admin-Kanal und schickt dem Inhaber eine DM: "Gerät <name> (<Anzeigeform>) wurde mit deinem Funkkonto verknüpft. Warst du das nicht, melde dich sofort bei einem Admin." Auch das ist nur Information.

Warum diese Richtung: Ein abgeschriebener oder aus einem Stream abgelesener Code verbindet höchstens das Gerät des Opfers mit dem Discord-Konto des Angreifers, und das lehnt das Opfer in Schritt 5 ab. Einen Zugang verschafft er nie. Gegen gezieltes Überreden ("tipp mal diesen Code ein", RFC 8628 Abschnitt 5.4) helfen in beiden Richtungen nur der Warntext, die Benachrichtigung und die Freigabe durch einen Admin (3.5).

Grenzen: 3 Codes je Verbindung, 10 je Präfix und Stunde. Für das Verknüpfen gelten die Grenzen je Discord-ID aus 3.8. Eine globale Sperre der Registrierung gibt es nicht; ein globaler Zähler (100 je Stunde) löst nur einen Alarm aus.

**Weg B: Antrag ohne Code (Stufe 1, solange es keinen Bot gibt).** Das Setup fragt SC-Name und Discord-Name (A42) und legt sie in `anmeldung.json` ab.

1. Wie Schritt 1 oben, dann `Registrierung{geraetename, scName, discordName}`. `scName` und `discordName` stehen nur zusammen oder gar nicht; ohne sie gilt Weg A.
2. Der Server prüft die Namen (3.3), legt ein Antragskonto (3.3) und das Gerät in WARTET an und antwortet `{antrag: true}` ohne Code. Danach sendet er `GeraetStatus{WARTET}` mit `scName`, `discordName` und `antrag: true` und wechselt nach WARTERAUM. Es gibt keine Rückfrage `VerknuepfungBestaetigen`, denn es gibt kein fremdes Konto, mit dem verknüpft würde.
3. Die Namen sind Angaben, keine Beweise, und kommen erst mit der Freigabe in den Namensraum. Ein offener Antrag besetzt keinen Namen, auch keinen Discord-Namen; zwei Anträge "Mustermann" nebeneinander sind möglich. Der Antrag geht an die Admin-Sitzungen (`GeraetStatus` mit Vorschlägen aus den Vormerkungen, `Meldung{ANTRAG_NEU}`), nie an Sitzungen ohne Admin-Sitzung (3.9). Freigegeben wird wie in 3.5, nachdem der Admin die Gerätekennung mit dem Mitglied verglichen hat (etwa per Discord: "Die Kennung ist kein Geheimnis").
4. Grenzen: dieselben Töpfe wie für Codes (3 je Verbindung, 10 je Präfix und Stunde), dazu höchstens 30 offene Anträge; darüber `ANTRAEGE_VOLL`, eine Meldung an jedes ADMIN-Konto und ein Alarm. Gegen eine Antragsflut gibt es "Alle ablehnen, die zu keiner Vormerkung passen" (`AntraegeAblehnen`, 4.5).

### 3.5 Freigabe

- Frei gibt nur ein Konto mit `GERAETE_FREIGEBEN`, im Client in einer Admin-Sitzung mit frischer Bestätigung (3.9) per `GeraetEntscheiden`, oder das Admin-Werkzeug. Das gilt für das erste Gerät eines neuen Kontos und für jedes weitere Gerät. Ohne Admin-Sitzung ergibt `GeraetEntscheiden` FREIGEBEN oder ABLEHNEN `ADMIN_SITZUNG_NOETIG`, ohne frischen Code `SCHRITT_BESTAETIGEN`.
- Antrag (3.4, Weg B): Die Freigabeansicht zeigt zusätzlich SC-Name und Discord-Name des Antrags, die davon schon vergebenen SC-Namen (`scNamenVergeben`) und passende Vormerkungen als Vorschlag (gleicher SC-Name oder gleicher Discord-Name nach NFKC, ohne Groß und klein; über den SC-Namen bei genau einem Treffer vorausgewählt). Übernommen wird eine Vormerkung nur ausdrücklich mit `vormerkungId`: Ihr SC-Name wird Hauptname, Rollen und Notiz gehen an das Konto (eine Untergilde seit 0.11.8 nicht mehr), die Vormerkung verschwindet. Ohne Vormerkung kommen nur die noch freien SC-Namen des Antrags in den Namensraum; ein vergebener SC-Name bricht die Freigabe nicht ab. Den Discord-Namen des Antrags bekommt das Konto nur, wenn er frei ist (A53): Trägt ihn schon ein Konto oder eine andere Vormerkung als die übernommene, wird ohne ihn freigegeben, wie bei den SC-Namen, und so gibt sich niemand in "Wer ist da" als ein anderer aus; die Freigabeansicht zeigt das vorher (`discordNameVergeben`). Bis 0.6.x lehnte der Server die Freigabe dann mit `NAME_VERGEBEN` ab. Seit 0.7.0 gilt ein PC, ein Konto (Sarevoc, 04.10.2026): Jeder PC stellt seinen eigenen Antrag und bekommt sein eigenes Konto, und der zweite PC desselben Menschen darf nicht an dessen eigenem Namen scheitern. Mit `kontoId` hängt der Admin das Gerät eines Antrags an ein vorhandenes, aktives Konto; das Antragskonto geht dann darin auf, und der Discord-Name des Kontos bleibt. Der Verwaltungsteil bietet das seit 0.7.0 nicht mehr an, die Konsole weiter (`--an-konto`), etwa für den PC eines Admins mit neuem Geräteschlüssel. An privilegierte Konten geht das nur an der Konsole.
- Geräte desselben Kontos geben nicht frei. Ein freigegebenes Geschwistergerät kann eine Anfrage nur mit BESTAETIGEN oder EINSPRUCH markieren, als Hinweis für den Admin. So kann ein erschlichenes oder kopiertes Gerät weder weitere Geräte freigeben noch neue Geräte des Inhabers ablehnen.
- Die Freigabeansicht zeigt: Discord-Benutzername, Anzeigename, Gerätename, Anzeigeform, Schlüsselart "laut Client, ungeprüft", Zeitpunkt der Verknüpfung, ob es das erste Gerät des Kontos ist, und die Markierung eines Geschwistergeräts. Bei Zweifel fragt der Admin beim Inhaber nach, etwa per Discord, und lässt sich die Anzeigeform vorlesen.
- Privilegierte Konten (6.10): Verknüpfung und Freigabe nur über das Admin-Werkzeug auf dem Server, weder über den Bot noch im Client (`NUR_ADMIN_WERKZEUG`). Für Konten der Stufe RAIDLEITER ist die Verknüpfung über den Bot erlaubt; die Freigabeansicht verlangt dann eine zusätzliche Bestätigung "Rückfrage beim Inhaber erfolgt".
- Nach der Freigabe sendet der Server der wartenden Verbindung `GeraetStatus{FREIGEGEBEN}`, direkt danach `Welcome{status: "FREIGEGEBEN", ...}`, und setzt fort wie bei 4.6 AKTIV_SYNC. Eine Ablehnung ergibt `GeraetStatus{ABGELEHNT}`, dann `Bye{ENTFERNT}` und die Trennung. Seit 0.7.0 (Sarevoc, 04.10.2026: Löschen heißt vollständig entfernen, nichts sperrt von selbst) löscht sie das Gerät samt Fingerabdruck und ein Antragskonto mit seinen Namen; gesperrt wird nichts, und der PC darf später neu beantragen. Das Bye hält den Client davon ab, gleich von selbst wieder zu beantragen (4.6 GETRENNT); erst Verbinden durch den Nutzer stellt einen neuen Antrag.
- Freigegeben wird nie über Discord. Einen Freigabeknopf in Discord gibt es nicht, auch nicht als Option.
- Bootstrap: Der Admin startet den eigenen Client, der den Code zeigt. Auf dem Server verknüpft er mit `funk-admin verknuepfen --code XXXX-XXXX-XXXX --discord <id> --stufe ADMIN`, bestätigt im Client und gibt dann mit `funk-admin freigeben <Anzeigeform>` frei, nachdem er die Anzeigeform im eigenen Client abgelesen hat. Kein Weg führt vom Code direkt zu FREIGEGEBEN.
- Eine automatische Freigabe über eine Discord-Rolle gibt es in v1 nicht (Offener Punkt).

### 3.6 Lebenszyklus: Sperren, Entfernen, Verlust, Fristen

| Vorgang | Wer | Wirkung |
| --- | --- | --- |
| Konto sperren | `KONTEN_VERWALTEN` (6.10) oder Abgleich (3.7) | Status GESPERRT mit Grund. Alle laufenden Sitzungen des Kontos bekommen sofort `Fehler{KONTO_GESPERRT}` und werden getrennt, die UDP-Schlüssel gelöscht. Neue Verbindungen enden nach `Hello` mit demselben Fehler. Die Plätze bleiben für ein späteres Entsperren gespeichert |
| Gerät selbst entfernen | das Gerät selbst | Status ENTFERNT, die laufende Sitzung endet. Der Schlüssel darf sich neu registrieren |
| Anderes Gerät desselben Kontos entfernen | Geschwistergerät | Ist das Zielgerät jünger (später verknüpft): sofort ENTFERNT, nicht dauerhaft. Ist es älter: nur ein Antrag (`BEANTRAGT`), den ein Admin entscheidet. Bei Streit gewinnt das ältere Gerät. Admins und Inhaber bekommen eine Meldung |
| Antrag ablehnen | `GERAETE_FREIGEBEN` (3.5) oder Admin-Werkzeug | seit 0.7.0: Gerät und Antragskonto werden gelöscht, gesperrt wird nichts; der PC darf neu beantragen. Bis 0.6.x: Status ABGELEHNT, Fingerabdruck auf Dauer gesperrt |
| Gerät dauerhaft sperren | nur `GERAETE_FREIGEBEN` oder Admin-Werkzeug | Status ENTZOGEN, der Fingerabdruck bleibt gesperrt, auch nach dem Löschen des Kontos. Eine laufende Sitzung endet sofort. Seit 0.7.0 die einzige Gerätesperre und immer eine bewusste Handlung eines Admins (Sarevoc, 04.10.2026: nichts sperrt von selbst); der Verwaltungsteil bietet sie nicht an, nur die Konsole (`sperren`). Aufheben lässt sie sich seit 0.8.0 (nächste Zeile) |
| Sperre aufheben | seit 0.8.0, wie dauerhaft sperren: `GERAETE_FREIGEBEN` oder Admin-Werkzeug | Ein ENTZOGENES Gerät wird ENTFERNT, sein Fingerabdruck kommt von der Sperrliste. Der PC gilt danach wie unbekannt, darf neu beantragen und braucht wieder eine Freigabe; von selbst kommt er nicht in den Funk. Im Client `GeraetEntscheiden{ENTSPERREN}` (4.5), im Verwaltungsteil auf der Mitgliederkarte. Geschwistergeräte nie über den Client, Geräte eines ADMIN-Kontos nur durch einen ADMIN, Geräte fremder privilegierter Konten und des Eigentümers nur an der Konsole. Die Konsole (`sperre-aufheben`) hebt auch eine Sperre auf, zu der kein Gerät mehr steht (Konto später gelöscht). Journal `SPERRE_AUFGEHOBEN` |
| Gerätewechsel | Nutzer | neues Gerät, neuer Code (3.4), Freigabe durch einen Admin. Das alte Gerät kann sich selbst entfernen, oder das neue beantragt dessen Entfernung |
| Verlust, Windows-Neuinstallation | Nutzer, Admin | Der Schlüssel ist verloren, eine Wiederherstellung gibt es absichtlich nicht. Neues Gerät per Code, Freigabe durch einen Admin. Das alte Gerät entfernt oder sperrt der Admin |
| Zu viele Geräte | - | mehr als 3 Geräte in WARTET oder FREIGEGEBEN: `GERAETE_LIMIT` |
| Warten ohne Entscheidung | - | Geräte in WARTET verfallen nach 24 h (gelöscht) und zählen danach nicht mehr zum Limit |
| Lange nicht benutzt | - | FREIGEGEBEN-Geräte ohne Sitzung seit 90 Tagen fallen auf WARTET zurück und brauchen eine neue Freigabe |
| Gilde verlassen oder Rolle verloren | Abgleich (3.7) | Konto GESPERRT mit Grund ABGLEICH, bei ADMIN-Konten nur Meldung |
| Admin verliert sein Gerät | Admin-Werkzeug auf dem Server | neues Gerät per Code, dann Verknüpfen und Freigabe über die Kommandozeile |
| Löschung (DSGVO) | `KONTEN_VERWALTEN` (6.10) | Konto, Geräte mit Fingerabdrücken, Namen, Pegel, Ablage, Plätze, Admin-Nachweis und Mitgliedsangaben werden gelöscht. Seit 0.7.0 kommt dabei nichts auf die Sperrliste; nur eine bewusste Sperre (Gerät dauerhaft sperren) bleibt. Bis 0.6.x blieben auch die Fingerabdrücke abgelehnter Geräte gesperrt; der Server 0.7.0 verwirft beim ersten Laden einmal jede alte Sperre, zu der kein entzogenes Gerät mehr steht, behält die übrigen als bewusst und schreibt jede freigewordene Gerätekennung ins Journal (`SPERRE_VERWORFEN`) |

Rechte und Status prüft der Server bei jeder Aktion am aktuellen Schnappschuss (6.2), nicht an einem Token. Änderungen wirken also sofort. Aus dem Client gelten alle Handlungen mit `GERAETE_FREIGEBEN` oder `KONTEN_VERWALTEN` nur in einer Admin-Sitzung, die heiklen (sperren, entsperren, löschen, dauerhaft sperren, fremde Geräte entfernen) nur mit frischer Bestätigung (3.9). ADMIN-Konten sperrt ein anderer Admin im Client nur als Notbremse; entsperren, löschen und herabstufen geht nur an der Konsole, das Konto des Eigentümers fasst im Client niemand an (3.10). Geräte eines anderen privilegierten Kontos (also auch anderer Admins) entfernt oder sperrt im Client niemand, das geht nur an der Konsole (`NUR_ADMIN_WERKZEUG`); ein neues Gerät gäbe ohnehin nur die Konsole frei.

### 3.7 Abgleich mit Discord

- Der Bot prüft jedes verknüpfte Konto einzeln per REST (`GET /guilds/{gilde}/members/{discordId}`) auf Gildenmitgliedschaft und freigeschaltete Rolle: alle 6 h und bei jedem `RaidHelperImport`, höchstens alle 30 min. Ob diese Einzelabfrage ohne das privilegierte Intent GUILD_MEMBERS geht, klärt ein Spike (Offener Punkt).
- Er meldet dem Server `Mitgliedschaft{discordId, status: "OK" | "NICHT_MITGLIED" | "ROLLE_FEHLT", benutzername?, name?}`. Nur eindeutige Antworten zählen: "Unknown Member" bzw. Mitglied ohne freigeschaltete Rolle. Fehler, Zeitüberschreitungen und Ratenlimits von Discord ändern nichts.
- Der Server sperrt bei NICHT_MITGLIED oder ROLLE_FEHLT das Konto mit Grund ABGLEICH. Für ADMIN-Konten gibt es nur eine Meldung. Höchstens 3 solche Sperren je Stunde; darüber gibt es nur Meldungen und einen Alarm, damit ein Discord-Fehler oder ein kompromittierter Bot nicht mitten im Einsatz alle aussperrt.
- Meldet ein späterer Abgleich wieder OK, hebt der Server eine Sperre mit Grund ABGLEICH selbst auf. Sperren durch einen Admin bleiben.
- `benutzername` wird bei jedem Abgleich aktualisiert, `name` nur als Vorschlag für den Admin.
- Konten ohne Discord-ID (Antrag, 3.4 Weg B) nehmen am Abgleich nicht teil.

### 3.8 Bot-Dienst und Admin-Werkzeug (serverintern, nicht Teil des Drahtprotokolls)

- Der Bot-Dienst ist ein eigener systemd-Dienst mit eigenem Benutzer. Er baut nur ausgehend die Verbindung zum Discord-Gateway auf. Slash-Befehle kommen über das Gateway, einen HTTP-Interactions-Endpunkt gibt es nicht. Das Token kommt per `LoadCredential`.
- Betrieb des Bots: Im Discord Developer Portal ist "Public Bot" abgeschaltet. Beim Start verlässt der Bot jede Gilde außer der konfigurierten. Er öffnet keinen lauschenden Socket; systemd setzt `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6` und sperrt per `IPAddressDeny` localhost und private Netze (außer dem DNS-Resolver). Discord selbst lässt sich nicht auf feste Adressen eingrenzen. Mindest-Loglevel ist Information, Codes, Tokens und REST-Nutzlasten stehen nie im Log.
- Die Verbindung zum Funk-Server läuft über einen UNIX-Socket mit `SO_PEERCRED`-Prüfung (erlaubte UID), Zeilen-JSON. Operationen:
  - Bot an Server: `Verknuepfen{discordId, benutzername, name, code}` -> `{ergebnis: "OK" | "CODE_UNGUELTIG" | "GRENZE" | "KONTO_GESPERRT" | "NUR_ADMIN_WERKZEUG" | "GERAETE_LIMIT" | "ABGELEHNT" | "FRIST", geraet?: {name, anzeige}}`.
  - Bot an Server: `Mitgliedschaft{discordId, status, benutzername?, name?}` -> `{}`.
  - Server an Bot: `Meldung{ziel: "ADMIN_KANAL" | "DM", discordId?, art, text}`.
  - Die Operation `Entscheidung` aus Entwurf 1 entfällt ersatzlos.
- Grenzen im Server, unabhängig vom Bot: `Verknuepfen` höchstens 5 Fehlversuche und 3 Erfolge je Discord-ID und Stunde, 30 Erfolge je Stunde global; darüber `GRENZE` und ein Alarm. Nie für privilegierte Konten. `Mitgliedschaft` sperrt nie ein ADMIN-Konto und höchstens 3 Konten je Stunde.
- Das Admin-Werkzeug ist eine Kommandozeile auf dem Server über einen zweiten UNIX-Socket, nur für root und den Dienstbenutzer. Befehle: Code verknüpfen (`verknuepfen --code --discord [--stufe]`), Gerät per Anzeigeform oder Fingerabdruck freigeben, entfernen oder sperren (seit 0.8.0 auch die Sperre aufheben, `sperre-aufheben`, auch ohne Gerät), Konto anlegen, sperren, entsperren, Stufe und Rechte setzen, letzten Admin verwalten, signiertes Profil einspielen (prüft die Signatur selbst), Vorrangspur anzeigen und leeren, Messwerte anzeigen, Sicherung. Die Sicherung schließt `$CREDENTIALS_DIRECTORY` aus.
- Seit Stufe 1 dazu: Anträge und Mitglieder lesen (`antraege`, `mitglieder`), Journal lesen (`journal [--konto] [--anzahl]`), SC-Namen setzen (`scnamen --konto --namen A,B`; eine leere Liste, also alle wegnehmen, nur mit `--leeren`, ebenso bei `rechte`), freigeben mit `--vormerkung` oder `--an-konto`, und die Befehle des Eigentümers aus 3.10 (`eigentuemer`, `admin-vorsehen`, `admin-ernennen --kennung`, `admin-einrichtung-neu [--anzeigen]`, `zweitfaktor-bestaetigen --code`, `notfallcodes`, `admin-sperren`, `admin-entsperren`, `admin-zuruecksetzen`, `admin-entziehen [--stufe]`, `admin-schluessel-entfernen --kennung`, `admins`). Jede Handlung der Konsole steht im Journal (Urheber 0). Pflege von Mitgliedern, Rollenkatalog und Untergilden gehört in den Verwaltungsteil des Clients (A43), die Konsole liest sie nur. Offline, ohne laufenden Dienst: `funk-admin totp-schluessel-erzeugen --datei <pfad>` für das Credential `funk-totp-schluessel` (3.9).
- Seit 0.3.0 dazu: Auftrag für einen Abend erteilen (`auftrag --konto <id> [--von HH:MM] [--bis HH:MM] [--rechte A,B] [--grund <text>]`, Zeiten auch als `JJJJ-MM-TT HH:MM`, in Ortszeit Europe/Berlin; ohne `--bis` der nächste Zeitpunkt 04:00 nach dem Beginn), entziehen (`auftrag-entziehen --konto <id>`) und auflisten (`auftraege`); eine Vorlage der Flotte übernehmen (`flotte-vorlage`, als ein Paket nach N5, mit der Liste der Annahmen; bis 0.12.0 die alte Startvorlage, nur auf eine Flotte ohne Einheiten und Rollen; seit 0.13.0 die Vorlage Star Citizen, nächster Punkt). Die Konsole ist Urheber 0 und unterliegt denselben Regeln aus 6.12 wie ein Admin. Die Sicherung lässt alle Aufträge weg (6.12).
- Seit 0.6.0 (Stufe 3, 6.16): ein Kanalnetz anlegen (`netz-setzen ... --kanaele "Ruhe,Schiff 1,..."` oder `--kanaele-datei <datei>`, dazu `--gruppe`; die erste Bezeichnung ist die Ruhe) und eine Kanalvorlage neben die Flotte legen (`flotte-vorlage [--name starcitizen]`, nur solange es außer Global kein Kanalnetz gibt; von 0.6.0 bis 0.12.0 die alte Kanalvorlage, seit 0.13.0 die Vorlage Star Citizen, 6.16). Beides löst das Versionstor aus (4.7).

### 3.9 Admin-Nachweis und Admin-Sitzung

Ein gestohlener Geräteschlüssel eines Admins soll keine Verwaltung sein (T11). Deshalb wirken die Verwaltungsrechte `GERAETE_FREIGEBEN`, `KONTEN_VERWALTEN` und `NETZE_VERWALTEN` nur in einer Admin-Sitzung: Die AKTIV-Sitzung eines ADMIN-Kontos weist sich zusätzlich mit einem eigenen Admin-Schlüssel und einem Code aus der App aus. In jeder anderen Sitzung hat ein ADMIN-Konto nur die Rechte der Führung und `ZUWEISEN`, und Anfragen, die ein Verwaltungsrecht brauchen, ergeben `ADMIN_SITZUNG_NOETIG` ohne Seiteneffekt. Es gibt keinen Schalter, der das abschaltet, auch nicht für Tests.

**Admin-Schlüssel.** ECDSA P-256, im TPM (Microsoft Platform Crypto Provider) ohne Rückfall auf Software, getrennt vom Geräteschlüssel; höchstens 3 je Admin (etwa PC und Laptop). Der Server speichert nur die SPKI. Die Admin-Kennung ist ihre Anzeigeform nach 8.5 (`XXXX-XXXX-XXXX-XXXX`). Derselbe Schlüssel gehört nie zwei Konten.

**Unterschrift** (Vektor 8.14): ECDSA P-256 mit SHA-256, Format IEEE P1363 (64 Byte), über `"tacrelay admin v1"` (ASCII) || `0x00` || Sitzungs-ID (`u32`) || Fingerabdruck des Geräts (32 Byte) || Nonce (32 Byte) || Zweck (ASCII: `ANTRAG`, `GEHEIMNIS`, `EINRICHTUNG` oder `ANMELDUNG`). Die Nonce holt der Client mit `AdminNonce`; sie gilt 60 s und genau einmal, je Verbindung gibt es eine, eine neue ersetzt die offene. So gilt eine Unterschrift weder in einer anderen Sitzung noch auf einem anderen Gerät, noch später, noch für einen anderen Zweck (T12). Ohne gültige Unterschrift zählt kein Code als Versuch, also kann niemand einen Admin durch Raten aussperren.

**Zustände** je Konto: VORGESEHEN (der Eigentümer hat einen Antrag erlaubt), EINRICHTUNG (Schlüssel eingetragen, Geheimnis erzeugt, erster Code fehlt), AKTIV (Admin-Sitzungen möglich), GESPERRT (Konsole oder 10 Fehlversuche in 24 h, bis der Eigentümer an der Konsole entsperrt). Das eigene Konto sieht seinen Zustand in `Welcome.konto.admin` und `Mitgliedschaften.konto.admin`; das Menü "Verwaltung" zeigt der Client nur bei VORGESEHEN, EINRICHTUNG oder AKTIV.

**Einrichtung.**
1. Der Eigentümer sieht das Konto an der Konsole vor: `admin-vorsehen --konto`. Ein eingerichteter Admin bleibt dabei AKTIV (neuer PC).
2. Der Client (Verwaltungsteil) erzeugt den Admin-Schlüssel und sendet `AdminEinrichtung{op: ANTRAG, schluessel: SPKI, unterschrift}`, unterschrieben mit dem neuen Schlüssel für den Zweck ANTRAG (Besitznachweis). Nur für ein vorgesehenes Konto, sonst `KEIN_RECHT`. Die Antwort nennt die Admin-Kennung; jeder Antrag ist eine Meldung `ADMIN_GEAENDERT{op: ANTRAG}` an jedes ADMIN-Konto. Ein neuer Antrag ersetzt den offenen.
3. Der Admin liest die Kennung am Telefon vor, der Eigentümer trägt sie ein: `admin-ernennen --konto --kennung`. Passt sie nicht zum Antrag, ändert sich nichts. Das Konto bekommt die Stufe ADMIN, falls es sie nicht hat. Beim ersten Schlüssel entsteht das TOTP-Geheimnis: Zustand EINRICHTUNG.
4. Binnen 10 min holt der Client das Geheimnis genau einmal ab: `AdminEinrichtung{op: GEHEIMNIS_ABHOLEN}` (Zweck GEHEIMNIS). Antwort `{geheimnis: Base32, uri: otpauth, gueltigBis}`. Der Client zeigt QR-Code und Text für die Authenticator-App und nullt beides danach.
5. Binnen 10 min danach der erste Code: `AdminEinrichtung{op: BESTAETIGEN, code}` (Zweck EINRICHTUNG). Stimmt er, ist der Admin AKTIV, und die Antwort trägt genau einmal 10 Notfallcodes.

Ohne Client geht Schritt 4 und 5 an der Konsole: `admin-einrichtung-neu --konto --anzeigen` zeigt den Schlüssel in Vierergruppen zum Abtippen und den otpauth-Link, `zweitfaktor-bestaetigen --konto --code` schließt ab und zeigt die Notfallcodes. Ein neues Geheimnis entsteht nur an der Konsole (`admin-einrichtung-neu`); es gibt keinen Weg über das Netz, denn Schadcode auf dem Admin-PC bräuchte sonst nur einen mitgelesenen Code für ein eigenes Geheimnis. Ein neues Geheimnis beendet offene Admin-Sitzungen, das alte und die alten Notfallcodes gelten nicht mehr.

**TOTP** nach RFC 6238 mit den Standardwerten, die jede App versteht: HMAC-SHA-1, 6 Ziffern, 30 s, T0 = 0, Geheimnis 160 bit aus `RandomNumberGenerator`, Base32 nach RFC 4648 ohne `=`. URI: `otpauth://totp/JG%20TacRelay:<Hauptname>?secret=<Base32>&issuer=JG%20TacRelay&algorithm=SHA1&digits=6&period=30`, das Etikett prozentkodiert. Eingabe: Leerzeichen und `-` werden übergangen, danach genau 6 ASCII-Ziffern. Geprüft wird der aktuelle Zeitschritt, dann der vorige und der nächste; angenommen wird nur ein Schritt nach dem zuletzt angenommenen. Damit gilt jeder Code genau einmal, und nach einem späteren Schritt nie mehr ein früherer (RFC 6238 5.2). Verglichen wird in konstanter Zeit. Passt dreimal in Folge nur ein Nachbarschritt, trägt die Antwort `uhrHinweis: true` ("Uhr des Telefons prüfen").

**Notfallcodes:** 10 je Admin, Form `XXXX-XXXX-XXXX` mit dem Alphabet und der Normalisierung aus 3.4 und 8.6, je einmal gültig, statt eines Codes aus der App. Gespeichert nur als HMAC-SHA256 mit einem Schlüssel, der per HKDF-SHA256 (Salz `"tacrelay server"`, Info `"tacrelay notfallcode v1"`) aus `funk-totp-schluessel` abgeleitet ist, gebunden an die Konto-ID. Jede Benutzung ist eine Meldung `NOTFALLCODE{uebrig}` an jedes ADMIN-Konto. Neue gibt es nur an der Konsole (`notfallcodes`); die alten verfallen dann.

**Fehlversuche** zählen nur mit gültiger Unterschrift (Anmeldung) oder in einer offenen Admin-Sitzung (Bestätigung). 5 in 15 min ergeben 15 min Pause, 10 in 24 h sperren bis zur Konsole. Beides meldet `ZWEITFAKTOR_GESPERRT` (mit `details.bis` bei der Pause) und eine Meldung an jedes ADMIN-Konto. Ein Erfolg setzt die Zähler nicht zurück. Lässt sich ein Chiffrat nicht öffnen (Credential getauscht), ist das ein Betriebsfehler (`INTERN`, Eintrag im Log), kein Fehlversuch.

**Speicherung** in `admins.json` (nur für den Dienstbenutzer lesbar): je Konto Zustand, Schlüssel mit Kennung, das Geheimnis nur verschlüsselt, der zuletzt angenommene Zeitschritt, die Prüfwerte der Notfallcodes, Fehlversuche, Pause, Sperrgrund (`KONSOLE` oder `FEHLVERSUCHE`) und die Geräte, von denen schon eine Admin-Sitzung ausging. Verschlüsselung (Vektor 8.15): AES-256-GCM mit `funk-totp-schluessel` (32 Byte, per systemd `LoadCredential`, nie in einer Sicherung), je Verschlüsselung eine neue Nonce (96 bit), gespeichert als Nonce || Chiffrat || Tag, Base64. AAD: `"tacrelay totp v1"` (ASCII) || `0x00` || Konto-ID (`u32`) || Generation (`u32`). Die Generation zählt jedes neue Geheimnis des Kontos; so lässt sich kein Chiffrat auf ein anderes Konto oder einen alten Stand umhängen. Entschlüsselt wird nur für die Dauer einer Prüfung, der Puffer wird danach genullt. Geheimnis, Codes und Notfallcodes stehen nie im Log und nie in `ToString`.

**Admin-Sitzung.** `AdminNonce`, dann `AdminAnmeldung{unterschrift, code | notfallcode}` (Zweck ANMELDUNG). Voraussetzungen: Konto ADMIN und AKTIV, Gerät FREIGEGEBEN, Admin-Nachweis AKTIV. Antwort `{bis, bestaetigtBis, uhrHinweis?, notfallcodesUebrig?}`. Die Admin-Sitzung gehört zur Verbindung, ist flüchtig und endet:
- nach 12 h (seit 0.9.1, Sarevoc 08.10.2026, Q54; bis 0.9.0 schon nach 30 min ohne Verwaltungshandlung, die jede Verwaltungsanfrage verlängerte);
- mit der Verbindung, mit `AdminAbmelden` und mit einem Neustart des Servers;
- sofort bei Sperre oder Löschung des Kontos, Sperre oder Entfernung des Geräts, Stufe nicht mehr ADMIN, Sperre, Entzug oder Zurücksetzen des Nachweises, neuem Zweitfaktor und entferntem Schlüssel. Außer bei Trennung und Abmelden bekommt die Verbindung `Meldung{ADMIN_SITZUNG, details: {op: BEENDET, grund}}`.

Geht eine Admin-Sitzung von einem Gerät aus, das noch nie eine hatte, geht `Meldung{ADMIN_SITZUNG, details: {op: BEGONNEN, neuesGeraet: true, geraet}}` an jedes ADMIN-Konto. So bemerkt ein Admin, wenn jemand anderes mit seinem Konto die Verwaltung öffnet.

**Frische Bestätigung.** Heikle Handlungen verlangen einen Code, der höchstens 5 min alt ist: die Anmeldung selbst oder `AdminBestaetigen{code | notfallcode}` in der offenen Admin-Sitzung (außerhalb `ADMIN_SITZUNG_NOETIG`, und der Code zählt nicht). Ohne frischen Code antwortet der Server `SCHRITT_BESTAETIGEN` mit `details.bis` (Ende der Admin-Sitzung); der Client fragt den Code und sendet die Handlung erneut. Die eigenen Regeln einer Handlung prüft der Server vorher: Was ohnehin verboten ist, fragt nicht nach einem Code.

| Handlung | Bedingung |
| --- | --- |
| fremde Geräte und Konten sehen, Journal lesen, Mitglieder und Rollenkatalog pflegen, Name und SC-Namen eines Kontos setzen, Netze, Schiffe und Rollen anlegen und ändern | Admin-Sitzung |
| Gerät freigeben oder ablehnen, Anträge ablehnen, Gerät dauerhaft sperren, Sperre aufheben, fremdes Gerät entfernen | frische Bestätigung |
| Konto eines FUNKER oder RAIDLEITER sperren, entsperren, löschen | frische Bestätigung |
| Konto eines anderen Admins sperren (Notbremse) | frische Bestätigung; entsperren, löschen, Stufe, Rechte, Namen und seine Geräte (entfernen, dauerhaft sperren, Sperre aufheben) nur an der Konsole |
| Stufe FUNKER oder RAIDLEITER setzen, Einzelrechte ohne Verwaltungsrechte | frische Bestätigung, nur für Konten, die nicht privilegiert sind |
| Netze, Schiffe oder Rollen löschen; Stufe 2: Beauftragung erteilen, Flotte übernehmen | frische Bestätigung |
| Stufe ADMIN setzen oder nehmen, Verwaltungsrechte als Einzelrecht, Admin ernennen, entziehen, Zweitfaktor zurücksetzen oder neu einrichten, Eigentümer | nur Konsole |
| Konto des Eigentümers sperren, ändern, löschen, seine Geräte entfernen | nur Konsole; im Client für niemanden |

**Pushes.** Was heute an die Verwaltungsrechte geht (`GeraetStatus` fremder Geräte, `Meldung` an `GERAETE_FREIGEBEN` oder `KONTEN_VERWALTEN`, etwa `ANTRAG_NEU`, `MITGLIED_GEAENDERT`, `RECHTE_GEAENDERT`), geht nur an Admin-Sitzungen. Ausnahme Warnungen: `ADMIN_SITZUNG`, `ZWEITFAKTOR_GESPERRT`, `NOTFALLCODE`, `ADMIN_GEAENDERT`, `ANTRAEGE_VOLL`, `VERDRAENGUNG_HAEUFIG` und `KOPIERVERDACHT` gehen an jede Sitzung eines ADMIN-Kontos, auch ohne Admin-Sitzung. Sie enthalten keine Verwaltungsdaten.

**Journal.** `journal.json` (nur für den Dienstbenutzer lesbar), 90 Tage, höchstens 50.000 Einträge, je Eintrag Zeit, Art, Urheber (Konto-ID, 0 = Konsole), betroffenes Konto und Klartext (höchstens 200 Zeichen, mit Klarnamen): Admin-Sitzungen, Bestätigungen, Fehlversuche, Pausen und Sperren, Notfallcodes, Anträge, Ernennungen, Freigaben und Ablehnungen, Kontoänderungen, Mitglieder, Rollenkatalog, Abzeichen, Verwaltung von Netzen, Schiffen und Rollen, alle Konsolenbefehle. Codes, Geheimnisse und Notfallcodes stehen nie darin. Lesen nur in einer Admin-Sitzung mit `KONTEN_VERWALTEN` (`Journal`, 4.5) und an der Konsole (`journal`). Klarnamen stehen nur im Journal und an der Konsole, nie im Log.

### 3.10 Eigentümer

- Genau ein ADMIN-Konto trägt `eigentuemer` (Sarevoc). Gesetzt wird es nur an der Konsole (`eigentuemer --konto`), ein neuer ersetzt den alten; aus dem Client gibt es keinen Weg dorthin. Ist keiner gesetzt, warnt der Server beim Start. Das Konto des Eigentümers bleibt ADMIN: `stufe` mit einer anderen Stufe und `admin-entziehen` verweigert auch die Konsole, bis ein anderes ADMIN-Konto Eigentümer ist.
- Im Client fasst niemand das Konto des Eigentümers an: sperren, entsperren, Stufe, Rechte, Name, SC-Namen, löschen und das Entfernen seiner Geräte ergeben `NUR_ADMIN_WERKZEUG`, auch für andere Admins mit Admin-Sitzung und frischem Code. Er selbst ändert seine SC-Namen (`ProfilSetzen`), seinen Anzeigenamen und in seiner Admin-Sitzung seinen Discord-Namen (`KontoVerwaltung DISCORDNAME`). Sein Konto fällt nie unter die Löschfrist (3.3).
- Nur der Eigentümer an der Konsole ernennt Admins (vorsehen, dann ernennen mit der vorgelesenen Kennung), richtet den Zweitfaktor ein und neu ein, erzeugt Notfallcodes, sperrt, entsperrt, setzt zurück und entzieht den Admin-Nachweis (`admin-entziehen` stuft auf RAIDLEITER oder FUNKER herab), entfernt Admin-Schlüssel und wechselt den Eigentümer. Die Ernennung im Client mit Vier-Augen gibt es in Stufe 1 nicht.
- Verlust: Telefon weg: Anmeldung mit Notfallcode, bis der Eigentümer `admin-einrichtung-neu` ausführt. Admin-PC weg: neues Gerät per Antrag oder Code, Freigabe an der Konsole (privilegiert), dann vorsehen, Antrag und Ernennung für den neuen Schlüssel, den alten entfernt der Eigentümer. Verdacht auf einen übernommenen Admin: `admin-sperren` an der Konsole oder als Notbremse ein anderer Admin im Client (Konto sperren).

## 4. Steuerkanal

### 4.1 Transport

TCP mit TLS über `SslStream`, ohne Kestrel, ohne HTTP und ohne WebSocket. So gibt es keine HTTP-Oberfläche, und das Framing ist eigenes (4.2).

Server (`SslServerAuthenticationOptions`):
- `ServerCertificateContext`: Server-Identität (3.1).
- `EnabledSslProtocols = Tls13 | Tls12`. TLS 1.2 bleibt nur für Windows-10-Clients, die per Schannel kein TLS 1.3 können (server-sicherheit.md B4). Der Konfigurationsschalter `nurTls13` schaltet es ab, sobald niemand mehr Windows 10 nutzt.
- `CipherSuitesPolicy` (wirkt nur unter Linux, der Server läuft dort): TLS 1.3 `TLS_AES_256_GCM_SHA384`, `TLS_AES_128_GCM_SHA256`, `TLS_CHACHA20_POLY1305_SHA256`; TLS 1.2 nur `TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384` und `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256`.
- `ClientCertificateRequired = true`. `RemoteCertificateValidationCallback` gibt nur `true`, wenn ein Zertifikat mit P-256-Schlüssel vorliegt. Kettenfehler sind egal, ohne Zertifikat wird abgebrochen.
- `ApplicationProtocols = ["tacrelay/1"]`. Ohne vereinbartes ALPN trennt der Server nach dem Handshake.
- `AllowRenegotiation = false`, `AllowTlsResume = false` (jede Verbindung beweist den Geräteschlüssel neu), `CertificateRevocationCheckMode = NoCheck`.
- Handshake-Frist 5 s, danach Hello-Frist 3 s.
- Einstufung direkt nach dem Handshake über den Fingerabdruck des Client-Zertifikats: FREIGEGEBEN mit aktivem Konto heißt "bekannt", alles andere "unbekannt". Unbekannte Verbindungen zählen gegen die Grenzen für Unbekannte (4.8), bekannte nie.
- Socket: `NoDelay = true` (MUSS). Unter Linux `TCP_NOTSENT_LOWAT` auf 16 KiB per `SetRawSocketOption` (Ebene `IPPROTO_TCP` = 6, Option 25). Für Sitzungen im Tunnelmodus zusätzlich ein kleiner `SendBufferSize` (etwa 64 KiB).
- Der Server bindet nur die ausdrücklich konfigurierten Adressen und Ports. Ist die Liste leer, startet er nicht. Zu jeder TCP-Bindadresse gehört eine UDP-Bindadresse mit gleicher IP, sonst startet er nicht. Eine Bindung an alle Adressen gibt es nur mit einer ausdrücklichen Einstellung, samt Warnung im Log.

Client (`SslClientAuthenticationOptions`):
- `TargetHost` ist der Host aus dem Profil (SNI).
- `EnabledSslProtocols = None`: Das Betriebssystem wählt, also Windows 11 TLS 1.3 und Windows 10 TLS 1.2.
- `ClientCertificates`: das Gerätezertifikat, der private Schlüssel liegt im TPM bzw. KSP (Spike: Offene Punkte).
- `RemoteCertificateValidationCallback`: SHA-256(SPKI des Blattzertifikats) muss in `pins` stehen, sonst Abbruch dieser Verbindung. Bei der Erstverbindung versucht der Client danach noch die übrigen Adressen desselben Versuchs (alle Hosts des Profils, 7.1 Schritte 1 und 2); zeigt keine einen gültigen Pin, lautet die Anzeige "Server nicht vertrauenswürdig", ohne Ausnahmeknopf und ohne automatischen Neuversuch. Bis zum Paket "Einrichten und Umzug" brach schon der erste Pin-Fehler ab; im Übergangsprofil eines Umzugs (alter und neuer Host) hätte dann ein fremder Dienst unter einem der Namen jeden Client vom echten Server ferngehalten. Zur Behandlung beim Wiederverbinden siehe 7.1.
- `ApplicationProtocols = ["tacrelay/1"]`, `AllowRenegotiation = false`, `AllowTlsResume = false`.
- Socket: `NoDelay = true` (MUSS), `SendBufferSize` etwa 32 KiB.

### 4.2 Framing

Jeder Rahmen hat diese Form:

```
Offset Laenge Feld
0      4      Laenge       u32, Anzahl der folgenden Bytes (8 + Body)
4      1      Rahmenversion u8, = 1
5      1      Flags        u8, Bit 0 = ANTWORT, Bit 1..7 = 0
6      2      Typ          u16
8      4      Anfrage-Nr   u32
12     n      Body         n = Laenge - 8
```

- Längengrenzen: vor `Hello` höchstens 4096; in REGISTRIERUNG und WARTERAUM höchstens 1032 (Body 1024); in AKTIV_SYNC und AKTIV höchstens 1.048.576 (1 MiB). Die Rechnung, dass jede Nachricht an ihren Höchstwerten hineinpasst, steht in 4.10.
- Eine Länge unter 8 oder über der Grenze des Zustands, eine Rahmenversion ungleich 1 oder gesetzte reservierte Flag-Bits führen zu `Fehler{PROTOKOLL}` bzw. `Fehler{ZU_GROSS}` und zur Trennung. Puffer werden erst nach der Längenprüfung angefordert, aus einem Pool.
- Typbereiche: `0x0001-0x3FFF` JSON-Pflicht, `0x4000-0x7FFF` JSON optional, `0x8000-0xBFFF` binär Pflicht, `0xC000-0xFFFF` binär optional. Ein unbekannter Pflicht-Typ ergibt `PROTOKOLL` und Trennung, ein unbekannter optionaler Typ wird still übergangen.
- Anfrage-Nr: 0 ist ein Push ohne Antwort. Anfragen des Clients zählen von 1 bis `0x7FFFFFFF` streng aufsteigend, Anfragen des Servers haben Bit 31 gesetzt (`0x80000001...`).
- Jede Anfrage bekommt genau eine Antwort: entweder derselbe Typ mit Flag ANTWORT und derselben Anfrage-Nr, oder `Fehler` (`0x0006`) mit Flag ANTWORT und derselben Anfrage-Nr. Eine Antwort ohne offene Anfrage ergibt `PROTOKOLL`.
- Offen sein dürfen je Richtung höchstens 32 Anfragen. Server-Anfragen haben Fristen: `VoiceKeys` 10 s, `VerknuepfungBestaetigen` 120 s. Bei `VoiceKeys` trennt der Server nach Fristablauf; bei `VerknuepfungBestaetigen` gilt die Frist als "nein".
- Jeder Rahmen wird mit genau einem Write-Aufruf geschrieben (Kopf und Body in einem Puffer).
- Senden: Der Server hat je Sitzung eine Schreibschleife mit zwei Warteschlangen. Steuerrahmen gehen vor. Tunnel-Sprache (`0x8001`) wird beim Einreihen mit der monotonen Uhr gestempelt; ein Rahmen, der vor dem Schreiben älter als 60 ms ist, wird verworfen. Je Absender liegen höchstens 3 Tunnel-Sprachrahmen in der Warteschlange; bei einem vierten fällt der älteste dieses Absenders heraus, nie der eines anderen.

### 4.3 Kodierung der JSON-Bodies

- UTF-8, ein JSON-Objekt, `System.Text.Json` mit Source-Generator. Der Sender schreibt kompakt, ohne Leerraum, mit Feldern in Katalogreihenfolge, Nicht-ASCII-Zeichen unmaskiert als UTF-8 (in .NET: `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`). Der Empfänger akzeptiert jede gültige Reihenfolge und beide Schreibweisen von Zeichen.
- Streng lesen: keine Kommentare, keine abschließenden Kommas, Tiefe höchstens 8. Doppelte Schlüssel werden abgelehnt (Prüfpunkt .NET 10, Offene Punkte). Unbekannte Felder werden übergangen, fehlende Pflichtfelder ergeben `UNGUELTIG`. Ein Feld mit dem Wert `null` gilt wie ein fehlendes Feld; Löschen wird immer ausdrücklich ausgedrückt (etwa `leeren: true` oder ID 0).
- Ganzzahlen als JSON-Zahlen ohne Nachkommastellen, höchstens 2^53-1. Kommazahlen nur, wo angegeben (`db`, `jitterMs`, `zielVerzoegerungMs`). Snowflakes (Discord-IDs) als Zeichenkette.
- Zeichenketten in NFC. Steuerzeichen (U+0000-U+001F, U+007F-U+009F) und Bidi-Steuerzeichen (U+202A-U+202E, U+2066-U+2069) sind verboten. Längen zählen Unicode-Skalarwerte.
- Schlüsselmaterial gibt es in JSON nicht mehr; die Sitzungsschlüssel gehen binär (`0x8002`). Base64 (Standardalphabet, mit Auffüllung) nur für Fingerabdrücke, Pins und das signierte Profil.
- Nicht wohlgeformtes JSON ergibt `PROTOKOLL` und Trennung. Ein wohlgeformter, aber ungültiger Inhalt ergibt `UNGUELTIG` ohne Trennung.

### 4.4 Nachrichtenkatalog, Übersicht

A = Anfrage (mit Antwort), P = Push. Rechte nach 6.1 und 6.10. "selbst" heißt: nur für das eigene Konto. `GERAETE_FREIGEBEN`, `KONTEN_VERWALTEN` und `NETZE_VERWALTEN` wirken nur in einer Admin-Sitzung (3.9), heikle Handlungen nur mit frischer Bestätigung. Alle übrigen Rechte zählen zum Zeitpunkt der Anfrage, mit einem laufenden Auftrag (6.12).

| Typ | Name | Richtung | Art | Zustand | Recht |
| --- | --- | --- | --- | --- | --- |
| 0x0001 | Hello | C->S | P | WARTE_HELLO | - |
| 0x0002 | Welcome | S->C | P | nach Hello, nach Freigabe | - |
| 0x0003 | Bye | beide | P | alle | - |
| 0x0004 | Ping | C->S | A | ab REGISTRIERUNG | - |
| 0x0006 | Fehler | beide | Antwort oder P | alle | - |
| 0x0010 | Registrierung | C->S | A | REGISTRIERUNG | - |
| 0x0011 | GeraetStatus | S->C | P | REGISTRIERUNG, WARTERAUM, AKTIV | - |
| 0x0012 | GeraeteListe | C->S | A | AKTIV | selbst, fremd `GERAETE_FREIGEBEN` |
| 0x0013 | GeraetEntscheiden | C->S | A | AKTIV | `GERAETE_FREIGEBEN`; selbes Konto nur Markierung; ENTSPERREN (seit 0.8.0) wie dauerhaft entfernen |
| 0x0014 | GeraetEntfernen | C->S | A | AKTIV | selbst, Geschwister nach 3.6, dauerhaft nur `GERAETE_FREIGEBEN` |
| 0x0015 | KontoVerwaltung | C->S | A | AKTIV | `KONTEN_VERWALTEN` (6.10) |
| 0x0016 | VerknuepfungBestaetigen | S->C | A (Server-Anfrage) | REGISTRIERUNG | - |
| 0x0017 | Beauftragung | C->S | A | AKTIV | ERTEILEN, ENTZIEHEN `KONTEN_VERWALTEN` mit frischer Bestätigung; LISTE `KONTEN_VERWALTEN`; ABGEBEN selbst (6.12) |
| 0x0018 | ProfilSetzen | C->S | A | AKTIV | selbst |
| 0x0019 | Journal | C->S | A | AKTIV | `KONTEN_VERWALTEN` |
| 0x001A | Rollenkatalog | C->S | A | AKTIV | LESEN alle, SETZEN `KONTEN_VERWALTEN`; nur ergänzen auch `ZUWEISEN` oder `GLOBAL_SENDEN` (6.13) |
| 0x001B | Mitglied | C->S | A | AKTIV | `KONTEN_VERWALTEN` |
| 0x001C | Lieblingsrollen | C->S | A | AKTIV | selbst |
| 0x001D | Untergilde (entfallen seit 0.11.8, Nummer reserviert) | C->S | A | AKTIV | alle: LISTE leer, SETZEN und LOESCHEN `UNGUELTIG` |
| 0x001E | Mitgliedskarte | C->S | A | AKTIV | alle, Umfang nach 6.13 |
| 0x001F | AntraegeAblehnen | C->S | A | AKTIV | `GERAETE_FREIGEBEN`, frische Bestätigung |
| 0x0020 | Zustand | C->S A, S->C P | A/P | AKTIV_SYNC, AKTIV | - |
| 0x0021 | NetzListe | S->C | P | AKTIV_SYNC, AKTIV | - |
| 0x0022 | Mitgliedschaften | S->C | P | AKTIV_SYNC, AKTIV | - |
| 0x0023 | Praesenz | S->C | P | AKTIV_SYNC, AKTIV | Details nur `ZUWEISEN` |
| 0x0024 | SendeFehler | S->C | P | AKTIV | - |
| 0x0025 | Abzeichen | C->S | A | AKTIV | `KONTEN_VERWALTEN` (6.13, seit 0.11.7) |
| 0x0026 | MeldungSenden | C->S | A | AKTIV | Haken "darf Meldungen senden" und Stufe RAIDLEITER oder ADMIN oder laufender Auftrag, nicht im Funker-Modus (6.18, seit 0.12.0) |
| 0x0027 | BereitMelden | C->S | A | AKTIV | alle, solange die Bereit-Abfrage offen ist (6.18, seit 0.12.0) |
| 0x0030 | SendeauswahlSetzen | C->S | A | AKTIV | selbst |
| 0x0031 | HoerenSetzen | C->S | A | AKTIV | selbst |
| 0x0032 | EinsteigenRolleSchiff | C->S | A | AKTIV | selbst (Selbstwahl) oder `ZUWEISEN` |
| 0x0033 | Verschieben | C->S | A | AKTIV | `ZUWEISEN` |
| 0x0034 | Stummschalten | C->S | A | AKTIV | `ZUWEISEN` |
| 0x0035 | FeldSetzen | C->S | A | AKTIV | selbst: Ch.-Wechsel im selben Kanalnetz (auch im festen Feld), offene Netze in freie Felder (6.16, seit 0.8.1) |
| 0x0036 | SatzAnwenden | C->S | A | AKTIV | selbst |
| 0x0037 | PlaetzeFreigeben | C->S | A | AKTIV | `ZUWEISEN` |
| 0x0038 | KontenVerzeichnis | C->S | A | AKTIV | `ZUWEISEN` |
| 0x0039 | RolleWaehlen | C->S | A | AKTIV | selbst, Rolle `selbstWaehlbar`, ohne Sperre der Selbstwahl (6.16, seit 0.6.0) |
| 0x003A | KanalSperre | C->S | A | AKTIV | `ZUWEISEN`; ein ADMIN-Konto nur durch einen ADMIN (6.17, seit 0.6.0) |
| 0x003C | Funkermodus | C->S | A | AKTIV | Stufe ADMIN, nur das eigene Konto, ohne Admin-Sitzung (6.15) |
| 0x003D | PhaseSchalten | C->S | A | AKTIV | `ZUWEISEN` (6.14) |
| 0x0040 | AufstellungSpeichern | C->S | A | AKTIV | `ZUWEISEN`; fremde ändern nur Ersteller oder ADMIN |
| 0x0041 | AufstellungListe | C->S | A | AKTIV | `ZUWEISEN` |
| 0x0042 | AufstellungAnwenden | C->S | A | AKTIV | `ZUWEISEN` |
| 0x0043 | AufstellungLoeschen | C->S | A | AKTIV | `ZUWEISEN`; fremde nur Ersteller oder ADMIN |
| 0x0044 | RaidHelperImport | C->S | A | AKTIV | `ZUWEISEN` |
| 0x0045 | RegelTabelle | C->S | A | AKTIV | `ZUWEISEN` |
| 0x0046 | RaidHelperEvents | C->S | A | AKTIV | `ZUWEISEN` |
| 0x0047 | AufstellungGeaendert | S->C | P | AKTIV | an `ZUWEISEN` |
| 0x0050 | DurchsageStart | S->C | P | AKTIV | - |
| 0x0051 | DurchsageEnde | S->C | P | AKTIV | - |
| 0x0052 | SprechanzeigeAbo | C->S | A | AKTIV | `SPRECHANZEIGE_ALLE` |
| 0x0053 | Sprechanzeige | S->C | P | AKTIV | Abo |
| 0x0060 | PegelVoreinstellungen | S->C P, C->S A (lesen) | A/P | AKTIV_SYNC, AKTIV | selbst |
| 0x0061 | PegelSetzen | C->S | A | AKTIV | selbst |
| 0x0062 | Ablage | C->S | A | AKTIV | selbst |
| 0x0070 | (frei, in Entwurf 1 VoiceKeys, nicht verwenden) | - | - | - | - |
| 0x0071 | KeyRotation | C->S | A | AKTIV | - |
| 0x0072 | UdpStatus | S->C | P | AKTIV | - |
| 0x0073 | TunnelModus | C->S | A | AKTIV | - |
| 0x0074 | EmpfangsBericht | C->S | P | AKTIV | - |
| 0x0075 | VerlustHinweis | S->C | P | AKTIV | - |
| 0x0080 | Verwaltung | C->S | A | AKTIV | `NETZE_VERWALTEN`, LOESCHEN und PAKET mit frischer Bestätigung (seit 0.5.0 auch PHASE_LOESCHEN); SELBSTWAHL auch `ZUWEISEN` (auch aus einem Auftrag) ohne Admin-Sitzung |
| 0x0090 | AdminNonce | C->S | A | AKTIV | Konto mit Admin-Nachweis |
| 0x0091 | AdminAnmeldung | C->S | A | AKTIV | ADMIN mit Admin-Nachweis AKTIV |
| 0x0092 | AdminBestaetigen | C->S | A | AKTIV | offene Admin-Sitzung |
| 0x0093 | AdminAbmelden | C->S | A | AKTIV | offene Admin-Sitzung |
| 0x0094 | AdminEinrichtung | C->S | A | AKTIV | Konto mit Admin-Nachweis (3.9) |
| 0x4001 | ServerLast | S->C | P, optional | AKTIV | an `ZUWEISEN` und Admins |
| 0x4002 | Meldung | S->C | P, optional | AKTIV | je Art |
| 0x4003 | Belegung | S->C | P, optional | AKTIV_SYNC, AKTIV | - |
| 0x4004 | PhaseGeschaltet | S->C | P, optional | AKTIV_SYNC, AKTIV | - |
| 0x4005 | Meldestand | S->C | P, optional | AKTIV_SYNC, AKTIV | an alle (6.18, seit 0.12.0) |
| 0x4006 | BereitStand | S->C | P, optional | AKTIV_SYNC, AKTIV | an alle, die Meldungen senden dürfen (6.18, seit 0.12.0) |
| 0x8001 | SpracheTunnel | beide | P, binär | AKTIV | - |
| 0x8002 | VoiceKeys | S->C | A (Server-Anfrage), binär | AKTIV_SYNC, AKTIV | - |

### 4.5 Nachrichten im Einzelnen

Schreibweise: `feld: typ` und dazu die Grenze. `?` heißt optional.

**0x0001 Hello** (C->S, erster Rahmen nach TLS, binnen 3 s)
- `minor: u16`: höchste unterstützte Nebenversion (v1.0 = 0)
- `client: {name: str(32), version: str(32) SemVer}`
- `schluesselArt: "TPM" | "SOFTWARE"`
- `opus: str(16)`: libopus-Version, z. B. `"1.6.1"`
- `faehigkeiten: [str(32)]` höchstens 16; bekannt ist `"tunnel"`

**0x0002 Welcome** (S->C)
- immer: `minor: u16` (vereinbart = Minimum beider Seiten), `status: "UNBEKANNT" | "WARTET" | "FREIGEGEBEN"`
- bei UNBEKANNT zusätzlich nur `registrierung: {codeLaenge: 12, gueltigSek: 300}`. Es gibt keine Serverversion, keine Namen und keine Zahlen über Konten oder Netze.
- bei WARTET: `konto: {id, name, benutzername, scNamen?, discordName?}`, `geraet: {id, name, anzeige}`. Bei einem Antrag (3.4, Weg B) stehen dort die beantragten Namen.
- bei FREIGEGEBEN zusätzlich: `sitzungsId: u32`, `konto: {id, name, benutzername, stufe, rechte: [str], scNamen?: [str] 1..3, discordName?, admin?: "VORGESEHEN" | "EINRICHTUNG" | "AKTIV" | "GESPERRT", adminAntrag?: true, eigentuemer?: true, beauftragung?: {von: i64, bis: i64, rechte: [str], durch: kontoId (0 = Konsole), grund?: str(48), aktiv: bool}}` (`admin`, `adminAntrag`, `eigentuemer` nur für das eigene Konto, 3.9, 3.10; `rechte` nennt die dauerhaften Rechte des Kontos, die Verwaltungsrechte darin wirken nur in einer Admin-Sitzung; `beauftragung` ist ein laufender oder geplanter Auftrag, 6.12, dessen Rechte mit `aktiv` dazukommen; seit 0.5.0 `alsFunkerBis?: i64`, solange das Konto "Heute als Funker" spielt, 6.15), `server: {version}`, `udp: {port: u16 (öffentlicher Port), maxDatagramm: 1200}`, `serverZeit: i64`, `mindestClientVersion: str`, `profil: {daten: b64, signatur: b64}` (das aktuelle, offline signierte Profil, 3.1), `zustandsVersion: i64`, `grenzen: {rahmenMax: 1048576, opusMax: 400, zieleNetzMax: 16, zieleDurchsageMax: 64, pingMs: 2000, totMs: 6000, ruheMs: 10000, keepaliveMs: 2000}`
- Liegt die Client-Version unter `mindestClientVersion`, sendet der Server statt Welcome `Fehler{VERSION_ZU_ALT, details: {mindest}}` und trennt.

**0x0003 Bye** (beide): `grund: "BEENDET" | "WARTUNG" | "VERDRAENGT" | "ERSETZT" | "GESPERRT" | "ENTZOGEN" | "ENTFERNT" | "WARTEZEIT" | "PROTOKOLL"`, `wiederInMs?: u32` (bei WARTUNG und WARTEZEIT), `durch?: {geraetName: str(40), anzeige: str(19)}` (bei VERDRAENGT). Danach TLS `close_notify` und Trennung.

**0x0004 Ping** (C->S Anfrage, alle 2 s ab Hello, unabhängig vom übrigen Verkehr): `nr: u32`. Antwort: `nr`, `udpEmpfangen: i64` (Zahl gültiger UDP-Datagramme hoch von dieser Sitzung seit Beginn), `udpGesendet: i64` (Zahl der UDP-Datagramme runter an diese Sitzung seit Beginn), `serverZeit: i64`. Beide Zähler dienen der Ausfallerkennung (5.12).

**0x0006 Fehler**: `code: str` (4.9), `text?: str(200)`, `bezugTyp?: u16`, `details?: Objekt` höchstens 4 KiB. Bei einem schweren Code (4.9) folgt die Trennung.

**0x0010 Registrierung** (C->S): `geraetename: str(40)`, `scName?: str(32)`, `discordName?: str(32)` (die beiden Namen nur zusammen, 3.4 Weg B). `geraetename` ist seit JG TacRelay 0.6.3 `PC-` und die ersten vier Zeichen der Anzeigeform (8.5), etwa `PC-5CD2`, nie der Name des Windows-PCs (Sarevoc, 04.10.2026); bis 0.6.2 war es `Environment.MachineName`, und so heißen ältere Geräte auf dem Server weiter. Der Server nimmt jeden gültigen Text an. Antwort ohne Namen: `code: str(14)` (Anzeigeform `XXXX-XXXX-XXXX`), `gueltigBis: i64`, `befehl: str(40)` (`"/funk verknuepfen"`). Antwort mit Namen: `{antrag: true}`, danach `GeraetStatus{WARTET}` und WARTERAUM. Fehler: `GRENZE`, `SERVER_VOLL`, `ANTRAEGE_VOLL`, `UNGUELTIG` (Namen verletzen 3.3).

**0x0011 GeraetStatus** (S->C): `geraetId: u32`, `kontoId: u32`, `kontoName: str`, `benutzername: str` (`-` ohne Discord-Anbindung, 3.3), `status: "WARTET" | "FREIGEGEBEN" | "ABGELEHNT" | "ENTFERNT" | "ENTZOGEN" | "BEANTRAGT"`, `anzeige: str(19)`, `fingerabdruck: b64`, `schluesselArt` (laut Client, ungeprüft), `erstesGeraet: bool`, `markierung?: "BESTAETIGT" | "EINSPRUCH"`, `grund?: str(40)`, `durch?: kontoId`, `scName?: str(32)` (beim Antrag der beantragte, sonst der Hauptname), `discordName?`, `antrag?: true`, `scNamenVergeben?: [str]` (Namen des Antrags, die schon ein Konto trägt), `discordNameVergeben?: true` (den Discord-Namen des Antrags trägt schon ein Konto oder eine Vormerkung; nur an Admin-Sitzungen, A53), `vormerkungen?: [{id, scName, discordName?, rollen: [u16], untergilde?, ueberScName: bool}]` höchstens 8 (nur an Admin-Sitzungen, 3.5; `untergilde` füllt der Server seit 0.11.8 nicht mehr). Empfänger: Admin-Sitzungen mit `GERAETE_FREIGEBEN` und Sitzungen desselben Kontos; eine Verbindung in REGISTRIERUNG oder WARTERAUM nur für ihr eigenes Gerät.

**0x0012 GeraeteListe** (C->S): `kontoId?: u32`, `nurWartend?: bool`. Antwort: `geraete: [{id, kontoId, kontoName, benutzername, name, anzeige, fingerabdruck, schluesselArt, status, markierung?, verknuepft: i64, zuletzt: i64?, scName?, discordName?, antrag?, scNamenVergeben?, discordNameVergeben?, vormerkungen?}]`, höchstens 500. Fremde Konten nur in einer Admin-Sitzung, sonst nur die eigenen Geräte.

**0x0013 GeraetEntscheiden** (C->S): `geraetId`, `entscheidung: "FREIGEBEN" | "ABLEHNEN" | "BESTAETIGEN" | "EINSPRUCH" | "ENTSPERREN"`, `vormerkungId?: u32`, `kontoId?: u32` (höchstens eins von beiden, nur bei FREIGEBEN, 3.5). FREIGEBEN und ABLEHNEN nur mit `GERAETE_FREIGEBEN` in einer Admin-Sitzung mit frischer Bestätigung und nur für nicht privilegierte Konten (sonst `NUR_ADMIN_WERKZEUG`). ABLEHNEN löscht seit 0.7.0 das Gerät und ein Antragskonto (3.5). BESTAETIGEN und EINSPRUCH nur von einem FREIGEGEBEN-Gerät desselben Kontos; sie setzen nur die Markierung. ENTSPERREN (seit 0.8.0) hebt die Sperre eines ENTZOGENEN Geräts auf (3.6), mit den Rechten von `GeraetEntfernen` mit `dauerhaft`: `GERAETE_FREIGEBEN` in einer Admin-Sitzung mit frischer Bestätigung, keine Geschwistergeräte, Geräte eines ADMIN-Kontos nur durch einen ADMIN, fremde privilegierte Konten und der Eigentümer nur über das Admin-Werkzeug (`NUR_ADMIN_WERKZEUG`); das Gerät wird ENTFERNT, ein Gerät ohne Sperre ergibt `UNGUELTIG`. Ein Client stellt ENTSPERREN nur an Server ab 0.8.0 (4.7). Antwort `{}`.

**0x0014 GeraetEntfernen** (C->S): `geraetId`, `dauerhaft: bool`, `grund: str(40)`. `dauerhaft = true` nur mit `GERAETE_FREIGEBEN` (Status ENTZOGEN). Sonst nach 3.6: das eigene Gerät oder ein jüngeres Geschwistergerät wird ENTFERNT, ein älteres Geschwistergerät wird nur BEANTRAGT. Fremde Geräte privilegierter Konten und des Eigentümers nur über das Admin-Werkzeug (`NUR_ADMIN_WERKZEUG`, 3.10, 6.10). Antwort `{status: "ENTFERNT" | "ENTZOGEN" | "BEANTRAGT"}`. Wirkt sofort.

**0x0015 KontoVerwaltung** (C->S): `op: "SPERREN" | "ENTSPERREN" | "STUFE" | "RECHTE" | "NAME" | "LOESCHEN" | "SCNAMEN" | "DISCORDNAME"`, `kontoId`, `wert?` (SCNAMEN: `[str]` 0 bis 3, eindeutig nach 3.3, sonst `NAME_VERGEBEN` mit `details.name`; DISCORDNAME: `str(32)`, leer, null oder fehlend leert ihn, eindeutig nach 3.3, sonst `NAME_VERGEBEN` mit `details.name`, A53). Regeln in 6.10 und 3.9; SCNAMEN und DISCORDNAME brauchen die Admin-Sitzung, aber keine frische Bestätigung. Antwort `{}`.

**0x0016 VerknuepfungBestaetigen** (S->C Server-Anfrage, Frist 120 s): `konto: {name, benutzername, neu: bool}`. Antwort `{ja: bool}`. Ablauf in 3.4.

**0x0017 Beauftragung** (C->S, 6.12, Entwurf zur Raidleitung N1): `op: "ERTEILEN" | "ENTZIEHEN" | "ABGEBEN" | "LISTE"`, `kontoId?: u32` (Pflicht bei ERTEILEN und ENTZIEHEN), `von?: i64`, `bis?: i64` (Unix-ms), `rechte?: [str]`, `grund?: str(48)`. ERTEILEN legt einen Auftrag an oder ersetzt den des Kontos (verlängern); `von` fehlt oder liegt zurück: jetzt; `bis` fehlt: der nächste Zeitpunkt 04:00 Uhr Ortszeit (Europe/Berlin) nach dem Beginn; `rechte` fehlt: alle sechs der Einsatzleitung. ERTEILEN, ENTZIEHEN und LISTE nur mit `KONTEN_VERWALTEN` in einer Admin-Sitzung (sonst `KEIN_RECHT` bzw. `ADMIN_SITZUNG_NOETIG`); ERTEILEN und ENTZIEHEN prüfen erst die Regeln aus 6.12 (`UNGUELTIG`, `KEIN_RECHT`, `UNBEKANNT`, mit Klartext in `text`) und verlangen dann eine frische Bestätigung (`SCHRITT_BESTAETIGEN`). ABGEBEN gibt der Beauftragte selbst ("Abend beenden"), ohne Auftrag `UNBEKANNT`. Antwort `{beauftragungen: [{kontoId, name, scName?, von, bis, rechte, durch, seit, grund?, aktiv}]}` höchstens 4, die laufenden und geplanten, nach dem Beginn geordnet; bei ABGEBEN leer.

**0x0018 ProfilSetzen** (C->S, selbst): `scNamen?: [str(32)] 1..3` (Hauptname zuerst), `discordName?: str(32)`, `zeichenZeigen?: bool` ("Mein Zeichen zeigen", A49), `karteFreigeben?: bool` ("Meine Angaben in Wer ist da zeigen", Antwort 3, Standard an), seit 0.11.7 `abzeichenVorn?: str(16)` (das eine Abzeichen vor dem Namen; "" = keine Wahl) und `abzeichenAus?: [str(16)]` höchstens 64 (die ausgeblendeten Abzeichen, ersetzt die Liste). Erlaubt sind in beiden Katalogkennungen und `ADMIN`/`EINSATZLEITUNG`; besitzen muss man sie nicht, gezeigt wird ohnehin nur Besessenes. Eine andere Kennung (auch `FUEHRUNG`) ergibt `UNGUELTIG` mit Klartext, und dann ändert sich auch sonst nichts. Nicht angegebene Felder bleiben. Antwort `{scNamen, discordName?, zeichenZeigen, karteFreigeben, abzeichenVorn?, abzeichenAus?}`, bereinigt nach 3.3. Ein SC-Name, den ein anderes Konto oder eine Vormerkung trägt, ergibt `NAME_VERGEBEN` mit `details: {name}`. Den Discord-Namen ändert `ProfilSetzen` seit A53 nicht mehr (3.3): `discordName` mit dem bisherigen Wert wird geduldet, ein anderer ergibt `KEIN_RECHT`, und dann ändert sich auch sonst nichts. Clients schicken das Feld nicht mehr.

**0x0019 Journal** (C->S, `KONTEN_VERWALTEN` in einer Admin-Sitzung): `vor?: i64` (nur ältere Einträge), `kontoId?: u32`. Antwort `{eintraege: [{zeit: i64, art: str(32), durch: u32 (0 = Konsole), kontoId?, text: str(200)}]}`, neueste zuerst, höchstens 200 (3.9).

**0x001A Rollenkatalog** (C->S): `op: "LESEN" | "SETZEN"`, `eintraege?: [{id: u16 1..65534, name: str(40), gruppe?: str(20), rollen?: [u16] (Funkrollen-Vorschlag) höchstens 16, raidHelper?: [{className, specName}] höchstens 8}]` höchstens 64, `version?: i64`. LESEN darf jedes Konto, SETZEN ersetzt den ganzen Katalog (6.13); ohne Admin-Sitzung nur zum Ergänzen und nur mit `ZUWEISEN` oder `GLOBAL_SENDEN` (Antwort 4, 6.13). Antwort `{eintraege, version}`; eine abweichende `version` ergibt `VERALTET`.

**0x001B Mitglied** (C->S, `KONTEN_VERWALTEN` in einer Admin-Sitzung): `op: "LISTE" | "VORMERKEN" | "AENDERN" | "LOESCHEN" | "EINFUEGEN"`, dazu je nach `op` `kontoId?`, `vormerkungId?` (AENDERN genau eins von beiden, LOESCHEN nur Vormerkungen), `scName?`, `discordName?`, `rollen?: [u16]` höchstens 16, `untergilde?: u16` (seit 0.11.8 übergangen, nur für ältere Verwaltungen), `notiz?: str(80)`, `zeilen?: [str(200)]` höchstens 200 (EINFUEGEN, Form in 6.13), `ab?: u32` (LISTE: ab diesem Eintrag), seit 0.12.0 `meldungenSenden?: bool` (nur AENDERN eines Kontos: Haken "darf Meldungen senden" setzen oder wegnehmen, 6.18; fehlt = bleibt; bei einer Vormerkung `UNGUELTIG`; Journal `MELDUNGEN`, wenn er sich ändert; ein Server vor 0.12.0 übergeht das Feld still, die Verwaltung schickt es deshalb nur an Server ab 0.12.0). Antwort `{mitglieder?, ids?, zeilen?, weiter?, hinweis?}`: bei LISTE `mitglieder: [{kontoId? | vormerkungId?, name, scNamen, discordName?, benutzername?, untergilde? (seit 0.11.8 nie gefüllt), rollen, lieblingsrollen?, ersteWahl?, stufe?, zustand: "ONLINE" | "FREIGEGEBEN" | "ANTRAG" | "VORGEMERKT" | "GESPERRT", zuletzt?, notiz?, eigentuemer?, admin?, raenge?: [str(16)] höchstens 3, taetigkeiten?: [str(16)] höchstens 6, meldungenSenden?: true}]` (die Abzeichen seit 0.11.7 vollständig, auch ausgeblendete; sie fehlen, wenn das Konto keine hat; `meldungenSenden` seit 0.12.0 nur bei gesetztem Haken, auch bei einem Funker, bei dem er nicht wirkt), erst die Konten, dann die Vormerkungen, seitenweise zu höchstens 250; gibt es mehr, nennt `weiter` den Wert für `ab` der nächsten Seite; sonst die angelegten oder geänderten `ids`. Passt bei EINFUEGEN eine Zeile nicht, wird nichts angelegt, und die Antwort nennt jede fehlerhafte Zeile: `zeilen: [{zeile: u16, grund: str(100)}]`, `ids` leer. Seit 0.11.8 trägt eine gelungene EINFUEGEN-Antwort `hinweis?: str(100)`, wenn in einer Zeile etwas in Spalte 3 stand ("Spalte 3 (Untergilde) wird seit 0.11.8 übergangen", einmal je Liste); ältere Verwaltungen übergehen das Feld.

**0x001C Lieblingsrollen** (C->S, selbst): `rollen: [u16]` höchstens 8 aus dem Katalog, `ersteWahl?: u16` (aus der Liste oder aus den eingetragenen Rollen). Antwort `{rollen, ersteWahl?}`; Rollen, die der Admin schon eingetragen hat, fallen aus den Wünschen weg.

**0x001D Untergilde** (C->S), seit 0.11.8 entfallen, Nummer reserviert (Sarevoc: "Untergilden entfallen ganz, überall"): Der Typ bleibt als Hülle im Katalog, denn Clients bis 0.11.7 rufen LISTE beim Öffnen von "Wer ist da", des Bretts und der Verwaltung, und ein unbekannter Typ trennte sie. Felder unverändert: `op: "LISTE" | "SETZEN" | "LOESCHEN"`, `id?: u16`, `name?: str(32)`, `kuerzel?: str(4)`, `konten?: [u32]`, `vormerkungen?: [u32]` zusammen höchstens 40, `stammEinheit?: u16`, `ansprechpartner?: u32`. LISTE beantwortet der Server jedem Konto mit `{untergilden: []}`, SETZEN und LOESCHEN jedem mit `UNGUELTIG` und dem Text "Untergilden gibt es seit 0.11.8 nicht mehr", ohne Seiteneffekt und ohne Trennung. Antwortform `{untergilden?: [{id, name, kuerzel, konten: [{kontoId, name}], vormerkungen?, stammEinheit?, ansprechpartner?}], id?}` wie bis 0.11.7. Clients ab 0.11.8 schicken 0x001D nicht mehr.

**0x001E Mitgliedskarte** (C->S): `kontoId`. Antwort `{kontoId, name, scName?, scNamen?, discordName?, untergilde? (seit 0.11.8 nie gefüllt), rollen?, lieblingsrollen?, ersteWahl?, online, schiffId?, rolleId?, platz?, letzterPlatz?: {schiffId?, rolleId?}, zeichen: [str], verbindung?, guete?, freigegeben?: bool, abzeichen?: [str(16)] höchstens 9}`; welche Felder gefüllt sind, regelt 6.13. `abzeichen` (seit 0.11.7) sind die sichtbaren Abzeichen mit denselben Sperren wie `zeichen`; das Konto selbst sieht alle eigenen, andere weder ausgeblendete Abzeichen noch ausgeblendete Zeichen. `freigegeben` sagt, ob der Inhaber seine Angaben freigibt (Antwort 3); für die Einsatzleitung, Admins und das Konto selbst steht dort `true`. Andere bekommen nur Karten von Konten in der Praesenz, sonst `UNBEKANNT`.

**0x001F AntraegeAblehnen** (C->S, `GERAETE_FREIGEBEN` in einer Admin-Sitzung mit frischer Bestätigung): `nurOhneVormerkung: true`. Lehnt jedes wartende Gerät eines Antrags ab, zu dem keine Vormerkung passt (3.5), mit `GeraetStatus{ABGELEHNT}`, `Bye{ENTFERNT}` und Trennung; seit 0.7.0 sind diese Anträge danach gelöscht, gesperrt wird nichts. Antwort `{anzahl: u16}`.

**0x0020 Zustand**: Als C->S-Anfrage (Body `{}`) bittet der Client um den vollständigen Stand. Der Server schickt dann `NetzListe`, `Mitgliedschaften`, `Praesenz{voll: true}` und `PegelVoreinstellungen` und beantwortet die Anfrage zuletzt mit `{zustandsVersion}`. In AKTIV_SYNC schickt der Server dieselbe Folge ungefragt und schließt mit dem Push `Zustand{zustandsVersion}`. So weiß der Client, dass der Stand vollständig ist. Der Client fordert Zustand höchstens einmal je `zustandsVersion` bzw. NetzListe-Version und höchstens alle 5 s an.

**0x0021 NetzListe** (S->C, beim Start und nach jeder Verwaltungsänderung vollständig):
- `version: i64`
- `einstellungen: {durchsageNurTaste: bool, selbstwahl: bool, selbstwahlDurch?: kontoId, selbstwahlBis?: i64, nachzuegler?: bool}`. `selbstwahl` sagt, ob Funker Rolle und Schiff gerade selbst wählen dürfen (die Einsatzleitung kann das im Einsatz sperren). Bei einer Sperre nennt `selbstwahlDurch` den Urheber (0 = Konsole oder ein Stand von vor 0.3.0) und `selbstwahlBis` ihr Ende (12 h nach dem Setzen, fehlt bei Ständen von vor 0.3.0); `nachzuegler` (fehlt: true) heißt "Wechsel sperren": Wer noch keinen Platz hat, nimmt weiter einen freien; false ist "Ganz sperren" (6.9). `durchsageNurTaste` (Standard true) verbietet Durchsagen per Sprachaktivierung.
- `netze: [{id: u16, name: str(32), typ: "GLOBAL" | "NORMAL", reihenfolge: u16, nurTaste: bool, ausloeserStandard: "VA" | "TASTE", offen: "NEIN" | "HOEREN" | "HOEREN_SENDEN", standard: bool, schiffId?: u16, schiffsnetz?: str(16), kanaele?: [str(20)], gruppe?: str(20), herkunft?: str(32)}]`, höchstens 128. Seit 0.6.0: `kanaele` macht das Netz zum Kanalnetz (6.16), 2 bis 31 nicht leere Bezeichnungen, Index 0 ist die Ruhe ("Ruhe"), Index 1 bei Global ist Orgaweit; nur für Netz-IDs bis 1022, höchstens 64 Kanalnetze. `gruppe` ordnet die Kanalwahl (etwa "Schiffe", "Boden"). Fehlt `kanaele`, ist es ein gewöhnliches Netz. `offen`, `nurTaste`, `ausloeserStandard` und `standard` eines Kanalnetzes gelten für alle seine Kanäle; ein Kanalnetz mit `standard` liegt in STANDARD auf Ruhe (6.9). `ausloeserStandard` ist die Voreinstellung für den Auslöser je Netz im Client (A20); der Nutzer darf abweichen, außer bei `nurTaste`. Seit 0.5.0 (A62, A64) legt die Konsole (`funk-admin netz-setzen`) ein Netz ohne Schalter mit `VA` an, mit `--taste` oder `--nur-taste` mit `TASTE`, Netz 1 (Global) immer mit `TASTE` und `nurTaste`; der Verwaltungsteil legt neue Netze mit `VA` an, die alte Startvorlage legte ihr Kommandonetz bis 0.9.0 mit `TASTE` an. Seit 0.6.0 setzt die Konsole eine Ch.-Liste mit `--kanaele "Ruhe,Schiff 1,..."` oder `--kanaele-datei` (die erste Bezeichnung muss die Ruhe sein) und `--gruppe`; der Verwaltungsteil schickt `kanaele` nur an einen Server ab 0.6.0. Gespeicherte Netze behalten ihren Wert, der Server schreibt nichts um. Seit 0.8.0 sagt `herkunft`, woher ein Netz kommt (Vorlage "Star Citizen", bis 0.12.0 die Namen der beiden alten Vorlagen, Baustein, "von Hand"); nur der Verwaltungsteil liest es, als Herkunftsschild und Filter. Die Vorlagen schreiben es mit, Global nie. Fehlt es in `NETZ_SETZEN`, behält der Server die bisherige Herkunft (ein Client 0.7.x kennt das Feld nicht); leer oder nur Leerzeichen ergibt `UNGUELTIG`. Netze ohne Herkunft (vor 0.8.0 angelegt) leitet der Verwaltungsteil ab, gespeichert wird die Ableitung nicht.
- `schiffe: [{id: u16, name: str(32), reihenfolge: u16, netze: {kennung: netzId}, plaetze: [{rolleId, kapazitaet: u8, mindest?: u8}], art?: "SCHIFF" | "TRUPP" | "ORGA", typ?: str(32)}]`, höchstens 64, je Schiff höchstens 8 Kennungen und 16 Plätze. Seit 0.6.0 darf eine Kennung auf eine Kanal-ID (Ch. 1 und höher) zeigen: Die Einheit "Schiff 3" gibt so ihren Ch. vor (6.16); ein Kanalnetz selbst, eine Ruhe und Orgaweit gehen nicht. Ein Schiff ist eine Einheit (A39): `art` fehlt bei Ständen von vor 0.3.0 und heißt dann SCHIFF; `typ` ist frei ("Hammerhead"); `mindest` ist die Mindestbesetzung eines Platzes für die Ampel auf dem Brett, höchstens die Kapazität, und wird beim Einteilen nicht geprüft.
- `rollen: [{id: u16, name: str(40), reihenfolge: u16, selbstWaehlbar: bool, brauchtSchiff: bool, kapazitaet?: u8, felder: [{feld: u8, netzId?: u16, schiffsnetz?: str(16), senden: bool, hoeren: bool, sendenStandard: bool, fest: bool, ch?: u8}], herkunft?: str(32)}]`, höchstens 64, je Rolle höchstens 16 Felder. `herkunft` (seit 0.8.0) wie bei `netze`. Genau eines von `netzId` und `schiffsnetz` ist gesetzt. Kennungen bestehen aus `[A-Z0-9_]`, 1-16 Zeichen. `kapazitaet` gilt für die Nutzung ohne Schiff und fehlt bei unbegrenzter Kapazität. Seit 0.6.0 darf `netzId` ein Kanalnetz nennen (auch Global, wenn es eines ist); der Ch. wird dann aufgelöst (6.16). `ch` (0 bis 30, nur mit `netzId` eines Kanalnetzes, nie Orgaweit) legt ihn fest. Eine Kanal-ID steht nie in einem Rollenfeld.
- seit 0.5.0 `phasen?: [{nr: u8 (1-5), name: str(24), kurz: str(12), felder?: [{rolleId: u16, feld: u8, netzId?: u16, schiffsnetz?: str(16), senden: bool, hoeren: bool, sendenStandard: bool, fest: bool, ch?: u8}]}]`, höchstens 5 Einträge (die Grundstellung und 4 Phasen) und zusammen höchstens 512 Felder, je Rolle und Phase höchstens 16 (6.14). Die Grundstellung (`nr` 1) steht immer zuerst, nur mit Namen (ohne eigenen Namen "Grundstellung", Kurzname "GRUND"). Die Felder einer Phase stehen flach, jedes mit seiner Rolle; die Variante einer Rolle sind ihre Einträge, sonst dieselbe Form wie `rollen[].felder`. Fehlt das Feld, hat die Flotte keine Phasen (oder der Server ist älter). Die laufende Phase steht nicht hier, sondern in `PhaseGeschaltet` (0x4004).

**0x0022 Mitgliedschaften** (S->C, eigene, immer vollständig):
- `zustandsVersion: i64`, `kontoVersion: i64`, `schiffId?: u16`, `rolleId?: u16`, `platz?: "BELEGT" | "RESERVIERT"`
- `felder: [{feld: u8, netzId: u16, hoeren: bool, senden: bool, fest: bool}]`, höchstens 16, `feld` und `netzId` je eindeutig. Global steht nie darin. Seit 0.6.0 darf `netzId` eine Kanal-ID sein (6.16); bei Ch. 0 (Ruhe) gelten `hoeren` und `senden` erst, wenn ein Ch. gewählt ist.
- `rolle?: u16` (seit 0.6.0): die gewählte Rolle (`RolleWaehlen`); fehlt, wenn die Felder aus der Rolle des Platzes (`rolleId`) oder aus einer anderen Vorgabe kommen.
- `global: {senden: bool}`
- `sendeauswahl: {felder: [u8], global: bool}`: Vorschlag des Servers nach einer Umstellung bzw. der gespeicherte Stand, auch bei WIEDERHERGESTELLT
- `stumm: [{netzId: u16 (0 = alle, 1 = Global), bis: i64, durch: kontoId}]`
- `gesperrt?: [{netzId: u16, bis: i64, durch: kontoId, alle?: true}]` (seit 0.6.0, 6.17): die Kanalsperren, die das Konto gerade treffen, die eigenen (höchstens 16) und die für alle (`alle`, höchstens 64); fehlt ohne Sperre. `netzId` ist ein Kanal, ein Kanalnetz (alle Ch.) oder ein Netz. Ein Feld mit einem gesperrten Kanal bleibt stehen; der Client zeigt es rot mit Setzendem und Ende.
- `quelle: "STANDARD" | "ROLLE" | "SELBST" | "VERSCHIEBEN" | "AUFSTELLUNG" | "WIEDERHERGESTELLT" | "SATZ"`, `durch?: kontoId`
- `letzterStand?: {schiffId?, rolleId?, felder: [...], sendeauswahl}`: der frühere Stand, sobald der Server einen kennt (nach Ablauf der Platzfrist, nach `PlaetzeFreigeben` und nach `Verschieben` mit `rolleId: 0`, 6.9), zur Übernahme per `SatzAnwenden` ("wie letztes Mal", A21)
- `entfallen?: [{feld, netzId, grund}]`: Felder, die bei einer Umstellung nicht untergebracht werden konnten
- `phase?: u8` (seit 0.5.0): nur wenn dieser Stand aus einem Phasenwechsel kommt, die Nummer der neuen Phase (6.14). So sagt der Client "Phase Entern (Kobold): sendest in Trupp Alpha" (bis 0.10.0 "... du sendest jetzt in Trupp Alpha") statt "Eingeteilt von Kobold". Ein Phasenwechsel setzt `quelle` nicht neu (es gibt keinen Wert PHASE; `MitgliedschaftsQuelle` ist eine strenge Aufzählung, an der Clients 0.4.x scheiterten).
- `konto?: {rechte: [str], scNamen: [str], discordName?, admin?, adminAntrag?, eigentuemer?, rollen: [u16], lieblingsrollen: [u16], ersteWahl?, untergilde? (seit 0.11.8 nie gefüllt), zeichen: [str], zeichenZeigen: bool, karteFreigeben: bool, beauftragung?: {von, bis, rechte, durch, grund?, aktiv}}`: die eigenen Kontoangaben nach der letzten Änderung (Stufe 1). Kommt auch ohne Änderung der Felder, sobald sich Namen, Rechte, Rollen, Abzeichen, Admin-Zustand, Zeichen oder ein Auftrag ändern (F3: erteilt, begonnen, entzogen, abgegeben, beendet, abgelaufen). `rechte` sind die dauerhaften Rechte; die eines laufenden Auftrags (`aktiv`) kommen dazu, so erscheint und verschwindet das Brett ohne Neustart. Seit 0.5.0 `alsFunkerBis?: i64`: Steht es da, spielt das Konto "Heute als Funker" (6.15), und die Rechte der Einsatzleitung ruhen bis dahin, auch die aus `rechte`; ein Client 0.4.x kennt das Feld nicht und hält sie für wirksam, der Server lehnt dann mit `KEIN_RECHT` ab. Seit 0.11.7 `abzeichen?: [str(16)]` höchstens 9 (alle eigenen Abzeichen, Ränge in Laufbahnfolge, dann Tätigkeiten in Katalogfolge, auch ausgeblendete), `abzeichenVorn?: str(16)` und `abzeichenAus?: [str(16)]` (die eigene Wahl aus `ProfilSetzen`, so gespeichert, auch wenn das Abzeichen gerade nicht besessen wird); die automatischen Zeichen stehen weiter in `zeichen`, vollständig. Seit 0.12.0 `meldungenSenden?: true`: der eigene Haken "darf Meldungen senden" (6.18); ob gerade gesendet werden darf, rechnet der Client daraus mit Stufe, `beauftragung.aktiv` und `alsFunkerBis` nach derselben Regel wie der Server.

**0x0023 Praesenz** (S->C): `voll: bool`, `zustandsVersion: i64`, `eintraege: [{kontoId, name, benutzername, online: bool, schiffId?, rolleId?, platz?, scName?, discordName?, untergilde?, zeichen?: [str], abzeichen?: [str(16)], abzeichenVorn?: str(16)}]`, `entfernt: [kontoId]`. `scName` ist der Hauptname, `discordName` steht für alle da (Antwort 3). `untergilde` füllt der Server seit 0.11.8 nicht mehr (Untergilden entfallen). `zeichen` fehlt, wenn der Inhaber seine Angaben nicht freigibt (`karteFreigeben`), `zeichen` auch, wenn er sein Zeichen nicht zeigen will (6.13); seit 0.11.7 fehlen in `zeichen` die automatischen Zeichen, die er ausgeblendet hat. Seit 0.11.7 trägt `abzeichen` die sichtbaren Abzeichen (höchstens 9, Ränge in Laufbahnfolge, dann Tätigkeiten in Katalogfolge) und `abzeichenVorn` das gewählte Abzeichen für vorn, nur wenn der Inhaber es besitzt und es sichtbar ist (unter `abzeichen` oder `zeichen`); beide mit denselben Sperren wie `zeichen`, und fehlt etwas, nimmt der Client das erste sichtbare Zeichen oder Abzeichen. Umfang: alle Konten mit Sitzung und alle Konten mit einem Platz (BELEGT oder RESERVIERT). Nur Sitzungen mit `ZUWEISEN` bekommen je Eintrag zusätzlich `felder: [{feld, netzId, hoeren, senden, fest}]`, `sendetIn: [u16]`, `stumm: [...]`, `verbindung: "UDP" | "TUNNEL" | null`, `guete?: "GUT" | "MAESSIG" | "SCHLECHT"` (7.8), seit 0.3.0 (F4) bei einem Platz `quelle`, `durch?` und `seit?` (wie er zustande kam, durch wen, Unix-ms) und `letzterPlatz?: {schiffId?, rolleId?}` (für "wie zuletzt für alle ohne Platz"), seit 0.5.0 `alsFunker?: true` für einen ADMIN im Funker-Modus (6.15), den die Einsatzleitung einteilen darf, seit 0.6.0 `rolle?: u16` (die gewählte Rolle, wie in Mitgliedschaften) und `gesperrt?: [{netzId, bis, durch}]` (die eigenen Kanalsperren des Kontos, höchstens 16; die für alle nennt `KanalSperre` LISTE). Datensparsam: Ein Funker sieht die Belegung der Schiffe (A15), nicht die Felder der anderen. Beginnt oder endet ein Auftrag, bekommt der Beauftragte einen vollen Stand mit bzw. ohne Details (6.12 Regel 9). Läuft die Frist eines Platzes ohne jede Aktion ab (Wiederverbindung 10 min, Reservierung 12 h, 6.9), geht im nächsten 10-s-Takt ein Delta für das Konto hinaus; ohne Platz und ohne Sitzung steht es dann in `entfernt` (seit der Gegenprüfung vom 01.10.2026, vorher stand die Person auf dem Brett weiter im Block). Ändert sich an den Einträgen nur, was allein Sitzungen mit `ZUWEISEN` sehen (Felder, Sendeauswahl, gewählte Rolle, eigene Kanalsperren: `FeldSetzen`, `HoerenSetzen`, `RolleWaehlen`, `KanalSperre`), geht das Delta nur an diese Sitzungen; eine Sperre für alle schickt den Getroffenen nur `Mitgliedschaften` (seit der Gegenprüfung 3.7 vom 03.10.2026: vorher bekam jeder Funker bei jedem Ch.-Wechsel einen unveränderten Eintrag und bei einer Sperre für alle ein Delta mit allen Getroffenen).

**0x0024 SendeFehler** (S->C): `fehler: [{code: "KEIN_SENDERECHT" | "ZIEL_ENTZOGEN" | "NUR_TASTE" | "KEIN_RECHT_GLOBAL" | "KEIN_RECHT_DURCHSAGE" | "MEHRFACH_NICHT_ERLAUBT" | "STUMMGESCHALTET" | "UNBEKANNTES_NETZ" | "UNGUELTIG" | "GRENZE", netzId?: u16, durch?: kontoId}]` höchstens 17, `teilweise: bool` (true = die übrigen Ziele wurden bedient), `verworfen: u32` (ganz verworfene Pakete seit der letzten Meldung). Höchstens 1 je Sekunde und Sitzung, mit einer Ausnahme: `ZIEL_ENTZOGEN` geht sofort, einmal je Netz und Umstellung. `ZIEL_ENTZOGEN` nennt der Server, wenn das Senderecht in diesem Netz in den letzten 10 s durch eine Umstellung (`durch`) wegfiel. Damit sieht der Nutzer "kein Senderecht in X" bzw. "verschoben durch Y", statt dass seine Sprache ohne Hinweis verloren geht (A18).

**0x0025 Abzeichen** (C->S, seit 0.11.7, `KONTEN_VERWALTEN` in einer Admin-Sitzung): `kontoId: u32`, `raenge: [str(16)]` höchstens 3, `taetigkeiten: [str(16)]` höchstens 6. Beide Listen ersetzen die gespeicherten (befördern und zurückstufen heißt: den Rang der Laufbahn austauschen, entfernen: weglassen). Der Server prüft jede Kennung gegen den Abzeichenkatalog: von der richtigen Art (ein Rang steht nicht unter `taetigkeiten` und umgekehrt), höchstens ein Rang je Laufbahn, keine Doppelten; ein Verstoß ergibt `UNGUELTIG` mit Klartext in `text` und ohne Trennung, ein unbekanntes Konto oder ein Antrag `UNBEKANNT`. Mehr als 3 Ränge, mehr als 6 Tätigkeiten oder eine Kennung über 16 Zeichen sind wie jede Längengrenze `ZU_GROSS` (4.9). Ohne Admin-Sitzung `ADMIN_SITZUNG_NOETIG` bzw. `KEIN_RECHT`, auch für die Führung (6.13). Antwort `{kontoId, raenge, taetigkeiten}`: der gespeicherte Stand, Ränge in Laufbahnfolge, Tätigkeiten in Katalogfolge. Danach Journal `ABZEICHEN`, `Meldung{MITGLIED_GEAENDERT}` an die Admin-Sitzungen, `Mitgliedschaften` an das Konto und ein Praesenz-Delta. Ein Client stellt die Anfrage nur an Server ab 0.11.7 (`Versionen.ServerFassungFuer`).

**0x0026 MeldungSenden** (C->S, seit 0.12.0, Recht nach 6.18): `art: str(24)`, `motiv?: str(32)`, `groesse?: u8` (1 normal, 2 doppelt, 3 dreifach; fehlt = 1), `zeile?: str(60)` (freiwillige Zeile unter der Grafik), `ziel?: i64` (Countdown: Zielzeit in Serverzeit, Unix-ms), `form?: str(16)` (Countdown: `Startzeit` oder `Restzeit`), `text?: str(120)`, `farbe?: str(16)`, `dauer?: str(16)` (Textmeldung). `art` ist offener Text: `Befehl`, `Zustand`, `ZustandAufheben`, `Ampel`, `AmpelAus`, `Countdown`, `CountdownAbbrechen`, `BereitAbfrage`, `BereitSchliessen`, `Text`, `TextAufheben`. `motiv` ist eine Kennung aus dem Meldungskatalog (`meldungen.json`, 6.18) und muss zur Art passen: Befehl, Zustand und Ampel brauchen eins ihrer Art; beim Countdown folgt es aus `form` (`countdown_startzeit`, `countdown_restzeit`; steht es da, muss es passen); bei der Bereit-Abfrage ist es ab Werk `bereit_melden`. Beim Countdown sind `form` und `ziel` Pflicht, `ziel` liegt in der Zukunft und höchstens 24 h voraus. Zustände bleiben immer klein (`groesse` wird 1). Die Textmeldung (`art` `Text`, M24) trägt statt eines Motivs `text` (Pflicht, getrimmt nicht leer, höchstens 120 Zeichen), `farbe` (`weiss`, `gelb`, `orange`, `rot`, `gruen`, `blau`; fehlt = `weiss`), `dauer` (`8s` oder `bleibt`; fehlt = `8s`) und `groesse`; `motiv` und `zeile` zählen dort nicht. `TextAufheben` hebt die laufende Textmeldung auf, ohne weitere Felder. Eine unbekannte Art, ein fehlendes, unbekanntes oder unpassendes Motiv, eine falsche Zielzeit, eine Textmeldung ohne Text und eine unbekannte Farbe oder Dauer ergeben `UNGUELTIG` mit Klartext in `text` ("unbekannte Art X", "sammeln ist kein Motiv für Zustand", "Die Zielzeit ist schon vorbei", "Textmeldung braucht einen Text", "unbekannte Farbe X", "unbekannte Dauer X"), ohne Trennung und ohne Seiteneffekt; ohne Recht `KEIN_RECHT`. Längen über den Grenzen sind `ZU_GROSS` (4.9) und trennen, auch ein `text` über 120 Zeichen (der Client begrenzt die Eingabe deshalb hart); `groesse` außerhalb 1-3 `UNGUELTIG`. Je Sender höchstens eine Meldung in 2 s (4.8, Ratengruppe Meldung), sonst `GRENZE`; der Client zählt die 2 s ab der Antwort auf die vorige. `BereitSchliessen` ohne offene Abfrage ist kein Fehler und sendet nichts. Antwort `{}`; die Meldung selbst bekommen alle, auch der Sender, als `Meldestand` (0x4005). Ein Client stellt die Anfrage nur an Server ab 0.12.0 (`Versionen.MeldesystemAb`).

**0x0027 BereitMelden** (C->S, seit 0.12.0, jedes Konto): `nummer: u32` (aus `Meldestand.abfrage.nummer`). Ist keine Abfrage mit dieser Nummer offen (auch: nach 2 min abgelaufen, durch eine neue ersetzt), kommt `UNBEKANNT`; ein zweites Bereit ändert nichts. Antwort `{}`. Danach geht der neue `BereitStand` (0x4006) an alle, die senden dürfen. Höchstens 1/s, Stoß 2 (4.8). Nur an Server ab 0.12.0.

**0x0030 SendeauswahlSetzen** (C->S): `felder: [u8]` 0-16 Einträge, aufsteigend, ohne Doppelte; `global: bool`. Der Server prüft: jedes Feld hat eine Mitgliedschaft mit `senden` und seit 0.6.0 keinen für das Konto gesperrten Kanal (`KEIN_RECHT`, 6.17), Global nur mit `GLOBAL_SENDEN`, mehr als ein Ziel nur mit `MEHRFACH_SENDEN`, jeweils mit den Rechten zur Zeit der Anfrage, ein laufender Auftrag eingeschlossen (6.12). Antwort `{}`. Wirkung: Anzeige für die Einsatzleitung (`Praesenz.sendetIn`) und Speicherung für "wie letztes Mal". Maßgeblich für die Weiterleitung sind die Ziele im Sprachpaket. Komfortzustand (6.2), wird zusammengefasst (4.8).

**0x0031 HoerenSetzen** (C->S): `netzId: u16`, `hoeren: bool`. Erlaubt auch für feste Felder. Für Global gibt es `GLOBAL_PFLICHT`, für ein Netz ohne eigene Mitgliedschaft `UNBEKANNT`. Antwort `{zustandsVersion}`. Wird zusammengefasst (4.8).

**0x0032 EinsteigenRolleSchiff** (C->S): `kontoId?: u32` (fehlt = selbst), `rolleId: u16`, `schiffId?: u16`, `erzwingen?: bool` (nur `ZUWEISEN`, übergeht die Kapazität), `basisVersion?: i64`. Für ein fremdes Konto gilt alles wie bei `Verschieben` mit `rolleId`. Wer sich selbst einträgt ("Einsteigen"), braucht `selbstwahl = true` und `rolle.selbstWaehlbar = true` (sonst `KEIN_RECHT`); die Kapazität wird geprüft (`PLATZ_BELEGT`). Bei gesperrter Selbstwahl kommt `SELBSTWAHL_GESPERRT`, außer für Konten mit `ZUWEISEN` und, solange `nachzuegler` gilt ("Wechsel sperren"), für Konten ohne Platz; eine Sperre wirkt nur, solange ihr Urheber `ZUWEISEN` hat und `selbstwahlBis` nicht erreicht ist (6.9). Eine Rolle, die auf dem gewählten Schiff nicht in `plaetze` steht, und `brauchtSchiff` ohne `schiffId` ergeben `UNGUELTIG`. Der Server bildet die Felder aus der Rolle; Schiffsnetze werden über `schiff.netze[kennung]` aufgelöst, fehlt die Kennung, bleibt das Feld leer. Feste Felder bleiben (6.9). Die Sendeauswahl kommt aus `sendenStandard`. Antwort `{zustandsVersion}`. Das ist A15: Rolle und Schiff in einem Schritt, alle Felder stellen sich zusammen um.

**0x0033 Verschieben** (C->S): `basisVersion?: i64`, `aenderungen: [{kontoId, rolleId?: u16, schiffId?: u16, felder?: [{feld, netzId, hoeren, senden}], felderPatch?: [...], sendeFelder?: [u8]}]`, 1-200.
- `rolleId` (1-65534): Felder aus der Rolle; `rolleId: 0` entfernt Rolle und Platz, das Konto bekommt STANDARD, soweit nicht `felder` angegeben sind. Seit 0.3.0 merkt der Server dabei den bisherigen Platz mit Feldern und Sendeauswahl als `letzterStand` ("wie letztes Mal", A21).
- `schiffId` allein: gleiche Rolle auf dem neuen Schiff, die Schiffsnetze werden neu aufgelöst. `schiffId: 0` entfernt das Schiff.
- `felder`: ersetzt die Felder vollständig; alle so gesetzten Felder sind fest.
- `felderPatch`: Einträge `{feld?, netzId, hoeren?, senden?, fest?}` oder `{feld, leeren: true}`. Ohne `feld` nimmt der Server das Feld, in dem das Netz schon steht, sonst das nächste freie. Feldlage, hören und senden der übrigen Felder bleiben. Fehlendes `hoeren` heißt true, fehlendes `senden` heißt false, fehlendes `fest` heißt true.
- Reihenfolge je Änderung: Rolle und Schiff, dann `felder`, dann `felderPatch`, dann `sendeFelder`.
- Sendeauswahl ohne `sendeFelder` und ohne Rolle oder Schiff: Die bisherige bleibt, soweit sie gültig ist. Seit 0.11.0 wie `FeldSetzen` Regel 7: Geht ein Feld per `felderPatch` mit `feld` von der Ruhe auf einen Ch. desselben Kanalnetzes, sendet es danach, ist der Kanal für das Konto nicht gesperrt und bleibt die Sendeauswahl sonst leer (keine Felder, Global nicht), wird dieses Feld Sendeziel (Ch. je Person, 6.16).
- Alles oder nichts. Die Kapazität wird am Endzustand der ganzen Anfrage geprüft, ein Tausch in einer Anfrage ist also möglich. Ein ADMIN-Konto als Ziel ergibt `KEIN_RECHT`, außer der Anfragende ist ADMIN oder das Ziel spielt "Heute als Funker" (Rangregel 6.10, 6.15).
- Weicht bei einem betroffenen Konto `kontoVersion` über `basisVersion` hinaus ab, kommt `VERALTET` mit `details.konten: [{kontoId, kontoVersion}]` und `details.zustandsVersion`.
- Antwort `{zustandsVersion, offline: [kontoId], verdraengt: [kontoId]}`.

**0x0034 Stummschalten** (C->S): `kontoId`, `netzId: u16` (0 = alle, einschließlich Global und Durchsage; 1 = nur Global; sonst ein Netz, seit 0.6.0 auch eine Kanal-ID oder ein Kanalnetz für alle seine Ch.; eine Ruhe ergibt `UNGUELTIG`, Orgaweit `UNBEKANNT`), `sekunden: u16` (0 = aufheben, höchstens 3600). Rangregeln in 6.10. Antwort `{}`. Der Betroffene bekommt `Mitgliedschaften` mit `stumm`.

**0x0035 FeldSetzen** (C->S, selbst): `feld: u8`, `netzId?: u16` (fehlt = Feld leeren). Antwort `{zustandsVersion}`. Regeln in dieser Folge (seit 0.6.0, 6.16):
Seit 0.8.1 (wie im früheren Funkprogramm, Sarevoc am 06.10.2026, Q44/Q45): Den Kanal eines Felds tauscht man nicht selbst, das tun der Admin über die Rolle und die Einsatzleitung über das Brett. Selbst gehen nur der Ch.-Wechsel (Regel 5, auch im festen Feld) und ein offenes Netz in ein freies Feld (Regel 6, so trägt sich der Echo-Funker ein).
1. Leeren: ein leeres Feld bleibt leer (Erfolg ohne Änderung); ein belegtes Feld leert man nicht selbst: im festen Feld `FELD_FEST` (vor allen anderen Regeln), sonst `KEIN_RECHT` (eine Kanalsperre nach Regel 3 geht dann vor). Bis 0.8.0 durfte man nicht feste Felder leeren.
2. `netzId` ist ein gewöhnliches Netz oder eine Kanal-ID mit bekanntem Kanalnetz und einem Ch. aus seiner Liste (auch Ruhe), sonst `UNBEKANNT`. Netz 1 (Global), Orgaweit und die Netz-ID eines Kanalnetzes selbst ergeben `UNGUELTIG`.
3. Kanalsperre (6.17, seit Schritt 3.2): Hält das Feld einen für das Konto gesperrten Kanal oder ist das neue Netz bzw. der neue Kanal gesperrt, kommt `KEIN_RECHT` mit `details: {grund: "GESPERRT", durch, bis}`, auch beim Leeren; das Feld bleibt stehen.
4. Derselbe Kanal (dieselbe Netz-ID) steht schon in einem anderen Feld: `UNGUELTIG`.
5. Ch.-Wechsel: Das Feld hält schon einen Kanal oder die Ruhe desselben Kanalnetzes. Erlaubt unabhängig von `offen` und seit 0.8.1 auch im festen Feld; `hoeren` und `senden` bleiben, wie sie waren. Ein anderes Netz in einem belegten Feld: im festen Feld `FELD_FEST`, sonst `KEIN_RECHT`.
6. Freies Feld: Das Netz bzw. Kanalnetz muss offen sein (`offen != "NEIN"`), sonst `KEIN_RECHT`. Dann gilt `hoeren = true` und `senden = (offen == "HOEREN_SENDEN")`. Von 0.6.0 bis 0.8.0 genügte auch die Deckung durch die Rolle, und ein belegtes nicht festes Feld ließ sich so umlegen.
7. Die Sendeauswahl behält ihre Felder, solange sie senden dürfen; ein Feld auf Ruhe oder mit einem gesperrten Kanal fällt heraus. Wechselt das Netz hinter einem gesendeten Feld, gilt C4 (6.11). Seit 0.9.1: Geht ein Feld von der Ruhe auf einen Ch. und ist die Sendeauswahl danach leer (keine Felder, Global nicht), wird dieses Feld Sendeziel; so sendet nach der Rollenwahl (alles auf Ruhe, 6.16) der erste gewählte Ch. ohne weiteren Klick.
Bis 0.5.0 galten nur offene Netze, ohne Deckung durch die Rolle.

**0x0036 SatzAnwenden** (C->S, selbst): `rolleId?: u16`, `schiffId?: u16`, `felder: [{feld, netzId, hoeren, senden}]` höchstens 16, `sendeauswahl?: {felder, global}`. Atomar. Rolle und Schiff werden geprüft wie bei `EinsteigenRolleSchiff`; scheitert das, ändert sich nichts. Seit 0.8.1 (Q44 bis Q46) bringt ein Satz keine Kanäle mehr in die Felder: Mit `rolleId` kommen die Felder aus der Rolle wie bei der Selbstwahl (um feste Felder herum, Ch. nach 6.16 ohne den Ch. aus dem Satz), ohne `rolleId` bleiben sie, wie sie sind. Aus dem Satz gelten nur `hoeren` für Netze, die schon in einem Feld liegen (außer in festen Feldern), und offene Netze (senden nur bei `HOEREN_SENDEN`) als Zusatznetze auf ihrem Feld oder dem nächsten freien; die `sendeauswahl` des Satzes gilt, soweit sie zu den Feldern passt. Andere Felder werden übersprungen und gemeldet, ebenso ein für das Konto gesperrter Kanal (`KEIN_RECHT`, 6.17). Bis 0.8.0 nahm ein Satz jedes offene oder von der Rolle gedeckte Netz auf seinem Feld. Feste Felder bleiben. Antwort `{zustandsVersion, uebersprungen: [{feld, code}]}`. Dafür gedacht: "wie letztes Mal" und benannte Sätze (A21). `rolleId: 0` gibt den eigenen Platz auf; bei wirkender Sperre der Selbstwahl (6.9) ist das ein Wechsel und ergibt für ein Konto mit Platz und ohne `ZUWEISEN` `SELBSTWAHL_GESPERRT`. Seit 0.5.0: Läuft eine Phase mit einer Variante für die Rolle, kommen die Felder aus der Phase, aus dem Satz nur `hoeren` für Netze, die darin vorkommen, und offene Netze als Zusatznetze (6.14, "Wie letztes Mal").

**0x0037 PlaetzeFreigeben** (C->S): `{}`. Gibt alle Plätze von Konten ohne Sitzung frei (BELEGT in der Frist und RESERVIERT), die von ADMIN-Konten nur für einen ADMIN oder im Funker-Modus (6.10, 6.15). Der frühere Stand bleibt als `letzterStand`. Antwort `{zustandsVersion, anzahl: u16}`.

**0x0038 KontenVerzeichnis** (C->S, seit 0.3.0, N4): `{}`. Nur mit `ZUWEISEN` (aus Stufe, Einzelrecht oder laufendem Auftrag), sonst `KEIN_RECHT`. Antwort `{konten: [{kontoId, name, benutzername, scName?, discordName?, stufe, online: bool, schiffId?, rolleId?, platz?, letzterPlatz?: {schiffId?, rolleId?}, untergilde?}]}` (`untergilde` seit 0.11.8 nie gefüllt) mit allen aktiven Konten (nicht gesperrt, keine offenen Anträge), auch offline, nach Konto-ID, höchstens 500; ohne Discord-ID. `platz` ist BELEGT oder RESERVIERT, `schiffId` und `rolleId` stehen nur bei einem solchen Platz. Seit 0.5.0 `alsFunker?: true` für einen ADMIN im Funker-Modus (6.15). Dafür gedacht: "Alle Mitglieder" auf dem Brett, damit die Einsatzleitung auch Abwesende vorab einteilen kann. Höchstens 1 je 5 s (4.8).

**0x0039 RolleWaehlen** (C->S, selbst, seit 0.6.0, 6.16): `{rolleId: u16, ch?: [{feld: u8, ch: u8 (0-30)}] höchstens 16, je Feld einmal}`. Antwort `{zustandsVersion, uebersprungen: [{feld, code}]}` (Form wie bei `SatzAnwenden`).
- Unbekannte Rolle: `UNBEKANNT`; nicht `selbstWaehlbar`: `KEIN_RECHT`.
- Die Felder kommen aus der Rolle, bei laufender Phase mit einer Variante für diese Rolle aus der Variante; Schiffsnetz-Kennungen löst die Einheit des Platzes auf (ohne Platz bleibt das Feld leer). Den Ch. jedes Felds mit einem Kanalnetz löst der Server nach 6.16 auf; `ch` ist der Wunsch des Clients für ein Rollenfeld; seit 0.8.1 (Q46: nach der Rollenwahl beginnt jeder Ch. auf Ruhe, der Funker wechselt selbst) prüft der Server ihn noch, wendet ihn aber nicht mehr an, und ein Client ab 0.8.1 schickt keinen.
- Feste Felder bleiben; kollidierende Rollenfelder rücken nach wie bei der Selbstwahl (6.9). Was über Feld 15 geht, steht in `uebersprungen` (`UNGUELTIG`) und in `Mitgliedschaften.entfallen`. Ein Wunsch für ein Feld, das kein Kanalnetz trägt, oder mit einem Ch. außerhalb der Liste steht ebenfalls in `uebersprungen` (`UNGUELTIG`), das Feld bekommt den Ch. nach der Folge ohne Wunsch.
- Platz, Reservierung und Brett bleiben unverändert. `quelle` wird `SELBST` (kein neuer Wert), `rolle` die gewählte Rolle.
- Die Sendeauswahl kommt aus `sendenStandard` der Rolle; ein Feld auf Ruhe sendet nicht. Gesperrte Kanäle (6.17) legt der Server trotzdem ins Feld (rot, ohne Hören und Senden); ist der Sendevorschlag gesperrt, gilt das erste sendefähige Feld ohne Sperre.
- Die Sperre der Selbstwahl gilt nicht (sie betrifft den Platz); die Einsatzleitung bindet über feste Felder und Kanalsperren (6.17).
- Ein Client 0.6.0 schickt die Anfrage nur an Server ab 0.6.0 (`Versionen.ServerFassungFuer`). Höchstens 5/s, Burst 10, zusammen mit `FeldSetzen` und `SatzAnwenden` (4.8).

**0x003A KanalSperre** (C->S, `ZUWEISEN`, seit 0.6.0, 6.17): `{op: "SETZEN" | "AUFHEBEN" | "LISTE", kontoId?: u32 (0 = für alle ohne ZUWEISEN), netzId?: u16, bis?: i64}`; SETZEN und AUFHEBEN brauchen `kontoId` und `netzId`, `bis` nur SETZEN. Antwort `{sperren: [{kontoId, netzId, bis, durch}]}` mit allen laufenden Sperren (höchstens 500, `kontoId` 0 = für alle).
- Ohne wirksames `ZUWEISEN` (Stufe, Einzelrecht, Auftrag; nicht im Funker-Modus) `KEIN_RECHT`, auch für LISTE.
- `netzId`: eine Kanal-ID (ein Ch.), die Netz-ID eines Kanalnetzes (alle Ch.) oder ein gewöhnliches Netz; Global (Netz 1, auch Orgaweit 0x8021) und eine Ruhe ergeben `UNGUELTIG` (für Global bleibt Stummschalten), ein Kanal außerhalb der Liste oder ein unbekanntes Netz `UNBEKANNT`. Die übrigen Global-Kanäle (Ch. 2 und höher) sind gewöhnliche Kanäle.
- `bis` fehlt: das nächste 04:00 Europe/Berlin, höchstens 24 h; sonst muss es nach jetzt und höchstens 24 h später liegen (`UNGUELTIG`).
- Rangregel wie Stummschalten (6.10): Ein ADMIN-Konto sperrt nur ein ADMIN, auch im Funker-Modus; eine Sperre für alle trifft ADMIN-Konten (im Funker-Modus, ohne `ZUWEISEN`) nur, wenn ein ADMIN sie gesetzt hat. Eine laufende Sperre auf dasselbe Konto und Netz ersetzt nur, wer sie aufheben dürfte. AUFHEBEN: wer gesetzt hat, ein Konto höherer Stufe als der Setzende oder ein ADMIN; der Gesperrte hebt eine fremde Sperre nie selbst auf. Eine abgelaufene Sperre ist `UNBEKANNT`.
- Grenzen: je Konto 16, für alle 64, zusammen 500 (`UNGUELTIG` mit Text). Höchstens 10/s, Burst 20, zusammen mit Stummschalten (4.8).
- Folgen: `Mitgliedschaften` mit `gesperrt` an die Betroffenen, ein Praesenz-Delta nur an `ZUWEISEN` (bei einer Sperre für alle nur für Konten, deren Sendeauswahl sie ändert, 4.5 Praesenz), `Meldung{KANALSPERRE}` an `ZUWEISEN`, Journal (`KANAL_GESPERRT`, `KANAL_FREI` mit Kanal und Klarnamen, etwa "Brücke Ch. 3 (Schiff 3) für Mustermann (Konto 7) gesperrt bis 03.10. 04:00 durch ..."). Ein Client 0.6.0 schickt die Anfrage nur an Server ab 0.6.0.

**0x003C Funkermodus** (C->S, seit 0.5.0, 6.15): `{an: bool}`. Nur ein aktives Konto der Stufe ADMIN für sich selbst, ohne Admin-Sitzung und ohne Code; sonst `KEIN_RECHT`. Ein anderes Konto lässt sich nicht nennen (ein fremdes Feld wird übergangen und gilt nicht). `an: true` schaltet "Heute als Funker" bis zum nächsten 04:00 Uhr Europe/Berlin ein, höchstens 24 h; läuft der Modus schon, bleibt sein Ende. `an: false` beendet ihn. Antwort `{an: bool, bis?: i64}` mit dem Stand danach. Folgen wie beim Beginn und Ende eines Auftrags (6.12 Regel 9): `Mitgliedschaften` mit `konto.alsFunkerBis`, ein voller Praesenz-Stand ohne bzw. mit Details, Ende eines Sprechanzeige-Abos, Journal. Höchstens 1/s, Burst 2 (4.8). Ein Server 0.4.x kennt den Pflichttyp nicht und trennt mit `PROTOKOLL`; der Client schickt ihn deshalb erst an Server ab 0.5.0 (4.7).

**0x003D PhaseSchalten** (C->S, seit 0.5.0, 6.14): `{nr: u8 (1-5), erwartet: u8 (1-5)}`. Nur mit `ZUWEISEN` (aus Stufe, Einzelrecht oder laufendem Auftrag), sonst `KEIN_RECHT`. `erwartet` ist die Phase, die der Client für die laufende hält; weicht sie ab, kommt `VERALTET` mit `details: {nr, durch}` (die laufende Phase und wer sie geschaltet hat, 0 = Konsole oder Rückfall), und nichts ändert sich: Drücken zwei Leiter zugleich, schaltet der zweite nicht noch einmal. Eine Nummer ohne Phase ergibt `UNBEKANNT`; die laufende Nummer noch einmal ist Ok ohne Umstellung. Antwort `{nr, umgestellt: u16, ausgelassen: [kontoId], zustandsVersion: i64}`; `ausgelassen` nennt Konten, die der Schaltende nach der Rangregel nicht einteilen darf (ADMIN ohne Funker-Modus, wenn kein ADMIN schaltet). Die Vorschau "9 Funker" rechnet der Client selbst aus NetzListe und Praesenz. Höchstens 1 je 2 s, Burst 2 (4.8). Für Server 0.4.x gilt dasselbe wie bei 0x003C.

**0x0040 AufstellungSpeichern** (C->S): `id?: u32`, `version?: u32`, `name: str(48)`, `eintraege: [{kontoId?: u32, discordId?: str, rolleId: u16, schiffId?: u16, felder?: [...], sendeFelder?: [u8]}]`, höchstens 200, je Eintrag `kontoId` oder `discordId`, je Eintrag höchstens 16 Felder, geprüft nach 6.9. Antwort `{id, version}`. Weicht die Version ab, kommt `VERALTET`. Eine neue Aufstellung gehört dem, der sie anlegt (`ersteller`); ändern (mit `id`) und löschen dürfen seit 0.3.0 nur ihr Ersteller und ein ADMIN, sonst `KEIN_RECHT` (6.12 Regel 10, F10). Anwenden dürfen alle mit `ZUWEISEN`. Danach geht `AufstellungGeaendert` an alle Sitzungen mit `ZUWEISEN`.

**0x0041 AufstellungListe** (C->S): `id?: u32`. Ohne id: `{aufstellungen: [{id, name, version, anzahl, geaendert: i64, durch: kontoId, ersteller?: kontoId}]}`. Mit id: das vollständige Objekt, seit 0.3.0 mit `ersteller?`. Aufstellungen von 0.2.0 bekommen beim Laden den als Ersteller, der sie zuletzt gespeichert hat (`durch`).

**0x0042 AufstellungAnwenden** (C->S): `id: u32`, `version?: u32` (fehlt = aktuelle), `modus: "ERGAENZEN" | "ERSETZEN"`, `basisVersion?: i64`, `erzwingen?: bool`. Ablauf nach 6.7. Antwort `{zustandsVersion, version (die angewandte), angewendet: u16, offline: [kontoId], unverknuepft: [discordId], verdraengt: [kontoId], uebersprungen: [{eintrag, kontoId, code}]}`. Fehler `VERALTET` bzw. `PLATZ_BELEGT` mit `details.fehler: [{eintrag, kontoId?, discordId?, code}]`.

**0x0043 AufstellungLoeschen** (C->S): `id`, `version`. Antwort `{}`, danach `AufstellungGeaendert{geloescht: true}`.

**0x0044 RaidHelperImport** (C->S): `eventId: str(200)`: nur Ziffern (höchstens 24) oder ein Discord-Nachrichtenlink `https://discord.com/channels/<gilde>/<kanal>/<nachricht>`. Beim Link übernimmt der Server die letzte Zahl und prüft die Gilden-ID des Links gegen die konfigurierte Gilden-ID (sonst `UNGUELTIG`). Der Server holt `GET https://raid-helper.xyz/api/v4/events/{eventId}`, mit dem Server-Key falls konfiguriert (per `LoadCredential`, nur auf dem Server, Header-Redaction im HttpClient-Logging ausdrücklich an), Timeout 5 s, höchstens 1 MiB, strenges JSON. `serverId` muss die konfigurierte Gilde sein, sonst `UNGUELTIG`. Übernommen werden nur die Mindestfelder (raidhelper.md B17). Die Status queued und Klassen mit `type = "default"` (Bench, Late, Tentative, Absence) werden nicht eingeteilt. Antwort: `{event: {id, titel, start, ende, lastUpdated}, anmeldungen: [{discordId, name, className, specName, status, position, kontoId?, vorschlag?: {rolleId, schiffId?}}]}`, höchstens 200. Der Vorschlag kommt aus der Regeltabelle. Der Import vergibt keine Rechte: `leaderId` und `coLeaders` werden in v1 nur angezeigt. Serverweit geht höchstens 1 Abruf je 5 s an Raid-Helper, mit Backoff bei 429. Der Snapshot wird nach 14 Tagen gelöscht (einstellbar). Der Import stößt den Discord-Abgleich an (3.7).

**0x0045 RegelTabelle** (C->S): `op: "LESEN" | "SETZEN"`, `regeln?: [{className: str(40), specName: str(40) oder "*", rolleId, schiffId?}]`, höchstens 200. Antwort `{regeln, version}`.

**0x0046 RaidHelperEvents** (C->S): `{}`. Nur mit konfiguriertem Server-Key, sonst `EXTERN` mit `details.grund = "kein Server-Key"`. Der Server holt `GET /servers/{gilde}/events` mit `IncludeSignUps: false` und `StartTimeFilter` = jetzt - 12 h. Antwort `{events: [{id, titel, start, ende, lastUpdated}]}`, höchstens 50. Es gilt dieselbe serverweite Abrufgrenze wie beim Import.

**0x0047 AufstellungGeaendert** (S->C an `ZUWEISEN`): `id`, `version`, `durch: kontoId`, `geloescht: bool`. `durch` ist seit 0.3.0 der, der gespeichert oder gelöscht hat (vorher immer 0, K3).

**0x0050 DurchsageStart** (S->C an einen Empfänger, beim ersten Paket einer Sprechphase mit Art DURCHSAGE oder GLOBAL, das an ihn geht, 6.4): `sprecher: kontoId`, `art: "DURCHSAGE" | "GLOBAL"`, `netze: [u16]` (für diesen Empfänger, leer = an alle), `sprechphase: u16`. Er dient nur der Anzeige (Banner). Die Absenkung beginnt beim Empfänger schon mit dem ersten Sprachpaket.

**0x0051 DurchsageEnde**: `sprecher`, `sprechphase`. Kommt beim ENDE-Paket, nach 300 ms ohne Paket dieser Sprechphase, wenn der Empfänger aus der Durchsage herausfällt, und beim Sitzungsende des Sprechers. Nur Anzeige.

**0x0052 SprechanzeigeAbo** (C->S): `an: bool`. Antwort `{}`.

**0x0053 Sprechanzeige** (S->C, nur bei Änderung, höchstens 5 je Sekunde): `aktiv: [{kontoId, art: "NETZ" | "DURCHSAGE" | "GLOBAL", netze: [u16]}]`. Das ist für die Übersicht der Einsatzleitung über alle Netze. In den eigenen Hörnetzen zeigt der Client Sprecher und Pegel ohnehin aus dem Stempel und dem decodierten Signal (A18).

**0x0060 PegelVoreinstellungen**: `eintraege: [{kontoId, netzId: u16 (0 = alle Netze), db: Zahl -60.0..+12.0 in Schritten von 0.5}]`, höchstens 2000. Beim Start per Push, auf Anfrage (Body `{}`) als Antwort. Geräteübergreifend am Konto gespeichert (A4). Der Server wertet die Werte nicht aus, er mischt nicht.

**0x0061 PegelSetzen** (C->S): `kontoId`, `netzId`, `db?` (fehlt = löschen). Antwort `{}`. Komfortzustand, wird zusammengefasst.

**0x0062 Ablage** (C->S): `op: "LESEN" | "SCHREIBEN"`, `bereich: "SAETZE" | "LETZTE" | "ANSICHT"`, `daten?: Objekt` (serialisiert höchstens 16 KiB, Tiefe höchstens 8), `version?: u32`. Antwort `{daten, version}`. Der Server speichert das undurchsichtig, er deutet es nicht. Dafür gedacht: benannte Sätze und "die letzten fünf" (A21), Ansichtseinstellungen. Komfortzustand.

**0x0071 KeyRotation** (C->S): `epoche: u8` (aktuelle), `grund: "ZAEHLER" | "FEHLER"`. Antwort `{}`, danach eine neue VoiceKeys-Anfrage. Höchstens 1 je 10 s.

**0x0072 UdpStatus** (S->C): `zustand: "AUS" | "PRUEFUNG" | "GEBUNDEN"`, `adresse?: str(47)` (IP:Port, wie der Server sie sieht), `gleicheIp?: bool` (UDP-IP gleich der IP der TLS-Verbindung). Bei `gleicheIp = false` zeigt der Client einen Hinweis; das kann bei Mehrfach-WAN oder CGNAT harmlos sein.

**0x0073 TunnelModus** (C->S): `an: bool`, `grund: "KEINE_BINDUNG" | "UDP_AUSFALL" | "UDP_ZURUECK" | "NUTZER"`. Antwort `{an}`. Nur der Client schaltet, der Server rät nie (Lehre aus Mumble #1968 und #6311).

**0x0074 EmpfangsBericht** (C->S Push, alle 5 s in AKTIV): `zeitraumMs: u16`, `weg: "UDP" | "TUNNEL"`, `runter: {empfangen: u32, verloren: u32, doppelt: u32, umsortiert: u32}` (aus den Lücken des runter-Zählers), `rttMs?: {p50: u16, p95: u16}` (aus KEEPALIVE_ECHO, fehlt im Tunnel), `jitterMs: Zahl` (7.8), `zielVerzoegerungMs: Zahl` (Jitterpuffer), `spaetVerworfen: u32`, `unterlaeufe: u32` (Ausgabe).

**0x0075 VerlustHinweis** (S->C an Sender): `prozent: u8` 0-100 = Verlust hoch dieses Senders (vom Server aus den Zählerlücken gemessen, ohne Ausreißerschnitt) plus p75 des Verlusts runter seiner Hörer der letzten 10 s (aus deren EmpfangsBerichten), höchstens 100. Hörer, die seit über 30 s mehr als 20 % runter verlieren, zählen nicht mit und bekommen stattdessen `Meldung{EIGENER_VERLUST}`. Verwerfungen durch den Server zählen nicht, denn sie verbrauchen keinen Zähler. Gesendet wird bei einer Änderung um mindestens 2 Punkte, höchstens alle 5 s. Der Client setzt daraus `OPUS_SET_PACKET_LOSS_PERC` (codec-ausgabe.md B16).

**0x0080 Verwaltung** (C->S): `op: "NETZ_SETZEN" | "NETZ_LOESCHEN" | "SCHIFF_SETZEN" | "SCHIFF_LOESCHEN" | "ROLLE_SETZEN" | "ROLLE_LOESCHEN" | "SELBSTWAHL" | "EINSTELLUNG"`, `objekt: {...}` (Form wie in NetzListe; bei EINSTELLUNG `{durchsageNurTaste}`), `version: i64` (NetzListe-Version). Der Wechsel ist atomar. Danach geht `NetzListe` an alle, und Mitgliedschaften auf gelöschte Netze fallen weg (mit `Mitgliedschaften`-Push). Global (ID 1) lässt sich nicht löschen und nicht umtypisieren. `SELBSTWAHL` mit `objekt: {an: bool, nachzuegler?: bool}` darf auch `ZUWEISEN` (aus Stufe, Einzelrecht oder laufendem Auftrag), ohne Admin-Sitzung; `nachzuegler` (fehlt: true) wählt beim Sperren zwischen "Wechsel sperren" und "Ganz sperren", Clients 0.2.0 schicken nur `an` (6.9). Seit 0.3.0 gibt es `PAKET` mit `objekt: {ops: [{op, objekt}]}`, 1-200 Schritte der übrigen Arten außer `SELBSTWAHL` und `PAKET`, in dieser Reihenfolge und gegen dieselbe `version`: Alles oder nichts, die NetzListe-Version steigt einmal, und ein Fehler nennt den Schritt in `details.fehler: [{eintrag, code}]` (N5 "Übernehmen" der Flotten-Einstellungsseite, auch die Startvorlage). Alles andere braucht `NETZE_VERWALTEN` in einer Admin-Sitzung, die drei LOESCHEN-Operationen und `PAKET` zusätzlich eine frische Bestätigung (3.9). Seit 0.5.0 `PHASE_SETZEN` mit einer Phase in der Form von `NetzListe.phasen[]` und `PHASE_LOESCHEN` mit `{id: nr}` (6.14), einzeln und in `PAKET`; `PHASE_LOESCHEN` braucht wie die übrigen LOESCHEN-Operationen eine frische Bestätigung. Ein Server 0.4.x kennt die beiden Werte nicht (`VerwaltungsOperation` ist streng); der Verwaltungsteil schickt sie nur an Server ab 0.5.0. Clients lesen die Aufzählung nie. Eine Phase in `PAKET` liegt höchstens auf Tiefe 7 (4.3), deshalb stehen ihre Felder flach. Seit 0.6.0 trägt `NETZ_SETZEN` `kanaele` und `gruppe` (6.16): Eine Netz-ID ab 0x8000 ergibt `UNGUELTIG` (auch für ein gespeichertes Netz aus einem älteren Stand, das sich dann nur noch löschen lässt), ebenso `kanaele` an einer Netz-ID über 1022, ein 65. Kanalnetz und ein Kanalnetz, in dessen Bereich der Kanal-IDs ein gespeichertes Netz liegt. Wird ein gewöhnliches Netz zum Kanalnetz, liegen die Felder mit seiner Netz-ID danach auf seiner Ruhe (hören und senden bleiben), und Einheitskennungen darauf fallen weg. Wird die Liste kürzer, fallen Mitgliedschaften, Stummschaltungen und Einheitskennungen auf die weggefallenen Ch. weg, und ein festes `ch` in Rollenfeldern und Phasen darüber verfällt (der Ch. wird dann aufgelöst); entfällt `kanaele` ganz, gilt das für alle Kanal-IDs des Netzes. `NETZ_LOESCHEN` eines Kanalnetzes räumt ebenso alle seine Kanal-IDs. Global verliert seine Ch. nur, wenn kein Rollen- oder Phasenfeld es nennt (`UNGUELTIG`).

**0x0090 AdminNonce** (C->S, Body `{}`): nur für ein Konto mit Admin-Nachweis (auch VORGESEHEN), sonst `KEIN_RECHT`. Antwort `{nonce: b64 (32 Byte), gueltigMs: u32}` (60.000). Die Nonce gilt einmal und ersetzt eine offene derselben Verbindung (3.9).

**0x0091 AdminAnmeldung** (C->S): `unterschrift: b64 (64 Byte, Zweck ANMELDUNG)`, genau eins von `code: str(16)` und `notfallcode: str(32)`. Antwort `{bis: i64, bestaetigtBis: i64, uhrHinweis?: true, notfallcodesUebrig?: u8}`. Fehler: `KEIN_RECHT` (kein ADMIN, kein Nachweis, Nachweis nicht AKTIV oder Unterschrift ungültig; zählt nie als Fehlversuch), `UNGUELTIG` (Nonce fehlt oder abgelaufen), `ZWEITFAKTOR_FALSCH`, `ZWEITFAKTOR_GESPERRT` (`details.bis` bei einer Pause).

**0x0092 AdminBestaetigen** (C->S): genau eins von `code` und `notfallcode`. Nur in einer offenen Admin-Sitzung, sonst `ADMIN_SITZUNG_NOETIG`. Antwort wie AdminAnmeldung; `bestaetigtBis` ist dann jetzt + 5 min.

**0x0093 AdminAbmelden** (C->S, Body `{}`): beendet die Admin-Sitzung ("Verwaltung schließen"); der Funk läuft weiter. Ohne Admin-Sitzung `ADMIN_SITZUNG_NOETIG`. Antwort `{}`.

**0x0094 AdminEinrichtung** (C->S): `op: "ANTRAG" | "GEHEIMNIS_ABHOLEN" | "BESTAETIGEN"`, `unterschrift: b64 (64 Byte)` (Zweck ANTRAG, GEHEIMNIS bzw. EINRICHTUNG, nach vorheriger AdminNonce), bei ANTRAG `schluessel: b64` (SPKI des Admin-Schlüssels, 91 Byte), bei BESTAETIGEN `code`. Antwort `{kennung?, geheimnis?: Base32, uri?: otpauth (höchstens 512; der längste Name, 32 Skalarwerte zu je 4 UTF-8-Byte prozentkodiert, ergibt 508), gueltigBis?, notfallcodes?: [str(14)] 10}`: bei ANTRAG die Kennung, bei GEHEIMNIS_ABHOLEN Geheimnis und URI (genau einmal), bei BESTAETIGEN die Notfallcodes (genau einmal). Ablauf und Fristen in 3.9; eine abgelaufene Einrichtung ergibt `UNGUELTIG` mit dem Hinweis auf `admin-einrichtung-neu`.

**0x4001 ServerLast** (optional): `stufe: u8` 0-2 (7.4), `verworfen: u32`, `ausgangMbit: Zahl`.

**0x4002 Meldung** (optional): `art: str`, `kontoId?`, `geraetId?`, `netzId?`, `details?: Objekt`. Arten und Empfänger:

| Art | Empfänger | Anlass |
| --- | --- | --- |
| RATE | `ZUWEISEN` | ein Sender überschreitet Paket- oder Byte-Topf (mit Stummschalt-Knopf im Client) |
| VERDRAENGUNG_HAEUFIG | jedes ADMIN-Konto (Warnung) | mehr als 2 Verdrängungen je Konto in 5 min (4.6) |
| GERAET_ANTRAG | `GERAETE_FREIGEBEN` (Admin-Sitzung), Inhaber | Entfernungsantrag gegen ein älteres Gerät |
| RECHTE_GEAENDERT | `KONTEN_VERWALTEN` (Admin-Sitzung) | jede Änderung an Stufe oder Rechten |
| ABGLEICH | `KONTEN_VERWALTEN` (Admin-Sitzung) | Sperre oder Aufhebung durch den Discord-Abgleich |
| KOPIERVERDACHT | jedes ADMIN-Konto (Warnung) | kopierte Pakete an einer Sitzung (5.10) |
| ADMIN_SITZUNG | jedes ADMIN-Konto (Warnung) bzw. die Verbindung | `details.op`: BEGONNEN (von einem neuen Gerät, mit `neuesGeraet`, `geraet`) oder BEENDET (mit `grund`, nur an die Verbindung) (3.9) |
| ZWEITFAKTOR_GESPERRT | jedes ADMIN-Konto (Warnung) | Pause nach 5 oder Sperre nach 10 Fehlversuchen, `details.bis` bei der Pause |
| NOTFALLCODE | jedes ADMIN-Konto (Warnung) | ein Notfallcode wurde benutzt, `details.uebrig` |
| ADMIN_GEAENDERT | jedes ADMIN-Konto (Warnung) | `details.op`: ANTRAG, VORGESEHEN, ERNANNT, EINGERICHTET, ZWEITFAKTOR_NEU, NOTFALLCODES, GESPERRT, ENTSPERRT, ZURUECKGESETZT, ENTZOGEN, SCHLUESSEL_ENTFERNT, EIGENTUEMER |
| ANTRAG_NEU | `GERAETE_FREIGEBEN` (Admin-Sitzung) | neuer Antrag ohne Code (3.4) |
| ANTRAEGE_VOLL | jedes ADMIN-Konto (Warnung) | 30 offene Anträge erreicht |
| MITGLIED_GEAENDERT | `KONTEN_VERWALTEN` (Admin-Sitzung) | Mitglieder, Vormerkungen, Rollenkatalog oder Abzeichen geändert (6.13) |
| AUFSTELLUNG_NACHGEZOGEN | `ZUWEISEN` | offener Eintrag der letzten Aufstellung wurde angewendet (6.9) |
| BEAUFTRAGUNG | `KONTEN_VERWALTEN` (Admin-Sitzung), Beauftragter | seit 0.3.0 (6.12 Regel 8): `details.op` ERTEILT, BEGONNEN, ENTZOGEN, ABGEGEBEN, ABGELAUFEN oder BEENDET, dazu `von`, `bis`, `durch` (0 = Konsole), `grund?` und bei BEENDET `warum` (GESPERRT, GELOESCHT, STUFE, ERTEILER) |
| KANALSPERRE | `ZUWEISEN` | seit 0.6.0 (6.17): `details.op` GESETZT, AUFGEHOBEN, ABGELAUFEN oder BEENDET (`warum` AUFTRAG: der Setzende hat kein `ZUWEISEN` mehr; NETZ: Kanal oder Netz gelöscht), dazu `bis`, `durch`; `kontoId` fehlt bei einer Sperre für alle, `netzId` nennt den Kanal |
| EMPFANG_GEDROSSELT | Betroffener | Strom-Abwurf bei Überlast (7.4) |
| EIGENER_VERLUST | Betroffener | Verbindung verliert hoch oder runter dauerhaft viel (7.8) |

**0x4003 Belegung** (optional, seit 0.3.0, K1, F5): `zustandsVersion: i64`, `online: u16`, `mitPlatz: u16`, `plaetze: [{schiffId?, rolleId, belegt: u16, reserviert: u16, kapazitaet?: u8, mindest?: u8}]` höchstens 1088 (64 Einheiten zu 16 Plätzen und 64 Rollen): je Platz einer Einheit (und je Rolle ohne Schiff, dann ohne `schiffId`) BELEGT und RESERVIERT so, wie die Kapazitätsprüfung des Servers sie zählt; `kapazitaet` fehlt bei unbegrenzt. An alle AKTIV-Sitzungen beim `Zustand` und nach jeder Änderung, geprüft nach jedem Commit und alle 10 s (Platzfristen laufen ohne Änderung ab), gesendet nur, wenn sich etwas geändert hat. Daraus bauen das Brett Kopf ("23 online - 19 mit Platz - 4 ohne"), Spaltenköpfe und Ampel, und das Einsteigen die Anzeige "Gunner 1/2". Clients 0.2.0 übergehen sie (4.7).

**0x4004 PhaseGeschaltet** (optional, seit 0.5.0, 6.14): `{nr: u8, name: str(24), kurz: str(12), durch: kontoId (0 = Konsole oder Rückfall nach 12 h), seit: i64}`: die laufende Phase. An alle AKTIV-Sitzungen bei jedem Wechsel (auch beim Rückfall und wenn die laufende Phase gelöscht wird) und nach Welcome hinter der Sync-Folge, sobald die Flotte Phasen hat oder eine andere als die Grundstellung läuft. Clients 0.4.x übergehen ihn (4.7).

**0x4005 Meldestand** (optional, seit 0.12.0, 6.18): `anlass: str(24)`, `von?: kontoId`, `befehl?`, `zustand?`, `ampel?`, `countdown?`, `abfrage?`, jeder Eintrag `{motiv: str(32), groesse: u8 (1-3), zeile?: str(60), von: kontoId, seit: i64, ziel?: i64, form?: str(16), nummer?: u32}`, dazu `text?: {text: str(120), farbe: str(16), groesse: u8 (1-3), dauer: str(16), von: kontoId, seit: i64}` (Textmeldung, M24). Trägt immer den ganzen laufenden Stand: was fehlt, läuft nicht. `befehl` steht nur da, wenn die Meldung ein Befehl war (`anlass` `Befehl`); er bleibt nicht stehen. `text` trägt eine stehende Textmeldung (`dauer` `bleibt`) in jedem Push und nach Welcome; eine mit `8s` nur im Push mit `anlass` `Text`, sie bleibt wie ein Befehl nicht stehen (der Client zeigt sie 8 s ab dem Eingang). Beim Countdown nennt `ziel` die Zielzeit und `form` die Form, bei der Bereit-Abfrage `nummer` ihre Nummer und `ziel` ihr spätestes Ende (2 min nach dem Start). `anlass` ist die Art der Meldung (0x0026), `von` das auslösende Konto; nach Welcome kommt der Stand mit `anlass` `Stand` und ohne `von` (M20, hinter der Sync-Folge, nur wenn etwas läuft; vergangene Befehle nie), nach Ablauf der Bereit-Abfrage mit `BereitAbgelaufen`. An alle AKTIV-Sitzungen nach jeder Meldung. Einen Countdown blendet der Client bei 0 selbst aus; der Server nimmt ihn danach still aus seinem Stand. Clients bis 0.11.9 übergehen den Push (4.7).

**0x4006 BereitStand** (optional, seit 0.12.0, 6.18): `nummer: u32`, `offen: bool`, `bereit: u16`, `online: u16`, `fehlen: [kontoId]` höchstens 500, `von: kontoId`, `bis: i64`. "n von m bereit": `online` zählt die aktiven Sitzungen, `bereit` die davon, die bereit gemeldet haben, `fehlen` nennt die übrigen Konten aufsteigend (Namen aus der Praesenz). Nur an Sitzungen, die gerade senden dürfen (6.18): beim Start der Abfrage, nach jedem neuen Bereit, wenn eine Sitzung beginnt oder endet, wenn ein Admin einem Konto den Haken setzt, nach Welcome und am Ende mit `offen: false` (geschlossen oder nach 2 min abgelaufen, mit dem letzten Stand).

**0x8001 SpracheTunnel** (beide, binär, Anfrage-Nr 0): Der Body ist genau ein vollständiges Sprachdatagramm nach Abschnitt 5 (Kopf, Chiffrat, Tag; hoch 41-569 Byte, runter 45-477 Byte). Es gelten dieselben Schlüssel, Zähler und Replay-Fenster wie über UDP. Die Sitzungs-ID im Kopf MUSS die Sitzung der tragenden TLS-Verbindung sein; sonst `PROTOKOLL` und Trennung. Beim Client gilt dasselbe für runter.

**0x8002 VoiceKeys** (S->C Server-Anfrage, binär, Frist 10 s): Body genau 90 Byte:

```
Offset Laenge Feld
0      1      Epoche      u8
1      1      Grund       u8: 0 = START, 1 = ROTATION
2      32     K_hoch
34     12     IV_hoch
46     32     K_runter
78     12     IV_runter
```

Die Client-Antwort (Typ 0x8002, Flag ANTWORT) ist genau 1 Byte: die installierte Epoche. Eine andere Länge oder eine falsche Epoche ergibt `PROTOKOLL`. Der Client liest den Body in einen eigenen, festgehaltenen Puffer, übergibt die Schlüssel an neue `AesGcm`-Instanzen und nullt den Puffer mit `ZeroMemory`. Interne Puffer von `SslStream` und die Schlüsselkopie in `AesGcm` bis zum `Dispose` lassen sich nicht nullen; das ist hingenommen. Ablauf in 5.8.

### 4.6 Zustandsautomat

Server, je Verbindung:

| Zustand | Erlaubte Client-Nachrichten | Übergang |
| --- | --- | --- |
| TLS | (Handshake) | fertig, ALPN passt, P-256-Zertifikat -> Einstufung (4.1) -> WARTE_HELLO; sonst oder nach 5 s -> ENDE |
| WARTE_HELLO | Hello | Gerät unbekannt oder ENTFERNT -> REGISTRIERUNG; WARTET -> WARTERAUM; FREIGEGEBEN und Konto AKTIV -> AKTIV_SYNC; ENTZOGEN oder bewusst gesperrter Fingerabdruck -> `GERAET_GESPERRT`, ENDE (ein abgelehntes Gerät ist seit 0.7.0 gelöscht und damit unbekannt); Konto GESPERRT -> `KONTO_GESPERRT`, ENDE; etwas anderes oder 3 s -> ENDE |
| REGISTRIERUNG | Registrierung, Antwort auf VerknuepfungBestaetigen, Ping, Bye | Verknüpfung bestätigt -> WARTERAUM; 5 min seit Hello -> ENDE |
| WARTERAUM | Ping, Bye | Freigabe -> Welcome(FREIGEGEBEN) -> AKTIV_SYNC; Ablehnung -> ENDE; 15 min -> `Bye{WARTEZEIT, wiederInMs: 60000}`, ENDE |
| AKTIV_SYNC | Antwort auf VoiceKeys, Ping, Bye | Server sendet VoiceKeys(START, Epoche 0), NetzListe, Mitgliedschaften, Praesenz, PegelVoreinstellungen, Zustand. Antwort auf VoiceKeys -> AKTIV; 10 s ohne Antwort -> ENDE |
| AKTIV | alle nach Katalog und Rechten | Sperre, Entzug, Entfernung, Verdrängung, Ersetzung, Protokollfehler, 10 s ohne jeden Rahmen, Bye -> ENDE |
| ENDE | - | UDP-Schlüssel verwerfen (`AesGcm.Dispose`, eigene Puffer genullt), Sitzungs-ID 24 h nicht neu vergeben, Praesenz `online = false`, die Platzfrist (6.9) beginnt. War die Sitzung AKTIV, kommt ihr Präfix in die Vorrangspur |

- Eine Nachricht, die im aktuellen Zustand nicht erlaubt ist, ergibt vor AKTIV `PROTOKOLL` und Trennung. In AKTIV ergibt eine Anfrage ohne Recht `KEIN_RECHT` ohne Trennung.
- Verdrängung: Erst wenn eine Verbindung eines FREIGEGEBEN-Geräts AKTIV_SYNC erreicht, endet eine ältere Sitzung desselben Kontos. Aus REGISTRIERUNG oder WARTERAUM heraus wird nie verdrängt. Ist es dasselbe Gerät (gleicher Fingerabdruck), endet die alte mit `Bye{ERSETZT}` und zählt nicht als Verdrängung. Sonst endet sie mit `Bye{VERDRAENGT, durch: {geraetName, anzeige}}`. Mehr als 2 Verdrängungen zwischen verschiedenen Geräten eines Kontos in 5 min lösen `Meldung{VERDRAENGUNG_HAEUFIG}` aus. Weitere verdrängende Verbindungen dieses Kontos lehnt der Server dann mit `VERDRAENGEN_GESPERRT` ab, bis ein Admin entscheidet oder 30 min vergangen sind; die laufende Sitzung bleibt. Ein solches Hin und Her deutet auf einen kopierten Schlüssel.
- Unfreigegebene Verbindungen (Einstufung "unbekannt" nach TLS): serverweit höchstens 20, je Präfix höchstens 2. Freigegebene Geräte zählen nie mit.

Client:

| Zustand | Bedeutung | Übergang |
| --- | --- | --- |
| GETRENNT | keine Verbindung | Start, Schnellphase oder Backoff (7.1) abgelaufen -> VERBINDEN. Kein automatischer Neuversuch nach `KONTO_GESPERRT`, `GERAET_GESPERRT`, `VERSION_ZU_ALT`, `VERDRAENGEN_GESPERRT` und nach `Bye{VERDRAENGT}` auf der aktuellen Verbindung, seit 0.6.3 auch nicht nach `Bye{ENTFERNT}` (Gerät entfernt, Konto gelöscht oder wartendes Gerät verfallen, seit 0.7.0 auch Antrag abgelehnt): Der Client zeigt "vom Funk abgemeldet", und erst Verbinden durch den Nutzer stellt einen neuen Antrag (vorher verband er gleich neu und stellte ihn still). Bei VERDRAENGT zeigt der Client "verdrängt durch <Gerätename> (<Anzeigeform>)" mit den Knöpfen "zurückholen" (neu verbinden) und "Admin informieren". Bei `GERAET_GESPERRT` (bewusste Sperre) sagt er, dass das Gerät gesperrt ist, und verweist an einen Admin; einen neuen Geräteschlüssel bietet er nicht an (3.2). Endet ein Antrag mit `Bye{ENTFERNT}` (nie AKTIV gewesen), sagt er, dass der Antrag abgelehnt oder verfallen ist und wie man neu beantragt |
| VERBINDEN | DNS, TCP (7.1), TLS mit Pin, Hello | Welcome UNBEKANNT -> REGISTRIERUNG; WARTET -> WARTERAUM; FREIGEGEBEN -> SYNC |
| REGISTRIERUNG | Code anzeigen, auf Verknüpfung warten, Rückfrage beantworten | GeraetStatus WARTET -> WARTERAUM; Code abgelaufen oder Fehler-Push -> neuen Code anbieten |
| WARTERAUM | "Warte auf Freigabe" | GeraetStatus FREIGEGEBEN und Welcome -> SYNC; `Bye{WARTEZEIT}` -> nach `wiederInMs` (+/-20 %) neu verbinden |
| SYNC | VoiceKeys beantworten, Stand sammeln | Push Zustand -> AKTIV/UDP_BINDEN |
| AKTIV/UDP_BINDEN | ANMELDUNG senden (5.10) | GEBUNDEN -> AKTIV/UDP; 3 s ohne Bindung -> TunnelModus an -> AKTIV/TUNNEL |
| AKTIV/UDP | Normalbetrieb, KEEPALIVE alle 2 s | UDP-Ausfall (5.12) -> AKTIV/TUNNEL |
| AKTIV/TUNNEL | Sprache im Steuerkanal | UDP-Probe (5.12); 10 s sauber -> TunnelModus aus -> AKTIV/UDP |
| jeder | - | Verbindungsverlust, 6 s ohne Ping-Antwort, schwerer Fehler -> GETRENNT |

Der Client sendet ab Hello alle 2 s ein Ping, unabhängig vom übrigen Verkehr. Bleibt 6 s lang jede Ping-Antwort aus, gilt die Verbindung als tot. Der Server trennt nach 10 s ohne jeden Rahmen vom Client. Vor einem Neuaufbau schließt der Client die alte Verbindung und wertet `Bye` nur auf der aktuellen Verbindung aus.

### 4.7 Versionierung

- Hauptversion: ALPN `tacrelay/1` für Steuerkanal und Framing, dazu Byte 0 = 1 im Sprachpaket. Ein Bruch bekommt ein neues ALPN (`tacrelay/2`). Der Server darf in einer Übergangszeit beide anbieten.
- Nebenversion: `Hello.minor` und `Welcome.minor`, es gilt das Minimum. Neue Nachrichten einer Nebenversion liegen in den optionalen Typbereichen oder werden nur nach vereinbarter Nebenversion gesendet. Neue JSON-Felder sind immer optional, unbekannte werden übergangen.
- Im Sprachpaket führen eine unbekannte Version, ein unbekannter Typ, ein gesetztes reserviertes Byte oder eine unbekannte Zielart zum Verwerfen. Reservierte Kennzeichen-Bits setzt der Server beim Weiterleiten auf 0.
- Mindestversion: `mindestClientVersion` in Welcome, durchgesetzt mit `VERSION_ZU_ALT`. Das Update selbst läuft über den signierten Weg, nicht über dieses Protokoll. `Hello.client.version` ist die Fassung des Baus (Directory.Build.props); Clients bis 0.1.0 meldeten fest "0.1.0".
- Stufe 2 (0.3.0) hält Clients 0.2.0 und 0.1.0 am Netz, ohne `minor` zu erhöhen: Die neuen Pushes liegen im optionalen Bereich (0x4003 Belegung), neue Felder in bestehenden Nachrichten sind optional (`konto.beauftragung`, `einstellungen.selbstwahlDurch`/`selbstwahlBis`/`nachzuegler`, `schiffe[].art`/`typ`, `plaetze[].mindest`, Praesenz-Details, `ersteller`), und Aufzählungswerte, die ein alter Client nicht kennt (`ERTEILT` usw.), stehen nur in Nachrichten, die er nie anfordert (0x0017, 0x0038), oder als Text in `Meldung.details`. Neue Anfragen stellen nur neue Clients. Umgekehrt stellt ein Client 0.3.0 die Pflichttypen 0x0017 und 0x0038 nur, wenn der Server in `Welcome.server.version` mindestens 0.3.0 meldet (`Versionen.ServerKennt`): Ein Server 0.2.0 kennt sie nicht und trennt bei einem unbekannten Pflichttyp mit `PROTOKOLL`. Statt zu senden, antwortet sich der Client selbst mit `VERSION` und einem Satz. Geprüft am 01.10.2026 mit den Programmen von 0.2.0 (funk-echo, echo-probe) gegen einen Server 0.3.0, während die Konsole Auftrag, Einheit TRUPP, Sperre ohne Nachzügler und Entzug durchspielte: beide blieben verbunden, ohne Fehler.
- Ausbaustufe S2 (0.5.0) hält Clients 0.4.x am Netz auf dieselbe Weise: neuer Push im optionalen Bereich (0x4004 PhaseGeschaltet), neue Felder optional (`NetzListe.phasen`, `Mitgliedschaften.phase`, `konto.alsFunkerBis` in Welcome und Mitgliedschaften, `alsFunker` in Praesenz-Details und KontenVerzeichnis), kein neuer Wert in den strengen Aufzählungen, die Clients lesen (`Recht`, `Stufe`, `MitgliedschaftsQuelle`, `Fehlercode`). Neue Werte von `VerwaltungsOperation` stehen nur in Anfragen. Die Pflichttypen 0x003C und 0x003D stellt ein Client 0.5.0 nur an Server ab 0.5.0 (`Versionen.ServerFassungFuer`). Ein Server 0.4.x liest einen Zustand von 0.5.0 (unbekannte Felder übergeht er), schreibt Phasen, laufende Phase und Funker-Modus aber nicht zurück: Beim ersten Schreiben sind sie fort. Gemessen am 01.10.2026 mit den Programmen des Gesamtpakets 0.4.0 (20261001_1539): Server 0.5.0 auf dem Zustand von 0.4.0 mit laufender Phase 2, funk-echo 0.4.0 bekommt NetzListe mit `phasen` und bleibt über den Rückfall auf Phase 1 (Push 0x4004) verbunden, Echo-Probe 0.4.0 und 0.5.0 BESTANDEN; Server 0.4.0 auf dem Zustand von 0.5.0 startet, Echo-Probe 0.4.0 BESTANDEN, nach dem ersten Schreiben fehlen `phasen`, `nebenstand.phase` und `alsFunkerBis`. Die Sicherung der Flotte im Verwaltungsteil (`Flottenentwurf.AlsNetzListe`) enthält die Phasen noch nicht; das kommt mit der Clientseite.
- Stufe 3 (0.6.0) hält Clients 0.5.x am Netz, solange es kein Kanalnetz gibt: neue Felder optional (`netze[].kanaele`, `gruppe`, `ch` in Rollen- und Phasenfeldern, `Mitgliedschaften.rolle`, `rolle` in Praesenz-Details), kein neuer Wert in den strengen Aufzählungen, die neue Anfrage 0x0039 stellt ein Client 0.6.0 nur an Server ab 0.6.0. **Versionstor:** Gibt es mindestens ein Kanalnetz (auch Global mit `kanaele`), beantwortet der Server ein Hello mit `client.version` unter 0.6.0 mit `Fehler{VERSION_ZU_ALT, details: {mindest: "0.6.0"}}` und trennt; Verbindungen unter 0.6.0, die schon Hello hatten (auch im WARTERAUM), trennt er auf dieselbe Weise im Commit, der das erste Kanalnetz anlegt. Clients 0.5.x versuchen danach nicht neu (4.6). Grund: Ein Client 0.5.x kennt keine Kanal-IDs; jede gestempelte Kanal-ID wäre für ihn ein unbekanntes Netz (C8, Zustand alle 5 s), und Ch. und Ruhe könnte er weder zeigen noch wählen. Die NetzListe selbst bliebe mit 31 Einträgen unter 128 gültig (Variante C), die Trennung mit `ZU_GROSS` aus Variante A gibt es hier nicht. Liegt `mindestClientVersion` aus server.json höher, gilt sie; gibt es kein Kanalnetz mehr (alle gelöscht), fällt das Tor. Ein Server 0.5.0 liest einen Zustand von 0.6.0 nur, solange kein Konto und keine Einheit eine Kanal-ID hält (sonst lehnt er den Stand beim Start ab); nach dem Löschen aller Kanalnetze ist das so, die neuen Felder (`kanaele`, `gruppe`, `ch`, `gewaehlteRolle`) übergeht er und schreibt sie beim ersten Commit nicht zurück.
- 0.8.0 hält Clients 0.7.x am Netz: kein neuer Typ, kein neues Pflichtfeld. Der neue Wert ENTSPERREN steht nur in einer Anfrage (`GeraetEntscheiden.entscheidung`), die ein Client 0.7.x nie stellt; nach dem Aufheben geht der bekannte Push `GeraetStatus{ENTFERNT}` an die Admins. Umgekehrt stellt ein Client 0.8.0 ENTSPERREN nur, wenn der Server in `Welcome.server.version` mindestens 0.8.0 meldet (`Versionen.EntsperrenAb`); ein Server 0.7.x kennt den Wert nicht, antwortet `UNGUELTIG` und bleibt verbunden. Der Verwaltungsteil zeigt dann statt des Knopfs den Hinweis, dass es erst ab Server 0.8.0 geht. Das neue optionale Feld `herkunft` an `netze[]` und `rollen[]` (NetzListe, `NETZ_SETZEN`, `ROLLE_SETZEN`) übergeht eine Gegenstelle 0.7.x wie jedes unbekannte Feld: Ein Server 0.7.x speichert es nicht, ein Client 0.7.x schickt es nie mit, und der Server 0.8.0 behält dann die bisherige Herkunft. Der Verwaltungsteil 0.8.0 zählt eine abweichende Herkunft nie als Änderung, so dass an einem Server 0.7.x kein Entwurf offen bleibt. Ein Server 0.7.x liest eine lage.json von 0.8.0 (das Feld `herkunft` an Netzen und Rollen übergeht er) und schreibt es beim nächsten Commit nicht zurück; nach dem Rückweg zählt die abgeleitete Herkunft.
- 0.11.7 (Abzeichen) hält Clients bis 0.11.6 am Netz: Die neue Anfrage 0x0025 stellt nur ein Client ab 0.11.7 und nur an einen Server ab 0.11.7 (`Versionen.AbzeichenAb`; ein älterer Server trennt bei einem unbekannten Pflichttyp mit `PROTOKOLL`). Alle neuen Felder sind optional (`ProfilSetzen.abzeichenVorn`/`abzeichenAus` und in der Antwort, `konto.abzeichen`/`abzeichenVorn`/`abzeichenAus`, `Praesenz.eintraege[].abzeichen`/`abzeichenVorn`, `Mitgliedskarte.abzeichen`, `Mitglied LISTE` `raenge`/`taetigkeiten`), kein neuer Wert in einer strengen Aufzählung. Ein Client bis 0.11.6 übergeht sie und zeigt weiter nur die Zeichen; ein Server bis 0.11.6 übergeht die neuen Felder in `ProfilSetzen`. Ein Server bis 0.11.6 liest eine `mitglieder.json` von 0.11.7 (die vier neuen Felder je Mitglied übergeht er) und schreibt sie beim nächsten Speichern nicht zurück: Die Abzeichen sind dann fort. Umgekehrt lädt 0.11.7 eine ältere Datei mit leeren Abzeichen.
- 0.12.0 (Meldesystem) hält Clients bis 0.11.9 am Netz: Die neuen Pushes liegen im optionalen Bereich (0x4005 Meldestand, 0x4006 BereitStand), die neuen Anfragen 0x0026 und 0x0027 stellt nur ein Client ab 0.12.0 und nur an einen Server ab 0.12.0 (`Versionen.MeldesystemAb`), die neuen Felder sind optional (`Mitglied` `meldungenSenden` in AENDERN und LISTE, `konto.meldungenSenden`), es gibt keinen neuen Wert in einer strengen Aufzählung und kein neues Recht (der Haken ist ein Mitgliedsfeld wie die Abzeichen; ein neuer Wert in `Recht` brächte ältere Clients aus dem Tritt). `art`, `anlass`, `motiv`, `form`, `farbe` und `dauer` sind offener Text: Neue Werte brauchen keinen neuen Typ, ein Server, der eine Art nicht kennt, antwortet `UNGUELTIG`; eine Farbe, die ein Client nicht kennt, zeigt er weiß. Ein Server bis 0.11.9 übergeht `meldungenSenden` in `Mitglied AENDERN` still und meldet Erfolg; die Verwaltung ab 0.12.0 schickt den Haken deshalb nur an Server ab 0.12.0 (`Verwaltungslage.MeldungenBekannt`). Ein Server bis 0.11.9 liest eine `mitglieder.json` von 0.12.0 (das Feld `meldungenSenden` je Mitglied übergeht er) und schreibt es beim nächsten Speichern nicht zurück: Die Haken sind dann fort. Umgekehrt lädt 0.12.0 eine ältere Datei ohne Haken. Der laufende Stand der Meldungen lebt nur im Speicher; nach einem Neustart des Servers ist er leer.

### 4.8 Ratenlimits und Grenzen

Alle Limits sind Token-Buckets (Rate je Sekunde, Burst) mit monotoner Uhr. Die Werte sind Startwerte (Offene Punkte). Grenzen "je Präfix" zählen nach 0.

| Bereich | Grenze |
| --- | --- |
| TCP-Annahme und TLS-Handshakes | je Präfix: unbekannt 10 je Minute (Burst 5), Präfix aus der Vorrangspur 60 je Minute (Burst 10); gleichzeitige Verbindungen je Präfix 8 (gemeinsames NAT). Global 200 Handshakes/s nur als CPU-Schutz, nach Messung festzulegen; die Vorrangspur ist davon ausgenommen |
| Vorrangspur | Präfixe mit einer AKTIV-Sitzung in den letzten 14 Tagen, als HMAC-SHA256-Werte (Schlüssel per `LoadCredential`) dauerhaft gespeichert, damit sie einen Neustart überstehen; höchstens 2000 Einträge |
| Unfreigegebene Verbindungen | je Präfix 2, global 20; nur Verbindungen mit Einstufung "unbekannt" nach TLS |
| Fristen | TLS 5 s, Hello 3 s, REGISTRIERUNG 5 min, WARTERAUM 15 min |
| Registrierung | 3 Codes je Verbindung, 10 je Präfix und Stunde; globaler Zähler 100 je Stunde nur als Alarm |
| Verknüpfen über den Bot | 5 Fehlversuche und 3 Erfolge je Discord-ID und Stunde, 30 Erfolge je Stunde global, darüber `GRENZE` und Alarm (3.8) |
| Steuerrahmen gesamt je Sitzung | 30/s, Burst 60; 256 KiB/s, Burst 1 MiB (ohne SpracheTunnel) |
| Zustandsmeldungen: SendeauswahlSetzen, HoerenSetzen, PegelSetzen, SprechanzeigeAbo | je Art 4/s, Burst 8. Überschuss wird zusammengefasst und ist kein Verstoß: Je Sitzung und Art gibt es einen wartenden Platz; eine neuere Anfrage ersetzt die wartende, die ersetzte bekommt `Fehler{UEBERHOLT}`; die wartende wird angewendet, sobald der Topf es erlaubt. Es gilt also immer der letzte Stand |
| FeldSetzen, SatzAnwenden, RolleWaehlen (seit 0.6.0) | zusammen 5/s, Burst 10; eine Flut endet bei `GRENZE` ohne Trennung (erst mehr als 50 Verstöße in 60 s trennen). Der Client wartet nach einem Ch.-Wechsel per Mausrad 400 ms Ruhe vor dem Senden (6.16) |
| Kanalnetze (seit 0.6.0) | höchstens 64 (zählen zu den 128 Netzen), Netz-ID bis 1022, je 2 bis 31 Ch.-Bezeichnungen zu 20 Zeichen, Gruppe 20 Zeichen; `RolleWaehlen.ch` höchstens 16 |
| EinsteigenRolleSchiff, Verschieben, Stummschalten, PlaetzeFreigeben, KanalSperre (seit 0.6.0) | 10/s, Burst 20 |
| Kanalsperren (seit 0.6.0) | je Konto 16, für alle 64, zusammen 500; höchstens 24 h |
| AufstellungAnwenden | 2/s, Burst 5 |
| AufstellungSpeichern/Loeschen, Verwaltung (auch `PAKET`, ein Paket zählt einmal), KontoVerwaltung, RegelTabelle, Geräte-Nachrichten, Journal, Rollenkatalog, Mitglied, Untergilde, AntraegeAblehnen, Beauftragung, Abzeichen (seit 0.11.7) | 1/s, Burst 5 |
| KontenVerzeichnis | 1 je 5 s, Burst 2 |
| Funkermodus (seit 0.5.0) | 1/s, Burst 2 |
| PhaseSchalten (seit 0.5.0) | 1 je 2 s, Burst 2; eine Flut endet bei `GRENZE` ohne Trennung (erst mehr als 50 Verstöße in 60 s trennen) |
| MeldungSenden (seit 0.12.0, M19) | 1 je 2 s, Burst 1: je Sender höchstens eine Meldung in 2 s (eine Sitzung je Konto); früher `GRENZE` ohne Trennung. Der Client zählt die 2 s ab der Antwort auf die vorige und kommt so nie zu früh an |
| BereitMelden (seit 0.12.0) | 1/s, Burst 2 |
| Meldesystem (seit 0.12.0, 6.18) | Art 24, Motiv 32, Zeile 60, Form 16 Zeichen; Text 120, Farbe und Dauer je 16 Zeichen (Textmeldung); Größe 1-3; Countdown höchstens 24 h voraus; Bereit-Abfrage höchstens 2 min; `BereitStand.fehlen` höchstens 500 |
| Phasen (6.14) | Grundstellung und höchstens 4 Phasen, Name 24 und Kurzname 12 Zeichen, zusammen höchstens 512 Variantenfelder, je Rolle und Phase 16; Rückfall auf die Grundstellung 12 h nach dem letzten Schalten |
| Aufträge (6.12) | höchstens 4 laufende oder geplante, Beginn bis 14 Tage voraus, Dauer bis 24 h, Grund 48 Zeichen; `Verwaltung PAKET` 200 Schritte; Typ einer Einheit 32 Zeichen |
| ProfilSetzen, Lieblingsrollen | 1/s, Burst 2 |
| Mitgliedskarte | 5/s, Burst 10 |
| AdminAnmeldung, AdminBestaetigen, AdminAbmelden, AdminEinrichtung | zusammen 1/s, Burst 3 |
| AdminNonce | eigener Eimer, 1/s, Burst 3: Eine Oberfläche, die nach einem Tippfehler gleich wieder Nonce und Anmeldung schickt, scheitert nicht an der zweiten Nachricht (Offener Punkt 30) |
| Zweitfaktor (3.9) | Fehlversuche nur mit gültiger Unterschrift oder in einer Admin-Sitzung; 5 in 15 min ergeben 15 min Pause, 10 in 24 h sperren bis zur Konsole. Nonce 60 s, einmal. Einrichtung: Geheimnis 10 min nach der Ernennung abholbar, erster Code 10 min danach |
| Admin-Sitzung (3.9) | Leerlauf 30 min, höchstens 12 h, frische Bestätigung 5 min |
| Anträge ohne Code (3.4) | dieselben Töpfe wie Registrierung, dazu höchstens 30 offene Anträge (`ANTRAEGE_VOLL`) |
| Mitglieder (6.13) | Rollenkatalog 64 Einträge, Vormerkungen 500, Rollen je Mitglied 16, Lieblingsrollen 8, Untergilden 32 mit je 40 Mitgliedern (nur noch Prüfgrenze der Hülle 0x001D, seit 0.11.8), "Liste einfügen" 200 Zeilen zu je 200 Zeichen, Mitgliederliste 250 je Seite, SC-Namen 3 je Konto, Admin-Schlüssel 3 je Admin |
| Ablage | 1/s, Burst 5 |
| RaidHelperImport, RaidHelperEvents | zusammen 1 je 10 s je Sitzung; serverweit 1 Abruf je 5 s an Raid-Helper |
| Zustand (Anfrage) | 1 je 5 s, Burst 2 |
| Ping | 2/s |
| EmpfangsBericht | 1 je 2 s |
| KeyRotation | 1 je 10 s |
| Sprache je Sitzung | 60 Pakete/s, Burst 20; Opus-Nutzlast 16.000 Byte/s, Burst 4000 Byte (6.3) |
| Verstoß | `Fehler{GRENZE}` (höchstens 1 Meldung/s), die Nachricht wird verworfen. Mehr als 50 Verstöße in 60 s: Trennung, und dieses Gerät (Fingerabdruck, nicht die IP) ist 60 s lang gesperrt. Andere Konten hinter derselben IP sind nicht betroffen |

Die Grenzen des Sprachkanals stehen in 5.9, 5.10 und 7.5.

### 4.9 Fehlercodes

| Code | Bedeutung | Trennung |
| --- | --- | --- |
| PROTOKOLL | Rahmen, JSON oder Zustand verletzt | ja |
| VERSION | Hauptversion oder Nebenversion unvereinbar | ja |
| VERSION_ZU_ALT | Client unter der Mindestversion | ja |
| ZU_GROSS | Grenze einer Rahmen- oder Feldlänge verletzt | ja |
| UNGUELTIG | Inhalt verletzt eine Regel | nein |
| UNBEKANNT | ID gibt es nicht oder sie gehört nicht dem Anfragenden | nein |
| KEIN_RECHT | fehlendes Recht oder Rang (6.10) | nein |
| VERALTET | Version oder basisVersion stimmt nicht, mit `details` | nein |
| GRENZE | Ratenlimit | nein (Ausnahme 4.8) |
| UEBERHOLT | Zustandsmeldung durch eine neuere ersetzt (4.8), kein Verstoß | nein |
| CODE_UNGUELTIG | Einmalcode falsch, abgelaufen oder verbraucht (nicht unterschieden); nur an den Bot | - |
| GERAETE_LIMIT | mehr als 3 aktive Geräte | nein |
| NUR_ADMIN_WERKZEUG | privilegiertes Konto, nur über das Admin-Werkzeug | nein |
| PLATZ_BELEGT | Kapazität erreicht, mit `details` | nein |
| SELBSTWAHL_GESPERRT | Selbstwahl ist gerade gesperrt | nein |
| FELD_FEST | festes Feld, nur die Einsatzleitung (`ZUWEISEN`) ändert es | nein |
| GLOBAL_PFLICHT | Global lässt sich nicht abschalten | nein |
| GERAET_GESPERRT | Gerät ENTZOGEN oder sein Fingerabdruck bewusst gesperrt (3.6); bis 0.6.x auch ABGELEHNT | ja |
| KONTO_GESPERRT | Konto GESPERRT; als Push nach einer gescheiterten Verknüpfung in REGISTRIERUNG ohne Trennung | ja |
| VERDRAENGT | neue Sitzung desselben Kontos von einem anderen Gerät | ja |
| VERDRAENGEN_GESPERRT | zu häufiges Verdrängen, Admin entscheidet (4.6) | ja |
| SERVER_VOLL | keine neuen Registrierungen und unfreigegebenen Verbindungen (7.4) | ja |
| EXTERN | Raid-Helper nicht erreichbar, kein Server-Key oder Antwort ungültig | nein |
| INTERN | Serverfehler | nein |
| NAME_VERGEBEN | SC-Name gehört schon einem anderen Konto oder einer Vormerkung, mit `details.name` (3.3) | nein |
| ADMIN_SITZUNG_NOETIG | Verwaltungsrecht vorhanden, aber keine offene Admin-Sitzung (3.9) | nein |
| SCHRITT_BESTAETIGEN | heikle Handlung ohne frischen Code, mit `details.bis` (Ende der Admin-Sitzung); der Client fragt einen Code und sendet erneut | nein |
| ZWEITFAKTOR_FALSCH | Code oder Notfallcode falsch oder schon benutzt | nein |
| ZWEITFAKTOR_GESPERRT | Pause (mit `details.bis`) oder Sperre bis zur Konsole | nein |
| ANTRAEGE_VOLL | 30 offene Anträge (3.4) | nein |

Die sechs Codes der Stufe 1 stehen direkt in v1, weil noch kein Server im Betrieb war; `mindestClientVersion` hält ältere Testclients fern. Clients 0.1.0 (seit dem 30.09.2026 am Heimserver) schicken keine Anfrage, die einen davon auslöst: Sie kennen weder Anträge mit Namen noch Verwaltungsnachrichten (geprüft am Quelltext von 0.1.0). Deshalb bleibt `mindestClientVersion` in der Vorlage bei 0.1.0, bis alle das Setup 0.2.0 haben.

### 4.10 Größen im ungünstigsten Fall

Gerechnet mit 130 Byte je Name (32 Skalarwerte zu höchstens 4 Byte UTF-8 plus Anführungszeichen) und kompaktem JSON nach 4.3. Jede Nachricht bleibt mit mehr als dem Faktor 2 unter der Rahmengrenze von 1 MiB. P-02b prüft das mit ausgeschöpften Höchstwerten.

| Nachricht | ungünstigster Fall | Rechnung |
| --- | --- | --- |
| NetzListe | ca. 240 KB | 128 Netze zu 360 B, 64 Schiffe zu 970 B (8 Kennungen, 16 Plätze), 64 Rollen zu 2 KB (16 Felder) |
| Praesenz voll | ca. 360 KB | 100 Sitzungen mit Details zu 2,6 KB, dazu 400 Konten ohne Sitzung mit Platz zu 250 B |
| PegelVoreinstellungen | ca. 100 KB | 2000 Einträge zu 50 B |
| AufstellungSpeichern bzw. AufstellungListe mit id | ca. 260 KB | 200 Einträge zu 1,3 KB (16 Felder) |
| GeraeteListe | ca. 175 KB | 500 Geräte zu 350 B |
| RaidHelperImport (Antwort) | ca. 90 KB | 200 Anmeldungen zu 450 B |
| Mitglied LISTE (eine Seite) | ca. 350 KB | 250 Einträge zu 1,4 KB (5 Namen, 16 Rollen, 8 Wünsche, Notiz 80 Zeichen); seit 0.11.7 mit 9 Abzeichen gemessen 428 KB |
| Praesenz voll (Stufe 1) | ca. 450 KB | wie oben, je Eintrag dazu Hauptname und Zeichen (180 B) |
| Praesenz voll (0.11.7) | ca. 750 KB | Stufe 3 (gemessen 638 KB), je Eintrag dazu 9 Abzeichen und eines vorn (220 B, zusammen 110 KB); über der halben, unter der Rahmengrenze, P-02b meldet es als Befund |
| GeraeteListe (Stufe 1) | ca. 470 KB | 500 Geräte zu 750 B mit Namen des Antrags, dazu 30 offene Anträge mit 8 Vorschlägen zu 400 B |
| Untergilde LISTE | ca. 200 KB bis 0.11.7, seit 0.11.8 leer | 32 Untergilden mit 40 Mitgliedern zu 150 B |
| Rollenkatalog | ca. 190 KB | 64 Einträge zu 3 KB (8 Raid-Helper-Klassen) |
| Journal | ca. 180 KB | 200 Einträge zu 900 B |
| Ablage | 16 KiB | Grenze |
| NetzListe (Stufe 2) | 257 KB gemessen | wie oben, je Einheit dazu Art, Typ (32 Zeichen) und je Platz `mindest` |
| Praesenz voll (Stufe 1) | 593 KB gemessen | wie oben, für alle 500 Einträge Hauptname, Discord-Name, Untergilde und drei Zeichen; die Schätzung von 450 KB war zu niedrig |
| Praesenz voll (Stufe 2) | 605 KB gemessen | dazu je Sitzung mit Details `quelle`, `durch`, `seit`, `letzterPlatz` (zusammen 12 KB) |
| Belegung | 109 KB gemessen | 1088 Plätze zu 100 B |
| KontenVerzeichnis (Antwort) | 376 KB gemessen | 500 Konten zu 750 B (vier Namen) |
| Beauftragung (Antwort) | 3 KB gemessen | 4 Aufträge |
| NetzListe (S2, 0.5.0) | 323 KB gemessen | wie Stufe 2, dazu 5 Phasen mit Namen aus Vier-Byte-Zeichen und 512 Variantenfeldern mit Kennung (16 Zeichen); die Phasen legen 66 KB dazu, etwa 128 B je Feld, Faktor 3,25 unter der Rahmengrenze |
| NetzListe (Stufe 3, 0.6.0) | 483 KB gemessen | wie S2, davon 64 Kanalnetze mit je 31 Ch.-Bezeichnungen und einer Gruppe aus Vier-Byte-Zeichen, `ch` in allen Rollen- und Phasenfeldern; Faktor 2,17 unter der Rahmengrenze (`Stufe3KanalIdTests`). Die alte Kanalvorlage (bis 0.12.0) mit 31 Kanalnetzen lag bei wenigen KB; die Vorlage Star Citizen hat 10 |
| Praesenz voll (Stufe 3, 0.6.0) | 638 KB gemessen | wie Stufe 2, dazu `rolle` und `alsFunker` je Eintrag mit Details und alle 500 Kanalsperren als `gesperrt` (31 Konten zu 16, eines zu 4); die Sperren legen etwa 30 KB dazu, Faktor 1,64 unter der Rahmengrenze (`SteuerGroessenTests.P02b_Stufe3_KanalsperrenMitgliedschaftenPraesenz`) |
| Mitgliedschaften (Stufe 3, 0.6.0) | 8 KB gemessen | 16 Felder, 17 Stummschaltungen und 80 Kanalsperren (16 eigene, 64 für alle) |
| KanalSperre (Antwort, 0.6.0) | 40 KB gemessen | alle 500 Sperren, Faktor 26 |

Stufe 3 legt die Kanäle nicht in die Praesenz (nur `rolle` in den Details, 2 Byte je Eintrag). Ein Ch.-Wechsel schreibt `lage.json` neu; gemessen im Speicher auf dem Entwicklungsrechner (Windows, `Stufe3LastTests`, nicht Linux): 40 Funker in einer großen Flotte (31 Kanalnetze, 21 Rollen) zu 9 Kanälen ergeben 76 KB und einen Commit von 4,7 ms im Median, 500 Konten zu 16 Kanälen 753 KB und 9,4 ms; der Schnappschuss mit 100 Sitzungen und 900 verschiedenen Kanälen baut sich in 1,8 ms, die Weiterleitung an 99 Hörer bleibt unter 0,1 ms je Paket. Die Last mit 40 Funkern über das Netz misst Schritt 3.6.

Praesenz voll liegt damit im ungünstigsten Fall bei Faktor 1,73 (seit Stufe 3 mit allen Kanalsperren 1,64) statt über 2 unter der Rahmengrenze (Bericht in P-02b, gemessen am 01.10.2026). Schon Stufe 1 lag mit 593 KB darüber; Stufe 2 legt 2 % dazu. Im ungünstigsten Fall heißt: 500 Konten, je vier Namen aus 32 Vier-Byte-Zeichen, 100 Sitzungen mit allen Details. Ein Ausweg, falls nötig: Praesenz voll in Teilen senden (Offene Punkte).

Im Tunnelbetrieb stehen große Steuerrahmen vor der Sprache (Head-of-line-Blocking). Das ist als Notbetrieb hingenommen; typische NetzListen liegen bei 20-40 KB.

## 5. Sprachkanal (UDP)

### 5.1 Überblick

- Der Client hat einen UDP-Socket auf einem Ephemeral-Port, mit `Connect` auf die IP-Adresse der bestehenden TLS-Verbindung und den öffentlichen UDP-Port aus Welcome. Er fragt dafür nicht erneut DNS, so gibt es kein Rennen nach einem Adresswechsel. Nie ein fester Port und nie ein Lauschen auf allen Schnittstellen.
- Der Client sendet einen Strom hoch, auch beim Senden in mehrere Netze: Der Server fächert auf.
- Der Server prüft, stempelt und versiegelt je Empfänger neu, mit dessen Schlüssel.
- Ein Datagramm ist höchstens 1200 Byte groß (UDP-Nutzlast); tatsächlich höchstens 569 Byte hoch und 477 Byte runter. Die Opus-Nutzlast ist höchstens 400 Byte groß. 1200 + 48 Byte IPv6/UDP liegen unter 1280, und 1200 + 28 passen auch durch WireGuard über PPPoE (1412).

### 5.2 Gemeinsamer Kopf (16 Byte, vollständig AAD)

```
Offset Laenge Feld
0      1      Version      u8 = 0x01
1      1      Typ          u8 (5.3)
2      1      Epoche       u8
3      1      reserviert   u8 = 0x00
4      4      Sitzungs-ID  u32 (hoch: Sender, runter: Empfaenger)
8      8      Zaehler      u64
16     n      Chiffrat     n = Laenge des Klartexts
16+n   16     Tag          AES-GCM-Tag, 16 Byte
```

AAD sind genau die 16 Kopfbytes. Die Sitzungs-ID ist öffentlich. Sie dient nur der Zuordnung, nie der Authentisierung.

### 5.3 Pakettypen

| Typ | Name | Richtung | Klartext |
| --- | --- | --- | --- |
| 0x01 | ANMELDUNG | hoch | 16 Byte 0x00 |
| 0x02 | PFAD_PRUEFUNG | runter | 16 Byte Zufall (Challenge) |
| 0x03 | PFAD_ANTWORT | hoch | 16 Byte, Echo der Challenge |
| 0x04 | KEEPALIVE | hoch | nr u32, zeitMs u32 (Client-Uhr, monoton, mod 2^32), 8 Byte 0x00 |
| 0x05 | KEEPALIVE_ECHO | runter | nr u32, zeitMs u32 (Echo), serverZeitMs u32 (monotone Serveruhr, mod 2^32), 4 Byte 0x00 |
| 0x10 | SPRACHE_HOCH | hoch | 5.4 |
| 0x11 | SPRACHE_RUNTER | runter | 5.5 |

Die Typen 0x01-0x05 sind immer genau 48 Byte groß. Ein Typ in der falschen Richtung wird vor dem Entschlüsseln verworfen.

### 5.4 Klartext SPRACHE_HOCH

```
Offset Laenge Feld
0      4      Frame-Nr      u32
4      2      Sprechphase   u16
6      1      Kennzeichen   u8 (5.6)
7      1      Zielart       u8: 1 = NETZ, 2 = DURCHSAGE
8      1      Ziel-Anzahl   u8: n
9      2n     Netz-IDs      u16[n], streng aufsteigend
9+2n   m      Opus          m = Rest, 0..400
```

- NETZ: n = 1..16. DURCHSAGE: n = 0 (an alle) oder 1..64. Global (1) ist als Durchsageziel verboten (Durchsage an alle heißt n = 0).
- ENDE gesetzt: m = 0, PREROLL nicht gesetzt. Sonst m = 1..400.
- Datagrammlänge = 16 + 9 + 2n + m + 16, also 41..569.

### 5.5 Klartext SPRACHE_RUNTER (vom Server gestempelt)

```
Offset Laenge Feld
0      4      Absender      u32 kontoId (vom Server)
4      4      Frame-Nr      u32 (vom Sender uebernommen)
8      2      Sprechphase   u16 (uebernommen)
10     1      Kennzeichen   u8 (BEGINN vom Server je Empfaenger, 6.4;
                              ENDE, PREROLL, VA uebernommen; reserviert = 0)
11     1      Art           u8: 1 = NETZ, 2 = DURCHSAGE, 3 = GLOBAL (vom Server)
12     1      Netz-Anzahl   u8: k (vom Server), 0..16
13     2k     Netz-IDs      u16[k] (vom Server, Reihenfolge 6.4)
13+2k  m      Opus          (uebernommen, unveraendert)
```

Die Netz-IDs sind die Netze, über die dieser Empfänger den Spruch hört: die Schnittmenge aus den erlaubten Zielen und seinen Hörnetzen, bei Global und Durchsage nach 6.4. Absender, Art, Netze und BEGINN bestimmt allein der Server. Der Sender kann nichts davon fälschen. Datagrammlänge 45..477. Ein ENDE kann auch vom Server erzeugt sein (6.4).

### 5.6 Kennzeichen, Sprechphase, Frame-Nr, Pre-roll, Ende

| Bit | Maske | Name | Bedeutung |
| --- | --- | --- | --- |
| 0 | 0x01 | BEGINN | hoch: erstes Paket einer Sprechphase (Hinweis). runter: vom Server gesetzt beim ersten Paket dieser Sprechphase an diesen Empfänger |
| 1 | 0x02 | ENDE | Ende-Markierung, ohne Opus |
| 2 | 0x04 | PREROLL | Frame stammt aus dem Vorlauf-Puffer (Burst) |
| 3 | 0x08 | VA | 1 = Sprachaktivierung, 0 = Sprechtaste (steuert beim Empfänger den Funk-Klick, A11, und die Prüfung "nur Taste", A20) |
| 4-7 | 0xF0 | reserviert | Sender 0, der Server setzt sie beim Weiterleiten auf 0 |

- Frame-Nr: Innerhalb einer Sprechphase steigt sie je codiertem Frame um genau 1, gezählt über Abtastwerte (960 Samples bei 48 kHz = 1). Werden zwei Frames nach einer Verzögerung im Abstand von 1 ms codiert, tragen sie n und n+1. Nur beim Beginn einer Sprechphase wird sie an die Sitzungsuhr angelegt: `max(letzte vergebene + 1, floor((t_aufnahme - t0) / 20 ms))`, mit `t_aufnahme` als Aufnahmezeitpunkt des ersten Frames der Phase (beim Pre-roll des ersten Vorlauf-Frames). So bleibt die Pausenlänge zwischen Sprechphasen erkennbar, und innerhalb einer Phase gibt es weder Schein-Duplikate noch Scheinlücken durch Taktdrift oder verzögerte Threads. Der Jitterpuffer ordnet nach Frame-Nr innerhalb einer Sprechphase.
- Sprechphase: Startwert zufällig je Sitzung, +1 je neuer Sprechphase (mod 2^16). Zielart, Ziele und VA-Kennzeichen sind innerhalb einer Sprechphase konstant (MUSS, Client-Regel C2 in 6.11). Ein neuer Wert heißt beim Empfänger: neue Abspiel-Zeitlinie. Die Laufzeitstatistik je Absender bleibt erhalten.
- Pre-roll (audio-dsp.md 6-7): Bei Sprachaktivierung sendet der Client nach der Bestätigung des Gates die Vorlauf-Frames sofort als Burst, höchstens 7 Pakete (140 ms), jedes mit PREROLL und fortlaufender Frame-Nr, das erste mit BEGINN. Bei der Sprechtaste gibt es keinen Pre-roll, ebenso bei einer Fortsetzung nach einem Zielwechsel (C2, C4). Der Empfänger DARF PREROLL-Pakete NICHT in die Laufzeitstatistik einrechnen und MUSS die Mehrverzögerung binnen 1 s abbauen: leicht beschleunigen oder führende leise Frames kürzen. Das ist Sache des Jitterpuffers, das Protokoll kennzeichnet nur.
- Ende: Nach dem letzten Audio-Frame sendet der Client das ENDE-Paket dreimal im 20-ms-Abstand, mit fortlaufender Frame-Nr und ohne Opus. Der Empfänger wertet das erste ENDE einer Sprechphase aus und übergeht die weiteren. Ohne ENDE gilt die Regel C6 (6.11).
- DTX ist aus (codec-ausgabe.md B17). Solange die Taste gedrückt ist, laufen die Frames weiter, auch bei Stille.
- Der Client ruft `opus_encode` mit `max_data_bytes = 400` auf (MUSS), damit kein Frame an der Grenze des Servers scheitert.

### 5.7 Verschlüsselung

- AEAD_AES_256_GCM über `AesGcm` mit 32-Byte-Schlüssel, 12-Byte-Nonce und 16-Byte-Tag.
- Nonce nach RFC 8446 Abschnitt 5.3: Der 64-Bit-Zähler wird big-endian links mit Nullen auf 12 Byte aufgefüllt und mit dem statischen IV der Richtung und Epoche XOR-verknüpft: `nonce = IV XOR (0x00000000 || Zaehler_be64)`.
- Es gibt getrennte Schlüssel und IVs für hoch und runter (5.8).
- Der Zähler ist je Sitzung und Richtung ein einziger, streng steigender `u64`. Er beginnt bei 0 und läuft über alle Epochen und über UDP und Tunnel weiter. Er wird nie zurückgesetzt und nie wiederverwendet. Jedes Paket jedes Typs (auch ANMELDUNG, KEEPALIVE, PFAD_* und vom Server erzeugte ENDE) verbraucht einen Wert. Vergeben wird er nur in einem Thread: am Server im Relay-Thread, am Client im Sende-Thread (5.14). Wer davon abweicht, vergibt ihn atomar (`Interlocked.Increment`).
- Die Eindeutigkeit von (Schlüssel, Nonce) ist doppelt gesichert: Die Schlüssel sind je Epoche zufällig, und der Zähler wiederholt sich nie.
- Eine `AesGcm`-Instanz gibt es je Schlüssel. Sie wird nur von dem einen Thread benutzt, dem sie gehört (5.14), und beim Verwerfen ihrer Epoche mit `Dispose` freigegeben.

### 5.8 Schlüssel, Epochen, Rotation

- Schlüsselmaterial je Epoche: `K_hoch` (32), `IV_hoch` (12), `K_runter` (32), `IV_runter` (12). Alles kommt aus `RandomNumberGenerator.Fill` im Server. Es wird nicht abgeleitet, nie wiederverwendet und nur über den Steuerkanal übertragen, binär in `VoiceKeys` (0x8002). Eigene Puffer werden nach Gebrauch mit `CryptographicOperations.ZeroMemory` gelöscht, Schlüssel nie geloggt. Grenzen der Löschbarkeit in 4.5 (0x8002).
- Sitzungsbeginn: VoiceKeys(START, Epoche 0). Jede Wiederverbindung ist eine neue Sitzung mit neuen Schlüsseln. Es gibt keine Wiederaufnahme und keinen gespeicherten Zähler (server-sicherheit.md B7).
- Rotation, Auslöser beim Server: 60 min seit Epochenbeginn, oder 2^20 Pakete in einer Richtung in dieser Epoche (der Server zählt gesendete Pakete runter und empfangene hoch). Der Client fordert per `KeyRotation{ZAEHLER}` an, wenn er 2^21 Pakete hoch in der Epoche gesendet hat. Harte Grenze: 2^22 Pakete je Richtung und Epoche. Darüber hinaus sendet der Sender in dieser Epoche nichts mehr (Sprache wird verworfen). Hält das länger als 5 s an, beginnt der Client eine neue Sitzung. Die Vertraulichkeitsgrenze von AES-GCM (2^23 Pakete, RFC 9001 Abschnitt 6.6) wird so nie erreicht.
- Rotation, Ablauf (make before break), mit e als aktueller Epoche:
  1. Der Server erzeugt das Material für e+1 und sendet `VoiceKeys{epoche: e+1, grund: ROTATION}`. Ab sofort nimmt er hoch e und e+1 an.
  2. Der Client installiert e+1, nimmt runter e und e+1 an, sendet hoch ab sofort in e+1 und antwortet mit e+1.
  3. Mit der Antwort sendet der Server runter in e+1. 10 s später verwirft er e (beide Richtungen, `Dispose`).
  4. Der Client verwirft e 10 s nach dem ersten runter-Paket in e+1, spätestens 20 s nach seiner Antwort.
  5. Es läuft höchstens eine Rotation zugleich. Kommt binnen 10 s keine Antwort, trennt der Server (Client verbindet neu).
- Epochen werden mod 256 verglichen. Angenommen werden nur die aktuelle, die angekündigte nächste und für 10 s die vorige.
- Scheitern je Sitzung und Epoche mehr als 2^16 Entschlüsselungen, erzwingt der Server eine Rotation und protokolliert einen Angriffsverdacht.

### 5.9 Empfangsprüfung (Reihenfolge verbindlich)

Server, für jedes Datagramm (UDP) bzw. jeden SpracheTunnel-Rahmen:

0. Empfangsalter: Die Empfangsschleife stempelt jedes Datagramm direkt nach dem Empfang mit der monotonen Uhr. Der Relay-Thread verwirft Sprache, die länger als 60 ms zwischen Empfang und Verarbeitung lag. Kernel-Zeitstempel wären genauer, sind aber nur per P/Invoke erreichbar (Offener Punkt).
1. Länge: Der Empfangspuffer ist 2048 Byte groß. Wird der Puffer voll oder das Datagramm abgeschnitten (`SocketFlags.Truncated`, Lehre aus Mumble PR #7236), oder ist es kleiner als 41 bzw. größer als 1200 Byte, wird es verworfen.
2. Kopf: Version 1, Typ bekannt und in Richtung hoch, reserviert = 0, Länge passt zum Typ (0x01, 0x03, 0x04 genau 48 Byte; 0x10 41-569 Byte), sonst verwerfen.
3. Sitzung zur Sitzungs-ID vorhanden und in AKTIV, sonst verwerfen. Das kostet eine Tabellensuche, sonst nichts. Tunnel: Die Sitzungs-ID MUSS die der tragenden TLS-Verbindung sein, sonst `PROTOKOLL` und Trennung.
4. Herkunft und Budget:
   - Tunnel: Tunnelbudget der Sitzung, 200 Versuche/s. Die Schritte 9 und der Vorfilter entfallen.
   - Quelladresse gleich der gebundenen Adresse dieser Sitzung: Budget "gebunden", 200 Versuche/s. Der Vorfilter je Präfix gilt hier nicht, damit gefälschter Müll von der IP eines Mitglieds dessen echte Pakete nicht verdrängt.
   - Jede andere Quelle: zuerst der Vorfilter je Präfix (400 Datagramme/s, Burst 200), dann das Fremdbudget je (Sitzung, Quelladresse) mit 60 Versuchen/s, Burst 30 (eine volle Sprachspur). Je Sitzung werden höchstens 16 Quelladressen verfolgt (die älteste fällt heraus), zusammen höchstens 200 Versuche/s.
   - Über dem Budget wird ohne weitere Arbeit verworfen.
5. Epoche angenommen (5.8), sonst verwerfen.
6. Replay-Vorprüfung, nur lesend: Zähler > Höchstwert, oder `Höchstwert - Zähler < W` und das Bit ist nicht gesetzt. Sonst verwerfen, mit einer Ausnahme: die Kopierprüfung (5.10).
7. AES-256-GCM entschlüsseln (Schlüssel und IV der Epoche, Richtung hoch, AAD = Kopf). Bei falschem Tag verwerfen, zählen und das Fenster nicht verändern.
8. Replay-Fenster: atomares Prüfen und Setzen, also erst nach der Tag-Prüfung (WireGuard, server-sicherheit.md B7). Maßgeblich ist nur dieser Schritt: Ist das Bit schon gesetzt, wird verworfen. Schritt 6 ist nur ein Vorfilter. Bei Verarbeitung in mehr als einem Thread geschieht Schritt 8 unter der Sperre der Sitzung oder per CAS auf dem Fensterwort.
9. Adressbehandlung (5.10), nur UDP.
10. Nach Typ verarbeiten: Sprache nach 6.3, KEEPALIVE nach 5.11, PFAD_ANTWORT und ANMELDUNG nach 5.10.

Das Replay-Fenster umfasst W = 2048 Pakete, als Bitmap je Sitzung und Richtung, gemeinsam für alle Epochen und für UDP und Tunnel. Das Verfahren nach RFC 6479 oder ein einfacher 2048-Bit-Ring sind beide zulässig. Das Verhalten legt Vektor 8.7 fest.

Client, für runter-Pakete aus UDP und Tunnel: gleiche Reihenfolge ohne Schritt 4 und 9, Mindestlänge 45 Byte für Sprache, genau 48 Byte für 0x02 und 0x05. Der verbundene Socket filtert die Quelle. Unter Windows MUSS `SIO_UDP_CONNRESET` ausgeschaltet sein, sonst bricht ein ICMP "port unreachable" den Empfang ab.

Auf ein verworfenes Datagramm antwortet der Server nie, weder mit Fehler noch mit Echo.

### 5.10 Adressbindung, Pfadprüfung, NAT-Rebinding

Je Sitzung gibt es höchstens eine gebundene Adresse B und höchstens 2 offene Kandidaten.

- Ein gültiges Paket von B wird normal verarbeitet.
- Ein gültiges Paket (Schritte 1-8 bestanden) von einer anderen Adresse A: Die Sprache darin wird weitergeleitet, sie ist authentisch. Runter-Sprache geht aber weiter nur an B. Eine Prüfung löst nur ein Paket aus, das einen neuen Höchstwert des Zählers bringt (nach RFC 9000 Abschnitt 9.3). Umsortierte ältere Pakete von einer früheren Adresse lösen nichts aus.
- Neuer Kandidat A: Der Server legt eine Challenge (16 Zufallsbytes) für A an, 3 s gültig, und sendet `PFAD_PRUEFUNG` an A. Ohne Antwort wiederholt er nach 250 ms, höchstens 4 je Sekunde und Kandidat, und nie mehr als eine PFAD_PRUEFUNG je auslösendem gültigem Paket von A. Das Paket ist 48 Byte groß, nie größer als das auslösende. Sind schon 2 Kandidaten offen, wird ein weiterer bis zum Ablauf eines offenen übergangen; neue Kandidaten verdrängen ältere nicht.
- Gegenprobe: Zugleich sendet der Server eine eigene PFAD_PRUEFUNG an B (höchstens eine je Kandidat). B ist eine bereits geprüfte Adresse.
- Der Client beantwortet jede gültige PFAD_PRUEFUNG sofort mit `PFAD_ANTWORT`, die Challenge im Klartext, über seinen verbundenen Socket.
- Eine PFAD_ANTWORT gilt nur, wenn sie von genau der Adresse kommt, an die ihre Challenge ging (Vergleich per `FixedTimeEquals`). Eine neue Challenge an dieselbe Adresse ersetzt die alte.
- Kopierprüfung: Solange ein Kandidat offen ist, merkt sich der Server die Zählerwerte, die er von Kandidaten angenommen hat (höchstens 256). Kommt von B ein Datagramm, das in Schritt 6 als Duplikat eines solchen Werts gilt, wird es trotzdem entschlüsselt (Budget von B). Besteht der Tag, hat jemand echte Pakete kopiert und schneller zugestellt: Der Kandidat wird verworfen, A 60 s lang für diese Sitzung gesperrt, und der Server meldet `KOPIERVERDACHT`.
- Umgebunden wird auf A erst, wenn (1) eine gültige PFAD_ANTWORT von A vorliegt, (2) von B seit mindestens 500 ms nichts Authentisches mehr kam (weder neue Pakete noch authentische Duplikate noch die Antwort auf die Gegenprobe) und (3) kein Kopierverdacht besteht. Antwortet B auf die Gegenprobe, lebt B: Der Kandidat wird verworfen. Mit der Umbindung sendet der Server `UdpStatus{GEBUNDEN, adresse, gleicheIp}`; ab jetzt geht runter-Sprache an A.
- Rückkehr: Eine in den letzten 60 s gebundene Adresse gilt als geprüft. Ein nicht prüfendes Paket von ihr mit neuem Höchstwert bindet ohne neue Challenge sofort zurück (RFC 9000 Abschnitt 9.3.3).
- Erstbindung: Nach der Antwort auf VoiceKeys sendet der Client `ANMELDUNG` alle 250 ms, höchstens 12-mal, bis eine PFAD_PRUEFUNG kommt. Es gibt noch kein B, die Frist aus (2) entfällt. Nach 3 s ohne `UdpStatus{GEBUNDEN}` schaltet er den Tunnel ein (5.12).
- NAT-Rebinding: Ändert das NAT den Port, kommen die Pakete von A, und B verstummt. Nach 500 ms Frist und einer Umlaufzeit ist A gebunden. Die Sprache hoch geht ohne Lücke weiter, sofern das Fremdbudget reicht (60/s, also eine volle Spur). Runter fehlen beim Sprechenden höchstens etwa 500 ms plus 1 RTT, beim stummen Hörer (nur KEEPALIVE) etwa 1 RTT.
- Sicherheit: Ein blinder Angreifer ohne Sicht auf die Pakete kann weder umleiten noch eine Prüfung auslösen, denn dazu braucht er ein authentisches Paket. Ein Beobachter mit schnellerem Weg (T7 im selben WLAN, T8) kann echte Pakete kopieren und von A zustellen. Gegenprobe, Kopierprüfung und Rückkehr bei neuem Höchstwert machen ein Umlenken unwahrscheinlich. Sicher verhindern lässt es sich nicht, wenn er zugleich die Originale unterdrückt; dann kann er den Pfad aber ohnehin stören. Mitlesen oder einsprechen kann er nie, denn runter ist mit dem Schlüssel des Empfängers versiegelt. Die Verstärkung bleibt bei höchstens Faktor 1. Das folgt dem Vorbild von RFC 9000 Abschnitt 9.3 und ist strenger als WireGuard.

### 5.11 Keepalive

- Der Client sendet in AKTIV/UDP alle 2 s ein `KEEPALIVE`, unabhängig davon, ob er spricht, dazu sofort eines bei `NetworkChange.NetworkAddressChanged`. Das hält die NAT-Zuordnung (RFC 4787) und dient als Messprobe für RTT und Pfadüberwachung.
- Das weicht bewusst von RFC 8085 Abschnitt 3.5 ab (Keepalives nicht öfter als alle 15 s). Dort geht es um das Offenhalten von NAT-Zuständen sonst ruhender Flüsse; hier geht es um die Überwachung einer aktiven Echtzeitsitzung, die beim Sprechen ohnehin 50 Pakete/s sendet. Die Last liegt bei 40 Sitzungen unter 3 KB/s.
- Der Server antwortet mit `KEEPALIVE_ECHO` (48 Byte, gleiche Größe), aber nur an die gebundene Adresse. Von einer fremden Adresse löst ein Keepalive stattdessen die Pfadprüfung aus (5.10).
- Aus dem Echo misst der Client die Umlaufzeit (Anzeige, Jitterpuffer-Startwert, EmpfangsBericht).

### 5.12 Ausfallerkennung und Tunnelmodus über den Steuerkanal

- Verdacht auf einen UDP-Ausfall hat der Client, wenn eines davon eintritt:
  - kein KEEPALIVE_ECHO seit 3 s (also ein Echo mehr als 1 s überfällig),
  - runter-Ausfall: `udpGesendet` ist zwischen der vorletzten und der letzten Ping-Antwort um mindestens 5 gestiegen, der Client hat im selben Zeitraum per UDP kein runter-Datagramm empfangen,
  - hoch-Ausfall: `udpEmpfangen` ist zwischen der vorletzten und der letzten Ping-Antwort nicht gestiegen, obwohl der Client dazwischen mindestens 3 Datagramme per UDP gesendet hat,
  - eine Adressänderung am Rechner (`NetworkAddressChanged`).
- Bei Verdacht sendet der Client sofort 3 KEEPALIVE im Abstand von 100 ms. Das stößt Pfadprüfung und Rebinding an. Kommt binnen 1 s weder ein Echo noch `UdpStatus{GEBUNDEN}`, schaltet er per `TunnelModus{an: true, grund: UDP_AUSFALL}` den Tunnel ein und zeigt "Empfang gestört, Notbetrieb über TCP". Ziel: Ausfall in jeder Richtung binnen 3 s erkannt, Tunnel binnen 4 s aktiv, auch beim reinen Hörer.
- Weitere Gründe zum Einschalten: KEINE_BINDUNG (3 s nach VoiceKeys noch nicht gebunden) und NUTZER. Einschalten tut nur der Client.
- Im Tunnel gehen Datagramme hoch und runter als `SpracheTunnel` (0x8001), mit identischem Format, gleichen Schlüsseln, gleichem Zähler und gleichem Replay-Fenster. Der Server sendet runter nur noch im Tunnel. Hoch nimmt er beide Wege an.
- UDP-Probe: Alle 30 s sendet der Client ANMELDUNG und danach KEEPALIVE. Zurückgeschaltet wird mit `TunnelModus{an: false, grund: UDP_ZURUECK}` erst, wenn die Bindung steht und die Probe 10 s lang Echos ohne Verlust liefert; die Sprache läuft so lange weiter im Tunnel. Nach zwei Rückfällen binnen 10 min verdoppelt der Client den Probenabstand, bis höchstens 5 min.
- Staut der Tunnel: je Absender höchstens 3 Rahmen in der Warteschlange, Verwerfen ab 60 ms Alter (4.2), Steuerrahmen gehen vor. `NoDelay`, `TCP_NOTSENT_LOWAT` und kleine Sendepuffer (4.1) verhindern, dass sich Sekunden veralteter Sprache im Kernel stauen. Der Client deckelt im Tunnel die Zielverzögerung seines Jitterpuffers. Head-of-line-Blocking wird als Notbetrieb hingenommen.
- Für den Tunnel gelten dieselben Sprach-Grenzen wie für UDP.

### 5.13 Prüfung der Opus-Nutzlast

- Der Client MUSS jedes empfangene Opus-Paket mit `opus_packet_get_nb_samples(..., 48000) == 960` prüfen und andernfalls verwerfen. Schädliche Nutzlast eines veränderten Senders erreicht sonst libopus bei allen Hörern (Stichwort CVE-2025-71264). libopus bleibt gepinnt und gepflegt.
- Der Server MUSS vor der Weiterleitung m <= 400 und die Dauer ohne libopus aus dem TOC-Byte prüfen (RFC 6716 Abschnitt 3.1) und alles verwerfen, was nicht genau 20 ms mono ergibt. `config = toc >> 3`, `s = (toc >> 2) & 1`, `c = toc & 3`.
  - Framedauer: config 0-11 (SILK) je `config mod 4` 10, 20, 40, 60 ms; config 12-15 (Hybrid) je `config mod 2` 10, 20 ms; config 16-31 (CELT) je `config mod 4` 2,5, 5, 10, 20 ms.
  - Anzahl: c = 0 ergibt 1, c = 1 oder 2 ergibt 2, c = 3 ergibt `byte1 & 0x3F` (verlangt m >= 2).
  - Bedingung: Dauer x Anzahl = 20 ms und s = 0.

### 5.14 Sockets und Threads

- Server:
  - Je konfigurierter Adresse ein UDP-Socket, `ReceiveBufferSize` 512 KiB bis 1 MiB (ein größerer Puffer verbirgt bei einem Stau Sekunden an Rückstand). Die Empfangsschleife nutzt die allokationsfreien `SocketAddress`-Überladungen, stempelt die Empfangszeit und reicht Puffer aus einem Pool über einen `Channel` mit fester Kapazität an den Relay-Thread. Ist der Channel voll, wird verworfen.
  - Die TLS-Leser der Sitzungen reichen SpracheTunnel-Rahmen ebenso an den Relay-Thread.
  - Genau ein Relay-Thread verarbeitet alle Sprachpakete aus UDP und Tunnel: Empfangsprüfung, Replay-Fenster, Budgets, Phasenbuch, Rechteprüfung, Stempel, Versiegeln, Zähler, Pfadprüfung, Keepalive-Echo. Ihm gehören alle `AesGcm`-Instanzen des Sprachpfads. Andere Threads ändern diesen Zustand nur über Befehle in seiner Warteschlange (Schlüssel installieren, Rotation, Sitzung beenden, Tunnel an oder aus).
  - Ein Sende-Thread übernimmt die fertig versiegelten Pakete vom Pacer (7.4) und macht die Systemaufrufe. Tunnel-Rahmen gehen an die Schreibschleife der jeweiligen Sitzung.
  - Kapazität: Das Heimbudget begrenzt auf etwa 18.000 Sendungen/s. Szenario D (78.000 Pakete/s) sollte ein Kern des 5950X schaffen, das ist aber erst nach P-31 belegt. Wird doch parallelisiert, gilt: eine `AesGcm`-Instanz je Thread und Schritt 8 unter der Sperre der Sitzung.
  - Der Pfad vor dem Entschlüsseln allokiert nichts.
- Client:
  - `Socket(AddressFamily der TLS-Verbindung, Dgram, Udp)`, `Bind` auf Port 0, `Connect(Server-IP, UDP-Port)`.
  - Ein Sende-Thread für alle hoch-Datagramme (UDP und Tunnel) besitzt die hoch-Instanz von `AesGcm` und den hoch-Zähler. Ein Empfangs-Thread für alle runter-Datagramme aus UDP und Tunnel besitzt die runter-Instanz und das Fenster.
  - Die Audio-Threads blockieren nie auf dem Socket; sie übergeben Frames über eine Warteschlange ohne Sperren.

## 6. Weiterleitung und Rechte im Server

### 6.1 Rechte und Stufen

| Recht | Bedeutung | FUNKER | RAIDLEITER (Führung) | Auftrag Einsatzleitung (6.12) | ADMIN |
| --- | --- | --- | --- | --- | --- |
| GLOBAL_SENDEN | in Global senden (Führung, A23) | - | x | x (abwählbar) | x |
| DURCHSAGE | Durchsage an ausgewählte Netze (A8) | - | x | x (abwählbar) | x |
| DURCHSAGE_ALLE | Durchsage an alle | - | x | x (abwählbar) | x |
| MEHRFACH_SENDEN | NETZ-Senden in mehr als ein Netz zugleich (A7) | - | x | x (abwählbar) | x |
| ZUWEISEN | andere verschieben, Aufstellungen, Stummschalten, Selbstwahl sperren, Plätze freigeben, Raid-Helper, Kontenverzeichnis (A5, A6, A13), mit Rangregel (6.10) | - | - (bis 0.2.0 x) | x | x |
| SPRECHANZEIGE_ALLE | Sprechanzeige aller Netze | - | x | x | x |
| GERAETE_FREIGEBEN | Geräte freigeben, ablehnen, dauerhaft sperren | - | - | nie | x |
| KONTEN_VERWALTEN | Konten sperren, Stufe, Rechte, Name, löschen, Aufträge erteilen und entziehen (6.10, 6.12) | - | - | nie | x |
| NETZE_VERWALTEN | Netze, Schiffe, Rollen und Einstellungen anlegen und ändern | - | - | nie | x |

Rechte gehören zum Konto: Standard aus der Stufe, einzeln änderbar als sichtbare Abweichung (6.10). Wirksam sind sie mit den Rechten eines laufenden Auftrags (6.12); der Server rechnet das je Aktion und je Sprachpaket zur jeweiligen Zeit aus. Zusätzlich ergibt sich das Senderecht je Netz aus der Mitgliedschaft.

Die drei Verwaltungsrechte `GERAETE_FREIGEBEN`, `KONTEN_VERWALTEN` und `NETZE_VERWALTEN` wirken nur in einer Admin-Sitzung (3.9); außerhalb hat ein ADMIN-Konto die Rechte der Führung und `ZUWEISEN`. Als Einzelrecht vergibt sie nur die Konsole. Seit 0.3.0 (Stufe 2, A37) hat RAIDLEITER kein `ZUWEISEN` mehr: Die Stufe heißt in der Oberfläche "Führung" und darf Global und Durchsagen, eingeteilt wird mit einem Auftrag für einen Abend (6.12) oder, als sichtbare Ausnahme, mit dem Einzelrecht `ZUWEISEN` (etwa für einen festen Stellvertreter). Ein Konto, das unter 0.2.0 RAIDLEITER war, verliert `ZUWEISEN` mit dem Update; seine Abweichungen (`plus`, `minus`) bleiben.

Seit 0.5.0 (6.15): Solange ein ADMIN "Heute als Funker" spielt, fehlen ihm die Rechte der Einsatzleitung (die Spalte Auftrag, also Führung und `ZUWEISEN`), je Aktion und je Sprachpaket; die drei Verwaltungsrechte bleiben und wirken wie immer nur in der Admin-Sitzung. Die Tabelle selbst ändert sich nicht.

### 6.2 Serverzustand als Schnappschuss, Schreiber, Versionen

- Der ganze entscheidungsrelevante Zustand liegt in einem unveränderlichen Schnappschuss-Objekt: Konten mit Status, Rechten und Stufe, Netze, Schiffe, Rollen, Einstellungen, Mitgliedschaften je Konto (Feld -> Netz, hören, senden, fest), Plätze, Stummschaltungen, aktive Sitzungen, dazu der Index Netz -> hörende Sitzungen, die `zustandsVersion` und je Konto die `kontoVersion`. Die Sammlungen sind unveränderlich mit strukturellem Teilen (etwa `ImmutableDictionary`), damit nicht jede Änderung eine Vollkopie baut.
- Zwei Klassen von Änderungen:
  - Weiterleitungsrelevant (Mitgliedschaften, Hören, Plätze, Rechte, Aufträge, Stummschaltungen, Netze, Konto- und Gerätestatus): über den Zustandsschreiber, synchron gespeichert, mit Austausch des Schnappschusses. Ein Auftrag liegt beim Konto im Lagezustand (`lage.json`), nicht im Kontenbuch; der Schnappschuss hält je Sitzung seine Rechte mit `von` und `bis`, so wirkt Beginn und Ablauf je Paket, ohne dass ein neuer Schnappschuss nötig ist.
  - Komfortzustand (Sendeauswahl, Pegel, Ablage): zusammengefasst und asynchron gespeichert, ohne Austausch des Schnappschusses.
- Genau ein Zustandsschreiber führt die weiterleitungsrelevanten Änderungen nacheinander aus (ein Thread oder Channel). Aufträge mit `ZUWEISEN` und Verwaltung gehen in der Warteschlange vor Selbständerungen. Alle innerhalb von 5 ms angefallenen Änderungen schreibt er in einer Transaktion (Gruppen-Commit; bei SQLite WAL mit `synchronous=NORMAL`: nach einem Stromausfall kann die letzte Sekunde fehlen, der Stand bleibt aber in sich stimmig). Er baut den neuen Schnappschuss, schreibt ihn dauerhaft, tauscht ihn per `Interlocked.Exchange` aus und meldet erst danach an die Clients.
- `zustandsVersion` steigt mit jedem Austausch. `kontoVersion` eines Kontos ist die `zustandsVersion` seiner letzten weiterleitungsrelevanten Änderung. `basisVersion` prüft nur die betroffenen Konten: `VERALTET`, wenn eines davon eine `kontoVersion` größer als `basisVersion` hat. So scheitert ein Verschieben nicht daran, dass ein anderer Funker gerade sein Hören umschaltet.
- Die Weiterleitung liest die Schnappschuss-Referenz genau einmal je Paket und arbeitet ohne Sperren. Jedes Paket sieht also entweder den alten oder den neuen Stand vollständig.

### 6.3 Senderecht je Paket

Pseudocode, nach erfolgreicher Empfangsprüfung (5.9):

```
z = Schnappschuss (einmal lesen); k = z.konto(sitzung.konto)
Form pruefen: Zielart in {1,2}; n und Sortierung nach 5.4; 1 kein Durchsageziel;
   ENDE/Opus-Regeln; m <= 400; TOC (5.13)
   verletzt -> ganz verwerfen(UNGUELTIG)
Ratenlimit der Sitzung (60 Pakete/s Burst 20; Opus 16000 B/s Burst 4000)
   sonst ganz verwerfen(GRENZE), Meldung{RATE} an ZUWEISEN
erlaubt = []; gestrichen = []
wenn Zielart == NETZ:
   wenn n > 1 und nicht k.recht(MEHRFACH_SENDEN): ganz verwerfen(MEHRFACH_NICHT_ERLAUBT)
   fuer jedes netz in ziele:
      wenn netz unbekannt:                        streichen(netz, UNBEKANNTES_NETZ)
      sonst wenn netz == 1 und nicht k.recht(GLOBAL_SENDEN): streichen(netz, KEIN_RECHT_GLOBAL)
      sonst wenn netz != 1 und nicht (k.mitgliedschaft(netz) und .senden):
                                                  streichen(netz, ZIEL_ENTZOGEN falls in den
                                                  letzten 10 s durch Umstellung, sonst KEIN_SENDERECHT)
      sonst wenn z.gesperrt(k, netz):             streichen(netz, KEIN_SENDERECHT, durch = Setzender)
      sonst wenn z.stumm(k, netz):                streichen(netz, STUMMGESCHALTET)
      sonst wenn z.netz(netz).nurTaste und VA:    streichen(netz, NUR_TASTE)
      sonst erlaubt += netz
wenn Zielart == DURCHSAGE:
   brauche k.recht(DURCHSAGE); bei n == 0 zusaetzlich k.recht(DURCHSAGE_ALLE)
      sonst ganz verwerfen(KEIN_RECHT_DURCHSAGE)
   wenn z.durchsageNurTaste und VA: ganz verwerfen(NUR_TASTE)
   wenn z.stumm(k, alle): ganz verwerfen(STUMMGESCHALTET)
   wenn n == 0: erlaubt = alle
   sonst fuer jedes netz in ziele:
      wenn netz unbekannt: streichen(netz, UNBEKANNTES_NETZ)
      sonst wenn z.gesperrt(k, netz): streichen(netz, KEIN_SENDERECHT, durch = Setzender)
      sonst wenn z.stumm(k, netz): streichen(netz, STUMMGESCHALTET)
      sonst erlaubt += netz
wenn erlaubt leer: ganz verwerfen (Code des ersten gestrichenen Ziels)
gestrichen nicht leer -> SendeFehler{fehler: gestrichen, teilweise: true}
-> Empfaengerbestimmung (6.4) mit erlaubt
```

- `k.recht(...)` sind die wirksamen Rechte zur Zeit des Pakets: Stufe, Abweichungen und ein Auftrag, solange `von <= jetzt < bis` (6.12). Läuft ein Auftrag mitten im Global-Spruch ab, wird das nächste Paket nach Global gestrichen.
- `z.stumm(k, netz)` ist wahr bei einer Stummschaltung für dieses Netz, für Global (bei netz == 1) oder für alle.
- Ganz verworfen wird nur bei Formfehlern, Ratenverstoß, Mehrfachzielen ohne `MEHRFACH_SENDEN`, fehlendem Durchsagerecht, Durchsage per VA bei `durchsageNurTaste`, Stummschaltung für alle, oder wenn kein Ziel übrig bleibt. Sonst geht das Paket an die erlaubte Teilmenge. Das ist so fälschungssicher wie vorher, weil der Server ohnehin je Empfänger stempelt. So bleibt der Raidleiter in Kommando hörbar, auch wenn ihm eine Umstellung gerade Crew 3 entzogen hat.
- Jeder Verwurf und jede Streichung wird gezählt und über `SendeFehler` gemeldet (4.5).
- Eine Durchsage an alle (n = 0) hält nur eine Stummschaltung für alle auf. Netz-Stummschaltungen wirken bei einer Durchsage an ausgewählte Netze je Zielnetz.
- "Nur Taste" (A20) setzt der Server am Kennzeichen VA durch. Ein absichtlich lügender Client kann das umgehen. Das ist bekannt und hingenommen, denn der Schutz richtet sich gegen Fehlbedienung, nicht gegen Angriffe.
- Kanäle (seit 0.6.0, 6.16): Ein Ziel darf eine Kanal-ID sein. "Netz unbekannt" heißt dann: kein gültiger Kanal eines bekannten Kanalnetzes (`UNBEKANNTES_NETZ`); ein gültiger Kanal ohne eigene Mitgliedschaft mit `senden` ergibt `KEIN_SENDERECHT` bzw. `ZIEL_ENTZOGEN`, die Ruhe (Ch. 0) nie etwas anderes. `nurTaste` und `z.stumm` gelten auch über das Kanalnetz: eine Stummschaltung des Kanalnetzes trifft alle seine Ch. Eine Durchsage an einen gültigen Kanal, den niemand hält, erreicht niemanden und wird nicht gestrichen; die Netz-ID eines Kanalnetzes als Durchsageziel erreicht ebenfalls niemanden (kein "alle Ch.").
- Kanalsperren (seit 0.6.0, 6.17): `z.gesperrt(k, netz)` ist wahr, wenn eine laufende Sperre des Kontos oder eine Sperre für alle (nur ohne wirksames `ZUWEISEN`, ein ADMIN-Konto nur bei einer Sperre von einem ADMIN) den Kanal, sein Kanalnetz oder das Netz trifft. Zeit und Ausnahme der Einsatzleitung gelten je Paket, ohne neuen Schnappschuss. Global, Orgaweit und die Durchsage an alle trifft keine Sperre.
- Warum die Ziele im Paket stehen und nicht als vorher gemeldetes Sendeziel im Server: So gibt es kein Rennen zwischen Numpad-Tastendruck und Steuerkanal, und keinen Ton im falschen Netz nach einem Umschalten (die Lehre aus dem VoiceTarget-Befund bei Mumble). Das Senderecht prüft der Server trotzdem je Paket gegen den aktuellen Schnappschuss.

### 6.4 Empfängerbestimmung, Stempel, Sprechphasen je Empfänger

Der Relay-Thread führt ein Phasenbuch: je Sender die laufende Sprechphase p mit Nummer, Zielart, Zielen, VA-Kennzeichen, der Menge `p.bedient` der Empfänger, die schon ein Paket dieser Phase bekommen haben, und je Empfänger den zuletzt gestempelten Netzen `p.netze[r]`. Eine Phase ist offen bis ENDE oder 2 s ohne Paket. Weicht ein Paket mit gleicher Nummer in Zielart, Zielen oder VA von p ab (fehlerhafter Client), behandelt der Server es als neue Phase.

```
p = phasenbuch.hole_oder_neu(Sender, Sprechphase, Zielart, Ziele, VA)
wenn p neu und die vorige Phase q dieses Senders noch Empfaenger hat:
   fuer jedes r in q.bedient: erzeugtes ENDE an r; bei Art != NETZ DurchsageEnde an r
fuer jede Sitzung r in z.aktiv, r != Sender, mit gebundener Adresse oder Tunnel,
      ohne Strom-Abwurf fuer (Sender, Phase) bei r (7.4):
   wenn Zielart == DURCHSAGE:
      n == 0: treffer = [], empfangen = ja
      sonst:  treffer = erlaubt geschnitten mit r.netze (hoeren egal), ohne fuer r gesperrte; empfangen = treffer nicht leer
      art = DURCHSAGE
   wenn Zielart == NETZ:
      treffer = { netz in erlaubt | netz == 1 oder (r.mitgliedschaft(netz).hoeren == true und nicht z.gesperrt(r, netz)) }
      empfangen = treffer nicht leer
      art = GLOBAL wenn 1 in treffer, sonst NETZ
   wenn nicht empfangen:
      wenn r in p.bedient: erzeugtes ENDE an r (Netze = p.netze[r]), p.bedient -= r,
         bei Art != NETZ DurchsageEnde an r
      weiter mit naechstem r
   wenn Paket ein ENDE ist und r nicht in p.bedient: weiter mit naechstem r
   Reihenfolge der treffer: zuerst 1 (Global), danach nach r's Feldnummer aufsteigend
   kz = Kennzeichen & 0x0E                (ENDE, PREROLL, VA uebernehmen)
   wenn r nicht in p.bedient: kz |= BEGINN; p.bedient += r; bei art != NETZ DurchsageStart an r
   p.netze[r] = treffer
   Stempel: Absender = k.kontoId, Art, Netz-IDs = treffer, Kennzeichen = kz;
            Frame-Nr, Sprechphase, Opus uebernommen
   versiegeln mit r's Schluessel runter, r's Zaehler, r's Epoche; an Pacer bzw. Tunnel
wenn Paket ein ENDE ist: p beendet (weitere ENDE-Kopien gehen nur an p.bedient)
nach 300 ms ohne Paket von p: bei Art != NETZ DurchsageEnde an p.bedient
```

- Ein erzeugtes ENDE ist ein SPRACHE_RUNTER mit Absender, Art und Sprechphase der Phase, Frame-Nr = zuletzt an r gestempelte + 1, Kennzeichen ENDE, Netzen `p.netze[r]` und ohne Opus. Der Server sendet es einmal; geht es verloren, greift die Regel C6 beim Empfänger.
- So bekommt ein Hörer, der mitten in eine Sprechphase hineinkommt (Verschieben, HoerenSetzen, Aufstellung), sein BEGINN (Klick nach A11) und bei einer Durchsage sein Banner. Wer herausfällt oder wenn der Sender das Recht verliert, bekommt ein ENDE statt 300 ms Nachblubbern.
- Je Empfänger und Paket gibt es genau eine Kopie, auch wenn er mehrere Zielnetze hört.
- Der Absender bekommt seinen eigenen Spruch nie zurück.
- Wie der Empfänger mehrere Treffer-Netze abspielt, regelt C7 (6.11). Der Pegel je Funker (A4) hängt am gestempelten `Absender`.
- Das Rennen zwischen Weiterleitung nach neuem Stand und dem Mitgliedschaften-Push regelt C8 (6.11).

### 6.5 Global

- Es gibt genau ein Netz vom Typ GLOBAL mit ID 1. Es steht in keinem Feld, alle aktiven Sitzungen hören es, und `HoerenSetzen(1, false)` ergibt `GLOBAL_PFLICHT` (A23).
- Senden darf nur, wer `GLOBAL_SENDEN` hat (Führung). Die Erstkonfiguration legt Global mit `nurTaste = true` und `ausloeserStandard = "TASTE"` an (A20). Beim Empfänger trägt das Paket die Art GLOBAL. Der Client zeigt es auf dem Sonderfeld in der Leitfarbe (dunkles Petrol, bis 0.12.0 weinrot) und senkt NETZ-Ströme ab wie bei einer Durchsage.
- Den Mindestpegel für Global und Durchsage setzt der Client durch (C7); den Wert legt ein Offener Punkt fest. Der Server kennt keine Lautstärken.
- Seit 0.6.0 kann Global ein Kanalnetz sein (`kanaele` an Netz 1, etwa "Ruhe", "Orgaweit", "Global 1" bis "Global 20"). Das feste Global-Feld bleibt immer auf "Orgaweit": Ch. 1 von Global ist Netz 1 selbst, mit allen Regeln dieses Abschnitts; seine berechnete Kanal-ID 0x8021 gilt nirgends (`UNGUELTIG` in Feldern, `UNBEKANNTES_NETZ` als Sprachziel). Die übrigen Ch. sind gewöhnliche Kanäle, die man wie andere in ein Feld legt; sie hört nur, wer sie hat. `nurTaste` von Global gilt seit Schritt 3.3 nur für Orgaweit: Global 1 bis 20 erlauben Sprachaktivierung (Annahme zur alten Kanalvorlage, 0.6.0 bis 0.12.0), ab Werk steht der Client dort wie bei Orgaweit auf Taste (`ausloeserStandard` von Global).

### 6.6 Durchsage und Absenkung

- Eine Durchsage erreicht alle Mitglieder der erlaubten Zielnetze, auch bei `hoeren = false`, bzw. bei n = 0 alle aktiven Sitzungen (6.4). Ein Aus-Schalter schützt also nicht vor Durchsagen, das ist gewollt.
- Standardmäßig nur per Taste (`durchsageNurTaste = true`), damit Räuspern und Gate-Fehlauslösungen nicht bei allen 40 die Netze absenken.
- Der Server mischt nicht. Die Absenkung besteht bei ihm darin, dass er die Art DURCHSAGE oder GLOBAL stempelt und je Empfänger `DurchsageStart` und `DurchsageEnde` meldet. Abgesenkt werden beim Empfänger nur NETZ-Ströme, nie GLOBAL- oder DURCHSAGE-Ströme; zwei Durchsagen senken sich also nicht gegenseitig ab (C9).
- Normale Sprache in anderen Netzen läuft während einer Durchsage weiter, nur leiser. Eine "Durchsage mit Stille", bei der der Server andere Netze anhält, ist für v2 vorgemerkt.
- Unter Überlast haben Durchsage und Global Vorrang (7.4).

### 6.7 Aufstellung atomar

Beim Anwenden (`AufstellungAnwenden`, `Verschieben`, `EinsteigenRolleSchiff` für andere, `PlaetzeFreigeben`, `Verwaltung`) gilt:

1. Der Zustandsschreiber prüft alles vorab: Objekt und Version (bei `AufstellungAnwenden` ohne `version` die aktuelle), alle Rollen, Schiffe und Netze existieren, Feldlisten nach 6.9, Rechte und Rang des Anfragenden (6.10), `basisVersion` je betroffenem Konto (6.2).
2. Kapazität am Endzustand der ganzen Anfrage. Bei `AufstellungAnwenden` hat die Aufstellung Vorrang: Nicht gelistete Konten, die einen von ihr gebrauchten Platz BELEGT oder RESERVIERT haben, fallen auf STANDARD zurück und stehen in der Antwort unter `verdraengt`. `PLATZ_BELEGT` gibt es dort nur, wenn die Aufstellung selbst mehr Konten auf einen Platz setzt, als die Kapazität erlaubt, und `erzwingen` fehlt.
3. Bei ERSETZEN fallen alle nicht gelisteten Konten mit Platz auf STANDARD zurück. Ausgenommen sind der Anfragende und Konten mit `ZUWEISEN` (auch aus einem laufenden Auftrag), solange sie nicht in der Liste stehen. Einträge für ADMIN-Konten werden übersprungen und gemeldet, wenn der Anfragende kein ADMIN ist (6.10).
4. Konten ohne Sitzung bekommen einen RESERVIERTEN Platz (6.9). Unverknüpfte Discord-IDs bleiben offen und werden nachgezogen (6.9).
5. Ein einziger Fehler in Schritt 1 oder 2 lehnt das Ganze ab, mit einer Liste aller betroffenen Einträge (`details.fehler`); nichts wird geändert.
6. Der Schreiber baut den neuen Schnappschuss mit allen Mitgliedschaften, festen Feldern, Sendeauswahlen und `zustandsVersion + 1`, schreibt ihn dauerhaft (Gruppen-Commit, 6.2) und tauscht ihn aus. Ab dem nächsten Paket gilt der neue Stand für Senderecht und Hören.
7. Er meldet: `Mitgliedschaften` an alle betroffenen online-Sitzungen, `Praesenz` (Delta) an alle aktiven, dann die Antwort an den Anfragenden.

Ziel: Von der Taste der Einsatzleitung bis zum geänderten Hören bei allen sind es höchstens 1 RTT plus 10 ms Serverzeit für 40 Konten (A13: Aufstellung für 40 in unter 1 s), auch während andere Funker gleichzeitig Numpad-Tasten benutzen. Gemessen in P-09 und P-50. Die Tasten- und Joystick-Belegung für die Einsatzleitung ist reine Client-Sache und löst `AufstellungAnwenden` oder `Verschieben` aus.

### 6.8 Hören nur für Mitglieder

- Die Weiterleitung nutzt ausschließlich die Mitgliedschaften im Schnappschuss (6.4). Welche Netze der Client zu hören glaubt, spielt keine Rolle.
- Nach dem Austausch des Schnappschusses bekommt ein entferntes Mitglied kein einziges weiteres Paket des Netzes, nur einmal ein erzeugtes ENDE. Unterwegs können nur Pakete sein, die schon vor dem Austausch versiegelt waren (Prüfpunkt P-47).
- Sitzungen in REGISTRIERUNG oder WARTERAUM bekommen weder Sprache noch Netz-, Konto- oder Aufstellungsdaten (P-44).

### 6.9 Plätze, Felder, Sendeauswahl

- Feldlisten (gilt für Rollen, `Verschieben`, `AufstellungSpeichern`, `SatzAnwenden`, `FeldSetzen`, `EinsteigenRolleSchiff`, `RolleWaehlen`): `feld` 0-15 und eindeutig; `netzId` existiert und ist ungleich 1; je Konto steht jedes Netz in höchstens einem Feld. Seit 0.6.0 ist `netzId` in den Feldern eines Kontos ein gewöhnliches Netz oder eine Kanal-ID (auch Ruhe), nie die Netz-ID eines Kanalnetzes und nie Orgaweit (6.16); `felderPatch`, `felder` und Aufstellungen nehmen Kanal-IDs wie Netze. Beim Bilden aus einer Rolle werden Doppelte zusammengefasst: Ein festes Feld gewinnt, sonst das niedrigere Feld.
- STANDARD: die Netze mit `standard = true` in der Folge von `reihenfolge` ab Feld 0; `hoeren = true`; `senden = (offen == "HOEREN_SENDEN")`; nicht fest; Sendeauswahl = das erste Feld mit `senden`. Ein Kanalnetz mit `standard` liegt dort auf Ruhe (Ch. 0) und sendet nicht.
- Seit 0.5.0 (6.14) nimmt jeder Weg zum Platz die laufende Phase: Hat sie eine Variante für die Rolle, kommen die Felder aus der Variante statt aus `rollen[].felder` (Einsteigen, Verschieben mit `rolleId` oder `schiffId`, "Wie zuletzt", Aufstellung, Nachziehen, `SatzAnwenden` mit der Sonderregel in 6.14).
- Feste Felder: `fest = true` haben Felder, die `Verschieben` oder `AufstellungAnwenden` ausdrücklich setzen, und Rollenfelder mit `fest = true`. `FeldSetzen` und `SatzAnwenden` ändern feste Felder nicht (`FELD_FEST`), bis auf den Ch.-Wechsel per `FeldSetzen` (seit 0.8.1); `HoerenSetzen` bleibt erlaubt (A17). Bei der Selbstwahl behalten feste Felder ihre Feldnummer; kollidierende Rollenfelder rücken auf das nächste freie Feld; was über Feld 15 hinausgeht, entfällt und steht in `Mitgliedschaften.entfallen`. Die Einsatzleitung hebt `fest` per `felderPatch` mit `fest: false` auf.
- Sendeauswahl: Menge von Feldnummern plus Global. Rolle bzw. Aufstellung geben sie über `sendenStandard` bzw. `sendeFelder` vor; ohne `MEHRFACH_SENDEN` gilt nur das niedrigste markierte Feld. Nach jeder Umstellung meldet `Mitgliedschaften` die neue Sendeauswahl, auch beim Anmelden. Nach "Phase 2" muss also niemand sein Sendenetz suchen.
- Platzzustände:
  - BELEGT: Das Konto hat eine Sitzung oder ist in der Wiederverbindungsfrist (10 min nach Sitzungsende).
  - RESERVIERT: Durch `ZUWEISEN` (Verschieben, EinsteigenRolleSchiff für andere) oder `AufstellungAnwenden` gesetzt, gilt 12 h ab dem Setzen, auch ohne Sitzung. Seit 0.11.0 setzt `Verschieben` mit `rolleId` 0 ohne `felder` und ohne `felderPatch` ("zurück auf Standard", auch über "Abend beenden") keine Reservierung: Es ist keine Einteilung. Ein `felderPatch` allein (etwa der Ch. eines Felds, 6.16) reserviert wie bisher.
  - Die Kapazität zählt BELEGT und RESERVIERT. Selbst gewählte Plätze verfallen mit der Wiederverbindungsfrist; von `ZUWEISEN` gesetzte bleiben für die 12 h, auch wenn jemand kurz abstürzt.
- Beim Anmelden: Innerhalb der Frist oder bei einer Reservierung stellt der Server den Stand still wieder her, ohne neue `kontoVersion`. Eine Reservierung kommt mit ihrer Herkunft und `durch` zurück: AUFSTELLUNG, seit 0.11.0 auch VERSCHIEBEN (bis 0.10.0 als WIEDERHERGESTELLT mit `durch`); so zeigt auch ein älterer Client ", eingeteilt von ...". Hat der Funker nach der Einteilung selbst etwas geändert (`FeldSetzen`, `RolleWaehlen`, `SatzAnwenden` ohne Rolle; die Reservierung bleibt dabei), bleibt seine Quelle SELBST, ROLLE oder SATZ ohne `durch` (seit 0.11.0). WIEDERHERGESTELLT (mit `durch` 0) gibt es nur noch in der Frist ohne Reservierung. Sonst startet das Konto mit STANDARD, und `Mitgliedschaften.letzterStand` nennt den früheren Stand zur Übernahme (A21), die der Client anbietet und per `SatzAnwenden` ausführt. Die erste Anmeldung eines Kontos ohne je eine Sitzung behält ihren Anfangsstand seit 0.11.0 nur ohne Reservierung: Mit abgelaufener gilt STANDARD wie nach einer Sitzung (vorher saß das Konto wieder auf seinem längst vergebenen Platz), und ein VERSCHIEBEN ohne Reservierung ("zurück auf Standard") kommt als STANDARD ohne `durch`.
- Kapazität und Wahl: Eine Rolle, die im Schiff nicht in `plaetze` steht, ist dort nicht wählbar (`UNGUELTIG`). Rollen ohne Schiff haben eine optionale `kapazitaet` (fehlt = unbegrenzt). `brauchtSchiff` ohne `schiffId` ergibt `UNGUELTIG`. Gesperrte Selbstwahl ergibt `SELBSTWAHL_GESPERRT` (Regeln unten). `mindest` eines Platzes zählt nur für die Anzeige (Ampel auf dem Brett); einteilen geht auch darunter.
- Einheiten (A39): Ein Schiff im Datenmodell ist eine Einheit mit `art` SCHIFF, TRUPP oder ORGA und freiem `typ`. Der Server behandelt alle drei gleich (Kennungen, Plätze, Kapazität); Infanterie und Orga sind nur Einheiten ohne Schiffsnetze.
- Sperre der Selbstwahl (F9, seit 0.3.0): `Verwaltung SELBSTWAHL {an: false}` merkt den Urheber (`selbstwahlDurch`, die Konsole als 0) und eine Frist (`selbstwahlBis` = jetzt + 12 h). Mit `nachzuegler` (Standard, "Wechsel sperren") nimmt ein Konto ohne Platz weiter einen freien Platz, wer einen hat, wechselt nicht; ohne ("Ganz sperren") wählt niemand selbst. Konten mit `ZUWEISEN` sind nie gesperrt. Den eigenen Platz per `SatzAnwenden` mit `rolleId: 0` aufgeben ist bei wirkender Sperre ebenfalls ein Wechsel (`SELBSTWAHL_GESPERRT`); sonst stiege man danach als Nachzügler ohne Platz woanders ein. Die Sperre wirkt nur, solange ihr Urheber `ZUWEISEN` hat (die Konsole immer) und die Frist läuft; danach hebt der Fristen-Takt sie auf. Endet der Auftrag des Urhebers, endet die Sperre mit ihm (6.12 Regel 7). Eine Sperre aus 0.2.0 hat weder Urheber noch Frist und bleibt, bis die Konsole oder jemand mit `ZUWEISEN` sie aufhebt. Seit 0.5.0 setzt eine Sperre nur, wer `ZUWEISEN` hat (die Konsole immer); `NETZE_VERWALTEN` allein, etwa ein ADMIN im Funker-Modus in der Admin-Sitzung, hebt sie auf, setzt aber keine (`KEIN_RECHT`), denn sie wirkte keinen Augenblick.
- Nachziehen: Der Server merkt sich die zuletzt angewandte Aufstellung (id, version, Zeit). Ihre offenen Einträge (unverknüpfte Discord-IDs, Konten ohne Sitzung) greifen bei Verknüpfung und Freigabe bzw. Anmeldung automatisch, solange keine andere Aufstellung angewandt wurde, höchstens 12 h vergangen sind und der Anwender noch `ZUWEISEN` hat; endet sein Auftrag, endet das Nachziehen (6.12 Regel 7). Konten mit `ZUWEISEN` bekommen `Meldung{AUFSTELLUNG_NACHGEZOGEN}`.
- `PlaetzeFreigeben` löst alle Plätze von Konten ohne Sitzung, etwa vor einem neuen Einsatz.

### 6.10 Rangfolge und Rechteverwaltung

- Rang: FUNKER < RAIDLEITER < ADMIN.
- Rangregel (seit 0.3.0): `ZUWEISEN` wirkt auf FUNKER und RAIDLEITER, auf ADMIN-Konten nur durch einen ADMIN (Verschieben, EinsteigenRolleSchiff für andere, Stummschalten, PlaetzeFreigeben, Einträge einer Aufstellung). Sonst `KEIN_RECHT` bzw. bei Aufstellungen Überspringen mit Meldung. Bis 0.2.0 galt "gleiche oder niedrigere Stufe"; sie hätte einen FUNKER mit Auftrag daran gehindert, die Führung einzuteilen, und ihr Grund entfiel, als RAIDLEITER `ZUWEISEN` verlor. Seit 0.5.0 (6.15) wirkt `ZUWEISEN` auch auf ein ADMIN-Konto, solange es "Heute als Funker" spielt: Verschieben, EinsteigenRolleSchiff für andere, PlaetzeFreigeben, Einträge einer Aufstellung samt Verdrängen, Schützen und Nachziehen, ERSETZEN und das Schalten einer Phase. Stummschalten gehört nicht dazu: Ein ADMIN-Konto schaltet weiter nur ein ADMIN stumm, auch im Funker-Modus, denn der Betroffene hebt eine fremde Stummschaltung nie selbst auf, und ein abtrünniger Beauftragter könnte den Eigentümer sonst bis zu einer Stunde stumm halten.
- Eine Stummschaltung heben auf: wer sie gesetzt hat, ein Konto höherer Stufe als der Setzende, oder ein ADMIN. Der Betroffene selbst kann eine fremde Stummschaltung nicht aufheben.
- Rechteverwaltung (`KontoVerwaltung`):
  1. Niemand vergibt ein Recht, das er selbst nicht dauerhaft hat; Rechte aus einem Auftrag zählen dabei nicht (6.12 Regel 3).
  2. Niemand ändert die eigene Stufe oder die eigenen Rechte.
  3. Konten der Stufe ADMIN ändert, sperrt und löscht nur ein ADMIN.
  4. Der letzte aktive ADMIN kann nicht gesperrt, herabgestuft oder gelöscht werden; das geht nur über das Admin-Werkzeug.
  5. STUFE setzt die Rechte auf den Standard der neuen Stufe zurück. Einzelrechte werden als sichtbare Abweichung (`plus`, `minus`) gespeichert und in der Verwaltung angezeigt.
  6. Jede Änderung an Stufe oder Rechten meldet der Server an `KONTEN_VERWALTEN` (`Meldung{RECHTE_GEAENDERT}`) und über den Bot in den Admin-Kanal.
  7. Aus dem Client nur in einer Admin-Sitzung (3.9); SPERREN, ENTSPERREN, STUFE, RECHTE und LOESCHEN nur mit frischer Bestätigung. Ohne Admin-Sitzung `ADMIN_SITZUNG_NOETIG`, ohne frischen Code `SCHRITT_BESTAETIGEN`, beides ohne Seiteneffekt.
  8. Stufe ADMIN setzen, Verwaltungsrechte als Einzelrecht vergeben und Stufe oder Rechte eines privilegierten Kontos ändern geht nur an der Konsole (`NUR_ADMIN_WERKZEUG`).
  9. ADMIN-Konten werden im Client nur gesperrt (Notbremse, Regel 4 gilt weiter). Entsperren, löschen, Stufe und Rechte eines ADMIN-Kontos gehen nur an der Konsole, Name und SC-Namen eines anderen ADMIN-Kontos ebenso; die eigenen Namen setzt jeder selbst. Geräte eines anderen privilegierten Kontos entfernt oder sperrt im Client niemand (`NUR_ADMIN_WERKZEUG`); die eigenen Geräte und Geschwister gehen nach 3.6.
  10. Das Konto des Eigentümers fasst im Client niemand an (3.10).
- Privilegierte Konten (Stufe ADMIN oder eines der Rechte `GERAETE_FREIGEBEN`, `KONTEN_VERWALTEN`, `NETZE_VERWALTEN`): Geräte nur über das Admin-Werkzeug (3.5). Wird ein Konto privilegiert, verfallen seine Geräte in WARTET.

### 6.11 Regeln für den Client (verbindlich)

- C1 Sendeauswahl: Der Client hält die Sendeauswahl als Feldnummern und setzt sie erst beim Senden über die aktuellen Mitgliedschaften in Netz-IDs um.
- C2 Konstante Phase: Zielart, Ziele und VA-Kennzeichen sind je Sprechphase konstant. Jede Änderung beendet die Phase mit ENDE an die alten Ziele und beginnt eine neue mit BEGINN, ohne neuen Pre-roll. Wechselt die Auswahl bei gedrückter Taste (Numpad), beginnt die neue Phase erst, wenn die Auswahl 250 ms stabil ist; so gehen keine Wortfetzen in Zwischennetze.
- C3 Auslöser: Die Taste schlägt die Sprachaktivierung. Ohne `MEHRFACH_SENDEN` gilt die zuletzt gedrückte Netz-Taste, nie mehrere Ziele. Mit `MEHRFACH_SENDEN` ergeben gleichzeitig gehaltene Netz-Tasten die Vereinigung. Der Standard-Auslöser je Netz kommt aus `ausloeserStandard` (A10, A20).
- C4 Umstellung mitten im Spruch: Fällt durch einen Mitgliedschaften-Push ein gerade gesendetes Ziel weg (Feld geleert, Senderecht entzogen, stummgeschaltet), beendet der Client die Phase und setzt ohne Unterbrechung eine neue Phase mit den verbleibenden Zielen fort; bleibt keines, endet das Senden. Wechselt dagegen das Netz hinter einem gesendeten Feld auf ein anderes Netz, geht an das neue Netz erst nach Loslassen und erneutem Drücken (PTT) bzw. nach geschlossenem Gate (VA) etwas; die übrigen Ziele setzen fort. In beiden Fällen zeigt der Client "Sendenetz geändert durch <Name>: jetzt <Netz>".
- C5 Zusammenfassen: Zustandsmeldungen (SendeauswahlSetzen, HoerenSetzen, PegelSetzen, SprechanzeigeAbo) sendet der Client nur mit dem letzten Stand, höchstens 4 je Sekunde und Art; einen Pegelregler beim Loslassen oder höchstens alle 250 ms. Tastenwiederholung und Hat-Switch führen so nie zu Verstößen.
- C6 Empfang und Ende: Eine Zeitlinie beginnt mit dem ersten Paket jedes Paares (Absender, Sprechphase), auch ohne BEGINN; BEGINN ist nur der Hinweis für den Klick. Die Phase endet mit dem ersten ENDE oder 300 ms, nachdem der Jitterpuffer dieses Stroms leer gelaufen ist (nicht 300 ms nach dem letzten Paket). Später eintreffende Pakete derselben Phase setzen sie ohne Klick fort. Folgt auf ein ENDE binnen 100 ms eine neue Phase desselben Absenders (C4), entfallen Ende- und Beginn-Klick. Verlustverdeckung (FEC, Deep PLC) höchstens 60 ms am Stück, danach ausblenden bis zum Ende.
- C7 Mehrere Treffer-Netze: Lautstärke = Maximum über die Treffer-Felder, Panorama vom Feld mit der höchsten Lautstärke (bei Gleichstand das erste der Liste), Pegel je Funker aus dem Eintrag (Absender, dieses Netz), sonst (Absender, 0). Bei Art GLOBAL und DURCHSAGE sind Pegel je Funker und Feldlautstärke nach unten auf einen Mindestpegel begrenzt, und eine lokale Stummschaltung einzelner Personen wirkt dort nicht (A23).
- C8 Rennen mit Pushes: Ein Netz, das in der NetzListe steht, aber in keinem eigenen Feld: mittig und mit Standardlautstärke abspielen, bis zu 500 ms auf den Mitgliedschaften-Push warten und dann zuordnen, erst danach `Zustand` anfordern. Eine unbekannte Netz-ID: mittig abspielen, "unbekanntes Netz" zeigen, `Zustand` anfordern. `Zustand` höchstens einmal je Version und höchstens alle 5 s.
- C9 Absenkung: Beim ersten Paket mit Art DURCHSAGE oder GLOBAL senkt der Client alle NETZ-Ströme ab (Standard -12 dB, einstellbar 0 bis -30 dB, Anstieg 50 ms) und hebt sie 300 ms nach dem Phasenende dieses Stroms im Empfänger wieder an (C6). `DurchsageEnde` steuert nur die Anzeige. GLOBAL- und DURCHSAGE-Ströme werden nie abgesenkt.
- C10 Encoder und Frame-Nr nach 5.6.
- C11 Threads und Sockets nach 5.14.

### 6.12 Beauftragung (Stufe 2, seit 0.3.0)

Der "Auftrag für einen Abend" aus dem Entwurf zur Raidleitung (A37) gibt einem Konto für eine begrenzte Zeit die Einsatzleitung: einteilen (`ZUWEISEN`), die Sprechanzeige aller Netze und, abwählbar, die Rechte der Führung. Er liegt als `Beauftragung{kontoId, von, bis, rechte, durch, seit, grund?, begonnen}` am Konto im Lagezustand, nicht im Kontenbuch. Erteilt wird er im Verwaltungsteil (0x0017) oder an der Konsole (`funk-admin auftrag`, BETRIEB.md).

1. Erteilen und Entziehen nur mit `KONTEN_VERWALTEN`, im Client nur in einer Admin-Sitzung mit frischer Bestätigung (3.9); die Konsole darf es immer. Der Erteilende muss jedes Recht des Auftrags selbst dauerhaft haben (aus Stufe oder Einzelrecht, nie aus einem eigenen Auftrag), sonst `KEIN_RECHT`. Sich selbst erteilt niemand einen Auftrag (`KEIN_RECHT`).
2. Ein Auftrag trägt nur Rechte der Einsatzleitung (Tabelle 6.1), immer `ZUWEISEN` und `SPRECHANZEIGE_ALLE`; die vier Rechte der Führung sind abwählbar, Verwaltungsrechte unmöglich (`UNGUELTIG`). Ein Beauftragter wird dadurch nie privilegiert, seine Geräte bleiben unberührt. Die Zusatzbestätigung "Rückfrage beim Inhaber erfolgt" bei der Gerätefreigabe eines Kontos mit laufendem oder geplantem Auftrag (3.5) ist eine Regel der Oberfläche; die Aufträge kennt sie aus `Beauftragung LISTE`.
3. Weitergeben geht nicht: Ohne `KONTEN_VERWALTEN` erteilt der Beauftragte keine Aufträge und ändert keine Stufen, Rechte oder Konten; Auftragsrechte zählen bei Regel 1 aus 6.10 nicht mit.
4. Ein Auftrag je Konto; erneutes Erteilen ersetzt ihn, etwa zum Verlängern. Höchstens 4 laufende oder geplante Aufträge gleichzeitig. Beginn höchstens 14 Tage in der Zukunft, Ende nach dem Beginn und höchstens 24 h danach. Nicht für ADMIN-Konten und nicht für gesperrte (`UNGUELTIG`, mit Klartext).
5. Wirksam nur, solange `von <= jetzt < bis` (Serverzeit). Das prüft der Server bei jeder Aktion und je Sprachpaket am Schnappschuss, der je Sitzung die Rechte des Auftrags mit `von` und `bis` hält; dieselben wirksamen Rechte gelten bei den Empfängern von Pushes (Praesenz-Details, Meldungen an `ZUWEISEN`, `AufstellungGeaendert`, Sprechanzeige). Ein Fristen-Takt (jede Sekunde) räumt abgelaufene Aufträge ab, meldet geplante beim Beginn, schickt die Pushes und schreibt das Journal; an der Wirksamkeit ändert er nichts mehr.
6. Entziehen wirkt sofort; der Beauftragte kann ihn selbst abgeben (ABGEBEN, "Abend beenden"). Von selbst endet er, wenn das Konto gesperrt, gelöscht oder in der Stufe geändert wird, und wenn das erteilende Konto gesperrt oder gelöscht wird oder `KONTEN_VERWALTEN` verliert (`warum` ERTEILER). Aufträge von der Konsole hängen an keinem Konto.
7. Hat das Konto nach dem Ende kein `ZUWEISEN` mehr, enden eine Sperre der Selbstwahl, die es gesetzt hat, das Nachziehen der Aufstellung, die es zuletzt angewandt hat (6.9), und seit 0.6.0 die Kanalsperren, die es gesetzt hat (6.17). Dasselbe gilt beim Beginn des Funker-Modus (6.15). Reservierungen laufen ihre 12 h weiter, Stummschaltungen ihre Zeit, die laufende Einteilung bleibt.
8. Sichtbar: Journal (`AUFTRAG_ERTEILT`, `AUFTRAG_BEGONNEN`, `AUFTRAG_ENTZOGEN`, `AUFTRAG_ABGEGEBEN`, `AUFTRAG_ABGELAUFEN`, `AUFTRAG_BEENDET`, mit Klarnamen und Zeiten in Ortszeit), `Meldung{BEAUFTRAGUNG}` an `KONTEN_VERWALTEN` und an den Beauftragten, und eine Bot-Nachricht in den Admin-Kanal; bis es den Bot-Dienst gibt, steht sie nur als Logzeile des Dienstes da (`journalctl -u funk-server`), Journal und Meldung tragen die Nachvollziehbarkeit.
9. Beim Beginn schickt der Server der Sitzung einen vollen Praesenz-Stand mit Details, beim Ende einen ohne, und beendet dann ein Sprechanzeige-Abo ohne `SPRECHANZEIGE_ALLE`. Der Client erfährt Beginn und Ende über `Mitgliedschaften.konto.beauftragung` (`aktiv`) und blendet das Brett ohne Neustart ein und aus.
10. Gespeicherte Aufstellungen ändern oder löschen nur ihr Ersteller oder ein ADMIN (4.5, 0x0040); eigene anlegen und alle anwenden darf jeder mit `ZUWEISEN`.

Gespeichert wird der Auftrag mit dem Lagezustand und übersteht einen Neustart. `funk-admin sicherung` lässt Aufträge aus der Kopie von `lage.json` weg: Ein entzogener Auftrag soll nicht aus einem alten Stand zurückkommen; nach dem Zurückspielen erteilt der Admin sie neu. Dauerhaft Beauftragte, etwa ein fester Stellvertreter, gehen weiter über das Einzelrecht `ZUWEISEN`.

### 6.13 Mitglieder, Rollen, Mitgliedskarte (Untergilden seit 0.11.8 entfallen)

Gepflegt wird alles hier nur mit `KONTEN_VERWALTEN` in einer Admin-Sitzung (A43), mit einer Ausnahme: Den Rollenkatalog ergänzen darf auch die Führung (Antwort 4, unten). Jede Änderung steht im Journal, geht als `Meldung{MITGLIED_GEAENDERT}` an die Admin-Sitzungen, und betroffene Konten bekommen `Mitgliedschaften` mit ihren neuen Angaben (`konto`) sowie ein Praesenz-Delta. Gespeichert wird in `mitglieder.json`.

- **Rollenkatalog** (N6): höchstens 64 Einträge `{id, name, gruppe?, rollen? (Vorschlag für Funkrollen), raidHelper? (className/specName)}`, IDs und Namen eindeutig (Namen nach NFKC, Großschreibung). SETZEN ersetzt den ganzen Katalog und zählt `version` hoch; entfernte Einträge verschwinden aus Mitgliederrollen, Wünschen und Vormerkungen. Ergänzen dürfen auch Konten mit `ZUWEISEN` oder `GLOBAL_SENDEN` ohne Admin-Sitzung (Führung und Einsatzleitung, Sarevoc am 30.09.2026, Antwort 4): SETZEN muss dann jeden vorhandenen Eintrag unverändert enthalten (Name, Gruppe, Funkrollen, Raid-Helper) und darf nur neue anfügen, sonst gilt die Antwort ohne Recht (`KEIN_RECHT`, bei einem ADMIN-Konto `ADMIN_SITZUNG_NOETIG`). Entfernen und Umbenennen bleibt beim Admin, denn mit einem Eintrag verschwänden die Mitgliederrollen, die nur der Admin vergibt. Das Journal nennt, wer ergänzt hat.
- **Mitgliederrollen**: höchstens 16 je Konto, nur aus dem Katalog, gesetzt vom Admin (`Mitglied AENDERN`). Das Mitglied kann sie nicht entfernen.
- **Lieblingsrollen** (N8): das Mitglied selbst hakt bis zu 8 Katalogrollen ab, eine als erste Wahl (auch eine eingetragene Rolle). Rollen, die der Admin schon eingetragen hat, stehen nicht zusätzlich als Wunsch da.
- **Vormerkungen** (N7): höchstens 500, `{scName, discordName?, rollen, notiz? (80)}`, immer als künftiger FUNKER ohne Rechte. SC-Name und Discord-Name einer Vormerkung sind belegt (`NAME_VERGEBEN` für Konten und andere Vormerkungen, A53 für den Discord-Namen). Bei der Freigabe schlägt der Server passende Vormerkungen vor und übernimmt eine nur mit `vormerkungId` (3.5).
- **Liste einfügen** (`Mitglied EINFUEGEN`): je Zeile `SC-Name; Discord-Name; Kürzel; Rolle, Rolle` (nur der SC-Name ist Pflicht, Rollen nach Katalognamen). Die dritte Spalte bleibt im Format, damit alte Listen weiter passen, wird seit 0.11.8 aber übergangen; man lässt sie leer (`SC;Discord;;Rollen`). Steht dort etwas, legt der Server trotzdem an und schickt einmal je Liste `hinweis` ("Spalte 3 (Untergilde) wird seit 0.11.8 übergangen"). Alles oder nichts: Passt eine Zeile nicht (SC-Name oder Discord-Name vergeben oder doppelt in der Liste, Rolle unbekannt, zu viele Felder), wird nichts angelegt, und jede fehlerhafte Zeile steht mit Klartext in der Antwort.
- **Untergilden** (N9): entfallen seit 0.11.8 (Sarevoc: "Untergilden entfallen ganz, überall"; neue Gruppen offen für 0.12). Nichts zeigt, sucht oder vergibt sie mehr, kein Recht hing je an ihnen. `0x001D` bleibt als Hülle (4.5), `untergilde` in KontoStand, Praesenz, KontenVerzeichnis, Mitgliedskarte, Mitglied LISTE und Vormerkungsvorschlag bleibt im Protokoll, wird aber nie gefüllt; `Mitglied` übergeht `untergilde`. Gespeichert bleibt `untergilden` in `mitglieder.json` und wird weiter geschrieben, damit eine ältere Serverfassung den Stand wieder laden kann (sie scheitert, wenn das Feld fehlt); gelesen oder gezeigt wird es nicht. Verändert wird es nur, wenn ein Konto gelöscht wird oder eine Vormerkung wegfällt (gelöscht oder bei der Freigabe übernommen): Deren Nummer fällt aus `konten` bzw. `vormerkungen` heraus, die eines Kontos auch aus `ansprechpartner` (Datenschutz); die Freigabe trägt keine Untergilde mehr ein.
- **Zeichen** (A49): Der Server leitet sie ab, nie der Client: `ADMIN` für ein ADMIN-Konto mit eingerichtetem Admin-Nachweis, `FUEHRUNG` für die Stufe RAIDLEITER, seit 0.3.0 `EINSATZLEITUNG` für einen laufenden Auftrag (ein geplanter zeigt noch nichts). "Mein Zeichen zeigen" (`ProfilSetzen.zeichenZeigen`, Standard an) blendet die eigenen Zeichen für andere aus; das Konto selbst sieht sie immer. Seit 0.11.7 lässt sich `ADMIN` und `EINSATZLEITUNG` einzeln ausblenden (`abzeichenAus`, wie Abzeichen); `FUEHRUNG` schickt der Server weiter, der Client zeichnet es nicht mehr.
- **Abzeichen** (seit 0.11.7, Sarevoc 14./15.10.2026): Ränge in drei Laufbahnen (Marine 10, Navy 10, Orga 8; je Laufbahn höchstens einer) und Tätigkeiten (22, höchstens 6 je Person). Katalog, Namen und Bilder kommen mit dem Programm (`abzeichen.json`, eingebettet in Funk.Protokoll); im Programm wird nichts angelegt oder umbenannt. Vergeben nur `KONTEN_VERWALTEN` in einer Admin-Sitzung (`Abzeichen`, 0x0025), nie die Führung. Grundsatz (Sarevoc): "ein Abzeichen haben ist eine Sache, es zeigen eine eigene Entscheidung". Das Mitglied wählt selbst ein Abzeichen für vorn (`abzeichenVorn`) und blendet jedes einzeln aus (`abzeichenAus`), auch die Zeichen `ADMIN` und `EINSATZLEITUNG`; "Mein Zeichen zeigen" aus blendet alles aus. Ausblenden ändert nur die Anzeige bei anderen, nie Rechte. Praesenz und Mitgliedskarte tragen nur Sichtbares mit denselben Sperren wie die Zeichen (Freigabe, "Mein Zeichen zeigen"; auch Sitzungen mit `ZUWEISEN` sehen Ausgeblendetes nicht), vorn nur, wenn besessen und sichtbar. Das Konto selbst bekommt in `konto` alles Eigene, Admin-Sitzungen in `Mitglied LISTE` alles, was ein Mitglied besitzt, damit sie vergeben können. Das Journal nennt jede Vergabe (`ABZEICHEN`, mit Titeln wie "Marine: Sergeant, Gunner M").
- **Mitgliedskarte und "Wer ist da"** (A48, N10, Sarevoc am 30.09.2026, Antwort 3): "Wer ist da" zeigt den Discord-Namen als Namen und den eindeutigen Hauptnamen daneben, denn Discord-Namen können sich gleichen (3.3). Praesenz trägt für alle Hauptname und Discord-Name, dazu Zeichen und Abzeichen, solange der Inhaber seine Angaben freigibt (`ProfilSetzen.karteFreigeben`, Standard an). Die Karte (`Mitgliedskarte`) zeigt jedem Namen, Hauptname, Discord-Name, online und Platz; Zeichen und Abzeichen nur mit Freigabe, Rollen, Wünsche und erste Wahl mit Freigabe und solange `mitgliedskarte.rollenFuerAlle` in der Serverkonfiguration gilt (Standard ja). Weitere SC-Namen, Verbindung, Güte und letzten Platz sehen nur Konten mit `ZUWEISEN`, ADMIN-Konten und das Konto selbst; sie sehen auch alles andere, unabhängig von der Freigabe. Andere bekommen Karten nur von Konten in der Praesenz (online oder mit Platz), sonst `UNBEKANNT`; so lässt sich über die Karte nicht die ganze Mitgliederliste abfragen.
- Wird ein Konto gelöscht, fallen seine Mitgliedsangaben (seit 0.11.7 auch Abzeichen und ihre Ansicht) weg (Datenschutz). Auch sein Platz in einer gespeicherten Untergilde und die Rolle als Ansprechpartner fallen weg (oben).

### 6.14 Einsatzphasen (Ausbaustufe S2, seit 0.5.0)

Eine Phase ist ein Netzbild der Flotte, keine Personenliste (Entwurf zur Ausbaustufe S2, heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Zuteilung"): Der Admin legt einmal fest, welche Felder eine Rolle in "2 Entern" hat, und die Einsatzleitung schaltet im Raid mit zwei Drücken. Plätze, Rollen, Reservierungen und Belegung ändert eine Phase nie, also gibt es kein Verdrängen und kein `PLATZ_BELEGT`, und Nachzügler und Ersatzleute bekommen das Netzbild von selbst.

- Modell: Phase 1 ist die Grundstellung (die Felder der Rollen); sie trägt nur Name und Kurzname. Dazu höchstens 4 weitere Phasen (Nummer 2 bis 5, Lücken erlaubt), je mit Name (24) und Kurzname (12) und je Rolle, die sich ändert, einer Variante: einer vollständigen Feldliste in der Form der Rollenfelder (6.9, mindestens ein Feld). Rollen ohne Variante bleiben in der Phase wie in der Grundstellung. Eine Variante gilt für alle Einheiten mit dieser Rolle; ein eigenes Netz je Schiff geht über eine Schiffsnetz-Kennung. Zusammen höchstens 512 Variantenfelder. Die Phasen gehören zur NetzListe (`phasen`, Version wie jede Verwaltung), die laufende Phase `{nr, seit, durch}` zum Lagezustand: Ein Wechsel ändert die NetzListe nicht.
- Anlegen: `Verwaltung PHASE_SETZEN` und `PHASE_LOESCHEN` mit `NETZE_VERWALTEN` in der Admin-Sitzung, einzeln oder im `PAKET`. Netz- und Rollenverweise prüft der Server wie bei den Rollenfeldern; was zu einem gelöschten Netz oder einer gelöschten Rolle gehört, fällt aus den Varianten. Bleibt einer Variante nach `NETZ_LOESCHEN` kein Feld, fällt sie ganz weg (eine leere Variante gibt es nicht, auch nicht beim Laden des Zustands): Die Rolle steht in der Phase dann wie in der Grundstellung, und läuft die Phase, bekommt sie deren Felder sofort. Ändert `PHASE_SETZEN` die laufende Phase, stellt der Server sofort um, wie ein Wechsel von der alten auf die neue Fassung. Wird die laufende Phase gelöscht, fällt die Flotte auf die Grundstellung zurück, mit allen Folgen eines Wechsels. Die Grundstellung löscht niemand.
- Schalten (`PhaseSchalten`, 0x003D), in einem Commit:
  1. Nur mit `ZUWEISEN`. Die Führung ohne Auftrag und ein ADMIN im Funker-Modus schalten nicht.
  2. `erwartet` ungleich der laufenden Phase: `VERALTET` mit `{nr, durch}`.
  3. Unbekannte Nummer: `UNBEKANNT`; die laufende noch einmal: Ok, nichts umgestellt.
  4. Betroffen ist jedes Konto mit Platz (BELEGT oder RESERVIERT, auch ohne Sitzung) und seit 0.6.0 jedes Konto mit gewählter Rolle (6.16), dessen Rolle in der alten oder der neuen Phase eine Variante hat und das der Schaltende nach der Rangregel einteilen darf (6.10); die übrigen stehen in `ausgelassen`. Die Phase richtet sich nach der gewählten Rolle, sonst nach der Rolle des Platzes; Schiffsnetz-Kennungen löst die Einheit des Platzes auf, Kanalnetze der Ch. nach 6.16.
  5. Neue Felder: die Rollenfelder der neuen Phase und jedes bisherige Feld, dessen Netz nicht unter den Rollenfeldern der alten Phase steht (Zusatznetze der Leitung, eigene offene Netze). Diese bleibenden Felder behalten ihre Nummer, belegte Nummern rücken nach wie bei der Selbstwahl; ein festes bleibendes Feld gewinnt gegen ein Rollenfeld mit demselben Netz, ein nicht festes geht darin auf.
  6. "Hören aus" bleibt für Netze, die vorher und nachher da sind, wenn das neue Rollenfeld nicht fest ist.
  7. Sendeauswahl: Wer auf dem Sendevorschlag der alten Phase sendete, sendet danach auf dem der neuen. Wer selbst etwas anderes gewählt hat und es weiter darf, behält es (Netze über ihre neue Feldnummer). Verglichen wird mit dem Komfortzustand (die echte Auswahl des Clients), sonst mit der Sendeauswahl am Konto.
  8. Unveränderte Konten fasst der Server nicht an: keine neue `kontoVersion`, kein Push.
  9. Platz, `quelle`, `durch` und Reservierung bleiben. Verlorene Senderechte vermerkt der Server wie bei jeder Umstellung (`ZIEL_ENTZOGEN`, 6.3).
  10. Die laufende Phase steht im Lagezustand mit Nummer, Zeit und Urheber.
  11. `PhaseGeschaltet` an alle AKTIV-Sitzungen, `Mitgliedschaften` mit `phase` an die Umgestellten, ein Praesenz-Delta.
  12. Rückfall auf die Grundstellung 12 h nach dem letzten Schalten im Fristen-Takt, mit denselben Folgen (`durch` 0). "Abend beenden" schaltet im Client zuerst auf 1.
  13. Konten ohne Sitzung mit Platz werden still nachgeführt; beim Anmelden stimmen ihre Felder.
- Jeder Weg zum Platz nimmt die laufende Phase (6.9). Sonderregel für `SatzAnwenden` mit `rolleId` ("Wie letztes Mal" schickt die Felder eines früheren Abends): Hat die laufende Phase eine Variante für die Rolle, baut der Server die Rollenfelder aus der Phase (um feste Felder herum) und die Sendeauswahl aus ihrem Sendevorschlag; aus dem Satz gelten nur `hoeren` für Netze, die darin vorkommen, und offene Netze als Zusatznetze, auf ihrem Feld oder dem nächsten freien. Was nicht passt, steht in `uebersprungen`.
- Rückweg: "Phase zurück" rechnet aus denselben Regeln und ist fast genau. Ein "Hören aus" auf einem Netz, das der verlassenen Phase fehlte, kommt als "an" zurück, und ein Zusatznetz, das zugleich ein Netz der verlassenen Phase war, fällt weg.
- Bedienung im Client (zweistufig, Phasenleiste, Meldezeile) steht im Entwurf zur Ausbaustufe S2; der Server kennt keine Vorschau.

### 6.15 "Heute als Funker" (Ausbaustufe S2, seit 0.5.0)

Ein ADMIN, der einen Abend nur funken will, während ein anderer leitet, schaltet am eigenen Konto "Heute als Funker" ein (0x003C, Entwurf zur Ausbaustufe S2). Eine zweite Anmeldung gibt es dafür nicht.

- Wer: nur ein aktives Konto der Stufe ADMIN, nur für sich selbst, ohne Admin-Sitzung und ohne Code (der Modus senkt eigene Rechte). Niemand setzt einen anderen in den Modus, verlängert oder beendet ihn.
- Rechte: Solange der Modus läuft (`alsFunkerBis` in der Zukunft), fehlen die Rechte der Einsatzleitung (Führung und `ZUWEISEN`, 6.1), je Aktion und je Sprachpaket, auch bei den Empfängern von Pushes (Praesenz-Details, Meldungen an `ZUWEISEN`, Sprechanzeige). Kein Global, keine Durchsage, kein Mehrfachsenden, kein Brett. Die Sendeauswahl verliert Global und Mehrfachziele. Die Verwaltungsrechte bleiben und wirken nur in der Admin-Sitzung.
- Einteilen: Die Rangregel (6.10) lässt `ZUWEISEN` auf das Konto wirken; Stummschalten bleibt beim ADMIN. ERSETZEN trifft das Konto, denn es hat kein `ZUWEISEN`.
- Dauer: bis zum nächsten 04:00 Uhr Europe/Berlin, höchstens 24 h. Ende mit `an: false`, mit dem Ablauf (die Rechte gelten schon ab `bis`, der Fristen-Takt schickt die Pushes und schreibt das Journal), mit einem Stufenwechsel und mit einer Sperre des Kontos.
- Regel 7 aus 6.12 gilt beim Einschalten: Eine Sperre der Selbstwahl, die das Konto gesetzt hat, und das Nachziehen seiner zuletzt angewandten Aufstellung enden; sie kommen beim Ausschalten nicht zurück.
- Ort: `alsFunkerBis` am Lage-Konto, Abendzustand wie ein Auftrag. Er übersteht ein Neuverbinden und einen Neustart; `funk-admin sicherung` lässt ihn weg.
- Sichtbar: Journal (`FUNKER_MODUS_EIN` "... spielt als Funker bis 02.10. 04:00", `FUNKER_MODUS_AUS` "... wieder als Admin", bei Ablauf, Stufenwechsel oder Sperre mit dem Grund), `konto.alsFunkerBis` in Welcome und Mitgliedschaften, `alsFunker` in Praesenz-Details und KontenVerzeichnis. Beim Beginn bekommt die Sitzung einen vollen Praesenz-Stand ohne Details, beim Ende mit, und ein Sprechanzeige-Abo endet (wie 6.12 Regel 9).

### 6.16 Kanäle mit Ch. (Stufe 3, seit 0.6.0)

Wie im früheren Funkprogramm wählt jeder Funker je Feld einen Ch. (Entwurf zu Stufe 3, Variante C, heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Funkfeld"). Statt 30 Kanälen mal 20 Ch. als 600 Netze gibt es Kanalnetze, deren Kanäle eine berechnete Kanal-ID haben: Nichts wird bei Bedarf angelegt oder freigegeben, die NetzListe bleibt klein (Vorlage Star Citizen: 10 Kanalnetze), und ein Ch.-Wechsel ist ein gewöhnlicher Commit.

- **Kanalnetz:** ein Netz mit `kanaele`, einer Liste von 2 bis 31 Ch.-Bezeichnungen (je höchstens 20 Zeichen, nicht leer). Index 0 ist die Ruhe, Index 1 bis 30 sind Kanäle; die Listen hängen am Kanaltyp (etwa "Schiff 1" bis "Schiff 12", "Staffel 1" bis "Staffel 4", "Fracht", "Betankung", "Reparatur" in der Vorlage Star Citizen). Höchstens 64 Kanalnetze, Netz-ID 1 bis 1022.
- **Kanal-ID:** `0x8000 + n x 32 + c` mit der Netz-ID n des Kanalnetzes und dem Ch. c (0 bis 30); zurück `n = (id - 0x8000) >> 5`, `c = id & 31`. Höchste 0x8000 + 1022 x 32 + 30 = 65502. IDs ab 0x8000 sind den Kanälen vorbehalten: `NETZ_SETZEN` mit einer solchen ID ergibt `UNGUELTIG`. Eine Kanal-ID ist gültig, wenn ihr Kanalnetz existiert, c kleiner als die Länge seiner Liste ist und sie nicht Orgaweit (n = 1, c = 1) ist; c = 31 und n = 0 sind nie gültig.
- **Ruhe (Ch. 0), ab Werk:** Das Feld trägt das Kanalnetz, hört und sendet aber nichts, unabhängig von `hoeren` und `senden`; diese beiden gelten erst, wenn ein Ch. gewählt ist. Der Server leitet nichts in eine Ruhe weiter und nimmt keine Ruhe in die Sendeauswahl. Je Konto steht die Ruhe eines Kanalnetzes wie jeder Kanal in höchstens einem Feld.
- **Global als Kanalnetz:** Ch. 1 heißt Orgaweit und ist Netz 1 selbst (6.5), alle hören es, das feste Global-Feld bleibt darauf; die übrigen Ch. sind gewöhnliche Kanäle. So bleibt das heutige Global für alte Wege (Durchsage, Stummschalten mit 1, `Mitgliedschaften.global`) unverändert.
- **Rechte:** `offen`, `nurTaste`, `ausloeserStandard` und `standard` gelten je Kanalnetz für alle seine Ch. Eine Stummschaltung kann einen Kanal oder das ganze Kanalnetz treffen (4.5 Stummschalten); die von Netz 1 trifft so Orgaweit und alle Global-Kanäle. Ausnahme: `nurTaste` von Global gilt nur für Orgaweit, die Global-Kanäle erlauben Sprachaktivierung (6.5, Annahme). Für die Vorlage Star Citizen gilt: alle Kanalnetze `HOEREN_SENDEN`, alle mit Sprachaktivierung, auch Kommando.
- **FeldSetzen** (0x0035, Regeln dort): Der Ch.-Wechsel im selben Kanalnetz ist immer erlaubt, seit 0.8.1 auch im festen Feld, nur nicht in Feldern mit einem gesperrten Kanal (6.17); einen anderen Kanal legt man nicht selbst in ein belegtes Feld, in ein freies nur ein offenes Netz.
- **Gewählte Rolle und RolleWaehlen** (0x0039): Eine Rolle ist, was das frühere Funkprogramm Preset nannte. `RolleWaehlen` legt nur Felder an und merkt die Rolle als gewählte Rolle (`Mitgliedschaften.rolle`); Platz, Reservierung und Brett bleiben, die Sperre der Selbstwahl gilt nicht. Die gewählte Rolle entfällt, sobald die Felder wieder aus einer anderen Vorgabe kommen: Einsteigen, Verschieben mit Rolle, Schiff oder `felder`, Aufstellung, Nachziehen, `SatzAnwenden` mit Rolle, STANDARD (auch beim Anmelden nach Ablauf der Wiederverbindungsfrist, 6.9; der Client stellt sie dann mit `RolleWaehlen` wieder her, seit 0.8.1 ohne gemerkte Ch.), oder wenn die Rolle gelöscht wird. Felder, die man danach selbst ändert (`FeldSetzen`, `HoerenSetzen`, `felderPatch`), lassen sie stehen. Die Phase (6.14) richtet sich nach der gewählten Rolle, sonst nach der Rolle des Platzes; eine Deckung durch die Rolle gibt es in `FeldSetzen` und `SatzAnwenden` seit 0.8.1 nicht mehr.
- **Ch.-Auflösung** für jedes Rollenfeld mit einem Kanalnetz, bei jedem Weg, der Felder aus einer Rolle bildet (Einsteigen, Verschieben mit Rolle oder Schiff, Aufstellung, Nachziehen, Phase, "Wie letztes Mal", RolleWaehlen), in dieser Folge: (1) das feste `ch` des Rollenfelds; (2) die Einheit des Platzes nennt eine Kanal-ID dieses Kanalnetzes (die erste nach Kennung); (3) der Ch., den das Konto in diesem Kanalnetz gerade hat (das Feld mit derselben Nummer zuerst, sonst das niedrigste); (4) der Wunsch aus `RolleWaehlen.ch` für dieses Feld; (5) Ruhe. Eine Schiffsnetz-Kennung, die auf eine Kanal-ID zeigt, gibt den Kanal unmittelbar. Bleibt das Konto im Kanalnetz, behält es so seinen Ch., auch über Phasen.
- **Weiterleitung** (6.3, 6.4): Kanal-IDs stehen im Schnappschuss wie Netze, der Index Netz -> Hörer nimmt jeden Kanal auf, den eine aktive Sitzung in einem Feld hat (ohne Ruhe). Gestempelt werden die Kanal-IDs; der Client ordnet sie über seine Felder zu.
- **Verwaltung** (4.5 Verwaltung): Ein gewöhnliches Netz, das Kanalnetz wird, legt seine Felder auf Ruhe. `kanaele` kürzen oder entfernen und `NETZ_LOESCHEN` eines Kanalnetzes räumen die betroffenen Kanal-IDs aus Konten, Stummschaltungen, Einheitskennungen, Rollen und Phasen. Gespeicherte Aufstellungen, Sätze und `letzterStand` behalten ihre Kanal-IDs; beim Anwenden scheitern die ungültigen wie ein gelöschtes Netz.
- **Vorlage "Star Citizen"** (`Flottenvorlagen.StarCitizen`, seit 0.13.0; Sarevoc 19.10.2026, Q13, Q18, Q26, `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Umbenennung zu JG TacRelay"): 10 Kanalnetze mit ihren Ch.-Listen (Index 0 Ruhe): 1 Kommando (Flottenführung, Stab), 2 Flotte (Allgemein), 3 Brücke (Schiff 1 bis 12, Alle), 4 Crew (Schiff 1 bis 12), 5 Jäger (Staffel 1 bis 4, Alle), 6 Bodentrupp (Trupp 1 bis 4, Alle), 7 Logistik (Fracht, Betankung, Reparatur), 8 Bergung und Bergbau (Bergung, Bergbau), 9 Sanität (Rettung), 10 Allgemein (Treffpunkt, Reserve 1 bis 3); alle `HOEREN_SENDEN`, alle mit Sprachaktivierung, auch Kommando, ohne Gruppe; Global bleibt, wie es ist. 14 Rollen "Standard (Gast)", "Flottenführung", "Kapitän", "Pilot", "Gunner", "Ingenieur", "Staffelführer", "Jägerpilot", "Truppführer", "Bodentrupp", "Sanitäter", "Logistik", "Bergung und Bergbau" und "Funker (Leitstand)" mit ihren Feldern 1 bis 9 ohne Global (das feste Feld), alle hören und senden, Sendevorschlag das erste Feld, selbst wählbar, ohne Einheit. "Schiff n", "Staffel n" und "Trupp n" stehen ohne festen Ch. im Rollenfeld (ab Werk Ruhe, den Ch. wählt der Funker oder die Leitung), "alle" fest auf dem Ch. Alle am Ende der Liste, Kanäle ohne Nummer fest auf ihrem ersten Ch.; keine Einheiten. 24 Schritte in einem Paket, Netz-IDs frei ab 100 (bis 1022, ohne fremdes Netz im Bereich ihrer Kanal-IDs), Rollen-IDs frei ab 101; die alte Flotte bleibt. Übernehmen im Verwaltungsteil (Admin-Sitzung, frischer Code, Rückfrage mit dem Versionstor) oder an der Konsole (`funk-admin flotte-vorlage [--name starcitizen]`), beides nur, solange es außer Global kein Kanalnetz gibt. Die sechs Annahmen (Ch. n, Alle, erster Ch., offen, Global, Einheiten) stehen in der Vorlage und gelb im Verwaltungsteil; der Admin prüft sie. Von 0.6.0 bis 0.12.0 gab es stattdessen die alte Kanalvorlage (Global als Kanalnetz, 30 Kanalnetze, 21 Rollen, 52 Schritte); seit dem Umbau zu JG TacRelay gibt es sie nicht mehr.
- **Last:** Jeder Ch.-Wechsel ist ein Commit mit einem Schreiben von `lage.json`. Mehrere Anfragen eines Durchlaufs der Zentrale schreibt der Gruppen-Commit einmal (6.2); je Sitzung gilt 5/s, Burst 10 (4.8), und der Client sendet nach dem Mausrad erst nach 400 ms Ruhe. Messwerte in 4.10.
- **Übergang:** Ein Zustand von 0.5.0 lädt unverändert (keine Kanalnetze, kein Tor). Rückweg: alle Kanalnetze löschen und Global ohne Ch. setzen (räumt jede Kanal-ID), dann fällt das Versionstor (4.7), und ein Server 0.5.0 startet wieder auf dem Zustand; zweite Linie ist die Sicherung von `lage.json` vor der Übernahme.

### 6.17 Kanalsperren (Stufe 3, seit 0.6.0, Schritt 3.2)

Die Einsatzleitung (`ZUWEISEN` aus Stufe, Einzelrecht oder Auftrag) oder ein Admin sperrt einen Kanal, ein ganzes Kanalnetz oder ein gewöhnliches Netz für ein Konto oder für alle ohne `ZUWEISEN` (Sarevocs Antworten Q8 und Q9, Entwurf zu Stufe 3, heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Funkfeld"). Anfrage 0x003A `KanalSperre` (4.5).

- **Gesperrt heißt:** Das Konto hört den Kanal nicht (die Weiterleitung lässt es je Paket aus; wer mitten in einer Sprechphase gesperrt wird, bekommt ein erzeugtes ENDE wie in 6.8), sein Senden dorthin wird mit `KEIN_SENDERECHT` und `durch` (dem Setzenden) gestrichen, eine Durchsage an diesen Kanal erreicht es nicht, und es legt den Kanal nicht selbst in ein Feld (`FeldSetzen`, `SatzAnwenden`). Ein Feld, das den Kanal schon hält, bleibt stehen (rot im Client): kein Ch.-Wechsel und kein Leeren darin (`KEIN_RECHT` mit `details: {grund: "GESPERRT", durch, bis}`). Ein gesperrter Kanal steht nie in der Sendeauswahl; die Sperre nimmt ihn heraus (C4), `SendeauswahlSetzen` lehnt ihn ab. `hoeren` und `senden` der Mitgliedschaft bleiben, wie sie sind, und gelten wieder, sobald die Sperre endet; `HoerenSetzen` geht, wirkt aber erst dann. Global (Orgaweit) und die Durchsage an alle erreichen das Konto weiter. Eine Ruhe hört und sendet ohnehin nicht und ist nie gesperrt.
- **Rollen, Phasen, Einteilung:** Bilden Rolle, Phase, Aufstellung oder die Einsatzleitung die Felder, liegt ein gesperrter Kanal trotzdem im Feld (rot), so sieht der Funker, was fehlt. `RolleWaehlen` nimmt dann als Sendevorschlag das erste sendefähige Feld ohne Sperre.
- **Ziele:** eine Kanal-ID (ein Ch.), ein Kanalnetz (alle Ch., auch später angelegte Felder) oder ein gewöhnliches Netz. Global (Netz 1 mit allen Global-Kanälen) und Orgaweit nie, dafür bleibt Stummschalten; die einzelnen Global-Kanäle (Ch. 2 und höher) schon.
- **Für alle** (`kontoId` 0): trifft jedes Konto ohne wirksames `ZUWEISEN` zur Zeit des Pakets bzw. der Anfrage, ein ADMIN-Konto (im Funker-Modus) nur, wenn ein ADMIN gesetzt hat. Beginnt ein Auftrag, ist der Beauftragte sofort ausgenommen.
- **Dauer:** bis `bis`, ohne Angabe bis zum nächsten 04:00 Europe/Berlin, höchstens 24 h. Wirkung endet auf die Millisekunde; der Fristen-Takt (1 s) räumt ab, schreibt das Journal (`KANAL_FREI`, abgelaufen) und schickt `Mitgliedschaften`.
- **Ende mit dem Auftrag:** Hat der Setzende kein `ZUWEISEN` mehr (Auftrag entzogen, abgegeben, abgelaufen oder beendet, Funker-Modus begonnen, Konto gelöscht), enden seine Sperren, im selben Commit oder spätestens mit dem nächsten Takt (Regel 7 aus 6.12, wie die Sperre der Selbstwahl).
- **Rang:** wie Stummschalten (6.10): Ein ADMIN-Konto sperrt nur ein ADMIN; aufheben darf, wer gesetzt hat, ein Konto höherer Stufe als der Setzende oder ein ADMIN, nie der Gesperrte selbst eine fremde Sperre.
- **Verwaltung:** Kürzt `NETZ_SETZEN` die Ch.-Liste oder löscht `NETZ_LOESCHEN` ein Netz oder Kanalnetz, enden die Sperren darauf (Meldung BEENDET, NETZ). Wird ein gewöhnliches Netz zum Kanalnetz, gilt eine Sperre darauf für alle seine Ch.
- **Grenzen:** je Konto 16, für alle 64, zusammen 500; 10/s, Burst 20 (4.8).
- **Sichtbar:** Journal (`KANAL_GESPERRT`, `KANAL_FREI` mit Kanal, Klarnamen und Zeiten in Ortszeit), `Meldung{KANALSPERRE}` an `ZUWEISEN`, `Mitgliedschaften.gesperrt` an die Betroffenen (eigene und für alle), `gesperrt` in den Praesenz-Details (eigene), LISTE für die Einsatzleitung.
- **Ort:** am Konto im Lagezustand (`lage.json`, `konten[].sperren`), die für alle im Schnappschuss (`sperrenAlle`), denn die Weiterleitung fragt sie je Paket; beide überstehen einen Neustart. `funk-admin sicherung` lässt sie weg wie Aufträge: Eine aufgehobene Sperre soll nicht aus einem alten Stand zurückkommen.
- Der Index Netz -> Hörer (6.2) bleibt ohne Sperren; die Weiterleitung prüft sie je Paket, weil Ende und Ausnahme der Einsatzleitung von der Zeit abhängen.

### 6.18 Meldesystem (seit 0.12.0)

Grafische Meldungen über dem Spiel an alle, die online sind (Sarevoc 11.10. und 16.10.2026, Entscheidungen M1-M24, heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Overlay und Meldungen"): Befehle, Zustände, eine Ampel, ein Countdown, eine Textmeldung ohne Grafik und eine Bereit-Abfrage. Anfragen 0x0026 `MeldungSenden` und 0x0027 `BereitMelden`, Pushes 0x4005 `Meldestand` und 0x4006 `BereitStand` (4.5).

- **Recht** (M1, M15-M18): Senden darf, wer den Haken "darf Meldungen senden" hat UND Stufe RAIDLEITER ("Führung") oder ADMIN ist oder als Einsatzleitung einen laufenden Auftrag hat (6.12); wer "Heute als Funker" spielt (6.15), sendet nicht. Den Haken setzt und nimmt nur ein Admin in der Admin-Sitzung (`Mitglied AENDERN` mit `meldungenSenden`, `KONTEN_VERWALTEN`, Journal `MELDUNGEN`); er bleibt, bis ein Admin ihn wegnimmt, und steht bei einem Funker weiter, wirkt dort aber nicht. Es gibt kein neues Recht in `Recht` (4.7): Der Haken ist ein Mitgliedsfeld in `mitglieder.json` wie die Abzeichen. Der Client rechnet nach derselben Regel aus `konto.meldungenSenden`, Stufe, `beauftragung.aktiv` und `alsFunkerBis`, ob er das Feld "Meldungen" zeigt; entscheidend ist der Server.
- **Ziel:** immer alle aktiven Sitzungen (M2), auch der Sender; keine Auswahl nach Netz.
- **Arten** (M3-M14, M19, M22): Ein Befehl (8 s groß beim Funker) ersetzt den vorigen, gleich von wem, und bleibt nicht stehen. Ein Zustand (klein) bleibt, bis er aufgehoben oder durch einen anderen ersetzt wird. Ampel (rot, gelb, grün, aus) und Countdown haben je einen eigenen Platz; die Ampel schaltet nur von Hand (keine Startfolge, M22). Der Countdown zählt auf eine Zielzeit in Serverzeit (Form "Start um" mit Uhrzeit und Rest oder "Restzeit"); die Zeiten blendet der Client in die freien Felder der Grafik, bei 0 verschwindet er, und der Server nimmt ihn still aus seinem Stand. Größe 1-3 (normal, doppelt, dreifach, M21) für Befehl, Ampel, Countdown und Bereit-Abfrage; ein Zustand bleibt immer klein. Dazu eine freiwillige Zeile bis 60 Zeichen.
- **Textmeldung** (M24, Sarevoc 16.10.2026 abends): nur Text, ohne Grafik, bis 120 Zeichen, in einer festen Farbe (Weiß, Gelb, Orange, Rot, Grün, Blau; Kennungen `weiss` ... `blau`), Größe 1-3, Dauer `8s` (ab Werk) oder `bleibt` (bis aufgehoben oder durch eine neue Textmeldung ersetzt, gleich von wem). Recht und Rate wie bei allen Meldungen. Eine stehende gehört zum laufenden Stand und kommt nach Welcome, eine mit 8 s nicht. Wie sie aussieht (Farbton, Schrift, höchstens zwei Zeilen, Platz unter dem Befehlsplatz, Ton wie bei Befehlen), legt der Client fest.
- **Bereit-Abfrage:** Die Leitung startet sie (neue Nummer, eine laufende wird ersetzt); jeder Funker meldet sich mit `BereitMelden` (Taste oder Knopf). `BereitStand` geht nur an die, die senden dürfen: "n von m bereit" mit m = aktive Sitzungen ohne den Startenden und den fehlenden Konten. Sie endet durch die Leitung (`BereitSchliessen`) oder nach 2 min von selbst (`Meldestand{anlass: BereitAbgelaufen}` an alle, letzter `BereitStand` mit `offen: false`). Wer die Abfrage startet, zählt nicht mit: weder in m noch unter den fehlenden Konten.
- **Katalog:** die Motive mit Kennung, Art, Anzeigename und Ausführungen (Schlicht ab Werk, Metall, Nur Schrift) in `meldungen.json`, fest im Programm wie der Abzeichenkatalog; Server und Client lesen dieselbe Datei. Die Grafiken sind Sarevocs eigene (mit ChatGPT erstellt) und kommen verkleinert mit dem Client; welche Ausführung und welches Stopp-Bild (Handschuh oder Xeno) jemand sieht, wählt jeder Funker selbst, fehlt ein Motiv in der Ausführung, zeigt der Client Schlicht.
- **Spät Kommende** (M20): Nach Welcome bekommt jede Sitzung den laufenden Stand (Zustand, Ampel, Countdown, offene Abfrage, stehende Textmeldung) als `Meldestand{anlass: Stand}`, nie vergangene Befehle und keine Textmeldung mit 8 s.
- **Rate** (M19): je Sender höchstens eine Meldung in 2 s (4.8), sonst `GRENZE` ohne Trennung.
- **Uhr:** Zeiten sind Serverzeit (Unix-ms, wie `PingAntwort.serverZeit`). Der Client gleicht seine Uhr laufend über Ping ab, wie NTP mit Laufzeitausgleich (von den letzten 8 Proben die mit der kürzesten Laufzeit, Fehler höchstens die halbe Laufzeit, Ziel etwa 0,1 s); vor der ersten Ping-Antwort grob aus `Welcome.serverZeit`. Am Server ändert sich dafür nichts.
- **Ort:** Der laufende Stand lebt nur im Speicher der Zentrale; nach einem Neustart ist er leer. Der Haken steht in `mitglieder.json`.

## 7. Robustheit

### 7.1 Wiederverbinden, auch nach IP-Wechsel des Servers (DynDNS)

- Erkennung: 6 s ohne Ping-Antwort, ein TCP-Fehler, ein TLS-Fehler oder `Bye`.
- Der Client schließt die alte Verbindung, bevor er eine neue aufbaut.
- Schnellphase: Nach dem Verlust einer Sitzung, die mindestens 30 s AKTIV war, 2 min lang alle 2 s (+/-20 %) ein Versuch, jeder mit neuer Namensauflösung. Danach Backoff 4 / 8 / 15 / 15 ... s, jeweils +/-20 % Zufall. Nach `Bye{WARTUNG, wiederInMs}` wartet der Client `wiederInMs` plus 0-2 s Zufall und beginnt dann die Schnellphase. Die Vorrangspur (4.8) trägt die Versuche an den globalen Töpfen vorbei.
- Ablauf je Versuch:
  1. Alle Hosts des Profils neu auflösen, nur die im Profil erlaubten Familien (Heimphase nur IPv4). Hat der Client inzwischen ein neueres Profil übernommen (3.1), gelten ab diesem Versuch seine Hosts, sein Port und seine Pins.
  2. Verbindungsaufbau nach RFC 8305: Adressen um 250 ms versetzt parallel versuchen, die erste erfolgreiche TCP-Verbindung gewinnt, je Adresse 3 s.
  3. Scheitert die Auflösung, die letzte erfolgreiche Adresse versuchen.
  4. TLS mit Pin. Ein Pin-Fehler beim Wiederverbinden gilt zuerst als "falscher Host", denn die alte IP kann inzwischen einem anderen Telekom-Kunden gehören: nächste Adresse, diese Adresse 60 s meiden, keine Alarmanzeige. Erst wenn 2 min lang an keiner Adresse ein gültiger Pin kam, zeigt der Client "Server nicht vertrauenswürdig" (ohne Ausnahmeknopf).
  5. Hello, neue Sitzung, neue Schlüssel.
- Autoritative Auflösung (KANN, nach Spike): Nennt das Profil `nameserver`, fragt der Client in der Schnellphase die A-Einträge direkt dort ab, mit einem kleinen eigenen DNS-Client über UDP. Die Antwort ist nur ein Hinweis, den Server beweist der Pin. Scheitert das (etwa weil ein Netz DNS nach außen sperrt), nimmt er den Weg über das Betriebssystem. Nur so wirkt eine kurze TTL auch an den Zwischenspeichern von FRITZ!Box und Provider vorbei; `DnsQuery_W` mit `DNS_QUERY_BYPASS_CACHE` umgeht nur den Windows-Cache.
- DynDNS: In der Heimphase nur ein A-Eintrag (kein AAAA, sonst meldet die FRITZ!Box ihre eigene IPv6-Adresse statt der des LXC), TTL höchstens 60 s.
- Der Server hält Plätze und Mitgliedschaften am Konto, nicht an der Sitzung (6.9). Nach der Wiederverbindung kommt `Mitgliedschaften{quelle: "WIEDERHERGESTELLT"}` mit denselben Feldern und derselben Sendeauswahl; hält eine Reservierung der Einsatzleitung den Stand, seit 0.11.0 mit deren Herkunft (VERSCHIEBEN oder AUFSTELLUNG mit `durch`, 6.9), nach einer eigenen Änderung mit der eigenen Quelle. Pegel und Ablage liegen ohnehin am Konto.
- UDP nutzt die IP-Adresse der neuen TLS-Verbindung (5.1), so gibt es kein zweites DNS-Rennen.
- Zielwerte: nach einem Serverneustart höchstens 10 s; nach einem IP-Wechsel höchstens 20 s ab dem Zeitpunkt, an dem der autoritative DNS-Eintrag die neue Adresse zeigt (mit autoritativer Auflösung), sonst höchstens TTL + 20 s. Die DSL-Neusynchronisierung und die DynDNS-Aktualisierung durch Router oder Updater kommen davor und liegen außerhalb dieses Protokolls.
- Der Client zeigt den Zustand deutlich an ("Verbindung verloren, neuer Versuch in 2 s").

### 7.2 Serverneustart

- Dauerhaft gespeichert sind Konten, Geräte, Netze, Schiffe, Rollen, Einstellungen, Aufstellungen samt zuletzt angewandter, Regeltabelle, Plätze mit Zustand und Fristen, Mitgliedschaften, Stummschaltungen, Pegel, Ablage, Sendeauswahl und die Vorrangspur (als HMAC-Werte). Das Speichern geschieht vor dem Umschalten (6.2, 6.7).
- Flüchtig sind Sitzungen, Schlüssel, Zähler, Replay-Fenster, Phasenbuch, laufende Durchsagen und Einmalcodes. Einmalcodes dürfen auch gespeichert werden, dann aber nur als Hashwert.
- Ein geordneter Neustart sendet `Bye{WARTUNG, wiederInMs}` an alle. Bei einem harten Neustart erkennen die Clients den Ausfall nach höchstens 6 s. Die Wiederverbindung läuft nach 7.1; die Zufallsstreuung verhindert, dass alle gleichzeitig anstürmen, und die Vorrangspur hält die globalen Töpfe für Mitglieder frei, auch während einer Handshake-Flut (P-24).

### 7.3 Paketverlust, Reihenfolge, Jitter

- UDP wird nicht neu gesendet. Verluste verdeckt der Empfänger mit FEC aus dem Folgepaket, danach Deep PLC (codec-ausgabe.md B16, B21), höchstens 60 ms am Stück (C6). Der Verlusthinweis zum Sender läuft über EmpfangsBericht und VerlustHinweis.
- Umordnung: Das Replay-Fenster nimmt Pakete bis 2048 zurück an. Der Server leitet in Ankunftsreihenfolge weiter und ordnet nicht um. Der Jitterpuffer ordnet nach Frame-Nr innerhalb der Sprechphase.
- Doppelte Pakete verwirft das Replay-Fenster.
- ENDE wird dreifach gesendet (5.6); fällt ein Empfänger heraus, erzeugt der Server ein ENDE (6.4). Ohne ENDE gilt C6.
- Der Empfänger schützt sich gegen einen böswilligen Sender: Ein Sprung der Frame-Nr um mehr als 3000 (60 s) innerhalb einer Sprechphase zählt als neue Sprechphase. Frames hinter dem Abspielpunkt werden verworfen.
- Die Reihenfolge im Steuerkanal sichert TCP. Das Rennen zwischen Mitgliedschaften-Push und Sprachpaketen behandelt C8.

### 7.4 Überlast und Ausgangsbudget

- Zu Hause liegt der Engpass in der Upload-Warteschlange von Router und DSL-Modem, nicht im Server. Der Server gibt über die Gigabit-Karte sonst alles sofort ab, seine eigenen Warteschlangen blieben leer und seine Rangfolge wirkungslos. Deshalb sendet er über einen Pacer:
  - Token-Bucket auf Leitungsbytes je Paket: UDP-Nutzlast + 28 (IPv4) bzw. 48 (IPv6) + `leitungsZuschlag` (Standard 26 Byte für PPPoE und Ethernet). Tunnelrahmen zählen mit Rahmenlänge plus 60 Byte.
  - Rate `ausgangMbit`: höchstens 80 % des gemessenen Netto-Uploads, Standard 35 Mbit/s in der Heimphase (50 Mbit/s brutto). Burst höchstens 5 ms Leitungszeit.
  - Beispielrechnung bei 48 kbit/s mit FEC: ca. 140 B Opus + 47 B Kopf, Stempel und Tag + 28 IP/UDP + 26 Zuschlag = ca. 240 B, also ca. 96 kbit/s je Strom. 35 Mbit/s reichen für ca. 360 Ströme; Szenario B braucht ca. 180.
  - Erst damit ist der Server die Engstelle und seine Rangfolge wirkt.
- Rangfolge im Pacer: (1) PFAD_PRUEFUNG und KEEPALIVE_ECHO, (2) Sprache mit Art GLOBAL oder DURCHSAGE, (3) Sprache NETZ. Steuerrahmen laufen über TCP und haben in der Schreibschleife Vorrang (4.2).
- Jedes Paket trägt den Einreihzeitpunkt; ein Paket, das älter als 20 ms in einer Sendewarteschlange liegt, wird verworfen, nicht verspätet gesendet.
- Überlaststufe 1 (Pacer-Wartezeit im Median über 100 ms größer als 5 ms, oder Altersverwürfe): Strom-Abwurf statt Paketverlust. Je Empfänger werden ganze NETZ-Ströme (Absender, Sprechphase) nicht mehr zugestellt, in fester Rangfolge: zuerst Netze, in denen der Empfänger kein Senderecht hat, darunter zuerst das mit der höchsten Feldnummer; nie GLOBAL oder DURCHSAGE. Ein abgeworfener Strom bleibt bis zum Ende seiner Sprechphase abgeworfen; neue Phasen werden erst wieder zugestellt, wenn die Überlast 2 s vorbei ist. Betroffene Hörer bekommen `Meldung{EMPFANG_GEDROSSELT, netzId}`. Serververwerfungen gehen nicht in den VerlustHinweis ein.
- Überlaststufe 2 (Verarbeitungszeit länger als 1 s über 10 ms): keine neuen Registrierungen und keine neuen unfreigegebenen Verbindungen (`SERVER_VOLL`). Freigegebene Geräte werden immer angenommen (bis 100 Sitzungen), damit gerade die zurückkommen, die bei einer Störung kurz getrennt wurden. `ServerLast{stufe}` geht an Raidleiter und Admins.
- Den Anteil eines einzelnen Senders begrenzen Paket- und Byte-Topf je Sitzung (6.3). Ein Troll mit 39 Hörern kommt so auf höchstens etwa 6-7 Mbit/s.
- Die Zähler (verworfen je Grund, Verarbeitungszeit p50/p99 ab Empfangsstempel, Pacer-Wartezeit, Ausgangsrate, GC-Pausen) stehen im Log und über das Admin-Werkzeug bereit.

### 7.5 Missbrauchsschutz

- Token-Buckets je Präfix (Handshakes, UDP-Vorfilter), je Sitzung (Steuerrahmen, Sprache nach Paketen und Bytes, Entschlüsselungsbudget getrennt nach gebundener Adresse, fremden Adressen und Tunnel), je Discord-ID (Verknüpfen) und global (Handshakes als CPU-Schutz, Ausgangsbudget). Werte in 4.8, 5.9 und 5.10.
- Keine Verstärkung:

| Auslöser | Antwort des Servers | Verhältnis |
| --- | --- | --- |
| beliebiges TCP-Segment | TCP-Handshake, danach TLS; Adresse durch TCP bestätigt | - |
| ungültiges oder nicht authentisiertes Datagramm | nichts | 0 |
| gültiges Datagramm mit neuem Höchstwert von fremder Adresse | höchstens eine PFAD_PRUEFUNG an diese Adresse (48 Byte) je auslösendem Paket, höchstens 4/s, dazu eine Gegenprobe an die schon geprüfte Adresse | <= 1 |
| gültiges KEEPALIVE von gebundener Adresse | KEEPALIVE_ECHO, 48 Byte | 1 |
| Sprache | nur an gebundene Adressen anderer Sitzungen, die ihre Erreichbarkeit bewiesen haben | - |

- Jeder Parser prüft Längen vor jeder Allokation. Kein Feld aus dem Netz bestimmt eine Puffergröße über die festen Grenzen hinaus.
- Logs enthalten nie Schlüssel, IVs, Codes, Tokens oder Opus. IP-Adressen und Konto-IDs werden per HMAC pseudonymisiert (`Microsoft.Extensions.Compliance.Redaction`, server-sicherheit.md B15); der HMAC-Schlüssel kommt per `LoadCredential`. Im HttpClient-Logging (Raid-Helper) ist die Header-Redaction ausdrücklich eingeschaltet.
- Speicherabbilder: Am Server `LimitCORE=0` und `DOTNET_DbgEnableMiniDump=0`, damit weder systemd-coredump noch createdump Sitzungsschlüssel oder den Identitätsschlüssel ablegen. Am Client schaltet das Programm keine Windows-Fehlerberichte ab; ein WER-Abbild kann die kurzlebigen Sitzungsschlüssel enthalten, der Geräteschlüssel liegt im TPM bzw. nach Kenntnisstand im isolierten Schlüsseldienst (Offener Punkt). Das ist hingenommen.
- Die Sicherung schließt `$CREDENTIALS_DIRECTORY` aus.

### 7.6 Heimbetrieb (Proxmox-LXC, Telekom-DSL)

- Am Router gibt es nur zwei Weiterleitungen zum LXC: TCP (öffentlich 443 auf den Bindport) und UDP (öffentlich 47443). SSH bleibt im LAN. Port 80 wird nicht gebraucht (3.1).
- Der LXC bekommt eine feste IPv4 (DHCP-Reservierung). Der Server bindet sie ausdrücklich, TCP und UDP mit gleicher IP (4.1). IPv6 unterstützt das Protokoll; im Heimbetrieb wird es erst nach Klärung der Router-Freigabe eingeschaltet, und dann bindet der Server an die Schnittstelle oder bindet bei einer Präfixänderung neu, denn das Telekom-Präfix wechselt.
- In der FRITZ!Box den LXC als Echtzeitanwendung priorisieren und die automatische Neuverbindung auf eine Uhrzeit ohne Einsätze legen. Den Netto-Upload messen und `ausgangMbit` danach setzen. Der DynDNS-Updater protokolliert Adresswechsel.
- systemd-Härtung nach server-sicherheit.md 3) und B13, ohne `MemoryDenyWriteExecute`, dazu `LimitCORE=0` und `DOTNET_DbgEnableMiniDump=0`. Der Bot-Dienst ist getrennt und hat einen eigenen Benutzer (3.8).
- Die Heim-IP sehen alle Mitglieder, und über DynDNS jeder, der den Namen kennt. Ein verärgertes Ex-Mitglied kann den DSL-Anschluss während eines Einsatzes volumetrisch lahmlegen, das Protokoll kann dagegen nichts tun. Vor dem produktiven Einsatz empfiehlt sich der Umzug zum Anbieter oder ein kleiner Vorposten bei einem Anbieter (Offener Punkt).
- Ein Vorposten (VPS mit WireGuard-Tunnel zum LXC) MUSS die Absenderadressen erhalten: nur DNAT, Rückweg per Policy-Routing durch den Tunnel, kein Masquerade. Sonst sähe der Server alle Clients unter einer Adresse, und die Grenzen je Präfix, die Adressbindung und die Pfadprüfung liefen ins Leere. Als Sicherung gibt es die Einstellung `vertrauenswuerdigeWeiterleiter`: Für deren Adressen gelten statt der Grenzen je Präfix nur die globalen Töpfe und die Grenzen je Sitzung, mit Warnung im Log. MTU: 1200 + 28 Byte liegen unter 1412 (WireGuard über PPPoE).

### 7.7 Latenzbudget (Mund zu Ohr)

Ziel: p95 höchstens 150 ms bei 20 ms Netz-RTT und Sprechtaste. Bei Sprachaktivierung kommt die Gate-Bestätigung hinzu, die der Pre-roll-Burst auffängt und der Empfänger binnen 1 s abbaut.

| Stufe | Zielwert | Grundlage |
| --- | --- | --- |
| Aufnahme (WASAPI, 10-ms-Periode) | 10 ms | codec-ausgabe.md B23 |
| APM mit AEC3 und RNNoise | ca. 24 ms | audio-dsp.md, Korrekturen |
| Opus-Frame und Look-ahead | 20 + ca. 5 ms | codec-ausgabe.md B14 |
| Netz hoch | RTT/2, hier 10 ms | - |
| Server: Verarbeitung und Pacer | p99 unter 2 ms, Warteschlange höchstens 20 ms, typisch unter 1 ms | Z2, 7.4 |
| Netz runter | 10 ms | - |
| Jitterpuffer | 40-60 ms (mit FEC mindestens 40) | codec-ausgabe.md B16, B19 |
| Ausgabe (Mischer, WASAPI) | 10-20 ms | codec-ausgabe.md B23 |
| Summe | ca. 130-155 ms | ITU-T G.114 nennt 150 ms für gute Interaktivität |

Gemessen wird in P-20 und P-50 mit einer Klick-Schleife über zwei Clients (Loopback-Aufnahme), dazu je Client die Einzelwerte aus 7.8.

### 7.8 Messwerte

- Der Server protokolliert je Sitzung in 10-s-Fenstern (Log und Admin-Werkzeug):
  - hoch: empfangen, verloren, umsortiert und doppelt aus den Lücken des hoch-Zählers (er umfasst alle Pakettypen und beide Wege; verloren geht nur UDP);
  - Interarrival-Jitter nach RFC 3550 Abschnitt 6.4.1: `D(i,j) = (Rj - Ri) - (Sj - Si)`, `J = J + (abs(D(i-1,i)) - J) / 16`, mit `S` = Frame-Nr x 20 ms und `R` = Empfangsstempel (monoton), nur aufeinanderfolgende Pakete innerhalb einer Sprechphase, ohne PREROLL;
  - runter: gesendet und vom Server verworfen, getrennt nach Budget, Alter und Strom-Abwurf;
  - Tunnelanteil, Pfadwechsel, Kopierverdacht, RTT und Werte aus dem EmpfangsBericht.
- Der Client meldet im EmpfangsBericht: Verlust runter aus den Lücken des runter-Zählers, RTT p50/p95 aus KEEPALIVE_ECHO, Jitter (gleiche Formel über die runter-Sprache, Höchstwert über die Ströme), Zielverzögerung des Jitterpuffers, Spätverwürfe und Ausgabe-Unterläufe.
- Verliert ein Sender hoch über 30 s mehr als 10 %, bekommt er `Meldung{EIGENER_VERLUST, richtung: "HOCH"}` ("dein Upload verliert"); für runter gilt 20 % (4.5, VerlustHinweis).
- Güteklasse je Praesenz-Eintrag für `ZUWEISEN`: GUT (Verlust unter 2 % und Jitter unter 20 ms), MAESSIG (unter 8 % und unter 50 ms), sonst SCHLECHT.

## 8. Testvektoren und Prüfplan

Alle Schlüssel und Werte hier sind reine Testwerte.

### 8.1 Werkzeugprüfung AES-256-GCM

Vor den eigenen Vektoren MUSS die Test-Suite die GCM-Referenzfälle bestehen (McGrew/Viega, Test Case 13 und 14). Schlüssel 32 x 0x00, IV 12 x 0x00:
- TC13, leerer Klartext: Tag `530f8afbc74536b9a963b4f1c4cb738b`
- TC14, Klartext 16 x 0x00: Chiffrat `cea7403d4d606b6e074ec5d3baf39d18`, Tag `d0d1c8a799996bf0265b98b5d48ab919`

Unter .NET 10.0.12 am 29.09.2026 nachgerechnet: beide stimmen.

### 8.2 Nonce-Bildung

| IV | Zähler | Nonce |
| --- | --- | --- |
| `000102030405060708090a0b` | 1 | `000102030405060708090a0a` |
| `000102030405060708090a0b` | `0x0102030405060708` | `00010203050705030d0f0d03` |
| `a0a1a2a3a4a5a6a7a8a9aaab` | 0 | `a0a1a2a3a4a5a6a7a8a9aaab` |
| `a0a1a2a3a4a5a6a7a8a9aaab` | 5 | `a0a1a2a3a4a5a6a7a8a9aaae` |
| `b0b1b2b3b4b5b6b7b8b9babb` | 42 | `b0b1b2b3b4b5b6b7b8b9ba91` |
| `c0c1c2c3c4c5c6c7c8c9cacb` | 2^32 | `c0c1c2c3c4c5c6c6c8c9cacb` |

### 8.3 Sprachdatagramme (mit .NET 10.0.12 AesGcm gerechnet, Rückweg geprüft)

Testschlüssel:
- `K_hoch` Epoche 0 = `000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f`, `IV_hoch` = `a0a1a2a3a4a5a6a7a8a9aaab`
- `K_runter` Epoche 0 = `202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f`, `IV_runter` = `b0b1b2b3b4b5b6b7b8b9babb`
- `K_hoch` Epoche 1 = `404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f`, `IV_hoch` = `c0c1c2c3c4c5c6c7c8c9cacb`

**TV-A SPRACHE_HOCH**: Sitzung `0x1a2b3c4d`, Epoche 0, Zähler 5. Frame-Nr 100, Sprechphase 3, Kennzeichen BEGINN mit Sprechtaste (0x01), Zielart NETZ, Ziele [2, 7], Opus `f801020304050607` (TOC 0xF8: CELT FB 20 ms, Inhalt Attrappe).
```
Kopf      0110 0000 1a2b3c4d 0000000000000005
Klartext  00000064 0003 01 01 02 0002 0007 f801020304050607   (21 B)
Nonce     a0a1a2a3a4a5a6a7a8a9aaae
Chiffrat  cefa04952a75d72b9f0fa81e8f9a6f33fadd803ce6
Tag       f3a15ea103cbea77c18b354dd540186d
Datagramm 011000001a2b3c4d0000000000000005cefa04952a75d72b9f0fa81e8f9a6f33fadd803ce6f3a15ea103cbea77c18b354dd540186d   (53 B)
```

**TV-B SPRACHE_RUNTER**: Empfängersitzung `0x5e6f7081`, Epoche 0, Zähler 42. Absender Konto 1001, übernommen aus TV-A, Art NETZ. Der Empfänger hört nur Netz 7. Es ist das erste Paket dieser Phase an ihn, der Server setzt also BEGINN.
```
Kopf      0111 0000 5e6f7081 000000000000002a
Klartext  000003e9 00000064 0003 01 01 01 0007 f801020304050607   (23 B)
Nonce     b0b1b2b3b4b5b6b7b8b9ba91
Chiffrat  9c7690b94867debba4d41624c65308a12e168a1c633470
Tag       cb5c50af7c7cbb3338fd7167f180cdb9
Datagramm 011100005e6f7081000000000000002a9c7690b94867debba4d41624c65308a12e168a1c633470cb5c50af7c7cbb3338fd7167f180cdb9   (55 B)
```

**TV-C KEEPALIVE**: Sitzung `0x1a2b3c4d`, Epoche 0, Zähler 6, nr 1, zeitMs `0x00001234`.
```
Kopf      0104 0000 1a2b3c4d 0000000000000006
Klartext  00000001 00001234 0000000000000000
Nonce     a0a1a2a3a4a5a6a7a8a9aaad
Chiffrat  f8e1ac92fb8f3bc066323499a1d6928a
Tag       ceaf234f5024fa1affe3d9e53ec31d96
Datagramm 010400001a2b3c4d0000000000000006f8e1ac92fb8f3bc066323499a1d6928aceaf234f5024fa1affe3d9e53ec31d96   (48 B)
```

**TV-D SPRACHE_HOCH, ENDE, Epoche 1, Zähler 2^32**: Frame-Nr 120, Sprechphase 3, Kennzeichen ENDE (0x02), NETZ [2], ohne Opus. Positivfall für die vollständige Empfangsprüfung (5.9): 43 Byte, also über der Mindestlänge 41.
```
Kopf      0110 0100 1a2b3c4d 0000000100000000
Klartext  00000078 0003 02 01 01 0002   (11 B)
Nonce     c0c1c2c3c4c5c6c6c8c9cacb
Chiffrat  dc393148d56cede37bc707
Tag       11f32fd891159aae37b5ee596eacdb60
Datagramm 011001001a2b3c4d0000000100000000dc393148d56cede37bc70711f32fd891159aae37b5ee596eacdb60   (43 B)
```

**TV-E PFAD_PRUEFUNG**: Empfängersitzung `0x5e6f7081`, Epoche 0, Zähler 43, Challenge `0f0e0d0c0b0a09080706050403020100`.
```
Nonce     b0b1b2b3b4b5b6b7b8b9ba90
Chiffrat  333211e4a53a9e0037b91c1f79252e3b
Tag       83c9ba39a8c66b1c2a3da09f88176547
Datagramm 010200005e6f7081000000000000002b333211e4a53a9e0037b91c1f79252e3b83c9ba39a8c66b1c2a3da09f88176547   (48 B)
```

**TV-F ANMELDUNG**: Sitzung `0x1a2b3c4d`, Epoche 0, Zähler 0, Klartext 16 x 0x00.
```
Nonce     a0a1a2a3a4a5a6a7a8a9aaab
Chiffrat  e6187c2d45cb02bf626587d3077ac0de
Tag       ea6b42ff94f0cf209c1998c8323032fa
Datagramm 010100001a2b3c4d0000000000000000e6187c2d45cb02bf626587d3077ac0deea6b42ff94f0cf209c1998c8323032fa   (48 B)
```

Negativfälle, jeder MUSS scheitern bzw. verworfen werden:
- TV-A mit Kopf-Byte 15 = `0x04` statt `0x05`: Tag ungültig. Nachgerechnet, die Entschlüsselung scheitert.
- TV-A mit einem gekippten Bit im Tag oder Chiffrat: Tag ungültig.
- TV-A entschlüsselt mit `K_runter`: Tag ungültig.
- TV-A mit Typ `0x11` beim Server: wird vor dem Entschlüsseln verworfen (falsche Richtung).
- TV-A mit reserviert = `0x01`: wird vor dem Entschlüsseln verworfen.
- TV-A zweimal: das zweite Mal verwirft das Replay-Fenster.
- TV-A mit Epoche 1 im Kopf, solange Epoche 1 nicht angekündigt ist: wird vor dem Entschlüsseln verworfen.
- Ein Datagramm mit 40 Byte: in Schritt 1 verworfen. Ein SPRACHE_RUNTER mit 44 Byte beim Client: verworfen.
- TV-A als SpracheTunnel-Rahmen über die TLS-Verbindung einer anderen Sitzung: `PROTOKOLL` und Trennung.

### 8.4 Steuerrahmen

| Fall | Bytes |
| --- | --- |
| Ping-Anfrage, Anfrage-Nr 7, Body `{"nr":1}` | `0000001001000004000000077b226e72223a317d` (20 B) |
| Ping-Antwort, Body `{"nr":1,"udpEmpfangen":250,"udpGesendet":1250,"serverZeit":1790000000000}` | `0000005101010004000000077b226e72223a312c22756470456d7066616e67656e223a3235302c22756470476573656e646574223a313235302c227365727665725a656974223a313739303030303030303030307d` (85 B) |
| Fehler-Antwort auf Verschieben (Anfrage-Nr 8), Body `{"code":"KEIN_RECHT","bezugTyp":51}` | `0000002b01010006000000087b22636f6465223a224b45494e5f5245434854222c2262657a7567547970223a35317d` (47 B) |
| VoiceKeys-Anfrage (Server-Anfrage `0x80000001`), Epoche 0, START, Schlüssel und IVs der Epoche 0 aus 8.3 | `0000006201008002800000010000000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1fa0a1a2a3a4a5a6a7a8a9aaab202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3fb0b1b2b3b4b5b6b7b8b9babb` (102 B) |
| Client-Antwort darauf, Epoche 0 | `00000009010180028000000100` (13 B) |
| Client-Antwort auf eine Rotation (Server-Anfrage `0x80000002`), Epoche 1 | `00000009010180028000000201` (13 B) |

Negativfälle: Länge 7; vor Hello Länge 4097; in WARTERAUM Länge 1033; in AKTIV Länge 1.048.577; Rahmenversion 2; Flags `0x02`; unbekannter Typ `0x3FFF` (Trennung); unbekannter Typ `0x4FFF` (wird übergangen); Antwort mit unbekannter Anfrage-Nr (Trennung); Body mit doppeltem Schlüssel `{"nr":1,"nr":2}` (`PROTOKOLL`); VoiceKeys-Anfrage mit 89 Byte Body (`PROTOKOLL`); VoiceKeys-Antwort mit falscher Epoche (`PROTOKOLL`).

### 8.5 SPKI-Fingerabdruck

Als Testschlüssel dient der öffentliche Punkt Q = G der Kurve P-256 (privater Schlüssel 1, nur für Tests).
```
SPKI (DER)  3059301306072a8648ce3d020106082a8648ce3d03010703420004
            6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296
            4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5
SHA-256     5cd252fb0ce8932436faf8ccd1040981b89ee4ad6b9fe9e2a2b7e71aacb27cd3
Base64      XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=
Anzeige     5CD2-52FB-0CE8-9324
```
`ECDsa.ExportSubjectPublicKeyInfo()` in .NET 10 liefert genau diese DER-Bytes (nachgeprüft). Ein Pin ist die Base64-Form.

### 8.6 Einmalcode

Normalisiert wird im Server, nach der Eingabe in Discord.

| Eingabe | normalisiert | SHA-256 (ASCII) |
| --- | --- | --- |
| `" k7m4 q9tx-2hwd "` | `K7M4Q9TX2HWD` | `d30125c87bc69a4ec95a4fcbf0d928c1df3f6b53e119a8cd057518efc9635790` |
| `K7M4-Q9TX-2HWO` | `K7M4Q9TX2HW0` | anderer Code (O -> 0) |
| `K7M4-Q9TX-2HW` | 11 Zeichen | ungültig, Fehlversuch |
| `K7M4-Q9TX-2HWU` | `U` nicht im Alphabet | ungültig, Fehlversuch |

### 8.7 Replay-Fenster (W = 2048, alle Tags gültig, außer wo anders angegeben)

| Schritt | Zähler | Ergebnis | Grund |
| --- | --- | --- | --- |
| 1 | 0 | annehmen | erstes Paket |
| 2 | 1 | annehmen | neu |
| 3 | 1 | verwerfen | doppelt |
| 4 | 5 | annehmen | neues Maximum |
| 5 | 3 | annehmen | im Fenster, ungesehen |
| 6 | 3 | verwerfen | doppelt |
| 7 | 2053 | annehmen | neues Maximum |
| 8 | 5 | verwerfen | 2053 - 5 = 2048, nicht < W |
| 9 | 6 | annehmen | 2047 < W, ungesehen |
| 10 | 4 | verwerfen | zu alt |
| 11 | 100000 | annehmen | großer Sprung |
| 12 | 99999 | annehmen | im Fenster |
| 13 | 97952 | verwerfen | Abstand 2048 |
| 14 | 97953 | annehmen | Abstand 2047 |
| 15 | 100000 | verwerfen | doppelt |
| 16 | 100001, falscher Tag | verwerfen | Fenster bleibt unverändert |
| 17 | 100001, richtiger Tag | annehmen | Beleg für "Aktualisierung erst nach der Tag-Prüfung" |

Zusatz für P-05: Zwei Threads legen dieselbe Kopie von Zähler 100002 gleichzeitig vor; genau eine wird angenommen.

### 8.8 Opus-TOC (Serverprüfung 5.13)

| Opus-Anfang | config, s, c | Ergebnis |
| --- | --- | --- |
| `f8` | 31 (CELT FB 20 ms), 0, 0 | 20 ms, annehmen |
| `78` | 15 (Hybrid FB 20 ms), 0, 0 | 20 ms, annehmen |
| `f0` | 30 (CELT FB 10 ms), 0, 0 | 10 ms, verwerfen |
| `f1` | 30, 0, 1 | 2 x 10 ms, annehmen |
| `fc` | 31, 1 (Stereo), 0 | verwerfen |
| `0b 02` | 1 (SILK NB 20 ms), 0, 3, M = 2 | 40 ms, verwerfen |
| `0b` allein | c = 3 ohne Byte 1 | verwerfen |
| `f8` mit 400 Byte Nutzlast | 31, 0, 0 | annehmen |
| `f8` mit 401 Byte Nutzlast | - | verwerfen (m > 400, vor der TOC-Prüfung) |

### 8.9 Rechte-Matrix (Senderecht und Empfänger, deterministisch)

Grundzustand. Netze: 1 Global (`nurTaste`), 2 Kommando, 7 Crew Schiff 3, 8 Brücke Schiff 3 (`nurTaste`). `durchsageNurTaste = true`. Umstellungen und Stummschaltungen nimmt ein Admin-Konto vor, das keine Sitzung hat.

| Konto | Stufe | Felder (Feld: Netz, hören, senden) |
| --- | --- | --- |
| A 1001 | RAIDLEITER | 0: 2 h s; 1: 7 h s |
| B 1002 | FUNKER | 0: 7 h s; 1: 8 h s |
| C 1003 | FUNKER | 0: 2 h -; 1: 7 hören AUS, s; 2: 8 h - |
| D 1004 | FUNKER | keine |
| E 1005 | FUNKER | 0: 7 h -; 1: 2 h - |

| Nr | Sender, Zielart, Ziele, Auslöser | Erwartung (Empfänger: Art [Netze]) |
| --- | --- | --- |
| 1 | B, NETZ, [7], PTT | A: NETZ [7]; E: NETZ [7]; C nichts (Hören AUS); D nichts |
| 2 | B, NETZ, [8], VA | verworfen NUR_TASTE, SendeFehler an B |
| 3 | B, NETZ, [8], PTT | C: NETZ [8] |
| 4 | C, NETZ, [2], PTT | verworfen KEIN_SENDERECHT |
| 5 | B, NETZ, [1], PTT | verworfen KEIN_RECHT_GLOBAL |
| 6 | A, NETZ, [1], PTT | B, C, D, E: GLOBAL [1] |
| 7 | A, NETZ, [2, 7], PTT | B: NETZ [7]; C: NETZ [2]; E: NETZ [7, 2] (Feldfolge von E); D nichts |
| 8 | B, NETZ, [7, 8], PTT | verworfen MEHRFACH_NICHT_ERLAUBT |
| 9 | A, DURCHSAGE, [7], PTT | B, C, E: DURCHSAGE [7] (C trotz Hören AUS); D nichts |
| 10 | A, DURCHSAGE, [], PTT | B, C, D, E: DURCHSAGE [] |
| 11 | D, DURCHSAGE, [], PTT | verworfen KEIN_RECHT_DURCHSAGE |
| 12 | A, NETZ, [7, 2] (unsortiert) | verworfen UNGUELTIG |
| 13 | A, NETZ, [1, 2], PTT | B: GLOBAL [1]; C: GLOBAL [1, 2]; D: GLOBAL [1]; E: GLOBAL [1, 2] |
| 14 | nach Verschieben (C verliert Feld 0), A, NETZ, [2], PTT | E: NETZ [2]; C nichts |
| 15 | Stummschalten(A, 7, 60 s), dann A, NETZ, [7] / A, NETZ, [2], PTT | verworfen STUMMGESCHALTET / C, E: NETZ [2] |
| 16 | A, DURCHSAGE, [1] | verworfen UNGUELTIG |
| 17 | A, NETZ, [1], VA | verworfen NUR_TASTE (Global nurTaste) |
| 18 | A, DURCHSAGE, [7], VA | verworfen NUR_TASTE (durchsageNurTaste) |
| 19 | Stummschalten(A, 7, 60 s), dann A, NETZ, [2, 7], PTT | C: NETZ [2]; E: NETZ [2]; B nichts; SendeFehler an A: STUMMGESCHALTET Netz 7, teilweise |
| 20 | A, NETZ, [2, 99], PTT (99 unbekannt) | C: NETZ [2]; E: NETZ [2]; SendeFehler an A: UNBEKANNTES_NETZ Netz 99, teilweise |
| 21 | Stummschalten(A, 7, 60 s), dann A, DURCHSAGE, [2, 7], PTT | C: DURCHSAGE [2]; E: DURCHSAGE [2]; B nichts; SendeFehler an A: STUMMGESCHALTET Netz 7, teilweise |
| 22 | Stummschalten(A, 0 = alle, 60 s), dann A, DURCHSAGE, [], PTT | verworfen STUMMGESCHALTET |
| 23 | nach Verschieben (A verliert Feld 1), A, NETZ, [2, 7], PTT | C: NETZ [2]; E: NETZ [2]; B nichts; SendeFehler an A sofort: ZIEL_ENTZOGEN Netz 7 durch Admin, teilweise |

In keinem Fall erhält der Sender sein eigenes Paket. Das jeweils erste Paket einer Phase an einen Empfänger trägt BEGINN.

Die Matrix gilt mit 0.3.0 unverändert, denn RAIDLEITER behält Global, Durchsage und Mehrfachsenden. Dazu kommt Stufe 2 (Tests in `RechteBeauftragungTests.cs` und `WeiterleitungSchreiberTests.cs`):

| Nr | Fall | Erwartung |
| --- | --- | --- |
| 24 | ein FUNKER mit Auftrag bis t: 1 ms vor t Verschieben und NETZ, [1], PTT | Verschieben erlaubt, Global geht durch |
| 25 | derselbe 1 ms nach t, bevor der Fristen-Takt lief | Verschieben und PlaetzeFreigeben `KEIN_RECHT`, Global verworfen KEIN_RECHT_GLOBAL, Durchsage verworfen KEIN_RECHT_DURCHSAGE; der Takt meldet danach ABGELAUFEN |
| 26 | geplanter Auftrag ab t: Verschieben und Global vor t, dann ab t | vor t `KEIN_RECHT` und KEIN_RECHT_GLOBAL, ab t beides erlaubt, auch ohne Takt; der Takt meldet BEGONNEN genau einmal |
| 27 | Rangregel je Stufe des Anfragenden und des Ziels, mit und ohne Auftrag (54 Kombinationen, P19) | `ZUWEISEN` wirkt auf FUNKER und RAIDLEITER, auf ADMIN nur durch ADMIN; RAIDLEITER ohne Auftrag teilt nicht ein |

### 8.10 Folge-Matrix (Übergänge, simulierte Uhr)

Grundzustand wie 8.9. Sprechphasen mit PTT, sofern nicht anders angegeben.

| Nr | Ablauf | Erwartung |
| --- | --- | --- |
| F1 | A sendet NETZ [2, 7] (Phase 5), Pakete 1-2; danach nimmt der Admin A das Feld 1 (Netz 7); Paket 3 trifft noch ein | Pakete 1-2: B NETZ [7], C NETZ [2], E NETZ [7, 2], je das erste mit BEGINN. Paket 3: C NETZ [2], E NETZ [2] ohne BEGINN; B bekommt einmal ein erzeugtes ENDE [7]; A bekommt sofort ZIEL_ENTZOGEN Netz 7. A's Client beendet Phase 5 und setzt ohne Unterbrechung Phase 6 an [2] fort (C4); C und E hören keinen Klick (C6) |
| F2 | B sendet NETZ [7]; mitten in der Phase schaltet C HoerenSetzen(7, true) | nächstes Paket an C mit BEGINN; A und E unverändert, ohne BEGINN |
| F3 | B sendet NETZ [7]; der Admin verschiebt E aus Netz 7 | E bekommt einmal ein erzeugtes ENDE [7], danach nichts; A unverändert |
| F4 | A sendet DURCHSAGE [7]; der Admin gibt D ein Feld mit Netz 7 | D: nächstes Paket DURCHSAGE [7] mit BEGINN, dazu DurchsageStart{1001, DURCHSAGE, [7]} |
| F5 | B (fehlerhafter Client) wechselt innerhalb von Phase 9 die Ziele von [7] auf [8] | Server behandelt das als neue Phase: A und E bekommen ein erzeugtes ENDE [7], C bekommt BEGINN [8]; nichts wird verworfen |
| F6 | B sendet Phase 3 mit 3 Paketen, alle drei ENDE gehen verloren; 400 ms später trifft ein Paket 4 derselben Phase ein | Empfänger beenden die Phase 300 ms nach leerem Jitterpuffer (Klick bei PTT); Paket 4 setzt fort ohne Beginn-Klick |
| F7 | B hält PTT; die Sendeauswahl wechselt von Feld 0 (Netz 7) nach 100 ms auf Feld 1 (Netz 8) und nach weiteren 100 ms zurück auf Feld 0 | Client sendet beim ersten Wechsel ENDE an [7], dann nichts, bis die Auswahl 250 ms stabil ist; danach neue Phase an [7] mit BEGINN, ohne Pre-roll; Netz 8 bekommt nichts |
| F8 | A sendet DURCHSAGE []; mitten in der Phase schaltet der Admin A für alle stumm | weitere Pakete verworfen STUMMGESCHALTET; B, C, D, E bekommen ein erzeugtes ENDE und DurchsageEnde |
| F9 | Ein Client sendet 10 s lang 30 SendeauswahlSetzen je Sekunde | überzählige mit UEBERHOLT beantwortet, kein Verstoß, keine Trennung; am Ende gilt der letzte Stand in Praesenz.sendetIn |
| F10 | Schiff 3, Rolle CO mit Kapazität 1; E hat sich selbst eingetragen; eine Aufstellung ohne E setzt B als CO auf Schiff 3 | B ist CO; E fällt auf STANDARD; Antwort `verdraengt: [1005]` |
| F11 | Verschieben tauscht in einer Anfrage A und B zwischen zwei Plätzen mit Kapazität 1 | angenommen (Kapazität am Endzustand) |
| F12 | Verschieben(B) mit basisVersion v, nachdem D sein Hören umgeschaltet hat / nachdem B sein Hören umgeschaltet hat | angenommen / VERALTET mit details.konten [1002] |
| F13 | Sitzung von E wird durch ein zweites Gerät von E verdrängt, dreimal hin und her binnen 5 min | beim dritten Mal Meldung VERDRAENGUNG_HAEUFIG; weitere Verdrängungen VERDRAENGEN_GESPERRT, die laufende Sitzung bleibt |

### 8.11 Profilsignatur

Schlüssel: der Testschlüssel aus 8.5 (privat 1, Q = G). `daten` sind genau diese 259 Byte ASCII ohne Zeilenumbruch:

```
{"name":"Test","profilVersion":2,"ausgestellt":1790000000000,"gueltigBis":1805000000000,"hosts":[{"name":"funk.example","familien":["IPv4"]}],"tcpPort":443,"pins":["XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=","AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="]}
```

```
Praefix           74616372656c61792070726f66696c20763100   (19 B)
SHA-256(Praefix || daten)
                  1988cee397416db14408d00f19b55bf6bc27cca89aac8b949ee66ce1031d8026
Signatur (P1363)  8ae61a79115c7b38f827d9f107aa84d31eb8d677ab3118fa7f20b6a84d48adfe
                  41af3313051f3c4137391f314f0a5c80325bb4a8afb3768ceb349f700404206b
Signatur (Base64) iuYaeRFcezj4J9nxB6qE0x641nerMRj6fyC2qE1Irf5BrzMTBR88QTc5HzFPClyAMlu0qK+zdozrNJ9wBAQgaw==
```

ECDSA-Signaturen sind zufällig; das ist ein Prüfvektor, keiner zum Nachbilden. Neu gerechnet am 19.10.2026 für das Präfix von JG TacRelay, mit .NET 10.0.12 (nur öffentlicher Schlüssel) nachgeprüft: Die Signatur gilt. Sie scheitert mit einem gekippten Bit in `daten`, ohne Präfix und mit einem gekippten Bit in der Signatur. Der Client übernimmt dieses Profil nur, wenn seine gespeicherte `profilVersion` kleiner als 2 ist.

### 8.12 Prüfplan

Deterministisch (Protokollkern, simulierte Uhr, ohne Netzwerk):
- P-01 Rahmen kodieren und dekodieren (8.4), Längengrenzen je Zustand, Typbereiche, ein Write je Rahmen.
- P-02 JSON-Regeln: doppelte Schlüssel, Tiefe 9, Steuer- und Bidi-Zeichen, Bereichsgrenzen, fehlende Pflichtfelder, unbekannte Felder werden übergangen, `null` wie fehlend.
- P-02b Jede Nachricht an ihren Höchstwerten (4.10) bleibt unter der Rahmengrenze und wird vom Empfänger angenommen.
- P-03 Nonce (8.2), Zähler an 2^32 und 2^63.
- P-04 Datagramme TV-A bis TV-F bytegenau versiegeln und öffnen und durch die vollständige Empfangsprüfung 5.9 schicken (TV-D als Positivfall), dazu die Negativfälle aus 8.3, Grenzfälle 40/41 Byte hoch, 44/45 Byte runter.
- P-05 Replay-Fenster (8.7), dazu zwei Threads mit derselben Paketkopie.
- P-06 Epochen: Annahmefenster für e, e+1 und e-1 mit simulierter Uhr; der Zähler läuft über Epochen; Auslöser 60 min, 2^20, 2^21 und 2^22; Trennung ohne Antwort auf VoiceKeys; `Dispose` beim Verwerfen.
- P-07 Pfadprüfung: Nur ein neuer Höchstwert löst eine Prüfung aus; genau eine PFAD_PRUEFUNG je auslösendem Paket, Wiederholung nach 250 ms, höchstens 4/s; Gegenprobe an B; Antwort von falscher Adresse wird verworfen; vor der Umbindung keine Sprache an A; Umbindungsfrist 500 ms; Rückkehr bei neuem Höchstwert von B; Kopier-Rennen (schnellerer Beobachter kopiert Pakete und Antworten) ergibt Kopierverdacht statt Umbindung; gefälschte Pakete mit bekannter Sitzungs-ID von vielen Adressen verhindern ein echtes Rebinding nicht; Verhältnis <= 1.
- P-08 Rechte-Matrix (8.9), dazu Zufallstests mit diesen Eigenschaften: Kein Empfänger ohne Mitgliedschaft mit Hören (außer Global und Durchsage), der Absender ist nie Empfänger, der Stempel ist genau die Schnittmenge aus erlaubten Zielen und Hörnetzen, je Empfänger gibt es höchstens eine Kopie, das erste Paket einer Phase an einen Empfänger trägt BEGINN.
- P-09 Aufstellung atomar: Austausch zwischen Paket k und k+1, kein Paket mit gemischtem Stand; bei einem Validierungsfehler alles oder nichts mit vollständiger Fehlerliste; Kapazitätskonflikt mit Vorrang, Tausch, fehlende Version, veraltete Version, ERSETZEN ohne den Anfragenden; Absturz zwischen Speichern und Austausch hinterlässt einen stimmigen Stand; Zeit von der Taste bis zum geänderten Hören unter gleichzeitiger HoerenSetzen-Last.
- P-10 Zustandsautomat: jede Nachricht in jedem Zustand gegen 4.6 (erlaubt, Fehler, Trennung), alle Fristen; 60 s ununterbrochener Empfang ohne Server-Push führt nicht zur Trennung; Verdrängung nur aus AKTIV_SYNC, ERSETZT bei gleichem Gerät, VERDRAENGEN_GESPERRT; SpracheTunnel mit fremder Sitzungs-ID ergibt PROTOKOLL.
- P-11 Registrierung und Geräte: Normalisierung (8.6), Code gebunden an die Verbindung, Ablauf nach 5 min, Rückfrage im Client (ja, nein, Frist), Grenzen je Präfix und je Discord-ID, einheitlicher Fehlercode, privilegierte Konten nur über das Admin-Werkzeug, kein Weg vom Code zu FREIGEGEBEN, WARTET verfällt nach 24 h, Geschwister können nur markieren, älteres Gerät nur per Antrag entfernbar, dauerhafte Sperre nur mit GERAETE_FREIGEBEN, 90-Tage-Regel.
- P-12 Sperre und Entzug beenden laufende Sitzungen binnen 1 s; die Schlüssel sind gelöscht, spätere Pakete bleiben ohne Antwort.
- P-13 Opus-TOC und Länge (8.8), Encoder mit `max_data_bytes = 400`.
- P-14 SPKI, Pin und Profil (8.5, 8.11): richtiger Pin, falscher Pin, Reserve-Pin; signiertes Profil wird übernommen; falsche Signatur, fehlendes Präfix, gleiche oder kleinere `profilVersion` und ein unsigniertes Profil mit neuem Pin oder neuem Host werden verworfen; Pin-Fehler beim Wiederverbinden gilt erst nach 2 min als Alarm. Übernahme aus Welcome (3.1): gespeichert, nach einem Neustart noch gültig, ältere, gleiche, fremd signierte, unsignierte und kaputte verworfen und protokolliert, ebenso eines ohne den Pin des liefernden Servers, eine gelöschte oder kaputte Ablage fällt auf das Installer-Profil zurück. Erstverbindung mit fremdem Pin an einem Host nimmt den anderen Host; ohne gültigen Pin irgendwo Alarm.
- P-15 Token-Buckets mit simulierter Uhr, alle Werte aus 4.8 und 5.9; Zusammenfassen mit UEBERHOLT (F9); Vorrangspur umgeht den globalen Topf; Gerätesperre statt IP-Sperre.
- P-16 Unabhängige Gegenrechnung: TV-A bis TV-F, 8.4, 8.5 und 8.11 mit einer zweiten Implementierung (Python `cryptography` oder OpenSSL), möglichst durch jemand anderen.
- P-17 Folge-Matrix (8.10).
- P-18 Frame-Nr: Zwei Frames im Abstand von 1 ms codiert ergeben n und n+1; Neuanlage bei BEGINN größer als die letzte vergebene; Pre-roll fortlaufend.
- P-19 Rang- und Rechteregeln (6.10): jede Kombination aus Stufe des Anfragenden und des Ziels, letzter Admin, Abweichungen nach STUFE.

Integration (Linux-Server, Windows-Client, simulierte Clients, UDP-Proxy):
- P-20 Ende zu Ende: 1 Sprecher, 3 Hörer, Opus-Nutzlast bitgleich; Mund-zu-Ohr-Messung per Klick-Schleife (7.7).
- P-21 UDP-Proxy: Verlust 1, 5 und 20 %, Serienverlust nach Gilbert-Elliott, Jitter 0-80 ms, Umordnung, Verdopplung, NAT-Portwechsel mitten im Sprechen und bei einem stummen Hörer, Sperre nur hoch und nur runter. Ziel: Erkennung binnen 3 s, Tunnel binnen 4 s.
- P-22 Tunnel: UDP gesperrt ergibt Tunnel binnen 3 s; UDP wieder frei ergibt Rückkehr erst nach 10 s sauberer Probe; kein Pendeln bei wackligem UDP; ein gemeinsamer Zähler ohne Lücke und ohne Wiederholung; Tunnel bei 2 % Verlust und 100 ms RTT, Verzug p99 messen.
- P-23 Server-IP-Wechsel: DNS-Eintrag im Test-DNS umstellen, Wiederverbindung nach 7.1, Mitgliedschaften WIEDERHERGESTELLT; im Feld zusätzlich "Neu verbinden" in der FRITZ!Box bei laufender Sitzung mit 5 Clients über die echte Resolverkette, Ausfallzeit je Client, Ziel 20 s.
- P-24 Serverneustart hart und geordnet; 40 Clients verbinden gleichzeitig neu, während eine Flut unauthentisierter Handshakes läuft.
- P-25 Windows 10 mit TLS 1.2 und Windows 11 mit TLS 1.3, jeweils mit TPM- und mit Software-Schlüssel.
- P-26 Gesättigter Heim-Upload durch einen parallelen Upload aus dem Heimnetz: RTT vom Server zu den Clients, Verlust je Priorität, Jitterpuffer-Ziel.
- P-27 WLAN-Aussetzer von 200-500 ms beim Sender und beim Hörer: kein Klick mitten im Satz (C6).
- P-28 AAAA-Eintrag auf die FRITZ!Box und Pin-Fehler an einer Adresse: Verbindungsaufbau nach RFC 8305 nimmt die richtige Adresse ohne 3-s-Wartezeiten.
- P-29 Umstellung mitten im Spruch mit echten Clients (Sender und Hörer): BEGINN und ENDE je Empfänger, ZIEL_ENTZOGEN, Fortsetzung nach C4.

Last (auf dem Ziel-LXC):
- P-30 Szenario B (12 Sprecher x 15 Hörer) 1 h: Verarbeitung je Paket p99 unter 2 ms ab Empfangsstempel, keine Verluste im Server, GC-Pausen mitgemessen.
- P-31 Szenario D (40 gleichzeitig) kurz, hinter einem tc-Shaper auf 45 Mbit/s oder über die echte Leitung: Pacer, Strom-Abwurf, Vorrang für Global und Durchsage, dazu "ein Troll bei voller Last".
- P-32 Dauerlauf 8 h mit Rotation alle 2 min (Testeinstellung) und 10 Wiederverbindungen je Stunde.

Sicherheit und Negativtests:
- P-40 Keine Antwort auf Ungültiges: Zufallsdatagramme, bekannte Sitzungs-ID mit falschem Tag, Replays, fremde Epoche. Der Mitschnitt zeigt 0 Byte vom Server.
- P-41 Verstärkung: gesendete durch empfangene Bytes je Quelladresse vor der Bindung <= 1.
- P-42 Fuzzing: UDP-Parser vor und nach dem Entschlüsseln (mit Testschlüsseln), Rahmen-Parser, binärer VoiceKeys-Parser, JSON-Nachrichten einschließlich Hello und Registrierung, Profil-Parser (Mutation der Vektoren), je Parser mindestens 10^7 Fälle ohne Ausnahme und Absturz.
- P-43 TLS: ohne Client-Zertifikat, mit RSA-Zertifikat, mit TLS 1.1, falschem ALPN, Renegotiation, falschem Serverschlüssel. Alles MUSS abbrechen.
- P-44 Kein Leck in Registrierung und Warteraum: keine Netz-, Konto- oder Aufstellungsdaten, keine Serverversion.
- P-45 Rechteausweitung: jede Anfrage ohne Recht oder Rang ergibt `KEIN_RECHT` bzw. `NUR_ADMIN_WERKZEUG` ohne Seiteneffekt (Zustand vorher gleich nachher), auch Stummschalten und Verschieben höherer Stufen und Selbstaufhebung fremder Stummschaltungen.
- P-46 Logs und Abbilder: Durchsuchung von Server-Log, Bot-Log und HttpClient-Log nach Schlüsseln, IVs, Codes, Tokens, dem Raid-Helper-Key und Klartext-IPs aus dem Testlauf ergibt keinen Treffer; am Server entsteht bei einem erzwungenen Absturz kein Abbild.
- P-47 Hören nach Entzug: Nach Verschieben kommt beim Entfernten außer einem ENDE kein weiteres Paket an (Mitschnitt).
- P-48 Kompromittierter Bot (simuliert): Flut von Verknuepfen- und Mitgliedschaft-Meldungen; die Grenzen aus 3.8 greifen, kein ADMIN-Konto wird gesperrt, kein Gerät wird freigegeben.
- P-49 Admin-Nachweis (3.9, 8.13 bis 8.15): RFC-Vektoren für HOTP und TOTP; Bindung der Unterschrift an Sitzung, Gerät, Nonce und Zweck; Nonce einmal; jeder Code einmal, kein älterer nach einem neueren, Fenster genau einen Schritt; Pause nach 5 und Sperre nach 10 Fehlversuchen, ohne gültige Unterschrift kein Fehlversuch; Notfallcodes einmal und an das Konto gebunden; Fristen der Einrichtung; Chiffrat an Konto und Generation gebunden; getauschter Schlüssel ist kein Fehlversuch; Geheimnis, Codes und Notfallcodes weder in Log, `ToString` noch Klartext in den Dateien.
- P-51 Gemeinsamer Test: Jede Verwaltungsnachricht (auch Journal, Rollenkatalog, Mitglied, AntraegeAblehnen, seit 0.11.7 Abzeichen, AdminBestaetigen, AdminAbmelden; Untergilde seit 0.11.8 nicht mehr, sie ist für jeden `UNGUELTIG`) von einem ADMIN-Konto ohne Admin-Sitzung ergibt `ADMIN_SITZUNG_NOETIG`, von einem Funker `KEIN_RECHT`, beides ohne Seiteneffekt; Pushes an Verwaltungsrechte erreichen keine Sitzung ohne Admin-Sitzung, Warnungen jedes ADMIN-Konto. Leerlauf, Höchstdauer und frische Bestätigung mit gestellter Uhr. Ausnahme nach Antwort 4: Rollenkatalog SETZEN, das nur ergänzt, geht für Konten mit `ZUWEISEN` oder `GLOBAL_SENDEN` auch ohne Admin-Sitzung (P-53).
- P-52 Angriffe auf Namen und Rang: zwei Anträge mit demselben Namen, Freigabe ohne den vergebenen Namen, `NAME_VERGEBEN` über Konten und Vormerkungen (auch Breitschrift nach NFKC); Eigentümer herabstufen, sperren, löschen durch einen zweiten Admin mit Admin-Sitzung und frischem Code; Verwaltungsrecht als Einzelrecht und Stufe ADMIN aus dem Client; P-19 mit Admin-Sitzung und frischer Bestätigung als dritter Achse.
- P-53 Antworten 3 und 4 (Stufe 1, Client): Discord-Namen mit Zeichen aus Cf, Zl, Zp, Co, Cn, anderen Leerzeichen als U+0020 oder doppelten Leerzeichen ergeben `UNGUELTIG`; Praesenz trägt den Discord-Namen für alle, Zeichen nur mit Freigabe (seit 0.11.8 nie eine Untergilde); nach `karteFreigeben: false` zeigt die Karte anderen nur Namen, Platz und online (`freigegeben: false`), der Einsatzleitung weiter alles; ein Funker bekommt die Karte eines Getrennten ohne Platz nicht (`UNBEKANNT`). Rollenkatalog SETZEN ohne Admin-Sitzung: ergänzen geht mit `ZUWEISEN` oder `GLOBAL_SENDEN`, umbenennen, ersetzen und leeren nicht; ein Funker darf beides nicht. AdminNonce hat einen eigenen Eimer: drei Nonces lassen der Anmeldung ihren Stoß.
- P-54 Discord-Name nach A53: gleicher Discord-Name in anderer Schreibung, in Breitschrift oder mit Leerzeichen am Rand ergibt `NAME_VERGEBEN` (Konten und Vormerkungen); `ProfilSetzen` mit fremdem oder neuem eigenem Discord-Namen über den Draht ergibt `KEIN_RECHT` ohne jede Änderung, derselbe Wert geht; `KontoVerwaltung DISCORDNAME` vom Funker `KEIN_RECHT`, vom ADMIN ohne Admin-Sitzung `ADMIN_SITZUNG_NOETIG`, in der Admin-Sitzung ohne frischen Code erlaubt; den Eigentümer und andere Admins nur die Konsole; ein Antrag mit vergebenem Discord-Namen wird angenommen und seit 0.7.0 ohne diesen Namen freigegeben (bis 0.6.x lehnte die Freigabe ab), mit der passenden Vormerkung samt Namen; "Liste einfügen" nennt vergebene und doppelte Discord-Namen je Zeile; nach dem Laden ist der Namensraum wieder da.
- P-55 Stufe 2, Server und Protokoll (0.3.0): RAIDLEITER ohne `ZUWEISEN` in Lage und Kontenbuch gleich; Rangregel in allen 54 Kombinationen (8.9 Nr 27); Erteilen nur mit `KONTEN_VERWALTEN` und nur mit eigenen dauerhaften Rechten (ein Konto mit Einzelrecht `KONTEN_VERWALTEN` ohne `ZUWEISEN` verteilt kein `ZUWEISEN`), nicht an sich selbst, nicht an ADMIN oder gesperrte Konten, Grenzen von 4.8; der Beauftragte erteilt nichts, ändert keine Konten, verschiebt keinen ADMIN; Ablauf und Beginn je Aktion und Paket ohne Takt (8.9 Nr 24-26); Entzug, Abgabe, Sperre, Stufenwechsel und Wegfall des Erteilers beenden den Auftrag samt Sperre der Selbstwahl und Nachziehen; fremde Aufstellungen nur Ersteller oder ADMIN; Wechselsperre mit Nachzüglern; `PAKET` atomar mit einer NetzListe; Belegung je Platz; über den Draht Admin-Sitzung und frischer Code, Brett erscheint und verschwindet über `Mitgliedschaften`, `KontenVerzeichnis` nur mit `ZUWEISEN`, Beauftragung in der Ratengrenze der Verwaltung (Stoß 5, dann `GRENZE` ohne Trennung); an der Konsole übersteht ein Auftrag den Neustart, `sicherung` lässt ihn weg.
- P-56 Übergang 0.2.0 -> 0.3.0: ein Zustandsverzeichnis, das die Programmteile von 0.2.0 selbst geschrieben haben (`tests\Funk.Tests\Daten\zustand-0.2.0`), lädt der Server 0.3.0 ohne Verlust (jedes Feld jeder Datei mit demselben Wert, berechnete Rechte ausgenommen), mit den neuen Regeln danach; Clients 0.2.0 (funk-echo, echo-probe) bleiben an einem Server 0.3.0 verbunden, während Auftrag, Einheit TRUPP, Sperre und Entzug laufen (4.7).
- P-58 Einsatzphasen (0.5.0, `S2PhasenLageTests`, `S2DrahtTests`, `S2FormTests`): "2 Entern" stellt nur Rollen mit Variante um (Marines senden in Trupp Alpha, der CO hört mit, Pilot und Gunner ohne neue `kontoVersion`), Zusatznetze und "Hören aus" bleiben, die eigene Sendeauswahl aus dem Komfortzustand bleibt, zurück stellt her, VERALTET bei zwei Leitern, ein ADMIN ohne Funker-Modus wird bei einem Nicht-ADMIN ausgelassen, ohne `ZUWEISEN` und mit abgelaufenem Auftrag `KEIN_RECHT`, Offline nachgeführt, jeder Weg zum Platz in Phase 2 (Einsteigen, Verschieben, Aufstellung, Nachziehen, `SatzAnwenden`), Rückfall nach 12 h, 60 Wechsel hintereinander stimmig, die Grenzen beim Anlegen, die laufende Phase ändern und löschen, Paket atomar; über den Draht Anlegen nur in der Admin-Sitzung, `PhaseGeschaltet` an alle und nach Welcome, `Mitgliedschaften.phase`, `details {nr, durch}`, eine Flut endet bei `GRENZE` ohne Trennung, ein Client 0.5.0 schickt 0x003C und 0x003D keinem Server 0.4.0.
- P-59 Funker-Modus (0.5.0, `S2FunkerLageTests`, `S2DrahtTests`, Rangmatrix P-19 mit 84 Fällen): nur ein ADMIN für sich selbst, ein fremdes Konto im Body zählt nicht, die Rechte der Einsatzleitung ruhen je Aktion und je Paket, die Verwaltungsrechte bleiben, die Einsatzleitung setzt um, gibt frei und ersetzt, ein Beauftragter schaltet nicht stumm, Ablauf um 04:00 über den Takt, Ende bei Stufenwechsel und Sperre, Regel 7 beim Einschalten, das Brett verschwindet und erscheint ohne Neustart, Journal.
- P-60 Übergang 0.4.0 -> 0.5.0 (`Uebergang040Tests`): ein Zustandsverzeichnis, das die Programmteile von 0.4.0 selbst geschrieben haben (`tests\Funk.Tests\Daten\zustand-0.4.0`), lädt 0.5.0 ohne Verlust; Phasen und Funker-Modus auf diesem Stand, Speichern und Laden, Sicherung ohne Funker-Modus.
- P-61 Kanäle mit Ch. (0.6.0, `Stufe3KanalIdTests`, `Stufe3NetzListeTests`, `Stufe3FeldSetzenTests`, `Stufe3RolleWaehlenTests`, `Stufe3ChAufloesungTests`, `Stufe3WeiterleitungTests`, `Stufe3VerwaltungTests`): Kanal-ID hin und zurück an den Rändern (1022/Ch. 30 = 65502, n = 0, n = 1023, Ch. 31, Orgaweit), Prüfregeln von `kanaele`, `gruppe`, `ch` und `RolleWaehlen`, eine NetzListe 0.6.0 besteht die Prüfung von 0.5.x; FeldSetzen Regeln 1 bis 6 mit Ch.-Wechsel, Ruhe, Deckung und Sendeauswahl; RolleWaehlen mit Platz, Reservierung, Brett unverändert, Sperre der Selbstwahl ohne Wirkung, festen Feldern, nicht selbstwählbar, Phase und Wunsch; die Folge der Ch.-Auflösung auf jedem Weg; Sprache in Kanälen (Hörer je Kanal, Ruhe hört nichts, fremde und geratene Kanal-IDs, Stummschaltung und Taste über das Kanalnetz); Kanalnetz anlegen, kürzen, entfernen, löschen räumt jede Kanal-ID, Grenzen, gespeicherter Stand stimmig; Größen mit ausgeschöpften Werten.
- P-62 Versionstor und Übergang 0.5.0 -> 0.6.0 (`Stufe3VersionstorTests`, `Stufe3UebergangTests`): ohne Kanalnetz bleiben Clients 0.5.x verbunden; der Commit des ersten Kanalnetzes trennt sie mit `VERSION_ZU_ALT` (`mindest` 0.6.0), ein neues Hello unter 0.6.0 ebenso, Clients ab 0.6.0 bleiben; nach dem Löschen fällt das Tor. Ein Zustandsverzeichnis von 0.5.0 (`tests\Funk.Tests\Daten\zustand-0.5.0`) lädt ohne Verlust, Kanalnetze und Rollen darauf, Rückweg ohne Kanal-ID im Zustand. Angriffe: Flut von `FeldSetzen` und `RolleWaehlen` endet bei `GRENZE` ohne Trennung und ohne Wirkung der Abgewiesenen.
- P-63 Kanalsperren (0.6.0, Schritt 3.2, `Stufe3KanalsperreTests`, `Stufe3KanalsperreDrahtTests`, `SteuerGroessenTests.P02b_Stufe3_KanalsperrenMitgliedschaftenPraesenz`): Angriffe zuerst: ein Funker und die Führung ohne Auftrag sperren nicht und heben nicht auf, eine Einsatzleitung sperrt keinen ADMIN, auch nicht im Funker-Modus und nicht über eine Sperre für alle; Hören trotz Sperre per `HoerenSetzen`, Pakete und Durchsagen in einen gesperrten Kanal (auch als Mehrfachziel), Ch.-Wechsel und Leeren im gesperrten Feld, Satz und Sendeauswahl mit gesperrtem Kanal. Dazu Ziele (Global, Orgaweit, Ruhe, unbekannt), Ablauf im Heißpfad und im Takt, Ende mit dem Auftrag und dem Funker-Modus, Sperre und Phase, Sperre und `RolleWaehlen`, Grenzen 16, 64 und 500, Netz löschen und Ch. kürzen, Neustart und Sicherung; über den Draht Ablehnung, `Mitgliedschaften.gesperrt`, Praesenz-Details, Meldung, `details` von `FeldSetzen`, Journal, Sperre für alle, Ratengrenze und Server 0.5.0.
- P-64 Vorlage, Verwaltungsteil und Konsole mit Kanälen (0.6.0, Schritt 3.3, `Stufe3VorlageTests`, `Stufe3VerwaltungsteilTests`, `Stufe3KonsoleTests`, `AdminWerkzeugTabelleTests`; seit 0.13.0 mit der Vorlage Star Citizen): die Vorlage gegen die Vorgabe unter `tests\Funk.Tests\Daten\starcitizen` (zehn Kanäle mit ihren Ch., 14 Rollen mit ihren Feldern), 24 Schritte am Server neben der alten Flotte, alle Kanäle offen mit Sprachaktivierung, auch Kommando, Global ohne Ch.; Entwurf mit Kanälen (Regeln wie der Server, Vergleich nach Inhalt, Sicherung mit Ch. und Phasen, kein Ch. an einen Server 0.5.0); Reiter Kanäle mit Gruppe und Ch.-Liste, fester Ch. eines Rollenfelds, Rückfrage mit Versionstor; `netz-setzen --kanaele` und `--kanaele-datei`, `flotte-vorlage --name starcitizen` mit Versionstor und ohne zweiten Lauf. Bis 0.12.0 prüfte P-64 die alte Kanalvorlage gegen ihre Tabellen.
- P-57 Gegenprüfung Stufe 2 (01.10.2026, `RaidleitungGegenpruefungTests`): Bei wirkender Wechselsperre gibt ein Konto mit Platz ihn nicht per `SatzAnwenden` mit `rolleId: 0` auf (sonst stiege es als Nachzügler woanders ein); ein Platz, dessen Frist ohne Aktion abläuft, kommt über den Takt als Praesenz-Delta zum Brett; ein Client 0.3.0 schickt einem Server, der sich als 0.2.0 meldet, weder 0x0017 noch 0x0038 und bleibt verbunden; das Brett rechnet nach abgelaufenen Nachzüglern nicht mehr jede Sekunde neu.

Feld:
- P-50 Raidabend im Kleinen mit 5-10 Leuten (P1). Gemessen werden Wiederverbindungen, Verlust, Jitter, RTT, Tunnelanteil je Netzart, Rotationen, Mund-zu-Ohr-Latenz und die Zeit von der Raidleiter-Taste bis zum geänderten Hören, auch beim Sprechen während "Phase 2".

### 8.13 TOTP und Base32 (RFC 6238 Anhang B, RFC 4226 Anhang D)

Geheimnis ASCII `"12345678901234567890"`, Base32 `GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ`.

```
HOTP, 6 Ziffern, Zähler 0-9: 755224 287082 359152 969429 338314 254676 287922 162583 399871 520489

TOTP, HMAC-SHA-1, 8 Ziffern (die App zeigt davon die letzten 6):
Zeit (s)       T (hex)            Code
59             0000000000000001   94287082
1111111109     00000000023523ec   07081804
1111111111     00000000023523ed   14050471
1234567890     000000000273ef07   89005924
2000000000     0000000003f940aa   69279037
20000000000    0000000027bc86aa   65353130
```

Base32 nach RFC 4648 ohne `=`: "" -> "", "f" -> `MY`, "fo" -> `MZXQ`, "foo" -> `MZXW6`, "foob" -> `MZXW6YQ`, "fooba" -> `MZXW6YTB`, "foobar" -> `MZXW6YTBOI`. Dekodiert werden auch Kleinbuchstaben, Leerzeichen und `=` am Ende; übrige Bits ungleich 0 (`MZ`) sind ungültig. otpauth-URI für den Hauptnamen "Mustermann" mit diesem Geheimnis: `otpauth://totp/JG%20TacRelay%3AMustermann?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=JG%20TacRelay&algorithm=SHA1&digits=6&period=30`.

### 8.14 Unterschrift des Admin-Nachweises

Schlüssel wie 8.11 (d = 1, öffentlich = Basispunkt G, SPKI aus 8.5), Sitzung `0x1a2b3c4d`, Gerät 32 x `0x11`, Nonce `00..1f`, Zweck ANMELDUNG. Unterschrieben werden 95 Byte:

```
Daten             74616372656c61792061646d696e207631001a2b3c4d
                  1111111111111111111111111111111111111111111111111111111111111111
                  000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f
                  414e4d454c44554e47
Signatur (P1363)  37331cc28433ccb0605e334453cc3dbd7291e50e66dee8b375ea58e1ec7ec974
                  ed36f90621b124f439344a9d58d24b5e06a3c4ae6067b4117e6f85f0612e1e62
```

Wie in 8.11 ist das ein Prüfvektor, keiner zum Nachbilden. Gerechnet mit PowerShell 7 unter .NET (neu am 19.10.2026 für das Präfix von JG TacRelay), geprüft mit `Adminnachweis.Pruefen`: Die Signatur gilt; sie scheitert mit einer anderen Sitzung, einem anderen Gerät, einem gekippten Bit der Nonce, jedem anderen Zweck, einem anderen Schlüssel und im DER-Format.

### 8.15 Verschlüsselung eines TOTP-Geheimnisses

Schlüssel `funk-totp-schluessel` = `00..1f`, Nonce `a0..ab`, Konto 7, Generation 1, Geheimnis wie 8.13.

```
AAD               74616372656c617920746f7470207631000000000700000001
Nonce||Chiffrat   a0a1a2a3a4a5a6a7a8a9aaab d72a4f1970fd35875b55b6e1344ef5e847946020
||Tag             254dc67285b0568b9dd68299f48e4b65
```

Mit Konto 8 oder Generation 0 als AAD, mit einem gekippten Bit oder mit einem anderen Schlüssel scheitert die Entschlüsselung. Gerechnet mit PowerShell 7 (`AesGcm`), geprüft mit `TotpTresor.Entschluesseln`; die AAD bildet der Test von Hand nach, nicht über den Code.

## Anhang: Entscheidungen gegenüber den Recherchen

- Server-Identität per SPKI-Pin statt Let's Encrypt: kein Port 80, DynDNS-tauglich, Umzug ohne Zertifizierungsstelle, Rotation über Reserve-Pin und ein offline signiertes Serverprofil.
- Steuerkanal über `SslStream` mit eigenem Framing statt Kestrel oder WebSocket: keine HTTP-Oberfläche.
- Pfadprüfung vor jedem Adresswechsel mit Gegenprobe an die alte Adresse und Rückkehr bei neuem Höchstwert, nach dem Vorbild von RFC 9000 Abschnitt 9.3; strenger als WireGuard, das nur aus gültigen Paketen umbindet.
- Ein Zähler je Richtung über alle Epochen und Wege, ein Replay-Fenster.
- Ziele stehen ausdrücklich im Paket statt als vorab registrierte Sendeziele. Das vermeidet das VoiceTarget-Rennen.
- Einmalcode in umgekehrter Richtung gegenüber raidhelper.md B15: Der Client zeigt den Code, der Nutzer tippt ihn in Discord. Ein abgeschriebener Code ist so wertlos. Freigabe nie über Discord; die Operation `Entscheidung` entfällt. Der Bot ist ein eigener Prozess, nur ausgehend über das Gateway, ohne HTTP-Interactions-Endpunkt.
- Pro Konto höchstens eine Sitzung.
- Ping alle 2 s mit Totmeldung nach 6 s statt 15 s und 45 s (Skizze in server-sicherheit.md), weil eine gesunde, stille Verbindung sonst nicht von einer toten zu unterscheiden ist.
- KEEPALIVE alle 2 s statt 15-25 s (server-sicherheit.md B10, RFC 8085 Abschnitt 3.5): Pfadüberwachung einer aktiven Echtzeitsitzung, nicht bloßes Offenhalten eines NAT-Zustands.
- UDP-Port standardmäßig nicht 443.
- Sitzungsschlüssel binär statt als Base64 in JSON.
- Ein Relay-Thread statt paralleler Verarbeitung (server-sicherheit.md B21 nennt Parallelisierung für Szenario D); parallelisiert wird erst, wenn P-31 es verlangt.
- Rechte aus `leaderId` und `coLeaders` von Raid-Helper gibt es in v1 nicht (raidhelper.md empfahl sie).

## 9. Offene Punkte

1. Spike: Funktioniert ein TPM-Schlüssel (Microsoft Platform Crypto Provider, ECDSA P-256, persistiert) als Client-Zertifikat in `SslStream`, unter Windows 10 mit TLS 1.2 und unter Windows 11 mit TLS 1.3? Muss das Zertifikat dazu im Store CurrentUser/My liegen? Rückfall ist der Software-KSP. Dazu: Liegt der private Schlüssel beim Software-KSP wirklich außerhalb des eigenen Prozesses (CNG-Schlüsselisolation), sodass ein WER-Abbild ihn nicht enthält?
2. Wer in der Gruppe spielt noch unter Windows 10? Davon hängt ab, wie lange TLS 1.2 bleibt (Schalter `nurTls13`).
3. .NET-10-Details ohne Nachprüfung: `AllowTlsResume`, `CipherSuitesPolicy` mit TLS-1.2-Suiten unter Linux, Ablehnung doppelter JSON-Schlüssel (`AllowDuplicateProperties` bzw. strenge Voreinstellung, sonst eigener `Utf8JsonReader`-Vorlauf), `SocketFlags.Truncated` bei `ReceiveMessageFromAsync`, `SetRawSocketOption` für `TCP_NOTSENT_LOWAT`, Ausgabe von `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, Thread-Sicherheit von `AesGcm` (durch den Entwurf umgangen, aber für eine spätere Parallelisierung wichtig).
4. RFC-Stellen gegenlesen: RFC 8446 5.3 und 5.5, RFC 9001 6.6, RFC 9000 8.1, 9.3, 9.3.2 und 9.3.3, RFC 8085 3.5, RFC 4787, RFC 6479, RFC 6716 3.1, RFC 6562, RFC 3550 6.4.1, RFC 8305, RFC 8628 5.4. Das Zusammenfassungswerkzeug lieferte für RFC 9000 widersprüchliche Antworten; es braucht einen wörtlichen Abgleich.
5. Ports: Ist 443/TCP zu Hause frei (Foundry, Reverse Proxy)? Passt 47443/UDP? Wie sieht die Portfreigabe am Router aus?
6. Heimphase: Die Heim-IP sehen alle Mitglieder (DoS, Privatsphäre). Wann folgt der Umzug zum Anbieter oder ein Vorposten, der die Absenderadressen erhält?
7. Persistenz: SQLite (`Microsoft.Data.Sqlite`, NuGet) mit Gruppen-Commit oder eigene JSON-Dateien mit atomarem Ersetzen? Das ist eine Frage der Bordmittel.
8. Bot: Welche Bibliothek (NetCord oder Discord.Net, MIT, Eintrag in THIRD-PARTY-NOTICES)? Geht die Einzelabfrage `GET /guilds/{gilde}/members/{id}` für den Abgleich ohne das privilegierte Intent GUILD_MEMBERS (Spike)?
9. Datenschutzerklärung (Discord-Pflicht, DSGVO): Konto, Discord-ID, Discord-Benutzername, Geräte, Pegel, Ablage, Raid-Helper-Snapshot, Vorrangspur (HMAC-Präfixe, 14 Tage), Messwerte, Löschfristen (30 Tage ohne Gerät), Weg zur Löschung.
10. Fremde Durchsicht: Wer liest das Review-Paket (1.6) gegen, und wer rechnet die Testvektoren unabhängig nach (P-16)?
11. Alle Zahlenwerte sind Startwerte und müssen im Lasttest und am Raidabend bestätigt werden, besonders die neuen: Ping 2 s und Tot nach 6 s, KEEPALIVE 2 s, Umbindungsfrist 500 ms, Fremdbudget 60/s, globaler Handshake-Topf 200/s, Vorrangspur 14 Tage, Wiederverbindungsfrist 10 min, Reservierung 12 h, Opus-Grenze 400 Byte, Byte-Topf 16.000 B/s, Pacer 35 Mbit/s und Zuschlag 26 Byte, Warteschlange 20 ms, Empfangsalter 60 ms, Stabilitätsfrist 250 ms; dazu die alten (Fenster 2048, Rotation 60 min bzw. 2^20, Absenkung -12 dB).
12. A23 und A8: Welchen Mindestpegel für Global und Durchsage setzt der Client durch (C7)?
13. Raid-Helper: Premium, Template, Comp-Tool, die Abbildung className/specName auf Rolle und Schiff, ob ein Server-Key für `RaidHelperEvents` vorhanden ist, und ob `leaderId` und `coLeaders` später automatisch Rechte bekommen (v1: nein).
14. Automatische Gerätefreigabe über eine Discord-Rolle (v1: nein), TPM-Attestation zum Beleg der Schlüsselart (v2) und eine Freigabe für Geräte privilegierter Konten durch zwei Admins im Client statt nur über das Admin-Werkzeug (v2).
15. Sollen auch Geräte von RAIDLEITER-Konten nur über das Admin-Werkzeug laufen, wie ein Prüfer empfiehlt? v1 erlaubt den Bot mit zusätzlicher Rückfrage beim Admin, weil das Admin-Werkzeug in der Heimphase nur im LAN erreichbar ist.
16. Mehrere gleichzeitige Geräte je Konto (etwa ein Zweitgerät für das Overlay): v1 verdrängt. Gewünscht?
17. Durchsage mit Stille (Server hält andere Netze an) als Option für v2?
18. Autoritative DNS-Auflösung mit eigenem kleinem DNS-Client (7.1) oder nur `DnsQuery_W` mit Cache-Umgehung: Spike, Nutzen gegen zusätzlichen Parser-Code abwägen.
19. IPv6 im Heimbetrieb (Router-Freigabe, wechselndes Präfix, LXC).
20. Notfallplan bei einem Serverausfall im Einsatz (Discord-Voice als Rückfall), mit der Gruppe absprechen.
21. Profilschlüssel (Pflicht vor v1): Wer erzeugt und verwahrt ihn offline, samt Reserve? Eigener Schlüssel oder derselbe wie für das Update-Manifest (getrennt ist besser)? Wo liegt der offline erzeugte Reserve-Serverschlüssel?
22. Programmname und damit ALPN-Kennzeichen: seit 19.10.2026 entschieden (JG TacRelay, `tacrelay/1`).
23. FRITZ!Box: tatsächlicher Netto-Upload, Wirkung der Priorisierung als Echtzeitanwendung, Uhrzeit der automatischen Neuverbindung.
24. Kernel-Zeitstempel (`SO_TIMESTAMPNS`) für das Empfangsalter gehen in .NET nur per P/Invoke auf `recvmsg`: Spike, ob sich das lohnt.
25. Verbindungsaufbau nach RFC 8305 ist in .NET nicht eingebaut und muss selbst gebaut werden: Aufwand, oder in der Heimphase (nur IPv4) zurückstellen.
26. Namenstäuschung: Reicht der eindeutige Discord-Benutzername in Freigabe- und Sprecheranzeige, oder braucht es in v2 eine Verwechslerprüfung nach Unicode UTS #39?
27. Reicht ein Relay-Thread für Szenario D beim Anbieter, oder muss dort parallelisiert werden (P-31)?
28. Stufe 1, Fragen an Sarevoc: Sarevocs Antworten vom 30.09.2026 (heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Rechte und Verwaltung") bestätigen Frage 1 (Admins ernennt nur Sarevoc an der Konsole) und Frage 5 (Untergilden pflegt nur ein Admin). Frage 2 (die Authenticator-App auch unter Windows, mit Sperre der App) steht im Hinweis des Verwaltungsteils; seit 0.6.4 ist die App frei wählbar (Sarevoc, 04.10.2026). Seit Stufe 1, Teil 2 eingebaut (3.3, 4.5, 6.13, P-53): Frage 3 mit `karteFreigeben` und Discord-Namen ohne täuschende Zeichen, der eindeutige Hauptname steht daneben; Frage 4 als reines Ergänzen des Katalogs durch Konten mit `ZUWEISEN` oder `GLOBAL_SENDEN`. Offen bleibt, ob `mitgliedskarte.rollenFuerAlle` neben der Freigabe je Nutzer noch gebraucht wird, und eine Verwechslerprüfung für Discord-Namen (Punkt 26). Der ursprüngliche Stand vor dem Einbau: Frage 3 ("Wer ist da" zeigt den Discord-Namen als Namen, dazu Untergilde, Zeichen und Rollen für alle, außer ein Nutzer hat die Anzeige nicht freigegeben; Freigabe je Nutzer, ab Werk an) und Frage 4 (den Rollenkatalog legt auch die Führung oder Einsatzleitung an). Bis dahin gelten `mitgliedskarte.rollenFuerAlle` und der Katalog nur in der Admin-Sitzung. Vor dem Einbau zu klären: Wird der Discord-Name der angezeigte Name, braucht er Schutz vor Täuschung (unsichtbare Zeichen wie U+200B, Eindeutigkeit wie bei den SC-Namen); und SETZEN durch die Führung darf keine Einträge entfernen, sonst verschwinden mit ihnen die Mitgliederrollen, die nur der Admin vergibt. RAIDLEITER behielt `ZUWEISEN` bis 0.2.0 (seit 0.3.0 nicht mehr, 6.1).
29. Clientseite der Stufe 1: gebaut sind der Verwaltungsteil als eigene Programmdatei (nur im Admin-Setup, geladen nach Prüfsumme) mit Admin-Schlüssel im TPM ohne Rückfall, eigenem QR-Code, Code-Karte mit automatischer Wiederholung nach `SCHRITT_BESTAETIGEN`, dazu die Setup-Seite "Deine Namen". Offen: der Admin-Schlüssel im echten TPM ist ungeprüft (Tests nur mit flüchtigen Schlüsseln), der Probefunk mit Test-Admin-Schlüssel, das Journal im Verwaltungsteil und die geführte Einrichtung (M6).
30. Ratengrenze der Admin-Nachrichten (1/s, Burst 3): AdminNonce hat seit Stufe 1, Teil 2 einen eigenen Eimer (4.8). Am Raidabend bestätigen, dass eine Oberfläche nach Tippfehlern nicht an die Grenze stößt.
31. Zeitsynchronisierung am Host ist Pflicht für TOTP (Fenster nur einen Schritt); Prüfung beim Start oder nur Hinweis in BETRIEB.md?
32. Beauftragung (Stufe 2): Seit 0.3.0 eingebaut, Teil Server und Protokoll (6.12, 0x0017, 0x0038, 0x4003, `Verwaltung PAKET`, Rangregel 6.10). Offen: die Clientseite (Brett, "Einsteigen" mit "wie letztes Mal", Bereich Einsatzleitung und Flotten-Einstellungsseite im Verwaltungsteil, Zusatzbestätigung bei der Gerätefreigabe für Konten mit Auftrag); der Bot-Dienst für die Nachricht in den Admin-Kanal; Sicherungen außerhalb von `funk-admin sicherung` (Proxmox-Sicherung des Containers, eine Handkopie von `lage.json`) tragen Aufträge mit, beim Zurückspielen also entziehen; die Annahmen der Vorlage Star Citizen (`flotte-vorlage`) prüft der Admin.
33. Praesenz voll liegt im ungünstigsten Fall bei Faktor 1,73 (mit allen 500 Kanalsperren 1,64, Stufe 3) unter der Rahmengrenze statt über 2 (4.10). Wenn es in der Praxis eng wird: in Teilen senden oder die Details nur auf Anfrage.
34. Ausbaustufe S2 (0.5.0), Teil Server und Protokoll gebaut (6.14, 6.15). Offen: die Clientseite (Funker-Modus in *Ich*, Phasen im Verwaltungsteil und am Brett, Phasentasten, Meldezeile); der Rückweg auf einen Server 0.4.x verliert Phasen und Funker-Modus beim ersten Schreiben (4.7, gemessen), und die Sicherung der Flotte im Verwaltungsteil nimmt die Phasen noch nicht mit; ein Phasenwechsel schickt Steuerrahmen am Pacer vorbei genau im Moment des "Entern!" (7.4), Größen nicht gemessen; eine Konsole für Phasen gibt es nicht (Rückfall: Verwaltungsteil, "Phase zurück", Rückfall nach 12 h).
35. Stufe 3 (0.6.0), Schritte 3.0 bis 3.3 gebaut (6.16, 6.17, Kanalvorlage, Verwaltungsteil, Konsole): Offen sind der Client (3.4, 3.5), Last und Kreuzproben mit den gebauten Programmen (3.6). Die Ch.-Listen der alten Kanalvorlage trugen Annahmen; sie sind mit ihr entfallen, die Vorlage Star Citizen nennt ihre Annahmen selbst, und der Admin prüft sie. Global-Kanäle ("Global 1" bis "Global 20") erlauben seit 3.3 Sprachaktivierung (Annahme, Orgaweit bleibt nur Taste); Sarevoc bestätigt das. Vorab-Setups 0.6.0 aus Stufe 2 melden schon 0.6.0 und kämen durch das Versionstor, kennen aber keine Kanäle: vor der ersten Übernahme von Kanalnetzen durch Setups ab Schritt 3.4 ersetzen.
