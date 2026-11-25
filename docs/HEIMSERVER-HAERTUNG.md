# Heimserver härten

Checkliste für deinen Proxmox-Host und die Dienste darauf (Schutz gegen Botnetze, Scanner und Durchprobieren). Hier
wird nichts ausgeführt, jeder Punkt ist ein Schritt für dich, mit dem, was du siehst, wenn es geklappt hat. Zuerst lesen,
dann Punkt für Punkt, am besten an einem ruhigen Abend und nicht kurz vor einem Raid.

Der Funk schützt sich selbst mit der **eingebauten Sperrliste** (`BETRIEB.md`, Abschnitt Sperrliste): Adressen mit
gescheiterten Handshakes, Protokollmüll vor der Anmeldung oder Antrags- und Registrierungsfluten sperrt funk-server für
sich, befristet und steigend, und lässt deine Mitglieder dabei nie draußen. Das ist der Hauptschutz für den Funk; dafür
musst du nichts tun. Diese Liste kümmert sich um den Rest: die FRITZ!Box, Proxmox, SSH, Updates und nginx vor Foundry.

Platzhalter wie im Rest der Anleitung: `<CT-Nummer>` ist die Nummer eines Containers in Proxmox (der Funk-Container ist in
`HEIMSERVER-PROXMOX.md` die 150), `<dein-name>` dein DynDNS-Name, etwa `funk.example.org`.

## 0. Soll-Zustand

Von außen erreichbar sind genau:

| Port | Ziel | Wofür |
| --- | --- | --- |
| TCP 443 | nginx | Foundry (HTTPS) |
| TCP 8443 | Funk-Container | Steuerkanal des Funks (TLS) |
| UDP 47443 | Funk-Container | Sprache |

Sonst nichts: keine Proxmox-Weboberfläche (8006), kein SSH (22), kein Port 80, außer du brauchst ihn für das Zertifikat
von Let's Encrypt (siehe Punkt 6).

## 1. FRITZ!Box: nur nötige Freigaben

1. In der FRITZ!Box **Internet**, **Freigaben**, **Portfreigaben**. Jeder Eintrag, der nicht in der Tabelle oben steht, wird
   gelöscht oder bekommt den Haken **Freigabe aktiv** weg.
2. Bei jedem Gerät mit Freigaben: **Selbstständige Portfreigaben für dieses Gerät erlauben** aus. Sonst kann ein Programm im
   Netz per UPnP selbst Ports öffnen.
3. Unter **Internet**, **Freigaben**, **FRITZ!Box-Dienste**: **Internetzugriff auf die FRITZ!Box über HTTPS** aus, solange du
   ihn nicht von unterwegs brauchst. Er läge sonst neben deinen Diensten.

**Das siehst du, wenn es geklappt hat:** Die Liste der Portfreigaben zeigt nur noch die drei Einträge aus Punkt 0, bei 443
auf nginx, bei 8443 und 47443 auf den Funk-Container.

**Von außen prüfen:** Am Handy WLAN aus, mobile Daten an, im Browser `https://<dein-name>:8006` öffnen. Die Seite lädt
nicht (Zeitüberschreitung). `https://<dein-name>` zeigt Foundry.

## 2. Proxmox-Weboberfläche nicht nach außen

Die Weboberfläche (Port 8006) bleibt im Heimnetz. Nie in der FRITZ!Box freigeben, auch nicht kurz. Von unterwegs geht es
über einen VPN-Zugang ins Heimnetz (etwa den der FRITZ!Box), nicht über eine Portfreigabe.

Zusätzlich für die Anmeldung: In der Weboberfläche **Rechenzentrum** (Datacenter), **Berechtigungen**, **Zwei-Faktor**,
**Hinzufügen**, **TOTP**, mit einer Authenticator-App scannen. Danach fragt die Anmeldung als root nach dem Code. Vorher die
Wiederherstellungsschlüssel (**Recovery Keys**) anlegen und getrennt vom Telefon aufheben, wie die Notfallcodes des Funks.

**Das siehst du:** Die Prüfung aus Punkt 1 (Port 8006 von außen) lädt nicht; im Heimnetz fragt die Anmeldung nach dem Code.

## 3. SSH nur mit Schlüssel

Gilt für den Proxmox-Host und für jeden Container, in dem SSH läuft. Wichtig: Eine Sitzung bleibt offen, bis die neue
Anmeldung in einem zweiten Fenster geklappt hat. Notfalls bleibt die Shell in der Proxmox-Weboberfläche.

