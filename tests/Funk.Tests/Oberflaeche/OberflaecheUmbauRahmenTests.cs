// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Eingabe;
using Funk.Klang.Maschine;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// 0.10.0, AP1 (Gerüst der Mischung, Q87 bis Q90, E1 bis E6, E23): die Statuszeile "Mikrofon" statt der Leiste der Sprachaktivierung (Pegel,
/// Marke SENDET, Ruhe oder Getrennt, fünf Hinweissätze, auch die zwei neuen), Strg+T öffnet die Seite Sprachaktivierung, die Titelleiste in
/// schmalen Fenstern (Zeichen und Zahl unter 900, ohne "spricht: ..." unter 700), der Behälter der Feinheiten und das Panel
/// <see cref="Spaltenwechsel"/>. Gezeichnet ohne Fensterhandle (<see cref="Bildlauf"/>). Der Klassenname steht nach OberflaecheFensterTests
/// (Bildläufe danach; nicht "OberflaecheFeinheiten...", das sortierte davor).
/// </summary>
internal static class OberflaecheUmbauRahmenTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>
    /// Die Hinweissätze der Statuszeile (E4, m-8). Bis 0.11.1 fünf; seit 0.11.2 (Sarevoc 13.10.2026: "raus heißt raus") gibt es die drei zu Ruhe,
    /// Stummschaltung und Trennung ("... es wird nichts gesendet") nicht mehr.
    /// </summary>
    private static readonly string[] Saetze =
    [
        "Kanal braucht die Sprechtaste - keine belegt",
        "Stimme unter der Mindest-Funklautstärke",
    ];

    // ------------------------------------------------------------------------------------------------------------------------------
    // Modell
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Der Verlauf kennt seit 0.10.0 zwei neue Lagen: einen Kanal auf Taste ohne belegte Sprechtaste (<see cref="Sendeweg.OhneSprechtaste"/>: gelb
    /// und der Satz, außer die Maschine sendet doch, etwa über die Melde-Taste) und eine Phase, die das Gate unter der Mindest-Funklautstärke
    /// zurückhält (<see cref="PegelMeldung.Zurueckgehalten"/>: grau, der Satz, nach <see cref="PegelVerlauf.HinweisHaltMs"/> weg). Ein nicht offener
    /// Weg geht vor, bei Mikro stumm gibt es keinen Satz.
    /// </summary>
    [Test]
    public static void Umbau_Verlauf_OhneSprechtaste_UndZurueckgehalten()
    {
        var v = new PegelVerlauf();
        var zeit = 0L;
        PegelMeldung Aufnehmen(float db, float p, bool sendet, bool zurueck = false, bool stumm = false)
        {
            zeit += 20;
            v.Hinzufuegen(new PegelMeldung(zeit, db, -62, -40, p, sendet, stumm, Zurueckgehalten: zurueck));
            Pruefe.Wahr(v.Letzte(out var letzte), "abgelegt");
            return letzte;
        }

        Pruefe.Gleich("Kanal braucht die Sprechtaste - keine belegt", PegelVerlauf.HinweisText(Sendeweg.OhneSprechtaste), "Satz zum neuen Weg");
        v.Weg = Sendeweg.OhneSprechtaste;
        var m = Aufnehmen(-20, 0.95f, sendet: false);
        Pruefe.Wahr(!m.Sendet && m.WuerdeSenden, "ohne Sprechtaste, über der Linie gesprochen: gelb");
        Pruefe.Gleich(PegelVerlauf.HinweisText(Sendeweg.OhneSprechtaste), v.Hinweis(m), "und der Satz");
        m = Aufnehmen(-20, 0.95f, sendet: true);
        Pruefe.Wahr(m.Sendet && !m.WuerdeSenden, "sendet die Maschine doch (eine Taste drückt), geht es hinaus: grün");
        zeit += PegelVerlauf.HinweisHaltMs + 100;
        m = Aufnehmen(-60, 0.05f, sendet: false);
        Pruefe.Gleich<string?>(null, v.Hinweis(m), "still: der Satz geht weg");

        v.Weg = Sendeweg.Offen;
        m = Aufnehmen(-50, 0.95f, sendet: false, zurueck: true);
        Pruefe.Wahr(m.Zurueckgehalten && !m.Sendet && !m.WuerdeSenden, "zurückgehalten: grau, nicht gelb (m-8, Fall 4)");
        Pruefe.Gleich(PegelVerlauf.UnterMindestText, v.Hinweis(m), "der Satz Stimme unter der Mindest-Funklautstärke");
        m = Aufnehmen(-50, 0.95f, sendet: false);
        Pruefe.Gleich(PegelVerlauf.UnterMindestText, v.Hinweis(m), "er hält eine Weile");
        zeit += PegelVerlauf.HinweisHaltMs + 100;
        m = Aufnehmen(-60, 0.05f, sendet: false);
        Pruefe.Gleich<string?>(null, v.Hinweis(m), "nach der Haltezeit weg");
        m = Aufnehmen(-20, 0.95f, sendet: true, zurueck: true);
        Pruefe.Gleich<string?>(null, v.Hinweis(m), "geht im selben Paar schon etwas hinaus, kein Satz");
        m = Aufnehmen(-120, 0f, sendet: false, zurueck: true, stumm: true);
        Pruefe.Gleich<string?>(null, v.Hinweis(m), "Mikro stumm: kein Satz");

        // Ein nicht offener Weg geht vor: Dort ginge auch lautere Sprache nirgends hin. Seit 0.11.2 hat Ruhe keinen Satz (bis 0.11.1 "Ruhe: es wird
        // nichts gesendet"); auch der zur Mindest-Funklautstärke kommt dort nicht.
        v.Weg = Sendeweg.Ruhe;
        m = Aufnehmen(-20, 0.95f, sendet: false, zurueck: true);
        Pruefe.Gleich<string?>(null, v.Hinweis(m), "Ruhe geht vor: kein Satz");
        m = Aufnehmen(-50, 0.95f, sendet: false, zurueck: true);
        Pruefe.Gleich<string?>(null, v.Hinweis(m), "auch zurückgehalten kein Satz zur Mindest-Funklautstärke");
    }

    /// <summary>
    /// 0.11.2 (Sarevoc 13.10.2026: "die Meldung 'es wird nichts gesendet' will ich nicht ... raus heißt raus"): <see cref="PegelVerlauf.HinweisText"/> liefert
    /// nur noch für <see cref="Sendeweg.OhneSprechtaste"/> einen Satz (eine fehlende Einstellung, kein Zustand); für Ruhe, Stummschaltung durch die
    /// Leitung, Trennung und den offenen Weg null. Über der Linie gesprochen bleibt es auf diesen Wegen gelb, ohne Satz; ohne Sprechtaste gelb mit dem
    /// Satz in der Statuszeile, aber nicht in der Meldezeile über dem Spiel (das Funkfeld legt dort keinen Hinweis mehr ab).
    /// </summary>
    [Test]
    public static void Verlauf_HinweisText_NurOhneSprechtaste_NieInDerMeldezeile()
    {
        Pruefe.Gleich("Kanal braucht die Sprechtaste - keine belegt", PegelVerlauf.HinweisText(Sendeweg.OhneSprechtaste), "ohne Sprechtaste: der Satz");
        foreach (var weg in new[] { Sendeweg.Offen, Sendeweg.Ruhe, Sendeweg.Leitung, Sendeweg.Getrennt })
            Pruefe.Gleich<string?>(null, PegelVerlauf.HinweisText(weg), weg + ": kein Satz");
        Pruefe.Falsch(Enum.GetValues<Sendeweg>().Any(w => PegelVerlauf.HinweisText(w)?.Contains("nichts gesendet", StringComparison.Ordinal) == true),
            "\"es wird nichts gesendet\" gibt es nirgends");
        var v = new PegelVerlauf();
        var zeit = 0L;
        foreach (var weg in new[] { Sendeweg.Ruhe, Sendeweg.Leitung, Sendeweg.Getrennt, Sendeweg.OhneSprechtaste })
        {
            v.Weg = weg;
            zeit += 5000;
            v.Hinzufuegen(new PegelMeldung(zeit, -20, -62, -40, 0.95f, false));
            Pruefe.Wahr(v.Letzte(out var m) && m.WuerdeSenden && !m.Sendet, weg + ": über der Linie gesprochen: gelb");
            Pruefe.Gleich(weg == Sendeweg.OhneSprechtaste ? PegelVerlauf.HinweisText(weg) : null, v.Hinweis(m), weg + ": ein Satz nur ohne Sprechtaste");
        }

        // Ohne Sprechtaste: der Satz in der Statuszeile, nicht über dem Spiel (bis 0.11.1 dort unter dem Schlüssel "nichts-gesendet").
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var zeile = new Meldezeile(() => a.Uhr);
        funkfeld.Meldezeile = zeile;
        funkfeld.Kacheln.Single(k => k.IstSenden).Sprachaktivierung = false;
        Pruefe.Gleich(Sendeweg.OhneSprechtaste, a.Verlauf.Weg, "Kanal auf Taste, keine Sprechtaste");
        for (var i = 0; i < 3; i++)
        {
            a.Uhr += 20;
            a.Maschine.Melden(new PegelMeldung(a.Uhr, -20, -62, -40, 0.95f, false));
        }
        a.Uhr += 100;
        h.Takt();
        Pruefe.Gleich(PegelVerlauf.HinweisText(Sendeweg.OhneSprechtaste), h.Statuszeile.Hinweis, "die Statuszeile nennt den Satz");
        Pruefe.Falsch(zeile.Sichtbar || zeile.Zeigt(FunkfeldViewModel.HinweisSchluessel), "die Meldezeile über dem Spiel nicht");
        funkfeld.Dispose();
    }

    /// <summary>
    /// Das Funkfeld rechnet den neuen Weg: Ein gewählter Kanal auf Taste ohne belegte Sprechtaste ergibt <see cref="Sendeweg.OhneSprechtaste"/>; mit
    /// Sprechtaste oder auf Sprachaktivierung bleibt er offen. Jede Änderung der Tasten rechnet neu (EinstellungenViewModel).
    /// </summary>
    [Test]
    public static void Umbau_Funkfeld_KanalAufTasteOhneSprechtaste()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var einstellungen = (EinstellungenViewModel)Seiten.Einstellungen(h).Inhalt;
        var crew = funkfeld.Kacheln.Single(k => k.IstSenden);
        Pruefe.Wahr(crew.Sprachaktivierung && a.Maschine.Sprachaktivierung, "Beispiel: Crew gewählt, auf Sprachaktivierung");
        Pruefe.Wahr(einstellungen.Tasten.OhneSprechtaste, "ab Werk keine Sprechtaste (E8)");
        Pruefe.Gleich(Sendeweg.Offen, a.Verlauf.Weg, "Sprachaktivierung: offen, auch ohne Sprechtaste");

        crew.Sprachaktivierung = false;
        Pruefe.Falsch(a.Maschine.Sprachaktivierung, "Crew auf Taste");
        Pruefe.Gleich(Sendeweg.OhneSprechtaste, a.Verlauf.Weg, "Kanal auf Taste, keine Sprechtaste: geht nirgends hin");

        einstellungen.Tasten.Hinzufuegen(Tastenfunktion.Sprechen, Tastenbelegung.Taste(0x39));
        Pruefe.Gleich(Sendeweg.Offen, a.Verlauf.Weg, "Sprechtaste belegt: die Tastenänderung rechnet neu, offen");
        einstellungen.Tasten.Loeschen(einstellungen.Tasten.Funktionen[0].Belegungen[0].Zuordnung);
        Pruefe.Gleich(Sendeweg.OhneSprechtaste, a.Verlauf.Weg, "wieder entfernt: wieder ohne");

        // Klangmodul Sprachaktivierung aus: jeder Kanal sendet nur mit Taste, die Sprechtaste fehlt.
        crew.Sprachaktivierung = true;
        Pruefe.Gleich(Sendeweg.Offen, a.Verlauf.Weg, "Crew wieder auf Sprachaktivierung");
        h.Audio.SprachaktivierungAn = false;
        Pruefe.Gleich(Sendeweg.OhneSprechtaste, a.Verlauf.Weg, "Senden mit Stimme aus: ohne Sprechtaste geht nichts hinaus");
        funkfeld.Dispose();
    }

    /// <summary>
    /// Befund der Prüfung: "Stimme unter der Mindest-Funklautstärke" steht in der Statuszeile und am Pegelbalken, aber nicht in der Meldezeile über
    /// dem Spiel. Die Mindest-Funklautstärke hält leises Reden im Raum draußen, wie sie soll; jedes Gespräch im Raum blendete den Satz sonst über dem
    /// Spiel ein. Bis 0.11.1 standen die Sätze zu Fehlbedienungen (Ruhe, ein Kanal auf Taste ohne Sprechtaste) dort weiter; seit 0.11.2 steht über dem
    /// Spiel keiner dieser Sätze mehr (Meldezeile.Erlaubt), zu Ruhe gibt es gar keinen.
    /// </summary>
    [Test]
    public static void Umbau_Meldezeile_OhneSatzZurMindestFunklautstaerke()
    {
        using var a = new Aufbau(FunkDienstAttrappe.KanalBeispiel() with { SendeFelder = [3], GlobalSenden = false });
        OberflaecheStartseiteTests.SprechtasteBelegen(a);
        var h = a.Haupt(out var f, out _, out var audio, out _);
        var zeile = new Meldezeile(() => a.Uhr);
        f.Meldezeile = zeile;
        void Melden(float db, bool zurueck)
        {
            for (var i = 0; i < 3; i++)
            {
                a.Uhr += 20;
                a.Maschine.Melden(new PegelMeldung(a.Uhr, db, -62, -40, 0.95f, false, Zurueckgehalten: zurueck));
            }
            a.Uhr += 100;
            h.Takt();
        }
        Pruefe.Gleich(Sendeweg.Offen, a.Verlauf.Weg, "ein echter Kanal: offen");
        Melden(-50, zurueck: true);
        Pruefe.Gleich(PegelVerlauf.UnterMindestText, h.Statuszeile.Hinweis, "Statuszeile: der Satz");
        Pruefe.Gleich(PegelVerlauf.UnterMindestText, audio.NichtsGesendet, "Pegelbalken unter Senden: der Satz");
        Pruefe.Falsch(zeile.Zeilen.Any(z => z.Text == PegelVerlauf.UnterMindestText), "Meldezeile über dem Spiel: kein Satz");

        a.Uhr += PegelVerlauf.HinweisHaltMs * 3;
        a.Dienst.LageSetzen(x => x with { SendeFelder = [4] });
        Melden(-20, zurueck: false);
        Pruefe.Gleich(Sendeweg.Ruhe, a.Verlauf.Weg, "Ruhe");
        Pruefe.Falsch(zeile.Sichtbar, "Ruhe: seit 0.11.2 nichts in der Meldezeile");
        f.Dispose();
    }

    /// <summary>
    /// Die Statuszeile (E4): Marke Ruhe, SENDET oder Getrennt (Q90), Pegel und Linie aus der Meldung, Mikro stumm am Balken, der Hinweissatz aus dem
    /// Verlauf; ohne Verbindung "Getrennt" und der Satz "Nicht verbunden". Dazu der kurze Knopf "Wer ist da" (E23). Seit dem Befund der Prüfung steht
    /// die Linie vor der ersten Meldung, wo die Sprachaktivierung sie hat, nicht am linken Rand.
    /// </summary>
    [Test]
    public static void Umbau_Statuszeile_MarkeSatzUndStumm()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var s = h.Statuszeile;
        h.Takt();
        Pruefe.Gleich(StatusMarke.Ruhe, s.Marke, "verbunden, still: Ruhe");
        Pruefe.Gleich("Ruhe", s.MarkenText, "Text der Marke");
        Pruefe.Wahr(s.LinieDb > GateWerte.MinDb + 1 && Math.Abs(s.LinieDb - h.Schwelle.LinieDb) < 1e-9,
            T($"vor der ersten Meldung die Linie der Sprachaktivierung ({s.LinieDb:0.0}), nicht am Rand"));
        Pruefe.Wahr(StatuszeileViewModel.Hilfe.Length >= 200 && !StatuszeileViewModel.Hilfe.Any(char.IsDigit), "Hovertext der Statuszeile ausführlich, ohne Zahlen");
        OberflaecheFunkfeldAnsichtenTests.NurEinfacheZeichen(StatuszeileViewModel.Hilfe, "Statuszeile");
        a.Uhr += 20;
        a.Maschine.Melden(new PegelMeldung(a.Uhr, -21, -62, -45, 0.95f, true));
        h.Takt();
        Pruefe.Wahr(s is { Marke: StatusMarke.Sendet, MarkenText: "SENDET", Sendet: true, PegelDb: -21, LinieDb: -45, Hinweis: null }, "es geht hinaus: SENDET, Pegel und Linie");

        a.Dienst.LageSetzen(_ => FunkLage.Leer);
        h.Takt();
        Pruefe.Gleich(StatusMarke.Getrennt, s.Marke, "nicht verbunden: Getrennt (Q90)");
        Pruefe.Gleich("Getrennt", s.MarkenText, "Text der Marke");
        a.Uhr += 20;
        a.Maschine.Melden(new PegelMeldung(a.Uhr, -21, -62, -45, 0.95f, false));
        h.Takt();
        // Seit 0.11.2 ohne Satz (bis 0.11.1 "Nicht verbunden: es wird nichts gesendet"); gelb sagt, dass nichts hinausgeht.
        Pruefe.Wahr(s is { WuerdeSenden: true, Sendet: false, Hinweis: null }, "gesprochen: gelb, ohne Satz (" + s.Hinweis + ")");

        a.Maschine.MikroStumm = true;
        h.Takt();
        Pruefe.Wahr(s.Stumm, "Mikro stumm am Balken");
        a.Maschine.MikroStumm = false;
        h.Takt();
        Pruefe.Falsch(s.Stumm, "wieder an");

        // E23: der kurze Knopftext, getrennt leer.
        Pruefe.Gleich("", h.WerIstDa.KnopfKurz, "getrennt: nur das Zeichen");
        a.Dienst.LageSetzen(_ => FunkDienstAttrappe.Beispiel());
        Pruefe.Gleich("6", h.WerIstDa.KnopfKurz, "sechs online: nur die Zahl");
        Pruefe.Gleich("Wer ist da (6)", h.WerIstDa.KnopfText, "lang wie bisher");
        funkfeld.Dispose();
    }

    /// <summary>
    /// Der Behälter der Feinheiten (E6) mit mehreren Teilen: Die Kopfzeile zählt alle selbst gestellten Werte, der Satz nennt sie in der Reihenfolge
    /// der Teile, "Zurück auf Werkseinstellung" stellt jeden Teil zurück und rührt nichts an, was keiner ist (Gelerntes, Werte am Konto). Offen
    /// gemerkt unter der Kennung; eine alte Kennung wird beim Laden umgedeutet, ohne doppelt zu stehen.
    /// </summary>
    [Test]
    public static void Umbau_Feinheiten_BehaelterZaehltUndStelltZurueck()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb = 4.5;
        var nachlauf = new NachlaufTeil(a.Ablage, a.Maschine);
        var klang = new KlangfarbeTeil(a.Ablage, a.Maschine);
        var f = new Feinheiten(a.Ablage, "probe", "Probe", "Nachlauf und Klangfarbe", [nachlauf, klang]);
        // 0.11.1 (Sarevoc 12.10.2026): "Erweitert:" vorn, der Stand in Klammern danach; der Satz ab Werk sagt "unter Erweitert" statt "in Probe".
        Pruefe.Wahr(f is { AnzahlSelbstGestellt: 0, EtwasSelbstGestellt: false, Kopfzeile: "Erweitert: (ab Werk) Nachlauf und Klangfarbe" }, "ab Werk: " + f.Kopfzeile);
        Pruefe.Gleich("Alles unter Erweitert steht auf der Werkseinstellung.", f.StandSatz, "Satz ab Werk");
        var gemeldet = new List<string?>();
        f.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);
        nachlauf.NachlaufMs = 150;
        klang.Waerme = 2;
        klang.Schaerfe = -1;
        Pruefe.Gleich(3, f.AnzahlSelbstGestellt, "drei Werte selbst gestellt");
        Pruefe.Gleich("Erweitert: (3 selbst gestellt) Nachlauf und Klangfarbe", f.Kopfzeile, "Kopfzeile zählt");
        Pruefe.Gleich("(3 selbst gestellt) Nachlauf und Klangfarbe", f.Kopfrest, "der Rest nach dem fetten Wort");
        Pruefe.Wahr(gemeldet.Contains(nameof(Feinheiten.Kopfrest)) && gemeldet.Contains(nameof(Feinheiten.Kopfzeile)), "die Ansicht erfährt beides");
        Pruefe.Gleich("3 selbst gestellt: Nachlauf 150 ms, Wärme +2 dB, Schärfe -1 dB. Alles andere steht ab Werk.", f.StandSatz, "der Satz in der Reihenfolge der Teile");
        Pruefe.Wahr(f.KopfHilfe.Contains("selbst gestellt", StringComparison.Ordinal) && !f.KopfHilfe.Any(char.IsDigit), "Hovertext der Kopfzeile ohne Zahlen: " + f.KopfHilfe);
        f.ZurueckCommand.Execute(null);
        Pruefe.Wahr(nachlauf.NachlaufMs == TastenEinstellungen.NachlaufAbWerkMs && klang.Neutral && !f.EtwasSelbstGestellt, "alles zurück");
        Pruefe.Gleich(4.5, a.Ablage.Aktuell.Audio.GelernteVerstaerkungDb, "Gelerntes bleibt (E6)");
        Pruefe.Wahr(a.Maschine.Klangfarbe.IstNeutral && a.Maschine.TastenNachlaufMs == TastenEinstellungen.NachlaufAbWerkMs, "die Maschine bekommt es");

        f.Offen = true;
        Pruefe.Wahr(a.Ablage.Aktuell.Anzeige.OffeneFeinheiten.SequenceEqual(["probe"]), "offen unter der Kennung gemerkt");
        var anzeige = new AnzeigeEinstellungen { OffeneFeinheiten = ["sprechtaste", "tasten", "klangfarbe", "funkfeld"] };
        typeof(AnzeigeEinstellungen).GetMethod("Normalisieren", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(anzeige, null);
        Pruefe.Gleich("tasten,senden,funkfeld", string.Join(",", anzeige.OffeneFeinheiten), "umgedeutet, nicht doppelt (E6)");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Spaltenwechsel
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Zwei Spalten, die rutschen (für AP2 und AP3): ab <see cref="Spaltenwechsel.Ab"/> nebeneinander mit der Lücke dazwischen, darunter
    /// untereinander mit der Zeilenlücke; weitere Kinder über die ganze Breite darunter; ausgeblendete zählen nicht; die erste Spalte fest, wenn
    /// verlangt; frei gemessen nebeneinander. Ob nebeneinander, entscheidet nur das Messen.
    /// </summary>
    [Test]
    public static void Umbau_Spaltenwechsel_NebeneinanderUndUntereinander()
    {
        Bildlauf.Ausfuehren(_ =>
        {
            var a = new Border { Height = 50 };
            var b = new Border { Height = 80 };
            var c = new Border { Height = 30 };
            var weg = new Border { Height = 40, Visibility = Visibility.Collapsed };
            var p = new Spaltenwechsel { Ab = 600, Luecke = 24, Zeilenluecke = 16 };
            foreach (var k in new UIElement[] { a, weg, b, c }) p.Children.Add(k);
            // Der Platz, den das Panel dem Kind gibt (das Kind selbst steht mit fester Höhe darin mittig).
            Rect Lage(FrameworkElement e) => LayoutInformation.GetLayoutSlot(e);
            void Ordnen(double breite)
            {
                p.Measure(new Size(breite, double.PositiveInfinity));
                p.Arrange(new Rect(0, 0, breite, p.DesiredSize.Height));
                p.UpdateLayout();
            }

            Ordnen(1000);
            Pruefe.Wahr(p.Nebeneinander, "1000: nebeneinander");
            Pruefe.Gleich(new Rect(0, 0, 488, 80), Lage(a), "erste Spalte gleich breit, so hoch wie die Zeile");
            Pruefe.Gleich(new Rect(512, 0, 488, 80), Lage(b), "zweite Spalte nach der Lücke");
            Pruefe.Gleich(new Rect(0, 96, 1000, 30), Lage(c), "weitere über die ganze Breite unter der Zeile");
            Pruefe.Gleich(126.0, p.DesiredSize.Height, "Höhe: Zeile, Lücke, weiteres Kind");
            Pruefe.Gleich(0.0, weg.ActualWidth, "ausgeblendet ohne Platz");

            Ordnen(599);
            Pruefe.Falsch(p.Nebeneinander, "unter Ab: untereinander");
            Pruefe.Gleich(new Rect(0, 0, 599, 50), Lage(a), "erste oben, ganze Breite");
            Pruefe.Gleich(new Rect(0, 66, 599, 80), Lage(b), "zweite darunter nach der Zeilenlücke");
            Pruefe.Gleich(new Rect(0, 162, 599, 30), Lage(c), "weitere darunter");

            p.Aufteilung = SpaltenAufteilung.ErsteFest;
            p.ErsteBreite = 300;
            Ordnen(1000);
            Pruefe.Gleich(new Rect(0, 0, 300, 80), Lage(a), "erste Spalte fest");
            Pruefe.Gleich(new Rect(324, 0, 676, 80), Lage(b), "zweite nimmt den Rest");
            Pruefe.Gleich((296.0, 0.0), Spaltenwechsel.Spalten(320, 24, SpaltenAufteilung.ErsteFest, 300) is var (e1, z1) ? (Math.Round(e1), Math.Round(z1)) : default,
                "zu schmal: die erste höchstens so breit wie nach der Lücke Platz ist");

            // Ordnen in einer anderen Breite ohne neues Messen entscheidet nicht neu (kein Messen im Anordnen, CODEMAP).
            p.Arrange(new Rect(0, 0, 500, 200));
            Pruefe.Wahr(p.Nebeneinander, "die Entscheidung fällt nur beim Messen");

            p.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Pruefe.Wahr(p.Nebeneinander, "frei gemessen: nebeneinander");
            weg.Visibility = Visibility.Visible;
            b.Visibility = Visibility.Collapsed;
            c.Visibility = Visibility.Collapsed;
            Ordnen(1000);
            Pruefe.Wahr(p.Nebeneinander && Lage(weg).Left > 300, "die nächsten sichtbaren Kinder bilden die Spalten");
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Hauptfenster
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Hauptfenster mit Brett und Verwaltung (Admin mit ZUWEISEN), Namen und Hörweise gesetzt, auf Full HD.</summary>
    private static (Hauptfenster Haupt, HauptViewModel Modell, FunkfeldViewModel Funkfeld) Aufbauen(Aufbau a, List<Window> fenster)
    {
        a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
        a.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
        a.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        var dienst = new VerwaltungsdienstAttrappe();
        a.Verwaltung = new Funk.Verwaltung.Verwaltungsteil(dienst, new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst));
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Stufe.Admin, Admin = AdminStatus.Aktiv, Rechte = [.. l.Konto.Rechte, Recht.Zuweisen] } });
        var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
        fenster.Add(haupt);
        return (haupt, h, funkfeld);
    }

    private static T Teil<T>(Window haupt, string name) where T : FrameworkElement =>
        haupt.FindName(name) as T ?? throw new PruefungException(name + " fehlt");

    /// <summary>
    /// Die Statuszeile steht auf jeder Seite (Start, Einstellungen, Brett, Verwaltung, Seite Sprachaktivierung) zwischen Inhalt und Fußzeile, rollt
    /// nicht mit; keine Leiste, kein Trenner, kein Bereich unten mehr. Marke Getrennt, Ruhe und SENDET in Anthrazit lesbar (Kontrast 4,5:1),
    /// mit Namen für Bildschirmleser. Strg+T öffnet die Seite Sprachaktivierung, ohne Unterpunkt nichts.
    /// </summary>
    [Test]
    public static void Umbau_Statuszeile_AufJederSeite_MarkenUndKontrast()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
                var (haupt, h, funkfeld) = Aufbauen(a, fenster);
                var zeile = Teil<Border>(haupt, "Statuszeile");
                var marke = Teil<Border>(haupt, "StatusMarke");
                var markenText = Teil<TextBlock>(haupt, "StatusMarkenText");
                var pegel = Teil<Pegelbalken>(haupt, "StatusPegel");
                var fuss = Teil<Border>(haupt, "Fuss");
                Pruefe.Gleich("Pegel des Mikrofons", System.Windows.Automation.AutomationProperties.GetName(pegel), satz.Name + ": Name des Balkens");

                foreach (var ziel in new[] { HauptViewModel.StartKennung, HauptViewModel.EinstellungenKennung, HauptViewModel.RaidKennung, HauptViewModel.VerwaltungKennung,
                             HauptViewModel.SprachaktivierungKennung })
                {
                    h.AktiverBereich = h.Navigation.Single(b => b.Kennung == ziel);
                    var groesse = Fenstergroessen.KlasseFuer(ziel) switch
                    {
                        Groessenklasse.Klein => Fenstergroessen.KleinVoreinstellung,
                        Groessenklasse.Sprachaktivierung => Fenstergroessen.SprachaktivierungVoreinstellung,
                        Groessenklasse.Seite => Fenstergroessen.SeiteVoreinstellung,
                        _ => Fenstergroessen.GrossVoreinstellung,
                    };
                    _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                    var was = satz.Name + ", " + ziel;
                    var z = zeile.TransformToAncestor(Bildlauf.Wurzel(haupt)).TransformBounds(new Rect(zeile.RenderSize));
                    var f = fuss.TransformToAncestor(Bildlauf.Wurzel(haupt)).TransformBounds(new Rect(fuss.RenderSize));
                    Pruefe.Wahr(ZuSehen(zeile) && ZuSehen(pegel) && Sichtbar(zeile, "Mikrofon"), was + ": Statuszeile mit Mikrofon und Pegel");
                    Pruefe.Wahr(Math.Abs(z.Bottom - f.Top) < 0.5 && z.Height is > 24 and < 40, T($"{was}: direkt über der Fußzeile ({z.Top:0}-{z.Bottom:0})"));
                    Pruefe.Falsch(Bildlauf.Nachfahren<GridSplitter>(Bildlauf.Wurzel(haupt)).Any(), was + ": kein Trenner");
                    Pruefe.Falsch(Bildlauf.Nachfahren<ToggleButton>(Bildlauf.Wurzel(haupt)).Any(t => t.Name == "SchwellenLeiste"), was + ": keine Leiste");
                }

                h.AktiverBereich = h.Bereiche[0];
                foreach (var (lage, text) in new[] { ("Ruhe", "Ruhe"), ("Sendet", "SENDET"), ("Getrennt", "Getrennt") })
                {
                    if (lage == "Getrennt") a.Dienst.LageSetzen(_ => FunkLage.Leer);
                    a.Uhr += 20;
                    a.Maschine.Melden(new PegelMeldung(a.Uhr, lage == "Sendet" ? -21 : -80, -62, -45, lage == "Sendet" ? 0.95f : 0.05f, lage == "Sendet"));
                    h.Takt();
                    _ = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.KleinVoreinstellung);
                    var was = satz.Name + ", Marke " + lage;
                    Pruefe.Gleich(text, markenText.Text, was + ": Text");
                    Pruefe.Gleich("Mikrofon: " + text, System.Windows.Automation.AutomationProperties.GetName(marke), was + ": Name für Bildschirmleser");
                    var grund = Farbe(marke.Background);
                    var schrift = Farbe(markenText.Foreground);
                    Pruefe.Gleich(Farbe((Brush)haupt.FindResource(lage == "Sendet" ? "Pinsel.Senden" : "Pinsel.Flaeche")), grund, was + ": Fläche");
                    Pruefe.Wahr(Kontrast.Verhaeltnis(schrift, grund) >= 4.5, T($"{was}: Schrift lesbar ({Kontrast.Verhaeltnis(schrift, grund):0.00}:1)"));
                    KontrastPruefen(zeile, satz, was + ", Statuszeile", mindestens: 2);
                }

                // Strg+T (E2): an die Seite Sprachaktivierung gebunden, ohne Unterpunkt gesperrt.
                var strgT = haupt.InputBindings.OfType<KeyBinding>().Single(k => k.Key == Key.T && k.Modifiers == ModifierKeys.Control);
                Pruefe.Wahr(ReferenceEquals(strgT.Command, h.SprachaktivierungEinstellenCommand), satz.Name + ": Strg+T öffnet die Seite");
                a.Dienst.LageSetzen(_ => FunkDienstAttrappe.Beispiel());
                Pruefe.Wahr(strgT.Command.CanExecute(null), satz.Name + ": mit Unterpunkt geht Strg+T");
                h.Audio.SprachaktivierungAn = false;
                Pruefe.Falsch(strgT.Command.CanExecute(null), satz.Name + ": ohne Unterpunkt nichts");
                funkfeld.Dispose();
            }
        });
    }

    /// <summary>
    /// Alle fünf Hinweissätze passen ganz in die Statuszeile, auch in der kleinsten Breite der Seiten (640, der Balken dann 120 breit; Gegenprobe wie
    /// in OberflaecheMischungBilderTests). Dazu die Titelleiste nach der Breite (E23): bei 1100 alles, bei 899 "Wer ist da" und "Funkfeld" nur mit
    /// Zeichen und Zahl, bei 699 dazu ohne "spricht: ...". Die Namen bleiben im Hilfetext und für Bildschirmleser.
    /// </summary>
    [Test]
    public static void Umbau_SaetzeBei640_UndTitelleisteNachBreite()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var (haupt, h, funkfeld) = Aufbauen(a, fenster);
            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.EinstellungenKennung);
            var hinweis = Teil<TextBlock>(haupt, "StatusHinweis");
            var pegel = Teil<Pegelbalken>(haupt, "StatusPegel");
            var zeile = Teil<Border>(haupt, "Statuszeile");
            var marke = Teil<Border>(haupt, "StatusMarke");
            var dip = VisualTreeHelper.GetDpi(hinweis).PixelsPerDip;
            Rect Lage(FrameworkElement e) => e.TransformToAncestor(Bildlauf.Wurzel(haupt)).TransformBounds(new Rect(e.RenderSize));
            // Erst einmal zeichnen: Die Bindungen der Zeile greifen erst im Baum; danach überschreibt die Probe ihren Wert.
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(Fenstergroessen.SeiteMindest.Width, 700));
            foreach (var satz in Saetze)
            {
                hinweis.SetCurrentValue(TextBlock.TextProperty, satz);
                hinweis.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Visible);
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(Fenstergroessen.SeiteMindest.Width, 700));
                var voll = new FormattedText(satz, De, FlowDirection.LeftToRight, new Typeface(hinweis.FontFamily, hinweis.FontStyle, hinweis.FontWeight, hinweis.FontStretch),
                    hinweis.FontSize, Brushes.White, dip).WidthIncludingTrailingWhitespace;
                Console.WriteLine(T($"  640: \"{satz}\" {hinweis.ActualWidth:0.0} von {voll:0.0}, Balken {pegel.ActualWidth:0}"));
                Pruefe.Wahr(hinweis.ActualWidth >= voll - 0.5, T($"640: \"{satz}\" ganz ({hinweis.ActualWidth:0.0} von {voll:0.0})"));
                Pruefe.Gleich(120.0, pegel.ActualWidth, "640: der Balken 120 breit");
                Pruefe.Wahr(Lage(marke).Right <= Lage(zeile).Right + 0.5 && Lage(hinweis).Right <= Lage(pegel).Left + 0.5, "640: nichts läuft über");
            }

            a.Dienst.LageSetzen(l => l with { Sprecher = [new SprecherInfo(2, "Falke", [4], false)] });
            var werText = Teil<TextBlock>(haupt, "WerIstDaText");
            var werKurz = Teil<TextBlock>(haupt, "WerIstDaKurz");
            var ffText = Teil<TextBlock>(haupt, "FunkfeldText");
            var sprechend = Teil<FrameworkElement>(haupt, "SprechendPlatz");
            var werKnopf = Teil<Button>(haupt, "WerIstDaKnopf");
            var ffKnopf = Teil<Button>(haupt, "FunkfeldKnopf");
            foreach (var (breite, kurz, ohneSprecher) in new[] { (1100.0, false, false), (899.0, true, false), (699.0, true, true) })
            {
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(breite, 700));
                var was = T($"Titelleiste {breite:0}");
                Pruefe.Gleich(kurz, haupt.TitelKurz, was + ": kurz");
                Pruefe.Gleich(!kurz, ZuSehen(werText) && werText.Text == "Wer ist da (6)", was + ": Wer ist da mit Namen");
                Pruefe.Gleich(kurz, ZuSehen(werKurz) && werKurz.Text == "6", was + ": nur die Zahl");
                Pruefe.Gleich(!kurz, ZuSehen(ffText), was + ": Funkfeld mit Namen");
                Pruefe.Gleich(!ohneSprecher, ZuSehen(sprechend) && Sichtbar(sprechend, "spricht: Falke"), was + ": spricht");
                Pruefe.Gleich(ohneSprecher ? 120.0 : 160.0, pegel.ActualWidth, was + ": Breite des Balkens");
                Pruefe.Wahr(System.Windows.Automation.AutomationProperties.GetName(werKnopf) == "Wer ist da" && Glasdesign.Hilfe.GetText(werKnopf)!.StartsWith("Wer ist da", StringComparison.Ordinal),
                    was + ": der Name bleibt bei Wer ist da");
                Pruefe.Wahr(System.Windows.Automation.AutomationProperties.GetName(ffKnopf) == "Funkfeld" && Glasdesign.Hilfe.GetText(ffKnopf)!.StartsWith("Funkfeld", StringComparison.Ordinal),
                    was + ": der Name bleibt beim Funkfeld");
                var titel = Teil<Grid>(haupt, "TitelLeiste");
                foreach (var t in Bildlauf.Nachfahren<TextBlock>(titel).Where(ZuSehen))
                    Pruefe.Wahr(LayoutInformation.GetLayoutClip(t) is null || t.TextTrimming != TextTrimming.None, T($"{was}: \"{Kurz(Inhalt(t))}\" nicht abgeschnitten"));
            }
            a.Dienst.LageSetzen(_ => FunkLage.Leer);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(899, 700));
            Pruefe.Falsch(ZuSehen(werKurz), "getrennt und schmal: nur das Zeichen, keine leere Zahl");
            funkfeld.Dispose();
        });
    }
}
