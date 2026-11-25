// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using System.Text.Json.Serialization;
using Funk.Protokoll.Identitaet;

namespace Funk.Server.Betrieb;

/// <summary>Gemeinsame JSON-Einstellungen der Zustandsdateien. Sie sind serverintern, kein Drahtformat.</summary>
public static class Ablageformat
{
    public static readonly JsonSerializerOptions Optionen = Erzeugen();

    private static JsonSerializerOptions Erzeugen()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            WriteIndented = false,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        };
        o.Converters.Add(new FingerabdruckKonverter());
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }

    /// <summary>Fingerabdruck als Base64 (32 Byte), auch als Wörterbuchschlüssel.</summary>
    private sealed class FingerabdruckKonverter : JsonConverter<Fingerabdruck>
    {
        public override Fingerabdruck Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Fingerabdruck.TryAusBase64(reader.GetString(), out var f) ? f : throw new JsonException("Fingerabdruck ungültig.");

        public override void Write(Utf8JsonWriter writer, Fingerabdruck value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.AlsBase64());

        public override Fingerabdruck ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Read(ref reader, typeToConvert, options);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, Fingerabdruck value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.AlsBase64());
    }
}
