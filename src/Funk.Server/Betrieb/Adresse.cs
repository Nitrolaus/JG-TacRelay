// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Funk.Server.Betrieb;

/// <summary>
/// IP-Adresse mit Port als Wertetyp, allokationsfrei aus einer <see cref="SocketAddress"/> gelesen. IPv4-Adressen in
/// IPv6-Form (<c>::ffff:a.b.c.d</c>) werden auf IPv4 zurückgeführt (Abschnitt 0), damit Vergleiche und Zählungen
/// nicht an der Schreibweise hängen. IPv4 steht in den unteren 32 Bit von <see cref="Ip"/>.
/// </summary>
public readonly record struct Adresse(UInt128 Ip, ushort Port, bool V6)
{
    private static readonly UInt128 V4Abgebildet = (UInt128)0xFFFF << 32;
    private static readonly UInt128 V4Maske = ((UInt128)0xFFFF << 32) | 0xFFFF_FFFF;
    private static readonly UInt128 Obere64 = UInt128.MaxValue << 64;

    /// <summary>Präfix nach Abschnitt 0: IPv4 die einzelne Adresse, IPv6 das /64.</summary>
    public Praefix Praefix => new(V6 ? Ip & Obere64 : Ip, V6);

    /// <summary>Nur die IP, ohne Port.</summary>
    public Adresse OhnePort => this with { Port = 0 };

    /// <summary>Liest Familie, Port und IP aus einer Socket-Adresse (sockaddr_in bzw. sockaddr_in6), ohne zu allokieren.</summary>
    public static bool TryAus(SocketAddress quelle, out Adresse adresse)
    {
        ArgumentNullException.ThrowIfNull(quelle);
        adresse = default;
        var port = (ushort)((quelle[2] << 8) | quelle[3]);
        switch (quelle.Family)
        {
            case AddressFamily.InterNetwork when quelle.Size >= 8:
                adresse = new Adresse((uint)((quelle[4] << 24) | (quelle[5] << 16) | (quelle[6] << 8) | quelle[7]), port, false);
                return true;
            case AddressFamily.InterNetworkV6 when quelle.Size >= 24:
                UInt128 ip = 0;
                for (var i = 8; i < 24; i++) ip = (ip << 8) | quelle[i];
                adresse = Normalisieren(ip, port);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Aus einem Endpunkt, etwa der Gegenstelle einer TCP-Verbindung.</summary>
    public static Adresse Aus(IPEndPoint endpunkt)
    {
        ArgumentNullException.ThrowIfNull(endpunkt);
        return Aus(endpunkt.Address, (ushort)endpunkt.Port);
    }

    /// <summary>Aus IP und Port.</summary>
    public static Adresse Aus(IPAddress ip, ushort port)
    {
        ArgumentNullException.ThrowIfNull(ip);
        Span<byte> roh = stackalloc byte[16];
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            ip.TryWriteBytes(roh, out _);
            return new Adresse(BinaryPrimitives.ReadUInt32BigEndian(roh), port, false);
        }
        ip.TryWriteBytes(roh, out _);
        return Normalisieren(BinaryPrimitives.ReadUInt128BigEndian(roh), port);
    }

    private static Adresse Normalisieren(UInt128 ip, ushort port) =>
        (ip & ~(UInt128)0xFFFF_FFFF) == V4Abgebildet && (ip & V4Maske) == ip
            ? new Adresse(ip & 0xFFFF_FFFF, port, false)
            : new Adresse(ip, port, true);

    /// <summary>Die IP als <see cref="IPAddress"/> (allokiert; nur für Meldungen und Logbuch).</summary>
    public IPAddress IpAdresse()
    {
        Span<byte> roh = stackalloc byte[16];
        if (!V6)
        {
            BinaryPrimitives.WriteUInt32BigEndian(roh, (uint)Ip);
            return new IPAddress(roh[..4]);
        }
        BinaryPrimitives.WriteUInt128BigEndian(roh, Ip);
        return new IPAddress(roh);
    }

    /// <summary>"IP:Port" bzw. "[IPv6]:Port", wie <c>UdpStatus.adresse</c> es zeigt.</summary>
    public string AlsText() => V6
        ? string.Create(CultureInfo.InvariantCulture, $"[{IpAdresse()}]:{Port}")
        : string.Create(CultureInfo.InvariantCulture, $"{IpAdresse()}:{Port}");

    /// <summary>Verbirgt die Adresse; Klartext nur über <see cref="AlsText"/> (P-46).</summary>
    public override string ToString() => V6 ? "Adresse(v6, ***)" : "Adresse(v4, ***)";
}

/// <summary>Präfix einer Quelle nach Abschnitt 0 (IPv4-Adresse oder IPv6-/64), Schlüssel aller Grenzen "je Quelle".</summary>
public readonly record struct Praefix(UInt128 Wert, bool V6)
{
    /// <summary>16 Byte für HMAC-Werte (Vorrangspur, Schwärzung).</summary>
    public void Schreiben(Span<byte> ziel)
    {
        BinaryPrimitives.WriteUInt128BigEndian(ziel, Wert);
        if (!V6) ziel[0] = 0x04;
    }

    public override string ToString() => V6 ? "Praefix(v6/64, ***)" : "Praefix(v4, ***)";
}
