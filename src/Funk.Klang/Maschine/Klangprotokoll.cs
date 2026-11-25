// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>
/// Klangprotokoll (Klangstufe A, nur auf Wunsch): schreibt die Telemetrie der Sendekette als CSV in eine Datei
/// <c>klangprotokoll-JJJJMMTT-HHmmss.csv</c> im Ordner, den der Client gibt (sein Ablageordner). Je 100 ms eine Zeile (Pegel als
/// mittlere Leistung, Verstärkung der Pegelstufe am Ende, stärkste Absenkung des Begrenzers, höchste Sprachwahrscheinlichkeit,
/// Gate am Ende, gesendet ja oder nein, längste Laufzeiten), alle 10 s und am Ende eine Kommentarzeile mit den Laufzeiten
/// (p50/p95/p99/max) und den verpassten Fristen. Läuft nur im Melde-Thread, nie im Audio-Thread. Nach
/// <see cref="MaxBytes"/> hört es mit einem Vermerk auf.
/// </summary>
internal sealed class Klangprotokoll : IDisposable
{
    public const long MaxBytes = 20L * 1024 * 1024;
    private const int RahmenJeZeile = 10;
    private const long ZusammenfassungMs = 10_000;

    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    private readonly StreamWriter _w;
    private readonly TelemetrieEintrag[] _puffer = new TelemetrieEintrag[Telemetriejournal.Kapazitaet];
    private readonly TelemetrieEintrag[] _zeile = new TelemetrieEintrag[RahmenJeZeile];
    private long _gelesen;
    private int _inZeile;
    private long _letzteZusammenfassungMs = long.MinValue;
    private bool _voll;

    private Klangprotokoll(StreamWriter w, long ab)
    {
        _w = w;
        _gelesen = ab;
    }

    public string Pfad { get; private init; } = "";

