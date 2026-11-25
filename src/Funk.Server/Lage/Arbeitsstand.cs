// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>
/// Veränderlicher Arbeitsstand einer Transaktion des Zustandsschreibers über unveränderlichen Sammlungen.
/// Ein Rücksetzpunkt kostet nur das Festhalten der Referenzen; so lässt sich jeder Auftrag ganz oder gar
/// nicht anwenden (6.7 Schritt 5).
/// </summary>
internal sealed class Arbeitsstand
{
    public Arbeitsstand(Schnappschuss basis, Nebenstand neben, long jetzt)
    {
        Basis = basis;
        Jetzt = jetzt;
        NeueVersion = basis.ZustandsVersion + 1;
        Netze = basis.Netze;
        Schiffe = basis.Schiffe;
        Rollen = basis.Rollen;
        Konten = basis.Konten;
        Sitzungen = basis.Sitzungen;
        Einstellungen = basis.Einstellungen;
        NetzListeVersion = basis.NetzListeVersion;
        Phasen = basis.Phasen;
        SperrenAlle = basis.SperrenAlle;
        Neben = neben;
        var jeKonto = ImmutableSortedDictionary.CreateBuilder<uint, uint>();
        foreach (var s in basis.Sitzungen.Values) jeKonto[s.KontoId] = s.SitzungsId;
        SitzungJeKonto = jeKonto.ToImmutable();
    }

    public Schnappschuss Basis { get; }
    public long Jetzt { get; }

    /// <summary>Version des Schnappschusses, den diese Transaktion baut, falls sie etwas ändert.</summary>
    public long NeueVersion { get; }

    public ImmutableSortedDictionary<ushort, Netz> Netze { get; set; }
    public ImmutableSortedDictionary<ushort, Schiff> Schiffe { get; set; }
    public ImmutableSortedDictionary<ushort, Rolle> Rollen { get; set; }
    public ImmutableSortedDictionary<uint, Konto> Konten { get; private set; }
    public ImmutableSortedDictionary<uint, AktiveSitzung> Sitzungen { get; private set; }
    public ImmutableSortedDictionary<uint, uint> SitzungJeKonto { get; private set; }
    public Einstellungen Einstellungen { get; set; }
    public long NetzListeVersion { get; set; }
    public ImmutableSortedDictionary<byte, Einsatzphase> Phasen { get; set; }

    /// <summary>Kanalsperren für alle (6.17); ein Setzer muss <see cref="SchnappschussGeaendert"/> selbst setzen.</summary>
    public ImmutableArray<Kanalsperre> SperrenAlle { get; set; }

    public Nebenstand Neben { get; set; }

    /// <summary>
    /// Komfortzustand des Schreibers (die echte Sendeauswahl der Clients, 6.2), nur gelesen. Ohne ihn (Tests) gilt die Sendeauswahl
    /// am Konto.
    /// </summary>
    public Komfortzustand? Komfort { get; init; }
    public bool SchnappschussGeaendert { get; set; }

    /// <summary>Konten, deren Sendeauswahl der Schreiber neu gesetzt hat; ihr Komfortzustand wird verworfen.</summary>
    public ImmutableSortedSet<uint> AuswahlGesetzt { get; private set; } = [];

    public readonly record struct Ruecksetzpunkt(
        ImmutableSortedDictionary<ushort, Netz> Netze,
        ImmutableSortedDictionary<ushort, Schiff> Schiffe,
        ImmutableSortedDictionary<ushort, Rolle> Rollen,
        ImmutableSortedDictionary<uint, Konto> Konten,
        ImmutableSortedDictionary<uint, AktiveSitzung> Sitzungen,
        ImmutableSortedDictionary<uint, uint> SitzungJeKonto,
        Einstellungen Einstellungen,
        long NetzListeVersion,
        ImmutableSortedDictionary<byte, Einsatzphase> Phasen,
        ImmutableArray<Kanalsperre> SperrenAlle,
        Nebenstand Neben,
        bool SchnappschussGeaendert,
        ImmutableSortedSet<uint> AuswahlGesetzt);

    public Ruecksetzpunkt Sichern() => new(Netze, Schiffe, Rollen, Konten, Sitzungen, SitzungJeKonto, Einstellungen,
        NetzListeVersion, Phasen, SperrenAlle, Neben, SchnappschussGeaendert, AuswahlGesetzt);

