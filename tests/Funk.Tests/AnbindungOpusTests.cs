// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Funk.Klang.Kern;

namespace Funk.Tests;

/// <summary>Opus-Anbindung gegen die echte opus.dll (libopus 1.6.1, eigener Bau).</summary>
internal static class AnbindungOpusTests
{
    private const int R = OpusKodierer.RahmenSamples;

    private static byte[][] Kodieren(float[] signal, int verlustHinweis, OpusAnwendung anwendung = OpusAnwendung.Voip)
    {
        using var k = new OpusKodierer(anwendung);
        k.SetzeVerlustHinweis(verlustHinweis);
        var puffer = new byte[OpusKodierer.MaxPaketBytes];
        var pakete = new byte[signal.Length / R][];
        for (int f = 0; f < pakete.Length; f++)
        {
            int n = k.Kodieren(signal.AsSpan(f * R, R), puffer);
            pakete[f] = puffer.AsSpan(0, n).ToArray();
        }
        return pakete;
    }

    [Test]
    public static void Opus_Bibliotheken_EigenerBauAusDemProgrammordner()
    {
        NativeBibliotheken.Pruefen();
        Pruefe.Gleich("libopus 1.6.1", NativeBibliotheken.OpusVersion, "Opus-Version");
        Pruefe.Wahr(NativeBibliotheken.ApmVersion.Contains("webrtc-audio-processing 2.1", StringComparison.Ordinal), "APM-Version: " + NativeBibliotheken.ApmVersion);

        // Geladen wird aus dem Ordner der Assembly (DefaultDllImportSearchPaths), nicht aus PATH.
        using (new RnnoiseProzessor())
        using (new Resampler(48000, 16000))
        using (new EchoProzessor())
        using (new OpusDekodierer())
        using (new DeepFilterProzessor())
        {
            using var prozess = Process.GetCurrentProcess();
            var module = prozess.Modules.Cast<ProcessModule>().ToList();
            foreach (var dll in new[] { "opus.dll", "rnnoise.dll", "speexdsp_resampler.dll", "webrtc_apm.dll", "deepfilternet.dll" })
            {
                var m = module.FirstOrDefault(x => string.Equals(x.ModuleName, dll, StringComparison.OrdinalIgnoreCase));
                Pruefe.Wahr(m is not null, dll + " geladen");
                string soll = Path.Combine(AppContext.BaseDirectory, dll);
                Pruefe.Wahr(string.Equals(soll, m!.FileName, StringComparison.OrdinalIgnoreCase), $"{dll} aus {m.FileName}, erwartet {soll}");
            }
        }
    }

    [Test]
    public static void Anbindung_AlleImporte_NurAusDemAssemblyOrdner()
    {
        var erlaubt = new HashSet<string>(StringComparer.Ordinal) { "opus", "rnnoise", "speexdsp_resampler", "webrtc_apm", "deepfilternet" };
        var gesperrt = new HashSet<string>(StringComparer.Ordinal) { "rnnoise_model_from_filename", "rnnoise_model_from_file" };
        var einsprung = new HashSet<string>(StringComparer.Ordinal);
        var typen = typeof(OpusKodierer).Assembly.GetTypes().Where(t => t.Namespace == "Funk.Klang.Nativ");
        foreach (var typ in typen)
        foreach (var m in typ.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            var import = m.GetCustomAttribute<DllImportAttribute>();
            if (import is null) continue;
            string name = import.EntryPoint ?? m.Name;
            Pruefe.Wahr(erlaubt.Contains(import.Value), $"{name}: Bibliothek {import.Value}");
            Pruefe.Falsch(gesperrt.Contains(name), $"{name} ist gesperrt (BAUBERICHT)");
            Pruefe.Falsch(name.Contains("dred", StringComparison.Ordinal), $"{name}: DRED ist nicht gebaut");
            var pfad = m.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
            Pruefe.Wahr(pfad?.Paths == DllImportSearchPath.AssemblyDirectory, $"{name}: nur Assembly-Ordner");
            einsprung.Add(import.Value + ":" + name);
        }
        Pruefe.Gleich(52, einsprung.Count, "Anzahl nativer Einsprungpunkte (opus 16, rnnoise 5, speexdsp 14, apm 10 seit apm_c 1.1, deepfilternet 7 seit Klangstufe B)");
    }

