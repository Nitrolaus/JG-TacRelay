// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Server.Kern;
using Funk.Server.Lage;
using static Funk.Tests.Kanalprobe;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Tests;

/// <summary>
/// Kanalsperren in der Lage (Fassung 0.6.0, Schritt 3.2, FUNKPROTOKOLL 6.17, P-63), zuerst als Angriffe: ein Funker oder die Führung ohne
/// Auftrag sperrt, eine Einsatzleitung sperrt einen ADMIN (auch im Funker-Modus, auch über eine Sperre für alle), abgelaufene Sperren,
/// Hören trotz Sperre, Pakete in einen gesperrten Kanal, Ch.-Wechsel in einem gesperrten Feld, Sperre und Phase, Sperre und RolleWaehlen.
/// Dazu Ziele, Grenzen, das Ende mit dem Auftrag des Setzenden, Netz löschen, Neustart und Sicherung.
/// </summary>
internal static class Stufe3KanalsperreTests
{
    private const uint A = Funkprobe.A;
    private const uint Admin = Funkprobe.Admin;

    /// <summary>Ein ADMIN mit Sitzung (für den Funker-Modus).</summary>
    private const uint Max = 1009;

    private static readonly ushort Bridge3 = K(KanalBruecke, 3);
    private static readonly ushort Bridge5 = K(KanalBruecke, 5);
    private const long Stunde = 60L * 60 * 1000;

    private static Funkprobe Aufgestellt(Func<SchnappschussBauer, SchnappschussBauer>? mehr = null)
    {
        var f = Neu(b =>
        {
            b.MitKonto(new Konto { Id = Max, Name = "Max", Stufe = Stufe.Admin }).MitSitzung(Funkprobe.S(Max), Max, Max * 10);
            return mehr?.Invoke(b) ?? b;
        });
        Ok(f, Auftrag.FeldSetzen(F, 0, Bridge3), "F Schiff 3");
        Ok(f, Auftrag.FeldSetzen(G, 0, Bridge3), "G Schiff 3");
        Ok(f, Auftrag.FeldSetzen(H, 0, Bridge5), "H Schiff 5");
        return f;
    }

    private static Ergebnis Setzen(Funkprobe f, uint wer, uint konto, ushort netz, long? bis = null) =>
        f.Tun(Auftrag.KanalSperreSetzen(wer, konto, netz, bis ?? f.Jetzt + Stunde));

    private static Ergebnis Aufheben(Funkprobe f, uint wer, uint konto, ushort netz) => f.Tun(Auftrag.KanalSperreAufheben(wer, konto, netz));

    private static string Hoert(params uint[] konten) => string.Join(" ", konten.Select(k => Funkprobe.Buchstabe(Funkprobe.S(k))).Order(StringComparer.Ordinal));

    /// <summary>Wer ein echtes Paket bekommen hat (ohne vom Server erzeugte ENDE), nach Sitzung sortiert.</summary>
    private static string Empfaenger(Funkprobe f) =>
        string.Join(" ", f.M.Sprache.Where(s => !s.Stempel.Erzeugt).Select(s => Funkprobe.Buchstabe(s.Stempel.Empfaenger)).Distinct().Order(StringComparer.Ordinal));

    private static void Gestrichen(Funkprobe f, uint sender, ushort netz, uint durch, string was)
    {
        var fehler = f.M.Fehler.Where(x => x.Sitzung == Funkprobe.S(sender)).SelectMany(x => x.Fehler).ToList();
        Pruefe.Wahr(fehler.Any(e => e.Code == SendeFehlerCode.KeinSenderecht && e.NetzId == netz && e.Durch == durch),
            was + ": KEIN_SENDERECHT mit durch " + durch + " (" + string.Join(", ", fehler) + ")");
    }

