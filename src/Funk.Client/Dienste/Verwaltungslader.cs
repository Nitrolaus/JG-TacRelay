// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Funk.ClientKern;

namespace Funk.Client.Dienste;

/// <summary>Was das Laden des Verwaltungsteils ergab: der Teil, oder warum nicht (<c>null</c>: nicht installiert, der Normalfall).</summary>
public sealed record Verwaltungsladung(IVerwaltungsteil? Teil, string? Hinweis);

/// <summary>
/// Lädt den Verwaltungsteil (A45, ANMELDUNG-SICHERHEIT "Admin-Teile nur für Admins"), wenn das Admin-Setup ihn nach
/// <c>verwaltung\Funk.Verwaltung.dll</c> gelegt hat, und nur, wenn sein SHA-256 zu der Liste passt, die beim Bau in
/// TacRelay.dll geschrieben wurde (Metadaten <c>VerwaltungSha256</c>, Funk.Client.csproj). Eine fremde oder alte Datei
/// wird nicht geladen. Geladen werden genau die geprüften Bytes, nicht die Datei ein zweites Mal. Das schützt vor
/// vertauschten Fassungen, nicht vor Angreifern: Den Programmordner kann jeder Schadcode im Benutzerkonto samt Programmdatei
/// austauschen. Durchgesetzt wird allein am Server.
/// </summary>
public static class Verwaltungslader
{
    public const string Ordner = "verwaltung";
    public const string Datei = "Funk.Verwaltung.dll";
    public const string Metadatenname = "VerwaltungSha256";

    /// <summary>Höchstgröße der Datei; alles darüber ist sicher kein Verwaltungsteil.</summary>
    public const long MaxBytes = 16 * 1024 * 1024;

    public const string PasstNicht = "Verwaltungsteil passt nicht zu dieser Fassung - das Admin-Setup dieser Fassung ausführen.";

    /// <summary>Die SHA-256 (hex, klein) der Verwaltungsteile, die zu dieser Fassung passen.</summary>
    public static IReadOnlyList<string> Passende(Assembly programm)
    {
        ArgumentNullException.ThrowIfNull(programm);
        return [.. programm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(m => string.Equals(m.Key, Metadatenname, StringComparison.Ordinal))
            .SelectMany(m => (m.Value ?? "").Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries))
            .Where(h => h.Length == 64 && h.All(char.IsAsciiHexDigit))
            .Select(h => h.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Sucht, prüft und lädt den Verwaltungsteil unter <paramref name="programmordner"/>. Ohne Ordner kein Teil und kein
    /// Hinweis; mit falscher Prüfsumme kein Teil und <see cref="PasstNicht"/>.
    /// </summary>
    /// <param name="laden">Lädt die geprüften Bytes; ohne Angabe in den Standard-Ladekontext (Tests geben das schon geladene).</param>
    public static Verwaltungsladung Laden(string programmordner, IReadOnlyList<string> passende, IVerwaltungsWirt wirt, Func<byte[], Assembly>? laden = null)
    {
        ArgumentNullException.ThrowIfNull(programmordner);
        ArgumentNullException.ThrowIfNull(passende);
        ArgumentNullException.ThrowIfNull(wirt);
        var pfad = Path.Combine(programmordner, Ordner, Datei);
        byte[] bytes;
        try
        {
            if (!File.Exists(pfad)) return new Verwaltungsladung(null, null);
            if (new FileInfo(pfad).Length > MaxBytes) return new Verwaltungsladung(null, PasstNicht);
            bytes = File.ReadAllBytes(pfad);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Verwaltungsladung(null, "Verwaltungsteil lässt sich nicht lesen: " + ex.Message);
        }
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!passende.Contains(sha, StringComparer.Ordinal)) return new Verwaltungsladung(null, PasstNicht);
        try
        {
            var assembly = (laden ?? (b => AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(b))))(bytes);
            if (assembly.GetCustomAttribute<VerwaltungsteilAttribute>() is not { } a
                || !typeof(IVerwaltungsteilFabrik).IsAssignableFrom(a.Einstieg)
                || Activator.CreateInstance(a.Einstieg) is not IVerwaltungsteilFabrik fabrik)
                return new Verwaltungsladung(null, PasstNicht);
            return new Verwaltungsladung(fabrik.Erzeugen(wirt), null);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or TargetInvocationException or TypeLoadException
                                       or MissingMethodException or InvalidOperationException)
        {
            return new Verwaltungsladung(null, "Verwaltungsteil ließ sich nicht laden: " + ex.Message);
        }
    }
}

/// <summary>Der Wirt des Verwaltungsteils über dem Funkbetrieb: der laufende Client und der Oberflächentakt.</summary>
public sealed class VerwaltungsWirt : IVerwaltungsWirt
{
    private readonly FunkClientDienst _dienst;
    private readonly IOberflaechentakt _takt;

    /// <param name="ablageordner">Ordner für Sicherungen und Entwürfe der Flotte; beim Programm neben den Einstellungen.</param>
    public VerwaltungsWirt(FunkClientDienst dienst, IOberflaechentakt takt, string? ablageordner = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(takt);
        _dienst = dienst;
        _takt = takt;
        Ablageordner = ablageordner;
        _dienst.LageGeaendert += () => Geaendert?.Invoke();
    }

    public FunkClient? Client => _dienst.AktiverClient;

    public event Action? Geaendert;

    public void Einreihen(Action arbeit) => _takt.Einreihen(arbeit);

    public string? Ablageordner { get; }
}
