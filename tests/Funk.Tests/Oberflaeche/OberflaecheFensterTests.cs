// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Rauchtest der Fenster ohne Anzeige: Hauptfenster (jeder Bereich mit der Statuszeile, die Seite Sprachaktivierung, A26) und Funkfeld
/// werden auf einem STA-Thread gebaut, ihre Vorlage angewandt,
/// gemessen und in eine RenderTargetBitmap gezeichnet. Es entsteht kein Fensterhandle, nichts erscheint auf dem
/// Bildschirm; das prüft der Test am Ende selbst, ebenso dass der Start von App nicht anläuft. Geprüft wird, dass XAML und Ressourcen laden, jeder Bereich sich zeichnen lässt, WPF keinen
/// Bindungsfehler meldet und die Titelleisten in der Leitfarbe stehen. Mit <c>FUNK_BILDER=&lt;Ordner unter C:\Claude&gt;</c>
/// legt er die Ansichten als PNG ab, Bilder für docs\bilder ohne Programmstart und ohne sichtbares Fenster.
/// </summary>
internal static class OberflaecheFensterTests
{
    [Test]
    public static void Fenster_BauenUndZeichnenOhneBindungsfehler()
    {
        // Ohne Handle erscheint nichts; neben einem Spiel läuft der Test trotzdem nicht, weil ein Fehler im Startschutz
        // von App hier Fenster und Geräte öffnen würde (29.09.2026 zweimal geschehen).
        if (Spielschutz.Grund() is { } grund)
        {
            Console.WriteLine("HINWEIS OberflaecheFensterTests übersprungen: " + grund);
            return;
        }
        var bindungsfehler = new List<string>();
        Exception? fehler = null;
        var t = new Thread(() =>
        {
            try
            {
                Lauf(bindungsfehler);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                fehler = e;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (fehler is not null) throw new PruefungException("Fenster ließen sich nicht bauen: " + fehler);
        Pruefe.Gleich(0, bindungsfehler.Count, "Bindungsfehler: " + string.Join(" | ", bindungsfehler.Take(4)));
    }

    private static void Lauf(List<string> bindungsfehler)
    {
        var zuhoerer = new Sammelzuhoerer(bindungsfehler);
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(zuhoerer);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        try
        {
            // Gegenprobe: Eine absichtlich kaputte Bindung muss beim Zuhörer ankommen, sonst beweist "keine Fehler" nichts.
            var probe = new System.Windows.Controls.TextBlock();
            probe.SetBinding(System.Windows.Controls.TextBlock.TextProperty, new System.Windows.Data.Binding("GibtEsNicht"));
            probe.DataContext = new object();
            Leeren();
            lock (bindungsfehler)
            {
                if (bindungsfehler.Count == 0) throw new PruefungException("Gegenprobe: der Zuhörer meldet keine Bindungsfehler");
                bindungsfehler.Clear();
            }

            App.SpracheSetzen();
            App.HilfeEinrichten();
            // App anlegen stellt ihren Start in die Warteschlange des Dispatchers, und Leeren() arbeitet ihn ab. Ohne den
            // Schutz in OnStartup liefe hier das ganze Programm an: Einstellungen im Profil, Tasten, Geräte, sichtbare
            // Fenster. Deshalb erst den Schutz prüfen, dann anlegen, und am Ende nachsehen, dass nichts davon geschah.
            if (App.AlsProgrammGestartet) throw new PruefungException("Der Testprozess gilt als TacRelay.exe; App würde voll starten.");
            var einstellungen = Funk.Client.Ablage.EinstellungsSpeicher.StandardPfad();
            var einstellungenVorher = File.Exists(einstellungen) ? File.GetLastWriteTimeUtc(einstellungen) : (DateTime?)null;
            if (Application.Current is null)
            {
                var app = new App();
                app.InitializeComponent();
            }
            // Die Testklassen laufen in der Folge ihrer Namen. Legt ein Bildlauf (OberflaecheBilder.cs) vorher seine schlichte
            // Application an, gehört sie einem anderen, beendeten Thread, und hier ginge jeder Zugriff darauf schief.
            if (Application.Current!.Dispatcher != System.Windows.Threading.Dispatcher.CurrentDispatcher)
                throw new PruefungException("Application.Current gehört einem anderen Thread: Ein Bildlauf lief vor diesem Test. Testklassen mit Bildlauf müssen im Namen nach OberflaecheFensterTests kommen.");
            Leeren();

            using var a = new Aufbau();
            // Wie App.OnStartup vor dem ersten Fenster: die Farbstellung der Einstellungen, Anthrazit.
            Farbvorschlaege.Anwenden(Application.Current.Resources, a.Ablage.Aktuell.Farbstellung);
            var leitfarbe = Farbvorschlaege.Von(a.Ablage.Aktuell.Farbstellung).Leitfarbe;
            Pruefe.Gleich(Farbvorschlaege.Voreinstellung, a.Ablage.Aktuell.Farbstellung, "ohne gespeicherte Wahl die Voreinstellung");
            // Globale Tasten mit einer Quelle, dazu eine Joystick-Taste ohne DirectInput (grau, "nicht verfügbar").
            a.Ablage.Aktuell.Tasten.Eintraege.Add(Funk.Client.Ablage.TastenEintrag.Aus(new Funk.Klang.Eingabe.Tastenzuordnung(
                Funk.Klang.Eingabe.Tastenfunktion.Sprechen, new Funk.Klang.Eingabe.Tastenbelegung(Funk.Klang.Eingabe.EingabeArt.Joystick, 3, "HID#VID_3344&PID_0194#1"))));
            a.Tastenquelle = new Maschine.TastenQuelleAttrappe(Funk.Klang.Eingabe.EingabeArt.Tastatur, Funk.Klang.Eingabe.EingabeArt.Maus);
            var h = a.Haupt(out var funkfeld, out var schwelle, out var audio, out _);
            // Punkt 2 und 6: gesperrtes Mikrofon (Warnung oben) und die offene Frage "Headset oder Lautsprecher?".
            a.Maschine.AufnahmeFehlerBeimStart = "Windows verweigert den Zugriff auf das Mikrofon.";
            audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            a.Maschine.FehlerMelden(new InvalidOperationException("Ein Audiostrom ist stehen geblieben: spk-1"));
            for (var i = 0; i < 400; i++)
                a.Maschine.Melden(new PegelMeldung(i * 20L, i % 50 < 25 ? -25 : -60, -61, -52, i % 50 < 25 ? 0.9f : 0.1f, i % 50 < 25));
            h.Takt();

            var haupt = new Hauptfenster(h, a.Ablage);
            // Seit 0.6.0 stehen beide nur auf der Startseite (Q10), nicht mehr über jedem Bereich.
            FrameworkElement Benannt(string name) => Bildlauf.Nachfahren<FrameworkElement>(haupt).First(e => e.Name == name);
            h.AktiverBereich = h.Bereiche[0];
            Zeichnen(haupt, 1120, 760, "haupt-erststart");
            // Seit 0.10.0 (Q76): Die Hörweise ist im ersten Start in der Spalte Hören umrandet, mit der Frage darunter; der Willkommenssatz steht nur
            // ohne laufende Verbindung (hier ist die Beispiel-Lage verbunden, dann steht dort Trennen).
            Pruefe.Gleich(Visibility.Visible, Benannt("HoerweiseFrage").Visibility, "Frage Headset oder Lautsprecher auf der Startseite");
            Pruefe.Gleich(Visibility.Visible, Benannt("MikrofonWarnung").Visibility, "Warnung beim Mikrofon: gesperrt");
            var seitenRand = (FrameworkElement)haupt.Schwellenbereich.Parent;
            var statuszeile = Benannt("Statuszeile");
            foreach (var bereich in h.Bereiche)
            {
                h.AktiverBereich = bereich;
                var bild = Zeichnen(haupt, 1120, 760, "haupt-" + bereich.Kennung);
                PruefeFarbe(bild, 700, 4, leitfarbe, "Titelleiste Hauptfenster (" + bereich.Titel + ")");
                // Seit 0.10.0 (Q87) steht unten auf jeder Seite die Statuszeile "Mikrofon" statt der Leiste der Sprachaktivierung.
                Pruefe.Wahr(statuszeile.Visibility == Visibility.Visible && statuszeile.ActualHeight > 20, "Statuszeile (" + bereich.Titel + ")");
            }
            Pruefe.Gleich(Visibility.Collapsed, seitenRand.Visibility, "Sprachaktivierung nur auf ihrer Seite");

            // A26, seit 0.10.0 nur noch als Seite (Unterpunkt unter Einstellungen): in allen Stellungen der Schalter von A28.
            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            foreach (var (automatik, lernen) in new[] { (false, false), (true, false), (true, true) })
            {
                schwelle.Automatik = automatik;
                schwelle.Lernen = lernen;
                var was = automatik ? lernen ? "Lernend" : "Automatisch" : "Fest";
                var bild = Zeichnen(haupt, 1280, 900, "schwelle-" + was.ToLowerInvariant());
                PruefeFarbe(bild, 700, 4, leitfarbe, "Titelleiste mit Sprachaktivierung (" + was + ")");
                Pruefe.Gleich(Visibility.Visible, seitenRand.Visibility, "Seite zu sehen (" + was + ")");
                var grafik = haupt.Schwellenbereich.Verlaufsgrafik;
                Pruefe.Wahr(grafik.ActualHeight >= 120 && grafik.ActualWidth >= 300, $"Grafik im Hauptfenster {grafik.ActualWidth:0}x{grafik.ActualHeight:0} ({was})");
                var inhalt = VisualTreeHelper.GetDrawing((Visual)VisualTreeHelper.GetChild(grafik, 0));
                Pruefe.Wahr(inhalt is not null && inhalt.Bounds.Width >= 300, "Grafik gezeichnet (" + was + ")");
            }
            audio.HeadsetWaehlenCommand.Execute(null);
            audio.WiederholenCommand.Execute(null);
            h.AktiverBereich = h.Bereiche[0];
            Zeichnen(haupt, 1120, 760);
            Pruefe.Gleich(Visibility.Collapsed, Benannt("HoerweiseFrage").Visibility, "beantwortet: Frage weg");
            Pruefe.Gleich(Visibility.Collapsed, Benannt("MikrofonWarnung").Visibility, "Mikrofon wieder da: Warnung weg");
            Zeichnen(haupt, 700, 600, "schmal");
            Pruefe.Wahr(Bildlauf.Nachfahren<System.Windows.Controls.TextBlock>(haupt).Any(t => t.Text == "Start" && t.ActualWidth > 0), "auch schmal bleiben die Namen der Leiste (Q7)");
            Pruefe.Wahr(statuszeile.ActualHeight > 20, "auch schmal steht die Statuszeile");
            Zeichnen(haupt, 1120, 760);
            Pruefe.Gleich(Visibility.Collapsed, seitenRand.Visibility, "auf Start keine Sprachaktivierung");

            // Das Funkfeld in seinen Ansichten (A29: Raster, Liste), frei gemessen: die Wunschgröße, aus der es seine optimale
            // Größe rechnet (A32).
            var ff = new FunkfeldFenster(funkfeld, a.Ablage);
            foreach (var (darstellung, anzahl, name) in new[]
                     {
                         (FunkfeldDarstellung.Raster, 10, "funkfeld-raster-10"), (FunkfeldDarstellung.Raster, 5, "funkfeld-raster-5"),
                         (FunkfeldDarstellung.Liste, 10, "funkfeld-liste-10"),
                     })
            {
                funkfeld.Darstellung = darstellung;
                funkfeld.Ansicht = anzahl;
                var (ffBild, groesse) = Bildlauf.Zeichnen(ff);
                Pruefe.Wahr(groesse.Width <= 800 && groesse.Height <= 345, $"{name}: kompakt, {groesse.Width:0} x {groesse.Height:0}");
                PruefeFarbe(ffBild, 4, 4, leitfarbe, "Marke FUNKFELD (" + name + ")");
                Ablegen(ffBild, name);
            }
            funkfeld.Dispose();

            Leeren();
            foreach (Window w in Application.Current!.Windows)
                Pruefe.Gleich(IntPtr.Zero, new System.Windows.Interop.WindowInteropHelper(w).Handle, "kein Fensterhandle, nichts auf dem Bildschirm: " + w.GetType().Name);
            var einstellungenNachher = File.Exists(einstellungen) ? File.GetLastWriteTimeUtc(einstellungen) : (DateTime?)null;
            Pruefe.Gleich(einstellungenVorher, einstellungenNachher, "App hat keine Einstellungen geschrieben: " + einstellungen);
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(zuhoerer);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
        }
    }

    /// <summary>Lässt den Dispatcher alles abarbeiten, was bis zur Leerlaufstufe ansteht (Bindungen, Layout).</summary>
    private static void Leeren() =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));

    /// <summary>
    /// Wendet die Vorlage an und zeichnet ihre Wurzel; das Fenster selbst bleibt ohne Handle. Mit <paramref name="bildname"/>
    /// legt es das Bild ab, wenn <c>FUNK_BILDER</c> gesetzt ist (<see cref="Ablegen"/>).
    /// </summary>
    private static RenderTargetBitmap Zeichnen(Window fenster, int breite, int hoehe, string? bildname = null)
    {
        fenster.Width = breite;
        fenster.Height = hoehe;
        fenster.ApplyTemplate();
        var wurzel = (FrameworkElement)VisualTreeHelper.GetChild(fenster, 0);
        wurzel.Measure(new Size(breite, hoehe));
        wurzel.Arrange(new Rect(0, 0, breite, hoehe));
        wurzel.UpdateLayout();
        Leeren();
        var bild = new RenderTargetBitmap(breite, hoehe, 96, 96, PixelFormats.Pbgra32);
        bild.Render(wurzel);
        if (bildname is not null) Ablegen(bild, bildname);
        return bild;
    }

    /// <summary>
    /// Bilder der Oberfläche ohne Programmstart, statt tests\oberflaeche-bilder.ps1 mit echten, sichtbaren Fenstern: Mit
    /// <c>FUNK_BILDER=&lt;Ordner unter C:\Claude&gt;</c> legt der Test jede benannte Ansicht als PNG ab. Die Titelleiste
    /// zeichnet das Programm selbst (WindowChrome), im Bild fehlt also nichts.
    /// </summary>
    private static void Ablegen(BitmapSource bild, string name)
    {
        var ordner = Environment.GetEnvironmentVariable("FUNK_BILDER");
        if (string.IsNullOrWhiteSpace(ordner)) return;
        var ziel = Path.GetFullPath(ordner.Trim());
        if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_BILDER muss unter C:\Claude liegen: " + ziel);
        Directory.CreateDirectory(ziel);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bild));
        using var datei = File.Create(Path.Combine(ziel, name + ".png"));
        png.Save(datei);
    }

    private static void PruefeFarbe(BitmapSource bild, int x, int y, Color soll, string was)
    {
        var pixel = new byte[4];
        bild.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        var ist = Color.FromRgb(pixel[2], pixel[1], pixel[0]);
        Pruefe.Wahr(Math.Abs(ist.R - soll.R) <= 2 && Math.Abs(ist.G - soll.G) <= 2 && Math.Abs(ist.B - soll.B) <= 2,
            $"{was}: erwartet {soll}, war {ist}");
    }

    private sealed class Sammelzuhoerer(List<string> ziel) : TraceListener
    {
        private string _zeile = "";

        public override void Write(string? message) => _zeile += message;

        public override void WriteLine(string? message)
        {
            lock (ziel) ziel.Add(_zeile + message);
            _zeile = "";
        }
    }
}
