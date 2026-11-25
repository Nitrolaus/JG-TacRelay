// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Funk.Client.Modelle;

namespace Funk.Client.Fenster;

/// <summary>Ein Bild in einer ICO-Datei: Kantenlänge in Bildpunkten (256 für den Eintrag 0) und wo seine Daten stehen.</summary>
public readonly record struct Symbolbild(int Groesse, int Anfang, int Laenge);

/// <summary>
/// Liest das Verzeichnis einer ICO-Datei (ICONDIR) und wählt das Bild für eine Größe, als reine Rechnung ohne Windows. Damit lädt
/// <see cref="Ablagesymbol"/> das eigene Programmzeichen (tools\symbol-zeichnen.ps1) in der Größe, die der Infobereich verlangt.
/// </summary>
public static class Symboldatei
{
    /// <summary>Die Bilder; leer, wenn die Daten keine ICO-Datei sind. Einträge, die über das Ende zeigen, fallen weg.</summary>
    public static IReadOnlyList<Symbolbild> Bilder(ReadOnlySpan<byte> ico)
    {
        if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(ico[2..]) != 1) return [];
        var anzahl = BinaryPrimitives.ReadUInt16LittleEndian(ico[4..]);
        if (6 + (16 * anzahl) > ico.Length) return [];
        var bilder = new List<Symbolbild>(anzahl);
        for (var i = 0; i < anzahl; i++)
        {
            var eintrag = ico.Slice(6 + (16 * i), 16);
            var laenge = BinaryPrimitives.ReadUInt32LittleEndian(eintrag[8..]);
            var anfang = BinaryPrimitives.ReadUInt32LittleEndian(eintrag[12..]);
            if (laenge == 0 || anfang < 6 + (16 * (ulong)anzahl) || anfang + (ulong)laenge > (ulong)ico.Length) continue;
            bilder.Add(new Symbolbild(eintrag[0] == 0 ? 256 : eintrag[0], (int)anfang, (int)laenge));
        }
        return bilder;
    }

    /// <summary>Für <paramref name="soll"/> Bildpunkte das kleinste Bild, das mindestens so groß ist (Windows verkleinert sauber), sonst das größte.</summary>
    public static Symbolbild? Waehlen(IReadOnlyList<Symbolbild> bilder, int soll)
    {
        ArgumentNullException.ThrowIfNull(bilder);
        if (bilder.Count == 0) return null;
        var passend = bilder.Where(b => b.Groesse >= soll).OrderBy(b => b.Groesse).ToList();
        return passend.Count > 0 ? passend[0] : bilder.MaxBy(b => b.Groesse);
    }

    /// <summary>Das Programmzeichen, wie es in TacRelay.dll eingebettet ist (dieselbe Datei wie das Symbol der Programmdatei).</summary>
    public static byte[] Programmzeichen()
    {
        using var strom = typeof(Symboldatei).Assembly.GetManifestResourceStream("TacRelay.ico")
            ?? throw new InvalidOperationException("TacRelay.ico fehlt in TacRelay.dll.");
        using var puffer = new MemoryStream();
        strom.CopyTo(puffer);
        return puffer.ToArray();
    }
}

