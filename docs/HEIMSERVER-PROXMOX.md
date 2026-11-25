# Heimserver auf Proxmox einrichten

Stand 03.10.2026, Fassung 0.6.0 (Kanäle mit Rollen und Ch., dazu Einsatzphasen und Heute als Funker aus 0.5.0,
Einrichten und Umzug aus 0.4.0 sowie Raidleitung und Härtung aus 0.3.0). Schritt für Schritt für Sarevoc: der JG-TacRelay-Server in einem unprivilegierten LXC-Container auf deinem Proxmox (Ryzen 9 5950X),
ohne Docker, erreichbar über deinen Telekom-Anschluss. Du führst alles selbst aus; Claude hat keinen Zugang zum Server.

**Läuft dein Server schon?** Mit 0.5.0 gilt für dich Abschnitt 18, "Auf 0.6.0". Mit 0.4.0 erst Abschnitt 17, "Auf 0.5.0"
(mit dem Serverpaket 0.5.0), danach Abschnitt 18. Mit 0.2.0 oder 0.3.0 erst Abschnitt 16, "Auf 0.4.0" (mit dem Serverpaket
0.4.0); er nimmt aus Abschnitt 15 mit, was du brauchst, wenn du 0.3.0 auslässt. Mit 0.1.0 (Serverpaket 20260930_1957) erst
Abschnitt 14, "Von 0.1.0 auf 0.2.0", dann Abschnitt 16, 17 und 18. Die Abschnitte 1 bis 13 beschreiben eine Einrichtung von
Grund auf mit 0.6.0.

**Mit einem Befehl statt von Hand:** `sudo bash funk-einrichten.sh` im Ordner `linux` erledigt im Container, was die
Abschnitte 3, 4 und 6 (dort nur der Teil im Container) und 7 beschreiben, legt die Programme nach `/opt/funk` und fragt
vorher sechs Dinge (acht, wenn ein Router davor steht); Firewall und Router zeigt es nur an. Siehe `EINRICHTEN.md`, für einen Umzug auf einen anderen Server
`UMZUG.md`. Auf Linux ist das Skript noch nicht gelaufen; vor dem ersten echten Einsatz kommt der Probelauf im
Wegwerf-Container (`EINRICHTEN.md`, letzter Abschnitt).

Diese Anleitung baut auf drei Dateien auf und wiederholt sie nicht:

- `BETRIEB.md`: was der Server tut, alle Felder der Konfiguration, alle Befehle von `funk-admin`.
- `konfig/server.beispiel.json`: die Konfiguration für den Heimbetrieb.
- `konfig/funk-server.service`: die systemd-Unit mit Härtung und `LoadCredential`.

Im Paket liegen alle drei im Ordner `linux` (die beiden Beispiele unter `linux/konfig`), dazu die Programme
`funk-server.dll`, `funk-admin.dll`, `funk-echo.dll` und `funk-testclient.dll`. Verweise auf weitere Dokumente sind
nur Hintergrund: `FUNKPROTOKOLL-v1.md`, `LASTTEST.md` und `docs\entwurf\ENTSCHEIDUNGEN.md` (Kapitel "Server und Betrieb")
liegen im Repository, `server-sicherheit.md` in der Arbeitsablage (nicht im Repository). Zum Einrichten brauchst du keine davon.

**Router und DynDNS:** deine FRITZ!Box, dazu dein ddclient und die Portfreigabe für Foundry, die es schon gibt. Die
Menünamen unten hängen etwas von der FRITZ!OS-Version ab. Foundry und der Funkserver teilen sich die öffentliche
IPv4, sie dürfen sich nur keinen Port teilen (Abschnitt 8).

**Geheimnisse:** Serverschlüssel, Betriebsgeheimnis, TOTP-Schlüssel, Profilschlüssel, Reserveschlüssel und API-Tokens
tippst oder erzeugst du selbst, dazu kommen die Codes aus der Authenticator-App und deine Notfallcodes. Sie gehören nie in
einen Chat, nie in eine Datei unter `C:\Claude` und nie in ein Repository. Wo ein Geheimnis entsteht, steht es in dieser
Anleitung dabei.

## 0. Deine Werte

Such dir diese Werte einmal aus und nimm sie überall, wo sie vorkommen. Die Beispiele unten benutzen genau diese.

| Wert | Beispiel | Woher |
| --- | --- | --- |
| Container-ID | `150` | frei wählbar, noch nicht belegt (`pct list` zeigt die belegten) |
| Name des Containers | `funk` | frei |
| Feste LAN-Adresse | `192.168.1.250` | frei und außerhalb des DHCP-Bereichs deines Routers (Abschnitt 1) |
| Gateway und DNS | `192.168.1.1` | die Adresse deines Routers |
| Öffentlicher Name | `<dyndns-name>` | der Name aus deinem ddclient, Abschnitt 8 |
| TCP-Port außen / innen | `8443` / `8443` | außen nicht 443: den leitet deine FRITZ!Box schon an nginx (Foundry) |
| UDP-Port außen / innen | `47443` / `47443` | wie in `server.beispiel.json` |

In `server.beispiel.json` steht als LAN-Adresse `192.168.1.50`. Nimm stattdessen deine eigene.

## 1. Vorbereitung

Auf dem Proxmox-Host (Weboberfläche, Knoten, Shell):

```
pveversion
pvesm status
pct list
```

In der FRITZ!Box den DHCP-Bereich nachsehen (etwa unter Heimnetz, Netzwerk, Netzwerkeinstellungen, IPv4-Einstellungen;
die Namen der Menüs hängen von der FRITZ!OS-Version ab) und eine Adresse außerhalb davon wählen.

In der Übersicht der FRITZ!Box steht deine öffentliche IPv4-Adresse.

**Das siehst du, wenn es geklappt hat:**

- `pveversion` nennt deine Proxmox-Version, `pvesm status` die Speicher (etwa `local` für Vorlagen und `local-lvm` oder
  `local-zfs` für Container). Den Namen des Container-Speichers brauchst du in Abschnitt 2.
- Die öffentliche IPv4 beginnt nicht mit `100.64.` bis `100.127.` (das wäre Carrier-NAT, dann ginge keine Portfreigabe).
- Die gewählte LAN-Adresse antwortet nicht auf `ping` (sie ist frei).

## 2. Container anlegen

Debian als Vorlage. Welche Vorlagen es gibt, hängt vom Stand deines Proxmox ab; deshalb erst nachsehen:

```
pveam update
pveam available --section system | grep debian
```

Nimm für einen neuen Container die Vorlage `debian-13-standard_...` aus der Liste (Debian 13, die aktuelle stabile
Fassung; Debian 12 geht auch) und lade sie:

```
pveam download local <vorlage aus der liste>
```

Dann den Container anlegen. `local-lvm` ersetzt du durch deinen Container-Speicher aus Abschnitt 1:

```
pct create 150 local:vztmpl/<vorlage aus der liste> \
  --hostname funk --unprivileged 1 --features nesting=1 \
  --cores 2 --memory 1024 --swap 512 --rootfs local-lvm:8 \
  --net0 name=eth0,bridge=vmbr0,ip=192.168.1.250/24,gw=192.168.1.1,firewall=1 \
  --nameserver 192.168.1.1 --onboot 1
pct start 150
```

Warum diese Werte: Im Lasttest (`LASTTEST.md`) brauchte der Server mit 40 Funkern 8 bis 21 % eines Kerns; zwei Kerne
und 1 GB reichen mit viel Luft. `nesting=1` braucht die Härtung der systemd-Unit (eigene Mount-Namensräume), ohne sie
startet der Dienst in einem unprivilegierten Container nicht; unter Debian 13 fallen ohne nesting laut Proxmox-Forum auch
Dienste des Systems aus (`243/CREDENTIALS`, Abschnitt 7). `firewall=1` schaltet die Proxmox-Firewall für die
Netzwerkkarte des Containers ein (Abschnitt 7). Die feste Adresse steht im Container selbst, so hängt sie nicht am
DHCP des Routers.

In den Container kommst du ohne SSH, direkt vom Host:

```
pct enter 150
```

Darin das System auf den neuesten Stand bringen:

```
apt update && apt full-upgrade -y
apt install -y wget ca-certificates
```

**Das siehst du, wenn es geklappt hat:**

- `pct config 150` zeigt `unprivileged: 1`, `features: nesting=1` und die Netzwerkkarte mit deiner Adresse.
- Im Container zeigt `ip -4 addr show eth0` deine Adresse, und `ping -c 3 deb.debian.org` bekommt Antworten.
- `cat /etc/debian_version` nennt die Debian-Version. Die brauchst du in Abschnitt 3.

## 3. .NET-Laufzeit

Der Server ist framework-abhängig veröffentlicht; er braucht die .NET-10-Laufzeit (`Microsoft.NETCore.App`, ohne
ASP.NET), und dazu ICU, weil die Namen nach NFKC normalisiert werden (`BETRIEB.md`, Voraussetzungen). Welche Paketquelle
passt, hängt von der Debian-Version ab. Darum zwei Wege, erst A versuchen.

**Weg A, Paketquelle von Microsoft.** `<hauptversion>` ersetzt du durch die Zahl vor dem Punkt aus `/etc/debian_version`,
also `13` bei Debian 13 und `12` bei Debian 12:

```
wget https://packages.microsoft.com/config/debian/<hauptversion>/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
apt update
apt-cache policy dotnet-runtime-10.0
```

Nennt `apt-cache policy` einen Kandidaten (eine Versionsnummer, nicht `(none)`), dann:

```
apt install -y dotnet-runtime-10.0
```

Lädt `wget` nichts (Fehler 404) oder bleibt der Kandidat `(none)`, gibt es für deine Debian-Version noch keine Pakete
von Microsoft. Dann Weg B.

**Weg B, Installationsskript von Microsoft.** Es legt die Laufzeit nach `/usr/share/dotnet`; die Unit ruft
`/usr/bin/dotnet`, deshalb der Verweis. Das ICU-Paket heißt je nach Debian-Version anders, erst nachsehen:

```
apt-cache search '^libicu[0-9]+$'
apt install -y <paket aus der suche>
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --runtime dotnet --install-dir /usr/share/dotnet
ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
```

**Das siehst du, wenn es geklappt hat:**

```
dotnet --list-runtimes
```

zeigt eine Zeile `Microsoft.NETCore.App 10.0.x [...]`. Eine Desktop-Laufzeit braucht der Server nicht.

## 4. Dienstbenutzer und Verzeichnisse

Genau wie in `BETRIEB.md`, Erstinstallation Schritt 2, im Container:

```
useradd --system --no-create-home --shell /usr/sbin/nologin funk
install -d -m 0755 /opt/funk /etc/funk
install -d -m 0700 -o root -g root /etc/funk/credentials
```

`/var/lib/funk` (Zustand) und `/run/funk` (Verwaltungssocket) legt systemd selbst an.

**Das siehst du, wenn es geklappt hat:** `getent passwd funk` endet auf `/usr/sbin/nologin`, und
`ls -ld /etc/funk/credentials` zeigt `drwx------ root root`.

## 5. Dateien kopieren

Auf deinem Windows-PC im entpackten Paket (PowerShell, im Ordner des Pakets). `tar` und `scp` sind in Windows 10 und
11 dabei. `root@<proxmox>` ist dein Proxmox-Host:

```
tar -czf funk-linux.tgz -C linux .
scp funk-linux.tgz root@<proxmox>:/root/
```

Auf dem Proxmox-Host in den Container schieben:

```
pct push 150 /root/funk-linux.tgz /root/funk-linux.tgz
```

Im Container auspacken und an ihren Platz legen:

```
tar -xzf /root/funk-linux.tgz -C /opt/funk
chown -R root:root /opt/funk
chmod -R go-w /opt/funk
cp /opt/funk/konfig/funk-server.service /etc/systemd/system/
cp /opt/funk/konfig/server.beispiel.json /etc/funk/server.json
```

**Das siehst du, wenn es geklappt hat:**

```
dotnet /opt/funk/funk-server.dll --hilfe
dotnet /opt/funk/funk-admin.dll hilfe
```

zeigen ihre Hilfetexte. Ein Fehler wie "You must install or update .NET" heißt: Abschnitt 3 ist nicht fertig.

## 6. Server-Identität, Betriebsgeheimnis und Serverprofil

Das ist `BETRIEB.md`, Schritte 3 bis 5. Hier nur, wo was entsteht und wohin es gehört.

**Im Container** (die Schlüssel verlassen den Container nie, außer in die Sicherung in Abschnitt 12):

```
dotnet /opt/funk/funk-admin.dll schluessel-erzeugen --zertifikat /etc/funk/credentials/server.crt --schluessel /etc/funk/credentials/server.key
dotnet /opt/funk/funk-admin.dll geheimnis-erzeugen --datei /etc/funk/credentials/funk-geheimnis
dotnet /opt/funk/funk-admin.dll totp-schluessel-erzeugen --datei /etc/funk/credentials/funk-totp-schluessel
```

Der dritte Schlüssel verschlüsselt die Zweitfaktor-Geheimnisse der Admins (Authenticator-App). Er muss nicht auf den
Stick: Geht er verloren, richtest du den Zweitfaktor jedes Admins an der Konsole neu ein (`admin-einrichtung-neu`). Ab
0.4.0 ersetzt keiner der drei Befehle eine vorhandene Datei: Ein zweiter Aufruf endet mit `... gibt es schon. Nichts
geändert.` und Exit 3. Ersetzen ginge nur mit `--ersetzen`, und dann wäre beim TOTP-Schlüssel jeder schon eingerichtete
Zweitfaktor nicht mehr lesbar, beim Serverschlüssel bräuchte es ein neues Profil. Weil TOTP nur 30 s Spielraum hat,
braucht der Host eine richtige Uhr; Proxmox bringt `chrony` mit, `chronyc tracking` auf dem Host zeigt die Abweichung
(Abschnitt 14, Schritt 8). Der Container hat die Uhr des Hosts.

Die erste Ausgabe nennt den Pin des Serverschlüssels (Base64) und seine Anzeigeform. Den Pin schreibst du dir ab; er
ist öffentlich und kommt ins Profil.

