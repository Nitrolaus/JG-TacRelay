# JG-TacRelay-Server einrichten

Stand 02.10.2026, Fassung 0.5.0. Für alle, die den Funkserver von JG TacRelay auf einem eigenen Linux-Server betreiben.
Ein Befehl richtet alles ein. Beim ersten Mal fragt er sechs Dinge (acht, wenn ein Router davor steht), danach lässt er sich beliebig oft
wiederholen und ändert nur, was fehlt. Wer den Server von Grund auf in einem Proxmox-Container von Hand aufbauen will,
nimmt `HEIMSERVER-PROXMOX.md`. Zieht ein laufender Funk um, gilt zusätzlich `UMZUG.md`.

**Noch ungeprüft auf echtem Linux.** Das Skript ist auf Windows gebaut und so weit geprüft, wie es ohne Linux geht.
Bevor es auf einen Server mit anderen Diensten kommt, läuft es bei Sarevoc einmal in einem Wegwerf-Container (letzter
Abschnitt).

## Was du brauchst

- einen Rechner mit Debian 13 (für einen neuen Server die Empfehlung), Debian 12 oder Ubuntu 24.04, x86_64, mit systemd,
  und root (sudo)
- in einem Proxmox-Container: unprivilegiert mit `nesting=1` (`HEIMSERVER-PROXMOX.md`, Abschnitt 2). Ohne nesting
  startet der Dienst nicht; unter Debian 13 fallen dann laut Proxmox-Forum auch Dienste des Systems mit
  `243/CREDENTIALS` aus (hier nicht gemessen)
- einen Namen im DNS, etwa `funk.example.org`, mit einem A-Eintrag auf die öffentliche Adresse des Servers
- zwei freie Ports: TCP (auf einem Server ohne Router 443, sonst 8443) und UDP 47443
- das Serverpaket von Sarevoc, `<zeit>_JG-TacRelay-Server-<fassung>.zip`, dazu die Datei `.sha256` mit der Prüfsumme
- Sarevoc am Telefon, einmal für zwei Minuten

## 1. Paket holen

Auf dem Server, im Ordner, in dem das ZIP liegt:

```
sha256sum <zeit>_JG-TacRelay-Server-<fassung>.zip
apt install -y unzip
unzip <zeit>_JG-TacRelay-Server-<fassung>.zip
cd <zeit>_JG-TacRelay-Server-<fassung>/linux
```

**Das siehst du:** `sha256sum` zeigt dieselbe Zahl wie die Datei `.sha256` von Sarevoc. Im Ordner `linux` liegen
`funk-einrichten.sh`, die Programme (`funk-server.dll`, `funk-admin.dll` und weitere) und der Ordner `konfig`.

## 2. Ein Befehl

```
sudo bash funk-einrichten.sh
```

Bist du schon root (etwa im Container mit `pct enter`), lässt du `sudo` weg, hier und überall unten.

Zuerst bringt das Skript die .NET-Laufzeit 10 und libicu auf den Server, über apt. Gibt es die Laufzeit in deinen
Paketquellen nicht, fragt es, ob es die Paketquelle von Microsoft eintragen darf, und sonst, ob es das
Installationsskript von Microsoft nehmen darf. Dann legt es die Programme nach `/opt/funk`. Danach fragt es, und die
Eingabetaste nimmt jeweils den Vorschlag in eckigen Klammern:

| Frage | Vorschlag | Was du antwortest |
| --- | --- | --- |
| Öffentlicher Name | aus `hostname -f` | der Name im DNS, etwa `funk.example.org` |
| Adresse, an die der Server bindet | die Adresse, über die dieser Rechner ins Internet geht | meist Eingabetaste |
| Router mit Portfreigabe davor? | ja bei einer Adresse wie 192.168... oder 10... | auf einem gemieteten Server: n |
| TCP-Port am Server | 443, wenn frei, sonst 8443 | zieht der Funk von Sarevoc um: 8443 (`UMZUG.md`) |
| UDP-Port am Server | 47443 | Eingabetaste |
| Echo-Funker einrichten? | j | j, zum Prüfen |

Ist ein Port belegt, sagt es das (mit dem Programm, wenn `ss` es nennt) und fragt noch einmal. Fremde Dienste fasst
es nicht an.

**Das siehst du:** je Schritt eine Zeile mit `OK`, `NEU`, `BLEIBT` oder `FEHLT`, etwa `NEU    Benutzer funk`,
`NEU    Serverschlüssel und Zertifikat in /etc/funk/credentials, Pin XXXX-XXXX-XXXX-XXXX` und
`NEU    Kurzbefehl ... /usr/local/sbin/funk-admin`. Bei `FEHLT` steht dahinter, was zu tun ist. Dann kommt ein Block
"Firewall und Router" (unten) und am Ende:

