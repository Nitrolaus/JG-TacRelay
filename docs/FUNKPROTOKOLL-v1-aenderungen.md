# Funkprotokoll v1 - Änderungen, Ablehnungen, offene Punkte

## Änderungen nach der Prüfung
- Kopf: Entwurf 2; Herkunft der Zahlen ergänzt (8.4 und 8.11 neu gerechnet mit PowerShell 7.6.6 / .NET 10.0.12 im Speicher; RFC-9000-Abruf über Zusammenfassungswerkzeug widersprüchlich, daher kein Beleg)
- 0: Präfix-Begriff (IPv4-Adresse bzw. IPv6-/64), Normalisierung von ::ffff-Adressen, KiB/MiB; UDP-Standardport 47443 statt 443; Bindport und öffentlicher Port getrennt, Welcome nennt den öffentlichen (Prüfer Netz 20, 21)
- 1.1: Z2 mit Warteschlangengrenze 20 ms und Mund-zu-Ohr-Ziel, Z6 ohne globale Aussperrung, Z7 mit Ausfallerkennung binnen 3 s in jeder Richtung, Z8 um Messbarkeit ergänzt
- 1.2: Grenzen erweitert (64 Durchsageziele, 16 Plätze und 8 Kennungen je Schiff, 3 aktive Geräte); Nicht-Ziel 'schneller Kopierer mit Unterdrückung' ergänzt; keine Freigabe über Discord
- 1.4: T3 berichtigt (Client-Zertifikat filtert nur einfache Scanner, Parser für jeden erreichbar), T4/T5/T6/T8/T9 an neue Gegenmaßnahmen angepasst, T10 kompromittierter Bot neu
- 1.5/1.6: Lehren S1 und S4 neu formuliert (signiertes Profil, Code kein Inhaber-Token), Relay-Thread als Eigenbau-Maßnahme, Review-Paket etwa 4000 Zeilen
- 2: Begriffe Sendeauswahl (Feldnummern), Festes Feld, STANDARD, Privilegiertes Konto, Anzeigeform, Vorrangspur, Kandidat; Frame-Nr und Sprechphase neu definiert
- 3.1: Serverprofil nur noch offline signiert (ECDSA P-256, P1363, Domänenpräfix, profilVersion steigend, gueltigBis nur Warnung), keine unsignierten Änderungen; Profilschlüssel fest im Client (Prüfer Krypto 6)
- 3.2: Angebot eines neuen Geräteschlüssels bei GERAET_GESPERRT (Prüfer Krypto 3.5)
- 3.3: Discord-Benutzername, eindeutige Anzeigenamen (NFKC, Suffix), Sperrgrund, Rechte-Abweichung, Löschung kontoloser Geräte nach 30 Tagen
- 3.4: Code-Richtung umgekehrt: Client zeigt Code, Nutzer tippt ihn in Discord; Bindung an Verbindung und Fingerabdruck, 5 min; Rückfrage im Client (VerknuepfungBestaetigen); Warntext, DM an den Inhaber; keine globale Registrierungssperre, nur Alarm; Offener Punkt 22 entschieden (Prüfer Krypto 1.1, 1.2)
- 3.5: Freigabe nur durch GERAETE_FREIGEBEN oder Admin-Werkzeug; Geschwister nur BESTAETIGEN/EINSPRUCH als Markierung; privilegierte Konten nur Admin-Werkzeug; RAIDLEITER mit Zusatzbestätigung; Freigabeansicht mit Benutzername und 'laut Client, ungeprüft'; Bootstrap ohne Sonderpfad Code->FREIGEGEBEN (Prüfer Krypto 2, 3, 5)
- 3.6: Lebenszyklus neu: ENTFERNT (nicht dauerhaft) vs. ENTZOGEN (nur GERAETE_FREIGEBEN); ältere Geschwister nur per Antrag; WARTET verfällt nach 24 h; 90 Tage ohne Sitzung zurück auf WARTET (Prüfer Krypto 3, 10)
- 3.7 neu: Discord-Abgleich alle 6 h und bei Raid-Helper-Import, nur eindeutige Antworten, höchstens 3 Sperren/h, nie ADMIN, automatische Aufhebung von ABGLEICH-Sperren (Prüfer Krypto 10)
- 3.8: Bot-Operationen neu (Verknuepfen, Mitgliedschaft, Meldung), Entscheidung gestrichen, serverseitige Grenzen je Discord-ID und global, Public Bot aus, Gilden verlassen, systemd-Einschränkungen, Logregeln; Admin-Werkzeug um Verknüpfen, Profil einspielen, Vorrangspur und Messwerte erweitert, Sicherung ohne Credentials (Prüfer Krypto 11, 14)
- 4.1: Handshake 5 s, Hello 3 s; Einstufung per Fingerabdruck direkt nach TLS; NoDelay Pflicht, TCP_NOTSENT_LOWAT 16 KiB, kleine Sendepuffer; Pflicht gleicher IP für TCP- und UDP-Bindung (Prüfer Krypto 1, Netz 6, 9)
- 4.2: Rahmengrenze in AKTIV 1 MiB (vor Hello 4096, Registrierung/Warteraum 1032); Fristen je Server-Anfrage; ein Write je Rahmen; Tunnel-Sprache mit Altersgrenze 60 ms und höchstens 3 Rahmen je Absender statt 5 gesamt (Prüfer Netz 6, 15; Funk 12)
- 4.3: Encoder UnsafeRelaxedJsonEscaping für feste Größen, null gilt wie fehlend, kein Schlüsselmaterial mehr in JSON
- 4.4/4.5: neue bzw. geänderte Nachrichten: Registrierung (0x0010), VerknuepfungBestaetigen (0x0016), GeraetEntfernen (0x0014), SendeauswahlSetzen (0x0030, Feldnummern), SatzAnwenden (0x0036), PlaetzeFreigeben (0x0037), RaidHelperEvents (0x0046), AufstellungGeaendert (0x0047), Meldung (0x4002), VoiceKeys binär (0x8002, 0x0070 frei); Ping-Antwort mit udpGesendet; Welcome mit signiertem Profil und neuen Grenzen; Bye mit ERSETZT, ENTFERNT, WARTEZEIT und durch-Gerät; NetzListe mit ausloeserStandard, durchsageNurTaste, Rollen-kapazitaet, sendenStandard und fest; Mitgliedschaften mit kontoVersion, platz, fest, sendeauswahl, stumm-Liste, letzterStand, entfallen; Praesenz mit Feldern, Stumm und Güte für ZUWEISEN, Umfang online plus Plätze; SendeFehler als Liste je Ziel mit ZIEL_ENTZOGEN sofort; Verschieben mit felderPatch, schiffId allein, ID 0 zum Entfernen, Kapazität am Endzustand, basisVersion je Konto; AufstellungAnwenden mit optionaler Version, erzwingen, verdraengt und Fehlerliste; RaidHelperImport nimmt Discord-Links; EmpfangsBericht je Sitzung; VerlustHinweis neu berechnet; UdpStatus mit Adresse
- 4.6: Verdrängung erst ab AKTIV_SYNC, ERSETZT bei gleichem Gerät, Alarm und VERDRAENGEN_GESPERRT bei Hin und Her; REGISTRIERUNG 5 min, WARTERAUM 15 min mit Bye WARTEZEIT; Ping fest alle 2 s, tot nach 6 s, Server trennt nach 10 s; Client schließt alte Verbindung vor Neuaufbau (Prüfer Krypto 4, Netz 1, 22)
- 4.8: Grenzen je Präfix, Vorrangspur (HMAC-Präfixe, 14 Tage, dauerhaft gespeichert), globaler Handshake-Topf nur CPU-Schutz 200/s, Unbekannte erst nach TLS gezählt, Registrierung und Verknüpfen je Präfix bzw. Discord-ID, Zusammenfassen idempotenter Zustandsmeldungen mit UEBERHOLT ohne Verstoß, Byte-Topf für Steuerrahmen und Sprache, Sperre je Gerät statt je IP (Prüfer Krypto 1, 8; Funk 4)
- 4.9: neue Codes UEBERHOLT, NUR_ADMIN_WERKZEUG, SELBSTWAHL_GESPERRT, FELD_FEST, VERDRAENGEN_GESPERRT; CODE_UNGUELTIG nur noch an den Bot
- 4.10 neu: Größen im ungünstigsten Fall mit Rechnung (Prüfer Netz 15, Funk 12)
- 5.1/5.4/5.5: Opus höchstens 400 Byte; Durchsage bis 64 Ziele; Datagrammlängen hoch 41..569, runter 45..477; BEGINN runter vom Server je Empfänger (Prüfer Krypto 8, Funk 17)
- 5.6: Frame-Nr als Abtastzähler innerhalb der Phase, Neuanlage nur bei BEGINN; Phase mit konstanten Zielen und Auslöser; Encoder max_data_bytes 400 (Prüfer Netz 7, 25)
- 5.7/5.8: AesGcm je Schlüssel nur in einem Thread, Dispose beim Verwerfen; Schlüssel binär, Grenzen der Löschbarkeit offen benannt (Prüfer Krypto 14)
- 5.9: Mindestlänge 41 statt 48; Schritt 0 Empfangsalter; zweistufiger Vorfilter (gebundene Adresse umgeht den IP-Topf); Fremdbudget je (Sitzung, Adresse) 60/s; Tunnel-Sitzungs-ID muss zur TLS-Verbindung passen; Schritt 8 als atomares Prüfen und Setzen (Prüfer Netz 2, Krypto 7, 12, 13)
- 5.10: Prüfung nur bei neuem Höchstwert; Challenge je Kandidat, 3 s, Wiederholung nach 250 ms; höchstens 2 Kandidaten ohne Verdrängung; Gegenprobe an die alte Adresse; Antwort nur von derselben Adresse; Kopierprüfung über authentische Duplikate; Umbindungsfrist 500 ms; Rückkehr bei neuem Höchstwert; Sicherheitsaussage berichtigt (Prüfer Krypto 7, Netz 11)
- 5.11: KEEPALIVE alle 2 s in AKTIV, auch beim Sprechen, plus bei Adressänderung; Abweichung von RFC 8085 3.5 begründet; serverZeitMs monoton (Prüfer Netz 3, 12)
- 5.12: Ausfallerkennung für hoch und runter über udpGesendet/udpEmpfangen und ausbleibende Echos, 3 KEEPALIVE als Probe, Tunnel binnen 4 s; Rückkehr erst nach 10 s sauberer Probe, Probenabstand verdoppelt nach Rückfällen (Prüfer Netz 3, 23; Funk 5)
- 5.13: Server-TOC-Prüfung von SOLL zu MUSS, Längenprüfung vor TOC (Prüfer Krypto 8)
- 5.14: ein Relay-Thread für alle Sprachpakete aus UDP und Tunnel, eigener Sende-Thread, Channel mit fester Kapazität, Empfangspuffer 512 KiB bis 1 MiB; Client mit je einem Sende- und Empfangs-Thread (Prüfer Netz 8)
- 6.2: Komfortzustand asynchron, weiterleitungsrelevante Änderungen synchron; ZUWEISEN vorgezogen; Gruppen-Commit 5 ms; strukturelles Teilen; kontoVersion und basisVersion je betroffenem Konto (Prüfer Netz 19, Funk 14)
- 6.3: Teilweiterleitung an die erlaubte Teilmenge mit SendeFehler je Ziel; ganz verworfen nur in benannten Fällen; Stumm je Zielnetz auch bei Durchsage; durchsageNurTaste; Ratenprüfung vorn mit Byte-Topf und Meldung RATE (Prüfer Funk 2, 10; Netz 24; Krypto 8, 15)
- 6.4: Phasenbuch im Relay-Thread; BEGINN und DurchsageStart je Empfänger beim ersten Paket; vom Server erzeugtes ENDE bei Herausfallen, Rechteverlust und Zielwechsel; abweichende Ziele in gleicher Phase gelten als neue Phase (Prüfer Funk 3, 9; Netz 17)
- 6.5/6.6: Global in der Erstkonfiguration nurTaste; Durchsage standardmäßig nur per Taste; nur NETZ-Ströme werden abgesenkt; DurchsageEnde nur Anzeige (Prüfer Funk 10, 19)
- 6.7: Aufstellung hat Vorrang (verdraengt), Kapazität am Endzustand, ERSETZEN nimmt Anfragenden und nicht gelistete ZUWEISEN-Konten aus, Überspringen höherer Stufen, vollständige Fehlerliste, Zielwert unter Numpad-Last (Prüfer Funk 2, 6)
- 6.9 neu: gemeinsame Validierung aller Feldlisten, STANDARD definiert, feste Felder, Sendeauswahl mit Vorgabe aus Rolle bzw. Aufstellung, Platzzustände BELEGT (10 min Frist) und RESERVIERT (12 h), letzterStand zur Übernahme, Kapazitätsregeln, Nachziehen offener Aufstellungseinträge (Prüfer Funk 1, 7, 8, 13, 16, 18; Krypto 15)
- 6.10 neu: Rangregel für ZUWEISEN, Aufheben von Stummschaltungen, sechs Regeln für die Rechteverwaltung, privilegierte Konten (Prüfer Krypto 9, 15; Funk 18)
- 6.11 neu: verbindliche Client-Regeln C1-C11 (Sendeauswahl, konstante Phase mit 250-ms-Stabilität, Auslöser, Umstellung mitten im Spruch, Zusammenfassen, Empfangs- und Enderegel mit 300 ms ab leerem Puffer und 60 ms Verdeckung, Pegel bei Mehrfachtreffern mit Mindestpegel, Rennen mit Pushes, Absenkung) (Prüfer Funk 1, 3, 4, 11, 15; Netz 17)
- 7.1: Schnellphase 2 min alle 2 s mit Neuauflösung, RFC 8305 mit 250 ms Versatz, Pin-Fehler beim Wiederverbinden zuerst 'falscher Host', optionale autoritative Auflösung, nur A-Eintrag, neue Zielwerte (Prüfer Netz 5, 9)
- 7.2: Vorrangspur und weitere Zustände dauerhaft gespeichert; harter Ausfall nach 6 s erkannt
- 7.4: Pacer in Leitungsbytes auf höchstens 80 % des Netto-Uploads, Warteschlange 20 ms, Strom-Abwurf statt Paketverlust mit Hysterese, SERVER_VOLL nur für Registrierungen und Unbekannte (Prüfer Netz 4, 16; Krypto 8)
- 7.5: Verstärkungstabelle an die neue Pfadprüfung angepasst; LimitCORE=0 und DOTNET_DbgEnableMiniDump=0; HMAC-Schlüssel per LoadCredential; Header-Redaction; Client-Abbilder bewusst hingenommen (Prüfer Krypto 14)
- 7.6: feste LXC-IPv4, FRITZ!Box-Priorisierung und nächtliche Neuverbindung, Vorposten nur mit erhaltener Absenderadresse, vertrauenswürdige Weiterleiter, MTU-Prüfung (Prüfer Netz 4, 5, 9, 10)
- 7.7 neu: Latenzbudget Mund zu Ohr mit Messverfahren (Prüfer Netz 14)
- 7.8 neu: Messwerte im Server (Zählerlücken, Jitter nach RFC 3550 6.4.1, Verwerfungen, Pfadwechsel) und im Client, Güteklasse für Raidleiter, Hinweis 'dein Upload verliert' (Prüfer Netz 12, 13)
- 8.3: TV-B und TV-D mit Hinweis auf BEGINN bzw. vollständige Empfangsprüfung; neue Negativfälle (40/44 Byte, fremde Tunnel-Sitzung)
- 8.4: Ping-Antwort mit udpGesendet neu gerechnet (85 B); VoiceKeys als binäre Anfrage (102 B) und Antworten (13 B) neu gerechnet; Negativfälle an die neuen Längengrenzen angepasst
- 8.6-8.8: Normalisierung im Server; Zusatzfall für P-05 mit zwei Threads; TOC-Tabelle um 400/401 Byte ergänzt
- 8.9: Global mit nurTaste, durchsageNurTaste; Fall 9 auf PTT geändert; neue Fälle 17-23 (VA auf Global und Durchsage, Teilweiterleitung bei Stumm, unbekanntem Netz und Entzug)
- 8.10 neu: Folge-Matrix F1-F13 für Übergänge (Prüfer Funk 20)
- 8.11 neu: Prüfvektor für die Profilsignatur mit zwei Pins
- 8.12: Prüfplan erweitert: P-02b, P-17 bis P-19, P-26 bis P-29, P-48; P-04, P-05, P-07, P-09 bis P-15, P-21 bis P-24, P-30, P-31, P-42, P-45, P-46, P-50 ergänzt (Prüfer Netz 18)
- Anhang: neue Abweichungen von den Recherchen (Code-Richtung, Ping, KEEPALIVE, UDP-Port, binäre Schlüssel, Relay-Thread)
- 9: Offene Punkte neu geordnet: 12 (A20) und 22 (Code-Richtung) aus Entwurf 1 entschieden, 21 (Profilschlüssel) zur Pflicht vor v1; neu: Discord-Einzelabfrage ohne Intent, RAIDLEITER-Geräte, FRITZ!Box-Messung, Kernel-Zeitstempel, RFC 8305 selbst bauen, UTS #39, Relay-Kapazität, Software-KSP und Abbilder, Web-Abgleich von RFC 9000

