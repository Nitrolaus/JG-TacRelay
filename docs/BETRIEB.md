# Betrieb des Funk-Servers

Stand 03.10.2026, Fassung 0.6.0: Stufe 1 (Anmeldung mit Namen, Admin-Nachweis, Mitglieder), Stufe 2 (Auftrag für einen
Abend, Einheiten, Belegung), die Härtung (Sperrliste), Einrichten und Umzug (`funk-einrichten.sh`, Profilübernahme im
Client, Umzugspaket), die Ausbaustufe S2 (Einsatzphasen, Heute als Funker) und Stufe 3 (Kanäle mit Ch.,
Kanalsperren, Versionstor). Gilt für `funk-server` (Dienst) und `funk-admin` (Admin-Werkzeug), beide C# auf .NET 10,
framework-abhängig. Grundlage ist `FUNKPROTOKOLL-v1.md`, besonders 3.3 bis 3.10, 4.1, 4.6, 4.8, 5.9 bis 5.14, 6 und 7. Ein
Server mit 0.5.0 kommt über den Abschnitt "Update auf 0.6.0" unten auf den neuen Stand, einer mit 0.4.0 vorher über
"Update auf 0.5.0", einer mit 0.2.0 oder 0.3.0 davor über "Update auf 0.4.0", einer mit 0.1.0 noch davor über "Update von
0.1.0 auf 0.2.0". Auf 0.13.0 führt kein Update: Ein Server bis 0.12.0 wird neu eingerichtet (Abschnitt "Update auf
0.13.0"). Einen neuen Server richtet `EINRICHTEN.md` mit einem Befehl ein, einen Umzug beschreibt `UMZUG.md`.

## Was läuft

| Teil | Aufgabe |
| --- | --- |
| `funk-server` | Ein Prozess. Nach außen genau ein TCP-Port (TLS, Steuerkanal) und ein UDP-Port (Sprache), je konfigurierter Adresse. Kein HTTP, keine Web-Oberfläche, kein Admin-Port. |
| Verwaltungssocket | UNIX-Socket (Datei, keine Netzadresse) für `funk-admin`. Unter Linux nur Besitzer (0600) und Prüfung der UID der Gegenseite (`SO_PEERCRED`: root, Dienstbenutzer, `verwaltungsUids`). |
| `funk-admin` | Kommandozeile. Offline-Befehle für Schlüssel, Betriebsgeheimnis und Serverprofil; alle übrigen reden über den Socket mit dem laufenden Server. Ersetzt vorläufig den Discord-Bot (3.8). |
| Zustandsverzeichnis | `lage.json` (Kanäle, Einheiten, Rollen, Konten mit Feldern, Plätzen und ab 0.3.0 Aufträgen, Aufstellungen), `konten.json` (Konten, Anträge und Geräte), `komfort.json` (Pegel, Ablage, Sendeauswahl, Regeltabelle), `vorrangspur.json` (nur HMAC-Werte), `sperrliste.json` (ab 0.3.0, nur HMAC-Werte und Kennungen, nie in der Sicherung), `admins.json` (Admin-Nachweise, TOTP-Geheimnisse nur verschlüsselt, 0600), `mitglieder.json` (Rollenkatalog, Vormerkungen, Mitgliederrollen, Wünsche, Abzeichen, dazu Untergilden aus der Zeit vor 0.11.8 mitgeführt, ohne gelöschte Konten und Vormerkungen, 0600), `journal.json` (Verwaltung mit Klarnamen, 90 Tage, 0600). Jede Datei wird atomar ersetzt (Zwischendatei, Flush, Umbenennen). |

Im Prozess arbeiten: die TLS-Annahme, je Verbindung eine Lese- und eine Schreibschleife, die Zentrale als
einziger Schreiber für Kontenbuch, Einmalcodes und Zustandsschreiber (Gruppen-Commit), genau ein Relay-Thread
für den Sprachpfad, je UDP-Socket ein Empfangsthread und ein Sende-Thread mit Pacer (35 Mbit/s Standard).

## Voraussetzungen

- .NET 10 Runtime (`dotnet`), keine weiteren Pakete. Auf dem LXC zusätzlich `libicu`, denn die Lösung läuft
  mit `InvariantGlobalization=false` (NFKC-Namen nach 3.3, NFC nach 4.3).
- Ein Dienstbenutzer `funk` ohne Login.
- Serverprofil-Schlüssel und Reserve-Serverschlüssel liegen offline, nie auf dem Server (3.1, Offener Punkt 21).

## Erstinstallation (Linux, LXC)

1. Bauen und veröffentlichen (auf dem Entwicklungsrechner, Ausgabe framework-abhängig):

   ```
   dotnet publish src/Funk.Server -c Release -o out/funk
   dotnet publish src/Funk.Admin  -c Release -o out/funk
   ```

   `out/funk` nach `/opt/funk` auf den LXC kopieren. Aufruf: `dotnet /opt/funk/funk-server.dll`,
   `dotnet /opt/funk/funk-admin.dll`.

2. Benutzer und Verzeichnisse:

   ```
   useradd --system --no-create-home --shell /usr/sbin/nologin funk
   install -d -m 0755 /etc/funk
   install -d -m 0700 -o root -g root /etc/funk/credentials
   ```

   `/var/lib/funk` und `/run/funk` legt systemd an (`StateDirectory`, `RuntimeDirectory`, beide 0700).

3. Serverschlüssel und Zertifikat (ECDSA P-256, selbstsigniert, 20 Jahre):

   ```
   dotnet /opt/funk/funk-admin.dll schluessel-erzeugen \
       --zertifikat /etc/funk/credentials/server.crt --schluessel /etc/funk/credentials/server.key
   ```

   Die Ausgabe nennt den Pin (Base64) und die Anzeigeform. Einen Reserveschlüssel genauso erzeugen, aber am
   Offline-Rechner, und nur seinen Pin notieren. Ab 0.4.0 ersetzen die Befehle, die Schlüssel oder Geheimnisse erzeugen,
   keine vorhandene Datei: `<pfad> gibt es schon. Nichts geändert.` mit Exit 3. Ersetzen nur mit `--ersetzen`, und die
   Meldung nennt vorher, was das kostet (neuer Pin, unlesbarer Zweitfaktor, leere Vorrangspur).

4. Betriebsgeheimnis (32 Zufallsbytes; daraus leitet der Server die Schlüssel für die Schwärzung im Logbuch und
   für die Vorrangspur ab):

   ```
   dotnet /opt/funk/funk-admin.dll geheimnis-erzeugen --datei /etc/funk/credentials/funk-geheimnis
   ```

   TOTP-Schlüssel (32 Zufallsbytes; damit verschlüsselt der Server die TOTP-Geheimnisse der Admins in
   `admins.json` und leitet den Schlüssel für die Prüfwerte der Notfallcodes ab, 3.9):

   ```
   dotnet /opt/funk/funk-admin.dll totp-schluessel-erzeugen --datei /etc/funk/credentials/funk-totp-schluessel
   ```

   Geht dieser Schlüssel verloren, lassen sich die Geheimnisse nicht mehr öffnen: Der Eigentümer richtet dann den
   Zweitfaktor jedes Admins an der Konsole neu ein (`admin-einrichtung-neu`). Eine Kopie außerhalb des Servers ist
   deshalb nicht nötig, und in eine Sicherung gehört er nie.

5. Serverprofil (am Offline-Rechner, im ganzen Ordner `linux` des Pakets, auf dem Stick umbenannt in `funk-offline`,
   damit er mit den Schlüsseln darin nie in den Container gepackt wird; `funk-admin.dll` braucht `funk-server.dll`
   und `Funk.Protokoll.dll` daneben). `--tcp-port` ist der Port außen am Router, daheim 8443, weil 443 dort nginx gehört:

   ```
   dotnet funk-admin.dll profilschluessel-erzeugen --schluessel profil.key          # einmalig; SPKI in den Client einbauen
   dotnet funk-admin.dll profil-schreiben --name "Meine Staffel" --version 1 --host funk.example.org/v4 --tcp-port 8443 \
       --pin <Pin des Serverschlüssels> --pin <Pin des Reserveschlüssels> --aus daten.json
   dotnet funk-admin.dll profil-signieren --daten daten.json --schluessel profil.key --aus profil.json
   dotnet funk-admin.dll profil-pruefen --profil profil.json --spki <SPKI Base64>
   ```

   `profil.json` nach `/etc/funk/profil.json` kopieren. Der Server reicht es in Welcome nur durch (3.1); Clients ab 0.4.0
   übernehmen ein neueres daraus (Profilübernahme, `UMZUG.md`). Steht der Pin des laufenden Schlüssels nicht darin, warnt
   der Server beim Start und ab 0.4.0 auch bei `profil-neu-laden`, mit Version und Kennung: `Der Pin des Serverschlüssels
   (XXXX-XXXX-XXXX-XXXX) steht nicht im Serverprofil Version N: ...`. Zu den Clients kommen Profil und
   Profilschlüssel ab 0.2.0 nur mit dem Setup: `profil.json` nach `konfig\profil.json`, die SPKI-Zeile nach
   `konfig\profilschluessel.txt` im Repository, dann beide Setups bauen (`tools\setup-bauen.ps1`, mit und ohne
   `-Verwaltung`).

6. Konfiguration: `konfig/server.beispiel.json` nach `/etc/funk/server.json` kopieren und anpassen (Abschnitt
   Konfiguration). Prüfen ohne Start: `dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen`.

7. systemd: `konfig/funk-server.service` nach `/etc/systemd/system/` kopieren, dann

   ```
   systemctl daemon-reload
   systemctl enable --now funk-server
   journalctl -u funk-server -f
   ```

   Beim Start steht im Journal eine Zeile `Funk-Server ... bereit` mit Bindports, öffentlichen Ports und der
   Anzeigeform des Pins, davor die Warnung `Kein Eigentümer gesetzt`, bis ein Eigentümer eingetragen ist (3.10). Die
   Unit setzt `LimitCORE=0` und `DOTNET_DbgEnableMiniDump=0` (7.5). Fehlt eine Datei, die sie per `LoadCredential`
   lädt, startet systemd den Dienst nicht (`status=243/CREDENTIALS`).

## Konfiguration

JSON mit Kommentaren. Unbekannte Felder sind ein Fehler. Relative Pfade gelten ab dem Verzeichnis der Datei,
`${NAME}` wird aus der Umgebung ersetzt (unter systemd `${CREDENTIALS_DIRECTORY}`).