/// <summary>
/// Das Symbol im Infobereich der Taskleiste (RAID-S2-ENTWURF Abschnitt 5) über <c>Shell_NotifyIconW</c>, ohne WinForms und ohne neues
/// Paket: <c>UseWindowsForms</c> brächte Namenskonflikte mit WPF und die WinForms-Laufzeit in den Prozess der Klangmaschine, und
/// eine eigene Hülle lässt NOTIFYICON_VERSION_4, die Neuanmeldung über <c>TaskbarCreated</c> und die Größe des Zeichens je DPI in
/// der Hand.
/// <list type="bullet">
/// <item>Ein verstecktes Fenster der obersten Ebene (HwndSource, WS_POPUP, WS_EX_TOOLWINDOW, nie gezeigt) nimmt die Rückrufe;
/// ein reines Nachrichtenfenster (HWND_MESSAGE) bekäme die Rundsendung <c>TaskbarCreated</c> nicht.</item>
/// <item>uID 1, kein NIF_GUID: Eine GUID bände Windows an den Pfad der Programmdatei, und Probefunk, Paket und Setup laufen aus
/// verschiedenen Ordnern.</item>
/// <item>Linksklick (NIN_SELECT, NIN_KEYSELECT) meldet <see cref="Linksklick"/>; Rechtsklick (WM_CONTEXTMENU) baut das Menü aus
/// <see cref="Menue"/>. Nur dafür holt es sein unsichtbares Fenster kurz nach vorn, sonst schließt das Menü nicht (Windows-Verhalten);
/// das geschieht nur auf einen Rechtsklick, der dem Spiel den Fokus ohnehin schon genommen hat.</item>
/// <item>NIM_DELETE beim Entfernen und beim Freigeben.</item>
/// </list>
/// Nur auf dem Oberflächen-Thread. Kein Test legt es an; geprüft wird es von Hand (Bedienblatt), die Wahl des Bildes rechnerisch.
/// </summary>
public sealed partial class Ablagesymbol : IAblagesymbol
{
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NimSetversion = 4;
    private const uint NifMessage = 0x01;
    private const uint NifIcon = 0x02;
    private const uint NifTip = 0x04;
    private const uint NifShowtip = 0x80;
    private const uint NotifyiconVersion4 = 4;
    private const uint Kennung = 1;
    private const int Rueckruf = 0x8000 + 0x21;
    private const int WmContextmenu = 0x007B;
    private const int NinSelect = 0x0400;
    private const int NinKeyselect = 0x0401;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolwindow = 0x00000080;
    private const int SmCxsmicon = 49;
    private const uint MfString = 0x0000;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightbutton = 0x0002;
    private const uint TpmNonotify = 0x0080;
    private const uint TpmReturncmd = 0x0100;
    private const int TipMax = 127;

    private readonly HwndSource _fenster;
    private readonly uint _taskleisteNeu;
    private readonly byte[] _zeichen;
    private nint _symbol;
    private string _tooltip = "";
    private bool _angemeldet;
    private bool _frei;

    /// <param name="zeichen">Die ICO-Datei des Programmzeichens; ohne Angabe die eingebettete.</param>
    public Ablagesymbol(byte[]? zeichen = null)
    {
        _zeichen = zeichen ?? Symboldatei.Programmzeichen();
        _taskleisteNeu = RegisterWindowMessageW("TaskbarCreated");
        _fenster = new HwndSource(new HwndSourceParameters("JG TacRelay Infobereich")
        {
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolwindow,
            Width = 0,
            Height = 0,
        });
        _fenster.AddHook(Nachricht);
    }

    public event Action? Linksklick;

    public event Action<string>? Gewaehlt;

    public Func<IReadOnlyList<Menuepunkt>>? Menue { get; set; }

    public void Anmelden(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        if (_frei) return;
        _tooltip = tooltip;
        if (_symbol == 0) _symbol = SymbolLaden();
        var daten = Daten(NifMessage | NifIcon | NifTip | NifShowtip);
        // Nach einem Neustart des Explorers ist das alte fort; ein noch stehendes wird ersetzt statt verdoppelt.
        unsafe
        {
            _ = Shell_NotifyIconW(NimDelete, &daten);
            if (!Shell_NotifyIconW(NimAdd, &daten)) return;
            daten.ZeitOderVersion = NotifyiconVersion4;
            _ = Shell_NotifyIconW(NimSetversion, &daten);
        }
        _angemeldet = true;
    }

    public void TooltipSetzen(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        _tooltip = tooltip;
        if (!_angemeldet || _frei) return;
        var daten = Daten(NifTip | NifShowtip);
        unsafe
        {
            _ = Shell_NotifyIconW(NimModify, &daten);
        }
    }

    public void Entfernen()
    {
        if (!_angemeldet) return;
        _angemeldet = false;
        var daten = Daten(0);
        unsafe
        {
            _ = Shell_NotifyIconW(NimDelete, &daten);
        }
    }

    public void Dispose()
    {
        if (_frei) return;
        Entfernen();
        _frei = true;
        _fenster.RemoveHook(Nachricht);
        _fenster.Dispose();
        if (_symbol != 0) _ = DestroyIcon(_symbol);
        _symbol = 0;
    }

