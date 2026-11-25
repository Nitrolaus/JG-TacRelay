// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Funk.Klang.Nativ;

/// <summary>
/// Rohe Einsprungpunkte von opus.dll (libopus 1.6.1, eigener Bau, siehe native\BAUBERICHT.md).
/// Nur über die Hüllen in <c>Funk.Klang.Kern</c> benutzen: Sie prüfen jede Span-Länge, bevor ein
/// Zeiger die verwaltete Seite verlässt.
/// </summary>
/// <remarks>
/// <c>opus_encoder_ctl</c> und <c>opus_decoder_ctl</c> sind variadisch (<c>int request, ...</c>).
/// LibraryImport kennt keine variadischen Aufrufe. Unter win-x64 übergibt der Aufrufer variadische
/// Ganzzahl- und Zeigerargumente genau wie feste (RCX, RDX, R8, R9 mit Schattenbereich), deshalb
/// gibt es je Richtung eine feste Signatur. Für andere Plattformen (etwa macOS arm64) gilt das
/// nicht; dort braucht es eine kleine C-Schicht.
/// </remarks>
internal static unsafe partial class OpusNativ
{
    private const string Dll = "opus";

    // Fehlercodes (opus_defines.h)
    internal const int Ok = 0;
    internal const int BadArg = -1;
    internal const int BufferTooSmall = -2;
    internal const int InvalidPacket = -4;
    internal const int Unimplemented = -5;

    // Anwendungen, Signal, Bandbreite, Rahmendauer (opus_defines.h)
    internal const int ApplicationVoip = 2048;
    internal const int ApplicationAudio = 2049;
    internal const int SignalVoice = 3001;
    internal const int BandwidthFullband = 1105;
    internal const int Framesize20Ms = 5004;

    // CTL-Anfragen (opus_defines.h)
    internal const int SetApplication = 4000;
    internal const int GetApplication = 4001;
    internal const int SetBitrate = 4002;
    internal const int GetBitrate = 4003;
    internal const int SetMaxBandwidth = 4004;
    internal const int GetMaxBandwidth = 4005;
    internal const int SetVbr = 4006;
    internal const int GetVbr = 4007;
    internal const int SetComplexity = 4010;
    internal const int GetComplexity = 4011;
    internal const int SetInbandFec = 4012;
    internal const int GetInbandFec = 4013;
    internal const int SetPacketLossPerc = 4014;
    internal const int GetPacketLossPerc = 4015;
    internal const int SetDtx = 4016;
    internal const int GetDtx = 4017;
    internal const int SetVbrConstraint = 4020;
    internal const int GetVbrConstraint = 4021;
    internal const int SetSignal = 4024;
    internal const int GetSignal = 4025;
    internal const int GetLookahead = 4027;
    internal const int ResetState = 4028;
    internal const int GetSampleRate = 4029;
    internal const int GetLastPacketDuration = 4039;
    internal const int SetExpertFrameDuration = 4040;
    internal const int GetExpertFrameDuration = 4041;
    internal const int SetOsceBwe = 4054;
    internal const int GetOsceBwe = 4055;

    [LibraryImport(Dll, EntryPoint = "opus_get_version_string")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial nint VersionLesen();

    [LibraryImport(Dll, EntryPoint = "opus_strerror")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial nint FehlerText(int fehler);

    // ---------------------------------------------------------------- Kodierer

    [LibraryImport(Dll, EntryPoint = "opus_encoder_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial OpusKodiererHandle KodiererErstellen(int abtastrate, int kanaele, int anwendung, out int fehler);

    [LibraryImport(Dll, EntryPoint = "opus_encoder_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void KodiererFreigeben(nint kodierer);

    [LibraryImport(Dll, EntryPoint = "opus_encode_float")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int KodiereFloat(OpusKodiererHandle kodierer, float* pcm, int rahmenGroesse, byte* daten, int maxBytes);

    [LibraryImport(Dll, EntryPoint = "opus_encoder_ctl")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int KodiererSetzen(OpusKodiererHandle kodierer, int anfrage, int wert);

    [LibraryImport(Dll, EntryPoint = "opus_encoder_ctl")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int KodiererLesen(OpusKodiererHandle kodierer, int anfrage, int* wert);

    // --------------------------------------------------------------- Dekodierer

    [LibraryImport(Dll, EntryPoint = "opus_decoder_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial OpusDekodiererHandle DekodiererErstellen(int abtastrate, int kanaele, out int fehler);

    [LibraryImport(Dll, EntryPoint = "opus_decoder_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void DekodiererFreigeben(nint dekodierer);

    /// <summary><paramref name="daten"/> = null heißt: Paket fehlt, PLC über <paramref name="rahmenGroesse"/> Samples.</summary>
    [LibraryImport(Dll, EntryPoint = "opus_decode_float")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int DekodiereFloat(OpusDekodiererHandle dekodierer, byte* daten, int laenge, float* pcm, int rahmenGroesse, int fec);

    [LibraryImport(Dll, EntryPoint = "opus_decoder_ctl")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int DekodiererSetzen(OpusDekodiererHandle dekodierer, int anfrage, int wert);

    [LibraryImport(Dll, EntryPoint = "opus_decoder_ctl")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int DekodiererLesen(OpusDekodiererHandle dekodierer, int anfrage, int* wert);

    /// <summary>CTL ohne Argument, etwa OPUS_RESET_STATE.</summary>
    [LibraryImport(Dll, EntryPoint = "opus_decoder_ctl")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int DekodiererAnfrage(OpusDekodiererHandle dekodierer, int anfrage);

    // ---------------------------------------------------------------- Pakete, Soft-Clip

    [LibraryImport(Dll, EntryPoint = "opus_packet_get_nb_samples")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int PaketSamples(byte* paket, int laenge, int abtastrate);

    [LibraryImport(Dll, EntryPoint = "opus_packet_get_nb_frames")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int PaketRahmen(byte* paket, int laenge);

    /// <summary>Liest nur das erste Byte; der Aufrufer muss ein nicht leeres Paket übergeben.</summary>
    [LibraryImport(Dll, EntryPoint = "opus_packet_get_bandwidth")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int PaketBandbreite(byte* paket);

    /// <summary>Liest nur das erste Byte; der Aufrufer muss ein nicht leeres Paket übergeben.</summary>
    [LibraryImport(Dll, EntryPoint = "opus_packet_get_samples_per_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int PaketSamplesJeRahmen(byte* paket, int abtastrate);

    [LibraryImport(Dll, EntryPoint = "opus_packet_has_lbrr")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int PaketHatLbrr(byte* paket, int laenge);

    [LibraryImport(Dll, EntryPoint = "opus_pcm_soft_clip")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void SoftClip(float* pcm, int rahmenGroesse, int kanaele, float* speicher);

    /// <summary>Fehlertext von libopus; nur für Fehlermeldungen außerhalb des Audio-Threads.</summary>
    internal static string Text(int fehler) => Marshal.PtrToStringUTF8(FehlerText(fehler)) ?? fehler.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Besitzt einen <c>OpusEncoder*</c>; gibt ihn mit <c>opus_encoder_destroy</c> frei.</summary>
internal sealed class OpusKodiererHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public OpusKodiererHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        OpusNativ.KodiererFreigeben(handle);
        return true;
    }
}

/// <summary>Besitzt einen <c>OpusDecoder*</c>; gibt ihn mit <c>opus_decoder_destroy</c> frei.</summary>
internal sealed class OpusDekodiererHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public OpusDekodiererHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        OpusNativ.DekodiererFreigeben(handle);
        return true;
    }
}