    /// <summary>Legt Ordner und Datei an und beginnt mit dem nächsten Rahmen der Kette.</summary>
    public static Klangprotokoll Oeffnen(string ordner, Telemetriejournal journal, DateTime jetzt)
    {
        var voll = Path.GetFullPath(ordner);
        Directory.CreateDirectory(voll);
        var name = "klangprotokoll-" + jetzt.ToString("yyyyMMdd-HHmmss", C);
        var pfad = Path.Combine(voll, name + ".csv");
        for (var i = 2; File.Exists(pfad); i++) pfad = Path.Combine(voll, name + "-" + i.ToString(C) + ".csv");
        var w = new StreamWriter(new FileStream(pfad, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        w.WriteLine("# JG TacRelay Klangprotokoll der Sendekette, " + jetzt.ToString("yyyy-MM-dd HH:mm:ss", C) + ". Je Zeile 100 ms; Pegel in dBFS (mittlere Leistung), Wirkungen in dB, Zeiten in Mikrosekunden.");
        w.WriteLine("# gate: 0 Ruhe, 1 Anlauf, 2 Senden, 3 Nachlauf. pegelstufeDb am Ende der Zeile, begrenzerDb die stärkste Absenkung, pMax die höchste Sprachwahrscheinlichkeit.");
        w.WriteLine("zeitMs;rahmen;eingangDbfs;echoDbfs;verstaerktDbfs;rauschfilterDbfs;rauschfilterDb;pegelstufeDb;begrenzerDb;gatePegelDbfs;pMax;gate;sendet;echoUsMax;rauschfilterUsMax;pegelstufeUsMax;gateUsMax;opusUsMax;gesamtUsMax");
        return new Klangprotokoll(w, journal.Anzahl) { Pfad = pfad };
    }

    /// <summary>Neue Rahmen aus dem Journal schreiben (Melde-Thread).</summary>
    public void Fortschreiben(Telemetriejournal journal)
    {
        if (_voll) return;
        while (true)
        {
            var n = journal.Lesen(_gelesen, _puffer, out var erste);
            if (erste != _gelesen)
            {
                _w.WriteLine("# Lücke: " + (erste - _gelesen).ToString(C) + " Rahmen nicht gelesen");
                _inZeile = 0;
            }
            for (var i = 0; i < n; i++) Rahmen(_puffer[i], journal);
            _gelesen = erste + n;
            if (n < _puffer.Length) break;
        }
        _w.Flush();
        if (_w.BaseStream.Length > MaxBytes)
        {
            _w.WriteLine("# Grenze von " + (MaxBytes / 1024 / 1024).ToString(C) + " MB erreicht, Protokoll endet hier.");
            _w.Flush();
            _voll = true;
        }
    }

    /// <summary>Restliche Rahmen als letzte Zeile, Zusammenfassung, schließen.</summary>
    public void Beenden(Telemetriejournal journal)
    {
        if (!_voll)
        {
            Fortschreiben(journal);
            if (_inZeile > 0) ZeileSchreiben();
            Zusammenfassung(journal.Stand());
        }
        Dispose();
    }

    public void Dispose() => _w.Dispose();

    private void Rahmen(in TelemetrieEintrag e, Telemetriejournal journal)
    {
        _zeile[_inZeile++] = e;
        if (_inZeile < RahmenJeZeile) return;
        ZeileSchreiben();
        if (_letzteZusammenfassungMs == long.MinValue) _letzteZusammenfassungMs = e.ZeitMs;
        if (e.ZeitMs - _letzteZusammenfassungMs < ZusammenfassungMs) return;
        _letzteZusammenfassungMs = e.ZeitMs;
        Zusammenfassung(journal.Stand());
    }

    private void ZeileSchreiben()
    {
        var z = _zeile.AsSpan(0, _inZeile);
        var letzter = z[^1];
        var sb = new StringBuilder(200);
        sb.Append(C, $"{letzter.ZeitMs};{letzter.Rahmen};{Mittel(z, e => e.EingangDbfs):0.0};{Mittel(z, e => e.EchoDbfs):0.0};{Mittel(z, e => e.VerstaerktDbfs):0.0};{Mittel(z, e => e.RauschfilterDbfs):0.0};");
        sb.Append(C, $"{Durchschnitt(z, e => e.RauschfilterDb):0.0};{letzter.PegelstufeDb:0.00};{Kleinster(z, e => e.BegrenzerDb):0.00};{Mittel(z, e => e.GatePegelDbfs):0.0};");
        sb.Append(C, $"{Groesster(z, e => e.Sprachwahrscheinlichkeit):0.00};{(int)letzter.Gate};{(Irgendein(z) ? 1 : 0)};");
        sb.Append(C, $"{Groesster(z, e => e.Zeiten.EchoUs):0};{Groesster(z, e => e.Zeiten.RauschfilterUs):0};{Groesster(z, e => e.Zeiten.PegelstufeUs):0};");
        sb.Append(C, $"{Groesster(z, e => e.Zeiten.GateUs):0};{Groesster(z, e => e.Zeiten.OpusUs):0};{Groesster(z, e => e.Zeiten.GesamtUs):0}");
        _w.WriteLine(sb.ToString());
        _inZeile = 0;
    }

    private void Zusammenfassung(TelemetrieStand stand)
    {
        var sb = new StringBuilder("# Laufzeit nach ");
        sb.Append(C, $"{stand.Rahmen} Rahmen, Fristen verpasst {stand.DeadlineVerpasst} (über {stand.BudgetUs:0} µs):");
        foreach (var s in stand.Stufen) sb.Append(C, $" {s.Stufe} {s.P50Us:0}/{s.P95Us:0}/{s.P99Us:0}/{s.MaxUs:0}");
        sb.Append(" (p50/p95/p99/max µs)");
        _w.WriteLine(sb.ToString());
    }

    private static float Mittel(ReadOnlySpan<TelemetrieEintrag> z, Func<TelemetrieEintrag, float> wert)
    {
        double summe = 0;
        foreach (var e in z) summe += Math.Pow(10, wert(e) / 10.0);
        return Pegel.LeistungZuDb((float)(summe / z.Length));
    }

    private static float Durchschnitt(ReadOnlySpan<TelemetrieEintrag> z, Func<TelemetrieEintrag, float> wert)
    {
        double summe = 0;
        foreach (var e in z) summe += wert(e);
        return (float)(summe / z.Length);
    }

    private static float Kleinster(ReadOnlySpan<TelemetrieEintrag> z, Func<TelemetrieEintrag, float> wert)
    {
        var m = float.PositiveInfinity;
        foreach (var e in z) m = MathF.Min(m, wert(e));
        return m;
    }

    /// <summary>Größter endlicher Wert; 0, wenn die Stufe in keinem Rahmen lief.</summary>
    private static float Groesster(ReadOnlySpan<TelemetrieEintrag> z, Func<TelemetrieEintrag, float> wert)
    {
        var m = 0f;
        foreach (var e in z)
        {
            var w = wert(e);
            if (float.IsFinite(w)) m = MathF.Max(m, w);
        }
        return m;
    }

    private static bool Irgendein(ReadOnlySpan<TelemetrieEintrag> z)
    {
        foreach (var e in z)
        {
            if (e.Sendet) return true;
        }
        return false;
    }
}
