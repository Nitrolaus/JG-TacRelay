// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Hörprobe nach Sarevocs Fehlerbericht vom 01.10.2026 (Client 0.4.0, Tischmikrofon RØDE VideoMic GO II, Realtek-Lautsprecher,
/// Echounterdrückung an): "Ich glaube, die Hörprobe geht nicht, ich höre mich bei Aktivierung nicht."
/// <para>
/// Ursache: Die Hörprobe bis 0.4.0 war ein Mithören dessen, was die Sendekette hinausgibt (Sendekette.PaketeAusgeben), also
/// nur bei offenem Gate. Das Gate öffnet mit der Sprechtaste oder, wenn alle gewählten Sendenetze auf Sprachaktivierung
/// stehen (A10), über die Schwelle. Auf einem echten Server steht ein Netz ohne eigene Einstellung auf Taste
/// (<c>Netz.AusloeserStandard</c>), Global immer; ohne Verbindung gibt es kein Sendenetz. Wer dann die Hörprobe einschaltet und
/// spricht, hört nichts. Im Probefunk steht das Netz Echo auf VA, dort fiel es nie auf.
/// </para>
/// <para>
/// Über Lautsprecher ist Mithören beim Senden keine Lösung: Die eigene Stimme kommt um Puffer und Laufzeit verzögert zurück ins
/// Mikrofon. Mit Echounterdrückung bremst der Echoschutz dann die eigenen Anlaute, ohne kreist die Stimme
/// (<see cref="Mithoeren_UeberLautsprecher_SchadetDemSenden_Begruendung"/>). Deshalb gibt es die Hörprobe als Aufnahme: erst
/// aufnehmen, dann abspielen, nie beides zugleich.
/// </para>
/// Prüfstand mit Geräte-Attrappen, echter Kette (APM, RNNoise, Opus) und simulierter Uhr; keine echten Geräte.
/// </summary>
internal static class HoerprobeTests
{
    private const int TakteJeProbe = IKlangMaschine.ProbeDauerMs / 10;

    /// <summary>Werte der Wiedergabe je 10-ms-Takt (48 kHz Stereo im Prüfstand).</summary>
    private const int WerteJeTakt = 960;

    /// <summary>Lautsprecher: Die Wiedergabe kommt über <paramref name="pfad"/> und 40 ms später ins Mikrofon.</summary>
    private static void Lautsprecher(Pruefstand s, Func<float, float> pfad)
    {
        var linie = new Queue<float>(Enumerable.Repeat(0f, 40 * 48));
        s.Echo = (wiedergabe, mikrofon) =>
        {
            for (var i = 0; i < mikrofon.Length; i++)
            {
                linie.Enqueue(pfad(0.5f * (wiedergabe[2 * i] + wiedergabe[2 * i + 1])));
                mikrofon[i] += linie.Dequeue();
            }
        };
    }

    /// <summary>Pegel je 10 ms (linker Kanal) der mitgeschriebenen Wiedergabe von Wert <paramref name="von"/> bis <paramref name="bis"/>.</summary>
    private static float[] Bloecke(Pruefstand s, int von, int bis)
    {
        var ausgabe = CollectionsMarshal.AsSpan(s.Ausgabe);
        var n = (bis - von) / WerteJeTakt;
        var links = new float[480];
        var pegel = new float[n];
        for (var b = 0; b < n; b++)
        {
            var block = ausgabe.Slice(von + b * WerteJeTakt, WerteJeTakt);
            for (var i = 0; i < 480; i++) links[i] = block[2 * i];
            pegel[b] = Db(links);
        }
        return pegel;
    }

    /// <summary>Pegel je 10 ms eines Mono-Signals mit 48 kHz.</summary>
    private static float[] Bloecke(ReadOnlySpan<float> mono)
    {
        var pegel = new float[mono.Length / 480];
        for (var b = 0; b < pegel.Length; b++) pegel[b] = Db(mono.Slice(b * 480, 480));
        return pegel;
    }

