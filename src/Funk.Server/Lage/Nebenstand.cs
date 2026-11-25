// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>Zuletzt angewandte Aufstellung mit ihren offenen Einträgen zum Nachziehen (6.9).</summary>
public sealed record LetzteAufstellung(uint Id, uint Version, long Zeit, uint Anwender, Stufe AnwenderStufe)
{
    /// <summary>Konten ohne Sitzung beim Anwenden; Meldung bei ihrer Anmeldung.</summary>
    public ImmutableSortedSet<uint> OffeneKonten { get; init; } = [];

    /// <summary>Unverknüpfte Discord-IDs mit ihrem Eintrag; greifen bei der Verknüpfung.</summary>
    public ImmutableSortedDictionary<string, AufstellungsEintrag> OffeneDiscordIds { get; init; } =
        ImmutableSortedDictionary.Create<string, AufstellungsEintrag>(StringComparer.Ordinal);
}

/// <summary>Verdrängungen eines Kontos in den letzten 5 min und Beginn einer Sperre (4.6).</summary>
public sealed record Verdraengungsverlauf(ImmutableArray<long> Zeiten, long GesperrtSeit);

/// <summary>
/// Zustand des Schreibers, der nicht in den Schnappschuss gehört, weil die Weiterleitung ihn nie liest:
/// Aufstellungen, Nachziehen und Verdrängungsverlauf. Er wird mit dem Schnappschuss zusammen gespeichert.
/// </summary>
public sealed record Nebenstand
{
    public static Nebenstand Leer { get; } = new();

    public ImmutableSortedDictionary<uint, Aufstellung> Aufstellungen { get; init; } = ImmutableSortedDictionary<uint, Aufstellung>.Empty;
    public uint NaechsteAufstellungsId { get; init; } = 1;
    public LetzteAufstellung? Letzte { get; init; }
    public ImmutableSortedDictionary<uint, Verdraengungsverlauf> Verdraengungen { get; init; } =
        ImmutableSortedDictionary<uint, Verdraengungsverlauf>.Empty;

    /// <summary>
    /// Die laufende Einsatzphase (6.14 Regel 10); <c>null</c> heißt Grundstellung, wie in Ständen von vor 0.5.0. Ein Server 0.4.x
    /// liest das Feld nicht und schreibt es nicht zurück.
    /// </summary>
    public LaufendePhase? Phase { get; init; }

    /// <summary>Nummer der laufenden Phase, 1 = Grundstellung. Eine Methode, damit sie nicht in <c>lage.json</c> steht.</summary>
    public byte LaufendeNr() => Phase?.Nr ?? 1;
}

/// <summary>
/// Dauerhafte Ablage (6.2): wird vor dem Austausch synchron aufgerufen. Wirft sie, bleibt der alte Stand
/// in Kraft und alle Aufträge der Transaktion bekommen INTERN.
/// </summary>
public interface IZustandsSpeicher
{
    void Speichern(Schnappschuss schnappschuss, Nebenstand nebenstand);
}
