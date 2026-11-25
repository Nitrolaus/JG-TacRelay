// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Verarbeitung;

internal static class GateTests
{
    private static readonly float[] Rauschen65 = RauschRahmen(-65f, 1);

    private static float[] RauschRahmen(float dbfs, int seed)
    {
        var r = new Random(seed);
        var x = new float[480];
        var a = MathF.Pow(10f, dbfs / 20f) * MathF.Sqrt(3f);
        for (var i = 0; i < x.Length; i++) x[i] = a * (float)(r.NextDouble() * 2 - 1);
        return x;
    }

    private static float[] Ton(float dbfs, int rahmenNr)
    {
        var x = new float[480];
        var a = MathF.Pow(10f, dbfs / 20f) * MathF.Sqrt(2f);
        for (var i = 0; i < x.Length; i++) x[i] = a * MathF.Sin(2f * MathF.PI * 150f * (rahmenNr * 480 + i) / 48000f) + Rauschen65[i];
        return x;
    }

    private static List<(SenderahmenArt Art, long Nummer, float[] Daten)> Holen(SprachGate g)
    {
        var liste = new List<(SenderahmenArt, long, float[])>();
        var puffer = new float[480];
        while (g.Holen(puffer, out var art, out var nr)) liste.Add((art, nr, (float[])puffer.Clone()));
        return liste;
    }

    /// <summary>Gate eingeschwungen: 150 Rahmen Grundrauschen ohne Sprache.</summary>
    private static SprachGate Eingeschwungen(GateEinstellungen? e = null)
    {
        var g = new SprachGate(e);
        for (var i = 0; i < 150; i++) Pruefe.Gleich(0, g.Verarbeiten(RauschRahmen(-65f, 100 + i), 0f), "Rauschen öffnet nicht");
        return g;
    }

    [Test]
    public static void Oeffnen_MitVorlaufAusDemRing()
    {
        var g = Eingeschwungen();
        var alle = new List<(SenderahmenArt Art, long Nummer, float[] Daten)>();
        for (var i = 0; i < 4; i++)
        {
            g.Verarbeiten(Ton(-20f, i), 0.95f);
            alle.AddRange(Holen(g));
            if (i < 3) Pruefe.Gleich(GateZustand.Anlauf, g.Zustand, "Anlauf bis zur Bestätigung");
        }
        Pruefe.Gleich(GateZustand.Senden, g.Zustand, "nach 4 Rahmen bestätigt");
        // Vorlauf 30 ms vor dem Anlauf (Rahmen 150) bis zum Bestätigungsrahmen (153): 147..153.
        Pruefe.Gleich(7, alle.Count, "Burst aus 7 Rahmen");
        Pruefe.Gleich(147L, alle[0].Nummer, "Vorlauf beginnt 3 Rahmen vor dem Anlauf");
        Pruefe.Gleich(SenderahmenArt.Beginn | SenderahmenArt.Vorlauf | SenderahmenArt.Sprachaktivierung, alle[0].Art, "erster Rahmen");
        Pruefe.Wahr(alle.Take(6).All(a => (a.Art & SenderahmenArt.Vorlauf) != 0), "Vorlauf markiert");
        Pruefe.Gleich(SenderahmenArt.Sprachaktivierung, alle[6].Art, "aktueller Rahmen ohne Vorlauf");
        Pruefe.Gleich(0f, alle[0].Daten[0], "Beginn eingeblendet");
        Pruefe.Gleich(1L, g.Ausloesungen, "eine Auslösung");
    }

    [Test]
    public static void Nachlauf_DreihundertMs_LetzterAusgeblendet()
    {
        var g = Eingeschwungen();
        for (var i = 0; i < 20; i++)
        {
            g.Verarbeiten(Ton(-20f, i), 0.95f);
            Holen(g);
        }
        var nachher = new List<(SenderahmenArt Art, long Nummer, float[] Daten)>();
        for (var i = 0; i < 50; i++)
        {
            g.Verarbeiten(RauschRahmen(-65f, 500 + i), 0f);
            nachher.AddRange(Holen(g));
        }
        Pruefe.Gleich(30, nachher.Count, "Nachlauf 300 ms");
        Pruefe.Wahr((nachher[^1].Art & SenderahmenArt.Letzter) != 0, "letzter Rahmen markiert");
        Pruefe.Gleich(0f, nachher[^1].Daten[^1], "am Ende auf 0 ausgeblendet");
        Pruefe.Gleich(GateZustand.Leise, g.Zustand, "wieder leise");
    }

