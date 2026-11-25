// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using Funk.Klang.Eingabe;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Gegenprüfung der Klangmaschine: Befunde, die erst im Zusammenspiel auffielen, jeweils mit Regressionstest.
/// Automatische Linie hinter RNNoise (A26, A27), Hängeschutz der Sprechtaste, Empfangsanzeige je Sprecher (A18) und
/// doppeltes Freigeben.
/// </summary>
internal static class GegenpruefungKlangmaschineTests
{
    [Test]
    public static async Task Automatisch_AbstandVerschiebtLinie_AuchHinterRnnoise()
    {
        // Hinter RNNoise liegt der Rauschboden in einem ruhigen Raum bei -100 dBFS. Früher klebte die Linie dann an der
        // Untergrenze -60 dBFS, egal welcher Abstand eingestellt war: Der Abstand-Regler der Oberfläche blieb ohne Wirkung.
        await using var s = new Pruefstand();
        await s.StartenAsync();
        var (sprache, _) = Testszenen.Sprache(6, 201);
        s.Abspielen(sprache);
        PruefeWert.Hoechstens(-80, s.Meldungen[^1].RauschbodenDb, "Boden hinter RNNoise nahe digitaler Stille");
        foreach (var (abstand, linie) in new[] { (9f, -60f), (25f, -44f), (3f, -66f), (40f, -29f) })
        {
            s.Maschine.Gate = GateVorgaben.Standard with { AbstandDb = abstand };
            s.Stille(4);
            PruefeWert.Nahe(linie, s.Meldungen[^1].SchwelleDb, 0.01, $"Linie bei Abstand {abstand} dB");
        }
    }

    [Test]
    public static void Automatisch_ObenLiegenderBoden_UnveraendertBodenPlusAbstand()
    {
        // Liegt der Boden über der Untergrenze (lauter Raum, ohne RNNoise), gilt weiter Boden plus Abstand.
        var f = new GateFuehrung { Einstellung = GateVorgaben.Standard with { AbstandDb = 12f } };
        var rauschen = AnbindungSignale.Rauschen(48000 * 4, -50, 17);
        var puffer = new float[480];
        for (var r = 0; r < rauschen.Length / 480; r++)
        {
            var rahmen = rauschen.AsSpan(r * 480, 480);
            _ = f.Vorbereiten(Pegel.Dbfs(rahmen), 0f, false);
            f.Gate.Verarbeiten(rahmen, 0f);
            while (f.Gate.Holen(puffer, out _, out _))
            {
            }
        }
        PruefeWert.Nahe(f.Gate.RauschbodenDbfs + 12, f.LinieDb, 0.5, "Linie = Boden + Abstand");
        PruefeWert.Mindestens(-45, f.LinieDb, "über der Untergrenze");
    }

    [Test]
    public static void Sprechtaste_Haengeschutz_LaesstVerpasstesLoslassenLos()
    {
        var z = new SprechtastenZustand();
        var taste = Tastenbelegung.Taste(0xE01D);
        _ = z.Belegen(taste);
        Pruefe.Falsch(z.HaengerPruefen(false), "nicht gedrückt: nichts zu tun");
        z.Verarbeiten(new Tastenereignis(taste, true), out var w, out _);
        Pruefe.Gleich(1, w, "gedrückt");
        Pruefe.Falsch(z.HaengerPruefen(true), "physisch unten: bleibt gedrückt");
        Pruefe.Falsch(z.HaengerPruefen(false), "einmal oben reicht nicht (Rennen mit dem echten Loslassen)");
        Pruefe.Falsch(z.HaengerPruefen(true), "wieder unten: Zähler beginnt neu");
        Pruefe.Falsch(z.HaengerPruefen(false), "erste Prüfung oben");
        Pruefe.Wahr(z.HaengerPruefen(false), "zweite Prüfung oben: losgelassen melden");
        Pruefe.Falsch(z.IstGedrueckt, "gilt als losgelassen");
        Pruefe.Falsch(z.HaengerPruefen(false), "danach nichts mehr zu melden");
        z.Verarbeiten(new Tastenereignis(taste, false), out w, out _);
        Pruefe.Gleich(0, w, "das verspätete echte Loslassen meldet nicht doppelt");
        z.Verarbeiten(new Tastenereignis(taste, true), out w, out _);
        Pruefe.Gleich(1, w, "Tastenwiederholung einer weiter gehaltenen Taste drückt wieder");
    }

