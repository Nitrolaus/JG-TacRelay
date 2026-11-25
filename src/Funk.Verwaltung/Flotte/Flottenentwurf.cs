// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json;
using Funk.Protokoll.Steuerung;

namespace Funk.Verwaltung.Flotte;


/// <summary>
/// Der Entwurf der Flotten-Einstellungsseite (A40, RAIDLEITUNG-ENTWURF "Flotten-Einstellungsseite", "Arbeiten im Entwurf"):
/// Einheiten, Rollen und Netze als Arbeitsstand über der NetzListe des Servers (<see cref="Basis"/>). Änderungen bleiben lokal,
/// <see cref="Aenderungen"/> nennt sie im Klartext, <see cref="Pruefen"/> prüft dieselben Regeln wie der Server (6.9, Grenzen
/// aus 4.10) und sagt sie ohne Drahtnamen, <see cref="Paket"/> baut daraus ein Verwaltungspaket (N5).
/// <para>
/// Folge im Paket, damit jeder Schritt am Server auf einen gültigen Stand trifft: erst Löschen (Rollen, Einheiten, Netze; der
/// Server räumt Bezüge selbst ab, wie hier auch), dann neue Einheiten als Gerüst ohne Netze und Plätze (Netze nennen ihre
/// Einheit), Netze, Rollen (ihre Netze gibt es dann), Einheiten vollständig (Netze und Rollen gibt es dann), zuletzt die
/// Einstellung. Vorhandene Einheiten bekommen nie ein Gerüst: Ein Schiff ohne Plätze ließe der Server alle aussteigen.
/// </para>
/// Alles wird beim Setzen in die Form gebracht, in der der Server es zurückgibt (Art Schiff statt leer, Mindestbesetzung 0 als
/// leer, Kennungen sortiert, Felder nach Nummer); so gilt nur als geändert, was sich wirklich ändert. Die Herkunft (seit 0.8.0)
/// zählt nie als Änderung: Ein Server 0.7.x speichert sie nicht, der Entwurf bliebe sonst für immer offen.
/// </summary>
public sealed partial class Flottenentwurf
{
    /// <summary>So viele Felder zeigt der Felder-Editor (2x5 wie das Funkfeld, Global fest rechts unten).</summary>
    public const int EditorFelder = 9;

    /// <summary>Die Kennungen, die "Netze für diese Einheit anlegen" kennt, je Art.</summary>
    public static IReadOnlyList<string> Schiffskennungen { get; } = ["DURCHSAGEN", "CREW", "BRUECKE"];

    private static readonly string[] Truppnamen = ["Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliett"];

    private readonly SortedDictionary<ushort, Netz> _netze = new();
    private readonly SortedDictionary<ushort, Schiff> _schiffe = new();
    private readonly SortedDictionary<ushort, Rolle> _rollen = new();
    private readonly Dictionary<ushort, Netz> _basisNetze = [];
    private readonly Dictionary<ushort, Schiff> _basisSchiffe = [];
    private readonly Dictionary<ushort, Rolle> _basisRollen = [];
    private readonly SortedDictionary<byte, NetzPhase> _phasen = new();
    private readonly Dictionary<byte, NetzPhase> _basisPhasen = [];
    private bool _durchsageNurTaste;

    private Flottenentwurf(NetzListe basis)
    {
        Basis = basis;
        foreach (var n in basis.Netze) _basisNetze[n.Id] = Normal(n);
        foreach (var s in basis.Schiffe) _basisSchiffe[s.Id] = Normal(s);
        foreach (var r in basis.Rollen) _basisRollen[r.Id] = Normal(r);
        foreach (var p in basis.Phasen ?? []) _basisPhasen[p.Nr] = Normal(p);
    }

    /// <summary>Entwurf über <paramref name="basis"/> (dem Stand des Servers); mit <paramref name="stand"/> gleich mit dessen Inhalt (Datei, Sicherung).</summary>
    public static Flottenentwurf Aus(NetzListe basis, NetzListe? stand = null)
    {
        ArgumentNullException.ThrowIfNull(basis);
        var e = new Flottenentwurf(basis);
        e.Laden(stand ?? basis);
        return e;
    }

    /// <summary>Der Stand des Servers, gegen den gezählt und übernommen wird.</summary>
    public NetzListe Basis { get; }

    /// <summary>Etwas am Entwurf hat sich geändert.</summary>
    public event Action? Geaendert;

    /// <summary>Global zuerst, dann nach Reihenfolge.</summary>
    public IReadOnlyList<Netz> Netze => [.. _netze.Values.OrderBy(n => n.Id == Grenzen.GlobalNetzId ? 0 : 1).ThenBy(n => n.Reihenfolge).ThenBy(n => n.Id)];

    /// <summary>Schiffe, Infanterie, Orga, je nach Reihenfolge.</summary>
    public IReadOnlyList<Schiff> Schiffe => [.. _schiffe.Values.OrderBy(s => s.Art ?? Einheitsart.Schiff).ThenBy(s => s.Reihenfolge).ThenBy(s => s.Id)];

    public IReadOnlyList<Rolle> Rollen => [.. _rollen.Values.OrderBy(r => r.Reihenfolge).ThenBy(r => r.Id)];

    /// <summary>Durchsagen nur mit Sprechtaste (Einstellung).</summary>
    public bool DurchsageNurTaste
    {
        get => _durchsageNurTaste;
        set
        {
            if (_durchsageNurTaste == value) return;
            _durchsageNurTaste = value;
            Melden();
        }
    }

    public Netz? Netz(ushort id) => _netze.GetValueOrDefault(id);

    public Schiff? Schiff(ushort id) => _schiffe.GetValueOrDefault(id);

    public Rolle? Rolle(ushort id) => _rollen.GetValueOrDefault(id);

    /// <summary>Ersetzt den Inhalt durch <paramref name="stand"/>; die Basis bleibt.</summary>
    public void Laden(NetzListe stand)
    {
        ArgumentNullException.ThrowIfNull(stand);
        _netze.Clear();
        _schiffe.Clear();
        _rollen.Clear();
        foreach (var n in stand.Netze) _netze[n.Id] = Normal(n);
        foreach (var s in stand.Schiffe) _schiffe[s.Id] = Normal(s);
        foreach (var r in stand.Rollen) _rollen[r.Id] = Normal(r);
        _phasen.Clear();
        foreach (var p in stand.Phasen ?? []) _phasen[p.Nr] = Normal(p);
        // Global gibt es immer; eine Datei ohne Global bekommt das des Servers.
        if (!_netze.ContainsKey(Grenzen.GlobalNetzId) && _basisNetze.TryGetValue(Grenzen.GlobalNetzId, out var global)) _netze[global.Id] = global;
        _durchsageNurTaste = stand.Einstellungen.DurchsageNurTaste;
        Melden();
    }

    /// <summary>
    /// Neue Basis vom Server (ein anderer Admin hat übernommen): je Objekt gilt, was hier geändert wurde, alles andere kommt vom
    /// Server. So verdrängt der eigene Entwurf keine fremde Änderung, die er gar nicht angefasst hat.
    /// </summary>
    public Flottenentwurf Umsetzen(NetzListe neueBasis)
    {
        ArgumentNullException.ThrowIfNull(neueBasis);
        var neu = Aus(neueBasis);
        foreach (var id in _basisNetze.Keys.Union(_netze.Keys).ToList())
        {
            var eigen = _netze.GetValueOrDefault(id);
            var alt = _basisNetze.GetValueOrDefault(id);
            if (alt is null ? eigen is null : eigen is not null && Gleich(alt, eigen)) continue;
            if (eigen is null) neu.NetzLoeschen(id);
            else neu._netze[id] = eigen;
        }
        foreach (var id in _basisSchiffe.Keys.Union(_schiffe.Keys).ToList())
        {
            var alt = _basisSchiffe.GetValueOrDefault(id);
            var eigen = _schiffe.GetValueOrDefault(id);
            if (alt is null ? eigen is null : eigen is not null && Gleich(alt, eigen)) continue;
            if (eigen is null) neu.SchiffLoeschen(id);
            else neu._schiffe[id] = eigen;
        }
        foreach (var id in _basisRollen.Keys.Union(_rollen.Keys).ToList())
        {
            var alt = _basisRollen.GetValueOrDefault(id);
            var eigen = _rollen.GetValueOrDefault(id);
            if (alt is null ? eigen is null : eigen is not null && Gleich(alt, eigen)) continue;
            if (eigen is null) neu.RolleLoeschen(id);
            else neu._rollen[id] = eigen;
        }
        foreach (var nr in _basisPhasen.Keys.Union(_phasen.Keys).ToList())
        {
            var alt = _basisPhasen.GetValueOrDefault(nr);
            var eigen = _phasen.GetValueOrDefault(nr);
            if (alt is null ? eigen is null : eigen is not null && Gleich(alt, eigen)) continue;
            if (eigen is null) neu._phasen.Remove(nr);
            else neu._phasen[nr] = eigen;
        }
        neu.PhasenErlaubt = PhasenErlaubt;
        neu.KanaeleErlaubt = KanaeleErlaubt;
        if (_durchsageNurTaste != Basis.Einstellungen.DurchsageNurTaste) neu._durchsageNurTaste = _durchsageNurTaste;
        return neu;
    }