    private static float Db(ReadOnlySpan<float> x)
    {
        double summe = 0;
        foreach (var v in x) summe += (double)v * v;
        return (float)(10 * Math.Log10(Math.Max(1e-12, summe / x.Length)));
    }

    /// <summary>
    /// Wie gut folgt <paramref name="gehoert"/> dem <paramref name="gesprochen"/> (Sprache und Pausen, je 10 ms über bzw. unter
    /// <paramref name="abDb"/>): Anteil gleicher Blöcke beim besten Versatz von 0 bis 30 Blöcken, und dieser Versatz.
    /// </summary>
    private static (int Versatz, double Deckung) BesteDeckung(float[] gesprochen, float[] gehoert, float abDb)
    {
        var besterVersatz = 0;
        var beste = 0.0;
        for (var v = 0; v <= 30; v++)
        {
            var gleich = 0;
            for (var t = 0; t < gesprochen.Length; t++)
            {
                var g = t + v < gehoert.Length && gehoert[t + v] > abDb;
                if ((gesprochen[t] > abDb) == g) gleich++;
            }
            var deckung = gleich / (double)gesprochen.Length;
            if (deckung > beste)
            {
                beste = deckung;
                besterVersatz = v;
            }
        }
        return (besterVersatz, beste);
    }

    /// <summary>Die Pakete bilden vollständige Phasen: jede beginnt mit BEGINN, endet mit ENDE, kein Paket dazwischen ohne Phase.</summary>
    private static List<(bool Va, int Audio)> PhasenGanz(List<Gesendet> pakete)
    {
        var phasen = new List<(bool, int)>();
        var offen = false;
        var va = false;
        var audio = 0;
        foreach (var p in pakete)
        {
            if ((p.Kennzeichen & Sprachkennzeichen.Beginn) != 0)
            {
                Pruefe.Falsch(offen, "BEGINN in offener Phase");
                offen = true;
                va = (p.Kennzeichen & Sprachkennzeichen.Sprachaktivierung) != 0;
                audio = 0;
            }
            Pruefe.Wahr(offen, "Paket ohne BEGINN davor: " + p.Kennzeichen);
            if (p.Opus.Length > 0) audio++;
            if ((p.Kennzeichen & Sprachkennzeichen.Ende) != 0)
            {
                phasen.Add((va, audio));
                offen = false;
            }
        }
        Pruefe.Falsch(offen, "letzte Phase ohne ENDE");
        return phasen;
    }

    [Test]
    public static async Task Hoerprobe_MithoerenSpieltNurBeimSenden_UrsacheDesFehlerberichts()
    {
        // So stand es bei Sarevoc: Sendenetz auf Taste (Sprachaktivierung der Maschine aus), Mithören an, Lautsprecher mit
        // Echounterdrückung, gesprochen ohne Sprechtaste. Bis 0.4.0 hieß das Mithören "Hörprobe".
        await using var s = new Pruefstand { Mitschreiben = true };
        await s.StartenAsync();
        s.Maschine.Sprachaktivierung = false;
        s.Maschine.Hoerprobe = true;
        Lautsprecher(s, KlangMaschineTests.Kopplung12Db);
        s.Stille(50);
        var (sprache, _) = Testszenen.Sprache(8, 92);
        var von = s.Ausgabe.Count;
        s.Abspielen(sprache);
        s.Stille(30);
        var ohneTaste = Bloecke(s, von, s.Ausgabe.Count);
        Pruefe.Gleich(0, s.Pakete.Count, "ohne Sprechtaste gibt die Kette nichts heraus");
        PruefeWert.Hoechstens(-90, ohneTaste.Max(), "also bleibt das Mithören still: so hörte Sarevoc sich nicht");

        // Mit gehaltener Sprechtaste ist die Stimme da: Es lag am Senden, nicht an Geräten, Ausgang oder Pegel.
        s.Maschine.SprechtasteGedrueckt();
        von = s.Ausgabe.Count;
        s.Abspielen(sprache);
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(30);
        var mitTaste = Bloecke(s, von, s.Ausgabe.Count);
        PruefeWert.Mindestens(100, mitTaste.Count(p => p > -40f), "mit Sprechtaste hört man sich (Blöcke über -40 dBFS)");
    }

