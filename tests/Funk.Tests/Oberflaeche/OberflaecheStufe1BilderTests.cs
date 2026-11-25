// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using Hilfe = Glasdesign.Hilfe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Sichtproben für Sarevoc nach Stufe 1 von 0.6.0 (Hilfetexte, Fenstergrößen, Fassung im Titel), nur im Speicher gezeichnet: mit
/// <c>FUNK_BILDER</c> als PNG, ohne die Variable nur die Prüfungen. Die Hilfetexte stehen als ToolTip-Karte unter ihrem Knopf
/// (im Programm erscheinen sie in einem Popup, wenn die Maus eine halbe Sekunde ruht); gesperrt heißt IsEnabled aus. Der
/// Klassenname steht nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheStufe1BilderTests
{
    private const double Skala = 1.5;

    [Test]
    public static void Stufe1_Sichtproben_HilfetexteUndFenstergroessen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            // Hilfetexte: normal, mit Taste, gesperrt mit Grund.
            var proben = new (string Titel, ButtonBase Knopf, string Erwartet)[]
            {
                ("normal", Mit(new Button { Content = "Rollen neu" }, "Holt die Rollen neu vom Server.", null, null),
                    "Holt die Rollen neu vom Server."),
                ("mit Taste", Mit(new Button { Content = "Funkfeld" }, "Holt das Funkfeld nach vorn.", "Strg+F", null), "Holt das Funkfeld nach vorn. (Strg+F)"),
                ("gesperrt mit Grund", Mit(new Button { Content = "Einsteigen", IsEnabled = false }, "Steigt mit Einheit und Rolle ins Netz ein.", null,
                    "Erst verbinden, dann kannst du einsteigen."), "Erst verbinden, dann kannst du einsteigen."),
            };
            var bogen = new Bogen((3 * 360) + 40, 250, Skala, Palette.Anthrazit);
            bogen.Text("Hilfetexte (Tooltips) - halbe Sekunde Verzögerung, abschaltbar unter Einstellungen; Aussehen vorläufig", 20, 16, 16, fett: true);
            for (var i = 0; i < proben.Length; i++)
            {
                var (titel, knopf, erwartet) = proben[i];
                var x = 20 + (i * 360);
                bogen.Text(titel, x, 52, 12.5, Palette.Text2);
                Pruefe.Gleich(erwartet, knopf.ToolTip as string, titel + ": ToolTip aus Hilfe.Text, Hilfe.Taste und Hilfe.Gesperrt");
                Pruefe.Gleich(!knopf.IsEnabled, ToolTipService.GetShowOnDisabled(knopf), titel + ": gesperrt mit Grund auch sichtbar");
                var flaeche = new Border { Background = Bogen.Pinsel(Palette.Flaeche), Padding = new Thickness(8), Child = knopf };
                var kb = OberflaecheHilfeTests.Zeichnen(flaeche, Skala);
                bogen.Bild(kb, x, 76, kb.PixelWidth / Skala, kb.PixelHeight / Skala);
                var karte = OberflaecheHilfeTests.Zeichnen(new ToolTip { Content = knopf.ToolTip }, Skala);
                bogen.Bild(karte, x + 24, 76 + (kb.PixelHeight / Skala) + 6, karte.PixelWidth / Skala, karte.PixelHeight / Skala);
            }
            Bildablage.Ablegen(bogen.Fertig(), "tooltips");

            // Hauptfenster in den Größenklassen: klein, die Seite Sprachaktivierung (bis 0.9.3 aufgeklappt unter Klein), im Brett.
            using var a = new Aufbau();
            var dienst = new VerwaltungsdienstAttrappe();
            using var teil = new Funk.Verwaltung.Verwaltungsteil(dienst, new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst));
            a.Verwaltung = teil;
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            a.Dienst.LageSetzen(l => l with { Konto = new KontoInfo(9, "Nordlicht", "nordlicht", Stufe.Admin, [Recht.Zuweisen]) { Admin = AdminStatus.Aktiv } });
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.StartKennung);
            var haupt = new Hauptfenster(h, a.Ablage, OberflaecheFunkfeldGroesseTests.ZweiMonitore);
            fenster.Add(haupt);
            RenderTargetBitmap Fenster(string name)
            {
                var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(haupt.Width, haupt.Height));
                Bildablage.Ablegen(bild, name);
                return bild;
            }
            var klein = Fenster("haupt-klein");
            Pruefe.Wahr(klein.PixelWidth == 900 && klein.PixelHeight == 900, "klein: 900 x 900 (die Startseite, seit 0.10.0 Q89)");
            h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
            var seite = Fenster("haupt-sprachaktivierung");
            // Seit 0.10.0 eine Seite mit eigener Größe (ab Werk 1280 x 900); bis 0.9.3 klappte sie unter Klein auf (760 x 1006).
            Pruefe.Wahr(seite.PixelWidth == 1280 && seite.PixelHeight == 900, "Seite Sprachaktivierung: 1280 x 900");
            h.AktiverBereich = h.Bereiche.First(b => b.Kennung == HauptViewModel.RaidKennung);
            var brett = Fenster("haupt-brett");
            Pruefe.Wahr(brett.PixelWidth == 1120 && brett.PixelHeight == 780, "Brett: 1120 x 780");
            funkfeld.Dispose();
        });
    }

    private static ButtonBase Mit(ButtonBase knopf, string text, string? taste, string? gesperrt)
    {
        Hilfe.SetText(knopf, text);
        Hilfe.SetTaste(knopf, taste);
        Hilfe.SetGesperrt(knopf, gesperrt);
        return knopf;
    }
}
