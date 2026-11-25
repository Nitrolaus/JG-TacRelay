// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;

namespace Funk.Protokoll.Steuerung;

/// <summary>Art eines Meldungsmotivs (Meldesystem 0.12.0).</summary>
public enum Meldungsart
{
    /// <summary>Groß, 8 s beim Funker; ein neuer ersetzt den alten.</summary>
    Befehl,

    /// <summary>Klein neben dem Befehl, bleibt bis aufgehoben oder ersetzt.</summary>
    Zustand,

    /// <summary>Ampel rot, gelb, grün; eigener Platz.</summary>
    Ampel,

    /// <summary>Sanduhr mit freien Feldern für die Zeiten; eigener Platz.</summary>
    Countdown,

    /// <summary>Bereit-Abfrage.</summary>
    Bereit,
}

/// <summary>
/// Ein Motiv des Meldesystems: Kennung ("sammeln"), Art, Anzeigename für Kachel und Tooltip, die Bilder (meist eines mit dem
/// Namen der Kennung; beim Stopp zwei zur Wahl des Funkers, das erste ab Werk) und die Ausführungen, in denen es Bilder gibt.
/// </summary>
public sealed record Meldungsmotiv(string Kennung, Meldungsart Art, string Name, ImmutableArray<string> Bilder, ImmutableArray<string> Ausfuehrungen)
{
    /// <summary>Ob es das Motiv in dieser Ausführung gibt (sonst zeigt der Client Schlicht).</summary>
    public bool HatAusfuehrung(string? ausfuehrung) => ausfuehrung is not null && Ausfuehrungen.Contains(ausfuehrung, StringComparer.Ordinal);
}

/// <summary>
/// Der feste Katalog der Meldungsmotive (0.12.0, Sarevoc 16.10.2026, M3-M23): Sarevocs eigene Grafiken in den Ausführungen Schlicht (ab Werk),
/// Metall und Nur Schrift. Er kommt mit dem Programm (eingebettete <c>meldungen.json</c>), Server und Client lesen dieselbe Datei:
/// Der Server prüft dagegen Art und Motiv einer Meldung, der Client baut daraus Kacheln und findet die Bilder
/// (<see cref="Ressource"/>, eingebettet im Client unter <c>Meldungen.&lt;ausfuehrung&gt;.&lt;bild&gt;.png</c>).
/// </summary>
public static class Meldungskatalog
{
    /// <summary>Name der eingebetteten Ressource im Protokoll.</summary>
    public const string Katalogressource = "Funk.Protokoll.Meldungen.meldungen.json";

    /// <summary>Ausführung ab Werk; jedes Motiv hat sie, und sie ist der Ersatz, wenn die gewählte fehlt.</summary>
    public const string Schlicht = "schlicht";

    /// <summary>Ausführung mit Metallrahmen (für 17 Motive, nicht für los, stopfen, pause).</summary>
    public const string Metall = "metall";

    /// <summary>Nur Schrift (nur für los, stopfen, pause).</summary>
    public const string Schrift = "schrift";

    /// <summary>Motiv der Bereit-Abfrage, ab Werk.</summary>
    public const string BereitMotiv = "bereit_melden";

    /// <summary>Countdown-Form "Start um": Felder START UM (Uhrzeit) und COUNTDOWN (Rest).</summary>
    public const string FormStartzeit = "Startzeit";

    /// <summary>Countdown-Form "Restzeit": Feld RESTZEIT.</summary>
    public const string FormRestzeit = "Restzeit";

    /// <summary>Motiv zur Form <see cref="FormStartzeit"/>.</summary>
    public const string StartzeitMotiv = "countdown_startzeit";

    /// <summary>Motiv zur Form <see cref="FormRestzeit"/>.</summary>
    public const string RestzeitMotiv = "countdown_restzeit";

    /// <summary>Der Stopp-Befehl; sein Bild wählt jeder Funker (Handschuh ab Werk oder Xeno).</summary>
    public const string Stopp = "stopp";

