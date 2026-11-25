// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Funk.Protokoll.Identitaet;

// Liegt im Server (funk-server), nicht in der Client-Bibliothek Funk.Protokoll: Das normale Setup bringt keinen
// Verwaltungscode mit (A45, Gegenprüfung Stufe 1). Der Namensraum bleibt, damit Spezifikation, Server und Tests dieselben
// Namen tragen.

/// <summary>
/// Einmalpasswort nach RFC 6238 (TOTP) über RFC 4226 (HOTP) mit den Standardwerten, die jede App versteht, auch
/// jede Authenticator-App: HMAC-SHA-1, 6 Ziffern, 30 Sekunden (3.9). SHA-1 ist hier unbedenklich, denn die
/// Kollisionsschwäche betrifft HMAC nicht. Reine Funktionen ohne Zustand; Einmalverwendung und Fenster regelt das
/// <see cref="Adminbuch"/>.
/// </summary>
public static class Totp
{
    /// <summary>Ziffern eines Codes.</summary>
    public const int Stellen = 6;

    /// <summary>Länge eines Zeitschritts in Sekunden (X in RFC 6238).</summary>
    public const int SchrittSekunden = 30;

    /// <summary>Geheimnis 160 Bit, wie RFC 4226 Abschnitt 4 empfiehlt.</summary>
    public const int GeheimnisBytes = 20;

    /// <summary>Herausgeber im otpauth-URI und in der App.</summary>
    public const string Herausgeber = "JG TacRelay";

    /// <summary>Zeitschritt T = floor(Unixzeit / 30) (RFC 6238, 4.2, T0 = 0).</summary>
    public static long Zeitschritt(DateTimeOffset zeit) => zeit.ToUnixTimeSeconds() switch
    {
        < 0 => -1,
        var s => s / SchrittSekunden,
    };

    /// <summary>HOTP nach RFC 4226, 5.3 (dynamische Kürzung), mit <paramref name="stellen"/> Ziffern (6 bis 8).</summary>
    public static int Hotp(ReadOnlySpan<byte> geheimnis, long zaehler, int stellen = Stellen)
    {
        if (stellen is < 6 or > 8) throw new ArgumentOutOfRangeException(nameof(stellen));
        ArgumentOutOfRangeException.ThrowIfNegative(zaehler);
        Span<byte> nachricht = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(nachricht, zaehler);
        Span<byte> mac = stackalloc byte[HMACSHA1.HashSizeInBytes];
#pragma warning disable CA5350 // RFC 6238/4226 mit den Standardwerten, die jede App versteht; die Kollisionsschwäche von SHA-1 betrifft HMAC nicht (3.9).
        HMACSHA1.HashData(geheimnis, nachricht, mac);
#pragma warning restore CA5350
        var versatz = mac[^1] & 0x0F;
        var wert = ((mac[versatz] & 0x7F) << 24) | (mac[versatz + 1] << 16) | (mac[versatz + 2] << 8) | mac[versatz + 3];
        CryptographicOperations.ZeroMemory(mac);
        var teiler = stellen switch
        {
            6 => 1_000_000,
            7 => 10_000_000,
            _ => 100_000_000,
        };
        return wert % teiler;
    }

    /// <summary>Code zum Zeitschritt als Zahl.</summary>
    public static int Code(ReadOnlySpan<byte> geheimnis, long zeitschritt) => Hotp(geheimnis, zeitschritt);

    /// <summary>Code zum Zeitschritt in der Form, die die App zeigt (führende Nullen).</summary>
    public static string CodeText(ReadOnlySpan<byte> geheimnis, long zeitschritt) =>
        Code(geheimnis, zeitschritt).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>
    /// Liest einen eingegebenen Code: Leerraum und Bindestriche werden übergangen, danach genau 6 ASCII-Ziffern.
    /// Alles andere ist ungültig (auch Ziffern anderer Schriften).
    /// </summary>
    public static bool TryLesen(ReadOnlySpan<char> eingabe, out int code)
    {
        code = 0;
        if (eingabe.Length > 32) return false;
        var n = 0;
        foreach (var c in eingabe)
        {
            if (c == '-' || c == ' ') continue;
            if (c is < '0' or > '9' || ++n > Stellen) return false;
            code = code * 10 + (c - '0');
        }
        return n == Stellen;
    }

