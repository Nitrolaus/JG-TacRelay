// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Server.Lage;

/// <summary>Feld aus einer Rolle, samt Markierung <c>sendenStandard</c> für die Sendeauswahl.</summary>
public readonly record struct Rollenfeld(Mitgliedschaft Mitgliedschaft, bool Sendevorschlag);

/// <summary>Regeln für Feldlisten, STANDARD, Rollenfelder und Sendeauswahl (6.9).</summary>
public static class Feldregeln
{
    public const string GrundKeinFreiesFeld = "KEIN_FREIES_FELD";

    /// <summary>Kennungen bestehen aus [A-Z0-9_], 1-16 Zeichen (4.5 NetzListe).</summary>
    public static bool KennungGueltig(string? kennung)
    {
        if (string.IsNullOrEmpty(kennung) || kennung.Length > 16) return false;
        foreach (var c in kennung)
            if (!(c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')) return false;
        return true;
    }

    /// <summary>
    /// Feldliste eines Kontos: höchstens 16, Feld 0-15 aufsteigend und eindeutig, je Netz ein Feld; das Netz ist ein gewöhnliches Netz,
    /// ein Kanal oder eine Ruhe (6.16), nie Global, Ch. 1 von Global oder ein Kanalnetz selbst.
    /// </summary>
    public static string? FelderPruefen(ImmutableArray<Mitgliedschaft> felder, IReadOnlyDictionary<ushort, Netz> netze)
    {
        ArgumentNullException.ThrowIfNull(netze);
        if (felder.IsDefault) return "Felder fehlen";
        if (felder.Length > Fristen.FelderMax) return "mehr als 16 Felder";
        var vorher = -1;
        var gesehen = new HashSet<ushort>();
        foreach (var f in felder)
        {
            if (f.Feld > 15) return $"Feld {f.Feld} außerhalb 0-15";
            if (f.Feld <= vorher) return "Felder nicht aufsteigend oder doppelt";
            vorher = f.Feld;
            if (f.NetzId == Netz.GlobalId) return "Global steht in einem Feld";
            if (!Kanaele.AlsFeld(netze, f.NetzId)) return $"Kanal {f.NetzId} unbekannt oder kein Feld";
            if (!gesehen.Add(f.NetzId)) return $"Kanal {f.NetzId} in mehr als einem Feld";
        }
        return null;
    }

    /// <summary>Feldliste aus einer Anfrage (Verschieben, Aufstellung, SatzAnwenden), vor dem Übernehmen.</summary>
    public static Fehlercode VorgabenPruefen(IReadOnlyList<Feldvorgabe> felder, IReadOnlyDictionary<ushort, Netz> netze)
    {
        ArgumentNullException.ThrowIfNull(felder);
        ArgumentNullException.ThrowIfNull(netze);
        if (felder.Count > Fristen.FelderMax) return Fehlercode.Ungueltig;
        Span<bool> feldBelegt = stackalloc bool[16];
        var netzGesehen = new HashSet<ushort>();
        foreach (var f in felder)
        {
            if (f.Feld > 15 || feldBelegt[f.Feld]) return Fehlercode.Ungueltig;
            feldBelegt[f.Feld] = true;
            var code = FeldnetzPruefen(netze, f.NetzId);
            if (code != Fehlercode.Keiner) return code;
            if (!netzGesehen.Add(f.NetzId)) return Fehlercode.Ungueltig;
        }
        return Fehlercode.Keiner;
    }

    /// <summary>
    /// Netz-ID für ein Feld aus einer Anfrage (FeldSetzen, Verschieben, Aufstellung, Satz): unbekannt oder ungültiger Kanal ergibt
    /// UNBEKANNT; Global, Ch. 1 von Global und ein Kanalnetz selbst ergeben UNGUELTIG (6.16).
    /// </summary>
    public static Fehlercode FeldnetzPruefen(IReadOnlyDictionary<ushort, Netz> netze, ushort netzId) => Kanaele.Einordnen(netze, netzId) switch
    {
        Netzart.Gewoehnlich or Netzart.Kanal or Netzart.Ruhe => Fehlercode.Keiner,
        Netzart.Unbekannt => Fehlercode.Unbekannt,
        _ => Fehlercode.Ungueltig,
    };

    /// <summary>
    /// Felder einer Rolle (6.9, 6.16): Feld 0-15 eindeutig, genau eines von Netz und Kennung. Das Netz ist ein gewöhnliches Netz oder ein
    /// Kanalnetz (Global nur, wenn es eines ist), nie eine Kanal-ID; <c>ch</c> nur an einem Kanalnetz, in seiner Liste und nicht Ch. 1 von Global.
    /// </summary>
    public static string? RollePruefen(Rolle rolle, IReadOnlyDictionary<ushort, Netz> netze)
    {
        ArgumentNullException.ThrowIfNull(rolle);
        ArgumentNullException.ThrowIfNull(netze);
        if (rolle.Id is 0 or 0xFFFF) return "Rollen-ID außerhalb 1-65534";
        if (rolle.Felder.IsDefault || rolle.Felder.Length > Fristen.FelderMax) return "mehr als 16 Felder";
        Span<bool> belegt = stackalloc bool[16];
        foreach (var f in rolle.Felder)
        {
            if (f.Feld > 15 || belegt[f.Feld]) return $"Feld {f.Feld} ungültig oder doppelt";
            belegt[f.Feld] = true;
            var mitNetz = f.NetzId != 0;
            var mitKennung = f.Schiffsnetz is not null;
            if (mitNetz == mitKennung) return $"Feld {f.Feld}: genau eines von netzId und schiffsnetz";
            var kanalnetz = mitNetz && Kanaele.IstKanalnetz(netze, f.NetzId);
            if (mitNetz && !kanalnetz && Kanaele.Einordnen(netze, f.NetzId) != Netzart.Gewoehnlich) return $"Feld {f.Feld}: Kanal {f.NetzId} ungültig";
            if (mitKennung && !KennungGueltig(f.Schiffsnetz)) return $"Feld {f.Feld}: Kennung ungültig";
            if (f.Ch is { } ch && (!kanalnetz || !Kanaele.ChGueltig(netze[f.NetzId], ch))) return $"Feld {f.Feld}: Ch. {ch} ungültig";
        }
        return null;
    }

    public static string? SchiffPruefen(Schiff schiff, IReadOnlyDictionary<ushort, Netz> netze, IReadOnlyDictionary<ushort, Rolle> rollen)
    {
        ArgumentNullException.ThrowIfNull(schiff);
        ArgumentNullException.ThrowIfNull(netze);
        ArgumentNullException.ThrowIfNull(rollen);
        if (schiff.Id is 0 or 0xFFFF) return "Schiffs-ID außerhalb 1-65534";
        if (!Enum.IsDefined(schiff.Art)) return $"Art {(byte)schiff.Art} unbekannt";
        if (schiff.Typ is { } typ && (string.IsNullOrWhiteSpace(typ) || Namen.Skalarwerte(typ) > Fristen.EinheitTypMax)) return "Typ leer oder über 32 Zeichen";
        if (schiff.Netze.Count > Fristen.KennungenJeSchiffMax) return "mehr als 8 Kennungen";
        foreach (var (kennung, netz) in schiff.Netze)
        {
            if (!KennungGueltig(kennung)) return $"Kennung {kennung} ungültig";
            // Eine Kennung nennt ein gewöhnliches Netz oder einen Kanal (die Einheit gibt so den Ch. vor, 6.16), nie Global, ein Kanalnetz
            // selbst oder eine Ruhe.
            if (Kanaele.Einordnen(netze, netz) is not (Netzart.Gewoehnlich or Netzart.Kanal)) return $"Kennung {kennung}: Kanal {netz} ungültig";
        }
        if (schiff.Plaetze.IsDefault || schiff.Plaetze.Length > Fristen.PlaetzeJeSchiffMax) return "mehr als 16 Plätze";
        var gesehen = new HashSet<ushort>();
        foreach (var p in schiff.Plaetze)
        {
            if (!rollen.ContainsKey(p.RolleId)) return $"Platz mit unbekannter Rolle {p.RolleId}";
            if (!gesehen.Add(p.RolleId)) return $"Rolle {p.RolleId} doppelt";
            if (p.Mindest > p.Kapazitaet) return $"Rolle {p.RolleId}: Mindestbesetzung über der Kapazität";
        }
        return null;
    }

    public static ImmutableArray<Mitgliedschaft> Sortiert(IEnumerable<Mitgliedschaft> felder) =>
        [.. felder.OrderBy(f => f.Feld)];

    /// <summary>
    /// STANDARD (6.9): Netze mit <c>standard</c> in der Folge von <c>reihenfolge</c> ab Feld 0, hören, senden bei
    /// HOEREN_SENDEN, nicht fest; Sendeauswahl ist das erste Feld mit senden. Ein Kanalnetz liegt dort auf Ruhe (6.16) und ist nie der
    /// Sendevorschlag.
    /// </summary>
    public static (ImmutableArray<Mitgliedschaft> Felder, Sendeauswahl Auswahl) Standard(IEnumerable<Netz> netze)
    {
        var felder = ImmutableArray.CreateBuilder<Mitgliedschaft>();
        byte? vorschlag = null;
        foreach (var n in netze.Where(n => n.Typ == NetzTyp.Normal && n.Standard).OrderBy(n => n.Reihenfolge).ThenBy(n => n.Id))
        {
            if (felder.Count == Fristen.FelderMax) break;
            var feld = (byte)felder.Count;
            var senden = n.Offen == Offenheit.HoerenSenden;
            felder.Add(new Mitgliedschaft(feld, n.Kanaele.IsEmpty ? n.Id : Kanalrechnung.Id(n.Id, Kanalrechnung.Ruhe), true, senden, false));
            if (senden && n.Kanaele.IsEmpty) vorschlag ??= feld;
        }
        return (felder.ToImmutable(), vorschlag is { } v ? new Sendeauswahl([v], false) : Sendeauswahl.Leer);
    }

    /// <summary>
    /// Felder aus einer Rolle (6.9): Schiffsnetze über <c>schiff.netze[kennung]</c>, fehlt die Kennung, bleibt das Feld
    /// leer. Doppelte Netze werden zusammengefasst: ein festes Feld gewinnt, sonst das niedrigere. Hat die laufende
    /// <paramref name="phase"/> eine Variante für die Rolle, gelten deren Felder statt der Grundstellung (6.14); jeder Weg zum Platz
    /// gibt die laufende Phase mit. Ein Rollenfeld mit einem Kanalnetz bekommt seinen Ch. nach <see cref="ChAufloesen"/> (6.16), mit den
    /// bisherigen Feldern des Kontos (<paramref name="bisher"/>) und den Wünschen je Rollenfeld (<paramref name="wuensche"/>, RolleWaehlen).
    /// Eine Ruhe ist nie Sendevorschlag.
    /// </summary>
    public static IReadOnlyList<Rollenfeld> AusRolle(Rolle rolle, Schiff? schiff, IReadOnlyDictionary<ushort, Netz> netze, Einsatzphase? phase = null,
        ImmutableArray<Mitgliedschaft> bisher = default, IReadOnlyDictionary<byte, byte>? wuensche = null)
    {
        ArgumentNullException.ThrowIfNull(rolle);
        ArgumentNullException.ThrowIfNull(netze);
        var liste = new List<Rollenfeld>();
        var felder = phase?.VarianteFuer(rolle.Id)?.Felder ?? rolle.Felder;
        // Kanäle, die ein Feld der Rolle fest nennt: Ein freies Feld im selben Kanalnetz nimmt keinen davon (seit 19.10.2026, Vorlage Star
        // Citizen: "Jäger (Staffel n)" neben "Jäger (alle)"), sonst fielen beide Felder in eins.
        var fest = new HashSet<ushort>();
        foreach (var rf in felder)
            if (rf.Ch is { } c && netze.TryGetValue(rf.NetzId, out var kn) && !kn.Kanaele.IsEmpty && Kanaele.ChGueltig(kn, c)) fest.Add(Kanalrechnung.Id(rf.NetzId, c));
        foreach (var rf in felder.OrderBy(f => f.Feld))
        {
            var netz = rf.NetzId;
            if (netz == 0)
            {
                if (schiff is null || rf.Schiffsnetz is null || !schiff.Netze.TryGetValue(rf.Schiffsnetz, out netz)) continue;
            }
            else if (netze.TryGetValue(netz, out var kanalnetz) && !kanalnetz.Kanaele.IsEmpty)
            {
                netz = ChAufloesen(rf, kanalnetz, schiff, bisher, wuensche, fest);
            }
            var neu = new Rollenfeld(new Mitgliedschaft(rf.Feld, netz, rf.Hoeren, rf.Senden, rf.Fest), rf.SendenStandard && rf.Senden && !Kanaele.IstRuhe(netze, netz));
            var i = liste.FindIndex(x => x.Mitgliedschaft.NetzId == netz);
            if (i < 0) liste.Add(neu);
            else if (!liste[i].Mitgliedschaft.Fest && neu.Mitgliedschaft.Fest) liste[i] = neu;
        }
        liste.Sort((a, b) => a.Mitgliedschaft.Feld.CompareTo(b.Mitgliedschaft.Feld));
        return liste;
    }

    /// <summary>
    /// Ch.-Auflösung eines Rollenfelds mit Kanalnetz (6.16), in dieser Folge: (1) der feste Ch. des Rollenfelds; (2) die Einheit des
    /// Platzes nennt eine Kanal-ID dieses Kanalnetzes (die erste nach Kennung); (3) der Ch., den das Konto in diesem Kanalnetz gerade hat,
    /// das Feld mit derselben Nummer zuerst; (4) der Wunsch für dieses Feld; (5) Ruhe. Ein Kanal aus <paramref name="vergeben"/> (den ein
    /// anderes Feld der Rolle fest nennt) kommt in (2) bis (4) nicht in Frage (seit 19.10.2026).
    /// </summary>
    public static ushort ChAufloesen(RollenFeld rf, Netz kanalnetz, Schiff? schiff, ImmutableArray<Mitgliedschaft> bisher, IReadOnlyDictionary<byte, byte>? wuensche,
        IReadOnlySet<ushort>? vergeben = null)
    {
        ArgumentNullException.ThrowIfNull(kanalnetz);
        var n = kanalnetz.Id;
        if (rf.Ch is { } fest && Kanaele.ChGueltig(kanalnetz, fest)) return Kanalrechnung.Id(n, fest);
        bool Frei(ushort id) => vergeben is null || !vergeben.Contains(id);
        if (schiff is not null)
        {
            foreach (var id in schiff.Netze.Values)
                if (Kanalrechnung.Zerlegen(id, out var sn, out var sc) && sn == n && sc != Kanalrechnung.Ruhe && Kanaele.ChGueltig(kanalnetz, sc) && Frei(id)) return id;
        }
        if (!bisher.IsDefaultOrEmpty)
        {
            ushort? erstes = null;
            foreach (var m in bisher)
            {
                if (!Kanalrechnung.Zerlegen(m.NetzId, out var mn, out var mc) || mn != n || !Kanaele.ChGueltig(kanalnetz, mc) || !Frei(m.NetzId)) continue;
                if (m.Feld == rf.Feld) return m.NetzId;
                erstes ??= m.NetzId;
            }
            if (erstes is { } aktuell) return aktuell;
        }
        if (wuensche is not null && wuensche.TryGetValue(rf.Feld, out var wunsch) && Kanaele.ChGueltig(kanalnetz, wunsch) && Frei(Kanalrechnung.Id(n, wunsch)))
            return Kanalrechnung.Id(n, wunsch);
        return Kanalrechnung.Id(n, Kanalrechnung.Ruhe);
    }

    /// <summary>
    /// Selbstwahl (6.9): feste Felder behalten ihre Nummer, ein Rollenfeld mit einem Netz aus einem festen Feld geht
    /// darin auf; kollidierende Rollenfelder rücken auf das nächste freie Feld, was über 15 hinausgeht, entfällt.
    /// </summary>
    public static (ImmutableArray<Mitgliedschaft> Felder, ImmutableArray<byte> Marken, ImmutableArray<EntfallenesFeld> Entfallen)
        MitFestenFeldern(ImmutableArray<Mitgliedschaft> bestehend, IReadOnlyList<Rollenfeld> rollenFelder) =>
        Einordnen(bestehend.Where(f => f.Fest), rollenFelder);

    /// <summary>
    /// Ordnet Rollenfelder um bleibende Felder herum ein: Die bleibenden behalten ihre Feldnummer, ein Rollenfeld mit einem Netz
    /// aus einem bleibenden Feld geht darin auf; kollidierende Rollenfelder rücken auf das nächste freie Feld, was über 15
    /// hinausgeht, entfällt. <see cref="MitFestenFeldern"/> (Selbstwahl, 6.9) und das Schalten einer Phase (6.14) nutzen es.
    /// </summary>
    private static (ImmutableArray<Mitgliedschaft> Felder, ImmutableArray<byte> Marken, ImmutableArray<EntfallenesFeld> Entfallen)
        Einordnen(IEnumerable<Mitgliedschaft> bleibend, IReadOnlyList<Rollenfeld> rollenFelder)
    {
        ArgumentNullException.ThrowIfNull(rollenFelder);
        Span<bool> belegt = stackalloc bool[16];
        var festeNetze = new HashSet<ushort>();
        var ergebnis = new List<Mitgliedschaft>();
        foreach (var f in bleibend)
        {
            belegt[f.Feld] = true;
            festeNetze.Add(f.NetzId);
            ergebnis.Add(f);
        }

        var marken = new List<byte>();
        var nachruecken = new List<Rollenfeld>();
        foreach (var rf in rollenFelder)
        {
            var m = rf.Mitgliedschaft;
            if (festeNetze.Contains(m.NetzId)) continue;
            if (belegt[m.Feld])
            {
                nachruecken.Add(rf);
                continue;
            }
            belegt[m.Feld] = true;
            ergebnis.Add(m);
            if (rf.Sendevorschlag) marken.Add(m.Feld);
        }

        var entfallen = ImmutableArray.CreateBuilder<EntfallenesFeld>();
        foreach (var rf in nachruecken)
        {
            var m = rf.Mitgliedschaft;
            var ziel = -1;
            for (var f = m.Feld + 1; f < 16; f++)
            {
                if (!belegt[f])
                {
                    ziel = f;
                    break;
                }
            }
            if (ziel < 0)
            {
                entfallen.Add(new EntfallenesFeld(m.Feld, m.NetzId, GrundKeinFreiesFeld));
                continue;
            }
            belegt[ziel] = true;
            ergebnis.Add(m with { Feld = (byte)ziel });
            if (rf.Sendevorschlag) marken.Add((byte)ziel);
        }
        return (Sortiert(ergebnis), [.. marken.Order()], entfallen.ToImmutable());
    }

    /// <summary>
    /// Felder nach einem Phasenwechsel (6.14 Regeln 5 und 6): die Rollenfelder der neuen Phase, dazu jedes bisherige Feld, dessen
    /// Netz nicht unter den Rollenfeldern der alten Phase steht (Zusatznetze der Leitung, eigene offene Netze). Diese bleibenden
    /// Felder behalten ihre Nummer; belegte Nummern rücken nach wie bei <see cref="MitFestenFeldern"/>. Ein nicht festes bleibendes
    /// Feld, dessen Netz die neue Phase als Rollenfeld bringt, geht im Rollenfeld auf, ein festes gewinnt. "Hören aus" bleibt für
    /// Netze, die vorher und nachher da sind, wenn das neue Rollenfeld nicht fest ist.
    /// </summary>
    public static (ImmutableArray<Mitgliedschaft> Felder, ImmutableArray<byte> Marken, ImmutableArray<EntfallenesFeld> Entfallen)
        PhaseUmstellen(ImmutableArray<Mitgliedschaft> bisher, IReadOnlyList<Rollenfeld> alt, IReadOnlyList<Rollenfeld> neu)
    {
        ArgumentNullException.ThrowIfNull(alt);
        ArgumentNullException.ThrowIfNull(neu);
        var altNetze = new HashSet<ushort>(alt.Select(x => x.Mitgliedschaft.NetzId));
        var neuNetze = new HashSet<ushort>(neu.Select(x => x.Mitgliedschaft.NetzId));
        var hoerenAus = new HashSet<ushort>(bisher.Where(f => !f.Hoeren).Select(f => f.NetzId));
        var bleibend = bisher.Where(f => !altNetze.Contains(f.NetzId) && (f.Fest || !neuNetze.Contains(f.NetzId)));
        var angepasst = neu
            .Select(x => !x.Mitgliedschaft.Fest && hoerenAus.Contains(x.Mitgliedschaft.NetzId) ? x with { Mitgliedschaft = x.Mitgliedschaft with { Hoeren = false } } : x)
            .ToList();
        return Einordnen(bleibend, angepasst);
    }

    /// <summary>
    /// Sendeauswahl nach einem Phasenwechsel (6.14 Regel 7): Wer auf dem Sendevorschlag der alten Phase sendete, sendet danach auf
    /// dem der neuen (<paramref name="marken"/>). Wer selbst etwas anderes gewählt hat (<paramref name="tatsaechlich"/>, aus dem
    /// Komfortzustand), behält es, soweit er es weiter darf; Netze werden über ihre neue Feldnummer gefunden. Bleibt davon nichts,
    /// gilt der neue Vorschlag.
    /// </summary>
    public static Sendeauswahl PhaseAuswahl(IReadOnlyDictionary<ushort, Netz> netze, Sendeauswahl tatsaechlich, ImmutableArray<Mitgliedschaft> bisher,
        IReadOnlyList<Rollenfeld> alt, ImmutableArray<Mitgliedschaft> felder, IEnumerable<byte> marken, Recht rechte)
    {
        ArgumentNullException.ThrowIfNull(tatsaechlich);
        ArgumentNullException.ThrowIfNull(alt);
        var vorschlag = AuswahlAusMarken(netze, marken, felder, false, rechte);
        var gewaehlt = new HashSet<ushort>();
        foreach (var f in tatsaechlich.Felder)
            foreach (var m in bisher)
                if (m.Feld == f) gewaehlt.Add(m.NetzId);
        // Der Vorschlag der alten Phase, wie ihn der Server gesetzt hätte: ohne MEHRFACH_SENDEN nur das niedrigste markierte Feld.
        var altVorschlag = alt.Where(x => x.Sendevorschlag).Select(x => x.Mitgliedschaft.NetzId).ToList();
        if (!rechte.Hat(Recht.MehrfachSenden) && altVorschlag.Count > 1) altVorschlag = [altVorschlag[0]];
        if (!tatsaechlich.Global && gewaehlt.SetEquals(altVorschlag)) return vorschlag;
        var neueFelder = new List<byte>();
        foreach (var netz in gewaehlt)
            foreach (var m in felder)
                if (m.NetzId == netz) neueFelder.Add(m.Feld);
        var behalten = AuswahlAusMarken(netze, neueFelder, felder, tatsaechlich.Global, rechte);
        return behalten.Felder.IsEmpty && !behalten.Global ? vorschlag : behalten;
    }

    /// <summary>
    /// Eine Einsatzphase (6.14): Nummer 1 bis 5, Name (24) und Kurzname (12) nicht leer, die Grundstellung ohne Varianten, je Rolle
    /// höchstens eine Variante, Rolle vorhanden, Felder wie Rollenfelder (6.9). Die Grenzen über alle Phasen prüft der Schreiber.
    /// </summary>
    public static string? PhasePruefen(Einsatzphase phase, IReadOnlyDictionary<ushort, Netz> netze, IReadOnlyDictionary<ushort, Rolle> rollen)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(netze);
        ArgumentNullException.ThrowIfNull(rollen);
        if (phase.Nr is 0 or > Fristen.PhaseNrMax) return $"Phase {phase.Nr} außerhalb 1-5";
        if (string.IsNullOrWhiteSpace(phase.Name) || Namen.Skalarwerte(phase.Name) > Fristen.PhaseNameMax) return "Name leer oder über 24 Zeichen";
        if (string.IsNullOrWhiteSpace(phase.Kurz) || Namen.Skalarwerte(phase.Kurz) > Fristen.PhaseKurzMax) return "Kurzname leer oder über 12 Zeichen";
        if (phase.Nr == 1 && !phase.Varianten.IsEmpty) return "die Grundstellung trägt keine Varianten";
        var gesehen = new HashSet<ushort>();
        foreach (var v in phase.Varianten)
        {
            if (!rollen.TryGetValue(v.RolleId, out var rolle)) return $"Variante für unbekannte Rolle {v.RolleId}";
            if (!gesehen.Add(v.RolleId)) return $"Rolle {v.RolleId} doppelt";
            // Auf dem Draht stehen die Felder flach mit ihrer Rolle; eine Variante ohne Feld ließe sich dort nicht ausdrücken.
            if (v.Felder.IsEmpty) return $"Variante {v.RolleId} ohne Feld";
            if (RollePruefen(rolle with { Felder = v.Felder }, netze) is { } problem) return $"Variante {v.RolleId}: {problem}";
        }
        return null;
    }

    /// <summary>
    /// Sendeauswahl aus markierten Feldern (6.9): nur Felder mit senden, nie eine Ruhe (6.16); ohne MEHRFACH_SENDEN gilt nur das
    /// niedrigste markierte Feld; Global nur mit GLOBAL_SENDEN und ohne MEHRFACH_SENDEN nur ohne Feld.
    /// </summary>
    public static Sendeauswahl AuswahlAusMarken(IReadOnlyDictionary<ushort, Netz> netze, IEnumerable<byte> marken, ImmutableArray<Mitgliedschaft> felder, bool global, Recht rechte)
    {
        ArgumentNullException.ThrowIfNull(marken);
        var gueltig = new SortedSet<byte>();
        foreach (var m in marken)
        {
            foreach (var f in felder)
            {
                if (f.Feld == m && f.Senden && !Kanaele.IstRuhe(netze, f.NetzId))
                {
                    gueltig.Add(m);
                    break;
                }
            }
        }
        var mitGlobal = global && rechte.Hat(Recht.GlobalSenden);
        if (!rechte.Hat(Recht.MehrfachSenden))
        {
            if (gueltig.Count > 0) return new Sendeauswahl([gueltig.Min], false);
            return new Sendeauswahl([], mitGlobal);
        }
        return new Sendeauswahl([.. gueltig], mitGlobal);
    }

    /// <summary>Hält eine bestehende Sendeauswahl nach einer Umstellung gültig.</summary>
    public static Sendeauswahl AuswahlBereinigen(IReadOnlyDictionary<ushort, Netz> netze, Sendeauswahl alt, ImmutableArray<Mitgliedschaft> felder, Recht rechte)
    {
        ArgumentNullException.ThrowIfNull(alt);
        var neu = AuswahlAusMarken(netze, alt.Felder, felder, alt.Global, rechte);
        return neu.Equals(alt) ? alt : neu;
    }

    /// <summary>
    /// Prüfung von SendeauswahlSetzen (4.5): Felder aufsteigend ohne Doppelte, jedes mit senden und keine Ruhe (6.16); Global nur mit
    /// GLOBAL_SENDEN; mehr als ein Ziel nur mit MEHRFACH_SENDEN. <paramref name="rechte"/> sind die Rechte zum Zeitpunkt der
    /// Anfrage, mit einem laufenden Auftrag (6.12).
    /// </summary>
    public static Fehlercode AuswahlPruefen(IReadOnlyDictionary<ushort, Netz> netze, IReadOnlyList<byte> felder, bool global, Konto konto, Recht rechte)
    {
        ArgumentNullException.ThrowIfNull(felder);
        ArgumentNullException.ThrowIfNull(konto);
        if (felder.Count > Fristen.FelderMax) return Fehlercode.Ungueltig;
        var vorher = -1;
        foreach (var f in felder)
        {
            if (f <= vorher || f > 15) return Fehlercode.Ungueltig;
            vorher = f;
            if (konto.FeldNummer(f) is not { Senden: true } m || Kanaele.IstRuhe(netze, m.NetzId)) return Fehlercode.Ungueltig;
        }
        if (global && !rechte.Hat(Recht.GlobalSenden)) return Fehlercode.KeinRecht;
        if (felder.Count + (global ? 1 : 0) > 1 && !rechte.Hat(Recht.MehrfachSenden)) return Fehlercode.KeinRecht;
        return Fehlercode.Keiner;
    }
}
