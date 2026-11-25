// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Nativ;

namespace Funk.Klang.Kern;

/// <summary>
/// speexdsp-Resampler (Float, SSE), für 44,1-kHz-Geräte, den 16-kHz-Zweig und die Feinkorrektur der
/// Taktdrift über ein fraktionales Verhältnis (codec-ausgabe, Empfehlung 7). Mehrere Kanäle werden
/// verschränkt (interleaved) übergeben.
/// </summary>
/// <remarks>
/// <see cref="Verarbeiten"/> allokiert nicht und sperrt nicht. <see cref="SetzeRate"/> und
/// <see cref="SetzeRateFraktional"/> rechnen die Filtertabelle neu und können dabei nativ Speicher
/// nachfordern (speex_realloc in update_filter): für die Driftkorrektur deshalb von Anfang an mit
/// <see cref="Fraktional"/> anlegen, dann bleibt die Tabellengröße gleich (gemessen etwa 0,02 ms je
/// Umstellen). Nur aus einem Thread benutzen.
/// <para>
/// Raten und Verhältnis sind begrenzt (Gegenprüfung): Die Filterlänge wächst in speexdsp mit dem
/// Verhältnis, und ein unsinniger Wert (etwa 400 MHz zu 8 kHz) legt nativ Tabellen von vielen MB bis
/// GB an. Erlaubt sind Geräteraten von <see cref="MinAbtastrate"/> bis <see cref="MaxAbtastrate"/>
/// und Verhältnisse bis <see cref="MaxVerhaeltnis"/> in beide Richtungen.
/// </para>
/// <para>
/// NaN im Eingang ersetzt die Hülle nicht: Der Filter ist nicht rekursiv, NaN erscheint im Ausgang nur
/// für die Filterlänge und klingt dann ab (Test Resampler_NaN_KlingtNachFilterlaengeAb). APM, RNNoise,
/// Soft-Clip und Opus schützen sich dahinter selbst.
/// </para>
/// </remarks>
public sealed unsafe class Resampler : IDisposable
{
    /// <summary>SPEEX_RESAMPLER_QUALITY_DESKTOP; die Recherche nennt 5 bis 10.</summary>
    public const int StandardQualitaet = 5;
    public const int MaxKanaele = 8;
    /// <summary>Kleinste erlaubte Rate in Hz.</summary>
    public const int MinAbtastrate = 8000;
    /// <summary>Größte erlaubte Rate in Hz.</summary>
    public const int MaxAbtastrate = 384000;
    /// <summary>Größtes erlaubtes Verhältnis in jede Richtung (384000 zu 8000).</summary>
    public const int MaxVerhaeltnis = MaxAbtastrate / MinAbtastrate;

    private readonly SpeexResamplerHandle _handle;
    private long _aufrufe;

    /// <summary>Festes Verhältnis <paramref name="eingangsRate"/> zu <paramref name="ausgangsRate"/>.</summary>
    public Resampler(int eingangsRate, int ausgangsRate, int qualitaet = StandardQualitaet, int kanaele = 1)
    {
        PruefeParameter(eingangsRate, ausgangsRate, qualitaet, kanaele);
        _handle = SpeexNativ.Erstellen((uint)kanaele, (uint)eingangsRate, (uint)ausgangsRate, qualitaet, out int fehler);
        Kanaele = kanaele;
        PruefeHandle(fehler);
    }

    private Resampler(uint zaehler, uint nenner, int eingangsRate, int ausgangsRate, int qualitaet, int kanaele)
    {
        PruefeParameter(eingangsRate, ausgangsRate, qualitaet, kanaele);
        PruefeVerhaeltnis(zaehler, nenner);
        _handle = SpeexNativ.ErstellenFraktional((uint)kanaele, zaehler, nenner, (uint)eingangsRate, (uint)ausgangsRate, qualitaet, out int fehler);
        Kanaele = kanaele;
        PruefeHandle(fehler);
    }

    /// <summary>
    /// Fraktionales Verhältnis: <paramref name="zaehler"/>/<paramref name="nenner"/> ist das Verhältnis
    /// Eingangs- zu Ausgangsrate (speexdsp: ratio_num/ratio_den); die ganzzahligen Raten dienen nur der
    /// Filterauslegung. Beispiel 100 ppm schneller ausgeben: 480000/480048 bei 48000/48005.
    /// </summary>
    public static Resampler Fraktional(uint zaehler, uint nenner, int eingangsRate, int ausgangsRate, int qualitaet = StandardQualitaet, int kanaele = 1) =>
        new(zaehler, nenner, eingangsRate, ausgangsRate, qualitaet, kanaele);

    public int Kanaele { get; }

    /// <summary>Anzahl nativer Verarbeitungsaufrufe.</summary>
    public long Aufrufe => Volatile.Read(ref _aufrufe);

    /// <summary>Latenz in Eingangswerten je Kanal.</summary>
    public int EingangsLatenz => SpeexNativ.EingangsLatenz(_handle);

    /// <summary>Latenz in Ausgangswerten je Kanal.</summary>
    public int AusgangsLatenz => SpeexNativ.AusgangsLatenz(_handle);

    public int Qualitaet
    {
        get
        {
            SpeexNativ.QualitaetLesen(_handle, out int q);
            return q;
        }
    }

    /// <summary>Ganzzahlige Raten in Hz.</summary>
    public (int Eingang, int Ausgang) Raten
    {
        get
        {
            SpeexNativ.RateLesen(_handle, out uint ein, out uint aus);
            return ((int)ein, (int)aus);
        }
    }

