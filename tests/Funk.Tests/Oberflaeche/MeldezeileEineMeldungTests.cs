// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Meldezeile zeigt immer nur eine Meldung (Sarevoc Q112, 12.10.2026; bis dahin höchstens drei untereinander). Vorrang Fehler vor Leitung vor
/// Hinweis: Eine neue Meldung gleichen oder höheren Rangs ersetzt die stehende, und die ersetzte kommt nicht wieder; eine niedrigere wartet,
/// bis die stehende abläuft oder gelöst wird, und verfällt, wenn ihre eigene Dauer bis dahin abgelaufen ist. Derselbe Schlüssel ersetzt wie
/// bisher, auch eine wartende. Eine gehaltene Meldung (Melden) wartet, wenn sie verdrängt wird, und steht danach wieder da. Nach jedem
/// Schritt: höchstens eine Zeile, kein Zeilenumbruch im Text. Reines Modell an der Testuhr, ohne Fenster.
/// <para>
/// Seit 0.11.2 nimmt die Meldezeile nur noch die Schlüssel aus <see cref="Meldezeile.Erlaubt"/> an (Sarevoc 13.10.2026: nur, was die Leitung tut,
/// das eigene Funken und die Probe); alles andere fällt still weg. Die Regeln oben gelten für jeden erlaubten Schlüssel, darum laufen die Tests
/// mit ihnen; bis 0.11.1 standen hier frei gewählte Schlüssel wie "sendenetz" oder "h1".
/// </para>
/// </summary>
internal static class MeldezeileEineMeldungTests
{
    private const string Umstellung = Teilnehmermeldungen.UmstellungSchluessel;
    private const string Ansage = Teilnehmermeldungen.AnsageSchluessel;
    private const string Entzug = Teilnehmermeldungen.EntzugSchluessel;
    private const string Melden = Teilnehmermeldungen.MeldenSchluessel;
    private const string Phase = Phasensteuerung.Schluessel;

    /// <summary>Höchstens eine Meldung, ein Satz ohne Umbruch, Sichtbar und HatFehler passend zur stehenden.</summary>
    private static void EineZeile(Meldezeile z, string? erwartet, string was)
    {
        Pruefe.Wahr(z.Zeilen.Count <= Meldezeile.ZeilenMax && Meldezeile.ZeilenMax == 1, was + ": höchstens eine Meldung (" + z.Zeilen.Count + ")");
        Pruefe.Falsch(z.Text.Contains('\n', StringComparison.Ordinal), was + ": kein Umbruch");
        Pruefe.Gleich(erwartet ?? "", z.Text, was);
        Pruefe.Gleich(erwartet is not null, z.Sichtbar, was + ": sichtbar nur mit Meldung");
        Pruefe.Gleich(z.Zeilen.Count == 1 && z.Zeilen[0].Art == Meldeart.Fehler, z.HatFehler, was + ": HatFehler");
        if (erwartet is not null) Pruefe.Gleich(erwartet, z.Zeilen[0].Text, was + ": die Zeile");
    }

    [Test]
    public static void EineMeldung_GleicherOderHoehererRangErsetzt_UndKommtNichtWieder()
    {
        long t = 0;
        var z = new Meldezeile(() => t);
        EineZeile(z, null, "leer");
        z.Zeigen(Umstellung, "Eingeteilt von Ferox: Schiff 2, Gunner", Meldeart.Hinweis, 6000);
        EineZeile(z, "Eingeteilt von Ferox: Schiff 2, Gunner", "ein Hinweis");
        t = 100;
        z.Zeigen(Ansage, "Kanäle eingeteilt von Ferox", Meldeart.Hinweis, 2000);
        EineZeile(z, "Kanäle eingeteilt von Ferox", "gleicher Rang ersetzt");
        Pruefe.Falsch(z.Zeigt(Umstellung), "der ersetzte Hinweis ist fort, er wartet nicht");
        t = 200;
        z.Zeigen(Phase, "Phase 2 Entern läuft - 3 umgestellt", Meldeart.Leitung, 5000);
        EineZeile(z, "Phase 2 Entern läuft - 3 umgestellt", "die Leitung ersetzt den Hinweis");
        t = 300;
        z.Zeigen(Entzug, "Nicht gesendet: kein Senderecht mehr in Crew 2", Meldeart.Fehler, 3000);
        EineZeile(z, "Nicht gesendet: kein Senderecht mehr in Crew 2", "der Fehler ersetzt die Leitung");
        t = 400;
        z.Zeigen(Melden, "Melden geht nicht: kein Senderecht in Kommando", Meldeart.Fehler, 3000);
        EineZeile(z, "Melden geht nicht: kein Senderecht in Kommando", "ein neuer Fehler ersetzt den alten");
        Pruefe.Falsch(z.Zeigt(Entzug) || z.Zeigt(Phase) || z.Zeigt(Ansage), "nichts Ersetztes wartet");
        t = 3399;
        z.Takt();
        EineZeile(z, "Melden geht nicht: kein Senderecht in Kommando", "steht seine Dauer");
        t = 3400;
        z.Takt();
        // Die Umstellung (bis 6000) und die Phase (bis 5200) wären noch in ihrer Zeit: ersetzt heißt fort.
        EineZeile(z, null, "danach leer, nichts Ersetztes kommt wieder");
    }

