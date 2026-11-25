// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Server.Lage;

/// <summary>
/// Unveränderlicher Serverzustand (6.2): Konten, Netze, Schiffe, Rollen, Einstellungen und aktive
/// Sitzungen, dazu vorberechnete Tabellen für die Weiterleitung. Die Weiterleitung liest die Referenz
/// genau einmal je Paket und arbeitet ohne Sperren; jedes Paket sieht so den alten oder den neuen
/// Stand vollständig. Die Tabellen werden beim Bau einmal gefüllt und danach nur gelesen.
/// </summary>
public sealed class Schnappschuss
{
    internal const byte KeinMitglied = 0xFF;
    internal const byte BitHoeren = 0x80;
    internal const byte BitSenden = 0x40;
    internal const byte BitFest = 0x20;
    internal const byte FeldMaske = 0x0F;

    private readonly ImmutableSortedDictionary<uint, uint> _sitzungJeKonto;

    // Heißpfad-Tabellen: Netze und Sitzungen aufsteigend, Mitgliedschaften als Matrix Sitzung x Netz. Die Spalten sind alle Netze und
    // dazu jeder Kanal (6.16), den eine aktive Sitzung in einem Feld hält; eine Ruhe nie, sie hört und sendet nicht.
    private readonly ushort[] _netzIds;
    private readonly bool[] _nurTaste;
    private readonly ushort[] _regelnetz;
    private readonly uint[] _sitzungIds;
    private readonly Konto[] _sitzungKonten;
    private readonly Recht[] _sitzungRechte;
    private readonly Recht[] _auftragRechte;
    private readonly long[] _auftragVon;
    private readonly long[] _auftragBis;
    private readonly long[] _funkerBis;
    private readonly bool[] _mitSperren;
    private readonly byte[] _mitglied;
    private readonly int[] _hoerendeStart;
    private readonly uint[] _hoerende;

