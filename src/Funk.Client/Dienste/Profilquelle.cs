// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using System.Reflection;
using Funk.Client.Ablage;

namespace Funk.Client.Dienste;

/// <summary>
/// Serverprofil und Profilschlüssel, die mit dem Client kommen (3.1, ANMELDUNG-SICHERHEIT P1 und "Was an der normalen
/// Anmeldung sicher ist"): Die öffentlichen Profilschlüssel stehen beim Bau in TacRelay.dll (MSBuild-Eigenschaft
/// <c>FunkProfilschluessel</c> oder die Datei <c>konfig\profilschluessel.txt</c>, siehe Funk.Client.csproj), das signierte
/// Profil <c>profil.json</c> liegt im Programmordner, gebracht vom Setup. Eine Eingabe gibt es weder in der Oberfläche noch
/// in der Einstellungsdatei, und auch keinen Entwicklermodus, der sie öffnet: Einen Schalter, den ein Betrüger erklären kann,
/// gibt es nicht. Für Entwicklung und Probefunk bleibt der Testzugang auf Loopback, der kein Profil braucht.
/// <para>
/// <see cref="Ablage"/> (F1, EINRICHTEN-UMZUG-ENTWURF): Ein neueres Profil, das der Server nach der Freigabe in
/// <c>Welcome.profil</c> durchreicht und das mit einem eingebauten Schlüssel signiert ist, legt der Client als
/// <c>serverprofil.json</c> neben seine Einstellungen (<see cref="Profiluebernahme"/>). Gelesen wird beides bei jedem Gebrauch
/// neu und geprüft wie das Setup-Profil; es gilt das höchste gültige (<see cref="Profilpruefung.Waehlen"/>).
/// </para>
/// </summary>
public sealed record Profilquelle(string? Datei, IReadOnlyList<string> Schluessel, string? Ablage = null)
{
    public const string Dateiname = "profil.json";

    /// <summary>Name der Ablage neben <c>client-einstellungen.json</c> (<see cref="EinstellungsSpeicher.Dateiname"/>).</summary>
    public const string AblageDateiname = "serverprofil.json";

    /// <summary>Schlüssel der Metadaten in TacRelay.dll, beim Bau geschrieben.</summary>
    public const string Metadatenname = "FunkProfilschluessel";

    /// <summary>Was diese Fassung mitbringt: Profil aus dem Programmordner, Schlüssel aus der Programmdatei, Ablage neben den Einstellungen.</summary>
    public static Profilquelle Eingebaut { get; } =
        new(Path.Combine(AppContext.BaseDirectory, Dateiname), AusMetadaten(typeof(Profilquelle).Assembly), AblageNeben(EinstellungsSpeicher.StandardPfad()));

    /// <summary>Diese Fassung kennt mindestens einen Profilschlüssel.</summary>
    public bool HatSchluessel => Schluessel.Count > 0;

    /// <summary>Pfad der Ablage neben einer Einstellungsdatei: <c>serverprofil.json</c> im selben Ordner.</summary>
    public static string AblageNeben(string einstellungsdatei)
    {
        ArgumentNullException.ThrowIfNull(einstellungsdatei);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(einstellungsdatei)) ?? ".", AblageDateiname);
    }

    /// <summary>
    /// Die gültigen Profilschlüssel aus den Metadaten einer Programmdatei; getrennt durch Leerraum, Komma oder Semikolon.
    /// Was kein gültiger P-256-Schlüssel ist, fällt weg.
    /// </summary>
    public static IReadOnlyList<string> AusMetadaten(Assembly programm)
    {
        ArgumentNullException.ThrowIfNull(programm);
        return [.. programm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(m => string.Equals(m.Key, Metadatenname, StringComparison.Ordinal))
            .SelectMany(m => (m.Value ?? "").Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
            .Select(s => ProfilschluesselText.Pruefen(s))
            .Where(e => e.Gueltig)
            .Select(e => e.Spki!)
            .Distinct(StringComparer.Ordinal)];
    }
}
