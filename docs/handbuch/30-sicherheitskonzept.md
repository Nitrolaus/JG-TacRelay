## Teil C: Sicherheitskonzept

Dieser Teil beschreibt, wovor JG TacRelay schützt, mit welchen Mitteln, warum gerade so, und wo der Schutz bewusst endet. Er richtet sich an Admins, an den Betreiber des Servers und an alle, die das System prüfen wollen. Geheimnisse stehen hier keine: Wo ein Schlüssel, ein Code oder ein Geheimnis vorkommt, ist nur seine Art beschrieben, nie sein Wert. Wie man die Dinge einrichtet und bedient, steht in Teil B; dieser Teil verweist dorthin, statt Anleitungen zu wiederholen.

**Stand.** Wie in 1.9: Fassung 0.5.0 für Client, Server und Protokoll (Code-Stand `95aa78b`, Datum 02.10.2026). Gegenüber Server 0.4.0 und Client 0.4.1 ändert 0.5.0 für die Sicherheit drei Dinge, alle in C13: die Einsatzphasen (Nachrichten 0x003D und 0x4004), "Heute als Funker" für Admins (0x003C) und die Rangregel, die jetzt auch den Funker-Modus kennt. Schlüssel, Anmeldung, Admin-Nachweis (C11), Sprachkanal (C9) und Sperrliste (C14) sind gegenüber 0.4.0 unverändert (am Code geprüft: `Funk.Protokoll\Sprache` und `Funk.Server\Sprachpfad` sind bis auf die neue Kopfzeile in jeder Quelldatei Byte für Byte dieselben). Was 0.5.0 sonst bringt, sitzt im Client und ändert keine Prüfung des Servers: das Funkfeld in der Ablage im Infobereich, die Meldezeile, die Melde-Taste (sie spricht nur dorthin, wo der Server das Senderecht ohnehin vergibt), der einstellbare Funk-Klick (er entsteht auf dem eigenen PC und geht nie durch die Leitung, E3.9) und die Sprachaktivierung als Voreinstellung für neue Netze (B12.1). Der Server läuft heute als Heimserver in einem Proxmox-Container hinter einem DSL-Anschluss (TCP 8443, UDP 47443) und zieht erst nach Version 1 und einem Test mit ein paar Leuten auf einen gemieteten Server um (Anf. A55). Auf dem Heimserver läuft nach Angabe des Eigentümers noch der Server 0.4.0; das Update auf 0.5.0 steht aus (nicht am Server nachgesehen; `HEIMSERVER-PROXMOX.md`, Abschnitt 17). Was davon abhängt, wo der Server steht, ist im Text gekennzeichnet.

**Quellen.** Jede Zahl stammt aus dem Code oder aus der gemessenen Doku im Repository: dem Funkprotokoll (`docs/FUNKPROTOKOLL-v1.md`, im Text kurz "Protokoll 3.9" für Abschnitt 3.9), `docs/BETRIEB.md`, `docs/HEIMSERVER-HAERTUNG.md`, `docs/HEIMSERVER-PROXMOX.md`, `docs/EINRICHTEN.md`, `docs/UMZUG.md` und den Anforderungen A1 bis A65. Was nur geplant, geschätzt oder noch ungeprüft ist, steht ausdrücklich so da.

**Schreibweise der Befehle.** `funk-admin <befehl>` ist der Kurzbefehl, den `funk-einrichten.sh` anlegt; auf einem von Hand eingerichteten Server wie dem Heimserver heißt derselbe Aufruf `dotnet /opt/funk/funk-admin.dll <befehl> --konfig /etc/funk/server.json` (B0.2). Die Befehle laufen als root oder mit `sudo`. Alle `funk-admin`-Befehle in diesem Teil sind gegen die Optionsprüfung der Fassung 0.5.0 (`Funk.Admin\Befehlsoptionen.cs`) und gegen `funk-admin hilfe` geprüft. Für Phasen und den Funker-Modus (C13) gibt es keinen Konsolenbefehl; beides geschieht im Client.

**Rollen.** Das Programm gehört Sarevoc, alle Rechte liegen bei ihm (Anf. A41, Q23 vom 19.10.2026). Solange der Server bei Sarevoc läuft, ist Sarevoc *Eigentümer* im Sinne des Servers (ein Merkmal genau eines Admin-Kontos, C12) und verwahrt den Profilschlüssel. Admins ernennt nur Sarevoc (Antwort 1 vom 30.09.2026, Anf. A50).

## C1 Auf einen Blick

- **Kein Passwort, kein gemeinsamer Zugangscode, kein Token.** Jeder Rechner hat einen eigenen Geräteschlüssel, möglichst im TPM, und beweist ihn bei jeder Verbindung neu. Der Server kennt davon nur den öffentlichen Teil.
- **Den Server erkennt der Client an seinem Schlüssel (Pin), nicht an einem Zertifikat einer Zertifizierungsstelle.** Welche Server und Pins gelten, steht im Serverprofil, und das unterschreibt nur der Profilschlüssel. Der liegt offline, nie auf dem Server.
- **Steuerkanal über TLS 1.3** (TLS 1.2 nur für Windows 10) mit Gerätezertifikat; **Sprache mit AES-256-GCM**, Schlüssel je Sitzung, Wechsel spätestens nach 60 Minuten, Replay-Fenster gegen wiederholte Pakete.
- **In den Funk kommt nur, wen ein Admin freigibt,** nachdem er die Gerätekennung mit dem Mitglied verglichen hat. Ein Antrag allein öffnet nichts.
- **Verwaltungsrechte wirken nur in einer Admin-Sitzung:** mit einem eigenen Admin-Schlüssel im TPM und einem Code aus der Authenticator-App. Heikle Schritte brauchen einen höchstens 5 Minuten alten Code.
- **Admins ernennt nur der Eigentümer an der Serverkonsole.** Über das Netz führt kein Weg zu Admin-Rechten. Umgekehrt gilt: Wer auf dem Server root ist, hat alles.
- **Der Server entscheidet alles selbst:** Rechte je Handlung, Hören je Empfänger, Senderecht je Sprachpaket. Ein veränderter Client gewinnt dadurch nichts.
- **Einsatzphasen und "Heute als Funker" (ab 0.5.0) vergeben keine Rechte.** Eine Phase schaltet nur, wer `ZUWEISEN` hat, und nur Konten, die er nach der Rangregel einteilen darf. "Heute als Funker" nimmt einem Admin für einen Abend Rechte ab und gibt ihm keine (C13).
- **Eine eingebaute Sperrliste** bremst Scanner, Botnetze und Durchprobierer, ohne dass dafür etwas einzurichten ist. Mitglieder kommen über die Vorrangspur auch unter Angriff herein.
- **Datensparsam:** IP-Adressen und Konto-Nummern stehen im Logbuch nur als HMAC-Kürzel. Klarnamen gibt es nur im Journal (90 Tage) und an der Konsole. Telemetrie gibt es keine.
- **Nicht abwehrbar** ist eine Überlastung des Heimanschlusses, und die Heim-IP sehen alle Mitglieder. Eine Ende-zu-Ende-Verschlüsselung gibt es nicht: Der Server sieht die Sprache.

## C2 Schutzziele

Was geschützt wird und warum (Protokoll 1.3):

| Nr | Schutzgut | Warum es zählt | Vor allem geschützt durch |
| --- | --- | --- | --- |
| G1 | Verfügbarkeit während eines Einsatzes | Ein Ausfall mitten in der Schlacht ist der größte reale Schaden. | Grenzen je Adresse, Vorrangspur, Sperrliste mit Durchlass, Strom-Abwurf statt Paketverlust, schnelles Wiederverbinden |
| G2 | Vertraulichkeit der Sprache, vor allem Kommando | Rivalen sollen nicht mithören. | TLS, AES-256-GCM, Hören nur als Mitglied |
| G3 | Lagebild: Aufstellung, wer auf welchem Schiff sitzt, wer welche Netze hört | Für eine rivalisierende Org wertvoll (Lehre aus dem Mumble-Gastzugang). | nichts ohne freigegebenes Gerät; Warteraum ohne Einblick |
| G4 | Echtheit von Absender und Netz | Pegel je Funker, Anzeige, Befehlskette | Stempel durch den Server je Empfänger |
| G5 | Konten und Rechte | Wer ein Konto übernimmt, kommt an G2 und G3. | Geräteschlüssel, Freigabe durch Admins, Admin-Nachweis, Konsole |
| G6 | Heimanschluss und IP des Betreibers | In der Heimphase: Überlastung und Privatsphäre | nur teilweise; siehe C17 |

**Rangfolge.** G1 steht vorn. Deshalb sind automatische Strafen gedeckelt: Freigegebene Geräte zählen nie gegen die Grenzen für Unbekannte, die Sperrliste sperrt keine Adresse, von der in den letzten 14 Tagen ein Mitglied kam, und es gibt keine gemeinsame Grenze, über die Unbekannte freigegebene Geräte aussperren könnten (Protokoll 1.1, Z6). Lieber lässt das System einen Angreifer etwas länger anklopfen, als ein Mitglied vor einem Raid draußen stehen zu lassen.

## C3 Bedrohungsmodell

### C3.1 Übersicht

| Angreifer | Was er kann | Was ihn aufhält | Was bleibt |
| --- | --- | --- | --- |
| Scanner | Ports abklopfen, TLS-Proben, Zufallspakete, Fuzzing | Pflicht-Gerätezertifikat, ALPN, kurze Fristen, UDP ohne jede Antwort auf Ungültiges, Sperrliste | TLS-Stack und die Parser vor der Anmeldung sind für jeden erreichbar |
| Botnetz | viele Adressen, Handshake- und Antragsfluten | Grenzen je Adresse, Sperrliste, Vorrangspur für Mitglieder, globale Töpfe nur als CPU-Schutz | neue Anmeldungen können unter Angriff zeitweise scheitern; volumetrisch nicht abwehrbar |
| Durchprobieren | Codes, Zweitfaktor, Passphrasen, Anmeldungen am Host raten | lange Zufallswerte, Fehlversuchs-Töpfe, Zweitfaktor nur mit Admin-Schlüssel prüfbar, SSH nur mit Schlüssel | - |
| Gekapertes Discord-Konto | im Namen eines Mitglieds schreiben, eine fremde Kennung schicken | Freigabe nur durch einen Admin, Discord-Name eindeutig, Anhängen an ein bestehendes Konto nur nach Bestätigung | ein unaufmerksamer Admin |
| Übernommener Admin-PC | Geräte- und Admin-Schlüssel benutzen, mitlesen, was der Admin tippt | Admin-Sitzung nur mit Code vom Telefon, heikle Schritte nur mit frischem Code, Ernennen und Zweitfaktor nur an der Konsole, Warnungen, Journal | Schaden in den Minuten, in denen der Admin selbst angemeldet ist |
| Böswilliger Server | sich als Server ausgeben, Clients umleiten, als Betreiber alles lesen | Pin, offline signiertes Profil, Versionsregel; der Server kann kein Profil fälschen und keinen Code auf Clients bringen | wer den echten Server betreibt, sieht Sprache und Daten |
| Mithörer | im WLAN oder beim Provider mitlesen, Pakete umleiten, als Mitglied neugierig sein | TLS, AES-256-GCM, Hören nur als Mitglied (am Server), Pfadprüfung | Metadaten: wer wann wie viel spricht |
| Rivalisierende Org ohne Konto | hat vielleicht den Installer, kennt Host und Port | nichts ohne freigegebenes Gerät; Warteraum verrät nichts über Netze, Konten und Aufstellung | Anträge nur gedrosselt |
| Neugieriges Mitglied mit verändertem Client | jede Nachricht schicken, auch ohne Knopf dafür | Rechteprüfung am Server, Stempel durch den Server, Rangregel, eigene Bremse für Phasen und Funker-Modus (C3.10) | sieht, was jedes Mitglied sehen darf |
| Troll im eigenen Kreis | dauernd sprechen, fluten | Paket- und Byte-Topf je Sitzung, Stummschalten, Sperre durch Admin | - |
| Infostealer auf einem Mitglieds-PC | Dateien und Speicher des Benutzers lesen | Schlüssel im TPM nicht exportierbar, keine Tokens und Passwörter auf der Platte | mit Software-Schlüssel (ohne TPM) kopierbar |
| Dieb einer Sicherung | Zustandsdateien lesen | TOTP-Geheimnisse nur verschlüsselt, Notfallcodes nur als HMAC, Schlüssel nicht in `funk-admin sicherung` | Klarnamen im Journal; eine Container-Sicherung enthält auch die Schlüssel |
| Störer mit viel Bandbreite | den DSL-Anschluss zuschütten | nichts im Protokoll | volumetrisch nicht abwehrbar (Umzug) |

Die Zeilen folgen dem Bedrohungsmodell des Protokolls (Protokoll 1.4, T1 bis T15). Die sieben ersten Fälle stehen unten ausführlich; was ein Mitglied, ein Beauftragter oder ein Admin mit den Phasen und dem Funker-Modus von 0.5.0 anstellen könnte, steht in C3.10.

### C3.2 Scanner

Ein Scanner findet zwei offene Ports: TCP für den Steuerkanal und UDP für die Sprache. Mehr gibt es nicht: keine Web-Oberfläche, keinen HTTP-Endpunkt, keinen Admin-Port, keinen Port 80 (Protokoll 0, `BETRIEB.md`, Abschnitt "Was läuft").

- **TCP:** Der Server verlangt im TLS-Handshake ein Client-Zertifikat mit einem P-256-Schlüssel und das ALPN-Kennzeichen `tacrelay/1`; fehlt eins davon, trennt er. Der Handshake hat 5 Sekunden, danach das erste `Hello` 3 Sekunden. Vor `Hello` nimmt er höchstens 4096 Byte je Rahmen an, im Warteraum höchstens 1032. Das Pflicht-Zertifikat hält nur einfache Scanner ab, denn jeder kann sich ein P-256-Zertifikat erzeugen (Protokoll 1.4, T3); dahinter wirken die Fristen, die Grenzen und die Sperrliste.
- **UDP:** Auf ein ungültiges oder nicht authentisiertes Datagramm antwortet der Server nie, weder mit Fehler noch mit Echo (Protokoll 5.9). Ein Scanner sieht einen stummen Port.
- **Sperrliste:** Gescheiterte Handshakes, Handshakes ohne Gerätezertifikat und Protokollmüll vor der Anmeldung bringen Punkte; ab 10 Punkten in 10 Minuten ist die Adresse gesperrt, und jede weitere Verbindung von dort endet sofort nach der Annahme mit einem Reset, noch vor TLS (C14).

**Was bleibt:** Der TLS-Stack von .NET und die Parser für Rahmen, Hello, Anträge und JSON sind für jeden erreichbar. Sie prüfen jede Länge, bevor sie Speicher anfordern, und werden mit Zufallsmutationen geprüft (C18).

### C3.3 Botnetze

Ein Botnetz verteilt seine Versuche auf viele Adressen, sodass jede einzelne unter den Grenzen bleibt.

- **Grenzen je Adresse** gelten trotzdem: 10 Handshakes je Minute (Stoß 5) für unbekannte Adressen, 8 gleichzeitige Verbindungen, höchstens 2 unfreigegebene Verbindungen je Adresse und 20 serverweit, 3 Codes oder Anträge je Verbindung und 10 je Adresse und Stunde, höchstens 30 offene Anträge.
- **Globale Töpfe** (200 Handshakes je Sekunde) sind nur CPU-Schutz. Die **Vorrangspur** geht an ihnen vorbei: Adressen, von denen in den letzten 14 Tagen eine aktive Sitzung kam, kommen auch während einer Flut herein (Protokoll 4.8, 7.2).
- **Freigegebene Geräte** zählen nie gegen die Grenzen für Unbekannte. Bei Überlast (Stufe 2) nimmt der Server keine neuen Anträge und unfreigegebenen Verbindungen mehr an (`SERVER_VOLL`), freigegebene Geräte aber immer, bis 100 Sitzungen.
- **Antragsflut:** Ist die Tabelle der Zähler für Codes und Anträge voll (4096 Adressen je Stunde, Alarm `REGISTRIERUNG_FLUT`), bekommt jede neue Adresse `GRENZE`, aber keine Sperrlisten-Punkte. Sonst sperrte eine Flut aus vielen Adressen die echten neuen Mitglieder gleich mit aus. Gegen 30 volle Antragsplätze hat die Antragsliste im Verwaltungsteil den Knopf "Alle ablehnen, die zu keiner Vormerkung passen".

**Was bleibt:** Neue Anmeldungen können unter Angriff zeitweise scheitern; bestehende Mitglieder bleiben drin. Ein Angreifer mit sehr vielen Adressen (bei IPv6 vielen /48-Netzen) bleibt unter jeder Schwelle der Sperrliste, dann wirken nur die globalen Töpfe. Gegen ein volumetrisches Zuschütten der Leitung hilft das Protokoll nicht (C17).

### C3.4 Durchprobieren

Es gibt keine Passwörter, die man raten könnte. Was es an Codes gibt, ist entweder lang genug, kurzlebig und gezählt, oder es ist ohne einen zweiten, nicht ratbaren Faktor wertlos.

| Was | Stärke | Was das Raten begrenzt |
| --- | --- | --- |
| Code aus der Authenticator-App (Admins) | 6 Ziffern, 30 Sekunden | Ein Versuch zählt nur mit gültiger Unterschrift des Admin-Schlüssels aus dem TPM. 5 Fehlversuche in 15 Minuten ergeben 15 Minuten Pause, 10 in 24 Stunden sperren bis zur Konsole. |
| Notfallcode (Admins) | 12 Zeichen Crockford-Base32, 60 Bit | dieselben Töpfe wie der Code |
| Einmalcode (nur der Weg mit Code) | 12 Zeichen Crockford-Base32, 60 Bit, 5 Minuten, einmal | 3 je Verbindung, 10 je Adresse und Stunde; über den Bot 5 Fehlversuche je Discord-ID und Stunde |
| Gerätekennung, Admin-Kennung, Pin | kein Geheimnis | Sie beweisen nichts; den Besitz des Schlüssels beweist der TLS-Handshake bzw. die Unterschrift. |
| Passphrase des Umzugspakets | 26 Zeichen Base32 (130 Bit), PBKDF2-SHA256 mit 600.000 Runden | drei Eingabeversuche je Aufruf; gegen Raten an einem gestohlenen Paket hilft allein die Länge |
| SSH am Proxmox-Host | Schlüssel statt Passwort | Passwortanmeldung abgeschaltet (C16) |
| Proxmox-Oberfläche | nur im Heimnetz, mit Zweitfaktor | nicht von außen erreichbar |

**Zur Einordnung des Zweitfaktors:** Ein zufälliger Code trifft mit etwa 3 zu einer Million, weil drei Zeitschritte gelten. Bei höchstens 10 Versuchen bis zur Sperre bleibt die Chance unter 1 zu 30.000, und ein Angreifer braucht dafür schon den Admin-Schlüssel im TPM des Admins. Weil Fehlversuche nur mit gültiger Unterschrift zählen, kann ein Fremder einen Admin auch nicht durch absichtliche Fehlversuche aussperren.

