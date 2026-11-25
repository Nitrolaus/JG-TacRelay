// JG TacRelay - Maintainer: Sarevoc
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Server.Lage;

/// <summary>Was eine Netz-ID im Lagezustand bedeutet (6.16, seit 0.6.0).</summary>
public enum Netzart : byte
{
    /// <summary>Weder ein Netz noch ein gültiger Kanal.</summary>
    Unbekannt,

    /// <summary>Netz ohne Ch.-Liste (außer Global), auch ein Netz aus einem Stand vor 0.6.0 mit einer ID ab 0x8000.</summary>
    Gewoehnlich,

    /// <summary>Kanal-ID mit Ch. 1 und höher eines bestehenden Kanalnetzes, nicht Ch. 1 von Global.</summary>
    Kanal,

    /// <summary>Kanal-ID mit Ch. 0 eines bestehenden Kanalnetzes: das Feld trägt das Kanalnetz, hört und sendet nichts.</summary>
    Ruhe,

    /// <summary>Netz-ID eines Kanalnetzes selbst; steht nie in einem Feld.</summary>
    Kanalnetz,

    /// <summary>Netz 1.</summary>
    Global,

    /// <summary>Die berechnete Kanal-ID von Ch. 1 in Global; Ch. 1 ist Netz 1 selbst, die ID gilt nirgends.</summary>
    GlobalSelbst,
}

/// <summary>
/// Kanäle mit Ch. im Lagezustand (6.16): Einordnen einer Netz-ID gegen die Netze, das Netz, dessen Regeln für einen Kanal gelten
/// (<c>offen</c>, <c>nurTaste</c>, Stummschaltung), und die Prüfung eines Kanalnetzes.
/// </summary>
public static class Kanaele
{
    /// <summary>Ordnet <paramref name="id"/> gegen <paramref name="netze"/> ein.</summary>
    public static Netzart Einordnen(IReadOnlyDictionary<ushort, Netz> netze, ushort id)
    {
        ArgumentNullException.ThrowIfNull(netze);
        if (id == Netz.GlobalId) return Netzart.Global;
        if (Kanalrechnung.Zerlegen(id, out var n, out var ch) && netze.TryGetValue(n, out var kanalnetz) && ch < kanalnetz.Kanaele.Length)
        {
            if (n == Netz.GlobalId && ch == Kanalrechnung.GlobalSelbst) return Netzart.GlobalSelbst;
            return ch == Kanalrechnung.Ruhe ? Netzart.Ruhe : Netzart.Kanal;
        }
        if (!netze.TryGetValue(id, out var netz)) return Netzart.Unbekannt;
        return netz.Kanaele.IsEmpty ? Netzart.Gewoehnlich : Netzart.Kanalnetz;
    }

    /// <summary>Ob <paramref name="id"/> in einem Feld stehen darf: ein gewöhnliches Netz, ein Kanal oder eine Ruhe.</summary>
    public static bool AlsFeld(IReadOnlyDictionary<ushort, Netz> netze, ushort id) =>
        Einordnen(netze, id) is Netzart.Gewoehnlich or Netzart.Kanal or Netzart.Ruhe;

    /// <summary>Ob <paramref name="id"/> die Ruhe eines bestehenden Kanalnetzes ist.</summary>
    public static bool IstRuhe(IReadOnlyDictionary<ushort, Netz> netze, ushort id) => Einordnen(netze, id) == Netzart.Ruhe;

    /// <summary>Ob <paramref name="id"/> ein Kanalnetz ist (auch Global mit Ch.).</summary>
    public static bool IstKanalnetz(IReadOnlyDictionary<ushort, Netz> netze, ushort id)
    {
        ArgumentNullException.ThrowIfNull(netze);
        return netze.TryGetValue(id, out var n) && !n.Kanaele.IsEmpty;
    }

    /// <summary>
    /// Das Netz, dessen Regeln für <paramref name="id"/> gelten: bei einem Kanal und einer Ruhe ihr Kanalnetz, sonst das Netz selbst.
    /// </summary>
    public static ushort Regelnetz(IReadOnlyDictionary<ushort, Netz> netze, ushort id) =>
        Einordnen(netze, id) is Netzart.Kanal or Netzart.Ruhe && Kanalrechnung.Zerlegen(id, out var n, out _) ? n : id;

    /// <summary>Ob der Ch. <paramref name="ch"/> im Kanalnetz <paramref name="kanalnetz"/> ein Feld tragen darf (in der Liste, nicht Ch. 1 von Global).</summary>
    public static bool ChGueltig(Netz kanalnetz, byte ch)
    {
        ArgumentNullException.ThrowIfNull(kanalnetz);
        return ch < kanalnetz.Kanaele.Length && !(kanalnetz.Id == Netz.GlobalId && ch == Kanalrechnung.GlobalSelbst);
    }

    /// <summary>Zahl der Kanalnetze.</summary>
    public static int Anzahl(IReadOnlyDictionary<ushort, Netz> netze)
    {
        ArgumentNullException.ThrowIfNull(netze);
        var n = 0;
        foreach (var netz in netze.Values)
            if (!netz.Kanaele.IsEmpty) n++;
        return n;
    }

    /// <summary>
    /// Ch.-Liste, Gruppe und Lage eines Netzes gegen die übrigen (6.16): 2 bis 31 nicht leere Bezeichnungen zu höchstens 20 Zeichen, nur
    /// an Netz-IDs bis 1022, kein anderes Netz im Bereich seiner Kanal-IDs, höchstens 64 Kanalnetze. <c>null</c> heißt stimmig.
    /// </summary>
    public static string? NetzPruefen(Netz netz, IReadOnlyDictionary<ushort, Netz> netze)
    {
        ArgumentNullException.ThrowIfNull(netz);
        ArgumentNullException.ThrowIfNull(netze);
        if (netz.Gruppe is { } g && (string.IsNullOrWhiteSpace(g) || Namen.Skalarwerte(g) > Kanalrechnung.GruppeMax)) return $"Kanal {netz.Id}: Gruppe leer oder über 20 Zeichen";
        if (netz.Kanaele.IsEmpty) return null;
        if (netz.Kanaele.Length is < Kanalrechnung.EintraegeMin or > Kanalrechnung.EintraegeMax) return $"Kanalnetz {netz.Id}: {netz.Kanaele.Length} Einträge";
        if (netz.Id > Kanalrechnung.KanalnetzIdMax) return $"Kanalnetz {netz.Id} über 1022";
        foreach (var name in netz.Kanaele)
            if (string.IsNullOrWhiteSpace(name) || Namen.Skalarwerte(name) > Kanalrechnung.ChNameMax) return $"Kanalnetz {netz.Id}: Bezeichnung leer oder über 20 Zeichen";
        var erste = Kanalrechnung.Id(netz.Id, 0);
        foreach (var id in netze.Keys)
            if (id >= erste && id < erste + Kanalrechnung.ChJeNetz) return $"Kanalnetz {netz.Id}: Kanal {id} liegt im Bereich seiner Kanal-IDs";
        var anzahl = Anzahl(netze) + (netze.TryGetValue(netz.Id, out var alt) && !alt.Kanaele.IsEmpty ? 0 : 1);
        return anzahl > Kanalrechnung.KanalnetzeMax ? "mehr als 64 Kanalnetze" : null;
    }
}