| Feld | Standard | Bedeutung |
| --- | --- | --- |
| `binden` | - | IP-Adressen; je Adresse TCP und UDP mit gleicher IP (4.1). Leer: der Server startet nicht. `0.0.0.0` und `::` nur über `alleAdressen`. |
| `alleAdressen` | false | Ausdrücklich an alle Adressen binden, mit Warnung im Logbuch. |
| `tcpPort` / `oeffentlicherTcpPort` | 8443 / = tcpPort | Bindport im Container und Port am Router (steht im Profil). |
| `udpPort` / `oeffentlicherUdpPort` | 47443 / = udpPort | Bindport und öffentlicher Port; Welcome nennt den öffentlichen. |
| `nurTls13` | false | TLS 1.2 abschalten, sobald niemand mehr Windows 10 nutzt (Offener Punkt 2). |
| `zustandsverzeichnis` | `zustand` | Zustandsdateien. |
| `identitaet.zertifikat`, `identitaet.schluessel` | `server.crt`, `server.key` | PEM-Dateien, unter systemd aus `$CREDENTIALS_DIRECTORY`. |
| `geheimnis` | `geheimnis.bin` | Betriebsgeheimnis. |
| `totpSchluessel` | Pflicht in der Datei | Schlüssel für die TOTP-Geheimnisse (3.9), unter systemd `${CREDENTIALS_DIRECTORY}/funk-totp-schluessel` (so im Beispiel). Fehlt das Feld (eine `server.json` aus 0.1.0), melden `pruefen` und der Start `Konfiguration ungültig: totpSchluessel fehlt (neu seit 0.2.0, ...)` mit den nötigen Schritten und enden mit Exitcode 2; den Standard `totp-schluessel.bin` bekommen nur im Code gebaute Konfigurationen (Tests, Probefunk). Fehlt die Datei, endet der Start mit `funk-totp-schluessel fehlt (<pfad>)`; das sieht `pruefen` nicht, es prüft keine Schlüsseldateien. |
| `mitgliedskarte.rollenFuerAlle` | true | Rollen und Wünsche auf der Mitgliedskarte für alle sichtbar (6.13, Frage 3); false: nur Einsatzleitung, Admins und das Konto selbst. |
| `profil` | Pflicht | Signiertes Serverprofil `{daten, signatur}`. |
| `mindestClientVersion` | `0.1.0` | Darunter `Fehler{VERSION_ZU_ALT}` statt Welcome. Vorerst bei `0.1.0` lassen, solange Mitglieder JG TacRelay 0.1.0 haben: Er meldet im Hello fest `0.1.0`. Ab 0.2.0 melden JG TacRelay und `funk-echo` ihre Fassung aus `Directory.Build.props` (`FunkClientOptionen.Fassung`), `funk-testclient` meldet `1.0.0`. Haben alle 0.2.0, auf `0.2.0` setzen, `pruefen`, Neustart. Vor einem Umzug auf `0.4.0`, erst wenn alle das Setup 0.4.0 haben, denn erst 0.4.0 übernimmt das Übergangsprofil (`UMZUG.md`, Schritt 1). |
| `verwaltungsSocket` | `<zustand>/verwaltung.sock` | Pfad des UNIX-Sockets. |
| `verwaltungsUids` | `[]` | Weitere zugelassene UIDs am Socket (Linux). |
| `vertrauenswuerdigeWeiterleiter` | `[]` | Vorposten, die Absenderadressen erhalten (7.6); für sie gelten nur globale Töpfe. |
| `grenzen.ausgangMbit`, `grenzen.leitungsZuschlag` | 35, 26 | Pacer (7.4): höchstens 80 % des gemessenen Netto-Uploads. |
| `grenzen.maxSitzungen` | 100 | Gleichzeitige Sitzungen (1.2). |
| `grenzen.unbekannteGlobal`, `grenzen.unbekannteJePraefix` | 20, 2 | Unfreigegebene Verbindungen nach TLS (4.6, 4.8). Freigegebene Geräte zählen nie. |
| `grenzen.verbindungenJePraefix` | 8 | Gleichzeitige TCP-Verbindungen je Präfix (gemeinsames NAT). |
| `grenzen.handshakes...` | 10/min Stoß 5, Vorrang 60/min Stoß 10, global 200/s | Handshake-Töpfe je Präfix; der globale ist nur CPU-Schutz, die Vorrangspur geht daran vorbei. |
| `grenzen.udpEmpfangspuffer` | 1 MiB | 5.14: 512 KiB bis 1 MiB. |
| `grenzen.relaisKapazitaet` | 4096 | Warteschlange zum Relay-Thread; voll heißt verwerfen. |
| `sperrliste.aktiv` | true | Eingebaute Sperrliste (Abschnitt Sperrliste unten); `false` nur zur Fehlersuche, mit Warnung beim Start. |
| `sperrliste.schwelle`, `sperrliste.fensterSekunden` | 10, 600 | So viele Punkte in diesem Fenster sperren eine Adresse (IPv4) bzw. ein IPv6-/64. |
| `sperrliste.ersteSperreSekunden`, `sperrliste.faktor`, `sperrliste.hoechstdauerStunden` | 300, 6, 24 | Dauer der ersten Sperre, Faktor je weiterer Sperre, Obergrenze: 5 min, 30 min, 3 h, 18 h, 24 h. |
| `sperrliste.vergessenStunden` | 48 | So lange nach dem Ende der letzten Sperre merkt sich die Liste die Stufe. |
| `sperrliste.durchlassSekunden` | 30 | Während einer Sperre darf je Adresse alle so viele Sekunden eine Verbindung bis zum Handshake (freigegebenes Gerät hinter derselben Adresse). |
| `sperrliste.maxEintraege` | 10000 | Höchstens so viele Adressen verfolgt (Speicher); Sperren fallen nie heraus. |
| `sperrliste.v6Sammelschwelle` | 4 | So viele gesperrte /64 aus einem /48 sperren das ganze /48, ebenso `schwelle` mal dieser Wert an Punkten (40) aus seinen /64 zusammen; 0 schaltet beides ab. |
| `sperrliste.ausnahmen` | `[]` | Adressen oder Netze (CIDR), die nie gesperrt werden, etwa eine Überwachung im Heimnetz. |
| `sperrliste.klartextDatei` | null | Optional: je neuer Sperre eine Zeile mit Klartext-IP in diese Datei, nur für fail2ban auf dem Host (`konfig/fail2ban`, `HEIMSERVER-HAERTUNG.md`). Das Logbuch bleibt ohne Klartext-IP. |
| `log.stufe`, `log.datei` | INFO, null | Logbuch nach stdout (Journal), optional zusätzlich in eine Datei. |
| `test.rotationSekunden`, `test.zufallsPorts` | - | Nur Test- und Dauerläufe (P-32), nie im Betrieb. |

"Präfix" heißt bei IPv4 die einzelne Adresse, bei IPv6 das /64; IPv4 in IPv6-Form zählt als IPv4 (Abschnitt 0).

## Admin-Werkzeug

Aufruf über den laufenden Server: `dotnet /opt/funk/funk-admin.dll <befehl> --konfig /etc/funk/server.json ...`
(oder `--socket <pfad>`), als root oder als Benutzer `funk`. `funk-admin hilfe` zeigt alle Befehle. `--json-aus` gibt
die Antwort als JSON aus. In der Tabelle und den Beispielen unten steht kurz `funk-admin`: gemeint ist immer
`dotnet /opt/funk/funk-admin.dll`, und jeder Befehl, der mit dem Server redet, braucht `--konfig` (oder `--socket`).
Jeder Befehl nimmt nur seine Optionen: Eine unbekannte oder doppelte Option, ein Wert ohne Option (Leerzeichen ohne
Anführungszeichen) oder `--name=wert` bricht mit Exit 2 ab, bevor etwas geschieht, etwa `scnamen kennt --name nicht.
Erlaubt: --konto, --namen, --leeren (dazu --konfig oder --socket, --json-aus). Nichts geändert.` Eine Liste leeren
(`scnamen`, `rechte`) geht nur ausdrücklich mit `--leeren`.

| Befehl | Wirkung |
| --- | --- |
| `status` | Verbindungen, Sitzungen, Konten, Geräte, Kanäle, zustandsVersion, Unfreigegebene, Vorrangspur. |
| `konto-anlegen --discord <id> --benutzername <n> [--name] [--stufe]` | Konto ohne Gerät (Bootstrap, 3.8). |
| `verknuepfen --code XXXX-XXXX-XXXX --discord <id> --benutzername <n> [--name] [--stufe]` | Löst den Code ein, der Client fragt nach (120 s), bei ja entsteht das Gerät in WARTET. Wartet auf die Antwort. |
| `mitgliedschaft --discord <id> --status OK\|NICHT_MITGLIED\|ROLLE_FEHLT` | Server-Seite des Discord-Abgleichs (3.7), höchstens 3 Sperren je Stunde, nie ADMIN. |
| `konten`, `geraete [--konto] [--wartend] [--nicht-seit JJJJ-MM-TT]` | Übersicht; `--nicht-seit` (ab 0.4.0) zeigt nur Geräte ohne Sitzung seit dem Tag (UTC) mit `zuletzt <tag>` oder `nie verbunden`, die Nachzügler vor einem Umzug (`UMZUG.md`, Schritt 6); `konten` je Konto mit ID, Stufe, Status (ANTRAG, AKTIV, GESPERRT), `EIGENTÜMER`, SC-Namen (ohne SC-Namen, etwa aus 0.1.0, der Anzeigename) und `(Discord: <Discord-Name oder ->, <Benutzername>)`, etwa `1  ADMIN      AKTIV    test (Discord: -, test)`; bei Geräten die Schlüsselart immer "laut Client, ungeprüft". |
| `freigeben <anzeige>`, `ablehnen <anzeige>` | Freigabe per Anzeigeform oder vollem Fingerabdruck. Die wartende Verbindung geht sofort nach AKTIV_SYNC. `ablehnen` löscht seit 0.7.0 das wartende Gerät, einen Antrag samt Antragskonto, und sperrt nichts; die wartende Verbindung bekommt `GeraetStatus{ABGELEHNT}` und `Bye{ENTFERNT}`, der PC darf später neu beantragen. |
| `entfernen <anzeige> [--grund]`, `sperren <anzeige> [--grund]` | ENTFERNT bzw. ENTZOGEN (Fingerabdruck dauerhaft gesperrt, auch nach dem Löschen des Kontos); laufende Sitzung endet sofort. `sperren` ist seit 0.7.0 die einzige Gerätesperre und immer bewusst; Ablehnen und Löschen sperren nichts. |
| `sperre-aufheben <anzeige>` | Seit 0.8.0 das Gegenstück zu `sperren`: Das Gerät gilt danach als ENTFERNT, der PC kann einen neuen Antrag stellen und braucht wieder eine Freigabe. Findet über Anzeigeform oder Fingerabdruck auch eine Sperre ohne Gerät (Konto nach dem Sperren gelöscht). Abschnitt "Gerätesperre aufheben". |
| `konto-sperren`, `konto-entsperren`, `konto-loeschen --konto <id>` | 3.6; Sperre trennt sofort. Das Admin-Werkzeug darf auch den letzten Admin verwalten. Den Eigentümer löscht es ab 0.3.0 nicht: `Das Konto ist Eigentümer und lässt sich nicht löschen. Erst mit 'eigentuemer --konto <id>' ein anderes ADMIN-Konto zum Eigentümer machen. Nichts geändert.` |
| `stufe --konto <id> --stufe <S>`, `rechte --konto <id> --rechte A,B` oder `--leeren`, `name --konto <id> --name <n>` | 6.10. Ohne `--rechte` und ohne `--leeren` lehnt `rechte` ab. |
| `netz-setzen --id --name [--offen] [--standard] [--taste] [--nur-taste] [--reihenfolge] [--gruppe] [--kanaele]` oder `--kanaele-datei` oder `--json` | Kanal anlegen oder ändern (Form wie in NetzListe). Ab 0.5.0 ohne Schalter mit Sprachaktivierung (`--va` wirkt genauso), mit `--taste` ab Werk auf Taste, mit `--nur-taste` nur Taste; Global (Kanal 1) immer Taste. Gespeicherte Kanäle behalten ihren Wert. Seit 0.6.0 macht `--kanaele "Ruhe,Schiff 1,Schiff 2"` (oder `--kanaele-datei` mit einer Bezeichnung je Zeile) den Kanal zum Kanalnetz; die erste Bezeichnung ist die Ruhe (Ch. 0), `--gruppe` ordnet die Kanalwahl. Ab dem ersten Kanalnetz kommen nur noch Clients ab 0.6.0 hinein. |
| `rolle-setzen --id --name [--selbst] [--braucht-schiff] [--kapazitaet] --feld feld:netz[:h][:s][:std][:fest]` oder `--json` | Rolle. |
| `schiff-setzen --json '<objekt>'` | Schiff mit Kennungen und Plätzen. |
| `netz-loeschen`, `rolle-loeschen`, `schiff-loeschen --id` | Löschen; Mitgliedschaften darauf fallen weg. |
| `einstellung [--selbstwahl an\|aus] [--nachzuegler an\|aus] [--durchsage-nur-taste an\|aus]` | Einstellungen der NetzListe. `--selbstwahl aus` sperrt die Selbstwahl für 12 h (seit 0.3.0, 6.9); Funker ohne Platz nehmen dabei weiter einen freien, mit `--nachzuegler aus` auch sie nicht. |
| `auftrag --konto <id> [--von HH:MM] [--bis HH:MM] [--rechte A,B] [--grund <text>]` | Einsatzleitung für einen Abend (seit 0.3.0, 6.12): ohne `--bis` bis zum nächsten 04:00, höchstens 24 h, im Voraus bis 14 Tage, Zeiten in Ortszeit Europe/Berlin (auch `JJJJ-MM-TT HH:MM`). Ohne `--rechte` alle sechs der Einsatzleitung; Pflicht sind ZUWEISEN und SPRECHANZEIGE_ALLE. Nicht an ein ADMIN-Konto, nicht an ein gesperrtes. Ein zweiter Auftrag an dasselbe Konto ersetzt den ersten. |
| `auftrag-entziehen --konto <id>`, `auftraege` | Auftrag sofort beenden; laufende und geplante Aufträge mit Zeiten, Urheber und Grund. `sicherung` nimmt Aufträge nicht mit. |
| `flotte-vorlage [--name starcitizen]` | Legt die Vorlage Star Citizen neben die vorhandene Flotte (10 Kanalnetze mit ihren Ch.-Listen, Kommando bis Allgemein, alle offen zum Hören und Senden und mit Sprachaktivierung; 14 Rollen "Standard (Gast)" bis "Funker (Leitstand)", selbst wählbar; keine Einheiten; 24 Schritte; Global ändert sie nicht) und nennt sechs Annahmen, die der Admin prüft. Nur, solange es außer Global kein Kanalnetz gibt. Danach gilt das Versionstor: Clients unter 0.6.0 kommen nicht mehr hinein, also vorher die Setups 0.6.0 verteilen. Rückweg: die Kanalnetze löschen (HEIMSERVER-PROXMOX.md, Abschnitt 18). |
| `flotte-vorlage --name <alte Vorlage>` | Bis 0.12.0: die alte Startvorlage (ohne `--name`, nur auf eine leere Flotte) und seit 0.6.0 die alte Kanalvorlage (Global mit Ch., 30 Kanalnetze, 21 Rollen). Beide gibt es seit dem Umbau zu JG TacRelay (19.10.2026) nicht mehr; heute gilt `flotte-vorlage --name starcitizen`. |
| `kanalsperre-setzen --konto <id>` oder `--alle`, `--netz <nummer oder name>`, `--ch <n>`, `--bis HH:MM` | Seit 0.6.0 (6.17): einen Kanal für eine Person oder mit `--alle` für alle ohne Einsatzleitung sperren: weder hören noch senden noch hineinlegen, das Feld bleibt stehen. Ohne `--ch` gilt es dem ganzen Kanal (bei einem Kanalnetz allen Ch.), mit `--ch` einem Ch. des Kanalnetzes. Ohne `--bis` bis zum nächsten 04:00, höchstens 24 Stunden. Global, Orgaweit und eine Ruhe lassen sich nicht sperren. Im Journal steht die Konsole als Urheber. |
| `kanalsperre-aufheben --konto <id>` oder `--alle`, `--netz <nummer oder name>`, `--ch <n>` | Hebt eine Kanalsperre auf, auch eine, die die Einsatzleitung gesetzt hat. Gibt es keine, ändert sich nichts, und der Befehl sagt es. |
| `kanalsperren` | Die laufenden Kanalsperren: Kanal mit Ch., für wen, bis wann, von wem (Konsole oder Name). |
| `netzliste` | Kanäle (ein Kanalnetz mit `Ch. 0 bis <n>` am Ende der Zeile), Rollen, Schiffe, Version. |
| `verdraengung-freigeben --konto <id>` | Hebt VERDRAENGEN_GESPERRT auf (4.6). |
| `vorrangspur`, `vorrangspur-leeren` | Anzahl der Präfixe (nur HMAC-Werte) bzw. leeren. |
| `messwerte` | Relais insgesamt und je Sitzung (7.8): hoch empfangen, verloren, umsortiert, doppelt, Jitter, runter, Tunnelanteil, Pfadwechsel, Kopierverdacht, RTT, Güte. |
| `sicherung [--ziel <verz>]` | Kopie der Zustandsdateien, Standard `<zustand>/sicherungen/<zeit>`. Schlüssel und Geheimnis kommen nie mit. |
| `profil-neu-laden` | Liest `profil` neu, etwa nach dem Einspielen eines neuen Profils. Ab 0.4.0 antwortet es `Profil Version N geladen.` und, wenn der Pin dieses Servers im Profil fehlt, darunter `WARNUNG Der Pin des Serverschlüssels (...) steht nicht im Serverprofil Version N: ...`, bei einem anderen TCP-Port als in `server.json` `WARNUNG Das Serverprofil nennt TCP-Port ...`, dieselben Zeilen im Journal. Geladen ist es dann trotzdem, wie beim Start; die vorige Fassung wieder einspielen. |
| `antraege` | Offene Anträge ohne Code (3.4) mit SC-Name, Discord-Name, Gerätekennung und passenden Vormerkungen; `NAME VERGEBEN` bzw. `DISCORD-NAME VERGEBEN (<wem>)`, wenn der Name schon vergeben ist. Ab 0.3.0 dazu bestehende Konten mit passendem Namen (ganzer Name oder der Teil vor dem Schrägstrich, verglichen wie die Eindeutigkeit, auch mit dem Discord-Benutzernamen aus 0.1.0): `BESTEHENDES KONTO? <Name> (Konto <id>), <STUFE>: gehört das Gerät dazu (nur wenn sein neues Gerät dieselbe Kennung zeigt), dann funk-admin freigeben <kennung> --an-konto <id>`; JSON `konten`. Nur ein Hinweis, angehängt wird nie von selbst. Seit 0.7.0 gilt ein PC, ein Konto: Ein weiterer PC eines Mitglieds wird einfach freigegeben und bekommt sein eigenes Konto; anhängen nur noch den PC eines Admins mit neuem Geräteschlüssel. Die Namen tippt der Antragsteller selbst; vor dem Anhängen die Kennung mit dem Bildschirm des Geräts vergleichen. |
| `geraet-umhaengen <anzeige> --an-konto <id>` | Ab 0.3.0, nur Konsole: ein freigegebenes Gerät an ein anderes aktives Konto (höchstens drei aktive Geräte), etwa nach einem Antrag ins falsche Konto (`HEIMSERVER-PROXMOX.md`, Ende von Abschnitt 14). Die Sitzung des Geräts endet ohne Abschied, der Client verbindet gleich neu. Hat das alte Konto danach kein Gerät mehr, nennt die Antwort `konto-loeschen --konto <id>`. Journal `GERAET_UMGEHAENGT`. Ein wartender Antrag geht mit `freigeben --an-konto`. |
| `sperrliste` | Ab 0.3.0: laufende Sperren mit Kennung (HMAC wie im Logbuch, nie die Adresse), Ende, Stufe, Anlass und abgewiesenen Verbindungen; `Keine Sperren (n Adressen beobachtet).`, wenn keine läuft. |
| `entsperren --eintrag <kennung>` oder `--ip <adresse>` oder `--alle` | Ab 0.3.0: eine Sperre aufheben, nach Kennung aus `sperrliste`, nach Adresse (die das Mitglied nennt; bei IPv6 auch die Sammelsperre seines /48) oder alle. Die Antwort nennt nur die Kennung. Journal `SPERRE_AUFGEHOBEN`. |
| `freigeben <anzeige> [--vormerkung <id>] [--an-konto <id>]` | Freigabe eines Antrags mit Übernahme einer Vormerkung bzw. als zweites Gerät eines vorhandenen Kontos (3.5). Ist der Discord-Name des Antrags vergeben, gibt sie seit 0.7.0 ohne ihn frei (bis 0.6.x lehnte sie ab); trägt ihn eine Vormerkung, bekommt ihn das Konto mit `--vormerkung <id>`. |
| `scnamen --konto <id> --namen A,B,C` oder `--leeren` | SC-Namen eines Kontos setzen, eindeutig über Konten und Vormerkungen; `--leeren` nimmt alle weg. Ohne `--namen` und ohne `--leeren` lehnt `scnamen` ab. |
| `discordname --konto <id> --name "<Discord-Name>"` oder `--leeren` | Discord-Namen eines Kontos setzen oder wegnehmen: 2 bis 32 Zeichen, keine unsichtbaren Zeichen, eindeutig über Konten und Vormerkungen, auch gegen Groß- und Kleinschreibung und Breitschrift (verglichen wie SC-Namen). Vergeben: `Den Discord-Namen X trägt schon Y (Konto N). Nichts geändert.` Im Client ändert ihn nur ein Admin in der Admin-Sitzung (Verwaltung, Mitglieder, Karte eines Kontos), das Mitglied selbst nicht; die Konten anderer Admins und das des Eigentümers nur die Konsole. |
| `mitglieder`, `journal [--konto <id>] [--anzahl 50]` | Mitglieder und Vormerkungen lesen; Journal mit Klarnamen lesen (3.9). Pflege gehört in den Verwaltungsteil des Clients. |
| `eigentuemer --konto <id>` | Setzt den Eigentümer (3.10); nur ein ADMIN-Konto. |
| `admin-vorsehen --konto <id>` | Erlaubt dem Konto einen Antrag auf einen Admin-Schlüssel (auch für einen neuen PC eines Admins). |
| `admin-ernennen --konto <id> --kennung XXXX-XXXX-XXXX-XXXX` | Trägt den beantragten Admin-Schlüssel ein, wenn die vorgelesene Kennung passt; Stufe wird ADMIN; beim ersten Schlüssel beginnt die Einrichtung. |
| `admin-einrichtung-neu --konto <id> [--anzeigen]` | Neues TOTP-Geheimnis, altes und alte Notfallcodes verfallen, offene Admin-Sitzungen enden. Mit `--anzeigen` stehen Schlüssel (Vierergruppen) und otpauth-Link hier; kein Bildschirmfoto. |
| `zweitfaktor-bestaetigen --konto <id> --code <6 Ziffern>` | Erster Code nach `--anzeigen`; zeigt die 10 Notfallcodes. |
| `notfallcodes --konto <id>` | 10 neue Notfallcodes, die alten verfallen. |
| `admin-sperren`, `admin-entsperren`, `admin-zuruecksetzen --konto <id>` | Admin-Nachweis sperren (Sitzungen enden), entsperren (Fehlversuche fort), zurücksetzen (Schlüssel, Geheimnis, Notfallcodes fort, wieder vorgesehen). |
| `admin-entziehen --konto <id> [--stufe RAIDLEITER\|FUNKER]` | Kein Admin mehr: Nachweis fort, Stufe herab. |
| `admin-schluessel-entfernen --konto <id> --kennung <k>` | Einen Admin-Schlüssel entfernen, etwa den eines verlorenen PCs. |
| `admins` | Admin-Nachweise: Zustand, Kennungen, offene Anträge, Notfallcodes übrig, Sperrgrund, ab 0.3.0 auch in der Textzeile `Fehlversuche <n> (24 h)` und `Pause bis <HH:mm> UTC` (vorher nur mit `--json-aus`); nie Geheimnis oder Prüfwerte. |

