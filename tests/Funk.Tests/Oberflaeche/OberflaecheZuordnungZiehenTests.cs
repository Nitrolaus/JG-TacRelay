// JG TacRelay - Maintainer: Sarevoc
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using static Funk.Client.Dienste.FunkDienstAttrappe;
using Gong = GongSolutions.Wpf.DragDrop.DragDrop;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Das Ziehen auf dem Brett über die Ereignisse von WPF selbst (0.11.1 AP1, Sarevocs Befund vom 12.10.2026: "landet immer auf Standard").
/// Ein Ablegen löst in WPF erst PreviewDrop aus (tunnelt von der Wurzel nach innen) und dann Drop (steigt von innen nach außen), mit
/// denselben Argumenten; was im Tunnel als erledigt gilt, erreicht keinen Handler mehr. gong-wpf-dragdrop hängt sich mit DropEventType
/// Auto bei jedem Ziel, das kein ItemsControl ist, an die Preview-Ereignisse. So fing die Spalte der Einheit (das äußerste Ziel) jede
/// Ablage auf einen Block in ihr ab und setzte "den besten Platz" der Spalte statt des Blocks; in einer Flotte aus der früheren Kanalvorlage
/// (eine Spalte "Rollen ohne Einheit" mit 21 Blöcken) war das immer die erste Rolle. Die Modelltests sahen es nicht: Sie rufen AblegenAsync mit dem
/// Block direkt. Hier löst der Test das Ereignispaar auf dem Element unter der Maus aus, wie WPF es täte, ohne Maus, ohne Fenster auf
/// dem Bildschirm (Bildlauf), über dem Beispielabend der Attrappe, und prüft, was auf den Draht geht. Seit 0.11.2 über die Ziele des
/// Stammbaums (Ch., Kanal, Liste links); Spalten und Blöcke zeigt die Raid-Seite nicht mehr.
/// </summary>
internal static class OberflaecheZuordnungZiehenTests
{
    /// <summary>
    /// Was gong über das laufende Ziehen weiß (sein Feld für das IDragInfo, gesetzt beim Drücken der Maus auf einer Quelle): Ohne es nähme
    /// gong die Daten als fremdes IDataObject, nicht als Karte.
    /// </summary>
    private static readonly FieldInfo Ziehstand = typeof(Gong).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
        .Single(f => typeof(GongSolutions.Wpf.DragDrop.IDragInfo).IsAssignableFrom(f.FieldType));

    /// <summary>
    /// Das Ereignispaar einer Ablage, wie WPF es auslöst (OleDropTarget): PreviewDrop tunnelt von der Wurzel zum Element, Drop steigt von
    /// dort auf, beide mit denselben Argumenten; was im Tunnel erledigt wurde, erreicht keinen weiteren Handler. Gezogen wird ein Name
    /// (<see cref="BrettKarte"/>) oder seit 0.11.2 auch eine Person im Stammbaum (<see cref="KanalPerson"/>).
    /// </summary>
    private static void Ablegen(UIElement ziel, object daten, Point punkt)
    {
        var ctor = typeof(DragEventArgs).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single(c => c.GetParameters().Length == 5);
        var args = (DragEventArgs)ctor.Invoke([new DataObject(Gong.DataFormat.Name, daten), DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, (DependencyObject)ziel, punkt]);
        Ziehstand.SetValue(null, new Ziehinfo(daten) { Data = daten, Effects = DragDropEffects.Move });
        try
        {
            args.RoutedEvent = System.Windows.DragDrop.PreviewDropEvent;
            ziel.RaiseEvent(args);
            args.RoutedEvent = System.Windows.DragDrop.DropEvent;
            ziel.RaiseEvent(args);
        }
        finally
        {
            Ziehstand.SetValue(null, null);
        }
        Bildlauf.Leeren();
    }

    private static FrameworkElement Ziel(BrettAnsicht ansicht, object modell) =>
        Bildlauf.Nachfahren<FrameworkElement>(ansicht).Single(x => x is not ItemsControl and not Button && Gong.GetIsDropTarget(x) && ReferenceEquals(Gong.GetDropHandler(x), modell));

    private static Point Mitte(FrameworkElement e) => new(e.ActualWidth / 2, e.ActualHeight / 2);

    private static string Letzter(Aufbau a) => a.Dienst.Aufrufe.LastOrDefault(x => x.StartsWith("Verschieben", StringComparison.Ordinal)) ?? "(nichts)";