    [Test]
    public static void Nachlauf_SpracheKehrtZurueck_OhneNeuenVorlauf()
    {
        var g = Eingeschwungen();
        for (var i = 0; i < 10; i++)
        {
            g.Verarbeiten(Ton(-20f, i), 0.95f);
            Holen(g);
        }
        for (var i = 0; i < 10; i++)
        {
            g.Verarbeiten(RauschRahmen(-65f, 700 + i), 0f);
            Holen(g);
        }
        Pruefe.Gleich(GateZustand.Nachlauf, g.Zustand, "im Nachlauf");
        g.Verarbeiten(Ton(-20f, 30), 0.95f);
        var r = Holen(g);
        Pruefe.Gleich(GateZustand.Senden, g.Zustand, "zurück in SENDEN");
        Pruefe.Gleich(1, r.Count, "nur der aktuelle Rahmen");
        Pruefe.Gleich(SenderahmenArt.Sprachaktivierung, r[0].Art, "ohne Beginn und Vorlauf");
    }

    [Test]
    public static void KurzerStoss_WirdAbgewiesen()
    {
        var g = Eingeschwungen();
        // Zwei laute Rahmen mit hoher Wahrscheinlichkeit (ein Klopfen), dann Rauschen.
        for (var i = 0; i < 2; i++) g.Verarbeiten(Ton(-15f, i), 0.9f);
        var gesendet = 0;
        for (var i = 0; i < 15; i++) gesendet += g.Verarbeiten(RauschRahmen(-65f, 800 + i), 0f);
        Pruefe.Gleich(0, gesendet, "nichts gesendet");
        Pruefe.Gleich(1L, g.AbgewieseneAnlaeufe, "ein abgewiesener Anlauf");
        Pruefe.Gleich(GateZustand.Leise, g.Zustand, "wieder leise");
    }

    [Test]
    public static void LauterOhneSprachwahrscheinlichkeit_OeffnetNicht()
    {
        var g = Eingeschwungen();
        var gesendet = 0;
        for (var i = 0; i < 100; i++) gesendet += g.Verarbeiten(Ton(-10f, i), 0.2f);
        Pruefe.Gleich(0, gesendet, "Pegel allein reicht nicht");
    }

    [Test]
    public static void Sprechtaste_UmgehtGate_OhneVorlauf()
    {
        var g = Eingeschwungen();
        g.Verarbeiten(RauschRahmen(-65f, 1), 0f, sprechtaste: true);
        var r = Holen(g);
        Pruefe.Gleich(1, r.Count, "sofort ein Rahmen");
        Pruefe.Gleich(SenderahmenArt.Beginn, r[0].Art, "Beginn, keine Sprachaktivierung, kein Vorlauf");
        for (var i = 0; i < 5; i++)
        {
            Pruefe.Gleich(1, g.Verarbeiten(RauschRahmen(-65f, 2 + i), 0f, sprechtaste: true), "jeder Rahmen gesendet");
            Pruefe.Gleich(SenderahmenArt.Keine, Holen(g)[0].Art, "ohne Kennzeichen");
        }
        g.Verarbeiten(RauschRahmen(-65f, 9), 0f, sprechtaste: false);
        r = Holen(g);
        Pruefe.Gleich(SenderahmenArt.Letzter, r[0].Art, "Loslassen: letzter Rahmen");
        Pruefe.Gleich(0, g.Verarbeiten(RauschRahmen(-65f, 10), 0f), "danach still");
    }

    [Test]
    public static void Sprechtaste_BeiAbgeschalteterSprachaktivierung_Allein()
    {
        var g = Eingeschwungen();
        g.SprachaktivierungAn = false;
        var gesendet = 0;
        for (var i = 0; i < 20; i++) gesendet += g.Verarbeiten(Ton(-20f, i), 0.95f);
        Pruefe.Gleich(0, gesendet, "ohne Taste nichts");
        Pruefe.Gleich(1, g.Verarbeiten(Ton(-20f, 21), 0.95f, sprechtaste: true), "mit Taste");
    }

