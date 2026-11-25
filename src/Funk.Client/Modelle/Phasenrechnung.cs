// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Was ein Phasenwechsel ungefähr bewirkt, aus NetzListe und Praesenz gerechnet, ohne Anfrage an den Server (RAID-S2-ENTWURF
/// Abschnitt 3, "Vorschau im Client"): Ein Netzbild kennt keine Konflikte. Betroffen ist jeder mit Platz, dessen Rolle in der alten
/// oder neuen Phase eine Variante hat; ausgelassen, wen der Schaltende nach der Rangregel nicht einteilen darf (ein Admin ohne
/// Funker-Modus, wenn ein Nicht-Admin schaltet). Die genaue Zahl nennt danach der Server (nur wessen Felder sich ändern).
/// </summary>
public static class Phasenrechnung
{
    /// <summary>Die Phasen nach Nummer.</summary>
    public static IReadOnlyList<PhaseInfo> Geordnet(FunkLage lage)
    {
        ArgumentNullException.ThrowIfNull(lage);
        return [.. lage.Phasen.OrderBy(p => p.Nr)];
    }

    /// <summary>Die nächste (+1) oder vorige (-1) Phase nach der laufenden, oder <c>null</c> am Ende.</summary>
    public static PhaseInfo? Naechste(FunkLage lage, int richtung)
    {
        var phasen = Geordnet(lage);
        var i = phasen.ToList().FindIndex(p => p.Nr == lage.PhaseNr);
        if (i < 0) i = 0;
        var j = i + Math.Sign(richtung);
        return j >= 0 && j < phasen.Count && j != i ? phasen[j] : null;
    }

    /// <summary>"2 Entern".</summary>
    public static string Name(PhaseInfo phase)
    {
        ArgumentNullException.ThrowIfNull(phase);
        return phase.Nr.ToString(CultureInfo.CurrentCulture) + " " + phase.Name;
    }

    /// <summary>Die laufende Phase als "1 Anflug" (ohne Push die Grundstellung).</summary>
    public static string Laufend(FunkLage lage)
    {
        ArgumentNullException.ThrowIfNull(lage);
        return lage.PhaseVon(lage.PhaseNr) is { } p ? Name(p) : lage.PhaseNr.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>Wer bei einem Wechsel von <paramref name="von"/> nach <paramref name="nach"/> umgestellt und wer ausgelassen würde.</summary>
    public static (IReadOnlyList<FunkerInfo> Umgestellt, IReadOnlyList<FunkerInfo> Ausgelassen) Betroffene(FunkLage lage, byte von, byte nach)
    {
        ArgumentNullException.ThrowIfNull(lage);
        var rollen = new HashSet<ushort>();
        foreach (var p in new[] { lage.PhaseVon(von), lage.PhaseVon(nach) })
            if (p is not null) rollen.UnionWith(p.Varianten.Keys);
        var umgestellt = new List<FunkerInfo>();
        var ausgelassen = new List<FunkerInfo>();
        var ichAdmin = lage.Konto?.IstAdmin == true;
        foreach (var f in lage.Funker)
        {
            if (f.RolleId is not { } r || !rollen.Contains(r) || (f.Platz is null && !f.Online)) continue;
            if (!ichAdmin && f.Zeichen.Contains(Zeichensatz.Admin) && f.Details?.AlsFunker != true) ausgelassen.Add(f);
            else umgestellt.Add(f);
        }
        return (umgestellt, ausgelassen);
    }

    /// <summary>"Nordwind (Admin), Distel (Admin)": die Ausgelassenen für die Meldezeile.</summary>
    public static string Ausgelassene(IEnumerable<string> namen) => string.Join(", ", namen.Select(n => n + " (Admin)"));

    /// <summary>
    /// Der Text beim ersten Druck: "Phase 2/3 ENTERN? Nochmal drücken - 9 Funker, ausgelassen: Nordwind (Admin)". Die Zahl rechnet der
    /// Client aus NetzListe und Praesenz.
    /// </summary>
    public static string ScharfText(FunkLage lage, PhaseInfo ziel)
    {
        ArgumentNullException.ThrowIfNull(lage);
        ArgumentNullException.ThrowIfNull(ziel);
        var (umgestellt, ausgelassen) = Betroffene(lage, lage.PhaseNr, ziel.Nr);
        var hoechste = Geordnet(lage).Max(p => p.Nr);
        var text = "Phase " + ziel.Nr.ToString(CultureInfo.CurrentCulture) + "/" + hoechste.ToString(CultureInfo.CurrentCulture) + " " + ziel.Kurz.ToUpper(CultureInfo.CurrentCulture)
            + "? Nochmal drücken - " + umgestellt.Count.ToString(CultureInfo.CurrentCulture) + " Funker";
        if (ausgelassen.Count > 0) text += ", ausgelassen: " + Ausgelassene(ausgelassen.Select(f => f.ScName ?? f.Name));
        return text;
    }
}
