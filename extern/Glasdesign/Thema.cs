// Glasdesign - Maintainer: Sarevoc
using System.Collections;
using System.Resources;
using System.Windows;

namespace Glasdesign;

/// <summary>
/// Farbstellungen laden und zur Laufzeit umschalten.
/// <code>
/// Glasdesign.Thema.Setzen(Application.Current, "Weinrot");
/// </code>
/// Eine Farbstellung ist ein ResourceDictionary mit genau den Schlüsseln aus <see cref="Schluessel"/>. Eingebaut sind alle
/// XAML-Dateien unter Farbstellungen\ dieser Bibliothek (eine neue Datei dort ist ohne Codeänderung dabei); eine eigene
/// Datei außerhalb lädt <see cref="Laden(Uri)"/> (pack-Adresse der eigenen Anwendung oder Dateipfad).
/// Beim Setzen wird die zuletzt gesetzte Farbstellung auf der obersten Ebene der Ressourcen ersetzt und die neue ans Ende
/// gestellt; sie überdeckt damit die Vorgabe "Standard", die Glas.xaml mitbringt. Die Stile lesen alle Farben als
/// DynamicResource, deshalb wechseln offene Fenster sofort.
/// </summary>
public static class Thema
{
    private const string Komponente = "pack://application:,,,/Glasdesign;component/";
    private const string Ordner = "farbstellungen/";

    private static List<(string Name, string Datei)>? _eingebaut;

    /// <summary>
    /// Frühere Namen eingebauter Farbstellungen und ihr heutiger Name. Seit 19.10.2026 (Sarevoc, Q28) heißt die Farbstellung mit
    /// Anthrazit, Silber und Weinrot "Weinrot". Ihr alter Name steht nur noch hier, damit gespeicherte Werte (Entwürfe und
    /// Vorlagen von Glint, Einstellungen von Programmen) ohne Verlust weiter laden.
    /// </summary>
    private static readonly Dictionary<string, string> FruehereNamen = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    /// <summary>Nach jedem <see cref="Setzen(ResourceDictionary, ResourceDictionary)"/>; Absender ist das Ziel.</summary>
    public static event EventHandler? Gewechselt;

    /// <summary>
    /// Fassung der Bibliothek wie in Directory.Build.props, mit Vorabkennung (etwa "0.4.0-beta.1"), ohne den Commit, den
    /// das SDK mit "+" anhängt. Für Fenstertitel, Export und Herkunftsangaben.
    /// </summary>
    public static string Fassung { get; } = FassungVon(typeof(Thema).Assembly);

    /// <summary>Die Fassung einer beliebigen Assembly nach derselben Regel (für Programme, die Glasdesign nutzen).</summary>
    public static string FassungVon(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var info = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion;
        if (!string.IsNullOrEmpty(info)) return info.Split('+')[0];
        return assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";
    }

    /// <summary>Namen der eingebauten Farbstellungen (aus den Dateien unter Farbstellungen\), etwa Standard und Weinrot.</summary>
    public static IReadOnlyList<string> Eingebaut => (_eingebaut ??= Finden()).Select(e => e.Name).ToList();

