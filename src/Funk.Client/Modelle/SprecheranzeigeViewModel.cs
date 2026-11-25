// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;

namespace Funk.Client.Modelle;

/// <summary>
/// Der Abschnitt Sprecheranzeige der Einstellungen (seit 0.11.9, zwischen Funkfeld und Darstellung und Fehlersuche; Sarevoc 11.10. und 15.10.2026). Offen
/// stehen der Schalter "Sprecheranzeige an" (ab Werk an) und unter "Wo im Bild" zwei Schieberegler, Waagerecht und Senkrecht, je 0 bis 100 % des
/// Monitors (ab Werk 0 und 0, ganz oben links; Sarevoc 15.10.2026: "die Position mit Schieberegler einstellen, nicht fest vorgeschlagen", bis dahin ein
/// Raster mit neun Feldern). Solange ein Regler bewegt wird, steht die Probezeile live an der neuen Stelle über dem Spiel, und noch 3 s danach
/// (<see cref="ProbeVerlangt"/> bei jedem Schieben; kein eigener Knopf). Unter Erweitert
/// Größe, Durchsichtigkeit der Schrift, Zeilen, Nachhalten, Ausblenden, eigene Sendung und Monitor, daneben die Vorschau "So sieht sie aus"
/// (<see cref="Vorschau"/>) auf neutralem dunklem Grund. Gemerkt in <see cref="ClientEinstellungen.Sprecheranzeige"/>. Seit dem 19.10.2026 steht dort
/// auch das Spiel (Sarevoc Q10, <see cref="ClientEinstellungen.Spiel"/>): Star Citizen ab Werk oder ein anderes über seine Programmdatei; danach suchen
/// Sprecheranzeige, Meldungen und Meldezeile den Monitor. Läuft das Spiel im
/// exklusiven Vollbild, sagt es der Kasten des Abschnitts (<see cref="Vollbild"/>, alle <see cref="VollbildAbstandMs"/> geprüft); eine Meldezeile
/// gibt es dafür nicht (Sarevoc 13.10.2026: kein Spam).
/// </summary>
public sealed partial class SprecheranzeigeViewModel : ObservableObject
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Der Kasten "Läuft von selbst" des Abschnitts.</summary>
    public const string AutomatikText = "Läuft von selbst: Sie erscheint nur, solange jemand spricht, auf dem Monitor des Spiels.";

    /// <summary>Der Hinweis im Kasten, solange etwas im exklusiven Vollbild läuft (Vorgabe vom 15.10.2026; seit dem Spielprofil "Das Spiel" statt "Star Citizen").</summary>
    public const string VollbildText = "Das Spiel läuft im Vollbild; die Anzeige erscheint nur bei Randlos";

    /// <summary>Der Satz unter dem Schalter.</summary>
    public const string Erklaerung = "Über dem Spiel steht, wer gerade spricht und in welchem Kanal. Das Spiel muss dafür randlos laufen.";

    /// <summary>Hovertext am Schalter.</summary>
    public const string AnHilfe = "Sprecheranzeige: Solange jemand zu hören ist, steht über dem Spiel sein Name und der Kanal, etwa \"Brücke (Schiff 1)\"; "
        + "eine Durchsage als \"Durchsage an alle\", Global als \"Global\". Gezeigt wird nur, wen du wirklich hörst: abgeschaltete Kanäle und Funker, "
        + "die du stumm geschaltet hast, nicht. Die Anzeige nimmt nie den Fokus, lässt Klicks hindurch und ist nur zu sehen, solange jemand spricht. "
        + "Über dem Spiel erscheint sie nur im randlosen Fenstermodus. Ab Werk an.";

    /// <summary>Hovertext am Regler Waagerecht.</summary>
    public const string WaagerechtHilfe = "Waagerecht: Schiebt die Sprecheranzeige von links (0 Prozent) nach rechts (100 Prozent) über den Monitor. Die linke "
        + "obere Ecke der Schrift folgt dem Regler; ganz rechts bleibt sie so weit innen, dass nichts aus dem Monitor ragt. Solange du schiebst, steht "
        + "die Probezeile genau dort über dem Spiel, und noch 3 Sekunden danach. Mit den Pfeiltasten in ganzen Prozent, mit Bild auf und ab in "
        + "Zehnern. Ab Werk 0 Prozent, ganz links.";

    /// <summary>Hovertext am Regler Senkrecht.</summary>
    public const string SenkrechtHilfe = "Senkrecht: Schiebt die Sprecheranzeige von oben (0 Prozent) nach unten (100 Prozent) über den Monitor. Die linke "
        + "obere Ecke der Schrift folgt dem Regler; ganz unten bleibt sie so weit innen, dass nichts aus dem Monitor ragt, auch mit mehreren Zeilen. "
        + "Solange du schiebst, steht die Probezeile genau dort über dem Spiel, und noch 3 Sekunden danach. Ab Werk 0 Prozent, ganz oben.";

    /// <summary>Der Satz unter den Reglern.</summary>
    public const string LageSatz = "0 % und 0 % ist ganz oben links. Beim Schieben steht die Probezeile über dem Spiel.";

    /// <summary>Hovertext am Regler Größe.</summary>
    public const string GroesseHilfe = "Größe: Macht die Schrift der Sprecheranzeige samt Schatten kleiner oder größer, von 60 bis 200 Prozent. Größer, "
        + "wenn du die Namen auf einem großen oder weit entfernten Monitor schlecht liest; kleiner, wenn sie dir zu viel vom Spiel verdecken. Ab Werk 100 Prozent.";

    /// <summary>Hovertext am Regler Durchsichtigkeit.</summary>
    public const string DurchsichtigkeitHilfe = "Durchsichtigkeit der Schrift: Lässt Schrift und Schatten durchscheinen, bis 80 Prozent. Ab Werk deckend; "
        + "über hellem Spielbild liest sich deckende Schrift am besten.";

    /// <summary>Hovertext an den Zeilen.</summary>
    public const string ZeilenHilfe = "Höchstens so viele Zeilen: Wie viele Sprecher gleichzeitig dastehen, von 1 bis 4. Wer zuerst sprach, steht oben. Bei "
        + "einer Zeile steht genau ein Name; bei mehr Zeilen und mehr Sprechern sagt die letzte \"+n weitere\". Ab Werk 1.";

    /// <summary>Hovertext am Regler Nachhalten.</summary>
    public const string NachhaltenHilfe = "Nachhalten: So lange bleibt ein Name nach dem letzten Wort noch stehen, von 0 bis 5 Sekunden. Länger, wenn dir ein "
        + "Name zu schnell verschwindet; kürzer, wenn der nächste Sprecher zu lange wartet. Ab Werk 1,0 Sekunden.";

    /// <summary>Hovertext am Regler Ausblenden.</summary>
    public const string AusblendenHilfe = "Ausblenden: So lange blendet ein Name nach dem Nachhalten aus, von 0 bis 2 Sekunden; 0 heißt, er ist sofort fort. "
        + "Ab Werk 0,5 Sekunden.";

    /// <summary>Hovertext am Schalter Eigene Sendung.</summary>
    public const string EigeneSendungHilfe = "Eigene Sendung zeigen: Solange du sendest, steht auch eine Zeile \"Du -> Kanal (Ch.)\" da, in Weiß wie alles. Ab Werk aus.";

    /// <summary>Hovertext an der Wahl des Monitors.</summary>
    public const string MonitorHilfe = "Monitor der Sprecheranzeige: Automatisch nimmt den Monitor, auf dem das Spiel vorn läuft, sonst den zuletzt "
        + "bekannten, sonst den Hauptmonitor. Hauptmonitor und Monitor 1, 2, ... legen ihn fest; ist ein gewählter Monitor nicht angeschlossen, gilt "
        + "der Hauptmonitor. Jede Wahl zeigt die Probezeile 3 Sekunden.";

    /// <summary>Der Satz unter der Vorschau.</summary>
    public const string VorschauSatz = "Name fett, dahinter der Kanal mit Ch.; Durchsage und Global stehen als Wort da. Gezeigt wird nur, wen du wirklich "
        + "hörst; abgeschaltete Kanäle und stumm geschaltete Funker nicht.";

    /// <summary>Hovertext an der Wahl des Spiels.</summary>
    public const string SpielHilfe = "Spiel: Über welchem Spiel Sprecheranzeige, Meldungen und Meldezeile stehen. Ab Werk Star Citizen. Für ein anderes "
        + "Spiel trägst du den Namen seiner Programmdatei ein (etwa Spiel.exe) oder wählst sie mit dem Knopf aus. JG TacRelay sucht damit nur den "
        + "Monitor, auf dem das Spiel läuft; es liest nichts aus dem Spiel und schickt ihm nichts.";

    /// <summary>So oft prüft der Takt das exklusive Vollbild.</summary>
    public const long VollbildAbstandMs = 2000;

    private readonly Einstellungsablage _ablage;
    private readonly Func<IReadOnlyList<Bildschirm>> _bildschirme;
    private readonly Func<bool> _vollbild;
    private readonly Func<long> _uhr;
    private readonly Func<string?>? _dateiWaehlen;
    private long _vollbildZuletzt = long.MinValue;

    /// <param name="bildschirme">Die Monitore, wie Windows sie gerade aufzählt (nur gelesen); in Tests eine feste Liste.</param>
    /// <param name="vollbild">Läuft etwas im exklusiven Vollbild (im Betrieb <c>Spielfenster.ExklusivesVollbild</c>); ohne Angabe nie.</param>
    /// <param name="uhr">Millisekunden für den Abstand der Vollbildprüfung; ohne Angabe <c>Environment.TickCount64</c>.</param>
    /// <param name="dateiWaehlen">"Programmdatei wählen ..." beim Spiel (im Betrieb der Dateidialog); ohne Angabe ist der Knopf gesperrt.</param>
    public SprecheranzeigeViewModel(Einstellungsablage ablage, Func<IReadOnlyList<Bildschirm>> bildschirme, Func<bool>? vollbild = null, Func<long>? uhr = null,
        Func<string?>? dateiWaehlen = null)
    {
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(bildschirme);
        _ablage = ablage;
        _bildschirme = bildschirme;
        _vollbild = vollbild ?? (() => false);
        _uhr = uhr ?? (() => Environment.TickCount64);
        _dateiWaehlen = dateiWaehlen;
        MonitoreLesen();
        VorschauBauen();
    }

    private SprecheranzeigeEinstellungen E => _ablage.Aktuell.Sprecheranzeige;

    // ---- offen ----

    /// <summary>"Sprecheranzeige an"; ab Werk an.</summary>
    public bool An
    {
        get => E.An;
        set
        {
            if (value == An) return;
            E.An = value;
            Gemerkt();
        }
    }

    /// <summary>Waagerecht in Prozent des Monitors für den Regler (0 links bis 100 rechts, ganze Prozent); setzen merkt es und zeigt die Probe dort.</summary>
    public double Waagerecht
    {
        get => E.Waagerecht;
        set
        {
            if (!double.IsFinite(value)) return;
            var neu = (int)Math.Clamp(Math.Round(value), 0, SprecheranzeigeEinstellungen.LageMax);
            if (neu == E.Waagerecht) return;
            E.Waagerecht = neu;
            LageGemerkt(nameof(Waagerecht), nameof(WaagerechtText));
        }
    }

    /// <summary>Wert neben dem Regler, etwa "12 %".</summary>
    public string WaagerechtText => Prozent(E.Waagerecht);

    /// <summary>Senkrecht in Prozent des Monitors für den Regler (0 oben bis 100 unten, ganze Prozent); setzen merkt es und zeigt die Probe dort.</summary>
    public double Senkrecht
    {
        get => E.Senkrecht;
        set
        {
            if (!double.IsFinite(value)) return;
            var neu = (int)Math.Clamp(Math.Round(value), 0, SprecheranzeigeEinstellungen.LageMax);
            if (neu == E.Senkrecht) return;
            E.Senkrecht = neu;
            LageGemerkt(nameof(Senkrecht), nameof(SenkrechtText));
        }
    }

    /// <summary>Wert neben dem Regler, etwa "30 %".</summary>
    public string SenkrechtText => Prozent(E.Senkrecht);

    /// <summary>Nach jedem Schieben: merken, melden, das Fenster zieht um (<see cref="Wahl"/>), und die Probezeile steht dort noch 3 s.</summary>
    private void LageGemerkt(string wert, string text)
    {
        _ablage.Vormerken();
        OnPropertyChanged(wert);
        OnPropertyChanged(text);
        OnPropertyChanged(nameof(Wahl));
        Probe();
    }

    /// <summary>Lage und Monitor zusammen, für das Fenster.</summary>
    public Sprecherlage Wahl => new(E.Waagerecht, E.Senkrecht, Monitor);

    // ---- Erweitert ----

    /// <summary>Größe in Prozent für den Regler (60 bis 200, Schritte zu 10).</summary>
    public double Groesse
    {
        get => E.Groesse;
        set
        {
            if (!double.IsFinite(value)) return;
            var neu = (int)Math.Clamp(Math.Round(value / 10) * 10, SprecheranzeigeEinstellungen.GroesseMin, SprecheranzeigeEinstellungen.GroesseMax);
            if (neu == E.Groesse) return;
            E.Groesse = neu;
            Gemerkt();
        }
    }

    /// <summary>Wert neben dem Regler, etwa "100 %".</summary>
    public string GroesseText => Prozent(E.Groesse);

    /// <summary>Durchsichtigkeit der Schrift in Prozent für den Regler (0 bis 80, Schritte zu 5).</summary>
    public double Durchsichtigkeit
    {
        get => E.Durchsichtigkeit;
        set
        {
            if (!double.IsFinite(value)) return;
            var neu = (int)Math.Clamp(Math.Round(value / 5) * 5, 0, SprecheranzeigeEinstellungen.DurchsichtigkeitMax);
            if (neu == E.Durchsichtigkeit) return;
            E.Durchsichtigkeit = neu;
            Gemerkt();
        }
    }

    /// <summary>Wert neben dem Regler, etwa "0 %".</summary>
    public string DurchsichtigkeitText => Prozent(E.Durchsichtigkeit);

    /// <summary>Höchstens so viele Zeilen, 1 bis 4.</summary>
    public int Zeilen
    {
        get => E.Zeilen;
        set
        {
            var neu = Math.Clamp(value, SprecheranzeigeEinstellungen.ZeilenMin, SprecheranzeigeEinstellungen.ZeilenMax);
            if (neu == E.Zeilen) return;
            E.Zeilen = neu;
            Gemerkt();
        }
    }

    /// <summary>Die vier Knöpfe der Zeilen (1, 2, 3, 4); wahr setzen wählt, falsch setzen tut nichts.</summary>
    public bool Zeilen1
    {
        get => Zeilen == 1;
        set
        {
            if (value) Zeilen = 1;
        }
    }

    /// <inheritdoc cref="Zeilen1"/>
    public bool Zeilen2
    {
        get => Zeilen == 2;
        set
        {
            if (value) Zeilen = 2;
        }
    }

    /// <inheritdoc cref="Zeilen1"/>
    public bool Zeilen3
    {
        get => Zeilen == 3;
        set
        {
            if (value) Zeilen = 3;
        }
    }

    /// <inheritdoc cref="Zeilen1"/>
    public bool Zeilen4
    {
        get => Zeilen == 4;
        set
        {
            if (value) Zeilen = 4;
        }
    }

    /// <summary>Der Satz unter den Zeilen.</summary>
    public string ZeilenSatz => Zeilen == 1
        ? "Es steht genau ein Name: wer zuerst sprach. Hört er auf, rückt der nächste nach."
        : "Sprechen mehr, steht in der letzten Zeile \"+n weitere\". Wer zuerst sprach, bleibt oben.";

    /// <summary>Nachhalten in Sekunden für den Regler (0 bis 5, Schritte zu 0,1).</summary>
    public double Nachhalten
    {
        get => E.NachhaltenMs / 1000.0;
        set
        {
            if (!double.IsFinite(value)) return;
            var neu = (int)Math.Clamp(Math.Round(value * 10) * 100, 0, SprecheranzeigeEinstellungen.NachhaltenMaxMs);
            if (neu == E.NachhaltenMs) return;
            E.NachhaltenMs = neu;
            Gemerkt();
        }
    }

    /// <summary>Wert neben dem Regler, etwa "1,0 s".</summary>
    public string NachhaltenText => Sekunden(E.NachhaltenMs);

    /// <summary>Ausblenden in Sekunden für den Regler (0 bis 2, Schritte zu 0,1).</summary>
    public double Ausblenden
    {
        get => E.AusblendenMs / 1000.0;
        set
        {
            if (!double.IsFinite(value)) return;
            var neu = (int)Math.Clamp(Math.Round(value * 10) * 100, 0, SprecheranzeigeEinstellungen.AusblendenMaxMs);
            if (neu == E.AusblendenMs) return;
            E.AusblendenMs = neu;
            Gemerkt();
        }
    }

    /// <summary>Wert neben dem Regler, etwa "0,5 s".</summary>
    public string AusblendenText => Sekunden(E.AusblendenMs);

    /// <summary>"Eigene Sendung zeigen"; ab Werk aus.</summary>
    public bool EigeneSendung
    {
        get => E.EigeneSendung;
        set
        {
            if (value == EigeneSendung) return;
            E.EigeneSendung = value;
            Gemerkt();
        }
    }

    /// <summary>Die Monitore zur Wahl wie bei der Meldezeile: Automatisch, Hauptmonitor, Monitor 1 bis n, ein fehlender als "nicht angeschlossen".</summary>
    public ObservableCollection<Monitorwahl> Monitore { get; } = [];

    /// <summary>Der Monitor: <c>null</c> Automatisch, 0 Hauptmonitor, n der n-te; setzen merkt ihn und zeigt die Probe.</summary>
    public int? Monitor
    {
        get => E.Monitor;
        set
        {
            var neu = value is >= 0 and <= FunkfeldEinstellungen.MeldezeileMonitorMax ? value : null;
            if (neu == Monitor) return;
            E.Monitor = neu;
            _ablage.Vormerken();
            MonitoreLesen();
            OnPropertyChanged();
            OnPropertyChanged(nameof(GewaehlterMonitor));
            OnPropertyChanged(nameof(Wahl));
            Probe();
        }
    }

    /// <summary>Die gewählte Zeile der Monitorliste; <c>null</c> setzt nichts (WPF leert die Wahl beim Neufüllen der Liste).</summary>
    public Monitorwahl? GewaehlterMonitor
    {
        get => Monitore.FirstOrDefault(m => m.Wert == Monitor);
        set
        {
            if (value is not null) Monitor = value.Wert;
        }
    }

    /// <summary>Liest die Monitore neu (wenn Erweitert aufgeht); die Wahl bleibt, was gemerkt ist.</summary>
    public void MonitoreLesen()
    {
        var liste = MeldezeilePlatzViewModel.Monitorliste(_bildschirme(), Monitor);
        if (liste.Select(w => w.Name).SequenceEqual(Monitore.Select(w => w.Name), StringComparer.Ordinal)) return;
        Monitore.Clear();
        foreach (var w in liste) Monitore.Add(w);
        OnPropertyChanged(nameof(GewaehlterMonitor));
    }

    // ---- Spiel (Umbau 19.10.2026, Sarevoc Q10) ----

    private SpielEinstellungen S => _ablage.Aktuell.Spiel;

    /// <summary>"Star Citizen (ab Werk)" gewählt; setzen auf wahr nimmt wieder Star Citizen.</summary>
    public bool SpielStarCitizen
    {
        get => !S.Anderes;
        set
        {
            if (value) SpielAnderes = false;
        }
    }

    /// <summary>"Anderes Spiel" gewählt; der Prozessname bleibt gemerkt, auch wenn wieder Star Citizen gilt.</summary>
    public bool SpielAnderes
    {
        get => S.Anderes;
        set
        {
            if (value == S.Anderes) return;
            S.Anderes = value;
            SpielGemerkt();
        }
    }

    /// <summary>
    /// Der Prozessname des anderen Spiels für das Textfeld. Eingetippt oder gewählt darf auch die Programmdatei mit Pfad stehen;
    /// gemerkt wird der Name ohne Pfad und ohne ".exe" (<see cref="Spielprofil.Prozessname"/>). Ein gültiger Name wählt das andere Spiel mit;
    /// ein ungültiger lässt den gemerkten stehen.
    /// </summary>
    public string SpielProzess
    {
        get => S.Prozess ?? "";
        set
        {
            var neu = Spielprofil.Prozessname(value);
            if (neu is null || (neu == S.Prozess && S.Anderes))
            {
                OnPropertyChanged();
                return;
            }
            S.Prozess = neu;
            S.Anderes = true;
            SpielGemerkt();
        }
    }

    /// <summary>Für "selbst gestellt" und "Zurück auf Werkseinstellung": leer bei Star Citizen, sonst der Prozessname des anderen Spiels.</summary>
    public string SpielWert
    {
        get => S.Anderes && S.Prozess is { } p ? p : "";
        set
        {
            if (Spielprofil.Prozessname(value) is not { } neu)
            {
                SpielAnderes = false;
                return;
            }
            SpielProzess = neu;
        }
    }

    /// <summary>Der Satz unter der Wahl: nach welchem Programm gerade gesucht wird.</summary>
    public string SpielSatz => S.Anderes
        ? S.Prozess is { } p
            ? "Gesucht wird " + p + ".exe; Sprecheranzeige, Meldungen und Meldezeile stehen auf seinem Monitor."
            : "Trag die Programmdatei des Spiels ein oder wähle sie aus; bis dahin gilt Star Citizen."
        : "Gesucht wird Star Citizen (StarCitizen.exe, ohne den Launcher).";

    /// <summary>"Programmdatei wählen ...": der Dateidialog; der Pfad landet als Prozessname im Feld.</summary>
    [RelayCommand(CanExecute = nameof(KannDateiWaehlen))]
    private void SpielDateiWaehlen()
    {
        if (_dateiWaehlen?.Invoke() is { } pfad) SpielProzess = pfad;
    }

    private bool KannDateiWaehlen() => _dateiWaehlen is not null;

    /// <summary>Für "selbst gestellt": "Spiel Star Citizen" oder "Spiel Spiel.exe".</summary>
    public static string SpielText(string wert) => wert.Length == 0 ? "Spiel Star Citizen" : "Spiel " + wert + ".exe";

    private void SpielGemerkt()
    {
        _ablage.Vormerken();
        OnPropertyChanged(nameof(SpielStarCitizen));
        OnPropertyChanged(nameof(SpielAnderes));
        OnPropertyChanged(nameof(SpielProzess));
        OnPropertyChanged(nameof(SpielWert));
        OnPropertyChanged(nameof(SpielSatz));
    }

    /// <summary>"So sieht sie aus": dieselbe Vorlage wie über dem Spiel, mit Beispielzeilen nach Zeilen und eigener Sendung, in Größe und Durchsichtigkeit.</summary>
    public Sprecherbild Vorschau { get; } = new();

    // ---- Vollbild ----

    /// <summary>Läuft gerade etwas im exklusiven Vollbild (dann ist die Anzeige über dem Spiel nicht zu sehen)?</summary>
    [ObservableProperty]
    public partial bool Vollbild { get; private set; }

    /// <summary>Bildtakt: höchstens alle <see cref="VollbildAbstandMs"/> das exklusive Vollbild prüfen.</summary>
    public void Takt()
    {
        var jetzt = _uhr();
        if (_vollbildZuletzt != long.MinValue && jetzt - _vollbildZuletzt < VollbildAbstandMs) return;
        _vollbildZuletzt = jetzt;
        Vollbild = _vollbild();
    }

    // ---- "selbst gestellt" ----

    /// <summary>"Monitor automatisch", "Monitor: Hauptmonitor" oder "Monitor 2" für "selbst gestellt".</summary>
    public static string MonitorText(int? wahl) => wahl is null ? "Monitor automatisch" : wahl == 0 ? "Monitor: Hauptmonitor"
        : "Monitor " + wahl.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Prozent ohne Nachkommastelle, etwa "120 %".</summary>
    public static string Prozent(double p) => p.ToString("0", CultureInfo.InvariantCulture) + " %";

    /// <summary>Sekunden mit einer Stelle und Komma, etwa "1,5 s".</summary>
    public static string Sekunden(int ms) => (ms / 1000.0).ToString("0.0", De) + " s";

    /// <summary>Die Probezeile über dem Spiel zeigen; das Fenster hängt daran (App.xaml.cs).</summary>
    public event Action? ProbeVerlangt;

    /// <summary>Nach jedem Schieben an der Lage und jeder Wahl des Monitors.</summary>
    private void Probe() => ProbeVerlangt?.Invoke();

    /// <summary>Merkt vor, meldet alle abhängigen Werte und baut die Vorschau neu.</summary>
    private void Gemerkt()
    {
        _ablage.Vormerken();
        OnPropertyChanged(string.Empty);
        VorschauBauen();
    }

    /// <summary>
    /// Die Vorschau: so viele Beispielzeilen, wie Zeilen eingestellt sind (wie im Bild vom 15.10.2026: Funker 2 in Brücke, Funker 3 mit einer
    /// Durchsage an alle, mit eigener Sendung "Du -> Bodentrupp (Trupp 2)", dann Funker 4 in Global); Größe und Durchsichtigkeit wie gestellt.
    /// </summary>
    private void VorschauBauen()
    {
        var e = E;
        var beispiele = new List<(string Schluessel, string Name, string Dahinter, double Deckkraft)>
        {
            ("k2", "Funker 2", " Brücke (Schiff 1)", 1),
            ("k3", "Funker 3", " Durchsage an alle", 1),
        };
        if (e.EigeneSendung) beispiele.Add(("du", Sprecheranzeige.DuName, " -> Bodentrupp (Trupp 2)", 1));
        beispiele.Add(("k4", "Funker 4", " Global", 1));
        beispiele.Add(("k5", "Funker 5", " Durchsage: Brücke (Schiff 1)", 1));
        Vorschau.Aussehen(new SprecheranzeigeEinstellungen { Groesse = e.Groesse, Durchsichtigkeit = e.Durchsichtigkeit });
        Vorschau.Setzen([.. beispiele.Take(e.Zeilen)]);
    }
}
