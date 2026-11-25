// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>Ein Auftrag an den Zustandsschreiber. <see cref="Vorrang"/>: ZUWEISEN und Verwaltung vor Selbständerungen (6.2).</summary>
public sealed class Auftrag
{
    private readonly Func<Arbeitsstand, Ergebnis> _ausfuehren;

    internal Auftrag(string art, bool vorrang, Func<Arbeitsstand, Ergebnis> ausfuehren)
    {
        Art = art;
        Vorrang = vorrang;
        _ausfuehren = ausfuehren;
    }

    public string Art { get; }
    public bool Vorrang { get; }

    internal Ergebnis Ausfuehren(Arbeitsstand w) => _ausfuehren(w);

    public override string ToString() => Art;

    // ---- Selbständerungen (6.9) ----

    public static Auftrag HoerenSetzen(uint kontoId, ushort netzId, bool hoeren) =>
        new(nameof(HoerenSetzen), false, w => Zustandsschreiber.HoerenSetzenAus(w, kontoId, netzId, hoeren));

    public static Auftrag FeldSetzen(uint kontoId, byte feld, ushort? netzId) =>
        new(nameof(FeldSetzen), false, w => Zustandsschreiber.FeldSetzenAus(w, kontoId, feld, netzId));

    /// <summary>
    /// RolleWaehlen (6.16, seit 0.6.0): legt die Felder der Rolle an, ohne den Platz zu ändern. <paramref name="wuensche"/>: Ch. je
    /// Rollenfeld (Feld -> Ch.), Stufe 4 der Ch.-Auflösung.
    /// </summary>
    public static Auftrag RolleWaehlen(uint kontoId, ushort rolleId, IReadOnlyDictionary<byte, byte>? wuensche = null)
    {
        Dictionary<byte, byte>? kopie = wuensche is null ? null : new(wuensche);
        return new(nameof(RolleWaehlen), false, w => Zustandsschreiber.RolleWaehlenAus(w, kontoId, rolleId, kopie));
    }

    // Listen werden beim Anlegen geprüft und kopiert: ein fehlendes Argument fällt beim Aufrufer auf, nicht erst im
    // Gruppen-Commit, und spätere Änderungen an der Liste des Aufrufers wirken nicht mehr.

    public static Auftrag SatzAnwenden(uint kontoId, ushort? rolleId, ushort? schiffId, IReadOnlyList<Feldvorgabe> felder, Sendeauswahl? sendeauswahl = null)
    {
        ArgumentNullException.ThrowIfNull(felder);
        Feldvorgabe[] kopie = [.. felder];
        return new(nameof(SatzAnwenden), false, w => Zustandsschreiber.SatzAnwendenAus(w, kontoId, rolleId, schiffId, kopie, sendeauswahl));
    }

    /// <summary>EinsteigenRolleSchiff; mit <paramref name="kontoId"/> eines anderen Kontos wie Verschieben (ZUWEISEN).</summary>
    public static Auftrag EinsteigenRolleSchiff(uint anfragender, uint? kontoId, ushort rolleId, ushort? schiffId, bool erzwingen = false, long? basisVersion = null) =>
        new(nameof(EinsteigenRolleSchiff), kontoId is { } k && k != anfragender,
            w => Zustandsschreiber.EinsteigenAus(w, anfragender, kontoId, rolleId, schiffId, erzwingen, basisVersion));

    // ---- ZUWEISEN (6.7, 6.9, 6.10) ----

    public static Auftrag Verschieben(uint anfragender, IReadOnlyList<Aenderung> aenderungen, long? basisVersion = null)
    {
        ArgumentNullException.ThrowIfNull(aenderungen);
        Aenderung[] kopie = [.. aenderungen];
        return new(nameof(Verschieben), true, w => Zustandsschreiber.VerschiebenAus(w, anfragender, kopie, basisVersion, erzwingen: false));
    }

    public static Auftrag Stummschalten(uint anfragender, uint kontoId, ushort netzId, int sekunden) =>
        new(nameof(Stummschalten), true, w => Zustandsschreiber.StummschaltenAus(w, anfragender, kontoId, netzId, sekunden));

