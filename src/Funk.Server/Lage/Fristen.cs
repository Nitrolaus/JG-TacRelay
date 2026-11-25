// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Server.Lage;

/// <summary>Zeiten und Grenzen aus Kapitel 6 und 4.5, alle in Millisekunden.</summary>
public static class Fristen
{
    /// <summary>Wiederverbindungsfrist nach Sitzungsende (6.9).</summary>
    public const long WiederverbindungMs = 10 * 60 * 1000;

    /// <summary>Gültigkeit einer Reservierung durch ZUWEISEN oder Aufstellung (6.9).</summary>
    public const long ReservierungMs = 12L * 60 * 60 * 1000;

    /// <summary>Nachziehen offener Aufstellungseinträge (6.9).</summary>
    public const long NachziehenMs = 12L * 60 * 60 * 1000;

    /// <summary>ZIEL_ENTZOGEN statt KEIN_SENDERECHT, wenn die Umstellung so kurz zurückliegt (6.3).</summary>
    public const long EntzugMs = 10_000;

    /// <summary>Eine Sprechphase ist offen bis ENDE oder so lange ohne Paket (6.4).</summary>
    public const long PhaseOffenMs = 2_000;

    /// <summary>DurchsageEnde nach so langer Pause einer Durchsage- oder Global-Phase (6.4).</summary>
    public const long DurchsagePauseMs = 300;

    /// <summary>Höchstens ein SendeFehler je Sitzung in diesem Abstand, außer ZIEL_ENTZOGEN (4.5).</summary>
    public const long SendeFehlerAbstandMs = 1_000;

    /// <summary>Höchstens eine Meldung RATE je Sender in diesem Abstand (Festlegung dieses Bausteins).</summary>
    public const long RateMeldungAbstandMs = 1_000;

    /// <summary>Fenster für häufiges Verdrängen (4.6).</summary>
    public const long VerdraengungFensterMs = 5 * 60 * 1000;

    /// <summary>Sperre nach häufigem Verdrängen, bis ein Admin entscheidet (4.6).</summary>
    public const long VerdraengungSperreMs = 30 * 60 * 1000;

    /// <summary>Ein Auftrag beginnt höchstens so weit in der Zukunft (6.12 Regel 4).</summary>
    public const long AuftragVorausMs = 14L * 24 * 60 * 60 * 1000;

    /// <summary>Ein Auftrag dauert höchstens so lange (6.12 Regel 4).</summary>
    public const long AuftragDauerMs = 24L * 60 * 60 * 1000;

    /// <summary>Höchstens so viele laufende oder geplante Aufträge gleichzeitig (6.12 Regel 4).</summary>
    public const int AuftraegeMax = 4;

    /// <summary>Anlass eines Auftrags in Skalarwerten.</summary>
    public const int AuftragGrundMax = 48;

    /// <summary>Eine Sperre der Selbstwahl endet so lange nach dem Setzen von selbst (F9, 6.9).</summary>
    public const long SelbstwahlSperreMs = 12L * 60 * 60 * 1000;

    /// <summary>Typ einer Einheit ("Hammerhead") in Skalarwerten (F5).</summary>
    public const int EinheitTypMax = 32;

    /// <summary>Schritte je Verwaltungspaket (N5).</summary>
    public const int PaketSchritteMax = 200;

    /// <summary>Rückfall auf die Grundstellung so lange nach dem letzten Phasenwechsel (6.14 Regel 12).</summary>
    public const long PhaseRueckfallMs = 12L * 60 * 60 * 1000;

    /// <summary>Höchste Phasennummer (6.14): 1 ist die Grundstellung, dazu höchstens <see cref="PhasenMax"/> Phasen.</summary>
    public const byte PhaseNrMax = 5;

    /// <summary>Phasen über der Grundstellung.</summary>
    public const int PhasenMax = 4;

    /// <summary>Name und Kurzname einer Phase in Skalarwerten.</summary>
    public const int PhaseNameMax = 24;
    public const int PhaseKurzMax = 12;

    /// <summary>Variantenfelder über alle Phasen zusammen.</summary>
    public const int PhasenFelderMax = 512;

    /// <summary>Höchstdauer des Funker-Modus (6.15); ohne Angabe gilt das nächste 04:00 Ortszeit, das immer davor liegt.</summary>
    public const long FunkerModusMaxMs = 24L * 60 * 60 * 1000;

    /// <summary>Höchstdauer einer Stummschaltung (4.5).</summary>
    public const int StummMaxSekunden = 3600;

    /// <summary>Sprachgrenzen je Sitzung (4.8, 6.3).</summary>
    public const int PaketeJeSekunde = 60;
    public const int PaketeBurst = 20;
    public const int OpusBytesJeSekunde = 16_000;
    public const int OpusBytesBurst = 4_000;

    /// <summary>Grenzen aus 1.2 und 5.4.</summary>
    public const int OpusMax = 400;
    public const int ZieleNetzMax = 16;
    public const int ZieleDurchsageMax = 64;
    public const int FelderMax = 16;
    public const int NetzeMax = 128;
    public const int SchiffeMax = 64;
    public const int RollenMax = 64;
    public const int KennungenJeSchiffMax = 8;
    public const int PlaetzeJeSchiffMax = 16;
    public const int AenderungenMax = 200;
    public const int AufstellungEintraegeMax = 200;
    public const int SendeFehlerEintraegeMax = 17;
    public const int NameMax = 32;
    public const int RolleNameMax = 40;
    public const int HerkunftMax = 32;
    public const int AufstellungNameMax = 48;
}
