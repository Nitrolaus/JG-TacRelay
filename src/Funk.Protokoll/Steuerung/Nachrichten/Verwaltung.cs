// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;

namespace Funk.Protokoll.Steuerung;

// Bodies der Stufe 1 (Anmeldung, Admin-Nachweis, Mitglieder): 0x0018-0x001F und 0x0090-0x0094. Neue Felder sind optional
// (4.7); was geheim ist (Codes, Geheimnis, Notfallcodes), steht nie in ToString.

/// <summary>
/// 0x0018 ProfilSetzen (C->S, selbst): SC-Namen 1 bis 3, Discord-Name, "Mein Zeichen zeigen" (N2, F11) und "Meine Angaben in
/// Wer ist da zeigen" (Sarevoc, 30.09.2026, Antwort 3).
/// </summary>
public sealed record ProfilSetzenAnfrage : ISteuerBody
{
    public IReadOnlyList<string>? ScNamen { get; init; }
    public string? DiscordName { get; init; }
    public bool? ZeichenZeigen { get; init; }
    public bool? KarteFreigeben { get; init; }

    /// <summary>
    /// Seit 0.11.7: das eine Abzeichen "vorn" (vor dem Namen in Wer ist da). Fehlt = bleibt, "" = keine Wahl. Erlaubt sind
    /// Katalogkennungen und ADMIN/EINSATZLEITUNG; Unbekanntes ist UNGUELTIG.
    /// </summary>
    public string? AbzeichenVorn { get; init; }

    /// <summary>
    /// Seit 0.11.7: die ausgeblendeten Abzeichen, ersetzt die Liste (fehlt = bleibt). Ausblenden ändert nur die Anzeige bei
    /// anderen, nie Rechte; erlaubt wie bei <see cref="AbzeichenVorn"/>.
    /// </summary>
    public IReadOnlyList<string>? AbzeichenAus { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Texte(ScNamen, Grenzen.ScNamenMax, Grenzen.NameMax, "scNamen");
        if (ScNamen is not null) p.Regel(ScNamen.Count >= 1, "scNamen");
        p.Text(DiscordName, Grenzen.NameMax, "discordName");
        p.Text(AbzeichenVorn, Grenzen.AbzeichenKennungMax, "abzeichenVorn");
        p.Texte(AbzeichenAus, Grenzen.AbzeichenAusMax, Grenzen.AbzeichenKennungMax, "abzeichenAus");
    }
}

/// <summary>0x0018 ProfilSetzen, Antwort: die bereinigten Angaben.</summary>
public sealed record ProfilSetzenAntwort : ISteuerBody
{
    public required IReadOnlyList<string> ScNamen { get; init; }
    public string? DiscordName { get; init; }
    public required bool ZeichenZeigen { get; init; }

    /// <summary>Freigabe der eigenen Angaben in "Wer ist da"; fehlt sie (älterer Server), gilt sie als erteilt.</summary>
    public bool? KarteFreigeben { get; init; }

    /// <summary>Seit 0.11.7: das gespeicherte Abzeichen "vorn"; fehlt, wenn keins gewählt ist (oder bei einem älteren Server).</summary>
    public string? AbzeichenVorn { get; init; }

    /// <summary>Seit 0.11.7: die gespeicherten ausgeblendeten Abzeichen; fehlt bei einem älteren Server.</summary>
    public IReadOnlyList<string>? AbzeichenAus { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Texte(ScNamen, Grenzen.ScNamenMax, Grenzen.NameMax, "scNamen");
        p.Text(DiscordName, Grenzen.NameMax, "discordName");
        p.Text(AbzeichenVorn, Grenzen.AbzeichenKennungMax, "abzeichenVorn");
        p.Texte(AbzeichenAus, Grenzen.AbzeichenAusMax, Grenzen.AbzeichenKennungMax, "abzeichenAus");
    }
}

/// <summary>0x0019 Journal (C->S, <c>KONTEN_VERWALTEN</c> in einer Admin-Sitzung): Einträge vor <c>vor</c>, neueste zuerst.</summary>
public sealed record JournalAnfrage : ISteuerBody
{
    public long? Vor { get; init; }
    public uint? KontoId { get; init; }

