// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Funk.Tests")]
// Adminbuch, Mitgliederbuch und TOTP liegen im Server und brauchen die Zeichenregeln und Hilfen von hier.
[assembly: InternalsVisibleTo("funk-server")]

namespace Funk.Protokoll.Identitaet;

/// <summary>32 Byte (SHA-256) als Wertetyp, für Wörterbuchschlüssel ohne Heap-Anteil.</summary>
internal readonly record struct Hash32(ulong A, ulong B, ulong C, ulong D)
{
    public static Hash32 Aus(ReadOnlySpan<byte> wert) => new(
        BinaryPrimitives.ReadUInt64BigEndian(wert),
        BinaryPrimitives.ReadUInt64BigEndian(wert[8..]),
        BinaryPrimitives.ReadUInt64BigEndian(wert[16..]),
        BinaryPrimitives.ReadUInt64BigEndian(wert[24..]));

    public void Schreiben(Span<byte> ziel)
    {
        BinaryPrimitives.WriteUInt64BigEndian(ziel, A);
        BinaryPrimitives.WriteUInt64BigEndian(ziel[8..], B);
        BinaryPrimitives.WriteUInt64BigEndian(ziel[16..], C);
        BinaryPrimitives.WriteUInt64BigEndian(ziel[24..], D);
    }
}

/// <summary>Zufallsquelle als Naht für Tests; im Betrieb immer <c>RandomNumberGenerator.Fill</c>.</summary>
internal delegate void Zufallsquelle(Span<byte> ziel);

/// <summary>
/// Zeichenregeln aus 4.3 und 3.3: Längen in Unicode-Skalarwerten, keine Steuer- und Bidi-Zeichen,
/// NFC, Namensschlüssel nach NFKC und <c>ToUpperInvariant</c>.
/// </summary>
internal static class Zeichenregeln
{
    /// <summary>
    /// Ob <c>string.Normalize</c> in diesem Prozess wirkt. Unter <c>InvariantGlobalization=true</c> ist es
    /// eine Nullnummer; dann sind NFC-Prüfung und NFKC-Namensschlüssel wirkungslos (Bericht, offener Punkt).
    /// </summary>
    public static readonly bool NormalisierungWirksam = PruefeNormalisierung();

    private static bool PruefeNormalisierung()
    {
        try
        {
            return string.Equals("ﬁ".Normalize(NormalizationForm.FormKC), "fi", StringComparison.Ordinal);
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    public static bool IstVerboten(int skalar) =>
        skalar <= 0x1F || skalar is >= 0x7F and <= 0x9F || skalar is >= 0x202A and <= 0x202E || skalar is >= 0x2066 and <= 0x2069;

    /// <summary>Text nach 4.3 mit <paramref name="min"/> bis <paramref name="max"/> Skalarwerten.</summary>
    public static bool IstText(string? text, int min, int max)
    {
        if (text is null) return false;
        var rest = text.AsSpan();
        var anzahl = 0;
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out var verbraucht) != OperationStatus.Done) return false;
            if (IstVerboten(rune.Value)) return false;
            if (++anzahl > max) return false;
            rest = rest[verbraucht..];
        }
        if (anzahl < min) return false;
        try { return text.IsNormalized(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; } // nicht normalisierbar = ungueltig
    }

    /// <summary>
    /// Schlüssel für die Eindeutigkeit von Anzeigenamen, SC- und Discord-Namen (3.3, A53): NFKC, dann Großschreibung. Das große
    /// ẞ (U+1E9E) wird zum ß, wie <c>ToUpperInvariant</c> das kleine ß lässt; sonst hätten "groß" und "GROẞ" verschiedene
    /// Schlüssel. Über alle Zeichen bis U+1FFFF gemessen (01.10.2026) war das die einzige Stelle, an der der Schlüssel eines
    /// Zeichens von dem seiner Kleinschreibung abwich.
    /// </summary>
    public static string Namensschluessel(string name) =>
        name.Normalize(NormalizationForm.FormKC).ToUpperInvariant().Replace(GrossesEszett, KleinesEszett);

    private const char GrossesEszett = (char)0x1E9E;

    private const char KleinesEszett = (char)0x00DF;

