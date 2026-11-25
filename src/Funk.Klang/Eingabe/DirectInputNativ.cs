// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Funk.Klang.Eingabe;

/// <summary>
/// DirectInput 8 und die wenigen Windows-Aufrufe drumherum, nur für das Lesen von Spielgeräten. Werte und Aufbau aus
/// dinput.h (DIRECTINPUT_VERSION 0x0800, 64 Bit); die Größen der Strukturen prüft ein Test. Kein Force Feedback, keine
/// Tastatur oder Maus über DirectInput, kein Schreiben an Geräte. Eigene Registry-Zugriffe gibt es nicht; DirectInput
/// selbst pflegt beim Aufzählen und Öffnen Werte unter HKCU (siehe <see cref="DirectInputSprechtaste"/>).
/// </summary>
internal static unsafe partial class DirectInputNativ
{
    public const uint Version = 0x0800;

    /// <summary>DI8DEVCLASS_GAMECTRL: nur Spielgeräte.</summary>
    public const uint KlasseSpielgeraete = 4;

    /// <summary>DIEDFL_ATTACHEDONLY.</summary>
    public const uint NurAngeschlossene = 0x1;

    public const int WeiterAufzaehlen = 1;

    /// <summary>DISCL_NONEXCLUSIVE | DISCL_BACKGROUND: liest neben jedem Spiel mit und auch, wenn ein anderes Fenster vorn ist.</summary>
    public const uint NichtExklusivImHintergrund = 0x02 | 0x08;

    /// <summary>DI_BUFFEROVERFLOW (S_FALSE bei GetDeviceData).</summary>
    public const int PufferUebergelaufen = 1;

    /// <summary>DIERR_INPUTLOST.</summary>
    public const int EingabeVerloren = unchecked((int)0x8007001E);

    /// <summary>DIERR_NOTACQUIRED.</summary>
    public const int NichtErfasst = unchecked((int)0x8007000C);

    /// <summary>MAKEDIPROP(1), MAKEDIPROP(12), MAKEDIPROP(24): Eigenschaften werden als kleine Zahl statt GUID-Zeiger übergeben.</summary>
    public const nint EigenschaftPuffergroesse = 1;

    public const nint EigenschaftGuidUndPfad = 12;

    public const nint EigenschaftVidPid = 24;

    /// <summary>DIDC_POLLEDDEVICE | DIDC_POLLEDDATAFORMAT: Gerät meldet sich nicht selbst, es muss abgefragt werden.</summary>
    public const uint MussAbfragen = 0x00000002 | 0x00008000;

    /// <summary>DI8DEVTYPE_MOUSE, DI8DEVTYPE_KEYBOARD, DI8DEVTYPE_SCREENPOINTER: nie anlegen.</summary>
    public const byte TypMaus = 0x12;

    public const byte TypTastatur = 0x13;

    public const byte TypZeiger = 0x1A;

    public const int Pfadlaenge = 260;

    /// <summary>Datenformat der Sprechtaste: vier POV-Hüte ab Byte 0, 128 Knöpfe ab Byte 16, keine Achsen.</summary>
    public const int HutVersatz = 0;

    public const int KnopfVersatz = 16;

    public const int Datengroesse = 144;

    private const uint DidftOptional = 0x80000000;
    private const uint DidftJedeInstanz = 0x00FFFF00;
    private const uint DidftKnopf = 0x0000000C;
    private const uint DidftPov = 0x00000010;

    public const uint WmGeraeteaenderung = 0x0219;
    public const nint DbtAngekommen = 0x8000;
    public const nint DbtEntfernt = 0x8004;
    public const uint DbtSchnittstelle = 5;
    public const uint WsExWerkzeugfenster = 0x00000080;
    public const uint WsExNieAktivieren = 0x08000000;
    public const uint FehlerKlasseVorhanden = 1410;
    public const uint QsAlles = 0x04FF;
    public const uint MwmoEingabeDa = 0x0004;
    public const uint PmEntfernen = 0x0001;
    public const uint Unendlich = 0xFFFFFFFF;
    public const uint WartenFehlgeschlagen = 0xFFFFFFFF;
    public const uint WartenZeitAbgelaufen = 0x00000102;
    public const uint ZeitgeberHochaufloesend = 0x00000002;
    public const uint ZeitgeberVollzugriff = 0x001F0003;

    public static readonly Guid IidDirectInput8W = new("BF798031-483A-4DA2-AA99-5D64ED369700");