Einrichten und Umzug (ab 0.4.0). Diese Befehle laufen im Werkzeug selbst; `--konfig` nennt die `server.json` (oder
`FUNK_KONFIG`), den Schlüsselordner `--credentials` (sonst `/etc/funk/credentials` wie in der Unit). Über den
Verwaltungssocket redet nur `erster-admin`; `umzug-packen` und `umzug-einspielen` verlangen einen angehaltenen Dienst.

| Befehl | Wirkung |
| --- | --- |
| `konfig-schreiben --aus <server.json> --binden <ip> [--tcp-port 8443] [--udp-port 47443]` | Schreibt eine gültige `server.json` mit den Werten dieser Datei als Voreinstellung, nie über eine vorhandene (Exit 3). Weitere Optionen: `--oeffentlicher-tcp-port`, `--oeffentlicher-udp-port`, `--profil`, `--zustand`. |
| `pruefen [--vollstaendig] [--credentials <ordner>] [--spki <b64>] [--host <name>]` | Ohne Schalter wie `funk-server pruefen` (Regeln, Profilabgleich). Mit `--vollstaendig` auch Schlüsseldateien und ihre Rechte, Pin des Serverschlüssels im Profil, Signatur (mit `--spki` oder `profilschluessel.txt` neben `server.json`), Ablauf, DNS der Hosts, Uhr (`timedatectl`) und freie Ports. Je Befund eine Zeile `OK`, `HINWEIS`, `WARNUNG` oder `FEHLER`, am Ende `Ergebnis: n Fehler, m Warnungen.`; Exit 1 bei einem Fehler. Schreibt nichts. |
| `profil-anfrage --host <name> [--tcp-port <p>] [--zertifikat <crt>] [--aus <datei>]` | Die Zeile `TR-PROFILANFRAGE v1 <name> <port> <pin>` für Sarevoc und die Kennung zum Vorlesen; ohne `--tcp-port` und `--zertifikat` aus `--konfig` (`EINRICHTEN.md`, Abschnitt 3). |
| `profil-erneuern --bisher <profil.json> --schluessel <profil.key> [--anfrage <zeile\|datei>] [--reserve <crt\|key\|pin>] [--gueltig-tage 365]` | Nur auf Sarevocs Stick: die nächste Profilversion aus einer Anfrage (Übergang und Abschluss eines Umzugs) oder nur verlängert, mit Vergleich von Kennung, Name und Port am Telefon; die alte Fassung bleibt als `profil-v<N>.json`, eine Kopie liegt in `zum-verschicken`. Der Doppelklick `Profil erneuern.cmd` ruft es auf (`UMZUG.md`). |
| `umzug-packen --aus <paket> [--ohne-totp] [--mit-serverschluessel]` | Zustand und TOTP-Schlüssel verschlüsselt in ein Paket (AES-256-GCM, Schlüssel aus einer Passphrase mit 26 Zeichen, die nur einmal angezeigt wird). Nie darin: Betriebsgeheimnis, `vorrangspur.json` und `sperrliste.json`; der Serverschlüssel nur mit `--mit-serverschluessel`. Nie über ein vorhandenes Paket (Exit 3). |
| `umzug-einspielen --paket <paket> [--mit-serverschluessel] [--besitzer funk]` | Fragt die Passphrase (drei Versuche), legt Vorhandenes als `.vor-umzug-<zeit>` zur Seite und setzt Besitzer und Rechte; bei falscher Passphrase bleibt alles, wie es war. |
| `erster-admin` | Auf einem Server ohne Eigentümer: offene Anträge, die Kennung aus dem Client vergleichen, dann freigeben, ADMIN, Eigentümer, vorsehen und ernennen (`EINRICHTEN.md`, Abschnitt 4). |
| `einrichten [--profil <profil.json>] [--umzug <paket>] [--mit-serverschluessel]` | Nur aus `funk-einrichten.sh`, als root (`EINRICHTEN.md`); ohne Profil Exit 4 mit der Anfragezeile. |

Bootstrap des ersten Admins (3.4 Weg B, 3.5) mit JG TacRelay ab 0.2.0: Der Client stellt mit SC-Name und Discord-Name
einen Antrag und zeigt die Gerätekennung. Auf dem Server `dotnet /opt/funk/funk-admin.dll antraege --konfig
/etc/funk/server.json`, die Kennung mit der im Client vergleichen, dann `freigeben <Kennung>`. Ein Antrag wird als
FUNKER freigegeben; danach `stufe --konto <id> --stufe ADMIN` und `eigentuemer --konto <id>`. Den Weg mit Code
(`verknuepfen --code <code> --discord <id> --benutzername <n> [--stufe ADMIN]`, im Client bestätigen, dann `freigeben`)
gehen nur noch Programme ohne Namen: `funk-echo`, `funk-testclient echo-probe` und Clients 0.1.0. JG TacRelay 0.2.0
stellt immer einen Antrag (fehlen bestätigte Namen, fragt es vorher nach ihnen) und zeigt keinen Code mehr. Kein Weg
führt vom Code oder Antrag direkt zu FREIGEGEBEN. Schritt für Schritt: `HEIMSERVER-PROXMOX.md`, Abschnitt 10.

Erste Kanäle, etwa: `dotnet /opt/funk/funk-admin.dll netz-setzen --id 2 --name Kommando --offen HOEREN_SENDEN --standard --taste
--konfig /etc/funk/server.json`. Global (Kanal 1) legt der Server selbst an, nur per Taste. Ab 0.5.0 steht ein Kanal ohne Schalter auf
Sprachaktivierung; Kommando bleibt mit `--taste` auf Taste (die Vorlage Star Citizen legt es mit Sprachaktivierung an).
funk-admin 0.4.x kennt `--taste` nicht und lehnt mit Exit 2 ab, ohne etwas zu ändern; dort legt es ohne Schalter ohnehin auf
Taste an. Kanäle mit Ch. legt `flotte-vorlage` (Vorlage Star Citizen) an, erst wenn alle Clients 0.6.0 haben (Versionstor,
HEIMSERVER-PROXMOX.md, Abschnitt 18).