## Stufe 1 (30.09.2026): Anmeldung mit Namen, Admin-Nachweis, Mitglieder
Grundlage: der Entwurf zu Anmeldung und Sicherheit (P1-P13) und der Entwurf zur Raidleitung, Fassung 3 (N2, N6-N10, F3, F4, F6, F11-F15), die Anforderungen A37-A49; heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Rechte und Verwaltung" und Anhang A. Direkt in v1, weil noch kein Server im Betrieb ist.
- Kopf: Hinweis auf Stufe 1
- 1.1 Z5: Ausnahme für das TOTP-Geheimnis der Admins, nur als zweiter Faktor (P13)
- 1.4: T11 übernommener Admin-PC, T12 Code-Phishing, T13 Namensbesetzer, T14 Sicherungsdiebstahl auch als Container-Sicherung, T15 Dienstbenutzer (P11)
- 1.6: HKDF und HMAC-SHA-1 (nur TOTP) unter den Bordmitteln
- 2: Begriffe Antrag, SC-Name, Discord-Name, Admin-Schlüssel, Admin-Sitzung, Schritt-Bestätigung, Eigentümer, Vormerkung, Rollenkatalog, Lieblingsrolle, Untergilde, Zeichen, Journal, Beauftragung; Konto hängt an der Konto-ID, Discord-ID optional
- 3.3: `discordId` optional, `scNamen` (1-3, eindeutig über Konten und Vormerkungen), `discordName`, `eigentuemer`; `benutzername` ohne Bot leer, auf dem Draht `-`; Antragskonten; Eigentümer nie gelöscht (P2)
- 3.4: Weg B, Antrag ohne Code mit SC-Name und Discord-Name, Namen erst mit der Freigabe im Namensraum, höchstens 30 offene Anträge, AntraegeAblehnen (P3)
- 3.5: Freigabe im Client nur in Admin-Sitzung mit frischer Bestätigung; Freigabeansicht mit Namen, vergebenen Namen und Vorschlägen aus Vormerkungen; `vormerkungId` und `kontoId` (P4, F14)
- 3.6, 3.7, 3.8: Admin-Sitzung für alle Verwaltungshandlungen; Konten ohne Discord-ID ohne Abgleich; neue Befehle des Admin-Werkzeugs
- 3.9 neu: Admin-Nachweis (Schlüssel, Unterschrift, Zustände, Einrichtung, TOTP, Notfallcodes, Fehlversuche, Speicherung, Admin-Sitzung, frische Bestätigung, Pushes, Journal) (P5)
- 3.10 neu: Eigentümer (P6)
- 4.4, 4.5: 0x0017 reserviert, 0x0018-0x001F (ProfilSetzen, Journal, Rollenkatalog, Mitglied, Lieblingsrollen, Untergilde, Mitgliedskarte, AntraegeAblehnen), 0x0090-0x0094 (AdminNonce, AdminAnmeldung, AdminBestaetigen, AdminAbmelden, AdminEinrichtung); neue Felder in Welcome, Registrierung, GeraetStatus, GeraeteListe, GeraetEntscheiden, KontoVerwaltung (SCNAMEN), Mitgliedschaften (`konto`), Praesenz (`scName`, `untergilde`, `zeichen`); neue Meldungsarten, Warnungen an jedes ADMIN-Konto (P7, N2, N6-N10, F3, F4, F6, F11-F15)
- 4.8: Ratengruppen Profil, Mitgliedskarte, Admin; Grenzen für Zweitfaktor, Admin-Sitzung, Anträge und Mitglieder (P8)
- 4.9: NAME_VERGEBEN, ADMIN_SITZUNG_NOETIG, SCHRITT_BESTAETIGEN, ZWEITFAKTOR_FALSCH, ZWEITFAKTOR_GESPERRT, ANTRAEGE_VOLL, keiner trennt (P9)
- 4.10: Größen der neuen Nachrichten; Mitglied LISTE seitenweise
- 6.1, 6.10: Verwaltungsrechte nur in Admin-Sitzung, Regeln 7-10 (P10)
- 6.12 reserviert (Beauftragung, Stufe 2), 6.13 neu: Mitglieder, Rollen, Untergilden, Zeichen, Mitgliedskarte
- 8.12: P-49, P-51, P-52; 8.13-8.15 neu: TOTP- und Base32-Vektoren, Unterschrift des Admin-Nachweises, Verschlüsselung eines Geheimnisses (P12)
- 9: Punkte 28-32

**Abweichungen von den Entwürfen, bewusst:**
- AAD der Verschlüsselung: Konto-ID und Generation statt Fingerabdruck des Admin-Schlüssels. Ein Admin hat bis zu 3 Schlüssel (PC, Laptop), das Geheimnis gehört zum Konto; die Generation verhindert das Zurückspielen eines alten Chiffrats nach `admin-einrichtung-neu`.
- Eigener Zweck `EINRICHTUNG` für den ersten Code (statt ANMELDUNG), damit eine Unterschrift für die Einrichtung nie eine Admin-Sitzung öffnet.
- GeraetStatus und GeraeteListe tragen die Vorschläge als `vormerkungen` (Liste mit `ueberScName`) statt eines einzelnen Treffers; die Vorauswahl bei genau einem Treffer über den SC-Namen macht der Client.
- AntraegeAblehnen als eigene Nachricht 0x001F statt als Operation von GeraetEntscheiden, weil sie nicht an einem Gerät hängt.
- Mitglied EINFUEGEN nennt fehlerhafte Zeilen in der Antwort (`zeilen`), nicht in `Fehler.details`, weil 200 Zeilen mit Klartext die 4 KiB der Details sprengen können.
- Mitglied LISTE seitenweise zu 250 (`ab`, `weiter`), weil 1000 Einträge im ungünstigsten Fall die Rahmengrenze überschreiten würden (4.10).
- `SCHRITT_BESTAETIGEN` trägt in `details.bis` das Ende der Admin-Sitzung, damit der Client weiß, ob sich ein Code noch lohnt.
- Konsole: `admin-einrichtung-neu --anzeigen` zeigt Schlüssel und otpauth-Link selbst, `zweitfaktor-bestaetigen --code` schließt an der Konsole ab, `notfallcodes` erzeugt neue; so geht die Einrichtung auch ohne Verwaltungsteil im Client.
- Die Regeln einer heiklen Handlung prüft der Server vor der frischen Bestätigung, damit ein ohnehin verbotener Schritt nicht nach einem Code fragt.
- Name und SC-Namen eines anderen ADMIN-Kontos nur an der Konsole ("ADMIN-Konten im Client nur sperren"), die eigenen setzt jeder selbst.
- Fragen an Sarevoc mit Voreinstellung: Ernennung nur an der Konsole (Frage 1); Rollen auf der Mitgliedskarte für alle, abschaltbar mit `mitgliedskarte.rollenFuerAlle` (Frage 3); Untergilden pflegt nur ein Admin (Frage 5); RAIDLEITER behält `ZUWEISEN` bis Stufe 2.

**Nicht gebaut in Stufe 1:** Beauftragung (nur Datenmodell und reservierte Nummer), Flotte übernehmen (Paket), Selbsteinteilung, Kontenverzeichnis, die Clientseite (Verwaltungsteil mit Admin-Schlüssel im TPM, QR-Code, Code-Fenster, Setup-Seite "Deine Namen") und der Probefunk mit Test-Admin.

**Gegenprüfung Stufe 1 (30.09.2026), nachgezogen:**
- 3.6, 3.9, 4.5 (0x0014), 6.10 Regel 9: Fremde Geräte privilegierter Konten, also auch die anderer Admins, entfernt oder sperrt im Client niemand; das geht nur an der Konsole (`NUR_ADMIN_WERKZEUG`). Vorher konnte ein Admin mit frischem Code die Geräte eines anderen Admins entfernen oder für immer sperren (Fingerabdruck gesperrt). Die Vorlage erlaubt gegen einen anderen Admin im Client nur die Notbremse "Konto sperren".
- 3.10: Das Konto des Eigentümers bleibt ADMIN. `stufe` mit einer anderen Stufe und `admin-entziehen` verweigert auch die Konsole, bis ein anderes ADMIN-Konto Eigentümer ist. Vorher blieb das Merkmal an einem herabgestuften Konto hängen, und `admin-entziehen` hätte den Admin-Nachweis schon entfernt, bevor es an der Stufe scheiterte.
- 4.5 (0x0094): `uri` höchstens 512 statt 300. Ein Name aus 32 Zeichen mit je 4 UTF-8-Byte ergibt prozentkodiert 508 Zeichen; der Server hätte das Geheimnis als abgeholt vermerkt, der Client die Antwort mit `ZU_GROSS` verworfen.
- Offen, nicht eingebaut: Sarevocs Antworten vom 30.09.2026 zu Frage 3 ("Wer ist da" zeigt den Discord-Namen als Namen; Untergilde, Zeichen und Rollen für alle nur mit Freigabe je Nutzer, ab Werk an) und Frage 4 (den Rollenkatalog legt auch die Führung oder Einsatzleitung an). Stufe 1 folgt hier noch den Voreinstellungen der Entwürfe (`mitgliedskarte.rollenFuerAlle`, Katalog nur in der Admin-Sitzung). Seit Teil 2 eingebaut, siehe unten.

## Stufe 1, Teil 2 (30.09.2026): Client, Antworten 3 und 4
Grundlage: dieselben Entwürfe samt ihrer Sicherheitsprüfung und Sarevocs Antworten vom 30.09.2026, die der Vorlage vorgehen. Der Client ist nicht Teil des Drahtprotokolls; hier stehen nur die Stellen, an denen sich Draht oder Server ändern, und die Entscheidungen, die für beide gelten.
- 3.3: `discordName` ohne täuschende Zeichen (Cf, Zl, Zp, Co, Cn, andere Leerzeichen als U+0020, doppelte Leerzeichen), weil er seit Antwort 3 für alle sichtbar ist; Clients zeigen den eindeutigen Hauptnamen daneben
- 4.4, 4.5: `ProfilSetzen.karteFreigeben` (Antwort 3, Standard an), `Mitgliedschaften.konto.karteFreigeben`; Praesenz mit `discordName` für alle, Untergilde und Zeichen nach Freigabe; `Mitgliedskarte` mit `freigegeben` und `UNBEKANNT` für Konten außerhalb der Praesenz; `Rollenkatalog` SETZEN zum Ergänzen auch mit `ZUWEISEN` oder `GLOBAL_SENDEN` (Antwort 4)
- 4.8: AdminNonce in einem eigenen Eimer (1/s, Burst 3), getrennt von den übrigen Admin-Nachrichten (Offener Punkt 30)
- 6.13: Rollenkatalog und Mitgliedskarte nach den Antworten 3 und 4
- 8.12: P-51 mit der Ausnahme aus Antwort 4, P-53 neu
- 9: Punkte 28 bis 30 nachgeführt

**Client, Stufe 1:**
- Anmeldung mit SC-Name und Discord-Name: Setup-Seite "Deine Namen" schreibt `anmeldung.json` neben die Einstellungen, der Client übernimmt die Namen und fragt beim Antrag "stimmt das?"; der Warteraum zeigt die Gerätekennung groß mit "Kopieren" und dem Satz für Discord.
- Serverprofil und Profilschlüssel kommen mit dem Client (Profilschlüssel in den Metadaten der Programmdatei, `profil.json` im Programmordner). Eine Eingabe gibt es im normalen Client nicht mehr; der Testzugang bleibt für die Entwicklung.
- "Wer ist da" (Knopf im Hauptfenster und Strg+6), "Ich" unter Einstellungen mit Namen, Lieblingsrollen, Zeichen und Freigabe, Zeichen selbst gezeichnet (Lorbeerkranz in drei Formen zur Wahl, Führung als Platzhalter, Stern für Stufe 2).
- Verwaltungsteil als eigene Programmdatei `verwaltung\Funk.Verwaltung.dll`, nur im Admin-Setup. Der Client verweist nicht auf sie und lädt sie nur, wenn ihr SHA-256 zu dem passt, der beim Bau in TacRelay.dll steht. Admin-Schlüssel nur im TPM, ohne Rückfall; QR-Code selbst gebaut; die Code-Karte fragt nach `SCHRITT_BESTAETIGEN` und wiederholt die Handlung.

