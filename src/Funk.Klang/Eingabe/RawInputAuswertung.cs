// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;

namespace Funk.Klang.Eingabe;

/// <summary>Eine Taste oder ein Mausknopf ging hinunter oder hinauf.</summary>
public readonly record struct Tastenereignis(Tastenbelegung Taste, bool Gedrueckt);

/// <summary>
/// Liest die Rohdaten eines WM_INPUT (Struktur RAWINPUT, 64 Bit) ohne Allokation: Tastatur mit Scancode und den Präfixen
/// E0/E1, Maus mit den Knöpfen 1 bis 5. Mausbewegung, Rad und HID-Geräte liefern nichts. Rein und prüfbar ohne Fenster.
/// </summary>
public static class RawInputAuswertung
{
    public const int TypMaus = 0;
    public const int TypTastatur = 1;

    /// <summary>RAWINPUTHEADER unter 64 Bit: dwType, dwSize, hDevice, wParam.</summary>
    public const int KopfBytes = 24;

    public const int TastaturBytes = KopfBytes + 16;
    public const int MausBytes = KopfBytes + 24;

    private const ushort RiKeyBreak = 0x01;
    private const ushort RiKeyE0 = 0x02;
    private const ushort RiKeyE1 = 0x04;
    private const ushort UeberlaufMakeCode = 0xFF;
    private const ushort VkUnecht = 0xFF;

    /// <summary>
    /// Wertet einen RAWINPUT-Puffer aus und schreibt die Tasten- und Knopfwechsel nach <paramref name="ziel"/> (Platz für
    /// fünf reicht immer). Rückgabe: Zahl der Ereignisse.
    /// </summary>
    public static int Auswerten(ReadOnlySpan<byte> rohdaten, Span<Tastenereignis> ziel)
    {
        if (rohdaten.Length < KopfBytes) return 0;
        var typ = BinaryPrimitives.ReadInt32LittleEndian(rohdaten);
        if (typ == TypTastatur && rohdaten.Length >= TastaturBytes) return Tastatur(rohdaten[KopfBytes..], ziel);
        if (typ == TypMaus && rohdaten.Length >= MausBytes) return Maus(rohdaten[KopfBytes..], ziel);
        return 0;
    }

    /// <summary>Scancode mit Präfix: 0x00nn, 0xE0nn oder 0xE1nn.</summary>
    public static int Scancode(ushort makeCode, bool e0, bool e1) => (e1 ? 0xE100 : e0 ? 0xE000 : 0) | (makeCode & 0xFF);

    /// <summary>
    /// Virtueller Tastencode, den Windows diesem Tastatur-RAWINPUT zugeordnet hat (für den Hängeschutz), oder 0 bei Maus,
    /// zu kurzen Daten und unechten Tasten. Es ist der Code dieses Ereignisses, also bei NumLock etwa VK_NUMPAD7 statt
    /// VK_HOME, und bei Strg, Umschalt und Alt der seitenlose Code (VK_CONTROL für links und rechts).
    /// </summary>
    public static ushort VirtuelleTaste(ReadOnlySpan<byte> rohdaten)
    {
        if (rohdaten.Length < TastaturBytes || BinaryPrimitives.ReadInt32LittleEndian(rohdaten) != TypTastatur) return 0;
        var vkey = BinaryPrimitives.ReadUInt16LittleEndian(rohdaten[(KopfBytes + 6)..]);
        return vkey == VkUnecht ? (ushort)0 : vkey;
    }

    /// <summary>Virtueller Code der Mausknöpfe 1 bis 5 (VK_LBUTTON, VK_RBUTTON, VK_MBUTTON, VK_XBUTTON1, VK_XBUTTON2), sonst 0.</summary>
    public static ushort VirtuelleTaste(int mausKnopf) => mausKnopf switch
    {
        1 => 0x01,
        2 => 0x02,
        3 => 0x04,
        4 => 0x05,
        5 => 0x06,
        _ => 0,
    };

