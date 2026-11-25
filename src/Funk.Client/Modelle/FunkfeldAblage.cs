// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Client.Dienste;

namespace Funk.Client.Modelle;

/// <summary>
/// Das Funkfeld-Fenster, wie die Ablage es braucht; im Betrieb die App über dem echten <c>FunkfeldFenster</c>, in Tests eine Attrappe.
/// Zeigen über Taste und Symbol geht nie mit Fokus: Star Citizen bleibt vorn.
/// </summary>
public interface IFunkfeldFenster
{
    /// <summary>Das Fenster ist offen und nicht verborgen.</summary>
    bool Sichtbar { get; }

    /// <summary>Zeigen ohne Fokus (ShowActivated=false, kein Activate), oben bleiben; baut das Fenster bei Bedarf.</summary>
    void ZeigenOhneFokus();

    /// <summary>Zeigen und nach vorn holen; nur aus dem Hauptfenster (Knopf, Strg+F), wo der Fokus ohnehin bei uns ist.</summary>
    void ZeigenMitFokus();

    /// <summary>Verbergen (Hide): Lage, Größe, Ansicht und Durchsichtigkeit bleiben.</summary>
    void Verbergen();

    /// <summary>Wirklich schließen (Programmende).</summary>
    void Schliessen();

    /// <summary>Für eine Farbstellung neu bauen; <paramref name="zeigen"/> falsch: abgelegt neu bauen (erst beim nächsten Zeigen).</summary>
    void NeuAufbauen(bool zeigen);
}

/// <summary>Ein Eintrag im Menü des Symbols.</summary>
public sealed record Menuepunkt(string Schluessel, string Text);

/// <summary>
/// Das Symbol im Infobereich der Taskleiste; im Betrieb <c>Ablagesymbol</c> (Shell_NotifyIconW), in Tests eine Attrappe. Kein Test
/// legt ein echtes Symbol an.
/// </summary>
public interface IAblagesymbol : IDisposable
{
    /// <summary>Anmelden mit Tooltip (auch erneut, etwa nach einem Neustart des Explorers).</summary>
    void Anmelden(string tooltip);

    void TooltipSetzen(string tooltip);

    /// <summary>Abmelden (NIM_DELETE).</summary>
    void Entfernen();

    /// <summary>Linksklick auf das Symbol.</summary>
    event Action? Linksklick;

    /// <summary>Ein Eintrag des Menüs wurde gewählt (Schlüssel aus <see cref="Menue"/>).</summary>
    event Action<string>? Gewaehlt;

    /// <summary>Baut das Menü beim Rechtsklick, damit es den Stand gerade zeigt.</summary>
    Func<IReadOnlyList<Menuepunkt>>? Menue { get; set; }
}

/// <summary>
/// Das Funkfeld in der Ablage (RAID-S2-ENTWURF Abschnitt 5): Abgelegt heißt verborgen, nicht geschlossen. Eine frei belegbare Taste
/// oder ein Joystickknopf ("Funkfeld ablegen/holen") und der Linksklick auf das Symbol im Infobereich schalten um, ohne Fokus: Das
/// Funkfeld zeigt sich, ohne Star Citizen den Fokus zu nehmen, und bleibt oben. Das X der Titelzeile legt ab
/// (<see cref="SchliessenAngefragt"/>); wirklich geschlossen wird es nur mit dem Programm. Der Neuaufbau für eine Farbstellung
/// bleibt abgelegt, wenn es abgelegt war. Das Symbol bleibt, solange das Programm läuft; sein Tooltip nennt Stand und Sendenetz,
/// höchstens einmal je Sekunde neu und nur bei einem Wechsel; sein Menü: zeigen oder ablegen, seit 0.10.0 "Mikro stumm" oder "Mikro wieder an",
/// Hauptfenster, für Admins "Heute als
/// Funker" (öffnet die Rückfrage im Hauptfenster) bzw. "Wieder Admin" (ein Klick), Beenden. Beim ersten Ablegen ein Hinweis in der Meldezeile, wo das Symbol liegt (einmal, gemerkt).
/// <para>Nur auf dem Oberflächen-Thread; Tasten und Joystick kommen über den Oberflächentakt.</para>
/// </summary>
public sealed class FunkfeldAblage
{
    /// <summary>Schlüssel des ersten Hinweises in der Meldezeile.</summary>
    public const string HinweisSchluessel = "ablage";

    private const int TooltipAbstandMs = 1000;

    private readonly IFunkfeldFenster _fenster;
    private readonly IAblagesymbol? _symbol;
    private readonly Einstellungsablage _ablage;
    private readonly Meldezeile _zeile;
    private readonly IFunkDienst _dienst;
    private readonly Func<long> _uhr;
    private readonly FunkermodusViewModel? _funkermodus;
    private bool _programmEnde;
    private bool _gestartet;
    private string _tooltip = "";
    private long _tooltipZuletzt = long.MinValue / 2;

