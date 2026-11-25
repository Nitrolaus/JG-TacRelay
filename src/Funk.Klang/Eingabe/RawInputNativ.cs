// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;

namespace Funk.Klang.Eingabe;

/// <summary>
/// Die wenigen user32-Aufrufe für Raw Input: ein Nachrichtenfenster (Klasse "Message", nie sichtbar), Anmeldung mit
/// RIDEV_INPUTSINK, WM_INPUT lesen. Keine Hooks, kein RegisterHotKey, kein SendInput.
/// </summary>
internal static unsafe partial class RawInputNativ
{
    public const uint WmInput = 0x00FF;
    public const uint WmApp = 0x8000;
    public const uint RidInput = 0x10000003;
    public const uint RidevRemove = 0x00000001;
    public const uint RidevInputSink = 0x00000100;
    public const ushort SeiteGenerisch = 0x01;
    public const ushort NutzungMaus = 0x02;
    public const ushort NutzungTastatur = 0x06;

    /// <summary>HWND_MESSAGE: Elternfenster für reine Nachrichtenfenster.</summary>
    public static readonly nint NurNachrichten = -3;

    [StructLayout(LayoutKind.Sequential)]
    public struct Nachricht
    {
        public nint Fenster;
        public uint Meldung;
        public nint WParam;
        public nint LParam;
        public uint Zeit;
        public int X;
        public int Y;
        public uint Privat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawInputGeraet
    {
        public ushort Seite;
        public ushort Nutzung;
        public uint Flags;
        public nint Ziel;
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FensterErzeugen(uint exStil, string klasse, string? titel, uint stil, int x, int y, int breite, int hoehe, nint eltern, nint menue, nint instanz, nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FensterZerstoeren(nint fenster);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    public static partial int NachrichtHolen(out Nachricht nachricht, nint fenster, uint von, uint bis);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint NachrichtVerteilen(in Nachricht nachricht);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool NachrichtSenden(nint fenster, uint meldung, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterRawInputDevices", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GeraeteAnmelden(RawInputGeraet* geraete, uint anzahl, uint groesse);

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputData")]
    public static partial uint DatenLesen(nint rawInput, uint befehl, void* daten, ref uint groesse, uint kopfGroesse);

    [LibraryImport("user32.dll", EntryPoint = "GetKeyNameTextW")]
    public static partial int TastennameLesen(int lParam, char* puffer, int groesse);

    public const uint WmTimer = 0x0113;

    /// <summary>Zeitgeber am Nachrichtenfenster (WM_TIMER in dessen Warteschlange, ohne Rückruffunktion).</summary>
    [LibraryImport("user32.dll", EntryPoint = "SetTimer", SetLastError = true)]
    public static partial nuint ZeitgeberSetzen(nint fenster, nuint kennung, uint abstandMs, nint rueckruf);

    [LibraryImport("user32.dll", EntryPoint = "KillTimer", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ZeitgeberLoeschen(nint fenster, nuint kennung);

    /// <summary>Physischer Zustand einer Taste (GetAsyncKeyState, nur lesend); oberstes Bit gesetzt heißt unten.</summary>
    [LibraryImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    public static partial short TastenzustandLesen(int virtuelleTaste);

    /// <summary>Das Fenster im Vordergrund (GetForegroundWindow, nur lesend; beim Belegen).</summary>
    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    public static partial nint VordergrundFenster();

    /// <summary>Prozess eines Fensters (GetWindowThreadProcessId, nur lesend, ohne Handle auf den Prozess).</summary>
    [LibraryImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    public static partial uint FensterProzess(nint fenster, out uint prozessId);
}