    [Test]
    public static async Task Probe_JusAufbau_LautsprecherMitEchounterdrueckungNetzAufTaste_SpieltDieStimmeDanachAb()
    {
        // Sarevocs Aufbau: Tischmikrofon und Lautsprecher (Kopplung 12 dB, 40 ms), Echounterdrückung an, Sendenetz auf Taste.
        await using var s = new Pruefstand { Mitschreiben = true };
        await s.StartenAsync();
        s.Maschine.Sprachaktivierung = false;
        Lautsprecher(s, KlangMaschineTests.Kopplung12Db);
        s.Stille(100);
        Pruefe.Gleich(new ProbeStand(ProbeZustand.Bereit, 0), s.Maschine.Probe, "bereit");

        s.Maschine.ProbeStarten();
        Pruefe.Gleich(new ProbeStand(ProbeZustand.Aufnahme, IKlangMaschine.ProbeDauerMs), s.Maschine.Probe, "nimmt ab dem nächsten Rahmen auf");
        var (sprache, _) = Testszenen.Sprache(IKlangMaschine.ProbeDauerMs / 1000.0, 93);
        Pruefe.Gleich(TakteJeProbe * 480, sprache.Length, "Szene so lang wie die Probe");
        var aufnahme = s.Ausgabe.Count;
        s.Abspielen(sprache.AsSpan(0, sprache.Length / 2));
        Pruefe.Gleich(new ProbeStand(ProbeZustand.Aufnahme, IKlangMaschine.ProbeDauerMs / 2), s.Maschine.Probe, "nach der Hälfte");
        s.Abspielen(sprache.AsSpan(sprache.Length / 2));
        var stand = s.Maschine.Probe;
        Pruefe.Wahr(stand.Zustand == ProbeZustand.Wiedergabe && stand.RestMs > IKlangMaschine.ProbeDauerMs - 100, "danach Wiedergabe: " + stand);
        var wiedergabe = s.Ausgabe.Count;
        s.Stille(TakteJeProbe + 50);
        Pruefe.Gleich(ProbeZustand.Bereit, s.Maschine.Probe.Zustand, "nach der Wiedergabe wieder bereit");
        var ende = s.Ausgabe.Count;
        s.Stille(300);

        var waehrendAufnahme = Bloecke(s, aufnahme, wiedergabe);
        var gehoert = Bloecke(s, wiedergabe, ende);
        var danach = Bloecke(s, ende + 30 * WerteJeTakt, s.Ausgabe.Count);
        var gesprochen = Bloecke(sprache);
        var (versatz, deckung) = BesteDeckung(gesprochen, gehoert, -45f);
        Console.WriteLine($"  Probe bei Sarevoc: Deckung {deckung:P1} bei {versatz * 10} ms Versatz, gesprochen {gesprochen.Count(p => p > -45f)} Blöcke, " +
            $"gehört {gehoert.Count(p => p > -45f)}, Spitze {gehoert.Max():F1} dBFS, Hörprobe {s.Maschine.Statistik.Hoerprobe.Normal} Pakete dekodiert");
        PruefeWert.Hoechstens(-90, waehrendAufnahme.Max(), "während Sarevoc spricht, spielt nichts aus den Lautsprechern");
        PruefeWert.Mindestens(0.9, deckung, "danach kommt seine Stimme: die Wiedergabe folgt Sprache und Pausen");
        PruefeWert.Hoechstens(15, versatz, "mit höchstens 150 ms Puffer");
        Pruefe.Gleich(0, s.Pakete.Count, "gesendet wurde nichts (Netz auf Taste, keine Sprechtaste)");
        PruefeWert.Hoechstens(-90, danach.Max(), "danach Ruhe: Das Echo der Wiedergabe wurde nicht wieder aufgenommen");
    }

