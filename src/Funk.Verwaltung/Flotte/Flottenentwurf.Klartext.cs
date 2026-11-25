// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung.Flotte;

/// <summary>Wohin ein Hinweis der Übersicht "Heute" springt (seit 0.8.0).</summary>
public enum HinweisZiel
{
    Rolle,
    Kanal,
    Einheit,

    /// <summary>Die Kanäle mit dem Filter "alte Flotte", der genannte Kanal gewählt.</summary>
    KanaeleAlteFlotte,
}

/// <summary>
/// Ein Hinweis, den die Übersicht "Heute" selbst findet (seit 0.8.0): Klartext und wohin "Zeigen" springt. <paramref name="Suche"/> füllt
/// die Suche der Liste, etwa mit dem Namen einer doppelten Rolle, damit beide nebeneinander stehen.
/// </summary>
public sealed record FlottenHinweis(string Text, HinweisZiel Ziel, ushort Id, string? Suche = null);

/// <summary>Wo ein Kanal vorkommt (seit 0.8.0, "Verwendet in").</summary>
public enum VerwendungsArt
{
    Rolle,
    Phase,
    Einheit,
}

/// <summary>
/// Eine Stelle, die einen Kanal nennt (seit 0.8.0): Klartext und das Ziel des Sprungs, bei <see cref="VerwendungsArt.Phase"/> die Nummer der
/// Phase. <paramref name="RolleId"/> ist die Rolle dahinter (bei Rollen und Varianten), sonst <c>null</c>.
/// </summary>
public sealed record Verwendungsstelle(VerwendungsArt Art, ushort Id, string Text, ushort? RolleId);

/// <summary>
/// Klartext für den Umbau der Verwaltung (seit 0.8.0, docs/entwurf/VERWALTUNG-UMBAU.md): das Herkunftsschild je Rolle und Kanal, "Was
/// ändert sich?" je Objekt, wo ein Kanal vorkommt, und die Hinweise der Übersicht "Heute".
/// </summary>
public sealed partial class Flottenentwurf
{
    /// <summary>Herkunft dessen, was der Verwaltungsteil selbst anlegt.</summary>
    public const string VonHand = "von Hand";

    /// <summary>Herkunft der Kanäle und Rollen aus dem Baustein Trupp.</summary>
    public const string BausteinTruppHerkunft = "Baustein Trupp";

    /// <summary>Herkunft der Kanäle und Rollen aus dem Baustein Orga-Einheit.</summary>
    public const string BausteinOrgaHerkunft = "Baustein Orga";

    /// <summary>Abgeleitete Herkunft eines Kanals oder einer Rolle ohne gespeicherte Herkunft (vor 0.8.0 angelegt); nur zur Anzeige.</summary>
    public const string AlteFlotte = "alte Flotte";

    /// <summary>Abgeleitete Herkunft eines Kanals mit Ch. (oder einer Rolle nur aus solchen) ohne gespeicherte Herkunft: eine Vorlage vor 0.8.0.</summary>
    public const string AlteVorlage = "alte Vorlage";

    /// <summary>
    /// Das Herkunftsschild eines Kanals: die gespeicherte Herkunft, sonst abgeleitet. Fehlt sie im Entwurf, gilt die des Servers, denn der
    /// behält sie, wenn keine mitkommt. Kanäle mit Ch. ohne Herkunft stammen aus einer Vorlage vor 0.8.0 (<see cref="AlteVorlage"/>),
    /// alles andere aus der alten Flotte. Global hat keins (<c>null</c>), es gehört zu jeder Flotte.
    /// </summary>
    public string? HerkunftVon(Netz netz)
    {
        ArgumentNullException.ThrowIfNull(netz);
        if (netz.Id == Grenzen.GlobalNetzId) return null;
        return netz.Herkunft ?? _basisNetze.GetValueOrDefault(netz.Id)?.Herkunft
            ?? (netz.Kanaele is not null ? AlteVorlage : AlteFlotte);
    }

    /// <summary>
    /// Das Herkunftsschild einer Rolle: die gespeicherte Herkunft (fehlt sie im Entwurf, die des Servers), sonst die alte Vorlage, wenn jedes Feld
    /// auf einem Kanal mit Ch. liegt, sonst die alte Flotte.
    /// </summary>
    public string HerkunftVon(Rolle rolle)
    {
        ArgumentNullException.ThrowIfNull(rolle);
        return rolle.Herkunft ?? _basisRollen.GetValueOrDefault(rolle.Id)?.Herkunft
            ?? (rolle.Felder.Count > 0 && rolle.Felder.All(f => f.NetzId is { } id && Netz(id)?.Kanaele is not null)
                ? AlteVorlage
                : AlteFlotte);
    }

