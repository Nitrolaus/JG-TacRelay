// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client.Design;
using Funk.Client.Fenster;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Gemeinsame Prüfungen an gezeichneten Ansichten (<see cref="Bildlauf"/>): was zu sehen ist, welche Texte dastehen und ob
/// die Kontrastregeln aus ENTWURF.md halten (Text 4,5:1 gegen die Fläche darunter, Rand der Textfelder und Fokus 3:1).
/// </summary>
internal static class Ansichtspruefung
{
    /// <summary>
    /// Zeichnet das Hauptfenster so hoch, dass der ganze Bereich ohne Bildlauf hineinpasst. Seit 0.8.0 rollt die Verwaltung selbst
    /// (VerwaltungBildlauf, die Leiste des Entwurfs bleibt darunter); dann zählt ihr Bildlauf statt dem der Bereiche.
    /// </summary>
    public static RenderTargetBitmap Ganz(Hauptfenster haupt, double breite = 1120)
    {
        _ = Bildlauf.Zeichnen(haupt, 1, new Size(breite, 900));
        var laeufe = Bildlauf.Nachfahren<ScrollViewer>(Bildlauf.Wurzel(haupt)).ToList();
        var lauf = laeufe.FirstOrDefault(s => s.Name == "VerwaltungBildlauf" && ZuSehen(s))
            ?? laeufe.First(s => s.Content is StackPanel p && p.Children.OfType<ContentControl>().Any());
        var mehr = Math.Max(0, lauf.ExtentHeight - lauf.ViewportHeight);
        var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(breite, Math.Ceiling(900 + mehr + 2)));
        Pruefe.Wahr(lauf.ExtentHeight <= lauf.ViewportHeight + 1, "der ganze Bereich passt ins Bild");
        return bild;
    }

    /// <summary>Ob ein sichtbarer Text im Baum <paramref name="text"/> enthält.</summary>
    public static bool Sichtbar(DependencyObject wurzel, string text) =>
        Bildlauf.Nachfahren<TextBlock>(wurzel).Any(t => ZuSehen(t) && Inhalt(t).Contains(text, StringComparison.Ordinal))
        || Bildlauf.Nachfahren<TextBox>(wurzel).Any(t => ZuSehen(t) && t.Text.Contains(text, StringComparison.Ordinal));

    /// <summary>
    /// Kontrastregeln an dem, was gezeichnet ist: jeder sichtbare Text (auch einzelne Runs mit eigener Farbe) gegen die
    /// nächste deckende Fläche darunter mindestens 4,5:1, der Rand jedes Textfelds gegen seine Umgebung und der Fokus im
    /// Akzent mindestens 3:1. Ausgenommen sind abgeschaltete Bedienelemente (WCAG 1.4.3, inaktive Komponenten). Text auf einem
    /// Glasknopf trägt die Schrift des Knopfs (Verlauf von Glasdesign); dafür misst OberflaecheGlasTests den Kontrast an den
    /// gezeichneten Pixeln, hier wird nur verlangt, dass er keine eigene Farbe setzt.
    /// </summary>
    public static int KontrastPruefen(DependencyObject wurzel, Farbsatz satz, string was, int mindestens = 20)
    {
        var geprueft = 0;
        foreach (var t in Bildlauf.Nachfahren<TextBlock>(wurzel))
        {
            if (!ZuSehen(t) || string.IsNullOrWhiteSpace(Inhalt(t)) || !Aktiv(t)) continue;
            if (Glasknopf(t) is { } knopf)
            {
                Pruefe.Wahr(ReferenceEquals(t.Foreground, knopf.Foreground) && t.Inlines.OfType<Run>().All(r => ReferenceEquals(r.Foreground, knopf.Foreground)),
                    $"{was}: Text \"{Kurz(Inhalt(t))}\" auf Glas mit eigener Farbe");
                geprueft++;
                continue;
            }
            var grund = Flaeche(t);
            var farben = t.Inlines.Count == 0
                ? [Farbe(t.Foreground)]
                : t.Inlines.OfType<Run>().Where(r => !string.IsNullOrWhiteSpace(r.Text)).Select(r => Farbe(r.Foreground)).ToList();
            foreach (var f in farben)
            {
                var v = Kontrast.Verhaeltnis(f, grund);
                Pruefe.Wahr(v >= 4.5, $"{was}: Text \"{Kurz(Inhalt(t))}\" {f} auf {grund} nur {v:0.00}:1");
                geprueft++;
            }
        }
        foreach (var feld in Bildlauf.Nachfahren<TextBox>(wurzel).Where(ZuSehen))
        {
            if (!Aktiv(feld)) continue;
            var innen = Farbe(feld.Background);
            Pruefe.Wahr(Kontrast.Verhaeltnis(Farbe(feld.Foreground), innen) >= 4.5, $"{was}: Schrift im Textfeld {feld.Name}");
            var aussen = Flaeche(feld);
            Pruefe.Wahr(Kontrast.Verhaeltnis(Farbe(feld.BorderBrush), aussen) >= 3, $"{was}: Rand des Textfelds {feld.Name} gegen {aussen}");
            Pruefe.Wahr(Kontrast.Verhaeltnis(satz.Akzent, aussen) >= 3 && Kontrast.Verhaeltnis(satz.Akzent, innen) >= 3, $"{was}: Fokusrand im Akzent");
        }
        Pruefe.Wahr(geprueft >= mindestens, was + ": genug Text geprüft (" + geprueft + ")");
        return geprueft;
    }

    /// <summary>
    /// Gezeichnet und nicht verborgen: Ohne Fensterhandle bleibt <see cref="UIElement.IsVisible"/> immer falsch, darum über die
    /// Sichtbarkeit aller Vorfahren bis unter das Fenster (ein nie gezeigtes Fenster steht selbst auf Collapsed) und die
    /// belegte Fläche.
    /// </summary>
    public static bool ZuSehen(FrameworkElement e)
    {
        if (e.ActualWidth <= 0 || e.ActualHeight <= 0) return false;
        for (DependencyObject? d = e; d is not null and not Window; d = VisualTreeHelper.GetParent(d))
            if (d is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    /// <summary>Der Text eines TextBlock; bei Runs deren Inhalt, denn <see cref="TextBlock.Text"/> kennt gebundene Runs nicht.</summary>
    public static string Inhalt(TextBlock t) => t.Inlines.Count == 0 ? t.Text : string.Concat(t.Inlines.OfType<Run>().Select(r => r.Text));

    /// <summary>Der Glasknopf, auf dem <paramref name="d"/> steht (ein Knopf mit Glaskörper in seiner Vorlage), oder null.</summary>
    public static System.Windows.Controls.Primitives.ButtonBase? Glasknopf(DependencyObject d)
    {
        for (var e = VisualTreeHelper.GetParent(d); e is not null; e = VisualTreeHelper.GetParent(e))
            if (e is System.Windows.Controls.Primitives.ButtonBase b)
                return Bildlauf.Nachfahren<Glasdesign.Glaskoerper>(b).Any() ? b : null;
        return null;
    }

    public static bool Aktiv(DependencyObject d)
    {
        for (var e = d; e is not null; e = VisualTreeHelper.GetParent(e))
            if (e is UIElement { IsEnabled: false }) return false;
        return true;
    }

    /// <summary>Die nächste deckende Fläche unter dem Element (Rand, Panel oder Steuerelement); zuletzt das Fenster.</summary>
    public static Color Flaeche(DependencyObject d)
    {
        for (var e = VisualTreeHelper.GetParent(d); e is not null; e = VisualTreeHelper.GetParent(e))
        {
            var pinsel = e switch
            {
                Border b => b.Background,
                Panel p => p.Background,
                Control c => c.Background,
                _ => null,
            };
            if (pinsel is SolidColorBrush { Color.A: 255 } s) return s.Color;
        }
        return Palette.Anthrazit;
    }

    public static Color Farbe(Brush pinsel) => pinsel is SolidColorBrush s ? s.Color : throw new PruefungException("keine Vollfarbe: " + pinsel);

    /// <summary>
    /// Kein Bedienelement und kein Text unter <paramref name="wurzel"/> ist beschnitten oder ragt aus ihr; gekürzte Texte (CharacterEllipsis) sind
    /// nicht gekürzt, und umbrechender Text bricht nie mitten im Wort (kein Wort breiter als seine Zeile). Seit 0.10.0 (AP2) aus dem Entwurf der
    /// Mischung übernommen (Prüfbericht Mischung, "Zusätzlich gefunden"): Sie erkennt auch Text, den ein Vorfahr beschneidet. Ein Text in einer
    /// waagerechten Reihe bekommt selbst immer seine ganze Breite, beschnitten wird die Reihe (so stand bei 900 "Wartet auf deine Tas" da, ohne
    /// Befund). Einen Punkt Spiel für die Rundung des Layouts. Liefert die Zahl der geprüften Elemente. <paramref name="gekuerztErlaubt"/>: Namen von
    /// Texten, die mit Auslassungszeichen kürzen dürfen (CharacterEllipsis, etwa ein langer Name); beschnitten werden dürfen auch sie nicht.
    /// </summary>
    public static int NichtsAbgeschnitten(FrameworkElement wurzel, string was, params string[] gekuerztErlaubt)
    {
        var befunde = Abschnittbefunde(wurzel, t => gekuerztErlaubt.Contains(t.Name), out var n);
        Pruefe.Gleich("", string.Join("\n", befunde), was + ": abgeschnitten");
        return n;
    }

    /// <summary>
    /// Die Befunde von <see cref="NichtsAbgeschnitten"/> als Liste, ohne zu werfen (für Wächter, die viele Seiten in einem Lauf sammeln, seit 0.11.1
    /// AP4); <paramref name="gekuerztErlaubt"/> sagt je Text, ob er mit Auslassungszeichen kürzen darf, <paramref name="n"/> ist die Zahl der
    /// geprüften Elemente.
    /// </summary>
    public static List<string> Abschnittbefunde(FrameworkElement wurzel, Func<TextBlock, bool> gekuerztErlaubt, out int n)
    {
        var befunde = new List<string>();
        n = 0;
        foreach (var e in Bildlauf.Nachfahren<FrameworkElement>(wurzel)
                     .Where(e => e is System.Windows.Controls.Primitives.ButtonBase or ComboBox or TextBox or Slider or Funk.Client.Steuerelemente.Stecker
                         or Funk.Client.Steuerelemente.Pegelbalken or TextBlock))
        {
            if (!ZuSehen(e)) continue;
            // Teile einer Steuerelementvorlage (etwa der Text im Auswahlfeld) prüft ihr Steuerelement mit; Text aus einer Datenvorlage (alle Seiten
            // des Hauptfensters stehen in einer, ihr TemplatedParent ist der ContentPresenter) zählt. Im Entwurf der Mischung (lose XAML) hatten
            // die Texte gar keinen TemplatedParent.
            if (e is TextBlock { TemplatedParent: not null and not ContentPresenter }) continue;
            n++;
            var name = e.GetType().Name + " " + (e is TextBlock t0 ? "\"" + Kurz(Inhalt(t0)) + "\"" : e.Name);
            if (System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(e) is { } clip && (clip.Bounds.Width < e.ActualWidth - 0.5 || clip.Bounds.Height < e.ActualHeight - 0.5))
                befunde.Add($"{name} beschnitten ({clip.Bounds.Width:0.0} von {e.ActualWidth:0.0})");
            var r = e.TransformToAncestor(wurzel).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
            if (r.Left < -0.5 || r.Right > wurzel.ActualWidth + 0.5 || r.Top < -0.5 || r.Bottom > wurzel.ActualHeight + 0.5)
                befunde.Add($"{name} ragt hinaus ({r.Left:0}..{r.Right:0} von {wurzel.ActualWidth:0})");
            for (var d = VisualTreeHelper.GetParent(e); d is FrameworkElement v && !ReferenceEquals(v, wurzel); d = VisualTreeHelper.GetParent(d))
            {
                // Ein Bildlauf schneidet nur sein Fenster aus dem Inhalt; was gerollt außer Sicht steht, ist nicht abgeschnitten.
                if (v is ScrollContentPresenter) break;
                // Ein Vorfahr, der schmaler angeordnet ist, als er sein will, schneidet über seinen Layout-Clip (nicht über Clip).
                if ((VisualTreeHelper.GetClip(v) ?? System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(v)) is not { } schnitt) continue;
                var rv = e.TransformToAncestor(v).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
                var c = schnitt.Bounds;
                if (rv.Left >= c.Left - 1 && rv.Top >= c.Top - 1 && rv.Right <= c.Right + 1 && rv.Bottom <= c.Bottom + 1) continue;
                befunde.Add($"{name} beschnitten von {v.GetType().Name} {v.Name} ({rv.Left:0}..{rv.Right:0} x {rv.Top:0}..{rv.Bottom:0}, sichtbar {c.Left:0}..{c.Right:0} x {c.Top:0}..{c.Bottom:0})");
                break;
            }
            if (e is not TextBlock t || Inhalt(t) is not { Length: > 0 } inhalt) continue;
            double Textbreite(string s) => new FormattedText(s, System.Globalization.CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight,
                new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize, Brushes.White, 1).WidthIncludingTrailingWhitespace;
            if (t.TextTrimming != TextTrimming.None && Textbreite(inhalt) > t.ActualWidth + 0.5 && !gekuerztErlaubt(t))
                befunde.Add($"{name} gekürzt ({Textbreite(inhalt):0.0} von {t.ActualWidth:0.0})");
            // Ein Dateipfad (etwa "Entwurf gespeichert: C:\...\entwurf-....json") hat keine Stelle zum Umbrechen; ihn mitten im Wort zu brechen ist das Beste, was geht.
            if (t.TextWrapping != TextWrapping.NoWrap && t.Inlines.Count <= 1)
                foreach (var wort in inhalt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !w.Contains('\\') && Textbreite(w) > t.ActualWidth + 0.5))
                    befunde.Add($"Wort \"{wort}\" bricht in {name} ({Textbreite(wort):0.0} > {t.ActualWidth:0.0})");
        }
        return befunde;
    }

    /// <summary>
    /// Ein Text oder Bedienelement, das ein anderes überdeckt (Sarevocs Bild vom 12.10.2026: der Knopf "Mitglied vormerken" über der Überschrift
    /// "Mitglieder"; seit 0.11.1 AP4): je zwei sichtbare Elemente (Knöpfe, Auswahl- und Textfelder, Listen, Regler und Texte außerhalb einer
    /// Steuerelementvorlage), von denen keines im anderen steckt, dürfen sich um höchstens einen Punkt überschneiden. Liefert die Befunde.
    /// </summary>
    public static List<string> Ueberlappungen(FrameworkElement wurzel)
    {
        var befunde = new List<string>();
        // Teile einer Steuerelementvorlage (der Knopf im Auswahlfeld, der Text im Listeneintrag) zählen nicht: Sie liegen von Natur aus übereinander.
        // Was in einem Bildlauf außer Sicht gerollt ist (ListBox mit MaxHeight), liegt unter nichts.
        var elemente = Bildlauf.Nachfahren<FrameworkElement>(wurzel)
            .Where(e => e is System.Windows.Controls.Primitives.ButtonBase or ComboBox or TextBox or ListBox or Slider or TextBlock)
            .Where(e => e is { TemplatedParent: null or ContentPresenter } && ZuSehen(e) && e is not TextBlock { Text.Length: 0, Inlines.Count: 0 })
            .Where(e => ImBildlaufZuSehen(e, wurzel))
            .Select(e => (Element: e, Rechteck: e.TransformToAncestor(wurzel).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight))))
            .ToList();
        for (var i = 0; i < elemente.Count; i++)
        {
            for (var j = i + 1; j < elemente.Count; j++)
            {
                var (a, ra) = elemente[i];
                var (b, rb) = elemente[j];
                if (Vorfahr(a, b) || Vorfahr(b, a)) continue;
                var schnitt = Rect.Intersect(ra, rb);
                if (schnitt.IsEmpty || schnitt.Width <= 1 || schnitt.Height <= 1) continue;
                befunde.Add($"{Bezeichnung(a)} überdeckt {Bezeichnung(b)} ({schnitt.Width:0} x {schnitt.Height:0} Punkte)");
            }
        }
        return befunde;
    }

    /// <summary>
    /// Ein Text, der mit Auslassungszeichen kürzen darf (TextTrimming), braucht einen Hilfetext, der ihn ganz zeigt: einen ToolTip an sich
    /// selbst oder einen Hilfetext (<c>gd:Hilfe.Text</c>, ToolTip) an einem Vorfahren bis <paramref name="wurzel"/>. Sonst steht bei einem langen
    /// Namen "Mitglie..." da, und niemand erfährt den Rest (0.11.1 AP4). Liefert die Befunde, auch für Texte, die gerade nicht gekürzt sind.
    /// </summary>
    public static List<string> GekuerztOhneHilfe(FrameworkElement wurzel) =>
        Bildlauf.Nachfahren<TextBlock>(wurzel)
            .Where(t => ZuSehen(t) && t.TextTrimming != TextTrimming.None && t is { TemplatedParent: null or ContentPresenter } && !HatHilfe(t, wurzel))
            .Select(t => $"{Bezeichnung(t)} kürzt mit Auslassungszeichen, aber ohne Hilfetext")
            .ToList();

    /// <summary>Ob <paramref name="e"/> selbst oder ein Vorfahr bis <paramref name="wurzel"/> einen ToolTip oder Hilfetext trägt.</summary>
    public static bool HatHilfe(FrameworkElement e, FrameworkElement wurzel)
    {
        for (DependencyObject? d = e; d is not null && !ReferenceEquals(d, wurzel); d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { ToolTip: not null }) return true;
            if (!string.IsNullOrEmpty(Glasdesign.Hilfe.GetText(d))) return true;
        }
        return false;
    }

    /// <summary>Ob <paramref name="e"/> in jedem Bildlauf zwischen ihm und <paramref name="wurzel"/> wenigstens zum Teil im Fenster steht (nicht außer Sicht gerollt).</summary>
    public static bool ImBildlaufZuSehen(FrameworkElement e, FrameworkElement wurzel)
    {
        for (var d = VisualTreeHelper.GetParent(e); d is FrameworkElement v && !ReferenceEquals(v, wurzel); d = VisualTreeHelper.GetParent(d))
        {
            if (v is not ScrollContentPresenter s) continue;
            var r = e.TransformToAncestor(s).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
            if (r.Bottom <= 0.5 || r.Top >= s.ActualHeight - 0.5 || r.Right <= 0.5 || r.Left >= s.ActualWidth - 0.5) return false;
        }
        return true;
    }

    private static bool Vorfahr(DependencyObject vorfahr, DependencyObject d)
    {
        for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p))
            if (ReferenceEquals(p, vorfahr)) return true;
        return false;
    }

    private static string Bezeichnung(FrameworkElement e) =>
        e.GetType().Name + " " + (e is TextBlock t ? "\"" + Kurz(Inhalt(t)) + "\"" : e.Name.Length > 0 ? e.Name : e is ContentControl { Content: string s } ? "\"" + Kurz(s) + "\"" : "");

    public static string Kurz(string text) => text.Length <= 40 ? text : text[..40] + "...";
}
