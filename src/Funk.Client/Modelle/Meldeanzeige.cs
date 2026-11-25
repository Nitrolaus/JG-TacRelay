// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Klang.Verarbeitung;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Die Meldungen der Leitung über dem Spiel beim Funker (seit 0.12.0, Sarevoc 11.10. und 16.10.2026, M3-M23, tmp\melde\SPEZ.md "Client"). Ein eigenes
/// Fenster wie die Sprecheranzeige (<c>Fenster.MeldeanzeigeFenster</c>), das nur zu sehen ist, solange etwas da ist. Nebeneinander, oben bündig:
/// <list type="bullet">
/// <item>Groß: ein Befehl, <see cref="BefehlMs"/> lang ab dem Eingang, ein neuer ersetzt den alten (gleich von wem); sonst eine offene
/// Bereit-Abfrage, darunter nach dem eigenen Melden "gemeldet" (die Quittung, M3-M14).</item>
/// <item>Klein daneben ein Zustand (<see cref="ZustandAnteil"/> der Grundgröße), bis er aufgehoben oder ersetzt wird.</item>
/// <item>Je ein eigener Platz für die Ampel (nur von Hand, M22) und den Countdown; in dessen freie Felder kommen die Zeiten als weiße Schrift mit
/// dem Schatten der Sprecheranzeige (Variante B): "Start um" mit Uhrzeit (Ortszeit) und Rest, "Restzeit" mit Rest. Bei 0 verschwindet er.</item>
/// <item>Unter der Reihe, linksbündig unter dem Befehlsplatz, die Textmeldung (M24): nur Buchstaben in ihrer Farbe mit demselben Schatten, keine
/// Fläche, kein Rahmen, höchstens zwei Zeilen (<see cref="Textplatz"/>); mit 8 s (<see cref="TextMs"/> ab dem Eingang, wie ein Befehl) oder
/// stehend, bis sie aufgehoben oder durch eine neue ersetzt wird.</item>
/// </list>
/// Größe: die längste Seite des zugeschnittenen Bilds ist die Grundgröße des Funkers (<see cref="MeldungenEinstellungen.Grundgroesse"/>, ab Werk 180)
/// mal dem gesendeten Faktor 1 bis 3 (M21); beim Countdown die Höhe <see cref="CountdownAnteil"/> davon, damit die Zeiten lesbar sind. Nie größer
/// als der Monitor (<see cref="Grenze"/>): Ein Bild passt immer hinein, und eine Reihe, die zu breit würde, wird als Ganzes kleiner. Die freiwillige
/// Zeile der Leitung steht unter ihrer Grafik. Das Bild kommt in der Ausführung des Funkers, mit Rückfall auf Schlicht, beim Stopp in seiner
/// Wahl (<see cref="Meldungskatalog.Ressource"/>).
/// <para>
/// Ton (abschaltbar, <see cref="MeldungenEinstellungen.Ton"/>): ein kurzer bei jedem Befehl und jeder Textmeldung, je einer in den letzten zehn
/// Sekunden eines Countdowns; keiner bei Zuständen. Zeiten: der Befehl nach der eigenen Uhr (<paramref name="uhr"/> im Konstruktor), der Countdown nach der Serverzeit mit
/// Uhrabgleich (<see cref="IFunkDienst.ServerJetztMs"/>). Der laufende Stand kommt aus der Lage (<see cref="FunkLage.Meldestand"/>), der Befehl
/// nur als Ereignis (<see cref="Eingegangen"/>). Keine Daueranimation: Der Takt ändert nur etwas, wenn eine Zeit umspringt.
/// </para>
/// </summary>
public sealed partial class Meldeanzeige : ObservableObject
{
    /// <summary>So lange steht ein Befehl (M3-M14: "Befehle groß 8 s").</summary>
    public const long BefehlMs = 8000;

    /// <summary>So lange steht eine Textmeldung mit der Dauer "8 s" (M24), ab dem Eingang wie ein Befehl.</summary>
    public const long TextMs = 8000;

    /// <summary>So lange steht die Probe nach dem letzten Schieben an der Lage oder einer Wahl unter Erweitert.</summary>
    public const long ProbeMs = 3000;

    /// <summary>Ein Zustand ist klein: diese Teile der Grundgröße (immer, auch wenn die Leitung mehr wählt).</summary>
    public const double ZustandAnteil = 0.5;

    /// <summary>Der Countdown ist ein Querformat: seine Höhe ist dieser Teil von Grundgröße mal Faktor (die Breite folgt dem Bild).</summary>
    public const double CountdownAnteil = 0.6;

    /// <summary>Die letzten so vielen Sekunden eines Countdowns bekommen je einen Ton.</summary>
    public const int TonSekunden = 10;

    /// <summary>Was die Probe zeigt: ein Befehl mit dieser Zeile.</summary>
    public const string ProbeZeile = "Probe: hier stehen die Meldungen";

