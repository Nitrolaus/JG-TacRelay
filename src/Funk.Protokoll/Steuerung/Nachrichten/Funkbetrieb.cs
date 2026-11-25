// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

// Bodies 0x0030-0x0037 aus 4.5: Sendeauswahl, Hören, Rolle und Schiff, Verschieben, Stummschalten, Felder.

/// <summary>0x0030 SendeauswahlSetzen. Antwort <c>{}</c>.</summary>
public sealed record SendeauswahlSetzenAnfrage : ISteuerBody
{
    public required IReadOnlyList<byte> Felder { get; init; }
    public required bool Global { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Liste(Felder, Grenzen.FelderMax, "felder");
        p.Aufsteigend(Felder, "felder");
        foreach (var f in Felder) p.Bereich(f, 0, Grenzen.FeldMax, "felder");
    }
}

/// <summary>0x0031 HoerenSetzen. Antwort <see cref="ZustandsVersionBody"/>.</summary>
public sealed record HoerenSetzenAnfrage : ISteuerBody
{
    public required ushort NetzId { get; init; }
    public required bool Hoeren { get; init; }

    public void Pruefen(ref Pruefung p) => p.Bereich(NetzId, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "netzId");
}

/// <summary>0x0032 EinsteigenRolleSchiff. Antwort <see cref="ZustandsVersionBody"/>.</summary>
public sealed record EinsteigenRolleSchiffAnfrage : ISteuerBody
{
    public uint? KontoId { get; init; }
    public required ushort RolleId { get; init; }
    public ushort? SchiffId { get; init; }
    public bool? Erzwingen { get; init; }
    public long? BasisVersion { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(RolleId, 1, Grenzen.NetzIdMax, "rolleId");
        p.Bereich(BasisVersion, 0, JsonVorpruefung.MaxGanzzahl, "basisVersion");
    }
}

/// <summary>Feld mit Netz, hören und senden (Verschieben, SatzAnwenden, Aufstellung).</summary>
public sealed record FeldBelegung : ISteuerBody
{
    public required byte Feld { get; init; }
    public required ushort NetzId { get; init; }
    public required bool Hoeren { get; init; }
    public required bool Senden { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Feld, 0, Grenzen.FeldMax, "felder.feld");
        p.Bereich(NetzId, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "felder.netzId");
    }
}

/// <summary>
/// Eintrag in <c>felderPatch</c>: <c>{feld?, netzId, hoeren?, senden?, fest?}</c> oder <c>{feld, leeren: true}</c>.
/// Fehlendes <c>hoeren</c> heißt true, fehlendes <c>senden</c> false, fehlendes <c>fest</c> true.
/// </summary>
public sealed record FeldPatch : ISteuerBody
{
    public byte? Feld { get; init; }
    public ushort? NetzId { get; init; }
    public bool? Hoeren { get; init; }
    public bool? Senden { get; init; }
    public bool? Fest { get; init; }
    public bool? Leeren { get; init; }

    /// <summary>Wirksamer Wert für hören.</summary>
    public bool HoerenWirksam => Hoeren ?? true;

    /// <summary>Wirksamer Wert für senden.</summary>
    public bool SendenWirksam => Senden ?? false;

    /// <summary>Wirksamer Wert für fest.</summary>
    public bool FestWirksam => Fest ?? true;

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Feld, 0, Grenzen.FeldMax, "felderPatch.feld");
        if (Leeren == true)
        {
            p.Pflicht(Feld is not null, "felderPatch.feld");
            p.Regel(NetzId is null, "felderPatch.netzId");
        }
        else
        {
            p.Pflicht(NetzId is not null, "felderPatch.netzId");
            p.Bereich(NetzId, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "felderPatch.netzId");
        }
    }
}

