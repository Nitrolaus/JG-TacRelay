// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;

namespace Funk.Protokoll.Steuerung;

/// <summary>Versionierung nach 4.7.</summary>
public static class Versionen
{
    /// <summary>ALPN-Kennzeichen der Hauptversion 1 (Steuerkanal und Framing).</summary>
    public const string Alpn = "tacrelay/1";

    /// <summary>Hauptversion; ein Bruch bekommt ein neues ALPN.</summary>
    public const int Hauptversion = 1;

    /// <summary>Höchste unterstützte Nebenversion (v1.0 = 0).</summary>
    public const ushort Nebenversion = 0;

    /// <summary>Vereinbarte Nebenversion: das Minimum beider Seiten.</summary>
    public static ushort NebenversionVereinbaren(ushort client, ushort server) => Math.Min(client, server);

    /// <summary>
    /// Ob die Client-Version unter <paramref name="mindest"/> liegt (dann <c>VERSION_ZU_ALT</c>). Eine nicht lesbare
    /// Client-Version gilt als zu alt; eine nicht lesbare Mindestversion ist ein Konfigurationsfehler.
    /// </summary>
    public static bool ClientZuAlt(string clientVersion, string mindest)
    {
        if (!SemVer.TryParse(mindest, out var m)) throw new ArgumentException("Mindestversion ist keine SemVer.", nameof(mindest));
        return !SemVer.TryParse(clientVersion, out var c) || c.CompareTo(m) < 0;
    }

    /// <summary>
    /// Seit welcher Fassung ein Server einen Pflichttyp kennt, wo er jünger ist als die Fassung 0.1.0 (4.7): 0x0017 Beauftragung und
    /// 0x0038 KontenVerzeichnis kamen mit 0.3.0, 0x003C Funkermodus und 0x003D PhaseSchalten mit 0.5.0, 0x0039 RolleWaehlen und 0x003A KanalSperre mit 0.6.0,
    /// 0x0025 Abzeichen mit 0.11.7, 0x0026 MeldungSenden und 0x0027 BereitMelden mit 0.12.0. Ein älterer Server trennt bei einem
    /// Pflichttyp, den er nicht kennt, mit PROTOKOLL.
    /// </summary>
    public static string? ServerFassungFuer(Nachrichtentyp typ) => typ switch
    {
        Nachrichtentyp.Beauftragung or Nachrichtentyp.KontenVerzeichnis => "0.3.0",
        Nachrichtentyp.Funkermodus or Nachrichtentyp.PhaseSchalten => "0.5.0",
        Nachrichtentyp.RolleWaehlen or Nachrichtentyp.KanalSperre => Kanalrechnung.MindestFassung,
        Nachrichtentyp.Abzeichen => AbzeichenAb,
        Nachrichtentyp.MeldungSenden or Nachrichtentyp.BereitMelden => MeldesystemAb,
        _ => null,
    };

    /// <summary>
    /// Seit dieser Fassung kennt ein Server das Meldesystem: 0x0026 MeldungSenden, 0x0027 BereitMelden, die Pushes 0x4005 Meldestand
    /// und 0x4006 BereitStand, das Feld <c>meldungenSenden</c> in Mitglied (AENDERN, LISTE) und KontoStand. Ein älterer übergeht das
    /// Feld still; den Haken setzt die Verwaltung deshalb nur an einem Server ab dieser Fassung.
    /// </summary>
    public const string MeldesystemAb = "0.12.0";

    /// <summary>
    /// Seit dieser Fassung kennt ein Server Abzeichen: 0x0025 Abzeichen und die neuen Felder in ProfilSetzen, KontoStand, Praesenz,
    /// Mitgliedskarte und Mitglied LISTE. Ein älterer übergeht die Felder still.
    /// </summary>
    public const string AbzeichenAb = "0.11.7";

    /// <summary>
    /// Ob ein Server, der in Welcome <paramref name="serverVersion"/> meldet, den Typ kennt. Ohne lesbare Fassung gilt ja; Angaben
    /// hinter dem Patch (Vorabstand) zählen nicht.
    /// </summary>
    public static bool ServerKennt(string? serverVersion, Nachrichtentyp typ) =>
        ServerFassungFuer(typ) is not { } noetig || ServerAb(serverVersion, noetig);

