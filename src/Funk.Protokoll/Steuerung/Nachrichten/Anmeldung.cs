// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Funk.Protokoll.Steuerung;

// Bodies 0x0001-0x0016 aus 4.5. Feldreihenfolge = Katalogreihenfolge (so schreibt der Sender).
// Optionale Felder sind nullable: JSON-null gilt wie ein fehlendes Feld (4.3).

/// <summary>Leerer Body <c>{}</c>; unbekannte Felder werden übergangen.</summary>
public sealed record LeererBody : ISteuerBody
{
    /// <summary>Gemeinsame Instanz.</summary>
    public static LeererBody Wert { get; } = new();

    public void Pruefen(ref Pruefung p) { }
}

/// <summary><c>{zustandsVersion}</c>: Antwort auf Zustand, HoerenSetzen, EinsteigenRolleSchiff, FeldSetzen und Push Zustand.</summary>
public sealed record ZustandsVersionBody : ISteuerBody
{
    public required long ZustandsVersion { get; init; }

    public void Pruefen(ref Pruefung p) => p.Bereich(ZustandsVersion, 0, JsonVorpruefung.MaxGanzzahl, "zustandsVersion");
}

/// <summary>0x0001 Hello (C->S, erster Rahmen nach TLS).</summary>
public sealed record Hello : ISteuerBody
{
    public required ushort Minor { get; init; }
    public required ClientAngabe Client { get; init; }
    public required SchluesselArt SchluesselArt { get; init; }
    public required string Opus { get; init; }
    public required IReadOnlyList<string> Faehigkeiten { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Objekt(Client);
        p.PflichtText(Opus, 16, "opus");
        p.Texte(Faehigkeiten, Grenzen.FaehigkeitenMax, 32, "faehigkeiten");
    }
}

/// <summary><c>Hello.client</c>.</summary>
public sealed record ClientAngabe : ISteuerBody
{
    public required string Name { get; init; }
    public required string Version { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, 32, "client.name");
        p.PflichtText(Version, 32, "client.version");
        if (p.Ok) p.Regel(SemVer.TryParse(Version, out _), "client.version");
    }
}

/// <summary>0x0002 Welcome (S->C). Welche Felder gesetzt sind, hängt von <see cref="Status"/> ab.</summary>
public sealed record Welcome : ISteuerBody
{
    public required ushort Minor { get; init; }
    public required WelcomeStatus Status { get; init; }
    public RegistrierungsAngabe? Registrierung { get; init; }
    public uint? SitzungsId { get; init; }
    public KontoAngabe? Konto { get; init; }
    public GeraetAngabe? Geraet { get; init; }
    public ServerAngabe? Server { get; init; }
    public UdpAngabe? Udp { get; init; }
    public long? ServerZeit { get; init; }
    public string? MindestClientVersion { get; init; }
    public SigniertesProfil? Profil { get; init; }
    public long? ZustandsVersion { get; init; }
    public WelcomeGrenzen? Grenzen { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        switch (Status)
        {
            case WelcomeStatus.Unbekannt:
                p.Pflicht(Registrierung is not null, "registrierung");
                break;
            case WelcomeStatus.Wartet:
                p.Pflicht(Konto is not null, "konto");
                p.Pflicht(Geraet is not null, "geraet");
                break;
            case WelcomeStatus.Freigegeben:
                p.Pflicht(SitzungsId is > 0, "sitzungsId");
                p.Pflicht(Konto is { Stufe: not null, Rechte: not null }, "konto");
                p.Pflicht(Server is not null, "server");
                p.Pflicht(Udp is not null, "udp");
                p.Pflicht(ServerZeit is not null, "serverZeit");
                p.Pflicht(MindestClientVersion is not null, "mindestClientVersion");
                p.Pflicht(Profil is not null, "profil");
                p.Pflicht(ZustandsVersion is not null, "zustandsVersion");
                p.Pflicht(Grenzen is not null, "grenzen");
                break;
        }
        p.Objekt(Registrierung);
        p.Objekt(Konto);
        p.Objekt(Geraet);
        p.Objekt(Server);
        p.Objekt(Udp);
        p.Text(MindestClientVersion, 32, "mindestClientVersion");
        if (MindestClientVersion is not null && p.Ok) p.Regel(SemVer.TryParse(MindestClientVersion, out _), "mindestClientVersion");
        p.Objekt(Profil);
        p.Bereich(ZustandsVersion, 0, JsonVorpruefung.MaxGanzzahl, "zustandsVersion");
    }

