// JG TacRelay - Maintainer: Sarevoc
using Funk.Server.Lage;
using static Funk.Tests.Kanalprobe;
using Flottenvorlagen = Funk.Protokoll.Steuerung.Flottenvorlagen;

namespace Funk.Tests;

/// <summary>
/// Fassung 0.9.1 (Sarevoc 08.10.2026): Das erste Feld, das nach der Rollenwahl von der Ruhe auf einen Ch. geht, wird Sendeziel, wenn noch keins gewählt
/// ist; Kommando ist in der Vorlage kein "nur Taste" (Q53); die Ch.-Listen stehen ohne Scrollen ganz da.
/// </summary>
internal static class Fassung091Tests
{
    [Test]
    public static void Rollenwahl_ErsterChAusDerRuhe_WirdSendeziel_WennNochKeinsGewaehlt()
    {
        var f = Neu();
        Ok(f, Auftrag.RolleWaehlen(G, Co), "G wählt Kapitän");
        Pruefe.Wahr(Konto(f, G).Sendeauswahl is { Felder.IsEmpty: true, Global: false }, "alles auf Ruhe: kein Sendeziel");

        Ok(f, Auftrag.FeldSetzen(G, 0, K(Kommando, 1)), "Kommando 1");
        Pruefe.Wahr(Konto(f, G).Sendeauswahl.Felder.SequenceEqual([(byte)0]), "Kommando ist jetzt Sendeziel");

        Ok(f, Auftrag.FeldSetzen(G, 1, K(KanalBruecke, 3)), "Brücke Schiff 3");
        Pruefe.Wahr(Konto(f, G).Sendeauswahl.Felder.SequenceEqual([(byte)0]), "ist schon eins gewählt, bleibt es");

        Ok(f, Auftrag.FeldSetzen(G, 0, K(Kommando, 0)), "Kommando zurück auf Ruhe");
        Pruefe.Wahr(Konto(f, G).Sendeauswahl.Felder.IsEmpty, "eine Ruhe sendet nicht: kein Sendeziel");
        Ok(f, Auftrag.FeldSetzen(G, 1, K(KanalBruecke, 4)), "Ch.-Wechsel ohne Ruhe dazwischen");
        Pruefe.Wahr(Konto(f, G).Sendeauswahl.Felder.IsEmpty, "nur der Schritt aus der Ruhe wählt von selbst");
        Ok(f, Auftrag.FeldSetzen(G, 0, K(Kommando, 2)), "Kommando 2 aus der Ruhe");
        Pruefe.Wahr(Konto(f, G).Sendeauswahl.Felder.SequenceEqual([(byte)0]), "wieder Sendeziel");
    }

    [Test]
    public static void StarCitizen_KeinKanalNurTaste_AbWerkSprachaktivierung()
    {
        var netze = Flottenvorlagen.StarCitizen().Paket.Ops.Where(o => o.Op == Funk.Protokoll.Steuerung.VerwaltungsOperation.NetzSetzen)
            .Select(o => (Funk.Protokoll.Steuerung.Netz)o.ObjektLesen<Funk.Protokoll.Steuerung.Netz>().Body!).ToList();
        Pruefe.Falsch(netze.Single(n => n.Name == "Kommando").NurTaste, "Kommando ist nicht nur Taste (Q53)");
        Pruefe.Wahr(netze.All(n => !n.NurTaste && n.AusloeserStandard == Funk.Protokoll.Steuerung.Ausloeser.Va), "kein Kanal der Vorlage ist nur Taste, alle ab Werk VA");
    }

    [Test]
    public static void Klappliste_LaengsteChListe_OhneScrollen()
    {
        // Ein Eintrag ist mit Rand etwa 22 hoch; die längste Liste der Vorlage ist Brücke (Ruhe, Schiff 1 bis 12, Alle), die Rollenliste hat 14.
        var laengste = Math.Max(Flottenvorlagen.StarCitizenKanaele.Max(k => k.Kanaele.Count), Flottenvorlagen.StarCitizenRollen.Count);
        Pruefe.Gleich(14, laengste, "Brücke mit 14 Einträgen, 14 Rollen");
        Pruefe.Wahr(Funk.Client.Steuerelemente.Klappwahl.ListenHoeheMax >= laengste * 24 + 8, "Platz für " + laengste + " Einträge");
    }
}
