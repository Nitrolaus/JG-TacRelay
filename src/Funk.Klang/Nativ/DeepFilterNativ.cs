// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Funk.Klang.Nativ;

/// <summary>
/// Rohe Einsprungpunkte von deepfilternet.dll (eigene C-Schicht über libDF, DeepFilterNet 0.5.6 mit fest einkompiliertem
/// Modell DeepFilterNet3; Bau und Prüfung in native\BAUBERICHT.md, Schnittstelle in native\eigen\deepfilternet\deepfilternet.h).
/// Nur über <c>Funk.Klang.Kern.DeepFilterProzessor</c> benutzen.
/// </summary>
/// <remarks>
/// Keine Funktion der Schicht bricht den Prozess ab: Paniken in libDF oder tract kommen als Fehlercode zurück. Die Version ist
/// ein statischer Zeiger (als <c>nint</c> lesen, nie als string-Rückgabe, sonst gibt der Marshaller ihn frei).
/// </remarks>
internal static unsafe partial class DeepFilterNativ
{
    private const string Dll = "deepfilternet";

    internal const int Ok = 0;
    internal const int FehlerZeiger = -1;
    internal const int FehlerLaenge = -2;
    internal const int FehlerWert = -3;
    internal const int FehlerModell = -4;
    internal const int FehlerRechnen = -5;
    internal const int FehlerPanik = -6;
    internal const int FehlerGesperrt = -7;

    [LibraryImport(Dll, EntryPoint = "dfn_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial nint VersionLesen();

    /// <summary>Legt einen Filter an (einige hundert Millisekunden: Modell laden und optimieren). NULL bei Fehler.</summary>
    [LibraryImport(Dll, EntryPoint = "dfn_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial DeepFilterHandle Erstellen(float hoechsteAbsenkungDb, float nachfilterBeta, int* fehler);

    [LibraryImport(Dll, EntryPoint = "dfn_frame_length")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int RahmenGroesse(DeepFilterHandle zustand);

    [LibraryImport(Dll, EntryPoint = "dfn_delay_samples")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int Verzoegerung(DeepFilterHandle zustand);

    /// <summary>
    /// Filtert genau <paramref name="anzahl"/> Werte (float in [-1, 1]); <paramref name="ein"/> und <paramref name="aus"/> dürfen
    /// sich nicht überlappen. Rückgabe 0 oder ein Fehlercode; nach einem Rechenfehler ist der Zustand gesperrt.
    /// </summary>
    [LibraryImport(Dll, EntryPoint = "dfn_process")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int Verarbeiten(DeepFilterHandle zustand, float* ein, float* aus, int anzahl, float* lsnr);

    /// <summary>Schwellen der Rechenstufen nach dem lokalen SNR (siehe <c>Funk.Klang.Kern.DeepFilterSchwellen</c>).</summary>
    [LibraryImport(Dll, EntryPoint = "dfn_set_thresholds")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int SchwellenSetzen(DeepFilterHandle zustand, float minDb, float maxErbDb, float maxDfDb);

    [LibraryImport(Dll, EntryPoint = "dfn_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void Freigeben(nint zustand);
}

/// <summary>Besitzt einen <c>DfnZustand*</c>; gibt ihn mit <c>dfn_destroy</c> frei.</summary>
internal sealed class DeepFilterHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public DeepFilterHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        DeepFilterNativ.Freigeben(handle);
        return true;
    }
}
