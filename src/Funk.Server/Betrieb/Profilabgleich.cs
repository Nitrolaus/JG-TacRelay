// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Server.Betrieb;

/// <summary>
/// Abgleich der server.json mit dem Serverprofil, das Welcome durchreicht (3.1). Die Clients nehmen Host und TCP-Port aus
/// dem Profil; <c>oeffentlicherTcpPort</c> sagt, welchen Port der Router nach außen gibt. Weichen beide ab, ist eines davon
/// falsch, und kein Client kommt an (Sarevocs Heimserver: Beispielkonfiguration mit 443, Profil mit 8443). Das ist eine Warnung,
/// kein Startfehler: Welcher der beiden Werte stimmt, weiß nur der Betreiber. Gewarnt wird beim Start und bei
/// <c>funk-server pruefen</c>.
/// </summary>
public static class Profilabgleich
{
    /// <summary>
    /// Warnung, wenn das Profil einen anderen TCP-Port nennt als <c>oeffentlicherTcpPort</c> (ohne Angabe <c>tcpPort</c>);
    /// <c>null</c>, wenn beide passen oder das System die Ports zuteilt (Port 0, Tests).
    /// </summary>
    public static string? PortWarnung(ServerKonfiguration konfiguration, ushort profilPort)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);
        var oeffentlich = konfiguration.TcpPortOeffentlich;
        if (oeffentlich == 0 || oeffentlich == profilPort) return null;
        var herkunft = konfiguration.OeffentlicherTcpPort is null ? "tcpPort (ohne oeffentlicherTcpPort)" : "oeffentlicherTcpPort";
        var p = profilPort.ToString(CultureInfo.InvariantCulture);
        var o = oeffentlich.ToString(CultureInfo.InvariantCulture);
        return "Das Serverprofil nennt TCP-Port " + p + ", server.json aber " + herkunft + " " + o + ". Die Clients verbinden sich auf Port "
            + p + " aus dem Profil. Leitet der Router " + p + " weiter, trag \"oeffentlicherTcpPort\": " + p + " in server.json ein; "
            + "sonst das Profil mit dem richtigen Port neu schreiben (funk-admin profil-schreiben --tcp-port) und signieren.";
    }

    /// <summary>
    /// Für <c>funk-server pruefen</c>: liest das Profil wie der Start (<c>Zentrale.ProfilLaden</c>) und gleicht es ab.
    /// <c>Fehler</c>: So startet der Server nicht. <c>Warnungen</c>: Er startet, aber etwas passt nicht zusammen.
    /// </summary>
    public static (string? Fehler, IReadOnlyList<string> Warnungen) Pruefen(ServerKonfiguration konfiguration)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);
        if (konfiguration.Profil is not { } pfad) return ("kein Profil konfiguriert", []);
        byte[] roh;
        try
        {
            var datei = konfiguration.Aufloesen(pfad);
            if (!File.Exists(datei)) return ("Profildatei fehlt: " + datei, []);
            roh = File.ReadAllBytes(datei);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ("Profildatei nicht lesbar: " + e.Message, []);
        }
        if (!I.SigniertesProfil.TryLesen(roh, out var signiert)) return ("Profil ist kein gültiges {daten, signatur}", []);
        if (!I.Serverprofil.TryLesen(signiert.Daten, out var inhalt))
            return (null, ["Die Daten im Serverprofil lassen sich nicht lesen; die Clients werden es ablehnen."]);
        return (null, PortWarnung(konfiguration, inhalt.TcpPort) is { } w ? [w] : []);
    }
}