Eigentümer und erster Admin (3.9, 3.10): `funk-admin eigentuemer --konto <id>` für das eigene ADMIN-Konto, dann
`admin-vorsehen --konto <id>`, im Client mit Verwaltungsteil "Verwaltung einrichten" (Antrag), die Kennung aus
dem Client mit `admin-ernennen --konto <id> --kennung <Kennung>` eintragen, im Client Geheimnis abholen, in der
Authenticator-App scannen und den ersten Code eingeben. Solange der Verwaltungsteil fehlt: `admin-einrichtung-neu
--konto <id> --anzeigen`, den Schlüssel in der App von Hand eingeben und `zweitfaktor-bestaetigen --konto <id>
--code <Code>`. Die Notfallcodes getrennt vom Telefon aufbewahren. Beim Start warnt der Server, wenn kein
Eigentümer gesetzt ist oder `verwaltungsUids` nicht leer ist, und wenn zwei Konten aus einem älteren Stand denselben
Discord-Namen tragen (ohne Klarnamen; `konten` zeigt sie). Ein ADMIN-Konto aus 0.1.0 (mit Code verknüpft) hat noch
keine SC-Namen und keinen Discord-Namen; die SC-Namen setzt `scnamen --konto <id> --namen <Name>`, den Discord-Namen
`discordname --konto <id> --name "<Name>"`. Schritt für Schritt: `HEIMSERVER-PROXMOX.md`, Abschnitt 10 (neu) und 14
(Update von 0.1.0).

## Heimbetrieb im LXC (7.6)

- Der LXC bekommt eine feste IPv4, im Container selbst eingetragen und außerhalb des DHCP-Bereichs der FRITZ!Box
  (`HEIMSERVER-PROXMOX.md`, Abschnitte 0 bis 2). Genau diese Adresse steht in `binden`; TCP und UDP binden sie beide.
- Am Router genau zwei Weiterleitungen auf den LXC:
  - TCP öffentlich 8443 auf den Bindport `tcpPort` (Standard 8443); 443 gehört daheim schon nginx (Foundry). Auf
    einem eigenen Server ohne nginx ginge auch 443 auf 8443, dann `oeffentlicherTcpPort` 443 und `--tcp-port 443`.
  - UDP öffentlich 47443 auf `udpPort` (Standard 47443).
  `oeffentlicherTcpPort` und `oeffentlicherUdpPort` nennen die Router-Seite; Welcome gibt den öffentlichen UDP-Port
  weiter, das Profil trägt den öffentlichen TCP-Port. UDP nicht auf 443 (QUIC-Filter in fremden Netzen). SSH bleibt
  im LAN, Port 80 wird nicht gebraucht.
- DynDNS nur mit A-Eintrag (kein AAAA, sonst meldet die FRITZ!Box ihre eigene IPv6), TTL möglichst höchstens 60 s, bis 300 s
  geht es noch.
- In der FRITZ!Box den LXC als Echtzeitanwendung priorisieren, die Zwangstrennung auf eine Uhrzeit ohne Einsätze
  legen, den Netto-Upload messen und `grenzen.ausgangMbit` auf höchstens 80 % davon setzen.
- IPv6 erst nach Klärung der Router-Freigabe (Offener Punkt 19); dann die IPv6-Adresse zusätzlich in `binden`.
- Ein Vorposten (VPS mit WireGuard) muss die Absenderadressen erhalten (nur DNAT); seine Adresse gehört dann nach
  `vertrauenswuerdigeWeiterleiter`, sonst liefen die Grenzen je Präfix und die Pfadprüfung ins Leere.
- Die Heim-IP sehen alle Mitglieder; gegen volumetrische Angriffe hilft nur der Umzug oder ein Vorposten
  (Offener Punkt 6).

## Sperrliste

Ab 0.3.0 sperrt funk-server Adressen, die sich wie Scanner, Botnetze oder Durchprobierer benehmen, befristet und selbst;
dafür ist nichts einzurichten. Das ist der Hauptschutz des Funks. Härtung von nginx, FRITZ!Box, Proxmox und SSH sowie
fail2ban als Zusatz: `HEIMSERVER-HAERTUNG.md`.

- **Punkte je Präfix** (IPv4-Adresse, IPv6-/64), in 10 Minuten zusammengezählt: gescheiterter oder abgebrochener
  TLS-Handshake 1 (Scanner, Browser, ein Client, der den Pin ablehnt; das sieht der Server gleich), leerer Handshake-Topf
  des Präfixes 1, Handshake ohne Gerätezertifikat oder ALPN 2, Protokollmüll vor der Anmeldung oder kein Hello in der
  Frist 3, Antrag oder Registrierungscode über der Grenze (3 je Verbindung, 10 je Präfix und Stunde) 3, gesprengte
  Ratengrenzen eines unfreigegebenen Geräts 5. Ab 10 Punkten ist das Präfix gesperrt.
- **Wirkung:** Die TLS-Annahme schließt Verbindungen gesperrter Präfixe gleich nach `accept` mit RST, vor TLS und vor
  den Töpfen; das kostet fast nichts. UDP ohne Sitzung verwirft der Server ohnehin vor jeder Kryptografie (5.9).
- **Dauer:** 5 min, dann je weiterer Sperre sechsmal so lang (30 min, 3 h, 18 h), höchstens 24 h. Nach 48 h ohne Sperre
  beginnt eine Adresse wieder bei 5 min. Sind 4 /64 aus demselben /48 gesperrt oder sammeln seine /64 zusammen 40 Punkte
  in 10 min, gilt eine Sammelsperre für das /48 (Kennung `g6#...`), gegen Angreifer, die ihr /64 wechseln, auch je
  Verbindung. Für ein Mitglied im selben /48 gilt sie nicht, wenn sein /64 in der Vorrangspur steht; sonst kommt es über den
  Durchlass herein und hebt sie auf.
- **Globale Grenzen sind kein Verstoß:** Ist die Tabelle der Code- und Antragszähler voll (4096 Präfixe je Stunde, Alarm
  `REGISTRIERUNG_FLUT`), bekommt jedes neue Präfix `GRENZE`, aber keine Punkte; sonst sperrte eine Flut aus vielen Präfixen
  neue Mitglieder gleich mit aus.
- **Uhr:** Die Liste rechnet mit der Systemuhr, damit Sperren einen Neustart überstehen. Springt die Uhr zurück, gilt eine
  Sperre höchstens ihre Dauer ab dem Sprung, der Durchlass bleibt, und das Fenster der Punkte beginnt neu.
- **Nie gesperrt:** Präfixe der Vorrangspur (in den letzten 14 Tagen eine AKTIV-Sitzung von dort: Familie, gemeinsames
  NAT, die eigene Heimadresse), `vertrauenswuerdigeWeiterleiter` und `sperrliste.ausnahmen`; dort zählen nicht einmal
  Punkte. Ein freigegebenes Gerät hebt nach dem Handshake die Sperre seiner Adresse auf (Logbuch `Sperrliste: Sperre für
  p4#... aufgehoben (freigegebenes Gerät)`). Damit es hinter einer gesperrten Adresse überhaupt bis dahin kommt (CGNAT,
  ein Mitglied an einer neuen Adresse, die ein Angreifer teilt), lässt die Sperre alle 30 s eine Verbindung bis zum
  Handshake durch. Klopft der Angreifer genau dann an, nimmt er den Durchlass; dann hilft `entsperren --ip`.
- **Speicher:** höchstens 10.000 Präfixe; ist die Liste voll, fallen zuerst ungesperrte, lange ruhige heraus, Sperren
  nie.
- **Neustart:** Sperren und Stufen stehen in `sperrliste.json` im Zustandsverzeichnis und überstehen Neustart und
  Update, damit ein Angreifer, der einen Neustart abwartet oder auslöst, nicht von vorn beginnt. Gespeichert werden nur
  HMAC-Werte (Schlüssel aus dem Betriebsgeheimnis, wie die Vorrangspur) und die Kennung des Logbuchs, nie eine Adresse.
  Eine Sperre, die weiter als 24 h in die Zukunft reicht (verstellte Uhr, bearbeitete Datei), gilt beim Laden nur 24 h.
  Die Datei gehört nicht in die Sicherung.
- **Logbuch:** je neuer Sperre `WARNUNG Sperrliste: p4#... für 5 min gesperrt (Stufe 1, Anlass HANDSHAKE, 10 Punkte)`,
  abgewiesene Verbindungen nur mit `log.stufe` DEBUG. Wie überall nur die Kennung, nie die Adresse.
- **fail2ban** kann mit HMAC-Werten nichts anfangen. Dafür gibt es, Voreinstellung aus, `sperrliste.klartextDatei`: je
  neuer Sperre eine Zeile in fester Form mit Klartext-IP in eine eigene Datei, etwa `/var/log/funk/sperren.log` (die
  Unit legt `/var/log/funk` mit `LogsDirectory=funk` an), nie ins Logbuch. funk-server legt die Datei schon beim Start an,
  leer und ohne etwas abzuschneiden, denn fail2ban startet mit einem Jail ohne Datei gar nicht; geht das nicht, steht
  gleich beim Start `Sperrliste: Klartextdatei nicht schreibbar` im Logbuch. Filter und Jail-Beispiel liegen in
  `konfig/fail2ban/`; das Jail sperrt 5 min ohne Steigerung, so lange wie die erste Sperre des Servers, denn am Host gibt
  es keinen Durchlass für Mitglieder hinter derselben Adresse, und die Sperre trifft dort alle Ports, auch Foundry. Für den
  Heimbetrieb ist es nicht empfohlen (`HEIMSERVER-HAERTUNG.md`, Punkt 7.2). fail2ban liefe auf dem Proxmox-Host, denn
  im unprivilegierten LXC kann es keine Firewallregeln setzen; Einzelheiten in `HEIMSERVER-HAERTUNG.md`, Punkt 7.
- **Konsole:** `sperrliste` zeigt die Sperren, `entsperren --eintrag <kennung>`, `--ip <adresse>` oder `--alle` hebt sie
  auf.

## Gerätesperre aufheben

Seit 0.8.0. Gemeint ist die Sperre eines PCs (`sperren`, Status ENTZOGEN), nicht die Sperrliste der Adressen oben. Eine
Gerätesperre setzt nur ein Admin bewusst; aufheben lässt sie sich mit denselben Rechten:

- **Im Verwaltungsteil:** Mitglieder, Karte des Kontos öffnen. Unter "Geräte" steht jedes Gerät mit Kennung und Zustand;
  an einem gesperrten "Sperre aufheben ...", dann eine Rückfrage und der frische Code aus der Authenticator-App. Bei
  Admin-Konten und beim Eigentümer steht dort nur der Hinweis auf die Konsole, wie beim Sperren.
- **An der Konsole:** `funk-admin sperre-aufheben <anzeige>` mit der Gerätekennung, die der PC auf seiner Startseite zeigt,
  oder dem vollen Fingerabdruck. Das geht auch, wenn das Konto nach dem Sperren gelöscht wurde und es kein Gerät mehr
  dazu gibt; die Kennung steht dann im Journal unter `GERAET_ENTZOGEN`.
- **Danach:** Das Gerät gilt als ENTFERNT, nicht als freigegeben. Der PC kann einen neuen Antrag stellen; er erscheint
  unter Anträge und braucht wieder eine Freigabe. Das Journal vermerkt `SPERRE_AUFGEHOBEN` mit Gerät, Konto und wer.
- Ein zweites Mal antwortet die Konsole `Gerät ist nicht gesperrt`, der Verwaltungsteil `Das Gerät ist nicht mehr
  gesperrt. Neu laden.`

## Laufender Betrieb

- Geordneter Neustart: `systemctl restart funk-server`. Auf SIGTERM geht `Bye{WARTUNG, wiederInMs: 10000}` an alle
  Verbindungen, auch in REGISTRIERUNG und WARTERAUM (7.2); die Clients kommen über die Vorrangspur zurück. Konten, Geräte, Kanäle, Plätze, Mitgliedschaften,
  Stummschaltungen, Aufstellungen, Pegel, Ablage, Sendeauswahl und die Vorrangspur überstehen den Neustart;
  Sitzungen, Schlüssel, Zähler und Einmalcodes nicht. Innerhalb von 10 min kommen die Mitgliedschaften als
  WIEDERHERGESTELLT zurück.
- Logbuch: eine Zeile je Eintrag mit UTC-Zeit und Stufe. IP-Adressen, Präfixe und Konto-IDs erscheinen nur als
  gekürzter HMAC-Wert (`ip#...`, `k#...`); Schlüssel, IVs, Codes und Opus nie. Alle 10 s eine Messwertzeile je
  aktiver Sitzung.
- Überlast (7.4): Stufe 2 (Verarbeitung im Median über 10 ms) sperrt neue Registrierungen und unfreigegebene
  Verbindungen (`SERVER_VOLL`); freigegebene Geräte kommen immer herein (bis 100 Sitzungen). `ServerLast` geht an alle
  mit `ZUWEISEN`: Admins und ab 0.3.0 die Einsatzleitung mit Auftrag (bis 0.2.0 auch die Stufe RAIDLEITER).
- Warnung `Vorfilter voll`: mehr als 65.536 fremde Präfixe schicken Datagramme mit gültiger Sitzungs-ID, meist
  gefälschte Quelladressen. Neue Präfixe gehen dann ohne Vorfilter weiter, damit Erstbindung und NAT-Rebinding der
  Mitglieder nicht ausfallen; die Budgets je Sitzung (5.9 Schritt 4) begrenzen die Arbeit.
- Rotation der UDP-Schlüssel alle 60 min oder nach 2^20 Paketen je Richtung, auf Anforderung des Clients
  (KeyRotation) und bei Angriffsverdacht (mehr als 2^16 falsche Tags).
