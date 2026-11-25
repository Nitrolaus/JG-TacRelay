// JG TacRelay - Maintainer: Sarevoc
namespace Funk.ClientKern.Echo;

/// <summary>
/// Einstellungen des <see cref="EchoFunker"/>. Die Voreinstellung ist der Probebetrieb: Netz "Echo", 500 ms Pause nach
/// dem Ende, höchstens 30 s je Phase. Die Ratenbremse liegt knapp unter den Grenzen des Servers je Sitzung (6.3: 60
/// Pakete/s mit Stoß 20, Opus 16000 B/s mit Stoß 4000); höhere Werte lehnt <see cref="Pruefen"/> ab.
/// </summary>
public sealed record EchoOptionen
{
    /// <summary>Name des Echo-Netzes in der Probe-Konfiguration.</summary>
    public const string StandardNetz = "Echo";

    /// <summary>Obergrenze einer aufgenommenen Phase laut Auftrag.</summary>
    public static readonly TimeSpan MaxPhaseGrenze = TimeSpan.FromSeconds(30);

    /// <summary>
    /// So lange wartet funk-echo nach <c>Bye{VERDRAENGT}</c> oder einer Abweisung, bevor er endet und systemd ihn neu startet.
    /// Ohne Pause gäbe es Ping-Pong mit einem anderen Gerät desselben Kontos: Jeder Neustart verdrängte es wieder. Ein Mensch
    /// verbindet nach VERDRAENGT nicht von selbst neu (4.6), höchstens alle 10 Minuten wechselt das Konto also den Platz.
    /// </summary>
    public static readonly TimeSpan RuheNachAbweisung = TimeSpan.FromMinutes(10);

    /// <summary>Grenzen des Servers je Sitzung (6.3); die Bremse bleibt darunter.</summary>
    public const double ServerPaketeJeSekunde = 60;
    public const double ServerPaketeStoss = 20;
    public const double ServerOpusBytesJeSekunde = 16_000;
    public const double ServerOpusBytesStoss = 4_000;

    /// <summary>Netz, auf dem der Funker mithört und zurücksendet: Name (Groß- und Kleinschreibung egal) oder Netz-ID.</summary>
    public string Netz { get; init; } = StandardNetz;

    /// <summary>Pause zwischen dem erkannten Ende einer Phase und dem Beginn ihrer Wiedergabe.</summary>
    public TimeSpan Pause { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Höchstdauer einer aufgenommenen Phase; Frames darüber hinaus werden nicht aufgenommen.</summary>
    public TimeSpan MaxPhase { get; init; } = MaxPhaseGrenze;

    /// <summary>Ohne ENDE gilt eine Phase nach so langer Ruhe als beendet (C6: 300 ms).</summary>
    public TimeSpan Leerlauf { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Nach einer Wiedergabe nimmt der Funker vom selben Sprecher so lange keine neue Phase an. Über Lautsprecher kann das
    /// Mikrofon die Wiedergabe wieder aufnehmen (Jitterpuffer, Ausgabe und Nachlauf des Gates liegen darunter); so
    /// schaukelt sich keine Rückkopplung auf, die der Inhaltsvergleich nicht erkennt (neu kodiert, andere Bytes).
    /// </summary>
    public TimeSpan Nachlauf { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>So lange merkt sich der Funker, was er selbst gesendet hat (Schleifenschutz).</summary>
    public TimeSpan Gedaechtnis { get; init; } = TimeSpan.FromMinutes(2);

    public double PaketeJeSekunde { get; init; } = 55;
    public double PaketeStoss { get; init; } = 18;
    public double OpusBytesJeSekunde { get; init; } = 15_000;
    public double OpusBytesStoss { get; init; } = 3_600;

    /// <summary>Höchstzahl der Frames je Phase (20 ms je Frame).</summary>
    public int MaxFrames => (int)Math.Max(1, MaxPhase.TotalMilliseconds / 20);

    /// <summary>Wirft <see cref="ArgumentOutOfRangeException"/>, wenn ein Wert außerhalb seines Bereichs liegt.</summary>
    public void Pruefen()
    {
        if (string.IsNullOrWhiteSpace(Netz)) throw new ArgumentOutOfRangeException(nameof(Netz), "Kanal fehlt.");
        Bereich(Pause, TimeSpan.Zero, TimeSpan.FromSeconds(10), nameof(Pause));
        Bereich(MaxPhase, TimeSpan.FromMilliseconds(20), MaxPhaseGrenze, nameof(MaxPhase));
        Bereich(Leerlauf, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5), nameof(Leerlauf));
        Bereich(Nachlauf, TimeSpan.Zero, TimeSpan.FromSeconds(10), nameof(Nachlauf));
        Bereich(Gedaechtnis, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1), nameof(Gedaechtnis));
        Rate(PaketeJeSekunde, ServerPaketeJeSekunde, nameof(PaketeJeSekunde));
        // Ein Pre-roll-Stoß (7) plus die drei ENDE-Kopien muss durchgehen.
        if (PaketeStoss is < 10 or > ServerPaketeStoss) throw new ArgumentOutOfRangeException(nameof(PaketeStoss), PaketeStoss, "10 bis 20 (6.3).");
        Rate(OpusBytesJeSekunde, ServerOpusBytesJeSekunde, nameof(OpusBytesJeSekunde));
        // Mindestens ein Frame der Höchstlänge (400 Byte, 5.4) muss in den Stoß passen.
        if (OpusBytesStoss is < 400 or > ServerOpusBytesStoss) throw new ArgumentOutOfRangeException(nameof(OpusBytesStoss), OpusBytesStoss, "400 bis 4000 (6.3).");
    }

    private static void Bereich(TimeSpan wert, TimeSpan min, TimeSpan max, string name)
    {
        if (wert < min || wert > max) throw new ArgumentOutOfRangeException(name, wert, $"{min} bis {max}.");
    }

    private static void Rate(double wert, double server, string name)
    {
        if (!(wert > 0 && wert <= server)) throw new ArgumentOutOfRangeException(name, wert, "größer 0 und höchstens die Servergrenze (6.3).");
    }
}

/// <summary>Warum der Echo-Funker aufhört (Verdrängt, abgewiesen) und mit welchem Code funk-echo endet.</summary>
public sealed record EchoAbschied(int Code, string Text)
{
    /// <summary>Exitcode nach <c>Bye{VERDRAENGT}</c>.</summary>
    public const int CodeVerdraengt = 3;

    /// <summary>Exitcode, wenn der Server den Funker abweist (Sperre, Fassung, Pin).</summary>
    public const int CodeAbgewiesen = 4;
}
