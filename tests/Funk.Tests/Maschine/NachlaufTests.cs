// JG TacRelay - Maintainer: Sarevoc
using System.Security.Cryptography;
using Funk.Client.Klang;
using Funk.ClientKern;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Oberflaeche;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Nachlauf der Sprechtaste (Q66, 0.9.3) an der echten Klangmaschine im Prüfstand: Nach dem Loslassen sendet sie genau so lange weiter wie
/// eingestellt, wie mit gedrückter Taste; ein neuer Druck derselben Taste im Nachlauf setzt dieselbe Phase fort (kein ENDE, kein Beginn, kein Klick),
/// einer der anderen Taste beendet sie und beginnt eine neue am anderen Ziel (Prüfung 0.9.3); Mikro stumm, Stoppen und ein Abbruch des Stroms
/// beenden ihn sofort; der Klick beim Loslassen kommt erst danach; die Melde-Taste bleibt dabei am Meldenetz; die Sprachaktivierung bleibt
/// unberührt und beginnt nach dem Nachlauf ihre eigene Phase.
/// </summary>
internal static class NachlaufTests
{
    private const ushort Kommando = 2;
    private const ushort Crew = 22;

    /// <summary>Beginne, Enden, Audiopakete und Takt des letzten ENDE.</summary>
    private static (int Beginne, int Enden, int Audio, long EndeTakt) Zaehlen(List<Gesendet> pakete) =>
        (pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0), pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Ende) != 0),
            pakete.Count(p => p.Opus.Length > 0), pakete.LastOrDefault(p => (p.Kennzeichen & Sprachkennzeichen.Ende) != 0)?.Takt ?? -1);

    private static async Task<Pruefstand> Starten(int nachlaufMs, bool sprachaktivierung = false)
    {
        var s = new Pruefstand();
        s.Maschine.Sprachaktivierung = sprachaktivierung;
        s.Maschine.TastenNachlaufMs = nachlaufMs;
        await s.StartenAsync();
        s.Stille(50);
        return s;
    }

    /// <summary>Sprache für <paramref name="takte"/> Takte, damit gesendet wird, was zu hören wäre.</summary>
    private static void Sprechen(Pruefstand s, int takte, int seed = 61)
    {
        var (sprache, _) = Testszenen.Sprache(Math.Ceiling(takte / 100.0) + 1, seed);
        s.Abspielen(sprache.AsSpan(0, takte * 480));
    }

    [Test]
    public static async Task Nachlauf_SendetGenauSoLangWeiterWieEingestellt()
    {
        foreach (var n in new[] { 0, 100, 250 })
        {
            await using var s = await Starten(n);
            s.Maschine.SprechtasteGedrueckt();
            var druck = s.Takte + 1;
            Sprechen(s, 30);
            s.Maschine.SprechtasteLosgelassen();
            var los = s.Takte + 1;
            if (n > 0)
            {
                s.Stille(1);
                Pruefe.Wahr(s.Maschine.Kette.ImNachlauf, n + " ms: die Kette sendet den Nachlauf");
                Pruefe.Falsch(s.Maschine.Kette.Sprechtaste, n + " ms: die Taste selbst ist los");
                s.Stille((n / 10) - 1);
                Pruefe.Wahr(s.Maschine.Kette.ImNachlauf, n + " ms: im letzten Rahmen des Nachlaufs noch");
                Pruefe.Gleich(0, Zaehlen(s.Pakete).Enden, n + " ms: noch kein ENDE");
            }
            s.Stille(30);
            Pruefe.Falsch(s.Maschine.Kette.ImNachlauf, n + " ms: danach vorbei");
            var (beginne, enden, audio, endeTakt) = Zaehlen(s.Pakete);
            Pruefe.Gleich(1, beginne, n + " ms: eine Phase");
            Pruefe.Gleich(1, enden, n + " ms: ein ENDE");
            Pruefe.Gleich(los + (n / 10), endeTakt, n + " ms: ENDE genau nach dem Nachlauf (im Rahmen danach, ausgeblendet)");
            var rahmen = los + (n / 10) - druck + 1;
            Pruefe.Gleich((int)((rahmen + 1) / 2), audio, n + " ms: so viele Pakete zu 20 ms wie Rahmen bis dahin");
        }
    }

    [Test]
    public static async Task Nachlauf_ErneutGedrueckt_SetztNahtlosFort_OhneNeuenKlick()
    {
        // Mitten im Nachlauf und im letzten Rahmen des Nachlaufs wieder gedrückt: dieselbe Phase. Einen Rahmen später: eine neue.
        foreach (var (warten, phasen) in new[] { (3, 1), (10, 1), (11, 2) })
        {
            await using var s = await Starten(100);
            var klick = s.Maschine.Mischer.Klick!;
            s.Maschine.SprechtasteGedrueckt();
            Sprechen(s, 20);
            s.Maschine.SprechtasteLosgelassen();
            s.Stille(warten);
            s.Maschine.SprechtasteGedrueckt();
            Sprechen(s, 20, 62);
            s.Maschine.SprechtasteLosgelassen();
            var los = s.Takte + 1;
            s.Stille(40);
            var (beginne, enden, _, endeTakt) = Zaehlen(s.Pakete);
            var was = $"nach {warten} Rahmen wieder gedrückt";
            Pruefe.Gleich(phasen, beginne, was + ": Beginne");
            Pruefe.Gleich(phasen, enden, was + ": Enden");
            Pruefe.Gleich(los + 10, endeTakt, was + ": das letzte ENDE nach dem Nachlauf des zweiten Loslassens");
            Pruefe.Gleich((long)phasen, klick.EigeneDrueckeGestartet, was + ": Klick beim Drücken nur je Phase");
            Pruefe.Gleich((long)(2 * phasen), klick.Gestartet, was + ": je Phase ein Klick beim Drücken und einer beim Loslassen");
        }
    }

    [Test]
    public static async Task Nachlauf_MikroStumm_BeendetSofort_OhneKlick()
    {
        await using var s = await Starten(500);
        var klick = s.Maschine.Mischer.Klick!;
        s.Maschine.SprechtasteGedrueckt();
        Sprechen(s, 20);
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(3);
        Pruefe.Wahr(s.Maschine.Kette.ImNachlauf, "im Nachlauf");
        s.Maschine.MikroStumm = true;
        var stumm = s.Takte + 1;
        s.Stille(1);
        var (_, enden, _, endeTakt) = Zaehlen(s.Pakete);
        Pruefe.Gleich(1, enden, "ENDE sofort");
        Pruefe.Gleich(stumm, endeTakt, "im ersten stummen Rahmen");
        Pruefe.Falsch(s.Maschine.Kette.ImNachlauf, "der Nachlauf ist vorbei");
        var anzahl = s.Pakete.Count;
        s.Stille(60);
        s.Maschine.MikroStumm = false;
        s.Stille(60);
        Pruefe.Gleich(anzahl, s.Pakete.Count, "danach nichts mehr, auch nicht nach dem Lösen");
        Pruefe.Gleich(1L, klick.Gestartet, "nur der Klick beim Drücken; stumm klickt es beim Loslassen nicht (wie bisher)");
    }

    [Test]
    public static async Task Nachlauf_KlickBeimLoslassen_KommtErstNachDemNachlauf()
    {
        foreach (var n in new[] { 0, 150 })
        {
            await using var s = await Starten(n);
            var klick = s.Maschine.Mischer.Klick!;
            s.Maschine.SprechtasteGedrueckt();
            s.Stille(1);
            Pruefe.Gleich(1L, klick.Gestartet, n + " ms: Klick beim Drücken im nächsten Block");
            Sprechen(s, 20);
            s.Maschine.SprechtasteLosgelassen();
            var los = s.Takte + 1;
            var gestartet = new List<long>();
            for (var i = 0; i < (n / 10) + 5; i++)
            {
                s.Stille(1);
                gestartet.Add(klick.Gestartet);
            }
            // Die Kette beendet das Senden im Rahmen los + n/10 und fordert dort den Klick an; die Wiedergabe startet ihn im Takt danach.
            var erster = gestartet.FindIndex(g => g == 2) + los;
            Pruefe.Gleich(los + (n / 10) + 1, erster, n + " ms: Klick beim Loslassen erst nach dem Nachlauf");
            Pruefe.Gleich(2L, klick.Gestartet, n + " ms: genau ein Klick beim Loslassen");
            var (_, _, _, endeTakt) = Zaehlen(s.Pakete);
            Pruefe.Gleich(erster - 1, endeTakt, n + " ms: im Takt nach dem ENDE");
        }
    }

    [Test]
    public static async Task Nachlauf_SprachaktivierungBleibtUnberuehrt()
    {
        var (sprache, _) = Testszenen.Sprache(12, 63);
        async Task<string> Lauf(int n)
        {
            await using var s = new Pruefstand();
            s.Maschine.TastenNachlaufMs = n;
            await s.StartenAsync();
            s.Abspielen(sprache);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var p in s.Pakete)
            {
                sha.AppendData(BitConverter.GetBytes(p.Takt));
                sha.AppendData([(byte)p.Kennzeichen]);
                sha.AppendData(p.Opus);
            }
            Pruefe.Wahr(s.Pakete.Any(p => (p.Kennzeichen & Sprachkennzeichen.Sprachaktivierung) != 0), "die Sprachaktivierung sendet");
            return Convert.ToHexString(sha.GetHashAndReset());
        }
        Pruefe.Gleich(await Lauf(0), await Lauf(1000), "ohne Taste sendet die Maschine mit und ohne Nachlauf Paket für Paket dasselbe");
    }

    /// <summary>
    /// Prüfung 0.9.3: Taste los mitten in der Sprache, Sprachaktivierung an. Die Tastenphase endet genau nach dem Nachlauf; die weitere Sprache
    /// öffnet danach die Sprachaktivierung als eigene Phase mit Beginn und Kennzeichen, und kein Rahmen der Sprachaktivierung geht in die
    /// Tastenphase.
    /// </summary>
    [Test]
    public static async Task Nachlauf_MitSprachaktivierung_EigenePhaseNachDemNachlauf()
    {
        var (sprache, marken) = Testszenen.Sprache(12, 63);
        var aeusserung = marken.First(m => m.Ende - m.Start >= 48_000);
        await using var s = await Starten(100, sprachaktivierung: true);
        var anfang = s.Takte;
        var druckRahmen = (aeusserung.Start / 480) - 5;
        var losRahmen = (aeusserung.Start / 480) + 40;
        long los = -1;
        s.Abspielen(sprache, t =>
        {
            if (t - anfang == druckRahmen) s.Maschine.SprechtasteGedrueckt();
            if (t - anfang != losRahmen) return;
            s.Maschine.SprechtasteLosgelassen();
            los = t + 1;
        });
        s.Stille(60);
        var tastenBeginn = s.Pakete.FindIndex(p => (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0 && p.Takt > anfang + druckRahmen);
        Pruefe.Wahr(tastenBeginn >= 0 && (s.Pakete[tastenBeginn].Kennzeichen & Sprachkennzeichen.Sprachaktivierung) == 0, "die Tastenphase beginnt ohne Kennzeichen der Sprachaktivierung");
        var tastenEnde = s.Pakete.FindIndex(tastenBeginn, p => (p.Kennzeichen & Sprachkennzeichen.Ende) != 0);
        Pruefe.Gleich(los + 10, s.Pakete[tastenEnde].Takt, "die Tastenphase endet genau nach dem Nachlauf");
        Pruefe.Falsch(s.Pakete.Skip(tastenBeginn).Take(tastenEnde - tastenBeginn + 1).Any(p => (p.Kennzeichen & Sprachkennzeichen.Sprachaktivierung) != 0),
            "kein Paket der Sprachaktivierung in der Tastenphase");
        var vaBeginn = s.Pakete.FindIndex(tastenEnde, p => (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0);
        Pruefe.Wahr(vaBeginn > tastenEnde, "danach eine neue Phase mit Beginn");
        Pruefe.Wahr((s.Pakete[vaBeginn].Kennzeichen & Sprachkennzeichen.Sprachaktivierung) != 0, "mit Kennzeichen der Sprachaktivierung");
        Pruefe.Wahr(s.Pakete[vaBeginn].Takt > los + 10, "nach dem Ende der Tastenphase");
    }

    /// <summary>
    /// Prüfung 0.9.3: Die Sperre der klemmenden Taste (Klangstufe C2a) greift im Nachlauf nicht. Bis dahin zählte die Klemmregel die Pausen auch
    /// nach dem Loslassen; nach 500 ms Pause im Nachlauf war die Taste gesperrt, und ein neuer Druck darin sendete nichts und klickte nicht. Ablauf
    /// wie <c>KlangC2FixTests.C2F_M1_KlemmendeTaste_AbschaltenErstInDerPause</c>: 52 s ohne Sprache, dann Rede; nach dem Urteil mitten im Satz los
    /// (noch nicht gesperrt), 700 ms Pause im Nachlauf von 1000 ms, wieder drücken und weiterreden.
    /// </summary>
    [Test]
    public static async Task Nachlauf_KlemmendeTaste_SperreGreiftNichtImNachlauf()
    {
        const int Rate = 48000;
        const int R = 480;
        await using var s = new Pruefstand();
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 3f, Gelernt: true);
        s.Maschine.Echounterdrueckung = false;
        s.Maschine.TastenNachlaufMs = 1000;
        await s.StartenAsync();
        s.Maschine.Sprachaktivierung = false;
        s.Maschine.SprechtasteGedrueckt();
        s.Abspielen(AnbindungSignale.Rauschen(52 * Rate, Pumpmessung.GrundrauschenDbfs, 31));
        var rede = KlangC2EmpfangTests.Korpusrede(20, out var ph, saat: 32, pausenMs: 300, nurFunklaut: true);
        var rauschen = AnbindungSignale.Rauschen(rede.Length, Pumpmessung.GrundrauschenDbfs, 33);
        for (var i = 0; i < rede.Length; i++) rede[i] += rauschen[i];
        var k = 0;
        for (; (k + 1) * R <= rede.Length; k++)
        {
            var mittenImSatz = ph.Any(p => k * R >= p.Von + (20 * R) && k * R < p.Bis - (20 * R));
            if (mittenImSatz && s.Maschine.EigenesUrteil.Urteil == DauersendUrteil.KlemmendeTaste && !s.Maschine.Kette.TasteGesperrt) break;
            s.Takt(rede.AsSpan(k * R, R));
        }
        Pruefe.Wahr((k + 1) * R <= rede.Length, "Voraussetzung: Urteil klemmende Taste mitten im Satz, noch nicht gesperrt");
        s.Maschine.SprechtasteLosgelassen();
        s.Abspielen(AnbindungSignale.Rauschen(70 * R, Pumpmessung.GrundrauschenDbfs, 34));
        Pruefe.Wahr(s.Maschine.Kette.ImNachlauf, "nach 700 ms Pause noch im Nachlauf");
        Pruefe.Falsch(s.Maschine.Kette.TasteGesperrt, "im Nachlauf nicht gesperrt");
        var enden = s.Pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Ende) != 0);
        s.Maschine.SprechtasteGedrueckt();
        var druck = s.Takte + 1;
        s.Abspielen(rede.AsSpan((k + 70) * R, 100 * R));
        Pruefe.Falsch(s.Maschine.Kette.TasteGesperrt, "der neue Druck sendet");
        Pruefe.Gleich(enden, s.Pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Ende) != 0), "dieselbe Phase läuft weiter, kein ENDE");
        Pruefe.Gleich(50, s.Pakete.Count(p => p.Opus.Length > 0 && p.Takt >= druck), "jedes Paket der Rede nach dem Druck geht hinaus");
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(150);
    }

    [Test]
    public static async Task Nachlauf_Begrenzt_AufZehnMsSchritte()
    {
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false);
        var a = new KlangMaschinenAttrappe();
        Pruefe.Gleich(0, s.Maschine.TastenNachlaufMs, "die Maschine ab Werk ohne Nachlauf, wie bis 0.9.2");
        foreach (var (ein, aus) in new[] { (-5, 0), (0, 0), (104, 100), (105, 110), (250, 250), (1000, 1000), (1234, 1000), (int.MaxValue, 1000), (int.MinValue, 0) })
        {
            s.Maschine.TastenNachlaufMs = ein;
            a.TastenNachlaufMs = ein;
            Pruefe.Gleich(aus, s.Maschine.TastenNachlaufMs, $"Maschine: {ein}");
            Pruefe.Gleich(aus, a.TastenNachlaufMs, $"Attrappe: {ein}");
        }
    }

    /// <summary>Die Folge an der Brücke ohne die einzelnen Pakete.</summary>
    private static string Folge(FunkwegAttrappe weg) =>
        string.Join(" | ", weg.Aufrufe.Where(x => !x.StartsWith("Senden", StringComparison.Ordinal)).Select(x => x.Split(" va=")[0]));

    /// <summary>Die Folge an der Brücke mit der Zahl der Pakete je Abschnitt ("Senden x12"), also auch, an welches Ziel sie gingen.</summary>
    private static string FolgeMitPaketen(FunkwegAttrappe weg)
    {
        var teile = new List<string>();
        var senden = 0;
        foreach (var x in weg.Aufrufe)
        {
            if (x.StartsWith("Senden", StringComparison.Ordinal))
            {
                senden++;
                continue;
            }
            if (senden > 0) teile.Add("Senden x" + senden);
            senden = 0;
            teile.Add(x.Split(" va=")[0]);
        }
        if (senden > 0) teile.Add("Senden x" + senden);
        return string.Join(" | ", teile);
    }

    /// <summary>Pakete mit Ton (Opus) der Maschine bis einschließlich Takt <paramref name="bis"/> und danach.</summary>
    private static (int Bis, int Danach) TonPakete(List<Gesendet> pakete, long bis) =>
        (pakete.Count(p => p.Opus.Length > 0 && p.Takt <= bis), pakete.Count(p => p.Opus.Length > 0 && p.Takt > bis));

    /// <summary>Takte der Pakete mit Beginn und mit Ende.</summary>
    private static (long[] Beginne, long[] Enden) Grenzen(List<Gesendet> pakete) =>
        ([.. pakete.Where(p => (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0).Select(p => p.Takt)],
            [.. pakete.Where(p => (p.Kennzeichen & Sprachkennzeichen.Ende) != 0).Select(p => p.Takt)]);

    [Test]
    public static async Task Nachlauf_MeldeTaste_BleibtAmMeldenetz_UndSetztFort()
    {
        await using var s = await Starten(100, sprachaktivierung: true);
        using var bruecke = new KlangBruecke(s.Maschine);
        var weg = new FunkwegAttrappe { Ziel = Sendeziel.Netz(Crew) };
        bruecke.Anbinden(weg);
        var st = new Sprechsteuerung(s.Maschine, bruecke, () => new MeldeEntscheid(Kommando, "Kommando", null));
        var klick = s.Maschine.Mischer.Klick!;

        st.MeldenGedrueckt();
        Sprechen(s, 20);
        st.MeldenLosgelassen();
        s.Stille(5);
        Pruefe.Gleich("Beginnen Netz [2]", Folge(weg), "im Nachlauf läuft die Meldung weiter, am Meldenetz");
        weg.Ziel = Sendeziel.Netz(23);
        bruecke.AuswahlGeaendert();
        Pruefe.Falsch(weg.Aufrufe.Any(x => x.StartsWith("Umschalten", StringComparison.Ordinal)), "Numpad + im Nachlauf schaltet die Meldung nicht um");

        st.MeldenGedrueckt();
        Sprechen(s, 10, 64);
        Pruefe.Gleich("Beginnen Netz [2]", Folge(weg), "im Nachlauf wieder gedrückt: dieselbe Phase am Meldenetz");
        st.MeldenLosgelassen();
        s.Stille(30);
        Pruefe.Gleich("Beginnen Netz [2] | Beenden", Folge(weg), "eine Meldung, ein Ende");
        Pruefe.Gleich(1L, klick.EigeneDrueckeGestartet, "ein Klick beim Drücken");

        st.SprechenGedrueckt();
        s.Stille(5);
        Pruefe.Gleich("Beginnen Netz [2] | Beenden | Beginnen Netz [23]", Folge(weg), "der nächste Spruch geht an die neue Sendeauswahl");
        st.SprechenLosgelassen();
        s.Stille(30);
    }

    /// <summary>
    /// Prüfung 0.9.3: Die andere Taste im Nachlauf ist ein anderes Ziel. Die Maschine beendet die alte Phase im nächsten Rahmen mit ENDE und beginnt
    /// im Rahmen danach eine neue; bis zu diesem ENDE bleibt die Brücke am alten Ziel. So geht kein Rest des alten Spruchs ins andere Netz, und es
    /// klickt für beide Sprüche beim Loslassen und beim Drücken. Bis zur Prüfung lief der Nachlauf ohne ENDE weiter, die Brücke schaltete gleich
    /// um, und sein Rest ging ans neue Ziel, ohne Klick.
    /// </summary>
    private static async Task ZielwechselImNachlauf(bool vonMelden)
    {
        await using var s = await Starten(100);
        using var bruecke = new KlangBruecke(s.Maschine);
        var weg = new FunkwegAttrappe { Ziel = Sendeziel.Netz(Crew) };
        bruecke.Anbinden(weg);
        var st = new Sprechsteuerung(s.Maschine, bruecke, () => new MeldeEntscheid(Kommando, "Kommando", null));
        var klick = s.Maschine.Mischer.Klick!;
        Action ersteDruecken = vonMelden ? st.MeldenGedrueckt : st.SprechenGedrueckt;
        Action ersteLos = vonMelden ? st.MeldenLosgelassen : st.SprechenLosgelassen;
        Action zweiteDruecken = vonMelden ? st.SprechenGedrueckt : st.MeldenGedrueckt;
        Action zweiteLos = vonMelden ? st.SprechenLosgelassen : st.MeldenLosgelassen;
        var (erstesZiel, zweitesZiel) = vonMelden ? (Kommando, Crew) : (Crew, Kommando);
        var was = vonMelden ? "Sprechtaste im Nachlauf der Meldung" : "Melde-Taste im Nachlauf des Spruchs an die Crew";

        ersteDruecken();
        var druck = s.Takte + 1;
        Sprechen(s, 20);
        ersteLos();
        s.Stille(4);
        Pruefe.Wahr(s.Maschine.Kette.ImNachlauf, was + ": im Nachlauf");
        var wechsel = s.Takte + 1;
        zweiteDruecken();
        Sprechen(s, 10, 65);
        zweiteLos();
        s.Stille(30);

        // Das Paket mit Beginn ist erst mit dem zweiten Rahmen zu 20 ms fertig, also einen Takt nach dem Rahmen, mit dem die Phase beginnt.
        var (beginne, enden) = Grenzen(s.Pakete);
        Pruefe.Gleich($"{druck + 1}, {wechsel + 2}", string.Join(", ", beginne), was + ": zwei Phasen, die zweite beginnt im Rahmen nach dem Wechsel");
        Pruefe.Gleich(wechsel, enden[0], was + ": die erste endet im Rahmen des Wechsels mit ENDE (ausgeblendet), nicht erst nach dem Nachlauf");
        var (bis, danach) = TonPakete(s.Pakete, wechsel);
        Pruefe.Gleich((int)((wechsel - druck + 2) / 2), bis, was + ": so viele Pakete wie Rahmen bis zum Wechsel, samt dem Nachlauf bis dahin");
        Pruefe.Gleich($"Beginnen Netz [{erstesZiel}] | Senden x{bis} | Beenden | Beginnen Netz [{zweitesZiel}] | Senden x{danach} | Beenden",
            FolgeMitPaketen(weg), was + ": jedes Paket des ersten Spruchs, auch das Ende im Nachlauf, am ersten Ziel; erst die neue Phase am zweiten");
        Pruefe.Gleich(2L, klick.EigeneDrueckeGestartet, was + ": Klick beim Drücken für beide Sprüche");
        Pruefe.Gleich(4L, klick.Gestartet, was + ": und je einer beim Loslassen");
    }

    [Test]
    public static Task Nachlauf_SprechtasteImNachlaufDerMeldung_SpruchGehtAnDieCrew() => ZielwechselImNachlauf(vonMelden: true);

    [Test]
    public static Task Nachlauf_MeldeTasteImNachlaufDesSpruchs_RestBleibtBeiDerCrew() => ZielwechselImNachlauf(vonMelden: false);

    [Test]
    public static async Task Nachlauf_DieselbeTaste_SetztAuchUeberDieSteuerungFort()
    {
        // Gegenstück zum Zielwechsel: Dieselbe Taste fürs selbe Ziel setzt den Spruch fort, auch über die Sprechsteuerung.
        await using var s = await Starten(100);
        using var bruecke = new KlangBruecke(s.Maschine);
        var weg = new FunkwegAttrappe { Ziel = Sendeziel.Netz(Crew) };
        bruecke.Anbinden(weg);
        var st = new Sprechsteuerung(s.Maschine, bruecke, () => new MeldeEntscheid(Kommando, "Kommando", null));
        var klick = s.Maschine.Mischer.Klick!;
        st.SprechenGedrueckt();
        Sprechen(s, 20);
        st.SprechenLosgelassen();
        s.Stille(4);
        st.SprechenGedrueckt();
        Sprechen(s, 10, 66);
        st.SprechenLosgelassen();
        s.Stille(30);
        Pruefe.Gleich("Beginnen Netz [22] | Beenden", Folge(weg), "ein Spruch an die Crew");
        Pruefe.Gleich(1L, klick.EigeneDrueckeGestartet, "ein Klick beim Drücken");
        Pruefe.Gleich(2L, klick.Gestartet, "und einer beim Loslassen");
    }

    [Test]
    public static async Task Nachlauf_AudioGestoppt_KeinRestNachDemNeustart()
    {
        await using var s = await Starten(500);
        var klick = s.Maschine.Mischer.Klick!;
        s.Maschine.SprechtasteGedrueckt();
        Sprechen(s, 20);
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(3);
        Pruefe.Wahr(s.Maschine.Kette.ImNachlauf, "im Nachlauf");
        await s.Maschine.StoppenAsync();
        Pruefe.Falsch(s.Maschine.Kette.ImNachlauf, "Stoppen beendet den Nachlauf");
        var vorher = s.Pakete.Count;
        await s.StartenAsync();
        var start = s.Takte + 1;
        s.Stille(60);
        var danach = s.Pakete.Skip(vorher).ToList();
        // Die Phase von vor dem Stoppen schließt der erste Rahmen des neuen Stroms mit ENDE (ausgeblendet, wie ein Loslassen bis 0.9.2).
        Pruefe.Wahr(danach.Count is > 0 and <= 2 && danach.All(p => p.Takt == start), "nur das ENDE der alten Phase im ersten Rahmen, kein Rest des Nachlaufs");
        Pruefe.Wahr((danach[^1].Kennzeichen & Sprachkennzeichen.Ende) != 0, "mit ENDE");
        Pruefe.Falsch(danach.Any(p => (p.Kennzeichen & Sprachkennzeichen.Beginn) != 0), "keine neue Phase");
        Pruefe.Gleich(1L, klick.Gestartet, "beim Stoppen kein Klick (ohne Wiedergabe hört ihn niemand), auch nicht nach dem Neustart");
        s.Maschine.SprechtasteGedrueckt();
        var druck = s.Takte + 1;
        Sprechen(s, 10, 67);
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(60);
        var (beginne, _) = Grenzen(s.Pakete);
        Pruefe.Gleich(druck + 1, beginne[^1], "ein neuer Druck danach beginnt eine neue Phase (Paket zu 20 ms, ein Takt später fertig)");
        Pruefe.Gleich(2L, klick.EigeneDrueckeGestartet, "mit Klick beim Drücken");
    }

    [Test]
    public static async Task Nachlauf_StromBrichtAb_SchliesstDasLoslassenAb()
    {
        await using var s = await Starten(500);
        var klick = s.Maschine.Mischer.Klick!;
        s.Maschine.SprechtasteGedrueckt();
        Sprechen(s, 20);
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(3);
        Pruefe.Wahr(s.Maschine.Kette.ImNachlauf, "im Nachlauf");
        var alt = s.System.Aufnahme!;
        alt.Abbrechen();
        Pruefe.Wahr(alt.Freigegeben && s.System.Aufnahme is { Freigegeben: false }, "der Strom ist neu geöffnet");
        Pruefe.Falsch(s.Maschine.Kette.ImNachlauf, "der Abbruch beendet den Nachlauf");
        var vorher = s.Pakete.Count;
        var start = s.Takte + 1;
        s.Stille(60);
        var danach = s.Pakete.Skip(vorher).ToList();
        Pruefe.Wahr(danach.Count is > 0 and <= 2 && danach.All(p => p.Takt == start), "nur das ENDE der alten Phase im ersten Rahmen, kein Rest des Nachlaufs");
        Pruefe.Wahr((danach[^1].Kennzeichen & Sprachkennzeichen.Ende) != 0, "mit ENDE");
        Pruefe.Gleich(2L, klick.Gestartet, "die Wiedergabe läuft weiter: Klick beim Loslassen");
        s.Maschine.SprechtasteGedrueckt();
        var druck = s.Takte + 1;
        Sprechen(s, 10, 68);
        s.Maschine.SprechtasteLosgelassen();
        s.Stille(60);
        var (beginne, _) = Grenzen(s.Pakete);
        Pruefe.Gleich(druck + 1, beginne[^1], "ein neuer Druck danach ist kein Fortsetzen: neue Phase (Paket zu 20 ms, ein Takt später fertig)");
        Pruefe.Gleich(2L, klick.EigeneDrueckeGestartet, "mit Klick beim Drücken");
    }

    [Test]
    public static async Task Nachlauf_OhneMikrofon_KlicktBeimLoslassenGleich()
    {
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false);
        s.Maschine.TastenNachlaufMs = 300;
        var klick = s.Maschine.Mischer.Klick!;
        // Nicht gestartet: keine Aufnahme, keine Kette, die das Ende sieht. Der Klick wird wie bisher gleich angefordert (hier nicht gespielt).
        s.Maschine.SprechtasteGedrueckt();
        s.Maschine.SprechtasteLosgelassen();
        Pruefe.Falsch(s.Maschine.Kette.ImNachlauf || s.Maschine.Kette.Sprechtaste, "kein Nachlauf ohne Mikrofon");
        await s.StartenAsync();
        s.Stille(5);
        Pruefe.Gleich(2L, klick.Gestartet, "beide angeforderten Klicks spielen, sobald die Wiedergabe läuft");
        Pruefe.Gleich(0, s.Pakete.Count, "und nichts geht hinaus");
    }
}
