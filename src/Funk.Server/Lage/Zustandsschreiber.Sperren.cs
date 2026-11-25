// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Server.Lage;

/// <summary>
/// Kanalsperren (6.17, seit 0.6.0): setzen und aufheben (<c>ZUWEISEN</c>, Rangregel wie Stummschalten), der Fristen-Takt (Ablauf bis 04:00)
/// und das Ende, wenn der Setzende kein <c>ZUWEISEN</c> mehr hat (Regel 7 aus 6.12, wie die Sperre der Selbstwahl). Gesperrt heißt: nicht
/// hören, nicht senden, nicht in ein Feld legen, kein Ch.-Wechsel in einem gesperrten Feld; das Feld bleibt stehen. Das setzen Weiterleitung
/// (<see cref="Schnappschuss.GesperrtAn"/>), FeldSetzen, SatzAnwenden und die Sendeauswahl durch (<see cref="Arbeitsstand.OhneGesperrte"/>).
/// </summary>
public sealed partial class Zustandsschreiber
{
    /// <summary>
    /// KanalSperre SETZEN (6.17): <c>ZUWEISEN</c>; ein ADMIN-Konto sperrt nur ein ADMIN, auch im Funker-Modus. Eine Sperre für alle
    /// (<paramref name="kontoId"/> 0), die ein ADMIN setzt, trifft auch ADMIN-Konten ohne <c>ZUWEISEN</c>. Eine laufende Sperre auf dasselbe
    /// Netz ersetzt nur, wer sie auch aufheben dürfte. Grenzen: 16 je Konto, 64 für alle, 500 zusammen.
    /// </summary>
    internal static Ergebnis KanalSperreSetzenAus(Arbeitsstand w, uint anfragender, uint kontoId, ushort netzId, long bis)
    {
        if (w.Konto(anfragender) is not { } anf) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!w.Wirksam(anf).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var ziel = Sperrregeln.ZielPruefen(w.Netze, netzId);
        if (ziel == Fehlercode.Ungueltig) return Ergebnis.Mit(ziel, "Global (auch dessen Ch. 1) und eine Ruhe lassen sich nicht sperren; für Global bleibt Stummschalten.");
        if (ziel != Fehlercode.Keiner) return Ergebnis.Mit(ziel);
        var jetzt = w.Jetzt;
        if (bis <= jetzt || bis - jetzt > Kanalrechnung.SperreMaxMs) return Ergebnis.Mit(Fehlercode.Ungueltig, "Eine Kanalsperre endet nach jetzt und höchstens 24 Stunden später.");
        // Das Admin-Werkzeug (Konsole) hat kein Konto; als Urheber steht dann 0 da, wie bei einem Auftrag von der Konsole.
        var durch = DurchVon(anfragender);
        var neu = new Kanalsperre(netzId, bis, durch) { Seit = jetzt };
        var gesamt = SperrenGesamt(w);

        if (kontoId == 0)
        {
            neu = neu with { AuchAdmins = anf.Stufe == Stufe.Admin };
            var vorher = Laufend(w.SperrenAlle, netzId, jetzt);
            if (vorher is { } v && !DarfSperreAufheben(w, anf, null, v)) return Ergebnis.Mit(Fehlercode.KeinRecht);
            var liste = w.SperrenAlle.Where(s => s.NetzId != netzId).ToList();
            if (liste.Count >= Kanalrechnung.SperrenAlleMax) return Ergebnis.Mit(Fehlercode.Ungueltig, "Höchstens 64 Kanalsperren für alle.");
            if (gesamt - (w.SperrenAlle.Length - liste.Count) >= Kanalrechnung.SperrenMax) return Ergebnis.Mit(Fehlercode.Ungueltig, "Höchstens 500 Kanalsperren.");
            liste.Add(neu);
            w.SperrenAlle = [.. liste.OrderBy(s => s.NetzId)];
            w.SchnappschussGeaendert = true;
            // Wessen Sendeauswahl sich ändert, dessen Details ändern sich (Praesenz an die Einsatzleitung); alle anderen Getroffenen
            // bekommen nur Mitgliedschaften mit der Sperre, denn an ihrem Praesenz-Eintrag ändert sie nichts (Gegenprüfung 3.7).
            var ausgewaehlt = new List<uint>();
            var nurListe = new List<uint>();
            foreach (var k in w.Konten.Values.ToList())
            {
                if (!Sperrregeln.AlleTreffen(k, w.Wirksam(k), neu)) continue;
                var geaendert = AuswahlOhneGesperrte(w, k);
                if (!w.Online(k.Id)) continue;
                (geaendert ? ausgewaehlt : nurListe).Add(k.Id);
            }
            return new Ergebnis
            {
                BetroffeneKonten = [.. ausgewaehlt],
                NurDetails = true,
                NurMitgliedschaften = [.. nurListe],
                Kanalsperren = [new KanalsperrFolge(0, neu, KanalsperrAnlass.Gesetzt, durch)],
            };
        }

        if (w.Konto(kontoId) is not { } konto) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!Rechteregeln.DarfStummschalten(anf.Stufe, konto.Stufe)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var alt = Laufend(konto.Sperren, netzId, jetzt);
        if (alt is { } a && !DarfSperreAufheben(w, anf, konto, a)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var eigene = konto.Sperren.Where(s => s.NetzId != netzId).ToList();
        if (eigene.Count >= Kanalrechnung.SperrenJeKontoMax) return Ergebnis.Mit(Fehlercode.Ungueltig, "Höchstens 16 Kanalsperren je Konto.");
        if (gesamt - (konto.Sperren.Length - eigene.Count) >= Kanalrechnung.SperrenMax) return Ergebnis.Mit(Fehlercode.Ungueltig, "Höchstens 500 Kanalsperren.");
        eigene.Add(neu);
        w.KontoSetzen(konto with { Sperren = [.. eigene.OrderBy(s => s.NetzId)] });
        AuswahlOhneGesperrte(w, w.Konto(kontoId)!);
        // Die eigene Sperre steht nur in den Details (Praesenz an die Einsatzleitung).
        return new Ergebnis { BetroffeneKonten = [kontoId], NurDetails = true, Kanalsperren = [new KanalsperrFolge(kontoId, neu, KanalsperrAnlass.Gesetzt, durch)] };
    }

    /// <summary>
    /// KanalSperre AUFHEBEN (6.17): mit <c>ZUWEISEN</c> und wie bei einer Stummschaltung (6.10): wer sie gesetzt hat, ein Konto höherer Stufe
    /// als der Setzende oder ein ADMIN; der Gesperrte selbst nie eine fremde. Eine abgelaufene gilt als aufgehoben (UNBEKANNT).
    /// </summary>
    internal static Ergebnis KanalSperreAufhebenAus(Arbeitsstand w, uint anfragender, uint kontoId, ushort netzId)
    {
        if (w.Konto(anfragender) is not { } anf) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!w.Wirksam(anf).Hat(Recht.Zuweisen)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        var jetzt = w.Jetzt;
        if (kontoId == 0)
        {
            if (Laufend(w.SperrenAlle, netzId, jetzt) is not { } s) return Ergebnis.Mit(Fehlercode.Unbekannt);
            if (!DarfSperreAufheben(w, anf, null, s)) return Ergebnis.Mit(Fehlercode.KeinRecht);
            var betroffen = OnlineGetroffen(w, s);
            w.SperrenAlle = [.. w.SperrenAlle.Where(x => x.NetzId != netzId)];
            w.SchnappschussGeaendert = true;
            // Nur Mitgliedschaften: Aufheben ändert an keinem Praesenz-Eintrag etwas (Gegenprüfung 3.7).
            return new Ergebnis { NurMitgliedschaften = [.. betroffen], Kanalsperren = [new KanalsperrFolge(0, s, KanalsperrAnlass.Aufgehoben, DurchVon(anfragender))] };
        }
        if (w.Konto(kontoId) is not { } konto) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (Laufend(konto.Sperren, netzId, jetzt) is not { } eigene) return Ergebnis.Mit(Fehlercode.Unbekannt);
        if (!DarfSperreAufheben(w, anf, konto, eigene)) return Ergebnis.Mit(Fehlercode.KeinRecht);
        w.KontoSetzen(konto with { Sperren = [.. konto.Sperren.Where(x => x.NetzId != netzId)] });
        return new Ergebnis { BetroffeneKonten = [kontoId], NurDetails = true, Kanalsperren = [new KanalsperrFolge(kontoId, eigene, KanalsperrAnlass.Aufgehoben, DurchVon(anfragender))] };
    }

    /// <summary>
    /// Fristen-Takt der Kanalsperren: abgelaufene (ABGELAUFEN) und die, deren Setzender kein wirksames <c>ZUWEISEN</c> mehr hat (BEENDET,
    /// AUFTRAG; etwa ein abgelaufener Auftrag oder ein gelöschtes Konto), fallen weg. Wirkung hatten sie schon vorher keine mehr (Ablauf) bzw.
    /// enden hier spätestens eine Sekunde nach dem Auftrag.
    /// </summary>
    private static void SperrenFristen(Arbeitsstand w, Folgen folgen)
    {
        var jetzt = w.Jetzt;
        SperrenEntfernen(w, s => s.Bis <= jetzt ? (KanalsperrAnlass.Abgelaufen, null)
            : !SetzerWirkt(w, s) ? (KanalsperrAnlass.Beendet, "AUFTRAG")
            : null, folgen.Sperren, folgen.Betroffen);
    }

    /// <summary>Regel 7 aus 6.12 für Kanalsperren: Hat das Konto kein <c>ZUWEISEN</c> mehr, enden die Sperren, die es gesetzt hat.</summary>
    private static void SperrenDesSetzenden(Arbeitsstand w, uint setzender, Folgen folgen) =>
        SperrenEntfernen(w, s => s.Durch == setzender ? (KanalsperrAnlass.Beendet, "AUFTRAG") : null, folgen.Sperren, folgen.Betroffen);

    /// <summary>Kanal oder Netz fällt weg (NETZ_LOESCHEN, kürzere Ch.-Liste): Sperren darauf enden (BEENDET, NETZ).</summary>
    private static void SperrenAufNetz(Arbeitsstand w, Func<ushort, bool> weg, List<KanalsperrFolge> folgen, ICollection<uint> betroffen) =>
        SperrenEntfernen(w, s => weg(s.NetzId) ? (KanalsperrAnlass.Beendet, "NETZ") : null, folgen, betroffen);

    /// <summary>
    /// Entfernt jede Sperre, für die <paramref name="warum"/> einen Anlass liefert, aus den Konten und aus den Sperren für alle; die Konten,
    /// die eine Sperre traf und online sind, bekommen <c>Mitgliedschaften</c>.
    /// </summary>
    private static void SperrenEntfernen(Arbeitsstand w, Func<Kanalsperre, (KanalsperrAnlass Anlass, string? Grund)?> warum,
        List<KanalsperrFolge> folgen, ICollection<uint> betroffen)
    {
        foreach (var k in w.Konten.Values.Where(k => !k.Sperren.IsEmpty).ToList())
        {
            var bleibt = new List<Kanalsperre>(k.Sperren.Length);
            foreach (var s in k.Sperren)
            {
                if (warum(s) is { } a) folgen.Add(new KanalsperrFolge(k.Id, s, a.Anlass, 0, a.Grund));
                else bleibt.Add(s);
            }
            if (bleibt.Count == k.Sperren.Length) continue;
            w.KontoSetzen(k with { Sperren = [.. bleibt] });
            if (!betroffen.Contains(k.Id)) betroffen.Add(k.Id);
        }
        if (w.SperrenAlle.IsEmpty) return;
        var alle = new List<Kanalsperre>(w.SperrenAlle.Length);
        foreach (var s in w.SperrenAlle)
        {
            if (warum(s) is not { } a)
            {
                alle.Add(s);
                continue;
            }
            folgen.Add(new KanalsperrFolge(0, s, a.Anlass, 0, a.Grund));
            foreach (var id in OnlineGetroffen(w, s))
                if (!betroffen.Contains(id)) betroffen.Add(id);
        }
        if (alle.Count == w.SperrenAlle.Length) return;
        w.SperrenAlle = [.. alle];
        w.SchnappschussGeaendert = true;
    }

    /// <summary>Ob der Setzende einer Sperre noch <c>ZUWEISEN</c> hat (die Konsole, Urheber 0, immer).</summary>
    internal static bool SetzerWirkt(Arbeitsstand w, Kanalsperre sperre) => sperre.Durch == 0 || w.RechteVon(sperre.Durch).Hat(Recht.Zuweisen);

    /// <summary>Der Urheber einer Sperre im Zustand: Das Admin-Werkzeug (Konsole) hat kein Konto und steht als 0 da; dann endet die Sperre nur durch Ablauf, Aufheben oder wenn der Kanal wegfällt (<see cref="SetzerWirkt"/>).</summary>
    private static uint DurchVon(uint anfragender) => anfragender == InterneAuftraege.AdminWerkzeug ? 0u : anfragender;

    /// <summary>
    /// Aufheben wie eine Stummschaltung (6.10): wer sie gesetzt hat, ein ADMIN oder ein Konto höherer Stufe als der Setzende; ein gelöschter
    /// Setzender zählt wie ein ADMIN. Der Gesperrte (<paramref name="ziel"/>) hebt eine fremde Sperre nie selbst auf.
    /// </summary>
    private static bool DarfSperreAufheben(Arbeitsstand w, Konto anf, Konto? ziel, Kanalsperre sperre)
    {
        if (sperre.Durch == anf.Id) return true;
        if (ziel is not null && ziel.Id == anf.Id) return false;
        if (anf.Stufe == Stufe.Admin) return true;
        var setzerStufe = w.Konto(sperre.Durch)?.Stufe ?? Stufe.Admin;
        return anf.Stufe > setzerStufe;
    }

    /// <summary>Die laufende Sperre auf <paramref name="netzId"/> in der Liste, sonst <c>null</c>.</summary>
    private static Kanalsperre? Laufend(ImmutableArray<Kanalsperre> sperren, ushort netzId, long jetzt)
    {
        foreach (var s in sperren)
            if (s.NetzId == netzId && s.Bis > jetzt) return s;
        return null;
    }

    private static int SperrenGesamt(Arbeitsstand w) => w.SperrenAlle.Length + w.Konten.Values.Sum(k => k.Sperren.Length);

    /// <summary>Konten mit Sitzung, die eine Sperre für alle gerade trifft.</summary>
    private static List<uint> OnlineGetroffen(Arbeitsstand w, Kanalsperre sperre) =>
        [.. w.Konten.Values.Where(k => w.Online(k.Id) && Sperrregeln.AlleTreffen(k, w.Wirksam(k), sperre)).Select(k => k.Id)];

    /// <summary>
    /// Nach einer neuen Sperre: die Sendeauswahl, die der Client gerade nutzt (Komfortzustand), ohne gesperrte Felder. Ändert sie sich, gilt
    /// sie am Konto, und der Komfortzustand fällt weg; so sieht der Client die Umstellung (C4). <c>true</c>, wenn sie sich geändert hat.
    /// </summary>
    private static bool AuswahlOhneGesperrte(Arbeitsstand w, Konto k)
    {
        var tatsaechlich = w.AuswahlTatsaechlich(k);
        var ohne = w.OhneGesperrte(k, tatsaechlich);
        if (ohne.Equals(tatsaechlich)) return false;
        w.KontoSetzen(k with { Sendeauswahl = ohne });
        w.AuswahlUebernehmen(k.Id);
        return true;
    }
}
