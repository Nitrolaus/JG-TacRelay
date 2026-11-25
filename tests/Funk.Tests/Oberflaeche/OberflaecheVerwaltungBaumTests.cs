// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;
using Funk.Verwaltung.Ansichten;
using Funk.Verwaltung.Modelle;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Fassung 0.9.1 (Sarevoc 08.10.2026): die Navigation der Verwaltung als Baum wie im Explorer (Q55 Variante A, Unterpunkte deutlich eingerückt)
/// und die Rückfrage beim Beenden mit offener Admin-Sitzung.
/// </summary>
internal static class OberflaecheVerwaltungBaumTests
{
    private static readonly string[] Gruppen = ["Heute", "Leute", "Funk", "Einsatz", "Werkzeuge"];

    private static (VerwaltungsdienstAttrappe D, VerwaltungViewModel Vm, Verwaltungsteil Teil) Offen()
    {
        var d = new VerwaltungsdienstAttrappe();
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var teil = new Verwaltungsteil(d, vm);
        d.LageSetzen(_ => new Verwaltungslage { Verbunden = true, Name = "Nordlicht", Admin = AdminStatus.Aktiv, SchluesselVorhanden = true });
        vm.Code = VerwaltungsdienstAttrappe.RichtigerCode;
        vm.AnmeldenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return (d, vm, teil);
    }

