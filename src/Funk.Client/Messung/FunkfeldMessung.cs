// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;

namespace Funk.Client.Messung;

/// <summary>Eine Probe alle 100 ms: das echte Fenster in Bildpunkten (GetWindowRect), in Einheiten, DPI, Monitor, Kachelraster.</summary>
public sealed record Messprobe(long Ms, int Links, int Oben, int BreitePx, int HoehePx, double BreiteEinheiten, double HoeheEinheiten, double Dpi,
    int Monitor, string SizeToContent, double RasterAngebotBreite, double RasterAngebotHoehe, double RasterBreite, double RasterHoehe, double Deckkraft);

/// <summary>Auswertung eines Laufs: ob das Fenster wuchs, und wie weit es am Ende von der erwarteten Größe abwich.</summary>
public sealed record Messergebnis(bool Waechst, int EndeBreitePx, int EndeHoehePx, int ErwartetBreitePx, int ErwartetHoehePx, int AbweichungPx, bool Passt)
{
    /// <summary>So viel Bildpunkte Spiel für Rundung und Rahmen.</summary>
    public const int Spiel = 2;

    /// <summary>
    /// Wachsen: Eine spätere Probe ist breiter oder höher als die erste (mehr als <see cref="Spiel"/>). Passt: nicht
    /// gewachsen, und die letzte Probe weicht höchstens um <see cref="Spiel"/> von der erwarteten Größe ab.
    /// </summary>
    public static Messergebnis Auswerten(IReadOnlyList<Messprobe> proben, int erwartetBreitePx, int erwartetHoehePx)
    {
        ArgumentNullException.ThrowIfNull(proben);
        if (proben.Count == 0) return new Messergebnis(false, 0, 0, erwartetBreitePx, erwartetHoehePx, int.MaxValue, false);
        var erste = proben[0];
        var ende = proben[^1];
        var waechst = proben.Any(p => p.BreitePx > erste.BreitePx + Spiel || p.HoehePx > erste.HoehePx + Spiel);
        var abweichung = Math.Max(Math.Abs(ende.BreitePx - erwartetBreitePx), Math.Abs(ende.HoehePx - erwartetHoehePx));
        return new Messergebnis(waechst, ende.BreitePx, ende.HoehePx, erwartetBreitePx, erwartetHoehePx, abweichung, !waechst && abweichung <= Spiel);
    }
}

/// <summary>Was die Messung erwartet hat: der Platz, den das Funkfeld sich ausgerechnet hat.</summary>
public sealed record Messerwartung(int Links, int Oben, int BreitePx, int HoehePx, double BreiteEinheiten, double HoeheEinheiten, double Skala,
    double WunschBreite, double WunschHoehe);

/// <summary>Der Bericht als JSON neben Bild und Wegwerf-Einstellungen.</summary>
public sealed record Messbericht(string Zeit, FunkfeldMessauftrag Auftrag, IReadOnlyList<Bildschirm> Bildschirme, Messerwartung? Erwartet,
    Pixelrechteck? BeimZeigen, bool Nachgefuehrt, string? Bild, IReadOnlyList<Messprobe> Proben, Messergebnis? Ergebnis, string? Abbruch);

/// <summary>
/// Das Messwerkzeug für das echte Fenster (A32): <c>TacRelay.exe --funkfeld-messen ...</c>. Öffnet NUR das Funkfeld mit
/// der Vorführungsszene (Attrappen: kein Netz, kein Audiogerät, keine globalen Tasten, keine Joysticks), ohne Fokus
/// (ShowActivated=false), oben links auf dem Hauptmonitor, und hält die angegebene Zeit lang alle 100 ms die echte Größe
/// fest. Einmal nimmt es das Fenster selbst per PrintWindow (PW_RENDERFULLCONTENT) auf, nie den Bildschirm; liefert Windows
/// nichts, gibt es kein Bild. Einstellungen schreibt es nur in eine Wegwerf-Datei neben den Bericht. Startet während der
/// Messung ein Spiel, schließt es sofort (Code 4). Aufruf und Zusammenfassung: tools\funkfeld-messen.ps1.
/// </summary>
public static class FunkfeldMessung
{
    /// <summary>Verlangt der Aufruf die Messung? Dann startet sonst nichts vom Programm.</summary>
    public static bool Verlangt(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains(FunkfeldMessauftrag.Schalter, StringComparer.Ordinal);
    }

    /// <summary>Prüft Auftrag, Freigabe, Ablageort und Spiel, dann läuft die Messung; am Ende beendet sie das Programm.</summary>
    public static void Starten(Application app, IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var (auftrag, code, grund, bericht) = FunkfeldMessauftrag.Lesen(args);
        if (auftrag is null)
        {
            Ablehnen(app, code, grund, bericht);
            return;
        }
        if (Spielsperre.Jetzt() is { } spiel)
        {
            Ablehnen(app, FunkfeldMessauftrag.CodeSpiel, spiel + ", es wird nichts geöffnet.", auftrag.Bericht);
            return;
        }
        new Messlauf(app, auftrag).Beginnen();
    }

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static void Ablehnen(Application app, int code, string? grund, string? bericht)
    {
        if (bericht is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bericht)!);
                File.WriteAllText(bericht, JsonSerializer.Serialize(new { verweigert = grund, code }, Json));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Dann bleibt nur der Exitcode.
            }
        }
        app.Shutdown(code);
    }
}

