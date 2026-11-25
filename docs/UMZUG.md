# Umzug des Funkservers

Stand 02.10.2026, Fassung 0.5.0. Für den Eigentümer und den neuen Betreiber: der Funk zieht von einem Heimserver (`heim.example.org`, TCP 8443) auf einen
Server bei einem künftigen Betreiber, ohne dass jemand ein neues Setup braucht und ohne dass Konten, Geräte, Kanäle, Mitglieder, Admins und
der Eigentümer verloren gehen. Grundlage ist der Entwurf zum Umzug in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Server und Betrieb". Den
neuen Server richtet der neue Betreiber mit `EINRICHTEN.md` ein.

Der Weg hier ist Variante A aus dem Entwurf: Der neue Server bekommt einen eigenen Schlüssel, kein privater Serverschlüssel
reist, und die Clients lernen den neuen Server über ein Übergangsprofil kennen, das ihnen der alte Server reicht
(Profilübernahme, F1). Namen und Ports unten sind Beispiele: `funk.example.org` für den neuen Betreiber, `heim.example.org` für den Heimserver.

## Vorweg: was gelten muss

- **Clients ab 0.4.0.** Erst 0.4.0 übernimmt ein neueres Profil vom Server (Profilübernahme); die Profilkarte zeigt
  dann `Vom Server übernommen, Version N.` Clients 0.2.0 und 0.3.0 tun das nicht und fänden den neuen Server nie.
  Deshalb bekommen alle zuerst das Setup 0.4.0, und danach setzt Sarevoc `mindestClientVersion` auf `0.4.0` (Schritt 1).
- **Der alte Server läuft mit 0.4.0** (`HEIMSERVER-PROXMOX.md`, Abschnitt 16). `umzug-packen` und `geraete --nicht-seit`
  gibt es erst ab 0.4.0, und erst ab 0.4.0 warnt `profil-neu-laden`, wenn im Profil der Pin des Servers fehlt oder es
  einen anderen TCP-Port nennt als `server.json` (Schritt 4).
- **Auf dem Stick die Programme 0.4.0.** `profil-erneuern` gibt es erst ab 0.4.0; dein `funk-offline` stammt aus einem
  älteren Paket. Kopier den ganzen Inhalt des Ordners `linux` aus dem Serverpaket 0.4.0 nach `funk-offline` und lass
  vorhandene Dateien ersetzen (`HEIMSERVER-PROXMOX.md`, Abschnitt 16, Schritt 11). `profil.json`, `profil.key`,
  `reserve.key` und `reserve.crt` bleiben dabei liegen; das Paket hat keine Dateien mit diesen Namen.
- **Derselbe TCP-Port auf beiden Servern.** Ein Profil kennt nur einen TCP-Port für alle Hosts. Im Übergang stehen
  beide Server im Profil, also muss der neue unter demselben Port erreichbar sein wie der alte: 8443. Der neue Betreiber antwortet in
  `funk-einrichten.sh` auf "TCP-Port am Server" mit 8443, auch wenn 443 frei wäre. 443 kommt, wenn überhaupt, später mit
  eigenem Plan.
- **Nur Sarevoc unterschreibt Profile**, mit `profil.key` auf seinem Stick. Ein Profil, das nicht mit dem eingebauten
  Schlüssel unterschrieben ist, nimmt kein Client und kein `funk-einrichten.sh` an.
- **Die Kennung geht übers Telefon, mit Name und Port.** Die Anfragezeile darf jeden Weg nehmen; ob sie wirklich vom neuen Betreiber
  kommt und unterwegs niemand etwas daran geändert hat, zeigt nur der Vergleich am Telefon. Die Kennung deckt nur den Pin,
  deshalb gehören Name und Port dazu.
- **Der Stick gehört geschützt.** `profil.key` und `reserve.key` liegen dort als unverschlüsselte Dateien; wie Sarevoc ihn
  schützt, entscheidet er selbst.

## Vorlauf, ein bis zwei Wochen vorher

