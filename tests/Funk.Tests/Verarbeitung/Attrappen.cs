// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

/// <summary>
/// Dekodierer-Attrappe: Ein Paket trägt in den ersten 4 Bytes die Frame-Nr (little endian). Dekodiert wird ein
/// fortlaufender harmonischer Klang (Grundton 200 Hz, 5 Obertöne), Sample-genau an der Frame-Nr ausgerichtet.
/// FEC liefert den fehlenden Abschnitt vor dem Folgepaket leicht leiser (wie LBRR), Verdecken setzt den Klang
/// fort und wird je Aufruf leiser (wie PLC). Zählt alle Aufrufe.
/// </summary>
internal sealed class AttrappenDekodierer : IDekodierer
{
    public const float Amplitude = 0.4f;
    public const float GrundHz = 200f;

    private long _naechster;
    private float _plcFaktor = 1f;

    public int Abtastrate => 48000;
    public int Normal { get; private set; }
    public int Fec { get; private set; }
    public int Plc { get; private set; }
    public int PlcSamples { get; private set; }

    /// <summary>PLC liefert einen Gleichwert von 0,25 statt der Fortsetzung, damit die Verdeckung im Ausgang erkennbar ist.</summary>
    public bool PlcAlsGleichwert { get; init; }

    public static byte[] Paket(uint frame)
    {
        var p = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(p, frame);
        p[4] = 0x5A;
        return p;
    }

    public static float Wert(long sample)
    {
        var t = sample / 48000.0;
        double s = 0;
        for (var k = 1; k <= 5; k++) s += Math.Sin(2 * Math.PI * GrundHz * k * t) / k;
        return (float)(Amplitude * s / 1.6);
    }

    public int Dekodieren(ReadOnlySpan<byte> paket, Span<float> ziel)
    {
        if (paket.Length < 4) return -1;
        var start = BinaryPrimitives.ReadUInt32LittleEndian(paket) * 960L;
        for (var i = 0; i < 960; i++) ziel[i] = Wert(start + i);
        _naechster = start + 960;
        _plcFaktor = 1f;
        Normal++;
        return 960;
    }

    public int DekodierenFec(ReadOnlySpan<byte> folgepaket, Span<float> ziel, int fehlendeSamples)
    {
        if (folgepaket.Length < 4) return -1;
        var start = BinaryPrimitives.ReadUInt32LittleEndian(folgepaket) * 960L - fehlendeSamples;
        for (var i = 0; i < fehlendeSamples; i++) ziel[i] = 0.9f * Wert(start + i);
        _naechster = start + fehlendeSamples;
        Fec++;
        return fehlendeSamples;
    }

    public int Verdecken(Span<float> ziel, int fehlendeSamples)
    {
        _plcFaktor *= 0.8f;
        for (var i = 0; i < fehlendeSamples; i++) ziel[i] = PlcAlsGleichwert ? 0.25f : _plcFaktor * Wert(_naechster + i);
        _naechster += fehlendeSamples;
        Plc++;
        PlcSamples += fehlendeSamples;
        return fehlendeSamples;
    }

    public void Dispose()
    {
    }
}

/// <summary>Kodierer-Attrappe: schreibt einen laufenden Zähler (4 Bytes), dann Vorzeichen und Spitze des ersten Samples.</summary>
internal sealed class AttrappenKodierer : IKodierer
{
    public uint Zaehler { get; private set; }
    public int VerlustHinweis { get; private set; } = -1;
    public bool Scheitern { get; set; }
    public List<float[]> Eingaben { get; } = [];
    public bool Mitschreiben { get; set; } = true;

    public int Kodieren(ReadOnlySpan<float> pcm20ms, Span<byte> ziel)
    {
        if (pcm20ms.Length != 960) throw new PruefungException("Kodierer bekam keine 960 Samples");
        if (Scheitern) return -1;
        if (Mitschreiben) Eingaben.Add(pcm20ms.ToArray());
        BinaryPrimitives.WriteUInt32LittleEndian(ziel, Zaehler++);
        ziel[4] = (byte)(pcm20ms[0] >= 0 ? 1 : 0);
        ziel[5] = (byte)Math.Min(255, Pegel.Spitze(pcm20ms) * 255);
        return 6;
    }

    public void SetzeVerlustHinweis(int prozent) => VerlustHinweis = prozent;

    public void Dispose()
    {
    }
}

/// <summary>Klangquelle für Mischertests: Sinus mit fester Amplitude, Aktiv-Zustand und geplanten Ereignissen.</summary>
internal sealed class TestQuelle(float amplitude, float frequenzHz = 440f) : IKlangquelle
{
    private long _sample;

    public float Amplitude { get; set; } = amplitude;
    public bool Aktiv { get; set; } = true;
    public QuellenEreignis NaechstesEreignis { get; set; }

    /// <summary>Gleichwert statt Sinus (für Pegelmessungen ohne Welligkeit).</summary>
    public bool Gleichwert { get; set; }

    public QuellenEreignis Lesen(Span<float> ziel)
    {
        for (var i = 0; i < ziel.Length; i++)
        {
            ziel[i] = !Aktiv ? 0f
                : Gleichwert ? Amplitude
                : Amplitude * MathF.Sin(2f * MathF.PI * frequenzHz * (_sample + i) / 48000f);
        }
        _sample += ziel.Length;
        var e = NaechstesEreignis | (Aktiv ? QuellenEreignis.Aktiv : QuellenEreignis.Keine);
        NaechstesEreignis = QuellenEreignis.Keine;
        return e;
    }
}

/// <summary>Zusätzliche Prüfhilfen für Messwerte.</summary>
internal static class PruefeWert
{
    public static void Nahe(double erwartet, double ist, double toleranz, string was)
    {
        if (double.IsNaN(ist) || Math.Abs(erwartet - ist) > toleranz)
            throw new PruefungException(FormattableString.Invariant($"{was}: erwartet {erwartet} +/- {toleranz}, war {ist}"));
    }

    public static void Hoechstens(double grenze, double ist, string was)
    {
        if (double.IsNaN(ist) || ist > grenze)
            throw new PruefungException(FormattableString.Invariant($"{was}: höchstens {grenze}, war {ist}"));
    }

    public static void Mindestens(double grenze, double ist, string was)
    {
        if (double.IsNaN(ist) || ist < grenze)
            throw new PruefungException(FormattableString.Invariant($"{was}: mindestens {grenze}, war {ist}"));
    }

    /// <summary>Allokationen der Aktion im aktuellen Thread (nach einem Aufwärmlauf zu messen).</summary>
    public static long Allokiert(Action aktion)
    {
        var vorher = GC.GetAllocatedBytesForCurrentThread();
        aktion();
        return GC.GetAllocatedBytesForCurrentThread() - vorher;
    }
}
