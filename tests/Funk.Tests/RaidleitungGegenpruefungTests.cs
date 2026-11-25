// JG TacRelay - Maintainer: Sarevoc
using System.Net;
using System.Text.Json.Nodes;
using Funk.Client.Dienste;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;
using Funk.Tests.Oberflaeche;
using L = Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// Gegenprüfung der Raidleitung (Fassung 0.3.0, 01.10.2026), je Befund erst rot, dann grün:
/// <list type="bullet">
/// <item>Die Wechselsperre ließ sich in zwei Schritten umgehen: <c>SatzAnwenden</c> mit <c>rolleId 0</c> gibt den Platz auf, danach
/// steigt man als Nachzügler ohne Platz woanders ein.</item>
/// <item>Ein Platz, dessen Frist abläuft (Wiederverbindung 10 min, Reservierung 12 h), wurde ohne Aktion nie als Praesenz gemeldet:
/// Das Brett zeigte die Person weiter im Block und lehnte Ablagen dort mit "voll" ab, obwohl der Server den Platz frei sah.</item>
/// <item>Ein Client 0.3.0 an einem Server 0.2.0 schickte <c>KontenVerzeichnis</c> (0x0038) und <c>Beauftragung</c> (0x0017); ein
/// Server 0.2.0 kennt beide nicht und trennt bei einem unbekannten Pflichttyp mit PROTOKOLL.</item>
/// <item>Ein Nachzügler, der länger als 15 Minuten ohne Platz blieb, ließ das Brett danach jede Sekunde ganz neu rechnen.</item>
/// </list>
/// </summary>
internal static class RaidleitungGegenpruefungTests
{
    private const uint Admin = Funkprobe.Admin;
    private const uint D = Funkprobe.D;
    private const uint E = Funkprobe.E;
    private const ushort Co = Funkprobe.RolleCo;
    private const ushort Pilot = Funkprobe.RollePilot;
    private const ushort Schiff = Funkprobe.Schiff3;

    // ---------------------------------------------------------------- Wechselsperre (F9)

    [Test]
    public static void Wechselsperre_PlatzAufgebenUndWoandersEinsteigen_GehtNicht()
    {
        var f = new Funkprobe(b => b.MitRolle(new L.Rolle(63, "Frei") { SelbstWaehlbar = true }));
        Pruefe.Wahr(f.Tun(L.Auftrag.EinsteigenRolleSchiff(D, null, Pilot, Schiff)).Erfolg, "D hat einen Platz");
        Pruefe.Wahr(f.Tun(L.Auftrag.SelbstwahlSetzen(Admin, false, f.Z.NetzListeVersion)).Erfolg, "Wechsel sperren (Nachzügler frei)");

        var aufgeben = f.Tun(L.Auftrag.SatzAnwenden(D, 0, null, []));
        Pruefe.Gleich(L.Fehlercode.SelbstwahlGesperrt, aufgeben.Fehler, "den Platz selbst aufgeben ist ein Wechsel");
        Pruefe.Wahr(f.Z.Konten[D].Platz is { RolleId: Pilot }, "D bleibt Pilot");
        Pruefe.Gleich(L.Fehlercode.SelbstwahlGesperrt, f.Tun(L.Auftrag.EinsteigenRolleSchiff(D, null, 63, null)).Fehler, "und steigt nicht woanders ein");

        // Ohne Platz gibt es nichts aufzugeben: SatzAnwenden mit rolleId 0 bleibt erlaubt, und der Nachzügler steigt ein.
        Pruefe.Wahr(f.Tun(L.Auftrag.SatzAnwenden(E, 0, null, [])).Erfolg, "ohne Platz: rolleId 0 ändert nichts am Platz");
        Pruefe.Wahr(f.Tun(L.Auftrag.EinsteigenRolleSchiff(E, null, 63, null)).Erfolg, "Nachzügler ohne Platz steigt ein");

        // Ganz sperren: dasselbe.
        Pruefe.Wahr(f.Tun(L.Auftrag.SelbstwahlSetzen(Admin, false, f.Z.NetzListeVersion, nachzuegler: false)).Erfolg, "Ganz sperren");
        Pruefe.Gleich(L.Fehlercode.SelbstwahlGesperrt, f.Tun(L.Auftrag.SatzAnwenden(E, 0, null, [])).Fehler, "auch bei Ganz sperren");

        // Wer ZUWEISEN hat, ist nie gesperrt; ohne Sperre geht Aufgeben wie bisher.
        Pruefe.Wahr(f.Tun(L.Auftrag.EinsteigenRolleSchiff(Admin, null, 63, null)).Erfolg && f.Tun(L.Auftrag.SatzAnwenden(Admin, 0, null, [])).Erfolg,
            "der Admin gibt seinen Platz auf");
        Pruefe.Wahr(f.Tun(L.Auftrag.SelbstwahlSetzen(Admin, true, f.Z.NetzListeVersion)).Erfolg, "Sperre aufgehoben");
        Pruefe.Wahr(f.Tun(L.Auftrag.SatzAnwenden(D, 0, null, [])).Erfolg && f.Z.Konten[D].Platz is null, "ohne Sperre gibt D den Platz auf");
    }

