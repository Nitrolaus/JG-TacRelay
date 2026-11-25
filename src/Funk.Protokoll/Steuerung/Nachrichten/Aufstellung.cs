// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

// Bodies 0x0040-0x0047 aus 4.5: Aufstellungen, Raid-Helper, Regeltabelle.

/// <summary>Eintrag einer Aufstellung: je Eintrag <c>kontoId</c> oder <c>discordId</c>.</summary>
public sealed record AufstellungsEintrag : ISteuerBody
{
    public uint? KontoId { get; init; }
    public string? DiscordId { get; init; }
    public required ushort RolleId { get; init; }
    public ushort? SchiffId { get; init; }
    public IReadOnlyList<FeldBelegung>? Felder { get; init; }
    public IReadOnlyList<byte>? SendeFelder { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Regel(KontoId is null != DiscordId is null, "eintraege.kontoId");
        p.Snowflake(DiscordId, "eintraege.discordId");
        p.Objekte(Felder, Grenzen.FelderMax, "eintraege.felder");
        Verschiebung.SendeFelderPruefen(SendeFelder, ref p, "eintraege.sendeFelder");
    }
}

/// <summary>0x0040 AufstellungSpeichern, Anfrage.</summary>
public sealed record AufstellungSpeichernAnfrage : ISteuerBody
{
    public uint? Id { get; init; }
    public uint? Version { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<AufstellungsEintrag> Eintraege { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.AufstellungsNameMax, "name");
        p.Objekte(Eintraege, Grenzen.AufstellungsEintraegeMax, "eintraege");
    }
}

