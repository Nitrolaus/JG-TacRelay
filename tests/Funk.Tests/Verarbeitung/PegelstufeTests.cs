// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

/// <summary>
/// Pegelstufe der Sendekette (Klangstufe A): Festverstärkung, satzweise Halteregel und Einmessen, ohne Klangmaschine. Die
/// Sprachwahrscheinlichkeit kommt hier direkt aus dem Test (1 = Sprache, 0 = Pause), die Rahmen sind Rauschen mit festem Pegel.
/// Die Regel: Innerhalb einer Äußerung ändert sich die Verstärkung nie; erst nach <see cref="Pegelstufe.PauseRahmen"/> Rahmen
/// Pause darf sie sich bewegen, um höchstens einen Schritt je Pause.
/// </summary>
internal static class PegelstufeTests
{
    private static float[] Rahmen(float dbfs, int seed) => AnbindungSignale.Rauschen(480, dbfs, (ulong)seed);

    /// <summary>Schickt <paramref name="anzahl"/> Rahmen durch und liefert die zurückgegebenen Verstärkungen.</summary>
    private static List<float> Folge(Pegelstufe s, int anzahl, float dbfs, float p, ref int seed)
    {
        var g = new List<float>();
        for (var i = 0; i < anzahl; i++) g.Add(s.Verarbeiten(Rahmen(dbfs, seed++), p));
        return g;
    }

    private static float Spanne(List<float> g) => g.Max() - g.Min();

    [Test]
    public static void Pegelstufe_Fest_KonstanteVerstaerkungAufDenSampleGenau()
    {
        var s = new Pegelstufe(new LautstaerkeEinstellung(false, 9f));
        var faktor = Pegel.DbZuLinear(9f);
        for (var r = 0; r < 300; r++)
        {
            var ein = Rahmen(r % 7 == 0 ? -20f : -45f, r);
            var aus = (float[])ein.Clone();
            var g = s.Verarbeiten(aus, r % 3 == 0 ? 1f : 0f);
            Pruefe.Gleich(9f, g, $"Rahmen {r}: Verstärkung");
            for (var i = 0; i < 480; i++) PruefeWert.Nahe(ein[i] * faktor, aus[i], 1e-6, $"Rahmen {r}, Sample {i}");
        }
        Pruefe.Gleich(new LautstaerkeEinstellung(false, 9f), s.Einstellung, "Einstellung");
    }

    [Test]
    public static void Pegelstufe_Halteregel_InnerhalbDerAeusserungKonstant()
    {
        // Start 0 dB, Sprache bei -40 dBFS: Das Ziel läge 16 dB höher. In der Äußerung (auch über kurze Lücken und einen
        // Pegelsprung um 15 dB) bleibt die Verstärkung stehen, erst nach der Pause bewegt sie sich, um höchstens einen
        // Anfangsschritt.
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        var seed = 1;
        var a = Folge(s, 120, -40f, 1f, ref seed);
        a.AddRange(Folge(s, Pegelstufe.PauseRahmen - 20, -70f, 0f, ref seed)); // Lücke unter der Pausenlänge
        a.AddRange(Folge(s, 80, -25f, 1f, ref seed)); // lauter Schluss
        Pruefe.Gleich(0f, Spanne(a), "Verstärkung in der ersten Äußerung");
        Pruefe.Gleich(0f, a[0], "Startwert");

        var pause = Folge(s, Pegelstufe.PauseRahmen + 10, -70f, 0f, ref seed);
        Pruefe.Gleich(0f, pause[Pegelstufe.PauseRahmen - 2], "vor dem Ende der Pausenlänge noch unverändert");
        var nachPause = pause[^1];
        PruefeWert.Mindestens(0.1, nachPause, "nach der Pause angehoben");
        PruefeWert.Hoechstens(Pegelstufe.AnfangsSchrittDb + 1e-4, nachPause, "höchstens ein Anfangsschritt je Pause");

        var b = Folge(s, 100, -40f, 1f, ref seed);
        b.AddRange(Folge(s, 100, -22f, 1f, ref seed));
        Pruefe.Gleich(0f, Spanne(b), "Verstärkung in der zweiten Äußerung trotz 18 dB Sprung");
        Pruefe.Gleich(nachPause, b[0], "die zweite Äußerung beginnt mit dem Wert nach der Pause");
    }

