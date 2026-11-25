// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Server.Lage;

/// <summary>Rechteverwaltung (6.10) und Verwaltung von Netzen, Schiffen, Rollen, Einstellungen (4.5 Verwaltung).</summary>
public sealed partial class Zustandsschreiber
{
    /// <summary>
    /// KontoVerwaltung nach 6.10: (1) niemand vergibt ein Recht, das er nicht hat (Rechte aus einem Auftrag zählen dabei nicht);
    /// (2) niemand ändert die eigene Stufe oder die eigenen Rechte; (3) ADMIN-Konten ändert nur ein ADMIN; (4) den letzten
    /// aktiven ADMIN nur das Admin-Werkzeug; (5) STUFE setzt die Rechte auf den Standard zurück; (6) Meldung RECHTE_GEAENDERT.
    /// Danach die Folgen für Aufträge (6.12 Regel 6): Sperre, Stufenwechsel und Löschung beenden sie.
    /// </summary>
    internal static Ergebnis KontoVerwaltungAus(Arbeitsstand w, uint anfragender, KontoOperation op, uint kontoId,
        Stufe? stufe, Recht? rechte, string? name)
    {
        var alt = w.Konto(kontoId);
        var e = KontoVerwaltungKern(w, anfragender, op, kontoId, stufe, rechte, name);
        if (!e.Erfolg || alt is null) return e;
        var folgen = new Folgen();
        AuftraegeNachKontoaenderung(w, alt, w.Konto(kontoId), folgen);
        return folgen.Ergebnis(e);
    }