    /// <summary>Welcome für ein unbekanntes Gerät: nach 4.5 nur <c>registrierung</c>, keine weiteren Angaben (P-44).</summary>
    public static Welcome Unbekannt(ushort minor) => new()
    {
        Minor = minor,
        Status = WelcomeStatus.Unbekannt,
        Registrierung = new RegistrierungsAngabe { CodeLaenge = 12, GueltigSek = 300 },
    };
}

/// <summary><c>Welcome.registrierung</c>.</summary>
public sealed record RegistrierungsAngabe : ISteuerBody
{
    public required byte CodeLaenge { get; init; }
    public required ushort GueltigSek { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>
/// <c>Welcome.konto</c>: bei WARTET id, name, benutzername (leer ohne Discord-Verknüpfung); bei FREIGEGEBEN zusätzlich
/// stufe und rechte, dazu SC-Namen, Discord-Name und der Admin-Zustand des eigenen Kontos (F2, 3.9).
/// </summary>
public sealed record KontoAngabe : ISteuerBody
{
    public required uint Id { get; init; }
    public required string Name { get; init; }
    public required string Benutzername { get; init; }
    public Stufe? Stufe { get; init; }
    public IReadOnlyList<Recht>? Rechte { get; init; }
    public IReadOnlyList<string>? ScNamen { get; init; }
    public string? DiscordName { get; init; }
    public AdminStatus? Admin { get; init; }
    public bool? AdminAntrag { get; init; }
    public bool? Eigentuemer { get; init; }

    /// <summary>Laufender oder geplanter Auftrag für die Einsatzleitung (F2, 6.12); nur bei FREIGEGEBEN für das eigene Konto.</summary>
    public BeauftragungAngabe? Beauftragung { get; init; }

    /// <summary>"Heute als Funker" (6.15, seit 0.5.0): bis wann der eigene Funker-Modus läuft; nur bei FREIGEGEBEN, fehlt, wenn er aus ist.</summary>
    public long? AlsFunkerBis { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Objekt(Beauftragung);
        p.Bereich(AlsFunkerBis, 0, JsonVorpruefung.MaxGanzzahl, "konto.alsFunkerBis");
        p.Bereich(Id, 1, int.MaxValue, "konto.id");
        p.PflichtText(Name, Grenzen.NameMax, "konto.name");
        p.PflichtText(Benutzername, Grenzen.NameMax, "konto.benutzername");
        p.Liste(Rechte, Grenzen.RechteMax, "konto.rechte");
        p.Texte(ScNamen, Grenzen.ScNamenMax, Grenzen.NameMax, "konto.scNamen");
        p.Text(DiscordName, Grenzen.NameMax, "konto.discordName");
    }
}

/// <summary><c>Welcome.geraet</c>.</summary>
public sealed record GeraetAngabe : ISteuerBody
{
    public required uint Id { get; init; }
    public required string Name { get; init; }
    public required string Anzeige { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.GeraeteNameMax, "geraet.name");
        p.PflichtText(Anzeige, Grenzen.AnzeigeMax, "geraet.anzeige");
    }
}

/// <summary><c>Welcome.server</c>.</summary>
public sealed record ServerAngabe : ISteuerBody
{
    public required string Version { get; init; }

    public void Pruefen(ref Pruefung p) => p.PflichtText(Version, 32, "server.version");
}

/// <summary><c>Welcome.udp</c>.</summary>
public sealed record UdpAngabe : ISteuerBody
{
    public required ushort Port { get; init; }
    public required ushort MaxDatagramm { get; init; }

    public void Pruefen(ref Pruefung p) => p.Bereich(Port, 1, ushort.MaxValue, "udp.port");
}

/// <summary>Signiertes Serverprofil <c>{daten: b64, signatur: b64}</c> (3.1); geprüft wird es an anderer Stelle.</summary>
public sealed record SigniertesProfil : ISteuerBody
{
    public required string Daten { get; init; }
    public required string Signatur { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Base64(Daten, "profil.daten");
        p.Base64Genau(Signatur, 64, "profil.signatur");
    }
}

/// <summary><c>Welcome.grenzen</c>.</summary>
public sealed record WelcomeGrenzen
{
    public required uint RahmenMax { get; init; }
    public required ushort OpusMax { get; init; }
    public required byte ZieleNetzMax { get; init; }
    public required byte ZieleDurchsageMax { get; init; }
    public required uint PingMs { get; init; }
    public required uint TotMs { get; init; }
    public required uint RuheMs { get; init; }
    public required uint KeepaliveMs { get; init; }

