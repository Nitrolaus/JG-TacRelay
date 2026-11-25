// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Identitaet;

/// <summary>
/// Ob ein Antrag zu einem bestehenden Konto passen könnte (Sarevocs Fall vom 01.10.2026: Antrag mit seinem SC- und Discord-Namen neben dem
/// Konto aus 0.1.0, freigegeben als neues Konto). Verglichen werden die Namensschlüssel (NFKC, Groß/Klein wie bei der
/// Eindeutigkeit) der ganzen Namen und ihres Teils vor dem ersten Schrägstrich: "Mustermann/Max" passt zu "Mustermann" und zum
/// Discord-Benutzernamen "mustermann", der Rufname dahinter allein nicht. Nur ein Hinweis für den Admin; angehängt wird nie von
/// selbst (3.5, Regeln für Admins beim Freigeben).
/// </summary>
public static class Kontovorschlag
{
    /// <summary>Namensschlüssel eines Namens und seines Teils vor dem Schrägstrich; leere und einzelne Zeichen zählen nicht.</summary>
    public static IEnumerable<string> Schluessel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) yield break;
        var ganz = name.Trim();
        if (ganz.Length >= 2) yield return Zeichenregeln.Namensschluessel(ganz);
        var schraeg = ganz.IndexOf('/', StringComparison.Ordinal);
        if (schraeg < 0) yield break;
        var vorn = ganz[..schraeg].Trim();
        if (vorn.Length >= 2) yield return Zeichenregeln.Namensschluessel(vorn);
    }

    /// <summary>Ob ein Name des Antrags (SC-Name, Discord-Name) zu einem Namen des Kontos passt.</summary>
    public static bool Passt(IEnumerable<string?> antrag, IEnumerable<string?> konto)
    {
        ArgumentNullException.ThrowIfNull(antrag);
        ArgumentNullException.ThrowIfNull(konto);
        var a = antrag.SelectMany(Schluessel).ToHashSet(StringComparer.Ordinal);
        return a.Count > 0 && konto.SelectMany(Schluessel).Any(a.Contains);
    }

    /// <summary>Die Namen eines Kontos für den Vergleich: Anzeigename, Discord-Benutzername (Konten aus 0.1.0), Discord-Name, SC-Namen.</summary>
    public static IEnumerable<string?> NamenVon(Konto k)
    {
        ArgumentNullException.ThrowIfNull(k);
        return [k.Name, k.Benutzername, k.DiscordName, .. k.ScNamen];
    }
}
