// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Protokoll.Sprache;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Klangstufe C1 im Klangkern (KLANGSTUFE-C-ENTWURF.md im Planungsordner, Aufteilung C1): Vorlauf 120 ms von außen einstellbar und im Rahmen des
/// Protokolls (höchstens 7 PREROLL-Pakete), Korridor relativ zur gelernten Funkstimme (Mindest-Funklautstärke als Untergrenze in Automatisch
/// und Lernend, Obergrenze Stimme minus 6 dB, Wünsche per Maximum), Pegelstufe lernt nur Freigegebenes, keine Verklemmung bei leisem Mikrofon,
/// Einmessen mit zwei Sätzen.
/// </summary>
internal static class KlangC1Tests
{
    private const int R = 480;

    /// <summary>Rahmen mit Sinus (200 Hz) auf gegebenem Pegel (dBFS, mittlere Leistung).</summary>
    private static float[] Ton(int rahmen, float dbfs, int versatz = 0)
    {
        var x = new float[rahmen * R];
        var a = MathF.Sqrt(2f) * MathF.Pow(10f, dbfs / 20f);
        for (var i = 0; i < x.Length; i++) x[i] = a * MathF.Sin(2f * MathF.PI * 200f * (i + versatz) / 48000f);
        return x;
    }

    private static float[] Folge(params float[][] teile) => [.. teile.SelectMany(t => t)];

