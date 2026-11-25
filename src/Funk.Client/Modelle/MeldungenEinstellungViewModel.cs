// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Der Abschnitt Meldungen der Einstellungen (seit 0.12.0, nach Sprecheranzeige; tmp\melde\SPEZ.md "Client"): offen unter "Wo im Bild" zwei
/// Schieberegler, Waagerecht und Senkrecht, je 0 bis 100 % des Monitors, ab Werk 50 und 0 (oben Mitte); solange geschoben wird, steht die Probe an der
/// neuen Stelle über dem Spiel, und noch 3 s danach (<see cref="ProbeVerlangt"/>, wie bei der Sprecheranzeige). Unter Erweitert die Grundgröße, die
/// Ausführung (Schlicht, Metall, Nur Schrift), das Bild beim Stopp (Handschuh, Xeno) und der Ton; jede Wahl dort zeigt ebenfalls die Probe. Einen
/// Schalter zum ganz Abschalten gibt es nicht (Vorgabe). Gemerkt in <see cref="ClientEinstellungen.Meldungen"/>.
/// </summary>
public sealed partial class MeldungenEinstellungViewModel : ObservableObject
{
    /// <summary>Der Kasten "Läuft von selbst" des Abschnitts.</summary>
    public const string AutomatikText = "Läuft von selbst: Befehle und Hinweise der Leitung erscheinen über dem Spiel, auf dem Monitor des Spiels.";

    /// <summary>Der Satz über den Reglern.</summary>
    public const string Erklaerung = "Wenn die Leitung etwas an alle schickt (Befehl, Zustand, Ampel, Countdown, Bereit-Abfrage), steht es als Grafik über dem Spiel. "
        + "Das Spiel muss dafür randlos laufen.";

    /// <summary>Hovertext am Regler Waagerecht.</summary>
    public const string WaagerechtHilfe = "Waagerecht: Schiebt die Meldungen von links (0 Prozent) über die Mitte (50 Prozent) nach rechts (100 Prozent). Sie bleiben "
        + "immer ganz im Monitor. Solange du schiebst, steht eine Probe genau dort über dem Spiel, und noch 3 Sekunden danach. Mit den Pfeiltasten in "
        + "ganzen Prozent, mit Bild auf und ab in Zehnern. Ab Werk 50 Prozent, die Mitte.";

    /// <summary>Hovertext am Regler Senkrecht.</summary>
    public const string SenkrechtHilfe = "Senkrecht: Schiebt die Meldungen von oben (0 Prozent) nach unten (100 Prozent). Sie bleiben immer ganz im Monitor. Solange du "
        + "schiebst, steht eine Probe genau dort über dem Spiel, und noch 3 Sekunden danach. Ab Werk 0 Prozent, ganz oben.";

    /// <summary>Der Satz unter den Reglern.</summary>
    public const string LageSatz = "50 % und 0 % ist oben in der Mitte. Beim Schieben steht eine Probe über dem Spiel.";

    /// <summary>Hovertext am Regler Grundgröße.</summary>
    public const string GroesseHilfe = "Grundgröße: So groß ist eine Meldung bei \"normal\", die längste Seite des Bilds in Bildpunkten bei 100 Prozent Anzeige, von 100 "
        + "bis 400. Die Leitung kann doppelt oder dreifach senden; nie wird eine Meldung größer als dein Monitor. Zustände stehen halb so groß daneben. "
        + "Ab Werk 180.";

    /// <summary>Hovertext an der Ausführung.</summary>
    public const string AusfuehrungHilfe = "Ausführung der Bilder: Schlicht (ab Werk), Metall mit Rahmen oder Nur Schrift. Gibt es ein Bild in deiner Wahl nicht (Metall "
        + "fehlt für Los, Stopfen und Pause, Nur Schrift gibt es nur für diese drei), kommt es in Schlicht.";

    /// <summary>Hovertext am Stopp-Bild.</summary>
    public const string StoppHilfe = "Bild beim Befehl Stopp: der Handschuh (ab Werk) oder das Xeno-Stoppschild. Das wählt jeder Funker selbst.";

    /// <summary>Hovertext am Schalter Ton.</summary>
    public const string TonHilfe = "Ton: ein kurzer Doppelton bei jedem Befehl und jeder Textmeldung und je Sekunde ein kurzer Ton in den letzten 10 Sekunden "
        + "eines Countdowns, über JG TacRelay (mit der Funk-Lautstärke), selbst erzeugt. Bei Zuständen gibt es keinen. Ab Werk an.";

