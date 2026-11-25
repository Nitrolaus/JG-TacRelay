// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Funk.Protokoll.Identitaet;

/// <summary>
/// SPKI-Fingerabdruck: SHA-256 über das DER-kodierte SubjectPublicKeyInfo (3.1, 3.2, Vektor 8.5).
/// Er ist zugleich Geräte-Identität und Server-Pin. Wertetyp ohne Heap-Anteil; <c>default</c> ist 32 Nullbytes.
/// </summary>
public readonly struct Fingerabdruck : IEquatable<Fingerabdruck>
{
    /// <summary>Länge in Byte.</summary>
    public const int Laenge = 32;

    /// <summary>Länge der Anzeigeform <c>XXXX-XXXX-XXXX-XXXX</c>.</summary>
    public const int AnzeigeLaenge = 19;

    /// <summary>Länge der Base64-Form (Pin).</summary>
    public const int Base64Laenge = 44;

    private readonly Hash32 _wert;

    private Fingerabdruck(Hash32 wert) => _wert = wert;

    /// <summary>Übernimmt genau 32 Byte als Fingerabdruck.</summary>
    public Fingerabdruck(ReadOnlySpan<byte> wert)
    {
        if (wert.Length != Laenge) throw new ArgumentException($"Ein Fingerabdruck hat {Laenge} Byte.", nameof(wert));
        _wert = Hash32.Aus(wert);
    }

    internal Hash32 Wert => _wert;

    /// <summary>SHA-256 über die übergebenen SPKI-Bytes, ohne sie zu prüfen.</summary>
    public static Fingerabdruck AusSpki(ReadOnlySpan<byte> spkiDer)
    {
        Span<byte> hash = stackalloc byte[Laenge];
        SHA256.HashData(spkiDer, hash);
        return new Fingerabdruck(Hash32.Aus(hash));
    }

    /// <summary>Fingerabdruck des öffentlichen Teils eines Schlüssels.</summary>
    public static Fingerabdruck AusSchluessel(ECDsa schluessel)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        return AusSpki(schluessel.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Fingerabdruck des Blattzertifikats, gleich welcher Schlüsseltyp (Pin-Prüfung, 3.1).</summary>
    public static Fingerabdruck AusZertifikat(X509Certificate2 zertifikat)
    {
        ArgumentNullException.ThrowIfNull(zertifikat);
        return AusSpki(zertifikat.PublicKey.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Liest die Base64-Form streng (Standardalphabet, Auffüllung, kanonisch, 32 Byte).</summary>
    public static bool TryAusBase64(ReadOnlySpan<char> text, out Fingerabdruck fingerabdruck)
    {
        fingerabdruck = default;
        if (text.Length != Base64Laenge) return false;
        Span<byte> roh = stackalloc byte[Laenge];
        if (!StrengesBase64.TryDekodieren(text, roh, out var n) || n != Laenge) return false;
        fingerabdruck = new Fingerabdruck(Hash32.Aus(roh));
        return true;
    }

    /// <summary>Schreibt die 32 Byte in <paramref name="ziel"/>.</summary>
    public void Schreiben(Span<byte> ziel)
    {
        if (ziel.Length < Laenge) throw new ArgumentException($"Ziel braucht {Laenge} Byte.", nameof(ziel));
        _wert.Schreiben(ziel);
    }

    /// <summary>Die 32 Byte als neues Feld.</summary>
    public byte[] AlsBytes()
    {
        var bytes = new byte[Laenge];
        _wert.Schreiben(bytes);
        return bytes;
    }

    /// <summary>Base64-Form, so steht ein Pin im Serverprofil.</summary>
    public string AlsBase64()
    {
        Span<byte> roh = stackalloc byte[Laenge];
        _wert.Schreiben(roh);
        return Convert.ToBase64String(roh);
    }

    /// <summary>Anzeigeform: die ersten 8 Byte hexadezimal, groß, in 4er-Gruppen (Begriffe, 8.5).</summary>
    public string Anzeige => string.Create(AnzeigeLaenge, _wert.A, static (ziel, kopf) =>
    {
        const string Hex = "0123456789ABCDEF";
        var j = 0;
        for (var i = 0; i < 16; i++)
        {
            if (i is 4 or 8 or 12) ziel[j++] = '-';
            ziel[j++] = Hex[(int)(kopf >> (60 - 4 * i)) & 0xF];
        }
    });

    /// <summary>
    /// Ob eine eingegebene Anzeigeform zu diesem Fingerabdruck passt. Bindestriche und Leerraum werden
    /// übergangen, Groß- und Kleinschreibung ist egal (Admin-Werkzeug, 3.5).
    /// </summary>
    public bool AnzeigePasst(ReadOnlySpan<char> eingabe)
    {
        var anzeige = Anzeige;
        var j = 0;
        foreach (var c in eingabe)
        {
            if (c == '-' || char.IsWhiteSpace(c)) continue;
            while (j < anzeige.Length && anzeige[j] == '-') j++;
            if (j == anzeige.Length || char.ToUpperInvariant(c) != anzeige[j]) return false;
            j++;
        }
        return j == anzeige.Length;
    }

    public bool Equals(Fingerabdruck other) => _wert == other._wert;

    public override bool Equals(object? obj) => obj is Fingerabdruck f && Equals(f);

    public override int GetHashCode() => _wert.GetHashCode();

    public static bool operator ==(Fingerabdruck links, Fingerabdruck rechts) => links.Equals(rechts);

    public static bool operator !=(Fingerabdruck links, Fingerabdruck rechts) => !links.Equals(rechts);

    public override string ToString() => Anzeige;
}

/// <summary>
/// Kanonisches SubjectPublicKeyInfo eines P-256-Schlüssels (id-ecPublicKey, prime256v1, unkomprimierter Punkt):
/// genau 91 Byte mit festem Kopf. So kann ein Schlüssel nicht unter zwei Fingerabdrücken auftreten.
/// </summary>
public static class P256Spki
{
    /// <summary>Länge des kanonischen SPKI in Byte.</summary>
    public const int Laenge = 91;

    private static ReadOnlySpan<byte> Kopf =>
    [
        0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01,
        0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04,
    ];

    /// <summary>Ob die Bytes die kanonische P-256-Form haben. Prüft nicht, ob der Punkt auf der Kurve liegt.</summary>
    public static bool IstKanonisch(ReadOnlySpan<byte> spki) => spki.Length == Laenge && spki.StartsWith(Kopf);

    /// <summary>
    /// Ob das SPKI kanonisch P-256 ist und sich als Schlüssel laden lässt (Punkt auf der Kurve).
    /// </summary>
    public static bool IstGueltig(ReadOnlySpan<byte> spki)
    {
        if (!IstKanonisch(spki)) return false;
        try
        {
            using var schluessel = ECDsa.Create();
            schluessel.ImportSubjectPublicKeyInfo(spki, out var gelesen);
            return gelesen == Laenge;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false; // so meldet Windows-CNG einen Punkt außerhalb der Kurve (.NET 10, geprüft)
        }
    }
}
