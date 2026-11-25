// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Text.Json;
using System.Text.Unicode;

namespace Funk.Protokoll.Steuerung;

/// <summary>
/// Strenger Vorlauf über einen JSON-Body vor der Deserialisierung (4.3). Er trennt die beiden Fehlerklassen:
/// <list type="bullet">
/// <item><c>PROTOKOLL</c> (Trennung): kein gültiges UTF-8, nicht wohlgeformt, Kommentare, abschließende Kommas,
/// Tiefe über 8, doppelte Schlüssel (auch unbekannte), oberste Ebene kein Objekt, Reste nach dem Objekt.</item>
/// <item><c>UNGUELTIG</c> (ohne Trennung): verbotene Zeichen oder nicht NFC in Zeichenketten und Schlüsseln,
/// einsame Surrogate, Zahlen mit Betrag über 2^53-1 oder nicht endlich.</item>
/// </list>
/// Der Deserialisierer von System.Text.Json erkennt doppelte Schlüssel nur bei bekannten Feldern und prüft
/// UTF-8 erst beim Umwandeln (gemessen unter .NET 10.0.12); deshalb dieser eigene Durchgang.
/// </summary>
public static class JsonVorpruefung
{
    /// <summary>Höchste Schachtelungstiefe (Objekte und Felder zusammen).</summary>
    public const int MaxTiefe = 8;

    /// <summary>Größte zulässige Ganzzahl (2^53-1).</summary>
    public const long MaxGanzzahl = (1L << 53) - 1;

    private static readonly JsonReaderOptions LeseOptionen = new()
    {
        MaxDepth = MaxTiefe,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        AllowMultipleValues = false,
    };

    /// <summary>Lese-Optionen für alle weiteren Leser des Steuerkanals.</summary>
    public static JsonReaderOptions Optionen => LeseOptionen;

    /// <summary>Prüft einen Body. <c>null</c> heißt bestanden.</summary>
    public static Fehlercode? Pruefen(ReadOnlySpan<byte> body)
    {
        if (!Utf8.IsValid(body)) return Fehlercode.Protokoll;

        Fehlercode? inhalt = null;
        var reader = new Utf8JsonReader(body, LeseOptionen);
        // Je Tiefe eine wiederverwendete Namensmenge; Tiefe 0 ist das oberste Objekt.
        var namen = new HashSet<string>?[MaxTiefe + 1];
        byte[]? miete = null;
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return Fehlercode.Protokoll;
            Oeffnen(namen, reader.CurrentDepth);
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        Oeffnen(namen, reader.CurrentDepth);
                        break;
                    case JsonTokenType.PropertyName:
                        if (!SchluesselLesen(ref reader, out var name)) inhalt ??= Fehlercode.Ungueltig;
                        if (!namen[reader.CurrentDepth]!.Add(name)) return Fehlercode.Protokoll;
                        if (inhalt is null && !Textregeln.Pruefen(name)) inhalt = Fehlercode.Ungueltig;
                        break;
                    case JsonTokenType.String:
                        if (inhalt is null && !ZeichenketteGilt(ref reader, ref miete)) inhalt = Fehlercode.Ungueltig;
                        break;
                    case JsonTokenType.Number:
                        if (inhalt is null && !ZahlGilt(ref reader)) inhalt = Fehlercode.Ungueltig;
                        break;
                    default:
                        break;
                }
            }
            return inhalt;
        }
        catch (JsonException)
        {
            return Fehlercode.Protokoll;
        }
        finally
        {
            if (miete is not null) ArrayPool<byte>.Shared.Return(miete, clearArray: true);
        }
    }

    /// <summary>
    /// Ein Objekt auf Tiefe d hat seine Schlüssel auf Tiefe d+1 (so zählt <see cref="Utf8JsonReader.CurrentDepth"/>).
    /// </summary>
    private static void Oeffnen(HashSet<string>?[] namen, int objektTiefe)
    {
        var menge = namen[objektTiefe + 1] ??= new HashSet<string>(StringComparer.Ordinal);
        menge.Clear();
    }

    /// <summary>
    /// Liest einen Schlüssel unmaskiert. Ein einsames Surrogat ergibt <c>false</c>; dann dient die Rohform als
    /// Schlüssel für die Doppelprüfung, damit ein späterer Formfehler weiter als <c>PROTOKOLL</c> erkannt wird.
    /// </summary>
    private static bool SchluesselLesen(ref Utf8JsonReader reader, out string name)
    {
        if (!reader.ValueIsEscaped)
        {
            name = System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
            return true;
        }
        try
        {
            name = reader.GetString()!;
            return true;
        }
        catch (InvalidOperationException)
        {
            name = "\0roh:" + System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
            return false;
        }
    }

    private static bool ZeichenketteGilt(ref Utf8JsonReader reader, ref byte[]? miete)
    {
        // Gelesen wird immer aus einem zusammenhängenden Span, also nie ValueSequence.
        var roh = reader.ValueSpan;
        if (!reader.ValueIsEscaped) return Textregeln.Pruefen(roh);

        // Unmaskieren wird nur kürzer; der Rohwert ist also eine obere Schranke.
        if (miete is null || miete.Length < roh.Length)
        {
            if (miete is not null) ArrayPool<byte>.Shared.Return(miete, clearArray: true);
            miete = ArrayPool<byte>.Shared.Rent(Math.Max(roh.Length, 256));
        }
        int n;
        try
        {
            n = reader.CopyString(miete);
        }
        catch (InvalidOperationException)
        {
            return false; // einsames Surrogat
        }
        return Textregeln.Pruefen(miete.AsSpan(0, n));
    }

    private static bool ZahlGilt(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt64(out var ganz)) return ganz is >= -MaxGanzzahl and <= MaxGanzzahl;
        if (reader.TryGetDouble(out var d)) return double.IsFinite(d) && Math.Abs(d) <= MaxGanzzahl;
        return false;
    }
}
