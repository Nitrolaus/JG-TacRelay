// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>
/// Regeln der Kanalsperren (6.17, seit 0.6.0) für Schreiber, Weiterleitung und Abbildung: was sich sperren lässt, wen eine Sperre für
/// alle trifft und welche Sperre ein Netz oder einen Kanal für ein Konto gerade sperrt. Eine Sperre auf ein Kanalnetz trifft alle seine
/// Ch., über das Regelnetz des Kanals (6.16). Allokationsfrei, damit der Heißpfad sie je Paket fragen kann.
/// </summary>
public static class Sperrregeln
{
    /// <summary>
    /// Ob sich <paramref name="netzId"/> sperren lässt: ein gewöhnliches Netz, ein Kanal oder ein Kanalnetz (alle Ch.). Global (Ch. 1, mit
    /// allen Global-Kanälen) nie, dafür bleibt Stummschalten; eine Ruhe hört und sendet ohnehin nicht.
    /// </summary>
    public static Fehlercode ZielPruefen(IReadOnlyDictionary<ushort, Netz> netze, ushort netzId) => Kanaele.Einordnen(netze, netzId) switch
    {
        Netzart.Gewoehnlich or Netzart.Kanal or Netzart.Kanalnetz => Fehlercode.Keiner,
        Netzart.Unbekannt => Fehlercode.Unbekannt,
        _ => Fehlercode.Ungueltig,
    };

    /// <summary>Ob die Sperre zu <paramref name="jetzt"/> läuft und das Netz <paramref name="netzId"/> mit seinem Regelnetz trifft.</summary>
    public static bool Trifft(in Kanalsperre sperre, ushort netzId, ushort regelnetz, long jetzt) =>
        sperre.Bis > jetzt && (sperre.NetzId == netzId || sperre.NetzId == regelnetz);

    /// <summary>
    /// Ob eine Sperre für alle das Konto trifft: nur ohne wirksames <c>ZUWEISEN</c> (die Einsatzleitung bleibt ausgenommen), ein ADMIN-Konto
    /// nur, wenn ein ADMIN sie gesetzt hat (Rangregel 6.10, auch im Funker-Modus).
    /// </summary>
    public static bool AlleTreffen(Konto konto, Recht rechte, in Kanalsperre sperre)
    {
        ArgumentNullException.ThrowIfNull(konto);
        return !rechte.Hat(Recht.Zuweisen) && (konto.Stufe != Stufe.Admin || sperre.AuchAdmins);
    }

    /// <summary>
    /// Die Sperre, die das Netz (Spalte mit Regelnetz) für das Konto zu <paramref name="jetzt"/> sperrt: zuerst die eigenen, dann die für
    /// alle. <paramref name="rechte"/> sind die wirksamen Rechte zu <paramref name="jetzt"/>.
    /// </summary>
    public static bool Gesperrt(Konto konto, Recht rechte, ImmutableArray<Kanalsperre> alle, ushort netzId, ushort regelnetz, long jetzt, out Kanalsperre sperre)
    {
        ArgumentNullException.ThrowIfNull(konto);
        foreach (var s in konto.Sperren)
        {
            if (!Trifft(s, netzId, regelnetz, jetzt)) continue;
            sperre = s;
            return true;
        }
        if (!alle.IsDefaultOrEmpty)
        {
            foreach (var s in alle)
            {
                if (!Trifft(s, netzId, regelnetz, jetzt) || !AlleTreffen(konto, rechte, s)) continue;
                sperre = s;
                return true;
            }
        }
        sperre = default;
        return false;
    }

    /// <summary>
    /// Wie <see cref="Gesperrt(Konto, Recht, ImmutableArray{Kanalsperre}, ushort, ushort, long, out Kanalsperre)"/> für eine Netz-ID aus
    /// einem Feld oder einer Anfrage: Nur ein gewöhnliches Netz und ein Kanal sind je gesperrt, Global, dessen Ch. 1, eine Ruhe und Unbekanntes nie.
    /// </summary>
    public static bool Gesperrt(IReadOnlyDictionary<ushort, Netz> netze, ImmutableArray<Kanalsperre> alle, Konto konto, Recht rechte, ushort netzId, long jetzt,
        out Kanalsperre sperre)
    {
        sperre = default;
        if (Kanaele.Einordnen(netze, netzId) is not (Netzart.Gewoehnlich or Netzart.Kanal)) return false;
        return Gesperrt(konto, rechte, alle, netzId, Kanaele.Regelnetz(netze, netzId), jetzt, out sperre);
    }

    /// <summary>Alle Sperren, die das Konto zu <paramref name="jetzt"/> treffen (<c>Mitgliedschaften.gesperrt</c>): die eigenen, dann die für alle.</summary>
    public static List<(Kanalsperre Sperre, bool Alle)> Wirksam(Konto konto, Recht rechte, ImmutableArray<Kanalsperre> alle, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(konto);
        var liste = new List<(Kanalsperre, bool)>();
        foreach (var s in konto.Sperren)
            if (s.Bis > jetzt) liste.Add((s, false));
        if (!alle.IsDefaultOrEmpty)
        {
            foreach (var s in alle)
                if (s.Bis > jetzt && AlleTreffen(konto, rechte, s)) liste.Add((s, true));
        }
        return liste;
    }
}