- Sicherung: `funk-admin sicherung`, danach das Sicherungsverzeichnis wegkopieren. `$CREDENTIALS_DIRECTORY` gehört
  nicht in die Sicherung. Sie enthält `admins.json`, `mitglieder.json` und `journal.json`; die TOTP-Geheimnisse darin
  sind nur mit `funk-totp-schluessel` zu öffnen, das Journal trägt aber Klarnamen. Eine Container-Sicherung des LXC
  (Proxmox vzdump) enthält `/etc/funk/credentials` mit: solche Sicherungen nur verschlüsselt ablegen (T14).
  Zurückspielen: Dienste stoppen, die Dateien der Sicherung nach `/var/lib/funk` kopieren (Dateien, die dort liegen, aber
  in der Sicherung fehlen, vorher wegnehmen), `chown funk:funk`, `chmod 0600`, starten; Schritt für Schritt in
  `HEIMSERVER-PROXMOX.md`, Abschnitt 12. `sicherung` schreibt das Journal vorher immer sofort, auch wenn gerade ein
  Schreiben im Hintergrund läuft.
- Uhrzeit: TOTP verträgt nur einen Zeitschritt (30 s) Abweichung. Zeitsynchronisierung am Host (systemd-timesyncd
  oder chrony) ist Pflicht; meldet ein Admin "Uhr des Telefons prüfen", liegt es meist am Telefon. Ab 0.3.0 prüft der
  Server beim Start, ob die Uhr offensichtlich falsch geht (vor dem Erscheinen der Fassung, oder der jüngste
  Journaleintrag liegt mehr als 5 min in der Zukunft), und warnt im Logbuch. Ob der Host synchronisiert, liest im Dienst
  niemand: `ProtectClock=true` sperrt auch das Lesen über `adjtimex`. `funk-server pruefen` in der Shell liest es und
  zeigt `Uhr: synchronisiert (Kernel, höchstens ... ms Abweichung).` oder warnt mit `adjtimex TIME_ERROR`; der Container
  teilt die Uhr des Hosts.
- Journal: Die Zentrale übergibt den Stand höchstens einmal je Sekunde und schreibt ihn ab 0.3.0 im eigenen Faden;
  Schreibvorgänge laufen nacheinander, ein älterer Stand überschreibt nie einen neueren. Ein Schreibfehler steht als
  `FEHLER Journal, Speichern` im Logbuch, der nächste Versuch folgt.
- Admin-Sitzungen enden nach 12 h (seit 0.9.1 nicht mehr nach 30 min ohne Verwaltungshandlung), mit der Verbindung und mit jedem Neustart.
  Warnungen (Admin-Sitzung von einem neuen Gerät, Zweitfaktor gesperrt, Notfallcode benutzt, Admin geändert) gehen an
  jede Sitzung eines ADMIN-Kontos; alles Verwaltende nur an Admin-Sitzungen.

## Update auf 0.13.0

Die erste Fassung als JG TacRelay (Sarevoc 18./19.10.2026, Q7 und Q14), ohne Übergang: Server und Clients werden gemeinsam neu
aufgesetzt, der alte Server-Stand wird nicht übernommen. Bis 0.12.0 hieß das Programm anders, und alle Kennungen sind neu.
Ein Server bis 0.12.0 lässt sich deshalb nicht auf 0.13.0 aktualisieren, und kein Client 0.13.0 verbindet sich mit ihm. Die
Abschnitte "Update auf ..." darunter führen nicht zu 0.13.0; sie bleiben als Geschichte stehen.

- Neue Kennungen: ALPN `tacrelay/1` (auch in einem nginx davor, falls es Verbindungen am Protokollnamen sortiert,
  `HEIMSERVER-PROXMOX.md`, Abschnitt 8), die Präfixe der Signaturen und der Schlüsselableitung (HKDF), die Anfragezeile
  `TR-PROFILANFRAGE`, das Umzugspaket `.tru`, der fail2ban-Filter und das Jail `tacrelay` (`konfig/fail2ban`,
  `HEIMSERVER-HAERTUNG.md`, Punkt 7.2). Ein Jail unter dem alten Namen auf dem Host vorher entfernen, sonst sucht es nach
  einer Datei, die es nicht mehr gibt, und fail2ban startet nicht.
- Neu aufsetzen: den alten Dienst stoppen, Zustand, Konfiguration und Schlüssel des alten Servers entfernen (`/var/lib/funk`,
  `/etc/funk` samt `credentials`), dann einrichten wie einen neuen Server, mit `EINRICHTEN.md` oder von Hand nach
  `HEIMSERVER-PROXMOX.md`, Abschnitte 4 bis 13. `funk-einrichten.sh` ersetzt keine vorhandene `server.json` und keinen
  vorhandenen Schlüssel; was vom alten Server bleibt, nähme es also weiter. Pfade, Dienstbenutzer und die Namen der Units
  (`funk-server`, `funk-echo`) bleiben; in den Units ändert sich nur die Beschreibung.
- Serverprofil neu signieren: Die Signatur über das alte Präfix gilt nicht mehr, auch mit demselben `profil.key` nicht.
  `profil-erneuern` lehnt das alte Profil deshalb ab (`Das bisherige Profil ist nicht mit diesem profil.key unterschrieben
  ...`, am Code gelesen). Am Stick erst den ganzen Ordner `linux` des Pakets 0.13.0 nach `funk-offline`, dann das Profil von
  Hand wie unter Erstinstallation, Schritt 5 (`profil-schreiben`, `profil-signieren`, `profil-pruefen`), mit dem Pin des
  neuen Servers aus seiner Anfragezeile und dem Pin der Reserve. Der Profilschlüssel selbst bleibt. Das Profil kommt nach
  `/etc/funk/profil.json` (mit dem Skript `funk-einrichten.sh --profil profil.json`) und nach `konfig\profil.json` im
  Repository; erst damit gebaute Setups verbinden mit dem Server.
- Geräte neu freigeben: Kein Konto, kein Gerät, keine Freigabe und keine Flotte kommt mit. Der erste Admin wie auf einem
  frischen Server mit `erster-admin` (`EINRICHTEN.md`, Abschnitt 4), alle anderen stellen mit dem Setup 0.13.0 einen
  neuen Antrag (`antraege`, `freigeben`). Kanäle und Rollen neu, etwa mit `flotte-vorlage` (Vorlage Star Citizen).
- Admin-Nachweis und Zweitfaktor (TOTP) neu: Die Schlüssel im TPM heißen jetzt `JG-TacRelay-Geraet` und
  `JG-TacRelay-Admin`, der Herausgeber in der Authenticator-App `JG TacRelay`. Jeder Admin richtet die Verwaltung neu ein
  (`admin-vorsehen`, im Client "Verwaltung einrichten", `admin-ernennen`, Geheimnis abholen, erster Code, zehn neue
  Notfallcodes; Handbuch B10.2 und B10.3) und löscht danach den Eintrag unter dem alten Namen in der App.
- Clients: Das alte Programm deinstallieren; sein Datenordner unter `%LOCALAPPDATA%` bleibt dabei stehen und gehört von
  Hand gelöscht. JG TacRelay legt seine Einstellungen neu in `%LOCALAPPDATA%\JG-TacRelay` an und übernimmt nichts daraus.
  Mitglieder bekommen das Setup 0.13.0, Admins das Admin-Setup 0.13.0.

## Update auf 0.12.0

Das Meldesystem kommt dazu (Sarevoc 11.10. und 16.10.2026): Führung, Admins und Einsatzleitung mit laufendem Auftrag schicken Befehle,
Zustände, Ampel, Countdown und eine Bereit-Abfrage als Grafik an alle, die online sind, aber nur nach Ernennung durch einen Admin. Wie
0.11.8: erst `sicherung`, dann der Server, dann die Setups für alle und das Admin-Setup. Kein neues Feld in server.json, keine Units,
kein neuer Wert in lage.json.

- Ernennen: Haken "darf Meldungen senden" auf der Mitgliederkarte der Verwaltung (Admin-Sitzung), im Journal als `MELDUNGEN`. Er steht
  in `mitglieder.json` (`meldungenSenden` je Mitglied) und bleibt, bis ein Admin ihn wegnimmt. Bei einem Funker bleibt er stehen, wirkt
  aber nicht; wer "Heute als Funker" spielt, sendet nicht. Eine `mitglieder.json` von 0.11.9 lädt ohne Haken.
- Was gerade läuft (Zustand, Ampel, Countdown, offene Bereit-Abfrage), hält der Server nur im Speicher: Nach einem Neustart ist es leer,
  und jeder, der neu verbindet, bekommt den laufenden Stand. Je Sender geht höchstens eine Meldung in 2 s, eine Bereit-Abfrage endet
  spätestens nach 2 min. Die Zeiten des Countdowns sind Serverzeit; die Clients gleichen ihre Uhr über Ping ab. Die Zeitsynchronisierung
  am Host bleibt Pflicht (siehe Laufender Betrieb, Uhrzeit; `funk-server pruefen` zeigt sie).
- Clients bis 0.11.9 bleiben verbunden und sehen keine Meldungen. Ein Client 0.12.0 schickt Meldungen nur an einen Server ab 0.12.0.
- Rückweg auf 0.11.9 mit dessen Serverpaket: Er startet auf der `mitglieder.json` von 0.12.0, schreibt die Haken beim nächsten Speichern
  aber nicht zurück; nach einem neuen Update auf 0.12.0 sind sie neu zu setzen.

## Update auf 0.11.8

Untergilden entfallen (Sarevoc: "Untergilden entfallen ganz, überall"). Wie 0.11.0: erst `sicherung`, dann der Server, dann die
Setups für alle und das Admin-Setup. Kein neues Feld in server.json, keine Units, kein neuer Wert in lage.json.

- 0x001D Untergilde bleibt als Hülle: LISTE gibt eine leere Liste, SETZEN und LOESCHEN antworten `UNGUELTIG` mit "Untergilden
  gibt es seit 0.11.8 nicht mehr". Ältere Clients werden so nicht getrennt; sie zeigen keine Kürzel mehr, ihre Verwaltung eine
  leere Seite Untergilden.
- `untergilden` in `mitglieder.json` bleibt und wird weiter geschrieben, aber nie gelesen oder gezeigt. Verändert wird es nur
  beim Löschen eines Kontos und beim Wegfall einer Vormerkung (gelöscht oder bei der Freigabe übernommen): Deren Nummer fällt
  aus den Mitgliederlisten heraus, die eines Kontos auch als Ansprechpartner (Datenschutz). Nicht von Hand entfernen: Ein
  Server bis 0.11.7 bricht beim Laden ab, wenn das Feld fehlt. Der Rückweg auf 0.11.7 geht deshalb mit dessen Serverpaket;
  die alten Untergilden sind dann wieder da, ohne die inzwischen gelöschten Konten und Vormerkungen.
- Liste einfügen: Spalte 3 wird übergangen, mit einem Hinweis je Liste; eine unbekannte Untergilde ist kein Zeilenfehler mehr.

## Update auf 0.11.0

Wie 0.9.1: erst `sicherung`, dann der Server, dann die Setups für alle und das Admin-Setup (`HEIMSERVER-PROXMOX.md`,
Abschnitt 13). Kein neues Feld in server.json, keine Units, kein neuer Wert in lage.json; der Rückweg auf 0.10.0 geht mit
dessen Serverpaket. Was sich am Server ändert (Sarevoc, 12.10.2026, Zuordnung):

- Wer offline von der Einsatzleitung eingeteilt wurde, kommt beim Anmelden als VERSCHIEBEN mit `durch` zurück, nicht mehr
  als WIEDERHERGESTELLT. Auch Clients 0.9.x und 0.10.0 zeigen dann ", eingeteilt von ...". Hat der Funker danach selbst
  einen Ch. oder eine Rolle gewählt, bleibt seine Quelle (selbst gewählt).
- Stellt die Einsatzleitung im Brett den Ch. eines Felds auf Ruhe ein ("Ch."), wird das Feld Sendeziel, solange die Person
  noch nirgends hin sendet; wie beim eigenen Ch. seit 0.9.1.
- "Zurück auf Standard" (auch "Abend beenden" am Brett) setzt keine Reservierung mehr; der Funker kommt beim nächsten Login
  ohne Einteilung und auf seiner gemerkten Rolle.
- Ein Konto, das sich nie angemeldet hat, belegt nach Ablauf seiner Reservierung keinen inzwischen vergebenen Platz mehr
  (vorher konnten zwei auf einem Platz mit Kapazität 1 sitzen).
- Untergilde SETZEN ohne Stammeinheit oder Ansprechpartner behält die gespeicherten Werte (vorher löschte jedes Speichern
  aus dem Verwaltungsteil beide). War bei dir schon etwas gelöscht, trag es einmal neu ein.

Im Client: Personen als Kacheln in "Wer ist da" und links im Brett, "Ch." je Feld im Detail des Bretts, die Ansage "Für
heute eingeteilt von ..." beim Login und die gemerkte eigene Rolle, die eine Einteilung durch andere übersteht
(`tools\paket\LIESMICH.txt`, "Neu in 0.11.0").

## Update auf 0.9.1

Wie 0.9.0: erst `sicherung`, dann der Server, dann die Setups (`HEIMSERVER-PROXMOX.md`, Abschnitt 13). Kein neues Feld, keine Units.
Was sich ändert (Sarevoc, 08.10.2026):

- Nach der Rollenwahl steht alles auf Ruhe. Wählt der Funker dann in einem Feld einen Ch., wird dieses Feld Sendeziel, wenn
  noch keins gewählt ist. Bis 0.9.0 ging erst nach einem Klick auf das Feld etwas hinaus.
