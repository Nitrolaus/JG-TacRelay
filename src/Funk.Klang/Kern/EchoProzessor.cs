// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Funk.Klang.Nativ;

namespace Funk.Klang.Kern;

/// <summary>Stufe der WebRTC-Rauschunterdrückung; die Werte entsprechen apm_configure.</summary>
public enum ApmRauschStufe
{
    Aus = -1,
    Niedrig = 0,
    Mittel = 1,
    Hoch = 2,
    SehrHoch = 3,
}

/// <summary>Schalter des APM (apm_configure).</summary>
/// <param name="Echounterdrueckung">AEC3 (Desktop, nicht AECM). Erzwingt den Hochpass, auch wenn <paramref name="Hochpass"/> aus ist.</param>
/// <param name="Hochpass">Hochpass im Aufnahmezweig.</param>
/// <param name="Rauschen">WebRTC-NS; aus, solange RNNoise läuft (audio-dsp, Empfehlung 3).</param>
/// <param name="Agc2Adaptiv">AGC2 mit adaptiver digitaler Verstärkung und Limiter.</param>
/// <param name="Agc2MaxGainDb">Größte Verstärkung der AGC2 in dB, (0, 90]; nur geprüft, wenn AGC2 an ist.</param>
public readonly record struct ApmEinstellungen(
    bool Echounterdrueckung, bool Hochpass, ApmRauschStufe Rauschen, bool Agc2Adaptiv, float Agc2MaxGainDb)
{
    /// <summary>
    /// Vorschlag nach audio-dsp, Empfehlung 3: AEC3 und Hochpass an, WebRTC-NS aus (RNNoise übernimmt),
    /// AGC2 adaptiv. Die 30 dB Maximalverstärkung stammen aus dem Rauchtest und sind per Hörtest festzulegen.
    /// </summary>
    public static ApmEinstellungen Sprechfunk => new(true, true, ApmRauschStufe.Aus, true, 30f);

    /// <summary>Nur AEC3 und Hochpass.</summary>
    public static ApmEinstellungen EchoUndHochpass => new(true, true, ApmRauschStufe.Aus, false, 0f);

    /// <summary>Alles aus (Zustand nach dem Anlegen).</summary>
    public static ApmEinstellungen Aus => new(false, false, ApmRauschStufe.Aus, false, 0f);
}

/// <summary>AEC-Kennwerte vom letzten Aufnahmerahmen.</summary>
/// <param name="Erl">Echo Return Loss in dB (NaN, wenn nicht vorhanden).</param>
/// <param name="Erle">Echo Return Loss Enhancement in dB (NaN, wenn nicht vorhanden).</param>
/// <param name="VerzoegerungMs">Vom AEC geschätzte Verzögerung in ms (-1, wenn nicht vorhanden).</param>
/// <param name="Gueltig">Erst nach etwa 2 s Anlauf von AEC3 und geschätzter ERL wahr (apm_get_stats).</param>
public readonly record struct EchoStatistik(float Erl, float Erle, int VerzoegerungMs, bool Gueltig);