    /// <summary>Unter der Bereit-Abfrage, sobald du bereit gemeldet hast.</summary>
    public const string GemeldetText = "gemeldet";

    private readonly IFunkDienst _dienst;
    private readonly Func<MeldungenEinstellungen> _einstellungen;
    private readonly Func<long> _uhr;
    private readonly Func<long> _serverJetzt;
    private readonly Func<Signaltonart, bool> _ton;
    private readonly Func<uint?> _gemeldet;
    private readonly Func<long, DateTimeOffset> _ortszeit;
    private Meldungseintrag? _befehl;
    private long _befehlBis = long.MinValue;
    private Textmeldung? _text;
    private long _textBis = long.MinValue;
    private long _probeBis = long.MinValue;
    private string _probeMotiv = "sammeln";
    private (long Seit, long Ziel)? _tonCountdown;
    private long _tonSekunde = long.MaxValue;

    /// <param name="einstellungen">Die gemerkten Einstellungen, je Aufruf gelesen.</param>
    /// <param name="uhr">Millisekunden für Befehl und Probe (im Betrieb <c>Environment.TickCount64</c>, in Tests eine Attrappe).</param>
    /// <param name="serverJetzt">Serverzeit in Unix-ms für den Countdown; ohne Angabe <see cref="IFunkDienst.ServerJetztMs"/>.</param>
    /// <param name="ton">Spielt einen Signalton (im Betrieb <c>IKlangMaschine.TonSpielen</c>); ohne Angabe keiner.</param>
    /// <param name="gemeldet">Die Nummer der Abfrage, auf die du bereit gemeldet hast (<see cref="Bereitmeldung.GemeldetNummer"/>); ohne Angabe keine.</param>
    /// <param name="ortszeit">Unix-ms in Ortszeit für "Start um"; ohne Angabe die Zeitzone dieses PCs.</param>
    public Meldeanzeige(IFunkDienst dienst, Func<MeldungenEinstellungen> einstellungen, Func<long> uhr, Func<long>? serverJetzt = null,
        Func<Signaltonart, bool>? ton = null, Func<uint?>? gemeldet = null, Func<long, DateTimeOffset>? ortszeit = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(einstellungen);
        ArgumentNullException.ThrowIfNull(uhr);
        _dienst = dienst;
        _einstellungen = einstellungen;
        _uhr = uhr;
        _serverJetzt = serverJetzt ?? dienst.ServerJetztMs;
        _ton = ton ?? (_ => false);
        _gemeldet = gemeldet ?? (() => null);
        _ortszeit = ortszeit ?? (ms => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime());
    }

    /// <summary>Was das Fenster zeigt.</summary>
    public Meldebild Bild { get; } = new();

    /// <summary>Es ist etwas zu zeigen (sonst ist das Fenster verborgen, nicht leer gezeigt).</summary>
    [ObservableProperty]
    public partial bool Sichtbar { get; private set; }

