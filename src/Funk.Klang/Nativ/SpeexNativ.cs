// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Funk.Klang.Nativ;

/// <summary>
/// Rohe Einsprungpunkte von speexdsp_resampler.dll (speexdsp 1.2.1, nur der Resampler,
/// Float-Variante mit SSE). Nur über <c>Funk.Klang.Kern.Resampler</c> benutzen.
/// </summary>
/// <remarks>
/// Die Längen werden als <c>spx_uint32_t*</c> übergeben: hinein die Größe des Puffers je Kanal,
/// heraus die verbrauchten bzw. erzeugten Werte je Kanal. Ein- und Ausgabe dürfen sich nicht
/// überlappen.
/// </remarks>
internal static unsafe partial class SpeexNativ
{
    private const string Dll = "speexdsp_resampler";

    internal const int Erfolg = 0;
    internal const int QualitaetMin = 0;
    internal const int QualitaetMax = 10;

    [LibraryImport(Dll, EntryPoint = "speex_resampler_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial SpeexResamplerHandle Erstellen(uint kanaele, uint eingangsRate, uint ausgangsRate, int qualitaet, out int fehler);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_init_frac")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial SpeexResamplerHandle ErstellenFraktional(uint kanaele, uint zaehler, uint nenner, uint eingangsRate, uint ausgangsRate, int qualitaet, out int fehler);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void Freigeben(nint zustand);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_process_interleaved_float")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int VerarbeitenVerschraenkt(SpeexResamplerHandle zustand, float* ein, uint* einLaenge, float* aus, uint* ausLaenge);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_set_rate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int RateSetzen(SpeexResamplerHandle zustand, uint eingangsRate, uint ausgangsRate);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_set_rate_frac")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int RateSetzenFraktional(SpeexResamplerHandle zustand, uint zaehler, uint nenner, uint eingangsRate, uint ausgangsRate);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_get_rate")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void RateLesen(SpeexResamplerHandle zustand, out uint eingangsRate, out uint ausgangsRate);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_get_ratio")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void VerhaeltnisLesen(SpeexResamplerHandle zustand, out uint zaehler, out uint nenner);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_get_quality")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void QualitaetLesen(SpeexResamplerHandle zustand, out int qualitaet);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_get_input_latency")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int EingangsLatenz(SpeexResamplerHandle zustand);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_get_output_latency")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int AusgangsLatenz(SpeexResamplerHandle zustand);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_skip_zeros")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int NullenUeberspringen(SpeexResamplerHandle zustand);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_reset_mem")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int SpeicherLeeren(SpeexResamplerHandle zustand);

    [LibraryImport(Dll, EntryPoint = "speex_resampler_strerror")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial nint FehlerText(int fehler);

    /// <summary>Fehlertext des Resamplers; nur für Fehlermeldungen außerhalb des Audio-Threads.</summary>
    internal static string Text(int fehler) => Marshal.PtrToStringUTF8(FehlerText(fehler)) ?? fehler.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Besitzt einen <c>SpeexResamplerState*</c>; gibt ihn mit <c>speex_resampler_destroy</c> frei.</summary>
internal sealed class SpeexResamplerHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SpeexResamplerHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        SpeexNativ.Freigeben(handle);
        return true;
    }
}
