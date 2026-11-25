// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>Aufträge mit ZUWEISEN (6.7, 6.9, 6.10): Verschieben, Stummschalten, PlaetzeFreigeben.</summary>
public sealed partial class Zustandsschreiber
{
    /// <summary>
    /// Verschieben (4.5, 6.7): alles oder nichts, Ziele höherer Stufe ergeben KEIN_RECHT, <c>basisVersion</c> nur
    /// gegen die betroffenen Konten, Kapazität am Endzustand der ganzen Anfrage.
    /// </summary>
    internal static Ergebnis VerschiebenAus(Arbeitsstand w, uint anfragender, IReadOnlyList<Aenderung> aenderungen, long? basisVersion, bool erzwingen)
    {
        ArgumentNullException.ThrowIfNull(aenderungen);
        if (w.Konto(anfragender) is not { } anf) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!w.Wirksam(anf).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (aenderungen.Count is 0 or > Fristen.AenderungenMax) return Ergebnis.Mit(Fehlercode.Ungueltig);

        var fehler = new List<Eintragsfehler>();
        var ziele = new List<Konto>();
        var gesehen = new HashSet<uint>();
        for (var i = 0; i < aenderungen.Count; i++)
        {
            var a = aenderungen[i];
            if (w.Konto(a.KontoId) is not { } ziel)
            {
                fehler.Add(new Eintragsfehler(i, a.KontoId, null, Fehlercode.Unbekannt));
                continue;
            }
            if (!gesehen.Add(a.KontoId)) fehler.Add(new Eintragsfehler(i, a.KontoId, null, Fehlercode.Ungueltig));
            if (!Rechteregeln.DarfZuweisen(anf.Stufe, ziel, w.Jetzt)) return Ergebnis.Mit(Fehlercode.KeinRecht);
            ziele.Add(ziel);
        }
        if (fehler.Count > 0) return Ergebnis.Mit(fehler[0].Code, fehler);
        if (basisVersion is { } b && ziele.Where(z => z.KontoVersion > b).ToList() is { Count: > 0 } veraltet) return Veraltet(veraltet);

        var plaetze = new List<(ushort, ushort)>();
        for (var i = 0; i < aenderungen.Count; i++)
        {
            var (code, neu) = Anwenden(w, ziele[i], aenderungen[i], anfragender, Quelle.Verschieben);
            if (code != Fehlercode.Keiner)
            {
                fehler.Add(new Eintragsfehler(i, ziele[i].Id, null, code));
                continue;
            }
            w.KontoSetzen(neu, umstellungDurch: anfragender);
            if (neu.Platz is { } p) plaetze.Add((p.SchiffId, p.RolleId));
        }
        if (fehler.Count > 0) return Ergebnis.Mit(fehler[0].Code, fehler);

        if (!erzwingen && Ueberbelegt(w, plaetze) is { Count: > 0 } ueber)
        {
            for (var i = 0; i < aenderungen.Count; i++)
            {
                if (w.Konto(ziele[i].Id)?.Platz is { } p && ueber.Contains((p.SchiffId, p.RolleId)))
                    fehler.Add(new Eintragsfehler(i, ziele[i].Id, null, Fehlercode.PlatzBelegt));
            }
            return Ergebnis.Mit(Fehlercode.PlatzBelegt, fehler);
        }

        return new Ergebnis
        {
            Offline = [.. ziele.Where(z => !w.Online(z.Id)).Select(z => z.Id)],
            BetroffeneKonten = [.. ziele.Select(z => z.Id)],
        };
    }

