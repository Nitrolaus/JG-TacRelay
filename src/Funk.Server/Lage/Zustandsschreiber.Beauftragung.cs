// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;

namespace Funk.Server.Lage;

/// <summary>
/// Beauftragung (6.12, RAIDLEITUNG-ENTWURF Regeln 1-10): Auftrag für die Einsatzleitung erteilen, entziehen, abgeben, den
/// Fristen-Takt und das Ende bei Sperre, Stufenwechsel oder wenn der Erteilende sein Recht verliert. Admin-Sitzung und
/// frische Bestätigung prüft die Zentrale vorher; hier gelten die Regeln, die sich am Lagezustand entscheiden.
/// </summary>
public sealed partial class Zustandsschreiber
{
    /// <summary>Wer einen Auftrag erteilt, entzieht oder die Selbstwahl sperrt, als Urheber: die Konsole zählt als 0.</summary>
    internal static uint Urheber(uint anfragender) => anfragender == InterneAuftraege.AdminWerkzeug ? 0 : anfragender;

    /// <summary>
    /// Prüft einen Auftrag gegen die Regeln 1, 2 und 4, ohne etwas zu ändern. Die Zentrale fragt so vor der frischen Bestätigung,
    /// damit ein ohnehin verbotener Auftrag nicht erst nach einem Code abgelehnt wird.
    /// </summary>
    public static (Fehlercode Code, string? Text, Beauftragung? Auftrag) BeauftragungBilden(IReadOnlyDictionary<uint, Konto> konten,
        uint anfragender, uint kontoId, long? von, long bis, Recht? rechte, string? grund, long jetzt)
    {
        ArgumentNullException.ThrowIfNull(konten);
        if (!konten.TryGetValue(anfragender, out var anf)) return (Fehlercode.Unbekannt, null, null);
        // Regel 1: nur mit KONTEN_VERWALTEN, dauerhaft (ein Auftrag trägt es nie).
        if (!anf.Rechte.Hat(Recht.KontenVerwalten)) return (Fehlercode.KeinRecht, null, null);
        if (anfragender == kontoId) return (Fehlercode.KeinRecht, "Einen Auftrag an sich selbst gibt es nicht.", null);
        if (!konten.TryGetValue(kontoId, out var ziel)) return (Fehlercode.Unbekannt, null, null);
        if (ziel.Stufe == Stufe.Admin) return (Fehlercode.Ungueltig, "Ein ADMIN braucht keinen Auftrag.", null);
        if (ziel.Status != KontoStatus.Aktiv) return (Fehlercode.Ungueltig, "Das Konto ist gesperrt.", null);

        // Regel 2: nur Rechte der Einsatzleitung, ZUWEISEN und SPRECHANZEIGE_ALLE immer.
        var r = rechte ?? Rechteregeln.Einsatzleitung;
        if ((r & ~Rechteregeln.Einsatzleitung) != 0) return (Fehlercode.Ungueltig, "Ein Auftrag trägt nur Rechte der Einsatzleitung.", null);
        if (!r.Hat(Rechteregeln.EinsatzleitungPflicht)) return (Fehlercode.Ungueltig, "Ein Auftrag trägt immer ZUWEISEN und SPRECHANZEIGE_ALLE.", null);
        // Regel 1, zweiter Teil: jedes Recht selbst dauerhaft haben (aus Stufe oder Einzelrecht, nie aus einem Auftrag).
        if ((r & ~anf.Rechte) != 0) return (Fehlercode.KeinRecht, "Ein Recht, das du selbst nicht hast, kannst du nicht vergeben.", null);

        if (grund is not null && (string.IsNullOrWhiteSpace(grund) || Namen.Skalarwerte(grund) > Fristen.AuftragGrundMax))
            return (Fehlercode.Ungueltig, "Der Anlass hat höchstens 48 Zeichen.", null);

        // Regel 4: Beginn höchstens 14 Tage voraus, Ende höchstens 24 h nach dem Beginn.
        var beginn = Math.Max(von ?? jetzt, jetzt);
        if (beginn - jetzt > Fristen.AuftragVorausMs) return (Fehlercode.Ungueltig, "Ein Auftrag beginnt höchstens 14 Tage im Voraus.", null);
        if (bis <= beginn) return (Fehlercode.Ungueltig, "Das Ende liegt nicht nach dem Beginn.", null);
        if (bis - beginn > Fristen.AuftragDauerMs) return (Fehlercode.Ungueltig, "Ein Auftrag dauert höchstens 24 Stunden.", null);
        var andere = konten.Values.Count(k => k.Id != kontoId && k.Beauftragung is { } b && b.Bis > jetzt);
        if (andere >= Fristen.AuftraegeMax) return (Fehlercode.Ungueltig, "Es laufen schon 4 Aufträge (laufend oder geplant).", null);

        var auftrag = new Beauftragung(kontoId, beginn, bis, r, Urheber(anfragender), jetzt)
        {
            Grund = grund?.Trim(),
            Begonnen = beginn <= jetzt,
        };
        return (Fehlercode.Keiner, null, auftrag);
    }

