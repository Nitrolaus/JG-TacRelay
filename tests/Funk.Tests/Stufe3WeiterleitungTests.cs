// JG TacRelay - Maintainer: Sarevoc
using Funk.Server.Lage;
using static Funk.Tests.Kanalprobe;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Tests;

/// <summary>
/// Sprache in Kanälen (Fassung 0.6.0, FUNKPROTOKOLL 6.3, 6.4, 6.16, P-61): Hörer je Kanal, die Ruhe hört und sendet nie, geratene
/// und fremde Kanal-IDs im Sprachpaket, nur Taste und Stummschaltung über das Kanalnetz, ein Ch.-Wechsel mitten im Spruch, Durchsage
/// an Kanäle, Gesamt und ein Global-Kanal.
/// </summary>
internal static class Stufe3WeiterleitungTests
{
    private static readonly ushort Bridge3 = K(KanalBruecke, 3);

    private static Funkprobe Aufgestellt()
    {
        var f = Neu();
        Ok(f, Auftrag.FeldSetzen(F, 0, Bridge3), "F Schiff 3");
        Ok(f, Auftrag.FeldSetzen(G, 0, Bridge3), "G Schiff 3");
        Ok(f, Auftrag.FeldSetzen(H, 0, K(KanalBruecke, 5)), "H Schiff 5");
        Ok(f, Auftrag.FeldSetzen(Funkprobe.E, 2, K(KanalBruecke, 0)), "E Ruhe");
        return f;
    }

    private static string Hoert(uint konto, string art, params ushort[] netze) => $"{Funkprobe.Buchstabe(Funkprobe.S(konto))}:{art}[{string.Join(",", netze)}]";

    /// <summary>Kopien echter Pakete; ein vom Server erzeugtes ENDE an die Hörer einer abgelösten Phase zählt nicht (6.4).</summary>
    private static int Echte(Funkprobe f) => f.M.Sprache.Count(s => !s.Stempel.Erzeugt);

    private static void Verworfen(Funkprobe f, Weiterleitungsergebnis r, SendeFehlerCode code, string was)
    {
        Pruefe.Wahr(r.Verworfen, was + ": verworfen");
        Pruefe.Gleich(code, r.Grund, was);
        Pruefe.Gleich(0, Echte(f), was + ": keine Kopie");
        f.M.Leeren();
    }

    [Test]
    public static void Sprache_HoererJeKanal_RuheHoertNicht()
    {
        var f = Aufgestellt();
        var r = f.Senden(F, Sendeart.Netz, [Bridge3], phase: 1);
        Pruefe.Falsch(r.Verworfen, "nicht verworfen: " + r.Grund);
        Pruefe.Gleich(Hoert(G, "NETZ", Bridge3), f.M.Empfang(), "nur G hört Schiff 3; H (Schiff 5) und E (Ruhe) nicht");
        f.M.Leeren();
        Ok(f, Auftrag.HoerenSetzen(G, Bridge3, false), "G hört Schiff 3 nicht mehr");
        var aus = f.Senden(F, Sendeart.Netz, [Bridge3], phase: 2);
        Pruefe.Wahr(!aus.Verworfen && Echte(f) == 0, "erlaubt, aber niemand hört");
        f.M.Leeren();
        var h = f.Senden(H, Sendeart.Netz, [K(KanalBruecke, 5)], phase: 3);
        Pruefe.Wahr(!h.Verworfen && Echte(f) == 0, "H allein in Schiff 5");
    }

