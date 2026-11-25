// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;
using Pfad = System.Windows.Shapes.Path;
using Rahmenform = System.Windows.Shapes.Rectangle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Durchsichtigkeit (A30, A33). Hauptweg seit Sarevoc, 30.09.2026 ("das transparente Fenster macht nicht das ganze Funkfenster
/// transparent, so geht das nicht"): das ganze Fenster, 0 bis 85 %, die Deckkraft nie unter 15 %; wie gut man dann über
/// hellem Spielbild noch liest, ist Sarevocs Wahl, geprüft werden Deckkraft und Flächen. Die Option "Schrift und Symbole deckend
/// halten" behält die Lesbarkeitsregeln (Befund der Gegenprüfung: bei 70 % über der Sonne Kachelgrund #929292,
/// Zweitschrift 1,9:1, Weiß 3,1:1, Knopfränder weg): bei jeder Reglerstellung alle Texte auf den Kacheln und in der
/// Titelzeile mindestens 4,5:1, Grenzen und Symbole mindestens 3:1, auch mit reinem Weiß hinter dem Fenster. Geprüft
/// zweimal: gerechnet (<see cref="FunkfeldDeckung"/>, jede ganze Prozentstufe, alle drei Farbstellungen) und gemessen, per
/// Pixel im Speicherbild bei 175 % über Weiß gelegt (<see cref="Bildlauf"/>, ohne Fensterhandle). Dazu die Schichtung:
/// immer geschichtet, nie ein neues Fenster. Mit <c>FUNK_BILDER</c> entsteht funkfeld-durchsichtig.png.
/// </summary>
internal static class OberflaecheFunkfeldDurchsichtTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
    private const double Skala = 1.75;

    /// <summary>Die Stellungen, die das Bild misst: jeder Rasterpunkt des Reglers (5 %), dazu 1 %.</summary>
    private static readonly int[] Stellungen = [0, 1, .. Enumerable.Range(1, 17).Select(i => i * 5)];
    private static readonly int[] Rot = [40, 85];
    private static readonly int[] Ganz = [20, 40, 48, 60, 85];
    private static readonly int[] Bildstufen = [0, 40, 85];
    private static readonly bool[] Modi = [false, true];

    [Test]
    public static void Durchsicht_Rechnung_JedeStellungLesbar()
    {
        foreach (var satz in Farbvorschlaege.Alle)
        {
            var m = FunkfeldDeckung.Mindest(satz);
            Console.WriteLine(string.Create(De,
                $"MASS {satz.Name}: kleinste Deckung Kachel {m.Kachel} ({FunkfeldDeckung.Prozent(m.Kachel)} % durchsichtig), Global {m.Global} ({FunkfeldDeckung.Prozent(m.Global)} %), Unterlage {m.Unterlage} ({FunkfeldDeckung.Prozent(m.Unterlage)} %)"));
            Pruefe.Wahr(m.Kachel < 255 && m.Global < 255 && m.Unterlage < 255, satz.Name + ": jede Fläche hält ihren Inhalt deckend (sonst 255)");
            for (var p = 0; p <= FunkfeldEinstellungen.DurchsichtigkeitMax; p++)
            {
                var d = FunkfeldDeckung.Berechnen(Durchsicht.Aus(p, schriftDeckend: true), satz);
                var was = string.Create(De, $"{satz.Name}, deckend halten, {p} %");
                Pruefe.Gleich((byte)Math.Round(Math.Max(Durchsicht.MindestAlpha, 1 - (p / 100.0)) * 255), d.Luecken, was + ": Lücken folgen dem Regler");
                Pruefe.Wahr(d.Kachel >= d.Luecken && d.Global >= d.Luecken && d.Unterlage >= d.Luecken, was + ": tragende Flächen nie durchsichtiger als die Lücken");
                Pruefe.Wahr(Lesbarkeit.Haelt(Lesbarkeit.UeberWeiss(Palette.Flaeche, d.Kachel), FunkfeldDeckung.KachelPaare), was + ": Kachel über Weiß");
                Pruefe.Wahr(Lesbarkeit.Haelt(Lesbarkeit.UeberWeiss(satz.Leitfarbe, d.Global), FunkfeldDeckung.KachelPaare), was + ": Global über Weiß");
                Pruefe.Wahr(Lesbarkeit.Haelt(Lesbarkeit.UeberWeiss(Palette.FlaecheTief, d.Unterlage), FunkfeldDeckung.UnterlagePaare(satz)), was + ": Titelzeile über Weiß");
                Pruefe.Wahr(Lesbarkeit.Haelt(Lesbarkeit.UeberWeiss(Palette.Flaeche, d.Kachel), FunkfeldDeckung.AusPaare, d.AusDeckkraft), was + ": AUS gedimmt über Weiß");
                Pruefe.Wahr(d.AusDeckkraft >= Palette.AusDeckkraft && d.AusDeckkraft <= 1, was + ": Dimmung zwischen 0,7 und 1");
                Pruefe.Wahr(Lesbarkeit.Haelt(Lesbarkeit.UeberWeiss(Palette.Flaeche, d.Kachel), FunkfeldDeckung.LeerPaare, d.LeerDeckkraft), was + ": leeres Feld über Weiß");
                Pruefe.Wahr(Lesbarkeit.Haelt(Lesbarkeit.UeberWeiss(satz.Leitfarbe, d.Global), FunkfeldDeckung.LeerPaare, d.LeerDeckkraft), was + ": leeres Global über Weiß");
                Pruefe.Wahr(d.LeerDeckkraft >= Palette.LeerDeckkraft && d.LeerDeckkraft <= 1, was + ": Dimmung leerer Felder zwischen 0,55 und 1");
                Pruefe.Gleich(1.0, d.Fenster, was + ": das Fenster bleibt deckend");

                // Hauptweg: das ganze Fenster, alle Flächen deckend, nur die Deckkraft des Fensters sinkt, nie unter 15 %.
                var g = FunkfeldDeckung.Berechnen(Durchsicht.Aus(p, schriftDeckend: false), satz);
                var ganz = string.Create(De, $"{satz.Name}, ganzes Fenster, {p} %");
                Pruefe.Gleich(FunkfeldDeckung.DeckendFuer(satz) with { Fenster = g.Fenster }, g, ganz + ": alle Flächen deckend");
                Pruefe.Wahr(Math.Abs(g.Fenster - (1 - (p / 100.0))) < 1e-9 && g.Fenster >= Durchsicht.MindestDeckkraft - 1e-9, ganz + ": Deckkraft des Fensters");
            }
            var hoechst = FunkfeldDeckung.Berechnen(Durchsicht.Aus(85, true), satz);
            Pruefe.Wahr(hoechst.Kachel >= 3 * hoechst.Luecken, satz.Name + ": bei 85 % sind die Kacheln deutlich deckender als die Lücken");
            Console.WriteLine(string.Create(De, $"MASS {satz.Name} deckend halten bei 85 %: Lücken Alpha {hoechst.Luecken}, Kachel {hoechst.Kachel}, Global {hoechst.Global}, Unterlage {hoechst.Unterlage}, AUS gedimmt auf {hoechst.AusDeckkraft:0.00}, leer auf {hoechst.LeerDeckkraft:0.00}"));

            // Leere Felder deckend: Name und FREI 4,5:1 auf Kachel und Global, so blass wie möglich, nicht unter 0,55.
            var deckend = FunkfeldDeckung.Berechnen(Durchsicht.Aus(0, true), satz);
            Pruefe.Gleich(FunkfeldDeckung.DeckendFuer(satz), deckend, satz.Name + ", 0 %: alles deckend");
            Pruefe.Gleich(deckend, FunkfeldDeckung.Berechnen(Durchsicht.Aus(0, false), satz), satz.Name + ", 0 %: in beiden Wegen dasselbe");
            foreach (var (flaeche, name) in new[] { (Palette.Flaeche, "Kachel"), (satz.Leitfarbe, "Global") })
            {
                var gedimmt = Lesbarkeit.Mischen(Palette.Text, flaeche, deckend.LeerDeckkraft);
                var k = Kontrast.Verhaeltnis(gedimmt, flaeche);
                Pruefe.Wahr(k >= 4.5, string.Create(De, $"{satz.Name}: leerer Name auf {name} deckend {k:0.00}:1"));
                Console.WriteLine(string.Create(De, $"MASS {satz.Name}: leeres Feld deckend {deckend.LeerDeckkraft:0.00}, Name und FREI auf {name} {k:0.00}:1"));
            }
            var eineStufeBlasser = Math.Round(deckend.LeerDeckkraft - 0.01, 2);
            if (eineStufeBlasser >= Palette.LeerDeckkraft)
                Pruefe.Falsch(Lesbarkeit.Haelt(satz.Leitfarbe, FunkfeldDeckung.LeerPaare, eineStufeBlasser, Lesbarkeit.Reserve), satz.Name + ": eine Stufe blasser hielte Global nicht mehr");
        }
        Pruefe.Gleich(Palette.LeerDeckkraft, FunkfeldDeckung.DeckendFuer(Farbvorschlaege.Anthrazit).LeerDeckkraft, "Anthrazit: leere Felder mit 0,55");
        // Gegenprobe: Eine hellere Leitfarbe (der Akzent Petrol als Fläche) braucht für leere Felder auf Global mehr Deckkraft.
        Pruefe.Wahr(FunkfeldDeckung.DeckendFuer(Farbvorschlaege.Anthrazit with { Leitfarbe = Palette.Akzent }).LeerDeckkraft > Palette.LeerDeckkraft,
            "Gegenprobe: Global in hellerer Leitfarbe braucht mehr");
        Pruefe.Wahr(Kontrast.Verhaeltnis(Lesbarkeit.Mischen(Palette.Text, Palette.Flaeche, 0.45), Palette.Flaeche) < 4.1
            && Kontrast.Verhaeltnis(Lesbarkeit.Mischen(Palette.Text2, Palette.Flaeche, 0.45), Palette.Flaeche) < 3, "Gegenprobe: bei 0,45 frei 4,03:1 und FREI in Zweitschrift 2,95:1");
        Pruefe.Gleich(Palette.AusDeckkraft, FunkfeldDeckung.Deckend.AusDeckkraft, "deckend dimmt AUS wie bisher");

        // Gegenprobe, der Befund der Gegenprüfung: Kachel und Lücken gleich durchsichtig, 70 % über Weiß.
        var altKachel = Lesbarkeit.Mischen(Palette.Flaeche, Lesbarkeit.Mischen(Palette.Anthrazit, Colors.White, 0.3), 0.3);
        Pruefe.Wahr(Kontrast.Verhaeltnis(Palette.Text2, altKachel) < 2.5 && Kontrast.Verhaeltnis(Palette.Text, altKachel) < 3.5,
            string.Create(De, $"Gegenprobe: früher Zweitschrift {Kontrast.Verhaeltnis(Palette.Text2, altKachel):0.0}:1, Weiß {Kontrast.Verhaeltnis(Palette.Text, altKachel):0.0}:1 auf {OberflaecheFunkfeldBilderTests.Hex(altKachel)}"));

        // Ganzes Fenster über reinem Weiß: bis wohin weiße Namen noch 3:1 halten. Das begrenzte früher den Regler auf 48 %;
        // Sarevoc hat entschieden, dass es jetzt bis 85 % geht. Hier nur gemessen, damit die Grenze sichtbar bleibt.
        var grenze = FunkfeldEinstellungen.DurchsichtigkeitMax;
        foreach (var satz in Farbvorschlaege.Alle)
        {
            for (var p = 0; p <= FunkfeldEinstellungen.DurchsichtigkeitMax; p++)
            {
                var alpha = (byte)Math.Round((1 - (p / 100.0)) * 255);
                var namen = Math.Min(Kontrast.Verhaeltnis(Palette.Text, Lesbarkeit.UeberWeiss(Palette.Flaeche, alpha)),
                    Kontrast.Verhaeltnis(Palette.Text, Lesbarkeit.UeberWeiss(satz.Leitfarbe, alpha)));
                if (namen >= 3) continue;
                grenze = Math.Min(grenze, p - 1);
                break;
            }
        }
        Pruefe.Gleich(48, grenze, "ganzes Fenster über reinem Weiß: weiße Namen halten 3:1 bis 48 % (Messung, keine Grenze mehr)");
        Console.WriteLine(string.Create(De, $"MASS ganzes Fenster über reinem Weiß: weiße Namen halten 3:1 bis {grenze} %, darüber ist es Sarevocs Wahl (Regler bis 85 %)"));
    }

    /// <summary>
    /// Die Szene für die Messung: alles, was auf einer Kachel stehen kann. Crew und Global SENDEN, und du sprichst gerade (grün,
    /// der knappste Text); Durchsagen ist AUS (gedimmt), Jäger hat einen Sprecher, andere hören mit "Ruhe".
    /// </summary>
    private static Aufbau MessAufbau() => new(OberflaecheFunkfeldBilderTests.Szene() with { GlobalSenden = true });

    private static FunkfeldViewModel Messszene(Aufbau a)
    {
        var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
        a.Maschine.Melden(new PegelMeldung(a.Uhr, -20, -60, -50, 0.9f, true));
        a.Uhr += FunkfeldViewModel.PegelAbstandMs;
        f.Takt();
        Pruefe.Wahr(f.Kacheln.Count(k => k.EigeneSendung) == 2, "Du sprichst auf Crew und Global");
        return f;
    }

    [Test]
    public static void Durchsicht_Bild_UeberWeissGemessen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = MessAufbau();
            var f = Messszene(a);
            f.SchriftDeckend = true;
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            var tiefst = (Text: double.MaxValue, Grenze: double.MaxValue);
            foreach (var p in Stellungen)
            {
                f.Durchsichtigkeit = p;
                Pruefe.Wahr(ff.AllowsTransparency, p + " %: immer geschichtet, dasselbe Fenster");
                var d = f.Deckung;
                Pruefe.Gleich(d.Kachel, ((SolidColorBrush)ff.Resources["Funkfeld.Kachel"]).Color.A, p + " %: Kachel mit der gerechneten Deckung");
                Pruefe.Gleich(d.Luecken, ((SolidColorBrush)ff.Resources["Funkfeld.Grund"]).Color.A, p + " %: Lücken mit der gerechneten Deckung");
                Pruefe.Gleich(1.0, ff.Opacity, p + " %: deckend halten, das Fenster bleibt deckend");
                var messungen = MessenUeberWeiss(ff, out _);
                foreach (var m in messungen)
                    Pruefe.Wahr(m.Verhaeltnis >= m.Verlangt, string.Create(De, $"{p} % deckend halten, {m.Was}: {m.Verhaeltnis:0.00}:1, verlangt {m.Verlangt}:1"));
                var text = messungen.Where(m => m.Verlangt > 4).Min(m => m.Verhaeltnis);
                var grenze = messungen.Where(m => m.Verlangt < 4).Min(m => m.Verhaeltnis);
                tiefst = (Math.Min(tiefst.Text, text), Math.Min(tiefst.Grenze, grenze));
                if (p % 20 == 0 || p == 1 || p == 85)
                    Console.WriteLine(string.Create(De, $"MASS über Weiß bei {p} %: {messungen.Count} Stellen, Schrift mindestens {text:0.00}:1, Grenzen und Symbole mindestens {grenze:0.00}:1"));
                Pruefe.Wahr(messungen.Count(m => m.Was.Contains("Du sprichst", StringComparison.Ordinal)) == 2, p + " %: Du sprichst gemessen");
                Pruefe.Wahr(messungen.Any(m => m.Was.StartsWith("Grenze Knopf", StringComparison.Ordinal))
                    && messungen.Any(m => m.Was.StartsWith("Grenze Punkt AUS", StringComparison.Ordinal))
                    && messungen.Any(m => m.Was.StartsWith("Knauf", StringComparison.Ordinal))
                    && messungen.Any(m => m.Was.StartsWith("Schiebeschalter", StringComparison.Ordinal)), p + " %: Grenzen gefunden");
            }
            Console.WriteLine(string.Create(De, $"MASS über Weiß, deckend halten, alle Stellungen 0-85 %: Schrift mindestens {tiefst.Text:0.00}:1, Grenzen und Symbole mindestens {tiefst.Grenze:0.00}:1"));

            // Ablehnung in roter Schrift auf der Unterlage der Titelzeile, bei 40 und 85 %.
            f.SendenWaehlenAsync(f.Kacheln.Single(k => k.Name == "Jäger"), hinzufuegen: false).GetAwaiter().GetResult();
            Pruefe.Wahr(f.Hinweis is { Length: > 0 }, "Ablehnung");
            foreach (var p in Rot)
            {
                f.Durchsichtigkeit = p;
                var rot = MessenUeberWeiss(ff, out _).Single(m => m.Was.Contains(f.Hinweis!, StringComparison.Ordinal));
                Pruefe.Wahr(rot.Verhaeltnis >= 4.5, string.Create(De, $"{p} %: rote Schrift {rot.Verhaeltnis:0.00}:1"));
            }

            // Ganzes Fenster (Hauptweg): dieselbe Instanz, die Deckkraft folgt sofort; gemessen wird, nicht begrenzt.
            f.SendenWaehlenAsync(f.Kacheln.Single(k => k.Name == "Crew"), hinzufuegen: false).GetAwaiter().GetResult();
            f.SchriftDeckend = false;
            foreach (var p in Ganz)
            {
                f.Durchsichtigkeit = p;
                Pruefe.Wahr(Math.Abs(ff.Opacity - (1 - (p / 100.0))) < 1e-9, p + " %: Deckkraft des ganzen Fensters");
                Pruefe.Gleich((byte)255, ((SolidColorBrush)ff.Resources["Funkfeld.Kachel"]).Color.A, p + " %: die Flächen selbst bleiben deckend");
                var namen = MessenUeberWeiss(ff, out _).Where(WeisserName).ToList();
                Pruefe.Gleich(9, namen.Count, p + " %: alle weißen Namen gemessen (Durchsagen ist AUS und gedimmt)");
                var min = namen.Min(m => m.Verhaeltnis);
                if (p <= 48) Pruefe.Wahr(min >= 3, string.Create(De, $"{p} % ganzes Fenster: weiße Namen über Weiß {min:0.00}:1"));
                Console.WriteLine(string.Create(De, $"MASS ganzes Fenster {p} % über reinem Weiß: weiße Namen mindestens {min:0.00}:1"));
            }
            f.Dispose();
        });
    }

    /// <summary>
    /// A33 (Sarevoc: "beim Schieben der Transparenz blendet das Fenster aus, das soll so nicht sein"): Das Funkfeld ist immer
    /// geschichtet, auch deckend, und jeder Wechsel der Durchsichtigkeit, auch über 0 hinweg und zwischen den Wegen, wirkt am
    /// selben Fenster sofort; es gibt kein Ereignis mehr, das ein neues Fenster verlangt.
    /// </summary>
    [Test]
    public static void Durchsicht_ImmerGeschichtet_OhneNeuOeffnen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(OberflaecheFunkfeldBilderTests.Szene());
            var f = a.Funkfeld();
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            Pruefe.Wahr(ff.AllowsTransparency, "0 %: schon geschichtet");
            Pruefe.Gleich(1.0, ff.Opacity, "0 %: deckend");
            Pruefe.Gleich((byte)255, ((SolidColorBrush)ff.Resources["Funkfeld.Grund"]).Color.A, "0 %: Grund deckend");
            Pruefe.Gleich(null, typeof(FunkfeldFenster).GetEvent("NeuOeffnen"), "kein NeuOeffnen");
            Pruefe.Gleich(null, typeof(FunkfeldFenster).GetProperty("SchichtungFalsch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
                "keine Prüfung der Schichtung mehr");
            foreach (var (p, deckendHalten) in new[] { (40, false), (0, false), (70, true), (5, true), (0, true), (85, false), (0, false) })
            {
                f.SchriftDeckend = deckendHalten;
                f.Durchsichtigkeit = p;
                var was = string.Create(De, $"{p} % {(deckendHalten ? "deckend halten" : "ganzes Fenster")}");
                Pruefe.Wahr(ff.AllowsTransparency, was + ": dasselbe geschichtete Fenster");
                Pruefe.Wahr(Math.Abs(ff.Opacity - f.Deckung.Fenster) < 1e-9, was + ": Deckkraft sofort");
                Pruefe.Gleich(f.Deckung.Luecken, ((SolidColorBrush)ff.Resources["Funkfeld.Grund"]).Color.A, was + ": Lücken sofort");
                Pruefe.Gleich(f.Deckung.Kachel, ((SolidColorBrush)ff.Resources["Funkfeld.Kachel"]).Color.A, was + ": Kacheln sofort");
            }
            Pruefe.Gleich(Palette.AusDeckkraft, (double)ff.Resources["Funkfeld.AusDeckkraft"], "deckend dimmt AUS wie immer");
            f.Dispose();
        });
    }

    /// <summary>
    /// Fokusrahmen (ENTWURF: Fokus 3:1) auf der Fläche, die sie umschließen, mit "deckend halten" auch bei 85 % über reinem Weiß, in beiden
    /// Ansichten. Befund der Gegenprüfung: In der Liste deckt der Kachelkopf die ganze Kachel, und sein Rahmen außen herum
    /// (Margin -3) lag über den durchsichtigen Lücken, weiß auf fast Weiß. Ohne Fensterhandle gibt es keinen Tastaturfokus
    /// und also keinen gezeichneten Rahmen; sein Ort kommt darum aus der Vorlage (FocusVisualStyle: Rechteck mit Margin und
    /// Strichstärke), gemessen wird im Speicherbild der Grund direkt innen am Rahmen, an allen vier Seiten.
    /// </summary>
    [Test]
    public static void Durchsicht_Fokusrahmen_AufTragenderFlaeche()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = MessAufbau();
            var f = Messszene(a);
            f.SchriftDeckend = true;
            f.Durchsichtigkeit = FunkfeldEinstellungen.DurchsichtigkeitMax;
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            Pruefe.Wahr(ff.AllowsTransparency, "85 %: geschichtet");
            foreach (var d in new[] { FunkfeldDarstellung.Raster, FunkfeldDarstellung.Liste })
            {
                f.Darstellung = d;
                var (roh, _) = Bildlauf.Zeichnen(ff, Skala);
                var bild = Pixelbild.UeberWeiss(roh, ff.Opacity);
                var wurzel = Bildlauf.Wurzel(ff);
                var gemessen = 0;
                var unterKnauf = 0;
                var tiefst = double.MaxValue;
                foreach (var e in Bildlauf.Nachfahren<Control>(wurzel).Where(e => e.Focusable && Zaehlt(e, wurzel)))
                {
                    var was = $"{d}, Fokus {e.GetType().Name} {(e.DataContext is KachelViewModel kv ? kv.Name : "Titelzeile")}";
                    var rahmen = Fokusrahmen(e) ?? throw new PruefungException(was + ": kein eigener Fokusrahmen (Rechteck in FocusVisualStyle)");
                    if (rahmen.Stroke is not SolidColorBrush { Color: var farbe }) throw new PruefungException(was + ": Fokusrahmen ohne feste Farbe");
                    var r = Rechteck(e, wurzel);
                    var m = rahmen.Margin;
                    var ring = new Rect(r.X + m.Left, r.Y + m.Top, Math.Max(0, r.Width - m.Left - m.Right), Math.Max(0, r.Height - m.Top - m.Bottom));
                    var s = rahmen.StrokeThickness;
                    var innen = new Rect(ring.X + s, ring.Y + s, Math.Max(1, ring.Width - (2 * s)), Math.Max(1, ring.Height - (2 * s)));
                    // Schiebeschalter in der Liste: Der Rahmen liegt dicht um die Spur, und an der Seite, auf der der weiße Knauf steht,
                    // ist der Grund innen am Rahmen der Knauf selbst (weiß auf weiß). Bis zum 02.10.2026 stand der Knauf auf 10 in der
                    // Liste zwei Einheiten zu weit rechts, und die Ziffer 10 darin machte den Grund dort nur zufällig dunkel. Offen und
                    // nicht behoben (PROJEKT.md, Schritt 2): Hier zählt der Rahmen an den Seiten ohne Knauf.
                    Rect? knauf = e is Funk.Client.Steuerelemente.Schiebeschalter sch && sch.Template.FindName("PART_Knauf", sch) is FrameworkElement kn
                        ? kn.TransformToAncestor(wurzel).TransformBounds(new Rect(kn.RenderSize))
                        : null;
                    foreach (var (seite, streifen) in new[]
                             {
                                 ("oben", new Rect(innen.X, innen.Y, innen.Width, 1)), ("unten", new Rect(innen.X, innen.Bottom - 1, innen.Width, 1)),
                                 ("links", new Rect(innen.X, innen.Y, 1, innen.Height)), ("rechts", new Rect(innen.Right - 1, innen.Y, 1, innen.Height)),
                             })
                    {
                        if (knauf is { } kr && kr.IntersectsWith(streifen))
                        {
                            unterKnauf++;
                            continue;
                        }
                        var grund = bild.Haeufigste(streifen);
                        var k = Kontrast.Verhaeltnis(farbe, grund);
                        tiefst = Math.Min(tiefst, k);
                        Pruefe.Wahr(k >= 3, string.Create(De, $"{was}, {seite}: {k:0.00}:1 gegen {OberflaecheFunkfeldBilderTests.Hex(grund)}, verlangt 3:1"));
                    }
                    // Einen halben Bildpunkt Spiel: Die Layoutrundung bei 175 % verschiebt Kanten um Bruchteile einer Einheit.
                    var umgebung = Umgebung(e, wurzel, ff);
                    umgebung.Inflate(0.5, 0.5);
                    Pruefe.Wahr(umgebung.Contains(ring), string.Create(CultureInfo.InvariantCulture,
                        $"{was}: der Rahmen {ring} liegt auf seiner Kachel oder der Unterlage der Titelzeile {umgebung}"));
                    gemessen++;
                }
                Pruefe.Wahr(gemessen >= f.Kacheln.Count(k => !k.IstLeer) + 4, $"{d}: Fokusrahmen aller Kacheln und der Titelzeile gemessen, waren {gemessen}");
                Pruefe.Wahr(unterKnauf <= 1, $"{d}: höchstens eine Seite des Schiebeschalters liegt auf dem Knauf (waren {unterKnauf})");
                Console.WriteLine(string.Create(De, $"MASS Fokusrahmen {d} bei 85 % über Weiß: {gemessen} Rahmen, mindestens {tiefst:0.00}:1, Seiten auf dem weißen Knauf nicht gezählt: {unterKnauf}"));
            }
            f.Dispose();
        });
    }

    /// <summary>
    /// Leere Felder (Stil Kachel.Blass): Name und FREI halten 4,5:1, deckend und mit "deckend halten" bei 40 und 85 % über reinem Weiß, auf der
    /// Kachel und auf Global (getrennt ist auch Global leer), in Anthrazit, in Raster und Liste. Gemessen im Speicherbild bei
    /// 175 %. Vorher, mit 0,45, hatten "frei" 4,03:1 und "FREI" 2,95:1.
    /// </summary>
    [Test]
    public static void Leer_NameUndFrei_UeberWeissLesbar()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var tiefst = double.MaxValue;
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz);
                foreach (var getrennt in Modi)
                {
                    var b = OberflaecheFunkfeldBilderTests.Szene();
                    using var a = new Aufbau(getrennt ? Funk.Client.Dienste.FunkLage.Leer : b with { Felder = [.. b.Felder.Where(x => x.Feld is not (3 or 7))] });
                    a.Ablage.Aktuell.Farbstellung = satz.Stellung;
                    var f = a.Funkfeld();
                    f.SchriftDeckend = true;
                    var leer = f.Kacheln.Count(k => k.IstLeer);
                    Pruefe.Gleich(getrennt ? f.Kacheln.Count : 2, leer, satz.Name + ": leere Felder in der Szene");
                    var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
                    fenster.Add(ff);
                    foreach (var d in new[] { FunkfeldDarstellung.Raster, FunkfeldDarstellung.Liste })
                    {
                        f.Darstellung = d;
                        foreach (var p in Rot.Prepend(0))
                        {
                            f.Durchsichtigkeit = p;
                            Pruefe.Gleich(f.Deckung.LeerDeckkraft, (double)ff.Resources["Funkfeld.LeerDeckkraft"], $"{satz.Name}, {p} %: Dimmung aus der Rechnung");
                            var (roh, _) = Bildlauf.Zeichnen(ff, Skala);
                            var bild = Pixelbild.UeberWeiss(roh, ff.Opacity);
                            var wurzel = Bildlauf.Wurzel(ff);
                            var texte = Bildlauf.Nachfahren<TextBlock>(wurzel)
                                .Where(t => t.DataContext is KachelViewModel { IstLeer: true } && (t.Name == "NameText" || t.Text == "FREI") && Sichtbar(t, wurzel)).ToList();
                            Pruefe.Gleich(2 * leer, texte.Count, $"{satz.Name}, {d}, {p} %: Name und FREI jedes leeren Felds gefunden");
                            foreach (var t in texte)
                            {
                                var r = Rechteck(t, wurzel);
                                var k = bild.Staerkste(r, bild.Haeufigste(r));
                                tiefst = Math.Min(tiefst, k);
                                var wo = ((KachelViewModel)t.DataContext).IstGlobal ? "Global" : "Kachel";
                                Pruefe.Wahr(k >= 4.5, string.Create(De, $"{satz.Name}, {d}, {p} %, leer auf {wo}: '{t.Text}' {k:0.00}:1"));
                            }
                        }
                    }
                    f.Dispose();
                }
            }
            Console.WriteLine(string.Create(De, $"MASS leere Felder über Weiß (Anthrazit, Raster, Liste, 0/40/85 %): Name und FREI mindestens {tiefst:0.00}:1"));
        });
    }

    /// <summary>Sichtbar im Baum bis zur Wurzel, ohne Rücksicht auf den Zustand des Felds (anders als <see cref="Zaehlt"/>).</summary>
    private static bool Sichtbar(FrameworkElement e, FrameworkElement wurzel)
    {
        if (e.ActualWidth < 1 || e.ActualHeight < 1) return false;
        for (DependencyObject? d = e; d is not null && !ReferenceEquals(d, wurzel); d = VisualTreeHelper.GetParent(d))
        {
            if (d is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: <= 0 }) return false;
        }
        return true;
    }

    /// <summary>Das Rechteck, das der FocusVisualStyle eines Elements zeichnet, oder <c>null</c>.</summary>
    private static Rahmenform? Fokusrahmen(FrameworkElement e) =>
        e.FocusVisualStyle?.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == Control.TemplateProperty)?.Value is ControlTemplate vorlage
            ? vorlage.LoadContent() as Rahmenform
            : null;

    [Test]
    public static void Durchsicht_Vergleichsbild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = MessAufbau();
            var f = Messszene(a);
            var zellen = new List<(int Prozent, bool Ganz, RenderTargetBitmap Bild, double Fenster, Size Groesse, string Messung)>();
            var ff = new FunkfeldFenster(f, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(ff);
            foreach (var deckendHalten in Modi)
            {
                var ganz = !deckendHalten;
                foreach (var p in Bildstufen)
                {
                    f.SchriftDeckend = deckendHalten;
                    f.Durchsichtigkeit = p;
                    var messungen = MessenUeberWeiss(ff, out _);
                    var namen = messungen.Where(WeisserName).Min(m => m.Verhaeltnis);
                    var messung = ganz
                        ? string.Create(De, $"über reinem Weiß gemessen: weiße Namen ab {namen:0.00}:1")
                        : string.Create(De, $"über reinem Weiß gemessen: Schrift ab {messungen.Where(m => m.Verlangt > 4).Min(m => m.Verhaeltnis):0.00}:1, Grenzen und Symbole ab {messungen.Where(m => m.Verlangt < 4).Min(m => m.Verhaeltnis):0.00}:1");
                    var (bild, groesse) = Bildlauf.Zeichnen(ff);
                    zellen.Add((p, ganz, bild, ff.Opacity, groesse, messung));
                }
            }
            Bildablage.Ablegen(Vergleichsbogen(zellen), "funkfeld-durchsichtig");
            f.Dispose();
        });
    }

    private static RenderTargetBitmap Vergleichsbogen(List<(int Prozent, bool Ganz, RenderTargetBitmap Bild, double Fenster, Size Groesse, string Messung)> zellen)
    {
        const double rand = 24;
        const double zelleB = 840;
        const double zelleH = 250;
        const double zeile = 26 + zelleH + 40;
        var b = new Bogen(rand + zelleB + 24 + zelleB + rand, 96 + (Bildstufen.Length * zeile), 1, OberflaecheFunkfeldBilderTests.Grund);
        b.Text("Funkfeld durchsichtig - Ansicht A Raster über einem selbst gezeichneten Hintergrund (steht für das Spiel), mit Sonne und einem Streifen reinem Weiß", rand, 16, 18, fett: true);
        b.Text("Links das ganze Fenster (Voreinstellung, Sarevoc 30.09.2026): alles gleichmäßig durchsichtig, bis 85 %; 15 % Deckkraft bleiben, damit das Funkfeld greifbar bleibt. Crew und Global senden, du sprichst gerade.",
            rand, 44, 12, OberflaecheFunkfeldBilderTests.Grau);
        b.Text("Rechts mit der Option \"Schrift und Symbole deckend halten\": Lücken und Titelzeile folgen dem Regler, Kacheln und Unterlagen bleiben so deckend, dass jede Schrift 4,5:1 und jede Grenze 3:1 hält.",
            rand, 62, 12, OberflaecheFunkfeldBilderTests.Grau);
        foreach (var s in zellen)
        {
            var x = rand + (s.Ganz ? 0 : zelleB + 24);
            var y = 96 + (Array.IndexOf(Bildstufen, s.Prozent) * zeile);
            b.Text($"{s.Prozent} % - {(s.Ganz ? "ganzes Fenster" : "Schrift und Symbole deckend halten")}", x, y, 14, fett: true);
            var zelle = new Rect(x, y + 26, zelleB, zelleH);
            b.Zeichnung(dc =>
            {
                Spielbild.Zeichnen(dc, zelle);
                dc.DrawRectangle(Bogen.Pinsel(Colors.White), null, new Rect(zelle.X + 430, zelle.Y, 120, zelle.Height));
            });
            b.Bild(s.Bild, zelle.X + 32, zelle.Y + 26, s.Groesse.Width, s.Groesse.Height, s.Fenster);
            b.Text(s.Messung, x, zelle.Bottom + 6, 12, Colors.White);
        }
        return b.Fertig();
    }

    /// <summary>Weißer Name einer Kachel; im Zustand AUS ist er absichtlich gedimmt und nicht mehr weiß.</summary>
    private static bool WeisserName(Messung m) => m.Was.StartsWith("Name", StringComparison.Ordinal) && !m.Was.EndsWith(" AUS", StringComparison.Ordinal);

    /// <summary>Eine gemessene Stelle: was, gemessenes Verhältnis, verlangtes Verhältnis.</summary>
    internal sealed record Messung(string Was, double Verhaeltnis, double Verlangt);

    /// <summary>
    /// Zeichnet das Funkfeld bei 175 %, legt es mit der Deckkraft des Fensters über reines Weiß und misst jede sichtbare
    /// Schrift (4,5:1; Symbolschrift 3:1), jedes Symbol (3:1) und die Grenzen der Bedienelemente auf den Kacheln und in der
    /// Titelzeile (3:1): hellstes Bildpunkt gegen den häufigsten Bildpunkt der Umgebung. Ausgenommen sind gesperrte
    /// Elemente und leere Felder (WCAG nimmt inaktive Elemente aus; leere Felder sind absichtlich blass).
    /// </summary>
    internal static List<Messung> MessenUeberWeiss(FunkfeldFenster ff, out Pixelbild bild)
    {
        var (roh, _) = Bildlauf.Zeichnen(ff, Skala);
        bild = Pixelbild.UeberWeiss(roh, ff.Opacity);
        var wurzel = Bildlauf.Wurzel(ff);
        var ergebnis = new List<Messung>();
        var knopfStil = (Style)ff.FindResource("Kachel.Knopf");
        var randStil = (Style)ff.FindResource("Kachel.Rand");

        foreach (var t in Bildlauf.Nachfahren<TextBlock>(wurzel))
        {
            if (!Zaehlt(t, wurzel, auchGesperrt: t.Name == "NameText") || string.IsNullOrWhiteSpace(t.Text)) continue;
            var symbol = t.FontFamily.Source.Contains("Icons", StringComparison.Ordinal) || t.FontFamily.Source.Contains("MDL2", StringComparison.Ordinal);
            var r = Rechteck(t, wurzel);
            var grund = bild.Haeufigste(r);
            var name = t.Name == "NameText" ? "Name" : symbol ? "Symbolschrift" : "Schrift";
            var gedimmt = t.DataContext is KachelViewModel { IstAus: true } ? " AUS" : "";
            ergebnis.Add(new Messung($"{name} '{t.Text}'{gedimmt}", bild.Staerkste(r, grund), symbol ? 3 : 4.5));
        }
        foreach (var s in Bildlauf.Nachfahren<Pfad>(wurzel))
        {
            if (!Zaehlt(s, wurzel) || s.Stroke is null) continue;
            var r = Rechteck(s, wurzel);
            ergebnis.Add(new Messung("Symbol in " + (s.DataContext is KachelViewModel kv ? kv.Name : "der Titelzeile"), bild.Staerkste(r, bild.Haeufigste(r)), 3));
        }
        foreach (var k in Bildlauf.Nachfahren<ButtonBase>(wurzel).Where(k => ReferenceEquals(k.Style, knopfStil) && Zaehlt(k, wurzel)))
        {
            var r = Rechteck(k, wurzel);
            var oben = new Rect(r.X + 4, r.Y, Math.Max(1, r.Width - 8), 1.2);
            ergebnis.Add(new Messung("Grenze Knopf " + (k.DataContext is KachelViewModel kv ? kv.Name : "Titelzeile"), bild.Staerkste(oben, bild.Haeufigste(Umgebung(k, wurzel, ff))), 3));
        }
        // Statuspunkt AUS (Stufe 3, grauer Ring statt der Marke): der Ring gegen die gedimmte Kachel.
        foreach (var m in Bildlauf.Nachfahren<Grid>(wurzel).Where(m => m.Name == "Punkt" && Zaehlt(m, wurzel) && m.DataContext is KachelViewModel { IstAus: true }))
        {
            var r = Rechteck(m, wurzel);
            ergebnis.Add(new Messung("Grenze Punkt AUS " + ((KachelViewModel)m.DataContext).Name, bild.Staerkste(r, bild.Haeufigste(Umgebung(m, wurzel, ff))), 3));
        }
        foreach (var rand in Bildlauf.Nachfahren<Border>(wurzel).Where(b => ReferenceEquals(b.Style, randStil) && b.DataContext is KachelViewModel { IstHoeren: true } or KachelViewModel { IstSenden: true }))
        {
            var r = Rechteck(rand, wurzel);
            var links = new Rect(r.X, r.Y + (r.Height / 3), 1.2, r.Height / 3);
            ergebnis.Add(new Messung("Rand " + ((KachelViewModel)rand.DataContext).ZustandText + " " + ((KachelViewModel)rand.DataContext).Name,
                bild.Staerkste(links, bild.Haeufigste(Umgebung(rand, wurzel, ff))), 3));
        }
        // Schiebeschalter 5/10 (A33): Spurrand gegen die Unterlage, weißer Knauf gegen die Spur (oben, über einer Zahl im Knauf).
        if (Zaehlt(ff.FeldSchalter, wurzel) && ff.FeldSchalter.Template.FindName("PART_Spur", ff.FeldSchalter) is Border spur
            && ff.FeldSchalter.Template.FindName("PART_Knauf", ff.FeldSchalter) is Border knaufS)
        {
            var r = Rechteck(spur, wurzel);
            // Der Spurrand am Ende gegenüber dem Knauf, auf halber Höhe: Dort liegt nur die Spur, oben und unten reicht der Knauf der Liste bis an den Rand.
            var randX = ff.FeldSchalter.IsChecked == true ? r.X : r.Right - 1.2;
            ergebnis.Add(new Messung("Schiebeschalter Spur", bild.Staerkste(new Rect(randX, r.Y + (r.Height / 2) - 1, 1.2, 2), bild.Haeufigste(Umgebung(spur, wurzel, ff))), 3));
            var k = Rechteck(knaufS, wurzel);
            ergebnis.Add(new Messung("Schiebeschalter Knauf", bild.Staerkste(new Rect(k.X + (k.Width / 2) - 1, k.Y + 1.2, 2, 1.2), bild.Haeufigste(r)), 3));
        }
        foreach (var regler in Bildlauf.Nachfahren<Slider>(wurzel).Where(s => Zaehlt(s, wurzel)))
        {
            // Lautstärke auf den Kacheln, Durchsichtigkeit in der Titelzeile (A30). Die Spur wird rechts gemessen, wo der
            // Wert sie nicht überdeckt; ganz oben (85 %) gibt es dort keine Spur mehr, nur den Knauf.
            var wo = regler.DataContext is KachelViewModel kv ? kv.Name : "Titelzeile";
            var grund = bild.Haeufigste(Umgebung(regler, wurzel, ff));
            var knauf = Bildlauf.Nachfahren<Thumb>(regler).Single();
            var k = Rechteck(knauf, wurzel);
            ergebnis.Add(new Messung("Knauf " + wo, bild.Staerkste(new Rect(k.X + (k.Width / 2) - 1, k.Y + (k.Height / 2) - 1, 2, 2), grund), 3));
            var r = Rechteck(regler, wurzel);
            if (regler.Value < regler.Maximum)
                ergebnis.Add(new Messung("Grenze Spur " + wo, bild.Staerkste(new Rect(r.Right - 4, r.Y + (r.Height / 2) - 1.5, 2, 3), grund), 3));
        }
        return ergebnis;
    }

    /// <summary>
    /// Sichtbar, nicht gesperrt und nicht in einem leeren Feld. Mit <paramref name="auchGesperrt"/> zählt es auch gesperrt: Der Name
    /// eines festen Felds steht in einer gesperrten Kanalwahl (Stufe 3) und muss trotzdem lesbar sein.
    /// </summary>
    private static bool Zaehlt(FrameworkElement e, FrameworkElement wurzel, bool auchGesperrt = false)
    {
        if ((!e.IsEnabled && !auchGesperrt) || e.ActualWidth < 1 || e.ActualHeight < 1 || e.DataContext is KachelViewModel { IstLeer: true }) return false;
        for (DependencyObject? d = e; d is not null && !ReferenceEquals(d, wurzel); d = VisualTreeHelper.GetParent(d))
        {
            if (d is UIElement { Visibility: not Visibility.Visible } or UIElement { Opacity: <= 0 }) return false;
        }
        return true;
    }

    /// <summary>Rechteck eines Elements in Einheiten, bezogen auf die Wurzel.</summary>
    private static Rect Rechteck(FrameworkElement e, FrameworkElement wurzel) =>
        e.TransformToAncestor(wurzel).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    /// <summary>
    /// Die Fläche, auf der ein Element steht: seine Kachel, die Unterlage des Reglers in der Titelzeile, seit Stufe 3 die Unterlage
    /// von Rollenliste und "Mehrere senden" (Raster) und die zweite Kopfzeile (Liste), seit Klangstufe C2b die des Mikro-Knopfs, sonst die
    /// Unterlage der Knöpfe.
    /// </summary>
    private static Rect Umgebung(FrameworkElement e, FrameworkElement wurzel, FunkfeldFenster ff)
    {
        for (DependencyObject? d = e; d is not null && !ReferenceEquals(d, wurzel); d = VisualTreeHelper.GetParent(d))
        {
            if (d is ContentPresenter { Content: KachelViewModel } kachel) return Rechteck(kachel, wurzel);
            if (ReferenceEquals(d, ff.ReglerUnterlage)) return Rechteck(ff.ReglerUnterlage, wurzel);
            if (ReferenceEquals(d, ff.MikroUnterlage)) return Rechteck(ff.MikroUnterlage, wurzel);
            if (ReferenceEquals(d, ff.RolleUnterlage)) return Rechteck(ff.RolleUnterlage, wurzel);
            if (ReferenceEquals(d, ff.Kopf2)) return Rechteck(ff.Kopf2, wurzel);
        }
        return Rechteck(ff.KnopfUnterlage, wurzel);
    }

    /// <summary>
    /// Bildpunkte eines gezeichneten Fensters, über reines Weiß gelegt wie bei der Fensterverwaltung: je Kanal
    /// vormultipliziert, mit der Deckkraft des ganzen Fensters. Koordinaten in Einheiten (175 %).
    /// </summary>
    internal sealed class Pixelbild
    {
        private readonly byte[] _rgb;
        private readonly Dictionary<int, double> _leuchtdichte = [];

        private Pixelbild(byte[] rgb, int breite, int hoehe)
        {
            _rgb = rgb;
            Breite = breite;
            Hoehe = hoehe;
        }

        public int Breite { get; }

        public int Hoehe { get; }

        public static Pixelbild UeberWeiss(BitmapSource bild, double fenster)
        {
            var b = bild.PixelWidth;
            var h = bild.PixelHeight;
            var p = new byte[b * h * 4];
            bild.CopyPixels(p, b * 4, 0);
            var rgb = new byte[b * h * 3];
            for (var i = 0; i < b * h; i++)
            {
                var a = p[(i * 4) + 3] * fenster / 255.0;
                for (var k = 0; k < 3; k++) rgb[(i * 3) + k] = (byte)Math.Round(Math.Clamp((p[(i * 4) + 2 - k] * fenster) + (255 * (1 - a)), 0, 255));
            }
            return new Pixelbild(rgb, b, h);
        }

        public Color Farbe(int x, int y)
        {
            var i = ((y * Breite) + x) * 3;
            return Color.FromRgb(_rgb[i], _rgb[i + 1], _rgb[i + 2]);
        }

        /// <summary>Die häufigste Farbe im Bereich: der Grund, auf dem Schrift oder Grenze steht.</summary>
        public Color Haeufigste(Rect einheiten)
        {
            var zaehler = new Dictionary<int, int>();
            foreach (var (x, y) in Punkte(einheiten))
            {
                var c = Farbe(x, y);
                var s = (c.R << 16) | (c.G << 8) | c.B;
                zaehler[s] = zaehler.GetValueOrDefault(s) + 1;
            }
            var max = zaehler.MaxBy(z => z.Value).Key;
            return Color.FromRgb((byte)(max >> 16), (byte)(max >> 8), (byte)max);
        }

        /// <summary>Das größte Kontrastverhältnis eines Bildpunkts im Bereich gegen <paramref name="grund"/>.</summary>
        public double Staerkste(Rect einheiten, Color grund)
        {
            var lg = Leuchtdichte(grund);
            var beste = 1.0;
            foreach (var (x, y) in Punkte(einheiten))
            {
                var l = Leuchtdichte(Farbe(x, y));
                beste = Math.Max(beste, (Math.Max(l, lg) + 0.05) / (Math.Min(l, lg) + 0.05));
            }
            return beste;
        }

        private double Leuchtdichte(Color c)
        {
            var s = (c.R << 16) | (c.G << 8) | c.B;
            if (!_leuchtdichte.TryGetValue(s, out var l))
            {
                l = Kontrast.Leuchtdichte(c);
                _leuchtdichte[s] = l;
            }
            return l;
        }

        private IEnumerable<(int X, int Y)> Punkte(Rect einheiten)
        {
            var x0 = Math.Clamp((int)Math.Floor(einheiten.Left * Skala), 0, Breite - 1);
            var y0 = Math.Clamp((int)Math.Floor(einheiten.Top * Skala), 0, Hoehe - 1);
            var x1 = Math.Clamp((int)Math.Ceiling(einheiten.Right * Skala), x0 + 1, Breite);
            var y1 = Math.Clamp((int)Math.Ceiling(einheiten.Bottom * Skala), y0 + 1, Hoehe);
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++) yield return (x, y);
            }
        }
    }
}
