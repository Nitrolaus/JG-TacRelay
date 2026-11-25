// JG TacRelay - Maintainer: Sarevoc
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Spielprofil (Umbau JG TacRelay, Sarevoc 18./19.10.2026, Q10): Unter Einstellungen > Sprecheranzeige > Erweitert steht "Spiel" mit Star Citizen ab Werk
/// und "Anderes Spiel" mit dem Namen der Programmdatei (eintippen oder wählen). Danach suchen Sprecheranzeige, Meldeanzeige und Meldezeile den Monitor;
/// die Hinweise sagen "das Spiel" statt "Star Citizen". Geprüft: der Prozessname aus einer Eingabe, das wirksame Spiel, die Ablage (nachsichtig),
/// das Modell mit Dateiwahl und Werkseinstellung, die Monitorsuche im Quelltext der App, die Texte und im Bildlauf (kein Fenster auf dem Bildschirm)
/// die Steuerelemente. Mit FUNK_BILDER das Bild vorschau-0119-einstellungen-spielprofil.
/// </summary>
internal static class OberflaecheSpielprofilTests
{
    [Test]
    public static void Spielprofil_Prozessname_AusEingabe_UndWirksam()
    {
        foreach (var (eingabe, soll) in new (string?, string?)[]
                 {
                     ("Spiel.exe", "Spiel"), (@"C:\Spiele\Elite Dangerous\EliteDangerous64.EXE", "EliteDangerous64"), ("  StarCitizen  ", "StarCitizen"),
                     ("\"D:\\x\\y.exe\"", "y"), ("a/b/c.exe", "c"), ("", null), ("   ", null), (null, null), ("x<y", null), ("ge|teilt", null),
                     (new string('a', Spielprofil.ProzessnameMax + 1), null), (".exe", null),
                 })
            Pruefe.Gleich(soll, Spielprofil.Prozessname(eingabe), "Prozessname aus " + (eingabe ?? "null"));

        var e = new SpielEinstellungen();
        Pruefe.Gleich("StarCitizen", Spielprofil.Prozess(e), "ab Werk Star Citizen");
        Pruefe.Gleich("Star Citizen", Spielprofil.Name(e), "Name ab Werk");
        Pruefe.Gleich(Spielfenster.Prozess, Spielprofil.Prozess(e), "derselbe Prozess wie bisher fest im Spielfenster");
        e.Anderes = true;
        Pruefe.Gleich("StarCitizen", Spielprofil.Prozess(e), "anderes Spiel ohne Namen: weiter Star Citizen");
        e.Prozess = "EliteDangerous64";
        Pruefe.Gleich("EliteDangerous64", Spielprofil.Prozess(e), "das andere Spiel");
        Pruefe.Gleich("EliteDangerous64.exe", Spielprofil.Name(e), "Name mit .exe");
        e.Anderes = false;
        Pruefe.Gleich("StarCitizen", Spielprofil.Prozess(e), "zurück auf Star Citizen, der Name bleibt gemerkt");

        // Die Suche selbst liest nur die Prozessliste: ein Name, den es nicht gibt, ergibt keinen Monitor und wirft nicht.
        Pruefe.Gleich<Bildschirm?>(null, Spielfenster.Monitor("GibtEsNichtJgTacRelay4711"), "kein Prozess: kein Monitor");
        Pruefe.Gleich<Bildschirm?>(null, Spielfenster.MonitorImVordergrund("GibtEsNichtJgTacRelay4711"), "kein Prozess vorn: kein Monitor");
        Pruefe.Gleich<Bildschirm?>(null, Spielfenster.Monitor(""), "leerer Name: kein Monitor");
    }

