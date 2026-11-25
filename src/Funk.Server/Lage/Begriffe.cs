// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Server.Lage;

// Namen bewusst anders als in Funk.Protokoll.Sprache (Kennzeichen, Zielart, Spruchart), damit
// Code, der beide Namensräume einbindet, ohne Mehrdeutigkeit auskommt.

/// <summary>Stufe eines Kontos (6.1). Die Zahl ist der Rang (6.10): FUNKER &lt; RAIDLEITER &lt; ADMIN.</summary>
public enum Stufe : byte
{
    Funker = 0,
    Raidleiter = 1,
    Admin = 2,
}

/// <summary>Status eines Kontos (3.3).</summary>
public enum KontoStatus : byte
{
    Aktiv = 0,
    Gesperrt = 1,
}

/// <summary>Zielart im Sprachpaket hoch (5.4): 1 = NETZ, 2 = DURCHSAGE.</summary>
public enum Sendeart : byte
{
    Keine = 0,
    Netz = 1,
    Durchsage = 2,
}

/// <summary>Art im Stempel runter (5.5), allein vom Server bestimmt.</summary>
public enum Stempelart : byte
{
    Keine = 0,
    Netz = 1,
    Durchsage = 2,
    Global = 3,
}

/// <summary>Kennzeichen-Byte (5.6). Bits 4-7 sind reserviert und werden beim Weiterleiten 0.</summary>
[Flags]
public enum Paketkennzeichen : byte
{
    Keine = 0,
    Beginn = 0x01,
    Ende = 0x02,
    Preroll = 0x04,
    Va = 0x08,
}

/// <summary>Netztyp (4.5 NetzListe). Genau ein Netz ist GLOBAL, und zwar Netz 1.</summary>
public enum NetzTyp : byte
{
    Normal = 0,
    Global = 1,
}

/// <summary>Selbstwahl eines Netzes durch Funker (4.5 NetzListe <c>offen</c>).</summary>
public enum Offenheit : byte
{
    Nein = 0,
    Hoeren = 1,
    HoerenSenden = 2,
}

/// <summary>Voreinstellung des Auslösers je Netz im Client (A20).</summary>
public enum Ausloeser : byte
{
    Taste = 0,
    Va = 1,
}

/// <summary>Art einer Einheit (A39, 4.5 NetzListe <c>schiffe[].art</c>): Schiff, Trupp (Infanterie) oder Orga-Einheit.</summary>
public enum Einheitsart : byte
{
    Schiff = 0,
    Trupp = 1,
    Orga = 2,
}

/// <summary>Anlass einer Folge der Beauftragung (6.12, Meldung BEAUFTRAGUNG <c>details.op</c>).</summary>
public enum BeauftragungsAnlass : byte
{
    Erteilt = 0,
    Begonnen,
    Entzogen,
    Abgegeben,
    Beendet,
    Abgelaufen,
}

/// <summary>Anlass einer Folge der Kanalsperren (6.17, Meldung KANALSPERRE <c>details.op</c>).</summary>
public enum KanalsperrAnlass : byte
{
    Gesetzt = 0,
    Aufgehoben,
    Abgelaufen,
    Beendet,
}

/// <summary>Zustand eines Platzes (6.9).</summary>
public enum PlatzZustand : byte
{
    Frei = 0,
    Belegt = 1,
    Reserviert = 2,
}

/// <summary>Herkunft des aktuellen Stands (4.5 Mitgliedschaften <c>quelle</c>).</summary>
public enum Quelle : byte
{
    Standard = 0,
    Rolle,
    Selbst,
    Verschieben,
    Aufstellung,
    Wiederhergestellt,
    Satz,
}

/// <summary>Codes in SendeFehler (4.5, 6.3).</summary>
public enum SendeFehlerCode : byte
{
    Keiner = 0,
    KeinSenderecht,
    ZielEntzogen,
    NurTaste,
    KeinRechtGlobal,
    KeinRechtDurchsage,
    MehrfachNichtErlaubt,
    Stummgeschaltet,
    UnbekanntesNetz,
    Ungueltig,
    Grenze,
}

/// <summary>Fehlercodes des Zustandsschreibers, Teilmenge aus 4.9.</summary>
public enum Fehlercode : byte
{
    Keiner = 0,
    Ungueltig,
    Unbekannt,
    KeinRecht,
    Veraltet,
    PlatzBelegt,
    SelbstwahlGesperrt,
    FeldFest,
    GlobalPflicht,
    NurAdminWerkzeug,
    KontoGesperrt,
    VerdraengenGesperrt,
    Intern,
}

/// <summary>Modus von AufstellungAnwenden (4.5).</summary>
public enum Aufstellungsmodus : byte
{
    Ergaenzen = 0,
    Ersetzen = 1,
}

/// <summary>Operationen von KontoVerwaltung (4.5, 6.10).</summary>
public enum KontoOperation : byte
{
    Sperren = 0,
    Entsperren,
    Stufe,
    Rechte,
    Name,
    Loeschen,
}

