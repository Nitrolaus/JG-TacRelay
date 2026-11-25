// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Geraete;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>Ein Strom der Attrappe: Der Test ruft die Rückrufe selbst im Takt auf (kein Thread, kein Gerät).</summary>
internal sealed class AttrappenStrom(string geraetId, StromFormat format) : IAudioStrom
{
    public AufnahmeRueckruf? Aufnahme { get; init; }

    public WiedergabeRueckruf? Wiedergabe { get; init; }

    public StromFormat Format => format;

    public string GeraetId => geraetId;

    public bool Freigegeben { get; private set; }

    public event Action<Exception?>? Abgebrochen;

    public void Liefern(ReadOnlySpan<float> werte)
    {
        if (!Freigegeben) Aufnahme?.Invoke(werte);
    }

    public void Ziehen(Span<float> ziel)
    {
        if (Freigegeben || Wiedergabe is null) ziel.Clear();
        else Wiedergabe(ziel);
    }

    public void Abbrechen() => Abgebrochen?.Invoke(new InvalidOperationException("Attrappe: Gerät weg"));

    public void Dispose() => Freigegeben = true;
}

/// <summary>Geräte-Attrappe mit zwei Mikrofonen und zwei Ausgängen; Formate und Standardgeräte frei einstellbar.</summary>
internal sealed class AttrappenSystem : IAudioSystem
{
    public List<AudioGeraet> Ein { get; } = [new("mic-1", "Mikrofon A", true), new("mic-2", "Mikrofon B", false)];

    public List<AudioGeraet> Aus { get; } = [new("spk-1", "Lautsprecher A", true), new("spk-2", "Kopfhörer B", false)];

    public StromFormat AufnahmeFormat { get; set; } = new(48000, 1);

    public StromFormat WiedergabeFormat { get; set; } = new(48000, 2);

    public AttrappenStrom? Aufnahme { get; private set; }

    public AttrappenStrom? Loopback { get; private set; }

    public AttrappenStrom? Wiedergabe { get; private set; }

    public int Oeffnungen { get; private set; }

    /// <summary>Gesetzt, wirft das Öffnen des Mikrofons diesen Fehler (etwa den verweigerten Zugriff durch den Datenschutz).</summary>
    public Exception? AufnahmeFehler { get; set; }

    /// <summary>Gesetzt, wirft das Öffnen der Wiedergabe diesen Fehler.</summary>
    public Exception? WiedergabeFehler { get; set; }

    public event Action? GeraeteGeaendert;

    public IReadOnlyList<AudioGeraet> Eingaenge() => [.. Ein];

    public IReadOnlyList<AudioGeraet> Ausgaenge() => [.. Aus];

    public string? StandardEingang() => Ein.FirstOrDefault(g => g.IstStandard)?.Id;

    public string? StandardAusgang() => Aus.FirstOrDefault(g => g.IstStandard)?.Id;

    public IAudioStrom OeffneAufnahme(string geraetId, AufnahmeRueckruf rueckruf)
    {
        if (AufnahmeFehler is { } f) throw f;
        Oeffnungen++;
        Aufnahme = new AttrappenStrom(geraetId, AufnahmeFormat) { Aufnahme = rueckruf };
        return Aufnahme;
    }

    public IAudioStrom OeffneLoopback(string ausgangId, AufnahmeRueckruf rueckruf)
    {
        Oeffnungen++;
        Loopback = new AttrappenStrom(ausgangId, WiedergabeFormat) { Aufnahme = rueckruf };
        return Loopback;
    }

    public IAudioStrom OeffneWiedergabe(string geraetId, int latenzMs, WiedergabeRueckruf rueckruf)
    {
        if (WiedergabeFehler is { } f) throw f;
        Oeffnungen++;
        Wiedergabe = new AttrappenStrom(geraetId, WiedergabeFormat) { Wiedergabe = rueckruf };
        return Wiedergabe;
    }

    /// <summary>Standardgerät wechseln und das Ereignis auslösen wie Windows.</summary>
    public void StandardSetzen(string eingangId, string ausgangId)
    {
        for (var i = 0; i < Ein.Count; i++) Ein[i] = Ein[i] with { IstStandard = Ein[i].Id == eingangId };
        for (var i = 0; i < Aus.Count; i++) Aus[i] = Aus[i] with { IstStandard = Aus[i].Id == ausgangId };
        GeraeteGeaendert?.Invoke();
    }

