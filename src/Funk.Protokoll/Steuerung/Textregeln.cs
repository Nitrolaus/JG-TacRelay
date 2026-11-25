// JG TacRelay - Maintainer: Sarevoc
using System.Text;

namespace Funk.Protokoll.Steuerung;

/// <summary>
/// Regeln für Zeichenketten nach 4.3: NFC, keine Steuerzeichen (U+0000-U+001F, U+007F-U+009F), keine
/// Bidi-Steuerzeichen (U+202A-U+202E, U+2066-U+2069); Längen zählen Unicode-Skalarwerte.
/// </summary>
public static class Textregeln
{
    /// <summary>
    /// Ob die Laufzeit NFC wirklich prüfen kann. Unter <c>InvariantGlobalization=true</c> (Directory.Build.props)
    /// ist <see cref="string.IsNormalized(NormalizationForm)"/> wirkungslos und meldet auch zerlegte Formen als NFC
    /// (gemessen unter .NET 10.0.12). Dann gilt nur die sichere Kurzprüfung: Text ganz unter U+0300 ist immer NFC.
    /// </summary>
    /// <remarks>
    /// Die Probe steht bewusst als Maskierung da ("a" + U+0308, zerlegt): Normalisiert ein Editor die Quelldatei
    /// nach NFC, würde aus einem sichtbaren zerlegten Zeichen still die vorkomponierte Form, und die Probe meldete
    /// dann immer "nicht prüfbar".
    /// </remarks>
    public static bool NfcPruefbar { get; } = !"a\u0308".IsNormalized(NormalizationForm.FormC);

    /// <summary>Ob der Skalarwert als Steuer- oder Bidi-Steuerzeichen verboten ist.</summary>
    public static bool IstVerboten(int skalar) =>
        skalar <= 0x1F || (skalar >= 0x7F && skalar <= 0x9F)
        || (skalar >= 0x202A && skalar <= 0x202E) || (skalar >= 0x2066 && skalar <= 0x2069);

    /// <summary>Zählt die Unicode-Skalarwerte (Surrogatpaare zählen einfach).</summary>
    public static int Skalarwerte(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var n = 0;
        foreach (var c in text)
        {
            if (!char.IsLowSurrogate(c)) n++;
        }
        return n;
    }

    /// <summary>
    /// Prüft einen bereits entschlüsselten (unmaskierten) UTF-8-Text auf verbotene Zeichen und NFC.
    /// Liefert <c>true</c>, wenn der Text gilt. Ungültiges UTF-8 gilt nicht.
    /// </summary>
    public static bool Pruefen(ReadOnlySpan<byte> utf8)
    {
        var nurUnterU0300 = true;
        var rest = utf8;
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(rest, out var rune, out var verbraucht) != System.Buffers.OperationStatus.Done) return false;
            if (IstVerboten(rune.Value)) return false;
            if (rune.Value >= 0x300) nurUnterU0300 = false;
            rest = rest[verbraucht..];
        }
        if (nurUnterU0300 || !NfcPruefbar) return true;
        return IstNfc(Encoding.UTF8.GetString(utf8));
    }

    /// <summary>Prüft eine .NET-Zeichenkette mit denselben Regeln wie <see cref="Pruefen(ReadOnlySpan{byte})"/>.</summary>
    public static bool Pruefen(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IstGueltigesUtf16(text)) return false;
        var nurUnterU0300 = true;
        foreach (var r in text.EnumerateRunes())
        {
            if (IstVerboten(r.Value)) return false;
            if (r.Value >= 0x300) nurUnterU0300 = false;
        }
        if (nurUnterU0300 || !NfcPruefbar) return true;
        return IstNfc(text);
    }

    /// <summary>NFC-Prüfung, die bei nicht normalisierbarem Text (z. B. nicht zugewiesene Codepunkte) ablehnt statt zu werfen.</summary>
    private static bool IstNfc(string text)
    {
        try { return text.IsNormalized(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }
    }

    private static bool IstGueltigesUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }
        return true;
    }
}