    /// <summary>
    /// Platz auf dem Monitor in Einheiten (ohne den Rand), den die Anzeige nie überschreitet; das Fenster setzt ihn nach dem Monitor, auf dem sie
    /// steht. Ohne Angabe unbegrenzt.
    /// </summary>
    public Size Grenze
    {
        get;
        set
        {
            if (value == field) return;
            field = value;
            Abgleichen();
        }
    } = new(double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>Die Probe steht gerade.</summary>
    public bool ProbeLaeuft => _uhr() < _probeBis;

    /// <summary>
    /// Ein Push 0x4005 kam an (<see cref="MeldungEingegangen"/>, auf dem Oberflächen-Thread): ein Befehl steht ab jetzt <see cref="BefehlMs"/> lang groß
    /// und klingt, wenn der Ton an ist; eine Textmeldung klingt ebenso und ersetzt die laufende, mit 8 s steht sie <see cref="TextMs"/> lang. Der übrige
    /// Stand kommt aus der Lage, auch eine bleibende Textmeldung.
    /// </summary>
    public void Eingegangen(Meldestand stand)
    {
        ArgumentNullException.ThrowIfNull(stand);
        if (string.Equals(stand.Anlass, Meldungsvorgang.Befehl, StringComparison.Ordinal) && stand.Befehl is { } b)
        {
            _befehl = b;
            _befehlBis = _uhr() + BefehlMs;
            if (_einstellungen().Ton) _ = _ton(Signaltonart.Befehl);
        }
        else if (string.Equals(stand.Anlass, Meldungsvorgang.Text, StringComparison.Ordinal) && stand.Text is { } t)
        {
            // Eine bleibende steht in der Lage; die mit 8 s merkt sich die Anzeige wie einen Befehl. Beides ersetzt, was vorher stand.
            var bleibt = Textmeldungen.Bleibend(t) is not null;
            _text = bleibt ? null : t;
            _textBis = bleibt ? long.MinValue : _uhr() + TextMs;
            if (_einstellungen().Ton) _ = _ton(Signaltonart.Befehl);
        }
        else if (string.Equals(stand.Anlass, Meldungsvorgang.TextAufheben, StringComparison.Ordinal))
        {
            _text = null;
            _textBis = long.MinValue;
        }
        Abgleichen();
    }

    /// <summary>Bildtakt: Ende des Befehls und der Probe, der Countdown (Zeiten, Ton, Ende), der Stand aus der Lage.</summary>
    public void Takt() => Abgleichen();

    /// <summary>
    /// Die Probe bis <see cref="ProbeMs"/> nach diesem Aufruf: ein Befehl <paramref name="motiv"/> (ohne Angabe Sammeln) in Grundgröße mit der Zeile
    /// <see cref="ProbeZeile"/>, an der eingestellten Stelle. Jedes Schieben an der Lage ruft es wieder.
    /// </summary>
    public void ProbeZeigen(string? motiv = null)
    {
        _probeBis = _uhr() + ProbeMs;
        _probeMotiv = Meldungskatalog.Kennt(motiv) ? motiv! : "sammeln";
        Abgleichen();
    }

    /// <summary>"04:52", "1:04:52": der Rest in ganzen Sekunden (aufgerundet), unter einer Stunde mit Minuten und Sekunden zweistellig.</summary>
    public static string Restzeit(long sekunden)
    {
        var s = Math.Max(0, sekunden);
        var h = s / 3600;
        var m = s % 3600 / 60;
        var r = s % 60;
        return h > 0
            ? h.ToString(CultureInfo.InvariantCulture) + ":" + m.ToString("00", CultureInfo.InvariantCulture) + ":" + r.ToString("00", CultureInfo.InvariantCulture)
            : m.ToString("00", CultureInfo.InvariantCulture) + ":" + r.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Die Sekunden bis <paramref name="ziel"/>, aufgerundet: 9,2 s sind 10, 0,1 s ist 1, 0 und weniger ist vorbei.</summary>
    public static long RestSekunden(long ziel, long jetzt) => ziel <= jetzt ? 0 : (ziel - jetzt + 999) / 1000;

    private void Abgleichen()
    {
        var e = _einstellungen();
        var jetzt = _uhr();
        var plaetze = new List<Platzwunsch>();
        Textmeldung? text = null;
        if (jetzt < _probeBis)
        {
            plaetze.Add(new Platzwunsch(Meldebild.Haupt, _probeMotiv, 1, ProbeZeile, null, null));
        }
        else
        {
            var stand = _dienst.Lage.Meldestand;
            if (_text is { } t && jetzt < _textBis) text = t;
            else
            {
                _text = null;
                text = Textmeldungen.Bleibend(stand?.Text);
            }
            if (_befehl is { } b && jetzt < _befehlBis) plaetze.Add(new Platzwunsch(Meldebild.Haupt, b.Motiv, b.Groesse, b.Zeile, null, null));
            else if (stand?.Abfrage is { } a)
                plaetze.Add(new Platzwunsch(Meldebild.Haupt, a.Motiv, a.Groesse, a.Zeile, a.Nummer is { } n && _gemeldet() == n ? GemeldetText : null, null));
            if (jetzt >= _befehlBis) _befehl = null;
            if (stand?.Zustand is { } z) plaetze.Add(new Platzwunsch(Meldebild.Zustand, z.Motiv, 1, z.Zeile, null, null));
            if (stand?.Ampel is { } am) plaetze.Add(new Platzwunsch(Meldebild.Ampel, am.Motiv, am.Groesse, am.Zeile, null, null));
            if (stand?.Countdown is { Ziel: { } ziel } c && CountdownTexte(c, ziel, e) is { } texte)
                plaetze.Add(new Platzwunsch(Meldebild.Countdown, c.Motiv, c.Groesse, c.Zeile, null, texte));
            else
                _tonCountdown = null;
        }
        Bild.Setzen(plaetze, e, Grenze, text);
        Sichtbar = Bild.Plaetze.Count > 0 || Bild.Textplatz.Da;
    }

    /// <summary>Die Zeiten des Countdowns, oder <c>null</c>, wenn er vorbei ist; in den letzten zehn Sekunden je Sekunde ein Ton.</summary>
    private IReadOnlyList<string>? CountdownTexte(Meldungseintrag c, long ziel, MeldungenEinstellungen e)
    {
        var rest = RestSekunden(ziel, _serverJetzt());
        if (rest <= 0) return null;
        var wer = (c.Seit, ziel);
        if (_tonCountdown != wer)
        {
            _tonCountdown = wer;
            _tonSekunde = long.MaxValue;
        }
        if (rest <= TonSekunden && rest < _tonSekunde)
        {
            _tonSekunde = rest;
            if (e.Ton) _ = _ton(Signaltonart.Sekunde);
        }
        var restText = Restzeit(rest);
        return string.Equals(c.Form, Meldungskatalog.FormStartzeit, StringComparison.Ordinal) || string.Equals(c.Motiv, Meldungskatalog.StartzeitMotiv, StringComparison.Ordinal)
            ? [_ortszeit(ziel).ToString("HH:mm", CultureInfo.InvariantCulture), restText]
            : [restText];
    }
}

/// <summary>Was an einem Platz stehen soll (für <see cref="Meldebild.Setzen"/>).</summary>
internal readonly record struct Platzwunsch(string Schluessel, string Motiv, byte Groesse, string? Zeile, string? Zusatz, IReadOnlyList<string>? Zeiten);

/// <summary>
/// Was die Meldungen über dem Spiel zeigen (seit 0.12.0): die Plätze nebeneinander, Befehl oder Bereit-Abfrage, Zustand, Ampel, Countdown, darunter
/// die Textmeldung (<see cref="Textplatz"/>). Das Fenster und das Vorschaubild nehmen dieselbe Vorlage (<c>Meldeanzeige.Bild</c> in Design\Stile.xaml).
/// Ein Platz bleibt dasselbe Objekt, solange sein Schlüssel bleibt; nur was sich ändert, meldet sich (der Countdown einmal je Sekunde).
/// </summary>
public sealed class Meldebild
{
    public const string Haupt = "haupt";
    public const string Zustand = "zustand";
    public const string Ampel = "ampel";
    public const string Countdown = "countdown";

    /// <summary>Abstand zwischen zwei Plätzen in Einheiten bei Grundgröße 180 (wächst mit ihr).</summary>
    public const double LueckeGrund = 18;

    /// <summary>Schrift der Zeile bei Grundgröße 180 und "normal" (etwas kleiner als die Schrift der Sprecheranzeige, 26).</summary>
    public const double ZeileGrund = 20;

    /// <summary>
    /// Grundschrift der Textmeldung bei Grundgröße 180 und "normal": die Schrift der Sprecheranzeige bei 100 % (<see cref="Sprecherbild.SchriftGrund"/>,
    /// 26). Beide stehen als Schrift mit demselben Schatten über dem Spiel und werden mit einem Blick gelesen; die Zeile unter einer Grafik ist mit 20
    /// bewusst kleiner, weil sie nur beschriftet, die Textmeldung aber selbst die Meldung ist. Doppelt 52, dreifach 78; sie wächst und schrumpft mit
    /// der Grundgröße des Funkers wie die Bilder.
    /// </summary>
    public const double TextGrund = Sprecherbild.SchriftGrund;

    /// <summary>Die Plätze von links nach rechts.</summary>
    public ObservableCollection<Meldeplatz> Plaetze { get; } = [];

    /// <summary>Die Textmeldung unter der Reihe; <see cref="Textplatz.Da"/> sagt, ob eine steht.</summary>
    public Textplatz Textplatz { get; } = new();

    /// <summary>
    /// Die freien Felder der Countdown-Bilder im ganzen Bild (600 x 400), an den Bildern gemessen (Schlicht und Metall, der gemeinsame innere
    /// Teil, tmp\melde\felder_messen.py): "Start um" (Uhrzeit) und "Countdown" (Rest) im Bild countdown_startzeit, "Restzeit" in countdown_restzeit.
    /// </summary>
    public static readonly Rect FeldStartUm = new(290, 115, 248, 50);

    /// <inheritdoc cref="FeldStartUm"/>
    public static readonly Rect FeldCountdown = new(290, 222, 256, 65);

    /// <inheritdoc cref="FeldStartUm"/>
    public static readonly Rect FeldRestzeit = new(240, 140, 312, 124);

    /// <summary>Was Bildschirmleser und Tests lesen: je Platz eine Zeile, die Textmeldung als letzte.</summary>
    public string Text => string.Join("\n", Plaetze.Select(p => p.Text).Concat(Textplatz.Da ? [Textplatz.Beschreibung] : []));

    internal void Setzen(IReadOnlyList<Platzwunsch> wuensche, MeldungenEinstellungen e, Size grenze, Textmeldung? text = null)
    {
        var g = Math.Clamp(e.Grundgroesse, MeldungenEinstellungen.GrundgroesseMin, MeldungenEinstellungen.GrundgroesseMax);
        var luecke = LueckeGrund * g / MeldungenEinstellungen.GrundgroesseAbWerk;
        var gebaut = new List<(Platzwunsch W, string? Ressource, Meldungsgrafik? Grafik, double Breite, double Hoehe, double Faktor)>();
        foreach (var w in wuensche)
        {
            var ressource = Meldungskatalog.Ressource(w.Motiv, e.AusfuehrungKennung, string.Equals(w.Motiv, Meldungskatalog.Stopp, StringComparison.Ordinal) ? e.StoppBildKennung : null);
            var grafik = Meldungsbilder.Ganz(ressource);
            var f = Math.Clamp((int)w.Groesse, 1, Grenzen.MeldungGroesseMax);
            var seite = w.Schluessel == Zustand ? g * Meldeanzeige.ZustandAnteil : g * f;
            var verh = grafik?.Seitenverhaeltnis ?? 1;
            double breite, hoehe;
            if (w.Schluessel == Countdown)
            {
                hoehe = seite * Meldeanzeige.CountdownAnteil;
                breite = hoehe * verh;
            }
            else if (verh >= 1)
            {
                breite = seite;
                hoehe = seite / verh;
            }
            else
            {
                hoehe = seite;
                breite = seite * verh;
            }
            // Nie größer als der Monitor: das Bild ganz hinein.
            var passt = Math.Min(1, Math.Min(grenze.Width / breite, grenze.Height / hoehe));
            gebaut.Add((w, ressource, grafik, breite * passt, hoehe * passt, w.Schluessel == Zustand ? 0.8 : 1 + (0.5 * (f - 1))));
        }
        // Eine Reihe, die zu breit würde, wird als Ganzes kleiner.
        var summe = gebaut.Sum(x => x.Breite) + (luecke * Math.Max(0, gebaut.Count - 1));
        var reihe = double.IsFinite(grenze.Width) && summe > grenze.Width && summe > 0 ? grenze.Width / summe : 1;

        var schluessel = gebaut.Select(x => x.W.Schluessel).ToList();
        if (!schluessel.SequenceEqual(Plaetze.Select(p => p.Schluessel), StringComparer.Ordinal))
        {
            var bisher = Plaetze.ToDictionary(p => p.Schluessel, StringComparer.Ordinal);
            Plaetze.Clear();
            foreach (var s in schluessel) Plaetze.Add(bisher.GetValueOrDefault(s) ?? new Meldeplatz(s));
        }
        for (var i = 0; i < gebaut.Count; i++)
        {
            var (w, ressource, grafik, breite, hoehe, schrift) = gebaut[i];
            Plaetze[i].Setzen(w, ressource, grafik, breite * reihe, hoehe * reihe, Math.Clamp(ZeileGrund * g / MeldungenEinstellungen.GrundgroesseAbWerk * schrift * reihe, 11, 64),
                i < gebaut.Count - 1 ? luecke * reihe : 0);
        }
        // Die Textmeldung unter der Reihe (M24), mit dem Abstand zwischen den Plätzen, wenn darüber etwas steht.
        Textplatz.Setzen(text, TextGrund * g / MeldungenEinstellungen.GrundgroesseAbWerk, gebaut.Count > 0 ? luecke * reihe : 0, grenze.Width);
    }
}

/// <summary>
/// Die Textmeldung über dem Spiel (seit 0.12.0, M24): nur Buchstaben in der gesendeten Farbe (<see cref="Textfarben"/>), fett in Segoe UI (Systemschrift),
/// mit dem Schatten der Sprecheranzeige (Variante B, <see cref="Schattenwurf"/>), keine Fläche, kein Rahmen, linksbündig unter dem Befehlsplatz. Größe:
/// Grundschrift (<see cref="Meldebild.TextGrund"/> mal Grundgröße durch 180) mal dem gesendeten Faktor 1 bis 3. Umbruch in höchstens zwei Zeilen: Was in
/// eine Zeile von <see cref="ZeileEm"/> Schriftgrößen passt (etwa 60 Zeichen, die Hälfte von 120), steht in einer; sonst bricht der Text in zwei etwa
/// gleich lange Zeilen um. Nie breiter als der Monitor: Reicht seine Breite nicht für zwei Zeilen, wird die Schrift kleiner, bis sie reicht.
/// </summary>
public sealed partial class Textplatz : ObservableObject
{
    /// <summary>So breit (in Schriftgrößen) darf eine Zeile werden, bevor der Text in zwei Zeilen umbricht: etwa 60 Zeichen fetter Segoe UI.</summary>
    public const double ZeileEm = 32;

    /// <summary>Kleiner wird die Schrift auch auf einem sehr schmalen Monitor nicht.</summary>
    public const double SchriftMin = 10;

    /// <summary>Die Schrift der Vorlage (Schrift.Text in Design\Stile.xaml, fett); gemessen wird mit derselben.</summary>
    internal static readonly Typeface Schriftart = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private (string Text, double Schrift, double Verfuegbar) _gemessen = ("", 0, 0);
    private (double Schrift, double Breite, int Zeilen) _form;

    /// <summary>Eine Textmeldung steht.</summary>
    [ObservableProperty]
    public partial bool Da { get; private set; }

    [ObservableProperty]
    public partial string? Text { get; private set; }

    /// <summary>Kennung der Farbe ("gelb"), ohne Textmeldung leer.</summary>
    [ObservableProperty]
    public partial string Farbe { get; private set; } = "";

    [ObservableProperty]
    public partial Brush Pinsel { get; private set; } = Brushes.White;

    [ObservableProperty]
    public partial double Schrift { get; private set; } = Meldebild.TextGrund;

    /// <summary>Breite des Textblocks in Einheiten; er bricht bei ihr um.</summary>
    [ObservableProperty]
    public partial double Breite { get; private set; }

    /// <summary>1 oder 2.</summary>
    [ObservableProperty]
    public partial int Zeilen { get; private set; }

    /// <summary>Oben der Abstand zur Reihe, rechts und unten Platz für den Schatten.</summary>
    [ObservableProperty]
    public partial Thickness Rand { get; private set; }

    [ObservableProperty]
    public partial System.Windows.Media.Effects.DropShadowEffect Schatten { get; private set; } = Schattenwurf.Effekt(Meldebild.TextGrund);

    /// <summary>Was Bildschirmleser und Tests lesen: "text gelb (Alle zum Hangar 2)".</summary>
    [ObservableProperty]
    public partial string Beschreibung { get; private set; } = "";

    internal void Setzen(Textmeldung? t, double grundschrift, double oben, double grenzeBreite)
    {
        if (t is null || string.IsNullOrWhiteSpace(t.Text))
        {
            Da = false;
            Text = null;
            Farbe = "";
            Beschreibung = "";
            return;
        }
        var farbe = Textfarben.Von(t.Farbe);
        var f = Math.Clamp((int)t.Groesse, 1, Grenzen.MeldungGroesseMax);
        var gewuenscht = grundschrift * f;
        // Platz für den Schatten rechts und unten (links und oben hält ihn der Rand der Vorlage, 6).
        var schatten = Math.Max(0, Math.Ceiling((Sprecherbild.UnschaerfeGrund + Sprecherbild.VersatzGrund) * gewuenscht / Sprecherbild.SchriftGrund) + 2 - 6);
        var verfuegbar = double.IsFinite(grenzeBreite) ? Math.Max(1, grenzeBreite - schatten) : double.PositiveInfinity;
        var schluessel = (t.Text, gewuenscht, verfuegbar);
        if (_gemessen != schluessel)
        {
            _gemessen = schluessel;
            _form = Formen(t.Text, gewuenscht, verfuegbar);
        }
        var (schrift, breite, zeilen) = _form;
        Text = t.Text;
        Farbe = farbe.Kennung;
        if (!ReferenceEquals(Pinsel, farbe.Pinsel)) Pinsel = farbe.Pinsel;
        if (Math.Abs(Schrift - schrift) > 0.05)
        {
            Schrift = schrift;
            Schatten = Schattenwurf.Effekt(schrift);
        }
        Breite = breite;
        Zeilen = zeilen;
        Rand = new Thickness(0, Math.Round(oben, 1), schatten, schatten);
        Beschreibung = "text " + farbe.Kennung + " (" + t.Text + ")";
        Da = true;
    }

    /// <summary>
    /// Schrift, Breite und Zeilenzahl für <paramref name="text"/>: in einer Zeile, wenn sie höchstens <see cref="ZeileEm"/> Schriftgrößen und
    /// <paramref name="verfuegbar"/> breit ist; sonst die schmalste Breite (bis <paramref name="verfuegbar"/>), bei der es zwei Zeilen bleiben, also zwei
    /// etwa gleich lange; reicht auch <paramref name="verfuegbar"/> nicht, je ein Zehntel kleiner bis <see cref="SchriftMin"/>. Gemessen mit derselben Schrift
    /// und demselben Umbruch wie die Vorlage (WPF bricht ein zu langes Wort mitten durch). Die Breite ist aufgerundet und hat eine Einheit Luft.
    /// </summary>
    internal static (double Schrift, double Breite, int Zeilen) Formen(string text, double schrift, double verfuegbar)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var s = Math.Max(SchriftMin, schrift); ; s = Math.Max(SchriftMin, s * 0.9))
        {
            var einzeilig = Messen(text, s, double.PositiveInfinity);
            var lang = einzeilig.WidthIncludingTrailingWhitespace;
            if (lang + 1 <= Math.Min(verfuegbar, ZeileEm * s)) return (s, Math.Min(Math.Ceiling(lang) + 1, verfuegbar), 1);
            var hoch = Math.Min(Math.Ceiling(lang) + 1, verfuegbar);
            if (ZeilenZahl(text, s, hoch, einzeilig.Height) <= 2)
            {
                // Die schmalste Breite mit höchstens zwei Zeilen: dann sind beide etwa gleich lang.
                var tief = Math.Max(1, (lang / 2) - 1);
                while (hoch - tief > 0.5)
                {
                    var mitte = (hoch + tief) / 2;
                    if (ZeilenZahl(text, s, mitte, einzeilig.Height) <= 2) hoch = mitte;
                    else tief = mitte;
                }
                var breite = Math.Min(Math.Ceiling(hoch) + 1, verfuegbar);
                return (s, breite, ZeilenZahl(text, s, breite, einzeilig.Height));
            }
            if (s <= SchriftMin) return (s, hoch, ZeilenZahl(text, s, hoch, einzeilig.Height));
        }
    }