    public void Pruefen(ref Pruefung p) => p.Bereich(Vor, 0, JsonVorpruefung.MaxGanzzahl, "vor");
}

/// <summary>Ein Eintrag des Journals: Zeit, Art, wer (0 = Konsole), welches Konto, Klartext.</summary>
public sealed record JournalEintrag : ISteuerBody
{
    public required long Zeit { get; init; }
    public required string Art { get; init; }
    public required uint Durch { get; init; }
    public uint? KontoId { get; init; }
    public required string Text { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Art, 32, "eintraege.art");
        p.PflichtText(Text, 200, "eintraege.text");
    }
}

/// <summary>0x0019 Journal, Antwort, höchstens 200 Einträge.</summary>
public sealed record JournalAntwort : ISteuerBody
{
    public required IReadOnlyList<JournalEintrag> Eintraege { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekte(Eintraege, Grenzen.JournalMax, "eintraege");
}

/// <summary>Raid-Helper-Klasse eines Katalogeintrags (S4).</summary>
public sealed record RaidHelperKlasse : ISteuerBody
{
    public required string ClassName { get; init; }
    public required string SpecName { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(ClassName, Grenzen.KlassenNameMax, "raidHelper.className");
        p.PflichtText(SpecName, Grenzen.KlassenNameMax, "raidHelper.specName");
    }
}

/// <summary>Eintrag des Rollenkatalogs (N6).</summary>
public sealed record Rollenkatalogeintrag : ISteuerBody
{
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    public string? Gruppe { get; init; }
    public IReadOnlyList<ushort>? Rollen { get; init; }
    public IReadOnlyList<RaidHelperKlasse>? RaidHelper { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Bereich(Id, 1, ushort.MaxValue - 1, "eintraege.id");
        p.PflichtText(Name, Grenzen.RollenNameMax, "eintraege.name");
        p.Text(Gruppe, 20, "eintraege.gruppe");
        p.Liste(Rollen, Grenzen.MitgliederrollenMax, "eintraege.rollen");
        p.Objekte(RaidHelper, 8, "eintraege.raidHelper");
    }
}

/// <summary>0x001A Rollenkatalog (C->S): LESEN für alle, SETZEN mit <c>KONTEN_VERWALTEN</c> in einer Admin-Sitzung.</summary>
public sealed record RollenkatalogAnfrage : ISteuerBody
{
    public required RegelOperation Op { get; init; }
    public IReadOnlyList<Rollenkatalogeintrag>? Eintraege { get; init; }
    public long? Version { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Objekte(Eintraege, Grenzen.KatalogMax, "eintraege");
        if (Op == RegelOperation.Setzen) p.Pflicht(Eintraege is not null, "eintraege");
        p.Bereich(Version, 0, JsonVorpruefung.MaxGanzzahl, "version");
    }
}

/// <summary>0x001A Rollenkatalog, Antwort.</summary>
public sealed record RollenkatalogAntwort : ISteuerBody
{
    public required IReadOnlyList<Rollenkatalogeintrag> Eintraege { get; init; }
    public required long Version { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekte(Eintraege, Grenzen.KatalogMax, "eintraege");
}

/// <summary>
/// 0x001B Mitglied (C->S, <c>KONTEN_VERWALTEN</c> in einer Admin-Sitzung): LISTE, VORMERKEN, AENDERN (Konto oder
/// Vormerkung), LOESCHEN (Vormerkung), EINFUEGEN (Zeilen "SC-Name; Discord-Name; Kürzel; Rolle, Rolle", die dritte Spalte
/// wird seit 0.11.8 übergangen).
/// </summary>
public sealed record MitgliedAnfrage : ISteuerBody
{
    public required MitgliedOperation Op { get; init; }
    public uint? KontoId { get; init; }
    public uint? VormerkungId { get; init; }
    public string? ScName { get; init; }
    public string? DiscordName { get; init; }
    public IReadOnlyList<ushort>? Rollen { get; init; }

    /// <summary>Untergilde; seit 0.11.8 entfallen, der Server übergeht das Feld (bleibt für ältere Verwaltungen).</summary>
    public ushort? Untergilde { get; init; }

    public string? Notiz { get; init; }
    public IReadOnlyList<string>? Zeilen { get; init; }