    [Test]
    public static void Pegelstufe_KurzePausen_KeineAenderung()
    {
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        var seed = 5;
        var g = new List<float>();
        for (var k = 0; k < 20; k++)
        {
            g.AddRange(Folge(s, 150, -40f, 1f, ref seed));
            g.AddRange(Folge(s, Pegelstufe.PauseRahmen - 1, -70f, 0f, ref seed));
        }
        Pruefe.Gleich(0f, Spanne(g), "ohne Pause von voller Länge keine Änderung");
    }

    [Test]
    public static void Pegelstufe_Halteregel_FindetDenGrundpegelLangsam()
    {
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        var seed = 9;
        // 30 Äußerungen zu 2 s bei -40 dBFS: Das Ziel ist Ziel - (-40) dB.
        var ziel = Pegelstufe.ZielDbfs + 40f;
        float vorher = 0f;
        for (var k = 0; k < 30; k++)
        {
            var a = Folge(s, 200, -40f, 1f, ref seed);
            Pruefe.Gleich(0f, Spanne(a), $"Äußerung {k} konstant");
            var p = Folge(s, Pegelstufe.PauseRahmen + 5, -70f, 0f, ref seed);
            var schritt = MathF.Abs(p[^1] - vorher);
            var grenze = k < Pegelstufe.AnfangRahmen / 200 ? Pegelstufe.AnfangsSchrittDb : Pegelstufe.SchrittDb;
            PruefeWert.Hoechstens(grenze + 1e-4, schritt, $"Schritt nach Äußerung {k}");
            vorher = p[^1];
        }
        PruefeWert.Nahe(ziel, vorher, 0.05, "nach 60 s Sprache am Ziel");
        PruefeWert.Nahe(-40, s.GrundpegelDbfs, 0.05, "Grundpegel");

        // Danach spricht dieselbe Person 10 dB lauter: Die Verstärkung sinkt nur in Schritten von höchstens 1 dB je Pause.
        for (var k = 0; k < 6; k++)
        {
            var a = Folge(s, 200, -30f, 1f, ref seed);
            Pruefe.Gleich(vorher, a[0], "hält bis zur Pause");
            Pruefe.Gleich(0f, Spanne(a), "lautere Äußerung konstant");
            var p = Folge(s, Pegelstufe.PauseRahmen + 5, -70f, 0f, ref seed);
            PruefeWert.Hoechstens(Pegelstufe.SchrittDb + 1e-4, vorher - p[^1], "nach der Anfangszeit höchstens 1 dB je Pause");
            PruefeWert.Mindestens(0, vorher - p[^1], "geht nach unten");
            vorher = p[^1];
        }
    }

    /// <summary>Lernt 30 Äußerungen zu 2 s bei <paramref name="sprache"/> mit Pausen bei <paramref name="pause"/>; danach ist die Anfangszeit vorbei.</summary>
    private static Pegelstufe Gelernt(float sprache, float pause, ref int seed)
    {
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        for (var k = 0; k < 30; k++)
        {
            Folge(s, 200, sprache, 1f, ref seed);
            Folge(s, Pegelstufe.PauseRahmen + 5, pause, 0f, ref seed);
        }
        return s;
    }

    /// <summary>Eine Äußerung zu 2 s und eine volle Pause; Rückgabe: Verstärkung danach.</summary>
    private static float Aeusserung(Pegelstufe s, float sprache, float pause, ref int seed)
    {
        Folge(s, 200, sprache, 1f, ref seed);
        return Folge(s, Pegelstufe.PauseRahmen + 5, pause, 0f, ref seed)[^1];
    }

