// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Klang.Verarbeitung;

/// <summary>
/// Kennzeichen eines Sprachpakets, bitgleich mit FUNKPROTOKOLL-v1 Abschnitt 5.6.
/// </summary>
[Flags]
public enum Sprachkennzeichen
{
    Keine = 0,

    /// <summary>Erstes Paket einer Sprechphase (Hinweis für den Beginn-Klick).</summary>
    Beginn = 0x01,

    /// <summary>Ende-Markierung, ohne Opus.</summary>
    Ende = 0x02,

    /// <summary>Frame stammt aus dem Vorlauf-Puffer (Burst); zählt nicht in die Laufzeitstatistik.</summary>
    Preroll = 0x04,

    /// <summary>1 = Sprachaktivierung, 0 = Sprechtaste.</summary>
    Sprachaktivierung = 0x08,
}

/// <summary>Was eine Klangquelle in einem gelesenen Block erlebt hat.</summary>
[Flags]
public enum QuellenEreignis
{
    Keine = 0,

    /// <summary>Eine Sprechphase ist offen (füllt oder spielt). Steuert die Absenkung bei Durchsagen.</summary>
    Aktiv = 0x01,

    /// <summary>In diesem Block begann die Wiedergabe einer Sprechphase.</summary>
    Begonnen = 0x02,

    /// <summary>In diesem Block endete die Wiedergabe einer Sprechphase.</summary>
    Beendet = 0x04,

    /// <summary>Beginn-Klick fällig (Taste gedrückt).</summary>
    KlickDruck = 0x08,

    /// <summary>Ende-Klick fällig (Taste losgelassen).</summary>
    KlickLoslassen = 0x10,

    /// <summary>Die betroffene Sprechphase kam aus der Sprachaktivierung (Klick sehr leise oder aus).</summary>
    Sprachaktivierung = 0x20,
}

/// <summary>Quelle für den Mischer. <see cref="Lesen"/> läuft im Audio-Thread: keine Allokation, keine Sperre.</summary>
public interface IKlangquelle
{
    /// <summary>Füllt <paramref name="ziel"/> vollständig (Stille als 0) und meldet die Ereignisse dieses Blocks.</summary>
    QuellenEreignis Lesen(Span<float> ziel);
}