    /// <summary>LISTE: ab diesem Eintrag (aus <c>weiter</c> der vorigen Seite), sonst von vorn.</summary>
    public uint? Ab { get; init; }

    /// <summary>
    /// Seit 0.12.0, nur AENDERN eines Kontos: Haken "darf Meldungen senden" (Meldesystem, M15) setzen oder wegnehmen; fehlt = bleibt.
    /// Bei einer Vormerkung UNGUELTIG. Ein älterer Server übergeht das Feld still (deshalb fragt die Verwaltung erst ab 0.12.0).
    /// </summary>
    public bool? MeldungenSenden { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Text(ScName, Grenzen.NameMax, "scName");
        p.Text(DiscordName, Grenzen.NameMax, "discordName");
        p.Liste(Rollen, Grenzen.MitgliederrollenMax, "rollen");
        p.Text(Notiz, 80, "notiz");
        p.Texte(Zeilen, Grenzen.EinfuegenZeilenMax, Grenzen.EinfuegenZeileLaenge, "zeilen");
        switch (Op)
        {
            case MitgliedOperation.Vormerken:
                p.Pflicht(ScName is not null, "scName");
                break;
            case MitgliedOperation.Aendern:
                p.Regel(KontoId is null != VormerkungId is null, "kontoId");
                break;
            case MitgliedOperation.Loeschen:
                p.Pflicht(VormerkungId is not null, "vormerkungId");
                break;
            case MitgliedOperation.Einfuegen:
                p.Pflicht(Zeilen is { Count: > 0 }, "zeilen");
                break;
        }
    }
}

/// <summary>Ein Mitglied in <c>Mitglied LISTE</c>: ein Konto oder eine Vormerkung.</summary>
public sealed record MitgliedEintrag : ISteuerBody
{
    public uint? KontoId { get; init; }
    public uint? VormerkungId { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> ScNamen { get; init; }
    public string? DiscordName { get; init; }
    public string? Benutzername { get; init; }

    /// <summary>Untergilde. Seit 0.11.8 entfallen: Der Server füllt das Feld nicht mehr; es bleibt nur für ältere Gegenstellen.</summary>
    public ushort? Untergilde { get; init; }

    public required IReadOnlyList<ushort> Rollen { get; init; }
    public IReadOnlyList<ushort>? Lieblingsrollen { get; init; }
    public ushort? ErsteWahl { get; init; }
    public Stufe? Stufe { get; init; }
    public required MitgliedZustand Zustand { get; init; }
    public long? Zuletzt { get; init; }
    public string? Notiz { get; init; }
    public bool? Eigentuemer { get; init; }
    public AdminStatus? Admin { get; init; }

    /// <summary>Seit 0.11.7: alle Ränge des Kontos (einer je Laufbahn), auch ausgeblendete; fehlt, wenn es keine hat.</summary>
    public IReadOnlyList<string>? Raenge { get; init; }

    /// <summary>Seit 0.11.7: alle Tätigkeiten des Kontos, auch ausgeblendete; fehlt, wenn es keine hat.</summary>
    public IReadOnlyList<string>? Taetigkeiten { get; init; }

    /// <summary>
    /// Seit 0.12.0: der Haken "darf Meldungen senden" ist gesetzt (nur dann steht das Feld da). Er wirkt nur bei Führung, Admin
    /// oder Einsatzleitung mit laufendem Auftrag (<see cref="Melderecht"/>), bleibt aber auch bei einem Funker stehen.
    /// </summary>
    public bool? MeldungenSenden { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.NameMax, "mitglieder.name");
        p.Texte(ScNamen, Grenzen.ScNamenMax, Grenzen.NameMax, "mitglieder.scNamen");
        p.Liste(Rollen, Grenzen.MitgliederrollenMax, "mitglieder.rollen");
        p.Liste(Lieblingsrollen, Grenzen.LieblingsrollenMax, "mitglieder.lieblingsrollen");
        p.Text(Notiz, 80, "mitglieder.notiz");
        p.Texte(Raenge, Grenzen.RaengeMax, Grenzen.AbzeichenKennungMax, "mitglieder.raenge");
        p.Texte(Taetigkeiten, Grenzen.TaetigkeitenMax, Grenzen.AbzeichenKennungMax, "mitglieder.taetigkeiten");
    }
}

/// <summary>Zeile aus "Liste einfügen", die nicht passt.</summary>
public sealed record Zeilenfehler : ISteuerBody
{
    public required ushort Zeile { get; init; }
    public required string Grund { get; init; }