    [Test]
    public static void Spielprofil_Ablage_NachsichtigGelesen_UndGeschrieben()
    {
        using var o = new Testordner();
        var datei = o.Datei("einstellungen.json");
        var s = new EinstellungsSpeicher(datei);
        var leer = s.Laden();
        Pruefe.Wahr(leer.Spiel is { Anderes: false, Prozess: null }, "ohne Abschnitt: Star Citizen");

        File.WriteAllText(datei, """{ "spiel": { "anderes": "ja", "prozess": 5 } }""", new UTF8Encoding(false));
        var kaputt = s.Laden();
        Pruefe.Gleich(null, s.LetzterFehler, "lesbar trotz falscher Werte");
        Pruefe.Wahr(kaputt.Spiel is { Anderes: false, Prozess: null }, "falsche Werte: ab Werk");
        File.WriteAllText(datei, """{ "spiel": { "anderes": true, "prozess": "C:\\Spiele\\X\\Spiel.exe" } }""", new UTF8Encoding(false));
        var pfad = s.Laden();
        Pruefe.Wahr(pfad.Spiel is { Anderes: true, Prozess: "Spiel" }, "ein Pfad wird beim Lesen zum Prozessnamen: " + pfad.Spiel.Prozess);

        var n = new ClientEinstellungen();
        n.Spiel.Anderes = true;
        n.Spiel.Prozess = "Spiel";
        s.Speichern(n);
        var text = File.ReadAllText(datei);
        Pruefe.Wahr(text.Contains("\"spiel\"", StringComparison.Ordinal) && text.Contains("\"anderes\": true", StringComparison.Ordinal)
            && text.Contains("\"prozess\": \"Spiel\"", StringComparison.Ordinal), "geschrieben");
        s.Speichern(new ClientEinstellungen());
        Pruefe.Falsch(File.ReadAllText(datei).Contains("\"prozess\"", StringComparison.Ordinal), "ohne Namen wird kein Prozess geschrieben");
    }

    [Test]
    public static void Spielprofil_Modell_WahlDateiUndWerkseinstellung()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        string? gewaehlt = @"C:\Spiele\Elite Dangerous\EliteDangerous64.exe";
        var s = new SprecheranzeigeViewModel(a.Ablage, () => a.Bildschirme, dateiWaehlen: () => gewaehlt);
        Pruefe.Wahr(s.SpielStarCitizen && !s.SpielAnderes && s.SpielProzess.Length == 0 && s.SpielWert.Length == 0, "ab Werk Star Citizen");
        Pruefe.Wahr(s.SpielSatz.Contains("Star Citizen", StringComparison.Ordinal), "der Satz nennt Star Citizen: " + s.SpielSatz);

        s.SpielAnderes = true;
        Pruefe.Wahr(a.Ablage.Aktuell.Spiel is { Anderes: true, Prozess: null } && !s.SpielStarCitizen, "anderes Spiel gewählt, noch ohne Namen");
        Pruefe.Wahr(s.SpielSatz.StartsWith("Trag die Programmdatei", StringComparison.Ordinal), "der Satz sagt, was fehlt: " + s.SpielSatz);
        Pruefe.Gleich("StarCitizen", Spielprofil.Prozess(a.Ablage.Aktuell.Spiel), "bis dahin gilt Star Citizen");

        Pruefe.Wahr(s.SpielDateiWaehlenCommand.CanExecute(null), "mit Dateiwahl frei");
        s.SpielDateiWaehlenCommand.Execute(null);
        Pruefe.Wahr(s.SpielProzess == "EliteDangerous64" && a.Ablage.Aktuell.Spiel.Prozess == "EliteDangerous64", "die gewählte Programmdatei als Prozessname");
        Pruefe.Gleich("EliteDangerous64", Spielprofil.Prozess(a.Ablage.Aktuell.Spiel), "danach sucht die Monitorsuche");
        Pruefe.Wahr(s.SpielSatz.Contains("EliteDangerous64.exe", StringComparison.Ordinal), "der Satz nennt das Spiel: " + s.SpielSatz);
        Pruefe.Gleich("Spiel EliteDangerous64.exe", SprecheranzeigeViewModel.SpielText(s.SpielWert), "für selbst gestellt");

        gewaehlt = null;
        s.SpielDateiWaehlenCommand.Execute(null);
        Pruefe.Gleich("EliteDangerous64", s.SpielProzess, "abgebrochen: nichts geändert");
        s.SpielProzess = "x<y";
        Pruefe.Gleich("EliteDangerous64", s.SpielProzess, "ein ungültiger Name ändert nichts");
        s.SpielProzess = "Spiel.exe";
        Pruefe.Wahr(a.Ablage.Aktuell.Spiel is { Anderes: true, Prozess: "Spiel" }, "eingetippt mit .exe");