/// <summary>Ein Lauf des Messwerkzeugs, auf dem Oberflächen-Thread; gibt am Ende (<see cref="Dispose"/>) alle Attrappen frei.</summary>
internal sealed class Messlauf(Application app, FunkfeldMessauftrag auftrag) : IDisposable
{
    private readonly List<Messprobe> _proben = [];
    private readonly Stopwatch _uhr = new();
    private IReadOnlyList<Bildschirm> _bildschirme = [];
    private Einstellungsablage? _ablage;
    private KlangMaschinenAttrappe? _maschine;
    private Vorfuehrung? _vorfuehrung;
    private IDisposable? _anbindung;
    private KlangBruecke? _bruecke;
    private FunkfeldViewModel? _modell;
    private FunkfeldFenster? _fenster;
    private DispatcherTimer? _bildtakt;
    private DispatcherTimer? _probentakt;
    private Messerwartung? _erwartet;
    private string? _bild;
    private bool _bildVersucht;
    private bool _beendet;
    private string? _abbruch;

    public void Beginnen()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(auftrag.Bericht)!);
            if (File.Exists(auftrag.Einstellungen)) File.Delete(auftrag.Einstellungen);
            _bildschirme = WindowsBildschirme.Instanz.Alle();
            var haupt = Platzierung.Haupt(_bildschirme);
            // Frische Einstellungen mit fester Lage oben links auf dem Hauptmonitor, ohne Größe: So nimmt das Funkfeld die
            // optimale Größe auf demselben Weg wie beim Start des Programms.
            var e = new ClientEinstellungen();
            e.Normalisieren();
            e.Funkfeld.Darstellung = auftrag.Darstellung;
            e.Funkfeld.Ansicht = auftrag.Felder;
            e.Funkfeld.Fensterorte[FunkfeldEinstellungen.Schluessel(auftrag.Darstellung, auftrag.Felder)] =
                new FensterOrt(haupt.Arbeit.Links + Platzierung.Pixel(24, haupt.Skala), haupt.Arbeit.Oben + Platzierung.Pixel(24, haupt.Skala));
            var takt = new DispatcherTakt(app.Dispatcher);
            _ablage = new Einstellungsablage(new EinstellungsSpeicher(auftrag.Einstellungen), e, takt);
            Farbvorschlaege.Anwenden(app.Resources, e.Farbstellung);

            _maschine = new KlangMaschinenAttrappe(vorfuehrung: true);
            var dienst = new FunkDienstAttrappe(FunkDienstAttrappe.Beispiel());
            _vorfuehrung = new Vorfuehrung(dienst, _maschine);
            var verlauf = new PegelVerlauf();
            _anbindung = verlauf.Anbinden(_maschine);
            _bruecke = new KlangBruecke(_maschine);
            _modell = new FunkfeldViewModel(dienst, _maschine, verlauf, _ablage, takt, netzKlang: _bruecke);
            _fenster = new FunkfeldFenster(_modell, _ablage) { ShowActivated = false };
            if (_fenster.Platzfuehrung.Gesetzt is { } p)
            {
                var r = p.Rahmen;
                _erwartet = new Messerwartung(p.Links, p.Oben, r.Breite, r.Hoehe, p.Breite, p.Hoehe, p.Bildschirm.Skala,
                    _fenster.LetzteWunschgroesse.Width, _fenster.LetzteWunschgroesse.Height);
            }
            _fenster.Closed += (_, _) =>
            {
                if (!_beendet) Beenden(0, "Das Funkfeld wurde vor dem Ende geschlossen.");
            };
            _bildtakt = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => _modell?.Takt(), app.Dispatcher);
            _probentakt = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal, (_, _) => Probe(), app.Dispatcher);
            _uhr.Start();
            _fenster.Show();
            _bildtakt.Start();
            _probentakt.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Beenden(FunkfeldMessauftrag.CodeFehler, "Fehler beim Start der Messung: " + ex.Message);
        }
    }

    private void Probe()
    {
        if (_beendet || _fenster is null) return;
        if (Spielsperre.Jetzt() is { } spiel)
        {
            Beenden(FunkfeldMessauftrag.CodeSpiel, spiel + ", das Funkfeld wurde sofort geschlossen.");
            return;
        }
        var ms = _uhr.ElapsedMilliseconds;
        if (WindowsBildschirme.Rahmen(_fenster) is { } r)
        {
            var monitor = _bildschirme.Select((b, i) => (b, i)).Where(x => x.b.Bereich.Schnitt(r) > 0).OrderByDescending(x => x.b.Bereich.Schnitt(r))
                .Select(x => x.i).DefaultIfEmpty(-1).First();
            var raster = Nachfahren<Kachelraster>(_fenster).FirstOrDefault();
            _proben.Add(new Messprobe(ms, r.Links, r.Oben, r.Breite, r.Hoehe, Math.Round(_fenster.ActualWidth, 2), Math.Round(_fenster.ActualHeight, 2),
                VisualTreeHelper.GetDpi(_fenster).PixelsPerInchX, monitor, _fenster.SizeToContent.ToString(),
                Endlich(raster?.LetztesAngebot.Width), Endlich(raster?.LetztesAngebot.Height), Math.Round(raster?.ActualWidth ?? 0, 2),
                Math.Round(raster?.ActualHeight ?? 0, 2), _fenster.Opacity));
        }
        if (!_bildVersucht && ms >= 1000)
        {
            _bildVersucht = true;
            _bild = Fensterbild.Ablegen(_fenster, auftrag.Bild) ? auftrag.Bild : null;
        }
        if (ms >= auftrag.Sekunden * 1000L) Beenden(0, null);
    }

    private void Beenden(int code, string? abbruch)
    {
        if (_beendet) return;
        _beendet = true;
        _abbruch = abbruch;
        _bildtakt?.Stop();
        _probentakt?.Stop();
        var zeigen = _fenster?.Platzfuehrung;
        _fenster?.Close();
        Dispose();
        var ergebnis = _erwartet is { } e ? Messergebnis.Auswerten(_proben, e.BreitePx, e.HoehePx) : null;
        var bericht = new Messbericht(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), auftrag, _bildschirme,
            _erwartet, zeigen?.BeimZeigen, zeigen?.Nachgefuehrt ?? false, _bild, _proben, ergebnis, _abbruch);
        try
        {
            File.WriteAllText(auftrag.Bericht, JsonSerializer.Serialize(bericht, FunkfeldMessung.Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            code = code == 0 ? FunkfeldMessauftrag.CodeFehler : code;
        }
        app.Shutdown(code);
    }

    /// <summary>Attrappen freigeben und die Wegwerf-Einstellungen schreiben; mehrfach aufrufbar.</summary>
    public void Dispose()
    {
        _vorfuehrung?.Dispose();
        _vorfuehrung = null;
        _anbindung?.Dispose();
        _anbindung = null;
        _bruecke?.Dispose();
        _bruecke = null;
        _modell?.Dispose();
        _modell = null;
        _maschine?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _maschine = null;
        _ablage?.Sichern();
        _ablage?.Dispose();
        _ablage = null;
    }

    private static double Endlich(double? wert) => wert is { } w && double.IsFinite(w) ? Math.Round(w, 2) : -1;

    private static IEnumerable<T> Nachfahren<T>(DependencyObject wurzel) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(wurzel); i++)
        {
            var kind = VisualTreeHelper.GetChild(wurzel, i);
            if (kind is T t) yield return t;
            foreach (var n in Nachfahren<T>(kind)) yield return n;
        }
    }
}

