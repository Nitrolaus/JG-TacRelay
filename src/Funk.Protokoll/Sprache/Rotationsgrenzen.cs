// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Sprache;

/// <summary>
/// Grenzen und Fristen für Epochen und Rotation (5.8). Die Voreinstellung ist die Spezifikation;
/// kleinere Werte sind für Tests und Dauerläufe gedacht (P-32: Rotation alle 2 min).
/// </summary>
public sealed record Rotationsgrenzen
{
    public static Rotationsgrenzen Standard { get; } = new();

    /// <summary>Server rotiert 60 min nach Epochenbeginn.</summary>
    public TimeSpan EpochenDauer { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>Server rotiert ab 2^20 Paketen in einer Richtung und Epoche.</summary>
    public long PaketeBisRotation { get; init; } = 1L << 20;

    /// <summary>Client fordert ab 2^21 gesendeten Paketen hoch per KeyRotation{ZAEHLER} an.</summary>
    public long PaketeBisAnforderung { get; init; } = 1L << 21;

    /// <summary>Harte Grenze 2^22 Pakete je Richtung und Epoche; darüber sendet der Sender in der Epoche nichts mehr.</summary>
    public long HarteGrenze { get; init; } = 1L << 22;

    /// <summary>Mehr gescheiterte Entschlüsselungen je Epoche erzwingen eine Rotation (Angriffsverdacht).</summary>
    public long FehlschlaegeBisVerdacht { get; init; } = 1L << 16;

    /// <summary>Frist für die Antwort auf VoiceKeys (START und ROTATION).</summary>
    public TimeSpan AntwortFrist { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Server: So lange nach der Antwort nimmt er die vorige Epoche noch an (Ablauf Schritt 3).</summary>
    public TimeSpan VorigeNachUmschalten { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Client: vorige Epoche bis 10 s nach dem ersten runter-Paket der neuen (Ablauf Schritt 4) ...</summary>
    public TimeSpan VorigeNachErstemPaket { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>... spätestens aber 20 s nach seiner Antwort.</summary>
    public TimeSpan VorigeSpaetestens { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Hält die harte Grenze länger an, beginnt der Client eine neue Sitzung.</summary>
    public TimeSpan SperreBisNeueSitzung { get; init; } = TimeSpan.FromSeconds(5);
}
