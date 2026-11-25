// Glasdesign - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Media;

namespace Glasdesign;

/// <summary>Teile der Zeichnung, für Tests und Messungen einzeln abschaltbar.</summary>
[Flags]
internal enum Leuchtteile
{
    Keine = 0,
    Spur = 1,
    Schatten = 2,
    Knauf = 4,
    Zeichen = 8,
    Fokus = 16,
    Alle = Spur | Schatten | Knauf | Zeichen | Fokus,
}

/// <summary>
/// Der JG-Schalter (Joystick Guardian) im Glaslook von Glas 3 (Muster 3b, Sarevocs Wahl vom 02.10.2026). Der Knauf ist eine
/// gewölbte Glaslinse mit Haarlinie, Spiegelungen und punktuellem Glanzlicht (<see cref="Glanzbild"/>) und einem Zeichen
/// darin, die Spur eine gläserne Rinne mit Haarlinie, niedriger als der Knauf. Leuchtend fließt die Leuchtfarbe vom Knauf in
/// die Rinne.
/// <para>
/// Die Bedeutungen sind die des Originals und bleiben getrennt: <see cref="IstAn"/> ist die Stellung (eingestellt oder
/// angezeigt), <see cref="Stellung"/> die gezeichnete Knaufposition (animiert die Vorlage), <see cref="Leuchtet"/> das
/// bestätigte Leuchten (null: wie <see cref="IstAn"/>), <see cref="Wartet"/> eine noch nicht bestätigte Änderung (gelber
/// Rand, nie Leuchten). Leuchten also nur, wenn bestätigt, nicht wartend und bedienbar (<see cref="Leuchtend"/>).
/// </para>
/// Reine Zeichnung: nicht treffbar, nicht fokussierbar, liest nichts. Bedienung, Fokus und Bindungen gehören der äußeren
/// CheckBox (Stil Glas.Leuchtschalter). Alle Kanten liegen auf ganzen Gerätepixeln (<see cref="Leuchtgeometrie"/>).
/// </summary>
public sealed class Leuchtschalter : FrameworkElement
{
    public static readonly DependencyProperty StellungProperty = DependencyProperty.Register(
        nameof(Stellung), typeof(double), typeof(Leuchtschalter),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, null,
            (_, wert) => wert is double s && double.IsFinite(s) ? Math.Clamp(s, 0, 1) : 0d));

    public static readonly DependencyProperty IstAnProperty = Wert<bool?>(nameof(IstAn), false);
    public static readonly DependencyProperty LeuchtetProperty = Wert<bool?>(nameof(Leuchtet), null);
    public static readonly DependencyProperty WartetProperty = Wert(nameof(Wartet), false);
    public static readonly DependencyProperty UeberfahrenProperty = Saumwert(nameof(Ueberfahren));
    public static readonly DependencyProperty GedruecktProperty = Wert(nameof(Gedrueckt), false);
    public static readonly DependencyProperty FokusProperty = Saumwert(nameof(Fokus));
    public static readonly DependencyProperty SaumFarbeProperty = Wert(nameof(SaumFarbe), Colors.Transparent);

    /// <summary>Gezeichnete Stärke des Leuchtsaums um Knauf und Spur; blendet weich zum Ziel (<see cref="Leuchtsaum.Ziel"/>).</summary>
    public static readonly DependencyProperty SaumStaerkeProperty = Wert(nameof(SaumStaerke), 0d);
    public static readonly DependencyProperty LeuchtfarbeProperty = Wert(nameof(Leuchtfarbe), Color.FromRgb(0x17, 0xEE, 0xFF));
    public static readonly DependencyProperty ZeichenProperty = Wert<Geometry?>(nameof(Zeichen), null);
    public static readonly DependencyProperty SpiegelFarbeProperty = Wert(nameof(SpiegelFarbe), Colors.White);

    /// <summary>Das Glanzprofil der Farbstellung; ein Wechsel leert den gemerkten Umgebungsschatten.</summary>
    public static readonly DependencyProperty GlanzprofilProperty = DependencyProperty.Register(
        nameof(Glanzprofil), typeof(Glanzprofil), typeof(Leuchtschalter),
        new FrameworkPropertyMetadata(Glasdesign.Glanzprofil.Standard, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) =>
        {
            ((Leuchtschalter)d)._umgebung = null;
            ((Leuchtschalter)d).SaumSetzen();
        }));

    /// <summary>Der Bauplan der Farbstellung (Feld, Knauf, Spur, Zeichen, Schatten); ein Wechsel setzt die Größe neu.</summary>
    public static readonly DependencyProperty BauplanProperty = DependencyProperty.Register(
        nameof(Bauplan), typeof(Bauplan), typeof(Leuchtschalter),
        new FrameworkPropertyMetadata(Glasdesign.Bauplan.Standard, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) =>
        {
            var l = (Leuchtschalter)d;
            l._umgebung = null;
            l.Width = l.Bauplan.LeuchtBreite;
            l.Height = l.Bauplan.LeuchtHoehe;
        }));

    public Bauplan Bauplan
    {
        get => GetValue(BauplanProperty) as Bauplan ?? Glasdesign.Bauplan.Standard;
        set => SetValue(BauplanProperty, value);
    }

    public Glanzprofil Glanzprofil
    {
        get => GetValue(GlanzprofilProperty) as Glanzprofil ?? Glasdesign.Glanzprofil.Standard;
        set => SetValue(GlanzprofilProperty, value);
    }

    public static readonly DependencyProperty SchattenProperty = Glas.SchattenProperty.AddOwner(
        typeof(Leuchtschalter), new FrameworkPropertyMetadata(Schattenstufe.Voll, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SchattenFarbeProperty = DependencyProperty.Register(
        nameof(SchattenFarbe), typeof(Color), typeof(Leuchtschalter),
        new FrameworkPropertyMetadata(Colors.Black, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Leuchtschalter)d)._umgebung = null));

    public static readonly DependencyProperty KanteProperty = Pinsel(nameof(Kante));
    public static readonly DependencyProperty KanteUeberProperty = Pinsel(nameof(KanteUeber));
    public static readonly DependencyProperty KanteGesperrtProperty = Pinsel(nameof(KanteGesperrt));
    public static readonly DependencyProperty RinnenkanteProperty = Pinsel(nameof(Rinnenkante));
    public static readonly DependencyProperty WartekanteProperty = Pinsel(nameof(Wartekante));
    public static readonly DependencyProperty KnaufKoerperProperty = Pinsel(nameof(KnaufKoerper));
    public static readonly DependencyProperty KnaufUeberProperty = Pinsel(nameof(KnaufUeber));
    public static readonly DependencyProperty KnaufGedruecktProperty = Pinsel(nameof(KnaufGedrueckt));
    public static readonly DependencyProperty KnaufGesperrtProperty = Pinsel(nameof(KnaufGesperrt));
    public static readonly DependencyProperty RinneProperty = Pinsel(nameof(Rinne));
    public static readonly DependencyProperty RinneGesperrtProperty = Pinsel(nameof(RinneGesperrt));
    public static readonly DependencyProperty InnenschattenProperty = Pinsel(nameof(Innenschatten));
    public static readonly DependencyProperty LichtfleckProperty = Pinsel(nameof(Lichtfleck));
    public static readonly DependencyProperty KontaktschattenProperty = Pinsel(nameof(Kontaktschatten));
    public static readonly DependencyProperty ZeichenAusProperty = Pinsel(nameof(ZeichenAus));
    public static readonly DependencyProperty ZeichenGesperrtProperty = Pinsel(nameof(ZeichenGesperrt));
    public static readonly DependencyProperty FokusPinselProperty = Pinsel(nameof(FokusPinsel));

    private static DependencyProperty Wert<T>(string name, T vorgabe) => DependencyProperty.Register(
        name, typeof(T), typeof(Leuchtschalter), new FrameworkPropertyMetadata(vorgabe, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Pinsel(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Leuchtschalter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Saumwert(string name) => DependencyProperty.Register(
        name, typeof(bool), typeof(Leuchtschalter), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Leuchtschalter)d).SaumSetzen()));

    private Leuchtteile _teile = Leuchtteile.Alle;
    private (Brush Normal, Brush Gedrueckt)? _umgebung;
    private Glanzbild? _knaufBild;
    private Glanzbild? _spurBild;
    private Saumbild? _saumBild;

    static Leuchtschalter()
    {
        IsEnabledProperty.OverrideMetadata(typeof(Leuchtschalter), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Leuchtschalter)d).SaumSetzen()));
    }

    private void SaumSetzen() => Saumblende.Setzen(this, SaumStaerkeProperty, Leuchtsaum.Ziel(Glanzprofil, Ueberfahren, Fokus, false, IsEnabled), Glanzprofil.LeuchtsaumEinblenden);

    public Leuchtschalter()
    {
        Width = Glasdesign.Bauplan.Standard.LeuchtBreite;
        Height = Glasdesign.Bauplan.Standard.LeuchtHoehe;
        Focusable = false;
        IsHitTestVisible = false;
        SetResourceReference(KanteProperty, Schluessel.Kante);
        SetResourceReference(KanteUeberProperty, Schluessel.KanteUeber);
        SetResourceReference(KanteGesperrtProperty, Schluessel.KanteGesperrt);
        SetResourceReference(RinnenkanteProperty, Schluessel.LeuchtRinneKante);
        SetResourceReference(WartekanteProperty, Schluessel.LeuchtWartet);
        SetResourceReference(KnaufKoerperProperty, Schluessel.LeuchtKnauf);
        SetResourceReference(KnaufUeberProperty, Schluessel.LeuchtKnaufUeber);
        SetResourceReference(KnaufGedruecktProperty, Schluessel.LeuchtKnaufGedrueckt);
        SetResourceReference(KnaufGesperrtProperty, Schluessel.LeuchtKnaufGesperrt);
        SetResourceReference(RinneProperty, Schluessel.LeuchtRinne);
        SetResourceReference(RinneGesperrtProperty, Schluessel.LeuchtRinneGesperrt);
        SetResourceReference(InnenschattenProperty, Schluessel.Innenschatten);
        SetResourceReference(LichtfleckProperty, Schluessel.LeuchtLichtfleck);
        SetResourceReference(KontaktschattenProperty, Schluessel.Kontaktschatten);
        SetResourceReference(SchattenFarbeProperty, Schluessel.SchattenFarbe);
        SetResourceReference(SpiegelFarbeProperty, Schluessel.SpiegelFarbe);
        SetResourceReference(GlanzprofilProperty, Schluessel.Glanzprofil);
        SetResourceReference(BauplanProperty, Schluessel.Bauplan);
        SetResourceReference(ZeichenAusProperty, Schluessel.LeuchtZeichen);
        SetResourceReference(ZeichenGesperrtProperty, Schluessel.ZeichenGesperrt);
        SetResourceReference(FokusPinselProperty, Schluessel.Fokus);
        SetResourceReference(SaumFarbeProperty, Schluessel.LeuchtsaumFarbe);
    }

    /// <summary>Gezeichnete Knaufposition zwischen 0 (aus, links) und 1 (an, rechts); die Vorlage animiert sie (220 ms).</summary>
    public double Stellung { get => (double)GetValue(StellungProperty); set => SetValue(StellungProperty, value); }

    /// <summary>Stellung des Schalters (eingestellt oder angezeigt). Allein kein Nachweis einer aktiven Funktion.</summary>
    public bool? IstAn { get => (bool?)GetValue(IstAnProperty); set => SetValue(IstAnProperty, value); }

    /// <summary>Bestätigtes Leuchten. false unterdrückt es auch bei <see cref="IstAn"/>; null folgt <see cref="IstAn"/>.</summary>
    public bool? Leuchtet { get => (bool?)GetValue(LeuchtetProperty); set => SetValue(LeuchtetProperty, value); }

    /// <summary>Änderung wartet auf Bestätigung: gelber Rand an der Spur, kein Leuchten.</summary>
    public bool Wartet { get => (bool)GetValue(WartetProperty); set => SetValue(WartetProperty, value); }

    public bool Ueberfahren { get => (bool)GetValue(UeberfahrenProperty); set => SetValue(UeberfahrenProperty, value); }

    public bool Gedrueckt { get => (bool)GetValue(GedruecktProperty); set => SetValue(GedruecktProperty, value); }

    /// <summary>Tastaturfokus: Leuchtsaum um Knauf und Spur (die Vorlage setzt ihn über <see cref="Glas.TastaturfokusProperty"/>).</summary>
    public bool Fokus { get => (bool)GetValue(FokusProperty); set => SetValue(FokusProperty, value); }

    /// <summary>Ton des Leuchtsaums (<see cref="Schluessel.LeuchtsaumFarbe"/>).</summary>
    public Color SaumFarbe { get => (Color)GetValue(SaumFarbeProperty); set => SetValue(SaumFarbeProperty, value); }

    public double SaumStaerke { get => (double)GetValue(SaumStaerkeProperty); set => SetValue(SaumStaerkeProperty, value); }

    /// <summary>Farbe des Leuchtens; der aufrufende Bereich bestimmt sie, die Zeichnung leitet nichts daraus ab.</summary>
    public Color Leuchtfarbe { get => (Color)GetValue(LeuchtfarbeProperty); set => SetValue(LeuchtfarbeProperty, value); }

    /// <summary>Zeichen im Knauf (<see cref="Leuchtzeichen"/>); null = leere Glaslinse.</summary>
    public Geometry? Zeichen { get => (Geometry?)GetValue(ZeichenProperty); set => SetValue(ZeichenProperty, value); }

    /// <summary>Farbton der Spiegelungen auf dem Knauf (weiß).</summary>
    public Color SpiegelFarbe { get => (Color)GetValue(SpiegelFarbeProperty); set => SetValue(SpiegelFarbeProperty, value); }

    public Schattenstufe Schatten { get => (Schattenstufe)GetValue(SchattenProperty); set => SetValue(SchattenProperty, value); }
    public Color SchattenFarbe { get => (Color)GetValue(SchattenFarbeProperty); set => SetValue(SchattenFarbeProperty, value); }
    public Brush? Kante { get => (Brush?)GetValue(KanteProperty); set => SetValue(KanteProperty, value); }
    public Brush? KanteUeber { get => (Brush?)GetValue(KanteUeberProperty); set => SetValue(KanteUeberProperty, value); }
    public Brush? KanteGesperrt { get => (Brush?)GetValue(KanteGesperrtProperty); set => SetValue(KanteGesperrtProperty, value); }
    public Brush? Rinnenkante { get => (Brush?)GetValue(RinnenkanteProperty); set => SetValue(RinnenkanteProperty, value); }
    public Brush? Wartekante { get => (Brush?)GetValue(WartekanteProperty); set => SetValue(WartekanteProperty, value); }
    public Brush? KnaufKoerper { get => (Brush?)GetValue(KnaufKoerperProperty); set => SetValue(KnaufKoerperProperty, value); }
    public Brush? KnaufUeber { get => (Brush?)GetValue(KnaufUeberProperty); set => SetValue(KnaufUeberProperty, value); }
    public Brush? KnaufGedrueckt { get => (Brush?)GetValue(KnaufGedruecktProperty); set => SetValue(KnaufGedruecktProperty, value); }
    public Brush? KnaufGesperrt { get => (Brush?)GetValue(KnaufGesperrtProperty); set => SetValue(KnaufGesperrtProperty, value); }
    public Brush? Rinne { get => (Brush?)GetValue(RinneProperty); set => SetValue(RinneProperty, value); }
    public Brush? RinneGesperrt { get => (Brush?)GetValue(RinneGesperrtProperty); set => SetValue(RinneGesperrtProperty, value); }
    public Brush? Innenschatten { get => (Brush?)GetValue(InnenschattenProperty); set => SetValue(InnenschattenProperty, value); }
    public Brush? Lichtfleck { get => (Brush?)GetValue(LichtfleckProperty); set => SetValue(LichtfleckProperty, value); }
    public Brush? Kontaktschatten { get => (Brush?)GetValue(KontaktschattenProperty); set => SetValue(KontaktschattenProperty, value); }
    public Brush? ZeichenAus { get => (Brush?)GetValue(ZeichenAusProperty); set => SetValue(ZeichenAusProperty, value); }
    public Brush? ZeichenGesperrt { get => (Brush?)GetValue(ZeichenGesperrtProperty); set => SetValue(ZeichenGesperrtProperty, value); }
    public Brush? FokusPinsel { get => (Brush?)GetValue(FokusPinselProperty); set => SetValue(FokusPinselProperty, value); }

    /// <summary>Wie im Original: <c>(Leuchtet ?? IstAn == true) &amp;&amp; !Wartet &amp;&amp; IsEnabled</c>.</summary>
    public bool Leuchtend => (Leuchtet ?? (IstAn == true)) && !Wartet && IsEnabled;

    /// <summary>Maße bei der aktuellen Skalierung, für Tests und Messungen.</summary>
    public Leuchtmass Mass => Leuchtgeometrie.Berechnen(Glasgeometrie.Skala(this), Bauplan);

    /// <summary>Welche Teile gezeichnet werden (Tests messen damit einzelne Teile).</summary>
    internal Leuchtteile Teile
    {
        get => _teile;
        set
        {
            _teile = value;
            InvalidateVisual();
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        var m = Leuchtgeometrie.Berechnen(Glasgeometrie.Skala(this), Bauplan);
        var palette = Leuchtend ? Leuchtpalette.Fuer(Leuchtfarbe) : null;
        var knaufX = m.KnaufLinks(Stellung);
        if (_teile.HasFlag(Leuchtteile.Fokus) && SaumStaerke > 0) SaumZeichnen(dc, m);
        if (_teile.HasFlag(Leuchtteile.Spur)) SpurZeichnen(dc, m, palette, knaufX);
        if (_teile.HasFlag(Leuchtteile.Schatten) && IsEnabled && Schatten != Schattenstufe.Aus) SchattenZeichnen(dc, m, knaufX);
        if (_teile.HasFlag(Leuchtteile.Knauf) || _teile.HasFlag(Leuchtteile.Zeichen)) KnaufZeichnen(dc, m, palette, knaufX);
    }

    /// <summary>
    /// Gläserne Rinne: Haarlinie (wartend gelb), Rinne mit Innenschatten, schwacher Kantenspiegel; leuchtend fließt die
    /// Farbe vom Knauf hinein. Wie im Original bleibt "wartet" auch gesperrt sichtbar.
    /// </summary>
    private void SpurZeichnen(DrawingContext dc, Leuchtmass m, Leuchtpalette? palette, double knaufX)
    {
        var s = m.Skala;
        var f = new Glasflaeche(m.SpurLinks, m.SpurOben, m.SpurBreite, m.Spur, m.Spur / 2.0, s);
        _spurBild = Glanzbild.Holen(_spurBild, f, Glanzprofil, SpiegelFarbe, 0, null);
        var gesperrt = !IsEnabled;
        dc.DrawGeometry(gesperrt ? RinneGesperrt : Rinne, null, _spurBild.Koerper);
        if (!gesperrt)
        {
            // Die Rinne liegt tiefer als die Fläche: oben fällt der Schatten des Rands hinein.
            if (Innenschatten is { } innen) dc.DrawGeometry(Pinselwerk.Oben(innen, 0.5), null, _spurBild.Koerper);
            if (palette is not null)
            {
                dc.DrawGeometry(palette.RinneTon, null, _spurBild.Koerper);
                // Das Licht fließt aus dem Knauf in die Rinne: am Knauf am hellsten, nach außen auslaufend.
                var innenhoehe = m.Spur - 2.0 * m.Haarlinie;
                var quelle = new Point((knaufX + m.Knauf / 2.0) / s, (m.SpurOben + m.Spur - m.Haarlinie) / s);
                dc.DrawGeometry(Pinselwerk.Strahl(quelle, (m.SpurBreite - 2.0 * m.Haarlinie) * 0.75 / s, innenhoehe * 0.9 / s, palette.StrahlFarbe), null, _spurBild.Koerper);
            }
        }
        if (Wartet) dc.DrawGeometry(Wartekante, null, f.Ring(1, 2));
        dc.DrawGeometry(Wartet ? Wartekante : gesperrt ? KanteGesperrt : Rinnenkante, null, _spurBild.Haar);
    }

    /// <summary>
    /// Umgebungsschatten (Tiefe 7 %, Unschärfe 50 % der Höhe, als weicher Verlauf statt Effekt) und Kontaktschatten (flach,
    /// knapp unter dem Knauf). Die Deckung steckt in den Pinseln, nicht in PushOpacity.
    /// </summary>
    private void SchattenZeichnen(DrawingContext dc, Leuchtmass m, double knaufX)
    {
        var s = m.Skala;
        var d = m.Knauf / s;
        var links = knaufX / s;
        var oben = m.KnaufOben / s;
        var gedrueckt = Gedrueckt;
        if (Schatten == Schattenstufe.Voll)
        {
            var plan = Bauplan;
            var tiefe = d * (gedrueckt ? plan.SchattenTiefeGedrueckt : plan.SchattenTiefe);
            var weich = d * (gedrueckt ? plan.SchattenUnschaerfeGedrueckt : plan.SchattenUnschaerfe);
            var r = d / 2 + weich * 0.6;
            var p = Glanzprofil;
            _umgebung ??= (Umgebung(SchattenFarbe, p.Schatten, plan.SchattenUnschaerfe), Umgebung(SchattenFarbe, p.Schatten * 0.65, plan.SchattenUnschaerfeGedrueckt));
            dc.DrawEllipse(gedrueckt ? _umgebung.Value.Gedrueckt : _umgebung.Value.Normal, null, new Point(links + d / 2, oben + d / 2 + tiefe), r, r);
        }
        if (Kontaktschatten is null) return;
        var k = Bauplan;
        var (my, bw, bh, deckung) = gedrueckt
            ? (k.KontaktLageGedrueckt, k.KontaktBreiteGedrueckt, k.KontaktHoeheGedrueckt, k.KontaktDeckungGedrueckt)
            : (k.KontaktLage, k.KontaktBreite, k.KontaktHoehe, k.KontaktDeckung);
        dc.DrawEllipse(Pinselwerk.Gedaempft(Kontaktschatten, deckung), null, new Point(links + d / 2, oben + d * my), d * bw / 2, d * bh / 2);
    }

    /// <summary>Weicher runder Schatten: deckender Kern bis zum Knaufrand minus Unschärfe, dann auslaufend.</summary>
    private static RadialGradientBrush Umgebung(Color farbe, double deckung, double weich)
    {
        // Kern und Radius wie in SchattenZeichnen, als Anteil des Radius: hängt nur vom Unschärfe-Anteil ab, nicht von der Größe.
        var kern = (0.5 - weich * 0.35) / (0.5 + weich * 0.6);
        var a = (byte)Math.Round(farbe.A * deckung);
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Color.FromArgb(a, farbe.R, farbe.G, farbe.B), 0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(a, farbe.R, farbe.G, farbe.B), kern));
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a / 3), farbe.R, farbe.G, farbe.B), kern + (1 - kern) * 0.45));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, farbe.R, farbe.G, farbe.B), 1));
        b.Freeze();
        return b;
    }

    /// <summary>Gewölbte Glaslinse mit Haarlinie, Spiegelungen und Zeichen. Das Leuchten des Zeichens bleibt im Glaskörper.</summary>
    private void KnaufZeichnen(DrawingContext dc, Leuchtmass m, Leuchtpalette? palette, double knaufX)
    {
        var s = m.Skala;
        var f = new Glasflaeche(knaufX, m.KnaufOben, m.Knauf, m.Knauf, m.Knauf / 2.0, s);
        var mitte = f.Dip(m.Knauf / 2.0, m.Knauf / 2.0);
        var gesperrt = !IsEnabled;
        var ueber = Ueberfahren && !gesperrt;
        var gedrueckt = Gedrueckt && !gesperrt;
        var profil = Glanzprofil;
        var staerke = (gedrueckt ? profil.Gedrueckt : ueber ? profil.Ueberfahren : 1.0) * 0.85;
        // Das Bodenleuchten des Glanzprofils tönt in die Leuchtfarbe, solange der Schalter leuchtet; sonst bleibt es im Ton
        // der Spiegelungen, damit ein dunkler Schalter nicht farbig wirkt.
        _knaufBild = Glanzbild.Holen(_knaufBild, f, profil, SpiegelFarbe, staerke, palette?.Lichtfleck ?? Lichtfleck, palette is null ? null : Leuchtfarbe);
        var rk = (m.Knauf / 2.0 - m.Haarlinie) / s;
        var knauf = _teile.HasFlag(Leuchtteile.Knauf);
        if (knauf)
        {
            dc.DrawGeometry(gesperrt ? KnaufGesperrt : gedrueckt ? KnaufGedrueckt : ueber ? KnaufUeber : KnaufKoerper, null, _knaufBild.Koerper);
            dc.DrawGeometry(gesperrt ? KanteGesperrt : ueber ? KanteUeber : Kante, null, _knaufBild.Haar);
            if (!gesperrt)
            {
                if (gedrueckt && Innenschatten is { } innen) dc.DrawGeometry(Pinselwerk.Oben(innen, 0.45), null, _knaufBild.Koerper);
                if (palette is not null) dc.DrawEllipse(palette.Halo, null, mitte, rk, rk * 0.85);
                _knaufBild.Zeichnen(dc);
            }
        }
        if (_teile.HasFlag(Leuchtteile.Zeichen) && Zeichen is { } zeichen && rk > 0)
        {
            if (palette is not null) dc.PushClip(_knaufBild.Koerper);
            ZeichenZeichnen(dc, m, zeichen, mitte, palette, gesperrt, gedrueckt);
            if (palette is not null) dc.Pop();
        }
    }

    /// <summary>
    /// Zeichen im Kasten von 250 x 83 Einheiten, mittig auf ganzen Pixeln; gedrückt eine Linie tiefer. Leuchtend wie im
    /// Original: elf weiche Striche (66 bis 6 Einheiten, Deckung 70 %), darüber die Füllung von hell nach satt.
    /// </summary>
    private void ZeichenZeichnen(DrawingContext dc, Leuchtmass m, Geometry zeichen, Point mitte, Leuchtpalette? palette, bool gesperrt, bool gedrueckt)
    {
        var s = m.Skala;
        var breite = m.Zeichen / s;
        var g = breite / Leuchtgeometrie.ZeichenEinheitenBreite;
        var hoehe = Leuchtgeometrie.ZeichenEinheitenHoehe * g;
        var tiefer = gedrueckt ? Glasgeometrie.Linie(1, s) / s : 0;
        dc.PushTransform(new MatrixTransform(g, 0, 0, g, mitte.X - breite / 2, mitte.Y - hoehe / 2 + tiefer));
        if (gesperrt)
        {
            dc.DrawGeometry(ZeichenGesperrt, null, zeichen);
        }
        else if (palette is not null)
        {
            foreach (var stift in palette.Schein) dc.DrawGeometry(null, stift, zeichen);
            dc.DrawGeometry(palette.ZeichenFuellung, palette.ZeichenStift, zeichen);
        }
        else
        {
            // Ohne die dunkle Haarlinie des Originals (0,14 DIP, sichtbar erst ab etwa 400 %): Sie dunkelte die schräge Ecke
            // unten am J so weit ab, dass das Zeichen bei 125 % an Pixeln gemessen links 1 Pixel weiter innen begann.
            dc.DrawGeometry(ZeichenAus, null, zeichen);
        }
        dc.Pop();
    }

    /// <summary>Leuchtsaum wie <see cref="Leuchtsaum"/> um die Pille aus Knauf und Spur, unter allem anderen.</summary>
    private void SaumZeichnen(DrawingContext dc, Leuchtmass m)
    {
        var f = new Glasflaeche(m.KnaufLinksAus, m.KnaufOben, m.KnaufLinksAn + m.Knauf - m.KnaufLinksAus, m.Knauf, m.Knauf / 2.0, m.Skala);
        _saumBild = Saumbild.Holen(_saumBild, f, Glanzprofil, SaumFarbe, SaumStaerke);
        _saumBild.Zeichnen(dc);
    }
}