### C3.5 Gekapertes Discord-Konto

Discord ist heute nur der Weg, auf dem ein Mitglied dem Admin seinen SC-Namen und seine Gerätekennung schickt. Einen Discord-Bot gibt es noch nicht; Discord gibt keine Rechte und gibt nichts frei (Protokoll 1.2: "Keine Freigabe von Geräten über Discord").

Wer das Discord-Konto eines Mitglieds übernimmt, kann dem Admin die Kennung seines eigenen Rechners schicken. Dagegen steht:

- **Freigabe nur durch einen Admin** in der Admin-Sitzung mit frischem Code oder an der Konsole. Ein Antrag führt nie von selbst in den Funk.
- **Der Discord-Name ist eindeutig** (Anf. A53). Trägt ihn schon ein Konto oder eine Vormerkung, bekommt das neue Konto ihn bei der Freigabe nicht (seit 0.7.0; bis 0.6.x lehnte der Server die Freigabe dann mit `NAME_VERGEBEN` ab). Ein Angreifer bekommt den Namen eines bestehenden Mitglieds also nicht noch einmal; er landet höchstens als neues Konto ohne diesen Namen, das der Admin sieht.
- **An ein bestehendes Konto** hängt seit 0.7.0 nur noch die Konsole ein Gerät (ein PC, ein Konto), und auch sie nur ausdrücklich (`freigeben <kennung> --an-konto <id>`), und nur nach Bestätigung durch das Mitglied selbst, am sichersten im Sprachkanal. Der Hinweis `BESTEHENDES KONTO?` in `funk-admin antraege` ist nur ein Hinweis; angehängt wird nie von selbst. Sonst übernähme ein gekapertes Discord-Konto das Funkkonto samt Rollen.
- **Nie Admin-Rechte:** Geräte privilegierter Konten gibt nur die Konsole frei, und ein Admin-Schlüssel kommt nur über den Eigentümer an der Konsole ins System.

**Später mit Bot:** Dann verbindet ein Einmalcode, den der Client zeigt und der Nutzer in Discord eintippt, das Gerät mit der Discord-ID (Protokoll 3.4). Ein abgeschriebener oder aus einem Stream abgelesener Code verbindet höchstens das Gerät des Opfers mit dem Konto des Angreifers, und das lehnt das Opfer ab; auch dann braucht jede Verknüpfung noch die Freigabe durch einen Admin, und der Inhaber bekommt eine Nachricht (Protokoll 1.4, T5).

**Was bleibt:** ein Admin, der trotz Warnzeichen freigibt.

### C3.6 Übernommener Admin-PC

Läuft Schadcode auf dem PC eines Admins, kann er Geräte- und Admin-Schlüssel benutzen, aber nicht wegkopieren, denn beide liegen nicht exportierbar im TPM. Er kann mitlesen, was der Admin tippt.

- **Ohne Admin-Sitzung** hat ein Admin-Konto nur die Rechte der Führung und `ZUWEISEN` (Global, Durchsagen, einteilen); solange es "Heute als Funker" spielt (C13), ruhen auch die. Verwalten (Geräte, Konten, Netze) geht nur in einer Admin-Sitzung, und die braucht den Code vom Telefon.
- **Heikle Schritte** (freigeben, sperren, Rechte, Aufträge, Flotte übernehmen) brauchen einen höchstens 5 Minuten alten Code. Die Admin-Sitzung endet nach 12 Stunden, mit der Verbindung und mit "Verwaltung schließen".
- **Nie über das Netz:** Admins ernennen oder entziehen, den Zweitfaktor neu einrichten, den Eigentümer anfassen, andere Admin-Konten entsperren, herabstufen oder löschen. Das geht nur an der Konsole. Besonders wichtig: Könnte ein Admin seinen Zweitfaktor im Client neu einrichten, bräuchte Schadcode nur einen einzigen mitgelesenen Code, um sich ein eigenes Geheimnis zu holen und danach jederzeit Admin-Sitzungen zu öffnen, auch nachts.
- **Warnungen:** Öffnet jemand eine Admin-Sitzung von einem Gerät, das noch nie eine hatte, bekommt jedes Admin-Konto eine Meldung, auch ohne offene Admin-Sitzung. Ebenso bei Notfallcodes, Sperren des Zweitfaktors und Änderungen an Admins.
- **Journal:** Jede Verwaltungshandlung steht mit Klarnamen im Journal.
- **Notbremse:** An der Konsole `funk-admin admin-sperren --konto <id>`; ein anderer Admin kann das Konto im Client sperren (nicht entsperren).

**Was bleibt:** Während eine Admin-Sitzung offen ist, kann der Schadcode im Namen des Admins handeln, bis auf die Konsolen-Dinge oben. Liegt die Authenticator-App auf demselben PC, fängt er beide Faktoren. Deshalb gehört die App aufs Telefon (C11).

### C3.7 Böswilliger Server

Drei Fälle, mit verschiedener Antwort:

- **Ein fremder Server unter dem richtigen Namen** (DNS umgebogen, die alte Heim-IP gehört inzwischen einem anderen Telekom-Kunden): Der Client prüft den Schlüssel des Servers gegen die Pins im Serverprofil. Passt keiner, verbindet er nicht und zeigt "Server nicht vertrauenswürdig", ohne Ausnahmeknopf. Bei der ersten Verbindung versucht er vorher die übrigen Hosts des Profils; beim Wiederverbinden meidet er eine Adresse mit falschem Pin 60 Sekunden lang und meldet sich erst nach 2 Minuten ohne gültigen Pin (Protokoll 4.1, 7.1).
- **Jemand hat den privaten Serverschlüssel erbeutet:** Weil der Client nur den Schlüssel prüft (nicht Name, Kette oder Gültigkeit, Protokoll 3.1), besteht dieser Jemand die Pin-Prüfung, wenn er den Verkehr zu sich umlenken kann. Er kann aber weder die Pins noch die Hosts der Clients ändern, denn das Serverprofil ist offline unterschrieben; ein älteres, echt unterschriebenes Profil scheitert an der Versionsregel. Abhilfe ist ein neuer Serverschlüssel und ein neues Profil ohne den alten Pin (C7).
- **Der Betreiber des echten Servers ist böswillig, oder jemand ist dort root:** Dann hat er alles, was der Server hat. Er hört die Sprache (es gibt keine Ende-zu-Ende-Verschlüsselung), liest Konten und Journal mit Klarnamen, ist am Verwaltungssocket Eigentümer und kann den Eigentümer neu setzen. Der Dienstprozess hält den TOTP-Schlüssel im Speicher. Was er **nicht** kann: den Clients ein anderes Profil unterschieben (dafür braucht es den Profilschlüssel, der nie auf dem Server liegt), Programmcode auf die PCs der Mitglieder oder Admins bringen (es gibt keinen Weg, auf dem der Server Code liefert; der Verwaltungsteil kommt nur mit dem Admin-Setup), private Geräte- oder Admin-Schlüssel auslesen (der Server speichert nur deren öffentlichen Teil).

**Folge für den Betrieb:** Der Betreiber des Servers muss vertrauenswürdig sein; technisch ist root der Eigentümer. Das ist ein Grund, warum der Umzug zu einem anderen Betreiber erst nach Version 1 kommt und warum der Schutz des Hosts zum Sicherheitskonzept gehört (C16).

### C3.8 Mithören

| Wer hört mit | Was er bekommt | Warum |
| --- | --- | --- |
| Passiver Mithörer im WLAN oder beim Provider | nichts vom Inhalt; sieht, dass, wann und wie viel gesprochen wird | Steuerkanal in TLS, Sprache in AES-256-GCM. Paketgrößen, Sprechzeiten und Gegenstellen bleiben sichtbar (Protokoll 1.2). Bei TLS 1.2 (Windows 10) ist der öffentliche Geräteschlüssel lesbar; das Gerätezertifikat trägt deshalb keine persönlichen Angaben. |
| Aktiver Angreifer auf dem Weg, auch einer mit schnellerem Weg im selben WLAN | kann Pakete verwerfen, verzögern, kopieren, den Sprachpfad stören | Mitlesen oder Einsprechen geht nicht: Jedes Paket ist authentisiert, Wiederholungen fallen am Replay-Fenster heraus, und bevor der Server die Adresse einer Sitzung wechselt, prüft er den neuen Weg (C9). |
| Neugieriges Mitglied, auch mit verändertem Client | nur, was jedes Mitglied hören darf | Gehört wird nur, wo das Konto Mitglied ist; das erzwingt der Server je Empfänger, nicht der Client. Global hören alle, Durchsagen alle Mitglieder der Zielnetze. Die Sprechanzeige aller Netze gibt es nur mit dem Recht `SPRECHANZEIGE_ALLE`. |
| Rivale ohne Konto | nichts | Ohne freigegebenes Gerät gibt es keine Sprachschlüssel, und der Warteraum zeigt keine Netze, Konten oder Aufstellungen. |
| Wer den Server betreibt oder übernimmt | die ganze Sprache im Klartext | Der Server muss jedes Paket prüfen, stempeln und für jeden Empfänger neu verschlüsseln (Hop-by-Hop wie bei Mumble). |

### C3.9 Außerhalb des Modells

Geheimdienste, Angriffe auf Discord, auf den Anbieter der Authenticator-App oder auf Raid-Helper selbst, physischer Zugriff auf den Server und Hintertüren in .NET oder OpenSSL (Protokoll 1.4).

### C3.10 Missbrauch von Phasen und Funker-Modus (ab 0.5.0)

Zwei Dinge von 0.5.0 greifen in die Einteilung ein: Eine **Phase** stellt die Felder vieler Konten auf einmal um, und **"Heute als Funker"** macht einen Admin für einen Abend zu einem Funker, den die Einsatzleitung einteilen kann. Die Regeln dazu stehen in C13. Hier steht, wer damit etwas Unerlaubtes versuchen könnte und was der Server tut. Alle Zeilen der Tabelle stehen als Tests da (Standzeilen vom 01.10.2026, 20:15 und 23:05: `S2PhasenLageTests`, `S2FunkerLageTests`, `S2DrahtTests`, `SteuerBremseTests`, `Uebergang040Tests`).

| Versuch | Antwort des Servers |
| --- | --- |
| Phase schalten ohne `ZUWEISEN`: ein Funker, die Führung ohne Auftrag, ein Marine oder ein Admin im Funker-Modus | `KEIN_RECHT`, am Zustand ändert sich nichts. Ein unbekanntes Konto bekommt `UNBEKANNT`. |
| Phase schalten mit einem Auftrag, der gerade abgelaufen ist, noch bevor der Takt ihn abräumt | `KEIN_RECHT`: Ein Auftrag zählt bei jeder Handlung nur bis zu seinem `bis`. |
| Beauftragter gegen einen Admin: einen Admin verschieben, stummschalten oder mit einer Phase umstellen | Verschieben und Stummschalten ergeben `KEIN_RECHT`. Eine Phase lässt das Konto eines Admins, dessen Platz sie betrifft, aus; es steht in der Antwort unter `ausgelassen` und behält seine Felder. Das ist die Rangregel (C13): Admins teilt nur ein Admin ein, es sei denn, sie spielen "Heute als Funker". Stummschalten darf einen Admin auch dann nur ein Admin. |
| Phasenflut: viele Schaltnachrichten hintereinander | Die Bremse lässt 1 Nachricht je 2 Sekunden zu, mit Stoß 2 (C14.1). Der Rest bekommt `GRENZE`; im Test mit sechs Wechseln in kurzer Folge gingen die ersten zwei durch, der Rest bekam `GRENZE`, die Verbindung blieb bestehen (getrennt wird wie bei jeder Nachricht erst nach mehr als 50 Verstößen in 60 Sekunden, C8), und der Server stand auf der zuletzt bestätigten Phase. Ein weiterer Test schaltet 60-mal hintereinander ohne die Bremse direkt im Zustand: Danach haben alle Konten wieder dieselben Felder wie vorher, und der Zustand besteht seine Stimmigkeitsprüfung. |
| Zwei Leiter schalten gleichzeitig | Der zweite hat in `erwartet` eine Phase angegeben, die nicht mehr läuft: `VERALTET`, mit der laufenden Nummer und dem Konto, das sie geschaltet hat. Es wird nichts umgestellt. |
| Ein Funker oder ein Beauftragter schaltet "Heute als Funker" ein | `KEIN_RECHT`: Nur ein aktives Admin-Konto darf. |
| Ein Funker schreibt die Konto-Nummer eines Admins in die Nachricht | `KEIN_RECHT`. Die Nachricht hat gar kein Feld für ein fremdes Konto; sie wirkt immer auf den Absender, und ein zusätzliches Feld wird übergangen. Niemand setzt einen anderen in den Modus, verlängert ihn oder beendet ihn. |
| Viele Umschaltungen des Funker-Modus | Eigener Eimer, 1 je Sekunde, Stoß 2 (C14.1). |
| Ein Admin im Funker-Modus sperrt über sein Verwaltungsrecht die Selbstwahl | `KEIN_RECHT`: Eine Sperre setzt nur, wer `ZUWEISEN` hat (die Konsole ausgenommen). Sonst hätte der Takt sie gleich wieder aufgehoben. |
| Clients 0.5.0 an einem Server 0.4.0 | Sie schicken `0x003C` und `0x003D` gar nicht erst: Ein Server 0.4.0 trennte bei einer Nachricht, die er nicht kennt, mit `PROTOKOLL`. Der Client blendet Phasen und Funker-Modus dann aus. |

**Was bleibt:** Ein Beauftragter oder ein Admin mit `ZUWEISEN` kann die Flotte mit zwei Drücken umstellen, und das ist so gewollt; abgesichert ist, wer es darf und wen es trifft, nicht, ob die Phase richtig gewählt ist. Zwei Wege zurück gibt es von selbst: "Phase zurück", und 12 Stunden nach dem letzten Schalten fällt die Flotte auf die Grundstellung. Das Schalten schreibt der Server als Zeile ins Logbuch (Nummern vorher und nachher, wie viele umgestellt und wie viele ausgelassen wurden, wer es war nur als Kürzel, C15.2), nicht ins Journal; Phasen anlegen, ändern und löschen sind Verwaltungshandlungen und stehen im Journal, Funker-Modus ein und aus ebenfalls (C15.3). Nicht gemessen ist die Netzlast eines Phasenwechsels bei 40 Leuten (D3.5).

## C4 Grundsätze

Die Grundsätze kommen aus den Fehlern des früheren Funkprogramms und aus der Entscheidung für einen Eigenbau (Protokoll 1.5, 1.6):

| Früheres Funkprogramm | JG TacRelay | Begründung |
| --- | --- | --- |
| Passwörter im Klartext | keine Passwörter; jedes Gerät hat einen eigenen Schlüssel, im TPM nicht exportierbar | Was nicht existiert, kann nicht abgeschrieben, weitergesagt oder gestohlen werden. |
| öffentlich lesbarer Handshake-Token | kein gemeinsames Geheimnis, kein Inhaber-Token; Sprachschlüssel nur durch TLS, je Sitzung neu | Wer einen gemeinsamen Token kennt, ist drin; er lässt sich weitergeben und mitlesen. Die Sitzungs-ID ist bewusst öffentlich und ohne Schlüssel wertlos. |
| unsignierte Updates | Serverprofil nur offline signiert, mit steigender Version; Mindestversion vom Server erzwungen | Wer den Server übernimmt, soll die Clients nicht umleiten können. Signierte Updates selbst fehlen noch (C17). |
| offene Ports auf allen Schnittstellen | genau ein TCP- und ein UDP-Port, nur auf ausdrücklich konfigurierten Adressen | Eine leere Bindeliste startet den Server gar nicht; alle Adressen nur mit `alleAdressen` und Warnung. |
| Telemetrie | keine; Messwerte bleiben auf dem eigenen Server | Datensparsamkeit |

Dazu gelten diese Regeln:

- **Keine eigene Kryptografie.** Verwendet werden nur Bausteine von .NET: `SslStream`, `AesGcm`, `ECDsa`, `SHA256`, `HMACSHA256`, `HKDF`, `RandomNumberGenerator`, `CryptographicOperations.FixedTimeEquals` und `ZeroMemory`, dazu `HMACSHA1` ausschließlich für TOTP nach RFC 6238, weil jede Authenticator-App diese Standardwerte versteht. Die Kollisionsschwäche von SHA-1 betrifft HMAC nicht.
- **Eine einzige Ausnahme vom "kein gemeinsames Geheimnis":** das TOTP-Geheimnis der Admins. Es gilt nur für Admin-Konten, nur als zweiter Faktor neben dem Admin-Schlüssel und reicht nie allein; am Server liegt es nur verschlüsselt (Protokoll 1.1, Z5).
- **Der Server entscheidet.** Rechte, Stufen und Status prüft er bei jeder Handlung am aktuellen Zustand, nicht an einem Token; Änderungen wirken sofort. Das Senderecht prüft er für jedes Sprachpaket. Dass normale Mitglieder keine Verwaltungsmenüs sehen, ist Bequemlichkeit, kein Schutz: Wer den Client hat, kann ihn verändern und jede Nachricht schicken. Jede Sicherheitsaussage in diesem Teil gilt auch dann, wenn ein Angreifer das Admin-Setup hat und das Protokoll kennt.
- **Kein unsicherer Modus.** Es gibt keinen Knopf "trotzdem verbinden", keine Abschaltung der Prüfungen im Release, keinen Schalter, der Admin-Sitzung oder Zweitfaktor abschaltet, auch nicht für Tests. Im Release-Build lassen sich Serverprofil und Profilschlüssel weder in der Oberfläche noch in der Einstellungsdatei eingeben, und es gibt keinen Entwicklermodus, der das öffnet: Einen Schalter, den ein Betrüger einem Mitglied erklären kann, gibt es nicht. Der Testzugang ohne TLS existiert nur im Testwerkzeug `funk-testserver` und nur auf Loopback; der Dienst selbst hat ihn nicht.
- **Kleine Angriffsfläche.** Der Protokollkern ist eine eigene kleine Bibliothek ohne Abhängigkeiten. Parsen, Versiegeln, Replay-Fenster, Rechteprüfung und Empfängerbestimmung sind reine Funktionen, getrennt vom Netzwerk, damit sie sich testen und durchsehen lassen. Der Sprachpfad des Servers läuft in genau einem Thread; so gibt es keine Nebenläufigkeit auf Zählern und Schlüsseln.
- **Nur geheim, was geheim sein muss.** Kennungen, Pins, das Serverprofil und der öffentliche Profilschlüssel sind öffentlich und dürfen jeden Weg nehmen. Geheim sind nur die privaten Schlüssel, die Geheimnisse und die Codes in der Tabelle unten.

## C5 Schlüssel und Geheimnisse

