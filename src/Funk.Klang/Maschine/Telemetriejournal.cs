// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Maschine;

/// <summary>
/// Telemetrie der Sendekette: die letzten <see cref="Kapazitaet"/> Rahmen (gut 10 s) als <see cref="TelemetrieEintrag"/> und je
/// <see cref="KlangStufe"/> eine Laufzeitverteilung über die ganze Sitzung. Genau ein Schreiber (der Aufnahme-Thread), beliebig
/// viele Leser, ohne Sperre und ohne Allokation beim Schreiben; Lesen wie im <see cref="Rahmenjournal"/>.
/// </summary>
internal sealed class Telemetriejournal
{
    public const int Kapazitaet = 1024;

    /// <summary>Frist eines Rahmens: Die Kette muss einen 10-ms-Rahmen in 10 ms schaffen.</summary>
    public const double BudgetUs = 10_000;

    private const int Maske = Kapazitaet - 1;
    private static readonly KlangStufe[] Stufen = Enum.GetValues<KlangStufe>();

    private readonly TelemetrieEintrag[] _ring = new TelemetrieEintrag[Kapazitaet];
    private readonly Laufzeitverteilung[] _laufzeiten;
    private long _anzahl;
    private long _verpasst;

    public Telemetriejournal()
    {
        _laufzeiten = new Laufzeitverteilung[Stufen.Length];
        for (var i = 0; i < _laufzeiten.Length; i++) _laufzeiten[i] = new Laufzeitverteilung();
    }

    public long Anzahl => Volatile.Read(ref _anzahl);

    public long Verpasst => Volatile.Read(ref _verpasst);

    /// <summary>Nur der Schreiber.</summary>
    public void Schreiben(in TelemetrieEintrag eintrag)
    {
        foreach (var s in Stufen)
        {
            var us = eintrag.Zeiten.Zeit(s);
            if (float.IsFinite(us)) _laufzeiten[(int)s].Hinzufuegen(us);
        }
        if (eintrag.Zeiten.GesamtUs > BudgetUs) Volatile.Write(ref _verpasst, _verpasst + 1);
        var n = _anzahl;
        _ring[(int)(n & Maske)] = eintrag;
        Volatile.Write(ref _anzahl, n + 1);
    }

    /// <summary>Kopiert Einträge ab Nummer <paramref name="von"/>; Rückgabe und <paramref name="ersteGueltige"/> wie <see cref="Rahmenjournal.Lesen"/>.</summary>
    public int Lesen(long von, Span<TelemetrieEintrag> ziel, out long ersteGueltige)
    {
        var bis = Anzahl;
        var start = Math.Max(von, bis - Kapazitaet + 1);
        var anzahl = (int)Math.Clamp(bis - start, 0, ziel.Length);
        for (var i = 0; i < anzahl; i++) ziel[i] = _ring[(int)((start + i) & Maske)];
        Interlocked.MemoryBarrier();
        var aeltesteSichere = Anzahl - Kapazitaet + 1;
        var verworfen = (int)Math.Clamp(aeltesteSichere - start, 0, anzahl);
        if (verworfen > 0) ziel[verworfen..anzahl].CopyTo(ziel);
        ersteGueltige = start + verworfen;
        return anzahl - verworfen;
    }

    public TelemetrieStand Stand()
    {
        Span<TelemetrieEintrag> letzter = stackalloc TelemetrieEintrag[1];
        var n = Anzahl;
        var gelesen = n > 0 ? Lesen(n - 1, letzter, out _) : 0;
        var stufen = new StufenLaufzeit[Stufen.Length];
        for (var i = 0; i < stufen.Length; i++) stufen[i] = _laufzeiten[i].Lesen(Stufen[i]);
        return new TelemetrieStand(n, Verpasst, BudgetUs, stufen, gelesen > 0 ? letzter[0] : default);
    }
}

/// <summary>
/// Häufigkeiten von Laufzeiten in Eimern zu einer sechzehntel Oktave (Schritt 4,4 %) ab 1/16 µs, dazu Anzahl und Maximum. Ein
/// Schreiber; Leser bekommen eine Momentaufnahme. Quantile sind die Oberkante ihres Eimers, höchstens das Maximum.
/// </summary>
internal sealed class Laufzeitverteilung
{
    private const int Eimer = 400;
    private const int JeOktave = 16;
    private const int Versatz = 4;

    private readonly long[] _eimer = new long[Eimer];
    private long _anzahl;
    private double _max;

    public void Hinzufuegen(double us)
    {
        var i = us <= 0 ? 0 : (int)Math.Clamp(Math.Floor((Math.Log2(us) + Versatz) * JeOktave), 0, Eimer - 1);
        _eimer[i]++;
        if (us > _max) Volatile.Write(ref _max, us);
        Volatile.Write(ref _anzahl, _anzahl + 1);
    }

    public StufenLaufzeit Lesen(KlangStufe stufe)
    {
        var anzahl = Volatile.Read(ref _anzahl);
        var max = Volatile.Read(ref _max);
        if (anzahl == 0) return new StufenLaufzeit(stufe, 0, 0, 0, 0, 0);
        var kopie = new long[Eimer];
        Array.Copy(_eimer, kopie, Eimer);
        var gesamt = 0L;
        foreach (var e in kopie) gesamt += e;
        return new StufenLaufzeit(stufe, anzahl, Quantil(kopie, gesamt, 0.50, max), Quantil(kopie, gesamt, 0.95, max),
            Quantil(kopie, gesamt, 0.99, max), max);
    }

    private static double Quantil(long[] eimer, long gesamt, double q, double max)
    {
        var ziel = Math.Max(1, (long)Math.Ceiling(q * gesamt));
        var summe = 0L;
        for (var i = 0; i < eimer.Length; i++)
        {
            summe += eimer[i];
            if (summe >= ziel) return Math.Min(max, Math.Pow(2, (double)(i + 1) / JeOktave - Versatz));
        }
        return max;
    }
}