    public void Pruefen(ref Pruefung p) => p.PflichtText(Grund, 100, "zeilen.grund");
}

/// <summary>
/// 0x001B Mitglied, Antwort: bei LISTE die Mitglieder (seitenweise zu höchstens 250, weiter mit <c>ab</c>), sonst die angelegten oder geänderten IDs. Bei EINFUEGEN mit
/// fehlerhaften Zeilen stehen sie unter <c>zeilen</c>, und nichts wurde angelegt (<c>ids</c> leer). Seit 0.11.8 kann eine
/// gelungene EINFUEGEN-Antwort einen <c>hinweis</c> tragen (Spalte 3 übergangen); ältere Verwaltungen übergehen das Feld.
/// </summary>
public sealed record MitgliedAntwort : ISteuerBody
{
    public IReadOnlyList<MitgliedEintrag>? Mitglieder { get; init; }
    public IReadOnlyList<uint>? Ids { get; init; }
    public IReadOnlyList<Zeilenfehler>? Zeilen { get; init; }

    /// <summary>LISTE: Es gibt weitere Einträge; die nächste Seite mit <c>ab</c> = diesem Wert holen.</summary>
    public uint? Weiter { get; init; }

    /// <summary>Seit 0.11.8, nur bei EINFUEGEN: ein Hinweis, der nichts verhindert hat (Spalte 3 übergangen).</summary>
    public string? Hinweis { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Text(Hinweis, 100, "hinweis");
        p.Objekte(Mitglieder, Grenzen.MitgliederListeMax, "mitglieder");
        p.Liste(Ids, Grenzen.EinfuegenZeilenMax, "ids");
        p.Objekte(Zeilen, Grenzen.EinfuegenZeilenMax, "zeilen");
    }
}

/// <summary>0x001C Lieblingsrollen (C->S, selbst): bis zu 8 aus dem Katalog, eine als erste Wahl (N8).</summary>
public sealed record LieblingsrollenAnfrage : ISteuerBody
{
    public required IReadOnlyList<ushort> Rollen { get; init; }
    public ushort? ErsteWahl { get; init; }

    public void Pruefen(ref Pruefung p) => p.Liste(Rollen, Grenzen.LieblingsrollenMax, "rollen");
}

/// <summary>0x001C Lieblingsrollen, Antwort: bereinigt (Rollen, die der Admin schon eingetragen hat, fallen weg).</summary>
public sealed record LieblingsrollenAntwort : ISteuerBody
{
    public required IReadOnlyList<ushort> Rollen { get; init; }
    public ushort? ErsteWahl { get; init; }

    public void Pruefen(ref Pruefung p) => p.Liste(Rollen, Grenzen.LieblingsrollenMax, "rollen");
}

/// <summary>
/// 0x001D Untergilde (C->S), seit 0.11.8 entfallen, Nummer reserviert: Der Typ bleibt als Hülle, damit ältere Clients, die
/// LISTE beim Öffnen rufen, nicht getrennt werden. LISTE liefert eine leere Liste, SETZEN und LOESCHEN antworten mit
/// UNGUELTIG ("Untergilden gibt es seit 0.11.8 nicht mehr").
/// </summary>
public sealed record UntergildeAnfrage : ISteuerBody
{
    public required UntergildeOperation Op { get; init; }
    public ushort? Id { get; init; }
    public string? Name { get; init; }
    public string? Kuerzel { get; init; }
    public IReadOnlyList<uint>? Konten { get; init; }
    public IReadOnlyList<uint>? Vormerkungen { get; init; }

    /// <summary>Einheit, die sie meist fliegt (schiffId). Bei SETZEN seit 0.11.0: fehlt = bleibt wie gespeichert, 0 = keins.</summary>
    public ushort? StammEinheit { get; init; }