    private static int Tastatur(ReadOnlySpan<byte> k, Span<Tastenereignis> ziel)
    {
        var makeCode = BinaryPrimitives.ReadUInt16LittleEndian(k);
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(k[2..]);
        var vkey = BinaryPrimitives.ReadUInt16LittleEndian(k[6..]);
        // Überlauf und die unechten Tasten aus Escape-Folgen (etwa das Schein-Shift vor Pfeiltasten bei NumLock)
        // sind keine Tasten des Nutzers.
        if (makeCode == UeberlaufMakeCode || vkey == VkUnecht || makeCode == 0 || ziel.IsEmpty) return 0;
        var code = Scancode(makeCode, (flags & RiKeyE0) != 0, (flags & RiKeyE1) != 0);
        ziel[0] = new Tastenereignis(Tastenbelegung.Taste(code), (flags & RiKeyBreak) == 0);
        return 1;
    }

    private static int Maus(ReadOnlySpan<byte> m, Span<Tastenereignis> ziel)
    {
        // RAWMOUSE: usFlags (2), Füllung (2), usButtonFlags (2), usButtonData (2), ...
        var knoepfe = BinaryPrimitives.ReadUInt16LittleEndian(m[4..]);
        var anzahl = 0;
        for (var knopf = 1; knopf <= 5 && anzahl < ziel.Length; knopf++)
        {
            var unten = 1 << (2 * (knopf - 1));
            var oben = unten << 1;
            if ((knoepfe & unten) != 0) ziel[anzahl++] = new Tastenereignis(Tastenbelegung.Maus(knopf), true);
            if ((knoepfe & oben) != 0 && anzahl < ziel.Length) ziel[anzahl++] = new Tastenereignis(Tastenbelegung.Maus(knopf), false);
        }
        return anzahl;
    }
}

/// <summary>
/// Zustand der globalen Tasten über Rohereignisse, für mehrere Zuordnungen (<see cref="Tastenzuordnung"/>): Die
/// Sprechtaste gilt als gedrückt, solange irgendeine ihrer Tasten unten ist, und meldet nur diesen Wechsel; eine
/// Netztaste meldet nur die Flanke nach unten. Tastenwiederholung beim Halten und fremde Tasten zählen nicht. Bedient
/// außerdem das Belegen (nächste gedrückte Taste). Nicht threadsicher; gehört dem Thread, der die Rohdaten liest.
/// </summary>
internal sealed class SprechtastenZustand
{
    /// <summary>So viele Prüfungen hintereinander muss eine gehaltene Taste physisch oben sein, bevor sie als losgelassen gilt.</summary>
    public const int HaengerPruefungen = 2;

    private Tastenzuordnung[] _zuordnungen = [];
    private bool[] _unten = [];
    private ushort[] _virtuell = [];
    private int[] _obenGezaehlt = [];

    /// <summary>Eine Taste der Funktion <see cref="Tastenfunktion.Sprechen"/> ist unten.</summary>
    public bool IstGedrueckt { get; private set; }

    /// <summary>
    /// Eine Taste der Funktion <see cref="Tastenfunktion.Melden"/> ist unten (gehalten wie die Sprechtaste, nicht als Flanke). Wer
    /// den Zustand führt, vergleicht vor und nach <see cref="Verarbeiten(in Tastenereignis, ushort, out int, out Tastenfunktion?, out Tastenbelegung?)"/>,
    /// <see cref="HaengerPruefen(Func{ushort, bool})"/> und <see cref="Zuordnen"/> und meldet den Wechsel.
    /// </summary>
    public bool MeldenGedrueckt { get; private set; }

    /// <summary>Irgendeine zugeordnete Taste gilt als unten; so lange läuft der Hängeschutz.</summary>
    public bool EineUnten => Array.IndexOf(_unten, true) >= 0;

    /// <summary>Wartet auf die nächste gedrückte Taste (Belegen). Setzen vergisst einen gemerkten Modifikator (<see cref="Vorsichtig"/>).</summary>
    public bool Lernt
    {
        get;
        set
        {
            field = value;
            _kandidat = null;
            _modifikatorenUnten = 0;
            _kombiniert = false;
        }
    }

