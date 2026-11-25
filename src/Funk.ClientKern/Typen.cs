// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.ClientKern;

/// <summary>Antwort auf eine eigene Anfrage: derselbe Typ mit Body oder <c>Fehler</c> (4.2).</summary>
public sealed record Antwort(Nachrichtentyp Typ, object? Body, Fehler? Fehler)
{
    public bool Ok => Fehler is null;

    public Fehlercode? Code => Fehler?.Code;

    /// <summary>Body als <typeparamref name="T"/>; wirft bei Fehler oder anderem Typ.</summary>
    public T Als<T>() where T : class =>
        Body as T ?? throw new InvalidOperationException(Fehler is { } f ? "Fehler " + Fehlercodes.Drahtname(f.Code) : "Body ist kein " + typeof(T).Name);
}

/// <summary>Die Sitzung endete, bevor die Antwort kam.</summary>
public sealed class VerbindungGetrenntException : Exception
{
    public VerbindungGetrenntException() : base("Verbindung getrennt")
    {
    }

    public VerbindungGetrenntException(string meldung) : base(meldung)
    {
    }

    public VerbindungGetrenntException(string meldung, Exception innen) : base(meldung, innen)
    {
    }
}

/// <summary>Ziel eines Sprachstroms: Zielart NETZ (1-16 Netze) oder DURCHSAGE (0 = an alle, sonst 1-64 Netze, ohne Global).</summary>
public sealed record Sendeziel
{
    private Sendeziel(Zielart art, ImmutableArray<ushort> netze)
    {
        Art = art;
        Netze = netze;
    }

    public Zielart Art { get; }

    /// <summary>Streng aufsteigend (5.4).</summary>
    public ImmutableArray<ushort> Netze { get; }

    /// <summary>DURCHSAGE an alle (n = 0).</summary>
    public static Sendeziel DurchsageAnAlle { get; } = new(Zielart.Durchsage, []);

    /// <summary>NETZ an die Netze; sortiert, Doppelte entfallen.</summary>
    public static Sendeziel Netz(params ReadOnlySpan<ushort> netze)
    {
        var n = Ordnen(netze);
        if (n.Length is 0 or > Sprachkanal.NetzZieleMax) throw new ArgumentException("NETZ braucht 1 bis 16 Ziele.", nameof(netze));
        return new Sendeziel(Zielart.Netz, n);
    }

    /// <summary>DURCHSAGE an ausgewählte Netze (ohne Global); leer heißt an alle.</summary>
    public static Sendeziel Durchsage(params ReadOnlySpan<ushort> netze)
    {
        var n = Ordnen(netze);
        if (n.Length > Sprachkanal.DurchsageZieleMax) throw new ArgumentException("DURCHSAGE hat höchstens 64 Ziele.", nameof(netze));
        if (n.Contains(Sprachkanal.GlobalNetz)) throw new ArgumentException("Global ist kein Durchsageziel (5.4).", nameof(netze));
        return new Sendeziel(Zielart.Durchsage, n);
    }

    public bool Equals(Sendeziel? other) => other is not null && Art == other.Art && Netze.AsSpan().SequenceEqual(other.Netze.AsSpan());

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Art);
        foreach (var n in Netze) h.Add(n);
        return h.ToHashCode();
    }

    public override string ToString() => Art + " [" + string.Join(", ", Netze) + "]";

    private static ImmutableArray<ushort> Ordnen(ReadOnlySpan<ushort> netze)
    {
        var b = ImmutableArray.CreateBuilder<ushort>(netze.Length);
        foreach (var n in netze)
        {
            if (n is 0 or ushort.MaxValue) throw new ArgumentException("Netz-ID 1 bis 65534.", nameof(netze));
            if (!b.Contains(n)) b.Add(n);
        }
        b.Sort();
        return b.ToImmutable();
    }
}

/// <summary>Einmalcode für die Registrierung (3.4): anzeigen, der Nutzer gibt ihn in Discord ein.</summary>
public sealed record Registrierungscode(string Code, long GueltigBis, string Befehl)
{
    /// <summary>Der Code steht nie in <c>ToString</c> und damit in keinem Log (P-46).</summary>
    public override string ToString() => "Registrierungscode(***, gültig bis " + GueltigBis + ")";
}

/// <summary>Ein Push des Servers, der nicht schon im <see cref="Lagebild"/> aufgeht (SendeFehler, Durchsage, Meldung ...).</summary>
public sealed record PushEreignis(Nachrichtentyp Typ, object Body);

/// <summary>Zustandswechsel des Clients nach 4.6.</summary>
public sealed record Zustandswechsel(ClientZustand Von, ClientZustand Nach, string? Grund);

/// <summary>C4: Das Sendenetz hat sich durch eine Umstellung geändert ("Sendenetz geändert durch Name: jetzt Netz").</summary>
public sealed record SendenetzGeaendert(Sendeziel Vorher, Sendeziel? Jetzt, uint? Durch);

/// <summary>
/// Ende einer Sitzung, für Anzeige und Wiederverbindung. Scheiterte schon der Aufbau, sagen <paramref name="Aufbau"/> und
/// <paramref name="Einzelheit"/> woran (Adresse, Port, Meldung des Betriebssystems), damit die Oberfläche es lesbar machen kann.
/// <paramref name="Wiederverbindung"/>: Der Aufbau scheiterte beim Wiederverbinden (7.1), nicht bei der Erstverbindung. Das
/// unterscheidet den Pin-Alarm: bei der Erstverbindung Abbruch (4.1), beim Wiederverbinden versucht die Schleife weiter.
/// </summary>
public sealed record Sitzungsende(string Grund, ByeGrund? Bye, uint? WiederInMs, Fehlercode? Fehler, long? AktivDauerMs, ByeDurch? Durch,
    AufbauFehler? Aufbau = null, string? Einzelheit = null, bool Wiederverbindung = false);