    [Test]
    public static async Task Probe_LautsprecherOhneEchounterdrueckung_KeineSchleife_SprachaktivierungSendetNichts()
    {
        // Schlimmster Fall: Lautsprecher mit 3 dB Kopplung, Echounterdrückung aus, Sendenetz auf Sprachaktivierung mit niedriger
        // fester Schwelle. Das Echo der Wiedergabe liegt weit über der Schwelle; gesendet werden darf es trotzdem nicht, und
        // aufgenommen wird es nicht wieder (keine zweite Runde).
        await using var s = new Pruefstand { Mitschreiben = true };
        s.Maschine.Echounterdrueckung = false;
        await s.StartenAsync();
        s.Maschine.Gate = GateVorgaben.Standard with { Modus = GateModus.Fest, SchwelleDb = -50f };
        Lautsprecher(s, KlangMaschineTests.Kopplung3Db);
        s.Stille(100);
        var phasenVorher = s.Maschine.Statistik.Hoerprobe.Phasen;
        s.Maschine.ProbeStarten();
        var (sprache, _) = Testszenen.Sprache(IKlangMaschine.ProbeDauerMs / 1000.0, 95);
        s.Abspielen(sprache);
        var msWiedergabe = s.Uhr.Us / 1000;
        var verworfenAufnahme = s.Maschine.Kette.VaVerworfenePakete;
        s.Stille(TakteJeProbe + 20);
        var msEnde = s.Uhr.Us / 1000;
        var verworfenWiedergabe = s.Maschine.Kette.VaVerworfenePakete - verworfenAufnahme;
        Pruefe.Gleich(ProbeZustand.Bereit, s.Maschine.Probe.Zustand, "bereit");
        var ende = s.Ausgabe.Count;
        s.Stille(300);

        var echo = s.Meldungen.Count(m => m.ZeitMs > msWiedergabe && m.ZeitMs < msEnde && m.PegelDbfs > -50f);
        Console.WriteLine($"  Ohne Echounterdrückung: {echo} Meldungen mit Echo über der Schwelle, verworfen bei der Aufnahme " +
            $"{verworfenAufnahme} und beim Echo der Wiedergabe {verworfenWiedergabe} Pakete, gesendet {s.Pakete.Count}");
        PruefeWert.Mindestens(10, echo, "Gegenprobe: Das Echo der Wiedergabe lag über der Schwelle der Sprachaktivierung");
        PruefeWert.Mindestens(1, verworfenAufnahme, "Gegenprobe: Die eigene Stimme öffnete das Gate während der Aufnahme");
        PruefeWert.Mindestens(1, verworfenWiedergabe, "Gegenprobe: Das Echo der Wiedergabe öffnete das Gate");
        Pruefe.Gleich(0, s.Pakete.Count, "die Sprachaktivierung sendete weder die Probe noch ihr Echo");
        Pruefe.Gleich(1L, s.Maschine.Statistik.Hoerprobe.Phasen - phasenVorher, "genau eine Wiedergabe");
        PruefeWert.Hoechstens(-90, Bloecke(s, ende + 30 * WerteJeTakt, s.Ausgabe.Count).Max(), "danach Ruhe, keine Schleife");

        // Nach der Probe sendet die Sprachaktivierung wieder.
        var (nochmal, _) = Testszenen.Sprache(4, 96);
        s.Abspielen(nochmal.Select(v => v * 4f).ToArray());
        s.Stille(60);
        PruefeWert.Mindestens(1, PhasenGanz(s.Pakete).Count(p => p.Va), "danach wieder Phasen der Sprachaktivierung");
    }