/// <summary>
/// Nimmt ein eigenes Fenster per PrintWindow mit PW_RENDERFULLCONTENT auf (C:\Claude\CLAUDE.md, Abschnitt Bildschirm): Das
/// Fenster zeichnet sich selbst in ein Speicherbild, auch verdeckt. Kein Bildschirminhalt wird gelesen; liefert Windows
/// nichts, gibt es kein Bild.
/// </summary>
internal static partial class Fensterbild
{
    private const uint PwRenderfullcontent = 2;

    public static bool Ablegen(Window fenster, string pfad)
    {
        var h = new WindowInteropHelper(fenster).Handle;
        if (h == IntPtr.Zero || WindowsBildschirme.Rahmen(fenster) is not { Breite: > 0, Hoehe: > 0 } r) return false;
        var dc = CreateCompatibleDC(0);
        if (dc == 0) return false;
        try
        {
            var kopf = new Bildkopf { Groesse = 40, Breite = r.Breite, Hoehe = -r.Hoehe, Ebenen = 1, Bits = 32 };
            var bitmap = CreateDIBSection(dc, ref kopf, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0) return false;
            try
            {
                var vorher = SelectObject(dc, bitmap);
                bool ok;
                try
                {
                    ok = PrintWindow(h, dc, PwRenderfullcontent);
                }
                finally
                {
                    SelectObject(dc, vorher);
                }
                if (!ok) return false;
                var zeile = r.Breite * 4;
                var bild = BitmapSource.Create(r.Breite, r.Hoehe, 96, 96, PixelFormats.Bgr32, null, bits, zeile * r.Hoehe, zeile);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bild));
                using var datei = File.Create(pfad);
                png.Save(datei);
                return true;
            }
            finally
            {
                DeleteObject(bitmap);
            }
        }
        finally
        {
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bildkopf
    {
        public uint Groesse;
        public int Breite;
        public int Hoehe;
        public ushort Ebenen;
        public ushort Bits;
        public uint Kompression;
        public uint Bildgroesse;
        public int ProMeterX;
        public int ProMeterY;
        public uint Farben;
        public uint Wichtig;
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateDIBSection(nint dc, ref Bildkopf kopf, uint verwendung, out nint bits, nint abschnitt, uint versatz);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint objekt);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint objekt);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint fenster, nint dc, uint flags);
}