    /// <summary>
    /// Vorsichtig belegen (seit 0.10.0, Befund der Prüfung): Beim ersten Start wartet die Startseite von selbst und ohne Frist auf die Sprechtaste
    /// (E8), also auch dann, wenn man das Fenster nur bedient. Dann zählen Tab, Eingabe, Leertaste, die Pfeile, Pos1, Ende, Bild auf und Bild ab im
    /// eigenen Fenster nicht (sie bedienen die Oberfläche, <see cref="BedientFenster"/>); Strg, Alt, Umschalt und Windows (<see cref="IstModifikator"/>)
    /// zählen erst beim Loslassen und nur allein, und eine Taste, die zusammen mit einem von ihnen gedrückt wird (Strg+2, Alt+Tab), zählt nie.
    /// Sonst würde Alt beim Wechsel mit Alt+Tab zur Sprechtaste, Strg bei Strg+2, Tab beim Gang durch die Felder. Bis 0.9.3 begann das Belegen
    /// nur nach einem Klick auf "Belegen", mit Frist; dort und bei "Ändern" bleibt es bei der ersten gedrückten Taste.
    /// </summary>
    public bool Vorsichtig { get; set; }

    /// <summary>
    /// Für <see cref="Vorsichtig"/>: Ein Modifikator ist physisch unten, auch einer, der schon vor dem Belegen gedrückt wurde (etwa Strg aus
    /// Strg+1). Ohne Angabe zählen nur die Ereignisse während des Belegens.
    /// </summary>
    public Func<bool>? ModifikatorUnten { get; set; }

    /// <summary>Vorsichtig: der zuletzt allein gedrückte Modifikator; er wird beim Loslassen zur Taste, wenn nichts dazwischenkam.</summary>
    private Tastenbelegung? _kandidat;

    /// <summary>Vorsichtig: welche Modifikatoren während des Belegens unten sind (je ein Bit, <see cref="ModifikatorBit"/>).</summary>
    private int _modifikatorenUnten;

    /// <summary>Vorsichtig: Seit dem ersten Modifikator kam eine andere Taste dazu (ein Kürzel); bis alle oben sind, zählt keiner.</summary>
    private bool _kombiniert;

    /// <summary>Strg, Alt, Umschalt und Windows, je links und rechts (Scancodes mit Präfix).</summary>
    public static bool IstModifikator(Tastenbelegung taste) => taste.Art == EingabeArt.Tastatur && ModifikatorBit(taste.Code) != 0;

    /// <summary>
    /// Tasten, mit denen man ein Fenster bedient: Tab (0x0F), Eingabe (0x1C), Leertaste (0x39), die Pfeile und Pos1, Ende, Bild auf, Bild ab
    /// (die eigenen Tasten mit Präfix E0; die des Ziffernblocks und die Eingabe dort zählen weiter).
    /// </summary>
    public static bool BedientFenster(Tastenbelegung taste) =>
        taste.Art == EingabeArt.Tastatur && taste.Code is 0x000F or 0x001C or 0x0039 or 0xE048 or 0xE050 or 0xE04B or 0xE04D or 0xE047 or 0xE04F or 0xE049 or 0xE051;

    private static int ModifikatorBit(int code) => code switch
    {
        0x001D => 1, // Strg links
        0xE01D => 2, // Strg rechts
        0x0038 => 4, // Alt links
        0xE038 => 8, // Alt rechts (AltGr)
        0x002A => 16, // Umschalt links
        0x0036 => 32, // Umschalt rechts
        0xE05B => 64, // Windows links
        0xE05C => 128, // Windows rechts
        _ => 0,
    };

    /// <summary>
    /// Beim Belegen: Ein Fenster dieses Programms ist vorn. Dann bedienen linke, rechte und mittlere Maustaste die Oberfläche (etwa
    /// "Abbrechen", ein Kontextmenü, das Rad) und werden nicht zur Taste; sonst sendete danach jeder Klick. Seit 0.10.0 auch rechts und mitte:
    /// Beim ersten Start wartet die Startseite von selbst auf die Sprechtaste, und ein Rechtsklick ins Fenster belegte sonst Maustaste 2 (E8).
    /// Maustaste 4 und 5 zählen auch im eigenen Fenster. Ohne Angabe gilt nichts als vorn.
    /// </summary>
    public Func<bool>? EigenesFensterVorn { get; set; }