/// <summary>Eine Änderung in <c>Verschieben.aenderungen</c>.</summary>
public sealed record Verschiebung : ISteuerBody
{
    public required uint KontoId { get; init; }
    public ushort? RolleId { get; init; }
    public ushort? SchiffId { get; init; }
    public IReadOnlyList<FeldBelegung>? Felder { get; init; }
    public IReadOnlyList<FeldPatch>? FelderPatch { get; init; }
    public IReadOnlyList<byte>? SendeFelder { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(RolleId, 0, Grenzen.NetzIdMax, "aenderungen.rolleId");
        p.Objekte(Felder, Grenzen.FelderMax, "aenderungen.felder");
        p.Objekte(FelderPatch, Grenzen.FelderMax, "aenderungen.felderPatch");
        SendeFelderPruefen(SendeFelder, ref p, "aenderungen.sendeFelder");
    }

    internal static void SendeFelderPruefen(IReadOnlyList<byte>? felder, ref Pruefung p, string name)
    {
        p.Liste(felder, Grenzen.FelderMax, name);
        p.Aufsteigend(felder, name);
        if (felder is not null) foreach (var f in felder) p.Bereich(f, 0, Grenzen.FeldMax, name);
    }
}

/// <summary>0x0033 Verschieben, Anfrage.</summary>
public sealed record VerschiebenAnfrage : ISteuerBody
{
    public long? BasisVersion { get; init; }
    public required IReadOnlyList<Verschiebung> Aenderungen { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(BasisVersion, 0, JsonVorpruefung.MaxGanzzahl, "basisVersion");
        p.Objekte(Aenderungen, Grenzen.AenderungenMax, "aenderungen");
        p.Regel(Aenderungen.Count >= 1, "aenderungen");
    }
}

/// <summary>0x0033 Verschieben, Antwort.</summary>
public sealed record VerschiebenAntwort : ISteuerBody
{
    public required long ZustandsVersion { get; init; }
    public required IReadOnlyList<uint> Offline { get; init; }
    public required IReadOnlyList<uint> Verdraengt { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Liste(Offline, Grenzen.AenderungenMax, "offline");
        p.Liste(Verdraengt, Grenzen.KontenMax, "verdraengt");
    }
}

/// <summary>0x0034 Stummschalten. Antwort <c>{}</c>.</summary>
public sealed record StummschaltenAnfrage : ISteuerBody
{
    public required uint KontoId { get; init; }
    public required ushort NetzId { get; init; }
    public required ushort Sekunden { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(NetzId, 0, Grenzen.NetzIdMax, "netzId");
        p.Bereich(Sekunden, 0, Grenzen.StummSekundenMax, "sekunden");
    }
}

/// <summary>0x0035 FeldSetzen. Fehlendes <c>netzId</c> leert das Feld. Antwort <see cref="ZustandsVersionBody"/>.</summary>
public sealed record FeldSetzenAnfrage : ISteuerBody
{
    public required byte Feld { get; init; }
    public ushort? NetzId { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Feld, 0, Grenzen.FeldMax, "feld");
        p.Bereich(NetzId, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "netzId");
    }
}

/// <summary>
/// 0x0039 RolleWaehlen (C->S, selbst, 6.16, seit 0.6.0): legt die Felder einer Rolle an, ohne den Platz zu ändern. <c>ch</c> sind
/// Wünsche des Clients für den Ch. einzelner Rollenfelder (Stufe 4 der Ch.-Auflösung). Antwort <see cref="SatzAnwendenAntwort"/>.
/// </summary>
public sealed record RolleWaehlenAnfrage : ISteuerBody
{
    public required ushort RolleId { get; init; }
    public IReadOnlyList<ChWunsch>? Ch { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(RolleId, 1, Grenzen.NetzIdMax, "rolleId");
        p.Liste(Ch, Kanalrechnung.WuenscheMax, "ch");
        if (Ch is null || !p.Ok) return;
        Span<bool> gesehen = stackalloc bool[Grenzen.FeldMax + 1];
        foreach (var w in Ch)
        {
            p.Bereich(w.Feld, 0, Grenzen.FeldMax, "ch.feld");
            p.Bereich(w.Ch, 0, Kanalrechnung.ChMax, "ch.ch");
            if (!p.Ok) return;
            p.Regel(!gesehen[w.Feld], "ch.feld");
            gesehen[w.Feld] = true;
        }
    }
}

