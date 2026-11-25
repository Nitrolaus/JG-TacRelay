// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Funk.ClientKern.Echo;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Testserver;

/// <summary>
/// Probe-Konfiguration (<c>konfig\probefunk.json</c>) für den Testserver auf dem eigenen PC: Netze, Schiffe und Rollen in
/// der Form von <c>funk-admin netz-setzen/schiff-setzen/rolle-setzen --json</c>, dazu die automatische Freigabe über den
/// Loopback-Testzugang und die Einstellungen des Echo-Funkers. Keine Geheimnisse; Serverschlüssel, Betriebsgeheimnis und
/// Profilschlüssel entstehen bei jedem Start neu im Speicher. Unbekannte Felder sind ein Fehler.
/// </summary>
public sealed class Probekonfiguration
{
    /// <summary>Name im Serverprofil.</summary>
    public string Name { get; init; } = "Probefunk";

    public ProbeFreigabe Freigabe { get; init; } = new();

    public ProbeEcho Echo { get; init; } = new();

    public ProbeEinstellungen? Einstellungen { get; init; }

    /// <summary>Netze wie in NetzListe; Schiffsnetze tragen <c>schiffId</c> und <c>schiffsnetz</c>.</summary>
    public List<JsonObject> Netze { get; init; } = [];

    public List<JsonObject> Schiffe { get; init; } = [];

    public List<JsonObject> Rollen { get; init; } = [];

    internal static readonly JsonSerializerOptions Optionen = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Liest die Datei. Formfehler werfen <see cref="InvalidDataException"/>; die Regeln prüft <see cref="Pruefen"/>.</summary>
    public static Probekonfiguration Laden(string pfad)
    {
        ArgumentNullException.ThrowIfNull(pfad);
        try
        {
            return JsonSerializer.Deserialize<Probekonfiguration>(File.ReadAllBytes(pfad), Optionen)
                ?? throw new InvalidDataException("Probe-Konfiguration ist leer.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("Probe-Konfiguration ist kein gültiges JSON: " + e.Message, e);
        }
    }

    /// <summary>Prüft die Regeln, die nicht erst der Server prüft. Leere Liste heißt: startfähig.</summary>
    public IReadOnlyList<string> Pruefen()
    {
        var fehler = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) fehler.Add("name fehlt.");
        if (Freigabe.StufeLesen() is null) fehler.Add("freigabe.stufe: ADMIN, RAIDLEITER oder FUNKER.");
        if (string.IsNullOrWhiteSpace(Freigabe.Name)) fehler.Add("freigabe.name fehlt.");
        try
        {
            Echo.Optionen().Pruefen();
        }
        catch (ArgumentOutOfRangeException e)
        {
            fehler.Add("echo: " + e.Message);
        }
        var ids = new HashSet<long>();
        foreach (var n in Netze)
        {
            if (n["id"] is not JsonValue v || !v.TryGetValue<long>(out var id)) fehler.Add("netze: id fehlt.");
            else if (!ids.Add(id)) fehler.Add(string.Create(CultureInfo.InvariantCulture, $"netze: id {id} doppelt."));
        }
        if (!Netze.Any(n => string.Equals((string?)n["name"], Echo.Netz, StringComparison.OrdinalIgnoreCase)))
            fehler.Add("echo.netz '" + Echo.Netz + "' steht nicht unter netze.");
        return fehler;
    }
}

/// <summary>Automatische Freigabe neuer Geräte über den Loopback-Testzugang (nur Probe, nie im Betrieb).</summary>
public sealed class ProbeFreigabe
{
    public bool Automatisch { get; init; } = true;

    /// <summary>Stufe der automatisch angelegten Konten.</summary>
    public string Stufe { get; init; } = "RAIDLEITER";

    /// <summary>Anzeigename der Konten; bei mehreren Geräten hängt der Server " (2)" usw. an.</summary>
    public string Name { get; init; } = "Probefunker";

    public I.Stufe? StufeLesen() => Stufe switch
    {
        "ADMIN" => I.Stufe.Admin,
        "RAIDLEITER" => I.Stufe.Raidleiter,
        "FUNKER" => I.Stufe.Funker,
        _ => null,
    };
}

/// <summary>Einstellungen des Echo-Funkers im Testserver.</summary>
public sealed class ProbeEcho
{
    public string Netz { get; init; } = EchoOptionen.StandardNetz;

    public int PauseMs { get; init; } = 500;

    public double MaxSekunden { get; init; } = 30;

    public EchoOptionen Optionen() => new()
    {
        Netz = Netz,
        Pause = TimeSpan.FromMilliseconds(PauseMs),
        MaxPhase = TimeSpan.FromSeconds(MaxSekunden),
    };
}

/// <summary>Einstellungen der NetzListe (wie <c>funk-admin einstellung</c>).</summary>
public sealed class ProbeEinstellungen
{
    public bool? Selbstwahl { get; init; }

    public bool? DurchsageNurTaste { get; init; }
}