    /// <summary>
    /// Der heutige Name zu <paramref name="name"/>: Ein früherer Name einer eingebauten Farbstellung wird zu ihrem heutigen
    /// (Groß- und Kleinschreibung egal), alles andere kommt unverändert zurück, auch ein unbekannter Name.
    /// <see cref="Laden(string)"/>, <see cref="Quelltext"/> und <see cref="Setzen(Application, string)"/> nehmen frühere
    /// Namen von selbst an; wer einen gespeicherten Namen weiterführt oder anzeigt, liest ihn hierüber.
    /// </summary>
    public static string HeutigerName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return FruehereNamen.TryGetValue(name.Trim(), out var heute) ? heute : name;
    }

    /// <summary>
    /// Der Quelltext einer eingebauten Farbstellung (so wie er im Repository unter src\Glasdesign\Farbstellungen\ steht) und
    /// ihr Dateiname; null, wenn es sie nicht gibt. Für Werkzeuge wie Glint, die daraus einen Patch erzeugen.
    /// </summary>
    public static (string Datei, string Text)? Quelltext(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _eingebaut ??= Finden();
        var gesucht = HeutigerName(name).Trim();
        var datei = _eingebaut.FirstOrDefault(e => string.Equals(e.Name, gesucht, StringComparison.OrdinalIgnoreCase)).Datei;
        if (datei is null) return null;
        // Die BAML-Namen sind klein geschrieben (standard), die eingebettete Datei trägt den echten Namen (Standard.xaml).
        var assembly = typeof(Thema).Assembly;
        var ressource = assembly.GetManifestResourceNames().FirstOrDefault(n => string.Equals(n, "Glasdesign.Farbstellungen." + datei + ".xaml", StringComparison.OrdinalIgnoreCase));
        if (ressource is null) return null;
        using var strom = assembly.GetManifestResourceStream(ressource);
        if (strom is null) return null;
        using var leser = new StreamReader(strom);
        return (ressource["Glasdesign.Farbstellungen.".Length..], leser.ReadToEnd());
    }

    /// <summary>pack-Adresse einer Datei dieser Bibliothek.</summary>
    public static Uri Adresse(string relativ)
    {
        _ = Application.Current; // Statischer Konstruktor von Application meldet das pack-Schema an.
        return new Uri(Komponente + relativ, UriKind.Absolute);
    }

    /// <summary>
    /// Frisch geladene Stile: mit Vorgaben Glas.xaml (Farbstellung Standard, alle Glas.*-Stile, Vorgabestile für Button,
    /// CheckBox und ToolTip), ohne Vorgaben Glas.Stile.xaml (nur die benannten Stile, keine Farbstellung).
    /// </summary>
    public static ResourceDictionary Stile(bool mitVorgaben = true) =>
        new() { Source = Adresse(mitVorgaben ? "Glas.xaml" : "Glas.Stile.xaml") };

    /// <summary>Eine eingebaute Farbstellung nach Namen (Groß- und Kleinschreibung egal, frühere Namen wie <see cref="HeutigerName"/>).</summary>
    public static ResourceDictionary Laden(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _eingebaut ??= Finden();
        var gesucht = HeutigerName(name).Trim();
        var treffer = _eingebaut.FirstOrDefault(e => string.Equals(e.Name, gesucht, StringComparison.OrdinalIgnoreCase)).Datei
                      ?? throw new ArgumentException($"Unbekannte Farbstellung \"{name}\". Vorhanden: {string.Join(", ", Eingebaut)}.", nameof(name));
        return Laden(Adresse("Farbstellungen/" + treffer + ".xaml"));
    }

    /// <summary>
    /// Eine Farbstellung aus einer pack-Adresse oder einer losen XAML-Datei (file-Adresse). Lose Dateien liest ein
    /// XAML-Leser, der Glasdesign als lokale Assembly kennt: So findet eine Kopie von Standard.xaml ihr
    /// <c>xmlns:gd="clr-namespace:Glasdesign"</c> (Glanzprofil) auch außerhalb der Bibliothek, ohne Änderung an der Kopie.
    /// </summary>
    public static ResourceDictionary Laden(Uri quelle)
    {
        ArgumentNullException.ThrowIfNull(quelle);
        if (quelle.IsAbsoluteUri && quelle.Scheme == "pack")
        {
            _ = Application.Current;
            return new ResourceDictionary { Source = quelle };
        }
        if (!quelle.IsAbsoluteUri || !quelle.IsFile) return new ResourceDictionary { Source = quelle };
        using var datei = File.OpenRead(quelle.LocalPath);
        using var xml = System.Xml.XmlReader.Create(datei);
        var einstellungen = new System.Xaml.XamlXmlReaderSettings { LocalAssembly = typeof(Thema).Assembly, BaseUri = quelle };
        using var leser = new System.Xaml.XamlXmlReader(xml, System.Windows.Markup.XamlReader.GetWpfSchemaContext(), einstellungen);
        return System.Windows.Markup.XamlReader.Load(leser) as ResourceDictionary
               ?? throw new ArgumentException("Keine Farbstellung (ResourceDictionary): " + quelle, nameof(quelle));
    }

    /// <summary>Schlüssel aus <see cref="Schluessel.Alle"/>, die in <paramref name="farbstellung"/> fehlen (leer = vollständig).</summary>
    public static IReadOnlyList<string> Fehlende(ResourceDictionary farbstellung)
    {
        ArgumentNullException.ThrowIfNull(farbstellung);
        return Schluessel.Alle.Where(k => !farbstellung.Contains(k)).ToList();
    }

    /// <summary>Setzt eine eingebaute Farbstellung in die Ressourcen der Anwendung und richtet die Hilfe ein.</summary>
    public static void Setzen(Application app, string name)
    {
        ArgumentNullException.ThrowIfNull(app);
        Setzen(app.Resources, Laden(name));
        Hilfe.Einrichten();
    }

    /// <summary>Setzt eine Farbstellung aus einer Datei in die Ressourcen der Anwendung.</summary>
    public static void Setzen(Application app, Uri quelle)
    {
        ArgumentNullException.ThrowIfNull(app);
        Setzen(app.Resources, Laden(quelle));
        Hilfe.Einrichten();
    }

    /// <summary>Setzt eine eingebaute Farbstellung in beliebige Ressourcen (etwa eines Fensters).</summary>
    public static void Setzen(ResourceDictionary ziel, string name) => Setzen(ziel, Laden(name));

    /// <summary>
    /// Ersetzt die zuletzt gesetzte Farbstellung in <paramref name="ziel"/> durch <paramref name="farbstellung"/>.
    /// Wirft <see cref="ArgumentException"/>, wenn Schlüssel fehlen, und nennt sie.
    /// </summary>
    public static void Setzen(ResourceDictionary ziel, ResourceDictionary farbstellung)
    {
        ArgumentNullException.ThrowIfNull(ziel);
        ArgumentNullException.ThrowIfNull(farbstellung);
        var fehlende = Fehlende(farbstellung);
        if (fehlende.Count > 0)
            throw new ArgumentException("Farbstellung unvollständig, es fehlen: " + string.Join(", ", fehlende), nameof(farbstellung));
        // Nur Farbstellungen auf oberster Ebene, die den Namen selbst tragen; Glas.xaml bringt Standard nur verschachtelt mit.
        for (var i = ziel.MergedDictionaries.Count - 1; i >= 0; i--)
            if (EigeneSchluessel(ziel.MergedDictionaries[i]).Contains(Schluessel.Name)) ziel.MergedDictionaries.RemoveAt(i);
        ziel.MergedDictionaries.Add(farbstellung);
        Gewechselt?.Invoke(ziel, EventArgs.Empty);
    }

    /// <summary>Name der wirksamen Farbstellung in <paramref name="ziel"/>, oder null.</summary>
    public static string? Aktuell(ResourceDictionary ziel)
    {
        ArgumentNullException.ThrowIfNull(ziel);
        return ziel[Schluessel.Name] as string;
    }

    private static HashSet<object> EigeneSchluessel(ResourceDictionary d) => d.Keys.Cast<object>().ToHashSet();

    private static List<(string Name, string Datei)> Finden()
    {
        var namen = new List<(string Name, string Datei)>();
        var assembly = typeof(Thema).Assembly;
        using var strom = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources");
        if (strom is null) return namen;
        using var leser = new ResourceReader(strom);
        foreach (DictionaryEntry eintrag in leser)
        {
            var schluessel = (string)eintrag.Key;
            if (!schluessel.StartsWith(Ordner, StringComparison.Ordinal) || !schluessel.EndsWith(".baml", StringComparison.Ordinal)) continue;
            var datei = schluessel[Ordner.Length..^".baml".Length];
            if (Laden(Adresse("Farbstellungen/" + datei + ".xaml"))[Schluessel.Name] is string name) namen.Add((name, datei));
        }
        // Standard zuerst: Es ist die Vorgabe. Danach nach Namen.
        return namen.OrderBy(n => n.Name == "Standard" ? 0 : 1).ThenBy(n => n.Name, StringComparer.Ordinal).ToList();
    }
}
