// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;

namespace Funk.Client.Fenster;

/// <summary>
/// Die Raid-Seite der Einsatzleitung (Bereich Raid). Hier steht nur, was die Ansicht selbst braucht: Strg+E springt in die Suche. Seit 0.11.2
/// (Stammbaum nach rechts) gibt es kein Menü an den Namen, kein Ch.-Raster und keine Phasen mehr; eine Ablage auf einen Platz kommt aus dieser
/// Ansicht nicht mehr, das Gleiten an den Platz (<see cref="Gelandet"/>) bleibt für das Modell.
/// </summary>
public partial class BrettAnsicht : UserControl
{
    public BrettAnsicht()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.E || Keyboard.Modifiers != ModifierKeys.Control) return;
            Suchfeld.Focus();
            Suchfeld.SelectAll();
            e.Handled = true;
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is BrettViewModel alt) alt.Gelandet -= Gelandet;
            if (e.NewValue is BrettViewModel neu) neu.Gelandet += Gelandet;
        };
    }

    /// <summary>Die letzte Landung einer Karte (Tests spulen sie vor), sonst <c>null</c>.</summary>
    internal Sanftlauf? LetzteLandung { get; private set; }

    /// <summary>
    /// Nach dem Loslassen auf einem Platz gleitet die Karte in etwa 150 ms vom Ziehbild an ihren Platz (0.11.1). Seit 0.11.2 zeigt die Ansicht
    /// keine Plätze mehr; ohne Liste des Blocks geschieht nichts.
    /// </summary>
    private void Gelandet(object? sender, Landung landung)
    {
        var liste = Nachfahren<ItemsControl>(this).FirstOrDefault(x => ReferenceEquals(x.ItemsSource, landung.Block.Eintraege));
        if (liste is null) return;
        liste.UpdateLayout();
        if (liste.ItemContainerGenerator.ContainerFromItem(landung.Karte) is not UIElement behaelter) return;
        var von = new Vector();
        if (landung.Punkt.Bezug is { } bezug && bezug.FindCommonVisualAncestor(behaelter) is not null)
        {
            var ecke = bezug.TransformToVisual(behaelter).Transform(new Point(landung.Punkt.Punkt.X - landung.Punkt.Griff.X, landung.Punkt.Punkt.Y - landung.Punkt.Griff.Y));
            von = new Vector(ecke.X, ecke.Y);
        }
        LetzteLandung = Sanft.Gleiten(behaelter, von, Sanft.VorschauDeckung, Sanft.Landung);
    }

    private static IEnumerable<T> Nachfahren<T>(DependencyObject wurzel) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(wurzel); i++)
        {
            var kind = System.Windows.Media.VisualTreeHelper.GetChild(wurzel, i);
            if (kind is T t) yield return t;
            foreach (var weiter in Nachfahren<T>(kind)) yield return weiter;
        }
    }

    /// <summary>Das Suchfeld links (Tests, Fokus).</summary>
    internal TextBox Suche => Suchfeld;

    /// <summary>Kein Menü an einem Namen (Sarevoc 13.10.2026: "kein Menü bei einem Funker"); die alten Vorlagen der Plätze hängen noch daran.</summary>
    private void Karte_ContextMenuOpening(object sender, ContextMenuEventArgs e) => e.Handled = true;

    /// <summary>Das Menü einer Karte bis 0.11.1 ("Setzen auf", Platz räumen, Stumm); die Ansicht zeigt es seit 0.11.2 nicht mehr.</summary>
    internal static void Fuellen(ContextMenu menue, BrettViewModel brett)
    {
        ArgumentNullException.ThrowIfNull(menue);
        ArgumentNullException.ThrowIfNull(brett);
        menue.Items.Clear();
    }
}