    /// <summary>Linke, rechte und mittlere Maustaste: Im eigenen Fenster bedienen sie beim Belegen die Oberfläche (<see cref="EigenesFensterVorn"/>).</summary>
    public static bool BedientOberflaeche(Tastenbelegung taste) =>
        taste.Art == EingabeArt.Maus && taste.Code is Tastenbelegung.MausLinks or Tastenbelegung.MausRechts or Tastenbelegung.MausMitte;

    /// <summary>Eine einzige Sprechtaste (oder keine); siehe <see cref="Zuordnen"/>.</summary>
    public bool Belegen(Tastenbelegung? belegung) =>
        Zuordnen(belegung is { } b ? [new Tastenzuordnung(Tastenfunktion.Sprechen, b)] : []);

    /// <summary>Neue Zuordnungen; gehaltene Tasten gelten als losgelassen. Rückgabe wahr: Loslassen der Sprechtaste melden.</summary>
    public bool Zuordnen(IReadOnlyList<Tastenzuordnung> zuordnungen)
    {
        ArgumentNullException.ThrowIfNull(zuordnungen);
        _zuordnungen = [.. zuordnungen];
        _unten = new bool[_zuordnungen.Length];
        _virtuell = new ushort[_zuordnungen.Length];
        _obenGezaehlt = new int[_zuordnungen.Length];
        MeldenGedrueckt = false;
        if (!IstGedrueckt) return false;
        IstGedrueckt = false;
        return true;
    }

    /// <summary>
    /// Ein Rohereignis verarbeiten. <paramref name="wechsel"/>: +1 Sprechtaste gedrückt, -1 losgelassen, 0 nichts zu
    /// melden. <paramref name="gelernt"/> ist gesetzt, wenn gerade gelernt wurde.
    /// </summary>
    public void Verarbeiten(in Tastenereignis e, out int wechsel, out Tastenbelegung? gelernt) =>
        Verarbeiten(e, 0, out wechsel, out _, out gelernt);

    /// <summary>
    /// Wie oben, dazu <paramref name="virtuell"/> (virtueller Code dieses Ereignisses für den Hängeschutz, 0 wenn
    /// unbekannt) und <paramref name="ausgeloest"/>: eine Netztaste ging gerade hinunter.
    /// </summary>
    public void Verarbeiten(in Tastenereignis e, ushort virtuell, out int wechsel, out Tastenfunktion? ausgeloest, out Tastenbelegung? gelernt)
    {
        wechsel = 0;
        ausgeloest = null;
        gelernt = null;
        if (Lernt && Vorsichtig && e.Taste.Art == EingabeArt.Tastatur)
        {
            if (VorsichtigLernen(e) is { } taste)
            {
                Lernt = false;
                gelernt = taste;
                return;
            }
            // Gedrückt: weder gelernt noch eine Zuordnung (wie beim Lernen sonst). Losgelassen: weiter wie immer.
            if (e.Gedrueckt) return;
        }
        else if (Lernt && e.Gedrueckt)
        {
            if (BedientOberflaeche(e.Taste) && EigenesFensterVorn?.Invoke() == true) return;
            Lernt = false;
            gelernt = e.Taste;
            return;
        }
        var vorher = IstGedrueckt;
        for (var i = 0; i < _zuordnungen.Length; i++)
        {
            ref readonly var z = ref _zuordnungen[i];
            if (!z.Taste.GleicheTaste(e.Taste) || _unten[i] == e.Gedrueckt) continue;
            _unten[i] = e.Gedrueckt;
            _obenGezaehlt[i] = 0;
            if (e.Gedrueckt) _virtuell[i] = virtuell;
            if (e.Gedrueckt && !Tastenfunktionen.IstGehalten(z.Funktion)) ausgeloest ??= z.Funktion;
        }
        IstGedrueckt = Unten(Tastenfunktion.Sprechen);
        MeldenGedrueckt = Unten(Tastenfunktion.Melden);
        if (IstGedrueckt != vorher) wechsel = IstGedrueckt ? 1 : -1;
    }