### C5.1 Was geheim ist

| Was | Art und Ort | Erzeugt von | Darf nie | Wenn verloren oder erbeutet |
| --- | --- | --- | --- | --- |
| Geräteschlüssel | ECDSA P-256, je Client-Installation; im TPM, ohne TPM im Software-Schlüsselspeicher von Windows; nicht exportierbar; Name `JG-TacRelay-Geraet` | Client beim ersten Start | als Datei existieren; den PC verlassen (der Server kennt nur den öffentlichen Teil) | neuer Antrag, neue Freigabe; das alte Gerät entfernt oder sperrt ein Admin. Eine Wiederherstellung gibt es absichtlich nicht. |
| Admin-Schlüssel | ECDSA P-256 nur im TPM, ohne Rückfall auf Software; Name `JG-TacRelay-Admin`; höchstens 3 je Admin | Verwaltungsteil des Clients | als Datei existieren; zwei Konten gehören | Eigentümer trägt einen neuen ein (vorsehen, ernennen) und entfernt den alten. |
| Serverschlüssel mit Zertifikat | ECDSA P-256, selbstsigniert, 20 Jahre; `/etc/funk/credentials` (Ordner 0700, Dateien 0600), per systemd `LoadCredential` in den Dienst | `funk-admin schluessel-erzeugen` oder `funk-einrichten.sh` | in `server.json` stehen; in `funk-admin sicherung`; in eine Cloud oder einen Chat | Kopie auf dem Offline-Stick. Ohne ihn neuer Pin, also neues Profil. Erbeutet: neuer Schlüssel und neues Profil ohne den alten Pin. |
| Reserve-Serverschlüssel | ECDSA P-256, offline auf dem Stick; nur sein Pin steht im Profil | am Offline-Rechner | auf den Server, bevor er gebraucht wird | Er ist die Rückfallebene für einen Schlüsselwechsel ohne neues Setup. |
| Profilschlüssel | ECDSA P-256, `profil.key` auf dem Stick im Ordner `funk-offline` (ungeschütztes PEM, deshalb Stick mit BitLocker To Go) | Sarevoc am Offline-Rechner | auf den Server; weitergegeben werden, auch nicht zum Bauen der Setups (dafür reichen `profil.json` und der öffentliche Teil); in eine Cloud oder einen Chat | Ohne ihn kein neues Profil; ein neuer Profilschlüssel kommt nur mit einer neuen Programmfassung zu den Clients. |
| Betriebsgeheimnis `funk-geheimnis` | 32 Zufallsbytes in `/etc/funk/credentials`; daraus leitet der Server per HKDF-SHA256 je einen Schlüssel für die Schwärzung im Logbuch, die Vorrangspur und die Sperrliste ab | `funk-admin geheimnis-erzeugen` | in eine Sicherung; ins Umzugspaket | Mit einem neuen ändern sich die Kürzel im Logbuch, und die gespeicherten Werte von Vorrangspur und Sperrliste passen nicht mehr; beide fangen leer an. Weitere Folgen hat es nicht, denn der Server leitet nur diese drei Schlüssel daraus ab. |
| TOTP-Schlüssel `funk-totp-schluessel` | 32 Zufallsbytes in `/etc/funk/credentials`; verschlüsselt die TOTP-Geheimnisse und ergibt den Schlüssel für die Prüfwerte der Notfallcodes | `funk-admin totp-schluessel-erzeugen` | in eine Sicherung; auf den Stick (er bleibt im Container und reist nur verschlüsselt im Umzugspaket, außer mit `--ohne-totp`) | Alle Admins richten den Zweitfaktor an der Konsole neu ein. |
| TOTP-Geheimnis je Admin | 160 Bit; am Server nur verschlüsselt in `admins.json`, beim Admin in der Authenticator-App | Server bei der Ernennung oder mit `admin-einrichtung-neu` | im Logbuch, im Journal, in einer Fehlermeldung stehen; im Client gespeichert werden; als Bildschirmfoto existieren | neu nur an der Konsole; das alte und die alten Notfallcodes gelten dann nicht mehr. |
| Notfallcodes | 10 je Admin, je 60 Bit, jeder einmal; am Server nur als HMAC-SHA256 | Server, genau einmal angezeigt | neben dem Telefon liegen | neue nur an der Konsole (`notfallcodes --konto <id>`), die alten verfallen. |
| Sprachschlüssel | je Sitzung und Epoche zwei Schlüssel (32 Byte) und zwei IV (12 Byte), nur im Speicher | Server aus `RandomNumberGenerator`, an den Client nur durch TLS | geloggt oder gespeichert werden | Jede Wiederverbindung bringt neue. |
| Passphrase des Umzugspakets | 26 Zeichen Base32 (130 Bit) | `umzug-packen`, genau einmal angezeigt | aufgeschrieben, fotografiert oder auf dem Weg des Pakets geschickt werden (nur am Telefon) | Ohne sie ist das Paket wertlos. |

### C5.2 Was nicht geheim ist

Diese Dinge darfst du offen schicken, etwa über Discord; sie beweisen allein nichts:

- die **Gerätekennung** (Anzeigeform des Fingerabdrucks, `XXXX-XXXX-XXXX-XXXX`) und die **Admin-Kennung**,
- der **Pin** des Servers und seine Anzeigeform,
- das **Serverprofil** `profil.json` und der **öffentliche Profilschlüssel** (die lange Zeile, die mit `MFkw` beginnt),
- die **Anfragezeile** `TR-PROFILANFRAGE v1 ...` beim Einrichten eines neuen Servers,
- SC-Name und Discord-Name aus dem Setup (`anmeldung.json`).

Öffentlich heißt nicht beliebig: Ob eine Kennung oder eine Anfragezeile wirklich von dem kommt, der sie geschickt hat, klärt erst der Vergleich über einen zweiten Weg (Sprachkanal, Telefon). Darauf bauen Freigabe, Ernennung und Profil.

## C6 Geräteschlüssel

**Erzeugung.** Beim ersten Start legt der Client einen ECDSA-P-256-Schlüssel an, zuerst über den Microsoft Platform Crypto Provider im TPM 2.0, mit `ExportPolicy = None`, dauerhaft und im Benutzerkontext. Fehlt ein TPM oder scheitert das, nimmt er den Software-Schlüsselspeicher von Windows (Microsoft Software Key Storage Provider), ebenfalls nicht exportierbar, und meldet die Schlüsselart `SOFTWARE` (Protokoll 3.2).

**Gerätezertifikat.** Selbstsigniert, Subject `CN=Funk-Geraet` ohne persönliche Angaben, 16 Zufallsbytes Seriennummer, gültig 20 Jahre, Verwendung nur Client-Anmeldung. Ohne persönliche Angaben, weil das Zertifikat bei TLS 1.2 unverschlüsselt übertragen wird.

**Identität.** Der Server nimmt genau ein Blattzertifikat mit P-256-Schlüssel an; Kette und Gültigkeit zählen nicht. Den Besitz des Schlüssels beweist der TLS-Handshake. Die Identität des Geräts ist der Fingerabdruck, SHA-256 über den öffentlichen Schlüssel (SPKI); die Gerätekennung zeigt die ersten 8 Byte davon in Vierergruppen, etwa `5CD2-52FB-0CE8-9324`. TLS-Wiederaufnahme ist abgeschaltet: Jede Verbindung beweist den Schlüssel neu.

**Schlüsselart "laut Client, ungeprüft".** Ob der Schlüssel wirklich im TPM liegt, kann der Server ohne TPM-Attestation nicht prüfen; der Admin sieht die Angabe deshalb mit diesem Zusatz. Attestation ist frühestens für eine Version 2 vorgesehen.

**Warum TPM, und warum ein Rückfall.** Ein Schlüssel im TPM lässt sich nicht wegkopieren, auch nicht von einem Infostealer; benutzen lässt er sich auf dem PC trotzdem, solange dort Schadcode läuft. Der Rückfall auf Software ist nötig, weil manche PCs (vor allem mit Windows 10) kein nutzbares TPM haben; ein solcher Schlüssel ist für Schadcode des Benutzers kopierbar (Protokoll 1.4, T6). Für Admin-Schlüssel gibt es diesen Rückfall nicht.

**Kopierverdacht.** Je Konto gibt es höchstens eine Sitzung; eine neue verdrängt die alte. Verdrängen sich verschiedene Geräte desselben Kontos mehr als zweimal in 5 Minuten, geht `VERDRAENGUNG_HAEUFIG` an die Admins, und weitere verdrängende Verbindungen lehnt der Server 30 Minuten lang ab oder bis ein Admin `funk-admin verdraengung-freigeben --konto <id>` ausführt. Ein solches Hin und Her deutet auf einen kopierten Schlüssel.

**Lebenszyklus.**

| Vorgang | Wirkung |
| --- | --- |
| höchstens 3 Geräte je Konto in WARTET oder FREIGEGEBEN | darüber `GERAETE_LIMIT` |
| Gerät wartet ohne Entscheidung | nach 24 Stunden gelöscht |
| freigegebenes Gerät 90 Tage ohne Sitzung | fällt auf WARTET zurück und braucht eine neue Freigabe |
| Gerät entfernen (`funk-admin entfernen <kennung>`) | Status ENTFERNT, die Sitzung endet; der Schlüssel darf sich neu anmelden |
| Gerät dauerhaft sperren (`funk-admin sperren <kennung>`) | Status ENTZOGEN, der Fingerabdruck bleibt gesperrt; der Client bietet dann einen neuen Schlüssel an |
| Verlust, Windows neu | neuer Antrag, neue Freigabe; das alte Gerät entfernt oder sperrt ein Admin |

Ein Gerät gibt nie ein anderes Gerät desselben Kontos frei. So kann ein erschlichenes oder kopiertes Gerät weder weitere Geräte freigeben noch neue Geräte des Inhabers ablehnen (Protokoll 3.5).

## C7 Serverprofil, Pins und Profilschlüssel

### C7.1 Pin statt Zertifizierungsstelle

Der Server hat einen langlebigen Schlüssel (ECDSA P-256) mit selbstsigniertem Zertifikat. Der Client prüft ihn allein über Pins: SHA-256 über den öffentlichen Schlüssel des Serverzertifikats. Name, Kette und Gültigkeit prüft er nicht (Protokoll 3.1).

**Begründung:** Das ersetzt Let's Encrypt. Es braucht keinen Port 80, passt zu einer wechselnden Heim-IP mit DynDNS, und ein Umzug geht ohne Zertifizierungsstelle. Der Preis: Wer den privaten Serverschlüssel hat, besteht die Prüfung. Deshalb liegt der Schlüssel nur in `/etc/funk/credentials`, kommt per `LoadCredential` in den Dienst, steht nie in einer Konfigurationsdatei und nie in `funk-admin sicherung`, und deshalb steht im Profil immer ein Reserve-Pin für einen Wechsel.

### C7.2 Das signierte Serverprofil

Welche Server ein Client kennt, steht im Serverprofil, einem Objekt `{daten, signatur}`:

- `daten` nennt Name, Version (`profilVersion`), Ausstellung und Ablauf (`gueltigBis`), 1 bis 4 Hosts mit erlaubten Adressfamilien, den TCP-Port und 2 bis 3 Pins, nämlich den des laufenden Serverschlüssels und mindestens einen Reserve-Pin.
- `signatur` ist ECDSA P-256 mit SHA-256 über `"tacrelay profil v1"`, ein Nullbyte und `daten`, so wie es übertragen wird. Unterschrieben wird offline mit dem Profilschlüssel.
- Die öffentlichen Profilschlüssel sind fest in den Client eingebaut und wechseln nur mit einer neuen Programmfassung. Der Client nimmt mehrere eingebaute Profilschlüssel an; für einen späteren Wechsel des Profilschlüssels muss deshalb kein privater Schlüssel reisen.
- Ein Profil kennt nur einen TCP-Port für alle Hosts. Im Übergang eines Umzugs müssen deshalb beide Server unter demselben Port erreichbar sein (`UMZUG.md`).

### C7.3 Wann der Client ein Profil annimmt

- Das Profil kommt mit dem Setup (`profil.json` im Programmordner). Seit 0.2.0 gibt es im Client kein Feld mehr für Profil oder Profilschlüssel; so kann niemand einem Mitglied ein falsches Profil samt passendem Schlüssel unterschieben.
- Nach der Freigabe reicht der Server ein neueres Profil in `Welcome` durch. Der Client (ab 0.4.0) übernimmt es nur, wenn (1) die Signatur mit einem eingebauten Profilschlüssel gilt, (2) der Inhalt dem Schema entspricht, (3) `profilVersion` größer ist als die höchste bekannte und (4) der Pin darin steht, den genau dieser Server im Handshake gezeigt hat. Ältere, gleiche, unsignierte, fremd signierte und unlesbare Profile verwirft er und schreibt es in `serverprofil-protokoll.txt`.
- Regel (4) verhindert, dass ein echt unterschriebenes Profil, das aus Versehen auf dem falschen Server liegt (etwa das Abschlussprofil eines Umzugs auf dem alten Server), alle Clients aussperrt.
- Übernommen wird atomar nach `serverprofil.json` neben die Einstellungen. Bei jedem Gebrauch gilt das gültige Profil mit der höheren Version aus Setup und Ablage; wirksam wird es ab dem nächsten Verbindungsaufbau.
- Nach `gueltigBis` bleibt ein Profil in Gebrauch; der Client warnt nur.

### C7.4 Der Profilschlüssel bleibt offline

Der Profilschlüssel entsteht am Offline-Rechner und liegt mit dem Reserve-Serverschlüssel im Ordner `funk-offline` auf einem USB-Stick (`HEIMSERVER-PROXMOX.md`, Abschnitt 6). Der Ordner heißt bewusst anders als `linux` im Paket, damit er nie versehentlich mit den Programmen in den Container gepackt wird. `profil.key` liegt dort als ungeschützte Datei; den Stick deshalb mit BitLocker To Go verschlüsseln (`UMZUG.md`).

Ein neues Profil schreibt der Inhaber des Profilschlüssels mit `profil-erneuern` auf dem Stick (Doppelklick auf `Profil erneuern.cmd`). Anlässe sind ein neuer Host, ein neuer Pin (Umzug, Schlüsselwechsel) und eine Verlängerung. Beim Umzug schickt der neue Server eine Anfragezeile mit Name, Port und Pin. **Die Zeile darf jeden Weg nehmen, aber unterschrieben wird erst nach dem Vergleich am Telefon, und zwar von Kennung, Name und Port**: Die Kennung deckt nur den Pin, ein unterwegs geänderter Name schickte alle Clients zu einem fremden Rechner (`UMZUG.md`, Warnungen). Das Werkzeug fragt deshalb alle drei ab.

### C7.5 Was das Profil schützt und was nicht

- **Schützt:** Ein Server mit gültigem Pin, auch einer mit erbeutetem Serverschlüssel, kann nur durchreichen, was offline unterschrieben ist. Er kann den Clients weder neue Hosts noch neue Pins geben. Ein älteres echtes Profil (zurück auf alte Hosts oder Pins) scheitert an der Versionsregel.
- **Schützt nicht:** Wer auf dem PC eines Mitglieds Dateien ändern kann, kann die Ablage löschen oder gegen ein älteres echtes Profil tauschen, nie aber unter die Version des Setups. Gegen jemanden mit diesem Zugriff schützt das Profil ohnehin nicht.
- **Offen:** Wer den Profilschlüssel samt Reserve langfristig verwahrt, ist als Offener Punkt 21 des Protokolls notiert ("Pflicht vor v1").

## C8 Steuerkanal (TLS)

Steuerkanal heißt: Anmeldung, Rechte, Netze, Plätze, Verwaltung und die Ausgabe der Sprachschlüssel. Er läuft über TCP mit TLS (`SslStream`) und einem eigenen Rahmenformat, ohne HTTP und ohne WebSocket (Protokoll 4.1).

| Einstellung am Server | Wert | Warum |
| --- | --- | --- |
| Protokolle | TLS 1.3 und TLS 1.2; mit `nurTls13: true` nur TLS 1.3 | TLS 1.2 nur für Windows-10-Clients, deren Schannel kein TLS 1.3 kann (Offener Punkt 2: wer nutzt noch Windows 10) |
| Cipher-Suites (unter Linux) | TLS 1.3: `TLS_AES_256_GCM_SHA384`, `TLS_AES_128_GCM_SHA256`, `TLS_CHACHA20_POLY1305_SHA256`; TLS 1.2: nur `TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384` und `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256` | Vorgabe des Protokolls (4.1); die Liste wirkt nur unter Linux, und dort läuft der Server |
| Client-Zertifikat | Pflicht, nur P-256; Kette, Gültigkeit und Sperrlisten egal | Das Zertifikat trägt nur den Geräteschlüssel; die Identität ist sein Fingerabdruck. |
| ALPN | `tacrelay/1`, sonst Trennung | Fremde Protokolle fallen gleich heraus. |
| Neuverhandlung, Wiederaufnahme | beide aus | Jede Verbindung beweist den Geräteschlüssel neu. |
| Fristen | Handshake 5 s, danach `Hello` 3 s, Anmeldung 5 min, Warteraum 15 min | Halbe Verbindungen belegen keine Plätze. |
| Rahmengrößen | vor `Hello` höchstens 4096 Byte, im Warteraum 1032, angemeldet 1 MiB; Puffer erst nach der Längenprüfung | Kein Feld aus dem Netz bestimmt eine Puffergröße über die festen Grenzen hinaus. |
| JSON-Tiefe | höchstens 8 Ebenen, doppelte Schlüssel abgelehnt (`JsonVorpruefung.MaxTiefe`) | Ein zu tief verschachteltes Objekt ergibt `PROTOKOLL` und Trennung, ehe es ausgewertet wird. |
| Bindung | nur die Adressen aus `binden`, je Adresse TCP und UDP mit gleicher IP | keine offenen Ports auf allen Schnittstellen |

**Client.** Der Client lässt Windows die TLS-Fassung wählen (Windows 11: TLS 1.3, Windows 10: TLS 1.2), schickt den Host aus dem Profil als SNI, das Gerätezertifikat als Client-Zertifikat und prüft den Serverschlüssel gegen die Pins. Er sendet alle 2 Sekunden ein Ping; bleibt 6 Sekunden lang jede Antwort aus, gilt die Verbindung als tot. Der Server trennt nach 10 Sekunden ohne jeden Rahmen.

**Unbekannt und bekannt.** Direkt nach dem Handshake stuft der Server die Verbindung über den Fingerabdruck ein: freigegebenes Gerät mit aktivem Konto heißt "bekannt", alles andere "unbekannt". Nur unbekannte Verbindungen belegen die knappen Plätze für Unfreigegebene (2 je Adresse, 20 serverweit).

**Fehlercodes ohne Auskunft.** Einen falschen, abgelaufenen oder verbrauchten Einmalcode unterscheidet der Server nicht (`CODE_UNGUELTIG`). Ein Warteraum verrät nichts über Netze, Konten und Aufstellung.

