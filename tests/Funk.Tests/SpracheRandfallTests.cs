// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using Funk.Protokoll.Schutz;
using Funk.Protokoll.Sprache;

namespace Funk.Tests;

/// <summary>
/// Randfälle aus der Gegenprüfung: Fenster gegen ein Referenzmodell, Zählerüberlauf, Epochenwechsel über 256 Rotationen,
/// leere und zu lange Eingaben, Fuzzing der Prüfkette und der Parser mit kanonischer Rückschrift (Z8).
/// </summary>
internal static class SpracheRandfallTests
{
    private static readonly long Sekunde = Stopwatch.Frequency;

    private static ulong Plus(ulong a, ulong b) => a > ulong.MaxValue - b ? ulong.MaxValue : a + b;

    private static byte[] Material(int saat)
    {
        var m = new byte[Schluesselmaterial.Laenge];
        new Random(saat).NextBytes(m);
        return m;
    }

    private static byte[] KeepaliveKlartext()
    {
        var k = new byte[Steuerpakete.Laenge];
        new Keepalive(1, 2).Schreiben(k);
        return k;
    }

    private static byte[] Paket(RichtungsSchluessel k, PaketTyp typ, uint sitzung, ulong zaehler, byte[] klartext)
    {
        var d = new byte[Datagramm.Laenge(klartext.Length)];
        Datagramm.Versiegeln(k, new SprachKopf(typ, k.Epoche, sitzung, zaehler), klartext, d);
        return d;
    }

    // ---------------------------------------------------------------- Replay-Fenster gegen ein Referenzmodell

    [Test]
    public static void Replay_GegenReferenzmodell()
    {
        // Referenz: angenommen, wenn noch nichts gesehen, über dem Höchstwert, oder Höchstwert - z < W und ungesehen.
        var rnd = new Random(20260929);
        foreach (var breite in new[] { 1, 2, 63, 64, 65, 100, 2048 })
        {
            var w = (ulong)breite;
            for (var lauf = 0; lauf < 12; lauf++)
            {
                var f = new ReplayFenster(breite);
                var gesehen = new HashSet<ulong>();
                var liste = new List<ulong>();
                ulong? hoechster = null;
                var start = (lauf % 4) switch
                {
                    0 => 0UL,
                    1 => ulong.MaxValue - 5000,
                    2 => ulong.MaxValue - 3,
                    _ => (ulong)rnd.NextInt64(0, long.MaxValue),
                };
                for (var i = 0; i < 2000; i++)
                {
                    var h = hoechster ?? start;
                    var z = rnd.Next(9) switch
                    {
                        0 or 1 => Plus(h, (ulong)rnd.Next(1, 3)),
                        2 => Plus(h, (ulong)rnd.Next(1, 64 * 70)),
                        3 => Plus(h, (ulong)rnd.NextInt64(1, 1L << 40)),
                        4 or 5 => h - Math.Min(h, (ulong)rnd.Next(0, 2 * breite + 130)),
                        6 => liste.Count > 0 ? liste[rnd.Next(liste.Count)] : h,
                        _ => h - Math.Min(h, w - Math.Min(w, (ulong)rnd.Next(0, 3))), // h-W, h-W+1, h-W+2
                    };
                    var erwartet = hoechster is not { } hh || z > hh || (hh - z < w && !gesehen.Contains(z));
                    var erwartetNeu = erwartet && (hoechster is null || z > hoechster);

                    if (f.IstNeu(z) != erwartet)
                        throw new PruefungException($"W {breite}, Lauf {lauf}, Schritt {i}: IstNeu({z}) sollte {erwartet} sein");
                    if (f.Annehmen(z, out var neu) != erwartet || neu != erwartetNeu)
                        throw new PruefungException($"W {breite}, Lauf {lauf}, Schritt {i}: Annehmen({z}) sollte {erwartet}/{erwartetNeu} sein");
                    if (erwartet)
                    {
                        gesehen.Add(z);
                        liste.Add(z);
                        if (hoechster is null || z > hoechster) hoechster = z;
                    }
                    if (f.Hoechster != (hoechster ?? 0))
                        throw new PruefungException($"W {breite}, Lauf {lauf}, Schritt {i}: Höchstwert {f.Hoechster} statt {hoechster}");
                }
            }
        }
    }

    // ---------------------------------------------------------------- 5.7 Zähler am Ende des u64