    private static FormattedText Messen(string text, double schrift, double breite)
    {
        var f = new FormattedText(text, System.Globalization.CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight, Schriftart, schrift, Brushes.White, 1.0);
        if (double.IsFinite(breite)) f.MaxTextWidth = Math.Max(1, breite);
        return f;
    }

    /// <summary>Wie viele Zeilen <paramref name="text"/> bei dieser Breite braucht (Höhe durch die Höhe einer Zeile).</summary>
    internal static int ZeilenZahl(string text, double schrift, double breite, double zeilenhoehe) =>
        (int)Math.Round(Messen(text, schrift, breite).Height / Math.Max(1, zeilenhoehe));
}

/// <summary>
/// Ein Platz der Meldungen über dem Spiel: das zugeschnittene Bild in seiner Größe, die Zeile der Leitung darunter, ein Zusatz ("gemeldet") und beim
/// Countdown die Zeiten in den freien Feldern.
/// </summary>
public sealed partial class Meldeplatz(string schluessel) : ObservableObject
{
    /// <summary><see cref="Meldebild.Haupt"/>, <see cref="Meldebild.Zustand"/>, <see cref="Meldebild.Ampel"/> oder <see cref="Meldebild.Countdown"/>.</summary>
    public string Schluessel { get; } = schluessel;

