## Teil B: Handbuch für Admins und IT

Dieser Teil ist für alle, die den Funkserver von JG TacRelay betreiben, prüfen oder auf einen anderen Server umziehen: Admins
der Gruppe, die IT (Anf. A57) und den Betreiber eines gemieteten Servers. Er beschreibt den Server so
vollständig, dass du ihn ohne weitere Unterlagen einrichten, pflegen, sichern, umziehen und reparieren kannst, mit allen
Befehlen. Was die Mitglieder im Programm sehen und tun, steht in Teil A, warum der Server so gebaut ist, in Teil C,
wie viel Leitung und Rechenzeit er braucht, in Teil D, und was rechtlich zu beachten ist, in Teil F.

## B0 Lesehilfe

### B0.1 Stand

Der Stand für das ganze Handbuch steht in 1.9. Für diesen Teil heißt das:

- Die Serverprogramme `funk-server`, `funk-admin`, `funk-echo` und `funk-testclient` haben im Code die Fassung 0.5.0
  (Stand `95aa78b`, 02.10.2026), C# auf .NET 10, framework-abhängig. Serverpaket, Gesamtpaket und
  Setups 0.5.0 sind am 02.10.2026 gebaut und geprüft, aber nie ausgeführt (1.9).
- Neu in 0.5.0 am Server: Einsatzphasen und "Heute als Funker" (B1.8, B10.7, B14.3), die Voreinstellung Sprachaktivierung
  für Netze, die du mit `netz-setzen` neu anlegst (B12.1), und "der Eigentümer" statt eines Namens in den Ausgaben der
  Programme. Eine `server.json` und die Units brauchen nichts Neues (B6, B7). Das Update von 0.4.0 steht in B19.6.
- Fassung 0.4.1 hat nur den Client geändert (die Vorprüfung für Admin-PCs nach Anf. A56, B10.6, und die Hörprobe, A3.4).
  Server- und Protokollcode von 0.4.1 sind die von 0.4.0.
- Auch Fassung 0.10.0 (Einstellungen einfach, 12.10.2026) ändert nur den Client (Teil A, 1.9); am Server, an `server.json`,
  den Units und `funk-admin` ändert sie nichts.
- Fassung 0.11.2 (13.10.2026) ändert am Server eine Sache: Ein Konto steht auch dann in der Präsenz, wenn es offline ist,
  keinen Platz hat und eine frische Reservierung trägt (die Einsatzleitung hat es vorab in Kanäle gezogen), höchstens 12
  Stunden lang wie jede Reservierung. So zeigt die Funkfeld-Zuteilung des Clients 0.11.2 auch diese Leute in ihrem Baum (A12.3).
  Kein neues Feld, kein neuer Nachrichtentyp, nichts Neues in `lage.json`; ältere Clients bekommen den Eintrag als offline
  und ohne Platz (ohne Platzstatus, Einheit und Rolle; `Abbildung.cs`). `server.json`, Units und `funk-admin` bleiben, wie
  sie sind.
- Fassung 0.11.7 (15.10.2026) bringt Abzeichen (B11.7): die neue Anfrage **Abzeichen** (0x0025) in der Admin-Sitzung, neue
  Felder in `mitglieder.json` (eine alte lädt unverändert), dazu Abzeichen in Präsenz und Mitgliedschaften. Client und Server
  gemeinsam aktualisieren. `server.json`, Units und `funk-admin` bleiben, wie sie sind.
- Fassung 0.11.8 (15.10.2026): Untergilden entfallen (B11.8). Der Server löscht keine gespeicherten Untergilden, er zeigt
  und ändert sie nur nicht mehr; ältere Clients bekommen eine leere Liste. `server.json`, Units und `funk-admin` bleiben,
  wie sie sind.
