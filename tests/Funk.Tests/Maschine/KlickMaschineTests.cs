// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Der Funk-Klick an der echten Klangmaschine (A63, seit 0.5.0): Einstellen über den Vertrag, die eigene Sprechtaste klickt in der
/// eingestellten Lautstärke oder gar nicht, "Anhören" spielt nur bei laufender Wiedergabe. Geräte-Attrappen, kein echtes Gerät.
/// </summary>
internal static class KlickMaschineTests
{
    /// <summary>Kanal 0 der Wiedergabe (die Attrappe spielt stereo; mittig trägt jeder Kanal den Klick mit dem Faktor 0,7071).</summary>
    internal static float[] Links(List<float> ausgabe) => [.. ausgabe.Where((_, i) => i % 2 == 0)];

    /// <summary>Pegel über 200 ms ab dem ersten Wert ungleich null in Kanal 0, auf mittig mono umgerechnet; ohne Klang <see cref="Pegel.MinDbfs"/>.</summary>
    internal static float KlickPegel(List<float> ausgabe)
    {
        var l = Links(ausgabe);
        var start = Array.FindIndex(l, v => v != 0f);
        if (start < 0) return Pegel.MinDbfs;
        var stueck = l.AsSpan(start, Math.Min(9600, l.Length - start)).ToArray();
        for (var i = 0; i < stueck.Length; i++) stueck[i] /= 0.70710678f;
        return FunkKlick.PegelUeber200Ms(stueck);
    }

#pragma warning disable CA1859
    private static IKlangMaschine AlsVertrag(KlangMaschine maschine) => maschine;
#pragma warning restore CA1859

    [Test]
    public static async Task Klick_EinstellenUndAnhoeren_UeberDenVertrag()
    {
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false);
        var m = AlsVertrag(s.Maschine);
        Pruefe.Wahr(m.Klick is { EigeneAn: true, AndereAn: true, EigeneDb: -12f, AndereDb: -15f }, "ab Werk wie FunkKlickEinstellungen");
        Pruefe.Falsch(m.KlickAnhoeren(), "ohne laufende Wiedergabe spielt nichts");
        await s.StartenAsync();
        s.Stille(5);
        s.Mitschreiben = true;

        m.SprechtasteGedrueckt();
        s.Stille(25);
        PruefeWert.Nahe(-37, KlickPegel(s.Ausgabe), 0.1, "eigene Sprechtaste: Klick ab Werk");
        s.Ausgabe.Clear();
        m.SprechtasteLosgelassen();
        s.Stille(25);
        PruefeWert.Nahe(-37, KlickPegel(s.Ausgabe), 0.1, "Loslassen");

        m.Klick = new FunkKlickEinstellungen { EigeneDb = -20f };
        Pruefe.Wahr(m.Klick is { EigeneDb: -20f }, "neue Einstellung gilt");
        s.Ausgabe.Clear();
        m.SprechtasteGedrueckt();
        s.Stille(25);
        PruefeWert.Nahe(-45, KlickPegel(s.Ausgabe), 0.1, "leiser eingestellt");
        m.SprechtasteLosgelassen();
        s.Stille(25);

        m.Klick = new FunkKlickEinstellungen { EigeneAn = false };
        s.Ausgabe.Clear();
        m.SprechtasteGedrueckt();
        s.Stille(10);
        m.SprechtasteLosgelassen();
        s.Stille(25);
        Pruefe.Gleich(Pegel.MinDbfs, KlickPegel(s.Ausgabe), "eigener Klick aus: still");

        s.Ausgabe.Clear();
        Pruefe.Wahr(m.KlickAnhoeren(), "Anhören bei laufender Wiedergabe");
        s.Stille(150);
        PruefeWert.Nahe(-40, KlickPegel(s.Ausgabe), 0.1, "Anhören spielt die anderen Sprecher, weil die eigene Taste aus ist");

