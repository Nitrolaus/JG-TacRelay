// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;

namespace Funk.Client.Fenster;

/// <summary>
/// Stil und Lage des Fensters der Meldezeile (RAID-S2-ENTWURF Abschnitt 3), als reine Rechnung ohne Fenster: nie aktiviert
/// (WS_EX_NOACTIVATE), durchklickbar (WS_EX_TRANSPARENT), nicht in Alt+Tab und Taskleiste (WS_EX_TOOLWINDOW, ohne WS_EX_APPWINDOW),
/// geschichtet (WS_EX_LAYERED). Seit 0.11.1 (Sarevoc 12.10.2026: "an einem festen Platz am Bildschirm, nicht da, wo das Funkfeld ist") steht
/// sie an einer von neun Stellen (<see cref="Meldezeilenstelle"/>, <see cref="Fest"/>) auf einem Monitor (<see cref="Monitor"/>: ab Werk
/// der, auf dem das Spiel läuft, ab Werk Star Citizen, sonst der Hauptmonitor), im ganzen Monitorrechteck, <see cref="Rand"/> Einheiten vom Rand; bis
/// 0.11.0 über dem Funkfeld, linksbündig. Ihre Breite ist fest <see cref="BreiteFest"/> bei 0 % mal der Skalierung des Funkfelds
/// (<see cref="Hoechstbreite"/>, Sarevoc Q111), nicht mehr an die Breite des Funkfelds gebunden; eine Meldung in einer Zeile.
/// </summary>
public static class Meldezeilenlage
{
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolwindow = 0x00000080;
    public const int WsExAppwindow = 0x00040000;
    public const int WsExLayered = 0x00080000;
    public const int WsExNoactivate = 0x08000000;

    /// <summary>Abstand der Meldezeile zum Rand des Monitors in Einheiten (an den Stellen links, rechts, oben und unten).</summary>
    public const double Rand = 8;

    /// <summary>Breite der Meldezeile in Einheiten bei 0 % (bis 0.10.0 die Grenze ohne Funkfeld, seit 0.11.1 die einzige).</summary>
    public const double BreiteFest = 640;

    /// <summary>
    /// Höchstbreite der Meldezeile in Einheiten: <see cref="BreiteFest"/> mal <paramref name="skalierung"/> (Sarevoc Q111, 0,7 bis 1,3, <see cref="Skalierung"/>),
    /// damit bei jeder Skalierung dieselben Sätze ganz passen wie bei 0 %. Bis 0.11.0 außerdem höchstens so breit wie das Funkfeld; seit 0.11.1
    /// hängt die Meldezeile nicht mehr am Funkfeld. Was nicht hineinpasst, kürzt das Fenster mit "...".
    /// </summary>
    public static double Hoechstbreite(double skalierung = 1) => BreiteFest * Skalierung(skalierung);

    /// <summary>
    /// Der Faktor, mit dem die Meldezeile skaliert (Sarevoc Q111): der des Funkfelds (<c>FunkfeldViewModel.Skala</c>), begrenzt auf
    /// <see cref="FunkfeldEinstellungen.SkalierungMin"/> bis <see cref="FunkfeldEinstellungen.SkalierungMax"/> (heute -30 bis +30 %; aus den
    /// Konstanten gerechnet, damit eine andere Grenze des Funkfelds die Meldezeile mitnimmt, Befund der Prüfung); Unsinn (0, negativ,
    /// unendlich) zählt als 1.
    /// </summary>
    public static double Skalierung(double faktor) =>
        faktor > 0 && double.IsFinite(faktor)
            ? Math.Clamp(faktor, (100 + FunkfeldEinstellungen.SkalierungMin) / 100.0, (100 + FunkfeldEinstellungen.SkalierungMax) / 100.0)
            : 1;

    /// <summary>Der erweiterte Fensterstil der Meldezeile aus dem bisherigen.</summary>
    public static int Stil(int alt) => (alt | WsExNoactivate | WsExTransparent | WsExToolwindow | WsExLayered) & ~WsExAppwindow;

    /// <summary>
    /// Der Monitor der Meldezeile aus der Wahl unter Einstellungen (<see cref="FunkfeldEinstellungen.MeldezeileMonitor"/>): <c>null</c>
    /// (Automatisch) der des Spiels (<paramref name="spiel"/>, sonst der Hauptmonitor), 0 der Hauptmonitor, n der n-te von
    /// <paramref name="bildschirme"/>; fehlt er gerade (abgesteckt), der Hauptmonitor.
    /// </summary>
    public static Bildschirm Monitor(IReadOnlyList<Bildschirm> bildschirme, int? wahl, Bildschirm? spiel)
    {
        ArgumentNullException.ThrowIfNull(bildschirme);
        if (bildschirme.Count == 0) throw new ArgumentException("Kein Monitor bekannt.", nameof(bildschirme));
        if (wahl is null) return spiel ?? Platzierung.Haupt(bildschirme);
        return wahl is > 0 && wahl.Value <= bildschirme.Count ? bildschirme[wahl.Value - 1] : Platzierung.Haupt(bildschirme);
    }

    /// <summary>
    /// Linke obere Ecke in Bildpunkten für eine Meldezeile von <paramref name="breite"/> mal <paramref name="hoehe"/> Bildpunkten an
    /// <paramref name="stelle"/> auf <paramref name="monitor"/>: im ganzen Monitorrechteck (nicht nur im Arbeitsbereich, die Meldezeile liegt
    /// auch über der Taskleiste), <see cref="Rand"/> Einheiten mit der Skala des Monitors vom Rand, in der Mitte zentriert; eine zu breite
    /// Meldezeile bleibt links und oben im Monitor.
    /// </summary>
    public static (int X, int Y) Fest(Meldezeilenstelle stelle, Bildschirm monitor, int breite, int hoehe)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var b = monitor.Bereich;
        var rand = Platzierung.Pixel(Rand, monitor.Skala > 0 && double.IsFinite(monitor.Skala) ? monitor.Skala : 1);
        var spalte = (int)stelle % 3;
        var zeile = (int)stelle / 3;
        var x = spalte switch
        {
            0 => b.Links + rand,
            1 => b.Links + ((b.Breite - breite) / 2),
            _ => b.Rechts - rand - breite,
        };
        var y = zeile switch
        {
            0 => b.Oben + rand,
            1 => b.Oben + ((b.Hoehe - hoehe) / 2),
            _ => b.Unten - rand - hoehe,
        };
        x = Math.Clamp(x, b.Links, Math.Max(b.Links, b.Rechts - breite));
        y = Math.Clamp(y, b.Oben, Math.Max(b.Oben, b.Unten - hoehe));
        return (x, y);
    }
}