**Verstöße.** Eine Ratengrenze ergibt `GRENZE` und verwirft die Nachricht. Mehr als 50 Verstöße in 60 Sekunden trennen die Verbindung, und dieses Gerät (der Fingerabdruck, nicht die IP) ist 60 Sekunden gesperrt; andere Konten hinter derselben IP trifft das nicht.

**Neue Nachrichten ab 0.5.0.** Drei Nachrichten sind dazugekommen: `0x003C` Funkermodus ("Heute als Funker", Anfrage des Admin-Kontos an den Server), `0x003D` PhaseSchalten (Anfrage der Einsatzleitung) und `0x4004` PhaseGeschaltet (Push des Servers an alle angemeldeten Sitzungen, bei jedem Wechsel und nach `Welcome`, sobald die Flotte Phasen hat). Für sie gilt dasselbe Rahmenwerk wie für alle: dieselben Längen- und Tiefenprüfungen, `Fehler{UNGUELTIG}` ohne Trennung bei einem Inhalt, der nicht passt, und eigene Töpfe (Funkermodus 1 je Sekunde, PhaseSchalten 1 je 2 Sekunden, je Stoß 2; C14.1). Der Push liegt im optionalen Nummernbereich, damit ein Client 0.4.x ihn übergeht; die beiden Anfragen schickt ein Client nur an einen Server ab 0.5.0, weil ein älterer bei einer unbekannten Pflichtnachricht mit `PROTOKOLL` trennt (`Versionen.ServerKennt`). Die Phasen reisen in `NetzListe` und in `Verwaltung` (`PHASE_SETZEN`, `PHASE_LOESCHEN`); eine Phase steht dort **flach** als `phasen[].felder[{rolleId, ...}]`, weil die verschachtelte Form im Paket auf Tiefe 9 gelegen hätte, über der Grenze von 8 (Standzeile 01.10.2026, 20:15: der Server trennte das Paket mit `PROTOKOLL`, und nur der Test über den Draht zeigte es). Im ungünstigsten Fall wächst die `NetzListe` mit 5 Phasen und 512 Variantenfeldern auf 323 KB (gemessen, 01.10.2026; Protokoll 4.10), das sind 66 KB mehr für die Phasen und der Faktor 3,25 unter der Rahmengrenze von 1 MiB.

## C9 Sprachkanal

### C9.1 Verfahren

Die Sprache läuft über UDP, in Ausnahmefällen im Steuerkanal getunnelt (Tunnelmodus, gleiche Schlüssel, gleicher Zähler, gleiches Replay-Fenster).

| Teil | Festlegung (Protokoll 5.2, 5.7) |
| --- | --- |
| Verfahren | AEAD_AES_256_GCM (`AesGcm`): Schlüssel 32 Byte, Nonce 12 Byte, Tag 16 Byte |
| Nonce | nach RFC 8446, Abschnitt 5.3: der 64-Bit-Zähler, links mit Nullen auf 12 Byte aufgefüllt, mit dem festen IV der Richtung und Epoche XOR-verknüpft |
| Richtungen | getrennte Schlüssel und IV für hoch (Client zum Server) und runter (Server zum Client) |
| Zähler | je Sitzung und Richtung ein streng steigender 64-Bit-Wert, beginnt bei 0, läuft über alle Epochen, UDP und Tunnel weiter, wird nie zurückgesetzt und nie wiederverwendet; jedes Paket jedes Typs verbraucht einen Wert |
| Zusatzdaten (AAD) | die 16 Kopfbytes: Version, Typ, Epoche, ein reserviertes Byte, Sitzungs-ID, Zähler |
| Sitzungs-ID | öffentlich, dient nur der Zuordnung, nie der Authentisierung |
| Größe | höchstens 1200 Byte je Datagramm, tatsächlich höchstens 569 Byte hoch und 477 runter; Opus-Nutzlast höchstens 400 Byte |

**Hop-by-Hop.** Der Server entschlüsselt jedes Paket, prüft das Senderecht, stempelt Absender, Art und Netz für jeden Empfänger und verschlüsselt neu mit dessen Schlüssel. Absender und Netz kann ein Sender deshalb nicht fälschen (G4). Die Kehrseite: Der Server sieht die Opus-Nutzlast im Klartext; eine Ende-zu-Ende-Verschlüsselung gibt es nicht (Protokoll 1.2).

**Doppelte Sicherung gegen eine wiederholte Nonce:** Die Schlüssel sind je Epoche zufällig, und der Zähler wiederholt sich nie. Vergeben wird er in genau einem Thread (am Server im Relay-Thread, am Client im Sende-Thread).

### C9.2 Schlüssel und Schlüsselwechsel

Das Schlüsselmaterial einer Epoche (zwei Schlüssel, zwei IV) kommt aus `RandomNumberGenerator` im Server. Es wird nicht abgeleitet, nie wiederverwendet und nur über den Steuerkanal übertragen, binär in der Nachricht `VoiceKeys`, nie in JSON. Eigene Puffer werden nach Gebrauch genullt, Schlüssel nie geloggt. Jede Wiederverbindung ist eine neue Sitzung mit neuen Schlüsseln; es gibt keine Wiederaufnahme und keinen gespeicherten Zähler. Eine Sitzungs-ID vergibt der Server 24 Stunden lang nicht neu.

| Auslöser | Wirkung (Protokoll 5.8, `Rotationsgrenzen.cs`) |
| --- | --- |
| 60 Minuten seit Beginn der Epoche | Server wechselt den Schlüssel |
| 2^20 Pakete in einer Richtung und Epoche | Server wechselt den Schlüssel |
| 2^21 gesendete Pakete hoch | Client fordert einen Wechsel an (`KeyRotation`) |
| 2^22 Pakete je Richtung und Epoche | harte Grenze: der Sender sendet in dieser Epoche nichts mehr; nach 5 Sekunden beginnt der Client eine neue Sitzung |
| mehr als 2^16 gescheiterte Entschlüsselungen je Epoche | Server erzwingt einen Wechsel und schreibt "Angriffsverdacht" ins Logbuch |

**Begründung der Grenzen:** Die Vertraulichkeitsgrenze von AES-GCM liegt bei 2^23 Paketen je Schlüssel (RFC 9001, Abschnitt 6.6). Mit diesen Grenzen wird sie nie erreicht.

**Ablauf ohne Lücke** (make before break): Der Server schickt die Schlüssel der nächsten Epoche und nimmt ab sofort beide an; der Client sendet in der neuen und antwortet; dann sendet auch der Server in der neuen und verwirft die alte 10 Sekunden später. Antwortet der Client nicht binnen 10 Sekunden, trennt der Server, und der Client verbindet neu.

### C9.3 Replay-Schutz und Empfangsprüfung

Ein Replay-Fenster von 2048 Paketen je Sitzung und Richtung, gemeinsam für alle Epochen und für UDP und Tunnel, verwirft jedes Paket, dessen Zähler schon angenommen war oder zu weit zurückliegt. Gesetzt wird das Fenster erst, nachdem das Paket seinen Tag bestanden hat; ein gefälschtes Paket kann es also nicht verschieben (Vorbild WireGuard).

Der Server prüft jedes Datagramm in fester Reihenfolge, die billigen Prüfungen zuerst (Protokoll 5.9):

1. Alter: Sprache, die länger als 60 ms zwischen Empfang und Verarbeitung lag, fällt heraus.
2. Länge: 41 bis 1200 Byte, abgeschnittene Datagramme fallen heraus.
3. Kopf: Version, Typ in der richtigen Richtung, reserviertes Byte 0, Länge passend zum Typ.
4. Sitzung zur Sitzungs-ID vorhanden und aktiv.
5. Budget je Herkunft (gebundene Adresse, fremde Adresse, Tunnel).
6. Epoche angenommen.
7. Replay-Vorprüfung, nur lesend.
8. Entschlüsseln; bei falschem Tag verwerfen und zählen.
9. Replay-Fenster setzen, maßgeblich.
10. Adressbehandlung, dann Verarbeitung nach Typ.

Auf ein verworfenes Datagramm antwortet der Server nie. Bis Schritt 8 hat er für einen Angreifer ohne Schlüssel kaum Arbeit; vor dem Entschlüsseln fordert der Pfad keinen Speicher an.

### C9.4 Adressbindung und Pfadprüfung

Je Sitzung gibt es eine gebundene Adresse; nur dorthin geht die Sprache runter. Kommt ein gültiges Paket von einer anderen Adresse (NAT hat den Port gewechselt, WLAN-Wechsel), prüft der Server den neuen Weg, bevor er umbindet (Protokoll 5.10):

- Er schickt eine Prüfung mit 16 Zufallsbytes an die neue Adresse (3 Sekunden gültig) und zugleich eine Gegenprobe an die bisherige.
- Umgebunden wird erst, wenn die neue Adresse richtig geantwortet hat, von der alten seit 500 ms nichts Echtes mehr kam und kein Kopierverdacht besteht.
- **Kopierprüfung:** Kommt ein echtes Paket, das schon von der neuen Adresse kam, auch noch von der alten, hat jemand echte Pakete kopiert und schneller zugestellt. Dann verwirft der Server den Kandidaten, sperrt die fremde Adresse 60 Sekunden für diese Sitzung und meldet `KOPIERVERDACHT`.
- **Keine Verstärkung:** Eine Prüfung ist 48 Byte groß, nie größer als das auslösende Paket, höchstens 4 je Sekunde. Ein blinder Angreifer kann weder umleiten noch eine Prüfung auslösen, denn dafür braucht er ein echtes Paket.

**Was bleibt:** Ein Beobachter mit schnellerem Weg, der echte Pakete kopiert und zugleich die Originale unterdrückt, kann den Sprachpfad stören. Mitlesen oder einsprechen kann er nicht.

### C9.5 Prüfung der Sprachdaten

Eine bösartige Opus-Nutzlast eines veränderten Senders würde bei allen Hörern im Decoder libopus landen (das Protokoll nennt als Stichwort CVE-2025-71264). Deshalb:

- Der **Server** prüft vor dem Weiterleiten ohne libopus aus dem ersten Byte (TOC), dass das Paket genau 20 ms Mono ergibt, und dass es höchstens 400 Byte hat; alles andere verwirft er.
- Der **Client** prüft jedes empfangene Opus-Paket noch einmal mit libopus auf genau 960 Samples bei 48 kHz und verwirft sonst.
- libopus bleibt auf eine bekannte Fassung festgelegt und wird gepflegt.

## C10 Anmeldung: Antrag und Freigabe

### C10.1 Ablauf

1. **Setup.** Die Seite "Deine Namen" fragt den Namen in Star Citizen (etwa `Mustermann`) und den Discord-Namen (etwa `Mustermann/Max`). Beide landen in `anmeldung.json` unter `%LOCALAPPDATA%\JG-TacRelay`; geheim ist daran nichts. Ein Passwort gibt es nicht.
2. **Erster Start.** Der Client fragt "stimmt das?", erzeugt den Geräteschlüssel, verbindet sich mit dem Server aus dem eingebauten Profil und stellt einen Antrag mit beiden Namen.
3. **Warteraum.** Der Client zeigt groß die Gerätekennung mit dem Satz: "Schick dem Admin in Discord deinen SC-Namen und diese Kennung. Die Kennung ist kein Geheimnis. Nach Codes oder Passwörtern fragt dich niemand."
4. **Freigabe.** Der Admin sieht den Antrag mit SC-Name, Discord-Name, Gerätekennung, Schlüsselart ("laut Client, ungeprüft") und Uhrzeit, vergleicht die Kennung mit der aus der Nachricht, prüft, dass die Nachricht wirklich vom Konto des Mitglieds kommt, sieht passende Vormerkungen und gibt frei: im Verwaltungsteil (Admin-Sitzung, frischer Code) oder an der Konsole.
5. **Fertig.** Der wartende Client geht ohne Neustart in den Funk; vorgemerkte Rollen gelten sofort.

An der Konsole:

```
funk-admin antraege
funk-admin freigeben <kennung>
funk-admin ablehnen <kennung>
```

`antraege` zeigt offene Anträge mit SC-Name, Discord-Name, Gerätekennung, passenden Vormerkungen und vergebenen Namen. Mit `freigeben <kennung> --vormerkung <id>` übernimmt die Freigabe eine Vormerkung.

### C10.2 Was ein Antrag erreicht und was nicht

- **Nichts geht ohne Admin.** Ein Antrag führt nie von selbst in den Funk, und kein Weg führt vom Antrag oder Code direkt zur Freigabe.
- **Der Warteraum verrät nichts** über Netze, Konten und Aufstellung. Er nimmt nur Ping und Abmeldung an, Rahmen bis 1032 Byte, und schickt die Verbindung nach 15 Minuten weg (sie kommt nach 60 Sekunden wieder).
- **Namen sind Angaben, keine Beweise.** Jeder kann "Mustermann" eintippen. Ein offener Antrag besetzt deshalb keinen Namen; in den Namensraum kommen SC-Name und Discord-Name erst mit der Freigabe. Sonst könnte ein Fremder mit einem Antrag einen Namen 24 Stunden lang blockieren. Bei der Freigabe kommen nur freie SC-Namen ins Konto; der Discord-Name muss frei sein, sonst lehnt der Server mit `NAME_VERGEBEN` ab und sagt, wem er gehört (Anf. A53).
- **Eindeutige, ehrliche Namen.** SC-Namen und Discord-Namen sind serverweit eindeutig nach Unicode-Normalform NFKC und ohne Unterschied von Groß und Klein. Unsichtbare und täuschende Zeichen sind verboten (unter anderem alle Default-Ignorable-Zeichen, Steuer- und Richtungszeichen, Hangul-Füller, leeres Braillemuster). Den Discord-Namen ändert nur ein Admin oder die Konsole, nie das Mitglied selbst; "Wer ist da" zeigt daneben den eindeutigen SC-Hauptnamen.
- **Spam im Warteraum** bremsen die Grenzen (2 unfreigegebene Verbindungen je Adresse, 20 serverweit, 10 Anträge je Adresse und Stunde) und höchstens 30 offene Anträge; darüber `ANTRAEGE_VOLL` und eine Meldung an jedes Admin-Konto. Wartende Geräte verfallen nach 24 Stunden.
- **Ablehnen sperrt nicht** (seit 0.7.0; Sarevoc, 04.10.2026: Löschen heißt vollständig entfernen, und keine Fehlbedienung darf jemanden aussperren). Ablehnen und Löschen entfernen alles, was der Server über den PC weiß; er darf neu beantragen, und jeder Antrag braucht wieder eine Freigabe. Eine Sperre auf den Fingerabdruck half ohnehin nicht gegen einen entschlossenen Angreifer, denn einen neuen Geräteschlüssel bekommt er mit einer Neuinstallation oder einem veränderten Client schnell; sie traf vor allem Ehrliche nach einer Fehlbedienung, so am 04.10.2026 ein Testgerät. Wer dauerhaft draußen bleiben soll, den sperrt ein Admin bewusst an der Konsole (`sperren`). Gegen viele Anträge helfen die Grenzen oben und "Alle ablehnen, die zu keiner Vormerkung passen"; der Client stellt nach einer Ablehnung nicht von selbst einen neuen Antrag.

### C10.3 Warum die Gerätekennung

Die Gerätekennung ist der Fingerabdruck des Schlüssels, dessen Besitz der TLS-Handshake schon bewiesen hat. Kommt sie über das Discord-Konto des Mitglieds beim Admin an und stimmt mit dem Antrag überein, gehört das Gerät dem, der geschrieben hat. Sie darf warten, bis der Admin Zeit hat. Ein Einmalcode dagegen müsste ohne Bot binnen 5 Minuten bei offener Verbindung eingetragen werden; das scheitert im Alltag.

### C10.4 Regeln für Admins beim Freigeben

Der Kennungsvergleich ist nur so gut wie die Frage, wer die Kennung geschickt hat. Anzeigenamen in Discord sind nicht eindeutig; jeder kann sich ein Konto mit dem Anzeigenamen "Mustermann/Max" anlegen. Deshalb:

- Die Kennung zählt nur, wenn sie vom bekannten Discord-Konto kommt. Den eindeutigen Benutzernamen prüfen (Profil anklicken), nicht nur den Anzeigenamen. Am sichersten schreibt der Admin selbst zuerst über die Mitgliederliste des Discords, oder er lässt sich die Kennung im Sprachkanal vorlesen.
- Keine Kennung "für einen Freund" freigeben. Jeder schickt seine eigene.
- Zeitdruck ("der Raid fängt gleich an") ist ein Warnzeichen, kein Grund. Im Zweifel ablehnen; ein Antrag lässt sich jederzeit neu stellen.
- Ein PC, ein Konto (seit 0.7.0): Ein weiterer PC eines Mitglieds bekommt sein eigenes Konto. An ein bestehendes Konto hängt nur die Konsole ein Gerät, etwa den PC eines Admins mit neuem Geräteschlüssel, und nur, wenn der Inhaber es selbst bestätigt hat, am besten im Sprachkanal (`freigeben <kennung> --an-konto <id>`). Geräte privilegierter Konten gibt nur die Konsole frei.

### C10.5 Später mit Bot: Einmalcode

Sobald es den Discord-Bot gibt, bleibt der Einmalcode der Hauptweg, weil er die Discord-ID bindet, die der Abgleich mit der Gilde braucht (Protokoll 3.4, 3.7). Die Richtung ist bewusst so gewählt: Der Client zeigt den Code, der Nutzer tippt ihn in Discord. 12 Zeichen, 60 Bit Zufall, 5 Minuten, einmalig, an Verbindung und Gerät gebunden, am Server nur als SHA-256-Wert. Heute nehmen diesen Weg nur Programme ohne Namen (der Echo-Funker und das Testwerkzeug), mit `funk-admin verknuepfen` an der Konsole; JG TacRelay selbst stellt seit 0.2.0 immer einen Antrag.

## C11 Admin-Nachweis

### C11.1 Warum ein eigener Nachweis

Ein gestohlener Geräteschlüssel eines Admins soll keine Verwaltung sein (Protokoll 1.4, T11). Deshalb wirken die drei Verwaltungsrechte `GERAETE_FREIGEBEN`, `KONTEN_VERWALTEN` und `NETZE_VERWALTEN` nur in einer **Admin-Sitzung**: Die Sitzung eines Admin-Kontos weist sich zusätzlich mit einem eigenen Admin-Schlüssel und einem Code aus der App aus. In jeder anderen Sitzung hat ein Admin-Konto nur die Rechte der Führung und `ZUWEISEN` (im Funker-Modus, C13, auch die nicht); eine Verwaltungsanfrage ergibt dort `ADMIN_SITZUNG_NOETIG`, ohne Wirkung.

