// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Klang.Maschine;

namespace Funk.Client.Modelle;

/// <summary>
/// Stimmproben (Sarevoc 08.10.2026, Q60; gebaut für den Prüfstand der Klangstufe D, seit 0.9.3 für Klangmessungen allgemein, denn die
/// Stimmerkennung ist eingestellt): fünf Aufnahmen je Sprecher (normal, leise, kurz, laut, gerufen), abgegriffen hinter Echo- und Rauschfilter,
/// vor Klangfarbe und Sprachaktivierung. Sie landen als WAV (48 kHz, 16 Bit, mono) im Ordner der Einstellungen unter
/// <c>stimmproben\&lt;Sprecher&gt;</c> und verlassen den PC nicht.
/// </summary>
public sealed partial class StimmprobenViewModel : ObservableObject
{
    // Prüfung 0.9.3: nicht "Rauschfilter prüfen", denn abgegriffen wird hinter dem Rauschfilter; die Aufnahmen sind schon gefiltert.
    public const string Erklaerung = "Für Klangmessungen brauchen wir echte Stimmen, etwa um die Klangfarbe an echten Stimmen zu prüfen. Jede Aufnahme geht nur in den Ordner deiner Einstellungen und verlässt den PC nicht. Stimmen anderer nur mit deren "
        + "Einverständnis aufnehmen.";

    public const string Lesetext = "Staffel Eins an alle, wir sammeln uns am Sprungpunkt. Jäger links, Bomber rechts, Abflug in zwei Minuten. "
        + "Brücke an Maschine, Schilde vorne verstärken. Verstanden, ich halte Position und melde mich, sobald wir Kontakt haben.";

    private readonly IKlangMaschine _maschine;
    private readonly Einstellungsablage _ablage;
    private readonly Func<DateTimeOffset> _jetzt;
    private StimmprobenSchritt? _laeuft;

    public StimmprobenViewModel(IKlangMaschine maschine, Einstellungsablage ablage, Func<DateTimeOffset>? jetzt = null)
    {
        ArgumentNullException.ThrowIfNull(maschine);
        ArgumentNullException.ThrowIfNull(ablage);
        _maschine = maschine;
        _ablage = ablage;
        _jetzt = jetzt ?? (() => DateTimeOffset.Now);
        Sprecher = ablage.Aktuell.Anmeldung.ScName ?? "";
        Schritte =
        [
            new(this, "normal", "Normal", "Lies den Text oben vor, so wie du am Funk sprichst.", 15),
            new(this, "leise", "Leise", "Lies ihn noch einmal leise, wie zu jemandem im Raum.", 10),
            new(this, "kurz", "Kurz", "Sag kurze Rufe mit Pausen dazwischen: Hit. Check. Kontakt. Ja. Nein. Klar.", 12),
            new(this, "laut", "Laut", "Sprich laut und aufgeregt, wie mitten im Gefecht.", 10),
            new(this, "gerufen", "Gerufen", "Ruf, als wäre es eilig: Achtung, Raketen! Ausweichen! Alle zurück!", 8),
        ];
    }

    /// <summary>Wessen Stimme: der Name kommt in den Ordner (ab Werk der SC-Name).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrdnerText))]
    public partial string Sprecher { get; set; }

    public ObservableCollection<StimmprobenSchritt> Schritte { get; }

    /// <summary>Wohin die Proben gehen, ohne den Pfad mit dem Windows-Benutzernamen zu zeigen.</summary>
    public string OrdnerText => "Im Ordner deiner Einstellungen unter stimmproben\\" + SprecherOrdner;

    private string SprecherOrdner
    {
        get
        {
            var ungueltig = Path.GetInvalidFileNameChars();
            var name = new string((Sprecher ?? "").Trim().Select(z => ungueltig.Contains(z) ? '_' : z).ToArray());
            return name.Length == 0 ? "unbenannt" : name;
        }
    }

    public string Ordner => Path.Combine(Path.GetDirectoryName(_ablage.Pfad) ?? AppContext.BaseDirectory, "stimmproben", SprecherOrdner);

    /// <summary>Läuft gerade eine Aufnahme (dann lässt sich keine zweite starten).</summary>
    public bool Laeuft => _laeuft is not null;

    internal void Starten(StimmprobenSchritt schritt)
    {
        if (_laeuft is not null) return;
        _laeuft = schritt;
        schritt.Zustand = "Aufnahme läuft ...";
        _maschine.StimmprobeStarten(schritt.Sekunden * 1000);
        foreach (var s in Schritte) s.KannStartenGeaendert();
    }

    /// <summary>Bildtakt: Fortschritt zeigen, eine fertige Probe abholen und als WAV schreiben.</summary>
    public void Takt()
    {
        if (_laeuft is not { } s) return;
        var stand = _maschine.Stimmprobe;
        if (stand.Laeuft) s.Zustand = string.Create(CultureInfo.CurrentCulture, $"Aufnahme läuft ... noch {Math.Max(0, (stand.DauerMs - stand.AufgenommenMs + 999) / 1000)} s");
        if (_maschine.StimmprobeHolen() is not { } daten) return;
        try
        {
            Directory.CreateDirectory(Ordner);
            var datei = Path.Combine(Ordner, _jetzt().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + s.Kennung + ".wav");
            WavSchreiben(datei, daten);
            s.Datei = datei;
            s.Zustand = "Gespeichert: " + Path.GetFileName(datei);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            s.Zustand = "Nicht gespeichert: " + ex.Message;
        }
        _laeuft = null;
        foreach (var x in Schritte) x.KannStartenGeaendert();
    }

    /// <summary>WAV 48 kHz, 16 Bit, mono; Werte über ±1 werden begrenzt.</summary>
    internal static void WavSchreiben(string datei, float[] daten)
    {
        using var f = new FileStream(datei, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(f);
        var bytes = daten.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(48000); w.Write(48000 * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        foreach (var x in daten) w.Write((short)Math.Round(Math.Clamp(x, -1f, 1f) * 32767f));
    }
}

/// <summary>Eine der fünf Aufnahmen: Titel, Anweisung, Dauer, Knopf und was daraus wurde.</summary>
public sealed partial class StimmprobenSchritt(StimmprobenViewModel eltern, string kennung, string titel, string anweisung, int sekunden) : ObservableObject
{
    public string Kennung { get; } = kennung;

    public string Titel { get; } = titel;

    public string Anweisung { get; } = anweisung;

    public int Sekunden { get; } = sekunden;

    public string KnopfText => string.Create(CultureInfo.CurrentCulture, $"Aufnehmen ({Sekunden} s)");

    [ObservableProperty]
    public partial string Zustand { get; set; } = "";

    [ObservableProperty]
    public partial string? Datei { get; set; }

    [RelayCommand(CanExecute = nameof(KannStarten))]
    private void Aufnehmen() => eltern.Starten(this);

    private bool KannStarten() => !eltern.Laeuft;

    internal void KannStartenGeaendert() => AufnehmenCommand.NotifyCanExecuteChanged();
}