    internal static Ergebnis BeauftragungErteilenAus(Arbeitsstand w, uint anfragender, uint kontoId, long? von, long bis, Recht? rechte, string? grund)
    {
        var (code, text, auftrag) = BeauftragungBilden(w.Konten, anfragender, kontoId, von, bis, rechte, grund, w.Jetzt);
        if (code != Fehlercode.Keiner) return text is null ? Ergebnis.Mit(code) : Ergebnis.Mit(code, text);
        var ziel = w.Konto(kontoId)!;
        var neu = ziel with { Beauftragung = auftrag };
        w.KontoSetzen(neu with { Sendeauswahl = Feldregeln.AuswahlBereinigen(w.Netze, neu.Sendeauswahl, neu.Felder, w.Wirksam(neu)) });
        return new Ergebnis
        {
            Beauftragungen = [new BeauftragungsFolge(kontoId, BeauftragungsAnlass.Erteilt, auftrag!, Urheber(anfragender))],
            BetroffeneKonten = [kontoId],
        };
    }

    internal static Ergebnis BeauftragungEntziehenAus(Arbeitsstand w, uint anfragender, uint kontoId)
    {
        if (w.Konto(anfragender) is not { } anf) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!anf.Rechte.Hat(Recht.KontenVerwalten)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        if (w.Konto(kontoId) is not { Beauftragung: not null } ziel) return Ergebnis.Mit(Fehlercode.Unbekannt);
        var folgen = new Folgen();
        AuftragBeenden(w, ziel, BeauftragungsAnlass.Entzogen, Urheber(anfragender), null, folgen);
        return folgen.Ergebnis();
    }

    internal static Ergebnis BeauftragungAbgebenAus(Arbeitsstand w, uint kontoId)
    {
        if (w.Konto(kontoId) is not { Beauftragung: not null } k) return Ergebnis.Mit(Fehlercode.Unbekannt);
        var folgen = new Folgen();
        AuftragBeenden(w, k, BeauftragungsAnlass.Abgegeben, kontoId, null, folgen);
        return folgen.Ergebnis();
    }

    /// <summary>
    /// Takt (6.12 Regel 5, F9): Aufträge nach <c>bis</c> abräumen, geplante bei <c>von</c> als begonnen melden, eine Sperre der
    /// Selbstwahl nach ihrer Frist oder ohne gültigen Urheber aufheben. Ohne Fälliges ändert er nichts.
    /// </summary>
    internal static Ergebnis FristenAus(Arbeitsstand w)
    {
        var jetzt = w.Jetzt;
        var folgen = new Folgen();
        foreach (var k in w.Konten.Values.Where(k => k.Beauftragung is not null).ToList())
        {
            var b = k.Beauftragung!;
            if (b.Bis <= jetzt)
            {
                AuftragBeenden(w, w.Konto(k.Id)!, BeauftragungsAnlass.Abgelaufen, 0, null, folgen);
            }
            else if (!b.Begonnen && b.Von <= jetzt)
            {
                var neu = k with { Beauftragung = b with { Begonnen = true } };
                w.KontoSetzen(neu);
                folgen.Liste.Add(new BeauftragungsFolge(k.Id, BeauftragungsAnlass.Begonnen, neu.Beauftragung!, 0));
                folgen.Betroffen.Add(k.Id);
            }
        }
        // Funker-Modus nach seinem Ende (6.15): Die Rechte gelten schon ab bis; hier gehen Push und Journal hinaus.
        foreach (var k in w.Konten.Values.Where(k => k.AlsFunkerBis != 0 && k.AlsFunkerBis <= jetzt).ToList())
            FunkermodusBeenden(w, k, "ABGELAUFEN", folgen);
        if (!w.Einstellungen.Selbstwahl && !SelbstwahlSperreWirkt(w))
        {
            SelbstwahlFreigeben(w);
            folgen.NetzListe = true;
        }
        PhaseRueckfall(w, folgen);
        SperrenFristen(w, folgen);
        return folgen.Ergebnis();
    }

