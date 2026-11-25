// JG TacRelay - Maintainer: Sarevoc
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Funk.Klang.Eingabe;

/// <summary>
/// Die Geräteschicht über DirectInput 8 im Lesethread, gebaut nach dem DirectInput-Kern des JG Signal Monitor
/// (core\src\Session.cpp, DirectInputReader.cpp): ein verstecktes Fenster der eigenen Klasse (nie gezeigt, nie
/// aktiviert) als Bezug für SetCooperativeLevel und als Empfänger der Gerätemeldungen für HID-Schnittstellen.
/// Spielgeräte werden nicht-exklusiv im Hintergrund geöffnet, mit Ereignis-Benachrichtigung und gepuffertem Lesen,
/// damit auch ein sehr kurzer Druck zwischen zwei Abholungen nicht verloren geht. Das Aufzählen macht
/// <see cref="DirectInputSuche"/> in einem eigenen Thread; das Öffnen hier prüft, dass die Instanz-GUID noch zum
/// aufgezählten Pfad gehört.
/// </summary>
internal sealed unsafe class DirectInputGeraete : ISpielgeraeteSchicht
{
    private const string Klassenname = "TacRelay.DirectInput";

    /// <summary>Puffer je Gerät in Einträgen; Knöpfe und Hüte allein füllen ihn kaum.</summary>
    private const uint Puffergroesse = 128;

    [ThreadStatic]
    private static bool _geaendert;

    private readonly nint _fenster;
    private readonly IDirectInput8W _directInput;
    private readonly nint _anmeldung;
    private bool _frei;

    public DirectInputGeraete()
    {
        var modul = DirectInputNativ.ModulGriff(null);
        _fenster = FensterAnlegen(modul);
        try
        {
            _directInput = DirectInputZugriff.Anlegen(modul);
            var filter = new DirectInputNativ.Schnittstellenfilter
            {
                Groesse = (uint)sizeof(DirectInputNativ.Schnittstellenfilter),
                Art = DirectInputNativ.DbtSchnittstelle,
                Klasse = DirectInputNativ.HidKlasse,
            };
            // Ohne Meldungen geht es auch: Ein abgestecktes Gerät fällt beim Lesen auf, ein neues beim nächsten Aufzählen.
            _anmeldung = DirectInputNativ.MeldungAnmelden(_fenster, &filter, 0);
        }
        catch
        {
            _ = RawInputNativ.FensterZerstoeren(_fenster);
            throw;
        }
    }

    public IOffenesSpielgeraet? Oeffnen(SpielgeraetInfo geraet, out string? fehler)
    {
        ArgumentNullException.ThrowIfNull(geraet);
        var zugang = DirectInputZugriff.Erzeugen(_directInput, geraet.Instanz, out var hr);
        if (zugang is null)
        {
            fehler = Text("CreateDevice", hr, geraet);
            return null;
        }
        AutoResetEvent? ereignis = null;
        string schritt;
        var faehigkeiten = new DirectInputNativ.Faehigkeiten { Groesse = (uint)sizeof(DirectInputNativ.Faehigkeiten) };
        if ((hr = zugang.SetDataFormat(DirectInputNativ.Sprechtastenformat)) < 0) schritt = "SetDataFormat";
        else if ((hr = zugang.SetCooperativeLevel(_fenster, DirectInputNativ.NichtExklusivImHintergrund)) < 0) schritt = "SetCooperativeLevel";
        else if ((hr = zugang.GetCapabilities(&faehigkeiten)) < 0) schritt = "GetCapabilities";
        else if (!string.Equals(DirectInputZugriff.PfadLesen(zugang), geraet.Kennung.Pfad, StringComparison.OrdinalIgnoreCase))
        {
            // Zwischen Aufzählen und Öffnen ist die Instanz-GUID zu einem anderen Gerät gewandert.
            schritt = "Pfadprüfung";
            hr = 0;
        }
        else
        {
            var abfragen = (faehigkeiten.Flags & DirectInputNativ.MussAbfragen) != 0;
            if (!abfragen)
            {
                ereignis = new AutoResetEvent(false);
                hr = zugang.SetEventNotification(ereignis.SafeWaitHandle.DangerousGetHandle());
            }
            var puffer = DirectInputNativ.DwordKopf(Puffergroesse);
            if (hr < 0) schritt = "SetEventNotification";
            else if ((hr = zugang.SetProperty(DirectInputNativ.EigenschaftPuffergroesse, &puffer.Kopf)) < 0) schritt = "SetProperty(BUFFERSIZE)";
            else if ((hr = zugang.Acquire()) < 0) schritt = "Acquire";
            else
            {
                fehler = null;
                return new DirectInputGeraet(zugang, ereignis, DirectInputZugriff.Begrenzen(faehigkeiten.Knoepfe, Joystickeingabe.Knoepfe),
                    DirectInputZugriff.Begrenzen(faehigkeiten.Povs, Joystickeingabe.Huete), abfragen);
            }
        }
        fehler = Text(schritt, hr, geraet);
        if (ereignis is not null)
        {
            _ = zugang.SetEventNotification(0);
            ereignis.Dispose();
        }
        DirectInputZugriff.Freigeben(zugang);
        return null;
    }

