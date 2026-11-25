// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using System.Text.Json;
using Funk.Protokoll.Identitaet;

namespace Funk.Client.Ablage;

/// <summary>
/// Die Namen aus dem Setup (A42, ANMELDUNG-SICHERHEIT, Ablauf des Antrags, Schritt 1): Die Seite "Deine Namen" schreibt
/// <c>anmeldung.json</c> neben die Einstellungen, <c>{"scName": "Mustermann", "discordName": "Mustermann/Max"}</c>. Der erste
/// Start übernimmt sie in die Einstellungen, solange dort noch keine Namen stehen, und fragt dann "stimmt das?". Geheim ist
/// daran nichts. Gelesen wird nachsichtig (Byte-Reihenfolge-Marke, Leerraum), geprüft streng nach den Regeln des Servers:
/// Was nicht passt, fällt weg, und der Client fragt die Namen selbst.
/// </summary>
public static class Anmeldedatei
{
    public const string Dateiname = "anmeldung.json";

    /// <summary>Byte-Reihenfolge-Marke von UTF-8; Inno Setup schreibt sie mit SaveStringsToUTF8File.</summary>
    private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>Ein Setup schreibt nie mehr als ein paar hundert Byte.</summary>
    public const long MaxBytes = 4096;

    /// <summary>Wo das Setup die Datei ablegt: neben <paramref name="einstellungen"/>.</summary>
    public static string PfadNeben(string einstellungen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(einstellungen);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(einstellungen)) ?? ".", Dateiname);
    }

    /// <summary>Liest die Namen; <c>null</c>, wenn es die Datei nicht gibt oder sie nichts Brauchbares enthält.</summary>
    public static (string? ScName, string? DiscordName)? Lesen(string pfad)
    {
        try
        {
            if (!File.Exists(pfad) || new FileInfo(pfad).Length > MaxBytes) return null;
            var bytes = File.ReadAllBytes(pfad).AsSpan();
            if (bytes.StartsWith(Bom)) bytes = bytes[3..];
            using var doc = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var sc = Text(doc.RootElement, "scName");
            var discord = Text(doc.RootElement, "discordName");
            var ergebnis = (Namensregeln.ScName(sc), Namensregeln.DiscordName(discord));
            return ergebnis.Item1 is null && ergebnis.Item2 is null ? null : ergebnis;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Übernimmt die Namen aus dem Setup in <paramref name="anmeldung"/>, wenn dort noch keine stehen. Liefert, ob etwas
    /// übernommen wurde. Bestätigt sind sie damit nicht: Der Client fragt beim Antrag.
    /// </summary>
    public static bool Uebernehmen(AnmeldeEinstellungen anmeldung, string pfad)
    {
        ArgumentNullException.ThrowIfNull(anmeldung);
        if (anmeldung.ScName is not null || anmeldung.DiscordName is not null) return false;
        if (Lesen(pfad) is not { } namen) return false;
        anmeldung.ScName = namen.ScName;
        anmeldung.DiscordName = namen.DiscordName;
        anmeldung.Bestaetigt = false;
        return true;
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
}