    /// <summary>
    /// KanalSperre SETZEN (0x003A, 6.17): <c>ZUWEISEN</c>; <paramref name="kontoId"/> 0 heißt für alle ohne <c>ZUWEISEN</c>.
    /// <paramref name="bis"/> (Lage-ms) rechnet der Aufrufer (das nächste 04:00 Ortszeit), höchstens 24 h. Ersetzt eine Sperre desselben
    /// Kontos auf dasselbe Netz.
    /// </summary>
    public static Auftrag KanalSperreSetzen(uint anfragender, uint kontoId, ushort netzId, long bis) =>
        new(nameof(KanalSperreSetzen), true, w => Zustandsschreiber.KanalSperreSetzenAus(w, anfragender, kontoId, netzId, bis));

    /// <summary>KanalSperre AUFHEBEN (6.17): wer gesetzt hat, ein höherer Rang oder ein ADMIN, jeweils mit <c>ZUWEISEN</c>.</summary>
    public static Auftrag KanalSperreAufheben(uint anfragender, uint kontoId, ushort netzId) =>
        new(nameof(KanalSperreAufheben), true, w => Zustandsschreiber.KanalSperreAufhebenAus(w, anfragender, kontoId, netzId));

    public static Auftrag PlaetzeFreigeben(uint anfragender) =>
        new(nameof(PlaetzeFreigeben), true, w => Zustandsschreiber.PlaetzeFreigebenAus(w, anfragender));