    public FunkfeldAblage(IFunkfeldFenster fenster, IAblagesymbol? symbol, Einstellungsablage ablage, Meldezeile zeile, IFunkDienst dienst, Func<long> uhrMs,
        FunkermodusViewModel? funkermodus = null)
    {
        ArgumentNullException.ThrowIfNull(fenster);
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(zeile);
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(uhrMs);
        _fenster = fenster;
        _symbol = symbol;
        _ablage = ablage;
        _zeile = zeile;
        _dienst = dienst;
        _uhr = uhrMs;
        _funkermodus = funkermodus;
        if (_symbol is not null)
        {
            _symbol.Linksklick += Umschalten;
            _symbol.Gewaehlt += Gewaehlt;
            _symbol.Menue = Menue;
        }
    }

    /// <summary>Das Funkfeld liegt im Infobereich.</summary>
    public bool Abgelegt { get; private set; }

    /// <summary>"Hauptfenster" im Menü des Symbols.</summary>
    public Action? HauptfensterZeigen { get; set; }

    /// <summary>"Heute als Funker" im Menü: Hauptfenster auf "Ich", wo die Rückfrage steht; ohne diesen Weg nur das Hauptfenster.</summary>
    public Action? FunkermodusZeigen { get; set; }

    /// <summary>"Beenden" im Menü des Symbols (das Programm endet mit dem Hauptfenster).</summary>
    public Action? Beenden { get; set; }

    /// <summary>Ob das Mikro gerade stumm ist, für "Mikro stumm" oder "Mikro wieder an" im Menü (seit 0.10.0); ohne diesen Weg fehlt der Eintrag.</summary>
    public Func<bool>? MikroStumm { get; set; }

    /// <summary>
    /// "Mikro stumm" und "Mikro wieder an" im Menü des Symbols (seit 0.10.0, Endfassung: auch ohne Funkfeld und ohne belegte Taste per Maus): wie die
    /// Taste "Mikro stumm", mit dem Satz in der Meldezeile.
    /// </summary>
    public Action? MikroUmschalten { get; set; }

    /// <summary>Abgelegt oder wieder sichtbar (Funkfeld-Takt, Meldezeile, Teilnehmermeldungen).</summary>
    public event Action<bool>? AbgelegtGeaendert;

    /// <summary>Programmstart: Symbol anmelden, das Funkfeld nach "Funkfeld beim Start zeigen" zeigen oder abgelegt lassen.</summary>
    public void Starten()
    {
        _gestartet = true;
        _tooltip = Tooltip();
        _tooltipZuletzt = _uhr();
        _symbol?.Anmelden(_tooltip);
        if (_ablage.Aktuell.Funkfeld.BeimStartZeigen) Zeigen();
        else Setzen(true);
    }

    /// <summary>Taste, Joystickknopf und Linksklick auf das Symbol: ablegen oder ohne Fokus holen.</summary>
    public void Umschalten()
    {
        if (_programmEnde) return;
        if (_fenster.Sichtbar) Ablegen();
        else Zeigen();
    }

    public void Ablegen()
    {
        if (_programmEnde) return;
        _fenster.Verbergen();
        Setzen(true);
        if (_ablage.Aktuell.Funkfeld.AblageHinweisGezeigt) return;
        _ablage.Aktuell.Funkfeld.AblageHinweisGezeigt = true;
        _ablage.Vormerken();
        _zeile.Zeigen(HinweisSchluessel, ErsterHinweis, Meldeart.Hinweis, Meldezeiten.ErsteAblageMs);
    }

    /// <summary>
    /// Der Hinweis beim ersten Ablegen; seit 0.11.0 eine Zeile (bis dahin "Funkfeld abgelegt - Taste oder Symbol im Infobereich holt es
    /// zurück (Windows legt neue Symbole unter ^)"). "(^)": Windows legt neue Symbole unter den Pfeil im Infobereich.
    /// </summary>
    public const string ErsterHinweis = "Funkfeld abgelegt: Taste oder Symbol (^) holt es";

    /// <summary>Zeigen: über Taste und Symbol ohne Fokus; <paramref name="mitFokus"/> nur aus dem Hauptfenster (Knopf, Strg+F).</summary>
    public void Zeigen(bool mitFokus = false)
    {
        if (_programmEnde) return;
        if (mitFokus) _fenster.ZeigenMitFokus();
        else _fenster.ZeigenOhneFokus();
        Setzen(false);
    }

    /// <summary>Ein Schließen des Funkfelds (X, Alt+F4) ist abzufangen: nicht am Programmende und nicht beim Neuaufbau.</summary>
    public bool SchliessenAbfangen => !_programmEnde && !_neuaufbau;

    /// <summary>
    /// Tooltip für das X: "Funkfeld ablegen. Zurück mit Num * oder dem Symbol im Infobereich." Ohne belegte Taste das Symbol und
    /// Strg+F im Hauptfenster.
    /// </summary>
    public static string AblegenTooltip(IReadOnlyList<string> tasten)
    {
        ArgumentNullException.ThrowIfNull(tasten);
        return tasten.Count == 0
            ? "Funkfeld ablegen. Zurück mit dem Symbol im Infobereich oder Strg+F im Hauptfenster."
            : "Funkfeld ablegen. Zurück mit " + string.Join(" oder ", tasten) + " oder dem Symbol im Infobereich.";
    }

