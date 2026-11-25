// JG TacRelay - Maintainer: Sarevoc
using CommunityToolkit.Mvvm.ComponentModel;
using Funk.Client.Ablage;
using Funk.Client.Klang;

namespace Funk.Client.Modelle;

/// <summary>Was die Marke rechts in der Statuszeile sagt (seit 0.10.0, E4). Wird nicht gespeichert.</summary>
public enum StatusMarke
{
    /// <summary>Verbunden, und gerade geht nichts hinaus: "Ruhe".</summary>
    Ruhe,

    /// <summary>Es geht gerade etwas hinaus: "SENDET" im Grün der Farbstellung.</summary>
    Sendet,

    /// <summary>Nicht verbunden (Q90): "Getrennt", gefärbt wie Ruhe; auch was du sagst, geht nirgends hin.</summary>
    Getrennt,
}

/// <summary>
/// Die Statuszeile "Mikrofon" unten im Hauptfenster (seit 0.10.0, Q87, Vorlage m-8): die eine Stelle für Pegel und SENDET, auf jeder Seite und
/// ohne Aufklappen. Sie ersetzt die Leiste der Sprachaktivierung. Links "Mikrofon", rechts der Hinweissatz aus <see cref="PegelVerlauf.Hinweis"/>
/// (etwa "Ruhe: es wird nichts gesendet" oder "Stimme unter der Mindest-Funklautstärke"), der Pegelbalken mit der Linie der Sprachaktivierung
/// und die Marke SENDET, Ruhe oder Getrennt. Liest dieselben Meldungen wie Grafik und Funkfeld, nur bei neuer Meldung; die Setter melden nur
/// Änderungen, damit WPF nicht 30-mal je Sekunde neu bindet.
/// </summary>
public sealed partial class StatuszeileViewModel : ObservableObject
{
    private readonly PegelVerlauf _verlauf;
    private readonly AudioViewModel _audio;
    private readonly Func<bool> _verbunden;
    private readonly Func<double>? _linieOhneMeldung;
    private long _gesehen = -1;

    /// <param name="verlauf">Die Pegelmeldungen der Maschine, mit dem Sendeweg des Funkfelds.</param>
    /// <param name="audio">Für Mikro stumm am Balken, auch ohne laufendes Audio richtig.</param>
    /// <param name="verbunden">Die Lage ist aktiv (<c>FunkLage.IstAktiv</c>); sonst steht die Marke auf Getrennt.</param>
    /// <param name="linieOhneMeldung">
    /// Die Linie, solange noch keine Pegelmeldung kam (im Betrieb <see cref="SchwellenViewModel.LinieDb"/>, der gemerkte Wert der Sprachaktivierung).
    /// Ohne Audio kommt keine Meldung, etwa mit "Audio beim Start öffnen" aus oder gesperrtem Mikrofon; die Linie stand dann am linken Rand
    /// (Befund der Prüfung). Ohne Angabe die Untergrenze.
    /// </param>
    public StatuszeileViewModel(PegelVerlauf verlauf, AudioViewModel audio, Func<bool> verbunden, Func<double>? linieOhneMeldung = null)
    {
        ArgumentNullException.ThrowIfNull(verlauf);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(verbunden);
        _verlauf = verlauf;
        _audio = audio;
        _verbunden = verbunden;
        _linieOhneMeldung = linieOhneMeldung;
        PegelDb = GateWerte.MinDb;
        LinieDb = LinieOhneMeldung();
        Marke = verbunden() ? StatusMarke.Ruhe : StatusMarke.Getrennt;
    }