    /// <summary>Gekürztes Verhältnis Eingang zu Ausgang.</summary>
    public (uint Zaehler, uint Nenner) Verhaeltnis
    {
        get
        {
            SpeexNativ.VerhaeltnisLesen(_handle, out uint z, out uint n);
            return (z, n);
        }
    }

    /// <summary>
    /// Rechnet so viel von <paramref name="eingang"/> um, wie in <paramref name="ausgang"/> passt. Beide
    /// verschränkt, Länge je ein Vielfaches der Kanalzahl, ohne Überlappung. Die Zähler gelten je Kanal:
    /// verbraucht wurden <c>verbraucht * Kanaele</c> Eingangswerte, erzeugt <c>erzeugt * Kanaele</c>.
    /// </summary>
    public void Verarbeiten(ReadOnlySpan<float> eingang, Span<float> ausgang, out int verbraucht, out int erzeugt)
    {
        LaengenPruefung.Vielfaches(eingang.Length, Kanaele, nameof(eingang));
        LaengenPruefung.Vielfaches(ausgang.Length, Kanaele, nameof(ausgang));
        if (eingang.Overlaps(ausgang))
            throw new ArgumentException("Ein- und Ausgabe dürfen sich nicht überlappen (speexdsp).", nameof(ausgang));
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        verbraucht = 0;
        erzeugt = 0;
        if (eingang.IsEmpty || ausgang.IsEmpty) return;

        uint einLaenge = (uint)(eingang.Length / Kanaele);
        uint ausLaenge = (uint)(ausgang.Length / Kanaele);
        int rc;
        fixed (float* e = eingang)
        fixed (float* a = ausgang)
        {
            rc = SpeexNativ.VerarbeitenVerschraenkt(_handle, e, &einLaenge, a, &ausLaenge);
        }
        _aufrufe++;
        if (rc != SpeexNativ.Erfolg)
            throw new InvalidOperationException("speex_resampler_process_interleaved_float: " + SpeexNativ.Text(rc));
        verbraucht = (int)einLaenge;
        erzeugt = (int)ausLaenge;
    }

    /// <summary>Neues festes Verhältnis. Rechnet die Filtertabelle neu, siehe Klassenhinweis.</summary>
    public void SetzeRate(int eingangsRate, int ausgangsRate)
    {
        PruefeRaten(eingangsRate, ausgangsRate);
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        PruefeRc(SpeexNativ.RateSetzen(_handle, (uint)eingangsRate, (uint)ausgangsRate), "speex_resampler_set_rate");
    }

    /// <summary>Neues fraktionales Verhältnis (Driftkorrektur). Rechnet die Filtertabelle neu, siehe Klassenhinweis.</summary>
    public void SetzeRateFraktional(uint zaehler, uint nenner, int eingangsRate, int ausgangsRate)
    {
        PruefeVerhaeltnis(zaehler, nenner);
        PruefeRaten(eingangsRate, ausgangsRate);
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        PruefeRc(SpeexNativ.RateSetzenFraktional(_handle, zaehler, nenner, (uint)eingangsRate, (uint)ausgangsRate), "speex_resampler_set_rate_frac");
    }

    /// <summary>Leert den Filterspeicher für einen neuen, unabhängigen Strom.</summary>
    public void Zuruecksetzen()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        PruefeRc(SpeexNativ.SpeicherLeeren(_handle), "speex_resampler_reset_mem");
    }

    /// <summary>Lässt die führenden Nullen der Filterlatenz weg (nur direkt nach dem Anlegen oder Zurücksetzen sinnvoll).</summary>
    public void UeberspringeNullen()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        PruefeRc(SpeexNativ.NullenUeberspringen(_handle), "speex_resampler_skip_zeros");
    }

    public void Dispose() => _handle.Dispose();

    private static void PruefeParameter(int eingangsRate, int ausgangsRate, int qualitaet, int kanaele)
    {
        PruefeRaten(eingangsRate, ausgangsRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(qualitaet, SpeexNativ.QualitaetMin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(qualitaet, SpeexNativ.QualitaetMax);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kanaele);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(kanaele, MaxKanaele);
    }

    private static void PruefeRaten(int eingangsRate, int ausgangsRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(eingangsRate, MinAbtastrate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(eingangsRate, MaxAbtastrate);
        ArgumentOutOfRangeException.ThrowIfLessThan(ausgangsRate, MinAbtastrate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ausgangsRate, MaxAbtastrate);
    }

    private static void PruefeVerhaeltnis(uint zaehler, uint nenner)
    {
        ArgumentOutOfRangeException.ThrowIfZero(zaehler);
        ArgumentOutOfRangeException.ThrowIfZero(nenner);
        if ((ulong)zaehler > (ulong)nenner * MaxVerhaeltnis || (ulong)nenner > (ulong)zaehler * MaxVerhaeltnis)
            throw new ArgumentOutOfRangeException(nameof(zaehler), zaehler, $"Verhältnis {zaehler}/{nenner} liegt außerhalb 1/{MaxVerhaeltnis} bis {MaxVerhaeltnis}.");
    }

    private void PruefeHandle(int fehler)
    {
        if (_handle.IsInvalid || fehler != SpeexNativ.Erfolg)
        {
            _handle.Dispose();
            throw new InvalidOperationException("speex_resampler_init: " + SpeexNativ.Text(fehler));
        }
    }

    private static void PruefeRc(int rc, string funktion)
    {
        if (rc != SpeexNativ.Erfolg) throw new InvalidOperationException(funktion + ": " + SpeexNativ.Text(rc));
    }
}