    /// <summary>Das Motiv des Katalogs ("sammeln").</summary>
    [ObservableProperty]
    public partial string Motiv { get; private set; } = "";

    /// <summary>Der Name der Ressource, die gezeigt wird (mit Ausführung und Rückfall auf Schlicht).</summary>
    [ObservableProperty]
    public partial string? Ressource { get; private set; }

    [ObservableProperty]
    public partial ImageSource? Bild { get; private set; }

    [ObservableProperty]
    public partial double Breite { get; private set; }

    [ObservableProperty]
    public partial double Hoehe { get; private set; }

    /// <summary>Abstand nach rechts zum nächsten Platz.</summary>
    [ObservableProperty]
    public partial Thickness Abstand { get; private set; }

    /// <summary>Die freiwillige Zeile der Leitung unter der Grafik, sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? Zeile { get; private set; }

    /// <summary>Unter der Zeile, etwa "gemeldet" bei der Bereit-Abfrage; sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? Zusatz { get; private set; }

    [ObservableProperty]
    public partial double Schrift { get; private set; } = Meldebild.ZeileGrund;

    /// <summary>So breit darf die Zeile werden, bevor sie umbricht: mindestens das Bild, höchstens 2,5 mal so breit.</summary>
    [ObservableProperty]
    public partial double ZeileBreite { get; private set; }

