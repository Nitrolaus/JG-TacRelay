// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

namespace Funk.Protokoll.Identitaet;

/// <summary>
/// Signiertes Serverprofil <c>{daten: b64, signatur: b64}</c> (3.1, Vektor 8.11). Geprüft wird über die
/// empfangenen Bytes von <c>daten</c>, ohne Kanonisierung; der Server reicht das Objekt nur durch.
/// </summary>
public sealed class SigniertesProfil
{
    /// <summary>Länge einer P-256-Signatur im Format IEEE P1363 (r und s je 32 Byte).</summary>
    public const int SignaturLaenge = 64;

    private readonly byte[] _daten;
    private readonly byte[] _signatur;

    /// <summary>ASCII <c>"tacrelay profil v1"</c> und ein Nullbyte, 19 Byte, vor <c>daten</c> signiert.</summary>
    public static ReadOnlySpan<byte> Praefix => "tacrelay profil v1\0"u8;

    /// <summary>Übernimmt Kopien der Bytes. Die Signatur muss 64 Byte lang sein.</summary>
    public SigniertesProfil(ReadOnlySpan<byte> daten, ReadOnlySpan<byte> signatur)
    {
        if (daten.IsEmpty || daten.Length > Serverprofil.MaxDatenBytes)
            throw new ArgumentException($"daten muss 1 bis {Serverprofil.MaxDatenBytes} Byte lang sein.", nameof(daten));
        if (signatur.Length != SignaturLaenge)
            throw new ArgumentException($"Die Signatur hat {SignaturLaenge} Byte.", nameof(signatur));
        _daten = daten.ToArray();
        _signatur = signatur.ToArray();
    }

    public ReadOnlySpan<byte> Daten => _daten;

    public ReadOnlySpan<byte> Signatur => _signatur;

