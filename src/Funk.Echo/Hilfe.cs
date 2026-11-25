// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Echo;

/// <summary>Aufrufhilfe des Echo-Funkers.</summary>
internal static class Hilfe
{
    public const string Text = """
        funk-echo: Echo-Funker. Hört im Kanal Echo mit und sendet jede Sprechphase nach 500 ms Pause zurück
        (höchstens 30 s je Phase, eine Phase zur Zeit, das eigene Echo nimmt er nie wieder auf).

          Betrieb (TLS mit Pin, Linux):
            funk-echo --profil <profil.json> --identitaet <geraet.pem> [--adresse <host|ip>] [--port <n>]
                      [--profilschluessel <SPKI Base64>] [--netz Echo] [--name Echo-Funker] [--ruhe <sekunden>]
            Fehlt geraet.pem, wird sie angelegt (0600). Beim ersten Start zeigt der Funker einen Registrierungscode und die
            beiden Befehle zum Abschreiben (dotnet /opt/funk/funk-admin.dll verknuepfen ..., dann ... freigeben ...).
            Der Kanal Echo muss offen sein (HOEREN_SENDEN) oder der Funker wird von der Einsatzleitung hineingesetzt. Ein
            Kanal mit Ch. geht nicht: --netz nennt einen gewöhnlichen Kanal (Name oder Nummer).

          Verdrängt (ein anderes Gerät desselben Kontos, etwa die Echo-Probe mit seiner Discord-Zahl) oder vom Server
          abgewiesen: Der Funker verbindet nicht selbst neu. Nach der Ruhezeit (--ruhe, Voreinstellung 600 s) endet er mit
          Code 3 (verdrängt) bzw. 4 (abgewiesen), und systemd startet ihn neu (Restart=on-failure).

          Probe (Testserver auf Loopback):
            funk-echo --testzugang 127.0.0.1:<port> [--netz Echo]

          Unter Windows geht TLS nur mit FUNK_TLS_IM_PROFIL=1, weil Schannel den Schlüssel im Benutzerprofil ablegt.
          Beenden: Strg+C bzw. SIGTERM (Code 0).
        """;
}
