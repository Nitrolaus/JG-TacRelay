// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Identitaet;

/// <summary>Antwort auf <c>GeraeteListe</c> (4.5), höchstens 500 Einträge, älteste zuerst.</summary>
public sealed record Geraeteauskunft(Fehlercode Fehler, IReadOnlyList<Geraet> Geraete) : Ergebnis(Fehler, []);

/// <summary>
/// Zusätze einer Freigabe (3.5, F14): <see cref="AnKonto"/> hängt das Gerät eines Antrags an ein vorhandenes Konto (zweites
/// Gerät eines Mitglieds), <see cref="Vormerkung"/> übernimmt eine Vormerkung (ihr SC-Name wird Hauptname). Beides nur
/// ausdrücklich, nie über einen Namensvergleich allein.
/// </summary>
public sealed record Freigabezusatz(uint? AnKonto = null, Vormerkungsuebernahme? Vormerkung = null);

/// <summary>Was das Kontenbuch aus einer Vormerkung übernimmt; Rollen zieht das Mitgliederbuch nach.</summary>
public sealed record Vormerkungsuebernahme(uint VormerkungId, string ScName, string? DiscordName);

public sealed partial class Kontenbuch
{
    /// <summary>Höchstens so viele Einträge in <c>GeraeteListe</c>.</summary>
    public const int MaxGeraeteListe = 500;