**1. Neue Clients an alle.** Alle Mitglieder bekommen das Setup 0.4.0 (`<zeit>_JG-TacRelay-Setup-0.4.0.exe`), die
Admins das Admin-Setup 0.4.0, jeweils mit **Aktualisieren**. Haben alle aktiven Mitglieder es installiert, setzt Sarevoc auf
dem alten Server in `/etc/funk/server.json` die Zeile `"mindestClientVersion": "0.1.0",` auf `"mindestClientVersion":
"0.4.0",` (nur die Zahl ändern, Anführungszeichen und Komma bleiben), dann prüfen und, nur wenn die Prüfung besteht, neu
starten:

```
nano /etc/funk/server.json
dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen && systemctl restart funk-server
```

**Das siehst du:** `Konfiguration in Ordnung.`, dann startet der Dienst neu, und `journalctl -u funk-server -n 20
--no-pager` zeigt `Funk-Server 0.5.0 bereit`. Ein Client unter 0.4.0 bekommt danach beim Verbinden `VERSION_ZU_ALT`
und braucht das Setup 0.4.0. Meldet `pruefen` einen Fehler, startet nichts neu; dann die Zeile berichtigen.

**2. Der neue Betreiber richtet den neuen Server ein.** Der Name `funk.example.org` zeigt im DNS auf die Adresse des neuen Servers, mit kurzer TTL
(höchstens 300 s). Dann nach `EINRICHTEN.md`, Abschnitte 1 und 2, mit TCP-Port 8443. Es endet mit
`WARTET AUF PROFIL` und der Anfragezeile. Der neue Betreiber schickt Sarevoc die Zeile und liest ihm am Telefon die Kennung vor, dazu Name
und Port.