    /// <summary>
    /// Folgen einer Kontoänderung für Aufträge (6.12 Regel 6): Der eigene endet bei Sperre, Stufenwechsel und Löschung; die
    /// erteilten enden, wenn der Erteilende gesperrt, gelöscht oder ohne <c>KONTEN_VERWALTEN</c> ist. Ebenso endet der Funker-Modus
    /// (6.15) bei Sperre und Stufenwechsel.
    /// </summary>
    internal static void AuftraegeNachKontoaenderung(Arbeitsstand w, Konto alt, Konto? neu, Folgen folgen)
    {
        ArgumentNullException.ThrowIfNull(alt);
        if (neu is not null && alt.AlsFunkerBis != 0 && w.Konto(neu.Id) is { AlsFunkerBis: not 0 } funker)
        {
            var warum = neu.Status == KontoStatus.Gesperrt && alt.Status != KontoStatus.Gesperrt ? "GESPERRT"
                : neu.Stufe != alt.Stufe ? "STUFE"
                : null;
            if (warum is not null) FunkermodusBeenden(w, funker, warum, folgen);
        }
        if (alt.Beauftragung is not null)
        {
            string? grund = neu is null ? "GELOESCHT"
                : neu.Status == KontoStatus.Gesperrt && alt.Status != KontoStatus.Gesperrt ? "GESPERRT"
                : neu.Stufe != alt.Stufe ? "STUFE"
                : null;
            if (grund is not null && neu is not null && w.Konto(neu.Id) is { Beauftragung: not null } aktuell)
                AuftragBeenden(w, aktuell, BeauftragungsAnlass.Beendet, 0, grund, folgen);
            else if (grund is not null && neu is null)
                folgen.Liste.Add(new BeauftragungsFolge(alt.Id, BeauftragungsAnlass.Beendet, alt.Beauftragung, 0, grund));
        }
        var erteilerWeg = neu is null || neu.Status != KontoStatus.Aktiv || !neu.Rechte.Hat(Recht.KontenVerwalten);
        if (!erteilerWeg) return;
        foreach (var k in w.Konten.Values.Where(k => k.Beauftragung is { } b && b.Durch == alt.Id && b.Durch != 0).ToList())
            AuftragBeenden(w, k, BeauftragungsAnlass.Beendet, 0, "ERTEILER", folgen);
    }

    /// <summary>
    /// Beendet einen Auftrag und zieht Regel 7 nach: Hat das Konto danach kein <c>ZUWEISEN</c> mehr, enden seine Sperre der
    /// Selbstwahl und das Nachziehen seiner zuletzt angewandten Aufstellung. Reservierungen und Stummschaltungen laufen weiter.
    /// </summary>
    private static void AuftragBeenden(Arbeitsstand w, Konto k, BeauftragungsAnlass anlass, uint durch, string? grund, Folgen folgen)
    {
        var b = k.Beauftragung!;
        var ohne = k with { Beauftragung = null };
        ohne = ohne with { Sendeauswahl = Feldregeln.AuswahlBereinigen(w.Netze, ohne.Sendeauswahl, ohne.Felder, w.Wirksam(ohne)) };
        w.KontoSetzen(ohne);
        folgen.Liste.Add(new BeauftragungsFolge(k.Id, anlass, b, durch, grund));
        folgen.Betroffen.Add(k.Id);
        ZuweisenVerloren(w, ohne, folgen);
    }

    /// <summary>
    /// Ob die Sperre der Selbstwahl gerade wirkt (F9): innerhalb ihrer Frist und solange ihr Urheber noch <c>ZUWEISEN</c> hat
    /// (die Konsole und Stände von vor 0.3.0 haben Urheber 0). So endet sie für jede Aktion schon vor dem Takt.
    /// </summary>
    internal static bool SelbstwahlSperreWirkt(Arbeitsstand w)
    {
        var e = w.Einstellungen;
        if (e.Selbstwahl) return false;
        if (e.SelbstwahlBis != 0 && e.SelbstwahlBis <= w.Jetzt) return false;
        return e.SelbstwahlDurch == 0 || w.RechteVon(e.SelbstwahlDurch).Hat(Recht.Zuweisen);
    }

    private static void SelbstwahlFreigeben(Arbeitsstand w)
    {
        w.Einstellungen = w.Einstellungen with { Selbstwahl = true, SelbstwahlDurch = 0, SelbstwahlBis = 0, Nachzuegler = true };
        w.NetzListeVersion = w.NeueVersion;
        w.SchnappschussGeaendert = true;
    }

    /// <summary>Sammelt die Folgen mehrerer Auftragsänderungen, Wechsel des Funker-Modus und Phasenwechsel einer Transaktion.</summary>
    internal sealed class Folgen
    {
        public List<BeauftragungsFolge> Liste { get; } = [];
        public List<FunkermodusFolge> Funker { get; } = [];
        public HashSet<uint> Betroffen { get; } = [];

        /// <summary>Kanalsperren, die enden (6.17): Ablauf, Ende mit dem Auftrag des Setzenden.</summary>
        public List<KanalsperrFolge> Sperren { get; } = [];