    public static Auftrag AufstellungSpeichern(uint anfragender, uint? id, uint? version, string name, IReadOnlyList<AufstellungsEintrag> eintraege)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(eintraege);
        AufstellungsEintrag[] kopie = [.. eintraege];
        return new(nameof(AufstellungSpeichern), true, w => Zustandsschreiber.AufstellungSpeichernAus(w, anfragender, id, version, name, kopie));
    }

    public static Auftrag AufstellungLoeschen(uint anfragender, uint id, uint version) =>
        new(nameof(AufstellungLoeschen), true, w => Zustandsschreiber.AufstellungLoeschenAus(w, anfragender, id, version));

    public static Auftrag AufstellungAnwenden(uint anfragender, uint id, uint? version, Aufstellungsmodus modus, long? basisVersion = null, bool erzwingen = false) =>
        new(nameof(AufstellungAnwenden), true, w => Zustandsschreiber.AufstellungAnwendenAus(w, anfragender, id, version, modus, basisVersion, erzwingen));

    // ---- Verwaltung (6.10, 4.5 Verwaltung) ----

    public static Auftrag KontoVerwaltung(uint anfragender, KontoOperation op, uint kontoId, Stufe? stufe = null, Recht? rechte = null, string? name = null) =>
        new(nameof(KontoVerwaltung), true, w => Zustandsschreiber.KontoVerwaltungAus(w, anfragender, op, kontoId, stufe, rechte, name));

    public static Auftrag NetzSetzen(uint anfragender, Netz netz, long version)
    {
        ArgumentNullException.ThrowIfNull(netz);
        return new(nameof(NetzSetzen), true, w => Zustandsschreiber.NetzSetzenAus(w, anfragender, netz, version));
    }

    public static Auftrag NetzLoeschen(uint anfragender, ushort netzId, long version) =>
        new(nameof(NetzLoeschen), true, w => Zustandsschreiber.NetzLoeschenAus(w, anfragender, netzId, version));

    public static Auftrag SchiffSetzen(uint anfragender, Schiff schiff, long version)
    {
        ArgumentNullException.ThrowIfNull(schiff);
        return new(nameof(SchiffSetzen), true, w => Zustandsschreiber.SchiffSetzenAus(w, anfragender, schiff, version));
    }

    public static Auftrag SchiffLoeschen(uint anfragender, ushort schiffId, long version) =>
        new(nameof(SchiffLoeschen), true, w => Zustandsschreiber.SchiffLoeschenAus(w, anfragender, schiffId, version));

    public static Auftrag RolleSetzen(uint anfragender, Rolle rolle, long version)
    {
        ArgumentNullException.ThrowIfNull(rolle);
        return new(nameof(RolleSetzen), true, w => Zustandsschreiber.RolleSetzenAus(w, anfragender, rolle, version));
    }

    public static Auftrag RolleLoeschen(uint anfragender, ushort rolleId, long version) =>
        new(nameof(RolleLoeschen), true, w => Zustandsschreiber.RolleLoeschenAus(w, anfragender, rolleId, version));

    /// <summary>
    /// Verwaltung SELBSTWAHL: <c>NETZE_VERWALTEN</c> oder <c>ZUWEISEN</c>. Eine Sperre trägt Urheber und Frist (12 h) und
    /// lässt Nachzügler ohne Platz durch, außer mit <paramref name="nachzuegler"/> false ("Ganz sperren", F9).
    /// </summary>
    public static Auftrag SelbstwahlSetzen(uint anfragender, bool an, long version, bool? nachzuegler = null) =>
        new(nameof(SelbstwahlSetzen), true, w => Zustandsschreiber.EinstellungAus(w, anfragender, version, selbstwahl: an, durchsageNurTaste: null, nachzuegler));

    public static Auftrag EinstellungSetzen(uint anfragender, bool durchsageNurTaste, long version) =>
        new(nameof(EinstellungSetzen), true, w => Zustandsschreiber.EinstellungAus(w, anfragender, version, selbstwahl: null, durchsageNurTaste: durchsageNurTaste));

    /// <summary>
    /// Verwaltung PAKET (N5): bis zu 200 Operationen auf einem Arbeitsstand, die NetzListe-Version einmal geprüft, alles oder
    /// nichts, danach eine NetzListe. So übernimmt der Verwaltungsteil eine ganze Flotte in einem Schritt.
    /// </summary>
    public static Auftrag Paket(uint anfragender, IReadOnlyList<Verwaltungsschritt> schritte, long version)
    {
        ArgumentNullException.ThrowIfNull(schritte);
        Verwaltungsschritt[] kopie = [.. schritte];
        return new(nameof(Paket), true, w => Zustandsschreiber.PaketAus(w, anfragender, kopie, version));
    }

    // ---- Einsatzphasen und Funker-Modus (6.14, 6.15, seit 0.5.0) ----

    /// <summary>
    /// PhaseSchalten (6.14): <c>ZUWEISEN</c>; <paramref name="erwartet"/> ist die Phase, die der Anfragende für die laufende hält
    /// (sonst VERALTET). Stellt nur Konten um, deren Felder sich ändern.
    /// </summary>
    public static Auftrag PhaseSchalten(uint anfragender, byte nr, byte erwartet) =>
        new(nameof(PhaseSchalten), true, w => Zustandsschreiber.PhaseSchaltenAus(w, anfragender, nr, erwartet));

    /// <summary>Verwaltung PHASE_SETZEN (6.14): <c>NETZE_VERWALTEN</c>, NetzListe-Version wie jede Verwaltung.</summary>
    public static Auftrag PhaseSetzen(uint anfragender, Einsatzphase phase, long version)
    {
        ArgumentNullException.ThrowIfNull(phase);
        return new(nameof(PhaseSetzen), true, w => Zustandsschreiber.PhaseSetzenAus(w, anfragender, phase, version));
    }

    /// <summary>Verwaltung PHASE_LOESCHEN (6.14).</summary>
    public static Auftrag PhaseLoeschen(uint anfragender, byte nr, long version) =>
        new(nameof(PhaseLoeschen), true, w => Zustandsschreiber.PhaseLoeschenAus(w, anfragender, nr, version));

    /// <summary>
    /// "Heute als Funker" (6.15): nur Stufe ADMIN, nur das eigene Konto. <paramref name="bis"/> (Lage-ms) rechnet der Aufrufer
    /// (das nächste 04:00 Ortszeit), höchstens 24 h; beim Ausschalten ohne Bedeutung.
    /// </summary>
    public static Auftrag FunkermodusSetzen(uint kontoId, bool an, long bis) =>
        new(nameof(FunkermodusSetzen), false, w => Zustandsschreiber.FunkermodusAus(w, kontoId, an, bis));

    // ---- Beauftragung (6.12) ----

    /// <summary>
    /// Auftrag für die Einsatzleitung erteilen oder ersetzen. <paramref name="von"/> fehlt oder liegt zurück: jetzt.
    /// <paramref name="rechte"/> fehlt: alle sechs der Einsatzleitung. Admin-Sitzung und frische Bestätigung prüft die Zentrale.
    /// </summary>
    public static Auftrag BeauftragungErteilen(uint anfragender, uint kontoId, long? von, long bis, Recht? rechte = null, string? grund = null) =>
        new(nameof(BeauftragungErteilen), true, w => Zustandsschreiber.BeauftragungErteilenAus(w, anfragender, kontoId, von, bis, rechte, grund));

    public static Auftrag BeauftragungEntziehen(uint anfragender, uint kontoId) =>
        new(nameof(BeauftragungEntziehen), true, w => Zustandsschreiber.BeauftragungEntziehenAus(w, anfragender, kontoId));

    /// <summary>Der Beauftragte gibt seinen Auftrag selbst ab ("Abend beenden").</summary>
    public static Auftrag BeauftragungAbgeben(uint kontoId) =>
        new(nameof(BeauftragungAbgeben), true, w => Zustandsschreiber.BeauftragungAbgebenAus(w, kontoId));

    /// <summary>
    /// Fristen-Takt (6.12 Regel 5, F9): räumt abgelaufene Aufträge ab, meldet begonnene und hebt eine Sperre der Selbstwahl
    /// nach 12 h auf. Ändert nichts, wenn nichts fällig ist.
    /// </summary>
    public static Auftrag FristenPruefen() => new(nameof(FristenPruefen), true, Zustandsschreiber.FristenAus);

    // ---- Serverintern: Konten und Sitzungen ----

    /// <summary>Konto anlegen (Verknüpfung 3.4 oder Admin-Werkzeug); ohne Felder bekommt es STANDARD.</summary>
    public static Auftrag KontoAnlegen(Konto konto)
    {
        ArgumentNullException.ThrowIfNull(konto);
        return new(nameof(KontoAnlegen), false, w => Zustandsschreiber.KontoAnlegenAus(w, konto));
    }

    /// <summary>Sitzung wird AKTIV (4.6 AKTIV_SYNC): Verdrängung, Wiederherstellung nach 6.9, Nachziehen.</summary>
    public static Auftrag SitzungAktivieren(uint kontoId, uint sitzungsId, uint geraetId) =>
        new(nameof(SitzungAktivieren), false, w => Zustandsschreiber.SitzungAktivierenAus(w, kontoId, sitzungsId, geraetId));

    public static Auftrag SitzungBeenden(uint sitzungsId) =>
        new(nameof(SitzungBeenden), false, w => Zustandsschreiber.SitzungBeendenAus(w, sitzungsId));

    /// <summary>Ein Admin hebt die Verdrängungssperre auf (4.6); braucht <c>GERAETE_FREIGEBEN</c>.</summary>
    public static Auftrag VerdraengungFreigeben(uint anfragender, uint kontoId) =>
        new(nameof(VerdraengungFreigeben), true, w => Zustandsschreiber.VerdraengungFreigebenAus(w, anfragender, kontoId));
}

