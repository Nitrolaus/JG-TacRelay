// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;

namespace Funk.Tests;

/// <summary>Grenzen aus 4.8, soweit sie Steuerrahmen und Bodies betreffen, mit simulierter Uhr.</summary>
internal static class SteuerBremseTests
{
    private static RahmenKopf Kopf(Nachrichtentyp typ, int body = 20, Nachrichtenart art = Nachrichtenart.Anfrage) =>
        new((uint)(body + 8), 1, art == Nachrichtenart.Antwort ? Rahmen.FlagAntwort : (byte)0, (ushort)typ, art == Nachrichtenart.Push ? 0u : 1u);

    [Test]
    public static void Bremse_RahmenGesamt30JeSekundeStoss60()
    {
        var b = new Steuerbremse(0);
        for (var i = 0; i < 60; i++) Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(Kopf(Nachrichtentyp.Bye, art: Nachrichtenart.Push), 0).Urteil, $"Stoß {i}");
        var e = b.Pruefen(Kopf(Nachrichtentyp.Bye, art: Nachrichtenart.Push), 0);
        Pruefe.Gleich(Bremsurteil.Grenze, e.Urteil, "61. Rahmen");
        Pruefe.Wahr(e.GrenzeMelden, "erste GRENZE-Meldung");
        Pruefe.Falsch(b.Pruefen(Kopf(Nachrichtentyp.Bye, art: Nachrichtenart.Push), 500).GrenzeMelden, "höchstens 1 Meldung/s");
        for (var i = 0; i < 14; i++) Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(Kopf(Nachrichtentyp.Bye, art: Nachrichtenart.Push), 1000).Urteil, $"nach 1 s: {i}");
    }

    [Test]
    public static void Bremse_Bytes256KiBJeSekundeStoss1MiB_OhneSpracheTunnel()
    {
        var b = new Steuerbremse(0);
        Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(Kopf(Nachrichtentyp.Ablage, body: 1_048_576 - 12 - 100, art: Nachrichtenart.Push), 0).Urteil, "fast 1 MiB");
        Pruefe.Gleich(Bremsurteil.Grenze, b.Pruefen(Kopf(Nachrichtentyp.Bye, body: 200, art: Nachrichtenart.Push), 0).Urteil, "Topf leer");
        for (var i = 0; i < 1000; i++)
        {
            Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(Kopf(Nachrichtentyp.SpracheTunnel, body: 500, art: Nachrichtenart.Push), 0).Urteil, "Tunnel zählt nicht");
        }
        Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(Kopf(Nachrichtentyp.Bye, body: 200, art: Nachrichtenart.Push), 1000).Urteil, "nach 1 s wieder Platz");
    }

    [Test]
    public static void Bremse_GruppenAus4_8()
    {
        Pruefe.Gleich(new Rate(4, 8), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.HoerenSetzen)), "Zustandsmeldung");
        Pruefe.Gleich(new Rate(5, 10), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.SatzAnwenden)), "FeldSetzen, SatzAnwenden");
        Pruefe.Gleich(new Rate(10, 20), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.Verschieben)), "Umstellungen");
        Pruefe.Gleich(new Rate(2, 5), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.AufstellungAnwenden)), "AufstellungAnwenden");
        Pruefe.Gleich(new Rate(1, 5), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.GeraetEntfernen)), "Geräte-Nachrichten");
        Pruefe.Gleich(new Rate(1, 5), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.Ablage)), "Ablage");
        Pruefe.Gleich(new Rate(0.1, 1), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.RaidHelperEvents)), "Raid-Helper 1 je 10 s");
        Pruefe.Gleich(new Rate(0.2, 2), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.Zustand)), "Zustand 1 je 5 s, Stoß 2");
        Pruefe.Gleich(new Rate(0.5, 1), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.EmpfangsBericht)), "EmpfangsBericht 1 je 2 s");
        Pruefe.Gleich(new Rate(0.1, 1), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.KeyRotation)), "KeyRotation 1 je 10 s");
        Pruefe.Gleich(new Rate(1, 3), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.AdminNonce)), "AdminNonce 1/s, Stoß 3");
        Pruefe.Gleich(new Rate(1, 3), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.AdminAnmeldung)), "Admin 1/s, Stoß 3");
        Pruefe.Gleich(new Rate(1, 2), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.Funkermodus)), "Funkermodus 1/s, Stoß 2");
        Pruefe.Gleich(new Rate(0.5, 2), Ratengrenzen.Von(Ratengrenzen.Gruppe(Nachrichtentyp.PhaseSchalten)), "PhaseSchalten 1 je 2 s, Stoß 2");

        // Eine Flut von Phasenwechseln endet nach dem Stoß bei GRENZE, ohne Trennung; nach 2 s geht wieder einer. Der Funker-Modus
        // hat einen eigenen Eimer.
        var p = new Steuerbremse(0);
        for (var i = 0; i < 2; i++) Pruefe.Gleich(Bremsurteil.Annehmen, p.Pruefen(Kopf(Nachrichtentyp.PhaseSchalten), 0).Urteil, "Phase " + (i + 1));
        for (var i = 0; i < 10; i++) Pruefe.Gleich(Bremsurteil.Grenze, p.Pruefen(Kopf(Nachrichtentyp.PhaseSchalten), 100 * i).Urteil, "Flut " + i);
        Pruefe.Gleich(Bremsurteil.Annehmen, p.Pruefen(Kopf(Nachrichtentyp.Funkermodus), 1000).Urteil, "Funkermodus aus eigenem Eimer");
        Pruefe.Gleich(Bremsurteil.Annehmen, p.Pruefen(Kopf(Nachrichtentyp.PhaseSchalten), 2100).Urteil, "nach gut 2 s wieder eine Phase");
        Pruefe.Gleich(Bremsurteil.Grenze, p.Pruefen(Kopf(Nachrichtentyp.PhaseSchalten), 2100).Urteil, "aber nur eine");

        // AdminNonce hat einen eigenen Eimer (Offener Punkt 30): Drei Nonces lassen der Anmeldung ihren Stoß.
        var n = new Steuerbremse(0);
        for (var i = 0; i < 3; i++) Pruefe.Gleich(Bremsurteil.Annehmen, n.Pruefen(Kopf(Nachrichtentyp.AdminNonce), 0).Urteil, "Nonce " + (i + 1));
        Pruefe.Gleich(Bremsurteil.Grenze, n.Pruefen(Kopf(Nachrichtentyp.AdminNonce), 0).Urteil, "vierte Nonce im selben Augenblick");
        for (var i = 0; i < 3; i++) Pruefe.Gleich(Bremsurteil.Annehmen, n.Pruefen(Kopf(Nachrichtentyp.AdminAnmeldung), 0).Urteil, "Anmeldung " + (i + 1) + " trotz leerem Nonce-Eimer");

        // RaidHelperImport und RaidHelperEvents teilen sich einen Eimer.
        var b = new Steuerbremse(0);
        Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(Kopf(Nachrichtentyp.RaidHelperImport), 0).Urteil, "Import");
        Pruefe.Gleich(Bremsurteil.Grenze, b.Pruefen(Kopf(Nachrichtentyp.RaidHelperEvents), 5000).Urteil, "Events 5 s danach");
        Pruefe.Gleich(Bremsurteil.Annehmen, b.Pruefen(Kopf(Nachrichtentyp.RaidHelperEvents), 10_000).Urteil, "Events 10 s danach");

        // Antworten des Clients (etwa auf VoiceKeys) haben keine eigene Gruppe.
        var c = new Steuerbremse(0);
        for (var i = 0; i < 5; i++) Pruefe.Gleich(Bremsurteil.Annehmen, c.Pruefen(Kopf(Nachrichtentyp.KeyRotation, art: Nachrichtenart.Antwort), 0).Urteil, "Antwort");
        Pruefe.Gleich(Bremsurteil.Annehmen, c.Pruefen(Kopf(Nachrichtentyp.KeyRotation), 0).Urteil, "KeyRotation 1");
        Pruefe.Gleich(Bremsurteil.Grenze, c.Pruefen(Kopf(Nachrichtentyp.KeyRotation), 9_999).Urteil, "KeyRotation vor 10 s");
    }

    [Test]
    public static void Bremse_MehrAls50VerstoesseIn60s_Trennen()
    {
        var b = new Steuerbremse(0);
        b.Pruefen(Kopf(Nachrichtentyp.Ablage), 0);
        var t = 0L;
        for (var i = 0; i < 50; i++)
        {
            // 5 je Stoß, dann Verstoß; Ablage füllt 1/s nach.
            Pruefe.Gleich(Bremsurteil.Grenze, VerstossErzwingen(b, ref t), $"Verstoß {i + 1}");
        }
        Pruefe.Gleich(Bremsurteil.Trennen, VerstossErzwingen(b, ref t), "51. Verstoß");
        Pruefe.Wahr(t < Ratengrenzen.VerstossFensterMs, "alles binnen 60 s");
    }

    private static Bremsurteil VerstossErzwingen(Steuerbremse b, ref long t)
    {
        while (true)
        {
            var u = b.Pruefen(Kopf(Nachrichtentyp.Ablage), t).Urteil;
            if (u != Bremsurteil.Annehmen) return u;
            t += 10;
        }
    }

    [Test]
    public static void F9_Zustandsmeldungen_ZusammengefasstOhneVerstoss()
    {
        // 10 s lang 30 SendeauswahlSetzen je Sekunde: überzählige UEBERHOLT, kein Verstoß, am Ende gilt der letzte Stand.
        var b = new Steuerbremse(0);
        var zf = new Zusammenfasser<int>(b);
        var angewendet = new List<int>();
        var ueberholt = 0;
        const ushort Typ = (ushort)Nachrichtentyp.SendeauswahlSetzen;
        uint nr = 0;
        for (long t = 0; t < 10_000; t += 33)
        {
            nr++;
            var e = b.Pruefen(Kopf(Nachrichtentyp.SendeauswahlSetzen), t);
            Pruefe.Wahr(e.Urteil is Bremsurteil.Annehmen or Bremsurteil.Zusammenfassen, $"kein Verstoß bei {t}: {e.Urteil}");
            if (e.Urteil == Bremsurteil.Annehmen) angewendet.Add((int)nr);
            else if (zf.Einreihen(Typ, nr, (int)nr) is not null) ueberholt++;
            if (zf.VersucheEntnehmen(Typ, t, out _, out var w)) angewendet.Add(w);
        }
        // Nachlauf: die wartende Meldung wird angewendet, sobald der Eimer es erlaubt.
        for (long t = 10_000; zf.Wartend > 0; t += 50)
        {
            if (zf.VersucheEntnehmen(Typ, t, out _, out var w)) angewendet.Add(w);
        }
        Pruefe.Gleich((int)nr, angewendet[^1], "letzter Stand gilt");
        Pruefe.Wahr(ueberholt > 200, $"UEBERHOLT: {ueberholt}");
        Pruefe.Wahr(angewendet.SequenceEqual(angewendet.Order()), "Reihenfolge bleibt erhalten");
        Pruefe.Gleich((int)nr, angewendet.Count + ueberholt, "jede Meldung angewendet oder überholt");
    }
}