- Command sendet wie alle anderen Kanäle mit Sprachaktivierung (Q53). Das gilt für neu angelegte Vorlagen. **Auf einem Server,
  der die alte Kanalvorlage schon trägt,** stellt der Admin es einmal selbst um: Verwaltung, Funk, Kanäle, "Command", die Schalter
  "Nur mit Sprechtaste" und "Voreinstellung Sprechtaste statt Sprachaktivierung" aus, dann "Übernehmen".
- Die Ch.-Listen im Funkfeld stehen ganz da, ohne Scrollen (bis 0.9.0 sah man nur bis Ch. 14).
- Die Admin-Sitzung endet nicht mehr nach 30 Minuten ohne Verwaltung, sondern hält bis 12 Stunden (oder bis Trennen und
  "Verwaltung schließen"); beim Beenden des Programms mit offener Admin-Sitzung kommt eine Rückfrage (Q54).
- Der Verwaltungsteil zeigt seine Navigation als Baum mit Ordnern (Heute, Leute, Funk, Einsatz, Werkzeuge).
- Clients 0.9.0 an einem Server 0.9.1 wählen das erste Sendeziel ebenfalls von selbst (das macht der Server); ein Client 0.9.1
  an einem Server 0.9.0 nicht.

## Update auf 0.9.0

Von 0.8.1 wie jedes Update ohne neuen Schlüssel: `HEIMSERVER-PROXMOX.md`, Abschnitt 13, erst `sicherung`, dann der Server,
dann die Setups. Am Server ändert sich nichts außer der Fassung: kein neues Feld, kein neuer Befehl, kein anderes Verhalten.
Was sich ändert, steckt im Client (Sarevoc, 06.10.2026, Q48 bis Q52):

- Auf der Seite Audio steht die Karte "Klangmodule" mit zwei Spalten Senden und Empfangen. Jedes Modul hat einen
  Schiebeschalter und einen ausführlichen Hovertext: Echounterdrückung, Echoschutz, Rauschunterdrückung (Standard oder Stark),
  Automatische Lautstärke, Sprachaktivierung, Mindest-Funklautstärke, Stimmerkennung (gesperrt bis Klangstufe D), Auto
  Dauersend-Dämpfung und Pegelausgleich. Sie ersetzt die Karten Verarbeitung und Auto Dauersend-Dämpfung.
- Ab Werk ist alles an wie bisher; danach bleibt jeder Schalter so, wie der Funker ihn zuletzt gestellt hat. Ältere
  Einstellungsdateien lesen sich ohne Verlust, die neuen Schalter sind dann an.
- Neu schaltbar: Echoschutz (aus mit Warnung, nur mit Headset sinnvoll), Sprachaktivierung (aus: jeder Kanal sendet nur mit Taste,
  die VA-Schalter in Funkfeld und Meine Kanäle sind gesperrt), Mindest-Funklautstärke und Pegelausgleich. Der Pegelausgleich hängt
  nicht mehr an der Auto Dauersend-Dämpfung.
- Clients 0.8.x und 0.9.0 arbeiten an Servern 0.8.1 und 0.9.0 gleich; ein Rückweg des Servers auf 0.8.1 ist ohne Weiteres möglich.

## Update auf 0.8.1

Von 0.8.0 wie jedes Update ohne neuen Schlüssel: `HEIMSERVER-PROXMOX.md`, Abschnitt 13, erst `sicherung`, dann der Server,
dann die Setups. Kein neues Feld in `server.json` oder `lage.json`, die Units bleiben. Was sich ändert (Sarevoc, 06.10.2026, Q44
bis Q46):

- Im Funkfeld ist der Feldname eine feste Beschriftung. Welche Kanäle in den Feldern liegen, bestimmt die Rolle oben links;
  ändern können das der Admin über die Rolle und die Einsatzleitung über das Brett. Der Funker wählt je Feld nur den Ch.,
  auch in einem festen Feld; gesperrt ist der Ch. nur bei einer Kanalsperre und bei Global.
- Der Server setzt das durch: `FeldSetzen` erlaubt selbst nur den Ch.-Wechsel und ein offenes Netz in ein freies Feld;
  "Wie letztes Mal" bringt keine Kanäle mehr in die Felder. Der Echo-Funker trägt sich weiter selbst ins offene Echo-Netz
  ein, an seiner Einrichtung ändert sich nichts.
- Nach der Rollenwahl beginnt jeder Ch. auf Ruhe; der Client merkt sich keine Ch. mehr. Was eine Zuordnung (Einheit vom
  Brett) vorgibt, gilt wie bisher.
- Clients 0.8.0 bleiben am Server 0.8.1 verbunden. Ihre Kanalwahl am Namen bekommt die Antwort "Dafür fehlt das Recht."
  bzw. "Festes Feld", ihre gemerkten Ch. wirken nicht mehr. Ein Client 0.8.1 an einem Server 0.8.0 funktioniert; nur der
  Ch.-Wechsel in einem festen Feld wird dort mit "Festes Feld" abgelehnt.
- Rückweg: Am Format der Zustandsdateien ändert sich nichts; ein Server 0.8.0 liest den Stand von 0.8.1 unverändert. Hin- und
  Rückweg misst der lokale Lauf.

## Update auf 0.8.0

Von 0.7.x wie jedes Update ohne neuen Schlüssel: `HEIMSERVER-PROXMOX.md`, Abschnitt 13, erst `sicherung`, dann der Server,
dann die Setups. Kein neues Feld in `server.json`, die Units bleiben. Was sich ändert:

- Gerätesperre aufheben (Abschnitt oben): neuer Konsolenbefehl `sperre-aufheben`, im Verwaltungsteil auf der
  Mitgliederkarte. Am Draht nur ein neuer Wert in einer Anfrage (`GeraetEntscheiden` mit `ENTSPERREN`).
- Umbau der Verwaltung (nur Admin-Setup): links Heute, Leute, Funk, Einsatz und Werkzeuge, der Entwurf als Leiste unten,
  Rollen mit dem Funkfeld-Editor. Dazu tragen Kanäle und Rollen ein Herkunftsschild: das optionale Feld `herkunft` in
  `lage.json` und am Draht (der Name der Vorlage, damals der beiden alten Vorlagen, seit 19.10.2026 "Star Citizen", dazu
  "Baustein Trupp", "Baustein Orga", "von Hand"). Eine vorhandene Flotte bleibt, wie sie ist; die Verwaltung zeigt bei ihr
  eine abgeleitete Herkunft ("alte Flotte" oder den Namen der alten Kanalvorlage), bis ein Kanal oder eine Rolle neu
  geschrieben wird.
- Clients und Admin-Setups 0.7.x bleiben am Server 0.8.0 verbunden und funktionieren wie bisher; nur der Knopf "Sperre
  aufheben" fehlt ihnen. Ein Admin-Setup 0.8.0 an einem Server 0.7.x zeigt statt des Knopfs den Satz, dass es erst ab
  Server 0.8.0 geht; er schickt dann nichts. Ändert ein Admin-Setup 0.7.x die Flotte, behält der Server 0.8.0 die
  gespeicherte Herkunft; ein Admin-Setup 0.8.0 an einem Server 0.7.x schickt sie mit, der Server übergeht sie, und der
  Entwurf zählt das nicht als offene Änderung.
- Behoben: Ein wartender PC konnte direkt nach der Freigabe selten einmal mit `UNBEKANNT` getrennt werden (Rennen im Server
  zwischen Freigabe und Sitzungsstart); er verband sich dann neu. Seit 0.8.0 nicht mehr.
- Rückweg: Ein Server 0.7.x liest `lage.json` von 0.8.0 weiter und übergeht `herkunft`; beim nächsten Schreiben fällt das
  Feld weg, die Flotte selbst bleibt. Sonst ändert sich am Format der Zustandsdateien nichts. Zweite Linie ist die
  Sicherung von vor dem Update. Hin- und Rückweg sind noch nicht gemessen; das gehört in den lokalen Lauf.

## Update auf 0.7.0

Von 0.6.x wie jedes Update ohne neuen Schlüssel: `HEIMSERVER-PROXMOX.md`, Abschnitt 13, erst `sicherung`, dann der Server,
dann die Setups. Kein neues Feld in `server.json`, die Units bleiben. Was sich ändert (Sarevoc, 04.10.2026: Löschen heißt
vollständig entfernen, nichts sperrt von selbst, ein PC ist ein Konto):

- Beim ersten Start verwirft der Server 0.7.0 einmal die Gerätesperren aus 0.6.x, zu denen kein entzogenes Gerät mehr steht
  (das sind die aus Ablehnen und Löschen), und die abgelehnten Geräte, und speichert gleich neu. Das Logbuch sagt dann
  `Umstellung auf 0.7.0: <n> alte Gerätesperren und abgelehnte Geräte verworfen; ...`, und `konten.json` trägt danach
  `nurBewussteSperren: true`. Bewusste Sperren (`sperren`) bleiben, solange das gesperrte Gerät noch an seinem Konto steht;
  war dessen Konto schon gelöscht, ist die Sperre nicht mehr zu erkennen und fällt mit weg. Welche Gerätekennungen frei
  wurden, steht je Kennung im Journal (Art `SPERRE_VERWORFEN`, `funk-admin journal`). Soll einer dieser PCs draußen bleiben,
  sperrst du ihn mit `sperren`, sobald er wieder einen Antrag stellt; vorher kennt der Server ihn nicht.
- Danach löschen `ablehnen` und "Alle ablehnen, die zu keiner Vormerkung passen" Gerät und Antragskonto, und
  `konto-loeschen` sowie "Konto löschen" im Verwaltungsteil entfernen alles; beides sperrt nichts, der PC darf neu
  beantragen. `sperren` ist die einzige Gerätesperre. Ein vergebener Discord-Name bricht die Freigabe nicht mehr ab, das
  Konto bekommt ihn nur nicht. Die IP-Sperrliste gegen Rateangriffe (Abschnitt Sperrliste) bleibt, wie sie ist.
- Am Draht ändert sich kein Feld. Clients ab 0.6.3 stellen nach einer Ablehnung keinen Antrag von selbst (`Bye{ENTFERNT}`),
  ältere verbänden sich gleich neu und stellten ihn wieder. Haben alle das Setup 0.7.0, `mindestClientVersion` auf `0.7.0`
  heben (Handbuch B6.3).
- Rückweg: Ein Server 0.6.x liest `konten.json` von 0.7.0 weiter (das neue Feld übergeht er und lässt es beim nächsten
  Schreiben weg); die verworfenen Sperren kommen damit nicht zurück. Startet danach wieder 0.7.0, läuft die Umstellung
  noch einmal nach denselben Regeln. Zweite Linie ist die Sicherung von vor dem Update.
- Gemessen am 04.10.2026 auf dem Entwicklungsrechner, ohne TLS, nur über Loopback, mit `funk-testserver` und Echo-Funker,
  mit den Programmen des Gesamtpakets 0.6.0 (20261003_1532; der Serverteil ist bis 0.6.2 derselbe) und dem Bau 0.7.0: Auf
  einem Zustand von 0.6.0 mit einem abgelehnten, einem gesperrten und einem entfernten Gerät und einem gelöschten Konto
  startete der Server 0.7.0 ohne Fehler, verwarf eine alte Sperre (Logbuchzeile mit 1, ein Eintrag `SPERRE_VERWORFEN`) und
  behielt die bewusste; das gelöschte Konto hatte unter 0.6.0 keine Sperre hinterlassen. Die Echo-Probe 0.6.0 und 0.7.0
  meldete `BESTANDEN`. Der Server 0.6.0 startete danach auf dem Zustand von 0.7.0, die Echo-Probe 0.6.0 bestand; ein
  erneuter Start mit 0.7.0 lief ohne neue Umstellungszeile und ohne Fehler.
- Bei jedem dieser Starts stand kurz `WARNUNG Laststufe 2` und etwa eine Sekunde später `Laststufe 0`, mit 0.6.0 genauso
  wie mit 0.7.0. Das ist kein Fehler des Updates; in dieser Sekunde nimmt der Server nur keine neuen Anträge an und lässt
  keine unbekannten Geräte herein, freigegebene merken davon nichts.

## Update auf 0.6.0

Von 0.5.0, Schritt für Schritt: `HEIMSERVER-PROXMOX.md`, Abschnitt 18. Das Wichtigste:

- Reihenfolge: erst der Server, dann die Setups 0.6.0 für alle, erst danach die Vorlage `flotte-vorlage --name <alte Vorlage>`
  (die alte Kanalvorlage gibt es seit dem Umbau zu JG TacRelay, 19.10.2026, nicht mehr; heute gilt `flotte-vorlage --name starcitizen`).
  Mit dem ersten Kanal mit Ch. (Kanalnetz, 6.16) gilt das Versionstor: Ein Hello unter 0.6.0 bekommt `VERSION_ZU_ALT`
  (`mindest` 0.6.0), laufende Sitzungen unter 0.6.0 trennt der Commit; `mindestClientVersion` bleibt dafür unverändert.
- Kein neuer Schlüssel, kein neues Feld in `server.json`; `funk-server.service` und `funk-echo.service` sind inhaltlich
  dieselben wie in 0.5.0.
- Neu im Zustand (`lage.json`): Kanalnetze mit `kanaele` und `gruppe`, Kanal-IDs ab 0x8000 in Feldern, die gewählte
  Rolle je Konto, Kanalsperren am Konto und die für alle im Nebenstand (6.17). `sicherung` lässt die Kanalsperren weg wie
  die Aufträge. Ein Zustand von 0.5.0 wird ohne Verlust geladen.