/// <summary>
/// Aus einer Leuchtfarbe abgeleitete Pinsel und Stifte, eingefroren und geteilt. Der Vorrat ist begrenzt (16 Farben, dann
/// neu), damit beliebige Farben ihn nicht unbegrenzt wachsen lassen.
/// </summary>
internal sealed class Leuchtpalette
{
    private const int Hoechstens = 16;
    private static readonly Dictionary<Color, Leuchtpalette> Vorrat = [];
    private static readonly Lock Sperre = new();

    private Leuchtpalette(Color farbe)
    {
        Farbe = farbe;
        RinneTon = Senkrecht(Alpha(farbe, 0x1C), Alpha(farbe, 0x48));
        StrahlFarbe = Alpha(Mischen(farbe, Colors.White, 0.2), 0x80);
        var halo = new RadialGradientBrush();
        halo.GradientStops.Add(new GradientStop(Alpha(farbe, 98), 0));
        halo.GradientStops.Add(new GradientStop(Alpha(farbe, 35), 0.5));
        halo.GradientStops.Add(new GradientStop(Alpha(farbe, 0), 1));
        halo.Freeze();
        Halo = halo;
        var fleck = new RadialGradientBrush(Alpha(Mischen(farbe, Colors.White, 0.55), 0x6C), Alpha(farbe, 0));
        fleck.Freeze();
        Lichtfleck = fleck;
        var (hell, flaeche) = Zeichenfarben(farbe);
        ZeichenFuellung = Senkrecht(hell, flaeche);
        var stift = new Pen(new SolidColorBrush(hell), 1.6);
        stift.Freeze();
        ZeichenStift = stift;
        var schein = new List<Pen>();
        for (var breite = 66; breite >= 6; breite -= 6)
        {
            var p = new Pen(new SolidColorBrush(Alpha(farbe, (byte)Math.Round((7 + (66 - breite) / 3) * 0.70))), breite) { LineJoin = PenLineJoin.Round };
            p.Freeze();
            schein.Add(p);
        }
        Schein = schein;
    }

