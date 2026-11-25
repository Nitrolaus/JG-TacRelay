// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Protokoll.Sprache;

namespace Funk.Tests;

/// <summary>Formate 5.2 bis 5.6 und Prüfwert 8.8 (P-13, P-18).</summary>
internal static class SpracheFormatTests
{
    private static byte[] Hex(string text) => SprachTestwerte.Hex(text);

    private static byte[] Opus(int laenge)
    {
        var o = new byte[laenge];
        o[0] = 0xf8;
        return o;
    }

    // ---------------------------------------------------------------- 8.8 Opus-TOC

    private static readonly (string Anfang, int Laenge, int Config, int S, int C, Opusbefund Ergebnis)[] Tabelle88 =
    [
        ("f8", 1, 31, 0, 0, Opusbefund.Gueltig),
        ("78", 1, 15, 0, 0, Opusbefund.Gueltig),
        ("f0", 1, 30, 0, 0, Opusbefund.FalscheDauer),
        ("f1", 1, 30, 0, 1, Opusbefund.Gueltig),
        ("fc", 1, 31, 1, 0, Opusbefund.Stereo),
        ("0b02", 2, 1, 0, 3, Opusbefund.FalscheDauer),
        ("0b", 1, 1, 0, 3, Opusbefund.AnzahlFehlt),
        ("f8", 400, 31, 0, 0, Opusbefund.Gueltig),
        ("f8", 401, 31, 0, 0, Opusbefund.ZuLang),
    ];

    [Test]
    public static void Opus_8_8_Tabelle()
    {
        foreach (var (anfang, laenge, config, s, c, ergebnis) in Tabelle88)
        {
            var opus = new byte[laenge];
            Hex(anfang).CopyTo(opus, 0);
            var toc = opus[0];
            var name = $"{anfang} mit {laenge} Byte";
            Pruefe.Gleich(config, toc >> 3, name + " config");
            Pruefe.Gleich(s, (toc >> 2) & 1, name + " s");
            Pruefe.Gleich(c, toc & 3, name + " c");
            Pruefe.Gleich(ergebnis, OpusPruefung.Pruefen(opus), name);
        }
    }

    [Test]
    public static void Opus_8_8_ImSprachpaket()
    {
        // "f8 mit 401 Byte: verwerfen (m > 400, vor der TOC-Prüfung)" in der Formprüfung von SPRACHE_HOCH.
        foreach (var (anfang, laenge, _, _, _, ergebnis) in Tabelle88)
        {
            var klartext = new byte[SpracheHoch.FesterTeil + 2 + laenge];
            Hex("00000001 0001 00 01 01 0002").CopyTo(klartext, 0);
            Hex(anfang).CopyTo(klartext, SpracheHoch.FesterTeil + 2);
            var erwartet = ergebnis switch
            {
                Opusbefund.Gueltig => Formfehler.Keiner,
                Opusbefund.ZuLang => Formfehler.OpusZuLang,
                _ => Formfehler.OpusUngueltig,
            };
            Pruefe.Gleich(erwartet, SpracheHoch.Lesen(klartext, out _), $"{anfang} mit {laenge} Byte im Paket");
        }
    }

    [Test]
    public static void Opus_FrameDauern_5_13()
    {
        int[] silk = [480, 960, 1920, 2880];
        int[] celt = [120, 240, 480, 960];
        for (var config = 0; config < 32; config++)
        {
            var erwartet = config < 12 ? silk[config % 4] : config < 16 ? (config % 2 == 0 ? 480 : 960) : celt[config % 4];
            Pruefe.Gleich(erwartet, OpusPruefung.FrameSamples(config), $"config {config}");
        }
        Pruefe.Gleich(Opusbefund.Gueltig, OpusPruefung.Pruefen(Hex("f3 02")), "CELT 10 ms, c = 3, M = 2");
        Pruefe.Gleich(Opusbefund.Gueltig, OpusPruefung.Pruefen(Hex("f3 c2")), "M aus den unteren 6 Bit");
        Pruefe.Gleich(Opusbefund.FalscheDauer, OpusPruefung.Pruefen(Hex("f3 00")), "M = 0");
        Pruefe.Gleich(Opusbefund.Leer, OpusPruefung.Pruefen([]), "leer");
    }

