// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Server.Betrieb;

/// <summary>
/// Dateien im Zustandsverzeichnis mit atomarem Ersetzen (Offener Punkt 7: eigene JSON-Dateien): erst in eine
/// Nachbardatei schreiben, auf die Platte zwingen, dann per Umbenennen ersetzen. Ein Absturz mitten im Schreiben
/// hinterlässt so entweder den alten oder den neuen Stand, nie einen halben.
/// </summary>
public static class Dateiablage
{
    /// <summary>Endung der Zwischendatei beim Schreiben.</summary>
    public const string Zwischenendung = ".neu";

    /// <summary>
    /// Schreibt <paramref name="inhalt"/> atomar nach <paramref name="pfad"/>. <paramref name="nurBesitzer"/>: unter
    /// Linux Modus 0600 (Schlüssel, Geheimnis, personenbezogene Stände). Mit <paramref name="ersetzen"/> = <c>false</c> bleibt
    /// eine vorhandene Datei unangetastet, und es kommt <see cref="SchonDaException"/>: So tauscht ein zweiter Aufruf von
    /// <c>funk-admin schluessel-erzeugen</c> nicht still Serverschlüssel, Geheimnis oder TOTP-Schlüssel aus (bis 01.10.2026 tat
    /// er das).
    /// </summary>
    public static void AtomarSchreiben(string pfad, ReadOnlySpan<byte> inhalt, bool nurBesitzer = true, bool ersetzen = true)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        var voll = Path.GetFullPath(pfad);
        var verzeichnis = Path.GetDirectoryName(voll) ?? throw new ArgumentException("Pfad ohne Verzeichnis.", nameof(pfad));
        if (!ersetzen && File.Exists(voll)) throw new SchonDaException(voll);
        Directory.CreateDirectory(verzeichnis);
        var zwischen = voll + Zwischenendung;
        // Eine liegengebliebene Zwischendatei erst entfernen, dann neu anlegen (CreateNew). Mit FileMode.Create schriebe root durch
        // eine untergeschobene Zwischendatei hindurch, die ein Verweis auf eine fremde Datei ist (umzug-einspielen schreibt als root
        // nach /var/lib/funk, das dem Dienstbenutzer gehört), und eine vorhandene behielte ihre alten Rechte statt 0600.
        File.Delete(zwischen);
        var optionen = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough,
        };
        if (nurBesitzer && !OperatingSystem.IsWindows()) optionen.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var strom = new FileStream(zwischen, optionen))
        {
            strom.Write(inhalt);
            strom.Flush(flushToDisk: true);
        }
        try
        {
            File.Move(zwischen, voll, overwrite: ersetzen);
        }
        catch (IOException) when (!ersetzen && File.Exists(voll))
        {
            // Zwischen Prüfung und Umbenennen ist jemand zuvorgekommen: die fremde Datei bleibt.
            File.Delete(zwischen);
            throw new SchonDaException(voll);
        }
    }

    /// <summary>Legt einen Pfad an, ohne zu ersetzen: <see cref="SchonDaException"/>, wenn es ihn schon gibt.</summary>
    public static void NeuSchreiben(string pfad, ReadOnlySpan<byte> inhalt, bool nurBesitzer = true) =>
        AtomarSchreiben(pfad, inhalt, nurBesitzer, ersetzen: false);

    /// <summary>Liest eine Datei, falls es sie gibt. Eine liegengebliebene Zwischendatei wird verworfen.</summary>
    public static byte[]? LesenFallsVorhanden(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        var zwischen = Path.GetFullPath(pfad) + Zwischenendung;
        if (File.Exists(zwischen)) File.Delete(zwischen);
        return File.Exists(pfad) ? File.ReadAllBytes(pfad) : null;
    }

    /// <summary>Legt ein Verzeichnis an; unter Linux nur für den Besitzer zugänglich (0700).</summary>
    public static void VerzeichnisAnlegen(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(pfad);
            return;
        }
        Directory.CreateDirectory(pfad, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}

/// <summary>Die Datei gibt es schon; nichts wurde geschrieben (<see cref="Dateiablage.AtomarSchreiben"/> ohne Ersetzen).</summary>
public sealed class SchonDaException : IOException
{
    public SchonDaException()
    {
    }

    public SchonDaException(string pfad) : base(pfad + " gibt es schon.") => Pfad = pfad;

    public SchonDaException(string meldung, Exception innen) : base(meldung, innen)
    {
    }

    /// <summary>Der vorhandene Pfad.</summary>
    public string? Pfad { get; }
}