    /// <summary>Der Schatten der Schrift wie in der Sprecheranzeige (Variante B), zur Schrift passend.</summary>
    [ObservableProperty]
    public partial System.Windows.Media.Effects.DropShadowEffect Schatten { get; private set; } = Schattenwurf.Effekt(Meldebild.ZeileGrund);

    /// <summary>Beim Countdown die Felder mit den Zeiten; sonst leer.</summary>
    public ObservableCollection<Meldefeld> Felder { get; } = [];

    /// <summary>Was Bildschirmleser und Tests lesen: "haupt sammeln (Hangar 2)", Zeiten in eckigen Klammern.</summary>
    [ObservableProperty]
    public partial string Text { get; private set; } = "";

    private string TextBauen() => Schluessel + " " + Motiv + (Zeile is { } z ? " (" + z + ")" : "") + (Zusatz is { } u ? " " + u : "")
        + (Felder.Count > 0 ? " [" + string.Join(" | ", Felder.Select(f => f.Text)) + "]" : "");

    internal void Setzen(Platzwunsch w, string? ressource, Meldungsgrafik? grafik, double breite, double hoehe, double schrift, double abstand)
    {
        Motiv = w.Motiv;
        if (!ReferenceEquals(Bild, grafik?.Bild)) Bild = grafik?.Bild;
        Ressource = grafik is null ? null : ressource;
        Breite = Math.Round(breite, 1);
        Hoehe = Math.Round(hoehe, 1);
        Abstand = new Thickness(0, 0, abstand, 0);
        Zeile = w.Zeile;
        Zusatz = w.Zusatz;
        if (Math.Abs(Schrift - schrift) > 0.05)
        {
            Schrift = schrift;
            Schatten = Schattenwurf.Effekt(schrift);
        }
        ZeileBreite = Math.Max(Breite, Math.Min(Breite * 2.5, 600));
        FelderSetzen(w, grafik);
        var text = TextBauen();
        if (string.Equals(text, Text, StringComparison.Ordinal)) return;
        Text = text;
    }