- Neue Nachrichten: 0x0039 RolleWaehlen, 0x003A KanalSperre; ein Client 0.6.0 schickt sie nur an einen Server ab 0.6.0.
- Gemessen auf dem Entwicklungsrechner, ohne TLS, mit den Programmen des Gesamtpakets 0.5.0 (20261002_0157) und dem Bau
  0.6.0: Der Server 0.6.0 lief auf einem Zustand von 0.5.0, `funk-echo` 0.5.0 kam nach dem Neustart wieder, die Echo-Probe
  0.5.0 und 0.6.0 meldete `BESTANDEN`. Nach `flotte-vorlage --name <alte Vorlage>` (52 Schritte) wies der Server `funk-echo` 0.5.0
  mit `VERSION_ZU_ALT` ab, `funk-echo` 0.6.0 und die Echo-Probe 0.6.0 bestanden.
- Rückweg vor der Vorlage: Der Server 0.5.0 startete auf dem Zustand von 0.6.0 (Echo-Probe `BESTANDEN`). Nach der Vorlage
  startet er erst, wenn kein Konto mehr eine Kanal-ID im Feld hat; sonst `Start gescheitert: ... Schnappschuss nicht
  stimmig: Konto <n>: Netz <id> unbekannt`. Rückweg deshalb: alle Kanäle mit Ch. löschen (`netzliste` nennt sie mit
  `Ch. 0 bis <n>`) und Global ohne Ch. setzen (`netz-setzen --id 1 --name Global`); danach fällt das Tor, und der Server
  0.5.0 startete auf dem Zustand (Echo-Probe `BESTANDEN`). Zweite Linie ist die Sicherung von kurz vor der Vorlage.
- Last mit 40 Testfunkern (`funk-testclient last --szenarien AJK`, Release, Testzugang): Jeder Funker wechselt jede Sekunde
  einen Ch. (Szenario K), 2371 Wechsel in 60 s ohne Fehlschlag, Antwort p50 3,5 ms, p99 7,3 ms, höchstens 112 ms;
  Weiterleitung p99 0,27 ms gegen 0,25 ms ohne Wechsel (J), kein Verlust; Serverprozess im Mittel 26 % eines Kerns (ohne
  Wechsel 5 %).
- Neu an der Konsole: `flotte-vorlage --name <alte Vorlage>`, `kanalsperre-setzen`, `kanalsperre-aufheben`, `kanalsperren`,
  `netz-setzen --kanaele`/`--kanaele-datei`/`--gruppe`; Ausgaben sagen Kanal statt Netz.

## Update auf 0.5.0

Von 0.4.0, Schritt für Schritt: `HEIMSERVER-PROXMOX.md`, Abschnitt 17. Das Wichtigste:

- Kein neuer Schlüssel, kein neues Feld in `server.json`. `funk-server.service` und `funk-echo.service` haben gegenüber
  0.4.0 nur eine neue Kommentarzeile ("JG TacRelay - Maintainer: Sarevoc") und bleiben liegen.
- Neu im Zustand (`lage.json`): bis zu vier Einsatzphasen als Netzbild je Rolle, die laufende Phase (6.14) und je Konto
  `alsFunkerBis` für "Heute als Funker" (6.15). Ein Zustand von 0.4.0 wird ohne Verlust geladen; ohne Phasen läuft die
  Grundstellung. Zwölf Stunden nach dem letzten Schalten fällt die Flotte auf die Grundstellung zurück, auch nach einem
  Neustart. `sicherung` lässt neben den Aufträgen jetzt auch den Funker-Modus weg, Phasen und laufende Phase bleiben darin.
- Neue Nachrichten: 0x003C Funkermodus und 0x003D PhaseSchalten, dazu der Push 0x4004. Ein Client 0.5.0 schickt sie nur an
  einen Server ab 0.5.0; ein älterer Server trennt bei einem Pflichttyp, den er nicht kennt, mit PROTOKOLL.
- Gemessen auf dem Entwicklungsrechner, ohne TLS: Der Server 0.5.0 lief auf einem Zustand von 0.4.0 mit laufender Phase 2,
  `funk-echo` 0.4.0 blieb verbunden, die Echo-Probe 0.4.0 und 0.5.0 meldete `BESTANDEN`. Rückweg: Der Server 0.4.0 startete
  auf dem Zustand von 0.5.0 (Echo-Probe `BESTANDEN`); beim ersten Schreiben fehlen dann Phasen, laufende Phase und
  `alsFunkerBis`.
- Neu: `netz-setzen` legt ohne Schalter ein Netz auf Sprachaktivierung an (Tabelle oben); gespeicherte Netze behalten ihren
  Wert. Die Ausgaben von `funk-admin` sagen "der Eigentümer" statt eines Namens, und `einrichten` fragt sechs Dinge, mit
  Router acht.

## Update auf 0.4.0

Von 0.2.0 oder 0.3.0, Schritt für Schritt: `HEIMSERVER-PROXMOX.md`, Abschnitt 16. Das Wichtigste:

- Kein neuer Schlüssel, kein neues Feld in `server.json`. `funk-server.service` ist dieselbe wie in 0.3.0 (mit
  `LogsDirectory`); wer von 0.2.0 kommt, kopiert sie neu und ruft `systemctl daemon-reload`. `funk-echo.service` liest
  die Adresse jetzt aus `/etc/funk/echo.env` (`EnvironmentFile=-`, Voreinstellung in `Environment=`); eine Unit mit der
  Adresse in `ExecStart` läuft weiter.
- Am Aufbau der Zustandsdateien hat sich gegenüber 0.3.0 nichts geändert. Gemessen auf dem Entwicklungsrechner, ohne TLS:
  Der Server 0.4.0 lief auf einem Zustand von 0.2.0, und danach liefen die Server 0.3.0 und 0.2.0 auf dem, was 0.4.0
  geschrieben hatte, je mit `BESTANDEN` der Echo-Probe.
- `funk-echo` 0.2.0 und 0.3.0 und die Echo-Probe 0.2.0 bis 0.4.0 funkten mit dem Server 0.4.0. Die Profilübernahme gibt
  es erst ab dem Client 0.4.0; `mindestClientVersion` kommt deshalb erst vor einem Umzug auf `0.4.0`.
- Neu: `profil-neu-laden` nennt die Version und warnt wie der Start, wenn der Pin des Servers im Profil fehlt oder es einen anderen TCP-Port nennt; Schlüssel
  und Geheimnisse ersetzt nur noch `--ersetzen`; die Befehle für Einrichten und Umzug (Tabelle oben).

## Update von 0.2.0 auf 0.3.0

Der Ablauf für den Heimserver Schritt für Schritt steht in `HEIMSERVER-PROXMOX.md`, Abschnitt 15. Das Wesentliche:

- Kein neuer Schlüssel, kein Pflichtfeld in `server.json`; die Sperrliste läuft mit ihren Voreinstellungen, der Abschnitt
  `"sperrliste"` darf fehlen oder nur einzelne Felder nennen. In `funk-server.service` kommt `LogsDirectory=funk` (nur
  für `sperrliste.klartextDatei`): Unit neu kopieren, `systemctl daemon-reload`. `funk-echo.service` ist unverändert.
- Erst der Server, dann die Clients. Clients 0.2.0 funken mit dem Server 0.3.0 weiter: Belegung (0x4003) ist ein
  optionaler Push, neue Felder übergehen sie. Gemessen am 01.10.2026 über den Testzugang auf Loopback, ohne TLS: Zustand
  von den Programmen 0.2.0 (b181e4b) angelegt, Server 0.3.0 darauf mit unveränderter `server.json` (`pruefen` Exit 0);
  `funk-echo` 0.2.0 und ein `funk-echo` 0.2.0 im Konto RAIDLEITER blieben verbunden, während `auftrag`, `auftraege`,
  eine Einheit TRUPP, `einstellung --selbstwahl aus --nachzuegler aus` und `auftrag-entziehen` liefen; Echo-Probe 0.2.0
  und 0.3.0 `BESTANDEN` (Stand modul/raid). Auf main nach dem Zusammenführen mit der Härtung wiederholt: `pruefen`
  (Exit 0, mit der Zeile `Uhr: ...`), Start, `funk-echo` 0.2.0 verbunden, Auftrag, Einheit, Sperre, `sperrliste` und
  `sicherung` (ohne `beauftragung` in der gesicherten `lage.json`).
- Ein Client 0.3.0 schickt 0x0038 `KontenVerzeichnis` und 0x0017 `Beauftragung` nur, wenn der Server in Welcome
  mindestens 0.3.0 meldet (`Versionen.ServerFassungFuer`), sonst antwortet er sich selbst mit `VERSION`: Ein Server
  0.2.0 würde bei diesen Pflichttypen mit `PROTOKOLL` trennen. Am Code geprüft, nicht mit den Programmen 0.2.0.
- RAIDLEITER ("Führung") hat kein `ZUWEISEN` mehr. Einteilen geht mit ADMIN, einem Einzelrecht `ZUWEISEN` oder einem
  Auftrag (`auftrag`). Eine Sperre der Selbstwahl aus 0.2.0 hat weder Urheber noch Frist und bleibt stehen, bis jemand
  `einstellung --selbstwahl an` schickt. Bei wirkender Sperre zählt das Aufgeben des eigenen Platzes als Wechsel (in
  0.2.0 ging es).
- Zustand: `lage.json` trägt ab 0.3.0 Aufträge, Art, Typ und Mindestbesetzung der Einheiten, Urheber und Frist der
  Sperre, Ersteller der Aufstellungen; Aufstellungen aus 0.2.0 bekommen beim Laden den als Ersteller, der sie zuletzt
  gespeichert hat. Neu ist `sperrliste.json`. `sicherung` schreibt `lage.json` ohne Aufträge.
- Rückweg: Der Server 0.2.0 las den von 0.3.0 geschriebenen Zustand (Auftrag, Einheit TRUPP, Sperre mit Nachzüglern)
  ohne Fehler, Konten, Netze, Rollen und Einheiten vollständig, Echo-Probe 0.2.0 `BESTANDEN`; beim nächsten Schreiben
  fehlen `beauftragung`, `art`, `mindest` und die Frist der Sperre, die Sperre selbst bleibt ohne Frist stehen. Eine
  `server.json` mit Abschnitt `"sperrliste"` lehnt 0.2.0 ab (`The JSON property 'sperrliste' could not be mapped ...`,
  Exit 2). Gemessen am 01.10.2026 mit `funk-testserver --konfig` auf Loopback (tmp\tr-030\uebergang-main).

## Update von 0.1.0 auf 0.2.0

Der Ablauf für den Heimserver Schritt für Schritt steht in `HEIMSERVER-PROXMOX.md`, Abschnitt 14. Das Wesentliche:

- Erst der Server, dann die Clients. Die neuen Anfragen des Clients 0.2.0 (`ProfilSetzen` bis `AntraegeAblehnen`,
  0x0018 bis 0x001F, und die Admin-Nachrichten 0x0090 bis 0x0094) liegen im Pflichtbereich unter 0x4000, und die
  Nebenversion blieb 0 (4.7 sähe dafür optionale Typbereiche oder eine vereinbarte Nebenversion vor). Ein Server 0.1.0
  kennt sie nicht, antwortet `PROTOKOLL` und trennt (4.2). Der Client 0.2.0 schickt `Rollenkatalog` und `Untergilde`
  schon beim Öffnen von Einstellungen und "Wer ist da".
- Ein Client 0.1.0 arbeitet mit dem Server 0.2.0: Die Nebenversion bleibt 0, er schickt nur Nachrichten aus 0.1.0, neue
  JSON-Felder übergeht er, neue Meldungsarten sind nur Zeichenketten, und neue Fehlercodes gibt es nur auf Anfragen, die
  er nicht stellt (neue Nachrichten und Verwaltung). Gemessen am 01.10.2026: `funk-testclient echo-probe` aus 0.1.0
  (5053590) über den Testzugang gegen `funk-testserver` auf dem Stand von Stufe 1 (d6bc57d), `BESTANDEN`. Wiederholt
  gegen den Endstand des Nachtbaus (8f884ae, Status `Funk-Server 0.2.0`): Echo-Probe 0.1.0 und 0.2.0 `BESTANDEN`, ein
  Client 0.1.0 in einem ADMIN-Konto blieb verbunden, während `eigentuemer`, `scnamen`, `discordname` und
  `admin-vorsehen` liefen. Gemessen ist nur der Testzugang auf Loopback, der TLS-Weg nicht (Schannel legt unter Windows
  Schlüssel im Profil ab).
- Neu am Server: das Credential `funk-totp-schluessel` (`funk-admin totp-schluessel-erzeugen`, einmal), die Zeile
  `LoadCredential=funk-totp-schluessel:...` in `funk-server.service` und das Feld `totpSchluessel` in `server.json`;
  `mitgliedskarte` ist freiwillig. `funk-echo.service` ist unverändert.
- Ein `server.json` mit `totpSchluessel` lehnt der Server 0.1.0 ab (unbekannte Felder sind ein Fehler); für einen
  Rückweg die alte Datei aufheben. Die Zustandsdateien von 0.1.0 liest 0.2.0 weiter: Konten ohne SC-Namen, ohne
  Discord-Namen und ohne Eigentümer. Dazu kommen `admins.json`, `mitglieder.json` und `journal.json`. Gemessen am
  01.10.2026 mit `funk-testserver --konfig` auf Loopback: Zustand von 0.1.0 (5053590) angelegt; mit der alten
  `server.json` enden `pruefen` und der Start von 0.2.0 mit Exitcode 2 und `totpSchluessel fehlt (neu seit 0.2.0, ...)`
  samt den drei Schritten (TOTP-Schlüssel, Feld, Unit); mit `totpSchluessel` Start, Konten, Geräte und Netze
  vollständig, `eigentuemer`, `scnamen`, `discordname` und `admin-vorsehen` gehen. Der Test `Uebergang010Tests` lädt
  einen von 0.1.0 erzeugten Zustand (`tests\Funk.Tests\Daten\zustand-0.1.0`) auch über einen Neustart. Rückweg: Der
  Server 0.1.0 mit der alten `server.json` las den von 0.2.0 (d6bc57d) geschriebenen Zustand wieder (Konten, Geräte,
  Netze; die neuen Felder übergeht er); der Nachtbau hat das gespeicherte Format nicht verändert.