    // ---------------------------------------------------------------- 5.2 Kopf

    [Test]
    public static void Kopf_5_2_SchreibenUndLesen()
    {
        var kopf = new SprachKopf(PaketTyp.SpracheHoch, 7, 0xdeadbeef, 0x0102030405060708);
        var b = new byte[16];
        kopf.Schreiben(b);
        Pruefe.BytesGleich(Hex("01 10 07 00 deadbeef 0102030405060708"), b, "Kopf");
        Pruefe.Gleich(kopf, SprachKopf.Lesen(b), "zurück");
    }

    // ---------------------------------------------------------------- 5.3 feste Typen

    [Test]
    public static void Typen_5_3_RichtungUndLaenge()
    {
        foreach (var typ in new[] { PaketTyp.Anmeldung, PaketTyp.PfadAntwort, PaketTyp.Keepalive, PaketTyp.SpracheHoch })
            Pruefe.Gleich(Richtung.Hoch, PaketTypen.RichtungVon(typ), $"{typ} hoch");
        foreach (var typ in new[] { PaketTyp.PfadPruefung, PaketTyp.KeepaliveEcho, PaketTyp.SpracheRunter })
            Pruefe.Gleich(Richtung.Runter, PaketTypen.RichtungVon(typ), $"{typ} runter");
        for (var typ = 1; typ <= 5; typ++)
        {
            Pruefe.Wahr(PaketTypen.LaengePasst((PaketTyp)typ, 48), $"Typ {typ} 48 Byte");
            Pruefe.Falsch(PaketTypen.LaengePasst((PaketTyp)typ, 47), $"Typ {typ} 47 Byte");
            Pruefe.Falsch(PaketTypen.LaengePasst((PaketTyp)typ, 49), $"Typ {typ} 49 Byte");
        }
        Pruefe.Wahr(PaketTypen.LaengePasst(PaketTyp.SpracheHoch, 41) && PaketTypen.LaengePasst(PaketTyp.SpracheHoch, 569), "hoch 41..569");
        Pruefe.Falsch(PaketTypen.LaengePasst(PaketTyp.SpracheHoch, 40) || PaketTypen.LaengePasst(PaketTyp.SpracheHoch, 570), "hoch außerhalb");
        Pruefe.Wahr(PaketTypen.LaengePasst(PaketTyp.SpracheRunter, 45) && PaketTypen.LaengePasst(PaketTyp.SpracheRunter, 477), "runter 45..477");
        Pruefe.Falsch(PaketTypen.LaengePasst(PaketTyp.SpracheRunter, 44) || PaketTypen.LaengePasst(PaketTyp.SpracheRunter, 478), "runter außerhalb");
        Pruefe.Falsch(PaketTypen.IstBekannt((PaketTyp)0x06) || PaketTypen.IstBekannt(0) || PaketTypen.IstBekannt((PaketTyp)0x12), "unbekannte Typen");
    }

    [Test]
    public static void Typen_5_3_FesteKlartexte()
    {
        var b = new byte[16];
        new KeepaliveEcho(1, 0x1234, 0xfedcba98).Schreiben(b);
        Pruefe.BytesGleich(Hex("00000001 00001234 fedcba98 00000000"), b, "KEEPALIVE_ECHO");
        Pruefe.Wahr(KeepaliveEcho.TryLesen(b, out var echo), "ECHO lesen");
        Pruefe.Gleich(new KeepaliveEcho(1, 0x1234, 0xfedcba98), echo, "ECHO zurück");
        b[15] = 1;
        Pruefe.Falsch(KeepaliveEcho.TryLesen(b, out _), "ECHO mit Füllbyte ungleich 0");

        new Keepalive(uint.MaxValue, 7).Schreiben(b);
        Pruefe.BytesGleich(Hex("ffffffff 00000007 0000000000000000"), b, "KEEPALIVE");
        b[8] = 1;
        Pruefe.Falsch(Keepalive.TryLesen(b, out _), "KEEPALIVE mit Füllbyte ungleich 0");
        Pruefe.Falsch(Keepalive.TryLesen(new byte[15], out _), "KEEPALIVE 15 Byte");

        Steuerpakete.AnmeldungSchreiben(b);
        Pruefe.Wahr(Steuerpakete.AnmeldungGueltig(b), "ANMELDUNG");
        b[5] = 1;
        Pruefe.Falsch(Steuerpakete.AnmeldungGueltig(b), "ANMELDUNG nicht null");

        var c1 = new byte[16];
        var c2 = new byte[16];
        Steuerpakete.ChallengeErzeugen(c1);
        Steuerpakete.ChallengeErzeugen(c2);
        Pruefe.Falsch(c1.AsSpan().SequenceEqual(c2), "zwei Challenges verschieden");
        Pruefe.Wahr(Steuerpakete.ChallengeGleich(c1, c1), "Echo passt");
        Pruefe.Falsch(Steuerpakete.ChallengeGleich(c1, c2), "fremde Challenge");
    }

