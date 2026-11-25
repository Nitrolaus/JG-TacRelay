// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json.Nodes;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Server.Kern;

/// <summary>
/// Antrag ins falsche Konto (Sarevocs Fall vom 01.10.2026): Sarevoc gab sein Gerät per Antrag frei und bekam ein neues Konto mit seinen Namen
/// (FUNKER) statt seines ADMIN-Kontos aus 0.1.0. Dagegen nennt <c>antraege</c> jetzt bestehende Konten mit passenden Namen samt
/// dem Befehl zum Anhängen, und <c>geraet-umhaengen</c> repariert es hinterher (HEIMSERVER-PROXMOX.md, Abschnitt 14).
/// </summary>
public sealed partial class Zentrale
{
    /// <summary>Aktive Konten (keine Anträge), deren Namen zu SC- oder Discord-Name des Antrags passen.</summary>
    private static List<I.Konto> PassendeKonten(I.Kontenstand stand, I.Konto antrag)
    {
        var namen = new[] { antrag.Antrag?.ScName, antrag.Antrag?.DiscordName ?? antrag.DiscordName };
        return [.. stand.Konten.Values
            .Where(k => !k.IstAntrag && k.KontoId != antrag.KontoId && k.Status == I.KontoStatus.Aktiv && I.Kontovorschlag.Passt(namen, I.Kontovorschlag.NamenVon(k)))
            .OrderBy(k => k.KontoId)];
    }

    /// <summary>
    /// "  BESTEHENDES KONTO? Mustermann (Konto 3), ADMIN: gehört das Gerät dazu (nur wenn sein neues Gerät dieselbe Kennung zeigt), dann
    /// funk-admin freigeben ... --an-konto 3". Die Namen eines Antrags wählt der Antragsteller selbst; der Hinweis erscheint also
    /// gerade bei einem Nachahmer, deshalb verlangt er den Vergleich der Kennung (Gegenprüfung 0.3.0).
    /// </summary>
    private string KontoHinweis(I.Konto k, string anzeige) => string.Create(CultureInfo.InvariantCulture,
        $"  BESTEHENDES KONTO? {KontoText(k.KontoId)}, {I.Rechteregeln.Drahtname(k.Stufe)}: gehört das Gerät dazu (nur wenn sein neues Gerät dieselbe Kennung zeigt), dann funk-admin freigeben {anzeige} --an-konto {k.KontoId}");

    /// <summary>Konsole: <c>geraet-umhaengen &lt;gerät&gt; --an-konto &lt;id&gt;</c>.</summary>
    private VerwaltungsAntwort GeraetUmhaengenKonsole(JsonObject a)
    {
        if (Text(a, "geraet") is not { } suche) return VerwaltungsAntwort.Fehler("geraet fehlt (Anzeigeform oder Fingerabdruck)");
        if (Zahl(a, "anKonto") is not { } ziel) return VerwaltungsAntwort.Fehler("--an-konto fehlt: an welches Konto? (funk-admin konten zeigt die Nummern)");
        var f = Konten.GeraetSuchen(suche, out var g);
        if (f != I.Fehlercode.Keiner || g is null) return VerwaltungsAntwort.Fehler(f == I.Fehlercode.Ungueltig ? "Anzeigeform mehrdeutig, vollen Fingerabdruck nehmen" : "Gerät unbekannt");
        var vorher = g.KontoId;
        var e = Konten.GeraetUmhaengen(I.Akteur.AdminWerkzeug, g.GeraetId, ziel);
        if (!e.Ok)
        {
            return VerwaltungsAntwort.Fehler(e.Fehler switch
            {
                I.Fehlercode.Unbekannt => "Konto " + ziel.ToString(CultureInfo.InvariantCulture) + " gibt es nicht (oder es ist ein offener Antrag). Nichts geändert.",
                I.Fehlercode.Ungueltig when g.Status == I.GeraetStatus.Wartet => "Das Gerät wartet noch auf die Freigabe: funk-admin freigeben " + g.Anzeige + " --an-konto "
                    + ziel.ToString(CultureInfo.InvariantCulture) + ". Nichts geändert.",
                I.Fehlercode.Ungueltig => "Nur ein freigegebenes Gerät eines anderen Kontos lässt sich umhängen. Nichts geändert.",
                I.Fehlercode.KontoGesperrt => "Das Zielkonto ist gesperrt. Nichts geändert.",
                I.Fehlercode.GeraeteLimit => "Das Zielkonto hat schon drei aktive Geräte (funk-admin geraete --konto " + ziel.ToString(CultureInfo.InvariantCulture) + "). Nichts geändert.",
                _ => I.Drahtnamen.Drahtname(e.Fehler),
            });
        }
        Vermerken("GERAET_UMGEHAENGT", KonsoleDurch, ziel, $"Konsole: Gerät {g.Name} ({g.Anzeige}) von {KontoText(vorher)} an {KontoText(ziel)} gehängt");
        WirkungenAusfuehren(e.Wirkungen);
        KontoGeaendert(vorher);
        KontoGeaendert(ziel);
        Log.Info("Admin-Werkzeug: Gerät umgehängt von " + Log.Schwaerzung.Konto(vorher) + " an " + Log.Schwaerzung.Konto(ziel));
        var rest = Konten.Stand.Geraete.Values.Count(x => x.KontoId == vorher && x.IstAktiv);
        var text = "OK: " + g.Anzeige + " gehört jetzt zu " + KontoText(ziel) + "; der Client verbindet sich gleich neu.";
        if (rest == 0)
            text += " " + KontoText(vorher) + " hat kein Gerät mehr. Wenn es nur aus Versehen entstand: funk-admin konto-loeschen --konto "
                + vorher.ToString(CultureInfo.InvariantCulture) + " (Namen gibt es danach frei, etwa für discordname und scnamen am Zielkonto).";
        return VerwaltungsAntwort.Gut(text, new JsonObject { ["geraetId"] = g.GeraetId, ["vorher"] = vorher, ["kontoId"] = ziel });
    }
}
