// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;

namespace Funk.Klang.Eingabe;

/// <summary>
/// Joystick-Belegungen in <see cref="Tastenbelegung.Code"/>: 1 bis 128 sind Knöpfe, gezählt wie in der
/// Windows-Spielgerätesteuerung und in Star Citizen; ab <see cref="HutBasis"/> folgen die acht Richtungen der bis zu
/// vier POV-Hüte als HutBasis + 8 * (Hut - 1) + Richtung. Eine Hauptrichtung (oben, rechts, unten, links) gilt auch in
/// den beiden Nachbarschrägen als gedrückt, wie bei Spielen, die einen Hut als vier Knöpfe lesen; eine Schräge gilt nur
/// genau in dieser Richtung.
/// </summary>
public static class Joystickeingabe
{
    public const int Knoepfe = 128;
    public const int Huete = 4;
    public const int HutBasis = 0x100;

    public static Tastenbelegung Knopf(int knopf, Geraetekennung geraet)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(knopf, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(knopf, Knoepfe);
        return new Tastenbelegung(EingabeArt.Joystick, knopf, geraet.ToString());
    }

    /// <summary>Belegung einer Hutrichtung; <paramref name="hut"/> zählt ab 1.</summary>
    public static Tastenbelegung Hut(int hut, Hutrichtung richtung, Geraetekennung geraet)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hut, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hut, Huete);
        return new Tastenbelegung(EingabeArt.Joystick, HutBasis + (8 * (hut - 1)) + (int)richtung, geraet.ToString());
    }

    public static bool IstKnopf(int code, out int knopf)
    {
        knopf = code;
        return code is >= 1 and <= Knoepfe;
    }

    /// <summary>Zerlegt einen Hut-Code; <paramref name="hut"/> zählt ab 1.</summary>
    public static bool IstHut(int code, out int hut, out Hutrichtung richtung)
    {
        var rest = code - HutBasis;
        hut = (rest / 8) + 1;
        richtung = (Hutrichtung)(rest & 7);
        return rest >= 0 && rest < 8 * Huete;
    }

    public static bool IstGueltig(int code) => IstKnopf(code, out _) || IstHut(code, out _, out _);

    /// <summary>
    /// Richtung eines DirectInput-POV-Werts (Hundertstel Grad im Uhrzeigersinn ab oben) auf 45 Grad gerundet, oder -1
    /// für die Mitte. Mitte ist nach der DirectInput-Dokumentation jeder Wert mit LOWORD 0xFFFF; Werte ab 36000 gelten
    /// ebenfalls als Mitte.
    /// </summary>
    public static int HutRichtungAus(uint pov)
    {
        if ((pov & 0xFFFF) == 0xFFFF || pov >= 36000) return -1;
        return (int)((pov + 2250) / 4500) & 7;
    }

    public static string Richtungsname(Hutrichtung richtung) => richtung switch
    {
        Hutrichtung.Oben => "oben",
        Hutrichtung.ObenRechts => "oben rechts",
        Hutrichtung.Rechts => "rechts",
        Hutrichtung.UntenRechts => "unten rechts",
        Hutrichtung.Unten => "unten",
        Hutrichtung.UntenLinks => "unten links",
        Hutrichtung.Links => "links",
        _ => "oben links",
    };

    /// <summary>
    /// Name einer Belegung für die Oberfläche, etwa "VPC Stick MT-50CM3 - Knopf 5" oder "VPC Stick MT-50CM3 - Hut 1
    /// oben". Der Gerätename ist der beim Belegen gespeicherte; ohne ihn steht "Joystick". Andere Arten liefern die
    /// Kurzform von <see cref="Tastenbelegung.ToString"/>.
    /// </summary>
    public static string Anzeigename(Tastenbelegung belegung)
    {
        if (belegung.Art != EingabeArt.Joystick) return belegung.ToString();
        var name = Geraetekennung.Lesen(belegung.Geraet).Name.Trim();
        if (name.Length == 0) name = "Joystick";
        if (IstKnopf(belegung.Code, out var knopf)) return string.Create(CultureInfo.InvariantCulture, $"{name} - Knopf {knopf}");
        if (IstHut(belegung.Code, out var hut, out var richtung))
            return string.Create(CultureInfo.InvariantCulture, $"{name} - Hut {hut} {Richtungsname(richtung)}");
        return string.Create(CultureInfo.InvariantCulture, $"{name} - Eingabe {belegung.Code}");
    }
}

