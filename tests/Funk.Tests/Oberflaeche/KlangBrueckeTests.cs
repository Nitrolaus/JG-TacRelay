// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using Funk.Client.Ablage;
using Funk.Client.Dienste;
using Funk.Client.Klang;
using Funk.ClientKern;
using Funk.Klang.Kern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Protokoll.Sprache;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests.Oberflaeche;

/// <summary>Zeichnet auf, was die Brücke am Sprechkanal tut.</summary>
internal sealed class FunkwegAttrappe : IFunkweg
{
    private readonly Lock _sperre = new();

    public List<string> Aufrufe { get; } = [];

    public Sendeziel? Ziel { get; set; } = Sendeziel.Netz(2);

    public bool PhaseOffen { get; private set; }

    public bool Aktiv => PhaseOffen;

    public Sendeziel? ZielAusAuswahl() => Ziel;

    public bool Beginnen(Sendeziel ziel, bool sprachaktivierung, long? aufnahmeTakte)
    {
        Merken($"Beginnen {ziel} va={sprachaktivierung} takte={aufnahmeTakte}");
        PhaseOffen = true;
        return true;
    }

    public bool Senden(ReadOnlySpan<byte> opus, bool preroll)
    {
        Merken($"Senden {opus.Length} preroll={preroll}");
        return PhaseOffen;
    }

    public void Beenden()
    {
        Merken("Beenden");
        PhaseOffen = false;
    }

    public void Umschalten(Sendeziel neu) => Merken("Umschalten " + neu);

    private void Merken(string s)
    {
        lock (_sperre) Aufrufe.Add(s);
    }
}

/// <summary>
/// Die Brücke zwischen Klang und Funkbetrieb (Punkt 3 des Integrationsauftrags): Beginn, Pakete und Ende einer Phase an
/// den Sprechkanal, Sendeauswahl nach C1/C2, Empfang mit Mischer-Netz, C7 und Durchsage, Lautstärke und Panorama je
/// Netz (A9), Empfangspegel je Netz (A18), Verlusthinweis und Opus-Prüfung.
/// </summary>
internal static class KlangBrueckeTests
{
    private static readonly byte[] Opus = [0xF8, 0x01, 0x02, 0x03, 0x04, 0x05];

    /// <summary>Ein Paket so, wie es der Sprachweg nach der Prüfung meldet.</summary>
    internal static void Empfangen(ISprachEmpfaenger e, uint absender, uint frame, Kennzeichen k, Spruchart art, ushort[] netze, byte[]? opus = null)
    {
        var puffer = new byte[Sprachkanal.MaxKlartext];
        var n = SpracheRunter.Schreiben(puffer, absender, frame, 4, k, art, netze, (k & Kennzeichen.Ende) != 0 ? [] : opus ?? Opus);
        Pruefe.Gleich(Formfehler.Keiner, SpracheRunter.Lesen(puffer.AsSpan(0, n), out var p), "Form des Pakets");
        e.Empfangen(new SprachEmpfang(p, false, false, false, 0, Verbindungsweg.Udp));
    }

    [Test]
    public static async Task Senden_BeginnPaketeEnde_AnDenSprechkanal()
    {
        var maschine = new KlangMaschinenAttrappe();
        using var b = new KlangBruecke(maschine);
        var weg = new FunkwegAttrappe { Ziel = Sendeziel.Netz(2, 7) };
        b.Anbinden(weg);

        maschine.SendenSimulieren(Sprachkennzeichen.Beginn | Sprachkennzeichen.Preroll | Sprachkennzeichen.Sprachaktivierung, new byte[120], 4711);
        maschine.SendenSimulieren(Sprachkennzeichen.Preroll | Sprachkennzeichen.Sprachaktivierung, new byte[118]);
        maschine.SendenSimulieren(Sprachkennzeichen.Sprachaktivierung, new byte[130]);
        maschine.SendenSimulieren(Sprachkennzeichen.Ende | Sprachkennzeichen.Sprachaktivierung, []);
        Pruefe.Gleich(string.Join(" | ", [
            "Beginnen Netz [2, 7] va=True takte=4711", "Senden 120 preroll=True", "Senden 118 preroll=True", "Senden 130 preroll=False", "Beenden"]),
            string.Join(" | ", weg.Aufrufe), "Folge am Sprechkanal");
        Pruefe.Gleich(3L, b.GesendetePakete, "drei Pakete hinaus");

        // Sprechtaste: VA aus; ohne Sendenetz beginnt keine Phase, und was kommt, geht ins Leere.
        weg.Aufrufe.Clear();
        maschine.SendenSimulieren(Sprachkennzeichen.Beginn, new byte[100], 9);
        maschine.SendenSimulieren(Sprachkennzeichen.Ende, []);
        Pruefe.Wahr(weg.Aufrufe[0].StartsWith("Beginnen Netz [2, 7] va=False", StringComparison.Ordinal), "Taste: VA aus, " + weg.Aufrufe[0]);
        weg.Aufrufe.Clear();
        weg.Ziel = null;
        maschine.SendenSimulieren(Sprachkennzeichen.Beginn, new byte[100], 9);
        Pruefe.Falsch(weg.Aufrufe.Any(a => a.StartsWith("Beginnen", StringComparison.Ordinal)), "ohne Sendenetz kein Beginn");

        await using var fremd = new FunkClientAttrappe();
        b.Loesen(fremd.Client);
        weg.Aufrufe.Clear();
        maschine.SendenSimulieren(Sprachkennzeichen.Beginn, new byte[100], 9);
        Pruefe.Wahr(weg.Aufrufe.Count > 0, "Loesen mit fremdem Client ändert nichts");
    }