    [Test]
    public static void Zaehler_AmEndeDesU64_KeinUmlaufAufNull()
    {
        // 2^64 Pakete lassen sich nicht senden; der Zähler wird deshalb per Reflection kurz vor das Ende gestellt.
        using var kanal = new SendeKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0(), 0);
        var feld = typeof(SendeKanal).GetField("_zaehler", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new PruefungException("Feld _zaehler nicht gefunden");
        feld.SetValue(kanal, ulong.MaxValue - 1);

        var klartext = new byte[Steuerpakete.Laenge];
        var d = new byte[Sprachkanal.FestesDatagramm];
        Pruefe.Wahr(kanal.Versiegeln(PaketTyp.Anmeldung, klartext, d, 0, out _), "2^64-2 wird noch vergeben");
        Pruefe.Gleich(ulong.MaxValue - 1, SprachKopf.Lesen(d).Zaehler, "Zähler im Kopf");

        Array.Clear(d);
        Pruefe.Falsch(kanal.Versiegeln(PaketTyp.Anmeldung, klartext, d, Sekunde, out var n), "2^64-1 wird nicht vergeben");
        Pruefe.Gleich(0, n, "keine Länge");
        Pruefe.BytesGleich(new byte[d.Length], d, "nichts geschrieben");
        Pruefe.Wahr(kanal.Gesperrt, "gesperrt");
        Pruefe.Gleich(ulong.MaxValue, kanal.NaechsterZaehler, "kein Umlauf auf 0");

        // Auch eine neue Epoche gibt keinen Zählerwert frei, der Client muss eine neue Sitzung beginnen.
        kanal.Umschalten(SprachTestwerte.Hoch1(), 2 * Sekunde);
        Pruefe.Falsch(kanal.Versiegeln(PaketTyp.Anmeldung, klartext, d, 2 * Sekunde, out _), "auch in Epoche 1 nicht");
        Pruefe.Falsch(kanal.NeueSitzungNoetig(7 * Sekunde), "genau 5 s gesperrt");
        Pruefe.Wahr(kanal.NeueSitzungNoetig(7 * Sekunde + 1), "länger als 5 s: neue Sitzung");
    }

    [Test]
    public static void Replay_ZaehlerAmEndeDesU64()
    {
        var f = new ReplayFenster();
        Pruefe.Wahr(f.Annehmen(ulong.MaxValue, out var neu) && neu, "2^64-1 als erstes Paket");
        Pruefe.Falsch(f.Annehmen(ulong.MaxValue), "doppelt");
        Pruefe.Wahr(f.Annehmen(ulong.MaxValue - 2047), "Abstand 2047");
        Pruefe.Falsch(f.Annehmen(ulong.MaxValue - 2048), "Abstand 2048");
        Pruefe.Falsch(f.Annehmen(0), "0 weit zu alt");
    }

    // ---------------------------------------------------------------- 5.8 Epochen

    [Test]
    public static void Epoche_Server_AnkuendigenUebernehmen_DisposeNach10s()
    {
        var e0 = SprachTestwerte.Hoch0();
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, e0);
        Pruefe.Wirft<InvalidOperationException>(() => kanal.Uebernehmen(0), "Übernehmen ohne Ankündigung");

        using var e2 = new RichtungsSchluessel(2, SprachTestwerte.KHoch1, SprachTestwerte.IvHoch1);
        Pruefe.Wirft<ArgumentException>(() => kanal.Ankuendigen(e2, 0), "e+2 statt e+1");

        var e1 = SprachTestwerte.Hoch1();
        kanal.Ankuendigen(e1, 0);
        Pruefe.Gleich<byte?>(1, kanal.NaechsteEpoche, "angekündigt");
        using var nochmal = new RichtungsSchluessel(1, SprachTestwerte.KHoch0, SprachTestwerte.IvHoch0);
        Pruefe.Wirft<InvalidOperationException>(() => kanal.Ankuendigen(nochmal, 0), "zweite Ankündigung");
        Pruefe.Wirft<InvalidOperationException>(() => kanal.Umschalten(nochmal, 0), "Umschalten während einer Ankündigung");

        kanal.Uebernehmen(Sekunde);
        Pruefe.Gleich((byte)1, kanal.Epoche, "e+1 aktuell");
        Pruefe.Gleich<byte?>(0, kanal.VorigeEpoche, "e als vorige");
        Pruefe.Wirft<InvalidOperationException>(() => kanal.Ankuendigen(nochmal, 2 * Sekunde), "keine Rotation, solange e noch offen ist");