    /// <summary>GUID_DEVINTERFACE_HID: Meldungen für HID-Geräte, zu denen alle Spielgeräte gehören.</summary>
    public static readonly Guid HidKlasse = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    /// <summary>
    /// Das Datenformat liegt einmal für die ganze Laufzeit im nativen Speicher. Nur Knöpfe und Hüte: Achsen stehen nicht
    /// darin, damit ein bewegter Stick weder den Puffer füllt noch unnötig weckt. Die Zählung der Knöpfe ist dieselbe
    /// wie bei c_dfDIJoystick2 (DIDFT_ANYINSTANCE in Reihenfolge), Knopf 5 ist also derselbe wie im Spiel.
    /// </summary>
    public static readonly Datenformat* Sprechtastenformat = FormatAnlegen();

    private static Datenformat* FormatAnlegen()
    {
        const int objekte = Joystickeingabe.Huete + Joystickeingabe.Knoepfe;
        var liste = (Objektformat*)NativeMemory.AllocZeroed((nuint)(objekte * sizeof(Objektformat)));
        for (var i = 0; i < Joystickeingabe.Huete; i++)
            liste[i] = new Objektformat { Versatz = (uint)(HutVersatz + (4 * i)), Typ = DidftOptional | DidftPov | DidftJedeInstanz };
        for (var i = 0; i < Joystickeingabe.Knoepfe; i++)
            liste[Joystickeingabe.Huete + i] = new Objektformat { Versatz = (uint)(KnopfVersatz + i), Typ = DidftOptional | DidftKnopf | DidftJedeInstanz };
        var format = (Datenformat*)NativeMemory.AllocZeroed((nuint)sizeof(Datenformat));
        *format = new Datenformat
        {
            Groesse = (uint)sizeof(Datenformat),
            ObjektGroesse = (uint)sizeof(Objektformat),
            DatenGroesse = Datengroesse,
            Objekte = objekte,
            Liste = liste,
        };
        return format;
    }

    /// <summary>DIOBJECTDATAFORMAT; Art null heißt: jedes Objekt dieses Typs.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Objektformat
    {
        public Guid* Art;
        public uint Versatz;
        public uint Typ;
        public uint Flags;
    }

    /// <summary>DIDATAFORMAT.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Datenformat
    {
        public uint Groesse;
        public uint ObjektGroesse;
        public uint Flags;
        public uint DatenGroesse;
        public uint Objekte;
        public Objektformat* Liste;
    }

    /// <summary>Was GetDeviceState in das Datenformat oben schreibt.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Rohstand
    {
        public fixed uint Huete[4];
        public fixed byte Knoepfe[128];
    }

    /// <summary>DIDEVICEOBJECTDATA (mit uAppData ab Version 0x0800).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Objektdaten
    {
        public uint Versatz;
        public uint Wert;
        public uint Zeitstempel;
        public uint Folge;
        public nuint Anwendungsdaten;
    }

    /// <summary>DIDEVCAPS.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Faehigkeiten
    {
        public uint Groesse;
        public uint Flags;
        public uint Typ;
        public uint Achsen;
        public uint Knoepfe;
        public uint Povs;
        public uint FfAbtastung;
        public uint FfAufloesung;
        public uint Firmware;
        public uint Hardware;
        public uint FfTreiber;
    }

    /// <summary>DIPROPHEADER.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Eigenschaftskopf
    {
        public uint Groesse;
        public uint KopfGroesse;
        public uint Objekt;
        public uint Wie;
    }

    /// <summary>DIPROPDWORD.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DwordEigenschaft
    {
        public Eigenschaftskopf Kopf;
        public uint Wert;
    }

    /// <summary>DIPROPGUIDANDPATH.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PfadEigenschaft
    {
        public Eigenschaftskopf Kopf;
        public Guid Klasse;
        public fixed char Pfad[Pfadlaenge];
    }

    /// <summary>DIDEVICEINSTANCEW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Geraeteinstanz
    {
        public uint Groesse;
        public Guid Instanz;
        public Guid Produkt;
        public uint Typ;
        public fixed char Instanzname[Pfadlaenge];
        public fixed char Produktname[Pfadlaenge];
        public Guid FfTreiber;
        public ushort NutzungsSeite;
        public ushort Nutzung;
    }

    /// <summary>WNDCLASSEXW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Fensterklasse
    {
        public uint Groesse;
        public uint Stil;
        public nint Prozedur;
        public int KlassenExtra;
        public int FensterExtra;
        public nint Instanz;
        public nint Symbol;
        public nint Zeiger;
        public nint Hintergrund;
        public char* Menue;
        public char* Name;
        public nint SymbolKlein;
    }

    /// <summary>DEV_BROADCAST_DEVICEINTERFACE_W als Filter für RegisterDeviceNotification.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Schnittstellenfilter
    {
        public uint Groesse;
        public uint Art;
        public uint Reserviert;
        public Guid Klasse;
        public char Name;
    }

    public static DwordEigenschaft DwordKopf(uint wert = 0) => new()
    {
        Kopf = new Eigenschaftskopf { Groesse = (uint)sizeof(DwordEigenschaft), KopfGroesse = (uint)sizeof(Eigenschaftskopf) },
        Wert = wert,
    };