    internal Schnappschuss(
        long zustandsVersion,
        long netzListeVersion,
        Einstellungen einstellungen,
        ImmutableSortedDictionary<ushort, Netz> netze,
        ImmutableSortedDictionary<ushort, Schiff> schiffe,
        ImmutableSortedDictionary<ushort, Rolle> rollen,
        ImmutableSortedDictionary<uint, Konto> konten,
        ImmutableSortedDictionary<uint, AktiveSitzung> sitzungen,
        ImmutableSortedDictionary<byte, Einsatzphase>? phasen = null,
        ImmutableArray<Kanalsperre> sperrenAlle = default)
    {
        ZustandsVersion = zustandsVersion;
        NetzListeVersion = netzListeVersion;
        Einstellungen = einstellungen;
        Netze = netze;
        Schiffe = schiffe;
        Rollen = rollen;
        Konten = konten;
        Sitzungen = sitzungen;
        Phasen = phasen ?? ImmutableSortedDictionary<byte, Einsatzphase>.Empty;
        SperrenAlle = sperrenAlle.IsDefault ? [] : sperrenAlle;

        var jeKonto = ImmutableSortedDictionary.CreateBuilder<uint, uint>();
        foreach (var s in sitzungen.Values) jeKonto[s.KontoId] = s.SitzungsId;
        _sitzungJeKonto = jeKonto.ToImmutable();

        KanalnetzeAktiv = netze.Values.Any(x => !x.Kanaele.IsEmpty);
        var spalten = new SortedSet<ushort>(netze.Keys);
        if (KanalnetzeAktiv)
        {
            foreach (var s in sitzungen.Values)
            {
                if (!konten.TryGetValue(s.KontoId, out var k)) continue;
                foreach (var f in k.Felder)
                    if (Kanaele.Einordnen(netze, f.NetzId) == Netzart.Kanal) spalten.Add(f.NetzId);
            }
        }
        var n = spalten.Count;
        _netzIds = new ushort[n];
        _nurTaste = new bool[n];
        _regelnetz = new ushort[n];
        var i = 0;
        foreach (var id in spalten)
        {
            // Ein Kanal nimmt nurTaste und Stummschaltung von seinem Kanalnetz (6.16). Ausnahme seit Schritt 3.3: nurTaste von Global gilt
            // nur für Ch. 1 (Netz 1 selbst); die übrigen Ch. von Global erlauben Sprachaktivierung (6.5, Annahme).
            var regel = Kanaele.Regelnetz(netze, id);
            _netzIds[i] = id;
            _nurTaste[i] = netze[regel].NurTaste && !(regel == Netz.GlobalId && id != Netz.GlobalId);
            _regelnetz[i] = regel;
            i++;
        }

        var anzahl = sitzungen.Count;
        _sitzungIds = new uint[anzahl];
        _sitzungKonten = new Konto[anzahl];
        _sitzungRechte = new Recht[anzahl];
        _auftragRechte = new Recht[anzahl];
        _auftragVon = new long[anzahl];
        _auftragBis = new long[anzahl];
        _funkerBis = new long[anzahl];
        _mitSperren = new bool[anzahl];
        _mitglied = new byte[anzahl * n];
        _mitglied.AsSpan().Fill(KeinMitglied);
        var zaehlJeNetz = new int[n];
        i = 0;
        foreach (var s in sitzungen.Values)
        {
            // Eine Sitzung ohne Konto verletzt die Stimmigkeit; Pruefen() meldet das. Hier bleibt sie ohne Mitgliedschaft.
            var konto = konten.TryGetValue(s.KontoId, out var k) ? k : new Konto { Id = s.KontoId, Status = KontoStatus.Gesperrt };
            _sitzungIds[i] = s.SitzungsId;
            _sitzungKonten[i] = konto;
            _sitzungRechte[i] = konto.Rechte;
            if (konto.Beauftragung is { } b)
            {
                _auftragRechte[i] = b.Rechte;
                _auftragVon[i] = b.Von;
                _auftragBis[i] = b.Bis;
            }
            _funkerBis[i] = konto.AlsFunkerBis;
            _mitSperren[i] = !konto.Sperren.IsEmpty;
            foreach (var f in konto.Felder)
            {
                var ni = NetzIndex(f.NetzId);
                if (ni < 0 || f.Feld > FeldMaske) continue;
                var wert = (byte)(f.Feld | (f.Hoeren ? BitHoeren : 0) | (f.Senden ? BitSenden : 0) | (f.Fest ? BitFest : 0));
                _mitglied[i * n + ni] = wert;
                if (f.Hoeren) zaehlJeNetz[ni]++;
            }
            i++;
        }

        // Index Netz -> hörende Sitzungen (6.2). Global hören alle aktiven Sitzungen.
        var globalIndex = NetzIndex(Netz.GlobalId);
        if (globalIndex >= 0) zaehlJeNetz[globalIndex] = anzahl;
        _hoerendeStart = new int[n + 1];
        for (var ni = 0; ni < n; ni++) _hoerendeStart[ni + 1] = _hoerendeStart[ni] + zaehlJeNetz[ni];
        _hoerende = new uint[_hoerendeStart[n]];
        var pos = new int[n];
        for (var si = 0; si < anzahl; si++)
        {
            for (var ni = 0; ni < n; ni++)
            {
                var hoert = ni == globalIndex || (_mitglied[si * n + ni] != KeinMitglied && (_mitglied[si * n + ni] & BitHoeren) != 0);
                if (!hoert) continue;
                _hoerende[_hoerendeStart[ni] + pos[ni]++] = _sitzungIds[si];
            }
        }
    }

    public long ZustandsVersion { get; }
    public long NetzListeVersion { get; }

    /// <summary>Es gibt mindestens ein Kanalnetz (auch Global mit Ch.): Dann gilt das Versionstor 0.6.0 (4.7, 6.16).</summary>
    public bool KanalnetzeAktiv { get; }
    public Einstellungen Einstellungen { get; }
    public ImmutableSortedDictionary<ushort, Netz> Netze { get; }
    public ImmutableSortedDictionary<ushort, Schiff> Schiffe { get; }
    public ImmutableSortedDictionary<ushort, Rolle> Rollen { get; }
    public ImmutableSortedDictionary<uint, Konto> Konten { get; }

    /// <summary>
    /// Einsatzphasen der Flotte nach Nummer (6.14, Teil der NetzListe). Die laufende Phase steht im Nebenstand, nicht hier: Ein
    /// Phasenwechsel ändert die NetzListe nicht.
    /// </summary>
    public ImmutableSortedDictionary<byte, Einsatzphase> Phasen { get; }

