// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Net.Sockets;
using Funk.Protokoll.Sprache;
using L = Funk.Server.Lage;
using S = Funk.Protokoll.Steuerung;

namespace Funk.Server.Sprachpfad;

/// <summary>
/// Zähler einer Sitzung, die der Relay-Thread schreibt und die Verbindung für die Ping-Antwort liest (4.5 Ping:
/// <c>udpEmpfangen</c>, <c>udpGesendet</c>, 5.12). Threadsicher über <see cref="Interlocked"/>.
/// </summary>
public sealed class SitzungsZaehler
{
    private long _udpEmpfangen;
    private long _udpGesendet;

    public long UdpEmpfangen => Interlocked.Read(ref _udpEmpfangen);

    public long UdpGesendet => Interlocked.Read(ref _udpGesendet);

    internal void Empfangen() => Interlocked.Increment(ref _udpEmpfangen);

    internal void Gesendet() => Interlocked.Increment(ref _udpGesendet);
}

/// <summary>
/// Was der Relay-Thread an den Steuerkanal weiterreicht. Die Umsetzung stellt die Nachrichten der Verbindung der
/// jeweiligen Sitzung zu; sie darf den Relay-Thread nicht blockieren.
/// </summary>
public interface IRelaisSenke
{
    /// <summary><c>UdpStatus</c> (4.5) an die Sitzung.</summary>
    void UdpStatus(uint sitzung, S.UdpZustand zustand, string? adresse, bool? gleicheIp);

    /// <summary>VoiceKeys ROTATION (5.8) stellen. Die Senke übernimmt <paramref name="material"/> (88 Byte) und nullt es.</summary>
    void VoiceKeys(uint sitzung, byte epoche, byte[] material);

    /// <summary>Sitzung beenden (keine Antwort auf VoiceKeys, Protokollfehler im Sprachkanal).</summary>
    void Trennen(uint sitzung, S.Fehlercode fehler);

    /// <summary>SpracheTunnel-Rahmen runter (5.12); <paramref name="datagramm"/> gehört danach der Senke.</summary>
    void Tunnel(uint sitzung, uint absender, byte[] datagramm);

    void DurchsageStart(uint empfaenger, uint sprecher, L.Stempelart art, ReadOnlySpan<ushort> netze, ushort sprechphase);

    void DurchsageEnde(uint empfaenger, uint sprecher, ushort sprechphase);

    void SendeFehler(uint sitzung, ReadOnlySpan<L.SendeFehlerEintrag> fehler, bool teilweise, uint verworfen);

    /// <summary>Meldung RATE an alle mit ZUWEISEN (6.3).</summary>
    void Rate(uint sitzung, uint kontoId);

    /// <summary>Meldung KOPIERVERDACHT an alle mit GERAETE_FREIGEBEN (5.10).</summary>
    void Kopierverdacht(uint sitzung, uint kontoId);

    /// <summary><c>VerlustHinweis</c> an einen Sender (4.5).</summary>
    void VerlustHinweis(uint sitzung, byte prozent);

    /// <summary><c>Meldung{EIGENER_VERLUST}</c> an den Betroffenen (7.8).</summary>
    void EigenerVerlust(uint sitzung, string richtung);

    /// <summary>Überlaststufe hat sich geändert (7.4).</summary>
    void Last(int stufe, uint verworfen, double ausgangMbit);

    /// <summary>Wer gerade spricht (Sprechanzeige 0x0053 an Abonnenten), nur bei Änderung und höchstens 5 je Sekunde.</summary>
    void Sprechanzeige(IReadOnlyList<Sprecher> aktiv);
}

/// <summary>Ein Eintrag der Sprechanzeige: Konto, Art und Zielnetze der laufenden Sprechphase.</summary>
public sealed record Sprecher(uint KontoId, S.SprechArt Art, IReadOnlyList<ushort> Netze);

/// <summary>Ein empfangenes Datagramm oder ein SpracheTunnel-Body auf dem Weg zum Relay-Thread; aus einem Vorrat.</summary>
internal sealed class Empfangspaket
{
    public readonly byte[] Puffer = new byte[Sprachkanal.EmpfangspufferLaenge];
    public int Laenge;
    public long Empfangen;
    public SocketAddress? Quelle;
    public int Socket;
    public Weg Weg;
    public uint Tragende;
    public bool Abgeschnitten;
}

/// <summary>Leistungswerte des Relais aus der Konfiguration.</summary>
public sealed record RelaisOptionen
{
    public double AusgangMbit { get; init; } = 35;
    public int LeitungsZuschlag { get; init; } = 26;
    public int Kapazitaet { get; init; } = 4096;
    public Rotationsgrenzen Rotation { get; init; } = Rotationsgrenzen.Standard;
}

/// <summary>Messwerte einer Sitzung nach 7.8, als Momentaufnahme des letzten 10-s-Fensters.</summary>
public sealed record SitzungsMesswerte(
    uint SitzungsId,
    uint KontoId,
    string Weg,
    bool Gebunden,
    long HochEmpfangen,
    long HochVerloren,
    long HochUmsortiert,
    long HochDoppelt,
    double JitterMs,
    long RunterGesendet,
    double TunnelAnteil,
    long Pfadwechsel,
    long Kopierverdacht,
    double? RttMs,
    double? RunterVerlustProzent,
    string Guete);

/// <summary>Messwerte des Relais insgesamt (7.4, 7.8).</summary>
public sealed record RelaisMesswerte(
    int Sitzungen,
    long Verarbeitet,
    long VerworfenVoll,
    long VerworfenPruefung,
    long AusgangGesendet,
    long AusgangAltersverwurf,
    double AusgangMbitGemessen,
    double VerarbeitungMsP50,
    double VerarbeitungMsP99,
    int Laststufe,
    IReadOnlyList<SitzungsMesswerte> JeSitzung);