```
WARTET AUF PROFIL
...
    TR-PROFILANFRAGE v1 funk.example.org 443 <pin>

Lies ihm am Telefon die Kennung vor: XXXX-XXXX-XXXX-XXXX, dazu Name und Port: funk.example.org, 443
```

Das ist richtig so: Der Server startet erst mit einem Profil von Sarevoc.

## 3. Profil von Sarevoc

1. Schick Sarevoc die Zeile, die mit `TR-PROFILANFRAGE` beginnt, egal wie (Discord geht). Sie ist nicht geheim. Sie liegt
   auch in `/etc/funk/profilanfrage.txt`.
2. Ruf Sarevoc an und lies ihm die Kennung vor, dazu den Namen und den Port. Er vergleicht alle drei mit dem, was sein Werkzeug
   zeigt. So weiß er, dass die Zeile von dir kommt und unterwegs niemand Name oder Port verändert hat. Die Kennung allein
   deckt nur den Schlüssel deines Servers.
3. Sarevoc schickt dir `profil.json` zurück. Leg die Datei in den Ordner `linux` und ruf auf (das Skript kopiert nur die
   Programme nach `/opt/funk`, nie das, was du daneben in den Ordner legst):

```
sudo bash funk-einrichten.sh --profil profil.json
```

**Das siehst du:** keine Frage mehr, dafür

```
NEU    Serverprofil Version 2 nach /etc/funk/profil.json (unterschrieben vom Eigentümer, XXXX-XXXX-XXXX-XXXX; Pin, Name und Port passen)
NEU    funk-server eingeschaltet und gestartet (systemctl enable --now)
OK     Funk-Server 0.5.0 bereit: 1 Adresse(n), TCP Bindport 443 (öffentlich 443), UDP Bindport 47443 ...
OK     TCP 443 und UDP 47443 hören (ss)

==== Funk-Server 0.5.0 bereit ====
...
Schutz:      eingebaute Sperrliste gegen Scanner und Fluten (den Rechner selbst härten: HEIMSERVER-HAERTUNG.md), ansehen mit sudo funk-admin sperrliste
Weiter:      JG TacRelay installieren, verbinden und auf die Freigabe warten (Antrag); dann hier: sudo funk-admin erster-admin
```

Die Sperrliste braucht keinen Eintrag in `server.json`; was sie tut, steht in `BETRIEB.md`, Abschnitt Sperrliste. Für den
Rest des Rechners (SSH, Updates, Firewall) gibt es die Checkliste `HEIMSERVER-HAERTUNG.md`, auch sie liegt im Ordner `linux`.

Lehnt das Skript das Profil ab, sagt es warum: nicht von Sarevoc unterschrieben, der Pin dieses Servers fehlt, der Name
oder der Port passt nicht, oder es ist älter als das eingespielte. Dann eingespielt wird nichts.

## 4. Erster Admin

Den ersten Admin braucht nur ein frischer Server. Bei einem Umzug kommen Konten, Admins und Eigentümer mit (`UMZUG.md`).

1. JG TacRelay mit dem Admin-Setup installieren, das Sarevoc dir gibt; es kennt das neue Profil. Namen eintragen, verbinden.
   Der Client wartet auf die Freigabe und zeigt die Gerätekennung groß.
2. Auf dem Server:

```
sudo funk-admin erster-admin
```

3. Es zeigt die offenen Anträge und fragt nach der Kennung aus deinem Client. Dann gibt es frei, macht das Konto zum
   Admin und Eigentümer und sieht es als Admin vor. Danach im Client unter Verwaltung "Verwaltung einrichten" und die
   Kennung des Admin-Schlüssels eingeben, die der Client zeigt.

**Das siehst du:** `OK: Konto <id> freigegeben`, `... Stufe ADMIN`, `... Eigentümer`, `... als Admin vorgesehen`, am
Ende die Antwort auf das Ernennen. `sudo funk-admin konten` zeigt dein Konto mit `ADMIN` und `EIGENTÜMER`.

## 5. Fertig

Der Kurzbefehl `funk-admin` setzt `--konfig /etc/funk/server.json` selbst: `sudo funk-admin konten`,
`sudo funk-admin geraete`, `sudo funk-admin admins`, `sudo funk-admin pruefen --vollstaendig`. Alle Befehle stehen in
`BETRIEB.md`.