    private void FelderSetzen(Platzwunsch w, Meldungsgrafik? grafik)
    {
        if (w.Zeiten is not { } zeiten || grafik is null)
        {
            if (Felder.Count > 0) Felder.Clear();
            return;
        }
        Rect[] rechtecke = zeiten.Count >= 2 ? [Meldebild.FeldStartUm, Meldebild.FeldCountdown] : [Meldebild.FeldRestzeit];
        string[] namen = zeiten.Count >= 2 ? ["startum", "countdown"] : ["restzeit"];
        if (Felder.Count != rechtecke.Length || !Felder.Select(f => f.Name).SequenceEqual(namen, StringComparer.Ordinal))
        {
            Felder.Clear();
            foreach (var n in namen) Felder.Add(new Meldefeld(n));
        }
        // Vom ganzen Bild (Bildpunkte des Originals) in den Zuschnitt, dann in Einheiten des Platzes.
        var a = grafik.Ausschnitt;
        var sx = a.Width > 0 ? Breite / a.Width : 0;
        var sy = a.Height > 0 ? Hoehe / a.Height : 0;
        for (var i = 0; i < Felder.Count; i++)
        {
            var r = rechtecke[i];
            Felder[i].Setzen(zeiten[Math.Min(i, zeiten.Count - 1)], new Rect((r.X - a.X) * sx, (r.Y - a.Y) * sy, r.Width * sx, r.Height * sy));
        }
    }
}

