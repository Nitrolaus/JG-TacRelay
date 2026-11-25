// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;

namespace Funk.Testclient;

/// <summary>Einfache Befehlszeile: erstes Wort ist der Befehl, dann <c>--name wert</c> oder Schalter <c>--name</c>.</summary>
internal sealed class Befehlszeile
{
    private readonly Dictionary<string, string> _werte = new(StringComparer.Ordinal);
    private readonly HashSet<string> _schalter = new(StringComparer.Ordinal);

    public Befehlszeile(string[] args, IReadOnlySet<string> schalter)
    {
        Befehl = args.Length > 0 ? args[0] : "hilfe";
        for (var i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Unerwartet: " + args[i]);
            var name = args[i][2..];
            if (schalter.Contains(name))
            {
                _schalter.Add(name);
                continue;
            }
            if (i + 1 >= args.Length) throw new ArgumentException("Wert fehlt für --" + name);
            _werte[name] = args[++i];
        }
    }

    public string Befehl { get; }

    public string? Wert(string name) => _werte.GetValueOrDefault(name);

    public string Pflicht(string name) => Wert(name) ?? throw new ArgumentException("--" + name + " fehlt.");

    public int Zahl(string name, int standard) =>
        Wert(name) is { } w ? int.Parse(w, NumberStyles.Integer, CultureInfo.InvariantCulture) : standard;

    public double Kommazahl(string name, double standard) =>
        Wert(name) is { } w ? double.Parse(w, NumberStyles.Float, CultureInfo.InvariantCulture) : standard;

    public bool Schalter(string name) => _schalter.Contains(name);
}