/// <summary>Eintrag von Verschieben (4.5). Reihenfolge: Rolle und Schiff, Felder, FelderPatch, SendeFelder.</summary>
public sealed record Aenderung(uint KontoId)
{
    /// <summary>1-65534: Felder aus der Rolle; 0: Rolle und Platz entfernen, STANDARD.</summary>
    public ushort? RolleId { get; init; }

    /// <summary>Allein: gleiche Rolle auf neuem Schiff; 0 entfernt das Schiff.</summary>
    public ushort? SchiffId { get; init; }

    /// <summary>Ersetzt die Felder vollständig; alle so gesetzten Felder sind fest.</summary>
    public ImmutableArray<Feldvorgabe>? Felder { get; init; }

    public ImmutableArray<Feldpatch>? FelderPatch { get; init; }
    public ImmutableArray<byte>? SendeFelder { get; init; }
}

/// <summary>
/// Eintrag von <c>felderPatch</c> (4.5): Netz setzen oder Feld leeren. Fehlendes hören heißt true,
/// fehlendes senden false, fehlendes fest true. Ohne Feld: das Feld mit diesem Netz, sonst das nächste freie.
/// </summary>
public readonly record struct Feldpatch(byte? Feld, ushort NetzId, bool? Hoeren = null, bool? Senden = null, bool? Fest = null, bool Leeren = false)
{
    public static Feldpatch Leer(byte feld) => new(feld, 0, Leeren: true);
}

/// <summary>
/// Der eine Zustandsschreiber (6.2). Führt weiterleitungsrelevante Änderungen nacheinander aus, fasst eine
/// Warteschlange zu einer Transaktion zusammen (Gruppen-Commit), speichert, tauscht den Schnappschuss per
/// <see cref="Interlocked.Exchange{T}(ref T, T)"/> aus und liefert erst danach die Ergebnisse. Nicht threadsicher
/// außer <see cref="Aktuell"/>, das jeder Thread lesen darf.
/// </summary>
public sealed partial class Zustandsschreiber
{
    private readonly IZustandsSpeicher? _speicher;
    private readonly List<Auftrag> _vorrang = [];
    private readonly List<Auftrag> _normal = [];
    private Schnappschuss _aktuell;
    private Nebenstand _neben;