/// <summary>Richtung eines POV-Huts, im Uhrzeigersinn ab oben, in Schritten von 45 Grad.</summary>
public enum Hutrichtung
{
    Oben,
    ObenRechts,
    Rechts,
    UntenRechts,
    Unten,
    UntenLinks,
    Links,
    ObenLinks,
}

/// <summary>
/// Woran eine Joystick-Belegung ihr Gerät wiederfindet: Schnittstellenpfad (DIPROP_GUIDANDPATH), VID/PID und
/// Produktname. Die DirectInput-Instanz-GUID gehört nicht dazu: Sie hängt am Platz in DirectInputs Register, nicht am
/// Gerät, und wandert etwa zu den Klonen von Joystick Guardian (gemessen im JG Signal Monitor am 08.09.2026). In
/// <see cref="Tastenbelegung.Geraet"/> steht die Kennung als Text "VVVV:PPPP|Pfad|Produktname", VID und PID
/// hexadezimal.
/// </summary>
public readonly record struct Geraetekennung(string Pfad, ushort Vid, ushort Pid, string Name)
{
    /// <summary>
    /// Virtuelles Gerät nach dem Pfad: Klone von Joystick Guardian (HIDMaestro), vJoy und andere nachgebildete Geräte.
    /// Die Muster sind dieselben wie im JG Signal Monitor (core\src\DeviceCatalog.cpp, KindFromPath).
    /// </summary>
    public bool IstVirtuell
    {
        get
        {
            var p = Pfad ?? "";
            return p.Contains("hidmaestro", StringComparison.OrdinalIgnoreCase)
                || p.Contains("vid_1234&pid_bead", StringComparison.OrdinalIgnoreCase)
                || p.Contains("vjoy", StringComparison.OrdinalIgnoreCase)
                || p.Contains("root#", StringComparison.OrdinalIgnoreCase)
                || p.Contains("vhid", StringComparison.OrdinalIgnoreCase)
                || p.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                || p.Contains("hidmini", StringComparison.OrdinalIgnoreCase)
                || p.Contains("emulat", StringComparison.OrdinalIgnoreCase);
        }
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Vid:X4}:{Pid:X4}|{Pfad}|{Name}");

    /// <summary>
    /// Liest eine Kennung aus <see cref="Tastenbelegung.Geraet"/>. Ein bloßer Pfad ohne den Kopf "VVVV:PPPP|" gilt auch;
    /// VID und PID kommen dann aus dem Pfad ("vid_xxxx", "pid_xxxx"), der Name bleibt leer. Null wird zur leeren Kennung.
    /// </summary>
    public static Geraetekennung Lesen(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new Geraetekennung("", 0, 0, "");
        if (text.Length >= 10 && text[4] == ':' && text[9] == '|'
            && ushort.TryParse(text.AsSpan(0, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var vid)
            && ushort.TryParse(text.AsSpan(5, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var pid))
        {
            var rest = text[10..];
            // Pfade enthalten kein "|"; der Name darf eines enthalten und steht deshalb hinten.
            var trenner = rest.IndexOf('|', StringComparison.Ordinal);
            return trenner < 0
                ? new Geraetekennung(rest, vid, pid, "")
                : new Geraetekennung(rest[..trenner], vid, pid, rest[(trenner + 1)..]);
        }
        return new Geraetekennung(text, HexNach(text, "vid_"), HexNach(text, "pid_"), "");
    }

    private static ushort HexNach(string text, string marke)
    {
        var i = text.IndexOf(marke, StringComparison.OrdinalIgnoreCase);
        if (i < 0 || i + marke.Length + 4 > text.Length) return 0;
        return ushort.TryParse(text.AsSpan(i + marke.Length, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var wert)
            ? wert
            : (ushort)0;
    }
}

/// <summary>Ein angeschlossenes Spielgerät, wie DirectInput es aufzählt. <see cref="Instanz"/> taugt nur zum Öffnen gleich
/// nach dem Aufzählen, nicht zum Wiedererkennen (siehe <see cref="Geraetekennung"/>).</summary>
public sealed record SpielgeraetInfo(Guid Instanz, Geraetekennung Kennung, int Knoepfe, int Huete, bool MussAbfragen);