    public Color Farbe { get; }
    public Brush RinneTon { get; }
    public Color StrahlFarbe { get; }
    public Brush Halo { get; }
    public RadialGradientBrush Lichtfleck { get; }
    public Brush ZeichenFuellung { get; }
    public Pen ZeichenStift { get; }
    public IReadOnlyList<Pen> Schein { get; }

    public static Leuchtpalette Fuer(Color farbe)
    {
        lock (Sperre)
        {
            if (Vorrat.TryGetValue(farbe, out var p)) return p;
            if (Vorrat.Count >= Hoechstens) Vorrat.Clear();
            p = new Leuchtpalette(farbe);
            Vorrat[farbe] = p;
            return p;
        }
    }

    /// <summary>Helle und satte Zeichenfarbe; für die vier JG-Farben die abgestimmten Werte des Originals.</summary>
    internal static (Color Hell, Color Flaeche) Zeichenfarben(Color farbe)
    {
        if (farbe == Color.FromRgb(0x26, 0xFF, 0x91)) return (Color.FromRgb(0xB1, 0xFF, 0xD0), Color.FromRgb(0x13, 0xEF, 0x80));
        if (farbe == Color.FromRgb(0x17, 0xEE, 0xFF)) return (Color.FromRgb(0xC0, 0xFB, 0xFF), Color.FromRgb(0x20, 0xEA, 0xF4));
        if (farbe == Color.FromRgb(0xFF, 0x42, 0x33)) return (Color.FromRgb(0xFF, 0xC0, 0xBC), Color.FromRgb(0xFF, 0x43, 0x39));
        if (farbe == Color.FromRgb(0xFF, 0xD3, 0x3D)) return (Color.FromRgb(0xFF, 0xF1, 0xBA), Color.FromRgb(0xFF, 0xD3, 0x3D));
        return (Color.FromArgb(farbe.A, (byte)(farbe.R + (255 - farbe.R) * 0.7), (byte)(farbe.G + (255 - farbe.G) * 0.7), (byte)(farbe.B + (255 - farbe.B) * 0.7)), farbe);
    }

    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static Color Mischen(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static LinearGradientBrush Senkrecht(Color oben, Color unten)
    {
        var b = new LinearGradientBrush(oben, unten, new Point(0.5, 0), new Point(0.5, 1));
        b.Freeze();
        return b;
    }
}
