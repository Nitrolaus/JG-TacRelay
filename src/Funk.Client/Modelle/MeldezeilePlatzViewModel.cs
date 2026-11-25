// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;

namespace Funk.Client.Modelle;

/// <summary>
/// Wo die Meldezeile steht (seit 0.11.1, Sarevoc 12.10.2026: "an einem festen Platz am Bildschirm aufpoppen, nicht da, wo das Funkfeld ist"), unter
/// Einstellungen > Funkfeld und Darstellung > Erweitert, Gruppe Meldezeile: ein Raster aus neun Feldern (<see cref="Felder"/>, drei Zeilen zu drei
/// Spalten wie der Monitor), dazu die Wahl des Monitors (<see cref="Monitore"/>: Automatisch, Hauptmonitor, Monitor n). Ein Klick auf ein Feld
/// setzt die <see cref="Stelle"/> und zeigt die Meldezeile dort 3 s als Probe (<see cref="ProbeVerlangt"/>, <c>Meldezeiten.ProbeMs</c>), auch ein
/// Klick auf das schon gewählte Feld (<see cref="Stellenfeld.WaehlenCommand"/>); ebenso die Wahl des Monitors und der Knopf "Probe zeigen".
/// Gemerkt in <see cref="FunkfeldEinstellungen.MeldezeileStelle"/> und
/// <see cref="FunkfeldEinstellungen.MeldezeileMonitor"/>; das Fenster liest <see cref="Wahl"/>. Die Monitore kommen als Liste von außen
/// (im Betrieb <c>WindowsBildschirme</c>, nur gelesen), neu gelesen mit <see cref="MonitoreLesen"/>, wenn der Klappbereich aufgeht.
/// </summary>
public sealed partial class MeldezeilePlatzViewModel : ObservableObject
{
    /// <summary>Der Satz unter der Zwischenüberschrift.</summary>
    public const string Erklaerung = "Die Meldezeile steht an einem festen Platz des Monitors, nicht am Funkfeld; ab Werk oben in der Mitte auf dem "
        + "Monitor, auf dem das Spiel läuft. Ein Klick auf ein Feld setzt die Stelle und zeigt die Meldezeile dort kurz als Probe.";

    /// <summary>Hovertext am Raster (jedes Feld trägt dazu seine Stelle).</summary>
    public const string RasterHilfe = "Setzt die Meldezeile an diese Stelle des Monitors und zeigt sie dort 3 Sekunden als Probe. Die Meldezeile "
        + "nimmt nie den Fokus und lässt Klicks hindurch; über dem Spiel ist sie nur im randlosen Fenstermodus zu sehen.";

    /// <summary>Hovertext an der Wahl des Monitors.</summary>
    public const string MonitorHilfe = "Monitor der Meldezeile: Automatisch nimmt den Monitor, auf dem das Spiel gerade läuft, sonst den "
        + "Hauptmonitor. Hauptmonitor und Monitor 1, 2, ... legen ihn fest; ist ein gewählter Monitor nicht angeschlossen, gilt der Hauptmonitor. "
        + "Die Nummern sind die, in denen Windows die Monitore aufzählt. Jede Wahl zeigt die Meldezeile 3 Sekunden als Probe.";

    /// <summary>Hovertext am Knopf "Probe zeigen".</summary>
    public const string ProbeHilfe = "Probe zeigen: Zeigt die Meldezeile 3 Sekunden an ihrer Stelle, so wie sie im Spiel erscheint, mit dem Satz "
        + "\"" + Meldezeile.ProbeText + "\". Nichts wird gesendet, nichts verstellt.";

    /// <summary>Der Text der Wahl Automatisch (kurz, damit er ins Auswahlfeld passt; der Satz darunter nennt das Spiel).</summary>
    public const string AutomatischText = "Automatisch (Spiel, sonst Hauptmonitor)";

    /// <summary>Der Text der Wahl Hauptmonitor.</summary>
    public const string HauptmonitorText = "Hauptmonitor";

    private readonly Einstellungsablage _ablage;
    private readonly Func<IReadOnlyList<Bildschirm>> _bildschirme;

    /// <param name="bildschirme">Die Monitore, wie Windows sie gerade aufzählt (nur gelesen); in Tests eine feste Liste.</param>
    public MeldezeilePlatzViewModel(Einstellungsablage ablage, Func<IReadOnlyList<Bildschirm>> bildschirme)
    {
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(bildschirme);
        _ablage = ablage;
        _bildschirme = bildschirme;
        Felder = [.. Stellen.Select(s => new Stellenfeld(this, s))];
        MonitoreLesen();
    }