    /// <summary>
    /// Kanalsperren für alle ohne <c>ZUWEISEN</c> (6.17, seit 0.6.0), höchstens 64. Sie stehen hier und nicht im Nebenstand, weil die
    /// Weiterleitung sie je Paket fragt; die eigenen Sperren eines Kontos stehen am Konto.
    /// </summary>
    public ImmutableArray<Kanalsperre> SperrenAlle { get; }

    /// <summary>Aktive Sitzungen nach Sitzungs-ID. Nur sie bekommen Sprache (6.8).</summary>
    public ImmutableSortedDictionary<uint, AktiveSitzung> Sitzungen { get; }

    public uint? SitzungVonKonto(uint kontoId) => _sitzungJeKonto.TryGetValue(kontoId, out var s) ? s : null;

    /// <summary>Rechte eines Kontos zum Zeitpunkt <paramref name="jetzt"/>, mit einem laufenden Auftrag (6.12).</summary>
    public Recht RechteVon(uint kontoId, long jetzt) => Konten.TryGetValue(kontoId, out var k) ? k.RechteZu(jetzt) : Recht.Keine;

    /// <summary>
    /// Belegung aller Plätze für das Live-Brett und das Einsteigen (Push <c>Belegung</c>): je Platz einer Einheit und je Rolle
    /// ohne Einheit, wie viele Konten ihn BELEGT und wie viele RESERVIERT halten (die Kapazität zählt beide, 6.9), dazu
    /// wie viele online sind und wie viele davon einen Platz haben. Der Client zählte bisher nur Konten online (K1).
    /// </summary>
    public Belegungsstand Belegung(long jetzt)
    {
        var zaehler = new Dictionary<(ushort, ushort), (int Belegt, int Reserviert)>();
        var mitPlatz = 0;
        foreach (var k in Konten.Values)
        {
            if (k.Platz is not { } p) continue;
            var online = IstOnline(k.Id);
            if (online) mitPlatz++;
            var zustand = PlatzZustandBerechnen(k, online, jetzt);
            if (zustand == PlatzZustand.Frei) continue;
            zaehler.TryGetValue((p.SchiffId, p.RolleId), out var z);
            zaehler[(p.SchiffId, p.RolleId)] = zustand == PlatzZustand.Belegt ? (z.Belegt + 1, z.Reserviert) : (z.Belegt, z.Reserviert + 1);
        }
        var plaetze = ImmutableArray.CreateBuilder<Platzbelegung>();
        foreach (var s in Schiffe.Values)
        {
            foreach (var p in s.Plaetze)
            {
                zaehler.TryGetValue((s.Id, p.RolleId), out var z);
                plaetze.Add(new Platzbelegung(s.Id, p.RolleId, z.Belegt, z.Reserviert, p.Kapazitaet, p.Mindest));
            }
        }
        foreach (var r in Rollen.Values)
        {
            if (r.BrauchtSchiff) continue;
            zaehler.TryGetValue((0, r.Id), out var z);
            plaetze.Add(new Platzbelegung(0, r.Id, z.Belegt, z.Reserviert, r.Kapazitaet, 0));
        }
        return new Belegungsstand(plaetze.ToImmutable(), Sitzungen.Count, mitPlatz);
    }

    /// <summary>
    /// Konten, deren Platzzustand zwischen <paramref name="vorher"/> und <paramref name="jetzt"/> allein durch Fristen wechselt (6.9:
    /// Wiederverbindung 10 min, Reservierung 12 h). Dafür gibt es keinen Commit und damit keine Praesenz; ohne Meldung stünde die
    /// Person auf dem Brett weiter im Block, und das Brett lehnte Ablagen dort als "voll" ab. Ohne Sitzung läuft der Zustand mit der
    /// Zeit nur vorwärts (RESERVIERT, BELEGT, FREI), deshalb reicht der Vergleich der beiden Zeitpunkte am heutigen Konto.
    /// </summary>
    public IReadOnlyList<uint> PlatzzustandGewechselt(long vorher, long jetzt)
    {
        List<uint>? liste = null;
        foreach (var k in Konten.Values)
        {
            if (k.Platz is null) continue;
            var online = IstOnline(k.Id);
            if (PlatzZustandBerechnen(k, online, vorher) != PlatzZustandBerechnen(k, online, jetzt)) (liste ??= []).Add(k.Id);
        }
        return liste is null ? [] : liste;
    }