    /// <summary>Werte aus 4.5.</summary>
    public static WelcomeGrenzen Standard { get; } = new()
    {
        RahmenMax = Rahmengrenzen.Aktiv,
        OpusMax = 400,
        ZieleNetzMax = 16,
        ZieleDurchsageMax = 64,
        PingMs = 2000,
        TotMs = 6000,
        RuheMs = 10000,
        KeepaliveMs = 2000,
    };
}

/// <summary>0x0003 Bye (beide Richtungen).</summary>
public sealed record Bye : ISteuerBody
{
    public required ByeGrund Grund { get; init; }
    public uint? WiederInMs { get; init; }
    public ByeDurch? Durch { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekt(Durch);
}

/// <summary><c>Bye.durch</c> bei VERDRAENGT.</summary>
public sealed record ByeDurch : ISteuerBody
{
    public required string GeraetName { get; init; }
    public required string Anzeige { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(GeraetName, Grenzen.GeraeteNameMax, "durch.geraetName");
        p.PflichtText(Anzeige, Grenzen.AnzeigeMax, "durch.anzeige");
    }
}

/// <summary>0x0004 Ping, Anfrage.</summary>
public sealed record PingAnfrage : ISteuerBody
{
    public required uint Nr { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>0x0004 Ping, Antwort.</summary>
public sealed record PingAntwort : ISteuerBody
{
    public required uint Nr { get; init; }
    public required long UdpEmpfangen { get; init; }
    public required long UdpGesendet { get; init; }
    public required long ServerZeit { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(UdpEmpfangen, 0, JsonVorpruefung.MaxGanzzahl, "udpEmpfangen");
        p.Bereich(UdpGesendet, 0, JsonVorpruefung.MaxGanzzahl, "udpGesendet");
    }
}

/// <summary>0x0006 Fehler, als Antwort oder Push.</summary>
public sealed record Fehler : ISteuerBody
{
    public required Fehlercode Code { get; init; }
    public string? Text { get; init; }
    public ushort? BezugTyp { get; init; }
    public JsonElement? Details { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Text(Text, 200, "text");
        p.FreiesJson(Details, Grenzen.FehlerDetailsMax, "details");
        p.IstObjekt(Details, "details");
    }
}

/// <summary>
/// 0x0010 Registrierung, Anfrage. Mit <c>scName</c> und <c>discordName</c> (beide oder keiner, P3) entsteht ohne Code ein
/// Antrag, den ein Admin freigibt; ohne sie gibt es wie bisher einen Einmalcode für den Bot.
/// </summary>
public sealed record RegistrierungAnfrage : ISteuerBody
{
    public required string Geraetename { get; init; }
    public string? ScName { get; init; }
    public string? DiscordName { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Geraetename, Grenzen.GeraeteNameMax, "geraetename");
        p.Text(ScName, Grenzen.NameMax, "scName");
        p.Text(DiscordName, Grenzen.NameMax, "discordName");
        p.Regel(ScName is null == DiscordName is null, "discordName");
    }
}

/// <summary>
/// 0x0010 Registrierung, Antwort: der Einmalcode (<c>code</c>, <c>gueltigBis</c>, <c>befehl</c>) oder beim Antrag nur
/// <c>antrag: true</c>; danach folgt <c>GeraetStatus{WARTET}</c>.
/// </summary>
public sealed record RegistrierungAntwort : ISteuerBody
{
    public string? Code { get; init; }
    public long? GueltigBis { get; init; }
    public string? Befehl { get; init; }
    public bool? Antrag { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Text(Code, 14, "code");
        p.Text(Befehl, 40, "befehl");
        p.Regel(Antrag == true || (Code is not null && GueltigBis is not null && Befehl is not null), "code");
    }

