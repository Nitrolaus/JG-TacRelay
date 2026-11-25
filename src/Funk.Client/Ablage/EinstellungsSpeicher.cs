// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Funk.Client.Design;
using Funk.Client.Dienste;

namespace Funk.Client.Ablage;

/// <summary>
/// Liest und schreibt <see cref="ClientEinstellungen"/> als JSON. Der Pfad ist
/// <c>%LOCALAPPDATA%\JG-TacRelay\client-einstellungen.json</c>, lässt sich aber über die Umgebungsvariable
/// <see cref="Umgebungsvariable"/> auf eine andere Datei legen (Tests, Probeläufe, portable Nutzung). Geschrieben wird
/// atomar: erst eine Nachbardatei, dann Ersetzen. Eine unlesbare Datei wird nicht überschrieben, sondern als
/// <c>.defekt-JJJJMMTT-HHmmss</c> beiseitegelegt; danach gelten die Standardwerte.
/// </summary>
public sealed class EinstellungsSpeicher
{
    public const string Umgebungsvariable = "TACRELAY_EINSTELLUNGEN";

    /// <summary>
    /// Name der Datei in <c>%LOCALAPPDATA%\JG-TacRelay</c>, seit 0.1.0 unverändert. Das Setup fragt nach ihr, ob es schon
    /// Einstellungen gibt (<c>EinstellungenDa</c> in installer\TacRelay.iss); wer ihn ändert, ändert auch das Setup.
    /// </summary>
    public const string Dateiname = "client-einstellungen.json";

    /// <summary>Obergrenze der Datei; alles darüber ist sicher keine Einstellungsdatei.</summary>
    public const long MaxBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Optionen = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    public EinstellungsSpeicher(string pfad)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pfad);
        Pfad = Path.GetFullPath(pfad);
    }

    /// <summary>Vollständiger Pfad der Datei.</summary>
    public string Pfad { get; }

    /// <summary>Was beim letzten Laden schiefging, oder <c>null</c>.</summary>
    public string? LetzterFehler { get; private set; }

    /// <summary>Pfad aus der Umgebungsvariable oder der Standardpfad im Benutzerordner der App.</summary>
    public static string StandardPfad()
    {
        var eigen = Environment.GetEnvironmentVariable(Umgebungsvariable);
        if (!string.IsNullOrWhiteSpace(eigen)) return Path.GetFullPath(eigen.Trim());
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JG-TacRelay", Dateiname);
    }

    /// <summary>Liest die Datei; fehlt sie oder ist sie unbrauchbar, gelten Standardwerte.</summary>
    public ClientEinstellungen Laden()
    {
        LetzterFehler = null;
        var e = new ClientEinstellungen();
        if (!File.Exists(Pfad))
        {
            e.Normalisieren();
            return e;
        }
        try
        {
            var laenge = new FileInfo(Pfad).Length;
            if (laenge > MaxBytes) throw new InvalidDataException("Einstellungsdatei größer als 1 MiB.");
            var daten = File.ReadAllBytes(Pfad);
            var gelesen = JsonSerializer.Deserialize<ClientEinstellungen>(daten, Optionen)
                ?? throw new InvalidDataException("Einstellungsdatei ist leer (null).");
            gelesen.Normalisieren();
            return gelesen;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            LetzterFehler = "Einstellungen unlesbar, Standardwerte gelten: " + Beschreibung(ex);
            Beiseitelegen();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LetzterFehler = "Einstellungen nicht lesbar, Standardwerte gelten: " + Beschreibung(ex);
        }
        e.Normalisieren();
        return e;
    }

    /// <summary>
    /// Was beim Lesen oder Schreiben schiefging, als Satz ohne Pfad (seit 0.6.0): <c>ex.Message</c> nennt bei Dateifehlern den ganzen
    /// Pfad samt Windows-Benutzernamen, und Fußzeile und Einstellungen landen auf Bildschirmfotos.
    /// </summary>
    internal static string Beschreibung(Exception ex) => ex switch
    {
        JsonException j => "kein gültiges JSON" + (j.LineNumber is { } z ? " (Zeile " + (z + 1).ToString(CultureInfo.InvariantCulture) + ")" : "") + ".",
        InvalidDataException => ex.Message,
        UnauthorizedAccessException => "kein Zugriff (fehlende Rechte oder ein Ordner an der Stelle der Datei).",
        DirectoryNotFoundException => "der Ordner fehlt.",
        PathTooLongException => "der Pfad ist zu lang.",
        IOException => "die Datei ist gesperrt oder nicht erreichbar (offen in einem anderen Programm, Datenträger voll oder getrennt).",
        _ => "der Inhalt passt nicht zu den Einstellungen.",
    };

    /// <summary>Schreibt atomar. Wirft bei Schreibfehlern (<see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>).</summary>
    public void Speichern(ClientEinstellungen einstellungen)
    {
        ArgumentNullException.ThrowIfNull(einstellungen);
        var daten = Serialisieren(einstellungen);
        Schreiben(daten);
    }

    /// <summary>JSON der Einstellungen (auf dem Oberflächen-Thread, damit niemand währenddessen ändert).</summary>
    public static byte[] Serialisieren(ClientEinstellungen einstellungen) => JsonSerializer.SerializeToUtf8Bytes(einstellungen, Optionen);

    /// <summary>Schreibt fertiges JSON atomar (darf auf einem anderen Thread laufen).</summary>
    public void Schreiben(byte[] daten)
    {
        ArgumentNullException.ThrowIfNull(daten);
        var ordner = Path.GetDirectoryName(Pfad)!;
        Directory.CreateDirectory(ordner);
        var neu = Pfad + ".neu";
        File.WriteAllBytes(neu, daten);
        File.Move(neu, Pfad, overwrite: true);
    }

    private void Beiseitelegen()
    {
        try
        {
            var ziel = Pfad + ".defekt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (!File.Exists(ziel)) File.Move(Pfad, ziel);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dann bleibt sie liegen; beim nächsten Speichern wird sie ersetzt.
        }
    }
}

