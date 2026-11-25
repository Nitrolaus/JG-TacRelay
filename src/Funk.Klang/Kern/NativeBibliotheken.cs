// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using Funk.Klang.Nativ;

namespace Funk.Klang.Kern;

/// <summary>
/// Startprüfung der vier nativen Bibliotheken: lädt jede über einen harmlosen Aufruf und vergleicht
/// die Opus-Version. Für den Client-Start und die Diagnose, nicht für den Audio-Thread.
/// </summary>
public static class NativeBibliotheken
{
    public const string ErwarteteOpusVersion = "libopus 1.6.1";

    /// <summary>Versionszeichenkette von opus.dll.</summary>
    public static string OpusVersion => Marshal.PtrToStringUTF8(OpusNativ.VersionLesen()) ?? string.Empty;

    /// <summary>Versionszeichenkette von webrtc_apm.dll (eigene C-Schicht).</summary>
    public static string ApmVersion => EchoProzessor.Version;

    /// <summary>
    /// Lädt opus, rnnoise, speexdsp_resampler und webrtc_apm. Wirft <see cref="DllNotFoundException"/>
    /// oder <see cref="EntryPointNotFoundException"/>, wenn eine fehlt, und
    /// <see cref="InvalidOperationException"/>, wenn Version oder Rahmengröße nicht passen.
    /// </summary>
    public static void Pruefen()
    {
        string opus = OpusVersion;
        if (!opus.Equals(ErwarteteOpusVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"opus.dll meldet \"{opus}\", erwartet \"{ErwarteteOpusVersion}\".");

        int rahmen = RnnoiseNativ.RahmenGroesse();
        if (rahmen != RnnoiseProzessor.RahmenSamples)
            throw new InvalidOperationException($"rnnoise.dll meldet Rahmengröße {rahmen}, erwartet {RnnoiseProzessor.RahmenSamples}.");

        if (SpeexNativ.FehlerText(SpeexNativ.Erfolg) == 0)
            throw new InvalidOperationException("speexdsp_resampler.dll liefert keinen Fehlertext.");

        if (string.IsNullOrEmpty(ApmVersion))
            throw new InvalidOperationException("webrtc_apm.dll liefert keine Version.");
    }
}