    /// <summary>Der Einmalcode erscheint nie in <c>ToString</c> und damit in keinem Log (1.5, P-46).</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Code = {(Code is null ? "-" : "***")}, GueltigBis = {GueltigBis}, Befehl = {Befehl}, Antrag = {Antrag}");
        return true;
    }
}

/// <summary>0x0011 GeraetStatus (S->C Push).</summary>
public sealed record GeraetStatus : ISteuerBody
{
    public required uint GeraetId { get; init; }
    public required uint KontoId { get; init; }
    public required string KontoName { get; init; }
    public required string Benutzername { get; init; }
    public required GeraeteStatus Status { get; init; }
    public required string Anzeige { get; init; }
    public required string Fingerabdruck { get; init; }
    public required SchluesselArt SchluesselArt { get; init; }
    public required bool ErstesGeraet { get; init; }
    public Markierung? Markierung { get; init; }
    public string? Grund { get; init; }
    public uint? Durch { get; init; }

    /// <summary>SC-Name des Kontos bzw. des Antrags (F14).</summary>
    public string? ScName { get; init; }
    public string? DiscordName { get; init; }

    /// <summary>Das Gerät gehört zu einem Antrag ohne Code (3.4).</summary>
    public bool? Antrag { get; init; }

    /// <summary>Namen des Antrags, die schon ein freigegebenes Konto trägt; freigegeben wird dann ohne sie.</summary>
    public IReadOnlyList<string>? ScNamenVergeben { get; init; }

    /// <summary>Den Discord-Namen des Antrags trägt schon ein Konto oder eine Vormerkung; freigeben geht so nicht (A53).</summary>
    public bool? DiscordNameVergeben { get; init; }

    /// <summary>Passende Vormerkungen als Vorschlag, nur an Admin-Sitzungen (F14).</summary>
    public IReadOnlyList<VormerkungVorschlag>? Vormerkungen { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(KontoName, Grenzen.NameMax, "kontoName");
        p.PflichtText(Benutzername, Grenzen.NameMax, "benutzername");
        p.PflichtText(Anzeige, Grenzen.AnzeigeMax, "anzeige");
        p.Base64Genau(Fingerabdruck, 32, "fingerabdruck");
        p.Text(Grund, 40, "grund");
        p.Text(ScName, Grenzen.NameMax, "scName");
        p.Text(DiscordName, Grenzen.NameMax, "discordName");
        p.Texte(ScNamenVergeben, Grenzen.ScNamenMax, Grenzen.NameMax, "scNamenVergeben");
        p.Objekte(Vormerkungen, Grenzen.VorschlaegeMax, "vormerkungen");
    }
}

/// <summary>Vormerkung als Vorschlag in der Freigabe (F14); übernommen wird sie nur mit <c>vormerkungId</c>.</summary>
public sealed record VormerkungVorschlag : ISteuerBody
{
    public required uint Id { get; init; }
    public required string ScName { get; init; }
    public string? DiscordName { get; init; }
    public required IReadOnlyList<ushort> Rollen { get; init; }

    /// <summary>Untergilde der Vormerkung. Seit 0.11.8 entfallen: Der Server füllt das Feld nicht mehr; es bleibt nur für ältere Gegenstellen.</summary>
    public ushort? Untergilde { get; init; }

