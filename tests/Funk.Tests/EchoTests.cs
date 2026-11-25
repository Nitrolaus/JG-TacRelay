// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.ClientKern.Echo;
using Funk.Protokoll.Schutz;
using Funk.Protokoll.Sprache;

namespace Funk.Tests;

/// <summary>
/// Echoband und Ratenbremse des Echo-Funkers mit simulierter Zeit, ohne Netz: Reihenfolge und Raster der Wiedergabe,
/// Pause, Pre-roll, Höchstdauer 30 s, Leerlauf, eine Phase zur Zeit, Schleifenschutz und die Grenzen des Servers (6.3).
/// </summary>
internal static class EchoTests
{
    private const ushort EchoNetz = 2;

    /// <summary>Opus-Nutzlast mit TOC 0xF8 und Lauf und Frame-Nr darin, damit jede Nutzlast eindeutig ist.</summary>
    private static byte[] Opus(byte lauf, uint frame, int laenge = 24)
    {
        var o = new byte[laenge];
        o[0] = 0xF8;
        o[1] = lauf;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(o.AsSpan(2), frame);
        for (var i = 6; i < o.Length; i++) o[i] = 0xA5;
        return o;
    }

    private static EchoPaket Audio(uint absender, ushort phase, uint frame, long ankunft, byte lauf = 1, Kennzeichen k = Kennzeichen.None,
        Spruchart art = Spruchart.Netz, ushort netz = EchoNetz) =>
        new(absender, phase, frame, k, art, netz, Opus(lauf, frame), ankunft);

    private static EchoPaket Ende(uint absender, ushort phase, uint frame, long ankunft) =>
        new(absender, phase, frame, Kennzeichen.Ende, Spruchart.Netz, EchoNetz, [], ankunft);

    /// <summary>Nimmt eine Phase von <paramref name="frames"/> Frames ab <paramref name="erste"/> auf, im 20-ms-Takt ab <paramref name="t"/>.</summary>
    private static long Sprechen(Echoband band, uint absender, ushort phase, uint erste, int frames, long t, byte lauf = 1, bool ende = true)
    {
        for (var i = 0; i < frames; i++, t += 20)
            band.Paket(Audio(absender, phase, erste + (uint)i, t, lauf, i == 0 ? Kennzeichen.Beginn : Kennzeichen.None), t);
        if (ende) band.Paket(Ende(absender, phase, erste + (uint)frames, t), t);
        return t;
    }

    [Test]
    public static void Echoband_PhaseKommtNachDerPauseGeordnetImRasterZurueck()
    {
        var band = new Echoband();
        long t = 1000;
        // VA-Phase: drei Pre-roll-Frames als Stoß, dann 22 Frames im Takt; 110 und 111 kommen vertauscht an.
        for (uint f = 100; f < 103; f++)
            Pruefe.Wahr(band.Paket(Audio(5, 40, f, t, k: Kennzeichen.Va | Kennzeichen.Preroll | (f == 100 ? Kennzeichen.Beginn : 0)), t) is EchoUrteil.Begonnen or EchoUrteil.Aufgenommen, "Pre-roll aufgenommen");
        Pruefe.Gleich(EchoZustand.Aufnahme, band.Zustand, "Aufnahme läuft");
        var folge = Enumerable.Range(103, 22).Select(i => (uint)i).ToList();
        (folge[7], folge[8]) = (folge[8], folge[7]);
        foreach (var f in folge)
        {
            t += 20;
            Pruefe.Gleich(EchoUrteil.Aufgenommen, band.Paket(Audio(5, 40, f, t, k: Kennzeichen.Va), t), "Frame " + f);
        }
        t += 20;
        Pruefe.Gleich(EchoUrteil.Beendet, band.Paket(Ende(5, 40, 125, t), t), "ENDE schließt die Aufnahme");
        Pruefe.Gleich(EchoZustand.Pause, band.Zustand, "Pause nach dem ENDE");
        Pruefe.Gleich(EchoUrteil.Beschaeftigt, band.Paket(Ende(5, 40, 126, t + 20), t + 20), "weitere ENDE-Kopien in der Pause");
        Pruefe.Wahr(band.Takt(t + 499) is null, "vor Ablauf der 500 ms keine Wiedergabe");

        var phase = band.Takt(t + 500);
        Pruefe.Wahr(phase is not null, "nach 500 ms Pause zur Wiedergabe");
        Pruefe.Gleich(EchoZustand.Wiedergabe, band.Zustand, "Wiedergabe läuft");
        Pruefe.Gleich(25, phase!.Frames.Count, "alle 25 Frames");
        Pruefe.Wahr(phase.Va && phase.Netz == EchoNetz && phase.Absender == 5 && !phase.Gekuerzt, "VA, Netz, Absender");
        for (var i = 0; i < 25; i++) Pruefe.BytesGleich(Opus(1, (uint)(100 + i)), phase.Frames[i].Opus, "Reihenfolge nach Frame-Nr, Frame " + i);
        Pruefe.Wahr(phase.Frames.Take(3).All(f => f.Preroll && f.VersatzMs == 0), "Pre-roll als Stoß bei 0");
        Pruefe.Wahr(phase.Frames.Skip(3).All(f => !f.Preroll), "danach kein Pre-roll");
        Pruefe.Gleich(20L, phase.Frames[3].VersatzMs, "erster Frame nach dem Stoß bei 20 ms");
        Pruefe.Gleich((124L - 102) * 20, phase.Frames[24].VersatzMs, "letzter Frame im 20-ms-Raster");

        Pruefe.Gleich(EchoUrteil.Beschaeftigt, band.Paket(Audio(6, 1, 1, t + 600), t + 600), "während der Wiedergabe wird nichts aufgenommen");
        band.WiedergabeBeendet(t + 1100);
        Pruefe.Gleich(EchoZustand.Bereit, band.Zustand, "wieder bereit");
        Pruefe.Gleich(1, band.Wiedergaben, "eine Wiedergabe");
        Pruefe.Gleich(EchoUrteil.Beschaeftigt, band.Paket(Audio(6, 1, 1, t + 1050), t + 1101), "angekommen vor 'bereit': verspätet");
        Pruefe.Gleich(EchoUrteil.Begonnen, band.Paket(Audio(6, 2, 1, t + 1110, lauf: 2, k: Kennzeichen.Beginn), t + 1110), "danach wieder aufnahmebereit");
    }

