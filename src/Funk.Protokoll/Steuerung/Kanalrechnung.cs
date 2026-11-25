// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

/// <summary>
/// Kanäle mit Ch. (6.16, seit 0.6.0): Ein Kanal ist das Paar (Kanalnetz, Ch.) mit der berechneten Kanal-ID
/// <c>0x8000 + Netz-ID x 32 + Ch.</c>. Nichts wird angelegt oder freigegeben; die ID ergibt sich aus Kanalnetz und Ch. und zurück.
/// Ch. 0 ist die Ruhe eines Kanalnetzes (ab Werk), Ch. 1 von Global ist Netz 1 selbst (<see cref="GlobalSelbst"/>). Ob eine Kanal-ID zu
/// einem bestehenden Kanalnetz passt, weiß nur, wer die NetzListe kennt; hier steht nur die Form.
/// </summary>
public static class Kanalrechnung
{
    /// <summary>Erste Kanal-ID; die IDs ab hier sind den Kanälen vorbehalten.</summary>
    public const ushort Basis = 0x8000;

    /// <summary>Plätze je Kanalnetz im ID-Raum (5 Bit).</summary>
    public const int ChJeNetz = 32;

    /// <summary>Höchster Ch.: Index 30, also höchstens 31 Einträge in <c>kanaele</c>.</summary>
    public const byte ChMax = 30;

    /// <summary>Einträge in <c>netze[].kanaele</c>: mindestens Ruhe und ein Kanal, höchstens 31.</summary>
    public const int EintraegeMin = 2;

    /// <summary>Einträge in <c>netze[].kanaele</c>, Index 0 bis 30.</summary>
    public const int EintraegeMax = ChMax + 1;

    /// <summary>Höchste Netz-ID eines Kanalnetzes: 0x8000 + 1022 x 32 + 30 = 65502 bleibt unter 65534.</summary>
    public const ushort KanalnetzIdMax = 1022;

    /// <summary>Kanalnetze in einer NetzListe; sie zählen zu den 128 Netzen.</summary>
    public const int KanalnetzeMax = 64;

    /// <summary>Länge einer Ch.-Bezeichnung ("Flottenführung").</summary>
    public const int ChNameMax = 20;

    /// <summary>Länge von <c>netze[].gruppe</c> ("Schiffe").</summary>
    public const int GruppeMax = 20;

    /// <summary>Ch. 0: Das Feld trägt das Kanalnetz, hört und sendet nichts.</summary>
    public const byte Ruhe = 0;

    /// <summary>Ch. 1 von Global: Netz 1 selbst.</summary>
    public const byte GlobalSelbst = 1;

    /// <summary>Wünsche je <c>RolleWaehlen</c>.</summary>
    public const int WuenscheMax = 16;

    /// <summary>Kanalsperren je Konto (6.17).</summary>
    public const int SperrenJeKontoMax = 16;

    /// <summary>Kanalsperren für alle ohne <c>ZUWEISEN</c> (6.17).</summary>
    public const int SperrenAlleMax = 64;

    /// <summary>Kanalsperren zusammen (6.17).</summary>
    public const int SperrenMax = 500;

    /// <summary>Einträge in <c>Mitgliedschaften.gesperrt</c>: die eigenen und die für alle.</summary>
    public const int GesperrtMax = SperrenJeKontoMax + SperrenAlleMax;

    /// <summary>Höchstdauer einer Kanalsperre (6.17): bis zum nächsten 04:00, höchstens 24 h.</summary>
    public const long SperreMaxMs = 24L * 60 * 60 * 1000;

    /// <summary>Fassung, ab der ein Client Kanäle kennt (Versionstor, 4.7).</summary>
    public const string MindestFassung = "0.6.0";

    /// <summary>Kanal-ID von Ch. <paramref name="ch"/> im Kanalnetz <paramref name="netz"/> (Netz 1 bis 1022, Ch. 0 bis 30).</summary>
    public static ushort Id(ushort netz, byte ch)
    {
        ArgumentOutOfRangeException.ThrowIfZero(netz);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(netz, KanalnetzIdMax);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ch, ChMax);
        return (ushort)(Basis + netz * ChJeNetz + ch);
    }

    /// <summary>Ob die ID im Bereich der Kanäle liegt (ab 0x8000), gültig oder nicht.</summary>
    public static bool IstKanalbereich(ushort id) => id >= Basis;

    /// <summary>Zerlegt eine Kanal-ID in Kanalnetz und Ch.; <c>false</c>, wenn sie dieser Form nicht genügt.</summary>
    public static bool Zerlegen(ushort id, out ushort netz, out byte ch)
    {
        netz = 0;
        ch = 0;
        if (id < Basis) return false;
        var n = (id - Basis) / ChJeNetz;
        var c = (id - Basis) % ChJeNetz;
        if (n is 0 or > KanalnetzIdMax || c > ChMax) return false;
        netz = (ushort)n;
        ch = (byte)c;
        return true;
    }

    /// <summary>Ob die ID die Ruhe eines Kanalnetzes ist (Ch. 0).</summary>
    public static bool IstRuhe(ushort id) => Zerlegen(id, out _, out var ch) && ch == Ruhe;

    /// <summary>Die berechnete Kanal-ID von Ch. 1 in Global (0x8021); sie gilt nirgends, Ch. 1 ist Netz 1 selbst.</summary>
    public static ushort GlobalSelbstId { get; } = Id(Grenzen.GlobalNetzId, GlobalSelbst);
}
