// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>Gemeinsame Hilfen der Steuerkanal-Tests.</summary>
internal static class SteuerHilfe
{
    // Testschlüssel aus 8.3 (Epoche 0).
    public static readonly byte[] KHoch = Convert.FromHexString("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
    public static readonly byte[] IvHoch = Convert.FromHexString("a0a1a2a3a4a5a6a7a8a9aaab");
    public static readonly byte[] KRunter = Convert.FromHexString("202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f");
    public static readonly byte[] IvRunter = Convert.FromHexString("b0b1b2b3b4b5b6b7b8b9babb");

    /// <summary>TV-A aus 8.3: SPRACHE_HOCH der Sitzung 0x1a2b3c4d (53 Byte).</summary>
    public static readonly byte[] TvA = Convert.FromHexString(
        "011000001a2b3c4d0000000000000005cefa04952a75d72b9f0fa81e8f9a6f33fadd803ce6f3a15ea103cbea77c18b354dd540186d");

    /// <summary>TV-B aus 8.3: SPRACHE_RUNTER an Sitzung 0x5e6f7081 (55 Byte).</summary>
    public static readonly byte[] TvB = Convert.FromHexString(
        "011100005e6f7081000000000000002a9c7690b94867debba4d41624c65308a12e168a1c633470cb5c50af7c7cbb3338fd7167f180cdb9");

    public const uint SitzungA = 0x1a2b3c4d;
    public const uint SitzungB = 0x5e6f7081;

    public static RahmenKopf Kopf(byte[] rahmen, int max = Rahmengrenzen.Aktiv)
    {
        var f = Rahmen.KopfLesen(rahmen, max, out var kopf);
        if (f is not null) throw new PruefungException($"Kopf ungültig: {f}");
        return kopf;
    }

    public static Eingang AnServer(ServerKanal k, byte[] rahmen, long jetztMs = 0) =>
        k.Empfangen(Kopf(rahmen, k.Rahmengrenze), rahmen.AsSpan(Rahmen.KopfLaenge), jetztMs);

    public static Eingang AnClient(ClientKanal k, byte[] rahmen, long jetztMs = 0) =>
        k.Empfangen(Kopf(rahmen), rahmen.AsSpan(Rahmen.KopfLaenge), jetztMs);

    public static byte[] Json<T>(Nachrichtentyp typ, T body, uint nr = 0, byte flags = 0) => Rahmen.BauenJson(typ, flags, nr, body);

    /// <summary>Server-Kanal im gewünschten Zustand, auf dem echten Weg hingeführt.</summary>
    public static ServerKanal ServerIn(ServerZustand ziel, long t = 0)
    {
        var k = new ServerKanal(t);
        if (ziel == ServerZustand.Tls) return k;
        k.Automat.TlsFertig(true, true, t);
        switch (ziel)
        {
            case ServerZustand.WarteHello:
                return k;
            case ServerZustand.Registrierung:
                k.Automat.HelloVerarbeitet(Einstufung.Unbekannt, false, t);
                return k;
            case ServerZustand.Warteraum:
                k.Automat.HelloVerarbeitet(Einstufung.Wartet, false, t);
                return k;
        }
        k.Automat.HelloVerarbeitet(Einstufung.Freigegeben, false, t);
        k.SitzungsId = SitzungA;
        var nr = k.VoiceKeysStellen(0, t);
        if (ziel == ServerZustand.AktivSync) return k;
        var e = AnServer(k, Rahmen.Bauen((ushort)Nachrichtentyp.VoiceKeys, Rahmen.FlagAntwort, nr, [0]), t);
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "VoiceKeys-Antwort");
        Pruefe.Gleich(ServerZustand.Aktiv, k.Automat.Zustand, "nach VoiceKeys-Antwort");
        if (ziel == ServerZustand.Aktiv) return k;
        k.Automat.Beenden(t);
        return k;
    }

    /// <summary>Welcome{FREIGEGEBEN} mit allen Pflichtfeldern (Profil aus 8.11).</summary>
    public static Welcome WelcomeFreigegeben(uint sitzung = SitzungB) => new()
    {
        Minor = 0,
        Status = WelcomeStatus.Freigegeben,
        SitzungsId = sitzung,
        Konto = new KontoAngabe { Id = 1001, Name = "Test", Benutzername = "test", Stufe = Stufe.Admin, Rechte = [Recht.GlobalSenden, Recht.Zuweisen] },
        Server = new ServerAngabe { Version = "0.1.0" },
        Udp = new UdpAngabe { Port = 47443, MaxDatagramm = 1200 },
        ServerZeit = 1_790_000_000_000,
        MindestClientVersion = "0.1.0",
        Profil = new SigniertesProfil
        {
            Daten = Convert.ToBase64String("{\"name\":\"Test\",\"profilVersion\":2}"u8),
            Signatur = "iuYaeRFcezj4J9nxB6qE0x641nerMRj6fyC2qE1Irf5BrzMTBR88QTc5HzFPClyAMlu0qK+zdozrNJ9wBAQgaw==",
        },
        ZustandsVersion = 1,
        Grenzen = WelcomeGrenzen.Standard,
    };

    /// <summary>Client-Kanal im gewünschten Zustand, auf dem echten Weg hingeführt.</summary>
    public static ClientKanal ClientIn(ClientZustand ziel, long t = 0)
    {
        var k = new ClientKanal();
        if (ziel == ClientZustand.Getrennt) return k;
        k.Verbinden(t);
        switch (ziel)
        {
            case ClientZustand.Verbinden:
                return k;
            case ClientZustand.Registrierung:
                AnClient(k, Json(Nachrichtentyp.Welcome, Welcome.Unbekannt(0)), t);
                return k;
            case ClientZustand.Warteraum:
                AnClient(k, Json(Nachrichtentyp.Welcome, new Welcome
                {
                    Minor = 0,
                    Status = WelcomeStatus.Wartet,
                    Konto = new KontoAngabe { Id = 1005, Name = "E", Benutzername = "e" },
                    Geraet = new GeraetAngabe { Id = 7, Name = "PC", Anzeige = "5CD2-52FB-0CE8-9324" },
                }), t);
                return k;
        }
        AnClient(k, Json(Nachrichtentyp.Welcome, WelcomeFreigegeben()), t);
        if (ziel == ClientZustand.Sync) return k;
        AnClient(k, Json(Nachrichtentyp.Zustand, new ZustandsVersionBody { ZustandsVersion = 1 }), t);
        if (ziel == ClientZustand.AktivUdpBinden) return k;
        k.Automat.UdpGebunden(t);
        if (ziel == ClientZustand.AktivUdp) return k;
        k.Automat.UdpAusfall(t);
        return k;
    }

    /// <summary>Nur-Schreib-Strom, der jeden Write-Aufruf zählt (P-01: ein Write je Rahmen).</summary>
    public sealed class ZaehlStrom : Stream
    {
        private readonly MemoryStream _inhalt = new();

        public int Schreibaufrufe { get; private set; }

        public byte[] Inhalt => _inhalt.ToArray();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inhalt.Length;
        public override long Position { get => _inhalt.Length; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Schreibaufrufe++;
            _inhalt.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inhalt.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Pool, der jede Anforderung mitschreibt (Puffer erst nach der Längenprüfung).</summary>
    public sealed class ZaehlPool : System.Buffers.ArrayPool<byte>
    {
        public List<int> Anforderungen { get; } = [];

        public override byte[] Rent(int minimumLength)
        {
            Anforderungen.Add(minimumLength);
            return new byte[minimumLength];
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            if (clearArray) Array.Clear(array);
        }
    }
}