    public void Zuruecksetzen(Ruecksetzpunkt p)
    {
        Netze = p.Netze;
        Schiffe = p.Schiffe;
        Rollen = p.Rollen;
        Konten = p.Konten;
        Sitzungen = p.Sitzungen;
        SitzungJeKonto = p.SitzungJeKonto;
        Einstellungen = p.Einstellungen;
        NetzListeVersion = p.NetzListeVersion;
        Phasen = p.Phasen;
        SperrenAlle = p.SperrenAlle;
        Neben = p.Neben;
        SchnappschussGeaendert = p.SchnappschussGeaendert;
        AuswahlGesetzt = p.AuswahlGesetzt;
    }

    public Konto? Konto(uint id) => Konten.TryGetValue(id, out var k) ? k : null;

    public bool Online(uint kontoId) => SitzungJeKonto.ContainsKey(kontoId);

    public PlatzZustand Platzzustand(Konto k) => Schnappschuss.PlatzZustandBerechnen(k, Online(k.Id), Jetzt);

    /// <summary>Rechte zum Zeitpunkt dieser Transaktion, mit einem laufenden Auftrag (6.12 Regel 5).</summary>
    public Recht RechteVon(uint kontoId) => Konto(kontoId)?.RechteZu(Jetzt) ?? Recht.Keine;

    /// <summary>Wie <see cref="RechteVon"/> für ein Konto, das noch nicht zurückgeschrieben ist.</summary>
    public Recht Wirksam(Konto k) => k.RechteZu(Jetzt);

    /// <summary>
    /// Die laufende Einsatzphase mit ihren Varianten, <c>null</c> in der Grundstellung (6.14). Jeder Weg zum Platz nimmt sie
    /// (<see cref="Feldregeln.AusRolle"/>).
    /// </summary>
    public Einsatzphase? AktivePhase() => PhaseDefinition(Neben.LaufendeNr());

    /// <summary>Die Definition einer Phase; <c>null</c> für die Grundstellung und für eine Nummer, die es nicht gibt.</summary>
    public Einsatzphase? PhaseDefinition(byte nr) => nr > 1 && Phasen.TryGetValue(nr, out var p) ? p : null;

    /// <summary>
    /// Die Sendeauswahl, die der Client gerade wirklich nutzt (6.14 Regel 7): der Komfortzustand, solange diese Transaktion die
    /// Auswahl des Kontos nicht selbst neu gesetzt hat, sonst die am Konto.
    /// </summary>
    public Sendeauswahl AuswahlTatsaechlich(Konto k)
    {
        ArgumentNullException.ThrowIfNull(k);
        if (AuswahlGesetzt.Contains(k.Id) || Komfort is null || !Komfort.Hat(k.Id, out var a)) return k.Sendeauswahl;
        return a;
    }

    /// <summary>Die Sendeauswahl am Konto gilt ab jetzt, auch wenn sie gleich geblieben ist: Der Komfortzustand wird verworfen.</summary>
    public void AuswahlUebernehmen(uint kontoId) => AuswahlGesetzt = AuswahlGesetzt.Add(kontoId);

    /// <summary>
    /// Schreibt ein Konto zurück. <paramref name="relevant"/>: weiterleitungsrelevante Änderung, dann wird
    /// <c>kontoVersion</c> die neue <c>zustandsVersion</c> (6.2). <paramref name="umstellungDurch"/> ungleich 0:
    /// Umstellung durch ein anderes Konto; verlorene Senderechte werden als Entzug vermerkt (ZIEL_ENTZOGEN).
    /// </summary>
    public void KontoSetzen(Konto neu, bool relevant = true, uint umstellungDurch = 0)
    {
        var alt = Konto(neu.Id);
        if (relevant)
        {
            neu = neu with
            {
                KontoVersion = NeueVersion,
                Stumm = Aufraeumen(neu.Stumm),
                Entzuege = Entzuege(alt, neu, umstellungDurch),
                Sendeauswahl = OhneGesperrte(neu, neu.Sendeauswahl),
            };
            if (alt is null || !alt.Sendeauswahl.Equals(neu.Sendeauswahl)) AuswahlGesetzt = AuswahlGesetzt.Add(neu.Id);
        }
        Konten = Konten.SetItem(neu.Id, neu);
        SchnappschussGeaendert = true;
    }