**Firewall und Router** fasst das Skript nicht an, es zeigt nur, was nötig ist: hinter einem Router die zwei
Portfreigaben, im Proxmox-Container die Regeln für die Proxmox-Firewall. Auf einem Server mit aktivem ufw zeigt es die
zwei Regeln und trägt sie nur ein, wenn du ausdrücklich j oder ja sagst. Bei nftables zeigt es die Regeln, eintragen
musst du sie selbst.

**Noch einmal aufrufen** schadet nie: Was passt, steht als `OK` oder `BLEIBT` da, und der Dienst läuft weiter. Ein
**Update** ist derselbe Befehl im Ordner `linux` des neuen Pakets: Die alten Programme kommen nach `/opt/funk.<fassung>`,
der Dienst steht dafür kurz (es fragt vorher), dann startet er mit den neuen.

## Echo-Funker

Wenn du beim ersten Lauf j gesagt hast, zeigt der Abschluss die Befehle. Erst einen Kanal Echo anlegen, dann den
Echo-Funker einmal von Hand starten; er zeigt einen Registrierungscode und die zwei Befehle zum Freigeben:

```
sudo funk-admin netz-setzen --id 90 --name Echo --offen HOEREN_SENDEN --va
sudo runuser -u funk-echo -- /usr/bin/dotnet /opt/funk/funk-echo.dll --profil /etc/funk/profil.json --identitaet /var/lib/funk-echo/geraet.pem --adresse <adresse>
```

In einer zweiten Shell, mit dem Code aus der ersten:

```
sudo funk-admin verknuepfen --code <code> --discord 100000000000000001 --benutzername echo-funker --name Echo
sudo funk-admin freigeben <anzeigeform>
```

Dann in der ersten Shell Strg+C und noch einmal `sudo bash funk-einrichten.sh`; das startet ihn als Dienst. Die
Adresse steht in `/etc/funk/echo.env`. Mehr zum Test mit dem Echo-Funker: `HEIMSERVER-PROXMOX.md`, Abschnitt 11.

## Weitere Kanäle

Ein Kanal ohne Schalter steht auf Sprachaktivierung, wie das Echo oben (`--va` wirkt genauso). Den Kanal Kommando legst du mit `--taste`
an, dann beginnt er bei jedem auf der Sprechtaste; Global (Kanal 1) legt der Server selbst an, immer mit Taste. Die IDs sind
Beispiele, `sudo funk-admin netzliste` zeigt die belegten:

```
sudo funk-admin netz-setzen --id 2 --name Kommando --offen HOEREN_SENDEN --standard --taste
```

Eine ganze Flotte, die Vorlage Star Citizen (10 Kanäle mit Ch. von Kommando bis Allgemein und 14 Rollen), legt `sudo funk-admin
flotte-vorlage` auf einen Server ohne Kanalnetze; alle Kanäle stehen dort auf Sprachaktivierung, auch Kommando. Die Annahmen,
die sie nennt, prüft der Admin. Danach kommen nur noch Clients ab 0.6.0 hinein; auf einem neuen Server mit neuen Setups
ist das kein Hindernis.

## Wenn etwas fehlt

| Meldung | Was zu tun ist |
| --- | --- |
| `status=243/CREDENTIALS` | nennt das Skript eine fehlende Schlüsseldatei: noch einmal aufrufen, es legt fehlende an. Fehlt keine und ist es ein Proxmox-Container, wie bei 226: auf dem Host `pct set <id> --features nesting=1`, Container neu starten |
| `status=226/NAMESPACE` | nur im Proxmox-Container: auf dem Host `pct set <id> --features nesting=1`, Container neu starten |
| `Der Server lehnt die Konfiguration ab` | `/etc/funk/server.json` berichtigen; `sudo funk-admin pruefen --vollstaendig` nennt jeden Befund |
| `Die Uhr ist nicht synchronisiert` | `apt install systemd-timesyncd`; der Zweitfaktor verträgt nur 30 s Abweichung |
| `... gibt es schon, stammt aber nicht von JG TacRelay` | eine fremde Datei; das Skript lässt sie, du entscheidest |
| `/usr/bin/dotnet gibt es schon, aber ohne Laufzeit 10` | ein .NET, das womöglich einem anderen Dienst gehört; die Laufzeit 10 auf demselben Weg dazulegen, auf dem es kam, dann noch einmal aufrufen |

Eine vorhandene `/etc/funk/server.json` überschreibt das Skript nie. Soll es neu fragen, leg sie zur Seite
(`mv /etc/funk/server.json /etc/funk/server.json.alt`) und ruf es noch einmal auf.

## Was das Skript anlegt

