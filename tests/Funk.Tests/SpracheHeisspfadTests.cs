// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Sprache;

namespace Funk.Tests;

/// <summary>Der Paketpfad allokiert nichts (5.14: "Der Pfad vor dem Entschlüsseln allokiert nichts"), gemessen statt behauptet.</summary>
internal static class SpracheHeisspfadTests
{
    [Test]
    public static void Heisspfad_SendenPruefenStempeln_OhneAllokation()
    {
        const int Anzahl = 1000;
        using var client = new SendeKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0(), 0);
        using var server = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        using var runter = new SendeKanal(SprachTestwerte.SitzungB, Richtung.Runter, SprachTestwerte.Runter0(), 0);
        var u = new TestUmgebung(server);
        var daten = new Empfangsdaten(Richtung.Hoch, Weg.Udp, 0);
        var klartext = new byte[64];
        var datagramm = new byte[Sprachkanal.EmpfangspufferLaenge];
        var empfangen = new byte[Sprachkanal.EmpfangspufferLaenge];
        var gestempelt = new byte[64];
        var ausgang = new byte[Sprachkanal.EmpfangspufferLaenge];
        ushort[] netze = [7];
        var n = SpracheHoch.Schreiben(klartext, 100, 3, Kennzeichen.Beginn, Zielart.Netz, [2, 7], SprachTestwerte.OpusAttrappe);

        void Runde()
        {
            client.Versiegeln(PaketTyp.SpracheHoch, klartext.AsSpan(0, n), datagramm, 0, out var laenge);
            var e = Empfangspruefung.Pruefen(u, datagramm.AsSpan(0, laenge), daten, 0, empfangen, out _, out _);
            if (e != Empfangsergebnis.Angenommen) throw new PruefungException("nicht angenommen: " + e);
            SpracheHoch.Lesen(empfangen.AsSpan(0, laenge - Sprachkanal.Ueberhang), out var hoch);
            var m = SpracheRunter.Stempeln(gestempelt, 1001, hoch, beginn: false, Spruchart.Netz, netze);
            runter.Versiegeln(PaketTyp.SpracheRunter, gestempelt.AsSpan(0, m), ausgang, 0, out _);
        }

        for (var i = 0; i < 50; i++) Runde(); // Aufwärmen: JIT, erste Aufrufe in die Kryptobibliothek
        var vorher = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Anzahl; i++) Runde();
        var allokiert = GC.GetAllocatedBytesForCurrentThread() - vorher;
        Pruefe.Gleich(0L, allokiert, $"allokierte Bytes in {Anzahl} Runden senden, prüfen, stempeln, versiegeln");
    }

    [Test]
    public static void Heisspfad_VerwurfVorDemEntschluesseln_OhneAllokation()
    {
        using var server = new EmpfangsKanal(SprachTestwerte.SitzungA, Richtung.Hoch, SprachTestwerte.Hoch0());
        var u = new TestUmgebung(server);
        var daten = new Empfangsdaten(Richtung.Hoch, Weg.Udp, 0);
        var klartext = new byte[Sprachkanal.MaxKlartext];
        var falscheRichtung = SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[1] = 0x11);
        var fremdeEpoche = SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[2] = 0x07);
        var fremdeSitzung = SprachTestwerte.Kopie(SprachTestwerte.TvA, x => x[4] = 0x00);
        Empfangspruefung.Pruefen(u, SprachTestwerte.TvA, daten, 0, klartext, out _, out _);

        void Runde()
        {
            Empfangspruefung.Pruefen(u, falscheRichtung, daten, 0, klartext, out _, out _);
            Empfangspruefung.Pruefen(u, fremdeEpoche, daten, 0, klartext, out _, out _);
            Empfangspruefung.Pruefen(u, fremdeSitzung, daten, 0, klartext, out _, out _);
            Empfangspruefung.Pruefen(u, SprachTestwerte.TvA, daten, 0, klartext, out _, out _); // Replay
        }

        for (var i = 0; i < 50; i++) Runde();
        var vorher = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Runde();
        Pruefe.Gleich(0L, GC.GetAllocatedBytesForCurrentThread() - vorher, "allokierte Bytes beim Verwerfen vor Schritt 7");
        Pruefe.Gleich(1L, server.Entschluesselungen, "nur das erste TV-A wurde entschlüsselt");
    }
}