    [Test]
    public static void Sprache_RuheSendetNie_GerateneUndFremdeIds()
    {
        var f = Aufgestellt();
        Verworfen(f, f.Senden(Funkprobe.E, Sendeart.Netz, [K(KanalBruecke, 0)], phase: 1), SendeFehlerCode.KeinSenderecht, "in die Ruhe senden");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [K(KanalBruecke, 21)], phase: 2), SendeFehlerCode.UnbekanntesNetz, "Ch. 21 außerhalb der Liste");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [(ushort)(0x8000 + KanalBruecke * 32 + 31)], phase: 3), SendeFehlerCode.UnbekanntesNetz, "Ch. 31");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [K(500, 1)], phase: 4), SendeFehlerCode.UnbekanntesNetz, "fremdes Kanalnetz");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [Kanalrechnung.GlobalSelbstId], phase: 5), SendeFehlerCode.UnbekanntesNetz, "Gesamt als Kanal-ID");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [K(KanalBruecke, 8)], phase: 6), SendeFehlerCode.KeinSenderecht, "gültiger Kanal, den F nicht hält");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [KanalBruecke], phase: 7), SendeFehlerCode.KeinSenderecht, "das Kanalnetz selbst");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [0x8000], phase: 8), SendeFehlerCode.UnbekanntesNetz, "Netz 0 im Kanalbereich");
    }

    [Test]
    public static void Sprache_NurTasteUndStummschaltung_UeberDasKanalnetz()
    {
        var f = Aufgestellt();
        var cmd = K(Kommando, 2);
        Ok(f, Auftrag.FeldSetzen(F, 1, cmd), "F Kommando 2");
        Ok(f, Auftrag.FeldSetzen(G, 1, cmd), "G Kommando 2");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [cmd], va: true, phase: 1), SendeFehlerCode.NurTaste, "Kommando nur Taste");
        Pruefe.Falsch(f.Senden(F, Sendeart.Netz, [cmd], phase: 2).Verworfen, "mit Taste");
        Pruefe.Gleich(Hoert(G, "NETZ", cmd), f.M.Empfang(), "G hört Kommando 2");
        f.M.Leeren();
        Pruefe.Wahr(f.Stumm(F, KanalBruecke).Erfolg, "F im ganzen Kanalnetz Brücke stumm");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [Bridge3], phase: 3), SendeFehlerCode.Stummgeschaltet, "Schiff 3 über das Kanalnetz stumm");
        Pruefe.Falsch(f.Senden(F, Sendeart.Netz, [cmd], phase: 4).Verworfen, "Kommando bleibt frei");
        f.M.Leeren();
        Pruefe.Wahr(f.Stumm(F, KanalBruecke, 0).Erfolg, "aufgehoben");
        Pruefe.Wahr(f.Stumm(F, Bridge3).Erfolg, "nur Schiff 3 stumm");
        Verworfen(f, f.Senden(F, Sendeart.Netz, [Bridge3], phase: 5), SendeFehlerCode.Stummgeschaltet, "Schiff 3 stumm");
        Pruefe.Gleich(Fehlercode.Unbekannt, f.Stumm(F, K(KanalBruecke, 21)).Fehler, "Stummschalten auf einen Ch. außerhalb der Liste");
    }

    [Test]
    public static void Sprache_ChWechselMittenImSpruch_ErzeugtesEnde()
    {
        var f = Aufgestellt();
        f.Senden(F, Sendeart.Netz, [Bridge3], phase: 7, frame: 100);
        Pruefe.Gleich(Hoert(G, "NETZ", Bridge3) + "+B", f.M.Empfang(mitKennzeichen: true), "G hört mit BEGINN");
        f.M.Leeren();
        Ok(f, Auftrag.FeldSetzen(G, 0, K(KanalBruecke, 5)), "G wechselt auf Schiff 5");
        f.Senden(F, Sendeart.Netz, [Bridge3], phase: 7, frame: 101);
        Pruefe.Gleich(Hoert(G, "NETZ", Bridge3) + "+E+X", f.M.Empfang(mitKennzeichen: true), "G bekommt ein erzeugtes ENDE statt weiterer Pakete");
    }

    [Test]
    public static void Sprache_DurchsageAnKanaele_GesamtUndGlobalKanal()
    {
        var f = Aufgestellt();
        Ok(f, Auftrag.HoerenSetzen(G, Bridge3, false), "G hört Schiff 3 nicht");
        Pruefe.Falsch(f.Senden(Funkprobe.A, Sendeart.Durchsage, [Bridge3], phase: 1).Verworfen, "Durchsage an Schiff 3");
        Pruefe.Gleich(Hoert(F, "DURCHSAGE", Bridge3) + " " + Hoert(G, "DURCHSAGE", Bridge3), f.M.Empfang(), "auch G mit hören aus");
        f.M.Leeren();
        var leer = f.Senden(Funkprobe.A, Sendeart.Durchsage, [K(KanalBruecke, 9)], phase: 2);
        Pruefe.Wahr(!leer.Verworfen && Echte(f) == 0 && f.M.Fehler.Count == 0, "Durchsage an einen Kanal ohne Hörer: niemand, kein Fehler");
        f.M.Leeren();
        Pruefe.Falsch(f.Senden(Funkprobe.A, Sendeart.Netz, [Netz.GlobalId], phase: 3).Verworfen, "Global (Gesamt)");
        Pruefe.Gleich(7, f.M.Sprache.Count(s => s.Stempel.Art == Stempelart.Global), "alle anderen sieben Sitzungen hören Gesamt");
        f.M.Leeren();
        var global1 = K(Netz.GlobalId, 2);
        Ok(f, Auftrag.FeldSetzen(F, 2, global1), "F Global 1");
        Ok(f, Auftrag.FeldSetzen(H, 2, global1), "H Global 1");
        Pruefe.Falsch(f.Senden(F, Sendeart.Netz, [global1], phase: 4).Verworfen, "Global 1");
        Pruefe.Gleich(Hoert(H, "NETZ", global1), f.M.Empfang(), "Global 1 hört nur, wer ihn hat, als gewöhnlicher Kanal");
    }
}
