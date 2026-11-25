// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>
/// Einsatzphasen und Funker-Modus (Fassung 0.5.0, RAID-S2-ENTWURF Abschnitte 1 und 3, FUNKPROTOKOLL 6.14 und 6.15). Eine Phase ist
/// ein Netzbild der Flotte: Sie ändert nur Felder (und unter Umständen das Sendenetz) der Konten, deren Rolle in der alten oder der
/// neuen Phase eine Variante hat, nie Platz, Rolle, Reservierung oder Belegung. Der Funker-Modus nimmt einem ADMIN bis zum nächsten
/// 04:00 die Rechte der Einsatzleitung und macht ihn für sie einteilbar.
/// </summary>
public sealed partial class Zustandsschreiber
{
    /// <summary>
    /// PhaseSchalten (6.14 Regeln 1 bis 3, 10, 11): <c>ZUWEISEN</c>; <paramref name="erwartet"/> muss die laufende Phase sein (sonst
    /// VERALTET mit dem Stand, damit ein zweiter Leiter nicht noch einmal schaltet); die laufende Nummer noch einmal ist Ok ohne Umstellung.
    /// </summary>
    internal static Ergebnis PhaseSchaltenAus(Arbeitsstand w, uint anfragender, byte nr, byte erwartet)
    {
        if (w.Konto(anfragender) is not { } anf) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!w.Wirksam(anf).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (nr is 0 or > Fristen.PhaseNrMax || erwartet is 0 or > Fristen.PhaseNrMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var laufend = w.Neben.LaufendeNr();
        if (erwartet != laufend) return new Ergebnis { Fehler = Fehlercode.Veraltet, PhaseStand = w.Neben.Phase ?? new LaufendePhase(1, 0, 0) };
        if (nr != 1 && !w.Phasen.ContainsKey(nr)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (nr == laufend) return new Ergebnis();
        var wechsel = Schalten(w, laufend, nr, anf.Stufe, Urheber(anfragender));
        return new Ergebnis { Phasenwechsel = wechsel, BetroffeneKonten = wechsel.Umgestellt, Anzahl = wechsel.Umgestellt.Length };
    }

    /// <summary>Schaltet von <paramref name="von"/> auf <paramref name="nach"/> und merkt die laufende Phase im Lagezustand.</summary>
    private static Phasenwechsel Schalten(Arbeitsstand w, byte von, byte nach, Stufe anfragenderStufe, uint durch, Einsatzphase? vonDefinition = null)
    {
        var (umgestellt, ausgelassen) = Umstellen(w, vonDefinition ?? w.PhaseDefinition(von), w.PhaseDefinition(nach), anfragenderStufe, durch);
        w.Neben = w.Neben with { Phase = new LaufendePhase(nach, w.Jetzt, durch) };
        return new Phasenwechsel(nach, von, durch, w.Jetzt) { Umgestellt = umgestellt, Ausgelassen = ausgelassen };
    }

    /// <summary>
    /// Stellt die Konten von einem Netzbild auf ein anderes um (6.14 Regeln 4 bis 9, 13): jedes Konto mit Platz (BELEGT oder
    /// RESERVIERT, auch ohne Sitzung) oder mit gewählter Rolle (6.16), dessen Rolle (die gewählte, sonst die des Platzes) in
    /// <paramref name="von"/> oder <paramref name="nach"/> eine Variante hat und das der
    /// Schaltende nach der Rangregel einteilen darf (sonst ausgelassen). Unveränderte Konten fasst er nicht an (K4): keine neue
    /// <c>kontoVersion</c>, kein Push. Platz, Quelle und Reservierung bleiben; verlorene Senderechte vermerkt <c>KontoSetzen</c>.
    /// </summary>
    private static (ImmutableArray<uint> Umgestellt, ImmutableArray<uint> Ausgelassen) Umstellen(Arbeitsstand w, Einsatzphase? von, Einsatzphase? nach,
        Stufe anfragenderStufe, uint durch)
    {
        var rollen = new HashSet<ushort>();
        if (von is not null) foreach (var v in von.Varianten) rollen.Add(v.RolleId);
        if (nach is not null) foreach (var v in nach.Varianten) rollen.Add(v.RolleId);
        if (rollen.Count == 0) return ([], []);
        var umgestellt = ImmutableArray.CreateBuilder<uint>();
        var ausgelassen = ImmutableArray.CreateBuilder<uint>();
        foreach (var k in w.Konten.Values.ToList())
        {
            // Die Phase richtet sich nach der gewählten Rolle, sonst nach der Rolle eines Platzes, der BELEGT oder RESERVIERT ist (6.16).
            var rolleId = k.GewaehlteRolle ?? (k.Platz is { } p && w.Platzzustand(k) != PlatzZustand.Frei ? p.RolleId : null);
            if (rolleId is not { } rid || !rollen.Contains(rid) || !w.Rollen.TryGetValue(rid, out var rolle)) continue;
            if (!Rechteregeln.DarfZuweisen(anfragenderStufe, k, w.Jetzt))
            {
                ausgelassen.Add(k.Id);
                continue;
            }
            var schiff = k.Platz is { SchiffId: not 0 } ps ? w.Schiffe.GetValueOrDefault(ps.SchiffId) : null;
            var alt = Feldregeln.AusRolle(rolle, schiff, w.Netze, von, k.Felder);
            var neu = Feldregeln.AusRolle(rolle, schiff, w.Netze, nach, k.Felder);
            var (felder, marken, entfallen) = Feldregeln.PhaseUmstellen(k.Felder, alt, neu);
            var tatsaechlich = w.AuswahlTatsaechlich(k);
            var auswahl = Feldregeln.PhaseAuswahl(w.Netze, tatsaechlich, k.Felder, alt, felder, marken, w.Wirksam(k));
            if (felder.AsSpan().SequenceEqual(k.Felder.AsSpan()) && auswahl.Equals(tatsaechlich) && entfallen.IsEmpty) continue;
            w.KontoSetzen(k with { Felder = felder, Sendeauswahl = auswahl, Entfallen = entfallen }, umstellungDurch: durch);
            // Die neue Sendeauswahl gilt, auch wenn sie am Konto gleich blieb: Der Komfortzustand nennt Feldnummern von vorher.
            w.AuswahlUebernehmen(k.Id);
            umgestellt.Add(k.Id);
        }
        return (umgestellt.ToImmutable(), ausgelassen.ToImmutable());
    }

    /// <summary>
    /// Verwaltung PHASE_SETZEN (6.14): <c>NETZE_VERWALTEN</c>, Prüfung wie die Rollenfelder, höchstens 4 Phasen über der Grundstellung
    /// und 512 Variantenfelder. Ändert sie die laufende Phase, stellt der Server sofort um, wie ein Wechsel von der alten auf die neue
    /// Fassung; sonst gelten die neuen Felder beim nächsten Schalten.
    /// </summary>
    internal static Ergebnis PhaseSetzenAus(Arbeitsstand w, uint anfragender, Einsatzphase phase, long version)
    {
        ArgumentNullException.ThrowIfNull(phase);
        var code = VerwaltungPruefen(w, anfragender, version);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        if (Feldregeln.PhasePruefen(phase, w.Netze, w.Rollen) is { } problem) return Ergebnis.Mit(Fehlercode.Ungueltig, problem);
        var phasen = w.Phasen.SetItem(phase.Nr, phase);
        if (phasen.Keys.Count(n => n > 1) > Fristen.PhasenMax) return Ergebnis.Mit(Fehlercode.Ungueltig, "Höchstens 4 Phasen über der Grundstellung.");
        if (phasen.Values.Sum(p => p.FelderAnzahl()) > Fristen.PhasenFelderMax) return Ergebnis.Mit(Fehlercode.Ungueltig, "Höchstens 512 Variantenfelder über alle Phasen.");
        var vorher = w.Phasen.GetValueOrDefault(phase.Nr);
        w.Phasen = phasen;
        var betroffen = ImmutableArray<uint>.Empty;
        if (phase.Nr > 1 && w.Neben.LaufendeNr() == phase.Nr)
            (betroffen, _) = Umstellen(w, vorher, phase, w.Konto(anfragender)?.Stufe ?? Stufe.Admin, Urheber(anfragender));
        return NetzListeNeu(w, betroffen);
    }

    /// <summary>
    /// Verwaltung PHASE_LOESCHEN (6.14): Die Grundstellung bleibt (<c>UNGUELTIG</c>). Läuft die Phase gerade, fällt die Flotte auf die
    /// Grundstellung zurück, mit den Folgen eines Wechsels.
    /// </summary>
    internal static Ergebnis PhaseLoeschenAus(Arbeitsstand w, uint anfragender, byte nr, long version)
    {
        var code = VerwaltungPruefen(w, anfragender, version);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        if (nr is <= 1 or > Fristen.PhaseNrMax) return Ergebnis.Mit(Fehlercode.Ungueltig, "Die Grundstellung bleibt.");
        if (!w.Phasen.TryGetValue(nr, out var definition)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        Phasenwechsel? wechsel = null;
        if (w.Neben.LaufendeNr() == nr) wechsel = Schalten(w, nr, 1, w.Konto(anfragender)?.Stufe ?? Stufe.Admin, Urheber(anfragender), definition);
        w.Phasen = w.Phasen.Remove(nr);
        var e = NetzListeNeu(w, wechsel?.Umgestellt ?? []);
        return wechsel is null ? e : new Ergebnis { NetzListeGeaendert = true, BetroffeneKonten = e.BetroffeneKonten, Phasenwechsel = wechsel };
    }

    /// <summary>Rückfall auf die Grundstellung 12 h nach dem letzten Schalten (6.14 Regel 12), aus dem Fristen-Takt.</summary>
    private static void PhaseRueckfall(Arbeitsstand w, Folgen folgen)
    {
        if (w.Neben.Phase is not { Nr: > 1 } p || w.Jetzt - p.Seit < Fristen.PhaseRueckfallMs) return;
        var wechsel = Schalten(w, p.Nr, 1, Stufe.Admin, 0);
        folgen.Phase = wechsel;
        folgen.Betroffen.UnionWith(wechsel.Umgestellt);
    }

    /// <summary>
    /// "Heute als Funker" (6.15): nur ein aktives ADMIN-Konto für sich selbst. Beim Einschalten ruhen die Rechte der Einsatzleitung
    /// bis <paramref name="bis"/> (höchstens 24 h); die Sendeauswahl verliert Global und Mehrfachziele, und Regel 7 aus 6.12 gilt: die
    /// eigene Sperre der Selbstwahl und das eigene Nachziehen enden. Ein zweites Einschalten verlängert nicht.
    /// </summary>
    internal static Ergebnis FunkermodusAus(Arbeitsstand w, uint kontoId, bool an, long bis)
    {
        if (w.Konto(kontoId) is not { } k) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (k.Stufe != Stufe.Admin || k.Status != KontoStatus.Aktiv) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var folgen = new Folgen();
        if (!an)
        {
            if (k.AlsFunkerBis == 0) return new Ergebnis();
            FunkermodusBeenden(w, k, null, folgen);
            return folgen.Ergebnis();
        }
        if (bis <= w.Jetzt || bis - w.Jetzt > Fristen.FunkerModusMaxMs) return Ergebnis.Mit(Fehlercode.Ungueltig, "Der Funker-Modus endet nach jetzt und höchstens 24 h später.");
        if (k.AlsFunkerZu(w.Jetzt)) return new Ergebnis();
        var neu = k with { AlsFunkerBis = bis };
        neu = neu with { Sendeauswahl = Feldregeln.AuswahlBereinigen(w.Netze, w.AuswahlTatsaechlich(k), neu.Felder, w.Wirksam(neu)) };
        w.KontoSetzen(neu);
        w.AuswahlUebernehmen(k.Id);
        folgen.Funker.Add(new FunkermodusFolge(k.Id, true, bis));
        folgen.Betroffen.Add(k.Id);
        ZuweisenVerloren(w, neu, folgen);
        return folgen.Ergebnis();
    }

    /// <summary>Beendet den Funker-Modus eines Kontos (selbst, Ablauf, Stufenwechsel, Sperre); die Rechte kommen sofort zurück.</summary>
    private static void FunkermodusBeenden(Arbeitsstand w, Konto k, string? grund, Folgen folgen)
    {
        w.KontoSetzen(k with { AlsFunkerBis = 0 });
        folgen.Funker.Add(new FunkermodusFolge(k.Id, false, 0, grund));
        folgen.Betroffen.Add(k.Id);
    }

    /// <summary>
    /// Regel 7 aus 6.12, für das Ende eines Auftrags und den Beginn des Funker-Modus: Hat das Konto kein <c>ZUWEISEN</c> mehr, enden
    /// seine Sperre der Selbstwahl, das Nachziehen der Aufstellung, die es zuletzt angewandt hat, und seit 0.6.0 seine Kanalsperren (6.17).
    /// </summary>
    private static void ZuweisenVerloren(Arbeitsstand w, Konto k, Folgen folgen)
    {
        if (w.Wirksam(k).Hat(Recht.Zuweisen)) return;
        if (!w.Einstellungen.Selbstwahl && w.Einstellungen.SelbstwahlDurch == k.Id)
        {
            SelbstwahlFreigeben(w);
            folgen.NetzListe = true;
        }
        if (w.Neben.Letzte is { } la && la.Anwender == k.Id) w.Neben = w.Neben with { Letzte = null };
        SperrenDesSetzenden(w, k.Id, folgen);
    }
}