    public bool GeraeteGeaendert()
    {
        var geaendert = _geaendert;
        _geaendert = false;
        return geaendert;
    }

    public void Dispose()
    {
        if (_frei) return;
        _frei = true;
        if (_anmeldung != 0) _ = DirectInputNativ.MeldungAbmelden(_anmeldung);
        DirectInputZugriff.Freigeben(_directInput);
        _ = RawInputNativ.FensterZerstoeren(_fenster);
    }

    private static nint FensterAnlegen(nint modul)
    {
        fixed (char* name = Klassenname)
        {
            var klasse = new DirectInputNativ.Fensterklasse
            {
                Groesse = (uint)sizeof(DirectInputNativ.Fensterklasse),
                Prozedur = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&Fensterprozedur,
                Instanz = modul,
                Name = name,
            };
            if (DirectInputNativ.KlasseAnmelden(&klasse) == 0)
            {
                var fehler = Marshal.GetLastPInvokeError();
                if (fehler != DirectInputNativ.FehlerKlasseVorhanden) throw new Win32Exception(fehler);
            }
        }
        // Oberstes Fenster wie im JG Signal Monitor (DirectInput verlangt eines), aber ohne WS_VISIBLE: nie sichtbar,
        // nicht in der Taskleiste, nimmt nie den Fokus.
        var fenster = RawInputNativ.FensterErzeugen(DirectInputNativ.WsExWerkzeugfenster | DirectInputNativ.WsExNieAktivieren,
            Klassenname, "JG TacRelay DirectInput", 0, 0, 0, 0, 0, 0, 0, modul, 0);
        if (fenster == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return fenster;
    }

    [UnmanagedCallersOnly]
    private static nint Fensterprozedur(nint fenster, uint meldung, nint wParam, nint lParam)
    {
        if (meldung == DirectInputNativ.WmGeraeteaenderung && (wParam == DirectInputNativ.DbtAngekommen || wParam == DirectInputNativ.DbtEntfernt))
        {
            // Nur merken; der Lesethread prüft nach dem Verteilen der Nachrichten und lässt neu aufzählen.
            _geaendert = true;
            return 1;
        }
        return DirectInputNativ.Standardprozedur(fenster, meldung, wParam, lParam);
    }

    private static string Text(string schritt, int hr, SpielgeraetInfo geraet) =>
        string.Create(CultureInfo.InvariantCulture, $"DirectInput, {schritt}: 0x{hr:X8} ({geraet.Kennung.Name})");
}

/// <summary>Gemeinsame Handgriffe an DirectInput für Suche und Lesethread: anlegen, einwickeln, freigeben, Pfad lesen.</summary>
internal static unsafe class DirectInputZugriff
{
    private static readonly StrategyBasedComWrappers Huelle = new();

