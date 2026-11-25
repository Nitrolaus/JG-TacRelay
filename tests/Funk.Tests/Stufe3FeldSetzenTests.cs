// JG TacRelay - Maintainer: Sarevoc
using Funk.Server.Lage;
using static Funk.Tests.Kanalprobe;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Tests;

/// <summary>
/// FeldSetzen mit Kanälen (Fassung 0.6.0, FUNKPROTOKOLL 4.5 0x0035 Regeln 1 bis 6, 6.16, P-61) am Zustandsschreiber: Kanal und Ruhe
/// in ein Feld, Ch.-Wechsel immer erlaubt mit bleibendem hören und senden, auch über die Ruhe und seit 0.8.1 auch im festen Feld,
/// seit 0.8.1 (Q44/Q45) weder Leeren noch Umlegen noch Deckung durch die Rolle, die Sendeauswahl nach dem Ch.-Wechsel und die Angriffe
/// mit geratenen Kanal-IDs.
/// </summary>
internal static class Stufe3FeldSetzenTests
{
    [Test]
    public static void FeldSetzen_KanalRuheUndChWechsel_HoerenUndSendenBleiben()
    {
        var f = Neu();
        Ok(f, Auftrag.FeldSetzen(F, 0, K(KanalBruecke, 3)), "Brücke, Schiff 3 (offen)");
        Pruefe.Gleich("0:100/3hs", Felder(f, F), "hören und senden aus offen HOEREN_SENDEN");
        Ok(f, Auftrag.FeldSetzen(F, 0, K(KanalBruecke, 5)), "Ch.-Wechsel auf Schiff 5");
        Pruefe.Gleich("0:100/5hs", Felder(f, F), "Ch.-Wechsel");
        Ok(f, Auftrag.HoerenSetzen(F, K(KanalBruecke, 5), false), "hören aus");
        Ok(f, Auftrag.FeldSetzen(F, 0, K(KanalBruecke, 0)), "Ruhe");
        Pruefe.Gleich("0:100/0s", Felder(f, F), "die Ruhe behält senden als Vorgabe, hören bleibt aus");
        Ok(f, Auftrag.FeldSetzen(F, 0, K(KanalBruecke, 7)), "aus der Ruhe auf Schiff 7");
        Pruefe.Gleich("0:100/7s", Felder(f, F), "hören und senden bleiben über die Ruhe hinweg");
        Ok(f, Auftrag.FeldSetzen(F, 1, K(Jaeger, 24)), "Staffel 4-Foxtrot (nur hören)");
        Pruefe.Gleich("0:100/7s 1:103/24h", Felder(f, F), "Jäger hört nur");
        Pruefe.Gleich(Fehlercode.Unbekannt, Code(f, Auftrag.FeldSetzen(F, 1, K(Jaeger, 25))), "Jäger hat 25 Einträge, Ch. 25 gibt es nicht");
        Ok(f, Auftrag.FeldSetzen(F, 2, K(Netz.GlobalId, 2)), "Global 1 als gewöhnlicher Kanal");
        Pruefe.Gleich(Quelle.Selbst, Konto(f, F).Quelle, "Quelle SELBST");
        // Seit 0.8.1 (Q44): Ein Feld leert man nicht selbst, und ein belegtes Feld bekommt keinen anderen Kanal.
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.FeldSetzen(F, 2, null)), "leeren");
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 9))), "anderes Kanalnetz im belegten Feld");
        Pruefe.Wahr(Felder(f, F).StartsWith("0:100/7s 1:103/24h 2:", StringComparison.Ordinal), "nichts geleert, nichts umgelegt");
    }

    [Test]
    public static void FeldSetzen_Angriffe_GerateneUndFalscheIds()
    {
        var f = Neu();
        Ok(f, Auftrag.FeldSetzen(F, 0, K(KanalBruecke, 3)), "Ausgangslage");
        Pruefe.Gleich(Fehlercode.Unbekannt, Code(f, Auftrag.FeldSetzen(F, 1, K(500, 1))), "Kanalnetz 500 gibt es nicht");
        Pruefe.Gleich(Fehlercode.Unbekannt, Code(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 21))), "Ch. 21 außerhalb der Liste");
        Pruefe.Gleich(Fehlercode.Unbekannt, Code(f, Auftrag.FeldSetzen(F, 1, (ushort)(0x8000 + KanalBruecke * 32 + 31))), "Ch. 31");
        Pruefe.Gleich(Fehlercode.Unbekannt, Code(f, Auftrag.FeldSetzen(F, 1, 0x8000)), "Netz 0");
        Pruefe.Gleich(Fehlercode.Unbekannt, Code(f, Auftrag.FeldSetzen(F, 1, (ushort)(0x8000 + 1023 * 32 + 1))), "Netz 1023");
        Pruefe.Gleich(Fehlercode.Unbekannt, Code(f, Auftrag.FeldSetzen(F, 1, 0xFFFF)), "0xFFFF");
        Pruefe.Gleich(Fehlercode.Ungueltig, Code(f, Auftrag.FeldSetzen(F, 1, KanalBruecke)), "die Netz-ID des Kanalnetzes selbst");
        Pruefe.Gleich(Fehlercode.Ungueltig, Code(f, Auftrag.FeldSetzen(F, 1, Netz.GlobalId)), "Global");
        Pruefe.Gleich(Fehlercode.Ungueltig, Code(f, Auftrag.FeldSetzen(F, 1, Kanalrechnung.GlobalSelbstId)), "Gesamt liegt nur im festen Global-Feld");
        Pruefe.Gleich(Fehlercode.Ungueltig, Code(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 3))), "derselbe Kanal in einem zweiten Feld");
        Ok(f, Auftrag.FeldSetzen(F, 2, K(Kommando, 0)), "Kommando auf Ruhe");
        Pruefe.Gleich(Fehlercode.Ungueltig, Code(f, Auftrag.FeldSetzen(F, 3, K(Kommando, 0))), "dieselbe Ruhe in einem zweiten Feld");
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.FeldSetzen(F, 3, K(KanalCrew, 4))), "Crew ist zu und nicht gedeckt");
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.FeldSetzen(F, 3, Funkprobe.Crew)), "gewöhnliches Netz zu, nicht gedeckt");
        Pruefe.Gleich(Fehlercode.Ungueltig, Code(f, Auftrag.FeldSetzen(F, 16, K(KanalBruecke, 4))), "Feld 16");
        Pruefe.Gleich("0:100/3hs 2:101/0hs", Felder(f, F), "nichts Abgewiesenes hat etwas geändert");
        Pruefe.Gleich("", string.Join("; ", f.Z.Pruefen()), "stimmig");
    }

    [Test]
    public static void FeldSetzen_FestesFeld_ChWechselJa_LeerenUndUmlegenNein()
    {
        // Bis 0.8.0 sperrte "fest" auch den Ch.; seit 0.8.1 (Q45, Sarevoc am 06.10.2026) bleibt der Ch. für den Funker immer wählbar.
        var f = Neu();
        Ok(f, Auftrag.Verschieben(Funkprobe.Admin, [new Aenderung(F) { FelderPatch = [new Feldpatch(4, K(KanalBruecke, 6), Senden: true)] }]), "Einsatzleitung legt Schiff 6 fest");
        Pruefe.Gleich("4:100/6hsf", Felder(f, F), "fest");
        Ok(f, Auftrag.FeldSetzen(F, 4, K(KanalBruecke, 7)), "Ch.-Wechsel im festen Feld");
        Pruefe.Gleich("4:100/7hsf", Felder(f, F), "Ch. gewechselt, das Feld bleibt fest");
        Pruefe.Gleich(Fehlercode.FeldFest, Code(f, Auftrag.FeldSetzen(F, 4, K(Kommando, 2))), "anderes Kanalnetz im festen Feld");
        Pruefe.Gleich(Fehlercode.FeldFest, Code(f, Auftrag.FeldSetzen(F, 4, Netz.GlobalId)), "FELD_FEST geht den übrigen Prüfungen vor");
        Pruefe.Gleich(Fehlercode.FeldFest, Code(f, Auftrag.FeldSetzen(F, 4, null)), "leeren im festen Feld");
        Ok(f, Auftrag.HoerenSetzen(F, K(KanalBruecke, 7), false), "hören darf er abschalten (A17)");
        Pruefe.Gleich("4:100/7sf", Felder(f, F), "nur hören geändert");
        Ok(f, Auftrag.FeldSetzen(F, 4, K(KanalBruecke, 0)), "Ruhe im festen Feld");
        Pruefe.Gleich("4:100/0sf", Felder(f, F), "Ruhe, fest bleibt");
    }

    [Test]
    public static void FeldSetzen_KeineDeckungMehr_NurChWechselUndOffeneNetzeInFreieFelder()
    {
        // Bis 0.8.0 durfte ein Funker ein von seiner Rolle gedecktes Kanalnetz in ein weiteres Feld legen; seit 0.8.1 (Q44) nicht mehr.
        var f = Neu();
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.FeldSetzen(G, 5, K(KanalCrew, 9))), "ohne Rolle: Crew zu");
        Ok(f, Auftrag.RolleWaehlen(G, Co), "G wählt Kapitän");
        Pruefe.Gleich("0:101/0hs 1:100/0hs 2:102/0hs", Felder(f, G), "alle Kanalnetze ab Werk auf Ruhe");
        Ok(f, Auftrag.FeldSetzen(G, 2, K(KanalCrew, 9)), "Ch.-Wechsel im zuen Kanalnetz: immer erlaubt");
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.FeldSetzen(G, 5, K(KanalCrew, 11))), "ein zweiter Crew-Kanal: keine Deckung mehr");
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.FeldSetzen(G, 0, K(KanalBruecke, 3))), "offenes Kanalnetz in ein belegtes Feld");
        Ok(f, Auftrag.FeldSetzen(G, 5, K(Jaeger, 24)), "offenes Netz in ein freies Feld (so trägt sich der Echo-Funker ein)");
        Pruefe.Gleich("0:101/0hs 1:100/0hs 2:102/9hs 5:103/24h", Felder(f, G), "Jäger hört nur, wie offen es ist");

        Ok(f, Auftrag.EinsteigenRolleSchiff(H, null, BrueckeRolle, Schiff4), "H steigt in Schiff 4 ein");
        Pruefe.Gleich("0:100/4hs 1:102/4h", Felder(f, H), "Kennung BRIDGE gibt den Kanal, Crew Ch. 4 aus der Einheit");
        Pruefe.Gleich(Fehlercode.KeinRecht, Code(f, Auftrag.FeldSetzen(H, 6, K(KanalCrew, 11))), "auch die Rolle des Platzes deckt nicht mehr");
        Pruefe.Gleich("0:100/4hs 1:102/4h", Felder(f, H), "unverändert");
    }

    [Test]
    public static void FeldSetzen_Sendeauswahl_BleibtBeimChWechsel_RuheSendetNicht()
    {
        var f = Neu();
        Ok(f, Auftrag.RolleWaehlen(F, JaegerRolle), "F wählt Jägerpilot");
        Pruefe.Wahr(Konto(f, F).Sendeauswahl.Felder.SequenceEqual([(byte)1]), "Sendevorschlag Feld 1 (Brücke Ch. 2)");
        Ok(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 5)), "Ch.-Wechsel im Sendefeld");
        Pruefe.Wahr(Konto(f, F).Sendeauswahl.Felder.SequenceEqual([(byte)1]), "die Sendeauswahl bleibt auf Feld 1 (C4 im Client)");
        Ok(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 0)), "Ruhe im Sendefeld");
        Pruefe.Wahr(Konto(f, F).Sendeauswahl.Felder.IsEmpty, "eine Ruhe sendet nicht: Feld 1 fällt heraus");
        Pruefe.Gleich(Funk.Server.Lage.Fehlercode.Ungueltig, f.Schreiber.SendeauswahlSetzen(F, [1], false, f.Jetzt), "SendeauswahlSetzen auf eine Ruhe");
        Ok(f, Auftrag.FeldSetzen(F, 1, K(KanalBruecke, 6)), "wieder ein Ch.");
        Pruefe.Gleich(Funk.Server.Lage.Fehlercode.Keiner, f.Schreiber.SendeauswahlSetzen(F, [1], false, f.Jetzt), "jetzt sendet Feld 1 wieder");
    }
}