    [Test]
    public static void Sendeauswahl_WechselMittenImSpruch_SchaltetUm()
    {
        var maschine = new KlangMaschinenAttrappe();
        using var b = new KlangBruecke(maschine);
        var weg = new FunkwegAttrappe();
        b.Anbinden(weg);
        b.AuswahlGeaendert();
        Pruefe.Gleich(0, weg.Aufrufe.Count, "ohne laufenden Spruch nichts");
        maschine.SendenSimulieren(Sprachkennzeichen.Beginn, new byte[100], 1);
        weg.Ziel = Sendeziel.Netz(5);
        b.AuswahlGeaendert();
        Pruefe.Gleich("Umschalten Netz [5]", weg.Aufrufe[^1], "C2: umschalten ans neue Ziel");
        weg.Ziel = null;
        b.AuswahlGeaendert();
        Pruefe.Gleich("Beenden", weg.Aufrufe[^1], "kein Sendenetz mehr: Phase endet");
    }

    [Test]
    public static void Empfang_MischerNetzDurchsageUndAnkunft()
    {
        var maschine = new KlangMaschinenAttrappe { JetztUs = 123_456 };
        using var b = new KlangBruecke(maschine);
        Empfangen(b, 11, 100, Kennzeichen.Beginn | Kennzeichen.Va, Spruchart.Netz, [7]);
        Empfangen(b, 12, 5, Kennzeichen.Beginn, Spruchart.Global, [1]);
        Empfangen(b, 11, 101, Kennzeichen.Ende | Kennzeichen.Va, Spruchart.Netz, [7]);
        Empfangen(b, 13, 1, Kennzeichen.Beginn, Spruchart.Durchsage, []);
        var e = maschine.Empfangen;
        Pruefe.Gleich(4, e.Count, "vier Pakete an die Maschine");
        Pruefe.Gleich(4L, b.EmpfangenePakete, "gezählt");
        var n7 = b.MischerNetzVon(7);
        var n1 = b.MischerNetzVon(1);
        Pruefe.Wahr(n7 is >= 0 and < IKlangMaschine.FunkNetze && n1 is >= 0 and < IKlangMaschine.FunkNetze && n7 != n1, $"eigene Mischer-Netze: 7 -> {n7}, 1 -> {n1}");
        Pruefe.Gleich((11u, n7, (ushort)4, 100u, Sprachkennzeichen.Beginn | Sprachkennzeichen.Sprachaktivierung, 123_456L, false),
            (e[0].KontoId, e[0].Netz, e[0].Sprechphase, e[0].FrameNr, e[0].Kennzeichen, e[0].AnkunftUs, e[0].Durchsage), "Netz-Paket");
        Pruefe.Wahr(e[0].Opus.AsSpan().SequenceEqual(Opus), "Opus unverändert");
        Pruefe.Wahr(e[1].Durchsage && e[1].Netz == n1, "GLOBAL geht als Durchsage in den Mischer (C9)");
        Pruefe.Wahr(e[2].Kennzeichen.HasFlag(Sprachkennzeichen.Ende) && e[2].Opus.Length == 0, "ENDE ohne Opus");
        Pruefe.Wahr(e[3].Durchsage && e[3].Netz >= 0, "Durchsage an alle ohne Netz");
        Pruefe.Gleich(e[0].Netz, e[2].Netz, "gleiches Netz, gleiches Mischer-Netz");
    }