    public static IDirectInput8W Anlegen(nint modul)
    {
        var iid = DirectInputNativ.IidDirectInput8W;
        void* zeiger = null;
        var hr = DirectInputNativ.DirectInput8Create(modul, DirectInputNativ.Version, &iid, &zeiger, null);
        if (hr < 0 || zeiger == null)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"DirectInput 8 ließ sich nicht anlegen: 0x{hr:X8}"));
        return Einwickeln<IDirectInput8W>(zeiger);
    }

    public static T Einwickeln<T>(void* zeiger) where T : class
    {
        try
        {
            return (T)Huelle.GetOrCreateObjectForComInstance((nint)zeiger, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            _ = Marshal.Release((nint)zeiger);
        }
    }

    /// <summary>Gibt ein COM-Objekt sofort frei statt erst im Finalizer.</summary>
    public static void Freigeben(object? objekt)
    {
        if (objekt is ComObject com) com.FinalRelease();
    }

    public static IDirectInputDevice8W? Erzeugen(IDirectInput8W directInput, Guid instanz, out int hr)
    {
        void* zeiger = null;
        hr = directInput.CreateDevice(&instanz, &zeiger, null);
        return hr < 0 || zeiger == null ? null : Einwickeln<IDirectInputDevice8W>(zeiger);
    }

    public static string PfadLesen(IDirectInputDevice8W zugang)
    {
        var pfad = new DirectInputNativ.PfadEigenschaft
        {
            Kopf = new DirectInputNativ.Eigenschaftskopf
            {
                Groesse = (uint)sizeof(DirectInputNativ.PfadEigenschaft),
                KopfGroesse = (uint)sizeof(DirectInputNativ.Eigenschaftskopf),
            },
        };
        if (zugang.GetProperty(DirectInputNativ.EigenschaftGuidUndPfad, &pfad.Kopf) < 0) return "";
        return Bereinigen(new ReadOnlySpan<char>(pfad.Pfad, DirectInputNativ.Pfadlaenge));
    }

    /// <summary>Bis zur ersten Null, ohne Steuerzeichen (wie CleanText im JG Signal Monitor).</summary>
    public static string Bereinigen(ReadOnlySpan<char> text)
    {
        var ende = text.IndexOf('\0');
        if (ende >= 0) text = text[..ende];
        Span<char> kopie = stackalloc char[text.Length];
        for (var i = 0; i < text.Length; i++) kopie[i] = text[i] < ' ' ? ' ' : text[i];
        return new string(kopie).Trim();
    }

    public static int Begrenzen(uint wert, int hoechstens) => (int)Math.Min(wert, (uint)hoechstens);
}

/// <summary>
/// Ein über DirectInput geöffnetes Spielgerät (nicht-exklusiv, Hintergrund, Datenformat nur Knöpfe und Hüte). Geht der
/// Zugriff verloren, wird er einmal zurückgeholt; scheitert das, gilt das Gerät als weg.
/// </summary>
internal sealed unsafe class DirectInputGeraet : IOffenesSpielgeraet
{
    private const int Rohplaetze = 64;

    private readonly IDirectInputDevice8W _zugang;
    private readonly AutoResetEvent? _ereignis;
    private readonly nint _ereignisGriff;
    private readonly int _knoepfe;
    private readonly int _huete;
    private readonly bool _abfragen;
    private readonly DirectInputNativ.Objektdaten[] _roh = new DirectInputNativ.Objektdaten[Rohplaetze];
    private bool _frei;

    public DirectInputGeraet(IDirectInputDevice8W zugang, AutoResetEvent? ereignis, int knoepfe, int huete, bool abfragen)
    {
        _zugang = zugang;
        _ereignis = ereignis;
        _ereignisGriff = ereignis?.SafeWaitHandle.DangerousGetHandle() ?? 0;
        _knoepfe = knoepfe;
        _huete = huete;
        _abfragen = abfragen;
    }

    public nint Ereignis => _ereignisGriff;