    // ---------------------------------------------------------------- 5.4 SPRACHE_HOCH

    [Test]
    public static void Hoch_5_4_Grenzlaengen()
    {
        var b = new byte[600];
        var n = SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.Ende, Zielart.Durchsage, [], []);
        Pruefe.Gleich(Sprachkanal.HochMin, Datagramm.Laenge(n), "Durchsage an alle, ENDE: 41 Byte");

        var ziele = Enumerable.Range(2, 64).Select(i => (ushort)i).ToArray();
        n = SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.None, Zielart.Durchsage, ziele, Opus(400));
        Pruefe.Gleich(Sprachkanal.HochMax, Datagramm.Laenge(n), "Durchsage an 64 Netze mit 400 Byte Opus: 569 Byte");
        Pruefe.Gleich(Formfehler.Keiner, SpracheHoch.Lesen(b.AsSpan(0, n), out var p), "569 lesen");
        Pruefe.Gleich(64, p.ZielAnzahl, "64 Ziele");
        Pruefe.Gleich((ushort)65, p.Ziel(63), "letztes Ziel");

        var netze = Enumerable.Range(1, 16).Select(i => (ushort)i).ToArray();
        n = SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.Va, Zielart.Netz, netze, Opus(1));
        Pruefe.Gleich(Formfehler.Keiner, SpracheHoch.Lesen(b.AsSpan(0, n), out p), "NETZ mit 16 Zielen, Global darunter");
        Pruefe.Gleich((ushort)1, p.Ziel(0), "Global als NETZ-Ziel erlaubt");
    }

    [Test]
    public static void Hoch_5_4_Formfehler_BeimLesen()
    {
        (string Name, string Klartext, Formfehler Fehler)[] faelle =
        [
            ("zu kurz", "00000001 0001 00 01", Formfehler.ZuKurz),
            ("Zielart 0", "00000001 0001 00 00 01 0002 f8", Formfehler.ZielartUnbekannt),
            ("Zielart 3", "00000001 0001 00 03 01 0002 f8", Formfehler.ZielartUnbekannt),
            ("NETZ n = 0", "00000001 0001 00 01 00 f8", Formfehler.ZielAnzahlUngueltig),
            ("NETZ n = 17", "00000001 0001 00 01 11" + string.Concat(Enumerable.Range(1, 17).Select(i => i.ToString("x4", System.Globalization.CultureInfo.InvariantCulture))) + "f8", Formfehler.ZielAnzahlUngueltig),
            ("DURCHSAGE n = 65", "00000001 0001 00 02 41" + string.Concat(Enumerable.Range(2, 65).Select(i => i.ToString("x4", System.Globalization.CultureInfo.InvariantCulture))) + "f8", Formfehler.ZielAnzahlUngueltig),
            ("Ziele abgeschnitten", "00000001 0001 00 01 02 0002", Formfehler.ZuKurz),
            ("Ziele gleich", "00000001 0001 00 01 02 0002 0002 f8", Formfehler.ZieleNichtAufsteigend),
            ("Ziele fallend", "00000001 0001 00 01 02 0007 0002 f8", Formfehler.ZieleNichtAufsteigend),
            ("Global als Durchsageziel", "00000001 0001 00 02 02 0001 0002 f8", Formfehler.GlobalAlsDurchsageziel),
            ("ENDE mit Opus", "00000001 0001 02 01 01 0002 f8", Formfehler.EndeMitOpus),
            ("ENDE mit PREROLL", "00000001 0001 06 01 01 0002", Formfehler.EndeMitPreroll),
            ("ohne ENDE ohne Opus", "00000001 0001 00 01 01 0002", Formfehler.OpusFehlt),
            ("TOC 10 ms", "00000001 0001 00 01 01 0002 f0", Formfehler.OpusUngueltig),
        ];
        foreach (var (name, klartext, fehler) in faelle)
            Pruefe.Gleich(fehler, SpracheHoch.Lesen(Hex(klartext), out _), name);

        // Reservierte Kennzeichen-Bits lehnt die Formprüfung nicht ab; der Server setzt sie beim Stempeln auf 0 (5.6).
        Pruefe.Gleich(Formfehler.Keiner, SpracheHoch.Lesen(Hex("00000001 0001 f1 01 01 0002 f8"), out var p), "reservierte Bits");
        Pruefe.Gleich((Kennzeichen)0xf1, p.Kennzeichen, "roh gelesen");
    }

    [Test]
    public static void Hoch_5_4_Formfehler_BeimSchreiben()
    {
        var b = new byte[600];
        var opus = new byte[] { 0xf8 };
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.None, Zielart.Netz, [], opus), "NETZ ohne Ziel");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.None, Zielart.Netz,
            Enumerable.Range(1, 17).Select(i => (ushort)i).ToArray(), opus), "NETZ mit 17 Zielen");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.None, Zielart.Durchsage,
            Enumerable.Range(2, 65).Select(i => (ushort)i).ToArray(), opus), "Durchsage mit 65 Zielen");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.None, Zielart.Netz, [7, 2], opus), "fallend");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.None, Zielart.Durchsage, [1], opus), "Global als Durchsageziel");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.Ende, Zielart.Netz, [2], opus), "ENDE mit Opus");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.Ende | Kennzeichen.Preroll, Zielart.Netz, [2], []), "ENDE mit PREROLL");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.None, Zielart.Netz, [2], []), "ohne Opus");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.None, Zielart.Netz, [2], Opus(401)), "401 Byte Opus");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, (Kennzeichen)0x10, Zielart.Netz, [2], opus), "reservierte Bits");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(b, 1, 1, Kennzeichen.None, (Zielart)3, [2], opus), "Zielart 3");
        Pruefe.Wirft<ArgumentException>(() => SpracheHoch.Schreiben(new byte[11], 1, 1, Kennzeichen.None, Zielart.Netz, [2], opus), "Ziel zu klein");
    }

    // ---------------------------------------------------------------- 5.5 SPRACHE_RUNTER

    [Test]
    public static void Runter_5_5_GrenzlaengenUndFormfehler()
    {
        var b = new byte[600];
        var n = SpracheRunter.Schreiben(b, 9, 1, 1, Kennzeichen.Ende, Spruchart.Durchsage, [], []);
        Pruefe.Gleich(Sprachkanal.RunterMin, Datagramm.Laenge(n), "Durchsage an alle, ENDE: 45 Byte");
        var netze = Enumerable.Range(1, 16).Select(i => (ushort)i).ToArray();
        n = SpracheRunter.Schreiben(b, 9, 1, 1, Kennzeichen.None, Spruchart.Global, netze, Opus(400));
        Pruefe.Gleich(Sprachkanal.RunterMax, Datagramm.Laenge(n), "16 Netze mit 400 Byte Opus: 477 Byte");
        Pruefe.Gleich(Formfehler.Keiner, SpracheRunter.Lesen(b.AsSpan(0, n), out var p), "477 lesen");
        Pruefe.Gleich(Spruchart.Global, p.Art, "Art");
        Pruefe.Gleich(16, p.NetzAnzahl, "k");
        Pruefe.Gleich(400, p.Opus.Length, "m");

        (string Name, string Klartext, Formfehler Fehler)[] faelle =
        [
            ("zu kurz", "00000009 00000001 0001 02 02", Formfehler.ZuKurz),
            ("Art 0", "00000009 00000001 0001 00 00 00 f8", Formfehler.ArtUnbekannt),
            ("Art 4", "00000009 00000001 0001 00 04 00 f8", Formfehler.ArtUnbekannt),
            ("k = 17", "00000009 00000001 0001 00 01 11" + string.Concat(Enumerable.Range(1, 17).Select(i => i.ToString("x4", System.Globalization.CultureInfo.InvariantCulture))) + "f8", Formfehler.NetzAnzahlUngueltig),
            ("Netze abgeschnitten", "00000009 00000001 0001 00 01 02 0002", Formfehler.ZuKurz),
            ("ENDE mit Opus", "00000009 00000001 0001 02 01 01 0002 f8", Formfehler.EndeMitOpus),
            ("ohne Opus", "00000009 00000001 0001 00 01 01 0002", Formfehler.OpusFehlt),
        ];
        foreach (var (name, klartext, fehler) in faelle)
            Pruefe.Gleich(fehler, SpracheRunter.Lesen(Hex(klartext), out _), name);
        Pruefe.Wirft<ArgumentException>(() => SpracheRunter.Schreiben(b, 9, 1, 1, Kennzeichen.None, Spruchart.Netz,
            Enumerable.Range(1, 17).Select(i => (ushort)i).ToArray(), [0xf8]), "17 Netze");
    }

    [Test]
    public static void Runter_6_4_StempelMaskiertKennzeichen()
    {
        // Sender setzt BEGINN, PREROLL, VA und ein reserviertes Bit; übernommen werden nur PREROLL und VA.
        var hochKlartext = Hex("00000010 0009 1d 01 01 0002 f8aa");
        Pruefe.Gleich(Formfehler.Keiner, SpracheHoch.Lesen(hochKlartext, out var hoch), "hoch lesen");
        var b = new byte[64];
        var n = SpracheRunter.Stempeln(b, 77, hoch, beginn: false, Spruchart.Netz, [2]);
        Pruefe.Gleich(Formfehler.Keiner, SpracheRunter.Lesen(b.AsSpan(0, n), out var runter), "runter lesen");
        Pruefe.Gleich(Kennzeichen.Preroll | Kennzeichen.Va, runter.Kennzeichen, "ohne BEGINN des Senders, ohne reservierte Bits");
        Pruefe.Gleich(16u, runter.FrameNr, "Frame-Nr übernommen");
        Pruefe.Gleich((ushort)9, runter.Sprechphase, "Sprechphase übernommen");
        Pruefe.BytesGleich(Hex("f8aa"), runter.Opus, "Opus unverändert");

        n = SpracheRunter.Stempeln(b, 77, hoch, beginn: true, Spruchart.Global, [1, 2]);
        SpracheRunter.Lesen(b.AsSpan(0, n), out runter);
        Pruefe.Gleich(Kennzeichen.Beginn | Kennzeichen.Preroll | Kennzeichen.Va, runter.Kennzeichen, "BEGINN vom Server");
        Pruefe.Gleich((ushort)1, runter.Netz(0), "Global zuerst");
    }

    // ---------------------------------------------------------------- 5.6 Sprechphase, Frame-Nr, Pre-roll, Ende (P-18)

    private static long Ms(long ms) => ms * Stopwatch.Frequency / 1000;

    [Test]
    public static void Phase_P18_FrameNrUndEnde()
    {
        var g = new SprechphasenGeber(t0: Ms(1000), startwert: 0xffff);
        g.Beginnen(Ms(1000 + 2000), sprachaktivierung: false); // 2 s nach t0: Anker 100
        Pruefe.Gleich((ushort)0xffff, g.Sprechphase, "Startwert");
        var a = g.Audio();
        var b = g.Audio(); // im Abstand von 1 ms codiert: trotzdem n und n+1
        Pruefe.Gleich(new Sendeframe(0xffff, 100, Kennzeichen.Beginn), a, "erster Frame");
        Pruefe.Gleich(new Sendeframe(0xffff, 101, Kennzeichen.None), b, "zweiter Frame");
        for (var i = 0; i < 98; i++) g.Audio(); // bis 199
        Pruefe.Gleich(199u, g.LetzteFrameNr, "letzter Audio-Frame");

        Pruefe.Gleich(new Sendeframe(0xffff, 200, Kennzeichen.Ende), g.Ende(), "ENDE 1");
        Pruefe.Gleich(new Sendeframe(0xffff, 201, Kennzeichen.Ende), g.Ende(), "ENDE 2");
        Pruefe.Gleich(new Sendeframe(0xffff, 202, Kennzeichen.Ende), g.Ende(), "ENDE 3");
        Pruefe.Wirft<InvalidOperationException>(() => g.Ende(), "kein viertes ENDE");
        Pruefe.Wirft<InvalidOperationException>(() => g.Audio(), "kein Audio nach ENDE");

        // Neuanlage kurz danach: Anker 150 liegt unter 203, also weiter ab 203; Phase +1 mod 2^16.
        g.Beginnen(Ms(1000 + 3000), sprachaktivierung: false);
        Pruefe.Gleich((ushort)0, g.Sprechphase, "Sprechphase mod 2^16");
        Pruefe.Gleich(new Sendeframe(0, 203, Kennzeichen.Beginn), g.Audio(), "größer als die letzte vergebene");
        g.Ende();
        g.Ende();
        g.Ende();

        // Nach einer langen Pause gewinnt der Anker: 60 s nach t0 ergibt 3000.
        g.Beginnen(Ms(1000 + 60_000), sprachaktivierung: false);
        Pruefe.Gleich(3000u, g.Audio().FrameNr, "Pausenlänge bleibt erkennbar");
    }

    [Test]
    public static void Phase_P18_PrerollFortlaufend()
    {
        var g = new SprechphasenGeber(t0: 0, startwert: 5);
        g.Beginnen(Ms(20), sprachaktivierung: true); // Anker 1
        var erster = g.Audio(preroll: true);
        Pruefe.Gleich(new Sendeframe(5, 1, Kennzeichen.Beginn | Kennzeichen.Preroll | Kennzeichen.Va), erster, "erster Vorlauf-Frame");
        for (uint i = 2; i <= 7; i++)
            Pruefe.Gleich(new Sendeframe(5, i, Kennzeichen.Preroll | Kennzeichen.Va), g.Audio(preroll: true), $"Vorlauf-Frame {i}");
        Pruefe.Wirft<InvalidOperationException>(() => g.Audio(preroll: true), "höchstens 7 Vorlauf-Frames");
        Pruefe.Gleich(new Sendeframe(5, 8, Kennzeichen.Va), g.Audio(), "danach normal, VA bleibt");
        Pruefe.Wirft<InvalidOperationException>(() => g.Audio(preroll: true), "kein Pre-roll nach normalem Audio");
        Pruefe.Gleich(new Sendeframe(5, 9, Kennzeichen.Ende | Kennzeichen.Va), g.Ende(), "ENDE einer VA-Phase");
    }

    [Test]
    public static void Phase_KeinPrerollBeiTasteOderFortsetzung()
    {
        var g = new SprechphasenGeber(t0: 0, startwert: 1);
        g.Beginnen(0, sprachaktivierung: false);
        Pruefe.Wirft<InvalidOperationException>(() => g.Audio(preroll: true), "Sprechtaste ohne Pre-roll");
        Pruefe.Wirft<InvalidOperationException>(() => g.Beginnen(0, false), "Beginnen bei offener Phase");
        g.Audio();
        g.Ende();
        g.Beginnen(0, sprachaktivierung: true, fortsetzung: true);
        Pruefe.Wirft<InvalidOperationException>(() => g.Audio(preroll: true), "Fortsetzung nach Zielwechsel ohne Pre-roll");
        Pruefe.Gleich(Kennzeichen.Beginn | Kennzeichen.Va, g.Audio().Kennzeichen, "Fortsetzung beginnt mit BEGINN");

        var neu = new SprechphasenGeber(0, 1);
        Pruefe.Wirft<InvalidOperationException>(() => neu.Audio(), "Audio ohne Phase");
        Pruefe.Wirft<InvalidOperationException>(() => neu.Ende(), "Ende ohne Phase");
    }
}