    public bool IstOnline(uint kontoId) => _sitzungJeKonto.ContainsKey(kontoId);

    public bool IstAktiv(uint sitzungsId) => SitzungsIndex(sitzungsId) >= 0;

    public Konto? KontoVonSitzung(uint sitzungsId)
    {
        var i = SitzungsIndex(sitzungsId);
        return i < 0 ? null : _sitzungKonten[i];
    }

    /// <summary>Sitzungen, die dieses Netz hören (Global: alle aktiven), aufsteigend.</summary>
    public ReadOnlySpan<uint> HoerendeSitzungen(ushort netzId)
    {
        var ni = NetzIndex(netzId);
        return ni < 0 ? [] : _hoerende.AsSpan(_hoerendeStart[ni], _hoerendeStart[ni + 1] - _hoerendeStart[ni]);
    }

    /// <summary>Aktive Sitzungen, deren Konto dieses Recht zum Zeitpunkt <paramref name="jetzt"/> hat (Empfänger von Meldungen und Pushes, 6.12).</summary>
    public IEnumerable<uint> SitzungenMitRecht(Recht recht, long jetzt)
    {
        for (var i = 0; i < _sitzungIds.Length; i++)
            if (RechteAn(i, jetzt).Hat(recht)) yield return _sitzungIds[i];
    }

    /// <summary>Platzzustand nach 6.9 zum Zeitpunkt <paramref name="jetzt"/>.</summary>
    public PlatzZustand PlatzZustandVon(uint kontoId, long jetzt) =>
        Konten.TryGetValue(kontoId, out var k) ? PlatzZustandBerechnen(k, IstOnline(kontoId), jetzt) : PlatzZustand.Frei;

    internal static PlatzZustand PlatzZustandBerechnen(Konto k, bool online, long jetzt)
    {
        if (k.Platz is null) return PlatzZustand.Frei;
        if (online) return PlatzZustand.Belegt;
        if (k.Reservierung is { } r && jetzt - r.Gesetzt < Fristen.ReservierungMs) return PlatzZustand.Reserviert;
        if (k.SitzungsEnde > 0 && jetzt - k.SitzungsEnde < Fristen.WiederverbindungMs) return PlatzZustand.Belegt;
        return PlatzZustand.Frei;
    }

    /// <summary>
    /// <c>z.stumm(k, netz)</c> aus 6.3: Stummschaltung für dieses Netz, für Global (bei Netz 1) oder für alle; bei einem Kanal auch die
    /// für sein ganzes Kanalnetz (<paramref name="regelnetz"/>, 6.16). Mit <paramref name="netzId"/> 0 wird nur nach einer Stummschaltung
    /// für alle gefragt.
    /// </summary>
    public static bool IstStumm(Konto konto, ushort netzId, ushort regelnetz, long jetzt, out uint durch)
    {
        ArgumentNullException.ThrowIfNull(konto);
        foreach (var s in konto.Stumm)
        {
            if (s.Bis > jetzt && (s.NetzId == 0 || s.NetzId == netzId || s.NetzId == regelnetz))
            {
                durch = s.Durch;
                return true;
            }
        }
        durch = 0;
        return false;
    }

    /// <summary>Ob das Senderecht in diesem Netz in den letzten 10 s durch eine Umstellung wegfiel (6.3).</summary>
    public static bool IstEntzogen(Konto konto, ushort netzId, long jetzt, out Entzug entzug)
    {
        ArgumentNullException.ThrowIfNull(konto);
        foreach (var e in konto.Entzuege)
        {
            if (e.NetzId == netzId && jetzt - e.Zeit <= Fristen.EntzugMs)
            {
                entzug = e;
                return true;
            }
        }
        entzug = default;
        return false;
    }

    // ---- Heißpfad (intern, allokationsfrei) ----

    internal int NetzAnzahl => _netzIds.Length;

    internal int SitzungAnzahl => _sitzungIds.Length;

    internal int NetzIndex(ushort netzId) => _netzIds.AsSpan().BinarySearch(netzId) is var i and >= 0 ? i : -1;

    internal int SitzungsIndex(uint sitzungsId) => _sitzungIds.AsSpan().BinarySearch(sitzungsId) is var i and >= 0 ? i : -1;

