// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;

namespace Funk.Protokoll.Steuerung;

/// <summary>Ein Rang einer Laufbahn: Kennung ("MARINE_04"), Name ("Sergeant"), Laufbahn und Stufe (1-basiert).</summary>
public sealed record AbzeichenRang(string Kennung, string Name, string Laufbahn, int Stufe);

/// <summary>Eine Laufbahn (Marine, Navy, Orga) mit ihren Rängen in aufsteigender Reihenfolge.</summary>
public sealed record AbzeichenLaufbahn(string Kennung, string Name, ImmutableArray<AbzeichenRang> Raenge);

/// <summary>Eine Tätigkeit ("Gunner M") mit ihrer Gruppe ("Waffen").</summary>
public sealed record AbzeichenTaetigkeit(string Kennung, string Name, string Gruppe);

/// <summary>
/// Der feste Abzeichenkatalog (0.11.7, Sarevoc 14./15.10.2026): Ränge in drei Laufbahnen und Tätigkeiten. Er kommt mit dem Programm
/// (eingebettete <c>abzeichen.json</c>), wird einmal geladen und im Programm nie geändert: Bilder und Namen gehören zum Programm,
/// vergeben werden die Abzeichen in einer Admin-Sitzung (0x0025). Dazu kommen die automatischen Zeichen ADMIN und
/// EINSATZLEITUNG, die der Server ableitet; sie stehen nicht im Katalog, lassen sich aber wie Abzeichen ausblenden und vorn zeigen.
/// </summary>
public static class Abzeichenkatalog
{
    /// <summary>Name der eingebetteten Ressource.</summary>
    public const string Ressource = "Funk.Protokoll.Abzeichen.abzeichen.json";

    /// <summary>Automatisches Zeichen eines eingerichteten Admins (Kranz).</summary>
    public const string Admin = "ADMIN";

    /// <summary>Automatisches Zeichen eines laufenden Auftrags der Einsatzleitung (Stern).</summary>
    public const string Einsatzleitung = "EINSATZLEITUNG";

    /// <summary>
    /// Stufenmerkmal RAIDLEITER: Der Server darf die Kennung weiter schicken, der Client zeichnet sie seit 0.11.7 nicht mehr; sie
    /// lässt sich weder vorn zeigen noch ausblenden.
    /// </summary>
    public const string Fuehrung = "FUEHRUNG";