    [Test]
    public static void Opus_Kodierer_EinstellungenLautEntwurf()
    {
        using var k = new OpusKodierer();
        var e = k.EinstellungenLesen();
        Pruefe.Gleich(2048, e.Anwendung, "Anwendung VOIP");
        Pruefe.Gleich(48000, e.Bitrate, "Bitrate");
        Pruefe.Wahr(e.Vbr, "VBR");
        Pruefe.Wahr(e.VbrBeschraenkt, "VBR constrained");
        Pruefe.Gleich(10, e.Komplexitaet, "Komplexität");
        Pruefe.Gleich(1105, e.MaxBandbreite, "Fullband");
        Pruefe.Gleich(1, e.InbandFec, "In-band-FEC");
        Pruefe.Gleich(0, e.VerlustHinweis, "Verlusthinweis");
        Pruefe.Falsch(e.Dtx, "DTX");
        Pruefe.Gleich(3001, e.Signal, "Signal VOICE");
        Pruefe.Gleich(5004, e.RahmenDauer, "20 ms");
        Pruefe.Gleich(312, e.Lookahead, "Lookahead");
        Pruefe.Gleich(e.Lookahead, k.Lookahead, "Lookahead-Eigenschaft");

        using var a = new OpusKodierer(OpusAnwendung.Audio, 64000);
        var ea = a.EinstellungenLesen();
        Pruefe.Gleich(2049, ea.Anwendung, "Anwendung AUDIO");
        Pruefe.Gleich(64000, ea.Bitrate, "Bitrate AUDIO");

        using var d = new OpusDekodierer();
        Pruefe.Gleich(7, d.Komplexitaet, "Decoder-Komplexität");
        Pruefe.Wahr(d.BweAktiv, "OSCE-BWE");
        Pruefe.Gleich(48000, d.Abtastrate, "Abtastrate");

        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new OpusKodierer((OpusAnwendung)2051).Dispose(), "Anwendung LOWDELAY");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new OpusKodierer(bitrate: 5999).Dispose(), "Bitrate zu klein");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => new OpusDekodierer(11).Dispose(), "Decoder-Komplexität 11");
    }

    [Test]
    public static void Opus_Verlusthinweis_WirktAbNaechstemRahmen()
    {
        using var k = new OpusKodierer();
        var pcm = AnbindungSignale.Sinus(R, 440, 0.3);
        var ziel = new byte[OpusKodierer.MaxPaketBytes];

        k.SetzeVerlustHinweis(15);
        Pruefe.Gleich(0, k.EinstellungenLesen().VerlustHinweis, "vor dem Rahmen noch alt");
        k.Kodieren(pcm, ziel);
        Pruefe.Gleich(15, k.EinstellungenLesen().VerlustHinweis, "nach dem Rahmen neu");

        k.SetzeVerlustHinweis(150);
        k.Kodieren(pcm, ziel);
        Pruefe.Gleich(100, k.EinstellungenLesen().VerlustHinweis, "oben begrenzt");
        k.SetzeVerlustHinweis(-5);
        k.Kodieren(pcm, ziel);
        Pruefe.Gleich(0, k.EinstellungenLesen().VerlustHinweis, "unten begrenzt");

        k.SetzeBitrate(32000);
        k.Kodieren(pcm, ziel);
        Pruefe.Gleich(32000, k.EinstellungenLesen().Bitrate, "Bitrate umgestellt");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => k.SetzeBitrate(1000), "Bitrate außerhalb");
    }

    [Test]
    public static void Opus_Rundreise_Sinus_PegelUndKorrelation()
    {
        var sinus = AnbindungSignale.Sinus(AnbindungSignale.Fs, 440, 0.5);
        using var k = new OpusKodierer();
        using var d = new OpusDekodierer();
        var aus = new float[sinus.Length];
        var paket = new byte[OpusKodierer.MaxPaketBytes];
        for (int f = 0; f < sinus.Length; f += R)
        {
            int n = k.Kodieren(sinus.AsSpan(f, R), paket);
            Pruefe.Gleich(R, d.Dekodieren(paket.AsSpan(0, n), aus.AsSpan(f, R)), "Samples je Paket");
        }
        // Ab 100 ms, 800 ms lang, um den Lookahead des Kodierers versetzt
        int start = 4800, laenge = 38400, versatz = k.Lookahead;
        double korr = AnbindungSignale.Korrelation(sinus.AsSpan(start, laenge), aus.AsSpan(start + versatz, laenge));
        double pegel = AnbindungSignale.PegelDb(aus.AsSpan(start + versatz, laenge)) - AnbindungSignale.PegelDb(sinus.AsSpan(start, laenge));
        Pruefe.Wahr(korr > 0.99, $"Korrelation Sinus {korr:0.0000}");
        Pruefe.Wahr(Math.Abs(pegel) < 1.0, $"Pegelabweichung Sinus {pegel:0.00} dB");
        Pruefe.Gleich(50L, d.DekodiertePakete, "dekodierte Pakete");
        Pruefe.Gleich(0L, d.UngueltigePakete, "ungültige Pakete");
    }

    [Test]
    public static void Opus_Rundreise_Sprache_VerzugGleichLookahead()
    {
        var sprache = AnbindungSignale.Sprache(AnbindungSignale.Fs * 2, 31);
        var pakete = Kodieren(sprache, 0);
        using var d = new OpusDekodierer();
        var aus = new float[sprache.Length];
        for (int f = 0; f < pakete.Length; f++) d.Dekodieren(pakete[f], aus.AsSpan(f * R, R));

        var (verzug, korr) = AnbindungSignale.BesterVerzug(sprache, aus, 4800, 24000, 600);
        Pruefe.Wahr(Math.Abs(verzug - 312) <= 4, $"Verzug {verzug}, erwartet Lookahead 312");
        Pruefe.Wahr(korr > 0.95, $"Korrelation Sprache {korr:0.000}");
        double pegel = AnbindungSignale.PegelDb(aus.AsSpan(4800 + verzug, 72000)) - AnbindungSignale.PegelDb(sprache.AsSpan(4800, 72000));
        Pruefe.Wahr(Math.Abs(pegel) < 1.5, $"Pegelabweichung Sprache {pegel:0.00} dB");
    }

    [Test]
    public static void Opus_Toc_TabelleStimmtMitLibopus()
    {
        Span<byte> paket = stackalloc byte[1];
        for (int konfiguration = 0; konfiguration < 32; konfiguration++)
        {
            paket[0] = (byte)(konfiguration << 3);
            var toc = OpusPaket.Toc(paket);
            Pruefe.Gleich(konfiguration, toc.Konfiguration, "Konfiguration");
            Pruefe.Gleich(OpusPaket.BandbreiteNativ(paket), toc.Bandbreite, $"Bandbreite Konfiguration {konfiguration}");
            Pruefe.Gleich(OpusPaket.SamplesJeRahmenNativ(paket), toc.SamplesJeRahmen, $"Rahmendauer Konfiguration {konfiguration}");
        }
        var hybridVoll = new OpusToc(0x78);
        Pruefe.Gleich(OpusModus.Hybrid, hybridVoll.Modus, "0x78 Hybrid");
        Pruefe.Gleich(OpusBandbreite.Voll, hybridVoll.Bandbreite, "0x78 Fullband");
        Pruefe.Gleich(960, hybridVoll.SamplesJeRahmen, "0x78 20 ms");
        Pruefe.Wahr(new OpusToc(0x7C).Stereo, "Stereo-Bit");
        Pruefe.Gleich(3, new OpusToc(0x7B).RahmenCode, "Code 3");
    }

    [Test]
    public static void Opus_Toc_48kbit_HybridFullband20ms()
    {
        var sprache = AnbindungSignale.Sprache(AnbindungSignale.Fs * 2, 7);
        foreach (var anwendung in new[] { OpusAnwendung.Voip, OpusAnwendung.Audio })
        {
            var pakete = Kodieren(sprache, 10, anwendung);
            int mitLbrr = 0;
            foreach (var p in pakete)
            {
                var toc = OpusPaket.Toc(p);
                Pruefe.Gleich(OpusModus.Hybrid, toc.Modus, $"{anwendung}: Modus");
                Pruefe.Gleich(OpusBandbreite.Voll, toc.Bandbreite, $"{anwendung}: Bandbreite");
                Pruefe.Gleich(960, toc.SamplesJeRahmen, $"{anwendung}: Rahmendauer");
                Pruefe.Falsch(toc.Stereo, $"{anwendung}: mono");
                Pruefe.Gleich(0, toc.RahmenCode, $"{anwendung}: ein Rahmen");
                Pruefe.Gleich(1, OpusPaket.AnzahlRahmen(p), $"{anwendung}: Rahmen laut libopus");
                Pruefe.Gleich(960, OpusPaket.AnzahlSamples(p), $"{anwendung}: Samples laut libopus");
                if (OpusPaket.HatLbrr(p)) mitLbrr++;
            }
            // Pausen und stimmlose Stellen tragen kein LBRR; gemessen 72 % der Pakete.
            Pruefe.Wahr(mitLbrr > pakete.Length / 2, $"{anwendung}: LBRR in {mitLbrr} von {pakete.Length}");
        }
        foreach (var p in Kodieren(sprache, 0)) Pruefe.Falsch(OpusPaket.HatLbrr(p), "Verlusthinweis 0: kein LBRR");
    }

    [Test]
    public static void Opus_Fec_VerlorenesPaket_LiefertSignal()
    {
        var sprache = AnbindungSignale.Sprache(AnbindungSignale.Fs * 4, 11);
        var pakete = Kodieren(sprache, 20);
        var referenz = new float[sprache.Length];
        using (var d = new OpusDekodierer())
            for (int f = 0; f < pakete.Length; f++) d.Dekodieren(pakete[f], referenz.AsSpan(f * R, R));

        int gezaehlt = 0;
        double summeFec = 0, summePlc = 0;
        for (int verloren = 10; verloren < pakete.Length - 1; verloren += 7)
        {
            var refRahmen = referenz.AsSpan(verloren * R, R);
            if (!OpusPaket.HatLbrr(pakete[verloren + 1]) || AnbindungSignale.PegelDb(refRahmen) < -35) continue;

            var mitFec = new float[R];
            var mitPlc = new float[R];
            using var dF = new OpusDekodierer();
            using var dP = new OpusDekodierer();
            var weg = new float[R];
            for (int f = 0; f < verloren; f++)
            {
                dF.Dekodieren(pakete[f], weg);
                dP.Dekodieren(pakete[f], weg);
            }
            Pruefe.Gleich(R, dF.DekodierenFec(pakete[verloren + 1], mitFec, R), "FEC-Samples");
            Pruefe.Gleich(R, dP.Verdecken(mitPlc, R), "PLC-Samples");
            Pruefe.Gleich(1L, dF.FecMitLbrr, "FEC mit LBRR gezählt");

            double korr = AnbindungSignale.Korrelation(refRahmen, mitFec);
            double pegel = AnbindungSignale.PegelDb(mitFec) - AnbindungSignale.PegelDb(refRahmen);
            Pruefe.Wahr(korr > 0.8, $"Rahmen {verloren}: FEC-Korrelation {korr:0.000}");
            Pruefe.Wahr(Math.Abs(pegel) < 6, $"Rahmen {verloren}: FEC-Pegel {pegel:0.0} dB");
            summeFec += AnbindungSignale.AbstandDb(refRahmen, mitFec);
            summePlc += AnbindungSignale.AbstandDb(refRahmen, mitPlc);
            gezaehlt++;
        }
        Pruefe.Wahr(gezaehlt >= 8, $"genug Verlustrahmen mit LBRR: {gezaehlt}");
        double vorteil = (summeFec - summePlc) / gezaehlt;
        Pruefe.Wahr(vorteil > 6, $"FEC im Mittel {vorteil:0.0} dB näher an der Referenz als PLC");
    }

    [Test]
    public static void Opus_Fec_OhneLbrr_VerdecktSelbst()
    {
        var pakete = Kodieren(AnbindungSignale.Sprache(AnbindungSignale.Fs, 12), 0);
        using var d = new OpusDekodierer();
        var ziel = new float[R];
        for (int f = 0; f < 20; f++) d.Dekodieren(pakete[f], ziel);
        Pruefe.Gleich(R, d.DekodierenFec(pakete[21], ziel, R), "Samples ohne LBRR");
        Pruefe.Gleich(1L, d.FecVersuche, "FEC-Versuch gezählt");
        Pruefe.Gleich(0L, d.FecMitLbrr, "kein LBRR");
        Pruefe.Wahr(ziel.All(float.IsFinite), "endliche Werte");
    }

    [Test]
    public static void Opus_Plc_LiefertSamples()
    {
        var sinus = AnbindungSignale.Sinus(AnbindungSignale.Fs, 300, 0.4);
        var pakete = Kodieren(sinus, 0);
        using var d = new OpusDekodierer();
        var ziel = new float[OpusDekodierer.MaxSamples];
        for (int f = 0; f < 25; f++) d.Dekodieren(pakete[f], ziel);

        Pruefe.Gleich(960, d.Verdecken(ziel, 960), "PLC 20 ms");
        Pruefe.Wahr(ziel.AsSpan(0, 960).ToArray().All(float.IsFinite), "PLC endlich");
        double pegel = AnbindungSignale.PegelDb(ziel.AsSpan(0, 960));
        Pruefe.Wahr(pegel > -30, $"PLC setzt den Ton fort: {pegel:0.0} dBFS");
        Pruefe.Gleich(960, d.LetzteDauer, "letzte Dauer");
        Pruefe.Gleich(480, d.Verdecken(ziel, 480), "PLC 10 ms");
        Pruefe.Gleich(120, d.Verdecken(ziel, 120), "PLC 2,5 ms");
        Pruefe.Gleich(5760, d.Verdecken(ziel, 5760), "PLC 120 ms");
        Pruefe.Gleich(4L, d.Verdeckungen, "Verdeckungen gezählt");
        Pruefe.Gleich(960, d.Dekodieren(pakete[25], ziel), "danach normal weiter");

        d.Zuruecksetzen();
        Pruefe.Gleich(960, d.Dekodieren(pakete[26], ziel), "nach Zurücksetzen");
    }

    [Test]
    public static void Opus_UngueltigesPaket_WirdVerdecktNichtGeworfen()
    {
        using var d = new OpusDekodierer();
        var ziel = new float[R];
        // TOC 0x7B: Code 3 mit Rahmenzahl 0 ist nach RFC 6716 ungültig.
        Pruefe.Gleich(R, d.Dekodieren([0x7B, 0x00], ziel), "kaputtes Paket: 20 ms Ersatz");
        Pruefe.Gleich(1L, d.UngueltigePakete, "ungültig gezählt");
        Pruefe.Wahr(d.LetzterFehler < 0, "Fehlercode gemerkt");
        Pruefe.Gleich(R, d.Dekodieren([], ziel), "leeres Paket: 20 ms Ersatz");
        Pruefe.Gleich(2L, d.UngueltigePakete, "leer gezählt");
        Pruefe.Gleich(2L, d.Verdeckungen, "beide verdeckt");
        Pruefe.Gleich(0L, d.DekodiertePakete, "nichts normal dekodiert");
        Pruefe.Gleich(-1, OpusPaket.AnzahlSamples([]), "AnzahlSamples leer: OPUS_BAD_ARG");
        Pruefe.Falsch(OpusPaket.HatLbrr([]), "HatLbrr leer");
    }

    [Test]
    public static void Opus_Laengen_WerfenVorDemNativenAufruf()
    {
        using var k = new OpusKodierer();
        var ziel = new byte[OpusKodierer.MaxPaketBytes];
        Pruefe.Wirft<ArgumentException>(() => k.Kodieren(new float[959], ziel), "959 Samples");
        Pruefe.Wirft<ArgumentException>(() => k.Kodieren(new float[961], ziel), "961 Samples");
        Pruefe.Wirft<ArgumentException>(() => k.Kodieren(new float[480], ziel), "10 ms");
        Pruefe.Wirft<ArgumentException>(() => k.Kodieren(new float[960], []), "leeres Ziel");
        Pruefe.Gleich(0L, k.KodierteRahmen, "Kodierer nie nativ aufgerufen");

        using var d = new OpusDekodierer();
        var paket = new byte[] { 0x78, 0x00, 0x00 };
        Pruefe.Wirft<ArgumentException>(() => d.Dekodieren(paket, new float[959]), "Dekodieren Ziel 959");
        Pruefe.Wirft<ArgumentException>(() => d.DekodierenFec(paket, new float[960], 100), "FEC 100 kein Vielfaches von 120");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => d.DekodierenFec(paket, new float[960], 0), "FEC 0");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => d.DekodierenFec(paket, new float[960], -120), "FEC negativ");
        Pruefe.Wirft<ArgumentException>(() => d.DekodierenFec(paket, new float[960], 1920), "FEC länger als Ziel");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => d.DekodierenFec(paket, new float[6000], 5880), "FEC über 120 ms");
        Pruefe.Wirft<ArgumentException>(() => d.Verdecken(new float[960], 1000), "PLC 1000");
        Pruefe.Wirft<ArgumentException>(() => d.Verdecken(new float[480], 960), "PLC länger als Ziel");
        Pruefe.Gleich(0L, d.DekodiertePakete + d.FecVersuche + d.Verdeckungen + d.UngueltigePakete, "Dekodierer nie nativ aufgerufen");

        Pruefe.Wirft<ArgumentException>(() => OpusPaket.Toc([]), "TOC leer");
        Pruefe.Wirft<ArgumentException>(() => OpusPaket.BandbreiteNativ([]), "Bandbreite leer (libopus liest blind data[0])");
        Pruefe.Wirft<ArgumentException>(() => new OpusSoftClip(2).Anwenden(new float[3]), "Soft-Clip stereo ungerade");
    }

    [Test]
    public static void Opus_NachDispose_KeinNativerZugriff()
    {
        var k = new OpusKodierer();
        k.Dispose();
        Pruefe.Wirft<ObjectDisposedException>(() => k.Kodieren(new float[960], new byte[OpusKodierer.MaxPaketBytes]), "Kodieren nach Dispose");
        var d = new OpusDekodierer();
        d.Dispose();
        Pruefe.Wirft<ObjectDisposedException>(() => d.Verdecken(new float[960], 960), "Verdecken nach Dispose");
        k.Dispose();
        d.Dispose();
    }

    [Test]
    public static void Opus_SoftClip_BegrenztWeich()
    {
        var laut = AnbindungSignale.Sinus(960, 440, 1.8);
        var clip = new OpusSoftClip();
        clip.Anwenden(laut);
        float spitze = laut.Max(v => Math.Abs(v));
        Pruefe.Wahr(spitze <= 1f && spitze > 0.9f, $"Spitze {spitze:0.000}");

        var leise = AnbindungSignale.Sinus(960, 440, 0.5);
        var kopie = (float[])leise.Clone();
        new OpusSoftClip().Anwenden(leise);
        Pruefe.Wahr(leise.AsSpan().SequenceEqual(kopie), "unter 1 bleibt unverändert");

        var stereo = new float[1920];
        for (int i = 0; i < 960; i++) { stereo[2 * i] = laut[i] * 1.5f; stereo[2 * i + 1] = 0.25f; }
        new OpusSoftClip(2).Anwenden(stereo);
        Pruefe.Wahr(stereo.All(v => Math.Abs(v) <= 1f), "stereo begrenzt");
        Pruefe.Gleich(0.25f, stereo[1], "rechter Kanal unberührt");
    }
}
