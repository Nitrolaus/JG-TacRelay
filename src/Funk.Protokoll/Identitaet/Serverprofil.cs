// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Funk.Protokoll.Identitaet;

/// <summary>Ein Host des Serverprofils mit den erlaubten Adressfamilien.</summary>
public sealed record ProfilHost(string Name, bool IPv4, bool IPv6);

/// <summary>
/// Inhalt (<c>daten</c>) des Serverprofils nach 3.1. Gelesen wird streng nach 4.3: doppelte Schlüssel,
/// Kommentare, abschließende Kommas und Tiefe über 8 scheitern, unbekannte Felder werden übergangen,
/// <c>null</c> gilt wie fehlend. Gelesen wird nur, was schon signaturgeprüft ist (<see cref="ProfilAnnahme"/>).
/// </summary>
public sealed record Serverprofil(
    string Name,
    uint ProfilVersion,
    long Ausgestellt,
    long GueltigBis,
    IReadOnlyList<ProfilHost> Hosts,
    ushort TcpPort,
    IReadOnlyList<Fingerabdruck> Pins,
    IReadOnlyList<string> Nameserver)
{
    /// <summary>Obergrenze für <c>daten</c> in Byte. Das Schema braucht weit weniger; das ist Härtung.</summary>
    public const int MaxDatenBytes = 16 * 1024;

    /// <summary>Größte zulässige Ganzzahl in JSON (4.3).</summary>
    public const long MaxGanzzahl = (1L << 53) - 1;

    internal static readonly JsonReaderOptions LeseOptionen = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8,
    };

    private static readonly JsonWriterOptions SchreibOptionen = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    /// <summary>Ob der Pin (SHA-256 über das SPKI des Blattzertifikats) aktuell oder Reserve ist.</summary>
    public bool PinPasst(Fingerabdruck pin)
    {
        foreach (var p in Pins)
            if (p == pin) return true;
        return false;
    }

    /// <summary>Pin-Prüfung für den <c>RemoteCertificateValidationCallback</c> des Clients (3.1, 4.1).</summary>
    public bool ZertifikatPasst(X509Certificate2? blatt) => blatt is not null && PinPasst(Fingerabdruck.AusZertifikat(blatt));

    /// <summary>Nach <c>gueltigBis</c> bleibt das Profil in Gebrauch, der Client warnt nur (3.1).</summary>
    public bool IstAbgelaufen(DateTimeOffset jetzt) => jetzt.ToUnixTimeMilliseconds() > GueltigBis;

    /// <summary>
    /// Ob <paramref name="name"/> als Host im Profil stehen darf (RFC 1123, höchstens 253 Zeichen, kein Punkt am Ende),
    /// dieselbe Regel wie beim Lesen. Für Werkzeug und Client, damit sie vor dem Schreiben bzw. Verbinden genau sagen können,
    /// was nicht stimmt.
    /// </summary>
    public static bool HostnameGueltig(string? name) => Zeichenregeln.IstHostname(name);

    /// <summary>Kompaktes JSON in Katalogreihenfolge, so wie das Offline-Werkzeug es signiert.</summary>
    public byte[] AlsJson()
    {
        var puffer = new ArrayBufferWriter<byte>(512);
        using (var w = new Utf8JsonWriter(puffer, SchreibOptionen))
        {
            w.WriteStartObject();
            w.WriteString("name", Name);
            w.WriteNumber("profilVersion", ProfilVersion);
            w.WriteNumber("ausgestellt", Ausgestellt);
            w.WriteNumber("gueltigBis", GueltigBis);
            w.WriteStartArray("hosts");
            foreach (var h in Hosts)
            {
                w.WriteStartObject();
                w.WriteString("name", h.Name);
                w.WriteStartArray("familien");
                if (h.IPv4) w.WriteStringValue("IPv4");
                if (h.IPv6) w.WriteStringValue("IPv6");
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("tcpPort", TcpPort);
            w.WriteStartArray("pins");
            foreach (var p in Pins) w.WriteStringValue(p.AlsBase64());
            w.WriteEndArray();
            if (Nameserver.Count > 0)
            {
                w.WriteStartArray("nameserver");
                foreach (var n in Nameserver) w.WriteStringValue(n);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return puffer.WrittenSpan.ToArray();
    }

    /// <summary>Liest <c>daten</c> streng. Wirft nie; ungültig heißt <c>false</c>.</summary>
    public static bool TryLesen(ReadOnlySpan<byte> daten, [NotNullWhen(true)] out Serverprofil? profil)
    {
        profil = null;
        if (daten.IsEmpty || daten.Length > MaxDatenBytes) return false;
        try
        {
            return Lesen(daten, out profil);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false; // ungültiges UTF-8 oder einzelne Ersatzzeichen in Zeichenketten
        }
        catch (ArgumentException)
        {
            return false; // Normalisierung ungültiger Zeichen
        }
    }

    private static bool Lesen(ReadOnlySpan<byte> daten, out Serverprofil? profil)
    {
        profil = null;
        var r = new Utf8JsonReader(daten, LeseOptionen);
        if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return false;

        string? name = null;
        uint? version = null;
        long? ausgestellt = null, gueltigBis = null;
        List<ProfilHost>? hosts = null;
        ushort? port = null;
        List<Fingerabdruck>? pins = null;
        List<string>? nameserver = null;
        var gesehen = new HashSet<string>(StringComparer.Ordinal);

        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            var feld = r.GetString()!;
            if (!gesehen.Add(feld) || !r.Read()) return false;
            if (r.TokenType == JsonTokenType.Null) continue;
            var ok = feld switch
            {
                "name" => StrengesJson.Text(ref r, 1, 32, out name),
                "profilVersion" => StrengesJson.U32(ref r, out version),
                "ausgestellt" => StrengesJson.Zeit(ref r, out ausgestellt),
                "gueltigBis" => StrengesJson.Zeit(ref r, out gueltigBis),
                "hosts" => LeseHosts(ref r, out hosts),
                "tcpPort" => StrengesJson.Port(ref r, out port),
                "pins" => LesePins(ref r, out pins),
                "nameserver" => LeseNameserver(ref r, out nameserver),
                _ => StrengesJson.Ueberspringen(ref r),
            };
            if (!ok) return false;
        }
        if (r.TokenType != JsonTokenType.EndObject || r.Read()) return false;

        if (name is null || version is null || ausgestellt is null || gueltigBis is null
            || hosts is null || port is null || pins is null) return false;
        if (gueltigBis <= ausgestellt) return false;

        profil = new Serverprofil(name, version.Value, ausgestellt.Value, gueltigBis.Value, hosts, port.Value, pins,
            nameserver ?? []);
        return true;
    }

    private static bool LeseHosts(ref Utf8JsonReader r, out List<ProfilHost>? hosts)
    {
        hosts = null;
        if (r.TokenType != JsonTokenType.StartArray) return false;
        var liste = new List<ProfilHost>(4);
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (liste.Count == 4 || !LeseHost(ref r, out var host)) return false;
            liste.Add(host!);
        }
        if (liste.Count == 0) return false;
        hosts = liste;
        return true;
    }

    private static bool LeseHost(ref Utf8JsonReader r, out ProfilHost? host)
    {
        host = null;
        if (r.TokenType != JsonTokenType.StartObject) return false;
        string? name = null;
        bool? v4 = null, v6 = null;
        var gesehen = new HashSet<string>(StringComparer.Ordinal);
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            var feld = r.GetString()!;
            if (!gesehen.Add(feld) || !r.Read()) return false;
            if (r.TokenType == JsonTokenType.Null) continue;
            var ok = feld switch
            {
                "name" => StrengesJson.Hostname(ref r, out name),
                "familien" => LeseFamilien(ref r, out v4, out v6),
                _ => StrengesJson.Ueberspringen(ref r),
            };
            if (!ok) return false;
        }
        if (name is null || v4 is null || v6 is null) return false;
        host = new ProfilHost(name, v4.Value, v6.Value);
        return true;
    }

    private static bool LeseFamilien(ref Utf8JsonReader r, out bool? v4, out bool? v6)
    {
        v4 = v6 = null;
        if (r.TokenType != JsonTokenType.StartArray) return false;
        bool a = false, b = false;
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (r.TokenType != JsonTokenType.String) return false;
            if (r.ValueTextEquals("IPv4"u8) && !a) a = true;
            else if (r.ValueTextEquals("IPv6"u8) && !b) b = true;
            else return false;
        }
        if (!a && !b) return false;
        v4 = a;
        v6 = b;
        return true;
    }

    private static bool LesePins(ref Utf8JsonReader r, out List<Fingerabdruck>? pins)
    {
        pins = null;
        if (r.TokenType != JsonTokenType.StartArray) return false;
        var liste = new List<Fingerabdruck>(3);
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (liste.Count == 3 || r.TokenType != JsonTokenType.String) return false;
            if (!Fingerabdruck.TryAusBase64(r.GetString(), out var pin) || liste.Contains(pin)) return false;
            liste.Add(pin);
        }
        if (liste.Count < 2) return false;
        pins = liste;
        return true;
    }

    private static bool LeseNameserver(ref Utf8JsonReader r, out List<string>? nameserver)
    {
        nameserver = null;
        if (r.TokenType != JsonTokenType.StartArray) return false;
        var liste = new List<string>(4);
        while (r.Read() && r.TokenType != JsonTokenType.EndArray)
        {
            if (liste.Count == 4 || !StrengesJson.Hostname(ref r, out var ns)) return false;
            liste.Add(ns!);
        }
        nameserver = liste;
        return true;
    }
}