Die Vorgabe war "höchster Standard" mit einem Zweitfaktor ausschließlich von Proton (Anf. A44); seit 0.6.4 (Sarevoc, 04.10.2026) geht jede Authenticator-App nach RFC 6238, und welche, entscheidet jeder Admin selbst. Das Programm nennt deshalb keine. Der Maßstab für Anmeldungen mit hohem Wert ist ein Schlüssel in Hardware, den der Mensch davor bei jeder Unterschrift bestätigt (FIDO2, Passkey). Mit einer Authenticator-App als einzigem Zweitfaktor baut JG TacRelay den Hardware-Teil selbst:

| Baustein | Aufgabe | entspricht |
| --- | --- | --- |
| Admin-Schlüssel, ECDSA P-256 im TPM, nicht exportierbar | Besitz: nur dieser PC kann die Admin-Anmeldung unterschreiben | dem Hardware-Schlüssel |
| Code aus der Authenticator-App (TOTP nach RFC 6238) | zweiter, getrennter Faktor, am besten auf dem Telefon | dem Zweitfaktor |
| gepinnter TLS-Kanal, Unterschrift an Sitzung, Gerät, Nonce und Zweck gebunden | kein Umleiten auf eine falsche Gegenstelle; ein Code nützt nur in genau dieser Sitzung | der Phishing-Festigkeit |

**Was gegenüber FIDO2 fehlt, ehrlich gesagt:** (1) die Bestätigung durch den Menschen bei jeder Unterschrift, denn Schadcode auf dem Admin-PC kann den TPM-Schlüssel still benutzen (er braucht dann aber noch den aktuellen Code vom Telefon); (2) der Nachweis, dass der Schlüssel wirklich in Hardware liegt (Attestation), denn ein gefälschtes Admin-Setup könnte einen kopierbaren Schlüssel anlegen, ohne dass der Server es merkt. "Höchster Standard" heißt hier also: das Beste, was mit einer Authenticator-App als einzigem Zweitfaktor geht.

### C11.2 Voraussetzungen am Admin-PC (ab 0.4.1)

Festgelegt am 01.10.2026 (Anf. A56), umgesetzt im Client 0.4.1. Die Vorprüfung im Verwaltungsteil zeigt die drei
Voraussetzungen unter diesen Überschriften (`Voraussetzungen.cs`, `Voraussetzungstexte`):

| Zeile im Client | Art | Begründung |
| --- | --- | --- |
| TPM 2.0 (technisch Pflicht) | Pflicht, technisch erzwungen | Der Admin-Schlüssel entsteht nur im TPM, nicht exportierbar, und wird nie als Datei gespeichert oder kopiert. Ohne nutzbares TPM 2.0 bleibt der Knopf zum Anlegen aus, und es gibt auf diesem PC keine Verwaltung; einen Rückfall auf Software gibt es mit Absicht nicht. |
| Windows-Konto mit PIN oder Kennwort (Richtlinie) | Richtlinie der Gilde, keine technische Prüfung | Der TPM-Schlüssel lässt sich nicht wegkopieren, aber auf demselben PC benutzen. Wer am PC sitzt, soll dafür die Windows-Anmeldung brauchen. Ein Microsoft-Konto ist nicht nötig. |
| BitLocker oder Geräteverschlüsselung (empfohlen) | empfohlen, keine Pflicht | Schützt die Daten bei Zugriff auf die ausgebaute oder fremd gestartete Platte. Fehlt sie, warnt der Client wörtlich: "BitLocker/Geräteverschlüsselung wird empfohlen, um Daten bei Offline-Zugriff auf die Festplatte zu schützen." |

Wie die Vorprüfung arbeitet, steht in B10.6: Sie liest nur, legt keinen Schlüssel an und braucht keine Adminrechte. Der
Server ändert sich dafür nicht. Bis 0.4.0 nannte der Verwaltungsteil BitLocker noch als Pflicht; in 0.4.1 ist der Satz
fort.

Eine Grenze der BitLocker-Zeile: Windows nennt die Werte der Eigenschaft, die der Client liest, nicht öffentlich; die
Deutung folgt verbreiteten Beobachtungen und zählt "angehalten" und "wartet auf Aktivierung" als aus. Laut Microsoft
läuft die automatische Geräteverschlüsselung bis zum Schutz durch das TPM mit offenem Schlüssel, und mit nur lokalen
Konten bleibt das so; ein solcher PC bekommt die Warnung zu Recht (Kommentar in `Voraussetzungen.cs`, `PROJEKT.md`,
Standzeile 01.10.2026 18:30). An einem PC mit Geräteverschlüsselung ist das nicht gemessen.

**Restrisiko ohne Plattenverschlüsselung** (Einschätzung aus dem Entwurf, nicht gemessen): Ein Dieb des PCs kommt unter Umständen an den gespeicherten Schlüsselblock und kann ihn mit demselben TPM in demselben PC weiter benutzen. Für eine Admin-Sitzung braucht er trotzdem noch den Code vom Telefon. Deshalb: Bei Diebstahl sofort sperren (C19).

**Authenticator-App.** Empfohlen ist die App auf dem **Telefon**, nicht auf dem Admin-PC: Liegen Admin-Schlüssel und Code auf demselben Rechner, fängt Schadcode dort beide. Die Einrichtung auf dem Windows-PC bleibt möglich, mit deutlichem Hinweis (Antwort 2 vom 30.09.2026). Synchronisiert die App über ein Konto beim Anbieter, dessen Wiederherstellungscodes getrennt aufheben; ohne Synchronisierung die Sicherung der App nutzen, falls sie eine hat. Welche App, ist seit 0.6.4 frei (Sarevoc, 04.10.2026): Der Server sieht ohnehin nur sechs Ziffern und kann nicht erkennen, welche App sie errechnet hat. Das ist tragbar, weil der Code nie allein reicht.

### C11.3 Admin-Schlüssel

- Eigener Schlüssel neben dem Geräteschlüssel, Name `JG-TacRelay-Admin`, ECDSA P-256, erzeugt nur vom Verwaltungsteil über den Microsoft Platform Crypto Provider, nicht exportierbar, nur zum Unterschreiben, im Benutzerkontext. Höchstens 3 je Admin (etwa PC und Laptop), derselbe Schlüssel gehört nie zwei Konten. Der Server speichert nur den öffentlichen Teil; die Admin-Kennung ist dessen Anzeigeform.
- **Warum ein zweiter Schlüssel und nicht der Geräteschlüssel:** Der Geräteschlüssel entsteht über den offenen Anmeldeweg, den jeder mit dem Installer erreicht, und wird bei jedem Verbinden ohne Zutun benutzt. Der Admin-Schlüssel entsteht nur nach einer Entscheidung des Eigentümers und kommt nur an der Konsole ins System. So führt der offene Weg nie zu Admin-Rechten, auch nicht über einen Fehler im Antragsweg. Und er lässt sich allein entziehen: Wer nicht mehr Admin ist, funkt mit seinem Gerät weiter.
- **Unterschrift** (Protokoll 3.9): ECDSA P-256 mit SHA-256 über `"tacrelay admin v1"`, ein Nullbyte, die Sitzungs-ID, den Fingerabdruck des Geräts, eine Nonce des Servers (32 Byte) und den Zweck (`ANTRAG`, `GEHEIMNIS`, `EINRICHTUNG` oder `ANMELDUNG`). Die Nonce gilt 60 Sekunden und genau einmal. So gilt eine Unterschrift weder in einer anderen Sitzung noch auf einem anderen Gerät, noch später, noch für einen anderen Zweck. Ein Code, den ein Betrüger dem Admin abschwatzt ("lies mir mal vor"), nützt ohne den Admin-Schlüssel nichts.

### C11.4 Zweitfaktor (TOTP)

- RFC 6238 mit den Standardwerten, die jede App versteht: HMAC-SHA-1, 6 Ziffern, 30 Sekunden, Geheimnis 160 Bit aus `RandomNumberGenerator`.
- Angenommen wird der aktuelle Zeitschritt, dann der vorige und der nächste, aber nur ein Schritt **nach** dem zuletzt angenommenen. Damit gilt jeder Code genau einmal, und ein älterer, mitgelesener Code gilt nicht mehr, sobald ein neuerer benutzt wurde. Verglichen wird in konstanter Zeit.
- Passt dreimal in Folge nur ein Nachbarschritt, sagt der Server "Uhr des Telefons prüfen".
- **Speicherung am Server** in `admins.json` (nur für den Dienstbenutzer lesbar): das Geheimnis nur verschlüsselt mit AES-256-GCM und dem TOTP-Schlüssel, je Verschlüsselung eine neue Nonce, als Zusatzdaten `"tacrelay totp v1"`, Konto-Nummer und eine Generation, die jedes neue Geheimnis des Kontos hochzählt. So lässt sich kein Chiffrat auf ein anderes Konto oder einen alten Stand umhängen. Entschlüsselt wird nur für die Dauer einer Prüfung; der Puffer wird danach genullt.
- **Uhr:** TOTP verträgt nur einen Zeitschritt Abweichung. Am Host muss die Zeitsynchronisierung laufen (C16).

### C11.5 Notfallcodes

10 je Admin, Form `XXXX-XXXX-XXXX`, je einmal gültig, statt eines Codes aus der App. Gespeichert nur als HMAC-SHA256 mit einem Schlüssel, der aus dem TOTP-Schlüssel abgeleitet und an die Konto-Nummer gebunden ist. Jede Benutzung geht als Meldung an jedes Admin-Konto. Aufbewahren getrennt vom Telefon: ausgedruckt an einem sicheren Ort oder als sichere Notiz in einem Passwortmanager. Neue gibt es nur an der Konsole (`funk-admin notfallcodes --konto <id>`); die alten verfallen dann.

### C11.6 Fehlversuche

- Ein Fehlversuch zählt nur mit gültiger Unterschrift des Admin-Schlüssels (Anmeldung) oder in einer offenen Admin-Sitzung (Bestätigung). Wer den Schlüssel nicht hat, kann keinen Code ausprobieren und keinen Admin aussperren.
- 5 Fehlversuche in 15 Minuten ergeben 15 Minuten Pause, 10 in 24 Stunden sperren den Zweitfaktor, bis der Eigentümer an der Konsole entsperrt (`funk-admin admin-entsperren --konto <id>`). Beides meldet `ZWEITFAKTOR_GESPERRT` und geht als Meldung an jedes Admin-Konto. Ein Erfolg setzt die Zähler nicht zurück. Notfallcodes zählen in dieselben Töpfe.
- Admin-Nachrichten haben zusätzlich eine Ratengrenze von 1 je Sekunde (Stoß 3), die Nonce einen eigenen Topf derselben Größe.

### C11.7 Admin-Sitzung

- **Öffnen:** Im Client unter "Verwaltung" den Code eingeben; der Client holt eine Nonce, unterschreibt mit dem Admin-Schlüssel und schickt Unterschrift und Code (oder einen Notfallcode). Voraussetzungen am Server: Konto ADMIN und aktiv, Gerät freigegeben, Admin-Nachweis aktiv.
- **Ende:** nach 12 Stunden (seit 0.9.1 nicht mehr nach 30 Minuten ohne Verwaltungshandlung, Q54), mit der Verbindung, mit "Verwaltung schließen" und mit jedem Neustart des Servers. Sofort bei Sperre oder Löschung des Kontos, Sperre oder Entfernung des Geräts, Verlust der Stufe ADMIN, Sperre, Entzug oder Zurücksetzen des Admin-Nachweises, neuem Zweitfaktor und entferntem Schlüssel. Endet nur die Admin-Sitzung (Leerlauf, Höchstdauer, "Verwaltung schließen", Sperre des Admin-Nachweises), läuft der Funk weiter.
- **Neues Gerät:** Geht eine Admin-Sitzung von einem Gerät aus, das noch nie eine hatte, bekommt jedes Admin-Konto die Meldung `ADMIN_SITZUNG` mit dem Gerät.

### C11.8 Schritt-Bestätigung

Heikle Handlungen verlangen einen Code, der höchstens 5 Minuten alt ist: die Anmeldung selbst oder eine Bestätigung in der offenen Admin-Sitzung. Fehlt er, antwortet der Server `SCHRITT_BESTAETIGEN`, der Client fragt den Code und schickt die Handlung noch einmal. Innerhalb der 5 Minuten gehen weitere heikle Handlungen ohne neuen Code, damit ein Admin vor dem Raid zehn Geräte freigeben kann, ohne zehnmal zu tippen. Was ohnehin verboten ist, fragt gar nicht erst nach einem Code.

| Handlung | braucht |
| --- | --- |
| funken, Global, Durchsagen, einteilen, stummschalten, "Abend beenden", Phase schalten | normale Sitzung, kein Code; im Raid hat die Einsatzleitung dafür keine Zeit (Anf. A38). Phase schalten und einteilen setzen `ZUWEISEN` voraus. |
| "Heute als Funker" ein- und ausschalten (nur das eigene Admin-Konto) | normale Sitzung, kein Code, keine Admin-Sitzung: Der Modus senkt eigene Rechte, er gibt keine |
| fremde Geräte und Konten sehen, Journal lesen, Mitglieder und Rollenkatalog pflegen, Name und SC-Namen eines Kontos setzen, Netze, Einheiten, Rollen und Phasen anlegen und ändern | Admin-Sitzung |
| Gerät freigeben oder ablehnen, Anträge ablehnen, Gerät dauerhaft sperren, fremdes Gerät entfernen | frischer Code |
| Konto eines Funkers oder der Führung sperren, entsperren, löschen | frischer Code |
| Konto eines anderen Admins sperren (Notbremse) | frischer Code; entsperren, löschen, Stufe, Rechte, Namen und seine Geräte nur an der Konsole |
| Stufe FUNKER oder RAIDLEITER setzen, Einzelrechte ohne Verwaltungsrechte | frischer Code, nur für nicht privilegierte Konten |
| Netze, Einheiten, Rollen oder Phasen löschen; Auftrag erteilen oder entziehen; Flotte als Paket übernehmen (mit den Phasen) | frischer Code |
| Stufe ADMIN setzen oder nehmen, Verwaltungsrechte als Einzelrecht, Admin ernennen oder entziehen, Zweitfaktor zurücksetzen oder neu einrichten, Eigentümer setzen | nur Konsole |
| Konto des Eigentümers sperren, ändern, löschen, seine Geräte entfernen | nur Konsole; im Client für niemanden |

### C11.9 Warnungen an alle Admins

Was an die Verwaltung geht (Geräte anderer, neue Anträge, Rechteänderungen), geht nur an offene Admin-Sitzungen. Ausnahme sind Warnungen: Admin-Sitzung von einem neuen Gerät, Zweitfaktor gesperrt, Notfallcode benutzt, Admin geändert, Antragsplätze voll, häufiges Verdrängen und Kopierverdacht gehen an **jede** Sitzung eines Admin-Kontos, auch ohne Admin-Sitzung. Sie enthalten keine Verwaltungsdaten. Nur so bemerkt ein Admin, wenn jemand anderes mit seinem Konto die Verwaltung öffnet. Solange es keinen Discord-Bot gibt, kommen Warnungen nur im Client an; Nachrichten in Discord gibt es noch nicht, was der Bot schicken würde, steht bis dahin nur als Logzeile ohne Namen im Logbuch.

### C11.10 Einrichten eines Admins

Die Befehle und Antworten an der Konsole stehen in B10.3, die Schritte im Client in A17. Hier geht es darum, was jeder Schritt absichert.

1. Der Eigentümer sieht das Konto an der Konsole vor: `funk-admin admin-vorsehen --konto <id>`. Erst dann zeigt der Client mit Verwaltungsteil diesem Konto den Abschnitt **Verwaltung einrichten**.
2. Der künftige Admin installiert das Admin-Setup und klickt dort auf **Admin-Schlüssel anlegen und beantragen**. Der Client erzeugt den Admin-Schlüssel im TPM, schickt den Antrag mit Besitznachweis und zeigt die Admin-Kennung. Jeder Antrag geht als Meldung an jedes Admin-Konto; ein neuer ersetzt den offenen.
3. Der Eigentümer lässt sich die Admin-Kennung am Telefon vorlesen (Discord-Sprache genügt) und trägt sie ein: `funk-admin admin-ernennen --konto <id> --kennung XXXX-XXXX-XXXX-XXXX`. Passt sie nicht zum Antrag, ändert sich nichts. Hat jemand anderes das Gerät des Kontos in der Hand und stellt einen eigenen Antrag, passt die vorgelesene Kennung nicht.
4. Binnen 10 Minuten holt der Client das TOTP-Geheimnis genau einmal ab und zeigt es als QR-Code und als Text zum Scannen mit einer Authenticator-App. Kein Bildschirmfoto. Der Client speichert es nicht und löscht es aus der Anzeige, sobald der Bereich verlassen wird.
5. Binnen 10 Minuten danach der erste Code. Stimmt er, ist der Admin aktiv, und der Client zeigt genau einmal die 10 Notfallcodes.

Ohne Verwaltungsteil gehen Schritt 4 und 5 an der Konsole: `funk-admin admin-einrichtung-neu --konto <id> --anzeigen` zeigt Schlüssel und otpauth-Link zum Abtippen, `funk-admin zweitfaktor-bestaetigen --konto <id> --code <code>` schließt ab und zeigt die Notfallcodes. Ein neues Geheimnis entsteht nur an der Konsole.

### C11.11 Verwaltungsteil nur im Admin-Setup

Die Oberfläche der Verwaltung liegt in einer eigenen Bibliothek (`verwaltung\Funk.Verwaltung.dll`), die nur das Admin-Setup mitbringt; das normale Setup enthält keinen Verwaltungscode (Anf. A45). Der Client lädt sie nur, wenn ihr SHA-256 zu der Liste passt, die beim Bau eingetragen wird, und zeigt das Menü nur, wenn der Server für das Konto "vorgesehen" oder "eingerichtet" meldet. Das Admin-Setup gibt der Eigentümer selbst weiter, mit Prüfsumme auf einem getrennten Weg.

**Was das schützt und was nicht:** Die Prüfsumme schützt gegen vertauschte Fassungen, nicht gegen Angreifer. Das Setup installiert ohne Administratorrechte in den Benutzerordner (`%LOCALAPPDATA%\Programs\JG-TacRelay`), und diesen Ordner kann jeder Schadcode im Benutzerkonto samt Programmdatei austauschen. Auf dem PC eines Admins zählt deshalb vor allem, dass dort kein Schadcode läuft. Durchgesetzt wird alles am Server.

## C12 Eigentümer und Konsole

**Eigentümer.** Genau ein Admin-Konto trägt das Merkmal `eigentuemer`; das ist Sarevoc (Anf. A41). Gesetzt wird es nur an der Konsole (`funk-admin eigentuemer --konto <id>`); ein neuer Eigentümer ersetzt den alten. Ist keiner gesetzt, warnt der Server beim Start mit "Kein Eigentümer gesetzt". Im Client fasst niemand das Konto des Eigentümers an, auch kein anderer Admin mit Admin-Sitzung und frischem Code. Die Konsole verweigert es, den Eigentümer herabzustufen oder zu löschen, solange kein anderes Admin-Konto Eigentümer ist. Sein Konto fällt nie unter die Löschfrist.

