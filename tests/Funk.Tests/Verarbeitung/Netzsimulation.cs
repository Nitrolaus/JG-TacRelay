// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

/// <summary>Simulierte monotone Uhr in Mikrosekunden; die Wiedergabe stellt sie vor jedem Lesen.</summary>
internal sealed class SimUhr
{
    public long Us { get; set; }

    public long Lesen() => Us;
}

internal sealed record SimPaket(long AnkunftUs, ushort Phase, uint Frame, Sprachkennzeichen Kennzeichen, byte[] Daten);

/// <summary>Ein Frame, wie er wiedergegeben wurde: Zeitpunkt der Erzeugung und Verzögerung gegen die Sendezeit.</summary>
internal readonly record struct Wiedergabe(long ZeitUs, uint Frame, double VerzoegerungMs);

internal sealed class Wiedergabeergebnis
{
    public List<Wiedergabe> Frames { get; } = [];
    public List<(long ZeitUs, QuellenEreignis Ereignis)> Ereignisse { get; } = [];
    public float Spitze { get; set; }
    public bool NichtEndlich { get; set; }
    public List<float> Ausgabe { get; } = [];

    public IEnumerable<Wiedergabe> Ab(long zeitUs) => Frames.Where(f => f.ZeitUs >= zeitUs);
}

/// <summary>
/// Simulierte Netze und simulierte Wiedergabe mit fester Zeitbasis. Frame n einer Phase wird zum Zeitpunkt
/// n * 20 ms gesendet (die Frame-Nr hängt an der Sitzungsuhr, FUNKPROTOKOLL-v1 5.6). Die Ankunft ergibt sich aus
/// einer Verzögerungsfunktion; null heißt verloren.
/// </summary>
internal static class Netzsimulation
{
    public const long FrameUs = 20_000;

    /// <summary>
    /// Eine Sprechphase: <paramref name="anzahl"/> Frames ab <paramref name="ersterFrame"/>, danach dreimal ENDE
    /// (fortlaufende Frame-Nr, 20 ms Abstand). Die ersten <paramref name="vorlauf"/> Frames kommen als PREROLL-Burst
    /// zusammen mit dem ersten Echtzeit-Frame.
    /// </summary>
    public static List<SimPaket> Phase(
        ushort phase, uint ersterFrame, int anzahl, Func<uint, long?> verzoegerungUs,
        bool va = false, int vorlauf = 0, bool mitEnde = true, bool mitBeginn = true)
    {
        var liste = new List<SimPaket>();
        var grund = va ? Sprachkennzeichen.Sprachaktivierung : Sprachkennzeichen.Keine;
        for (var i = 0; i < anzahl; i++)
        {
            var frame = ersterFrame + (uint)i;
            var sendeFrame = i < vorlauf ? ersterFrame + (uint)vorlauf : frame;
            var d = verzoegerungUs(frame);
            if (d is null) continue;
            var k = grund;
            if (i == 0 && mitBeginn) k |= Sprachkennzeichen.Beginn;
            if (i < vorlauf) k |= Sprachkennzeichen.Preroll;
            liste.Add(new SimPaket(sendeFrame * FrameUs + d.Value, phase, frame, k, AttrappenDekodierer.Paket(frame)));
        }
        if (mitEnde)
        {
            for (var j = 0; j < 3; j++)
            {
                var frame = ersterFrame + (uint)anzahl + (uint)j;
                var d = verzoegerungUs(frame);
                if (d is null) continue;
                liste.Add(new SimPaket(frame * FrameUs + d.Value, phase, frame, grund | Sprachkennzeichen.Ende, []));
            }
        }
        return liste;
    }

    public static Func<uint, long?> Konstant(long us) => _ => us;

    /// <summary>Gleichverteilter Jitter: Grundlaufzeit plus 0..spanne, fester Seed.</summary>
    public static Func<uint, long?> Jitter(long grundUs, long spanneUs, int seed)
    {
        var werte = new Dictionary<uint, long>();
        var zufall = new Random(seed);
        return frame =>
        {
            if (!werte.TryGetValue(frame, out var w))
            {
                w = grundUs + (long)(zufall.NextDouble() * spanneUs);
                werte[frame] = w;
            }
            return w;
        };
    }

    /// <summary>Bündel: Pakete stauen sich und werden alle <paramref name="periodeUs"/> gemeinsam zugestellt.</summary>
    public static Func<uint, long?> Buendel(long grundUs, long periodeUs) => frame =>
    {
        var ankunft = frame * FrameUs + grundUs;
        var freigabe = (ankunft + periodeUs - 1) / periodeUs * periodeUs;
        return freigabe - frame * FrameUs;
    };

    public static Func<uint, long?> Verlust(Func<uint, long?> basis, Func<uint, bool> verloren) =>
        frame => verloren(frame) ? null : basis(frame);

    /// <summary>
    /// Wiedergabe: alle 10 ms (Versatz 3 ms, damit Ankunft und Abholung nie gleichzeitig liegen) werden die bis dahin
    /// angekommenen Pakete eingereiht und 480 Samples gelesen.
    /// </summary>
    public static Wiedergabeergebnis Abspielen(Jitterpuffer puffer, SimUhr uhr, IEnumerable<SimPaket> pakete, long dauerUs, bool ausgabeMitschreiben = false, long startUs = 3_000)
    {
        var sortiert = pakete.OrderBy(p => p.AnkunftUs).ToList();
        var ergebnis = new Wiedergabeergebnis();
        var block = new float[480];
        var index = 0;
        var erzeugt = puffer.Statistik.ErzeugteFrames;
        for (var t = startUs; t < dauerUs; t += 10_000)
        {
            while (index < sortiert.Count && sortiert[index].AnkunftUs <= t)
            {
                var p = sortiert[index++];
                if (!puffer.Einreihen(p.AnkunftUs, p.Phase, p.Frame, p.Kennzeichen, p.Daten))
                    throw new PruefungException("Übergabering voll");
            }
            uhr.Us = t;
            var e = puffer.Lesen(block);
            if ((e & ~QuellenEreignis.Aktiv) != 0) ergebnis.Ereignisse.Add((t, e));
            var s = puffer.Statistik;
            if (s.ErzeugteFrames != erzeugt)
            {
                erzeugt = s.ErzeugteFrames;
                ergebnis.Frames.Add(new Wiedergabe(t, s.ZuletztGespielterFrame, (t - s.ZuletztGespielterFrame * FrameUs) / 1000.0));
            }
            foreach (var v in block)
            {
                if (!float.IsFinite(v)) ergebnis.NichtEndlich = true;
                ergebnis.Spitze = MathF.Max(ergebnis.Spitze, MathF.Abs(v));
            }
            if (ausgabeMitschreiben) ergebnis.Ausgabe.AddRange(block);
        }
        return ergebnis;
    }
}