    public void Entfernen(string id)
    {
        Ein.RemoveAll(g => g.Id == id);
        Aus.RemoveAll(g => g.Id == id);
        GeraeteGeaendert?.Invoke();
    }

    public void Dispose()
    {
    }
}

/// <summary>Ein gesendetes Paket, wie der Transport es sah, mit dem Takt (10 ms) der Zustellung.</summary>
internal sealed record Gesendet(long Takt, Sprachkennzeichen Kennzeichen, byte[] Opus);

/// <summary>
/// Prüfstand: Klangmaschine mit Geräte-Attrappe, simulierter Uhr und ohne eigene Threads. Jeder <see cref="Takt"/> ist
/// 10 ms: erst zieht die Wiedergabe (10 ms), dieselben Werte gehen in den Loopback, dann liefert das Mikrofon 10 ms,
/// zuletzt wird zugestellt. Alles deterministisch.
/// </summary>
internal sealed class Pruefstand : IAsyncDisposable
{
    public const long TaktUs = 10_000;

    /// <summary>
    /// Sprachaktivierung, die jeder in diesem Ablauf neu gebaute Prüfstand setzt (auch in Tasks, die daraus starten); <c>null</c>: die
    /// Voreinstellung der Maschine. So laufen die Szenen von <see cref="MessungSpielton"/> und <see cref="MessungAntwortUndKlick"/>
    /// unverändert mit einer anderen Einstellung (<see cref="MessungGrenzlinie"/>).
    /// </summary>
    internal static readonly AsyncLocal<GateEinstellung?> GateFuerMessung = new();

    private readonly float[] _wiedergabe;
    private readonly float[] _mikrofon;
    private readonly TelemetrieEintrag[] _telemetriePuffer = new TelemetrieEintrag[64];
    private long _telemetrieGelesen;

    /// <param name="aec3">Abweichende AEC3-Konfiguration (<see cref="KlangMaschinenOptionen.Aec3"/>); null: Voreinstellung der DLL.</param>
    /// <param name="klickStartwert">Startwert des Rauschens im Funk-Klick; fest, damit jeder Lauf dasselbe hört.</param>
    /// <param name="mitPegelstufe">Pegelstufe und Begrenzer vor RNNoise (Klangstufe A); falsch mit <paramref name="apm"/> = Sprechfunk ist die alte Kette.</param>
    /// <param name="apm">Abweichende APM-Einstellung (<see cref="KlangMaschinenOptionen.Apm"/>); null: Voreinstellung.</param>
    /// <param name="kodierer">Ersatz für den Opus-Kodierer (<see cref="KlangMaschinenOptionen.KodiererFabrik"/>).</param>
    /// <param name="deepFilter">Ersatz für das Anlegen von DeepFilterNet3 (<see cref="KlangMaschinenOptionen.DeepFilterFabrik"/>).</param>
    public Pruefstand(bool mitApm = true, bool mitRnnoise = true, StromFormat? aufnahme = null, StromFormat? wiedergabe = null,
        Klang.Verarbeitung.ISprachwahrscheinlichkeit? schaetzer = null, string? aec3 = null, int klickStartwert = 2026,
        bool mitPegelstufe = true, Klang.Kern.ApmEinstellungen? apm = null, Func<Klang.Kern.IKodierer>? kodierer = null,
        Func<Klang.Kern.DeepFilterProzessor>? deepFilter = null)
    {
        var vorgabe = new KlangMaschinenOptionen();
        System = new AttrappenSystem();
        if (aufnahme is { } a) System.AufnahmeFormat = a;
        if (wiedergabe is { } w) System.WiedergabeFormat = w;
        Maschine = new KlangMaschine(new KlangMaschinenOptionen
        {
            AudioSystem = System,
            UhrUs = Uhr.Lesen,
            EigeneThreads = false,
            MitApm = mitApm,
            MitRnnoise = mitRnnoise,
            Sprachwahrscheinlichkeit = schaetzer,
            Aec3 = aec3,
            KlickStartwert = klickStartwert,
            MitPegelstufe = mitPegelstufe,
            Apm = apm ?? vorgabe.Apm,
            KodiererFabrik = kodierer,
            DeepFilterFabrik = deepFilter,
        });
        if (GateFuerMessung.Value is { } gate) Maschine.Gate = gate;
        Maschine.SprachpaketBereit += (in SendeRahmen r) => Pakete.Add(new Gesendet(Takte, r.Kennzeichen, r.Opus.ToArray()));
        Maschine.Pegel += m => Meldungen.Add(m);
        Maschine.Fehler += (_, e) => Fehler.Add(e);
        _wiedergabe = new float[System.WiedergabeFormat.Abtastrate / 100 * System.WiedergabeFormat.Kanaele];
        _mikrofon = new float[System.AufnahmeFormat.Abtastrate / 100 * System.AufnahmeFormat.Kanaele];
    }

