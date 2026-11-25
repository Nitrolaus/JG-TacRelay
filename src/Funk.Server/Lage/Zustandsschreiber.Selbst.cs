// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>
/// Selbständerungen (6.5, 6.9, 6.16): HoerenSetzen, FeldSetzen, RolleWaehlen, SatzAnwenden, EinsteigenRolleSchiff (Selbstwahl).
/// </summary>
public sealed partial class Zustandsschreiber
{
    internal static Ergebnis HoerenSetzenAus(Arbeitsstand w, uint kontoId, ushort netzId, bool hoeren)
    {
        if (w.Konto(kontoId) is not { } k) return Ergebnis.Mit(Fehlercode.Unbekannt);
        // Global hören alle aktiven Sitzungen; abschalten geht nicht (6.5).
        if (netzId == Netz.GlobalId) return hoeren ? new Ergebnis() : Ergebnis.Mit(Fehlercode.GlobalPflicht);
        if (k.FeldMitNetz(netzId) is not { } f) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (f.Hoeren == hoeren) return new Ergebnis();
        w.KontoSetzen(k with { Felder = k.Felder.Replace(f, f with { Hoeren = hoeren }) });
        return OkDetails(kontoId);
    }

    /// <summary>
    /// Erfolg einer Selbständerung, die am Praesenz-Eintrag nur Details ändert (Felder, Quelle, gewählte Rolle): Das Delta geht nur an die
    /// Einsatzleitung (<see cref="Ergebnis.NurDetails"/>).
    /// </summary>
    private static Ergebnis OkDetails(uint kontoId) => new() { BetroffeneKonten = [kontoId], NurDetails = true };