    /// <summary>Was unter Meldungen > Erweitert steht.</summary>
    public const string InhaltErweitert = "Grundgröße, Ausführung, Bild beim Stopp, Ton";

    private readonly Einstellungsablage _ablage;

    public MeldungenEinstellungViewModel(Einstellungsablage ablage)
    {
        ArgumentNullException.ThrowIfNull(ablage);
        _ablage = ablage;
        var stopp = Meldungskatalog.Motiv(Meldungskatalog.Stopp)!;
        BildHandschuh = Meldungsbilder.Kachel(Meldungskatalog.RessourceVon(Meldungskatalog.Schlicht, stopp.Bilder[0]))?.Bild;
        BildXeno = Meldungsbilder.Kachel(Meldungskatalog.RessourceVon(Meldungskatalog.Schlicht, stopp.Bilder[1]))?.Bild;
        BildSchlicht = Meldungsbilder.Kachel(Meldungskatalog.RessourceVon(Meldungskatalog.Schlicht, "sammeln"))?.Bild;
        BildMetall = Meldungsbilder.Kachel(Meldungskatalog.RessourceVon(Meldungskatalog.Metall, "sammeln"))?.Bild;
        BildSchrift = Meldungsbilder.Kachel(Meldungskatalog.RessourceVon(Meldungskatalog.Schrift, "los"))?.Bild;
    }

    private MeldungenEinstellungen E => _ablage.Aktuell.Meldungen;

    /// <summary>Die Einstellungen, wie sie gerade gelten (für Anzeige und Feld der Leitung).</summary>
    public MeldungenEinstellungen Aktuell => E;

    // ---- offen ----

    /// <summary>Waagerecht in Prozent für den Regler (ganze Prozent); setzen merkt es und zeigt die Probe dort.</summary>
    public double Waagerecht
    {
        get => E.Waagerecht;
        set
        {
            if (!double.IsFinite(value)) return;
            var neu = (int)Math.Clamp(Math.Round(value), 0, MeldungenEinstellungen.LageMax);
            if (neu == E.Waagerecht) return;
            E.Waagerecht = neu;
            Gemerkt(nameof(Waagerecht), nameof(WaagerechtText), nameof(Wahl));
        }
    }

    public string WaagerechtText => SprecheranzeigeViewModel.Prozent(E.Waagerecht);

    /// <summary>Senkrecht in Prozent für den Regler (ganze Prozent); setzen merkt es und zeigt die Probe dort.</summary>
    public double Senkrecht
    {
        get => E.Senkrecht;
        set
        {
            if (!double.IsFinite(value)) return;
            var neu = (int)Math.Clamp(Math.Round(value), 0, MeldungenEinstellungen.LageMax);
            if (neu == E.Senkrecht) return;
            E.Senkrecht = neu;
            Gemerkt(nameof(Senkrecht), nameof(SenkrechtText), nameof(Wahl));
        }
    }

    public string SenkrechtText => SprecheranzeigeViewModel.Prozent(E.Senkrecht);

    /// <summary>Die Lage für das Fenster (immer der Monitor des Spiels, ohne eigene Wahl).</summary>
    public Sprecherlage Wahl => new(E.Waagerecht, E.Senkrecht, null);

    // ---- Erweitert ----

    /// <summary>Grundgröße in Einheiten für den Regler (100 bis 400, Schritte zu 10).</summary>
    public double Grundgroesse
    {
        get => E.Grundgroesse;
        set
        {
            if (!double.IsFinite(value)) return;
            var neu = (int)Math.Clamp(Math.Round(value / 10) * 10, MeldungenEinstellungen.GrundgroesseMin, MeldungenEinstellungen.GrundgroesseMax);
            if (neu == E.Grundgroesse) return;
            E.Grundgroesse = neu;
            Gemerkt(nameof(Grundgroesse), nameof(GrundgroesseText));
        }
    }

    /// <summary>Wert neben dem Regler, etwa "180".</summary>
    public string GrundgroesseText => E.Grundgroesse.ToString(CultureInfo.InvariantCulture);