    private static readonly Lazy<Inhalt> Geladen = new(Laden, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Fassung der Katalogdatei.</summary>
    public static int Fassung => Geladen.Value.Fassung;

    /// <summary>Die Laufbahnen in Katalogfolge.</summary>
    public static ImmutableArray<AbzeichenLaufbahn> Laufbahnen => Geladen.Value.Laufbahnen;

    /// <summary>Die Tätigkeiten in Katalogfolge.</summary>
    public static ImmutableArray<AbzeichenTaetigkeit> Taetigkeiten => Geladen.Value.Taetigkeiten;

    /// <summary>Die Gruppen der Tätigkeiten in der Reihenfolge ihres ersten Auftretens.</summary>
    public static ImmutableArray<string> Gruppen => Geladen.Value.Gruppen;

    /// <summary>Die automatischen Zeichen, die sich wie Abzeichen ausblenden und vorn zeigen lassen.</summary>
    public static ImmutableArray<string> Automatisch { get; } = [Admin, Einsatzleitung];

    /// <summary>Der Rang zu einer Kennung oder <c>null</c>.</summary>
    public static AbzeichenRang? Rang(string? kennung) =>
        kennung is not null && Geladen.Value.Raenge.TryGetValue(kennung, out var r) ? r : null;

    /// <summary>Die Laufbahn zu einer Kennung (Laufbahn oder Rang) oder <c>null</c>.</summary>
    public static AbzeichenLaufbahn? Laufbahn(string? kennung)
    {
        if (kennung is null) return null;
        var lauf = Rang(kennung)?.Laufbahn ?? kennung;
        foreach (var l in Laufbahnen)
            if (string.Equals(l.Kennung, lauf, StringComparison.Ordinal)) return l;
        return null;
    }

    /// <summary>Die Ränge einer Laufbahn in aufsteigender Reihenfolge, leer für eine unbekannte.</summary>
    public static ImmutableArray<AbzeichenRang> RaengeVon(string laufbahn) => Laufbahn(laufbahn)?.Raenge ?? [];

    /// <summary>Die Tätigkeit zu einer Kennung oder <c>null</c>.</summary>
    public static AbzeichenTaetigkeit? Taetigkeit(string? kennung) =>
        kennung is not null && Geladen.Value.TaetigkeitNach.TryGetValue(kennung, out var t) ? t : null;

    /// <summary>Ob die Kennung ein Rang ist.</summary>
    public static bool IstRang(string? kennung) => Rang(kennung) is not null;

    /// <summary>Ob die Kennung eine Tätigkeit ist.</summary>
    public static bool IstTaetigkeit(string? kennung) => Taetigkeit(kennung) is not null;

    /// <summary>Ob die Kennung im Katalog steht (Rang oder Tätigkeit).</summary>
    public static bool Kennt(string? kennung) => IstRang(kennung) || IstTaetigkeit(kennung);

    /// <summary>Ob die Kennung ein automatisches Zeichen ist, das sich ausblenden und vorn zeigen lässt (ADMIN, EINSATZLEITUNG).</summary>
    public static bool IstAutomatisch(string? kennung) => kennung is Admin or Einsatzleitung;

    /// <summary>Was in <c>ProfilSetzen.abzeichenVorn</c> und <c>abzeichenAus</c> stehen darf: Katalogkennungen und die automatischen Zeichen.</summary>
    public static bool IstWaehlbar(string? kennung) => Kennt(kennung) || IstAutomatisch(kennung);

    /// <summary>Name aus dem Katalog ("Sergeant", "Gunner M") oder <c>null</c>.</summary>
    public static string? Name(string? kennung) => Rang(kennung)?.Name ?? Taetigkeit(kennung)?.Name;

    /// <summary>Titel für Hilfetext und Journal: "Marine: Sergeant" für einen Rang, "Gunner M" für eine Tätigkeit, sonst <c>null</c>.</summary>
    public static string? Titel(string? kennung)
    {
        if (Rang(kennung) is { } r) return (Laufbahn(r.Laufbahn)?.Name ?? r.Laufbahn) + ": " + r.Name;
        return Taetigkeit(kennung)?.Name;
    }

    /// <summary>
    /// Ordnungszahl für die Anzeige: automatische Zeichen zuerst, dann Ränge in Laufbahnfolge, dann Tätigkeiten in Katalogfolge;
    /// Unbekanntes zuletzt.
    /// </summary>
    public static int Reihenfolge(string? kennung)
    {
        if (kennung == Admin) return 0;
        if (kennung == Einsatzleitung) return 1;
        return kennung is not null && Geladen.Value.Folge.TryGetValue(kennung, out var n) ? n : int.MaxValue;
    }

    /// <summary>Ordnet Kennungen nach <see cref="Reihenfolge"/>, ohne Doppelte; die Reihenfolge Gleichrangiger bleibt.</summary>
    public static ImmutableArray<string> Ordnen(IEnumerable<string> kennungen)
    {
        ArgumentNullException.ThrowIfNull(kennungen);
        return [.. kennungen.Distinct(StringComparer.Ordinal).OrderBy(Reihenfolge)];
    }

    // ---------------------------------------------------------------- Laden

    private sealed record Inhalt(
        int Fassung,
        ImmutableArray<AbzeichenLaufbahn> Laufbahnen,
        ImmutableArray<AbzeichenTaetigkeit> Taetigkeiten,
        ImmutableArray<string> Gruppen,
        FrozenDictionary<string, AbzeichenRang> Raenge,
        FrozenDictionary<string, AbzeichenTaetigkeit> TaetigkeitNach,
        FrozenDictionary<string, int> Folge);

    /// <summary>Liest die eingebettete Datei ohne Reflexion (JsonDocument); ein Fehler darin ist ein Baufehler und wirft.</summary>
    private static Inhalt Laden()
    {
        using var strom = typeof(Abzeichenkatalog).Assembly.GetManifestResourceStream(Ressource)
            ?? throw new InvalidOperationException("Abzeichenkatalog fehlt: " + Ressource);
        using var dokument = JsonDocument.Parse(strom);
        var wurzel = dokument.RootElement;
        var fassung = wurzel.GetProperty("fassung").GetInt32();
        var laufbahnen = ImmutableArray.CreateBuilder<AbzeichenLaufbahn>();
        var raenge = new Dictionary<string, AbzeichenRang>(StringComparer.Ordinal);
        var folge = new Dictionary<string, int>(StringComparer.Ordinal);
        var n = 10;
        foreach (var l in wurzel.GetProperty("laufbahnen").EnumerateArray())
        {
            var kennung = Text(l, "kennung");
            var liste = ImmutableArray.CreateBuilder<AbzeichenRang>();
            var stufe = 0;
            foreach (var r in l.GetProperty("raenge").EnumerateArray())
            {
                var rang = new AbzeichenRang(Text(r, "kennung"), Text(r, "name"), kennung, ++stufe);
                Eintragen(raenge, rang.Kennung, rang);
                folge[rang.Kennung] = n++;
                liste.Add(rang);
            }
            laufbahnen.Add(new AbzeichenLaufbahn(kennung, Text(l, "name"), liste.ToImmutable()));
        }
        var taetigkeiten = ImmutableArray.CreateBuilder<AbzeichenTaetigkeit>();
        var nach = new Dictionary<string, AbzeichenTaetigkeit>(StringComparer.Ordinal);
        var gruppen = new List<string>();
        foreach (var t in wurzel.GetProperty("taetigkeiten").EnumerateArray())
        {
            var taetigkeit = new AbzeichenTaetigkeit(Text(t, "kennung"), Text(t, "name"), Text(t, "gruppe"));
            if (raenge.ContainsKey(taetigkeit.Kennung)) throw new InvalidDataException("Kennung doppelt im Abzeichenkatalog: " + taetigkeit.Kennung);
            Eintragen(nach, taetigkeit.Kennung, taetigkeit);
            folge[taetigkeit.Kennung] = n++;
            taetigkeiten.Add(taetigkeit);
            if (!gruppen.Contains(taetigkeit.Gruppe)) gruppen.Add(taetigkeit.Gruppe);
        }
        return new Inhalt(fassung, laufbahnen.ToImmutable(), taetigkeiten.ToImmutable(), [.. gruppen],
            raenge.ToFrozenDictionary(StringComparer.Ordinal), nach.ToFrozenDictionary(StringComparer.Ordinal), folge.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static string Text(JsonElement e, string name) =>
        e.GetProperty(name).GetString() is { Length: > 0 } s ? s : throw new InvalidDataException("Abzeichenkatalog: " + name + " fehlt");

    private static void Eintragen<T>(Dictionary<string, T> ziel, string kennung, T wert)
    {
        if (kennung.Length > Grenzen.AbzeichenKennungMax || IstAutomatisch(kennung) || kennung == Fuehrung || !ziel.TryAdd(kennung, wert))
            throw new InvalidDataException("Kennung ungültig oder doppelt im Abzeichenkatalog: " + kennung);
    }
}
