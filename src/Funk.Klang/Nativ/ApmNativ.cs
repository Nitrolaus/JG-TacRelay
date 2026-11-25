// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Funk.Klang.Nativ;

/// <summary>
/// Rohe Einsprungpunkte von webrtc_apm.dll (eigene C-Schicht apm_c 1.1 über webrtc-audio-processing 2.1,
/// Beschreibung in native\eigen\webrtc-apm\apm_c.h). Nur über <c>Funk.Klang.Kern.EchoProzessor</c>
/// benutzen.
/// </summary>
/// <remarks>
/// Achtung: <c>apm_process_capture</c> und <c>apm_process_render</c> bekommen nur einen Zeiger und
/// keine Länge. Die Schicht liest bzw. schreibt blind <c>kanaele * abtastrate / 100</c> Werte
/// (BAUBERICHT, Prüfbefund [mittel]). Ein zu kurzer Puffer zerstört den Heap ohne Fehlercode. Die
/// Hülle prüft die Länge deshalb vor jedem Aufruf.
/// </remarks>
internal static unsafe partial class ApmNativ
{
    private const string Dll = "webrtc_apm";

    internal const int Ok = 0;
    internal const int StatistikUngueltig = 1;
    internal const int FehlerNullzeiger = -5;
    internal const int FehlerParameter = -6;
    internal const int FehlerNichtAn = -12;
    internal const int WarnungBegrenzt = -13;

    [LibraryImport(Dll, EntryPoint = "apm_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial ApmHandle Erstellen(int abtastrate, int kanaele);

    [LibraryImport(Dll, EntryPoint = "apm_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void Freigeben(nint apm);

    [LibraryImport(Dll, EntryPoint = "apm_configure")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int Konfigurieren(ApmHandle apm, int echo, int hochpass, int rauschStufe, int agc2Adaptiv, float agc2MaxGainDb);

    [LibraryImport(Dll, EntryPoint = "apm_set_stream_delay_ms")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int VerzoegerungSetzen(ApmHandle apm, int ms);

    /// <summary>Verarbeitet einen 10-ms-Mikrofonrahmen in-place. Keine Längenangabe, siehe Klassenhinweis.</summary>
    [LibraryImport(Dll, EntryPoint = "apm_process_capture")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int AufnahmeVerarbeiten(ApmHandle apm, float* werte);

    /// <summary>Übergibt einen 10-ms-Wiedergaberahmen (nur gelesen). Keine Längenangabe, siehe Klassenhinweis.</summary>
    [LibraryImport(Dll, EntryPoint = "apm_process_render")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int WiedergabeVerarbeiten(ApmHandle apm, float* werte);

    [LibraryImport(Dll, EntryPoint = "apm_get_stats")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int StatistikLesen(ApmHandle apm, float* erl, float* erle, int* verzoegerungMs);

    /// <summary>AEC3-Konfiguration als "name=wert;..." (Namen wie in EchoCanceller3Config); baut bei laufendem AEC neu auf.</summary>
    [LibraryImport(Dll, EntryPoint = "apm_aec3_configure", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int Aec3Konfigurieren(ApmHandle apm, string spec, out int fehlerPosition);

    /// <summary>
    /// Schreibt die AEC3-Konfiguration als Text; <paramref name="wirksam"/> 1 heißt: die des laufenden EchoCanceller3.
    /// Rückgabe: Länge ohne Nullzeichen, oder negativ (-12, wenn kein AEC3 läuft).
    /// </summary>
    [LibraryImport(Dll, EntryPoint = "apm_aec3_describe")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int Aec3Beschreiben(ApmHandle apm, int wirksam, byte* puffer, int laenge);

    /// <summary>Statische UTF-8-Zeichenkette; nie freigeben, deshalb <c>nint</c> statt <c>string</c>.</summary>
    [LibraryImport(Dll, EntryPoint = "apm_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial nint VersionLesen();
}

/// <summary>Besitzt ein <c>apm_handle*</c>; gibt es mit <c>apm_destroy</c> frei.</summary>
internal sealed class ApmHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public ApmHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        ApmNativ.Freigeben(handle);
        return true;
    }
}
