// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Admin;

/// <summary>Hilfetext des Admin-Werkzeugs.</summary>
internal static class Hilfe
{
    public const string Text = """
        funk-admin <befehl> [argumente]

        Jeder Befehl nimmt nur seine Optionen. Eine unbekannte, doppelte oder vertippte Option bricht ab (Exit 2),
        bevor etwas geschieht. Werte mit Leerzeichen gehören in Anführungszeichen. Alles wegnehmen (SC-Namen, Rechte,
        Discord-Name) geht nur ausdrücklich mit --leeren.

        Exitcodes: 0 gut, 1 Fehler, 2 Aufruf falsch, 3 Datei gibt es schon, 4 einrichten wartet auf das Profil.
        Bei 2 und 3 ist nichts geschehen.

        Offline (Dateien, kein Server nötig):
          schluessel-erzeugen --zertifikat <crt> --schluessel <key> [--name Funk-Server] [--ersetzen]
          geheimnis-erzeugen --datei <pfad> [--ersetzen]
          totp-schluessel-erzeugen --datei <pfad> [--ersetzen]   (funk-totp-schluessel, per LoadCredential, nie in eine Sicherung)
          profilschluessel-erzeugen --schluessel <key> [--ersetzen]   (nur am Offline-Rechner, nie auf dem Server)
            Eine vorhandene Datei bleibt; ersetzt wird nur mit --ersetzen, und dann gilt der alte Schlüssel nicht mehr.
          profil-schreiben --name <n> --version <u32> --host <name[/v4|/v6|/v4v6]>... --tcp-port <p>
                           --pin <b64> --pin <b64> [--nameserver <n>]... [--gueltig-tage 365] --aus <daten.json>
          profil-signieren --daten <daten.json> --schluessel <profil.key> --aus <profil.json>
          profil-pruefen --profil <profil.json> --spki <b64> [--spki <b64>]

        Einrichten (auf dem Server; --konfig <server.json> oder FUNK_KONFIG, erster-admin auch --socket <pfad>;
        Schlüsselordner mit --credentials, sonst /etc/funk/credentials):
          konfig-schreiben --aus <server.json> --binden <ip> [--binden <ip>] [--tcp-port 8443] [--udp-port 47443]
                           [--oeffentlicher-tcp-port <p>] [--oeffentlicher-udp-port <p>] [--profil <pfad>] [--zustand <ordner>]
            (schreibt eine gültige server.json, nie über eine vorhandene)
          pruefen [--vollstaendig] [--credentials <ordner>] [--spki <b64>]... [--host <name>]
            (--vollstaendig: Schlüsseldateien und Rechte, Pin, Signatur, Ablauf, DNS, Uhr, freie Ports)
          profil-anfrage --host <name[/v4|/v6|/v4v6]> [--tcp-port <p>] [--zertifikat <crt>] [--credentials <ordner>] [--aus <datei>]
            (die Zeile TR-PROFILANFRAGE v1 für den Eigentümer und die Kennung zum Vorlesen; ohne --tcp-port und --zertifikat aus --konfig)
          erster-admin   (offene Anträge, Kennung aus dem Client; freigeben, ADMIN, Eigentümer, vorsehen, dann ernennen)
          einrichten [--profil <profil.json>] [--umzug <paket>] [--mit-serverschluessel]
            (als root aus funk-einrichten.sh: beim ersten Lauf sechs Fragen (mit Router acht), dann nur, was fehlt; Benutzer, Schlüssel,
             server.json, Units, Kurzbefehl funk-admin, Profil prüfen, starten; ohne Profil Exit 4 mit der Anfragezeile)

        Auf dem Stick des Eigentümers (offline, fragt nach):
          profil-erneuern --bisher <profil.json> --schluessel <profil.key> [--anfrage <zeile|datei>] [--reserve <crt|key|pin>]
                          [--gueltig-tage 365]
            (zeigt das Profil, nimmt die Anfragezeile oder profilanfrage.txt oder verlängert nur, fragt nach der Kennung,
             sichert die alte Fassung als profil-v<N>.json und legt eine Kopie nach zum-verschicken)

        Umzug (Dienst vorher anhalten: systemctl stop funk-server; --konfig <server.json> oder FUNK_KONFIG):
          umzug-packen --aus <paket> [--ohne-totp] [--mit-serverschluessel] [--credentials <ordner>]
            (verschlüsselt, zeigt die Passphrase einmal; ohne funk-geheimnis und vorrangspur)
          umzug-einspielen --paket <paket> [--mit-serverschluessel] [--besitzer funk] [--credentials <ordner>]
            (fragt die Passphrase, legt Vorhandenes zur Seite, setzt Besitzer und Rechte)

        Über den laufenden Server (--konfig <server.json> oder --socket <pfad>, [--json-aus]):
          status | konten | netzliste | vorrangspur | vorrangspur-leeren | messwerte | profil-neu-laden
          sicherung [--ziel <verzeichnis>]      (nur Zustandsdateien, nie Schlüssel oder Geheimnis)
          sperrliste                             (gesperrte Adressen als Kennung wie im Logbuch, mit Ende und Anlass)
          entsperren --eintrag <kennung> | --ip <adresse> | --alle
          konto-anlegen --discord <id> --benutzername <n> [--name <n>] [--stufe ADMIN|RAIDLEITER|FUNKER]
          verknuepfen --code XXXX-XXXX-XXXX --discord <id> --benutzername <n> [--name <n>] [--stufe ...]
          konto-sperren|konto-entsperren|konto-loeschen --konto <id>
          stufe --konto <id> --stufe <STUFE>     rechte --konto <id> --rechte A,B,C | --leeren     name --konto <id> --name <n>
          mitgliedschaft --discord <id> --status OK|NICHT_MITGLIED|ROLLE_FEHLT
          geraete [--konto <id>] [--wartend] [--nicht-seit JJJJ-MM-TT]   (--nicht-seit: seit dem Tag nicht verbunden)
          antraege                               (offene Anträge mit Namen, vergebenen Namen, passenden Vormerkungen und bestehenden Konten)
          freigeben <anzeige|fingerabdruck> [--vormerkung <id>] [--an-konto <id>]
          ablehnen <anzeige|fingerabdruck>
          entfernen|sperren <anzeige|fingerabdruck> [--grund <text>]
          sperre-aufheben <anzeige|fingerabdruck>   (hebt eine Gerätesperre auf, auch nach dem Löschen des Kontos; der PC kann neu beantragen)
          geraet-umhaengen <anzeige|fingerabdruck> --an-konto <id>   (freigegebenes Gerät an ein anderes Konto, etwa nach einem Antrag ins falsche)
          scnamen --konto <id> --namen A,B,C | --leeren     mitglieder     journal [--konto <id>] [--anzahl 50]
          discordname --konto <id> --name "<Discord-Name>" | --leeren   (eindeutig; im Client ändert ihn nur ein Admin)

        Eigentümer und Admins (nur hier, nie im Client):
          eigentuemer --konto <id>
          admins
          admin-vorsehen --konto <id>
          admin-ernennen --konto <id> --kennung XXXX-XXXX-XXXX-XXXX     (Kennung vom Admin am Telefon vorlesen lassen)
          admin-einrichtung-neu --konto <id> [--anzeigen]               (--anzeigen: otpauth-URI und Schlüssel hier ausgeben)
          zweitfaktor-bestaetigen --konto <id> --code <6 Ziffern>        (erster Code nach --anzeigen, gibt Notfallcodes)
          notfallcodes --konto <id>                                     (10 neue, die alten gelten nicht mehr)
          admin-sperren|admin-entsperren|admin-zuruecksetzen --konto <id>
          admin-entziehen --konto <id> [--stufe RAIDLEITER|FUNKER]
          admin-schluessel-entfernen --konto <id> --kennung XXXX-XXXX-XXXX-XXXX
          netz-setzen --id <n> --name <n> [--offen NEIN|HOEREN|HOEREN_SENDEN] [--standard] [--taste] [--nur-taste] [--reihenfolge n]
            (ohne Schalter Sprachaktivierung, --va wirkt genauso; --taste: ab Werk Taste; --nur-taste: nur Taste; Global immer Taste)
          netz-setzen --id 2 --name Kommando --offen HOEREN_SENDEN --standard --taste   (Kommando mit Taste)
          netz-setzen --id 120 --name Brücke --offen HOEREN_SENDEN --gruppe Schiffe --kanaele "Ruhe,Schiff 1,Schiff 2,Schiff 3"
            (Kanalnetz mit Ch.: die erste Bezeichnung ist die Ruhe, Ch. 0; --kanaele-datei <datei> nimmt eine Bezeichnung je Zeile.
             Ab dem ersten Kanalnetz kommen nur noch Clients ab 0.6.0 hinein)
          netz-setzen --json '<objekt wie in NetzListe>'
          rolle-setzen --id <n> --name <n> [--selbst] [--braucht-schiff] [--kapazitaet n] --feld feld:netz[:h][:s][:std][:fest]...
          rolle-setzen --json '<objekt>'     schiff-setzen --json '<objekt>'
          netz-loeschen|schiff-loeschen|rolle-loeschen --id <n>
          einstellung [--selbstwahl an|aus] [--nachzuegler an|aus] [--durchsage-nur-taste an|aus]
          verdraengung-freigeben --konto <id>

        Einsatzleitung und Flotte:
          auftrag --konto <id> [--von HH:MM] [--bis HH:MM] [--rechte A,B] [--grund <text>]   (Auftrag für einen Abend, ohne --bis bis 04:00)
          auftrag-entziehen --konto <id>     auftraege
          flotte-vorlage [--name starcitizen]   (Vorlage Star Citizen: 10 Kanäle mit Ch., 14 Rollen; legt neben die alte Flotte, nur
                                              ohne Kanäle mit Ch.; nennt die Annahmen, die der Admin prüfen soll; danach kommen nur
                                              noch Clients ab 0.6.0 hinein)

        Kanalsperren (ab 0.6.0; die Konsole darf auch aufheben, was die Einsatzleitung gesetzt hat):
          kanalsperre-setzen --konto <id> | --alle --netz <nummer oder name> [--ch <n>] [--bis HH:MM]   (für eine Person oder für alle ohne
                                              Einsatzleitung; ohne --ch der ganze Kanal, ohne --bis bis 04:00, höchstens 24 Stunden)
          kanalsperre-aufheben --konto <id> | --alle --netz <nummer oder name> [--ch <n>]     kanalsperren
        """;
}