    internal uint SitzungIdAn(int index) => _sitzungIds[index];

    internal Konto KontoAn(int index) => _sitzungKonten[index];

    /// <summary>
    /// Rechte der Sitzung zum Zeitpunkt <paramref name="jetzt"/> (Lage-ms): dauerhaft, dazu die eines Auftrags, solange er gilt,
    /// ohne die der Einsatzleitung, solange der Funker-Modus läuft (6.15). Der Heißpfad fragt je Paket; Auftrag und Funker-Modus
    /// enden so auf die Millisekunde, ohne neuen Schnappschuss (6.12 Regel 5). Gleich <see cref="Konto.RechteZu"/>.
    /// </summary>
    internal Recht RechteAn(int index, long jetzt)
    {
        var r = jetzt >= _auftragVon[index] && jetzt < _auftragBis[index] ? _sitzungRechte[index] | _auftragRechte[index] : _sitzungRechte[index];
        return jetzt < _funkerBis[index] ? r & ~Rechteregeln.Einsatzleitung : r;
    }

    internal bool NurTasteAn(int netzIndex) => _nurTaste[netzIndex];

    /// <summary>Das Netz, dessen Regeln für die Spalte gelten: bei einem Kanal sein Kanalnetz, sonst das Netz selbst (6.16).</summary>
    internal ushort RegelnetzAn(int netzIndex) => _regelnetz[netzIndex];

    /// <summary>
    /// Ob <paramref name="netzId"/> ein gültiger Kanal oder eine Ruhe eines bestehenden Kanalnetzes ist, auch wenn keine Sitzung ihn hält
    /// und er deshalb keine Spalte hat (6.3: dann KEIN_SENDERECHT statt UNBEKANNTES_NETZ).
    /// </summary>
    internal bool KanalGueltig(ushort netzId) => Kanaele.Einordnen(Netze, netzId) is Netzart.Kanal or Netzart.Ruhe;

    /// <summary>Mitgliedschaftsbyte: <see cref="KeinMitglied"/> oder Feld | Bits hören, senden, fest.</summary>
    internal byte MitgliedAn(int sitzungsIndex, int netzIndex) => _mitglied[sitzungsIndex * _netzIds.Length + netzIndex];

    /// <summary>
    /// Ob eine Kanalsperre (6.17) die Sitzung im Netz der Spalte zu <paramref name="jetzt"/> sperrt, und wer sie gesetzt hat. Die Zeit und die
    /// Ausnahme der Einsatzleitung gelten je Paket, ohne neuen Schnappschuss: Ein Auftrag, der beginnt oder endet, wirkt sofort.
    /// </summary>
    internal bool GesperrtAn(int sitzungsIndex, int netzIndex, long jetzt, out uint durch)
    {
        durch = 0;
        if (!_mitSperren[sitzungsIndex] && SperrenAlle.IsEmpty) return false;
        var netz = _netzIds[netzIndex];
        if (netz == Netz.GlobalId) return false;
        var rechte = SperrenAlle.IsEmpty ? Recht.Keine : RechteAn(sitzungsIndex, jetzt);
        if (!Sperrregeln.Gesperrt(_sitzungKonten[sitzungsIndex], rechte, SperrenAlle, netz, _regelnetz[netzIndex], jetzt, out var sperre)) return false;
        durch = sperre.Durch;
        return true;
    }

    // ---- Stimmigkeit ----