    /// <summary>
    /// Zeichen, die sich nicht zeigen oder nur als Leerraum, ohne Format- oder Leerzeichen zu sein: Default_Ignorable_Code_Point
    /// aus Unicode (darunter der Verbinder U+034F, Variantenwähler, mongolische Freiformwähler und die Hangul-Füller, die als
    /// Buchstaben gelten) und das leere Braillemuster U+2800. Mit einem davon sähe ein Name aus wie ein vergebener und hätte
    /// doch einen anderen Namensschlüssel (Gegenprüfung des Nachtbaus, 01.10.2026).
    /// </summary>
    public static bool IstUnsichtbar(int skalar) =>
        skalar is 0x00AD or 0x034F or 0x061C or 0x115F or 0x1160 or 0x17B4 or 0x17B5 or (>= 0x180B and <= 0x180F)
            or (>= 0x200B and <= 0x200F) or (>= 0x202A and <= 0x202E) or (>= 0x2060 and <= 0x206F) or 0x2800 or 0x3164
            or (>= 0xFE00 and <= 0xFE0F) or 0xFEFF or 0xFFA0 or (>= 0xFFF0 and <= 0xFFF8) or (>= 0x1BCA0 and <= 0x1BCA3)
            or (>= 0x1D173 and <= 0x1D17A) or (>= 0xE0000 and <= 0xE0FFF);

    /// <summary>Kürzeste und längste Namen aus dem Setup (A42): 2 bis 32 Zeichen.</summary>
    public const int ScNameMin = 2;

    public const int ScNameMax = 32;

    /// <summary>
    /// SC-Name nach A42: Leerzeichen vorn und hinten fallen weg, dann 2 bis 32 Zeichen aus Buchstaben (auch Umlaute),
    /// Ziffern, Leerzeichen, <c>_</c>, <c>-</c> und <c>.</c>, NFC, ohne Steuer- und Richtungszeichen. Sonst <c>null</c>.
    /// </summary>
    public static string? ScNameBereinigen(string? text)
    {
        if (text is null) return null;
        var t = text.Trim(' ');
        if (!IstText(t, ScNameMin, ScNameMax)) return null;
        foreach (var r in t.EnumerateRunes())
        {
            // Die Hangul-Füller gelten als Buchstaben, zeigen sich aber nicht.
            if (IstUnsichtbar(r.Value)) return null;
            if (Rune.IsLetterOrDigit(r) || r.Value is ' ' or '_' or '-' or '.') continue;
            return null;
        }
        return t;
    }

    /// <summary>
    /// Discord-Name, wie eingetippt ("Mustermann/Max"): freier als der SC-Name, 2 bis 32 Zeichen, auch <c>/</c>, sonst wie
    /// 4.3. Leerzeichen vorn und hinten fallen weg. Weil "Wer ist da" ihn als Namen zeigt (Sarevoc, 30.09.2026, Antwort 3), sind
    /// unsichtbare und täuschende Zeichen verboten: Formatzeichen (etwa U+200B, U+200D, U+200E, U+FEFF, U+00AD), andere
    /// Leerzeichen als U+0020 und zwei Leerzeichen hintereinander, Zeilen- und Absatztrenner, private und nicht vergebene
    /// Zeichen, dazu alles aus <see cref="IstUnsichtbar"/> (Verbinder, Variantenwähler, Hangul-Füller). Sonst <c>null</c>.
    /// </summary>
    public static string? DiscordNameBereinigen(string? text)
    {
        if (text is null) return null;
        var t = text.Trim(' ');
        if (!IstText(t, ScNameMin, ScNameMax) || t.Contains("  ", StringComparison.Ordinal)) return null;
        foreach (var r in t.EnumerateRunes())
        {
            if (IstUnsichtbar(r.Value)) return null;
            var art = Rune.GetUnicodeCategory(r);
            if (art is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate)
                return null;
            if (art == UnicodeCategory.SpaceSeparator && r.Value != ' ') return null;
        }
        return t;
    }