**Konsole.** Die Konsole ist `funk-admin` auf dem Server. Es spricht mit dem laufenden Dienst über einen UNIX-Socket, eine Datei mit Modus 0600; der Server prüft zusätzlich die Benutzerkennung der Gegenseite (`SO_PEERCRED`) und lässt nur root, den Dienstbenutzer `funk` und die in `verwaltungsUids` eingetragenen zu. `verwaltungsUids` bleibt leer; ist es nicht leer, warnt der Server beim Start. Jede Handlung an der Konsole steht im Journal, mit "Konsole" als Urheber.

**Nur an der Konsole:** Eigentümer setzen; Admins vorsehen, ernennen, sperren, entsperren, zurücksetzen und entziehen; Admin-Schlüssel entfernen; Zweitfaktor neu einrichten; Notfallcodes erzeugen; Stufe ADMIN und Verwaltungsrechte als Einzelrecht; Geräte privilegierter Konten freigeben; Geräte anderer privilegierter Konten entfernen und sperren; Geräte zwischen Konten umhängen; Profil neu laden; Sicherung.

**Warum nur an der Konsole:** Säße die Ernennung im Client, könnte jemand, der PC und Telefon des Eigentümers zugleich in der Hand hat, sich einen dauerhaften zweiten Admin anlegen. An der Konsole braucht er zusätzlich den Zugang zum Server. Ernannt wird selten; die Konsole kostet dafür zwei Minuten.

**Die Kehrseite: Konsole heißt Eigentümer.** Wer auf dem Server root ist oder den Dienstbenutzer `funk` übernimmt, darf am Socket alles, auch den Eigentümer neu setzen, und der Dienstprozess hält den TOTP-Schlüssel im Speicher. Eine Lücke im Server, die Code ausführen lässt, führt also zu allem. Dagegen helfen nur die Härtung des Dienstes, die kleine Angriffsfläche und eine fremde Durchsicht. Der Zugang zum Host (heute der Proxmox-Host) ist deshalb das wertvollste Gut. Nach einem Umzug liegt die Konsole beim neuen Betreiber.

## C13 Rechte, Stufen und Auftrag

| Recht | Bedeutung | FUNKER | RAIDLEITER ("Führung") | Auftrag (Einsatzleitung) | ADMIN |
| --- | --- | --- | --- | --- | --- |
| `GLOBAL_SENDEN` | in Global senden | - | ja | ja, abwählbar | ja |
| `DURCHSAGE` | Durchsage an ausgewählte Netze | - | ja | ja, abwählbar | ja |
| `DURCHSAGE_ALLE` | Durchsage an alle | - | ja | ja, abwählbar | ja |
| `MEHRFACH_SENDEN` | in mehrere Netze zugleich senden | - | ja | ja, abwählbar | ja |
| `ZUWEISEN` | andere verschieben, Aufstellungen, stummschalten, Selbstwahl sperren, Plätze freigeben | - | - | ja | ja |
| `SPRECHANZEIGE_ALLE` | Sprechanzeige aller Netze | - | ja | ja | ja |
| `GERAETE_FREIGEBEN` | Geräte freigeben, ablehnen, dauerhaft sperren | - | - | nie | nur in Admin-Sitzung |
| `KONTEN_VERWALTEN` | Konten sperren, Stufe, Rechte, Name, löschen, Aufträge | - | - | nie | nur in Admin-Sitzung |
| `NETZE_VERWALTEN` | Netze, Einheiten, Rollen, Einstellungen | - | - | nie | nur in Admin-Sitzung |

**Prüfung am Server.** Rechte und Status prüft der Server bei jeder Handlung und für jedes Sprachpaket am aktuellen Zustand. Gehört wird nur, wo das Konto Mitglied mit eingeschaltetem Hören ist; Absender, Art und Netz stempelt der Server. Ein veränderter Client kann sich weder Hören noch Senden erschleichen. Die Spalte ADMIN gilt nicht, solange das Konto "Heute als Funker" spielt: Dann fehlen ihm alle Rechte aus den Zeilen bis `SPRECHANZEIGE_ALLE` (siehe unten).

**Rechteverwaltung** (Protokoll 6.10):

- Niemand vergibt ein Recht, das er selbst nicht dauerhaft hat; Rechte aus einem Auftrag zählen dabei nicht.
- Niemand ändert die eigene Stufe oder die eigenen Rechte.
- Der letzte aktive Admin kann im Client nicht gesperrt, herabgestuft oder gelöscht werden.
- Stufe ADMIN und Verwaltungsrechte als Einzelrecht vergibt nur die Konsole; Einzelrechte erscheinen als sichtbare Abweichung (`plus`, `minus`).
- Jede Änderung an Stufe oder Rechten geht als Meldung an die Verwaltung und steht im Journal.
- **Rangregel:** `ZUWEISEN` wirkt auf Funker und Führung, auf Admin-Konten nur durch einen Admin oder solange das Konto "Heute als Funker" spielt (seit 0.5.0, `Rechteregeln.DarfZuweisen`). Sie gilt für Verschieben, Plätze freigeben, Aufstellungen mit Verdrängen, Schützen und Nachziehen und für das Schalten einer Phase. Einen Admin stummschalten darf nur ein Admin, auch wenn er im Funker-Modus ist. Eine Stummschaltung hebt auf, wer sie gesetzt hat, ein Konto höherer Stufe oder ein Admin, nie der Betroffene selbst.
- **Privilegierte Konten** (Stufe ADMIN oder eines der drei Verwaltungsrechte): Geräte nur über die Konsole. Wird ein Konto privilegiert, verfallen seine wartenden Geräte.

**Auftrag für einen Abend** (Anf. A37, Protokoll 6.12). Seit 0.3.0 teilt die Stufe RAIDLEITER nicht mehr ein. Einteilen darf ein Admin oder wer von einem Admin einen befristeten Auftrag bekommt:

- Erteilen und entziehen nur mit `KONTEN_VERWALTEN`, im Client mit frischem Code, oder an der Konsole (`funk-admin auftrag --konto <id>`, `funk-admin auftrag-entziehen --konto <id>`, `funk-admin auftraege`). Sich selbst erteilt niemand einen Auftrag.
- Ein Auftrag trägt immer `ZUWEISEN` und `SPRECHANZEIGE_ALLE`, wahlweise die vier Rechte der Führung, **nie** ein Verwaltungsrecht. Ein Beauftragter wird dadurch nie privilegiert und kann nichts weitergeben.
- Beginn höchstens 14 Tage voraus, Dauer höchstens 24 Stunden, ohne Angabe bis zum nächsten 04:00 Uhr (Europe/Berlin); höchstens 4 laufende oder geplante Aufträge; nicht für Admin-Konten und gesperrte Konten.
- Wirksam nur in seinem Zeitfenster, geprüft je Handlung und je Sprachpaket. Er endet sofort mit dem Entzug, mit der Abgabe ("Abend beenden"), mit Sperre, Löschung oder Stufenwechsel des Kontos und wenn der Erteilende gesperrt oder gelöscht wird oder `KONTEN_VERWALTEN` verliert.
- Jeder Schritt steht im Journal. `funk-admin sicherung` lässt Aufträge und seit 0.5.0 auch den Funker-Modus weg, damit ein entzogener Auftrag nicht aus einem alten Stand zurückkommt (Phasen und die laufende Phase bleiben in der Sicherung, denn die Felder der Konten passen zu ihr; `Zustandsablage.OhneAuftraege`). Eine Sicherung des ganzen Containers trägt sie dagegen mit; nach dem Zurückspielen also prüfen und entziehen.
- Offen: Die Zusatzbestätigung "Rückfrage beim Inhaber erfolgt" bei der Gerätefreigabe für Konten mit Auftrag ist als Regel der Oberfläche vorgesehen, aber noch nicht gebaut.

**Heute als Funker** (seit 0.5.0, Anf. A61, Protokoll 6.15). Ein Admin, der einen Abend nur mitfunken will, während ein anderer leitet, schaltet das am eigenen Konto ein. Eine zweite Anmeldung gibt es dafür nicht.

- **Wer und wie:** Nur ein aktives Konto der Stufe ADMIN, nur für sich selbst, ohne Admin-Sitzung und ohne Code, denn der Modus nimmt Rechte und gibt keine. Niemand setzt einen anderen in den Modus, verlängert ihn oder beendet ihn.
- **Was ruht:** Bis zum nächsten 04:00 Uhr (Europe/Berlin), höchstens 24 Stunden lang, fehlen dem Konto die Rechte der Einsatzleitung: die vier Rechte der Führung (`GLOBAL_SENDEN`, `DURCHSAGE`, `DURCHSAGE_ALLE`, `MEHRFACH_SENDEN`), `SPRECHANZEIGE_ALLE` und `ZUWEISEN`. Das gilt an drei Stellen im Code: am Konto (`Konto.RechteZu`), im Heißpfad bei jedem Sprachpaket (`Schnappschuss.RechteAn`) und im Lagebild des Clients (`Lagebild.HatRecht`); auch die Pushes, die nur an Inhaber von `ZUWEISEN` gehen (Präsenzdetails, Meldungen), und die Sprechanzeige aller Netze erreichen das Konto dann nicht mehr. Die Sendeauswahl verliert Global und Mehrfachziele. Die drei Verwaltungsrechte bleiben und wirken wie immer nur in einer Admin-Sitzung.
- **Wer einteilt:** Die Einsatzleitung teilt das Konto ein wie einen Funker (Rangregel oben). Das ist die eine Stelle, an der die Rangregel nachgibt: Ein Inhaber von `ZUWEISEN` darf ein Admin-Konto sonst nicht umsetzen, im Funker-Modus aber schon. Das Konto selbst gewinnt dadurch nichts; es hat in dieser Zeit weniger Rechte als sonst.
- **Ende:** mit "Wieder Admin" (ohne Code), um 04:00 durch den Fristen-Takt (die Rechte gelten schon ab `bis`, auch bevor der Takt läuft), bei einem Stufenwechsel und bei einer Sperre des Kontos. Beim Einschalten enden die eigene Sperre der Selbstwahl und das eigene Nachziehen einer angewandten Aufstellung (Regel 7 aus 6.12); beim Ausschalten kommen sie nicht zurück. Ein zweites Einschalten verlängert nicht.
- **Spuren:** Ein- und Ausschalten stehen im Journal (Klarname, Zeit, bei Ablauf, Stufenwechsel oder Sperre mit dem Grund). Die Einsatzleitung sieht "als Funker" an dem Konto, gewöhnliche Mitglieder nicht.
- **Was es nicht ist:** kein Schutz gegen einen übernommenen Admin-PC. "Wieder Admin" braucht keinen Code und gibt nur die Rechte zurück, die das Konto ohne Admin-Sitzung ohnehin hat (C3.6).

**Einsatzphasen** (seit 0.5.0, Anf. A61, Protokoll 6.14). Eine Phase ist ein Netzbild der Flotte, keine Personenliste: Die Grundstellung sind die Felder der Rollen, dazu kommen höchstens 4 Phasen (Nummer 2 bis 5), die für einzelne Rollen andere Felder festlegen. Plätze, Rollen, Reservierungen und Belegung ändert eine Phase nie; es gibt also kein Verdrängen.

- **Anlegen, ändern, löschen:** Phasen legt und ändert, wer `NETZE_VERWALTEN` hat, in einer Admin-Sitzung; löschen und ein ganzes Paket brauchen den frischen Code (C11.8). Grenzen: Name 24 und Kurzname 12 Zeichen, zusammen höchstens 512 Variantenfelder, je Rolle und Phase 16 Felder. Die Grundstellung löscht niemand. Ändert ein Admin die laufende Phase, stellt der Server sofort um.
- **Schalten:** Nur mit `ZUWEISEN` (bei einem Auftrag nur im Zeitfenster; ein Admin im Funker-Modus und die Führung ohne Auftrag schalten nicht). Der Server stellt nur Konten um, deren Felder sich ändern, und nur solche, die der Schaltende nach der Rangregel einteilen darf; die übrigen stehen in der Antwort unter `ausgelassen`. Wer sein Sendenetz selbst gewählt hat und es weiter darf, behält es; "Hören aus" bleibt für Netze, die vorher und nachher da sind, wenn das neue Feld nicht fest ist. Ein Senderecht, das ein Feld in der neuen Phase verliert, vermerkt der Server wie bei jeder Umstellung (`ZIEL_ENTZOGEN`).
- **Zweiter Leiter:** Jede Schaltnachricht nennt in `erwartet` die Phase, die der Absender für die laufende hält. Weicht sie ab, antwortet der Server `VERALTET` und schaltet nichts (C3.10).
- **Von selbst zurück:** 12 Stunden nach dem letzten Schalten fällt die Flotte auf die Grundstellung, auch nach einem Neustart des Servers; die Folgen sind die eines Wechsels.
- **Bremse:** 1 Schaltnachricht je 2 Sekunden, Stoß 2 (C14.1).

**Gegen Trolle.** Je Sitzung höchstens 60 Sprachpakete je Sekunde (Stoß 20) und 16.000 Byte Opus je Sekunde (Stoß 4000). Stummschalten geht für höchstens eine Stunde am Stück; wer nicht aufhört, dessen Konto sperrt ein Admin.

## C14 Sperrliste und Grenzen

### C14.1 Grenzen

Alle Grenzen sind Token-Eimer (Rate und Stoß) mit monotoner Uhr. "Je Adresse" heißt bei IPv4 die einzelne Adresse, bei IPv6 das /64.

| Bereich | Grenze |
| --- | --- |
| TLS-Handshakes je Adresse | unbekannt 10 je Minute (Stoß 5), aus der Vorrangspur 60 je Minute (Stoß 10) |
| gleichzeitige TCP-Verbindungen je Adresse | 8 (gemeinsames NAT) |
| Handshakes serverweit | 200 je Sekunde, nur CPU-Schutz; die Vorrangspur geht daran vorbei |
| unfreigegebene Verbindungen nach TLS | 2 je Adresse, 20 serverweit; freigegebene Geräte zählen nie |
| Codes und Anträge | 3 je Verbindung, 10 je Adresse und Stunde, höchstens 30 offene Anträge |
| Steuerrahmen je Sitzung | 30 je Sekunde (Stoß 60), 256 KiB je Sekunde (Stoß 1 MiB) |
| Sprache je Sitzung | 60 Pakete je Sekunde (Stoß 20), Opus 16.000 Byte je Sekunde (Stoß 4000) |
| UDP von fremden Adressen | Vorfilter je Adresse 400 Datagramme je Sekunde (Stoß 200), je Sitzung und fremder Adresse 60 je Sekunde (Stoß 30), höchstens 16 fremde Adressen je Sitzung, zusammen 200 je Sekunde |
| Admin-Nachrichten | 1 je Sekunde (Stoß 3), Nonce eigener Topf gleicher Größe |
| "Heute als Funker" (`0x003C`, seit 0.5.0) | eigener Topf, 1 je Sekunde (Stoß 2) |
| Phase schalten (`0x003D`, seit 0.5.0) | eigener Topf, 1 je 2 Sekunden (Stoß 2); eine Flut endet bei `GRENZE`, ohne dass die Verbindung getrennt wird (erst die Regel "Verstöße" unten trennt) |
| Phasen (seit 0.5.0) | Grundstellung und höchstens 4 Phasen; Name 24 und Kurzname 12 Zeichen; zusammen höchstens 512 Variantenfelder, je Rolle und Phase 16; Rückfall auf die Grundstellung 12 Stunden nach dem letzten Schalten |
| Sitzungen | höchstens 100 gleichzeitig, eine je Konto |
| Verstöße | mehr als 50 in 60 Sekunden: Trennung und 60 Sekunden Sperre des Geräts |

**Keine Verstärkung.** Auf Ungültiges antwortet der Server nicht; auf ein gültiges Paket von fremder Adresse höchstens mit einer Pfadprüfung derselben oder kleinerer Größe; ein Keepalive-Echo nur an die gebundene Adresse; Sprache nur an Adressen, die ihre Erreichbarkeit bewiesen haben. Der Server taugt deshalb nicht als Verstärker für Angriffe auf Dritte (Protokoll 7.5).

### C14.2 Vorrangspur

Adressen, von denen in den letzten 14 Tagen eine aktive Sitzung kam, merkt sich der Server als HMAC-Werte (höchstens 2000), auch über einen Neustart. Sie gehen an den globalen Töpfen vorbei und werden von der Sperrliste nie gesperrt. So kommen Mitglieder nach einem Neustart oder während einer Handshake-Flut als Erste zurück: Familie, gemeinsames NAT, die eigene Heimadresse.

### C14.3 Die eingebaute Sperrliste

Seit 0.3.0 sperrt `funk-server` Adressen, die sich wie Scanner, Botnetze oder Durchprobierer benehmen, selbst und befristet (Anf. A51). Dafür ist nichts einzurichten; das ist der Hauptschutz des Funks (`BETRIEB.md`, Abschnitt "Sperrliste").

| Anlass je Adresse | Punkte |
| --- | --- |
| gescheiterter oder abgebrochener TLS-Handshake (Scanner, Browser, auch ein Client, der den Pin ablehnt) | 1 |
| leerer Handshake-Topf der Adresse | 1 |
| Handshake ohne Gerätezertifikat oder ohne ALPN | 2 |
| Protokollmüll vor der Anmeldung, kein `Hello` in der Frist | 3 |
| Anträge oder Registrierungscodes über der Grenze | 3 |
| gesprengte Ratengrenzen eines unfreigegebenen Geräts | 5 |

