// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using Funk.Server.Betrieb;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Komfortzustand am Konto (6.2, 7.2): Pegelvoreinstellungen (A4), Ablage (A21), die zuletzt gemeldete Sendeauswahl
/// und die Regeltabelle für den Raid-Helper-Import. Der Server deutet Pegel und Ablage nicht. Gespeichert wird
/// zusammengefasst und asynchron (<c>komfort.json</c>), ohne Austausch des Schnappschusses. Gehört der Zentrale.
/// </summary>
public sealed class Komfortablage
{
    public const string Datei = "komfort.json";

    private readonly Dictionary<uint, Dictionary<(uint Konto, ushort Netz), double>> _pegel = [];
    private readonly Dictionary<uint, Dictionary<S.AblageBereich, (JsonElement Daten, uint Version)>> _ablage = [];
    private readonly Dictionary<uint, (byte[] Felder, bool Global)> _auswahl = [];
    private List<S.Regel> _regeln = [];

    public bool Geaendert { get; private set; }

    public uint RegelVersion { get; private set; }

    public IReadOnlyList<S.Regel> Regeln => _regeln;

    public IReadOnlyDictionary<uint, (byte[] Felder, bool Global)> Auswahlen => _auswahl;

    public S.PegelVoreinstellungen PegelVon(uint kontoId)
    {
        if (!_pegel.TryGetValue(kontoId, out var p)) return new S.PegelVoreinstellungen { Eintraege = [] };
        return new S.PegelVoreinstellungen
        {
            Eintraege = [.. p.OrderBy(e => e.Key.Konto).ThenBy(e => e.Key.Netz)
                .Select(e => new S.PegelEintrag { KontoId = e.Key.Konto, NetzId = e.Key.Netz, Db = e.Value })],
        };
    }

    /// <summary>PegelSetzen (4.5): fehlendes <c>db</c> löscht; höchstens 2000 Einträge je Konto.</summary>
    public S.Fehlercode? PegelSetzen(uint kontoId, uint ziel, ushort netz, double? db)
    {
        if (!_pegel.TryGetValue(kontoId, out var p)) _pegel[kontoId] = p = [];
        if (db is not { } wert)
        {
            if (p.Remove((ziel, netz))) Geaendert = true;
            return null;
        }
        if (!p.ContainsKey((ziel, netz)) && p.Count >= S.Grenzen.PegelMax) return S.Fehlercode.Grenze;
        p[(ziel, netz)] = wert;
        Geaendert = true;
        return null;
    }

    public S.AblageAntwort AblageLesen(uint kontoId, S.AblageBereich bereich) =>
        _ablage.TryGetValue(kontoId, out var a) && a.TryGetValue(bereich, out var e)
            ? new S.AblageAntwort { Daten = e.Daten, Version = e.Version }
            : new S.AblageAntwort { Daten = null, Version = 0 };

    /// <summary>SCHREIBEN mit optionaler Version: weicht sie ab, kommt <c>VERALTET</c>.</summary>
    public (S.Fehlercode? Fehler, S.AblageAntwort Antwort) AblageSchreiben(uint kontoId, S.AblageBereich bereich, JsonElement daten, uint? version)
    {
        if (!_ablage.TryGetValue(kontoId, out var a)) _ablage[kontoId] = a = [];
        a.TryGetValue(bereich, out var alt);
        if (version is { } v && v != alt.Version) return (S.Fehlercode.Veraltet, new S.AblageAntwort { Daten = alt.Daten, Version = alt.Version });
        var neu = (daten.Clone(), unchecked(alt.Version + 1));
        a[bereich] = neu;
        Geaendert = true;
        return (null, new S.AblageAntwort { Daten = neu.Item1, Version = neu.Item2 });
    }

    public void AuswahlMerken(uint kontoId, IReadOnlyList<byte> felder, bool global)
    {
        _auswahl[kontoId] = ([.. felder], global);
        Geaendert = true;
    }

    public (IReadOnlyList<S.Regel> Regeln, uint Version) RegelnSetzen(IReadOnlyList<S.Regel> regeln)
    {
        _regeln = [.. regeln];
        RegelVersion++;
        Geaendert = true;
        return (_regeln, RegelVersion);
    }

    /// <summary>Konto gelöscht (3.3, 3.6): Pegel, Ablage und Auswahl fort; Pegel anderer für dieses Konto ebenso.</summary>
    public void KontoLoeschen(uint kontoId)
    {
        Geaendert |= _pegel.Remove(kontoId) | _ablage.Remove(kontoId) | _auswahl.Remove(kontoId);
        foreach (var p in _pegel.Values)
        {
            foreach (var schluessel in p.Keys.Where(k => k.Konto == kontoId).ToList())
            {
                p.Remove(schluessel);
                Geaendert = true;
            }
        }
    }

    public byte[] AlsJson()
    {
        Geaendert = false;
        var inhalt = new Inhalt(
            [.. _pegel.SelectMany(p => p.Value.Select(e => new PegelZeile(p.Key, e.Key.Konto, e.Key.Netz, e.Value)))],
            [.. _ablage.SelectMany(a => a.Value.Select(e => new AblageZeile(a.Key, e.Key, e.Value.Daten, e.Value.Version)))],
            [.. _auswahl.Select(a => new AuswahlZeile(a.Key, a.Value.Felder, a.Value.Global))],
            _regeln,
            RegelVersion);
        return JsonSerializer.SerializeToUtf8Bytes(inhalt, Ablageformat.Optionen);
    }

    public void Laden(ReadOnlySpan<byte> json)
    {
        var d = JsonSerializer.Deserialize<Inhalt>(json, Ablageformat.Optionen) ?? throw new InvalidDataException("komfort.json ist leer.");
        _pegel.Clear();
        _ablage.Clear();
        _auswahl.Clear();
        foreach (var p in d.Pegel)
        {
            if (!_pegel.TryGetValue(p.Besitzer, out var z)) _pegel[p.Besitzer] = z = [];
            z[(p.Konto, p.Netz)] = p.Db;
        }
        foreach (var a in d.Ablage)
        {
            if (!_ablage.TryGetValue(a.Konto, out var z)) _ablage[a.Konto] = z = [];
            z[a.Bereich] = (a.Daten.Clone(), a.Version);
        }
        foreach (var a in d.Auswahl) _auswahl[a.Konto] = (a.Felder, a.Global);
        _regeln = [.. d.Regeln];
        RegelVersion = d.RegelVersion;
        Geaendert = false;
    }

    private sealed record PegelZeile(uint Besitzer, uint Konto, ushort Netz, double Db);

    private sealed record AblageZeile(uint Konto, S.AblageBereich Bereich, JsonElement Daten, uint Version);

    private sealed record AuswahlZeile(uint Konto, byte[] Felder, bool Global);

    private sealed record Inhalt(List<PegelZeile> Pegel, List<AblageZeile> Ablage, List<AuswahlZeile> Auswahl, List<S.Regel> Regeln, uint RegelVersion);
}