    public Zustandsschreiber(Schnappschuss start, Nebenstand? neben = null, IZustandsSpeicher? speicher = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        _aktuell = start;
        _neben = neben ?? Nebenstand.Leer;
        _speicher = speicher;
    }

    /// <summary>Der gültige Schnappschuss. Die Weiterleitung liest ihn genau einmal je Paket.</summary>
    public Schnappschuss Aktuell => Volatile.Read(ref _aktuell);

    public Nebenstand Neben => _neben;

    /// <summary>Komfortzustand (Sendeauswahl), ohne Austausch des Schnappschusses (6.2).</summary>
    public Komfortzustand Komfort { get; } = new();

    public int Wartend => _vorrang.Count + _normal.Count;

    public void Einreihen(Auftrag auftrag)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        (auftrag.Vorrang ? _vorrang : _normal).Add(auftrag);
    }

    /// <summary>Alle wartenden Aufträge als eine Transaktion, Vorrang zuerst, sonst in Eingangsfolge.</summary>
    public IReadOnlyList<(Auftrag Auftrag, Ergebnis Ergebnis)> Abarbeiten(long jetzt)
    {
        var liste = new List<Auftrag>(_vorrang.Count + _normal.Count);
        liste.AddRange(_vorrang);
        liste.AddRange(_normal);
        _vorrang.Clear();
        _normal.Clear();
        var ergebnisse = Transaktion(liste, jetzt);
        var paare = new (Auftrag, Ergebnis)[liste.Count];
        for (var i = 0; i < liste.Count; i++) paare[i] = (liste[i], ergebnisse[i]);
        return paare;
    }

    /// <summary>Ein einzelner Auftrag als eigene Transaktion.</summary>
    public Ergebnis Ausfuehren(Auftrag auftrag, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(auftrag);
        return Transaktion([auftrag], jetzt)[0];
    }

    /// <summary>
    /// SendeauswahlSetzen (4.5): Komfortzustand, geprüft gegen den aktuellen Schnappschuss, ohne Austausch.
    /// Das Zusammenfassen nach 4.8 übernimmt ein <see cref="Zusammenfasser{T}"/> davor.
    /// </summary>
    public Fehlercode SendeauswahlSetzen(uint kontoId, IReadOnlyList<byte> felder, bool global, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(felder);
        var z = Aktuell;
        if (!z.Konten.TryGetValue(kontoId, out var konto)) return Fehlercode.Unbekannt;
        var rechte = konto.RechteZu(jetzt);
        var code = Feldregeln.AuswahlPruefen(z.Netze, felder, global, konto, rechte);
        // Ein gesperrter Kanal steht nie in der Sendeauswahl (6.17).
        if (code == Fehlercode.Keiner && felder.Any(f => konto.FeldNummer(f) is { } m && Sperrregeln.Gesperrt(z.Netze, z.SperrenAlle, konto, rechte, m.NetzId, jetzt, out _)))
            code = Fehlercode.KeinRecht;
        if (code == Fehlercode.Keiner) Komfort.Setzen(kontoId, new Sendeauswahl([.. felder], global));
        return code;
    }

    private Ergebnis[] Transaktion(List<Auftrag> auftraege, long jetzt)
    {
        var alt = _aktuell;
        var w = new Arbeitsstand(alt, _neben, jetzt) { Komfort = Komfort };
        var ergebnisse = new Ergebnis[auftraege.Count];
        for (var i = 0; i < auftraege.Count; i++)
        {
            var punkt = w.Sichern();
            Ergebnis e;
            try
            {
                e = auftraege[i].Ausfuehren(w);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Ein fehlerhafter Auftrag trifft nur sich selbst: sein Teil wird zurückgesetzt, die übrigen Aufträge der
                // Gruppe laufen weiter und bekommen ihre Antwort. Die Ausnahme bleibt für das Protokoll erhalten.
                e = new Ergebnis { Fehler = Fehlercode.Intern, Ausnahme = ex };
            }
            if (!e.Erfolg) w.Zuruecksetzen(punkt);
            ergebnisse[i] = e;
        }

        var nebenGeaendert = !ReferenceEquals(w.Neben, _neben);
        if (!w.SchnappschussGeaendert && !nebenGeaendert)
        {
            foreach (var e in ergebnisse) e.ZustandsVersion = alt.ZustandsVersion;
            return ergebnisse;
        }

        var neu = w.SchnappschussGeaendert ? w.Bauen() : alt;
        try
        {
            _speicher?.Speichern(neu, w.Neben);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nichts ist ausgetauscht; der alte Stand bleibt vollständig in Kraft (P-09).
            for (var i = 0; i < ergebnisse.Length; i++)
                ergebnisse[i] = new Ergebnis { Fehler = Fehlercode.Intern, ZustandsVersion = alt.ZustandsVersion, Ausnahme = ex };
            return ergebnisse;
        }

        _neben = w.Neben;
        if (!ReferenceEquals(neu, alt)) Interlocked.Exchange(ref _aktuell, neu);
        foreach (var kontoId in w.AuswahlGesetzt) Komfort.Verwerfen(kontoId);
        foreach (var e in ergebnisse) e.ZustandsVersion = e.Erfolg ? neu.ZustandsVersion : alt.ZustandsVersion;
        return ergebnisse;
    }

    // ---- Hilfen für alle Aufträge ----

    private static Ergebnis Ok(params uint[] betroffen) => new() { BetroffeneKonten = [.. betroffen] };

    private static Ergebnis Veraltet(IEnumerable<Konto> konten) => new()
    {
        Fehler = Fehlercode.Veraltet,
        VeralteteKonten = [.. konten.Select(k => new KontoStand(k.Id, k.KontoVersion))],
    };

    /// <summary>Rolle auf Schiff wählbar (6.9): Rolle in <c>plaetze</c> des Schiffs, <c>brauchtSchiff</c> nur mit Schiff.</summary>
    private static (Fehlercode Code, Schiff? Schiff) PlatzPruefen(Arbeitsstand w, Rolle rolle, ushort schiffId)
    {
        if (schiffId == 0) return rolle.BrauchtSchiff ? (Fehlercode.Ungueltig, null) : (Fehlercode.Keiner, null);
        if (!w.Schiffe.TryGetValue(schiffId, out var schiff)) return (Fehlercode.Unbekannt, null);
        return schiff.Kapazitaet(rolle.Id) is null ? (Fehlercode.Ungueltig, null) : (Fehlercode.Keiner, schiff);
    }

    private static int? Kapazitaet(Arbeitsstand w, ushort schiffId, ushort rolleId)
    {
        if (schiffId != 0) return w.Schiffe.TryGetValue(schiffId, out var s) ? s.Kapazitaet(rolleId) : 0;
        return w.Rollen.TryGetValue(rolleId, out var r) ? r.Kapazitaet : 0;
    }

    /// <summary>Konten, die einen Platz BELEGT oder RESERVIERT halten.</summary>
    private static List<Konto> Inhaber(Arbeitsstand w, ushort schiffId, ushort rolleId) =>
        [.. w.Konten.Values.Where(k => k.Platz is { } p && p.SchiffId == schiffId && p.RolleId == rolleId && w.Platzzustand(k) != PlatzZustand.Frei)];

    /// <summary>Kapazität am Endzustand (6.7 Schritt 2): Plätze, die mehr Inhaber haben als erlaubt.</summary>
    private static List<(ushort SchiffId, ushort RolleId)> Ueberbelegt(Arbeitsstand w, IEnumerable<(ushort SchiffId, ushort RolleId)> plaetze)
    {
        var ueber = new List<(ushort, ushort)>();
        foreach (var (schiff, rolle) in plaetze.Distinct())
        {
            if (Kapazitaet(w, schiff, rolle) is { } kap && Inhaber(w, schiff, rolle).Count > kap) ueber.Add((schiff, rolle));
        }
        return ueber;
    }

    /// <summary>STANDARD für ein Konto, mit dem früheren Stand zur Übernahme (A21).</summary>
    private static Konto AufStandard(Arbeitsstand w, Konto k, uint durch, bool letztenStandMerken)
    {
        var (felder, auswahl) = Feldregeln.Standard(w.Netze.Values);
        return k with
        {
            Felder = felder,
            Sendeauswahl = auswahl,
            Platz = null,
            Reservierung = null,
            Quelle = Quelle.Standard,
            QuelleDurch = durch,
            Entfallen = [],
            LetzterStand = letztenStandMerken ? new FrueherStand(k.Platz, k.Felder, k.Sendeauswahl) : k.LetzterStand,
            GewaehlteRolle = null,
        };
    }

    // ---- Konten und Sitzungen ----

    internal static Ergebnis KontoAnlegenAus(Arbeitsstand w, Konto konto)
    {
        ArgumentNullException.ThrowIfNull(konto);
        if (konto.Id is 0 or > int.MaxValue || w.Konten.ContainsKey(konto.Id)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (Namen.Skalarwerte(konto.Name) > Fristen.NameMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (konto.DiscordId is { } d && w.Konten.Values.Any(k => k.DiscordId == d)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var neu = konto with { Name = Namen.Eindeutig(konto.Name, w.Konten.Values, konto.Id) };
        if (neu.Felder.IsDefaultOrEmpty)
        {
            var (felder, auswahl) = Feldregeln.Standard(w.Netze.Values);
            neu = neu with { Felder = felder, Sendeauswahl = auswahl, Quelle = Quelle.Standard };
        }
        else if (Feldregeln.FelderPruefen(neu.Felder, w.Netze) is not null)
        {
            return Ergebnis.Mit(Fehlercode.Ungueltig);
        }
        w.KontoSetzen(neu);

        var meldungen = new List<Meldung>();
        Nachziehen(w, neu, meldungen);
        return new Ergebnis { Meldungen = [.. meldungen], BetroffeneKonten = [neu.Id] };
    }

    internal static Ergebnis SitzungAktivierenAus(Arbeitsstand w, uint kontoId, uint sitzungsId, uint geraetId)
    {
        if (w.Konto(kontoId) is not { } k) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (k.Status != KontoStatus.Aktiv) return Ergebnis.Mit(Fehlercode.KontoGesperrt);
        if (sitzungsId == 0 || w.Sitzungen.ContainsKey(sitzungsId)) return Ergebnis.Mit(Fehlercode.Ungueltig);

        var beendet = new List<BeendeteSitzung>();
        var meldungen = new List<Meldung>();
        var jetzt = w.Jetzt;
        if (w.SitzungJeKonto.TryGetValue(kontoId, out var alteId))
        {
            var alte = w.Sitzungen[alteId];
            if (alte.GeraetId == geraetId)
            {
                beendet.Add(new BeendeteSitzung(alteId, kontoId, Sitzungsende.Ersetzt));
            }
            else
            {
                // Verdrängung zwischen verschiedenen Geräten (4.6): mehr als 2 in 5 min melden und danach sperren.
                var verlauf = w.Neben.Verdraengungen.TryGetValue(kontoId, out var v) ? v : new Verdraengungsverlauf([], 0);
                if (verlauf.GesperrtSeit != 0 && jetzt - verlauf.GesperrtSeit < Fristen.VerdraengungSperreMs)
                    return Ergebnis.Mit(Fehlercode.VerdraengenGesperrt);
                ImmutableArray<long> zeiten = [.. verlauf.Zeiten.Where(t => jetzt - t < Fristen.VerdraengungFensterMs), jetzt];
                var gesperrt = 0L;
                if (zeiten.Length > 2)
                {
                    meldungen.Add(new Meldung(MeldungsArt.VerdraengungHaeufig, kontoId));
                    gesperrt = jetzt;
                }
                w.Neben = w.Neben with { Verdraengungen = w.Neben.Verdraengungen.SetItem(kontoId, new Verdraengungsverlauf(zeiten, gesperrt)) };
                beendet.Add(new BeendeteSitzung(alteId, kontoId, Sitzungsende.Verdraengt));
            }
            w.SitzungEntfernen(alteId);
        }
        else
        {
            Wiederherstellen(w, k);
            if (w.Neben.Letzte is { } la && jetzt - la.Zeit <= Fristen.NachziehenMs && la.OffeneKonten.Contains(kontoId))
            {
                meldungen.Add(new Meldung(MeldungsArt.AufstellungNachgezogen, kontoId, la.Anwender));
                w.Neben = w.Neben with { Letzte = la with { OffeneKonten = la.OffeneKonten.Remove(kontoId) } };
            }
        }
        w.SitzungHinzu(new AktiveSitzung(sitzungsId, kontoId, geraetId));
        return new Ergebnis { BeendeteSitzungen = [.. beendet], Meldungen = [.. meldungen], BetroffeneKonten = [kontoId] };
    }

    /// <summary>
    /// Beim Anmelden (6.9): bei einer Reservierung oder innerhalb der Frist still wiederherstellen, sonst
    /// STANDARD mit <c>letzterStand</c>. Eine Reservierung kommt mit ihrer Herkunft zurück, seit 0.11.0 auch VERSCHIEBEN mit
    /// <c>durch</c> (vorher WIEDERHERGESTELLT): So sieht auch ein alter Client ", eingeteilt von X". Hat der Funker nach der
    /// Einteilung selbst etwas geändert (Quelle SELBST, ROLLE oder SATZ), bleibt seine Quelle (seit 0.11.0, Befund der Prüfung).
    /// Die erste Anmeldung eines Kontos behält ihren Anfangsstand nur, solange keine abgelaufene Reservierung daran hängt
    /// (seit 0.11.0, Doppelbelegung).
    /// </summary>
    private static void Wiederherstellen(Arbeitsstand w, Konto k)
    {
        var jetzt = w.Jetzt;
        // Eine Reservierung sagt, wer den Stand gesetzt hat; sie geht der Frist vor (AUFSTELLUNG oder VERSCHIEBEN statt WIEDERHERGESTELLT).
        if (k.Reservierung is { } r && jetzt - r.Gesetzt < Fristen.ReservierungMs)
        {
            // Seit der Einteilung selbst geändert (FeldSetzen, RolleWaehlen, SatzAnwenden ohne Rolle; die Reservierung bleibt dabei):
            // Der Stand gehört nicht mehr der Leitung. Sonst stünde nach jeder kurzen Trennung "eingeteilt von X" statt "selbst", und
            // ein Client 0.11.0 sagte die Einteilung noch einmal an.
            if (k.Quelle is Quelle.Selbst or Quelle.Rolle or Quelle.Satz) return;
            var quelle = r.Quelle is Quelle.Aufstellung or Quelle.Verschieben ? r.Quelle : Quelle.Wiederhergestellt;
            w.KontoSetzen(k with { Quelle = quelle, QuelleDurch = r.Durch }, relevant: false);
            return;
        }
        if (k.SitzungsEnde > 0 && jetzt - k.SitzungsEnde < Fristen.WiederverbindungMs)
        {
            w.KontoSetzen(k with { Quelle = Quelle.Wiederhergestellt, QuelleDurch = 0 }, relevant: false);
            return;
        }
        // Nie angemeldet und nie eingeteilt: der Anfangsstand bleibt. Mit abgelaufener Reservierung ist der Platz längst frei und
        // vielleicht vergeben; dann STANDARD wie nach einer Sitzung, sonst säßen zwei auf einem Platz (seit 0.11.0).
        if (k.SitzungsEnde == 0 && k.Reservierung is null)
        {
            // "Zurück auf Standard" setzt seit 0.11.0 keine Reservierung: Ein nie angemeldetes Konto käme sonst mit VERSCHIEBEN und durch
            // zurück, als wäre es eingeteilt, und der Client sagte eine Einteilung an.
            if (k.Quelle == Quelle.Verschieben && k.Platz is null) w.KontoSetzen(k with { Quelle = Quelle.Standard, QuelleDurch = 0 }, relevant: false);
            return;
        }
        var standard = AufStandard(w, k, 0, letztenStandMerken: true);
        var gleich = standard.Platz == k.Platz && standard.Felder.AsSpan().SequenceEqual(k.Felder.AsSpan());
        w.KontoSetzen(gleich ? k with { Quelle = Quelle.Standard } : standard, relevant: !gleich);
    }

    internal static Ergebnis SitzungBeendenAus(Arbeitsstand w, uint sitzungsId)
    {
        if (!w.Sitzungen.TryGetValue(sitzungsId, out var s)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        w.SitzungEntfernen(sitzungsId);
        if (w.Konto(s.KontoId) is { } k) w.KontoSetzen(k with { SitzungsEnde = w.Jetzt }, relevant: false);
        return new Ergebnis();
    }

    internal static Ergebnis VerdraengungFreigebenAus(Arbeitsstand w, uint anfragender, uint kontoId)
    {
        if (!w.RechteVon(anfragender).Hat(Recht.GeraeteFreigeben)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (!w.Neben.Verdraengungen.ContainsKey(kontoId)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        w.Neben = w.Neben with { Verdraengungen = w.Neben.Verdraengungen.Remove(kontoId) };
        return new Ergebnis();
    }
}
