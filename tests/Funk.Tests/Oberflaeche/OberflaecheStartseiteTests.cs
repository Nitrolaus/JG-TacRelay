// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Glasdesign;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Startseite (0.6.0, Stufe 2, Sarevocs abgenommene Skizze; seit 0.10.0 nach der Mischung, Q87 bis Q92): Logo, Name, Gerät freigegeben oder
/// wartend mit Kennung, Verbinden oder Trennen, zwei Stecker für Server und Sprache mit Wort daneben, Online-Zahl, darunter Sprechen und Hören
/// (Mikrofon, Sprechtaste, Senden mit Stimme; Ausgabe, Funk-Lautstärke, Headset oder Lautsprecher, die eine Hörprobe), die Gerätewarnung an der
/// Stelle der Hörprobe, Details nur im Admin-Setup, beim allerersten Start Hörweise und Sprechtaste. Funkfeld 5, Funkfeld 10, Wer ist da und
/// der Pegel stehen seit 0.10.0 nicht mehr hier (OberflaecheUmbauStartTests prüft den neuen Aufbau im Einzelnen). Für Funker nirgends Server,
/// Host, Port, Adresse oder Gerätename. Mit <c>FUNK_BILDER</c> entstehen die Sichtproben für Sarevoc.
/// </summary>
internal static class OberflaecheStartseiteTests
{
    private const string Kennung = "4F2A-81C0-77D3-9C31";

    /// <summary>Die Bytes des eingebauten Platzhalters aus der Ressource von TacRelay.dll (bis Schritt 3.5 stand das im Client, wo es niemand brauchte).</summary>
    private static byte[] EingebautBytes()
    {
        using var strom = typeof(Gildenpaket).Assembly.GetManifestResourceStream("Gildenlogo-Platzhalter.png") ?? throw new PruefungException("Das eingebaute Gildenlogo fehlt in TacRelay.dll.");
        using var kopie = new MemoryStream();
        strom.CopyTo(kopie);
        return kopie.ToArray();
    }
    private static readonly bool[] AdminSetups = [false, true];

    private static readonly string[] UnterDetails = ["funk.example.org", "TCP-Port 8443", "Adresse"];

    private static readonly string[] NichtFuerFunker = ["funk.example.org", "8443", "TCP-Port", "Adresse", "Gaming-PC", "Meine Staffel:"];

    private static readonly double[] Skalen = [1.0, 1.75];

    /// <summary>Eine Sprechtaste wie nach dem Einrichten (Maustaste 4), gleich in den Einstellungen; vor dem Aufbau des Hauptfensters.</summary>
    internal static void SprechtasteBelegen(Aufbau a) =>
        a.Ablage.Aktuell.Tasten.Eintraege.Add(TastenEintrag.Aus(new Funk.Klang.Eingabe.Tastenzuordnung(Funk.Klang.Eingabe.Tastenfunktion.Sprechen,
            Funk.Klang.Eingabe.Tastenbelegung.Maus(Funk.Klang.Eingabe.Tastenbelegung.MausX1))));

    /// <summary>Gültiges Profil, bestätigte Namen, Headset gewählt: so steht ein Funker nach dem Einrichten da.</summary>
    internal static OberflaecheProfilTests.Schluessel Einrichten(Aufbau a, bool hoerweise = true)
    {
        var k = new OberflaecheProfilTests.Schluessel();
        var profil = a.Ordner.Datei("profil.json");
        OberflaecheProfilTests.ProfilSchreiben(profil, k);
        a.Profil = new Profilquelle(profil, [k.Spki]);
        if (hoerweise) a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
        a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
        a.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
        a.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
        return k;
    }

    [Test]
    public static void Start_Leiste_StartUndEinstellungen()
    {
        using var a = new Aufbau(FunkLage.Leer);
        using var k = Einrichten(a);
        var h = a.Haupt(out _, out _, out _, out _);
        // Seit 0.10.0 (AP3, Q87, E1) nur Start und Einstellungen; Audio und Meine Kanäle sind in Start und den Feinheiten aufgegangen.
        Pruefe.Gleich("Start|Einstellungen", string.Join("|", h.Bereiche.Select(b => b.Titel)), "Einträge der Leiste");
        Pruefe.Gleich("start", h.Bereiche[0].Kennung, "Kennung");
        Pruefe.Gleich("Symbol.Start", h.Bereiche[0].Symbol, "eigenes Symbol");
        Pruefe.Wahr(ReferenceEquals(h.Start, h.Bereiche[0].Inhalt), "Start zeigt die Startseite");
        Pruefe.Wahr(ReferenceEquals(h.Verbindung, h.Start.Verbindung), "die Startseite trägt die Verbindung");
        // Alte Einstellungsdatei mit dem Bereich "verbindung": landet auf Start.
        a.Ablage.Aktuell.Bereich = "verbindung";
        var alt = a.Haupt(out _, out _, out _, out _);
        Pruefe.Wahr(ReferenceEquals(alt.AktiverBereich, alt.Bereiche[0]), "alte Datei: Start");
    }