    /// <summary>
    /// Hovertext der ganzen Statuszeile (Befund der Prüfung: Sie ist die einzige Stelle für Pegel und SENDET, Q87, und bis 0.9.3 hatte die Leiste
    /// einen). Sagt, was Farben, Linie und Marke heißen und wo man es genau einstellt; ohne Zahlen wie die übrigen Hovertexte.
    /// </summary>
    public const string Hilfe = "Mikrofon: Der Balken zeigt, wie laut dein Mikrofon gerade ist, die Marke rechts, ob etwas hinausgeht. Grün heißt, "
        + "deine Stimme geht in den Funk, die Marke sagt dann SENDET. Gelb heißt, es hätte ausgelöst, geht aber nirgends hin, etwa bei Ruhe, ohne "
        + "Verbindung oder in einem Kanal auf Taste ohne Sprechtaste; nur bei der fehlenden Sprechtaste steht der Grund daneben. Grau heißt, es geht nichts hinaus: zu leise unter "
        + "der roten Linie, oder leises Reden unter der Mindest-Funklautstärke, dann steht auch das daneben. Die Linie ist die Schwelle der "
        + "Sprachaktivierung: Erst was darüber liegt und nach Sprache klingt, öffnet den Funk. Getrennt "
        + "heißt, du bist nicht verbunden, Ruhe, dass gerade nichts hinausgeht. Genau einstellen kannst du die Sprachaktivierung mit Strg+T oder "
        + "unter Einstellungen > Senden.";

    private double LinieOhneMeldung() => _linieOhneMeldung is { } f && f() is var db && double.IsFinite(db) ? db : GateWerte.MinDb;

    /// <summary>Pegel der jüngsten Meldung in dBFS.</summary>
    [ObservableProperty]
    public partial double PegelDb { get; private set; }

    /// <summary>Die Linie der Sprachaktivierung (<c>SchwelleDb</c> der Meldung): ab hier beginnt eine Sendung.</summary>
    [ObservableProperty]
    public partial double LinieDb { get; private set; }

    /// <summary>Es geht gerade etwas hinaus: der Balken grün.</summary>
    [ObservableProperty]
    public partial bool Sendet { get; private set; }

    /// <summary>Q43: Es hätte ausgelöst, aber es geht nichts hinaus: der Balken gelb.</summary>
    [ObservableProperty]
    public partial bool WuerdeSenden { get; private set; }

    /// <summary>Mikro stumm: der Balken sagt "stumm".</summary>
    [ObservableProperty]
    public partial bool Stumm { get; private set; }

    /// <summary>Was die Marke sagt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkenText))]
    public partial StatusMarke Marke { get; private set; }

    /// <summary>Der Text der Marke: "SENDET", "Ruhe" oder "Getrennt".</summary>
    public string MarkenText => Marke switch
    {
        StatusMarke.Sendet => "SENDET",
        StatusMarke.Getrennt => "Getrennt",
        _ => "Ruhe",
    };

    /// <summary>Der Hinweissatz neben dem Balken, nur wenn du sprichst und nichts hinausgeht; sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? Hinweis { get; private set; }

    /// <summary>Bildtakt: Stumm und Verbindung immer, die Werte der Meldung nur, wenn es eine neue gibt.</summary>
    public void Takt()
    {
        Stumm = _audio.Stumm;
        var gesamt = _verlauf.Gesamt;
        if (gesamt != _gesehen && _verlauf.Letzte(out var m))
        {
            _gesehen = gesamt;
            PegelDb = m.PegelDbfs;
            LinieDb = m.SchwelleDb;
            Sendet = m.Sendet;
            WuerdeSenden = m.WuerdeSenden;
            Hinweis = _verlauf.Hinweis(m);
        }
        else if (_gesehen < 0)
        {
            // Noch keine Meldung: die Linie aus der Sprachaktivierung, damit sie nicht am linken Rand steht.
            LinieDb = LinieOhneMeldung();
        }
        // Getrennt geht vor: Ohne Verbindung geht nichts hinaus, auch wenn die Maschine gerade sendet (etwa mit gehaltener Melde-Taste).
        Marke = !_verbunden() ? StatusMarke.Getrennt : Sendet ? StatusMarke.Sendet : StatusMarke.Ruhe;
    }
}