    /// <summary>SHA-256 über <see cref="Praefix"/> und <paramref name="daten"/>; das ist der signierte Hashwert.</summary>
    public static void HashBilden(ReadOnlySpan<byte> daten, Span<byte> ziel)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(Praefix);
        h.AppendData(daten);
        if (!h.TryGetHashAndReset(ziel, out var n) || n != 32)
            throw new ArgumentException("Ziel braucht 32 Byte.", nameof(ziel));
    }

    /// <summary>Signiert offline mit dem Profilschlüssel (Werkzeug und Tests; nie auf dem Server).</summary>
    public static SigniertesProfil Signieren(ReadOnlySpan<byte> daten, ECDsa profilSchluessel)
    {
        ArgumentNullException.ThrowIfNull(profilSchluessel);
        Span<byte> hash = stackalloc byte[32];
        HashBilden(daten, hash);
        var signatur = profilSchluessel.SignHash(hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new SigniertesProfil(daten, signatur);
    }

    /// <summary>
    /// Ob die Signatur mit dem öffentlichen Schlüssel gilt. Wirft der Kryptoanbieter bei einer Signatur, die er nicht
    /// verarbeiten kann (unter Windows-CNG gemessen: kein Wurf, <c>false</c>; unter OpenSSL ungeprüft), gilt sie als
    /// ungültig. Das Admin-Werkzeug prüft Profile unter Linux.
    /// </summary>
    public bool SignaturGilt(ECDsa oeffentlich)
    {
        ArgumentNullException.ThrowIfNull(oeffentlich);
        Span<byte> hash = stackalloc byte[32];
        HashBilden(_daten, hash);
        try
        {
            return oeffentlich.VerifyHash(hash, _signatur, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary><c>{"daten":"...","signatur":"..."}</c>, kompakt.</summary>
    public byte[] AlsJson()
    {
        var puffer = new ArrayBufferWriter<byte>(_daten.Length * 4 / 3 + 128);
        using (var w = new Utf8JsonWriter(puffer))
        {
            w.WriteStartObject();
            w.WriteBase64String("daten", _daten);
            w.WriteBase64String("signatur", _signatur);
            w.WriteEndObject();
        }
        return puffer.WrittenSpan.ToArray();
    }

    /// <summary>Liest das Objekt streng (4.3). Beide Felder sind Pflicht. Wirft nie.</summary>
    public static bool TryLesen(ReadOnlySpan<byte> json, [NotNullWhen(true)] out SigniertesProfil? profil)
    {
        profil = null;
        if (json.IsEmpty || json.Length > 2 * Serverprofil.MaxDatenBytes) return false;
        try
        {
            var r = new Utf8JsonReader(json, Serverprofil.LeseOptionen);
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return false;
            byte[]? daten = null, signatur = null;
            var gesehen = new HashSet<string>(StringComparer.Ordinal);
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                var feld = r.GetString()!;
                if (!gesehen.Add(feld) || !r.Read()) return false;
                if (r.TokenType == JsonTokenType.Null) continue;
                var ok = feld switch
                {
                    "daten" => StrengesJson.Base64(ref r, Serverprofil.MaxDatenBytes, out daten),
                    "signatur" => StrengesJson.Base64(ref r, SignaturLaenge, out signatur),
                    _ => StrengesJson.Ueberspringen(ref r),
                };
                if (!ok) return false;
            }
            if (r.TokenType != JsonTokenType.EndObject || r.Read()) return false;
            if (daten is null || signatur is not { Length: SignaturLaenge }) return false;
            profil = new SigniertesProfil(daten, signatur);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>Ein fest eingebauter öffentlicher Profilschlüssel (aktuell oder Reserve), ECDSA P-256.</summary>
public sealed class Profilschluessel : IDisposable
{
    private readonly ECDsa _schluessel;

    private Profilschluessel(ECDsa schluessel, Fingerabdruck fingerabdruck)
    {
        _schluessel = schluessel;
        Fingerabdruck = fingerabdruck;
    }

    public Fingerabdruck Fingerabdruck { get; }

    /// <summary>Lädt den Schlüssel aus dem SPKI; nur kanonisches P-256 wird angenommen.</summary>
    public static Profilschluessel AusSpki(ReadOnlySpan<byte> spki)
    {
        if (!P256Spki.IstKanonisch(spki)) throw new ArgumentException("Profilschlüssel müssen ECDSA P-256 sein.", nameof(spki));
        var schluessel = ECDsa.Create();
        try
        {
            schluessel.ImportSubjectPublicKeyInfo(spki, out _);
        }
        catch
        {
            schluessel.Dispose();
            throw;
        }
        return new Profilschluessel(schluessel, Fingerabdruck.AusSpki(spki));
    }

    public bool Prueft(SigniertesProfil profil)
    {
        ArgumentNullException.ThrowIfNull(profil);
        return profil.SignaturGilt(_schluessel);
    }

    public void Dispose() => _schluessel.Dispose();
}

/// <summary>Urteil über ein angebotenes Profil (3.1).</summary>
public enum ProfilUrteil
{
    /// <summary>Signatur gilt, Inhalt gültig, Version größer als die gespeicherte: übernehmen.</summary>
    Uebernehmen,

    /// <summary>Gleiche oder kleinere Version: still übergehen.</summary>
    NichtNeuer,

    /// <summary>Keine eingebaute Signatur gilt: verwerfen und protokollieren.</summary>
    SignaturUngueltig,

    /// <summary>Signatur gilt, aber der Inhalt verletzt das Schema: verwerfen und protokollieren.</summary>
    InhaltUngueltig,
}

/// <summary>Ergebnis der Profilprüfung. <see cref="Abgelaufen"/> heißt: benutzen, aber warnen.</summary>
public sealed record ProfilPruefung(ProfilUrteil Urteil, Serverprofil? Profil, bool Abgelaufen);

/// <summary>Annahmeregel für Serverprofile im Client und im Admin-Werkzeug (3.1, P-14).</summary>
public static class ProfilAnnahme
{
    /// <summary>
    /// Erst die Signatur gegen die eingebauten Schlüssel, dann der Inhalt, dann die Version. Unsigniertes wird
    /// nie gelesen, auch keine neuen Hosts oder Pins.
    /// </summary>
    /// <param name="gespeicherteVersion"><c>null</c>, wenn noch kein Profil gespeichert ist.</param>
    public static ProfilPruefung Pruefen(
        SigniertesProfil angebot, IReadOnlyList<Profilschluessel> eingebaut, uint? gespeicherteVersion, DateTimeOffset jetzt)
    {
        ArgumentNullException.ThrowIfNull(angebot);
        ArgumentNullException.ThrowIfNull(eingebaut);

        var signiert = false;
        foreach (var schluessel in eingebaut)
        {
            if (!schluessel.Prueft(angebot)) continue;
            signiert = true;
            break;
        }
        if (!signiert) return new ProfilPruefung(ProfilUrteil.SignaturUngueltig, null, false);
        if (!Serverprofil.TryLesen(angebot.Daten, out var profil)) return new ProfilPruefung(ProfilUrteil.InhaltUngueltig, null, false);
        var urteil = gespeicherteVersion is { } alt && profil.ProfilVersion <= alt ? ProfilUrteil.NichtNeuer : ProfilUrteil.Uebernehmen;
        return new ProfilPruefung(urteil, profil, profil.IstAbgelaufen(jetzt));
    }
}

/// <summary>
/// Pin-Fehler beim Wiederverbinden (7.1 Schritt 4, P-14): zuerst "falscher Host", die Adresse 60 s meiden,
/// keine Alarmanzeige. Erst wenn 2 min lang an keiner Adresse ein gültiger Pin kam, gilt der Server als nicht
/// vertrauenswürdig. Für die Erstverbindung gilt das nicht (4.1: dort sofort Abbruch mit Anzeige).
/// </summary>
public sealed class PinWaechter(TimeProvider zeit)
{
    /// <summary>So lange wird eine Adresse nach einem Pin-Fehler gemieden.</summary>
    public static readonly TimeSpan Meiden = TimeSpan.FromSeconds(60);

    /// <summary>So lange ohne gültigen Pin, bis der Alarm kommt.</summary>
    public static readonly TimeSpan AlarmNach = TimeSpan.FromMinutes(2);

    private readonly Dictionary<string, DateTimeOffset> _gemiedenBis = new(StringComparer.Ordinal);
    private DateTimeOffset? _ersterFehler;

    /// <summary>Meldet einen Pin-Fehler an <paramref name="adresse"/> (IP:Port). Liefert, ob jetzt Alarm gilt.</summary>
    public bool PinFehler(string adresse)
    {
        ArgumentNullException.ThrowIfNull(adresse);
        var jetzt = zeit.GetUtcNow();
        _ersterFehler ??= jetzt;
        _gemiedenBis[adresse] = jetzt + Meiden;
        return Alarm;
    }

    /// <summary>Ein gültiger Pin setzt alles zurück.</summary>
    public void PinGueltig()
    {
        _ersterFehler = null;
        _gemiedenBis.Clear();
    }

    /// <summary>Ob die Adresse gerade zu meiden ist.</summary>
    public bool IstGemieden(string adresse) =>
        _gemiedenBis.TryGetValue(adresse, out var bis) && zeit.GetUtcNow() < bis;

    /// <summary>"Server nicht vertrauenswürdig" anzeigen (ohne Ausnahmeknopf).</summary>
    public bool Alarm => _ersterFehler is { } erster && zeit.GetUtcNow() - erster >= AlarmNach;
}