    /// <summary>
    /// Ansprechpartner (Konto aus <see cref="Konten"/>). Bei SETZEN seit 0.11.0: fehlt = bleibt wie gespeichert (fällt still weg,
    /// wenn er nicht mehr unter den Konten steht), 0 = keins. Ein Server bis 0.10.0 lehnt 0 als UNGUELTIG ab.
    /// </summary>
    public uint? Ansprechpartner { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Text(Name, Grenzen.NameMax, "name");
        p.Text(Kuerzel, 4, "kuerzel");
        p.Liste(Konten, Grenzen.UntergildeMitgliederMax, "konten");
        p.Liste(Vormerkungen, Grenzen.UntergildeMitgliederMax, "vormerkungen");
        if (Op == UntergildeOperation.Setzen)
        {
            p.Pflicht(Name is not null, "name");
            p.Pflicht(Kuerzel is not null, "kuerzel");
        }
        if (Op == UntergildeOperation.Loeschen) p.Pflicht(Id is > 0, "id");
    }
}

/// <summary>Mitglied einer Untergilde in der Liste: Konto und Hauptname.</summary>
public sealed record UntergildeMitglied
{
    public required uint KontoId { get; init; }
    public required string Name { get; init; }
}

/// <summary>Eine Untergilde. Vormerkungen nur in einer Admin-Sitzung (sie nennen Leute, die noch nicht angemeldet sind).</summary>
public sealed record UntergildeEintrag : ISteuerBody
{
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    public required string Kuerzel { get; init; }
    public required IReadOnlyList<UntergildeMitglied> Konten { get; init; }
    public IReadOnlyList<uint>? Vormerkungen { get; init; }
    public ushort? StammEinheit { get; init; }
    public uint? Ansprechpartner { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.NameMax, "untergilden.name");
        p.PflichtText(Kuerzel, 4, "untergilden.kuerzel");
        p.Liste(Konten, Grenzen.UntergildeMitgliederMax, "untergilden.konten");
        p.Liste(Vormerkungen, Grenzen.UntergildeMitgliederMax, "untergilden.vormerkungen");
    }
}

/// <summary>0x001D Untergilde, Antwort (seit 0.11.8 entfallen): bei LISTE immer leer.</summary>
public sealed record UntergildeAntwort : ISteuerBody
{
    public IReadOnlyList<UntergildeEintrag>? Untergilden { get; init; }
    public ushort? Id { get; init; }