    [Test]
    public static void Start_Modell_SteckerOnlineNameUndKnoepfe()
    {
        using var a = new Aufbau(FunkLage.Leer);
        using var k = Einrichten(a);
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var s = h.Start;

        // Getrennt
        Pruefe.Gleich(Steckerzustand.Getrennt, s.Server, "getrennt: Server");
        Pruefe.Gleich(Steckerzustand.Getrennt, s.Sprache, "getrennt: Sprache");
        Pruefe.Gleich("getrennt", s.ServerWort, "Wort neben dem Stecker");
        Pruefe.Gleich("getrennt", s.SpracheWort, "Wort neben dem Stecker");
        Pruefe.Gleich("Online: -", s.OnlineText, "getrennt keine Zahl");
        Pruefe.Gleich("Mustermann/Max", s.Name, "Name aus der Anmeldung");
        Pruefe.Wahr(s.ZeigtVerbinden && s.VerbindenCommand.CanExecute(null), "Verbinden");
        // Seit 0.10.0 (Q87, Variante C): Funkfeld 5, Funkfeld 10 und Wer ist da stehen nicht mehr auf Start, die Titelleiste reicht.
        Pruefe.Falsch(typeof(StartViewModel).GetProperty("Funkfeld5Command") is not null || typeof(StartViewModel).GetProperty("WerIstDaOeffnenCommand") is not null,
            "keine Knöpfe Funkfeld und Wer ist da mehr auf Start");
        // Stufe 2b (Befund N3): ohne laufendes Audio gesperrt, mit Grund; getrennt und mit Audio frei.
        Pruefe.Falsch(s.HoerprobeCommand.CanExecute(null), "Hörprobe ohne Audio gesperrt");
        Pruefe.Wahr(s.HoerprobeGesperrt.Contains("Audio", StringComparison.Ordinal), "Grund: " + s.HoerprobeGesperrt);
        h.Audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        h.Takt();
        Pruefe.Wahr(s.HoerprobeCommand.CanExecute(null), "Hörprobe getrennt, mit Audio");
        Pruefe.Falsch(s.GeraetFreigegeben || s.WartetAufFreigabe, "noch nie freigegeben");

        // Verbindet
        a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Verbinden });
        Pruefe.Gleich(Steckerzustand.Verbindet, s.Server, "verbindet: Server");
        Pruefe.Gleich("verbindet", s.ServerWort, "verbindet: Wort");
        Pruefe.Wahr(!s.ZeigtVerbinden && s.ZeigtTrennen, "beim Verbinden steht Trennen da");
        // Seit 0.10.0 (Q75): die eine Hörprobe auch beim Verbinden und verbunden (bis 0.9.3 nur getrennt).
        Pruefe.Wahr(s.HoerprobeCommand.CanExecute(null), "Hörprobe auch beim Verbinden");

        // Warteraum: Server verbunden, Sprache nicht, Kennung zum Vorlesen
        a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Warteraum, Server = "Meine Staffel", Geraet = Kennung, Schluesselart = "TPM" });
        Pruefe.Gleich(Steckerzustand.Verbunden, s.Server, "Warteraum: Steuerkanal steht");
        Pruefe.Gleich(Steckerzustand.Getrennt, s.Sprache, "Warteraum: keine Sprache");
        Pruefe.Wahr(s.WartetAufFreigabe && !s.GeraetFreigegeben, "wartet auf Freigabe");
        Pruefe.Gleich("Wartet auf Freigabe", s.GeraetText, "Satz unter dem Namen");
        Pruefe.Gleich(Kennung, s.Verbindung.GeraetAnzeige, "Kennung zum Vorlesen");

        // Verbunden über UDP
        a.Dienst.LageSetzen(_ => FunkDienstAttrappe.Beispiel());
        Pruefe.Gleich(Steckerzustand.Verbunden, s.Server, "verbunden: Server");
        Pruefe.Gleich(Steckerzustand.Verbunden, s.Sprache, "verbunden: Sprache");
        Pruefe.Gleich("verbunden", s.SpracheWort, "Wort");
        Pruefe.Wahr(h.WerIstDa.Anzahl > 0, "jemand ist online");
        Pruefe.Gleich("Online: " + h.WerIstDa.Anzahl.ToString(CultureInfo.CurrentCulture), s.OnlineText, "Zahl aus Wer ist da");
        Pruefe.Wahr(s.GeraetFreigegeben && s.GeraetText == "Gerät freigegeben", "freigegeben");
        Pruefe.Gleich((bool?)true, a.Ablage.Aktuell.Verbindung.Freigegeben, "gemerkt");
        Pruefe.Wahr(s.HoerprobeCommand.CanExecute(null), "Hörprobe auch verbunden (Q75)");

        // Tunnel: Sprache über den Steuerkanal
        a.Dienst.LageSetzen(l => l with { Status = Verbindungsstatus.AktivTunnel });
        Pruefe.Gleich(Steckerzustand.Tunnel, s.Sprache, "Tunnel");
        Pruefe.Gleich("Tunnel", s.SpracheWort, "Wort Tunnel");

        // Wieder getrennt: Das Gerät bleibt als freigegeben gemerkt.
        a.Dienst.LageSetzen(_ => FunkLage.Leer);
        Pruefe.Wahr(s.GeraetFreigegeben && s.GeraetText == "Gerät freigegeben", "getrennt, aber schon freigegeben");
        Pruefe.Gleich("Online: -", s.OnlineText, "getrennt wieder keine Zahl");
        // Seit 0.6.3: "vom Funk abgemeldet" (Bye ENTFERNT, kein Neuversuch): nicht mehr freigegeben, auch getrennt. Es zählt der
        // Zustand FunkLage.Entfernt, nicht der Text: Der Hinweis allein ändert nichts, erst der Zustand nimmt den Haken weg.
        var abgemeldet = Fehlernummern.Anhaengen("Dieses Gerät wurde vom Funk abgemeldet (entfernt).", Fehlernummern.GeraetEntfernt);
        a.Dienst.LageSetzen(_ => FunkLage.Leer with { Hinweis = abgemeldet });
        Pruefe.Wahr(s.GeraetFreigegeben, "nur der Hinweis: Haken bleibt");
        a.Dienst.LageSetzen(_ => FunkLage.Leer with { Hinweis = abgemeldet, Entfernt = true });
        Pruefe.Wahr(s.Verbindung.Entfernt && !s.GeraetFreigegeben && a.Ablage.Aktuell.Verbindung.Freigegeben == false, "entfernt: kein Haken mehr, und so gemerkt");
        Pruefe.Gleich("Getrennt", VerbindungViewModel.Text(s.Verbindung.Status), "entfernt: getrennt");
        // Ein neuer Antrag (Gerät entfernt): nicht mehr freigegeben.
        a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Warteraum, Geraet = Kennung });
        Pruefe.Gleich((bool?)false, a.Ablage.Aktuell.Verbindung.Freigegeben, "wieder im Warteraum: als nicht freigegeben gemerkt");
        funkfeld.Dispose();
    }

    /// <summary>
    /// Q10 und Q76 (seit 0.10.0, E8): Beim allerersten Start fehlen zwei Angaben, Headset oder Lautsprecher und die Sprechtaste; erst danach kommt
    /// Verbinden. Die Sprechtaste geht auch später ("Später"). Beim nächsten Start ist beides weg.
    /// </summary>
    [Test]
    public static void Start_Erststart_NurEinmal_VorDemErstenVerbinden()
    {
        using var a = new Aufbau(FunkLage.Leer);
        using var k = Einrichten(a, hoerweise: false);
        var h = a.Haupt(out _, out _, out var audio, out _);
        Pruefe.Wahr(h.Start.ErststartOffen && h.Start.SprechtasteSchrittOffen, "erster Start: beide Angaben stehen auf der Startseite");
        Pruefe.Falsch(h.Start.VerbindenCommand.CanExecute(null), "erst die Angaben, dann Verbinden");
        Pruefe.Gleich(StartViewModel.ErstHoerweise, h.Start.VerbindenGesperrt, "zuerst fehlt die Hörweise");
        audio.LautsprecherWaehlenCommand.Execute(null);
        // Bis 0.9.3 war der erste Start damit erledigt; seit 0.10.0 bleibt der Schritt Sprechtaste (Q76).
        Pruefe.Wahr(h.Start.ErststartOffen && h.Start.SprechtasteSchrittOffen, "Hörweise gewählt: die Sprechtaste fehlt noch");
        Pruefe.Gleich(StartViewModel.ErstSprechtaste, h.Start.VerbindenGesperrt, "jetzt fehlt nur noch die Sprechtaste");
        Pruefe.Falsch(h.Start.VerbindenCommand.CanExecute(null), "noch kein Verbinden");
        h.Start.SpaeterCommand.Execute(null);
        Pruefe.Falsch(h.Start.ErststartOffen, "Später: erledigt");
        Pruefe.Wahr(h.Start.VerbindenCommand.CanExecute(null), "jetzt Verbinden");
        Pruefe.Wahr(h.Start.ZeigtSprechtasteWarnung, "ohne Taste steht die Warnung auf Start");
        var spaeter = a.Haupt(out _, out _, out _, out _);
        Pruefe.Falsch(spaeter.Start.ErststartOffen, "beim nächsten Start nicht mehr");
        var xaml = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Fenster", "Hauptfenster.xaml"));
        Pruefe.Falsch(xaml.Contains("ErststartKarte", StringComparison.Ordinal) || xaml.Contains("GeraeteWarnung", StringComparison.Ordinal),
            "keine Karte mehr über allen Bereichen");
    }

    [Test]
    public static void Start_Details_NurImAdminSetup_UnterEinstellungenErweitert()
    {
        using var a = new Aufbau(FunkLage.Leer);
        using var k = Einrichten(a);
        var funker = a.Haupt(out _, out _, out _, out _);
        Pruefe.Falsch(funker.Start.IstAdminSetup, "Funker-Setup: keine Details");
        Pruefe.Gleich(null, funker.Einstellungen.Details, "Funker-Setup: unter Einstellungen keine Details");
        var dienst = new VerwaltungsdienstAttrappe();
        using var teil = new Funk.Verwaltung.Verwaltungsteil(dienst, new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst));
        a.Verwaltung = teil;
        var admin = a.Haupt(out _, out _, out _, out _);
        Pruefe.Wahr(admin.Start.IstAdminSetup && ReferenceEquals(admin.Einstellungen.Details, admin.Verbindung), "Admin-Setup: die Details gehören zu Einstellungen, Erweitert");
        // Von der Startseite weg (N2, Gegenprüfung Stufe 2: Dort rollte der ungünstigste Fall; N10 entfällt mit dem Knopf).
        Pruefe.Falsch(typeof(StartViewModel).GetProperty("DetailsCommand") is not null || typeof(StartViewModel).GetProperty("DetailsOffen") is not null, "kein Knopf Details mehr auf der Startseite");
    }

    /// <summary>
    /// Gegenprüfung Stufe 2: Bis 0.5.x stand das Feld Adresse für alle da, seit Stufe 2 nur unter Details im Admin-Setup (Q3, Q14).
    /// Eine Adresse aus 0.5.x gälte im Funker-Setup sonst unsichtbar weiter (statt der Hosts aus dem Profil), ließe sich nicht mehr
    /// ändern, und eine ungültige verwiese auf Details, die es dort nicht gibt. Im Funker-Setup fällt sie deshalb weg; im
    /// Admin-Setup bleibt sie.
    /// </summary>
    [Test]
    public static void Start_AdresseAusAlterDatei_ImFunkerSetupWeg_ImAdminSetupBleibt()
    {
        using (var a = new Aufbau(FunkLage.Leer))
        using (var k = Einrichten(a))
        {
            a.Ablage.Aktuell.Verbindung.Adresse = "https://alt.example.org";
            var s = a.Haupt(out _, out _, out _, out _).Start;
            Pruefe.Gleich(null, a.Ablage.Aktuell.Verbindung.Adresse, "Funker-Setup: die alte Adresse gilt nicht mehr");
            Pruefe.Gleich(null, s.Verbindung.WasFehlt, "kein Verweis auf Details, die es im Funker-Setup nicht gibt");
            Pruefe.Wahr(s.VerbindenCommand.CanExecute(null), "Verbinden geht mit den Hosts aus dem Profil");
        }
        using (var a = new Aufbau(FunkLage.Leer))
        using (var k = Einrichten(a))
        {
            a.Ablage.Aktuell.Verbindung.Adresse = "192.168.55.210";
            var vd = new VerwaltungsdienstAttrappe();
            using var teil = new Funk.Verwaltung.Verwaltungsteil(vd, new Funk.Verwaltung.Modelle.VerwaltungViewModel(vd));
            a.Verwaltung = teil;
            _ = a.Haupt(out _, out _, out _, out _);
            Pruefe.Gleich("192.168.55.210", a.Ablage.Aktuell.Verbindung.Adresse, "Admin-Setup: die Adresse bleibt (Details)");
        }
    }

    /// <summary>
    /// Gegenprüfung Stufe 2 (Q12), seit 0.10.0 in Klein 900 x 900 (Q89): kein Rollbalken in der kleinsten Größe von Klein auch mit der echten
    /// Gerätewarnung von Windows (Datenschutz, zwei Sätze) und zusammen mit einer Meldung der Verbindung (nach Trennen "Getrennt", beim Verbinden
    /// der häufigste und der längste Satz für Funker), und neu beim ersten Start (Q76): allein, mit Gerätewarnung und mit Meldung ("Beim Start
    /// verbinden" während der erste Start offen ist). Ohne Gerätewarnung läuft Audio, wie nach dem Start. Einheiten sind nicht dabei: Mit der Karte
    /// Einsteigen darf Start rollen (E10). Gemessen ohne Bild, bei 100 und 175 %. Seit 0.11.2 steht unter den Spalten der Abschnitt "Senden"
    /// (Sarevoc 13.10.2026); er darf in Klein hereinrollen, alles bis Sprechen und Hören steht ohne Rollen da (<see cref="BisSenden"/>).
    /// </summary>
    [Test]
    public static void Start_KeinRollbalken_AuchMitMeldungUndGeraetewarnung()
    {
        // Der längste Satz, den ein Funker unter Verbinden sehen kann.
        var saetze = Enum.GetValues<AufbauFehler>().SelectMany(f => new[] { Verbindungstexte.Aufbau(f, "x"), Verbindungstexte.Aufbau(f, "x", true) })
            .Concat(Enum.GetValues<ByeGrund>().Select(b => Verbindungstexte.Ende(new Sitzungsende("Bye " + b, b, 10_000, null, 5_000, null))))
            .Concat(Enum.GetValues<Fehlercode>().Select(c => Verbindungstexte.Ende(new Sitzungsende("Fehler", null, null, c, null, null))))
            .Select(m => m.Text).ToList();
        var laengster = saetze.MaxBy(t => t.Length)!;
        var netz = Verbindungstexte.Aufbau(AufbauFehler.Netz, "x").Text;
        // So meldet die Klangmaschine ein vom Datenschutz gesperrtes Mikrofon (der häufigste Gerätefehler).
        var datenschutz = Funk.Klang.Maschine.KlangMaschine.Beschreiben(new UnauthorizedAccessException(), aufnahme: true);
        Bildlauf.Ausfuehren(fenster =>
        {
            var mass = new List<string>();
            foreach (var (zustand, warnung, hinweis, erststart) in new (Verbindungsstatus, string?, string?, bool)[]
                     {
                         (Verbindungsstatus.Aktiv, null, null, false),
                         (Verbindungsstatus.Getrennt, datenschutz, null, false),
                         (Verbindungsstatus.Getrennt, datenschutz, "Getrennt", false),
                         (Verbindungsstatus.Verbinden, datenschutz, netz, false),
                         (Verbindungsstatus.Verbinden, null, laengster, false),
                         (Verbindungsstatus.Verbinden, datenschutz, laengster, false),
                         (Verbindungsstatus.Getrennt, null, null, true),
                         (Verbindungsstatus.Getrennt, datenschutz, null, true),
                         (Verbindungsstatus.Verbinden, null, laengster, true),
                     })
            {
                Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
                using var a = new Aufbau(FunkLage.Leer);
                using var k = Einrichten(a, hoerweise: !erststart);
                // Wie im Programm: Mit globalen Tasten wartet der erste Start sichtbar auf die Sprechtaste (das Wartefeld).
                a.Tastenquelle = new Maschine.TastenQuelleAttrappe(Funk.Klang.Eingabe.EingabeArt.Tastatur, Funk.Klang.Eingabe.EingabeArt.Maus);
                var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
                if (warnung is not null) a.Maschine.AufnahmeFehlerBeimStart = warnung;
                audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = zustand, Hinweis = hinweis });
                h.Takt();
                h.AktiverBereich = h.Bereiche[0];
                h.FensterAktiviert();
                Pruefe.Gleich(erststart, h.Start.ErststartOffen && h.Start.Sprechtaste.Wartet, "erster Start: offen und wartet auf die Taste");
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var groesse = Fenstergroessen.Mindest(Groessenklasse.Klein);
                Pruefe.Gleich(new Size(900, 900), groesse, "Klein mindestens 900 x 900 (Q89)");
                var was = (erststart ? "erster Start, " : "") + zustand + (warnung is null ? "" : " mit Gerätewarnung") + (hinweis is null ? "" : ", Meldung " + hinweis.Length + " Zeichen");
                foreach (var skala in Skalen)
                {
                    _ = Bildlauf.Zeichnen(haupt, skala, groesse);
                    var seite = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is StartViewModel);
                    var lauf = Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(haupt)).First(sv => sv.Content is StackPanel p && p.Children.OfType<ContentControl>().Any());
                    if (hinweis is not null) Pruefe.Wahr(Sichtbar(seite, hinweis[..Math.Min(20, hinweis.Length)]), was + ": die Meldung steht da");
                    if (hinweis?.LastIndexOf(" (Fehler ", StringComparison.Ordinal) is > 0 and var n)
                        Pruefe.Wahr(Sichtbar(seite, hinweis[(n + 1)..]), was + ": die Fehlernummer steht da");
                    if (warnung is not null) Pruefe.Wahr(Sichtbar(seite, "Windows verweigert den Zugriff"), was + ": die Gerätewarnung steht da");
                    // Die Hörprobe bräuchte beide Geräte; bei einer Gerätewarnung nimmt die Warnung ihren Platz ein.
                    var probe = Bildlauf.Nachfahren<Button>(seite).Single(b => b.Name == "HoerprobeKnopf");
                    Pruefe.Gleich(warnung is null, ZuSehen(probe), was + ": Hörprobe nur ohne Gerätewarnung");
                    if (erststart)
                    {
                        Pruefe.Wahr(Sichtbar(seite, "Drück jetzt deine Sprechtaste") && Sichtbar(seite, "Wartet auf deine Taste ..."), was + ": der Schritt Sprechtaste");
                        Pruefe.Wahr(ZuSehen(Bildlauf.Nachfahren<Button>(seite).Single(b => b.Name == "SpaeterKnopf")), was + ": Später");
                    }
                    var (unten, platz) = BisSenden(Bildlauf.Wurzel(haupt));
                    var zeile = string.Create(CultureInfo.InvariantCulture,
                        $"MASS {was} bei {skala:0.00}: Inhalt {lauf.ExtentHeight:0.0}, Platz {lauf.ViewportHeight:0.0}, Fenster ohne Rollen {groesse.Height - lauf.ViewportHeight + lauf.ExtentHeight:0}, Sprechen und Hören bis {unten:0.0}");
                    Console.WriteLine(zeile);
                    mass.Add(zeile);
                    Messdateien.Schreiben("start-kein-rollbalken.txt", mass);
                    Pruefe.Wahr(unten <= platz + 0.5,
                        $"{was}: bis Sprechen und Hören kein Rollen bei {groesse.Width:0} x {groesse.Height:0} und {skala * 100:0} % (bis {unten:0.0}, Platz {platz:0.0})");
                }
                h.Start.ErwartenBeenden();
                funkfeld.Dispose();
            }
        });
    }

    /// <summary>
    /// Die Sichtproben für Sarevoc und die Prüfungen an ihnen: je Zustand in Anthrazit (bis 0.9.3 auch mit aufgeklappter
    /// Sprachaktivierung; seit 0.10.0 steht unten die Statuszeile) und mit Details im Admin-Setup; seit 0.10.0 (Mischung) dazu Audio aus und
    /// ohne Sprechtaste. In der kleinsten Größe der Klasse Klein (900 x 900, Q89) kein Rollbalken, nichts abgeschnitten, keine Adresse, kein
    /// Port, kein Gerätename, Kontrast jedes Texts, Stecker und Logo mittig, kein abgeschnittener Schatten. Ohne Gerätewarnung und außer bei
    /// "Audio aus" läuft Audio, und eine Sprechtaste ist belegt, wie nach dem Einrichten.
    /// </summary>
    [Test]
    public static void Start_Sichtproben_UndPruefungen()
    {
        var mass = new List<string>();
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var (name, stellung, zustand) in new (string, Farbstellung, string)[]
                     {
                         ("start-getrennt-anthrazit", Farbstellung.Anthrazit, "getrennt"),
                         ("start-verbindet-anthrazit", Farbstellung.Anthrazit, "verbindet"),
                         ("start-verbunden-udp-anthrazit", Farbstellung.Anthrazit, "verbunden"),
                         ("start-verbunden-tunnel-anthrazit", Farbstellung.Anthrazit, "tunnel"),
                         ("start-wartet-auf-freigabe-anthrazit", Farbstellung.Anthrazit, "wartet"),
                         ("start-erststart-anthrazit", Farbstellung.Anthrazit, "erststart"),
                         ("start-geraetewarnung-anthrazit", Farbstellung.Anthrazit, "warnung"),
                         ("start-audio-aus-anthrazit", Farbstellung.Anthrazit, "audioaus"),
                         ("start-ohne-sprechtaste-anthrazit", Farbstellung.Anthrazit, "ohnetaste"),
                         ("start-admin-geraetewarnung-anthrazit", Farbstellung.Anthrazit, "adminwarnung"),
                     })
            {
                Bildlauf.RessourcenSetzen(stellung);
                var satz = Farbvorschlaege.Von(stellung);
                using var a = new Aufbau(FunkLage.Leer);
                using var k = Einrichten(a, hoerweise: zustand != "erststart");
                a.Ablage.Aktuell.Farbstellung = stellung;
                a.Tastenquelle = new Maschine.TastenQuelleAttrappe(Funk.Klang.Eingabe.EingabeArt.Tastatur, Funk.Klang.Eingabe.EingabeArt.Maus);
                if (zustand is not ("erststart" or "ohnetaste")) SprechtasteBelegen(a);
                Funk.Verwaltung.Verwaltungsteil? teil = null;
                if (zustand == "adminwarnung")
                {
                    var vd = new VerwaltungsdienstAttrappe();
                    teil = new Funk.Verwaltung.Verwaltungsteil(vd, new Funk.Verwaltung.Modelle.VerwaltungViewModel(vd));
                    a.Verwaltung = teil;
                }
                var h = a.Haupt(out var funkfeld, out var schwelle, out var audio, out _);
                switch (zustand)
                {
                    case "verbindet":
                        a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Verbinden });
                        break;
                    case "verbunden":
                        a.Dienst.LageSetzen(_ => FunkDienstAttrappe.Beispiel());
                        break;
                    case "tunnel":
                        a.Dienst.LageSetzen(_ => FunkDienstAttrappe.Beispiel() with { Status = Verbindungsstatus.AktivTunnel });
                        break;
                    case "wartet":
                        a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Warteraum, Server = "Meine Staffel", Geraet = Kennung, Schluesselart = "TPM" });
                        break;
                    case "warnung" or "adminwarnung":
                        a.Maschine.AufnahmeFehlerBeimStart = "Windows verweigert den Zugriff auf das Mikrofon.";
                        break;
                }
                if (zustand != "audioaus") audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                for (var i = 0; i < 60; i++)
                    a.Maschine.Melden(new Funk.Klang.Maschine.PegelMeldung(i * 20L, i % 20 < 10 ? -24 : -58, -61, -50, i % 20 < 10 ? 0.9f : 0.1f, i % 20 < 10));
                a.Maschine.EmpfangsPegelMelden(2, -20f);
                h.Takt();
                h.AktiverBereich = h.Bereiche[0];
                h.FensterAktiviert();
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var klein = Fenstergroessen.Mindest(haupt.Klasse);
                var groesse = klein;
                var (bild, _) = Bildlauf.Zeichnen(haupt, 1, groesse);
                Bildablage.Ablegen(bild, name);
                var was = satz.Name + ", " + zustand;
                var seite = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is StartViewModel);
                var lauf = Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(haupt)).First(sv => sv.Content is StackPanel p && p.Children.OfType<ContentControl>().Any());

                // Kein Rollbalken nach der Anmeldung (Q12), in der kleinsten Größe, auch im Admin-Setup mit Gerätewarnung (N2: die Details stehen unter Einstellungen); im
                // Warteraum (das Gerät ist noch nicht freigegeben, die Anmeldung läuft noch).
                mass.Add(string.Create(CultureInfo.InvariantCulture,
                    $"MASS {was}: Fenster {groesse.Width:0} x {groesse.Height:0}, Inhalt {lauf.ExtentHeight:0.0} hoch, Platz {lauf.ViewportHeight:0.0} x {lauf.ViewportWidth:0.0}"));
                Messdateien.Schreiben("start-mass.txt", mass);
                // Seit 0.10.0 die strengere Prüfung aus dem Entwurf der Mischung: auch Text, den ein Vorfahr beschneidet, gekürzt oder mitten im Wort umbrochen.
                _ = NichtsAbgeschnitten(seite, was);
                // Mit Einheiten (die Beispiel-Flotte) steht die Karte Einsteigen unter der Startkarte, dann darf Start rollen (E10).
                // Seit 0.11.2 darf der Abschnitt "Senden" unter den Spalten hereinrollen; bis Sprechen und Hören kein Rollen (BisSenden).
                if (zustand != "wartet" && !h.Start.Netze.ZeigtEinsteigen)
                {
                    var (unten, platz) = BisSenden(Bildlauf.Wurzel(haupt));
                    Pruefe.Wahr(unten <= platz + 0.5,
                        $"{was}: bis Sprechen und Hören kein Rollen bei {groesse.Width:0} x {groesse.Height:0} (bis {unten:0.0}, Platz {platz:0.0}, Inhalt {lauf.ExtentHeight:0.0})");
                    var (b175, _) = Bildlauf.Zeichnen(haupt, 1.75, groesse);
                    (unten, platz) = BisSenden(Bildlauf.Wurzel(haupt));
                    Pruefe.Wahr(unten <= platz + 0.5, $"{was}: auch bei Render-Skala 1,75 bis Sprechen und Hören kein Rollen");
                    if (zustand == "verbunden" && stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(b175, name + "-175");
                }

                // Nirgends Server, Host, Port, Adresse oder Gerätename auf der Startseite, auch nicht im Admin-Setup: Die Details stehen unter Einstellungen.
                foreach (var verboten in NichtFuerFunker)
                    Pruefe.Falsch(Sichtbar(seite, verboten), was + ": für Funker nicht zu sehen: " + verboten);
                Pruefe.Falsch(Bildlauf.Nachfahren<TextBox>(seite).Any(t => ZuSehen(t) && t.Name == "AdresseFeld"), was + ": kein Feld Adresse");

                // Stecker mit Wort, Online-Zahl, Logo, Name.
                Pruefe.Wahr(Sichtbar(seite, h.Start.ServerWort) && Sichtbar(seite, h.Start.SpracheWort) && Sichtbar(seite, h.Start.OnlineText), was + ": Worte und Online");
                Pruefe.Wahr(Sichtbar(seite, "Server") && Sichtbar(seite, "Sprache") && Sichtbar(seite, "Mustermann/Max"), was + ": Beschriftung und Name");
                Pruefe.Wahr(Sichtbar(seite, "Mikrofon") && Sichtbar(seite, "Ausgabe"), was + ": Mikrofon und Ausgabe beschriftet");
                var mik = Bildlauf.Nachfahren<ComboBox>(seite).Single(c => c.Name == "MikrofonWahl");
                var aus = Bildlauf.Nachfahren<ComboBox>(seite).Single(c => c.Name == "AusgabeWahl");
                var mp = mik.TranslatePoint(new Point(0, 0), seite);
                var ap = aus.TranslatePoint(new Point(0, 0), seite);
                Pruefe.Wahr(Math.Abs(mp.Y - ap.Y) < 1 && ap.X > mp.X + mik.ActualWidth, was + ": Mikrofon und Ausgabe nebeneinander");
                var warnung = Bildlauf.Nachfahren<Border>(seite).Single(b => b.Name == "MikrofonWarnung");
                Pruefe.Gleich(zustand is "warnung" or "adminwarnung", ZuSehen(warnung), was + ": Gerätewarnung nur beim Mikrofon");
                if (zustand is "warnung" or "adminwarnung") Pruefe.Wahr(Sichtbar(seite, "verweigert den Zugriff"), was + ": Text der Warnung");
                Pruefe.Gleich(zustand == "erststart", Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => e.Name == "ErststartHinweis" && ZuSehen(e)), was + ": Erststart");
                Pruefe.Gleich(zustand == "erststart", Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => e.Name == "SprechtasteSchritt" && ZuSehen(e)), was + ": Schritt Sprechtaste");
                Pruefe.Gleich(zustand == "ohnetaste", Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => e.Name == "SprechtasteWarnung" && ZuSehen(e)), was + ": Warnung ohne Sprechtaste");
                Pruefe.Gleich(zustand is not ("erststart" or "ohnetaste"), Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => e.Name == "SprechtasteBelegt" && ZuSehen(e)),
                    was + ": Sprechtaste mit Namen und Ändern");
                // Seit 0.11.2 kein "Mikrofon und Wiedergabe sind zu" mit "Audio starten" mehr auf Start (Sarevoc 13.10.2026: "wer braucht das"; bis 0.11.1 E9).
                Pruefe.Falsch(Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => e.Name is "AudioAusRand" or "AudioStartenKnopf"), was + ": kein Audio starten auf Start");
                Pruefe.Gleich(zustand == "wartet", Bildlauf.Nachfahren<TextBox>(seite).Any(t => t.Name == "KennungFeld" && ZuSehen(t) && t.Text == Kennung), was + ": Kennung groß");
                Pruefe.Falsch(Bildlauf.Nachfahren<FrameworkElement>(seite).Any(e => e.Name == "DetailsKnopf"), was + ": kein Knopf Details auf der Startseite");

                // Knöpfe: Verbinden in der Leitfarbe und groß. Funkfeld 5, Funkfeld 10 und Wer ist da stehen seit 0.10.0 nicht mehr hier (Q87).
                var verbinden = Bildlauf.Nachfahren<Button>(seite).Single(b => b.Name == "VerbindenKnopf");
                var trennen = Bildlauf.Nachfahren<Button>(seite).Single(b => b.Name == "TrennenKnopf");
                Pruefe.Gleich(h.Start.ZeigtVerbinden, ZuSehen(verbinden), was + ": Verbinden");
                Pruefe.Gleich(h.Start.ZeigtTrennen, ZuSehen(trennen), was + ": Trennen");
                // Seit 0.11.2 flach (Knopf.Haupt in der Leitfarbe mit Akzentrand); bis 0.11.1 Glas.
                var haupt1 = verbinden.FindResource("Knopf.Haupt");
                Pruefe.Wahr(ReferenceEquals(haupt1, verbinden.Style) && !Bildlauf.Nachfahren<Glaskoerper>(verbinden).Any(), was + ": Verbinden ist der Hauptknopf in der Leitfarbe, flach");
                Pruefe.Falsch(Bildlauf.Nachfahren<Button>(seite).Any(b => b.Name is "Funkfeld5Knopf" or "Funkfeld10Knopf" or "StartWerIstDaKnopf"), was + ": keine Knöpfe Funkfeld und Wer ist da");
                var probe = Bildlauf.Nachfahren<Button>(seite).Single(x => x.Name == "HoerprobeKnopf");
                if (!probe.IsEnabled) Pruefe.Wahr(ToolTipService.GetShowOnDisabled(probe) && probe.ToolTip is string, was + ": Hörprobe gesperrt mit Grund");
                Pruefe.Wahr(Bildlauf.Nachfahren<Button>(seite).Count(b => ZuSehen(b) && ReferenceEquals(b.Style, haupt1)) <= 1,
                    was + ": höchstens ein Hauptknopf auf der Startseite (eine Karte)");

                KontrastPruefen(seite, satz, was, mindestens: 8);
                SchattenFrei(seite, was);
                Mittig(seite, was, mass);
                funkfeld.Dispose();
                teil?.Dispose();
            }
        });
        Messdateien.Schreiben("start-mass.txt", mass);
    }

    /// <summary>Stecker und Logo mittig in ihren Feldern: Element im Feld und gezeichnete Pixel, bei 100 und 175 %.</summary>
    private static void Mittig(FrameworkElement seite, string was, List<string> mass)
    {
        var logo = Bildlauf.Nachfahren<Image>(seite).Single(i => i.Name == "Logo");
        var feld = (FrameworkElement)VisualTreeHelper.GetParent(logo);
        var p = logo.TranslatePoint(new Point(0, 0), feld);
        Pruefe.Wahr(Math.Abs(p.X - ((feld.ActualWidth - logo.ActualWidth) / 2)) < 0.01 && Math.Abs(p.Y - ((feld.ActualHeight - logo.ActualHeight) / 2)) < 0.01,
            $"{was}: Logo mittig im Feld ({p.X:0.00}, {p.Y:0.00})");
        Pruefe.Wahr(Math.Abs(feld.ActualWidth - 96) < 0.5 && Math.Abs(feld.ActualHeight - 96) < 0.5, $"{was}: Logofeld 96 ({feld.ActualWidth:0} x {feld.ActualHeight:0})");
        foreach (var skala in new[] { 1.0, 1.75 })
        {
            var (l, o, r, u) = Rand(OberflaecheHilfeTests.Zeichnen(new Image { Source = logo.Source, Width = 96, Height = 96, Stretch = Stretch.Uniform }, skala));
            mass.Add(string.Create(CultureInfo.InvariantCulture, $"MASS {was}: Logo bei {skala * 100:0} %: Rand links {l}, rechts {r}, oben {o}, unten {u} Pixel (Grafik selbst: 5/0/4/12 von 512)"));
        }
        foreach (var stecker in Bildlauf.Nachfahren<Stecker>(seite))
        {
            foreach (var skala in new[] { 1.0, 1.75 })
            {
                var probe = new Stecker { Zustand = stecker.Zustand, Width = stecker.ActualWidth, Height = stecker.ActualHeight };
                var (l, o, r, u) = Rand(OberflaecheHilfeTests.Zeichnen(probe, skala));
                Pruefe.Wahr(Math.Abs(l - r) <= 1 && Math.Abs(o - u) <= 1, $"{was}: Stecker {stecker.Name} ({stecker.Zustand}) mittig bei {skala * 100:0} %: links {l}, rechts {r}, oben {o}, unten {u}");
            }
        }
    }

    /// <summary>Abstand der gezeichneten (nicht durchsichtigen) Pixel zu den vier Rändern des Bilds.</summary>
    internal static (int Links, int Oben, int Rechts, int Unten) Rand(BitmapSource bild)
    {
        var b = bild.PixelWidth;
        var h = bild.PixelHeight;
        var px = new byte[b * h * 4];
        bild.CopyPixels(px, b * 4, 0);
        int l = b, o = h, r = -1, u = -1;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < b; x++)
            {
                if (px[((y * b) + x) * 4 + 3] < 8) continue;
                l = Math.Min(l, x);
                r = Math.Max(r, x);
                o = Math.Min(o, y);
                u = Math.Max(u, y);
            }
        Pruefe.Wahr(r >= 0, "etwas gezeichnet");
        return (l, o, b - 1 - r, h - 1 - u);
    }

    /// <summary>
    /// Klein 900 x 900 (Q89) seit 0.11.2: Start steht bis zu den Spalten Sprechen und Hören (Grundansicht) ohne Rollen da; der Abschnitt "Senden"
    /// darunter (StartSenden, Sarevoc 13.10.2026: "auf der Startseite ... unten ... das Notwendige zum Senden") darf hereinrollen. Bis 0.11.1 galt für die
    /// ganze Seite: kein Rollbalken; mit "Senden" ist die Seite in Klein 30 bis 190 Einheiten zu hoch (offene Frage an Sarevoc).
    /// Liefert, wo die Grundansicht unten endet, im sichtbaren Teil des Bildlaufs der Seiten, und wie hoch dieser Teil ist.
    /// </summary>
    internal static (double Unten, double Platz) BisSenden(FrameworkElement wurzel)
    {
        var lauf = Bildlauf.Nachfahren<ScrollViewer>(wurzel).First(sv => sv.Content is StackPanel p && p.Children.OfType<ContentControl>().Any());
        var seite = Bildlauf.Nachfahren<ContentPresenter>(wurzel).First(c => c.Content is StartViewModel);
        var grund = Bildlauf.Nachfahren<FrameworkElement>(seite).First(e => e.Name == "Grundansicht");
        var unten = grund.TransformToAncestor(lauf).TransformBounds(new Rect(grund.RenderSize)).Bottom;
        return (unten, lauf.ViewportHeight);
    }

    /// <summary>
    /// Glasknöpfe zeichnen ihren Schatten außerhalb ihrer Grenzen. Wie weit, misst <see cref="Schattenrand"/> an einem gleichen
    /// Knopf mit derselben Schattenstufe (bis Stufe 2b stand hier fest "seitlich und oben 4, unten 14 Einheiten"; mit Glasdesign
    /// 0.3.1 reicht der volle Schatten deutlich weiter). Kein Vorfahr, der abschneidet (ClipToBounds, Bildlauf, Clip), darf diesen
    /// Bereich anschneiden. Liefert die Zahl der geprüften Knöpfe.
    /// </summary>
    internal static int SchattenFrei(FrameworkElement wurzel, string was)
    {
        var befunde = new List<string>();
        var n = SchattenPruefen(wurzel, was, befunde);
        Pruefe.Gleich("", string.Join("\n", befunde), was + ": angeschnittene Schatten");
        return n;
    }

    /// <summary>Wie <see cref="SchattenFrei"/>, sammelt aber jeden Befund in <paramref name="befunde"/>, statt beim ersten abzubrechen.</summary>
    internal static int SchattenPruefen(FrameworkElement wurzel, string was, List<string> befunde)
    {
        var n = 0;
        foreach (var k in Bildlauf.Nachfahren<ButtonBase>(wurzel).Where(b => ZuSehen(b) && Bildlauf.Nachfahren<Glaskoerper>(b).Any()))
        {
            n++;
            var rand = Schattenrand(k);
            var schatten = new Rect(-rand.Left, -rand.Top, k.ActualWidth + rand.Left + rand.Right, k.ActualHeight + rand.Top + rand.Bottom);
            for (var d = VisualTreeHelper.GetParent(k); d is FrameworkElement v && d is not Window; d = VisualTreeHelper.GetParent(d))
            {
                if (!v.ClipToBounds && v is not ScrollContentPresenter && VisualTreeHelper.GetClip(v) is null) continue;
                var r = k.TransformToAncestor(v).TransformBounds(schatten);
                // Ein Bildlauf schneidet oben und unten nur am Anfang und Ende seines Inhalts für immer ab; dazwischen holt Rollen
                // den Rest ins Bild.
                var (oben, unten) = v is ScrollContentPresenter s
                    ? (Math.Min(0, -s.VerticalOffset), Math.Max(v.ActualHeight, s.ExtentHeight - s.VerticalOffset))
                    : (0, v.ActualHeight);
                if (r.Left >= -0.5 && r.Top >= oben - 0.5 && r.Right <= v.ActualWidth + 0.5 && r.Bottom <= unten + 0.5) continue;
                befunde.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{was}: Schatten von {Name(k)} ({Glas.GetSchatten(k)}, Rand {rand}) angeschnitten von {v.GetType().Name} {v.Name} (Schatten {r}, Fläche {v.ActualWidth:0.0} x {v.ActualHeight:0.0})"));
            }
        }
        return n;
    }

    /// <summary>Ab dieser Deckung (von 255) zählt ein Schattenpixel: Auf der Kartenfläche dunkelt er sie um etwa 3 Stufen ab.</summary>
    internal const byte SchattenSichtbarAb = 16;

    private static readonly Dictionary<(Type, Style?, Schattenstufe, int, int), Thickness> Schattenraender = [];

    /// <summary>
    /// Wie weit der Schatten eines Glasknopfs über seine Grenzen reicht (Einheiten, je Seite): ein gleicher Knopf (Typ, Stil, Größe,
    /// Schattenstufe) allein auf durchsichtigem Grund, zweifach gezeichnet; gezählt wird jedes Pixel außerhalb des Knopfs mit
    /// mindestens <see cref="SchattenSichtbarAb"/> Deckung. Je Größe einmal gemessen.
    /// </summary>
    internal static Thickness Schattenrand(ButtonBase k)
    {
        const double luft = 60;
        const double skala = 2;
        var stufe = Glas.GetSchatten(k);
        var (b, h) = ((int)Math.Round(k.ActualWidth), (int)Math.Round(k.ActualHeight));
        var schluessel = (k.GetType(), k.Style, stufe, b, h);
        if (Schattenraender.TryGetValue(schluessel, out var bekannt)) return bekannt;
        var probe = (ButtonBase)Activator.CreateInstance(k.GetType())!;
        if (k.Style is not null) probe.Style = k.Style;
        (probe.Width, probe.Height, probe.Margin) = (b, h, new Thickness(luft));
        Glas.SetSchatten(probe, stufe);
        var bild = OberflaecheHilfeTests.Zeichnen(probe, skala);
        var bytes = new byte[bild.PixelWidth * bild.PixelHeight * 4];
        bild.CopyPixels(bytes, bild.PixelWidth * 4, 0);
        int innenL = (int)(luft * skala), innenO = innenL, innenR = innenL + (int)(b * skala), innenU = innenO + (int)(h * skala);
        int l = innenL, o = innenO, r = innenR, u = innenU;
        for (var y = 0; y < bild.PixelHeight; y++)
            for (var x = 0; x < bild.PixelWidth; x++)
            {
                if (bytes[(((y * bild.PixelWidth) + x) * 4) + 3] < SchattenSichtbarAb) continue;
                (l, o, r, u) = (Math.Min(l, x), Math.Min(o, y), Math.Max(r, x + 1), Math.Max(u, y + 1));
            }
        var rand = new Thickness((innenL - l) / skala, (innenO - o) / skala, (r - innenR) / skala, (u - innenU) / skala);
        Schattenraender[schluessel] = rand;
        return rand;
    }

    private static string Name(ButtonBase b) => b.Name.Length > 0 ? b.Name : b.Content as string ?? b.GetType().Name;

    /// <summary>Die Pegellinie im Verlauf der Sprachaktivierung ist feiner als bis 0.5.x (1,5 Einheiten).</summary>
    [Test]
    public static void Start_Schwellengrafik_PegellinieFeiner()
    {
        Pruefe.Wahr(SchwellenGrafik.PegelLinienbreite < 1.5, "feiner als 1,5: " + SchwellenGrafik.PegelLinienbreite.ToString(CultureInfo.InvariantCulture));
        Pruefe.Wahr(SchwellenGrafik.PegelLinienbreite >= 1, "noch gut zu sehen");
    }

    /// <summary>Das Logo kommt aus dem Gildenpaket; bis Stufe 4 der eingebaute Platzhalter (Eigenbau, 512 x 512).</summary>
    [Test]
    public static void Start_Logo_AusDemGildenpaket()
    {
        Bildlauf.Ausfuehren(_ =>
        {
            var logo = Gildenpaket.Eingebaut.Logo;
            Pruefe.Wahr(logo is BitmapSource { IsFrozen: true }, "eingefroren, also in jedem Thread nutzbar");
            Pruefe.Wahr(logo.Width > 0 && logo.Height > 0 && Math.Abs(logo.Width - logo.Height) < 0.01, "quadratisch");
            var ersatz = new Gildenpaket(() => new MemoryStream(EingebautBytes()));
            Pruefe.Wahr(ersatz.Logo is BitmapSource, "eine andere Quelle liefert dasselbe Format (Stufe 4 tauscht nur sie)");
        });
        var notices = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "THIRD-PARTY-NOTICES.txt"));
        Pruefe.Wahr(notices.Contains("Gildenlogo", StringComparison.Ordinal) && notices.Contains(@"grafik\gildenlogo\gildenlogo-platzhalter.svg", StringComparison.Ordinal),
            "Herkunft in THIRD-PARTY-NOTICES");
        Pruefe.Wahr(File.Exists(Path.Combine(EinrichtungTests.Repo(), "grafik", "gildenlogo", "gildenlogo-platzhalter.svg")), "Vorlage des Platzhalters im Repository");
        Pruefe.Wahr(notices.Contains("Glasdesign.dll", StringComparison.Ordinal), "Glasdesign.dll in THIRD-PARTY-NOTICES");
    }

    /// <summary>
    /// Ein gesperrter Knopf sagt im Tooltip warum (Befund N1); die Sichtprobe zeigt Knopf und Hilfekarte. Seit 0.10.0 dient Verbinden beim ersten
    /// Start als Beispiel (bis 0.9.3 Funkfeld 5, das es auf Start nicht mehr gibt, Q87).
    /// </summary>
    [Test]
    public static void Start_TooltipMitGesperrtGrund_Sichtprobe()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer);
            using var k = Einrichten(a, hoerweise: false);
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            var haupt = new Hauptfenster(h, a.Ablage);
            _ = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.Mindest(Groessenklasse.Klein));
            var knopf = Bildlauf.Nachfahren<Button>(Bildlauf.Wurzel(haupt)).Single(b => b.Name == "VerbindenKnopf");
            Pruefe.Falsch(knopf.IsEnabled, "beim ersten Start gesperrt");
            Pruefe.Gleich(StartViewModel.ErstHoerweise, knopf.ToolTip as string, "der Grund");
            Pruefe.Wahr(ToolTipService.GetShowOnDisabled(knopf), "auch gesperrt sichtbar");
            const double skala = 1.5;
            var bogen = new Bogen(460, 170, skala, Palette.Anthrazit);
            bogen.Text("Gesperrter Knopf mit Grund (Tooltip)", 16, 12, 14, fett: true);
            var kopie = new Button { Content = "Verbinden", IsEnabled = false };
            Glasdesign.Hilfe.SetText(kopie, "Verbindet dich mit dem Funk der Gilde.");
            Glasdesign.Hilfe.SetGesperrt(kopie, StartViewModel.ErstHoerweise);
            var flaeche = new Border { Background = Bogen.Pinsel(Palette.Flaeche), Padding = new Thickness(10, 8, 10, 18), Child = kopie };
            var kb = OberflaecheHilfeTests.Zeichnen(flaeche, skala);
            bogen.Bild(kb, 16, 44, kb.PixelWidth / skala, kb.PixelHeight / skala);
            var karte = OberflaecheHilfeTests.Zeichnen(new ToolTip { Content = kopie.ToolTip }, skala);
            bogen.Bild(karte, 40, 44 + (kb.PixelHeight / skala) + 4, karte.PixelWidth / skala, karte.PixelHeight / skala);
            Bildablage.Ablegen(bogen.Fertig(), "tooltip-gesperrt-grund");
            funkfeld.Dispose();
        });
    }

    // ------------------------------------------------------------------ Stufe 3, Schritt 3.5: Restbefunde der Gegenprüfung Stufe 2 (N1, N2, N4, N9, N10)

    /// <summary>
    /// N1: Die Ablage zeigt den Pfad der Einstellungen nicht (er trägt den Windows-Benutzernamen), sondern wo sie liegen, dazu einen
    /// Knopf "Ordner öffnen". Der Test sieht in Funker- und Admin-Setup nach, dass weder der Pfad noch der Benutzername irgendwo steht,
    /// und dass der Knopf den Ordner der Datei öffnet (hier an der Attrappe).
    /// </summary>
    [Test]
    public static void Einstellungen_Ablage_ZeigtKeinenPfad_UndOeffnetDenOrdner()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer);
            using var k = Einrichten(a);
            var h = a.Haupt(out _, out _, out _, out _);
            Pruefe.Gleich("An einem eigenen Ort", h.Einstellungen.OrtText, "der Testordner liegt nicht im Benutzerordner der App");
            // Seit 0.10.0 (AP3) unter Fehlersuche > Feinheiten (bis 0.9.3 Karte Ablage).
            Seiten.Feinheiten(h, Abschnittskennung.Fehlersuche);
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            _ = Ganz(haupt, 960);
            var inhalt = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);
            Pruefe.Falsch(Sichtbar(inhalt, a.Speicher.Pfad) || Sichtbar(inhalt, a.Ordner.Pfad) || Sichtbar(inhalt, Environment.UserName), "weder der Pfad noch der Benutzername steht da");
            Pruefe.Wahr(Sichtbar(inhalt, "Einstellungen: An einem eigenen Ort"), "stattdessen: wo sie liegen");
            var knopf = Bildlauf.Nachfahren<Button>(inhalt).Single(b => b.Content is "Ordner öffnen");
            Pruefe.Wahr(ZuSehen(knopf) && knopf.IsEnabled && Hilfe.GetText(knopf) is { } hilfe && hilfe.Contains("Explorer", StringComparison.Ordinal), "Knopf Ordner öffnen mit Hilfetext");
            knopf.Command.Execute(null);
            Pruefe.Gleich(a.Ordner.Pfad, a.Fenster.Geoeffnet.Single(), "geöffnet wird der Ordner der Datei, nicht ein anderer");

            // Im Regelfall liegt die Datei im Benutzerordner der App: Dann sagt der Satz genau das.
            using var b2 = new Aufbau(FunkLage.Leer);
            var pfad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JG-TacRelay", "einstellungen.json");
            using var ablage = new Einstellungsablage(new EinstellungsSpeicher(pfad), new ClientEinstellungen(), SofortTakt.Instanz, TimeSpan.FromHours(1));
            var regel = new EinstellungenViewModel(ablage, b2.Funkfeld());
            Pruefe.Gleich("Im Benutzerordner unter JG-TacRelay", regel.OrtText, "im Regelfall");
            Pruefe.Falsch(regel.OrdnerOeffnenCommand.CanExecute(null), "ohne Fensterdienst gibt es nichts zu öffnen");
        });
    }

    /// <summary>
    /// N2: Die Details der Verbindung stehen unter Einstellungen, Erweitert, und nur im Admin-Setup; die Startseite hat dort keinen Knopf
    /// mehr und rollt auch im ungünstigsten Admin-Fall nicht (das misst der Sichtprobentest als Zustand "adminwarnung").
    /// </summary>
    [Test]
    public static void Einstellungen_Erweitert_DetailsNurImAdminSetup_MitHostPortUndAdresse()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var admin in AdminSetups)
            {
                var was = admin ? "Admin-Setup" : "Funker-Setup";
                using var a = new Aufbau(FunkLage.Leer);
                using var k = Einrichten(a);
                Funk.Verwaltung.Verwaltungsteil? teil = null;
                if (admin)
                {
                    var vd = new VerwaltungsdienstAttrappe();
                    teil = new Funk.Verwaltung.Verwaltungsteil(vd, new Funk.Verwaltung.Modelle.VerwaltungViewModel(vd));
                    a.Verwaltung = teil;
                }
                var h = a.Haupt(out _, out _, out _, out _);
                h.AktiverBereich = h.Bereiche.Single(b => b.Inhalt is EinstellungenViewModel);
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                _ = Ganz(haupt, 960);
                var inhalt = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);
                foreach (var da in UnterDetails)
                    Pruefe.Gleich(admin, Sichtbar(inhalt, da), was + ": unter Erweitert " + da);
                Pruefe.Gleich(admin, Bildlauf.Nachfahren<TextBox>(inhalt).Any(t => ZuSehen(t) && t.Name == "AdresseFeld"), was + ": Feld Adresse");
                Pruefe.Gleich(admin, Sichtbar(inhalt, "Erweitert: Details der Verbindung"), was + ": Überschrift Erweitert");
                teil?.Dispose();
            }
        });
    }

    /// <summary>
    /// N4: Eine Einstellungsdatei aus 0.5.x kennt den Freigabestand nicht. Bis zum ersten Verbinden sagt die Startseite dann "Wird beim
    /// Verbinden geprüft" statt "noch nicht freigegeben"; nach dem Verbinden gilt "freigegeben" auch getrennt (gemerkt), ein neuer
    /// Antrag macht es zu einem bekannten "noch nicht freigegeben".
    /// </summary>
    [Test]
    public static void Start_GeraetStandUnbekannt_WirdBeimVerbindenGeprueft_DanachGemerkt()
    {
        using var a = new Aufbau(FunkLage.Leer);
        using var k = Einrichten(a);
        Pruefe.Gleich((bool?)null, a.Ablage.Aktuell.Verbindung.Freigegeben, "eine neue oder alte Datei kennt den Stand nicht");
        var s = a.Haupt(out _, out _, out _, out _).Start;
        Pruefe.Gleich("Wird beim Verbinden geprüft", s.GeraetText, "unbekannter Stand: keine falsche Aussage");
        Pruefe.Falsch(s.GeraetFreigegeben, "und kein Haken");
        a.Dienst.LageSetzen(_ => FunkDienstAttrappe.Beispiel());
        Pruefe.Gleich("Gerät freigegeben", s.GeraetText, "verbunden: freigegeben");
        Pruefe.Gleich((bool?)true, a.Ablage.Aktuell.Verbindung.Freigegeben, "gemerkt");
        a.Dienst.LageSetzen(_ => FunkLage.Leer);
        Pruefe.Gleich("Gerät freigegeben", s.GeraetText, "getrennt nach der Freigabe: bleibt freigegeben");
        a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Registrierung, Server = "Meine Staffel", Geraet = "ABCD-EFGH" });
        a.Dienst.LageSetzen(_ => FunkLage.Leer);
        Pruefe.Gleich("Gerät noch nicht freigegeben", s.GeraetText, "nach einem neuen Antrag: bekannt nicht freigegeben");
        Pruefe.Gleich((bool?)false, a.Ablage.Aktuell.Verbindung.Freigegeben, "auch das ist gemerkt");

        // Die Datei schreibt nichts, solange der Stand unbekannt ist: Ein älterer Client liest sie unverändert.
        using var b = new Aufbau(FunkLage.Leer);
        var json = System.Text.Json.JsonSerializer.Serialize(b.Ablage.Aktuell.Verbindung);
        Pruefe.Falsch(json.Contains("reigegeben", StringComparison.Ordinal), "unbekannt wird nicht geschrieben: " + json);
    }

    /// <summary>N9: Trennen bleibt sichtbar, sobald verbunden oder verbindend wird, auch bei offener Erststart-Frage ("Beim Start verbinden").</summary>
    [Test]
    public static void Start_Trennen_BleibtBeiOffenerErststartFrage()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer);
            using var k = Einrichten(a, hoerweise: false);
            var h = a.Haupt(out _, out _, out _, out _);
            var s = h.Start;
            Pruefe.Wahr(s.ErststartOffen && !s.ZeigtKnopfZeile && !s.ZeigtVerbinden && !s.ZeigtTrennen, "Frage offen, nichts läuft: keine Knöpfe, erst die Frage");
            a.Dienst.LageSetzen(_ => FunkLage.Leer with { Status = Verbindungsstatus.Verbinden });
            Pruefe.Wahr(s.ErststartOffen && s.ZeigtTrennen && s.ZeigtKnopfZeile && !s.ZeigtVerbinden, "Frage offen und es verbindet: Trennen bleibt, Verbinden nicht");
            h.AktiverBereich = h.Bereiche[0];
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            _ = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.Mindest(haupt.Klasse));
            var seite = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is StartViewModel);
            var trennen = Bildlauf.Nachfahren<Button>(seite).Single(b => b.Name == "TrennenKnopf");
            var verbinden = Bildlauf.Nachfahren<Button>(seite).Single(b => b.Name == "VerbindenKnopf");
            Pruefe.Wahr(ZuSehen(trennen) && trennen.IsEnabled && !ZuSehen(verbinden), "im Fenster: Trennen zu sehen und gültig, Verbinden nicht");
            a.Dienst.LageSetzen(_ => FunkLage.Leer);
            Pruefe.Falsch(s.ZeigtKnopfZeile, "getrennt und Frage offen: wieder nur die Frage");
        });
    }

    // 0.8.1, Teil 3: Start_RuheSatzUnterDemBalken_GriffeFrei entfällt mit 0.10.0 (AP2, Q87, Bild m-8): Start hat keinen Pegelbalken mehr; Pegel,
    // SENDET und der Satz "Ruhe: es wird nichts gesendet" stehen nur noch in der Statuszeile (OberflaecheUmbauRahmenTests, OberflaechePegelGelbTests).
}
