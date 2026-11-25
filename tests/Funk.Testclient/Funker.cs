// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Funk.ClientKern;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.Testclient;

/// <summary>
/// Testrahmen in der Opus-Nutzlast: TOC 0xF8 (CELT FB 20 ms, mono, ein Frame, nach 5.13 gültig), dann Kennung,
/// Lauf, Sprecher, laufende Nummer und Sendezeit (Stopwatch-Takte), Rest Füllbytes.
/// <code>
/// 0: 0xF8   1: 'L'   2: Lauf   3-4: Sprecher u16   5-8: Nummer u32   9-16: Sendetakte i64   17..: 0xA5
/// </code>
/// </summary>
internal static class Testrahmen
{
    public const int MinLaenge = 17;

    public static void Schreiben(Span<byte> ziel, byte lauf, ushort sprecher, uint nummer, long takte)
    {
        ziel[0] = 0xF8;
        ziel[1] = (byte)'L';
        ziel[2] = lauf;
        BinaryPrimitives.WriteUInt16BigEndian(ziel[3..], sprecher);
        BinaryPrimitives.WriteUInt32BigEndian(ziel[5..], nummer);
        BinaryPrimitives.WriteInt64BigEndian(ziel[9..], takte);
        ziel[MinLaenge..].Fill(0xA5);
    }

    public static bool Lesen(ReadOnlySpan<byte> opus, out byte lauf, out ushort sprecher, out uint nummer, out long takte)
    {
        lauf = 0;
        sprecher = 0;
        nummer = 0;
        takte = 0;
        if (opus.Length < MinLaenge || opus[0] != 0xF8 || opus[1] != (byte)'L') return false;
        lauf = opus[2];
        sprecher = BinaryPrimitives.ReadUInt16BigEndian(opus[3..]);
        nummer = BinaryPrimitives.ReadUInt32BigEndian(opus[5..]);
        takte = BinaryPrimitives.ReadInt64BigEndian(opus[9..]);
        return true;
    }
}

/// <summary>
/// Statistik eines Empfängers für einen Lauf: Pakete je Sprecher, Bytes (Datagramm wie auf dem Draht), Laufzeit
/// Mund-Server-Ohr aus dem Zeitstempel im Testrahmen, Jitter nach RFC 3550 je Strom mit der echten Sendezeit als S
/// (Transportjitter ohne den Taktgeber des Testclients).
/// </summary>
internal sealed class Empfangsstatistik(int funker, int laufzeitProben)
{
    private readonly Lock _sperre = new();
    private readonly long[] _anzahl = new long[funker];
    private readonly uint[] _letzteNummer = new uint[funker];
    private readonly long[] _letzteSende = new long[funker];
    private readonly long[] _letzteAnkunft = new long[funker];
    private readonly double[] _jitter = new double[funker];
    private readonly bool[] _gesehen = new bool[funker];
    private readonly List<float> _laufzeit = new(laufzeitProben);

    public byte Lauf { get; set; }
    public long Pakete { get; private set; }
    public long Bytes { get; private set; }
    public long Fremd { get; private set; }
    public long Tunnel { get; private set; }

    public void Zuruecksetzen(byte lauf)
    {
        lock (_sperre)
        {
            Lauf = lauf;
            Array.Clear(_anzahl);
            Array.Clear(_jitter);
            Array.Clear(_gesehen);
            _laufzeit.Clear();
            Pakete = 0;
            Bytes = 0;
            Fremd = 0;
            Tunnel = 0;
        }
    }

    public void Paket(in SprachEmpfang e)
    {
        var jetzt = e.EmpfangenTakte;
        if (e.Paket.IstEnde) return;
        if (!Testrahmen.Lesen(e.Paket.Opus, out var lauf, out var sprecher, out var nummer, out var gesendet)) return;
        lock (_sperre)
        {
            if (lauf != Lauf || sprecher >= _anzahl.Length)
            {
                Fremd++;
                return;
            }
            Pakete++;
            // Datagramm auf dem Draht: Kopf 16 + fester Teil 13 + 2k Netze + Opus + Tag 16 (5.5).
            Bytes += 45 + 2 * e.Paket.NetzAnzahl + e.Paket.Opus.Length;
            if (e.Weg == Verbindungsweg.Tunnel) Tunnel++;
            _anzahl[sprecher]++;
            if (_laufzeit.Count < _laufzeit.Capacity) _laufzeit.Add((float)((jetzt - gesendet) * 1000.0 / Stopwatch.Frequency));
            if (_gesehen[sprecher] && nummer == _letzteNummer[sprecher] + 1)
            {
                var d = (jetzt - _letzteAnkunft[sprecher] - (gesendet - _letzteSende[sprecher])) * 1000.0 / Stopwatch.Frequency;
                _jitter[sprecher] += (Math.Abs(d) - _jitter[sprecher]) / 16.0;
            }
            if (!_gesehen[sprecher] || nummer > _letzteNummer[sprecher])
            {
                _letzteNummer[sprecher] = nummer;
                _letzteSende[sprecher] = gesendet;
                _letzteAnkunft[sprecher] = jetzt;
            }
            _gesehen[sprecher] = true;
        }
    }

    public long Anzahl(int sprecher)
    {
        lock (_sperre) return _anzahl[sprecher];
    }

    public double Jitter(int sprecher)
    {
        lock (_sperre) return _jitter[sprecher];
    }

    public float[] Laufzeiten()
    {
        lock (_sperre) return [.. _laufzeit];
    }
}

/// <summary>Ein simulierter Funker: Geräteschlüssel nur im Speicher, eigene Loopback-Adresse, Client aus Funk.ClientKern.</summary>
internal sealed class Funker : ISprachEmpfaenger, IAsyncDisposable
{
    private readonly TaskCompletionSource<Registrierungscode> _code = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Funker(int index, int anzahl, IVerbindungsaufbau aufbau, Geraeteidentitaet identitaet)
    {
        Index = index;
        Identitaet = identitaet;
        Statistik = new Empfangsstatistik(anzahl, 3_000_000 / Math.Max(1, anzahl) + 60_000);
        Client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = aufbau,
            Identitaet = identitaet,
            ClientName = "funk-testclient",
            ClientVersion = "1.0.0",
            Geraetename = "Last " + index.ToString(CultureInfo.InvariantCulture),
            Rueckfrage = (_, _) => Task.FromResult(true),
            Empfaenger = this,
        });
        Client.CodeErhalten += (_, c) => _code.TrySetResult(c);
    }

    public int Index { get; }
    public Geraeteidentitaet Identitaet { get; }
    public FunkClient Client { get; }
    public Empfangsstatistik Statistik { get; }
    public string Discord => (300_000_000_000_000_000UL + (ulong)Index).ToString(CultureInfo.InvariantCulture);
    public string Benutzer => "funker" + Index.ToString("D2", CultureInfo.InvariantCulture);

    /// <summary>Eigene Adresse 127.0.x.y: jedes simulierte Gerät hat ein eigenes Präfix (Grenzen je Präfix, 4.8).</summary>
    public static IPAddress Adresse(int index) => new([127, 0, (byte)(20 + index / 250), (byte)(2 + index % 250)]);

    public Task<Registrierungscode> CodeAsync(TimeSpan frist) => _code.Task.WaitAsync(frist);

    public long Gesendet;

    public void Empfangen(in SprachEmpfang empfang) => Statistik.Paket(empfang);

    public void ZeitlinieBeendet(uint absender, ushort sprechphase)
    {
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync().ConfigureAwait(false);
        Identitaet.Dispose();
    }
}