    [Test]
    public static void EineMeldung_NiedrigereWartet_UndVerfaellt()
    {
        long t = 0;
        var z = new Meldezeile(() => t);
        z.Zeigen(Entzug, "Nicht gesendet: kein Senderecht mehr in Crew 1", Meldeart.Fehler, 5000);
        t = 1000;
        z.Zeigen(Umstellung, "Eingeteilt von Ferox: Schiff 3, Gunner", Meldeart.Hinweis, 2000);
        EineZeile(z, "Nicht gesendet: kein Senderecht mehr in Crew 1", "der Hinweis wartet");
        Pruefe.Wahr(z.Zeigt(Umstellung), "er wartet (Zeigt)");
        t = 2000;
        z.Zeigen(Ansage, "Kanäle eingeteilt von Ferox", Meldeart.Hinweis, 8000);
        t = 2500;
        z.Zeigen(Phase, "Phase 2 Entern läuft - 3 umgestellt", Meldeart.Leitung, 4000);
        EineZeile(z, "Nicht gesendet: kein Senderecht mehr in Crew 1", "Leitung und Hinweise warten hinter dem Fehler");
        t = 3000;
        z.Takt();
        Pruefe.Falsch(z.Zeigt(Umstellung), "nach seinen 2 s verfallen, ungesehen");
        EineZeile(z, "Nicht gesendet: kein Senderecht mehr in Crew 1", "der Fehler steht weiter");
        t = 5000;
        z.Takt();
        EineZeile(z, "Phase 2 Entern läuft - 3 umgestellt", "der Fehler ist fort: die ranghöchste wartende, mit dem Rest ihrer Dauer");
        t = 6499;
        z.Takt();
        EineZeile(z, "Phase 2 Entern läuft - 3 umgestellt", "ihre Dauer zählt ab dem Eingang (2500 + 4000)");
        t = 6500;
        z.Takt();
        EineZeile(z, "Kanäle eingeteilt von Ferox", "dann der Hinweis");
        t = 10_000;
        z.Takt();
        EineZeile(z, null, "der Hinweis endet nach seinen 8 s ab Eingang");

        // Ohne Takt dazwischen: Eine abgelaufene stehende hält keine neue auf.
        t = 20_000;
        z.Zeigen(Entzug, "Nicht gesendet: kein Senderecht mehr in Crew 2", Meldeart.Fehler, 1000);
        t = 21_000;
        z.Zeigen(Umstellung, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 2000);
        EineZeile(z, "Eingeteilt von Ferox: Schiff 1, Gunner", "der Fehler war schon abgelaufen");

        // Warten zwei gleichen Rangs, kommt die neuere zuerst.
        t = 30_000;
        z.Loesen(Umstellung);
        z.Zeigen(Entzug, "Nicht gesendet: kein Senderecht mehr in Crew 2", Meldeart.Fehler, 1000);
        z.Zeigen(Umstellung, "Eingeteilt von Ferox: Schiff 2, Pilot", Meldeart.Hinweis, 9000);
        z.Zeigen(Ansage, "Kanäle eingeteilt von Kobold", Meldeart.Hinweis, 9000);
        z.Loesen(Entzug);
        EineZeile(z, "Kanäle eingeteilt von Kobold", "gelöst: die neuere wartende rückt nach");
    }

