// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Funk.Protokoll.Identitaet;

/// <summary>
/// Einmalcode der Registrierung (3.4, Vektor 8.6): 12 Zeichen Crockford-Base32, also 60 bit aus
/// <c>RandomNumberGenerator</c>, angezeigt als <c>XXXX-XXXX-XXXX</c>. Der Server speichert nur SHA-256 über die
/// normalisierte ASCII-Form.
/// </summary>
public static class Einmalcode
{
    /// <summary>Crockford-Base32 ohne I, L, O, U.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Zeichen der normalisierten Form.</summary>
    public const int Zeichen = 12;

    /// <summary>Länge der Anzeigeform mit zwei Bindestrichen.</summary>
    public const int AnzeigeLaenge = 14;

    /// <summary>Längere Eingaben werden ohne weitere Prüfung abgelehnt (Härtung).</summary>
    public const int MaxEingabe = 64;

    /// <summary>Gültigkeit ab Ausstellung.</summary>
    public static readonly TimeSpan Gueltigkeit = TimeSpan.FromMinutes(5);

    /// <summary>Neuer Code in Anzeigeform.</summary>
    public static string Erzeugen() => Erzeugen(RandomNumberGenerator.Fill);

    internal static string Erzeugen(Zufallsquelle zufall)
    {
        Span<byte> roh = stackalloc byte[8];
        zufall(roh);
        var bits = BinaryPrimitives.ReadUInt64BigEndian(roh) & ((1UL << 60) - 1);
        CryptographicOperations.ZeroMemory(roh);
        return string.Create(AnzeigeLaenge, bits, static (ziel, wert) =>
        {
            var j = 0;
            for (var i = 0; i < Zeichen; i++)
            {
                if (i is 4 or 8) ziel[j++] = '-';
                ziel[j++] = Alphabet[(int)(wert >> (55 - 5 * i)) & 31];
            }
        });
    }

    /// <summary>
    /// Normalisiert wie der Server (3.4 Schritt 4): Leerraum und <c>-</c> entfernen, Großbuchstaben,
    /// <c>O</c> zu <c>0</c>, <c>I</c> und <c>L</c> zu <c>1</c>. Gültig sind danach genau 12 Zeichen aus dem Alphabet.
    /// Nicht-ASCII ist immer ungültig, damit keine Fallfaltung fremder Schriften einen Code trifft.
    /// </summary>
    /// <param name="ziel">mindestens 12 Zeichen</param>
    public static bool Normalisieren(ReadOnlySpan<char> eingabe, Span<char> ziel)
    {
        if (ziel.Length < Zeichen) throw new ArgumentException($"Ziel braucht {Zeichen} Zeichen.", nameof(ziel));
        if (eingabe.Length > MaxEingabe) return false;
        var n = 0;
        foreach (var roh in eingabe)
        {
            if (roh == '-' || char.IsWhiteSpace(roh)) continue;
            if (roh > 0x7F) return false;
            var c = roh is >= 'a' and <= 'z' ? (char)(roh - ('a' - 'A')) : roh;
            c = c switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => c,
            };
            if (n == Zeichen || !Alphabet.Contains(c, StringComparison.Ordinal)) return false;
            ziel[n++] = c;
        }
        return n == Zeichen;
    }

    /// <summary>SHA-256 über die normalisierte Form als ASCII.</summary>
    public static void Hash(ReadOnlySpan<char> normalisiert, Span<byte> ziel)
    {
        if (normalisiert.Length != Zeichen) throw new ArgumentException($"Erwartet {Zeichen} normalisierte Zeichen.", nameof(normalisiert));
        Span<byte> ascii = stackalloc byte[Zeichen];
        for (var i = 0; i < Zeichen; i++)
        {
            if (normalisiert[i] > 0x7F) throw new ArgumentException("Nur ASCII.", nameof(normalisiert));
            ascii[i] = (byte)normalisiert[i];
        }
        SHA256.HashData(ascii, ziel);
    }

    /// <summary>Normalisieren und hashen in einem Schritt; <c>false</c> bei ungültiger Eingabe.</summary>
    internal static bool TryHash(ReadOnlySpan<char> eingabe, out Hash32 hash)
    {
        hash = default;
        Span<char> norm = stackalloc char[Zeichen];
        if (!Normalisieren(eingabe, norm)) return false;
        Span<byte> h = stackalloc byte[32];
        Hash(norm, h);
        hash = Hash32.Aus(h);
        return true;
    }
}
