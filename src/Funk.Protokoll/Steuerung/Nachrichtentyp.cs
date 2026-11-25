// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

/// <summary>Nachrichtentypen des Steuerkanals (4.4). 0x0070 ist frei und darf nicht verwendet werden.</summary>
public enum Nachrichtentyp : ushort
{
    Hello = 0x0001,
    Welcome = 0x0002,
    Bye = 0x0003,
    Ping = 0x0004,
    Fehler = 0x0006,
    Registrierung = 0x0010,
    GeraetStatus = 0x0011,
    GeraeteListe = 0x0012,
    GeraetEntscheiden = 0x0013,
    GeraetEntfernen = 0x0014,
    KontoVerwaltung = 0x0015,
    VerknuepfungBestaetigen = 0x0016,
    Beauftragung = 0x0017,
    ProfilSetzen = 0x0018,
    Journal = 0x0019,
    Rollenkatalog = 0x001A,
    Mitglied = 0x001B,
    Lieblingsrollen = 0x001C,
    /// <summary>Seit 0.11.8 entfallen, Nummer reserviert: nur noch die Hülle (LISTE leer, sonst UNGUELTIG).</summary>
    Untergilde = 0x001D,
    Mitgliedskarte = 0x001E,
    AntraegeAblehnen = 0x001F,
    Zustand = 0x0020,
    NetzListe = 0x0021,
    Mitgliedschaften = 0x0022,
    Praesenz = 0x0023,
    SendeFehler = 0x0024,
    Abzeichen = 0x0025,
    /// <summary>Seit 0.12.0: eine Meldung an alle (Meldesystem).</summary>
    MeldungSenden = 0x0026,
    /// <summary>Seit 0.12.0: "bereit" auf die offene Bereit-Abfrage.</summary>
    BereitMelden = 0x0027,
    SendeauswahlSetzen = 0x0030,
    HoerenSetzen = 0x0031,
    EinsteigenRolleSchiff = 0x0032,
    Verschieben = 0x0033,
    Stummschalten = 0x0034,
    FeldSetzen = 0x0035,
    SatzAnwenden = 0x0036,
    PlaetzeFreigeben = 0x0037,
    KontenVerzeichnis = 0x0038,
    RolleWaehlen = 0x0039,
    KanalSperre = 0x003A,
    Funkermodus = 0x003C,
    PhaseSchalten = 0x003D,
    AufstellungSpeichern = 0x0040,
    AufstellungListe = 0x0041,
    AufstellungAnwenden = 0x0042,
    AufstellungLoeschen = 0x0043,
    RaidHelperImport = 0x0044,
    RegelTabelle = 0x0045,
    RaidHelperEvents = 0x0046,
    AufstellungGeaendert = 0x0047,
    DurchsageStart = 0x0050,
    DurchsageEnde = 0x0051,
    SprechanzeigeAbo = 0x0052,
    Sprechanzeige = 0x0053,
    PegelVoreinstellungen = 0x0060,
    PegelSetzen = 0x0061,
    Ablage = 0x0062,
    KeyRotation = 0x0071,
    UdpStatus = 0x0072,
    TunnelModus = 0x0073,
    EmpfangsBericht = 0x0074,
    VerlustHinweis = 0x0075,
    Verwaltung = 0x0080,
    AdminNonce = 0x0090,
    AdminAnmeldung = 0x0091,
    AdminBestaetigen = 0x0092,
    AdminAbmelden = 0x0093,
    AdminEinrichtung = 0x0094,
    ServerLast = 0x4001,
    Meldung = 0x4002,
    Belegung = 0x4003,
    PhaseGeschaltet = 0x4004,
    /// <summary>Seit 0.12.0, optional: der laufende Stand des Meldesystems an alle.</summary>
    Meldestand = 0x4005,
    /// <summary>Seit 0.12.0, optional: "n von m bereit" an die, die senden dürfen.</summary>
    BereitStand = 0x4006,
    SpracheTunnel = 0x8001,
    VoiceKeys = 0x8002,
}

/// <summary>Typbereiche aus 4.2.</summary>
public enum Typbereich : byte
{
    /// <summary>0x0000: in keinem Bereich, wie ein unbekannter Pflicht-Typ.</summary>
    Ungueltig,
    /// <summary>0x0001-0x3FFF: JSON, Pflicht.</summary>
    JsonPflicht,
    /// <summary>0x4000-0x7FFF: JSON, optional.</summary>
    JsonOptional,
    /// <summary>0x8000-0xBFFF: binär, Pflicht.</summary>
    BinaerPflicht,
    /// <summary>0xC000-0xFFFF: binär, optional.</summary>
    BinaerOptional,
}

/// <summary>Einordnung von Typnummern.</summary>
public static class Typbereiche
{
    /// <summary>Bereich einer Typnummer.</summary>
    public static Typbereich Von(ushort typ) => typ switch
    {
        0 => Typbereich.Ungueltig,
        < 0x4000 => Typbereich.JsonPflicht,
        < 0x8000 => Typbereich.JsonOptional,
        < 0xC000 => Typbereich.BinaerPflicht,
        _ => Typbereich.BinaerOptional,
    };

    /// <summary>Ein unbekannter Typ aus diesem Bereich wird still übergangen.</summary>
    public static bool IstOptional(ushort typ) => Von(typ) is Typbereich.JsonOptional or Typbereich.BinaerOptional;

    /// <summary>Der Body ist JSON.</summary>
    public static bool IstJson(ushort typ) => Von(typ) is Typbereich.JsonPflicht or Typbereich.JsonOptional;
}
