// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json.Serialization;

namespace Funk.Client.Ablage;

/// <summary>
/// Das Spiel, über dem Sprecheranzeige, Meldeanzeige und Meldezeile stehen (Sarevoc, 18./19.10.2026, Q10; JSON <c>spiel</c>): ab Werk Star Citizen,
/// sonst ein anderes Spiel über den Namen seines Prozesses, also die Programmdatei ohne Pfad und ohne ".exe". Damit sucht der Client nur den
/// Monitor, auf dem das Spiel läuft; er liest nichts aus dem Spiel. Nachsichtig gelesen: Was kein Text ist, wird still <c>null</c>, ein ungültiger
/// Name ebenso, und ohne Namen gilt wieder Star Citizen. Ein älterer Client übergeht den Abschnitt.
/// </summary>
public sealed class SpielEinstellungen
{
    /// <summary>Ein anderes Spiel statt Star Citizen; ab Werk aus.</summary>
    [JsonConverter(typeof(NachsichtigAus))]
    public bool Anderes { get; set; }

    /// <summary>Der Prozessname des anderen Spiels ("Spiel" für Spiel.exe), oder <c>null</c>; <c>null</c> wird nicht geschrieben.</summary>
    [JsonConverter(typeof(NachsichtigerText))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Prozess { get; set; }

    internal void Normalisieren() => Prozess = Spielprofil.Prozessname(Prozess);
}

/// <summary>Reine Rechnung zum Spielprofil (<see cref="SpielEinstellungen"/>): Prozessname aus einer Eingabe, das wirksame Spiel, sein Name.</summary>
public static class Spielprofil
{
    /// <summary>Der Name des Spiels ab Werk.</summary>
    public const string StarCitizenName = "Star Citizen";

    /// <summary>Der Prozessname von Star Citizen, genau (StarCitizen_Launcher zählt nicht).</summary>
    public const string StarCitizenProzess = "StarCitizen";

    /// <summary>Höchstlänge eines Prozessnamens (Windows erlaubt längere Dateinamen; ein Spiel braucht sie nicht).</summary>
    public const int ProzessnameMax = 64;

    /// <summary>
    /// Der Prozessname aus einer Eingabe: eine Programmdatei mit Pfad ("C:\Spiele\X\Spiel.exe"), ein Dateiname ("Spiel.exe") oder schon der
    /// Name ("Spiel"). Pfad und ".exe" fallen weg, Leerzeichen am Rand auch. Leer, zu lang oder mit Zeichen, die in keinem Dateinamen stehen
    /// dürfen: <c>null</c>.
    /// </summary>
    public static string? Prozessname(string? eingabe)
    {
        if (string.IsNullOrWhiteSpace(eingabe)) return null;
        var t = eingabe.Trim().Trim('"').Trim();
        var schnitt = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        if (schnitt >= 0) t = t[(schnitt + 1)..];
        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t[..^4];
        t = t.Trim();
        if (t.Length is 0 or > ProzessnameMax) return null;
        foreach (var c in t)
            if (char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|') return null;
        return t;
    }

    /// <summary>Der Prozessname, nach dem der Client sucht: Star Citizen ab Werk und solange beim anderen Spiel kein Name steht.</summary>
    public static string Prozess(SpielEinstellungen e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.Anderes && e.Prozess is { } p ? p : StarCitizenProzess;
    }

    /// <summary>Wie das Spiel in Sätzen heißt: "Star Citizen" oder die Programmdatei des anderen Spiels ("Spiel.exe").</summary>
    public static string Name(SpielEinstellungen e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.Anderes && e.Prozess is { } p ? p + ".exe" : StarCitizenName;
    }
}