    public int WechselLesen(Span<Eingabewechsel> ziel, out bool unvollstaendig)
    {
        unvollstaendig = false;
        if (_abfragen && !Abfragen()) return -1;
        var n = 0;
        var zurueckgeholt = false;
        for (var runde = 0; runde < 8; runde++)
        {
            uint anzahl = Rohplaetze;
            int hr;
            fixed (DirectInputNativ.Objektdaten* p = _roh)
            {
                hr = _zugang.GetDeviceData((uint)sizeof(DirectInputNativ.Objektdaten), p, &anzahl, 0);
            }
            if (hr is DirectInputNativ.EingabeVerloren or DirectInputNativ.NichtErfasst)
            {
                // Was in der Lücke geschah, ist unbekannt: Der Aufrufer liest danach den Stand.
                if (zurueckgeholt || _zugang.Acquire() < 0) return -1;
                zurueckgeholt = true;
                unvollstaendig = true;
                continue;
            }
            if (hr < 0) return -1;
            if (hr == DirectInputNativ.PufferUebergelaufen) unvollstaendig = true;
            for (var i = 0; i < (int)anzahl; i++)
            {
                if (!Umsetzen(in _roh[i], out var wechsel)) continue;
                if (n < ziel.Length) ziel[n++] = wechsel;
                else unvollstaendig = true;
            }
            if (anzahl < Rohplaetze) return n;
        }
        unvollstaendig = true;
        return n;
    }

    public bool StandLesen(out Eingabestand stand)
    {
        stand = default;
        if (_abfragen && !Abfragen()) return false;
        var roh = default(DirectInputNativ.Rohstand);
        for (var versuch = 0; versuch < 2; versuch++)
        {
            var hr = _zugang.GetDeviceState((uint)sizeof(DirectInputNativ.Rohstand), &roh);
            if (hr is DirectInputNativ.EingabeVerloren or DirectInputNativ.NichtErfasst)
            {
                if (_zugang.Acquire() < 0) return false;
                continue;
            }
            if (hr < 0) return false;
            var knoepfe = UInt128.Zero;
            for (var i = 0; i < _knoepfe; i++)
            {
                if ((roh.Knoepfe[i] & 0x80) != 0) knoepfe |= UInt128.One << i;
            }
            var huete = 0u;
            for (var i = 0; i < _huete; i++) huete |= (uint)(Joystickeingabe.HutRichtungAus(roh.Huete[i]) + 1) << (8 * i);
            stand = new Eingabestand(knoepfe, huete);
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_frei) return;
        _frei = true;
        _ = _zugang.Unacquire();
        if (_ereignis is not null) _ = _zugang.SetEventNotification(0);
        DirectInputZugriff.Freigeben(_zugang);
        _ereignis?.Dispose();
    }

    private bool Abfragen()
    {
        var hr = _zugang.Poll();
        if (hr is DirectInputNativ.EingabeVerloren or DirectInputNativ.NichtErfasst)
        {
            if (_zugang.Acquire() < 0) return false;
            hr = _zugang.Poll();
        }
        return hr >= 0;
    }

    /// <summary>Ein gepufferter Eintrag im Datenformat der Sprechtaste: Hüte ab Byte 0, Knöpfe ab Byte 16.</summary>
    private bool Umsetzen(in DirectInputNativ.Objektdaten daten, out Eingabewechsel wechsel)
    {
        wechsel = default;
        if (daten.Versatz >= DirectInputNativ.Datengroesse) return false;
        var versatz = (int)daten.Versatz;
        if (versatz < DirectInputNativ.KnopfVersatz)
        {
            var hut = (versatz - DirectInputNativ.HutVersatz) / 4;
            if (versatz % 4 != 0 || hut >= _huete) return false;
            wechsel = Eingabewechsel.Hut(hut, Joystickeingabe.HutRichtungAus(daten.Wert));
            return true;
        }
        var knopf = versatz - DirectInputNativ.KnopfVersatz;
        if (knopf >= _knoepfe) return false;
        wechsel = Eingabewechsel.Knopf(knopf, (daten.Wert & 0x80) != 0);
        return true;
    }
}