    /// <summary>Die Herkunftsschilder der Rollen oder der Kanäle (ohne Global), in der Reihenfolge der Liste, jedes einmal.</summary>
    public IReadOnlyList<string> Herkuenfte(bool rollen) => rollen
        ? [.. Rollen.Select(HerkunftVon).Distinct(StringComparer.Ordinal)]
        : [.. Netze.Select(HerkunftVon).OfType<string>().Distinct(StringComparer.Ordinal)];

    /// <summary>Der Name einer Rolle ohne Nummer davor ("Kapitän" aus "3 Kapitän"), um doppelte Namen zu finden.</summary>
    public static string Grundname(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var t = name.Trim();
        var leer = t.IndexOf(' ', StringComparison.Ordinal);
        return leer > 0 && t[..leer].All(char.IsAsciiDigit) ? t[(leer + 1)..].Trim() : t;
    }

    // ================================================================ Was ändert sich?

    /// <summary>
    /// "Was ändert sich?": je geändertes Objekt eine Zeile in Klartext, mit Herkunftsschild bei Rollen und Kanälen, etwa "Rolle
    /// Kapitän (alte Flotte): Selbst wählbar wird aus" oder "Kanal Kommando (Star Citizen) fällt weg". Dieselben Objekte in derselben
    /// Folge wie <see cref="Aenderungen"/>.
    /// </summary>
    public IReadOnlyList<string> Klartext()
    {
        var liste = new List<string>();
        Zeilen(liste, _basisSchiffe, _schiffe, s => "Einheit " + Anzeige(s.Name, s.Id), Gleich, Unterschiede);
        Zeilen(liste, _basisRollen, _rollen, r => "Rolle " + Anzeige(r.Name, r.Id) + Schild(HerkunftVon(r)), Gleich, Unterschiede);
        Zeilen(liste, _basisNetze, _netze, n => "Kanal " + Anzeige(n.Name, n.Id) + Schild(HerkunftVon(n)), Gleich, Unterschiede);
        if (PhasenErlaubt)
        {
            Zeilen(liste, _basisPhasen.ToDictionary(p => (ushort)p.Key, p => p.Value), EigenePhasen().ToDictionary(p => (ushort)p.Nr, p => p),
                p => "Phase " + p.Nr.ToString(CultureInfo.InvariantCulture) + " " + p.Name, Gleich, Unterschiede);
        }
        if (_durchsageNurTaste != Basis.Einstellungen.DurchsageNurTaste) liste.Add("Durchsagen nur mit Sprechtaste wird " + AnAus(_durchsageNurTaste));
        return liste;
    }

    private static void Zeilen<T>(List<string> liste, IReadOnlyDictionary<ushort, T> basis, IReadOnlyDictionary<ushort, T> neu, Func<T, string> wer,
        Func<T, T, bool> gleich, Func<T, T, IEnumerable<string>> unterschiede)
    {
        foreach (var (id, alt) in basis.OrderBy(p => p.Key))
            if (!neu.ContainsKey(id)) liste.Add(wer(alt) + " fällt weg");
        foreach (var (id, n) in neu.OrderBy(p => p.Key))
        {
            if (!basis.TryGetValue(id, out var alt)) liste.Add(wer(n) + " kommt dazu");
            else if (!gleich(alt, n)) liste.Add(wer(n) + ": " + string.Join(", ", unterschiede(alt, n).DefaultIfEmpty("geändert")));
        }
    }

    private static string Schild(string? herkunft) => herkunft is null ? "" : " (" + herkunft + ")";

    private static string Anzeige(string name, ushort id) => string.IsNullOrWhiteSpace(name) ? "Nummer " + id.ToString(CultureInfo.InvariantCulture) : name;

    private static string AnAus(bool an) => an ? "an" : "aus";