    /// <summary>Ausführung als Nummer (0 Schlicht, 1 Metall, 2 Nur Schrift).</summary>
    public int Ausfuehrung
    {
        get => E.Ausfuehrung;
        set
        {
            var neu = Math.Clamp(value, MeldungenEinstellungen.AusfuehrungSchlicht, MeldungenEinstellungen.AusfuehrungSchrift);
            if (neu == E.Ausfuehrung) return;
            E.Ausfuehrung = neu;
            Gemerkt(nameof(Ausfuehrung), nameof(AusfuehrungSchlicht), nameof(AusfuehrungMetall), nameof(AusfuehrungSchrift));
        }
    }

    public bool AusfuehrungSchlicht
    {
        get => Ausfuehrung == MeldungenEinstellungen.AusfuehrungSchlicht;
        set
        {
            if (value) Ausfuehrung = MeldungenEinstellungen.AusfuehrungSchlicht;
        }
    }

    public bool AusfuehrungMetall
    {
        get => Ausfuehrung == MeldungenEinstellungen.AusfuehrungMetall;
        set
        {
            if (value) Ausfuehrung = MeldungenEinstellungen.AusfuehrungMetall;
        }
    }

    public bool AusfuehrungSchrift
    {
        get => Ausfuehrung == MeldungenEinstellungen.AusfuehrungSchrift;
        set
        {
            if (value) Ausfuehrung = MeldungenEinstellungen.AusfuehrungSchrift;
        }
    }

    /// <summary>Stopp-Bild als Nummer (0 Handschuh, 1 Xeno).</summary>
    public int StoppBild
    {
        get => E.StoppBild;
        set
        {
            var neu = Math.Clamp(value, MeldungenEinstellungen.StoppHandschuh, MeldungenEinstellungen.StoppXeno);
            if (neu == E.StoppBild) return;
            E.StoppBild = neu;
            Gemerkt(nameof(StoppBild), nameof(StoppHandschuh), nameof(StoppXeno));
        }
    }

    public bool StoppHandschuh
    {
        get => StoppBild == MeldungenEinstellungen.StoppHandschuh;
        set
        {
            if (value) StoppBild = MeldungenEinstellungen.StoppHandschuh;
        }
    }

    public bool StoppXeno
    {
        get => StoppBild == MeldungenEinstellungen.StoppXeno;
        set
        {
            if (value) StoppBild = MeldungenEinstellungen.StoppXeno;
        }
    }

    /// <summary>Ton bei Befehlen, Textmeldungen und in den letzten 10 s eines Countdowns; ab Werk an.</summary>
    public bool Ton
    {
        get => E.Ton;
        set
        {
            if (value == E.Ton) return;
            E.Ton = value;
            _ablage.Vormerken();
            OnPropertyChanged();
        }
    }

    // ---- Bilder für die Wahl ----

    public ImageSource? BildHandschuh { get; }

    public ImageSource? BildXeno { get; }

    public ImageSource? BildSchlicht { get; }

    public ImageSource? BildMetall { get; }

    public ImageSource? BildSchrift { get; }

    // ---- "selbst gestellt" ----

    /// <summary>"Ausführung Metall", "Ausführung Nur Schrift".</summary>
    public static string AusfuehrungText(int a) => "Ausführung " + (a == MeldungenEinstellungen.AusfuehrungMetall ? "Metall"
        : a == MeldungenEinstellungen.AusfuehrungSchrift ? "Nur Schrift" : "Schlicht");

    /// <summary>"Stopp: Xeno".</summary>
    public static string StoppText(int s) => "Stopp: " + (s == MeldungenEinstellungen.StoppXeno ? "Xeno" : "Handschuh");

    /// <summary>
    /// Die Probe über dem Spiel zeigen: ein Befehl in der neuen Lage und Größe (<c>null</c>: Sammeln), beim Stopp-Bild der Stopp. Das Fenster hängt
    /// daran (App.xaml.cs).
    /// </summary>
    public event Action<string?>? ProbeVerlangt;

    /// <summary>Merkt vor, meldet die Werte und zeigt die Probe (beim Stopp-Bild mit dem Stopp).</summary>
    private void Gemerkt(params string[] namen)
    {
        _ablage.Vormerken();
        foreach (var n in namen) OnPropertyChanged(n);
        ProbeVerlangt?.Invoke(namen.Contains(nameof(StoppBild)) ? Meldungskatalog.Stopp : null);
    }
}