    [Test]
    public static void Echoband_HoechstensDreissigSekundenJePhase()
    {
        var band = new Echoband();
        Pruefe.Gleich(1500, new EchoOptionen().MaxFrames, "30 s sind 1500 Frames");
        long t = 0;
        var voll = 0;
        for (uint i = 0; i < 1600; i++, t += 20)
            if (band.Paket(Audio(9, 3, 5000 + i, t, k: i == 0 ? Kennzeichen.Beginn : Kennzeichen.None), t) == EchoUrteil.Voll) voll++;
        Pruefe.Gleich(100, voll, "100 Frames über 30 s werden nicht aufgenommen");
        band.Paket(Ende(9, 3, 6600, t), t);
        var phase = band.Takt(t + 500)!;
        Pruefe.Gleich(1500, phase.Frames.Count, "genau 30 s");
        Pruefe.Wahr(phase.Gekuerzt, "als gekürzt markiert");
        Pruefe.BytesGleich(Opus(1, 5000), phase.Frames[0].Opus, "der Anfang bleibt");
        Pruefe.BytesGleich(Opus(1, 6499), phase.Frames[^1].Opus, "Ende bei 30 s");
        Pruefe.Gleich(1499L * 20, phase.Frames[^1].VersatzMs, "Raster bis 29,98 s");
        Pruefe.Gleich(1, band.Gekuerzt, "Zähler gekürzt");

        // Auch ein Frame weit hinter dem Anfang (Sprung) sprengt die 30 s nicht.
        band.WiedergabeBeendet(t + 31_000);
        var t2 = t + 32_100;
        Pruefe.Gleich(EchoUrteil.Begonnen, band.Paket(Audio(9, 4, 100, t2, lauf: 3, k: Kennzeichen.Beginn), t2), "nach dem Nachlauf wieder");
        Pruefe.Gleich(EchoUrteil.Voll, band.Paket(Audio(9, 4, 1600, t2 + 20, lauf: 3), t2 + 20), "Spanne über 1500 Frames");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new EchoOptionen { MaxPhase = TimeSpan.FromSeconds(31) }.Pruefen(), "mehr als 30 s");
    }

    [Test]
    public static void Echoband_LeerlaufBeendetEinePhaseOhneEnde()
    {
        var band = new Echoband();
        var t = Sprechen(band, 3, 7, 10, 10, 0, ende: false);
        var letztes = t - 20;
        Pruefe.Wahr(band.Takt(letztes + 299) is null && band.Zustand == EchoZustand.Aufnahme, "299 ms Ruhe: Aufnahme läuft");
        Pruefe.Wahr(band.Takt(letztes + 300) is null && band.Zustand == EchoZustand.Pause, "300 ms Ruhe: Ende erkannt, Pause");
        var phase = band.Takt(letztes + 800);
        Pruefe.Gleich(10, phase?.Frames.Count ?? 0, "Wiedergabe 500 ms nach dem erkannten Ende");
    }

    [Test]
    public static void Echoband_EinePhaseZurZeitUndNurNetzspracheImEchoNetz()
    {
        var band = new Echoband { EigenesKonto = 77 };
        Pruefe.Gleich(EchoUrteil.Eigen, band.Paket(Audio(77, 1, 1, 0), 0), "der eigene Absender");
        Pruefe.Gleich(EchoUrteil.KeinNetz, band.Paket(Audio(5, 1, 1, 0, art: Spruchart.Durchsage), 0), "Durchsage");
        Pruefe.Gleich(EchoUrteil.KeinNetz, band.Paket(Audio(5, 1, 1, 0, art: Spruchart.Global), 0), "Global");
        Pruefe.Gleich(EchoUrteil.KeinNetz, band.Paket(Audio(5, 1, 1, 0, netz: 0), 0), "nicht im Echo-Netz");
        Pruefe.Gleich(EchoUrteil.OhneAudio, band.Paket(Ende(5, 1, 9, 0), 0), "ENDE ohne Aufnahme");
        Pruefe.Gleich(EchoZustand.Bereit, band.Zustand, "noch bereit");

        Pruefe.Gleich(EchoUrteil.Begonnen, band.Paket(Audio(5, 1, 1, 10, k: Kennzeichen.Beginn), 10), "Sprecher A beginnt");
        Pruefe.Gleich(EchoUrteil.AnderePhase, band.Paket(Audio(6, 1, 1, 20, lauf: 2), 20), "Sprecher B währenddessen");
        Pruefe.Gleich(EchoUrteil.AnderePhase, band.Paket(Audio(5, 2, 50, 20), 20), "neue Phase von A während der Aufnahme");
        Pruefe.Gleich(EchoUrteil.Doppelt, band.Paket(Audio(5, 1, 1, 30), 30), "doppelter Frame");
        Pruefe.Gleich(EchoUrteil.Aufgenommen, band.Paket(Audio(5, 1, 2, 30), 30), "nächster Frame von A");
        band.Paket(Ende(5, 1, 3, 40), 40);
        var phase = band.Takt(540)!;
        Pruefe.Gleich(2, phase.Frames.Count, "nur die Frames von A");
        Pruefe.Wahr(!phase.Va && phase.Frames.All(f => !f.Preroll), "Sprechtaste: kein VA, kein Pre-roll");
        Pruefe.Gleich(0L, phase.Frames[0].VersatzMs, "Wiedergabe beginnt mit dem ersten Frame");
    }

    [Test]
    public static void Echoband_SchleifenschutzErkenntDasEigeneEcho()
    {
        var band = new Echoband { EigenesKonto = 1 };
        // Sprecher 5 spricht, das Band gibt 20 Frames wieder.
        var t = Sprechen(band, 5, 1, 100, 20, 0, lauf: 1);
        var phase = band.Takt(t + 500)!;
        band.WiedergabeBeendet(t + 1000);

        // Ein zweiter Echo-Funker (Konto 9) schickt genau diese Frames zurück: verworfen, keine Wiedergabe.
        t += 1500;
        for (var i = 0; i < phase.Frames.Count; i++, t += 20)
            band.Paket(new EchoPaket(9, 30, 7000 + (uint)i, i == 0 ? Kennzeichen.Beginn : Kennzeichen.None, Spruchart.Netz, EchoNetz, phase.Frames[i].Opus, t), t);
        band.Paket(Ende(9, 30, 7020, t), t);
        Pruefe.Gleich(1, band.Schleifen, "als eigenes Echo erkannt");
        Pruefe.Gleich(EchoZustand.Bereit, band.Zustand, "sofort wieder bereit");
        Pruefe.Wahr(band.Takt(t + 1000) is null, "keine Wiedergabe");

        // Nur das Ende des eigenen Echos (zwei Frames), etwa weil der andere Funker spät bereit war: ebenfalls verworfen.
        t += 1100;
        band.Paket(new EchoPaket(9, 31, 8000, Kennzeichen.Beginn, Spruchart.Netz, EchoNetz, phase.Frames[18].Opus, t), t);
        band.Paket(new EchoPaket(9, 31, 8001, Kennzeichen.None, Spruchart.Netz, EchoNetz, phase.Frames[19].Opus, t + 20), t + 20);
        band.Paket(Ende(9, 31, 8002, t + 40), t + 40);
        Pruefe.Gleich(2, band.Schleifen, "auch ein Rest des eigenen Echos");

        // Weniger als die Hälfte bekannt (Stille-Frames sind oft bitgleich): das ist ein Mensch, die Phase kommt zurück.
        t += 200;
        for (var i = 0; i < 10; i++, t += 20)
        {
            var opus = i < 4 ? phase.Frames[i].Opus : Opus(7, (uint)i);
            band.Paket(new EchoPaket(6, 50, 9000 + (uint)i, i == 0 ? Kennzeichen.Beginn : Kennzeichen.None, Spruchart.Netz, EchoNetz, opus, t), t);
        }
        band.Paket(Ende(6, 50, 9010, t), t);
        Pruefe.Gleich(2, band.Schleifen, "4 von 10 bekannt: keine Schleife");
        Pruefe.Gleich(10, band.Takt(t + 500)?.Frames.Count ?? 0, "Phase kommt zurück");
    }

    [Test]
    public static void Echoband_BeginntNurMitBeginnUndRuhtNachDerWiedergabe()
    {
        var band = new Echoband();
        Pruefe.Gleich(EchoUrteil.Mitten, band.Paket(Audio(5, 1, 12, 0), 0), "ohne BEGINN und ohne Pre-roll: Rest einer Phase");
        // Umsortierter Pre-roll-Stoß: der zweite Frame kommt vor dem ersten und beginnt trotzdem.
        Pruefe.Gleich(EchoUrteil.Begonnen, band.Paket(Audio(5, 1, 11, 20, k: Kennzeichen.Preroll | Kennzeichen.Va), 20), "Pre-roll beginnt");
        Pruefe.Gleich(EchoUrteil.Aufgenommen, band.Paket(Audio(5, 1, 10, 20, k: Kennzeichen.Beginn | Kennzeichen.Preroll | Kennzeichen.Va), 20), "der erste Frame danach");
        band.Paket(Audio(5, 1, 12, 40, k: Kennzeichen.Va), 40);
        band.Paket(Ende(5, 1, 13, 60), 60);
        var phase = band.Takt(560)!;
        Pruefe.Wahr(phase.Frames.Count == 3 && phase.Frames[0].Preroll && phase.Frames[1].Preroll && !phase.Frames[2].Preroll, "Stoß geordnet");
        Pruefe.BytesGleich(Opus(1, 10), phase.Frames[0].Opus, "Frame 10 zuerst");
        band.WiedergabeBeendet(700);

        // Rückkopplung über Lautsprecher: derselbe Sprecher 300 ms nach der Wiedergabe, neu kodiert: ruht.
        Pruefe.Gleich(EchoUrteil.Nachlauf, band.Paket(Audio(5, 2, 100, 1000, lauf: 9, k: Kennzeichen.Beginn), 1000), "Sprecher ruht nach der Wiedergabe");
        Pruefe.Gleich(EchoUrteil.Mitten, band.Paket(Audio(5, 2, 150, 1800, lauf: 9), 1800), "und der Rest dieser Phase auch danach nicht");
        Pruefe.Gleich(EchoUrteil.Begonnen, band.Paket(Audio(6, 1, 1, 1000, lauf: 8, k: Kennzeichen.Beginn), 1000), "ein anderer Sprecher sofort");
        band.Paket(Ende(6, 1, 2, 1020), 1020);
        band.Takt(1520);
        band.WiedergabeBeendet(1600);
        Pruefe.Gleich(EchoUrteil.Begonnen, band.Paket(Audio(5, 3, 300, 1800, lauf: 7, k: Kennzeichen.Beginn), 1800), "nach Ablauf der Sekunde wieder");
    }

    [Test]
    public static void Echoband_GedaechtnisVerfaelltNachZweiMinuten()
    {
        var band = new Echoband();
        var t = Sprechen(band, 5, 1, 100, 5, 0);
        var phase = band.Takt(t + 500)!;
        band.WiedergabeBeendet(t + 700);
        var spaeter = t + 700 + (long)TimeSpan.FromMinutes(2).TotalMilliseconds + 1;
        band.Takt(spaeter);
        for (var i = 0; i < 5; i++)
            band.Paket(new EchoPaket(9, 2, 50 + (uint)i, i == 0 ? Kennzeichen.Beginn : Kennzeichen.None, Spruchart.Netz, EchoNetz, phase.Frames[i].Opus, spaeter + i), spaeter + i);
        band.Paket(Ende(9, 2, 55, spaeter + 10), spaeter + 10);
        Pruefe.Gleich(0, band.Schleifen, "nach dem Gedächtnis kein Schleifenverdacht mehr");
        Pruefe.Gleich(EchoZustand.Pause, band.Zustand, "wird wieder aufgenommen");
    }

    [Test]
    public static void Ratenbremse_HaeltDieGrenzenDesServers()
    {
        // Worst case: 50 Frames/s mit 400 Byte (20000 B/s) und ein Pre-roll-Stoß von 7, dazu je Phase drei ENDE.
        var o = new EchoOptionen();
        var f = Stopwatch.Frequency;
        long jetzt = 1000 * f;
        var bremse = new Ratenbremse(o, jetzt);
        var paketeServer = new TokenEimer(EchoOptionen.ServerPaketeStoss, EchoOptionen.ServerPaketeJeSekunde, jetzt);
        var bytesServer = new TokenEimer(EchoOptionen.ServerOpusBytesStoss, EchoOptionen.ServerOpusBytesJeSekunde, jetzt);
        var start = jetzt;
        long verzoegert = 0;
        for (var i = 0; i < 1500; i++)
        {
            var soll = start + (i < 7 ? 0 : (i - 6) * f / 50) + verzoegert;
            if (soll > jetzt) jetzt = soll;
            long warte;
            while ((warte = bremse.Nehmen(jetzt, 400)) > 0)
            {
                verzoegert += warte;
                jetzt += warte;
            }
            Pruefe.Wahr(paketeServer.Nehmen(jetzt) && bytesServer.Nehmen(jetzt, 400), "Server nimmt Frame " + i + " an");
            if (i % 250 == 249)
            {
                while ((warte = bremse.Nehmen(jetzt, 0, 3)) > 0) jetzt += warte;
                // Strenger als im Betrieb: alle drei ENDE im selben Augenblick statt im 20-ms-Abstand.
                Pruefe.Wahr(paketeServer.Nehmen(jetzt, 3), "Server nimmt die drei ENDE an");
            }
        }
        var sekunden = (jetzt - start) / (double)f;
        Pruefe.Wahr(sekunden > 1500 * 400 / 16_000.0, "400-Byte-Frames werden gebremst (Opus-Budget)");
        Pruefe.Wahr(sekunden < 1500 * 400 / 14_000.0, "aber nicht mehr als nötig");

        // Normale Sprache (120 Byte) geht ungebremst im 20-ms-Takt durch.
        var b2 = new Ratenbremse(o, 0);
        for (var i = 0; i < 500; i++) Pruefe.Gleich(0L, b2.Nehmen(i * f / 50, 120), "120 Byte je 20 ms ungebremst");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new EchoOptionen { PaketeJeSekunde = 61 }.Pruefen(), "über der Servergrenze");
    }

    [Test]
    public static void EchoOptionen_ServergrenzenSindDieDesServers()
    {
        // Funk.ClientKern kennt Funk.Server nicht; die Bremse rechnet gegen Abschriften. Senkt der Server seine Grenzen,
        // müssen die Abschriften mit, sonst verwirft er Echo-Frames still als GRENZE und der Test oben prüft das Falsche.
        Pruefe.Gleich((double)Funk.Server.Lage.Fristen.PaketeJeSekunde, EchoOptionen.ServerPaketeJeSekunde, "Pakete je Sekunde");
        Pruefe.Gleich((double)Funk.Server.Lage.Fristen.PaketeBurst, EchoOptionen.ServerPaketeStoss, "Paketstoß");
        Pruefe.Gleich((double)Funk.Server.Lage.Fristen.OpusBytesJeSekunde, EchoOptionen.ServerOpusBytesJeSekunde, "Opus-Bytes je Sekunde");
        Pruefe.Gleich((double)Funk.Server.Lage.Fristen.OpusBytesBurst, EchoOptionen.ServerOpusBytesStoss, "Opus-Stoß");
        Pruefe.Gleich(Funk.Server.Lage.Fristen.OpusMax, Sprachkanal.OpusMax, "Höchstlänge eines Frames; EchoOptionen.Pruefen verlangt dafür einen Stoß ab 400 Byte");
    }
}