/// <summary>
/// WebRTC Audio Processing Module (AEC3, Hochpass, NS, AGC2) über die eigene C-Schicht apm_c.
/// Rahmen je 10 ms: <c>Kanaele * Abtastrate / 100</c> Werte, float [-1, 1], mehrere Kanäle planar
/// (erst alle Werte von Kanal 0, dann Kanal 1).
/// </summary>
/// <remarks>
/// Die C-Schicht bekommt keine Länge und liest bzw. schreibt blind einen ganzen Rahmen. Beide
/// Verarbeitungsmethoden prüfen deshalb die genaue Länge, bevor der Zeiger übergeben wird.
/// <para>
/// Threads: <see cref="VerarbeiteAufnahme"/> (Aufnahme-Thread) und <see cref="VerarbeiteWiedergabe"/>
/// (Wiedergabe- bzw. Loopback-Thread) dürfen gleichzeitig laufen, jede Richtung aber nur aus einem
/// Thread. <see cref="SetzeVerzoegerung"/> und <see cref="Statistik"/> jederzeit. Beide
/// Verarbeitungsmethoden allokieren nicht und werfen nur bei falscher Länge; APM-Fehler kommen als
/// Rückgabecode. <see cref="Konfigurieren"/> sperrt im APM und gehört nicht in den Audio-Thread.
/// </para>
/// <para>
/// Numerischer Schutz (Gegenprüfung): Ein einziges NaN in Aufnahme oder Wiedergabe vergiftet AEC3
/// dauerhaft, danach kommt nur noch NaN heraus und die Statistik bleibt ungültig. Beide Methoden
/// ersetzen NaN und ±Unendlich deshalb vorher durch 0 und zählen sie in <see cref="ErsetzteWerte"/>:
/// die Aufnahme in-place, die Wiedergabe (nur gelesen) in einem vorab angelegten Ersatzpuffer.
/// Endliche Werte außerhalb [-1, 1] begrenzt das APM selbst.
/// </para>
/// <para>
/// AEC3-Konfiguration (C-Schicht 1.1): Jede Instanz startet mit der Voreinstellung <see cref="Aec3TacRelay"/> der
/// DLL (Upstream-Vorgabe von webrtc plus die eigene Abstimmung, siehe docs\MODULBERICHT-aec3.md).
/// <see cref="Aec3Einstellen"/> ändert einzelne Felder von <c>EchoCanceller3Config</c> per Name, <see cref="Aec3Lesen"/>
/// liest sie zurück, wahlweise so, wie der laufende EchoCanceller3 sie bekam. Ein Umbau (AEC ein oder aus, neue
/// AEC3-Konfiguration bei laufendem AEC) legt das APM in der C-Schicht neu an; Aufnahme und Wiedergabe warten dabei
/// kurz, alle Stufen beginnen von vorn.
/// </para>
/// </remarks>
public sealed unsafe class EchoProzessor : IDisposable
{
    public const int MinAbtastrate = 8000;
    public const int MaxAbtastrate = 384000;
    public const int MaxVerzoegerungMs = 500;

    /// <summary>Eintrag für <see cref="Aec3Einstellen"/>: zurück auf die Upstream-Vorgabe von webrtc (EchoCanceller3Config()).</summary>
    public const string Aec3Webrtc = "voreinstellung=webrtc";

    /// <summary>Eintrag für <see cref="Aec3Einstellen"/>: zurück auf die Voreinstellung der DLL (Upstream plus eigene Abstimmung).</summary>
    public const string Aec3TacRelay = "voreinstellung=tacrelay";

    private readonly ApmHandle _handle;
    private readonly float[] _wiedergabeErsatz;
    private long _aufnahmeRahmen;
    private long _wiedergabeRahmen;
    private long _ersetzt;