    /// <summary>
    /// Eine Änderung auf ein Konto anwenden, ohne zu speichern. Reihenfolge nach 4.5: Rolle und Schiff, dann
    /// <c>felder</c>, dann <c>felderPatch</c>, dann <c>sendeFelder</c>. Setzt die Reservierung (6.9), außer bei "zurück auf
    /// Standard" durch Verschieben (seit 0.11.0).
    /// </summary>
    private static (Fehlercode Code, Konto Konto) Anwenden(Arbeitsstand w, Konto k, Aenderung a, uint durch, Quelle quelle)
    {
        var felder = k.Felder;
        var platz = k.Platz;
        var auswahl = k.Sendeauswahl;
        IEnumerable<byte>? marken = null;
        var rechte = w.Wirksam(k);
        var letzter = k.LetzterStand;

        if (a.RolleId is { } rolleId)
        {
            if (rolleId == 0)
            {
                // Zurück auf STANDARD: der bisherige Platz wird der letzte Stand, damit "wie zuletzt" und "wie letztes Mal"
                // beim nächsten Raid greifen (RAIDLEITUNG-ENTWURF, Nach dem Raid; A21).
                if (k.Platz is not null) letzter = new FrueherStand(k.Platz, k.Felder, k.Sendeauswahl);
                platz = null;
                (felder, auswahl) = Feldregeln.Standard(w.Netze.Values);
            }
            else
            {
                if (!w.Rollen.TryGetValue(rolleId, out var rolle)) return (Fehlercode.Unbekannt, k);
                var schiffId = a.SchiffId ?? 0;
                var (code, schiff) = PlatzPruefen(w, rolle, schiffId);
                if (code != Fehlercode.Keiner) return (code, k);
                var rf = Feldregeln.AusRolle(rolle, schiff, w.Netze, w.AktivePhase(), k.Felder);
                felder = Feldregeln.Sortiert(rf.Select(x => x.Mitgliedschaft));
                marken = rf.Where(x => x.Sendevorschlag).Select(x => x.Mitgliedschaft.Feld);
                platz = new Platz(schiffId, rolleId, w.Jetzt);
            }
        }
        else if (a.SchiffId is { } schiffId)
        {
            if (platz is not { } p || !w.Rollen.TryGetValue(p.RolleId, out var rolle)) return (Fehlercode.Ungueltig, k);
            var (code, schiff) = PlatzPruefen(w, rolle, schiffId);
            if (code != Fehlercode.Keiner) return (code, k);
            var rf = Feldregeln.AusRolle(rolle, schiff, w.Netze, w.AktivePhase(), k.Felder);
            felder = Feldregeln.Sortiert(rf.Select(x => x.Mitgliedschaft));
            marken = rf.Where(x => x.Sendevorschlag).Select(x => x.Mitgliedschaft.Feld);
            platz = new Platz(schiffId, p.RolleId, w.Jetzt);
        }

        if (a.Felder is { } vorgaben)
        {
            var code = Feldregeln.VorgabenPruefen(vorgaben, w.Netze);
            if (code != Fehlercode.Keiner) return (code, k);
            felder = Feldregeln.Sortiert(vorgaben.Select(v => new Mitgliedschaft(v.Feld, v.NetzId, v.Hoeren, v.Senden, true)));
        }

        var vorPatch = felder;
        if (a.FelderPatch is { } patches)
        {
            var (code, gepatcht) = Patchen(w, felder, patches);
            if (code != Fehlercode.Keiner) return (code, k);
            felder = gepatcht;
        }

        if (a.SendeFelder is { } sendeFelder) auswahl = Feldregeln.AuswahlAusMarken(w.Netze, sendeFelder, felder, false, rechte);
        else if (marken is not null) auswahl = Feldregeln.AuswahlAusMarken(w.Netze, marken, felder, false, rechte);
        else
        {
            auswahl = Feldregeln.AuswahlBereinigen(w.Netze, auswahl, felder, rechte);
            // Ch. je Person (seit 0.11.0) wie FeldSetzen Regel 7 (0.9.1): Geht ein Feld per felderPatch mit Feldnummer von der Ruhe auf
            // einen Ch. und sendet nichts, wird es Sendeziel. Sonst hörte die Person den Ch. der Leitung, sendete aber nirgendwohin.
            if (auswahl.Felder.IsEmpty && !auswahl.Global && a.FelderPatch is { } ps && VonRuheAufCh(w, k, vorPatch, felder, ps) is { } ziel)
                auswahl = Feldregeln.AuswahlAusMarken(w.Netze, [ziel], felder, false, rechte);
        }

        if (Feldregeln.FelderPruefen(felder, w.Netze) is not null) return (Fehlercode.Ungueltig, k);
        // "Zurück auf Standard" (rolleId 0 ohne Felder, auch über "Abend beenden") ist keine Einteilung: keine Reservierung, sonst käme
        // der Funker beim nächsten Login als VERSCHIEBEN zurück, und sein Client stellte die gemerkte Rolle nicht her (seit 0.11.0).
        var zurueck = quelle == Quelle.Verschieben && a.RolleId == 0 && a.Felder is null && a.FelderPatch is null;
        return (Fehlercode.Keiner, k with
        {
            Felder = felder,
            Platz = platz,
            Sendeauswahl = auswahl,
            Quelle = quelle,
            QuelleDurch = durch,
            Reservierung = zurueck ? null : new Reservierung(w.Jetzt, quelle, durch),
            Entfallen = [],
            LetzterStand = letzter,
            // Bilden Rolle, Schiff oder eine ganze Feldliste die Felder, entfällt die gewählte Rolle (6.16); ein felderPatch lässt sie stehen.
            GewaehlteRolle = a.RolleId is null && a.SchiffId is null && a.Felder is null ? k.GewaehlteRolle : null,
        });
    }

