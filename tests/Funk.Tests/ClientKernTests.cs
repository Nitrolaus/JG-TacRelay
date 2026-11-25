// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using Funk.ClientKern;
using Funk.Protokoll.Identitaet;
using Funk.Protokoll.Sprache;

namespace Funk.Tests;

/// <summary>Bausteine der Client-Bibliothek ohne Netzwerk: Wiederverbindungsplan (7.1), Zeitlinien (C6), Messwerte (7.8), Ziele, Testzugang.</summary>
internal static class ClientKernTests
{
    [Test]
    public static void Plan_SchnellphaseNachLangerSitzungDannBackoff()
    {
        var p = new Wiederverbindungsplan(() => 0.5);
        // 0.5 ergibt Streufaktor 1.0.
        Pruefe.Gleich(2_000L, p.Verloren(0, aktivDauerMs: 30_000), "Schnellphase: 2 s");
        Pruefe.Gleich(Wiederverbindungsphase.Schnell, p.Phase, "Phase schnell");
        var t = 2_000L;
        while (t < 120_000)
        {
            var n = p.Fehlschlag(t);
            Pruefe.Gleich(t + 2_000, n, "alle 2 s in den ersten 2 min");
            t = n;
        }
        Pruefe.Gleich(t + 4_000, p.Fehlschlag(t), "danach Backoff 4 s");
        Pruefe.Gleich(Wiederverbindungsphase.Backoff, p.Phase, "Phase Backoff");
        Pruefe.Gleich(t + 8_000, p.Fehlschlag(t), "8 s");
        Pruefe.Gleich(t + 15_000, p.Fehlschlag(t), "15 s");
        Pruefe.Gleich(t + 15_000, p.Fehlschlag(t), "15 s bleibt");
        p.Erfolg();
        Pruefe.Gleich(t + 4_000, p.Verloren(t, aktivDauerMs: 29_999), "kurze Sitzung: gleich Backoff ab 4 s");
    }

    [Test]
    public static void Plan_WartungUndStreuung()
    {
        var p = new Wiederverbindungsplan(() => 0.0);
        Pruefe.Gleich(10_000L, p.Verloren(0, 60_000, wartungInMs: 10_000), "WARTUNG: wiederInMs plus 0 bis 2 s (Zufall 0)");
        Pruefe.Gleich(Wiederverbindungsphase.Wartung, p.Phase, "Phase Wartung");
        Pruefe.Gleich(10_000L + 1_600, p.Fehlschlag(10_000), "danach Schnellphase, Streuung -20 %");
        var q = new Wiederverbindungsplan(() => 0.999);
        Pruefe.Wahr(q.Verloren(0, 60_000, wartungInMs: 10_000) is > 11_990 and <= 12_000, "WARTUNG: höchstens 2 s Zufall");
        Pruefe.Wahr(q.Fehlschlag(0) is >= 2_390 and <= 2_400, "Streuung +20 %");
        var w = new Wiederverbindungsplan(() => 0.5);
        Pruefe.Gleich(60_000L, w.Verloren(0, null, wartezeitInMs: 60_000), "WARTEZEIT: wiederInMs +/-20 %");
    }

    [Test]
    public static void Zeitlinien_ErstesEndeFortsetzungSprungUndLeerlauf()
    {
        var z = new Zeitlinienbuch();
        Pruefe.Wahr(z.Paket(7, 100, 5000, false, 0, out var neu, out var fort, out var ende) && neu && !fort && !ende, "erstes Paket: neue Zeitlinie, auch ohne BEGINN");
        Pruefe.Wahr(z.Paket(7, 100, 5001, false, 20, out neu, out _, out _) && !neu, "Folgepaket");
        Pruefe.Wahr(z.Paket(7, 100, 5002, true, 40, out _, out _, out ende) && ende, "erstes ENDE wird gemeldet");
        Pruefe.Falsch(z.Paket(7, 100, 5003, true, 60, out _, out _, out _), "weitere ENDE-Kopien nicht");
        Pruefe.Wahr(z.Paket(7, 101, 5010, false, 90, out neu, out fort, out _) && neu && fort, "neue Phase binnen 100 ms nach ENDE: Fortsetzung (C4, C6)");
        Pruefe.Wahr(z.Paket(7, 100, 5004, false, 95, out neu, out _, out _) && !neu, "spätes Paket der beendeten Phase setzt ohne Klick fort");
        Pruefe.Wahr(z.Paket(7, 101, 9000, false, 200, out neu, out fort, out _) && neu && !fort, "Sprung über 3000 Frames: neue Phase (7.3)");
        var beendet = new List<(uint, ushort)>();
        z.Takt(500, (a, p) => beendet.Add((a, p)));
        Pruefe.Wahr(beendet.Contains((7u, (ushort)101)) && beendet.Contains((7u, (ushort)100)), "300 ms ohne Paket beendet die Zeitlinien");
        beendet.Clear();
        z.Takt(700, (a, p) => beendet.Add((a, p)));
        Pruefe.Gleich(0, beendet.Count, "nur einmal gemeldet");
        z.Takt(10_000, (_, _) => { });
        Pruefe.Gleich(0, z.Offen, "nach 5 s aufgeräumt");
    }