    [Test]
    public static void Empfang_MehrereTrefferNetze_C7LautestesNetz()
    {
        var maschine = new KlangMaschinenAttrappe();
        using var b = new KlangBruecke(maschine);
        b.NetzEinstellen(5, -6f, -1f);
        b.NetzEinstellen(7, 0f, 1f);
        var n5 = b.MischerNetzVon(5);
        var n7 = b.MischerNetzVon(7);
        Pruefe.Gleich((-6f, -1f), (maschine.NetzLautstaerke(n5), maschine.NetzPanorama(n5)), "Einstellung vor dem ersten Paket angewandt (A9)");
        Pruefe.Gleich((0f, 1f), (maschine.NetzLautstaerke(n7), maschine.NetzPanorama(n7)), "rechts");
        Empfangen(b, 20, 1, Kennzeichen.Beginn, Spruchart.Netz, [5, 7]);
        Pruefe.Gleich(n7, maschine.Empfangen[^1].Netz, "7 ist lauter: dessen Lautstärke und Panorama gelten (C7)");
        b.NetzEinstellen(5, 3f, -1f);
        Pruefe.Gleich(3f, maschine.NetzLautstaerke(n5), "Regler wirkt sofort");
        Empfangen(b, 20, 2, Kennzeichen.None, Spruchart.Netz, [5, 7]);
        Pruefe.Gleich(n5, maschine.Empfangen[^1].Netz, "jetzt ist 5 lauter");
        b.NetzEinstellen(7, 3f, 1f);
        Empfangen(b, 20, 3, Kennzeichen.None, Spruchart.Netz, [7, 5]);
        Pruefe.Gleich(n7, maschine.Empfangen[^1].Netz, "Gleichstand: das erste der Liste");
    }

    [Test]
    public static void Empfang_VieleNetze_VergibtMischerNetzeNeu()
    {
        var maschine = new KlangMaschinenAttrappe();
        using var b = new KlangBruecke(maschine);
        for (ushort n = 1; n <= IKlangMaschine.FunkNetze + 10; n++) Empfangen(b, 30, n, Kennzeichen.None, Spruchart.Netz, [n]);
        Pruefe.Wahr(maschine.Empfangen.All(p => p.Netz is >= 0 and < IKlangMaschine.FunkNetze), "nie über 62 (63 ist die Hörprobe)");
        Pruefe.Gleich(-1, b.MischerNetzVon(1), "das am längsten unbenutzte Netz wurde neu vergeben");
        Pruefe.Wahr(b.MischerNetzVon(IKlangMaschine.FunkNetze + 10) >= 0, "das jüngste hat eins");
    }

    [Test]
    public static void Empfangspegel_JeFunknetzHoechstwert()
    {
        var maschine = new KlangMaschinenAttrappe();
        using var b = new KlangBruecke(maschine);
        var gemeldet = new List<(ushort Netz, float Db)>();
        b.NetzPegel += (n, db) => gemeldet.Add((n, db));
        Empfangen(b, 9, 1, Kennzeichen.Beginn, Spruchart.Netz, [5, 7]);
        Empfangen(b, 10, 1, Kennzeichen.Beginn, Spruchart.Netz, [7]);
        var m7 = b.MischerNetzVon(7);
        maschine.SprecherPegelMelden(new SprecherPegel(9, m7, -20f, true), new SprecherPegel(10, m7, -30f, true), new SprecherPegel(11, m7, -5f, false));
        var d = gemeldet.ToDictionary(x => x.Netz, x => x.Db);
        Pruefe.Gleich(2, d.Count, "Netze 5 und 7");
        Pruefe.Gleich(-20f, d[5], "Sprecher 9 auch in Netz 5 gestempelt");
        Pruefe.Gleich(-20f, d[7], "Höchstwert über die Sprecher");
        gemeldet.Clear();
        maschine.SprecherPegelMelden(new SprecherPegel(9, m7, -20f, false));
        Pruefe.Gleich(0, gemeldet.Count, "wer nicht spricht, zeigt keinen Pegel");

        // Vorführung: Die Attrappe liefert schon je Funknetz; das geht unverändert durch.
        maschine.EmpfangsPegelMelden(4, -12f);
        Pruefe.Gleich(((ushort)4, -12f), gemeldet.Single(), "Pegel je Netz der Attrappe durchgereicht");
    }

