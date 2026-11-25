// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>Stufen der Sendekette, deren Laufzeit je 10-ms-Rahmen gemessen wird (Klangstufe A).</summary>
public enum KlangStufe
{
    /// <summary>Echoreferenz: Sprachschätzung auf dem Loopback und Übergabe an den Wiedergabezweig des APM.</summary>
    Referenz,

    /// <summary>APM im Aufnahmezweig: AEC3 und Hochpass; in der alten Kette (bis 0.6.0 Stufe 2c) auch AGC2.</summary>
    Echo,

    /// <summary>RNNoise: Rauschunterdrückung und Sprachwahrscheinlichkeit.</summary>
    Rauschfilter,

    /// <summary>Pegelstufe: Halteregel oder feste Verstärkung.</summary>
    Pegelstufe,

    /// <summary>Sicherheitsbegrenzer hinter der Pegelstufe.</summary>
    Begrenzer,

    /// <summary>Führung und Sprach-Gate (Sprachaktivierung, Sprechtaste) samt Paketbildung, ohne das Kodieren.</summary>
    Gate,

    /// <summary>Opus-Kodierer; nur Rahmen, in denen kodiert wurde.</summary>
    Opus,

    /// <summary>Der ganze Rahmen in der Sendekette.</summary>
    Gesamt,
}

/// <summary>Laufzeit einer Stufe über alle bisher gemessenen Rahmen in Mikrosekunden (Quantile auf etwa 4,4 % genau).</summary>
public readonly record struct StufenLaufzeit(KlangStufe Stufe, long Anzahl, double P50Us, double P95Us, double P99Us, double MaxUs);

/// <summary>Laufzeit je Stufe in einem Rahmen in Mikrosekunden; NaN heißt: Die Stufe lief in diesem Rahmen nicht.</summary>
public readonly record struct Stufenzeiten(
    float ReferenzUs, float EchoUs, float RauschfilterUs, float PegelstufeUs, float BegrenzerUs, float GateUs, float OpusUs, float GesamtUs)
{
    public float Zeit(KlangStufe stufe) => stufe switch
    {
        KlangStufe.Referenz => ReferenzUs,
        KlangStufe.Echo => EchoUs,
        KlangStufe.Rauschfilter => RauschfilterUs,
        KlangStufe.Pegelstufe => PegelstufeUs,
        KlangStufe.Begrenzer => BegrenzerUs,
        KlangStufe.Gate => GateUs,
        KlangStufe.Opus => OpusUs,
        _ => GesamtUs,
    };
}

/// <summary>
/// Ein Aufnahmerahmen (10 ms) aus Sicht der Sendekette. Pegel in dBFS über den mittleren quadratischen Wert des Rahmens
/// (<see cref="Pegel"/>), Wirkungen in dB.
/// </summary>
/// <param name="Rahmen">Laufende Nummer des Rahmens in der Kette (0 = erster).</param>
/// <param name="ZeitMs">Uhr der Maschine in Millisekunden.</param>
/// <param name="EingangDbfs">Mikrofon vor dem APM.</param>
/// <param name="EchoDbfs">Hinter dem APM (AEC3, Hochpass; in der alten Kette mit AGC2).</param>
/// <param name="VerstaerktDbfs">Hinter Pegelstufe und Begrenzer, vor RNNoise (ohne Pegelstufe gleich <paramref name="EchoDbfs"/>).</param>
/// <param name="RauschfilterDbfs">Hinter RNNoise (bei abgeschalteter Rauschunterdrückung gleich <paramref name="VerstaerktDbfs"/>).</param>
/// <param name="RauschfilterDb">
/// Wirkung von RNNoise: Pegel hinter RNNoise gegen den Pegel desselben Signals davor (RNNoise verzögert um zwei Rahmen); 0 ohne
/// Rauschunterdrückung.
/// </param>
/// <param name="PegelstufeDb">Verstärkung der Pegelstufe am Ende des Rahmens; 0 ohne Pegelstufe.</param>
/// <param name="BegrenzerDb">Stärkste Absenkung des Begrenzers im Rahmen (0 oder negativ).</param>
/// <param name="GatePegelDbfs">Pegel, den das Gate bekommt; dieselbe Skala wie die Pegelanzeige und die Schwellen.</param>
/// <param name="Sprachwahrscheinlichkeit">Sprachwahrscheinlichkeit von RNNoise für das Signal, das ins Gate geht.</param>
/// <param name="Gate">Zustand des Gates nach dem Rahmen.</param>
/// <param name="Sendet">Nach dem Rahmen offen (Senden, Nachlauf) oder Sprechtaste gedrückt.</param>
/// <param name="Zeiten">Laufzeit je Stufe.</param>
public readonly record struct TelemetrieEintrag(
    long Rahmen, long ZeitMs, float EingangDbfs, float EchoDbfs, float VerstaerktDbfs, float RauschfilterDbfs, float RauschfilterDb, float PegelstufeDb,
    float BegrenzerDb, float GatePegelDbfs, float Sprachwahrscheinlichkeit, GateZustand Gate, bool Sendet, Stufenzeiten Zeiten);

/// <summary>
/// Telemetrie der Sendekette als Momentaufnahme: Laufzeit je Stufe (p50, p95, p99, Maximum), verpasste Fristen und der letzte
/// Rahmen. <see cref="DeadlineVerpasst"/> zählt Rahmen, deren ganze Kette länger als <see cref="BudgetUs"/> (10 ms, die Dauer
/// eines Rahmens) brauchte.
/// </summary>
public sealed record TelemetrieStand(long Rahmen, long DeadlineVerpasst, double BudgetUs, IReadOnlyList<StufenLaufzeit> Stufen, TelemetrieEintrag Letzter)
{
    /// <summary>
    /// Mikrofon (Kennung), für das die Halteregel gerade lernt; null, solange keins offen war. Der Client merkt es mit der gelernten
    /// Verstärkung (<see cref="Verarbeitung.LautstaerkeEinstellung.Eingang"/>).
    /// </summary>
    public string? Eingang { get; init; }

    public StufenLaufzeit Stufe(KlangStufe stufe) => Stufen.FirstOrDefault(s => s.Stufe == stufe) with { Stufe = stufe };
}