        kanal.Takt(11 * Sekunde - 1);
        Pruefe.Falsch(e0.Verworfen, "vor Ablauf der 10 s");
        kanal.Takt(11 * Sekunde);
        Pruefe.Wahr(e0.Verworfen, "Dispose 10 s nach der Antwort");
        Pruefe.Gleich<byte?>(null, kanal.VorigeEpoche, "e verworfen");
        Pruefe.Falsch(e1.Verworfen, "e+1 lebt");
    }

    [Test]
    public static void Epoche_Client_ErstesPaketSpaet_HoechstensZwanzigSekunden()
    {
        var e0 = SprachTestwerte.Runter0();
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungB, Richtung.Runter, e0);
        using var sender0 = SprachTestwerte.Runter0();
        using var sender1 = new RichtungsSchluessel(1, SprachTestwerte.KHoch1, SprachTestwerte.IvHoch1);
        kanal.Umschalten(new RichtungsSchluessel(1, SprachTestwerte.KHoch1, SprachTestwerte.IvHoch1), 0);
        var echo = new byte[Steuerpakete.Laenge];
        new KeepaliveEcho(1, 2, 3).Schreiben(echo);
        var k = new byte[Sprachkanal.MaxKlartext];

        // Pakete der vorigen Epoche verlängern nichts und starten die 10-s-Frist nicht.
        Pruefe.Gleich(Empfangsergebnis.Angenommen,
            kanal.Oeffnen(Paket(sender0, PaketTyp.KeepaliveEcho, SprachTestwerte.SitzungB, 1, echo), k, 5 * Sekunde), "e nach 5 s");
        // Das erste Paket in e+1 kommt erst nach 15 s: 15 + 10 = 25 s, die Obergrenze 20 s bleibt maßgeblich.
        Pruefe.Gleich(Empfangsergebnis.Angenommen,
            kanal.Oeffnen(Paket(sender1, PaketTyp.KeepaliveEcho, SprachTestwerte.SitzungB, 2, echo), k, 15 * Sekunde), "erstes Paket in e+1");
        Pruefe.Gleich(Empfangsergebnis.Angenommen,
            kanal.Oeffnen(Paket(sender0, PaketTyp.KeepaliveEcho, SprachTestwerte.SitzungB, 3, echo), k, 20 * Sekunde - 1), "e knapp vor 20 s");
        Pruefe.Gleich(Empfangsergebnis.EpocheNichtAngenommen,
            kanal.Oeffnen(Paket(sender0, PaketTyp.KeepaliveEcho, SprachTestwerte.SitzungB, 4, echo), k, 20 * Sekunde), "e ab 20 s");
        Pruefe.Wahr(e0.Verworfen, "Dispose");
    }

    [Test]
    public static void Epoche_Server_256Rotationen_Mod256()
    {
        long t = 0;
        var m0 = Material(0);
        using var bund = new ServerSchluesselbund(SprachTestwerte.SitzungA, m0, t);
        using var client = new SendeKanal(SprachTestwerte.SitzungA, Richtung.Hoch, Schluesselmaterial.Hoch(0, m0), t);
        Pruefe.Wahr(bund.AntwortErhalten(0, t), "START");
        var klartext = KeepaliveKlartext();
        var d = new byte[Sprachkanal.FestesDatagramm];
        var k = new byte[Sprachkanal.MaxKlartext];

        for (var i = 1; i <= 256; i++)
        {
            t += 11 * Sekunde; // die vorige Epoche ist jeweils verworfen
            var m = Material(i);
            var e = bund.RotationBeginnen(m, t);
            Pruefe.Gleich(unchecked((byte)i), e, $"Rotation {i}");
            client.Umschalten(Schluesselmaterial.Hoch(e, m), t);
            Pruefe.Wahr(client.Versiegeln(PaketTyp.Keepalive, klartext, d, t, out _), $"senden in {e}");
            Pruefe.Gleich(Empfangsergebnis.Angenommen, bund.Hoch.Oeffnen(d, k, t), $"hoch in {e} vor der Antwort");
            Pruefe.Wahr(bund.AntwortErhalten(e, t), $"Antwort {e}");
        }
        Pruefe.Gleich((byte)0, bund.Epoche, "256 Rotationen später wieder Epoche 0");
        Pruefe.Gleich(256UL, client.NaechsterZaehler, "Zähler lief ohne Rücksetzen durch alle Epochen");

        // Ein Paket mit dem Schlüssel der ersten Epoche 0 und frischem Zähler: gleiche Epochennummer, anderer Schlüssel.
        using var alt = Schluesselmaterial.Hoch(0, m0);
        var altesPaket = Paket(alt, PaketTyp.Keepalive, SprachTestwerte.SitzungA, 1000, klartext);
        Pruefe.Gleich(Empfangsergebnis.TagUngueltig, bund.Hoch.Oeffnen(altesPaket, k, t), "alter Schlüssel der Epoche 0");
    }

    // ---------------------------------------------------------------- NeuerHoechstwert nach Verwurf (5.10 Auslöser)

    [Test]
    public static void NeuerHoechstwert_NachJedemVerwurfFalsch()
    {
        using var sender = SprachTestwerte.Hoch0();
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        var klartext = KeepaliveKlartext();
        var k = new byte[Sprachkanal.MaxKlartext];
        byte[] P(ulong z) => Paket(sender, PaketTyp.Keepalive, SprachTestwerte.SitzungA, z, klartext);

        Pruefe.Gleich(Empfangsergebnis.Angenommen, kanal.Oeffnen(P(10), k, 0), "10");
        Pruefe.Wahr(kanal.NeuerHoechstwert, "10 ist neuer Höchstwert");
        Pruefe.Gleich(Empfangsergebnis.Replay, kanal.Oeffnen(P(10), k, 0), "10 doppelt");
        Pruefe.Falsch(kanal.NeuerHoechstwert, "ein Replay löst keine Pfadprüfung aus");

        var gefaelscht = P(11);
        gefaelscht[^1] ^= 1;
        Pruefe.Gleich(Empfangsergebnis.TagUngueltig, kanal.Oeffnen(gefaelscht, k, 0), "11 gefälscht");
        Pruefe.Falsch(kanal.NeuerHoechstwert, "eine Fälschung löst keine Pfadprüfung aus");

        var fremdeEpoche = P(12);
        fremdeEpoche[2] = 9;
        Pruefe.Gleich(Empfangsergebnis.EpocheNichtAngenommen, kanal.Oeffnen(fremdeEpoche, k, 0), "fremde Epoche");
        Pruefe.Falsch(kanal.NeuerHoechstwert, "nach Verwurf in Schritt 5");

        Pruefe.Gleich(Empfangsergebnis.Angenommen, kanal.Oeffnen(P(5), k, 0), "5 umsortiert");
        Pruefe.Falsch(kanal.NeuerHoechstwert, "umsortiertes Paket bringt keinen Höchstwert");
        Pruefe.Gleich(Empfangsergebnis.Angenommen, kanal.Oeffnen(P(11), k, 0), "11 echt");
        Pruefe.Wahr(kanal.NeuerHoechstwert, "11 ist neuer Höchstwert");
    }

    // ---------------------------------------------------------------- Leere, kurze und zu lange Eingaben

    [Test]
    public static void Eingaben_LeerUndKurz_OhneAusnahme()
    {
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        var u = new TestUmgebung(kanal);
        var k = new byte[Sprachkanal.MaxKlartext];
        foreach (var laenge in new[] { 0, 1, 2, 15, 16, 17, 31, 32, 40 })
            Pruefe.Gleich(Empfangsergebnis.LaengeUngueltig, SprachTestwerte.Server(u, SprachTestwerte.TvA.AsSpan(0, laenge), k), $"{laenge} Byte");

        Pruefe.Gleich(Empfangsergebnis.LaengeUngueltig, kanal.Oeffnen(SprachTestwerte.TvA.AsSpan(0, 31), k, 0), "Kanal: 31 Byte");
        using var schluessel = SprachTestwerte.Hoch0();
        Pruefe.Falsch(Datagramm.Oeffnen(schluessel, [], k), "Datagramm leer");
        Pruefe.Falsch(Datagramm.Oeffnen(schluessel, SprachTestwerte.TvA.AsSpan(0, 31), k), "Datagramm 31 Byte");
        Pruefe.Gleich(0L, kanal.Entschluesselungen, "nichts entschlüsselt");

        Pruefe.Gleich(Formfehler.ZuKurz, SpracheHoch.Lesen([], out _), "SPRACHE_HOCH leer");
        Pruefe.Gleich(Formfehler.ZuKurz, SpracheHoch.Lesen(new byte[8], out _), "SPRACHE_HOCH 8 Byte");
        Pruefe.Gleich(Formfehler.ZuKurz, SpracheRunter.Lesen([], out _), "SPRACHE_RUNTER leer");
        Pruefe.Gleich(Formfehler.ZuKurz, SpracheRunter.Lesen(new byte[12], out _), "SPRACHE_RUNTER 12 Byte");
        Pruefe.Falsch(Keepalive.TryLesen([], out _), "KEEPALIVE leer");
        Pruefe.Falsch(Keepalive.TryLesen(new byte[17], out _), "KEEPALIVE 17 Byte");
        Pruefe.Falsch(KeepaliveEcho.TryLesen([], out _), "ECHO leer");
        Pruefe.Falsch(Steuerpakete.AnmeldungGueltig([]), "ANMELDUNG leer");
        Pruefe.Falsch(Steuerpakete.AnmeldungGueltig(new byte[17]), "ANMELDUNG 17 Byte");
        Pruefe.Falsch(Steuerpakete.ChallengeGleich([], []), "Challenge leer");
        Pruefe.Falsch(Steuerpakete.ChallengeGleich(new byte[15], new byte[15]), "Challenge 15 Byte");
        Pruefe.Falsch(Steuerpakete.ChallengeGleich(new byte[16], new byte[17]), "Challenge verschieden lang");
    }

    [Test]
    public static void Eingaben_ZuLang()
    {
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        var u = new TestUmgebung(kanal);
        var k = new byte[Sprachkanal.MaxKlartext];
        var gross = new byte[Sprachkanal.EmpfangspufferLaenge];
        SprachTestwerte.TvA.AsSpan(0, 16).CopyTo(gross);

        Pruefe.Gleich(Empfangsergebnis.LaengePasstNichtZumTyp, SprachTestwerte.Server(u, gross.AsSpan(0, 1200), k), "SPRACHE_HOCH 1200 Byte");
        Pruefe.Gleich(Empfangsergebnis.LaengeUngueltig, SprachTestwerte.Server(u, gross.AsSpan(0, 1201), k), "1201 Byte");
        Pruefe.Gleich(Empfangsergebnis.LaengeUngueltig, SprachTestwerte.Server(u, gross, k), "voller Puffer, 2048 Byte");
        gross[1] = (byte)PaketTyp.Keepalive;
        Pruefe.Gleich(Empfangsergebnis.LaengePasstNichtZumTyp, SprachTestwerte.Server(u, gross.AsSpan(0, 49), k), "KEEPALIVE 49 Byte");
        Pruefe.Gleich(Empfangsergebnis.LaengePasstNichtZumTyp, SprachTestwerte.Server(u, gross.AsSpan(0, 1200), k), "KEEPALIVE 1200 Byte");
        Pruefe.Gleich(Empfangsergebnis.LaengeUngueltig, Empfangspruefung.Pruefen(u, SprachTestwerte.TvC,
            new Empfangsdaten(Richtung.Hoch, Weg.Udp, 0, Abgeschnitten: true), 0, k, out _, out _), "abgeschnitten trotz passender Länge");
        Pruefe.Gleich(0L, kanal.Entschluesselungen, "nichts entschlüsselt");

        // Opus über 400 Byte scheitert an der Länge, bevor der TOC gelesen wird (8.8 letzte Zeile), auch bei ungültigem TOC.
        foreach (var toc in new byte[] { 0xfc, 0xf0, 0x0b })
        {
            var opus = new byte[401];
            opus[0] = toc;
            Pruefe.Gleich(Opusbefund.ZuLang, OpusPruefung.Pruefen(opus), $"TOC {toc:x2} mit 401 Byte");
            var klartext = new byte[SpracheHoch.FesterTeil + 2 + opus.Length];
            SprachTestwerte.Hex("00000001 0001 00 01 01 0002").CopyTo(klartext, 0);
            opus.CopyTo(klartext, SpracheHoch.FesterTeil + 2);
            Pruefe.Gleich(Formfehler.OpusZuLang, SpracheHoch.Lesen(klartext, out _), $"TOC {toc:x2} mit 401 Byte im Paket");
        }
    }

    [Test]
    public static void Eingaben_KlartextpufferZuKlein_WirftImmer()
    {
        // Die Ausnahme hängt nicht davon ab, ob ein Datagramm die Schritte 1 bis 6 besteht.
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        var u = new TestUmgebung(kanal);
        var daten = new Empfangsdaten(Richtung.Hoch, Weg.Udp, 0);
        Pruefe.Wirft<ArgumentOutOfRangeException>(() =>
            Empfangspruefung.Pruefen(u, [], daten, 0, new byte[Sprachkanal.MaxKlartext - 1], out _, out _), "leeres Datagramm");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() =>
            Empfangspruefung.Pruefen(u, SprachTestwerte.TvA, daten, 0, new byte[64], out _, out _), "gültiges Datagramm");
        Pruefe.Gleich(0L, kanal.Entschluesselungen, "nichts entschlüsselt");
        Pruefe.Wahr(kanal.IstNeu(5), "Fenster unverändert");
    }

    [Test]
    public static void Tunnel_FremdeSitzung_VorDerTabellensuche()
    {
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        var u = new TestUmgebung(kanal);
        var k = new byte[Sprachkanal.MaxKlartext];
        var unbekannt = SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[4] ^= 0xff);
        Pruefe.Gleich(Empfangsergebnis.TunnelFremdeSitzung,
            SprachTestwerte.Server(u, unbekannt, k, weg: Weg.Tunnel, tragende: SprachTestwerte.SitzungA), "unbekannte Sitzungs-ID im Tunnel");
        Pruefe.Gleich(Empfangsergebnis.SitzungUnbekannt, SprachTestwerte.Server(u, unbekannt, k), "dieselbe per UDP");
    }

    // ---------------------------------------------------------------- Geheimnisse

    [Test]
    public static void Geheimnisse_NichtInToString_DisposeSperrt()
    {
        var schluessel = new RichtungsSchluessel(0, SprachTestwerte.KHoch0, SprachTestwerte.IvHoch0);
        using var sende = new SendeKanal(1, Richtung.Hoch, SprachTestwerte.Hoch0(), 0);
        using var empfang = new EmpfangsKanal(1, Richtung.Hoch, SprachTestwerte.Hoch0());
        using var bund = new ServerSchluesselbund(1, SprachTestwerte.VoiceKeysRahmen.AsSpan(14), 0);
        foreach (var text in new[] { schluessel.ToString(), sende.ToString(), empfang.ToString(), bund.ToString() })
        {
            var t = text ?? "";
            foreach (var geheim in new[] { SprachTestwerte.KHoch0, SprachTestwerte.IvHoch0, SprachTestwerte.KRunter0, SprachTestwerte.IvRunter0 })
            {
                Pruefe.Falsch(t.Contains(Convert.ToHexString(geheim.AsSpan(0, 6)), StringComparison.OrdinalIgnoreCase), $"ToString verrät nichts: {t}");
                Pruefe.Falsch(t.Contains(Convert.ToBase64String(geheim.AsSpan(0, 6)), StringComparison.Ordinal), $"ToString verrät nichts (Base64): {t}");
            }
        }

        schluessel.Dispose();
        schluessel.Dispose();
        Pruefe.Wirft<ObjectDisposedException>(() => schluessel.Versiegeln(0, [], [], [], new byte[16]), "Versiegeln nach Dispose");
        Pruefe.Wirft<ObjectDisposedException>(() => schluessel.Oeffnen(0, [], [], new byte[16], []), "Öffnen nach Dispose");
    }

    // ---------------------------------------------------------------- Sprechphase vor t0

    [Test]
    public static void Phase_AufnahmeVorT0_AnkerNull()
    {
        var g = new SprechphasenGeber(t0: 100 * Sekunde, startwert: 9);
        g.Beginnen(tAufnahme: 50 * Sekunde, sprachaktivierung: false);
        Pruefe.Gleich(new Sendeframe(9, 0, Kennzeichen.Beginn), g.Audio(), "vor t0 gilt Anker 0");
        g.Ende();
        g.Beginnen(tAufnahme: 0, sprachaktivierung: false);
        Pruefe.Gleich(new Sendeframe(10, 2, Kennzeichen.Beginn), g.Audio(), "danach letzte vergebene + 1");
    }

    // ---------------------------------------------------------------- Fuzzing (klein; P-42 verlangt 10^7 Fälle separat)

    [Test]
    public static void Fuzz_Empfangspruefung_KeineAusnahme_NieAngenommen()
    {
        var rnd = new Random(5_9);
        using var server = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        server.Ankuendigen(SprachTestwerte.Hoch1(), 0);
        using var client = new EmpfangsKanal(SprachTestwerte.SitzungB, Richtung.Runter, SprachTestwerte.Runter0());
        var us = new TestUmgebung(server);
        var uc = new TestUmgebung(client);
        var k = new byte[Sprachkanal.MaxKlartext];
        var puffer = new byte[Sprachkanal.EmpfangspufferLaenge];
        // Nach Zähler aufsteigend (F 0, A 5, C 6, D 2^32), damit das Nachreichen am Ende nicht am Fensterrand scheitert.
        byte[][] hoch = [SprachTestwerte.TvF, SprachTestwerte.TvA, SprachTestwerte.TvC, SprachTestwerte.TvD];
        byte[][] runter = [SprachTestwerte.TvB, SprachTestwerte.TvE];

        for (var i = 0; i < 20_000; i++)
        {
            var anServer = rnd.Next(3) != 0;
            var vorlagen = anServer ? hoch : runter;
            int laenge;
            byte[]? vorlage = null;
            if (rnd.Next(4) != 0)
            {
                vorlage = vorlagen[rnd.Next(vorlagen.Length)];
                vorlage.CopyTo(puffer, 0);
                laenge = vorlage.Length;
                if (rnd.Next(8) == 0) laenge = Math.Clamp(laenge + rnd.Next(-3, 4), 0, puffer.Length);
                for (var j = rnd.Next(1, 5); j > 0; j--)
                    puffer[rnd.Next(Math.Max(1, laenge))] ^= (byte)rnd.Next(1, 256);
            }
            else
            {
                laenge = rnd.Next(0, puffer.Length + 1);
                rnd.NextBytes(puffer.AsSpan(0, laenge));
                if (laenge >= 16 && rnd.Next(2) == 0)
                {
                    // plausibler Kopf, damit auch die hinteren Schritte erreicht werden
                    puffer[0] = Sprachkanal.Version;
                    puffer[2] = (byte)rnd.Next(0, 2);
                    puffer[3] = 0;
                    BinaryPrimitives.WriteUInt32BigEndian(puffer.AsSpan(4), anServer ? SprachTestwerte.SitzungA : SprachTestwerte.SitzungB);
                }
            }
            var datagramm = puffer.AsSpan(0, laenge);
            if (vorlage is not null && datagramm.SequenceEqual(vorlage)) continue;

            var weg = rnd.Next(5) == 0 ? Weg.Tunnel : Weg.Udp;
            var daten = new Empfangsdaten(anServer ? Richtung.Hoch : Richtung.Runter, weg, 0, rnd.Next(20) == 0,
                anServer ? SprachTestwerte.SitzungA : SprachTestwerte.SitzungB);
            var e = anServer
                ? Empfangspruefung.Pruefen(us, datagramm, daten, 0, k, out _, out _)
                : Empfangspruefung.Pruefen(uc, datagramm, daten, 0, k, out _, out _);
            if (e == Empfangsergebnis.Angenommen)
                throw new PruefungException($"Fall {i}: verändertes Datagramm angenommen: {Convert.ToHexString(datagramm)}");
        }

        // Kein Verwurf hat das Fenster verschoben: die echten Vektoren gehen danach noch durch.
        foreach (var d in hoch) Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Server(us, d, k), "Vektor hoch nach dem Fuzzing");
        foreach (var d in runter) Pruefe.Gleich(Empfangsergebnis.Angenommen, SprachTestwerte.Client(uc, d, k), "Vektor runter nach dem Fuzzing");
    }

    [Test]
    public static void Fuzz_Parser_KeineAusnahme_KanonischeRueckschrift()
    {
        var rnd = new Random(8_8);
        var b = new byte[700];
        var aus = new byte[700];
        var ziele = new ushort[64];
        int gueltigHoch = 0, gueltigRunter = 0;
        byte[][] tocs = [[0xf8], [0x78], [0xf1], [0xf3, 0x02], [0x0b, 0x01], [0xfc], [0xf0], [0x0b]];

        for (var i = 0; i < 60_000; i++)
        {
            var runter = (i & 1) == 1;
            var fest = runter ? SpracheRunter.FesterTeil : SpracheHoch.FesterTeil;
            int laenge;
            if (rnd.Next(4) == 0)
            {
                laenge = rnd.Next(0, 600);
                rnd.NextBytes(b.AsSpan(0, laenge));
            }
            else
            {
                // halb strukturiert: plausible Felder, damit die tieferen Regeln erreicht werden
                var anzahl = rnd.Next(0, runter ? 18 : 20);
                var ende = rnd.Next(3) == 0;
                var toc = tocs[rnd.Next(tocs.Length)];
                var m = ende ? (rnd.Next(4) == 0 ? 1 : 0) : Math.Max(toc.Length, rnd.Next(0, 3) == 0 ? rnd.Next(395, 403) : rnd.Next(1, 40));
                laenge = fest + 2 * anzahl + m;
                rnd.NextBytes(b.AsSpan(0, laenge));
                var kz = (byte)(rnd.Next(16) & ~0x02);
                if (ende) kz |= 0x02;
                if (rnd.Next(6) == 0) kz |= 0x10;
                b[fest - 3] = kz;
                b[fest - 2] = (byte)(runter ? rnd.Next(0, 5) : rnd.Next(0, 4));
                b[fest - 1] = (byte)anzahl;
                var netz = rnd.Next(0, 3);
                for (var j = 0; j < anzahl; j++)
                {
                    netz += rnd.Next(runter ? -5 : 0, 400);
                    BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(fest + 2 * j), (ushort)Math.Clamp(netz, 0, 65535));
                }
                if (m > 0) toc.AsSpan(0, Math.Min(toc.Length, m)).CopyTo(b.AsSpan(fest + 2 * anzahl));
                if (rnd.Next(5) == 0) b[rnd.Next(laenge)] ^= (byte)rnd.Next(1, 256);
            }

            var klartext = b.AsSpan(0, laenge);
            if (!runter && SpracheHoch.Lesen(klartext, out var h) == Formfehler.Keiner)
            {
                gueltigHoch++;
                var n = h.ZielAnzahl;
                Pruefe.Wahr(h.Zielart == Zielart.Netz ? n is >= 1 and <= 16 : n is >= 0 and <= 64, "Zielanzahl");
                for (var j = 0; j < n; j++)
                {
                    ziele[j] = h.Ziel(j);
                    Pruefe.Wahr(j == 0 || ziele[j] > ziele[j - 1], "streng aufsteigend");
                    Pruefe.Wahr(h.Zielart != Zielart.Durchsage || ziele[j] != Sprachkanal.GlobalNetz, "Global kein Durchsageziel");
                }
                Pruefe.Wahr(h.IstEnde
                    ? h.Opus.IsEmpty && (h.Kennzeichen & Kennzeichen.Preroll) == 0
                    : OpusPruefung.Pruefen(h.Opus) == Opusbefund.Gueltig, "ENDE- und Opus-Regeln");
                Pruefe.Gleich(laenge, SpracheHoch.FesterTeil + 2 * n + h.Opus.Length, "Länge");
                if (((byte)h.Kennzeichen & KennzeichenMasken.Reserviert) == 0)
                {
                    var geschrieben = SpracheHoch.Schreiben(aus, h.FrameNr, h.Sprechphase, h.Kennzeichen, h.Zielart, ziele.AsSpan(0, n), h.Opus);
                    Pruefe.BytesGleich(klartext, aus.AsSpan(0, geschrieben), "SPRACHE_HOCH kanonisch");
                }
            }
            else if (runter && SpracheRunter.Lesen(klartext, out var r) == Formfehler.Keiner)
            {
                gueltigRunter++;
                var n = r.NetzAnzahl;
                Pruefe.Wahr(n is >= 0 and <= 16, "Netzanzahl");
                for (var j = 0; j < n; j++) ziele[j] = r.Netz(j);
                Pruefe.Wahr(r.Art is Spruchart.Netz or Spruchart.Durchsage or Spruchart.Global, "Art");
                Pruefe.Wahr(r.IstEnde ? r.Opus.IsEmpty : r.Opus.Length is >= 1 and <= 400, "ENDE- und Opus-Regeln");
                if (((byte)r.Kennzeichen & KennzeichenMasken.Reserviert) == 0)
                {
                    var geschrieben = SpracheRunter.Schreiben(aus, r.Absender, r.FrameNr, r.Sprechphase, r.Kennzeichen, r.Art, ziele.AsSpan(0, n), r.Opus);
                    Pruefe.BytesGleich(klartext, aus.AsSpan(0, geschrieben), "SPRACHE_RUNTER kanonisch");
                }
            }
        }
        Pruefe.Wahr(gueltigHoch > 1000, $"genug gültige SPRACHE_HOCH erreicht ({gueltigHoch})");
        Pruefe.Wahr(gueltigRunter > 1000, $"genug gültige SPRACHE_RUNTER erreicht ({gueltigRunter})");
    }
}