    /// <summary>
    /// Vorsichtig belegen mit einem Tastaturereignis (<see cref="Vorsichtig"/>): liefert die Taste, die jetzt gilt, sonst null. Ein Modifikator merkt
    /// sich beim Drücken als Kandidat und gilt beim Loslassen, wenn bis dahin keine andere Taste kam; zwei zugleich (AltGr meldet Strg links und
    /// Alt rechts) ergeben den zuletzt gedrückten. Jede andere Taste zählt nur, wenn kein Modifikator unten ist und sie das eigene Fenster nicht
    /// bedient (<see cref="BedientFenster"/>, nur wenn es vorn ist).
    /// </summary>
    private Tastenbelegung? VorsichtigLernen(in Tastenereignis e)
    {
        var bit = ModifikatorBit(e.Taste.Code);
        if (bit != 0)
        {
            if (e.Gedrueckt)
            {
                // Tastenwiederholung beim Halten ändert nichts.
                if ((_modifikatorenUnten & bit) != 0) return null;
                _modifikatorenUnten |= bit;
                if (!_kombiniert) _kandidat = e.Taste;
                return null;
            }
            _modifikatorenUnten &= ~bit;
            var gilt = !_kombiniert && _kandidat is { } k && k.GleicheTaste(e.Taste);
            if (_modifikatorenUnten == 0 || gilt)
            {
                _kandidat = null;
                _kombiniert = false;
            }
            return gilt ? e.Taste : null;
        }
        if (!e.Gedrueckt) return null;
        if (_modifikatorenUnten != 0 || ModifikatorUnten?.Invoke() == true)
        {
            // Ein Kürzel (Strg+2, Alt+Tab): weder die Taste noch der Modifikator.
            _kandidat = null;
            _kombiniert = _modifikatorenUnten != 0;
            return null;
        }
        if (BedientFenster(e.Taste) && EigenesFensterVorn?.Invoke() == true) return null;
        return e.Taste;
    }

    /// <summary>
    /// Hängeschutz mit einem physischen Zustand für alle gehaltenen Tasten (eine Sprechtaste); siehe die Fassung mit
    /// Abfrage je virtuellem Code.
    /// </summary>
    public bool HaengerPruefen(bool physischUnten) => HaengerPruefen(_ => physischUnten);

    /// <summary>
    /// Hängeschutz: Raw Input meldet ein Loslassen nicht, wenn es hinter dem sicheren Desktop (UAC, Strg+Alt+Entf) oder
    /// bei gesperrtem Bildschirm geschieht; dann bliebe die Sprechtaste gedrückt und das Mikrofon offen. Der Aufrufer
    /// fragt, solange eine Taste unten ist, regelmäßig den physischen Zustand je virtuellem Code ab. Ist eine gehaltene
    /// Taste <see cref="HaengerPruefungen"/> Mal hintereinander oben, gilt sie als losgelassen. Rückgabe wahr: Loslassen
    /// der Sprechtaste melden.
    /// </summary>
    public bool HaengerPruefen(Func<ushort, bool> physischUnten)
    {
        ArgumentNullException.ThrowIfNull(physischUnten);
        var vorher = IstGedrueckt;
        for (var i = 0; i < _unten.Length; i++)
        {
            if (!_unten[i]) continue;
            if (physischUnten(_virtuell[i]))
            {
                _obenGezaehlt[i] = 0;
                continue;
            }
            if (++_obenGezaehlt[i] < HaengerPruefungen) continue;
            _obenGezaehlt[i] = 0;
            _unten[i] = false;
        }
        IstGedrueckt = Unten(Tastenfunktion.Sprechen);
        MeldenGedrueckt = Unten(Tastenfunktion.Melden);
        return vorher && !IstGedrueckt;
    }

    private bool Unten(Tastenfunktion funktion)
    {
        for (var i = 0; i < _unten.Length; i++)
        {
            if (_unten[i] && _zuordnungen[i].Funktion == funktion) return true;
        }
        return false;
    }
}
