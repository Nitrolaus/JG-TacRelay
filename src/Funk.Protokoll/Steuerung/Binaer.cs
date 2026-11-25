// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;

namespace Funk.Protokoll.Steuerung;

/// <summary><c>VoiceKeys.grund</c>.</summary>
public enum VoiceKeysGrund : byte
{
    Start = 0,
    Rotation = 1,
}

/// <summary>Sicht auf einen gelesenen VoiceKeys-Body; die Spans zeigen in den Empfangspuffer.</summary>
public readonly ref struct VoiceKeysAnsicht
{
    internal VoiceKeysAnsicht(ReadOnlySpan<byte> body)
    {
        Epoche = body[0];
        Grund = (VoiceKeysGrund)body[1];
        KHoch = body.Slice(2, 32);
        IvHoch = body.Slice(34, 12);
        KRunter = body.Slice(46, 32);
        IvRunter = body.Slice(78, 12);
    }

    public byte Epoche { get; }
    public VoiceKeysGrund Grund { get; }
    public ReadOnlySpan<byte> KHoch { get; }
    public ReadOnlySpan<byte> IvHoch { get; }
    public ReadOnlySpan<byte> KRunter { get; }
    public ReadOnlySpan<byte> IvRunter { get; }
}

/// <summary>
/// 0x8002 VoiceKeys (4.5): Server-Anfrage mit genau 90 Byte, Client-Antwort mit genau 1 Byte (installierte
/// Epoche). Andere Längen, ein unbekannter Grund oder eine falsche Epoche ergeben <c>PROTOKOLL</c>.
/// Der Aufrufer nullt seine Schlüsselpuffer nach Gebrauch mit <c>CryptographicOperations.ZeroMemory</c>.
/// </summary>
public static class VoiceKeys
{
    public const int BodyLaenge = 90;
    public const int AntwortLaenge = 1;
    public const int SchluesselLaenge = 32;
    public const int IvLaenge = 12;

    /// <summary>Schreibt den 90-Byte-Body.</summary>
    public static void Schreiben(Span<byte> ziel, byte epoche, VoiceKeysGrund grund,
        ReadOnlySpan<byte> kHoch, ReadOnlySpan<byte> ivHoch, ReadOnlySpan<byte> kRunter, ReadOnlySpan<byte> ivRunter)
    {
        if (ziel.Length < BodyLaenge) throw new ArgumentException("Ziel zu kurz.", nameof(ziel));
        if (kHoch.Length != SchluesselLaenge || kRunter.Length != SchluesselLaenge) throw new ArgumentException("Schlüssel müssen 32 Byte lang sein.");
        if (ivHoch.Length != IvLaenge || ivRunter.Length != IvLaenge) throw new ArgumentException("IVs müssen 12 Byte lang sein.");
        ziel[0] = epoche;
        ziel[1] = (byte)grund;
        kHoch.CopyTo(ziel[2..]);
        ivHoch.CopyTo(ziel[34..]);
        kRunter.CopyTo(ziel[46..]);
        ivRunter.CopyTo(ziel[78..]);
    }

    /// <summary>Prüft einen empfangenen Anfrage-Body. <c>null</c> heißt gültig.</summary>
    public static Fehlercode? AnfragePruefen(ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLaenge) return Fehlercode.Protokoll;
        if (body[1] > (byte)VoiceKeysGrund.Rotation) return Fehlercode.Protokoll;
        return null;
    }

    /// <summary>Liest einen geprüften Anfrage-Body.</summary>
    public static VoiceKeysAnsicht Lesen(ReadOnlySpan<byte> body)
    {
        if (AnfragePruefen(body) is not null) throw new ArgumentException("Kein gültiger VoiceKeys-Body.", nameof(body));
        return new VoiceKeysAnsicht(body);
    }

    /// <summary>Prüft die Client-Antwort gegen die angefragte Epoche.</summary>
    public static Fehlercode? AntwortPruefen(ReadOnlySpan<byte> body, byte erwarteteEpoche) =>
        body.Length == AntwortLaenge && body[0] == erwarteteEpoche ? null : Fehlercode.Protokoll;
}

/// <summary>Ergebnis der Vorprüfung eines SpracheTunnel-Rahmens.</summary>
public enum TunnelPruefung : byte
{
    /// <summary>Weiter an die Empfangsprüfung des Sprachkanals (5.9 ab Schritt 4).</summary>
    Weiter,
    /// <summary>Still verwerfen (5.9 Schritte 1 und 2).</summary>
    Verwerfen,
    /// <summary>Fremde Sitzungs-ID: <c>PROTOKOLL</c> und Trennung (4.5, 5.9 Schritt 3).</summary>
    Protokoll,
}

/// <summary>
/// 0x8001 SpracheTunnel (4.5): Der Body ist genau ein Sprachdatagramm nach Abschnitt 5. Hier nur die
/// Schritte 1-3 der Empfangsprüfung 5.9, soweit sie ohne Schlüssel gehen; alles Weitere macht der Sprachkanal.
/// </summary>
public static class SpracheTunnel
{
    private const int KopfLaenge = 16;

    /// <summary>
    /// Prüft Länge, Kopf und Sitzungs-ID. <paramref name="hoch"/>: Rahmen kommt vom Client (Server prüft);
    /// sonst vom Server (Client prüft). <paramref name="sitzungsId"/> ist die Sitzung der tragenden Verbindung.
    /// </summary>
    public static TunnelPruefung Pruefen(ReadOnlySpan<byte> body, uint sitzungsId, bool hoch)
    {
        // Schritt 1: Länge.
        if (body.Length < 41 || body.Length > 1200) return TunnelPruefung.Verwerfen;
        // Schritt 2: Version, Typ in dieser Richtung, reserviert, Länge passt zum Typ.
        if (body[0] != 0x01 || body[3] != 0x00) return TunnelPruefung.Verwerfen;
        var passt = (hoch, body[1]) switch
        {
            (true, 0x01 or 0x03 or 0x04) => body.Length == 48,
            (true, 0x10) => body.Length is >= 41 and <= 569,
            (false, 0x02 or 0x05) => body.Length == 48,
            (false, 0x11) => body.Length is >= 45 and <= 477,
            _ => false,
        };
        if (!passt) return TunnelPruefung.Verwerfen;
        // Schritt 3: Sitzungs-ID muss die der tragenden TLS-Verbindung sein.
        return BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4)) == sitzungsId && body.Length >= KopfLaenge
            ? TunnelPruefung.Weiter
            : TunnelPruefung.Protokoll;
    }
}