    private NotifyIconData Daten(uint flags)
    {
        var d = new NotifyIconData
        {
            Groesse = (uint)Marshal.SizeOf<NotifyIconData>(),
            Fenster = _fenster.Handle,
            Kennung = Kennung,
            Flags = flags,
            Rueckruf = Rueckruf,
            Symbol = _symbol,
        };
        var tip = _tooltip.Length <= TipMax ? _tooltip : _tooltip[..TipMax];
        unsafe
        {
            for (var i = 0; i < tip.Length; i++) d.Tip[i] = tip[i];
            d.Tip[tip.Length] = '\0';
        }
        return d;
    }

    /// <summary>Das Programmzeichen in der kleinen Symbolgröße des Systems bei der DPI des versteckten Fensters.</summary>
    private nint SymbolLaden()
    {
        var dpi = GetDpiForWindow(_fenster.Handle);
        var groesse = GetSystemMetricsForDpi(SmCxsmicon, dpi == 0 ? 96 : dpi);
        if (groesse <= 0) groesse = 16;
        if (Symboldatei.Waehlen(Symboldatei.Bilder(_zeichen), groesse) is not { } bild) return 0;
        unsafe
        {
            fixed (byte* p = &_zeichen[bild.Anfang])
                return CreateIconFromResourceEx(p, (uint)bild.Laenge, 1, 0x00030000, groesse, groesse, 0);
        }
    }

    private nint Nachricht(nint fenster, int nachricht, nint w, nint l, ref bool behandelt)
    {
        if (_taskleisteNeu != 0 && (uint)nachricht == _taskleisteNeu)
        {
            // Der Explorer ist neu gestartet: wieder anmelden, sonst bliebe das Symbol bis zum Programmende fort.
            if (_angemeldet) Anmelden(_tooltip);
            return 0;
        }
        if (nachricht != Rueckruf) return 0;
        behandelt = true;
        switch ((int)(l & 0xFFFF))
        {
            case NinSelect or NinKeyselect:
                Linksklick?.Invoke();
                break;
            case WmContextmenu:
                MenueZeigen((short)(w & 0xFFFF), (short)((w >> 16) & 0xFFFF));
                break;
        }
        return 0;
    }

    private void MenueZeigen(int x, int y)
    {
        if (Menue?.Invoke() is not { Count: > 0 } punkte) return;
        var menue = CreatePopupMenu();
        if (menue == 0) return;
        try
        {
            for (var i = 0; i < punkte.Count; i++)
            {
                // "Beenden" steht abgesetzt, damit es kein Fehlgriff aus "Hauptfenster" wird.
                if (i == punkte.Count - 1 && i > 0) _ = AppendMenuW(menue, MfSeparator, 0, null);
                _ = AppendMenuW(menue, MfString, (nuint)(i + 1), punkte[i].Text.Replace("&", "&&", StringComparison.Ordinal));
            }
            _ = SetForegroundWindow(_fenster.Handle);
            var wahl = TrackPopupMenuEx(menue, TpmReturncmd | TpmRightbutton | TpmNonotify, x, y, _fenster.Handle, 0);
            _ = PostMessageW(_fenster.Handle, 0, 0, 0);
            if (wahl > 0 && wahl <= punkte.Count) Gewaehlt?.Invoke(punkte[wahl - 1].Schluessel);
        }
        finally
        {
            _ = DestroyMenu(menue);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct NotifyIconData
    {
        public uint Groesse;
        public nint Fenster;
        public uint Kennung;
        public uint Flags;
        public uint Rueckruf;
        public nint Symbol;
        public fixed char Tip[128];
        public uint Zustand;
        public uint ZustandMaske;
        public fixed char Info[256];
        public uint ZeitOderVersion;
        public fixed char InfoTitel[64];
        public uint InfoFlags;
        public Guid Element;
        public nint BallonSymbol;
    }

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool Shell_NotifyIconW(uint nachricht, NotifyIconData* daten);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    private static unsafe partial nint CreateIconFromResourceEx(byte* daten, uint laenge, int symbol, uint fassung, int breite, int hoehe, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint symbol);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint fenster);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menue, uint flags, nuint kennung, string? text);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menue, uint flags, int x, int y, nint fenster, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menue);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint fenster);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint fenster, uint nachricht, nint w, nint l);
}