    /// <summary>Passt über den SC-Namen (sonst über den Discord-Namen); bei genau einem solchen Treffer vorausgewählt.</summary>
    public required bool UeberScName { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(ScName, Grenzen.NameMax, "vormerkungen.scName");
        p.Text(DiscordName, Grenzen.NameMax, "vormerkungen.discordName");
        p.Liste(Rollen, Grenzen.MitgliederrollenMax, "vormerkungen.rollen");
    }
}

/// <summary>0x0012 GeraeteListe, Anfrage.</summary>
public sealed record GeraeteListeAnfrage : ISteuerBody
{
    public uint? KontoId { get; init; }
    public bool? NurWartend { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>0x0012 GeraeteListe, Antwort.</summary>
public sealed record GeraeteListeAntwort : ISteuerBody
{
    public required IReadOnlyList<GeraetEintrag> Geraete { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekte(Geraete, Grenzen.GeraeteListeMax, "geraete");
}

/// <summary>Eintrag in <c>GeraeteListe.geraete</c>.</summary>
public sealed record GeraetEintrag : ISteuerBody
{
    public required uint Id { get; init; }
    public required uint KontoId { get; init; }
    public required string KontoName { get; init; }
    public required string Benutzername { get; init; }
    public required string Name { get; init; }
    public required string Anzeige { get; init; }
    public required string Fingerabdruck { get; init; }
    public required SchluesselArt SchluesselArt { get; init; }
    public required GeraeteStatus Status { get; init; }
    public Markierung? Markierung { get; init; }
    public required long Verknuepft { get; init; }
    public long? Zuletzt { get; init; }
    public string? ScName { get; init; }
    public string? DiscordName { get; init; }
    public bool? Antrag { get; init; }
    public IReadOnlyList<string>? ScNamenVergeben { get; init; }

    /// <summary>Den Discord-Namen des Antrags trägt schon ein Konto oder eine Vormerkung; freigeben geht so nicht (A53).</summary>
    public bool? DiscordNameVergeben { get; init; }

    public IReadOnlyList<VormerkungVorschlag>? Vormerkungen { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(KontoName, Grenzen.NameMax, "kontoName");
        p.PflichtText(Benutzername, Grenzen.NameMax, "benutzername");
        p.PflichtText(Name, Grenzen.GeraeteNameMax, "name");
        p.PflichtText(Anzeige, Grenzen.AnzeigeMax, "anzeige");
        p.Base64Genau(Fingerabdruck, 32, "fingerabdruck");
        p.Text(ScName, Grenzen.NameMax, "scName");
        p.Text(DiscordName, Grenzen.NameMax, "discordName");
        p.Texte(ScNamenVergeben, Grenzen.ScNamenMax, Grenzen.NameMax, "scNamenVergeben");
        p.Objekte(Vormerkungen, Grenzen.VorschlaegeMax, "vormerkungen");
    }
}

/// <summary>
/// 0x0013 GeraetEntscheiden, Anfrage. Antwort <c>{}</c>. Bei FREIGEBEN eines Antrags übernimmt <c>vormerkungId</c> eine
/// Vormerkung, <c>kontoId</c> hängt das Gerät an ein vorhandenes Konto (zweites Gerät); höchstens eins von beiden (F14).
/// </summary>
public sealed record GeraetEntscheidenAnfrage : ISteuerBody
{
    public required uint GeraetId { get; init; }
    public required GeraetEntscheidung Entscheidung { get; init; }
    public uint? VormerkungId { get; init; }
    public uint? KontoId { get; init; }

    public void Pruefen(ref Pruefung p) => p.Regel(VormerkungId is null || KontoId is null, "kontoId");
}

/// <summary>0x0014 GeraetEntfernen, Anfrage.</summary>
public sealed record GeraetEntfernenAnfrage : ISteuerBody
{
    public required uint GeraetId { get; init; }
    public required bool Dauerhaft { get; init; }
    public required string Grund { get; init; }

    public void Pruefen(ref Pruefung p) => p.PflichtText(Grund, 40, "grund");
}

/// <summary>0x0014 GeraetEntfernen, Antwort.</summary>
public sealed record GeraetEntfernenAntwort : ISteuerBody
{
    public required GeraeteStatus Status { get; init; }

    public void Pruefen(ref Pruefung p) =>
        p.Regel(Status is GeraeteStatus.Entfernt or GeraeteStatus.Entzogen or GeraeteStatus.Beantragt, "status");
}

/// <summary>0x0015 KontoVerwaltung, Anfrage. <c>wert</c> hängt von <c>op</c> ab (6.10). Antwort <c>{}</c>.</summary>
public sealed record KontoVerwaltungAnfrage : ISteuerBody
{
    public required KontoOperation Op { get; init; }
    public required uint KontoId { get; init; }
    public JsonElement? Wert { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>0x0016 VerknuepfungBestaetigen, Server-Anfrage.</summary>
public sealed record VerknuepfungBestaetigenAnfrage : ISteuerBody
{
    public required VerknuepfungKonto Konto { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekt(Konto);
}

/// <summary><c>VerknuepfungBestaetigen.konto</c>.</summary>
public sealed record VerknuepfungKonto : ISteuerBody
{
    public required string Name { get; init; }
    public required string Benutzername { get; init; }
    public required bool Neu { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.NameMax, "konto.name");
        p.PflichtText(Benutzername, Grenzen.NameMax, "konto.benutzername");
    }
}

/// <summary>0x0016 VerknuepfungBestaetigen, Antwort des Clients.</summary>
public sealed record VerknuepfungBestaetigenAntwort : ISteuerBody
{
    public required bool Ja { get; init; }

    public void Pruefen(ref Pruefung p) { }
}
