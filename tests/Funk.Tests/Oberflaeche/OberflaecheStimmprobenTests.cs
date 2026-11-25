// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Stimmproben (Q60): fünf Aufnahmen, WAV im Ordner der Einstellungen, eine nach der anderen. Bis 0.9.3 eine Karte auf der Seite Audio, seit 0.10.0
/// unter Einstellungen > Fehlersuche > Feinheiten (Q65, Endfassung).
/// </summary>
internal static class OberflaecheStimmprobenTests
{
    [Test]
    public static void Stimmproben_FuenfAufnahmen_AlsWavImOrdnerDerEinstellungen()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
        var audio = a.Audio();
        var sp = audio.Stimmproben;
        Pruefe.Gleich("Mustermann", sp.Sprecher, "ab Werk der SC-Name");
        Pruefe.Gleich("normal|leise|kurz|laut|gerufen", string.Join("|", sp.Schritte.Select(x => x.Kennung)), "fünf Aufnahmen");
        sp.Sprecher = "Test/Test";
        Pruefe.Wahr(sp.OrdnerText.EndsWith("stimmproben\\Test_Test", StringComparison.Ordinal), "unerlaubte Zeichen im Namen ersetzt: " + sp.OrdnerText);
        Pruefe.Falsch(sp.OrdnerText.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase) && Environment.UserName.Length > 2, "kein Windows-Benutzername auf dem Schirm");

        a.Maschine.StimmprobeHalten = true;
        var normal = sp.Schritte[0];
        normal.AufnehmenCommand.Execute(null);
        Pruefe.Gleich(15000, a.Maschine.StimmprobeGestartet.Single(), "normal: 15 s");
        Pruefe.Falsch(sp.Schritte[1].AufnehmenCommand.CanExecute(null), "während einer Aufnahme keine zweite");
        audio.Takt();
        Pruefe.Wahr(normal.Zustand.StartsWith("Aufnahme läuft", StringComparison.Ordinal), "läuft: " + normal.Zustand);

        a.Maschine.StimmprobeHalten = false;
        audio.Takt();
        Pruefe.Wahr(normal.Datei is { } d && File.Exists(d) && d.StartsWith(sp.Ordner, StringComparison.Ordinal) && d.EndsWith("_normal.wav", StringComparison.Ordinal), "gespeichert: " + normal.Datei);
        Pruefe.Wahr(normal.Zustand.StartsWith("Gespeichert:", StringComparison.Ordinal), normal.Zustand);
        var bytes = File.ReadAllBytes(normal.Datei!);
        Pruefe.Gleich(44 + 15 * 48000 * 2, bytes.Length, "WAV-Kopf und 15 s zu 16 Bit");
        Pruefe.Gleich(48000, BitConverter.ToInt32(bytes, 24), "48 kHz");
        Pruefe.Gleich((short)1, BitConverter.ToInt16(bytes, 22), "mono");
        Pruefe.Wahr(sp.Schritte[1].AufnehmenCommand.CanExecute(null), "danach geht die nächste");
        Pruefe.Wahr(Directory.GetDirectories(Path.GetDirectoryName(a.Ablage.Pfad)!, "stimmproben").Length == 1, "unter dem Ordner der Einstellungen");
    }

    [Test]
    public static void Stimmproben_UnterFehlersuche()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var b = new Aufbau(FunkDienstAttrappe.Beispiel());
            var h = b.Haupt(out var funkfeld, out _, out _, out _);
            Seiten.Feinheiten(h, Abschnittskennung.Fehlersuche);
            var haupt = new Hauptfenster(h, b.Ablage);
            fenster.Add(haupt);
            var bild = Bildlauf.Zeichnen(haupt, 1, new Size(1100, 3200)).Bild;
            var wurzel = Bildlauf.Wurzel(haupt);
            var karte = Bildlauf.Nachfahren<Border>(wurzel).Single(x => x.Name == "StimmprobenKarte");
            Pruefe.Wahr(Ansichtspruefung.ZuSehen(karte) && Bildlauf.Nachfahren<Border>(Bildlauf.Nachfahren<Expander>(wurzel).Single(x => x.Name == "FehlersucheFeinheiten")).Contains(karte),
                "unter Fehlersuche > Feinheiten zu sehen");
            var knoepfe = Bildlauf.Nachfahren<Button>(karte).Where(k => k.Command is not null).ToList();
            Pruefe.Gleich(5, knoepfe.Count, "fünf Knöpfe Aufnehmen");
            var r = karte.TransformToAncestor(wurzel).TransformBounds(new Rect(karte.RenderSize));
            Bildablage.Ablegen(new System.Windows.Media.Imaging.CroppedBitmap(bild, new Int32Rect(0, (int)r.Top, bild.PixelWidth,
                (int)Math.Min(r.Height, bild.PixelHeight - r.Top))), "stimmproben-karte");
            funkfeld.Dispose();
        });
    }
}