    public void Pruefen(ref Pruefung p) => p.Objekte(Untergilden, Grenzen.UntergildenMax, "untergilden");
}

/// <summary>0x001E Mitgliedskarte (C->S): die Karte eines Kontos, Felder je nach Sichtbarkeit (A48, Frage 3).</summary>
public sealed record MitgliedskarteAnfrage : ISteuerBody
{
    public required uint KontoId { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>Letzter Platz eines Kontos (für die Karte).</summary>
public sealed record LetzterPlatz
{
    public ushort? SchiffId { get; init; }
    public ushort? RolleId { get; init; }
}

/// <summary>
/// 0x001E Mitgliedskarte, Antwort. Jeder sieht Hauptname, Zeichen, Platz und (nach Voreinstellung) die Rollen;
/// weitere SC-Namen, Discord-Name, Verbindung und letzter Platz nur Einsatzleitung (ZUWEISEN), Admins und das Konto selbst.
/// </summary>
public sealed record MitgliedskarteAntwort : ISteuerBody
{
    public required uint KontoId { get; init; }
    public required string Name { get; init; }
    public string? ScName { get; init; }
    public IReadOnlyList<string>? ScNamen { get; init; }
    public string? DiscordName { get; init; }

    /// <summary>Untergilde. Seit 0.11.8 entfallen: Der Server füllt das Feld nicht mehr; es bleibt nur für ältere Gegenstellen.</summary>
    public ushort? Untergilde { get; init; }

    public IReadOnlyList<ushort>? Rollen { get; init; }
    public IReadOnlyList<ushort>? Lieblingsrollen { get; init; }
    public ushort? ErsteWahl { get; init; }
    public required bool Online { get; init; }
    public ushort? SchiffId { get; init; }
    public ushort? RolleId { get; init; }
    public PlatzStatus? Platz { get; init; }
    public LetzterPlatz? LetzterPlatz { get; init; }
    public required IReadOnlyList<string> Zeichen { get; init; }
    public Verbindungsweg? Verbindung { get; init; }
    public Guete? Guete { get; init; }

    /// <summary>
    /// Ob der Inhaber seine Angaben freigibt (<c>ProfilSetzen.karteFreigeben</c>, Antwort 3); für die Einsatzleitung, Admins und
    /// das Konto selbst immer <c>true</c>. Fehlen dann Rollen, sieht sie auf diesem Server nur die Einsatzleitung.
    /// </summary>
    public bool? Freigegeben { get; init; }

    /// <summary>
    /// Seit 0.11.7: die sichtbaren Abzeichen (Ränge in Laufbahnfolge, dann Tätigkeiten in Katalogfolge), mit denselben Sperren wie
    /// <see cref="Zeichen"/>; das Konto selbst sieht alle eigenen. Fehlt, wenn keins zu zeigen ist.
    /// </summary>
    public IReadOnlyList<string>? Abzeichen { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Name, Grenzen.NameMax, "name");
        p.Text(ScName, Grenzen.NameMax, "scName");
        p.Texte(ScNamen, Grenzen.ScNamenMax, Grenzen.NameMax, "scNamen");
        p.Text(DiscordName, Grenzen.NameMax, "discordName");
        p.Liste(Rollen, Grenzen.MitgliederrollenMax, "rollen");
        p.Liste(Lieblingsrollen, Grenzen.LieblingsrollenMax, "lieblingsrollen");
        p.Texte(Zeichen, Grenzen.ZeichenMax, 16, "zeichen");
        p.Texte(Abzeichen, Grenzen.AbzeichenMax, Grenzen.AbzeichenKennungMax, "abzeichen");
    }
}

/// <summary>
/// 0x0025 Abzeichen (C->S, seit 0.11.7, <c>KONTEN_VERWALTEN</c> in einer Admin-Sitzung): setzt die Ränge (höchstens einer je
/// Laufbahn) und Tätigkeiten (höchstens 6) eines Kontos; beide Listen ersetzen die gespeicherten. Der Server prüft jede Kennung
/// gegen den Abzeichenkatalog (Art, Laufbahn, keine Doppelten); ein Verstoß ist UNGUELTIG ohne Trennung.
/// </summary>
public sealed record AbzeichenAnfrage : ISteuerBody
{
    public required uint KontoId { get; init; }
    public required IReadOnlyList<string> Raenge { get; init; }
    public required IReadOnlyList<string> Taetigkeiten { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Texte(Raenge, Grenzen.RaengeMax, Grenzen.AbzeichenKennungMax, "raenge");
        p.Texte(Taetigkeiten, Grenzen.TaetigkeitenMax, Grenzen.AbzeichenKennungMax, "taetigkeiten");
    }
}

/// <summary>0x0025 Abzeichen, Antwort: der gespeicherte Stand, Ränge in Laufbahnfolge, Tätigkeiten in Katalogfolge.</summary>
public sealed record AbzeichenAntwort : ISteuerBody
{
    public required uint KontoId { get; init; }
    public required IReadOnlyList<string> Raenge { get; init; }
    public required IReadOnlyList<string> Taetigkeiten { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Texte(Raenge, Grenzen.RaengeMax, Grenzen.AbzeichenKennungMax, "raenge");
        p.Texte(Taetigkeiten, Grenzen.TaetigkeitenMax, Grenzen.AbzeichenKennungMax, "taetigkeiten");
    }
}

/// <summary>0x001F AntraegeAblehnen (C->S): "Alle ablehnen, die zu keiner Vormerkung passen" (frische Bestätigung).</summary>
public sealed record AntraegeAblehnenAnfrage : ISteuerBody
{
    public required bool NurOhneVormerkung { get; init; }