    /// <summary>
    /// Vergleicht einen eingegebenen Code mit dem Code eines Zeitschritts in konstanter Zeit
    /// (<see cref="CryptographicOperations.FixedTimeEquals"/>).
    /// </summary>
    public static bool Passt(ReadOnlySpan<byte> geheimnis, long zeitschritt, int eingabe)
    {
        if (zeitschritt < 0) return false;
        Span<byte> soll = stackalloc byte[4];
        Span<byte> ist = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(soll, Code(geheimnis, zeitschritt));
        BinaryPrimitives.WriteInt32BigEndian(ist, eingabe);
        return CryptographicOperations.FixedTimeEquals(soll, ist);
    }

    /// <summary>Neues Geheimnis aus <see cref="RandomNumberGenerator"/>.</summary>
    public static byte[] NeuesGeheimnis() => RandomNumberGenerator.GetBytes(GeheimnisBytes);

    /// <summary>
    /// <c>otpauth://totp/JG%20TacRelay:&lt;Name&gt;?secret=...&amp;issuer=JG%20TacRelay</c> (Key Uri Format, das jede
    /// Authenticator-App einliest). Algorithmus, Ziffern und Periode stehen ausdrücklich darin, obwohl es die Standardwerte
    /// sind, damit keine App rät. Name und Herausgeber werden prozentkodiert (der Herausgeber hat ein Leerzeichen).
    /// </summary>
    public static string OtpauthUri(string kontoname, ReadOnlySpan<byte> geheimnis)
    {
        ArgumentNullException.ThrowIfNull(kontoname);
        var etikett = Uri.EscapeDataString(Herausgeber + ":" + kontoname);
        return "otpauth://totp/" + etikett + "?secret=" + Base32.Kodieren(geheimnis) + "&issuer=" + Uri.EscapeDataString(Herausgeber)
            + "&algorithm=SHA1&digits=6&period=30";
    }

    /// <summary>Geheimnis in Vierergruppen zum Abtippen, etwa <c>JBSW Y3DP EHPK 3PXP</c>.</summary>
    public static string Gruppiert(ReadOnlySpan<byte> geheimnis)
    {
        var text = Base32.Kodieren(geheimnis);
        var sb = new StringBuilder(text.Length + text.Length / 4);
        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(text[i]);
        }
        return sb.ToString();
    }
}

/// <summary>Base32 nach RFC 4648, Abschnitt 6 (Alphabet A-Z, 2-7), ohne Auffüllung, wie es otpauth-URIs tragen.</summary>
public static class Base32
{
    /// <summary>Das Alphabet aus RFC 4648.</summary>
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Kodiert ohne <c>=</c>.</summary>
    public static string Kodieren(ReadOnlySpan<byte> daten)
    {
        var sb = new StringBuilder((daten.Length * 8 + 4) / 5);
        var puffer = 0;
        var bits = 0;
        foreach (var b in daten)
        {
            puffer = (puffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(puffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(puffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    /// <summary>
    /// Dekodiert; Leerraum wird übergangen, Kleinbuchstaben gelten wie große, <c>=</c> am Ende ist erlaubt. Übrige
    /// Bits am Ende müssen 0 sein (kanonisch).
    /// </summary>
    public static bool TryDekodieren(ReadOnlySpan<char> text, out byte[] daten)
    {
        daten = [];
        var ausgabe = new List<byte>(text.Length * 5 / 8);
        var puffer = 0;
        var bits = 0;
        var ende = false;
        foreach (var roh in text)
        {
            if (roh is ' ' or '\t') continue;
            if (roh == '=')
            {
                ende = true;
                continue;
            }
            if (ende) return false;
            var c = roh is >= 'a' and <= 'z' ? (char)(roh - ('a' - 'A')) : roh;
            var wert = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (wert < 0) return false;
            puffer = (puffer << 5) | wert;
            bits += 5;
            if (bits >= 8)
            {
                ausgabe.Add((byte)(puffer >> (bits - 8)));
                bits -= 8;
            }
            puffer &= (1 << bits) - 1;
        }
        if (bits >= 5 || puffer != 0) return false;
        daten = [.. ausgabe];
        return true;
    }
}