    [Test]
    public static void EineMeldung_GleicherSchluesselErsetzt_AuchWartend()
    {
        long t = 0;
        var z = new Meldezeile(() => t);
        z.Zeigen(Phase, "Phase 2/3 ENTERN? Nochmal drücken - 3 Funker", Meldeart.Leitung, 5000);
        t = 100;
        z.Zeigen(Phase, "Phasen schaltet die Einsatzleitung", Meldeart.Hinweis, 3000);
        EineZeile(z, "Phasen schaltet die Einsatzleitung", "derselbe Schlüssel ersetzt, auch mit niedrigerem Rang");
        t = 200;
        z.Zeigen(Entzug, "Nicht gesendet: kein Senderecht mehr in Crew 1", Meldeart.Fehler, 5000);
        t = 300;
        z.Zeigen(Umstellung, "Eingeteilt von Ferox: Schiff 2, Gunner", Meldeart.Hinweis, 2000);
        t = 400;
        z.Zeigen(Umstellung, "Eingeteilt von Ferox: Schiff 3, Gunner", Meldeart.Hinweis, 6000);
        EineZeile(z, "Nicht gesendet: kein Senderecht mehr in Crew 1", "der Fehler steht");
        z.Zeigen(Entzug, "Nicht gesendet: kein Senderecht mehr in Crew 4", Meldeart.Fehler, 5000);
        EineZeile(z, "Nicht gesendet: kein Senderecht mehr in Crew 4", "derselbe Schlüssel ersetzt an seinem Platz");
        t = 5400;
        z.Takt();
        EineZeile(z, "Eingeteilt von Ferox: Schiff 3, Gunner", "die wartende mit demselben Schlüssel ist ersetzt: nur Schiff 3, nicht Schiff 2");
        z.Loesen(Umstellung);
        EineZeile(z, null, "gelöst: nichts wartet mehr");

        // Eine wartende lösen lässt die stehende stehen. Seit 0.11.1 erscheint dieselbe Meldung (Schlüssel und Text) höchstens einmal je 5 min
        // (Entprellung, OberflaecheMeldezeilePlatzTests); der Entzug in Crew 1 stand bei t = 200, darum erst nach 5 min noch einmal.
        t = 400_000;
        z.Zeigen(Entzug, "Nicht gesendet: kein Senderecht mehr in Crew 1", Meldeart.Fehler, 5000);
        z.Zeigen(Umstellung, "Eingeteilt von Ferox: Schiff 2, Gunner", Meldeart.Hinweis, 5000);
        z.Loesen(Umstellung);
        Pruefe.Falsch(z.Zeigt(Umstellung), "die wartende ist fort");
        EineZeile(z, "Nicht gesendet: kein Senderecht mehr in Crew 1", "die stehende bleibt");
        z.Loesen(Entzug);
        EineZeile(z, null, "danach leer");
    }

    [Test]
    public static void EineMeldung_GehalteneWartet_UndKommtWieder()
    {
        long t = 0;
        var z = new Meldezeile(() => t);
        z.Halten(Teilnehmermeldungen.MeldenSchluessel, "Melden: an Kommando", Meldeart.Hinweis, senden: true);
        EineZeile(z, "Melden: an Kommando", "gehalten");
        t = 100;
        z.Zeigen("entzug", "Nicht gesendet: kein Senderecht mehr in Crew 1", Meldeart.Fehler, 5000);
        EineZeile(z, "Nicht gesendet: kein Senderecht mehr in Crew 1", "der Fehler verdrängt sie");
        Pruefe.Wahr(z.Zeigt(Teilnehmermeldungen.MeldenSchluessel), "die gehaltene wartet");
        t = 5100;
        z.Takt();
        EineZeile(z, "Melden: an Kommando", "nach dem Fehler wieder da, solange gehalten");
        Pruefe.Wahr(z.Zeilen[0].Senden, "weiter eine Zeile über das eigene Senden (grün, Q41)");
        t = 5200;
        z.Zeigen(Umstellung, "Eingeteilt von Ferox: Schiff 2, Gunner", Meldeart.Hinweis, 2000);
        EineZeile(z, "Eingeteilt von Ferox: Schiff 2, Gunner", "gleicher Rang verdrängt sie auch");
        t = 7200;
        z.Takt();
        EineZeile(z, "Melden: an Kommando", "und sie kommt wieder");
        t = 600_000;
        z.Takt();
        EineZeile(z, "Melden: an Kommando", "gehalten läuft nicht ab");
        z.Loesen(Teilnehmermeldungen.MeldenSchluessel);
        EineZeile(z, null, "losgelassen: leer");

        // Losgelassen, während sie wartet: Sie kommt nicht wieder.
        z.Halten(Teilnehmermeldungen.MeldenSchluessel, "Melden: an Kommando", Meldeart.Hinweis, senden: true);
        z.Zeigen("entzug", "Nicht gesendet: kein Senderecht mehr in Crew 1", Meldeart.Fehler, 1000);
        z.Loesen(Teilnehmermeldungen.MeldenSchluessel);
        t = 601_000;
        z.Takt();
        EineZeile(z, null, "losgelassen hinter dem Fehler: danach leer");
    }