    [Test]
    public static async Task Verlusthinweis_UndOpusPruefung()
    {
        var maschine = new KlangMaschinenAttrappe();
        using var b = new KlangBruecke(maschine);
        await using var c = new FunkClientAttrappe();
        b.Anbinden(c.Client);
        c.Client.PushMelden(new PushEreignis(Nachrichtentyp.VerlustHinweis, new VerlustHinweis { Prozent = 7 }));
        Pruefe.Gleich(7, maschine.VerlustHinweis, "VerlustHinweis an den Kodierer");
        b.Loesen(c.Client);
        c.Client.PushMelden(new PushEreignis(Nachrichtentyp.VerlustHinweis, new VerlustHinweis { Prozent = 30 }));
        Pruefe.Gleich(7, maschine.VerlustHinweis, "nach dem Lösen nicht mehr");

        using var k = new OpusKodierer();
        var paket = new byte[400];
        var n = k.Kodieren(AnbindungSignale.Sprache(960, 3), paket);
        Pruefe.Gleich(960, b.Samples(paket.AsSpan(0, n)), "libopus: 20 ms");
        Pruefe.Gleich(-1, b.Samples([]), "leer ungültig");
        Pruefe.Gleich(-1, b.Samples([0x03, 0xFF]), "Code 3 mit unsinniger Rahmenzahl ungültig");
    }

    [Test]
    public static void Funkfeld_LautstaerkeUndOhr_WirkenAufDenMischer()
    {
        using var a = new Aufbau();
        var f = a.Funkfeld();
        var crew = f.Kacheln.Single(k => k.Name == "Crew");
        var netz = a.Bruecke.MischerNetzVon(crew.NetzId!.Value);
        Pruefe.Wahr(netz >= 0, "Netze der Kacheln sind dem Mischer schon bekannt");
        crew.LautstaerkeDb = -7.2;
        Pruefe.Gleich(-7f, a.Maschine.NetzLautstaerke(netz), "Kachel-Regler wirkt (A9), auf 0,5 dB gerundet");

        var netze = a.Netze();
        var zeile = netze.Felder.Single(z => z.Netz == "Crew");
        Pruefe.Wahr(zeile.Ohr!.Beide && !zeile.Ohr.Links, "Standard: beide Ohren");
        zeile.Ohr.Links = true;
        Pruefe.Gleich(-1f, a.Maschine.NetzPanorama(netz), "links");
        Pruefe.Gleich(-7f, a.Maschine.NetzLautstaerke(netz), "Lautstärke bleibt");
        zeile.Ohr.Beide = false;
        Pruefe.Wahr(zeile.Ohr.Links, "Abwählen durch die Gruppe ändert nichts");
        Pruefe.Gleich(-1f, a.Ablage.Aktuell.NetzOderNull(crew.NetzId.Value)!.Panorama, "gemerkt");
        var global = netze.GlobalOhr!;
        global.Rechts = true;
        Pruefe.Gleich(1f, a.Maschine.NetzPanorama(a.Bruecke.MischerNetzVon(1)), "Global rechts");

        // Beim nächsten Start gelten die gespeicherten Werte sofort.
        a.Ablage.Sichern();
        using var zweiter = new Aufbau(einstellungen: a.Speicher.Laden());
        _ = zweiter.Funkfeld();
        var n2 = zweiter.Bruecke.MischerNetzVon(crew.NetzId.Value);
        Pruefe.Gleich((-7f, -1f), (zweiter.Maschine.NetzLautstaerke(n2), zweiter.Maschine.NetzPanorama(n2)), "nach dem Neustart");
    }
}

/// <summary>Ein FunkClient ohne Verbindung, nur um Ereignisse auszulösen.</summary>
internal sealed class FunkClientAttrappe : IAsyncDisposable
{
    private readonly Geraeteidentitaet _identitaet = Geraeteidentitaet.Fluechtig();

    public FunkClientAttrappe()
    {
        Client = new FunkClient(new FunkClientOptionen
        {
            Aufbau = new DelegatAufbau((_, _) => throw new AufbauException(AufbauFehler.Netz, "Attrappe")),
            Identitaet = _identitaet,
        });
    }

    public FunkClient Client { get; }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        _identitaet.Dispose();
    }
}