    private static (Fehlercode Code, ImmutableArray<Mitgliedschaft> Felder) Patchen(Arbeitsstand w, ImmutableArray<Mitgliedschaft> felder, IReadOnlyList<Feldpatch> patches)
    {
        var liste = felder.ToList();
        foreach (var p in patches)
        {
            if (p.Leeren)
            {
                if (p.Feld is not { } leer || leer > 15) return (Fehlercode.Ungueltig, felder);
                liste.RemoveAll(f => f.Feld == leer);
                continue;
            }
            if (p.NetzId == 0) return (Fehlercode.Ungueltig, felder);
            var netzCode = Feldregeln.FeldnetzPruefen(w.Netze, p.NetzId);
            if (netzCode != Fehlercode.Keiner) return (netzCode, felder);
            var hoeren = p.Hoeren ?? true;
            var senden = p.Senden ?? false;
            var fest = p.Fest ?? true;
            if (p.Feld is { } feld)
            {
                if (feld > 15) return (Fehlercode.Ungueltig, felder);
                // Ein Netz steht je Konto in höchstens einem Feld: es wandert in das angegebene Feld.
                liste.RemoveAll(f => f.NetzId == p.NetzId || f.Feld == feld);
                liste.Add(new Mitgliedschaft(feld, p.NetzId, hoeren, senden, fest));
                continue;
            }
            var i = liste.FindIndex(f => f.NetzId == p.NetzId);
            if (i >= 0)
            {
                liste[i] = new Mitgliedschaft(liste[i].Feld, p.NetzId, hoeren, senden, fest);
                continue;
            }
            var frei = Enumerable.Range(0, 16).FirstOrDefault(n => liste.TrueForAll(f => f.Feld != n), -1);
            if (frei < 0) return (Fehlercode.Ungueltig, felder);
            liste.Add(new Mitgliedschaft((byte)frei, p.NetzId, hoeren, senden, fest));
        }
        return (Fehlercode.Keiner, Feldregeln.Sortiert(liste));
    }

    /// <summary>
    /// Das erste Feld eines <c>felderPatch</c> mit Feldnummer, das von der Ruhe auf einen Ch. desselben Kanalnetzes geht, danach
    /// sendet und nicht gesperrt ist (Ch. je Person, seit 0.11.0); sonst <c>null</c>.
    /// </summary>
    private static byte? VonRuheAufCh(Arbeitsstand w, Konto k, ImmutableArray<Mitgliedschaft> vorher, ImmutableArray<Mitgliedschaft> nachher,
        IReadOnlyList<Feldpatch> patches)
    {
        foreach (var p in patches)
        {
            if (p.Leeren || p.Feld is not { } feld) continue;
            Mitgliedschaft? alt = null, neu = null;
            foreach (var m in vorher)
                if (m.Feld == feld) alt = m;
            foreach (var m in nachher)
                if (m.Feld == feld) neu = m;
            if (alt is not { } a || neu is not { Senden: true } n || !Kanaele.IstRuhe(w.Netze, a.NetzId) || Kanaele.IstRuhe(w.Netze, n.NetzId)) continue;
            if (Kanaele.Regelnetz(w.Netze, a.NetzId) != Kanaele.Regelnetz(w.Netze, n.NetzId) || w.Gesperrt(k, n.NetzId, out _)) continue;
            return feld;
        }
        return null;
    }