/// <summary>Arten von Meldung (4.5), soweit sie aus diesem Baustein entstehen.</summary>
public enum MeldungsArt : byte
{
    Rate = 0,
    VerdraengungHaeufig,
    RechteGeaendert,
    AufstellungNachgezogen,
}

/// <summary>Warum eine Sitzung durch eine Änderung am Zustand endet.</summary>
public enum Sitzungsende : byte
{
    /// <summary>Bye{VERDRAENGT}: neues Gerät desselben Kontos (4.6).</summary>
    Verdraengt = 0,

    /// <summary>Bye{ERSETZT}: dasselbe Gerät verbindet neu (4.6).</summary>
    Ersetzt,

    /// <summary>Fehler{KONTO_GESPERRT} und Trennung (3.6).</summary>
    KontoGesperrt,

    /// <summary>Konto gelöscht (3.6).</summary>
    KontoGeloescht,
}

/// <summary>Drahtnamen der Codes und Arten, wie sie im Steuerkanal stehen.</summary>
public static class Drahtnamen
{
    public static string Von(SendeFehlerCode code) => code switch
    {
        SendeFehlerCode.KeinSenderecht => "KEIN_SENDERECHT",
        SendeFehlerCode.ZielEntzogen => "ZIEL_ENTZOGEN",
        SendeFehlerCode.NurTaste => "NUR_TASTE",
        SendeFehlerCode.KeinRechtGlobal => "KEIN_RECHT_GLOBAL",
        SendeFehlerCode.KeinRechtDurchsage => "KEIN_RECHT_DURCHSAGE",
        SendeFehlerCode.MehrfachNichtErlaubt => "MEHRFACH_NICHT_ERLAUBT",
        SendeFehlerCode.Stummgeschaltet => "STUMMGESCHALTET",
        SendeFehlerCode.UnbekanntesNetz => "UNBEKANNTES_NETZ",
        SendeFehlerCode.Ungueltig => "UNGUELTIG",
        SendeFehlerCode.Grenze => "GRENZE",
        _ => "",
    };

    public static string Von(Fehlercode code) => code switch
    {
        Fehlercode.Ungueltig => "UNGUELTIG",
        Fehlercode.Unbekannt => "UNBEKANNT",
        Fehlercode.KeinRecht => "KEIN_RECHT",
        Fehlercode.Veraltet => "VERALTET",
        Fehlercode.PlatzBelegt => "PLATZ_BELEGT",
        Fehlercode.SelbstwahlGesperrt => "SELBSTWAHL_GESPERRT",
        Fehlercode.FeldFest => "FELD_FEST",
        Fehlercode.GlobalPflicht => "GLOBAL_PFLICHT",
        Fehlercode.NurAdminWerkzeug => "NUR_ADMIN_WERKZEUG",
        Fehlercode.KontoGesperrt => "KONTO_GESPERRT",
        Fehlercode.VerdraengenGesperrt => "VERDRAENGEN_GESPERRT",
        Fehlercode.Intern => "INTERN",
        _ => "",
    };

    public static string Von(Stempelart art) => art switch
    {
        Stempelart.Netz => "NETZ",
        Stempelart.Durchsage => "DURCHSAGE",
        Stempelart.Global => "GLOBAL",
        _ => "",
    };

    public static string Von(Quelle quelle) => quelle switch
    {
        Quelle.Standard => "STANDARD",
        Quelle.Rolle => "ROLLE",
        Quelle.Selbst => "SELBST",
        Quelle.Verschieben => "VERSCHIEBEN",
        Quelle.Aufstellung => "AUFSTELLUNG",
        Quelle.Wiederhergestellt => "WIEDERHERGESTELLT",
        Quelle.Satz => "SATZ",
        _ => "",
    };

    public static string Von(BeauftragungsAnlass anlass) => anlass switch
    {
        BeauftragungsAnlass.Erteilt => "ERTEILT",
        BeauftragungsAnlass.Begonnen => "BEGONNEN",
        BeauftragungsAnlass.Entzogen => "ENTZOGEN",
        BeauftragungsAnlass.Abgegeben => "ABGEGEBEN",
        BeauftragungsAnlass.Beendet => "BEENDET",
        BeauftragungsAnlass.Abgelaufen => "ABGELAUFEN",
        _ => "",
    };

    public static string Von(KanalsperrAnlass anlass) => anlass switch
    {
        KanalsperrAnlass.Gesetzt => "GESETZT",
        KanalsperrAnlass.Aufgehoben => "AUFGEHOBEN",
        KanalsperrAnlass.Abgelaufen => "ABGELAUFEN",
        KanalsperrAnlass.Beendet => "BEENDET",
        _ => "",
    };

    public static string Von(MeldungsArt art) => art switch
    {
        MeldungsArt.Rate => "RATE",
        MeldungsArt.VerdraengungHaeufig => "VERDRAENGUNG_HAEUFIG",
        MeldungsArt.RechteGeaendert => "RECHTE_GEAENDERT",
        MeldungsArt.AufstellungNachgezogen => "AUFSTELLUNG_NACHGEZOGEN",
        _ => "",
    };
}
