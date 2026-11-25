// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Schutz;
using Funk.Protokoll.Sprache;

namespace Funk.Tests;

/// <summary>Prüfwert 8.7 (Replay-Fenster, W = 2048) und P-05.</summary>
internal static class SpracheReplayTests
{
    /// <summary>Schritte 1 bis 15 aus 8.7, alle mit gültigem Tag.</summary>
    private static readonly (int Schritt, ulong Zaehler, bool Annehmen)[] Tabelle87 =
    [
        (1, 0, true),
        (2, 1, true),
        (3, 1, false),
        (4, 5, true),
        (5, 3, true),
        (6, 3, false),
        (7, 2053, true),
        (8, 5, false),
        (9, 6, true),
        (10, 4, false),
        (11, 100000, true),
        (12, 99999, true),
        (13, 97952, false),
        (14, 97953, true),
        (15, 100000, false),
    ];

    [Test]
    public static void Replay_8_7_Fenster()
    {
        var f = new ReplayFenster(Sprachkanal.Fensterbreite);
        Pruefe.Gleich(2048UL, f.Breite, "W");
        foreach (var (schritt, zaehler, annehmen) in Tabelle87)
        {
            Pruefe.Gleich(annehmen, f.IstNeu(zaehler), $"Schritt {schritt}, Zähler {zaehler}, Vorprüfung");
            Pruefe.Gleich(annehmen, f.Annehmen(zaehler), $"Schritt {schritt}, Zähler {zaehler}, Annehmen");
        }

        // 16: 100001 mit falschem Tag. Die Vorprüfung sagt neu, der Tag scheitert, Annehmen wird nie gerufen.
        Pruefe.Wahr(f.IstNeu(100001), "Schritt 16, Vorprüfung");
        Pruefe.Gleich(100000UL, f.Hoechster, "Schritt 16, Fenster unverändert");
        // 17: dasselbe mit richtigem Tag.
        Pruefe.Wahr(f.Annehmen(100001), "Schritt 17");
        Pruefe.Gleich(100001UL, f.Hoechster, "Schritt 17, neuer Höchstwert");
    }

    [Test]
    public static void Replay_8_7_DurchDieEmpfangspruefung()
    {
        using var sender = SprachTestwerte.Hoch0();
        using var kanal = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        var u = new TestUmgebung(kanal);
        var klartext = new byte[16];
        new Keepalive(1, 0x1234).Schreiben(klartext);
        var d = new byte[Sprachkanal.FestesDatagramm];
        var k = new byte[Sprachkanal.MaxKlartext];

        Empfangsergebnis Vorlegen(ulong zaehler, bool tagKippen = false)
        {
            Datagramm.Versiegeln(sender, new SprachKopf(PaketTyp.Keepalive, 0, SprachTestwerte.SitzungA, zaehler), klartext, d);
            if (tagKippen) d[^1] ^= 0x80;
            return SprachTestwerte.Server(u, d, k);
        }

        foreach (var (schritt, zaehler, annehmen) in Tabelle87)
        {
            var e = Vorlegen(zaehler);
            Pruefe.Gleich(annehmen ? Empfangsergebnis.Angenommen : Empfangsergebnis.Replay, e, $"Schritt {schritt}, Zähler {zaehler}");
        }

        var versuche = kanal.Entschluesselungen;
        Pruefe.Gleich(Empfangsergebnis.TagUngueltig, Vorlegen(100001, tagKippen: true), "Schritt 16, falscher Tag");
        Pruefe.Gleich(versuche + 1, kanal.Entschluesselungen, "Schritt 16 wurde entschlüsselt");
        Pruefe.Gleich(100000UL, kanal.Hoechster, "Schritt 16, Fenster bleibt unverändert");
        Pruefe.Wahr(kanal.IstNeu(100001), "Schritt 16, 100001 weiter ungesehen");

        Pruefe.Gleich(Empfangsergebnis.Angenommen, Vorlegen(100001), "Schritt 17, richtiger Tag");
        Pruefe.Gleich(100001UL, kanal.Hoechster, "Schritt 17, Aktualisierung erst nach der Tag-Prüfung");
    }

    [Test]
    public static void Replay_P05_Schritt8Entscheidet()
    {
        // Beide Kopien bestehen die Vorprüfung (Schritt 6), bevor eine gesetzt wird; Schritt 8 lässt genau eine durch.
        var f = new ReplayFenster();
        f.Annehmen(100000);
        f.Annehmen(100001);
        Pruefe.Wahr(f.IstNeu(100002), "Kopie 1, Vorprüfung");
        Pruefe.Wahr(f.IstNeu(100002), "Kopie 2, Vorprüfung");
        Pruefe.Wahr(f.Annehmen(100002), "Kopie 1, Schritt 8");
        Pruefe.Falsch(f.Annehmen(100002), "Kopie 2, Schritt 8");
    }

    [Test]
    public static void Replay_P05_ZweiThreadsGleicheKopie()
    {
        const int Runden = 2000;
        var fenster = new ReplayFenster[Runden];
        for (var r = 0; r < Runden; r++)
        {
            fenster[r] = new ReplayFenster();
            fenster[r].Annehmen(100000);
            fenster[r].Annehmen(100001);
        }
        var gewonnen = new int[Runden];
        var beideVorgeprueft = 0;
        using var start = new Barrier(2);

        void Lauf()
        {
            for (var r = 0; r < Runden; r++)
            {
                start.SignalAndWait();
                if (!fenster[r].IstNeu(100002)) continue;
                if (fenster[r].Annehmen(100002)) Interlocked.Increment(ref gewonnen[r]);
                else Interlocked.Increment(ref beideVorgeprueft);
            }
        }

        var t1 = new Thread(Lauf);
        var t2 = new Thread(Lauf);
        t1.Start();
        t2.Start();
        t1.Join();
        t2.Join();

        for (var r = 0; r < Runden; r++)
            Pruefe.Gleich(1, gewonnen[r], $"Runde {r}: genau eine Kopie angenommen");
        Console.WriteLine($"  P-05: in {beideVorgeprueft} von {Runden} Runden bestanden beide Kopien die Vorprüfung, Schritt 8 entschied.");
    }
}