    private static string Zahl(int n, string eins, string mehr) => n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? eins : mehr);

    private IEnumerable<string> Unterschiede(Schiff a, Schiff b)
    {
        if (a.Name != b.Name) yield return "umbenannt, vorher " + a.Name;
        if ((a.Art ?? Einheitsart.Schiff) != (b.Art ?? Einheitsart.Schiff)) yield return "jetzt " + Artname(b.Art);
        if (a.Typ != b.Typ) yield return b.Typ is { } typ ? "Typ jetzt " + typ : "ohne Typ";
        foreach (var k in a.Netze.Keys.Union(b.Netze.Keys).Order(StringComparer.Ordinal))
        {
            ushort? alt = a.Netze.TryGetValue(k, out var x) ? x : null;
            ushort? neu = b.Netze.TryGetValue(k, out var y) ? y : null;
            if (alt == neu) continue;
            yield return neu is { } n ? Kennungstext(k) + (alt is null ? " neu: " : " jetzt ") + NetzName(n) : Kennungstext(k) + " fällt weg";
        }
        foreach (var rolle in a.Plaetze.Select(p => p.RolleId).Union(b.Plaetze.Select(p => p.RolleId)))
        {
            var alt = a.Plaetze.FirstOrDefault(p => p.RolleId == rolle);
            var neu = b.Plaetze.FirstOrDefault(p => p.RolleId == rolle);
            if (alt == neu) continue;
            var name = Rolle(rolle)?.Name ?? "Rolle " + rolle.ToString(CultureInfo.InvariantCulture);
            yield return neu is null ? name + ": Plätze fallen weg" : alt is null ? name + ": " + Plaetze(neu) + " neu" : name + ": " + Plaetze(neu) + " statt " + Plaetze(alt);
        }
        if (a.Reihenfolge != b.Reihenfolge) yield return "andere Stelle in der Liste";
    }

    private static string Plaetze(SchiffsPlatz p) =>
        Zahl(p.Kapazitaet, "Platz", "Plätze") + (p.Mindest is { } m ? ", mindestens " + m.ToString(CultureInfo.InvariantCulture) : "");

    private IEnumerable<string> Unterschiede(Rolle a, Rolle b)
    {
        if (a.Name != b.Name) yield return "umbenannt, vorher " + a.Name;
        if (a.SelbstWaehlbar != b.SelbstWaehlbar) yield return "Selbst wählbar wird " + AnAus(b.SelbstWaehlbar);
        if (a.BrauchtSchiff != b.BrauchtSchiff) yield return "Braucht eine Einheit wird " + AnAus(b.BrauchtSchiff);
        if (a.Kapazitaet != b.Kapazitaet) yield return b.Kapazitaet is { } k ? "Kapazität jetzt " + k.ToString(CultureInfo.InvariantCulture) : "Kapazität ohne Grenze";
        foreach (var t in Felderunterschiede(a.Felder, b.Felder)) yield return t;
        if (a.Reihenfolge != b.Reihenfolge) yield return "andere Stelle in der Liste";
    }

    /// <summary>Je Feld, was sich ändert: "Feld 3 jetzt Brücke (Ch. wie zuletzt, ab Werk Ruhe)", "Feld 4 hört nicht mehr, fest", "Feld 9 leer".</summary>
    private IEnumerable<string> Felderunterschiede(IReadOnlyList<RollenFeld> alt, IReadOnlyList<RollenFeld> neu)
    {
        foreach (var nr in alt.Select(f => f.Feld).Union(neu.Select(f => f.Feld)).Order())
        {
            var a = alt.FirstOrDefault(f => f.Feld == nr);
            var b = neu.FirstOrDefault(f => f.Feld == nr);
            if (a == b) continue;
            var feld = "Feld " + (nr + 1).ToString(CultureInfo.InvariantCulture);
            if (b is null)
            {
                yield return feld + " leer";
                continue;
            }
            if (a is null)
            {
                yield return feld + " neu: " + Quelle(b);
                continue;
            }
            var teile = new List<string>();
            if (a.NetzId != b.NetzId || a.Schiffsnetz != b.Schiffsnetz || a.Ch != b.Ch) teile.Add("jetzt " + Quelle(b));
            if (a.Hoeren != b.Hoeren) teile.Add(b.Hoeren ? "hört" : "hört nicht mehr");
            if (a.Senden != b.Senden) teile.Add(b.Senden ? "sendet" : "sendet nicht mehr");
            if (a.SendenStandard != b.SendenStandard) teile.Add(b.SendenStandard ? "zuerst senden" : "nicht mehr zuerst senden");
            if (a.Fest != b.Fest) teile.Add(b.Fest ? "fest" : "nicht mehr fest");
            yield return feld + " " + string.Join(", ", teile);
        }
    }

    private IEnumerable<string> Unterschiede(Netz a, Netz b)
    {
        if (a.Name != b.Name) yield return "umbenannt, vorher " + a.Name;
        if (a.NurTaste != b.NurTaste) yield return "nur mit Sprechtaste wird " + AnAus(b.NurTaste);
        if (a.AusloeserStandard != b.AusloeserStandard)
            yield return "Voreinstellung jetzt " + (b.AusloeserStandard == Ausloeser.Taste ? "Sprechtaste" : "Sprachaktivierung");
        if (a.Offen != b.Offen) yield return "jeder darf ihn selbst dazunehmen: " + Offentext(b.Offen);
        if (a.Standard != b.Standard) yield return "haben alle ohne Platz wird " + AnAus(b.Standard);
        if (a.Gruppe != b.Gruppe) yield return b.Gruppe is { } g ? "Gruppe jetzt " + g : "ohne Gruppe";
        if (!(a.Kanaele ?? []).SequenceEqual(b.Kanaele ?? []))
            yield return b.Kanaele is { } k ? (a.Kanaele is null ? "bekommt " : "jetzt ") + Zahl(k.Count - 1, "Ch.", "Ch.") : "ohne Ch.";
        if (a.SchiffId != b.SchiffId || a.Schiffsnetz != b.Schiffsnetz)
            yield return b.SchiffId is { } s && Schiff(s) is { } einheit ? "gehört jetzt zu " + einheit.Name : "gehört zu keiner Einheit mehr";
        if (a.Reihenfolge != b.Reihenfolge) yield return "andere Stelle in der Liste";
    }

    private static string Offentext(NetzOffen offen) => offen switch
    {
        NetzOffen.Hoeren => "nur hören",
        NetzOffen.HoerenSenden => "hören und sprechen",
        _ => "nein",
    };

    private IEnumerable<string> Unterschiede(NetzPhase a, NetzPhase b)
    {
        if (a.Name != b.Name) yield return "umbenannt, vorher " + a.Name;
        if (a.Kurz != b.Kurz) yield return "Kurzname jetzt " + b.Kurz;
        var altFelder = a.Felder ?? [];
        var neuFelder = b.Felder ?? [];
        foreach (var rolle in altFelder.Select(f => f.RolleId).Union(neuFelder.Select(f => f.RolleId)).Order())
        {
            var alt = altFelder.Where(f => f.RolleId == rolle).ToList();
            var neu = neuFelder.Where(f => f.RolleId == rolle).ToList();
            if (alt.SequenceEqual(neu)) continue;
            var name = Rolle(rolle)?.Name ?? "Rolle " + rolle.ToString(CultureInfo.InvariantCulture);
            yield return neu.Count == 0 ? name + " wieder wie Grundstellung" : alt.Count == 0 ? "Variante für " + name + " neu" : "Variante für " + name + " geändert";
        }
    }

    // ================================================================ Verwendet in

    /// <summary>"Verwendet in" eines Kanals: Rollen, Varianten in den Phasen und Einheiten, die ihn nennen (ein Ch. zählt beim Kanal).</summary>
    public IReadOnlyList<Verwendungsstelle> VerwendetIn(ushort netzId) => Verwendungen().GetValueOrDefault(netzId) ?? [];

    /// <summary>
    /// Wo jeder Kanal vorkommt, nach Netz-ID: Rollenfelder (direkt oder über die Kennung einer Einheit, auf der die Rolle Plätze hat),
    /// Varianten in den Phasen und Einheitskanäle. Die Kanal-ID eines Ch. zählt beim Kanalnetz. Je Rolle und Kanal eine Zeile.
    /// </summary>
    private Dictionary<ushort, List<Verwendungsstelle>> Verwendungen()
    {
        var index = new Dictionary<ushort, List<Verwendungsstelle>>();
        var schiffe = Schiffe;
        foreach (var r in Rollen)
        {
            var stellen = Stellen(r, schiffe, r.Felder.Select(f => (f.Feld, f.NetzId, f.Schiffsnetz)));
            foreach (var (netz, wo) in stellen)
                Eintragen(index, netz, new Verwendungsstelle(VerwendungsArt.Rolle, r.Id, "Rolle " + Anzeige(r.Name, r.Id) + Schild(HerkunftVon(r)) + ": " + string.Join(", ", wo), r.Id));
        }
        foreach (var p in Phasen.Where(p => p.Nr > 1))
        {
            foreach (var g in (p.Felder ?? []).GroupBy(f => f.RolleId))
            {
                if (Rolle(g.Key) is not { } r) continue;
                foreach (var (netz, wo) in Stellen(r, schiffe, g.Select(f => (f.Feld, f.NetzId, f.Schiffsnetz))))
                {
                    Eintragen(index, netz, new Verwendungsstelle(VerwendungsArt.Phase, p.Nr,
                        "Phase " + p.Nr.ToString(CultureInfo.InvariantCulture) + " " + p.Name + ", Rolle " + Anzeige(r.Name, r.Id) + ": " + string.Join(", ", wo), r.Id));
                }
            }
        }
        foreach (var s in schiffe)
        {
            foreach (var (k, id) in s.Netze)
            {
                var ch = Kanalrechnung.Zerlegen(id, out _, out var c) ? ", Ch. " + c.ToString(CultureInfo.InvariantCulture) : "";
                Eintragen(index, NetzVon(id), new Verwendungsstelle(VerwendungsArt.Einheit, s.Id, "Einheit " + Anzeige(s.Name, s.Id) + " als " + Kennungstext(k) + ch, null));
            }
        }
        return index;
    }

    /// <summary>Die Kanäle der Felder einer Rolle mit den Stellen: "Feld 1" oder "Feld 3 auf Schiff 1, Schiff 2" (über die Kennung).</summary>
    private static Dictionary<ushort, List<string>> Stellen(Rolle r, IReadOnlyList<Schiff> schiffe, IEnumerable<(byte Feld, ushort? NetzId, string? Schiffsnetz)> felder)
    {
        var stellen = new Dictionary<ushort, List<string>>();
        void Dazu(ushort netz, string wo)
        {
            if (!stellen.TryGetValue(netz, out var liste)) stellen[netz] = liste = [];
            liste.Add(wo);
        }
        foreach (var (feld, netzId, kennung) in felder.OrderBy(f => f.Feld))
        {
            var wo = "Feld " + (feld + 1).ToString(CultureInfo.InvariantCulture);
            if (netzId is { } id) Dazu(NetzVon(id), wo);
            else if (kennung is { } k)
            {
                foreach (var g in schiffe.Where(s => s.Plaetze.Any(p => p.RolleId == r.Id) && s.Netze.ContainsKey(k)).GroupBy(s => NetzVon(s.Netze[k])))
                    Dazu(g.Key, wo + " auf " + string.Join(", ", g.Select(s => s.Name)));
            }
        }
        return stellen;
    }

    private static void Eintragen(Dictionary<ushort, List<Verwendungsstelle>> index, ushort netz, Verwendungsstelle v)
    {
        if (!index.TryGetValue(netz, out var liste)) index[netz] = liste = [];
        liste.Add(v);
    }

    /// <summary>Die Netz-ID eines Kanals oder Netzes: bei der Kanal-ID eines Ch. das Kanalnetz.</summary>
    private static ushort NetzVon(ushort id) => Kanalrechnung.Zerlegen(id, out var n, out _) ? n : id;

    // ================================================================ Hinweise der Übersicht "Heute"

    /// <summary>
    /// Die Hinweise der Übersicht "Heute" in dieser Folge: doppelte Rollennamen (ohne Nummer davor) mit "Selbst wählbar" bei allen, Rollen
    /// ohne Felder, Rollen mit Einheit, die auf keiner Einheit stehen, Einheiten ohne Plätze, Kanäle in keiner Rolle (nicht offen und nicht
    /// für alle ohne Platz), Kanäle, die nur noch Rollen der alten Flotte benutzen (nur wenn es Rollen anderer Herkunft gibt). Mehr als drei
    /// gleiche Hinweise stehen als einer da; der Sprung geht zum ersten.
    /// </summary>
    public IReadOnlyList<FlottenHinweis> Hinweise()
    {
        var b = new List<FlottenHinweis>();
        var rollen = Rollen;
        var schiffe = Schiffe;
        foreach (var g in rollen.Where(r => r.SelbstWaehlbar && !string.IsNullOrWhiteSpace(r.Name)).GroupBy(r => Grundname(r.Name), StringComparer.OrdinalIgnoreCase))
        {
            var gleiche = g.ToList();
            if (gleiche.Count < 2) continue;
            var wie = gleiche.Count == 2 ? "zweimal, beide" : gleiche.Count.ToString(CultureInfo.InvariantCulture) + "-mal, alle";
            var herkunft = string.Join(", ", gleiche.Select(HerkunftVon).Distinct(StringComparer.Ordinal));
            b.Add(new FlottenHinweis("Die Rolle " + g.Key + " gibt es " + wie + " selbst wählbar (" + herkunft + "): In der Rollenwahl stehen sie nebeneinander.",
                HinweisZiel.Rolle, gleiche[0].Id, g.Key));
        }
        Sammeln(b, [.. rollen.Where(r => r.Felder.Count == 0).Select(r => (r.Id, Anzeige(r.Name, r.Id)))], HinweisZiel.Rolle,
            n => "Die Rolle " + n + " hat keine Felder: Wer sie wählt, hat nur Global.",
            (zahl, namen) => zahl + " Rollen haben keine Felder: " + namen + ". Wer eine davon wählt, hat nur Global.");
        Sammeln(b, [.. rollen.Where(r => r.BrauchtSchiff && !schiffe.Any(s => s.Plaetze.Any(p => p.RolleId == r.Id))).Select(r => (r.Id, Anzeige(r.Name, r.Id)))],
            HinweisZiel.Rolle,
            n => "Die Rolle " + n + " braucht eine Einheit, steht aber auf keiner: Niemand kann sie wählen.",
            (zahl, namen) => zahl + " Rollen brauchen eine Einheit, stehen aber auf keiner: " + namen + ". Niemand kann sie wählen.");
        Sammeln(b, [.. schiffe.Where(s => s.Plaetze.Count == 0).Select(s => (s.Id, Anzeige(s.Name, s.Id)))], HinweisZiel.Einheit,
            n => "Die Einheit " + n + " hat keine Plätze: Dort kann niemand einsteigen.",
            (zahl, namen) => zahl + " Einheiten haben keine Plätze: " + namen + ". Dort kann niemand einsteigen.");

        var index = Verwendungen();
        var netze = Netze.Where(n => n.Id != Grenzen.GlobalNetzId).ToList();
        List<ushort> RollenAn(Netz n) =>
            index.TryGetValue(n.Id, out var v) ? [.. v.Select(x => x.RolleId).OfType<ushort>().Distinct()] : [];
        Sammeln(b, [.. netze.Where(n => RollenAn(n).Count == 0 && n.Offen == NetzOffen.Nein && !n.Standard).Select(n => (n.Id, Anzeige(n.Name, n.Id)))],
            HinweisZiel.Kanal,
            n => "Der Kanal " + n + " steht in keiner Rolle und ist nicht offen: Von selbst bekommt ihn niemand.",
            (zahl, namen) => zahl + " Kanäle stehen in keiner Rolle und sind nicht offen: " + namen + ". Von selbst bekommt sie niemand.");
        if (rollen.Any(r => HerkunftVon(r) != AlteFlotte))
        {
            var alt = netze.Where(n => RollenAn(n) is { Count: > 0 } ids && ids.All(id => Rolle(id) is { } r && HerkunftVon(r) == AlteFlotte))
                .Select(n => (n.Id, Name: Anzeige(n.Name, n.Id))).ToList();
            if (alt.Count > 0)
            {
                b.Add(new FlottenHinweis(alt.Count == 1
                    ? "Nur noch Rollen der alten Flotte benutzen den Kanal " + alt[0].Name + "."
                    : "Nur noch Rollen der alten Flotte benutzen " + alt.Count.ToString(CultureInfo.InvariantCulture) + " Kanäle: " + Namen([.. alt.Select(a => a.Name)]) + ".",
                    HinweisZiel.KanaeleAlteFlotte, alt[0].Id));
            }
        }
        return b;
    }

    /// <summary>Bis drei Treffer je ein Hinweis, mehr als drei als einer mit den ersten drei Namen; der Sprung geht zum ersten.</summary>
    private static void Sammeln(List<FlottenHinweis> b, List<(ushort Id, string Name)> treffer, HinweisZiel ziel, Func<string, string> eins,
        Func<string, string, string> mehrere)
    {
        if (treffer.Count <= 3)
        {
            foreach (var (id, name) in treffer) b.Add(new FlottenHinweis(eins(name), ziel, id));
            return;
        }
        b.Add(new FlottenHinweis(mehrere(treffer.Count.ToString(CultureInfo.InvariantCulture), Namen([.. treffer.Select(t => t.Name)])), ziel, treffer[0].Id));
    }

    /// <summary>"A, B, C und 4 weitere".</summary>
    private static string Namen(List<string> namen) =>
        namen.Count <= 3 ? string.Join(", ", namen) : string.Join(", ", namen.Take(3)) + " und " + (namen.Count - 3).ToString(CultureInfo.InvariantCulture) + " weitere";
}