    /// <summary>Die neun Stellen in der Reihenfolge des Rasters (drei Zeilen zu drei Spalten, oben links zuerst).</summary>
    public static IReadOnlyList<Meldezeilenstelle> Stellen { get; } = Enum.GetValues<Meldezeilenstelle>();

    /// <summary>Die Stelle in Wörtern, etwa "oben Mitte" (Raster, Hilfetexte, "selbst gestellt").</summary>
    public static string Name(Meldezeilenstelle stelle) => stelle switch
    {
        Meldezeilenstelle.ObenLinks => "oben links",
        Meldezeilenstelle.ObenMitte => "oben Mitte",
        Meldezeilenstelle.ObenRechts => "oben rechts",
        Meldezeilenstelle.MitteLinks => "Mitte links",
        Meldezeilenstelle.Mitte => "Mitte",
        Meldezeilenstelle.MitteRechts => "Mitte rechts",
        Meldezeilenstelle.UntenLinks => "unten links",
        Meldezeilenstelle.UntenMitte => "unten Mitte",
        _ => "unten rechts",
    };

    /// <summary>Die neun Felder des Rasters, in der Reihenfolge von <see cref="Stellen"/>.</summary>
    public IReadOnlyList<Stellenfeld> Felder { get; }

    /// <summary>Die Monitore zur Wahl: Automatisch, Hauptmonitor, dann Monitor 1 bis n; ein gewählter, der gerade fehlt, steht als "nicht angeschlossen" dabei.</summary>
    public ObservableCollection<Monitorwahl> Monitore { get; } = [];

    /// <summary>Die Stelle im Raster; setzen merkt sie, meldet die Felder und zeigt die Probe.</summary>
    public Meldezeilenstelle Stelle
    {
        get => _ablage.Aktuell.Funkfeld.MeldezeileStelle;
        set
        {
            if (!Enum.IsDefined(value) || value == Stelle) return;
            _ablage.Aktuell.Funkfeld.MeldezeileStelle = value;
            _ablage.Vormerken();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Wahl));
            foreach (var f in Felder) f.Melden();
            Probe();
        }
    }

    /// <summary>Der Monitor: <c>null</c> Automatisch, 0 Hauptmonitor, n der n-te; setzen merkt ihn und zeigt die Probe.</summary>
    public int? Monitor
    {
        get => _ablage.Aktuell.Funkfeld.MeldezeileMonitor;
        set
        {
            var neu = value is >= 0 and <= FunkfeldEinstellungen.MeldezeileMonitorMax ? value : null;
            if (neu == Monitor) return;
            _ablage.Aktuell.Funkfeld.MeldezeileMonitor = neu;
            _ablage.Vormerken();
            MonitoreLesen();
            OnPropertyChanged();
            // Auch, wenn die Liste gleich bleibt: Das Auswahlfeld zieht nach, etwa nach "Zurück auf Werkseinstellung".
            OnPropertyChanged(nameof(GewaehlterMonitor));
            OnPropertyChanged(nameof(Wahl));
            Probe();
        }
    }

    /// <summary>Die gewählte Zeile der Monitorliste (für das Auswahlfeld); <c>null</c> setzt nichts (WPF leert die Wahl beim Neufüllen der Liste).</summary>
    public Monitorwahl? GewaehlterMonitor
    {
        get => Monitore.FirstOrDefault(m => m.Wert == Monitor);
        set
        {
            if (value is not null) Monitor = value.Wert;
        }
    }

    /// <summary>Stelle und Monitor zusammen, für das Fenster der Meldezeile.</summary>
    public Meldezeilenwahl Wahl => new(Stelle, Monitor);

    /// <summary>"Meldezeile oben Mitte" für "selbst gestellt".</summary>
    public static string StelleText(Meldezeilenstelle stelle) => "Meldezeile " + Name(stelle);

    /// <summary>"Meldezeile auf dem Hauptmonitor" oder "Meldezeile auf Monitor 2" für "selbst gestellt".</summary>
    public static string MonitorText(int? wahl) => wahl is null ? "Meldezeile automatisch" : wahl == 0 ? "Meldezeile auf dem Hauptmonitor"
        : "Meldezeile auf Monitor " + wahl.Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Liest die Monitore neu und baut die Liste: Automatisch, Hauptmonitor, "Monitor 1: 3840 x 2160 (Hauptmonitor)", "Monitor 2: 2560 x 1440", ...;
    /// ist der gewählte gerade nicht dabei, "Monitor 3 (nicht angeschlossen)". Die Wahl bleibt, was gemerkt ist.
    /// </summary>
    public void MonitoreLesen()
    {
        var liste = Monitorliste(_bildschirme(), Monitor);
        if (liste.Select(w => w.Name).SequenceEqual(Monitore.Select(w => w.Name), StringComparer.Ordinal)) return;
        Monitore.Clear();
        foreach (var w in liste) Monitore.Add(w);
        OnPropertyChanged(nameof(GewaehlterMonitor));
    }

    /// <summary>
    /// Die Zeilen eines Auswahlfelds der Monitore aus <paramref name="alle"/> und der gemerkten Wahl <paramref name="gewaehlt"/> (siehe
    /// <see cref="MonitoreLesen"/>); seit 0.11.9 auch für die Sprecheranzeige.
    /// </summary>
    public static List<Monitorwahl> Monitorliste(IReadOnlyList<Bildschirm> alle, int? gewaehlt)
    {
        ArgumentNullException.ThrowIfNull(alle);
        var liste = new List<Monitorwahl> { new(null, AutomatischText), new(0, HauptmonitorText) };
        for (var i = 0; i < alle.Count && i < FunkfeldEinstellungen.MeldezeileMonitorMax; i++)
        {
            var b = alle[i];
            var text = "Monitor " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + b.Bereich.Breite.ToString(CultureInfo.InvariantCulture) + " x "
                + b.Bereich.Hoehe.ToString(CultureInfo.InvariantCulture) + (b.Haupt ? " (" + HauptmonitorText + ")" : "");
            liste.Add(new Monitorwahl(i + 1, text));
        }
        if (gewaehlt is { } m && liste.All(w => w.Wert != m))
            liste.Add(new Monitorwahl(m, "Monitor " + m.ToString(CultureInfo.InvariantCulture) + " (nicht angeschlossen)"));
        return liste;
    }

    /// <summary>Die Meldezeile 3 s an ihrer Stelle zeigen; das Fenster hängt daran (App.xaml.cs).</summary>
    public event Action? ProbeVerlangt;

    /// <summary>Knopf "Probe zeigen", und nach jeder Wahl.</summary>
    [RelayCommand]
    private void Probe() => ProbeVerlangt?.Invoke();
}

