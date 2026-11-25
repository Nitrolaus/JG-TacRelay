// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Klang.Verarbeitung;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Meldezeile einzeilig (0.11.0, Sarevoc 12.10.2026: "Im Funkfeld die Meldung des Kanals platzsparender und maximal einzeilig anordnen, das
/// Funkfeld muss klein bleiben"). Jede Meldung ist genau eine Zeile, nie ein Umbruch; zu Langes kürzt mit "..." (CharacterEllipsis wie überall
/// im Programm). Seit 0.11.1 steht die Meldezeile an einem festen Platz des Monitors und ist fest 640 Einheiten breit bei 0 % (bis 0.11.0
/// höchstens so breit wie das Funkfeld, über der 5er-Liste nur 288); geprüft im Speicher (Bildlauf, ohne Fensterhandle) bei 100 und 175 %
/// und mit der Skalierung -30, 0 und +30 %. Seit Sarevoc Q112 steht nur eine Meldung da, seit Q111 skaliert die Meldezeile mit dem Funkfeld
/// (Schrift, Innenrand, Höhe): Was bei 0 % ganz passt, passt bei -30 und +30 % ganz. Mit <c>FUNK_BILDER</c> entsteht
/// 0111-6-meldezeile-skalierung (Anthrazit, 175 %, -30, 0 und +30 %).
/// </summary>
internal static class OberflaecheMeldezeileEinzeiligTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private static readonly double[] Skalen = [1.0, 1.75];
    private static readonly int[] Skalierungen = [0, -30, 30];

    /// <summary>Eine Meldung zum Zeichnen; <paramref name="Fest"/>: ein Satz ohne Namen und Listen, der bei jeder Skalierung ganz passen soll.</summary>
    private sealed record Probe(string Schluessel, string Text, Meldeart Art, bool Ausrufe = false, bool Fest = false);

    /// <summary>
    /// Jede Art Meldung der Zeile in einer langen, aber üblichen Form (Namen erfunden); die festen Sätze aus dem Programm selbst. Die längste
    /// sucht <see cref="Laengste"/> nach gemessener Breite. Seit 0.11.2 nur noch, was über dem Spiel erscheint (<see cref="Meldezeile.Erlaubt"/>);
    /// Sendekanal, Mikro stumm, Dauersender, Urteil des Wächters, Hinweis zur Ablage, Rauschfilter und die Sätze zu Ruhe und Sprechtaste fallen
    /// still weg und stehen hier nicht mehr.
    /// </summary>
    private static List<Probe> Proben()
    {
        var lage = Funk.Client.Dienste.FunkDienstAttrappe.MitRaidabend().Lage;
        return
        [
            new(Teilnehmermeldungen.AnsageSchluessel, "Eingeteilt von Mustermann: Schiff 12, Kapitän", Meldeart.Hinweis),
            new(Teilnehmermeldungen.UmstellungSchluessel, "Kanäle geändert von Mustermann: sendest in Brücke (Schiff 12)", Meldeart.Hinweis),
            new(Teilnehmermeldungen.UmstellungSchluessel, "Phase Entern (Mustermann): hörst Brücke (Schiff 12), Kommando", Meldeart.Hinweis),
            new(Teilnehmermeldungen.EntzugSchluessel, "Nicht gesendet: kein Senderecht mehr in Brücke (Schiff 12), Global (Mustermann)", Meldeart.Fehler),
            new(Phasensteuerung.Schluessel, "Phase 2/3 ENTERN? Nochmal drücken - 12 Funker, ausgelassen: Nordwind (Admin), Distel (Admin)", Meldeart.Leitung),
            new(Phasensteuerung.Schluessel, "Keine Phasen angelegt (Flotte > Phasen)", Meldeart.Hinweis, Fest: true),
            new(Phasensteuerung.Schluessel, "Phasen schaltet die Einsatzleitung", Meldeart.Hinweis, Fest: true),
            new(Teilnehmermeldungen.MeldenSchluessel, Meldenetzwahl.Hindernis(null, lage)!, Meldeart.Fehler, Fest: true),
            new(Teilnehmermeldungen.MeldenSchluessel, "Melden: an Brücke (Schiff 12)", Meldeart.Hinweis),
            new(Meldezeile.ProbeSchluessel, Meldezeile.ProbeText, Meldeart.Hinweis, Fest: true),
        ];
    }

    /// <summary>Ein Satz, der auch in 640 Einheiten nicht ganz passt (zwei lange Entzüge hintereinander): Er muss bei jeder Skalierung kürzen.</summary>
    private static Probe Ueberlang()
    {
        var entzug = Proben().First(p => p.Schluessel == Teilnehmermeldungen.EntzugSchluessel);
        return entzug with { Text = entzug.Text + ", " + entzug.Text[16..] };
    }

    private static readonly Typeface Schrift = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static double Breite(string text, double groesse = 12) =>
        new FormattedText(text, De, FlowDirection.LeftToRight, Schrift, groesse, Brushes.White, 1).WidthIncludingTrailingWhitespace;

    private static Probe Laengste() => Proben().MaxBy(p => Breite(p.Text))!;

    /// <summary>Die längste Meldung und zwei Hinweise mit anderen Schlüsseln, zusammen gemeldet.</summary>
    private static Probe[] Drei(Probe laengste) =>
        [laengste, .. Proben().Where(p => p.Art == Meldeart.Hinweis && p.Schluessel != laengste.Schluessel).DistinctBy(p => p.Schluessel).Take(2)];

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Monitor mit Skala <paramref name="s"/>, wie Sarevocs Rechner 3840 x 2160 (bei 100 % ebenso groß, damit nur die Skala wechselt).</summary>
    private static FesteBildschirme Monitor(double s) =>
        new([new Bildschirm(new Pixelrechteck(0, 0, 3840, 2160), new Pixelrechteck(0, 0, 3840, 2076), s, true)]);

    /// <summary>Der Faktor zu einer Skalierung in Prozent, wie <c>FunkfeldViewModel.Skala</c> ihn liefert.</summary>
    private static double Faktor(int skalierung) => (100 + skalierung) / 100.0;

    /// <summary>Eine Meldezeile mit ihrer Breite und Skalierung gesetzt (wie <c>Abgleichen</c> vor dem Zeigen), ohne Fensterhandle.</summary>
    private static MeldezeileFenster Fenster(List<Window> fenster, Meldezeile zeile, FesteBildschirme monitore, int skalierung)
    {
        var mf = new MeldezeileFenster(zeile, monitore, () => Faktor(skalierung));
        fenster.Add(mf);
        mf.GroesseSetzen();
        return mf;
    }

    /// <summary>
    /// Jede gezeigte Meldung ist genau eine Zeile: kein Umbruch (NoWrap), kürzt mit Auslassung (CharacterEllipsis), so hoch wie eine Zeile der
    /// Schrift, und die Meldezeile ist höchstens <paramref name="hoechstens"/> breit (in Einheiten des Fensters, mit der Skalierung des
    /// Rahmens, Q111). Liefert, welche Sätze gekürzt stehen. Seit Q112 ist es immer genau ein Satz.
    /// </summary>
    private static List<string> EineZeileJeMeldung(MeldezeileFenster mf, double hoechstens, string was)
    {
        var wurzel = Bildlauf.Wurzel(mf);
        var saetze = Bildlauf.Nachfahren<TextBlock>(wurzel).Where(t => t.Name == "Satz" && ZuSehen(t)).ToList();
        Pruefe.Gleich(Meldezeile.ZeilenMax, saetze.Count, was + ": genau ein Satz (Q112)");
        var k = mf.Rahmen.LayoutTransform.Value.M11;
        Pruefe.Wahr(wurzel.ActualWidth <= hoechstens + 0.5 && mf.Rahmen.ActualWidth * k <= hoechstens + 0.5,
            T($"{was}: höchstens {hoechstens:0.##} breit, ist {wurzel.ActualWidth:0.##}"));
        var gekuerzt = new List<string>();
        foreach (var t in saetze)
        {
            var inhalt = Inhalt(t);
            Pruefe.Wahr(t.TextWrapping == TextWrapping.NoWrap && t.TextTrimming == TextTrimming.CharacterEllipsis, was + ": kein Umbruch, Kürzung mit Auslassung");
            Pruefe.Falsch(inhalt.Contains('\n', StringComparison.Ordinal) || inhalt.Contains('\r', StringComparison.Ordinal), was + ": kein Zeilenumbruch im Text");
            var zeile = new FormattedText("Ag", De, FlowDirection.LeftToRight, new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize,
                Brushes.White, 1).Height;
            Pruefe.Wahr(t.ActualHeight < zeile * 1.5, T($"{was}: \"{Kurz(inhalt)}\" ist eine Zeile ({t.ActualHeight:0.##} hoch, eine Zeile {zeile:0.##})"));
            var voll = new FormattedText(inhalt, De, FlowDirection.LeftToRight, new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize,
                Brushes.White, 1).WidthIncludingTrailingWhitespace;
            if (voll > t.ActualWidth + 0.5) gekuerzt.Add(inhalt);
        }
        // Alle Sätze gleich hoch: keiner nimmt eine zweite Zeile.
        Pruefe.Wahr(saetze.Max(t => t.ActualHeight) - saetze.Min(t => t.ActualHeight) < 0.5, was + ": alle Sätze gleich hoch");
        // Gekürzt werden darf nur der Satz selbst ("Satz"), beschnitten nichts.
        NichtsAbgeschnitten(wurzel, was, "Satz");
        return gekuerzt;
    }

    /// <summary>
    /// Jede Meldung einzeln und drei zusammen gemeldet, bei 100 und 175 % und mit der Skalierung -30, 0 und +30 %: eine Zeile, höchstens 640 mal
    /// die Skalierung breit (seit 0.11.1 fest, nicht mehr nach dem Funkfeld), die festen Sätze ganz, ein überlanger Satz gekürzt mit "...", von
    /// dreien nur die ranghöchste (Q112).
    /// </summary>
    [Test]
    public static void Meldezeile_EineZeileJeMeldung_FesteBreite640MalSkalierung()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var laengste = Laengste();
            var ueberlang = Ueberlang();
            foreach (var s in Skalen)
            {
                var monitore = Monitor(s);
                foreach (var skalierung in Skalierungen)
                {
                    var was = T($"{s * 100:0} %, Skalierung {skalierung}");
                    var zeile = new Meldezeile(() => 0);
                    var mf = Fenster(fenster, zeile, monitore, skalierung);
                    // Q111: Die feste Breite (640) skaliert mit; MaxWidth gilt vor der Skalierung des Rahmens.
                    var hoechstens = Meldezeilenlage.BreiteFest * Faktor(skalierung);
                    Pruefe.Wahr(Math.Abs((mf.Rahmen.MaxWidth * Faktor(skalierung)) - hoechstens) < 0.01, T($"{was}: Höchstbreite {mf.Rahmen.MaxWidth * Faktor(skalierung):0.##}, erwartet {hoechstens:0.##}"));

                    // Jede Meldung allein (jede mit eigenem Text, die Entprellung der Meldezeile sieht keine Wiederholung).
                    var gekuerzt = new List<string>();
                    foreach (var p in Proben().Append(ueberlang))
                    {
                        zeile.Zeigen("probe", p.Text, p.Art, 5000, p.Ausrufe);
                        _ = Bildlauf.Zeichnen(mf, s);
                        gekuerzt.AddRange(EineZeileJeMeldung(mf, hoechstens, was + ", " + Kurz(p.Text)));
                        Pruefe.Gleich(p.Ausrufe ? 1 : 0, Bildlauf.Nachfahren<ContentControl>(Bildlauf.Wurzel(mf)).Count(c => c.Name == "Ausrufe" && ZuSehen(c)),
                            was + ": das gelbe Zeichen nur vor dem Urteil");
                    }
                    zeile.Loesen("probe");

                    // Drei zusammen gemeldet (Leitung, Hinweis, Hinweis; bis 0.11.1 der dritte das Urteil mit "!"): seit Q112 steht nur die ranghöchste
                    // da, die längste.
                    foreach (var p in Drei(laengste)) zeile.Zeigen(p.Schluessel, p.Text, p.Art, 5000, p.Ausrufe);
                    var (_, eine) = Bildlauf.Zeichnen(mf, s);
                    _ = EineZeileJeMeldung(mf, hoechstens, was + ", drei gemeldet");
                    Pruefe.Gleich(laengste.Text, zeile.Text, was + ": die Leitung vor den Hinweisen");
                    var zeilenhoehe = Bildlauf.Nachfahren<TextBlock>(Bildlauf.Wurzel(mf)).First(t => t.Name == "Satz").ActualHeight;
                    Pruefe.Wahr(eine.Height <= (((zeilenhoehe + 2) + 8) * Faktor(skalierung)) + 0.5, T($"{was}: eine Zeile, {eine.Height:0.##} hoch"));
                    Console.WriteLine(T($"MASS Meldezeile {was}: höchstens {hoechstens:0.#} breit, eine Meldung {eine.Width:0.#} x {eine.Height:0.#}, eine Zeile {zeilenhoehe * Faktor(skalierung):0.#} hoch; gekürzt {gekuerzt.Count} von {Proben().Count + 1}"));

                    // Der überlange Satz kürzt mit "..."; die festen Sätze passen bei jeder Skalierung ganz (Q111).
                    Pruefe.Wahr(gekuerzt.Contains(ueberlang.Text), was + ": der überlange Satz kürzt mit \"...\"");
                    var fest = Proben().Where(p => p.Fest).Select(p => p.Text).ToHashSet();
                    Pruefe.Falsch(gekuerzt.Any(fest.Contains), was + ": feste Sätze ganz: " + string.Join(" | ", gekuerzt.Where(fest.Contains)));
                }
            }
        });
    }

    /// <summary>
    /// Sarevoc Q111: Die Meldezeile skaliert mit dem Funkfeld. Bei 100 und 175 % mit Skalierung -30, 0 und +30 %: Jeder Satz, der bei 0 % ganz passt,
    /// passt bei -30 und +30 % ganz (bis Q111 kürzte bei -30 % fast jeder über der Liste); Schrift, Innenrand und Höhe wachsen und schrumpfen
    /// mit dem Faktor (LayoutTransform des Rahmens), und die Meldezeile ist nie breiter als 640 mal den Faktor.
    /// </summary>
    [Test]
    public static void Meldezeile_SkaliertMitDemFunkfeld_GanzeSaetzeBleibenGanz()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var ueberlang = Ueberlang();
            foreach (var s in Skalen)
            {
                var monitore = Monitor(s);
                HashSet<string>? ganzBeiNull = null;
                var hoeheBeiNull = 0.0;
                foreach (var skalierung in Skalierungen)
                {
                    var was = T($"{s * 100:0} %, Skalierung {skalierung}");
                    var zeile = new Meldezeile(() => 0);
                    var mf = Fenster(fenster, zeile, monitore, skalierung);
                    var hoechstens = Meldezeilenlage.BreiteFest * Faktor(skalierung);
                    Pruefe.Wahr(Math.Abs(mf.Rahmen.LayoutTransform.Value.M11 - Faktor(skalierung)) < 1e-9 && Math.Abs(mf.Rahmen.LayoutTransform.Value.M22 - Faktor(skalierung)) < 1e-9,
                        was + ": der Rahmen skaliert mit dem Funkfeld");

                    var ganz = new HashSet<string>();
                    var gekuerzt = new List<string>();
                    var hoehe = 0.0;
                    foreach (var p in Proben().Append(ueberlang))
                    {
                        zeile.Zeigen("probe", p.Text, p.Art, 5000, p.Ausrufe);
                        var (_, groesse) = Bildlauf.Zeichnen(mf, s);
                        hoehe = Math.Max(hoehe, groesse.Height);
                        Pruefe.Wahr(groesse.Width <= hoechstens + 0.5, T($"{was}: nicht breiter als 640 mal den Faktor ({groesse.Width:0.#} zu {hoechstens:0.#})"));
                        var kurz = EineZeileJeMeldung(mf, hoechstens, was + ", " + Kurz(p.Text));
                        gekuerzt.AddRange(kurz);
                        if (kurz.Count == 0) ganz.Add(p.Text);
                        var satz = Bildlauf.Nachfahren<TextBlock>(Bildlauf.Wurzel(mf)).Single(t => t.Name == "Satz" && ZuSehen(t));
                        Pruefe.Wahr(Math.Abs(satz.FontSize - 12) < 1e-9, was + ": die Schrift selbst bleibt 12, der Rahmen skaliert sie");
                    }
                    zeile.Loesen("probe");
                    Pruefe.Wahr(gekuerzt.Contains(ueberlang.Text) && ganz.Count > 0, was + ": der überlange Satz kürzt, die übrigen passen (sonst prüfte der Vergleich nichts)");

                    if (skalierung == 0)
                    {
                        ganzBeiNull = ganz;
                        hoeheBeiNull = hoehe;
                    }
                    else
                    {
                        var verloren = ganzBeiNull!.Where(t => !ganz.Contains(t)).ToList();
                        Pruefe.Gleich(0, verloren.Count, was + ": bei 0 % ganz, jetzt gekürzt: " + string.Join(" | ", verloren));
                        // Höhe mit dem Faktor (auf ganze Einheiten gerundet gezeichnet, darum eine Einheit Spiel).
                        Pruefe.Wahr(Math.Abs(hoehe - (hoeheBeiNull * Faktor(skalierung))) <= 1, T($"{was}: {hoehe:0.##} hoch, erwartet {hoeheBeiNull * Faktor(skalierung):0.##} (bei 0 % {hoeheBeiNull:0.##})"));
                    }
                    Console.WriteLine(T($"MASS Meldezeile Q111 {was}: höchstens {hoechstens:0.#} breit, {hoehe:0.#} hoch, ganz {ganz.Count} von {Proben().Count + 1}"));
                }
            }
        });
    }

    /// <summary>
    /// Die Rechnung dahinter ohne Fenster: die Höchstbreite ist seit 0.11.1 fest 640 mal die Skalierung (bis 0.11.0 höchstens die des Funkfelds in
    /// Bildpunkten durch die Skala seines Monitors), Unsinn als Faktor zählt als 1; ein Zeilenumbruch im Text wird zum Leerzeichen (ein TextBlock
    /// bräche an ihm auch ohne Umbruch).
    /// </summary>
    [Test]
    public static void Meldezeile_Hoechstbreite_UndUmbruchImText()
    {
        Pruefe.Gleich(640.0, Meldezeilenlage.Hoechstbreite(), "bei 0 %: 640, wie bis 0.10.0 ohne Funkfeld");
        Pruefe.Gleich(640.0, Meldezeilenlage.Hoechstbreite(1), "Faktor 1: 640");
        Pruefe.Wahr(Math.Abs(Meldezeilenlage.Hoechstbreite(0.7) - 448) < 1e-9, "bei -30 %: 640 * 0,7");
        Pruefe.Wahr(Math.Abs(Meldezeilenlage.Hoechstbreite(1.3) - 832) < 1e-9, "bei +30 %: 640 * 1,3");
        Pruefe.Gleich(640.0, Meldezeilenlage.Hoechstbreite(0), "Faktor 0 zählt als 1");
        Pruefe.Gleich(640.0, Meldezeilenlage.Hoechstbreite(double.NaN), "NaN zählt als 1");
        Pruefe.Wahr(Math.Abs(Meldezeilenlage.Hoechstbreite(0.2) - 448) < 1e-9, "nicht unter -30 %");
        Pruefe.Gleich(1.0, Meldezeilenlage.Skalierung(0), "Faktor 0 zählt als 1");
        Pruefe.Gleich(1.0, Meldezeilenlage.Skalierung(double.NaN), "NaN zählt als 1");
        Pruefe.Gleich(0.7, Meldezeilenlage.Skalierung(0.5), "nicht unter -30 %");
        Pruefe.Gleich(1.3, Meldezeilenlage.Skalierung(2), "nicht über +30 %");

        Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Gunner", Meldezeile.Einzeilig("Eingeteilt von Ferox: Schiff 1, Gunner"), "ohne Umbruch unverändert");
        Pruefe.Gleich("Gesperrt bis 21:30 von Nordwind", Meldezeile.Einzeilig("Gesperrt bis 21:30\nvon Nordwind"), "\\n");
        Pruefe.Gleich("a b c", Meldezeile.Einzeilig("a\r\nb\r" + (char)0x2028 + "c"), "\\r\\n, \\r und Zeilentrenner");
        var z = new Meldezeile(() => 0);
        // Seit 0.11.2 mit Schlüsseln aus Meldezeile.Erlaubt (bis 0.11.1 frei gewählte, die jetzt still wegfallen).
        z.Zeigen(Teilnehmermeldungen.EntzugSchluessel, "Nicht gesendet: kein Senderecht\nmehr in Crew 1", Meldeart.Fehler, 1000);
        z.Halten(Teilnehmermeldungen.MeldenSchluessel, "Melden: an\nKommando", Meldeart.Hinweis);
        // Seit Q112 eine Meldung: der Fehler; die gehaltene wartet und kommt einzeilig, wenn er gelöst ist.
        Pruefe.Gleich("Nicht gesendet: kein Senderecht mehr in Crew 1", string.Join("|", z.Zeilen.Select(m => m.Text)), "Zeigen einzeilig");
        z.Loesen(Teilnehmermeldungen.EntzugSchluessel);
        Pruefe.Gleich("Melden: an Kommando", string.Join("|", z.Zeilen.Select(m => m.Text)), "Halten einzeilig");
        Pruefe.Falsch(z.Text.Contains('\n', StringComparison.Ordinal), "eine Zeile");
    }

    /// <summary>
    /// Bild für Sarevoc (mit <c>FUNK_BILDER</c>): die längste Meldung, mit zwei weiteren gemeldet (seit Q112 steht nur sie da), bei 175 % mit der
    /// Skalierung -30, 0 und +30 % untereinander (seit Q111 skaliert die Meldezeile mit; seit 0.11.1 ohne Funkfeld darunter, sie steht an
    /// einem festen Platz). Anthrazit (Voreinstellung), Kontrast und nur einfache Zeichen in den Sätzen geprüft.
    /// </summary>
    [Test]
    public static void Meldezeile_LaengsteMeldung_DreiSkalierungen_Bild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            const double Skala = 1.75;
            var satz = Farbvorschlaege.Von(Farbvorschlaege.Voreinstellung);
            var monitore = Monitor(Skala);
            var laengste = Laengste();
            var zeilen = new List<(string Titel, RenderTargetBitmap Bild, Size Groesse)>();
            foreach (var skalierung in Skalierungen.Order())
            {
                var was = T($"175 %, Skalierung {skalierung}");
                var zeile = new Meldezeile(() => 0);
                foreach (var p in Drei(laengste)) zeile.Zeigen(p.Schluessel, p.Text, p.Art, 5000, p.Ausrufe);
                var mf = Fenster(fenster, zeile, monitore, skalierung);
                var (bild, groesse) = Bildlauf.Zeichnen(mf, Skala);
                var gekuerzt = EineZeileJeMeldung(mf, Meldezeilenlage.BreiteFest * Faktor(skalierung), was);
                Pruefe.Gleich(laengste.Text, zeile.Text, was + ": nur die ranghöchste steht da");
                Pruefe.Gleich(0, gekuerzt.Count, was + ": die längste übliche Meldung passt in 640 mal den Faktor ganz: " + string.Join(" | ", gekuerzt));
                KontrastPruefen(Bildlauf.Wurzel(mf), satz, was, mindestens: 1);
                foreach (var t in zeile.Zeilen)
                    Pruefe.Falsch(t.Text.Any(c => c is (char)0x2013 or (char)0x2014 or (char)0x2212 or (char)0x201E or (char)0x201C or (char)0x201D or (char)0x2026 or (char)0x2022 or (char)0x00A0), was + ": nur einfache Zeichen in \"" + t.Text + "\"");
                zeilen.Add((T($"175 %, Skalierung {skalierung:+0;-0;0} %"), bild, groesse));
            }

            // Ein Bogen: die drei Meldezeilen untereinander, je mit Überschrift.
            const double rand = 16;
            const double kopf = 22;
            var unterschrift = "Längste Meldung: " + laengste.Text;
            var breite = Math.Max(zeilen.Max(z => z.Groesse.Width), Breite(unterschrift, 11)) + (2 * rand);
            var hoehe = rand + zeilen.Sum(z => kopf + z.Groesse.Height + rand) + 20;
            var bogen = new Bogen(breite, hoehe, Skala, OberflaecheFunkfeldBilderTests.Grund);
            var y = rand;
            foreach (var z in zeilen)
            {
                bogen.Text(z.Titel, rand, y, 12, fett: true);
                bogen.Bild(z.Bild, rand, y + kopf, z.Groesse.Width, z.Groesse.Height);
                y += kopf + z.Groesse.Height + rand;
            }
            bogen.Text(unterschrift, rand, hoehe - rand - 14, 11, OberflaecheFunkfeldBilderTests.Grau);
            Bildablage.Ablegen(bogen.Fertig(), "0111-6-meldezeile-skalierung");
        });
    }
}