    /// <summary>Das X der Titelzeile: wahr heißt abgefangen und abgelegt; am Programmende und beim Neuaufbau schließt es wirklich.</summary>
    public bool SchliessenAngefragt()
    {
        if (_programmEnde || _neuaufbau) return false;
        Ablegen();
        return true;
    }

    private bool _neuaufbau;

    /// <summary>Programmende (Hauptfenster schließt): das Funkfeld wirklich schließen und das Symbol entfernen.</summary>
    public void ProgrammEnde()
    {
        if (_programmEnde) return;
        _programmEnde = true;
        _fenster.Schliessen();
        _symbol?.Entfernen();
    }

    /// <summary>Neue Farbstellung: neu bauen, abgelegt bleibt abgelegt.</summary>
    public void NeuAufbauen()
    {
        if (_programmEnde) return;
        _neuaufbau = true;
        try
        {
            _fenster.NeuAufbauen(zeigen: !Abgelegt);
        }
        finally
        {
            _neuaufbau = false;
        }
    }

    /// <summary>Das Menü des Symbols, so wie es gerade gilt.</summary>
    public IReadOnlyList<Menuepunkt> Menue()
    {
        var liste = new List<Menuepunkt>
        {
            Abgelegt || !_fenster.Sichtbar ? new Menuepunkt("zeigen", "Funkfeld zeigen") : new Menuepunkt("ablegen", "Funkfeld ablegen"),
        };
        // Seit 0.10.0: Mikro stumm auch ohne Funkfeld und ohne belegte Taste, gleich an zweiter Stelle.
        if (MikroUmschalten is not null) liste.Add(MikroStumm?.Invoke() == true ? new Menuepunkt("mikro", "Mikro wieder an") : new Menuepunkt("mikro", "Mikro stumm"));
        liste.Add(new Menuepunkt("haupt", "Hauptfenster"));
        var l = _dienst.Lage;
        if (l is { IstAktiv: true, ServerS2: true, Konto.IstAdmin: true })
            liste.Add(l.Konto.AlsFunker ? new Menuepunkt("admin", "Wieder Admin") : new Menuepunkt("funker", "Heute als Funker"));
        liste.Add(new Menuepunkt("beenden", "Beenden"));
        return liste;
    }

    /// <summary>Im Bildtakt: Tooltip nur bei einem Wechsel und höchstens einmal je Sekunde.</summary>
    public void Takt()
    {
        if (!_gestartet || _programmEnde || _symbol is null) return;
        var jetzt = _uhr();
        if (jetzt - _tooltipZuletzt < TooltipAbstandMs) return;
        var neu = Tooltip();
        if (string.Equals(neu, _tooltip, StringComparison.Ordinal)) return;
        _tooltip = neu;
        _tooltipZuletzt = jetzt;
        _symbol.TooltipSetzen(neu);
    }

    /// <summary>"JG TacRelay - Funkfeld abgelegt - Senden: Crew 3" (höchstens 127 Zeichen, so viel nimmt Windows).</summary>
    public string Tooltip()
    {
        var l = _dienst.Lage;
        var stand = "JG TacRelay - Funkfeld " + (Abgelegt ? "abgelegt" : "sichtbar") + " - ";
        string rest;
        if (!l.IstAktiv) rest = "nicht verbunden";
        else
        {
            var netze = l.SendeFelder.Select(f => l.FeldVon(f) is { } feld ? l.NetzVon(feld.NetzId)?.Name : null).OfType<string>().ToList();
            if (l.GlobalSenden && l.Global is { } g) netze.Add(g.Name);
            rest = netze.Count == 0 ? "kein Sendekanal" : "Senden: " + string.Join(", ", netze);
        }
        var text = stand + rest;
        return text.Length <= 127 ? text : text[..124] + "...";
    }

    private void Gewaehlt(string schluessel)
    {
        switch (schluessel)
        {
            case "zeigen":
                Zeigen();
                break;
            case "ablegen":
                Ablegen();
                break;
            case "mikro":
                MikroUmschalten?.Invoke();
                break;
            case "haupt":
                HauptfensterZeigen?.Invoke();
                break;
            case "funker":
                // Einschalten nie ohne die Rückfrage mit den Folgen (Abschnitt 1): Sie steht im Hauptfenster unter "Ich".
                (FunkermodusZeigen ?? HauptfensterZeigen)?.Invoke();
                if (_funkermodus?.EinschaltenCommand.CanExecute(null) == true) _funkermodus.EinschaltenCommand.Execute(null);
                break;
            case "admin":
                if (_funkermodus is not null) _ = _funkermodus.UmschaltenAsync(false);
                break;
            case "beenden":
                Beenden?.Invoke();
                break;
        }
    }

    private void Setzen(bool abgelegt)
    {
        if (Abgelegt == abgelegt) return;
        Abgelegt = abgelegt;
        AbgelegtGeaendert?.Invoke(abgelegt);
    }
}