**Abweichungen von der Vorlage, bewusst:**
- Ein ADMIN-Konto ohne Admin-Sitzung darf den Katalog ergänzen wie die Führung, weil es deren Rechte trägt; Umbenennen, Ersetzen und Leeren braucht weiter die Admin-Sitzung. P-51 nennt die Ausnahme.
- `Mitgliedskarte.freigegeben` ausdrücklich statt aus fehlenden Rollen geschlossen: Mit `mitgliedskarte.rollenFuerAlle` aus fehlen Rollen auch bei freigegebenen Karten, und der Client soll "nicht freigegeben" von "nur für die Einsatzleitung" unterscheiden.
- Karten nur von Konten in der Praesenz (Befund beim Einbau von Antwort 3): Vorher bekam jedes Konto zu jeder Konto-ID Namen und Discord-Namen, auch von Konten, die nie online waren; so ließ sich die Mitgliederliste abfragen.
- Die Prüfsumme des Verwaltungsteils schützt vor vertauschten Fassungen, nicht vor Schadcode im Benutzerkonto, der auch die Programmdatei tauschen kann. Durchgesetzt wird allein am Server (Admin-Sitzung, Admin-Schlüssel, Zweitfaktor).

**Nicht gebaut in Teil 2:** Probefunk mit Test-Admin-Schlüssel, Journal im Verwaltungsteil, geführte Einrichtung (M6); der Admin-Schlüssel im echten TPM ist ungeprüft.

## Nachtrag A53 und Fassung 0.2.0 (01.10.2026): Discord-Name eindeutig und nur vom Admin
Grundlage: Sarevocs Entscheidung A53 vom 30.09.2026 abends. Der Discord-Name ist seit Antwort 3 der Name in "Wer ist da"; bis hier konnte jedes freigegebene Mitglied ihn mit `ProfilSetzen` frei setzen, auch auf den eines anderen. Das war eine Täuschung, gegen die der eindeutige Hauptname daneben allein nicht reichte.
- 3.3: `discordName` ist serverweit eindeutig, mit denselben Vergleichsregeln wie die SC-Namen (NFKC, `ToUpperInvariant`, also auch Breitschrift und andere Schreibung), über alle Konten und Vormerkungen. Offene Anträge stehen nicht im Namensraum. Setzen darf ihn nur ein Konto mit `KONTEN_VERWALTEN` in einer Admin-Sitzung (`KontoVerwaltung DISCORDNAME`, ohne frische Bestätigung wie SCNAMEN) und die Konsole (`funk-admin discordname`). Andere ADMIN-Konten und den Eigentümer ändert nur die Konsole, das eigene Konto der Admin selbst.
- 3.4, 3.5: Ein Antrag mit dem Discord-Namen eines anderen wird angenommen (er besetzt nichts), aber nicht freigegeben: `GeraetEntscheiden FREIGEBEN` ergibt `NAME_VERGEBEN` mit einem Satz, wem der Name gehört, und `details: {name, kontoId? | vormerkungId?}`; die Konsole sagt dasselbe. Mit der Vormerkung, die den Namen trägt, geht die Freigabe. Beim zweiten Gerät eines Mitglieds (`kontoId`) bleibt der Discord-Name des Kontos und der des Antrags spielt keine Rolle.
- 4.5: `KontoVerwaltung` mit `op: "DISCORDNAME"`, `wert: str | null` (leer oder null leert). `ProfilSetzen.discordName` ändert nichts mehr: Derselbe Wert wie bisher wird geduldet, ein anderer ergibt `KEIN_RECHT` ("Den Discord-Namen ändert nur ein Admin"), und dann bleiben auch die SC-Namen, wie sie waren. `GeraetStatus` und `GeraeteListe` tragen `discordNameVergeben?: true`, nur für Admin-Sitzungen (wie die Vorschläge, P-44).
- 6.13: Vormerkungen und "Liste einfügen" halten auch die Discord-Namen eindeutig; eine Zeile mit vergebenem oder doppeltem Discord-Namen steht mit Klartext da, und nichts wird angelegt.
- 8.12: P-54 neu.
- Server: Der Namensraum der Discord-Namen wird beim Laden gebaut, nicht gespeichert. Trägt ein älterer Stand zwei gleiche Discord-Namen, warnt der Start (ohne Klarnamen im Logbuch); ändern geht an der Konsole.

**Fassung 0.2.0 und Übergang von 0.1.0:**
- 4.7: Der Client meldet in Hello die Fassung seines Baus (Directory.Build.props), nicht mehr eine feste "0.1.0". Clients 0.1.0 melden weiter "0.1.0"; mit `mindestClientVersion` "0.1.0" verbinden sie wie bisher (gemessen mit den Programmen von 0.1.0 gegen diesen Server).
- 7.6: Eine server.json ohne `totpSchluessel` (aus 0.1.0) nimmt `pruefen` und der Start nicht mehr still mit dem Standardpfad an, sondern nennen die drei Schritte (Schlüssel erzeugen, Feld eintragen, Unit mit LoadCredential).

**Client:** "Ich" zeigt den Discord-Namen nur an, mit dem Hinweis, dass ein Admin ihn ändert, und schickt ihn nicht mehr mit. Im Verwaltungsteil trägt der Admin ihn auf der Karte eines Kontos ein; die Anträge warnen vorher, wenn er vergeben ist. Lorbeerkranz in der Form Klassisch (A53).

**Gegenprüfung des Nachtbaus (01.10.2026):**
- 3.3: Die Sperre gegen unsichtbare Zeichen ging nur über die Unicode-Klassen. Default_Ignorable_Code_Point aus Mn und Lo kamen durch (Verbinder U+034F, Variantenwähler, Hangul-Füller U+3164; die Füller gelten als Buchstaben, also auch im SC-Namen). "Fritz/DC" mit einem Füller dahinter sah aus wie "Fritz/DC", hatte aber einen eigenen Schlüssel und kam als Antrag durch die Freigabe. Jetzt sind alle Default_Ignorable_Code_Point und U+2800 in SC- und Discord-Namen `UNGUELTIG`.
- 3.3: Der Namensschlüssel ließ das große ẞ stehen, `ToUpperInvariant` auch das kleine ß; "groß" und "GROẞ" waren verschiedene Namen. Über alle Zeichen bis U+1FFFF gemessen die einzige solche Lücke; ẞ zählt jetzt wie ß. Den gespeicherten Namensraum der Anzeigenamen (`namen` in konten.json) zieht das Laden auf die neue Rechnung nach, und zusammenfallende Vormerkungen oder Katalogrollen lassen Laden und "Liste einfügen" nicht mehr scheitern.

## Stufe 2, Server und Protokoll (Fassung 0.3.0, 01.10.2026): Auftrag für einen Abend, Einheiten, Belegung
Grundlage: Sarevocs Antworten A36-A40, A43, A46-A50, A53, A54 (heute `docs\entwurf\ENTSCHEIDUNGEN.md`, Anhang A), der Entwurf zur Raidleitung, Fassung 3, mit Bauplan S1 und Sicherheitsprüfung (dort Kapitel "Zuteilung"), der Prototyp vom 30.09.2026. Gebaut ist der Teil Server und Protokoll; die Clientseite (Brett, Einsteigen, Bereich Einsatzleitung und Flottenseite im Verwaltungsteil) folgt.
- 2: Raidleiter heißt in der Oberfläche "Führung" und teilt nicht mehr ein; neu Einsatzleitung, Beauftragung, Einheit. Wo die Spezifikation fürs Einteilen "Raidleiter" sagte (4.5, 4.9 FELD_FEST, 6.7, 6.9), steht jetzt Einsatzleitung.
- 4.4, 4.5: `Beauftragung` 0x0017 (N1, Pflichtbereich, war reserviert), `KontenVerzeichnis` 0x0038 (N4), `Belegung` 0x4003 (optionaler Push), `Verwaltung PAKET` (N5), `SELBSTWAHL {an, nachzuegler?}` (F9); neue optionale Felder: `Welcome.konto.beauftragung` und `Mitgliedschaften.konto.beauftragung` (F2, F3), `NetzListe.einstellungen.selbstwahlDurch/selbstwahlBis/nachzuegler` (F9), `schiffe[].art/typ`, `plaetze[].mindest` (F5), Praesenz-Details `quelle`, `durch`, `seit`, `letzterPlatz` (F4), `AufstellungListe.ersteller` (F10); `Meldung{BEAUFTRAGUNG}` (F7); `AufstellungGeaendert.durch` trägt den Urheber (K3); `Verschieben` mit `rolleId: 0` merkt den letzten Stand.
- 4.7: Clients 0.2.0 bleiben verbunden; neue Werte nur in neuen Nachrichten oder im optionalen Bereich. Gemessen mit funk-echo und echo-probe von 0.2.0 gegen einen Server 0.3.0.
- 4.8: Beauftragung im Topf der Verwaltung, KontenVerzeichnis 1 je 5 s, Grenzen der Aufträge und des Pakets.
- 4.10: neue Zeilen, gemessen statt geschätzt; Praesenz voll liegt im ungünstigsten Fall bei Faktor 1,73 (siehe unten).
- 6.1: RAIDLEITER ohne `ZUWEISEN`, Spalte Auftrag; wirksame Rechte zur Zeit. 6.2: Aufträge im Lagezustand, je Sitzung mit `von` und `bis` im Schnappschuss. 6.3: Rechte zur Zeit des Pakets. 6.7: Ausnahme bei ERSETZEN auch für Beauftragte, Überspringen nur noch für ADMIN-Ziele.
- 6.9: Einheiten; Sperre der Selbstwahl mit Urheber, Frist und Nachzüglern; Nachziehen endet mit dem Auftrag des Anwenders.
- 6.10: Rangregel neu (ADMIN-Ziele nur durch ADMIN, sonst FUNKER und RAIDLEITER), auch beim Stummschalten; Regel 1 ohne Auftragsrechte.
- 6.12: die Regeln 1-10, wie gebaut. 6.13: Zeichen `EINSATZLEITUNG` für einen laufenden Auftrag.
- 8.9: Fälle 24-27; 8.12: P-55 und P-56; 9: Punkte 28, 32 nachgeführt, 33 neu.

**Server:** `Konto.RechteZu(jetzt)` am Lage-Konto ersetzt das Lesen von `Rechte` an allen Stellen, die einteilen, senden oder Pushes nach Rechten verteilen. Der Fristen-Takt prüft jede Sekunde und schreibt nur, wenn etwas fällig ist; die Belegung wird nach jedem Commit und alle 10 s geprüft. `sicherung` schreibt `lage.json` ohne Aufträge. Beim Laden eines Stands von 0.2.0 bekommt jede Aufstellung ihren letzten Speichernden als Ersteller. Die alte Startvorlage als Paket (`Flottenvorlagen`, 15 Schiffe, 52 Netze mit Global, eine Rolle, 82 Schritte) mit sechs Annahmen, die gelb bleiben, bis jemand sie prüft.

**Admin-Werkzeug:** `auftrag`, `auftrag-entziehen`, `auftraege`, `flotte-vorlage` (nur auf eine leere Flotte), `einstellung --nachzuegler an|aus`; Zeiten in Ortszeit Europe/Berlin.

**Übergang von 0.2.0:** Ein Zustandsverzeichnis, das die Programmteile von 0.2.0 selbst geschrieben haben, liegt als Testdaten bei (`tests\Funk.Tests\Daten\zustand-0.2.0`, Herkunft dort); 0.3.0 liest und schreibt jede Datei ohne Verlust. Gewollt anders: RAIDLEITER verliert `ZUWEISEN` (die berechneten `rechte` in `konten.json` und `lage.json` ändern sich), eine Sperre der Selbstwahl aus 0.2.0 bleibt ohne Urheber und Frist bestehen, bis sie jemand aufhebt. `server.json` braucht nichts Neues; `mindestClientVersion` bleibt.

**Abweichungen von der Vorlage, bewusst:**
- F3: `Mitgliedschaften.konto.rechte` bleibt bei den dauerhaften Rechten; der Auftrag steht daneben in `konto.beauftragung` mit `aktiv`. So sieht der Client, woher ein Recht kommt, und zeigt "Einsatzleitung bis 04:00 (von Nordwind)".
- N4: `KontenVerzeichnis` nennt den Hauptnamen (`scName`) statt aller SC-Namen und dazu Discord-Name, Platz und Untergilde; weitere SC-Namen zeigt die Mitgliedskarte. Weniger Daten in einer Liste, die jeder mit `ZUWEISEN` abruft.
- N5: Antwort auf `PAKET` ist `{}` wie bei jeder Verwaltung; die neue Version kommt mit der NetzListe.
- F4: Praesenz-Details ohne `scNamen`, aus demselben Grund wie bei N4.
- F7: `Meldung{BEAUFTRAGUNG}` trägt zusätzlich `durch`, `grund` und bei BEENDET `warum`.
- Regel 5: Der Fristen-Takt prüft jede Sekunde statt "spätestens 5 s"; für die Wirksamkeit spielt er keine Rolle, sie hängt an der Zeit je Aktion und Paket.
- Regel 6: Aufträge von der Konsole (`durch` 0) hängen an keinem Konto und enden nicht mit einem Erteiler.
- Neu ohne Vorlage: `Belegung` 0x4003 als eigener Push. K1 verlangte, dass der Client BELEGT und RESERVIERT zählt wie der Server; die Praesenz kennt aber nur Konten mit Sitzung oder Platz und keine Kapazitäten. Der Server rechnet die Belegung, wie er die Kapazität prüft, und schickt sie allen.

**Befund:** Praesenz voll mit allen Feldern aus Stufe 1 und 2 misst 605 KB (Stufe 1 allein 593 KB; 4.10 schätzte 450 KB), Faktor 1,73 statt über 2 unter der Rahmengrenze. Die Grenze selbst hält; P-02b meldet es als Bericht. Offener Punkt 33.

**Nicht gebaut:** die Clientseite der Stufe 2; der Bot-Dienst (die Nachricht in den Admin-Kanal bleibt eine Logzeile).