    /// <summary>Kürzt auf höchstens <paramref name="max"/> Skalarwerte, ohne ein Ersatzpaar zu trennen.</summary>
    public static string Kuerzen(string text, int max)
    {
        var laenge = 0;
        var anzahl = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (anzahl == max) return text[..laenge];
            laenge += rune.Utf16SequenceLength;
            anzahl++;
        }
        return text;
    }

    /// <summary>Discord-Snowflake in kanonischer Dezimalform: nur Ziffern, keine führende Null, passt in u64, nicht 0.</summary>
    public static bool IstSnowflake(string? text) =>
        text is { Length: >= 1 and <= 20 }
        && text[0] is >= '1' and <= '9'
        && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    /// <summary>Discord-Benutzername: 1-32 druckbare ASCII-Zeichen ohne Leerzeichen.</summary>
    public static bool IstBenutzername(string? text)
    {
        if (text is not { Length: >= 1 and <= 32 }) return false;
        foreach (var c in text)
            if (c is < '!' or > '~') return false;
        return true;
    }

    /// <summary>Hostname nach RFC 1123 (Buchstaben, Ziffern, Bindestrich), höchstens 253 Zeichen, kein Punkt am Ende.</summary>
    public static bool IstHostname(string? text)
    {
        if (text is not { Length: >= 1 and <= 253 }) return false;
        var labelLaenge = 0;
        var vorher = '.';
        foreach (var c in text)
        {
            if (c == '.')
            {
                if (labelLaenge == 0 || vorher == '-') return false;
                labelLaenge = 0;
            }
            else
            {
                var erlaubt = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';
                if (!erlaubt || (labelLaenge == 0 && c == '-') || ++labelLaenge > 63) return false;
            }
            vorher = c;
        }
        return labelLaenge > 0 && vorher != '-';
    }
}

/// <summary>
/// Base64 nach 4.3 streng: Standardalphabet, mit Auffüllung, ohne Leerraum und kanonisch (ungenutzte
/// Bits des letzten Zeichens sind 0). <c>Convert.TryFromBase64Chars</c> allein duldet Leerraum.
/// </summary>
internal static class StrengesBase64
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    public static int DekodierteLaenge(ReadOnlySpan<char> text)
    {
        if (text.Length == 0 || text.Length % 4 != 0) return -1;
        var auffuellung = text[^1] == '=' ? (text[^2] == '=' ? 2 : 1) : 0;
        return text.Length / 4 * 3 - auffuellung;
    }

    public static bool TryDekodieren(ReadOnlySpan<char> text, Span<byte> ziel, out int geschrieben)
    {
        geschrieben = 0;
        var laenge = DekodierteLaenge(text);
        if (laenge < 0 || ziel.Length < laenge) return false;
        var auffuellung = text.Length / 4 * 3 - laenge;
        var daten = text[..^auffuellung];
        foreach (var c in daten)
            if (Alphabet.IndexOf(c, StringComparison.Ordinal) < 0) return false;
        var letzter = Alphabet.IndexOf(daten[^1], StringComparison.Ordinal);
        if (auffuellung == 1 && (letzter & 0x3) != 0) return false;
        if (auffuellung == 2 && (letzter & 0xF) != 0) return false;
        return Convert.TryFromBase64Chars(text, ziel, out geschrieben) && geschrieben == laenge;
    }
}

/// <summary>
/// Gleitendes Zeitfenster: zählt Ereignisse der letzten <c>fenster</c>, höchstens <c>obergrenze</c> gemerkt.
/// Misst mit der monotonen Uhr (<see cref="TimeProvider.GetTimestamp"/>, 4.8), damit ein Sprung der Wanduhr
/// keine Grenze vorzeitig öffnet.
/// </summary>
internal sealed class Zeitfensterzaehler(TimeProvider zeit, TimeSpan fenster, int obergrenze)
{
    private readonly Queue<long> _zeiten = new();

    public int Anzahl()
    {
        var jetzt = zeit.GetTimestamp();
        while (_zeiten.Count > 0 && zeit.GetElapsedTime(_zeiten.Peek(), jetzt) >= fenster) _zeiten.Dequeue();
        return _zeiten.Count;
    }

    public void Zaehlen()
    {
        if (Anzahl() < obergrenze) _zeiten.Enqueue(zeit.GetTimestamp());
    }
}
