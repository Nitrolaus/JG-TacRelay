// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;

namespace Funk.Klang.Geraete;

/// <summary>Format eines geöffneten Stroms: float32, verschränkt, Abtastrate und Kanalzahl des Geräts.</summary>
public readonly record struct StromFormat(int Abtastrate, int Kanaele);

/// <summary>Aufnahme- oder Loopback-Daten im Audio-Thread des Geräts: verschränkte Werte, nur während des Aufrufs gültig.</summary>
public delegate void AufnahmeRueckruf(ReadOnlySpan<float> werte);

/// <summary>Wiedergabe im Audio-Thread des Geräts: <paramref name="werte"/> vollständig füllen (verschränkt).</summary>
public delegate void WiedergabeRueckruf(Span<float> werte);

/// <summary>Ein geöffneter Strom. Dispose hält ihn an und blockiert, bis sein Audio-Thread steht.</summary>
public interface IAudioStrom : IDisposable
{
    StromFormat Format { get; }

    /// <summary>Endpunkt-ID des Geräts, auf dem der Strom läuft.</summary>
    string GeraetId { get; }

    /// <summary>Der Strom ist von selbst stehen geblieben (Gerät entfernt, Treiberfehler); aus einem Fremd-Thread.</summary>
    event Action<Exception?>? Abgebrochen;
}

/// <summary>
/// Naht zu den Audiogeräten. Im Produkt WASAPI über NAudio (<see cref="WasapiSystem"/>), in Tests eine Attrappe, die
/// die Rückrufe selbst im Takt aufruft. Alle Methoden laufen im Steuer-Thread der Maschine, nie im Audio-Thread.
/// </summary>
public interface IAudioSystem : IDisposable
{
    IReadOnlyList<AudioGeraet> Eingaenge();

    IReadOnlyList<AudioGeraet> Ausgaenge();

    /// <summary>ID des Standardgeräts für Kommunikation (Aufnahme), oder null, wenn es keins gibt.</summary>
    string? StandardEingang();

    /// <summary>ID des Standardgeräts für Kommunikation (Wiedergabe), oder null, wenn es keins gibt.</summary>
    string? StandardAusgang();

    /// <summary>Mikrofon öffnen und starten: geteilt, ereignisgesteuert, MMCSS "Pro Audio", ohne Ducking.</summary>
    IAudioStrom OeffneAufnahme(string geraetId, AufnahmeRueckruf rueckruf);

    /// <summary>Loopback des Wiedergabegeräts (was der Lautsprecher spielt) als Echoreferenz öffnen und starten.</summary>
    IAudioStrom OeffneLoopback(string ausgangId, AufnahmeRueckruf rueckruf);

    /// <summary>
    /// Wiedergabe öffnen und starten: geteilt, ereignisgesteuert, Kategorie GameChat, ohne Ducking, MMCSS "Pro Audio",
    /// Puffer <paramref name="latenzMs"/>.
    /// </summary>
    IAudioStrom OeffneWiedergabe(string geraetId, int latenzMs, WiedergabeRueckruf rueckruf);

    /// <summary>Geräte kamen oder gingen, ein Standardgerät wechselte; aus einem Fremd-Thread, nur kurz behandeln.</summary>
    event Action? GeraeteGeaendert;
}
