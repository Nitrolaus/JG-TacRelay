// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Protokoll.Schutz;

namespace Funk.Tests;

internal static class SchutzTests
{
    private static readonly byte[] Iv = Convert.FromHexString("000102030405060708090A0B");

    [Test]
    public static void Nonce_ZaehlerNull_GleichIv()
    {
        Span<byte> n = stackalloc byte[Nonce.Laenge];
        Nonce.Bilden(Iv, 0, n);
        Pruefe.BytesGleich(Iv, n, "Zähler 0");
    }

    [Test]
    public static void Nonce_ZaehlerEins_NurLetztesByte()
    {
        Span<byte> n = stackalloc byte[Nonce.Laenge];
        Nonce.Bilden(Iv, 1, n);
        Pruefe.BytesGleich(Convert.FromHexString("000102030405060708090A0A"), n, "Zähler 1");
    }

    [Test]
    public static void Nonce_VollerZaehler_BigEndianRechtsbuendig()
    {
        Span<byte> n = stackalloc byte[Nonce.Laenge];
        Nonce.Bilden(Iv, 0x0102030405060708UL, n);
        // Bytes 4..11: 04^01 05^02 06^03 07^04 08^05 09^06 0A^07 0B^08
        Pruefe.BytesGleich(Convert.FromHexString("00010203050705030D0F0D03"), n, "Zähler 0x0102030405060708");
    }

    [Test]
    public static void Nonce_FalscheLaengen_Werfen()
    {
        Pruefe.Wirft<ArgumentException>(() => Nonce.Bilden(new byte[11], 0, new byte[12]), "IV zu kurz");
        Pruefe.Wirft<ArgumentException>(() => Nonce.Bilden(Iv, 0, new byte[13]), "Ziel zu lang");
    }

    [Test]
    public static void Replay_ErstesPaket_IstNeu()
    {
        var f = new ReplayFenster(128);
        Pruefe.Wahr(f.IstNeu(0), "0 neu");
        Pruefe.Wahr(f.Annehmen(0), "0 annehmen");
        Pruefe.Falsch(f.IstNeu(0), "0 doppelt");
    }

    [Test]
    public static void Replay_Doppelt_WirdAbgelehnt()
    {
        var f = new ReplayFenster();
        foreach (var z in new ulong[] { 5, 7, 6, 10 }) { Pruefe.Wahr(f.IstNeu(z), $"{z} neu"); Pruefe.Wahr(f.Annehmen(z), $"{z} annehmen"); }
        foreach (var z in new ulong[] { 5, 6, 7, 10 }) Pruefe.Falsch(f.IstNeu(z), $"{z} doppelt");
        Pruefe.Wahr(f.IstNeu(8), "8 Lücke");
        Pruefe.Wahr(f.IstNeu(9), "9 Lücke");
    }

    [Test]
    public static void Replay_ZuAlt_WirdAbgelehnt()
    {
        // Der Konstruktor nimmt seit dem Abgleich mit 5.9/8.7 die Breite W selbst, nicht die Maskengröße.
        var f = new ReplayFenster(64);
        f.Annehmen(1000);
        Pruefe.Gleich(64UL, f.Breite, "wirksame Breite");
        Pruefe.Wahr(f.IstNeu(1000 - 63), "am Rand noch drin");
        Pruefe.Falsch(f.IstNeu(1000 - 64), "genau Breite zu alt");
        Pruefe.Falsch(f.IstNeu(0), "weit zu alt");
    }

    [Test]
    public static void Replay_GrosserSprung_LoeschtAlteBits()
    {
        var f = new ReplayFenster(128);
        for (ulong z = 0; z < 60; z++) f.Annehmen(z);
        f.Annehmen(1_000_000);
        // Nach dem Sprung darf ein Zähler, der zufällig auf ein altes Bit fällt, nicht als gesehen gelten.
        var imFenster = 1_000_000UL - 10;
        Pruefe.Wahr(f.IstNeu(imFenster), "altes Bit wurde gelöscht");
    }

    [Test]
    public static void Replay_Umordnung_ImFenster()
    {
        var f = new ReplayFenster(2048);
        var rnd = new Random(42);
        var zaehler = Enumerable.Range(0, 1500).Select(i => (ulong)i).OrderBy(_ => rnd.Next()).ToArray();
        var hoechster = 0UL;
        foreach (var z in zaehler)
        {
            hoechster = Math.Max(hoechster, z);
            if (hoechster - z >= f.Breite) continue; // zu alt, darf abgelehnt werden
            Pruefe.Wahr(f.IstNeu(z), $"{z} neu trotz Umordnung");
            Pruefe.Wahr(f.Annehmen(z), $"{z} annehmen");
        }
        foreach (var z in zaehler.Where(z => hoechster - z < f.Breite))
            Pruefe.Falsch(f.IstNeu(z), $"{z} doppelt nach Umordnung");
    }

    [Test]
    public static void Replay_AnnehmenAusserhalb_Abgelehnt()
    {
        var f = new ReplayFenster(128);
        Pruefe.Wahr(f.Annehmen(500), "500 annehmen");
        Pruefe.Falsch(f.Annehmen(1), "Annehmen zu alt");
        Pruefe.Gleich(500UL, f.Hoechster, "Höchstwert unverändert");
    }

    [Test]
    public static void TokenEimer_BremstUndFuelltNach()
    {
        long t = 0;
        var e = new TokenEimer(kapazitaet: 5, proSekunde: 50, jetzt: t);
        for (var i = 0; i < 5; i++) Pruefe.Wahr(e.Nehmen(t), $"Stoß {i}");
        Pruefe.Falsch(e.Nehmen(t), "Eimer leer");
        t += Stopwatch.Frequency / 50; // 20 ms -> eine Marke
        Pruefe.Wahr(e.Nehmen(t), "nach 20 ms eine Marke");
        Pruefe.Falsch(e.Nehmen(t), "danach wieder leer");
        t += Stopwatch.Frequency * 10; // lange Pause
        for (var i = 0; i < 5; i++) Pruefe.Wahr(e.Nehmen(t), $"voll {i}");
        Pruefe.Falsch(e.Nehmen(t), "nicht über Kapazität");
    }
}