    [Test]
    public static void Rauschboden_NichtNachgefuehrtWaehrendSenden()
    {
        var g = Eingeschwungen();
        var vorher = g.RauschbodenDbfs;
        PruefeWert.Nahe(-65, vorher, 1.5, "Boden beim Grundrauschen");
        for (var i = 0; i < 500; i++)
        {
            g.Verarbeiten(Ton(-20f, i), 0.95f);
            Holen(g);
        }
        Pruefe.Gleich(GateZustand.Senden, g.Zustand, "5 s Sprache gesendet");
        PruefeWert.Nahe(vorher, g.RauschbodenDbfs, 0.5, "Boden unverändert nach 5 s Sprache");
    }

    [Test]
    public static void Rauschboden_FolgtLauteremGrundrauschen()
    {
        var g = Eingeschwungen();
        for (var i = 0; i < 300; i++) g.Verarbeiten(RauschRahmen(-50f, 2000 + i), 0f);
        PruefeWert.Nahe(-50, g.RauschbodenDbfs, 1.5, "Boden nach 3 s lauterem Rauschen (Lüfter an)");
    }

    [Test]
    public static void Empfindlichkeit_VerschiebtNurDenOffset()
    {
        var g = Eingeschwungen();
        var abstand = g.AbstandAutoDb;
        var schwelle = g.SchwelleAuto;
        g.Empfindlichkeit = 1f;
        Pruefe.Gleich(abstand, g.AbstandAutoDb, "Selbstjustierung unberührt");
        PruefeWert.Nahe(abstand - 6, g.AbstandWirksamDb, 1e-5, "Abstand 6 dB kleiner");
        PruefeWert.Nahe(schwelle - 0.1, g.SchwelleWirksam, 1e-5, "Schwelle 0,1 kleiner");
        g.Empfindlichkeit = -1f;
        PruefeWert.Nahe(abstand + 6, g.AbstandWirksamDb, 1e-5, "Abstand 6 dB größer");
        g.Empfindlichkeit = 5f;
        Pruefe.Gleich(1f, g.Empfindlichkeit, "Regler begrenzt");
    }

    [Test]
    public static void Selbstjustierung_Schwelle_AusHoerbarerNichtSprache()
    {
        var g = new SprachGate();
        Pruefe.Gleich(0.5f, g.SchwelleAuto, "Startwert Schwelle");
        // Stille Rahmen zählen nicht für die Schwelle, auch wenn es viele sind.
        for (var i = 0; i < 1000; i++) g.Verarbeiten(RauschRahmen(-65f, 3000 + i), 0f);
        Pruefe.Gleich(0.5f, g.SchwelleAuto, "ohne hörbare Nicht-Sprache bleibt der Startwert");
        // Hörbarer Hintergrund mit Wahrscheinlichkeit 0,3 (jeder zehnte Rahmen 10 dB lauter).
        for (var i = 0; i < 1500; i++) g.Verarbeiten(RauschRahmen(i % 10 == 0 ? -55f : -65f, 5000 + i), 0.3f);
        PruefeWert.Nahe(0.3 + 0.15, g.SchwelleAuto, 0.03, "Schwelle = P95 + 0,15");
        // Abgewiesene Anläufe (zwei laute Rahmen mit hoher Wahrscheinlichkeit) heben die Schwelle bis zur Obergrenze.
        for (var k = 0; k < 150; k++)
        {
            for (var i = 0; i < 2; i++) g.Verarbeiten(Ton(-20f, i), 0.95f);
            for (var i = 0; i < 10; i++) g.Verarbeiten(RauschRahmen(-65f, 9000 + i), 0f);
        }
        PruefeWert.Mindestens(100, g.AbgewieseneAnlaeufe, "Anläufe abgewiesen");
        Pruefe.Gleich(0.8f, g.SchwelleAuto, "Schwelle höchstens 0,8");
    }