**3. Sarevoc schreibt das Übergangsprofil.** Stick einstecken, im Ordner `funk-offline` Doppelklick auf
`Profil erneuern.cmd`. Die Datei und das Werkzeug dahinter kommen mit dem Serverpaket 0.4.0 (Vorweg, "Auf dem Stick die
Programme 0.4.0"); danach liegen sie neben `profil.json`, `profil.key` und `reserve.crt`. Nur die cmd-Datei dazulegen
reicht nicht: Ein älteres `funk-admin.dll` antwortet `Unbekannter Befehl: profil-erneuern. funk-admin hilfe zeigt alle.`
Sie ruft auf:

```
dotnet funk-admin.dll profil-erneuern --bisher profil.json --schluessel profil.key
```

Das Werkzeug zeigt das bisherige Profil, Version 1. Auf "Eingabe:" fügst du die Zeile des neuen Betreibers ein (Rechtsklick). Es zeigt,
was neu ist: der Host `funk.example.org` und der Pin des neuen Servers. Dann fragt es "Hat der Server-Admin dir am Telefon genau diese Kennung
vorgelesen: XXXX-XXXX-XXXX-XXXX, dazu den Namen funk.example.org und den Port 8443? (j/n)". Nur wenn Kennung, Name und
Port Zeichen für Zeichen stimmen: j. Die Kennung deckt nur den Schlüssel des neuen Servers; Name und Port stehen ungeschützt in der Zeile.

Nennt die Zeile einen anderen Port als dein Profil (etwa 443), lehnt das Werkzeug vor der Frage ab, mit `TCP-Port 8443 ->
443: ...` und `Nichts geändert.` Ein Profil kennt nur einen Port, und im Übergang steht dein Server mit drin. Dann legt
der neue Betreiber `/etc/funk/server.json` zur Seite, ruft `funk-einrichten.sh` noch einmal auf, antwortet bei den Portfragen mit 8443
(`EINRICHTEN.md`, "Wenn etwas fehlt") und schickt dir die neue Zeile.

**Das siehst du:** `Urteil: Uebernehmen` und `Kopie zum Verschicken: ...\zum-verschicken\profil.json`. Das neue Profil
ist Version 2 mit beiden Hosts (erst Sarevocs, dann der neue) und drei Pins: Sarevocs Server, der neue Server und die Reserve. Mehr
passen nicht hinein. Die bisherige Fassung liegt als `profil-v1.json` daneben.

**4. Sarevoc legt Version 2 auf den alten Server.** Nicht an einem Spielabend: Am Ende verbinden sich alle Clients einmal
neu. `zum-verschicken\profil.json` wie in `HEIMSERVER-PROXMOX.md`, Abschnitt 6, in den Container nach
`/etc/funk/profil.json`, dann zuerst prüfen:

```
dotnet /opt/funk/funk-admin.dll pruefen --vollstaendig --konfig /etc/funk/server.json
```

**Das siehst du:** unter anderem `OK      Serverprofil Version 2: ...` mit beiden Hosts und `TCP-Port 8443.`, dazu
`OK      Pin des Serverschlüssels XXXX-XXXX-XXXX-XXXX steht im Profil.` mit dem Pin deines Servers. Auf den alten Server
gehört nur ein Profil, in dem sein eigener Pin steht und sein Port (Version 2, nie Version 3 aus Schritt 10): Ein Profil
ohne den Pin des Servers, der es reicht, nimmt kein Client an, und mit einem anderen Port suchen alle Clients deinen
Server unter diesem Port. Steht dort `FEHLER  Pin des Serverschlüssels ... steht nicht im Profil` oder
`WARNUNG Das Serverprofil nennt TCP-Port ...`, lädst du diese Datei nicht: `profil-v1.json` vom Stick zurück nach
`/etc/funk/profil.json`, und Version 2 noch einmal schreiben (Schritt 3). Sonst laden:

```
dotnet /opt/funk/funk-admin.dll profil-neu-laden --konfig /etc/funk/server.json
```

**Das siehst du:** `Profil Version 2 geladen.` und keine Zeile `WARNUNG` darunter. Erst dann neu starten, damit auch die
Clients Version 2 bekommen, die gerade verbunden sind; sie verbinden sich nach ein paar Sekunden von selbst wieder:

```
systemctl restart funk-server
```

**Das siehst du:** `journalctl -u funk-server -n 20 --no-pager` zeigt `Serverprofil Version 2 mit 3 Pins geladen` und
`Funk-Server 0.5.0 bereit`. Ab jetzt hat jeder verbundene Client Version 2. Dieselbe Datei geht an den neuen Betreiber und an Claude
(für neue Setups); sie ist öffentlich.

Hast du doch ein falsches Profil geladen, steht unter `Profil Version 3 geladen.` eine Zeile wie `WARNUNG Der Pin des
Serverschlüssels (XXXX-XXXX-XXXX-XXXX) steht nicht im Serverprofil Version 3: ...` oder `WARNUNG Das Serverprofil nennt
TCP-Port 443, ...`, dieselbe auch im Journal. Dann nicht neu starten, sondern gleich die vorige Fassung zurück: die
vorige `profil-v<N>.json` vom Stick nach `/etc/funk/profil.json` und noch einmal `profil-neu-laden`.

**5. Der neue Betreiber spielt Version 2 ein und prüft von außen.** Im Ordner `linux`:

```
sudo bash funk-einrichten.sh --profil profil.json
```

Der Server startet leer. Von einem PC außerhalb, in PowerShell: `Test-NetConnection funk.example.org -Port 8443`
zeigt `TcpTestSucceeded : True`. Danach den neuen Server **wieder anhalten**, bis zum Umzugsabend:

```
sudo systemctl disable --now funk-server
```

Warum: Die Clients versuchen die Hosts der Reihe nach, Sarevocs Server hat 250 ms Vorsprung. Antwortet er einmal nicht
(Neueinwahl, Neustart), landet ein Client auf dem leeren neuen Server und stellt dort einen Antrag. Läuft er doch
einmal leer mit: dort nichts freigeben; der Umzug legt diesen Stand zur Seite.

**6. Warten auf die Nachzügler.** Bis jedes aktive Gerät seit Schritt 4 einmal verbunden war. Auf dem alten Server,
mit dem Tag nach Schritt 4 (hast du Version 2 am 5. Oktober geladen, dann `2026-10-06`):

```
dotnet /opt/funk/funk-admin.dll geraete --nicht-seit <JJJJ-MM-TT> --konfig /etc/funk/server.json
```

Wer dort steht, verbindet sich einmal; sonst braucht er nach dem Umzug das neue Setup. Der Tag nach Schritt 4, weil
`geraete` den ganzen Tag ab 0 Uhr UTC zählt, auch das Ende einer Sitzung: Wer am Tag von Schritt 4 nur vor dem Laden
verbunden war, sähe mit diesem Tag aus wie verbunden, auch ohne Version 2. Dafür steht noch in der Liste, wer nur am Abend
von Schritt 4 nach dem Neustart verbunden war und Version 2 schon hat; einmal verbinden schadet nicht.

## Umzugsabend, der Funk ist etwa eine Viertelstunde aus

**7. Sarevoc packt den alten Server.** Erst sichern (dafür muss er noch laufen), dann anhalten, abschalten und packen:

```
dotnet /opt/funk/funk-admin.dll sicherung --konfig /etc/funk/server.json
systemctl disable --now funk-echo funk-server
dotnet /opt/funk/funk-admin.dll umzug-packen --aus /root/umzug.tru --konfig /etc/funk/server.json
```

`disable` gehört dazu: Startet der alte Server später wieder (etwa nach einem Neustart des Proxmox-Hosts), finden ihn
die Clients zuerst und arbeiten auf dem alten Stand. `umzug-packen` zeigt die Passphrase genau einmal, 26 Zeichen in
Vierergruppen. Nicht aufschreiben, nicht abfotografieren. Im Paket sind Konten, Geräte, Kanäle, Mitglieder, Admins,
Journal, laufende Aufträge (in `lage.json`) und der TOTP-Schlüssel, nicht der Serverschlüssel, nicht das
Betriebsgeheimnis, nicht die Vorrangspur und nicht die Sperrliste.
Das Paket holst du über den Host aus dem Container (`pct pull`) und schickst es dem neuen Betreiber auf jedem Weg; die Passphrase nur am
Telefon.

**8. Der neue Betreiber spielt es ein.** Das Paket in den Ordner `linux`, dann:

```
sudo bash funk-einrichten.sh --umzug umzug.tru
```

Es fragt nach der Passphrase (Bindestriche und Leerzeichen egal, drei Versuche).

**Das siehst du:** `Eingespielt nach /var/lib/funk: lage.json, konten.json, ...`, `Eingespielt nach /etc/funk/credentials:
funk-totp-schluessel.`, dann `bereit` und der Abschluss mit `Weiter: prüfen mit sudo funk-admin konten, ...`. Was vorher
auf dem neuen Server lag, liegt mit der Endung `.vor-umzug-<zeit>` daneben. Bei einer falschen Passphrase bleibt alles,
wie es war.

**9. Prüfen.** Der neue Betreiber: `sudo funk-admin konten` (Sarevoc als `ADMIN` und `EIGENTÜMER`), `sudo funk-admin geraete`,
`sudo funk-admin admins`. Sarevoc verbindet sich; nach dem `Bye{WARTUNG}` versuchen es die Clients von selbst wieder: Sarevocs Host
antwortet nicht mehr, der neue zeigt einen Pin aus Version 2. Wer mag, macht eine Echo-Probe (`EINRICHTEN.md`, Echo-Funker;
der Echo-Funker braucht auf dem neuen Server einen eigenen Geräteschlüssel).

## Nachlauf, zwei bis vier Wochen später

**10. Sarevoc schreibt Version 3.** Doppelklick auf `Profil erneuern.cmd`, die Zeile des neuen Betreibers noch einmal einfügen (oder
Eingabetaste, wenn sie als `profilanfrage.txt` neben `profil.json` liegt). Das Werkzeug erkennt den Abschluss: Host und
Pin stehen schon drin, daneben noch Sarevocs alter Server. Auf "Den alten Server herausnehmen?" mit j antworten, dann die
Kennung vergleichen. Version 3 kennt nur noch `funk.example.org`, den Pin des neuen Servers und die Reserve.

**11. Der neue Betreiber spielt Version 3 ein**, mit `sudo bash funk-einrichten.sh --profil profil.json`. Der laufende Server lädt es
neu, ohne Neustart; die Clients übernehmen es beim nächsten Verbinden. Claude baut die Setups mit Version 3.

**12. Erst jetzt aufräumen:** das alte LXC löschen (`pct destroy`), Sarevocs alten Serverschlüssel auf dem Stick und in den
vzdump-Archiven vernichten (`HEIMSERVER-PROXMOX.md`, Abschnitt 12). Setups mit Version 1 oder 2 nicht mehr weitergeben.

## Rückweg, bis Schritt 10

Sarevocs alter Server ist angehalten, aber unverändert. Der neue Betreiber hält seinen an, Sarevoc startet seinen wieder:

```
sudo systemctl disable --now funk-server
systemctl enable --now funk-server funk-echo
```

Die erste Zeile beim neuen Betreiber, die zweite im alten Container bei Sarevoc. Die Clients finden den alten Server über
`heim.example.org` mit dem alten Pin; beides steht in Version 2. Was auf dem neuen Server inzwischen geändert wurde,
holt `umzug-packen` dort zurück, eingespielt bei Sarevoc mit dem Dienst angehalten:

```
dotnet /opt/funk/funk-admin.dll umzug-einspielen --paket /root/zurueck.tru --konfig /etc/funk/server.json
```

## Warnungen

- **Derselbe TCP-Port** auf beiden Servern, siehe oben. Sonst erreichen die Clients mit Version 2 einen der beiden nicht.
- **Der alte Server läuft nach dem Umzugsabend nie wieder mit** (`disable` in Schritt 7). Er steht in Version 2 als
  erster Host.
- **Der neue Server läuft im Vorlauf nicht leer mit** (Schritt 5), sonst landen Clients dort, wenn Sarevocs Server kurz
  nicht antwortet.
- **Keine fremde Antwort unter einem Host aus dem Profil.** Seit 0.4.0 überspringt der Client bei der ersten
  Verbindung einen Host mit fremdem Schlüssel und versucht den nächsten; beim Wiederverbinden meidet er ihn 60 s. Trotzdem
  darf unter `funk.example.org` und `heim.example.org` kein anderer Dienst auf Port 8443 antworten.
- **Nachzügler:** Wer im Vorlauf nie verbindet, kennt nur Version 1 und braucht nach dem Umzug das neue Setup.
- **Gefälschte Anfrage:** Schickt jemand anderes eine Anfragezeile mit eigenem Namen und Pin und Sarevoc unterschreibt sie,
  verbinden sich alle Clients mit dessen Server. Deshalb nie ohne den Vergleich am Telefon j sagen, und zwar von Kennung,
  Name und Port: Die Kennung deckt nur den Pin, ein unterwegs geänderter Name schickte die Clients zu einem fremden Rechner.
- **Was reist:** Der TOTP-Schlüssel ist im Paket, damit der Zweitfaktor der Admins weiter gilt. Ohne ihn
  (`umzug-packen --ohne-totp`) richtet jeder Admin den Zweitfaktor an der Konsole neu ein (`admin-einrichtung-neu`). Das
  Paket ist verschlüsselt (AES-256-GCM, Schlüssel aus der Passphrase); die Passphrase geht nie den Weg des Pakets.
- **Konsole heißt Eigentümer:** Wer auf dem neuen Server root ist, darf am Verwaltungssocket alles, auch den Eigentümer neu
  setzen. Eigentümer bleibt Sarevoc; ob der Umzug erst nach V1 kommt, entscheidet Sarevoc.
- **Betriebsgeheimnis, Vorrangspur und Sperrliste** bleiben auf dem alten Server; der neue hat eigene. Das kostet nichts;
  die Sperrliste fängt auf dem neuen Server leer an.

## Notweg ohne Profilübernahme

Bleiben Clients unter 0.4.0, gibt es nur Variante B oder C aus dem Entwurf: Der neue Server zeigt einen Pin aus
Version 1, entweder Sarevocs alten Serverschlüssel (`umzug-packen --mit-serverschluessel` und
`funk-einrichten.sh --umzug <paket> --mit-serverschluessel`) oder den Reserveschlüssel, und der Eigentümer stellt
`heim.example.org` im DNS auf die Adresse des neuen Servers. Dann merken die Mitglieder nichts, aber ein privater Schlüssel reist, und
alte Kopien (Stick, vzdump) müssen danach weg. Mit dem Reserveschlüssel ist die Reserve verbraucht; eine neue trägt
`profil-erneuern` noch nicht ein.