        await m.StoppenAsync();
        Pruefe.Falsch(m.KlickAnhoeren(), "nach dem Stoppen nicht mehr");
        Pruefe.Wirft<ArgumentNullException>(() => m.Klick = null!, "null");
    }

    /// <summary>
    /// Sarevoc spielt über Lautsprecher (Echounterdrückung an, A14), und seit 0.5.0 sind die Klicks anderer Sprecher lauter (A63) und mehr
    /// Netze auf Sprachaktivierung (A62). Ein anderer Funker spricht 14 kurze Sprüche mit der Taste, je mit Beginn und Ende, also 28
    /// Klicks in voller Lautstärke der anderen; Stimme und Klicks kommen über die Lautsprecher 40 ms später ins Mikrofon. Der Nutzer
    /// schweigt (Grundrauschen -65 dBFS). Seine Sprachaktivierung darf nach dem Einschwingen (5 s) nicht öffnen: mit 3 dB, 12 dB
    /// und verzerrender Kopplung, mit dem Klick so leise wie bis 0.4.1 (hier -23 dB), ab Werk (-15 dB) und ganz laut (-6 dB).
    /// <para>
    /// Seit Stufe 2b ist der Klick ein Rauschen, jedes Mal ein anderes; beim verzerrenden Lautsprecher, dem knappsten Fall, läuft
    /// die Szene darum mit drei Startwerten. Mit dem Rauschen öffnete das Gate dort zuerst in 7 von 18 Läufen, und ganz ohne Klick
    /// einmal: Der Echoschutz lernte die Kopplung auch in seiner Haltezeit nach unten (behoben in <see cref="Echoschutz"/>).
    /// </para>
    /// </summary>
    [Test]
    public static async Task Klick_AndererSprecherUeberLautsprecher_LoestSprachaktivierungNichtAus()
    {
        var zeilen = new List<string>();
        foreach (var (name, pfad, startwerte) in new (string, Func<float, float>, int[])[]
                 {
                     ("3 dB", KlangMaschineTests.Kopplung3Db, [2026]), ("12 dB", KlangMaschineTests.Kopplung12Db, [2026]),
                     ("verzerrt", KlangMaschineTests.KopplungVerzerrt, [2026, 1, 5]),
                 })
        {
            foreach (var (was, db) in new[] { ("wie bis 0.4.1", -23f), ("ab Werk", FunkKlickEinstellungen.AndereVoreinstellungDb), ("ganz laut", FunkKlickEinstellungen.MaxDb) })
            {
                foreach (var startwert in startwerte)
                {
                    var (fehl, klicks) = await LautsprecherLauf(pfad, db, startwert);
                    zeilen.Add($"{name}, {was}, Startwert {startwert}: {fehl}");
                    PruefeWert.Mindestens(28, klicks, $"{name}, {was}: die Klicks kamen in die Wiedergabe");
                    Pruefe.Gleich(0, fehl, $"{name}, Klick {was} ({db} dB), Startwert {startwert}: keine eigene Phase durch Klick oder Echo");
                }
            }
        }
        Console.WriteLine("  Klicks anderer über Lautsprecher, eigene Phasen ohne eigene Rede: " + string.Join("; ", zeilen));
    }

    /// <summary>35 s Prüfstand wie oben; Rückgabe: eigene Phasen ab 5 s und wie viele Klicks die Wiedergabe enthielt.</summary>
    private static async Task<(int Fehlphasen, int Klicks)> LautsprecherLauf(Func<float, float> pfad, float andereDb, int startwert)
    {
        const int Phasen = 14;
        const int PhaseTakte = 100;
        const int AbstandTakte = 250;
        await using var s = new Pruefstand(klickStartwert: startwert);
        s.Maschine.Echounterdrueckung = true;
        s.Maschine.Klick = new FunkKlickEinstellungen { AndereDb = andereDb };
        await s.StartenAsync();
        var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
        s.Echo = (wiedergabe, mikrofon) =>
        {
            for (var i = 0; i < mikrofon.Length; i++)
            {
                linie.Enqueue(pfad(0.5f * (wiedergabe[2 * i] + wiedergabe[2 * i + 1])));
                mikrofon[i] += linie.Dequeue();
            }
        };
        var (fern, _) = Testszenen.Sprache(Phasen * AbstandTakte / 100.0 + 2, 71);
        using var kodierer = new Funk.Klang.Kern.OpusKodierer();
        var puffer = new byte[Funk.Klang.Kern.OpusKodierer.MaxPaketBytes];
        // Je Phase: 1 s Rede in 20-ms-Paketen ab dem Takt k * 250 + 300 (die erste nach 3 s), dann dreimal ENDE wie beim Sender.
        var pakete = new List<(long Takt, ushort Phase, uint Frame, Sprachkennzeichen Kennzeichen, byte[] Daten)>();
        for (var k = 0; k < Phasen; k++)
        {
            var start = k * AbstandTakte + 300;
            uint frame = 0;
            for (var t = 0; t < PhaseTakte; t += 2, frame++)
            {
                var n = kodierer.Kodieren(fern.AsSpan((start + t) * 480, 960), puffer);
                pakete.Add((start + t, (ushort)(10 + k), frame, frame == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine, puffer[..n]));
            }
            for (var e = 0; e < 3; e++) pakete.Add((start + PhaseTakte, (ushort)(10 + k), frame + (uint)e, Sprachkennzeichen.Ende, []));
        }
        var nah = AnbindungSignale.Rauschen((Phasen * AbstandTakte + 500) * 480, -65, 72);
        var naechstes = 0;
        s.Abspielen(nah, takt =>
        {
            while (naechstes < pakete.Count && pakete[naechstes].Takt <= takt)
            {
                var p = pakete[naechstes++];
                _ = s.Maschine.SpracheEmpfangen(77, 0, p.Phase, p.Frame, p.Kennzeichen, p.Daten, s.Uhr.Us);
            }
        });
        var eigene = s.Pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0 && p.Takt >= 500);
        return (eigene, (int)s.Maschine.Mischer.Klick!.Gestartet);
    }

    /// <summary>
    /// Hörproben des eingebauten Klicks (seit Stufe 2b rosa Rauschen) aus der echten Klangmaschine im Prüfstand, nichts über ein
    /// Gerät: eigene Taste, andere Sprecher mit Taste, andere Sprecher per Sprachaktivierung (dezent), je dreimal drücken und
    /// loslassen, in der Voreinstellung. Jeder Einsatz liegt über 200 ms bei seinem Pegel unter der Sprache (-37, -40 und -58 dBFS),
    /// und keiner gleicht dem davor (jedes Mal ein neues Rauschen). Mit <c>FUNK_HOERPROBEN</c> (Ordner unter C:\Claude) entstehen
    /// daraus WAV-Dateien, 48 kHz, 16 Bit, Stereo, so wie die Wiedergabe sie bekommt.
    /// </summary>
    [Test]
    public static async Task Klick_Hoerproben_AusDerMaschine()
    {
        foreach (var (name, art, pegel) in new[] { ("eigene-taste", 0, -37f), ("andere-sprecher", 1, -40f), ("dezent", 2, -58f) })
        {
            await using var s = new Pruefstand(mitApm: false, mitRnnoise: false);
            await s.StartenAsync();
            s.Stille(20);
            s.Mitschreiben = true;
            if (art == 0)
            {
                for (var k = 0; k < 3; k++)
                {
                    s.Stille(40);
                    s.Maschine.SprechtasteGedrueckt();
                    s.Stille(60);
                    s.Maschine.SprechtasteLosgelassen();
                    s.Stille(40);
                }
            }
            else
            {
                // Je Phase 0,6 s Stille in 20-ms-Paketen, dann dreimal ENDE wie beim Sender; dezent mit dem Kennzeichen der Sprachaktivierung.
                var va = art == 2 ? Sprachkennzeichen.Sprachaktivierung : Sprachkennzeichen.Keine;
                using var kodierer = new Funk.Klang.Kern.OpusKodierer();
                var puffer = new byte[Funk.Klang.Kern.OpusKodierer.MaxPaketBytes];
                var stille = new float[960];
                var pakete = new List<(long Takt, ushort Phase, uint Frame, Sprachkennzeichen Kennzeichen, byte[] Daten)>();
                for (var k = 0; k < 3; k++)
                {
                    var start = 40 + (k * 140);
                    uint frame = 0;
                    for (var t = 0; t < 60; t += 2, frame++)
                    {
                        var n = kodierer.Kodieren(stille, puffer);
                        pakete.Add((start + t, (ushort)(20 + k), frame, va | (frame == 0 ? Sprachkennzeichen.Beginn : Sprachkennzeichen.Keine), puffer[..n]));
                    }
                    for (var e = 0; e < 3; e++) pakete.Add((start + 60, (ushort)(20 + k), frame + (uint)e, va | Sprachkennzeichen.Ende, []));
                }
                var naechstes = 0;
                s.Abspielen(new float[460 * 480], takt =>
                {
                    while (naechstes < pakete.Count && pakete[naechstes].Takt <= takt)
                    {
                        var p = pakete[naechstes++];
                        _ = s.Maschine.SpracheEmpfangen(77, 0, p.Phase, p.Frame, p.Kennzeichen, p.Daten, s.Uhr.Us);
                    }
                });
            }
            s.Stille(30);
            var einsaetze = Einsaetze(Links(s.Ausgabe));
            Console.WriteLine($"  {name}: " + string.Join(", ", einsaetze.Select(e => $"{e.Pegel:0.00} dBFS")));
            Pruefe.Gleich(6, einsaetze.Count, name + ": dreimal drücken und loslassen");
            foreach (var e in einsaetze) PruefeWert.Nahe(pegel, e.Pegel, 0.2, $"{name}: über 200 ms {pegel} dBFS");
            for (var i = 2; i < einsaetze.Count; i++)
                PruefeWert.Hoechstens(0.5, Math.Abs(Aehnlich(einsaetze[i].Klang, einsaetze[i - 2].Klang)), $"{name}: Einsatz {i} ein neues Rauschen");
            if (Environment.GetEnvironmentVariable("FUNK_HOERPROBEN") is { Length: > 0 } ordner)
            {
                var ziel = Path.GetFullPath(ordner.Trim());
                if (!ziel.StartsWith(@"C:\Claude\", StringComparison.OrdinalIgnoreCase)) throw new PruefungException(@"FUNK_HOERPROBEN muss unter C:\Claude liegen: " + ziel);
                Directory.CreateDirectory(ziel);
                WavSchreiben(Path.Combine(ziel, "funkklick-" + name + ".wav"), s.Ausgabe);
            }
        }
    }

    /// <summary>Einsätze in Kanal 0 (auf mittig mono umgerechnet): Beginn nach 50 ms Ruhe, Pegel über 200 ms und die ersten 30 ms.</summary>
    private static List<(float Pegel, float[] Klang)> Einsaetze(float[] links)
    {
        var mono = links.Select(v => v / 0.70710678f).ToArray();
        var einsaetze = new List<(float, float[])>();
        var ruhe = int.MaxValue;
        for (var i = 0; i < mono.Length; i++)
        {
            if (MathF.Abs(mono[i]) <= 1e-6f)
            {
                ruhe = ruhe == int.MaxValue ? ruhe : ruhe + 1;
                continue;
            }
            if (ruhe >= 50 * 48)
            {
                var stueck = mono.AsSpan(i, Math.Min(9600, mono.Length - i));
                einsaetze.Add((FunkKlick.PegelUeber200Ms(stueck), stueck[..Math.Min(1440, stueck.Length)].ToArray()));
            }
            ruhe = 0;
        }
        return einsaetze;
    }

    private static double Aehnlich(float[] a, float[] b)
    {
        double ab = 0, aa = 0, bb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            ab += (double)a[i] * b[i];
            aa += (double)a[i] * a[i];
            bb += (double)b[i] * b[i];
        }
        return aa <= 0 || bb <= 0 ? 0 : ab / Math.Sqrt(aa * bb);
    }

    /// <summary>WAV, 48 kHz, 16 Bit, zwei Kanäle verschränkt.</summary>
    private static void WavSchreiben(string pfad, List<float> stereo)
    {
        using var datei = File.Create(pfad);
        using var w = new BinaryWriter(datei);
        var bytes = stereo.Count * 2;
        w.Write("RIFF"u8);
        w.Write(36 + bytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(48000);
        w.Write(48000 * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(bytes);
        foreach (var v in stereo) w.Write((short)Math.Clamp(MathF.Round(v * 32767f), -32767f, 32767f));
    }
}