/// <summary>Kleine Lesehilfen für streng gelesenes JSON nach 4.3.</summary>
internal static class StrengesJson
{
    public static bool Text(ref Utf8JsonReader r, int min, int max, out string? wert)
    {
        wert = null;
        if (r.TokenType != JsonTokenType.String) return false;
        var s = r.GetString();
        if (!Zeichenregeln.IstText(s, min, max)) return false;
        wert = s;
        return true;
    }

    public static bool Hostname(ref Utf8JsonReader r, out string? wert)
    {
        wert = null;
        if (r.TokenType != JsonTokenType.String) return false;
        var s = r.GetString();
        if (!Zeichenregeln.IstHostname(s)) return false;
        wert = s;
        return true;
    }

    public static bool U32(ref Utf8JsonReader r, out uint? wert)
    {
        wert = null;
        if (r.TokenType != JsonTokenType.Number || !r.TryGetUInt32(out var v)) return false;
        wert = v;
        return true;
    }

    public static bool Zeit(ref Utf8JsonReader r, out long? wert)
    {
        wert = null;
        if (r.TokenType != JsonTokenType.Number || !r.TryGetInt64(out var v) || v < 0 || v > Serverprofil.MaxGanzzahl) return false;
        wert = v;
        return true;
    }

    public static bool Port(ref Utf8JsonReader r, out ushort? wert)
    {
        wert = null;
        if (r.TokenType != JsonTokenType.Number || !r.TryGetUInt16(out var v) || v == 0) return false;
        wert = v;
        return true;
    }

