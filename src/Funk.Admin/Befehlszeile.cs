// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;

namespace Funk.Admin;

/// <summary>
/// Zerlegt <c>befehl [positional] --name wert --schalter</c>; Wiederholungen sammeln sich (etwa <c>--pin</c>). Ob die
/// Optionen zum Befehl passen, prüft <see cref="Befehlsoptionen.Pruefen"/>, bevor etwas geschieht; die Zerlegung selbst
/// lehnt nichts ab.
/// </summary>
public sealed class Befehlszeile
{
    private readonly Dictionary<string, List<string>> _werte = new(StringComparer.Ordinal);
    private readonly HashSet<string> _schalter = new(StringComparer.Ordinal);
    private readonly List<(string Name, string? Wert)> _genannt = [];

    public Befehlszeile(IReadOnlyList<string> args, IReadOnlySet<string> schalterNamen)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(schalterNamen);
        Befehl = args.Count > 0 ? args[0] : "hilfe";
        for (var i = 1; i < args.Count; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                Positional.Add(a);
                continue;
            }
            var name = a[2..];
            if (schalterNamen.Contains(name))
            {
                _schalter.Add(name);
                _genannt.Add((name, null));
                continue;
            }
            if (i + 1 >= args.Count)
            {
                // Erst Befehlsoptionen.Pruefen sagt, ob die Option überhaupt bekannt ist; dann erst "braucht einen Wert".
                OhneWert = name;
                _genannt.Add((name, null));
                continue;
            }
            if (!_werte.TryGetValue(name, out var liste)) _werte[name] = liste = [];
            var wert = args[++i];
            liste.Add(wert);
            _genannt.Add((name, wert));
        }
    }

    public string Befehl { get; }

    public List<string> Positional { get; } = [];

    /// <summary>Alle Optionen und Schalter in der Reihenfolge der Eingabe, Schalter ohne Wert.</summary>
    public IReadOnlyList<(string Name, string? Wert)> Genannt => _genannt;

    /// <summary>Die erste Option am Ende ohne Wert dahinter, oder <c>null</c>.</summary>
    public string? OhneWert { get; }

    public string? Wert(string name) => _werte.TryGetValue(name, out var l) ? l[^1] : null;

    public string Pflicht(string name) => Wert(name) ?? throw new ArgumentException($"--{name} fehlt.");

    public IReadOnlyList<string> Alle(string name) => _werte.TryGetValue(name, out var l) ? l : [];

    public bool Schalter(string name) => _schalter.Contains(name);

    public uint? Zahl(string name) =>
        Wert(name) is { } w ? uint.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out var z) ? z : throw new ArgumentException($"--{name} ist keine Zahl.") : null;
}