    [Test]
    public static void Messwerte_LueckenUmsortiertUndJitter()
    {
        var m = new Messwerte();
        foreach (var z in new ulong[] { 0, 1, 2, 5, 6, 4, 7 }) m.RunterAngenommen(z, tunnel: false);
        var s = m.Stand();
        Pruefe.Gleich(7L, s.RunterEmpfangen, "empfangen");
        Pruefe.Gleich(1L, s.RunterVerloren, "3 fehlt, 4 kam umsortiert");
        Pruefe.Gleich(1L, s.RunterUmsortiert, "umsortiert");

        // RFC 3550: gleichmäßig 20 ms ergibt 0, ein Paket 10 ms zu spät erhöht J um 10/16.
        var t = (double)Monouhr.Ms() - 200;
        for (uint f = 0; f < 5; f++) m.Sprache(1, 9, f, false, t + f * 20.0);
        Pruefe.Wahr(m.JitterMs() < 1e-9, "kein Jitter bei 20 ms Abstand");
        m.Sprache(1, 9, 5, false, t + 5 * 20.0 + 10);
        Pruefe.Wahr(Math.Abs(m.JitterMs() - 10.0 / 16) < 1e-9, "J = J + (|D| - J)/16");
        m.Sprache(1, 9, 6, true, t + 6 * 20.0 + 500);
        Pruefe.Wahr(Math.Abs(m.JitterMs() - 10.0 / 16) < 1e-9, "PREROLL zählt nicht");
    }

    [Test]
    public static void Sendeziel_SortiertUndGeprueft()
    {
        Pruefe.Gleich("Netz [2, 7]", Sendeziel.Netz(7, 2, 7).ToString(), "sortiert, ohne Doppelte");
        Pruefe.Wirft<ArgumentException>(() => Sendeziel.Netz(), "NETZ braucht ein Ziel");
        Pruefe.Wirft<ArgumentException>(() => Sendeziel.Durchsage(1), "Global ist kein Durchsageziel");
        Pruefe.Wirft<ArgumentException>(() => Sendeziel.Netz(0), "Netz 0 gibt es nicht");
        Pruefe.Gleich(0, Sendeziel.DurchsageAnAlle.Netze.Length, "Durchsage an alle: n = 0");
        Pruefe.Wahr(Sendeziel.Netz(2, 7).Equals(Sendeziel.Netz(7, 2)), "Wertgleichheit");
    }

    [Test]
    public static void Testzugang_NachweisGiltNurFuerDenSchluessel()
    {
        using var id = Geraeteidentitaet.Fluechtig();
        var spki = id.Schluessel.ExportSubjectPublicKeyInfo();
        var zufall = RandomNumberGenerator.GetBytes(Testzugang.ZufallLaenge);
        var sig = Testzugang.NachweisBilden(id.Schluessel, zufall, spki);
        Pruefe.Wahr(Testzugang.NachweisPruefen(spki, zufall, sig, out var fp) && fp == id.Fingerabdruck, "Nachweis gilt, Fingerabdruck stimmt");
        var anderer = (byte[])zufall.Clone();
        anderer[0] ^= 1;
        Pruefe.Falsch(Testzugang.NachweisPruefen(spki, anderer, sig, out _), "anderer Zufallswert");
        using var fremd = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Pruefe.Falsch(Testzugang.NachweisPruefen(fremd.ExportSubjectPublicKeyInfo(), zufall, sig, out _), "fremdes SPKI");
        Pruefe.Wirft<ArgumentException>(() => _ = new TestzugangAufbau(new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.0.2.1"), 1), id), "nur Loopback");
        Pruefe.Gleich(P256Spki.Laenge, Testzugang.SpkiLaenge, "SPKI 91 Byte");
    }

    [Test]
    public static void Opus_ClientPrueftWieDerServer()
    {
        var p = TocOpusPruefer.Instanz;
        Pruefe.Gleich(960, p.Samples([0xF8, 1, 2]), "CELT 20 ms");
        Pruefe.Gleich(-1, p.Samples([0xF0, 1]), "10 ms verworfen");
        Pruefe.Gleich(-1, p.Samples([0xFC, 1]), "Stereo verworfen");
        Pruefe.Gleich(960, p.Samples([0xF1, 1]), "2 x 10 ms");
        Pruefe.Gleich(OpusPruefung.SamplesJeFrame, 960, "960 Samples je Frame");
    }
}