- die Benutzer `funk` und `funk-echo` (Systembenutzer ohne Anmeldung)
- `/opt/funk` (nur die Programme, Anleitungen und `konfig` samt den fail2ban-Beispielen aus dem Paket, Besitzer root), `/etc/funk` mit `server.json`, `profil.json`, `profilschluessel.txt`,
  `profilanfrage.txt` und `echo.env`, `/etc/funk/credentials` (0700) mit Serverschlüssel, Zertifikat, Betriebsgeheimnis
  und TOTP-Schlüssel (je 0600), `/var/lib/funk-echo`
- `/etc/systemd/system/funk-server.service` und `funk-echo.service`, bei einem Port unter 1024 dazu
  `funk-server.service.d/10-port-unter-1024.conf`; eine abweichende Unit bleibt als `.alt` daneben. `/var/lib/funk`,
  `/run/funk` und `/var/log/funk` (nur für die wahlweise Klartextzeile der Sperrliste) legt systemd selbst an.
- den Kurzbefehl `/usr/local/sbin/funk-admin`

Schlüssel erzeugt es nur, wenn sie fehlen, und ersetzt sie nie. Andere Dienste, Units und Dateien fasst es nicht an.

## Für Sarevoc: Probelauf im Wegwerf-Container

Bevor ein künftiger Betreiber das Skript bekommt, einmal mit Debian 13, einmal mit Debian 12 (falls dein Heimserver noch darauf läuft;
`cat /etc/debian_version` im Container sagt es) und einmal mit Ubuntu 24.04. Auf dem Proxmox-Host einen Container wie in
`HEIMSERVER-PROXMOX.md`, Abschnitt 2, nur mit eigener Nummer (hier 199), eigenem Namen und `ip=dhcp`. Das Paket wie in
Abschnitt 5 hineinschieben, im Container auspacken, dann:

1. `bash funk-einrichten.sh` mit den Vorschlägen und dem Namen `funk-probe.example`. Erwartet: .NET und libicu `NEU`,
   die Fragen, dann `WARTET AUF PROFIL`. Prüfen: `getent passwd funk funk-echo`, `ls -l /etc/funk/credentials` (vier
   Dateien `-rw-------`, Ordner `drwx------`), `ls -ld /opt/funk` (root, nicht für andere beschreibbar),
   `systemctl cat funk-echo` (Adresse aus `/etc/funk/echo.env`), `cat /usr/local/sbin/funk-admin`.
2. Ein Probeprofil, nicht mit deinem echten Schlüssel: im Container `dotnet /opt/funk/funk-admin.dll profilschluessel-erzeugen --schluessel /root/probe.key`,
   die Zeile mit `MFkw` hinten an `konfig/profilschluessel.txt` im ausgepackten Paket hängen (nicht in `/opt/funk`, das
   ersetzt das Skript), einen zweiten Pin mit `dotnet /opt/funk/funk-admin.dll schluessel-erzeugen --zertifikat /root/r.crt --schluessel /root/r.key`,
   dann `profil-schreiben` mit dem Pin aus der Anfragezeile und diesem zweiten Pin, `profil-signieren` mit
   `/root/probe.key` (Abschnitt 6).
3. `bash funk-einrichten.sh --profil /root/profil.json`. Erwartet: `bereit`, Ports hören, Abschluss mit der Zeile
   `Schutz:` und `erster-admin`. Dann `funk-admin sperrliste` (erwartet `Keine Sperren (...)`), `ls -ld /var/log/funk`
   (angelegt von systemd über `LogsDirectory`) und `ls /opt/funk/konfig/fail2ban` (`filter.d` und `jail.d`).
4. Noch einmal ohne alles: nur `OK` und `BLEIBT`, kein `NEU`.
5. Mit TCP 443 und "kein Router" in einem zweiten Container: Drop-in da, `ss -ltnp` zeigt 443 bei `dotnet`.
6. Fehlerbilder: einen Container ohne `nesting=1` (erwartet die `pct set`-Zeile, je nach System bei `226/NAMESPACE` oder
   bei `243/CREDENTIALS`; welches dein Debian 13 zeigt, ist noch nicht gemessen), `funk-totp-schluessel` umbenennen
   (erwartet `243/CREDENTIALS`), `apt install ufw && ufw enable` vor dem Lauf (erwartet die Frage nach den Regeln).
7. Umzug zwischen zwei Probecontainern nach `UMZUG.md`, Umzugsabend, mit `umzug-packen` und `--umzug`.

Danach `pct stop 199` und `pct destroy 199`. Was dabei anders aussieht als hier beschrieben, an Claude.