    [Test]
    public static async Task Probe_SprachaktivierungSchweigt_SprechtasteSendet_OffenePhaseBleibtGanz()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        s.Maschine.Gate = GateVorgaben.Standard with { Modus = GateModus.Fest, SchwelleDb = -60f, Sprachsicherheit = 0f };
        s.Stille(100);
        var laut = KlangMaschineTests.Vokal(48000 * 20, 0.3f); // dauernd weit über -60 dBFS, RNNoise lässt ihn durch
        var pos = 0;
        void Sprechen(int takte)
        {
            s.Abspielen(laut.AsSpan(pos, takte * 480));
            pos += takte * 480;
        }

        // 1. Eine Phase der Sprachaktivierung läuft schon, als die Probe beginnt: Sie geht zu Ende wie sonst.
        Sprechen(50);
        Pruefe.Wahr(s.Pakete.Count > 0, "die Sprachaktivierung sendet");
        s.Maschine.ProbeStarten();
        Sprechen(30);
        s.Stille(60);
        var nachErster = s.Pakete.Count;
        Pruefe.Gleich(1, PhasenGanz(s.Pakete).Count, "die laufende Phase endet mit ENDE");

        // 2. Während der Probe öffnet die Sprachaktivierung keine neue Phase; die Anzeige sagt nicht SENDET.
        var msVon = s.Uhr.Us / 1000;
        Sprechen(80);
        s.Stille(40);
        var msBis = s.Uhr.Us / 1000;
        Pruefe.Gleich(nachErster, s.Pakete.Count, "keine neue Phase der Sprachaktivierung während der Probe");
        Pruefe.Falsch(s.Meldungen.Any(m => m.ZeitMs > msVon && m.ZeitMs <= msBis && m.Sendet), "Anzeige: Ruhe statt SENDET");

        // 3. Die Sprechtaste sendet auch während der Probe.
        s.Maschine.SprechtasteGedrueckt();
        Sprechen(50);
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(10);
        var phasen = PhasenGanz(s.Pakete);
        Pruefe.Gleich(2, phasen.Count, "dazu die Phase der Sprechtaste");
        Pruefe.Falsch(phasen[1].Va, "ohne VA-Kennzeichen");
        PruefeWert.Nahe(25, phasen[1].Audio, 2, "0,5 s Sprechtaste = 25 Pakete");