- Nach dem ersten Start: `eigentuemer --konto <id>`, `scnamen --konto <id> --namen <Name>`, `discordname --konto <id>
  --name "<Name>"`, dann `admin-vorsehen`, Antrag im Verwaltungsteil, `admin-ernennen --kennung` und Zweitfaktor wie
  oben unter Eigentümer und erster Admin.
- Ab 0.2.0 meldet der Client im Hello seine echte Fassung; 0.1.0 meldete fest `0.1.0`. `mindestClientVersion` erst auf
  `0.2.0` heben, wenn alle Mitglieder das Setup 0.2.0 haben.

## Entwicklung unter Windows

```
. C:\Claude\work\JG-TacRelay\tools\dotnet-umgebung.ps1
dotnet build C:\Claude\work\JG-TacRelay\TacRelay.slnx -c Debug --disable-build-servers
dotnet run --project C:\Claude\work\JG-TacRelay\src\Funk.Server --no-build -- --konfig <server.json>
dotnet run --project C:\Claude\work\JG-TacRelay\src\Funk.Admin --no-build -- status --konfig <server.json>
```

- Schannel (Windows-TLS) kann keine flüchtigen Schlüssel benutzen ("platform does not support ephemeral keys",
  gemessen unter .NET 10.0.12). Der Server übernimmt seinen Schlüssel deshalb unter Windows für die Laufzeit in den
  Schlüsselspeicher des Benutzers (`%APPDATA%\Microsoft\Crypto\Keys`) und löscht ihn beim Beenden wieder. Unter
  Linux bleibt er im Prozessspeicher. Ein abgestürzter Prozess kann dort eine Schlüsseldatei zurücklassen.
- Der Verwaltungssocket prüft unter Windows die Gegenseite nicht; es schützen nur die Rechte des
  Zustandsverzeichnisses.
- Die Cipher-Suites aus 4.1 und `TCP_NOTSENT_LOWAT` wirken nur unter Linux.
- `tools\dotnet-umgebung.ps1` bindet `C:\Claude\tools\dev-umgebung.ps1` ein (Zonen-Cache, keine Telemetrie) und hält
  dazu Temp, CLI-Zustand, Entwicklerzertifikat und PATH des Benutzerprofils aus dem Bau heraus. Jeder dotnet-Aufruf
  gehört dahinter; `SicherungTests` prüft das für die Skripte des Repositorys.

## Tests

```
pwsh -NoProfile -File tests\testlauf.ps1 Server
```

- `tests\testlauf.ps1` bindet `tools\dotnet-umgebung.ps1` ein, baut ohne Bauserver und startet den Testlauf ohne erneutes
  Bauen; Argumente gehen als Filter an den Testlauf.
- Tests mit echten Geräten, Eingabegeräten oder Fenstern laufen nur mit `FUNK_RAUCHTEST=1` und nur, solange kein Spiel
  und keine Vollbildanwendung läuft (`Spielschutz`); startet eins während des Tests, gibt er sofort frei.

- `ServerGrundTests`: Konfiguration, Adressen und Präfixe, Zulassung (Töpfe je Präfix und /64, Vorrangspur am
  globalen Topf vorbei, Unfreigegebene je Präfix und global, Gerätesperre statt IP-Sperre, Weiterleiter),
  Vorrangspur, Schwärzung, atomare Ablage und Neustart der Zustandsdateien.
- `ServerZusammenspielTests` und `ServerPfadTests`: Server auf 127.0.0.1 mit zugeteilten Ports. Anmeldung vom
  unbekannten Gerät bis AKTIV über Registrierung, `verknuepfen` und `freigeben` am echten Verwaltungssocket;
  Ablehnung, Sperre, Entzug, Verdrängung und ERSETZT; UDP-Weg eines Pakets von A über den Server zu B mit Stempel;
  Keepalive-Echo; Müll und Replays von fremder Adresse ohne Antwort und ohne Pfadprüfung; keine Pfadprüfung auf
  Datagramme unter 48 Byte (keine Verstärkung); NAT-Rebinding mit
  Pfadprüfung; Tunnel in beide Richtungen; Rotation; Ratengrenzen mit Trennung und Gerätesperre; Zusammenfassen
  ohne Verstoß; Hello-Frist; Bye{WARTUNG}; Neustart mit WIEDERHERGESTELLT; Admin-Werkzeug als Kommandozeile.
  Diese Verbindungen gehen am TLS-Handshake vorbei über denselben Weg wie danach.
- `RobustheitTests` (Gegenprüfung): keine Pfadprüfung auf Datagramme unter 48 Byte, Tunnel mit fremder
  Sitzungs-ID trennt, Vorfilter und Zulassung unter einer Präfixflut, Bye{WARTUNG} auch in REGISTRIERUNG und
  WARTERAUM, Sende-Thread des Servers und Client-Bibliothek überleben Ausnahmen (Thread, Audio-Schicht, Ereignisse).
- `ServerTlsTests`: echtes TLS mit P-256-Client-Zertifikat, ohne Zertifikat, falsches ALPN, RSA-Zertifikat. Unter
  Linux laufen sie immer. Unter Windows nur mit `FUNK_TLS_IM_PROFIL=1`, weil Schannel dafür Schlüsseldateien im
  Benutzerprofil anlegt (siehe oben); ohne die Variable melden sie "übersprungen".
- `ServerLastTests`: kleiner Lasttest auf Loopback, nur mit `FUNK_LASTTEST=1`. Viele Sitzungen im selben Netz,
  mehrere Sprecher mit 50 Paketen/s; gemessen werden Zustellquote und Verarbeitungszeit des Relais. Größe über
  `FUNK_LAST_SITZUNGEN` (20), `FUNK_LAST_SPRECHER` (4), `FUNK_LAST_SEKUNDEN` (5) und `FUNK_LAST_MBIT` (500; Loopback
  hat kein DSL-Budget). Jedes Gerät bekommt eine eigene Loopback-Adresse 127.0.x.y, sonst griffe die Grenze von
  10 Registrierungen je Präfix und Stunde. Aufruf im Release-Build:

  ```
  $env:FUNK_LASTTEST='1'; $env:FUNK_LAST_SITZUNGEN='40'; $env:FUNK_LAST_SPRECHER='12'
  dotnet run --project tests\Funk.Tests -c Release --no-build -- Last_
  ```

- Testverzeichnis: `FUNK_TESTVERZEICHNIS`, sonst `C:\Claude\tmp\tr-servertests` bzw. das System-Temp unter Linux. Ohne
  C:\Claude bricht der Lauf unter Windows ab, statt ins Temp-Verzeichnis des Benutzers auszuweichen.
- `ClientKernTests`, `ClientZusammenspielTests`, `ClientTlsTests` (Filter `Client` bzw. `Kern`): die Client-Bibliothek
  `Funk.ClientKern` gegen den Dienst, 3 bis 6 Clients, 8.9-Stichproben Ende zu Ende, F1 (C4), Wiederverbinden nach
  geordnetem und hartem Neustart, Tunnel bei gesperrtem UDP samt Rückkehr und UDP-Ausfall, Verdrängung ohne
  automatischen Neuversuch, RFC 8305. Der TLS-Fall mit Pin läuft wie `ServerTlsTests` nur unter Linux oder mit
  `FUNK_TLS_IM_PROFIL=1`.

## Testclient und Lasttest

`funk-testclient` (Projekt `tests\Funk.Testclient`) startet N simulierte Funker auf Basis von `Funk.ClientKern` und
misst die Szenarien A bis D aus server-sicherheit.md B20. Ergebnisse und Einordnung stehen in `docs\LASTTEST.md`.
`last` ist ein Werkzeug für den Entwicklungsrechner, es startet `funk-testserver`, und den gibt es im Serverpaket
nicht. Auf dem Heimserver brauchst du nur `echo-probe`.

```
dotnet run --project tests\Funk.Testclient -c Release --no-build -- last --funker 40 --sekunden 30 --szenarien ABCD
dotnet run --project tests\Funk.Testclient -c Release --no-build -- hilfe
```

- `last` legt unter `tr-last\<zeit>\` außerhalb der Arbeitskopie (`$FUNK_TESTVERZEICHNIS`, sonst `C:\Claude\tmp`; ohne C:\Claude bricht es unter Windows ab, unter Linux das Temp-Verzeichnis) ein frisches Zustandsverzeichnis an, erzeugt Serverschlüssel, Reserve-Pin,
  Betriebsgeheimnis, Profilschlüssel und signiertes Profil offline mit `funk-admin`, startet `funk-testserver` mit
  Testzugang auf Loopback, meldet die Funker über `funk-admin verknuepfen` und `freigeben` an, legt die Kanäle mit
  `netz-setzen` an und löscht das Verzeichnis am Ende wieder.
- `lauf` arbeitet gegen einen laufenden Server: `--profil`, `--adresse`, `--port`, `--zugang tls|test`, `--admin`
  (Pfad zu `funk-admin.dll`) und `--socket` (Verwaltungssocket, also auf dem Serverrechner). Mit `--server-pid` werden
  CPU und Speicher des Serverprozesses mitgemessen.
- `funk-testserver` (Projekt `tests\Funk.Testserver`) ist nur ein Testwerkzeug: der unveränderte Dienst plus ein
  Testzugang auf Loopback, der statt TLS den Besitz des Geräteschlüssels per Signatur prüft. Grund ist dieselbe
  Schannel-Eigenschaft wie oben; im Betrieb gibt es diesen Zugang nicht.
- `echo-probe` spricht eine synthetische Phase in den Kanal Echo und prüft das Echo (vollständig, Reihenfolge, BEGINN,
  ENDE, Pause ab 500 ms); `--testzugang 127.0.0.1:<port>` oder `--profil` mit TLS. Exitcode 0 heißt bestanden.

## Echo-Funker und Probefunk

Der Echo-Funker (`Funk.ClientKern.Echo`) ist ein gewöhnlicher Teilnehmer: Er hört im Kanal Echo mit, nimmt jede
Sprechphase auf (Opus-Pakete, nicht dekodiert, höchstens 30 s, eine Phase zur Zeit) und sendet sie 500 ms nach dem Ende
als eigene Phase zurück, im 20-ms-Raster der Frame-Nr und unter den Ratengrenzen der Sitzung (6.3). Gegen Schleifen:
nie der eigene Absender, Aufnahme nur ab BEGINN oder Pre-roll, eine Sekunde Ruhe für den eben zurückgespielten
Sprecher (Rückkopplung über Lautsprecher) und kein Wiederaufnehmen der eigenen Frames (Hash, 2 min Gedächtnis).

- `funk-echo` (Projekt `src\Funk.Echo`): eigener Prozess, im Betrieb mit TLS, Pin und Schlüsseldatei
  (`--profil`, `--identitaet`, `--adresse`), in der Probe mit `--testzugang`. Unit: `konfig\funk-echo.service`.
  Er hat keine Namen und stellt keinen Antrag: Beim ersten Start holt er einen Registrierungscode, den der Admin mit
  `verknuepfen` einlöst (3.4 Weg A), dann `freigeben`. Das gilt auch am Server 0.2.0; ebenso für `funk-testclient
  echo-probe`.
  Einrichtung auf dem Heimserver: `HEIMSERVER-PROXMOX.md` (im Repository unter `docs\`, im Paket unter `linux\`),
  Abschnitt 11. Die Unit erlaubt anders als die des Servers `AF_NETLINK`, weil .NET im Client Adressänderungen
  darüber meldet. Ab 0.4.0 liest sie `--adresse` aus `/etc/funk/echo.env` (`FUNK_ECHO_ADRESSE=...`, angelegt von
  `funk-einrichten.sh` oder von Hand), sonst gilt die Zeile `Environment=` in der Unit.
- `funk-testserver --probe konfig\probefunk.json --testzugang 0 --echo`: Probefunk auf dem eigenen PC. Serverschlüssel,
  Betriebsgeheimnis und Profilschlüssel nur im Speicher, frisches Zustandsverzeichnis (`--zustand`, wird beim Beenden
  gelöscht), Kanäle, Schiffe und Rollen aus der Probe-Konfiguration. Geräte, die ihren Schlüssel über den
  Loopback-Testzugang nachweisen, gibt der Testserver ohne Handarbeit frei (dieselben Schritte wie `verknuepfen` und
  `freigeben`, die Rückfrage beantwortet er selbst). Der Dienst selbst kennt diesen Weg nicht. `--client <programm>`
  startet ein Begleitprogramm (den Client mit `--testzugang` oder `funk-testclient echo-probe`) und endet mit ihm. Den
  Client JG TacRelay startet er nur mit `--client-freigegeben` (so in `Probefunk starten.cmd`): Er zeigt Fenster,
  öffnet Mikrofon und Wiedergabe und meldet globale Tasten an. Ein langer Pfad des Verwaltungssockets weicht nach
  `C:\Claude\tmp` aus, falls es den Ordner gibt, sonst ins Temp-Verzeichnis.
- Paket für Sarevoc: `tools\paket-bauen.ps1` (Client, Probefunk, Linux-Programme, Lizenzen, MANIFEST mit SHA-256, ZIP) samt
  Prüfung ohne Fenster und ohne Audio.