/// <summary>
/// Liest eine Aufzählung nachsichtig: ein bekannter Name (ohne Rücksicht auf Groß- und Kleinschreibung) oder eine bekannte
/// Zahl gilt, alles andere wird still zum <see cref="Ersatz"/> (sonst dem ersten Wert, 0). So macht eine entfallene Wahl,
/// etwa die Ansicht "Leiste", nicht die ganze Datei unlesbar. Geschrieben wird der Name.
/// </summary>
public class NachsichtigeAufzaehlung<T> : JsonConverter<T> where T : struct, Enum
{
    /// <summary>Was für einen unbekannten Wert gilt.</summary>
    protected virtual T Ersatz => default;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var text = reader.GetString();
                return !int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
                    && Enum.TryParse<T>(text, ignoreCase: true, out var wert) && Enum.IsDefined(wert) ? wert : Ersatz;
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var zahl) && Enum.IsDefined(typeof(T), zahl) ? (T)Enum.ToObject(typeof(T), zahl) : Ersatz;
            default:
                reader.Skip();
                return Ersatz;
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Farbstellung nachsichtig gelesen; jeder alte oder unbekannte Wert wird die Voreinstellung Anthrazit.</summary>
public sealed class NachsichtigeFarbstellung : NachsichtigeAufzaehlung<Farbstellung>
{
    protected override Farbstellung Ersatz => Farbvorschlaege.Voreinstellung;
}

/// <summary>
/// Liest eine ganze Zahl nachsichtig (seit 0.6.0, Skalierung des Funkfelds): eine Zahl, auch mit Nachkommastellen (gerundet), oder
/// ein Text mit einer Zahl; alles andere wird still 0, statt die ganze Datei unlesbar zu machen. Die Grenzen setzt
/// <c>Normalisieren</c>.
/// </summary>
public sealed class NachsichtigeZahl : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Lesen(ref reader) ?? 0;

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteNumberValue(value);
    }

    /// <summary>Die Zahl am Leser, oder <c>null</c>, wenn dort keine steht (der Wert ist dann übersprungen).</summary>
    internal static int? Lesen(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetDouble(out var d) && double.IsFinite(d) ? (int)Math.Clamp(Math.Round(d), int.MinValue, int.MaxValue) : null;
            case JsonTokenType.String:
                return double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && double.IsFinite(t)
                    ? (int)Math.Clamp(Math.Round(t), int.MinValue, int.MaxValue)
                    : null;
            default:
                reader.Skip();
                return null;
        }
    }
}

/// <summary>Wie <see cref="NachsichtigeZahl"/>, aber ohne Zahl <c>null</c> (gemerkter Ch., gemerkte Rolle).</summary>
public sealed class NachsichtigeZahlOderNull : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : NachsichtigeZahl.Lesen(ref reader);

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is { } v) writer.WriteNumberValue(v);
        else writer.WriteNullValue();
    }
}

/// <summary>
/// Ein Schalter, nachsichtig gelesen (Klangstufe A): <c>true</c> oder <c>false</c>, auch als Text oder 1 und 0; alles andere wird
/// still zum <see cref="Ersatz"/>, statt die ganze Datei unlesbar zu machen.
/// </summary>
public abstract class NachsichtigerSchalter : JsonConverter<bool>
{
    protected abstract bool Ersatz { get; }

    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.String:
                return bool.TryParse(reader.GetString(), out var wert) ? wert : Ersatz;
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var zahl) && zahl is 0 or 1 ? zahl == 1 : Ersatz;
            default:
                reader.Skip();
                return Ersatz;
        }
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBooleanValue(value);
    }
}

/// <summary>Nachsichtiger Schalter, ab Werk an.</summary>
public sealed class NachsichtigAn : NachsichtigerSchalter
{
    protected override bool Ersatz => true;
}

/// <summary>Nachsichtiger Schalter, ab Werk aus.</summary>
public sealed class NachsichtigAus : NachsichtigerSchalter
{
    protected override bool Ersatz => false;
}