    public void Pruefen(ref Pruefung p) => p.Regel(NurOhneVormerkung, "nurOhneVormerkung");
}

/// <summary>0x001F AntraegeAblehnen, Antwort.</summary>
public sealed record AntraegeAblehnenAntwort : ISteuerBody
{
    public required ushort Anzahl { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>0x0090 AdminNonce, Antwort: Nonce des Servers (32 Byte, Base64), gilt 60 s und einmal (3.9).</summary>
public sealed record AdminNonceAntwort : ISteuerBody
{
    public required string Nonce { get; init; }
    public required uint GueltigMs { get; init; }

    public void Pruefen(ref Pruefung p) => p.Base64Genau(Nonce, 32, "nonce");
}

/// <summary>
/// 0x0091 AdminAnmeldung (C->S): Unterschrift des Admin-Schlüssels (Zweck ANMELDUNG) und ein Code aus der App oder ein
/// Notfallcode, genau eins von beiden.
/// </summary>
public sealed record AdminAnmeldungAnfrage : ISteuerBody
{
    public required string Unterschrift { get; init; }
    public string? Code { get; init; }
    public string? Notfallcode { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Base64Genau(Unterschrift, 64, "unterschrift");
        p.Text(Code, 16, "code");
        p.Text(Notfallcode, 32, "notfallcode");
        p.Regel(Code is null != Notfallcode is null, "code");
    }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(Notfallcode is null ? "Unterschrift = ***, Code = ***" : "Unterschrift = ***, Notfallcode = ***");
        return true;
    }
}

/// <summary>
/// Antwort auf AdminAnmeldung und AdminBestaetigen: Ende der Admin-Sitzung (Leerlauf oder 12 h), Ende der frischen
/// Bestätigung, Hinweise.
/// </summary>
public sealed record AdminSitzungAntwort : ISteuerBody
{
    public required long Bis { get; init; }
    public required long BestaetigtBis { get; init; }
    public bool? UhrHinweis { get; init; }
    public byte? NotfallcodesUebrig { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>0x0092 AdminBestaetigen (C->S): frischer Code, nur in einer offenen Admin-Sitzung.</summary>
public sealed record AdminBestaetigenAnfrage : ISteuerBody
{
    public string? Code { get; init; }
    public string? Notfallcode { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Text(Code, 16, "code");
        p.Text(Notfallcode, 32, "notfallcode");
        p.Regel(Code is null != Notfallcode is null, "code");
    }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(Notfallcode is null ? "Code = ***" : "Notfallcode = ***");
        return true;
    }
}

/// <summary>
/// 0x0094 AdminEinrichtung (C->S): ANTRAG (nur für ein vorgesehenes Konto, mit dem neuen Schlüssel als SPKI),
/// GEHEIMNIS_ABHOLEN (genau einmal, 10 min), BESTAETIGEN (erster Code). Immer mit Unterschrift und vorheriger AdminNonce.
/// </summary>
public sealed record AdminEinrichtungAnfrage : ISteuerBody
{
    public required AdminEinrichtungOperation Op { get; init; }
    public required string Unterschrift { get; init; }
    public string? Schluessel { get; init; }
    public string? Code { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Base64Genau(Unterschrift, 64, "unterschrift");
        p.Base64Genau(Schluessel, 91, "schluessel");
        p.Text(Code, 16, "code");
        if (Op == AdminEinrichtungOperation.Antrag) p.Pflicht(Schluessel is not null, "schluessel");
        if (Op == AdminEinrichtungOperation.Bestaetigen) p.Pflicht(Code is not null, "code");
    }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Op = {Op}, Unterschrift = ***, Code = ***");
        return true;
    }
}

/// <summary>
/// 0x0094 AdminEinrichtung, Antwort: bei ANTRAG die Admin-Kennung zum Vorlesen, bei GEHEIMNIS_ABHOLEN das Geheimnis (Base32)
/// samt otpauth-URI für den QR-Code, bei BESTAETIGEN die 10 Notfallcodes. Alles genau einmal.
/// </summary>
public sealed record AdminEinrichtungAntwort : ISteuerBody
{
    public string? Kennung { get; init; }
    public string? Geheimnis { get; init; }
    public string? Uri { get; init; }
    public long? GueltigBis { get; init; }
    public IReadOnlyList<string>? Notfallcodes { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Text(Kennung, Grenzen.AnzeigeMax, "kennung");
        p.Text(Geheimnis, 64, "geheimnis");
        p.Text(Uri, Grenzen.OtpauthUriMax, "uri");
        p.Texte(Notfallcodes, Grenzen.NotfallcodesMax, 14, "notfallcodes");
    }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(CultureInfo.InvariantCulture, $"Kennung = {Kennung}, Geheimnis = ***, Uri = ***, GueltigBis = {GueltigBis}, Notfallcodes = ***");
        return true;
    }
}