    /// <summary>
    /// 0.11.2 (Sarevoc 13.10.2026: "ich will nur eine Overlay-Meldung, wenn ein Admin oder Raidleiter die absetzt oder einer funkt"): Über dem Spiel
    /// erscheint nur, was <see cref="Meldezeile.Erlaubt"/> nennt (Umstellung, Ansage, Entzug, Melden, Phase, Probe). Mikro stumm, Dauersender,
    /// Urteil des Wächters, Sendekanal, Rauschfilter, der Hinweis zur Ablage und "es wird nichts gesendet" fallen still weg, gezeigt wie gehalten,
    /// und verdrängen keine stehende Meldung.
    /// </summary>
    [Test]
    public static void EineMeldung_NurErlaubteSchluessel_AndereFallenStillWeg()
    {
        Pruefe.Gleich("ansage|entzug|melden|phase|probe|umstellung", string.Join("|", Meldezeile.Erlaubt.Order(StringComparer.Ordinal)), "genau diese sechs Schlüssel");
        long t = 0;
        var z = new Meldezeile(() => t);
        foreach (var schluessel in Meldezeile.Erlaubt.Order(StringComparer.Ordinal))
        {
            t += 1000;
            z.Zeigen(schluessel, "Meldung zu " + schluessel, Meldeart.Hinweis, 500, immer: true);
            EineZeile(z, "Meldung zu " + schluessel, schluessel + " erscheint");
            t += 1000;
            z.Takt();
            EineZeile(z, null, schluessel + " abgelaufen");
        }
        z.Halten(Melden, "Melden: an Kommando", Meldeart.Hinweis, senden: true);
        EineZeile(z, "Melden: an Kommando", "Melden gehalten erscheint");
        z.Loesen(Melden);

        string[] weg = [MikroViewModel.Schluessel, DauersendViewModel.TasteSchluessel, DauersendViewModel.UrteilSchluessel, Teilnehmermeldungen.SendenetzSchluessel,
            "rauschfilter", FunkfeldAblage.HinweisSchluessel, FunkfeldViewModel.HinweisSchluessel];
        foreach (var schluessel in weg)
        {
            Pruefe.Falsch(Meldezeile.Erlaubt.Contains(schluessel), schluessel + " ist nicht erlaubt");
            z.Zeigen(schluessel, "Meldung zu " + schluessel, Meldeart.Hinweis, 5000, immer: true);
            EineZeile(z, null, schluessel + " fällt still weg (Hinweis)");
            z.Zeigen(schluessel, "Fehler zu " + schluessel, Meldeart.Fehler, 5000);
            EineZeile(z, null, schluessel + " fällt still weg, auch als Fehler");
            z.Halten(schluessel, "gehalten: " + schluessel, Meldeart.Hinweis);
            EineZeile(z, null, schluessel + " fällt still weg, auch gehalten");
            Pruefe.Falsch(z.Zeigt(schluessel), schluessel + " wartet auch nicht");
        }

        // Eine stehende erlaubte Meldung bleibt stehen, was auch immer sonst ankommt.
        z.Zeigen(Umstellung, "Eingeteilt von Ferox: Schiff 1, Gunner", Meldeart.Hinweis, 5000, immer: true);
        foreach (var schluessel in weg) z.Zeigen(schluessel, "Fehler zu " + schluessel, Meldeart.Fehler, 5000);
        EineZeile(z, "Eingeteilt von Ferox: Schiff 1, Gunner", "nichts Unerlaubtes verdrängt die Umstellung");
    }
}
