// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Client.Dienste;

/// <summary>
/// Kurze Fehlernummern zum Weitersagen (Sarevoc, Grilling Q5): Die Meldung für Funker beschreibt die Lage und endet mit
/// " (Fehler NN)"; Adresse, Port, Pfad und Meldung von Windows stehen nur in der Einzelheit. Nummernkreise: 01-09 Serverprofil,
/// 11-19 Verbindungsaufbau, 21-29 Verbindung verloren oder getrennt, 31-39 Server lehnt ab, 41-49 interne Fehler. Normale
/// Zustände (selbst getrennt, Wartung, verdrängt, ersetzt) haben keine Nummer. Die Bedeutungen für den Admin stehen in der Tabelle der Tests (FehlernummernTabelle); der Client selbst braucht sie nicht.
/// </summary>
public static class Fehlernummern
{
    public const string ProfilFehlt = "01";
    public const string ProfildateiFehlt = "02";
    public const string ProfilZuGross = "03";
    public const string ProfilUnlesbar = "04";
    public const string ProfilUnsigniert = "05";
    public const string ProfilKeinProfil = "06";
    public const string ProfilschluesselFehlt = "07";
    public const string ProfilInhaltUngueltig = "08";
    public const string ProfilFremd = "09";

    public const string ServerNichtErreichbar = "11";
    public const string TlsGescheitert = "12";
    public const string KeinFunkServer = "13";
    public const string FremderServer = "14";
    public const string NichtVertrauenswuerdig = "15";
    public const string UnbekannterSchluessel = "16";
    public const string EinstellungUngueltig = "17";
    public const string GeraeteschluesselFehlt = "18";

    public const string KeineAntwort = "21";
    public const string VerbindungVerloren = "22";
    public const string VerbindungGetrennt = "23";
    public const string ServerBeendet = "24";

    public const string VersionZuAlt = "31";
    public const string VersionPasstNicht = "32";
    public const string KontoGesperrt = "33";
    public const string GeraetGesperrt = "34";
    public const string GeraetEntfernt = "35";
    public const string WechselGesperrt = "36";
    public const string ServerVoll = "37";
    public const string Protokollfehler = "38";
    public const string Abgelehnt = "39";

    public const string InternVerbinden = "41";
    public const string InternAnmeldung = "42";
    public const string InternSitzung = "43";
    public const string InternVorbereitung = "44";

    /// <summary>Hängt " (Fehler NN)" an den Satz; ohne Nummer bleibt er, wie er ist.</summary>
    public static string Anhaengen(string satz, string? nummer) => nummer is null ? satz : satz + " (Fehler " + nummer + ")";
}