- **Schwelle:** 10 Punkte in 10 Minuten sperren die Adresse (IPv4) bzw. das /64.
- **Wirkung:** Die Annahme schließt Verbindungen gesperrter Adressen gleich nach `accept` mit einem Reset, vor TLS und vor den Töpfen; das kostet fast nichts. UDP ohne Sitzung verwirft der Server ohnehin vor jeder Kryptografie.
- **Dauer:** 5 Minuten, dann je weiterer Sperre sechsmal so lang (30 Minuten, 3 Stunden, 18 Stunden), höchstens 24 Stunden. Nach 48 Stunden ohne Sperre beginnt eine Adresse wieder bei 5 Minuten.
- **IPv6:** Sind 4 /64 aus demselben /48 gesperrt oder sammeln seine /64 zusammen 40 Punkte, gilt eine Sammelsperre für das /48. Für ein Mitglied im selben /48 gilt sie nicht, wenn sein /64 in der Vorrangspur steht.
- **Nie gesperrt:** Adressen der Vorrangspur, eingetragene Weiterleiter (`vertrauenswuerdigeWeiterleiter`) und Ausnahmen (`sperrliste.ausnahmen`); dort zählen nicht einmal Punkte.
- **Durchlass:** Während einer Sperre lässt der Server je Adresse alle 30 Sekunden eine Verbindung bis zum Handshake durch. Ein freigegebenes Gerät hinter derselben Adresse (CGNAT, Mobilfunk, ein Mitglied an einer neuen Adresse, die ein Angreifer teilt) kommt so herein und hebt die Sperre seiner Adresse auf. Nimmt der Angreifer genau dann den Durchlass, hilft `funk-admin entsperren --ip <adresse>`.
- **Globale Grenzen sind kein Verstoß:** Eine volle Zählertabelle für Codes und Anträge (4096 Adressen je Stunde) gibt `GRENZE`, aber keine Punkte.
- **Neustart:** Sperren und Stufen stehen in `sperrliste.json` und überstehen Neustart und Update, damit ein Angreifer, der einen Neustart abwartet oder auslöst, nicht von vorn beginnt. Gespeichert werden nur HMAC-Werte und die Kennung aus dem Logbuch, nie eine Adresse. Eine Sperre, die weiter als 24 Stunden in die Zukunft reicht (verstellte Uhr, bearbeitete Datei), gilt beim Laden nur 24 Stunden. Die Datei gehört nicht in die Sicherung und reist nicht mit einem Umzug.
- **Uhr:** Springt die Systemuhr zurück, gilt eine Sperre höchstens ihre Dauer ab dem Sprung, und der Durchlass bleibt.
- **Speicher:** höchstens 10.000 Adressen; ist die Liste voll, fallen zuerst ungesperrte, lange ruhige heraus, Sperren nie.

Ansehen und aufheben (Einzelheiten und alle Antworten in B16.2):

```
funk-admin sperrliste
funk-admin entsperren --ip <adresse des mitglieds>
funk-admin entsperren --eintrag <kennung>
funk-admin entsperren --alle
```

`sperrliste` zeigt laufende Sperren mit Kennung (wie im Logbuch, nie die Adresse), Ende, Stufe und Anlass; läuft keine, steht dort `Keine Sperren (n Adressen beobachtet).`. Die Adresse für `--ip` nennt dir das Mitglied. Die Antwort nennt nur die Kennung; das Aufheben steht im Journal.

**Einstellungen.** Ohne Abschnitt `sperrliste` in `server.json` gelten die Voreinstellungen oben. `sperrliste.aktiv: false` ist nur für die Fehlersuche gedacht, mit Warnung beim Start.

**fail2ban** ist für den Funk nur ein Zusatz und für den Heimserver nicht empfohlen (`HEIMSERVER-HAERTUNG.md`, Punkt 7.2): fail2ban am Proxmox-Host kennt keinen Durchlass, sperrt alle Ports (auch Foundry) und braucht eine Datei mit Klartext-Adressen (`sperrliste.klartextDatei`, Voreinstellung aus). Wer es trotzdem will, findet Filter und Jail in `konfig/fail2ban/`; das Jail sperrt nur 5 Minuten ohne Steigerung.

## C15 Datensparsamkeit

### C15.1 Was wo liegt und wie lange

Die Tabelle zeigt die Daten aus Sicht der Sicherheit. Alle personenbezogenen Daten Feld für Feld, mit Rechtsgrundlage und Löschweg, stehen in F11.2.

| Daten | Ort | Wer sie sieht | Frist |
| --- | --- | --- | --- |
| Konten: SC-Namen, Discord-Name, Anzeigename, Stufe, Rechte, Status | `konten.json` am Server | Admins; Hauptname und Discord-Name sieht jedes Mitglied in "Wer ist da" | Konten ohne freigegebenes Gerät werden 30 Tage nach dem letzten Geräteereignis gelöscht, samt Pegeln, Ablage und Plätzen; das Konto des Eigentümers nicht |
| Geräte: Fingerabdruck, Gerätename, Schlüsselart | `konten.json` | Admins | wartend 24 Stunden; freigegeben ohne Sitzung 90 Tage, dann wieder wartend; abgelehnt oder mit dem Konto gelöscht: sofort fort. Nur der Fingerabdruck eines bewusst gesperrten Geräts (`sperren`) bleibt als bloßer Hashwert |
| Discord-ID und Discord-Benutzername | `konten.json`, nur bei Konten aus dem Weg mit Code | Admins | wie das Konto |
| Mitgliedsangaben: Rollen, Wünsche, Abzeichen, Notiz, Vormerkungen | `mitglieder.json` (0600) | Admins; Rollen und Abzeichen nach Freigabe durch das Mitglied | fallen mit dem Konto weg |
| Untergilden aus der Zeit vor 0.11.8 (Name, Kürzel, Kontonummern und Vormerkungsnummern ihrer Mitglieder) | `mitglieder.json` (0600) | niemand: seit 0.11.8 weder gezeigt noch gelesen | bleiben stehen, damit eine ältere Serverfassung den Stand laden kann; wird ein Konto oder eine Vormerkung gelöscht, fällt deren Nummer heraus (auch als Ansprechpartner), sonst ändert sich nichts daran |
| Pegel, Ablage, Sendeauswahl | `komfort.json` | das Konto selbst | mit dem Konto |
| Lagezustand: Felder, Plätze, Aufstellungen, Aufträge, ab 0.5.0 Phasen, laufende Phase und `alsFunkerBis` | `lage.json` | Inhaber von `ZUWEISEN` (auf der Raid-Seite, bis Client 0.11.1 das Brett, in Präsenzdetails und im Kontenverzeichnis, dort auch "als Funker"); das Konto selbst sieht seine eigenen Felder und seinen Funker-Modus | Felder und Plätze fallen mit dem Konto weg; ein Auftrag gilt höchstens 24 Stunden, der Funker-Modus bis 04:00 (höchstens 24 Stunden); `funk-admin sicherung` lässt beide weg; Phasen bleiben, bis ein Admin sie löscht (sie sind Netzbilder ohne Namen; die laufende Phase nennt nur das Konto, das zuletzt geschaltet hat) |
| Journal der Verwaltung mit Klarnamen | `journal.json` (0600) | nur Admin-Sitzung mit `KONTEN_VERWALTEN` und die Konsole | 90 Tage, höchstens 50.000 Einträge, je Eintrag höchstens 200 Zeichen |
| Logbuch (Betrieb, Messwerte alle 10 Sekunden je Sitzung) | stdout des Dienstes, also das systemd-Journal; wahlweise eine Datei | root | wie lange, bestimmt journald auf dem Server, nicht JG TacRelay |
| Vorrangspur | `vorrangspur.json`, nur HMAC-Werte | - | 14 Tage, höchstens 2000 |
| Sperrliste | `sperrliste.json`, nur HMAC-Werte und Kennungen | Konsole | Stufe 48 Stunden nach Sperrende, Sperren höchstens 24 Stunden |
| Klartext-Adressen für fail2ban | `/var/log/funk/sperren.log`, nur wenn eingeschaltet | root | keine Frist; von Hand leeren (`truncate -s 0 /var/log/funk/sperren.log`) |
| Client: Namen aus dem Setup, Einstellungen, übernommenes Profil | `%LOCALAPPDATA%\JG-TacRelay` | der Benutzer | bis zur Deinstallation |

**Telemetrie gibt es keine.** Messwerte (Verlust, Jitter, Umlaufzeit) bleiben auf dem eigenen Server.

### C15.2 Logbuch: Adressen nur als HMAC

IP-Adressen, Präfixe und Konto-Nummern erscheinen im Logbuch nur als gekürzter HMAC-SHA256-Wert: `ip#...` und `ip6#...` für Adressen, `p4#...` und `p6#...` für Präfixe, `g6#...` für ein IPv6-/48, `k#...` für Konten, jeweils 10 Hexzeichen. Der Schlüssel dafür wird aus dem Betriebsgeheimnis abgeleitet und nie ausgegeben. Schlüssel, IV, Codes, Geheimnisse und Opus-Daten stehen nie im Logbuch; Klarnamen auch nicht. Eine Sperre steht dort etwa so:

```
WARNUNG Sperrliste: p4#... für 5 min gesperrt (Stufe 1, Anlass HANDSHAKE, 10 Punkte)
```

Seit 0.5.0 schreibt der Server auch jedes Schalten einer Phase als Zeile ins Logbuch, in der Form `Phase 1 -> 2: <n> umgestellt, <m> ausgelassen, durch k#...` (kam der Wechsel von der Konsole oder vom Rückfall nach 12 Stunden, steht statt des Kürzels "Konsole oder Rückfall"). Ein Name steht darin nicht.

**Pseudonym, nicht anonym.** Wer das Betriebsgeheimnis hat, kann für eine bekannte Adresse den Wert ausrechnen und vergleichen; genau so findet `entsperren --ip` die Sperre eines Mitglieds. Ohne das Geheimnis führt vom Kürzel kein Weg zurück zur Adresse. Das Betriebsgeheimnis liegt deshalb wie die Schlüssel in `/etc/funk/credentials` und nie in einer Sicherung.

### C15.3 Journal: Klarnamen, 90 Tage

Das Journal hält fest, wer in der Verwaltung was getan hat, mit Zeit, Art, Urheber (Konto oder "Konsole"), betroffenem Konto und einem Klartext mit Namen: Admin-Sitzungen, Bestätigungen, Fehlversuche, Pausen und Sperren, Notfallcodes, Anträge, Ernennungen, Freigaben und Ablehnungen, Kontoänderungen, Mitglieder, Rollenkatalog, Abzeichen, Netze, Einheiten und Phasen (anlegen, ändern, löschen), Aufträge, "Heute als Funker" (ein und aus, mit dem Grund beim Ende durch Ablauf, Stufenwechsel oder Sperre), aufgehobene Sperren und alle Konsolenbefehle. Codes, Geheimnisse und Notfallcodes stehen nie darin. Es ist der Ort, an dem Klarnamen stehen dürfen, weil Nachvollziehbarkeit sie braucht; deshalb ist es kurz befristet und nur in der Admin-Sitzung und an der Konsole lesbar:

```
funk-admin journal --anzahl 50
funk-admin journal --konto <id>
```

### C15.4 Sicherungen verlängern Fristen

`funk-admin sicherung` legt Kopien der Zustandsdateien unter `/var/lib/funk/sicherungen/<zeit>` ab. Sie enthalten das Journal mit Klarnamen und `admins.json` mit den verschlüsselten TOTP-Geheimnissen, aber keine Schlüssel und nicht das Betriebsgeheimnis. Wer Sicherungen länger als 90 Tage aufhebt, hebt auch die Klarnamen länger auf. Eine Sicherung des ganzen Containers (Proxmox vzdump) enthält zusätzlich `/etc/funk/credentials`, also Serverschlüssel, Betriebsgeheimnis und TOTP-Schlüssel neben `admins.json`: Wer so eine Sicherung stiehlt, kann die Zweitfaktor-Geheimnisse aller Admins öffnen. Solche Sicherungen nur verschlüsselt ablegen.

### C15.5 Was Mitglieder voneinander sehen

"Wer ist da" zeigt den Discord-Namen als Namen und den eindeutigen SC-Hauptnamen daneben. Zeichen, Abzeichen und Rollen sehen andere nur, solange das Mitglied seine Angaben freigibt (Häkchen "Meine Angaben in Wer ist da zeigen", ab Werk an); Rollen und Wünsche zusätzlich nur, solange `mitgliedskarte.rollenFuerAlle` in `server.json` gilt (Voreinstellung ja). Weitere SC-Namen, Verbindungsgüte und letzter Platz sehen nur die Einsatzleitung, Admins und das Konto selbst. Karten gibt es nur für Konten, die gerade online sind oder einen Platz haben; über die Karte lässt sich nicht die ganze Mitgliederliste abfragen.

### C15.6 Löschen

Ein Konto löscht ein Admin im Client (frischer Code; Admin-Konten nur an der Konsole) oder die Konsole (`funk-admin konto-loeschen --konto <id>`; den Eigentümer löscht sie nicht). Konto, Namen, Geräte samt Fingerabdrücken, Pegel, Ablage, Plätze, Admin-Nachweis und Mitgliedsangaben fallen weg; seit 0.7.0 kommt dabei nichts auf die Sperrliste (Sarevoc, 04.10.2026: Löschen heißt vollständig entfernen), und der PC darf neu beantragen. Nur der Fingerabdruck eines Geräts, das ein Admin bewusst gesperrt hat (`sperren`), bleibt als bloßer Hashwert, damit es gesperrt bleibt. Eine Datenschutzerklärung ist noch nicht verabschiedet; das Protokoll führt sie als Offenen Punkt 9 (Discord-Pflicht, DSGVO), mit Konto, Geräten, Pegeln, Ablage, Vorrangspur, Messwerten, Löschfristen und dem Weg zur Löschung. Einen Entwurf enthält F11.10.

## C16 Härtung des Hosts

Weil root auf dem Server alles hat, gehört der Host zum Sicherheitskonzept. Die Schritte im Einzelnen stehen in B17 sowie in `HEIMSERVER-HAERTUNG.md` (Heimserver) und `EINRICHTEN.md` (eigener Server); hier steht, was sie bewirken und warum.

### C16.1 Der Dienst

`funk-server` läuft als eigener Systembenutzer `funk` ohne Anmeldung, mit einer gehärteten systemd-Unit (`konfig/funk-server.service`; jede Zeile mit ihrer Wirkung in B7.1). Was sie für die Sicherheit bewirkt:

- **Kein root und keine Fähigkeiten im Dienst:** `User=funk`, `NoNewPrivileges`, `CapabilityBoundingSet=` und `AmbientCapabilities=` leer. Bei einem TCP-Port unter 1024 legt `funk-einrichten.sh` ein Drop-in mit genau dem Recht zum Binden an (B4.4).
- **Schlüssel nie aus der Konfiguration:** Serverschlüssel, Zertifikat, Betriebsgeheimnis und TOTP-Schlüssel kommen per `LoadCredential` von systemd; fehlt eine Datei, startet der Dienst nicht (`status=243/CREDENTIALS`).
- **Nur drei beschreibbare Ordner:** `ProtectSystem=strict`, `ProtectHome`, `PrivateTmp`, `PrivateDevices`; schreiben darf der Dienst nur in `/var/lib/funk`, `/run/funk` und `/var/log/funk`, mit `UMask=0077`.
- **Kein Speicherabbild:** `LimitCORE=0` und `DOTNET_DbgEnableMiniDump=0`, damit keine Sitzungs- oder Identitätsschlüssel in einer Datei landen.
- **Weniger Angriffsfläche im Kernel:** die `Protect...`- und `Restrict...`-Zeilen, nur die Adressfamilien UNIX, IPv4 und IPv6. `MemoryDenyWriteExecute` bleibt aus, weil der JIT-Compiler von .NET Speicher ausführbar machen muss.

### C16.2 Dateien und Rechte

| Pfad | Rechte | Inhalt |
| --- | --- | --- |
| `/etc/funk/credentials` | Ordner 0700, Dateien 0600, root | Serverschlüssel, Zertifikat, Betriebsgeheimnis, TOTP-Schlüssel |
| `/var/lib/funk` | 0700, Dienstbenutzer | Zustandsdateien, Sicherungen |
| `/run/funk` | 0700, Dienstbenutzer; Socket 0600 | Verwaltungssocket `verwaltung.sock` (B1.5) |
| `/opt/funk` | root, für andere nicht beschreibbar | Programme |
| `/etc/funk/server.json`, `/etc/funk/profil.json` | lesbar, ohne Geheimnisse | Konfiguration, öffentliches Profil |

Zustandsdateien werden atomar ersetzt (Zwischendatei, Flush, Umbenennen). Die Zwischendatei legt das Programm neu an, statt eine vorhandene zu öffnen, und `chown` folgt beim Einspielen keinem Verweis; so kann ein übernommener Dienstbenutzer root beim Umzug nicht durch eine untergeschobene Datei schreiben lassen. Befehle, die Schlüssel oder Geheimnisse erzeugen, ersetzen ab 0.4.0 keine vorhandene Datei (Exit 3), außer mit `--ersetzen`, und sagen vorher, was das kostet.

Prüfen, ob alles stimmt:

```
funk-admin pruefen --vollstaendig
ls -l /etc/funk/credentials
ss -tulpen
```

`pruefen --vollstaendig` prüft unter anderem Schlüsseldateien und ihre Rechte, den Pin des Serverschlüssels im Profil, die Signatur, den Ablauf, die Uhr und freie Ports; je Befund eine Zeile `OK`, `HINWEIS`, `WARNUNG` oder `FEHLER`. `ls -l` zeigt die Schlüsseldateien mit `-rw-------`. `ss -tulpen` zeigt den TCP- und den UDP-Port des Funks auf der konfigurierten Adresse und sonst höchstens Dienste auf `127.0.0.1`.

### C16.3 Netz und Firewall

**Heimserver.** Von außen erreichbar sind genau drei Ports: TCP 443 (nginx vor Foundry auf demselben Host), TCP 8443 und UDP 47443 (Funk). Keine Proxmox-Oberfläche (8006), kein SSH (22), kein Port 80, außer nginx braucht ihn für das Zertifikat von Let's Encrypt. In der FRITZ!Box gibt es nur diese Freigaben, selbstständige Portfreigaben (UPnP) sind für die Geräte aus, und der Fernzugriff auf die FRITZ!Box über HTTPS ist aus, solange du ihn nicht brauchst.

Der Funk-Container ist unprivilegiert (mit `nesting=1`, ohne das startet der gehärtete Dienst nicht) und hat eine Proxmox-Firewall an seiner Netzwerkkarte (Regeln in B3.4, `HEIMSERVER-PROXMOX.md`, Abschnitt 7). Eingehend lässt sie nur die zwei Ports des Funks herein. Ausgehend kommt der Container ins Internet (Updates), ins Heimnetz aber nur zum DNS der FRITZ!Box. **Begründung:** Wird der Container übernommen, bleibt der Rest des Heimnetzes, etwa Foundry auf demselben Host, außer Reichweite. Im Container selbst braucht es kein SSH; `pct enter` vom Host reicht.

**Eigener oder gemieteter Server.** `funk-einrichten.sh` fasst Firewall und Router nicht an, es zeigt nur, was nötig ist; auf einem Server mit aktivem ufw trägt es die zwei Regeln nur nach ausdrücklichem "ja" ein, bei nftables zeigt es sie nur. SSH nur mit Schlüssel und automatische Sicherheitsupdates gelten dort genauso (Checkliste unten).

### C16.4 Proxmox-Host, SSH und Updates