/// <summary>
/// Ein Schalter mit drittem Stand "unbekannt" (seit 0.10.0, Schritt Sprechtaste beim ersten Start), nachsichtig gelesen: <c>true</c> oder
/// <c>false</c>, auch als Text oder 1 und 0; <c>null</c> und alles andere wird still <c>null</c>, statt die ganze Datei unlesbar zu machen. Null
/// liest und schreibt das Serialisieren selbst, so greift <c>JsonIgnoreCondition.WhenWritingNull</c> an der Eigenschaft.
/// </summary>
public sealed class NachsichtigerSchalterOderNull : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.String:
                return bool.TryParse(reader.GetString(), out var wert) ? wert : null;
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var zahl) && zahl is 0 or 1 ? zahl == 1 : null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is { } w) writer.WriteBooleanValue(w);
        else writer.WriteNullValue();
    }
}

/// <summary>
/// Ein Text, nachsichtig gelesen (Klangstufe A, Mikrofon der gelernten Verstärkung): ein Text oder null; alles andere wird still
/// <c>null</c>, statt die ganze Datei unlesbar zu machen.
/// </summary>
public sealed class NachsichtigerText : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>
/// Eine Liste von Texten, nachsichtig gelesen (seit 0.9.3, die aufgeklappten Klappbereiche): Texte bleiben, alles andere in der Liste fällt still
/// weg; ist es gar keine Liste, wird sie leer, statt die ganze Datei unlesbar zu machen.
/// </summary>
public sealed class NachsichtigeTextliste : JsonConverter<List<string>>
{
    public override bool HandleNull => true;

    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var liste = new List<string>();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return liste;
        }
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String) liste.Add(reader.GetString()!);
            else reader.Skip();
        }
        return liste;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartArray();
        foreach (var t in value ?? []) writer.WriteStringValue(t);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Eine Zahl in dB, nachsichtig gelesen (Klangstufe A): eine Zahl oder ein Text mit Zahl (Punkt als Dezimalzeichen); alles andere
/// wird still <c>null</c>. Grenzen setzt danach das Normalisieren.
/// </summary>
public sealed class NachsichtigeDezibel : JsonConverter<double?>
{
    public override bool HandleNull => true;

    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetDouble(out var zahl) ? zahl : null;
            case JsonTokenType.String:
                return double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) ? text : null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value is { } w && double.IsFinite(w)) writer.WriteNumberValue(w);
        else writer.WriteNullValue();
    }
}

/// <summary>
/// Hält die geladenen Einstellungen für die ganze Anwendung und speichert gebündelt: <see cref="Vormerken"/> nach jeder
/// Änderung, geschrieben wird eine halbe Sekunde nach der letzten. Geändert wird nur auf dem Oberflächen-Thread.
/// </summary>
public sealed class Einstellungsablage : IDisposable
{
    private readonly EinstellungsSpeicher _speicher;
    private readonly IOberflaechentakt _takt;
    private readonly Timer _zeitgeber;
    private readonly TimeSpan _verzoegerung;
    private int _vorgemerkt;

    public Einstellungsablage(EinstellungsSpeicher speicher, ClientEinstellungen aktuell, IOberflaechentakt takt, TimeSpan? verzoegerung = null)
    {
        ArgumentNullException.ThrowIfNull(speicher);
        ArgumentNullException.ThrowIfNull(aktuell);
        ArgumentNullException.ThrowIfNull(takt);
        _speicher = speicher;
        _takt = takt;
        Aktuell = aktuell;
        _verzoegerung = verzoegerung ?? TimeSpan.FromMilliseconds(500);
        _zeitgeber = new Timer(_ => _takt.Einreihen(Sichern), null, Timeout.Infinite, Timeout.Infinite);
    }

    public ClientEinstellungen Aktuell { get; }

    /// <summary>Pfad der Datei, für die Anzeige.</summary>
    public string Pfad => _speicher.Pfad;

    /// <summary>Letzter Schreibfehler oder <c>null</c>.</summary>
    public string? Fehler
    {
        get;
        private set
        {
            if (string.Equals(field, value, StringComparison.Ordinal)) return;
            field = value;
            FehlerGeaendert?.Invoke();
        }
    }

    /// <summary><see cref="Fehler"/> hat sich geändert (auf dem Thread, der gesichert hat, im Betrieb der Oberflächen-Thread).</summary>
    public event Action? FehlerGeaendert;

    /// <summary>Wie oft tatsächlich geschrieben wurde (Tests).</summary>
    public int Schreibvorgaenge { get; private set; }

    /// <summary>Merkt eine Änderung vor; geschrieben wird nach der Verzögerung.</summary>
    public void Vormerken()
    {
        Volatile.Write(ref _vorgemerkt, 1);
        _zeitgeber.Change(_verzoegerung, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Schreibt sofort, wenn etwas vorgemerkt ist (Ende der Anwendung, Tests).</summary>
    public void Sichern()
    {
        if (Interlocked.Exchange(ref _vorgemerkt, 0) == 0) return;
        try
        {
            _speicher.Speichern(Aktuell);
            Schreibvorgaenge++;
            Fehler = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Fehler = "Einstellungen nicht gespeichert: " + EinstellungsSpeicher.Beschreibung(ex);
        }
    }

    public void Dispose() => _zeitgeber.Dispose();
}