        // 4. Nach Aufnahme, Wiedergabe und Ausklang sendet die Sprachaktivierung wieder.
        while (s.Maschine.Probe.Zustand != ProbeZustand.Bereit) s.Stille(10);
        s.Stille(Sendekette.ProbeNachlaufRahmen);
        Sprechen(80);
        s.Stille(60);
        phasen = PhasenGanz(s.Pakete);
        Pruefe.Wahr(phasen.Count >= 3 && phasen[2].Va, "danach wieder Sprachaktivierung");
    }

    [Test]
    public static async Task Probe_AbbrechenNeustartUndStopp()
    {
        await using var s = new Pruefstand { Mitschreiben = true };
        await s.StartenAsync();
        s.Maschine.Sprachaktivierung = false;
        var sprache = KlangMaschineTests.Vokal(48000 * 12, 0.3f); // ohne Pausen: jede Stelle der Wiedergabe ist hörbar
        s.Stille(50);

        // Abbrechen während der Aufnahme: Es wird nichts abgespielt.
        s.Maschine.ProbeStarten();
        s.Abspielen(sprache.AsSpan(0, 100 * 480));
        s.Maschine.ProbeAbbrechen();
        Pruefe.Gleich(new ProbeStand(ProbeZustand.Bereit, 0), s.Maschine.Probe, "sofort bereit");
        var von = s.Ausgabe.Count;
        s.Stille(TakteJeProbe + 50);
        PruefeWert.Hoechstens(-90, Bloecke(s, von, s.Ausgabe.Count).Max(), "abgebrochene Aufnahme wird nicht abgespielt");

        // Abbrechen während der Wiedergabe: Sie hört gleich auf.
        s.Maschine.ProbeStarten();
        s.Abspielen(sprache.AsSpan(0, TakteJeProbe * 480));
        Pruefe.Gleich(ProbeZustand.Wiedergabe, s.Maschine.Probe.Zustand, "spielt ab");
        von = s.Ausgabe.Count;
        s.Stille(100);
        PruefeWert.Mindestens(20, Bloecke(s, von, s.Ausgabe.Count).Count(p => p > -45f), "Wiedergabe hörbar");
        s.Maschine.ProbeAbbrechen();
        Pruefe.Gleich(ProbeZustand.Bereit, s.Maschine.Probe.Zustand, "bereit");
        // Der Puffer spielt aus, was er hat (Ziel 80 ms), blendet aus, und 100 ms nach dem ENDE kommt der Funk-Klick wie
        // beim Empfänger; danach ist Ruhe.
        s.Stille(40);
        von = s.Ausgabe.Count;
        s.Stille(200);
        PruefeWert.Hoechstens(-90, Bloecke(s, von, s.Ausgabe.Count).Max(), "nach dem Abbrechen still");

        // Neustart während der Wiedergabe beginnt eine neue Aufnahme.
        s.Maschine.ProbeStarten();
        s.Abspielen(sprache.AsSpan(0, TakteJeProbe * 480));
        s.Stille(50);
        Pruefe.Gleich(ProbeZustand.Wiedergabe, s.Maschine.Probe.Zustand, "spielt ab");
        s.Maschine.ProbeStarten();
        s.Stille(2);
        Pruefe.Gleich(new ProbeStand(ProbeZustand.Aufnahme, IKlangMaschine.ProbeDauerMs - 20), s.Maschine.Probe, "nimmt neu auf");

        // Audio stoppen beendet die Probe.
        await s.Maschine.StoppenAsync();
        Pruefe.Gleich(new ProbeStand(ProbeZustand.Bereit, 0), s.Maschine.Probe, "nach dem Stoppen bereit");
    }

    [Test]
    public static async Task Probe_AudioPfade_AllokierenNicht()
    {
        await using var s = new Pruefstand();
        await s.StartenAsync();
        var (signal, _) = Testszenen.Sprache(12, 99);
        var wiedergabe = new float[WerteJeTakt];
        void Schritt(int t)
        {
            s.Uhr.Us += Pruefstand.TaktUs;
            s.System.Wiedergabe!.Ziehen(wiedergabe);
            s.System.Loopback!.Liefern(wiedergabe);
            s.System.Aufnahme!.Liefern(signal.AsSpan(t % 1100 * 480, 480));
        }
        // Aufwärmen mit einer ganzen Probe, dann eine zweite messen (samt Starten und Abbrechen aus diesem Thread).
        s.Maschine.ProbeStarten();
        for (var t = 0; t < 1100; t++)
        {
            Schritt(t);
            s.Maschine.Zustellen();
        }
        Pruefe.Gleich(ProbeZustand.Bereit, s.Maschine.Probe.Zustand, "erste Probe fertig");
        var bytes = PruefeWert.Allokiert(() =>
        {
            s.Maschine.ProbeStarten();
            for (var t = 0; t < 1100; t++) Schritt(t);
            s.Maschine.ProbeStarten();
            for (var t = 0; t < 700; t++) Schritt(t);
            s.Maschine.ProbeAbbrechen();
            for (var t = 0; t < 10; t++) Schritt(t);
        });
        s.Maschine.Zustellen();
        Console.WriteLine($"  Probe: {bytes} Byte in 18 s (Aufnahme, Wiedergabe, Abbrechen)");
        Pruefe.Gleich(0L, bytes, "allokierte Bytes in den Audio-Pfaden mit Probe");
        PruefeWert.Mindestens(2, s.Maschine.Statistik.Hoerprobe.Phasen, "beide Wiedergaben liefen durch den Puffer der Hörprobe");
    }

    [Test]
    public static async Task Mithoeren_UeberLautsprecher_SchadetDemSenden_Begruendung()
    {
        // Warum Mithören beim Senden nur mit Headset: Über Lautsprecher kommt die eigene Stimme verzögert zurück ins Mikrofon.
        // Mit Echounterdrückung hält der Echoschutz das für fremde Sprache und lässt eigene Anlaute nicht durch (weniger
        // gesendet), ohne hält das eigene Echo das Gate offen (mehr gesendet, Phasen kleben zusammen).
        // Gemessen wird die Stimme allein, ohne Funk-Klick: Das Mithören spielt die eigenen Phasen mit Klick wie beim Empfänger, und
        // seit 0.5.0 (A63) ist der Klick lauter; er verschiebt die Zahlen (mit Klick ab Werk zum Vergleich darunter).
        async Task<int> Audio(bool aec, Func<float, float> pfad, bool mithoeren, bool klick = false)
        {
            await using var s = new Pruefstand();
            s.Maschine.Echounterdrueckung = aec;
            // Vorlauf wie bis 0.6.0 (30 ms): Seit Klangstufe C1 trägt jede Phase 120 ms Vorlauf, in beiden Armen gleich; bei weniger,
            // längeren Phasen (das Echo klebt sie zusammen) verdünnt das die Zahl, um die es hier geht (gemessen: 813 -> 961, also 1,18).
            s.Maschine.Gate = GateVorgaben.Standard with { VorlaufMs = 30 };
            if (!klick) s.Maschine.Klick = new FunkKlickEinstellungen { EigeneAn = false, AndereAn = false };
            await s.StartenAsync();
            s.Maschine.Hoerprobe = mithoeren;
            Lautsprecher(s, pfad);
            var (sprache, _) = Testszenen.Sprache(30, 91);
            s.Abspielen(sprache);
            s.Stille(100);
            return s.Pakete.Count(p => p.Opus.Length > 0);
        }
        var mitAecOhne = await Audio(true, KlangMaschineTests.Kopplung12Db, false);
        var mitAecMit = await Audio(true, KlangMaschineTests.Kopplung12Db, true);
        var ohneAecOhne = await Audio(false, KlangMaschineTests.Kopplung3Db, false);
        var ohneAecMit = await Audio(false, KlangMaschineTests.Kopplung3Db, true);
        var mitAecMitKlick = await Audio(true, KlangMaschineTests.Kopplung12Db, true, klick: true);
        var ohneAecMitKlick = await Audio(false, KlangMaschineTests.Kopplung3Db, true, klick: true);
        Console.WriteLine($"  Mithören über Lautsprecher, ohne Klick: mit Echounterdrückung (12 dB) {mitAecOhne} -> {mitAecMit} Pakete gesendet, " +
            $"ohne (3 dB) {ohneAecOhne} -> {ohneAecMit}; mit Klick ab Werk {mitAecMitKlick} und {ohneAecMitKlick}");
        // Bis 0.6.0 Stufe 2c (AGC2 im APM) 763 -> 678 Pakete, seit Klangstufe A (Pegelstufe, keine AGC2) 764 -> 733: weniger
        // Schaden, aber weiterhin weniger gesendet.
        PruefeWert.Hoechstens(0.98 * mitAecOhne, mitAecMit, "mit Echounterdrückung kommen eigene Anlaute schlechter durch");
        PruefeWert.Mindestens(1.2 * ohneAecOhne, ohneAecMit, "ohne Echounterdrückung hält das eigene Echo das Gate offen");
        PruefeWert.Mindestens(1.2 * ohneAecOhne, ohneAecMitKlick, "ohne Echounterdrückung auch mit Klick");
        PruefeWert.Hoechstens(mitAecOhne, mitAecMitKlick, "mit Echounterdrückung und Klick nicht mehr als ohne Mithören");
    }
}
