// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Klang;
using Funk.Klang.Maschine;

namespace Funk.Client.Modelle;

/// <summary>
/// Sprachaktivierung einstellen (A26-A28), kein eigenes Fenster; seit 0.10.0 nur noch als Seite (bis 0.9.3 auch unten im Hauptfenster
/// aufklappbar). <see cref="ModusKurz"/> und <see cref="LinieText"/> zeigte die Leiste; sie bleiben, die Statuszeile zeigt sie nicht. Nach Sarevocs Vorgabe
/// vom 29.09.2026 gibt es zwei Schalter statt Modusknöpfen und keine Rückmeldeknöpfe: <see cref="Automatik"/> (die
/// Schwelle bestimmt sich selbst und justiert laufend nach, A27) und <see cref="Lernen"/> (lernende Schwelle innerhalb
/// des Korridors, A28). Gelernt wird still in der Klangmaschine. Ohne Automatik gilt die Linie, die man zieht.
/// Jede Änderung geht sofort an <see cref="IKlangMaschine.Gate"/> und in die Einstellungen; die Livewerte holt
/// <see cref="Takt"/> im Bildtakt aus dem <see cref="PegelVerlauf"/>.
/// <para>
/// Ziehen lässt sich seit Stufe 2c nur, was einen Griff hat (Sarevoc): ohne Automatik die Linie, mit Automatik seit Klangstufe C1 der Korridor
/// relativ zur gelernten Stimme (<see cref="Korridor"/>, Mindest-Funklautstärke links/unten, Obergrenze rechts/oben, derselbe wie am
/// Pegelbalken); er ersetzt die Grenzlinie aus Stufe 2c. Solange die Stimme unbekannt ist, gilt beim Lernen der Korridor von/bis wie bis
/// 0.6.0. Die Schwelle der Automatik ist eine dünne rote Linie und zeigt nur an.
/// </para>
/// </summary>
public sealed partial class SchwellenViewModel : ObservableObject
{
    private static readonly string[] EinstellNamen =
    [
        nameof(Modus), nameof(Automatik), nameof(Lernen), nameof(KannLernen), nameof(ZeigeSchwelle), nameof(ZeigeKorridor),
        nameof(ZeigeAbstand), nameof(ModusErklaerung), nameof(ModusKurz), nameof(GrafikHilfe), nameof(SchwelleDb),
        nameof(AbstandDb), nameof(KorridorVonDb), nameof(KorridorBisDb), nameof(AnstiegSekunden), nameof(RueckkehrSekunden),
        nameof(Sprachsicherheit), nameof(LinieDb), nameof(LinieText),
    ];

    private readonly IKlangMaschine _maschine;
    private readonly Einstellungsablage _ablage;
    private long _gesehen = -1;