1. Auf deinem PC (PowerShell), einmal:

```
ssh-keygen -t ed25519 -C "admin-pc"
```

   Eine Passphrase vergeben. Der öffentliche Teil liegt danach in `C:\Users\<du>\.ssh\id_ed25519.pub`.

2. Den öffentlichen Teil auf den Host bringen. Auf dem Host (Shell der Weboberfläche) die Datei öffnen

```
nano /root/.ssh/authorized_keys
```

   und die eine Zeile aus `id_ed25519.pub` ans Ende anfügen (sie beginnt mit `ssh-ed25519`). Auf dem Proxmox-Host stehen
   dort schon Schlüssel des Hosts selbst; die bleiben.

3. In einem zweiten Fenster auf dem PC prüfen:

```
ssh root@<lan-adresse des hosts>
```

   Es fragt nach der Passphrase des Schlüssels, nicht nach dem Passwort von root. Erst wenn das klappt, weiter.

4. Auf dem Host die Passwortanmeldung abschalten:

```
nano /etc/ssh/sshd_config.d/10-nur-schluessel.conf
```

   mit diesem Inhalt:

```
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
```

   Dann prüfen und neu laden:

```
sshd -t
systemctl reload ssh
```

**Das siehst du:** `sshd -t` gibt nichts aus. Eine neue Anmeldung mit Schlüssel klappt; ohne Schlüssel kommt
`Permission denied (publickey)` statt einer Passwortabfrage. Prüfen lässt sich das mit `ssh -o PubkeyAuthentication=no
root@<lan-adresse des hosts>`.

## 4. Sicherheitsupdates von selbst

**In den Containern** (Funk, nginx, Foundry), je Container:

```
apt install unattended-upgrades
dpkg-reconfigure -plow unattended-upgrades
```

Die Frage beantwortest du mit **Ja**. Die Voreinstellung nimmt nur Sicherheitsupdates aus Debian. Die .NET-Laufzeit im
Funk-Container aktualisierst du weiter von Hand mit Neustart des Dienstes (`HEIMSERVER-PROXMOX.md`, Abschnitt 13): Ein
stilles Update der Laufzeit mitten im Raid wäre schlechter als eines, das du planst.

**Auf dem Proxmox-Host** von Hand, etwa monatlich und nach Sicherheitsmeldungen von Proxmox:

```
apt update
apt list --upgradable
apt full-upgrade
```

Ein neuer Kernel braucht einen Neustart, den du an einem Abend ohne Raid machst. Auf dem Host keine
unattended-upgrades: Proxmox-Pakete gehören zusammen aktualisiert.

**Das siehst du:** `unattended-upgrade --dry-run --debug` im Container nennt am Ende die Pakete, die es nehmen würde, oder
`No packages found that can be upgraded unattended`. Das Protokoll liegt unter `/var/log/unattended-upgrades/`.

## 5. nginx vor Foundry

Deine bestehende Konfiguration bleibt, du ergänzt nur. Wo nginx läuft (auf dem Host oder in einem Container), ist egal;
die Dateien liegen unter `/etc/nginx/`. Vorher eine Kopie:

```
cp -a /etc/nginx /root/nginx-vorher
```

**Keine Verzeichnislisten und keine Versionsnummer.** Verzeichnislisten sind in nginx aus, solange nirgends
`autoindex on` steht:

```
grep -rn autoindex /etc/nginx
```

Findet das eine Zeile mit `on`, auf `off` setzen. Im Block `http { ... }` in `/etc/nginx/nginx.conf`:

```
server_tokens off;
```

**Anfragen begrenzen (limit_req), erst zur Probe.** Foundry lädt beim Betreten einer Welt viele Dateien auf einmal (Karten,
Module), nach Strg+F5 alle noch einmal; eine knappe Grenze für alles träfe deine Spieler. Deshalb zwei Zonen: eine
großzügige für alles, gegen Fluten, und eine enge für die Anmeldung, gegen Durchprobieren von Passwörtern. Und deshalb
läuft die Grenze zuerst nur zur Probe: nginx zählt mit und schreibt ins Protokoll, was es abweisen würde, weist aber nichts
ab. In `nginx.conf`, im Block `http { ... }`:

```
limit_req_zone $binary_remote_addr zone=foundry_alles:10m rate=50r/s;
limit_req_zone $binary_remote_addr zone=foundry_anmeldung:10m rate=10r/m;
limit_req_status 429;
```

