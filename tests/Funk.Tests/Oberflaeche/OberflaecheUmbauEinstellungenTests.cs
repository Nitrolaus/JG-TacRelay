// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// 0.10.0, AP3 (Einstellungen nach der Mischung, Q87, Bilder m-4 und m-5; E2, E3, E6, E11 bis E19, E21, E22): die Seite Einstellungen mit der Karte
/// "Ich" oben und den Abschnitten Tasten, Empfangen, Senden, Funkfeld und Darstellung und Fehlersuche, je mit dem Kasten "Läuft von selbst" (Fehlersuche
/// mit einem Satz) und den zugeklappten Feinheiten; "Zurück auf Werkseinstellung" je Abschnitt nur für das, was dazugehört (Gelerntes und Werte am
/// Konto bleiben); der Klappzustand über den Neuaufbau; die Echounterdrückung in drei Stufen; "Beim Start verbinden" nach der Freigabe; "Leises
/// Reden aussperren" ohne Wirkung; ein Knopf "Speichern" unter Ich; die Tasten der Einsatzleitung nur mit Recht; "Genau einstellen ..."; genau eine
/// Hörprobe im Fenster; der Klangtreiber unter Fehlersuche, die Warnungen beim Start in der Fußzeile. Gezeichnet ohne Fensterhandle (<see cref="Bildlauf"/>)
/// bei 1100, 960, 760 und 640 mit Kontrast, Glasschatten und <see cref="Ansichtspruefung.NichtsAbgeschnitten"/>. Der Klassenname steht nach
/// OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheUmbauEinstellungenTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    // ------------------------------------------------------------------------------------------------------------------------------
    // Modell
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reihenfolge und Kennungen der Abschnitte (m-4), ab Werk alle Feinheiten zu, die Kästen "Läuft von selbst" (nicht bei Ich und Fehlersuche, dort
    /// ein Satz), die Kopfzeilen vorn mit dem Stand. Seit 0.11.9 steht zwischen Funkfeld und Darstellung und Fehlersuche die Sprecheranzeige (bis
    /// 0.11.8 prüfte der Test, dass es sie noch nicht gibt). Texte mit einfachen Zeichen, die Hovertexte ohne Zahlen.
    /// </summary>
    [Test]
    public static void Umbau_Abschnitte_ReihenfolgeKaestenUndKopfzeilen()
    {
        using var a = new Aufbau();
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var e = h.Einstellungen;
        Pruefe.Gleich("Ich, Tasten, Empfangen, Senden, Funkfeld und Darstellung, Sprecheranzeige, Meldungen, Fehlersuche", string.Join(", ", e.Abschnitte.Select(x => x.Name)),
            "Reihenfolge (m-4, seit 0.11.9 mit der Sprecheranzeige, seit 0.12.0 mit den Meldungen)");
        Pruefe.Gleich("ich tasten empfangen senden funkfeld sprecheranzeige meldungen fehlersuche", string.Join(" ", e.Abschnitte.Select(x => x.Kennung)), "Kennungen (E6)");
        Pruefe.Wahr(e.Abschnitte.All(x => !x.Feinheiten.Offen), "ab Werk alle Feinheiten zu");
        Pruefe.Wahr(e.AbschnittIch is { Automatik: null, Satz: null }, "Ich ohne Kasten");
        Pruefe.Wahr(e.AbschnittFehlersuche is { Automatik: null, Satz: EinstellungenViewModel.SatzFehlersuche }, "Fehlersuche ohne Kasten, mit Satz");
        foreach (var x in new[] { e.AbschnittTasten!, e.AbschnittEmpfangen!, e.AbschnittSenden!, e.AbschnittFunkfeld!, e.AbschnittSprecheranzeige!, e.AbschnittMeldungen! })
            Pruefe.Wahr(x.Automatik?.StartsWith("Läuft von selbst: ", StringComparison.Ordinal) == true && x.Satz is null, x.Name + ": Kasten");
        Pruefe.Gleich(EinstellungenViewModel.AutomatikSenden, e.AbschnittSenden!.Automatik, "Senden: ein Satz (Prüfbericht Mischung, Punkt 6)");
        foreach (var x in e.Abschnitte)
        {
            var f = x.Feinheiten;
            // 0.11.1 (Sarevoc 12.10.2026): "Erweitert:" vorn (fett in der Ansicht), dann der Stand in Klammern und der Inhalt; bis 0.11.0 hieß der Bereich
            // anders und der Stand stand vor dem Doppelpunkt. Der Satz sagt "unter Erweitert", weil unter Senden Schalter offen darüber stehen.
            Pruefe.Gleich("Erweitert: (ab Werk) " + f.Inhalt, f.Kopfzeile, x.Name + ": ab Werk");
            Pruefe.Gleich("(ab Werk) " + f.Inhalt, f.Kopfrest, x.Name + ": was nach dem fetten Wort steht");
            Pruefe.Gleich("Alles unter Erweitert steht auf der Werkseinstellung.", f.StandSatz, x.Name + ": Satz");
            Pruefe.Falsch(f.ZurueckCommand.CanExecute(null), x.Name + ": Zurück gesperrt ab Werk");
            Pruefe.Falsch(f.ZurueckHilfe.Any(char.IsDigit) || f.KopfHilfe.Any(char.IsDigit), x.Name + ": Hovertexte ohne Zahlen");
            foreach (var text in new[] { x.Name, f.Inhalt, f.Kopfzeile, f.StandSatz, f.KopfHilfe, f.ZurueckHilfe, x.Automatik ?? "", x.Satz ?? "" })
                OberflaecheFunkfeldAnsichtenTests.NurEinfacheZeichen(text, x.Name);
        }
        Pruefe.Gleich("Zeichen und Sichtbarkeit", e.AbschnittIch!.Feinheiten.Inhalt, "Erweitert unter Ich (E14)");
        Pruefe.Gleich("Feste Verstärkung, Mithören, Klangfarbe", e.AbschnittSenden!.Feinheiten.Inhalt, "Senden > Erweitert seit 0.11.1 (die Schalter stehen offen)");
        Pruefe.Gleich("Klangprotokoll, Ordner der Einstellungen, Klangtreiber, Stimmproben", e.AbschnittFehlersuche!.Feinheiten.Inhalt, "Fehlersuche");
        Pruefe.Gleich("Klang: Attrappe", e.Klangtreiber, "der Klangtreiber steht unter Fehlersuche (E13)");
        // Seit dem Befund der Prüfung auch die neuen Hovertexte auf Start, an der Statuszeile und an den Rollen (bis dahin nur die unter Einstellungen).
        foreach (var hilfe in new[] { EinstellungenViewModel.KanalLautstaerkeHilfe, EinstellungenViewModel.MithoerenHilfe, EinstellungenViewModel.GenauEinstellenHilfe,
                     EinstellungenViewModel.VerbindenBeimStartHilfe, AudioViewModel.EchoAutomatischHilfe, AudioViewModel.EchoAnHilfe, AudioViewModel.EchoAusHilfe,
                     MeinProfilViewModel.SpeichernHilfe, StartViewModel.AendernHilfe, StartViewModel.BelegenHilfe, StartViewModel.SpaeterHilfe,
                     StartViewModel.AudioStartenHilfe, StartViewModel.HoerprobeHilfe, StartViewModel.WarteFeldHilfe, AudioViewModel.HeadsetHilfe,
                     AudioViewModel.LautsprecherHilfe, AudioViewModel.FunkLautstaerkeHilfe, StatuszeileViewModel.Hilfe, RollenWunsch.WunschHilfe })
        {
            Pruefe.Wahr(hilfe.Length >= 200, "ausführlicher Hovertext: " + hilfe[..30]);
            Pruefe.Falsch(hilfe.Any(char.IsDigit), "ohne Zahlen: " + hilfe[..30]);
            OberflaecheFunkfeldAnsichtenTests.NurEinfacheZeichen(hilfe, "Hovertext");
        }
        // Kurze Texte (Abbrechen, Sperrgründe, Stern) ohne Mindestlänge, aber mit denselben Zeichenregeln.
        foreach (var kurz in new[] { StartViewModel.AbbrechenHilfe, RollenWunsch.VomAdminGrund, RollenWunsch.ErsteWahlHilfe, LautstaerkeViewModel.NurOhneAutomatik,
                     LautstaerkeViewModel.OhneMikrofon, HauptViewModel.SprachaktivierungGesperrt })
        {
            Pruefe.Falsch(kurz.Any(char.IsDigit), "ohne Zahlen: " + kurz[..30]);
            OberflaecheFunkfeldAnsichtenTests.NurEinfacheZeichen(kurz, "Hovertext");
        }
        funkfeld.Dispose();
    }

    /// <summary>
    /// "Zurück auf Werkseinstellung" je Abschnitt (E6) stellt nur zurück, was dazugehört; Gelerntes (Einmessen, gelernte Verstärkung, die Abstände der
    /// Mindest-Funklautstärke) und Werte am Konto bleiben. Die Kopfzeile zählt, was selbst gestellt ist, der Satz nennt es.
    /// </summary>
    [Test]
    public static void Umbau_Feinheiten_ZurueckJeAbschnitt_NurWasDazugehoert()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        a.Ablage.Aktuell.Audio.Echounterdrueckung = false;
        var h = a.Haupt(out var funkfeld, out var schwelle, out var audio, out var netze);
        var e = h.Einstellungen;
        try
        {
            // Gelerntes, das bleiben muss.
            a.Ablage.Aktuell.Gate.MindestAbstandDb = 14;
            a.Ablage.Aktuell.Gate.MindestEingemessen = true;
            a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb = 6;

            // Senden: vier Werte, davon zwei offen über dem Klappbereich und einer auf der Seite Sprachaktivierung. Seit 0.11.1 zählt Erweitert nur,
            // was darin steht (Klangfarbe); Echoschutz und Echounterdrückung stehen offen und zeigen ihren Stand selbst (bis 0.11.0: "4 selbst
            // gestellt"), und die genau eingestellte Sprachaktivierung zählt seit Q117 (Sarevoc 13.10.2026, "nein") nicht mehr mit: Ihre Seite hat ihre
            // eigenen Standardwerte (bis dahin "2 selbst gestellt: Wärme +2 dB, Sprachaktivierung genau eingestellt").
            audio.Klangfarbe.Waerme = 2;
            audio.Echoschutz = false;
            audio.EchoAn = true;
            schwelle.Automatik = false;
            var senden = e.AbschnittSenden!.Feinheiten;
            Pruefe.Gleich("Erweitert: (1 selbst gestellt) " + senden.Inhalt, senden.Kopfzeile, "Senden zählt nur die Wärme (Q117)");
            Pruefe.Gleich("1 selbst gestellt: Wärme +2 dB. Alles andere steht ab Werk.", senden.StandSatz, "der Satz nennt nur, was unter Erweitert steht");
            Pruefe.Wahr(senden.ZurueckHilfe.Contains(EinstellungenViewModel.SendenZurueckZusatz, StringComparison.Ordinal)
                && senden.ZurueckHilfe.Contains("was offen darüber steht, bleibt", StringComparison.Ordinal), "der Hovertext am Knopf nennt die Seite Sprachaktivierung: " + senden.ZurueckHilfe);
            Pruefe.Wahr(e.Abschnitte.Where(x => x != e.AbschnittSenden).All(x => x.Feinheiten.ZurueckZusatz is null && !x.Feinheiten.ZurueckHilfe.Contains("Seite Sprachaktivierung", StringComparison.Ordinal)),
                "nur unter Senden steht der Satz zur Seite Sprachaktivierung");
            // Tasten, Empfangen, Funkfeld und Darstellung, Ich, Fehlersuche.
            e.Tasten.Nachlauf.NachlaufMs = 200;
            audio.Klick.EigeneAn = false;
            netze.Felder[0].Regelung!.LautstaerkeDb = -6;
            netze.Felder[1].Ohr!.Links = true;
            audio.StufeFastWeg = true;
            e.Durchsichtigkeit = 30;
            e.Hilfetexte = false;
            h.MeinProfil.ZeichenNebenNamen = false;
            h.MeinProfil.ZeichenZeigen = false;
            audio.Lautstaerke.Klangprotokoll = true;
            Pruefe.Gleich("Nachlauf 200 ms, Klick bei deiner Taste aus", string.Join(", ", e.AbschnittTasten!.Feinheiten.SelbstGestellt), "Tasten");
            Pruefe.Gleich("Lautstärke in 1 Kanal, links oder rechts in 1 Kanal, Dauersender höchstens fast weg", string.Join(", ", e.AbschnittEmpfangen!.Feinheiten.SelbstGestellt),
                "Empfangen (lokal je Kanal; hören je Kanal und Pegel je Person stehen am Konto)");
            Pruefe.Gleich("Durchsichtigkeit 30 %, Tooltips aus", string.Join(", ", e.AbschnittFunkfeld!.Feinheiten.SelbstGestellt), "Funkfeld und Darstellung");
            Pruefe.Gleich("Zeichen neben Namen aus", string.Join(", ", e.AbschnittIch!.Feinheiten.SelbstGestellt), "Ich: "
                + "\"Mein Zeichen anderen zeigen\" steht am Konto und zählt nicht");
            Pruefe.Gleich("Klangprotokoll an", string.Join(", ", e.AbschnittFehlersuche!.Feinheiten.SelbstGestellt), "Fehlersuche");

            senden.ZurueckCommand.Execute(null);
            Pruefe.Wahr(audio.Klangfarbe.Neutral, "Senden > Erweitert zurück");
            Pruefe.Wahr(!audio.Echoschutz && audio.EchoAn && a.Maschine.Echounterdrueckung, "die offenen Schalter bleiben, wie sie stehen (0.11.1)");
            Pruefe.Falsch(schwelle.Automatik, "die Seite Sprachaktivierung bleibt, wie sie ist: Sie hat ihre eigenen Standardwerte (Q117; bis dahin stellte der Knopf sie zurück)");
            schwelle.Automatik = true;
            audio.Echoschutz = true;
            audio.EchoAutomatisch = true;
            Pruefe.Falsch(a.Maschine.Echounterdrueckung, "Echounterdrückung wieder automatisch: mit Headset aus");
            Pruefe.Wahr(a.Ablage.Aktuell.Gate is { MindestAbstandDb: 14, MindestEingemessen: true } && a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb == 6,
                "Gelerntes bleibt (Abstand der Mindest-Funklautstärke, eingemessen, gelernte Verstärkung)");
            Pruefe.Gleich(0, senden.AnzahlSelbstGestellt, "Senden ab Werk");
            Pruefe.Wahr(e.Tasten.Nachlauf.NachlaufMs == 200 && e.Durchsichtigkeit == 30 && audio.Lautstaerke.Klangprotokoll && !h.MeinProfil.ZeichenNebenNamen,
                "die anderen Abschnitte unberührt");

            e.AbschnittTasten.Feinheiten.ZurueckCommand.Execute(null);
            Pruefe.Wahr(e.Tasten.Nachlauf.NachlaufMs == TastenEinstellungen.NachlaufAbWerkMs && audio.Klick.EigeneAn, "Tasten zurück");
            e.AbschnittEmpfangen.Feinheiten.ZurueckCommand.Execute(null);
            Pruefe.Wahr(netze.Felder[0].Regelung!.LautstaerkeDb == 0 && netze.Felder[1].Ohr!.Beide && audio.StufeDeutlichLeiser, "Empfangen zurück");
            Pruefe.Wahr(a.Ablage.Aktuell.Netze.Values.All(n => n is null || (n.LautstaerkeDb == 0 && n.Panorama == 0)), "auch in den Einstellungen je Kanal");
            e.AbschnittFunkfeld.Feinheiten.ZurueckCommand.Execute(null);
            Pruefe.Wahr(e.Durchsichtigkeit == 0 && e.Hilfetexte && Glasdesign.Hilfe.Zeigen, "Funkfeld und Darstellung zurück");
            e.AbschnittIch.Feinheiten.ZurueckCommand.Execute(null);
            Pruefe.Wahr(h.MeinProfil.ZeichenNebenNamen && !h.MeinProfil.ZeichenZeigen, "Ich zurück; der Wert am Konto bleibt");
            e.AbschnittFehlersuche.Feinheiten.ZurueckCommand.Execute(null);
            Pruefe.Falsch(audio.Lautstaerke.Klangprotokoll, "Fehlersuche zurück");
            Pruefe.Wahr(e.Abschnitte.All(x => x.Feinheiten.AnzahlSelbstGestellt == 0), "alles ab Werk");
        }
        finally
        {
            Glasdesign.Hilfe.Zeigen = true;
            funkfeld.Dispose();
        }
    }

    /// <summary>Der Klappzustand jedes Abschnitts bleibt über den Neuaufbau; alte Kennungen aus 0.9.3 werden einmal umgedeutet, Fremdes stört nicht.</summary>
    [Test]
    public static void Umbau_Feinheiten_OffenGemerkt_UeberDenNeuaufbau()
    {
        using var a = new Aufbau();
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        h.Einstellungen.AbschnittSenden!.Feinheiten.Offen = true;
        h.Einstellungen.AbschnittFehlersuche!.Feinheiten.Offen = true;
        a.Ablage.Sichern();
        funkfeld.Dispose();
        var neu = a.Haupt(out var funkfeld2, out _, out _, out _);
        Pruefe.Gleich("senden fehlersuche", string.Join(" ", neu.Einstellungen.Abschnitte.Where(x => x.Feinheiten.Offen).Select(x => x.Kennung)), "über den Neuaufbau");
        Pruefe.Gleich("senden,fehlersuche", string.Join(",", a.Speicher.Laden().Anzeige.OffeneFeinheiten), "in der Datei");
        funkfeld2.Dispose();

        File.WriteAllText(a.Speicher.Pfad, "{ \"anzeige\": { \"offeneFeinheiten\": [\"klangfarbe\", \"sprechtaste\", \"sprecheranzeige\", 7] } }");
        var alt = a.Speicher.Laden();
        Pruefe.Gleich("senden,tasten,sprecheranzeige", string.Join(",", alt.Anzeige.OffeneFeinheiten), "alte Kennungen umgedeutet, eine unbekannte bleibt stehen, keine Zahl");
        Pruefe.Gleich(null, a.Speicher.LetzterFehler, "nichts beiseitegelegt");
    }

    /// <summary>
    /// E11: Echounterdrückung in drei Stufen. Automatisch folgt der Hörweise (ohne Wahl an, Headset aus, Lautsprecher an); von Hand gilt sie, egal was
    /// auf Start gewählt ist, und ein Wechsel der Hörweise ändert nur, was auf Automatisch steht. Das alte Feld trägt immer den wirksamen Wert; eine Datei
    /// von vor 0.10.0, deren Echounterdrückung nicht zur Hörweise passt, wird einmal "von Hand".
    /// </summary>
    [Test]
    public static void Umbau_Echounterdrueckung_DreistufigUndAlteDatei()
    {
        using var a = new Aufbau(FunkLage.Leer);
        var audio = a.Audio();
        Pruefe.Wahr(audio.EchoAutomatisch && audio.Echounterdrueckung && a.Maschine.Echounterdrueckung, "ab Werk Automatisch, ohne Hörweise an");
        audio.Headset = true;
        Pruefe.Wahr(audio.EchoAutomatisch && !audio.Echounterdrueckung && !a.Maschine.Echounterdrueckung, "Headset: aus");
        audio.Lautsprecher = true;
        Pruefe.Wahr(audio.Echounterdrueckung && a.Maschine.Echounterdrueckung, "Lautsprecher: an");
        var gemeldet = new List<string?>();
        audio.PropertyChanged += (_, p) => gemeldet.Add(p.PropertyName);
        audio.EchoAus = true;
        Pruefe.Wahr(audio.EchoAus && !audio.EchoAutomatisch && !a.Maschine.Echounterdrueckung && a.Ablage.Aktuell.Audio is { EchounterdrueckungVonHand: false, Echounterdrueckung: false },
            "Aus von Hand, das alte Feld trägt den wirksamen Wert");
        Pruefe.Wahr(gemeldet.Contains(nameof(AudioViewModel.EchoAus)) && gemeldet.Contains(nameof(AudioViewModel.EchoAutomatisch)) && gemeldet.Contains(nameof(AudioViewModel.EchoHinweis)),
            "die Knöpfe erfahren es");
        audio.Headset = true;
        audio.Lautsprecher = true;
        Pruefe.Falsch(a.Maschine.Echounterdrueckung, "von Hand: ein Wechsel der Hörweise ändert nichts");
        audio.EchoAutomatisch = true;
        Pruefe.Wahr(a.Maschine.Echounterdrueckung && a.Ablage.Aktuell.Audio.EchounterdrueckungVonHand is null, "Automatisch: folgt wieder den Lautsprechern");
        audio.EchoAn = true;
        audio.Headset = true;
        Pruefe.Wahr(a.Maschine.Echounterdrueckung && audio.EchoHinweis.StartsWith("Immer an", StringComparison.Ordinal), "An von Hand bleibt mit Headset an");
        a.Ablage.Sichern();
        var text = File.ReadAllText(a.Speicher.Pfad);
        Pruefe.Wahr(text.Contains("\"echounterdrueckungVonHand\": true", StringComparison.Ordinal) && text.Contains("\"echounterdrueckung\": true", StringComparison.Ordinal),
            "in der Datei: von Hand und der wirksame Wert für ältere Clients");

        var pfad = a.Ordner.Datei("alt.json");
        var speicher = new EinstellungsSpeicher(pfad);
        foreach (var (json, vonHand, wirksam, was) in new (string, bool?, bool, string)[]
                 {
                     ("{ \"audio\": { \"hoerweise\": \"Headset\", \"echounterdrueckung\": true } }", true, true, "0.9.3, Headset und von Hand an"),
                     ("{ \"audio\": { \"hoerweise\": \"Headset\", \"echounterdrueckung\": false } }", null, false, "0.9.3, Headset wie Automatisch"),
                     ("{ \"audio\": { \"hoerweise\": \"Lautsprecher\", \"echounterdrueckung\": false } }", false, false, "0.9.3, Lautsprecher und von Hand aus"),
                     ("{ \"audio\": { \"echounterdrueckung\": true } }", null, true, "ohne Hörweise, an"),
                     ("{ \"audio\": { \"hoerweise\": \"Lautsprecher\", \"echounterdrueckung\": true, \"echounterdrueckungVonHand\": \"abc\" } }", null, true, "unlesbar gilt als Automatisch"),
                     ("{ \"audio\": { \"hoerweise\": \"Headset\", \"echounterdrueckung\": true, \"echounterdrueckungVonHand\": false } }", false, false, "von Hand geht vor"),
                 })
        {
            File.WriteAllText(pfad, json);
            var l = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, was + ": kein Fehler");
            Pruefe.Gleich(vonHand, l.Audio.EchounterdrueckungVonHand, was + ": von Hand");
            Pruefe.Gleich(wirksam, l.Audio.Echounterdrueckung, was + ": wirksam");
        }
        speicher.Speichern(new ClientEinstellungen());
        Pruefe.Falsch(File.ReadAllText(pfad).Contains("echounterdrueckungVonHand", StringComparison.Ordinal), "Automatisch wird nicht geschrieben");
    }

    /// <summary>
    /// E12 (Endfassung #83): Ist das Gerät freigegeben (auch aus einer Datei vor 0.10.0), geht "Beim Start verbinden" einmal an; danach gilt der
    /// Schalter, ausgeschaltet bleibt es aus. Ohne Freigabe bleibt es aus, bis die erste Verbindung freigibt; der Schalter unter Einstellungen zieht nach.
    /// </summary>
    [Test]
    public static void Umbau_BeimStartVerbinden_NachDerFreigabeEinmalAn()
    {
        using (var alt = new Aufbau(FunkLage.Leer))
        {
            alt.Ablage.Aktuell.Verbindung.Freigegeben = true;
            alt.Ablage.Aktuell.Verbindung.BeimStartVerbinden = false;
            var h = alt.Haupt(out var ff, out _, out _, out _);
            Pruefe.Wahr(alt.Ablage.Aktuell.Verbindung is { BeimStartVerbinden: true, BeimStartVerbindenVoreingestellt: true }, "Datei vor 0.10.0, freigegeben: einmal an");
            Pruefe.Gleich(0, h.Einstellungen.AbschnittFunkfeld!.Feinheiten.AnzahlSelbstGestellt, "nach der Freigabe ist an ab Werk");
            h.Einstellungen.VerbindenBeimStart = false;
            Pruefe.Gleich("Beim Start verbinden aus", string.Join(", ", h.Einstellungen.AbschnittFunkfeld.Feinheiten.SelbstGestellt), "aus ist selbst gestellt");
            alt.Ablage.Sichern();
            Pruefe.Wahr(File.ReadAllText(alt.Speicher.Pfad).Contains("\"beimStartVerbindenVoreingestellt\": true", StringComparison.Ordinal), "in der Datei");
            ff.Dispose();
            var neu = alt.Haupt(out var ff2, out _, out _, out _);
            Pruefe.Falsch(alt.Ablage.Aktuell.Verbindung.BeimStartVerbinden, "danach bleibt aus aus");
            Pruefe.Falsch(neu.Einstellungen.VerbindenBeimStart, "auch der Schalter");
            ff2.Dispose();
        }
        using (var neu = new Aufbau(FunkLage.Leer))
        {
            var h = neu.Haupt(out var ff, out _, out _, out _);
            Pruefe.Wahr(neu.Ablage.Aktuell.Verbindung is { BeimStartVerbinden: false, BeimStartVerbindenVoreingestellt: false }, "ohne Freigabe: aus");
            Pruefe.Gleich(0, h.Einstellungen.AbschnittFunkfeld!.Feinheiten.AnzahlSelbstGestellt, "vor der Freigabe ist aus ab Werk");
            var gemeldet = new List<string?>();
            h.Einstellungen.PropertyChanged += (_, p) => gemeldet.Add(p.PropertyName);
            neu.Dienst.LageSetzen(_ => FunkDienstAttrappe.Beispiel());
            Pruefe.Wahr(neu.Ablage.Aktuell.Verbindung is { Freigegeben: true, BeimStartVerbinden: true, BeimStartVerbindenVoreingestellt: true }, "die erste Verbindung gibt frei: an");
            Pruefe.Wahr(h.Einstellungen.VerbindenBeimStart && gemeldet.Contains(nameof(EinstellungenViewModel.VerbindenBeimStart)), "der Schalter zieht nach");
            Pruefe.Gleich(0, h.Einstellungen.AbschnittFunkfeld.Feinheiten.AnzahlSelbstGestellt, "an ist jetzt ab Werk");
            ff.Dispose();
        }
        var d = new VerbindungsEinstellungen { Freigegeben = false };
        Pruefe.Falsch(d.NachFreigabeVoreinstellen() || d.BeimStartVerbinden, "nicht freigegeben: nichts");
    }

    /// <summary>E15: "Leises Reden aussperren" hat keinen Schalter mehr; die Maschine bekommt immer aus, das Feld bleibt in der Datei für ältere Clients.</summary>
    [Test]
    public static void Umbau_LeisesRedenAussperren_ImmerAus_FeldBleibt()
    {
        Pruefe.Falsch(new DaempfungWerte { LeisesRedenSperren = true }.AlsEinstellung().LeisesRedenSperren, "die Maschine bekommt immer aus");
        Pruefe.Gleich(null, typeof(AudioViewModel).GetProperty("LeisesRedenSperren"), "kein Schalter mehr");
        using var a = new Aufbau(FunkLage.Leer);
        a.Ablage.Aktuell.Daempfung.LeisesRedenSperren = true;
        var audio = a.Audio();
        Pruefe.Falsch(a.Maschine.Daempfung.LeisesRedenSperren, "beim Anlegen aus");
        audio.StufeFastWeg = true;
        Pruefe.Falsch(a.Maschine.Daempfung.LeisesRedenSperren, "auch nach einer Änderung aus");
        a.Ablage.Sichern();
        Pruefe.Wahr(File.ReadAllText(a.Speicher.Pfad).Contains("\"leisesRedenSperren\": true", StringComparison.Ordinal), "das Feld bleibt in der Datei");
    }

    /// <summary>
    /// E14: ein Knopf "Speichern" unter Ich speichert Namen und Wünsche, jeweils nur, was geändert ist; ohne Änderung sagt er es. Ein ungültiger Name
    /// hält die Wünsche nicht auf, die Fehler stehen getrennt. Ohne Verbindung gesperrt.
    /// </summary>
    [Test]
    public static async Task Umbau_Ich_SpeichernNamenUndWuensche()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var p = h.MeinProfil;
        await p.LadenAsync();
        Pruefe.Wahr(p.Aktiv && p.SpeichernCommand.CanExecute(null) && p.HatKatalog, "verbunden: Speichern geht");
        await p.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Gleich("Nichts geändert.", p.SpeichernHinweis, "ohne Änderung");
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.StartsWith("Profil", StringComparison.Ordinal) || x.StartsWith("Lieblingsrollen", StringComparison.Ordinal)), "nichts gesendet");

        p.ScName = "Nordwind";
        await p.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Profil [Nordwind]") && !a.Dienst.Aufrufe.Any(x => x.StartsWith("Lieblingsrollen", StringComparison.Ordinal)), "nur die Namen: "
            + string.Join(" | ", a.Dienst.Aufrufe));
        Pruefe.Gleich("Gespeichert.", p.SpeichernHinweis, "gespeichert");

        p.Wuensche.Single(w => w.Id == 5).Gewaehlt = true;
        await p.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Lieblingsrollen [4,5] *1") && a.Dienst.Aufrufe.Count(x => x.StartsWith("Profil", StringComparison.Ordinal)) == 1,
            "nur die Wünsche: " + string.Join(" | ", a.Dienst.Aufrufe));

        p.ScName = "x";
        p.Wuensche.Single(w => w.Id == 6).Gewaehlt = true;
        await p.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(p.NamenFehler?.Contains("kein gültiger SC-Name", StringComparison.Ordinal) == true && p.RollenFehler is null && p.SpeichernHinweis is null, "der Name scheitert: " + p.NamenFehler);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Lieblingsrollen [4,5,6] *1"), "die Wünsche gehen trotzdem");

        a.Dienst.LageSetzen(_ => FunkLage.Leer);
        Pruefe.Falsch(p.SpeichernCommand.CanExecute(null), "getrennt gesperrt");
        funkfeld.Dispose();
    }

    /// <summary>E18: Phase vor und zurück stehen unter Tasten nur, solange du die Einsatzleitung hast; belegte Tasten bleiben gemerkt.</summary>
    [Test]
    public static void Umbau_Tasten_EinsatzleitungNurMitRecht()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var t = h.Einstellungen.Tasten;
        Pruefe.Falsch(t.LeitungSichtbar, "ohne ZUWEISEN keine Tasten der Einsatzleitung");
        Pruefe.Gleich("Sprechtaste", t.Sprechen.Titel, "Gruppe Sprechtaste");
        Pruefe.Gleich("Sendekanal vor, Sendekanal zurück, Melde-Taste", string.Join(", ", t.KanalTasten.Select(z => z.Titel)), "Gruppe Kanal und Melde-Taste");
        Pruefe.Gleich("Funkfeld ablegen/holen, Dauersender stumm, Mikro stumm, Bereit", string.Join(", ", t.WeitereTasten.Select(z => z.Titel)),
            "Gruppe Weitere Tasten (seit 0.12.0 mit Bereit)");
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Rechte = [Recht.Zuweisen] } });
        Pruefe.Wahr(t.LeitungSichtbar, "mit ZUWEISEN: zu sehen");
        a.Dienst.LageSetzen(_ => FunkLage.Leer);
        Pruefe.Falsch(t.LeitungSichtbar, "getrennt: nicht zu sehen");
        funkfeld.Dispose();
    }

    /// <summary>Die Geräteliste folgt dem Ereignis der echten Klangmaschine (seit 0.10.0), auch ohne laufendes Audio; ein Fehler im Empfänger bleibt ein Hintergrundfehler.</summary>
    [Test]
    public static async Task Umbau_Geraeteereignis_DerMaschine_AuchOhneAudio()
    {
        await using var s = new Funk.Tests.Maschine.Pruefstand();
        var gemeldet = 0;
        s.Maschine.GeraeteListeGeaendert += () => gemeldet++;
        s.System.Entfernen("mic-2");
        Pruefe.Gleich(1, gemeldet, "ohne laufendes Audio gemeldet");
        await s.StartenAsync();
        s.System.StandardSetzen("mic-1", "spk-2");
        Pruefe.Gleich(2, gemeldet, "im Lauf gemeldet");
        Pruefe.Gleich("spk-2", s.System.Wiedergabe!.GeraetId, "und vorher abgeglichen");
        s.Maschine.GeraeteListeGeaendert += () => throw new InvalidOperationException("Empfänger kaputt");
        var fehler = s.Fehler.Count;
        s.System.StandardSetzen("mic-1", "spk-1");
        Pruefe.Wahr(s.Fehler.Count > fehler && gemeldet == 3, "ein Fehler im Empfänger wird als Hintergrundfehler gemeldet");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Bildlauf
    // ------------------------------------------------------------------------------------------------------------------------------

    private static ContentPresenter Seite(Hauptfenster haupt) =>
        Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);

    private static T Teil<T>(FrameworkElement seite, string name) where T : FrameworkElement =>
        Bildlauf.Nachfahren<T>(seite).FirstOrDefault(e => e.Name == name) ?? throw new PruefungException(name + " fehlt");

    /// <summary>Die Einstellungen wie nach dem Einrichten, verbunden mit dem Beispielabend, Katalog geladen.</summary>
    private static (Hauptfenster Haupt, HauptViewModel Modell, FunkfeldViewModel Funkfeld) Aufbauen(Aufbau a, List<Window> fenster, out IDisposable schluessel)
    {
        schluessel = OberflaecheStartseiteTests.Einrichten(a);
        OberflaecheStartseiteTests.SprechtasteBelegen(a);
        a.Tastenquelle = new Maschine.TastenQuelleAttrappe(Funk.Klang.Eingabe.EingabeArt.Tastatur, Funk.Klang.Eingabe.EingabeArt.Maus);
        var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
        audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        h.MeinProfil.LadenAsync().GetAwaiter().GetResult();
        h.AktiverBereich = Seiten.Einstellungen(h);
        h.Takt();
        var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
        fenster.Add(haupt);
        return (haupt, h, funkfeld);
    }

    /// <summary>
    /// Die Seite zugeklappt (m-4) bei 1100, 960, 760 und 640: Ich offen mit Namen und Speichern, alle Feinheiten zu, die Kästen neben den Namen ab
    /// <see cref="EinstellungenViewModel.KastenDanebenAb"/> (bei 1100 und 960), darunter unter ihnen (760, 640; m-4c); seit 0.11.9 mit der Sprecheranzeige. Dann
    /// alle Feinheiten offen: Senden in zwei Spalten bei 1100 und 960 (m-5), untereinander bei 760 und 640; nichts abgeschnitten, Glasschatten frei
    /// (auch bei 175 %), Kontrast in Anthrazit, die Zugriffstasten des ganzen Fensters eindeutig, auch in den Feinheiten. Höhen in der Messdatei.
    /// </summary>
    [Test]
    public static void Umbau_Einstellungen_ZuUndOffen_BreitenKontrastSchatten()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var mass = new List<string>();
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
                var (haupt, h, funkfeld) = Aufbauen(a, fenster, out var schluessel);
                using var k = schluessel;
                foreach (var (breite, daneben) in new[] { (1100.0, true), (960.0, true), (760.0, false), (640.0, false) })
                {
                    var groesse = new Size(breite, breite == 960 ? 700 : 900);
                    _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                    var seite = Seite(haupt);
                    var was = T($"{satz.Name}, zu, {breite:0}");
                    Pruefe.Wahr(ZuSehen(Teil<TextBox>(seite, "MeinScName")) && ZuSehen(Teil<Button>(seite, "SpeichernKnopf")) && Sichtbar(seite, "Rollenwünsche"),
                        was + ": Ich offen mit Namen, Wünschen und Speichern (m-4)");
                    foreach (var x in Bildlauf.Nachfahren<Expander>(seite)) Pruefe.Falsch(x.IsExpanded, was + ": " + x.Name + " zu");
                    var koepfe = Bildlauf.Nachfahren<Spaltenwechsel>(seite).Where(s => s.Name == "Kopfzeile").ToList();
                    Pruefe.Gleich(7, koepfe.Count, was + ": sieben Abschnitte mit Kopf (seit 0.11.9 mit der Sprecheranzeige, seit 0.12.0 mit den Meldungen)");
                    foreach (var kopf in koepfe)
                    {
                        Pruefe.Gleich(daneben, kopf.Nebeneinander, T($"{was}: Kasten neben dem Namen ({kopf.ActualWidth:0.0} breit, ab {EinstellungenViewModel.KastenDanebenAb:0})"));
                        var name = Bildlauf.Nachfahren<TextBlock>(kopf).First(t => t.Name == "AbschnittName");
                        var zweite = (FrameworkElement)kopf.Children[1];
                        var yName = name.TranslatePoint(new Point(0, 0), kopf).Y;
                        var yKasten = zweite.TranslatePoint(new Point(0, 0), kopf).Y;
                        if (!daneben) Pruefe.Wahr(yKasten > yName + 10, was + ": " + Inhalt(name) + ": Kasten unter dem Namen (m-4c)");
                    }
                    foreach (var kasten in new[] { EinstellungenViewModel.AutomatikTasten, EinstellungenViewModel.AutomatikEmpfangen, EinstellungenViewModel.AutomatikSenden,
                                 EinstellungenViewModel.AutomatikFunkfeld, SprecheranzeigeViewModel.AutomatikText, EinstellungenViewModel.SatzFehlersuche })
                        Pruefe.Wahr(Sichtbar(seite, kasten), was + ": " + kasten);
                    _ = NichtsAbgeschnitten(seite, was);
                    _ = OberflaecheStartseiteTests.SchattenFrei(seite, was);
                    var lauf = Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(haupt)).First(sv => sv.Content is StackPanel p && p.Children.OfType<ContentControl>().Any());
                    mass.Add(T($"MASS {was}: Inhalt {lauf.ExtentHeight:0.0}, Fenster ohne Rollen {groesse.Height - lauf.ViewportHeight + lauf.ExtentHeight:0}, Kopfzeile {koepfe[0].ActualWidth:0.0} breit"));
                    if (breite == 960)
                    {
                        // Befund der Prüfung: In der Größe ab Werk (960 x 700) sind wie in m-4b mindestens Tasten und Empfangen ohne Rollen zu sehen; bis dahin
                        // füllte die Karte Ich mit einer Zeile je Katalogrolle das ganze Fenster. Die Rollen stehen jetzt als Umschalter, die vom Admin
                        // eingetragenen nur einmal (mit Schloss), das Ergänzen des Katalogs in den Feinheiten.
                        var ich = Teil<Border>(seite, "IchKarte");
                        // Seit 0.11.4 ist jedes "Erweitert:" eine Karte mit Titel und Beschreibung (Sarevoc: Variante B, wie Windows 11), zwei Zeilen statt
                        // einer: Tasten mit seiner Karte ganz und von Empfangen Name und Kasten; die Karte von Empfangen darf unter den Rand rutschen.
                        var tastenKarte = Teil<Expander>(seite, "TastenFeinheiten");
                        var empfangen = Teil<Expander>(seite, "EmpfangenFeinheiten");
                        var tastenUnten = tastenKarte.TranslatePoint(new Point(0, tastenKarte.ActualHeight), lauf).Y;
                        var empfangenKopf = empfangen.TranslatePoint(new Point(0, 0), lauf).Y;
                        mass.Add(T($"MASS {was}: Karte Ich {ich.ActualHeight:0.0} hoch, Tasten endet bei {tastenUnten:0.0}, Empfangen-Karte beginnt bei {empfangenKopf:0.0} von {lauf.ViewportHeight:0.0}"));
                        Pruefe.Wahr(tastenUnten <= lauf.ViewportHeight + 0.5 && empfangenKopf <= lauf.ViewportHeight + 0.5,
                            T($"{was}: Tasten ganz, von Empfangen Name und Kasten ohne Rollen zu sehen (Tasten endet bei {tastenUnten:0.0}, Empfangen-Karte beginnt bei {empfangenKopf:0.0} von {lauf.ViewportHeight:0.0})"));
                        var rollen = Bildlauf.Nachfahren<ToggleButton>(Teil<ItemsControl>(seite, "RollenWahl")).Where(t => t.Name == "RolleUmschalter").ToList();
                        Pruefe.Gleich(h.MeinProfil.Wuensche.Count, rollen.Count, was + ": je Katalogrolle ein Umschalter");
                        Pruefe.Gleich(h.MeinProfil.VomAdmin.Count, rollen.Count(r => !r.IsEnabled && r.IsChecked == true), was + ": die vom Admin eingetragenen gesperrt und an");
                        Pruefe.Falsch(Sichtbar(seite, "Vom Admin eingetragen") || Sichtbar(seite, "vom Admin eingetragen"), was + ": die vom Admin eingetragenen nicht noch einmal als Liste");
                        Pruefe.Falsch(Bildlauf.Nachfahren<StackPanel>(seite).Any(p => p.Name == "KatalogTeil" && ZuSehen(p)) || Sichtbar(seite, "Rolle zum Katalog hinzufügen"),
                            was + ": das Ergänzen des Katalogs steht in den Feinheiten");
                    }
                    if (satz.Stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(Bildlauf.Zeichnen(haupt, 1, groesse).Bild, T($"umbau-einstellungen-zu-{breite:0}"));
                }

                Seiten.Feinheiten(h);
                foreach (var (breite, zweiSpalten) in new[] { (1100.0, true), (960.0, true), (760.0, false), (640.0, false) })
                {
                    var bild = Ganz(haupt, breite);
                    var seite = Seite(haupt);
                    var was = T($"{satz.Name}, offen, {breite:0}");
                    var spalten = Teil<Spaltenwechsel>(seite, "SendenSpalten");
                    mass.Add(T($"MASS {was}: Seite {seite.ActualHeight:0.0} hoch, Senden {spalten.ActualWidth:0.0} breit, zwei Spalten {spalten.Nebeneinander}"));
                    Pruefe.Gleich(zweiSpalten, spalten.Nebeneinander, T($"{was}: Senden in zwei Spalten ({spalten.ActualWidth:0.0}, ab {EinstellungenViewModel.SendenZweiSpaltenAb:0})"));
                    Pruefe.Wahr(NichtsAbgeschnitten(seite, was) >= 150, was + ": genug Elemente geprüft");
                    _ = OberflaecheStartseiteTests.SchattenFrei(seite, was);
                    if (breite == 1100) KontrastPruefen(seite, satz, was, mindestens: 150);
                    if (breite == 960)
                    {
                        _ = Bildlauf.Zeichnen(haupt, 1.75, new Size(breite, bild.PixelHeight));
                        _ = OberflaecheStartseiteTests.SchattenFrei(seite, was + ", 175 %");
                    }
                    if (satz.Stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(bild, T($"umbau-einstellungen-offen-{breite:0}"));
                }
                var tasten = OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt));
                Pruefe.Gleich("", string.Join(" | ", OberflaecheHauptfensterTests.Doppelte(tasten, "Einstellungen, alle Feinheiten offen")), satz.Name + ": Zugriffstasten eindeutig");
                Pruefe.Wahr(tasten.Select(t => t.Taste).ToHashSet().IsSupersetOf(['S', 'E', 'F', 'R', 'D', 'K', 'G', 'Z', 'N', 'A', 'V']),
                    satz.Name + ": die Zugriffstasten des Plans (" + string.Join("", tasten.Select(t => t.Taste).Distinct().Order()) + ")");
                funkfeld.Dispose();
            }
            Messdateien.Schreiben("umbau-einstellungen-mass.txt", mass);
        });
    }

    /// <summary>
    /// "Genau einstellen ..." unter Senden > Feinheiten (E3) öffnet die Seite Sprachaktivierung; ohne den Unterpunkt ist der Knopf gesperrt mit Grund.
    /// Mithören steht hier (E19), mit Headset bedienbar. Genau eine Hörprobe im ganzen Fenster, auf Start (Q75, E19).
    /// </summary>
    [Test]
    public static void Umbau_GenauEinstellen_UndEineHoerprobe()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var (haupt, h, funkfeld) = Aufbauen(a, fenster, out var schluessel);
            using var k = schluessel;
            Seiten.Feinheiten(h, Abschnittskennung.Senden);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 1900));
            var seite = Seite(haupt);
            var knopf = Teil<Button>(seite, "GenauEinstellenKnopf");
            Pruefe.Wahr(ZuSehen(knopf) && knopf.IsEnabled, "mit Unterpunkt frei");
            Pruefe.Gleich(EinstellungenViewModel.GenauEinstellenHilfe, Glasdesign.Hilfe.GetText(knopf), "Hovertext");
            var mithoeren = Teil<CheckBox>(seite, "MithoerenSchalter");
            Pruefe.Wahr(ZuSehen(mithoeren) && mithoeren.IsEnabled, "Mithören unter Senden > Erweitert, mit Headset bedienbar (E19)");
            knopf.Command.Execute(knopf.CommandParameter);
            Pruefe.Wahr(h.IstSprachaktivierungsseite, "öffnet die Seite Sprachaktivierung");

            h.Audio.SprachaktivierungAn = false;
            Pruefe.Wahr(h.AktiverBereich.Kennung == HauptViewModel.EinstellungenKennung, "die Seite verschwand: zurück zu Einstellungen");
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 1900));
            // Die Seite ist neu aufgebaut (sie war gegen die Seite Sprachaktivierung getauscht): den Knopf neu holen.
            knopf = Teil<Button>(Seite(haupt), "GenauEinstellenKnopf");
            Pruefe.Falsch(knopf.IsEnabled, "ohne Unterpunkt gesperrt");
            Pruefe.Gleich(HauptViewModel.SprachaktivierungGesperrt, knopf.ToolTip as string, "mit Grund");
            h.Audio.SprachaktivierungAn = true;

            // Genau eine Hörprobe: auf Start; nicht unter Einstellungen (auch nicht in den Feinheiten) und nicht auf der Seite Sprachaktivierung.
            int Hoerproben() => Bildlauf.Nachfahren<ButtonBase>(Bildlauf.Wurzel(haupt)).Count(b => ZuSehen(b) && (AutomationProperties.GetName(b) == "Hörprobe"
                || ReferenceEquals(b.Command, h.Audio.Hoerprobe.ProbeCommand)));
            Seiten.Feinheiten(h);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 3600));
            Pruefe.Gleich(0, Hoerproben(), "keine Hörprobe unter Einstellungen");
            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1280, 900));
            Pruefe.Gleich(0, Hoerproben(), "keine auf der Seite Sprachaktivierung (E19)");
            h.AktiverBereich = h.Bereiche[0];
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 900));
            Pruefe.Gleich(1, Hoerproben(), "genau eine, auf Start (Q75)");
            funkfeld.Dispose();
        });
    }

    /// <summary>
    /// E13: Der Klangtreiber steht unter Fehlersuche, nicht in der Fußzeile; Warnungen beim Start ("Globale Tasten nicht verfügbar: ...") stehen links in
    /// der Fußzeile, damit sie nicht zugeklappt verschwinden, und zusätzlich unter Fehlersuche.
    /// </summary>
    [Test]
    public static void Umbau_Fusszeile_StarthinweiseLinks_KlangtreiberUnterFehlersuche()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var verbindung = new VerbindungViewModel(a.Dienst, a.Ablage, a.Takt, a.Fenster, a.Profil);
            var audio = a.Audio();
            var funkfeld = a.Funkfeld();
            var netze = a.Netze(funkfeld);
            var einstellungen = new EinstellungenViewModel(a.Ablage, funkfeld, new TastenViewModel(a.Ablage, null, dienst: a.Dienst, takt: a.Takt, maschine: a.Maschine), a.Fenster);
            var schwelle = new SchwellenViewModel(a.Maschine, a.Verlauf, a.Ablage, audio.Hoerprobe, audio.Korridor);
            var h = new HauptViewModel(verbindung, audio, netze, einstellungen, funkfeld, schwelle, a.Dienst, a.Fenster, a.Ablage, a.Takt, "Klang: WASAPI",
                starthinweise: "Globale Tasten nicht verfügbar: Raw Input fehlt.");
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 900));
            var fuss = (Border)haupt.FindName("Fuss");
            var texte = string.Join(" | ", Bildlauf.Nachfahren<TextBlock>(fuss).Where(ZuSehen).Select(Inhalt));
            Pruefe.Wahr(texte.Contains("Globale Tasten nicht verfügbar", StringComparison.Ordinal), "die Warnung beim Start steht in der Fußzeile: " + texte);
            Pruefe.Falsch(texte.Contains("Klang: WASAPI", StringComparison.Ordinal), "der Klangtreiber nicht: " + texte);
            Seiten.Feinheiten(h, Abschnittskennung.Fehlersuche);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 2400));
            var seite = Seite(haupt);
            Pruefe.Wahr(Sichtbar(seite, "Klang: WASAPI") && Sichtbar(seite, "Beim Start: Globale Tasten nicht verfügbar"), "beides unter Fehlersuche > Feinheiten");
            funkfeld.Dispose();
        });
    }
}