    public SimUhr Uhr { get; } = new() { Us = 1_000_000 };

    public AttrappenSystem System { get; }

    public KlangMaschine Maschine { get; }

    public List<Gesendet> Pakete { get; } = [];

    public List<PegelMeldung> Meldungen { get; } = [];

    public List<Exception> Fehler { get; } = [];

    /// <summary>Wiedergabe aller Takte (verschränkt, Geräteformat), wenn <see cref="Mitschreiben"/> an ist.</summary>
    public List<float> Ausgabe { get; } = [];

    public bool Mitschreiben { get; set; }

    /// <summary>Telemetrie jedes Aufnahmerahmens, wenn <see cref="TelemetrieMitschreiben"/> an ist (nach jedem Takt gelesen).</summary>
    public List<TelemetrieEintrag> Telemetrie { get; } = [];

    public bool TelemetrieMitschreiben { get; set; }

    public long Takte { get; private set; }

    /// <summary>Werte der Wiedergabe im letzten Takt (Geräteformat).</summary>
    public ReadOnlySpan<float> LetzteWiedergabe => _wiedergabe;

    /// <summary>Wird vor dem Mikrofon zum Signal addiert (etwa ein Echo der Wiedergabe); bekommt die Wiedergabe dieses Takts.</summary>
    public Action<float[], float[]>? Echo { get; set; }

    /// <summary>
    /// Ton eines anderen Programms (etwa Star Citizen) auf demselben Ausgabegerät: wird nach dem Ziehen zur Wiedergabe
    /// dieses Takts addiert (Takt, Werte im Geräteformat), steht damit im Loopback und im Echo, nicht in <see cref="Ausgabe"/>.
    /// </summary>
    public Action<long, float[]>? Fremdton { get; set; }

    public Task StartenAsync(string? ein = null, string? aus = null) => Maschine.StartenAsync(ein, aus, CancellationToken.None);

    /// <summary>Ein Takt mit 48-kHz-Mono-Mikrofonwerten (480); bei anderem Aufnahmeformat wird das Signal hier nicht umgerechnet.</summary>
    public void Takt(ReadOnlySpan<float> mikrofon)
    {
        Uhr.Us += TaktUs;
        Takte++;
        System.Wiedergabe?.Ziehen(_wiedergabe);
        if (Mitschreiben) Ausgabe.AddRange(_wiedergabe);
        Fremdton?.Invoke(Takte, _wiedergabe);
        System.Loopback?.Liefern(_wiedergabe);
        mikrofon.CopyTo(_mikrofon);
        Echo?.Invoke(_wiedergabe, _mikrofon);
        System.Aufnahme?.Liefern(_mikrofon);
        Maschine.Zustellen();
        if (TelemetrieMitschreiben)
        {
            var n = Maschine.TelemetrieLesen(_telemetrieGelesen, _telemetriePuffer, out var erste);
            if (erste != _telemetrieGelesen) throw new InvalidOperationException("Telemetrie übersprungen");
            for (var i = 0; i < n; i++) Telemetrie.Add(_telemetriePuffer[i]);
            _telemetrieGelesen = erste + n;
        }
    }

    /// <summary>Ein ganzes Signal (48 kHz mono) in Takten zu 10 ms durchschicken; <paramref name="vorher"/> je Takt.</summary>
    public void Abspielen(ReadOnlySpan<float> signal, Action<long>? vorher = null)
    {
        for (var pos = 0; pos + 480 <= signal.Length; pos += 480)
        {
            vorher?.Invoke(Takte);
            Takt(signal.Slice(pos, 480));
        }
    }

    public void Stille(int takte)
    {
        var null480 = new float[480];
        for (var i = 0; i < takte; i++) Takt(null480);
    }

    public async ValueTask DisposeAsync() => await Maschine.DisposeAsync();
}
