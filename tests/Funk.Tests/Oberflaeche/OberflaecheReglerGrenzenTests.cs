// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Prüfung aller Regler (02.10.2026, 0.5.0 Schritt 2): Sarevoc sah einmal den Knopf eines Schiebereglers "ins Leere laufen" und
/// fand die Stelle nicht wieder. Gemessen wird im Speicherbild ohne Fensterhandle (<see cref="Bildlauf"/>): Der Knauf liegt
/// ganz in der Bahn, an der Stelle, die der Wert verlangt; der Wert liegt zwischen Minimum und Maximum; das Modell hinter dem
/// Regler liegt ebenfalls dazwischen und zeigt dasselbe wie der Regler (bis auf die Rastung). Das gilt für die Regler der
/// Funkfeld-Kacheln, den Regler der Durchsichtigkeit in Titelzeile und Einstellungen, den Pegel je Funker, die Schwelle samt
/// Korridor, den Klick und den Schiebeschalter 5/10, mit Grenzwerten, Unsinn (NaN, unendlich, Rundung) und alten
/// Einstellungsdateien; Breiten wie in Raster und Liste, Schriftgröße 175 % über die Größe des Bilds.
/// </summary>
internal static class OberflaecheReglerGrenzenTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Werte, die kein Regler und kein Modell je verlassen dürfen: Grenzen, davor und dahinter, Rundung, Unsinn.</summary>
    internal static readonly double[] Probewerte =
        [-1000, -100, -91, -90, -60, -45.3, -30.2, -30, -12.5, -6.4, -6, -0.2, 0, 0.05, 0.1, 0.5, 1, 3.3, 12, 12.4, 29.9, 30, 37, 60.5, 85, 85.5, 100, 1000,
            double.NaN, double.PositiveInfinity, double.NegativeInfinity];

    private static readonly double[] Breiten = [1100.0, 760.0, 560.0];
    private static readonly Size[] FunkfeldGroessen = [new(150, 60), new(240, 120), new(300, 300), new(500, 222), new(800, 222)];
    /// <summary>Seit 0.10.0 (AP3) nur noch Einstellungen, mit allen Feinheiten offen (bis 0.9.3 Audio, Meine Kanäle und Einstellungen).</summary>
    private static readonly int[] Bereiche = [1];
    private static readonly GateModus[] Modi = [GateModus.Fest, GateModus.Automatisch, GateModus.Lernend];

    /// <summary>Befunde zu einem Regler im Bild; leer, wenn alles stimmt. <c>null</c>, wenn er nicht zu sehen ist (Breite 0).</summary>
    internal static List<string>? Befunde(Slider s, string was)
    {
        var f = new List<string>();
        if (s.ActualWidth < 1 || s.Template is null) return null;
        if (!(s.Value >= s.Minimum && s.Value <= s.Maximum))
            f.Add(string.Create(De, $"{was}: Wert {s.Value} außerhalb {s.Minimum} bis {s.Maximum}"));
        if (s.Template.FindName("PART_Track", s) is not Track { Thumb: { } daumen } spur)
        {
            f.Add(was + ": keine Spur oder kein Knauf");
            return f;
        }
        var r = daumen.TransformToAncestor(s).TransformBounds(new Rect(daumen.RenderSize));
        const double Spiel = 0.51;
        if (r.Left < -Spiel || r.Right > s.ActualWidth + Spiel)
            f.Add(string.Create(De, $"{was}: Knauf {r.Left:0.0} bis {r.Right:0.0} steht außerhalb der Bahn 0 bis {s.ActualWidth:0.0} (Wert {s.Value})"));
        var anteil = (Math.Clamp(s.Value, s.Minimum, s.Maximum) - s.Minimum) / (s.Maximum - s.Minimum);
        var erwartet = (daumen.ActualWidth / 2) + (anteil * (spur.ActualWidth - daumen.ActualWidth));
        var mitte = r.Left + (r.Width / 2);
        if (Math.Abs(mitte - erwartet) > Spiel + 0.5)
            f.Add(string.Create(De, $"{was}: Knauf in der Mitte bei {mitte:0.0}, der Wert {s.Value} verlangt {erwartet:0.0}"));

        // Das Modell hinter dem Regler: in den Grenzen und dasselbe wie der Regler, bis auf die Rastung.
        if (BindingOperations.GetBinding(s, RangeBase.ValueProperty) is { Path.Path: { Length: > 0 } pfad } && s.DataContext is { } dc
            && dc.GetType().GetProperty(pfad)?.GetValue(dc) is { } roh)
        {
            var m = Convert.ToDouble(roh, CultureInfo.InvariantCulture);
            if (!(m >= s.Minimum && m <= s.Maximum))
                f.Add(string.Create(De, $"{was}: Modell {pfad} = {m} liegt außerhalb {s.Minimum} bis {s.Maximum}"));
            else if (Math.Abs(s.Value - m) > (s.IsSnapToTickEnabled ? s.TickFrequency / 2 : 1e-6) + 1e-9)
                f.Add(string.Create(De, $"{was}: Regler zeigt {s.Value}, das Modell {pfad} sagt {m}"));
        }
        return f;
    }

    /// <summary>Die schmalste Bahn, die ein Lauf sah (zur Ausgabe: MASS-Zeile).</summary>
    [ThreadStatic]
    private static (double Breite, string Wo) KleinsteBahn;

    /// <summary>Alle Regler unter <paramref name="wurzel"/> messen; zählt die sichtbaren.</summary>
    internal static int Messen(FrameworkElement wurzel, string szene, List<string> fehler)
    {
        if (KleinsteBahn.Wo is null) KleinsteBahn = (double.MaxValue, "");
        wurzel.UpdateLayout();
        Bildlauf.Leeren();
        wurzel.UpdateLayout();
        var gesehen = 0;
        foreach (var s in Bildlauf.Nachfahren<Slider>(wurzel))
        {
            var name = System.Windows.Automation.AutomationProperties.GetName(s);
            var b = Befunde(s, szene + ", " + (string.IsNullOrEmpty(name) ? "Regler" : name));
            if (b is null) continue;
            gesehen++;
            fehler.AddRange(b);
            if (s.ActualWidth < KleinsteBahn.Breite) KleinsteBahn = (s.ActualWidth, szene + ", " + name);
        }
        return gesehen;
    }

    /// <summary>Gleiche Befunde (nur der Probewert war ein anderer) einmal, höchstens 25 Zeilen.</summary>
    private static string Zusammenfassen(List<string> fehler) => string.Join(Environment.NewLine,
        fehler.Select(f => Regex.Replace(Regex.Replace(f, @" \[Probe [^\]]*\]", "", RegexOptions.None, TimeSpan.FromSeconds(1)), @"-?\d+(,\d+)?", "#", RegexOptions.None, TimeSpan.FromSeconds(1)))
            .Distinct().Take(25));

    private static string Probe(double w) => " [Probe " + w.ToString(CultureInfo.InvariantCulture) + "]";

    private static void Setze(Action a)
    {
        try
        {
            a();
        }
        catch (Exception e) when (e is ArgumentException or OverflowException or InvalidOperationException)
        {
            // Ein Modell darf Unsinn ablehnen; es darf ihn nicht stehen lassen (das prüft die Messung danach).
        }
    }

    /// <summary>Kacheln des Funkfelds und Regler der Titelzeile, in Raster 10, Raster 5 und Liste, mit allen Probewerten.</summary>
    [Test]
    public static void Regler_Funkfeld_KnaufBleibtInDerBahn()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var fehler = new List<string>();
            var gesehen = 0;
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            foreach (var (d, n, offen) in new[]
                     {
                         (FunkfeldDarstellung.Raster, 10, false), (FunkfeldDarstellung.Raster, 5, false),
                         (FunkfeldDarstellung.Liste, 10, true), (FunkfeldDarstellung.Liste, 5, true), (FunkfeldDarstellung.Liste, 10, false),
                     })
            {
                f.Darstellung = d;
                f.Ansicht = n;
                f.ReglerOffen = offen;
                var szene = $"{d}-{n}{(offen ? " mit Regler" : "")}";
                Bildbauen(ff);
                gesehen += Messen(Bildlauf.Wurzel(ff), szene, fehler);
                // Das Funkfeld lässt sich bis MinWidth 150 verkleinern; die Regler dürfen dann kleiner werden, der Knauf aber nie
                // über die Bahn hinaus.
                foreach (var g in FunkfeldGroessen)
                {
                    Bildlauf.Zeichnen(ff, 1.75, g);
                    gesehen += Messen(Bildlauf.Wurzel(ff), szene + string.Create(De, $" bei {g.Width}x{g.Height}"), fehler);
                }
                Bildbauen(ff);
                foreach (var w in Probewerte)
                {
                    foreach (var k in f.Kacheln.Where(k => k.NetzId is not null)) Setze(() => k.LautstaerkeDb = w);
                    Setze(() => f.ReglerWert = w);
                    Bildbauen(ff);
                    gesehen += Messen(Bildlauf.Wurzel(ff), szene + Probe(w), fehler);
                }
            }
            Console.WriteLine(string.Create(De, $"MASS Regler Funkfeld: schmalste Bahn {KleinsteBahn.Breite:0.0} bei {KleinsteBahn.Wo}"));
            Pruefe.Wahr(gesehen > 60, "die Messung sieht die Regler (" + gesehen + ")");
            Pruefe.Gleich("", Zusammenfassen(fehler), "Regler im Funkfeld");
            f.Dispose();
        });
    }

    private static void Bildbauen(Window w) => Bildlauf.Zeichnen(w);

    /// <summary>
    /// Hauptfenster: Lautstärke je Netz und Pegel je Funker, Durchsichtigkeit und die Lautstärken des Klicks (seit 0.10.0 alle in den Feinheiten
    /// der Einstellungen; bis 0.9.3 Bereiche Netze und Audio), Schwelle mit Abstand, Korridor, Anstieg, Rückkehr und Sprachsicherheit (alle drei
    /// Modi, Seite Sprachaktivierung); bei drei Breiten und mit allen Probewerten. Der Pegel je Funker kommt auch vom Server.
    /// </summary>
    [Test]
    public static void Regler_Hauptfenster_KnaufBleibtInDerBahn()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var fehler = new List<string>();
            var gesehen = new Dictionary<string, int>();
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var h = a.Haupt(out var f, out var sw, out var audio, out var netze);
            var e = (EinstellungenViewModel)Seiten.Einstellungen(h).Inhalt;
            // Seit 0.10.0 (AP3) stehen die Regler der Kanäle, der Funker, des Klicks und der Durchsichtigkeit in den Feinheiten der Einstellungen.
            Seiten.Feinheiten(h);
            var haupt = new Hauptfenster(h, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(haupt);
            haupt.ApplyTemplate();
            var wurzel = Bildlauf.Wurzel(haupt);
            // Seit 0.10.0 steht der Schwellenbereich nur noch auf seiner Seite (bis 0.9.3 aufgeklappt unter jedem Bereich).
            var ziele = Bereiche.Select(i => h.Bereiche[i]).Append(h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung)).ToList();
            foreach (var breite in Breiten)
            {
                foreach (var bereich in ziele)
                {
                    foreach (var modus in Modi)
                    {
                        h.AktiverBereich = bereich;
                        sw.Modus = modus;
                        var szene = $"Breite {breite}, {bereich.Titel}, {modus}";
                        Bildlauf.Zeichnen(haupt, 1.75, new Size(breite, 1060));
                        Zaehlen(gesehen, szene, Messen(wurzel, szene, fehler));
                        foreach (var w in Probewerte)
                        {
                            foreach (var z in netze.Funker.ToList()) Setze(() => z.PegelDb = w);
                            // Vom Server (ein anderer Client, eine ältere Fassung): derselbe Pegel für alle, auch Unsinn.
                            var pegel = netze.Funker.ToDictionary(z => z.KontoId, _ => (float)w);
                            a.Dienst.LageSetzen(l => l with { FunkerPegel = pegel });
                            foreach (var z in netze.Felder.Where(z => z.Regelung is not null)) Setze(() => z.Regelung!.LautstaerkeDb = w);
                            if (netze.GlobalRegelung is { } g) Setze(() => g.LautstaerkeDb = w);
                            Setze(() => e.Durchsichtigkeit = w);
                            Setze(() => sw.SchwelleDb = w);
                            Setze(() => sw.AbstandDb = w);
                            Setze(() => sw.KorridorVonDb = w);
                            Setze(() => sw.KorridorBisDb = w);
                            Setze(() => sw.AnstiegSekunden = w);
                            Setze(() => sw.RueckkehrSekunden = w);
                            Setze(() => sw.Sprachsicherheit = w);
                            Setze(() => sw.Korridor.UntergrenzeZiehen(w));
                            Setze(() => sw.Korridor.ObergrenzeZiehen(w));
                            Setze(() => audio.Klick.EigeneDb = w);
                            Setze(() => audio.Klick.AndereDb = w);
                            Bildlauf.Zeichnen(haupt, 1.75, new Size(breite, 1060));
                            Zaehlen(gesehen, szene + Probe(w), Messen(wurzel, szene + Probe(w), fehler));
                        }
                    }
                }
            }
            Console.WriteLine(string.Create(De, $"MASS Regler Hauptfenster: schmalste Bahn {KleinsteBahn.Breite:0.0} bei {KleinsteBahn.Wo}"));
            // Seit 0.10.0 unter Einstellungen (Empfangen, Senden, Tasten, Funkfeld und Darstellung offen; bis 0.9.3 Seite Meine Kanäle).
            Pruefe.Wahr(gesehen.Where(g => g.Key.Contains("Einstellungen", StringComparison.Ordinal)).All(g => g.Value >= 5)
                && gesehen.Keys.Any(k => k.Contains("Einstellungen", StringComparison.Ordinal)), "Einstellungen: Kanal-, Funker- und weitere Regler gesehen: " + Zusammen(gesehen, "Einstellungen"));
            // Seit Stufe 2b stehen die zwei Lautstärken des Klicks nicht mehr im Schwellenbereich, sondern im Bereich Audio. Seit 0.10.0 zählt nur
            // die Seite Sprachaktivierung, im Lernmodus mit sieben Reglern (bis 0.9.3 mindestens 8, weil der Bereich unten zu den Reglern des
            // Bereichs darüber zählte).
            Pruefe.Wahr(gesehen.Where(g => g.Key.Contains("Sprachaktivierung, Lernend", StringComparison.Ordinal)).All(g => g.Value >= 7) && gesehen.Keys.Any(k => k.Contains("Sprachaktivierung, Lernend", StringComparison.Ordinal)),
                "Schwellenbereich im Lernmodus: alle Regler gesehen: " + Zusammen(gesehen, "Sprachaktivierung, Lernend"));
            Pruefe.Gleich("", Zusammenfassen(fehler), "Regler im Hauptfenster");
            f.Dispose();
        });

        static void Zaehlen(Dictionary<string, int> d, string szene, int n) => d[Regex.Replace(szene, @" \[Probe [^\]]*\]", "", RegexOptions.None, TimeSpan.FromSeconds(1))] = n;

        static string Zusammen(Dictionary<string, int> d, string teil) =>
            string.Join("; ", d.Where(g => g.Key.Contains(teil, StringComparison.Ordinal)).Select(g => g.Key + " = " + g.Value));
    }

    /// <summary>
    /// Eine alte oder von Hand veränderte Einstellungsdatei mit Werten weit außerhalb der Bahnen: Beim Laden werden sie
    /// begrenzt, und jeder Regler zeigt gleich nach dem Start einen Knauf in der Bahn, an der Stelle seines Werts.
    /// </summary>
    [Test]
    public static void Regler_AlteEinstellungen_WerdenBeimLadenBegrenzt()
    {
        const string Alt = """
            {
              "gate": { "modus": "Lernend", "schwelleDb": -300, "abstandDb": 99, "korridorVonDb": 10, "korridorBisDb": -200,
                        "anstiegSekunden": -5, "rueckkehrSekunden": 1000000000, "sprachsicherheit": 7, "grenzlinieDb": -300 },
              "netze": { "2": { "lautstaerkeDb": 99, "panorama": 9 }, "4": { "lautstaerkeDb": -99 }, "6": { "lautstaerkeDb": -12.25 } },
              "funkfeld": { "durchsichtigkeit": 97 },
              "klick": { "eigeneDb": -100.5, "andereDb": 5 }
            }
            """;
        using var ordner = new Testordner();
        var speicher = new EinstellungsSpeicher(ordner.Datei("alt.json"));
        File.WriteAllText(speicher.Pfad, Alt);
        var geladen = speicher.Laden();
        Pruefe.Gleich(null, speicher.LetzterFehler, "die alte Datei ist lesbar");
        Pruefe.Wahr(geladen.Gate.SchwelleDb >= GateWerte.MinDb && geladen.Gate.AbstandDb <= GateWerte.AbstandMaxDb && geladen.Gate.Sprachsicherheit <= 1
            && geladen.Gate.AnstiegSekunden >= GateWerte.ZeitMinSek && geladen.Gate.RueckkehrSekunden <= GateWerte.RueckkehrMaxSek
            && geladen.Gate.GrenzlinieDb >= GateWerte.MinDb, "Gate begrenzt");
        Pruefe.Wahr(geladen.Gate.KorridorBisDb - geladen.Gate.KorridorVonDb >= GateWerte.KorridorMinBreiteDb, "Korridor hat seine Mindestbreite");
        Pruefe.Gleich(12f, geladen.Netze["2"].LautstaerkeDb, "Lautstärke oben begrenzt");
        Pruefe.Gleich(-30f, geladen.Netze["4"].LautstaerkeDb, "Lautstärke unten begrenzt");
        Pruefe.Gleich(FunkfeldEinstellungen.DurchsichtigkeitMax, geladen.Funkfeld.Durchsichtigkeit, "Durchsichtigkeit begrenzt");
        Pruefe.Wahr(geladen.Klick.EigeneDb >= FunkKlickEinstellungen.MinDb && geladen.Klick.AndereDb <= FunkKlickEinstellungen.MaxDb, "Klick begrenzt");

        Bildlauf.Ausfuehren(fenster =>
        {
            var fehler = new List<string>();
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene(), geladen);
            var h = a.Haupt(out var f, out _, out _, out _);
            Seiten.Feinheiten(h);
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            var haupt = new Hauptfenster(h, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            fenster.Add(haupt);
            Bildlauf.Zeichnen(ff);
            var gesehen = Messen(Bildlauf.Wurzel(ff), "Funkfeld", fehler);
            foreach (var bereich in Bereiche.Select(i => h.Bereiche[i]).Append(h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung)).ToList())
            {
                h.AktiverBereich = bereich;
                Bildlauf.Zeichnen(haupt, 1.75, new Size(1100, 1060));
                gesehen += Messen(Bildlauf.Wurzel(haupt), "Hauptfenster " + bereich.Titel, fehler);
            }
            Pruefe.Wahr(gesehen > 20, "die Messung sieht die Regler (" + gesehen + ")");
            Pruefe.Gleich("", Zusammenfassen(fehler), "Regler nach dem Laden der alten Datei");
            Pruefe.Gleich(12.0, f.Kacheln.First(k => k.NetzId == 2).LautstaerkeDb, "Kachel zeigt die begrenzte Lautstärke");
            f.Dispose();
        });
    }

    /// <summary>
    /// Schiebeschalter 5/10: Der Knauf steht in jeder Ansicht und nach jedem Wechsel dazwischen ganz in der Spur, links oder
    /// rechts am Rand. Befund vom 02.10.2026: Wechselte die Ansicht von Raster (Weg 14) nach Liste (Knauf 16 breit, Weg 12)
    /// bei 10 Feldern, blieb der Versatz 14, und der Knauf stand zwei Einheiten über das Ende der Spur hinaus.
    /// </summary>
    [Test]
    public static void Schiebeschalter_KnaufStehtNachJedemWechselInDerSpur()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var fehler = new List<string>();
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            Bildlauf.Zeichnen(ff);
            var s = ff.FeldSchalter;
            var schritte = 0;
            foreach (var (d, n) in Wechsel)
            {
                f.Darstellung = d;
                f.Ansicht = n;
                foreach (var skala in Skalen)
                {
                    Bildlauf.Zeichnen(ff, skala);
                    SchalterBefund(s, string.Create(De, $"{d}-{n} bei {skala * 100:0} %"), fehler);
                    schritte++;
                }
            }
            // Ziehen mit Unsinn: der Versatz bleibt in 0 bis Weg, auch bei NaN, unendlich und riesigen Strecken.
            Bildlauf.Zeichnen(ff);
            foreach (var dx in Ziehstrecken)
            {
                s.ZiehenBeginnt(40);
                s.ZiehenBis(40 + dx);
                if (!(s.KnaufVersatz >= 0 && s.KnaufVersatz <= s.Weg + 1e-9))
                    fehler.Add(string.Create(De, $"Ziehen um {dx}: Versatz {s.KnaufVersatz} außerhalb 0 bis {s.Weg}"));
                s.ZiehenEndet();
                SchalterBefund(s, string.Create(De, $"nach Ziehen um {dx}"), fehler);
            }
            Pruefe.Gleich("", Zusammenfassen(fehler), "Schiebeschalter");
            Pruefe.Gleich(Wechsel.Length * Skalen.Length, schritte, "alle Wechsel gemessen");
            f.Dispose();
        });
    }

    private static readonly (FunkfeldDarstellung, int)[] Wechsel =
    [
        (FunkfeldDarstellung.Raster, 10), (FunkfeldDarstellung.Liste, 10), (FunkfeldDarstellung.Raster, 10), (FunkfeldDarstellung.Raster, 5),
        (FunkfeldDarstellung.Liste, 5), (FunkfeldDarstellung.Liste, 10), (FunkfeldDarstellung.Raster, 10), (FunkfeldDarstellung.Liste, 5),
        (FunkfeldDarstellung.Raster, 5), (FunkfeldDarstellung.Liste, 10),
    ];

    private static readonly double[] Skalen = [1.0, 1.75];

    private static readonly double[] Ziehstrecken = [double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e9, -1e9, 3, -3, 0];

    private static void SchalterBefund(Schiebeschalter s, string szene, List<string> fehler)
    {
        var spur = (Border)s.Template.FindName("PART_Spur", s);
        var knauf = (FrameworkElement)s.Template.FindName("PART_Knauf", s);
        var links = spur.BorderThickness.Left + spur.Padding.Left;
        var rechts = spur.ActualWidth - spur.BorderThickness.Right - spur.Padding.Right;
        var r = knauf.TransformToAncestor(spur).TransformBounds(new Rect(knauf.RenderSize));
        const double Spiel = 0.6;
        if (r.Left < links - Spiel || r.Right > rechts + Spiel)
            fehler.Add(string.Create(De, $"{szene}: Knauf {r.Left:0.0} bis {r.Right:0.0} steht außerhalb der Spur {links:0.0} bis {rechts:0.0}"));
        else if (s.IsChecked == true && Math.Abs(r.Right - rechts) > Spiel)
            fehler.Add(string.Create(De, $"{szene}: auf 10 steht der Knauf bei {r.Right:0.0}, nicht am Ende der Spur ({rechts:0.0})"));
        else if (s.IsChecked != true && Math.Abs(r.Left - links) > Spiel)
            fehler.Add(string.Create(De, $"{szene}: auf 5 steht der Knauf bei {r.Left:0.0}, nicht am Anfang der Spur ({links:0.0})"));
    }

    /// <summary>
    /// Der ziehbare Regler in der Verlaufsgrafik (Schwelle, Korridor): Jede Linie liegt in der Pegelfläche, auch bei Werten
    /// außerhalb und bei Unsinn, und jede Stelle, auch eine ohne Zahl, ergibt einen Wert innerhalb der Grenzen.
    /// </summary>
    [Test]
    public static void Regler_Schwellengrafik_LinieBleibtInDerFlaeche()
    {
        var flaeche = new Rect(10, 20, 300, 140);
        foreach (var w in Probewerte)
        {
            var y = SchwellenGrafik.DbZuY(w, flaeche);
            Pruefe.Wahr(y >= flaeche.Top - 1e-9 && y <= flaeche.Bottom + 1e-9, string.Create(De, $"{w} dB liegt in der Fläche (y = {y})"));
            var db = SchwellenGrafik.YZuDb(w, flaeche);
            Pruefe.Wahr(db >= SchwellenGrafik.MinDb && db <= SchwellenGrafik.MaxDb, string.Create(De, $"y = {w} ergibt einen Wert in den Grenzen ({db})"));
        }
        Pruefe.Gleich(flaeche.Bottom, SchwellenGrafik.DbZuY(double.NaN, flaeche), "ohne Zahl steht die Linie ganz unten");
        Pruefe.Gleich(SchwellenGrafik.MinDb, SchwellenGrafik.YZuDb(double.NaN, flaeche), "ohne Zahl gilt die unterste Stelle");
    }
}