    // ---------------------------------------------------------------- Platzfristen ohne Aktion

    [Test]
    public static void Platzfristen_WechselNurDurchDieZeit_WerdenErkannt()
    {
        const uint Offline = 1300;
        var f = new Funkprobe(b => b.MitKonto(new L.Konto { Id = Offline, Name = "Offline" }));
        Pruefe.Wahr(f.Tun(L.Auftrag.EinsteigenRolleSchiff(D, null, Pilot, Schiff)).Erfolg, "D Pilot, selbst gewählt (ohne Reservierung)");
        Pruefe.Wahr(f.Tun(L.Auftrag.EinsteigenRolleSchiff(Admin, Offline, Co, Schiff)).Erfolg, "Offline als CO gesetzt: RESERVIERT");
        var t0 = f.Jetzt;
        Pruefe.Wahr(f.Tun(L.Auftrag.SitzungBeenden(Funkprobe.S(D))).Erfolg, "D trennt");
        var z = f.Z;
        Pruefe.Gleich(L.PlatzZustand.Belegt, z.PlatzZustandVon(D, t0 + 1), "D in der Wiederverbindungsfrist belegt");

        Pruefe.Gleich(0, z.PlatzzustandGewechselt(t0, t0 + L.Fristen.WiederverbindungMs - 1).Count, "innerhalb der Fristen wechselt nichts");
        Pruefe.Wahr(z.PlatzzustandGewechselt(t0 + L.Fristen.WiederverbindungMs - 1, t0 + L.Fristen.WiederverbindungMs).SequenceEqual([D]),
            "nach 10 min ist D frei, ohne dass jemand etwas tat");
        var spaet = z.PlatzzustandGewechselt(t0 + L.Fristen.ReservierungMs - 1, t0 + L.Fristen.ReservierungMs);
        Pruefe.Wahr(spaet.SequenceEqual([Offline]), "nach 12 h endet die Reservierung: " + string.Join(",", spaet));
        Pruefe.Wahr(z.PlatzzustandGewechselt(t0, t0 + 2 * L.Fristen.ReservierungMs).Order().SequenceEqual([D, Offline]), "über beide Fristen hinweg beide");
        Pruefe.Falsch(z.PlatzzustandGewechselt(t0, t0 + 2 * L.Fristen.ReservierungMs).Contains(Funkprobe.A), "wer online ist, wechselt nicht durch die Zeit");
    }