    public EchoProzessor(int abtastrate = 48000, int kanaele = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(abtastrate, MinAbtastrate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(abtastrate, MaxAbtastrate);
        if (abtastrate % 100 != 0)
            throw new ArgumentOutOfRangeException(nameof(abtastrate), abtastrate, "Muss durch 100 teilbar sein (10-ms-Rahmen).");
        ArgumentOutOfRangeException.ThrowIfLessThan(kanaele, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(kanaele, 2);

        _handle = ApmNativ.Erstellen(abtastrate, kanaele);
        if (_handle.IsInvalid)
        {
            _handle.Dispose();
            throw new InvalidOperationException("apm_create lieferte NULL.");
        }
        Abtastrate = abtastrate;
        Kanaele = kanaele;
        WerteJeKanal = abtastrate / 100;
        RahmenWerte = WerteJeKanal * kanaele;
        _wiedergabeErsatz = new float[RahmenWerte];
    }

    public int Abtastrate { get; }
    public int Kanaele { get; }
    /// <summary>Werte je Kanal und 10-ms-Rahmen.</summary>
    public int WerteJeKanal { get; }
    /// <summary>Genaue Länge jedes Rahmens über alle Kanäle.</summary>
    public int RahmenWerte { get; }

    /// <summary>Anzahl an das APM übergebener Aufnahmerahmen.</summary>
    public long AufnahmeRahmen => Volatile.Read(ref _aufnahmeRahmen);
    /// <summary>Anzahl an das APM übergebener Wiedergaberahmen.</summary>
    public long WiedergabeRahmen => Volatile.Read(ref _wiedergabeRahmen);
    /// <summary>NaN- und Unendlich-Werte aus Aufnahme und Wiedergabe, die vor dem APM durch 0 ersetzt wurden.</summary>
    public long ErsetzteWerte => Interlocked.Read(ref _ersetzt);

    /// <summary>Statische Versionszeichenkette der C-Schicht.</summary>
    public static string Version => Marshal.PtrToStringUTF8(ApmNativ.VersionLesen()) ?? string.Empty;

    /// <summary>Schaltet die Stufen. Prüft die Werte selbst, bevor sie das APM erreichen.</summary>
    public void Konfigurieren(ApmEinstellungen einstellungen)
    {
        if (!Enum.IsDefined(einstellungen.Rauschen))
            throw new ArgumentOutOfRangeException(nameof(einstellungen), einstellungen.Rauschen, "Unbekannte Rauschstufe.");
        if (einstellungen.Agc2Adaptiv && !(float.IsFinite(einstellungen.Agc2MaxGainDb) && einstellungen.Agc2MaxGainDb > 0f && einstellungen.Agc2MaxGainDb <= 90f))
            throw new ArgumentOutOfRangeException(nameof(einstellungen), einstellungen.Agc2MaxGainDb, "AGC2-Maximalverstärkung muss in (0, 90] dB liegen.");
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);

        int rc = ApmNativ.Konfigurieren(_handle,
            einstellungen.Echounterdrueckung ? 1 : 0,
            einstellungen.Hochpass ? 1 : 0,
            (int)einstellungen.Rauschen,
            einstellungen.Agc2Adaptiv ? 1 : 0,
            einstellungen.Agc2MaxGainDb);
        if (rc != ApmNativ.Ok) throw new InvalidOperationException($"apm_configure: Fehler {rc}");
    }

    /// <summary>
    /// Ändert die AEC3-Konfiguration: <c>"name=wert;name=wert"</c> mit den Feldpfaden aus <c>EchoCanceller3Config</c>
    /// (etwa <c>suppressor.normal_tuning.max_inc_factor=4</c>), ausgehend von der aktuellen Konfiguration;
    /// <see cref="Aec3Webrtc"/> oder <see cref="Aec3TacRelay"/> als Eintrag setzt vorher zurück. Alles oder nichts:
    /// Unbekannte Namen, unpassende Werte oder eine Konfiguration, die <c>EchoCanceller3Config::Validate</c> anpassen
    /// müsste, werfen <see cref="ArgumentException"/>, und es bleibt beim alten Stand. Läuft der AEC, wird er sofort
    /// mit der neuen Konfiguration neu aufgebaut. Nicht aus dem Audio-Thread.
    /// </summary>
    public void Aec3Einstellen(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        int rc = ApmNativ.Aec3Konfigurieren(_handle, spec, out var position);
        if (rc == ApmNativ.FehlerParameter)
        {
            var rest = position >= 0 && position < spec.Length ? spec[position..] : "(Gesamtprüfung)";
            throw new ArgumentException($"AEC3-Konfiguration abgelehnt ab Zeichen {position}: {rest}", nameof(spec));
        }
        if (rc != ApmNativ.Ok) throw new InvalidOperationException($"apm_aec3_configure: Fehler {rc}");
    }

    /// <summary>
    /// AEC3-Konfiguration als Text <c>"name=wert;..."</c> mit allen Feldern, die die C-Schicht kennt.
    /// <paramref name="wirksam"/>: die des laufenden EchoCanceller3 (null, wenn der AEC aus ist); sonst die eingestellte.
    /// </summary>
    public string? Aec3Text(bool wirksam = false)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        for (var versuch = 0; versuch < 3; versuch++)
        {
            int laenge = ApmNativ.Aec3Beschreiben(_handle, wirksam ? 1 : 0, null, 0);
            if (laenge == ApmNativ.FehlerNichtAn) return null;
            if (laenge < 0) throw new InvalidOperationException($"apm_aec3_describe: Fehler {laenge}");
            var puffer = new byte[laenge + 1];
            int rc;
            fixed (byte* p = puffer)
            {
                rc = ApmNativ.Aec3Beschreiben(_handle, wirksam ? 1 : 0, p, puffer.Length);
            }
            if (rc == ApmNativ.FehlerNichtAn) return null;
            if (rc < 0) throw new InvalidOperationException($"apm_aec3_describe: Fehler {rc}");
            // Hat ein Umbau dazwischen die Länge geändert, blieb der Puffer leer: noch einmal.
            if (rc == laenge) return Encoding.UTF8.GetString(puffer, 0, laenge);
        }
        throw new InvalidOperationException("apm_aec3_describe: Länge wechselt ständig.");
    }