    [Test]
    public static void Selbstjustierung_OhneDasEinschwingen()
    {
        // Wie RNNoise am Anfang einer Sitzung: Die ersten Rahmen kommen leer, dann lässt es Rauschen durch und hält es für halbe
        // Sprache (in der echten Kette p 0,5 bis 0,68 in den Rahmen 8 bis 24). Das darf die Schwelle der ganzen Sitzung nicht heben.
        var g = new SprachGate();
        for (var i = 0; i < 5; i++) g.Verarbeiten(RauschRahmen(-100f, 100 + i), 0f);
        for (var i = 5; i < 30; i++) g.Verarbeiten(RauschRahmen(-75f, 100 + i), 0.45f);
        Pruefe.Gleich(GateZustand.Leise, g.Zustand, "das Einschwingen öffnet nicht");
        // Danach hörbare Nicht-Sprache ohne Sprachverdacht (jeder zweite Rahmen 15 dB über dem Boden).
        for (var i = 0; i < 400; i++) g.Verarbeiten(RauschRahmen(i % 2 == 0 ? -85f : -100f, 200 + i), 0f);
        Pruefe.Gleich(0.4f, g.SchwelleAuto, "Schwelle an der Untergrenze, das Einschwingen zählt nicht");
        // Nach dem Einschwingen zählt jeder hörbare Rahmen wieder: dieselben 25 Rahmen heben die Schwelle jetzt.
        for (var i = 0; i < 25; i++) g.Verarbeiten(RauschRahmen(-75f, 700 + i), 0.45f);
        PruefeWert.Nahe(0.45 + 0.15, g.SchwelleAuto, 0.03, "später zählt derselbe Verdacht");
    }

    [Test]
    public static void Selbstjustierung_Abstand_AusStreuungNichtAusKlicks()
    {
        var g = new SprachGate();
        Pruefe.Gleich(9f, g.AbstandAutoDb, "Startwert Abstand");
        // Stationäres Rauschen mit jedem fünften Rahmen 25 dB lauter (Tastenklick): Abstand bleibt klein.
        for (var i = 0; i < 1500; i++) g.Verarbeiten(RauschRahmen(i % 5 == 0 ? -40f : -65f, 3000 + i), 0f);
        Pruefe.Gleich(6f, g.AbstandAutoDb, "Klicks treiben den Abstand nicht hoch");
        // Schwankender Hintergrund (Pegel je Rahmen gleichverteilt über 8 dB): Abstand wächst mit der Streuung.
        var zufall = new Random(9);
        for (var i = 0; i < 6000; i++) g.Verarbeiten(RauschRahmen(-69f + 8f * (float)zufall.NextDouble(), 4000 + i % 500), 0f);
        PruefeWert.Mindestens(10, g.AbstandAutoDb, "Abstand aus der Streuung der Pausen");
        PruefeWert.Hoechstens(20, g.AbstandAutoDb, "Abstand begrenzt");
    }
    [Test]
    public static void Stimmhaftigkeit_TonJa_RauschenNein()
    {
        var s = new Stimmhaftigkeit();
        var p = 0f;
        for (var i = 0; i < 5; i++) p = s.Schaetzen(Ton(-30f, i));
        PruefeWert.Mindestens(0.95, p, "harmonischer Ton");
        s.Zuruecksetzen();
        for (var i = 0; i < 5; i++) p = s.Schaetzen(RauschRahmen(-30f, 40 + i));
        Pruefe.Gleich(0f, p, "weißes Rauschen");
        s.Zuruecksetzen();
        Pruefe.Gleich(0f, s.Schaetzen(new float[480]), "digitale Stille");
    }

    [Test]
    public static void Echtzeit_GateUndSchaetzer_AllokierenNicht()
    {
        var g = new SprachGate();
        var s = new Stimmhaftigkeit();
        var (signal, _) = Testszenen.Sprache(20, 3);
        var puffer = new float[480];
        var pos = 0;
        void Schritt()
        {
            var rahmen = signal.AsSpan(pos, 480);
            var p = s.Schaetzen(rahmen);
            g.Verarbeiten(rahmen, p, 0f, pos / 480 % 700 > 650);
            while (g.Holen(puffer, out _, out _))
            {
            }
            pos = (pos + 480) % (signal.Length - 480);
        }
        for (var i = 0; i < 200; i++) Schritt();
        var bytes = PruefeWert.Allokiert(() =>
        {
            for (var i = 0; i < 1800; i++) Schritt();
        });
        Pruefe.Gleich(0L, bytes, "allokierte Bytes in Schaetzen/Verarbeiten/Holen");
    }
}