**Auf deinem Offline-Rechner** (dein Windows-PC geht, aber in einem eigenen Ordner, am besten auf einem USB-Stick, nicht
unter `C:\Claude`). Kopier dafür den **ganzen Ordner `linux`** aus dem Paket auf den Stick, **benenne ihn dort in
`funk-offline` um** und öffne in diesem Ordner PowerShell (im Explorer Rechtsklick in den Ordner, "In Terminal
öffnen"). `funk-admin.dll` allein reicht nicht, sie braucht `funk-server.dll` und `Funk.Protokoll.dll` daneben. Sie
läuft mit der .NET-Laufzeit, die JG TacRelay auf deinem PC ohnehin braucht. Die Schlüssel landen dann in
`funk-offline` auf dem Stick. Deshalb der andere Name: Diesen Ordner packst du nie für Abschnitt 5 oder 13 ein, sonst
kämen deine privaten Schlüssel in den Container.

```
dotnet funk-admin.dll profilschluessel-erzeugen --schluessel profil.key
dotnet funk-admin.dll schluessel-erzeugen --zertifikat reserve.crt --schluessel reserve.key
```

Der erste Befehl gibt den öffentlichen Teil des Profilschlüssels (SPKI, Base64) aus, eine lange Zeile, die mit `MFkw`
beginnt, und darunter `Fingerabdruck: XXXX-XXXX-XXXX-XXXX`. Die Zeile ist öffentlich; die Clients bekommen sie mit dem
Setup (unten). Der zweite ist der Reserveschlüssel für den Server; du brauchst nur seinen Pin. `profil.key` und
`reserve.key` bleiben auf dem Stick.

Dann das Profil schreiben und signieren, mit deinem öffentlichen Namen aus Abschnitt 8 und dem Port außen (8443):

```
dotnet funk-admin.dll profil-schreiben --name "Meine Staffel" --version 1 --host <dyndns-name>/v4 --tcp-port 8443 --pin <pin des servers> --pin <pin der reserve> --aus daten.json
dotnet funk-admin.dll profil-signieren --daten daten.json --schluessel profil.key --aus profil.json
dotnet funk-admin.dll profil-pruefen --profil profil.json --spki <spki des profilschluessels>
```

`/v4` heißt: In der Heimphase nur IPv4 (Abschnitt 9). `profil.json` ist öffentlich; es geht in den Container und mit
dem Setup an die Clients. Kopieren wie in Abschnitt 5 (`scp`, `pct push`), im Container nach `/etc/funk/profil.json`:

```
install -m 0644 /root/profil.json /etc/funk/profil.json
```

**An die Clients:** Ab 0.2.0 gibt es im Client kein Feld mehr für Profil und Profilschlüssel; beides kommt mit dem Setup,
damit niemand einem Mitglied ein falsches Profil unterschieben kann. Schick Claude `profil.json` und die Zeile, die mit
`MFkw` beginnt, nie `profil.key`. Claude legt beides im Repository nach `konfig\profil.json` und
`konfig\profilschluessel.txt` und baut damit beide Setups, das normale und das Admin-Setup. Für eine eigene Gruppe
trägst du dort deinen eigenen Profilschlüssel und dein eigenes Profil ein (Handbuch B0.4).

**Das siehst du, wenn es geklappt hat:**

- `profil-pruefen` meldet `Urteil: Uebernehmen` und listet deinen Host und beide Pins.
- Im Container zeigt `ls -l /etc/funk/credentials` vier Dateien, `server.key`, `funk-geheimnis` und `funk-totp-schluessel` mit `-rw-------`.

## 7. Konfiguration, Dienst und Firewall

**Konfiguration.** In `/etc/funk/server.json` (Felder in `BETRIEB.md`, Abschnitt Konfiguration) trägst du unter
`binden` deine LAN-Adresse ein und setzt `oeffentlicherTcpPort` von 443 auf `8443`, weil 443 an deiner FRITZ!Box
schon nginx gehört. Alles andere passt für den Anfang. `grenzen.ausgangMbit` bleibt bei 35, solange du den
Netto-Upload nicht gemessen hast (höchstens 80 % davon, bei 50 Mbit/s Upload also etwa 35 bis 40). `totpSchluessel`
(der dritte Schlüssel aus Abschnitt 6) und `mitgliedskarte` stehen im Beispiel schon richtig. `mindestClientVersion`
bleibt `0.1.0`; warum, steht in Abschnitt 14.

```
nano /etc/funk/server.json
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
```

`pruefen` liest Konfiguration und Profil, schaut aber nicht nach, ob die Schlüsseldateien da sind. Das zeigt erst der
Start.

**Dienst.** Die Unit liegt schon unter `/etc/systemd/system/`:

```
systemctl daemon-reload
systemctl enable --now funk-server
journalctl -u funk-server -n 30 --no-pager
```

**Firewall im Container.** Der Server öffnet von sich aus nur die zwei Ports, und nur auf der Adresse aus `binden`
(`BETRIEB.md`: kein HTTP, kein Admin-Port, der Verwaltungssocket ist eine Datei). Nachsehen, was sonst noch lauscht:

```
ss -tulpen
```

Lauscht dort ein SSH-Dienst (Port 22, je nach Vorlage), brauchst du ihn nicht; `pct enter` reicht. Wenn du magst:

```
systemctl disable --now ssh
```

**Proxmox-Firewall.** Sie wirkt an der Netzwerkkarte des Containers (`firewall=1` aus Abschnitt 2). Auf dem Host die
Datei `/etc/pve/firewall/150.fw` anlegen:

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

Eingehend nur die zwei Ports. Ausgehend kommt der Container ins Internet (Updates), aber in dein Heimnetz nur zum
DNS der FRITZ!Box, so bleibt zum Beispiel Foundry auf demselben Host außer Reichweite (`docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Server und Betrieb", Heimphase).
Antworten auf eingehende Verbindungen lässt die Proxmox-Firewall immer durch.

Die Regeln wirken erst, wenn die Firewall auch auf Rechenzentrumsebene an ist (Weboberfläche: Datacenter, Firewall,
Options, Firewall: Yes). Proxmox lässt danach die Weboberfläche (Port 8006) und SSH zum Host aus dem lokalen Netz
weiter zu. Schalte sie trotzdem ein, während du eine offene Shell auf dem Host hast.

```
pve-firewall compile
pve-firewall status
```

**Das siehst du, wenn es geklappt hat:**

- `pruefen` meldet eine Zeile zur Uhr (`Uhr: ...`, Abschnitt 15, Schritt 6) und am Ende `Konfiguration in Ordnung.`
- Das Journal zeigt eine Zeile `Funk-Server 0.5.0 bereit: 1 Adresse(n), TCP Bindport 8443 (öffentlich 8443), UDP
  Bindport 47443 (öffentlich 47443), ... Pin XXXX-XXXX-XXXX-XXXX`, und die Anzeigeform des Pins ist die aus Abschnitt 6.
  Steht davor `WARNUNG Der Pin des Serverschlüssels (XXXX-XXXX-XXXX-XXXX) steht nicht im Serverprofil Version 1: ...`,
  passt `profil.json` nicht zum Schlüssel.
- Davor warnt das Journal `Kein Eigentümer gesetzt: funk-admin eigentuemer --konto <id> (3.10). ...`. Das ist richtig so,
  bis du in Abschnitt 10 Eigentümer bist.
- Steht im Journal `status=243/CREDENTIALS`, findet systemd eine der Dateien nicht, die die Unit mit `LoadCredential`
  lädt, meist `funk-totp-schluessel` (Abschnitt 6), und startet den Dienst gar nicht. Liegen alle Dateien in
  `/etc/funk/credentials`, sieh nach `nesting=1` (`pct config 150` auf dem Host): Ohne nesting fallen unter Debian 13 laut
  Proxmox-Forum Dienste mit `243/CREDENTIALS` aus statt mit `226/NAMESPACE` (hier nicht gemessen). Abhilfe wie unten bei 226.
- Warnen `pruefen` oder das Journal, dass das Serverprofil einen anderen TCP-Port nennt als `oeffentlicherTcpPort`, steht
  in `server.json` meist noch die 443 aus dem Beispiel: auf `8443` setzen, dann `pruefen` und `systemctl restart funk-server`.
- `ss -tulpen` zeigt `8443` (tcp) und `47443` (udp) auf deiner LAN-Adresse, sonst höchstens Dienste auf `127.0.0.1`.
- `pve-firewall status` meldet `Status: enabled/running`, und die Weboberfläche von Proxmox geht weiter.
- Steht im Journal `status=226/NAMESPACE`, fehlt `nesting=1`: `pct set 150 --features nesting=1`, Container neu starten.

## 8. Öffentlicher Name (DynDNS) und Portfreigabe

Dein Telekom-Anschluss hat eine echte, aber wechselnde IPv4. Die Clients finden den Server deshalb über einen Namen, der
bei jedem Wechsel nachgezogen wird, mit kurzer TTL (Ziel höchstens 60 s, `FUNKPROTOKOLL-v1.md` 7.1). Nach einem Wechsel
verbinden die Clients selbst neu.

**Du hast schon ddclient** (für Foundry). Foundry und der Funkserver liegen hinter derselben öffentlichen IPv4, also
reicht derselbe Name. Du brauchst nichts Neues anzumelden und nichts an ddclient zu ändern. Willst du lieber einen
eigenen Namen wie `funk.<deine-domain>`, trägst du ihn als zweiten Namen in ddclient ein; wie das geht, hängt von
deinem Anbieter ab.

Wichtig ist die TTL, also wie lange andere Rechner die alte Adresse zwischenspeichern. Nach einem Adresswechsel
kommen die Clients erst wieder durch, wenn die TTL abgelaufen ist; gut sind höchstens 60 s, bis 300 s geht es noch.
Nachsehen, in PowerShell:

```
Resolve-DnsName <dyndns-name> -Type A -DnsOnly | Format-List Name, IPAddress, TTL
```

Steht dort mehr als 300, stell die TTL beim Anbieter kürzer ein, wenn er das erlaubt. Den Zugang deines ddclient
(Kennwort oder Token) brauchst du dafür nicht in einen Chat zu schreiben; er bleibt, wo er ist.

Später, beim Umzug auf einen gemieteten Server, wäre eine Subdomain einer eigenen Domain am saubersten (vorher klären, wo
`example.org` verwaltet wird): Der Name im Serverprofil bliebe dann beim Umzug gleich.

**Portfreigabe** in der FRITZ!Box (etwa unter Internet, Freigaben, Portfreigaben, Gerät für Freigaben hinzufügen: den
Container `funk` bzw. seine Adresse wählen), genau zwei Freigaben:

| Protokoll | Port außen | an Port | Ziel |
| --- | --- | --- | --- |
| TCP | 8443 | 8443 | 192.168.1.250 |
| UDP | 47443 | 47443 | 192.168.1.250 |

Keine weiteren: kein SSH, kein Port 80, UDP nicht auf 443 (`BETRIEB.md`, Heimbetrieb).

**Foundry behält 443.** Deine FRITZ!Box leitet TCP 443 schon an nginx (Foundry). Diese Freigabe bleibt, wie sie ist;
der Funk bekommt außen 8443, UDP 47443 kollidiert mit nichts. Später ginge auch 443 für beide, wenn nginx die
Verbindungen am Protokollnamen (ALPN `tacrelay/1`) auseinandersortiert (`stream` mit `ssl_preread`); das lohnt
erst, wenn Mitspieler hinter Firmen-Firewalls sitzen, die nur 443 durchlassen.

Außerdem aus `BETRIEB.md`: den
Container als Echtzeitanwendung priorisieren und eine tägliche Neueinwahl, falls dein Anschluss eine hat, auf eine Zeit
ohne Einsätze legen.

**Das siehst du, wenn es geklappt hat:**

```
nslookup <dyndns-name>
nslookup -type=AAAA <dyndns-name>
```

Die erste Abfrage liefert deine öffentliche IPv4 aus der Übersicht der FRITZ!Box, die zweite keine Adresse.

Von außen prüfen (dein PC im Hotspot deines Handys, nicht im Heimnetz), in PowerShell:

```
Test-NetConnection <dyndns-name> -Port 8443
```

zeigt `TcpTestSucceeded : True`. UDP lässt sich so nicht prüfen; das zeigt der Client: Läuft die Sprache über "Tunnel"
statt über UDP, stimmt die UDP-Freigabe nicht.

## 9. IPv6

In der Heimphase bleibt der Server bei IPv4:

- Im Profil steht der Host mit `/v4`, im DNS nur der A-Eintrag. Ein AAAA-Eintrag würde von der FRITZ!Box ihre eigene
  IPv6 melden, nicht die des Containers (`FUNKPROTOKOLL-v1.md` 7.1).
- Der Container bekommt vom Router trotzdem eine IPv6-Adresse. Der Server bindet sie nicht (`binden` nennt nur die
  IPv4), und in der FRITZ!Box gibst du keine IPv6-Freigabe für den Container frei.
- Das Telekom-Präfix wechselt. IPv6 kommt erst, wenn die Router-Freigabe geklärt ist (`BETRIEB.md`, Offener Punkt 19);
  dann bekommt `binden` die IPv6 dazu und das Profil `/v4v6`.

**Das siehst du, wenn es geklappt hat:** `ss -tulpen` zeigt die beiden Ports nur auf der IPv4-Adresse.

## 10. Erster Admin, Eigentümer und Verwaltung

Der erste Admin bist du, mit deinem Client ab 0.2.0. Du meldest dich mit deinen Namen an (Antrag), gibst dein Gerät an der
Konsole selbst frei, machst dein Konto zum Admin und zum Eigentümer und richtest dann die Verwaltung im Client ein.
Hintergrund: `BETRIEB.md`, Bootstrap des ersten Admins.

**Welches Setup:** Du nimmst das Admin-Setup (`<zeit>_JG-TacRelay-Admin-Setup-<fassung>.exe`), das ist der Client mit dem
Verwaltungsteil. Alle anderen bekommen das normale Setup (`<zeit>_JG-TacRelay-Setup-<fassung>.exe`), darin ist kein
Verwaltungscode. Serverprofil und Profilschlüssel bringen beide mit (Abschnitt 6); im Client trägst du dafür nichts ein.

Das Feld **Adresse** in der Karte **Erweitert: Details der Verbindung** (unter **Einstellungen**) bleibt leer, dann gilt der Host aus dem Profil.
Nur wenn du zu Hause über den DynDNS-Namen nicht durchkommst, trägst du dort die LAN-Adresse `192.168.1.250` ein;
geprüft wird trotzdem der Pin aus dem Profil. Versucht der Client es gerade noch (`Verbinde...`), gilt die neue Adresse
erst nach **Trennen** und wieder **Verbinden**; das steht dann auch oben im Client.

**Anmelden (Antrag):**

1. Admin-Setup ausführen. Auf der Seite **Deine Namen** trägst du deinen Namen in Star Citizen ein, so wie im Spiel, und
   deinen Namen auf dem Discord-Server der Gruppe, etwa `Mustermann/Max`. Die Seite kommt nur bei einer neuen Installation; sonst fragt der
   Client selbst.
2. JG TacRelay starten, links **Start** (Strg+1). Unter **Einstellungen** (Strg+2) zeigt die Karte **Erweitert: Details
   der Verbindung** (nur im Admin-Setup) `Meine Staffel: <dyndns-name> (nur IPv4), TCP-Port 8443` und `Signatur stimmt
   (Profilschlüssel XXXX-XXXX-XXXX-XXXX), Version 1, gültig bis ...`. Die Kennung des Profilschlüssels ist die Zeile
   `Fingerabdruck:` aus `profilschluessel-erzeugen` (Abschnitt 6). Bis 0.5.x stand das im Bereich Verbindung.
3. Auf **Start** fragt eine Karte `Anmelden als <SC-Name> (Discord: <Discord-Name>) - stimmt das?`. Stimmen die Namen, **So
   anmelden und verbinden**. Oben steht erst `Verbinde...`, dann `Wartet auf Freigabe`, und die Karte **Warten auf die
   Freigabe** zeigt die Gerätekennung groß, `XXXX-XXXX-XXXX-XXXX`. Klappt die Verbindung nicht, steht der Grund oben in
   Rot, etwa `Server nicht erreichbar (...)` oder `Server nicht vertrauenswürdig: ...`. Bei "nicht erreichbar" versucht der
   Client es von selbst weiter; bei "nicht vertrauenswürdig" passt der Serverschlüssel nicht zu den Pins im Profil, dann
   braucht es ein neues `profil.json` und neue Setups.
4. Im Container die offenen Anträge ansehen:

```
dotnet /opt/funk/funk-admin.dll antraege --konfig /etc/funk/server.json
```

Jede Zeile beginnt mit der Gerätekennung, dahinter SC-Name und Discord-Name, etwa
`XXXX-XXXX-XXXX-XXXX  <SC-Name> (Discord: <Discord-Name>)  seit <zeit> UTC`.

**Hast du auf diesem Server schon ein Konto** (etwa aus 0.1.0), steht ab 0.3.0 hinter deinem Antrag
`BESTEHENDES KONTO? <Name> (Konto <id>), ADMIN: gehört das Gerät dazu (nur wenn sein neues Gerät dieselbe Kennung zeigt),
dann funk-admin freigeben <gerätekennung> --an-konto <id>`. Vergleiche zuerst die Kennung vorn in der Zeile mit der in
deinem Client: Die Namen eines Antrags kann jeder eintippen, der Hinweis kommt also auch bei einem, der sich als du
ausgibt. Stimmt sie, nimmst du genau diesen Befehl, mit `dotnet /opt/funk/funk-admin.dll` davor und `--konfig
/etc/funk/server.json` dahinter: Das Gerät kommt an dein altes Konto, und dessen Stufe und Rechte gelten. Die Namen aus dem
Antrag übernimmt das alte Konto dabei nicht; die trägst du wie in Abschnitt 14, Schritt 10 ein (`scnamen`,
`discordname`), und aus Punkt 5 brauchst du nur noch `eigentuemer`, falls das Konto noch nicht Eigentümer ist. Mit
`freigeben` allein entsteht ein neues Konto als Funker; so geschah es am 01.10.2026, die Reparatur steht am Ende von
Abschnitt 14 (**Antrag ins falsche Konto**).

Stimmt die Kennung mit der im Client überein und gibt es kein altes Konto, gibst du frei:

```
dotnet /opt/funk/funk-admin.dll freigeben <gerätekennung> --konfig /etc/funk/server.json
```

Die Antwort ist `OK: <gerätekennung>`, und der Client zeigt oben `Verbunden`. Steht dort `Verbunden (Tunnel)`, kommt UDP
nicht durch; dann die UDP-Freigabe 47443 aus Abschnitt 8 prüfen.

5. Ein Antrag wird immer als Funker freigegeben. Deine Konto-ID steht vorn in deiner Zeile bei `konten`; damit machst du
   dein Konto zum Admin und zum Eigentümer:

```
dotnet /opt/funk/funk-admin.dll konten --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll stufe --konto <deine id> --stufe ADMIN --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll eigentuemer --konto <deine id> --konfig /etc/funk/server.json
```

`konten` zeigt je Konto eine Zeile mit Konto-ID, Stufe, Status, SC-Namen und `(Discord: <Discord-Name>)`. `stufe`
antwortet `OK`, `eigentuemer` antwortet `Eigentümer: <SC-Name> (Konto <id>)`. Die Rechte gelten sofort; die Anzeige
**Stufe** im Client wechselt beim nächsten Verbinden auf `Admin`.

6. **Verwaltung einrichten** (Admin-Schlüssel im TPM und Zweitfaktor mit einer Authenticator-App): genau wie in Abschnitt 14,
   Schritt 12. Die Schritte dort gelten für jede Einrichtung gleich.

**Kanäle** legst du an wie in `BETRIEB.md`, etwa den Kanal Echo für den nächsten Abschnitt (die ID 90 ist nur ein Beispiel,
sie darf noch nicht belegt sein; `netzliste` zeigt die belegten). Offen mit hören und senden, damit der Echo-Funker und
jeder Client sich selbst hineinsetzen können:

```
dotnet /opt/funk/funk-admin.dll netz-setzen --id 90 --name Echo --offen HOEREN_SENDEN --va --konfig /etc/funk/server.json
```

Ab 0.5.0 steht ein Kanal, den du ohne Schalter anlegst, auf Sprachaktivierung (`--va` wirkt genauso). Den Kanal Kommando legst du mit
`--taste` an, dann beginnt er bei jedem auf der Sprechtaste. Auch hier ist die ID nur ein Beispiel;
eine belegte ID ändert den Kanal, den es schon gibt:

```
dotnet /opt/funk/funk-admin.dll netz-setzen --id 2 --name Kommando --offen HOEREN_SENDEN --standard --taste --konfig /etc/funk/server.json
```

Kanäle, die schon da sind, behalten ihre Einstellung. funk-admin 0.4.x kennt `--taste` nicht und lehnt mit Exit 2 ab, ohne
etwas zu ändern; dort steht ein Kanal ohne Schalter ohnehin auf Taste. Die Kanäle mit Ch. (Vorlage Star Citizen)
übernimmst du erst, wenn alle Mitglieder das Setup 0.6.0 haben (Abschnitt 18, Schritt 9).

**Weitere Mitglieder** nehmen das normale Setup, tragen ihre Namen ein und verbinden. Sie schicken dir in Discord ihren
SC-Namen und die Gerätekennung aus der Karte **Warten auf die Freigabe**; die Kennung ist kein Geheimnis. Du vergleichst
sie mit `antraege` und gibst frei, an der Konsole mit `freigeben <gerätekennung>` oder im Client unter **Verwaltung**,
**Anträge**. Dort braucht Freigeben einen Code aus der Authenticator-App, der höchstens 5 Minuten alt ist; ist deine
Anmeldung älter, fragt der Client danach. Zeigt `antraege` hinter einem Antrag `passt zu Vormerkung <id> <SC-Name>`,
übernimmst du die Vormerkung mit `freigeben <gerätekennung> --vormerkung <id>`. Ein weiterer PC eines Mitglieds bekommt seit
0.7.0 sein eigenes Konto (ein PC, ein Konto); `--an-konto` brauchst du nur noch für den PC eines Admins mit neuem
Geräteschlüssel, wie oben.

Discord-Namen sind eindeutig, wie die SC-Namen, auch gegen andere Groß- und Kleinschreibung. Trägt schon ein Konto den
Discord-Namen eines Antrags, steht hinter dem Antrag `DISCORD-NAME VERGEBEN (<SC-Name> (Konto <id>))`. Seit 0.7.0 gibt
`freigeben` trotzdem frei, nur bekommt das neue Konto diesen Discord-Namen nicht (bis 0.6.x lehnte `freigeben` hier ab).
Nach der Freigabe ändert den Discord-Namen nur ein Admin:
an der Konsole mit `discordname` (Abschnitt 14, Schritt 10) oder im Client unter **Verwaltung**, **Mitglieder**, auf der
Karte des Kontos. Das Mitglied selbst sieht ihn unter **Ich**, ändern kann es ihn nicht.

**Das siehst du, wenn es geklappt hat:**

```
dotnet /opt/funk/funk-admin.dll konten --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll geraete --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll admins --konfig /etc/funk/server.json
```

`konten` zeigt dich als `ADMIN` mit `EIGENTÜMER` und deinem SC-Namen, `geraete` dein Gerät als `FREIGEGEBEN`, `admins`
deinen Admin-Nachweis als `AKTIV` mit `Notfallcodes 10`. Der Client zeigt oben `Verbunden` und unter **Server, Konto und
Gerät** dein Gerät mit `(TPM)` oder `(Software)`. Die Warnung `Kein Eigentümer gesetzt` steht beim nächsten Start des
Servers nicht mehr im Journal.

## 11. Test mit dem Echo-Funker

Der Echo-Funker ist ein Teilnehmer wie jeder Client: TLS mit Pin, eigener Geräteschlüssel. Er hört im Kanal Echo mit und
schickt jede Sprechphase nach 500 ms Pause zurück, höchstens 30 s je Phase. Er läuft im selben Container unter einem
eigenen Benutzer.

Anders als JG TacRelay haben der Echo-Funker und der Prüf-Client unten keine Namen und stellen keinen Antrag. Sie holen
sich einen Registrierungscode, den du an der Konsole mit `verknuepfen` einlöst; danach gibst du wie immer mit `freigeben`
frei. So geht es auch mit dem Server 0.2.0. Ein Echo-Funker, der schon unter 0.1.0 freigegeben war, bleibt es; nach dem
Update aus Abschnitt 14 ist hier nichts zu tun.

```
useradd --system --no-create-home --shell /usr/sbin/nologin funk-echo
install -d -m 0700 -o funk-echo -g funk-echo /var/lib/funk-echo
```

Die erste Registrierung von Hand, damit der Code auf dem Bildschirm steht und nicht im Journal. `--adresse` ist deine
LAN-Adresse (der Server bindet nur sie); geprüft wird trotzdem der Pin aus dem Profil:

```
runuser -u funk-echo -- dotnet /opt/funk/funk-echo.dll --profil /etc/funk/profil.json --identitaet /var/lib/funk-echo/geraet.pem --adresse 192.168.1.250
```

Er legt `geraet.pem` an (nur für ihn lesbar) und zeigt einen Registrierungscode samt der zwei Befehle, ab 0.3.0 ganz
ausgeschrieben mit `dotnet /opt/funk/funk-admin.dll` und `--konfig /etc/funk/server.json`; nur die Zahl hinter `--discord`
setzt du selbst ein. In einer zweiten Shell (`pct enter 150` noch einmal):

```
dotnet /opt/funk/funk-admin.dll verknuepfen --code <code> --discord 100000000000000001 --benutzername echo-funker --name Echo --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll freigeben <anzeigeform> --konfig /etc/funk/server.json
```

Die Discord-ID ist hier nur eine eindeutige Zahl; der Echo-Funker hat kein Discord-Konto. In der ersten Shell steht dann
"Bereit im Kanal 'Echo' (ID 90)" (bis 0.5.x "Bereit im Netz ..."). Mit Strg+C beenden und als Dienst starten. Die Unit liest die Adresse ab 0.4.0 aus
`/etc/funk/echo.env`; die erste Zeile unten legt die Datei mit deiner LAN-Adresse an, die Unit selbst änderst du nicht.
Ihre Zeile `RestrictAddressFamilies` mit `AF_NETLINK` bleibt so: Ohne `AF_NETLINK` bricht der Sprachweg des Echo-Funkers
beim Start ab.

```
echo 'FUNK_ECHO_ADRESSE=192.168.1.250' > /etc/funk/echo.env
cp /opt/funk/konfig/funk-echo.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now funk-echo
journalctl -u funk-echo -n 20 --no-pager
```

Das Journal zeigt `Bereit im Kanal 'Echo' (ID 90)`. Fehlt die Datei `echo.env`, nimmt die Unit die Adresse aus ihrer Zeile
`Environment=FUNK_ECHO_ADRESSE=192.168.1.50`, und der Echo-Funker erreicht deinen Server nicht.

**Prüfung ohne Client**, im Container. Der Prüf-Client braucht wie jedes Gerät eine Freigabe, danach spricht er eine
künstliche Sekunde in den Kanal Echo. In der ersten Shell starten:

```
dotnet /opt/funk/funk-testclient.dll echo-probe --profil /etc/funk/profil.json --adresse 192.168.1.250
```

Er zeigt `Gerät unbekannt. Registrierungscode <code> (5 Minuten gültig). In einer zweiten Shell:` und darunter die zwei
Befehle ganz ausgeschrieben, mit Code und Anzeigeform, und wartet 5 Minuten auf die Freigabe. Nur die Zahl hinter
`--discord` setzt du selbst ein, `100000000000000002`. Kopierst du lieber von hier, in der zweiten Shell:

```
dotnet /opt/funk/funk-admin.dll verknuepfen --code <code der probe> --discord 100000000000000002 --benutzername echo-probe --name Probe --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll freigeben <anzeigeform der probe> --konfig /etc/funk/server.json
```

Die Zahl muss eine andere sein als beim Echo-Funker. Mit dessen `100000000000000001` landete die Probe in seinem Konto.
Ein Konto hat nur eine Sitzung: Die Probe verdrängt den Echo-Funker. Ergebnis `NICHT BESTANDEN`, kein Echo. Der
Echo-Funker verbindet dann nicht selbst neu; ab 0.3.0 schreibt er `Verdrängt durch <gerät> (<kennung>): ...` ins
Journal, wartet 10 Minuten und endet mit Code 3, und systemd startet ihn neu. Die Wartezeit verhindert, dass er und ein
anderes Gerät desselben Kontos sich im Sekundentakt gegenseitig hinauswerfen. Ist das passiert: das Gerät der Probe
entfernen (unten) und `systemctl restart funk-echo`, dann musst du die 10 Minuten nicht abwarten. Das Konto `Probe` darf
bleiben; bei jedem weiteren Probelauf nimmst du dieselbe Zahl.

Weist der Server den Echo-Funker ab (Konto oder Gerät gesperrt, Verdrängen gesperrt, falscher Pin), endet er nach
derselben Wartezeit mit Code 4. Das Journal von funk-echo nennt den Grund in der Zeile davor.

Danach das Prüfgerät wieder entfernen:

```
dotnet /opt/funk/funk-admin.dll entfernen <anzeigeform der probe> --konfig /etc/funk/server.json
```

Ein Konto hat höchstens 3 aktive Geräte. Registrierst du den Echo-Funker mehrmals neu (etwa nach einem verlorenen
`geraet.pem`) oder vergisst das Entfernen der Probe, zeigt `geraete --konto <id> --konfig /etc/funk/server.json` die
alten Geräte, und `entfernen <anzeigeform>` nimmt sie weg.

**Mit dem Client:** Feld mit dem Kanal Echo belegen, einen Satz sprechen, eine halbe Sekunde später hörst du ihn über
Server, Jitterpuffer und Mischer zurück. Von außen (Hotspot) geprüft, ist damit der ganze Weg über Router und DynDNS
belegt.

**Das siehst du, wenn es geklappt hat:**

- `echo-probe` endet mit `BESTANDEN`: 50 von 50 Frames, Reihenfolge richtig, Pause ab 500 ms.
- Das Journal von `funk-echo` zeigt je Satz "Aufnahme beginnt", "Aufnahme ... beendet" und "Wiedergabe fertig".

## 12. Sicherung

**Schlüsseldateien.** Das Wichtigste sind `/etc/funk/credentials/server.crt`, `server.key` und `funk-geheimnis`. Ohne
sie gibt es einen neuen Pin, und jeder Client müsste ein neues Profil bekommen. Einmal auf deinen Offline-Stick (zu
`profil.key` und `reserve.key`), am besten verschlüsselt, nie nach `C:\Claude`, nie in eine Cloud, nie in einen Chat.
Aus dem Container holst du sie über den Host:

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

`funk-totp-schluessel` holst du nicht heraus; er bleibt nur im Container (Abschnitt 6). Geht er verloren, richtest du den
Zweitfaktor der Admins an der Konsole neu ein.

**Notfallcodes.** Die zehn Codes aus der Einrichtung (Abschnitt 14, Schritt 12) bewahrst du getrennt vom Telefon auf: als
sichere Notiz in einem Passwortmanager oder ausgedruckt an einem sicheren Ort. Jeder gilt einmal. Neue gibt es nur an der Konsole
(`notfallcodes --konto <id>`), dann gelten die alten nicht mehr.

**Zustand** (Konten, Geräte, Kanäle, Aufstellungen), regelmäßig und vor jedem Update, im Container:

```
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
```

Die Sicherung landet unter `/var/lib/funk/sicherungen/<zeit>`; Schlüssel und Geheimnis kommen nie mit (`BETRIEB.md`).
Seit 0.2.0 sind `admins.json`, `mitglieder.json` und `journal.json` dabei, jede aber erst, sobald es sie gibt:
`mitglieder.json` entsteht mit der ersten Pflege von Mitgliedern, Rollen oder Abzeichen, `admins.json` mit dem ersten
`admin-vorsehen`. Das Journal trägt Klarnamen; die TOTP-Geheimnisse in `admins.json` lassen sich ohne
`funk-totp-schluessel` nicht öffnen.

**Zurückspielen**, etwa nach einem Fehler beim Update oder einer Änderung, die du ungeschehen machen willst. Der Funk ist
dabei aus. Im Container:

```
systemctl stop funk-echo funk-server
ls /var/lib/funk/sicherungen/
mkdir -p /root/zustand-vorher
cp -a /var/lib/funk/*.json /root/zustand-vorher/
ls /var/lib/funk/ /var/lib/funk/sicherungen/<zeit>/
```

`ls` zeigt beide Ordner nebeneinander. Steht im ersten eine `.json`, die im zweiten fehlt (etwa `mitglieder.json`, wenn
die Sicherung älter ist als die erste Pflege der Mitglieder), nimmst du sie weg, sonst passt sie nicht zu den
zurückgespielten Konten; ihre Kopie liegt in `/root/zustand-vorher`. Nur `sperrliste.json` darf bleiben (ab 0.3.0, sie
steht nie in einer Sicherung):

```
rm /var/lib/funk/<datei, die in der sicherung fehlt>
cp /var/lib/funk/sicherungen/<zeit>/*.json /var/lib/funk/
chown funk:funk /var/lib/funk/*.json
chmod 0600 /var/lib/funk/*.json
systemctl start funk-server funk-echo
journalctl -u funk-server -n 20 --no-pager
```

`chown` und `chmod` gehören dazu: `cp` als root legt die Dateien root gehörend an, und der Dienst läuft als `funk`.

**Das siehst du:** Das Journal zeigt `Funk-Server ... bereit`, und `konten` zeigt den Stand der Sicherung. Endet der
Start mit `UnauthorizedAccessException`, fehlt das `chown`.

**Ganzer Container.** Eine Proxmox-Sicherung (vzdump) enthält auch `/etc/funk/credentials`, samt dem TOTP-Schlüssel, und
`/var/lib/funk` mit dem Journal (Klarnamen). Wohin sie geht, muss so gut geschützt sein wie der Stick; lege sie nur
verschlüsselt ab.

Den Geräteschlüssel des Echo-Funkers brauchst du nicht zu sichern; geht er verloren, registrierst du ihn neu.

**Das siehst du, wenn es geklappt hat:** Auf dem Stick liegen `server.key`, `server.crt`, `funk-geheimnis`,
`profil.key`, `reserve.key` und `reserve.crt`; auf dem Host liegt davon nichts mehr (`ls /root`).

## 13. Updates

**Reihenfolge:** erst der Server, dann die Clients. Ein neuer Client kann den Server nach Dingen fragen, die ein älterer
Server nicht kennt, und der trennt dann die Verbindung; so ist es beim Schritt von 0.1.0 auf 0.2.0 (Abschnitt 14).

**Neues Paket von JG TacRelay:** den Ordner `linux` wie in Abschnitt 5 übertragen, dann im Container:

```
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
systemctl stop funk-echo funk-server
tar -xzf /root/funk-linux.tgz -C /opt/funk
chown -R root:root /opt/funk
chmod -R go-w /opt/funk
systemctl start funk-server funk-echo
journalctl -u funk-server -n 20 --no-pager
```

`chmod` gehört dazu wie in Abschnitt 5: Das tgz aus Windows trägt für jede Datei "für alle beschreibbar", und `tar`
übernimmt das als root. Ohne `chmod` könnte danach jeder Benutzer im Container die Programme austauschen.

Beim Stoppen bekommen alle Clients `Bye{WARTUNG}` und kommen nach etwa 10 s von selbst wieder (`BETRIEB.md`, Laufender
Betrieb). Die Unit-Dateien in `/etc/systemd/system/` überschreibt das nicht; hat sich eine geändert, steht das in den
Hinweisen zum Paket. Dann die Unit neu kopieren und systemd neu einlesen lassen, sonst läuft die alte weiter. Beispiel
`funk-echo.service`; ab 0.4.0 steht deine Adresse in `/etc/funk/echo.env` (Abschnitt 11), die Unit passt du nicht mehr an:

```
cp /opt/funk/konfig/funk-echo.service /etc/systemd/system/
systemctl daemon-reload
systemctl restart funk-echo
```

Bei `funk-server.service` genauso: `cp`, `systemctl daemon-reload`, `systemctl restart funk-server`. Von 0.1.0 auf 0.2.0
hat sich genau diese Unit geändert, und dazu kommen ein neuer Schlüssel und ein neuer Eintrag in `server.json`; dafür gibt
es den eigenen Ablauf in Abschnitt 14. Von 0.2.0 auf 0.3.0 kommt in dieselbe Unit die Zeile `LogsDirectory=funk` (nur für
die Klartextzeile der Sperrliste, `HEIMSERVER-HAERTUNG.md`, Punkt 7.2); `server.json` braucht nichts Neues, die Sperrliste
läuft mit ihren Voreinstellungen. Von 0.3.0 auf 0.4.0 bleibt `funk-server.service` gleich, und `funk-echo.service` liest
die Adresse aus `/etc/funk/echo.env`. Den Ablauf von 0.2.0 oder 0.3.0 auf 0.4.0 beschreibt Abschnitt 16. Von 0.4.0 auf 0.5.0
kommt in beide Units nur eine Kommentarzeile dazu; sie bleiben liegen (Abschnitt 17).

Deine `/etc/funk/server.json` überschreibt kein Update; kopier `konfig/server.beispiel.json` nie darüber, dort fehlen
deine Adresse und deine Ports. Braucht eine neue Fassung ein neues Feld, steht es in den Hinweisen zum Paket.

**Client:** Jedes neue Setup erkennt die vorhandene Installation und bietet **Aktualisieren** an. Auf deinem PC und bei
jedem anderen Admin immer das Admin-Setup derselben Fassung wie der Client: Passt der Verwaltungsteil nicht zur Fassung,
lädt der Client ihn nicht und meldet `Verwaltungsteil passt nicht zu dieser Fassung - das Admin-Setup dieser Fassung
ausführen.`

**Debian und .NET** (Weg A aus Abschnitt 3), etwa monatlich:

```
apt update && apt full-upgrade -y
systemctl restart funk-server funk-echo
```

Mit Weg B lädst du die Laufzeit mit demselben Skript neu (`dotnet-install.sh --channel 10.0 --runtime dotnet
--install-dir /usr/share/dotnet`).

**Proxmox selbst** aktualisierst du wie gewohnt auf dem Host.

**Das siehst du, wenn es geklappt hat:** Das Journal zeigt wieder `Funk-Server ... bereit` mit derselben Anzeigeform des
Pins, `dotnet --list-runtimes` die neue 10.0.x, und die Clients sind nach kurzer Zeit wieder verbunden.

## 14. Von 0.1.0 auf 0.2.0 (Stufe 1)

Für deinen laufenden Server, eingerichtet am 30.09.2026 mit dem Serverpaket 20260930_1957 (Fassung 0.1.0). Neu in 0.2.0:

- Serverprofil und Profilschlüssel kommen mit dem Setup; im Client trägt niemand mehr etwas ein.
- Neue Mitglieder melden sich mit SC-Name und Discord-Name an (Antrag). Du vergleichst ihre Gerätekennung und gibst frei,
  an der Konsole oder im Client unter **Verwaltung**.
- Du bist Eigentümer. Nur der Eigentümer ernennt Admins, und das nur an der Konsole.
- Verwalten im Client geht nur mit einem Admin-Schlüssel im TPM deines PCs und einem Code aus der Authenticator-App, dazu
  gibt es zehn Notfallcodes.
- Der Server braucht dafür einen weiteren Schlüssel, `funk-totp-schluessel`, eine neue Zeile in seiner Unit und einen
  Eintrag in `server.json`.

Du brauchst: das Serverpaket 0.2.0 (Ordner `linux`), das Admin-Setup 0.2.0 für deinen PC, eine Authenticator-App auf dem
Telefon, eine Shell im Container (`pct enter 150`) und eine auf dem Proxmox-Host. Von Schritt 3 bis Schritt 9 ist der Funk
aus, etwa eine Viertelstunde.

**Erst der Server, dann der Client.**

- Der Client 0.2.0 fragt den Server nach Dingen, die es in 0.1.0 nicht gibt, etwa nach dem Rollenkatalog, sobald du
  **Einstellungen** oder **Wer ist da** öffnest. Ein Server 0.1.0 kennt diese Nachrichten nicht und trennt die Verbindung
  mit einem Protokollfehler.
- Andersherum geht es: Ein Client 0.1.0 verbindet, hört und spricht mit dem Server 0.2.0 wie bisher. Er schickt nur
  Nachrichten, die es schon in 0.1.0 gab, und übergeht die neuen Angaben, die der Server mitschickt. Geprüft auf dem
  Entwicklungsrechner mit den Programmen aus 0.1.0 gegen den Server 0.2.0: Die Echo-Probe aus 0.1.0 meldet `BESTANDEN`,
  und ein Client 0.1.0 in einem ADMIN-Konto blieb verbunden, während an der Konsole `eigentuemer`, `scnamen`,
  `discordname` und `admin-vorsehen` liefen. Zwischen Schritt 9 und Schritt 11 funkt dein Client 0.1.0 also weiter.
- Konten, Geräte und Netze übernimmt der neue Server, wie sie sind. Geprüft mit einem Zustand, den ein Server 0.1.0
  angelegt hatte: Konten, freigegebene Geräte und das Echo-Netz waren unter 0.2.0 alle da.
- `mindestClientVersion` in `server.json` bleibt `0.1.0`. Mit einem höheren Wert weist der Server jeden Client ab, der
  eine kleinere Fassung meldet, also jedes Mitglied, das noch JG TacRelay 0.1.0 hat. JG TacRelay 0.2.0 und `funk-echo`
  0.2.0 melden ihre echte Fassung (0.1.0 meldete fest `0.1.0`). Erst wenn alle das Setup 0.2.0 haben, setzt du in
  `server.json` `"mindestClientVersion": "0.2.0"`, dann `pruefen` und `systemctl restart funk-server`.

**Schritt 1: Paket in den Container.** Wie in Abschnitt 5: auf deinem PC im Ordner des neuen Pakets (PowerShell), dann auf
dem Host. Nimm den Ordner `linux` aus dem neuen Paket, nie `funk-offline` vom Stick.

```
tar -czf funk-linux.tgz -C linux .
scp funk-linux.tgz root@<proxmox>:/root/
```

```
pct push 150 /root/funk-linux.tgz /root/funk-linux.tgz
```

**Das siehst du:** Im Container zeigt `ls -l /root/funk-linux.tgz` die Datei mit der Uhrzeit von eben.

**Schritt 2: Sicherung.** Im Container, solange der alte Server noch läuft:

```
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
cp -a /opt/funk /root/funk-0.1.0
cp /etc/funk/server.json /root/server.json.0.1.0
cp /etc/systemd/system/funk-server.service /root/funk-server.service.0.1.0
```

Die erste Zeile sichert Konten, Geräte und Netze; wie du sie zurückspielst, steht in Abschnitt 12 unter
**Zurückspielen**. Die anderen drei heben Programme, Konfiguration und Unit von 0.1.0 für den Rückweg am Ende dieses
Abschnitts auf.

**Das siehst du:** `Sicherung nach /var/lib/funk/sicherungen/<zeit>`, und `ls /root` zeigt `funk-0.1.0`,
`server.json.0.1.0` und `funk-server.service.0.1.0`.

**Schritt 3: Dienste stoppen.**

```
systemctl stop funk-echo funk-server
systemctl is-active funk-server funk-echo
```

**Das siehst du:** zweimal `inactive`. Dein Client meldet `Der Server wird gewartet. ...` und versucht es danach von selbst
weiter.

**Schritt 4: Dateien tauschen.**

```
tar -xzf /root/funk-linux.tgz -C /opt/funk
chown -R root:root /opt/funk
chmod -R go-w /opt/funk
```

`chmod` gehört dazu wie in Abschnitt 13: Das tgz aus Windows trägt für jede Datei "für alle beschreibbar", `chmod` nimmt
das wieder weg. Deine `/etc/funk/server.json` fasst das nicht an, und so bleibt es: Kopier die neue
`konfig/server.beispiel.json` **nicht** darüber, dort fehlen deine Adresse und deine Ports. Du ergänzt in Schritt 6 nur
zwei Zeilen.

**Das siehst du:** `dotnet /opt/funk/funk-admin.dll hilfe` nennt `totp-schluessel-erzeugen` und einen Block
`Eigentümer und Admins (nur hier, nie im Client):`. Fehlen die beiden, liegt noch das alte Programm dort.

**Schritt 5: TOTP-Schlüssel erzeugen.** Er verschlüsselt die Zweitfaktor-Geheimnisse der Admins. Nur einmal ausführen: Ein
zweiter Aufruf ersetzt ihn, und jeder schon eingerichtete Zweitfaktor wäre nicht mehr lesbar.

```
dotnet /opt/funk/funk-admin.dll totp-schluessel-erzeugen --datei /etc/funk/credentials/funk-totp-schluessel
ls -l /etc/funk/credentials
```

**Das siehst du:** `funk-totp-schluessel (32 Zufallsbytes) geschrieben. Per LoadCredential einbinden, nie in eine
Sicherung.` `ls -l` zeigt vier Dateien; `funk-totp-schluessel` gehört `root root`, hat `-rw-------` und 32 Byte. Auf den
Stick muss er nicht (Abschnitt 6).

**Schritt 6: server.json ergänzen.**

```
nano /etc/funk/server.json
```

Unter die Zeile mit `"geheimnis"` kommt:

```
  "totpSchluessel": "${CREDENTIALS_DIRECTORY}/funk-totp-schluessel",
```

und unter die Zeile mit `"mindestClientVersion"`:

```
  "mitgliedskarte": { "rollenFuerAlle": true },
```

Beide Zeilen enden mit einem Komma, denn danach folgen weitere Einträge. `rollenFuerAlle` heißt: Rollen und
Lieblingsrollen auf der Mitgliedskarte sieht jeder; mit `false` nur Einsatzleitung, Admins und das Mitglied selbst.
`mindestClientVersion` lässt du auf `"0.1.0"`. Speichern mit Strg+O und Eingabetaste, schließen mit Strg+X. Dann:

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
grep -E 'totpSchluessel|mitgliedskarte' /etc/funk/server.json
```

**Das siehst du:** `Konfiguration in Ordnung.` und die beiden neuen Zeilen. Fehlt die Zeile `totpSchluessel`, meldet
`pruefen` stattdessen `Konfiguration ungültig:` und darunter `totpSchluessel fehlt (neu seit 0.2.0, Zweitfaktor der
Admins, 3.9). ...` mit den Schritten 5 bis 7 in einem Satz. Ob die Schlüsseldatei da ist, schaut `pruefen` nicht nach;
das zeigt erst der Start in Schritt 9. Steht dort `Konfiguration nicht lesbar: Konfiguration ist kein gültiges JSON:
...`, fehlt meist ein Komma oder ein Anführungszeichen; dann nennt die Meldung die Zeile (`LineNumber`), gezählt ab 0:
`LineNumber: 2` ist in nano die dritte Zeile (Strg+C zeigt dort die Zeilennummer). Ist ein Feldname vertippt, nennt sie
nur den Namen (`The JSON property 'totpSchlussel' could not be mapped ...`), keine Zeile; dann den Namen in der Datei
suchen.

**Schritt 7: neue Unit für den Server.** Neu ist darin nur die Zeile `LoadCredential=funk-totp-schluessel:...`. Du passt in
dieser Unit nichts an, also:

```
cp /opt/funk/konfig/funk-server.service /etc/systemd/system/
systemctl daemon-reload
systemctl cat funk-server | grep ^LoadCredential
```

**Das siehst du:** vier Zeilen `LoadCredential=...`, die letzte mit `funk-totp-schluessel`. `funk-echo.service` hat sich
nicht geändert; die bleibt, wie sie ist.

**Schritt 8: Uhrzeit prüfen.** Der Zweitfaktor verträgt nur 30 s Abweichung zwischen Server und Telefon. Der Container hat
die Uhr des Hosts, darum auf dem Proxmox-Host (Weboberfläche, Knoten, Shell):

```
chronyc tracking
```

**Das siehst du:** `Leap status` steht auf `Normal`, und `System time` nennt eine Abweichung weit unter einer Sekunde. Kennt
der Host `chronyc` nicht, zeigt `timedatectl` die Zeile `System clock synchronized: yes`. Auf dem Telefon steht die Uhrzeit
auf automatisch.

**Schritt 9: starten und Journal lesen.** Im Container:

```
systemctl start funk-server funk-echo
journalctl -u funk-server -n 30 --no-pager
journalctl -u funk-echo -n 20 --no-pager
```

**Das siehst du:**

- `WARNUNG Kein Eigentümer gesetzt: funk-admin eigentuemer --konto <id> (3.10). Bis dahin ist kein Konto im Client vor
  anderen Admins geschützt.` Das ist richtig so; das erledigt Schritt 10.
- `INFO Funk-Server 0.2.0 bereit: 1 Adresse(n), TCP Bindport 8443 (öffentlich 8443), UDP Bindport 47443 (öffentlich
  47443), TLS 1.3/1.2, Pin XXXX-XXXX-XXXX-XXXX`, mit derselben Anzeigeform des Pins wie vorher.
- Kurz danach `Sitzung beginnt` und `Sitzung aktiv` für deinen Client und den Echo-Funker. Dein Client zeigt wieder
  `Verbunden`, und das Journal von funk-echo zeigt `Bereit im Netz 'Echo' (ID 90)`, mit deiner Netz-ID.
- War der Funk länger als 10 Minuten aus, beginnt jedes Konto mit den Standardnetzen, wie nach jedem so langen Neustart
  (`FUNKPROTOKOLL-v1.md` 6.9). Ein Feld mit einem anderen Netz, etwa Echo, belegst du dann neu.

Wenn nicht:

- `Konfiguration ungültig:` und `totpSchluessel fehlt (neu seit 0.2.0, ...)`: In `server.json` fehlt die Zeile aus
  Schritt 6. Der Dienst endet dann sofort wieder.
- `Start gescheitert: InvalidDataException: funk-totp-schluessel fehlt (/run/credentials/funk-server.service/...)`: Die
  Unit ist noch die alte (Schritt 7, samt `daemon-reload`).
- `status=243/CREDENTIALS`: systemd findet `/etc/funk/credentials/funk-totp-schluessel` nicht (Schritt 5) und startet
  den Dienst gar nicht.
- `Konfiguration ungültig` oder `Konfiguration nicht lesbar`: zurück zu Schritt 6; `pruefen` sagt, was nicht passt.

Danach `systemctl restart funk-server`. Hat systemd nach mehreren Fehlstarts aufgegeben (`start request repeated too
quickly`), vorher `systemctl reset-failed funk-server`.

**Schritt 10: Eigentümer und Namen.** Erst deine Konto-ID:

```
dotnet /opt/funk/funk-admin.dll konten --konfig /etc/funk/server.json
```

Jede Zeile nennt Konto-ID, Stufe, Status, SC-Namen und dahinter `(Discord: <Discord-Name>, <Discord-Benutzername>)`.
Dein Konto ist das mit `ADMIN` und deinem Discord-Benutzernamen aus 0.1.0. SC-Namen und einen Discord-Namen hat es aus
0.1.0 noch nicht; darum steht dort der Name, den du beim Verknüpfen mit `--name` gegeben hast, und `Discord: -`, etwa
`1  ADMIN      AKTIV    Sarevoc (Discord: -, <dein discord-benutzername>)`. `Echo` und `Probe` sind die Konten aus
Abschnitt 11.

Das setzt voraus, dass dein Konto aus 0.1.0 `ADMIN` ist. Steht dort `FUNKER` oder `RAIDLEITER`, antwortet
`eigentuemer` `Eigentümer kann nur ein ADMIN-Konto sein`; dann zuerst:

```
dotnet /opt/funk/funk-admin.dll stufe --konto <deine id> --stufe ADMIN --konfig /etc/funk/server.json
```

Dann, mit deiner Konto-ID:

```
dotnet /opt/funk/funk-admin.dll eigentuemer --konto <deine id> --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll scnamen --konto <deine id> --namen '<dein SC-Name>' --konfig /etc/funk/server.json
```

Bis zu drei SC-Namen gehen, durch Komma getrennt, der erste ist dein Hauptname: `--namen 'Name eins,Name zwei'`. Jeder
hat 2 bis 32 Zeichen aus Buchstaben (auch Umlaute), Ziffern, Leerzeichen, `_`, `-` und Punkt; die Anführungszeichen
braucht es wegen der Leerzeichen.

**Einfache Anführungszeichen** `'...'` nehmen, nicht doppelte: In doppelten verändert bash `$`, den Backtick, `\` und `"`
(aus `"Sa$ra"` würde `Sa`). In einfachen bleibt alles, wie es dasteht. Nur ein einfaches Anführungszeichen im Namen selbst
schreibst du als `'\''`, etwa `--namen 'O'\''Neil'` für O'Neil.

Vertippt, etwa `--name` statt `--namen`, bricht funk-admin ab und ändert nichts: `scnamen kennt --name nicht. Erlaubt:
--konto, --namen, --leeren (dazu --konfig oder --socket, --json-aus). Nichts geändert.` Dann den Befehl richtig
wiederholen. Das gilt für jeden Befehl und jede Option. Alle SC-Namen wegnehmen geht nur ausdrücklich mit `--leeren`.

Deinen Discord-Namen, so wie auf dem Discord-Server der Gruppe (etwa `Mustermann/Max`), setzt nur ein Admin in einer Admin-Sitzung des
Clients oder die Konsole. Deine Admin-Sitzung gibt es erst nach Schritt 12, darum jetzt an der Konsole:

```
dotnet /opt/funk/funk-admin.dll discordname --konto <deine id> --name '<dein Discord-Name>' --konfig /etc/funk/server.json
```

Die Anführungszeichen braucht es, sobald Leerzeichen im Namen stehen; mit einfachen geht es immer (siehe oben), etwa
`--name 'Mustermann/Max'`. Discord-Namen haben 2 bis
32 Zeichen und sind eindeutig wie die SC-Namen, auch gegen andere Groß- und Kleinschreibung. Trägt ihn schon ein anderes
Konto, antwortet der Server `Fehler: Den Discord-Namen <name> trägt schon <SC-Name> (Konto <id>). Nichts geändert.`
Wegnehmen geht mit `--leeren` statt `--name ...`.

**Das siehst du:** `Eigentümer: Sarevoc (Konto <id>)`, `SC-Namen: <dein SC-Name>` und `Discord-Name von <dein SC-Name> (Konto
<id>): <dein Discord-Name>`. `konten` zeigt dann deine Zeile als `1  ADMIN      AKTIV    EIGENTÜMER <dein SC-Name>
(Discord: <dein Discord-Name>, <dein discord-benutzername>)`. Als Eigentümer ist dein Konto geschützt: Kein anderer Admin
ändert es im Client, das geht nur hier an der Konsole. Die Warnung `Kein Eigentümer gesetzt` prüft der Server nur beim
Start; beim nächsten Start steht sie nicht mehr im Journal.

**Schritt 11: Client 0.2.0 auf deinem PC.** JG TacRelay schließen, dann das Admin-Setup ausführen
(`<zeit>_JG-TacRelay-Admin-Setup-0.2.0.exe`). Es ist der Client 0.2.0 mit dem Verwaltungsteil; das normale Setup brauchst
du auf deinem PC nicht.

- Die Seite **JG TacRelay ist schon installiert** nennt `Gefunden: JG TacRelay, Fassung 0.1.0, ...` und `Dieses Setup
  bringt die neuere Fassung 0.2.0.` Wähle **Aktualisieren**.
- Vor dem Installieren steht `Aktualisieren auf Fassung 0.2.0 (bisher 0.1.0), im bisherigen Ordner` und `Dieses Setup
  enthält den Verwaltungsteil. Er bleibt gesperrt, bis der Eigentümer dich zum Admin ernennt.`
- Einstellungen und Geräteschlüssel bleiben, dein Gerät bleibt freigegeben. Profil und Profilschlüssel, die du in 0.1.0
  eingetragen hast, liest 0.2.0 nicht mehr; es nimmt die aus dem Setup.

Dann in JG TacRelay links **Start** (Strg+1), bis 0.5.x der Bereich Verbindung:

- Unter **Einstellungen** (Strg+2) zeigt die Karte **Erweitert: Details der Verbindung** (nur im Admin-Setup)
  `Meine Staffel: funk.example.org (nur IPv4), TCP-Port 8443` und `Signatur stimmt
  (Profilschlüssel XXXX-XXXX-XXXX-XXXX), Version 1, gültig bis 30.09.2027.` Die Kennung ist die Zeile `Fingerabdruck:`
  aus `profilschluessel-erzeugen`.
- Solange der Client getrennt ist, fragt eine Karte nach deinen Namen (`Trag deinen Namen in Star Citizen und deinen
  Namen in Discord ein.`). Trag sie ein und klick **So anmelden und verbinden**: Der Client merkt sie sich und verbindet.
  Einen Antrag gibt es dabei nicht, dein Gerät ist ja schon freigegeben. **Verbinden** allein geht auch; dann kommt die
  Karte beim nächsten Start wieder.

**Das siehst du:** oben `Verbunden`, unter **Einstellungen** in der Karte **Erweitert: Details der Verbindung** die Stufe `Admin`. **Einstellungen** (Strg+2) und
**Wer ist da** (Strg+6) gehen auf, ohne dass die Verbindung abreißt.

**Schritt 12: Verwaltung einrichten.** Der Admin-Schlüssel entsteht im TPM deines PCs und lässt sich nicht kopieren. Ohne
TPM 2.0 gibt es im Client keine Verwaltung; Anträge gibst du dann weiter an der Konsole frei (`antraege`, `freigeben`).
Dieser Weg ist am echten PC noch nicht ausprobiert; hakt es, sag Bescheid.

Ab dem Admin-Setup 0.4.1 zeigt die Karte **Verwaltung einrichten** vorher eine Vorprüfung, die keinen Schlüssel anlegt:
TPM 2.0 (technisch Pflicht; ohne ist der Knopf aus) und ein Windows-Konto mit PIN oder Kennwort (Richtlinie, ein lokales
Konto genügt, ein Microsoft-Konto ist nicht nötig). Eine Windows-PIN braucht das TPM für den Admin-Schlüssel nicht.
Bis 0.5.0 stand dort eine dritte Zeile mit einer Empfehlung zur Laufwerksverschlüsselung; seit dem Admin-Setup 0.5.1 ist
sie fort, wie du deine Daten schützt, entscheidest du selbst.

1. Im Container:

```
dotnet /opt/funk/funk-admin.dll admin-vorsehen --konto <deine id> --konfig /etc/funk/server.json
```

Die Antwort: `<SC-Name> (Konto <id>) ist vorgesehen. Im Client mit Verwaltungsteil jetzt 'Verwaltung einrichten'; danach
die vorgelesene Kennung mit admin-ernennen bestätigen.`

2. Im Client erscheint links **Verwaltung** (Strg+9), ohne Neustart. Dort **Admin-Schlüssel anlegen und beantragen**.
   Danach steht unter `Admin-Kennung zum Vorlesen am Telefon` eine Kennung `XXXX-XXXX-XXXX-XXXX`. Ein anderer Admin liest
   sie dir am Telefon vor; bei dir selbst liest du sie vom Bildschirm ab.
3. Im Container, mit dieser Kennung:

```
dotnet /opt/funk/funk-admin.dll admin-ernennen --konto <deine id> --kennung <kennung aus dem client> --konfig /etc/funk/server.json
```

Die Antwort: `<SC-Name> (Konto <id>) ist Admin (Schlüssel XXXX-XXXX-XXXX-XXXX). Der Client holt das TOTP-Geheimnis bis
<uhrzeit> UTC ab und zeigt den QR-Code; danach den ersten Code eingeben.` Die Uhrzeit ist UTC, bei dir also zwei Stunden
später, im Winter eine. Ab jetzt hast du 10 Minuten. `Kennung passt nicht zum Antrag ...` heißt: vertippt; es hat sich
nichts geändert, noch einmal. `kein offener Antrag: erst admin-vorsehen, dann im Client 'Verwaltung einrichten'` heißt:
Im Client fehlt noch der Klick aus Punkt 2.

4. Im Client wechselt die Verwaltung von selbst zu **Zweitfaktor mit einer Authenticator-App**. Klick **Geheimnis abholen
   (genau einmal)**. Es erscheinen ein QR-Code, darunter der Schlüssel in Vierergruppen zum Abtippen und `Den ersten Code
   bis <uhrzeit> Uhr eingeben.`
5. In der Authenticator-App auf dem Telefon einen neuen Eintrag anlegen und den QR-Code scannen; der Eintrag trägt
   JG TacRelay und deinen SC-Namen. Am besten auf dem Telefon, nicht in der Authenticator-App auf diesem PC: Lägen
   Admin-Schlüssel und Code auf demselben Rechner, fängt Schadcode dort beide. Kein Bildschirmfoto: Wer den QR-Code hat,
   hat das Geheimnis.
6. Den Code aus der App unter **Erster Code aus der App** eintippen, dann **Einrichtung abschließen**.
7. Es erscheinen **Deine zehn Notfallcodes**, nur dieses eine Mal. Getrennt vom Telefon aufbewahren: als sichere Notiz in
   einem Passwortmanager oder ausgedruckt an einem sicheren Ort, nie in einen Chat und nie unter `C:\Claude`. Jeder gilt einmal,
   wenn das Telefon fehlt. Dann **Aufgeschrieben**.
8. Warten, bis die App einen neuen Code zeigt, denn derselbe gilt nicht zweimal. Ihn eintippen und **Verwaltung öffnen**.

**Das siehst du, wenn es geklappt hat:**

- Der Client zeigt `Admin-Sitzung offen bis <uhrzeit> Uhr (oder bis du die Verwaltung schließt oder die Verbindung trennst).` und die
  Bereiche **Anträge**, **Mitglieder** und **Rollenkatalog** (bis 0.11.7 auch **Untergilden**).
- Im Container zeigt `dotnet /opt/funk/funk-admin.dll admins --konfig /etc/funk/server.json` deine Zeile mit `AKTIV`,
  deiner Schlüsselkennung und `Notfallcodes 10`.

**Zu spät?** Hast du das Geheimnis nicht binnen 10 Minuten abgeholt oder den ersten Code nicht rechtzeitig eingegeben:

```
dotnet /opt/funk/funk-admin.dll admin-einrichtung-neu --konto <deine id> --konfig /etc/funk/server.json
```

Die Antwort: `Neues Geheimnis für ... erzeugt. Der Client holt es bis <uhrzeit> UTC ab (Verwaltung einrichten).` Dann im
Client wieder **Geheimnis abholen** und ab Punkt 5 weiter; den alten Eintrag in der Authenticator-App kannst du löschen, er
gilt nicht mehr.

**Code abgelehnt?** Steht `Der Code stimmt nicht oder war schon benutzt. Nimm den nächsten, den die App zeigt, und prüf die
Uhr des Telefons.`, dann genau das: den nächsten Code nehmen und nachsehen, ob die Uhrzeit des Telefons auf automatisch
steht. Nach 5 falschen Codes in 15 Minuten pausiert die Anmeldung 15 Minuten, nach 10 in 24 Stunden ist der Zweitfaktor
gesperrt, bis du ihn an der Konsole entsperrst (`admin-entsperren --konto <id>`).

**Schritt 13, wenn du magst: Echo-Probe.** Wie in Abschnitt 11, "Prüfung ohne Client", mit derselben Zahl
`100000000000000002` wie beim ersten Mal. Mit dem Client: ein Feld mit dem Netz Echo belegen und einen Satz sprechen.

**Antrag ins falsche Konto** (so geschehen am 01.10.2026): Du hast dein Gerät nach einem Antrag mit `freigeben` ohne
`--an-konto` freigegeben und bist jetzt in einem neuen Konto mit deinen Namen, als Funker, statt in deinem ADMIN-Konto
aus 0.1.0. Der Client zeigt unter **Einstellungen** in der Karte **Erweitert: Details der Verbindung** die Stufe `Funker`, und **Verwaltung** fehlt. Erst
nachsehen:

```
dotnet /opt/funk/funk-admin.dll konten --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll geraete --konfig /etc/funk/server.json
```

`konten` zeigt zwei Konten von dir: das alte mit `ADMIN` (ohne Discord-Namen, `Discord: -, <dein
discord-benutzername>`), vielleicht schon mit `EIGENTÜMER`, und das neue mit `FUNKER`, deinem SC-Namen und `Discord:
<dein Discord-Name>`. Die erste Zahl jeder Zeile ist die Konto-ID; unten heißen sie `<alt>` und `<neu>`. `geraete` zeigt je
Gerät die Kennung und `Konto <id>`; dein neues Gerät (TPM) steht beim neuen Konto.

Zwei Wege führen zurück. **Weg A** (ab 0.3.0) hängt dein Gerät an das alte Konto; alles, was am alten Konto hängt (Stufe,
Eigentümer, Admin-Nachweis, Journal, weitere Geräte), bleibt. Den nimmst du, wenn `geraete` beim alten Konto noch ein
Gerät zeigt, das du weiter benutzt, und sonst auch, denn er ist der saubere Weg. **Weg B** geht schon mit 0.2.0: Das
neue Konto wird ADMIN und Eigentümer, das alte geht weg, samt seinen Geräten.

**Weg A, Gerät ans alte Konto,** in genau dieser Reihenfolge. Zuerst wird das alte Konto Eigentümer, auch wenn `konten` es
schon so zeigt (dann ändert sich nichts). Den Eigentümer lehnt `konto-loeschen` ab; vertauschst du später beim Löschen die
Zahlen, bleibt dein ADMIN-Konto so stehen:

```
dotnet /opt/funk/funk-admin.dll eigentuemer --konto <alt> --konfig /etc/funk/server.json
```

Die Antwort: `Eigentümer: <Name> (Konto <alt>)`. Dann das Gerät umhängen:

```
dotnet /opt/funk/funk-admin.dll geraet-umhaengen <gerätekennung> --an-konto <alt> --konfig /etc/funk/server.json
```

Die Antwort: `OK: <gerätekennung> gehört jetzt zu <Name> (Konto <alt>); der Client verbindet sich gleich neu.` und
dahinter `<Name> (Konto <neu>) hat kein Gerät mehr. ...`. Dein Client trennt kurz und verbindet von selbst neu, jetzt im
alten Konto. Vor dem Löschen noch einmal nachsehen:

```
dotnet /opt/funk/funk-admin.dll konten --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll geraete --konto <neu> --konfig /etc/funk/server.json
```

`konten` zeigt `<alt>` mit `ADMIN` und `EIGENTÜMER`, `<neu>` mit `FUNKER`; `geraete --konto <neu>` gibt nur eine leere
Zeile aus. Passt beides, das neue Konto löschen und seine Namen dem alten geben, in dieser Reihenfolge, denn Namen sind
eindeutig:

```
dotnet /opt/funk/funk-admin.dll konto-loeschen --konto <neu> --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll discordname --konto <alt> --name '<dein Discord-Name>' --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll scnamen --konto <alt> --namen '<dein SC-Name>' --konfig /etc/funk/server.json
```

Hast du beim Löschen doch `<alt>` erwischt, kommt `Fehler: Das Konto ist Eigentümer und lässt sich nicht löschen. ...`,
und es ist nichts passiert. Ein altes Gerät aus 0.1.0, das du nicht mehr brauchst, nimmst du danach mit `entfernen
<kennung des alten geräts>` weg.

**Weg B, das neue Konto übernimmt:**

```
dotnet /opt/funk/funk-admin.dll stufe --konto <neu> --stufe ADMIN --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll eigentuemer --konto <neu> --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll konto-loeschen --konto <alt> --konfig /etc/funk/server.json
```

Die Reihenfolge zählt: erst den Eigentümer wechseln, dann löschen. Ab 0.3.0 lehnt `konto-loeschen` den Eigentümer ab
(`Das Konto ist Eigentümer und lässt sich nicht löschen. ...`); 0.2.0 löschte ihn, und der Server stünde ohne Eigentümer
da. Mit dem alten Konto gehen seine Geräte und sein Admin-Nachweis.

**Danach, bei beiden Wegen:** Verwaltung einrichten wie in Schritt 12, mit der Konto-ID, die übrig ist.

**Das siehst du:** `konten` zeigt nur noch ein Konto von dir, `ADMIN` mit `EIGENTÜMER`, deinem SC-Namen und `Discord:
<dein Discord-Name>`. Der Client zeigt die Stufe `Admin`; nach `admin-vorsehen` erscheint links **Verwaltung**.

Damit es nicht wieder passiert, nennt `antraege` ab 0.3.0 ein passendes bestehendes Konto gleich hinter dem Antrag
(Abschnitt 10, Punkt 4), und die Freigabeansicht im Client zeigt es über der Kontoauswahl.

**Zurück auf 0.1.0,** nur wenn der neue Server nicht startet und du heute funken willst. Im Container:

```
systemctl stop funk-echo funk-server
mv /opt/funk /root/funk-0.2.0
cp -a /root/funk-0.1.0 /opt/funk
cp /root/server.json.0.1.0 /etc/funk/server.json
cp /root/funk-server.service.0.1.0 /etc/systemd/system/funk-server.service
systemctl daemon-reload
systemctl start funk-server funk-echo
```

Der Server 0.1.0 braucht die alte `server.json`: Unbekannte Felder wie `totpSchluessel` sind für ihn ein Fehler, und er
startet nicht. Konten, Geräte und Netze liest er weiter, auch wenn 0.2.0 sie inzwischen geschrieben hat; Eigentümer,
SC-Namen und Admin-Nachweis kennt er nicht (geprüft). Den Client 0.2.0 benutzt du dann nicht, er verliert die Verbindung, sobald du Einstellungen oder Wer ist da
öffnest. Zurück geht es mit dem Setup 0.1.0 (es fragt vor dem Rückschritt ausdrücklich nach). Fehlen dort dann Profil und
Profilschlüssel, trägst du sie wie am 30.09. wieder ein: 0.2.0 kennt diese Einstellungen nicht mehr und lässt sie beim
Speichern weg.

## 15. Von 0.2.0 auf 0.3.0 (Härtung und Raidleitung)

Für deinen Server mit 0.2.0 (Abschnitt 14). Willst du gleich auf 0.4.0, nimm Abschnitt 16; er sagt, welche Schritte von hier
du dann noch brauchst. Neu in 0.3.0:

- Der Server sperrt Scanner und Fluten selbst und befristet, mit seiner Sperrliste (`BETRIEB.md`, Abschnitt Sperrliste).
  Mitglieder und die Adressen, von denen sie funken, sperrt er nie. Einzurichten ist dafür nichts.
- Einteilen dürfen nur noch Admins und wer für einen Abend einen Auftrag hat. Die Stufe `RAIDLEITER` heißt im Client
  "Führung": Global, Durchsagen und die Sprechanzeige aller bleiben, Einteilen nicht.
- Im Client 0.3.0: das Brett der Einsatzleitung (Bereich **Raid**, Strg+5), Einsteigen nach Schiff, Trupp und Orga mit
  "Wie letztes Mal", im Verwaltungsteil die Bereiche **Flotte** und **Einsatzleitung**.
- `geraet-umhaengen` repariert einen Antrag, der ins falsche Konto ging (Ende von Abschnitt 14).
- `pruefen` zeigt, ob die Uhr synchronisiert ist.

Du brauchst: das Serverpaket 0.3.0 (Ordner `linux`), das Admin-Setup 0.3.0 für deinen PC und eine Shell im Container
(`pct enter 150`). Ein neuer Schlüssel ist nicht nötig, `server.json` braucht kein neues Feld, und `funk-echo.service`
bleibt, wie sie ist. In `funk-server.service` kommt eine Zeile dazu. Von Schritt 3 bis Schritt 7 ist der Funk aus, ein
paar Minuten.

**Erst der Server, dann der Client.**

- Clients 0.2.0 funken mit dem Server 0.3.0 weiter. Geprüft auf dem Entwicklungsrechner ohne TLS: `funk-echo` 0.2.0 und
  die Echo-Probe 0.2.0 gegen den Server 0.3.0 (`BESTANDEN`), auch in einem Konto `RAIDLEITER`, während an der Konsole
  Aufträge, eine Einheit und Sperren der Selbstwahl liefen. Brett, Einsteigen nach Einheiten und "Wie letztes Mal" haben
  sie nicht.
- Ein Client 0.3.0 schickt einem Server 0.2.0 die neuen Anfragen nicht und bleibt verbunden; wo die Antwort stünde, etwa
  unter "Alle Mitglieder" auf dem Brett, steht dann `Der Server hat noch Fassung 0.2.0 und kennt das nicht. Erst den
  Server aktualisieren, dann geht es.` Das ist am
  Code geprüft, mit den Programmen 0.2.0 nicht gemessen; deshalb trotzdem erst der Server.
- `mindestClientVersion` in `server.json` bleibt, wie sie ist.

**Schritt 1: Paket in den Container.** Wie in Abschnitt 14: auf deinem PC im Ordner des neuen Pakets (PowerShell), dann auf
dem Host. Nimm den Ordner `linux` aus dem neuen Paket, nie `funk-offline` vom Stick.

```
tar -czf funk-linux.tgz -C linux .
scp funk-linux.tgz root@<proxmox>:/root/
```

```
pct push 150 /root/funk-linux.tgz /root/funk-linux.tgz
```

**Das siehst du:** Im Container zeigt `ls -l /root/funk-linux.tgz` die Datei mit der Uhrzeit von eben.

**Schritt 2: Sicherung.** Im Container, solange der alte Server noch läuft:

```
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
cp -a /opt/funk /root/funk-0.2.0
cp /etc/funk/server.json /root/server.json.0.2.0
cp /etc/systemd/system/funk-server.service /root/funk-server.service.0.2.0
```

Die erste Zeile sichert Konten, Geräte, Netze, Mitglieder und Journal (zurückspielen: Abschnitt 12). Die anderen drei heben
Programme, Konfiguration und Unit von 0.2.0 für den Rückweg am Ende dieses Abschnitts auf.

**Das siehst du:** `Sicherung nach /var/lib/funk/sicherungen/<zeit>`, und `ls /root` zeigt `funk-0.2.0`,
`server.json.0.2.0` und `funk-server.service.0.2.0`.

**Schritt 3: Dienste stoppen.**

```
systemctl stop funk-echo funk-server
systemctl is-active funk-server funk-echo
```

**Das siehst du:** zweimal `inactive`. Dein Client meldet `Der Server wird gewartet. ...` und versucht es danach von selbst
weiter.

**Schritt 4: Dateien tauschen.**

```
tar -xzf /root/funk-linux.tgz -C /opt/funk
chown -R root:root /opt/funk
chmod -R go-w /opt/funk
```

`chmod` gehört dazu wie in Abschnitt 13. Deine `/etc/funk/server.json` bleibt, wie sie ist; die neue
`konfig/server.beispiel.json` kopierst du **nicht** darüber.

**Das siehst du:** `dotnet /opt/funk/funk-admin.dll hilfe` nennt `sperrliste` und am Ende den Block `Einsatzleitung und
Flotte:`. Fehlen sie, liegt noch das alte Programm dort.

**Schritt 5: neue Unit für den Server.** Neu sind darin nur `LogsDirectory=funk` und `LogsDirectoryMode=0750`. Du passt
in dieser Unit nichts an, also:

```
cp /opt/funk/konfig/funk-server.service /etc/systemd/system/
systemctl daemon-reload
systemctl cat funk-server | grep ^LogsDirectory
```

**Das siehst du:** `LogsDirectory=funk` und `LogsDirectoryMode=0750`. Gebraucht wird die Zeile nur für die Klartextzeile
der Sperrliste (unten, wahlweise); `funk-echo.service` hat sich nicht geändert.

**Schritt 6: prüfen.**

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
ls /usr/share/zoneinfo/Europe/Berlin
```

**Das siehst du:** zuerst eine Zeile zur Uhr, am Ende `Konfiguration in Ordnung.`, und `ls` nennt den Pfad noch einmal.
Die Zeile zur Uhr ist eine von dreien:

- `Uhr: synchronisiert (Kernel, höchstens ... ms Abweichung).` So soll es sein.
- `Uhr: vom Kernel als nicht synchronisiert gemeldet (siehe Warnung).`, darunter `WARNUNG: Der Kernel meldet die Uhr als
  nicht synchronisiert (adjtimex TIME_ERROR): ...` und am Ende `Konfiguration lesbar, aber bitte die Warnung oben
  ansehen.` Dann auf dem Proxmox-Host `chronyc tracking` wie in Abschnitt 14, Schritt 8, und die Zeitsynchronisierung
  des Hosts richten; der Container hat die Uhr des Hosts. Mit falscher Uhr passt kein Code aus der Authenticator-App.
- `Uhr: Synchronisierung hier nicht lesbar (anderes System oder ProtectClock); ...`: Der Container lässt das Lesen nicht zu.
  Dann zeigt `chronyc tracking` auf dem Host den Stand. Welche der Zeilen dein Container zeigt, ist noch nicht gemessen.

Meldet `ls` stattdessen `No such file or directory`, fehlt die Zeitzone. Dann rechnet der Server die Zeiten der Aufträge
(Schritt 11) in der Zeitzone des Containers statt in deutscher Zeit, meist in UTC. Abhilfe: `apt install tzdata`.

**Schritt 7: starten und Journal lesen.**

```
systemctl start funk-server funk-echo
journalctl -u funk-server -n 30 --no-pager
```

**Das siehst du:**

- `INFO Funk-Server 0.3.0 bereit: 1 Adresse(n), TCP Bindport 8443 (öffentlich 8443), UDP Bindport 47443 (öffentlich
  47443), TLS 1.3/1.2, Pin XXXX-XXXX-XXXX-XXXX`, mit derselben Anzeigeform des Pins wie vorher.
- Keine Zeile `WARNUNG Die Uhr des Servers ...`. Steht sie da, wie in Schritt 6 die Zeitsynchronisierung des Hosts prüfen.
- Kurz danach `Sitzung beginnt` und `Sitzung aktiv` für deinen Client und den Echo-Funker. Dein Client 0.2.0 zeigt wieder
  `Verbunden`.
- Hast du in Abschnitt 14, Schritt 10 noch keinen Eigentümer gesetzt, etwa weil dein Antrag ins falsche Konto ging,
  steht dort auch `WARNUNG Kein Eigentümer gesetzt: funk-admin eigentuemer --konto <id> (3.10). Bis dahin ist kein Konto
  im Client vor anderen Admins geschützt.` Das ist dann zu erwarten; Schritt 9 setzt ihn mit Weg A.

Wenn nicht: `Start gescheitert: ...` oder `Konfiguration ...` nennen den Grund; danach `systemctl restart funk-server`. Hat
systemd nach mehreren Fehlstarts aufgegeben (`start request repeated too quickly`), vorher `systemctl reset-failed
funk-server`. Startet er gar nicht, geht es zurück auf 0.2.0 (am Ende dieses Abschnitts).

**Schritt 8: Sperrliste ansehen.**

```
dotnet /opt/funk/funk-admin.dll sperrliste --konfig /etc/funk/server.json
```

**Das siehst du:** `Keine Sperren (<n> Adressen beobachtet).` Später stehen dort vielleicht Sperren, und im Journal je
Sperre eine Zeile wie `WARNUNG Sperrliste: p4#... für 5 min gesperrt (Stufe 1, Anlass HANDSHAKE, 10 Punkte)`, immer nur
mit Kennung, nie mit Adresse. Das sind Scanner aus dem Netz; du musst nichts tun.

Kommt ein Mitglied nicht mehr herein, lass dir seine Adresse nennen (etwa von einer Seite wie "Wie ist meine IP") und hebe
die Sperre auf:

```
dotnet /opt/funk/funk-admin.dll entsperren --ip <adresse des mitglieds> --konfig /etc/funk/server.json
```

Die Antwort ist `Sperre für p4#... aufgehoben.` oder `Für diese Adresse (p4#...) gibt es keine Sperre. Nichts geändert.`
Mehr dazu in `HEIMSERVER-HAERTUNG.md`, Abschnitt 8. Hast du dort fail2ban für den Funk eingerichtet (Punkt 7.2), sperrt
zusätzlich der Host die Adresse, und zwar für alles, auch für Foundry; `entsperren` hebt das nicht auf. Dann auf dem
Proxmox-Host noch `fail2ban-client unban <adresse des mitglieds>`.

Wahlweise, nichts davon ist Pflicht:

- Ausnahmen (etwa eine Überwachung im Heimnetz) und die Klartextzeile für fail2ban stellst du in `server.json` im
  Abschnitt `"sperrliste"` ein; die Felder stehen in `konfig/server.beispiel.json` und `BETRIEB.md`. Nach jeder Änderung
  `dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen && systemctl restart funk-server`: Dann startet
  der Server nur neu, wenn die Prüfung besteht.
- fail2ban für den Funk lässt du am Heimserver besser weg (`HEIMSERVER-HAERTUNG.md`, Punkt 7.2: am Host ohne Durchlass
  für Mitglieder und mit Foundry gesperrt). fail2ban auf dem Proxmox-Host für nginx und SSH: dort Punkt 7 und 7.1, mit
  den Bedingungen, die dort stehen.
- Den Rest des Hosts härten (FRITZ!Box, Proxmox, SSH, Updates, nginx vor Foundry): `HEIMSERVER-HAERTUNG.md`, Punkt für
  Punkt.

**Schritt 9: dein Konto reparieren,** falls `konten` noch zwei Konten von dir zeigt (Antrag ins falsche Konto, 01.10.2026):
Ende von Abschnitt 14, **Weg A**. Danach `admin-vorsehen` wie in Abschnitt 14, Schritt 12, Punkt 1. Die Verwaltung
richtest du dann mit dem Client 0.3.0 ein (Schritt 10).

**Schritt 10: Client 0.3.0 auf deinem PC.** JG TacRelay schließen, dann das Admin-Setup ausführen
(`<zeit>_JG-TacRelay-Admin-Setup-0.3.0.exe`).

- Die Seite **JG TacRelay ist schon installiert** nennt `Gefunden: JG TacRelay, Fassung 0.2.0, ...` und `Dieses Setup
  bringt die neuere Fassung 0.3.0.` Wähle **Aktualisieren**.
- Einstellungen, Namen und Geräteschlüssel bleiben, dein Gerät bleibt freigegeben.
- Nimm das Admin-Setup, nicht das normale: Einen Verwaltungsteil aus 0.2.0 lädt der Client 0.3.0 nicht und meldet
  `Verwaltungsteil passt nicht zu dieser Fassung - das Admin-Setup dieser Fassung ausführen.`

Ist deine Verwaltung noch nicht eingerichtet, weiter mit Abschnitt 14, Schritt 12, ab Punkt 2. Neu in 0.3.0: Verlässt du
den Bereich **Verwaltung**, bevor die Einrichtung fertig ist, ist das Geheimnis fort. Steht es schon in der
Authenticator-App, gibst du einfach den ersten Code ein; sonst `admin-einrichtung-neu` wie dort unter **Zu spät?**. Fragt die
Verwaltung bei einem heiklen Schritt nach einem frischen Code und das Telefon fehlt, geht auch ein Notfallcode.

**Das siehst du:** oben `Verbunden`, unter **Einstellungen** in der Karte **Erweitert: Details der Verbindung** die Stufe `Admin`, links **Raid** (Strg+5), denn
als Admin teilst du auch ohne Auftrag ein, und nach der Anmeldung in der Verwaltung die Bereiche **Anträge**,
**Mitglieder**, **Rollenkatalog**, **Flotte** und **Einsatzleitung** (bis 0.11.7 auch **Untergilden**).

Die Mitglieder bekommen das normale Setup 0.3.0 (`<zeit>_JG-TacRelay-Setup-0.3.0.exe`), ebenfalls mit **Aktualisieren**.

**Schritt 11: Einsatzleitung für einen Abend.** Ein Konto `RAIDLEITER` teilt nach dem Update nicht mehr ein. Wer an einem
Abend einteilen soll, bekommt einen Auftrag: im Client unter **Verwaltung**, **Einsatzleitung**, **Einsatzleitung
vergeben** (mit frischem Code) oder an der Konsole:

```
dotnet /opt/funk/funk-admin.dll auftrag --konto <id> --grund 'Op Nachtfalke' --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll auftraege --konfig /etc/funk/server.json
```

Ohne `--bis` gilt der Auftrag bis zum nächsten 04:00 Uhr, höchstens 24 Stunden. Mit `--bis 23:30` endet er früher, mit
`--von 20:00` beginnt er erst dann, und mit `--von '2026-10-09 20:00'` planst du bis 14 Tage voraus. Nicht an ein
ADMIN-Konto (ein Admin teilt ohnehin ein) und nicht an ein gesperrtes; höchstens vier Aufträge zugleich.

**Das siehst du:** `Einsatzleitung für <Name> (Konto <id>) von <TT.MM. HH:MM> bis <TT.MM.> 04:00.`, und `auftraege`
zeigt je Auftrag eine Zeile wie `Mustermann (Konto 2): läuft, 01.10. 20:15 bis 02.10. 04:00, von Konsole, Op Nachtfalke, Rechte
GLOBAL_SENDEN DURCHSAGE DURCHSAGE_ALLE MEHRFACH_SENDEN ZUWEISEN SPRECHANZEIGE_ALLE`. Die Zeit hinter `von` ist jetzt, in
deutscher Zeit; steht dort eine Zeit zwei Stunden früher (im Winter eine), fehlt die Zeitzone (Schritt 6). Der
Beauftragte sieht im Client 0.3.0 links **Raid**, ohne Neustart; 15 Minuten vor dem Ende warnt ihn der Client.

Vorzeitig beenden geht mit `auftrag-entziehen --konto <id>`, die Antwort ist `Auftrag von <Name> (Konto <id>) entzogen.`

Soll jemand dauerhaft einteilen, etwa ein fester Stellvertreter, gibst du ihm statt eines Auftrags das Recht `ZUWEISEN`.
`rechte` setzt die ganze Liste, für ein Konto `RAIDLEITER` also alles, was es behalten soll:

```
dotnet /opt/funk/funk-admin.dll rechte --konto <id> --rechte GLOBAL_SENDEN,DURCHSAGE,DURCHSAGE_ALLE,MEHRFACH_SENDEN,SPRECHANZEIGE_ALLE,ZUWEISEN --konfig /etc/funk/server.json
```

**Schritt 12: alte Sperre der Selbstwahl.** Hast du unter 0.2.0 die Selbstwahl gesperrt (`einstellung --selbstwahl aus`),
bleibt diese Sperre nach dem Update stehen, ohne Frist. `netzliste` zeigt es in der letzten Zeile mit `Selbstwahl False`.
Aufheben:

```
dotnet /opt/funk/funk-admin.dll einstellung --selbstwahl an --konfig /etc/funk/server.json
```

Die Antwort ist `OK, zustandsVersion <n>`, und `netzliste` zeigt `Selbstwahl True`. Neue Sperren (auf dem Brett **Wechsel
sperren** und **Ganz sperren**, an der Konsole `einstellung --selbstwahl aus`) enden nach 12 Stunden von selbst, eine vom
Brett auch mit dem Auftrag dessen, der sie gesetzt hat. Neu ist: Bei gesperrter Selbstwahl kann ein Mitglied seinen Platz
nicht mehr selbst aufgeben, das zählt als Wechsel.

**Schritt 13, wenn du magst: Flotte anlegen.** Auf eine leere Flotte (keine Schiffe, keine Rollen) legt die Konsole die
alte Startvorlage an:

```
dotnet /opt/funk/funk-admin.dll flotte-vorlage --konfig /etc/funk/server.json
```

**Das siehst du:** `<alte Vorlage> übernommen: <n> Schritte. Annahmen, bitte prüfen:` und darunter sechs Zeilen mit
`- `. Die Annahmen prüft der Admin. Hat dein Server schon Schiffe oder Rollen, lehnt der
Befehl ab (`Fehler: Die Flotte ist nicht leer (...); die Vorlage gehört auf eine leere Flotte. Nichts geändert.`). Dann im
Client unter **Verwaltung**, **Flotte**: Die alte Startvorlage kommt in den Entwurf, übernommen wird erst mit
**Übernehmen** (frischer Code). Die alte Startvorlage gibt es seit dem Umbau zu JG TacRelay (19.10.2026) nicht mehr; heute
gilt `flotte-vorlage --name starcitizen` (Vorlage Star Citizen), und die gehört auf eine Flotte ohne Kanalnetze.

**Sicherung und Aufträge.** `sicherung` nimmt Aufträge nicht mit, mit Absicht: Aus einer zurückgespielten Sicherung käme
sonst ein entzogener Auftrag zurück. Nach dem Zurückspielen (Abschnitt 12) vergibst du laufende Aufträge neu. Eine
Proxmox-Sicherung (vzdump) trägt sie dagegen mit; nach so einem Zurückspielen `auftraege` ansehen und, was nicht mehr
gelten soll, mit `auftrag-entziehen` beenden.

**Zurück auf 0.2.0,** nur wenn der neue Server nicht startet und du heute funken willst. Im Container:

```
systemctl stop funk-echo funk-server
mv /opt/funk /root/funk-0.3.0
cp -a /root/funk-0.2.0 /opt/funk
cp /root/server.json.0.2.0 /etc/funk/server.json
cp /root/funk-server.service.0.2.0 /etc/systemd/system/funk-server.service
systemctl daemon-reload
systemctl start funk-server funk-echo
dotnet /opt/funk/funk-admin.dll netzliste --konfig /etc/funk/server.json
```

Den Zustand musst du dafür nicht zurückspielen: Der Server 0.2.0 liest weiter, was 0.3.0 geschrieben hat. Geprüft auf dem
Entwicklungsrechner: Ein Server 0.3.0 lief auf dem Zustand von 0.2.0, bekam einen Auftrag, einen Trupp und eine Sperre der
Selbstwahl; danach startete der Server 0.2.0 auf diesen Dateien, Konten, Netze, Rollen und Einheiten waren da, und die
Echo-Probe meldete `BESTANDEN`. Was 0.2.0 nicht kennt (Aufträge, Art und Mindestbesetzung der Einheiten, die Frist einer
Sperre), lässt er beim nächsten Schreiben weg. Eine Sperre der Selbstwahl gilt unter 0.2.0 ohne Frist: Zeigt `netzliste`
in der letzten Zeile `Selbstwahl False`, hebst du sie mit `einstellung --selbstwahl an` auf. `sperrliste.json` lässt 0.2.0
einfach liegen.

`server.json.0.2.0` brauchst du nur, wenn du in `server.json` inzwischen einen Abschnitt `"sperrliste"` eingetragen hast:
Unbekannte Felder sind für den Server 0.2.0 ein Fehler, er meldet `Konfiguration nicht lesbar: ... The JSON property
'sperrliste' could not be mapped ...` und startet nicht (gemessen). Die Clients 0.3.0 bleiben am Server 0.2.0
verbunden (am Code geprüft, siehe oben); was neu ist in 0.3.0, etwa "Alle Mitglieder" auf dem Brett oder Einsatzleitung
und Flotte im Verwaltungsteil, geht dann nicht.

## 16. Auf 0.4.0 (Einrichten und Umzug)

Für deinen laufenden Server, ob er heute 0.2.0 hat oder schon 0.3.0 (Abschnitt 15). Lässt du 0.3.0 aus, holt dieser
Abschnitt nach, was von dort nötig ist; die Schritte, die nur dann gelten, sagen es. Neu in 0.4.0:

- Der Client übernimmt ein neueres, von dir unterschriebenes Serverprofil vom Server; die Profilkarte zeigt dann `Vom Server
  übernommen, Version N.` Darauf baut der Umzug auf einen anderen Server ohne neue Setups (`UMZUG.md`). Ein Profil, in dem
  der Pin des liefernden Servers fehlt, übernimmt er nicht.
- Bei der ersten Verbindung versucht der Client nach einem fremden Pin erst die übrigen Hosts des Profils, bevor er "Server
  nicht vertrauenswürdig" meldet.
- `funk-einrichten.sh` richtet einen neuen Server mit einem Befehl ein (`EINRICHTEN.md`), etwa bei einem künftigen Betreiber oder in deinem
  Probelauf im Wegwerf-Container. Für dieses Update brauchst du es nicht: Dein Server ist von Hand eingerichtet, und das
  Skript ist auf Linux noch nicht gelaufen.
- Neue Befehle von `funk-admin`: `pruefen --vollstaendig`, `profil-anfrage`, `profil-erneuern`, `umzug-packen`,
  `umzug-einspielen`, `erster-admin`, `konfig-schreiben`, `einrichten` und `geraete --nicht-seit` (`BETRIEB.md`).
- Die Befehle, die Schlüssel erzeugen, ersetzen eine vorhandene Datei nur noch mit `--ersetzen`, sonst Exit 3 und nichts
  geändert (Abschnitt 6).
- `profil-neu-laden` antwortet mit der Version (`Profil Version 1 geladen.`) und warnt wie der Start, wenn im Profil der
  Pin dieses Servers fehlt oder es einen anderen TCP-Port nennt als `server.json`.
- `funk-echo.service` liest die Adresse aus `/etc/funk/echo.env` (Abschnitt 11). Deine Unit mit der Adresse in der Zeile
  `ExecStart` läuft weiter.
- Im Client: die Fußzeile nennt Strg+5 (Raid) und Strg+9 (Verwaltung), wenn es die Bereiche gibt, und in der Flotte
  bricht die Auswahl je Feld lange Netznamen um, statt sie abzuschneiden.

Du brauchst: das Serverpaket 0.4.0 (Ordner `linux`), das Admin-Setup 0.4.0 für deinen PC und eine Shell im Container
(`pct enter 150`). Kein neuer Schlüssel, kein neues Feld in `server.json`. Von Schritt 3 bis Schritt 8 ist der Funk aus,
ein paar Minuten.

**Erst der Server, dann der Client.**

- Geprüft auf dem Entwicklungsrechner, ohne TLS: Der Server 0.4.0 lief auf einem Zustand, den die Programme 0.2.0
  geschrieben hatten, mit unveränderter `server.json`. `pruefen` bestand, `funk-echo` 0.2.0 und 0.3.0 waren im Netz Echo
  bereit, und die Echo-Probe 0.2.0, 0.3.0 und 0.4.0 meldete jeweils `BESTANDEN`. Deine Clients 0.2.0 oder 0.3.0 funken
  also weiter, bis sie das neue Setup haben.
- Am Heimserver ist 0.4.0 noch nicht gelaufen, und `funk-admin pruefen --vollstaendig` (Schritt 7) ist auf Linux neu. Was
  dort anders aussieht als hier, schick Claude; die Ausgabe enthält keine Geheimnisse.
- `mindestClientVersion` in `server.json` bleibt, wie sie ist. Auf `0.4.0` setzt du sie erst vor einem Umzug, wenn alle
  das Setup 0.4.0 haben (`UMZUG.md`, Schritt 1).

**Schritt 1: Paket in den Container.** Wie in Abschnitt 15: auf deinem PC im Ordner des neuen Pakets (PowerShell), dann
auf dem Host. Nimm den Ordner `linux` aus dem neuen Paket, nie `funk-offline` vom Stick.

```
tar -czf funk-linux.tgz -C linux .
scp funk-linux.tgz root@<proxmox>:/root/
```

```
pct push 150 /root/funk-linux.tgz /root/funk-linux.tgz
```

**Das siehst du:** Im Container zeigt `ls -l /root/funk-linux.tgz` die Datei mit der Uhrzeit von eben.

**Schritt 2: Fassung ansehen und sichern.** Im Container, solange der alte Server noch läuft:

```
grep -m1 -o 'funk-server/[0-9.]*' /opt/funk/funk-server.deps.json
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
cp -a /opt/funk /root/funk-vor-0.4.0
cp /etc/funk/server.json /root/server.json.vor-0.4.0
cp /etc/systemd/system/funk-server.service /root/funk-server.service.vor-0.4.0
cp /etc/systemd/system/funk-echo.service /root/funk-echo.service.vor-0.4.0
```

**Das siehst du:** Die erste Zeile zeigt `funk-server/0.2.0` oder `funk-server/0.3.0`, deine laufende Fassung; merk sie
dir für Schritt 9. Dann `Sicherung nach /var/lib/funk/sicherungen/<zeit>`, und `ls /root` zeigt `funk-vor-0.4.0`,
`server.json.vor-0.4.0`, `funk-server.service.vor-0.4.0` und `funk-echo.service.vor-0.4.0`. Gibt es `/root/funk-vor-0.4.0`
schon (ein zweiter Anlauf), lass die Zeile mit `cp -a` weg, sonst landet die Kopie darin statt daneben.

**Schritt 3: Dienste stoppen.**

```
systemctl stop funk-echo funk-server
systemctl is-active funk-server funk-echo
```

**Das siehst du:** zweimal `inactive`. Dein Client meldet `Der Server wird gewartet. ...` und versucht es danach von selbst
weiter.

**Schritt 4: Dateien tauschen.**

```
tar -xzf /root/funk-linux.tgz -C /opt/funk
chown -R root:root /opt/funk
chmod -R go-w /opt/funk
grep -m1 -o 'funk-server/[0-9.]*' /opt/funk/funk-server.deps.json
```

`chmod` gehört dazu wie in Abschnitt 13. Deine `/etc/funk/server.json` bleibt, wie sie ist; die neue
`konfig/server.beispiel.json` kopierst du **nicht** darüber.

**Das siehst du:** `funk-server/0.4.0`. Steht dort noch die alte Fassung, liegt das alte Programm dort; dann Schritt 1
wiederholen. `dotnet /opt/funk/funk-admin.dll hilfe` nennt jetzt auch `umzug-packen` und `sperrliste`.

**Schritt 5: Unit für den Server.** Du passt in dieser Unit nichts an, also:

```
cp /opt/funk/konfig/funk-server.service /etc/systemd/system/
systemctl daemon-reload
systemctl cat funk-server | grep ^LogsDirectory
```

**Das siehst du:** `LogsDirectory=funk` und `LogsDirectoryMode=0750`. Kamst du von 0.3.0, standen beide Zeilen schon
darin, und die Unit ist dieselbe; von 0.2.0 sind sie neu (Abschnitt 15, Schritt 5). Gebraucht werden sie nur für die
Klartextzeile der Sperrliste (`HEIMSERVER-HAERTUNG.md`, Punkt 7.2).

**Schritt 6, wenn du magst: Adresse des Echo-Funkers in eine eigene Datei.** Deine `funk-echo.service` läuft mit 0.4.0
weiter wie bisher; du kannst diesen Schritt auslassen. Nimmst du die neue Unit, steht die Adresse künftig in
`/etc/funk/echo.env`, und bei späteren Updates kopierst du die Unit ohne `nano`. Erst die Adresse aus deiner bisherigen
Unit ablesen, dann die Datei anlegen und die Unit tauschen:

```
grep -o -e '--adresse [0-9.]*' /root/funk-echo.service.vor-0.4.0
echo 'FUNK_ECHO_ADRESSE=192.168.1.250' > /etc/funk/echo.env
cp /opt/funk/konfig/funk-echo.service /etc/systemd/system/
systemctl daemon-reload
```

**Das siehst du:** Die erste Zeile zeigt `--adresse 192.168.1.250`. Steht dort eine andere Adresse, schreibst du genau
diese in die zweite Zeile. `cat /etc/funk/echo.env` zeigt danach `FUNK_ECHO_ADRESSE=` mit deiner Adresse. Gestartet wird
der Echo-Funker in Schritt 8.

**Schritt 7: prüfen.**

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
ls /usr/share/zoneinfo/Europe/Berlin
cp /opt/funk/konfig/profilschluessel.txt /etc/funk/
dotnet /opt/funk/funk-admin.dll pruefen --vollstaendig --konfig /etc/funk/server.json
```

**Das siehst du:**

- `funk-server ... pruefen`: zuerst eine Zeile zur Uhr, am Ende `Konfiguration in Ordnung.` Die drei möglichen
  Uhrzeilen erklärt Abschnitt 15, Schritt 6. `ls` nennt den Pfad noch einmal; fehlt er, rechnet der Server die Zeiten der
  Aufträge nicht in deutscher Zeit, Abhilfe `apt install tzdata`.
- `cp` legt den öffentlichen Teil deines Profilschlüssels neben `server.json`; damit prüft `funk-admin pruefen` auch die
  Unterschrift des Profils. Die Datei ist nicht geheim, `profil.key` bleibt auf dem Stick.
- `funk-admin pruefen --vollstaendig` schreibt je Befund eine Zeile, die mit `OK`, `HINWEIS`, `WARNUNG` oder `FEHLER`
  beginnt, und am Ende `Ergebnis: 0 Fehler, ...`. Darunter stehen `OK      Pin des Serverschlüssels XXXX-XXXX-XXXX-XXXX
  steht im Profil.` mit deinem Pin, `OK      Signatur des Profils stimmt (Profilschlüssel XXXX-XXXX-XXXX-XXXX).` mit der
  Kennung, die auch die Profilkarte im Client zeigt, und, weil der Dienst gerade steht, `OK      TCP 8443 auf
  192.168.1.250 frei.` und dasselbe für UDP 47443. Zur Uhr kommt eine von drei Zeilen: `OK      Uhr synchronisiert
  (NTP).`, `WARNUNG Uhr nicht synchronisiert: ...` oder `HINWEIS Uhrzeit hier nicht prüfbar ...`. Welche dein Container
  zeigt, ist noch nicht gemessen; bei der Warnung und beim Hinweis zeigt `chronyc tracking` auf dem Host den Stand
  (Abschnitt 14, Schritt 8).
- `pruefen` ändert nichts und startet nichts. Steht dort ein `FEHLER`, geh trotzdem weiter zu Schritt 8: Der Server lief
  bis eben mit denselben Dateien, und das Journal zeigt beim Start, ob etwas nicht stimmt. Die `FEHLER`-Zeile schickst
  du Claude.

**Schritt 8: starten und Journal lesen.**

```
systemctl start funk-server funk-echo
journalctl -u funk-server -n 30 --no-pager
journalctl -u funk-echo -n 10 --no-pager
```

**Das siehst du:**

- `INFO Serverprofil Version 1 mit 2 Pins geladen` und `INFO Funk-Server 0.4.0 bereit: 1 Adresse(n), TCP Bindport 8443
  (öffentlich 8443), UDP Bindport 47443 (öffentlich 47443), TLS 1.3/1.2, Pin XXXX-XXXX-XXXX-XXXX`, mit derselben
  Anzeigeform des Pins wie vorher.
- Keine Zeile `WARNUNG Der Pin des Serverschlüssels ...` und keine `WARNUNG Die Uhr des Servers ...`.
- Kurz danach `Sitzung beginnt` und `Sitzung aktiv` für deinen Client und den Echo-Funker. Dein Client zeigt wieder
  `Verbunden`.
- Hast du noch keinen Eigentümer gesetzt, etwa weil dein Antrag ins falsche Konto ging, steht dort auch `WARNUNG Kein
  Eigentümer gesetzt: funk-admin eigentuemer --konto <id> (3.10). ...`. Das richtet Schritt 9.
- Das Journal von `funk-echo` zeigt `Bereit im Netz 'Echo' (ID 90)`.

Wenn nicht: `Start gescheitert: ...` oder `Konfiguration ...` nennen den Grund; danach `systemctl restart funk-server`.
Hat systemd nach mehreren Fehlstarts aufgegeben (`start request repeated too quickly`), vorher `systemctl reset-failed
funk-server`. Startet er gar nicht, geht es zurück (am Ende dieses Abschnitts). Erreicht der Echo-Funker den Server nach
Schritt 6 nicht, stimmt die Adresse in `/etc/funk/echo.env` nicht.

**Schritt 9, nur wenn Schritt 2 `funk-server/0.2.0` zeigte:** Dann fehlt dir, was Abschnitt 15 nach dem Start vorsieht,
und du holst es jetzt nach, in dieser Reihenfolge:

- Abschnitt 15, Schritt 8: Sperrliste ansehen (`sperrliste`, `entsperren --ip`).
- Abschnitt 15, Schritt 9: dein Konto reparieren, falls `konten` noch zwei Konten von dir zeigt, mit **Weg A** am Ende von
  Abschnitt 14; danach `admin-vorsehen` wie in Abschnitt 14, Schritt 12, Punkt 1.
- Abschnitt 15, Schritte 11 bis 13: Einsatzleitung als Auftrag statt `RAIDLEITER`, eine alte Sperre der Selbstwahl
  aufheben, wahlweise die Flotte anlegen, dazu der Absatz **Sicherung und Aufträge**.

Schritt 10 aus Abschnitt 15 lässt du aus; an seine Stelle tritt Schritt 10 hier (Admin-Setup 0.4.0 statt 0.3.0). Kamst
du von 0.3.0, hast du all das schon und gehst gleich zu Schritt 10.

**Schritt 10: Client 0.4.0 auf deinem PC.** JG TacRelay schließen, dann das Admin-Setup ausführen
(`<zeit>_JG-TacRelay-Admin-Setup-0.4.0.exe`).

- Die Seite **JG TacRelay ist schon installiert** nennt `Gefunden: JG TacRelay, Fassung 0.2.0, ...` (oder `0.3.0`) und
  `Dieses Setup bringt die neuere Fassung 0.4.0.` Wähle **Aktualisieren**.
- Einstellungen, Namen und Geräteschlüssel bleiben, dein Gerät bleibt freigegeben.
- Nimm das Admin-Setup, nicht das normale: Einen Verwaltungsteil aus 0.2.0 oder 0.3.0 lädt der Client 0.4.0 nicht und
  meldet `Verwaltungsteil passt nicht zu dieser Fassung - das Admin-Setup dieser Fassung ausführen.`

Liegen dir schon die Setups 0.4.1 vor, nimm sie statt 0.4.0: `<zeit>_JG-TacRelay-Admin-Setup-0.4.1.exe` für dich, das
normale Setup 0.4.1 für die Mitglieder, ebenfalls mit **Aktualisieren** (auch über einer 0.4.0). 0.4.1 ändert nur den
Client: Die Hörprobe nimmt 5 s auf und spielt sie dann ab, mithören beim Senden geht nur mit Headset, und vor **Verwaltung
einrichten** steht eine Vorprüfung (Abschnitt 14, Schritt 12). Server- und Protokollcode sind dieselben wie in 0.4.0; der
Server bleibt deshalb auf 0.4.0, ein Serverpaket 0.4.1 gibt es nicht. Die Seite nennt dann
`Dieses Setup bringt die neuere Fassung 0.4.1.`

Ist deine Verwaltung noch nicht eingerichtet, weiter mit Abschnitt 14, Schritt 12, ab Punkt 2, mit dem, was Abschnitt 15,
Schritt 10 dazu sagt.

**Das siehst du:** oben `Verbunden`, unter **Einstellungen** in der Karte **Erweitert: Details der Verbindung** die Stufe `Admin`, links **Raid** (Strg+5) und nach
der Anmeldung **Verwaltung** (Strg+9); beide Tasten stehen jetzt auch unten in der Fußzeile. Die Karte **Serverprofil**
zeigt weiter `Signatur stimmt (Profilschlüssel ...), Version 1`, ohne die Zeile `Vom Server übernommen`: Server und Setup
haben dieselbe Version 1, und übernommen wird nur ein neueres Profil.

Die Mitglieder bekommen das normale Setup 0.4.0 (`<zeit>_JG-TacRelay-Setup-0.4.0.exe`), ebenfalls mit **Aktualisieren**.

**Schritt 11, vor einem Umzug Pflicht: Stick auf 0.4.0.** Für einen Umzug (`UMZUG.md`, Vorweg und Schritt 3) und wenn
dein Profil abläuft (`gültig bis` auf der Profilkarte), brauchst du in `funk-offline` die Programme 0.4.0 und
`Profil erneuern.cmd`. Ein älteres `funk-admin.dll` kennt `profil-erneuern` nicht; nur die cmd-Datei dazulegen reicht
deshalb nicht. Kopier den ganzen Inhalt des Ordners `linux` aus dem neuen Paket in `funk-offline` und lass vorhandene
Dateien ersetzen. `profil.key`, `reserve.key`, `reserve.crt` und `profil.json` dort bleiben, denn das Paket hat keine
Dateien mit diesen Namen.

**Das siehst du:** In `funk-offline` liegen `Profil erneuern.cmd` und `funk-admin.dll` mit Änderungsdatum von heute, daneben
weiter deine Schlüssel und `profil.json`.

**Zurück auf die Fassung von vorher,** nur wenn der neue Server nicht startet und du heute funken willst. Im Container:

```
systemctl stop funk-echo funk-server
mv /opt/funk /root/funk-0.4.0
cp -a /root/funk-vor-0.4.0 /opt/funk
cp /root/server.json.vor-0.4.0 /etc/funk/server.json
cp /root/funk-server.service.vor-0.4.0 /etc/systemd/system/funk-server.service
cp /root/funk-echo.service.vor-0.4.0 /etc/systemd/system/funk-echo.service
systemctl daemon-reload
systemctl start funk-server funk-echo
```

Den Zustand musst du dafür nicht zurückspielen. Geprüft auf dem Entwicklungsrechner: Der Server 0.4.0 lief auf dem Zustand
von 0.2.0 und bekam einen Auftrag; danach liefen der Server 0.3.0 und, in einem zweiten Lauf, der Server 0.2.0 auf genau
diesen Dateien, mit allen Konten, und die Echo-Probe ihrer Fassung meldete `BESTANDEN`. Was 0.2.0 aus 0.3.0 nicht kennt,
steht am Ende von Abschnitt 15. `/etc/funk/echo.env` und `/etc/funk/profilschluessel.txt` dürfen liegen bleiben, die
alten Programme lesen sie nicht. Im selben Lauf bestand auch die Echo-Probe 0.4.0 gegen die Server 0.3.0 und 0.2.0; der
Client 0.4.0 baut auf demselben Kern, gemessen ist das aber nur mit der Probe.

## 17. Auf 0.5.0 (Einsatzphasen und Heute als Funker)

Für deinen laufenden Server mit 0.4.0 (Debian 13, Container 180; die Beispiele weiter oben nehmen 150, hier steht deine
180). Läuft bei dir noch 0.2.0 oder 0.3.0, geh erst nach Abschnitt 16, mit dem Serverpaket 0.4.0, und danach hierher.
Neu in 0.5.0:

- **Einsatzphasen:** Die Einsatzleitung schaltet während eines Raids die Netze der ganzen Flotte um, etwa von der
  Grundstellung auf "Phase 2", per Taste, Joystickknopf oder Klick am Brett. Angelegt werden die Phasen im Verwaltungsteil
  unter Flotte, Phasen. Der Server hält die Phasen und die laufende Phase im Zustand; zwölf Stunden nach dem letzten
  Schalten fällt die Flotte auf die Grundstellung zurück.
- **Heute als Funker:** Ein Admin spielt einen Abend (bis 04:00) als gewöhnlicher Funker, damit die Einsatzleitung ihn in
  Netze und Plätze einteilen kann. Bis dahin ruhen seine Rechte der Einsatzleitung (Führung und Einteilen), dazu Global,
  Durchsagen und das Brett; die Verwaltung bleibt und gilt wie sonst nur in der Admin-Sitzung. Zurück geht es jederzeit
  mit "Wieder Admin", ohne Code.
- **Sprachaktivierung ist der Standard:** `funk-admin netz-setzen` legt ein Netz ohne Schalter auf Sprachaktivierung an
  (`--va` wirkt genauso); mit `--taste` steht es ab Werk auf der Sprechtaste, mit `--nur-taste` gibt es dort nur die
  Taste. Global (Netz 1) bleibt immer auf der Taste.
- Im Client: das Funkfeld lässt sich per Taste oder Joystickknopf in den Infobereich der Taskleiste ablegen und
  zurückholen, eine Melde-Taste spricht gehalten ins Meldenetz, und der Klick beim Sprechen ist einstellbar (Leiste
  Sprachaktivierung, Strg+T). Beispiele in Programm und Anleitungen nennen jetzt Max Mustermann, und die Ausgaben von
  `funk-admin` sagen "der Eigentümer" statt eines Namens.

Du brauchst: das Serverpaket 0.5.0 (Ordner `linux`), das Admin-Setup 0.5.0 für deinen PC und eine Shell im Container
(`pct enter 180`). Kein neuer Schlüssel, kein neues Feld in `server.json`, und die beiden Units bleiben, wie sie sind: Von
0.4.0 auf 0.5.0 kommt in `funk-server.service` und `funk-echo.service` nur eine Kommentarzeile dazu (am Quelltext
verglichen). Von Schritt 3 bis Schritt 7 ist der Funk aus, ein paar Minuten.

**Erst der Server, dann der Client.**

- Geprüft auf dem Entwicklungsrechner, ohne TLS: Der Server 0.5.0 lief auf einem Zustand, den die Programme 0.4.0
  geschrieben hatten, mit unveränderter `server.json`, auch mit einer laufenden Phase 2. `funk-echo` 0.4.0 blieb dabei
  verbunden, auch beim Rückfall auf Phase 1, und die Echo-Probe 0.4.0 und 0.5.0 meldete jeweils `BESTANDEN`. Deine Clients
  0.4.0 und 0.4.1 funken also weiter, bis sie das neue Setup haben; Phasen und "Heute als Funker" sehen sie nicht.
- Ein Client 0.5.0 an einem Server 0.4.0 blendet Phasen und "Heute als Funker" aus und schickt die neuen Nachrichten
  nicht. Das Admin-Setup 0.5.0 deshalb erst nach dem Server: Vorher blendet der Verwaltungsteil den Reiter Phasen aus.
- Am Heimserver ist 0.5.0 noch nicht gelaufen. Was dort anders aussieht als hier, schick Claude; die Ausgaben enthalten
  keine Geheimnisse.
- `mindestClientVersion` in `server.json` bleibt, wie sie ist.

**Schritt 1: Paket in den Container.** Wie in Abschnitt 16: auf deinem PC im Ordner des neuen Pakets (PowerShell), dann
auf dem Host. Nimm den Ordner `linux` aus dem neuen Paket, nie `funk-offline` vom Stick.

```
tar -czf funk-linux.tgz -C linux .
scp funk-linux.tgz root@<proxmox>:/root/
```

```
pct push 180 /root/funk-linux.tgz /root/funk-linux.tgz
```

**Das siehst du:** Im Container zeigt `ls -l /root/funk-linux.tgz` die Datei mit der Uhrzeit von eben.

**Schritt 2: Fassung ansehen und sichern.** Im Container, solange der alte Server noch läuft:

```
grep -m1 -o 'funk-server/[0-9.]*' /opt/funk/funk-server.deps.json
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
cp -a /opt/funk /root/funk-vor-0.5.0
cp /etc/funk/server.json /root/server.json.vor-0.5.0
cp /etc/systemd/system/funk-server.service /root/funk-server.service.vor-0.5.0
cp /etc/systemd/system/funk-echo.service /root/funk-echo.service.vor-0.5.0
```

**Das siehst du:** Die erste Zeile zeigt `funk-server/0.4.0`. Steht dort `0.2.0` oder `0.3.0`, bist du hier zu früh:
erst Abschnitt 16. Dann `Sicherung nach /var/lib/funk/sicherungen/<zeit>`, und `ls /root` zeigt `funk-vor-0.5.0`,
`server.json.vor-0.5.0`, `funk-server.service.vor-0.5.0` und `funk-echo.service.vor-0.5.0`. Gibt es `/root/funk-vor-0.5.0`
schon (ein zweiter Anlauf), lass die Zeile mit `cp -a` weg, sonst landet die Kopie darin statt daneben.

**Schritt 3: Dienste stoppen.**

```
systemctl stop funk-echo funk-server
systemctl is-active funk-server funk-echo
```

**Das siehst du:** zweimal `inactive`. Dein Client meldet `Der Server wird gewartet. ...` und versucht es danach von selbst
weiter.

**Schritt 4: Dateien tauschen.**

```
tar -xzf /root/funk-linux.tgz -C /opt/funk
chown -R root:root /opt/funk
chmod -R go-w /opt/funk
grep -m1 -o 'funk-server/[0-9.]*' /opt/funk/funk-server.deps.json
dotnet /opt/funk/funk-admin.dll hilfe | grep -e '--taste'
```

`chmod` gehört dazu wie in Abschnitt 13. Deine `/etc/funk/server.json` bleibt, wie sie ist; die neue
`konfig/server.beispiel.json` kopierst du **nicht** darüber.

**Das siehst du:** `funk-server/0.5.0`. Steht dort noch die alte Fassung, liegt das alte Programm dort; dann Schritt 1
wiederholen. Die Hilfe von `funk-admin` zeigt bei `netz-setzen` jetzt `[--taste]` und das Beispiel für Command.

**Schritt 5: Units, nichts zu tun.** Beide Units bleiben liegen, und `systemctl daemon-reload` brauchst du nicht. Du
kannst es nachsehen: Ist deine Unit noch die unveränderte Kopie aus dem Paket 0.4.0, zeigt der Vergleich nur die neue
Kommentarzeile.

```
diff /opt/funk/konfig/funk-server.service /etc/systemd/system/funk-server.service
```

**Das siehst du:** `1d0` und darunter `< # JG TacRelay - Maintainer: Sarevoc`, sonst nichts. Zeigt `diff` mehr, hast du
die Unit selbst angepasst oder sie stammt aus einer älteren Fassung; lass sie, wie sie ist, sie läuft. Nur wenn sie
aus 0.2.0 stammt, fehlen ihr `LogsDirectory=funk` und `LogsDirectoryMode=0750` (Abschnitt 15, Schritt 5).

**Schritt 6: prüfen.**

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
cp /opt/funk/konfig/profilschluessel.txt /etc/funk/
dotnet /opt/funk/funk-admin.dll pruefen --vollstaendig --konfig /etc/funk/server.json
```

**Das siehst du:** Wie in Abschnitt 16, Schritt 7: bei `funk-server ... pruefen` zuerst eine Zeile zur Uhr und am Ende
`Konfiguration in Ordnung.`; bei `funk-admin pruefen --vollstaendig` je Befund eine Zeile mit `OK`, `HINWEIS`, `WARNUNG`
oder `FEHLER` und am Ende `Ergebnis: 0 Fehler, ...`. Die Datei `profilschluessel.txt` ist dieselbe wie in 0.4.0 und nicht
geheim. `pruefen` ändert nichts und startet nichts. Steht dort ein `FEHLER`, geh trotzdem weiter zu Schritt 7: Der Server
lief bis eben mit denselben Dateien, und das Journal zeigt beim Start, ob etwas nicht stimmt. Die `FEHLER`-Zeile
schickst du Claude.

**Schritt 7: starten und Journal lesen.**

```
systemctl start funk-server funk-echo
journalctl -u funk-server -n 30 --no-pager
journalctl -u funk-echo -n 10 --no-pager
```

**Das siehst du:**

- `INFO Serverprofil Version <N> mit <M> Pins geladen` und `INFO Funk-Server 0.5.0 bereit: 1 Adresse(n), TCP Bindport
  8443 (öffentlich 8443), UDP Bindport 47443 (öffentlich 47443), TLS 1.3/1.2, Pin XXXX-XXXX-XXXX-XXXX`, mit derselben
  Anzeigeform des Pins wie vorher.
- Keine Zeile `WARNUNG Der Pin des Serverschlüssels ...` und keine `WARNUNG Die Uhr des Servers ...`.
- Kurz danach `Sitzung beginnt` und `Sitzung aktiv` für deinen Client und den Echo-Funker. Dein Client 0.4.x zeigt wieder
  `Verbunden`.
- Hast du noch keinen Eigentümer gesetzt, etwa weil dein Antrag ins falsche Konto ging, steht dort auch `WARNUNG Kein
  Eigentümer gesetzt: funk-admin eigentuemer --konto <id> (3.10). ...`. Das richtet Abschnitt 16, Schritt 9 (Weg A).
- Das Journal von `funk-echo` zeigt `Bereit im Netz 'Echo' (ID 90)`.

Wenn nicht: `Start gescheitert: ...` oder `Konfiguration ...` nennen den Grund; danach `systemctl restart funk-server`.
Hat systemd nach mehreren Fehlstarts aufgegeben (`start request repeated too quickly`), vorher `systemctl reset-failed
funk-server`. Startet er gar nicht, geht es zurück (am Ende dieses Abschnitts).

**Schritt 8: Client 0.5.0 auf deinem PC.** JG TacRelay schließen, dann das Admin-Setup ausführen
(`<zeit>_JG-TacRelay-Admin-Setup-0.5.0.exe`).

- Die Seite **JG TacRelay ist schon installiert** nennt `Gefunden: JG TacRelay, Fassung 0.4.0, ...` (oder `0.4.1`) und
  `Dieses Setup bringt die neuere Fassung 0.5.0.` Wähle **Aktualisieren**.
- Einstellungen, Namen und Geräteschlüssel bleiben, dein Gerät bleibt freigegeben. Die Einstellungsdatei bekommt neue
  Einträge (Tasten, Klick); ein Client 0.4.0 oder 0.4.1 liest sie weiter.
- Nimm das Admin-Setup, nicht das normale: Den Verwaltungsteil aus 0.4.x lädt der Client 0.5.0 nicht und meldet
  `Verwaltungsteil passt nicht zu dieser Fassung - das Admin-Setup dieser Fassung ausführen.`

Die Mitglieder bekommen das normale Setup 0.5.0 (`<zeit>_JG-TacRelay-Setup-0.5.0.exe`), ebenfalls mit **Aktualisieren**.

Liegen dir schon die Setups 0.5.1 vor, nimm sie statt 0.5.0: `<zeit>_JG-TacRelay-Admin-Setup-0.5.1.exe` für dich, das
normale Setup 0.5.1 für die Mitglieder, ebenfalls mit **Aktualisieren** (auch über einer 0.5.0). 0.5.1 ändert nur den
Client: Die Vorprüfung vor **Verwaltung einrichten** zeigt nur noch TPM 2.0 und die Richtlinie zum Windows-Konto
(Abschnitt 14, Schritt 12). Server- und Protokollcode sind dieselben wie in 0.5.0; der Server bleibt deshalb auf 0.5.0,
ein Serverpaket 0.5.1 gibt es nicht. Die Seite nennt dann `Dieses Setup bringt die neuere Fassung 0.5.1.`

**Das siehst du:** oben `Verbunden`; unter **Einstellungen** in **Ich** die Karte **Heute als Funker** mit dem Knopf
**Als Funker** (nur für Admins), im Verwaltungsteil unter **Flotte** den Reiter **Phasen**, im Bereich **Raid** am Brett die Phasenleiste, in
der Leiste Sprachaktivierung (Strg+T) die Einstellungen für den Klick und nach dem ersten Ablegen des Funkfelds ein
Symbol im Infobereich. Das Symbol, die Meldezeile und das Ablegen und Holen per Joystick sind nur im Code und im
Bildlauf geprüft, nicht am echten PC; was dort anders aussieht, schick Claude.

**Bestehende Netze bleiben, wie sie sind.** Ein Netz, das du vor 0.5.0 an der Konsole ohne `--va` angelegt hast, steht
weiter auf der Sprechtaste, bei jedem Client, der es nicht selbst umgestellt hat; auch Command bleibt, wie es ist. Wer
Sprachaktivierung will, hat zwei Wege:

- Jeder stellt es selbst ein: im Bereich **Netze** beim Netz den Schalter **Sprachaktivierung** (gilt nur für diesen
  Client und wird je Netz gemerkt, am Code gelesen).
- Du legst das Netz neu an: `funk-admin netz-setzen` mit der ID des Netzes ersetzt es ganz, mit den Werten aus dem
  Befehl. Nenne deshalb Name, `--offen`, `--standard` und `--reihenfolge` so, wie das Netz sie heute hat (`netzliste`
  zeigt Name, offen und standard, nicht die Reihenfolge), und lass `--taste` weg. Das ändert den Standard des Netzes für
  alle, die nicht selbst gewählt haben. Command lässt du aus: Es bleibt auf der Taste.

```
dotnet /opt/funk/funk-admin.dll netzliste --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll netz-setzen --id <id> --name <name> --offen <offen> --konfig /etc/funk/server.json
```

Setz `<id>`, `<name>` und `<offen>` (`NEIN`, `HOEREN` oder `HOEREN_SENDEN`) aus der Ausgabe von `netzliste` ein, dazu
`--standard` und `--reihenfolge <n>`, wenn das Netz sie hat. Global (Netz 1) bleibt immer auf der Taste.

**Zurück auf die Fassung von vorher,** nur wenn der neue Server nicht startet und du heute funken willst. Im Container:

```
systemctl stop funk-echo funk-server
mv /opt/funk /root/funk-0.5.0
cp -a /root/funk-vor-0.5.0 /opt/funk
cp /root/server.json.vor-0.5.0 /etc/funk/server.json
cp /root/funk-server.service.vor-0.5.0 /etc/systemd/system/funk-server.service
cp /root/funk-echo.service.vor-0.5.0 /etc/systemd/system/funk-echo.service
systemctl daemon-reload
systemctl start funk-server funk-echo
```

Den Zustand musst du dafür nicht zurückspielen. Geprüft auf dem Entwicklungsrechner: Der Server 0.4.0 startete auf dem
Zustand, den der Server 0.5.0 geschrieben hatte, und die Echo-Probe 0.4.0 meldete `BESTANDEN`. Nach dem ersten Schreiben
fehlen in den Dateien die Phasen, die laufende Phase und der Funker-Modus (`alsFunkerBis`); alles andere, Konten, Netze,
Rollen und Einheiten, bleibt. Phasen legst du nach einem späteren Update im Verwaltungsteil neu an; die Sicherungen der Flotte dort
tragen die Phasen mit. Ein Client 0.5.0 blendet an einem Server 0.4.0 Phasen und "Heute als Funker" aus.

## 18. Auf 0.6.0 (Kanäle mit Ch.)

Für deinen laufenden Server mit 0.5.0 (Container 180). Läuft bei dir noch 0.4.0, geh erst nach Abschnitt 17, mit dem
Serverpaket 0.5.0, und danach hierher. Neu in 0.6.0:

- **Kanäle mit Ch.:** Was bisher Netz hieß, heißt im Client jetzt Kanal. Ein Kanal kann Ch. haben, etwa das Kanalnetz
  Brücke der alten Vorlage mit Ruhe und Schiff 1 bis Schiff 20. Oben links im Funkfeld wählt jeder eine Rolle (die 21
  Rollen der alten Kanalvorlage), je Feld den Ch.; ab Werk steht jeder Ch.-Wähler auf Ruhe. Die alte Kanalvorlage legt
  Global mit Ch., 30 Kanäle mit ihren Ch.-Listen und die 21 Rollen neben deine bisherige Flotte; die alte Flotte und Echo
  bleiben.
- **Sperren:** Die Einsatzleitung sperrt einen Kanal für eine Person oder für alle bis 04:00: weder hören noch senden noch
  hineinlegen. Am Brett für eine Person im Detailfeld mit **sperren** und **freigeben**, für alle unter der Kanalliste mit
  **Kanal für alle sperren**, an der Konsole mit `kanalsperre-setzen`, `kanalsperre-aufheben` und `kanalsperren`.
- **Rollenliste schon mit der alten Flotte:** Sobald jemand das Setup 0.6.0 hat (Schritt 8), wählt er oben links selbst
  eine Rolle, auch eine der alten Flotte. Wer die Kapitänsrolle der alten Startvorlage wählt, bekommt Command und die
  Ausweichnetze zum Hören und Senden, ohne Platz auf einem Schiff und auch dann, wenn am Brett **Wechsel sperren** an ist.
  Das ist so gewollt: Gesperrt ist nur, was die Einsatzleitung sperrt. Soll das vor der Vorlage nicht gehen, nimm im
  Verwaltungsteil unter **Flotte** bei dieser Rolle den Haken **Selbst wählbar** heraus; dann steigt allerdings auch
  niemand mehr selbst als Kapitän ein. Nach der Vorlage (Schritt 9) steht diese Kapitänsrolle zweimal in der Liste, die
  alte Rolle und die aus der alten Kanalvorlage; nimm spätestens dann bei der alten den Haken heraus oder lösch die alte
  Flotte, sonst landet mancher in den alten Kanälen.
- Im Client: die **Startseite** statt des Bereichs Verbindung, **Glas** an Knöpfen und Schaltern, der Bereich **Meine
  Kanäle**, unter Audio die **automatische Lautstärke mit Halteregel** (sie gleicht die Grundlautstärke nur in Sprechpausen
  und in kleinen Schritten an, mitten im Satz wird nichts lauter oder leiser, also kein Pumpen) und der **Funk-Klick als
  kurzes Rauschen** wie am Funkgerät.
- An der Konsole: `netzliste` nennt bei einem Kanal mit Ch. am Ende der Zeile `Ch. 0 bis 20`, `status` zählt Kanäle, und
  `funk-echo` meldet `Bereit im Kanal 'Echo' (ID 90)`.

Du brauchst: das Serverpaket 0.6.0 (Ordner `linux`), das Admin-Setup 0.6.0 für deinen PC, das normale Setup 0.6.0 für die
Mitglieder und eine Shell im Container (`pct enter 180`). Kein neuer Schlüssel, kein neues Feld in `server.json`, und die
beiden Units bleiben, wie sie sind: Gegenüber 0.5.0 haben sie sich nicht geändert (am Inhalt verglichen). Von Schritt 3 bis
Schritt 7 ist der Funk aus, ein paar Minuten.

**Die Reihenfolge ist wichtig: erst der Server (Schritte 1 bis 7), dann die Setups für alle (Schritt 8), erst danach die
Vorlage (Schritt 9).**

- Solange es keinen Kanal mit Ch. gibt, funken Clients 0.5.x mit dem Server 0.6.0 weiter. Geprüft auf dem
  Entwicklungsrechner, ohne TLS, mit den Programmen des Gesamtpakets 0.5.0: Der Server 0.6.0 startete auf dem Zustand, den
  0.5.0 geschrieben hatte (Echo, Command und die alte Startvorlage mit 15 Schiffen), `funk-echo` 0.5.0 war eine halbe
  Sekunde nach dem Neustart wieder verbunden, und die Echo-Probe 0.5.0 und 0.6.0 meldete jeweils `BESTANDEN`.
- Sobald es den ersten Kanal mit Ch. gibt (die Vorlage aus Schritt 9 oder `netz-setzen --kanaele`), lässt der Server nur noch
  Clients ab 0.6.0 hinein (Versionstor). Ein Client 0.5.x wird sofort getrennt, meldet `Diese Version von JG TacRelay ist
  zu alt für den Server. Bitte aktualisieren.` und versucht es nicht von selbst wieder; mit dem Setup 0.6.0 kommt er wieder
  hinein. Geprüft wie oben mit `funk-echo` 0.5.0 als Client 0.5.x: getrennt mit `VERSION_ZU_ALT`, in den 30 Sekunden danach
  kein neuer Versuch, neu gestartet sofort wieder abgewiesen; `funk-echo` 0.6.0 und die Echo-Probe 0.6.0 bestanden.
- Ein Client 0.6.0 an einem Server 0.5.0 sperrt die Rollenliste ("Der Server kennt das noch nicht") und zeigt keine
  Ch.-Wähler und keine Sperren. Deshalb der Server zuerst.
- Die Vorab-Setups 0.6.0 vom 02.10.2026 kennen noch keine Kanäle mit Ch., lassen sich vom Versionstor aber nicht aufhalten
  (sie tragen schon die Fassung 0.6.0). Wer eines davon hat, installiert das Setup aus diesem Paket darüber: Die Seite
  **JG TacRelay ist schon installiert** nennt dann `Dieses Setup bringt dieselbe Fassung 0.6.0.`; wähle **Aktualisieren**.
- Am Heimserver ist 0.6.0 noch nicht gelaufen. Was dort anders aussieht als hier, schick Claude; die Ausgaben enthalten
  keine Geheimnisse.
- `mindestClientVersion` in `server.json` bleibt, wie sie ist; das Versionstor kommt mit dem ersten Kanal mit Ch. von selbst.

**Schritt 1: Paket in den Container.** Wie in Abschnitt 17: auf deinem PC im Ordner des neuen Pakets (PowerShell), dann
auf dem Host. Nimm den Ordner `linux` aus dem neuen Paket, nie `funk-offline` vom Stick.

```
tar -czf funk-linux.tgz -C linux .
scp funk-linux.tgz root@<proxmox>:/root/
```

```
pct push 180 /root/funk-linux.tgz /root/funk-linux.tgz
```

**Das siehst du:** Im Container zeigt `ls -l /root/funk-linux.tgz` die Datei mit der Uhrzeit von eben.

**Schritt 2: Fassung ansehen und sichern.** Im Container, solange der alte Server noch läuft:

```
grep -m1 -o 'funk-server/[0-9.]*' /opt/funk/funk-server.deps.json
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
cp -a /opt/funk /root/funk-vor-0.6.0
cp /etc/funk/server.json /root/server.json.vor-0.6.0
```

**Das siehst du:** Die erste Zeile zeigt `funk-server/0.5.0`. Steht dort `0.4.0` oder älter, bist du hier zu früh: erst
Abschnitt 17. Dann `Sicherung nach /var/lib/funk/sicherungen/<zeit>`, und `ls /root` zeigt `funk-vor-0.6.0` und
`server.json.vor-0.6.0`. Gibt es `/root/funk-vor-0.6.0` schon (ein zweiter Anlauf), lass die Zeile mit `cp -a` weg, sonst
landet die Kopie darin statt daneben.

**Schritt 3: Dienste stoppen.**

```
systemctl stop funk-echo funk-server
systemctl is-active funk-server funk-echo
```

**Das siehst du:** zweimal `inactive`. Dein Client meldet `Der Server wird gewartet. ...` und versucht es danach von selbst
weiter.

**Schritt 4: Dateien tauschen.**

```
tar -xzf /root/funk-linux.tgz -C /opt/funk
chown -R root:root /opt/funk
chmod -R go-w /opt/funk
grep -m1 -o 'funk-server/[0-9.]*' /opt/funk/funk-server.deps.json
dotnet /opt/funk/funk-admin.dll hilfe | grep -e 'flotte-vorlage' -e 'kanalsperre-setzen'
```

Deine `/etc/funk/server.json` bleibt, wie sie ist; die neue `konfig/server.beispiel.json` kopierst du **nicht** darüber.

**Das siehst du:** `funk-server/0.6.0`. Die Hilfe von `funk-admin` zeigt die Zeilen mit `flotte-vorlage --name <alte Vorlage>`
und `kanalsperre-setzen`. Steht dort noch die alte Fassung, liegt das alte Programm dort; dann Schritt 1 wiederholen.

**Schritt 5: Units, nichts zu tun.** Beide Units bleiben liegen, und `systemctl daemon-reload` brauchst du nicht. Du kannst
es nachsehen:

```
diff /opt/funk/konfig/funk-server.service /etc/systemd/system/funk-server.service
diff /opt/funk/konfig/funk-echo.service /etc/systemd/system/funk-echo.service
```

**Das siehst du:** keine Ausgabe, wenn deine Units die unveränderten Kopien aus dem Paket 0.5.0 sind. Stammen sie aus 0.4.0,
zeigt `diff` je `1d0` und darunter `< # JG TacRelay - Maintainer: Sarevoc`, sonst nichts. Zeigt `diff` mehr, hast du sie
selbst angepasst; lass sie, wie sie sind, sie laufen.

**Schritt 6: prüfen.**

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
cp /opt/funk/konfig/profilschluessel.txt /etc/funk/
dotnet /opt/funk/funk-admin.dll pruefen --vollstaendig --konfig /etc/funk/server.json
```

**Das siehst du:** Wie in Abschnitt 17, Schritt 6: bei `funk-server ... pruefen` zuerst eine Zeile zur Uhr und am Ende
`Konfiguration in Ordnung.`; bei `funk-admin pruefen --vollstaendig` je Befund eine Zeile mit `OK`, `HINWEIS`, `WARNUNG`
oder `FEHLER` und am Ende `Ergebnis: 0 Fehler, ...`. `pruefen` ändert nichts und startet nichts. Steht dort ein `FEHLER`,
geh trotzdem weiter zu Schritt 7: Der Server lief bis eben mit denselben Dateien, und das Journal zeigt beim Start, ob
etwas nicht stimmt. Die `FEHLER`-Zeile schickst du Claude.

**Schritt 7: starten und Journal lesen.**

```
systemctl start funk-server funk-echo
journalctl -u funk-server -n 30 --no-pager
journalctl -u funk-echo -n 10 --no-pager
```

**Das siehst du:**

- `INFO Serverprofil Version <N> mit <M> Pins geladen` und `INFO Funk-Server 0.6.0 bereit: 1 Adresse(n), TCP Bindport
  8443 (öffentlich 8443), UDP Bindport 47443 (öffentlich 47443), TLS 1.3/1.2, Pin XXXX-XXXX-XXXX-XXXX`, mit derselben
  Anzeigeform des Pins wie vorher.
- Keine Zeile `WARNUNG Der Pin des Serverschlüssels ...` und keine `WARNUNG Die Uhr des Servers ...`.
- Kurz danach `Sitzung beginnt` und `Sitzung aktiv` für deinen Client und den Echo-Funker. Dein Client 0.5.x zeigt wieder
  `Verbunden`.
- Das Journal von `funk-echo` zeigt `Bereit im Kanal 'Echo' (ID 90)`; bis 0.5.x stand dort `Netz`.

Wenn nicht: `Start gescheitert: ...` oder `Konfiguration ...` nennen den Grund; danach `systemctl restart funk-server`.
Hat systemd nach mehreren Fehlstarts aufgegeben (`start request repeated too quickly`), vorher `systemctl reset-failed
funk-server`. Startet er gar nicht, geht es zurück (Rückweg A am Ende dieses Abschnitts).

**Schritt 8: Setups 0.6.0 an alle.** Auf deinem PC JG TacRelay schließen, dann das Admin-Setup ausführen
(`<zeit>_JG-TacRelay-Admin-Setup-0.6.0.exe`).

- Die Seite **JG TacRelay ist schon installiert** nennt `Gefunden: JG TacRelay, Fassung 0.5.0, ...` (oder `0.5.1`, oder
  `0.6.0` bei einem Vorab-Setup) und `Dieses Setup bringt die neuere Fassung 0.6.0.` (beim Vorab-Setup `dieselbe Fassung`).
  Wähle **Aktualisieren**.
- Einstellungen, Namen und Geräteschlüssel bleiben, dein Gerät bleibt freigegeben. Die Einstellungsdatei bekommt neue
  Einträge (Skalierung des Funkfelds, Rolle, Ch. je Kanal, automatische Lautstärke); ein Client 0.5.x liest sie weiter.
  Geprüft ohne Fenster: 0.5.0, 0.5.1 und die Vorab-Stände lasen die Datei von 0.6.0 ohne Fehler; schreibt ein älterer
  Client sie zurück, fallen nur seine unbekannten Einträge weg.
- Nimm für dich das Admin-Setup, nicht das normale: Den Verwaltungsteil aus 0.5.x lädt der Client 0.6.0 nicht.

Die Mitglieder bekommen das normale Setup 0.6.0 (`<zeit>_JG-TacRelay-Setup-0.6.0.exe`), ebenfalls mit **Aktualisieren**.
Frag nach, bis alle es haben, die am nächsten Abend funken wollen: Wer mit 0.5.x übrig bleibt, kommt nach Schritt 9 erst
mit dem Setup wieder in den Funk. Am Server siehst du die Fassung der Clients nicht.

**Das siehst du:** oben `Verbunden`; die **Startseite** mit deinem Namen, Verbinden oder Trennen und den Steckern für
Server und Sprache; links **Meine Kanäle**; unter **Audio** in der Karte Verarbeitung **Automatische Lautstärke**
(eingeschaltet) und die Karte **Funk-Klick**; im Verwaltungsteil unter **Flotte** den Reiter **Kanäle** und den Knopf
der alten Kanalvorlage. Die neuen Ansichten sind nur im Code und im Bildlauf geprüft, nicht am echten PC; was dort anders
aussieht, schick Claude.

**Schritt 9: Vorlage übernehmen,** erst wenn alle das Setup 0.6.0 haben. Vorher eine Sicherung für den
Rückweg B, im Container:

```
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll flotte-vorlage --name <alte Vorlage> --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll netzliste --konfig /etc/funk/server.json | grep -c ' Ch\. 0 bis '
```

Merk dir die Zeit aus `Sicherung nach /var/lib/funk/sicherungen/<zeit>`. Statt der zweiten Zeile geht es auch im
Verwaltungsteil (Admin-Sitzung): **Flotte**, der Knopf der alten Kanalvorlage, **Übernehmen**; die Rückfrage nennt das Versionstor.
Die alte Kanalvorlage gibt es seit dem Umbau zu JG TacRelay (19.10.2026) nicht mehr; heute gilt
`flotte-vorlage --name starcitizen` (Vorlage Star Citizen: 24 Schritte, 10 Kanäle mit Ch., 14 Rollen; Global ändert sie nicht).

**Das siehst du:** `<alte Vorlage> übernommen: 52 Schritte. Ab jetzt kommen nur noch Clients ab 0.6.0 hinein (Versionstor);
...` und darunter die Annahmen, die der Admin prüft. Die letzte Zeile zählt `31`
(Global und die 30 Kanäle). Wer noch 0.5.x hat, sieht `Diese Version von JG TacRelay ist zu alt für den Server. Bitte
aktualisieren.` Die alte Flotte bleibt daneben; löschen kannst du sie später im Verwaltungsteil, wenn alle mit den neuen
Kanälen funken.

**Rückweg A, vor der Vorlage** (Schritt 9 nicht gemacht), nur wenn der neue Server nicht startet oder du heute mit dem
alten funken willst. Im Container:

```
systemctl stop funk-echo funk-server
mv /opt/funk /root/funk-0.6.0
cp -a /root/funk-vor-0.6.0 /opt/funk
cp /root/server.json.vor-0.6.0 /etc/funk/server.json
systemctl start funk-server funk-echo
```

Den Zustand musst du dafür nicht zurückspielen. Geprüft auf dem Entwicklungsrechner: Der Server 0.5.0 startete auf dem
Zustand, den der Server 0.6.0 geschrieben hatte, und die Echo-Probe 0.5.0 meldete `BESTANDEN`. Clients 0.6.0 funken mit
dem Server 0.5.0 weiter, ohne Rollenliste, Ch. und Sperren.

**Rückweg B, nach der Vorlage.** Erst müssen alle Kanäle mit Ch. weg, sonst startet ein Server 0.5.0 nicht: Im Journal
steht dann `Unhandled exception. System.InvalidOperationException: Schnappschuss nicht stimmig: Konto <n>: Netz <Kanal-ID>
unbekannt; ...` mit vielen Zeilen `at ...` darunter, und systemd versucht es alle paar Sekunden wieder. Festgehalten ist
die Zeile auf dem Entwicklungsrechner mit dem Probefunk-Server, mit Konten, die einen Kanal mit Ch. im Feld hatten; dort
heißt sie `Start gescheitert: InvalidOperationException: ...`. Der Server im Container fängt diesen Fehler nicht ab (aus
dem Programm abgelesen, dort nicht ausprobiert). Der Server 0.6.0 läuft dafür weiter. Im Container:

```
dotnet /opt/funk/funk-admin.dll netzliste --konfig /etc/funk/server.json | awk '$1 == "Kanal" && $2 != 1 && / Ch\. 0 bis / {print $2}' > /root/kanaele-mit-ch.txt
wc -l < /root/kanaele-mit-ch.txt
for id in $(cat /root/kanaele-mit-ch.txt); do dotnet /opt/funk/funk-admin.dll netz-loeschen --id $id --konfig /etc/funk/server.json; done
dotnet /opt/funk/funk-admin.dll netz-setzen --id 1 --name Global --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll netzliste --konfig /etc/funk/server.json | grep -c ' Ch\. '
```

**Das siehst du:** `wc` zählt `30` (die Kanäle der Vorlage, dazu jeder Kanal mit Ch., den du selbst angelegt hast), jede
Löschung `OK, zustandsVersion <n>`, und die letzte Zeile zählt `0`. Damit fällt das Versionstor, Clients 0.5.x kommen
wieder hinein, und Konten, Geräte, Echo, die alte Flotte und deine anderen Kanäle bleiben. Die Rollen der Vorlage bleiben
ohne Felder stehen. Danach kannst du wie im Rückweg A auf die Programme 0.5.0 zurück. Geprüft auf dem Entwicklungsrechner:
Nach genau diesen Befehlen kam `funk-echo` 0.5.0 wieder in den Server 0.6.0, der Server 0.5.0 startete auf dem Zustand,
und die Echo-Probe 0.5.0 meldete `BESTANDEN`.

Zweite Linie, wenn das nicht reicht: die Sicherung aus Schritt 9 zurückspielen wie in Abschnitt 12 unter **Zurückspielen**,
mit `<zeit>` aus Schritt 9. Dann ist der Stand von kurz vor der Vorlage wieder da; was danach an Feldern, Plätzen und
Kanälen geändert wurde, ist weg. Aufträge und Kanalsperren kommen in keiner Sicherung mit.