    /// <summary>
    /// FeldSetzen (4.5, 6.16, 6.17; seit 0.8.1, Q44/Q45): Den Kanal eines Felds tauscht man nicht selbst, das tun Admin
    /// und Einsatzleitung. Selbst geht nur zweierlei: der Ch.-Wechsel im selben Kanalnetz (auch von und zur Ruhe, auch im festen Feld;
    /// hören und senden bleiben, wie sie waren) und ein offenes Netz in ein freies Feld (so trägt sich der Echo-Funker ein; senden nur
    /// bei HOEREN_SENDEN). Ein anderes Netz in einem belegten Feld und das Leeren eines Felds lehnt der Server ab, im festen Feld mit
    /// FELD_FEST, sonst mit KEIN_RECHT. Netz oder Kanal (auch Ruhe) bekannt, nie Global, Ch. 1 von Global oder ein Kanalnetz selbst; ein Feld mit
    /// einem gesperrten Kanal und ein gesperrter Kanal nie (KEIN_RECHT mit der Sperre); derselbe Kanal nicht in einem zweiten Feld.
    /// </summary>
    internal static Ergebnis FeldSetzenAus(Arbeitsstand w, uint kontoId, byte feld, ushort? netzId)
    {
        if (w.Konto(kontoId) is not { } k) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (feld > 15) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var alt = k.FeldNummer(feld);
        var altKanalnetz = alt is { } a0 ? Kanaele.Regelnetz(w.Netze, a0.NetzId) : (ushort)0;
        bool ChWechsel(ushort id) => alt is not null && Kanaele.Regelnetz(w.Netze, id) is var r && r != id && r == altKanalnetz;
        // Ein festes Feld ändert man selbst nur im Ch.; FELD_FEST geht wie bis 0.8.0 allen anderen Prüfungen vor.
        if (alt is { Fest: true } && (netzId is not { } z || !ChWechsel(z))) return Ergebnis.Mit(Fehlercode.FeldFest);

        var felder = k.Felder.Where(f => f.Feld != feld).ToList();
        if (netzId is not { } id)
        {
            if (alt is null) return new Ergebnis();
            // Ein Feld mit einem gesperrten Kanal bleibt stehen (6.17): auch Leeren geht nicht.
            if (w.Gesperrt(k, alt.Value.NetzId, out var sperre)) return Ergebnis.Gesperrt(sperre);
            return Ergebnis.Mit(Fehlercode.KeinRecht);
        }
        var code = Feldregeln.FeldnetzPruefen(w.Netze, id);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        // Kanalsperre (6.17): kein Ch.-Wechsel im gesperrten Feld, nichts Gesperrtes hineinlegen.
        if (alt is { } gesperrtesFeld && w.Gesperrt(k, gesperrtesFeld.NetzId, out var feldSperre)) return Ergebnis.Gesperrt(feldSperre);
        if (w.Gesperrt(k, id, out var zielSperre)) return Ergebnis.Gesperrt(zielSperre);
        if (felder.Any(f => f.NetzId == id)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var regel = Kanaele.Regelnetz(w.Netze, id);
        if (alt is { } vorher)
        {
            // Ch.-Wechsel im selben Kanalnetz: immer erlaubt; ein anderes Netz im belegten Feld nie.
            if (!ChWechsel(id)) return Ergebnis.Mit(Fehlercode.KeinRecht);
            felder.Add(vorher with { NetzId = id });
        }
        else
        {
            // Freies Feld: nur ein offenes Netz, das der Admin für alle geöffnet hat.
            var netz = w.Netze[regel];
            if (netz.Offen == Offenheit.Nein) return Ergebnis.Mit(Fehlercode.KeinRecht);
            felder.Add(new Mitgliedschaft(feld, id, true, netz.Offen == Offenheit.HoerenSenden, false));
        }
        var neu = Feldregeln.Sortiert(felder);
        var auswahl = Feldregeln.AuswahlBereinigen(w.Netze, k.Sendeauswahl, neu, w.Wirksam(k));
        // 0.9.1 (Sarevoc 08.10.2026): Nach der Rollenwahl steht alles auf Ruhe und nichts ist Sendeziel. Geht ein Feld von der Ruhe auf einen Ch. und ist
        // noch nichts gewählt, wird es Sendeziel, sonst sendete der erste gewählte Ch. erst nach einem Klick auf das Feld.
        if (auswahl.Felder.IsEmpty && !auswahl.Global && alt is { } bisher && Kanaele.IstRuhe(w.Netze, bisher.NetzId) && !Kanaele.IstRuhe(w.Netze, id))
            auswahl = Feldregeln.AuswahlAusMarken(w.Netze, [feld], neu, false, w.Wirksam(k));
        w.KontoSetzen(k with
        {
            Felder = neu,
            Sendeauswahl = auswahl,
            Quelle = Quelle.Selbst,
            QuelleDurch = 0,
            Entfallen = [],
        });
        return OkDetails(kontoId);
    }

    /// <summary>
    /// RolleWaehlen (0x0039, 6.16): Die Felder der Rolle (bei laufender Phase mit Variante aus der Variante) kommen um die festen Felder
    /// herum wie bei der Selbstwahl, mit der Ch.-Auflösung (Wünsche gelten seit 0.8.1 nicht mehr, Q46); Platz, Reservierung und Brett bleiben, die Sperre der
    /// Selbstwahl gilt nicht. Wünsche, die nicht passen, und Felder ohne Platz stehen in <c>uebersprungen</c>.
    /// </summary>
    internal static Ergebnis RolleWaehlenAus(Arbeitsstand w, uint kontoId, ushort rolleId, IReadOnlyDictionary<byte, byte>? wuensche)
    {
        if (w.Konto(kontoId) is not { } k) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!w.Rollen.TryGetValue(rolleId, out var rolle)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!rolle.SelbstWaehlbar) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var phase = w.AktivePhase();
        var vorlage = phase?.VarianteFuer(rolleId)?.Felder ?? rolle.Felder;
        var uebersprungen = new List<UebersprungenerEintrag>();
        // Seit 0.8.1 (Q46) beginnt jeder Ch. nach der Rollenwahl auf Ruhe: Wünsche (von Clients bis 0.8.0) werden noch geprüft, aber
        // nicht mehr angewandt. Es gelten der feste Ch. des Rollenfelds, die Einheit des Platzes und der Ch., den das Konto schon hat.
        foreach (var (feld, ch) in wuensche ?? new Dictionary<byte, byte>())
        {
            var passt = false;
            foreach (var rf in vorlage)
            {
                if (rf.Feld == feld && rf.NetzId != 0 && w.Netze.TryGetValue(rf.NetzId, out var kanalnetz) && !kanalnetz.Kanaele.IsEmpty)
                    passt = Kanaele.ChGueltig(kanalnetz, ch);
            }
            if (!passt) uebersprungen.Add(new UebersprungenerEintrag(feld, kontoId, Fehlercode.Ungueltig));
        }
        var schiff = k.Platz is { SchiffId: not 0 } p ? w.Schiffe.GetValueOrDefault(p.SchiffId) : null;
        var rollenFelder = Feldregeln.AusRolle(rolle, schiff, w.Netze, phase, k.Felder);
        var (felder, marken, entfallen) = Feldregeln.MitFestenFeldern(k.Felder, rollenFelder);
        uebersprungen.AddRange(entfallen.Select(e => new UebersprungenerEintrag(e.Feld, kontoId, Fehlercode.Ungueltig)));
        // Gesperrte Kanäle liegen trotzdem im Feld (rot, 6.17); ist der Sendevorschlag gesperrt, sendet man im ersten sendefähigen Feld.
        bool Gesperrt(Mitgliedschaft m) => w.Gesperrt(k, m.NetzId, out _);
        List<byte> vorschlag = [.. marken.Where(m => !felder.Any(f => f.Feld == m && Gesperrt(f)))];
        if (vorschlag.Count == 0 && !marken.IsEmpty)
            vorschlag = [.. felder.Where(f => f.Senden && !Kanaele.IstRuhe(w.Netze, f.NetzId) && !Gesperrt(f)).Select(f => f.Feld).Take(1)];
        w.KontoSetzen(k with
        {
            Felder = felder,
            Sendeauswahl = Feldregeln.AuswahlAusMarken(w.Netze, vorschlag, felder, false, w.Wirksam(k)),
            Quelle = Quelle.Selbst,
            QuelleDurch = 0,
            Entfallen = entfallen,
            GewaehlteRolle = rolleId,
        });
        // Der Platz bleibt: Am Praesenz-Eintrag ändern sich nur Details (Felder, gewählte Rolle).
        return new Ergebnis { Uebersprungen = [.. uebersprungen.OrderBy(u => u.Eintrag)], BetroffeneKonten = [kontoId], NurDetails = true };
    }

