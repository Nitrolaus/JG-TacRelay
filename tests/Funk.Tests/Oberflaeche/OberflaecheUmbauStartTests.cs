// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Eingabe;
using Funk.Tests.Maschine;
using Funk.Tests.Verarbeitung;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// 0.10.0, AP2 (Start und erster Start nach der Mischung, Q87 bis Q92, E7 bis E10, E17; Bilder m-1, m-2 und m-7c): Start in zwei Spalten
/// "Sprechen" und "Hören", unter etwa 850 Fensterbreite untereinander (Q88), mit offener Spalte "Wer ist da" seit Q93 a in Klein nebeneinander
/// (verbreitert oder unter der Schublade, OberflaecheWerIstDaPlatzTests), nur maximiert auf schmalen Bildschirmen untereinander; der erste Start mit
/// Willkommenssatz, umrandeten Angaben, dem Schritt "Drück jetzt deine Sprechtaste", Esc und "Später" (Q76, E8); die Sprechtaste mit Namen,
/// "Ändern" und der Warnung ohne Taste (E17); die Funk-Lautstärke (Q74, E7) in Datei, Regler und Maschine; die eine Hörprobe auch verbunden
/// (Q75); "Audio starten" auf Start (E9). Gezeichnet ohne Fensterhandle (<see cref="Bildlauf"/>), mit Kontrast, Glasschatten und der Prüfung
/// <see cref="Ansichtspruefung.NichtsAbgeschnitten"/>. Der Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheUmbauStartTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Die zwei Angaben des ersten Starts, umrandet (E8).</summary>
    private static readonly string[] Rahmen = ["SprechtasteRahmen", "HoerweiseRahmen"];

    // ------------------------------------------------------------------------------------------------------------------------------
    // Modell
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Funk-Lautstärke (E7): Ab Werk 100 %, für bisherige Nutzer ändert sich nichts. Nachsichtig gelesen: Fehlt das Feld, ist es null, keine Zahl
    /// oder unter 0, gilt ab Werk (ein Fehler macht den Funk nie stumm); über 100 wird 100, Kommazahlen werden gerundet. Der Faktor für die
    /// Maschine ist Prozent zum Quadrat (50 % etwa -12 dB, 0 % stumm). Der Regler setzt in Schritten zu 5, gemerkt und sofort an die Maschine.
    /// </summary>
    [Test]
    public static void Umbau_FunkLautstaerke_DateiFaktorUndRegler()
    {
        using var o = new Testordner();
        var pfad = o.Datei("client-einstellungen.json");
        var speicher = new EinstellungsSpeicher(pfad);
        Pruefe.Gleich(100, speicher.Laden().Audio.FunkLautstaerke, "ohne Datei ab Werk 100");
        File.WriteAllText(pfad, "{ \"audio\": { \"echounterdrueckung\": false } }");
        Pruefe.Wahr(speicher.Laden().Audio is { FunkLautstaerke: 100, Echounterdrueckung: false }, "alte Datei (0.9.3): 100, der Rest gilt");
        foreach (var (json, soll) in new[] { ("null", 100), ("\"abc\"", 100), ("-5", 100), ("250", 100), ("55.5", 56), ("\"40\"", 40), ("0", 0), ("true", 100), ("[1]", 100) })
        {
            File.WriteAllText(pfad, "{ \"audio\": { \"funkLautstaerkeProzent\": " + json + ", \"echounterdrueckung\": false } }");
            var l = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, json + ": kein Fehler");
            Pruefe.Gleich(soll, l.Audio.FunkLautstaerke, json);
            Pruefe.Gleich<int?>(soll, l.Audio.FunkLautstaerkeProzent, json + ": das Feld selbst ist begrenzt");
            Pruefe.Falsch(l.Audio.Echounterdrueckung, json + ": der Rest bleibt");
        }
        Pruefe.Gleich(0, Directory.GetFiles(o.Pfad, "*.defekt-*").Length, "nichts beiseitegelegt");
        var e = new ClientEinstellungen();
        e.Audio.FunkLautstaerkeProzent = 80;
        speicher.Speichern(e);
        Pruefe.Wahr(File.ReadAllText(pfad).Contains("\"funkLautstaerkeProzent\": 80", StringComparison.Ordinal), "in der Datei als audio.funkLautstaerkeProzent");
        Pruefe.Gleich(80, speicher.Laden().Audio.FunkLautstaerke, "hin und zurück");

        PruefeWert.Nahe(1, AudioEinstellungen.FunkLautstaerkeFaktor(100), 1e-9, "100 %: unverändert");
        PruefeWert.Nahe(0, AudioEinstellungen.FunkLautstaerkeFaktor(0), 1e-9, "0 %: stumm");
        PruefeWert.Nahe(-12.04, 20 * Math.Log10(AudioEinstellungen.FunkLautstaerkeFaktor(50)), 0.01, "50 %: etwa -12 dB");

        using var a = new Aufbau(FunkLage.Leer);
        a.Ablage.Aktuell.Audio.FunkLautstaerkeProzent = 60;
        var audio = a.Audio();
        PruefeWert.Nahe(0.36, a.Maschine.FunkLautstaerke, 1e-6, "beim Aufbau der gemerkte Wert an die Maschine");
        Pruefe.Gleich("60 %", audio.FunkLautstaerkeText, "Text neben dem Regler");
        var gemeldet = new List<string?>();
        audio.PropertyChanged += (_, p) => gemeldet.Add(p.PropertyName);
        audio.FunkLautstaerke = 80;
        Pruefe.Wahr(a.Ablage.Aktuell.Audio.FunkLautstaerke == 80 && audio.FunkLautstaerkeText == "80 %", "gemerkt: " + audio.FunkLautstaerkeText);
        PruefeWert.Nahe(0.64, a.Maschine.FunkLautstaerke, 1e-6, "an die Maschine: (80/100)^2");
        Pruefe.Wahr(gemeldet.Contains(nameof(AudioViewModel.FunkLautstaerke)) && gemeldet.Contains(nameof(AudioViewModel.FunkLautstaerkeText)), "gemeldet");
        audio.FunkLautstaerke = 37;
        Pruefe.Gleich(35, a.Ablage.Aktuell.Audio.FunkLautstaerke, "auf 5 gerundet");
        audio.FunkLautstaerke = double.NaN;
        Pruefe.Gleich(35, a.Ablage.Aktuell.Audio.FunkLautstaerke, "keine Zahl ändert nichts");
        audio.FunkLautstaerke = 0;
        Pruefe.Gleich(0f, a.Maschine.FunkLautstaerke, "ganz links: stumm");
        Pruefe.Wahr(AudioViewModel.FunkLautstaerkeHilfe.Contains("Windows und das Spiel", StringComparison.Ordinal)
            && AudioViewModel.FunkLautstaerkeHilfe.Contains("Ab Werk", StringComparison.Ordinal), "Hovertext sagt, was es tut und was ab Werk gilt");
    }

    /// <summary>
    /// Der Schritt Sprechtaste (E8) in der Datei: Eine alte Datei mit gewählter Hörweise bekommt keinen Schritt (null bleibt null und wird nicht
    /// geschrieben), eine neue (ohne Hörweise und ohne Sprechtaste) den offenen Schritt. Nachsichtig gelesen.
    /// </summary>
    [Test]
    public static void Umbau_ErststartSprechtaste_DateiAltUndNeu()
    {
        using (var alt = new Aufbau(FunkLage.Leer))
        {
            alt.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
            var h = alt.Haupt(out var ff, out _, out _, out _);
            Pruefe.Gleich((bool?)null, alt.Ablage.Aktuell.Tasten.ErststartSprechtaste, "alte Datei mit Hörweise: kein Schritt");
            Pruefe.Falsch(h.Start.ErststartOffen || h.Start.SprechtasteSchrittOffen, "Start ohne ersten Start");
            Pruefe.Wahr(h.Start.ZeigtSprechtasteWarnung, "ohne Taste die Warnung");
            alt.Speicher.Speichern(alt.Ablage.Aktuell);
            Pruefe.Falsch(File.ReadAllText(alt.Speicher.Pfad).Contains("erststartSprechtaste", StringComparison.Ordinal), "null wird nicht geschrieben");
            ff.Dispose();
        }
        using (var neu = new Aufbau(FunkLage.Leer))
        {
            var h = neu.Haupt(out var ff, out _, out _, out _);
            Pruefe.Gleich((bool?)false, neu.Ablage.Aktuell.Tasten.ErststartSprechtaste, "neue Datei: Schritt offen");
            Pruefe.Wahr(h.Start.ErststartOffen && h.Start.SprechtasteSchrittOffen, "erster Start");
            neu.Ablage.Sichern();
            Pruefe.Gleich((bool?)false, neu.Speicher.Laden().Tasten.ErststartSprechtaste, "gemerkt");
            ff.Dispose();
        }
        using (var mitTaste = new Aufbau(FunkLage.Leer))
        {
            OberflaecheStartseiteTests.SprechtasteBelegen(mitTaste);
            _ = mitTaste.Haupt(out var ff, out _, out _, out _);
            Pruefe.Gleich((bool?)null, mitTaste.Ablage.Aktuell.Tasten.ErststartSprechtaste, "ohne Hörweise, aber mit Taste: kein Schritt");
            ff.Dispose();
        }
        using var o = new Testordner();
        var pfad = o.Datei("client-einstellungen.json");
        var speicher = new EinstellungsSpeicher(pfad);
        foreach (var (json, soll) in new (string, bool?)[] { ("true", true), ("false", false), ("1", true), ("\"false\"", false), ("null", null), ("\"abc\"", null), ("7", null) })
        {
            File.WriteAllText(pfad, "{ \"tasten\": { \"erststartSprechtaste\": " + json + " }, \"audio\": { \"echounterdrueckung\": false } }");
            var l = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, json + ": kein Fehler");
            Pruefe.Gleich(soll, l.Tasten.ErststartSprechtaste, json);
            Pruefe.Falsch(l.Audio.Echounterdrueckung, json + ": der Rest bleibt");
        }
    }

    /// <summary>
    /// Der erste Start im Modell (E8, Q76): Mit globalen Tasten wartet Start von selbst und ohne Frist auf die Sprechtaste, aber erst, wenn das
    /// Fenster vorn ist, und nur solange Start vorn ist; beim Verlassen und wenn das Fenster nicht mehr vorn ist, endet das Warten still und
    /// beginnt danach wieder. Esc bricht ab, dann steht dort "Belegen" und Start wartet nicht mehr von selbst. Eine Taste erledigt den Schritt.
    /// "Später" erledigt ihn ohne Taste, danach die Warnung. Ohne globale Tasten wartet nichts, "Belegen" ist gesperrt.
    /// </summary>
    [Test]
    public static async Task Umbau_Erststart_WartenEscSpaeterUndOhneQuelle()
    {
        using var a = new Aufbau(FunkLage.Leer);
        var quelle = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        a.Tastenquelle = quelle;
        var h = a.Haupt(out var ff, out _, out var audio, out _);
        var s = h.Start;
        Pruefe.Wahr(s.ErststartOffen && s.ZeigtErststartHinweis, "erster Start");
        Pruefe.Gleich("Willkommen. Zwei Angaben fehlen noch, dann kommt hier Verbinden.", s.ErststartSatz, "Satz mit zwei Angaben");
        Pruefe.Falsch(s.Sprechtaste.Wartet, "solange das Fenster nicht vorn war, wartet nichts");
        h.FensterAktiviert();
        Pruefe.Wahr(s.Sprechtaste.Wartet && s.ZeigtWarteFeld && !s.ZeigtSchrittBelegen, "Start vorn: wartet von selbst, ohne Belegen");
        // Befund der Prüfung: Der Schritt wartet vorsichtig, sonst würden Alt aus Alt+Tab, Strg aus Strg+2 oder Tab zur Sprechtaste.
        Pruefe.Gleich((bool?)true, quelle.ZuletztVorsichtig, "der Schritt belegt vorsichtig");
        h.FensterDeaktiviert();
        Pruefe.Falsch(s.Sprechtaste.Wartet, "Fenster nicht mehr vorn: kein Warten im Hintergrund");
        h.FensterAktiviert();
        Pruefe.Wahr(s.Sprechtaste.Wartet, "wieder vorn: wartet wieder");
        h.AktiverBereich = Seiten.Einstellungen(h);
        Pruefe.Falsch(s.Sprechtaste.Wartet, "Start verlassen: endet still");
        Pruefe.Gleich(null, s.Tasten.Hinweis, "ohne Hinweis");
        h.AktiverBereich = Seiten.Start(h);
        Pruefe.Wahr(s.Sprechtaste.Wartet, "zurück auf Start: wartet wieder");

        // Esc: abgebrochen, "Belegen" steht da, Start wartet nicht mehr von selbst.
        quelle.NaechsteTaste = Tastenbelegung.Taste(0x0001);
        s.Tasten.WartenBeenden();
        h.AktiverBereich = Seiten.Einstellungen(h);
        h.AktiverBereich = Seiten.Start(h);
        await s.Warten!.WaitAsync(TimeSpan.FromSeconds(5));
        Pruefe.Wahr(!s.Sprechtaste.Wartet && s.ZeigtSchrittBelegen && s.SprechtasteSchrittOffen, "Esc: Belegen statt Wartefeld, der Schritt bleibt");
        h.AktiverBereich = Seiten.Einstellungen(h);
        h.AktiverBereich = Seiten.Start(h);
        Pruefe.Falsch(s.Sprechtaste.Wartet, "nach Esc wartet Start nicht mehr von selbst");

        // Belegen: wieder ohne Frist; eine Taste erledigt den Schritt.
        quelle.NaechsteTaste = null;
        s.SprechtasteBelegenCommand.Execute(null);
        Pruefe.Wahr(s.Sprechtaste.Wartet, "Belegen wartet wieder");
        quelle.NaechsteTaste = Tastenbelegung.Maus(Tastenbelegung.MausX1);
        s.Tasten.WartenBeenden();
        s.SprechtasteBelegenCommand.Execute(null);
        await s.Warten!.WaitAsync(TimeSpan.FromSeconds(5));
        Pruefe.Falsch(s.SprechtasteSchrittOffen, "eine Taste erledigt den Schritt");
        Pruefe.Wahr(s.ErststartOffen && audio.FrageOffen, "die Hörweise fehlt noch");
        Pruefe.Gleich("Willkommen. Eine Angabe fehlt noch, dann kommt hier Verbinden.", s.ErststartSatz, "Satz mit einer Angabe");
        Pruefe.Gleich("Unten umrandet: womit du hörst.", s.ErststartUmrandet, "was umrandet ist");
        Pruefe.Gleich(StartViewModel.ErstHoerweise, s.VerbindenGesperrt, "Verbinden wartet auf die Hörweise");
        Pruefe.Wahr(s.ZeigtSprechtasteBelegt && !s.ZeigtSprechtasteWarnung, "die Taste steht mit Namen da");
        audio.Headset = true;
        Pruefe.Wahr(!s.ErststartOffen && s.ZeigtVerbinden && !s.ZeigtErststartHinweis, "beides da: Verbinden");
        ff.Dispose();

        // Später: ohne Taste erledigt, danach die Warnung; die Datei merkt es.
        using var b = new Aufbau(FunkLage.Leer);
        b.Tastenquelle = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        b.Ablage.Aktuell.Audio.Hoerweise = null;
        var hb = b.Haupt(out var ffb, out _, out var audiob, out _);
        hb.FensterAktiviert();
        audiob.Lautsprecher = true;
        Pruefe.Wahr(hb.Start.ErststartOffen && hb.Start.Sprechtaste.Wartet, "Hörweise gewählt: es fehlt die Sprechtaste, Start wartet");
        Pruefe.Gleich(StartViewModel.ErstSprechtaste, hb.Start.VerbindenGesperrt, "Grund an Verbinden");
        Pruefe.Gleich("Unten umrandet: deine Sprechtaste. Sie geht auch später.", hb.Start.ErststartUmrandet, "was umrandet ist");
        hb.Start.SpaeterCommand.Execute(null);
        Pruefe.Wahr(!hb.Start.ErststartOffen && !hb.Start.Sprechtaste.Wartet && hb.Start.ZeigtSprechtasteWarnung && hb.Start.ZeigtVerbinden, "Später: Verbinden, die Warnung");
        Pruefe.Falsch(hb.Start.SpaeterCommand.CanExecute(null), "Später nur, solange der Schritt offen ist");
        b.Ablage.Sichern();
        Pruefe.Gleich((bool?)true, b.Speicher.Laden().Tasten.ErststartSprechtaste, "erledigt gemerkt");
        ffb.Dispose();

        // Ohne globale Tasten: nichts wartet, kein Hinweis, Belegen und Ändern gesperrt.
        using var c = new Aufbau(FunkLage.Leer);
        var hc = c.Haupt(out var ffc, out _, out _, out _);
        hc.FensterAktiviert();
        Pruefe.Wahr(hc.Start.SprechtasteSchrittOffen && !hc.Start.Sprechtaste.Wartet && hc.Start.ZeigtSchrittBelegen, "ohne Quelle: Belegen statt Wartefeld");
        Pruefe.Gleich(null, hc.Start.Tasten.Hinweis, "und kein Hinweis");
        Pruefe.Falsch(hc.Start.SprechtasteBelegenCommand.CanExecute(null) || hc.Start.SprechtasteAendernCommand.CanExecute(null), "Belegen und Ändern gesperrt");
        Pruefe.Wahr(hc.Start.SpaeterCommand.CanExecute(null), "Später geht");
        ffc.Dispose();
    }

    /// <summary>
    /// "Ändern" auf Start (E17) ersetzt alle Sprechtasten; Esc lässt sie stehen; während des Wartens steht das Wartefeld mit "Abbrechen", danach
    /// wieder die Marken. Die Hörprobe geht auch verbunden (Q75). "Audio starten" steht da, solange Audio nicht läuft (E9).
    /// </summary>
    [Test]
    public static async Task Umbau_Start_AendernHoerprobeUndAudio()
    {
        using var a = new Aufbau(FunkLage.Leer);
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        OberflaecheStartseiteTests.SprechtasteBelegen(a);
        a.Ablage.Aktuell.Tasten.Eintraege.Add(TastenEintrag.Aus(new Tastenzuordnung(Tastenfunktion.Sprechen, Tastenbelegung.Taste(0xE01D))));
        var quelle = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        a.Tastenquelle = quelle;
        var h = a.Haupt(out var ff, out _, out var audio, out _);
        h.FensterAktiviert();
        var s = h.Start;
        Pruefe.Wahr(s.ZeigtSprechtasteBelegt && s.Sprechtaste.Belegungen.Count == 2 && !s.Sprechtaste.Wartet, "zwei Sprechtasten als Marken, Start wartet nicht");

        s.SprechtasteAendernCommand.Execute(null);
        Pruefe.Wahr(s.ZeigtSprechtasteWarten && !s.ZeigtSprechtasteBelegt, "Ändern: das Wartefeld mit Abbrechen");
        Pruefe.Gleich((bool?)false, quelle.ZuletztVorsichtig, "Ändern belegt die erste Taste, nicht vorsichtig");
        // Befund der Prüfung: Ändern wartet mit Frist und endet nicht, wenn das Fenster nicht mehr vorn ist; so lässt sich eine rechte oder
        // mittlere Maustaste mit einem Klick außerhalb belegen (wie unter Einstellungen).
        h.FensterDeaktiviert();
        Pruefe.Wahr(s.Sprechtaste.Wartet && s.ZeigtSprechtasteWarten, "Fenster nicht mehr vorn: Ändern wartet weiter");
        h.FensterAktiviert();
        Pruefe.Wahr(s.Sprechtaste.Wartet, "wieder vorn: wartet noch, ohne neu zu beginnen");
        s.SprechtasteAbbrechenCommand.Execute(null);
        await s.Warten!.WaitAsync(TimeSpan.FromSeconds(5));
        Pruefe.Wahr(s.ZeigtSprechtasteBelegt && s.Sprechtaste.Belegungen.Count == 2, "Abbrechen: alles steht noch");
        quelle.NaechsteTaste = Tastenbelegung.Taste(0x0001);
        s.SprechtasteAendernCommand.Execute(null);
        await s.Warten!.WaitAsync(TimeSpan.FromSeconds(5));
        Pruefe.Gleich(2, s.Sprechtaste.Belegungen.Count, "Esc: alles steht noch");
        quelle.NaechsteTaste = Tastenbelegung.Maus(Tastenbelegung.MausX2);
        s.SprechtasteAendernCommand.Execute(null);
        await s.Warten!.WaitAsync(TimeSpan.FromSeconds(5));
        Pruefe.Gleich("Maustaste 5 (vor)", string.Join(",", s.Sprechtaste.Belegungen.Select(b => b.Name)), "Ändern ersetzt alle Sprechtasten");
        s.Sprechtaste.Belegungen.Single().LoeschenCommand.Execute(null);
        Pruefe.Wahr(s.ZeigtSprechtasteWarnung && !s.ZeigtSprechtasteBelegt, "alle entfernt: die Warnung");
        quelle.NaechsteTaste = null;
        s.SprechtasteBelegenCommand.Execute(null);
        Pruefe.Wahr(s.Sprechtaste.Wartet && s.ZeigtSprechtasteWarten, "Belegen in der Warnung wartet");
        h.AktiverBereich = Seiten.Einstellungen(h);
        Pruefe.Falsch(s.Sprechtaste.Wartet, "ein Warten von Start endet beim Verlassen");
        h.AktiverBereich = Seiten.Start(h);
        Pruefe.Falsch(s.Sprechtaste.Wartet, "und beginnt außerhalb des ersten Starts nicht von selbst");

        // Audio aus: "Audio starten"; die Hörprobe gesperrt mit dem Grund der Hörprobe.
        Pruefe.Wahr(s.ZeigtAudioAus && !s.HoerprobeCommand.CanExecute(null), "Audio aus: Audio starten, Hörprobe gesperrt");
        Pruefe.Gleich(audio.Hoerprobe.ProbeGesperrt, s.HoerprobeGesperrt, "der Grund aus der Hörprobe");
        // Befund der Prüfung: Schlägt der Start fehl, steht der Grund auf Start unter "Mikrofon und Wiedergabe sind zu." (gekürzt, ganz im Hilfetext);
        // bis dahin nur unter Fehlersuche > Feinheiten, und "Audio starten" schien nichts zu tun.
        a.Maschine.StartFehler = new InvalidOperationException("Das Gerät ist belegt. Schließ das andere Programm und versuch es noch einmal.");
        await audio.StartenCommand.ExecuteAsync(null);
        Pruefe.Wahr(s.ZeigtAudioAus && !audio.Laeuft, "fehlgeschlagen: weiter Audio starten");
        Pruefe.Gleich("Audio ließ sich nicht starten: Das Gerät ist belegt.", audio.HinweisKurz, "der Grund kurz, für Start");
        Pruefe.Wahr(audio.Hinweis?.EndsWith("noch einmal.", StringComparison.Ordinal) == true, "ganz im Hilfetext");
        a.Maschine.StartFehler = null;
        await audio.StartenCommand.ExecuteAsync(null);
        h.Takt();
        Pruefe.Falsch(s.ZeigtAudioAus, "Audio läuft: kein Audio starten");
        Pruefe.Gleich<string?>(null, audio.HinweisKurz, "gelungen: kein Grund mehr");
        Pruefe.Gleich(StartViewModel.HoerprobeBereitText, s.HoerprobeText, "bereit: was die Hörprobe tut");
        a.Dienst.LageSetzen(_ => FunkDienstAttrappe.Beispiel());
        Pruefe.Wahr(s.HoerprobeCommand.CanExecute(null), "verbunden: die Hörprobe geht (Q75)");
        s.HoerprobeCommand.Execute(null);
        Pruefe.Gleich(1, a.Maschine.ProbeStarts, "an die Maschine");
        h.Takt();
        Pruefe.Gleich("Abbrechen", s.HoerprobeKnopfText, "während der Probe: Abbrechen");
        ff.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Ansicht
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Start im Hauptfenster wie nach dem Setup (Profil, bestätigte Namen, freigegeben), Audio läuft, globale Tasten (Attrappe); <paramref name="erststart"/>:
    /// ohne Hörweise und Taste. Den Profilschlüssel gibt der Aufrufer frei (<paramref name="schluessel"/>). Monitore ab Werk Full HD bei 100 %
    /// (<paramref name="bildschirme"/>, seit Q93 a für OberflaecheWerIstDaPlatzTests).
    /// </summary>
    internal static (Hauptfenster Haupt, HauptViewModel Modell, FunkfeldViewModel Funkfeld) Aufbauen(Aufbau a, List<Window> fenster, bool erststart, bool verbunden,
        out IDisposable schluessel, IBildschirme? bildschirme = null)
    {
        schluessel = OberflaecheStartseiteTests.Einrichten(a, hoerweise: !erststart);
        a.Ablage.Aktuell.Verbindung.Freigegeben = true;
        if (!erststart) OberflaecheStartseiteTests.SprechtasteBelegen(a);
        a.Ablage.Aktuell.Audio.FunkLautstaerkeProzent = 80;
        a.Tastenquelle = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
        audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        // Verbunden ohne Einheiten (Q89 gilt ohne Einsteigen): die Lage des Beispiels, aber im Zustand aktiv ohne Flotte mit Einheiten.
        if (verbunden) a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Aktiv });
        h.Takt();
        h.AktiverBereich = h.Bereiche[0];
        h.FensterAktiviert();
        var haupt = new Hauptfenster(h, a.Ablage, bildschirme ?? new FesteBildschirme([FullHd]));
        fenster.Add(haupt);
        return (haupt, h, funkfeld);
    }

    internal static T Teil<T>(FrameworkElement seite, string name) where T : FrameworkElement =>
        Bildlauf.Nachfahren<T>(seite).FirstOrDefault(e => e.Name == name) ?? throw new PruefungException(name + " fehlt");

    /// <summary>Die Breite, die ein Element frei gemessen will; danach misst das Layout wieder wie vorher.</summary>
    private static double Gemessen(FrameworkElement e)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var breite = e.DesiredSize.Width;
        e.InvalidateMeasure();
        return breite;
    }

    internal static ContentPresenter Seite(Hauptfenster haupt) =>
        Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is StartViewModel);

    /// <summary>Bedienelemente, die zu sehen sind (Knöpfe, Schalter, Auswahllisten, Regler, Textfelder), ohne die Teile ihrer Vorlagen.</summary>
    private static int Bedienelemente(FrameworkElement seite) =>
        Bildlauf.Nachfahren<FrameworkElement>(seite).Count(e => e is ButtonBase or ComboBox or Slider or TextBox && ZuSehen(e)
            && e.TemplatedParent is null or ContentPresenter);

    /// <summary>
    /// Start verbunden mit belegter Sprechtaste (Bild m-1): bei 1100 und 900 "Sprechen" und "Hören" nebeneinander, Mikrofon und Ausgabe auf einer
    /// Höhe, zehn Bedienelemente; bei 760 untereinander (Q88, m-2). Mit offener Spalte "Wer ist da" seit Q93 a in Klein immer nebeneinander: bei
    /// 1240 (Klein 900, um die Spalte verbreitert) steht sie daneben, bei 1100 und 900 liegt sie als Schublade über Start; nur maximiert steht sie
    /// wie bisher daneben, dort bei 1100 und 900 untereinander. Die Grenze <see cref="StartViewModel.ZweiSpaltenAb"/> nachgemessen. Nichts
    /// abgeschnitten, kein Rollbalken in Klein 900 x 900 (Q89), Glasschatten frei, Kontrast in Anthrazit.
    /// </summary>
    [Test]
    public static void Umbau_Start_ZweiSpaltenUndGestapelt()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var mass = new List<string>();
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                using var a = new Aufbau(FunkLage.Leer);
                var (haupt, h, funkfeld) = Aufbauen(a, fenster, erststart: false, verbunden: true, out var schluessel);
                using var k = schluessel;
                foreach (var (breite, offen, maximiert, nebeneinander) in new[]
                         {
                             (1100.0, false, false, true), (900.0, false, false, true), (760.0, false, false, false), (1240.0, true, false, true),
                             (1100.0, true, false, true), (900.0, true, false, true), (1100.0, true, true, false), (900.0, true, true, false),
                         })
                {
                    if (h.WerIstDa.Offen != offen) h.WerIstDa.UmschaltenCommand.Execute(null);
                    // Ohne Fensterhandle kommt kein StateChanged, deshalb die Stelle direkt.
                    haupt.WindowState = maximiert ? WindowState.Maximized : WindowState.Normal;
                    haupt.ZustandAnwenden();
                    var groesse = new Size(breite, 900);
                    _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                    var was = T($"{satz.Name}, {breite:0}") + (offen ? " mit Wer ist da" : "") + (maximiert ? ", maximiert" : "");
                    var schublade = offen && !maximiert && breite < Fenstergroessen.WerIstDaDanebenAb;
                    Pruefe.Gleich(schublade, haupt.Schublade, was + ": Schublade nur in Klein ohne Platz neben Start (Q93 a)");
                    var seite = Seite(haupt);
                    var grund = Teil<Spaltenwechsel>(seite, "Grundansicht");
                    mass.Add(T($"MASS {was}: Grundansicht {grund.ActualWidth:0.0} breit, nebeneinander {grund.Nebeneinander}"));
                    Pruefe.Gleich(nebeneinander, grund.Nebeneinander, was + T($": Spalten nebeneinander (Grundansicht {grund.ActualWidth:0.0}, ab {StartViewModel.ZweiSpaltenAb:0})"));
                    var mik = Teil<ComboBox>(seite, "MikrofonWahl").TranslatePoint(new Point(0, 0), seite);
                    var aus = Teil<ComboBox>(seite, "AusgabeWahl").TranslatePoint(new Point(0, 0), seite);
                    if (nebeneinander) Pruefe.Wahr(Math.Abs(mik.Y - aus.Y) < 1 && aus.X > mik.X, was + ": Mikrofon und Ausgabe auf einer Höhe");
                    else Pruefe.Wahr(aus.Y > mik.Y + 100 && Math.Abs(aus.X - mik.X) < 1, was + ": Hören unter Sprechen");
                    // Seit 0.11.2 dazu unten "Senden" (Sarevoc 13.10.2026): Einmessen, die vier Schiebeschalter der Klangmodule und "Erweitert:".
                    Pruefe.Gleich(16, Bedienelemente(seite), was + ": sechzehn Bedienelemente (Trennen, Mikrofon, Entfernen, Ändern, Stimme, Ausgabe, Lautstärke, Headset, "
                        + "Lautsprecher, Hörprobe; Einmessen, Rauschunterdrückung, Echoschutz, Automatische Lautstärke, Mindest-Funklautstärke, Erweitert)");
                    // Seit 0.11.2 steht der Pegelbalken mit den Griffen auf Start unter "Senden" (bis 0.11.1 keiner, Q87, mit dem Verweis auf die Statuszeile).
                    Pruefe.Gleich("StartPegel", string.Join("|", Bildlauf.Nachfahren<Pegelbalken>(seite).Select(p => p.Name)), was + ": ein Pegelbalken auf Start, unter Senden");
                    Pruefe.Wahr(!Sichtbar(seite, "Pegel und SENDET stehen unten in der Zeile Mikrofon.") && Sichtbar(seite, "80 %") && Sichtbar(seite, "Maustaste 4 (zurück)"),
                        was + ": kein Verweis mehr, Funk-Lautstärke und Tastenname");
                    // Maximiert bei 900 mit offener Spalte "Wer ist da" daneben bleiben der Seite 263 Einheiten (bis Q93 a auch in Klein 900). Seit dem
                    // Befund der Prüfung rutscht die Steckerzeile dort um (Online darunter, Sprache unter Server), statt "Serve" und "verb" abzuschneiden;
                    // nur ein langer Name darf mit "..." kürzen (ganz im Hilfetext). Bis dahin galt hier das Abschneiden als Sollzustand (Gegenprobe gegen
                    // den echten Fehler).
                    var zeile = Teil<Spaltenwechsel>(seite, "SteckerZeile");
                    var paar = Teil<Spaltenwechsel>(seite, "SteckerPaar");
                    mass.Add(T($"MASS {was}: Steckerzeile {zeile.ActualWidth:0.0} breit, Online daneben {zeile.Nebeneinander}, Stecker nebeneinander {paar.Nebeneinander}"));
                    Pruefe.Gleich(zeile.ActualWidth >= StartViewModel.OnlineDanebenAb, zeile.Nebeneinander, was + ": Online daneben nach der Grenze");
                    Pruefe.Gleich(!(breite == 900 && offen && maximiert), paar.Nebeneinander, was + ": die Stecker nebeneinander, nur maximiert bei 900 mit Wer ist da untereinander");
                    Pruefe.Wahr(NichtsAbgeschnitten(seite, was, "NameText") >= 30, was + ": nichts abgeschnitten (nur der Name darf kürzen), genug Elemente geprüft");
                    Pruefe.Gleich(h.Start.Name, Teil<TextBlock>(seite, "NameText").ToolTip as string, was + ": der ganze Name im Hilfetext");
                    if (satz.Stellung == Farbstellung.Anthrazit && breite == 1100 && !offen)
                    {
                        // Herleitung der Grenzen: je Stecker mit Wort frei gemessen, zwei davon passen unter SteckerNebeneinanderAb, mit Online unter
                        // OnlineDanebenAb ("verbunden" ist mit "verbindet" das längste Wort).
                        var teil = Math.Max(Gemessen(Teil<DockPanel>(seite, "ServerTeil")), Gemessen(Teil<DockPanel>(seite, "SpracheTeil")));
                        var online = Gemessen(Teil<TextBlock>(seite, "OnlineText"));
                        mass.Add(T($"MASS Stecker mit Wort {teil:0.0}, Online {online:0.0}"));
                        Pruefe.Wahr(2 * teil <= StartViewModel.SteckerNebeneinanderAb, T($"zwei Stecker ({2 * teil:0.0}) passen unter {StartViewModel.SteckerNebeneinanderAb:0}"));
                        Pruefe.Wahr(StartViewModel.SteckerNebeneinanderAb + online + 10 <= StartViewModel.OnlineDanebenAb,
                            T($"mit Online ({online:0.0}) passen sie unter {StartViewModel.OnlineDanebenAb:0}, auch mit zweistelliger Zahl"));
                    }
                    _ = OberflaecheStartseiteTests.SchattenFrei(seite, was);
                    // Klein 900 x 900, auch mit der Schublade "Wer ist da" darüber (Q93 a): bis Sprechen und Hören kein Rollen (seit 0.11.2 darf "Senden"
                    // hereinrollen, OberflaecheStartseiteTests.BisSenden; bis 0.11.1 kein Rollbalken), zwei Spalten, bei 100 und 175 %.
                    var name = offen ? (maximiert ? "-wer-ist-da-maximiert" : schublade ? "-wer-ist-da-schublade" : null) : "";
                    if (breite == 900 && !maximiert)
                    {
                        var (unten, platz) = OberflaecheStartseiteTests.BisSenden(Bildlauf.Wurzel(haupt));
                        Pruefe.Wahr(unten <= platz + 0.5, T($"{was}: bis Sprechen und Hören kein Rollen in Klein ({unten:0.0} von {platz:0.0})"));
                        Pruefe.Wahr(grund.ActualWidth >= StartViewModel.ZweiSpaltenAb + 17, T($"{was}: auch mit Rollbalken (17) noch zwei Spalten ({grund.ActualWidth:0.0})"));
                        KontrastPruefen(seite, satz, was, mindestens: 15);
                        var (b175, _) = Bildlauf.Zeichnen(haupt, 1.75, groesse);
                        _ = OberflaecheStartseiteTests.SchattenFrei(seite, was + ", 175 %");
                        if (satz.Stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(b175, "umbau-start-900" + name + "-175");
                    }
                    // Klein verbreitert (1240) zeichnet OberflaecheWerIstDaPlatzTests aus dem echten Ablauf (umbau-start-900-wer-ist-da-breit).
                    if (satz.Stellung == Farbstellung.Anthrazit && name is not null) Bildablage.Ablegen(Bildlauf.Zeichnen(haupt, 1, groesse).Bild, T($"umbau-start-{breite:0}") + name);
                }
                funkfeld.Dispose();
            }
            Messdateien.Schreiben("umbau-start-mass.txt", mass);
        });
    }

    /// <summary>
    /// Gegenprobe zu <see cref="Ansichtspruefung.NichtsAbgeschnitten"/> mit künstlich zu schmalen Elementen (Befund der Prüfung: bis dahin hing sie an
    /// einem echten Darstellungsfehler auf Start, und der Fall 900 mit "Wer ist da" blieb ungeprüft). Gefunden werden ein mit "..." gekürzter Text,
    /// außer er ist ausdrücklich erlaubt, und ein Wort, das ein zu schmaler Vorfahr beschneidet, auch wenn Kürzen erlaubt ist.
    /// </summary>
    [Test]
    public static void Umbau_Gegenprobe_PruefungFindetGekuerztesUndBeschnittenes()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            static StackPanel Probe(UIElement kind)
            {
                var p = new StackPanel { Width = 200 };
                p.Children.Add(kind);
                p.Measure(new Size(200, 200));
                p.Arrange(new Rect(0, 0, 200, 200));
                return p;
            }
            static bool Findet(StackPanel p, params string[] erlaubt)
            {
                try
                {
                    _ = NichtsAbgeschnitten(p, "Gegenprobe", erlaubt);
                    return false;
                }
                catch (PruefungException)
                {
                    return true;
                }
            }
            static TextBlock Lang() => new() { Name = "Lang", Text = "Ein viel zu langer Name", Width = 40, TextTrimming = TextTrimming.CharacterEllipsis };
            Pruefe.Wahr(Findet(Probe(Lang())), "ein mit Auslassungszeichen gekürzter Text wird gefunden");
            Pruefe.Falsch(Findet(Probe(Lang()), "Lang"), "ausdrücklich erlaubt: kein Befund");
            var reihe = new StackPanel { Orientation = Orientation.Horizontal };
            reihe.Children.Add(new TextBlock { Text = "verbunden" });
            var raster = new Grid { Width = 30, HorizontalAlignment = HorizontalAlignment.Left };
            raster.Children.Add(reihe);
            Pruefe.Wahr(Findet(Probe(raster), "Lang"), "ein Wort, das ein zu schmaler Vorfahr beschneidet, wird gefunden");
            Pruefe.Falsch(Findet(Probe(new TextBlock { Text = "verbunden" })), "passt es, kein Befund");
        });
    }

    /// <summary>
    /// Schlägt das Öffnen von Mikrofon und Wiedergabe fehl, steht der Grund ganz unter Einstellungen > Fehlersuche > Feinheiten. Von 0.10.0 bis 0.11.1
    /// stand er zusätzlich auf Start unter "Mikrofon und Wiedergabe sind zu." (Befund der Prüfung); seit 0.11.2 nicht mehr (Sarevoc 13.10.2026: "wer braucht
    /// das"). Start bleibt in Klein bis Sprechen und Hören ohne Rollen, nichts abgeschnitten; gelingt der nächste Start, ist der Grund fort.
    /// </summary>
    [Test]
    public static void Umbau_Start_AudioStartFehler_GrundUnterFehlersuche()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(FunkLage.Leer);
            a.Maschine.StartFehler = new InvalidOperationException("Das Gerät ist von einem anderen Programm belegt. Schließ es und versuch es noch einmal.");
            var (haupt, h, funkfeld) = Aufbauen(a, fenster, erststart: false, verbunden: true, out var schluessel);
            using var k = schluessel;
            var groesse = new Size(900, 900);
            var (bild, _) = Bildlauf.Zeichnen(haupt, 1, groesse);
            var seite = Seite(haupt);
            Pruefe.Falsch(Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => e.Name is "AudioAusRand" or "AudioStartFehler"), "kein Rand \"Audio aus\" mehr auf Start");
            Pruefe.Gleich("Audio ließ sich nicht starten: Das Gerät ist von einem anderen Programm belegt. Schließ es und versuch es noch einmal.", h.Audio.Hinweis, "der Grund im Modell");
            Pruefe.Gleich("Audio ließ sich nicht starten: Das Gerät ist von einem anderen Programm belegt.", h.Audio.HinweisKurz, "gekürzt auf den ersten Satz");
            _ = NichtsAbgeschnitten(seite, "Audio fehlgeschlagen, 900");
            var (unten, platz) = OberflaecheStartseiteTests.BisSenden(Bildlauf.Wurzel(haupt));
            Pruefe.Wahr(unten <= platz + 0.5, T($"bis Sprechen und Hören kein Rollen in Klein ({unten:0.0} von {platz:0.0})"));
            Bildablage.Ablegen(bild, "umbau-start-900-audio-fehler");

            Seiten.Feinheiten(h, Abschnittskennung.Fehlersuche);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 900));
            var einstellungen = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);
            Pruefe.Wahr(Sichtbar(einstellungen, "Audio ließ sich nicht starten: Das Gerät ist von einem anderen Programm belegt."), "der Grund unter Fehlersuche");

            a.Maschine.StartFehler = null;
            h.Audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            h.Takt();
            Pruefe.Wahr(h.Audio.Laeuft && h.Audio.Hinweis is null, "gelungen: kein Grund mehr");
            funkfeld.Dispose();
        });
    }

    /// <summary>
    /// Der erste Start (Bild m-7c): Willkommenssatz statt Verbinden, "Ich höre mit" ohne Wahl und umrandet mit dem Satz, was die Wahl bewirkt, der
    /// Schritt Sprechtaste umrandet mit dem Wartefeld. Bei 900 (zwei schmale Spalten) steht "Später" unter dem Wartefeld, bei 1100 daneben, bei 760
    /// (untereinander) wieder daneben; die Grenze <see cref="StartViewModel.SpaeterDanebenAb"/> nachgemessen. Nach "Später" die Warnung. Kein
    /// Rollbalken in Klein, nichts abgeschnitten, die Glasschatten von Headset und Lautsprecher im umrandeten Rahmen auch bei 175 % frei, Kontrast in
    /// Anthrazit.
    /// </summary>
    [Test]
    public static void Umbau_Erststart_SchrittSpaeterUndWarnung()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var mass = new List<string>();
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                using var a = new Aufbau(FunkLage.Leer);
                var (haupt, h, funkfeld) = Aufbauen(a, fenster, erststart: true, verbunden: false, out var schluessel);
                using var k = schluessel;
                Pruefe.Wahr(h.Start.ErststartOffen && h.Start.Sprechtaste.Wartet, satz.Name + ": erster Start, wartet");
                foreach (var (breite, daneben) in new[] { (900.0, false), (1100.0, true), (760.0, true) })
                {
                    var groesse = new Size(breite, 900);
                    _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                    var was = T($"{satz.Name}, erster Start {breite:0}");
                    var seite = Seite(haupt);
                    Pruefe.Wahr(ZuSehen(Teil<Border>(seite, "ErststartHinweis")) && Sichtbar(seite, h.Start.ErststartSatz), was + ": Willkommenssatz");
                    Pruefe.Falsch(ZuSehen(Teil<Button>(seite, "VerbindenKnopf")) || ZuSehen(Teil<Button>(seite, "TrennenKnopf")), was + ": kein Verbinden");
                    Pruefe.Wahr(Teil<RadioButton>(seite, "HeadsetWahl").IsChecked != true && Teil<RadioButton>(seite, "LautsprecherWahl").IsChecked != true, was + ": Hörweise ohne Wahl");
                    foreach (var rahmen in Rahmen)
                    {
                        var r = Teil<Border>(seite, rahmen);
                        Pruefe.Wahr(r.BorderThickness == new Thickness(2) && r.Padding == new Thickness(10, 8, 10, 8), was + ": " + rahmen + " umrandet, oben und unten gleich");
                    }
                    Pruefe.Wahr(ZuSehen(Teil<TextBlock>(seite, "HoerweiseFrage")) && Sichtbar(seite, "Drück jetzt deine Sprechtaste") && Sichtbar(seite, "Wartet auf deine Taste ..."),
                        was + ": Frage, Schritt und Wartefeld");
                    var feld = Teil<Border>(seite, "WarteFeld");
                    var spaeter = Teil<Button>(seite, "SpaeterKnopf");
                    var zeile = Teil<Spaltenwechsel>(seite, "SchrittZeile");
                    var f = feld.TranslatePoint(new Point(0, 0), seite);
                    var sp = spaeter.TranslatePoint(new Point(0, 0), seite);
                    // Was nebeneinander gebraucht wird: Wartefeld frei gemessen, Lücke, Später.
                    feld.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    spaeter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var gebraucht = feld.DesiredSize.Width + zeile.Luecke + spaeter.DesiredSize.Width;
                    mass.Add(T($"MASS {was}: Zeile des Schritts {zeile.ActualWidth:0.0} breit, gebraucht {gebraucht:0.0}, Später daneben {zeile.Nebeneinander}"));
                    Pruefe.Wahr(StartViewModel.SpaeterDanebenAb >= gebraucht, T($"{was}: die Grenze reicht für Wartefeld und Später ({gebraucht:0.0})"));
                    Pruefe.Gleich(daneben, zeile.Nebeneinander, T($"{was}: Später daneben (Zeile {zeile.ActualWidth:0.0})"));
                    if (daneben) Pruefe.Wahr(Math.Abs(sp.Y + (spaeter.ActualHeight / 2) - (f.Y + (feld.ActualHeight / 2))) < 2 && sp.X > f.X + feld.ActualWidth, was + ": Später neben dem Wartefeld");
                    else Pruefe.Wahr(sp.Y >= f.Y + feld.ActualHeight && Math.Abs(sp.X - f.X) < 1, was + ": Später unter dem Wartefeld");
                    _ = Bildlauf.Zeichnen(haupt, 1, groesse);
                    _ = NichtsAbgeschnitten(seite, was);
                    _ = OberflaecheStartseiteTests.SchattenFrei(seite, was);
                    if (breite == 900)
                    {
                        var lauf = Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(haupt)).First(sv => sv.Content is StackPanel p && p.Children.OfType<ContentControl>().Any());
                        var (unten, platz) = OberflaecheStartseiteTests.BisSenden(Bildlauf.Wurzel(haupt));
                        mass.Add(T($"MASS {was}: Inhalt {lauf.ExtentHeight:0.0} von {lauf.ViewportHeight:0.0}, Fenster ohne Rollen {900 - lauf.ViewportHeight + lauf.ExtentHeight:0}, Sprechen und Hören bis {unten:0.0}"));
                        // Seit 0.11.2 darf "Senden" hereinrollen (OberflaecheStartseiteTests.BisSenden); bis 0.11.1 kein Rollbalken.
                        Pruefe.Wahr(unten <= platz + 0.5, T($"{was}: bis Sprechen und Hören kein Rollen in Klein ({unten:0.0} von {platz:0.0})"));
                        KontrastPruefen(seite, satz, was, mindestens: 15);
                        var (b175, _) = Bildlauf.Zeichnen(haupt, 1.75, groesse);
                        _ = OberflaecheStartseiteTests.SchattenFrei(seite, was + ", 175 %");
                        if (satz.Stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(b175, "umbau-erststart-900-175");
                    }
                    if (satz.Stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(Bildlauf.Zeichnen(haupt, 1, groesse).Bild, T($"umbau-erststart-{breite:0}"));
                }

                // Hörweise gewählt, dann Später: die Warnung ohne Sprechtaste, Verbinden.
                h.Audio.Headset = true;
                h.Start.SpaeterCommand.Execute(null);
                var g = new Size(900, 900);
                _ = Bildlauf.Zeichnen(haupt, 1, g);
                var nach = Seite(haupt);
                var w = satz.Name + ", nach Später";
                Pruefe.Wahr(ZuSehen(Teil<Border>(nach, "SprechtasteWarnung")) && Sichtbar(nach, "Noch keine Sprechtaste"), w + ": die Warnung");
                Pruefe.Wahr(ZuSehen(Teil<Button>(nach, "VerbindenKnopf")) && !ZuSehen(Teil<Border>(nach, "ErststartHinweis")), w + ": Verbinden statt Willkommen");
                Pruefe.Falsch(ZuSehen(Teil<TextBlock>(nach, "HoerweiseFrage")), w + ": keine Frage mehr");
                Pruefe.Wahr(Teil<Border>(nach, "SprechtasteRahmen").BorderThickness == new Thickness(0), w + ": nicht mehr umrandet");
                _ = NichtsAbgeschnitten(nach, w);
                KontrastPruefen(nach, satz, w, mindestens: 15);
                if (satz.Stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(Bildlauf.Zeichnen(haupt, 1, g).Bild, "umbau-start-ohne-sprechtaste-900");
                funkfeld.Dispose();
            }
            Messdateien.Schreiben("umbau-erststart-mass.txt", mass);
        });
    }

    /// <summary>
    /// Ohne globale Tasten (etwa in der Vorführung): Im Schritt steht "Belegen" statt des Wartefelds, gesperrt mit Grund; Später geht. Audio aus:
    /// Von 0.10.0 bis 0.11.1 stand "Audio starten" in der Spalte Hören (E9); seit 0.11.2 nicht mehr auf Start (Sarevoc 13.10.2026), die Hörprobe sagt,
    /// dass sie erst mit laufendem Audio geht.
    /// </summary>
    [Test]
    public static void Umbau_Start_OhneQuelleUndAudioAus()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using var a = new Aufbau(FunkLage.Leer);
            using var k = OberflaecheStartseiteTests.Einrichten(a, hoerweise: false);
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            h.AktiverBereich = h.Bereiche[0];
            h.FensterAktiviert();
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(900, 900));
            var seite = Seite(haupt);
            var belegen = Teil<Button>(seite, "SchrittBelegenKnopf");
            Pruefe.Wahr(ZuSehen(belegen) && !belegen.IsEnabled && !ZuSehen(Teil<Border>(seite, "WarteFeld")), "Belegen statt Wartefeld, gesperrt");
            Pruefe.Gleich(TastenViewModel.OhneQuelleGrund, belegen.ToolTip as string, "mit Grund");
            Pruefe.Wahr(ZuSehen(Teil<Button>(seite, "SpaeterKnopf")) && Teil<Button>(seite, "SpaeterKnopf").IsEnabled, "Später geht");
            Pruefe.Falsch(Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => e.Name is "AudioAusRand" or "AudioStartenKnopf") || Sichtbar(seite, "Mikrofon und Wiedergabe sind zu."),
                "Audio aus: seit 0.11.2 kein \"Audio starten\" auf Start");
            var probe = Teil<Button>(seite, "HoerprobeKnopf");
            Pruefe.Wahr(!probe.IsEnabled && Glasdesign.Hilfe.GetGesperrt(probe) is { } grund && grund.Contains("Geht erst, wenn das Audio läuft.", StringComparison.Ordinal),
                "Audio aus: die Hörprobe gesperrt mit Grund: " + Glasdesign.Hilfe.GetGesperrt(probe));
            _ = NichtsAbgeschnitten(seite, "ohne Quelle, Audio aus");
            _ = OberflaecheStartseiteTests.SchattenFrei(seite, "ohne Quelle, Audio aus");
            Bildablage.Ablegen(Bildlauf.Zeichnen(haupt, 1, new Size(900, 900)).Bild, "umbau-erststart-ohne-tasten-audio-aus-900");
            h.Audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            h.Takt();
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(900, 900));
            Pruefe.Wahr(Teil<Button>(Seite(haupt), "HoerprobeKnopf").IsEnabled, "Audio läuft: die Hörprobe geht");
            funkfeld.Dispose();
        });
    }
}
