// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Testserver;

/// <summary>Aufrufhilfe des Testservers.</summary>
internal static class Hilfe
{
    public const string Text = """
        funk-testserver: der Dienst mit Testzugang auf Loopback, nur zum Testen (nie im Betrieb)

          --konfig <server.json> --testzugang <port> [--testzugang-adresse 127.0.0.1]
                [--echo] [--echo-netz Echo] [--freigabe-automatisch ADMIN|RAIDLEITER|FUNKER]
                Mit vorhandener Konfiguration, Schlüsseln und Zustand (Lasttest).

          --probe <probefunk.json> [--zustand <verzeichnis>] --testzugang <port|0> [--echo] [--port-datei <pfad>]
                [--socket <pfad>]
                Probefunk: alles Geheime nur im Speicher, frisches Zustandsverzeichnis (wird beim Beenden gelöscht),
                Netze, Rollen und Schiffe aus der Probe-Konfiguration, Geräte über den Testzugang ohne Handarbeit frei.
                Der Verwaltungssocket liegt im Zustand; ist der Pfad länger als 100 Byte (UTF-8), in C:\Claude\tmp
                (falls vorhanden), sonst im Temp-Verzeichnis.

          --client <programm> [--client-argumente "<argumente>"] [--client-freigegeben]
                Startet ein Begleitprogramm und endet mit ihm, mit dessen Exitcode. {testzugang} in den Argumenten wird
                127.0.0.1:<port>; ohne --client-argumente gilt "--testzugang {testzugang}" (der Client JG TacRelay).
                JG TacRelay zeigt Fenster und öffnet Mikrofon, Wiedergabe und globale Tasten; es startet nur mit
                --client-freigegeben (Probefunk starten.cmd).
                Beispiel Prüfung: --client funk-testclient.exe --client-argumente "echo-probe --testzugang {testzugang}"

          --echo: Echo-Funker im Kanal Echo; er sendet jede Sprechphase nach 500 ms Pause zurück (höchstens 30 s).
          Beenden: "ende" auf stdin, Strg+C oder Ende des Begleitprogramms.
        """;
}