/// <summary><c>{id, version}</c>: Antwort auf AufstellungSpeichern und Anfrage AufstellungLoeschen.</summary>
public sealed record AufstellungsVerweis : ISteuerBody
{
    public required uint Id { get; init; }
    public required uint Version { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>0x0041 AufstellungListe, Anfrage.</summary>
public sealed record AufstellungListeAnfrage : ISteuerBody
{
    public uint? Id { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>Kurzform in der Liste der Aufstellungen.</summary>
public sealed record AufstellungKurz : ISteuerBody
{
    public required uint Id { get; init; }
    public required string Name { get; init; }
    public required uint Version { get; init; }
    public required ushort Anzahl { get; init; }
    public required long Geaendert { get; init; }
    public required uint Durch { get; init; }

    /// <summary>Wer sie angelegt hat (F10): Ändern und Löschen nur er oder ein ADMIN.</summary>
    public uint? Ersteller { get; init; }

    public void Pruefen(ref Pruefung p) => p.PflichtText(Name, Grenzen.AufstellungsNameMax, "aufstellungen.name");
}

/// <summary>
/// 0x0041 AufstellungListe, Antwort: ohne id <c>{aufstellungen}</c>, mit id das vollständige Objekt
/// <c>{id, version, name, eintraege, geaendert?, durch?}</c>.
/// </summary>
public sealed record AufstellungListeAntwort : ISteuerBody
{
    public IReadOnlyList<AufstellungKurz>? Aufstellungen { get; init; }
    public uint? Id { get; init; }
    public uint? Version { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<AufstellungsEintrag>? Eintraege { get; init; }
    public long? Geaendert { get; init; }
    public uint? Durch { get; init; }

    /// <summary>Wer sie angelegt hat (F10).</summary>
    public uint? Ersteller { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        if (Aufstellungen is not null)
        {
            p.Objekte(Aufstellungen, int.MaxValue, "aufstellungen");
            return;
        }
        p.Pflicht(Id is not null && Version is not null && Name is not null && Eintraege is not null, "aufstellung");
        p.Text(Name, Grenzen.AufstellungsNameMax, "name");
        p.Objekte(Eintraege, Grenzen.AufstellungsEintraegeMax, "eintraege");
    }
}

/// <summary>0x0042 AufstellungAnwenden, Anfrage.</summary>
public sealed record AufstellungAnwendenAnfrage : ISteuerBody
{
    public required uint Id { get; init; }
    public uint? Version { get; init; }
    public required AufstellungsModus Modus { get; init; }
    public long? BasisVersion { get; init; }
    public bool? Erzwingen { get; init; }

    public void Pruefen(ref Pruefung p) => p.Bereich(BasisVersion, 0, JsonVorpruefung.MaxGanzzahl, "basisVersion");
}

/// <summary>Übersprungener Eintrag beim Anwenden einer Aufstellung.</summary>
public sealed record UebersprungenerEintrag
{
    public required ushort Eintrag { get; init; }
    public uint? KontoId { get; init; }
    public required string Code { get; init; }
}

/// <summary>0x0042 AufstellungAnwenden, Antwort.</summary>
public sealed record AufstellungAnwendenAntwort : ISteuerBody
{
    public required long ZustandsVersion { get; init; }
    public required uint Version { get; init; }
    public required ushort Angewendet { get; init; }
    public required IReadOnlyList<uint> Offline { get; init; }
    public required IReadOnlyList<string> Unverknuepft { get; init; }
    public required IReadOnlyList<uint> Verdraengt { get; init; }
    public required IReadOnlyList<UebersprungenerEintrag> Uebersprungen { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Liste(Offline, Grenzen.AufstellungsEintraegeMax, "offline");
        p.Liste(Unverknuepft, Grenzen.AufstellungsEintraegeMax, "unverknuepft");
        if (p.Ok) foreach (var d in Unverknuepft) p.Snowflake(d, "unverknuepft");
        p.Liste(Verdraengt, Grenzen.KontenMax, "verdraengt");
        p.Liste(Uebersprungen, Grenzen.AufstellungsEintraegeMax, "uebersprungen");
    }
}

// 0x0043 AufstellungLoeschen: Anfrage AufstellungsVerweis, Antwort {}.

/// <summary>0x0044 RaidHelperImport, Anfrage.</summary>
public sealed record RaidHelperImportAnfrage : ISteuerBody
{
    public required string EventId { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(EventId, 200, "eventId");
        if (p.Ok) p.Regel(RaidHelperEventId.IstGueltig(EventId), "eventId");
    }
}

/// <summary>Formprüfung von <c>eventId</c>: nur Ziffern (höchstens 24) oder ein Discord-Nachrichtenlink.</summary>
public static class RaidHelperEventId
{
    private const string Praefix = "https://discord.com/channels/";

    /// <summary>Ob <paramref name="wert"/> eine der beiden zulässigen Formen hat. Die Gilden-ID prüft der Server.</summary>
    public static bool IstGueltig(string wert)
    {
        ArgumentNullException.ThrowIfNull(wert);
        if (NurZiffern(wert, 24)) return true;
        if (!wert.StartsWith(Praefix, StringComparison.Ordinal)) return false;
        var teile = wert[Praefix.Length..].Split('/');
        return teile.Length == 3 && Array.TrueForAll(teile, t => NurZiffern(t, 20));
    }

    /// <summary>Die Nachrichten-ID: bei einem Link die letzte Zahl, sonst der Wert selbst.</summary>
    public static string NachrichtenId(string wert)
    {
        ArgumentNullException.ThrowIfNull(wert);
        var i = wert.LastIndexOf('/');
        return i < 0 ? wert : wert[(i + 1)..];
    }

    private static bool NurZiffern(string s, int max)
    {
        if (s.Length is 0 || s.Length > max) return false;
        foreach (var c in s)
        {
            if (c is < '0' or > '9') return false;
        }
        return true;
    }
}

/// <summary>Kopfdaten eines Raid-Helper-Events. Die Spezifikation nennt hier keine Längengrenzen.</summary>
public sealed record RaidHelperEvent : ISteuerBody
{
    public required string Id { get; init; }
    public required string Titel { get; init; }
    public required long Start { get; init; }
    public required long Ende { get; init; }
    public required long LastUpdated { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>Rollenvorschlag aus der Regeltabelle.</summary>
public sealed record RollenVorschlag
{
    public required ushort RolleId { get; init; }
    public ushort? SchiffId { get; init; }
}

/// <summary>Anmeldung in einem Raid-Helper-Event. Längen nennt die Spezifikation hier nicht.</summary>
public sealed record RaidHelperAnmeldung : ISteuerBody
{
    public required string DiscordId { get; init; }
    public required string Name { get; init; }
    public required string ClassName { get; init; }
    public required string SpecName { get; init; }
    public required string Status { get; init; }
    public required int Position { get; init; }
    public uint? KontoId { get; init; }
    public RollenVorschlag? Vorschlag { get; init; }

    public void Pruefen(ref Pruefung p) => p.Snowflake(DiscordId, "anmeldungen.discordId");
}

/// <summary>0x0044 RaidHelperImport, Antwort.</summary>
public sealed record RaidHelperImportAntwort : ISteuerBody
{
    public required RaidHelperEvent Event { get; init; }
    public required IReadOnlyList<RaidHelperAnmeldung> Anmeldungen { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Objekt(Event);
        p.Objekte(Anmeldungen, Grenzen.AnmeldungenMax, "anmeldungen");
    }
}

/// <summary>Regel der Regeltabelle: <c>specName</c> auch <c>"*"</c>.</summary>
public sealed record Regel : ISteuerBody
{
    public required string ClassName { get; init; }
    public required string SpecName { get; init; }
    public required ushort RolleId { get; init; }
    public ushort? SchiffId { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(ClassName, Grenzen.KlassenNameMax, "regeln.className");
        p.PflichtText(SpecName, Grenzen.KlassenNameMax, "regeln.specName");
    }
}

/// <summary>0x0045 RegelTabelle, Anfrage.</summary>
public sealed record RegelTabelleAnfrage : ISteuerBody
{
    public required RegelOperation Op { get; init; }
    public IReadOnlyList<Regel>? Regeln { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Objekte(Regeln, Grenzen.RegelnMax, "regeln");
        if (Op == RegelOperation.Setzen) p.Pflicht(Regeln is not null, "regeln");
    }
}

/// <summary>0x0045 RegelTabelle, Antwort.</summary>
public sealed record RegelTabelleAntwort : ISteuerBody
{
    public required IReadOnlyList<Regel> Regeln { get; init; }
    public required uint Version { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekte(Regeln, Grenzen.RegelnMax, "regeln");
}

/// <summary>0x0046 RaidHelperEvents, Antwort (Anfrage <c>{}</c>).</summary>
public sealed record RaidHelperEventsAntwort : ISteuerBody
{
    public required IReadOnlyList<RaidHelperEvent> Events { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekte(Events, Grenzen.EventsMax, "events");
}

/// <summary>0x0047 AufstellungGeaendert (S->C an <c>ZUWEISEN</c>).</summary>
public sealed record AufstellungGeaendert : ISteuerBody
{
    public required uint Id { get; init; }
    public required uint Version { get; init; }
    public required uint Durch { get; init; }
    public required bool Geloescht { get; init; }

    public void Pruefen(ref Pruefung p) { }
}