    /// <summary>
    /// SatzAnwenden (4.5, "Wie letztes Mal"): atomar. Seit 0.8.1 (Q44 bis Q46) bringt ein Satz keine Kanäle mehr in die Felder: Mit einer
    /// Rolle kommen die Felder aus der Rolle wie bei der Selbstwahl (bei laufender Phase mit Variante aus der Variante, 6.14, dann auch die
    /// Sendeauswahl aus ihrem Sendevorschlag), ohne Rolle bleiben die Felder, wie sie sind. Aus dem Satz gelten nur "hören" für Netze, die
    /// schon in einem Feld liegen (außer in festen Feldern), und offene Netze als Zusatznetze in freien Feldern; ein Ch. aus dem Satz gilt
    /// nicht, er löst sich wie bei der Selbstwahl auf. Mit <c>rolleId</c> 0 gibt man den Platz auf: Dann bleiben die festen Felder und
    /// offene Netze aus dem Satz. Was nicht passt, steht in <c>uebersprungen</c>. Mit einer Rolle entfällt die gewählte Rolle (6.16).
    /// </summary>
    internal static Ergebnis SatzAnwendenAus(Arbeitsstand w, uint kontoId, ushort? rolleId, ushort? schiffId,
        IReadOnlyList<Feldvorgabe> vorgaben, Sendeauswahl? sendeauswahl)
    {
        ArgumentNullException.ThrowIfNull(vorgaben);
        if (w.Konto(kontoId) is not { } k) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (vorgaben.Count > Fristen.FelderMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (schiffId is not null && rolleId is not { } _) return Ergebnis.Mit(Fehlercode.Ungueltig);

        if (rolleId is { } rid and not 0)
        {
            var (code, rolle, schiff) = SelbstwahlPruefen(w, k, rid, schiffId ?? 0);
            if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
            var phase = w.AktivePhase() is { } p && p.VarianteFuer(rid) is not null ? p : null;
            var (gesetzt, marken, entfallen) = Feldregeln.MitFestenFeldern(k.Felder, Feldregeln.AusRolle(rolle!, schiff, w.Netze, phase, k.Felder));
            var (felder, uebersprungen) = SatzEinordnen(w, k, gesetzt, vorgaben, phase is null);
            // In einer Phase brächte der Satz die Sendeauswahl eines früheren Abends (6.14, Falle "Wie letztes Mal"): dann der Sendevorschlag.
            var auswahl = phase is null && sendeauswahl is { } s
                ? Feldregeln.AuswahlAusMarken(w.Netze, s.Felder, felder, s.Global, w.Wirksam(k))
                : Feldregeln.AuswahlAusMarken(w.Netze, marken, felder, false, w.Wirksam(k));
            w.KontoSetzen(k with
            {
                Felder = felder,
                Sendeauswahl = auswahl,
                Platz = new Platz(schiffId ?? 0, rid, w.Jetzt),
                Reservierung = null,
                Quelle = Quelle.Satz,
                QuelleDurch = 0,
                Entfallen = entfallen,
                GewaehlteRolle = null,
            });
            if (Ueberbelegt(w, [(schiffId ?? 0, rid)]).Count > 0) return Ergebnis.Mit(Fehlercode.PlatzBelegt);
            return new Ergebnis { Uebersprungen = [.. uebersprungen], BetroffeneKonten = [kontoId] };
        }

        if (rolleId is null)
        {
            // Ohne Rolle bleiben Felder, Platz und gewählte Rolle; der Satz bringt nur "hören" und offene Netze in freien Feldern.
            var (felder, uebersprungen) = SatzEinordnen(w, k, k.Felder, vorgaben, true);
            var auswahl = sendeauswahl is { } s
                ? Feldregeln.AuswahlAusMarken(w.Netze, s.Felder, felder, s.Global, w.Wirksam(k))
                : Feldregeln.AuswahlBereinigen(w.Netze, k.Sendeauswahl, felder, w.Wirksam(k));
            w.KontoSetzen(k with
            {
                Felder = felder,
                Sendeauswahl = auswahl,
                Quelle = Quelle.Satz,
                QuelleDurch = 0,
                Entfallen = [],
            });
            return new Ergebnis { Uebersprungen = [.. uebersprungen], BetroffeneKonten = [kontoId] };
        }

        // Den eigenen Platz aufgeben ist bei gesperrter Selbstwahl ein Wechsel (F9). Sonst stiege man danach als Nachzügler
        // ohne Platz woanders ein, und "Wechsel sperren" wäre in zwei Schritten umgangen (Gegenprüfung 01.10.2026).
        if (k.Platz is not null && !w.Wirksam(k).Hat(Recht.Zuweisen) && SelbstwahlSperreWirkt(w)) return Ergebnis.Mit(Fehlercode.SelbstwahlGesperrt);
        var bleibend = k.Felder.Where(f => f.Fest).ToList();
        var ohnePlatz = new List<UebersprungenerEintrag>();
        foreach (var v in vorgaben)
        {
            var code = SatzFeldPruefen(w, k, v, bleibend);
            if (code != Fehlercode.Keiner)
            {
                ohnePlatz.Add(new UebersprungenerEintrag(v.Feld, kontoId, code));
                continue;
            }
            bleibend.Add(new Mitgliedschaft(v.Feld, v.NetzId, v.Hoeren, v.Senden, false));
        }
        var neu = Feldregeln.Sortiert(bleibend);
        var ersteAuswahl = sendeauswahl is { } s0
            ? Feldregeln.AuswahlAusMarken(w.Netze, s0.Felder, neu, s0.Global, w.Wirksam(k))
            : Feldregeln.AuswahlAusMarken(w.Netze, neu.Where(f => f.Senden && !Kanaele.IstRuhe(w.Netze, f.NetzId)).Select(f => f.Feld).Take(1), neu, false, w.Wirksam(k));
        w.KontoSetzen(k with
        {
            Felder = neu,
            Sendeauswahl = ersteAuswahl,
            Platz = null,
            Reservierung = null,
            Quelle = Quelle.Satz,
            QuelleDurch = 0,
            Entfallen = [],
            GewaehlteRolle = null,
        });
        return new Ergebnis { Uebersprungen = [.. ohnePlatz], BetroffeneKonten = [kontoId] };
    }

    /// <summary>
    /// Ordnet einen Satz um vorgegebene Felder ein (SatzAnwenden, seit 0.8.1): Liegt das Netz schon in einem Feld, gilt nur "hören"
    /// (außer im festen Feld); sonst nur ein offenes Netz (senden nur bei HOEREN_SENDEN) auf seinem Feld oder dem nächsten freien. Mit
    /// <paramref name="fest"/> (außerhalb einer Phase, wie bis 0.8.0) meldet ein Feld, das ein festes Feld belegt, FELD_FEST.
    /// </summary>
    private static (ImmutableArray<Mitgliedschaft> Felder, List<UebersprungenerEintrag> Uebersprungen) SatzEinordnen(Arbeitsstand w, Konto k,
        IEnumerable<Mitgliedschaft> basis, IReadOnlyList<Feldvorgabe> vorgaben, bool fest)
    {
        var felder = basis.ToList();
        var uebersprungen = new List<UebersprungenerEintrag>();
        foreach (var v in vorgaben)
        {
            var i = felder.FindIndex(f => f.NetzId == v.NetzId);
            if (i >= 0)
            {
                if (!felder[i].Fest) felder[i] = felder[i] with { Hoeren = v.Hoeren };
                continue;
            }
            Fehlercode code;
            if (v.Feld > 15) code = Fehlercode.Ungueltig;
            else if (fest && felder.Exists(f => f.Feld == v.Feld && f.Fest)) code = Fehlercode.FeldFest;
            else if (Feldregeln.FeldnetzPruefen(w.Netze, v.NetzId) is var netzCode && netzCode != Fehlercode.Keiner) code = netzCode;
            else if (w.Gesperrt(k, v.NetzId, out _)) code = Fehlercode.KeinRecht;
            else if (w.Netze[Kanaele.Regelnetz(w.Netze, v.NetzId)] is var netz && (netz.Offen == Offenheit.Nein || (v.Senden && netz.Offen != Offenheit.HoerenSenden)))
                code = Fehlercode.KeinRecht;
            else
            {
                var feld = felder.TrueForAll(f => f.Feld != v.Feld) ? v.Feld
                    : Enumerable.Range(0, 16).Select(n => (byte)n).FirstOrDefault(n => felder.TrueForAll(f => f.Feld != n), (byte)0xFF);
                if (feld == 0xFF) code = Fehlercode.Ungueltig;
                else
                {
                    felder.Add(new Mitgliedschaft(feld, v.NetzId, v.Hoeren, v.Senden, false));
                    continue;
                }
            }
            uebersprungen.Add(new UebersprungenerEintrag(v.Feld, k.Id, code));
        }
        return (Feldregeln.Sortiert(felder), uebersprungen);
    }

    /// <summary>Ein Feld des Satzes beim Aufgeben des Platzes: nur offene Netze (senden nur bei HOEREN_SENDEN).</summary>
    private static Fehlercode SatzFeldPruefen(Arbeitsstand w, Konto k, Feldvorgabe v, List<Mitgliedschaft> schon)
    {
        if (v.Feld > 15 || v.NetzId == Netz.GlobalId) return Fehlercode.Ungueltig;
        foreach (var f in schon)
        {
            if (f.Feld == v.Feld) return f.Fest ? Fehlercode.FeldFest : Fehlercode.Ungueltig;
            if (f.NetzId == v.NetzId) return Fehlercode.Ungueltig;
        }
        var netzCode = Feldregeln.FeldnetzPruefen(w.Netze, v.NetzId);
        if (netzCode != Fehlercode.Keiner) return netzCode;
        // Einen gesperrten Kanal legt man nicht selbst in ein Feld (6.17), auch nicht über "Wie letztes Mal".
        if (w.Gesperrt(k, v.NetzId, out _)) return Fehlercode.KeinRecht;
        var netz = w.Netze[Kanaele.Regelnetz(w.Netze, v.NetzId)];
        var darfHoeren = netz.Offen != Offenheit.Nein;
        var darfSenden = netz.Offen == Offenheit.HoerenSenden;
        if (!darfHoeren || (v.Senden && !darfSenden)) return Fehlercode.KeinRecht;
        return Fehlercode.Keiner;
    }

    /// <summary>
    /// Selbstwahl-Prüfung (4.5 EinsteigenRolleSchiff, F9): <c>selbstwahl</c>, <c>selbstWaehlbar</c>, Platz wählbar. Konten mit
    /// ZUWEISEN dürfen sich auch bei gesperrter Selbstwahl selbst eintragen; Konten ohne Platz ebenso, solange die Sperre
    /// Nachzügler zulässt ("Wechsel sperren" statt "Ganz sperren"). Eine Sperre nach ihrer Frist oder ohne gültigen Urheber
    /// wirkt nicht mehr, auch bevor der Takt sie aufhebt.
    /// </summary>
    private static (Fehlercode Code, Rolle? Rolle, Schiff? Schiff) SelbstwahlPruefen(Arbeitsstand w, Konto k, ushort rolleId, ushort schiffId)
    {
        var zuweisen = w.Wirksam(k).Hat(Recht.Zuweisen);
        var nachzuegler = w.Einstellungen.Nachzuegler && k.Platz is null;
        if (!zuweisen && !nachzuegler && SelbstwahlSperreWirkt(w)) return (Fehlercode.SelbstwahlGesperrt, null, null);
        if (!w.Rollen.TryGetValue(rolleId, out var rolle)) return (Fehlercode.Unbekannt, null, null);
        if (!zuweisen && !rolle.SelbstWaehlbar) return (Fehlercode.KeinRecht, null, null);
        var (code, schiff) = PlatzPruefen(w, rolle, schiffId);
        return (code, rolle, schiff);
    }

    internal static Ergebnis EinsteigenAus(Arbeitsstand w, uint anfragender, uint? kontoId, ushort rolleId, ushort? schiffId,
        bool erzwingen, long? basisVersion)
    {
        if (kontoId is { } ziel && ziel != anfragender)
        {
            // Für ein anderes Konto gilt alles wie bei Verschieben mit rolleId (4.5).
            var aenderung = new Aenderung(ziel) { RolleId = rolleId, SchiffId = schiffId ?? 0 };
            return VerschiebenAus(w, anfragender, [aenderung], basisVersion, erzwingen);
        }

        if (w.Konto(anfragender) is not { } k) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (rolleId == 0) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (erzwingen && !w.Wirksam(k).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (basisVersion is { } b && k.KontoVersion > b) return Veraltet([k]);
        var (code, rolle, schiff) = SelbstwahlPruefen(w, k, rolleId, schiffId ?? 0);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);

        var (felder, marken, entfallen) = Feldregeln.MitFestenFeldern(k.Felder, Feldregeln.AusRolle(rolle!, schiff, w.Netze, w.AktivePhase(), k.Felder));
        w.KontoSetzen(k with
        {
            Felder = felder,
            Sendeauswahl = Feldregeln.AuswahlAusMarken(w.Netze, marken, felder, false, w.Wirksam(k)),
            Platz = new Platz(schiffId ?? 0, rolleId, w.Jetzt),
            Reservierung = null,
            Quelle = Quelle.Rolle,
            QuelleDurch = 0,
            Entfallen = entfallen,
            GewaehlteRolle = null,
        });

        if (!erzwingen && Ueberbelegt(w, [(schiffId ?? 0, rolleId)]).Count > 0)
            return Ergebnis.Mit(Fehlercode.PlatzBelegt, [new Eintragsfehler(0, anfragender, null, Fehlercode.PlatzBelegt)]);
        return Ok(anfragender);
    }
}
