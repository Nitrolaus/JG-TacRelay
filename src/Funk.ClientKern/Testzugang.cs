// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Funk.Protokoll.Identitaet;

namespace Funk.ClientKern;

/// <summary>
/// Testzugang für Last- und Zusammenspieltests ohne TLS, nur auf Loopback. Unter Windows braucht ein echter TLS-Handshake
/// einen Schlüssel im Benutzerprofil (Schannel); der Testzugang umgeht das, beweist aber den Besitz des Geräteschlüssels:
/// <code>
/// Client -> Server: "TRTZ" (4), Version 1 (1), SPKI (91)
/// Server -> Client: Zufallswert (32)
/// Client -> Server: ECDSA-P-256-Signatur P1363 (64) über SHA-256(Präfix || Zufallswert || SPKI)
/// </code>
/// Danach folgen Steuerrahmen nach 4.2, unverschlüsselt. Kein Bestandteil des Protokolls, nie im Betrieb: Der Dienst
/// selbst hat keinen solchen Zugang, nur das Testwerkzeug <c>funk-testserver</c> bietet ihn auf Loopback an.
/// </summary>
public static class Testzugang
{
    public const int SpkiLaenge = P256Spki.Laenge;
    public const int ZufallLaenge = 32;
    public const int SignaturLaenge = 64;
    public const int AnfangLaenge = 5 + SpkiLaenge;

    /// <summary>Kennung am Anfang: "TRTZ" und Version 1.</summary>
    public static ReadOnlySpan<byte> Kennung => "TRTZ\u0001"u8;

    /// <summary>Signiertes Präfix mit Nullbyte, trennt den Nachweis von jeder anderen Signatur des Geräteschlüssels.</summary>
    public static ReadOnlySpan<byte> Praefix => "tacrelay testzugang v1\0"u8;

    /// <summary>Ob die Adresse Loopback ist; nur dort gibt es den Testzugang.</summary>
    public static bool IstLoopback(IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        return IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);
    }

    /// <summary>Signatur des Nachweises mit dem Geräteschlüssel.</summary>
    public static byte[] NachweisBilden(ECDsa schluessel, ReadOnlySpan<byte> zufall, ReadOnlySpan<byte> spki)
    {
        ArgumentNullException.ThrowIfNull(schluessel);
        Span<byte> hash = stackalloc byte[32];
        HashBilden(zufall, spki, hash);
        return schluessel.SignHash(hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>Prüft den Nachweis; liefert den Fingerabdruck des Geräts, wenn die Signatur zum SPKI passt.</summary>
    public static bool NachweisPruefen(ReadOnlySpan<byte> spki, ReadOnlySpan<byte> zufall, ReadOnlySpan<byte> signatur, out Fingerabdruck fingerabdruck)
    {
        fingerabdruck = default;
        if (spki.Length != SpkiLaenge || signatur.Length != SignaturLaenge || !P256Spki.IstGueltig(spki)) return false;
        try
        {
            using var oeffentlich = ECDsa.Create();
            oeffentlich.ImportSubjectPublicKeyInfo(spki, out _);
            Span<byte> hash = stackalloc byte[32];
            HashBilden(zufall, spki, hash);
            if (!oeffentlich.VerifyHash(hash, signatur, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        fingerabdruck = Fingerabdruck.AusSpki(spki);
        return true;
    }

    private static void HashBilden(ReadOnlySpan<byte> zufall, ReadOnlySpan<byte> spki, Span<byte> ziel)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(Praefix);
        h.AppendData(zufall);
        h.AppendData(spki);
        h.GetHashAndReset(ziel);
    }
}

/// <summary>Aufbau über den <see cref="Testzugang"/> eines Testservers auf Loopback.</summary>
public sealed class TestzugangAufbau : IVerbindungsaufbau
{
    private readonly IPEndPoint _ziel;
    private readonly Geraeteidentitaet _identitaet;
    private readonly IPAddress? _lokal;

    /// <param name="ziel">Testzugang des Servers; nur Loopback.</param>
    /// <param name="identitaet">Geräteschlüssel, dessen Besitz bewiesen wird.</param>
    /// <param name="lokal">Lokale Adresse (etwa 127.0.x.y, damit jedes simulierte Gerät ein eigenes Präfix hat).</param>
    public TestzugangAufbau(IPEndPoint ziel, Geraeteidentitaet identitaet, IPAddress? lokal = null)
    {
        ArgumentNullException.ThrowIfNull(ziel);
        ArgumentNullException.ThrowIfNull(identitaet);
        if (!Testzugang.IstLoopback(ziel.Address)) throw new ArgumentException("Den Testzugang gibt es nur auf Loopback.", nameof(ziel));
        if (lokal is not null && !Testzugang.IstLoopback(lokal)) throw new ArgumentException("Lokale Adresse muss Loopback sein.", nameof(lokal));
        _ziel = ziel;
        _identitaet = identitaet;
        _lokal = lokal;
    }

    public async Task<Steuerverbindung> AufbauenAsync(bool wiederverbindung, CancellationToken abbruch)
    {
        var s = new Socket(_ziel.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true, SendBufferSize = 32 * 1024 };
        try
        {
            if (_lokal is not null) s.Bind(new IPEndPoint(_lokal, 0));
            using var frist = CancellationTokenSource.CreateLinkedTokenSource(abbruch);
            frist.CancelAfter(TimeSpan.FromSeconds(5));
            await s.ConnectAsync(_ziel, frist.Token).ConfigureAwait(false);
            var strom = new NetworkStream(s, ownsSocket: false);
            var spki = _identitaet.Schluessel.ExportSubjectPublicKeyInfo();
            var anfang = new byte[Testzugang.AnfangLaenge];
            Testzugang.Kennung.CopyTo(anfang);
            spki.CopyTo(anfang.AsSpan(5));
            await strom.WriteAsync(anfang, frist.Token).ConfigureAwait(false);
            var zufall = new byte[Testzugang.ZufallLaenge];
            await strom.ReadExactlyAsync(zufall, frist.Token).ConfigureAwait(false);
            await strom.WriteAsync(Testzugang.NachweisBilden(_identitaet.Schluessel, zufall, spki), frist.Token).ConfigureAwait(false);
            var lokal = ((IPEndPoint)s.LocalEndPoint!).Address;
            return new Steuerverbindung(strom, _ziel.Address, lokal, s, beschreibung: "Testzugang");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or EndOfStreamException)
        {
            s.Dispose();
            abbruch.ThrowIfCancellationRequested();
            throw new AufbauException(AufbauFehler.Netz, "Testzugang nicht erreichbar", ex);
        }
    }
}