    [Test]
    public static void Sprechtaste_VirtuelleTaste_AusRohdatenUndMausknopf()
    {
        var b = new byte[RawInputAuswertung.TastaturBytes];
        BinaryPrimitives.WriteInt32LittleEndian(b, RawInputAuswertung.TypTastatur);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(24), 0x1D);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(26), 0x02);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(30), 0x11);
        Pruefe.Gleich((ushort)0x11, RawInputAuswertung.VirtuelleTaste(b), "rechte Strg: VK_CONTROL aus den Rohdaten");
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(30), 0xFF);
        Pruefe.Gleich((ushort)0, RawInputAuswertung.VirtuelleTaste(b), "unechte Taste: kein Code");
        var maus = new byte[RawInputAuswertung.MausBytes];
        Pruefe.Gleich((ushort)0, RawInputAuswertung.VirtuelleTaste(maus), "Maus-Rohdaten liefern keinen Tastencode");
        Pruefe.Gleich((ushort)0x01, RawInputAuswertung.VirtuelleTaste(Tastenbelegung.MausLinks), "VK_LBUTTON");
        Pruefe.Gleich((ushort)0x04, RawInputAuswertung.VirtuelleTaste(Tastenbelegung.MausMitte), "VK_MBUTTON");
        Pruefe.Gleich((ushort)0x06, RawInputAuswertung.VirtuelleTaste(Tastenbelegung.MausX2), "VK_XBUTTON2");
        Pruefe.Gleich((ushort)0, RawInputAuswertung.VirtuelleTaste(9), "unbekannter Knopf");
    }

    [Test]
    public static async Task Empfangsanzeige_WerSprichtWieLaut_JeNetz()
    {
        // A18: Die Kachel zeigt, wer gerade spricht, und einen Pegel. Zwei Sprecher auf Netz 3 und 4, einer davon für
        // sich selbst stumm gestellt: Die Anzeige zeigt beide (wie empfangen), nach dem Ende keinen mehr als sprechend.
        await using var s = new Pruefstand();
        await s.StartenAsync();
        var meldungen = new List<SprecherPegel[]>();
        s.Maschine.EmpfangsPegel += sprecher => meldungen.Add(sprecher.ToArray());
        s.Maschine.PegelFuerFunker(9, float.NegativeInfinity);
        using var kodierer = new OpusKodierer();
        var puffer = new byte[OpusKodierer.MaxPaketBytes];
        var vokal = new float[960 * 60];
        for (var i = 0; i < vokal.Length; i++) vokal[i] = 0.2f * MathF.Sin(2f * MathF.PI * 180f * i / 48000f);
        for (var f = 0; f < 60; f++)
        {
            var n = kodierer.Kodieren(vokal.AsSpan(f * 960, 960), puffer);
            var k = f == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine;
            _ = s.Maschine.SpracheEmpfangen(9, 3, 1, (uint)f, k, puffer.AsSpan(0, n), s.Uhr.Us);
            _ = s.Maschine.SpracheEmpfangen(10, 4, 1, (uint)f, k, puffer.AsSpan(0, n), s.Uhr.Us);
            s.Stille(2);
        }
        var waehrend = meldungen.Count;
        Pruefe.Wahr(waehrend >= 20, "etwa 25 Meldungen je Sekunde");
        var mitte = meldungen[waehrend / 2];
        Pruefe.Gleich(2, mitte.Length, "zwei Sprecher bekannt");
        var neun = mitte.Single(p => p.KontoId == 9);
        var zehn = mitte.Single(p => p.KontoId == 10);
        Pruefe.Wahr(neun.Spricht && zehn.Spricht, "beide sprechen");
        Pruefe.Gleich((3, 4), (neun.Netz, zehn.Netz), "Mischer-Netze");
        PruefeWert.Mindestens(-25, neun.PegelDbfs, "stumm gestellter Funker zeigt trotzdem, wie laut er ankommt");
        PruefeWert.Nahe(neun.PegelDbfs, zehn.PegelDbfs, 1.0, "gleiches Signal, gleicher Pegel");

        for (var f = 60; f < 63; f++)
        {
            _ = s.Maschine.SpracheEmpfangen(9, 3, 1, (uint)f, Sprachkennzeichen.Ende, ReadOnlySpan<byte>.Empty, s.Uhr.Us);
            _ = s.Maschine.SpracheEmpfangen(10, 4, 1, (uint)f, Sprachkennzeichen.Ende, ReadOnlySpan<byte>.Empty, s.Uhr.Us);
        }
        s.Stille(100);
        var zuletzt = meldungen[^1];
        Pruefe.Gleich(2, zuletzt.Length, "Sprecher bleiben bekannt (bis zum Aufräumen nach 60 s)");
        Pruefe.Falsch(zuletzt.Any(p => p.Spricht), "nach dem Ende spricht keiner mehr");
        PruefeWert.Hoechstens(-90, zuletzt.Max(p => p.PegelDbfs), "und der Pegel fällt auf Stille");
    }

    [Test]
    public static async Task Empfangsanzeige_OhneSprecher_KeineMeldung()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        var anzahl = 0;
        s.Maschine.EmpfangsPegel += _ => anzahl++;
        s.Stille(50);
        Pruefe.Gleich(0, anzahl, "ohne bekannte Sprecher bleibt die Anzeige still");
    }

    [Test]
    public static async Task Sprechtaste_ZweiThreadsVerschraenkt_KeinOffenesMikrofon()
    {
        // Drücken und Loslassen aus zwei Threads gleichzeitig (etwa Raw Input und ein Knopf der Oberfläche). Jeder Thread
        // endet mit Loslassen; danach darf die Kette nicht mehr "gedrückt" sehen. Das Rennen der früheren Fassung
        // (Unterbrechung zwischen Merker und Kette) ist so schmal, dass dieser Test es kaum trifft; er ist eine
        // Rauchprobe gegen grobe Rückschritte, der Beweis liegt in der Sperre.
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false);
        await s.StartenAsync();
        for (var runde = 0; runde < 20; runde++)
        {
            using var start = new Barrier(2);
            void Haemmern()
            {
                start.SignalAndWait();
                for (var i = 0; i < 20_000; i++)
                {
                    s.Maschine.SprechtasteGedrueckt();
                    s.Maschine.SprechtasteLosgelassen();
                }
            }
            await Task.WhenAll(Task.Run(Haemmern), Task.Run(Haemmern));
            Pruefe.Falsch(s.Maschine.Kette.Sprechtaste, $"Runde {runde}: Kette sieht nach dem letzten Loslassen keine Taste mehr");
        }
    }

    [Test]
    public static async Task DisposeAsync_GleichzeitigZweimal_GibtEinmalFrei()
    {
        var system = new AttrappenSystem();
        var maschine = new KlangMaschine(new KlangMaschinenOptionen { AudioSystem = system });
        await maschine.StartenAsync(null, null, CancellationToken.None);
        var start = new ManualResetEventSlim();
        var a = Task.Run(async () =>
        {
            start.Wait();
            await maschine.DisposeAsync();
        });
        var b = Task.Run(async () =>
        {
            start.Wait();
            await maschine.DisposeAsync();
        });
        start.Set();
        await Task.WhenAll(a, b);
        Pruefe.Wahr(system.Aufnahme!.Freigegeben && system.Wiedergabe!.Freigegeben, "Ströme freigegeben");
    }
}