    // ================================================================ Phasen (6.14, seit 0.5.0)

    /// <summary>Höchstens so viele Phasen über der Grundstellung (4.10).</summary>
    public const int PhasenMax = Grenzen.PhasenMax;

    /// <summary>
    /// Der Server kennt Phasen (ab 0.5.0): Nur dann gehen PHASE_SETZEN und PHASE_LOESCHEN ins Paket; <c>VerwaltungsOperation</c> ist
    /// streng, ein Server 0.4.x nähme das Paket nicht an.
    /// </summary>
    public bool PhasenErlaubt { get; set; }

    /// <summary>
    /// Der Server kennt Kanäle mit Ch. (ab 0.6.0, 6.16): Nur dann darf ein Kanal eine Ch.-Liste tragen. Ein älterer Server übersähe die
    /// Liste und legte ein gewöhnliches Netz an.
    /// </summary>
    public bool KanaeleErlaubt { get; set; }

    /// <summary>Die Phasen nach Nummer; Nummer 1 ist die Grundstellung (nur Name und Kurzname).</summary>
    public IReadOnlyList<NetzPhase> Phasen => [.. _phasen.Values];

    public NetzPhase? Phase(byte nr) => _phasen.GetValueOrDefault(nr);

    /// <summary>Die Variante einer Rolle in einer Phase als Rollenfelder; leer heißt: wie in der Grundstellung.</summary>
    public IReadOnlyList<RollenFeld> Variante(byte nr, ushort rolleId) =>
        [.. (Phase(nr)?.Felder ?? []).Where(f => f.RolleId == rolleId).OrderBy(f => f.Feld).Select(f => new RollenFeld
        {
            Feld = f.Feld, NetzId = f.NetzId, Schiffsnetz = f.Schiffsnetz, Hoeren = f.Hoeren, Senden = f.Senden, SendenStandard = f.SendenStandard, Fest = f.Fest,
            Ch = f.Ch,
        })];

    /// <summary>Rollen mit Variante in einer Phase.</summary>
    public IReadOnlyList<ushort> RollenMitVariante(byte nr) => [.. (Phase(nr)?.Felder ?? []).Select(f => f.RolleId).Distinct().Order()];

    public void PhaseSetzen(NetzPhase phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        _phasen[phase.Nr] = Normal(phase);
        Melden();
    }

