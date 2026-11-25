// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using Funk.Klang.Maschine;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Funk.Klang.Geraete;

/// <summary>
/// WASAPI über NAudio 3 (<see cref="WasapiRecorder"/>, <see cref="WasapiPlayer"/>): geteilter Modus, ereignisgesteuert,
/// float32 im Mischformat des Geräts (keine Wandlung durch Windows; die Rate rechnet die Maschine selbst mit speexdsp
/// um), Audio-Threads mit MMCSS "Pro Audio". Wiedergabe mit Kategorie GameChat, damit Star Citizen nicht leiser wird;
/// dazu für beide Richtungen die Ducking-Abwahl der Sitzung (Windows dämpft sonst andere Programme, sobald ein Strom
/// auf dem Kommunikationsgerät läuft). Die Aufnahme versucht den RAW-Modus (ohne Klangverbesserungen von Windows) und
/// fällt ohne ihn zurück, wenn das Gerät ihn nicht kann.
/// <para>
/// Standardgerät ist das für Kommunikation. Geräteereignisse kommen ohne Synchronisationskontext aus dem
/// Audio-Dienst-Thread von Windows; <see cref="GeraeteGeaendert"/> darf dort nur kurz etwas vormerken.
/// </para>
/// </summary>
public sealed class WasapiSystem : IAudioSystem
{
    public const string Mmcss = "Pro Audio";

    private readonly MMDeviceEnumerator _aufzaehler = new();
    private readonly MMDeviceNotificationClient _benachrichtigung;

    public WasapiSystem()
    {
        _benachrichtigung = _aufzaehler.CreateNotificationClient(useSynchronizationContext: false);
        _benachrichtigung.DeviceAdded += Geaendert;
        _benachrichtigung.DeviceRemoved += Geaendert;
        _benachrichtigung.DeviceStateChanged += Geaendert;
        _benachrichtigung.DefaultDeviceChanged += Geaendert;
    }

    public event Action? GeraeteGeaendert;

    /// <summary>Die Aufnahme läuft ohne RAW-Modus, weil das Gerät ihn ablehnte (zur Anzeige).</summary>
    public bool AufnahmeOhneRaw { get; private set; }

    public IReadOnlyList<AudioGeraet> Eingaenge() => Auflisten(DataFlow.Capture);

    public IReadOnlyList<AudioGeraet> Ausgaenge() => Auflisten(DataFlow.Render);

    public string? StandardEingang() => Standard(DataFlow.Capture);

    public string? StandardAusgang() => Standard(DataFlow.Render);

    public IAudioStrom OeffneAufnahme(string geraetId, AufnahmeRueckruf rueckruf)
    {
        ArgumentNullException.ThrowIfNull(rueckruf);
        try
        {
            AufnahmeOhneRaw = false;
            return Aufnahme.Oeffnen(_aufzaehler, geraetId, loopback: false, raw: true, rueckruf);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            AufnahmeOhneRaw = true;
            return Aufnahme.Oeffnen(_aufzaehler, geraetId, loopback: false, raw: false, rueckruf);
        }
    }

    public IAudioStrom OeffneLoopback(string ausgangId, AufnahmeRueckruf rueckruf)
    {
        ArgumentNullException.ThrowIfNull(rueckruf);
        return Aufnahme.Oeffnen(_aufzaehler, ausgangId, loopback: true, raw: false, rueckruf);
    }

    public IAudioStrom OeffneWiedergabe(string geraetId, int latenzMs, WiedergabeRueckruf rueckruf)
    {
        ArgumentNullException.ThrowIfNull(rueckruf);
        return Wiedergabe.Oeffnen(_aufzaehler, geraetId, latenzMs, rueckruf);
    }

    public void Dispose()
    {
        _benachrichtigung.Dispose();
        _aufzaehler.Dispose();
    }

    private void Geaendert(object? sender, EventArgs e) => GeraeteGeaendert?.Invoke();