    /// <summary>
    /// Stummschalten (4.5, 6.10): ZUWEISEN, ein ADMIN-Ziel nur durch einen ADMIN, auch im Funker-Modus (<see cref="Rechteregeln.DarfStummschalten"/>).
    /// Aufheben (0 s) darf, wer sie gesetzt hat, ein Konto höherer Stufe als der Setzende oder ein ADMIN; der Betroffene nie bei fremder.
    /// </summary>
    internal static Ergebnis StummschaltenAus(Arbeitsstand w, uint anfragender, uint kontoId, ushort netzId, int sekunden)
    {
        if (w.Konto(anfragender) is not { } anf) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!w.Wirksam(anf).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (w.Konto(kontoId) is not { } ziel) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!Rechteregeln.DarfStummschalten(anf.Stufe, ziel.Stufe)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (sekunden is < 0 or > Fristen.StummMaxSekunden) return Ergebnis.Mit(Fehlercode.Ungueltig);
        // Ein Netz, ein Kanal oder ein ganzes Kanalnetz (alle Ch., 6.16); eine Ruhe hört und sendet ohnehin nicht.
        switch (netzId == 0 ? Netzart.Global : Kanaele.Einordnen(w.Netze, netzId))
        {
            case Netzart.Unbekannt or Netzart.GlobalSelbst:
                return Ergebnis.Mit(Fehlercode.Unbekannt);
            case Netzart.Ruhe:
                return Ergebnis.Mit(Fehlercode.Ungueltig);
        }

        var jetzt = w.Jetzt;
        Stummschaltung? bestehend = null;
        foreach (var s in ziel.Stumm)
            if (s.NetzId == netzId && s.Bis > jetzt) bestehend = s;

        if (bestehend is { } b && !DarfStummAufheben(w, anf, ziel, b)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (sekunden == 0 && bestehend is null) return Ergebnis.Mit(Fehlercode.Unbekannt);

        var stumm = ziel.Stumm.Where(s => s.NetzId != netzId).ToList();
        if (sekunden > 0) stumm.Add(new Stummschaltung(netzId, jetzt + sekunden * 1000L, anfragender));
        w.KontoSetzen(ziel with { Stumm = [.. stumm.OrderBy(s => s.NetzId)] });
        return Ok(kontoId);
    }

    private static bool DarfStummAufheben(Arbeitsstand w, Konto anf, Konto ziel, Stummschaltung s)
    {
        if (s.Durch == anf.Id) return true;
        if (ziel.Id == anf.Id) return false;
        if (anf.Stufe == Stufe.Admin) return true;
        // Ein gelöschter Setzender zählt wie ein ADMIN: dann hebt nur noch ein ADMIN auf.
        var setzerStufe = w.Konto(s.Durch)?.Stufe ?? Stufe.Admin;
        return anf.Stufe > setzerStufe;
    }

    /// <summary>
    /// PlaetzeFreigeben (4.5): alle Plätze von Konten ohne Sitzung, die von ADMIN-Konten nur für einen ADMIN oder im Funker-Modus
    /// (6.10, 6.15).
    /// </summary>
    internal static Ergebnis PlaetzeFreigebenAus(Arbeitsstand w, uint anfragender)
    {
        if (w.Konto(anfragender) is not { } anf) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!w.Wirksam(anf).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var betroffen = new List<uint>();
        foreach (var k in w.Konten.Values.ToList())
        {
            if (w.Online(k.Id) || w.Platzzustand(k) == PlatzZustand.Frei) continue;
            if (!Rechteregeln.DarfZuweisen(anf.Stufe, k, w.Jetzt)) continue;
            w.KontoSetzen(AufStandard(w, k, anfragender, letztenStandMerken: true), umstellungDurch: anfragender);
            betroffen.Add(k.Id);
        }
        return new Ergebnis { Anzahl = betroffen.Count, BetroffeneKonten = [.. betroffen] };
    }
}