    /// <summary>
    /// AEC3-Konfiguration als Wörterbuch Feldpfad zu Wert (Schalter 0/1). <paramref name="wirksam"/> wie bei
    /// <see cref="Aec3Text"/>; null, wenn kein AEC3 läuft.
    /// </summary>
    public IReadOnlyDictionary<string, double>? Aec3Lesen(bool wirksam = false) =>
        Aec3Text(wirksam) is { } text ? Aec3Zerlegen(text) : null;

    /// <summary>Zerlegt einen Text <c>"name=wert;..."</c> (wie von <see cref="Aec3Text"/>) in ein Wörterbuch.</summary>
    public static IReadOnlyDictionary<string, double> Aec3Zerlegen(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var werte = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var eintrag in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var gleich = eintrag.IndexOf('=', StringComparison.Ordinal);
            if (gleich <= 0) throw new FormatException($"Eintrag ohne Namen oder Wert: {eintrag}");
            werte[eintrag[..gleich].Trim()] = double.Parse(eintrag[(gleich + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        return werte;
    }

    /// <summary>
    /// Startwert für die Verzögerung Wiedergabe zu Aufnahme in ms (AEC3 schätzt selbst weiter).
    /// Werte außerhalb 0 bis 500 begrenzt das APM. Rückgabe: der wirksame Wert.
    /// </summary>
    public int SetzeVerzoegerung(int ms)
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        int rc = ApmNativ.VerzoegerungSetzen(_handle, ms);
        if (rc != ApmNativ.Ok && rc != ApmNativ.WarnungBegrenzt)
            throw new InvalidOperationException($"apm_set_stream_delay_ms: Fehler {rc}");
        return Math.Clamp(ms, 0, MaxVerzoegerungMs);
    }

    /// <summary>
    /// Verarbeitet einen Mikrofonrahmen in-place. Länge genau <see cref="RahmenWerte"/>, sonst wirft
    /// die Methode, bevor der Zeiger das Haus verlässt. NaN und ±Unendlich werden vorher in-place zu 0,
    /// subnormale Ausgabewerte danach ebenfalls. Rückgabe: 0 oder APM-Fehlercode (negativ).
    /// </summary>
    public int VerarbeiteAufnahme(Span<float> rahmen)
    {
        LaengenPruefung.Genau(rahmen.Length, RahmenWerte, nameof(rahmen));
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        int ersetzt = Endlichkeit.Ersetzen(rahmen);
        if (ersetzt != 0) Interlocked.Add(ref _ersetzt, ersetzt);
        int rc;
        fixed (float* p = rahmen)
        {
            rc = ApmNativ.AufnahmeVerarbeiten(_handle, p);
        }
        _aufnahmeRahmen++;
        Endlichkeit.SubnormaleNullen(rahmen);
        return rc;
    }

    /// <summary>
    /// Übergibt einen Wiedergaberahmen als Echoreferenz (wird nicht verändert). Länge genau
    /// <see cref="RahmenWerte"/>. Enthält er NaN oder ±Unendlich, geht eine bereinigte Kopie ans APM.
    /// Rückgabe: 0 oder APM-Fehlercode (negativ).
    /// </summary>
    public int VerarbeiteWiedergabe(ReadOnlySpan<float> rahmen)
    {
        LaengenPruefung.Genau(rahmen.Length, RahmenWerte, nameof(rahmen));
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        if (!Endlichkeit.AlleEndlich(rahmen))
        {
            rahmen.CopyTo(_wiedergabeErsatz);
            Interlocked.Add(ref _ersetzt, Endlichkeit.Ersetzen(_wiedergabeErsatz));
            rahmen = _wiedergabeErsatz;
        }
        int rc;
        fixed (float* p = rahmen)
        {
            rc = ApmNativ.WiedergabeVerarbeiten(_handle, p);
        }
        _wiedergabeRahmen++;
        return rc;
    }

    /// <summary>Liest die AEC-Kennwerte; sperrt nicht und berührt das APM nicht (Kopie in der C-Schicht).</summary>
    public EchoStatistik Statistik()
    {
        ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
        float erl, erle;
        int verzoegerung;
        int rc = ApmNativ.StatistikLesen(_handle, &erl, &erle, &verzoegerung);
        return new EchoStatistik(erl, erle, verzoegerung, rc == ApmNativ.Ok);
    }

    public void Dispose() => _handle.Dispose();
}