    /// <summary>
    /// Seit dieser Fassung kennt ein Server <c>GeraetEntscheiden</c> mit ENTSPERREN (Gerätesperre aufheben). Ein älterer trennt
    /// nicht, er antwortet UNGUELTIG, weil er den Wert nicht kennt; der Client fragt ihn gar nicht erst.
    /// </summary>
    public const string EntsperrenAb = "0.8.0";

    /// <summary>
    /// Ob <paramref name="serverVersion"/> (aus Welcome) mindestens <paramref name="fassung"/> ist. Wie bei
    /// <see cref="ServerKennt"/> gilt ohne lesbare Fassung ja, und ein Vorabstand zählt wie die Fassung selbst.
    /// </summary>
    public static bool ServerAb(string? serverVersion, string fassung)
    {
        if (!SemVer.TryParse(fassung, out var n) || !SemVer.TryParse(serverVersion, out var s)) return true;
        return (s with { Vorab = null }).CompareTo(n) >= 0;
    }

    /// <summary><c>Fehler{VERSION_ZU_ALT, details: {mindest}}</c> (4.5 Welcome).</summary>
    public static Fehler VersionZuAltFehler(string mindest) => new()
    {
        Code = Fehlercode.VersionZuAlt,
        Details = SteuerJson.Element(new MindestAngabe { Mindest = mindest }),
    };
}

/// <summary><c>details</c> bei VERSION_ZU_ALT.</summary>
public sealed record MindestAngabe
{
    public required string Mindest { get; init; }
}

/// <summary>
/// Semantische Version nach SemVer 2.0.0: <c>MAJOR.MINOR.PATCH[-vorab][+build]</c>. Build-Angaben zählen beim
/// Vergleich nicht.
/// </summary>
public readonly record struct SemVer(ulong Major, ulong Minor, ulong Patch, string? Vorab) : IComparable<SemVer>
{
    public static bool operator <(SemVer a, SemVer b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVer a, SemVer b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVer a, SemVer b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVer a, SemVer b) => a.CompareTo(b) >= 0;

    /// <summary>Liest eine Version streng (keine führenden Nullen, keine leeren Kennungen).</summary>
    public static bool TryParse(string? text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrEmpty(text)) return false;
        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0 && !KennungenGueltig(text[(plus + 1)..], numerischOhneNull: false)) return false;
        var kern = plus >= 0 ? text[..plus] : text;
        string? vorab = null;
        var minus = kern.IndexOf('-', StringComparison.Ordinal);
        if (minus >= 0)
        {
            vorab = kern[(minus + 1)..];
            if (!KennungenGueltig(vorab, numerischOhneNull: true)) return false;
            kern = kern[..minus];
        }
        var teile = kern.Split('.');
        if (teile.Length != 3) return false;
        if (!Zahl(teile[0], out var a) || !Zahl(teile[1], out var b) || !Zahl(teile[2], out var c)) return false;
        version = new SemVer(a, b, c, vorab);
        return true;
    }

    public int CompareTo(SemVer other)
    {
        var r = Major.CompareTo(other.Major);
        if (r == 0) r = Minor.CompareTo(other.Minor);
        if (r == 0) r = Patch.CompareTo(other.Patch);
        if (r != 0) return r;
        if (Vorab is null) return other.Vorab is null ? 0 : 1;
        if (other.Vorab is null) return -1;
        var x = Vorab.Split('.');
        var y = other.Vorab.Split('.');
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var xz = ulong.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out var xn);
            var yz = ulong.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out var yn);
            r = (xz, yz) switch
            {
                (true, true) => xn.CompareTo(yn),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(x[i], y[i]),
            };
            if (r != 0) return r;
        }
        return x.Length.CompareTo(y.Length);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
        + (Vorab is null ? "" : "-" + Vorab);

    private static bool Zahl(string s, out ulong wert)
    {
        wert = 0;
        if (s.Length == 0 || (s.Length > 1 && s[0] == '0')) return false;
        foreach (var ch in s)
        {
            if (ch is < '0' or > '9') return false;
        }
        return ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out wert);
    }

    private static bool KennungenGueltig(string s, bool numerischOhneNull)
    {
        if (s.Length == 0) return false;
        foreach (var k in s.Split('.'))
        {
            if (k.Length == 0) return false;
            var numerisch = true;
            foreach (var ch in k)
            {
                if (ch is not ((>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '-')) return false;
                numerisch &= ch is >= '0' and <= '9';
            }
            if (numerischOhneNull && numerisch && k.Length > 1 && k[0] == '0') return false;
        }
        return true;
    }
}
