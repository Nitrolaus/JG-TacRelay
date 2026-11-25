// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Klang;

namespace Funk.Client.Dienste;

/// <summary>
/// Lässt in der Vorführung (<c>--vorfuehrung</c>) wechselnde Funker in verschiedenen Kanälen sprechen und liefert dazu
/// Empfangspegel, damit Funkfeld und Kacheln Leben zeigen. Nur Attrappen, keine Netzwerkverbindung. Die Kanäle kommen aus den eigenen
/// Feldern der Lage (hörend, nicht auf Ruhe), dazu Global; seit 19.10.2026 mit der Vorlage Star Citizen (bis dahin feste Netz-IDs).
/// </summary>
public sealed class Vorfuehrung : IDisposable
{
    private static readonly (uint Konto, string Name)[] Folge =
    [
        (2, "Falke"), (3, "Kobold"), (4, "Nordlicht"), (7, "Sturmvogel"), (5, "Kranich"), (2, "Falke"),
    ];

    private readonly FunkDienstAttrappe _dienst;
    private readonly KlangMaschinenAttrappe _maschine;
    private readonly Timer _wechsel;
    private readonly Timer _pegel;
    private readonly Random _zufall = new(11);
    private readonly Lock _sperre = new();
    private int _schritt;

    public Vorfuehrung(FunkDienstAttrappe dienst, KlangMaschinenAttrappe maschine)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(maschine);
        _dienst = dienst;
        _maschine = maschine;
        _wechsel = new Timer(_ => Wechseln(), null, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(2.5));
        _pegel = new Timer(_ => PegelMelden(), null, 0, 50);
    }

    private void Wechseln()
    {
        int schritt;
        lock (_sperre) schritt = _schritt++;
        var a = Folge[schritt % Folge.Length];
        var b = Folge[(schritt + 2) % Folge.Length];
        var lage = _dienst.Lage;
        List<ushort> netze = [.. lage.Felder.Where(f => f.Hoeren && !Funk.Protokoll.Steuerung.Kanalrechnung.IstRuhe(f.NetzId)).Select(f => f.NetzId), 1];
        var netzA = netze[schritt % netze.Count];
        var netzB = netze[(schritt + 3) % netze.Count];
        var sprecher = schritt % 3 == 2
            ? new List<SprecherInfo> { new(a.Konto, a.Name, [netzA], false) }
            : [new(a.Konto, a.Name, [netzA], false), new(b.Konto, b.Name, [netzB], false)];
        _dienst.LageSetzen(l => l with { Sprecher = sprecher });
    }

    private void PegelMelden()
    {
        foreach (var s in _dienst.Lage.Sprecher)
        {
            float db;
            lock (_sperre) db = -30f + (float)(_zufall.NextDouble() * 18.0);
            foreach (var n in s.Netze) _maschine.EmpfangsPegelMelden(n, db);
        }
    }

    public void Dispose()
    {
        _wechsel.Dispose();
        _pegel.Dispose();
    }
}