        s.SpielStarCitizen = true;
        Pruefe.Wahr(!s.SpielAnderes && a.Ablage.Aktuell.Spiel.Prozess == "Spiel" && Spielprofil.Prozess(a.Ablage.Aktuell.Spiel) == "StarCitizen",
            "zurück auf Star Citizen; der Name bleibt für später");
        s.SpielWert = "Spiel";
        Pruefe.Wahr(s.SpielAnderes && s.SpielWert == "Spiel", "der Wert für Werkseinstellung setzt das andere Spiel");
        s.SpielWert = "";
        Pruefe.Wahr(s.SpielStarCitizen && s.SpielWert.Length == 0, "Werkseinstellung: Star Citizen");

        var ohne = new SprecheranzeigeViewModel(a.Ablage, () => a.Bildschirme);
        Pruefe.Falsch(ohne.SpielDateiWaehlenCommand.CanExecute(null), "ohne Dateiwahl (Tests) ist der Knopf gesperrt");

        // Unter Erweitert zählt das Spiel als selbst gestellt mit, und "Zurück auf Werkseinstellung" nimmt Star Citizen.
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var vm = h.Einstellungen.Sprecheranzeige;
        var f = h.Einstellungen.AbschnittSprecheranzeige!.Feinheiten;
        Pruefe.Falsch(f.SelbstGestellt.Any(x => x.StartsWith("Spiel", StringComparison.Ordinal)), "Star Citizen zählt nicht als selbst gestellt");
        vm.SpielProzess = "Spiel";
        Pruefe.Wahr(f.StandSatz.Contains("Spiel Spiel.exe", StringComparison.Ordinal) && f.EtwasSelbstGestellt, "selbst gestellt: " + f.StandSatz);
        f.ZurueckCommand.Execute(null);
        Pruefe.Wahr(vm.SpielStarCitizen && Spielprofil.Prozess(a.Ablage.Aktuell.Spiel) == "StarCitizen", "nach Zurück auf Werkseinstellung wieder Star Citizen");
        funkfeld.Dispose();
        Pruefe.Wahr(EinstellungenViewModel.InhaltSprecheranzeigeErweitert.EndsWith("Spiel", StringComparison.Ordinal), "Erweitert nennt das Spiel");
    }

    /// <summary>Die Monitorsuche nimmt das gewählte Spiel: Sprecheranzeige, Meldeanzeige und Meldezeile in App.xaml.cs, keine feste Suche mehr.</summary>
    [Test]
    public static void Spielprofil_App_MonitorsucheMitDemGewaehltenSpiel()
    {
        var app = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "App.xaml.cs"));
        Pruefe.Gleich(2, Vorkommen(app, "Spielfenster.MonitorImVordergrund(Spielprofil.Prozess(_ablage.Aktuell.Spiel))"), "Sprecheranzeige und Meldeanzeige");
        Pruefe.Gleich(1, Vorkommen(app, "Spielfenster.Monitor(Spielprofil.Prozess(_ablage.Aktuell.Spiel))"), "Meldezeile");
        Pruefe.Falsch(app.Contains("spiel: Spielfenster.Monitor", StringComparison.Ordinal), "keine Suche mehr fest nach Star Citizen");
        Pruefe.Wahr(app.Contains("dateiWaehlen: Spieldatei.Waehlen", StringComparison.Ordinal), "der Knopf öffnet im Betrieb den Dateidialog");
    }

    private static int Vorkommen(string text, string teil)
    {
        var n = 0;
        for (var i = text.IndexOf(teil, StringComparison.Ordinal); i >= 0; i = text.IndexOf(teil, i + teil.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>Hinweise sagen "das Spiel" statt "Star Citizen", wo es um Monitor und Randlos geht.</summary>
    [Test]
    public static void Spielprofil_Hinweise_NennenDasSpiel()
    {
        foreach (var (name, text) in new[]
                 {
                     ("Sprecheranzeige Automatik", SprecheranzeigeViewModel.AutomatikText), ("Sprecheranzeige Erklärung", SprecheranzeigeViewModel.Erklaerung),
                     ("Sprecheranzeige Vollbild", SprecheranzeigeViewModel.VollbildText), ("Sprecheranzeige An", SprecheranzeigeViewModel.AnHilfe),
                     ("Sprecheranzeige Monitor", SprecheranzeigeViewModel.MonitorHilfe), ("Meldungen Automatik", MeldungenEinstellungViewModel.AutomatikText),
                     ("Meldezeile Monitor", MeldezeilePlatzViewModel.MonitorHilfe),
                 })
            Pruefe.Falsch(text.Contains("Star Citizen", StringComparison.Ordinal), name + ": " + text);
        Pruefe.Wahr(SprecheranzeigeViewModel.Erklaerung.EndsWith("Das Spiel muss dafür randlos laufen.", StringComparison.Ordinal), "Randlos: das Spiel");
        Pruefe.Wahr(SprecheranzeigeViewModel.VollbildText.StartsWith("Das Spiel läuft im Vollbild", StringComparison.Ordinal), "Vollbild: das Spiel");
    }

    [Test]
    public static void Spielprofil_ImAbschnittSprecheranzeige_Erweitert()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            a.Ablage.Aktuell.Spiel.Anderes = true;
            a.Ablage.Aktuell.Spiel.Prozess = "EliteDangerous64";
            var h = a.Haupt(out _, out _, out _, out _);
            Seiten.Feinheiten(h, Abschnittskennung.Sprecheranzeige);
            h.Takt();
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            var bild = Ganz(haupt, 1100);
            var abschnitt = Teil<StackPanel>(Seite(haupt), "AbschnittSprecheranzeige");
            var sc = Teil<RadioButton>(abschnitt, "SpielStarCitizen");
            var anderes = Teil<RadioButton>(abschnitt, "SpielAnderes");
            var feld = Teil<TextBox>(abschnitt, "SpielProzess");
            var knopf = Teil<Button>(abschnitt, "SpielDateiWaehlen");
            Pruefe.Wahr(ZuSehen(sc) && ZuSehen(anderes) && ZuSehen(feld) && ZuSehen(knopf), "Spiel mit beiden Wahlen, Feld und Knopf");
            Pruefe.Wahr(sc.IsChecked == false && anderes.IsChecked == true && feld.Text == "EliteDangerous64", "das andere Spiel steht da");
            Pruefe.Gleich("Star Citizen (ab Werk)", sc.Content, "Beschriftung ab Werk");
            Pruefe.Wahr(Sichtbar(abschnitt, "Gesucht wird EliteDangerous64.exe"), "der Satz darunter");
            foreach (var x in new FrameworkElement[] { sc, anderes, feld, knopf })
                Pruefe.Wahr(Glasdesign.Hilfe.GetText(x) is { Length: > 40 } && System.Windows.Automation.AutomationProperties.GetName(x).Length > 5, x.Name + ": Hilfe und Name");
            Pruefe.Wahr(NichtsAbgeschnitten(abschnitt, "Sprecheranzeige mit Spiel") >= 30, "nichts abgeschnitten");
            Bildablage.Ablegen(Ausschnitt(haupt, bild, "AbschnittSprecheranzeige"), "vorschau-0119-einstellungen-spielprofil");
        });
    }

    private static ContentPresenter Seite(Hauptfenster haupt) =>
        Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);

    private static T Teil<T>(FrameworkElement wurzel, string name) where T : FrameworkElement =>
        Bildlauf.Nachfahren<T>(wurzel).FirstOrDefault(e => e.Name == name) ?? throw new PruefungException(name + " fehlt");

    /// <summary>Ein Ausschnitt um einen Abschnitt der Einstellungen über die Breite der Karte (wie in OberflaecheSprecheranzeigeTests).</summary>
    private static System.Windows.Media.Imaging.CroppedBitmap Ausschnitt(Hauptfenster haupt, System.Windows.Media.Imaging.BitmapSource ganz, string abschnittName)
    {
        var wurzel = Bildlauf.Wurzel(haupt);
        var seite = Seite(haupt);
        var karte = Teil<Border>(seite, "AbschnitteKarte");
        var rk = karte.TransformToAncestor(wurzel).TransformBounds(new Rect(karte.RenderSize));
        var abschnitt = Teil<StackPanel>(seite, abschnittName);
        var r = abschnitt.TransformToAncestor(wurzel).TransformBounds(new Rect(abschnitt.RenderSize));
        var links = (int)Math.Max(0, Math.Floor(rk.Left - 8));
        var oben = (int)Math.Max(0, Math.Floor(r.Top - 14));
        return new System.Windows.Media.Imaging.CroppedBitmap(ganz, new Int32Rect(links, oben, (int)Math.Min(ganz.PixelWidth - links, Math.Ceiling(rk.Width + 16)),
            (int)Math.Min(ganz.PixelHeight - oben, Math.Ceiling(r.Height + 28))));
    }
}