    private static IPAddress Lokal(int n) => IPAddress.Parse("127.0.1." + n.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static async Task<bool> Warten(Func<bool> bedingung, int ms)
    {
        var bis = DateTime.UtcNow.AddMilliseconds(ms);
        while (!bedingung())
        {
            if (DateTime.UtcNow > bis) return false;
            await Task.Delay(50);
        }
        return true;
    }

    /// <summary>
    /// Über den Draht: Fritz sitzt auf Schiff 1 und trennt. Die Wiederverbindungsfrist läuft (hier vorgestellt auf 3 s vor ihrem
    /// Ende) ab, ohne dass jemand etwas tut. Das Brett (Sarevoc, ADMIN mit Details) muss es erfahren, sonst steht Fritz dort weiter.
    /// </summary>
    [Test]
    public static async Task Draht_PlatzfristLaeuftAb_BrettErfaehrtEsOhneAktion()
    {
        await using var t = await Testserver.StartenAsync();
        var halter = new ServerHalter { Server = t };
        await Testflotte.AnDieKonsoleAsync(t);
        await using var test = await KernFunker.AktivAsync(halter, "930000000000000011", "test", "ADMIN", Lokal(61));
        var fritz = await KernFunker.AktivAsync(halter, "930000000000000013", "fritz", "FUNKER", Lokal(63));
        var fritzId = fritz.KontoId;
        var nl = fritz.Client.Lage.NetzListe!;
        var co = nl.Rollen.Single(r => r.Name == "Kapitän");
        var schiff1 = nl.Schiffe.Single(s => s.Name == "Schiff 1");
        var ein = await fritz.Client.AnfrageAsync(Nachrichtentyp.EinsteigenRolleSchiff, new EinsteigenRolleSchiffAnfrage { RolleId = co.Id, SchiffId = schiff1.Id });
        Pruefe.Wahr(ein.Ok, "Fritz CO auf Schiff 1: " + ein.Fehler?.Code);
        await fritz.DisposeAsync();
        Pruefe.Wahr(await Warten(() => test.Client.Lage.Praesenz.TryGetValue(fritzId, out var e) && e is { Online: false, RolleId: not null, Platz: PlatzStatus.Belegt }, 10_000),
            "nach dem Trennen steht Fritz offline auf seinem Platz (Wiederverbindungsfrist)");

        // Die Frist auf 3 s vor ihr Ende stellen, ohne Folgen zu verteilen: so, als wäre Fritz vor knapp 10 Minuten gegangen.
        var z = t.Dienst.Zentrale;
        var gestellt = await z.Ausfuehren(() => z.Schreiber.Ausfuehren(new L.Auftrag("Test: Sitzungsende zurück", true, w =>
        {
            var k = w.Konto(fritzId)!;
            w.KontoSetzen(k with { SitzungsEnde = w.Jetzt - L.Fristen.WiederverbindungMs + 3000 }, relevant: false);
            return new L.Ergebnis();
        }), Zeit.LageMs()));
        Pruefe.Wahr(gestellt.Erfolg, "Frist gestellt");
        Pruefe.Wahr(test.Client.Lage.Praesenz.ContainsKey(fritzId), "noch steht er auf dem Brett");

        Pruefe.Wahr(await Warten(() => !test.Client.Lage.Praesenz.ContainsKey(fritzId), 30_000),
            "nach dem Ablauf der Frist meldet der Server Fritz ab (Praesenz), ohne dass jemand etwas tat");
        Pruefe.Wahr(z.Schreiber.Aktuell.PlatzZustandVon(fritzId, Zeit.LageMs()) == L.PlatzZustand.Frei, "der Server sieht den Platz frei");
    }

    // ---------------------------------------------------------------- Client 0.3.0 an einem Server 0.2.0

    /// <summary>
    /// Ein Server 0.2.0 kennt 0x0017 und 0x0038 nicht und trennt bei einem unbekannten Pflichttyp (4.7, Kanal: PROTOKOLL). Der
    /// Client 0.3.0 stellt sie deshalb nur, wenn der Server in Welcome mindestens 0.3.0 meldet; sonst kommt ohne Senden eine
    /// Antwort VERSION mit einem Satz. Bekannte Anfragen gehen weiter hinaus. Der Server hier ist 0.3.0, meldet sich aber als 0.2.0.
    /// </summary>
    [Test]
    public static async Task Draht_Client030AmServer020_StelltKeineAnfrageDieDortTrennt()
    {
        await using var t = await Testserver.StartenAsync();
        t.Dienst.Zentrale.Version = "0.2.0";
        var halter = new ServerHalter { Server = t };
        await using var test = await KernFunker.AktivAsync(halter, "930000000000000021", "test", "ADMIN", Lokal(71));
        Pruefe.Gleich("0.2.0", test.Client.Lage.Welcome?.Server?.Version, "Welcome meldet 0.2.0");

        var verzeichnis = await test.Client.AnfrageAsync(Nachrichtentyp.KontenVerzeichnis, new LeererBody());
        Pruefe.Gleich(Fehlercode.Version, verzeichnis.Code, "KontenVerzeichnis geht nicht hinaus");
        Pruefe.Wahr(verzeichnis.Fehler?.Text?.Contains("0.2.0", StringComparison.Ordinal) == true, "der Satz nennt die Fassung: " + verzeichnis.Fehler?.Text);
        var liste = await test.Client.AnfrageAsync(Nachrichtentyp.Beauftragung, new BeauftragungAnfrage { Op = BeauftragungOperation.Liste });
        Pruefe.Gleich(Fehlercode.Version, liste.Code, "Beauftragung geht nicht hinaus");
        var zustand = await test.Client.AnfrageAsync(Nachrichtentyp.Zustand, new LeererBody());
        Pruefe.Wahr(zustand.Ok, "bekannte Anfragen gehen weiter");
        Pruefe.Wahr(test.Enden.IsEmpty, "die Sitzung steht");

        Pruefe.Gleich("Der Server hat noch Fassung 0.2.0 und kennt das nicht. Erst den Server aktualisieren, dann geht es.",
            FunkClientDienst.Fehlertext(Fehlercode.Version, null, verzeichnis.Fehler?.Text), "Satz im Client");
    }

    // ---------------------------------------------------------------- Brett im Takt

    [Test]
    public static void Brett_NachzueglerNach15Minuten_KeinNeuaufbauJedeSekunde()
    {
        var a = new Aufbau(dienst: FunkDienstAttrappe.MitRaidabend());
        using var _ = a;
        var b = a.Brett();
        a.Dienst.LageSetzen(l => FunkDienstAttrappe.Belegungsstand(l with
        {
            Funker = [.. l.Funker, new FunkerInfo(15, "Wolke", true, null, null) { ScName = "Wolke", Details = new FunkerDetails([], [], []) }],
        }));
        var wolke = b.Karte(15)!;
        Pruefe.Wahr(b.Nachzuegler.Contains(wolke), "neu im Funk");
        a.Jetzt = FunkDienstAttrappe.Abend.AddMinutes(16);
        b.Takt();
        Pruefe.Wahr(!b.Nachzuegler.Contains(wolke) && b.OhnePlatz.Contains(wolke), "nach 15 Minuten unter ohne Platz");
        var vorher = b.Aufbauten;
        for (var s = 1; s <= 60; s++)
        {
            a.Jetzt = FunkDienstAttrappe.Abend.AddMinutes(16).AddSeconds(s);
            b.Takt();
        }
        Pruefe.Wahr(b.Aufbauten - vorher <= 1, "eine Minute Takt ohne Änderung baut nicht jede Sekunde neu: " + (b.Aufbauten - vorher));
        Pruefe.Wahr(b.OhnePlatz.Contains(wolke), "Wolke bleibt unter ohne Platz");
    }
}