    [Test]
    public static void Verwaltung_Navigation_BaumWieImExplorer_UnterpunkteEingerueckt_Zuklappbar()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var (_, vm, teil) = Offen();
            using var t = teil;
            Pruefe.Wahr(vm.IstOffen, "Admin-Sitzung offen");
            using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Verwaltung = teil };
            var h = a.Haupt(out _, out _, out _, out _);
            a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = AdminStatus.Aktiv } });
            h.AktiverBereich = h.Bereiche.Single(b => b.Kennung == HauptViewModel.VerwaltungKennung);
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            var bild = Ganz(haupt, 1280);
            var ansicht = Bildlauf.Nachfahren<VerwaltungAnsicht>(Bildlauf.Wurzel(haupt)).Single();
            var baum = (FrameworkElement)ansicht.FindName("NaviBaum");
            var ordner = Bildlauf.Nachfahren<Expander>(baum).ToList();
            Pruefe.Gleich(string.Join("|", Gruppen), string.Join("|", ordner.Select(o => o.Header as string)), "fünf Ordner in dieser Folge");
            Pruefe.Wahr(ordner.All(o => o.IsExpanded), "ab Werk aufgeklappt");

            var funk = ordner.Single(o => (string)o.Header == "Funk");
            var kopf = Bildlauf.Nachfahren<ToggleButton>(funk).First();
            var kanaele = Bildlauf.Nachfahren<RadioButton>(funk).Single(r => r.Content as string == "Kanäle");
            var xKopf = kopf.TranslatePoint(new Point(0, 0), baum).X;
            var xEintrag = kanaele.TranslatePoint(new Point(0, 0), baum).X;
            Pruefe.Wahr(xEintrag - xKopf >= 24, "Unterpunkte deutlich nach rechts eingerückt (" + (xEintrag - xKopf) + ")");
            // Bis zum Baum: Ein nie gezeigtes Fenster steht selbst auf Collapsed (CODEMAP, Fallen), IsVisible taugt hier nicht.
            bool Sichtbar(DependencyObject d)
            {
                for (var p = d; p is not null && !ReferenceEquals(p, baum); p = System.Windows.Media.VisualTreeHelper.GetParent(p))
                    if (p is UIElement { Visibility: not Visibility.Visible }) return false;
                return true;
            }
            Pruefe.Wahr(Sichtbar(kanaele), "aufgeklappt sichtbar");

            var rand = (Border)ansicht.FindName("NaviLinks");
            var r = rand.TransformToAncestor(Bildlauf.Wurzel(haupt)).TransformBounds(new Rect(rand.RenderSize));
            Bildablage.Ablegen(new CroppedBitmap(bild, new Int32Rect((int)r.Left, (int)r.Top, (int)Math.Min(r.Width + 8, bild.PixelWidth - r.Left),
                (int)Math.Min(r.Height + 8, bild.PixelHeight - r.Top))), "verwaltung-baum");

            funk.IsExpanded = false;
            _ = Ganz(haupt, 1280);
            Pruefe.Falsch(Sichtbar(kanaele), "zugeklappt: Kanäle verschwinden");
            vm.IstKanaele = true;
            Pruefe.Wahr(vm.IstKanaele, "die Wahl bleibt auch bei zugeklapptem Ordner");
            funk.IsExpanded = true;
        });
    }

    [Test]
    public static void Verwaltung_Navigation_MerktZugeklappteOrdner_UeberDenNeustart()
    {
        // Seit 0.9.3: Welche Ordner zu sind, steht im Ablageordner (verwaltung-navigation.txt); ab Werk und ohne Datei sind alle offen.
        using var ordner = new Testordner();
        var d = new VerwaltungsdienstAttrappe { Ablageordner = ordner.Pfad };
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        Pruefe.Wahr(vm is { NaviHeuteOffen: true, NaviLeuteOffen: true, NaviFunkOffen: true, NaviEinsatzOffen: true, NaviWerkzeugeOffen: true }, "ab Werk alle offen");
        vm.NaviLeuteOffen = false;
        vm.NaviWerkzeugeOffen = false;
        var datei = System.IO.Path.Combine(ordner.Pfad, VerwaltungViewModel.NaviDatei);
        Pruefe.Gleich("Leute|Werkzeuge", string.Join("|", System.IO.File.ReadAllLines(datei)), "gemerkt");

        var neu = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        Pruefe.Wahr(neu is { NaviHeuteOffen: true, NaviLeuteOffen: false, NaviFunkOffen: true, NaviEinsatzOffen: true, NaviWerkzeugeOffen: false }, "nach dem Neustart wie zuletzt");
        neu.NaviLeuteOffen = true;
        Pruefe.Gleich("Werkzeuge", string.Join("|", System.IO.File.ReadAllLines(datei)), "wieder aufgeklappt");

        var ohne = new VerwaltungViewModel(new VerwaltungsdienstAttrappe(), jetzt: () => VerwaltungsdienstAttrappe.Abend);
        ohne.NaviFunkOffen = false;
        Pruefe.Falsch(ohne.NaviFunkOffen, "ohne Ablageordner geht es auch, nur ohne Merken");
    }

    [Test]
    public static void Beenden_MitOffenerAdminSitzung_FragtNach()
    {
        using var ohne = new Aufbau(FunkDienstAttrappe.Beispiel());
        Pruefe.Falsch(ohne.Haupt(out _, out _, out _, out _).AdminSitzungOffen, "ohne Verwaltungsteil keine Admin-Sitzung");

        var (_, vm, teil) = Offen();
        using var t = teil;
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Verwaltung = teil };
        var h = a.Haupt(out _, out _, out _, out _);
        Pruefe.Wahr(h.AdminSitzungOffen, "Admin-Sitzung offen");
        var bestaetigt = 0;
        h.BeendenBestaetigt += () => bestaetigt++;

        h.BeendenFrageOffen = true;
        h.BeendenAbbrechenCommand.Execute(null);
        Pruefe.Wahr(!h.BeendenFrageOffen && bestaetigt == 0, "Abbrechen: das Programm läuft weiter");
        h.BeendenFrageOffen = true;
        h.BeendenBestaetigenCommand.Execute(null);
        Pruefe.Wahr(!h.BeendenFrageOffen && bestaetigt == 1, "Beenden: die App schließt ohne zweite Frage");
        Pruefe.Falsch(HauptViewModel.BeendenFrageText.Any(z => z is '–' or '—' or '„' or '“' or '…'), "nur einfache Zeichen");

        vm.AbmeldenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Pruefe.Falsch(h.AdminSitzungOffen, "nach Verwaltung schließen: keine Rückfrage mehr");

        // Die Rückfrage liegt über dem Fenster und zeichnet den Rest weich.
        Bildlauf.Ausfuehren(fenster =>
        {
            using var b = new Aufbau(FunkDienstAttrappe.Beispiel());
            var hb = b.Haupt(out _, out _, out _, out _);
            var haupt = new Hauptfenster(hb, b.Ablage);
            fenster.Add(haupt);
            hb.BeendenFrageOffen = true;
            var bild = Ganz(haupt, 960);
            var frage = (FrameworkElement)haupt.FindName("BeendenFrage");
            Pruefe.Gleich(Visibility.Visible, frage.Visibility, "die Rückfrage ist zu sehen");
            Pruefe.Wahr(((FrameworkElement)haupt.FindName("Inhalt")).Effect is System.Windows.Media.Effects.BlurEffect, "der Inhalt darunter ist weichgezeichnet");
            Bildablage.Ablegen(bild, "beenden-rueckfrage");
        });
    }
}
