// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json.Nodes;
using Funk.Server.Kern;

namespace Funk.Admin;

/// <summary>
/// <c>funk-admin erster-admin</c> (EINRICHTEN-UMZUG-ENTWURF, Ende von "Vorschlag Einrichtung"): auf einem frischen Server den
/// ersten Admin in einem Zug. Zeigt die offenen Anträge, fragt die Kennung aus dem Client (seit 0.10.0 "von der Startseite"), gibt genau
/// diesen Antrag frei und setzt Stufe ADMIN, Eigentümer und Admin vorsehen. Dann fragt es die Kennung des Admin-Schlüssels, die
/// der Client nach "Verwaltung einrichten" zeigt, und ernennt; leer heißt: später mit admin-ernennen. Gibt es schon einen
/// Eigentümer, rührt es nichts an. Redet nur über den Verwaltungssocket, wie alle anderen Befehle.
/// </summary>
public sealed class ErsterAdmin(TextReader ein, TextWriter aus, Func<string, JsonObject, Task<VerwaltungsAntwort>> senden)
{
    private readonly Dialog _d = new(ein, aus);

    private int Nichts(string grund)
    {
        aus.WriteLine(grund + " Nichts geändert.");
        return 1;
    }

    private static string Kurz(string kennung) => new([.. kennung.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);

    public async Task<int> AusfuehrenAsync()
    {
        var konten = await senden("konten", []).ConfigureAwait(false);
        if (!konten.Ok) return Nichts("Der Server antwortet nicht wie erwartet: " + konten.Text);
        if (konten.Daten is JsonArray ks && ks.FirstOrDefault(k => k?["eigentuemer"]?.GetValue<bool>() == true) is { } eigentuemer)
            return Nichts("Es gibt schon einen Eigentümer: Konto " + eigentuemer["kontoId"] + " (" + eigentuemer["name"] + "). erster-admin ist nur für einen frischen Server; "
                + "sonst die einzelnen Befehle (freigeben, stufe, admin-vorsehen, admin-ernennen).");

        var antraege = await senden("antraege", []).ConfigureAwait(false);
        if (!antraege.Ok) return Nichts("Anträge nicht lesbar: " + antraege.Text);
        if (antraege.Daten is not JsonArray liste || liste.Count == 0)
            return Nichts("Keine offenen Anträge. Erst im Client verbinden (Deine Namen, so anmelden); dann wartet er auf die Freigabe, und hier noch einmal erster-admin.");
        aus.WriteLine("Offene Anträge:");
        aus.WriteLine(antraege.Text);
        aus.WriteLine();

        JsonNode? antrag = null;
        for (var versuch = 0; versuch < 3 && antrag is null; versuch++)
        {
            // Seit 0.10.0: Die Kennung steht auf der Startseite des Clients (den Bereich Verbindung gibt es seit 0.6.0 nicht mehr).
            var eingabe = _d.Zeile("Kennung von der Startseite deines Clients (XXXX-XXXX-XXXX-XXXX): ");
            if (eingabe is null) return Nichts("Abgebrochen.");
            antrag = liste.FirstOrDefault(a => a?["anzeige"]?.GetValue<string>() is { } an && Kurz(an) == Kurz(eingabe) && Kurz(eingabe).Length > 0);
            if (antrag is null) aus.WriteLine("Zu dieser Kennung gibt es keinen offenen Antrag. Sie steht im Client auf der Startseite, solange er auf die Freigabe wartet.");
        }
        if (antrag is null) return Nichts("Keine passende Kennung.");
        var anzeige = antrag["anzeige"]!.GetValue<string>();
        var konto = antrag["kontoId"]!.GetValue<uint>();
        aus.WriteLine("Antrag " + anzeige + ": " + antrag["scName"] + " (Discord: " + antrag["discordName"] + ").");

        foreach (var (befehl, argumente, was) in new (string, JsonObject, string)[]
        {
            ("freigeben", new JsonObject { ["geraet"] = anzeige }, "freigegeben"),
            ("stufe", new JsonObject { ["konto"] = konto, ["stufe"] = "ADMIN" }, "Stufe ADMIN"),
            ("eigentuemer", new JsonObject { ["konto"] = konto }, "Eigentümer"),
            ("admin-vorsehen", new JsonObject { ["konto"] = konto }, "als Admin vorgesehen"),
        })
        {
            var a = await senden(befehl, argumente).ConfigureAwait(false);
            if (!a.Ok)
            {
                aus.WriteLine(befehl + " scheiterte: " + a.Text);
                aus.WriteLine("Bis hierher ist es geschehen; der Rest von Hand: funk-admin " + befehl + " ... (BETRIEB.md).");
                return 1;
            }
            aus.WriteLine("OK: Konto " + konto.ToString(CultureInfo.InvariantCulture) + " " + was + ".");
        }

        aus.WriteLine();
        aus.WriteLine("Jetzt im Client: Verwaltung, \"Verwaltung einrichten\". Er legt deinen Admin-Schlüssel an und zeigt dessen Kennung.");
        for (var versuch = 0; versuch < 5; versuch++)
        {
            var kennung = _d.Zeile("Kennung des Admin-Schlüssels (leer: später mit admin-ernennen): ")?.Trim();
            if (string.IsNullOrEmpty(kennung))
            {
                aus.WriteLine();
                aus.WriteLine("Später: funk-admin admin-ernennen --konto " + konto.ToString(CultureInfo.InvariantCulture) + " --kennung XXXX-XXXX-XXXX-XXXX");
                return 0;
            }
            var a = await senden("admin-ernennen", new JsonObject { ["konto"] = konto, ["kennung"] = kennung }).ConfigureAwait(false);
            if (a.Ok)
            {
                aus.WriteLine(a.Text);
                return 0;
            }
            aus.WriteLine(a.Text.StartsWith("kein offener Antrag", StringComparison.Ordinal)
                ? "Noch kein Antrag vom Client da: erst \"Verwaltung einrichten\", dann die Kennung noch einmal eingeben."
                : a.Text);
        }
        aus.WriteLine("Später: funk-admin admin-ernennen --konto " + konto.ToString(CultureInfo.InvariantCulture) + " --kennung XXXX-XXXX-XXXX-XXXX");
        return 1;
    }
}