**Gegenprüfung (01.10.2026, nach Server und Client 0.3.0), je Befund mit Test erst rot, dann grün (`RaidleitungGegenpruefungTests`, P-57):**
- 4.5, 6.9: Die Wechselsperre ließ sich in zwei Schritten umgehen: `SatzAnwenden` mit `rolleId: 0` gab den Platz auf, danach stieg man als Nachzügler ohne Platz woanders ein. Jetzt ist das Aufgeben bei wirkender Sperre selbst ein Wechsel (`SELBSTWAHL_GESPERRT`, außer mit `ZUWEISEN`). Anders als 0.2.0: Dort durfte man seinen Platz auch bei gesperrter Selbstwahl verlassen.
- 4.5 Praesenz, 6.9: Lief die Frist eines Platzes ohne Aktion ab (Wiederverbindung 10 min, Reservierung 12 h), ging keine Praesenz hinaus. Das Brett zeigte die Person weiter im Block und lehnte Ablagen dort als "voll" ab, obwohl der Server den Platz frei sah. Jetzt meldet der 10-s-Takt solche Konten als Delta (`Schnappschuss.PlatzzustandGewechselt`).
- 4.7: Ein Client 0.3.0 an einem Server 0.2.0 schickte 0x0038 (das Brett beim ersten Zeigen) und 0x0017 (Verwaltungsteil); ein Server 0.2.0 kennt beide nicht und trennt mit `PROTOKOLL`. Jetzt stellt der Client sie nur, wenn der Server in Welcome mindestens 0.3.0 meldet, sonst antwortet er sich selbst mit `VERSION` und einem Satz.
- Client: Ein Nachzügler, der länger als 15 Minuten ohne Platz blieb, ließ das Brett danach jede Sekunde ganz neu rechnen; jetzt einmal.