    private static string Id(ushort id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Seit 0.11.2 (Stammbaum, Sarevoc 13.10.2026) über die Ziele der neuen Raid-Seite: ein Name auf die Zeile eines Ch. (der Ch., nicht der Kanal darum),
    /// auf den Knoten eines Kanalnetzes (nichts, erst ein Ch.), eine Person aus dem Baum in die Liste links (heraus), ein Name in die Liste links
    /// (nichts), ein Name auf einen Kanal ohne Ch.; dazu die Verdrahtung. Bis 0.11.1 prüfte der Test Block gegen Spalte, Tausch, Netz und die
    /// Liste links als "auf Standard".
    /// </summary>
    [Test]
    public static void Ziehen_WpfEreignisse_ChKanalUndListeLinks()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var d = MitRaidabend();
            d.LageSetzen(BrettBaumTests.MitKanalBruecke);
            using var a = new Aufbau(dienst: d);
            var h = a.Haupt(out _, out _, out _, out _);
            var b = h.Brett;
            b.KartenAbwartenAsync().GetAwaiter().GetResult();
            h.BereichWaehlenCommand.Execute("5");
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            // Seit 0.11.3 steht unter den Reitern nur der offene Kanal (der Reiter selbst ist auch ein Ziel, ein Knopf).
            b.ReiterWaehlenCommand.Execute(b.Reiter.Single(r => r.Name == "Brücke"));
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
            var ansicht = Bildlauf.Nachfahren<BrettAnsicht>(Bildlauf.Wurzel(haupt)).Single();
            var bridge = b.Baum.Single(k => k.Name == "Brücke");
            var schiff3 = bridge.Ch.Single(c => c.Name == "Schiff 3");

            // Pollux auf die Zeile von Schiff 3: der Ch., hört und sendet.
            var zeile = Ziel(ansicht, schiff3);
            Ablegen(zeile, b.Karte(Pollux)!, Mitte(zeile));
            Pruefe.Gleich("Verschieben 14:-/- +" + Id(BrettBaumTests.K(3)) + "s @100", Letzter(a), "Ablage auf den Ch.; Meldung: " + b.Meldung);

            // Auf den Knoten des Kanalnetzes: nichts, erst ein Ch.
            var knoten = Ziel(ansicht, bridge);
            Ablegen(knoten, b.Karte(Hagel)!, Mitte(knoten));
            Pruefe.Gleich("Verschieben 14:-/- +" + Id(BrettBaumTests.K(3)) + "s @100", Letzter(a), "Ablage auf das Kanalnetz selbst: kein Verschieben");

            // Kobold aus dem Baum in die Liste links: heraus aus Brücke.
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
            var links = (FrameworkElement)ansicht.FindName("Links");
            var kobold = b.Baum.Single(k => k.Name == "Brücke").Ch.Single(c => c.Name == "Schiff 3").Leute.Single(p => p.Karte.KontoId == Kobold);
            Ablegen(links, kobold, new Point(links.ActualWidth / 2, links.ActualHeight / 2));
            Pruefe.Gleich("Verschieben 4:-/- -f5 @101", Letzter(a), "die Person nach links: heraus; Meldung: " + b.Meldung);

            // Ein Name in die Liste links: nichts (bis 0.11.1 "auf Standard").
            Ablegen(links, b.Karte(Falke)!, new Point(links.ActualWidth / 2, links.ActualHeight / 2));
            Pruefe.Gleich("Verschieben 4:-/- -f5 @101", Letzter(a), "ein Name nach links: kein Verschieben");

            // Ein Kanal ohne Ch. (Kommando, eigener Reiter) nimmt den Namen auf seinem Knoten.
            b.ReiterWaehlenCommand.Execute(b.Reiter.Single(r => r.Name == "Kommando"));
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
            var command = Ziel(ansicht, b.Baum.Single(k => k.Name == "Kommando"));
            Ablegen(command, b.Karte(Eule)!, Mitte(command));
            Pruefe.Gleich("Verschieben 5:-/- +" + Id(NetzKommando) + "s @102", Letzter(a), "Ablage auf Kommando");

            // Verdrahtung: Alle Ziele hängen an ihrem Modell; genau die Ziele mit Zielen darin lassen das Ereignis aufsteigen statt zu tunneln,
            // sonst gewönne das äußerste.
            var ziele = Bildlauf.Nachfahren<UIElement>(ansicht).Where(Gong.GetIsDropTarget).ToList();
            Pruefe.Wahr(ziele.Count > 0 && ziele.All(z => Gong.GetDropHandler(z) is not null), "jedes Ziel hat seinen Handler");
            foreach (var z in ziele.Where(z => z is not ItemsControl))
            {
                var innen = Bildlauf.Nachfahren<UIElement>(z).Any(Gong.GetIsDropTarget);
                Pruefe.Wahr(!innen || Gong.GetDropEventType(z) == GongSolutions.Wpf.DragDrop.EventType.Bubbled,
                    "ein Ziel mit Zielen darin lässt das Ereignis aufsteigen: " + Gong.GetDropHandler(z)?.GetType().Name);
            }
        });
    }
}