    private List<AudioGeraet> Auflisten(DataFlow richtung)
    {
        var standard = Standard(richtung);
        var liste = new List<AudioGeraet>();
        using var geraete = _aufzaehler.EnumerateAudioEndPoints(richtung, DeviceState.Active);
        foreach (var g in geraete)
        {
            using (g) liste.Add(new AudioGeraet(g.ID, g.FriendlyName, g.ID == standard));
        }
        return liste;
    }

    private string? Standard(DataFlow richtung)
    {
        if (!_aufzaehler.TryGetDefaultAudioEndpoint(richtung, Role.Communications, out var g) || g is null) return null;
        using (g) return g.ID;
    }

    /// <summary>
    /// Mischformat des Geräts, wenn es schon float32 ist (im geteilten Modus der Normalfall, dann ohne jede Wandlung);
    /// sonst float32 mit gleicher Rate und Kanalzahl, das Windows dann aus dem Mischformat wandelt.
    /// </summary>
    private static (WaveFormat Format, bool IstMischformat) FormatFloat(MMDevice geraet)
    {
        using var client = geraet.CreateAudioClient();
        var mix = client.MixFormat;
        return IstFloat32(mix) ? (mix, true) : (WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels), false);
    }

    private static bool IstFloat32(WaveFormat f) =>
        f.BitsPerSample == 32 && (f.Encoding == WaveFormatEncoding.IeeeFloat
            || (f is WaveFormatExtensible ext && ext.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT));

    /// <summary>
    /// Windows soll andere Programme nicht leiser stellen, solange dieser Strom läuft. Muss vor dem Start des Stroms
    /// laufen (IAudioSessionControl2::SetDuckingPreference: vor dem Aktivieren der Sitzung).
    /// </summary>
    private static void DuckingAbwaehlen(MMDevice geraet)
    {
        try
        {
            geraet.AudioSessionManager.AudioSessionControl.SetDuckingPreference(true);
        }
        catch (COMException)
        {
            // Nicht jedes Gerät hat eine Sitzungssteuerung; dann gibt es dort auch kein Ducking abzuwählen.
        }
    }

    private sealed class Aufnahme : IAudioStrom
    {
        private readonly MMDevice _geraet;
        private readonly WasapiRecorder _recorder;
        private readonly AufnahmeRueckruf _rueckruf;
        private readonly float[] _stille;
        private int _beendet;

        private Aufnahme(MMDevice geraet, WasapiRecorder recorder, StromFormat format, AufnahmeRueckruf rueckruf)
        {
            _geraet = geraet;
            _recorder = recorder;
            _rueckruf = rueckruf;
            Format = format;
            GeraetId = geraet.ID;
            _stille = new float[format.Abtastrate / 10 * format.Kanaele];
        }

        public StromFormat Format { get; }

        public string GeraetId { get; }

        public event Action<Exception?>? Abgebrochen;

        public static Aufnahme Oeffnen(MMDeviceEnumerator aufzaehler, string geraetId, bool loopback, bool raw, AufnahmeRueckruf rueckruf)
        {
            var geraet = aufzaehler.GetDevice(geraetId);
            WasapiRecorder? recorder = null;
            try
            {
                var (format, istMischformat) = FormatFloat(geraet);
                var bauer = new WasapiRecorderBuilder()
                    .WithDevice(geraet)
                    .WithSharedMode()
                    .WithEventSync()
                    .WithBufferLength(40)
                    .WithMmcssThreadPriority(Mmcss);
                if (!istMischformat) bauer = bauer.WithFormat(format);
                if (loopback) bauer = bauer.WithLoopbackCapture();
                if (raw) bauer = bauer.WithRawMode();
                recorder = bauer.Build();
                var ist = recorder.WaveFormat;
                if (!IstFloat32(ist) || ist.SampleRate != format.SampleRate || ist.Channels != format.Channels)
                    throw new NotSupportedException($"Aufnahmeformat {ist} statt float32 {format.SampleRate} Hz, {format.Channels} Kanäle.");
                var strom = new Aufnahme(geraet, recorder, new StromFormat(format.SampleRate, format.Channels), rueckruf);
                recorder.DataAvailable += strom.Daten;
                recorder.RecordingStopped += strom.Gestoppt;
                // Vor dem Start: Laut Windows SDK gilt die Abwahl nur, wenn sie gesetzt ist, bevor die Sitzung aktiv wird.
                // Danach hätte Windows andere Programme (Star Citizen) beim Öffnen des Kommunikationsstroms schon gedämpft.
                if (!loopback) DuckingAbwaehlen(geraet);
                recorder.StartRecording();
                return strom;
            }
            catch
            {
                recorder?.Dispose();
                geraet.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _beendet, 1) != 0) return;
            _recorder.RecordingStopped -= Gestoppt;
            _recorder.Dispose();
            _geraet.Dispose();
        }

        private void Daten(ReadOnlySpan<byte> puffer, AudioClientBufferFlags flags, long geraetePosition, long qpc)
        {
            var werte = MemoryMarshal.Cast<byte, float>(puffer);
            if ((flags & AudioClientBufferFlags.Silent) != 0)
            {
                // Laut WASAPI sind die Daten dann bedeutungslos und als Stille zu lesen.
                var rest = werte.Length;
                while (rest > 0)
                {
                    var n = Math.Min(rest, _stille.Length);
                    _rueckruf(_stille.AsSpan(0, n));
                    rest -= n;
                }
                return;
            }
            _rueckruf(werte);
        }

        private void Gestoppt(object? sender, StoppedEventArgs e)
        {
            if (Volatile.Read(ref _beendet) == 0) Abgebrochen?.Invoke(e.Exception);
        }
    }

    private sealed class Wiedergabe : IAudioStrom, IWaveProvider
    {
        private readonly MMDevice _geraet;
        private readonly WasapiPlayer _player;
        private readonly WiedergabeRueckruf _rueckruf;
        private int _beendet;

        private Wiedergabe(MMDevice geraet, WasapiPlayer player, WaveFormat format, WiedergabeRueckruf rueckruf)
        {
            _geraet = geraet;
            _player = player;
            _rueckruf = rueckruf;
            WaveFormat = format;
            Format = new StromFormat(format.SampleRate, format.Channels);
            GeraetId = geraet.ID;
        }

        public StromFormat Format { get; }

        public string GeraetId { get; }

        public WaveFormat WaveFormat { get; }

        public event Action<Exception?>? Abgebrochen;

        public static Wiedergabe Oeffnen(MMDeviceEnumerator aufzaehler, string geraetId, int latenzMs, WiedergabeRueckruf rueckruf)
        {
            var geraet = aufzaehler.GetDevice(geraetId);
            WasapiPlayer? player = null;
            try
            {
                var (format, _) = FormatFloat(geraet);
                player = new WasapiPlayerBuilder()
                    .WithDevice(geraet)
                    .WithSharedMode()
                    .WithEventSync()
                    .WithLatency(latenzMs)
                    .WithCategory(AudioStreamCategory.GameChat)
                    .WithMmcssThreadPriority(Mmcss)
                    .Build();
                var strom = new Wiedergabe(geraet, player, format, rueckruf);
                player.PlaybackStopped += strom.Gestoppt;
                player.Init(strom);
                DuckingAbwaehlen(geraet); // vor Play, siehe Aufnahme
                player.Play();
                return strom;
            }
            catch
            {
                player?.Dispose();
                geraet.Dispose();
                throw;
            }
        }

        public int Read(Span<byte> buffer)
        {
            _rueckruf(MemoryMarshal.Cast<byte, float>(buffer));
            return buffer.Length;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _beendet, 1) != 0) return;
            _player.PlaybackStopped -= Gestoppt;
            _player.Dispose();
            _geraet.Dispose();
        }

        private void Gestoppt(object? sender, StoppedEventArgs e)
        {
            if (Volatile.Read(ref _beendet) == 0) Abgebrochen?.Invoke(e.Exception);
        }
    }
}