/// <summary>
/// Ein Feld des Rasters: seine Stelle, der Name für Bildschirmleser, ob es gewählt ist und der Klick (<see cref="WaehlenCommand"/>). Der Klick
/// wählt die Stelle; trifft er das schon gewählte Feld, zeigt er die Probe noch einmal (Befund der Prüfung 0.11.1: Hilfetext und LIESMICH
/// versprachen die Probe je Klick, ein schon gewählter RadioButton änderte aber nichts). Das Feld in <c>Bereiche.xaml</c> liest
/// <see cref="Gewaehlt"/> nur (OneWay) und ruft den Klick als Command; so gibt es je Klick genau eine Probe.
/// </summary>
public sealed partial class Stellenfeld : ObservableObject
{
    private readonly MeldezeilePlatzViewModel _platz;

    public Stellenfeld(MeldezeilePlatzViewModel platz, Meldezeilenstelle stelle)
    {
        ArgumentNullException.ThrowIfNull(platz);
        _platz = platz;
        Stelle = stelle;
    }

    public Meldezeilenstelle Stelle { get; }

    /// <summary>Etwa "Meldezeile oben Mitte" (Name für Bildschirmleser).</summary>
    public string Name => MeldezeilePlatzViewModel.StelleText(Stelle);

    /// <summary>Hovertext: die Stelle und was der Klick tut.</summary>
    public string Hilfe => Name + ": " + MeldezeilePlatzViewModel.RasterHilfe;

    /// <summary>Gewählt; wahr setzen wählt die Stelle (und zeigt die Probe, wenn sie sich ändert), falsch setzen tut nichts (die Gruppe wählt das andere Feld).</summary>
    public bool Gewaehlt
    {
        get => _platz.Stelle == Stelle;
        set
        {
            if (value) _platz.Stelle = Stelle;
        }
    }

    /// <summary>Der Klick auf das Feld: wählt die Stelle (mit Probe); ist sie schon gewählt, zeigt er die Probe noch einmal.</summary>
    [RelayCommand]
    private void Waehlen()
    {
        if (_platz.Stelle == Stelle) _platz.ProbeCommand.Execute(null);
        else _platz.Stelle = Stelle;
    }

    internal void Melden() => OnPropertyChanged(nameof(Gewaehlt));
}

/// <summary>Eine Zeile der Monitorliste: der Wert für <see cref="FunkfeldEinstellungen.MeldezeileMonitor"/> und der Text (Name, wie die Vorlage der Auswahlfelder ihn liest).</summary>
public sealed record Monitorwahl(int? Wert, string Name)
{
    public override string ToString() => Name;
}