    private static readonly Lazy<Inhalt> Geladen = new(Laden, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Fassung der Katalogdatei.</summary>
    public static int Fassung => Geladen.Value.Fassung;

    /// <summary>Alle Motive in Katalogfolge (Befehle, Zustände, Ampel, Countdown, Bereit).</summary>
    public static ImmutableArray<Meldungsmotiv> Motive => Geladen.Value.Motive;

    /// <summary>Die Ausführungen in fester Folge: Schlicht, Metall, Nur Schrift.</summary>
    public static ImmutableArray<string> Ausfuehrungen { get; } = [Schlicht, Metall, Schrift];

    /// <summary>Das Motiv zu einer Kennung oder <c>null</c>.</summary>
    public static Meldungsmotiv? Motiv(string? kennung) =>
        kennung is not null && Geladen.Value.Nach.TryGetValue(kennung, out var m) ? m : null;

    /// <summary>Ob die Kennung im Katalog steht.</summary>
    public static bool Kennt(string? kennung) => Motiv(kennung) is not null;

    /// <summary>Ob die Kennung ein Motiv dieser Art ist.</summary>
    public static bool IstArt(string? kennung, Meldungsart art) => Motiv(kennung)?.Art == art;

    /// <summary>Die Motive einer Art in Katalogfolge.</summary>
    public static ImmutableArray<Meldungsmotiv> VonArt(Meldungsart art) => [.. Motive.Where(m => m.Art == art)];

    /// <summary>Das Countdown-Motiv zu einer Form (<see cref="FormStartzeit"/>, <see cref="FormRestzeit"/>) oder <c>null</c>.</summary>
    public static string? MotivFuerForm(string? form) => form switch
    {
        FormStartzeit => StartzeitMotiv,
        FormRestzeit => RestzeitMotiv,
        _ => null,
    };

    /// <summary>Die Form eines Countdown-Motivs oder <c>null</c>.</summary>
    public static string? FormFuerMotiv(string? motiv) => motiv switch
    {
        StartzeitMotiv => FormStartzeit,
        RestzeitMotiv => FormRestzeit,
        _ => null,
    };

    /// <summary>
    /// Die Ausführung, in der das Motiv gezeigt wird: die gewünschte, wenn es sie gibt, sonst Schlicht (Metall fehlt für los,
    /// stopfen, pause; Nur Schrift gibt es nur für diese drei).
    /// </summary>
    public static string AusfuehrungFuer(string kennung, string? gewuenscht) =>
        Motiv(kennung) is { } m && m.HatAusfuehrung(gewuenscht) ? gewuenscht! : Schlicht;

    /// <summary>
    /// Das Bild eines Motivs: <paramref name="wahl"/>, wenn das Motiv es als Bild führt (Stopp: <c>stopp_handschuh</c> oder
    /// <c>stopp_xeno</c>), sonst sein erstes. Für ein unbekanntes Motiv <c>null</c>.
    /// </summary>
    public static string? Bild(string kennung, string? wahl = null) => Motiv(kennung) is not { } m
        ? null
        : wahl is not null && m.Bilder.Contains(wahl, StringComparer.Ordinal) ? wahl : m.Bilder[0];

    /// <summary>
    /// Name der eingebetteten Bildressource im Client: <c>Meldungen.&lt;ausfuehrung&gt;.&lt;bild&gt;.png</c>, mit dem Ersatz Schlicht
    /// (<see cref="AusfuehrungFuer"/>) und der Bildwahl (<see cref="Bild"/>). Für ein unbekanntes Motiv <c>null</c>.
    /// </summary>
    public static string? Ressource(string kennung, string? ausfuehrung = null, string? bildwahl = null) =>
        Bild(kennung, bildwahl) is { } bild ? RessourceVon(AusfuehrungFuer(kennung, ausfuehrung), bild) : null;

    /// <summary>Name der Bildressource ohne Ersatz: genau diese Ausführung und dieses Bild.</summary>
    public static string RessourceVon(string ausfuehrung, string bild) => "Meldungen." + ausfuehrung + "." + bild + ".png";

    // ---------------------------------------------------------------- Laden

    private sealed record Inhalt(int Fassung, ImmutableArray<Meldungsmotiv> Motive, FrozenDictionary<string, Meldungsmotiv> Nach);

    /// <summary>Liest die eingebettete Datei ohne Reflexion (JsonDocument); ein Fehler darin ist ein Baufehler und wirft.</summary>
    private static Inhalt Laden()
    {
        using var strom = typeof(Meldungskatalog).Assembly.GetManifestResourceStream(Katalogressource)
            ?? throw new InvalidOperationException("Meldungskatalog fehlt: " + Katalogressource);
        using var dokument = JsonDocument.Parse(strom);
        var wurzel = dokument.RootElement;
        var fassung = wurzel.GetProperty("fassung").GetInt32();
        var motive = ImmutableArray.CreateBuilder<Meldungsmotiv>();
        var nach = new Dictionary<string, Meldungsmotiv>(StringComparer.Ordinal);
        var bilder = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in wurzel.GetProperty("motive").EnumerateArray())
        {
            var kennung = Text(e, "kennung");
            if (!Enum.TryParse<Meldungsart>(Text(e, "art"), ignoreCase: false, out var art) || !Enum.IsDefined(art))
                throw new InvalidDataException("Meldungskatalog: unbekannte Art bei " + kennung);
            ImmutableArray<string> eigene = e.TryGetProperty("bilder", out var b) ? [.. b.EnumerateArray().Select(x => x.GetString() ?? "")] : [kennung];
            ImmutableArray<string> ausfuehrungen = [.. e.GetProperty("ausfuehrungen").EnumerateArray().Select(x => x.GetString() ?? "")];
            if (kennung.Length > Grenzen.MotivKennungMax || !KennungGueltig(kennung) || eigene.IsEmpty || eigene.Any(x => !KennungGueltig(x) || !bilder.Add(x))
                || !ausfuehrungen.Contains(Schlicht) || ausfuehrungen.Any(a => !Ausfuehrungen.Contains(a)))
                throw new InvalidDataException("Meldungskatalog: Eintrag ungültig: " + kennung);
            var motiv = new Meldungsmotiv(kennung, art, Text(e, "name"), eigene, ausfuehrungen);
            if (!nach.TryAdd(kennung, motiv)) throw new InvalidDataException("Meldungskatalog: Kennung doppelt: " + kennung);
            motive.Add(motiv);
        }
        return new Inhalt(fassung, motive.ToImmutable(), nach.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>Kennungen und Bildnamen: a-z, 0-9 und _ (sie werden Teil eines Ressourcennamens).</summary>
    private static bool KennungGueltig(string s) => s.Length > 0 && s.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    private static string Text(JsonElement e, string name) =>
        e.GetProperty(name).GetString() is { Length: > 0 } s ? s : throw new InvalidDataException("Meldungskatalog: " + name + " fehlt");
}