    /// <param name="hoerprobe">Dieselbe Hörprobe wie auf Start (bis 0.9.3 im Bereich Audio); ohne Angabe eine eigene.</param>
    /// <param name="korridor">Derselbe Korridor wie am Pegelbalken (Klangstufe C1); ohne Angabe ein eigener.</param>
    public SchwellenViewModel(IKlangMaschine maschine, PegelVerlauf verlauf, Einstellungsablage ablage, HoerprobeViewModel? hoerprobe = null,
        KorridorViewModel? korridor = null)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(verlauf);
        ArgumentNullException.ThrowIfNull(ablage);
        _maschine = maschine;
        Verlauf = verlauf;
        _ablage = ablage;
        Hoerprobe = hoerprobe ?? new HoerprobeViewModel(maschine, ablage);
        Korridor = korridor ?? new KorridorViewModel(maschine, ablage);
        Korridor.Geaendert += AlleGeaendert;
        Korridor.Aktualisiert += () => Aktualisiert?.Invoke();
        Werte.Normalisieren();
        _maschine.Gate = Werte.AlsEinstellung();
        LiveSchwelleDb = Werte.SchwelleDb;
        RauschbodenDb = -70;
        PegelDbfs = GateWerte.MinDb;
    }

    /// <summary>Neue Livewerte sind da; die Grafik zeichnet neu.</summary>
    public event Action? Aktualisiert;

    public PegelVerlauf Verlauf { get; }

    private GateWerte Werte => _ablage.Aktuell.Gate;

    // ---- Schalter (A27, A28) ----

    /// <summary>Wirksamer Modus aus den beiden Schaltern.</summary>
    public GateModus Modus
    {
        get => Werte.Modus;
        set => Aendern(w => w.Modus = value);
    }

    /// <summary>A27: Schwelle automatisch bestimmen und laufend nachjustieren. Aus: feste Linie von Hand.</summary>
    public bool Automatik
    {
        get => Werte.Automatik;
        set => Aendern(w => w.Modus = GateWerte.ModusAus(value, w.Lernen));
    }

    /// <summary>
    /// A28: lernende Schwelle, nur mit Automatik wirksam. Ist die Automatik aus, bleibt die Wahl gemerkt und gilt, sobald
    /// sie wieder an ist.
    /// </summary>
    public bool Lernen
    {
        get => Werte.Lernen;
        set => Aendern(w =>
        {
            w.Lernen = value;
            if (w.Automatik) w.Modus = GateWerte.ModusAus(automatik: true, value);
        });
    }

    /// <summary>Lernen lässt sich nur mit Automatik schalten.</summary>
    public bool KannLernen => Automatik;

    /// <summary>Die feste Schwelle gilt nur ohne Automatik; nur dann hat ihre Linie in der Grafik einen Griff.</summary>
    public bool ZeigeSchwelle => !Automatik;

    /// <summary>
    /// Der Korridor von/bis aus 0.6.0 mit seinen zwei beschrifteten Griffen: nur beim Lernen und nur, solange die Stimme nicht gelernt ist;
    /// danach gilt der Korridor relativ zur Stimme (<see cref="Korridor"/>).
    /// </summary>
    public bool ZeigeKorridor => Modus == GateModus.Lernend && !Korridor.StimmeBekannt;

    /// <summary>Der Abstand zum Rauschboden gilt nur mit Automatik.</summary>
    public bool ZeigeAbstand => Automatik;

    /// <summary>Der Korridor am Pegelbalken und in der Grafik, die Mindest-Funklautstärke und das Einmessen (Klangstufe C1).</summary>
    public KorridorViewModel Korridor { get; }

    public string ModusErklaerung => Modus switch
    {
        GateModus.Fest => "Automatik aus: Die Linie steht, wo du sie hinziehst. Gesendet wird, was lauter ist und nach Sprache klingt.",
        GateModus.Automatisch => "Die Schwelle (rote Linie) folgt dem Grundrauschen mit festem Abstand und justiert sich laufend nach, immer zwischen "
            + "Mindest-Funklautstärke und Obergrenze.",
        _ => "Die Schwelle (rote Linie) lernt still mit: Bei Lärm im Hintergrund steigt sie, danach sinkt sie wieder, immer innerhalb des Korridors.",
    };

    /// <summary>
    /// Tooltip der Grafik, wo kein Teil darunter liegt: was sich darin ziehen lässt und womit (Wörter, keine Zahlen), seit Q43 (0.8.0) dahinter, was
    /// Grün und Gelb heißen (<see cref="KorridorViewModel.HilfeFarben"/>).
    /// </summary>
    public string GrafikHilfe => GriffHilfe + " " + KorridorViewModel.HilfeFarben;

    private string GriffHilfe => Modus switch
    {
        GateModus.Fest => "Die Linie mit dem runden Griff ziehen oder Pfeil hoch/runter (Strg: in großen Schritten).",
        _ when Korridor.StimmeBekannt => "Die rote Linie ist die Schwelle der Automatik, sie bewegt sich selbst. Unten die Mindest-Funklautstärke, oben die "
            + "Obergrenze, je mit runden Griffen: ziehen, oder Pfeil hoch/runter und Bild hoch/runter. In der Zone ziehen verschiebt beide.",
        GateModus.Automatisch => "Die rote Linie ist die Schwelle der Automatik, sie bewegt sich selbst. " + KorridorViewModel.HilfeNichtEingemessen,
        _ => "Die rote Linie ist die gelernte Schwelle, sie bewegt sich selbst. Den Korridor an den Griffen bis und von ziehen oder "
            + "Bild hoch/runter (Strg: von).",
    };

    /// <summary>Kurzform für die Leiste des zugeklappten Bereichs.</summary>
    public string ModusKurz => Modus switch
    {
        GateModus.Fest => "feste Schwelle",
        GateModus.Automatisch => "Automatik",
        _ => "Automatik, lernt",
    };

    // ---- Einstellwerte ----

    public double SchwelleDb
    {
        get => Werte.SchwelleDb;
        set => Aendern(w => w.SchwelleDb = (float)value);
    }

    public double AbstandDb
    {
        get => Werte.AbstandDb;
        set => Aendern(w => w.AbstandDb = (float)value);
    }

    public double KorridorVonDb
    {
        get => Werte.KorridorVonDb;
        set => Aendern(w => w.KorridorVonDb = (float)Math.Min(value, w.KorridorBisDb - GateWerte.KorridorMinBreiteDb));
    }

    public double KorridorBisDb
    {
        get => Werte.KorridorBisDb;
        set => Aendern(w => w.KorridorBisDb = (float)Math.Max(value, w.KorridorVonDb + GateWerte.KorridorMinBreiteDb));
    }

    public double AnstiegSekunden
    {
        get => Werte.AnstiegSekunden;
        set => Aendern(w => w.AnstiegSekunden = (float)value);
    }

    public double RueckkehrSekunden
    {
        get => Werte.RueckkehrSekunden;
        set => Aendern(w => w.RueckkehrSekunden = (float)value);
    }

    /// <summary>0..1; ab diesem Wert gilt ein Rahmen als Sprache.</summary>
    public double Sprachsicherheit
    {
        get => Werte.Sprachsicherheit;
        set => Aendern(w => w.Sprachsicherheit = (float)value);
    }

    // ---- Livewerte ----

    [ObservableProperty]
    public partial double PegelDbfs { get; private set; }

    [ObservableProperty]
    public partial double RauschbodenDb { get; private set; }

    /// <summary>Wirksame Schwelle laut Maschine (mit Automatik bewegt sie sich).</summary>
    [ObservableProperty]
    public partial double LiveSchwelleDb { get; private set; }

    [ObservableProperty]
    public partial double Sprachwahrscheinlichkeit { get; private set; }

    [ObservableProperty]
    public partial bool Sendet { get; private set; }

    /// <summary>ui-fix 0.6.2: Gesprochen, aber es geht nichts hinaus: der Satz dazu (in der Grafik; bis 0.9.3 auch in der Leiste), sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? NichtsGesendet { get; private set; }

    /// <summary>Q43 (0.8.0): Die Sprachaktivierung würde auslösen, aber es geht nichts hinaus; die Kurve der Grafik gelb.</summary>
    [ObservableProperty]
    public partial bool WuerdeSenden { get; private set; }

    /// <summary>Wo die Schwellenlinie gezeichnet wird: ohne Automatik der eingestellte Wert, sonst der wirksame.</summary>
    public double LinieDb => Automatik ? LiveSchwelleDb : SchwelleDb;

    public string LinieText => LinieDb.ToString("0.0", CultureInfo.CurrentCulture) + " dBFS";

    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    /// <summary>Hörprobe als Aufnahme und Mithören beim Senden, dieselbe wie auf Start und unter Senden > Erweitert (bis 0.9.3 im Bereich Audio).</summary>
    public HoerprobeViewModel Hoerprobe { get; }

    // ---- Bedienung der Grafik ----

    /// <summary>
    /// Die Linie wurde auf <paramref name="db"/> gezogen: ohne Automatik die Schwelle. Mit Automatik zeigt die Linie nur an, was
    /// die Maschine nimmt (rote Linie ohne Griff, Stufe 2c), und Ziehen ändert nichts; bis Stufe 2b setzte es den Abstand.
    /// </summary>
    public void LinieZiehen(double db)
    {
        if (Automatik || !double.IsFinite(db)) return;
        SchwelleDb = Math.Clamp(db, GateWerte.MinDb, GateWerte.MaxDb);
    }

    /// <summary>Ein Griff des Korridors von/bis (Lernend, Stimme noch unbekannt) wurde gezogen (<paramref name="oben"/>: bis, sonst von).</summary>
    public void KorridorZiehen(bool oben, double db)
    {
        if (!double.IsFinite(db)) return;
        db = Math.Clamp(db, GateWerte.MinDb, GateWerte.MaxDb);
        if (oben) KorridorBisDb = db;
        else KorridorVonDb = db;
    }

    /// <summary>
    /// Pfeil hoch/runter: die Linie mit dem runden Griff um <paramref name="schritt"/> dB verschieben, also ohne Automatik die
    /// Schwelle, mit Automatik und bekannter Stimme die Mindest-Funklautstärke. Der Korridor von/bis hat Bild hoch/runter.
    /// </summary>
    public void LinieSchieben(double schritt)
    {
        if (ZeigeSchwelle) LinieZiehen(SchwelleDb + schritt);
        else if (Korridor.Sichtbar) Korridor.UntergrenzeSchieben(schritt);
    }

    [RelayCommand]
    private void Zuruecksetzen()
    {
        var modus = Werte.Modus;
        var lernen = Werte.Lernen;
        Werte.Uebernehmen(new GateWerte().AlsEinstellung() with { Modus = modus });
        Werte.Lernen = lernen;
        Anwenden();
        Hinweis = "Standardwerte gesetzt.";
    }

    /// <summary>Holt die jüngste Pegelmeldung; <c>true</c>, wenn es Neues gab.</summary>
    public bool Takt()
    {
        Hoerprobe.Takt();
        var bekannt = Korridor.StimmeBekannt;
        Korridor.Takt();
        if (bekannt != Korridor.StimmeBekannt) AlleGeaendert();
        var gesamt = Verlauf.Gesamt;
        if (gesamt == _gesehen) return false;
        _gesehen = gesamt;
        if (!Verlauf.Letzte(out var m)) return false;
        PegelDbfs = m.PegelDbfs;
        RauschbodenDb = m.RauschbodenDb;
        LiveSchwelleDb = m.SchwelleDb;
        Sprachwahrscheinlichkeit = m.Sprachwahrscheinlichkeit;
        Sendet = m.Sendet;
        WuerdeSenden = m.WuerdeSenden;
        NichtsGesendet = Verlauf.Hinweis(m);
        OnPropertyChanged(nameof(LinieDb));
        OnPropertyChanged(nameof(LinieText));
        Aktualisiert?.Invoke();
        return true;
    }

    private void Aendern(Action<GateWerte> aenderung)
    {
        aenderung(Werte);
        Werte.Normalisieren();
        Anwenden();
    }

    private void Anwenden()
    {
        _maschine.Gate = Werte.AlsEinstellung();
        _ablage.Vormerken();
        AlleGeaendert();
        Korridor.Auffrischen();
    }

    private void AlleGeaendert()
    {
        foreach (var name in EinstellNamen) OnPropertyChanged(name);
        Aktualisiert?.Invoke();
    }
}
