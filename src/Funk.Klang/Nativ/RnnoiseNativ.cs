// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Funk.Klang.Nativ;

/// <summary>
/// Rohe Einsprungpunkte von rnnoise.dll (Eigenbau, Standardmodell fest einkompiliert).
/// Nur über <c>Funk.Klang.Kern.RnnoiseProzessor</c> benutzen.
/// </summary>
/// <remarks>
/// Bewusst nicht deklariert: <c>rnnoise_model_from_filename</c> (fopen ohne Prüfung, ANSI-Codepage,
/// Absturz bei fehlender Datei, siehe BAUBERICHT) und <c>rnnoise_model_from_buffer</c> (das
/// eingebaute Modell reicht, <c>rnnoise_create(NULL)</c>).
/// </remarks>
internal static unsafe partial class RnnoiseNativ
{
    private const string Dll = "rnnoise";

    [LibraryImport(Dll, EntryPoint = "rnnoise_get_frame_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int RahmenGroesse();

    /// <summary><paramref name="modell"/> = 0 nimmt das eingebaute Standardmodell.</summary>
    [LibraryImport(Dll, EntryPoint = "rnnoise_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial RnnoiseHandle Erstellen(nint modell);

    /// <summary>
    /// Setzt einen Zustand zurück (memset und Modell neu verknüpfen, ohne Allokation, solange
    /// <paramref name="modell"/> = 0 das eingebaute Modell wählt). Rückgabe 0 oder -1.
    /// </summary>
    [LibraryImport(Dll, EntryPoint = "rnnoise_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial int Initialisieren(RnnoiseHandle zustand, nint modell);

    [LibraryImport(Dll, EntryPoint = "rnnoise_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial void Freigeben(nint zustand);

    /// <summary>
    /// Verarbeitet genau 480 Werte im int16-Wertebereich (±32768). Liest <paramref name="ein"/> und
    /// schreibt <paramref name="aus"/> ohne Längenangabe; beide müssen 480 Werte fassen.
    /// Rückgabe: Sprachwahrscheinlichkeit in [0, 1].
    /// </summary>
    [LibraryImport(Dll, EntryPoint = "rnnoise_process_frame")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static partial float Verarbeiten(RnnoiseHandle zustand, float* aus, float* ein);
}

/// <summary>Besitzt einen <c>DenoiseState*</c>; gibt ihn mit <c>rnnoise_destroy</c> frei.</summary>
internal sealed class RnnoiseHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public RnnoiseHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        RnnoiseNativ.Freigeben(handle);
        return true;
    }
}