        public bool NetzListe { get; set; }
        public Phasenwechsel? Phase { get; set; }

        public Ergebnis Ergebnis(Ergebnis? basis = null)
        {
            var b = basis ?? new Ergebnis();
            if (Liste.Count == 0 && Funker.Count == 0 && Sperren.Count == 0 && !NetzListe && Phase is null) return b;
            return new Ergebnis
            {
                Fehler = b.Fehler,
                Fehlerliste = b.Fehlerliste,
                VeralteteKonten = b.VeralteteKonten,
                Offline = b.Offline,
                Verdraengt = b.Verdraengt,
                Zurueckgesetzt = b.Zurueckgesetzt,
                Unverknuepft = b.Unverknuepft,
                Uebersprungen = b.Uebersprungen,
                Anzahl = b.Anzahl,
                AufstellungsId = b.AufstellungsId,
                AufstellungsVersion = b.AufstellungsVersion,
                BetroffeneKonten = [.. b.BetroffeneKonten.Concat(Betroffen).Distinct()],
                Meldungen = b.Meldungen,
                BeendeteSitzungen = b.BeendeteSitzungen,
                NetzListeGeaendert = b.NetzListeGeaendert || NetzListe,
                AufstellungGeaendert = b.AufstellungGeaendert,
                GeraeteZurueckstufen = b.GeraeteZurueckstufen,
                Beauftragungen = [.. b.Beauftragungen, .. Liste],
                AufstellungDurch = b.AufstellungDurch,
                Text = b.Text,
                Ausnahme = b.Ausnahme,
                Phasenwechsel = Phase ?? b.Phasenwechsel,
                PhaseStand = b.PhaseStand,
                Funkermodi = [.. b.Funkermodi, .. Funker],
                Kanalsperren = [.. b.Kanalsperren, .. Sperren],
                Sperre = b.Sperre,
            };
        }
    }

    // ---- Verwaltungspaket (N5) ----

    internal static Ergebnis PaketAus(Arbeitsstand w, uint anfragender, IReadOnlyList<Verwaltungsschritt> schritte, long version)
    {
        ArgumentNullException.ThrowIfNull(schritte);
        var code = VerwaltungPruefen(w, anfragender, version);
        if (code != Fehlercode.Keiner) return Ergebnis.Mit(code);
        if (schritte.Count is 0 or > Fristen.PaketSchritteMax) return Ergebnis.Mit(Fehlercode.Ungueltig);
        var betroffen = new HashSet<uint>();
        var sperren = new List<KanalsperrFolge>();
        Phasenwechsel? wechsel = null;
        for (var i = 0; i < schritte.Count; i++)
        {
            // Jeder Schritt prüft wie einzeln, nur die Version gilt als schon geprüft: der Arbeitsstand ist der eigene.
            var v = w.NetzListeVersion;
            var r = schritte[i] switch
            {
                Verwaltungsschritt.NetzSetzen n => NetzSetzenAus(w, anfragender, n.Netz, v),
                Verwaltungsschritt.NetzLoeschen n => NetzLoeschenAus(w, anfragender, n.Id, v),
                Verwaltungsschritt.SchiffSetzen s => SchiffSetzenAus(w, anfragender, s.Schiff, v),
                Verwaltungsschritt.SchiffLoeschen s => SchiffLoeschenAus(w, anfragender, s.Id, v),
                Verwaltungsschritt.RolleSetzen r2 => RolleSetzenAus(w, anfragender, r2.Rolle, v),
                Verwaltungsschritt.RolleLoeschen r2 => RolleLoeschenAus(w, anfragender, r2.Id, v),
                Verwaltungsschritt.Einstellung e => EinstellungAus(w, anfragender, v, selbstwahl: null, durchsageNurTaste: e.DurchsageNurTaste),
                Verwaltungsschritt.PhaseSetzen p => PhaseSetzenAus(w, anfragender, p.Phase, v),
                Verwaltungsschritt.PhaseLoeschen p => PhaseLoeschenAus(w, anfragender, p.Nr, v),
                _ => Ergebnis.Mit(Fehlercode.Ungueltig),
            };
            if (!r.Erfolg) return Ergebnis.Mit(r.Fehler, [new Eintragsfehler(i, 0, null, r.Fehler)]);
            betroffen.UnionWith(r.BetroffeneKonten);
            sperren.AddRange(r.Kanalsperren);
            wechsel = r.Phasenwechsel ?? wechsel;
        }
        return new Ergebnis { NetzListeGeaendert = true, BetroffeneKonten = [.. betroffen.Order()], Phasenwechsel = wechsel, Kanalsperren = [.. sperren] };
    }
}
