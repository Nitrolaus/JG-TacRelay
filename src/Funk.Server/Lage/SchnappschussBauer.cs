// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>
/// Baut einen Anfangsschnappschuss, etwa beim Laden aus der Ablage oder in Tests. Global (Netz 1) ist
/// von Anfang an da. <see cref="Bauen"/> prüft die Stimmigkeit und wirft bei Verstößen.
/// </summary>
public sealed class SchnappschussBauer
{
    private readonly ImmutableSortedDictionary<ushort, Netz>.Builder _netze = ImmutableSortedDictionary.CreateBuilder<ushort, Netz>();
    private readonly ImmutableSortedDictionary<ushort, Schiff>.Builder _schiffe = ImmutableSortedDictionary.CreateBuilder<ushort, Schiff>();
    private readonly ImmutableSortedDictionary<ushort, Rolle>.Builder _rollen = ImmutableSortedDictionary.CreateBuilder<ushort, Rolle>();
    private readonly ImmutableSortedDictionary<uint, Konto>.Builder _konten = ImmutableSortedDictionary.CreateBuilder<uint, Konto>();
    private readonly ImmutableSortedDictionary<uint, AktiveSitzung>.Builder _sitzungen = ImmutableSortedDictionary.CreateBuilder<uint, AktiveSitzung>();
    private readonly ImmutableSortedDictionary<byte, Einsatzphase>.Builder _phasen = ImmutableSortedDictionary.CreateBuilder<byte, Einsatzphase>();
    private readonly ImmutableArray<Kanalsperre>.Builder _sperrenAlle = ImmutableArray.CreateBuilder<Kanalsperre>();
    private Einstellungen _einstellungen = new();
    private long _zustandsVersion = 1;
    private long _netzListeVersion = 1;

    public SchnappschussBauer() => _netze[Netz.GlobalId] = Netz.Global();

    public SchnappschussBauer MitNetz(Netz netz)
    {
        ArgumentNullException.ThrowIfNull(netz);
        _netze[netz.Id] = netz;
        return this;
    }

    public SchnappschussBauer MitSchiff(Schiff schiff)
    {
        ArgumentNullException.ThrowIfNull(schiff);
        _schiffe[schiff.Id] = schiff;
        return this;
    }

    public SchnappschussBauer MitRolle(Rolle rolle)
    {
        ArgumentNullException.ThrowIfNull(rolle);
        _rollen[rolle.Id] = rolle;
        return this;
    }

    public SchnappschussBauer MitPhase(Einsatzphase phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        _phasen[phase.Nr] = phase;
        return this;
    }

    public SchnappschussBauer MitKonto(Konto konto)
    {
        ArgumentNullException.ThrowIfNull(konto);
        _konten[konto.Id] = konto;
        return this;
    }

    public SchnappschussBauer MitSitzung(uint sitzungsId, uint kontoId, uint geraetId = 0)
    {
        _sitzungen[sitzungsId] = new AktiveSitzung(sitzungsId, kontoId, geraetId);
        return this;
    }

    /// <summary>Eine Kanalsperre für alle ohne <c>ZUWEISEN</c> (6.17).</summary>
    public SchnappschussBauer MitSperreFuerAlle(Kanalsperre sperre)
    {
        _sperrenAlle.Add(sperre);
        return this;
    }

    public SchnappschussBauer MitEinstellungen(Einstellungen einstellungen)
    {
        _einstellungen = einstellungen ?? throw new ArgumentNullException(nameof(einstellungen));
        return this;
    }

    public SchnappschussBauer MitVersion(long zustandsVersion, long netzListeVersion)
    {
        _zustandsVersion = zustandsVersion;
        _netzListeVersion = netzListeVersion;
        return this;
    }

    public Schnappschuss Bauen()
    {
        var z = new Schnappschuss(_zustandsVersion, _netzListeVersion, _einstellungen, _netze.ToImmutable(), _schiffe.ToImmutable(),
            _rollen.ToImmutable(), _konten.ToImmutable(), _sitzungen.ToImmutable(), _phasen.ToImmutable(), _sperrenAlle.ToImmutable());
        var fehler = z.Pruefen();
        if (fehler.Count > 0) throw new InvalidOperationException("Schnappschuss nicht stimmig: " + string.Join("; ", fehler));
        return z;
    }
}