    [Test]
    public static void Pegelstufe_Abweichler_EinRufVerschiebtNichts_DreiInFolgeSchon()
    {
        // Gegenprüfung Klangstufe A: Ein einzelner Ruf (15 dB lauter) verschob den Grundpegel und kostete 1 dB, als erster Satz einer
        // Sitzung sogar 12 dB. Nach der Anfangszeit zählt erst der dritte Abweichler in Folge.
        var seed = 71;
        var s = Gelernt(-40f, -70f, ref seed);
        var vorher = s.VerstaerkungDb;
        Pruefe.Gleich(vorher, Aeusserung(s, -25f, -70f, ref seed), "ein Ruf verschiebt nichts");
        var normal = Aeusserung(s, -40f, -70f, ref seed);
        PruefeWert.Nahe(vorher, normal, 0.05, "danach normal");
        vorher = normal;
        Pruefe.Gleich(vorher, Aeusserung(s, -25f, -70f, ref seed), "erster Abweichler einer Folge");
        Pruefe.Gleich(vorher, Aeusserung(s, -25f, -70f, ref seed), "zweiter");
        var dritter = Aeusserung(s, -25f, -70f, ref seed);
        PruefeWert.Nahe(vorher - Pegelstufe.SchrittDb, dritter, 1e-4, "der dritte in Folge zählt: neue Lage, 1 dB nach unten");
    }

    [Test]
    public static void Pegelstufe_FremdeStimme_NaeherAmRauschen_ZaehltNie()
    {
        // Andere im Raum: 15 dB leiser als die eigene Stimme bei gleichem Rauschen, also 15 dB weniger Abstand. Bis zur Gegenprüfung
        // hob jede Äußerung die Verstärkung um 1 dB (in 220 s um 19 dB).
        var seed = 81;
        var s = Gelernt(-40f, -70f, ref seed);
        var vorher = s.VerstaerkungDb;
        for (var k = 0; k < 20; k++) Pruefe.Gleich(vorher, Aeusserung(s, -55f, -70f, ref seed), $"fremde Stimme {k}");
    }

    [Test]
    public static void Pegelstufe_MikrofonLeiser_GleicherAbstand_ZaehltAbDemDritten()
    {
        // Das Mikrofon wird 15 dB leiser gestellt: Stimme und Rauschen sinken gemeinsam, der Abstand bleibt. Das ist die eigene Stimme
        // in neuer Lage; ab dem dritten Abweichler in Folge zählt sie und die Verstärkung steigt in Schritten von 1 dB.
        var seed = 91;
        var s = Gelernt(-40f, -70f, ref seed);
        var vorher = s.VerstaerkungDb;
        Pruefe.Gleich(vorher, Aeusserung(s, -55f, -85f, ref seed), "erster");
        Pruefe.Gleich(vorher, Aeusserung(s, -55f, -85f, ref seed), "zweiter");
        PruefeWert.Nahe(vorher + Pegelstufe.SchrittDb, Aeusserung(s, -55f, -85f, ref seed), 1e-4, "dritter: 1 dB nach oben");
        PruefeWert.Nahe(vorher + 2 * Pegelstufe.SchrittDb, Aeusserung(s, -55f, -85f, ref seed), 1e-4, "vierter: noch 1 dB");
    }