    /// <summary>Läuft eine Folge durch Führung und Gate (Wahrscheinlichkeit fest), liefert je Rahmen offen und Linie.</summary>
    private static (bool[] Offen, float[] Linie) Lauf(GateFuehrung f, float[] signal, float p)
    {
        var n = signal.Length / R;
        var offen = new bool[n];
        var linie = new float[n];
        var puffer = new float[R];
        for (var r = 0; r < n; r++)
        {
            var rahmen = signal.AsSpan(r * R, R);
            var pGate = f.Vorbereiten(Pegel.Dbfs(rahmen), p, false);
            linie[r] = f.AnzeigeLinieDb;
            f.Gate.Verarbeiten(rahmen, pGate);
            while (f.Gate.Holen(puffer, out _, out _))
            {
            }
            offen[r] = f.Gate.Zustand is GateZustand.Senden or GateZustand.Nachlauf;
        }
        return (offen, linie);
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 1: Vorpuffer
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C1_Vorlauf_VonAussenEinstellbar_AbWerk120()
    {
        Pruefe.Gleich(120, GateVorgaben.Standard.VorlaufMs, "Voreinstellung 120 ms");
        var f = new GateFuehrung { Einstellung = GateVorgaben.Standard };
        Pruefe.Gleich(120, f.Gate.VorlaufMs, "ab Werk 120 ms am Gate");
        f.Einstellung = GateVorgaben.Standard with { VorlaufMs = 80 };
        Pruefe.Gleich(80, f.Gate.VorlaufMs, "von außen auf 80 ms gestellt");
        Pruefe.Gleich(GateVorgaben.MaxVorlaufMs, GateVorgaben.Bereinigen(GateVorgaben.Standard with { VorlaufMs = 999 }).VorlaufMs, "höchstens 150 ms");
        Pruefe.Gleich(70, GateVorgaben.Bereinigen(GateVorgaben.Standard with { VorlaufMs = 75 }).VorlaufMs, "ganze 10 ms");
        Pruefe.Gleich(0, GateVorgaben.Bereinigen(GateVorgaben.Standard with { VorlaufMs = -5 }).VorlaufMs, "nicht negativ");

        // Bestätigung in 4 Rahmen: die 12 Rahmen vor dem ersten Anlauf-Rahmen gehen beim Öffnen mit hinaus.
        var g = new SprachGate { VorlaufMs = 120 };
        var puffer = new float[R];
        var erster = -1L;
        var stille = new float[R];
        for (var r = 0; r < 150; r++) g.Verarbeiten(stille, 0f);
        var ton = Ton(4, -20f);
        for (var r = 0; r < 4; r++)
        {
            g.Verarbeiten(ton.AsSpan(r * R, R), 0.95f);
            while (g.Holen(puffer, out var art, out var nr))
            {
                if ((art & SenderahmenArt.Beginn) != 0) erster = nr;
            }
        }
        Pruefe.Gleich(150L - 12, erster, "erster gesendeter Rahmen: 120 ms vor dem Anlauf");
    }

    /// <summary>
    /// Was die Sendeaufbereitung bei 120 ms Vorlauf aus einer Phase macht: Kennzeichen je Paket, für schnelle (4 Rahmen) und langsame
    /// Bestätigung (7 und 10 Rahmen; nur jeder dritte Rahmen erfüllt die Bedingung).
    /// </summary>
    private static List<(Sprachkennzeichen K, int Rahmen, int PaketImAufruf, int PaketeImAufruf)> Phase(int bestaetigtNach)
    {
        var s = new Sendeaufbereitung(new SprachGate(new GateEinstellungen { VorlaufMs = 120 }), new AttrappenKodierer { Mitschreiben = false });
        var aus = new List<(Sprachkennzeichen, int, int, int)>();
        var rahmen = 0;
        void Schritt(float a, float p)
        {
            var x = Ton(1, a <= 0 ? -100f : -20f, rahmen * R);
            if (a <= 0) Array.Clear(x);
            var n = s.Verarbeiten(x, p);
            var audio = 0;
            for (var i = 0; i < n; i++)
            {
                if (s.Paket(i).Length > 0) audio++;
            }
            var j = 0;
            for (var i = 0; i < n; i++) aus.Add((s.Kennzeichen(i), rahmen, s.Paket(i).Length > 0 ? j++ : -1, audio));
            rahmen++;
        }
        for (var r = 0; r < 150; r++) Schritt(0f, 0f);
        // Anlauf: Rahmen mit Pegel und p 0,95 erfüllen; bei langsamer Bestätigung nur jeder dritte (p 0,3 sonst).
        var schritt = bestaetigtNach == 4 ? 1 : 3;
        for (var r = 0; r < 60; r++) Schritt(1f, r < bestaetigtNach && r % schritt != 0 ? 0.3f : 0.95f);
        for (var r = 0; r < 60; r++) Schritt(0f, 0f);
        return aus;
    }

    [Test]
    public static void C1_Vorlauf120_HoechstensSiebenPreroll_ProtokollUndEmpfaengerNehmenJedePhase()
    {
        foreach (var k in new[] { 4, 7, 10 })
        {
            var pakete = Phase(k);
            var audio = pakete.Where(p => (p.K & Sprachkennzeichen.Ende) == 0).ToList();
            var preroll = audio.Count(p => (p.K & Sprachkennzeichen.Preroll) != 0);
            PruefeWert.Hoechstens(SprechphasenGeber.MaxPreroll, preroll, $"Bestätigung nach {k} Rahmen: PREROLL-Pakete");
            PruefeWert.Mindestens(5, preroll, $"Bestätigung nach {k} Rahmen: der Vorlauf geht als Burst hinaus");
            Pruefe.Wahr(audio.Take(preroll).All(p => (p.K & Sprachkennzeichen.Preroll) != 0), $"k={k}: PREROLL nur vorn");
            Pruefe.Wahr((audio[0].K & Sprachkennzeichen.Beginn) != 0, $"k={k}: das erste Paket trägt BEGINN");
            // Das erste Paket ohne PREROLL schließt mit dem laufenden Rahmen ab, geht also in Echtzeit hinaus.
            var erstesEcht = audio[preroll];
            Pruefe.Gleich(erstesEcht.PaketeImAufruf - 1, erstesEcht.PaketImAufruf, $"k={k}: das erste Paket ohne PREROLL ist das letzte des Bursts");

            // Der Sender (Funk.Protokoll) wirft beim achten PREROLL-Paket; er muss jede Phase annehmen.
            var geber = new SprechphasenGeber(0, 7);
            geber.Beginnen(0, sprachaktivierung: true);
            foreach (var p in audio) _ = geber.Audio((p.K & Sprachkennzeichen.Preroll) != 0);
            for (var i = 0; i < SprechphasenGeber.EndeWiederholungen; i++) _ = geber.Ende();
        }

        // Empfänger: viele Phasen mit langsamer Bestätigung, ohne Netzjitter. Der Burst darf die Laufzeitstatistik nicht aufblähen: Die
        // Zielverzögerung bleibt am Minimum, und jedes Paket wird gespielt.
        var dek = new AttrappenDekodierer();
        var uhr = new SimUhr();
        var jp = new Jitterpuffer(dek, uhrUs: uhr.Lesen);
        var mischer = new Mischer();
        mischer.QuelleHinzufuegen(jp, 2);
        var aus = new float[R * 2];
        ushort phase = 10;
        uint frame = 0;
        long basisUs = 0;
        var gesendetAudio = 0;
        for (var wdh = 0; wdh < 12; wdh++)
        {
            var pakete = Phase(wdh % 2 == 0 ? 10 : 7);
            phase++;
            var erster = true;
            foreach (var (k, rahmen, imAufruf, imGanzen) in pakete)
            {
                var sendeUs = basisUs + (rahmen + 1) * 10_000L;
                if (erster && (k & Sprachkennzeichen.Beginn) != 0)
                {
                    // Frame-Nr an der Sitzungsuhr: die Aufnahmezeit des ersten Burst-Pakets.
                    frame = (uint)((sendeUs - (imGanzen - imAufruf) * 20_000L) / 20_000L);
                    erster = false;
                }
                var ankunft = sendeUs + 20_000;
                while (uhr.Us + 10_000 <= ankunft)
                {
                    uhr.Us += 10_000;
                    mischer.Mischen(aus, 2);
                }
                jp.Einreihen(ankunft, phase, frame++, k, k.HasFlag(Sprachkennzeichen.Ende) ? [] : BitConverter.GetBytes(frame - 1).Concat(new byte[2]).ToArray());
                if (!k.HasFlag(Sprachkennzeichen.Ende)) gesendetAudio++;
            }
            basisUs += pakete[^1].Rahmen * 10_000L + 200_000L;
        }
        for (var i = 0; i < 100; i++)
        {
            uhr.Us += 10_000;
            mischer.Mischen(aus, 2);
        }
        var st = jp.Statistik;
        Pruefe.Gleich(12L, st.Phasen, "zwölf Phasen");
        Pruefe.Gleich(0L, st.SpaetVerworfen, "kein Paket zu spät");
        PruefeWert.Mindestens(gesendetAudio - 12, dek.Normal, "alle Pakete normal gespielt (je Phase höchstens eins durch die Blende)");
        PruefeWert.Hoechstens(40.0, st.ZielBerechnetMs, "Zielverzögerung bleibt am Minimum");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Freigabe über den Vorpuffer: eine Phase erst, wenn ihr Anlauf insgesamt laut genug ist
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Ein Gate mit 120 ms Vorlauf, Mindest-Funklautstärke -35 dBFS, nach 1,5 s Stille; liefert je Rahmen die gesendeten Nummern.</summary>
    private static (SprachGate Gate, Func<float[], float, bool, List<long>> Lauf) MindestGate(float mindest)
    {
        var g = new SprachGate(new GateEinstellungen { VorlaufMs = 120 }) { MindestDbfs = mindest };
        var puffer = new float[R];
        var nr = 0;
        List<long> Lauf(float[] signal, float p, bool taste)
        {
            var gesendet = new List<long>();
            for (var r = 0; r < signal.Length / R; r++)
            {
                g.Verarbeiten(signal.AsSpan(r * R, R), p, 0f, taste);
                while (g.Holen(puffer, out _, out var nummer)) gesendet.Add(nummer);
                nr++;
            }
            return gesendet;
        }
        Lauf(new float[R * 150], 0f, false);
        return (g, Lauf);
    }

    [Test]
    public static void C1_Freigabe_LeisePhaseBleibtZurueck_LauteMitVorlauf_ImSatzOffen_TasteImmer()
    {
        // Leise wie zur Familie (5 dB unter der Mindest-Funklautstärke), mit sicherer Sprache: Das Gate erkennt Sprache, hält sie aber zurück.
        var (g, lauf) = MindestGate(-35f);
        Pruefe.Gleich(0, lauf(Ton(80, -40f), 0.95f, false).Count, "leise: nichts gesendet");
        Pruefe.Gleich(GateZustand.Zurueckgehalten, g.Zustand, "zurückgehalten, solange sie spricht");
        Pruefe.Gleich(0, lauf(new float[R * 40], 0f, false).Count, "danach Stille: nichts");
        Pruefe.Gleich(GateZustand.Leise, g.Zustand, "und wieder leise");
        Pruefe.Gleich(1L, g.ZurueckgehalteneAnlaeufe, "eine zurückgehaltene Phase");

        // Normal mit leisem Anlaut (50 ms bei -45, dann -20; die Freigabe verlangt im Fenster Mindest plus 4 dB Zuschlag): Sie öffnet, und der Burst reicht vor den Anlaut zurück.
        var start = g.Rahmennummer;
        var normal = lauf(Folge(Ton(5, -45f), Ton(40, -20f, 5 * R)), 0.95f, false);
        Pruefe.Wahr(normal.Count > 0, "normal: gesendet");
        PruefeWert.Hoechstens(start, normal.Min(), "der leise Anlaut ging mit hinaus (Vorlauf)");
        // Im Satz leiser (unter die Mindest-Funklautstärke, über der Linie): bleibt offen, nichts wird abgeschnitten.
        var leiserImSatz = lauf(Ton(30, -40f), 0.95f, false);
        Pruefe.Gleich(30, leiserImSatz.Count, "im Satz leiser: jeder Rahmen geht hinaus");
        // Nach einer kurzen Pause (Komma: 300 ms Nachlauf und 200 ms) leise weiter: Fortsetzung desselben Spruchs, geht hinaus.
        lauf(new float[R * 50], 0f, false);
        Pruefe.Wahr(lauf(Ton(30, -40f), 0.95f, false).Count > 0, "Fortsetzung nach kurzer Pause: gesendet");
        lauf(new float[R * 80], 0f, false);

        // Leise beginnen, dann lauter werden: öffnet, sobald das Fenster laut genug ist, mit dem Vorlauf davor.
        var lauterAb = g.Rahmennummer + 60;
        var spaeter = lauf(Folge(Ton(60, -40f), Ton(30, -20f)), 0.95f, false);
        Pruefe.Wahr(spaeter.Count > 0, "erst leise, dann laut: gesendet");
        PruefeWert.Hoechstens(lauterAb - 5, spaeter.Min(), "der laute Teil samt Vorlauf");
        lauf(new float[R * 40], 0f, false);

        // Die Sprechtaste sendet immer, auch leise.
        Pruefe.Gleich(20, lauf(Ton(20, -45f), 0f, true).Count, "Sprechtaste: jeder Rahmen");
        lauf(new float[R], 0f, false);

        // Ohne Mindest-Funklautstärke wie bisher: leise Sprache öffnet.
        var (_, ohne) = MindestGate(float.NegativeInfinity);
        Pruefe.Wahr(ohne(Ton(80, -40f), 0.95f, false).Count > 0, "ohne Mindest: leise Sprache geht hinaus");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkte 2 und 3: Korridor relativ zur gelernten Funkstimme
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C1_Korridor_OhneGelernteStimme_WieBisher_KeineUntergrenze()
    {
        var f = new GateFuehrung { Einstellung = GateVorgaben.Standard };
        var (offen, linie) = Lauf(f, Folge(new float[R * 300], Ton(60, -50f)), 0.95f);
        Pruefe.Wahr(float.IsNaN(f.Korridor.UntergrenzeDb), "ohne Stimme keine Untergrenze");
        Pruefe.Gleich(GateVorgaben.MinLinieAutomatischDb, linie[290], "ruhiger Raum: die Linie wie bisher bei -60 dBFS");
        Pruefe.Wahr(offen[^1], "leise Sprache (-50 dBFS) sendet wie bisher");
    }

    [Test]
    public static void C1_Korridor_MitStimme_UntergrenzeInAutomatischUndLernend()
    {
        foreach (var modus in new[] { GateModus.Automatisch, GateModus.Lernend })
        {
            var e = GateVorgaben.Standard with { Modus = modus, KorridorVonDb = -58f, KorridorBisDb = -35f, MindestAbstandDb = 15f };
            var f = new GateFuehrung { Einstellung = e, StimmeDb = -23f };
            var (offenLeise, linie) = Lauf(f, Folge(new float[R * 300], Ton(60, -42f)), 0.95f);
            PruefeWert.Nahe(-38, linie[290], 0.01, $"{modus}: im Ruhigen an der Untergrenze (Stimme -23 minus 15 dB)");
            PruefeWert.Nahe(-38, f.Korridor.UntergrenzeDb, 0.01, $"{modus}: Untergrenze im Stand");
            PruefeWert.Nahe(-23, f.Korridor.StimmeDb, 0.01, $"{modus}: Stimme im Stand");
            Pruefe.Falsch(offenLeise.Any(o => o), $"{modus}: 4 dB unter der Mindest-Funklautstärke sendet nicht");

            var g = new GateFuehrung { Einstellung = e, StimmeDb = -23f };
            var (offenLaut, _) = Lauf(g, Folge(new float[R * 300], Ton(60, -28f)), 0.95f);
            Pruefe.Wahr(offenLaut[^1], $"{modus}: über Mindest plus Zuschlag (4 dB) sendet");
        }

        // Im Satz leiser werden (unter die Mindest-Funklautstärke): Die Phase bleibt offen, kein Wort wird abgeschnitten. Mit der
        // Mindest-Funklautstärke als Untergrenze der Linie (der erste Bau von C1) schloss das Gate nach 300 ms.
        var satz = new GateFuehrung { Einstellung = GateVorgaben.Standard with { MindestAbstandDb = 15f }, StimmeDb = -23f };
        var (offenSatz, _) = Lauf(satz, Folge(new float[R * 300], Ton(60, -25f), Ton(60, -42f, 60 * R)), 0.95f);
        Pruefe.Wahr(offenSatz.Skip(300 + 60).All(o => o), "im Satz 4 dB unter der Mindest-Funklautstärke: bleibt offen");

        // Der Abstand ist einstellbar und folgt der Stimme.
        var h = new GateFuehrung { Einstellung = GateVorgaben.Standard with { MindestAbstandDb = 10f }, StimmeDb = -30f };
        var (_, l2) = Lauf(h, new float[R * 100], 0.9f);
        PruefeWert.Nahe(-40, l2[^1], 0.01, "Stimme -30, Abstand 10: Untergrenze -40");
    }

    [Test]
    public static void C1_Korridor_Obergrenze_StimmeMinusSechs_AuchBeimLernen()
    {
        // Lauter Hintergrund (Rauschen -30 dBFS): Die Automatik wollte Boden plus 9 dB, also etwa -21; die Obergrenze ist Stimme minus 6.
        var auto = new GateFuehrung { Einstellung = GateVorgaben.Standard, StimmeDb = -23f };
        var (_, la) = Lauf(auto, AnbindungSignale.Rauschen(48000 * 4, -30, 3), 0f);
        PruefeWert.Nahe(-29, la[^1], 0.01, "Automatisch: nie über Stimme minus 6 dB");
        PruefeWert.Nahe(-29, auto.Korridor.ObergrenzeDb, 0.01, "Obergrenze im Stand");

        var lern = new GateFuehrung
        {
            Einstellung = GateVorgaben.Standard with { Modus = GateModus.Lernend, AbstandDb = 6f, KorridorVonDb = -90f, KorridorBisDb = -5f, AnstiegSekunden = 1f },
            StimmeDb = -23f,
        };
        var (_, ll) = Lauf(lern, AnbindungSignale.Rauschen(48000 * 6, -30, 4), 0f);
        PruefeWert.Nahe(-29, ll[^1], 0.01, "Lernend: der alte Korridor bis -5 gilt nicht mehr, Stimme minus 6 dB deckelt");

        var weiter = new GateFuehrung { Einstellung = GateVorgaben.Standard with { ObergrenzeAbstandDb = 10f }, StimmeDb = -23f };
        var (_, lw) = Lauf(weiter, AnbindungSignale.Rauschen(48000 * 4, -30, 3), 0f);
        PruefeWert.Nahe(-33, lw[^1], 0.01, "Obergrenze 10 dB unter der Stimme");
        Pruefe.Gleich(GateVorgaben.MinObergrenzeAbstandDb, GateVorgaben.Bereinigen(GateVorgaben.Standard with { ObergrenzeAbstandDb = 2f }).ObergrenzeAbstandDb,
            "weniger als 6 dB unter der Stimme geht nicht");
    }

    [Test]
    public static void C1_Korridor_WuenscheProMaximum_NieAddiert_FestUnberuehrt()
    {
        // Ruhiger Raum: Der Modus wünscht -60, die Untergrenze liegt bei -38 (Stimme -23).
        var f = new GateFuehrung { Einstellung = GateVorgaben.Standard with { MindestAbstandDb = 15f }, StimmeDb = -23f };
        var still = new float[R * 50];
        f.ZusatzWunschDb = -34f;
        var (_, l1) = Lauf(f, still, 0f);
        PruefeWert.Nahe(-34, l1[^1], 0.01, "ein zweiter Wunsch über der Untergrenze: das Maximum");
        f.ZusatzWunschDb = -45f;
        var (_, l2) = Lauf(f, still, 0f);
        PruefeWert.Nahe(-38, l2[^1], 0.01, "ein Wunsch unter der Untergrenze ändert nichts");
        f.ZusatzWunschDb = -10f;
        var (_, l3) = Lauf(f, still, 0f);
        PruefeWert.Nahe(-29, l3[^1], 0.01, "auch ein zweiter Wunsch bleibt unter der Obergrenze");

        // Beide heben an (Boden -45 plus 9 = -36; Wunsch -35): Ergebnis ist das Maximum, nicht die Summe der beiden Anhebungen.
        var g = new GateFuehrung { Einstellung = GateVorgaben.Standard with { MindestAbstandDb = 40f }, StimmeDb = -10f, ZusatzWunschDb = -35f };
        var (_, l4) = Lauf(g, AnbindungSignale.Rauschen(48000 * 4, -45, 5), 0f);
        var boden = g.Gate.RauschbodenDbfs;
        PruefeWert.Nahe(MathF.Max(boden + 9f, -35f), l4[^1], 0.3, "Maximum aus Boden plus Abstand und Wunsch");

        // Fest: Die Linie bestimmt der Funker selbst, die Stimme ändert nichts. (Einen Wunsch des Wächters bei Raumgeräusch nimmt Fest seit
        // Klangstufe C2a an, Q9; geprüft in KlangC2WaechterTests.C2_GateFuehrung_ZusatzWunsch_AuchImModusFest_NieUeberDieObergrenze.)
        var fest = new GateFuehrung { Einstellung = GateVorgaben.Standard with { Modus = GateModus.Fest, SchwelleDb = -50f }, StimmeDb = -23f };
        var (_, lf) = Lauf(fest, Ton(50, -80f), 1f);
        Pruefe.Wahr(lf.All(l => l == -50f), "Fest: die gezogene Schwelle");
    }

    [Test]
    public static void C1_Bereinigen_KorridorAbstaende()
    {
        var e = GateVorgaben.Bereinigen(GateVorgaben.Standard with { MindestAbstandDb = 7f, ObergrenzeAbstandDb = 6f });
        Pruefe.Gleich(9f, e.MindestAbstandDb, "Untergrenze mindestens 3 dB unter der Obergrenze, sie rückt nach unten");
        Pruefe.Gleich(GateVorgaben.MindestAbstandAbWerkDb, GateVorgaben.Bereinigen(GateVorgaben.Standard with { MindestAbstandDb = float.NaN }).MindestAbstandDb, "NaN: ab Werk");
        Pruefe.Gleich(GateVorgaben.MaxMindestAbstandDb, GateVorgaben.Bereinigen(GateVorgaben.Standard with { MindestAbstandDb = 500f }).MindestAbstandDb, "höchstens 60 dB");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 4: Pegelstufe lernt nur Freigegebenes
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static void C1_Lernfreigabe_Wahrheitstafel()
    {
        var normal = new LernfreigabeLage(Gesendet: true, Sprachwahrscheinlichkeit: 0.95f, Echoverdacht: false);
        Pruefe.Wahr(Lernfreigabe.Pruefen(normal), "eigene Stimme, gesendet, sichere Sprache");
        Pruefe.Falsch(Lernfreigabe.Pruefen(normal with { Gesendet = false }), "nicht gesendet");
        Pruefe.Falsch(Lernfreigabe.Pruefen(normal with { Sprachwahrscheinlichkeit = 0.8f }), "unsichere Sprache");
        Pruefe.Falsch(Lernfreigabe.Pruefen(normal with { Echoverdacht = true }), "Echoverdacht");
        Pruefe.Falsch(Lernfreigabe.Pruefen(normal with { WaechterEigeneStimme = false }), "der Wächter (C2) sagt: nicht die eigene Stimme");
        // Die Mindest-Funklautstärke prüft die Pegelstufe je Äußerung (C1_Kette_LeiseUnterDerUntergrenze_LerntNicht_MitTasteSchon).
    }

    [Test]
    public static void C1_Pegelstufe_LerntNurAusFreigegebenenRahmen()
    {
        static float Lauf(bool freigegeben)
        {
            var stufe = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
            var ton = Ton(100, -30f);
            for (var r = 0; r < 100; r++) stufe.Verarbeiten(ton.AsSpan(r * R, R).ToArray(), 0.95f, gesendet: true, freigegeben);
            for (var r = 0; r < 80; r++) stufe.Verarbeiten(new float[R], 0f, gesendet: false, freigegeben: false);
            return stufe.GrundpegelDbfs;
        }
        Pruefe.Wahr(float.IsNaN(Lauf(false)), "gesendet, aber nicht freigegeben: nichts gelernt");
        PruefeWert.Nahe(-30, Lauf(true), 0.2, "freigegeben: gelernt");
    }

    /// <summary>Wahrscheinlichkeit nach Pegel: Sprache über -80 dBFS, darunter Pause (für Ketten ohne RNNoise).</summary>
    private sealed class PegelSchaetzer : ISprachwahrscheinlichkeit
    {
        public float Schaetzen(ReadOnlySpan<float> rahmen10ms) => Pegel.Dbfs(rahmen10ms) > -80f ? 0.95f : 0f;

        public void Zuruecksetzen()
        {
        }
    }

    [Test]
    public static async Task C1_Kette_LeiseUnterDerUntergrenze_LerntNicht_MitTasteSchon()
    {
        // Warmstart: Stimme gelernt (+3 dB, also Grundpegel -26 dBFS vor der Pegelstufe). Viermal ein Ton 24 dB leiser, mit der Sprach-
        // aktivierung gesendet (feste Schwelle tief unten). Vor C1 zählte die dritte solche "Äußerung" als neue Lage, die Pegelstufe drehte auf.
        async Task<float> Grundpegel(bool taste)
        {
            await using var s = new Pruefstand(mitApm: false, mitRnnoise: false, schaetzer: new PegelSchaetzer());
            s.Maschine.Lautstaerke = new LautstaerkeEinstellung(true, 3f, Gelernt: true);
            s.Maschine.Gate = GateVorgaben.Standard with { Modus = GateModus.Fest, SchwelleDb = -85f, MindestAbstandDb = 15f };
            await s.StartenAsync();
            s.Stille(20);
            for (var i = 0; i < 4; i++)
            {
                if (taste) s.Maschine.SprechtasteGedrueckt();
                s.Abspielen(Ton(100, -50f));
                if (taste) s.Maschine.SprechtasteLosgelassen();
                s.Stille(100);
            }
            return s.Maschine.Kette.Pegelstufe!.GrundpegelDbfs;
        }
        PruefeWert.Nahe(-26, await Grundpegel(taste: false), 0.1, "Sprachaktivierung unter der Mindest-Funklautstärke: Grundpegel bleibt");
        PruefeWert.Hoechstens(-27.0, await Grundpegel(taste: true), "mit der Sprechtaste zählt es (dritte Äußerung in Folge)");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Verklemmung (Hinweis aus Sarevocs 0.6.0): leises Mikrofon, Pegelstufe ungelernt, Untergrenze ab Werk
    // ------------------------------------------------------------------------------------------------------------------------------

    [Test]
    public static async Task C1_Verklemmung_LeisesMikrofon_UngelernteStufe_SendetLerntUndGrenztDannEin()
    {
        // Wie bei Sarevoc: Sprache am Gate um -50 dBFS (Korpus 30 dB leiser, Pegelstufe auf dem Startwert +6 dB, nichts gelernt).
        var eingang = MessungGegenpruefungKlangA.Stueck(Pumpmessung.Eingang(-30f), 0, 14);
        var korridore = new List<KorridorStand>();
        var l = await MessungGegenpruefungKlangA.Ablauf(new LautstaerkeEinstellung(true, Pegelstufe.StartDb), [new("Nutzer", eingang, true)],
            einrichten: s => s.Maschine.Gate = GateVorgaben.Standard, jeTakt: (s, _) => korridore.Add(s.Maschine.Korridor));
        var t = l.T;
        var ersteZehn = t.Take(1000).Count(e => e.Sendet);
        PruefeWert.Mindestens(100, ersteZehn, "in den ersten 10 s wird gesendet (keine Untergrenze, solange die Stimme unbekannt ist)");
        PruefeWert.Mindestens(20.0, t[^1].PegelstufeDb, "die Pegelstufe hat gelernt und aufgedreht");
        var ab = korridore.FindIndex(k => k.StimmeBekannt);
        Pruefe.Wahr(ab > 0, "die Stimme gilt irgendwann als gelernt");
        var k = korridore[^1];
        PruefeWert.Nahe(k.MindestBezugDb - GateVorgaben.MindestAbstandAbWerkDb, k.UntergrenzeDb, 0.01, "dann wirkt die Untergrenze, relativ zum Bezug der Stimme");
        // Nach dem Einsetzen der Untergrenze geht weiter jede Äußerung hinaus.
        var a = Klangkorpus.Aeusserungen;
        // Stueck beginnt 300 ms vor der ersten Äußerung, auf ganze Rahmen.
        var versatz = Math.Max(0, a[0].Start - 14400) / R * R - Pumpmessung.VerzugGate;
        var spaeter = a.Take(14).Where(x => (x.Start - versatz) / R > ab + 100).ToList();
        PruefeWert.Mindestens(3, spaeter.Count, "Äußerungen nach dem Einsetzen");
        foreach (var x in spaeter)
        {
            var von = (x.Start - versatz) / R;
            var bis = Math.Min(t.Count, (x.Ende - versatz) / R);
            var gesendet = t.Skip(von).Take(bis - von).Count(e => e.Sendet);
            PruefeWert.Mindestens(0.5 * (bis - von), gesendet, $"Äußerung {x.Nr} geht hinaus");
        }

        // Gegenprobe: eine feste Grenze bei -38 dBFS (wie die Grenzlinie aus 2c) sperrt dasselbe Mikrofon, und die Pegelstufe lernt nicht.
        // Gemessen: 25 s lang nichts, erst ein Ruf ("Abbrechen, sofort abbrechen!", Korpus laut) bricht nach etwa 30 s durch.
        var fest = await MessungGegenpruefungKlangA.Ablauf(new LautstaerkeEinstellung(true, Pegelstufe.StartDb), [new("Nutzer", eingang, true)],
            einrichten: s => s.Maschine.Gate = GateVorgaben.Standard with { GrenzlinieDb = -38f });
        Pruefe.Gleich(0, fest.T.Take(2500).Count(e => e.Sendet), "mit fester Grenze: 25 s lang nichts gesendet");
        Pruefe.Gleich(Pegelstufe.StartDb, fest.T[2499].PegelstufeDb, "mit fester Grenze: die Pegelstufe bleibt auf dem Startwert (Verklemmung)");
        PruefeWert.Mindestens(1000, t.Take(2500).Count(e => e.Sendet), "relativ zur Stimme in denselben 25 s: gesendet");
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Punkt 5: Einmessen mit zwei Sätzen
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Ein "Satz": 50 Rahmen Sprache (Ton, p 0,95), 20 Rahmen Pause (p 0), wiederholt bis zur Länge.</summary>
    private static void Satz(Mindesteinmessung m, int rahmen, float dbfs, float spitzeDbfs)
    {
        for (var r = 0; r < rahmen; r++)
        {
            var sprache = r % 70 < 50;
            m.Zufuehren(sprache ? dbfs : -70f, sprache ? spitzeDbfs : -60f, sprache ? 0.95f : 0.05f);
        }
    }

    [Test]
    public static void C1_Mindesteinmessung_ZweiSaetze_LinieUnterDemLeisenSatz()
    {
        var m = new Mindesteinmessung();
        m.Starten();
        m.Zufuehren(-70f, -60f, 0f);
        Pruefe.Gleich(MindestEinmessSchritt.Normal, m.Stand.Schritt, "erst der normale Satz");
        Pruefe.Wahr(m.Laeuft, "läuft");
        Satz(m, Mindesteinmessung.SatzRahmen - 1, -30f, -20f);
        Pruefe.Gleich(MindestEinmessSchritt.Umschalten, m.Stand.Schritt, "dann umschalten");
        Satz(m, Mindesteinmessung.UmschaltRahmen, -70f, -60f);
        Pruefe.Gleich(MindestEinmessSchritt.Leise, m.Stand.Schritt, "dann leise");
        Satz(m, Mindesteinmessung.SatzRahmen, -45f, -35f);
        var st = m.Stand;
        Pruefe.Gleich(MindestEinmessSchritt.Fertig, st.Schritt, "fertig");
        PruefeWert.Nahe(-30, st.NormalDbfs, 0.05, "normaler Satz");
        PruefeWert.Nahe(-45, st.LeiseDbfs, 0.05, "leiser Satz");
        PruefeWert.Nahe(7, st.VerstaerkungDb, 0.05, "Grundverstärkung: Ziel -23 minus -30");
        // Linie: 3 dB unter dem leisen Satz (-48), das sind 18 dB unter der Stimme (-30).
        PruefeWert.Nahe(18, st.MindestAbstandDb, 0.01, "Untergrenze 18 dB unter der Stimme: leise minus 3 dB");
        Pruefe.Falsch(m.Laeuft, "läuft nicht mehr");

        // Spitzen: die Verstärkung bleibt so, dass die Spitzen bei -1 dBFS ankommen.
        var n = new Mindesteinmessung();
        n.Starten();
        Satz(n, Mindesteinmessung.SatzRahmen + 1, -30f, -5f);
        Satz(n, Mindesteinmessung.UmschaltRahmen, -70f, -60f);
        Satz(n, Mindesteinmessung.SatzRahmen, -45f, -35f);
        PruefeWert.Nahe(4, n.Stand.VerstaerkungDb, 0.15, "Spitze -5 dBFS: höchstens 4 dB");
    }

    [Test]
    public static void C1_Mindesteinmessung_Werkswerte_LinieLeiseMinusDreiDb_ZuschlagVier()
    {
        // Die milde Einstellung (klang-c1b, MessungKlangC1.Messung_KlangC1_Raster): Linie 3 dB unter dem leisen Satz, Zuschlag der Freigabe 4 dB.
        Pruefe.Gleich(0.0, Mindesteinmessung.LinieUeberLeiseAnteil, "kein Anteil des Unterschieds über dem leisen Satz");
        Pruefe.Gleich(-3.0, Mindesteinmessung.LinieVersatzDb, "Versatz: 3 dB unter dem leisen Satz");
        Pruefe.Gleich(4f, new SprachGate().FreigabeZuschlagDb, "Zuschlag der Freigabe ab Werk");

        // Abstand unter der Stimme = Unterschied plus 3 dB, auf 0,5 dB gerundet; mindestens 9 dB (Korridor), höchstens 60 dB.
        PruefeWert.Nahe(11, Mindesteinmessung.AbstandFuer(8), 0.001, "kleinster brauchbarer Unterschied (8 dB)");
        PruefeWert.Nahe(15, Mindesteinmessung.AbstandFuer(12), 0.001, "12 dB leiser: 15 dB unter der Stimme (nicht mehr 9 wie mit leise plus ein Drittel)");
        PruefeWert.Nahe(18, Mindesteinmessung.AbstandFuer(15), 0.001, "15 dB leiser");
        PruefeWert.Nahe(15.5, Mindesteinmessung.AbstandFuer(12.3), 0.001, "auf 0,5 dB gerundet");
        PruefeWert.Nahe(GateVorgaben.MaxMindestAbstandDb, Mindesteinmessung.AbstandFuer(58), 0.001, "nie weiter als 60 dB");
        // Die Lage ist Sache der Parameter (Messungen): leise plus ein Drittel des Unterschieds ergibt zwei Drittel, nie unter 9 dB.
        PruefeWert.Nahe(10, Mindesteinmessung.AbstandFuer(15, 1.0 / 3.0, 0.0), 0.001, "leise plus ein Drittel bei 15 dB Unterschied");
        PruefeWert.Nahe(9, Mindesteinmessung.AbstandFuer(12, 1.0 / 3.0, 0.0), 0.001, "dieselbe Lage bei 12 dB Unterschied: 8, auf den Korridor (9) angehoben");
        PruefeWert.Nahe(12, Mindesteinmessung.AbstandFuer(12, 0.0, 0.0), 0.001, "Linie bei leise");
    }

    [Test]
    public static void C1_Mindesteinmessung_ZuWenigUnterschied_OderSprache_Abbruch()
    {
        var m = new Mindesteinmessung();
        m.Starten();
        Satz(m, Mindesteinmessung.SatzRahmen + 1, -30f, -20f);
        Satz(m, Mindesteinmessung.UmschaltRahmen, -70f, -60f);
        Satz(m, Mindesteinmessung.SatzRahmen, -36f, -26f);
        Pruefe.Gleich(MindestEinmessSchritt.ZuWenigUnterschied, m.Stand.Schritt, "6 dB Unterschied sind zu wenig");
        PruefeWert.Nahe(-36, m.Stand.LeiseDbfs, 0.05, "die Pegel stehen trotzdem da");

        var still = new Mindesteinmessung();
        still.Starten();
        for (var r = 0; r < Mindesteinmessung.SatzRahmen + Mindesteinmessung.UmschaltRahmen + Mindesteinmessung.SatzRahmen + 1; r++) still.Zufuehren(-70f, -60f, 0.05f);
        Pruefe.Gleich(MindestEinmessSchritt.ZuWenigSprache, still.Stand.Schritt, "ohne Sprache");

        var ab = new Mindesteinmessung();
        ab.Starten();
        Satz(ab, 100, -30f, -20f);
        ab.Abbrechen();
        Pruefe.Gleich(MindestEinmessSchritt.Bereit, ab.Stand.Schritt, "abgebrochen: gleich bereit");
        ab.Zufuehren(-30f, -20f, 0.95f);
        Pruefe.Gleich(MindestEinmessSchritt.Bereit, ab.Stand.Schritt, "und bleibt es");
    }

    [Test]
    public static async Task C1_MindestEinmessen_InDerMaschine_SetztVerstaerkungUndUntergrenze()
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        await s.StartenAsync();
        s.Stille(50);
        s.Maschine.MindestEinmessenStarten();
        var normal = MessungGegenpruefungKlangA.Stueck(Pumpmessung.Eingang(0f), 0, 3)[..(Mindesteinmessung.SatzRahmen * R)];
        var leise = MessungGegenpruefungKlangA.Stueck(Pumpmessung.Eingang(-15f), 0, 3)[..(Mindesteinmessung.SatzRahmen * R)];
        s.Pakete.Clear();
        s.Abspielen(normal);
        s.Abspielen(AnbindungSignale.Rauschen(Mindesteinmessung.UmschaltRahmen * R, Pumpmessung.GrundrauschenDbfs, 3));
        s.Abspielen(leise);
        s.Stille(5);
        var st = s.Maschine.MindestEinmessen;
        Pruefe.Gleich(MindestEinmessSchritt.Fertig, st.Schritt, "fertig");
        Pruefe.Gleich(0, s.Pakete.Count(p => (p.Kennzeichen & Sprachkennzeichen.Sprachaktivierung) != 0), "während des Einmessens sendet die Sprachaktivierung nicht");
        PruefeWert.Nahe(15, st.NormalDbfs - st.LeiseDbfs, 1.5, "der leise Satz ist 15 dB leiser");
        PruefeWert.Nahe(18, st.MindestAbstandDb, 1.5, "Untergrenze 3 dB unter dem leisen Satz, also Unterschied plus 3 dB unter der Stimme");
        PruefeWert.Nahe(st.NormalDbfs - st.LeiseDbfs + 3, st.MindestAbstandDb, 0.3, "genau Unterschied plus 3 dB (auf 0,5 dB gerundet)");

        // Der Funker übernimmt: Verstärkung und Untergrenze auf einmal; die Stimme gilt sofort als gelernt.
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(true, st.VerstaerkungDb, Gelernt: true, Eingemessen: true);
        s.Maschine.Gate = GateVorgaben.Standard with { MindestAbstandDb = st.MindestAbstandDb };
        s.Stille(5);
        var k = s.Maschine.Korridor;
        Pruefe.Wahr(k.StimmeBekannt, "Stimme bekannt");
        PruefeWert.Nahe(Pegelstufe.ZielDbfs, k.StimmeDb, 0.5, "die Stimme liegt nach dem Einmessen am Ziel");
        PruefeWert.Nahe(k.StimmeDb - st.MindestAbstandDb, k.UntergrenzeDb, 0.01, "Untergrenze");
        PruefeWert.Nahe(st.VerstaerkungDb, s.Maschine.Kette.Pegelstufe!.VerstaerkungDb, 0.01, "Verstärkung übernommen");
    }

    /// <summary>
    /// 0.10.0 (E4a): Hält das Gate eine bestätigte Phase unter der Mindest-Funklautstärke zurück, sagt es die Pegelmeldung
    /// (<see cref="PegelMeldung.Zurueckgehalten"/>); die Statuszeile macht daraus "Stimme unter der Mindest-Funklautstärke". Eine Phase, die
    /// hinausgeht, die Sprechtaste und Mikro stumm melden es nie. Eine Ableitung in der Oberfläche aus der Untergrenze wäre ungenau, weil die
    /// Freigabe den Anlauf über den Vorpuffer misst.
    /// </summary>
    [Test]
    public static async Task C1_Zurueckgehalten_InDerPegelmeldung()
    {
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false, schaetzer: new PegelSchaetzer());
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(true, 3f, Gelernt: true, Eingemessen: true);
        s.Maschine.Gate = GateVorgaben.Standard;
        await s.StartenAsync();
        s.Stille(100);
        var k = s.Maschine.Korridor;
        Pruefe.Wahr(k.StimmeBekannt && float.IsFinite(k.UntergrenzeDb), "Stimme bekannt: es gibt eine Mindest-Funklautstärke (" + k.UntergrenzeDb + ")");

        // Leise wie zu jemandem im Raum: am Gate zwei dB unter der Mindest-Funklautstärke, über der Linie, sicher Sprache.
        var leise = k.UntergrenzeDb - 2f - 3f;
        s.Meldungen.Clear();
        s.Pakete.Clear();
        s.Abspielen(Ton(80, leise));
        var zurueck = s.Meldungen.Count(m => m.Zurueckgehalten);
        Pruefe.Wahr(zurueck > 10, "leise: die Meldungen sagen zurückgehalten (" + zurueck + " von " + s.Meldungen.Count + ")");
        Pruefe.Wahr(s.Meldungen.Where(m => m.Zurueckgehalten).All(m => !m.Sendet && !m.Stumm), "zurückgehalten heißt: nichts hinaus");
        Pruefe.Gleich(0, s.Pakete.Count, "und es ging nichts hinaus");
        s.Stille(80);

        // Laut wie am Funk: geht hinaus, danach nicht mehr zurückgehalten.
        s.Meldungen.Clear();
        s.Abspielen(Ton(80, -26f));
        Pruefe.Wahr(s.Meldungen.Skip(10).All(m => m.Sendet && !m.Zurueckgehalten), "laut: gesendet, nicht zurückgehalten");
        s.Stille(80);

        // Die Sprechtaste kennt keine Mindest-Funklautstärke.
        s.Meldungen.Clear();
        s.Maschine.SprechtasteGedrueckt();
        s.Abspielen(Ton(40, leise));
        s.Maschine.SprechtasteLosgelassen();
        Pruefe.Wahr(s.Meldungen.Count > 0 && s.Meldungen.All(m => !m.Zurueckgehalten), "Sprechtaste: nie zurückgehalten");
        s.Stille(80);

        // Mikro stumm verwirft jeden Rahmen.
        s.Meldungen.Clear();
        s.Maschine.MikroStumm = true;
        s.Abspielen(Ton(40, leise));
        Pruefe.Wahr(s.Meldungen.Count > 0 && s.Meldungen.All(m => m.Stumm && !m.Zurueckgehalten), "Mikro stumm: nie zurückgehalten");
    }

    [Test]
    public static async Task C1_Eingemessen_GiltAuchNachDemLernen()
    {
        // Hat die Halteregel schon gelernt, bliebe ein neuer Startwert ohne Wirkung; ein eingemessener gilt sofort.
        await using var s = new Pruefstand(mitApm: false, mitRnnoise: false, schaetzer: new PegelSchaetzer());
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(true, 3f, Gelernt: true);
        await s.StartenAsync();
        s.Stille(10);
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(true, 12f, Gelernt: true);
        s.Stille(5);
        PruefeWert.Nahe(3, s.Maschine.Kette.Pegelstufe!.VerstaerkungDb, 0.01, "gelernt: ein neuer Startwert gilt nicht");
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(true, 12f, Gelernt: true, Eingemessen: true);
        s.Stille(5);
        PruefeWert.Nahe(12, s.Maschine.Kette.Pegelstufe!.VerstaerkungDb, 0.01, "eingemessen: gilt sofort");
        PruefeWert.Nahe(Pegelstufe.ZielDbfs, s.Maschine.Korridor.StimmeDb, 0.01, "und die Stimme gilt als gelernt, am Ziel");
    }
}