    [Test]
    public static void Sperre_FunkerUndFuehrungOhneAuftrag_SperrenNicht_AufhebenNachRang()
    {
        var f = Aufgestellt();
        Pruefe.Gleich(Fehlercode.KeinRecht, Setzen(f, F, G, Bridge3).Fehler, "ein Funker sperrt nicht");
        Pruefe.Gleich(Fehlercode.KeinRecht, Setzen(f, F, 0, Bridge3).Fehler, "auch nicht für alle");
        Pruefe.Gleich(Fehlercode.KeinRecht, Setzen(f, A, G, Bridge3).Fehler, "die Führung ohne Auftrag sperrt nicht");
        Pruefe.Gleich(0, Konto(f, G).Sperren.Length, "nichts gesperrt");

        f.MitAuftrag(A);
        var r = Setzen(f, A, G, Bridge3);
        Pruefe.Wahr(r.Erfolg, "die Einsatzleitung sperrt: " + r);
        Pruefe.Wahr(r.BetroffeneKonten.Contains(G), "G bekommt Mitgliedschaften");
        Pruefe.Wahr(r.Kanalsperren is [{ KontoId: G, Anlass: KanalsperrAnlass.Gesetzt, Durch: A }], "Folge GESETZT durch A");
        Pruefe.Gleich(Fehlercode.KeinRecht, Aufheben(f, G, G, Bridge3).Fehler, "der Gesperrte hebt nicht auf");
        Pruefe.Gleich(Fehlercode.KeinRecht, Aufheben(f, F, G, Bridge3).Fehler, "ein Funker hebt nicht auf");
        f.MitAuftrag(Funkprobe.B);
        Pruefe.Gleich(Fehlercode.KeinRecht, Aufheben(f, Funkprobe.B, G, Bridge3).Fehler, "eine zweite Einsatzleitung niedrigerer Stufe nicht");
        Pruefe.Gleich(Fehlercode.KeinRecht, Setzen(f, Funkprobe.B, G, Bridge3, f.Jetzt + 2 * Stunde).Fehler, "und überschreibt sie auch nicht");
        var weg = Aufheben(f, Admin, G, Bridge3);
        Pruefe.Wahr(weg.Erfolg && weg.Kanalsperren is [{ Anlass: KanalsperrAnlass.Aufgehoben, Durch: Admin }], "ein ADMIN hebt auf: " + weg);
        Pruefe.Gleich(Fehlercode.Unbekannt, Aufheben(f, A, G, Bridge3).Fehler, "aufgehoben ist aufgehoben");
    }

    [Test]
    public static void Sperre_EinsatzleitungSperrtKeinenAdmin_AuchNichtImFunkerModusOderFuerAlle()
    {
        var f = Aufgestellt().MitAuftrag(A);
        var cmd = K(Kommando, 2);
        foreach (var k in new[] { F, G, Max, A }) Ok(f, Auftrag.FeldSetzen(k, 3, cmd), "Kommando 2 für " + k);
        Pruefe.Gleich(Fehlercode.KeinRecht, Setzen(f, A, Max, cmd).Fehler, "ein Beauftragter sperrt keinen ADMIN");
        Ok(f, Auftrag.FunkermodusSetzen(Max, true, f.Jetzt + 3 * Stunde), "Max spielt als Funker");
        Pruefe.Gleich(Fehlercode.KeinRecht, Setzen(f, A, Max, cmd).Fehler, "auch nicht im Funker-Modus (6.10)");

        Pruefe.Wahr(Setzen(f, A, 0, cmd).Erfolg, "A sperrt Kommando 2 für alle");
        Pruefe.Falsch(f.Senden(A, Sendeart.Netz, [cmd], phase: 1).Verworfen, "die Einsatzleitung bleibt ausgenommen");
        Pruefe.Gleich(Hoert(Max), Empfaenger(f), "F und G hören nicht, Max im Funker-Modus schon (Sperre eines Nicht-Admins)");
        f.M.Leeren();
        var r = f.Senden(F, Sendeart.Netz, [cmd], phase: 2);
        Pruefe.Wahr(r.Verworfen && r.Grund == SendeFehlerCode.KeinSenderecht, "F sendet nicht in Kommando 2");
        Gestrichen(f, F, cmd, A, "F");
        f.M.Leeren();

        Pruefe.Wahr(Setzen(f, Admin, 0, cmd).Erfolg, "ein ADMIN ersetzt die Sperre für alle");
        Pruefe.Wahr(f.Z.SperrenAlle is [{ AuchAdmins: true, Durch: Admin }], "eine Sperre, die ADMIN-Konten trifft");
        Pruefe.Falsch(f.Senden(A, Sendeart.Netz, [cmd], phase: 3).Verworfen, "A sendet weiter");
        Pruefe.Gleich("", Empfaenger(f), "jetzt hört auch Max im Funker-Modus nicht");
        f.M.Leeren();
        Ok(f, Auftrag.FunkermodusSetzen(Max, false, 0), "Max wieder als Admin");
        Pruefe.Falsch(f.Senden(A, Sendeart.Netz, [cmd], phase: 4).Verworfen, "A sendet");
        Pruefe.Gleich(Hoert(Max), Empfaenger(f), "mit ZUWEISEN ist Max ausgenommen");
    }

