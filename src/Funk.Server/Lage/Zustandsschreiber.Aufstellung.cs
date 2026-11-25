// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>Aufstellungen (6.7) und Nachziehen offener Einträge (6.9).</summary>
public sealed partial class Zustandsschreiber
{
    internal static Ergebnis AufstellungSpeichernAus(Arbeitsstand w, uint anfragender, uint? id, uint? version, string name,
        IReadOnlyList<AufstellungsEintrag> eintraege)
    {
        ArgumentNullException.ThrowIfNull(eintraege);
        if (!w.RechteVon(anfragender).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (string.IsNullOrWhiteSpace(name) || Namen.Skalarwerte(name) > Fristen.AufstellungNameMax || eintraege.Count > Fristen.AufstellungEintraegeMax)
            return Ergebnis.Mit(Fehlercode.Ungueltig);

        var fehler = new List<Eintragsfehler>();
        for (var i = 0; i < eintraege.Count; i++)
        {
            var e = eintraege[i];
            var code = (e.KontoId == 0) == string.IsNullOrEmpty(e.DiscordId) ? Fehlercode.Ungueltig : EintragPruefen(w, e);
            if (code != Fehlercode.Keiner) fehler.Add(new Eintragsfehler(i, e.KontoId, e.DiscordId, code));
        }
        if (fehler.Count > 0) return Ergebnis.Mit(fehler[0].Code, fehler);

        var neben = w.Neben;
        Aufstellung neu;
        if (id is { } vorhanden)
        {
            if (!neben.Aufstellungen.TryGetValue(vorhanden, out var alt)) return Ergebnis.Mit(Fehlercode.Unbekannt);
            if (!DarfAufstellungAendern(w, anfragender, alt)) return Ergebnis.Mit(Fehlercode.KeinRecht);
            if (version != alt.Version) return Ergebnis.Mit(Fehlercode.Veraltet);
            neu = alt with { Version = alt.Version + 1, Name = name, Eintraege = [.. eintraege], Geaendert = w.Jetzt, Durch = anfragender };
        }
        else
        {
            neu = new Aufstellung(neben.NaechsteAufstellungsId, 1, name)
            {
                Eintraege = [.. eintraege],
                Geaendert = w.Jetzt,
                Durch = anfragender,
                Ersteller = anfragender,
            };
            neben = neben with { NaechsteAufstellungsId = neben.NaechsteAufstellungsId + 1 };
        }
        w.Neben = neben with { Aufstellungen = neben.Aufstellungen.SetItem(neu.Id, neu) };
        return new Ergebnis { AufstellungsId = neu.Id, AufstellungsVersion = neu.Version, AufstellungGeaendert = true, AufstellungDurch = anfragender };
    }

    internal static Ergebnis AufstellungLoeschenAus(Arbeitsstand w, uint anfragender, uint id, uint version)
    {
        if (!w.RechteVon(anfragender).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (!w.Neben.Aufstellungen.TryGetValue(id, out var alt)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!DarfAufstellungAendern(w, anfragender, alt)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (alt.Version != version) return Ergebnis.Mit(Fehlercode.Veraltet);
        w.Neben = w.Neben with { Aufstellungen = w.Neben.Aufstellungen.Remove(id) };
        return new Ergebnis { AufstellungsId = id, AufstellungsVersion = version, AufstellungGeaendert = true, AufstellungDurch = anfragender };
    }

    /// <summary>
    /// F10 (6.12 Regel 10): Gespeicherte Aufstellungen anderer ändern oder löschen nur ihr Ersteller oder ein ADMIN. Anwenden
    /// und eigene anlegen darf jeder mit <c>ZUWEISEN</c>; so überschreibt ein Beauftragter keine vorbereiteten Aufstellungen.
    /// </summary>
    private static bool DarfAufstellungAendern(Arbeitsstand w, uint anfragender, Aufstellung a) =>
        a.Ersteller == anfragender || w.Konto(anfragender)?.Stufe == Stufe.Admin;

    /// <summary>Rolle, Schiff, Platz und Feldliste eines Eintrags (6.7 Schritt 1, 6.9).</summary>
    private static Fehlercode EintragPruefen(Arbeitsstand w, AufstellungsEintrag e)
    {
        if (e.RolleId != 0)
        {
            if (!w.Rollen.TryGetValue(e.RolleId, out var rolle)) return Fehlercode.Unbekannt;
            var (code, _) = PlatzPruefen(w, rolle, e.SchiffId);
            if (code != Fehlercode.Keiner) return code;
        }
        else if (e.SchiffId != 0)
        {
            return Fehlercode.Ungueltig;
        }
        if (e.Felder is { } felder)
        {
            var code = Feldregeln.VorgabenPruefen(felder, w.Netze);
            if (code != Fehlercode.Keiner) return code;
        }
        return Fehlercode.Keiner;
    }

    private static Aenderung AlsAenderung(uint kontoId, AufstellungsEintrag e) => new(kontoId)
    {
        RolleId = e.RolleId,
        SchiffId = e.SchiffId,
        Felder = e.Felder,
        SendeFelder = e.SendeFelder,
    };

    /// <summary>
    /// AufstellungAnwenden nach 6.7. Die Aufstellung hat Vorrang: nicht gelistete Inhaber eines von ihr gebrauchten
    /// Platzes fallen auf STANDARD zurück, soweit die Kapazität sonst nicht reicht (zuerst selbst gewählte, dann die
    /// zuletzt gesetzten), aber nur Inhaber gleicher oder niedrigerer Stufe (6.10). PLATZ_BELEGT, wenn die Aufstellung
    /// zusammen mit solchen geschützten Inhabern einen Platz überbucht und nicht erzwingt.
    /// </summary>
    internal static Ergebnis AufstellungAnwendenAus(Arbeitsstand w, uint anfragender, uint id, uint? version,
        Aufstellungsmodus modus, long? basisVersion, bool erzwingen)
    {
        if (w.Konto(anfragender) is not { } anf) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!w.Wirksam(anf).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (!w.Neben.Aufstellungen.TryGetValue(id, out var aufstellung)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (version is { } v && v != aufstellung.Version) return Ergebnis.Mit(Fehlercode.Veraltet);

        // Schritt 1: alles vorab prüfen.
        var fehler = new List<Eintragsfehler>();
        var uebersprungen = new List<UebersprungenerEintrag>();
        var unverknuepft = new List<string>();
        var offeneDiscord = ImmutableSortedDictionary.CreateBuilder<string, AufstellungsEintrag>(StringComparer.Ordinal);
        var plan = new List<(int Eintrag, Konto Konto, AufstellungsEintrag E)>();
        var geplant = new HashSet<uint>();
        for (var i = 0; i < aufstellung.Eintraege.Length; i++)
        {
            var e = aufstellung.Eintraege[i];
            Konto? k;
            if (e.KontoId != 0)
            {
                k = w.Konto(e.KontoId);
                if (k is null)
                {
                    uebersprungen.Add(new UebersprungenerEintrag(i, e.KontoId, Fehlercode.Unbekannt));
                    continue;
                }
            }
            else
            {
                k = w.Konten.Values.FirstOrDefault(x => x.DiscordId == e.DiscordId);
                if (k is null)
                {
                    unverknuepft.Add(e.DiscordId!);
                    offeneDiscord[e.DiscordId!] = e;
                    continue;
                }
            }
            if (!Rechteregeln.DarfZuweisen(anf.Stufe, k, w.Jetzt))
            {
                uebersprungen.Add(new UebersprungenerEintrag(i, k.Id, Fehlercode.KeinRecht));
                continue;
            }
            if (!geplant.Add(k.Id))
            {
                fehler.Add(new Eintragsfehler(i, k.Id, e.DiscordId, Fehlercode.Ungueltig));
                continue;
            }
            var code = EintragPruefen(w, e);
            if (code != Fehlercode.Keiner) fehler.Add(new Eintragsfehler(i, k.Id, e.DiscordId, code));
            plan.Add((i, k, e));
        }
        if (fehler.Count > 0) return Ergebnis.Mit(fehler[0].Code, fehler);
        if (basisVersion is { } b && plan.Select(p => p.Konto).Where(k => k.KontoVersion > b).ToList() is { Count: > 0 } veraltet)
            return Veraltet(veraltet);

        // Schritt 2: Kapazität, die die Aufstellung selbst braucht. Nicht gelistete Inhaber höherer Stufe als der
        // Anfragende verdrängt sie nicht (6.10: ZUWEISEN wirkt nur auf gleiche oder niedrigere Stufe); sie zählen mit.
        var gruppen = plan.Where(p => p.E.RolleId != 0).GroupBy(p => (p.E.SchiffId, p.E.RolleId)).ToList();
        if (!erzwingen)
        {
            foreach (var g in gruppen)
            {
                if (Kapazitaet(w, g.Key.SchiffId, g.Key.RolleId) is { } kap
                    && g.Count() + Geschuetzt(w, anf, g.Key.SchiffId, g.Key.RolleId, geplant).Count > kap)
                    fehler.AddRange(g.Select(p => new Eintragsfehler(p.Eintrag, p.Konto.Id, p.E.DiscordId, Fehlercode.PlatzBelegt)));
            }
            if (fehler.Count > 0) return Ergebnis.Mit(Fehlercode.PlatzBelegt, fehler.OrderBy(f => f.Eintrag));
        }

        // Schritt 3: ERSETZEN setzt nicht gelistete Konten mit Platz zurück.
        var zurueck = new List<uint>();
        if (modus == Aufstellungsmodus.Ersetzen)
        {
            foreach (var k in w.Konten.Values.ToList())
            {
                if (geplant.Contains(k.Id) || k.Id == anf.Id || w.Wirksam(k).Hat(Recht.Zuweisen)) continue;
                if (!Rechteregeln.DarfZuweisen(anf.Stufe, k, w.Jetzt) || w.Platzzustand(k) == PlatzZustand.Frei) continue;
                w.KontoSetzen(AufStandard(w, k, anf.Id, letztenStandMerken: false), umstellungDurch: anf.Id);
                zurueck.Add(k.Id);
            }
        }

        // Schritte 4 und 6: anwenden; Konten ohne Sitzung bekommen einen RESERVIERTEN Platz.
        foreach (var (eintrag, konto, e) in plan)
        {
            var (code, neu) = Anwenden(w, w.Konto(konto.Id)!, AlsAenderung(konto.Id, e), anf.Id, Quelle.Aufstellung);
            if (code != Fehlercode.Keiner) return Ergebnis.Mit(code, [new Eintragsfehler(eintrag, konto.Id, e.DiscordId, code)]);
            w.KontoSetzen(neu, umstellungDurch: anf.Id);
        }

        // Schritt 2, zweiter Teil: Vorrang der Aufstellung vor nicht gelisteten Inhabern.
        var verdraengt = new List<uint>();
        foreach (var g in gruppen)
        {
            if (Kapazitaet(w, g.Key.SchiffId, g.Key.RolleId) is not { } kap) continue;
            var geschuetzt = Geschuetzt(w, anf, g.Key.SchiffId, g.Key.RolleId, geplant).Count;
            var andere = Inhaber(w, g.Key.SchiffId, g.Key.RolleId)
                .Where(x => !geplant.Contains(x.Id) && Rechteregeln.DarfZuweisen(anf.Stufe, x, w.Jetzt))
                .OrderBy(x => x.Reservierung is null ? 0 : 1)
                .ThenByDescending(x => x.Reservierung?.Gesetzt ?? x.Platz!.Value.Gesetzt)
                .ThenByDescending(x => x.Id)
                .ToList();
            var zuviel = andere.Count - Math.Max(0, kap - g.Count() - geschuetzt);
            foreach (var x in andere.Take(Math.Max(0, zuviel)))
            {
                w.KontoSetzen(AufStandard(w, x, anf.Id, letztenStandMerken: false), umstellungDurch: anf.Id);
                verdraengt.Add(x.Id);
            }
        }

        var offline = plan.Where(p => !w.Online(p.Konto.Id)).Select(p => p.Konto.Id).ToList();
        w.Neben = w.Neben with
        {
            Letzte = new LetzteAufstellung(aufstellung.Id, aufstellung.Version, w.Jetzt, anf.Id, anf.Stufe)
            {
                OffeneKonten = [.. offline],
                OffeneDiscordIds = offeneDiscord.ToImmutable(),
            },
        };
        return new Ergebnis
        {
            AufstellungsId = aufstellung.Id,
            AufstellungsVersion = aufstellung.Version,
            Anzahl = plan.Count,
            Offline = [.. offline],
            Unverknuepft = [.. unverknuepft],
            Verdraengt = [.. verdraengt],
            Zurueckgesetzt = [.. zurueck],
            Uebersprungen = [.. uebersprungen],
            BetroffeneKonten = [.. plan.Select(p => p.Konto.Id).Concat(zurueck).Concat(verdraengt).Distinct()],
        };
    }

    /// <summary>Nicht gelistete Inhaber eines Platzes, die der Anfragende wegen ihrer höheren Stufe nicht verdrängen darf (6.10).</summary>
    private static List<Konto> Geschuetzt(Arbeitsstand w, Konto anf, ushort schiffId, ushort rolleId, HashSet<uint> geplant) =>
        [.. Inhaber(w, schiffId, rolleId).Where(x => !geplant.Contains(x.Id) && !Rechteregeln.DarfZuweisen(anf.Stufe, x, w.Jetzt))];

    /// <summary>
    /// Nachziehen bei der Verknüpfung (6.9): ein offener Eintrag der zuletzt angewandten Aufstellung für diese
    /// Discord-ID greift, solange höchstens 12 h vergangen sind und der Platz noch frei genug ist.
    /// </summary>
    private static void Nachziehen(Arbeitsstand w, Konto konto, List<Meldung> meldungen)
    {
        if (konto.DiscordId is not { } discordId || w.Neben.Letzte is not { } la) return;
        if (w.Jetzt - la.Zeit > Fristen.NachziehenMs || !la.OffeneDiscordIds.TryGetValue(discordId, out var e)) return;
        w.Neben = w.Neben with { Letzte = la with { OffeneDiscordIds = la.OffeneDiscordIds.Remove(discordId) } };
        // Nachziehen gilt nur, solange der Anwender noch einteilen darf: Mit seinem Auftrag endet es (6.12 Regel 7), auch
        // wenn der Takt den abgelaufenen Auftrag noch nicht abgeräumt hat.
        if (!w.RechteVon(la.Anwender).Hat(Recht.Zuweisen)) return;
        if (!Rechteregeln.DarfZuweisen(la.AnwenderStufe, konto, w.Jetzt) || EintragPruefen(w, e) != Fehlercode.Keiner) return;

        var punkt = w.Sichern();
        var (code, neu) = Anwenden(w, w.Konto(konto.Id)!, AlsAenderung(konto.Id, e), la.Anwender, Quelle.Aufstellung);
        if (code != Fehlercode.Keiner) return;
        w.KontoSetzen(neu, umstellungDurch: la.Anwender);
        if (neu.Platz is { } p && Ueberbelegt(w, [(p.SchiffId, p.RolleId)]).Count > 0)
        {
            w.Zuruecksetzen(punkt);
            return;
        }
        meldungen.Add(new Meldung(MeldungsArt.AufstellungNachgezogen, konto.Id, la.Anwender));
    }
}
