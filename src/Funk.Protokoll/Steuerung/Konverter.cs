// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Funk.Protokoll.Steuerung;

/// <summary>
/// Strenger Enum-Konverter für die Drahtform <c>GROSS_MIT_UNTERSTRICH</c> (4.3, 4.5): nur exakte Zeichenketten,
/// keine Zahlen, keine Groß-/Kleinschreibungs-Toleranz, keine Kommalisten. Der Drahtname entsteht aus dem
/// Membernamen per <see cref="JsonNamingPolicy.SnakeCaseUpper"/> (<c>HoerenSenden</c> -> <c>HOEREN_SENDEN</c>).
/// </summary>
public sealed class StrengeAufzaehlung<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly T[] Werte = Enum.GetValues<T>();
    private static readonly JsonEncodedText[] Namen = Array.ConvertAll(Werte,
        w => JsonEncodedText.Encode(JsonNamingPolicy.SnakeCaseUpper.ConvertName(w.ToString())));

    /// <summary>Drahtname eines Werts.</summary>
    internal static string Drahtname(T wert) => Namen[Index(wert)].Value;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Aufzählungswert muss eine Zeichenkette sein.");
        for (var i = 0; i < Namen.Length; i++)
        {
            if (reader.ValueTextEquals(Namen[i].EncodedUtf8Bytes)) return Werte[i];
        }
        throw new JsonException("Unbekannter Aufzählungswert.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(Namen[Index(value)]);
    }

    private static int Index(T wert)
    {
        var i = Array.IndexOf(Werte, wert);
        if (i < 0) throw new ArgumentOutOfRangeException(nameof(wert), "Wert gehört nicht zur Aufzählung.");
        return i;
    }
}

/// <summary>
/// Schreibt Zeichenketten nach 4.3 "Nicht-ASCII-Zeichen unmaskiert als UTF-8": maskiert werden nur <c>"</c>,
/// <c>\</c> und U+0000-U+001F. <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c> maskiert dagegen Zeichen
/// außerhalb der BMP (Emoji als <c>😀</c>, 12 statt 4 Byte), U+2028/U+2029, U+FEFF und nicht
/// zugewiesene Zeichen (gemessen unter .NET 10.0.12). Lesen bleibt beim Standard: beide Schreibweisen gelten.
/// </summary>
internal sealed class RoheZeichenkette : JsonConverter<string>
{
    private static ReadOnlySpan<byte> Hex => "0123456789abcdef"u8;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Zeichenkette erwartet.");
        return reader.GetString();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        var miete = ArrayPool<byte>.Shared.Rent(PufferGroesse(value));
        try
        {
            var n = Kodieren(value, miete);
            writer.WriteRawValue(miete.AsSpan(0, n), skipInputValidation: true);
        }
        finally
        {
            // Genullt: Die Zeichenkette kann ein Einmalcode sein (RegistrierungAntwort), und der Puffer geht an
            // den gemeinsamen Pool.
            ArrayPool<byte>.Shared.Return(miete, clearArray: true);
        }
    }

    /// <summary>
    /// Platzbedarf: vorn höchstens 3 Byte UTF-8 je UTF-16-Zeichen als Rohform, dahinter die maskierte Ausgabe mit
    /// höchstens 6 Byte je Rohbyte (Steuerzeichen) plus zwei Anführungszeichen.
    /// </summary>
    internal static int PufferGroesse(string wert) => checked(wert.Length * 3 + wert.Length * 6 + 2);

    public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() ?? throw new JsonException("Schlüssel fehlt.");

    public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value);
    }

    /// <summary>
    /// Kodiert <paramref name="wert"/> als JSON-Zeichenkette samt Anführungszeichen an den Anfang von
    /// <paramref name="puffer"/> (mindestens <see cref="PufferGroesse"/> Byte) und gibt die Länge zurück.
    /// </summary>
    internal static int Kodieren(string wert, Span<byte> puffer)
    {
        var rohbereich = wert.Length * 3;
        var roh = puffer[..rohbereich];
        var ziel = puffer[rohbereich..];
        var status = Utf8.FromUtf16(wert, roh, out _, out var geschrieben, replaceInvalidSequences: false);
        if (status != OperationStatus.Done) throw new JsonException("Zeichenkette enthält ungültiges UTF-16.");
        var quelle = roh[..geschrieben];
        var pos = 0;
        ziel[pos++] = (byte)'"';
        foreach (var b in quelle)
        {
            if (b == (byte)'"' || b == (byte)'\\')
            {
                ziel[pos++] = (byte)'\\';
                ziel[pos++] = b;
            }
            else if (b < 0x20)
            {
                ziel[pos++] = (byte)'\\';
                ziel[pos++] = (byte)'u';
                ziel[pos++] = (byte)'0';
                ziel[pos++] = (byte)'0';
                ziel[pos++] = Hex[b >> 4];
                ziel[pos++] = Hex[b & 0xF];
            }
            else
            {
                ziel[pos++] = b;
            }
        }
        ziel[pos++] = (byte)'"';
        // Ergebnis an den Pufferanfang schieben (überlappende Kopie ist bei Span erlaubt).
        ziel[..pos].CopyTo(puffer);
        return pos;
    }

    /// <summary>Kodiert zu einem neuen Byte-Feld; für Tests und Größenrechnungen.</summary>
    internal static byte[] Kodieren(string wert)
    {
        var puffer = new byte[PufferGroesse(wert)];
        return puffer.AsSpan(0, Kodieren(wert, puffer)).ToArray();
    }
}

/// <summary>
/// Freie JSON-Werte (<c>details</c>, <c>daten</c>, <c>objekt</c>, <c>wert</c>) werden unverändert durchgereicht:
/// gelesen als Kopie des Werts, geschrieben als die empfangenen Rohbytes. So bleibt die Ablage undurchsichtig.
/// </summary>
internal sealed class RohesJson : JsonConverter<JsonElement>
{
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var dokument = JsonDocument.ParseValue(ref reader);
        return dokument.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteRawValue(JsonMarshal.GetRawUtf8Value(value), skipInputValidation: true);
    }
}