- Fassung 0.12.0 (16.10.2026) bringt das Meldesystem (B11.9): die Anfragen **MeldungSenden** (0x0026) und **BereitMelden**
  (0x0027), die Pushes **Meldestand** (0x4005) und **BereitStand** (0x4006), den Haken **darf Meldungen senden** in
  `mitglieder.json` (eine alte lädt ohne ihn). Client und Server gemeinsam aktualisieren (`BETRIEB.md`, "Update auf
  0.12.0"); ältere Clients funken weiter, sehen aber keine Meldungen. `server.json`, Units und `funk-admin` bleiben, wie
  sie sind.
- Der Heimserver von Sarevoc ist von Hand nach `HEIMSERVER-PROXMOX.md` eingerichtet, nicht mit dem Skript; das ist
  wichtig für Updates (B19.3). Solange er läuft, ist Sarevoc dort Admin und Eigentümer (Anf. A41). Laut Auftraggeber
  (02.10.2026) läuft dort der Server 0.4.0 auf Debian 13 im Container 180; am Server selbst nicht nachgesehen. Das Update
  auf 0.5.0 steht aus. Die Container-Nummer 150 in den Beispielen dieses Teils ist nur das Beispiel aus
  `HEIMSERVER-PROXMOX.md`.

### B0.2 Wie Befehle hier stehen

In Tabellen und Beispielen steht kurz `funk-admin <befehl>`. Wie du das an deinem Rechner aufrufst, hängt davon ab, wie
der Server eingerichtet wurde:

| Wo | Aufruf |
| --- | --- |
| Server, eingerichtet mit `funk-einrichten.sh` | `sudo funk-admin <befehl>`; der Kurzbefehl `/usr/local/sbin/funk-admin` setzt `--konfig /etc/funk/server.json` selbst |
| Server, von Hand eingerichtet (`HEIMSERVER-PROXMOX.md`, so der Heimserver) | `dotnet /opt/funk/funk-admin.dll <befehl> --konfig /etc/funk/server.json`, als root |
| Offline-Rechner, Ordner `funk-offline` auf dem Stick | `dotnet funk-admin.dll <befehl>` in PowerShell, im Ordner `funk-offline` |

Bist du schon root (etwa im Container nach `pct enter`), lässt du `sudo` weg.

Platzhalter:

| Platzhalter | Bedeutung |
| --- | --- |
| `<id>` | Konto-ID, die erste Zahl jeder Zeile von `funk-admin konten` |
| `XXXX-XXXX-XXXX-XXXX` | Anzeigeform eines Fingerabdrucks: Gerätekennung, Admin-Kennung oder Kennung eines Pins (16 Zeichen in Vierergruppen) |
| `<CT-Nummer>` | Nummer des Proxmox-Containers; die Beispiele nehmen 150 wie `HEIMSERVER-PROXMOX.md` |
| `funk.example.org` | Beispielname für einen gemieteten Server, wie in `EINRICHTEN.md` und `UMZUG.md` |

| `203.0.113.10` | Beispieladresse eines gemieteten Servers (Bereich für Dokumentation, keine echte Adresse) |
| `192.168.1.250` | feste LAN-Adresse des Containers im Heimnetz (Beispiel aus `HEIMSERVER-PROXMOX.md`) |

Meldungen der Programme stehen hier wörtlich, wie Fassung 0.5.0 sie ausgibt. Sie sagen "der Eigentümer" (bis 0.4.1 nannten
sie an dieser Stelle einen Rufnamen); gemeint ist, wer den Profilschlüssel hält und die Serverprofile unterschreibt (B0.4). Die Beispielnamen sind Max Mustermann
mit dem SC-Namen `Mustermann` und dem Anmeldenamen (Discord-Namen) `Mustermann/Max`; das Konto hat den Anzeigenamen `Max`.

### B0.3 Wie belegt ist, was hier steht

Jede Zahl und jeder Befehl stammt aus dem Code von Fassung 0.5.0 (Stand `95aa78b`) oder aus gemessener Doku im Repository;
die Meldungen der Programme wurden für 0.5.0 gegen den Code gelesen, nicht alle neu ausgeführt. Die Kennzeichen
erklärt die Lesehilfe im Titelteil; in diesem Teil kommen vor:

- **gemessen**: mit den gebauten Programmen ausprobiert, meist auf dem Entwicklungsrechner unter Windows, ohne TLS.
- **am Code geprüft**: aus dem Quelltext abgelesen, nicht mit dem laufenden Programm ausprobiert.
- **auf Linux nicht gemessen**: Laut Projektstand sind unter Linux noch ungeprüft: `funk-einrichten.sh` als Ganzes, chown,
  Modus 0600, `timedatectl`, die Bindung an Ports unter 1024, die Prüfung des Verwaltungssockets und das Kopieren der
  Programmdateien auf Debian und Ubuntu. Vor dem ersten echten Einsatz des Skripts kommt der Probelauf im
  Wegwerf-Container (B5.8).
- **Annahme**: ausdrücklich so gekennzeichnet.

Die Ausführungen zu Protokoll und Sicherheit stützen sich auf `FUNKPROTOKOLL-v1.md`; "Protokoll 3.1" heißt Abschnitt 3.1
dort.

### B0.4 Eigene Gruppe: eigener Server, eigene Schlüssel

JG TacRelay ist quelloffen (Apache-2.0, F1). Eine Gruppe, die es für sich betreiben will, braucht dafür nichts von
Sarevoc und bekommt auch nichts von ihm: keinen Schlüssel, kein Profil, kein Passwort. Sie erzeugt alles selbst, und
wer ihren Profilschlüssel hält, ist für ihre Clients "der Eigentümer". Kein Schlüssel und kein Geheimnis wird je
zwischen Gruppen weitergegeben.

| Schritt | Was | Wo |
| --- | --- | --- |
| 1 | Den Quelltext holen und bauen; wie, steht in `README.md` | 1.10 |
| 2 | Am Offline-Rechner den eigenen Profilschlüssel erzeugen (`funk-admin profilschluessel-erzeugen`), dazu den Reserveschlüssel (`schluessel-erzeugen`); beides bleibt auf einem verschlüsselten Stick, nie auf dem Server | B1.6, B2.2, C7 |
| 3 | Den öffentlichen Teil des Profilschlüssels (eine Zeile, nicht geheim) in `konfig\profilschluessel.txt` eintragen und damit Setup und Admin-Setup selbst bauen; beim Bau kommt er fest in den Client | B8 |
| 4 | Den Server einrichten, daheim im Proxmox-Container oder auf einem gemieteten Server; Serverschlüssel, Betriebsgeheimnis und TOTP-Schlüssel entstehen dabei auf dem Server | B3 oder B4, B5 |
| 5 | Das Serverprofil mit dem eigenen Profilschlüssel unterschreiben und dem Setup beilegen | A16, B8 |
| 6 | Den ersten Admin ernennen und seinen Zweitfaktor einrichten | A17, B10 |
| 7 | Die Kanalvorlage wählen (ab Werk Star Citizen) oder eigene Kanäle und Rollen anlegen | B12 bis B14 |

Ein Client vertraut nur den Profilschlüsseln, die beim Bau in ihm stehen. Ein Setup, das für Sarevocs Server gebaut ist,
verbindet deshalb nicht mit dem Server einer anderen Gruppe, und umgekehrt; ohne eingebauten Profilschlüssel geht nur der
Probefunk. Das ist Absicht: So kann niemand die Mitglieder einer Gruppe mit einem fremden Profil auf einen anderen Server
lenken (C7).

## B1 Architektur

### B1.1 Überblick

```
Client (Windows)                                  Server (Linux, systemd)
JG TacRelay  --- TCP, TLS 1.3/1.2 ----------->  funk-server: Steuerkanal
                  ALPN tacrelay/1            (Konten, Geräte, Rechte, Netze, Plätze)
JG TacRelay  --- UDP, AES-256-GCM ----------->  funk-server: Sprachrelais
                  Rückfall: Tunnel im             (prüfen, Senderecht, stempeln,
                  Steuerkanal                      neu versiegeln, Pacer)
funk-admin    --- UNIX-Socket (Datei) -------->  funk-server: Verwaltung
                  nur auf dem Server
```

- Nach außen gibt es genau einen TCP-Port und einen UDP-Port je Bindadresse. Kein HTTP, keine Web-Oberfläche, kein
  Admin-Port. Verwaltet wird nur an der Konsole des Servers (Verwaltungssocket) und im Client über den Steuerkanal
  (Verwaltungsteil, nur mit Admin-Schlüssel und Zweitfaktor).
- Der Server dekodiert, mischt und transkodiert keine Sprache; er reicht Opus unverändert durch. Die Verschlüsselung ist
  Hop-by-Hop: Der Server sieht die Opus-Nutzlast im Klartext, eine Ende-zu-Ende-Verschlüsselung gibt es in v1 nicht (Protokoll 1.2).
- Die Clients erkennen den Server allein an seinem Pin (SHA-256 über den öffentlichen Schlüssel des Zertifikats). Name,
  Kette und Ablauf des Zertifikats prüfen sie nicht. Deshalb braucht es keine Zertifizierungsstelle, kein Let's Encrypt
  und keinen Port 80 (Protokoll 3.1).

### B1.2 Prozesse

| Prozess | Benutzer | Aufgabe | Start |
| --- | --- | --- | --- |
| `funk-server` | `funk` (Systembenutzer ohne Anmeldung) | Steuerkanal, Sprachrelais, Verwaltungssocket, Zustand | `funk-server.service` |
| `funk-echo` (wahlweise) | `funk-echo` | Echo-Funker: ein gewöhnlicher Teilnehmer, der jede Sprechphase im Netz Echo nach 500 ms zurückspielt | `funk-echo.service` |
| `funk-admin` | root (sudo) oder `funk` | Admin-Werkzeug: offline mit Dateien oder über den Verwaltungssocket | nur beim Aufruf |
| `funk-testclient` | beliebig | Prüfprogramm, auf dem Server nur `echo-probe` | nur beim Aufruf |

Im Prozess `funk-server` arbeiten: die TLS-Annahme, je Verbindung eine Lese- und eine Schreibschleife, die Zentrale als
einziger Schreiber für Kontenbuch, Einmalcodes und Zustand (Gruppen-Commit), genau ein Relay-Thread für den Sprachpfad
und je UDP-Socket ein Empfangs- und ein Sende-Thread mit Pacer (Voreinstellung 35 Mbit/s). Alle vier Programme sind
portable DLLs ohne Programmstarter und laufen mit `dotnet <name>.dll` auf der installierten .NET-Laufzeit.

### B1.3 Ports und Protokolle

| Weg | Voreinstellung | Was darüber läuft | Eingestellt in |
| --- | --- | --- | --- |
| TCP | Bindport 8443 | TLS 1.3 und 1.2, ALPN `tacrelay/1`, Steuerkanal; bei gesperrtem UDP auch die Sprache als Tunnel | `tcpPort`, `oeffentlicherTcpPort` in `server.json`; der öffentliche Port steht im Serverprofil |
| UDP | 47443 | Sprache, AES-256-GCM je Paket, Schlüssel aus dem Steuerkanal | `udpPort`, `oeffentlicherUdpPort`; den öffentlichen nennt der Server den Clients in Welcome |
| UNIX-Socket | `/run/funk/verwaltung.sock` | `funk-admin`, eine Zeile JSON hin, eine zurück | `verwaltungsSocket` |

- TCP und UDP binden je Adresse aus `binden` mit gleicher IP (Protokoll 4.1). An alle Adressen (`0.0.0.0`, `::`) bindet der Server
  nur mit `"alleAdressen": true` und warnt dann im Logbuch.
- Bindport und öffentlicher Port sind getrennt: Eine Weiterleitung wie außen 443 auf innen 8443 am Router ist zulässig.
- UDP nicht auf 443: Manche Firmen-, Hotel- und Uni-Netze behandeln UDP 443 als QUIC und verwerfen es.
- TLS unter Linux nur mit diesen Suiten (Protokoll 4.1): TLS 1.3 `TLS_AES_256_GCM_SHA384`, `TLS_AES_128_GCM_SHA256`,
  `TLS_CHACHA20_POLY1305_SHA256`; TLS 1.2 nur `TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384` und
  `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256`. Ohne vereinbartes ALPN trennt der Server nach dem Handshake. TLS 1.2 bleibt
  für Windows-10-Clients an, bis `nurTls13` auf `true` steht.
- Geordnetes Beenden (SIGTERM): Der Server schickt allen Verbindungen `Bye{WARTUNG, wiederInMs: 10000}`, auch in
  REGISTRIERUNG und WARTERAUM (Protokoll 7.2). Die Clients kommen danach von selbst wieder.

### B1.4 Dateien und Verzeichnisse

| Pfad | Inhalt | Besitzer, Rechte | Wer legt an |
| --- | --- | --- | --- |
| `/opt/funk/` | Programme, `konfig/` mit Units, Beispielen, `profilschluessel.txt` und `fail2ban/`, Anleitungen | root, für andere nicht beschreibbar (`chmod -R go-w`) | `funk-einrichten.sh` oder `tar` von Hand |
| `/opt/funk.<fassung>/` | die vorigen Programme nach einem Update mit dem Skript | root | `funk-einrichten.sh` |
| `/etc/funk/server.json` | Konfiguration (B6), keine Geheimnisse | root, 0644 | `einrichten` beim ersten Lauf, sonst von Hand |
| `/etc/funk/profil.json` | signiertes Serverprofil, öffentlich | root, 0644 | `einrichten --profil` oder von Hand |
| `/etc/funk/profil-v<N>.json` | die vorige Fassung des Profils nach dem Einspielen einer neuen | root | `einrichten --profil` |
| `/etc/funk/profilschluessel.txt` | öffentlicher Profilschlüssel (SPKI, Zeile mit `MFkw`), damit `pruefen` die Signatur prüft | root, 0644 | `einrichten` oder `cp` |
| `/etc/funk/profilanfrage.txt` | die Zeile `TR-PROFILANFRAGE v1 ...`, solange das Profil fehlt | root, 0644 | `einrichten` |
| `/etc/funk/echo.env` | `FUNK_ECHO_ADRESSE=<adresse>` für den Echo-Funker | root, 0644 | `einrichten` oder `echo` |
| `/etc/funk/credentials/` | `server.crt`, `server.key`, `funk-geheimnis`, `funk-totp-schluessel` | root, Ordner 0700, Dateien 0600 | `einrichten` oder `funk-admin ...-erzeugen` |
| `/var/lib/funk/` | Zustand (Tabelle unten), `sicherungen/<JJJJMMTT-HHmmss>/` | `funk`, 0700 | systemd (`StateDirectory`) |
| `/run/funk/verwaltung.sock` | Verwaltungssocket | `funk`, Socket 0600, Ordner 0700 | systemd (`RuntimeDirectory`) und Server |
| `/var/log/funk/sperren.log` | nur wahlweise: Klartextzeilen der Sperrliste für fail2ban (B16) | `funk`, Ordner 0750 | systemd (`LogsDirectory`) und Server |
| `/var/lib/funk-echo/geraet.pem` | Geräteschlüssel des Echo-Funkers | `funk-echo`, Ordner 0700 | `funk-echo` beim ersten Start |
| `/etc/systemd/system/funk-server.service`, `funk-echo.service` | Units (B7) | root, 0644 | `einrichten` oder `cp` |
| `/etc/systemd/system/funk-server.service.d/10-port-unter-1024.conf` | Drop-in für Ports unter 1024 | root, 0644 | `einrichten` |
| `/usr/local/sbin/funk-admin` | Kurzbefehl | root, 0755 | `einrichten` |
| `/run/credentials/funk-server.service/` | die Schlüsseldateien zur Laufzeit, von systemd bereitgestellt (`$CREDENTIALS_DIRECTORY`) | nur für den Dienst | systemd (`LoadCredential`) |

Die Zustandsdateien in `/var/lib/funk`. Jede wird atomar ersetzt (Zwischendatei, Flush, Umbenennen):

| Datei | Inhalt | in `sicherung` | in `umzug-packen` |
| --- | --- | --- | --- |
| `lage.json` | Netze, Einheiten, Rollen, Konten mit Feldern und Plätzen, Aufträge, Aufstellungen, ab 0.5.0 die Phasen, die laufende Phase und je Konto `alsFunkerBis` (Funker-Modus) | ja, ohne Aufträge und ohne Funker-Modus; Phasen und laufende Phase bleiben drin | ja, mit laufenden Aufträgen, Funker-Modus und laufender Phase |
| `konten.json` | Konten, Anträge, Geräte | ja | ja |
| `komfort.json` | Pegel, Ablage, Sendeauswahl, Regeltabelle | ja | ja |
| `vorrangspur.json` | Präfixe der Mitglieder, nur als HMAC-Werte | ja | nein |
| `sperrliste.json` | Sperren, nur HMAC-Werte und Kennungen | nein | nein |
| `admins.json` | Admin-Nachweise, TOTP-Geheimnisse nur verschlüsselt, Notfallcodes nur als Prüfwerte (0600) | ja | ja |
| `mitglieder.json` | Rollenkatalog, Vormerkungen, Mitgliederrollen, Wünsche, Abzeichen; Untergilden aus der Zeit vor 0.11.8 nur unverändert mitgeführt (0600) | ja | ja |
| `journal.json` | Verwaltungsjournal mit Klarnamen, 90 Tage (0600) | ja | ja |

`admins.json` entsteht mit dem ersten `admin-vorsehen`, `mitglieder.json` mit der ersten Pflege von Mitgliedern, Rollen
oder Abzeichen. Es ist normal, wenn eine davon auf einem jungen Server fehlt.

### B1.5 Verwaltungssocket

- Ein UNIX-Socket, also eine Datei und keine Netzadresse. Unter Linux hat er die Rechte 0600 und gehört dem
  Dienstbenutzer `funk`.
- Zusätzlich prüft der Server bei jeder Verbindung die UID der Gegenseite (`SO_PEERCRED`). Zugelassen sind root, der
  Dienstbenutzer und die UIDs aus `verwaltungsUids`. Jede andere Verbindung schließt er und schreibt
  `Verwaltungssocket: Gegenseite mit nicht zugelassener UID abgewiesen` ins Logbuch.
- Je Verbindung geht eine Zeile JSON hin (`{befehl, argumente}`, höchstens 64 KiB) und eine zurück (`{ok, text, daten}`).
  `funk-admin` wartet 30 s auf die Antwort, bei `verknuepfen` 140 s. Mit `--json-aus` gibt es die Antwort als JSON aus.
- **Konsole heißt Eigentümer:** Wer am Socket ist, darf alles, auch den Eigentümer neu setzen und Admins ernennen (Protokoll 3.10).
  Über das Funkprotokoll führt kein Weg zu diesen Befehlen. Deshalb ist root auf dem Server (und bei einem Container der
  Zugang zum Proxmox-Host) das wertvollste Gut. Weitere UIDs in `verwaltungsUids` bekommen dieselben Rechte; der Server
  warnt beim Start, solange die Liste nicht leer ist.
- Unter Windows (nur Entwicklung) prüft der Socket die Gegenseite nicht.

### B1.6 Schlüssel, Geheimnisse und Vertrauen

| Name | Liegt | Erzeugt mit | Wofür | Wenn er verloren geht oder ersetzt wird |
| --- | --- | --- | --- | --- |
| `server.key`, `server.crt` | `/etc/funk/credentials` | `schluessel-erzeugen` oder `einrichten` | Identität des Servers: ECDSA P-256, selbstsigniert, 20 Jahre | neuer Pin; die Clients nehmen den Server erst an, wenn der neue Pin im Profil steht (`profil-anfrage` an Sarevoc) |
| `funk-geheimnis` | `/etc/funk/credentials` | `geheimnis-erzeugen` oder `einrichten` | 32 Zufallsbytes; daraus die HMAC-Schlüssel für Schwärzung im Logbuch, Vorrangspur und Sperrliste | Vorrangspur fängt leer an, die Schwärzung passt nicht zu älteren Logzeilen |
| `funk-totp-schluessel` | `/etc/funk/credentials` | `totp-schluessel-erzeugen` oder `einrichten` | 32 Byte; verschlüsselt die TOTP-Geheimnisse in `admins.json` (AES-256-GCM), Schlüssel für die Prüfwerte der Notfallcodes | jeder eingerichtete Zweitfaktor ist unlesbar; jeder Admin richtet ihn an der Konsole neu ein (`admin-einrichtung-neu`) |
| `profil.key` | Stick, Ordner `funk-offline`, nie auf dem Server | `profilschluessel-erzeugen` | unterschreibt Serverprofile; der öffentliche Teil ist in jeden Client eingebaut | kein neues Profil mehr; ein neuer Profilschlüssel wirkt erst, wenn jeder ein Setup mit ihm hat |
| `reserve.key`, `reserve.crt` | Stick | `schluessel-erzeugen` am Offline-Rechner | Reserve-Serverschlüssel, sein Pin steht in jedem Profil | Reserve fort; eine neue trägt `profil-erneuern` noch nicht ein |
| Geräteschlüssel | Client, im TPM (sonst Software-Speicher), nicht exportierbar | Client | Anmeldung jedes Geräts (Gerätezertifikat im TLS) | Gerät neu anmelden und freigeben |
| Admin-Schlüssel | Client, nur im TPM, nicht exportierbar | Verwaltungsteil ("Verwaltung einrichten") | Besitznachweis für Admin-Sitzungen | neu ernennen (`admin-ernennen`), den alten entfernen (`admin-schluessel-entfernen`) |

Was davon geheim ist, was nicht und was bei Verlust oder Diebstahl folgt, ordnet C5 ein.

Geheimnisse liegen nie in `server.json`; der Dienst bekommt sie über `LoadCredential` von systemd. Die Befehle, die
Schlüssel oder Geheimnisse erzeugen, ersetzen seit 0.4.0 keine vorhandene Datei: Sie enden mit `<pfad> gibt es schon.
Nichts geändert.` und Exit 3, dahinter steht, was ein Ersetzen kostet. Ersetzt wird nur mit `--ersetzen` (B9.2).

### B1.7 Logbuch und Journal

- **Logbuch:** Der Server schreibt nach stdout, unter systemd also ins Journal (`journalctl -u funk-server`), wahlweise
  zusätzlich in eine Datei (`log.datei`). Eine Zeile je Eintrag mit UTC-Zeit und Stufe. IP-Adressen, Präfixe und
  Konto-IDs erscheinen nur als gekürzter HMAC-Wert (`ip#...`, `p4#...`, `k#...`); Schlüssel, IVs, Codes und Opus nie.
  Alle 10 s eine Messwertzeile je aktiver Sitzung. Ein Phasenwechsel (B1.8) steht als eine Zeile im Logbuch: `Phase <vorher> ->
  <neu>: n umgestellt, m ausgelassen, durch <Konto als k#...>`, bei der Konsole oder dem Rückfall nach 12 h `durch Konsole oder
  Rückfall` (am Code geprüft, `Zentrale.cs`).
- **Verwaltungsjournal** (`journal.json`): wer wann was verwaltet hat, mit Klarnamen, 90 Tage. Lesen mit `funk-admin
  journal` oder im Verwaltungsteil. Einträge der Konsole tragen "Konsole" als Urheber. Neu in 0.5.0: die Einträge
  `FUNKER_MODUS_EIN` und `FUNKER_MODUS_AUS` (B10.7); das Schalten einer Phase steht nicht im Verwaltungsjournal, sondern nur im
  Logbuch.

### B1.8 Neu in 0.5.0: Phasen und Funker-Modus am Server

Zwei Ausbaustufen kamen mit 0.5.0 (Standzeilen 01.10.2026 20:15 und 23:05, Protokoll 6.14 und 6.15). Am Draht sind es drei
Nachrichten (Protokoll 4.4):

| Typ | Name | Richtung | Wer darf | Grenze |
| --- | --- | --- | --- | --- |
| `0x003C` | Funkermodus | Client an Server, Anfrage | nur ein aktives ADMIN-Konto, nur für sich selbst, ohne Admin-Sitzung und ohne Code | 1 je Sekunde, Stoß 2 |
| `0x003D` | PhaseSchalten | Client an Server, Anfrage | wer `ZUWEISEN` hat (Stufe, Einzelrecht oder laufender Auftrag) | 1 je 2 Sekunden, Stoß 2; eine Flut endet bei `GRENZE` ohne Trennung |
| `0x4004` | PhaseGeschaltet | Server an Client, Push, optional | an alle aktiven Sitzungen, auch kurz nach Welcome | |

- **Phasen** (B14.3): Das Netzbild einer Flotte je Rolle, höchstens 4 Phasen neben der Grundstellung. Angelegt wird im
  Verwaltungsteil (`Verwaltung PHASE_SETZEN` und `PHASE_LOESCHEN`, Recht `NETZE_VERWALTEN`), geschaltet von der Einsatzleitung. Die
  Phasen gehören zur Netzliste (`NetzListe.phasen`), die laufende Phase mit Nummer, Zeit und Urheber zum Lagezustand in `lage.json`;
  ein Wechsel ändert die Netzliste nicht.
- **Funker-Modus** (B10.7): `alsFunkerBis` am Konto in `lage.json`, wie ein Auftrag ein Zustand des Abends.
- **Zustand:** `lage.json` trägt Phasen, laufende Phase und `alsFunkerBis` (B1.4). Ein Zustand, den die Programme 0.4.0 selbst
  geschrieben haben, wird verlustfrei geladen; er liegt als Testdaten bei (`tests\Funk.Tests\Daten\zustand-0.4.0`).
- **Alte und neue Programme:** Ein Client 0.5.0 schickt `0x003C` und `0x003D` nur an einen Server ab 0.5.0 (an einem älteren
  blendet er Phasen und Funker-Modus aus); ein Server 0.4.x trennte bei diesen Pflichttypen mit `PROTOKOLL`. Clients 0.4.x funken
  mit dem Server 0.5.0 weiter und sehen Phasen und Funker-Modus nicht (neue Felder und der Push liegen im optionalen Bereich).
  Gemessen ist das mit `funk-echo` und der Echo-Probe 0.4.0, nicht mit einem echten Client 0.4.0 (B19.6).
- **Größe:** Die Netzliste kann mit 5 Phasen und 512 Variantenfeldern im ungünstigsten Fall 323 KB groß werden (gemessen; die
  Phasen legen 66 KB dazu, Faktor 3,25 unter der Rahmengrenze von 1 MiB, Protokoll 4.10, Teil D).
- **Konsole:** Für Phasen und Funker-Modus gibt es keine Befehle in `funk-admin` (B9.8).
- **Nicht gemessen:** die Netzlast eines Phasenwechsels bei 40 Leuten am Pacer vorbei (Protokoll 7.4) und `Mitgliedschaften.phase`
  an einem echten Client 0.4.0.

## B2 Voraussetzungen

### B2.1 Server

| Was | Anforderung | Beleg |
| --- | --- | --- |
| System | Debian 13 (Empfehlung für neue Server), Debian 12 oder Ubuntu 24.04; x86_64; systemd; root | `EINRICHTEN.md`; das Skript prüft x86_64, systemd und `/etc/os-release` |
| Andere Systeme | nur von Hand: libicu und .NET-Laufzeit 10 unter `/usr/bin/dotnet` selbst bereitstellen | `funk-einrichten.sh` |
| Laufzeit | .NET-Laufzeit 10 (`Microsoft.NETCore.App`, ohne ASP.NET) und libicu, weil Namen nach NFKC normalisiert werden | `BETRIEB.md`, Voraussetzungen |
| Proxmox-Container | unprivilegiert, mit `nesting=1`; ohne nesting startet der Dienst nicht (`226/NAMESPACE`, unter Debian 13 laut Proxmox-Forum auch `243/CREDENTIALS`, nicht gemessen) | `HEIMSERVER-PROXMOX.md`, Abschnitt 2 |
| Größe des Containers | Beispiel: 2 Kerne, 1024 MB RAM, 512 MB Swap, 8 GB Platte | `HEIMSERVER-PROXMOX.md`, Abschnitt 2 |
| Uhr | Zeitsynchronisierung (chrony oder systemd-timesyncd), im Container auf dem Host; TOTP verträgt nur 30 s | `BETRIEB.md` |
| Zeitzone | `/usr/share/zoneinfo/Europe/Berlin` (Paket `tzdata`); sonst rechnen Aufträge in der Zeitzone des Systems | `HEIMSERVER-PROXMOX.md`, Abschnitt 15 |
| Netz | eine öffentliche IPv4, im Heimbetrieb ohne Carrier-NAT (die Adresse beginnt nicht mit `100.64.` bis `100.127.`) | `HEIMSERVER-PROXMOX.md`, Abschnitt 1 |
| DNS | ein A-Eintrag auf die öffentliche Adresse, TTL möglichst höchstens 60 s, bis 300 s geht es | `BETRIEB.md`, Heimbetrieb |
| Ports | ein freier TCP-Port (443 auf einem Server ohne Router, sonst 8443) und UDP 47443 | `EINRICHTEN.md` |

**Last, gemessen:** Mit 40 simulierten Funkern brauchte der Server bei 5 und bei 12 gleichzeitigen Sprechern im Mittel 8 bis 21 % eines Kerns, wenn alle 40 zugleich zu allen sprechen 74 %, und rund 100 MB Arbeitsspeicher
(Lasttest auf dem Entwicklungsrechner, Ryzen 9 5950X, Windows, Loopback ohne TLS, `LASTTEST.md`). Auf dem Ziel-LXC ist
das noch nicht gemessen.

**Upload, hochgerechnet aus dem Lasttest:** Der Lasttest schickt 160 Byte Opus je 20-ms-Rahmen, also 64 kbit/s, mehr als
die rechnerisch 120 Byte, die der Client mit seinen 48 kbit/s erzeugt (D3.1). Damit braucht jeder Sprachstrom, den der
Server hinausschickt, auf der Leitung 104,4 kbit/s (261 Byte je Paket samt IPv4, UDP und 26 Byte Zuschlag für PPPoE und
Ethernet, 50 Pakete je Sekunde). Teil D rechnet dasselbe für 20 und 40 Leute mit 1, 3 und 5 Sprechern.

| Szenario aus `LASTTEST.md` (40 Funker) | Ströme | Upload des Servers |
| --- | ---: | ---: |
| A: 5 Sprecher mit je 12 Hörern | 60 | 6,27 Mbit/s |
| B: 12 Sprecher mit je 15 Hörern | 180 | 18,80 Mbit/s |
| C: eine Durchsage an 39 Hörer | 39 | 4,04 Mbit/s |
| D: alle 40 sprechen zugleich im selben Netz | 1560 | 162,97 Mbit/s |

D ist kein realistischer Abend; daheim begrenzt der Pacer auf `grenzen.ausgangMbit`. Diesen Wert stellst du auf höchstens
80 % des gemessenen Netto-Uploads (Protokoll 7.4). Mit IPv6 kommen je Paket 20 Byte dazu. Die Zahlen stammen vom Lasttest
vom 29.09.2026 (vor 0.5.0); mit 0.5.0 ist er nicht wiederholt, und die Steuerlast eines Phasenwechsels bei 40 Leuten ist nicht gemessen
(B1.8).

### B2.2 Admins und Eigentümer

| Was | Anforderung |
| --- | --- |
| Konsole | root auf dem Server, bei einem Container auch Zugang zum Proxmox-Host. Ernennen, Eigentümer und Zweitfaktor gehen nur hier. |
| Admin-PC | Windows mit TPM 2.0. Der Admin-Schlüssel entsteht nicht exportierbar im TPM und liegt nie als Datei vor; ohne TPM gibt es auf diesem PC keine Verwaltung, einen Rückfall auf Software gibt es mit Absicht nicht. |
| Admin-PC ab Client 0.4.1 (Anf. A56) | Ein geschütztes Windows-Konto mit PIN oder Kennwort ist Voraussetzung (Richtlinie, keine technische Prüfung); ein Microsoft-Konto ist nicht nötig. BitLocker oder Geräteverschlüsselung wird empfohlen, ist aber keine Voraussetzung; fehlt sie, warnt die Vorprüfung im Verwaltungsteil (B10.6). |
| Admin-Setup | `<zeit>_JG-TacRelay-Admin-Setup-<fassung>.exe` in derselben Fassung wie der Client; ein Verwaltungsteil einer anderen Fassung lädt nicht |
| Zweitfaktor | Authenticator-App (jede nach RFC 6238), am besten auf dem Telefon, nicht auf dem Admin-PC |
| Stick des Eigentümers | Ordner `funk-offline` mit `profil.key`, `reserve.key`, `reserve.crt`, `profil.json` und den Programmen aus dem Serverpaket; mit BitLocker To Go verschlüsseln, denn die Schlüssel liegen dort als Dateien. Auf dem Rechner läuft `funk-admin.dll` mit der .NET-Laufzeit, die JG TacRelay ohnehin braucht. |

### B2.3 Wer was tut

| Rolle | Wer | Was |
| --- | --- | --- |
| Eigentümer | wer den Profilschlüssel hält: für Sarevocs Server Sarevoc (Anf. A41), bei einer eigenen Gruppe ihr Verwalter (B0.4) | unterschreibt Serverprofile mit `profil.key`, ist am Server Eigentümer, ernennt Admins an der Konsole |
| Betreiber | wer root auf dem Server ist, daheim oder nach einem Umzug der neue Betreiber | Einrichten, Updates, Sicherung, Umzug; hat an der Konsole alle Rechte |
| Admin | vom Eigentümer ernannt | Anträge, Mitglieder, Flotte (auch die Phasen), Einsatzleitung im Verwaltungsteil des Clients; kann für einen Abend selbst als Funker spielen, damit ein anderer ihn einteilt (B10.7) |
| Einsatzleitung | ein Mitglied mit Auftrag für einen Abend (oder ein ADMIN) | einteilen auf der Funkfeld-Zuteilung (bis Client 0.11.1 das Brett) und Phasen schalten (B14.3), ohne Verwaltungsrechte |

## B3 Einrichtung im Proxmox-LXC

Zwei Wege führen zum laufenden Server im Container. Mit dem Skript (unten) ist es ein Befehl mit ein paar Fragen. Von Hand
beschreibt ihn `HEIMSERVER-PROXMOX.md`, Abschnitte 1 bis 13, Schritt für Schritt; so ist der Heimserver von Sarevoc
eingerichtet. Beide Wege nutzen dieselben Pfade (B1.4); den Kurzbefehl `funk-admin`, `profilanfrage.txt` und `echo.env`
legt nur das Skript von selbst an.

### B3.1 Container anlegen

Auf dem Proxmox-Host (Weboberfläche, Knoten, Shell):

```
pveversion
pvesm status
pct list
pveam update
pveam available --section system | grep debian
pveam download local <vorlage aus der liste>
```

Für einen neuen Container die Vorlage `debian-13-standard_...` (Debian 12 geht auch; für Ubuntu 24.04 entsprechend die
Ubuntu-Vorlage aus `pveam available --section system`). Dann anlegen, mit deinem Container-Speicher aus `pvesm status`
statt `local-lvm` und deinen Adressen:

```
pct create 150 local:vztmpl/<vorlage aus der liste> \
  --hostname funk --unprivileged 1 --features nesting=1 \
  --cores 2 --memory 1024 --swap 512 --rootfs local-lvm:8 \
  --net0 name=eth0,bridge=vmbr0,ip=192.168.1.250/24,gw=192.168.1.1,firewall=1 \
  --nameserver 192.168.1.1 --onboot 1
pct start 150
pct enter 150
```

Ab `pct enter` bist du im Container, als root. Dort das System auf den neuesten Stand bringen:

```
apt update && apt full-upgrade -y
apt install -y wget ca-certificates
```

| Einstellung | Warum |
| --- | --- |
| `--unprivileged 1` | root im Container ist auf dem Host kein root |
| `--features nesting=1` | Die Härtung der Unit legt eigene Mount-Namensräume an; ohne nesting startet der Dienst nicht |
| feste Adresse in `--net0` | außerhalb des DHCP-Bereichs des Routers; sie kommt in `binden` und in die Portfreigabe |
| `firewall=1` | schaltet die Proxmox-Firewall für die Netzwerkkarte des Containers ein (B3.4) |
| `--onboot 1` | der Container startet mit dem Host |
| 2 Kerne, 1 GB | gemessen 8 bis 21 % eines Kerns bei 40 Funkern mit 5 bis 12 Sprechern (B2.1); reicht mit viel Luft |

Für einen Probelauf in einem Wegwerf-Container nimmst du eine eigene Nummer (etwa 199), einen eigenen Namen und `ip=dhcp`.

**Das siehst du:** `pct config 150` zeigt `unprivileged: 1`, `features: nesting=1` und die Netzwerkkarte mit deiner
Adresse. Im Container zeigt `ip -4 addr show eth0` die Adresse, `ping -c 3 deb.debian.org` bekommt Antworten, und
`cat /etc/debian_version` nennt die Debian-Version.

### B3.2 Paket hineinbringen

Das Serverpaket kommt als `<zeit>_JG-TacRelay-Server-<fassung>.zip` mit einer Datei `.sha256` daneben. Auf deinem
Windows-PC (PowerShell) zum Host, dort in den Container:

```
scp <zeit>_JG-TacRelay-Server-<fassung>.zip root@<proxmox>:/root/
```

```
pct push 150 /root/<zeit>_JG-TacRelay-Server-<fassung>.zip /root/<zeit>_JG-TacRelay-Server-<fassung>.zip
```

Im Container:

```
cd /root
sha256sum <zeit>_JG-TacRelay-Server-<fassung>.zip
apt install -y unzip
unzip <zeit>_JG-TacRelay-Server-<fassung>.zip
cd <zeit>_JG-TacRelay-Server-<fassung>/linux
```

`sha256sum` muss dieselbe Zahl zeigen wie die Datei `.sha256`. Im Ordner `linux` liegen `funk-einrichten.sh`, die
Programme und der Ordner `konfig`. Nie den Ordner `funk-offline` vom Stick in den Container bringen: Darin liegen
`profil.key` und `reserve.key`.

### B3.3 Einrichten mit dem Skript

```
bash funk-einrichten.sh
```

Die Fragen beim ersten Lauf und was du im Heimnetz antwortest (Einzelheiten in B5). Mit Router sind es acht Fragen, ohne
Router sechs (B5.3); hier steht jede einzeln:

| Frage | Antwort im Heimnetz |
| --- | --- |
| Öffentlicher Name | dein DynDNS-Name, etwa `funk.example.org` |
| Adresse dieses Rechners, an die der Server bindet | die feste LAN-Adresse des Containers, meist der Vorschlag |
| Sitzt ein Router mit Portfreigabe davor? | Eingabetaste (Vorschlag `j` bei einer privaten Adresse) |
| TCP-Port am Server | 8443 (443 gehört daheim nginx für Foundry) |
| TCP-Port außen am Router | 8443 |
| UDP-Port am Server, UDP-Port außen am Router | 47443 |
| Echo-Funker einrichten? | `j`, zum Prüfen |

Das Skript endet mit `WARTET AUF PROFIL` und der Anfragezeile. Weiter mit B8.3 (Profil vom Eigentümer), dann
`bash funk-einrichten.sh --profil profil.json`, dann B10.2 (erster Admin).

### B3.4 Proxmox-Firewall

Die Regeln wirken an der Netzwerkkarte des Containers (`firewall=1`). Auf dem Host die Datei `/etc/pve/firewall/150.fw`:

```
[OPTIONS]
enable: 1
policy_in: DROP
policy_out: ACCEPT

[RULES]
IN ACCEPT -p tcp -dport 8443
IN ACCEPT -p udp -dport 47443
OUT ACCEPT -dest 192.168.1.1 -p udp -dport 53
OUT ACCEPT -dest 192.168.1.1 -p tcp -dport 53
OUT DROP -dest 10.0.0.0/8
OUT DROP -dest 172.16.0.0/12
OUT DROP -dest 192.168.0.0/16
```

Eingehend nur die zwei Ports. Ausgehend kommt der Container ins Internet (Updates), ins Heimnetz aber nur zum DNS des
Routers; andere Dienste auf demselben Host (etwa Foundry) bleiben außer Reichweite. Die Regeln wirken erst, wenn die
Firewall auch auf Rechenzentrumsebene an ist (Weboberfläche: Datacenter, Firewall, Options, Firewall: Yes); dabei eine
Shell auf dem Host offen halten. Dann:

```
pve-firewall compile
pve-firewall status
```

**Das siehst du:** `Status: enabled/running`, und die Weboberfläche von Proxmox geht weiter. Im Container zeigt
`ss -tulpen` die beiden Ports auf der LAN-Adresse, sonst höchstens Dienste auf `127.0.0.1`. Einen SSH-Dienst im Container
(Port 22, je nach Vorlage) brauchst du nicht, `pct enter` reicht; wenn du magst: `systemctl disable --now ssh`.

### B3.5 Router, DynDNS und IPv6

- **Portfreigaben** am Router (FRITZ!Box: Internet, Freigaben, Portfreigaben), genau zwei: TCP außen 8443 an
  `192.168.1.250:8443`, UDP außen 47443 an `192.168.1.250:47443`. Kein SSH, kein Port 80, UDP nicht auf 443.
- **DynDNS** nur mit A-Eintrag. Ein AAAA-Eintrag würde von der FRITZ!Box ihre eigene IPv6 melden, nicht die des
  Containers. TTL prüfen (PowerShell): `Resolve-DnsName <dyndns-name> -Type A -DnsOnly | Format-List Name, IPAddress, TTL`.
- **IPv6** in der Heimphase nicht: Im Profil steht der Host mit `/v4`, `binden` nennt nur die IPv4. IPv6 kommt erst, wenn
  die Freigabe am Router geklärt ist (Offener Punkt 19).
- **FRITZ!Box:** den Container als Echtzeitanwendung priorisieren, die Zwangstrennung auf eine Uhrzeit ohne Einsätze legen,
  den Netto-Upload messen und `grenzen.ausgangMbit` auf höchstens 80 % davon setzen.

**Von außen prüfen** (PC im Handy-Hotspot, nicht im Heimnetz): `Test-NetConnection <dyndns-name> -Port 8443` zeigt
`TcpTestSucceeded : True`. UDP lässt sich so nicht prüfen; zeigt der Client `Verbunden (Tunnel)`, kommt UDP nicht durch.

## B4 Einrichtung auf einem gemieteten Server

Für einen Server bei Hetzner, OVH oder einem anderen Anbieter, ohne Router davor, mit einer öffentlichen Adresse direkt am
Rechner. Der Weg ist derselbe Befehl wie im Container; anders sind Name, Port, Firewall und die Fähigkeit für Port 443.
Die Oberflächen der Anbieter sind hier nicht beschrieben und nicht geprüft.

### B4.1 Vorher klären

| Frage | Warum |
| --- | --- |
| System | Debian 13 (Empfehlung), Debian 12 oder Ubuntu 24.04, x86_64 |
| Name im DNS | etwa `funk.example.org`, A-Eintrag auf die öffentliche IPv4 des Servers, TTL höchstens 300 s. Wer `example.org` verwaltet, ist vorher zu klären. |
| Was läuft schon? | `ss -tulpen` zeigt belegte Ports. Läuft dort schon ein anderer Dienst (etwa `coms.example.org`): Das Skript fasst fremde Dienste nicht an und fragt bei einem belegten Port nach einem anderen. |
| TCP-Port | Ein neuer, eigener Funk: 443, wenn frei (das Skript schlägt ihn dann vor). **Zieht der Funk vom Heimserver hierher um: 8443**, auch wenn 443 frei wäre, denn ein Profil kennt nur einen TCP-Port für alle Hosts (B20). |
| Firewall des Anbieters | Hat der Anbieter eine eigene Firewall vor dem Server, braucht sie dieselben zwei Regeln wie der Rechner selbst |

### B4.2 Rechner vorbereiten

Bevor der Funk kommt, den Rechner selbst absichern (Kurzfassung in B17): SSH nur mit Schlüssel, Sicherheitsupdates von
selbst.

```
apt install unattended-upgrades
dpkg-reconfigure -plow unattended-upgrades
```

### B4.3 Paket und Skript

Von deinem PC (PowerShell), `root@<server>` ist der gemietete Server:

```
scp <zeit>_JG-TacRelay-Server-<fassung>.zip root@<server>:/root/
```

Auf dem Server:

```
cd /root
sha256sum <zeit>_JG-TacRelay-Server-<fassung>.zip
sudo apt install -y unzip
unzip <zeit>_JG-TacRelay-Server-<fassung>.zip
cd <zeit>_JG-TacRelay-Server-<fassung>/linux
sudo bash funk-einrichten.sh
```

| Frage | Antwort auf einem gemieteten Server |
| --- | --- |
| Öffentlicher Name | `funk.example.org` (der Name aus dem DNS); mit IPv6 `funk.example.org/v4v6` |
| Adresse dieses Rechners | die öffentliche IPv4, meist der Vorschlag (aus `ip -4 route get 1.1.1.1`) |
| Router mit Portfreigabe davor? | `n` |
| TCP-Port am Server | 443 für einen neuen Funk (Vorschlag, wenn 443 frei ist), 8443 bei einem Umzug vom Heimserver |
| UDP-Port am Server | 47443 |
| Echo-Funker einrichten? | `j` |

Danach wie im Container: `WARTET AUF PROFIL`, Anfragezeile an Sarevoc, Kennung am Telefon, `sudo bash funk-einrichten.sh
--profil profil.json` (B8.3), erster Admin (B10.2) oder Umzugspaket (B20).

### B4.4 Port 443 und CAP_NET_BIND_SERVICE

Die Unit lässt `funk-server` als Benutzer `funk` ohne jede Fähigkeit laufen (`CapabilityBoundingSet=` und
`AmbientCapabilities=` leer). Ports unter 1024 darf ein gewöhnlicher Benutzer nicht binden. Steht in `server.json` ein
`tcpPort` oder `udpPort` unter 1024, legt `einrichten` deshalb ein Drop-in an, das genau diese eine Fähigkeit gibt:

```
# Angelegt von funk-admin einrichten: Der Server bindet einen Port unter 1024 (tcpPort oder udpPort in
# /etc/funk/server.json). Das darf er als Benutzer funk nur mit dieser einen Fähigkeit; die Unit selbst gibt ihm keine.
[Service]
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE
```

Datei: `/etc/systemd/system/funk-server.service.d/10-port-unter-1024.conf`. Ein fremdes Drop-in unter diesem Namen (ohne die
Zeile `Angelegt von funk-admin einrichten`) ersetzt `einrichten` nicht, es meldet `FEHLT`.

Prüfen:

```
systemctl cat funk-server
ss -ltnp
```

**Das siehst du:** Unter der Unit steht das Drop-in, und `ss -ltnp` zeigt 443 bei `dotnet`. Die Bindung unter 1024 ist
auf Linux noch nicht gemessen; das prüft der Probelauf (B5.8, Punkt 5). Von Hand eingerichtet, ohne Skript, legst du
dieselbe Datei an und rufst `systemctl daemon-reload` und `systemctl restart funk-server`.

Die Alternative ohne Fähigkeit wäre eine Weiterleitung von außen 443 auf innen 8443 (`oeffentlicherTcpPort` 443,
`tcpPort` 8443); `funk-admin pruefen --vollstaendig` nennt sie, wenn es keine Berechtigung für einen Port unter 1024 gibt.
Wie du eine solche Weiterleitung auf einem Server ohne Router baust, beschreibt dieses Handbuch nicht.

### B4.5 Firewall am Rechner

`einrichten` zeigt, was nötig ist, und fasst eine Firewall nur nach deinem ausdrücklichen Ja an:

| Was es findet | Was es tut |
| --- | --- |
| ufw aktiv (`ufw status` zeigt `Status: active`), Regeln fehlen | zeigt `ufw allow 443/tcp` und `ufw allow 47443/udp` (mit deinen Ports) und fragt `Diese Regeln jetzt in ufw eintragen? Nur ein ausdrückliches j oder ja trägt sie ein` |
| ufw aktiv, Regeln da | `OK     ufw lässt TCP ... und UDP ... herein` |
| nftables mit einer Kette `hook input` | zeigt `tcp dport <port> accept` und `udp dport <port> accept` für die Kette input (dauerhaft in `/etc/nftables.conf`) und meldet `BLEIBT nftables unverändert` |
| keine Firewall | `Hier ist keine Firewall aktiv (ufw, nftables). Filtert der Anbieter davor, dort TCP ... und UDP ... freigeben.` |

Willst du ufw erst einschalten, gib vorher deinen SSH-Port frei, sonst sperrst du dich aus (allgemeiner Hinweis, im
Projekt nicht geprüft; etwa `ufw allow 22/tcp`, wenn SSH auf 22 läuft).

**Von außen prüfen** (PowerShell auf deinem PC): `Test-NetConnection funk.example.org -Port 443` zeigt
`TcpTestSucceeded : True`.

### B4.6 IPv6 auf einem gemieteten Server (wahlweise)

Ohne Router fällt der Grund der Heimphase weg, IPv6 auszulassen. Dafür braucht es drei Dinge zusammen: die IPv6-Adresse
zusätzlich in `binden` (von Hand in `server.json`, `einrichten` fragt nur eine Adresse), einen AAAA-Eintrag im DNS und den
Host mit `/v4v6` im Profil (in der Anfragezeile: Name `funk.example.org/v4v6` bei der ersten Frage oder `profil-anfrage
--host funk.example.org/v4v6`). Gemessen ist der Betrieb mit IPv6 nicht.

## B5 funk-einrichten.sh im Detail

### B5.1 Aufruf

```
sudo bash funk-einrichten.sh                                      # erster Lauf, endet mit WARTET AUF PROFIL
sudo bash funk-einrichten.sh --profil profil.json                 # Profil prüfen, einspielen, starten
sudo bash funk-einrichten.sh --umzug <paket> [--mit-serverschluessel]   # Umzugspaket einspielen (B20)
bash funk-einrichten.sh --hilfe                                   # diese Zeilen
```

Andere Angaben lehnt es ab: `Unbekannte Angabe ... Erlaubt: --profil <datei>, --umzug <paket>, --mit-serverschluessel.
Nichts geändert.` Das Skript lässt sich beliebig oft aufrufen. Was passt, meldet es als `OK` oder `BLEIBT`, und der Dienst
läuft weiter.

| Exitcode | Bedeutung |
| --- | --- |
| 0 | eingerichtet, Server bereit |
| 1 | ein Schritt fehlt (`FEHLT` mit Abhilfe) oder abgebrochen |
| 2 | Aufruf falsch, nichts geändert |
| 4 | wartet auf das Profil vom Eigentümer (`WARTET AUF PROFIL`) |

Jede Zeile beginnt mit `OK`, `NEU`, `BLEIBT` oder `FEHLT`. Hinter `FEHLT` steht `Abhilfe:` und ein Satz, was zu tun ist.

### B5.2 Erste Stufe: das Skript selbst (vor .NET)

1. **Prüfen:** root (`Nicht als root. Abhilfe: sudo bash ...`), systemd (`/run/systemd/system`), Rechner `x86_64`,
   `/etc/os-release`. Debian, Ubuntu und Abkömmlinge (`ID` oder `ID_LIKE` debian oder ubuntu) bekommen Pakete über apt;
   auf anderen Systemen müssen libicu und die .NET-Laufzeit 10 schon da sein.
2. **libicu:** Ist `libicuuc.so` bekannt (`ldconfig -p`), `OK`. Sonst sucht es mit `apt-cache search --names-only
   '^libicu[0-9]+$'` das höchste Paket und installiert es.
3. **.NET-Laufzeit 10** unter `/usr/bin/dotnet`, in dieser Reihenfolge:
   - Liegt eine Laufzeit 10 unter `/usr/share/dotnet`, aber kein `/usr/bin/dotnet`, legt es den Verweis an.
   - Gibt es `/usr/bin/dotnet` ohne Laufzeit 10, hört es auf (`FEHLT`): Dieses .NET gehört womöglich einem anderen Dienst.
     Abhilfe: die Laufzeit 10 auf demselben Weg dazulegen, auf dem die vorhandene kam.
   - Bieten die Paketquellen `dotnet-runtime-10.0` an, installiert es das Paket (Updates kommen dann mit apt).
   - Sonst fragt es, ob es die Paketquelle von Microsoft eintragen darf:
     `https://packages.microsoft.com/config/<id>/<version>/packages-microsoft-prod.deb` (etwa `debian/13` oder
     `ubuntu/24.04`). Gibt es die nicht, meldet es das.
   - Sonst fragt es, ob es das Installationsskript von Microsoft nehmen darf
     (`https://dot.net/v1/dotnet-install.sh --channel 10.0 --runtime dotnet --install-dir /usr/share/dotnet`), und legt
     den Verweis `/usr/bin/dotnet` an. Updates dann mit demselben Skript.
   - Bei diesen Fragen heißt die Eingabetaste ja, das Ende der Eingabe nein.
4. **Programme nach `/opt/funk`:** Erst prüft es, dass es im Ordner `linux` eines Serverpakets steht
   (`funk-server.dll`, `funk-admin.dll`, `funk-echo.dll`, `funk-server.deps.json`, die beiden Units und
   `profilschluessel.txt` unter `konfig`). Verglichen und kopiert werden nur die Programmdateien: `*.dll`,
   `*.deps.json`, `*.runtimeconfig.json`, `*.md`, `funk-einrichten.sh`, `Profil erneuern.cmd`, `konfig/*` und
   `konfig/fail2ban/*/*.conf`. Was du daneben in den Ordner legst (`profil.json`, ein Umzugspaket), kommt nie nach
   `/opt/funk` und hält nie den Dienst an.
   - Sind die Dateien Byte für Byte gleich: `OK Programme <fassung> in /opt/funk, unverändert`.
   - Sonst und wenn `funk-server` läuft, fragt es `funk-server läuft mit <alt>. Für die Programme <neu> kurz anhalten?`,
     hält `funk-echo` und `funk-server` an und legt den alten Stand nach `/opt/funk.<alte fassung>` (gibt es den schon,
     mit Zeitstempel dahinter). Liegt in `/opt/funk` kein `funk-server.dll`, hört es auf: Das ist nicht unser Ordner.
   - Immer, auch bei unveränderten Programmen: `chown -R root:root /opt/funk` und `chmod -R go-w /opt/funk` (ein tgz aus
     Windows trägt "für alle beschreibbar").
5. Dann übergibt es an `funk-admin einrichten` mit denselben Angaben.

### B5.3 Zweite Stufe: funk-admin einrichten

Nur unter Linux und als root. Die Pfade stehen fest (B1.4), nicht in Optionen. Gibt es `/etc/funk/server.json` noch nicht,
fragt es zuerst; die Eingabetaste nimmt den Vorschlag in eckigen Klammern, nach fünf ungültigen Antworten oder am Ende der
Eingabe bricht es ab (`Abgebrochen. Nichts geändert.`).

| Frage | Vorschlag | Geprüft |
| --- | --- | --- |
| Öffentlicher Name, unter dem die Clients den Server finden | aus `hostname -f`, wenn er einen Punkt hat | gültiger Name, wahlweise mit `/v4`, `/v6` oder `/v4v6`, ohne `https://` und ohne Port; danach zeigt es, wohin der Name im DNS zeigt |
| Adresse dieses Rechners, an die der Server bindet | Quelladresse aus `ip -4 route get 1.1.1.1` | eine einzelne IP, nicht `0.0.0.0` oder `::`, auf diesem Rechner vorhanden |
| Sitzt ein Router mit Portfreigabe davor? | `j` bei einer privaten Adresse (10/8, 172.16/12, 192.168/16, 100.64/10, 127/8) | `j`, `ja`, `n`, `nein` |
| TCP-Port am Server | 8443 hinter einem Router oder wenn 443 belegt ist, sonst 443 | 1 bis 65535 und frei; bei belegtem Port nennt es das Programm (aus `ss`) und fragt noch einmal |
| TCP-Port außen am Router (nur mit Router) | wie am Server | 1 bis 65535 |
| UDP-Port am Server | 47443 | 1 bis 65535 und frei |
| UDP-Port außen am Router (nur mit Router) | wie am Server | 1 bis 65535 |
| Echo-Funker einrichten? | `j` | |

Das sind beim ersten Lauf sechs Fragen, mit Router acht (`Einrichtung.cs`, `Fragen`). Die Hilfe von `funk-admin` sagt es seit
0.5.0 auch: `beim ersten Lauf sechs Fragen (mit Router acht), dann nur, was fehlt` (`Hilfe.cs`; bis 0.4.1 stand dort "fünf
Fragen"). Ein Test zählt die gelesenen Antworten.

Vor der Portfrage erinnert es: `Zieht ein bestehender Funk hierher um, nimm denselben TCP-Port wie der alte Server (etwa
8443):` und `Ein Profil kennt nur einen TCP-Port für alle Hosts (UMZUG.md).`

Dann die Schritte, in dieser Reihenfolge:

| Schritt | Was geschieht | Meldung |
| --- | --- | --- |
| Benutzer | `funk`, mit Echo auch `funk-echo`, je `useradd --system --no-create-home --shell /usr/sbin/nologin` | `OK Benutzer funk` oder `NEU Benutzer funk (Systembenutzer ohne Anmeldung)` |
| Ordner | `/etc/funk` (0755), `/etc/funk/credentials` (0700), mit Echo `/var/lib/funk-echo` (0700, `funk-echo`); ein Schlüsselordner, der für andere offen ist, bekommt 0700 | `NEU`, `OK` oder `NEU Rechte von ... auf 0700 (waren ...)` |
| Schlüssel | `server.crt` und `server.key`, `funk-geheimnis`, `funk-totp-schluessel`: fehlende erzeugt es, vorhandene ersetzt es nie; offene Rechte setzt es auf 0600. Liegt nur eine von `server.crt` und `server.key` da, hört es auf | `NEU Serverschlüssel und Zertifikat in /etc/funk/credentials, Pin XXXX-XXXX-XXXX-XXXX` oder `BLEIBT ...` |
| server.json | beim ersten Lauf aus den Antworten geschrieben (wie `konfig-schreiben`), sonst nur gelesen und gegen die Regeln des Servers geprüft; überschrieben wird sie nie | `NEU /etc/funk/server.json (binden ..., TCP ..., UDP ...)` oder `BLEIBT ... (vorhanden und gültig; einrichten schreibt nie darüber)` |
| Profilschlüssel | `konfig/profilschluessel.txt` aus dem Paket nach `/etc/funk/profilschluessel.txt` | `OK` oder `NEU Profilschlüssel des Eigentümers (öffentlich)` |
| Units | `funk-server.service` und mit Echo `funk-echo.service` aus dem Paket, wenn sie fehlen oder abweichen; die bisherige bleibt als `.alt` daneben (gibt es die schon, mit Zeitstempel) | `OK Unit ...` oder `NEU Unit ... (die bisherige liegt als ... daneben)` |
| Drop-in | nur bei `tcpPort` oder `udpPort` unter 1024 (B4.4); ersetzt nur ein eigenes | `NEU Drop-in für Ports unter 1024 (CAP_NET_BIND_SERVICE) ...` |
| echo.env | mit Echo: `FUNK_ECHO_ADRESSE=<erste Adresse aus binden>`; eine vorhandene bleibt | `NEU /etc/funk/echo.env (...)` oder `BLEIBT` |
| Kurzbefehl | `/usr/local/sbin/funk-admin`; ersetzt nur einen eigenen | `NEU Kurzbefehl ...` oder `FEHLT ... stammt aber nicht von JG TacRelay` |
| Uhr | `timedatectl show -p NTPSynchronized --value` | `OK Uhr synchronisiert (NTP)`, `FEHLT Die Uhr ist nicht synchronisiert ...` oder `BLEIBT Uhrzeit hier nicht prüfbar ...` |
| Firewall und Router | nur Anzeige: im LXC die Regeln für `/etc/pve/firewall/<id>.fw`, hinter einem Router die zwei Portfreigaben, sonst ufw oder nftables (B4.5) | Block `Firewall und Router (...)` |
| Umzugspaket | nur mit `--umzug` (B20.3) | `Eingespielt nach ...` |
| Profil | prüft `/etc/funk/profil.json` oder das mit `--profil` angegebene (B5.4) | `NEU Serverprofil Version N ...`, `OK Serverprofil Version N ...` oder `WARTET AUF PROFIL` |
| Start | `systemctl daemon-reload`, dann `enable --now` oder `restart`; nur ein neues Profil bei laufendem Dienst lädt es mit `profil-neu-laden` ohne Neustart. Danach wartet es bis zu 30 s auf die Zeile `bereit` im Journal und prüft mit `ss`, dass beide Ports hören | `NEU funk-server eingeschaltet und gestartet (systemctl enable --now)`, `OK Funk-Server 0.5.0 bereit: ...`, `OK TCP ... und UDP ... hören (ss)` |
| Echo | gibt es `/var/lib/funk-echo/geraet.pem`, wird `funk-echo` eingeschaltet oder nach einer Änderung neu gestartet; sonst nennt der Abschluss die Befehle für die erste Registrierung | `NEU funk-echo eingeschaltet und gestartet` |
| Abschluss | Block `==== Funk-Server <fassung> bereit ====` mit Name, Ports, Pin, Stand, Eigentümer, Schutz und dem nächsten Schritt | |

### B5.4 Wie einrichten ein Profil prüft

Geprüft wird gegen die öffentlichen Profilschlüssel aus `konfig/profilschluessel.txt` des Pakets (jede Zeile, die mit
`MFkw` beginnt). Ein Profil wird nur eingespielt, wenn alles stimmt:

| Prüfung | Wenn nicht |
| --- | --- |
| signiertes `{daten, signatur}` | `... ist kein signiertes Serverprofil.` (etwa `daten.json` statt `profil.json`) |
| mit dem Profilschlüssel des Eigentümers unterschrieben | `Das Profil ist nicht mit dem Profilschlüssel des Eigentümers unterschrieben (...)` |
| Inhalt nach Protokoll 3.1 | `Das Profil ist richtig unterschrieben, aber sein Inhalt verletzt 3.1.` |
| Version höher als die eingespielte | `Das Profil hat Version N, eingespielt ist schon Version M.` |
| der Pin dieses Servers steht darin | `Der Pin dieses Servers (Kennung ...) steht nicht im Profil Version N.` |
| TCP-Port des Profils gleich dem öffentlichen TCP-Port aus `server.json` | `Das Profil nennt TCP-Port P, /etc/funk/server.json aber O (...)` |
| der Name aus dem ersten Lauf (oder aus `profilanfrage.txt`) steht darin | `Der Name ... steht nicht im Profil (...)` |
| nicht abgelaufen | `FEHLT Das Profil ist am ... abgelaufen; die Clients verbinden weiter, warnen aber.` Eingespielt wird es trotzdem. |

Eingespielt heißt: Das bisherige Profil wird zu `profil-v<N>.json` (oder `profil.json.alt`), das neue kommt mit 0644
nach `/etc/funk/profil.json`, und es meldet `NEU Serverprofil Version N nach /etc/funk/profil.json (unterschrieben vom
Eigentümer, XXXX-XXXX-XXXX-XXXX; Pin, Name und Port passen)`. Fehlt ein passendes Profil, schreibt es die Anfragezeile nach
`/etc/funk/profilanfrage.txt`, zeigt `WARTET AUF PROFIL` und endet mit Exit 4. Der Server startet erst mit einem Profil.

### B5.5 Wenn der Start scheitert

`einrichten` liest `systemctl show` und das Journal und nennt die Ursache:

| Befund | Meldung von einrichten |
| --- | --- |
| `243/CREDENTIALS`, eine Schlüsseldatei fehlt | `systemd findet eine Datei aus LoadCredential nicht (status=243/CREDENTIALS): <datei> fehlt in /etc/funk/credentials.` Abhilfe: noch einmal aufrufen |
| `243/CREDENTIALS`, alle Dateien da, im LXC | `... Im Proxmox-Container kommt das auch, wenn dem LXC nesting=1 fehlt (berichtet für Debian 13).` Abhilfe: `pct set <id> --features nesting=1` und `pct reboot <id>` |
| `243/CREDENTIALS`, alle Dateien da, kein Container | die Pfade unter `LoadCredential` in der Unit mit `/etc/funk/credentials` vergleichen |
| `226/NAMESPACE` | `Der Dienst darf im Container keine eigenen Namensräume anlegen (status=226/NAMESPACE): Dem LXC fehlt nesting=1.` |
| `Konfiguration ungültig` im Journal | `Der Server lehnt die Konfiguration ab: ...`; `funk-admin pruefen --vollstaendig` zeigt alle Befunde |
| anderes | `funk-server startet nicht (...)` mit den letzten zehn Zeilen des Journals |
| nach 30 s kein `bereit` | `funk-server meldet nach 30 s nicht "bereit".` Abhilfe: `journalctl -u funk-server -n 50 --no-pager` |

### B5.6 Was es nie tut

- `/etc/funk/server.json` überschreiben. Soll es neu fragen: `mv /etc/funk/server.json /etc/funk/server.json.alt`, dann
  noch einmal aufrufen.
- Einen vorhandenen Schlüssel ersetzen.
- Einen fremden Kurzbefehl oder ein fremdes Drop-in ersetzen (nur Dateien mit `Angelegt von funk-admin einrichten`).
- Fremde Units, Dienste oder Ports anfassen.
- ufw ohne ausdrückliches `j` oder `ja` ändern; nftables, Proxmox-Firewall und Router nie.

### B5.7 Update mit dem Skript

Ein Update ist derselbe Befehl im Ordner `linux` des neuen Pakets: `sudo bash funk-einrichten.sh`. Es fragt, bevor es den
Dienst anhält, legt die alten Programme nach `/opt/funk.<fassung>`, ersetzt abweichende Units (die alten als `.alt`) und
startet den Dienst mit den neuen Programmen. Vorher sichern (B18). Zurück, aus dem Skript abgeleitet und nicht gemessen:
im Ordner `linux` des vorigen Pakets (ab 0.4.0) noch einmal `sudo bash funk-einrichten.sh`; es legt dann die neueren
Programme zur Seite. Ein von Hand eingerichteter Server (der Heimserver von Sarevoc) bleibt beim Update von Hand
(B19.3), bis das Skript im Probelauf gelaufen ist.

### B5.8 Probelauf im Wegwerf-Container

Das Skript ist auf Windows gebaut und so weit geprüft, wie es ohne Linux geht. Vor dem ersten echten Einsatz läuft es
einmal mit Debian 13, Debian 12 und Ubuntu 24.04 in einem Wegwerf-Container (`EINRICHTEN.md`, letzter Abschnitt):

1. `bash funk-einrichten.sh` mit den Vorschlägen und dem Namen `funk-probe.example`. Erwartet: .NET und libicu `NEU`, die
   Fragen, `WARTET AUF PROFIL`. Prüfen: `getent passwd funk funk-echo`, `ls -l /etc/funk/credentials` (vier Dateien
   `-rw-------`, Ordner `drwx------`), `ls -ld /opt/funk` (root, für andere nicht beschreibbar), `systemctl cat funk-echo`
   (Adresse aus `/etc/funk/echo.env`), `cat /usr/local/sbin/funk-admin`.
2. Ein Probeprofil mit einem eigenen Probe-Profilschlüssel, nie mit dem echten: im Container
   `dotnet /opt/funk/funk-admin.dll profilschluessel-erzeugen --schluessel /root/probe.key`, die Zeile mit `MFkw` hinten an
   `konfig/profilschluessel.txt` im ausgepackten Paket (nicht in `/opt/funk`) hängen, einen zweiten Pin mit
   `dotnet /opt/funk/funk-admin.dll schluessel-erzeugen --zertifikat /root/r.crt --schluessel /root/r.key`, dann
   `profil-schreiben` mit dem Pin aus der Anfragezeile und diesem zweiten Pin, `profil-signieren` mit `/root/probe.key`.
3. `bash funk-einrichten.sh --profil /root/profil.json`. Erwartet: `bereit`, Ports hören, Abschluss mit `Schutz:` und
   `erster-admin`. Dann `funk-admin sperrliste` (`Keine Sperren (...)`), `ls -ld /var/log/funk`, `ls
   /opt/funk/konfig/fail2ban` (`filter.d` und `jail.d`).
4. Noch einmal ohne alles: nur `OK` und `BLEIBT`, kein `NEU`.
5. Mit TCP 443 und "kein Router" in einem zweiten Container: Drop-in da, `ss -ltnp` zeigt 443 bei `dotnet`.
6. Fehlerbilder: ein Container ohne `nesting=1` (erwartet die `pct set`-Zeile, bei `226/NAMESPACE` oder
   `243/CREDENTIALS`; welches Debian 13 zeigt, ist noch nicht gemessen), `funk-totp-schluessel` umbenennen (erwartet
   `243/CREDENTIALS`), `apt install ufw && ufw enable` vor dem Lauf (erwartet die Frage nach den Regeln).
7. Umzug zwischen zwei Probecontainern nach B20, mit `umzug-packen` und `--umzug`.

Danach `pct stop 199` und `pct destroy 199`.

### B5.9 Echo-Funker und Echo-Probe

Der Echo-Funker ist ein Teilnehmer wie jeder Client (TLS mit Pin, eigener Geräteschlüssel) und spielt jede Sprechphase im
Netz Echo nach 500 ms zurück, höchstens 30 s je Phase. Er hat keine Namen und stellt keinen Antrag: Beim ersten Start holt
er einen Registrierungscode, den die Konsole mit `verknuepfen` einlöst, danach `freigeben`. Die erste Registrierung läuft
von Hand, damit der Code auf dem Bildschirm steht und nicht im Journal.

Hat `einrichten` den Echo-Funker vorbereitet (Benutzer `funk-echo`, `/var/lib/funk-echo`, Unit, `echo.env`), nennt der
Abschluss die Befehle. Erst nach dem ersten Admin ein Netz Echo anlegen und den Echo-Funker einmal von Hand starten:

```
sudo funk-admin netz-setzen --id 90 --name Echo --offen HOEREN_SENDEN --va
sudo runuser -u funk-echo -- /usr/bin/dotnet /opt/funk/funk-echo.dll --profil /etc/funk/profil.json --identitaet /var/lib/funk-echo/geraet.pem --adresse <adresse aus binden>
```

Er legt `geraet.pem` an und zeigt einen Registrierungscode samt der zwei Befehle, ganz ausgeschrieben; nur die Zahl hinter
`--discord` setzt du selbst ein (eine eindeutige Zahl, der Echo-Funker hat kein Discord-Konto). In einer zweiten Shell:

```
sudo funk-admin verknuepfen --code <code> --discord 100000000000000001 --benutzername echo-funker --name Echo
sudo funk-admin freigeben <anzeigeform>
```

In der ersten Shell steht dann `Bereit im Netz 'Echo' (ID 90)`. Strg+C, dann noch einmal `sudo bash funk-einrichten.sh`;
das startet ihn als Dienst. Von Hand eingerichtet (ohne Skript):

```
useradd --system --no-create-home --shell /usr/sbin/nologin funk-echo
install -d -m 0700 -o funk-echo -g funk-echo /var/lib/funk-echo
echo 'FUNK_ECHO_ADRESSE=192.168.1.250' > /etc/funk/echo.env
cp /opt/funk/konfig/funk-echo.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now funk-echo
journalctl -u funk-echo -n 20 --no-pager
```

(die erste Registrierung mit `runuser` wie oben, vor `enable --now`).

**Echo-Probe ohne Client**, im Container oder auf dem Server. Der Prüf-Client braucht wie jedes Gerät eine Freigabe, dann
spricht er eine künstliche Sekunde ins Netz Echo:

```
dotnet /opt/funk/funk-testclient.dll echo-probe --profil /etc/funk/profil.json --adresse 192.168.1.250
```

Er zeigt einen Code und die zwei Befehle und wartet 5 Minuten. Freigeben wie oben, aber mit einer **anderen** Zahl hinter
`--discord` (etwa `100000000000000002`), sonst landet die Probe im Konto des Echo-Funkers und verdrängt ihn (ein Konto hat
eine Sitzung):

```
sudo funk-admin verknuepfen --code <code der probe> --discord 100000000000000002 --benutzername echo-probe --name Probe
sudo funk-admin freigeben <anzeigeform der probe>
```

**Das siehst du:** `echo-probe` endet mit `BESTANDEN` (50 von 50 Frames, Reihenfolge richtig, Pause ab 500 ms); das Journal
von `funk-echo` zeigt je Satz "Aufnahme beginnt", "Aufnahme ... beendet" und "Wiedergabe fertig". Danach das Gerät der
Probe wieder entfernen (`sudo funk-admin entfernen <anzeigeform der probe>`); das Konto `Probe` darf bleiben. Mit dem
Client: ein Feld mit dem Netz Echo belegen, einen Satz sprechen, eine halbe Sekunde später hörst du ihn zurück. Von außen
(Handy-Hotspot) geprüft, ist damit der ganze Weg über Router und DNS belegt.

## B6 server.json

### B6.1 Form und Regeln

- Gegenüber 0.4.0 hat sich an `server.json` nichts geändert: kein neues Feld, kein anderer Standardwert (am Code geprüft:
  `Konfiguration.cs` und `SperrlistenKonfiguration.cs` sind seit dem Stand `8c73864` nur um die Kopfzeile länger). Eine `server.json` aus 0.4.0 gilt mit 0.5.0 unverändert.
- JSON mit Kommentaren (`//`). Ein Komma nach dem letzten Eintrag ist ein Fehler.
- **Unbekannte Felder sind ein Fehler**, damit ein Tippfehler nicht still einen Standardwert ergibt. Ein vertippter Name
  ergibt `The JSON property '<name>' could not be mapped ...`, ohne Zeilennummer.
- Relative Pfade gelten ab dem Verzeichnis der Datei. `${NAME}` wird aus der Umgebung ersetzt; unter systemd setzt
  `LoadCredential` die Variable `${CREDENTIALS_DIRECTORY}`. An der Konsole gibt es sie nicht: `funk-admin` nimmt dort
  `--credentials <ordner>`, sonst `/etc/funk/credentials`.
- Keine Geheimnisse in der Datei, nur Pfade zu ihnen.
- Prüfen ohne Start, zwei Werkzeuge:

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
funk-admin pruefen --vollstaendig
```

`funk-server ... pruefen` liest Konfiguration und Profil wie der Start, zeigt eine Zeile zur Uhr und endet mit
`Konfiguration in Ordnung.` (Exit 0), `Konfiguration lesbar, aber bitte die Warnung oben ansehen.` (Exit 0) oder
`Konfiguration ungültig:` mit den Gründen (Exit 2). Ob die Schlüsseldateien da sind, sieht es nicht; das zeigt
`funk-admin pruefen --vollstaendig` (B9.3) oder erst der Start.

**Ändern, immer so:** erst prüfen, nur bei Erfolg neu starten.

```
nano /etc/funk/server.json
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen && systemctl restart funk-server
```

`Konfiguration nicht lesbar: Konfiguration ist kein gültiges JSON: ...` heißt meist ein fehlendes oder überzähliges
Komma oder Anführungszeichen. Die Meldung nennt die Zeile als `LineNumber`, gezählt ab 0: `LineNumber: 2` ist in nano die
dritte Zeile.

### B6.2 Alle Felder

"Standard" ist der Wert, wenn das Feld fehlt. Die Spalte "Beispiel" zeigt `konfig/server.beispiel.json`.

| Feld | Standard | Beispiel | Regel beim Prüfen | Bedeutung |
| --- | --- | --- | --- | --- |
| `binden` | leer | `["192.168.1.50"]` | Pflicht, außer mit `alleAdressen`; nur einzelne IPs, keine doppelt, nicht `0.0.0.0` oder `::` | Adressen, an die TCP und UDP binden. Leer: `Keine Bindadresse: ohne ausdrücklich konfigurierte Adressen startet der Server nicht (4.1).` |
| `alleAdressen` | `false` | `false` | schließt `binden` aus | an `0.0.0.0` und `::` binden, mit Warnung im Logbuch |
| `tcpPort` | 8443 | 8443 | 1 bis 65535 | TCP-Bindport |
| `oeffentlicherTcpPort` | wie `tcpPort` | 443 | 1 bis 65535 | Port, den die Clients von außen erreichen; muss zum Port im Profil passen (sonst Port-Warnung) |
| `udpPort` | 47443 | 47443 | 1 bis 65535 | UDP-Bindport |
| `oeffentlicherUdpPort` | wie `udpPort` | 47443 | 1 bis 65535 | öffentlicher UDP-Port; ihn nennt der Server den Clients in Welcome |
| `nurTls13` | `false` | `false` | | TLS 1.2 abschalten, sobald niemand mehr Windows 10 nutzt |
| `zustandsverzeichnis` | `zustand` | `/var/lib/funk` | | Zustandsdateien (B1.4) |
| `identitaet.zertifikat` | `server.crt` | `${CREDENTIALS_DIRECTORY}/server.crt` | Pflicht | Zertifikat des Servers (PEM) |
| `identitaet.schluessel` | `server.key` | `${CREDENTIALS_DIRECTORY}/server.key` | Pflicht | privater Schlüssel (PKCS#8, PEM, P-256) |
| `geheimnis` | `geheimnis.bin` | `${CREDENTIALS_DIRECTORY}/funk-geheimnis` | Pflicht; Datei 32 bis 4096 Byte | Betriebsgeheimnis |
| `totpSchluessel` | Pflicht in der Datei | `${CREDENTIALS_DIRECTORY}/funk-totp-schluessel` | Pflicht; Datei genau 32 Byte | Schlüssel für die TOTP-Geheimnisse der Admins. Fehlt das Feld (eine Datei aus 0.1.0): `totpSchluessel fehlt (neu seit 0.2.0, ...)` mit den drei nötigen Schritten, Exit 2 |
| `profil` | keiner | `/etc/funk/profil.json` | Pflicht | signiertes Serverprofil; ohne lesbares Profil startet der Server nicht |
| `mindestClientVersion` | `0.1.0` | `0.1.0` | gültige Versionsnummer | darunter bekommt ein Client `VERSION_ZU_ALT` statt Welcome (B6.3) |
| `mitgliedskarte.rollenFuerAlle` | `true` | `true` | | Rollen und Wünsche auf der Mitgliedskarte für alle sichtbar; `false`: nur Einsatzleitung, Admins und das Konto selbst |
| `verwaltungsSocket` | `<zustand>/verwaltung.sock` | `/run/funk/verwaltung.sock` | | Pfad des Verwaltungssockets |
| `verwaltungsUids` | leer | leer | | weitere UIDs am Verwaltungssocket, mit vollen Rechten; Warnung beim Start |
| `vertrauenswuerdigeWeiterleiter` | leer | leer | nur IP-Adressen | Vorposten (etwa ein VPS mit WireGuard, nur DNAT), die Absenderadressen erhalten; für sie gelten nur globale Töpfe, und sie stehen nie auf der Sperrliste |
| `grenzen.ausgangMbit` | 35 | 35 | über 0 bis 100000 | Budget des Pacers; höchstens 80 % des gemessenen Netto-Uploads |
| `grenzen.leitungsZuschlag` | 26 | 26 | 0 bis 1000 | Byte je Paket für PPPoE und Ethernet |
| `grenzen.maxSitzungen` | 100 | 100 | 1 bis 10000 | gleichzeitige Sitzungen |
| `grenzen.unbekannteGlobal` | 20 | 20 | ab 0 | unfreigegebene Verbindungen nach TLS, serverweit; freigegebene Geräte zählen nie |
| `grenzen.unbekannteJePraefix` | 2 | 2 | ab 0 | dasselbe je Präfix |
| `grenzen.verbindungenJePraefix` | 8 | 8 | ab 1 | gleichzeitige TCP-Verbindungen je Präfix (gemeinsames NAT) |
| `grenzen.handshakesUnbekanntJeMinute`, `...Stoss` | 10, 5 | 10, 5 | positiv, Stoß ab 1 | Handshake-Topf je Präfix |
| `grenzen.handshakesVorrangJeMinute`, `...Stoss` | 60, 10 | 60, 10 | positiv, Stoß ab 1 | Topf für Präfixe der Vorrangspur |
| `grenzen.handshakesGlobalJeSekunde` | 200 | 200 | positiv | nur CPU-Schutz; die Vorrangspur geht daran vorbei |
| `grenzen.udpEmpfangspuffer` | 1048576 | 1048576 | 64 KiB bis 16 MiB | Empfangspuffer der UDP-Sockets |
| `grenzen.relaisKapazitaet` | 4096 | 4096 | 64 bis 1000000 | Warteschlange zum Relay-Thread; voll heißt verwerfen |
| `sperrliste.aktiv` | `true` | `true` | | `false` nur zur Fehlersuche, mit Warnung beim Start |
| `sperrliste.schwelle` | 10 | 10 | 1 bis 1000 | so viele Punkte im Fenster sperren ein Präfix |
| `sperrliste.fensterSekunden` | 600 | 600 | 10 bis 86400 | Fenster für die Punkte |
| `sperrliste.ersteSperreSekunden` | 300 | 300 | 10 bis 86400 | Dauer der ersten Sperre |
| `sperrliste.faktor` | 6 | 6 | 1 bis 100 | jede weitere Sperre so viel länger |
| `sperrliste.hoechstdauerStunden` | 24 | 24 | 1 bis 720, nicht kürzer als die erste Sperre | Obergrenze |
| `sperrliste.vergessenStunden` | 48 | 48 | 1 bis 720 | so lange nach der letzten Sperre merkt sich die Liste die Stufe |
| `sperrliste.durchlassSekunden` | 30 | 30 | 5 bis 3600 | während einer Sperre je Adresse alle so viele Sekunden eine Verbindung bis zum Handshake |
| `sperrliste.maxEintraege` | 10000 | 10000 | 100 bis 1000000 | höchstens so viele Präfixe verfolgt; Sperren fallen nie heraus |
| `sperrliste.v6Sammelschwelle` | 4 | 4 | 0 (aus) oder 2 bis 1000 | so viele gesperrte /64 aus einem /48 sperren das /48 |
| `sperrliste.ausnahmen` | leer | leer | Adresse oder Netz (CIDR) | nie gesperrt, etwa `"192.168.1.0/24"` |
| `sperrliste.klartextDatei` | `null` | `null` | nicht leer | je neuer Sperre eine Zeile mit Klartext-IP, nur für fail2ban (B16) |
| `log.stufe` | `INFO` | `INFO` | `DEBUG`, `INFO`, `WARNUNG`, `FEHLER` | Mindeststufe des Logbuchs |
| `log.datei` | `null` | `null` | | zusätzlich in diese Datei |
| `test.rotationSekunden`, `test.zufallsPorts` | | | | nur Test- und Dauerläufe, nie im Betrieb |

"Präfix" heißt bei IPv4 die einzelne Adresse, bei IPv6 das /64; IPv4 in IPv6-Form zählt als IPv4.

### B6.3 mindestClientVersion

- Bleibt `0.1.0`, solange Mitglieder ältere Clients haben. Clients 0.1.0 melden fest `0.1.0`, ab 0.2.0 ihre echte Fassung.
- Vor einem Umzug kommt `0.4.0`, aber erst, wenn alle aktiven Mitglieder das Setup 0.4.0 (oder neuer) haben: Erst
  0.4.0 übernimmt ein neueres Profil vom Server (B20, Schritt 1).
- Nur die Zahl ändern, Anführungszeichen und Komma bleiben, dann `pruefen && systemctl restart funk-server`.

### B6.4 Zwei vollständige Beispiele

Heimbetrieb im Container (gekürzt nach `konfig/server.beispiel.json`, mit den Werten aus `HEIMSERVER-PROXMOX.md`):

```
{
  "binden": ["192.168.1.250"],
  "alleAdressen": false,
  "tcpPort": 8443,
  "oeffentlicherTcpPort": 8443,
  "udpPort": 47443,
  "oeffentlicherUdpPort": 47443,
  "nurTls13": false,
  "zustandsverzeichnis": "/var/lib/funk",
  "identitaet": {
    "zertifikat": "${CREDENTIALS_DIRECTORY}/server.crt",
    "schluessel": "${CREDENTIALS_DIRECTORY}/server.key"
  },
  "geheimnis": "${CREDENTIALS_DIRECTORY}/funk-geheimnis",
  "totpSchluessel": "${CREDENTIALS_DIRECTORY}/funk-totp-schluessel",
  "profil": "/etc/funk/profil.json",
  "mindestClientVersion": "0.1.0",
  "mitgliedskarte": { "rollenFuerAlle": true },
  "verwaltungsSocket": "/run/funk/verwaltung.sock",
  "verwaltungsUids": [],
  "vertrauenswuerdigeWeiterleiter": [],
  "grenzen": { "ausgangMbit": 35 },
  "log": { "stufe": "INFO", "datei": null }
}
```

Ein Abschnitt darf einzelne Felder nennen; die übrigen behalten ihren Standard. Das Beispiel im Paket nennt alle
`grenzen` und `sperrliste` mit ihren Standardwerten, und es steht dort `"oeffentlicherTcpPort": 443`. Daheim gehört dort
8443 hin, sonst warnt der Server (Port-Warnung, B21).

Gemieteter Server mit TCP 443, so wie `einrichten` und `konfig-schreiben` sie schreiben (gemessen mit
`konfig-schreiben --binden 192.0.2.10 --tcp-port 443`; hier mit der Beispieladresse):

```
// Geschrieben von funk-admin konfig-schreiben am <JJJJ-MM-TT HH:MM> UTC.
// Felder: BETRIEB.md, Abschnitt Konfiguration. Keine Geheimnisse: Schlüssel, Betriebsgeheimnis und TOTP-Schlüssel
// kommen per LoadCredential aus /etc/funk/credentials (funk-server.service).
{
  "binden": [
    "203.0.113.10"
  ],
  "alleAdressen": false,
  "tcpPort": 443,
  "udpPort": 47443,
  "nurTls13": false,
  "zustandsverzeichnis": "/var/lib/funk",
  "identitaet": {
    "zertifikat": "${CREDENTIALS_DIRECTORY}/server.crt",
    "schluessel": "${CREDENTIALS_DIRECTORY}/server.key"
  },
  "geheimnis": "${CREDENTIALS_DIRECTORY}/funk-geheimnis",
  "totpSchluessel": "${CREDENTIALS_DIRECTORY}/funk-totp-schluessel",
  "profil": "/etc/funk/profil.json",
  "mindestClientVersion": "0.1.0",
  "mitgliedskarte": {
    "rollenFuerAlle": true
  },
  "verwaltungsSocket": "/run/funk/verwaltung.sock",
  "log": {
    "stufe": "INFO"
  }
}
```

`oeffentlicherTcpPort` und `oeffentlicherUdpPort` schreibt es nur, wenn sie vom Bindport abweichen (mit Router).

## B7 systemd-Units

### B7.1 funk-server.service

Die Unit aus dem Paket (`konfig/funk-server.service`), nach `/etc/systemd/system/` kopiert. Angepasst wird darin nichts;
Abweichungen gehören in ein Drop-in unter `/etc/systemd/system/funk-server.service.d/`.

```
[Unit]
Description=JG TacRelay Server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=funk
Group=funk
ExecStart=/usr/bin/dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json
Restart=on-failure
RestartSec=2
KillSignal=SIGTERM
TimeoutStopSec=10

LoadCredential=server.crt:/etc/funk/credentials/server.crt
LoadCredential=server.key:/etc/funk/credentials/server.key
LoadCredential=funk-geheimnis:/etc/funk/credentials/funk-geheimnis
LoadCredential=funk-totp-schluessel:/etc/funk/credentials/funk-totp-schluessel

StateDirectory=funk
StateDirectoryMode=0700
RuntimeDirectory=funk
RuntimeDirectoryMode=0700
LogsDirectory=funk
LogsDirectoryMode=0750

LimitCORE=0
Environment=DOTNET_DbgEnableMiniDump=0
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1

NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
PrivateDevices=true
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectKernelLogs=true
ProtectControlGroups=true
ProtectClock=true
ProtectHostname=true
RestrictNamespaces=true
RestrictRealtime=true
RestrictSUIDSGID=true
LockPersonality=true
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6
SystemCallArchitectures=native
CapabilityBoundingSet=
AmbientCapabilities=
UMask=0077

[Install]
WantedBy=multi-user.target
```

(Die Kommentarzeilen der Datei sind hier weggelassen. Seit 0.5.0 steht als erste Zeile `# JG TacRelay - Maintainer: Sarevoc`;
das ist die einzige Änderung an dieser Unit und an `funk-echo.service` gegenüber 0.4.0, die Einstellungen sind dieselben. Eine
Unit aus 0.4.0 läuft mit 0.5.0 unverändert weiter, B19.6.)

| Block | Wirkung |
| --- | --- |
| `User=funk`, `ExecStart` | läuft als Dienstbenutzer mit der installierten Laufzeit unter `/usr/bin/dotnet` |
| `Restart=on-failure`, `RestartSec=2` | startet nach einem Absturz neu; nach zu vielen Fehlstarts gibt systemd auf (`start request repeated too quickly`) |
| `KillSignal=SIGTERM`, `TimeoutStopSec=10` | geordnetes Beenden mit `Bye{WARTUNG}` an alle (B1.3) |
| `LoadCredential=` | systemd stellt die vier Dateien aus `/etc/funk/credentials` unter `$CREDENTIALS_DIRECTORY` bereit; fehlt eine, startet der Dienst gar nicht (`status=243/CREDENTIALS`) |
| `StateDirectory`, `RuntimeDirectory`, `LogsDirectory` | legen `/var/lib/funk`, `/run/funk` und `/var/log/funk` mit Besitzer `funk` an; nur dort darf der Dienst schreiben. `LogsDirectory` braucht es nur für die Klartextdatei der Sperrliste |
| `LimitCORE=0`, `DOTNET_DbgEnableMiniDump=0` | keine Speicherabbilder mit Sitzungs- oder Identitätsschlüsseln (Protokoll 7.5) |
| `ProtectSystem=strict` bis `UMask=0077` | Härtung: System schreibgeschützt, kein Zugriff auf `/home`, eigenes `/tmp`, keine Geräte, kein Verstellen von Kernel und Uhr, nur die Adressfamilien UNIX, IPv4 und IPv6, keine Fähigkeiten. `MemoryDenyWriteExecute` bleibt aus, weil .NET JIT-Code erzeugt |
| Härtung insgesamt | legt für den Dienst eigene Mount-Namensräume an; dafür braucht der unprivilegierte LXC `nesting=1`, ohne nesting startet der Dienst nicht (`226/NAMESPACE`) |
| `ProtectClock=true` | sperrt dem Dienst auch das Lesen des Uhrzustands; deshalb zeigt erst `pruefen` in der Shell, ob die Uhr synchronisiert ist |

Für Ports unter 1024 kommt das Drop-in aus B4.4 dazu.

### B7.2 funk-echo.service

Wie `funk-server.service`, mit diesen Unterschieden:

| Zeile | Wert | Warum |
| --- | --- | --- |
| `After=` | `network-online.target funk-server.service` | startet nach dem Server |
| `User=`, `Group=` | `funk-echo` | eigener Benutzer, eigener Geräteschlüssel |
| `Environment=FUNK_ECHO_ADRESSE=192.168.1.50` und `EnvironmentFile=-/etc/funk/echo.env` | Voreinstellung und Datei | die Adresse steht in `/etc/funk/echo.env`, damit niemand die Unit ändern muss; fehlt die Datei, gilt die Voreinstellung (dann erreicht der Echo-Funker deinen Server meist nicht) |
| `ExecStart=` | `/usr/bin/dotnet /opt/funk/funk-echo.dll --profil /etc/funk/profil.json --identitaet /var/lib/funk-echo/geraet.pem --adresse ${FUNK_ECHO_ADRESSE}` | `--adresse` ist die Adresse aus `binden`; geprüft wird trotzdem der Pin aus dem Profil |
| `RestartSec=` | 5 | |
| `StateDirectory=funk-echo` | 0700 | dort liegt `geraet.pem` |
| `RestrictAddressFamilies=` | `AF_UNIX AF_INET AF_INET6 AF_NETLINK` | .NET meldet im Client Adressänderungen über Netlink; ohne `AF_NETLINK` bricht der Sprachweg des Echo-Funkers beim Start ab |
| keine `LoadCredential`-Zeilen | | der Echo-Funker hat keine Servergeheimnisse |

Der Echo-Funker verbindet nach einer Verdrängung oder Abweisung nicht selbst neu: Er wartet die Ruhezeit (Voreinstellung
600 s) und endet dann mit Code 3 (verdrängt) oder Code 4 (abgewiesen: Konto oder Gerät gesperrt, Verdrängen gesperrt,
falscher Pin); systemd startet ihn danach neu. Den Grund nennt das Journal in der Zeile davor.

### B7.3 Befehle für die Units

```
systemctl daemon-reload                     # nach jeder Änderung an einer Unit oder einem Drop-in
systemctl enable --now funk-server          # einschalten und starten
systemctl restart funk-server               # neu starten (Clients bekommen Bye{WARTUNG})
systemctl stop funk-echo funk-server        # anhalten
systemctl disable --now funk-server         # anhalten und beim Booten nicht mehr starten
systemctl is-active funk-server funk-echo   # active oder inactive oder failed
systemctl status funk-server
systemctl reset-failed funk-server          # nach "start request repeated too quickly"
systemctl cat funk-server                   # Unit samt Drop-ins, wie systemd sie liest
systemctl cat funk-server | grep ^LoadCredential
journalctl -u funk-server -f                # Logbuch laufend
journalctl -u funk-server -n 30 --no-pager  # die letzten 30 Zeilen
journalctl -u funk-echo -n 20 --no-pager
```

**Das siehst du beim Start:** `INFO Serverprofil Version N mit M Pins geladen` und
`INFO Funk-Server 0.5.0 bereit: 1 Adresse(n), TCP Bindport 8443 (öffentlich 8443), UDP Bindport 47443 (öffentlich 47443),
TLS 1.3/1.2, Pin XXXX-XXXX-XXXX-XXXX`. Davor, solange es keinen Eigentümer gibt, `WARNUNG Kein Eigentümer gesetzt:
funk-admin eigentuemer --konto <id> (3.10). Bis dahin ist kein Konto im Client vor anderen Admins geschützt.`

Weitere Warnungen beim Start, je eine Zeile: Uhr offensichtlich falsch, Sperrliste ausgeschaltet, Pin fehlt im Profil,
Profil nennt einen anderen TCP-Port, Bindung an alle Adressen, zwei Konten mit demselben Discord-Namen,
`verwaltungsUids` nicht leer, vertrauenswürdige Weiterleiter konfiguriert. Was dann zu tun ist, steht in B21.

## B8 Serverprofil und Pins

### B8.1 Was ein Profil ist

Das Serverprofil sagt den Clients, wo der Server ist und woran sie ihn erkennen. Es ist ein signiertes Objekt
`{daten, signatur}` (Protokoll 3.1):

| Teil von `daten` | Grenzen | Bedeutung |
| --- | --- | --- |
| `name` | bis 32 Zeichen | Name des Funks, im Beispiel `Meine Staffel` |
| `profilVersion` | ganze Zahl (u32) | Clients nehmen nur eine höhere Version an als die höchste bekannte |
| `ausgestellt`, `gueltigBis` | ms seit 1970 | Voreinstellung 365 Tage; nach Ablauf bleibt das Profil in Gebrauch, der Client warnt |
| `hosts` | 1 bis 4, je Name und Familien (IPv4, IPv6) | in dieser Reihenfolge versucht der Client die Hosts |
| `tcpPort` | einer für alle Hosts | der öffentliche TCP-Port |
| `pins` | 2 bis 3, verschieden | SHA-256 über das SubjectPublicKeyInfo der Serverzertifikate: der aktuelle Server und mindestens eine Reserve |
| `nameserver` | 0 bis 4, wahlweise | autoritative Nameserver der DynDNS-Zone |

Die Signatur ist ECDSA P-256 mit SHA-256 über `"tacrelay profil v1"`, ein Nullbyte und genau die Bytes von `daten`.
Unterschrieben wird offline mit `profil.key`; dessen öffentlicher Teil ist in jeden Client eingebaut. Den
Profilschlüssel hat nur Sarevoc, und er liegt nie auf einem Server.

**Pin und Kennung:** Ein Pin ist 44 Zeichen Base64 (`profil-schreiben --pin`, Anfragezeile). Seine Anzeigeform, die
"Kennung", ist `XXXX-XXXX-XXXX-XXXX`; sie steht im Journal beim Start, bei `pruefen` und in `profil-erneuern` und wird am
Telefon vorgelesen.

### B8.2 Wie ein Profil zu den Clients kommt

1. **Mit dem Setup:** Setup und Admin-Setup bringen `profil.json` und den öffentlichen Profilschlüssel mit. Im Client
   trägt niemand etwas ein, damit niemand einem Mitglied ein falsches Profil unterschieben kann. Ein neues Profil im Setup
   heißt: `profil.json` nach `konfig\profil.json` im Repository, die Zeile mit `MFkw` nach `konfig\profilschluessel.txt`,
   dann beide Setups neu bauen (`tools\setup-bauen.ps1`, mit und ohne `-Verwaltung`). Das ist Sache von Sarevoc.
2. **Über den Server (ab Client 0.4.0):** Der Server reicht `/etc/funk/profil.json` nach der Freigabe in Welcome durch.
   Der Client übernimmt es nur, wenn die Signatur mit einem eingebauten Schlüssel gilt, der Inhalt dem Schema entspricht,
   die Version höher ist als die höchste bekannte und **der Pin des Servers darin steht, der es gerade liefert**. Er
   legt es als `serverprofil.json` neben seine Einstellungen; wirksam wird es beim nächsten Verbindungsaufbau, und die
   Profilkarte zeigt `Vom Server übernommen, Version N.` Clients 0.2.0 und 0.3.0 übernehmen nichts.

Beim ersten Verbinden versucht ein Client 0.4.0 nach einem fremden Pin erst die übrigen Hosts des Profils, bevor er
"Server nicht vertrauenswürdig" meldet; beim Wiederverbinden meidet er einen solchen Host 60 s.

### B8.3 Ein Profil für einen neuen Server (Anfrage, Telefon, Einspielen)

Der Betreiber erzeugt auf dem Server eine Anfrage, Sarevoc unterschreibt auf dem Stick, der Betreiber spielt das
Profil ein.

**1. Anfrage auf dem Server.** `funk-einrichten.sh` endet ohne Profil mit `WARTET AUF PROFIL` und der Zeile; sie liegt auch
in `/etc/funk/profilanfrage.txt`. Ohne Skript:

```
funk-admin profil-anfrage --host funk.example.org
```

Ohne `--tcp-port` und `--zertifikat` nimmt es `oeffentlicherTcpPort` und das Zertifikat aus der Konfiguration
(`--credentials` oder `/etc/funk/credentials`). Ausgabe (mit eigenem Probe-Pin am 01.10.2026 gemessen; der Satz mit dem Eigentümer
für 0.5.0 aus dem Code gelesen):

```
TR-PROFILANFRAGE v1 funk.example.org 443 <pin, 44 Zeichen Base64>
Kennung zum Vorlesen: XXXX-XXXX-XXXX-XXXX
Schick dem Eigentümer die Zeile oben, egal auf welchem Weg; sie ist nicht geheim. Die Kennung, den Namen funk.example.org und den Port 443 liest du ihm am Telefon vor,
damit er sicher ist, dass die Zeile von dir kommt und unterwegs nicht verändert wurde.
```

Mit `--aus <datei>` schreibt es die Zeile zusätzlich in eine Datei, etwa als `profilanfrage.txt` für den Stick. Eine
Anfrage nennt genau einen Host.

**2. Die Zeile an Sarevoc,** auf jedem Weg (Discord geht), sie ist nicht geheim. **Dann anrufen** und Kennung, Name
und Port vorlesen. Die Kennung deckt nur den Pin; Name und Port stehen ungeschützt in der Zeile. Erst der Vergleich aller
drei am Telefon zeigt Sarevoc, dass die Zeile vom Betreiber kommt und unterwegs niemand etwas geändert hat. Schickt
jemand anderes eine Zeile mit eigenem Namen und Pin und Sarevoc unterschreibt sie, verbinden sich alle Clients mit
dessen Server.

**3. Sarevoc unterschreibt** mit `profil-erneuern` (B8.4) und schickt `zum-verschicken\profil.json` zurück.

**4. Einspielen.** Mit dem Skript, im Ordner `linux`:

```
sudo bash funk-einrichten.sh --profil profil.json
```

Von Hand, auf einem laufenden Server:

```
install -m 0644 /root/profil.json /etc/funk/profil.json
funk-admin pruefen --vollstaendig
funk-admin profil-neu-laden
```

Erst prüfen, dann laden: `pruefen --vollstaendig` muss `OK Pin des Serverschlüssels ... steht im Profil.` zeigen und
darf keine Port-Warnung haben. `profil-neu-laden` antwortet `Profil Version N geladen.`; fehlt der Pin des Servers im
Profil oder nennt es einen anderen TCP-Port, steht darunter `WARNUNG ...`, dieselbe Zeile im Journal. **Geladen ist das
Profil dann trotzdem** (wie beim Start). Also sofort die vorige Fassung zurück nach `/etc/funk/profil.json` und noch einmal
`profil-neu-laden`. Verbundene Clients bekommen ein neues Profil erst mit dem nächsten Welcome; soll es sofort zu allen,
`systemctl restart funk-server` (alle verbinden sich einmal neu, also nicht am Spielabend).

### B8.4 profil-erneuern auf dem Stick des Eigentümers

Der eine Schritt, der beim Eigentümer bleibt (Anf. A55). Im Ordner `funk-offline` auf dem Stick liegen `profil.json`,
`profil.key`, `reserve.crt`, `reserve.key` und die Programme aus dem Ordner `linux` des Serverpakets **0.4.0 oder neuer**;
ein älteres `funk-admin.dll` antwortet `Unbekannter Befehl: profil-erneuern. funk-admin hilfe zeigt alle.` Programme
aktualisieren: den ganzen Inhalt von `linux` des neuen Pakets nach `funk-offline` kopieren und vorhandene Dateien ersetzen
lassen; `profil.json`, `profil.key`, `reserve.key` und `reserve.crt` bleiben liegen, das Paket hat keine Dateien mit diesen
Namen.

**Doppelklick** auf `Profil erneuern.cmd`. Die Datei enthält:

```
@cd /d "%~dp0"
@dotnet funk-admin.dll profil-erneuern --bisher profil.json --schluessel profil.key
@pause
```

Oder in PowerShell im Ordner `funk-offline`, mit allen Optionen:

```
dotnet funk-admin.dll profil-erneuern --bisher profil.json --schluessel profil.key [--anfrage <zeile|datei>] [--reserve <crt|key|pin>] [--gueltig-tage 365]
```

Ablauf:

1. Es liest `profil.key`, prüft das bisherige Profil damit und zeigt es: Name, Version, gültig bis, TCP-Port, Hosts und
   Pins (die Reserve gekennzeichnet). Die Reserve findet es in `reserve.crt` oder `reserve.key` neben `profil.key` oder
   über `--reserve`.
2. `Was soll ins neue Profil?` Auf `Eingabe:` fügst du die Anfragezeile ein (Rechtsklick), drückst die Eingabetaste für
   `profilanfrage.txt` neben `profil.json`, oder `v` für nur verlängern.
3. Es baut das neue Profil und zeigt je Host und Pin `Neu:`, `Bleibt:` oder `Fällt weg:`, dazu einen Portwechsel:

| Fall | Was geschieht |
| --- | --- |
| Übergang eines Umzugs: neuer Host, neuer Pin | Host und Pin kommen dazu, der alte Server bleibt drin |
| Abschluss: Host und Pin stehen schon drin, daneben noch ein alter Server | Frage `Den alten Server herausnehmen? Dann bleiben nur <host>, der Pin <kennung> und die Reserve <kennung>.` |
| nur verlängern (`v`) | Version plus 1, neues Ablaufdatum, sonst alles gleich |
| mehr als 3 Pins oder 4 Hosts | es fragt mit Nummern, was wegfällt; die Reserve steht nie zur Wahl |
| anderer TCP-Port in der Anfrage, während ein anderer Host im Profil bleibt | Ablehnung vor der Telefonfrage: `TCP-Port 8443 -> 443: ...` und `Nichts geändert.` |
| der neue Server benutzt den Reserveschlüssel | Ablehnung; eine neue Reserve trägt `profil-erneuern` noch nicht ein (dann `profil-schreiben` und `profil-signieren` von Hand) |

4. Die Telefonfrage: `Hat der Server-Admin dir am Telefon genau diese Kennung vorgelesen: XXXX-XXXX-XXXX-XXXX, dazu den Namen
   funk.example.org und den Port 8443? (j/n)`. Nur wenn alle drei Zeichen für Zeichen stimmen: `j`. Ohne Anfrage fragt
   es `Profil Version N so schreiben?`.
5. Es unterschreibt, prüft das Ergebnis gegen den öffentlichen Teil von `profil.key` (`Urteil: Uebernehmen`), legt die
   alte Fassung als `profil-v<N>.json` daneben (überschrieben wird nie), schreibt das neue `profil.json` und eine Kopie nach
   `zum-verschicken\profil.json`.

Vor dem `j` wird nichts geschrieben. `--gueltig-tage` nimmt 1 bis 3650. Exit 0 heißt geschrieben, 1 nichts geändert.

**Wann:** für jeden neuen Server (Anfrage), für einen Umzug (Übergang und Abschluss, B20) und vor dem Ablauf. Ab 30 Tagen
vor dem Ablauf meldet `pruefen --vollstaendig` `HINWEIS Das Profil läuft am ... ab`; die Profilkarte im Client zeigt
`gültig bis`. Nach dem Verlängern das neue Profil auf den Server legen (B8.3, Schritt 4); Clients ab 0.4.0 übernehmen
es dort, ältere brauchen ein neues Setup.

### B8.5 Profil von Hand (das erste Profil, Sonderfälle)

So entstand das erste Profil für den Heimserver, am Offline-Rechner im Ordner `funk-offline`:

```
dotnet funk-admin.dll profilschluessel-erzeugen --schluessel profil.key
dotnet funk-admin.dll schluessel-erzeugen --zertifikat reserve.crt --schluessel reserve.key
dotnet funk-admin.dll profil-schreiben --name "Meine Staffel" --version 1 --host funk.example.org/v4 --tcp-port 8443 --pin <pin des servers> --pin <pin der reserve> --aus daten.json
dotnet funk-admin.dll profil-signieren --daten daten.json --schluessel profil.key --aus profil.json
dotnet funk-admin.dll profil-pruefen --profil profil.json --spki <spki des profilschluessels>
```

- `profilschluessel-erzeugen` gibt den öffentlichen Teil aus, eine Zeile mit `MFkw` (SPKI, Base64), und darunter
  `Fingerabdruck: XXXX-XXXX-XXXX-XXXX`. Die Zeile ist öffentlich und kommt in die Clients; `profil.key` bleibt auf dem Stick.
  Ein neuer Profilschlüssel heißt neue Setups für alle.
- `profil-schreiben` prüft die Grenzen aus B8.1 (`Profil verletzt 3.1 (Hosts 1-4, Pins 2-3 und verschieden, Port, Namen).`)
  und schreibt nur `daten`; `--nameserver` darf bis zu viermal stehen, `--gueltig-tage` ist 365, wenn es fehlt.
- `profil-pruefen` meldet `Urteil: Uebernehmen` und listet Hosts und Pins; Exit 0 nur dann. `--spki` darf mehrmals stehen.

Den Pin des Servers zeigt `schluessel-erzeugen` (`Pin (für pins im Serverprofil): ...`), später `profil-anfrage`.

## B9 funk-admin: Referenz

### B9.1 Aufruf, Optionsprüfung, Exitcodes

```
funk-admin <befehl> [gerät] [--option wert] [--schalter]
funk-admin hilfe
```

- `hilfe` (auch `--hilfe`, `-h`) zeigt alle Befehle; die Liste unten ist dieselbe, nach Aufgaben geordnet.
- **Wo der Server ist:** `--konfig <server.json>` (der Socketpfad steht darin) oder `--socket <pfad>`; ohne beides gilt die
  Umgebungsvariable `FUNK_KONFIG`. Der Kurzbefehl `/usr/local/sbin/funk-admin` setzt `FUNK_KONFIG=/etc/funk/server.json`,
  wenn sie fehlt. Jeder Befehl nimmt `--konfig` und `--socket` an, die Offline-Befehle ohne Wirkung.
- `--json-aus` (nur Befehle über den Server) gibt `{"ok": ..., "text": ..., "daten": ...}` aus, für Skripte.
- Werte mit Leerzeichen gehören in Anführungszeichen. In bash am sichersten in einfache (`'...'`): In doppelten verändert
  bash `$`, den Backtick, `\` und `"`. Ein einfaches Anführungszeichen im Wert selbst schreibst du als `'\''`, etwa
  `--namen 'O'\''Neil'`.
- Optionen, die mehrfach stehen dürfen: `--host`, `--pin`, `--nameserver`, `--spki`, `--feld`, `--binden`. Alle anderen
  höchstens einmal.

**Optionsprüfung.** Jeder Befehl nimmt nur seine Optionen. Bevor eine Datei entsteht oder der Server etwas hört, lehnt
`funk-admin` mit Exit 2 ab und ändert nichts (am 01.10.2026 mit den Programmen 0.4.0 gemessen; für 0.5.0 gegen den Code gelesen,
geändert haben sich nur die Beispielnamen in den Meldungen):

| Fehler | Meldung |
| --- | --- |
| unbekannte Option | `scnamen kennt --name nicht. Erlaubt: --konto, --namen, --leeren (dazu --konfig oder --socket, --json-aus). Nichts geändert.` |
| Wert ohne Option (Leerzeichen ohne Anführungszeichen) | `"Test" gehört zu keiner Option. Werte mit Leerzeichen gehören in Anführungszeichen, etwa --namen "Max Mustermann". Nichts geändert.` |
| `=` statt Leerzeichen | `--an-konto=3: zwischen Option und Wert gehört ein Leerzeichen, kein =, etwa --an-konto 3. Nichts geändert.` |
| Option doppelt | `--id darf nur einmal dastehen, hier steht es 2-mal. Nichts geändert.` |
| Wert fehlt, dahinter die nächste Option | `--name braucht einen Wert, dahinter steht aber --leeren. Nichts geändert.` |
| unbekannter Befehl | `Unbekannter Befehl: profil-erneuer. funk-admin hilfe zeigt alle.` |
| zwei Geräte | `<befehl> nimmt nur ein Gerät, "..." ist zu viel. Nichts geändert.` |

**`--leeren`.** Eine Liste ganz wegnehmen geht nur ausdrücklich: `scnamen --leeren`, `rechte --leeren`,
`discordname --leeren`. Fehlt der Wert und `--leeren` auch, lehnt es ab, etwa `--namen fehlt, etwa --namen
"Mustermann,Max". Alle SC-Namen wegnehmen geht nur mit --leeren. Nichts geändert.` Bis 01.10.2026 löschte ein vertipptes
`scnamen --name` still alle SC-Namen; deshalb die strenge Prüfung.

**`--ersetzen`.** Die Erzeugen-Befehle schreiben nie über eine vorhandene Datei (Exit 3, B9.2). `--ersetzen` erzwingt
es; die Meldung nennt vorher, was das kostet.

| Exitcode | Bedeutung |
| --- | --- |
| 0 | gut |
| 1 | Fehler (der Server lehnt ab und schreibt `Fehler: ...`, eine Datei ist unlesbar, der Socket antwortet nicht) |
| 2 | Aufruf falsch; nichts geschehen |
| 3 | Datei gibt es schon; nichts geschehen |
| 4 | `einrichten` wartet auf das Profil vom Eigentümer |

### B9.2 Offline: Schlüssel, Geheimnisse, Profil

Diese Befehle arbeiten nur mit Dateien und brauchen keinen laufenden Server.

| Befehl | Optionen | Wirkung |
| --- | --- | --- |
| `schluessel-erzeugen` | `--zertifikat <crt> --schluessel <key> [--name Funk-Server] [--ersetzen]` | ECDSA-P-256-Schlüssel und selbstsigniertes Zertifikat; gibt Pin (Base64) und Anzeigeform aus. Für den Server und für die Reserve |
| `geheimnis-erzeugen` | `--datei <pfad> [--ersetzen]` | Betriebsgeheimnis, 32 Zufallsbytes |
| `totp-schluessel-erzeugen` | `--datei <pfad> [--ersetzen]` | `funk-totp-schluessel`, 32 Zufallsbytes |
| `profilschluessel-erzeugen` | `--schluessel <key> [--ersetzen]` | Profilschlüssel, nur am Offline-Rechner; gibt die SPKI-Zeile (`MFkw...`) und `Fingerabdruck:` aus |
| `profil-schreiben` | `--name <n> --version <zahl> --host <name[/v4\|/v6\|/v4v6]>... --tcp-port <p> --pin <b64> --pin <b64> [--nameserver <n>]... [--gueltig-tage 365] --aus <daten.json>` | Profildaten, ohne Signatur |
| `profil-signieren` | `--daten <daten.json> --schluessel <profil.key> --aus <profil.json>` | signiert die Daten |
| `profil-pruefen` | `--profil <profil.json> --spki <b64> [--spki <b64>]` | prüft Signatur und Inhalt, `Urteil: Uebernehmen` mit Hosts und Pins; Exit 0 nur dann |

```
funk-admin schluessel-erzeugen --zertifikat /etc/funk/credentials/server.crt --schluessel /etc/funk/credentials/server.key
funk-admin geheimnis-erzeugen --datei /etc/funk/credentials/funk-geheimnis
funk-admin totp-schluessel-erzeugen --datei /etc/funk/credentials/funk-totp-schluessel
dotnet funk-admin.dll profilschluessel-erzeugen --schluessel profil.key
dotnet funk-admin.dll profil-schreiben --name "Meine Staffel" --version 1 --host funk.example.org/v4 --tcp-port 8443 --pin <pin des servers> --pin <pin der reserve> --aus daten.json
dotnet funk-admin.dll profil-signieren --daten daten.json --schluessel profil.key --aus profil.json
dotnet funk-admin.dll profil-pruefen --profil profil.json --spki <spki des profilschluessels>
```

Ausgaben (gemessen):

```
Serverschlüssel erzeugt. Der Schlüssel gehört nach $CREDENTIALS_DIRECTORY, nie ins Repository.
Pin (für pins im Serverprofil): <pin, 44 Zeichen Base64>
Anzeigeform: XXXX-XXXX-XXXX-XXXX
```

```
Betriebsgeheimnis (32 Zufallsbytes) geschrieben; nur für den Dienstbenutzer lesbar halten.
funk-totp-schluessel (32 Zufallsbytes) geschrieben. Per LoadCredential einbinden, nie in eine Sicherung.
```

Ein zweiter Aufruf auf dieselbe Datei endet mit Exit 3 und nennt die Folge eines Ersetzens (am 01.10.2026 gemessen; bei
`schluessel-erzeugen` steht seit 0.5.0 "an den Eigentümer", aus dem Code gelesen):

| Befehl | Meldung hinter `<pfad> gibt es schon. Nichts geändert.` |
| --- | --- |
| `schluessel-erzeugen` | `Mit --ersetzen entsteht ein neuer Schlüssel mit neuem Pin; die Clients nehmen den Server dann erst an, wenn der neue Pin im Profil steht (profil-anfrage an den Eigentümer).` |
| `geheimnis-erzeugen` | `Mit --ersetzen entsteht ein neues Betriebsgeheimnis; die Vorrangspur fängt dann leer an, und die Schwärzung im Logbuch passt nicht mehr zu älteren Zeilen.` |
| `totp-schluessel-erzeugen` | `Mit --ersetzen entsteht ein neuer Schlüssel; jeder eingerichtete Zweitfaktor ist dann unlesbar, und jeder Admin richtet ihn an der Konsole neu ein (admin-einrichtung-neu).` |
| `profilschluessel-erzeugen` | `Mit --ersetzen entsteht ein neuer Profilschlüssel; kein Client nimmt damit signierte Profile an, bis jeder ein Setup mit dem neuen Schlüssel hat.` |

### B9.3 Einrichten, Prüfen, Stick, Umzug

Diese Befehle laufen im Werkzeug selbst. `--konfig` (oder `FUNK_KONFIG`) nennt die `server.json`, `--credentials` den
Schlüsselordner (sonst `/etc/funk/credentials`). Über den Verwaltungssocket redet nur `erster-admin`.

| Befehl | Optionen | Wirkung |
| --- | --- | --- |
| `konfig-schreiben` | `--aus <server.json> --binden <ip> [--binden <ip>] [--tcp-port 8443] [--udp-port 47443] [--oeffentlicher-tcp-port <p>] [--oeffentlicher-udp-port <p>] [--profil <pfad>] [--zustand <ordner>]` | schreibt eine gültige `server.json` (B6.4), nie über eine vorhandene (Exit 3); nicht `0.0.0.0` (Exit 2) |
| `pruefen` | `[--vollstaendig] [--credentials <ordner>] [--spki <b64>]... [--host <name>]` | prüft, ändert nichts (unten) |
| `profil-anfrage` | `--host <name[/v4\|/v6\|/v4v6]> [--tcp-port <p>] [--zertifikat <crt>] [--credentials <ordner>] [--aus <datei>]` | Anfragezeile und Kennung (B8.3) |
| `erster-admin` | keine (dazu `--konfig` oder `--socket`) | erster Admin auf einem Server ohne Eigentümer (B10.2) |
| `einrichten` | `[--profil <profil.json>] [--umzug <paket>] [--mit-serverschluessel]` | nur aus `funk-einrichten.sh`, als root (B5); beim ersten Lauf sechs Fragen, mit Router acht |
| `profil-erneuern` | `--bisher <profil.json> --schluessel <profil.key> [--anfrage <zeile\|datei>] [--reserve <crt\|key\|pin>] [--gueltig-tage 365]` | nur auf dem Stick des Eigentümers (B8.4) |
| `umzug-packen` | `--aus <paket> [--ohne-totp] [--mit-serverschluessel] [--credentials <ordner>]` | Umzugspaket bei angehaltenem Dienst (B20.3) |
| `umzug-einspielen` | `--paket <paket> [--mit-serverschluessel] [--besitzer funk] [--credentials <ordner>]` | Umzugspaket einspielen, Dienst angehalten (B20.3) |

```
funk-admin konfig-schreiben --aus /etc/funk/server.json --binden 203.0.113.10 --tcp-port 443
funk-admin pruefen
funk-admin pruefen --vollstaendig
funk-admin pruefen --vollstaendig --host funk.example.org
funk-admin profil-anfrage --host funk.example.org --aus /root/profilanfrage.txt
funk-admin erster-admin
dotnet funk-admin.dll profil-erneuern --bisher profil.json --schluessel profil.key
funk-admin umzug-packen --aus /root/umzug.tru
funk-admin umzug-einspielen --paket /root/umzug.tru
```

**`pruefen`.** Ohne Schalter prüft es dasselbe wie `funk-server pruefen` (Regeln der `server.json`, Profil,
Portabgleich), nur ohne die Zeile zur Uhr, und schreibt je Befund eine Zeile mit `OK`, `HINWEIS`, `WARNUNG` oder `FEHLER`.
Mit `--vollstaendig` dazu:

| Prüfung | Zeilen |
| --- | --- |
| Schlüsselordner da und nicht für andere offen | `FEHLER Schlüsselordner ... gibt es nicht`, `WARNUNG ... ist für andere offen (Rechte ...): chmod 700 ...` |
| `server.crt` und `server.key` da, lesbar, passen zusammen, P-256 | `OK Serverschlüssel ... und Zertifikat passen zusammen (P-256), Pin XXXX-XXXX-XXXX-XXXX.` |
| Betriebsgeheimnis 32 bis 4096 Byte, TOTP-Schlüssel genau 32 Byte | `OK Betriebsgeheimnis ...: 32 Byte.`, `OK TOTP-Schlüssel ...: 32 Byte.` |
| Rechte der Schlüsseldateien (keine für Gruppe und andere) | `WARNUNG ... ist für andere lesbar (Rechte ...): chmod 600 ...` |
| Pin des Servers im Profil | `OK Pin des Serverschlüssels XXXX-XXXX-XXXX-XXXX steht im Profil.` oder `FEHLER ... steht nicht im Profil: Die Clients lehnen diesen Server ab. ...` |
| Name im Profil (mit `--host`) | `OK Der Name ... steht im Profil.` |
| Signatur, mit `--spki` oder `profilschluessel.txt` neben `server.json` | `OK Signatur des Profils stimmt (Profilschlüssel XXXX-XXXX-XXXX-XXXX).`; ohne Schlüssel `HINWEIS Signatur des Profils nicht geprüft ...` |
| Ablauf | `WARNUNG Das Profil ist am ... abgelaufen.` oder ab 30 Tagen vorher `HINWEIS Das Profil läuft am ... ab` |
| DNS je Host | `HINWEIS <host> zeigt im DNS auf ...` oder `WARNUNG ... hat im DNS keine passende Adresse` |
| Uhr (`timedatectl`) | `OK Uhr synchronisiert (NTP).`, `WARNUNG Uhr nicht synchronisiert: ...` oder `HINWEIS Uhrzeit hier nicht prüfbar ...` |
| Ports frei, nur bei angehaltenem Dienst | `OK TCP 8443 auf ... frei.`, `FEHLER ... ist belegt`, `FEHLER ... keine Berechtigung. Ports unter 1024 braucht der Dienst mit CAP_NET_BIND_SERVICE, oder eine Weiterleitung auf 8443.`; läuft der Dienst: `HINWEIS Der Dienst läuft ...` |

Am Ende steht `Ergebnis: n Fehler, m Warnungen.`; Exit 1 bei mindestens einem Fehler. Die Ausgabe enthält keine
Geheimnisse. Auf Linux ist die Ausgabe von `pruefen --vollstaendig` in diesem Handbuch nicht belegt (am Heimserver nicht nachgesehen); gemessen
ist es unter Windows.

### B9.4 Über den Server: Betrieb

| Befehl | Optionen | Wirkung |
| --- | --- | --- |
| `status` | | `Funk-Server <fassung>: n Verbindungen, n Sitzungen, n Konten, n Geräte, n Netze, zustandsVersion n, unfreigegeben n, Vorrangspur n` |
| `messwerte` | | Relais insgesamt und je Sitzung (Protokoll 7.8): verarbeitet, verworfen, Ausgang in Mbit/s, Verarbeitungszeit p50 und p99, Laststufe; mit `--json-aus` je Sitzung Verlust, Umordnung, Jitter, RTT, Tunnelanteil, Pfadwechsel |
| `netzliste` | | Netze, Rollen, Schiffe, Version, Selbstwahl, Durchsage nur Taste |
| `vorrangspur` | | Anzahl der Präfixe in der Vorrangspur (nur HMAC-Werte) |
| `vorrangspur-leeren` | | leert die Vorrangspur. Danach sind die Adressen der Mitglieder nicht mehr vor der Sperrliste geschützt, bis sie wieder eine Sitzung hatten |
| `sicherung` | `[--ziel <verzeichnis>]` | Kopie der Zustandsdateien (B18) |
| `profil-neu-laden` | | liest `profil` neu; `Profil Version N geladen.` und gegebenenfalls `WARNUNG` (B8.3) |
| `sperrliste` | | laufende Sperren (B16) |
| `entsperren` | `--eintrag <kennung>` oder `--ip <adresse>` oder `--alle` | hebt Sperren auf, genau eins der drei (B16) |

```
funk-admin status
funk-admin messwerte
funk-admin netzliste
funk-admin vorrangspur
funk-admin vorrangspur-leeren
funk-admin sicherung
funk-admin profil-neu-laden
funk-admin sperrliste
funk-admin entsperren --ip 198.51.100.23
funk-admin entsperren --eintrag p4#0a1b2c3d4e
funk-admin entsperren --alle
```

### B9.5 Über den Server: Konten, Geräte, Mitglieder

| Befehl | Optionen | Wirkung |
| --- | --- | --- |
| `konten` | | je Konto: ID, Stufe, Status (ANTRAG, AKTIV, GESPERRT), `EIGENTÜMER`, SC-Namen (ohne SC-Namen der Anzeigename), `(Discord: <Discord-Name oder ->, <Benutzername>)` |
| `antraege` | | offene Anträge mit Gerätekennung, SC-Name, Discord-Name, Zeit und Hinweisen (B11.2) |
| `geraete` | `[--konto <id>] [--wartend] [--nicht-seit JJJJ-MM-TT]` | Geräte mit ID, Kennung, Status, Schlüsselart (laut Client, ungeprüft), Name, Konto; `--nicht-seit` nur Geräte ohne Sitzung seit dem Tag (UTC), mit `zuletzt <tag>` oder `nie verbunden` |
| `freigeben` | `<gerät> [--vormerkung <id>] [--an-konto <id>]` | gibt einen Antrag oder ein wartendes Gerät frei; Antrag als neues Konto FUNKER, mit Vormerkung oder als weiteres Gerät eines Kontos. Seit 0.7.0 gilt ein PC, ein Konto; `--an-konto` ist die Ausnahme, etwa für den PC eines Admins mit neuem Geräteschlüssel |
| `ablehnen` | `<gerät>` | lehnt einen Antrag oder ein wartendes Gerät ab und löscht es, einen Antrag samt Antragskonto. Seit 0.7.0 sperrt das nichts: Der PC darf neu beantragen und braucht wieder eine Freigabe |
| `entfernen` | `<gerät> [--grund <text>]` | Gerät ENTFERNT; eine laufende Sitzung endet sofort; es kann sich neu anmelden |
| `sperren` | `<gerät> [--grund <text>]` | Gerät ENTZOGEN, der Fingerabdruck ist dauerhaft gesperrt, auch nach dem Löschen des Kontos; eine laufende Sitzung endet sofort. Seit 0.7.0 die einzige Gerätesperre und immer bewusst: Ablehnen und Löschen sperren nichts |
| `sperre-aufheben` | `<gerät>` | seit 0.8.0: hebt die Sperre auf, auch wenn es kein Gerät mehr dazu gibt (Konto gelöscht). Das Gerät gilt danach als ENTFERNT; der PC darf neu beantragen und braucht wieder eine Freigabe. Im Verwaltungsteil: Mitglieder, Karte des Kontos, Geräte |
| `geraet-umhaengen` | `<gerät> --an-konto <id>` | ein freigegebenes Gerät an ein anderes aktives Konto (höchstens drei aktive Geräte je Konto); die Sitzung endet, der Client verbindet gleich neu |
| `konto-anlegen` | `--discord <id> --benutzername <n> [--name <n>] [--stufe ADMIN\|RAIDLEITER\|FUNKER]` | Konto ohne Gerät (Bootstrap, Protokoll 3.8) |
| `verknuepfen` | `--code XXXX-XXXX-XXXX --discord <id> --benutzername <n> [--name <n>] [--stufe ...]` | löst einen Registrierungscode ein (Programme ohne Namen: `funk-echo`, `echo-probe`, Clients 0.1.0); der Client fragt nach (120 s), bei ja wartet das Gerät auf `freigeben` |
| `konto-sperren` | `--konto <id>` | sperrt das Konto, trennt sofort |
| `konto-entsperren` | `--konto <id>` | hebt die Sperre auf |
| `konto-loeschen` | `--konto <id>` | löscht das Konto samt Geräten; den Eigentümer nie |
| `stufe` | `--konto <id> --stufe ADMIN\|RAIDLEITER\|FUNKER` | setzt die Stufe; wer nicht mehr ADMIN ist, verliert den Admin-Nachweis; den Eigentümer nicht unter ADMIN |
| `rechte` | `--konto <id> --rechte A,B,C` oder `--leeren` | setzt die ganze Rechteliste (B11.4) |
| `name` | `--konto <id> --name <n>` | Anzeigename |
| `scnamen` | `--konto <id> --namen A,B,C` oder `--leeren` | bis zu drei SC-Namen, der erste ist der Hauptname; eindeutig über Konten und Vormerkungen |
| `discordname` | `--konto <id> --name "<Discord-Name>"` oder `--leeren` | Discord-Name, eindeutig (Anf. A53) |
| `mitgliedschaft` | `--discord <id> --status OK\|NICHT_MITGLIED\|ROLLE_FEHLT [--benutzername <n>] [--name <n>]` | Server-Seite des Discord-Abgleichs (Protokoll 3.7), höchstens 3 Sperren je Stunde, nie ADMIN; bis es den Bot gibt, von Hand |
| `mitglieder` | | Konten und Vormerkungen mit Zustand, SC-Namen und Discord-Namen |
| `journal` | `[--konto <id>] [--anzahl 50]` | Verwaltungsjournal mit Klarnamen, neueste zuerst, 1 bis 1000 Einträge |

`<gerät>` ist die Anzeigeform (`XXXX-XXXX-XXXX-XXXX`) oder der volle Fingerabdruck; ist die Anzeigeform mehrdeutig,
antwortet der Server `Anzeigeform mehrdeutig, vollen Fingerabdruck nehmen`. Wahlweise steht das Gerät als `--geraet`.

```
funk-admin konten
funk-admin antraege
funk-admin geraete
funk-admin geraete --konto 7
funk-admin geraete --wartend
funk-admin geraete --nicht-seit 2026-10-06
funk-admin freigeben ABCD-EFGH-IJKL-MNOP
funk-admin freigeben ABCD-EFGH-IJKL-MNOP --vormerkung 3
funk-admin freigeben ABCD-EFGH-IJKL-MNOP --an-konto 7
funk-admin ablehnen ABCD-EFGH-IJKL-MNOP
funk-admin entfernen ABCD-EFGH-IJKL-MNOP --grund 'PC verkauft'
funk-admin sperren ABCD-EFGH-IJKL-MNOP --grund 'gestohlen'
funk-admin geraet-umhaengen ABCD-EFGH-IJKL-MNOP --an-konto 1
funk-admin konto-anlegen --discord 100000000000000003 --benutzername max --name Max
funk-admin verknuepfen --code ABCD-EFGH-JKMN --discord 100000000000000001 --benutzername echo-funker --name Echo
funk-admin konto-sperren --konto 7
funk-admin konto-entsperren --konto 7
funk-admin konto-loeschen --konto 7
funk-admin stufe --konto 7 --stufe RAIDLEITER
funk-admin rechte --konto 7 --rechte GLOBAL_SENDEN,DURCHSAGE,DURCHSAGE_ALLE,MEHRFACH_SENDEN,SPRECHANZEIGE_ALLE,ZUWEISEN
funk-admin rechte --konto 7 --leeren
funk-admin name --konto 7 --name Max
funk-admin scnamen --konto 7 --namen 'Mustermann,Zweitname'
funk-admin scnamen --konto 7 --leeren
funk-admin discordname --konto 7 --name 'Mustermann/Max'
funk-admin discordname --konto 7 --leeren
funk-admin mitgliedschaft --discord 100000000000000003 --status NICHT_MITGLIED
funk-admin mitglieder
funk-admin journal --anzahl 100
funk-admin journal --konto 7
```

### B9.6 Über den Server: Eigentümer und Admins (nur hier, nie im Client)

| Befehl | Optionen | Wirkung |
| --- | --- | --- |
| `eigentuemer` | `--konto <id>` | setzt den Eigentümer; nur ein ADMIN-Konto |
| `admins` | | Admin-Nachweise: Zustand, Schlüsselkennungen, Antrag offen oder erlaubt, Notfallcodes übrig, Sperrgrund, `Fehlversuche <n> (24 h)`, `Pause bis <HH:mm> UTC`; nie Geheimnis oder Prüfwerte |
| `admin-vorsehen` | `--konto <id>` | erlaubt dem Konto einen Antrag auf einen Admin-Schlüssel (auch für einen neuen PC eines Admins) |
| `admin-ernennen` | `--konto <id> --kennung XXXX-XXXX-XXXX-XXXX` | trägt den beantragten Schlüssel ein, wenn die vorgelesene Kennung passt; Stufe wird ADMIN; beim ersten Schlüssel beginnt die Einrichtung des Zweitfaktors |
| `admin-einrichtung-neu` | `--konto <id> [--anzeigen]` | neues TOTP-Geheimnis; altes Geheimnis und alte Notfallcodes verfallen, offene Admin-Sitzungen enden. Mit `--anzeigen` stehen Schlüssel (Vierergruppen) und `otpauth`-Link an der Konsole |
| `zweitfaktor-bestaetigen` | `--konto <id> --code <6 Ziffern>` | erster Code nach `--anzeigen`; gibt die 10 Notfallcodes aus |
| `notfallcodes` | `--konto <id>` | 10 neue Notfallcodes, die alten gelten nicht mehr |
| `admin-sperren` | `--konto <id>` | sperrt den Admin-Nachweis; offene Admin-Sitzungen enden |
| `admin-entsperren` | `--konto <id>` | entsperrt, Fehlversuche gelöscht |
| `admin-zuruecksetzen` | `--konto <id>` | Schlüssel, Geheimnis und Notfallcodes fort, wieder vorgesehen |
| `admin-entziehen` | `--konto <id> [--stufe RAIDLEITER\|FUNKER]` | kein Admin mehr: Nachweis fort, Stufe herab (Voreinstellung RAIDLEITER); nicht beim Eigentümer |
| `admin-schluessel-entfernen` | `--konto <id> --kennung XXXX-XXXX-XXXX-XXXX` | einen Admin-Schlüssel entfernen, etwa den eines verlorenen PCs |

```
funk-admin eigentuemer --konto 1
funk-admin admins
funk-admin admin-vorsehen --konto 7
funk-admin admin-ernennen --konto 7 --kennung ABCD-EFGH-IJKL-MNOP
funk-admin admin-einrichtung-neu --konto 7
funk-admin admin-einrichtung-neu --konto 7 --anzeigen
funk-admin zweitfaktor-bestaetigen --konto 7 --code 123456
funk-admin notfallcodes --konto 7
funk-admin admin-sperren --konto 7
funk-admin admin-entsperren --konto 7
funk-admin admin-zuruecksetzen --konto 7
funk-admin admin-entziehen --konto 7 --stufe FUNKER
funk-admin admin-schluessel-entfernen --konto 7 --kennung ABCD-EFGH-IJKL-MNOP
```

### B9.7 Über den Server: Netze, Rollen, Flotte, Einstellungen

| Befehl | Optionen | Wirkung |
| --- | --- | --- |
| `netz-setzen` | `--id <n> --name <n> [--offen NEIN\|HOEREN\|HOEREN_SENDEN] [--standard] [--taste] [--nur-taste] [--va] [--reihenfolge n]` oder `--json '<objekt>'` | Netz anlegen oder ändern (B12). Ab 0.5.0 ohne Schalter mit Sprachaktivierung (`--va` wirkt genauso), mit `--taste` ab Werk auf Taste, mit `--nur-taste` nur Taste; Global (Netz 1) immer Taste |
| `netz-loeschen` | `--id <n>` | Netz löschen; Mitgliedschaften darauf fallen weg |
| `rolle-setzen` | `--id <n> --name <n> [--selbst] [--braucht-schiff] [--kapazitaet n] [--reihenfolge n] --feld feld:netz[:h][:s][:std][:fest]...` oder `--json '<objekt>'` | Rolle anlegen oder ändern (B13) |
| `rolle-loeschen` | `--id <n>` | Rolle löschen |
| `schiff-setzen` | `--json '<objekt>'` | Einheit (Schiff, Trupp, Orga) mit Netzkennungen und Plätzen (B14) |
| `schiff-loeschen` | `--id <n>` | Einheit löschen |
| `einstellung` | `[--selbstwahl an\|aus] [--nachzuegler an\|aus] [--durchsage-nur-taste an\|aus]` | Einstellungen der Netzliste (B12.3) |
| `verdraengung-freigeben` | `--konto <id>` | hebt `VERDRAENGEN_GESPERRT` auf (Protokoll 4.6) |

```
funk-admin netz-setzen --id 2 --name Kommando --offen HOEREN_SENDEN --standard --taste
funk-admin netz-setzen --id 90 --name Echo --offen HOEREN_SENDEN --va
funk-admin netz-loeschen --id 90
funk-admin rolle-setzen --id 4 --name Flottenführung --selbst --feld 0:3:h:s:std --feld 1:4:h:s --feld 2:2:h:s
funk-admin rolle-loeschen --id 4
funk-admin schiff-setzen --json '{"id":1,"name":"Schiff 1","reihenfolge":1,"netze":{"CREW":11,"BRUECKE":12},"plaetze":[{"rolleId":1,"kapazitaet":1},{"rolleId":2,"kapazitaet":6}]}'
funk-admin schiff-loeschen --id 1
funk-admin einstellung --selbstwahl aus --nachzuegler an
funk-admin einstellung --durchsage-nur-taste an
funk-admin verdraengung-freigeben --konto 7
```

### B9.8 Über den Server: Einsatzleitung

| Befehl | Optionen | Wirkung |
| --- | --- | --- |
| `auftrag` | `--konto <id> [--von HH:MM] [--bis HH:MM] [--rechte A,B] [--grund <text>]` | Einsatzleitung für einen Abend (B15) |
| `auftrag-entziehen` | `--konto <id>` | beendet den Auftrag sofort |
| `auftraege` | | laufende und geplante Aufträge mit Zeiten, Urheber, Grund und Rechten |
| `flotte-vorlage` | `[--name starcitizen]` | legt die Vorlage Star Citizen neben die vorhandene Flotte, nur auf eine Flotte ohne Kanalnetze (B14.2) |

```
funk-admin auftrag --konto 2 --grund 'Op Nachtfalke'
funk-admin auftrag --konto 2 --von 20:00 --bis 23:30
funk-admin auftrag --konto 2 --von '2026-10-09 20:00' --rechte ZUWEISEN,SPRECHANZEIGE_ALLE,GLOBAL_SENDEN
funk-admin auftraege
funk-admin auftrag-entziehen --konto 2
funk-admin flotte-vorlage
```

**Keine Befehle für Phasen und Funker-Modus.** `funk-admin` 0.5.0 hat weder einen Befehl, der eine Phase anlegt oder schaltet, noch
einen, der "Heute als Funker" ein- oder ausschaltet (am Code geprüft: `Befehlsoptionen.cs` und `Hilfe.cs` kennen keinen; der
Abschnitt "Nächste Schritte" in `PROJEKT.md`, Stand 02.10.2026 01:00, nennt "keine Konsole für Phasen" als offen). Phasen legt ein Admin
im Verwaltungsteil an (B14.3), schalten tut die Einsatzleitung im Client, und den Funker-Modus schaltet ein ADMIN-Konto nur für sich
selbst im Client (B10.7). An der Konsole siehst du die Folgen: `funk-admin journal` (B10.7), das Logbuch (B1.7) und
`funk-admin netzliste --json-aus` (dort steht `phasen`, sobald es welche gibt).

### B9.9 Was sich in 0.5.0 an `funk-admin` ändert

Verglichen mit 0.4.1 (am Code geprüft: `Befehlsoptionen.cs`, `Uebersetzung.cs`, `Hilfe.cs`, `Einrichtung.cs`, `Program.cs`,
`Serverpruefung.cs`; die Tests `AdminWerkzeugTabelleTests` und `BeispielnamenTests` halten es fest):

| Was | Vorher (bis 0.4.1) | Ab 0.5.0 |
| --- | --- | --- |
| Neue Befehle | | **keine**; auch keine für Phasen oder Funker-Modus (B9.8) |
| Neue Option | | `netz-setzen --taste` (B12.1); `--va` bleibt und wirkt wie kein Schalter |
| `netz-setzen` ohne Schalter | Netz auf Taste | Netz auf Sprachaktivierung (Global immer Taste); drei Kombinationen lehnt es mit Exit 2 ab (B12.1) |
| `einrichten` in der Hilfe | "fünf Fragen" | `beim ersten Lauf sechs Fragen (mit Router acht)`; die Fragen selbst haben sich nicht geändert (B5.3) |
| Wer ist gemeint | Meldungen nannten einen Rufnamen | Die Ausgaben sagen "der Eigentümer": die Anfragezeile (`profil-anfrage`, `einrichten`), `NEU Profilschlüssel des Eigentümers (öffentlich)`, die Profilprüfung von `einrichten` und `pruefen` (`unterschrieben vom Eigentümer`, `Das Profil ist nicht mit dem Profilschlüssel des Eigentümers unterschrieben (...)`, `... verlängern: der Eigentümer mit profil-erneuern`), der Hinweis bei `schluessel-erzeugen`, die Hilfe ("Auf dem Stick des Eigentümers"), `funk-einrichten.sh` (Kopfkommentar) und im Verwaltungsteil die Texte zu Ernennung und Zweitfaktor |
| Beispiele in Meldungen | Namen echter Mitglieder | `Max Mustermann`, `Mustermann,Max` (B9.1) |
| Kopfzeile | | Jede eigene Quelldatei beginnt mit `JG TacRelay - Maintainer: Sarevoc` (561 Dateien, Anf. A58 und A59, Ausnahmen `QrCode.cs` und `WeichesClipping.cs` mit fremdem Vermerk); die beiden Units tragen sie als erste Zeile, `funk-einrichten.sh` als zweite hinter `#!`; für den Betrieb ändert sich nichts |
| Fassung in Meldungen | `Funk-Server 0.4.0 bereit` | `Funk-Server 0.5.0 bereit` (B5.3, B7.3) |

## B10 Admins und Eigentümer

Dieser Abschnitt sagt, was an der Konsole zu tun ist. Warum der Admin-Nachweis aus Schlüssel im TPM und Code vom
Telefon besteht und was er nicht abdeckt, steht in C11; den Ablauf aus Sicht des Clients beschreibt A17.

### B10.1 Begriffe

| Begriff | Bedeutung |
| --- | --- |
| Eigentümer | genau ein ADMIN-Konto (Protokoll 3.10). Im Client fasst es niemand an; ändern, sperren, herabstufen geht nur an der Konsole. `konto-loeschen` lehnt es ab, `stufe` und `admin-entziehen` lassen es ADMIN. Eigentümer ist Sarevoc (Anf. A41). |
| Admin | ein Konto mit Stufe ADMIN und einem Admin-Nachweis: Admin-Schlüssel im TPM seines PCs und Zweitfaktor (TOTP) mit einer Authenticator-App. Höchstens drei Admin-Schlüssel je Konto. |
| Zustände des Nachweises | VORGESEHEN (darf beantragen), EINRICHTUNG (Schlüssel eingetragen, Zweitfaktor noch nicht), AKTIV, GESPERRT (an der Konsole oder nach Fehlversuchen) |
| Admin-Sitzung | im Client mit Admin-Schlüssel und Code geöffnet; endet nach 12 h (seit 0.9.1 nicht mehr nach 30 min ohne Verwaltungshandlung) und mit jedem Neustart des Servers |
| frische Bestätigung | heikle Handlungen im Client (etwa Geräte freigeben, Einsatzleitung vergeben, Flotte übernehmen) brauchen einen Code, der höchstens 5 min alt ist; sonst fragt der Client nach einem neuen |
| Konsole | `funk-admin` am Server; hat alle Rechte des Eigentümers (B1.5) |

Die drei Verwaltungsrechte (`GERAETE_FREIGEBEN`, `KONTEN_VERWALTEN`, `NETZE_VERWALTEN`) wirken nur in einer
Admin-Sitzung. In einer gewöhnlichen Sitzung hat ein ADMIN-Konto die Rechte der Führung und `ZUWEISEN`, solange es nicht "Heute
als Funker" spielt (B10.7): Dann ruhen beide, die Verwaltungsrechte bleiben. Warnungen
(Admin-Sitzung von einem neuen Gerät, Zweitfaktor gesperrt, Notfallcode benutzt, Admin geändert) gehen an jede Sitzung
eines ADMIN-Kontos, auch ohne Admin-Sitzung.

Nur an der Konsole: Eigentümer setzen, Admins vorsehen, ernennen, sperren, entsperren, zurücksetzen, entziehen,
Zweitfaktor neu einrichten, Notfallcodes neu, Admin-Schlüssel entfernen, die Stufe ADMIN setzen oder nehmen, Konten
anderer Admins und des Eigentümers ändern.

### B10.2 Erster Admin auf einem frischen Server

Den ersten Admin braucht nur ein frischer Server; bei einem Umzug kommen Konten, Admins und Eigentümer mit.

1. JG TacRelay mit dem **Admin-Setup** installieren (es kennt das Profil des Servers), auf der Seite **Deine Namen**
   SC-Namen und Discord-Namen eintragen, verbinden. Der Client stellt einen Antrag, wartet auf die Freigabe und zeigt die
   Gerätekennung groß.
2. Auf dem Server:

```
sudo funk-admin erster-admin
```

3. Es zeigt die offenen Anträge und fragt `Kennung von der Startseite deines Clients (XXXX-XXXX-XXXX-XXXX):` (seit
   0.10.0; bis 0.9.3 nannte es den Bereich Verbindung, den es seit 0.6.0 nicht mehr gibt; drei Versuche; Groß und Klein,
   Bindestriche und Leerzeichen sind egal). Passt sie zu einem Antrag, gibt es frei, setzt die
   Stufe ADMIN, macht das Konto zum Eigentümer und sieht es als Admin vor:

```
OK: Konto <id> freigegeben.
OK: Konto <id> Stufe ADMIN.
OK: Konto <id> Eigentümer.
OK: Konto <id> als Admin vorgesehen.
```

4. `Jetzt im Client: Verwaltung, "Verwaltung einrichten".` Der Client legt den Admin-Schlüssel im TPM an und zeigt seine
   Kennung. Auf `Kennung des Admin-Schlüssels (leer: später mit admin-ernennen):` diese Kennung eingeben; dann ernennt
   es. Leer heißt später: `funk-admin admin-ernennen --konto <id> --kennung XXXX-XXXX-XXXX-XXXX`.
5. Im Client weiter wie in B10.3 ab Punkt 4 (Geheimnis abholen, QR-Code, erster Code, Notfallcodes).

`erster-admin` rührt nichts an, wenn es schon einen Eigentümer gibt (`Es gibt schon einen Eigentümer: ...`), und bricht
ohne offene Anträge ab (`Keine offenen Anträge. Erst im Client verbinden ...`). Scheitert ein Schritt, sagt es, was bis
dahin geschehen ist, und den Rest machst du von Hand:

```
funk-admin antraege
funk-admin freigeben XXXX-XXXX-XXXX-XXXX
funk-admin konten
funk-admin stufe --konto <id> --stufe ADMIN
funk-admin eigentuemer --konto <id>
funk-admin admin-vorsehen --konto <id>
funk-admin admin-ernennen --konto <id> --kennung XXXX-XXXX-XXXX-XXXX
```

**Das siehst du:** `funk-admin konten` zeigt dein Konto mit `ADMIN` und `EIGENTÜMER`, `funk-admin admins` deinen Nachweis
als `AKTIV` mit `Notfallcodes 10`. Die Warnung `Kein Eigentümer gesetzt` steht beim nächsten Start nicht mehr im Journal.

**Hat der Server schon ein Konto von dir** (etwa aus 0.1.0), nimm nicht `freigeben` allein, sonst entsteht ein zweites
Konto als Funker. `antraege` nennt dann `BESTEHENDES KONTO? <Name> (Konto <id>), ADMIN: ...` mit dem passenden Befehl
`freigeben <kennung> --an-konto <id>` (B11.2).

### B10.3 Weitere Admins ernennen

Nur der Eigentümer, nur an der Konsole, mit Telefon.

1. Konsole: `funk-admin admin-vorsehen --konto <id>`. Antwort: `<Name> (Konto <id>) ist vorgesehen. Im Client mit
   Verwaltungsteil jetzt 'Verwaltung einrichten'; danach die vorgelesene Kennung mit admin-ernennen bestätigen.`
2. Der künftige Admin installiert das Admin-Setup derselben Fassung. Im Client erscheint links **Verwaltung** (Strg+9);
   dort **Admin-Schlüssel anlegen und beantragen**. Der Client zeigt `Admin-Kennung zum Vorlesen am Telefon`.
3. Anruf, er liest die Kennung vor. Konsole:

   ```
   funk-admin admin-ernennen --konto <id> --kennung XXXX-XXXX-XXXX-XXXX
   ```

   Antwort: `<Name> (Konto <id>) ist Admin (Schlüssel XXXX-XXXX-XXXX-XXXX). Der Client holt das TOTP-Geheimnis bis <HH:mm>
   UTC ab und zeigt den QR-Code; danach den ersten Code eingeben.` Ab jetzt 10 Minuten.
4. Im Client **Geheimnis abholen (genau einmal)**: QR-Code und Schlüssel in Vierergruppen.
5. In der Authenticator-App auf dem Telefon scannen; kein Bildschirmfoto (wer den QR-Code hat, hat das Geheimnis).
6. Den Code aus der App unter **Erster Code aus der App**, dann **Einrichtung abschließen**.
7. **Deine zehn Notfallcodes**, nur dieses eine Mal: getrennt vom Telefon aufbewahren, als sichere Notiz in einem Passwortmanager
   oder ausgedruckt an einem sicheren Ort, nie in einen Chat. Jeder gilt einmal. **Aufgeschrieben**.
8. Auf einen neuen Code der App warten (derselbe gilt nicht zweimal), eingeben, **Verwaltung öffnen**.

| Antwort an der Konsole | Bedeutung |
| --- | --- |
| `Kennung passt nicht zum Antrag (oder der Schlüssel ist schon eingetragen). Nichts geändert.` | vertippt oder ein fremder Antrag; noch einmal vorlesen lassen |
| `kein offener Antrag: erst admin-vorsehen, dann im Client 'Verwaltung einrichten'` | im Client fehlt der Klick aus Punkt 2 |
| `schon drei Admin-Schlüssel eingetragen; erst einen entfernen` | `admin-schluessel-entfernen` |
| `Schlüssel XXXX-XXXX-XXXX-XXXX für <Name> (Konto <id>) eingetragen.` | ein weiterer Schlüssel eines eingerichteten Admins (neuer PC); kein neuer Zweitfaktor |

**Zu spät** (Geheimnis nicht binnen 10 Minuten abgeholt oder erster Code zu spät): `funk-admin admin-einrichtung-neu
--konto <id>`, Antwort `Neues Geheimnis für ... erzeugt. Der Client holt es bis <HH:mm> UTC ab (Verwaltung einrichten).`,
dann im Client wieder **Geheimnis abholen**. Ab Client 0.3.0 ist das Geheimnis auch fort, wenn man den Bereich
**Verwaltung** vor dem Ende verlässt.

**Neuer PC eines Admins:** `admin-vorsehen`, im Client auf dem neuen PC "Verwaltung einrichten", `admin-ernennen` mit der
neuen Kennung (wird als weiterer Schlüssel eingetragen), den alten mit `admin-schluessel-entfernen` entfernen. Das neue
Gerät selbst gibt die Konsole frei, mit `--an-konto` an das Konto des Admins.

### B10.4 Zweitfaktor an der Konsole

Für den Fall, dass der Verwaltungsteil fehlt oder der QR-Code nicht geht:

```
funk-admin admin-einrichtung-neu --konto <id> --anzeigen
```

Ausgabe: `Zweitfaktor für <Name> (Konto <id>): in der Authenticator-App 'Manuell eingeben' und diesen Schlüssel abtippen
oder den Link als QR-Code scannen.`, darunter `Schlüssel:` in Vierergruppen und `Link: otpauth://totp/...`, dann
`Kein Bildschirmfoto.` Diese Ausgabe ist ein Geheimnis: nicht in eine Datei, nicht in einen Chat. Danach:

```
funk-admin zweitfaktor-bestaetigen --konto <id> --code <6 Ziffern>
```

Die Antwort sind die zehn Notfallcodes (`Notfallcodes für ..., nur jetzt sichtbar. ...`). Abgelaufen:
`Einrichtung abgelaufen: admin-einrichtung-neu`.

Parameter des Zweitfaktors: TOTP nach RFC 6238 mit HMAC-SHA-1, 6 Ziffern, 30 Sekunden, Geheimnis 160 Bit; Aussteller
`JG TacRelay`. Der Server verträgt einen Zeitschritt Abweichung; deshalb braucht der Server (im Container der Host) eine
synchronisierte Uhr, und auf dem Telefon steht die Uhrzeit auf automatisch.

### B10.5 Sperren, Fehlversuche, Verlust

| Fall | Was geschieht, was zu tun ist |
| --- | --- |
| 5 falsche Codes in 15 min | die Anmeldung pausiert 15 min (`admins` zeigt `Pause bis <HH:mm> UTC`) |
| 10 falsche Codes in 24 h | Zweitfaktor gesperrt (`gesperrt: FEHLVERSUCHE`), bis der Eigentümer mit dem Admin gesprochen hat und `admin-entsperren --konto <id>` aufruft |
| Telefon weg oder neu | mit Sync in der Authenticator-App: auf dem neuen Telefon anmelden. Ohne Sync: mit Notfallcodes anmelden, dann an der Konsole `admin-einrichtung-neu --konto <id>` und neu einrichten. Gestohlen: zuerst den Eigentümer informieren |
| Notfallcodes verbraucht oder verloren | `notfallcodes --konto <id>`: zehn neue, die alten gelten nicht mehr. Jeder benutzte Notfallcode ist eine Warnung an alle Admins |
| Admin-PC weg, Windows neu | der Admin-Schlüssel ist fort (gewollt, er ist nicht exportierbar). Neues Gerät an der Konsole freigeben, dann wie "Neuer PC" in B10.3 |
| Admin-PC gestohlen oder Schadcode vermutet | sofort `admin-sperren --konto <id>` (beendet offene Admin-Sitzungen). Ein anderer Admin kann im Client das Konto sperren. Danach PC neu aufsetzen und mit neuem Schlüssel und neuem Geheimnis neu einrichten (`admin-zuruecksetzen`, dann wie beim ersten Mal) |
| jemand soll kein Admin mehr sein | `admin-entziehen --konto <id> [--stufe FUNKER]`; er funkt mit seinem Gerät weiter |
| der Eigentümer hat alles verloren | die Konsole braucht weder Client noch Code: `admin-zuruecksetzen --konto <id>`, dann neu einrichten |
| `funk-totp-schluessel` verloren | jeder Admin richtet den Zweitfaktor an der Konsole neu ein (`admin-einrichtung-neu`) |

### B10.6 Der Admin-PC

Die Voraussetzungen hat Sarevoc am 01.10.2026 festgelegt (Anf. A56); der Client 0.4.1 setzt sie um. Begründung und
Restrisiko stehen in C11.2.

- **TPM 2.0 ist technisch Pflicht.** Der Verwaltungsteil legt den Admin-Schlüssel (`JG-TacRelay-Admin`, ECDSA P-256)
  über den Microsoft Platform Crypto Provider an, nicht exportierbar, nur zum Signieren. Scheitert das, gibt es keine
  Verwaltung: `Auf diesem PC lässt sich kein Admin-Schlüssel im TPM anlegen. Technisch Pflicht ist TPM 2.0; einen
  Admin-Schlüssel als Datei gibt es mit Absicht nicht. BitLocker und eine Windows-PIN braucht das TPM dafür nicht. Frag
  den Eigentümer, ob die Verwaltung auf einem anderen PC eingerichtet werden kann.`, dahinter, wenn Windows einen eigenen Code
  meldete, `(Windows meldet 0x...)` (`AdminSchluessel.cs`).
- **Windows-Konto mit PIN oder Kennwort** ist Voraussetzung als Richtlinie der Gilde (Anf. A56); ein Microsoft-Konto ist
  nicht nötig, und der Client prüft es nicht. Der TPM-Schlüssel lässt sich nicht wegkopieren, aber auf demselben PC
  benutzen; die Windows-Anmeldung ist das Schloss davor.
- **BitLocker oder Geräteverschlüsselung** wird empfohlen, ist aber keine Voraussetzung.

**Die Vorprüfung** (`Voraussetzungen.cs`, `VoraussetzungenWindows.cs`, neu in 0.4.1): Bevor der Schlüssel entsteht, zeigt
die Karte **Verwaltung einrichten** unter **Vorprüfung auf diesem PC** drei Zeilen, nur lesend, ohne Adminrechte und ohne
einen Schlüssel anzulegen. Beide Prüfungen laufen auf eigenen Fäden mit je 10 Sekunden Frist; wer nicht antwortet, gilt
als nicht feststellbar.

| Zeile | Wie geprüft | Ergebnis |
| --- | --- | --- |
| **TPM 2.0 (technisch Pflicht)** | Platform Crypto Provider öffnen und dessen Eigenschaft `PCP_PLATFORM_TYPE` lesen, etwa `TPM-Version:2.0 -Level:0-Revision:1.38-...` | TPM 2.0: `TPM 2.0 gefunden.` Kein TPM, TPM 1.2 oder ein Anbieter, der sich nicht öffnen lässt: ein Satz in Rot mit dem Code von Windows, und **Admin-Schlüssel anlegen und beantragen** bleibt aus. Nicht feststellbar: Hinweis, Einrichten bleibt möglich. |
| **Windows-Konto mit PIN oder Kennwort (Richtlinie)** | nicht geprüft | fester Satz: `Ein lokales Konto genügt, ein Microsoft-Konto ist nicht nötig. Das verlangt die Gilde, nicht das TPM; der Client prüft es nicht.` |
| **BitLocker oder Geräteverschlüsselung (empfohlen)** | Eigenschaft `System.Volume.BitLockerProtection` des Systemlaufwerks über die Windows-Shell; Microsoft nennt die Werte nicht öffentlich, gedeutet wird vorsichtig (1, 3, 6 an; 2, 4, 5, 8 aus) | an: `BitLocker/Geräteverschlüsselung ist für das Systemlaufwerk C: eingeschaltet.` Aus: `BitLocker/Geräteverschlüsselung wird empfohlen, um Daten bei Offline-Zugriff auf die Festplatte zu schützen.` und `Keine Voraussetzung: Das TPM braucht für den Admin-Schlüssel weder BitLocker noch eine Windows-PIN.` Sonst: nicht feststellbar, mit dem Satz, dass sie empfohlen, aber keine Voraussetzung ist. |

**Erneut prüfen** wiederholt beides. Gemessen am Admin-PC von Sarevoc am 01.10.2026 (nur lesend): TPM 2.0 gefunden,
BitLocker-Wert 2, also aus (`PROJEKT.md`, Standzeile 01.10.2026 18:30). Bis 0.4.0 nannte der Verwaltungsteil BitLocker
noch eine Pflicht für Admin-PCs; in 0.4.1 ist dieser Satz fort.
- Die Authenticator-App auf dem Telefon, nicht auf dem Admin-PC: Lägen Admin-Schlüssel und Code auf demselben Rechner,
  fängt Schadcode dort beide.
- Das Admin-Setup immer in derselben Fassung wie der Client; sonst meldet der Client `Verwaltungsteil passt nicht zu
  dieser Fassung - das Admin-Setup dieser Fassung ausführen.`
- Grenze: Ob der Schlüssel wirklich im TPM liegt, kann der Server nicht prüfen (keine TPM-Attestation). Dagegen helfen das
  Admin-Setup direkt vom Eigentümer mit geprüftem SHA-256 und die Ernennung am Telefon.

### B10.7 Heute als Funker (ab 0.5.0)

Ein ADMIN, der an einem Abend nur funken will, während ein anderer leitet, schaltet in seinem Client "Heute als Funker" ein
(Einstellungen, Karte **Ich**; Protokoll 6.15, Nachricht `0x003C`). Dann kann ihn die Einsatzleitung einteilen wie jeden Funker,
und er sitzt nicht mit Rechten da, die ihm in dieser Rolle nicht gehören. Eine zweite Anmeldung gibt es dafür nicht. Was ein
Admin dazu im Client tut und sieht, steht in Teil A; hier steht, was der Server tut und was du an der Konsole siehst.

| Frage | Antwort |
| --- | --- |
| Wer? | nur ein aktives Konto der Stufe ADMIN, nur für sich selbst, ohne Admin-Sitzung und ohne Code (der Modus senkt eigene Rechte). Niemand setzt einen anderen hinein, verlängert oder beendet ihn von außen; `funk-admin` hat dafür keinen Befehl (B9.8) |
| Wie lange? | bis zum nächsten 04:00 Uhr Europe/Berlin, höchstens 24 Stunden |
| Was ruht? | die Rechte der Einsatzleitung (Führung und `ZUWEISEN`) an drei Stellen: am Konto, im Heißpfad für Sprachpakete und im Lagebild des Clients (Standzeile 01.10.2026 20:15). Kein Global, keine Durchsage, kein Mehrfachsenden, keine Funkfeld-Zuteilung (bis Client 0.11.1 das Brett), kein Phasenschalten; die Sendeauswahl verliert Global und Mehrfachziele |
| Was bleibt? | die Verwaltungsrechte (`GERAETE_FREIGEBEN`, `KONTEN_VERWALTEN`, `NETZE_VERWALTEN`); sie wirken wie immer nur in einer Admin-Sitzung |
| Einteilen? | ja: Die Rangregel (B11.4) lässt `ZUWEISEN` auf dieses ADMIN-Konto wirken, solange der Modus läuft. Stummschalten eines ADMIN-Kontos bleibt einem ADMIN vorbehalten, auch im Modus |
| Beim Einschalten | eine Sperre der Selbstwahl, die das Konto gesetzt hat, und das Nachziehen seiner zuletzt angewandten Aufstellung enden; beim Ausschalten kommen sie nicht zurück |
| Ende | durch den Admin selbst ("Wieder Admin", ohne Code), um 04:00 über den Fristen-Takt, bei einem Stufenwechsel und bei einer Sperre des Kontos; jedes Ende steht im Journal |
| Neu verbinden | übersteht ein Neuverbinden und einen Neustart des Servers. "Beim Verbinden als Funker" im Client schaltet erst am nächsten Abend (ab 04:00) wieder ein, auch nach "Wieder Admin" mitten im Raid (Gegenprüfung 01.10.2026 23:05) |

**An der Konsole siehst du es nur im Journal** (`funk-admin journal --konto <id>`):

- `FUNKER_MODUS_EIN` mit dem Text `<Name> (Konto <id>) spielt als Funker bis <TT.MM. HH:MM>`, Urheber das Konto selbst,
- `FUNKER_MODUS_AUS` mit `<Name> (Konto <id>) wieder als Admin`, bei Ablauf, Stufenwechsel oder Sperre mit dem Zusatz
  `(Funker-Modus abgelaufen)`, `(Stufe geändert)` oder `(Konto gesperrt)` und "Konsole" als Urheber.

`funk-admin konten` zeigt den Modus nicht (am Code geprüft: die Zeile enthält Stufe, Status, Eigentümer, Namen). Das Ende kann
die Konsole nur mittelbar herbeiführen, mit einer Sperre (`konto-sperren`) oder einem Stufenwechsel (`stufe`); der zweite nimmt
einem Admin die Admin-Stufe und damit den Nachweis (B11.4), also kein Mittel für den Alltag.

**Rechte und Sicherung:** Der Modus liegt als `alsFunkerBis` in `lage.json`. `funk-admin sicherung` lässt ihn weg, wie die
Aufträge; nach dem Zurückspielen läuft kein Funker-Modus mehr. Das Umzugspaket und eine Proxmox-Sicherung des ganzen Containers
tragen ihn mit (B18, B20.3).

**Gegen Missbrauch als Tests belegt** (Standzeile 01.10.2026 20:15): Funker-Modus von einem Funker, von einem Beauftragten und mit
einem fremden Konto im Body werden mit `KEIN_RECHT` abgelehnt. Ein ADMIN im Funker-Modus kann keine Phase schalten (`KEIN_RECHT`).
Beim Bau fiel noch auf, dass ein ADMIN im Funker-Modus mit `NETZE_VERWALTEN` die Selbstwahl hätte sperren können; eine Sperre
setzt jetzt nur, wer `ZUWEISEN` hat (die Konsole ausgenommen).

## B11 Mitglieder, Anträge und Geräte

### B11.1 Wie ein Mitglied hereinkommt

1. Das Mitglied installiert das normale Setup (darin ist kein Verwaltungscode), trägt auf der Seite **Deine Namen** seinen
   SC-Namen und seinen Discord-Namen ein (etwa `Mustermann` und `Mustermann/Max`) und verbindet. Der Client stellt einen
   Antrag und zeigt die Gerätekennung groß, solange er auf die Freigabe wartet.
2. Das Mitglied schickt dem Admin SC-Namen und Gerätekennung, etwa über Discord; die Kennung ist kein Geheimnis.
3. Der Admin vergleicht die Kennung mit `antraege` (oder im Client unter **Verwaltung**, **Anträge**) und gibt frei. Im
   Client braucht Freigeben einen Code, der höchstens 5 Minuten alt ist.
4. Ein Antrag wird immer als FUNKER freigegeben. Der Client zeigt `Verbunden`.

Die Namen eines Antrags tippt der Antragsteller selbst; prüfen lässt sich nur die Kennung. Kein Weg führt vom Antrag oder
von einem Code direkt zu FREIGEGEBEN, immer entscheidet ein Admin. Programme ohne Namen (`funk-echo`,
`funk-testclient echo-probe`, Clients 0.1.0) holen statt eines Antrags einen Registrierungscode, den die Konsole mit
`verknuepfen` einlöst; danach `freigeben` wie bei jedem Gerät.

Ein Konto hat höchstens drei aktive Geräte und eine Sitzung zur Zeit: Ein zweites Gerät desselben Kontos verdrängt das
erste. Geräte eines privilegierten Kontos (ADMIN oder ein Verwaltungsrecht) gibt nur die Konsole frei.

### B11.2 Anträge lesen und entscheiden

```
funk-admin antraege
```

Jede Zeile beginnt mit der Gerätekennung:

```
XXXX-XXXX-XXXX-XXXX  <SC-Name> (Discord: <Discord-Name>)  seit <JJJJ-MM-TT HH:MM> UTC  <Hinweise>
```

| Hinweis hinter dem Antrag | Bedeutung | Was du tust |
| --- | --- | --- |
| keiner | neues Mitglied | Kennung vergleichen, `funk-admin freigeben <kennung>` |
| `passt zu Vormerkung <id> <SC-Name>` | ein Admin hat das Mitglied mit Rollen vorgemerkt | `funk-admin freigeben <kennung> --vormerkung <id>`; die Rollen gelten sofort |
| `BESTEHENDES KONTO? <Name> (Konto <id>), <STUFE>: ...` | es gibt schon ein Konto mit diesem Namen (ganzer Name oder der Teil vor dem Schrägstrich) | Seit 0.7.0 bekommt jeder PC sein eigenes Konto (ein PC, ein Konto): Ist es ein weiterer PC des Mitglieds, einfach `funk-admin freigeben <kennung>`. Nur den PC eines Admins mit neuem Geräteschlüssel hängst du an, wenn er dieselbe Kennung zeigt: `funk-admin freigeben <kennung> --an-konto <id>`. Nur ein Hinweis, angehängt wird nie von selbst |
| `NAME VERGEBEN` | der SC-Name gehört schon jemandem | klären; freigegeben wird ohne diesen Namen, der Anzeigename kann einen Zusatz wie (2) bekommen |
| `DISCORD-NAME VERGEBEN (<SC-Name> (Konto <id>))` | der Discord-Name gehört schon einem Konto | Seit 0.7.0 gibt `freigeben` trotzdem frei, nur ohne diesen Discord-Namen; so gibt sich niemand als der andere aus. Bis 0.6.x lehnte `freigeben` hier ab. Trägt ihn eine Vormerkung, bekommt ihn das Konto mit `--vormerkung` |

Weitere Fälle:

| Fall | Befehl |
| --- | --- |
| weiterer PC eines Mitglieds (Laptop) | `funk-admin freigeben <kennung>`: eigenes Konto (seit 0.7.0, ein PC, ein Konto) |
| unbekannt, falsch, Spam | `funk-admin ablehnen <kennung>`: löscht den Antrag, sperrt nichts. Kommt derselbe PC immer wieder, hält ihn nur `funk-admin sperren <kennung>` bewusst draußen |
| Antrag ins falsche Konto freigegeben (neues Konto als Funker statt des alten) | Gerät umhängen, B11.5 |

`freigeben` antwortet `OK: <kennung>`. Ohne offene Anträge sagt `antraege` `Keine offenen Anträge`.

### B11.3 Namen

| Name | Regeln | Wer ändert |
| --- | --- | --- |
| SC-Namen | bis zu drei, der erste ist der Hauptname; je 2 bis 32 Zeichen aus Buchstaben (auch Umlaute), Ziffern, Leerzeichen, `_`, `-` und Punkt; eindeutig über Konten und Vormerkungen, auch gegen Groß und Klein und Breitschrift. Die Eingabefelder für den SC-Namen im Client und im Verwaltungsteil nehmen seit 0.11.2 höchstens 22 Zeichen (A1.3); der Server und die Konsole nehmen weiter bis 32 | Admin (Konsole `scnamen`, Client in der Admin-Sitzung) |
| Discord-Name (Anmeldename) | 2 bis 32 Zeichen, keine unsichtbaren Zeichen, nur einfache Leerzeichen; Form frei, Hauptsache erkennbar, etwa `Mustermann/Max` (Anf. A54); eindeutig wie die SC-Namen | nur ein Admin (Anf. A53): an der Konsole `discordname`, im Client unter **Verwaltung**, **Mitglieder**, auf der Karte des Kontos. Konten anderer Admins und des Eigentümers nur an der Konsole. Das Mitglied sieht ihn unter **Ich**, ändern kann es ihn nicht |
| Anzeigename | `name --konto <id> --name <n>` | Konsole |

Namen sind eindeutig; wer Namen von einem Konto auf ein anderes bringen will, nimmt sie erst dort weg (`--leeren` oder
`konto-loeschen`). Antworten: `SC-Namen: <namen>`, `Discord-Name von <Name> (Konto <id>): <name>`; vergeben:
`Den Discord-Namen <name> trägt schon <SC-Name> (Konto <id>). Nichts geändert.` bzw. `Den Namen <name> hat schon jemand`.

### B11.4 Stufen und Rechte

| Stufe | im Client | Standardrechte |
| --- | --- | --- |
| FUNKER | Funker | keine besonderen; hört und sendet in den eigenen Feldern, wählt selbst Rolle und Schiff, wenn erlaubt |
| RAIDLEITER | Führung | `GLOBAL_SENDEN`, `DURCHSAGE`, `DURCHSAGE_ALLE`, `MEHRFACH_SENDEN`, `SPRECHANZEIGE_ALLE`; seit 0.3.0 kein `ZUWEISEN` |
| ADMIN | Admin | alle neun Rechte; die drei Verwaltungsrechte nur in einer Admin-Sitzung; im Funker-Modus (B10.7) ruhen die Rechte der Führung und `ZUWEISEN` |

| Recht | Wofür |
| --- | --- |
| `GLOBAL_SENDEN` | im Netz Global senden |
| `DURCHSAGE` | Durchsage an ausgewählte Netze (bis 64) |
| `DURCHSAGE_ALLE` | Durchsage an alle |
| `MEHRFACH_SENDEN` | in mehrere Ziele zugleich senden |
| `ZUWEISEN` | andere einteilen und Phasen schalten (Einsatzleitung); auf ADMIN-Konten nur durch einen ADMIN oder solange das Konto "Heute als Funker" spielt (Rangregel, B10.7) |
| `SPRECHANZEIGE_ALLE` | Sprechanzeige aller Netze |
| `GERAETE_FREIGEBEN`, `KONTEN_VERWALTEN`, `NETZE_VERWALTEN` | Verwaltung; wirken nur in einer Admin-Sitzung und machen ein Konto privilegiert |

`rechte --konto <id> --rechte A,B,C` setzt die ganze Liste, nicht einzelne Rechte dazu. Ein Konto RAIDLEITER, das
dauerhaft einteilen soll, bekommt also alles, was es behalten soll, plus `ZUWEISEN`:

```
funk-admin rechte --konto <id> --rechte GLOBAL_SENDEN,DURCHSAGE,DURCHSAGE_ALLE,MEHRFACH_SENDEN,SPRECHANZEIGE_ALLE,ZUWEISEN
```

Für einen einzelnen Abend ist ein Auftrag besser (B15). `stufe --stufe ADMIN` macht noch keinen Admin mit Verwaltung; dazu
gehört der Admin-Nachweis (B10.3). Wer die Stufe ADMIN verliert, verliert den Nachweis.

### B11.5 Geräte und Konten pflegen

| Aufgabe | Befehl | Wirkung |
| --- | --- | --- |
| alle Geräte, ein Konto, nur wartende | `geraete`, `geraete --konto <id>`, `geraete --wartend` | Liste mit Status und Schlüsselart (`TPM` oder `SOFTWARE`, laut Client, ungeprüft) |
| Gerät weg, darf wiederkommen | `entfernen <kennung> [--grund <text>]` | ENTFERNT, Sitzung endet sofort |
| Gerät nie wieder (gestohlen) | `sperren <kennung> [--grund <text>]` | ENTZOGEN, Fingerabdruck dauerhaft gesperrt |
| Sperre war ein Irrtum | `sperre-aufheben <kennung>` | seit 0.8.0: ENTFERNT, der PC darf neu beantragen |
| Gerät ans richtige Konto | `geraet-umhaengen <kennung> --an-konto <id>` | Antwort `OK: <kennung> gehört jetzt zu <Name> (Konto <id>); der Client verbindet sich gleich neu.`; hat das alte Konto kein Gerät mehr, nennt sie `konto-loeschen --konto <alt>` |
| Konto sperren, entsperren | `konto-sperren --konto <id>`, `konto-entsperren --konto <id>` | Sperre trennt sofort |
| Konto löschen | `konto-loeschen --konto <id>` | samt Geräten und Admin-Nachweis; den Eigentümer nie (`Das Konto ist Eigentümer und lässt sich nicht löschen. ...`) |
| Verdrängen gesperrt | `verdraengung-freigeben --konto <id>` | hebt `VERDRAENGEN_GESPERRT` auf |

**Antrag ins falsche Konto** (so geschehen am 01.10.2026): Ein Gerät wurde mit `freigeben` ohne `--an-konto` freigegeben;
das Mitglied steht jetzt in einem neuen Konto als Funker statt in seinem alten. In dieser Reihenfolge, damit das alte
Konto auch bei vertauschten Zahlen stehen bleibt:

```
funk-admin konten
funk-admin geraete
funk-admin eigentuemer --konto <alt>                      # nur, wenn das alte Konto der Eigentümer ist oder sein soll
funk-admin geraet-umhaengen <kennung> --an-konto <alt>
funk-admin konten
funk-admin geraete --konto <neu>
funk-admin konto-loeschen --konto <neu>
funk-admin discordname --konto <alt> --name '<Discord-Name>'
funk-admin scnamen --konto <alt> --namen '<SC-Name>'
```

`geraete --konto <neu>` gibt vor dem Löschen nur eine leere Zeile aus. Erst das neue Konto löschen, dann die Namen dem
alten geben, denn Namen sind eindeutig.

### B11.6 Mitgliederverwaltung im Client, Journal

Mitgliederrollen (der Rollenkatalog, etwa "Medium Fighter Pilot"), Lieblingsrollen und Vormerkungen pflegt ein Admin im
Verwaltungsteil des Clients, nicht an der Konsole (Untergilden gibt es seit 0.11.8 nicht mehr, B11.8). Die Konsole liest sie nur:

```
funk-admin mitglieder
funk-admin journal --anzahl 50
funk-admin journal --konto <id>
```

Das Journal zeigt je Eintrag Zeit (UTC), Art und Text mit Klarnamen, etwa `GERAET_UMGEHAENGT`, `SPERRE_AUFGEHOBEN`,
`ADMIN_ERNANNT`. Was ein Mitglied auf der Mitgliedskarte von anderen sieht, steuert `mitgliedskarte.rollenFuerAlle` (B6.2).

### B11.7 Abzeichen (ab 0.11.7)

Abzeichen sind Ränge in drei Laufbahnen (Marine 10, Navy 10, Orga 8 Stufen) und 22 Tätigkeiten. Je Laufbahn hat ein Konto
höchstens einen Rang, dazu höchstens 6 Tätigkeiten. Vergeben nur Admins, im Client in der Admin-Sitzung (Recht
`KONTEN_VERWALTEN`) auf der Seite **Abzeichen** ganz unten im Hauptmenü (A9.2); die Einsatzleitung nie, und an der Konsole
gibt es dafür keinen Befehl. Jeder Klick schickt die neue Anfrage **Abzeichen** (0x0025), die Ränge und Tätigkeiten des
Kontos ersetzt. Der Server prüft jede Kennung gegen den Katalog (Art, Laufbahn, keine Doppelten) und schreibt ins Journal
`ABZEICHEN` mit den Titeln.

Gespeichert wird in `mitglieder.json`, je Konto `raenge`, `taetigkeiten` und die eigene Wahl des Mitglieds
(`abzeichenVorn`, `abzeichenAus`; A9.1). Eine alte `mitglieder.json` ohne diese Felder lädt unverändert; wird ein Konto
gelöscht, sind seine Abzeichen mit fort. Andere bekommen in der Präsenz nur, was der Inhaber zeigt; Admins sehen in der
Verwaltung alles.

Ein neues Abzeichen gibt es nicht im Programm, sondern nur mit einer neuen Fassung: Wer eins will, zeichnet es nach der
Vorlage `grafik\abzeichen\LIESMICH.md` (SVG, 128 x 128, selbst gezeichnet, Kontrast 3:1) und gibt es dem Maintainer. Der legt
es mit einer neuen Kennung in den Katalog (eine einmal vergebene Kennung nie wiederverwenden), rechnet die Bilder mit
`tools\abzeichen-rendern.ps1` um und baut Server und Setups. Client und Server müssen dieselbe Fassung haben: Ein älterer
Server kennt das neue Abzeichen nicht und lehnt das Vergeben ab, ein Server vor 0.11.7 kennt die Anfrage gar nicht (der
Client fragt ihn dann nicht erst).

### B11.9 Meldungen senden (ab 0.12.0)

Seit 0.12.0 schickt die Leitung Grafiken an alle, die online sind: Befehle, Zustände, Ampel, Countdown und die
Bereit-Abfrage (A12.8, beim Funker A6.9). Senden darf ein Konto nur, wenn beides zutrifft:

- ein Admin hat es ernannt: Haken **darf Meldungen senden** auf der Mitgliederkarte im Verwaltungsteil (Seite
  **Mitglieder**, Karte des Kontos; nur in der Admin-Sitzung mit `KONTEN_VERWALTEN`). Gespeichert wird mit **Speichern**,
  im Journal steht `MELDUNGEN` mit "Meldungen senden für ... erlaubt" oder "weggenommen" und wer es war. Der Haken bleibt,
  bis ein Admin ihn wegnimmt; an einem offenen Antrag oder einer Vormerkung gibt es ihn nicht, und an einem Server vor 0.12.0
  steht er nicht auf der Karte;
- das Konto ist gerade Führung (RAIDLEITER), Admin oder Einsatzleitung mit laufendem Auftrag. Bei einem Funker bleibt der
  Haken stehen, wirkt aber nicht; wer **Heute als Funker** spielt, sendet nie. Der Hilfetext am Haken sagt es:
  `Wirkt nur bei Führung, Admin oder Einsatzleitung mit Auftrag.`

Ein neues Recht gibt es dafür nicht (es bräche ältere Clients); der Haken ist ein Feld des Mitglieds wie die Abzeichen,
`meldungenSenden` in `mitglieder.json`. An der Konsole gibt es keinen Befehl dafür. Ziel ist immer: alle, die online sind;
je Sender geht höchstens eine Meldung in 2 Sekunden. Was gerade läuft (Zustand, Ampel, Countdown, offene Abfrage), hält
der Server nur im Speicher; nach einem Neustart ist es leer. Bei der Bereit-Abfrage zählt, wer sie gestartet hat, nicht
mit (weder in `m` noch unter "fehlt").

**Update.** Client und Server gemeinsam aktualisieren, wie in `BETRIEB.md` unter "Update auf 0.12.0": erst `sicherung`,
dann der Server, dann das Setup für alle und das Admin-Setup für die Admins. Erst ein Client 0.12.0 zeigt das Feld
**Meldungen**, die Anzeige über dem Spiel und den Haken; Clients bis 0.11.9 bleiben verbunden und sehen keine Meldungen.
Ein Client 0.12.0 an einem älteren Server zeigt nichts davon.

### B11.8 Untergilden entfallen (ab 0.11.8)

Sarevoc hat entschieden: "Untergilden entfallen ganz, überall". Seit 0.11.8 gibt es im Client kein Kürzel mehr in Wer ist da
und auf der Funkfeld-Zuteilung, in der Verwaltung keine Seite **Untergilden** und auf der Karte eines Mitglieds keine
Auswahl. Ein Recht hing nie an einer Untergilde. Bei **Liste einfügen** bleibt das Format `SC-Name; Discord-Name; Kürzel;
Rollen`, die dritte Spalte lässt du leer (`SC;Discord;;Rollen`); steht dort etwas, legt der Server trotzdem an und meldet
einmal je Liste "Spalte 3 (Untergilde) wird seit 0.11.8 übergangen".

Am Server bleibt die Anfrage **Untergilde** (0x001D) nur als Hülle: LISTE gibt eine leere Liste, SETZEN und LOESCHEN lehnt
er mit "Untergilden gibt es seit 0.11.8 nicht mehr" ab. So trennt ein neuer Server ältere Clients nicht, die die Liste beim
Öffnen holen; ihre Verwaltung zeigt eine leere Seite. Gespeicherte Untergilden in `mitglieder.json` (Feld `untergilden`)
löscht er nicht: Er liest sie ein und schreibt sie unverändert zurück, zeigt und ändert sie aber nie, auch nicht, wenn ein
Konto oder eine Vormerkung gelöscht wird. Damit bleibt der Rückweg auf eine ältere Serverfassung offen (B19), denn die
scheitert beim Laden, wenn das Feld fehlt. Das Feld nicht von Hand aus der Datei nehmen.

## B12 Netze

### B12.1 Netz anlegen und ändern

```
funk-admin netz-setzen --id <n> --name <n> [--offen NEIN|HOEREN|HOEREN_SENDEN] [--standard] [--taste] [--nur-taste] [--va] [--reihenfolge n]
```

| Option | Voreinstellung | Bedeutung |
| --- | --- | --- |
| `--id` | Pflicht | 1 bis 65534. Netz 1 ist Global; der Server legt es selbst an, nur per Taste |
| `--name` | Pflicht | bis 32 Zeichen, ohne Sortiernummer; die Reihenfolge steht getrennt (Anf. A16) |
| `--offen` | `NEIN` | wer sich selbst hineinsetzen darf: niemand, nur hören, hören und senden |
| `--standard` | aus | jedes neue Konto bekommt die Standardnetze ab Feld 0; gesendet wird zuerst ins erste Feld mit Senderecht |
| `--taste` | aus | ab 0.5.0: Auslöser-Voreinstellung Sprechtaste; jeder kann im Client für sich umstellen |
| `--nur-taste` | aus | in diesem Netz nur mit Sprechtaste senden, keine Sprachaktivierung (auch die Voreinstellung ist dann Taste) |
| `--va` | | Sprachaktivierung als Voreinstellung; wirkt ab 0.5.0 wie kein Schalter, damit die Befehle aus den Anleitungen für 0.4.x weiter stimmen |
| `--reihenfolge` | 0 | Sortierung in der Anzeige |

**Taste oder VA ab Werk (neu in 0.5.0, Anf. A62 und A64):** Die Auslöser-Voreinstellung (`ausloeserStandard`) gilt im Client für
jeden, der das Netz nicht selbst umgestellt hat. Bis 0.4.1 stand ein Netz an der Konsole ohne `--va` auf Taste; ab 0.5.0 ist es
umgekehrt:

| Aufruf | Voreinstellung des Netzes |
| --- | --- |
| ohne Schalter | Sprachaktivierung (VA) |
| `--va` | Sprachaktivierung, wirkt wie kein Schalter |
| `--taste` | Sprechtaste, im Client umstellbar |
| `--nur-taste` | nur Sprechtaste (auch die Voreinstellung ist Taste); `--taste` dazu ist überflüssig, aber erlaubt |
| Netz 1 (Global) | immer Taste und nur Taste, wie beim Anlegen durch den Server (Protokoll 6.5) |

Drei Kombinationen lehnt `funk-admin` mit Exit 2 ab, ohne etwas zu ändern (am Code und im Test geprüft):

- `--taste` zusammen mit `--va`: `--taste und --va zusammen geht nicht: ohne Schalter steht ein Netz auf Sprachaktivierung,
  mit --taste auf Taste. Nichts geändert.`
- `--va` zusammen mit `--nur-taste`: `--va und --nur-taste zusammen geht nicht: ein Netz nur mit Taste steht auch ab Werk auf
  Taste. Nichts geändert.`
- `--va` bei Netz 1: `Global (Netz 1) bleibt immer auf Taste, --va geht dort nicht. Nichts geändert.`

**Kommando von Hand:** Die Beispiele in `EINRICHTEN.md`, `BETRIEB.md` und `HEIMSERVER-PROXMOX.md` und in der Hilfe legen
Kommando mit `--taste` an (Anf. A64, bis 0.9.0). Die Vorlage Star Citizen legt es mit Sprachaktivierung an wie die übrigen
Kanäle; wer es so will, lässt `--taste` weg. `funk-admin` 0.4.x kennt `--taste` nicht und lehnt mit Exit 2 ab; dort legt ein
Netz ohne Schalter ohnehin auf Taste an.

**Gespeicherte Netze bleiben, wie sie sind.** Die neue Voreinstellung gilt nur für das, was `netz-setzen` ab jetzt schreibt;
ein Zustand von 0.4.0 wird nicht umgeschrieben (die Voreinstellung im Lagemodell bleibt für Netze ohne Angabe Taste, im Test
`Uebergang040Tests` gesichert). Wer VA für ein vorhandenes Netz will, stellt es je Feld im Client um (A5.2) oder schreibt das Netz
mit `netz-setzen` neu. Achtung dabei: Eine vorhandene ID wird ganz neu geschrieben (der Server ersetzt das Netzobjekt, am Code
geprüft); ein Aufruf ohne `--taste` stellt dann ein Netz, das auf Taste stand, auf VA. Netze mit `schiffId` und `schiffsnetz` gehen
nur mit `--json` (unten).

Im Verwaltungsteil heißt der Schalter **Voreinstellung Sprechtaste statt Sprachaktivierung** und ist bei einem neu angelegten
Netz aus; dort beginnt ein Netz mit VA (`Flottenentwurf.cs`), wie an der Konsole ab 0.5.0. Die Vorlage Star Citizen setzt
auch Kommando auf VA, wie alle übrigen Kanäle (`Flottenvorlagen.cs`; die alten Vorlagen legten ihr Kommandonetz bis 0.9.0 auf
Taste); dieselbe Vorlage legt `flotte-vorlage` an der Konsole an (B14.2). `--nur-taste` heißt im Verwaltungsteil **Nur mit Sprechtaste**; dann kann auch niemand für sich auf VA umstellen.

`netz-setzen` mit einer vorhandenen ID ändert das Netz. Höchstens 128 Netze. Netze einer Einheit (Crew, Brücke) brauchen
`schiffId` und `schiffsnetz` und gehen deshalb nur mit `--json`, in der Form wie in der Netzliste:

```
funk-admin netz-setzen --json '{"id":11,"name":"Schiff 1 Crew","typ":"NORMAL","reihenfolge":11,"nurTaste":false,"ausloeserStandard":"VA","offen":"NEIN","standard":false,"schiffId":1,"schiffsnetz":"CREW"}'
```

Eine Kennung wie `CREW` hat 1 bis 16 Zeichen aus `A` bis `Z`, `0` bis `9` und `_`.

### B12.2 Beispiele, Löschen, Ansehen

```
funk-admin netz-setzen --id 2 --name Kommando --offen HOEREN_SENDEN --standard --taste
funk-admin netz-setzen --id 4 --name Durchsagen --offen HOEREN --nur-taste
funk-admin netz-setzen --id 6 --name Crew --offen HOEREN_SENDEN
funk-admin netz-setzen --id 90 --name Echo --offen HOEREN_SENDEN --va
funk-admin netz-loeschen --id 4
funk-admin netzliste
```

Kommando steht auf Taste, Durchsagen nur auf Taste, `Crew` ohne Schalter auf Sprachaktivierung, Echo ebenso (`--va` wirkt wie
kein Schalter, B12.1).

`netzliste` zeigt je Netz `Netz <id>  <name>  offen <...>  standard <...>`, dann Rollen und Schiffe und zuletzt
`Version <n>, Selbstwahl <True|False>, Durchsage nur Taste <True|False>`. Die Phasen (B14.3) stehen nicht in dieser Textform; mit
`--json-aus` enthält `daten` die ganze Netzliste samt `phasen`. Beim Löschen fallen Mitgliedschaften auf dem
Netz weg. War der Funk länger als 10 Minuten aus, beginnt jedes Konto wieder mit den Standardnetzen (Protokoll 6.9).

### B12.3 Einstellungen der Netzliste

```
funk-admin einstellung [--selbstwahl an|aus] [--nachzuegler an|aus] [--durchsage-nur-taste an|aus]
```

| Option | Wirkung |
| --- | --- |
| `--selbstwahl aus` | sperrt die Selbstwahl von Rolle und Platz für 12 Stunden (seit 0.3.0); bei gesperrter Selbstwahl zählt auch das Aufgeben des eigenen Platzes als Wechsel |
| `--nachzuegler an` (Voreinstellung) oder `aus` | ob Funker ohne Platz während der Sperre trotzdem einen freien nehmen (bis Client 0.11.1 "Wechsel sperren" gegen "Ganz sperren" auf dem Brett; seit 0.11.2 hat die Funkfeld-Zuteilung diese Knöpfe nicht mehr, gesperrt wird nur noch hier) |
| `--selbstwahl an` | hebt die Sperre auf; nötig für eine Sperre aus 0.2.0, die keine Frist hat |
| `--durchsage-nur-taste an\|aus` | Durchsagen nur mit Sprechtaste |

Antwort `OK, zustandsVersion <n>`.

## B13 Rollen (Funkrollen)

Eine Funkrolle wie "Kapitän" ist eine Vorlage für die Felder eines Funkers: je Feld ein festes Netz oder ein Netz
der Einheit (über die Kennung), mit Hören, Senden, Sendevorschlag und Festlegung. Sie ist etwas anderes als die
Mitgliederrollen im Rollenkatalog (B11.6).

```
funk-admin rolle-setzen --id <n> --name <n> [--selbst] [--braucht-schiff] [--kapazitaet n] [--reihenfolge n] --feld feld:netz[:h][:s][:std][:fest]...
```

| Option | Bedeutung |
| --- | --- |
| `--id`, `--name` | Pflicht; Name bis 40 Zeichen; höchstens 64 Rollen |
| `--selbst` | Funker dürfen die Rolle selbst wählen |
| `--braucht-schiff` | die Rolle gilt nur zusammen mit einer Einheit |
| `--kapazitaet` | wahlweise eine Kapazität der Rolle (Feld `kapazitaet`); die Plätze je Einheit stehen in der Einheit (B14) |
| `--reihenfolge` | Sortierung |
| `--feld` | mehrmals, höchstens 16: `feld:netz` mit Feld 0 bis 15 und Netz-ID, dahinter wahlweise `h` (hören), `s` (senden), `std` (dorthin wird zuerst gesendet), `fest` (der Funker kann das Feld nicht ändern) |

Felder mit einem Netz der Einheit (`schiffsnetz` statt `netzId`) gehen nur mit `--json`:

```
funk-admin rolle-setzen --id 4 --name Flottenführung --selbst --feld 0:3:h:s:std --feld 1:4:h:s --feld 2:5:h:s --feld 3:2:h:s
funk-admin rolle-setzen --json '{"id":1,"name":"Pilot","reihenfolge":1,"selbstWaehlbar":true,"brauchtSchiff":true,"felder":[{"feld":0,"schiffsnetz":"BRUECKE","hoeren":true,"senden":true,"sendenStandard":true,"fest":false},{"feld":1,"schiffsnetz":"CREW","hoeren":true,"senden":true,"sendenStandard":false,"fest":false},{"feld":2,"netzId":3,"hoeren":true,"senden":false,"sendenStandard":false,"fest":false}]}'
funk-admin rolle-loeschen --id 4
```

Die Beispiele stammen aus `konfig/probefunk.json`, der Probe-Konfiguration für den Testserver.

Seit 0.5.0 kann eine Rolle in einer Einsatzphase andere Felder haben als hier (B14.3). Die Felder, die `rolle-setzen` schreibt,
sind die der Grundstellung (Phase 1); Varianten legt nur der Verwaltungsteil an.

## B14 Flotte

### B14.1 Einheiten

Eine Einheit ist ein Schiff, ein Trupp (Infanterie) oder eine Orga-Einheit (Anf. A39). Alle drei haben Netze über Kennungen,
Plätze je Rolle mit Kapazität und wahlweise einer Mindestbesetzung (bis Client 0.11.1 für die Ampel auf dem Brett; die
Funkfeld-Zuteilung von 0.11.2 zeigt keine Einheiten und keine Ampel mehr; beim Einteilen prüft der Server sie nicht). An der
Konsole gehen Einheiten nur als JSON:

```
funk-admin schiff-setzen --json '{"id":1,"name":"Schiff 1","reihenfolge":1,"art":"SCHIFF","typ":"Hammerhead","netze":{"CREW":11,"BRUECKE":12},"plaetze":[{"rolleId":1,"kapazitaet":1},{"rolleId":2,"kapazitaet":6,"mindest":2},{"rolleId":3,"kapazitaet":1}]}'
funk-admin schiff-loeschen --id 1
```

| Feld | Grenzen | Bedeutung |
| --- | --- | --- |
| `id`, `name`, `reihenfolge` | Name bis 32 Zeichen; höchstens 64 Einheiten | |
| `art` | `SCHIFF`, `TRUPP`, `ORGA`; fehlt es, ist es ein Schiff | |
| `typ` | bis 32 Zeichen, wahlweise | freier Typ, etwa "Hammerhead" |
| `netze` | höchstens 8 Kennungen, je auf eine Netz-ID | die Netze der Einheit; die Netze selbst mit `netz-setzen --json` und `schiffId` |
| `plaetze` | höchstens 16, je `rolleId`, `kapazitaet`, wahlweise `mindest` (nicht über der Kapazität) | |

Leichter geht die Flotte im Client: **Verwaltung**, **Flotte** (nur mit `NETZE_VERWALTEN` in einer Admin-Sitzung). Dort
entsteht ein Entwurf; übernommen wird erst mit **Übernehmen** und frischem Code, in einem Schritt. Die Seite hat die Reiter
**Einheiten**, **Rollen** und **Netze** und, nur an einem Server ab 0.5.0, **Phasen** (B14.3).

### B14.2 Vorlage Star Citizen

```
funk-admin flotte-vorlage
```

Legt die Vorlage neben die vorhandene Flotte, nur solange es außer Global kein Kanalnetz gibt: 10 Kanäle mit Ch. (Kommando,
Flotte, Brücke, Crew, Jäger, Bodentrupp, Logistik, Bergung und Bergbau, Sanität, Allgemein), alle offen zum Hören und Senden,
mit Sprachaktivierung, und 14 Rollen von Standard (Gast) bis Funker (Leitstand), selbst wählbar; keine Einheiten. Global
ändert sie nicht. Antwort:
`Star Citizen übernommen: 24 Schritte. Ab jetzt kommen nur noch Clients ab 0.6.0 hinein (Versionstor); ...` mit `Annahmen,
bitte prüfen:` und darunter sechs Zeilen mit `- `. Die Annahmen prüft der Admin. Gibt es schon Kanalnetze: `Fehler: Es gibt
schon Kanalnetze; die Vorlage Star Citizen legt alle neu an und gehört auf eine Flotte ohne sie. Erst die Kanalnetze löschen.
Nichts geändert.` Im Client steht dieselbe Vorlage unter **Verwaltung**, **Werkzeuge**, **Vorlage Star Citizen** (nur an
einem Server ab 0.6.0 und ebenso nur ohne Kanäle mit Ch.): Sie kommt in den Entwurf, übernommen wird erst mit
**Übernehmen**. `--name` kennt nur `starcitizen`.

### B14.3 Einsatzphasen (ab 0.5.0)

Eine Phase ist ein Netzbild der Flotte, keine Personenliste (Protokoll 6.14): Der Admin legt einmal fest, welche Felder eine Rolle
in "Phase 2 Entern" hat, und die Einsatzleitung schaltet im Raid um. Plätze, Rollen, Reservierungen und Belegung ändert eine Phase
nie; es gibt also kein Verdrängen und kein `PLATZ_BELEGT`, und wer später dazukommt oder einspringt, bekommt das Netzbild der
laufenden Phase von selbst. Wie die Einsatzleitung per Taste oder Joystick schaltet (bis Client 0.11.1 auch am Brett),
steht in Teil A (A12.6).

| Was | Wert (am Code und im Protokoll geprüft) |
| --- | --- |
| Aufbau | Phase 1 ist die **Grundstellung**: die Felder der Rollen (B13); sie trägt nur Name und Kurzname und hat nie Varianten. Dazu höchstens **4 weitere Phasen** (Nummer 2 bis 5, Lücken erlaubt) |
| Name, Kurzname | Name bis 24, Kurzname bis 12 Zeichen, beide nicht leer; der Kurzname steht in der Meldezeile (bis Client 0.11.1 auch in der Phasenleiste am Brett) |
| Variante | je Rolle, die sich in der Phase ändert, eine vollständige Feldliste in der Form der Rollenfelder (mindestens ein Feld, höchstens 16). Eine Rolle ohne Variante bleibt "wie Grundstellung". Eine Variante gilt für alle Einheiten mit dieser Rolle; ein Netz je Schiff geht über eine Kennung (`schiffsnetz`) |
| Grenze | zusammen höchstens 512 Variantenfelder über alle Phasen |
| Anlegen | im Verwaltungsteil, **Verwaltung**, **Flotte**, Reiter **Phasen** (nur an einem Server ab 0.5.0; Recht `NETZE_VERWALTEN` in einer Admin-Sitzung). Liste mit Nummer, Name und Kurzname, **Neue Phase** (Tooltip: "Höchstens vier Phasen über der Grundstellung"), je Rolle **Variante anlegen** (übernimmt die Felder der Grundstellung zum Ändern, im Felder-Editor mit Vorschau) oder **wie Grundstellung**, darunter "Wen sie trifft". Übernommen wird wie alles der Flotte als ein Paket mit frischem Code (Phasen löschen zuerst, setzen zuletzt); Sicherung und Datei der Flotte tragen die Phasen mit (Standzeile 01.10.2026 22:21) |
| Server | `Verwaltung PHASE_SETZEN` und `PHASE_LOESCHEN`, einzeln oder im `PAKET`; Netz- und Rollenverweise prüft der Server wie bei den Rollenfeldern |
| Konsole | keine Befehle (B9.8) |

**Schalten (Protokoll 6.14, Nachricht `0x003D`):**

- Nur mit `ZUWEISEN`: ein ADMIN-Konto in der gewöhnlichen Sitzung, ein Beauftragter (B15) und wer das Recht einzeln hat. Die
  Führung ohne Auftrag und ein ADMIN im Funker-Modus (B10.7) schalten nicht (`KEIN_RECHT`).
- Der Client schickt mit, welche Phase er für die laufende hält (`erwartet`). Hat inzwischen ein zweiter Leiter geschaltet, antwortet
  der Server `VERALTET` und nennt die laufende Phase und wer geschaltet hat; die Flotte geht nicht ungesehen um.
- Betroffen ist jedes Konto mit Platz (belegt oder reserviert, auch ohne Sitzung), dessen Rolle in der alten oder der neuen Phase eine
  Variante hat. Konten, deren Felder sich nicht ändern, fasst der Server nicht an (keine neue `kontoVersion`, kein Push).
- **Rangregel:** Wen der Schaltende nach der Rangregel nicht einteilen darf, den lässt der Server aus: Ein ADMIN-Konto stellt nur ein
  ADMIN um, es sei denn, es spielt "Heute als Funker". Die Ausgelassenen stehen in der Antwort; der Client nennt sie in der
  Meldezeile.
- Zusatznetze, die die Leitung gesetzt hat, eigene offene Netze, "Hören aus" und die eigene Sendeauswahl bleiben erhalten (wer auf
  dem Sendevorschlag der alten Phase sendete, sendet danach auf dem der neuen).
- Der Server meldet den Wechsel an alle (`0x4004 PhaseGeschaltet`) und schickt den Umgestellten neue Mitgliedschaften; im Logbuch
  steht eine Zeile (B1.7).
- Bremse: 1 Wechsel je 2 Sekunden, Stoß 2. Eine Flut von Wechseln endet bei `GRENZE`, ohne Trennung; der Server bleibt auf der zuletzt
  bestätigten Phase (als Test belegt).

**Laufende Phase und Rückfall:** Die laufende Phase steht in `lage.json`. **12 Stunden nach dem letzten Schalten** fällt die Flotte
auf die Grundstellung zurück, im Fristen-Takt des Servers und auch nach einem Neustart; im Logbuch steht dann `durch Konsole oder
Rückfall`. "Abend beenden" im Client schaltet zuerst auf Phase 1.

**Eine Phase ändern oder löschen, während sie läuft:** Ändert der Admin die laufende Phase, stellt der Server beim Übernehmen
sofort um, wie bei einem Wechsel von der alten auf die neue Fassung; der Verwaltungsteil fragt vorher nach ("Phase N läuft gerade:
..."). Löscht er sie, fällt die Flotte auf die Grundstellung zurück. Die Grundstellung löscht niemand (`UNGUELTIG`). Löscht jemand
ein Netz, das die einzige Variante einer Rolle in einer Phase trug, fällt die Variante ganz weg: Die Rolle steht in der Phase wie in
der Grundstellung (bei einer laufenden Phase sofort). Bis zur Gegenprüfung am 01.10.2026 ließ `NETZ_LOESCHEN` eine Variante ohne
Feld stehen, und der Server startete danach nicht mehr; behoben (Standzeile 01.10.2026 23:05), `LageLaden` übergeht seitdem leere
Varianten.

**Als Tests belegt** (Standzeile 01.10.2026 20:15 und 23:05): ein Beauftragter gegen ein ADMIN-Konto (ausgelassen, beim Verschieben
`KEIN_RECHT`), Phase schalten ohne `ZUWEISEN`, mit abgelaufenem Auftrag und als ADMIN im Funker-Modus (je `KEIN_RECHT`), eine Flut
von Phasenwechseln (Stoß 2, dann `GRENZE`, keine Trennung).

**Offen und nicht gemessen:**

- Die Netzlast eines Phasenwechsels bei 40 Leuten (Steuerkanal an alle, am Pacer vorbei, Protokoll 7.4) ist nicht gemessen; die
  Netzliste mit 5 Phasen ist im ungünstigsten Fall 323 KB groß (B1.8).
- Die Komfortablage (`komfort.json`) behält die letzte Sendeauswahl über Phasenwechsel und setzt sie nach einem Neustart wieder; das
  ist als Befund offen (Standzeile 01.10.2026 20:15 und 23:05; sie bestand schon vor S2).
- Die Vorschau beim ersten Phasendruck erkennt Admins nur an ihrem Zeichen: Wer es ausblendet, steht erst im Ergebnis des Servers als
  ausgelassen.

## B15 Auftrag (Einsatzleitung für einen Abend)

Einteilen dürfen ADMIN-Konten (auch ohne Admin-Sitzung, nicht im Funker-Modus, B10.7) und wer einen Auftrag hat (Anf. A37). Ein
Auftrag gilt für einen Abend und endet von selbst. Mit dem Recht `ZUWEISEN`, das ein Auftrag immer enthält, schaltet der
Beauftragte auch die Einsatzphasen (B14.3). Ein ADMIN-Konto stellt er nach der Rangregel nicht ein (beim Verschieben antwortet der
Server `KEIN_RECHT`, beim Phasenschalten wird es ausgelassen), es sei denn, das Konto spielt "Heute als Funker".

```
funk-admin auftrag --konto <id> [--von HH:MM] [--bis HH:MM] [--rechte A,B] [--grund <text>]
```

| Regel | Wert |
| --- | --- |
| Beginn | ohne `--von` sofort; `--von 20:00` oder `--von '2026-10-09 20:00'` |
| Ende | ohne `--bis` der nächste Zeitpunkt 04:00; `--bis 23:30` früher |
| Zeitzone | Ortszeit Europe/Berlin; fehlt `/usr/share/zoneinfo/Europe/Berlin` (Paket `tzdata`), rechnet der Server in der Zeitzone des Systems, meist UTC |
| Dauer | höchstens 24 Stunden |
| im Voraus | bis 14 Tage |
| zugleich | höchstens 4 Aufträge |
| Rechte | ohne `--rechte` alle sechs der Einsatzleitung: `GLOBAL_SENDEN`, `DURCHSAGE`, `DURCHSAGE_ALLE`, `MEHRFACH_SENDEN`, `ZUWEISEN`, `SPRECHANZEIGE_ALLE`. Pflicht sind `ZUWEISEN` und `SPRECHANZEIGE_ALLE`; nie ein Verwaltungsrecht |
| Grund | wahlweise, bis 48 Zeichen |
| nicht an | ein ADMIN-Konto (es teilt ohnehin ein), ein gesperrtes Konto |
| zweiter Auftrag an dasselbe Konto | ersetzt den ersten |

```
funk-admin auftrag --konto 2 --grund 'Op Nachtfalke'
funk-admin auftraege
funk-admin auftrag-entziehen --konto 2
```

**Das siehst du:** `Einsatzleitung für <Name> (Konto <id>) von <TT.MM. HH:MM> bis <TT.MM.> 04:00.`; `auftraege` zeigt je
Auftrag `Mustermann (Konto 2): läuft, 01.10. 20:15 bis 02.10. 04:00, von Konsole, Op Nachtfalke, Rechte GLOBAL_SENDEN DURCHSAGE
DURCHSAGE_ALLE MEHRFACH_SENDEN ZUWEISEN SPRECHANZEIGE_ALLE` (oder `geplant`). Steht hinter `von` eine Zeit zwei Stunden
früher (im Winter eine), fehlt die Zeitzone. Der Beauftragte sieht im Client links **Raid** ohne Neustart; 15 Minuten vor
dem Ende warnt ihn der Client. `auftrag-entziehen` antwortet `Auftrag von <Name> (Konto <id>) entzogen.` oder `<Name>
(Konto <id>) hat keinen Auftrag.`

Im Client: **Verwaltung**, **Einsatzleitung**, **Einsatzleitung vergeben** (mit frischem Code). `sicherung` nimmt
Aufträge mit Absicht nicht mit; nach dem Zurückspielen vergibst du laufende Aufträge neu (B18). Wer dauerhaft einteilen
soll, bekommt statt eines Auftrags das Recht `ZUWEISEN` (B11.4).

## B16 Sperrliste und fail2ban

Warum die Sperrliste so gebaut ist und welche Grenzen daneben gelten, steht in C14.

### B16.1 Die eingebaute Sperrliste

Ab 0.3.0 sperrt `funk-server` Adressen, die sich wie Scanner, Botnetze oder Durchprobierer benehmen, selbst und befristet
(Anf. A51). Einzurichten ist dafür nichts; das ist der Hauptschutz des Funks.

**Punkte je Präfix** (IPv4-Adresse, IPv6-/64), in 10 Minuten zusammengezählt; ab 10 Punkten ist das Präfix gesperrt:

| Anlass | Punkte | Kennung im Logbuch |
| --- | ---: | --- |
| gescheiterter oder abgebrochener TLS-Handshake (Scanner, Browser, ein Client, der den Pin ablehnt) | 1 | `HANDSHAKE` |
| leerer Handshake-Topf des Präfixes | 1 | `HANDSHAKE_FLUT` |
| Handshake ohne Gerätezertifikat oder ALPN | 2 | `OHNE_GERAETEZERTIFIKAT` |
| Protokollmüll vor der Anmeldung oder kein Hello in der Frist | 3 | `PROTOKOLLFEHLER` |
| Antrag oder Registrierungscode über der Grenze (3 je Verbindung, 10 je Präfix und Stunde) | 3 | `ANTRAGSFLUT`, `REGISTRIERUNGSFLUT` |
| gesprengte Ratengrenzen eines unfreigegebenen Geräts | 5 | `RATENGRENZE` |

| Was | Wie |
| --- | --- |
| Wirkung | Verbindungen gesperrter Präfixe schließt die TLS-Annahme gleich nach `accept` mit RST, vor TLS und vor den Töpfen. UDP ohne Sitzung verwirft der Server ohnehin vor jeder Kryptografie |
| Dauer | 5 min, dann je weiterer Sperre sechsmal so lang (30 min, 3 h, 18 h), höchstens 24 h; nach 48 h ohne Sperre wieder ab 5 min |
| IPv6 | sind 4 /64 aus einem /48 gesperrt oder sammeln seine /64 zusammen 40 Punkte, gilt eine Sammelsperre für das /48 (Kennung `g6#...`) |
| nie gesperrt | Präfixe der Vorrangspur (in den letzten 14 Tagen eine aktive Sitzung von dort: Mitglieder, Familie hinter derselben Adresse), `vertrauenswuerdigeWeiterleiter`, `sperrliste.ausnahmen`; dort zählen nicht einmal Punkte |
| Durchlass | während einer Sperre je Adresse alle 30 s eine Verbindung bis zum Handshake; ein freigegebenes Gerät hebt danach die Sperre seiner Adresse auf (Logbuch `Sperrliste: Sperre für p4#... aufgehoben (freigegebenes Gerät)`) |
| globale Grenzen | sind kein Verstoß: Ist die Tabelle der Code- und Antragszähler voll (4096 Präfixe je Stunde, Alarm `REGISTRIERUNG_FLUT`), bekommt jedes neue Präfix `GRENZE`, aber keine Punkte |
| Speicher | höchstens 10000 Präfixe; ist die Liste voll, fallen zuerst ungesperrte, lange ruhige heraus, Sperren nie |
| Neustart | Sperren und Stufen stehen in `sperrliste.json` (nur HMAC-Werte und Kennungen, nie eine Adresse) und überstehen Neustart und Update; eine Sperre, die weiter als 24 h in die Zukunft reicht, gilt beim Laden nur 24 h. Die Datei gehört nicht in die Sicherung |
| Uhr | die Liste rechnet mit der Systemuhr; springt sie zurück, gilt eine Sperre höchstens ihre Dauer ab dem Sprung |
| Logbuch | je neuer Sperre `WARNUNG Sperrliste: p4#... für 5 min gesperrt (Stufe 1, Anlass HANDSHAKE, 10 Punkte)`, abgewiesene Verbindungen nur mit `log.stufe` DEBUG; nie die Adresse |

Die Werte stehen in `server.json` unter `sperrliste` (B6.2); der Abschnitt darf fehlen.

### B16.2 Ansehen und aufheben

```
funk-admin sperrliste
funk-admin entsperren --ip <adresse des mitglieds>
funk-admin entsperren --eintrag <kennung>
funk-admin entsperren --alle
```

`sperrliste` zeigt `Keine Sperren (<n> Adressen beobachtet).` oder `<n> Sperre(n), <m> Adressen beobachtet (Schwelle 10
Punkte in 10 min):` und je Sperre Kennung, `/48` bei einer Sammelsperre, Ende in UTC, Restdauer, Stufe, Anlass und die
Zahl abgewiesener Verbindungen.

`entsperren` braucht genau eins der drei. Antworten: `Sperre für p4#... aufgehoben.`, `Für diese Adresse (p4#...) gibt es
keine Sperre. Nichts geändert.`, `Keine Sperre mit der Kennung ... (funk-admin sperrliste zeigt sie). Nichts geändert.`,
`Alle Sperren aufgehoben (<n>).` Die Antwort nennt nur die Kennung, nie die Adresse. Bei IPv6 hebt `--ip` auch die
Sammelsperre des /48 auf. Journal: `SPERRE_AUFGEHOBEN`.

**Ein Mitglied kommt nicht herein:** Es sitzt an einer neuen Adresse, die ein Angreifer gerade füllt (gleiche öffentliche
Adresse bei Mobilfunk oder CGNAT). Seine Adresse lässt du dir nennen (etwa von einer Seite wie "Wie ist meine IP"), dann
`entsperren --ip`. Mit fail2ban zusätzlich auf dem Host `fail2ban-client unban <adresse>`.

### B16.3 fail2ban als Zusatz

fail2ban kann mit HMAC-Werten nichts anfangen. Dafür schreibt `funk-server` auf Wunsch je neuer Sperre eine Zeile mit
Klartext-IP in eine eigene Datei; das Logbuch bleibt ohne Klartext-IP. Voreinstellung: aus.

**Für den Heimserver nicht empfohlen** (`HEIMSERVER-HAERTUNG.md`, Punkt 7.2): fail2ban am Host kennt keinen Durchlass. Solange
es eine Adresse sperrt, erreicht ein Mitglied hinter ihr weder den Funk noch Foundry (die Sperre gilt für alle Ports), und
`funk-admin entsperren` hebt sie nicht auf. Im unprivilegierten LXC kann fail2ban keine Firewallregeln setzen; es liefe auf
dem Proxmox-Host.

Willst du es trotzdem, in dieser Reihenfolge:

**1. Klartextdatei einschalten** (im Container oder auf dem Server). Erst nachsehen, dass es noch keinen Abschnitt
`sperrliste` gibt (`grep -c sperrliste /etc/funk/server.json` zeigt `0`), dann diese Zeile mit dem Komma am Ende direkt über
die Zeile `"log": {` setzen, gleich eingerückt:

```
  "sperrliste": { "klartextDatei": "/var/log/funk/sperren.log" },
```

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen && systemctl restart funk-server
ls -l /var/log/funk/sperren.log
journalctl -u funk-server -n 20 --no-pager | grep Sperrliste
```

`funk-server` legt die Datei beim Start leer an (fail2ban startet mit einem Jail ohne Datei gar nicht). Im Journal steht
`Sperrliste: neue Sperren gehen zusätzlich mit Klartext-IP nach /var/log/funk/sperren.log`; steht dort `Sperrliste:
Klartextdatei nicht schreibbar`, fehlt in der Unit `LogsDirectory=funk`. Jede Zeile hat diese feste Form, eine je neuer
Sperre, etwa 150 Byte:

```
2026-10-01T09:30:05Z FUNK-SPERRE ip=203.0.113.7 netz=203.0.113.7/32 dauer=300 stufe=1 anlass=HANDSHAKE kennung=p4#0a1b2c3d4e
```

Eine Aufbewahrungsfrist gibt es nicht (offener Punkt); wird die Datei groß: `truncate -s 0 /var/log/funk/sperren.log`.

**2. Filter und Jail** aus dem Paket (`/opt/funk/konfig/fail2ban/`) nach `/etc/fail2ban/filter.d/tacrelay.conf` und
`/etc/fail2ban/jail.d/tacrelay.conf`. Der Filter:

```
[Definition]
failregex = FUNK-SPERRE ip=<HOST> netz=\S+ dauer=\d+ stufe=\d+ anlass=[A-Z_]+ kennung=\S+
ignoreregex =
```

Das Jail (für den Proxmox-Host):

```
[tacrelay]
enabled   = false
filter    = tacrelay
logpath   = /var/lib/lxc/<CT-Nummer>/rootfs/var/log/funk/sperren.log
backend   = polling
maxretry  = 1
findtime  = 1h
bantime   = 5m
chain     = FORWARD
banaction = iptables-allports
protocol  = all
port      = 8443,47443
```

| Zeile | Warum |
| --- | --- |
| `logpath` unter `/var/lib/lxc/<CT-Nummer>/rootfs` | so sieht der Host das Dateisystem eines laufenden Containers |
| `maxretry = 1` | jede Zeile ist schon eine Sperre des Servers |
| `bantime = 5m`, ohne Steigerung | so lang wie die erste Sperre des Servers; länger und steigend sperrt der Server selbst |
| `chain = FORWARD` | Verkehr zum Container läuft über die Brücke des Hosts, nicht durch INPUT; wirkt nur mit `sysctl net.bridge.bridge-nf-call-iptables` gleich 1 (Proxmox setzt das, sobald für eine Netzwerkkarte eines Containers die Firewall an ist) |
| `banaction = iptables-allports`, `protocol = all` | sperrt alle Ports der Adresse, auch UDP |

Auf einem gemieteten Server ohne Container liefe fail2ban auf demselben Rechner; dann wäre `logpath` direkt
`/var/log/funk/sperren.log` und `chain = INPUT`, wie es `HEIMSERVER-HAERTUNG.md` für nginx auf dem Host vorsieht. Das ist
abgeleitet und nicht gemessen.

**3. Prüfen, dann einschalten:**

```
fail2ban-regex /var/lib/lxc/150/rootfs/var/log/funk/sperren.log /etc/fail2ban/filter.d/tacrelay.conf
```

Erst danach im Jail `enabled = true`, dann:

```
systemctl restart fail2ban
fail2ban-client status tacrelay
fail2ban-client status
```

Unter `File list` steht der Pfad aus dem Jail, unter `Currently banned` eine 0; `fail2ban-client status` nennt weiter alle
Jails. Fehlt ein Jail, nennt `journalctl -u fail2ban -n 20 --no-pager` den Grund (`Have not found any log file for <jail>
jail`: der Container läuft nicht, oder der Pfad ist vertippt). Damit fail2ban nach einem Neustart des Hosts erst nach den
Containern startet, ein Drop-in `/etc/systemd/system/fail2ban.service.d/nach-containern.conf` mit `[Unit]` und
`After=pve-guests.service`, dann `systemctl daemon-reload`.

**4. Sperre testen** über den Handy-Hotspot: auf dem Host `fail2ban-client set tacrelay banip <adresse des hotspots>`;
JG TacRelay verliert die Verbindung, Foundry lädt nicht mehr. Dann `fail2ban-client set tacrelay unbanip <adresse>`.
Bleibt die Verbindung trotz Sperre, wirkt die Kette FORWARD nicht; dann das Jail wieder auf `enabled = false`.

## B17 Härtung

Was die Härtung bewirkt und warum der Host zum Sicherheitskonzept gehört, steht in C16.

### B17.1 Was der Server selbst mitbringt

- Nach außen nur zwei Ports, kein HTTP, keine Web-Oberfläche, kein Admin-Port; verwaltet wird über eine Datei
  (Verwaltungssocket) mit Prüfung der UID.
- Die eingebaute Sperrliste gegen Scanner, Handshake-, Antrags- und Registrierungsfluten (B16).
- Clients erkennen den Server am Pin; Profile sind offline signiert, der Profilschlüssel liegt nie auf dem Server. Wer den
  laufenden Serverschlüssel erbeutet, kann damit weder Pins noch Hosts der Clients ändern.
- Schlüssel kommen über `LoadCredential`, nie aus einer lesbaren Konfiguration; keine Speicherabbilder (`LimitCORE=0`).
- Die Unit läuft als eigener Benutzer ohne Fähigkeiten, mit schreibgeschütztem System und nur drei beschreibbaren Ordnern
  (B7.1).
- Das Logbuch nennt Adressen und Konten nur als gekürzte HMAC-Werte.

### B17.2 Checkliste für den Rest des Rechners (Kurzfassung von HEIMSERVER-HAERTUNG.md)

Die vollständige Checkliste mit "Das siehst du" je Punkt ist `HEIMSERVER-HAERTUNG.md` (im Paket unter `linux`). Sie wird
nicht ausgeführt, sondern Punkt für Punkt abgehakt, an einem ruhigen Abend.

| Punkt | Kurz | Prüfen |
| --- | --- | --- |
| 0 Soll-Zustand | von außen genau TCP 443 (nginx, Foundry), TCP 8443 und UDP 47443 (Funk); keine Proxmox-Weboberfläche (8006), kein SSH (22), Port 80 nur für die HTTP-Prüfung von Let's Encrypt | |
| 1 FRITZ!Box | nur die Freigaben aus Punkt 0; "Selbstständige Portfreigaben für dieses Gerät erlauben" aus (UPnP); Internetzugriff auf die FRITZ!Box über HTTPS aus | vom Handy über mobile Daten: `https://<dein-name>:8006` lädt nicht |
| 2 Proxmox-Weboberfläche | nie nach außen, von unterwegs über VPN; Zwei-Faktor (TOTP) für die Anmeldung, Recovery Keys getrennt vom Telefon | im Heimnetz fragt die Anmeldung nach dem Code |
| 3 SSH nur mit Schlüssel | auf dem Host und in jedem Container mit SSH (Befehle unten) | ohne Schlüssel `Permission denied (publickey)` |
| 4 Updates | in den Containern `unattended-upgrades` (nur Sicherheitsupdates aus Debian); die .NET-Laufzeit von Hand mit Neustart des Dienstes; auf dem Proxmox-Host von Hand, etwa monatlich | `unattended-upgrade --dry-run --debug` |
| 5 nginx vor Foundry | `server_tokens off;`, keine `autoindex on`; `limit_req` mit zwei Zonen, erst einen Spielabend mit `limit_req_dry_run on;`, dann scharf; `/setup` nur aus dem Heimnetz | `nginx -t`; `zgrep -c 'limiting requests' /var/log/nginx/error.log*` |
| 6 Port 80 | nur, wenn certbot über HTTP prüft; dann nur Umleitung auf HTTPS und `/.well-known/acme-challenge/` | `http://<dein-name>` springt auf `https://` |
| 7 fail2ban | auf dem Proxmox-Host, nicht im Container: `sshd` über das Journal, Start nach den Containern, Jails für nginx; für den Funk nicht empfohlen (B16.3) | `systemctl is-active fail2ban`, `fail2ban-client status` |
| 8 Mitglied bleibt draußen | `funk-admin sperrliste`, `funk-admin entsperren --ip <adresse>`; mit fail2ban dazu `fail2ban-client unban <adresse>` | `Sperre für p4#... aufgehoben.` |

SSH nur mit Schlüssel, in dieser Reihenfolge und mit einer zweiten offenen Sitzung, bis die neue Anmeldung geklappt hat. Auf
dem PC (PowerShell), einmal, mit Passphrase:

```
ssh-keygen -t ed25519 -C "admin-pc"
```

Die Zeile aus `C:\Users\<du>\.ssh\id_ed25519.pub` ans Ende von `/root/.ssh/authorized_keys` auf dem Server, dann in einem
zweiten Fenster `ssh root@<adresse>` prüfen. Erst wenn das klappt, `/etc/ssh/sshd_config.d/10-nur-schluessel.conf`:

```
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
```

```
sshd -t
systemctl reload ssh
ssh -o PubkeyAuthentication=no root@<adresse>
```

`sshd -t` gibt nichts aus; die letzte Zeile endet mit `Permission denied (publickey)`. Die Datei im Ordner
`sshd_config.d` wirkt nur, wenn `/etc/ssh/sshd_config` diesen Ordner einbindet (`Include`); die tatsächlich wirksamen
Werte zeigt `sshd -T` (offener Hinweis aus der Prüfung vom 01.10.2026, `PROJEKT.md`).

### B17.3 Auf einem gemieteten Server

- SSH nur mit Schlüssel und `unattended-upgrades` wie oben; einen Proxmox-Host, eine FRITZ!Box und nginx für Foundry gibt
  es dort nicht.
- Firewall am Rechner und beim Anbieter: nur SSH, der TCP-Port des Funks und UDP 47443 (B4.5).
- Läuft dort schon ein anderer Dienst (etwa `coms.example.org`), teilen sich beide keinen Port; `einrichten` prüft
  das.
- Wer root auf dem Server ist, hat am Verwaltungssocket alle Rechte des Eigentümers (B1.5). Root-Zugänge also so knapp
  wie möglich halten.

## B18 Sicherung und Zurückspielen

### B18.1 Was wohin gehört

| Was | Wie gesichert | Wohin |
| --- | --- | --- |
| Zustand (Konten, Geräte, Netze, Mitglieder, Admins, Journal) | `funk-admin sicherung`, regelmäßig und vor jedem Update | erst `/var/lib/funk/sicherungen/<zeit>`, dann weg vom Server |
| `server.crt`, `server.key`, `funk-geheimnis` | einmal von Hand | Stick des Eigentümers, verschlüsselt; nie in ein Repository, nie in eine Cloud, nie in einen Chat |
| `funk-totp-schluessel` | gar nicht | bleibt nur auf dem Server; geht er verloren, richten die Admins den Zweitfaktor neu ein |
| `profil.key`, `reserve.key`, `reserve.crt`, `profil.json` | liegen schon auf dem Stick | Stick, mit BitLocker To Go |
| Notfallcodes | vom Admin selbst | getrennt vom Telefon: sichere Notiz in einem Passwortmanager oder ausgedruckt an einem sicheren Ort |
| Geräteschlüssel des Echo-Funkers | gar nicht | geht er verloren, wird der Echo-Funker neu registriert |
| ganzer Container (vzdump) | wahlweise | nur verschlüsselt ablegen (B18.4) |

### B18.2 Zustand sichern

```
funk-admin sicherung
```

Antwort: `Sicherung nach /var/lib/funk/sicherungen/<JJJJMMTT-HHmmss>`. Kopiert werden, soweit vorhanden, `lage.json` (ohne
Aufträge und ohne Funker-Modus), `konten.json`, `komfort.json`, `vorrangspur.json`, `admins.json`, `mitglieder.json` und `journal.json`. Das
Journal schreibt der Server vorher sofort, auch wenn gerade ein Schreiben im Hintergrund läuft. Nie dabei: Schlüssel,
Betriebsgeheimnis, TOTP-Schlüssel und `sperrliste.json`.

- Die TOTP-Geheimnisse in `admins.json` lassen sich ohne `funk-totp-schluessel` nicht öffnen; das Journal trägt aber
  Klarnamen. Die Sicherung ist also schützenswert.
- **Aufträge** nimmt die Sicherung mit Absicht nicht mit: Aus einer zurückgespielten Sicherung käme sonst ein entzogener
  Auftrag zurück. Ab 0.5.0 gilt dasselbe für den Funker-Modus (`alsFunkerBis`, B10.7). Die Phasen und die laufende Phase bleiben
  in der Sicherung, denn die Felder der Konten passen zu ihr (am Code geprüft, `Zustandsablage.OhneAuftraege`).
- `--ziel <verzeichnis>`: Die Sicherung schreibt der Dienst selbst, als Benutzer `funk` unter den Schutzregeln der Unit.
  Ein relativer Pfad gilt ab dem Ordner der `server.json`. Beschreibbar ist für den Dienst nur, was die Unit freigibt
  (`/var/lib/funk`, `/run/funk`, `/var/log/funk`). Ein Ziel außerhalb davon scheitert deshalb voraussichtlich (aus Unit
  und Code abgeleitet, nicht gemessen). Am einfachsten das Standardziel nehmen und danach wegkopieren.

### B18.3 Schlüsseldateien auf den Stick

Aus dem Container über den Proxmox-Host:

```
pct pull 150 /etc/funk/credentials/server.key /root/server.key
pct pull 150 /etc/funk/credentials/server.crt /root/server.crt
pct pull 150 /etc/funk/credentials/funk-geheimnis /root/funk-geheimnis
```

Dann mit `scp` auf den Stick und auf dem Host löschen:

```
shred -u /root/server.key /root/funk-geheimnis
rm /root/server.crt
```

Auf dem Stick liegen danach `server.key`, `server.crt`, `funk-geheimnis`, `profil.key`, `reserve.key` und `reserve.crt`;
auf dem Host nichts mehr davon (`ls /root`). Auf einem gemieteten Server holst du dieselben drei Dateien mit `scp` direkt
auf den Stick. Ohne diese Dateien gibt es nach einem Totalausfall einen neuen Pin, und jeder Client braucht ein neues
Profil.

### B18.4 Sicherung des ganzen Containers

Eine Proxmox-Sicherung (vzdump, Proxmox Backup Server) enthält `/etc/funk/credentials` mit Serverschlüssel,
Betriebsgeheimnis und `funk-totp-schluessel` neben `admins.json` und dem Journal mit Klarnamen. Wer so eine Sicherung
stiehlt, hat die TOTP-Geheimnisse aller Admins (braucht für eine Admin-Sitzung aber weiter den Admin-Schlüssel im TPM).
Solche Sicherungen nur verschlüsselt ablegen (etwa Proxmox Backup Server mit Verschlüsselung auf der Client-Seite) oder
`/etc/funk/credentials` davon ausnehmen. Eine Proxmox-Sicherung trägt laufende Aufträge und den Funker-Modus mit; nach dem Zurückspielen
`funk-admin auftraege` ansehen und, was nicht mehr gelten soll, mit `auftrag-entziehen` beenden. Einen Funker-Modus beendet nur das
Konto selbst oder sein Ablauf um 04:00 (B10.7).

### B18.5 Zurückspielen

Etwa nach einem Fehler beim Update oder einer Änderung, die ungeschehen werden soll. Der Funk ist dabei aus.

```
systemctl stop funk-echo funk-server
ls /var/lib/funk/sicherungen/
mkdir -p /root/zustand-vorher
cp -a /var/lib/funk/*.json /root/zustand-vorher/
ls /var/lib/funk/ /var/lib/funk/sicherungen/<zeit>/
```

Steht in `/var/lib/funk` eine `.json`, die in der Sicherung fehlt (etwa `mitglieder.json`, wenn die Sicherung älter ist als
die erste Pflege der Mitglieder), nimmst du sie weg; sonst passt sie nicht zu den zurückgespielten Konten. Ihre Kopie liegt
in `/root/zustand-vorher`. Nur `sperrliste.json` darf bleiben, sie steht nie in einer Sicherung.

```
rm /var/lib/funk/<datei, die in der sicherung fehlt>
cp /var/lib/funk/sicherungen/<zeit>/*.json /var/lib/funk/
chown funk:funk /var/lib/funk/*.json
chmod 0600 /var/lib/funk/*.json
systemctl start funk-server funk-echo
journalctl -u funk-server -n 20 --no-pager
```

`chown` und `chmod` gehören dazu: `cp` als root legt die Dateien root gehörend an, und der Dienst läuft als `funk`. Endet der
Start mit `UnauthorizedAccessException`, fehlt das `chown`. **Das siehst du:** `Funk-Server ... bereit`, und `funk-admin
konten` zeigt den Stand der Sicherung. Danach laufende Aufträge neu vergeben; ein Admin, der "Heute als Funker" gespielt hat,
schaltet es bei Bedarf neu ein.

## B19 Updates

### B19.1 Reihenfolge und Regeln

- **Erst der Server, dann die Clients.** Ein neuer Client kann den Server nach Dingen fragen, die ein älterer nicht kennt.
  Ältere Clients funken mit einem neueren Server weiter (gemessen mit `funk-echo` und der Echo-Probe 0.2.0 und 0.3.0
  gegen den Server 0.4.0 und mit `funk-echo` und der Echo-Probe 0.4.0 gegen den Server 0.5.0, ohne TLS). Phasen und Funker-Modus
  sehen Clients 0.4.x nicht (B19.6); umgekehrt blendet ein Client 0.5.0 an einem älteren Server beide aus.
- Vorher sichern (B18.2), dazu Programme, `server.json` und Units für den Rückweg.
- `/etc/funk/server.json` überschreibt kein Update. `konfig/server.beispiel.json` nie darüber kopieren; dort fehlen deine
  Adresse und deine Ports. Braucht eine Fassung ein neues Feld, steht es in den Hinweisen zum Paket.
- Hat sich eine Unit geändert, sagen es die Hinweise zum Paket; dann die Unit neu kopieren und `systemctl daemon-reload`,
  sonst läuft die alte weiter.
- Clients: Jedes Setup erkennt die vorhandene Installation und bietet **Aktualisieren** an. Admins nehmen immer das
  Admin-Setup derselben Fassung wie der Client.
- `mindestClientVersion` erst heben, wenn alle Mitglieder das neue Setup haben (B6.3).

Die laufende Fassung:

```
grep -m1 -o 'funk-server/[0-9.]*' /opt/funk/funk-server.deps.json
funk-admin status
```

### B19.2 Was sich je Fassung am Server ändert

| Schritt | am Server zu tun | Anleitung |
| --- | --- | --- |
| 0.1.0 auf 0.2.0 | neuer Schlüssel `funk-totp-schluessel` (einmal), Feld `totpSchluessel` in `server.json`, Zeile `LoadCredential=funk-totp-schluessel:...` in der Unit | `HEIMSERVER-PROXMOX.md`, Abschnitt 14 |
| 0.2.0 auf 0.3.0 | Unit mit `LogsDirectory=funk` und `LogsDirectoryMode=0750`; kein Schlüssel, kein Pflichtfeld | Abschnitt 15 |
| 0.2.0 oder 0.3.0 auf 0.4.0 | Unit wie 0.3.0; wahlweise `/etc/funk/echo.env` für den Echo-Funker; kein Schlüssel, kein neues Feld | Abschnitt 16 |
| 0.4.0 auf 0.4.1 | am Server nichts: 0.4.1 ändert nur den Client, ein Serverpaket 0.4.1 gibt es nicht; Admins bekommen das Admin-Setup 0.4.1 (`20261001_1954_JG-TacRelay-Admin-Setup-0.4.1.exe`), Mitglieder das normale Setup 0.4.1 (`20261001_1955_JG-TacRelay-Setup-0.4.1.exe`) | Abschnitt 16, Schritt 10 (Absatz zu 0.4.1) |
| 0.4.0 oder 0.4.1 auf 0.5.0 | Programme tauschen; kein Schlüssel, kein Feld in `server.json`, Unit unverändert bis auf eine Kommentarzeile; der Zustand von 0.4.0 wird verlustfrei geladen | B19.6, `HEIMSERVER-PROXMOX.md` Abschnitt 17 (kommt mit dem Paketbau) |

Rückweg ohne Zurückspielen des Zustands: gemessen auf dem Entwicklungsrechner, ohne TLS. Der Server 0.4.0 lief auf dem
Zustand von 0.2.0, danach liefen die Server 0.3.0 und 0.2.0 auf dem, was 0.4.0 geschrieben hatte, je mit `BESTANDEN` der
Echo-Probe. Was eine ältere Fassung nicht kennt (Aufträge für 0.2.0, Phasen und Funker-Modus für 0.4.0), lässt sie beim nächsten Schreiben weg. Eine
`server.json` mit einem Feld, das die ältere Fassung nicht kennt, lehnt sie ab (`The JSON property ... could not be
mapped`); dafür die alte `server.json` aufheben.

### B19.3 Update von Hand (so am Heimserver)

Auf dem PC im Ordner des neuen Pakets (PowerShell) und auf dem Host; nie den Ordner `funk-offline` vom Stick nehmen:

```
tar -czf funk-linux.tgz -C linux .
scp funk-linux.tgz root@<proxmox>:/root/
```

```
pct push 150 /root/funk-linux.tgz /root/funk-linux.tgz
```

Im Container (`<alt>` ist die laufende Fassung):

```
grep -m1 -o 'funk-server/[0-9.]*' /opt/funk/funk-server.deps.json
funk-admin sicherung
cp -a /opt/funk /root/funk-<alt>
cp /etc/funk/server.json /root/server.json.<alt>
cp /etc/systemd/system/funk-server.service /root/funk-server.service.<alt>
cp /etc/systemd/system/funk-echo.service /root/funk-echo.service.<alt>
systemctl stop funk-echo funk-server
tar -xzf /root/funk-linux.tgz -C /opt/funk
chown -R root:root /opt/funk
chmod -R go-w /opt/funk
grep -m1 -o 'funk-server/[0-9.]*' /opt/funk/funk-server.deps.json
```

`chmod` gehört dazu: Ein tgz aus Windows trägt für jede Datei "für alle beschreibbar", und `tar` übernimmt das als root.
Ohne `chmod` könnte jeder Benutzer im Container die Programme austauschen. Wenn die Hinweise zum Paket eine geänderte Unit
nennen:

```
cp /opt/funk/konfig/funk-server.service /etc/systemd/system/
systemctl daemon-reload
```

Prüfen und starten:

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
cp /opt/funk/konfig/profilschluessel.txt /etc/funk/
funk-admin pruefen --vollstaendig
systemctl start funk-server funk-echo
journalctl -u funk-server -n 30 --no-pager
```

**Das siehst du:** die neue Fassung bei `grep`, `Konfiguration in Ordnung.`, `Ergebnis: 0 Fehler, ...` und im Journal
`Funk-Server <fassung> bereit` mit derselben Anzeigeform des Pins wie vorher. Beim Stoppen bekommen alle Clients
`Bye{WARTUNG}` und kommen nach etwa 10 s von selbst wieder.

**Zurück**, nur wenn der neue Server nicht startet:

```
systemctl stop funk-echo funk-server
mv /opt/funk /root/funk-<neu>
cp -a /root/funk-<alt> /opt/funk
cp /root/server.json.<alt> /etc/funk/server.json
cp /root/funk-server.service.<alt> /etc/systemd/system/funk-server.service
cp /root/funk-echo.service.<alt> /etc/systemd/system/funk-echo.service
systemctl daemon-reload
systemctl start funk-server funk-echo
```

Gibt es `/root/funk-<alt>` schon (ein zweiter Anlauf), die Zeile mit `cp -a` beim Sichern weglassen, sonst landet die Kopie
darin statt daneben.

### B19.4 Update mit dem Skript

Auf einem Server, der mit `funk-einrichten.sh` eingerichtet wurde: das neue Paket wie in B3.2 oder B4.3 holen und im
Ordner `linux` `sudo bash funk-einrichten.sh` aufrufen (B5.7). Vorher `sudo funk-admin sicherung`.

### B19.5 System und .NET

Mit .NET aus den Paketquellen (Weg A), etwa monatlich:

```
apt update && apt full-upgrade -y
systemctl restart funk-server funk-echo
```

Mit dem Installationsskript von Microsoft (Weg B) lädst du die Laufzeit mit demselben Skript neu:
`bash dotnet-install.sh --channel 10.0 --runtime dotnet --install-dir /usr/share/dotnet`, dann neu starten. `dotnet
--list-runtimes` zeigt danach die neue `Microsoft.NETCore.App 10.0.x`. Den Proxmox-Host aktualisierst du wie gewohnt; ein
neuer Kernel braucht einen Neustart an einem Abend ohne Raid.

### B19.6 Auf 0.5.0 (von 0.4.0 und 0.4.1)

Knapp; die ausführliche Schrittfolge für den Heimserver steht in `HEIMSERVER-PROXMOX.md`, Abschnitt 17 ("Auf 0.5.0"). Sie ist mit dem
Paketbau am 02.10.2026 geschrieben worden und liegt dem Serverpaket 0.5.0 bei; setzt dieser Abschnitt etwas anderes voraus als jener,
gilt jener. **Serverpaket und Setups 0.5.0 sind am 02.10.2026 gebaut** (`PROJEKT.md`); die
Dateinamen folgen dem Muster `<zeit>_JG-TacRelay-Server-0.5.0.zip`, `<zeit>_JG-TacRelay-Setup-0.5.0.exe` und
`<zeit>_JG-TacRelay-Admin-Setup-0.5.0.exe`. Der Heimserver läuft laut Auftraggeber (02.10.2026) mit Server 0.4.0 auf Debian 13 im
Container 180; die Beispiele hier und in B19.3 nehmen 150 (`<CT-Nummer>`).

**Was am Server zu tun ist:**

| Punkt | Antwort |
| --- | --- |
| Schlüssel, Geheimnisse | keine neuen |
| `server.json` | unverändert (B6.1); nicht überschreiben |
| Units | unverändert bis auf die erste Kommentarzeile `# JG TacRelay - Maintainer: Sarevoc` (B7.1); eine Unit aus 0.4.0 läuft weiter, neu kopieren ist freiwillig (dann `systemctl daemon-reload`) |
| Zustand | `lage.json` von 0.4.0 wird verlustfrei geladen und beim nächsten Schreiben um Phasen, laufende Phase und `alsFunkerBis` ergänzt |
| Ablauf | wie B19.3 (Handinstallation, so am Heimserver) oder B19.4 (Skript): vorher `funk-admin sicherung`, Programme tauschen, `pruefen`, `systemctl start`; die Clients bekommen `Bye{WARTUNG}` und kommen von selbst wieder |
| Das siehst du | im Journal `Funk-Server 0.5.0 bereit: ...` mit demselben Pin wie vorher |
| Neu für die Konsole | `netz-setzen --taste` (B12.1); sonst keine neuen Befehle, auch keine für Phasen oder Funker-Modus (B9.8) |
| Stick des Eigentümers | kein Muss: `profil-erneuern` ist unverändert; die Programme dort wie beim nächsten Profilwechsel aufrischen (B8.4) |
| `mindestClientVersion` | bleibt; Clients 0.4.x dürfen bleiben |

**Reihenfolge:** erst der Server, dann die Clients (B19.1). Mit dem Setup 0.5.0 bekommen die Mitglieder die Phasenleiste, die Ablage
und die Meldetaste (Teil A); die Admins nehmen das Admin-Setup 0.5.0.

**Was die Clients sehen:**

- Clients 0.4.0 und 0.4.1 funken mit dem Server 0.5.0 weiter und sehen Phasen und Funker-Modus nicht; die neuen Felder und der
  Push `0x4004` liegen im optionalen Bereich (Protokoll 4.7). Gemessen mit `funk-echo` 0.4.0 am Server 0.5.0 (Standzeile
  01.10.2026 20:15), nicht mit einem echten Client 0.4.0: Wie ein solcher Client `Mitgliedschaften.phase` aufnimmt, ist nicht
  gemessen.
- Ein Client 0.5.0 an einem Server 0.4.x blendet Phasen und Funker-Modus aus (er schickt `0x003C` und `0x003D` nur an Server ab
  0.5.0, `Versionen.ServerFassungFuer`); der Verwaltungsteil zeigt den Reiter **Phasen** dort nicht.
- Die Einstellungsdatei des Clients: neue Tasten stehen in `tasten.weitere`, der Funk-Klick unter `klick`; Clients 0.4.0 und 0.4.1
  lesen eine Datei von 0.5.0 ohne Fehler (gemessen mit den gebauten Clients, Standzeile 02.10.2026 00:10).

**Netze, die schon da sind:** Gespeicherte Netze behalten ihre Voreinstellung (B12.1). Die Voreinstellung Sprachaktivierung gilt
nur für Netze, die ab 0.5.0 mit `netz-setzen` ohne Schalter entstehen.

**Gemessen** auf dem Entwicklungsrechner, ohne TLS, mit den Programmen des Gesamtpakets 0.4.0 (`20261001_1539`) gegen den Debug-Bau
0.5.0 (Standzeile 01.10.2026 20:15): Der Server 0.5.0 startete auf einem Zustand, den die Programme 0.4.0 selbst geschrieben
hatten. Mit einer laufenden Phase 2 blieb `funk-echo` 0.4.0 über die Netzliste mit Phasen und über den Rückfall auf Phase 1
verbunden; die Echo-Probe 0.4.0 und die Echo-Probe 0.5.0 meldeten `BESTANDEN`. Am Heimserver und mit echten Clients ist das nicht
gelaufen.

**Rückweg auf 0.4.0:** wie "Zurück" in B19.3 (alte Programme, `server.json` und Units zurück). Der Server 0.4.0 startet auf dem
Zustand von 0.5.0 (gemessen, die Echo-Probe 0.4.0 `BESTANDEN`) und liest ihn, übergeht aber, was er nicht kennt. **Nach dem ersten
Schreiben fehlen die Phasen, die laufende Phase und `alsFunkerBis`**. Wer die Phasen später wieder braucht, sichert vor dem Rückweg
(`funk-admin sicherung` auf 0.5.0 nimmt sie mit, B18.2) und spielt sie nach einem erneuten Update zurück (B18.5).

## B20 Umzug

### B20.1 Der Weg

Der Funk zieht von einem Heimserver (`heim.example.org`, TCP 8443) auf einen Server bei einem künftigen Betreiber, ohne neue
Setups und ohne dass Konten, Geräte, Netze, Mitglieder, Admins und der Eigentümer verloren gehen (`UMZUG.md`, Variante
A). Der neue Server bekommt einen eigenen Schlüssel; kein privater Serverschlüssel reist. Die Clients lernen den neuen
Server über ein Übergangsprofil kennen, das ihnen der alte Server reicht. Geplant ist der Umzug erst nach Version 1 und
einem Test mit ein paar Leuten (Anf. A55).

**Was gelten muss:**

| Bedingung | Warum |
| --- | --- |
| alle aktiven Clients mindestens 0.4.0 | erst 0.4.0 übernimmt ein neueres Profil vom Server; 0.2.0 und 0.3.0 fänden den neuen Server nie |
| alter Server mit 0.4.0 | `umzug-packen` und `geraete --nicht-seit` gibt es erst ab 0.4.0; ab 0.4.0 warnt `profil-neu-laden` bei fehlendem Pin oder anderem Port |
| auf dem Stick des Eigentümers die Programme 0.4.0 | `profil-erneuern` gibt es erst ab 0.4.0 (B8.4) |
| derselbe TCP-Port auf beiden Servern (8443) | ein Profil kennt nur einen TCP-Port, und im Übergang stehen beide Server darin |
| nur Sarevoc unterschreibt Profile | ein anders unterschriebenes nimmt weder Client noch `funk-einrichten.sh` an |
| Kennung, Name und Port gehen übers Telefon | die Kennung deckt nur den Pin |
| Stick mit BitLocker To Go | `profil.key` und `reserve.key` liegen dort als Dateien |

### B20.2 Ablauf

| Schritt | Wer, wo | Was | Das siehst du |
| --- | --- | --- | --- |
| **Vorlauf, ein bis zwei Wochen vorher** | | | |
| 1 | Sarevoc, alter Server | alle Mitglieder bekommen das Setup 0.4.0 oder neuer (Admins das Admin-Setup); dann `mindestClientVersion` auf `"0.4.0"`, `pruefen && systemctl restart funk-server` | `Konfiguration in Ordnung.`, `Funk-Server <fassung> bereit` (mindestens 0.4.0); ältere Clients bekommen `VERSION_ZU_ALT` |
| 2 | neuer Betreiber, neuer Server | DNS `funk.example.org` auf seine Adresse (TTL höchstens 300 s), `sudo bash funk-einrichten.sh` mit **TCP 8443**; Anfragezeile an Sarevoc, Kennung, Name und Port am Telefon | `WARTET AUF PROFIL` |
| 3 | Sarevoc, Stick | Doppelklick `Profil erneuern.cmd`, die Zeile des neuen Betreibers einfügen, Kennung, Name und Port vergleichen, `j` | `Urteil: Uebernehmen`, `Kopie zum Verschicken: ...\zum-verschicken\profil.json`; Version 2 mit beiden Hosts (erst der Heimserver, dann der neue) und drei Pins; `profil-v1.json` daneben |
| 4 | Sarevoc, alter Server, nicht am Spielabend | Version 2 nach `/etc/funk/profil.json`; `funk-admin pruefen --vollstaendig`; nur ohne `FEHLER` beim Pin und ohne Port-Warnung `funk-admin profil-neu-laden`, dann `systemctl restart funk-server` | `OK Pin des Serverschlüssels ... steht im Profil.`, `Profil Version 2 geladen.` ohne `WARNUNG`; im Journal `Serverprofil Version 2 mit 3 Pins geladen` |
| 5 | neuer Betreiber, neuer Server | `sudo bash funk-einrichten.sh --profil profil.json`; von außen `Test-NetConnection funk.example.org -Port 8443`; danach **wieder anhalten**: `sudo systemctl disable --now funk-server` | `TcpTestSucceeded : True` |
| 6 | Sarevoc, alter Server | warten, bis jedes aktive Gerät seit Schritt 4 einmal verbunden war: `funk-admin geraete --nicht-seit <tag nach schritt 4>` | wer dort steht, verbindet sich einmal; sonst braucht er nach dem Umzug ein neues Setup |
| **Umzugsabend, der Funk ist etwa eine Viertelstunde aus** | | | |
| 7 | Sarevoc, alter Server | `funk-admin sicherung`, `systemctl disable --now funk-echo funk-server`, `funk-admin umzug-packen --aus /root/umzug.tru`; Paket über den Host holen (`pct pull 150 /root/umzug.tru /root/umzug.tru`) und auf jedem Weg an den neuen Betreiber; die Passphrase nur am Telefon | die Passphrase genau einmal, 26 Zeichen in Vierergruppen |
| 8 | neuer Betreiber, neuer Server | Paket in den Ordner `linux`, `sudo bash funk-einrichten.sh --umzug umzug.tru`, Passphrase eingeben | `Eingespielt nach /var/lib/funk: lage.json, konten.json, ...`, `Eingespielt nach /etc/funk/credentials: funk-totp-schluessel.`, `bereit` |
| 9 | neuer Betreiber und Sarevoc | `sudo funk-admin konten` (Sarevoc als `ADMIN` und `EIGENTÜMER`), `geraete`, `admins`; Sarevoc verbindet sich | die Clients finden nach `Bye{WARTUNG}` den neuen Host |
| **Nachlauf, zwei bis vier Wochen später** | | | |
| 10 | Sarevoc, Stick | `Profil erneuern.cmd`, die Zeile des neuen Betreibers noch einmal (oder Eingabetaste für `profilanfrage.txt`), `Den alten Server herausnehmen?` mit `j`, Kennung vergleichen | Version 3 mit nur `funk.example.org`, dem Pin des neuen Servers und der Reserve |
| 11 | neuer Betreiber | `sudo bash funk-einrichten.sh --profil profil.json`; der laufende Server lädt es ohne Neustart | die Clients übernehmen es beim nächsten Verbinden; neue Setups mit Version 3 |
| 12 | Sarevoc | erst jetzt aufräumen: altes LXC löschen (`pct destroy`), den alten Serverschlüssel auf dem Stick und in vzdump-Archiven vernichten; Setups mit Version 1 oder 2 nicht mehr weitergeben | |

Die Befehle von Schritt 1, 4 und 7 auf dem von Hand eingerichteten Heimserver ausgeschrieben:

```
nano /etc/funk/server.json
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen && systemctl restart funk-server
```

```
dotnet /opt/funk/funk-admin.dll pruefen --vollstaendig --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll profil-neu-laden --konfig /etc/funk/server.json
systemctl restart funk-server
```

```
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
systemctl disable --now funk-echo funk-server
dotnet /opt/funk/funk-admin.dll umzug-packen --aus /root/umzug.tru --konfig /etc/funk/server.json
```

**Schritt 4, falsches Profil:** Auf den alten Server gehört nur ein Profil mit seinem eigenen Pin und seinem Port (Version
2, nie Version 3 aus Schritt 10). Zeigt `profil-neu-laden` darunter `WARNUNG Der Pin des Serverschlüssels ... steht nicht
im Serverprofil Version 3: ...` oder `WARNUNG Das Serverprofil nennt TCP-Port 443, ...`: nicht neu starten, sondern die
vorige `profil-v<N>.json` vom Stick zurück nach `/etc/funk/profil.json` und noch einmal `profil-neu-laden`.

**Schritt 6, der Tag:** `geraete --nicht-seit` zählt den ganzen Tag ab 0 Uhr UTC, auch das Ende einer Sitzung. Deshalb der
Tag nach Schritt 4 (Version 2 am 5. Oktober geladen, dann `2026-10-06`). Wer nur am Abend von Schritt 4 nach dem Neustart
verbunden war, steht trotzdem in der Liste; einmal verbinden schadet nicht.

### B20.3 Das Umzugspaket

`umzug-packen` und `umzug-einspielen` laufen nur bei angehaltenem Dienst (`Der Dienst läuft noch (der Verwaltungssocket ...
antwortet). Erst anhalten: systemctl stop funk-server. Nichts geändert.`).

| Was | Wert |
| --- | --- |
| darin | `lage.json` (mit laufenden Aufträgen, ab 0.5.0 auch mit Phasen, laufender Phase und Funker-Modus), `konten.json`, `komfort.json`, `admins.json`, `mitglieder.json`, `journal.json`, soweit vorhanden, und `funk-totp-schluessel` |
| wahlweise | `--ohne-totp`: ohne TOTP-Schlüssel, dann richtet jeder Admin den Zweitfaktor neu ein; `--mit-serverschluessel`: `server.key` und `server.crt` (nur für den Notweg) |
| nie darin | `funk-geheimnis`, `vorrangspur.json`, `sperrliste.json`; der neue Server hat eigene |
| Format | innen ein tar mit `MANIFEST.json` (Name, Größe, SHA-256 je Datei); außen Kennung `TRUMZUG1`, AES-256-GCM, Kopf als zusätzliche authentifizierte Daten |
| Schlüssel | PBKDF2-SHA256 mit 600.000 Runden, Salz 16 Byte, aus einer Passphrase mit 26 Zeichen Base32 (130 Bit), die das Werkzeug erzeugt und genau einmal zeigt |
| Größe | höchstens 256 MiB |
| ersetzen | `umzug-packen` schreibt nie über ein vorhandenes Paket (Exit 3) |

`umzug-einspielen` (oder `funk-einrichten.sh --umzug`) fragt die Passphrase (Bindestriche und Leerzeichen egal, drei
Versuche), legt alle vorhandenen Zustandsdateien und die ersetzten Schlüssel mit der Endung `.vor-umzug-<JJJJMMTT-HHmmss>`
zur Seite, schreibt mit 0600, gibt die Zustandsdateien dem Benutzer `funk` (`--besitzer`), die Schlüssel bleiben bei
root. Bei falscher Passphrase bleibt alles, wie es war. Ein Paket mit Serverschlüssel spielt es nur mit
`--mit-serverschluessel` ein. Die Passphrase steht beim Eintippen sichtbar im Terminal (offener Punkt); niemand darf
zuschauen.

### B20.4 Rückweg bis Schritt 10

Der alte Heimserver ist angehalten, aber unverändert. Der neue Betreiber hält seinen an, Sarevoc startet den alten wieder:

```
sudo systemctl disable --now funk-server
```

```
systemctl enable --now funk-server funk-echo
```

Die erste Zeile beim neuen Betreiber, die zweite im alten Container. Die Clients finden den alten Server über `heim.example.org` mit
dem alten Pin; beides steht in Version 2. Was auf dem neuen Server inzwischen geändert wurde, holt `umzug-packen` dort
zurück, eingespielt auf dem Heimserver mit angehaltenem Dienst:

```
dotnet /opt/funk/funk-admin.dll umzug-einspielen --paket /root/zurueck.tru --konfig /etc/funk/server.json
```

### B20.5 Notweg ohne Profilübernahme

Bleiben Clients unter 0.4.0, gibt es nur Variante B oder C aus dem Entwurf: Der neue Server zeigt einen Pin aus Version 1,
entweder den alten Serverschlüssel des Heimservers oder den Reserveschlüssel, und der Eigentümer stellt `heim.example.org`
im DNS auf die Adresse des neuen Servers. Mit dem alten Schlüssel heißt das auf dem alten Server `umzug-packen --mit-serverschluessel`, auf
dem neuen `sudo bash funk-einrichten.sh --umzug <paket> --mit-serverschluessel`. Die Mitglieder merken nichts, aber ein
privater Schlüssel reist, und alte Kopien (Stick, vzdump) müssen danach weg. Mit dem Reserveschlüssel ist die Reserve
verbraucht.

### B20.6 Warnungen

- Der alte Server läuft nach dem Umzugsabend nie wieder mit (`disable` in Schritt 7); er steht in Version 2 als erster
  Host.
- Der neue Server läuft im Vorlauf nicht leer mit (Schritt 5): Die Clients versuchen die Hosts der Reihe nach, der
  Heimserver hat 250 ms Vorsprung; antwortet er einmal nicht, landet ein Client auf dem leeren Server und stellt dort einen
  Antrag. Läuft er doch einmal leer mit: dort nichts freigeben; der Umzug legt diesen Stand zur Seite.
- Unter `funk.example.org` und `heim.example.org` darf kein anderer Dienst auf Port 8443 antworten.
- Eine gefälschte Anfragezeile, die der Eigentümer unterschreibt, schickt alle Clients zu einem fremden Server. Nie ohne den
  Vergleich von Kennung, Name und Port am Telefon `j` sagen.
- Konsole heißt Eigentümer: Wer auf dem neuen Server root ist, darf am Verwaltungssocket alles, auch den Eigentümer neu setzen.
  Eigentümer im Sinne der Profile bleibt, wer den Profilschlüssel hält.

## B21 Fehlersuche

### B21.1 Erste Hilfe

Diese Befehle ändern nichts und zeigen fast immer, wo es hakt:

```
systemctl status funk-server
journalctl -u funk-server -n 50 --no-pager
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
funk-admin pruefen --vollstaendig
ss -tulpen
funk-admin status
funk-admin sperrliste
```

Im Proxmox-Container zusätzlich auf dem Host: `pct config <CT-Nummer>` (nesting) und `chronyc tracking` (Uhr). Die Ausgaben
enthalten keine Geheimnisse; nur `admin-einrichtung-neu --anzeigen`, `zweitfaktor-bestaetigen`, `notfallcodes` und
`umzug-packen` zeigen welche.

### B21.2 Start und systemd

| Meldung | Ursache | Abhilfe |
| --- | --- | --- |
| `status=243/CREDENTIALS` (Journal oder `systemctl status`) | systemd findet eine Datei nicht, die die Unit mit `LoadCredential` lädt, meist `funk-totp-schluessel`; der Dienst startet gar nicht. Im unprivilegierten LXC ohne `nesting=1` fallen unter Debian 13 laut Proxmox-Forum auch Dienste mit 243 statt 226 aus (nicht gemessen) | `ls -l /etc/funk/credentials` muss vier Dateien zeigen (`-rw-------`). Fehlt eine: `sudo bash funk-einrichten.sh` legt fehlende an, von Hand `funk-admin totp-schluessel-erzeugen --datei /etc/funk/credentials/funk-totp-schluessel` (und die anderen Erzeugen-Befehle). Liegen alle da: `systemctl cat funk-server \| grep ^LoadCredential` mit den Dateien vergleichen, im Container auf dem Host `pct config <id>`, dann `pct set <id> --features nesting=1` und `pct reboot <id>` |
| `status=226/NAMESPACE` | dem Container fehlt `nesting=1`; die Härtung der Unit darf keine Namensräume anlegen | auf dem Host `pct set <id> --features nesting=1`, Container neu starten |
| `Start gescheitert: InvalidDataException: funk-totp-schluessel fehlt (/run/credentials/funk-server.service/...)` | die Unit ist noch eine alte ohne `LoadCredential=funk-totp-schluessel` | Unit neu kopieren (`cp /opt/funk/konfig/funk-server.service /etc/systemd/system/`), `systemctl daemon-reload`, starten |
| `Start gescheitert: ... UnauthorizedAccessException ...` nach dem Zurückspielen | Zustandsdateien gehören root | `chown funk:funk /var/lib/funk/*.json`, `chmod 0600 /var/lib/funk/*.json` |
| `start request repeated too quickly` | systemd hat nach mehreren Fehlstarts aufgegeben | Ursache beheben, dann `systemctl reset-failed funk-server` und `systemctl start funk-server` |
| `funk-server meldet nach 30 s nicht "bereit".` (einrichten) | der Start hängt | `journalctl -u funk-server -n 50 --no-pager` |
| `ss zeigt TCP ... oder UDP ... nicht.` (einrichten) | die Adresse in `binden` gehört nicht zu diesem Rechner | `ss -tulpen`, `ip -4 addr`; `binden` berichtigen |
| `You must install or update .NET` | die Laufzeit fehlt | `dotnet --list-runtimes` muss `Microsoft.NETCore.App 10.0.x` zeigen; installieren wie in B5.2, Punkt 3 |
| `/usr/bin/dotnet gibt es schon, aber ohne Laufzeit 10` (Skript) | ein .NET, das womöglich einem anderen Dienst gehört | die Laufzeit 10 auf demselben Weg dazulegen, auf dem die vorhandene kam, dann noch einmal aufrufen |

### B21.3 Konfiguration

| Meldung | Ursache | Abhilfe |
| --- | --- | --- |
| `Konfiguration ungültig:` und darunter die Gründe (`pruefen` und Start, Exit 2) | eine Regel aus B6.2 ist verletzt, etwa `Keine Bindadresse: ...`, `profil ist Pflicht ...`, `tcpPort: ... liegt nicht in 1-65535.`, `log.stufe: DEBUG, INFO, WARNUNG oder FEHLER.`, `Serverprofil: Profildatei fehlt: ...` | `/etc/funk/server.json` berichtigen; `pruefen && systemctl restart funk-server`. `funk-admin pruefen --vollstaendig` nennt jeden Befund. `einrichten` meldet dasselbe als `... erfüllt die Regeln des Servers nicht: ...` oder `Der Server lehnt die Konfiguration ab: ...` |
| `Konfiguration ungültig:` mit `totpSchluessel fehlt (neu seit 0.2.0, Zweitfaktor der Admins, 3.9). ...` | eine `server.json` aus 0.1.0 | die drei Schritte aus der Meldung: Schlüssel erzeugen, Zeile `"totpSchluessel": "${CREDENTIALS_DIRECTORY}/funk-totp-schluessel",` eintragen, Unit mit der `LoadCredential`-Zeile (B19.2) |
| `Konfiguration nicht lesbar: Konfiguration ist kein gültiges JSON: ...` | Komma oder Anführungszeichen fehlt oder ist zu viel (auch nach dem letzten Eintrag) | die Zeile aus `LineNumber` (ab 0 gezählt) ansehen |
| `The JSON property '<name>' could not be mapped ...` | vertippter Feldname, oder die Fassung kennt das Feld nicht (etwa `sperrliste` für 0.2.0) | den Namen in der Datei suchen; für eine ältere Fassung die alte `server.json` nehmen |
| `... gibt es schon. konfig-schreiben schreibt nie über eine vorhandene server.json ...` (Exit 3) | Absicht | vorhandene ansehen (`pruefen`) oder zur Seite legen |

### B21.4 Profil und Pins

| Meldung | Ursache | Abhilfe |
| --- | --- | --- |
| Pin-Warnung: `WARNUNG Der Pin des Serverschlüssels (XXXX-XXXX-XXXX-XXXX) steht nicht im Serverprofil Version N: Clients mit diesem Profil lehnen den Server ab und übernehmen es nicht (3.1). ...` (Start, `profil-neu-laden`); `FEHLER Pin des Serverschlüssels ... steht nicht im Profil` (`pruefen --vollstaendig`); `Der Pin dieses Servers (Kennung ...) steht nicht im Profil Version N.` (einrichten) | `profil.json` passt nicht zum Serverschlüssel: ein neuer Schlüssel wurde erzeugt, oder ein falsches Profil liegt da (etwa Version 3 auf dem alten Server beim Umzug) | Hat es vorher gepasst: die vorige `profil-v<N>.json` zurück nach `/etc/funk/profil.json`, `funk-admin profil-neu-laden`. Sonst `funk-admin profil-anfrage`, Zeile an Sarevoc, Telefon, neues Profil einspielen (B8.3). Geladen ist das falsche Profil trotzdem; Clients ab 0.4.0 übernehmen es nicht |
| Port-Warnung: `WARNUNG Das Serverprofil nennt TCP-Port P, server.json aber oeffentlicherTcpPort O. Die Clients verbinden sich auf Port P aus dem Profil. ...` (Start, `pruefen`, `profil-neu-laden`); `Das Profil nennt TCP-Port P, /etc/funk/server.json aber O ...` (einrichten) | Profil und `server.json` nennen verschiedene öffentliche TCP-Ports, etwa die 443 aus dem Beispiel bei einem Profil mit 8443 (so am Heimserver geschehen) | Leitet der Router P weiter (oder bindet der Server P): `"oeffentlicherTcpPort": P` in `server.json`, `pruefen && systemctl restart funk-server`. Sonst ein Profil mit dem richtigen Port (bei einem Umzug geht nur derselbe Port) |
| `Das Profil hat Version N, eingespielt ist schon Version M.` | ein älteres Profil | das neueste `profil.json` vom Eigentümer nehmen |
| `Das Profil ist nicht mit dem Profilschlüssel des Eigentümers unterschrieben (...)` | fremd signiert oder falsche Datei | `zum-verschicken\profil.json` vom Eigentümer nehmen, nicht `daten.json` |
| `Der Name ... steht nicht im Profil (...)` | Name aus dem ersten Lauf und Profil passen nicht | Sarevoc die Anfragezeile mit diesem Namen schicken oder den Namen in `/etc/funk/profilanfrage.txt` berichtigen |
| `Das Profil ist am ... abgelaufen; die Clients verbinden weiter, warnen aber.` | `gueltigBis` vorbei | Sarevoc verlängert mit `profil-erneuern` (`v`), dann einspielen |
| `Unbekannter Befehl: profil-erneuern. funk-admin hilfe zeigt alle.` (Stick) | `funk-admin.dll` auf dem Stick älter als 0.4.0 | den ganzen Inhalt von `linux` aus dem Serverpaket 0.4.0 nach `funk-offline` (B8.4) |
| `TCP-Port 8443 -> 443: Die Anfrage nennt einen anderen Port als das bisherige Profil, ... Nichts geändert.` | beim Umzug hat der neue Server einen anderen Port | der neue Betreiber legt `/etc/funk/server.json` zur Seite, ruft `funk-einrichten.sh` noch einmal auf, antwortet mit 8443 und schickt die neue Zeile |
| Client: `Server nicht vertrauenswürdig: ...` | der Serverschlüssel passt nicht zu den Pins im Profil des Clients | wie bei der Pin-Warnung; braucht der Client ein neues Profil und kann es nicht übernehmen, ein neues Setup |

### B21.5 Uhr

| Meldung | Ursache | Abhilfe |
| --- | --- | --- |
| `WARNUNG Die Uhr des Servers steht auf ..., vor dem Erscheinen dieser Fassung: offensichtlich falsch. ...` (Start) | Systemuhr falsch | Zeitsynchronisierung einrichten (unten) |
| `WARNUNG Die Uhr des Servers geht nach: Sie steht auf ..., der jüngste Journaleintrag ist n min jünger. ...` (Start) | die Uhr ging einmal vor oder geht jetzt nach (mehr als 5 min) | wie oben |
| `Uhr: vom Kernel als nicht synchronisiert gemeldet (siehe Warnung).` und `WARNUNG: Der Kernel meldet die Uhr als nicht synchronisiert (adjtimex TIME_ERROR): ...` (`funk-server pruefen`) | kein Zeitdienst führt die Uhr | im Container auf dem Host: `chronyc tracking` (`Leap status` muss `Normal` sein) oder `timedatectl` (`System clock synchronized: yes`) |
| `Uhr: Synchronisierung hier nicht lesbar (anderes System oder ProtectClock); ...` | der Container lässt das Lesen nicht zu | auf dem Host `chronyc tracking` |
| `WARNUNG Uhr nicht synchronisiert: ...` (`funk-admin pruefen --vollstaendig`), `FEHLT Die Uhr ist nicht synchronisiert; der Zweitfaktor (TOTP) verträgt nur 30 s Abweichung.` (einrichten) | `timedatectl` meldet `NTPSynchronized=no` | `apt install systemd-timesyncd` (oder chrony), dann `timedatectl set-ntp true`; im Container gilt die Uhr des Hosts |
| `HINWEIS Uhrzeit hier nicht prüfbar (kein timedatectl, etwa im Container) ...` | im Container nicht prüfbar | auf dem Host `chronyc tracking` oder `timedatectl` |
| Client: `Der Code stimmt nicht oder war schon benutzt. Nimm den nächsten, den die App zeigt, und prüf die Uhr des Telefons.` | Code zu alt, schon benutzt, oder Uhr von Telefon oder Server falsch | den nächsten Code nehmen; auf dem Telefon die Uhrzeit auf automatisch; Server wie oben |
| Aufträge um eine oder zwei Stunden verschoben | die Zeitzone fehlt | `ls /usr/share/zoneinfo/Europe/Berlin`; fehlt sie, `apt install tzdata` |

### B21.6 Netz und Verbindung

| Meldung | Ursache | Abhilfe |
| --- | --- | --- |
| Client: `Server nicht erreichbar (...)` | DNS, Portfreigabe oder Firewall; der Client versucht es von selbst weiter | `nslookup <name>`, von außen `Test-NetConnection <name> -Port <tcp-port>`; Firewall und Freigaben (B3.4, B3.5, B4.5) |
| Client: `Verbunden (Tunnel)` | UDP kommt nicht durch, die Sprache läuft im Steuerkanal | UDP-Freigabe und Firewall für 47443 prüfen |
| Client: `VERSION_ZU_ALT` | `mindestClientVersion` ist höher als die Fassung des Clients | das aktuelle Setup |
| Client 0.5.0 zeigt keinen Reiter **Phasen**, keine Phasenleiste und kein "Heute als Funker" | der Server ist älter als 0.5.0 (der Client blendet beides dort aus) | `funk-admin status` zeigt die Fassung; Server auf 0.5.0 bringen (B19.6) |
| Phase schalten antwortet mit `VERALTET` | ein zweiter Leiter hat schon geschaltet; die Antwort nennt, wer | der Client zeigt es in der Meldezeile; erneut schalten, wenn gewollt |
| Phase lässt sich nicht schalten (`KEIN_RECHT`) | das Konto hat kein `ZUWEISEN` (Führung ohne Auftrag, abgelaufener Auftrag) oder ist ein ADMIN im Funker-Modus | Auftrag vergeben (B15) oder "Wieder Admin" im Client (B10.7) |
| Phase schalten antwortet mit `GRENZE`, die Verbindung bleibt | mehr als 1 Wechsel je 2 s, Stoß 2 | kurz warten; nur eine Flut von mehr als 50 Verstößen in 60 s trennt |
| Port belegt (einrichten fragt noch einmal, `pruefen --vollstaendig`: `FEHLER TCP ... ist belegt: Ein anderes Programm hört dort ...`) | ein anderer Dienst | `ss -ltnp` bzw. `ss -lunp` zeigt welcher; anderen Port nehmen, den fremden Dienst nicht anfassen |
| `FEHLER ...: keine Berechtigung. Ports unter 1024 braucht der Dienst mit CAP_NET_BIND_SERVICE, oder eine Weiterleitung auf 8443.` | Port unter 1024 ohne Drop-in | Drop-in aus B4.4 |
| Warnung `Vorfilter voll` | mehr als 65.536 fremde Präfixe schicken Datagramme mit gültiger Sitzungs-ID, meist gefälschte Quelladressen | nichts; neue Präfixe gehen ohne Vorfilter weiter, die Budgets je Sitzung begrenzen die Arbeit |
| neue Registrierungen bekommen `SERVER_VOLL` | Überlast Stufe 2 (Verarbeitung im Median über 10 ms); freigegebene Geräte kommen weiter herein (bis 100 Sitzungen) | `funk-admin messwerte`; Last senken |
| ein Mitglied kommt nicht herein, andere schon | seine Adresse ist gesperrt (Sperrliste, B16.2) | `funk-admin sperrliste`, `funk-admin entsperren --ip <adresse>`; mit fail2ban dazu `fail2ban-client unban <adresse>` auf dem Host |

### B21.7 Werkzeug, Socket, Admins

| Meldung | Ursache | Abhilfe |
| --- | --- | --- |
| `funk-admin` endet mit Exit 1 und einer Meldung des Systems, dass keine Verbindung zustande kam | der Dienst läuft nicht, oder `--konfig` fehlt bzw. nennt eine andere `server.json` (Socketpfad) | `systemctl is-active funk-server`; mit dem Kurzbefehl oder mit `--konfig /etc/funk/server.json` aufrufen |
| `--socket oder --konfig angeben (oder FUNK_KONFIG setzen).` | ohne Kurzbefehl keine Konfiguration genannt | `--konfig /etc/funk/server.json` |
| Logbuch: `Verwaltungssocket: Gegenseite mit nicht zugelassener UID abgewiesen` | Aufruf als anderer Benutzer | als root (`sudo`) oder `funk` aufrufen |
| `... kennt --... nicht. Erlaubt: ...` und die anderen Meldungen aus B9.1 (Exit 2) | Aufruf falsch, nichts geschehen | Befehl berichtigen |
| `<pfad> gibt es schon. Nichts geändert. Mit --ersetzen ...` (Exit 3) | Absicht: Schlüssel werden nicht still ersetzt | nur mit `--ersetzen`, wenn du die Folgen willst (B9.2) |
| `... gibt es schon, stammt aber nicht von JG TacRelay; einrichten lässt die Datei, wie sie ist.` | fremder Kurzbefehl oder fremdes Drop-in | selbst entscheiden; bis dahin die Befehle ausgeschrieben |
| `Kennung passt nicht zum Antrag ...`, `kein offener Antrag: ...`, `schon drei Admin-Schlüssel eingetragen ...` | beim Ernennen | B10.3 |
| `Eigentümer kann nur ein ADMIN-Konto sein` | `eigentuemer` auf einem Konto unter ADMIN | erst `stufe --konto <id> --stufe ADMIN` |
| `Das Konto ist Eigentümer und lässt sich nicht löschen. ...` | Schutz des Eigentümers | erst ein anderes ADMIN-Konto mit `eigentuemer` zum Eigentümer machen |
| `WARNUNG Kein Eigentümer gesetzt: ...` (Start) | frischer Server oder Antrag ins falsche Konto | B10.2 oder B11.5 |
| `WARNUNG verwaltungsUids ist nicht leer: ...` | weitere UIDs haben am Socket alle Rechte | nur lassen, wenn gewollt |
| `WARNUNG Konto k#... trägt einen Discord-Namen, den schon ein anderes Konto trägt: ...` | Doppelte aus einem älteren Stand | `funk-admin konten` zeigt die Namen; `discordname` ändern |
| Client: `Verwaltungsteil passt nicht zu dieser Fassung - das Admin-Setup dieser Fassung ausführen.` | Admin-Setup und Client verschiedener Fassung | das Admin-Setup derselben Fassung |
| Client: `Auf diesem PC lässt sich kein Admin-Schlüssel im TPM anlegen. ...` | kein nutzbares TPM 2.0 | Verwaltung auf einem PC mit TPM 2.0; Anträge bis dahin an der Konsole |
| Zweitfaktor pausiert oder gesperrt | 5 Fehlversuche in 15 min oder 10 in 24 h | `funk-admin admins` zeigt es; `admin-entsperren --konto <id>` nach Rücksprache |

### B21.8 Sperrliste und Echo-Funker

| Meldung | Ursache | Abhilfe |
| --- | --- | --- |
| `Sperrliste: Klartextdatei nicht schreibbar` (Start) | `sperrliste.klartextDatei` gesetzt, aber `LogsDirectory=funk` fehlt in der Unit | Unit aus dem Paket kopieren, `systemctl daemon-reload`, neu starten |
| `Sperrliste ausgeschaltet (sperrliste.aktiv = false): ...` | `aktiv` auf `false` | nur zur Fehlersuche; wieder auf `true` |
| fail2ban startet nicht, `Have not found any log file for <jail> jail` | Container läuft nicht, Pfad vertippt, oder die Klartextdatei fehlt | Container starten, Pfad prüfen, Klartextdatei einschalten (B16.3) |
| Echo-Funker endet mit Code 3, im Journal `Verdrängt durch <gerät> (<kennung>): ...` | ein anderes Gerät desselben Kontos (etwa die Echo-Probe mit derselben Discord-Zahl) | das fremde Gerät entfernen, `systemctl restart funk-echo`; für die Probe immer eine eigene Zahl |
| Echo-Funker endet mit Code 4 | abgewiesen: Konto oder Gerät gesperrt, Verdrängen gesperrt, falscher Pin | die Zeile davor im Journal von `funk-echo` nennt den Grund |
| Echo-Funker erreicht den Server nicht | falsche Adresse in `/etc/funk/echo.env` (oder die Datei fehlt und die Voreinstellung der Unit gilt) | `cat /etc/funk/echo.env`; die Adresse aus `binden` eintragen, `systemctl restart funk-echo` |
| `echo-probe` meldet `NICHT BESTANDEN`, kein Echo | die Probe hat den Echo-Funker verdrängt (gleiche Discord-Zahl, eine Sitzung je Konto) | Gerät der Probe entfernen, `systemctl restart funk-echo`, Probe mit eigener Zahl |

## B22 Rechte, Lizenz und Datenschutz im Betrieb

### B22.1 Rechteinhaber und Lizenz

- Maintainer und Rechteinhaber ist Sarevoc; das Programm gehört ihm, alle Rechte liegen bei ihm (Anf. A41, A59).
- Eine Genehmigung oder Meldung braucht der Betrieb nach bisheriger Einschätzung nicht, solange nur freigegebene
  Mitglieder hineinkommen; die DSGVO ist trotzdem zu beachten (A13.3, F11.9, kein Rechtsrat).
- Die Lizenz des eigenen Codes ist **noch nicht entschieden** (Anf. A58). Die Wege und was sie für einen Betreiber
  heißen, beschreibt F8; bis zur Entscheidung klärt, wer den Server betreibt, ändert oder weitergibt, das mit
  Sarevoc.
- Fremdbestandteile im Serverpaket: Die vier Serverprogramme enthalten nur eigenen Code; die .NET-Laufzeit kommt von
  Microsoft (apt oder Installationsskript) und liegt nicht im Paket. `THIRD-PARTY-NOTICES.txt` und der Ordner `lizenzen`
  im Paket gelten für den ganzen Produktstand, auch für Client und Setup (F7).

### B22.2 Welche Daten der Server hält

Welche personenbezogenen Daten in welcher Datei liegen, wie lange und wer sie sieht, steht vollständig in F11.2 und F11.3,
aus Sicht der Sicherheit in C15. Für den Betrieb wichtig:

- Die Namen der Mitglieder stehen in `konten.json` und `mitglieder.json`; das Verwaltungsjournal `journal.json` führt
  Klarnamen (90 Tage). Das Logbuch nennt Adressen, Präfixe und Konten nur als gekürzte HMAC-Werte (B1.7).
- Sicherungen enthalten das Journal mit Klarnamen, eine Sicherung des ganzen Containers zusätzlich alle Schlüssel (B18.4).
- Die Klartextdatei `/var/log/funk/sperren.log` gibt es nur, wenn du sie für fail2ban einschaltest; sie hat keine
  Aufbewahrungsfrist (B16.3).
- Sprache geht verschlüsselt über die Leitung, aber der Server sieht sie im Klartext, weil er weiterleitet (keine
  Ende-zu-Ende-Verschlüsselung, Protokoll 1.2). In Zustandsdateien und Logbuch steht keine Sprache.
- Die Adresse des Servers sehen alle Mitglieder; daheim ist das die Heimadresse.

## B23 Belege

Dieser Teil stützt sich auf den Stand von `main` bei Fassung 0.5.0 (`95aa78b`, 02.10.2026). Davor war er für 0.4.0 am Stand
`d45e33f` geschrieben und für 0.4.1 am Stand `8c73864` nachgesehen (dort Server-, Protokoll- und Werkzeugcode unverändert). Für 0.5.0
neu gegen den Code gelesen: `src/Funk.Admin` (`Befehlsoptionen.cs`, `Uebersetzung.cs`, `Hilfe.cs`, `Einrichtung.cs`, `Program.cs`,
`Serverpruefung.cs`), `src/Funk.Server` (Phasen, Funker-Modus, Rechte, Zustandsablage), `src/Funk.Protokoll` (Nachrichten,
Versionen, Grenzen, Steuerbremse), `src/Funk.Verwaltung` (Reiter Phasen) und die Standzeilen in `PROJEKT.md` vom 01.10.2026 20:15,
22:21, 23:05 und vom 02.10.2026 00:10 und 01:00. Die Suite stand am 02.10.2026 01:00 bei 1312 von 1312 bestandenen Tests. Die
Programme selbst wurden für diese Überarbeitung nicht gestartet; die Meldungen sind aus dem Code gelesen. Unterlagen:

| Quelle | Wofür |
| --- | --- |
| `docs/BETRIEB.md` | Architektur, Konfiguration, Befehle, Sperrliste, laufender Betrieb, Updates |
| `docs/EINRICHTEN.md`, `konfig/funk-einrichten.sh`, `src/Funk.Admin/Einrichtung.cs`, `Einrichtorte.cs`, `Konfigschreiber.cs` | Einrichten mit einem Befehl |
| `docs/HEIMSERVER-PROXMOX.md` | Container, Einrichtung von Hand, Erster Admin, Echo-Funker, Sicherung, Updates (Abschnitte 14 bis 16) |
| `docs/HEIMSERVER-HAERTUNG.md`, `konfig/fail2ban/` | Härtung, fail2ban |
| `docs/UMZUG.md`, `src/Funk.Admin/Umzug.cs`, `Umzugspaket.cs`, `Profilerneuerung.cs`, `Profilanfrage.cs` | Umzug, Profil erneuern |
| `docs/FUNKPROTOKOLL-v1.md` (3.1, 3.9, 3.10, 4.1, 4.4 bis 4.10, 6.1, 6.9, 6.10, 6.12, 6.14, 6.15, 7.x) | Profil, Pins, Admins, TLS, Rechte, Nachrichten, Phasen, Funker-Modus |
| `docs/LASTTEST.md` | Last und Upload |
| `konfig/server.beispiel.json`, `konfig/funk-server.service`, `konfig/funk-echo.service`, `konfig/probefunk.json` | Beispiele |
| `src/Funk.Admin/Hilfe.cs`, `Befehlsoptionen.cs`, `Uebersetzung.cs`, `Serverpruefung.cs`, `ErsterAdmin.cs`, `Program.cs` | Befehle und Optionen |
| `src/Funk.Server/Betrieb/Konfiguration.cs`, `SperrlistenKonfiguration.cs`, `Profilabgleich.cs`, `Uhrpruefung.cs`, `Dienst.cs`, `Kern/Zentrale.*.cs`, `Verwaltung/Verwaltungssocket.cs` | Regeln, Meldungen, Grenzen |
| `src/Funk.Server/Lage/Zustandsschreiber.Phasen.cs`, `Fristen.cs`, `Rechte.cs`, `Kern/Zustandsablage.cs`, `Kern/Zentrale.Raidleitung.cs` | Phasen, Funker-Modus, Rückfall, Rangregel, Sicherung ohne Aufträge und Funker-Modus (B1.8, B10.7, B14.3, B18.2) |
| `src/Funk.Protokoll/Steuerung/Versionen.cs`, `Grenzen.cs`, `Steuerbremse.cs`, `Nachrichten/Phasen.cs` | Fassungen je Nachricht, Grenzen, Bremsen |
| `src/Funk.Verwaltung/Modelle/FlotteViewModel.cs`, `Flotte/Flottenentwurf.cs`, `Ansichten/VerwaltungAnsicht.xaml` | Reiter Phasen im Verwaltungsteil |
| `tests/Funk.Tests/AdminWerkzeugTabelleTests.cs`, `BeispielnamenTests.cs` | `netz-setzen --taste`, Beispielnamen, "der Eigentümer" in den Ausgaben |
| `docs\entwurf\ENTSCHEIDUNGEN.md` (Anhang A: Anf. A37, A41, A51, A53 bis A59; Kapitel "Rechte und Verwaltung" und "Server und Betrieb") | Anforderungen und Entscheidungen des Eigentümers |
| Messung am 01.10.2026 | `funk-admin hilfe`, `funk-server --hilfe`, die Optionsprüfung, die Erzeugen-Befehle mit Exit 3, `konfig-schreiben`, `profil-anfrage`, `pruefen` ohne Profil; gebaut aus diesem Stand unter Windows, mit allen Ablagen außerhalb des Repositorys |