    public void KontoEntfernen(uint id)
    {
        Konten = Konten.Remove(id);
        SchnappschussGeaendert = true;
    }

    public void SitzungHinzu(AktiveSitzung s)
    {
        Sitzungen = Sitzungen.SetItem(s.SitzungsId, s);
        SitzungJeKonto = SitzungJeKonto.SetItem(s.KontoId, s.SitzungsId);
        SchnappschussGeaendert = true;
    }

    public void SitzungEntfernen(uint sitzungsId)
    {
        if (!Sitzungen.TryGetValue(sitzungsId, out var s)) return;
        Sitzungen = Sitzungen.Remove(sitzungsId);
        if (SitzungJeKonto.TryGetValue(s.KontoId, out var aktuell) && aktuell == sitzungsId)
            SitzungJeKonto = SitzungJeKonto.Remove(s.KontoId);
        SchnappschussGeaendert = true;
    }

    public Schnappschuss Bauen() =>
        new(NeueVersion, NetzListeVersion, Einstellungen, Netze, Schiffe, Rollen, Konten, Sitzungen, Phasen, SperrenAlle);

    /// <summary>
    /// Die Kanalsperre (6.17), die <paramref name="netzId"/> für das Konto zur Zeit dieser Transaktion sperrt: eigene oder für alle, mit
    /// den wirksamen Rechten (die Einsatzleitung ist von Sperren für alle ausgenommen). Global (auch dessen Ch. 1) und eine Ruhe sperrt nichts.
    /// </summary>
    public bool Gesperrt(Konto k, ushort netzId, out Kanalsperre sperre) =>
        Sperrregeln.Gesperrt(Netze, SperrenAlle, k, Wirksam(k), netzId, Jetzt, out sperre);

    /// <summary>
    /// Die Sendeauswahl ohne Felder, deren Kanal für das Konto gesperrt ist (6.17): Ein gesperrter Kanal steht nie in der Sendeauswahl, wie
    /// eine Ruhe. <see cref="KontoSetzen"/> hält das bei jeder weiterleitungsrelevanten Änderung ein.
    /// </summary>
    public Sendeauswahl OhneGesperrte(Konto k, Sendeauswahl auswahl)
    {
        ArgumentNullException.ThrowIfNull(k);
        ArgumentNullException.ThrowIfNull(auswahl);
        if (auswahl.Felder.IsEmpty || (k.Sperren.IsEmpty && SperrenAlle.IsEmpty)) return auswahl;
        var bleibt = auswahl.Felder.Where(f => k.FeldNummer(f) is not { } m || !Gesperrt(k, m.NetzId, out _)).ToImmutableArray();
        return bleibt.Length == auswahl.Felder.Length ? auswahl : new Sendeauswahl(bleibt, auswahl.Global);
    }

    private ImmutableArray<Stummschaltung> Aufraeumen(ImmutableArray<Stummschaltung> stumm)
    {
        if (stumm.IsDefaultOrEmpty) return [];
        var jetzt = Jetzt;
        return stumm.All(s => s.Bis > jetzt) ? stumm : [.. stumm.Where(s => s.Bis > jetzt)];
    }

    private ImmutableArray<Entzug> Entzuege(Konto? alt, Konto neu, uint durch)
    {
        var jetzt = Jetzt;
        var liste = new List<Entzug>();
        foreach (var e in neu.Entzuege.IsDefault ? [] : neu.Entzuege)
        {
            // Ein zurückgewonnenes Senderecht löscht den Vermerk.
            if (jetzt - e.Zeit > Fristen.EntzugMs) continue;
            if (neu.FeldMitNetz(e.NetzId) is { Senden: true }) continue;
            liste.Add(e);
        }
        if (alt is not null && durch != 0)
        {
            foreach (var f in alt.Felder)
            {
                if (!f.Senden || neu.FeldMitNetz(f.NetzId) is { Senden: true }) continue;
                liste.RemoveAll(e => e.NetzId == f.NetzId);
                liste.Add(new Entzug(f.NetzId, jetzt, durch, NeueVersion));
            }
        }
        return [.. liste];
    }
}