In deinem `server { ... }`-Block für Foundry (meist unter `/etc/nginx/sites-available/`), oben im Block, vor der ersten
`location`:

```
limit_req_dry_run on;
```

Das ist der Probebetrieb, er gilt für alle `location` darunter. Ihn gibt es ab nginx 1.17.1; `nginx -v` zeigt deine
Fassung, und `nginx -t` unten meldet einen Fehler, wenn deine ihn nicht kennt.

In derselben Datei, in der vorhandenen `location / { ... }` vor `proxy_pass`:

```
limit_req zone=foundry_alles burst=200 nodelay;
```

und zusätzlich zwei neue Blöcke für die Anmeldeseiten, mit denselben `proxy_`-Zeilen wie in deiner `location /`:

```
location = /join {
    limit_req zone=foundry_anmeldung burst=5 nodelay;
    # hier die proxy_-Zeilen aus location / kopieren
}
location = /auth {
    limit_req zone=foundry_anmeldung burst=5 nodelay;
    # hier die proxy_-Zeilen aus location / kopieren
}
```

Die Verbindung für Foundrys Live-Daten (`/socket.io/`) begrenzt du nicht; hat sie einen eigenen Block, bleibt er, wie er
ist.

Wenn du die Einrichtungsseite von Foundry (`/setup`, Welten starten, Module) nur zu Hause brauchst, sperr sie für außen.
Heimnetz anpassen, falls deins anders heißt:

```
location ^~ /setup {
    allow 192.168.1.0/24;
    deny all;
    # hier die proxy_-Zeilen aus location / kopieren
}
```

Dann prüfen und neu laden:

```
nginx -t
systemctl reload nginx
```

**Das siehst du:** `nginx -t` meldet `syntax is ok` und `test is successful`. Foundry lädt wie vorher, auch eine große
Karte. `curl -sI https://<dein-name> | grep -i server` zeigt `nginx` ohne Versionsnummer. Vom Handy (mobile Daten) zeigt
`https://<dein-name>/setup` `403 Forbidden`, falls du die Sperre eingebaut hast. Klappt etwas nicht: `cp -a
/root/nginx-vorher/. /etc/nginx/` und `systemctl reload nginx`.

**Probelauf, dann scharf.** Lass den Probebetrieb einen ganzen Spielabend laufen, mit allem, was dazugehört: eine große
Karte betreten und nach dem Einspielen eines Moduls Strg+F5. Danach dort, wo nginx läuft:

```
zgrep -c 'limiting requests' /var/log/nginx/error.log*
```

Steht hinter jeder Datei `:0`, wäre an dem Abend niemand gebremst worden. Dann nimmst du die Zeile `limit_req_dry_run on;`
wieder heraus, `nginx -t` und `systemctl reload nginx`; ab jetzt greift die Grenze. Steht irgendwo mehr als 0, zeigt

```
zgrep 'limiting requests' /var/log/nginx/error.log* | tail -5
```

welche Zone (`by zone "foundry_alles"` oder `"foundry_anmeldung"`) und welche Adresse (`client: ...`). Sind es deine
Spieler in `foundry_alles`, setz `burst` von 200 auf 400 und wiederhole den Probelauf, bevor du scharf schaltest.

Hakt es später trotzdem beim Betreten einer Welt (Bilder fehlen, im error.log steht `limiting requests`), ebenso `burst`
von 200 auf 400 setzen.

## 6. Port 80 und das Zertifikat

Holt certbot das Zertifikat für Foundry über Port 80 (HTTP-Prüfung), braucht es die Freigabe TCP 80 auf nginx. Dann leitet
nginx auf Port 80 nur auf HTTPS um und beantwortet `/.well-known/acme-challenge/`, sonst nichts. Holt certbot das
Zertifikat über DNS, braucht es Port 80 gar nicht; dann die Freigabe löschen.

**Das siehst du:** `http://<dein-name>` springt auf `https://`.

## 7. fail2ban auf dem Host

fail2ban liest Protokolle und sperrt Adressen, die dort auffallen, mit einer Firewallregel. **Im unprivilegierten LXC kann
fail2ban keine Firewallregeln setzen:** Der Container darf die Netzfiltertabellen nicht anfassen, und sein Verkehr läuft
ohnehin über die Brücke des Hosts. Darum liefe fail2ban auf dem Proxmox-Host und liest die Protokolle der Container dort,
wo der Host sie sieht: Solange ein Container läuft, liegt sein Wurzelverzeichnis auf dem Host unter
`/var/lib/lxc/<CT-Nummer>/rootfs`. Prüfen:

```
ls /var/lib/lxc/150/rootfs/var/lib/funk
```

Das zeigt die Zustandsdateien des Funks (`konten.json` und so weiter). Zeigt es nichts, läuft der Container nicht oder
liegt woanders; dann hier nicht weitermachen.

**Einrichten:**

```
apt install fail2ban
```

**Damit fail2ban nicht still ausfällt.** Fehlt die Logdatei eines eingeschalteten Jails, startet fail2ban gar nicht, auch
nicht mit den übrigen Jails. Das kann an zwei Stellen passieren:

1. Das Jail `sshd`, das Debian von selbst einschaltet, liest `/var/log/auth.log`. Zeigt `ls /var/log/auth.log` auf dem
   Host `No such file or directory`, soll es das Journal lesen:

```
nano /etc/fail2ban/jail.d/sshd-journal.conf
```

   mit diesem Inhalt:

```
[sshd]
backend = systemd
```

   Dann `systemctl restart fail2ban`. Meldet fail2ban danach im Journal, `systemd` sei als backend nicht verfügbar:
   `apt install python3-systemd` und noch einmal `systemctl restart fail2ban`.

2. Die Pfade unter `/var/lib/lxc/<CT-Nummer>/rootfs` gibt es nur, solange der Container läuft. Nach einem Neustart des
   Hosts soll fail2ban deshalb erst nach den Containern starten:

```
mkdir -p /etc/systemd/system/fail2ban.service.d
nano /etc/systemd/system/fail2ban.service.d/nach-containern.conf
```

   mit diesem Inhalt:

```
[Unit]
After=pve-guests.service
```

   und dann `systemctl daemon-reload`. Das wirkt für Container, die mit dem Host starten (in Proxmox beim Container
   **Optionen**, **Beim Booten starten**; der Funk-Container hat das aus `HEIMSERVER-PROXMOX.md`, Abschnitt 2). Hältst du
   einen Container länger an, kann fail2ban dessen Datei aus den Augen verlieren; nach dem Wiederanlaufen
   `systemctl restart fail2ban` und wie gleich unten nachsehen.

**Das siehst du,** nach der Einrichtung, nach jeder Änderung an einem Jail und nach jedem Neustart des Hosts:

```
systemctl is-active fail2ban
fail2ban-client status
```

Zuerst `active`, dann unter `Jail list:` alle Jails, die du eingeschaltet hast (am Anfang nur `sshd`). Steht dort `failed`
oder fehlt ein Jail, nennt `journalctl -u fail2ban -n 20 --no-pager` den Grund, bei einer fehlenden Datei etwa `Have not
found any log file for <jail> jail`: Dann läuft der Container nicht, oder der Pfad im Jail ist vertippt.

**Wichtig für Container:** Verkehr, den der Host an einen Container weiterreicht, läuft durch die Kette FORWARD, nicht
INPUT. Die Jails unten sperren deshalb in FORWARD. Das wirkt nur, wenn der Host den Brückenverkehr durch iptables schickt:

```
sysctl net.bridge.bridge-nf-call-iptables
```

Steht dort `= 1`, passt es (Proxmox setzt das, sobald für eine Netzwerkkarte eines Containers die Firewall an ist: in
der Weboberfläche beim Container **Netzwerk**, **net0**, Haken **Firewall**). Steht dort `= 0` oder kommt ein Fehler,
sperrt fail2ban ins Leere; dann bleibt es bei der Sperrliste des Funks und limit_req.

### 7.1 nginx

Läuft nginx in einem Container, liegen seine Protokolle auf dem Host unter
`/var/lib/lxc/<CT-Nummer-nginx>/rootfs/var/log/nginx/`; läuft es auf dem Host selbst, unter `/var/log/nginx/` (dann gilt
`chain = INPUT` statt `FORWARD`).

```
nano /etc/fail2ban/jail.d/nginx.conf
```

