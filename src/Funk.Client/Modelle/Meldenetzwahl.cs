// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>Eine Wahl für "Melde-Taste spricht in": ein Netz der Flotte oder <c>null</c> für die Voreinstellung (Kommando).</summary>
public sealed record MeldenetzWahl(ushort? NetzId, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Wohin die Melde-Taste spricht (RAID-S2-ENTWURF Abschnitt 4): ins gewählte Netz, ohne Wahl ins Netz "Kommando", wenn die Flotte
/// eines hat. Gespeichert wird die Netz-ID; fehlt das Netz später, gilt wieder die Voreinstellung. Reine Rechnung über der Lage.
/// </summary>
public static class Meldenetzwahl
{
    /// <summary>Name des Netzes, das ohne Wahl gilt (Flottenvorlage Star Citizen: Kommando).</summary>
    public const string Voreinstellung = "Kommando";

    /// <summary>Das Meldenetz nach Wahl und Lage, oder <c>null</c>, wenn es keins gibt.</summary>
    public static NetzInfo? Aufloesen(ushort? gewaehlt, FunkLage lage)
    {
        ArgumentNullException.ThrowIfNull(lage);
        if (gewaehlt is { } id && lage.NetzVon(id) is { IstGlobal: false } n) return n;
        return lage.Netze.FirstOrDefault(x => !x.IstGlobal && string.Equals(x.Name.Trim(), Voreinstellung, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Warum Melden gerade nicht geht, als Satz für die Meldezeile, oder <c>null</c>, wenn es geht: Es braucht das Netz und dort ein
    /// Feld mit Senderecht (der Server prüft je Paket die Mitgliedschaft mit senden, 6.3), seit 0.6.0 nicht auf Ruhe und nicht gesperrt.
    /// </summary>
    public static string? Hindernis(NetzInfo? netz, FunkLage lage)
    {
        ArgumentNullException.ThrowIfNull(lage);
        if (!lage.IstAktiv) return "Melden geht nicht: nicht verbunden";
        // Seit 0.10.0 steht die Wahl unter Einstellungen > Tasten im Klappbereich (bis 0.9.3 die Karte "Globale Tasten"), seit 0.11.1 heißt er
        // Erweitert. Seit 0.11.0 kürzer, damit es auch über der 5er-Liste in eine Zeile passt (bis dahin "Melden geht nicht: kein Meldekanal - unter
        // Einstellungen > Tasten > ... wählen"); der Weg bleibt genannt.
        if (netz is null) return "Melden: kein Meldekanal (Tasten > Erweitert)";
        return Ziel(netz, lage).Hindernis;
    }

    /// <summary>Für die Melde-Taste beim Drücken: Netz, Name und was dagegen spricht (aus dem Thread der Tastenquelle; die Lage ist unveränderlich).</summary>
    public static Klang.MeldeEntscheid Entscheid(ushort? gewaehlt, FunkLage lage)
    {
        ArgumentNullException.ThrowIfNull(lage);
        var netz = Aufloesen(gewaehlt, lage);
        if (netz is null) return new Klang.MeldeEntscheid(null, "", Hindernis(null, lage));
        var (id, hindernis) = Ziel(netz, lage);
        return new Klang.MeldeEntscheid(id, lage.KanalName(id), lage.IstAktiv ? hindernis : Hindernis(netz, lage));
    }

    /// <summary>
    /// Wohin die Meldung geht: bei einem gewöhnlichen Netz das Netz, bei einem Kanalnetz (6.16, Meldekanal) der Kanal, den du dort in
    /// einem Feld hast, das niedrigste Feld zuerst. Dazu, was dagegen spricht: kein Feld, nur die Ruhe, eine Sperre, kein Senderecht.
    /// </summary>
    private static (ushort Id, string? Hindernis) Ziel(NetzInfo netz, FunkLage lage)
    {
        if (!netz.IstKanalnetz)
        {
            if (lage.SperreVon(netz.Id) is { } s) return (netz.Id, "Melden geht nicht: " + lage.SperrSatz(s));
            return (netz.Id, lage.Felder.Any(f => f.NetzId == netz.Id && f.Senden) ? null : "Melden geht nicht: kein Senderecht in " + netz.Name);
        }
        var felder = lage.Felder.Where(f => lage.ChVon(f.NetzId) is not null && lage.NetzVon(f.NetzId)?.Id == netz.Id).OrderBy(f => f.Feld).ToList();
        if (felder.Count == 0) return (netz.Id, "Melden geht nicht: kein Senderecht in " + netz.Name);
        var kanaele = felder.Where(f => !Kanalrechnung.IstRuhe(f.NetzId)).ToList();
        if (kanaele.Count == 0) return (felder[0].NetzId, "Melden geht nicht: " + netz.Name + " steht auf Ruhe");
        var frei = kanaele.Where(f => lage.SperreVon(f.NetzId) is null).ToList();
        if (frei.Count == 0) return (kanaele[0].NetzId, "Melden geht nicht: " + lage.SperrSatz(lage.SperreVon(kanaele[0].NetzId)!));
        var ziel = frei.FirstOrDefault(f => f.Senden) ?? frei[0];
        return (ziel.NetzId, ziel.Senden ? null : "Melden geht nicht: kein Senderecht in " + lage.KanalName(ziel.NetzId));
    }

    /// <summary>Die Wahlen für die Einstellungen: die Voreinstellung zuerst, dann jedes Netz der Flotte außer Global.</summary>
    public static IReadOnlyList<MeldenetzWahl> Wahlen(FunkLage lage, ushort? gewaehlt)
    {
        ArgumentNullException.ThrowIfNull(lage);
        var kommando = Aufloesen(null, lage);
        var liste = new List<MeldenetzWahl>
        {
            new(null, kommando is null ? "ab Werk: " + Voreinstellung + " (die Flotte hat keins)" : "ab Werk: " + kommando.Name),
        };
        liste.AddRange(lage.Netze.Where(n => !n.IstGlobal).Select(n => new MeldenetzWahl(n.Id, n.Name)));
        // Ein gewähltes Netz, das die Lage (noch) nicht kennt, bleibt sichtbar, damit die Wahl nicht verloren geht.
        if (gewaehlt is { } id && liste.All(w => w.NetzId != id)) liste.Add(new MeldenetzWahl(id, "Kanal " + id.ToString(System.Globalization.CultureInfo.CurrentCulture) + " (gerade nicht da)"));
        return liste;
    }
}