    [Test]
    public static void Pegelstufe_Gelernt_SetztFort_NeuLernenBeginntVonVorn()
    {
        // Ein gelernter Startwert (frühere Sitzung) gilt als Grundpegel nach der Anfangszeit: Ein erster Satz 15 dB daneben ist ein
        // Abweichler, ein normaler bewegt höchstens 1 dB. Ein anderes Mikrofon (NeuLernen) beginnt die Anfangszeit neu.
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 17f, Gelernt: true));
        var seed = 101;
        Pruefe.Gleich(-40f, s.GrundpegelDbfs, "Grundpegel aus dem gelernten Wert");
        Pruefe.Gleich(17f, Aeusserung(s, -25f, -70f, ref seed), "lauter erster Satz zählt nicht");
        var normal = Aeusserung(s, -38f, -70f, ref seed);
        PruefeWert.Hoechstens(Pegelstufe.SchrittDb + 1e-4, 17f - normal, "normaler Satz: höchstens 1 dB");
        s.NeuLernen();
        PruefeWert.Nahe(normal + Pegelstufe.AnfangsSchrittDb, Aeusserung(s, -60f, -90f, ref seed), 1e-4, "nach NeuLernen ein Anfangsschritt");

        var t = new Pegelstufe(new LautstaerkeEinstellung(true, 17f));
        PruefeWert.Nahe(17f - Pegelstufe.AnfangsSchrittDb, Aeusserung(t, -25f, -70f, ref seed), 1e-4, "bloßer Startwert: Anfangsschritt");
    }

    [Test]
    public static void Pegelstufe_GesendetLernt_SchrittNurOhneSendung()
    {
        // Sprechtaste über zwei Sätze mit Pausen gehalten: kein Schritt, solange gesendet wird; nach dem Loslassen der aufgeschobene
        // Schritt, groß, weil die Sätze in die Anfangszeit fielen. Sprache ohne Sendung lernt nichts.
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        var x = new float[480];
        var seed = 111;
        for (var k = 0; k < 2; k++)
        {
            for (var i = 0; i < 200; i++)
            {
                Rahmen(-40f, seed++).CopyTo(x, 0);
                Pruefe.Gleich(0f, s.Verarbeiten(x, 1f, gesendet: true), $"Satz {k}, Rahmen {i}");
            }
            for (var i = 0; i < 100; i++)
            {
                Rahmen(-70f, seed++).CopyTo(x, 0);
                Pruefe.Gleich(0f, s.Verarbeiten(x, 0f, gesendet: true), $"Pause {k} in der Sendung, Rahmen {i}");
            }
        }
        Rahmen(-70f, seed++).CopyTo(x, 0);
        PruefeWert.Nahe(Pegelstufe.AnfangsSchrittDb, s.Verarbeiten(x, 0f, gesendet: false), 1e-4, "nach dem Loslassen");

        var t = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        for (var k = 0; k < 10; k++)
        {
            for (var i = 0; i < 200; i++)
            {
                Rahmen(-40f, seed++).CopyTo(x, 0);
                t.Verarbeiten(x, 1f, gesendet: false);
            }
            for (var i = 0; i < Pegelstufe.PauseRahmen + 5; i++)
            {
                Rahmen(-70f, seed++).CopyTo(x, 0);
                Pruefe.Gleich(0f, t.Verarbeiten(x, 0f, gesendet: false), "Sprache ohne Sendung lernt nichts");
            }
        }
    }

    [Test]
    public static void Pegelstufe_GespraechImRaum_OhneSendung_HaeltDiePauseNurAn()
    {
        // Nach einer gesendeten Äußerung (Taste los) reden andere im Raum: Sprache ohne Sendung zählt nicht und unterbricht die Pause
        // nicht, die leisen Lücken dazwischen zählen weiter. Sonst käme der Schritt neben einem Gespräch nie.
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        var x = new float[480];
        var seed = 121;
        for (var i = 0; i < 200; i++)
        {
            Rahmen(-40f, seed++).CopyTo(x, 0);
            s.Verarbeiten(x, 1f, gesendet: true);
        }
        // Die Taste bleibt noch 700 ms gedrückt: Die Äußerung ist zu Ende, der Schritt wartet.
        for (var i = 0; i < 70; i++)
        {
            Rahmen(-70f, seed++).CopyTo(x, 0);
            Pruefe.Gleich(0f, s.Verarbeiten(x, 0f, gesendet: true), "Taste noch gedrückt");
        }
        // Erst die eigene Stimme ohne Sendung (ein Satzteil bei geschlossener Sprachaktivierung): unterbricht die Pause wie bisher.
        var g = 0f;
        for (var k = 0; k < 10; k++)
        {
            for (var i = 0; i < 20; i++)
            {
                Rahmen(-40f, seed++).CopyTo(x, 0);
                g = s.Verarbeiten(x, 1f, gesendet: false);
            }
            for (var i = 0; i < 30; i++)
            {
                Rahmen(-70f, seed++).CopyTo(x, 0);
                g = s.Verarbeiten(x, 0f, gesendet: false);
            }
        }
        Pruefe.Gleich(0f, g, "eigene Stimme ohne Sendung: kein Schritt mitten im Satz");
        // Dann andere, 15 dB leiser als die eigene Stimme: halten die Pause nur an.
        for (var k = 0; k < 10; k++)
        {
            for (var i = 0; i < 20; i++)
            {
                Rahmen(-55f, seed++).CopyTo(x, 0);
                g = s.Verarbeiten(x, i % 4 == 0 ? 0.7f : 1f, gesendet: false);
            }
            for (var i = 0; i < 10; i++)
            {
                Rahmen(-70f, seed++).CopyTo(x, 0);
                g = s.Verarbeiten(x, 0f, gesendet: false);
            }
        }
        PruefeWert.Nahe(Pegelstufe.AnfangsSchrittDb, g, 1e-4, "der Schritt kam in einer Lücke");
    }

    [Test]
    public static void Pegelstufe_Anlaut_ZaehltNichtAlsPause()
    {
        // In der Kette kommt die Wahrscheinlichkeit einen Rahmen zu spät (RNNoise rechnet nach der Pegelstufe): Der erste Rahmen
        // eines Anlauts trägt noch die 0 der Pause. Fiele er auf den 50. Pausenrahmen, käme der Schritt im Anlaut (gemessen im
        // Klangkorpus, Äußerung 14). Ein Rahmen, der mehr als 10 dB über dem leisesten der Pause liegt, ist deshalb keine Pause.
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        var seed = 61;
        Folge(s, 200, -40f, 1f, ref seed);
        var vorher = Folge(s, Pegelstufe.PauseRahmen - 1, -70f, 0f, ref seed);
        var anlaut = Folge(s, 1, -50f, 0f, ref seed);
        anlaut.AddRange(Folge(s, 100, -40f, 1f, ref seed));
        Pruefe.Gleich(0f, vorher.Max(), "in der Pause noch nichts");
        Pruefe.Gleich(0f, Spanne(anlaut), "der Anlaut mit der alten Wahrscheinlichkeit ist keine Pause");
        Pruefe.Gleich(0f, anlaut[0], "kein Schritt im Anlaut");
        var pause = Folge(s, Pegelstufe.PauseRahmen + 5, -70f, 0f, ref seed);
        PruefeWert.Mindestens(0.1, pause[^1], "in der nächsten vollen Pause kommt der Schritt");
    }

    [Test]
    public static void Pegelstufe_Grenzen_UndUnsichereRahmen()
    {
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        var seed = 11;
        for (var k = 0; k < 40; k++)
        {
            Folge(s, 200, -95f, 1f, ref seed);
            Folge(s, Pegelstufe.PauseRahmen, -99f, 0f, ref seed);
        }
        PruefeWert.Hoechstens(Pegelstufe.MaxDb, s.VerstaerkungDb, "nie über der Obergrenze");
        var t = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        for (var k = 0; k < 40; k++)
        {
            Folge(t, 200, -3f, 1f, ref seed);
            Folge(t, Pegelstufe.PauseRahmen, -70f, 0f, ref seed);
        }
        PruefeWert.Mindestens(Pegelstufe.MinDb, t.VerstaerkungDb, "nie unter der Untergrenze");

        // Unsichere Rahmen (zwischen Pausen- und Sprachschwelle) sind weder Pause noch Sprache: Sie unterbrechen die Pause.
        var u = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        Folge(u, 200, -40f, 1f, ref seed);
        var g = new List<float>();
        for (var k = 0; k < 10; k++)
        {
            g.AddRange(Folge(u, Pegelstufe.PauseRahmen - 5, -70f, 0f, ref seed));
            g.AddRange(Folge(u, 3, -50f, 0.7f, ref seed));
        }
        Pruefe.Gleich(0f, Spanne(g), "unsichere Rahmen unterbrechen die Pause");
    }

    [Test]
    public static void Pegelstufe_Automatik_StartwertGiltBisZurErstenAeusserung()
    {
        // Der Client gibt nach dem Anlegen den gemerkten Wert der letzten Sitzung als Startwert: Er gilt, solange die Halteregel
        // noch keine Äußerung gelernt hat, danach nicht mehr (kein Sprung mitten in der Sitzung).
        var s = new Pegelstufe();
        var seed = 51;
        Pruefe.Gleich(Pegelstufe.StartDb, Folge(s, 1, -40f, 0f, ref seed)[0], "Voreinstellung");
        s.Setzen(new LautstaerkeEinstellung(true, 18f));
        Pruefe.Gleich(18f, Folge(s, 1, -40f, 0f, ref seed)[0], "gemerkter Startwert gilt");
        Folge(s, 100, -40f, 1f, ref seed);
        var gelernt = Folge(s, Pegelstufe.PauseRahmen + 5, -70f, 0f, ref seed)[^1];
        s.Setzen(new LautstaerkeEinstellung(true, 0f));
        Pruefe.Gleich(gelernt, Folge(s, 1, -40f, 1f, ref seed)[0], "nach der ersten Äußerung kein neuer Startwert");
    }

    [Test]
    public static void Pegelstufe_Moduswechsel()
    {
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 3f));
        var seed = 21;
        Pruefe.Gleich(3f, Folge(s, 1, -40f, 1f, ref seed)[0], "Automatik startet bei der festen Verstärkung");
        s.Setzen(new LautstaerkeEinstellung(false, 12f));
        Pruefe.Gleich(12f, Folge(s, 1, -40f, 1f, ref seed)[0], "Fest gilt ab dem nächsten Rahmen, auch mitten in der Äußerung");
        s.Setzen(new LautstaerkeEinstellung(true, 12f));
        var a = Folge(s, 50, -40f, 1f, ref seed);
        Pruefe.Gleich(12f, a.Max(), "zurück auf Automatik: weiter vom jetzigen Wert");
        Pruefe.Gleich(0f, Spanne(a), "und gehalten bis zur Pause");

        // Der Übergang auf einen neuen festen Wert läuft innerhalb eines Rahmens, ohne Sprung zwischen zwei Samples.
        var x = new float[480];
        Array.Fill(x, 0.01f);
        s.Setzen(new LautstaerkeEinstellung(false, 0f));
        s.Verarbeiten(x, 1f);
        var groessterSchritt = 0f;
        for (var i = 1; i < 480; i++) groessterSchritt = MathF.Max(groessterSchritt, MathF.Abs(x[i] - x[i - 1]));
        PruefeWert.Hoechstens(0.01f * (Pegel.DbZuLinear(12f) - 1f) / 100f, groessterSchritt, "Rampe über den Rahmen");
        PruefeWert.Nahe(0.01, x[^1], 1e-4, "am Ende des Rahmens der neue Wert");
    }

    [Test]
    public static void Pegelstufe_Einmessen_NormalUndLaut()
    {
        // 5 s normal bei -40 dBFS, 2 s laut bei -30 dBFS (Spitzen etwa 12 dB über dem Effektivwert), 1 s Pause. Der normale Pegel
        // bestimmt die Verstärkung, die lauten Spitzen begrenzen sie, damit der Begrenzer nur Netz bleibt.
        var s = new Pegelstufe(new LautstaerkeEinstellung(false, 0f));
        s.EinmessenStarten();
        Pruefe.Gleich(EinmessZustand.Laeuft, s.Einmessen.Zustand, "läuft sofort");
        Pruefe.Gleich(Pegelstufe.EinmessRahmen * 10, s.Einmessen.RestMs, "volle Dauer");
        var seed = 31;
        var g = Folge(s, 500, -40f, 1f, ref seed);
        g.AddRange(Folge(s, 200, -30f, 1f, ref seed));
        Pruefe.Gleich(EinmessZustand.Laeuft, s.Einmessen.Zustand, "noch nicht fertig");
        PruefeWert.Nahe(1000, s.Einmessen.RestMs, 10, "Rest");
        g.AddRange(Folge(s, 100, -70f, 0f, ref seed));
        Pruefe.Gleich(0f, Spanne(g), "die Verstärkung bleibt beim Einmessen stehen");
        var e = s.Einmessen;
        Pruefe.Gleich(EinmessZustand.Fertig, e.Zustand, "fertig nach 8 s");
        // Erwartung: Ziel minus normaler Pegel, höchstens so viel, dass die lauten Spitzen (Rauschen bei -30 dBFS, Spitzen um
        // -18 dBFS) bei -1 dBFS ankommen.
        var ausPegel = Pegelstufe.ZielDbfs + 40f;
        PruefeWert.Hoechstens(ausPegel + 0.5, e.ErgebnisDb, "nicht mehr als der normale Pegel braucht");
        PruefeWert.Hoechstens(Pegelstufe.EinmessSpitzeDbfs - (-30f + 9f), e.ErgebnisDb, "laute Spitzen bleiben unter -1 dBFS");
        PruefeWert.Mindestens(ausPegel - 6, e.ErgebnisDb, "aber nicht übertrieben vorsichtig");
        Pruefe.Gleich(0f, s.VerstaerkungDb, "das Ergebnis gilt erst, wenn es gesetzt wird");

        var t = new Pegelstufe(new LautstaerkeEinstellung(false, 0f));
        t.EinmessenStarten();
        Folge(t, 100, -40f, 1f, ref seed);
        Folge(t, Pegelstufe.EinmessRahmen - 100, -70f, 0f, ref seed);
        Pruefe.Gleich(EinmessZustand.ZuWenigSprache, t.Einmessen.Zustand, "1 s Sprache reicht nicht");

        var u = new Pegelstufe(new LautstaerkeEinstellung(false, 0f));
        u.EinmessenStarten();
        Folge(u, 300, -40f, 1f, ref seed);
        u.EinmessenAbbrechen();
        Folge(u, 1, -40f, 1f, ref seed);
        Pruefe.Gleich(EinmessZustand.Bereit, u.Einmessen.Zustand, "abgebrochen");
    }

    [Test]
    public static void Pegelstufe_NichtEndlich_VergiftetNicht()
    {
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        var seed = 41;
        var x = Rahmen(-40f, seed);
        x[100] = float.NaN;
        x[200] = float.PositiveInfinity;
        for (var r = 0; r < 100; r++) s.Verarbeiten((float[])x.Clone(), r % 2 == 0 ? float.NaN : 1f);
        Folge(s, 200, -40f, 1f, ref seed);
        Folge(s, Pegelstufe.PauseRahmen, -70f, 0f, ref seed);
        Pruefe.Wahr(float.IsFinite(s.VerstaerkungDb), "Verstärkung endlich");
        Pruefe.Wahr(float.IsFinite(s.GrundpegelDbfs), "Grundpegel endlich");
        PruefeWert.Mindestens(0.1, s.VerstaerkungDb, "lernt danach weiter");
        s.Setzen(new LautstaerkeEinstellung(false, float.NaN));
        Pruefe.Wahr(float.IsFinite(Folge(s, 1, -40f, 1f, ref seed)[0]), "NaN als feste Verstärkung wird nicht übernommen");
    }

    [Test]
    public static void Pegelstufe_AllokiertNicht()
    {
        var s = new Pegelstufe(new LautstaerkeEinstellung(true, 0f));
        var laut = Rahmen(-30f, 1);
        var leise = Rahmen(-70f, 2);
        var puffer = new float[480];
        for (var r = 0; r < 500; r++)
        {
            (r % 120 < 60 ? laut : leise).CopyTo(puffer, 0);
            s.Verarbeiten(puffer, r % 120 < 60 ? 1f : 0f);
        }
        var bytes = PruefeWert.Allokiert(() =>
        {
            s.EinmessenStarten();
            for (var r = 0; r < 2000; r++)
            {
                (r % 120 < 60 ? laut : leise).CopyTo(puffer, 0);
                s.Verarbeiten(puffer, r % 120 < 60 ? 1f : 0f);
            }
        });
        Pruefe.Gleich(0L, bytes, "Bytes in 2000 Rahmen mit Pausen, Rampen und Einmessen");
    }
}