- **Proxmox-Oberfläche** nur im Heimnetz, nie in der FRITZ!Box freigeben; von unterwegs über einen VPN-Zugang ins Heimnetz. Für die Anmeldung einen Zweitfaktor (TOTP, auch mit einer Authenticator-App) einrichten und die Wiederherstellungsschlüssel getrennt vom Telefon aufheben.
- **SSH nur mit Schlüssel**, am Host und in jedem Container mit SSH. Ohne Passwortanmeldung gibt es nichts zu raten. Die Schritte, mit einer zweiten offenen Sitzung als Rückweg, stehen in B17.2.
- **Sicherheitsupdates:** in den Containern automatisch mit `unattended-upgrades`; die .NET-Laufzeit im Funk-Container aktualisierst du von Hand mit Neustart des Dienstes, weil ein stilles Update mitten im Raid schlechter wäre als ein geplantes. Den Proxmox-Host von Hand, etwa monatlich und nach Sicherheitsmeldungen, Neustart an einem Abend ohne Raid.
- **Uhr:** Auf dem Host muss die Zeitsynchronisierung laufen (Proxmox bringt chrony mit; der Container hat die Uhr des Hosts). Der Zweitfaktor verträgt nur 30 Sekunden Abweichung. Der Server warnt beim Start, wenn die Uhr offensichtlich falsch geht (vor dem Erscheinen der Fassung, oder der jüngste Journaleintrag liegt mehr als 5 Minuten in der Zukunft). Ob der Kernel synchronisiert, zeigt `pruefen` in der Shell (im Dienst selbst sperrt `ProtectClock` das Nachsehen):

  ```
  dotnet /opt/funk/funk-server.dll --konfig /etc/funk/server.json pruefen
  ```

  Erwartet ist eine Zeile `Uhr: synchronisiert (Kernel, höchstens ... ms Abweichung).` vor `Konfiguration in Ordnung.`
- **nginx vor Foundry** (nur Heimserver): Verzeichnislisten und Versionsnummer aus, Anfragen begrenzen (`limit_req`, erst einen Spielabend im Probebetrieb, dann scharf), die Einrichtungsseite von Foundry nur aus dem Heimnetz. Einzelheiten: `HEIMSERVER-HAERTUNG.md`, Punkt 5.

### C16.5 Sicherungen und der Stick

- **Schlüssel:** Serverschlüssel, Zertifikat und Betriebsgeheimnis einmal auf den Offline-Stick, zu Profilschlüssel und Reserve, am besten verschlüsselt; nie in eine Cloud oder in einen Chat. Den TOTP-Schlüssel holst du nicht heraus; geht er verloren, richten die Admins den Zweitfaktor neu ein.
- **Zustand:** regelmäßig und vor jedem Update `funk-admin sicherung`. Schlüssel und Betriebsgeheimnis kommen nie mit.
- **Ganzer Container:** vzdump-Sicherungen nur verschlüsselt ablegen, denn sie enthalten `/etc/funk/credentials`.
- **Der Stick** (`funk-offline`) mit BitLocker To Go verschlüsseln; dort liegen `profil.key` und `reserve.key` als ungeschützte Dateien.
- **Umzugspaket:** AES-256-GCM, Schlüssel aus einer Passphrase mit 26 Zeichen über PBKDF2-SHA256 mit 600.000 Runden; das Paket darf jeden Weg nehmen, die Passphrase nur das Telefon. Betriebsgeheimnis, Vorrangspur und Sperrliste reisen nie mit, der Serverschlüssel nur mit `--mit-serverschluessel`.

## C17 Was bewusst nicht abgedeckt ist

| Lücke | Warum sie hingenommen ist | Was hilft |
| --- | --- | --- |
| **Volumetrische Angriffe** auf den Heimanschluss (Upload 50 Mbit/s) | Wer die Adresse kennt, kann die Leitung zuschütten, bevor ein Paket den Server erreicht; dagegen kann kein Protokoll etwas tun. | Umzug zu einem Anbieter oder ein Vorposten bei einem Anbieter, der die Absenderadressen erhält (nur DNAT, Eintrag in `vertrauenswuerdigeWeiterleiter`); für den Notfall Discord-Sprache |
| **Die Heim-IP ist sichtbar** | Alle Mitglieder sehen sie, und über DynDNS jeder, der den Namen kennt. Das liegt am Heimbetrieb selbst. | Umzug nach Version 1 |
| **Keine Ende-zu-Ende-Verschlüsselung** | Der Server muss jedes Paket prüfen, stempeln und je Empfänger neu verschlüsseln. | Vertrauen in den Betreiber; Härtung des Hosts |
| **Verkehrsanalyse** | Paketgrößen, Sprechzeiten und wer mit dem Server spricht, bleiben sichtbar (RFC 6562). | - |
| **Übernommener Server** (root, Dienstbenutzer, Lücke mit Codeausführung) | Wer dort root ist, ist Eigentümer. | Härtung, kleine Angriffsfläche, fremde Durchsicht |
| **Schadcode auf einem Client-PC** | Das TPM verhindert das Wegkopieren des Schlüssels, nicht seine Benutzung. Ein Windows-Fehlerbericht kann die kurzlebigen Sprachschlüssel enthalten. | ein sauberer PC; bei Verdacht Gerät sperren |
| **Schadcode auf einem Admin-PC während einer offenen Admin-Sitzung** | Er kann im Namen des Admins handeln, bis auf die Konsolen-Dinge. | App auf dem Telefon, kurze Admin-Sitzungen, Warnungen, Journal, Notbremse |
| **Keine Attestation** | Der Server kann nicht prüfen, ob ein Geräte- oder Admin-Schlüssel wirklich im TPM liegt. | Admin-Setup nur direkt vom Eigentümer, Ernennung am Telefon; Attestation frühestens in Version 2 |
| **Keine Bestätigung je Unterschrift** (Windows Hello für den Admin-Schlüssel) | Ob das mit dem Admin-Schlüssel aus .NET geht, ist noch zu prüfen. | später |
| **Setups und Updates nicht signiert** | Codesignatur und das offline signierte Update-Manifest fehlen noch; Windows SmartScreen warnt. | SHA-256 jedes Pakets in einer `.sha256`-Datei, von Hand prüfen; Admin-Setup nur direkt vom Eigentümer |
| **Welche Authenticator-App, ist nicht prüfbar** | Der Server sieht nur sechs Ziffern. | seit 0.6.4 frei wählbar; der Code reicht nie allein |
| **Kein Discord-Bot** | Kein Abgleich mit der Gilde: Wer Discord oder die Gilde verlässt, bleibt im Funk, bis ein Admin sperrt. Warnungen kommen nur im Client an. | Admins sperren von Hand; Bot später |
| **Geteilte Adressen** (CGNAT, Mobilfunk) | Eine Sperre trifft alle hinter der Adresse; ein Angreifer kann den Durchlass belegen. | Vorrangspur, Durchlass, `entsperren --ip` |
| **Sehr viele Adressen** (viele IPv6-/48) | bleiben unter jeder Schwelle der Sperrliste | nur die globalen Töpfe |
| **Verwechselbare Namen** | Namen mit ähnlich aussehenden Zeichen aus verschiedenen Schriften werden nicht verglichen (Unicode UTS #39 noch offen). | der eindeutige SC-Hauptname daneben; der Admin vergleicht die Kennung |
| **Beobachter mit schnellerem Netzweg** | kann echte Pakete kopieren und die Originale unterdrücken | Er kann stören, aber weder mitlesen noch einsprechen. |

Außerhalb des Modells bleiben Geheimdienste, Angriffe auf Discord, auf den Anbieter der Authenticator-App oder auf Raid-Helper selbst, physischer Zugriff auf den Server und Hintertüren in .NET oder OpenSSL.

## C18 Prüfstand und offene Punkte

**Was geprüft ist.**

- Die Testsuite besteht in der Fassung 0.5.0 mit 1312 von 1312 Tests (Standzeile 02.10.2026 01:00; die Zahl der Testmethoden unter `tests\Funk.Tests` am Code-Stand `95aa78b` neu gezählt, 1312). In der Fassung 0.4.0 waren es 1153 von 1153. Die Baustränge (Stufe 1 mit Anmeldung und Admin-Nachweis, Stufe 2 mit Auftrag, Härtung mit Sperrliste, Einrichten und Umzug, ab 0.5.0 die Ausbaustufe S2 mit Phasen und Funker-Modus) wurden nach dem Bau gegengeprüft; die Befunde wurden je zuerst als roter Test nachgestellt und dann behoben (`PROJEKT.md`, Standzeilen).
- Viele Angriffe aus diesem Teil stehen als Tests da, unter anderem: Antrag ins falsche Konto, Namensbesetzen mit unsichtbaren Zeichen, Antragsflut aus vielen Adressen, Sammelsperre gegen wechselnde /64, Rücksprung der Uhr, falsche Passphrase, verändertes, abgeschnittenes und fremdes Umzugspaket, Pfadausbruch, Rückfall auf ein älteres Profil, fremd signierte Profile. Dazu kommen seit 0.5.0 die Angriffe aus C3.10: Phase ohne `ZUWEISEN`, mit abgelaufenem Auftrag und als Admin im Funker-Modus, Beauftragter gegen Admin, Phasenflut mit Stoß 2 und Bremse, zwei Leiter gegen `VERALTET`, Funker-Modus von Funker und Beauftragtem und mit fremdem Konto in der Nachricht, Selbstwahl-Sperre im Funker-Modus. Die Gegenprüfung von S2 (vier Befunde, je zuerst als roter Test) fand unter anderem, dass `NETZ_LOESCHEN` eine Phasenvariante ohne Feld stehen ließ und der Server danach nicht mehr startete; behoben, und `LageLaden` übergeht leere Varianten jetzt (Standzeile 01.10.2026, 23:05).
- Die Parser werden mit Zufallsmutationen geprüft: 200.000 Mutationen je Lauf, einmal 3.000.000 ohne Ausnahme. Der volle Lauf mit 10^7 Fällen je Parser (Prüfplan P-42) steht noch aus.
- Die Testvektoren für AES-GCM sind mit .NET gerechnet. Fingerabdruck, Einmalcode und Profilsignatur sind zusätzlich unabhängig von .NET nachgerechnet (SHA-256 mit Python und OpenSSL 3.5.7, ECDSA P-256 als eigene kleine Rechnung; 17 von 17 Prüfungen bestanden). Die unabhängige Gegenrechnung der Sprachvektoren (P-16) steht aus.

**Was noch fehlt.**

- **Fremde Durchsicht** des sicherheitskritischen Eigenbaus: Spezifikation, Protokollkern und Testvektoren sind als Prüfpaket gedacht (Protokoll 1.6). Wer es liest, ist offen.
- **Unabhängige Prüfung von S2.** Phasen, Funker-Modus, Ablage und Melde-Taste sind im Haus gegengeprüft (Standzeile 01.10.2026, 23:05); eine unabhängige Prüfung ist in `PROJEKT.md` nicht eingetragen. Am echten PC nicht geprüft sind das Symbol im Infobereich, die Meldezeile über Star Citizen und das Ablegen und Holen des Funkfelds mit dem Joystick. Nicht gemessen sind die Netzlast eines Phasenwechsels bei 40 Leuten (D3.5) und der Push `Mitgliedschaften` mit Phase an einen echten Client 0.4.0; mit dem Echo-Funker 0.4.0 ist der Übergang gemessen (Server 0.5.0 auf dem Zustand von 0.4.0 und zurück, jeweils die Echo-Probe bestanden).
- **Lizenz und Durchsicht.** Ob der Quelltext offen oder geschlossen bleibt, ist nicht entschieden (Anf. A58); jede eigene Quelldatei trägt seit 0.5.0 nur die Kopfzeile "JG TacRelay - Maintainer: Sarevoc", ohne Lizenz- oder Rechtsangabe (Standzeile 02.10.2026 01:00). Für die Sicherheit heißt das: Bei einer offenen Lizenz kann jeder Protokoll und Code prüfen; bei einer geschlossenen nur, wer den Code von Sarevoc bekommt, und das Prüfpaket lässt sich trotzdem gezielt weitergeben. In beiden Fällen hängt keine Sicherheitsaussage dieses Teils am Geheimhalten von Code oder Protokoll; geheim sind nur die Schlüssel.
- **Unter Linux ungeprüft:** Besitzer und Rechte beim Einrichten und Einspielen, Uhrprüfung über timedatectl, Binden unter Port 1024, die Prüfung des Verwaltungssockets. Ein Probelauf in einem Wegwerf-Container mit Debian 13, Debian 12 und Ubuntu 24.04 ist vorgesehen (`EINRICHTEN.md`, letzter Abschnitt).
- **Bekannte niedrige Befunde, noch nicht behoben:** `funk-admin einrichten` legt einen fehlenden TOTP-Schlüssel still neu an, auch wenn es schon Admins gibt; deren Zweitfaktor ist danach nicht mehr lesbar. Die Passphrase des Umzugspakets steht beim Eintippen sichtbar im Terminal. `sperren.log` hat keine Aufbewahrungsfrist. fail2ban sperrt bei IPv6 nur die Einzeladresse. Ein neuer Reserve-Serverschlüssel lässt sich mit `profil-erneuern` noch nicht eintragen.
- **Offene Entscheidungen:** wer den Profilschlüssel samt Reserve langfristig verwahrt (Protokoll, Offener Punkt 21), Signatur von Setups und Updates, TPM-Attestation, Windows Hello für den Admin-Schlüssel, Ernennung durch zwei Admins im Client (erst nach Version 1), der Discord-Bot, die Datenschutzerklärung.
- **Lücken melden:** Ein fester Meldeweg für Sicherheitslücken ist noch nicht festgelegt. Bis dahin gehen Meldungen direkt an Sarevoc (Vorschlag).

## C19 Im Ernstfall

| Lage | Sofort | Danach |
| --- | --- | --- |
| Admin-PC gestohlen oder Schadcode vermutet | an der Konsole `funk-admin admin-sperren --konto <id>`; oder ein anderer Admin sperrt das Konto im Client (Notbremse, frischer Code) | PC neu aufsetzen; ein "Reparieren" mit dem Setup reicht nicht, weil der Programmordner für Schadcode beschreibbar ist. Das alte Gerät sperren (`funk-admin sperren <alte gerätekennung>`), das neue per Antrag an der Konsole freigeben (Admin-Konten sind privilegiert: `funk-admin freigeben <kennung> --an-konto <id>`). Dann neuer Admin-Schlüssel und neues Geheimnis: `funk-admin admin-zuruecksetzen --konto <id>` (Schlüssel, Geheimnis und Notfallcodes fort, wieder vorgesehen), im Client "Verwaltung einrichten", `funk-admin admin-ernennen --konto <id> --kennung <neue kennung>`, Geheimnis abholen, erster Code |
| Telefon eines Admins verloren | mit einem Notfallcode anmelden; bei Diebstahl zuerst den Eigentümer informieren | Eigentümer: `funk-admin admin-einrichtung-neu --konto <id>`; der Admin holt ein neues Geheimnis ab und bekommt mit dem ersten Code neue Notfallcodes; das alte Geheimnis und die alten Notfallcodes gelten nicht mehr |
| Notfallcodes aufgebraucht oder verloren | - | `funk-admin notfallcodes --konto <id>`; die alten verfallen |
| Zweitfaktor wegen Fehlversuchen gesperrt | mit dem Admin sprechen: war er es selbst? | `funk-admin admin-entsperren --konto <id>` |
| Gerät eines Mitglieds gestohlen oder verdächtig | `funk-admin sperren <kennung>` (dauerhaft) oder `funk-admin entfernen <kennung>` | neuer Antrag vom neuen Gerät, Kennungsvergleich, Freigabe |
| Konto missbraucht | `funk-admin konto-sperren --konto <id>`; alle Sitzungen enden sofort | klären, dann `funk-admin konto-entsperren --konto <id>` |
| `VERDRAENGUNG_HAEUFIG` oder `KOPIERVERDACHT` | mit dem Mitglied klären, welche Geräte es benutzt | erst danach `funk-admin verdraengung-freigeben --konto <id>`; ein unbekanntes Gerät sperren |
| Mitglied kommt nicht herein (Sperrliste) | Adresse vom Mitglied geben lassen | `funk-admin sperrliste`, dann `funk-admin entsperren --ip <adresse>` |
| Falsche Phase geschaltet (ab 0.5.0) | im Client die Gegenrichtung ("Phase zurück") oder die Grundstellung schalten, wieder mit zwei Drücken; wer `ZUWEISEN` hat, darf es | Eine Phase ändert nur Felder und Sendenetze, nie Plätze; "Phase zurück" rechnet aus denselben Regeln und ist fast genau (ein "Hören aus" auf einem Netz, das der verlassenen Phase fehlte, kommt als "an" zurück, Protokoll 6.14). Spätestens 12 Stunden nach dem letzten Schalten fällt die Flotte von selbst auf die Grundstellung. |
| Ein Admin will wieder leiten, steht aber im Funker-Modus (ab 0.5.0) | im Client "Wieder Admin", ohne Code (Einstellungen, Abschnitt "Ich", oder Menü des Symbols im Infobereich; die Fußzeile zeigt nur "als Funker bis 04:00"). Das Symbol im Infobereich ist nur im Code und im Bildlauf geprüft, nicht an einem echten PC. | Der Modus endet auch um 04:00, bei einem Stufenwechsel und bei einer Sperre des Kontos. Einen Konsolenbefehl dafür gibt es nicht. |
| Antragsflut | im Verwaltungsteil "Alle ablehnen, die zu keiner Vormerkung passen" (löscht sie, sperrt nichts) | einzelne an der Konsole mit `funk-admin antraege` und `funk-admin ablehnen <kennung>`; ein PC, der immer wiederkommt, bewusst mit `funk-admin sperren <kennung>` |
| Sicherung aus `funk-admin sicherung` gestohlen | - | TOTP-Geheimnisse und Notfallcodes sind ohne den TOTP-Schlüssel nicht lesbar; die Klarnamen im Journal sind offen |
| Sicherung des ganzen Containers gestohlen | als verloren behandeln: Sie enthält alle Schlüssel | Folgerung, nicht als Anleitung ausgearbeitet: neuer Zweitfaktor für jeden Admin (`admin-einrichtung-neu`), neuer Serverschlüssel mit neuem Profil ohne den alten Pin, neues Betriebsgeheimnis |
| Eigentümer hat Client, PC und Telefon verloren | Die Konsole braucht weder Client noch Code: `pct enter <CT-Nummer>` auf dem Proxmox-Host, auf einem gemieteten Server die Shell als root | `funk-admin admin-zuruecksetzen --konto <id>`, dann neu einrichten wie oben |

Welcher Befehl was sperrt, verwechselt man leicht:

| Befehl | trifft | Wirkung |
| --- | --- | --- |
| `funk-admin sperren <kennung>` | ein Gerät | dauerhaft gesperrt (ENTZOGEN), Sitzung endet |
| `funk-admin entfernen <kennung>` | ein Gerät | entfernt, darf sich neu anmelden |
| `funk-admin konto-sperren --konto <id>` | ein Konto mit allen Geräten | gesperrt, alle Sitzungen enden; Plätze bleiben für ein Entsperren gespeichert |
| `funk-admin admin-sperren --konto <id>` | den Admin-Nachweis eines Kontos | keine Admin-Sitzungen mehr; funken geht weiter |
| `funk-admin entsperren --ip <adresse>` | eine Sperre der Sperrliste | die Adresse kommt wieder herein |