    [LibraryImport("dinput8.dll")]
    public static partial int DirectInput8Create(nint instanz, uint version, Guid* schnittstelle, void** ergebnis, void* aussen);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint ModulGriff(char* name);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort KlasseAnmelden(Fensterklasse* klasse);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint Standardprozedur(nint fenster, uint meldung, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool NachrichtNehmen(out RawInputNativ.Nachricht nachricht, nint fenster, uint von, uint bis, uint entfernen);

    [LibraryImport("user32.dll", EntryPoint = "MsgWaitForMultipleObjectsEx", SetLastError = true)]
    public static partial uint Warten(uint anzahl, nint* griffe, uint millisekunden, uint weckmaske, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "RegisterDeviceNotificationW", SetLastError = true)]
    public static partial nint MeldungAnmelden(nint empfaenger, void* filter, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterDeviceNotification")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MeldungAbmelden(nint anmeldung);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    public static partial nint ZeitgeberErzeugen(nint attribute, char* name, uint flags, uint zugriff);

    [LibraryImport("kernel32.dll", EntryPoint = "SetWaitableTimer", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ZeitgeberStellen(nint zeitgeber, long* faellig, int periodeMs, nint routine, nint argument, [MarshalAs(UnmanagedType.Bool)] bool wecken);

    [LibraryImport("kernel32.dll", EntryPoint = "CancelWaitableTimer")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ZeitgeberAnhalten(nint zeitgeber);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GriffSchliessen(nint griff);

    [LibraryImport("ole32.dll", EntryPoint = "CoInitializeEx")]
    public static partial int ComStarten(void* reserviert, uint art);

    [LibraryImport("ole32.dll", EntryPoint = "CoUninitialize")]
    public static partial void ComBeenden();
}

/// <summary>
/// IDirectInput8W, nur die ersten beiden Methoden (Gerät anlegen, Geräte aufzählen). Die übrigen stehen nicht da, weil
/// sie nie gerufen werden; für die Reihenfolge in der Vtable genügt der Anfang.
/// </summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("BF798031-483A-4DA2-AA99-5D64ED369700")]
internal unsafe partial interface IDirectInput8W
{
    [PreserveSig]
    int CreateDevice(Guid* instanz, void** geraet, void* aussen);

    [PreserveSig]
    int EnumDevices(uint klasse, nint rueckruf, void* kontext, uint flags);
}

/// <summary>
/// IDirectInputDevice8W bis Poll (Stelle 25 der Vtable). Die Methoden für Force Feedback, Escape und Einstellungsdialog
/// stehen nur als Platzhalter für die Reihenfolge da und werden nie gerufen.
/// </summary>
[GeneratedComInterface(Options = ComInterfaceOptions.ComObjectWrapper)]
[Guid("54D41081-DC15-4833-A41B-748F73A38179")]
internal unsafe partial interface IDirectInputDevice8W
{
    [PreserveSig]
    int GetCapabilities(DirectInputNativ.Faehigkeiten* faehigkeiten);

    [PreserveSig]
    int EnumObjects(nint rueckruf, void* kontext, uint flags);

    [PreserveSig]
    int GetProperty(nint eigenschaft, DirectInputNativ.Eigenschaftskopf* kopf);

    [PreserveSig]
    int SetProperty(nint eigenschaft, DirectInputNativ.Eigenschaftskopf* kopf);

    [PreserveSig]
    int Acquire();

    [PreserveSig]
    int Unacquire();

    [PreserveSig]
    int GetDeviceState(uint groesse, void* daten);

    [PreserveSig]
    int GetDeviceData(uint objektGroesse, DirectInputNativ.Objektdaten* daten, uint* anzahl, uint flags);

    [PreserveSig]
    int SetDataFormat(DirectInputNativ.Datenformat* format);

    [PreserveSig]
    int SetEventNotification(nint ereignis);

    [PreserveSig]
    int SetCooperativeLevel(nint fenster, uint stufe);

    [PreserveSig]
    int GetObjectInfo(void* info, uint objekt, uint wie);

    [PreserveSig]
    int GetDeviceInfo(void* info);

    [PreserveSig]
    int Platzhalter16RunControlPanel();

    [PreserveSig]
    int Platzhalter17Initialize();

    [PreserveSig]
    int Platzhalter18();

    [PreserveSig]
    int Platzhalter19();

    [PreserveSig]
    int Platzhalter20();

    [PreserveSig]
    int Platzhalter21();

    [PreserveSig]
    int Platzhalter22();

    [PreserveSig]
    int Platzhalter23();

    [PreserveSig]
    int Platzhalter24();

    [PreserveSig]
    int Poll();
}
