// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>Markierte Äußerung in Samples (48 kHz), Ende ausschließlich. Start ist der hörbare Anlaut.</summary>
public readonly record struct Aeusserung(int Start, int Ende);

/// <summary>Kennzahlen eines Gate-Durchlaufs über eine Szene (audio-dsp.md, B20 und Empfehlung 8).</summary>
public sealed record GateMessung(
    double Minuten,
    int Oeffnungen,
    int Fehlausloesungen,
    double FehlausloesungenJeMinute,
    int Anlaute,
    int AbgeschnitteneAnlaute,
    double AnteilAbgeschnitten,
    double MittlererAnlaufverzugMs,
    long AbgewieseneAnlaeufe,
    double AnteilGesendet)
{
    public override string ToString() => FormattableString.Invariant(
        $"{Minuten:F1} min: {Oeffnungen} Öffnungen, {FehlausloesungenJeMinute:F2} Fehlauslösungen/min, {AbgeschnitteneAnlaute}/{Anlaute} Anlaute abgeschnitten, Anlaufverzug {MittlererAnlaufverzugMs:F0} ms, {AbgewieseneAnlaeufe} abgewiesen, {AnteilGesendet * 100:F0} % gesendet");
}

/// <summary>
/// Messhilfe für das Gate, offline und deterministisch: Szene rahmenweise durch Schätzer und Gate schicken
/// und zählen. Fehlauslösung (NDS) = eine Sprechphase, die keine markierte Äußerung berührt.
/// Abgeschnittener Anlaut (FEC) = der erste gesendete Rahmen einer Äußerung beginnt mehr als 10 ms nach
/// ihrem markierten Start, oder die Äußerung wird gar nicht gesendet. Allokiert; nicht für den Audio-Thread.
/// </summary>
public static class GateKennzahlen
{
    private const int Rahmen = Klangformat.Samples10ms;
    private const int ToleranzSamples = 10 * Klangformat.SamplesJeMs;

    public static GateMessung Messen(
        ReadOnlySpan<float> signal,
        IReadOnlyList<Aeusserung> aeusserungen,
        ISprachwahrscheinlichkeit schaetzer,
        GateEinstellungen? einstellungen = null,
        float empfindlichkeit = 0f)
    {
        ArgumentNullException.ThrowIfNull(aeusserungen);
        ArgumentNullException.ThrowIfNull(schaetzer);
        var gate = new SprachGate(einstellungen) { Empfindlichkeit = empfindlichkeit };
        schaetzer.Zuruecksetzen();
        var rahmenGesamt = signal.Length / Rahmen;
        var gesendet = new bool[rahmenGesamt];
        var phasen = new List<(long Start, long Ende, long Geoeffnet)>();
        var puffer = new float[Rahmen];
        long phasenStart = -1, geoeffnet = -1;

        for (var r = 0; r < rahmenGesamt; r++)
        {
            var rahmen = signal.Slice(r * Rahmen, Rahmen);
            var p = schaetzer.Schaetzen(rahmen);
            gate.Verarbeiten(rahmen, p);
            while (gate.Holen(puffer, out var art, out var nummer))
            {
                if (nummer >= 0 && nummer < rahmenGesamt) gesendet[nummer] = true;
                if ((art & SenderahmenArt.Beginn) != 0)
                {
                    if (phasenStart >= 0) phasen.Add((phasenStart, nummer - 1, geoeffnet));
                    phasenStart = nummer;
                    geoeffnet = r;
                }
                if ((art & SenderahmenArt.Letzter) != 0 && phasenStart >= 0)
                {
                    phasen.Add((phasenStart, nummer, geoeffnet));
                    phasenStart = -1;
                }
            }
        }
        if (phasenStart >= 0) phasen.Add((phasenStart, rahmenGesamt - 1, geoeffnet));

        var fehl = 0;
        foreach (var (start, ende, _) in phasen)
        {
            var beruehrt = false;
            foreach (var a in aeusserungen)
            {
                if (start * Rahmen < a.Ende && (ende + 1) * Rahmen > a.Start)
                {
                    beruehrt = true;
                    break;
                }
            }
            if (!beruehrt) fehl++;
        }

        var abgeschnitten = 0;
        double verzugSumme = 0;
        var verzugAnzahl = 0;
        foreach (var a in aeusserungen)
        {
            var ersterRahmen = -1;
            for (var r = Math.Max(0, a.Start / Rahmen); r < rahmenGesamt && r * Rahmen < a.Ende; r++)
            {
                if (gesendet[r])
                {
                    ersterRahmen = r;
                    break;
                }
            }
            if (ersterRahmen < 0 || ersterRahmen * Rahmen - a.Start > ToleranzSamples)
            {
                abgeschnitten++;
                continue;
            }
            foreach (var (start, ende, offen) in phasen)
            {
                if (ersterRahmen < start || ersterRahmen > ende) continue;
                // Verzug: vom Anlaut bis zum Ende des Rahmens, in dem das Gate öffnete (0, wenn es schon offen war).
                verzugSumme += Math.Max(0, (offen + 1) * Rahmen - a.Start) / (double)Klangformat.SamplesJeMs;
                verzugAnzahl++;
                break;
            }
        }

        var minuten = signal.Length / (double)Klangformat.Abtastrate / 60.0;
        var gesendetAnteil = rahmenGesamt == 0 ? 0 : gesendet.Count(g => g) / (double)rahmenGesamt;
        return new GateMessung(
            minuten,
            phasen.Count,
            fehl,
            minuten > 0 ? fehl / minuten : 0,
            aeusserungen.Count,
            abgeschnitten,
            aeusserungen.Count == 0 ? 0 : abgeschnitten / (double)aeusserungen.Count,
            verzugAnzahl == 0 ? 0 : verzugSumme / verzugAnzahl,
            gate.AbgewieseneAnlaeufe,
            gesendetAnteil);
    }
}