    public static bool Base64(ref Utf8JsonReader r, int maxBytes, out byte[]? wert)
    {
        wert = null;
        if (r.TokenType != JsonTokenType.String) return false;
        var s = r.GetString()!;
        var laenge = StrengesBase64.DekodierteLaenge(s);
        if (laenge <= 0 || laenge > maxBytes) return false;
        var bytes = new byte[laenge];
        if (!StrengesBase64.TryDekodieren(s, bytes, out _)) return false;
        wert = bytes;
        return true;
    }

    /// <summary>Übergeht einen unbekannten Wert, lehnt dabei doppelte Schlüssel auch in der Tiefe ab.</summary>
    public static bool Ueberspringen(ref Utf8JsonReader r)
    {
        switch (r.TokenType)
        {
            case JsonTokenType.StartObject:
                var gesehen = new HashSet<string>(StringComparer.Ordinal);
                while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
                {
                    if (!gesehen.Add(r.GetString()!) || !r.Read() || !Ueberspringen(ref r)) return false;
                }
                return r.TokenType == JsonTokenType.EndObject;
            case JsonTokenType.StartArray:
                while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                    if (!Ueberspringen(ref r)) return false;
                return r.TokenType == JsonTokenType.EndArray;
            case JsonTokenType.String:
                return Zeichenregeln.IstText(r.GetString(), 0, int.MaxValue);
            case JsonTokenType.Number:
            case JsonTokenType.True:
            case JsonTokenType.False:
            case JsonTokenType.Null:
                return true;
            default:
                return false;
        }
    }
}