/// <summary>Ein Wunsch in <c>RolleWaehlen.ch</c>: Ch. für das Rollenfeld <c>feld</c>.</summary>
public sealed record ChWunsch
{
    public required byte Feld { get; init; }
    public required byte Ch { get; init; }
}

/// <summary>
/// 0x003A KanalSperre (C->S, <c>ZUWEISEN</c>, 6.17, seit 0.6.0): einen Kanal, ein ganzes Kanalnetz oder ein gewöhnliches Netz für ein
/// Konto oder für alle ohne <c>ZUWEISEN</c> (<c>kontoId</c> 0) sperren, aufheben oder die laufenden Sperren lesen. <c>bis</c> fehlt: das
/// nächste 04:00 Europe/Berlin, höchstens 24 h. Antwort <see cref="KanalSperreAntwort"/>.
/// </summary>
public sealed record KanalSperreAnfrage : ISteuerBody
{
    public required KanalSperreOperation Op { get; init; }
    public uint? KontoId { get; init; }
    public ushort? NetzId { get; init; }
    public long? Bis { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(NetzId, Grenzen.NetzIdMin, Grenzen.NetzIdMax, "netzId");
        p.Bereich(Bis, 0, JsonVorpruefung.MaxGanzzahl, "bis");
        if (Op == KanalSperreOperation.Liste) return;
        p.Pflicht(KontoId is not null, "kontoId");
        p.Pflicht(NetzId is not null, "netzId");
        p.Regel(Op == KanalSperreOperation.Setzen || Bis is null, "bis");
    }
}

/// <summary>Antwort auf 0x003A: alle laufenden Kanalsperren (<c>kontoId</c> 0 = für alle), höchstens 500.</summary>
public sealed record KanalSperreAntwort : ISteuerBody
{
    public required IReadOnlyList<Kanalsperre> Sperren { get; init; }

    public void Pruefen(ref Pruefung p) => p.Liste(Sperren, Kanalrechnung.SperrenMax, "sperren");
}

/// <summary>
/// Eine Kanalsperre (6.17): <c>netzId</c> ist eine Kanal-ID (ein Ch.), ein Kanalnetz (alle Ch.) oder ein gewöhnliches Netz. In
/// <c>KanalSperre</c>-Antworten nennt <c>kontoId</c> das Konto (0 = für alle); in <c>Mitgliedschaften.gesperrt</c> fehlt es, dort heißt
/// <c>alle</c>, dass die Sperre für alle ohne <c>ZUWEISEN</c> gilt.
/// </summary>
public sealed record Kanalsperre
{
    public required ushort NetzId { get; init; }
    public required long Bis { get; init; }
    public required uint Durch { get; init; }
    public uint? KontoId { get; init; }
    public bool? Alle { get; init; }
}

/// <summary>0x0036 SatzAnwenden, Anfrage.</summary>
public sealed record SatzAnwendenAnfrage : ISteuerBody
{
    public ushort? RolleId { get; init; }
    public ushort? SchiffId { get; init; }
    public required IReadOnlyList<FeldBelegung> Felder { get; init; }
    public Sendeauswahl? Sendeauswahl { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Objekte(Felder, Grenzen.FelderMax, "felder");
        p.Objekt(Sendeauswahl);
    }
}

/// <summary>Übersprungenes Feld in <c>SatzAnwenden</c>.</summary>
public sealed record UebersprungenesFeld
{
    public required byte Feld { get; init; }
    public required string Code { get; init; }
}

/// <summary>0x0036 SatzAnwenden, Antwort.</summary>
public sealed record SatzAnwendenAntwort : ISteuerBody
{
    public required long ZustandsVersion { get; init; }
    public required IReadOnlyList<UebersprungenesFeld> Uebersprungen { get; init; }

    public void Pruefen(ref Pruefung p) => p.Liste(Uebersprungen, Grenzen.FelderMax, "uebersprungen");
}

/// <summary>0x0037 PlaetzeFreigeben, Antwort (Anfrage <c>{}</c>).</summary>
public sealed record PlaetzeFreigebenAntwort : ISteuerBody
{
    public required long ZustandsVersion { get; init; }
    public required ushort Anzahl { get; init; }

    public void Pruefen(ref Pruefung p) { }
}