## Ausbaustufe S2, Server und Protokoll (Fassung 0.5.0, 01.10.2026): Einsatzphasen, "Heute als Funker"
Grundlage: der Entwurf zur Ausbaustufe S2 (Zusammenführung dreier Entwürfe, Teil B; heute `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Zuteilung"), die Anforderungen A60 und A61 (dort Anhang A), Sarevocs Wunsch vom 01.10.2026 (Phasen per Joystick als Nächstes; ein Admin muss eingeteilt werden können, ein anderer leitet). Gebaut ist der Teil Server und Protokoll (B1, B3), dazu im ClientKern `Lagebild.HatRecht` mit dem Funker-Modus; die Clientseite (A, B2, C) folgt.
- 4.4, 4.5: `Funkermodus` 0x003C und `PhaseSchalten` 0x003D (Pflichtbereich, Typnummern aus dem Entwurf, in main d45e33f frei), `PhaseGeschaltet` 0x4004 (optionaler Push, auch nach Welcome); `Verwaltung PHASE_SETZEN` und `PHASE_LOESCHEN`, einzeln und in `PAKET`; neue optionale Felder `NetzListe.phasen`, `Mitgliedschaften.phase`, `konto.alsFunkerBis` (Welcome, Mitgliedschaften), `alsFunker` (Praesenz-Details, KontenVerzeichnis). Kein neuer Wert in `Recht`, `Stufe`, `MitgliedschaftsQuelle`, `Fehlercode`.
- 4.7: Clients 0.4.x bleiben verbunden; ein Client 0.5.0 stellt 0x003C und 0x003D nur an Server ab 0.5.0 (`Versionen.ServerFassungFuer`). Rückweg auf 0.4.x verliert Phasen, laufende Phase und Funker-Modus beim ersten Schreiben. Hin- und Rückweg gemessen mit den Programmen des Gesamtpakets 0.4.0 (20261001_1539) und dem Debug-Bau 0.5.0: Clients 0.4.0 bleiben am Server 0.5.0 über NetzListe mit `phasen` und den Rückfall (0x4004) verbunden, Echo-Probe 0.4.0 und 0.5.0 BESTANDEN; Server 0.4.0 startet auf dem Zustand von 0.5.0, Echo-Probe 0.4.0 BESTANDEN.
- 4.8: Funkermodus 1/s, Burst 2; PhaseSchalten 1 je 2 s, Burst 2; Grenzen der Phasen.
- 4.10: NetzListe mit Phasen gemessen: 323 KB im ungünstigsten Fall (5 Phasen, 512 Variantenfelder), 66 KB mehr als ohne Phasen.
- 6.1, 6.10: Funker-Modus nimmt die Rechte der Einsatzleitung; die Rangregel lässt `ZUWEISEN` auf einen ADMIN im Funker-Modus wirken; Stummschalten bekommt eine eigene Regel (unverändert: ADMIN-Ziele nur durch ADMIN).
- 6.9: Jeder Weg zum Platz nimmt die laufende Phase; eine Sperre der Selbstwahl setzt nur, wer `ZUWEISEN` hat.
- 6.12 Regel 7 gilt auch beim Beginn des Funker-Modus. Neu 6.14 Einsatzphasen und 6.15 "Heute als Funker".
- 8.12: P-58 bis P-60; P-19 (Rangmatrix) mit Funker-Modus 84 statt 54 Fälle. 9: Punkt 34.

**Server:** Lage-Konto `AlsFunkerBis`, `RechteZu` und der Heißpfad (`Schnappschuss.RechteAn`) nehmen die Rechte der Einsatzleitung heraus; `Rechteregeln.DarfZuweisen(stufe, ziel, jetzt)` an allen sieben Stellen (Verschieben, PlaetzeFreigeben, Aufstellung: Überspringen, ERSETZEN, Verdrängen, Schützen, Nachziehen) und im Phasenschalter, `DarfStummschalten` mit der alten Regel. `Einsatzphase` mit Varianten im Schnappschuss (gespeichert in `lage.json` unter `phasen`), die laufende Phase im Nebenstand (`nebenstand.phase`); `Feldregeln.AusRolle` mit Phase an allen fünf Aufrufen, `PhaseUmstellen` und `PhaseAuswahl` für die Regeln 5 bis 7. Fristen-Takt: Ende des Funker-Modus und Rückfall nach 12 h (`FristFaellig` kennt beide). `sicherung` lässt `alsFunkerBis` weg wie die Aufträge; Phasen und laufende Phase bleiben, denn die Felder passen zu ihr.

**Übergang von 0.4.0:** Ein Zustandsverzeichnis, das die Programmteile von 0.4.0 selbst geschrieben haben, liegt als Testdaten bei (`tests\Funk.Tests\Daten\zustand-0.4.0`, Herkunft dort, mit Aufträgen, Einheiten mit Art, Sperre ganz, Aufstellung mit Ersteller, Admin mit Platz); 0.5.0 liest und schreibt jede Datei ohne Verlust, neu kommen nur `alsFunkerBis: 0` je Konto und `phasen: []`. `server.json` braucht nichts Neues.

**Abweichungen vom Entwurf, bewusst:**
- `NetzListe.phasen[].varianten[{rolleId, felder[]}]` steht flach als `phasen[].felder[{rolleId, ...}]`: Als Varianten läge eine Phase in `Verwaltung PAKET` auf Tiefe 9, und 4.3 erlaubt 8; der Server trennte das Paket mit `PROTOKOLL` (gemessen im Drahttest). Die Grenze anzuheben hätte geholfen, aber dann bekäme ein Client 0.4.x, der eine tiefere `Ablage` eines neuen Clients liest, `PROTOKOLL`. Folge: Eine Variante hat mindestens ein Feld.
- `PHASE_LOESCHEN` braucht eine frische Bestätigung wie die übrigen LOESCHEN-Operationen (im Entwurf nicht genannt).
- Ändert `PHASE_SETZEN` die laufende Phase, stellt der Server sofort um; die Grundstellung lässt sich nicht löschen; Phasennummern dürfen Lücken haben (der Client wählt "die nächste").
- `SatzAnwenden` in einer Phase mit Variante: Die Sendeauswahl kommt aus dem Sendevorschlag der Phase, nicht aus dem Satz (seine Feldnummern stammen aus einem anderen Netzbild); offene Zusatznetze rücken auf das nächste freie Feld, wenn ihres belegt ist.
- Ein zweites "Heute als Funker" verlängert nicht; die Antwort nennt das bestehende Ende.
- Neu ohne Vorlage: Eine Sperre der Selbstwahl setzt nur, wer `ZUWEISEN` hat (6.9). Sonst setzte ein ADMIN im Funker-Modus mit `NETZE_VERWALTEN` in der Admin-Sitzung eine Sperre, die keinen Augenblick wirkt.

**Nicht gebaut:** die Clientseite (Teil A, B2, C des Entwurfs), dazu gehören auch die Phasen in der Sicherung der Flotte (`Flottenentwurf.AlsNetzListe`); eine Konsole für Phasen. Nicht mit echten Programmen 0.4.0 gemessen: `Mitgliedschaften.phase` an einen Client 0.4.0 (in der Messung saß niemand auf einem Platz, den die Phase änderte; abgedeckt nur durch die Regel, dass Clients unbekannte Felder übergehen).

**Gegenprüfung S2 (01.10.2026):** 6.14: `NETZ_LOESCHEN` ließ eine Variante ohne Feld stehen, wenn das gelöschte Netz ihr einziges war (Konsole `netz-loeschen`, Verwaltungsteil 0.4.0); beim nächsten Start lehnte der Server den gespeicherten Stand ab und startete nicht. Jetzt fällt eine solche Variante weg, beim Löschen wie beim Laden; läuft die Phase, bekommt die Rolle sofort die Felder der Grundstellung. Im Client: "Ja" der Rückfrage am Brett erwartet die Phase, die beim Klick lief (Regel 2), nicht die, die gerade läuft.

## Fassung 0.5.0, Vorgaben A62 bis A65 (01.10.2026): Sprachaktivierung als Voreinstellung, Funk-Klick, Beispielnamen
Grundlage: die Anforderungen A62 bis A65 (Sarevoc, 01.10.2026; heute `docs\entwurf\ENTSCHEIDUNGEN.md`, Anhang A). Am Draht ändert sich nichts: kein neuer Typ, kein neues Feld, kein neuer Wert.
- 4.5 (NetzListe, `ausloeserStandard`): Die Konsole legt ein Netz ohne Schalter mit `VA` an (A62), `--taste` gibt `TASTE` (A64:
  Command bleibt auf Taste, so steht es auch in der alten Startvorlage), `--nur-taste` ebenfalls `TASTE`; `--va` bleibt erlaubt und
  wirkt wie ohne Schalter. Netz 1 (Global) legt die Konsole immer mit `TASTE` und `nurTaste` an, wie der Server bei der
  Erstkonfiguration (6.5); `--va` lehnt sie dort ab, ebenso `--taste` mit `--va` und `--va` mit `--nur-taste` (Exit 2, nichts
  geändert). Bis 0.4.x stand ein Netz ohne `--va` auf `TASTE`. Gespeicherte Netze behalten ihren Wert; der Server schreibt nichts
  um, und die Voreinstellung im Lagemodell (`Netz.AusloeserStandard`, nur für Stände ohne das Feld) bleibt `TASTE`. Verwaltungsteil
  (neues Netz `VA`) und die alte Startvorlage waren schon so. Ein funk-admin 0.4.x kennt `--taste` nicht und lehnt mit Exit 2 ab.
- Client, ohne Draht (A11, A63): Der Funk-Klick ist einstellbar, unten in der Sprachaktivierung (Strg+T): Klang (Klick,
  Doppelklick, Klick mit Piep beim Loslassen, alle synthetisiert), eigene Taste und andere Sprecher je an oder aus mit Lautstärke,
  bei Sprachaktivierung dezent (18 dB leiser) oder aus, "Anhören". Die Lautstärke ist der Pegel über 200 ms bezogen auf die Sprache
  hinter der Kette (-25 dBFS). Ab Werk eigene Taste -12 dB, andere -15 dB; bis 0.4.1 lag der Klick bei -24 dBFS Spitze, über 200 ms
  -47,6 dBFS, also gut 22 dB unter der Sprache. Gespeichert unter `klick` in `client-einstellungen.json`, der Klang als Name und
  nachsichtig gelesen; ein Client 0.4.x übergeht `klick`. 7.7 (Latenz) unverändert: Der Klick wird lokal gemischt, nie gesendet.
- Texte (A65): Beispiele heißen Max Mustermann (`Mustermann/Max`), Ausgaben von funk-admin und Client nennen "den Eigentümer"
  statt Sarevoc; der otpauth-Beispielwert in 8.13 nennt `Mustermann`. Die Fragen von `funk-admin einrichten` sind sechs, mit Router acht
  (die Hilfe sagte fünf).

**Zusammenführung:** modul/s2 (Ausbaustufe S2) ist mit 0.4.1 (Hörprobe, Admin-Voraussetzungen, Handbuch) in main zusammengeführt;
die Abschnitte oben zu S2 gelten unverändert.

## Stufe 3, Schritte 3.0 und 3.1 (Fassung 0.6.0, 03.10.2026): Kanäle mit Ch.
Grundlage: der Entwurf zu Stufe 3 (Variante C "Kanalnetz mit Ch.", mit Sarevocs Antworten vom 02.10.2026; heute `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Funkfeld"), die Ch.-Listen und Presets der alten Kanalvorlage (Ch.-Listen je Kanaltyp, Annahmen markiert, 21 Rollen; mit ihr entfallen). Gebaut sind Protokoll und Server (3.0, 3.1), im ClientKern nur `RolleWaehlenAsync` und das Erkennen von Kanal-IDs im Stempel; Kanalsperren (3.2), Verwaltungsteil, Vorlage und Konsole (3.3) und der Client (3.4, 3.5) folgen.
- 2: Kanalnetz, Ch., Ruhe, Kanal, Kanal-ID, Orgaweit, gewählte Rolle; Netz heißt in der Oberfläche "Kanal".
- 4.4, 4.5: `RolleWaehlen` 0x0039 (Pflichtbereich, im Entwurf so vorgesehen, frei); `FeldSetzen` mit Kanal-IDs und neuen Regeln (Ch.-Wechsel immer, Deckung durch die Rolle); neue optionale Felder `netze[].kanaele` und `gruppe`, `ch` in Rollen- und Phasenfeldern, `Mitgliedschaften.rolle`, `rolle` in Praesenz-Details; Kanal-IDs in Feldern, Einheitskennungen, `felderPatch`, Aufstellungen und Stummschalten; `NETZ_SETZEN` mit `kanaele` und seinen Grenzen. Kein neuer Wert in `Recht`, `Stufe`, `MitgliedschaftsQuelle`, `Fehlercode`, `VerwaltungsOperation`.
- 4.7: Versionstor 0.6.0, sobald es ein Kanalnetz gibt; vorher bleiben Clients 0.5.x verbunden.
- 4.8: `RolleWaehlen` teilt sich den Eimer von `FeldSetzen` und `SatzAnwenden`; Grenzen der Kanalnetze.
- 4.10: NetzListe mit 64 Kanalnetzen zu 31 Bezeichnungen gemessen.
- 6.3, 6.5, 6.9, 6.14: Kanal-IDs im Senderecht, Global als Kanalnetz mit Orgaweit, STANDARD legt Kanalnetze auf Ruhe, die Phase richtet sich nach der gewählten Rolle. Neu 6.16 Kanäle mit Ch. und 6.17 Kanalsperren (nur festgelegt). 8.12: P-61, P-62. 9: Punkt 35.

**Server:** `Lage/Kanaele.cs` ordnet jede Netz-ID ein (gewöhnliches Netz, Kanal, Ruhe, Kanalnetz, Global, Orgaweit, unbekannt); `Netz.Kanaele` und `Gruppe`, `RollenFeld.Ch`, `Konto.GewaehlteRolle` in `lage.json`; `Feldregeln` prüfen Kanal-IDs und lösen den Ch. auf (`AusRolle`); `FeldSetzenAus` nach den neuen Regeln, neu `RolleWaehlenAus`; `NetzSetzenAus` und `NetzLoeschenAus` räumen Kanal-IDs; der Schnappschuss nimmt gehaltene Kanäle in den Index Netz -> Hörer, `Senderecht` unterscheidet unbekannte von nicht gehaltenen Kanälen; Versionstor in Hello und im Commit des ersten Kanalnetzes (`Zentrale`).

**Übergang von 0.5.0:** Testdaten `tests\Funk.Tests\Daten\zustand-0.5.0` (Herkunft dort: Programmteile von Server und Protokoll im Stand 0.5.0, auf dem Abend aus zustand-0.4.0 mit Phase 2 und Funker-Modus). 0.6.0 liest und schreibt jede Datei ohne Verlust; neu kommen nur `kanaele: []` je Netz, `gruppe`, `ch` und `gewaehlteRolle` (leer). `server.json` braucht nichts Neues.

**Abweichungen vom Entwurf, bewusst:**
- Ch. 0 ist "Ruhe" und ab Werk eingestellt (Sarevocs Antwort Q6), der Entwurf hatte Ch. 1 bis 20 mit Ch. 1 ab Werk. `kanaele` ist deshalb keine Zahl, sondern die Liste der Bezeichnungen (Index 0 Ruhe, höchstens 31 Einträge, Ch. bis 30), weil die Listen je Kanaltyp eigene Namen und Längen haben (in der alten Kanalvorlage etwa 16 Ch. in der einen Liste, 24 in einer anderen, eine dritte mit einem Notruf-Ch.). Die Ch.-Auflösung endet in Ruhe statt in Ch. 1.
- Die Ruhe behält `hoeren` und `senden` als Vorgabe für den nächsten Ch., wirkt aber nie; sonst hörte nach "Ruhe, dann Ch. 3" niemand mehr etwas, und die Regel "hören und senden bleiben" beim Ch.-Wechsel ginge nicht.
- Global hat ebenfalls Ch. (Sarevocs Antwort zu den Ch.-Listen, Annahme in den Ch.-Listen der alten Kanalvorlage): Ch. 1 "Orgaweit" ist Netz 1 selbst, damit alle alten Wege von Global unverändert bleiben; der Entwurf hatte Global ohne Ch.
- `FeldSetzen` prüft das feste Feld vor dem Netz (wie bis 0.5.0); der Entwurf nannte erst das Netz. Ein festes Feld ergibt also immer `FELD_FEST`.
- Die gewählte Rolle entfällt, wenn eine andere Vorgabe die Felder bildet (Einsteigen, Verschieben mit Rolle, Aufstellung, STANDARD); im Entwurf nicht genannt.
- `RolleWaehlen.ch`-Wünsche, die nicht passen, stehen in `uebersprungen`, statt still zu verfallen.
- Eine Durchsage an ein Kanalnetz erreicht niemanden (kein "alle Ch."); im Entwurf nicht genannt.
- Ein gewöhnliches Netz, das Kanalnetz wird, legt die Felder mit seiner Netz-ID auf seine Ruhe, und Einheitskennungen darauf fallen weg; sonst stünde die Netz-ID eines Kanalnetzes in einem Feld. Im Entwurf nicht genannt.
- Stummschalten nimmt Kanal-IDs und Kanalnetze (alle Ch.); im Entwurf gab es dafür nur die Kanalsperre.
- `RolleWaehlen` verlangt `selbstWaehlbar` auch von Konten mit `ZUWEISEN` (wörtlich nach Entwurf), anders als Einsteigen, wo `ZUWEISEN` nicht selbst wählbare Rollen nehmen darf.

**Nicht gebaut:** Kanalsperren (3.2), Verwaltungsteil, Kanalvorlage, Konsole (3.3), Client (3.4, 3.5), Last und Kreuzproben mit gebauten Programmen 0.5.0 (3.6). Nicht gemessen: Linux, Last mit 40 Funkern und Ch.-Wechseln je Sekunde.

## Stufe 3, Schritt 3.2 (Fassung 0.6.0, 03.10.2026): Kanalsperren
Grundlage: der Entwurf zu Stufe 3 (`KanalSperre`, Sarevocs Antworten Q8 und Q9 wie empfohlen; heute `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Funkfeld") und der Auftrag für Schritt 3.2. Gebaut sind Protokoll und Server, im ClientKern `KanalSperreAsync`; die Anzeige (roter Punkt, Brett) folgt mit 3.4 und 3.5.
- 4.4, 4.5: neu 0x003A `KanalSperre` (SETZEN, AUFHEBEN, LISTE); `Mitgliedschaften.gesperrt`, `gesperrt` in den Praesenz-Details; `FeldSetzen` Regel 3 (Sperre), `SatzAnwenden`, `SendeauswahlSetzen` und `RolleWaehlen` kennen Sperren; Meldung KANALSPERRE. Kein neuer Wert in `Fehlercode`, `Recht`, `Stufe`, `MitgliedschaftsQuelle`, `SendeFehlerCode`.
- 4.8: `KanalSperre` im Eimer von Stummschalten (10/s, Burst 20); Grenzen 16, 64, 500. 4.10: Größen gemessen.
- 6.3, 6.4: Streichen mit `KEIN_SENDERECHT` und `durch`, Empfänger ohne gesperrte Kanäle, je Paket. 6.12 Regel 7: Kanalsperren enden mit dem Auftrag. Neu 6.17 vollständig. 8.12: P-63.

**Server:** `Lage/Sperrregeln.cs` (Ziel, Treffer, Sperre für alle, wirksame Sperren), `Konto.Sperren`, `Schnappschuss.SperrenAlle` mit `GesperrtAn` im Heißpfad (`Senderecht`, `Weiterleiter.Schnitt`), `Zustandsschreiber.Sperren.cs` (Setzen, Aufheben, Fristen, Ende mit dem Setzenden, Räumen bei Netz und Ch.), `Arbeitsstand.OhneGesperrte` in jedem `KontoSetzen`; `Zentrale.Kanalsperren.cs` (Anfrage, Journal, Meldung), Takt in `FristFaellig`; `lage.json` mit `konten[].sperren` und `sperrenAlle`, die Sicherung lässt beide weg.

**Abweichungen vom Entwurf, bewusst:**
- Eine Sperre endet, sobald der Setzende kein `ZUWEISEN` mehr hat (Auftrag entzogen, abgegeben, abgelaufen, Funker-Modus, Konto gelöscht), wie die Sperre der Selbstwahl (Regel 7). Der Entwurf ließ sie ihre Zeit laufen; der Auftrag für Schritt 3.2 verlangt das Ende mit dem Auftrag.
- `op` kennt dazu LISTE, und jede Antwort nennt alle laufenden Sperren statt `{}`: Die Einsatzleitung braucht die Sperren für alle, die in keinen Mitgliedschaften stehen.
- Die Sperren für alle liegen im Schnappschuss, nicht im Nebenstand: Die Weiterleitung liest je Paket nur den Schnappschuss. Gespeichert werden sie wie der Nebenstand in `lage.json`.
- Der Index Netz -> Hörer bleibt ohne Sperren; die Weiterleitung prüft sie je Paket, weil Ablauf und Ausnahme der Einsatzleitung von der Zeit abhängen.
- Eine Sperre für alle, die kein ADMIN gesetzt hat, trifft kein ADMIN-Konto, auch nicht im Funker-Modus (Rangregel 6.10); im Entwurf nicht genannt.
- Ein Feld mit einem gesperrten Kanal lässt sich auch nicht leeren ("das Feld bleibt rot stehen"); der Entwurf nannte nur den Ch.-Wechsel.
- Ein gesperrter Kanal steht nie in der Sendeauswahl (die Sperre nimmt ihn heraus, `SendeauswahlSetzen` lehnt ab); der Entwurf nannte nur den Sendevorschlag von `RolleWaehlen`.
- Eine Durchsage des Gesperrten an den gesperrten Kanal wird ebenfalls mit `KEIN_SENDERECHT` gestrichen; im Entwurf nicht genannt.
- `Mitgliedschaften.gesperrt` nennt auch die Sperren für alle (`alle`), also bis 80 Einträge statt 16; die Praesenz-Details nur die eigenen.
- Löscht die Verwaltung ein Netz oder kürzt eine Ch.-Liste, enden die Sperren darauf mit Meldung (BEENDET, NETZ).

**Nicht gebaut:** Konsole für Sperren (Entwurf 4.2 `sperren`), Anzeige im Client (3.4, 3.5). Nicht gemessen: Linux, Last mit vielen Sperren unter Sprache.

## Stufe 3, Schritt 3.3 (Fassung 0.6.0, 03.10.2026): alte Kanalvorlage, Verwaltungsteil, Konsole
Grundlage: der Entwurf zu Stufe 3 (heute `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Funkfeld"), die Ch.-Listen und Presets der alten Kanalvorlage (damals mit Kopien unter den Testdaten; mit ihr entfallen, heute liegt dort `tests\Funk.Tests\Daten\starcitizen`), der Auftrag für Schritt 3.3.
- 4.5 NetzListe: Konsole mit `--kanaele`, `--kanaele-datei` und `--gruppe`; der Verwaltungsteil schickt `kanaele` nur an Server ab 0.6.0.
- 6.5, 6.16: `nurTaste` von Global gilt nur für Orgaweit, Global 1 bis 20 erlauben Sprachaktivierung (Annahme); neu der Absatz zur alten Kanalvorlage (seit 0.13.0 die Vorlage Star Citizen). 8.12: P-64. 9: Punkt 35.

**Gebaut:** die alte Kanalvorlage in `Flottenvorlagen` mit ihren Rollen, Kanalnetzen und Ch.-Listen (Protokoll, für Server und Verwaltungsteil); im Server die Ausnahme für die Global-Kanäle im Schnappschuss und `flotte-vorlage --name <alte Vorlage>` an der Konsole; `funk-admin netz-setzen --kanaele | --kanaele-datei, --gruppe`; im Verwaltungsteil der Reiter Kanäle (Gruppe, Ch.-Liste, Global mit Ch.), der feste Ch. je Rollen- und Variantenfeld ("wie zuletzt" oder ein Ch. der Liste), die Prüfung wie am Server (`Flottenentwurf.KanaelePruefen`, `ChFalsch`, Kennungen auf Kanäle), Vergleich der Ch.-Listen nach Inhalt, Sicherung mit Ch. und Phasen, die alte Kanalvorlage mit Annahmen und die Rückfrage, die das Versionstor ankündigt.

**Annahmen der Vorlage (gelb):** Global 1 bis 20 als gewöhnliche Kanäle mit Sprachaktivierung (Orgaweit bleibt nur Taste); welche Ch.-Liste einzelne Kanalnetze der alten Kanalvorlage tragen (Fireteam- und Squad-Führung, Flugleitung, Offiziere, Brücke intern, Logistik); Rolle 1 (Standard) der alten Kanalvorlage auf denselben Kanalnetzen wie die Kapitänsrollen; die Gruppen der Kanalwahl.

**Abweichungen vom Entwurf, bewusst:**
- Global 1 bis 20 erlauben Sprachaktivierung, Orgaweit nicht; bis 3.2 erbten alle Global-Kanäle `nurTaste` (Entscheidung des Auftraggebers als Annahme, Sarevoc bestätigt).
- Command ist "nur Taste" (`nurTaste`), nicht nur ab Werk Taste; der Entwurf nannte "Taste (A64)".
- Rollen-IDs frei ab 101 statt der Nummer aus dem früheren Funkprogramm der Gruppe, einem Ableger (Fork) von SRS: Die alte Startvorlage belegt 12; die Nummer steht im Namen (Rolle 12 der alten Kanalvorlage, eine Kapitänsrolle) und in `reihenfolge`.
- Die Vorlage gibt es im Verwaltungsteil und an der Konsole nur, solange außer Global kein Kanalnetz besteht; ein zweiter Lauf legte sonst 30 Kanalnetze doppelt an.
- Die Konsole verlangt die Ruhe als erste Bezeichnung von `--kanaele`, sonst rutschte jede Bezeichnung um einen Ch.
- Der Reiter heißt "Kanäle"; die übrigen Texte des Verwaltungsteils sagen noch "Netz" (die Wortliste mit Wächter ist Schritt 3.5).

**Nicht gebaut:** Konsole für Kanalsperren, Anleitung HEIMSERVER-PROXMOX.md Abschnitt 18 (3.6), Client (3.4, 3.5).

## Stufe 3, Gegenprüfung 3.7 (Fassung 0.6.0, 03.10.2026)
Unabhängige Gegenprüfung der ganzen Stufe 3 (Rechte, Sperren, Übergang, Oberfläche, Last). Am Draht ändert sich nichts.
- 4.5 Praesenz und 0x003A: Ein Delta, das nur Details ändert (`FeldSetzen`, `HoerenSetzen`, `RolleWaehlen`, `KanalSperre`), geht nur an Sitzungen mit `ZUWEISEN`; eine Sperre für alle schickt den Getroffenen nur `Mitgliedschaften` und der Einsatzleitung nur die Konten, deren Sendeauswahl sie ändert. Gemessen (`Stufe37GegenpruefungTests`): Bis 3.6 bekam jeder Funker je Ch.-Wechsel einen unveränderten Eintrag (etwa 130 Byte, bei 40 Sitzungen gut 5 KB je Wechsel an alle) und bei einer Sperre für alle ein Delta mit allen Getroffenen an alle Getroffenen (bei 100 Sitzungen und 10 Sperren je Sekunde, die die Grenze zulässt, mehrere MB/s über die DSL-Leitung).
- Client, Brett (Entwurf zu Stufe 3, Sarevoc Q9): "Für alle gesperrt" unter der Kanalliste mit "Kanal für alle sperren" (Kanal, bei Ch. "alle Ch." oder ein Ch., Global nur mit seinen Ch. und nie Orgaweit, Rückfrage bis 04:00) und "freigeben"; die Liste kommt aus `KanalSperre` LISTE. Bis 3.6 ging das nur an der Konsole, eine Einsatzleitung ohne Konsole konnte gar nicht für alle sperren.
- Anleitung HEIMSERVER-PROXMOX.md Abschnitt 18: Hinweis, dass die Rollenliste ab den Setups 0.6.0 auch die selbst wählbaren Rollen der alten Flotte anbietet (die Kapitänsrolle der alten Startvorlage gibt Command ohne Platz und trotz "Wechsel sperren"; nach der alten Kanalvorlage steht diese Rolle zweimal da); das Fehlerbild von Server 0.5.0 auf einem Stand mit Kanälen heißt im Container "Unhandled exception. ...", nicht "Start gescheitert: ..." (das schreibt nur der Probefunk-Server).
- Konsole (`kanalsperre-setzen`, `kanalsperre-aufheben`): Ein Name, den mehrere Kanäle tragen (neben der alten Kanalvorlage dreimal "Command"), wird abgelehnt und nennt die Nummern; bis 3.6 sperrte er still den mit der kleinsten Nummer.

## Fassung 0.7.0 (04.10.2026): Löschen heißt vollständig entfernen, ein PC ein Konto
Vorgabe Sarevoc nach dem Test mit dem zweiten PC: Löschen entfernt alles, was der Server über einen PC weiß, außer der IP-Sperrliste gegen Rateangriffe; gesperrt wird nur bewusst durch einen Admin, nie von selbst; ein PC ist ein Konto. Am Draht ändert sich kein Feld, nur das Verhalten.
- 3.2: Kein Angebot eines neuen Geräteschlüssels mehr (es war nie gebaut); `GERAET_GESPERRT` gibt es nur noch nach einer bewussten Sperre.
- 3.3, 3.5: ABLEHNEN löscht Gerät und Antragskonto, die wartende Verbindung bekommt `GeraetStatus{ABGELEHNT}` und dann `Bye{ENTFERNT}`, damit der Client nicht von selbst neu beantragt. Ein vergebener Discord-Name bricht die Freigabe nicht mehr ab; das Konto bekommt ihn nur nicht (A53 bleibt gewahrt).
- 3.6: Löschen legt nichts auf die Sperrliste; dauerhaft sperren (ENTZOGEN, Konsole) ist die einzige Gerätesperre. Beim ersten Laden verwirft der Server 0.7.0 einmal jede Sperre aus 0.6.x, zu der kein entzogenes Gerät mehr steht (die aus Ablehnen und Löschen), und die abgelehnten Geräte; Sperren entzogener Geräte bleiben als bewusst. Jede freigewordene Gerätekennung steht im Journal (`SPERRE_VERWORFEN`). `konten.json` trägt danach `nurBewussteSperren: true`, Server bis 0.6.x lesen die Datei weiter.
- 4.5 AntraegeAblehnen, 4.6 WARTE_HELLO und GETRENNT, 4.9 GERAET_GESPERRT entsprechend.
- Verwaltungsteil: kein "Zweites Gerät an Konto" mehr; die Konsole kann `--an-konto` weiter, etwa für den PC eines Admins mit neuem Geräteschlüssel.

## Fassung 0.8.0 (05.10.2026): Sperre aufheben, Umbau der Verwaltung
Nachtauftrag Sarevoc vom 05.10.2026, Teil 2: Die bewusste Gerätesperre aus 0.7.0 hatte keinen Weg zurück.
- 3.6: neue Zeile "Sperre aufheben": Ein ENTZOGENES Gerät wird ENTFERNT, sein Fingerabdruck frei; der PC darf neu beantragen und braucht wieder eine Freigabe. Rechte wie beim dauerhaften Sperren; an der Konsole (`sperre-aufheben`) auch für eine Sperre, zu der kein Gerät mehr steht. Journal `SPERRE_AUFGEHOBEN`.
- 3.8 (Befehle des Admin-Werkzeugs) und 3.9 (frische Bestätigung, Geräte anderer Admins nur an der Konsole): Sperre aufheben ergänzt.
- 4.5 0x0013: neuer Wert `ENTSPERREN` für `entscheidung`; 4.7: ein Client stellt ihn nur an Server ab 0.8.0, ein Server 0.7.x antwortet `UNGUELTIG`.

Teil 1 (Umbau der Verwaltung, docs/entwurf/VERWALTUNG-UMBAU.md): Herkunftsschild an Rollen und Kanälen.
- 4.5 NetzListe: neues optionales Feld `herkunft: str(32)` an `netze[]` und `rollen[]`, ebenso in `NETZ_SETZEN` und `ROLLE_SETZEN`. Die Vorlagen schreiben ihren Namen (damals die Namen der alten Kanalvorlage und der alten Startvorlage, seit 0.13.0 "Star Citizen"), der Verwaltungsteil Bausteine ("Baustein Trupp", "Baustein Orga") und "von Hand". Fehlt das Feld beim Setzen, bleibt die bisherige Herkunft; leer ergibt `UNGUELTIG`.
- 4.7: Gegenstellen 0.7.x übergehen das Feld; der Verwaltungsteil zählt die Herkunft nie als Änderung. lage.json trägt `herkunft` an Netzen und Rollen, ein Server 0.7.x liest sie trotzdem.

## Fassung 0.8.1 (06.10.2026): Felder wie im früheren Funkprogramm
Befund und Entscheidung Sarevoc vom 06.10.2026 (Q44 bis Q46): Den Kanal eines Felds bestimmt die Rolle wie das Preset im früheren Funkprogramm; der Funker wählt nur den Ch.
- 4.5 0x0035 `FeldSetzen`: selbst nur noch der Ch.-Wechsel im selben Kanalnetz, auch im festen Feld, und ein offenes Netz in ein freies Feld. Ein anderes Netz in einem belegten Feld und das Leeren eines belegten Felds ergeben `FELD_FEST` (festes Feld, vor allen anderen Prüfungen) bzw. `KEIN_RECHT`. Die Deckung durch die Rolle entfällt. Kein neuer Code, kein neues Feld.
- 4.5 0x0036 `SatzAnwenden`: Mit `rolleId` kommen die Felder aus der Rolle wie bei der Selbstwahl, ohne `rolleId` bleiben sie; aus dem Satz gelten nur `hoeren` für Netze, die schon in einem Feld liegen, und offene Netze in freien Feldern. Die Sonderregel der Phase (6.14) ist damit der Normalfall; mit Phase gilt weiter deren Sendevorschlag.
- 4.5 0x0039 `RolleWaehlen`: `ch` wird noch geprüft (ungültige Wünsche stehen weiter in `uebersprungen`), aber nicht mehr angewandt; nach der Rollenwahl beginnt jeder Ch. auf Ruhe, außer bei einem festen Ch. der Rolle, einer Einheit des Platzes und dem Ch., den das Konto im Kanalnetz schon hat. Ein Client ab 0.8.1 schickt kein `ch` mehr.
- 4.7: Ein Client 0.8.0 an einem Server 0.8.1 bekommt für das Umlegen oder Leeren über die alte Kanalwahl am Namen `KEIN_RECHT` bzw. `FELD_FEST` und zeigt den Satz dazu; seine Wünsche wirken nicht mehr. Ein Client 0.8.1 an einem Server 0.8.0 bietet den Ch.-Wechsel im festen Feld an, der Server 0.8.0 antwortet `FELD_FEST`, der Client zeigt den Satz.

## Fassung 0.9.1 (08.10.2026): erstes Sendeziel nach der Rollenwahl, Command mit Sprachaktivierung
Befund Sarevoc beim Test mit 0.9.0: Nach der Rollenwahl stand alles auf Ruhe, und auch nach dem Wechsel auf einen Ch. ging nichts hinaus.
- 4.5 0x0035 `FeldSetzen`, Regel 7: Geht ein Feld von der Ruhe auf einen Ch. und ist die Sendeauswahl danach leer (keine Felder, Global nicht), wird dieses Feld Sendeziel. Ein Ch.-Wechsel ohne Ruhe dazwischen wählt nichts von selbst; eine vorhandene Auswahl bleibt.
- Alte Kanalvorlage (und alte Startvorlage): Command ist nicht mehr "nur Taste", ab Werk Sprachaktivierung (Q53). Am Draht ändert sich nichts.

## Fassung 0.11.0 (12.10.2026): Zuordnung, Herkunft beim Login, Untergilde, keine Doppelbelegung
Abgenommener Umfang Sarevoc vom 12.10.2026 (Entwurf zur Zuordnung, heute `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Zuteilung", Q95 bis Q108). Kein neuer Nachrichtentyp, kein neues Pflichtfeld, kein neuer Wert in einer Aufzählung; geändert wird nur, welche vorhandene Quelle der Server beim Login meldet und was ein fehlendes optionales Feld bei Untergilde SETZEN heißt.
- 6.9 Beim Anmelden: Eine Reservierung aus `Verschieben` kommt als VERSCHIEBEN mit `durch` zurück (bis 0.10.0 WIEDERHERGESTELLT mit `durch`), AUFSTELLUNG wie bisher; WIEDERHERGESTELLT gibt es nur noch in der Frist ohne Reservierung, mit `durch` 0. Keine neue `kontoVersion`, kein Push über den Login hinaus.
- 6.9 RESERVIERT: `Verschieben` mit `rolleId` 0 ohne `felder` und `felderPatch` ("zurück auf Standard", "Abend beenden") setzt keine Reservierung mehr. Sonst käme der Funker innerhalb von 12 h als eingeteilt zurück, und sein Client stellte die gemerkte Rolle nicht her.
- 6.9 Erste Anmeldung: Ein Konto ohne je eine Sitzung behält seinen Anfangsstand nur ohne Reservierung. Mit abgelaufener Reservierung gilt STANDARD wie nach einer Sitzung; vorher belegte es seinen längst vergebenen Platz ein zweites Mal (vermutete Doppelbelegung, mit Test bestätigt und behoben). VERSCHIEBEN ohne Reservierung kommt dabei als STANDARD ohne `durch`.
- 4.5 0x001D Untergilde SETZEN: Fehlen `stammEinheit` oder `ansprechpartner`, bleiben die gespeicherten Werte; 0 heißt "keins". Ein behaltener Ansprechpartner außerhalb von `konten` fällt still weg, ein ausdrücklich genannter ergibt weiter `UNGUELTIG`. Bis 0.10.0 löschte jedes Speichern aus dem Verwaltungsteil beide Werte, weil er sie nie mitschickte; der Verwaltungsteil 0.11.0 schickt sie mit (den Ansprechpartner nur, solange er Mitglied ist, nie 0).
- 6.16 Ch. je Person: Die Einsatzleitung setzt den Ch. eines Felds mit `Verschieben` und `felderPatch: [{feld, netzId: Kanal-ID, hoeren, senden, fest}]` (Form seit v1). Hören, senden und fest gehen unverändert mit; so bleibt der Ch. ein Vorschlag, den der Funker mit `FeldSetzen` ändern darf (Q45). Platz und gewählte Rolle bleiben, eine neue `RolleWaehlen` behält den Ch. (Ch.-Auflösung Schritt 3).
- Befunde der Prüfung (12.10.2026), 6.9: Hat der Funker nach einer Einteilung selbst etwas geändert (`FeldSetzen`, `RolleWaehlen`, `SatzAnwenden` ohne Rolle; die Reservierung bleibt dabei), kommt er beim Login mit seiner Quelle SELBST, ROLLE oder SATZ ohne `durch` zurück, nicht als VERSCHIEBEN oder AUFSTELLUNG der Leitung. Sonst stand nach jeder kurzen Trennung "eingeteilt von X" statt "selbst" auf dem Brett und bei alten Clients, und ein Client 0.11.0 sagte die Einteilung noch einmal an. Ein Server 0.10.0 überschrieb die eigene Änderung ebenso, mit WIEDERHERGESTELLT und `durch`.
- Befunde der Prüfung, 4.5 0x0033 `Verschieben`: Wie `FeldSetzen` Regel 7 wird ein Feld, das per `felderPatch` mit `feld` von der Ruhe auf einen Ch. desselben Kanalnetzes geht und danach sendet, Sendeziel, wenn die Sendeauswahl sonst leer bliebe und der Kanal nicht gesperrt ist (ohne `sendeFelder`, ohne Rolle oder Schiff). Sonst hörte die Person den Ch. der Leitung, sendete aber nirgendwohin. Am Draht ändert sich nichts; ein Client schickt dafür nichts Neues, ein Server 0.10.0 lässt die Auswahl leer wie bisher.
- Client (keine Drahtänderung): Die Ansage beim Login erkennt eine schon gesehene Einteilung an (durch, Einheit, Rolle), ohne Platz an (durch, feste Felder und Ch.), nicht mehr an der `kontoVersion`; jede online gesehene Einteilung gilt als gelesen, auch nach einem Phasenwechsel.
- 4.7: Clients 0.9.x und 0.10.0 an einem Server 0.11.0 zeigen nach dem Login ", eingeteilt von ..." (nach einer eigenen Änderung "selbst gewählt") und eine Ch.-Wahl der Leitung als "Kanäle geändert von ..."; sie vergessen ihre gemerkte Rolle wie bisher. Ein Verwaltungsteil 0.10.0 verliert Stammeinheit und Ansprechpartner nicht mehr. Ein Client 0.11.0 an einem Server 0.10.0 sagt die Einteilung beim Login auch für WIEDERHERGESTELLT mit `durch` an; ein Verwaltungsteil 0.11.0 schickt dort die gelesenen Werte mit. lage.json bekommt kein Feld und keinen Wert, der Rückweg auf 0.10.0 bleibt.

## Fassung 0.11.7 (15.10.2026): Abzeichen
Abgenommene Vorgabe Sarevoc vom 14./15.10.2026: Abzeichen stehen für das, was die Leute spielen. Ein neuer Pflichttyp (0x0025), sonst nur optionale Felder; kein neuer Wert in einer strengen Aufzählung.
- 2 Begriffe: Abzeichen neu (Ränge in drei Laufbahnen, Tätigkeiten), Zeichen um das Ausblenden ergänzt. `FUEHRUNG` war ein Platzhalter: Der Server schickt die Kennung weiter (Stufenmerkmal RAIDLEITER), der Client zeichnet sie nicht mehr, ausblenden oder vorn zeigen lässt sie sich nicht.
- Abzeichenkatalog: fest im Programm, `src/Funk.Protokoll/Abzeichen/abzeichen.json` als eingebettete Ressource (`Funk.Protokoll.Abzeichen.abzeichen.json`), gelesen einmal von `Abzeichenkatalog`. Laufbahnen Marine (10 Ränge), Navy (10), Orga (8), dazu 22 Tätigkeiten in vier Gruppen; Kennungen `[A-Z0-9_]`, höchstens 16 Zeichen, eindeutig. Server und Client lesen dieselbe Datei; Namen und Bilder ändert nur ein neues Programm.
- 4.4/4.5 0x0025 Abzeichen (C->S, `KONTEN_VERWALTEN` in einer Admin-Sitzung): `{kontoId, raenge: [str(16)] <= 3, taetigkeiten: [str(16)] <= 6}` ersetzt beide Listen, Antwort `{kontoId, raenge, taetigkeiten}` in Katalogfolge. Prüfregeln des Servers: Kennung im Katalog und von der richtigen Art, höchstens ein Rang je Laufbahn, keine Doppelten (`UNGUELTIG` mit Klartext, ohne Trennung); unbekanntes Konto oder Antrag `UNBEKANNT`. Die Führung vergibt nicht (`KEIN_RECHT`). Ratengruppe wie Mitglied (4.8), verlängert eine Admin-Sitzung wie jede Verwaltungshandlung. Journal `ABZEICHEN` mit Titeln ("Marine: Sergeant, Gunner M"), danach `MITGLIED_GEAENDERT`, `Mitgliedschaften` und Praesenz-Delta wie bei `Mitglied AENDERN`.
- 4.5 0x0018 ProfilSetzen: `abzeichenVorn?: str(16)` ("" = keine Wahl) und `abzeichenAus?: [str(16)] <= 64` (ersetzt die Liste); erlaubt sind Katalogkennungen und `ADMIN`/`EINSATZLEITUNG`, sonst `UNGUELTIG`, und dann ändert sich auch nichts anderes. Die Antwort nennt beide.
- 4.5 0x0022 Mitgliedschaften, `konto`: `abzeichen?` (alle eigenen, auch ausgeblendete, höchstens 9), `abzeichenVorn?`, `abzeichenAus?`.
- 4.5 0x0023 Praesenz: `zeichen` ohne die ausgeblendeten automatischen Zeichen; neu `abzeichen?` (die sichtbaren, Ränge in Laufbahnfolge, dann Tätigkeiten in Katalogfolge) und `abzeichenVorn?` (nur wenn besessen und sichtbar). Dieselben Sperren wie bei `zeichen`: "Mein Zeichen zeigen" aus heißt nichts, ohne Freigabe nur für `ZUWEISEN`. Ausgeblendetes sieht auch die Einsatzleitung nicht.
- 4.5 0x001E Mitgliedskarte: `abzeichen?` (die sichtbaren; das Konto selbst sieht alle eigenen). 0x001B Mitglied LISTE: `raenge?` und `taetigkeiten?` vollständig, auch Ausgeblendetes, damit der Admin vergeben kann.
- 4.10: Praesenz voll mit 9 Abzeichen je Eintrag gemessen 748 KB (Stufe 3 638 KB): über der halben Rahmengrenze, darunter; P-02b meldet es als Befund wie Stufe 1 bis 3. Eine Seite Mitglied LISTE mit Abzeichen 428 KB, unter der halben Grenze.
- 6.13: Abzeichen-Abschnitt (vergeben nur Admins, "ein Abzeichen haben ist eine Sache, es zeigen eine eigene Entscheidung", ausblenden ändert nie Rechte). `mitglieder.json` trägt je Mitglied `raenge`, `taetigkeiten`, `abzeichenVorn`, `abzeichenAus`; eine ältere Datei lädt mit leeren Werten. Wird ein Konto gelöscht, gehen sie mit.
- 4.7: Clients bis 0.11.6 übergehen die neuen Felder und zeigen weiter nur die Zeichen. 0x0025 stellt ein Client nur an einen Server ab 0.11.7 (`Versionen.AbzeichenAb`, `ServerFassungFuer`). Ein Server bis 0.11.6 übergeht die neuen Felder in `ProfilSetzen`; auf einer `mitglieder.json` von 0.11.7 startet er, schreibt die Abzeichen beim nächsten Speichern aber nicht zurück.
- Grenzen (Funk.Protokoll `Grenzen`): `RaengeMax` 3, `TaetigkeitenMax` 6, `AbzeichenMax` 9, `AbzeichenKennungMax` 16, `AbzeichenAusMax` 64. SteuerKatalogTests zählen 72 Typen.

## Fassung 0.11.8 (15.10.2026): Untergilden entfallen
Entscheidung Sarevoc (Entwurf zur Zuordnung, `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Zuteilung"): "Untergilden entfallen ganz, überall (Wer ist da, Brett, Verwaltung, Mitgliederbuch-Anzeige)". Kein neuer Pflichttyp, kein Typ entfernt, nur ein optionales Feld neu.
- 2 Begriffe: Untergilde als entfallen gekennzeichnet; Vormerkung ohne Untergilde.
- 4.4/4.5 0x001D Untergilde: entfallen, Nummer reserviert. Der Typ bleibt als Hülle im Katalog, sonst trennte ein neuer Server Clients bis 0.11.7, die LISTE beim Öffnen rufen. LISTE liefert jedem `{untergilden: []}`, SETZEN und LOESCHEN jedem `UNGUELTIG` mit "Untergilden gibt es seit 0.11.8 nicht mehr", ohne Seiteneffekt. Die Anfrage verlängert eine Admin-Sitzung weiter, die Ratengruppe bleibt (4.8). Clients ab 0.11.8 schicken 0x001D nicht mehr.
- 4.5 Felder `untergilde` in KontoStand (`Mitgliedschaften.konto`, Welcome), PraesenzEintrag, VerzeichnisEintrag (0x0038), MitgliedskarteAntwort, MitgliedEintrag und VormerkungVorschlag: bleiben optional im Protokoll für ältere Gegenstellen, der Server füllt sie nie. `Mitglied.untergilde` übergeht der Server.
- 4.5 0x001B Mitglied EINFUEGEN: Format unverändert (`SC-Name; Discord-Name; Kürzel; Rollen`), die dritte Spalte wird übergangen; eine unbekannte Untergilde ist kein Zeilenfehler mehr. Neu in der Antwort `hinweis?: str(100)`, einmal je Liste, wenn in Spalte 3 etwas stand ("Spalte 3 (Untergilde) wird seit 0.11.8 übergangen"); angelegt wird trotzdem. Ältere Verwaltungen übergehen das Feld.
- 6.13: Untergilden entfallen. Gespeicherte Untergilden (`untergilden` in `mitglieder.json`) bleiben und werden weiter geschrieben, damit der Rückweg auf eine ältere Serverfassung offen bleibt (sie liest das Feld ohne Prüfung). Die Freigabe mit Vormerkung trägt das Konto nicht mehr in die Untergilde ein. Weiter nachgezogen wird nur das Entfernen (Datenschutz, berichtigt am 15.10.2026): Wird ein Konto gelöscht, fällt seine Nummer aus `konten` und `ansprechpartner`; fällt eine Vormerkung weg (gelöscht oder übernommen), ihre Nummer aus `vormerkungen`. Praesenz und Mitgliedskarte zeigen mit Freigabe Zeichen und Abzeichen, keine Untergilde.
- 4.7: Ein Client bis 0.11.7 an einem Server 0.11.8 bekommt leere Untergilden und zeigt keine Kürzel mehr; seine Verwaltung zeigt eine leere Seite Untergilden, Speichern dort scheitert mit dem Klartext. Ein Client 0.11.8 an einem Server bis 0.11.7 fragt 0x001D nicht und zeigt die Felder nicht, die der alte Server noch füllt; Liste einfügen mit einem Kürzel in Spalte 3 prüft der alte Server noch wie bisher.
- 4.10 und 6.13 Grenzen: `UntergildenMax` und `UntergildeMitgliederMax` bleiben als Prüfgrenzen der Hülle. SteuerKatalogTests zählen weiter 72 Typen.
- P-51 und P-53 angepasst: Untergilde ist keine Verwaltungsnachricht mit `ADMIN_SITZUNG_NOETIG` mehr; Praesenz trägt nie eine Untergilde.

## Fassung 0.12.0 (16.10.2026): Meldesystem
Entscheidungen Sarevoc vom 11.10. und 16.10.2026 (M1-M23, heute in `docs\entwurf\ENTSCHEIDUNGEN.md`, Kapitel "Overlay und Meldungen"): Befehle, Zustände, Ampel, Countdown und Bereit-Abfrage als Grafik an alle, die online sind. Zwei neue Pflichttypen, zwei neue Pushes im optionalen Bereich, sonst nur optionale Felder; kein neuer Wert in einer strengen Aufzählung, kein neues Recht.
- Meldungskatalog: fest im Programm, `src/Funk.Protokoll/Meldungen/meldungen.json` als eingebettete Ressource (`Funk.Protokoll.Meldungen.meldungen.json`), gelesen einmal von `Meldungskatalog`. 19 Motive: 8 Befehle (Stopp mit zwei Bildern, Handschuh und Xeno), 5 Zustände, 3 Ampelstellungen, 2 Countdowns, die Bereit-Abfrage; Kennungen `[a-z0-9_]`, höchstens 32 Zeichen. Ausführungen Schlicht (alle), Metall (nicht los, stopfen, pause), Nur Schrift (nur diese drei); fehlt eine, gilt Schlicht. Die Bilder (Sarevocs eigene, mit ChatGPT erstellt) kommen verkleinert mit dem Client.
- 4.4/4.5 0x0026 MeldungSenden (C->S): `{art: str(24), motiv?: str(32), groesse?: u8 1-3, zeile?: str(60), ziel?: i64, form?: str(16)}`, Antwort `{}`. `art` offener Text (Befehl, Zustand, ZustandAufheben, Ampel, AmpelAus, Countdown, CountdownAbbrechen, BereitAbfrage, BereitSchliessen). Prüfregeln des Servers: Art bekannt, Motiv im Katalog und von der richtigen Art, Countdown mit Form und Zielzeit in der Zukunft, höchstens 24 h voraus; sonst `UNGUELTIG` mit Klartext, ohne Trennung. Recht nach 6.18, sonst `KEIN_RECHT`.
- 4.4/4.5 0x0027 BereitMelden (C->S, jedes Konto): `{nummer: u32}`, Antwort `{}`; ohne offene Abfrage dieser Nummer `UNBEKANNT`.
- 4.4/4.5 0x4005 Meldestand (S->C, optional, an alle): der ganze laufende Stand (`zustand?`, `ampel?`, `countdown?`, `abfrage?`) mit `anlass` und `von`, bei einem Befehl dazu `befehl`; nach Welcome mit `anlass` `Stand`, ohne vergangene Befehle (M20).
- 4.4/4.5 0x4006 BereitStand (S->C, optional, an die, die senden dürfen): `{nummer, offen, bereit, online, fehlen: [kontoId] <= 500, von, bis}`.
- 4.5 0x001B Mitglied: `meldungenSenden?: bool` in AENDERN (nur Konten, bei einer Vormerkung `UNGUELTIG`, Journal `MELDUNGEN`) und `meldungenSenden?: true` in LISTE. 0x0022 Mitgliedschaften, `konto`: `meldungenSenden?: true`.
- 4.8: MeldungSenden 1 je 2 s, Burst 1 (je Sender, M19), BereitMelden 1/s, Burst 2; Grenzen des Meldesystems in der Tabelle.
- 6.18 neu: Meldesystem (Recht, Ziel, Arten, Bereit-Abfrage mit Ende nach 2 min, Katalog, Nachlieferung, Uhrabgleich über Ping im Client ohne Änderung am Server). Der Stand lebt nur im Speicher; der Haken steht in `mitglieder.json` (`meldungenSenden` je Mitglied), eine ältere Datei lädt ohne Haken.
- 4.7: Clients bis 0.11.9 übergehen die neuen Pushes und Felder; 0x0026 und 0x0027 stellt ein Client nur an einen Server ab 0.12.0 (`Versionen.MeldesystemAb`). Ein Server bis 0.11.9 übergeht `meldungenSenden` in `Mitglied AENDERN` still; die Verwaltung setzt den Haken deshalb nur an Servern ab 0.12.0 (`Verwaltungslage.MeldungenBekannt`). Ein Server bis 0.11.9 startet auf einer `mitglieder.json` von 0.12.0, schreibt die Haken beim nächsten Speichern aber nicht zurück.
- Grenzen (Funk.Protokoll `Grenzen`, `Fristen`): `MeldungsArtMax` 24, `MotivKennungMax` 32, `MeldungZeileMax` 60, `MeldungGroesseMax` 3, `CountdownFormMax` 16, `CountdownVorlaufMaxMs` 24 h, `MeldungAbstandMs` 2000, `BereitAbfrageMs` 120000. SteuerKatalogTests zählen 76 Typen.
- Abweichung von der Bauvorgabe: keine. Offen für Sarevoc: Der Startende einer Bereit-Abfrage zählt zu den Online-Sitzungen und steht unter "fehlt", bis er selbst bereit meldet (m = alle Online-Sitzungen, wörtlich nach der Vorgabe).
- Nachtrag Textmeldung (M24, Sarevoc 16.10.2026 abends): weitere Arten `Text` und `TextAufheben` derselben Anfrage 0x0026, kein neuer Typ. Neue optionale Felder in 0x0026 `text?: str(120)`, `farbe?: str(16)` (`weiss`, `gelb`, `orange`, `rot`, `gruen`, `blau`; fehlt = `weiss`), `dauer?: str(16)` (`8s` oder `bleibt`; fehlt = `8s`), in 0x4005 `text?: {text, farbe, groesse, dauer, von, seit}`. Prüfregeln des Servers: Text getrimmt nicht leer, Farbe und Dauer aus der festen Liste, sonst `UNGUELTIG` mit Klartext ("Textmeldung braucht einen Text", "unbekannte Farbe X", "unbekannte Dauer X"); ein Text über 120 Zeichen ist `ZU_GROSS` und trennt. Recht, Rate und Ziel wie bei allen Meldungen. Eine neue Textmeldung ersetzt die laufende; eine stehende (`bleibt`) gehört zum Stand und kommt nach Welcome, eine mit 8 s steht nur im Push mit `anlass` `Text`. Grenzen `MeldungTextMax` 120, `TextKennungMax` 16 (Farbe, Dauer); Konstanten in `Textmeldungen`.

## Abgelehnte Vorschläge
- **Krypto-Befund 2: zweiter Bestätigungsschritt in Discord (Bot fragt denselben Nutzer mit Anzeigeform per Ja/Nein oder /funk bestaetigen), bei unveränderter Code-Richtung**: Stattdessen die vom Prüfer genannte Alternative übernommen: Die Code-Richtung ist umgekehrt. Damit ist ein abgeschriebener Code wertlos. Ein zusätzlicher Discord-Schritt würde nur noch gegen Überreden helfen, und da hilft er so wenig wie die umgekehrte Richtung. Es entfällt also Code. Die Rückfrage findet stattdessen im Client statt (VerknuepfungBestaetigen). Übernommen sind die DM an den Inhaber, der Discord-Benutzername und der Hinweis 'laut Client, ungeprüft'.
- **Krypto-Befund 3 (3): Freigabe durch ein Geschwistergerät erst nach 24 h Einspruchsfrist wirksam**: Die Spezifikation ist strenger: Geschwistergeräte geben gar nicht mehr frei, sondern markieren nur (BESTAETIGEN/EINSPRUCH). Eine Frist wäre zusätzlicher Zustand im sicherheitskritischen Eigenbau. Für 5 bis 40 Nutzer ist die Freigabe durch einen Admin zumutbar.
- **Krypto-Befund 3 (4): neue Geräte privilegierter Konten auch 'durch zwei verschiedene Admins' im Client**: In v1 nur über das Admin-Werkzeug. Das ist weniger Code und kommt selten vor. Die Zwei-Admin-Freigabe steht als Offener Punkt 14 für v2.
- **Krypto-Befund 6: ohne Signatur darf ein Profil wenigstens neue Hosts anhängen**: Abgelehnt, es gibt überhaupt keine unsignierten Profiländerungen. Wer den laufenden Serverschlüssel erbeutet, könnte die Clients sonst über zusätzliche Hosts steuern. Ein Umzug ist selten, dann wird eben offline ein neues Profil signiert.
- **Krypto-Befund 11: auch für Konten der Stufe RAIDLEITER nur das Admin-Werkzeug, kein Code über den Bot**: Nur für privilegierte Konten übernommen (ADMIN und Verwaltungsrechte). In der Heimphase ist das Admin-Werkzeug nur im LAN erreichbar, also praktisch nur für Sarevoc. Raidleiter-Geräte brauchen ohnehin die Freigabe durch einen Admin, dazu eine Pflichtrückfrage beim Inhaber. Ob das reicht, steht als Offener Punkt 15.
- **Krypto-Befund 11: den Bot per systemd IPAddressDeny/Firewall nur zu Discord durchlassen**: Nur teilweise übernommen. Discord läuft über wechselnde CDN-Adressen, eine Positivliste ist nicht pflegbar. Umgesetzt: kein lauschender Socket, RestrictAddressFamilies, Sperre von localhost und privaten Netzen außer dem DNS-Resolver.
- **Krypto-Befund 14: am Client die WER-Dumps für den Prozess abschalten**: Das Programm verstellt keine Windows-Fehlerberichte, das wäre ein Eingriff in Systemeinstellungen. Die Sitzungsschlüssel sind kurzlebig. Der Geräteschlüssel liegt im TPM bzw. nach Kenntnisstand im isolierten Schlüsseldienst (Offener Punkt 1). Der Prüfer ließ das Hinnehmen ausdrücklich offen, es ist so festgehalten.
- **Krypto-Befund 16: Namen serverweit über ein Verwechsler-Skelett nach Unicode UTS #39 prüfen**: Nicht in v1. Das braucht eine externe Unicode-Datentabelle samt Lizenzeintrag und Pflege. Stattdessen: eindeutige Namen nach NFKC und Großschreibung, dazu der eindeutige ASCII-Discord-Benutzername in Freigabe-, Verwaltungs- und Sprecheranzeige. Der Nick ist nur ein Vorschlag. Offener Punkt 26 für v2.
- **Krypto-Befund 8: Ausgangsbudget fair je Absender verteilen, bei Überlast zuerst den Absender mit dem höchsten Anteil (Pakete x Empfänger) drosseln**: Ersetzt durch den Paket- und Byte-Topf je Sitzung, der den Anteil jedes einzelnen Senders deckelt (ein Troll höchstens etwa 6-7 Mbit/s), und durch den Strom-Abwurf je Empfänger aus Netz-Befund 16. Ein zweites Zuteilungsverfahren würde dasselbe doppelt regeln. Übernommen sind SendeFehler GRENZE, der Hinweis an die Raidleiter und der Testfall 'ein Troll bei voller Last'.
- **Krypto-Befund 7 (1): erst umbinden, wenn die alte Adresse seit mindestens 1 s nichts Gültiges mehr geschickt hat**: In veränderter Form übernommen. Die Frist ist 500 ms, und authentische Duplikate von der alten Adresse zählen ausdrücklich als Lebenszeichen (Kopierprüfung). Sonst hätte ein schnellerer Kopierer die Regel ausgehebelt, denn seine Kopien machen die Originale zu Replays. 500 ms genügen bei 50 Paketen/s als Beleg und halbieren die Lücke beim echten Rebinding. Startwert, Offener Punkt 11.
- **Netz-Befund 5 (2): in der Schnellphase verpflichtend direkt bei den autoritativen Nameservern auflösen (eigener DNS-Client)**: Nur als KANN nach einem Spike übernommen. Ein eigener DNS-Client ist zusätzlicher Parser-Code für Eingaben aus dem Netz, und manche Netze sperren DNS nach außen. Liegt die Neuverbindung der FRITZ!Box nachts, sind Adresswechsel im Einsatz selten. Die übrigen Teile (Schnellphase, Pin-Fehler als falscher Host, Erkennung in 6 s, Feldtest) sind vollständig übernommen.
- **Netz-Befund 8: SO_TIMESTAMPNS einschalten und die p99-Zeit ab Kernel-Stempel messen**: In .NET nur per P/Invoke auf recvmsg erreichbar, das ist kein Bordmittel. Ersatz: Zeitstempel mit der monotonen Uhr direkt nach dem Empfang, kleinerer Empfangspuffer und Verwerfen nach 60 ms Liegezeit. Der Kernel-Stempel bleibt als Spike (Offener Punkt 24).
- **Netz-Befund 15 und Funk-Befund 12: Teilrahmen (teil/von) für große Pushes und im Tunnel große Steuerrahmen in 4-KiB-Stücke teilen und mit der Sprache verschränken**: Stattdessen Rahmengrenze in AKTIV auf 1 MiB angehoben, mit einer Rechnung, dass jede Nachricht an ihren Höchstwerten mehr als den Faktor 2 darunter bleibt (4.10). Teilung und Verschränkung bräuchten Zusammenbau-Zustand und ein zweites Framing. Große Pushes sind selten, und der Tunnel ist Notbetrieb. Das Head-of-line-Blocking ist dort bewusst hingenommen.
- **Funk-Befund 8: Semantik 'Feld fehlt = unverändert, null = entfernen' bei Verschieben**: Ersetzt durch ausdrückliche Operationen ({feld, leeren: true}, rolleId 0, schiffId 0), und null gilt wie fehlend. System.Text.Json mit Source-Generator unterscheidet fehlend und null nicht ohne zusätzlichen Code, und ausdrückliche Operationen lassen sich besser testen. Alles andere aus dem Befund ist übernommen (felderPatch, schiffId allein, Felder in Praesenz für ZUWEISEN).
- **Funk-Befund 17: neue Zielart 3 = KENNUNG (etwa CREW), die der Server über alle Schiffe auflöst**: Stattdessen die vom Prüfer genannte Alternative: bis zu 64 Durchsageziele. Der Client löst 'alle Crews' selbst aus der NetzListe auf. Damit braucht der Server keine neue Auflösungslogik, und das Datagramm bleibt klein (höchstens 569 Byte).
- **Funk-Befund 5 (Teil): bei gedrückter Taste nach 1 s ein zusätzliches Ping senden**: Nicht nötig. Ping läuft ohnehin fest alle 2 s, KEEPALIVE ebenfalls alle 2 s, und die Ausfallerkennung sieht beide Richtungen binnen 3 s. Der Rest des Befunds (udpGesendet, sofortige KEEPALIVE-Probe, Tunnel mit Anzeige) ist übernommen.
- **Netz-Befund 1 (Alternative): EmpfangsBericht als Anfrage mit Antwort führen**: Nicht nötig, der feste Ping-Takt löst das Problem. Die Lebenderkennung hängt jetzt an ausbleibenden Ping-Antworten (6 s).

## Offene Punkte
- Spike TPM-Schlüssel als Client-Zertifikat in SslStream (Win10/TLS 1.2, Win11/TLS 1.3, Store CurrentUser/My nötig?), dazu: liegt der Software-KSP-Schlüssel wirklich außerhalb des eigenen Prozesses und damit außerhalb eines WER-Abbilds?
- Wer in der Gruppe nutzt noch Windows 10? Bestimmt, wie lange TLS 1.2 zugelassen bleibt (nurTls13)
- .NET-10-Details ungeprüft: AllowTlsResume, CipherSuitesPolicy mit TLS-1.2-Suiten unter Linux, Ablehnung doppelter JSON-Schlüssel, SocketFlags.Truncated bei ReceiveMessageFromAsync, SetRawSocketOption für TCP_NOTSENT_LOWAT, Ausgabe von UnsafeRelaxedJsonEscaping, Thread-Sicherheit von AesGcm (für spätere Parallelisierung)
- RFC-Stellen wörtlich gegenlesen: 8446 5.3/5.5, 9001 6.6, 9000 8.1/9.3/9.3.2/9.3.3, 8085 3.5, 4787, 6479, 6716 3.1, 6562, 3550 6.4.1, 8305, 8628 5.4; das Zusammenfassungswerkzeug lieferte für RFC 9000 widersprüchliche Antworten
- Ports: ist 443/TCP zu Hause frei (Foundry, Reverse Proxy), passt 47443/UDP, Portfreigabe am Router
- Heim-IP für alle Mitglieder sichtbar: Zeitpunkt für Umzug zum Anbieter oder Vorposten mit erhaltener Absenderadresse
- Persistenz: SQLite mit Gruppen-Commit oder eigene JSON-Dateien mit atomarem Ersetzen
- Bot-Bibliothek (NetCord oder Discord.Net) samt THIRD-PARTY-NOTICES; geht GET /guilds/{gilde}/members/{id} für den Abgleich ohne das privilegierte Intent GUILD_MEMBERS (Spike)?
- Datenschutzerklärung: Konto, Discord-ID und Benutzername, Geräte, Pegel, Ablage, Raid-Helper-Snapshot, Vorrangspur (HMAC-Präfixe, 14 Tage), Messwerte, Löschfristen (30 Tage ohne Gerät), Weg zur Löschung
- Fremde Durchsicht des Review-Pakets und unabhängige Gegenrechnung von TV-A bis TV-F, 8.4, 8.5 und 8.11 (P-16): wer?
- Alle Zahlenwerte sind Startwerte, besonders die neuen (Ping 2 s/6 s, KEEPALIVE 2 s, Umbindungsfrist 500 ms, Fremdbudget 60/s, Handshake-Topf 200/s, Vorrangspur 14 Tage, Platzfrist 10 min, Reservierung 12 h, Opus 400 Byte, Byte-Topf 16.000 B/s, Pacer 35 Mbit/s und Zuschlag 26 Byte, Warteschlange 20 ms, Empfangsalter 60 ms, Stabilitätsfrist 250 ms); per Lasttest und Raidabend bestätigen
- A23/A8: Wert des Mindestpegels für Global und Durchsage im Client (C7)
- Raid-Helper: Premium, Template, Comp-Tool, Abbildung className/specName auf Rolle und Schiff, Server-Key für RaidHelperEvents vorhanden?, Rechte aus leaderId/coLeaders (v1: nein)
- Automatische Gerätefreigabe über eine Discord-Rolle (v1: nein), TPM-Attestation (v2), Freigabe für Geräte privilegierter Konten durch zwei Admins im Client (v2)
- Sollen auch RAIDLEITER-Geräte nur über das Admin-Werkzeug laufen (Prüferempfehlung), obwohl es in der Heimphase nur im LAN erreichbar ist?
- Mehrere gleichzeitige Geräte je Konto (v1: neue Sitzung verdrängt die alte) - gewünscht?
- Durchsage mit Stille (Server hält andere Netze an) als Option für v2?
- Autoritative DNS-Auflösung mit eigenem kleinem DNS-Client oder nur DnsQuery_W mit Cache-Umgehung: Spike, Nutzen gegen zusätzlichen Parser-Code
- IPv6 im Heimbetrieb (Router-Freigabe, wechselndes Telekom-Präfix, LXC)
- Notfallplan bei Serverausfall im Einsatz (Discord-Voice als Rückfall) mit der Gruppe absprechen
- Profilschlüssel ist Pflicht vor v1: wer erzeugt und verwahrt ihn offline samt Reserve, eigener Schlüssel oder derselbe wie für das Update-Manifest, wo liegt der Reserve-Serverschlüssel?
- Programmname und damit ALPN-Kennzeichen: seit 19.10.2026 entschieden (JG TacRelay, tacrelay/1)
- FRITZ!Box: tatsächlichen Netto-Upload messen, Wirkung der Priorisierung als Echtzeitanwendung prüfen, Uhrzeit der automatischen Neuverbindung festlegen
- Kernel-Zeitstempel (SO_TIMESTAMPNS) nur per P/Invoke auf recvmsg: Spike, ob sich das gegenüber dem Stopwatch-Stempel lohnt
- Verbindungsaufbau nach RFC 8305 ist in .NET nicht eingebaut: Aufwand, oder in der Heimphase (nur IPv4) zurückstellen
- Namenstäuschung: Discord-Namen sind seit A53 eindeutig (NFKC, Großschreibung) und nur vom Admin änderbar; braucht es in v2 zusätzlich eine Verwechslerprüfung nach UTS #39 (etwa kyrillisches "а" statt "a")?
- Reicht ein Relay-Thread für Szenario D beim Anbieter, oder muss dort parallelisiert werden (P-31)?
- Stufe 1, Sarevocs Antworten vom 30.09.2026 eingebaut (Teil 2); offen: braucht es `mitgliedskarte.rollenFuerAlle` neben der Freigabe je Nutzer noch
- Clientseite der Stufe 1 gebaut (Teil 2); offen: Admin-Schlüssel im echten TPM prüfen, Probefunk mit Test-Admin-Schlüssel, Journal im Verwaltungsteil, geführte Einrichtung (M6)
- Ratengrenze Admin 1/s mit Burst 3, AdminNonce seit Teil 2 im eigenen Eimer: am Raidabend bestätigen
- Zeitsynchronisierung am Host als Pflicht für TOTP: Prüfung beim Start oder nur Hinweis in BETRIEB.md?
- Beauftragung (Stufe 2): Server und Protokoll seit 0.3.0 gebaut; offen sind die Clientseite, der Bot-Dienst, Aufträge in Sicherungen außerhalb von `funk-admin sicherung` (Proxmox, Handkopien) und die Prüfung der Annahmen der Vorlage (seit 0.13.0 Star Citizen) durch den Admin
- Praesenz voll im ungünstigsten Fall bei Faktor 1,73 unter der Rahmengrenze (4.10): in Teilen senden oder Details nur auf Anfrage, falls es in der Praxis eng wird