    /// <summary>
    /// <c>GeraetEntscheiden</c> (3.5, 4.5). FREIGEBEN und ABLEHNEN nur mit <c>GERAETE_FREIGEBEN</c> in einer Admin-Sitzung mit
    /// frischer Bestätigung und nur für nicht privilegierte Konten, sonst <c>NUR_ADMIN_WERKZEUG</c>; das Admin-Werkzeug darf
    /// alles. BESTAETIGEN und EINSPRUCH nur von einem freigegebenen Geschwistergerät; sie setzen nur die Markierung. Bei
    /// einem Antrag (3.4) kommen dessen Namen erst jetzt in den Namensraum, und nur die, die noch frei sind; mit
    /// <paramref name="zusatz"/> hängt das Gerät an ein vorhandenes Konto oder übernimmt eine Vormerkung. ABLEHNEN löscht seit
    /// 0.7.0 das wartende Gerät und einen Antrag ganz (<see cref="AblehnenIntern"/>).
    /// </summary>
    public Ergebnis GeraetEntscheiden(Akteur akteur, uint geraetId, Entscheidung entscheidung, Freigabezusatz? zusatz = null)
    {
        if (!Enum.IsDefined(entscheidung)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var s = _stand;
        var jetzt = Jetzt;
        if (!AkteurPruefen(s, akteur, out var ak)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var freigabe = entscheidung is Entscheidung.Freigeben or Entscheidung.Ablehnen;
        if (freigabe)
        {
            var recht = VerwaltungsrechtPruefen(akteur, ak, Rechte.GeraeteFreigeben);
            if (recht != Fehlercode.Keiner) return Ergebnis.Mit(recht);
        }
        else if (akteur.IstAdminWerkzeug)
        {
            return Ergebnis.Mit(Fehlercode.KeinRecht);
        }
        if (zusatz is not null && entscheidung != Entscheidung.Freigeben) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (!s.Geraete.TryGetValue(geraetId, out var g) || IstVerfallen(g, jetzt)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        var konto = s.Konten[g.KontoId];
        if (freigabe)
        {
            if (!akteur.IstAdminWerkzeug && konto.Privilegiert) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
        }
        else if (akteur.KontoId != g.KontoId || akteur.GeraetId == geraetId)
        {
            return Ergebnis.Mit(Fehlercode.KeinRecht);
        }
        if (g.Status != GeraetStatus.Wartet) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (freigabe && BestaetigungPruefen(akteur) is var b and not Fehlercode.Keiner) return Ergebnis.Mit(b);

        uint? durch = akteur.IstAdminWerkzeug ? null : akteur.KontoId;
        List<Wirkung> w;
        switch (entscheidung)
        {
            case Entscheidung.Freigeben when konto.IstAntrag || zusatz is not null:
            {
                var e = AntragFreigeben(ref s, akteur, g, konto, zusatz ?? new Freigabezusatz(), durch, jetzt);
                if (e is not null) return e;
                w = [new GeraetStatusGeaendert(g.GeraetId, s.Geraete[g.GeraetId].KontoId, GeraetStatusMeldung.Freigegeben, durch, null)];
                break;
            }
            case Entscheidung.Freigeben:
                s = MitGeraet(s, g with { Status = GeraetStatus.Freigegeben, FreigegebenAm = jetzt, WartefristAb = null, Markierung = Markierung.Keine });
                w = [new GeraetStatusGeaendert(g.GeraetId, g.KontoId, GeraetStatusMeldung.Freigegeben, durch, null)];
                break;
            case Entscheidung.Ablehnen:
                w = [];
                s = AblehnenIntern(s, g, durch, w);
                break;
            default:
                var markierung = entscheidung == Entscheidung.Bestaetigen ? Markierung.Bestaetigt : Markierung.Einspruch;
                s = MitGeraet(s, g with { Markierung = markierung });
                w = [new GeraetStatusGeaendert(g.GeraetId, g.KontoId, GeraetStatusMeldung.Wartet, durch, null)];
                break;
        }
        if (freigabe) s = Ereignis(s, s.Geraete.TryGetValue(g.GeraetId, out var neu) ? neu.KontoId : g.KontoId, jetzt);
        Uebernehmen(s);
        return new Ergebnis(Fehlercode.Keiner, w);
    }

    /// <summary>
    /// Freigabe eines Antrags (3.4, 3.5): an ein vorhandenes Konto hängen oder das Antragskonto zum Konto machen. Liefert
    /// einen Fehler oder <c>null</c>; <paramref name="s"/> trägt dann den neuen Stand.
    /// </summary>
    private Ergebnis? AntragFreigeben(ref Kontenstand s, Akteur akteur, Geraet g, Konto konto, Freigabezusatz zusatz, uint? durch, DateTimeOffset jetzt)
    {
        if (zusatz.AnKonto is not null && zusatz.Vormerkung is not null) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var freigegeben = g with { Status = GeraetStatus.Freigegeben, FreigegebenAm = jetzt, WartefristAb = null, Markierung = Markierung.Keine };
        if (zusatz.AnKonto is { } zielId)
        {
            // Zweites Gerät eines Mitglieds: nur aus einem Antrag, nur an ein freigegebenes, aktives Konto (3.5, Regeln für Admins).
            if (!konto.IstAntrag || zielId == konto.KontoId) return Ergebnis.Mit(Fehlercode.Ungueltig);
            if (!s.Konten.TryGetValue(zielId, out var ziel) || ziel.IstAntrag) return Ergebnis.Mit(Fehlercode.Unbekannt);
            if (ziel.Status != KontoStatus.Aktiv) return Ergebnis.Mit(Fehlercode.KontoGesperrt);
            if (!akteur.IstAdminWerkzeug && ziel.Privilegiert) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
            if (AktiveGeraete(s, zielId, jetzt) >= MaxAktiveGeraete) return Ergebnis.Mit(Fehlercode.GeraeteLimit);
            s = MitGeraet(s, freigegeben with { KontoId = zielId });
            s = Ereignis(s with { Konten = s.Konten.Remove(konto.KontoId) }, zielId, jetzt);
            return null;
        }
        if (!konto.IstAntrag) return Ergebnis.Mit(Fehlercode.Ungueltig);

        // Der Discord-Name muss frei sein (A53): Er ist der Name in "Wer ist da", und mit dem eines anderen gäbe sich der
        // Antragsteller als dieser aus. Belegt ihn schon ein Konto oder eine andere Vormerkung, bekommt das neue Konto ihn nicht;
        // die übernommene Vormerkung zählt nicht. Bis 0.6.x brach die Freigabe hier mit NAME_VERGEBEN ab. Seit 0.7.0 gilt ein PC,
        // ein Konto (Sarevoc, 04.10.2026): Der zweite PC desselben Menschen trägt denselben Namen und darf daran nicht scheitern.
        var discordName = konto.Antrag!.DiscordName ?? zusatz.Vormerkung?.DiscordName;
        if (discordName is not null && DiscordNameBesitzer(s, discordName, konto.KontoId, zusatz.Vormerkung?.VormerkungId) is not null)
            discordName = null;

        // Namen kommen erst jetzt in den Namensraum, und nur freie; ein vergebener Name bricht die Freigabe nicht ab
        // (RAIDLEITUNG-ENTWURF, SC-Namen). Die übernommene Vormerkung zählt dabei nicht als Belegung.
        var ignoriert = zusatz.Vormerkung is { } v ? Zeichenregeln.Namensschluessel(v.ScName) : null;
        var wunsch = new List<string>(2);
        if (zusatz.Vormerkung is { } vm) wunsch.Add(vm.ScName);
        wunsch.Add(konto.Antrag!.ScName);
        var namen = new List<string>(2);
        foreach (var n in wunsch)
        {
            var schluessel = Zeichenregeln.Namensschluessel(n);
            if (namen.Exists(x => Zeichenregeln.Namensschluessel(x) == schluessel)) continue;
            if (s.NachScName.ContainsKey(schluessel)) continue;
            if (schluessel != ignoriert && VormerkungsNamen?.Invoke(schluessel) == true) continue;
            namen.Add(n);
        }
        var anzeige = EindeutigerName(s, namen.Count > 0 ? namen[0] : konto.Antrag.ScName, konto.KontoId);
        var neu = konto with
        {
            Name = anzeige,
            ScNamen = [.. namen],
            DiscordName = discordName,
            Antrag = null,
        };
        s = MitGeraet(MitKonto(s, neu), freigegeben);
        s = s with
        {
            NachNamensschluessel = s.NachNamensschluessel.SetItem(Zeichenregeln.Namensschluessel(anzeige), konto.KontoId),
            NachDiscordName = discordName is null ? s.NachDiscordName : s.NachDiscordName.SetItem(Zeichenregeln.Namensschluessel(discordName), konto.KontoId),
        };
        s = MitScNamen(s, konto.KontoId, [], neu.ScNamen);
        return null;
    }

    /// <summary>
    /// ABLEHNEN (3.5) seit 0.7.0 (Sarevoc, 04.10.2026: Löschen heißt vollständig entfernen, nichts sperrt von selbst): Das wartende
    /// Gerät verschwindet samt Fingerabdruck aus dem Stand, bei einem Antrag auch das Antragskonto mit seinen Namen. Nichts kommt
    /// auf die Sperrliste; der PC kann später einen neuen Antrag stellen, der wieder eine Freigabe braucht. Die wartende Verbindung
    /// bekommt <c>GeraetStatus{ABGELEHNT}</c> aus dem Stand davor (<see cref="GeraetStatusGeaendert.Vorher"/>) und dann
    /// <c>Bye{ENTFERNT}</c>, damit der Client nicht gleich von selbst wieder beantragt. Ein Konto, das kein Antrag ist, bleibt.
    /// </summary>
    private static Kontenstand AblehnenIntern(Kontenstand s, Geraet g, uint? durch, List<Wirkung> w)
    {
        w.Add(new GeraetStatusGeaendert(g.GeraetId, g.KontoId, GeraetStatusMeldung.Abgelehnt, durch, null) { Vorher = s });
        w.Add(new SitzungenBeenden(g.KontoId, g.GeraetId, Trenngrund.GeraetAbgelehnt));
        s = OhneGeraet(s, g);
        return s.Konten.TryGetValue(g.KontoId, out var k) && k.IstAntrag ? KontoEntfernenIntern(s, k, Trenngrund.KontoGeloescht, w) : s;
    }

    /// <summary>
    /// <c>GeraetEntfernen</c> (3.6, 4.5). Dauerhaft (ENTZOGEN) nur mit <c>GERAETE_FREIGEBEN</c> oder über das
    /// Admin-Werkzeug. Das ist die einzige Gerätesperre und immer eine bewusste Handlung eines Admins (seit 0.7.0 sperren
    /// Ablehnen und Löschen nichts mehr); der Fingerabdruck bleibt gesperrt, auch wenn das Konto später gelöscht wird. Aufheben
    /// lässt sie sich seit 0.8.0 mit denselben Rechten (<see cref="GeraetEntsperren"/>, an der Konsole auch ohne Gerät).
    /// Sonst: das eigene Gerät und jüngere Geschwister sofort ENTFERNT, ältere Geschwister nur BEANTRAGT (das ältere Gerät
    /// gewinnt). Fremde Geräte nur
    /// mit <c>GERAETE_FREIGEBEN</c>. Geräte von ADMIN-Konten nur durch einen ADMIN (analog 6.10 Regel 3);
    /// Geschwistergeräte nie dauerhaft über den Client (T6). Fremde Geräte privilegierter Konten (andere Admins) weder
    /// entfernen noch dauerhaft sperren, nur an der Konsole (3.9, 6.10 Regel 9): Gegen einen anderen Admin gibt es im
    /// Client nur die Notbremse "Konto sperren", und ein neues Gerät eines privilegierten Kontos gibt nur die Konsole frei.
    /// </summary>
    public Entfernungsergebnis GeraetEntfernen(Akteur akteur, uint geraetId, bool dauerhaft, string grund)
    {
        ArgumentNullException.ThrowIfNull(grund);
        if (!Zeichenregeln.IstText(grund, 0, 40)) return new(Fehlercode.Ungueltig, null, []);
        var s = _stand;
        var jetzt = Jetzt;
        if (!AkteurPruefen(s, akteur, out var ak)) return new(Fehlercode.KeinRecht, null, []);
        if (dauerhaft && VerwaltungsrechtPruefen(akteur, ak, Rechte.GeraeteFreigeben) is var dr and not Fehlercode.Keiner) return new(dr, null, []);
        if (!s.Geraete.TryGetValue(geraetId, out var g) || IstVerfallen(g, jetzt)) return new(Fehlercode.Unbekannt, null, []);

        var ziel = s.Konten[g.KontoId];
        var rangVerletzt = !akteur.IstAdminWerkzeug && ziel.Stufe == Stufe.Admin && ak!.Stufe != Stufe.Admin;
        uint? durch = akteur.IstAdminWerkzeug ? null : akteur.KontoId;
        var text = grund.Length == 0 ? null : grund;
        // Geräte des Eigentümers fasst im Client niemand außer ihm selbst an (3.10).
        if (!akteur.IstAdminWerkzeug && ziel.Eigentuemer && akteur.KontoId != ziel.KontoId) return new(Fehlercode.NurAdminWerkzeug, null, []);

        if (dauerhaft)
        {
            var geschwister = !akteur.IstAdminWerkzeug && akteur.KontoId == g.KontoId && akteur.GeraetId != geraetId;
            if (geschwister || rangVerletzt) return new(Fehlercode.KeinRecht, null, []);
            if (!akteur.IstAdminWerkzeug && akteur.KontoId != g.KontoId && ziel.Privilegiert) return new(Fehlercode.NurAdminWerkzeug, null, []);
            if (g.Status == GeraetStatus.Entzogen) return new(Fehlercode.Keiner, GeraetStatusMeldung.Entzogen, []);
            if (BestaetigungPruefen(akteur) is var db and not Fehlercode.Keiner) return new(db, null, []);
            s = MitGeraet(s, g with { Status = GeraetStatus.Entzogen, WartefristAb = null, AntragVon = null, Markierung = Markierung.Keine });
            s = Ereignis(s with { GesperrteFingerabdruecke = s.GesperrteFingerabdruecke.Add(g.Fingerabdruck) }, g.KontoId, jetzt);
            Uebernehmen(s);
            return new(Fehlercode.Keiner, GeraetStatusMeldung.Entzogen,
            [
                new SitzungenBeenden(g.KontoId, g.GeraetId, Trenngrund.GeraetEntzogen),
                new GeraetStatusGeaendert(g.GeraetId, g.KontoId, GeraetStatusMeldung.Entzogen, durch, text),
            ]);
        }

        bool sofort;
        var fremd = false;
        if (akteur.IstAdminWerkzeug || akteur.GeraetId == geraetId) sofort = true;
        else if (akteur.KontoId == g.KontoId) sofort = g.IstJuengerAls(s.Geraete[akteur.GeraetId]);
        else if (ak!.Hat(Rechte.GeraeteFreigeben) && !rangVerletzt)
        {
            if (!akteur.AdminSitzung) return new(Fehlercode.AdminSitzungNoetig, null, []);
            if (ziel.Privilegiert) return new(Fehlercode.NurAdminWerkzeug, null, []);
            sofort = true;
            fremd = true;
        }
        else return new(Fehlercode.KeinRecht, null, []);
        if (!g.IstAktiv) return new(Fehlercode.Ungueltig, null, []);
        if (fremd && BestaetigungPruefen(akteur) is var fb and not Fehlercode.Keiner) return new(fb, null, []);

        if (!sofort)
        {
            if (g.AntragVon == akteur.GeraetId) return new(Fehlercode.Keiner, GeraetStatusMeldung.Beantragt, []);
            Uebernehmen(MitGeraet(s, g with { AntragVon = akteur.GeraetId }));
            return new(Fehlercode.Keiner, GeraetStatusMeldung.Beantragt,
            [
                new GeraetStatusGeaendert(g.GeraetId, g.KontoId, GeraetStatusMeldung.Beantragt, durch, text),
                new Meldung("GERAET_ANTRAG", g.KontoId, g.GeraetId),
            ]);
        }

        s = MitGeraet(s, g with { Status = GeraetStatus.Entfernt, WartefristAb = null, AntragVon = null, Markierung = Markierung.Keine });
        Uebernehmen(Ereignis(s, g.KontoId, jetzt));
        return new(Fehlercode.Keiner, GeraetStatusMeldung.Entfernt,
        [
            new SitzungenBeenden(g.KontoId, g.GeraetId, Trenngrund.GeraetEntfernt),
            new GeraetStatusGeaendert(g.GeraetId, g.KontoId, GeraetStatusMeldung.Entfernt, durch, text),
        ]);
    }

    /// <summary>
    /// Sperre aufheben (seit 0.8.0, 3.6): Ein ENTZOGENES Gerät wird ENTFERNT, und sein Fingerabdruck kommt von der Sperrliste. Der
    /// PC gilt danach wie unbekannt und kann einen neuen Antrag stellen, der wieder eine Freigabe braucht; von selbst kommt er nicht
    /// in den Funk. Rechte wie beim Sperren (<see cref="GeraetEntfernen"/> mit dauerhaft): <c>GERAETE_FREIGEBEN</c> in der
    /// Admin-Sitzung mit frischer Bestätigung, keine Geschwistergeräte über den Client, Geräte eines ADMIN-Kontos nur durch einen
    /// ADMIN, Geräte fremder privilegierter Konten und des Eigentümers nur an der Konsole. Ein Gerät ohne Sperre ergibt
    /// <c>UNGUELTIG</c>.
    /// </summary>
    public Ergebnis GeraetEntsperren(Akteur akteur, uint geraetId)
    {
        var s = _stand;
        var jetzt = Jetzt;
        if (!AkteurPruefen(s, akteur, out var ak)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (VerwaltungsrechtPruefen(akteur, ak, Rechte.GeraeteFreigeben) is var r and not Fehlercode.Keiner) return Ergebnis.Mit(r);
        if (!s.Geraete.TryGetValue(geraetId, out var g)) return Ergebnis.Mit(Fehlercode.Unbekannt);

        var ziel = s.Konten[g.KontoId];
        if (!akteur.IstAdminWerkzeug)
        {
            if (ziel.Eigentuemer && akteur.KontoId != ziel.KontoId) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
            var geschwister = akteur.KontoId == g.KontoId && akteur.GeraetId != geraetId;
            var rangVerletzt = ziel.Stufe == Stufe.Admin && ak!.Stufe != Stufe.Admin;
            if (geschwister || rangVerletzt) return Ergebnis.Mit(Fehlercode.KeinRecht);
            if (akteur.KontoId != g.KontoId && ziel.Privilegiert) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
        }
        // ABGELEHNT kann es seit 0.7.0 nicht mehr geben (die Umstellung verwirft es); gesperrt wäre es trotzdem (Einstufen).
        var entzogen = g.Status is GeraetStatus.Entzogen or GeraetStatus.Abgelehnt;
        if (!entzogen && !s.GesperrteFingerabdruecke.Contains(g.Fingerabdruck)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (BestaetigungPruefen(akteur) is var b and not Fehlercode.Keiner) return Ergebnis.Mit(b);

        uint? durch = akteur.IstAdminWerkzeug ? null : akteur.KontoId;
        var w = new List<Wirkung>();
        if (entzogen)
        {
            s = MitGeraet(s, g with { Status = GeraetStatus.Entfernt, WartefristAb = null, AntragVon = null, Markierung = Markierung.Keine });
            w.Add(new GeraetStatusGeaendert(g.GeraetId, g.KontoId, GeraetStatusMeldung.Entfernt, durch, null));
        }
        s = Ereignis(s with { GesperrteFingerabdruecke = s.GesperrteFingerabdruecke.Remove(g.Fingerabdruck) }, g.KontoId, jetzt);
        Uebernehmen(s);
        return new Ergebnis(Fehlercode.Keiner, w);
    }

    /// <summary>
    /// Sperre eines Fingerabdrucks aufheben, zu dem es kein Gerät mehr gibt (seit 0.8.0, nur Konsole): Eine bewusste Sperre bleibt
    /// stehen, auch wenn das Konto des Geräts später gelöscht wird; dann zeigt sie keine Mitgliederkarte mehr. Gibt es noch ein Gerät
    /// zum Fingerabdruck, gilt <see cref="GeraetEntsperren"/>. Ohne Sperre <c>UNGUELTIG</c>.
    /// </summary>
    public Ergebnis FingerabdruckEntsperren(Akteur akteur, Fingerabdruck fingerabdruck)
    {
        if (!akteur.IstAdminWerkzeug) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
        var s = _stand;
        if (s.GeraetZuFingerabdruck(fingerabdruck) is { } g) return GeraetEntsperren(akteur, g.GeraetId);
        if (!s.GesperrteFingerabdruecke.Contains(fingerabdruck)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        Uebernehmen(s with { GesperrteFingerabdruecke = s.GesperrteFingerabdruecke.Remove(fingerabdruck) });
        return Ergebnis.Mit(Fehlercode.Keiner);
    }

    /// <summary>
    /// Ein Admin weist einen Entfernungsantrag zurück (3.6: "den ein Admin entscheidet"). Die andere Entscheidung
    /// ist <see cref="GeraetEntfernen"/> durch den Admin. Eine eigene Drahtnachricht dafür gibt es in 4.5 noch nicht.
    /// </summary>
    public Ergebnis AntragZurueckweisen(Akteur akteur, uint geraetId)
    {
        var s = _stand;
        if (!AkteurPruefen(s, akteur, out var ak)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (VerwaltungsrechtPruefen(akteur, ak, Rechte.GeraeteFreigeben) is var r and not Fehlercode.Keiner) return Ergebnis.Mit(r);
        if (!s.Geraete.TryGetValue(geraetId, out var g)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (g.AntragVon is null) return Ergebnis.Mit(Fehlercode.Ungueltig);
        Uebernehmen(MitGeraet(s, g with { AntragVon = null }));
        uint? durch = akteur.IstAdminWerkzeug ? null : akteur.KontoId;
        return new Ergebnis(Fehlercode.Keiner, [new GeraetStatusGeaendert(g.GeraetId, g.KontoId, (GeraetStatusMeldung)g.Status, durch, null)]);
    }

    /// <summary>
    /// <c>GeraeteListe</c> (4.5): eigene Geräte immer, fremde nur mit <c>GERAETE_FREIGEBEN</c>. Ohne
    /// <paramref name="kontoId"/> sieht ein Freigeber alle Konten, alle anderen nur das eigene.
    /// </summary>
    public Geraeteauskunft GeraeteListe(Akteur akteur, uint? kontoId, bool nurWartend)
    {
        var s = Stand;
        var jetzt = Jetzt;
        if (!AkteurPruefen(s, akteur, out var ak)) return new(Fehlercode.KeinRecht, []);
        var darfAlle = VerwaltungsrechtPruefen(akteur, ak, Rechte.GeraeteFreigeben) == Fehlercode.Keiner;
        if (kontoId is { } id && id != akteur.KontoId && !darfAlle)
            return new(VerwaltungsrechtPruefen(akteur, ak, Rechte.GeraeteFreigeben), []);
        uint? filter = kontoId ?? (darfAlle ? null : akteur.KontoId);

        var liste = new List<Geraet>();
        foreach (var g in s.Geraete.Values)
        {
            if (filter is { } f && g.KontoId != f) continue;
            if (IstVerfallen(g, jetzt) || (nurWartend && g.Status != GeraetStatus.Wartet)) continue;
            liste.Add(g);
        }
        liste.Sort(static (a, b) => a.IstJuengerAls(b) ? 1 : b.IstJuengerAls(a) ? -1 : 0);
        if (liste.Count > MaxGeraeteListe) liste.RemoveRange(MaxGeraeteListe, liste.Count - MaxGeraeteListe);
        return new(Fehlercode.Keiner, liste);
    }

    /// <summary>
    /// Admin-Werkzeug: Gerät per Fingerabdruck (Base64) oder Anzeigeform finden. Passt eine Anzeigeform auf
    /// mehrere Geräte, ist sie mehrdeutig (<c>UNGUELTIG</c>); dann den vollen Fingerabdruck nehmen.
    /// </summary>
    public Fehlercode GeraetSuchen(string anzeigeOderFingerabdruck, out Geraet? geraet)
    {
        ArgumentNullException.ThrowIfNull(anzeigeOderFingerabdruck);
        geraet = null;
        var s = Stand;
        if (Fingerabdruck.TryAusBase64(anzeigeOderFingerabdruck, out var fp))
        {
            geraet = s.GeraetZuFingerabdruck(fp);
            return geraet is null ? Fehlercode.Unbekannt : Fehlercode.Keiner;
        }
        if (anzeigeOderFingerabdruck.Length > 64) return Fehlercode.Ungueltig;
        foreach (var g in s.Geraete.Values)
        {
            if (!g.Fingerabdruck.AnzeigePasst(anzeigeOderFingerabdruck)) continue;
            if (geraet is not null)
            {
                geraet = null;
                return Fehlercode.Ungueltig;
            }
            geraet = g;
        }
        return geraet is null ? Fehlercode.Unbekannt : Fehlercode.Keiner;
    }

    /// <summary>
    /// Admin-Werkzeug: einen gesperrten Fingerabdruck finden, auch ohne Gerät (Konto später gelöscht). Wie
    /// <see cref="GeraetSuchen"/>: Fingerabdruck (Base64) oder Anzeigeform; passt eine Anzeigeform auf mehrere, ist sie mehrdeutig
    /// (<c>UNGUELTIG</c>).
    /// </summary>
    public Fehlercode SperreSuchen(string anzeigeOderFingerabdruck, out Fingerabdruck fingerabdruck)
    {
        ArgumentNullException.ThrowIfNull(anzeigeOderFingerabdruck);
        fingerabdruck = default;
        var s = Stand;
        if (Fingerabdruck.TryAusBase64(anzeigeOderFingerabdruck, out var fp))
        {
            if (!s.GesperrteFingerabdruecke.Contains(fp)) return Fehlercode.Unbekannt;
            fingerabdruck = fp;
            return Fehlercode.Keiner;
        }
        if (anzeigeOderFingerabdruck.Length > 64) return Fehlercode.Ungueltig;
        var treffer = 0;
        foreach (var f in s.GesperrteFingerabdruecke)
        {
            if (!f.AnzeigePasst(anzeigeOderFingerabdruck)) continue;
            fingerabdruck = f;
            treffer++;
        }
        if (treffer == 1) return Fehlercode.Keiner;
        fingerabdruck = default;
        return treffer == 0 ? Fehlercode.Unbekannt : Fehlercode.Ungueltig;
    }

    /// <summary>
    /// Ob die Freigabeansicht die Zusatzbestätigung "Rückfrage beim Inhaber erfolgt" verlangt (3.5: Konten der
    /// Stufe RAIDLEITER). Das ist eine Regel der Oberfläche; die Drahtnachricht trägt kein Feld dafür.
    /// </summary>
    public bool BrauchtRueckfrageBeimInhaber(uint geraetId)
    {
        var s = Stand; // genau ein Schnappschuss, auch wenn ein anderer Thread fragt
        return s.Geraete.TryGetValue(geraetId, out var g) && s.Konten.TryGetValue(g.KontoId, out var k) && k.Stufe == Stufe.Raidleiter;
    }

    /// <summary>Ob das Gerät das erste seines Kontos ist (für die Freigabeansicht, 3.5).</summary>
    public bool IstErstesGeraet(uint geraetId)
    {
        var s = Stand;
        if (!s.Geraete.TryGetValue(geraetId, out var g)) return false;
        foreach (var anderes in s.Geraete.Values)
            if (anderes.KontoId == g.KontoId && anderes.GeraetId != geraetId) return false;
        return true;
    }
}