    private static Ergebnis KontoVerwaltungKern(Arbeitsstand w, uint anfragender, KontoOperation op, uint kontoId,
        Stufe? stufe, Recht? rechte, string? name)
    {
        if (w.Konto(anfragender) is not { } anf) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!anf.Rechte.Hat(Recht.KontenVerwalten)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (w.Konto(kontoId) is not { } ziel) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (ziel.Stufe == Stufe.Admin && anf.Stufe != Stufe.Admin) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var selbst = anf.Id == ziel.Id;
        var letzterAdmin = ziel.Stufe == Stufe.Admin && ziel.Status == KontoStatus.Aktiv
            && !w.Konten.Values.Any(k => k.Id != ziel.Id && k.Stufe == Stufe.Admin && k.Status == KontoStatus.Aktiv);

        switch (op)
        {
            case KontoOperation.Sperren:
            {
                if (letzterAdmin) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
                if (ziel.Status == KontoStatus.Gesperrt) return new Ergebnis();
                w.KontoSetzen(ziel with { Status = KontoStatus.Gesperrt });
                return new Ergebnis { BeendeteSitzungen = SitzungBeenden(w, ziel.Id, Sitzungsende.KontoGesperrt) };
            }
            case KontoOperation.Entsperren:
                if (ziel.Status == KontoStatus.Aktiv) return new Ergebnis();
                w.KontoSetzen(ziel with { Status = KontoStatus.Aktiv });
                return new Ergebnis();
            case KontoOperation.Stufe:
            {
                if (stufe is not { } neueStufe || !Enum.IsDefined(neueStufe)) return Ergebnis.Mit(Fehlercode.Ungueltig);
                if (selbst) return Ergebnis.Mit(Fehlercode.KeinRecht);
                if (neueStufe == Stufe.Admin && anf.Stufe != Stufe.Admin) return Ergebnis.Mit(Fehlercode.KeinRecht);
                var neueRechte = Rechteregeln.Standard(neueStufe);
                if ((neueRechte & ~ziel.Rechte & ~anf.Rechte) != 0) return Ergebnis.Mit(Fehlercode.KeinRecht);
                if (letzterAdmin && neueStufe != Stufe.Admin) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
                var neu = ziel with { Stufe = neueStufe, Plus = Recht.Keine, Minus = Recht.Keine };
                return RechteSchreiben(w, anf, ziel, neu);
            }
            case KontoOperation.Rechte:
            {
                if (rechte is not { } gewuenscht || (gewuenscht & ~Recht.Alle) != 0) return Ergebnis.Mit(Fehlercode.Ungueltig);
                if (selbst) return Ergebnis.Mit(Fehlercode.KeinRecht);
                if ((gewuenscht & ~ziel.Rechte & ~anf.Rechte) != 0) return Ergebnis.Mit(Fehlercode.KeinRecht);
                var (plus, minus) = Rechteregeln.Abweichung(ziel.Stufe, gewuenscht);
                return RechteSchreiben(w, anf, ziel, ziel with { Plus = plus, Minus = minus });
            }
            case KontoOperation.Name:
            {
                if (string.IsNullOrWhiteSpace(name) || Namen.Skalarwerte(name) > Fristen.NameMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
                w.KontoSetzen(ziel with { Name = Namen.Eindeutig(name, w.Konten.Values, ziel.Id) }, relevant: false);
                return new Ergebnis();
            }
            case KontoOperation.Loeschen:
            {
                if (letzterAdmin) return Ergebnis.Mit(Fehlercode.NurAdminWerkzeug);
                var beendet = SitzungBeenden(w, ziel.Id, Sitzungsende.KontoGeloescht);
                w.KontoEntfernen(ziel.Id);
                return new Ergebnis { BeendeteSitzungen = beendet };
            }
            default:
                return Ergebnis.Mit(Fehlercode.Ungueltig);
        }
    }

    private static Ergebnis RechteSchreiben(Arbeitsstand w, Konto anf, Konto alt, Konto neu)
    {
        if (neu.Stufe == alt.Stufe && neu.Rechte == alt.Rechte && neu.Plus == alt.Plus && neu.Minus == alt.Minus) return new Ergebnis();
        // Die Sendeauswahl bleibt gültig, auch wenn MEHRFACH_SENDEN oder GLOBAL_SENDEN wegfällt.
        neu = neu with { Sendeauswahl = Feldregeln.AuswahlBereinigen(w.Netze, neu.Sendeauswahl, neu.Felder, w.Wirksam(neu)) };
        w.KontoSetzen(neu);
        return new Ergebnis
        {
            Meldungen = [new Meldung(MeldungsArt.RechteGeaendert, neu.Id, anf.Id)],
            BetroffeneKonten = [neu.Id],
            GeraeteZurueckstufen = !alt.IstPrivilegiert && neu.IstPrivilegiert,
        };
    }

    private static ImmutableArray<BeendeteSitzung> SitzungBeenden(Arbeitsstand w, uint kontoId, Sitzungsende grund)
    {
        if (!w.SitzungJeKonto.TryGetValue(kontoId, out var sitzung)) return [];
        w.SitzungEntfernen(sitzung);
        if (w.Konto(kontoId) is { } k) w.KontoSetzen(k with { SitzungsEnde = w.Jetzt }, relevant: false);
        return [new BeendeteSitzung(sitzung, kontoId, grund)];
    }

    private static Fehlercode VerwaltungPruefen(Arbeitsstand w, uint anfragender, long version, Recht zusaetzlich = Recht.Keine)
    {
        var rechte = w.RechteVon(anfragender);
        if (!rechte.Hat(Recht.NetzeVerwalten) && (zusaetzlich == Recht.Keine || !rechte.Hat(zusaetzlich))) return Fehlercode.KeinRecht;
        return version != w.NetzListeVersion ? Fehlercode.Veraltet : Fehlercode.Keiner;
    }

    private static Ergebnis NetzListeNeu(Arbeitsstand w, IEnumerable<uint>? betroffen = null, IEnumerable<KanalsperrFolge>? sperren = null)
    {
        w.NetzListeVersion = w.NeueVersion;
        w.SchnappschussGeaendert = true;
        return new Ergebnis { NetzListeGeaendert = true, BetroffeneKonten = [.. betroffen ?? []], Kanalsperren = [.. sperren ?? []] };
    }

    /// <summary>
    /// NETZ_SETZEN (4.5, seit 0.6.0 mit <c>kanaele</c> und <c>gruppe</c>, 6.16): IDs ab 0x8000 sind den Kanälen vorbehalten; ein Netz von
    /// dort aus einem älteren Stand lässt sich nur noch löschen. Wird ein gewöhnliches Netz zum Kanalnetz, liegen seine Felder danach auf
    /// Ruhe; wird die Ch.-Liste kürzer oder entfällt, räumt <see cref="KanaeleRaeumen"/> die weggefallenen Kanal-IDs. Ohne <c>herkunft</c>
    /// (seit 0.8.0) bleibt die bisherige: Ein Client 0.7.0 kennt das Feld nicht und soll sie beim Ändern nicht löschen.
    /// </summary>
    internal static Ergebnis NetzSetzenAus(Arbeitsstand w, uint anfragender, Netz netz, long version)
    {
        ArgumentNullException.ThrowIfNull(netz);
        var code = VerwaltungPruefen(w, anfragender, version);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        if (netz.Id is 0 or 0xFFFF || string.IsNullOrWhiteSpace(netz.Name) || Namen.Skalarwerte(netz.Name) > Fristen.NameMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (Kanalrechnung.IstKanalbereich(netz.Id)) return Ergebnis.Mit(Fehlercode.Ungueltig, "IDs ab 32768 sind den Ch. der Kanalnetze vorbehalten.");
        // Global bleibt Netz 1 vom Typ GLOBAL; ein zweites GLOBAL gibt es nicht (6.5).
        if ((netz.Id == Netz.GlobalId) != (netz.Typ == NetzTyp.Global)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (netz.Id == Netz.GlobalId && netz.Standard) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (!w.Netze.ContainsKey(netz.Id) && w.Netze.Count > Fristen.NetzeMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (netz.SchiffId != 0 && !w.Schiffe.ContainsKey(netz.SchiffId)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (netz.Schiffsnetz is not null && !Feldregeln.KennungGueltig(netz.Schiffsnetz)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (!HerkunftGueltig(netz.Herkunft)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (Kanaele.NetzPruefen(netz, w.Netze) is { } problem) return Ergebnis.Mit(Fehlercode.Ungueltig, problem);
        // Ohne Ch. darf Global in keinem Rollen- oder Phasenfeld stehen (6.5); diese Felder entfernt der Admin vorher selbst.
        if (netz.Id == Netz.GlobalId && netz.Kanaele.IsEmpty
            && (w.Rollen.Values.Any(r => r.Felder.Any(f => f.NetzId == Netz.GlobalId)) || w.Phasen.Values.Any(p => p.Varianten.Any(v => v.Felder.Any(f => f.NetzId == Netz.GlobalId)))))
            return Ergebnis.Mit(Fehlercode.Ungueltig, "Rollen oder Phasen nennen Global-Kanäle; erst diese Felder entfernen.");
        var vorher = w.Netze.GetValueOrDefault(netz.Id);
        if (netz.Herkunft is null && vorher?.Herkunft is { } herkunft) netz = netz with { Herkunft = herkunft };
        w.Netze = w.Netze.SetItem(netz.Id, netz);
        var betroffen = new List<uint>();
        var sperren = new List<KanalsperrFolge>();
        if (vorher is { Kanaele.IsEmpty: true } && !netz.Kanaele.IsEmpty) betroffen.AddRange(ZuKanalnetz(w, anfragender, netz.Id));
        if (vorher is not null && netz.Kanaele.Length < vorher.Kanaele.Length) betroffen.AddRange(KanaeleRaeumen(w, anfragender, netz.Id, netz.Kanaele.Length, sperren));
        return NetzListeNeu(w, betroffen, sperren);
    }

    /// <summary>
    /// Ein gewöhnliches Netz wird Kanalnetz (6.16): Seine Netz-ID steht danach in keinem Feld mehr; die Felder liegen auf seiner Ruhe, mit
    /// hören und senden wie vorher. Einheitskennungen auf das Netz fallen weg, denn eine Einheit nennt einen Kanal, kein Kanalnetz.
    /// </summary>
    private static List<uint> ZuKanalnetz(Arbeitsstand w, uint durch, ushort n)
    {
        var ruhe = Kanalrechnung.Id(n, Kanalrechnung.Ruhe);
        foreach (var s in w.Schiffe.Values.ToList())
        {
            if (s.Netze.ContainsValue(n))
                w.Schiffe = w.Schiffe.SetItem(s.Id, s with { Netze = s.Netze.RemoveRange(s.Netze.Where(p => p.Value == n).Select(p => p.Key)) });
        }
        var betroffen = new List<uint>();
        foreach (var k in w.Konten.Values.ToList())
        {
            if (k.FeldMitNetz(n) is not { } f) continue;
            var felder = k.Felder.Replace(f, f with { NetzId = ruhe });
            w.KontoSetzen(k with { Felder = felder, Sendeauswahl = Feldregeln.AuswahlBereinigen(w.Netze, k.Sendeauswahl, felder, w.Wirksam(k)) }, umstellungDurch: durch);
            betroffen.Add(k.Id);
        }
        return betroffen;
    }

    /// <summary>
    /// Räumt die Kanal-IDs des Kanalnetzes <paramref name="n"/> ab Ch. <paramref name="ab"/> (0 = alle, auch die Ruhe) aus Feldern,
    /// Stummschaltungen und Kanalsperren (6.17, Folge in <paramref name="sperren"/>) der Konten und aus den Einheitskennungen; ein fester Ch.
    /// ab dort in Rollen und Phasen verfällt, der Ch. wird dann aufgelöst (6.16). Gespeicherte Aufstellungen, Sätze und der letzte Stand
    /// behalten ihre IDs und scheitern beim Anwenden wie ein gelöschtes Netz.
    /// </summary>
    private static List<uint> KanaeleRaeumen(Arbeitsstand w, uint durch, ushort n, int ab, List<KanalsperrFolge> sperren)
    {
        bool Weg(ushort id) => Kanalrechnung.Zerlegen(id, out var kn, out var c) && kn == n && c >= ab;
        RollenFeld Ohne(RollenFeld f) => f.NetzId == n && f.Ch >= ab ? f with { Ch = null } : f;
        foreach (var r in w.Rollen.Values.ToList())
        {
            if (r.Felder.Any(f => f.NetzId == n && f.Ch >= ab)) w.Rollen = w.Rollen.SetItem(r.Id, r with { Felder = [.. r.Felder.Select(Ohne)] });
        }
        foreach (var p in w.Phasen.Values.ToList())
        {
            if (p.Varianten.Any(v => v.Felder.Any(f => f.NetzId == n && f.Ch >= ab)))
                w.Phasen = w.Phasen.SetItem(p.Nr, p with { Varianten = [.. p.Varianten.Select(v => v with { Felder = [.. v.Felder.Select(Ohne)] })] });
        }
        foreach (var s in w.Schiffe.Values.ToList())
        {
            if (s.Netze.Values.Any(Weg)) w.Schiffe = w.Schiffe.SetItem(s.Id, s with { Netze = s.Netze.RemoveRange(s.Netze.Where(p => Weg(p.Value)).Select(p => p.Key)) });
        }
        var betroffen = new List<uint>();
        foreach (var k in w.Konten.Values.ToList())
        {
            if (!k.Felder.Any(f => Weg(f.NetzId)) && !k.Stumm.Any(s => Weg(s.NetzId))) continue;
            ImmutableArray<Mitgliedschaft> felder = [.. k.Felder.Where(f => !Weg(f.NetzId))];
            w.KontoSetzen(k with
            {
                Felder = felder,
                Stumm = [.. k.Stumm.Where(s => !Weg(s.NetzId))],
                Sendeauswahl = Feldregeln.AuswahlBereinigen(w.Netze, k.Sendeauswahl, felder, w.Wirksam(k)),
            }, umstellungDurch: durch);
            betroffen.Add(k.Id);
        }
        SperrenAufNetz(w, Weg, sperren, betroffen);
        return betroffen;
    }

    /// <summary>
    /// NETZ_LOESCHEN: Mitgliedschaften, Stummschaltungen, Kanalsperren, Rollenfelder und Schiffskennungen darauf fallen weg; bei einem
    /// Kanalnetz auch alle seine Kanal-IDs (6.16, 6.17).
    /// </summary>
    internal static Ergebnis NetzLoeschenAus(Arbeitsstand w, uint anfragender, ushort netzId, long version)
    {
        var code = VerwaltungPruefen(w, anfragender, version);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        if (netzId == Netz.GlobalId) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (!w.Netze.TryGetValue(netzId, out var geloescht)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        var sperren = new List<KanalsperrFolge>();
        var kanalBetroffen = geloescht.Kanaele.IsEmpty ? [] : KanaeleRaeumen(w, anfragender, netzId, 0, sperren);
        SperrenAufNetz(w, id => id == netzId, sperren, kanalBetroffen);
        w.Netze = w.Netze.Remove(netzId);

        foreach (var r in w.Rollen.Values.ToList())
        {
            if (r.Felder.Any(f => f.NetzId == netzId))
                w.Rollen = w.Rollen.SetItem(r.Id, r with { Felder = [.. r.Felder.Where(f => f.NetzId != netzId)] });
        }
        foreach (var s in w.Schiffe.Values.ToList())
        {
            if (s.Netze.ContainsValue(netzId))
                w.Schiffe = w.Schiffe.SetItem(s.Id, s with { Netze = s.Netze.RemoveRange(s.Netze.Where(p => p.Value == netzId).Select(p => p.Key)) });
        }
        // Wie bei den Rollenfeldern fällt das Netz aus den Varianten der Phasen (6.14). Eine Variante, der kein Feld bleibt, fällt ganz
        // weg: Der Draht kennt keine leere Variante (phasen[].felder), PhasePruefen lehnt sie ab, und beim nächsten Start lehnte der
        // SchnappschussBauer den ganzen Stand ab (Gegenprüfung S2). Die Rolle steht in der Phase dann wie in der Grundstellung.
        var laufend = w.Neben.LaufendeNr();
        var geleert = ImmutableArray.CreateBuilder<PhasenVariante>();
        foreach (var p in w.Phasen.Values.ToList())
        {
            if (!p.Varianten.Any(v => v.Felder.Any(f => f.NetzId == netzId))) continue;
            var varianten = p.Varianten.Select(v => v with { Felder = [.. v.Felder.Where(f => f.NetzId != netzId)] }).ToList();
            if (p.Nr == laufend) geleert.AddRange(varianten.Where(v => v.Felder.IsEmpty));
            w.Phasen = w.Phasen.SetItem(p.Nr, p with { Varianten = [.. varianten.Where(v => !v.Felder.IsEmpty)] });
        }

        var betroffen = kanalBetroffen;
        foreach (var k in w.Konten.Values.ToList())
        {
            var hatFeld = k.FeldMitNetz(netzId) is not null;
            var hatStumm = k.Stumm.Any(s => s.NetzId == netzId);
            if (!hatFeld && !hatStumm) continue;
            ImmutableArray<Mitgliedschaft> felder = [.. k.Felder.Where(f => f.NetzId != netzId)];
            w.KontoSetzen(k with
            {
                Felder = felder,
                Stumm = [.. k.Stumm.Where(s => s.NetzId != netzId)],
                Sendeauswahl = Feldregeln.AuswahlBereinigen(w.Netze, k.Sendeauswahl, felder, w.Wirksam(k)),
            }, umstellungDurch: anfragender);
            if (!betroffen.Contains(k.Id)) betroffen.Add(k.Id);
        }
        // Läuft die Phase, deren Variante wegfiel, bekommt die Rolle gleich die Felder der Grundstellung, wie beim Ändern der laufenden
        // Phase (PhaseSetzenAus): von der leeren Variante auf keine.
        if (geleert.Count > 0)
        {
            var (umgestellt, _) = Umstellen(w, new Einsatzphase(laufend, "", "") { Varianten = geleert.ToImmutable() }, new Einsatzphase(laufend, "", ""),
                w.Konto(anfragender)?.Stufe ?? Stufe.Admin, Urheber(anfragender));
            betroffen.AddRange(umgestellt.Where(id => !betroffen.Contains(id)));
        }
        return NetzListeNeu(w, betroffen, sperren);
    }

    internal static Ergebnis SchiffSetzenAus(Arbeitsstand w, uint anfragender, Schiff schiff, long version)
    {
        ArgumentNullException.ThrowIfNull(schiff);
        var code = VerwaltungPruefen(w, anfragender, version);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        if (string.IsNullOrWhiteSpace(schiff.Name) || Namen.Skalarwerte(schiff.Name) > Fristen.NameMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (!w.Schiffe.ContainsKey(schiff.Id) && w.Schiffe.Count >= Fristen.SchiffeMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (Feldregeln.SchiffPruefen(schiff, w.Netze, w.Rollen) is not null) return Ergebnis.Mit(Fehlercode.Ungueltig);
        w.Schiffe = w.Schiffe.SetItem(schiff.Id, schiff);
        // Plätze, die es auf dem Schiff nicht mehr gibt, fallen weg; die Felder bleiben.
        var betroffen = PlaetzeLoesen(w, anfragender, k => k.Platz is { } p && p.SchiffId == schiff.Id && schiff.Kapazitaet(p.RolleId) is null);
        return NetzListeNeu(w, betroffen);
    }

    internal static Ergebnis SchiffLoeschenAus(Arbeitsstand w, uint anfragender, ushort schiffId, long version)
    {
        var code = VerwaltungPruefen(w, anfragender, version);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        if (!w.Schiffe.ContainsKey(schiffId)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        w.Schiffe = w.Schiffe.Remove(schiffId);
        foreach (var n in w.Netze.Values.Where(n => n.SchiffId == schiffId).ToList())
            w.Netze = w.Netze.SetItem(n.Id, n with { SchiffId = 0, Schiffsnetz = null });
        var betroffen = PlaetzeLoesen(w, anfragender, k => k.Platz is { } p && p.SchiffId == schiffId);
        return NetzListeNeu(w, betroffen);
    }

    /// <summary>ROLLE_SETZEN (4.5); ohne <c>herkunft</c> (seit 0.8.0) bleibt die bisherige, wie bei <see cref="NetzSetzenAus"/>.</summary>
    internal static Ergebnis RolleSetzenAus(Arbeitsstand w, uint anfragender, Rolle rolle, long version)
    {
        ArgumentNullException.ThrowIfNull(rolle);
        var code = VerwaltungPruefen(w, anfragender, version);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        if (string.IsNullOrWhiteSpace(rolle.Name) || Namen.Skalarwerte(rolle.Name) > Fristen.RolleNameMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (!HerkunftGueltig(rolle.Herkunft)) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (!w.Rollen.ContainsKey(rolle.Id) && w.Rollen.Count >= Fristen.RollenMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (Feldregeln.RollePruefen(rolle, w.Netze) is not null) return Ergebnis.Mit(Fehlercode.Ungueltig);
        if (rolle.Herkunft is null && w.Rollen.GetValueOrDefault(rolle.Id)?.Herkunft is { } herkunft) rolle = rolle with { Herkunft = herkunft };
        w.Rollen = w.Rollen.SetItem(rolle.Id, rolle);
        var betroffen = PlaetzeLoesen(w, anfragender, k => k.Platz is { SchiffId: 0 } p && p.RolleId == rolle.Id && rolle.BrauchtSchiff);
        return NetzListeNeu(w, betroffen);
    }

    internal static Ergebnis RolleLoeschenAus(Arbeitsstand w, uint anfragender, ushort rolleId, long version)
    {
        var code = VerwaltungPruefen(w, anfragender, version);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        if (!w.Rollen.ContainsKey(rolleId)) return Ergebnis.Mit(Fehlercode.Unbekannt);
        w.Rollen = w.Rollen.Remove(rolleId);
        foreach (var s in w.Schiffe.Values.ToList())
        {
            if (s.Kapazitaet(rolleId) is not null)
                w.Schiffe = w.Schiffe.SetItem(s.Id, s with { Plaetze = [.. s.Plaetze.Where(p => p.RolleId != rolleId)] });
        }
        // Was zu einer gelöschten Rolle gehört, fällt mit ihr weg, auch ihre Varianten in den Phasen (6.14).
        foreach (var p in w.Phasen.Values.ToList())
        {
            if (p.VarianteFuer(rolleId) is not null)
                w.Phasen = w.Phasen.SetItem(p.Nr, p with { Varianten = [.. p.Varianten.Where(v => v.RolleId != rolleId)] });
        }
        var betroffen = PlaetzeLoesen(w, anfragender, k => k.Platz is { } p && p.RolleId == rolleId);
        // Die gewählte Rolle entfällt mit ihr (6.16); die Felder bleiben, wie sie sind.
        foreach (var k in w.Konten.Values.Where(k => k.GewaehlteRolle == rolleId).ToList())
        {
            w.KontoSetzen(k with { GewaehlteRolle = null });
            if (!betroffen.Contains(k.Id)) betroffen.Add(k.Id);
        }
        return NetzListeNeu(w, betroffen);
    }

    /// <summary>Herkunft (seit 0.8.0): fehlt oder ist ein Name bis 32 Zeichen, nicht nur Leerzeichen.</summary>
    private static bool HerkunftGueltig(string? herkunft) =>
        herkunft is null || (!string.IsNullOrWhiteSpace(herkunft) && Namen.Skalarwerte(herkunft) <= Fristen.HerkunftMax);

    private static List<uint> PlaetzeLoesen(Arbeitsstand w, uint durch, Func<Konto, bool> trifft)
    {
        var betroffen = new List<uint>();
        foreach (var k in w.Konten.Values.Where(trifft).ToList())
        {
            w.KontoSetzen(k with { Platz = null }, umstellungDurch: durch);
            betroffen.Add(k.Id);
        }
        return betroffen;
    }

    /// <summary>
    /// SELBSTWAHL (auch mit ZUWEISEN) und EINSTELLUNG <c>durchsageNurTaste</c>. Eine Sperre der Selbstwahl trägt ihren
    /// Urheber (die Konsole als 0) und endet nach 12 h oder mit dessen Auftrag; <paramref name="nachzuegler"/> false sperrt
    /// auch Konten ohne Platz ("Ganz sperren", F9).
    /// </summary>
    internal static Ergebnis EinstellungAus(Arbeitsstand w, uint anfragender, long version, bool? selbstwahl, bool? durchsageNurTaste, bool? nachzuegler = null)
    {
        var code = VerwaltungPruefen(w, anfragender, version, selbstwahl is null ? Recht.Keine : Recht.Zuweisen);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        // Sperren nur mit wirksamem ZUWEISEN (die Konsole immer): Eine Sperre wirkt nur, solange ihr Urheber ZUWEISEN hat (F9); ein
        // ADMIN im Funker-Modus (6.15) oder ein Konto mit NETZE_VERWALTEN allein setzte sonst eine, die der Takt gleich wieder aufhebt.
        if (selbstwahl == false && anfragender != InterneAuftraege.AdminWerkzeug && !w.RechteVon(anfragender).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var e = w.Einstellungen with { DurchsageNurTaste = durchsageNurTaste ?? w.Einstellungen.DurchsageNurTaste };
        if (selbstwahl is { } an)
        {
            e = an
                ? e with { Selbstwahl = true, SelbstwahlDurch = 0, SelbstwahlBis = 0, Nachzuegler = true }
                : e with
                {
                    Selbstwahl = false,
                    SelbstwahlDurch = Urheber(anfragender),
                    SelbstwahlBis = w.Jetzt + Fristen.SelbstwahlSperreMs,
                    Nachzuegler = nachzuegler ?? true,
                };
        }
        w.Einstellungen = e;
        return NetzListeNeu(w);
    }
}

/// <summary>Anzeigenamen serverweit eindeutig nach NFKC und <c>ToUpperInvariant</c> (3.3).</summary>
internal static class Namen
{
    public static string Schluessel(string name)
    {
        string normal;
        try
        {
            normal = name.Normalize(NormalizationForm.FormKC);
        }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException)
        {
            // Ungültiges UTF-16 oder keine Normalisierung im Invariant-Modus: dann gilt der Name wie er ist.
            normal = name;
        }
        return normal.ToUpperInvariant();
    }

    /// <summary>
    /// Hängt bei einer Kollision " (2)", " (3)" usw. an. Der Name wird dafür so gekürzt, dass er höchstens
    /// <see cref="Fristen.NameMax"/> Skalarwerte behält (3.3, 4.10).
    /// </summary>
    public static string Eindeutig(string name, IEnumerable<Konto> konten, uint eigeneId)
    {
        var belegt = konten.Where(k => k.Id != eigeneId).Select(k => Schluessel(k.Name)).ToHashSet(StringComparer.Ordinal);
        if (!belegt.Contains(Schluessel(name))) return name;
        for (var n = 2; ; n++)
        {
            var zusatz = string.Create(CultureInfo.InvariantCulture, $" ({n})");
            var versuch = Kuerzen(name, Fristen.NameMax - zusatz.Length) + zusatz;
            if (!belegt.Contains(Schluessel(versuch))) return versuch;
        }
    }

    /// <summary>Länge in Unicode-Skalarwerten (4.3); ungültige Ersatzzeichen zählen je als einer.</summary>
    public static int Skalarwerte(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var n = 0;
        foreach (var _ in text.EnumerateRunes()) n++;
        return n;
    }

    /// <summary>Kürzt auf höchstens <paramref name="max"/> Skalarwerte, ohne ein Ersatzpaar zu trennen.</summary>
    private static string Kuerzen(string text, int max)
    {
        var n = 0;
        var laenge = 0;
        foreach (var r in text.EnumerateRunes())
        {
            if (n == max) return text[..laenge];
            n++;
            laenge += r.Utf16SequenceLength;
        }
        return text;
    }
}