```
[nginx-limit-req]
enabled   = false
logpath   = /var/lib/lxc/<CT-Nummer-nginx>/rootfs/var/log/nginx/error.log
backend   = polling
chain     = FORWARD
port      = http,https
maxretry  = 10
findtime  = 10m
bantime   = 1h

[nginx-botsearch]
enabled   = true
logpath   = /var/lib/lxc/<CT-Nummer-nginx>/rootfs/var/log/nginx/access.log
backend   = polling
chain     = FORWARD
port      = http,https
maxretry  = 3
findtime  = 10m
bantime   = 1h
```

`nginx-limit-req` sperrt Adressen, die immer wieder an limit_req aus Punkt 5 stoßen, und zwar gleich für eine Stunde.
Deshalb steht es oben auf `enabled = false`. Einschalten (`enabled = true`, dann `systemctl restart fail2ban`) erst, wenn
limit_req scharf ist (Punkt 5, nach dem Probelauf) und danach ein ganzer Spielabend ohne `limiting requests` im error.log
lief. Sonst sperrt es Spieler, die nur viele Dateien auf einmal laden, etwa nach Strg+F5. Sperrt es doch einmal jemanden
von euch: `fail2ban-client unban <adresse>` auf dem Host. `nginx-botsearch` sperrt Adressen,
die typische Angriffspfade abklopfen (`wp-login.php`, `phpmyadmin` und Verwandte) und dabei 404 bekommen. Ob dein
Protokoll solche Zeilen hat, zeigt vorher:

```
fail2ban-regex /var/lib/lxc/<CT-Nummer-nginx>/rootfs/var/log/nginx/access.log /etc/fail2ban/filter.d/nginx-botsearch.conf
```

Steht unten `Lines: ... matched` mit 0, lass `nginx-botsearch` auf `enabled = false`; Foundry beantwortet solche Pfade
dann anders, und limit_req wirkt trotzdem.

### 7.2 Funk (optional, für deinen Heimserver nicht empfohlen)

Für den Funk ist fail2ban nur ein Zusatz, und für deinen Heimserver lässt du es besser weg. Die Sperrliste im Server
sperrt schon, vor jeder Arbeit, und sie lässt ein Mitglied hinter einer gesperrten Adresse (CGNAT, Mobilfunk) alle 30 s
bis zum Handshake durch; sein freigegebenes Gerät hebt die Sperre dann auf. fail2ban am Host kennt keinen Durchlass: Solange es
eine Adresse sperrt, erreicht ein Mitglied hinter ihr weder den Funk noch Foundry (die Sperre gilt für alle Ports), und
`funk-admin entsperren --ip` hebt sie nicht auf, dafür braucht es auf dem Host `fail2ban-client unban <adresse>`. Das Jail
aus dem Paket sperrt deshalb nur 5 Minuten, so lange wie die erste Sperre des Servers, und steigert nicht; länger und
steigend sperrt der Server selbst.

Willst du es trotzdem: funk-server muss dafür die gesperrten Adressen im Klartext in eine eigene Datei schreiben; das
Journal bleibt ohne Klartext-IP (Datenschutz, `BETRIEB.md`). Voreinstellung: aus.

1. Im Funk-Container nachsehen, dass `server.json` noch keinen Abschnitt `sperrliste` hat:

```
grep -c sperrliste /etc/funk/server.json
```

   Das zeigt `0`. Dann `nano /etc/funk/server.json` und diese Zeile genau so, mit dem Komma am Ende und ohne ein weiteres
   Komma, direkt über die Zeile `"log": {` setzen (gleich eingerückt wie sie):