    /// <summary>
    /// Neue Phase mit der nächsten freien Nummer (2 bis 5), ohne Varianten; fehlt die Grundstellung, kommt sie mit dem Namen des
    /// Servers dazu ("Grundstellung", "GRUND"). <c>null</c>, wenn schon 4 Phasen über der Grundstellung da sind.
    /// </summary>
    public NetzPhase? PhaseAnlegen(string name, string kurz)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(kurz);
        if (FreiePhase() is not { } frei) return null;
        if (!_phasen.ContainsKey(1)) _phasen[1] = new NetzPhase { Nr = 1, Name = GrundstellungName, Kurz = GrundstellungKurz };
        var p = new NetzPhase { Nr = frei, Name = name, Kurz = kurz };
        PhaseSetzen(p);
        return Phase(frei);
    }

    /// <summary>Die nächste freie Nummer über der Grundstellung (2 bis 5), <c>null</c>, wenn alle vergeben sind.</summary>
    public byte? FreiePhase()
    {
        for (byte n = 2; n <= Grenzen.PhaseNrMax; n++)
            if (!_phasen.ContainsKey(n)) return n;
        return null;
    }

    /// <summary>Die Phase steht im Entwurf anders als am Server (geändert oder gelöscht).</summary>
    public bool PhaseGeaendert(byte nr)
    {
        var alt = _basisPhasen.GetValueOrDefault(nr);
        var eigen = _phasen.GetValueOrDefault(nr);
        return alt is null ? eigen is not null && EigenePhasen().Contains(eigen) : eigen is null || !Gleich(alt, eigen);
    }

    /// <summary>Löscht eine Phase; die Grundstellung bleibt (der Server löscht sie nie).</summary>
    public void PhaseLoeschen(byte nr)
    {
        if (nr <= 1 || !_phasen.Remove(nr)) return;
        if (_phasen.Count == 1 && !_basisPhasen.ContainsKey(1)) _phasen.Remove(1);
        Melden();
    }

    /// <summary>"Variante anlegen": kopiert die Felder der Grundstellung der Rolle in die Phase.</summary>
    public void VarianteAnlegen(byte nr, ushort rolleId)
    {
        if (nr <= 1 || Rolle(rolleId) is not { } r || r.Felder.Count == 0) return;
        VarianteSetzen(nr, rolleId, r.Felder);
    }

    /// <summary>Setzt die Variante einer Rolle (vollständige Feldliste); leer heißt "wie Grundstellung".</summary>
    public void VarianteSetzen(byte nr, ushort rolleId, IReadOnlyList<RollenFeld> felder)
    {
        ArgumentNullException.ThrowIfNull(felder);
        if (nr <= 1 || Phase(nr) is not { } p) return;
        var uebrige = (p.Felder ?? []).Where(f => f.RolleId != rolleId);
        var neu = felder.Select(f => new PhasenFeld
        {
            RolleId = rolleId, Feld = f.Feld, NetzId = f.NetzId, Schiffsnetz = f.Schiffsnetz, Hoeren = f.Hoeren, Senden = f.Senden,
            SendenStandard = f.SendenStandard, Fest = f.Fest, Ch = f.Ch,
        });
        PhaseSetzen(p with { Felder = [.. uebrige, .. neu] });
    }

    /// <summary>"Wie Grundstellung": die Variante einer Rolle fällt weg.</summary>
    public void VarianteEntfernen(byte nr, ushort rolleId) => VarianteSetzen(nr, rolleId, []);

    /// <summary>Wen eine Phase trifft, im Klartext: "Marine (alle Schiffe), Kapitän (Schiff 1, Schiff 2)".</summary>
    public string Betroffene(byte nr)
    {
        var rollen = RollenMitVariante(nr);
        if (rollen.Count == 0) return "niemand: alle Rollen wie in der Grundstellung";
        return string.Join(", ", rollen.Select(id =>
        {
            var name = Rolle(id)?.Name ?? "Rolle " + id.ToString(CultureInfo.InvariantCulture);
            if (Rolle(id) is not { BrauchtSchiff: true }) return name;
            var einheiten = Schiffe.Where(s => s.Plaetze.Any(p => p.RolleId == id)).ToList();
            if (einheiten.Count == 0) return name + " (auf keiner Einheit)";
            // Je Art: "alle Schiffe", wenn jede Einheit dieser Art mit Plätzen sie trägt; sonst die Namen, ab fünf gekürzt.
            var teile = einheiten.GroupBy(s => s.Art ?? Einheitsart.Schiff).OrderBy(g => g.Key).Select(g =>
            {
                var alle = Schiffe.Count(s => (s.Art ?? Einheitsart.Schiff) == g.Key && s.Plaetze.Count > 0);
                if (g.Count() == alle && alle > 1) return "alle " + Mehrzahlname(g.Key);
                var namen = g.Select(s => s.Name).ToList();
                return namen.Count <= 4 ? string.Join(", ", namen) : string.Join(", ", namen.Take(3)) + " und " + (namen.Count - 3).ToString(CultureInfo.InvariantCulture) + " weitere";
            });
            return name + " (" + string.Join(", ", teile) + ")";
        }));
    }

    private static string Mehrzahlname(Einheitsart art) => art switch
    {
        Einheitsart.Trupp => "Trupps",
        Einheitsart.Orga => "Orga-Einheiten",
        _ => "Schiffe",
    };

    /// <summary>So nennt der Server die Grundstellung, solange niemand sie umbenannt hat.</summary>
    public const string GrundstellungName = "Grundstellung";

    public const string GrundstellungKurz = "GRUND";

    /// <summary>
    /// Die Phasen, die als Änderung zählen und ins Paket gehen: ohne eine Grundstellung, die nur hier steht und so heißt, wie der
    /// Server sie ohnehin nennt (sie kommt mit der ersten Phase dazu und wäre sonst eine Änderung, die niemand gemacht hat).
    /// </summary>
    private IEnumerable<NetzPhase> EigenePhasen() => _phasen.Values.Where(p =>
        !(p.Nr == 1 && !_basisPhasen.ContainsKey(1) && p.Name == GrundstellungName && p.Kurz == GrundstellungKurz && p.FelderAnzahl() == 0));

    // ================================================================ Ändern

    public void NetzSetzen(Netz netz)
    {
        ArgumentNullException.ThrowIfNull(netz);
        _netze[netz.Id] = Normal(netz);
        Melden();
    }

    /// <summary>Wie der Server: Rollenfelder und Kennungen auf das Netz fallen weg. Global bleibt.</summary>
    public void NetzLoeschen(ushort id)
    {
        if (id == Grenzen.GlobalNetzId || !_netze.Remove(id)) return;
        foreach (var r in _rollen.Values.Where(r => r.Felder.Any(f => f.NetzId == id)).ToList())
            _rollen[r.Id] = r with { Felder = [.. r.Felder.Where(f => f.NetzId != id)] };
        foreach (var p in _phasen.Values.Where(p => p.Felder?.Any(f => f.NetzId == id) == true).ToList())
            _phasen[p.Nr] = Normal(p with { Felder = [.. p.Felder!.Where(f => f.NetzId != id)] });
        // Wie der Server: Kennungen auf das Netz oder auf einen seiner Kanäle (6.16) fallen weg.
        bool Weg(ushort netz) => netz == id || (Kanalrechnung.Zerlegen(netz, out var n, out _) && n == id);
        foreach (var s in _schiffe.Values.Where(s => s.Netze.Values.Any(Weg)).ToList())
            _schiffe[s.Id] = Normal(s with { Netze = Kennungen(s.Netze.Where(p => !Weg(p.Value))) });
        Melden();
    }

    public void SchiffSetzen(Schiff schiff)
    {
        ArgumentNullException.ThrowIfNull(schiff);
        _schiffe[schiff.Id] = Normal(schiff);
        Melden();
    }

    /// <summary>Wie der Server: Die Netze der Einheit bleiben, gehören aber zu keiner Einheit mehr.</summary>
    public void SchiffLoeschen(ushort id)
    {
        if (!_schiffe.Remove(id)) return;
        foreach (var n in _netze.Values.Where(n => n.SchiffId == id).ToList()) _netze[n.Id] = n with { SchiffId = null, Schiffsnetz = null };
        Melden();
    }

    public void RolleSetzen(Rolle rolle)
    {
        ArgumentNullException.ThrowIfNull(rolle);
        _rollen[rolle.Id] = Normal(rolle);
        Melden();
    }

    /// <summary>Wie der Server: Die Plätze der Rolle auf den Einheiten fallen weg, ebenso ihre Varianten in den Phasen.</summary>
    public void RolleLoeschen(ushort id)
    {
        if (!_rollen.Remove(id)) return;
        foreach (var s in _schiffe.Values.Where(s => s.Plaetze.Any(p => p.RolleId == id)).ToList())
            _schiffe[s.Id] = s with { Plaetze = [.. s.Plaetze.Where(p => p.RolleId != id)] };
        foreach (var p in _phasen.Values.Where(p => p.Felder?.Any(f => f.RolleId == id) == true).ToList())
            _phasen[p.Nr] = Normal(p with { Felder = [.. p.Felder!.Where(f => f.RolleId != id)] });
        Melden();
    }

    /// <summary>Eine Kennung der Einheit auf ein Netz legen (oder mit <c>null</c> entfernen); das Netz gehört dann zu ihr.</summary>
    public void KennungSetzen(ushort schiffId, string kennung, ushort? netzId)
    {
        ArgumentNullException.ThrowIfNull(kennung);
        if (Schiff(schiffId) is not { } s) return;
        var netze = s.Netze.Where(p => !string.Equals(p.Key, kennung, StringComparison.Ordinal)).ToList();
        if (netzId is { } id) netze.Add(new KeyValuePair<string, ushort>(kennung, id));
        _schiffe[s.Id] = Normal(s with { Netze = Kennungen(netze) });
        // Ein Kanal mit Ch. gehört keiner Einheit; die Einheit nennt einen seiner Ch. (6.16).
        if (netzId is { } nid && Netz(nid) is { Kanaele: null } n && n.Id != Grenzen.GlobalNetzId
            && !_schiffe.Values.Any(x => x.Id != schiffId && x.Netze.Values.Contains(nid)))
            _netze[nid] = n with { SchiffId = schiffId, Schiffsnetz = KennungGueltig(kennung) ? kennung : null };
        Melden();
    }

    /// <summary>Neuer Kanal; <paramref name="herkunft"/> ist sein Herkunftsschild (seit 0.8.0), ohne Angabe "von Hand".</summary>
    public Netz NetzAnlegen(string name, string herkunft = VonHand)
    {
        ArgumentNullException.ThrowIfNull(name);
        var n = new Netz
        {
            Id = NeueId(_netze.Keys, _basisNetze.Keys, 2),
            Name = name,
            Typ = NetzTyp.Normal,
            Reihenfolge = NaechsteReihenfolge(_netze.Values.Where(x => x.Id != Grenzen.GlobalNetzId).Select(x => x.Reihenfolge)),
            NurTaste = false,
            AusloeserStandard = Ausloeser.Va,
            Offen = NetzOffen.Nein,
            Standard = false,
            Herkunft = herkunft,
        };
        NetzSetzen(n);
        return n;
    }

    /// <summary>Neue Rolle; <paramref name="herkunft"/> wie bei <see cref="NetzAnlegen"/>.</summary>
    public Rolle RolleAnlegen(string name, bool brauchtEinheit, string herkunft = VonHand)
    {
        ArgumentNullException.ThrowIfNull(name);
        var r = new Rolle
        {
            Id = NeueId(_rollen.Keys, _basisRollen.Keys, 1),
            Name = name,
            Reihenfolge = NaechsteReihenfolge(_rollen.Values.Select(x => x.Reihenfolge)),
            SelbstWaehlbar = true,
            BrauchtSchiff = brauchtEinheit,
            Felder = [],
            Herkunft = herkunft,
        };
        RolleSetzen(r);
        return r;
    }

    /// <summary>Neue Einheit mit dem nächsten freien Namen ihrer Art ("Schiff 4", "Trupp Bravo", "Orga 2"), ohne Netze und Plätze.</summary>
    public Schiff EinheitAnlegen(Einheitsart art, string? name = null)
    {
        var s = new Schiff
        {
            Id = NeueId(_schiffe.Keys, _basisSchiffe.Keys, 1),
            Name = name ?? FreierName(art),
            Reihenfolge = NaechsteReihenfolge(_schiffe.Values.Select(x => x.Reihenfolge)),
            Netze = new SortedDictionary<string, ushort>(StringComparer.Ordinal),
            Plaetze = [],
            Art = art,
        };
        SchiffSetzen(s);
        return Schiff(s.Id)!;
    }

    /// <summary>
    /// "Netze für diese Einheit anlegen" in einem Klick: bei einem Schiff Durchsagen, Crew und Brücke ("Crew 4" zu "Schiff 4"),
    /// bei Infanterie das Truppnetz, bei Orga das Orga-Netz, beide mit dem Namen der Einheit. Vorhandene Kennungen bleiben.
    /// </summary>
    public IReadOnlyList<Netz> NetzeFuerEinheitAnlegen(ushort schiffId, string herkunft = VonHand)
    {
        if (Schiff(schiffId) is not { } s) return [];
        var neu = new List<Netz>();
        var endung = Endung(s.Name);
        IEnumerable<(string Kennung, string Name)> soll = (s.Art ?? Einheitsart.Schiff) switch
        {
            Einheitsart.Trupp => [("TRUPP", s.Name)],
            Einheitsart.Orga => [("ORGA", s.Name)],
            _ => [("DURCHSAGEN", "Durchsagen " + endung), ("CREW", "Crew " + endung), ("BRUECKE", "Brücke " + endung)],
        };
        foreach (var (kennung, name) in soll)
        {
            if (Schiff(schiffId)!.Netze.ContainsKey(kennung)) continue;
            var n = NetzAnlegen(name, herkunft) with { SchiffId = schiffId, Schiffsnetz = kennung };
            _netze[n.Id] = n;
            neu.Add(n);
            KennungSetzen(schiffId, kennung, n.Id);
        }
        return neu;
    }

    /// <summary>"Einheit verdoppeln": Schiff 2 wie Schiff 1, mit eigenen Netzen und denselben Plätzen.</summary>
    public Schiff EinheitVerdoppeln(ushort schiffId)
    {
        var vorbild = Schiff(schiffId) ?? throw new ArgumentOutOfRangeException(nameof(schiffId));
        var name = NaechsterName(vorbild.Name);
        var kopie = EinheitAnlegen(vorbild.Art ?? Einheitsart.Schiff, name);
        kopie = kopie with { Typ = vorbild.Typ, Plaetze = vorbild.Plaetze };
        _schiffe[kopie.Id] = Normal(kopie);
        var alteEndung = Endung(vorbild.Name);
        var neueEndung = Endung(name);
        foreach (var (kennung, netzId) in vorbild.Netze)
        {
            var alt = Netz(netzId);
            var netzName = alt is null ? kennung
                : alt.Name.EndsWith(" " + alteEndung, StringComparison.Ordinal) ? alt.Name[..^alteEndung.Length] + neueEndung
                : string.Equals(alt.Name, vorbild.Name, StringComparison.Ordinal) ? name
                : alt.Name + " " + neueEndung;
            var n = NetzAnlegen(netzName);
            if (alt is not null) n = alt with { Id = n.Id, Name = netzName, Reihenfolge = n.Reihenfolge };
            _netze[n.Id] = n with { SchiffId = kopie.Id, Schiffsnetz = kennung };
            KennungSetzen(kopie.Id, kennung, n.Id);
        }
        Melden();
        return Schiff(kopie.Id)!;
    }

    /// <summary>
    /// "Rolle verdoppeln" (seit 0.8.0): eine Kopie mit dem nächsten freien Namen ("Kapitän 2") am Ende der Liste, mit denselben
    /// Feldern, Einstellungen und derselben Herkunft, dazu ihre Varianten in den Phasen. Plätze auf Einheiten bekommt die Kopie keine.
    /// </summary>
    public Rolle RolleVerdoppeln(ushort rolleId)
    {
        var vorbild = Rolle(rolleId) ?? throw new ArgumentOutOfRangeException(nameof(rolleId));
        var namen = _rollen.Values.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kopie = vorbild with
        {
            Id = NeueId(_rollen.Keys, _basisRollen.Keys, 1),
            Name = NaechsterName(vorbild.Name, namen),
            Reihenfolge = NaechsteReihenfolge(_rollen.Values.Select(x => x.Reihenfolge)),
        };
        _rollen[kopie.Id] = Normal(kopie);
        foreach (var p in _phasen.Values.Where(p => p.Felder?.Any(f => f.RolleId == rolleId) == true).ToList())
            _phasen[p.Nr] = Normal(p with { Felder = [.. p.Felder!, .. p.Felder!.Where(f => f.RolleId == rolleId).Select(f => f with { RolleId = kopie.Id })] });
        Melden();
        return Rolle(kopie.Id)!;
    }

    /// <summary>Baustein "Trupp" (Vorschlag): Infanterie mit Truppnetz, Plätze Truppführer 1 (mindestens 1), Sanitäter 1, Schütze 6.</summary>
    public Schiff BausteinTrupp()
    {
        var trupp = EinheitAnlegen(Einheitsart.Trupp);
        NetzeFuerEinheitAnlegen(trupp.Id, BausteinTruppHerkunft);
        var plaetze = new List<SchiffsPlatz>
        {
            new() { RolleId = EinheitsRolle("Truppführer", "TRUPP", BausteinTruppHerkunft).Id, Kapazitaet = 1, Mindest = 1 },
            new() { RolleId = EinheitsRolle("Sanitäter", "TRUPP", BausteinTruppHerkunft).Id, Kapazitaet = 1 },
            new() { RolleId = EinheitsRolle("Schütze", "TRUPP", BausteinTruppHerkunft).Id, Kapazitaet = 6 },
        };
        SchiffSetzen(Schiff(trupp.Id)! with { Plaetze = plaetze });
        return Schiff(trupp.Id)!;
    }

    /// <summary>Baustein "Orga-Einheit" (Vorschlag): Orga mit eigenem Netz und vier Plätzen der Rolle Orga.</summary>
    public Schiff BausteinOrga()
    {
        var orga = EinheitAnlegen(Einheitsart.Orga);
        NetzeFuerEinheitAnlegen(orga.Id, BausteinOrgaHerkunft);
        SchiffSetzen(Schiff(orga.Id)! with { Plaetze = [new SchiffsPlatz { RolleId = EinheitsRolle("Orga", "ORGA", BausteinOrgaHerkunft).Id, Kapazitaet = 4 }] });
        return Schiff(orga.Id)!;
    }

    /// <summary>Vorlage "Leer": nur Global (die Phasen fallen mit den Rollen; die Grundstellung des Servers bleibt dort stehen).</summary>
    public void Leeren()
    {
        _rollen.Clear();
        _schiffe.Clear();
        foreach (var nr in _phasen.Keys.Where(n => n > 1).ToList()) _phasen.Remove(nr);
        foreach (var id in _netze.Keys.Where(id => id != Grenzen.GlobalNetzId).ToList()) _netze.Remove(id);
        Melden();
    }

    /// <summary>Wendet die Schritte einer Vorlage auf den Entwurf an (IDs hat die Vorlage schon am Entwurf ausgerichtet).</summary>
    public void VorlageAnwenden(Flottenvorlage vorlage)
    {
        ArgumentNullException.ThrowIfNull(vorlage);
        foreach (var s in vorlage.Paket.Ops)
        {
            switch (s.Op)
            {
                case VerwaltungsOperation.NetzSetzen when s.ObjektLesen<Netz>().Body is Netz n:
                    _netze[n.Id] = Normal(n);
                    break;
                case VerwaltungsOperation.SchiffSetzen when s.ObjektLesen<Schiff>().Body is Schiff sc:
                    _schiffe[sc.Id] = Normal(sc);
                    break;
                case VerwaltungsOperation.RolleSetzen when s.ObjektLesen<Rolle>().Body is Rolle r:
                    _rollen[r.Id] = Normal(r);
                    break;
                case VerwaltungsOperation.NetzLoeschen when s.ObjektLesen<IdObjekt>().Body is IdObjekt id:
                    NetzLoeschen(id.Id);
                    break;
                case VerwaltungsOperation.SchiffLoeschen when s.ObjektLesen<IdObjekt>().Body is IdObjekt id:
                    SchiffLoeschen(id.Id);
                    break;
                case VerwaltungsOperation.RolleLoeschen when s.ObjektLesen<IdObjekt>().Body is IdObjekt id:
                    RolleLoeschen(id.Id);
                    break;
                case VerwaltungsOperation.Einstellung when s.ObjektLesen<EinstellungObjekt>().Body is EinstellungObjekt e:
                    _durchsageNurTaste = e.DurchsageNurTaste;
                    break;
            }
        }
        Melden();
    }

    /// <summary>Eine Einheit in ihrer Gruppe, eine Rolle oder ein Netz einen Platz nach oben (-1) oder unten (+1).</summary>
    public void SchiffVerschieben(ushort id, int richtung)
    {
        if (Schiff(id) is not { } s) return;
        var gruppe = Schiffe.Where(x => (x.Art ?? Einheitsart.Schiff) == (s.Art ?? Einheitsart.Schiff)).ToList();
        Tauschen(gruppe, id, richtung, x => x.Id, x => x.Reihenfolge, (x, r) => _schiffe[x.Id] = x with { Reihenfolge = r });
    }

    public void RolleVerschieben(ushort id, int richtung) =>
        Tauschen(Rollen, id, richtung, x => x.Id, x => x.Reihenfolge, (x, r) => _rollen[x.Id] = x with { Reihenfolge = r });

    public void NetzVerschieben(ushort id, int richtung) =>
        Tauschen([.. Netze.Where(n => n.Id != Grenzen.GlobalNetzId)], id, richtung, x => x.Id, x => x.Reihenfolge, (x, r) => _netze[x.Id] = x with { Reihenfolge = r });

    private void Tauschen<T>(IReadOnlyList<T> liste, ushort id, int richtung, Func<T, ushort> idVon, Func<T, ushort> reihe, Action<T, ushort> setzen)
    {
        var i = liste.ToList().FindIndex(x => idVon(x) == id);
        var j = i + Math.Sign(richtung);
        if (i < 0 || j < 0 || j >= liste.Count) return;
        var werte = liste.Select(reihe).ToList();
        // Gleiche Reihenfolgen erst durchnummerieren, sonst bewegt der Tausch nichts.
        if (werte.Distinct().Count() != werte.Count)
            werte = [.. Enumerable.Range(0, liste.Count).Select(k => (ushort)(werte.Min() + k))];
        var ordnung = liste.ToList();
        (ordnung[i], ordnung[j]) = (ordnung[j], ordnung[i]);
        for (var k = 0; k < ordnung.Count; k++)
            if (reihe(ordnung[k]) != werte[k]) setzen(ordnung[k], werte[k]);
        Melden();
    }

    // ================================================================ Zählen, Prüfen, Übernehmen

    /// <summary>Was sich gegenüber dem Server ändert, im Klartext: "neu: Einheit Schiff 16", "geändert: Netz Reserve (vorher Reserve 3)".</summary>
    public IReadOnlyList<string> Aenderungen()
    {
        var liste = new List<string>();
        Vergleichen(liste, "Einheit", _basisSchiffe, _schiffe, s => s.Name, Gleich);
        Vergleichen(liste, "Rolle", _basisRollen, _rollen, r => r.Name, Gleich);
        Vergleichen(liste, "Kanal", _basisNetze, _netze, n => n.Name, Gleich);
        if (PhasenErlaubt)
        {
            Vergleichen(liste, "Phase", _basisPhasen.ToDictionary(p => (ushort)p.Key, p => p.Value),
                EigenePhasen().ToDictionary(p => (ushort)p.Nr, p => p), p => p.Nr.ToString(CultureInfo.InvariantCulture) + " " + p.Name, Gleich);
        }
        if (_durchsageNurTaste != Basis.Einstellungen.DurchsageNurTaste) liste.Add("geändert: Durchsagen nur mit Sprechtaste");
        return liste;
    }

    private static void Vergleichen<T>(List<string> liste, string wort, IReadOnlyDictionary<ushort, T> basis, IReadOnlyDictionary<ushort, T> neu,
        Func<T, string> name, Func<T, T, bool> gleich)
    {
        foreach (var (id, alt) in basis.OrderBy(p => p.Key))
            if (!neu.ContainsKey(id)) liste.Add("gelöscht: " + wort + " " + name(alt));
        foreach (var (id, n) in neu.OrderBy(p => p.Key))
        {
            if (!basis.TryGetValue(id, out var alt)) liste.Add("neu: " + wort + " " + name(n));
            else if (!gleich(alt, n))
                liste.Add("geändert: " + wort + " " + name(n) + (string.Equals(name(alt), name(n), StringComparison.Ordinal) ? "" : " (vorher " + name(alt) + ")"));
        }
    }

    /// <summary>Die Regeln des Servers (6.9, 4.10) im Klartext; dazu Hinweise, die nicht sperren (Felder, die leer bleiben).</summary>
    public IReadOnlyList<Befund> Pruefen()
    {
        var b = new List<Befund>();
        void Sperrt(string text) => b.Add(new Befund(text, true));
        void Hinweis(string text) => b.Add(new Befund(text, false));

        if (_netze.Count - 1 > Grenzen.NetzeMax) Sperrt("Mehr als " + Grenzen.NetzeMax + " Kanäle neben Global (" + (_netze.Count - 1) + ").");
        if (_schiffe.Count > Grenzen.SchiffeMax) Sperrt("Mehr als " + Grenzen.SchiffeMax + " Einheiten (" + _schiffe.Count + ").");
        if (_rollen.Count > Grenzen.RollenMax) Sperrt("Mehr als " + Grenzen.RollenMax + " Rollen (" + _rollen.Count + ").");

        foreach (var n in Netze.Where(n => n.Id != Grenzen.GlobalNetzId))
        {
            var wer = Bezeichnung("Kanal", n.Id, n.Name);
            if (string.IsNullOrWhiteSpace(n.Name)) Sperrt(wer + ": Name fehlt.");
            else if (Laenge(n.Name) > Grenzen.NameMax) Sperrt(wer + ": Name länger als " + Grenzen.NameMax + " Zeichen.");
            if (n.Schiffsnetz is { } k && !KennungGueltig(k)) Sperrt(wer + ": " + KennungFalsch(k));
            if (n.SchiffId is { } sid && !_schiffe.ContainsKey(sid)) Sperrt(wer + " gehört zu einer Einheit, die es nicht mehr gibt.");
        }
        KanaelePruefen(Sperrt);

        foreach (var s in Schiffe)
        {
            var wer = string.IsNullOrWhiteSpace(s.Name) ? "Einheit Nummer " + s.Id.ToString(CultureInfo.InvariantCulture) : s.Name;
            if (string.IsNullOrWhiteSpace(s.Name)) Sperrt(wer + ": Name fehlt.");
            else if (Laenge(s.Name) > Grenzen.NameMax) Sperrt(wer + ": Name länger als " + Grenzen.NameMax + " Zeichen.");
            if (s.Typ is { } typ && Laenge(typ) > Grenzen.EinheitTypMax) Sperrt(wer + ": Typ länger als " + Grenzen.EinheitTypMax + " Zeichen.");
            if (s.Netze.Count > Grenzen.SchiffsnetzKennungenMax) Sperrt(wer + ": mehr als " + Grenzen.SchiffsnetzKennungenMax + " Einheitskanäle.");
            foreach (var (k, id) in s.Netze)
            {
                if (!KennungGueltig(k)) Sperrt(wer + ": " + KennungFalsch(k));
                else if (id == Grenzen.GlobalNetzId) Sperrt(wer + ": " + Kennungstext(k) + " zeigt auf Global; Global hat jeder schon.");
                else if (Kanalrechnung.Zerlegen(id, out var kn, out var kc))
                {
                    if (Netz(kn) is not { Kanaele: { } liste } || kc == 0 || kc >= liste.Count || (kn == Grenzen.GlobalNetzId && kc == Kanalrechnung.GlobalSelbst))
                        Sperrt(wer + ": " + Kennungstext(k) + " zeigt auf keinen Kanal mit Ch.");
                }
                else if (!_netze.ContainsKey(id)) Sperrt(wer + ": " + Kennungstext(k) + " zeigt auf keinen Kanal.");
                else if (Netz(id)!.Kanaele is not null) Sperrt(wer + ": " + Kennungstext(k) + " zeigt auf einen Kanal mit Ch., aber auf keinen Ch.; eine Einheit nennt einen Ch.");
            }
            if (s.Plaetze.Count > Grenzen.PlaetzeJeSchiffMax) Sperrt(wer + ": mehr als " + Grenzen.PlaetzeJeSchiffMax + " Plätze.");
            var gesehen = new HashSet<ushort>();
            foreach (var p in s.Plaetze)
            {
                if (Rolle(p.RolleId) is not { } r)
                {
                    Sperrt(wer + ": Ein Platz zeigt auf eine Rolle, die es nicht mehr gibt.");
                    continue;
                }
                if (!gesehen.Add(p.RolleId)) Sperrt(wer + ": Die Rolle " + r.Name + " steht zweimal.");
                if (p.Mindest is { } m && m > p.Kapazitaet) Sperrt(wer + ", " + r.Name + ": Mindestbesetzung " + m + " über den " + p.Kapazitaet + " Plätzen.");
                if (!r.BrauchtSchiff) Hinweis("Rolle " + r.Name + " braucht keine Einheit, steht aber auf " + wer + ".");
            }
        }

        foreach (var r in Rollen)
        {
            var wer = string.IsNullOrWhiteSpace(r.Name) ? "Rolle Nummer " + r.Id.ToString(CultureInfo.InvariantCulture) : "Rolle " + r.Name;
            if (string.IsNullOrWhiteSpace(r.Name)) Sperrt(wer + ": Name fehlt.");
            else if (Laenge(r.Name) > Grenzen.RollenNameMax) Sperrt(wer + ": Name länger als " + Grenzen.RollenNameMax + " Zeichen.");
            if (r.Felder.Count > Grenzen.FelderMax) Sperrt(wer + ": mehr als " + Grenzen.FelderMax + " Felder.");
            var nummern = new HashSet<byte>();
            foreach (var f in r.Felder)
            {
                var feld = "Feld " + (f.Feld + 1).ToString(CultureInfo.InvariantCulture);
                if (f.Feld > Grenzen.FeldMax) Sperrt(wer + ": " + feld + " gibt es nicht.");
                if (!nummern.Add(f.Feld)) Sperrt(wer + ": " + feld + " steht zweimal.");
                if ((f.NetzId is not null) == (f.Schiffsnetz is not null)) Sperrt(wer + ": " + feld + " hat keinen Kanal.");
                else if (f.NetzId == Grenzen.GlobalNetzId && Netz(Grenzen.GlobalNetzId)?.Kanaele is null) Sperrt(wer + ": " + feld + " nutzt Global; Global hat jeder schon im festen Feld.");
                else if (f.NetzId is { } id && !_netze.ContainsKey(id)) Sperrt(wer + ": " + feld + " zeigt auf einen Kanal, den es nicht mehr gibt.");
                else if (ChFalsch(f) is { } chText) Sperrt(wer + ", " + feld + ": " + chText);
                else if (f.Schiffsnetz is { } k && !KennungGueltig(k)) Sperrt(wer + ", " + feld + ": " + KennungFalsch(k));
                else if (f.Schiffsnetz is { } k2 && !r.BrauchtSchiff)
                    Hinweis(wer + " gehört zu keiner Einheit: " + feld + " (" + Kennungstext(k2) + ") bleibt leer.");
            }
            // Ein Netz nur einmal je Rolle: gleiche Netze oder gleiche Kennungen. Seit 19.10.2026 darf ein Kanal mit Ch. zweimal stehen, wenn die
            // Felder verschiedene Ch. meinen, einer fest, der andere frei oder fest auf einem anderen (Vorlage Star Citizen: "Jäger (Staffel n)"
            // und "Jäger (alle)"); der Server legt dann zwei Kanäle an (6.16).
            var felder = r.Felder.OrderBy(f => f.Feld).ToList();
            for (var i = 0; i < felder.Count; i++)
                for (var j = i + 1; j < felder.Count; j++)
                    if ((felder[i].NetzId is not null && Feldschluessel(felder[i].NetzId, felder[i].Ch) == Feldschluessel(felder[j].NetzId, felder[j].Ch))
                        || (felder[i].Schiffsnetz is { } k && string.Equals(k, felder[j].Schiffsnetz, StringComparison.Ordinal)))
                        Sperrt(wer + ": Feld " + (felder[i].Feld + 1) + " und Feld " + (felder[j].Feld + 1) + " nutzen beide " + Quelle(felder[i]) + ".");

            // Je Einheit, auf der die Rolle steht: Kennungen auflösen.
            var aufEinheiten = Schiffe.Where(s => s.Plaetze.Any(p => p.RolleId == r.Id)).ToList();
            if (r.BrauchtSchiff && aufEinheiten.Count == 0 && !string.IsNullOrWhiteSpace(r.Name))
                Hinweis(wer + " braucht eine Einheit, steht aber auf keiner: Niemand kann sie wählen.");
            foreach (var s in aufEinheiten)
            {
                var aufgeloest = new List<(byte Feld, ushort Netz, bool Kennung)>();
                foreach (var f in felder)
                {
                    if (f.NetzId is { } id) aufgeloest.Add((f.Feld, id, false));
                    else if (f.Schiffsnetz is { } k)
                    {
                        if (s.Netze.TryGetValue(k, out var nid)) aufgeloest.Add((f.Feld, nid, true));
                        else Hinweis(r.Name + " auf " + s.Name + ": Feld " + (f.Feld + 1) + " (" + Kennungstext(k) + ") gibt es dort nicht und bleibt leer.");
                    }
                }
                for (var i = 0; i < aufgeloest.Count; i++)
                    for (var j = i + 1; j < aufgeloest.Count; j++)
                        if (aufgeloest[i].Netz == aufgeloest[j].Netz && (aufgeloest[i].Kennung || aufgeloest[j].Kennung))
                            Sperrt(wer + " auf " + s.Name + ": Feld " + (aufgeloest[i].Feld + 1) + " und Feld " + (aufgeloest[j].Feld + 1) + " nutzen beide "
                                + NetzName(aufgeloest[i].Netz) + ".");
            }
        }
        if (PhasenErlaubt) PhasenPruefen(Sperrt);
        return b;
    }

    /// <summary>
    /// Die Regeln des Servers für Kanäle mit Ch. (6.16, Kanaele.NetzPruefen): nur an einem Server ab 0.6.0, 2 bis 31 Bezeichnungen zu höchstens
    /// 20 Zeichen (die erste ist die Ruhe), nur an Netz-IDs bis 1022, höchstens 64 Kanalnetze, Gruppe höchstens 20 Zeichen.
    /// </summary>
    private void KanaelePruefen(Action<string> sperrt)
    {
        var kanalnetze = _netze.Values.Where(n => n.Kanaele is not null).ToList();
        if (kanalnetze.Count > 0 && !KanaeleErlaubt)
            sperrt("Kanäle mit Ch. kennt der Server erst ab Fassung 0.6.0; erst den Server aktualisieren (" + string.Join(", ", kanalnetze.Select(n => n.Name)) + ").");
        if (kanalnetze.Count > Kanalrechnung.KanalnetzeMax) sperrt("Mehr als " + Kanalrechnung.KanalnetzeMax + " Kanäle mit Ch. (" + kanalnetze.Count + ").");
        foreach (var n in _netze.Values)
        {
            var wer = Bezeichnung("Kanal", n.Id, n.Name);
            if (n.Gruppe is { } g && Laenge(g) > Kanalrechnung.GruppeMax) sperrt(wer + ": Gruppe länger als " + Kanalrechnung.GruppeMax + " Zeichen.");
            if (n.Kanaele is not { } liste) continue;
            if (liste.Count is < Kanalrechnung.EintraegeMin or > Kanalrechnung.EintraegeMax)
                sperrt(wer + ": " + (liste.Count - 1) + " Ch.; es geht 1 bis " + Kanalrechnung.ChMax + " nach der Ruhe.");
            if (n.Id > Kanalrechnung.KanalnetzIdMax) sperrt(wer + ": Ch. gehen nur an Kanälen mit der Nummer bis " + Kanalrechnung.KanalnetzIdMax + ".");
            for (var i = 0; i < liste.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(liste[i])) sperrt(wer + ": Ch. " + i + " hat keine Bezeichnung.");
                else if (Laenge(liste[i]) > Kanalrechnung.ChNameMax) sperrt(wer + ": Ch. " + i + " (" + liste[i] + ") ist länger als " + Kanalrechnung.ChNameMax + " Zeichen.");
            }
        }
    }

    /// <summary>
    /// Woran zwei Felder einer Rolle oder Variante als derselbe Kanal gelten: das Netz, bei einem Kanal mit Ch. dazu der feste Ch. oder "frei".
    /// Zwei freie Felder im selben Kanal stoßen sich (beide bekämen denselben Ch.), ein festes und ein freies nicht (seit 19.10.2026).
    /// </summary>
    private string Feldschluessel(ushort? netzId, byte? ch) =>
        "N" + netzId?.ToString(CultureInfo.InvariantCulture) + (netzId is { } id && Netz(id)?.Kanaele is not null ? "/" + (ch?.ToString(CultureInfo.InvariantCulture) ?? "frei") : "");

    /// <summary>Der feste Ch. eines Rollen- oder Variantenfelds: nur an einem Kanal mit Ch., aus seiner Liste, nie Ch. 1 von Global (6.16).</summary>
    private string? ChFalsch(RollenFeld f)
    {
        if (f.Ch is not { } ch) return null;
        if (f.NetzId is not { } id || Netz(id)?.Kanaele is not { } liste) return "Ch. " + ch + " geht nur an einem Kanal mit Ch.";
        if (ch >= liste.Count) return "Ch. " + ch + " gibt es nicht, die Liste geht bis Ch. " + (liste.Count - 1) + ".";
        if (id == Grenzen.GlobalNetzId && ch == Kanalrechnung.GlobalSelbst) return "Ch. 1 von Global ist das feste Feld rechts unten, kein Ch. für ein Feld.";
        return null;
    }

    /// <summary>Die Regeln des Servers für Phasen (6.14, Feldregeln.PhasePruefen): Grenzen, Namen, Verweise, Felder je Variante.</summary>
    private void PhasenPruefen(Action<string> sperrt)
    {
        if (_phasen.Keys.Count(n => n > 1) > Grenzen.PhasenMax) sperrt("Mehr als " + Grenzen.PhasenMax + " Phasen über der Grundstellung.");
        var summe = _phasen.Values.Sum(p => p.FelderAnzahl());
        if (summe > Grenzen.PhasenFelderMax) sperrt("Mehr als " + Grenzen.PhasenFelderMax + " Felder in allen Varianten zusammen (" + summe + ").");
        foreach (var p in _phasen.Values)
        {
            var wer = "Phase " + p.Nr.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrWhiteSpace(p.Name) ? "" : " " + p.Name);
            if (string.IsNullOrWhiteSpace(p.Name)) sperrt(wer + ": Name fehlt.");
            else if (Laenge(p.Name) > Grenzen.PhaseNameMax) sperrt(wer + ": Name länger als " + Grenzen.PhaseNameMax + " Zeichen.");
            if (string.IsNullOrWhiteSpace(p.Kurz)) sperrt(wer + ": Kurzname fehlt.");
            else if (Laenge(p.Kurz) > Grenzen.PhaseKurzMax) sperrt(wer + ": Kurzname länger als " + Grenzen.PhaseKurzMax + " Zeichen.");
            if (p.Nr == 1 && p.FelderAnzahl() > 0) sperrt(wer + ": Die Grundstellung trägt keine Varianten; ihre Felder sind die der Rollen.");
            foreach (var g in (p.Felder ?? []).GroupBy(f => f.RolleId))
            {
                if (Rolle(g.Key) is not { } r)
                {
                    sperrt(wer + ": Eine Variante zeigt auf eine Rolle, die es nicht mehr gibt.");
                    continue;
                }
                var variante = wer + ", " + r.Name;
                if (g.Count() > Grenzen.FelderMax) sperrt(variante + ": mehr als " + Grenzen.FelderMax + " Felder.");
                var nummern = new HashSet<byte>();
                var netze = new HashSet<string>(StringComparer.Ordinal);
                foreach (var f in g)
                {
                    var feld = "Feld " + (f.Feld + 1).ToString(CultureInfo.InvariantCulture);
                    if (f.Feld > Grenzen.FeldMax) sperrt(variante + ": " + feld + " gibt es nicht.");
                    if (!nummern.Add(f.Feld)) sperrt(variante + ": " + feld + " steht zweimal.");
                    if ((f.NetzId is not null) == (f.Schiffsnetz is not null)) sperrt(variante + ": " + feld + " hat keinen Kanal.");
                    else if (f.NetzId == Grenzen.GlobalNetzId && Netz(Grenzen.GlobalNetzId)?.Kanaele is null) sperrt(variante + ": " + feld + " nutzt Global; Global hat jeder schon im festen Feld.");
                    else if (f.NetzId is { } id && !_netze.ContainsKey(id)) sperrt(variante + ": " + feld + " zeigt auf einen Kanal, den es nicht mehr gibt.");
                    else if (ChFalsch(new RollenFeld { Feld = f.Feld, NetzId = f.NetzId, Ch = f.Ch, Hoeren = f.Hoeren, Senden = f.Senden, SendenStandard = f.SendenStandard, Fest = f.Fest }) is { } chText)
                        sperrt(variante + ", " + feld + ": " + chText);
                    else if (f.Schiffsnetz is { } k && !KennungGueltig(k)) sperrt(variante + ", " + feld + ": " + KennungFalsch(k));
                    var schluessel = f.NetzId is not null ? Feldschluessel(f.NetzId, f.Ch) : "K" + f.Schiffsnetz;
                    if (!netze.Add(schluessel)) sperrt(variante + ": " + feld + " nutzt einen Kanal, der schon in einem anderen Feld steht.");
                }
            }
        }
    }

    /// <summary>Das Verwaltungspaket für alle Änderungen, in der Folge des Servers; höchstens 200 Schritte.</summary>
    public Paketergebnis Paket()
    {
        var ops = new List<VerwaltungsSchritt>();
        // Phasen löschen zuerst (die Grundstellung löscht der Server nie), setzen zuletzt: Dann gibt es ihre Rollen und Netze schon.
        if (PhasenErlaubt)
            foreach (var nr in _basisPhasen.Keys.Where(nr => nr > 1 && !_phasen.ContainsKey(nr)).Order()) ops.Add(Schritt(VerwaltungsOperation.PhaseLoeschen, new IdObjekt { Id = nr }));
        foreach (var id in _basisRollen.Keys.Where(id => !_rollen.ContainsKey(id)).Order()) ops.Add(Schritt(VerwaltungsOperation.RolleLoeschen, new IdObjekt { Id = id }));
        foreach (var id in _basisSchiffe.Keys.Where(id => !_schiffe.ContainsKey(id)).Order()) ops.Add(Schritt(VerwaltungsOperation.SchiffLoeschen, new IdObjekt { Id = id }));
        foreach (var id in _basisNetze.Keys.Where(id => !_netze.ContainsKey(id)).Order()) ops.Add(Schritt(VerwaltungsOperation.NetzLoeschen, new IdObjekt { Id = id }));
        foreach (var s in _schiffe.Values.Where(s => !_basisSchiffe.ContainsKey(s.Id)))
            ops.Add(Schritt(VerwaltungsOperation.SchiffSetzen, s with { Netze = new SortedDictionary<string, ushort>(StringComparer.Ordinal), Plaetze = [] }));
        foreach (var n in _netze.Values.Where(n => !_basisNetze.TryGetValue(n.Id, out var alt) || !Gleich(alt, n))) ops.Add(Schritt(VerwaltungsOperation.NetzSetzen, n));
        foreach (var r in _rollen.Values.Where(r => !_basisRollen.TryGetValue(r.Id, out var alt) || !Gleich(alt, r))) ops.Add(Schritt(VerwaltungsOperation.RolleSetzen, r));
        foreach (var s in _schiffe.Values.Where(s => !_basisSchiffe.TryGetValue(s.Id, out var alt) || !Gleich(alt, s))) ops.Add(Schritt(VerwaltungsOperation.SchiffSetzen, s));
        if (_durchsageNurTaste != Basis.Einstellungen.DurchsageNurTaste)
            ops.Add(Schritt(VerwaltungsOperation.Einstellung, new EinstellungObjekt { DurchsageNurTaste = _durchsageNurTaste }));
        if (PhasenErlaubt)
            foreach (var p in EigenePhasen().Where(p => !_basisPhasen.TryGetValue(p.Nr, out var alt) || !Gleich(alt, p))) ops.Add(Schritt(VerwaltungsOperation.PhaseSetzen, p));
        if (ops.Count == 0) return new Paketergebnis(null, "Nichts zu übernehmen.");
        if (ops.Count > Grenzen.PaketSchritteMax)
            return new Paketergebnis(null, "Zu viele Änderungen für einen Schritt: " + ops.Count + " von höchstens " + Grenzen.PaketSchritteMax
                + ". Erst einen Teil übernehmen, dann den Rest.");
        return new Paketergebnis(new VerwaltungsPaket { Ops = ops }, null);
    }

    /// <summary>Der Entwurf in der Form der NetzListe (Datei, Sicherung, Vorlagen), mit der Version der Basis.</summary>
    public NetzListe AlsNetzListe() => new()
    {
        Version = Basis.Version,
        Einstellungen = Basis.Einstellungen with { DurchsageNurTaste = _durchsageNurTaste },
        Netze = [.. _netze.Values],
        Schiffe = [.. _schiffe.Values],
        Rollen = [.. _rollen.Values],
        // Die Phasen gehören mit in Datei und Sicherung (bis 0.5.0 Server fehlten sie dort, PROJEKT S2).
        Phasen = _phasen.Count == 0 ? null : [.. _phasen.Values],
    };

    /// <summary>"Als Datei speichern": JSON in der Form der NetzListe.</summary>
    public byte[] AlsJson() => SteuerJson.Serialisieren(AlsNetzListe());

    /// <summary>"Aus Datei laden": eine NetzListe, oder <c>null</c>, wenn die Datei keine ist.</summary>
    public static NetzListe? JsonLesen(byte[] daten)
    {
        ArgumentNullException.ThrowIfNull(daten);
        try
        {
            return SteuerJson.Lesen(daten, SteuerJson.Info<NetzListe>()).Body as NetzListe;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ================================================================ Klartext und Auflösung

    /// <summary>Kennungen im Klartext statt Drahtnamen ("Crew der eigenen Einheit" statt CREW).</summary>
    public static string Kennungstext(string kennung) => kennung switch
    {
        "DURCHSAGEN" => "Durchsagen der eigenen Einheit",
        "CREW" => "Crew der eigenen Einheit",
        "BRUECKE" => "Brücke der eigenen Einheit",
        "TRUPP" => "Truppkanal der eigenen Einheit",
        "ORGA" => "Orga-Kanal der eigenen Einheit",
        _ => kennung + " der eigenen Einheit",
    };

    public static string Artname(Einheitsart? art) => art switch
    {
        Einheitsart.Trupp => "Infanterie",
        Einheitsart.Orga => "Orga",
        _ => "Schiff",
    };

    /// <summary>Name eines Netzes oder eines Kanals ("Brücke Ch. 3 (Schiff 3)", 6.16).</summary>
    public string NetzName(ushort id)
    {
        if (Kanalrechnung.Zerlegen(id, out var n, out var ch) && Netz(n) is { Kanaele: { } liste } kanalnetz)
            return kanalnetz.Name + " Ch. " + ch.ToString(CultureInfo.InvariantCulture) + (ch < liste.Count ? " (" + liste[ch] + ")" : "");
        return Netz(id)?.Name ?? "Kanal " + id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Netz oder Kennung eines Rollenfelds im Klartext.</summary>
    public string Quelle(RollenFeld f)
    {
        ArgumentNullException.ThrowIfNull(f);
        if (f.NetzId is { } id && Netz(id) is { Kanaele: { } liste } kanalnetz)
            return kanalnetz.Name + (f.Ch is { } ch && ch < liste.Count ? " Ch. " + ch.ToString(CultureInfo.InvariantCulture) + " (" + liste[ch] + ")" : " (Ch. wie zuletzt, ab Werk Ruhe)");
        return f.NetzId is { } nid ? NetzName(nid) : f.Schiffsnetz is { } k ? Kennungstext(k) : "nichts";
    }

    /// <summary>Wie die Felder einer Rolle auf einer Einheit aussehen ("So sieht Kapitän auf Schiff 2 aus").</summary>
    public IReadOnlyList<AufgeloestesFeld> Aufloesen(Rolle rolle, Schiff? schiff)
    {
        ArgumentNullException.ThrowIfNull(rolle);
        return [.. rolle.Felder.OrderBy(f => f.Feld).Select(f =>
        {
            ushort? id = f.NetzId ?? (f.Schiffsnetz is { } k && schiff is not null && schiff.Netze.TryGetValue(k, out var n) ? n : null);
            var name = f.NetzId is not null ? Quelle(f) : id is { } i ? NetzName(i) : f.Schiffsnetz is { } k2 ? Kennungstext(k2) : "leer";
            return new AufgeloestesFeld(f.Feld, name, f.Hoeren, f.Senden, f.SendenStandard, f.Fest, id is null);
        })];
    }

    public static bool KennungGueltig(string? kennung) =>
        !string.IsNullOrEmpty(kennung) && kennung.Length <= 16 && kennung.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    private static string KennungFalsch(string k) => "Die Kennung " + k + " geht nicht (nur A-Z, 0-9 und _, höchstens 16 Zeichen).";

    private static string Bezeichnung(string wort, ushort id, string name) =>
        string.IsNullOrWhiteSpace(name) ? wort + " Nummer " + id.ToString(CultureInfo.InvariantCulture) : wort + " " + name;

    private static int Laenge(string text) => text.EnumerateRunes().Count();

    // ================================================================ Innen

    private Rolle EinheitsRolle(string name, string kennung, string herkunft)
    {
        var vorhanden = _rollen.Values.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (vorhanden is not null) return vorhanden;
        var r = RolleAnlegen(name, brauchtEinheit: true, herkunft) with
        {
            Felder = [new RollenFeld { Feld = 0, Schiffsnetz = kennung, Hoeren = true, Senden = true, SendenStandard = true, Fest = false }],
        };
        RolleSetzen(r);
        return r;
    }

    private string FreierName(Einheitsart art)
    {
        var namen = _schiffe.Values.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        switch (art)
        {
            case Einheitsart.Trupp:
                foreach (var t in Truppnamen)
                    if (!namen.Contains("Trupp " + t)) return "Trupp " + t;
                return Nummeriert("Trupp", namen);
            case Einheitsart.Orga:
                return Nummeriert("Orga", namen);
            default:
                return Nummeriert("Schiff", namen);
        }
    }

    private static string Nummeriert(string wort, HashSet<string> namen)
    {
        var hoechste = namen.Select(n => n.StartsWith(wort + " ", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(n[(wort.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var z) ? z : 0)
            .DefaultIfEmpty(0).Max();
        return wort + " " + (hoechste + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>"Schiff 16" nach "Schiff 1" (nächste freie Nummer), "Logistik 2" nach "Logistik".</summary>
    private string NaechsterName(string name) => NaechsterName(name, _schiffe.Values.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>Wie <see cref="NaechsterName(string)"/>, gegen die Namen in <paramref name="namen"/> (Einheiten oder Rollen).</summary>
    private static string NaechsterName(string name, HashSet<string> namen)
    {
        var leer = name.LastIndexOf(' ');
        var stamm = leer > 0 && int.TryParse(name[(leer + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _) ? name[..leer] : name;
        if (string.Equals(stamm, name, StringComparison.Ordinal))
        {
            for (var n = 2; ; n++)
            {
                var versuch = stamm + " " + n.ToString(CultureInfo.InvariantCulture);
                if (!namen.Contains(versuch)) return versuch;
            }
        }
        return Nummeriert(stamm, namen);
    }

    /// <summary>Das letzte Wort eines Namens ("2" aus "Schiff 2"), ohne Leerzeichen der ganze Name.</summary>
    private static string Endung(string name)
    {
        var leer = name.TrimEnd().LastIndexOf(' ');
        return leer > 0 ? name.TrimEnd()[(leer + 1)..] : name.Trim();
    }

    private static ushort NeueId(IEnumerable<ushort> belegt, IEnumerable<ushort> basis, ushort start)
    {
        var alle = belegt.Concat(basis).ToHashSet();
        for (var id = start; id < 0xFFFF; id++)
            if (!alle.Contains(id)) return id;
        throw new InvalidOperationException("Keine freie ID mehr.");
    }

    private static ushort NaechsteReihenfolge(IEnumerable<ushort> vorhanden) => (ushort)Math.Min(ushort.MaxValue, vorhanden.Select(r => (int)r).DefaultIfEmpty(0).Max() + 1);

    private static SortedDictionary<string, ushort> Kennungen(IEnumerable<KeyValuePair<string, ushort>> paare)
    {
        var d = new SortedDictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var (k, v) in paare) d[k] = v;
        return d;
    }

    private static VerwaltungsSchritt Schritt<T>(VerwaltungsOperation op, T objekt) => new() { Op = op, Objekt = SteuerJson.Element(objekt) };

    private void Melden() => Geaendert?.Invoke();

    /// <summary>In die Form, in der der Server es zurückgibt (Abbildung.NetzListe).</summary>
    internal static Netz Normal(Netz n) => n with
    {
        SchiffId = n.SchiffId is 0 ? null : n.SchiffId,
        Schiffsnetz = string.IsNullOrEmpty(n.Schiffsnetz) ? null : n.Schiffsnetz,
        Kanaele = n.Kanaele is { Count: > 0 } k ? [.. k] : null,
        Gruppe = string.IsNullOrWhiteSpace(n.Gruppe) ? null : n.Gruppe,
    };

    /// <summary>Netze gleich, die Ch.-Liste nach Inhalt (der Record vergleicht Listen sonst nach Verweis), ohne die Herkunft.</summary>
    internal static bool Gleich(Netz a, Netz b) =>
        a with { Kanaele = null, Herkunft = null } == b with { Kanaele = null, Herkunft = null } && (a.Kanaele ?? []).SequenceEqual(b.Kanaele ?? []);

    internal static Schiff Normal(Schiff s) => s with
    {
        Art = s.Art ?? Einheitsart.Schiff,
        Typ = string.IsNullOrWhiteSpace(s.Typ) ? null : s.Typ,
        Netze = Kennungen(s.Netze),
        Plaetze = [.. s.Plaetze.Select(p => p.Mindest is 0 ? p with { Mindest = null } : p)],
    };

    internal static Rolle Normal(Rolle r) => r with
    {
        Felder = [.. r.Felder.OrderBy(f => f.Feld).Select(f => f.NetzId is 0 ? f with { NetzId = null } : f)],
    };

    /// <summary>Felder nach Rolle und Nummer, Netz 0 als leer, ohne Felder <c>null</c> (so gibt der Server sie zurück).</summary>
    internal static NetzPhase Normal(NetzPhase p) => p with
    {
        Felder = p.Felder is not { Count: > 0 } f
            ? null
            : [.. f.OrderBy(x => x.RolleId).ThenBy(x => x.Feld).Select(x => x with
            {
                NetzId = x.NetzId is 0 ? null : x.NetzId,
                Schiffsnetz = string.IsNullOrEmpty(x.Schiffsnetz) ? null : x.Schiffsnetz,
            })],
    };

    private static bool Gleich(NetzPhase a, NetzPhase b) =>
        a.Nr == b.Nr && a.Name == b.Name && a.Kurz == b.Kurz && (a.Felder ?? []).SequenceEqual(b.Felder ?? []);

    private static bool Gleich(Schiff a, Schiff b) =>
        a.Id == b.Id && a.Name == b.Name && a.Reihenfolge == b.Reihenfolge && a.Art == b.Art && a.Typ == b.Typ
        && a.Netze.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(b.Netze.OrderBy(p => p.Key, StringComparer.Ordinal))
        && a.Plaetze.SequenceEqual(b.Plaetze);

    private static bool Gleich(Rolle a, Rolle b) =>
        a.Id == b.Id && a.Name == b.Name && a.Reihenfolge == b.Reihenfolge && a.SelbstWaehlbar == b.SelbstWaehlbar && a.BrauchtSchiff == b.BrauchtSchiff
        && a.Kapazitaet == b.Kapazitaet && a.Felder.SequenceEqual(b.Felder);
}

/// <summary>Ein Befund der Prüfung im Klartext; <see cref="Sperrt"/>: so nimmt der Server es nicht an (sonst nur ein Hinweis).</summary>
public sealed record Befund(string Text, bool Sperrt);

/// <summary>Das Paket zum Übernehmen oder warum es keins gibt.</summary>
public sealed record Paketergebnis(VerwaltungsPaket? Paket, string? Fehler);

/// <summary>Ein aufgelöstes Feld einer Rolle auf einer Einheit (Vorschau "So sieht ... aus").</summary>
public sealed record AufgeloestesFeld(byte Feld, string Netz, bool Hoeren, bool Senden, bool Vorschlag, bool Fest, bool Leer);
