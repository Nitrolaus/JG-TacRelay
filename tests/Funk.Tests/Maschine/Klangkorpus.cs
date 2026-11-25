// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Klang.Kern;

namespace Funk.Tests.Maschine;

/// <summary>Eine Äußerung des Klangkorpus; Positionen in Samples bei 48 kHz ab Anfang des Korpus.</summary>
internal sealed record KorpusAeusserung(int Nr, string Stimme, string Huelle, float ZielDb, int Start, int Ende, string Text, IReadOnlyList<int> Woerter);

/// <summary>
/// Der Klangkorpus der Klangstufe A (tests\Funk.Tests\Daten\klangkorpus, erzeugt von klangkorpus-erzeugen.ps1 mit der
/// Windows-Sprachausgabe): 22 Äußerungen von drei Stimmen mit leisen und lauten Stellen, Hüllen innerhalb der Sätze, einem
/// betonten Wort, verklingenden Satzenden und Pausen von 150 ms bis 3 s; 110,3 s (seit 19.10.2026 neu erzeugt mit neutralem Rufnamen
/// "Staffel Eins", vorher 110,7 s). Die Datei hat 16 kHz und wird hier mit dem
/// speexdsp-Resampler (Qualität 10, ohne Versatz) auf 48 kHz gebracht.
/// </summary>
internal static class Klangkorpus
{
    private static readonly Lazy<(float[] Signal, IReadOnlyList<KorpusAeusserung> Aeusserungen)> Geladen = new(() => Laden("klangkorpus"));

    public static float[] Signal => Geladen.Value.Signal;

    public static IReadOnlyList<KorpusAeusserung> Aeusserungen => Geladen.Value.Aeusserungen;

    private static string Ordner => Path.Combine(EinrichtungTests.Repo(), "tests", "Funk.Tests", "Daten", "klangkorpus");

    /// <summary>Lädt <paramref name="name"/>.wav (16 kHz) mit <paramref name="name"/>-marken.csv aus dem Ordner des Korpus.</summary>
    internal static (float[], IReadOnlyList<KorpusAeusserung>) Laden(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Ordner, name + ".wav"));
        if (BitConverter.ToInt32(bytes, 24) != 16000 || BitConverter.ToInt16(bytes, 22) != 1 || BitConverter.ToInt16(bytes, 34) != 16)
            throw new InvalidOperationException(name + ".wav: erwartet 16 kHz, mono, 16 Bit.");
        var n = BitConverter.ToInt32(bytes, 40) / 2;
        var roh = new float[n];
        for (var i = 0; i < n; i++) roh[i] = BitConverter.ToInt16(bytes, 44 + 2 * i) / 32768f;

        using var r = new Resampler(16000, 48000, 10);
        r.UeberspringeNullen();
        var aus = new float[3 * n];
        var puffer = new float[4096];
        var pos = 0;
        var geschrieben = 0;
        var nullen = new float[r.EingangsLatenz];
        while (geschrieben < aus.Length)
        {
            var ein = pos < n ? roh.AsSpan(pos, Math.Min(1000, n - pos)) : nullen;
            r.Verarbeiten(ein, puffer, out var verbraucht, out var erzeugt);
            if (pos < n) pos += verbraucht;
            var k = Math.Min(erzeugt, aus.Length - geschrieben);
            puffer.AsSpan(0, k).CopyTo(aus.AsSpan(geschrieben));
            geschrieben += k;
        }

        var aeusserungen = new List<KorpusAeusserung>();
        var woerter = new Dictionary<int, List<int>>();
        foreach (var zeile in File.ReadAllLines(Path.Combine(Ordner, name + "-marken.csv")))
        {
            var f = zeile.Split(';');
            if (f[0] == "wort")
            {
                var nr = int.Parse(f[1], CultureInfo.InvariantCulture);
                if (!woerter.TryGetValue(nr, out var liste)) woerter[nr] = liste = [];
                liste.Add(3 * int.Parse(f[2], CultureInfo.InvariantCulture));
            }
        }
        foreach (var zeile in File.ReadAllLines(Path.Combine(Ordner, name + "-marken.csv")))
        {
            var f = zeile.Split(';');
            if (f[0] != "aeusserung") continue;
            var nr = int.Parse(f[1], CultureInfo.InvariantCulture);
            aeusserungen.Add(new KorpusAeusserung(nr, f[2], f[3], float.Parse(f[4], CultureInfo.InvariantCulture),
                3 * int.Parse(f[5], CultureInfo.InvariantCulture), 3 * int.Parse(f[6], CultureInfo.InvariantCulture), f[7],
                woerter.GetValueOrDefault(nr) ?? []));
        }
        return (aus, aeusserungen);
    }
}

/// <summary>
/// Kurze Funksprüche (Klangstufe C2a): klangkorpus-kurz.wav, lokal erzeugt mit <c>klangkorpus-erzeugen.ps1 -Name klangkorpus-kurz</c> (Plan in
/// klangkorpus-kurz-plan.csv): "Hit", "Check", "Kontakt", "Go", "Copy", "Ja", "Nein", "Links", "Rechts", "Feuer", "Break", "Raus" von drei Stimmen,
/// normal (-26 dBFS) und gerufen (-22 dBFS), je eine Äußerung, 220 bis 650 ms. Jede Äußerung ist genau ein Wort.
/// </summary>
internal static class Kurzkorpus
{
    private static readonly Lazy<(float[] Signal, IReadOnlyList<KorpusAeusserung> Aeusserungen)> Geladen = new(() => Klangkorpus.Laden("klangkorpus-kurz"));

    public static float[] Signal => Geladen.Value.Signal;

    public static IReadOnlyList<KorpusAeusserung> Aeusserungen => Geladen.Value.Aeusserungen;
}
