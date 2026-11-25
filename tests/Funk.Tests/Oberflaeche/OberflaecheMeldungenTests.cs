// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Klang.Eingabe;
using Funk.Klang.Verarbeitung;
using Funk.Protokoll.Steuerung;
using Funk.Tests.Maschine;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using static Funk.Tests.Oberflaeche.OberflaecheFunkfeldGroesseTests;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Die Oberfläche des Meldesystems 0.12.0 (Sarevoc 11.10. und 16.10.2026, M1-M24, tmp\melde\SPEZ.md "Client"), Schritt 2 und Textmeldung. Mit einer Uhr der Tests: die
/// Anzeige über dem Spiel (Befehl 8 s und Ersetzen, Zustand klein, Ampel, Countdown mit Zeiten in den Feldern, Ton, Ende bei 0, Größe mal Faktor und nie
/// größer als der Monitor, Rückfall der Ausführung, Stopp-Bild, Probe, "gemeldet"), der Uhrabgleich über die Serverzeit des Dienstes, Lage und Monitor
/// des Fensters (Stile, kein Owner, nie aktiviert, im Quelltext bewacht), der Signalton im Mischer, "Bereit" per Taste und Knopf, das Feld der Leitung
/// (Senden, Sperre von 2 s ab der Antwort, Zeile hart 60, Countdown aus Uhrzeit oder Dauer, Bereit-Stand), die Seite für die Führung ohne Auftrag, der
/// Abschnitt unter Einstellungen und der Haken auf der Mitgliederkarte. Im Bildlauf (kein Fenster auf dem Bildschirm) das Feld der Leitung, die Anzeige
/// und der Abschnitt; mit FUNK_BILDER die Bilder vorschau-0120-meldungen-leitung, -overlay und -einstellungen. Der Klassenname steht nach
/// OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheMeldungenTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Serverzeit der Tests: 16.10.2026, 19:25:08 UTC (21:25:08 in Ortszeit +2).</summary>
    private const long T0 = 1_792_178_708_000;

    private static readonly TimeSpan Mesz = TimeSpan.FromHours(2);

    private static DateTimeOffset Ort(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(Mesz);

    private static Meldungseintrag E(string motiv, byte groesse = 1, string? zeile = null, uint von = 2, long? ziel = null, string? form = null, uint? nummer = null) =>
        new() { Motiv = motiv, Groesse = groesse, Zeile = zeile, Von = von, Seit = T0, Ziel = ziel, Form = form, Nummer = nummer };

    private static Textmeldung Tm(string text, string farbe = Textmeldungen.Weiss, byte groesse = 1, string dauer = Textmeldungen.Kurz, uint von = 2) =>
        new() { Text = text, Farbe = farbe, Groesse = groesse, Dauer = dauer, Von = von, Seit = T0 };

    /// <summary>120 Zeichen in Wörtern, wie eine lange Textmeldung der Leitung.</summary>
    private const string Text120 = "Achtung: alle Schiffe sammeln am Tor von Port Tressler, danach Sprung nach Pyro; Funkdisziplin, nur die Leitung spricht.";

    /// <summary>Eine Anzeige über der Attrappe mit eigener Uhr, Serverzeit, Einstellungen, Tönen und "gemeldet".</summary>
    private sealed class Probe
    {
        public Probe()
        {
            Dienst = new FunkDienstAttrappe(FunkDienstAttrappe.Beispiel());
            Anzeige = new Meldeanzeige(Dienst, () => Einstellungen, () => Uhr, () => Server, t =>
            {
                Toene.Add(t);
                return true;
            }, () => Gemeldet, Ort);
        }

        public FunkDienstAttrappe Dienst { get; }
        public MeldungenEinstellungen Einstellungen { get; } = new();
        public long Uhr { get; set; } = 50_000;
        public long Server { get; set; } = T0;
        public uint? Gemeldet { get; set; }
        public List<Signaltonart> Toene { get; } = [];
        public Meldeanzeige Anzeige { get; }

        public string Text => Anzeige.Bild.Text;

        public void Stand(Func<Meldestand, Meldestand> aendern) =>
            Dienst.LageSetzen(l => l with { Meldestand = aendern(l.Meldestand ?? new Meldestand { Anlass = Meldungsvorgang.Stand }) });

        public void Befehl(string motiv, byte groesse = 1, string? zeile = null) =>
            Anzeige.Eingegangen(new Meldestand { Anlass = Meldungsvorgang.Befehl, Von = 2, Befehl = E(motiv, groesse, zeile) });

        /// <summary>Eine Textmeldung kommt an wie vom Server: eine bleibende steht danach in der Lage, eine mit 8 s ersetzt sie dort (leer).</summary>
        public void TextKommt(string text, string farbe = Textmeldungen.Weiss, byte groesse = 1, string dauer = Textmeldungen.Kurz)
        {
            var t = Tm(text, farbe, groesse, dauer);
            Stand(s => s with { Text = Textmeldungen.Bleibend(t) });
            Anzeige.Eingegangen(new Meldestand { Anlass = Meldungsvorgang.Text, Von = 2, Text = t });
        }

        public Textplatz Textplatz => Anzeige.Bild.Textplatz;

        /// <summary>Die Zeit läuft im Bildtakt weiter, Uhr und Serverzeit gleich schnell.</summary>
        public void Weiter(long ms)
        {
            var ende = Uhr + ms;
            while (Uhr < ende)
            {
                var schritt = Math.Min(33, ende - Uhr);
                Uhr += schritt;
                Server += schritt;
                Anzeige.Takt();
            }
        }

        public Meldeplatz Platz(string schluessel) => Anzeige.Bild.Plaetze.Single(p => p.Schluessel == schluessel);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Anzeige über dem Spiel
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Ein Befehl steht 8 s groß ab dem Eingang, mit Zeile und einem Ton; ein neuer ersetzt den alten (gleich von wem) und läuft wieder 8 s. Danach ist
    /// nichts mehr da, und das Fenster wäre verborgen. Ton aus: kein Ton. Der Anlass "Stand" nach Welcome bringt keinen Befehl (M20) und keinen Ton;
    /// ein Zustand bringt keinen Ton.
    /// </summary>
    [Test]
    public static void Meldungen_Anzeige_Befehl8s_Ersetzen_Ton()
    {
        var p = new Probe();
        Pruefe.Falsch(p.Anzeige.Sichtbar, "ohne Meldung nichts");
        p.Befehl("sammelpunkt_x", zeile: "Hangar 2");
        Pruefe.Wahr(p.Anzeige.Sichtbar && p.Text == "haupt sammelpunkt_x (Hangar 2)", "Befehl mit Zeile: " + p.Text);
        Pruefe.Gleich("Befehl", string.Join(",", p.Toene), "ein Ton beim Befehl");
        p.Weiter(5000);
        p.Befehl("stopp", 2);
        Pruefe.Gleich("haupt stopp", p.Text, "ein neuer Befehl ersetzt den alten (ohne Zeile, wenn keine kam)");
        Pruefe.Gleich(2, p.Toene.Count, "und klingt wieder");
        p.Weiter(Meldeanzeige.BefehlMs - 40);
        Pruefe.Gleich("haupt stopp", p.Text, "kurz vor 8 s noch da (ab dem neuen gezählt)");
        p.Weiter(80);
        Pruefe.Wahr(p.Text == "" && !p.Anzeige.Sichtbar, "nach 8 s fort: " + p.Text);

        p.Einstellungen.Ton = false;
        p.Befehl("rueckzug");
        Pruefe.Wahr(p.Text == "haupt rueckzug" && p.Toene.Count == 2, "Ton aus: Befehl ohne Ton");
        p.Einstellungen.Ton = true;
        p.Anzeige.Eingegangen(new Meldestand { Anlass = Meldungsvorgang.Stand, Zustand = E("pause") });
        p.Stand(s => s with { Zustand = E("pause") });
        p.Anzeige.Eingegangen(new Meldestand { Anlass = Meldungsvorgang.Zustand, Zustand = E("pause") });
        Pruefe.Gleich(2, p.Toene.Count, "Stand nach Welcome und Zustand: kein Ton");
        Pruefe.Gleich("haupt rueckzug\nzustand pause", p.Text, "der Befehl bleibt, der Zustand klein daneben");
    }

    /// <summary>
    /// Plätze nebeneinander: Befehl oder Bereit-Abfrage groß, Zustand klein daneben (halbe Grundgröße, auch wenn mehr gesendet wurde), Ampel und Countdown
    /// je eigener Platz. Größe: die längste Seite ist Grundgröße mal Faktor (180, 360, 540), der Countdown 0,6 davon hoch; nie größer als der Monitor,
    /// und eine zu breite Reihe wird als Ganzes kleiner. Ein Zustand wird ersetzt oder aufgehoben. Die Bereit-Abfrage steht groß, solange kein Befehl
    /// läuft; nach dem eigenen Melden steht "gemeldet" darunter.
    /// </summary>
    [Test]
    public static void Meldungen_Anzeige_Plaetze_Groesse_NieGroesserAlsDerMonitor()
    {
        var p = new Probe();
        p.Stand(s => s with { Zustand = E("position_halten", 3), Ampel = E("ampel_gelb_bereithalten", 2), Abfrage = E("bereit_melden", 3, "Alle bereit?", nummer: 4) });
        p.Anzeige.Takt();
        Pruefe.Gleich("haupt bereit_melden (Alle bereit?)\nzustand position_halten\nampel ampel_gelb_bereithalten", p.Text, "Bereit groß, Zustand, Ampel");
        static double Lang(Meldeplatz x) => Math.Max(x.Breite, x.Hoehe);
        Pruefe.Wahr(Math.Abs(Lang(p.Platz(Meldebild.Haupt)) - 540) < 0.6, T($"Bereit dreifach: 540 ({Lang(p.Platz(Meldebild.Haupt))})"));
        Pruefe.Wahr(Math.Abs(Lang(p.Platz(Meldebild.Zustand)) - 90) < 0.6, T($"Zustand klein, halbe Grundgröße, obwohl 3 gesendet: 90 ({Lang(p.Platz(Meldebild.Zustand))})"));
        Pruefe.Wahr(Math.Abs(p.Platz(Meldebild.Ampel).Hoehe - 360) < 0.6 && p.Platz(Meldebild.Ampel).Breite < 200, "Ampel doppelt: 360 hoch, hochkant");
        Pruefe.Wahr(p.Platz(Meldebild.Haupt).Abstand.Right > 0 && p.Platz(Meldebild.Ampel).Abstand.Right == 0, "Abstand zwischen den Plätzen, nicht nach dem letzten");

        p.Gemeldet = 4;
        p.Anzeige.Takt();
        Pruefe.Gleich("haupt bereit_melden (Alle bereit?) gemeldet", p.Platz(Meldebild.Haupt).Text, "nach dem eigenen Melden: gemeldet");
        p.Befehl("start_freigegeben", 3);
        Pruefe.Gleich("haupt start_freigegeben", p.Platz(Meldebild.Haupt).Text, "ein Befehl steht 8 s über der Abfrage");
        p.Weiter(Meldeanzeige.BefehlMs + 50);
        Pruefe.Gleich("haupt bereit_melden (Alle bereit?) gemeldet", p.Platz(Meldebild.Haupt).Text, "danach wieder die Abfrage");

        // Grundgröße 300: dreifach wären 900; auf einem Monitor mit 600 x 400 Platz nie größer als er.
        p.Einstellungen.Grundgroesse = 300;
        p.Anzeige.Grenze = new Size(600, 400);
        Pruefe.Wahr(p.Anzeige.Bild.Plaetze.All(x => x.Breite <= 600.5 && x.Hoehe <= 400.5), "kein Bild größer als der Platz auf dem Monitor");
        var summe = p.Anzeige.Bild.Plaetze.Sum(x => x.Breite + x.Abstand.Right);
        Pruefe.Wahr(summe <= 600.5, T($"die Reihe passt in die Breite ({summe:0.0})"));
        p.Anzeige.Grenze = new Size(double.PositiveInfinity, double.PositiveInfinity);
        p.Einstellungen.Grundgroesse = 180;

        // Zustand ersetzt, dann aufgehoben; Ampel aus, Abfrage zu.
        p.Stand(s => s with { Zustand = E("funkdisziplin") });
        p.Anzeige.Takt();
        Pruefe.Wahr(p.Text.Contains("zustand funkdisziplin", StringComparison.Ordinal) && !p.Text.Contains("position_halten", StringComparison.Ordinal), "Zustand ersetzt");
        p.Stand(s => s with { Zustand = null, Ampel = null, Abfrage = null });
        p.Anzeige.Takt();
        Pruefe.Wahr(p.Text == "" && !p.Anzeige.Sichtbar, "alles aufgehoben: nichts zu sehen");
        p.Stand(s => s with { Ampel = E("ampel_rot_stopp") });
        p.Dienst.LageSetzen(l => l with { Meldestand = null });
        p.Anzeige.Takt();
        Pruefe.Falsch(p.Anzeige.Sichtbar, "getrennt (kein Stand mehr): nichts");
    }

    /// <summary>
    /// Countdown "Start um": Uhrzeit in Ortszeit in das Feld START UM, der Rest (Minuten und Sekunden zweistellig, ab einer Stunde mit Stunden) in
    /// COUNTDOWN; "Restzeit": nur der Rest. Die Felder liegen in den freien Feldern des Bilds (an den Bildern gemessen, in den Zuschnitt umgerechnet).
    /// In den letzten zehn Sekunden je Sekunde ein Ton (zehn, auch mit dem Bildtakt nur einer je Sekunde), keiner mit Ton aus; bei 0 ist er fort.
    /// Gerechnet wird mit der Serverzeit des Dienstes (Uhrabgleich), nicht mit der Uhr dieses PCs.
    /// </summary>
    [Test]
    public static void Meldungen_Anzeige_Countdown_ZeitenFelderTonEnde_Serverzeit()
    {
        Pruefe.Gleich("04:52|00:09|1:04:52|00:00", string.Join("|", Meldeanzeige.Restzeit(292), Meldeanzeige.Restzeit(9), Meldeanzeige.Restzeit(3892),
            Meldeanzeige.Restzeit(-3)), "Restzeit in Worten");
        Pruefe.Gleich("10|1|1|0|0", string.Join("|", Meldeanzeige.RestSekunden(T0 + 9_200, T0), Meldeanzeige.RestSekunden(T0 + 100, T0),
            Meldeanzeige.RestSekunden(T0 + 1000, T0), Meldeanzeige.RestSekunden(T0, T0), Meldeanzeige.RestSekunden(T0 - 5, T0)), "aufgerundet, 0 ist vorbei");

        var p = new Probe();
        var ziel = T0 + 292_000; // 21:30:00 Ortszeit
        p.Stand(s => s with { Countdown = E("countdown_startzeit", 2, "Start Hangar", ziel: ziel, form: "Startzeit") });
        p.Anzeige.Takt();
        var c = p.Platz(Meldebild.Countdown);
        Pruefe.Gleich("countdown countdown_startzeit (Start Hangar) [21:30 | 04:52]", c.Text, "Start um 21:30, noch 4:52");
        Pruefe.Wahr(Math.Abs(c.Hoehe - (180 * 2 * Meldeanzeige.CountdownAnteil)) < 0.6 && c.Breite > c.Hoehe * 2, T($"doppelt: 216 hoch, quer ({c.Breite:0} x {c.Hoehe:0})"));
        Pruefe.Gleich("startum|countdown", string.Join("|", c.Felder.Select(f => f.Name)), "zwei Felder");
        foreach (var f in c.Felder)
        {
            Pruefe.Wahr(f.Rand.Left > c.Breite * 0.35 && f.Rand.Left + f.Breite <= c.Breite + 0.5 && f.Rand.Top >= 0 && f.Rand.Top + f.Hoehe <= c.Hoehe + 0.5,
                T($"Feld {f.Name} rechts der Sanduhr und im Bild ({f.Rand.Left:0},{f.Rand.Top:0} {f.Breite:0} x {f.Hoehe:0} in {c.Breite:0} x {c.Hoehe:0})"));
            Pruefe.Wahr(Math.Abs(f.Schrift - Math.Round(f.Hoehe * 0.66, 1)) < 0.2, "Schrift zwei Drittel der Feldhöhe");
        }
        Pruefe.Wahr(c.Felder[0].Rand.Top + c.Felder[0].Hoehe < c.Felder[1].Rand.Top, "START UM über COUNTDOWN");

        // Der PC geht 90 s vor: gerechnet wird mit der Serverzeit, der Rest bleibt.
        p.Uhr += 90_000;
        p.Anzeige.Takt();
        Pruefe.Gleich("21:30 | 04:52", string.Join(" | ", c.Felder.Select(f => f.Text)), "Uhr des PCs zählt nicht");
        p.Server += 292_000 - 10_500;
        p.Anzeige.Takt();
        Pruefe.Gleich("00:11", c.Felder[1].Text, "11 s vor Schluss");
        Pruefe.Gleich(0, p.Toene.Count, "vor den letzten 10 s kein Ton");
        p.Weiter(10_400);
        Pruefe.Gleich(10, p.Toene.Count(t => t == Signaltonart.Sekunde), "je Sekunde der letzten zehn ein Ton");
        Pruefe.Gleich("00:01", c.Felder[1].Text, "noch 1 s");
        p.Weiter(200);
        Pruefe.Wahr(!p.Anzeige.Sichtbar && p.Anzeige.Bild.Plaetze.Count == 0, "bei 0 fort");
        Pruefe.Gleich(10, p.Toene.Count, "bei 0 kein Ton mehr");

        // Restzeit: ein Feld; Ton aus.
        var q = new Probe();
        q.Einstellungen.Ton = false;
        q.Stand(s => s with { Countdown = E("countdown_restzeit", 1, ziel: T0 + 3_892_000, form: "Restzeit") });
        q.Anzeige.Takt();
        var r = q.Platz(Meldebild.Countdown);
        Pruefe.Gleich("restzeit:1:04:52", string.Join("|", r.Felder.Select(f => f.Name + ":" + f.Text)), "Restzeit: ein Feld mit Stunden");
        Pruefe.Wahr(Math.Abs(r.Hoehe - 108) < 0.6, "normal: 108 hoch");
        q.Server = T0 + 3_892_000 - 5_000;
        q.Weiter(6_000);
        Pruefe.Wahr(q.Toene.Count == 0 && !q.Anzeige.Sichtbar, "Ton aus: keiner; bei 0 fort");

        // Die echte Serverzeit des Dienstes: die Attrappe mit Versatz.
        var d = new FunkDienstAttrappe(FunkDienstAttrappe.Beispiel());
        var jetzt = new DateTimeOffset(2026, 10, 16, 19, 25, 8, TimeSpan.Zero);
        d.Uhr = () => jetzt;
        d.LageSetzen(l => l with { ZeitVersatz = TimeSpan.FromSeconds(30) });
        var a = new Meldeanzeige(d, () => new MeldungenEinstellungen(), () => 0, ortszeit: Ort);
        d.LageSetzen(l => l with { Meldestand = new Meldestand { Anlass = Meldungsvorgang.Stand, Countdown = E("countdown_restzeit", ziel: jetzt.ToUnixTimeMilliseconds() + 60_000, form: "Restzeit") } });
        a.Takt();
        Pruefe.Gleich("00:30", a.Bild.Plaetze.Single().Felder.Single().Text, "Serverzeit des Dienstes (30 s Versatz): noch 30 s, nicht 60");
    }

    /// <summary>
    /// Ausführung und Stopp-Bild des Funkers mit Rückfall auf Schlicht: Metall für Sammeln, Schlicht für Los in Metall, Nur Schrift für Los, Schlicht für
    /// Sammeln in Nur Schrift; beim Stopp Handschuh oder Xeno, in der Ausführung. Die Bilder sind auf ihre sichtbaren Bildpunkte zugeschnitten. Die Probe:
    /// 3 s ein Befehl Sammeln in Grundgröße mit der Probezeile, beim Stopp-Bild der Stopp; sie verdrängt für die 3 s, was steht.
    /// </summary>
    [Test]
    public static void Meldungen_Anzeige_Ausfuehrung_Rueckfall_Stopp_Probe()
    {
        var p = new Probe();
        string Ressource(string motiv)
        {
            p.Befehl(motiv);
            return p.Platz(Meldebild.Haupt).Ressource ?? "-";
        }
        Pruefe.Gleich("Meldungen.schlicht.sammeln.png", Ressource("sammeln"), "ab Werk Schlicht");
        Pruefe.Gleich("Meldungen.schlicht.stopp_handschuh.png", Ressource("stopp"), "ab Werk Handschuh");
        p.Einstellungen.Ausfuehrung = MeldungenEinstellungen.AusfuehrungMetall;
        p.Einstellungen.StoppBild = MeldungenEinstellungen.StoppXeno;
        Pruefe.Gleich("Meldungen.metall.sammeln.png", Ressource("sammeln"), "Metall");
        Pruefe.Gleich("Meldungen.schlicht.los.png", Ressource("los"), "Los gibt es nicht in Metall: Schlicht");
        Pruefe.Gleich("Meldungen.metall.stopp_xeno.png", Ressource("stopp"), "Xeno in Metall");
        p.Einstellungen.Ausfuehrung = MeldungenEinstellungen.AusfuehrungSchrift;
        Pruefe.Gleich("Meldungen.schrift.los.png", Ressource("los"), "Nur Schrift");
        Pruefe.Gleich("Meldungen.schlicht.sammeln.png", Ressource("sammeln"), "Sammeln gibt es nicht in Nur Schrift: Schlicht");
        var los = p.Platz(Meldebild.Haupt);
        p.Befehl("los");
        Pruefe.Wahr(los.Breite > los.Hoehe * 2 && Math.Abs(los.Breite - 180) < 0.6, T($"Nur Schrift zugeschnitten: breit und flach ({los.Breite:0} x {los.Hoehe:0})"));
        Pruefe.Wahr(p.Platz(Meldebild.Haupt).Bild is BitmapSource { IsFrozen: true }, "das Bild ist eingefroren");

        var g = Meldungsbilder.Ganz("Meldungen.schlicht.countdown_startzeit.png")!;
        Pruefe.Wahr(g.GanzBreite == 600 && g.GanzHoehe == 400 && g.Ausschnitt.Y > 40 && g.Ausschnitt.Height < 300 && g.Bild.PixelHeight == g.Ausschnitt.Height,
            T($"Countdown zugeschnitten: {g.Ausschnitt}"));
        Pruefe.Wahr(Meldebild.FeldStartUm.Top > g.Ausschnitt.Y && Meldebild.FeldCountdown.Bottom < g.Ausschnitt.Y + g.Ausschnitt.Height, "die Felder liegen im Zuschnitt");
        Pruefe.Gleich(new Int32Rect(1, 2, 3, 1), Meldungsbilder.Sichtbar(Punkte(5, 4, (1, 2), (3, 2)), 5, 4, 20), "sichtbare Bildpunkte: der Rahmen um sie");
        Pruefe.Gleich(new Int32Rect(0, 0, 5, 4), Meldungsbilder.Sichtbar(Punkte(5, 4), 5, 4, 20), "nichts sichtbar: das ganze Bild");
        Pruefe.Wahr(Meldungsbilder.Kachel("Meldungen.schlicht.gibtesnicht.png") is null && Meldungsbilder.Ganz(null) is null, "fehlt: null, kein Fehler");

        // Probe.
        var q = new Probe();
        q.Stand(s => s with { Zustand = E("pause") });
        q.Anzeige.ProbeZeigen();
        Pruefe.Wahr(q.Anzeige.ProbeLaeuft && q.Text == "haupt sammeln (" + Meldeanzeige.ProbeZeile + ")", "Probe: Sammeln mit der Probezeile, sonst nichts");
        q.Weiter(Meldeanzeige.ProbeMs - 40);
        Pruefe.Wahr(q.Anzeige.ProbeLaeuft, "bis 3 s");
        q.Weiter(80);
        Pruefe.Gleich("zustand pause", q.Text, "nach 3 s wieder, was steht");
        q.Anzeige.ProbeZeigen(Meldungskatalog.Stopp);
        Pruefe.Gleich("haupt stopp (" + Meldeanzeige.ProbeZeile + ")", q.Text, "beim Stopp-Bild der Stopp");
        Pruefe.Gleich(0, q.Toene.Count, "die Probe klingt nicht");
    }

    /// <summary>
    /// Textmeldung (M24) mit der Uhr der Tests: "8 s" steht 8 s ab dem Eingang und klingt wie ein Befehl (Ton aus: keiner); "bleibt stehen" steht, bis sie
    /// aufgehoben oder durch eine neue ersetzt wird, auch nach 30 s; eine neue mit 8 s ersetzt eine stehende, aufheben nimmt auch eine mit 8 s sofort weg.
    /// Nach Welcome (Anlass Stand) steht eine bleibende wieder da, ohne Ton. Unter einem Befehl steht der Text mit dem Abstand der Plätze, allein ganz oben;
    /// die Probe verdrängt ihn für 3 s.
    /// </summary>
    [Test]
    public static void Meldungen_Anzeige_Text_AchtSekundenBleibtErsetzenTon()
    {
        var p = new Probe();
        p.TextKommt("Alle zum Hangar 2", Textmeldungen.Gelb);
        Pruefe.Wahr(p.Anzeige.Sichtbar && p.Text == "text gelb (Alle zum Hangar 2)", "Text mit 8 s: " + p.Text);
        Pruefe.Gleich("Befehl", string.Join(",", p.Toene), "der kurze Ton wie bei Befehlen");
        Pruefe.Gleich(0.0, p.Textplatz.Rand.Top, "allein ganz oben, ohne Abstand");
        p.Weiter(Meldeanzeige.TextMs - 40);
        Pruefe.Gleich("text gelb (Alle zum Hangar 2)", p.Text, "kurz vor 8 s noch da");
        p.Weiter(80);
        Pruefe.Wahr(p.Text == "" && !p.Anzeige.Sichtbar && !p.Textplatz.Da, "nach 8 s fort");

        p.TextKommt("Pause bis 21 Uhr", Textmeldungen.Blau, 2, Textmeldungen.Bleibt);
        p.Weiter(30_000);
        Pruefe.Wahr(p.Text == "text blau (Pause bis 21 Uhr)" && p.Anzeige.Sichtbar, "bleibt stehen, auch nach 30 s: " + p.Text);
        Pruefe.Gleich(2, p.Toene.Count, "auch der bleibende klingt");
        p.TextKommt("Funkstille", Textmeldungen.Rot);
        Pruefe.Gleich("text rot (Funkstille)", p.Text, "ein neuer mit 8 s ersetzt den stehenden");
        p.Weiter(Meldeanzeige.TextMs + 40);
        Pruefe.Wahr(p.Text == "" && !p.Anzeige.Sichtbar, "danach ist nichts mehr da (der stehende war ersetzt)");

        p.Einstellungen.Ton = false;
        p.TextKommt("Sammeln am Tor", Textmeldungen.Gruen, 1, Textmeldungen.Bleibt);
        Pruefe.Wahr(p.Text == "text gruen (Sammeln am Tor)" && p.Toene.Count == 3, "Ton aus: kein Ton");
        p.Einstellungen.Ton = true;
        p.TextKommt("Achtung", Textmeldungen.Orange);
        p.Stand(s => s with { Text = null });
        p.Anzeige.Eingegangen(new Meldestand { Anlass = Meldungsvorgang.TextAufheben, Von = 2 });
        Pruefe.Wahr(p.Text == "" && !p.Anzeige.Sichtbar, "aufheben nimmt auch einen Text mit 8 s sofort weg");

        // Nach Welcome: die bleibende aus dem Stand, ohne Ton; ein Text mit 8 s im Stand zählt nicht (der Server schickt keinen).
        var toene = p.Toene.Count;
        p.Stand(s => s with { Text = Tm("Pause bis 21 Uhr", Textmeldungen.Blau, 1, Textmeldungen.Bleibt) });
        p.Anzeige.Eingegangen(new Meldestand { Anlass = Meldungsvorgang.Stand, Text = Tm("Pause bis 21 Uhr", Textmeldungen.Blau, 1, Textmeldungen.Bleibt) });
        Pruefe.Wahr(p.Text == "text blau (Pause bis 21 Uhr)" && p.Toene.Count == toene, "nach Welcome wieder da, ohne Ton");
        p.Stand(s => s with { Text = Tm("Funkstille") });
        p.Anzeige.Takt();
        Pruefe.Wahr(p.Text == "", "ein Text mit 8 s im Stand der Lage gilt nicht als stehend");

        // Unter dem Befehl, mit dem Abstand der Plätze; die Probe verdrängt ihn.
        p.TextKommt("Alle zum Hangar 2", dauer: Textmeldungen.Bleibt);
        p.Befehl("sammeln");
        Pruefe.Gleich("haupt sammeln\ntext weiss (Alle zum Hangar 2)", p.Text, "Befehl oben, Text darunter");
        Pruefe.Wahr(Math.Abs(p.Textplatz.Rand.Top - Meldebild.LueckeGrund) < 0.1, T($"Abstand zur Reihe wie zwischen den Plätzen ({p.Textplatz.Rand.Top})"));
        p.Anzeige.ProbeZeigen();
        Pruefe.Gleich("haupt sammeln (" + Meldeanzeige.ProbeZeile + ")", p.Text, "die Probe verdrängt den Text");
        p.Weiter(Meldeanzeige.ProbeMs + 40);
        Pruefe.Wahr(p.Text.EndsWith("text weiss (Alle zum Hangar 2)", StringComparison.Ordinal), "danach steht er wieder");
    }

    /// <summary>
    /// Größe, Umbruch und Farben der Textmeldung: Grundschrift 26 wie die Sprecheranzeige bei Grundgröße 180, mal 1, 2, 3, mit der Grundgröße des Funkers;
    /// ein kurzer Text in einer Zeile, 120 Zeichen in zwei etwa gleich langen; dreifach auf einem Full-HD-Monitor nie breiter als er (die Schrift wird
    /// kleiner, bis zwei Zeilen reichen), auch ein Wort ohne Lücke; die sechs Farben mit ihrem Ton (gegen den schwarzen Schatten mindestens 7:1), eine
    /// unbekannte Kennung zeigt Weiß; der Schatten wie die Sprecheranzeige.
    /// </summary>
    [Test]
    public static void Meldungen_Anzeige_Text_GroesseUmbruchFarben()
    {
        Pruefe.Gleich(120, Text120.Length, "der Probetext hat 120 Zeichen");
        Pruefe.Gleich(Sprecherbild.SchriftGrund, Meldebild.TextGrund, "Grundschrift wie die Sprecheranzeige bei 100 %");
        var p = new Probe();
        foreach (var (g, soll) in new (byte, double)[] { (1, 26), (2, 52), (3, 78) })
        {
            p.TextKommt("Los", groesse: g);
            Pruefe.Wahr(Math.Abs(p.Textplatz.Schrift - soll) < 0.01 && p.Textplatz.Zeilen == 1, T($"Faktor {g}: Schrift {soll} ({p.Textplatz.Schrift})"));
        }
        p.Einstellungen.Grundgroesse = 360;
        p.TextKommt("Los");
        Pruefe.Wahr(Math.Abs(p.Textplatz.Schrift - 52) < 0.01, "Grundgröße 360: normal 52");
        p.Einstellungen.Grundgroesse = 180;
        p.Anzeige.Takt();
        Pruefe.Wahr(Math.Abs(p.Textplatz.Schrift - 26) < 0.01 && p.Textplatz.Schatten is { Color: var sc, Opacity: 1 } s && sc == Colors.Black && Math.Abs(s.BlurRadius - (Sprecherbild.UnschaerfeGrund * 26 / Sprecherbild.SchriftGrund)) < 0.3,
            "schwarzer Schatten der Sprecheranzeige, zur Schrift passend");

        // Umbruch: kurz eine Zeile, 120 Zeichen zwei etwa gleich lange.
        p.TextKommt("Alle zum Hangar 2");
        Pruefe.Wahr(p.Textplatz.Zeilen == 1 && p.Textplatz.Breite < Textplatz.ZeileEm * 26, T($"kurz: eine Zeile ({p.Textplatz.Breite:0})"));
        p.TextKommt(Text120);
        var zwei = p.Textplatz.Breite;
        var ganz = new FormattedText(Text120, De, FlowDirection.LeftToRight, Textplatz.Schriftart, 26, Brushes.White, 1.0).WidthIncludingTrailingWhitespace;
        Pruefe.Wahr(p.Textplatz.Zeilen == 2 && zwei > ganz * 0.5 && zwei < ganz * 0.62, T($"120 Zeichen: zwei etwa gleich lange Zeilen ({zwei:0} von {ganz:0})"));
        var lang = Textplatz.Formen(Text120 + " " + Text120, 26, double.PositiveInfinity);
        Pruefe.Wahr(lang.Zeilen == 2 && lang.Breite > zwei, "auch länger und ohne Monitor bleibt es bei zwei Zeilen");

        // Nie breiter als der Monitor: dreifach auf Full HD (Platz 1872).
        p.Anzeige.Grenze = MeldeanzeigeFenster.Grenze(FullHd);
        p.TextKommt(Text120, groesse: 3);
        Pruefe.Wahr(p.Textplatz.Zeilen <= 2 && p.Textplatz.Breite + p.Textplatz.Rand.Right <= p.Anzeige.Grenze.Width + 0.5 && p.Textplatz.Schrift < 78 && p.Textplatz.Schrift > 40,
            T($"dreifach auf Full HD: {p.Textplatz.Zeilen} Zeilen, {p.Textplatz.Breite:0} breit, Schrift {p.Textplatz.Schrift:0.0}"));
        p.Anzeige.Grenze = new Size(600, 400);
        p.TextKommt(new string('x', 120), groesse: 3);
        Pruefe.Wahr(p.Textplatz.Zeilen <= 2 && p.Textplatz.Breite + p.Textplatz.Rand.Right <= 600.5,
            T($"ein Wort ohne Lücke auf 600: {p.Textplatz.Zeilen} Zeilen, {p.Textplatz.Breite:0} breit, Schrift {p.Textplatz.Schrift:0.0}"));
        p.Anzeige.Grenze = new Size(double.PositiveInfinity, double.PositiveInfinity);

        // Farben.
        Pruefe.Gleich("weiss|gelb|orange|rot|gruen|blau", string.Join("|", Textfarben.Alle.Select(f => f.Kennung)), "die Folge der Kennungen");
        Pruefe.Gleich("Weiß|Gelb|Orange|Rot|Grün|Blau", string.Join("|", Textfarben.Alle.Select(f => f.Name)), "die Namen");
        foreach (var f in Textfarben.Alle)
        {
            p.TextKommt("Farbe", f.Kennung);
            Pruefe.Wahr(p.Textplatz.Farbe == f.Kennung && p.Textplatz.Pinsel is SolidColorBrush { IsFrozen: true, Color: var c } && c == f.Farbe, f.Name + ": der Ton");
            Pruefe.Wahr(Kontrast.Verhaeltnis(f.Farbe, Colors.Black) >= 7, T($"{f.Name} gegen Schwarz {Kontrast.Verhaeltnis(f.Farbe, Colors.Black):0.0}:1"));
        }
        Pruefe.Wahr(Textfarben.Alle.Select(f => f.Farbe).Distinct().Count() == 6, "sechs verschiedene");
        p.TextKommt("Farbe", "lila");
        Pruefe.Wahr(p.Textplatz.Farbe == "weiss" && p.Textplatz.Pinsel is SolidColorBrush { Color: var w } && w == Colors.White, "unbekannt: Weiß");
    }

    private static byte[] Punkte(int breite, int hoehe, params (int X, int Y)[] sichtbar)
    {
        var b = new byte[breite * hoehe * 4];
        foreach (var (x, y) in sichtbar) b[(y * breite * 4) + (x * 4) + 3] = 200;
        return b;
    }

    /// <summary>
    /// Lage nach den Reglern für den ganzen Block: bei 0 % am linken oder oberen Rand (24 Einheiten mit der Skala), bei 100 % am rechten oder unteren, bei
    /// 50 % mittig (ab Werk 50 und 0, oben Mitte); ein zu großer Block steht am Rand. Die Grenze ist der Platz auf dem Monitor in Einheiten ohne den Rand.
    /// Monitor: der von Star Citizen vorn, sonst der zuletzt bekannte, sonst der Hauptmonitor; keine eigene Wahl.
    /// </summary>
    [Test]
    public static void Meldungen_Fenster_LageMittig_Grenze_Monitor()
    {
        (int, int) Ecke(int w, int s, Bildschirm b, int breite = 400, int hoehe = 200) => MeldeanzeigeFenster.Ecke(w, s, b, breite, hoehe);
        Pruefe.Gleich((50, 0), (MeldungenEinstellungen.WaagerechtAbWerk, MeldungenEinstellungen.SenkrechtAbWerk), "ab Werk oben Mitte");
        Pruefe.Gleich((24 + 736, 24), Ecke(50, 0, FullHd), "Full HD, 50 und 0: mittig oben");
        Pruefe.Gleich((24, 24), Ecke(0, 0, FullHd), "0 und 0: oben links");
        Pruefe.Gleich((1920 - 24 - 400, 1080 - 24 - 200), Ecke(100, 100, FullHd), "100 und 100: unten rechts, ganz im Monitor");
        Pruefe.Gleich((0, 0), Ecke(50, 50, FullHd, 3000, 2000), "größer als der Monitor: an seiner Kante");
        Pruefe.Gleich((20, 24), Ecke(50, 50, FullHd, 1900, 1050), "größer als der Platz, kleiner als der Monitor: so weit am Rand, dass er ganz drin ist");
        Pruefe.Gleich((1920 + 36 + 1044, 36), Ecke(50, 0, Wqhd), "WQHD bei 150 %: 36 Rand, mittig");
        Pruefe.Gleich(Ecke(0, 0, FullHd), Ecke(-9, -9, FullHd), "unter 0 zählt als 0");
        Pruefe.Gleich(new Size(1920 - 48, 1080 - 48), MeldeanzeigeFenster.Grenze(FullHd), "Grenze Full HD");
        Pruefe.Gleich(new Size((2560 / 1.5) - 48, (1440 / 1.5) - 48), MeldeanzeigeFenster.Grenze(Wqhd), "Grenze WQHD bei 150 % in Einheiten");

        Bildlauf.Ausfuehren(fenster =>
        {
            var anzeige = new Meldeanzeige(new FunkDienstAttrappe(FunkDienstAttrappe.Beispiel()), () => new MeldungenEinstellungen(), () => 0);
            Bildschirm? spiel = null;
            var f = new MeldeanzeigeFenster(anzeige, new FesteBildschirme([FullHd, Wqhd]), () => new Sprecherlage(50, 0, null), () => spiel);
            fenster.Add(f);
            Pruefe.Gleich((24 + 760, 24, FullHd), f.ZielFuer(352, 200).GetValueOrDefault(), "ohne Spiel: Hauptmonitor, oben Mitte");
            spiel = Wqhd;
            Pruefe.Gleich(Wqhd, f.ZielFuer(352, 200)!.Value.Monitor, "Star Citizen vorn auf dem WQHD");
            spiel = null;
            Pruefe.Gleich(Wqhd, f.ZielFuer(352, 200)!.Value.Monitor, "ein anderes Programm vorn: der zuletzt bekannte");
            var leer = new MeldeanzeigeFenster(anzeige, new FesteBildschirme([]));
            fenster.Add(leer);
            Pruefe.Wahr(leer.ZielFuer(10, 10) is null, "ohne Monitor kein Ziel");
            Pruefe.Wahr(f.WindowStyle == WindowStyle.None && f.AllowsTransparency && !f.ShowActivated && !f.ShowInTaskbar && !f.Focusable && !f.IsHitTestVisible
                && f.Topmost && f.SizeToContent == SizeToContent.WidthAndHeight && f.Owner is null && f.Background is SolidColorBrush { Color.A: 0 } && !f.IsVisible,
                "das Fenster wie die Sprecheranzeige: ohne Fläche, nie aktiviert, durchklickbar, ohne Owner, nicht gezeigt");
        });

        var code = File.ReadAllText(Quelle(@"src\Funk.Client\Fenster\MeldeanzeigeFenster.xaml.cs"));
        var app = File.ReadAllText(Quelle(@"src\Funk.Client\App.xaml.cs"));
        var ohne = Regex.Replace(code, @"//[^\n]*", "");
        foreach (var verboten in new[] { "Activate(", ".Focus(", "SetForegroundWindow", "Owner =", "OpenProcess" })
            Pruefe.Falsch(ohne.Contains(verboten, StringComparison.Ordinal), "Fenster ohne " + verboten);
        Pruefe.Wahr(Regex.IsMatch(ohne, @"SourceInitialized \+= \(_, _\) =>\s*\{[^}]*Meldezeilenlage\.Stil\("), "Stil beim SourceInitialized");
        foreach (Match m in Regex.Matches(ohne, @"\bSetWindowPos\(h,[^;]*;"))
            Pruefe.Wahr(m.Value.Contains("HwndTopmost", StringComparison.Ordinal) && m.Value.Contains("SwpNoactivate", StringComparison.Ordinal), "SetWindowPos mit TOPMOST und NOACTIVATE");
        Pruefe.Wahr(ohne.Contains("LageSetzen(nachOben: neu)", StringComparison.Ordinal) && ohne.Contains("Hide();", StringComparison.Ordinal), "beim Sichtbarwerden nach oben, Hide statt leer");
        var verdrahtung = app[app.IndexOf("new MeldeanzeigeFenster(", StringComparison.Ordinal)..];
        verdrahtung = verdrahtung[..verdrahtung.IndexOf("var haupt = HauptfensterBauen();", StringComparison.Ordinal)];
        Pruefe.Falsch(Regex.IsMatch(verdrahtung, @"\bOwner\b|\.Activate\(|\.Focus\("), "App.xaml.cs setzt keinen Owner und aktiviert nicht");
        Pruefe.Wahr(app.Contains("_meldeanzeigeFenster?.Close();", StringComparison.Ordinal), "beim Programmende zu");
    }

    private static string Quelle(string relativ)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new PruefungException("TacRelay.slnx nicht gefunden"), relativ);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Ton
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Der Signalton, selbst erzeugt: Befehl zwei Töne (660 und 880 Hz) mit Pause, Sekunde ein kurzer (1000 Hz); weich an und aus (die ersten und letzten
    /// Samples fast 0), die Spitze 6 dB unter der Sprache plus 3 dB eines Sinus. Im Mischer kommt er beim nächsten Block, mittig, unter der Funk-Lautstärke;
    /// ein neuer ersetzt einen laufenden. Die Maschine spielt nur mit laufender Wiedergabe, die Attrappe merkt jeden.
    /// </summary>
    [Test]
    public static void Meldungen_Signalton_ErzeugtGemischtGemerkt()
    {
        var befehl = new float[Signalton.Laenge(Signaltonart.Befehl)];
        Pruefe.Gleich(befehl.Length, Signalton.Erzeugen(Signaltonart.Befehl, befehl), "Länge des Befehls");
        Pruefe.Gleich((int)Math.Round((2 * Signalton.BefehlTonMs + Signalton.BefehlPauseMs) * Klangformat.SamplesJeMs), befehl.Length, "zwei Töne und die Pause");
        var spitze = befehl.Max(Math.Abs);
        var soll = Pegel.DbZuLinear(FunkKlickEinstellungen.SprachpegelDbfs + Signalton.PegelDb) * MathF.Sqrt(2);
        Pruefe.Wahr(Math.Abs(spitze - soll) < soll * 0.02, T($"Spitze {spitze:0.0000}, erwartet {soll:0.0000}"));
        Pruefe.Wahr(Math.Abs(befehl[0]) < 1e-6 && Math.Abs(befehl[^1]) < soll * 0.05, "weich an und aus");
        var pause = (int)Math.Round(Signalton.BefehlTonMs * Klangformat.SamplesJeMs) + 100;
        Pruefe.Gleich(0f, befehl[pause], "Stille in der Pause");
        var sekunde = new float[Signalton.Laenge(Signaltonart.Sekunde)];
        _ = Signalton.Erzeugen(Signaltonart.Sekunde, sekunde);
        Pruefe.Gleich((int)Math.Round(Signalton.SekundeMs * Klangformat.SamplesJeMs), sekunde.Length, "Sekunde: 60 ms");

        var mischer = new Mischer();
        var ausgabe = new float[Mischer.Blocklaenge * 2];
        mischer.Mischen(ausgabe, 2);
        Pruefe.Gleich(0f, ausgabe.Max(Math.Abs), "ohne Ton Stille");
        Pruefe.Wahr(mischer.Ton.Anfordern(Signaltonart.Sekunde), "angefordert");
        mischer.Mischen(ausgabe, 2);
        Pruefe.Wahr(ausgabe.Max(Math.Abs) > soll * 0.3 && mischer.Ton.Gespielt == 1, "im nächsten Block zu hören");
        Pruefe.Wahr(Math.Abs(ausgabe[200] - ausgabe[201]) < 1e-6, "mittig: links und rechts gleich");

        var maschine = new Funk.Client.Klang.KlangMaschinenAttrappe();
        Pruefe.Falsch(maschine.TonSpielen(Signaltonart.Befehl), "Attrappe ohne Wiedergabe: falsch");
        Pruefe.Gleich("Befehl", string.Join(",", maschine.Toene), "aber gemerkt");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Bereit per Taste und Knopf
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// "Bereit": ohne Abfrage kein Knopf, die Taste tut nichts. Fragt die Leitung (ein anderes Konto), steht "Bereit melden"; ein Klick oder die Taste
    /// meldet (einmal, zweimal ist wie einmal), danach "gemeldet". Wer selbst fragt, bekommt keinen Knopf. Eine neue Abfrage fragt neu. Die Taste steht
    /// unter Tasten > Erweitert bei den weiteren Tasten, ab Werk unbelegt, als Flanke, gemerkt unter "weitere" mit ihrem Namen.
    /// </summary>
    [Test]
    public static async Task Meldungen_Bereit_KnopfUndTaste()
    {
        using var t = new TastenTeile();
        var d = t.A.Dienst;
        var b = t.Bereit;
        Pruefe.Wahr(!b.Offen && !b.MeldenCommand.CanExecute(null), "ohne Abfrage kein Knopf");
        t.Quelle.Ausloesen(Tastenfunktion.Bereit);
        await t.Bis(() => true, "Taste ohne Abfrage");
        Pruefe.Falsch(d.Aufrufe.Any(x => x.StartsWith("Bereit", StringComparison.Ordinal)), "ohne Abfrage schickt die Taste nichts");

        d.LageSetzen(l => l with { Meldestand = new Meldestand { Anlass = Meldungsvorgang.BereitAbfrage, Abfrage = E("bereit_melden", von: 4, nummer: 7) } });
        b.Aktualisieren();
        Pruefe.Wahr(b.Offen && !b.Gemeldet && b.KnopfText == "Bereit melden" && b.MeldenCommand.CanExecute(null), "Nordlicht fragt: Knopf da");
        t.Quelle.Ausloesen(Tastenfunktion.Bereit);
        await t.Bis(() => b.Gemeldet, "die Taste meldet");
        Pruefe.Wahr(b.KnopfText == "gemeldet" && b.GemeldetNummer == 7 && !b.MeldenCommand.CanExecute(null), "danach gemeldet");
        Pruefe.Gleich(1, d.Aufrufe.Count(x => x == "Bereit 7"), "einmal geschickt");
        t.Quelle.Ausloesen(Tastenfunktion.Bereit);
        await b.Melden();
        await t.Bis(() => true, "die zweite Taste abgearbeitet");
        Pruefe.Gleich(1, d.Aufrufe.Count(x => x == "Bereit 7"), "zweimal ist wie einmal: nichts mehr geschickt");

        d.LageSetzen(l => l with { Meldestand = l.Meldestand! with { Abfrage = E("bereit_melden", von: 4, nummer: 8) } });
        b.Aktualisieren();
        Pruefe.Wahr(b.Offen && !b.Gemeldet, "eine neue Abfrage fragt neu");
        await b.MeldenCommand.ExecuteAsync(null);
        Pruefe.Wahr(b.Gemeldet && d.Aufrufe.Contains("Bereit 8"), "der Knopf meldet");

        d.LageSetzen(l => l with { Meldestand = l.Meldestand! with { Abfrage = E("bereit_melden", von: 1, nummer: 9) } });
        b.Aktualisieren();
        Pruefe.Falsch(b.Offen, "wer selbst fragt (Max), bekommt keinen Knopf");
        d.LageSetzen(l => l with { Meldestand = l.Meldestand! with { Abfrage = E("bereit_melden", von: 4, nummer: 10) } });
        d.NaechsterFehler = "Keine offene Bereit-Abfrage mit dieser Nummer.";
        b.Aktualisieren();
        await b.Melden();
        Pruefe.Wahr(!b.Gemeldet && b.Fehler == "Keine offene Bereit-Abfrage mit dieser Nummer.", "ein Fehler steht da, nicht gemeldet");
        d.LageSetzen(l => l with { Meldestand = null });
        b.Aktualisieren();
        Pruefe.Wahr(!b.Offen && b.Fehler is null, "Abfrage zu: kein Knopf, kein Fehler");

        // Die Taste unter Tasten > Erweitert.
        var vm = new TastenViewModel(t.A.Ablage, t.Quelle);
        var zeile = vm.Funktionen.Single(f => f.Funktion == Tastenfunktion.Bereit);
        Pruefe.Wahr(vm.WeitereTasten.Contains(zeile) && zeile.Belegungen.Count == 0 && zeile.Titel == "Bereit", "bei den weiteren Tasten, ab Werk unbelegt");
        Pruefe.Falsch(Tastenfunktionen.IstGehalten(Tastenfunktion.Bereit) || Tastenfunktionen.IstAlt(Tastenfunktion.Bereit), "Flanke, nicht in der Liste von 0.1.0");
        var f9 = new Tastenbelegung(EingabeArt.Tastatur, 0x78, null);
        t.A.Ablage.Aktuell.Tasten.Hinzufuegen(new Tastenzuordnung(Tastenfunktion.Bereit, f9));
        t.A.Ablage.Vormerken();
        t.A.Ablage.Sichern();
        var json = File.ReadAllText(t.A.Ablage.Pfad);
        Pruefe.Wahr(json.Contains("\"funktion\": \"Bereit\"", StringComparison.Ordinal) || json.Contains("\"funktion\":\"Bereit\"", StringComparison.Ordinal), "gemerkt unter weitere als \"Bereit\"");
        Pruefe.Wahr(new EinstellungsSpeicher(t.A.Ablage.Pfad).Laden().Tasten.Zuordnungen().Contains(new Tastenzuordnung(Tastenfunktion.Bereit, f9)), "wieder gelesen");
    }

    /// <summary>Tastenbetrieb wie in App.OnStartup mit der Bereitmeldung.</summary>
    private sealed class TastenTeile : IDisposable
    {
        public TastenTeile()
        {
            A = new Aufbau(takt: Takt);
            Funkfeld = A.Funkfeld();
            Zeile = new Meldezeile(() => A.Uhr);
            Ablage = new FunkfeldAblage(new FunkfeldFensterAttrappe(), new AblagesymbolAttrappe(), A.Ablage, Zeile, A.Dienst, () => A.Uhr);
            Phasen = new Phasensteuerung(A.Dienst, Zeile, () => A.Uhr, () => A.Jetzt);
            Teilnehmer = new Teilnehmermeldungen(A.Dienst, Zeile, Takt);
            Sprechen = new Sprechsteuerung(A.Maschine, A.Bruecke, () => new MeldeEntscheid(2, "Kommando", null));
            Bereit = new Bereitmeldung(A.Dienst);
            Betrieb = new Tastenbetrieb(Quelle, Sprechen, Takt, Funkfeld, Ablage, Phasen, Teilnehmer, bereit: Bereit);
            Takt.Abarbeiten();
        }

        public WarteschlangenTakt Takt { get; } = new();
        public Aufbau A { get; }
        public TastenQuelleAttrappe Quelle { get; } = new(EingabeArt.Tastatur);
        public FunkfeldViewModel Funkfeld { get; }
        public Meldezeile Zeile { get; }
        public FunkfeldAblage Ablage { get; }
        public Phasensteuerung Phasen { get; }
        public Teilnehmermeldungen Teilnehmer { get; }
        public Sprechsteuerung Sprechen { get; }
        public Bereitmeldung Bereit { get; }
        public Tastenbetrieb Betrieb { get; }

        public async Task Bis(Func<bool> fertig, string was)
        {
            for (var i = 0; i < 200; i++)
            {
                Takt.Abarbeiten();
                if (fertig() && Takt.Anzahl == 0) break;
                await Task.Delay(5);
            }
            Takt.Abarbeiten();
            Pruefe.Wahr(fertig(), was);
        }

        public void Dispose()
        {
            Betrieb.Dispose();
            Teilnehmer.Dispose();
            Funkfeld.Dispose();
            A.Dispose();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Feld der Leitung
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Die Attrappe mit Haken (Max ist Führung) und fester Uhr; das Feld mit eigener Uhr für die Sperre.</summary>
    private static (FunkDienstAttrappe Dienst, MeldungenViewModel Feld, Func<long> Uhr, Action<long> Weiter) Leitung(MeldungenEinstellungen? e = null)
    {
        var d = new FunkDienstAttrappe(FunkDienstAttrappe.Beispiel());
        var jetzt = DateTimeOffset.FromUnixTimeMilliseconds(T0);
        d.Uhr = () => jetzt;
        d.LageSetzen(l => l with { Konto = l.Konto! with { MeldungenSenden = true }, ServerMeldungen = true });
        long uhr = 100_000;
        var einst = e ?? new MeldungenEinstellungen();
        var feld = new MeldungenViewModel(d, () => einst, () => uhr) { Ortszeit = Ort };
        void Weiter(long ms)
        {
            uhr += ms;
            jetzt = jetzt.AddMilliseconds(ms);
            feld.Takt();
        }
        return (d, feld, () => uhr, Weiter);
    }

    /// <summary>
    /// Wer das Feld sieht: Haken und Führung an einem Server ab 0.12.0; nicht ohne Haken, nicht als Funker, nicht als Admin "heute als Funker", nicht an
    /// einem älteren Server, nicht getrennt. Ein Klick sendet mit Größe und Zeile (Zustände klein, Aufheben ohne Zeile); danach ist das Feld 2 s ab der
    /// Antwort gesperrt und sagt, wie lange noch; dann geht es wieder. Kommt doch GRENZE, steht der Satz da, und die Sperre beginnt neu. Die Zeile ist hart
    /// auf 60 Zeichen begrenzt. Was gerade läuft, ist an der Kachel markiert; "n von m bereit" mit den fehlenden Namen (wer fragt, zählt nicht mit).
    /// </summary>
    [Test]
    public static async Task Meldungen_Leitung_SichtbarSendenSperreZeileBereit()
    {
        var (d, feld, uhr, weiter) = Leitung();
        Pruefe.Wahr(MeldungenViewModel.Gilt(d.Lage) && feld.Sichtbar, "Führung mit Haken an 0.12.0");
        foreach (var (was, aendern) in new (string, Func<FunkLage, FunkLage>)[]
                 {
                     ("ohne Haken", l => l with { Konto = l.Konto! with { MeldungenSenden = false } }),
                     ("Funker mit Haken", l => l with { Konto = l.Konto! with { Stufe = Stufe.Funker } }),
                     ("Admin heute als Funker", l => l with { Konto = l.Konto! with { Stufe = Stufe.Admin, AlsFunkerBis = DateTimeOffset.Now.AddHours(2) } }),
                     ("Server 0.11.9", l => l with { ServerMeldungen = false }),
                     ("getrennt", l => l with { Status = Verbindungsstatus.Getrennt }),
                 })
        {
            Pruefe.Falsch(MeldungenViewModel.Gilt(aendern(d.Lage)), was + ": kein Feld");
        }
        Pruefe.Gleich("an alle, die online sind (6)  -  ein Klick sendet  -  du bist ernannt", feld.KopfText, "der Kopf mit der Zahl online");
        Pruefe.Gleich("Sammeln|Sammelpunkt|Stopp|Rückzug|Start frei|Med. Hilfe|Los|Stopfen", string.Join("|", feld.Befehle.Select(k => k.Kurzname)), "acht Befehle");
        Pruefe.Gleich("Einsatz n. gest.|Event n. gest.|Pos. halten|Funkdisziplin|Pause", string.Join("|", feld.Zustaende.Select(k => k.Kurzname)), "fünf Zustände");
        Pruefe.Gleich("rot|gelb|grün", string.Join("|", feld.Ampeln.Select(k => k.Kurzname)), "Ampel");
        Pruefe.Wahr(feld.Befehle.Concat(feld.Zustaende).Concat(feld.Ampeln).Concat(feld.Countdowns).Append(feld.Bereit).All(k => k.Bild is BitmapSource && k.Hilfe.Length > 40),
            "jede Kachel mit Bild und Hovertext");

        feld.GroesseDoppelt = true;
        feld.Zeile = "Hangar 2";
        await feld.SendenCommand.ExecuteAsync(feld.Befehle.Single(k => k.Kennung == "sammelpunkt_x"));
        Pruefe.Gleich("Meldung Befehl sammelpunkt_x x2 \"Hangar 2\"", d.Aufrufe[^1], "Befehl doppelt mit Zeile");
        Pruefe.Wahr(feld.Gesperrt && feld.Wartetext == "Nächste Meldung in 2,0 s" && !feld.SendenCommand.CanExecute(feld.Befehle[0]), "gesperrt ab der Antwort: " + feld.Wartetext);
        Pruefe.Wahr(feld.SendenCommand.CanExecute(feld.Countdowns[1]), "die Form des Countdowns wählt man trotzdem");
        weiter(1500);
        Pruefe.Gleich("Nächste Meldung in 0,5 s", feld.Wartetext, "die Wartezeit läuft sichtbar ab");
        weiter(500);
        Pruefe.Wahr(!feld.Gesperrt && feld.Wartetext is null && feld.SendenCommand.CanExecute(feld.Befehle[0]), "nach 2 s frei");
        Pruefe.Wahr(feld.GroesseDoppelt && feld.Zeile == "Hangar 2", "Größe und Zeile bleiben stehen");

        await feld.SendenCommand.ExecuteAsync(feld.Zustaende.Single(k => k.Kennung == "pause"));
        Pruefe.Gleich("Meldung Zustand pause \"Hangar 2\"", d.Aufrufe[^1], "Zustand immer klein (ohne Größe)");
        feld.Aktualisieren();
        Pruefe.Wahr(feld.Zustaende.Single(k => k.Kennung == "pause").Laeuft && feld.Zustaende.Count(k => k.Laeuft) == 1, "die laufende Kachel ist markiert");
        weiter(2000);
        await feld.ZustandAufhebenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Meldung ZustandAufheben", d.Aufrufe[^1], "aufheben ohne Zeile");
        Pruefe.Wahr(feld.Zustaende.All(k => !k.Laeuft), "nichts mehr markiert");

        // GRENZE trotz Sperre (anderes Fenster, Uhr): der Satz steht da, die Sperre beginnt neu.
        weiter(2000);
        d.NaechsterFehler = FunkClientDienst.Meldefehler(Fehlercode.Grenze, null);
        await feld.SendenCommand.ExecuteAsync(feld.Ampeln[0]);
        Pruefe.Wahr(feld.Fehler == "Höchstens eine Meldung in 2 Sekunden, kurz warten." && feld.Gesperrt, "GRENZE: Satz und neue Sperre");
        weiter(2000);
        await feld.SendenCommand.ExecuteAsync(feld.Ampeln[1]);
        Pruefe.Wahr(feld.Fehler is null && d.Aufrufe[^1] == "Meldung Ampel ampel_gelb_bereithalten x2 \"Hangar 2\"", "danach geht es, der Fehler ist fort");
        weiter(2000);
        await feld.AmpelAusCommand.ExecuteAsync(null);
        Pruefe.Gleich("Meldung AmpelAus", d.Aufrufe[^1], "Ampel aus");

        // Zeile hart 60.
        feld.Zeile = new string('x', 75);
        Pruefe.Wahr(feld.Zeile.Length == Grenzen.MeldungZeileMax && feld.ZeileRest == "noch 0 Zeichen", "hart auf 60 Zeichen");
        feld.Zeile = "";
        Pruefe.Gleich("noch 60 Zeichen", feld.ZeileRest, "leer");

        // Bereit-Abfrage: Max fragt und zählt nicht mit.
        weiter(2000);
        feld.GroesseNormal = true;
        await feld.SendenCommand.ExecuteAsync(feld.Bereit);
        Pruefe.Gleich("Meldung BereitAbfrage bereit_melden", d.Aufrufe[^1], "Bereit-Abfrage normal");
        feld.Aktualisieren();
        Pruefe.Wahr(feld.BereitOffen && feld.BereitZahl == "0 von 5 bereit" && feld.BereitFehlen == "es fehlen: Falke, Kobold, Nordlicht, Kranich, Sturmvogel",
            "0 von 5, die Namen: " + feld.BereitFehlen);
        Pruefe.Gleich(null, d.BereitVon(3, d.Lage.Meldestand!.Abfrage!.Nummer!.Value), "Kobold bereit");
        feld.Aktualisieren();
        Pruefe.Wahr(feld.BereitZahl == "1 von 5 bereit" && !feld.BereitFehlen!.Contains("Kobold", StringComparison.Ordinal), "1 von 5");
        weiter(2000);
        await feld.BereitSchliessenCommand.ExecuteAsync(null);
        feld.Aktualisieren();
        Pruefe.Wahr(!feld.BereitOffen && feld.BereitZahl == "zu: 1 von 5 bereit" && !feld.BereitSchliessenCommand.CanExecute(null), "geschlossen: das Ergebnis bleibt");
        _ = uhr;
    }

    /// <summary>
    /// Countdown aus Uhrzeit oder Dauer: "21:30" die nächste in Ortszeit (heute noch, sonst morgen), "21.30", "5 min", "90 s", "1 h 30 min", "1,5 min",
    /// eine Zahl allein Minuten; Unsinn, 0, mehr als 24 Stunden, 25:00 ergeben nichts. Der Satz darunter sagt, was es ergibt. Start sendet Form und Ziel
    /// in Serverzeit mit Größe und Zeile; abbrechen nur, solange einer läuft.
    /// </summary>
    [Test]
    public static async Task Meldungen_Leitung_Countdown_UhrzeitOderDauer()
    {
        long? Z(string e) => MeldungenViewModel.Zielzeit(e, T0, Ort);
        Pruefe.Gleich((long?)T0 + 292_000, Z("21:30"), "21:30 heute (jetzt 21:25:08)");
        Pruefe.Gleich((long?)T0 + 292_000, Z("21.30 Uhr"), "21.30 Uhr");
        Pruefe.Gleich((long?)T0 + 292_000 - 3_600_000 + 86_400_000, Z("20:30"), "20:30 ist vorbei: morgen");
        Pruefe.Gleich((long?)T0 + 300_000, Z("5 min"), "5 min");
        Pruefe.Gleich((long?)T0 + 90_000, Z("90 s"), "90 s");
        Pruefe.Gleich((long?)T0 + 5_400_000, Z("1 h 30 min"), "1 h 30 min");
        Pruefe.Gleich((long?)T0 + 90_000, Z("1,5 min"), "1,5 min");
        Pruefe.Gleich((long?)T0 + 600_000, Z("10"), "eine Zahl allein: Minuten");
        Pruefe.Gleich((long?)T0 + 150_000, Z("2 min 30 s"), "2 min 30 s");
        foreach (var unsinn in new[] { "", "abc", "5 äpfel", "0", "25:00", "21:61", "25 h", "-5" })
            Pruefe.Gleich((long?)null, Z(unsinn), "nichts: \"" + unsinn + "\"");
        Pruefe.Gleich("28 min|1 h 5 min|45 s", string.Join("|", MeldungenViewModel.Dauer(27 * 60_000 + 30_000), MeldungenViewModel.Dauer(65 * 60_000),
            MeldungenViewModel.Dauer(44_200)), "Dauer in Worten");

        var (d, feld, _, weiter) = Leitung();
        Pruefe.Wahr(feld.CountdownForm == "Startzeit" && feld.Countdowns[0].Gewaehlt && feld.CountdownSatz == MeldungenViewModel.CountdownBeispiel && !feld.CountdownStartenCommand.CanExecute(null),
            "ab Werk Start um, leer: das Beispiel, kein Start");
        feld.CountdownEingabe = "21:30";
        Pruefe.Gleich("Start um 21:30, in 5 min", feld.CountdownSatz, "der Satz sagt, was es ergibt");
        feld.CountdownEingabe = "quatsch";
        Pruefe.Wahr(feld.CountdownSatz.StartsWith("Nicht erkannt", StringComparison.Ordinal), feld.CountdownSatz);
        await feld.CountdownStartenCommand.ExecuteAsync(null);
        Pruefe.Wahr(feld.Fehler!.StartsWith("Countdown:", StringComparison.Ordinal) && !d.Aufrufe.Any(a => a.StartsWith("Meldung Countdown", StringComparison.Ordinal)), "Unsinn geht nicht hinaus");
        await feld.SendenCommand.ExecuteAsync(feld.Countdowns[1]);
        Pruefe.Wahr(feld.CountdownForm == "Restzeit" && feld.Countdowns[1].Gewaehlt && !feld.Countdowns[0].Gewaehlt && !d.Aufrufe.Any(a => a.StartsWith("Meldung Countdown", StringComparison.Ordinal)),
            "die Kachel wählt die Form, sie sendet nicht");
        feld.CountdownEingabe = "5 min";
        feld.GroesseDreifach = true;
        Pruefe.Falsch(feld.CountdownAbbrechenCommand.CanExecute(null), "abbrechen erst, wenn einer läuft");
        await feld.CountdownStartenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Meldung Countdown x3", d.Aufrufe[^1], "Countdown dreifach");
        Pruefe.Wahr(d.Lage.Meldestand!.Countdown is { Motiv: "countdown_restzeit", Form: "Restzeit", Groesse: 3 } c && c.Ziel == T0 + 300_000, "Form und Ziel in Serverzeit");
        feld.Aktualisieren();
        Pruefe.Wahr(feld.CountdownLaeuft && feld.Countdowns[1].Laeuft, "läuft, markiert");
        weiter(2000);
        Pruefe.Wahr(feld.CountdownAbbrechenCommand.CanExecute(null), "abbrechen geht");
        await feld.CountdownAbbrechenCommand.ExecuteAsync(null);
        feld.Aktualisieren();
        Pruefe.Wahr(d.Aufrufe[^1] == "Meldung CountdownAbbrechen" && !feld.CountdownLaeuft, "abgebrochen");
    }

    /// <summary>
    /// Text im Feld der Leitung (M24): Eingabe hart auf 120 Zeichen (ein Zeichenpaar wird nicht zerschnitten) mit Zähler; sechs Farbknöpfe, Weiß ab Werk,
    /// genau einer gewählt; Dauer "8 s" ab Werk oder "bleibt stehen"; Senden erst mit Text, mit Farbe, Dauer und der Größe aus der Größenwahl, ohne die
    /// Zeile der Grafiken; danach 2 s gesperrt wie jede Meldung; aufheben. Die Wahl bleibt stehen.
    /// </summary>
    [Test]
    public static async Task Meldungen_Leitung_Text_FeldSendenAufheben()
    {
        var (d, feld, _, weiter) = Leitung();
        Pruefe.Wahr(feld.TextEingabe == "" && feld.TextRest == "noch 120 Zeichen" && MeldungenViewModel.TextMax == 120 && !feld.TextSendenCommand.CanExecute(null),
            "leer: noch 120 Zeichen, Senden geht nicht");
        feld.TextEingabe = new string('x', 150);
        Pruefe.Wahr(feld.TextEingabe.Length == 120 && feld.TextRest == "noch 0 Zeichen", "hart auf 120");
        feld.TextEingabe = new string('x', 119) + "🚀";
        Pruefe.Gleich(119, feld.TextEingabe.Length, "eine Rakete an der Grenze wird nicht halbiert");
        feld.TextEingabe = "   ";
        Pruefe.Falsch(feld.TextSendenCommand.CanExecute(null), "nur Leerraum: Senden geht nicht");

        Pruefe.Gleich("Weiß|Gelb|Orange|Rot|Grün|Blau", string.Join("|", feld.Farbwahl.Select(f => f.Name)), "sechs Farben");
        Pruefe.Wahr(feld.TextFarbe == "weiss" && feld.Farbwahl.Count(f => f.Gewaehlt) == 1 && feld.Farbwahl[0].Gewaehlt, "Weiß ab Werk");
        Pruefe.Wahr(feld.Farbwahl.All(f => f.Hilfe.Length > 40 && f.Ansage == "Textfarbe " + f.Name && f.Pinsel is SolidColorBrush), "Hovertext, Name und Fläche je Farbe");
        var gemeldet = new List<string?>();
        feld.Farbwahl[1].PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);
        feld.Farbwahl[1].Gewaehlt = true;
        Pruefe.Wahr(feld.TextFarbe == "gelb" && feld.Farbwahl.Count(f => f.Gewaehlt) == 1 && feld.Farbwahl[1].Gewaehlt && gemeldet.Contains("Gewaehlt"), "Gelb gewählt, nur einer");
        Pruefe.Wahr(feld.TextDauerKurz && !feld.TextDauerBleibt && feld.TextDauer == "8s", "8 s ab Werk");
        feld.TextDauerBleibt = true;
        Pruefe.Wahr(feld.TextDauer == "bleibt" && !feld.TextDauerKurz, "bleibt stehen");

        feld.GroesseDoppelt = true;
        feld.Zeile = "Hangar 2";
        feld.TextEingabe = "  Alle zum Hangar 2  ";
        Pruefe.Wahr(feld.TextSendenCommand.CanExecute(null), "mit Text: Senden");
        await feld.TextSendenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Meldung Text x2 gelb bleibt \"Alle zum Hangar 2\"", d.Aufrufe[^1], "Text doppelt, gelb, bleibt, getrimmt, ohne die Zeile");
        Pruefe.Wahr(d.Lage.Meldestand?.Text is { Text: "Alle zum Hangar 2", Farbe: "gelb", Groesse: 2, Dauer: "bleibt" }, "beim Server angekommen (Attrappe)");
        Pruefe.Wahr(feld.Gesperrt && !feld.TextSendenCommand.CanExecute(null) && !feld.TextAufhebenCommand.CanExecute(null) && feld.Wartetext == "Nächste Meldung in 2,0 s",
            "danach 2 s gesperrt wie jede Meldung");
        Pruefe.Wahr(feld.TextEingabe == "  Alle zum Hangar 2  " && feld.TextFarbe == "gelb" && feld.TextDauerBleibt, "Text und Wahl bleiben stehen");
        weiter(2000);
        await feld.TextAufhebenCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe[^1] == "Meldung TextAufheben" && d.Lage.Meldestand is { Text: null }, "aufgehoben");
        weiter(2000);
        feld.GroesseNormal = true;
        feld.TextDauerKurz = true;
        feld.Farbwahl[3].Gewaehlt = true;
        await feld.TextSendenCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe[^1] == "Meldung Text rot 8s \"Alle zum Hangar 2\"" && feld.Zuletzt == "Text rot 8s: Alle zum Hangar 2", "normal (ohne Größe), rot, 8 s: " + d.Aufrufe[^1]);
    }

    /// <summary>
    /// Die Seite Funkfeld-Zuteilung (Strg+5): Die Führung mit Haken ohne Auftrag bekommt sie mit dem Feld allein (das Brett ist nicht da); ohne Haken gibt es
    /// sie für sie nicht. Mit Auftrag und Haken beides. Das Feld hängt am Brett, das Hauptmodell gleicht es nach jeder Lage ab.
    /// </summary>
    [Test]
    public static void Meldungen_Seite_FuehrungOhneAuftrag_NurDasFeld()
    {
        using var a = new Aufbau();
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        Pruefe.Falsch(h.Bereiche.Any(b => b.Kennung == HauptViewModel.RaidKennung), "Führung ohne Haken und ohne Auftrag: keine Seite");
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { MeldungenSenden = true }, ServerMeldungen = true });
        Pruefe.Wahr(h.Bereiche.Any(b => b.Kennung == HauptViewModel.RaidKennung), "mit Haken: die Seite");
        Pruefe.Wahr(ReferenceEquals(h.Brett.Meldungen, h.Meldungen) && h.Meldungen.Sichtbar && !h.Brett.Sichtbar, "nur das Feld, kein Brett");
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { MeldungenSenden = false } });
        Pruefe.Falsch(h.Bereiche.Any(b => b.Kennung == HauptViewModel.RaidKennung) || h.Meldungen.Sichtbar, "Haken weg: Seite weg");
        funkfeld.Dispose();
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Einstellungen und Verwaltung
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Der Abschnitt Meldungen nach Sprecheranzeige: ab Werk 50 und 0 (oben Mitte), Grundgröße 180, Schlicht, Handschuh, Ton an; kein Schalter zum ganz
    /// Abschalten. Jeder Schritt an der Lage merkt sie und zeigt die Probe (beim Stopp-Bild der Stopp), der Ton nicht. Regler runden auf ihre Schritte.
    /// Gespeichert als Zahlen und Schalter, nachsichtig gelesen. Erweitert zählt die sechs Werte (mit der Lage) und stellt sie zurück.
    /// </summary>
    [Test]
    public static void Meldungen_Einstellungen_Modell_Ablage_Erweitert()
    {
        using var a = new Aufbau();
        var s = new MeldungenEinstellungViewModel(a.Ablage);
        var proben = new List<string?>();
        s.ProbeVerlangt += m => proben.Add(m);
        Pruefe.Wahr(s.Waagerecht == 50 && s.Senkrecht == 0 && s.Grundgroesse == 180 && s.AusfuehrungSchlicht && s.StoppHandschuh && s.Ton, "ab Werk");
        Pruefe.Gleich(new Sprecherlage(50, 0, null), s.Wahl, "oben Mitte, Monitor von Star Citizen");
        for (var w = 51; w <= 60; w++) s.Waagerecht = w;
        Pruefe.Wahr(s.Waagerecht == 60 && a.Ablage.Aktuell.Meldungen.Waagerecht == 60 && proben.Count == 10 && s.WaagerechtText == "60 %", "zehn Schritte, zehn Proben");
        s.Senkrecht = 12.6;
        s.Grundgroesse = 247;
        s.Ausfuehrung = 9;
        s.StoppXeno = true;
        Pruefe.Wahr(s.Senkrecht == 13 && s.Grundgroesse == 250 && s.AusfuehrungSchrift && s.StoppBild == 1 && proben[^1] == Meldungskatalog.Stopp && proben.Count == 14,
            "gerundet, begrenzt, beim Stopp-Bild der Stopp als Probe");
        s.Ton = false;
        Pruefe.Gleich(14, proben.Count, "der Ton zeigt keine Probe");
        s.Grundgroesse = 20;
        Pruefe.Gleich(100.0, s.Grundgroesse, "darunter 100");
        s.Grundgroesse = 250;

        a.Ablage.Sichern();
        var abschnitt = Regex.Match(File.ReadAllText(a.Ablage.Pfad), "\"meldungen\":\\s*\\{[^}]*\\}").Value;
        Pruefe.Wahr(Regex.IsMatch(abschnitt, "\"waagerecht\":\\s*60") && Regex.IsMatch(abschnitt, "\"grundgroesse\":\\s*250") && Regex.IsMatch(abschnitt, "\"ausfuehrung\":\\s*2")
            && Regex.IsMatch(abschnitt, "\"stoppBild\":\\s*1") && Regex.IsMatch(abschnitt, "\"ton\":\\s*false"), "als Zahlen und Schalter gespeichert: " + abschnitt);
        Pruefe.Falsch(Regex.IsMatch(abschnitt, ":\\s*\"[A-Za-z]"), "keine Namen im Abschnitt");
        foreach (var (json, erwartet, was) in new (string, string, string)[]
                 {
                     ("{}", "50 0 180 0 0 True", "ohne Abschnitt: ab Werk"),
                     ("{\"meldungen\":{\"waagerecht\":101,\"senkrecht\":-1,\"grundgroesse\":5000,\"ausfuehrung\":7,\"stoppBild\":\"x\",\"ton\":\"quatsch\"}}", "50 0 180 0 0 True", "Unsinn: ab Werk"),
                     ("{\"meldungen\":{\"waagerecht\":\"0\",\"senkrecht\":100,\"grundgroesse\":400,\"ausfuehrung\":1,\"stoppBild\":1,\"ton\":false}}", "0 100 400 1 1 False", "Grenzen"),
                 })
        {
            var datei = a.Ordner.Datei("m-" + Guid.NewGuid().ToString("N")[..6] + ".json");
            File.WriteAllText(datei, json);
            var speicher = new EinstellungsSpeicher(datei);
            var e = speicher.Laden().Meldungen;
            Pruefe.Gleich(erwartet, string.Join(" ", e.Waagerecht, e.Senkrecht, e.Grundgroesse, e.Ausfuehrung, e.StoppBild, e.Ton), was);
            Pruefe.Gleich(null, speicher.LetzterFehler, was + ": die Datei ist lesbar");
        }

        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        Pruefe.Gleich("Ich|Tasten|Empfangen|Senden|Funkfeld und Darstellung|Sprecheranzeige|Meldungen|Fehlersuche", string.Join("|", h.Einstellungen.Abschnitte.Select(x => x.Name)),
            "der Abschnitt nach Sprecheranzeige");
        var k = h.Einstellungen.AbschnittMeldungen!;
        Pruefe.Wahr(k.Kennung == Abschnittskennung.Meldungen && k.KastenText == MeldungenEinstellungViewModel.AutomatikText, "Kennung und Kasten");
        Pruefe.Gleich("Waagerecht 60 %|Senkrecht 13 %|Grundgröße 250|Ausführung Nur Schrift|Stopp: Xeno|Ton aus", string.Join("|", k.Feinheiten.SelbstGestellt),
            "Erweitert zählt die sechs Werte");
        k.Feinheiten.ZurueckCommand.Execute(null);
        var m = h.Einstellungen.Meldungen;
        Pruefe.Wahr(k.Feinheiten.SelbstGestellt.Count == 0 && m.Waagerecht == 50 && m.Senkrecht == 0 && m.Grundgroesse == 180 && m.AusfuehrungSchlicht && m.StoppHandschuh && m.Ton,
            "zurück auf Werkseinstellung");
        funkfeld.Dispose();
    }

    /// <summary>
    /// Der Haken "darf Meldungen senden" auf der Mitgliederkarte (M15-M18): an jedem Konto, nicht an einem offenen Antrag oder einer Vormerkung, nicht an
    /// einem Server vor 0.12.0. Gesetzt wird mit Speichern (zuerst der Haken, scheitert er, geht nichts weiter); ein unveränderter Haken schickt nichts.
    /// </summary>
    [Test]
    public static async Task Meldungen_Verwaltung_HakenAufDerKarte()
    {
        var d = new VerwaltungsdienstAttrappe();
        using var vm = new Funk.Verwaltung.Modelle.VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var m = vm.Mitglieder;
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 5));
        var k = m.Karte!;
        Pruefe.Wahr(k.ZeigtMeldungen && !k.MeldungenSenden, "Kranich (Führung): Haken da, aus");
        await k.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Falsch(d.Aufrufe.Any(x => x.StartsWith("Meldungen", StringComparison.Ordinal)), "unverändert: nichts geschickt");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 5));
        m.Karte!.MeldungenSenden = true;
        await m.Karte.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe.Contains("Meldungen 5 an") && d.Mitglieder.Single(x => x.KontoId == 5).MeldungenSenden == true && m.Karte is null, "gesetzt mit Speichern");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 5));
        Pruefe.Wahr(m.Karte!.MeldungenSenden, "die Karte zeigt den Haken");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 21));
        Pruefe.Falsch(m.Karte!.ZeigtMeldungen, "offener Antrag: kein Haken");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.VormerkungId == 7));
        Pruefe.Falsch(m.Karte!.ZeigtMeldungen, "Vormerkung: kein Haken");
        d.LageSetzen(l => l with { ServerFassung = "0.11.9" });
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 3));
        Pruefe.Falsch(m.Karte!.ZeigtMeldungen, "Server 0.11.9: kein Haken");
        Pruefe.Wahr(Funk.Verwaltung.Modelle.MitgliedKarte.MeldungenHilfe.Contains("Führung, Admin oder Einsatzleitung", StringComparison.Ordinal)
            && Funk.Verwaltung.Modelle.MitgliedKarte.MeldungenSatz == "Wirkt nur bei Führung, Admin oder Einsatzleitung mit Auftrag.", "Hilfetext und Satz");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Bildlauf
    // ------------------------------------------------------------------------------------------------------------------------------

    private static T Teil<T>(FrameworkElement wurzel, string name) where T : FrameworkElement =>
        Bildlauf.Nachfahren<T>(wurzel).FirstOrDefault(e => e.Name == name) ?? throw new PruefungException(name + " fehlt");

    private static CroppedBitmap Ausschnitt(FrameworkElement wurzel, BitmapSource ganz, FrameworkElement teil, double rand = 10)
    {
        var r = teil.TransformToAncestor(wurzel).TransformBounds(new Rect(teil.RenderSize));
        var links = (int)Math.Max(0, Math.Floor(r.Left - rand));
        var oben = (int)Math.Max(0, Math.Floor(r.Top - rand));
        return new CroppedBitmap(ganz, new Int32Rect(links, oben, (int)Math.Min(ganz.PixelWidth - links, Math.Ceiling(r.Width + (2 * rand))),
            (int)Math.Min(ganz.PixelHeight - oben, Math.Ceiling(r.Height + (2 * rand)))));
    }

    /// <summary>
    /// Das Feld "Meldungen" auf der Funkfeld-Zuteilung im Bildlauf (kein Fenster auf dem Bildschirm), unter dem Brett der Einsatzleitung (Raidabend, Max mit
    /// Auftrag und Haken): Kopf mit der Zahl online und der Größe (doppelt gewählt), Befehle, Zustände (Position halten läuft) mit aufheben, Ampel (gelb
    /// läuft) mit aus, Countdown (Start um läuft, Eingabe 21:30 mit Satz, Start, abbrechen), Bereit-Abfrage (3 von 5, wer fehlt, schließen), die Zeile, der
    /// Text (Eingabe mit Zähler, sechs Farbknöpfe, Gelb gewählt, bleibt stehen, Senden, aufheben), der Satz für die Funker. Jede Kachel mit Bild, Name und Hovertext; nichts abgeschnitten, Kontrast, Zugriffstasten weiter eindeutig. Ein Klick auf eine
    /// Kachel sendet. Mit FUNK_BILDER das Bild vorschau-0120-meldungen-leitung.
    /// </summary>
    [Test]
    public static void Meldungen_Bildlauf_Leitung_Bild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            var satz = Farbvorschlaege.Von(Farbvorschlaege.Voreinstellung);
            var d = FunkDienstAttrappe.MitRaidabend();
            d.LageSetzen(BrettBaumTests.MitKanalBruecke);
            var abend = FunkDienstAttrappe.Abend.ToUnixTimeMilliseconds();
            var max = d.Lage.Konto!.Id;
            var fehlen = d.Lage.Funker.Where(f => f.Online && f.KontoId != max).Select(f => f.KontoId).Order().ToList();
            d.LageSetzen(l => l with
            {
                Konto = l.Konto! with { MeldungenSenden = true },
                ServerMeldungen = true,
                Meldestand = new Meldestand
                {
                    Anlass = Meldungsvorgang.Stand,
                    Zustand = E("position_halten", von: max),
                    Ampel = E("ampel_gelb_bereithalten", 2, von: max),
                    Countdown = E("countdown_startzeit", 2, von: max, ziel: abend + 600_000, form: "Startzeit"),
                    Abfrage = E("bereit_melden", von: max, ziel: abend + 120_000, nummer: 3),
                },
                BereitStand = new BereitStand { Nummer = 3, Offen = true, Bereit = 3, Online = 5, Fehlen = [.. fehlen.Take(2)], Von = max, Bis = abend + 120_000 },
            });
            using var a = new Aufbau(dienst: d);
            a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
            a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            h.Brett.KartenAbwartenAsync().GetAwaiter().GetResult();
            h.BereichWaehlenCommand.Execute("5");
            var m = h.Meldungen;
            m.GroesseDoppelt = true;
            m.Zeile = "Hangar 2";
            m.CountdownEingabe = "21:30";
            m.TextEingabe = "Alle zum Hangar 2, Abflug 21:30";
            m.Farbwahl[1].Gewaehlt = true;
            m.TextDauerBleibt = true;
            Pruefe.Wahr(h.IstBrett && h.Brett.Sichtbar && m.Sichtbar, "Brett und Feld");

            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1500, 1250));
            var wurzel = Bildlauf.Wurzel(haupt);
            var feld = Bildlauf.Nachfahren<MeldungenFeld>(wurzel).Single();
            var karte = Teil<Border>(feld, "MeldungenKarte");
            Pruefe.Wahr(ZuSehen(karte) && ZuSehen(Teil<Border>(Bildlauf.Nachfahren<BrettAnsicht>(wurzel).Single(), "LiveRahmen")), "Feld unter dem Brett zu sehen");
            Pruefe.Wahr(Bildlauf.Nachfahren<ScrollViewer>(wurzel).Single(s => s.Name == "MeldungenPlatz") is { } platz && platz.ExtentHeight <= platz.ViewportHeight + 1,
                "das Feld passt bei 1500 x 1250 ohne Rollen");
            foreach (var text in new[] { "Meldungen", "an alle, die online sind", "Befehle (8 s)", "Zustände (bis aufgehoben)", "Ampel", "Countdown", "Bereit-Abfrage",
                         "3 von 5 bereit", "es fehlen:", "Start um 21:30", "Zeile dazu (freiwillig, bis 60 Zeichen)", "noch 52 Zeichen", "Sammelpunkt", "Pos. halten",
                         "Text", "noch 89 Zeichen", "8 s", "bleibt stehen", "Senden", MeldungenViewModel.BereitEndeSatz, MeldungenViewModel.FussSatz[..40] })
                Pruefe.Wahr(Sichtbar(feld, text), "\"" + text + "\" zu sehen");
            // Text (M24): Eingabe hart 120, sechs Farbknöpfe mit ihrer Farbe (Gelb gewählt), Dauer, Senden, aufheben.
            var farben = Bildlauf.Nachfahren<RadioButton>(feld).Where(x => x.Name == "TextFarbe").ToList();
            Pruefe.Wahr(farben.Count == 6 && farben.All(x => ZuSehen(x) && x.ActualWidth >= 24 && Glasdesign.Hilfe.GetText(x) is { Length: > 40 }
                && AutomationProperties.GetName(x).StartsWith("Textfarbe ", StringComparison.Ordinal)), "sechs Farbknöpfe, je mit Hovertext und Namen");
            Pruefe.Gleich("Textfarbe Gelb", string.Join("|", farben.Where(x => x.IsChecked == true).Select(AutomationProperties.GetName)), "Gelb gewählt");
            Pruefe.Wahr(farben.Select(x => x.Background).OfType<SolidColorBrush>().Select(b => b.Color).SequenceEqual(Textfarben.Alle.Select(f => f.Farbe)), "jeder Knopf in seiner Farbe");
            Pruefe.Wahr(Teil<TextBox>(feld, "MeldungText").MaxLength == 120 && Teil<RadioButton>(feld, "TextDauerBleibt").IsChecked == true
                && ZuSehen(Teil<Button>(feld, "TextSenden")) && ZuSehen(Teil<Button>(feld, "TextAufheben")), "Eingabe hart 120, bleibt stehen gewählt, Senden und aufheben");
            var kacheln = Bildlauf.Nachfahren<Button>(feld).Where(x => x.Name == "Kachel").ToList();
            Pruefe.Gleich(19, kacheln.Count, "19 Kacheln (8 Befehle, 5 Zustände, 3 Ampel, 2 Countdown, Bereit)");
            foreach (var k in kacheln)
            {
                Pruefe.Wahr(ZuSehen(k) && Glasdesign.Hilfe.GetText(k) is { Length: > 40 } && AutomationProperties.GetName(k).Length > 2, "Kachel " + AutomationProperties.GetName(k));
                Pruefe.Wahr(Bildlauf.Nachfahren<Image>(k).Single().Source is BitmapSource, "Bild an " + AutomationProperties.GetName(k));
            }
            var laeuft = kacheln.Where(k => k.BorderThickness.Left >= 2).Select(k => AutomationProperties.GetName(k)).ToList();
            Pruefe.Gleich("Position halten|Gelb|Start um|Bereit melden", string.Join("|", laeuft), "was läuft (und die gewählte Form), hat einen Rand");
            Pruefe.Wahr(Teil<RadioButton>(feld, "GroesseDoppelt").IsChecked == true && Teil<TextBox>(feld, "MeldungZeile").MaxLength == 60, "doppelt gewählt, Zeile hart 60");
            Pruefe.Wahr(NichtsAbgeschnitten(karte, "Feld Meldungen") >= 40, "genug geprüft");
            _ = KontrastPruefen(karte, satz, "Feld Meldungen", mindestens: 30);
            _ = OberflaecheStartseiteTests.SchattenFrei(karte, "Feld Meldungen");
            var tasten = OberflaecheHauptfensterTests.Zugriffstasten(wurzel);
            Pruefe.Gleich("", string.Join(" | ", OberflaecheHauptfensterTests.Doppelte(tasten, "Funkfeld-Zuteilung mit Meldungen")), "Zugriffstasten eindeutig");
            Bildablage.Ablegen(Ausschnitt(wurzel, bild, karte, 12), "vorschau-0120-meldungen-leitung");

            // Ein Klick auf eine Kachel sendet (die Attrappe nimmt es wie der Server), danach ist das Feld kurz gesperrt.
            var stopp = kacheln.Single(k => AutomationProperties.GetName(k) == "Stopp");
            stopp.Command.Execute(stopp.CommandParameter);
            Bildlauf.Leeren();
            Pruefe.Wahr(d.Aufrufe.Contains("Meldung Befehl stopp x2 \"Hangar 2\"") && m.Gesperrt, "Klick sendet Stopp doppelt mit Zeile, dann gesperrt");
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1500, 1250));
            Pruefe.Wahr(Sichtbar(feld, "Nächste Meldung in"), "die Wartezeit steht da");
            Pruefe.Falsch(Teil<Button>(feld, "ZustandAufheben").IsEnabled, "gesperrt: aufheben geht gerade nicht");
            funkfeld.Dispose();
        });
    }

    /// <summary>
    /// Die Anzeige über dem Spiel, wie das Fenster sie zeichnet (ohne Handle): keine Fläche (Ecken durchsichtig), die Bilder, die Zeiten weiß mit Schatten
    /// in den Feldern der Sanduhr, die Zeile mittig darunter; dreifach dreimal so groß wie normal. Fünf Lagen auf einem selbst gezeichneten Spielbild, oben
    /// Mitte wie ab Werk: A Befehl normal mit Zeile, Zustand, Countdown "Start um"; B Bereit-Abfrage (gemeldet), Ampel gelb, Zustand, Restzeit; C Start
    /// freigegeben dreifach; D Metall mit dem Xeno-Stopp und Ampel rot; E Nur Schrift; F die Textmeldung (M24) gelb unter dem Xeno-Stopp in Metall
    /// und rot doppelt in zwei Zeilen, in Farbe, fett, mit Schatten, ohne Fläche, so umgebrochen wie gemessen. Mit FUNK_BILDER das Bild
    /// vorschau-0120-meldungen-overlay.
    /// </summary>
    [Test]
    public static void Meldungen_Bildlauf_Overlay_Bild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var p = new Probe();
            var f = new MeldeanzeigeFenster(p.Anzeige);
            fenster.Add(f);

            // A: Befehl normal mit Zeile, Zustand, Countdown Start um.
            p.Stand(s => s with { Zustand = E("position_halten"), Countdown = E("countdown_startzeit", ziel: T0 + 292_000, form: "Startzeit") });
            p.Befehl("sammelpunkt_x", zeile: "Hangar 2");
            var bildA = Bildlauf.Zeichnen(f).Bild;
            Pruefe.Gleich("haupt sammelpunkt_x (Hangar 2)\nzustand position_halten\ncountdown countdown_startzeit [21:30 | 04:52]", p.Text, "A");
            var wurzel = Bildlauf.Wurzel(f);
            var zeiten = Bildlauf.Nachfahren<TextBlock>(wurzel).Where(t => t.Name == "MeldeZeit").ToList();
            Pruefe.Gleich("21:30|04:52", string.Join("|", zeiten.Select(t => t.Text)), "die Zeiten in den Feldern");
            Pruefe.Wahr(zeiten.All(t => t.Foreground is SolidColorBrush { Color: var w } && w == Colors.White && t.Effect is DropShadowEffect { Color: var s } && s == Colors.Black
                && t.FontFamily.Source == "Segoe UI"), "weiß, Segoe UI, schwarzer Schatten");
            var zeile = Bildlauf.Nachfahren<TextBlock>(wurzel).Single(t => t.Name == "MeldeZeile" && t.Text == "Hangar 2");
            Pruefe.Wahr(ZuSehen(zeile) && zeile.Effect is DropShadowEffect && zeile.Foreground is SolidColorBrush { Color: var zw } && zw == Colors.White, "die Zeile weiß mit Schatten");
            var grafik = Bildlauf.Nachfahren<Image>(wurzel).First(i => i.Name == "MeldeGrafik");
            var zr = zeile.TransformToAncestor(wurzel).TransformBounds(new Rect(zeile.RenderSize));
            var gr = grafik.TransformToAncestor(wurzel).TransformBounds(new Rect(grafik.RenderSize));
            Pruefe.Wahr(zr.Top >= gr.Bottom - 0.5 && Math.Abs(((zr.Left + zr.Right) / 2) - ((gr.Left + gr.Right) / 2)) < 2, "die Zeile mittig unter der Grafik");
            Pruefe.Gleich(0, (int)Bildlauf.Pixel(bildA, 0, 0).A, "Ecke durchsichtig (keine Fläche)");
            Pruefe.Gleich(0, (int)Bildlauf.Pixel(bildA, bildA.PixelWidth - 1, bildA.PixelHeight - 1).A, "Ecke unten rechts durchsichtig");
            var feld = zeiten[1].TransformToAncestor(wurzel).TransformBounds(new Rect(zeiten[1].RenderSize));
            var weiss = 0;
            for (var x = (int)feld.Left; x < (int)feld.Right; x++)
                for (var y = (int)feld.Top; y < (int)feld.Bottom; y++)
                    if (Bildlauf.Pixel(bildA, x, y) is { A: 255, R: > 240, G: > 240, B: > 240 }) weiss++;
            Pruefe.Wahr(weiss > 30, "im Feld steht weiße Schrift (" + weiss + " Bildpunkte)");

            // B: Bereit-Abfrage gemeldet, Ampel gelb, Zustand, Restzeit.
            var q = new Probe { Gemeldet = 5 };
            q.Stand(s => s with
            {
                Abfrage = E("bereit_melden", zeile: "Bereit? Taste \"Bereit\" oder Klick", nummer: 5), Ampel = E("ampel_gelb_bereithalten"),
                Zustand = E("event_noch_nicht_gestartet"), Countdown = E("countdown_restzeit", ziel: T0 + 754_000, form: "Restzeit"),
            });
            var fb = new MeldeanzeigeFenster(q.Anzeige);
            fenster.Add(fb);
            q.Anzeige.Takt();
            var bildB = Bildlauf.Zeichnen(fb).Bild;
            Pruefe.Wahr(q.Text.Contains("gemeldet", StringComparison.Ordinal) && Sichtbar(Bildlauf.Wurzel(fb), "gemeldet") && Sichtbar(Bildlauf.Wurzel(fb), "12:34"),
                "B: gemeldet, Restzeit 12:34");

            // C: dreifach.
            var r = new Probe();
            var fc = new MeldeanzeigeFenster(r.Anzeige);
            fenster.Add(fc);
            r.Befehl("start_freigegeben");
            var normal = Bildlauf.Zeichnen(fc).Bild;
            r.Befehl("start_freigegeben", 3);
            var bildC = Bildlauf.Zeichnen(fc).Bild;
            Pruefe.Wahr(bildC.PixelHeight > normal.PixelHeight * 2.6 && bildC.PixelWidth > normal.PixelWidth * 2.6,
                T($"dreifach etwa dreimal so groß ({normal.PixelWidth} x {normal.PixelHeight} zu {bildC.PixelWidth} x {bildC.PixelHeight})"));

            // D: Metall, Stopp mit Xeno, Ampel rot, Start in einer Stunde; E: Nur Schrift.
            var dd = new Probe();
            dd.Einstellungen.Ausfuehrung = MeldungenEinstellungen.AusfuehrungMetall;
            dd.Einstellungen.StoppBild = MeldungenEinstellungen.StoppXeno;
            dd.Stand(s => s with { Ampel = E("ampel_rot_stopp"), Countdown = E("countdown_startzeit", ziel: T0 + 3_892_000, form: "Startzeit") });
            var fd = new MeldeanzeigeFenster(dd.Anzeige);
            fenster.Add(fd);
            dd.Befehl("stopp", zeile: "Alle Schiffe halten");
            var bildD = Bildlauf.Zeichnen(fd).Bild;
            Pruefe.Gleich("Meldungen.metall.stopp_xeno.png", dd.Platz(Meldebild.Haupt).Ressource, "D: Metall, Xeno");
            var ee = new Probe();
            ee.Einstellungen.Ausfuehrung = MeldungenEinstellungen.AusfuehrungSchrift;
            ee.Stand(s => s with { Zustand = E("pause") });
            var fe = new MeldeanzeigeFenster(ee.Anzeige);
            fenster.Add(fe);
            ee.Befehl("los", 2);
            var bildE = Bildlauf.Zeichnen(fe).Bild;

            // F: Textmeldung (M24) in zwei Farben und Größen mit dem neuen Xeno-Stopp in Metall (M25, drei Finger): links der Stopp mit einem Text in
            // Gelb, normal, 8 s, linksbündig unter dem Befehlsplatz; rechts, zu einem anderen Zeitpunkt, ein Text allein in Rot, doppelt, stehend, in zwei Zeilen.
            var ff = new Probe();
            ff.Einstellungen.Ausfuehrung = MeldungenEinstellungen.AusfuehrungMetall;
            ff.Einstellungen.StoppBild = MeldungenEinstellungen.StoppXeno;
            var f1 = new MeldeanzeigeFenster(ff.Anzeige);
            fenster.Add(f1);
            ff.Befehl("stopp");
            ff.TextKommt("Alle Schiffe halten, Hangar 2 bleibt frei", Textmeldungen.Gelb);
            var bildF1 = Bildlauf.Zeichnen(f1).Bild;
            Pruefe.Wahr(ff.Platz(Meldebild.Haupt).Ressource == "Meldungen.metall.stopp_xeno.png" && ff.Text.EndsWith("text gelb (Alle Schiffe halten, Hangar 2 bleibt frei)", StringComparison.Ordinal),
                "F: Xeno-Stopp in Metall mit gelbem Text: " + ff.Text);
            var w1 = Bildlauf.Wurzel(f1);
            var text1 = Bildlauf.Nachfahren<TextBlock>(w1).Single(t => t.Name == "MeldeText");
            var grafik1 = Bildlauf.Nachfahren<Image>(w1).First(i => i.Name == "MeldeGrafik");
            var tr1 = text1.TransformToAncestor(w1).TransformBounds(new Rect(text1.RenderSize));
            var gr1 = grafik1.TransformToAncestor(w1).TransformBounds(new Rect(grafik1.RenderSize));
            Pruefe.Wahr(ZuSehen(text1) && text1.Foreground is SolidColorBrush { Color: var gelb } && gelb == Textfarben.Von(Textmeldungen.Gelb).Farbe
                && text1.Effect is DropShadowEffect { Color: var sch } && sch == Colors.Black && text1.FontFamily.Source == "Segoe UI" && text1.FontWeight == FontWeights.Bold
                && text1.Background is null && Math.Abs(text1.FontSize - 26) < 0.01, "F: gelb, fett, Segoe UI 26, schwarzer Schatten, keine Fläche");
            Pruefe.Wahr(tr1.Top >= gr1.Bottom - 0.5 && Math.Abs(tr1.Left - gr1.Left) < 1, T($"F: unter dem Befehlsplatz, linksbündig ({tr1} unter {gr1})"));
            Pruefe.Gleich(0, (int)Bildlauf.Pixel(bildF1, bildF1.PixelWidth - 1, 0).A, "F: Ecke oben rechts durchsichtig (keine Fläche)");

            var fz = new Probe();
            var f2 = new MeldeanzeigeFenster(fz.Anzeige);
            fenster.Add(f2);
            fz.TextKommt("Pause bis 21:15 Uhr, danach gemeinsam Sprung nach Pyro; alle im Kanal Flotte", Textmeldungen.Rot, 2, Textmeldungen.Bleibt);
            var bildF2 = Bildlauf.Zeichnen(f2).Bild;
            var text2 = Bildlauf.Nachfahren<TextBlock>(Bildlauf.Wurzel(f2)).Single(t => t.Name == "MeldeText");
            var einzeilig = new FormattedText("Ag", De, FlowDirection.LeftToRight, Textplatz.Schriftart, text2.FontSize, Brushes.White, 1.0).Height;
            Pruefe.Wahr(fz.Textplatz.Zeilen == 2 && Math.Round(text2.ActualHeight / einzeilig) == 2 && Math.Abs(text2.FontSize - 52) < 0.01,
                T($"F: rot, doppelt (52), in zwei Zeilen wie gemessen ({text2.ActualHeight:0} hoch, Zeile {einzeilig:0})"));
            var rot = 0;
            for (var x = 0; x < bildF2.PixelWidth; x += 2)
                for (var y = 0; y < bildF2.PixelHeight; y += 2)
                    if (Bildlauf.Pixel(bildF2, x, y) is { A: 255, R: > 220, G: < 140, B: < 130 }) rot++;
            Pruefe.Wahr(rot > 100, "F: die Buchstaben stehen in Rot da (" + rot + " Bildpunkte)");

            // Der Bogen für Sarevoc: je Lage ein Spielbild, die Anzeige oben Mitte darauf (bei F zwei Zeitpunkte nebeneinander).
            const double B = 1800;
            Pruefe.Wahr(40 + bildF1.PixelWidth + 40 <= B - 40 - bildF2.PixelWidth, T($"F: beide Zeitpunkte passen nebeneinander ({bildF1.PixelWidth} und {bildF2.PixelWidth})"));
            var lagen = new (RenderTargetBitmap Bild, RenderTargetBitmap? Daneben, double Hoehe, string Text)[]
            {
                (bildA, null, 300, "A: Befehl normal (8 s) mit Zeile, Zustand klein daneben, Countdown \"Start um\" mit Uhrzeit und Rest"),
                (bildB, null, 300, "B: Bereit-Abfrage nach dem eigenen Melden (\"gemeldet\"), Ampel gelb, Zustand, Countdown \"Restzeit\""),
                (bildC, null, 660, "C: Start freigegeben dreifach (die Leitung wählt beim Senden normal, doppelt oder dreifach)"),
                (bildD, null, 300, "D: Ausführung Metall mit dem Xeno-Stopp (Korrektur v4, drei Finger) und Zeile, Ampel rot, Start in einer Stunde"),
                (bildE, null, 220, "E: Ausführung Nur Schrift (Los doppelt, Pause; was es nicht in Nur Schrift gibt, kommt in Schlicht)"),
                (bildF1, bildF2, 300, "F: Textmeldung. Links: Xeno-Stopp in Metall, darunter Text in Gelb, normal, 8 s. Rechts, zu einem anderen Zeitpunkt: Text allein in Rot, "
                    + "doppelt, bleibt stehen, in zwei Zeilen"),
            };
            var hoehe = 48 + lagen.Sum(l => l.Hoehe + 34);
            var bogen = new Bogen(B + 40, hoehe, 1, Color.FromRgb(0x2B, 0x2B, 0x2B));
            bogen.Text("Meldungen über dem Spiel 0.12.0: Grundgröße 180, ab Werk oben Mitte; Schrift weiß oder in der Farbe der Textmeldung mit Schatten, keine Fläche",
                20, 12, 15, Colors.White, fett: true);
            var y0 = 44.0;
            foreach (var (bildL, daneben, h, text) in lagen)
            {
                var rahmen = new Rect(20, y0, B, h);
                bogen.Zeichnung(dc => Spielbild.Zeichnen(dc, rahmen));
                if (daneben is null)
                {
                    bogen.Bild(bildL, 20 + ((B - bildL.PixelWidth) / 2), y0 + 18, bildL.PixelWidth, bildL.PixelHeight);
                }
                else
                {
                    bogen.Bild(bildL, 20 + 40, y0 + 18, bildL.PixelWidth, bildL.PixelHeight);
                    bogen.Bild(daneben, 20 + B - 40 - daneben.PixelWidth, y0 + 18, daneben.PixelWidth, daneben.PixelHeight);
                }
                bogen.Text(text, 20, y0 + h + 6, 12.5, Color.FromRgb(0xE0, 0xE0, 0xE0));
                y0 += h + 34;
            }
            Bildablage.Ablegen(bogen.Fertig(), "vorschau-0120-meldungen-overlay");
        });
    }

    /// <summary>
    /// Der Abschnitt Meldungen unter Einstellungen im Bildlauf, bei 1100 und 640 und bei 175 %: offen der Satz und die Regler Waagerecht und Senkrecht (0 bis 100,
    /// Pfeiltasten 1, Bild auf und ab 10, Wert daneben, Hovertext, Namen für Bildschirmleser), kein Schalter zum Abschalten; Erweitert mit Grundgröße, Ton,
    /// Ausführung und Stopp-Bild als Wahl mit Bild und dem Fuß. Nichts abgeschnitten, Kontrast, Zugriffstasten eindeutig. Schieben am Regler setzt die Lage
    /// und zeigt die Probe. Mit FUNK_BILDER das Bild vorschau-0120-meldungen-einstellungen.
    /// </summary>
    [Test]
    public static void Meldungen_Bildlauf_Einstellungen_Bild()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            var satz = Farbvorschlaege.Von(Farbvorschlaege.Voreinstellung);
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            using var k = OberflaecheStartseiteTests.Einrichten(a);
            OberflaecheStartseiteTests.SprechtasteBelegen(a);
            var h = a.Haupt(out var funkfeld, out _, out var audio, out _);
            audio.StartenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            h.MeinProfil.LadenAsync().GetAwaiter().GetResult();
            Seiten.Feinheiten(h, Abschnittskennung.Meldungen);
            h.Takt();
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            var s = h.Einstellungen.Meldungen;
            var proben = 0;
            s.ProbeVerlangt += _ => proben++;
            ContentPresenter Seite() => Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);

            foreach (var breite in new double[] { 1100, 640 })
            {
                var was = T($"{breite:0}");
                var bild = Ganz(haupt, breite);
                var abschnitt = Teil<StackPanel>(Seite(), "AbschnittMeldungen");
                Pruefe.Wahr(ZuSehen(abschnitt), was + ": der Abschnitt");
                foreach (var (name, wert, hilfe, ziel) in new[]
                         {
                             ("MeldungenWaagerecht", "MeldungenWaagerechtWert", MeldungenEinstellungViewModel.WaagerechtHilfe, "waagerecht"),
                             ("MeldungenSenkrecht", "MeldungenSenkrechtWert", MeldungenEinstellungViewModel.SenkrechtHilfe, "senkrecht"),
                         })
                {
                    var regler = Teil<Slider>(abschnitt, name);
                    Pruefe.Wahr(ZuSehen(regler) && regler.ActualWidth > 120 && regler is { Minimum: 0, Maximum: 100, SmallChange: 1, LargeChange: 10, IsSnapToTickEnabled: true, Focusable: true },
                        was + ": " + name);
                    Pruefe.Gleich(hilfe, Glasdesign.Hilfe.GetText(regler), was + ": Hovertext " + name);
                    Pruefe.Wahr(AutomationProperties.GetName(regler).Contains(ziel, StringComparison.Ordinal), was + ": Name " + name);
                    Pruefe.Wahr(ZuSehen(Teil<TextBlock>(abschnitt, wert)), was + ": Wert neben " + name);
                }
                Pruefe.Gleich("50 %|0 %", Teil<TextBlock>(abschnitt, "MeldungenWaagerechtWert").Text + "|" + Teil<TextBlock>(abschnitt, "MeldungenSenkrechtWert").Text, was + ": ab Werk");
                Pruefe.Gleich("MeldungenTon", string.Join("|", Bildlauf.Nachfahren<CheckBox>(abschnitt).Select(c => c.Name)), was + ": nur der Ton als Schalter, keiner zum Abschalten");
                Pruefe.Wahr(Sichtbar(abschnitt, "Wo im Bild") && Sichtbar(abschnitt, MeldungenEinstellungViewModel.AutomatikText[..40])
                    && Sichtbar(abschnitt, MeldungenEinstellungViewModel.LageSatz[..30]), was + ": Beschriftungen, Kasten, Satz");
                var groesse = Teil<Slider>(abschnitt, "MeldungenGroesse");
                Pruefe.Wahr(ZuSehen(groesse) && groesse is { Minimum: 100, Maximum: 400, Value: 180 } && Teil<TextBlock>(abschnitt, "MeldungenGroesseWert").Text == "180", was + ": Grundgröße 180");
                Pruefe.Wahr(Teil<CheckBox>(abschnitt, "MeldungenTon") is { IsChecked: true } ton && ZuSehen(ton), was + ": Ton an");
                var ausfuehrung = Bildlauf.Nachfahren<RadioButton>(Teil<WrapPanel>(abschnitt, "MeldungenAusfuehrung")).ToList();
                var stopp = Bildlauf.Nachfahren<RadioButton>(Teil<WrapPanel>(abschnitt, "MeldungenStopp")).ToList();
                Pruefe.Wahr(ausfuehrung.Count == 3 && ausfuehrung[0].IsChecked == true && stopp.Count == 2 && stopp[0].IsChecked == true
                    && ausfuehrung.Concat(stopp).All(x => ZuSehen(x) && Bildlauf.Nachfahren<Image>(x).Single().Source is BitmapSource), was + ": Wahl mit Bild, Schlicht und Handschuh");
                Pruefe.Wahr(Bildlauf.Nachfahren<Button>(abschnitt).Any(x => x.Name == "FeinheitenZurueck" && ZuSehen(x)) || Sichtbar(abschnitt, "Zurück auf Werkseinstellung"), was + ": der Fuß");
                Pruefe.Wahr(NichtsAbgeschnitten(abschnitt, was) >= 20, was + ": genug geprüft");
                _ = OberflaecheStartseiteTests.SchattenFrei(abschnitt, was);
                if (breite == 1100)
                {
                    _ = KontrastPruefen(abschnitt, satz, was + ", Meldungen", mindestens: 10);
                    var karte = Teil<Border>(Seite(), "AbschnitteKarte");
                    var rk = karte.TransformToAncestor(Bildlauf.Wurzel(haupt)).TransformBounds(new Rect(karte.RenderSize));
                    var ra = abschnitt.TransformToAncestor(Bildlauf.Wurzel(haupt)).TransformBounds(new Rect(abschnitt.RenderSize));
                    var links = (int)Math.Max(0, rk.Left - 8);
                    var oben = (int)Math.Max(0, ra.Top - 14);
                    Bildablage.Ablegen(new CroppedBitmap(bild, new Int32Rect(links, oben, (int)Math.Min(bild.PixelWidth - links, rk.Width + 16),
                        (int)Math.Min(bild.PixelHeight - oben, ra.Height + 28))), "vorschau-0120-meldungen-einstellungen");
                }
                _ = Bildlauf.Zeichnen(haupt, 1.75, new Size(breite, bild.PixelHeight));
                _ = NichtsAbgeschnitten(Teil<StackPanel>(Seite(), "AbschnittMeldungen"), was + ", 175 %");
            }

            _ = Ganz(haupt, 1100);
            var waagerecht = Teil<Slider>(Seite(), "MeldungenWaagerecht");
            foreach (var w in new double[] { 49, 48, 30 }) waagerecht.Value = w;
            Bildlauf.Leeren();
            Pruefe.Wahr(s.Waagerecht == 30 && a.Ablage.Aktuell.Meldungen.Waagerecht == 30 && proben == 3, T($"Waagerecht auf 30: gemerkt, je Schritt eine Probe ({proben})"));
            var metall = Bildlauf.Nachfahren<RadioButton>(Teil<WrapPanel>(Seite(), "MeldungenAusfuehrung")).ElementAt(1);
            metall.IsChecked = true;
            Bildlauf.Leeren();
            Pruefe.Wahr(s.AusfuehrungMetall && a.Ablage.Aktuell.Meldungen.Ausfuehrung == 1 && proben == 4, "Metall gewählt, mit Probe");
            var tasten = OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt));
            Pruefe.Gleich("", string.Join(" | ", OberflaecheHauptfensterTests.Doppelte(tasten, "Meldungen offen")), "Zugriffstasten eindeutig");
            funkfeld.Dispose();
        });
    }

    /// <summary>
    /// Der Knopf "Bereit melden" in der Titelleiste: nur solange eine Abfrage offen ist und ein anderer fragt, grün mit dunkler Schrift; ein Klick meldet,
    /// danach steht "gemeldet" und der Knopf ist aus. Ohne Abfrage ist er fort.
    /// </summary>
    [Test]
    public static void Meldungen_Bildlauf_BereitKnopf_Titelleiste()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            var haupt = new Hauptfenster(h, a.Ablage, new FesteBildschirme([FullHd]));
            fenster.Add(haupt);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1280, 900));
            var knopf = Teil<Button>(Bildlauf.Wurzel(haupt), "BereitKnopf");
            Pruefe.Falsch(ZuSehen(knopf), "ohne Abfrage kein Knopf");
            a.Dienst.LageSetzen(l => l with { ServerMeldungen = true, Meldestand = new Meldestand { Anlass = Meldungsvorgang.BereitAbfrage, Abfrage = E("bereit_melden", von: 4, nummer: 2) } });
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1280, 900));
            Pruefe.Wahr(ZuSehen(knopf) && Inhalt(Teil<TextBlock>(knopf, "BereitText")) == "Bereit melden" && knopf.IsEnabled, "Nordlicht fragt: der Knopf");
            Pruefe.Gleich(Bereitmeldung.Hilfe, Glasdesign.Hilfe.GetText(knopf), "Hovertext");
            Pruefe.Wahr(Kontrast.Verhaeltnis(Farbe(knopf.Foreground), Farbe(knopf.Background)) >= 4.5, "Schrift auf dem Knopf lesbar");
            knopf.Command.Execute(null);
            Bildlauf.Leeren();
            h.Bereit.Aktualisieren();
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1280, 900));
            Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Bereit 2") && Inhalt(Teil<TextBlock>(knopf, "BereitText")) == "gemeldet" && !knopf.IsEnabled, "gemeldet");
            a.Dienst.LageSetzen(l => l with { Meldestand = null });
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1280, 900));
            Pruefe.Falsch(ZuSehen(knopf), "Abfrage zu: kein Knopf");
            funkfeld.Dispose();
        });
    }

    private static string T(FormattableString s) => s.ToString(De);
}
