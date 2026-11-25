// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Globalization;
using Funk.Server.Kern;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Testserver;

/// <summary>
/// Freigabe eines Geräts ohne Handarbeit, nur für den Testserver: dieselben Schritte wie ein Admin mit
/// <c>funk-admin verknuepfen</c> und <c>freigeben</c> (3.4, 3.5), über die öffentlichen Methoden des Kontenbuchs in der
/// Arbeitsschleife der Zentrale. Die Rückfrage an den Client (3.4 Schritt 5) beantwortet der Testserver an seiner Stelle,
/// darum gibt es das nur für Geräte, die ihren Schlüssel über den Loopback-Testzugang nachgewiesen haben, und für den
/// Echo-Funker im selben Prozess. Der Dienst selbst kennt keinen solchen Weg; an seiner Logik ändert sich nichts.
/// </summary>
public static class Probefreigabe
{
    /// <summary>Discord-ID des Echo-Kontos im Testserver (keine echte ID, nur eindeutig).</summary>
    public const string EchoDiscordId = "100000000000000001";

    /// <summary>Verbindungsnummern der Rückfrage; negativ, damit sie nie mit echten Verbindungen zusammenfallen.</summary>
    private static long _verbindung = -1_000_000_000L;

    /// <summary>
    /// Verknüpft <paramref name="geraet"/> mit dem Konto zu <paramref name="discordId"/> (legt es mit Stufe und Name an, falls
    /// nötig), bestätigt und gibt frei. <paramref name="alteGeraeteEntfernen"/>: vorhandene Geräte dieses Kontos erst
    /// entfernen (ein Echo-Funker bekommt bei jedem Start einen neuen Schlüssel, das Limit ist 3). Liefert <c>null</c> bei
    /// Erfolg, sonst den Grund. Ein schon freigegebenes Gerät bleibt, wie es ist.
    /// </summary>
    public static Task<string?> FreigebenAsync(Zentrale zentrale, I.Fingerabdruck geraet, string geraetename, string discordId,
        string benutzername, string name, I.Stufe stufe, bool alteGeraeteEntfernen = false)
    {
        ArgumentNullException.ThrowIfNull(zentrale);
        return zentrale.Ausfuehren(() =>
        {
            var konten = zentrale.Konten;
            var stand = konten.Stand;
            if (stand.GeraetZuFingerabdruck(geraet) is { Status: I.GeraetStatus.Freigegeben }) return (string?)null;
            if (alteGeraeteEntfernen && stand.KontoZuDiscordId(discordId) is { } alt)
            {
                foreach (var g in stand.GeraeteVon(alt.KontoId).Where(g => g.Fingerabdruck != geraet).ToList())
                {
                    var e = konten.GeraetEntfernen(I.Akteur.AdminWerkzeug, g.GeraetId, dauerhaft: false, "Testserver: neues Gerät");
                    if (e.Ok) zentrale.WirkungenAusfuehren(e.Wirkungen);
                }
            }
            var verbindung = Interlocked.Decrement(ref _verbindung);
            var anfrage = konten.VerknuepfungAnfragen(new I.CodeBindung(verbindung, geraet, geraetename, I.SchluesselArt.Software),
                discordId, benutzername, name, I.Herkunft.AdminWerkzeug, stufe);
            if (anfrage.Fehler != I.Fehlercode.Keiner) return "verknuepfen: " + I.Drahtnamen.Drahtname(anfrage.Fehler);
            var antwort = konten.VerknuepfungBeantworten(verbindung, ja: true);
            if (antwort.Fehler != I.Fehlercode.Keiner) return "bestaetigen: " + I.Drahtnamen.Drahtname(antwort.Fehler);
            zentrale.WirkungenAusfuehren(antwort.Wirkungen);
            var freigabe = konten.GeraetEntscheiden(I.Akteur.AdminWerkzeug, antwort.GeraetId, I.Entscheidung.Freigeben);
            if (!freigabe.Ok) return "freigeben: " + I.Drahtnamen.Drahtname(freigabe.Fehler);
            zentrale.WirkungenAusfuehren(freigabe.Wirkungen);
            zentrale.Log.Info("Testserver: Gerät automatisch freigegeben, Konto " + zentrale.Log.Schwaerzung.Konto(antwort.KontoId));
            return null;
        });
    }

    /// <summary>Discord-ID aus dem Fingerabdruck (19 Ziffern, erste 1-9): je Gerät ein eigenes Probekonto.</summary>
    public static string DiscordIdAus(I.Fingerabdruck geraet)
    {
        var roh = BinaryPrimitives.ReadUInt64BigEndian(geraet.AlsBytes());
        return (1_000_000_000_000_000_000UL + roh % 9_000_000_000_000_000_000UL).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Discord-Benutzername aus der Anzeigeform: druckbares ASCII ohne Leerzeichen (3.3).</summary>
    public static string BenutzernameAus(I.Fingerabdruck geraet) => "probe-" + geraet.Anzeige[..9];
}