    /// <summary>Prüft die Invarianten des Zustands. Leere Liste heißt stimmig.</summary>
    public IReadOnlyList<string> Pruefen()
    {
        var fehler = new List<string>();
        if (!Netze.TryGetValue(Netz.GlobalId, out var global) || global.Typ != NetzTyp.Global)
            fehler.Add("Kanal 1 (Global) fehlt oder ist nicht GLOBAL");
        foreach (var n in Netze.Values)
        {
            if (n.Id is 0 or 0xFFFF) fehler.Add($"Kanal {n.Id}: ID außerhalb 1-65534");
            if (n.Id != Netz.GlobalId && n.Typ == NetzTyp.Global) fehler.Add($"Kanal {n.Id} ist ein zweites GLOBAL");
        }
        if (Netze.Count > Fristen.NetzeMax + 1) fehler.Add("zu viele Kanäle");
        foreach (var n in Netze.Values)
        {
            var problem = Kanaele.NetzPruefen(n, Netze);
            if (problem is not null) fehler.Add(problem);
        }

        foreach (var r in Rollen.Values)
        {
            var problem = Feldregeln.RollePruefen(r, Netze);
            if (problem is not null) fehler.Add($"Rolle {r.Id}: {problem}");
        }
        foreach (var s in Schiffe.Values)
        {
            var problem = Feldregeln.SchiffPruefen(s, Netze, Rollen);
            if (problem is not null) fehler.Add($"Schiff {s.Id}: {problem}");
        }
        foreach (var (nr, p) in Phasen)
        {
            if (p.Nr != nr) fehler.Add($"Phase {nr}: Nummer {p.Nr}");
            var problem = Feldregeln.PhasePruefen(p, Netze, Rollen);
            if (problem is not null) fehler.Add($"Phase {nr}: {problem}");
        }
        if (Phasen.Keys.Count(n => n > 1) > Fristen.PhasenMax) fehler.Add("zu viele Phasen");
        if (Phasen.Values.Sum(p => p.FelderAnzahl()) > Fristen.PhasenFelderMax) fehler.Add("zu viele Variantenfelder");

        // Kanalsperren (6.17): Grenzen und Ziele, die sich sperren lassen.
        if (SperrenAlle.Length > Kanalrechnung.SperrenAlleMax) fehler.Add("mehr als 64 Kanalsperren für alle");
        if (SperrenAlle.Length + Konten.Values.Sum(k => k.Sperren.Length) > Kanalrechnung.SperrenMax) fehler.Add("mehr als 500 Kanalsperren");
        foreach (var s in SperrenAlle)
            if (Sperrregeln.ZielPruefen(Netze, s.NetzId) != Fehlercode.Keiner) fehler.Add($"Kanalsperre für alle auf {s.NetzId} ungültig");

        foreach (var k in Konten.Values)
        {
            var problem = Feldregeln.FelderPruefen(k.Felder, Netze);
            if (problem is not null) fehler.Add($"Konto {k.Id}: {problem}");
            if (k.Sperren.Length > Kanalrechnung.SperrenJeKontoMax) fehler.Add($"Konto {k.Id}: mehr als 16 Kanalsperren");
            foreach (var s in k.Sperren)
                if (Sperrregeln.ZielPruefen(Netze, s.NetzId) != Fehlercode.Keiner) fehler.Add($"Konto {k.Id}: Kanalsperre auf {s.NetzId} ungültig");
            if (k.GewaehlteRolle is { } gr && !Rollen.ContainsKey(gr)) fehler.Add($"Konto {k.Id}: gewählte Rolle {gr} fehlt");
            if (k.Platz is { } p)
            {
                if (!Rollen.TryGetValue(p.RolleId, out var rolle)) fehler.Add($"Konto {k.Id}: Rolle {p.RolleId} fehlt");
                else if (p.SchiffId == 0 && rolle.BrauchtSchiff) fehler.Add($"Konto {k.Id}: Rolle braucht Schiff");
                else if (p.SchiffId != 0 && (!Schiffe.TryGetValue(p.SchiffId, out var schiff) || schiff.Kapazitaet(p.RolleId) is null))
                    fehler.Add($"Konto {k.Id}: Platz ({p.SchiffId}, {p.RolleId}) gibt es nicht");
            }
            var vorher = -1;
            foreach (var f in k.Sendeauswahl.Felder)
            {
                if (f <= vorher || f > FeldMaske) fehler.Add($"Konto {k.Id}: Sendeauswahl ungeordnet");
                vorher = f;
            }
        }

        var gesehen = new HashSet<uint>();
        foreach (var s in Sitzungen.Values)
        {
            if (s.SitzungsId == 0) fehler.Add("Sitzungs-ID 0");
            if (!Konten.TryGetValue(s.KontoId, out var k)) fehler.Add($"Sitzung {s.SitzungsId}: Konto {s.KontoId} fehlt");
            else if (k.Status != KontoStatus.Aktiv) fehler.Add($"Sitzung {s.SitzungsId}: Konto gesperrt");
            if (!gesehen.Add(s.KontoId)) fehler.Add($"Konto {s.KontoId} hat mehr als eine Sitzung");
        }
        return fehler;
    }
}