    [Test]
    public static void Sperre_Ziele_GlobalRuheUnbekanntUndFristen()
    {
        var f = Aufgestellt();
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Admin, G, Netz.GlobalId).Fehler, "Global nie, dafür bleibt Stummschalten");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Admin, G, Kanalrechnung.GlobalSelbstId).Fehler, "Gesamt als Kanal-ID");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Admin, G, K(KanalBruecke, 0)).Fehler, "eine Ruhe");
        Pruefe.Gleich(Fehlercode.Unbekannt, Setzen(f, Admin, G, K(KanalBruecke, 21)).Fehler, "Ch. außerhalb der Liste");
        Pruefe.Gleich(Fehlercode.Unbekannt, Setzen(f, Admin, G, K(500, 1)).Fehler, "ein fremdes Kanalnetz");
        Pruefe.Gleich(Fehlercode.Unbekannt, Setzen(f, Admin, G, 999).Fehler, "ein unbekanntes Netz");
        Pruefe.Gleich(Fehlercode.Unbekannt, Setzen(f, Admin, 4242, Bridge3).Fehler, "ein unbekanntes Konto");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Admin, G, Bridge3, f.Jetzt).Fehler, "bis nicht nach jetzt");
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Admin, G, Bridge3, f.Jetzt + 24 * Stunde + 1).Fehler, "länger als 24 h");
        Pruefe.Wahr(Setzen(f, Admin, G, K(Netz.GlobalId, 2)).Erfolg, "ein Global-Kanal (Global 1) ist ein gewöhnlicher Kanal");
        Pruefe.Wahr(Setzen(f, Admin, G, Funkprobe.Kommando).Erfolg, "ein gewöhnliches Netz");
        Pruefe.Wahr(Setzen(f, Admin, G, KanalBruecke).Erfolg, "ein ganzes Kanalnetz");
        Pruefe.Wahr(Setzen(f, Admin, G, Bridge3, f.Jetzt + 24 * Stunde).Erfolg, "genau 24 h");
        Pruefe.Gleich(4, Konto(f, G).Sperren.Length, "vier Sperren am Konto G");
    }

    [Test]
    public static void Sperre_Weiterleitung_HoerenSendenDurchsageGlobal()
    {
        var f = Aufgestellt();
        Pruefe.Wahr(Setzen(f, Admin, G, Bridge3).Erfolg, "G in Schiff 3 gesperrt");
        Pruefe.Falsch(f.Senden(F, Sendeart.Netz, [Bridge3], phase: 1).Verworfen, "F sendet in Schiff 3");
        Pruefe.Gleich("", Empfaenger(f), "G hört Schiff 3 nicht");
        f.M.Leeren();
        var g = f.Senden(G, Sendeart.Netz, [Bridge3], phase: 2);
        Pruefe.Wahr(g.Verworfen && g.Grund == SendeFehlerCode.KeinSenderecht, "G sendet nicht in Schiff 3");
        Gestrichen(f, G, Bridge3, Admin, "G");
        f.M.Leeren();
        Pruefe.Falsch(f.Senden(A, Sendeart.Durchsage, [Bridge3], phase: 3).Verworfen, "Durchsage an Schiff 3");
        Pruefe.Gleich(Hoert(F), Empfaenger(f), "eine Durchsage an den Kanal erreicht G nicht, F schon");
        f.M.Leeren();
        Pruefe.Falsch(f.Senden(A, Sendeart.Durchsage, [], phase: 4).Verworfen, "Durchsage an alle");
        Pruefe.Wahr(f.M.Sprache.Any(s => s.Stempel.Empfaenger == Funkprobe.S(G)), "die Durchsage an alle erreicht G");
        f.M.Leeren();
        Pruefe.Falsch(f.Senden(A, Sendeart.Netz, [Netz.GlobalId], phase: 5).Verworfen, "Global (Gesamt)");
        Pruefe.Wahr(f.M.Sprache.Any(s => s.Stempel.Empfaenger == Funkprobe.S(G) && s.Stempel.Art == Stempelart.Global), "Global erreicht G");
        f.M.Leeren();

        Pruefe.Wahr(Setzen(f, Admin, H, KanalBruecke).Erfolg, "H im ganzen Kanalnetz Brücke gesperrt");
        Ok(f, Auftrag.FeldSetzen(F, 1, Bridge5), "F legt Schiff 5 dazu");
        Pruefe.Falsch(f.Senden(F, Sendeart.Netz, [Bridge5], phase: 6).Verworfen, "F in Schiff 5");
        Pruefe.Gleich("", Empfaenger(f), "H hört über das Kanalnetz auch Schiff 5 nicht");
        f.M.Leeren();
        Pruefe.Wahr(f.Senden(H, Sendeart.Netz, [Bridge5], phase: 7).Verworfen, "H sendet nicht in Schiff 5");
        Gestrichen(f, H, Bridge5, Admin, "H");
    }

    [Test]
    public static void Sperre_MittenImSpruch_ErzeugtesEnde()
    {
        var f = Aufgestellt();
        f.Senden(F, Sendeart.Netz, [Bridge3], phase: 7, frame: 100);
        Pruefe.Gleich(Hoert(G), Empfaenger(f), "G hört F");
        f.M.Leeren();
        Pruefe.Wahr(Setzen(f, Admin, G, Bridge3).Erfolg, "G gesperrt, während F spricht");
        f.Senden(F, Sendeart.Netz, [Bridge3], phase: 7, frame: 101);
        Pruefe.Gleich(1, f.M.Sprache.Count, "genau ein Paket an G");
        var s = f.M.Sprache[0].Stempel;
        Pruefe.Wahr(s.Empfaenger == Funkprobe.S(G) && s.Erzeugt && (s.Kennzeichen & Paketkennzeichen.Ende) != 0, "ein erzeugtes ENDE statt weiterer Sprache");
    }

    [Test]
    public static void Sperre_FeldSetzen_NichtHineinlegen_KeinChWechsel_FeldBleibtStehen()
    {
        var f = Aufgestellt();
        Pruefe.Wahr(Setzen(f, Admin, G, Bridge3).Erfolg, "G in Schiff 3 gesperrt");
        var vorher = Felder(f, G);
        var wechsel = f.Tun(Auftrag.FeldSetzen(G, 0, Bridge5));
        Pruefe.Gleich(Fehlercode.KeinRecht, wechsel.Fehler, "kein Ch.-Wechsel im gesperrten Feld");
        Pruefe.Wahr(wechsel.Sperre is { Durch: Admin } sp && sp.Bis == f.Jetzt + Stunde, "die Ablehnung nennt Sperre, Setzenden und Ende");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.FeldSetzen(G, 0, null)).Fehler, "das gesperrte Feld wird auch nicht geleert");
        Pruefe.Gleich(vorher, Felder(f, G), "das Feld bleibt mit dem Kanal stehen (rot)");

        Pruefe.Wahr(Setzen(f, Admin, H, Bridge3).Erfolg, "H in Schiff 3 gesperrt");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.FeldSetzen(H, 0, Bridge3)).Fehler, "kein Ch.-Wechsel auf den gesperrten Kanal");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Tun(Auftrag.FeldSetzen(H, 1, Bridge3)).Fehler, "nicht in ein freies Feld legen");
        Pruefe.Wahr(f.Tun(Auftrag.FeldSetzen(H, 0, K(KanalBruecke, 6))).Erfolg, "ein anderer Ch. bleibt frei");

        Pruefe.Wahr(Aufheben(f, Admin, G, Bridge3).Erfolg, "aufgehoben");
        Pruefe.Wahr(f.Tun(Auftrag.FeldSetzen(G, 0, Bridge5)).Erfolg, "danach wechselt G");
    }

    [Test]
    public static void Sperre_HoerenSetzenUndSatz_HoerenTrotzSperreGehtNicht()
    {
        var f = Aufgestellt();
        Pruefe.Gleich(Fehlercode.Keiner, f.Schreiber.SendeauswahlSetzen(G, [0], false, f.Jetzt), "G sendet in Schiff 3 (Komfortzustand)");
        Pruefe.Wahr(Setzen(f, Admin, G, Bridge3).Erfolg, "G gesperrt");
        Pruefe.Falsch(f.Schreiber.Komfort.Hat(G, out _) || Konto(f, G).Sendeauswahl.Felder.Contains((byte)0), "die Sperre nimmt Schiff 3 aus Gs Sendeauswahl");
        Pruefe.Gleich(Fehlercode.KeinRecht, f.Schreiber.SendeauswahlSetzen(G, [0], false, f.Jetzt), "Sendeauswahl auf den gesperrten Kanal");
        Ok(f, Auftrag.HoerenSetzen(G, Bridge3, false), "hören aus");
        Ok(f, Auftrag.HoerenSetzen(G, Bridge3, true), "hören wieder an (Angriff)");
        f.Senden(F, Sendeart.Netz, [Bridge3], phase: 1);
        Pruefe.Gleich("", Empfaenger(f), "G hört trotzdem nicht");

        Pruefe.Wahr(Setzen(f, Admin, H, Bridge3).Erfolg, "H gesperrt");
        var satz = Ok(f, Auftrag.SatzAnwenden(H, null, null, [new Feldvorgabe(3, Bridge3, true, true)]), "Satz mit Schiff 3");
        Pruefe.Wahr(satz.Uebersprungen is [{ Eintrag: 3, Code: Fehlercode.KeinRecht }], "der gesperrte Kanal wird übersprungen");
    }

    [Test]
    public static void Sperre_Paketweg_MehrfachZieleTeilweiseGestrichen()
    {
        var f = Aufgestellt();
        Ok(f, Auftrag.FeldSetzen(A, 2, Bridge3), "A (Führung) legt Schiff 3 dazu");
        Pruefe.Wahr(Setzen(f, Admin, A, Bridge3).Erfolg, "A in Schiff 3 gesperrt");
        var r = f.Senden(A, Sendeart.Netz, [Funkprobe.Kommando, Bridge3], phase: 1);
        Pruefe.Falsch(r.Verworfen, "Mehrfachsenden: Kommando geht weiter");
        Pruefe.Wahr(f.M.Sprache.Where(s => !s.Stempel.Erzeugt).All(s => !s.Netze.Contains(Bridge3)), "nichts mit Schiff 3 im Stempel");
        Gestrichen(f, A, Bridge3, Admin, "A");
        f.M.Leeren();
        var d = f.Senden(A, Sendeart.Durchsage, [Bridge3], phase: 2);
        Pruefe.Wahr(d.Verworfen && d.Grund == SendeFehlerCode.KeinSenderecht, "eine Durchsage des Gesperrten in den Kanal fällt ganz weg");
        Pruefe.Gleich("", Empfaenger(f), "niemand hört sie");
    }

    [Test]
    public static void Sperre_Ablauf_ImHeisspfadSofort_ImTaktAbgeraeumt()
    {
        var f = Aufgestellt();
        Pruefe.Wahr(Setzen(f, Admin, G, Bridge3, f.Jetzt + 60_000).Erfolg, "für eine Minute");
        Pruefe.Wahr(Setzen(f, Admin, 0, Bridge5, f.Jetzt + 60_000).Erfolg, "Schiff 5 für alle");
        f.Jetzt += 60_000;
        f.Senden(F, Sendeart.Netz, [Bridge3], phase: 1);
        Pruefe.Gleich(Hoert(G), Empfaenger(f), "nach dem Ende hört G sofort, ohne neuen Stand");
        Pruefe.Wahr(f.Tun(Auftrag.FeldSetzen(G, 0, K(KanalBruecke, 4))).Erfolg, "und wechselt");
        var takt = Ok(f, Auftrag.FristenPruefen(), "Fristen-Takt");
        Pruefe.Gleich(2, takt.Kanalsperren.Count(k => k.Anlass == KanalsperrAnlass.Abgelaufen), "zwei Folgen ABGELAUFEN");
        Pruefe.Wahr(Konto(f, G).Sperren.IsEmpty && f.Z.SperrenAlle.IsEmpty, "abgeräumt");
    }

    [Test]
    public static void Sperre_EndetMitDemAuftrag_UndMitDemFunkerModus()
    {
        var f = Aufgestellt().MitAuftrag(A);
        Pruefe.Wahr(Setzen(f, A, G, Bridge3).Erfolg, "A sperrt G");
        Pruefe.Wahr(Setzen(f, A, 0, Bridge5).Erfolg, "A sperrt Schiff 5 für alle");
        Pruefe.Wahr(Setzen(f, Admin, H, Bridge5).Erfolg, "der Admin sperrt H");
        var e = Ok(f, Auftrag.BeauftragungEntziehen(Admin, A), "Auftrag entzogen");
        Pruefe.Gleich(2, e.Kanalsperren.Count(k => k is { Anlass: KanalsperrAnlass.Beendet, Grund: "AUFTRAG" }), "beide Sperren von A enden im selben Schritt");
        Pruefe.Wahr(Konto(f, G).Sperren.IsEmpty && f.Z.SperrenAlle.IsEmpty, "G frei, Schiff 5 für alle frei");
        Pruefe.Gleich(1, Konto(f, H).Sperren.Length, "die Sperre des Admins bleibt");

        f.MitAuftrag(Funkprobe.B);
        Pruefe.Wahr(Setzen(f, Funkprobe.B, G, Bridge3, f.Jetzt + 20 * Stunde).Erfolg, "B sperrt G für 20 h");
        f.Jetzt += 12 * Stunde;
        var takt = Ok(f, Auftrag.FristenPruefen(), "Takt nach dem Ende von Bs Auftrag (12 h)");
        Pruefe.Wahr(takt.Kanalsperren.Any(k => k is { KontoId: G, Anlass: KanalsperrAnlass.Beendet, Grund: "AUFTRAG" }), "Bs Sperre endet mit dem abgelaufenen Auftrag");
        Pruefe.Wahr(Konto(f, G).Sperren.IsEmpty, "G frei");

        Pruefe.Wahr(Setzen(f, Max, G, Bridge3).Erfolg, "Max (ADMIN) sperrt G");
        var funker = Ok(f, Auftrag.FunkermodusSetzen(Max, true, f.Jetzt + Stunde), "Max spielt als Funker");
        Pruefe.Wahr(funker.Kanalsperren is [{ KontoId: G, Anlass: KanalsperrAnlass.Beendet }], "mit dem Funker-Modus enden seine Sperren (Regel 7)");
    }

    [Test]
    public static void Sperre_Grenzen_16JeKonto_64FuerAlle_500Zusammen()
    {
        var f = Aufgestellt();
        for (byte c = 1; c <= 16; c++) Pruefe.Wahr(Setzen(f, Admin, G, K(Kommando, c)).Erfolg, "Sperre " + c);
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Admin, G, K(Kommando, 17)).Fehler, "die 17. Sperre eines Kontos");
        Pruefe.Wahr(Setzen(f, Admin, G, K(Kommando, 1), f.Jetzt + 2 * Stunde).Erfolg, "dieselbe noch einmal ersetzt sie");
        Pruefe.Gleich(16, Konto(f, G).Sperren.Length, "weiter 16");

        var alle = new List<ushort>();
        foreach (var n in new[] { Kommando, KanalBruecke, KanalCrew })
            for (byte c = 1; c <= 20; c++) alle.Add(K(n, c));
        for (byte c = 1; c <= 4; c++) alle.Add(K(Jaeger, c));
        foreach (var id in alle) Pruefe.Wahr(Setzen(f, Admin, 0, id).Erfolg, "für alle " + id);
        Pruefe.Gleich(Fehlercode.Ungueltig, Setzen(f, Admin, 0, K(Jaeger, 5)).Fehler, "die 65. Sperre für alle");

        // 30 weitere Konten mit je 16 Sperren: 480 + 16 + 64 = 560 liegt über 500, also nur so viele, dass es knapp passt.
        var g = Neu(b =>
        {
            for (uint k = 0; k < 31; k++)
                b.MitKonto(new Konto { Id = 2000 + k, Name = "V" + k, Sperren = [.. Enumerable.Range(1, 16).Select(c => new Kanalsperre(K(Kommando, (byte)c), Funkprobe.Start + Stunde, Admin))] });
            return b;
        });
        for (byte c = 1; c <= 4; c++) Pruefe.Wahr(g.Tun(Auftrag.KanalSperreSetzen(Admin, G, K(KanalBruecke, c), g.Jetzt + Stunde)).Erfolg, "bis 500: " + c);
        Pruefe.Gleich(Fehlercode.Ungueltig, g.Tun(Auftrag.KanalSperreSetzen(Admin, G, K(KanalBruecke, 5), g.Jetzt + Stunde)).Fehler, "die 501. Sperre");
    }

    [Test]
    public static void Sperre_RolleWaehlen_GesperrtGelegt_SendevorschlagWeicht()
    {
        // Seit 0.8.1 (Q46) gelten keine Wünsche mehr: Kommando 2 kommt als der Ch., den F schon hat (Stufe 3 der Ch.-Auflösung).
        var f = Aufgestellt();
        Ok(f, Auftrag.RolleWaehlen(F, Co), "F wählt Kapitän");
        Ok(f, Auftrag.FeldSetzen(F, 0, K(Kommando, 2)), "F wechselt selbst auf Kommando 2");
        Pruefe.Wahr(Setzen(f, Admin, F, Kommando).Erfolg, "F im ganzen Kanalnetz Kommando gesperrt");
        var r = Ok(f, Auftrag.RolleWaehlen(F, Co), "F wählt Kapitän noch einmal");
        Pruefe.Gleich(0, r.Uebersprungen.Length, "nichts übersprungen");
        Pruefe.Wahr(Konto(f, F).FeldNummer(0) is { NetzId: var cmd } && cmd == K(Kommando, 2), "Kommando 2 liegt trotzdem in Feld 0 (rot)");
        Pruefe.Gleich("[1]", Konto(f, F).Sendeauswahl.ToString(), "der Sendevorschlag Kommando ist gesperrt: das erste sendefähige Feld");
    }

    [Test]
    public static void Sperre_Phase_GesperrterKanalGelegt_NichtGesendet()
    {
        var f = Aufgestellt(b => b.MitPhase(new Einsatzphase(2, "Entern", "ENT")
        {
            Varianten = [new PhasenVariante(Co) { Felder = [Rf(0, Kommando, standard: true, ch: 4), Rf(1, KanalBruecke)] }],
        }));
        Ok(f, Auftrag.RolleWaehlen(G, Co, new Dictionary<byte, byte> { [1] = 3 }), "G wählt Kapitän");
        Pruefe.Wahr(Setzen(f, Admin, G, K(Kommando, 4)).Erfolg, "G in Kommando 4 gesperrt");
        Ok(f, Auftrag.PhaseSchalten(Admin, 2, 1), "Phase Entern");
        Pruefe.Wahr(Konto(f, G).FeldNummer(0) is { NetzId: var id } && id == K(Kommando, 4), "die Phase legt Kommando 4 ins Feld 0");
        Pruefe.Falsch(Konto(f, G).Sendeauswahl.Felder.Contains((byte)0), "aber nicht in die Sendeauswahl");
        Ok(f, Auftrag.FeldSetzen(F, 2, K(Kommando, 4)), "F legt Kommando 4 dazu");
        f.Senden(F, Sendeart.Netz, [K(Kommando, 4)], phase: 1);
        Pruefe.Gleich("", Empfaenger(f), "G hört Kommando 4 nicht");
    }

    [Test]
    public static void Sperre_NetzLoeschenUndChKuerzen_RaeumenSperren()
    {
        var f = Aufgestellt();
        Pruefe.Wahr(Setzen(f, Admin, G, Bridge3).Erfolg, "G Schiff 3");
        Pruefe.Wahr(Setzen(f, Admin, H, K(KanalBruecke, 1)).Erfolg, "H Schiff 1");
        Pruefe.Wahr(Setzen(f, Admin, 0, K(Jaeger, 2)).Erfolg, "Staffel 1-Bravo für alle");
        var kuerzer = f.Z.Netze[KanalBruecke] with { Kanaele = Liste("Schiff", 2) };
        var e = Ok(f, Auftrag.NetzSetzen(Admin, kuerzer, f.Z.NetzListeVersion), "Brücke nur noch bis Schiff 2");
        Pruefe.Wahr(Konto(f, G).Sperren.IsEmpty && Konto(f, H).Sperren.Length == 1, "die Sperre auf Schiff 3 fällt, die auf Schiff 1 bleibt");
        Pruefe.Wahr(e.Kanalsperren is [{ KontoId: G, Anlass: KanalsperrAnlass.Beendet, Grund: "NETZ" }], "Folge BEENDET (NETZ)");
        var l = Ok(f, Auftrag.NetzLoeschen(Admin, Jaeger, f.Z.NetzListeVersion), "Jäger gelöscht");
        Pruefe.Wahr(f.Z.SperrenAlle.IsEmpty && l.Kanalsperren.Length == 1, "die Sperre für alle auf Jäger fällt mit dem Kanalnetz");
        Pruefe.Gleich(0, f.Z.Pruefen().Count, "der Stand bleibt stimmig");
    }

    [Test]
    public static void Sperre_UebersteheNeustart_SicherungLaesstSieWeg()
    {
        var verzeichnis = Path.Combine(Testserver.Wurzel(), "sperren-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var f = Aufgestellt();
            Pruefe.Wahr(Setzen(f, Admin, G, Bridge3).Erfolg, "G gesperrt");
            Pruefe.Wahr(Setzen(f, Admin, 0, Bridge5).Erfolg, "Schiff 5 für alle");
            var ablage = new Zustandsablage(verzeichnis);
            ablage.Speichern(f.Z, f.Schreiber.Neben);
            var (geladen, _) = new Zustandsablage(verzeichnis).LageLaden();
            Pruefe.Wahr(geladen.Konten[G].Sperren is [{ Durch: Admin } s] && s.NetzId == Bridge3, "die Sperre von G übersteht den Neustart");
            Pruefe.Wahr(geladen.SperrenAlle is [{ AuchAdmins: true } a] && a.NetzId == Bridge5, "die Sperre für alle ebenso");
            var datei = File.ReadAllBytes(Path.Combine(verzeichnis, Zustandsablage.LageDatei));
            var sicherung = System.Text.Encoding.UTF8.GetString(Zustandsablage.OhneAuftraege(datei));
            Pruefe.Falsch(sicherung.Contains("\"sperren\"", StringComparison.Ordinal) || sicherung.Contains("\"sperrenAlle\"", StringComparison.Ordinal),
                "die Sicherung lässt Kanalsperren weg (wie Aufträge)");
        }
        finally
        {
            if (Directory.Exists(verzeichnis)) Directory.Delete(verzeichnis, true);
        }
    }
}