```
  "sperrliste": { "klartextDatei": "/var/log/funk/sperren.log" },
```

   Prüfen, und nur wenn die Prüfung besteht, neu starten (dafür steht `&&` dazwischen):

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen && systemctl restart funk-server
```

   Ein paar Sekunden später:

```
ls -l /var/log/funk/sperren.log
journalctl -u funk-server -n 20 --no-pager | grep Sperrliste
```

   **Das siehst du:** `Konfiguration in Ordnung.` (davor die Zeile zur Uhr wie in `HEIMSERVER-PROXMOX.md`, Abschnitt 15,
   Schritt 6). `ls` zeigt `sperren.log` mit Größe 0, dem Benutzer `funk` gehörend: funk-server legt die Datei beim Start
   an. Im Journal steht `Sperrliste: neue Sperren gehen zusätzlich mit Klartext-IP nach /var/log/funk/sperren.log`, aber
   keine Zeile `Sperrliste: Klartextdatei nicht schreibbar`; steht die da, fehlt in der Unit `LogsDirectory=funk`
   (`HEIMSERVER-PROXMOX.md`, Abschnitt 15, Schritt 5).

   Meldet `pruefen` stattdessen `Konfiguration nicht lesbar: ...` (etwa mit `trailing comma` oder `invalid start of a
   property name`), startet nichts neu, und der Server läuft weiter wie vorher. Dann steht die Zeile an der falschen Stelle
   oder hat ein Komma zu viel: berichtigen und den Befehl mit `pruefen && ...` wiederholen.

   **Zurück ohne Klartextdatei:** die Zeile wieder löschen, dann derselbe Befehl:

```
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen && systemctl restart funk-server
```

   Startet funk-server danach nicht (`systemctl is-active funk-server` zeigt `failed`), nennt `journalctl -u funk-server -n
   30 --no-pager` den Grund; nach dem Berichtigen `systemctl reset-failed funk-server` und `systemctl start funk-server`.

2. Auf dem Host Filter und Jail aus dem Paket (`linux/konfig/fail2ban/`) nach `/etc/fail2ban/filter.d/tacrelay.conf`
   und `/etc/fail2ban/jail.d/tacrelay.conf`, im Jail `<CT-Nummer>` ersetzen (150), dann prüfen:

```
fail2ban-regex /var/lib/lxc/150/rootfs/var/log/funk/sperren.log /etc/fail2ban/filter.d/tacrelay.conf
```

   Die Datei gibt es jetzt, leer; fail2ban-regex liest sie ohne Fehler und meldet 0 Zeilen. Zeilen hat sie erst nach der
   ersten Sperre. Kommt `No such file`, läuft der Container nicht, oder Schritt 1 fehlt.

3. Erst danach im Jail `enabled = true` und:

```
systemctl restart fail2ban
fail2ban-client status tacrelay
```

   **Das siehst du:** unter `File list` der Pfad aus dem Jail, unter `Currently banned` eine 0. Danach wie in Punkt 7 mit
   `fail2ban-client status` nachsehen, dass auch die anderen Jails noch laufen.

Jede Zeile in `sperren.log` hat diese feste Form, eine je neuer Sperre:

```
2026-10-01T09:30:05Z FUNK-SPERRE ip=203.0.113.7 netz=203.0.113.7/32 dauer=300 stufe=1 anlass=HANDSHAKE kennung=p4#0a1b2c3d4e
```

Die Datei wächst nur mit Sperren (etwa 150 Byte je Sperre). Wird sie groß, im Container leeren: `truncate -s 0
/var/log/funk/sperren.log`.

### 7.3 Prüfen, dass eine Sperre greift

Mit deinem PC über den Handy-Hotspot (mobile Daten, nicht dein WLAN): JG TacRelay verbindet. Die Adresse des Hotspots
zeigt etwa `wieistmeineip.de`. Auf dem Host:

```
fail2ban-client set tacrelay banip <adresse des hotspots>
```

JG TacRelay verliert die Verbindung und kommt nicht wieder herein, Foundry lädt vom Handy nicht mehr. Dann:

```
fail2ban-client set tacrelay unbanip <adresse des hotspots>
```

und alles geht wieder. Bleibt die Verbindung trotz Sperre, wirkt die Kette FORWARD nicht (siehe oben); dann das Jail
wieder auf `enabled = false`.

## 8. Wenn ein Mitglied draußen bleibt

Die Sperrliste des Funks sperrt nie Adressen, von denen in den letzten 14 Tagen ein Mitglied verbunden war (Vorrangspur),
und ein freigegebenes Gerät hebt eine Sperre seiner Adresse auf. Sitzt ein Mitglied aber an einer neuen Adresse, die ein
Angreifer gerade füllt (gleiche öffentliche IP beim Mobilfunk oder bei CGNAT), kann es eine Weile dauern, bis es
hereinkommt. Dann im Funk-Container:

```
dotnet /opt/funk/funk-admin.dll sperrliste --konfig /etc/funk/server.json
dotnet /opt/funk/funk-admin.dll entsperren --ip <adresse des mitglieds> --konfig /etc/funk/server.json
```

Die Adresse nennt dir das Mitglied (`wieistmeineip.de`). Mit fail2ban zusätzlich auf dem Host:
`fail2ban-client unban <adresse des mitglieds>`.

**Das siehst du:** `entsperren` antwortet `Sperre für p4#... aufgehoben.`, und das Mitglied verbindet beim nächsten
Versuch.