/// <summary>Ein freies Feld im Countdown-Bild mit seiner Zeit, weiß mit Schatten, so groß, dass sie hineinpasst.</summary>
public sealed partial class Meldefeld(string name) : ObservableObject
{
    /// <summary>"startum", "countdown" oder "restzeit".</summary>
    public string Name { get; } = name;

    [ObservableProperty]
    public partial string Text { get; private set; } = "";

    /// <summary>Lage im Platz: Rand links und oben.</summary>
    [ObservableProperty]
    public partial Thickness Rand { get; private set; }

    [ObservableProperty]
    public partial double Breite { get; private set; }

    [ObservableProperty]
    public partial double Hoehe { get; private set; }

    /// <summary>Schrift: zwei Drittel der Feldhöhe (zu breit wird sie in der Vorlage kleiner).</summary>
    [ObservableProperty]
    public partial double Schrift { get; private set; }

    [ObservableProperty]
    public partial System.Windows.Media.Effects.DropShadowEffect Schatten { get; private set; } = Schattenwurf.Effekt(Meldebild.ZeileGrund);

    internal void Setzen(string text, Rect r)
    {
        Text = text;
        Rand = new Thickness(Math.Round(r.X, 1), Math.Round(r.Y, 1), 0, 0);
        Breite = Math.Max(1, Math.Round(r.Width, 1));
        Hoehe = Math.Max(1, Math.Round(r.Height, 1));
        var schrift = Math.Max(6, Math.Round(r.Height * 0.66, 1));
        if (Math.Abs(Schrift - schrift) > 0.05)
        {
            Schrift = schrift;
            Schatten = Schattenwurf.Effekt(schrift);
        }
    }
}

/// <summary>
/// Der Schatten weißer Schrift über dem Spiel wie in der Sprecheranzeige, Variante B vom 15.10.2026 (<see cref="Sprecherbild"/>): schwarz, volle
/// Deckkraft, nach rechts unten; Unschärfe 3,5 und Versatz 1,5 bei Schrift 26, mit der Schrift skaliert. Eingefroren und je Größe gemerkt.
/// </summary>
public static class Schattenwurf
{
    private static readonly Dictionary<int, System.Windows.Media.Effects.DropShadowEffect> Gemerkt = [];
    private static readonly Lock Sperre = new();

    public static System.Windows.Media.Effects.DropShadowEffect Effekt(double schrift)
    {
        var schluessel = (int)Math.Round(Math.Max(1, schrift) * 2);
        lock (Sperre)
        {
            if (Gemerkt.TryGetValue(schluessel, out var e)) return e;
            var f = schluessel / 2.0 / Sprecherbild.SchriftGrund;
            e = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = 1,
                Direction = 315,
                BlurRadius = Sprecherbild.UnschaerfeGrund * f,
                ShadowDepth = Sprecherbild.VersatzGrund * f,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
            };
            e.Freeze();
            Gemerkt[schluessel] = e;
            return e;
        }
    }
}
