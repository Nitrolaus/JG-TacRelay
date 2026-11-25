// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>
/// P-08 Zufallstests gegen ein bewusst schlichtes Referenzmodell von 6.3/6.4, dazu Heißpfad ohne Allokation,
/// Formprüfung, Ratenlimit, SendeFehler-Drossel, Takt, Sitzungsende und Hören nur für aktive Sitzungen (6.8).
/// </summary>
internal static class WeiterleitungEigenschaftenTests
{
    private const uint A = Funkprobe.A;
    private const uint B = Funkprobe.B;
    private const uint C = Funkprobe.C;
    private const uint E = Funkprobe.E;

    // ---- P-08: Zufall gegen Referenzmodell ----

    private sealed record Erwartung(bool Verworfen, SendeFehlerCode Grund, Dictionary<uint, (Stempelart Art, ushort[] Netze)> Kopien);

    /// <summary>Referenz: 6.3 und 6.4 wörtlich über den Konto-Datensätzen, ohne Tabellen.</summary>
    private static Erwartung Referenz(Schnappschuss z, uint sitzung, Sendeart zielart, ushort[] ziele, bool va, long jetzt)
    {
        var leer = new Dictionary<uint, (Stempelart, ushort[])>();
        var k = z.KontoVonSitzung(sitzung)!;
        var rechte = k.Rechte;
        bool Stumm(ushort netz) => k.Stumm.Any(s => s.Bis > jetzt && (s.NetzId == 0 || s.NetzId == netz));
        var erlaubt = new List<ushort>();
        var gestrichen = new List<SendeFehlerCode>();
        if (zielart == Sendeart.Netz)
        {
            if (ziele.Length > 1 && !rechte.Hat(Recht.MehrfachSenden)) return new(true, SendeFehlerCode.MehrfachNichtErlaubt, leer);
            foreach (var n in ziele)
            {
                if (!z.Netze.ContainsKey(n)) gestrichen.Add(SendeFehlerCode.UnbekanntesNetz);
                else if (n == 1 && !rechte.Hat(Recht.GlobalSenden)) gestrichen.Add(SendeFehlerCode.KeinRechtGlobal);
                else if (n != 1 && k.FeldMitNetz(n) is not { Senden: true }) gestrichen.Add(SendeFehlerCode.KeinSenderecht);
                else if (Stumm(n)) gestrichen.Add(SendeFehlerCode.Stummgeschaltet);
                else if (z.Netze[n].NurTaste && va) gestrichen.Add(SendeFehlerCode.NurTaste);
                else erlaubt.Add(n);
            }
        }
        else
        {
            if (!rechte.Hat(Recht.Durchsage) || (ziele.Length == 0 && !rechte.Hat(Recht.DurchsageAlle))) return new(true, SendeFehlerCode.KeinRechtDurchsage, leer);
            if (z.Einstellungen.DurchsageNurTaste && va) return new(true, SendeFehlerCode.NurTaste, leer);
            if (k.Stumm.Any(s => s.Bis > jetzt && s.NetzId == 0)) return new(true, SendeFehlerCode.Stummgeschaltet, leer);
            foreach (var n in ziele)
            {
                if (!z.Netze.ContainsKey(n)) gestrichen.Add(SendeFehlerCode.UnbekanntesNetz);
                else if (Stumm(n)) gestrichen.Add(SendeFehlerCode.Stummgeschaltet);
                else erlaubt.Add(n);
            }
        }
        if (erlaubt.Count == 0 && gestrichen.Count > 0) return new(true, gestrichen[0], leer);

        var kopien = new Dictionary<uint, (Stempelart, ushort[])>();
        foreach (var r in z.Sitzungen.Values)
        {
            if (r.SitzungsId == sitzung) continue;
            var rk = z.Konten[r.KontoId];
            ushort[] treffer;
            Stempelart art;
            if (zielart == Sendeart.Durchsage)
            {
                art = Stempelart.Durchsage;
                treffer = [.. erlaubt.Where(n => rk.FeldMitNetz(n) is not null).OrderBy(n => rk.FeldMitNetz(n)!.Value.Feld)];
                if (ziele.Length > 0 && treffer.Length == 0) continue;
            }
            else
            {
                var normal = erlaubt.Where(n => n != 1 && rk.FeldMitNetz(n) is { Hoeren: true }).OrderBy(n => rk.FeldMitNetz(n)!.Value.Feld);
                treffer = erlaubt.Contains((ushort)1) ? [1, .. normal] : [.. normal];
                if (treffer.Length == 0) continue;
                art = treffer[0] == 1 ? Stempelart.Global : Stempelart.Netz;
            }
            kopien[r.SitzungsId] = (art, treffer);
        }
        return new(false, SendeFehlerCode.Keiner, kopien);
    }

    private static Schnappschuss ZufallsLage(Random rnd, int konten)
    {
        var bauer = new SchnappschussBauer().MitEinstellungen(new Einstellungen(DurchsageNurTaste: rnd.Next(2) == 0));
        ushort[] netze = [2, 3, 4, 5, 6, 7, 8, 9];
        foreach (var n in netze) bauer.MitNetz(new Netz(n, $"N{n}") { NurTaste = rnd.Next(4) == 0 });
        for (uint i = 0; i < konten; i++)
        {
            var id = 2000 + i;
            var stufe = (Stufe)rnd.Next(3);
            var felder = netze.OrderBy(_ => rnd.Next()).Take(rnd.Next(0, 7))
                .Select((n, j) => new Mitgliedschaft((byte)(j * 2 + rnd.Next(2)), n, rnd.Next(3) > 0, rnd.Next(2) == 0, false))
                .OrderBy(m => m.Feld);
            var stumm = rnd.Next(4) == 0
                ? ImmutableArray.Create(new Stummschaltung((ushort)(rnd.Next(3) == 0 ? 0 : netze[rnd.Next(netze.Length)]), Funkprobe.Start + 60_000, 1))
                : [];
            bauer.MitKonto(new Konto
            {
                Id = id,
                Name = $"K{i}",
                Stufe = stufe,
                Plus = rnd.Next(5) == 0 ? Recht.MehrfachSenden : Recht.Keine,
                Minus = rnd.Next(5) == 0 ? Recht.GlobalSenden : Recht.Keine,
                Felder = [.. felder],
                Stumm = stumm,
            });
            if (rnd.Next(6) > 0) bauer.MitSitzung(30_000 + i, id);
        }
        return bauer.Bauen();
    }

    [Test]
    public static void P08_Zufall_GleichReferenzUndEigenschaften()
    {
        var rnd = new Random(20260929);
        var pruefungen = 0;
        for (var lauf = 0; lauf < 400; lauf++)
        {
            var z = ZufallsLage(rnd, rnd.Next(2, 12));
            var sitzungen = z.Sitzungen.Keys.ToArray();
            if (sitzungen.Length == 0) continue;
            for (var p = 0; p < 20; p++)
            {
                var sender = sitzungen[rnd.Next(sitzungen.Length)];
                var zielart = rnd.Next(3) == 0 ? Sendeart.Durchsage : Sendeart.Netz;
                ushort[] moeglich = zielart == Sendeart.Netz ? [1, 2, 3, 4, 5, 6, 7, 8, 9, 42] : [2, 3, 4, 5, 6, 7, 8, 9, 42];
                var ziele = moeglich.Where(_ => rnd.Next(4) == 0).ToArray();
                if (zielart == Sendeart.Netz && ziele.Length == 0) ziele = [moeglich[rnd.Next(moeglich.Length)]];
                var va = rnd.Next(3) == 0;

                var relay = new Weiterleiter();
                var m = new Mitschrift();
                var paket = new Sprachpaket(sender, 7, 1, va ? Paketkennzeichen.Va : 0, zielart, ziele, Funkprobe.Opus);
                var ist = relay.Verarbeiten(z, paket, Funkprobe.Start, m);
                var soll = Referenz(z, sender, zielart, ziele, va, Funkprobe.Start);
                var fall = $"Lauf {lauf}/{p}: {zielart} [{string.Join(",", ziele)}] va={va}";

                Pruefe.Gleich(soll.Verworfen, ist.Verworfen, fall + " verworfen");
                if (soll.Verworfen)
                {
                    Pruefe.Gleich(soll.Grund, ist.Grund, fall + " Grund");
                    Pruefe.Gleich(0, m.Sprache.Count, fall + " keine Kopie");
                    continue;
                }
                Pruefe.Gleich(soll.Kopien.Count, m.Sprache.Count, fall + " Zahl der Kopien");
                Pruefe.Gleich(m.Sprache.Count, m.Sprache.Select(s => s.Stempel.Empfaenger).Distinct().Count(), fall + " höchstens eine Kopie je Empfänger");
                foreach (var (stempel, netze, _) in m.Sprache)
                {
                    Pruefe.Wahr(stempel.Empfaenger != sender, fall + " Absender ist nie Empfänger");
                    Pruefe.Wahr((stempel.Kennzeichen & Paketkennzeichen.Beginn) != 0, fall + " erstes Paket mit BEGINN");
                    Pruefe.Wahr(soll.Kopien.TryGetValue(stempel.Empfaenger, out var erwartet), fall + $" Empfänger {stempel.Empfaenger} erwartet");
                    Pruefe.Gleich(erwartet.Art, stempel.Art, fall + " Art");
                    Pruefe.Gleich(string.Join(",", erwartet.Netze), string.Join(",", netze), fall + " Stempel = Schnittmenge in Feldfolge");
                    if (zielart == Sendeart.Netz)
                    {
                        var rk = z.KontoVonSitzung(stempel.Empfaenger)!;
                        Pruefe.Wahr(netze.All(n => n == 1 || rk.FeldMitNetz(n) is { Hoeren: true }), fall + " kein Netz ohne Mitgliedschaft mit Hören");
                    }
                }
                pruefungen++;
            }
        }
        Pruefe.Wahr(pruefungen > 1000, $"genug zugestellte Fälle ({pruefungen})");
    }

    [Test]
    public static void P08_Zufall_PhasenfolgeBeginnNurEinmal()
    {
        var rnd = new Random(4711);
        for (var lauf = 0; lauf < 50; lauf++)
        {
            var f = new Funkprobe();
            var gesehen = new HashSet<(uint Empfaenger, ushort Phase)>();
            ushort phase = 1;
            for (var i = 0; i < 200; i++)
            {
                f.Jetzt += 20;
                if (rnd.Next(15) == 0) phase++;
                if (rnd.Next(25) == 0) f.Tun(Auftrag.HoerenSetzen(new[] { B, C, E }[rnd.Next(3)], 7, rnd.Next(2) == 0));
                f.M.Leeren();
                f.Senden(A, Sendeart.Netz, [2, 7], phase: phase, frame: (uint)i);
                foreach (var s in f.M.Sprache)
                {
                    var schluessel = (s.Stempel.Empfaenger, s.Stempel.Sprechphase);
                    if (s.Stempel.Erzeugt)
                    {
                        Pruefe.Wahr(gesehen.Remove(schluessel), "erzeugtes ENDE nur an Bediente");
                        continue;
                    }
                    var neu = gesehen.Add(schluessel);
                    Pruefe.Gleich(neu, (s.Stempel.Kennzeichen & Paketkennzeichen.Beginn) != 0, $"BEGINN genau beim ersten Paket der Phase an {s.Stempel.Empfaenger}");
                }
            }
        }
    }

    // ---- Heißpfad ohne Allokation ----

    private sealed class Zaehler : IWeiterleitungsZiel
    {
        public long Kopien;
        public long Sonstiges;

        public void Sprache(in Stempel stempel, ReadOnlySpan<ushort> netze, ReadOnlySpan<byte> opus) => Kopien++;
        public void DurchsageStart(uint empfaenger, uint sprecher, Stempelart art, ReadOnlySpan<ushort> netze, ushort sprechphase) => Sonstiges++;
        public void DurchsageEnde(uint empfaenger, uint sprecher, ushort sprechphase) => Sonstiges++;
        public void SendeFehler(uint sitzung, ReadOnlySpan<SendeFehlerEintrag> fehler, bool teilweise, uint verworfen) => Sonstiges++;
        public void RateUeberschritten(uint sitzung, uint kontoId) => Sonstiges++;
    }

    [Test]
    public static void Heisspfad_OhneAllokationJePaket()
    {
        const uint rl1 = 1006;
        const uint rl2 = 1007;
        var f = new Funkprobe(b => b
            .MitKonto(new Konto { Id = rl1, Name = "RL1", Stufe = Stufe.Raidleiter, Felder = [Funkprobe.F(0, 2, true, true)] })
            .MitKonto(new Konto { Id = rl2, Name = "RL2", Stufe = Stufe.Raidleiter, Felder = [Funkprobe.F(0, 7, true, true), Funkprobe.F(1, 2, true, true)] })
            .MitSitzung(Funkprobe.S(rl1), rl1)
            .MitSitzung(Funkprobe.S(rl2), rl2));
        var z = f.Z;
        var ziel = new Zaehler();
        ushort[] netz27 = [2, 7];
        ushort[] netz299 = [2, 99];
        ushort[] netz7 = [7];
        ushort[] netz12 = [1, 2];
        ushort[] alle = [];
        var opus = Funkprobe.Opus;
        var jetzt = Funkprobe.Start;
        var verworfen = 0L;

        // Je Sender höchstens 55 Pakete je Sekunde, damit das Ratenlimit nicht greift. A wechselt die Ziele
        // bei jedem Paket (neue Phase, erzeugte ENDE, SendeFehler), B beendet alle 10 Pakete seine Phase.
        void Runde(int n)
        {
            for (var i = 0; i < n; i++)
            {
                jetzt += 20;
                var phase = (ushort)(i / 10);
                var frame = (uint)i;
                verworfen += f.Relay.Verarbeiten(z, new Sprachpaket(Funkprobe.S(A), frame, phase, 0, Sendeart.Netz, (i & 1) == 0 ? netz27 : netz299, opus), jetzt, ziel).Verworfen ? 1 : 0;
                verworfen += f.Relay.Verarbeiten(z, new Sprachpaket(Funkprobe.S(B), frame, phase, 0, Sendeart.Netz, netz7, opus), jetzt, ziel).Verworfen ? 1 : 0;
                verworfen += f.Relay.Verarbeiten(z, new Sprachpaket(Funkprobe.S(rl1), frame, phase, 0, Sendeart.Durchsage, alle, opus), jetzt, ziel).Verworfen ? 1 : 0;
                verworfen += f.Relay.Verarbeiten(z, new Sprachpaket(Funkprobe.S(rl2), frame, phase, 0, Sendeart.Netz, netz12, opus), jetzt, ziel).Verworfen ? 1 : 0;
                if (i % 10 == 9)
                    verworfen += f.Relay.Verarbeiten(z, new Sprachpaket(Funkprobe.S(B), frame + 1, phase, Paketkennzeichen.Ende, Sendeart.Netz, netz7, []), jetzt, ziel).Verworfen ? 1 : 0;
                if (i % 5 == 0) f.Relay.Takt(z, jetzt, ziel);
            }
        }

        Runde(2_000);
        var vorher = GC.GetAllocatedBytesForCurrentThread();
        var kopienVorher = ziel.Kopien;
        Runde(10_000);
        var allokiert = GC.GetAllocatedBytesForCurrentThread() - vorher;
        Pruefe.Gleich(0L, verworfen, "kein Paket verworfen (Ratenlimit nicht erreicht)");
        Pruefe.Wahr(ziel.Kopien - kopienVorher > 150_000, $"Last lief ({ziel.Kopien - kopienVorher} Kopien)");
        Pruefe.Gleich(0L, allokiert, "Bytes allokiert in 41.000 Paketen nach dem Anlauf");
    }

    // ---- Form (5.4, 5.13) ----

    [Test]
    public static void Form_VerletzungenGanzVerworfen()
    {
        (string Was, Sendeart Art, ushort[] Ziele, Paketkennzeichen Kz, byte[] Opus)[] faelle =
        [
            ("Zielart 3", (Sendeart)3, [2], 0, Funkprobe.Opus),
            ("Zielart 0", Sendeart.Keine, [2], 0, Funkprobe.Opus),
            ("NETZ ohne Ziel", Sendeart.Netz, [], 0, Funkprobe.Opus),
            ("NETZ 17 Ziele", Sendeart.Netz, [.. Enumerable.Range(2, 17).Select(i => (ushort)i)], 0, Funkprobe.Opus),
            ("DURCHSAGE 65 Ziele", Sendeart.Durchsage, [.. Enumerable.Range(2, 65).Select(i => (ushort)i)], 0, Funkprobe.Opus),
            ("doppeltes Ziel", Sendeart.Netz, [2, 2], 0, Funkprobe.Opus),
            ("ENDE mit Opus", Sendeart.Netz, [2], Paketkennzeichen.Ende, Funkprobe.Opus),
            ("ENDE mit PREROLL", Sendeart.Netz, [2], Paketkennzeichen.Ende | Paketkennzeichen.Preroll, []),
            ("ohne Opus", Sendeart.Netz, [2], 0, []),
            ("Opus 401 Byte", Sendeart.Netz, [2], 0, [0xF8, .. new byte[400]]),
            ("Stereo", Sendeart.Netz, [2], 0, [0xFC, 1]),
            ("10 ms CELT", Sendeart.Netz, [2], 0, [0xF0, 1]),
            ("c=3 ohne Anzahl", Sendeart.Netz, [2], 0, [0xFB]),
            ("c=3, 3 x 10 ms", Sendeart.Netz, [2], 0, [0xF3, 0x03]),
        ];
        foreach (var (was, art, ziele, kz, opus) in faelle)
        {
            var f = new Funkprobe();
            var r = f.Relay.Verarbeiten(f.Z, new Sprachpaket(Funkprobe.S(A), 1, 1, kz, art, ziele, opus), f.Jetzt, f.M);
            Pruefe.Wahr(r.Verworfen && r.Grund == SendeFehlerCode.Ungueltig, was + ": UNGUELTIG");
            Pruefe.Gleich(0, f.M.Sprache.Count, was + ": nichts weitergeleitet");
        }

        (string Was, byte[] Opus)[] gueltig =
        [
            ("SILK 20 ms", [0x08, 1]),
            ("Hybrid 20 ms", [0x68, 1]),
            ("CELT 2 x 10 ms", [0xF1, 1, 2]),
            ("c=3, 2 x 10 ms", [0xF3, 0x02, 1]),
            ("Opus 400 Byte", [0xF8, .. new byte[399]]),
        ];
        foreach (var (was, opus) in gueltig)
        {
            var f = new Funkprobe();
            var r = f.Relay.Verarbeiten(f.Z, new Sprachpaket(Funkprobe.S(A), 1, 1, 0, Sendeart.Netz, [2], opus), f.Jetzt, f.M);
            Pruefe.Falsch(r.Verworfen, was + ": angenommen");
        }
    }

    [Test]
    public static void Form_ReservierteBitsUndBeginnDesSendersNichtUebernommen()
    {
        var f = new Funkprobe();
        var kz = (Paketkennzeichen)0xF0 | Paketkennzeichen.Beginn | Paketkennzeichen.Preroll | Paketkennzeichen.Va;
        f.Relay.Verarbeiten(f.Z, new Sprachpaket(Funkprobe.S(B), 1, 1, kz, Sendeart.Netz, [7], Funkprobe.Opus), f.Jetzt, f.M);
        f.Relay.Verarbeiten(f.Z, new Sprachpaket(Funkprobe.S(B), 2, 1, kz, Sendeart.Netz, [7], Funkprobe.Opus), f.Jetzt + 20, f.M);
        var zweites = f.M.Sprache.Skip(2).Select(s => s.Stempel.Kennzeichen).Distinct().Single();
        Pruefe.Gleich(Paketkennzeichen.Preroll | Paketkennzeichen.Va, zweites, "reserviert und Sender-BEGINN gelöscht, PREROLL und VA übernommen");
        Pruefe.Wahr(f.M.Sprache.Take(2).All(s => s.Stempel.Kennzeichen == (Paketkennzeichen.Beginn | Paketkennzeichen.Preroll | Paketkennzeichen.Va)),
            "BEGINN setzt der Server je Empfänger");
        Pruefe.Wahr(f.M.Sprache.All(s => s.Opus == Funkprobe.Opus.Length), "Opus unverändert");
    }

    // ---- Ratenlimit (4.8, 6.3) ----

    [Test]
    public static void Rate_PaketTopfUndByteTopf()
    {
        var f = new Funkprobe();
        var angenommen = 0;
        for (uint i = 0; i < 30; i++)
            if (!f.Senden(B, Sendeart.Netz, [7], frame: i).Verworfen) angenommen++;
        Pruefe.Gleich(Fristen.PaketeBurst, angenommen, "Burst 20 Pakete im selben Augenblick");
        Pruefe.Gleich(1, f.M.Raten.Count, "Meldung RATE höchstens einmal je Sekunde");
        Pruefe.Wahr(f.M.Fehler.Exists(x => x.Fehler.Any(e => e.Code == SendeFehlerCode.Grenze)), "SendeFehler GRENZE");
        f.Jetzt += 1000;
        angenommen = 0;
        for (uint i = 0; i < 60; i++)
        {
            f.Jetzt += 1000 / 60;
            if (!f.Senden(B, Sendeart.Netz, [7], frame: 100 + i).Verworfen) angenommen++;
        }
        Pruefe.Wahr(angenommen >= 59, $"60 Pakete je Sekunde gehen durch ({angenommen})");

        var g = new Funkprobe();
        byte[] gross = [0xF8, .. new byte[399]];
        var bytesAngenommen = 0;
        for (uint i = 0; i < 12; i++)
        {
            var r = g.Relay.Verarbeiten(g.Z, new Sprachpaket(Funkprobe.S(B), i, 1, 0, Sendeart.Netz, [7], gross), g.Jetzt, g.M);
            if (!r.Verworfen) bytesAngenommen++;
        }
        Pruefe.Gleich(10, bytesAngenommen, "Byte-Topf: Burst 4000 Byte = 10 x 400");
    }

    // ---- SendeFehler-Drossel (4.5) ----

    [Test]
    public static void SendeFehler_HoechstensEinerJeSekunde_Gesammelt()
    {
        var f = new Funkprobe();
        f.Senden(C, Sendeart.Netz, [2], frame: 1);
        Pruefe.Gleich(1, f.M.Fehler.Count, "erster sofort");
        f.Jetzt += 100;
        f.Senden(C, Sendeart.Netz, [2], frame: 2);
        f.Jetzt += 100;
        f.Senden(C, Sendeart.Netz, [8], frame: 3);
        Pruefe.Gleich(1, f.M.Fehler.Count, "weitere binnen 1 s gesammelt");
        f.Relay.Takt(f.Z, f.Jetzt + 500, f.M);
        Pruefe.Gleich(1, f.M.Fehler.Count, "Takt vor Ablauf schickt nichts");
        f.Relay.Takt(f.Z, f.Jetzt + 800, f.M);
        Pruefe.Gleich(2, f.M.Fehler.Count, "nach 1 s nachgereicht");
        var zweiter = f.M.Fehler[1];
        Pruefe.Gleich(2u, zweiter.Verworfen, "verworfen seit der letzten Meldung");
        Pruefe.Gleich("KeinSenderecht:2 KeinSenderecht:8", string.Join(" ", zweiter.Fehler.Select(e => $"{e.Code}:{e.NetzId}")), "gleiche Einträge zusammengefasst");
        Pruefe.Wahr(f.Relay.Verworfen(SendeFehlerCode.KeinSenderecht) == 3, "jeder Verwurf gezählt");
    }

    // ---- DurchsageEnde, Takt, Sitzungsende ----

    [Test]
    public static void DurchsageEnde_NachPauseUndBeiEnde()
    {
        var f = new Funkprobe();
        f.Senden(A, Sendeart.Durchsage, [7], phase: 3, frame: 1);
        Pruefe.Gleich(3, f.M.Starts.Count, "DurchsageStart an B, C, E");
        f.Relay.Takt(f.Z, f.Jetzt + 299, f.M);
        Pruefe.Gleich(0, f.M.Enden.Count, "vor 300 ms kein DurchsageEnde");
        f.Relay.Takt(f.Z, f.Jetzt + 300, f.M);
        Pruefe.Gleich(3, f.M.Enden.Count, "nach 300 ms ohne Paket DurchsageEnde");
        f.M.Leeren();
        f.Jetzt += 320;
        f.Senden(A, Sendeart.Durchsage, [7], phase: 3, frame: 2);
        Pruefe.Gleich(3, f.M.Starts.Count, "Fortsetzung zeigt das Banner wieder (ohne BEGINN)");
        Pruefe.Wahr(f.M.Sprache.TrueForAll(s => (s.Stempel.Kennzeichen & Paketkennzeichen.Beginn) == 0), "kein BEGINN");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Durchsage, [7], phase: 3, frame: 3, ende: true);
        Pruefe.Gleich("B:DURCHSAGE[7]+E C:DURCHSAGE[7]+E E:DURCHSAGE[7]+E", f.M.Empfang(true), "ENDE weitergeleitet");
        Pruefe.Gleich(3, f.M.Enden.Count, "DurchsageEnde beim ENDE-Paket");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Durchsage, [7], phase: 3, frame: 4, ende: true);
        Pruefe.Gleich(0, f.M.Enden.Count, "zweite ENDE-Kopie ohne weiteres DurchsageEnde");
        Pruefe.Gleich(3, f.M.Sprache.Count, "ENDE-Kopie an die Bedienten");
    }

    [Test]
    public static void Global_ArtUndDurchsageStart_EmpfaengerFaelltHeraus()
    {
        var f = new Funkprobe();
        f.Senden(A, Sendeart.Netz, [1, 2], phase: 1, frame: 1);
        Pruefe.Wahr(f.M.Starts.TrueForAll(s => s.Art == Stempelart.Global), "DurchsageStart{GLOBAL}");
        Pruefe.Gleich("[1,2]", "[" + string.Join(",", f.M.Starts.Single(s => s.Empfaenger == Funkprobe.S(C)).Netze) + "]", "Netze für C");
        // Stummschaltung nur für Global: die Phase geht an [2] weiter, B und D fallen heraus.
        Pruefe.Wahr(f.Stumm(A, 1).Erfolg, "Stumm für Global");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(A, Sendeart.Netz, [1, 2], phase: 1, frame: 2);
        Pruefe.Gleich("B:GLOBAL[1]+E+X C:NETZ[2] D:GLOBAL[1]+E+X E:NETZ[2]", f.M.Empfang(true), "herausgefallene bekommen ENDE");
        Pruefe.Gleich(4, f.M.Enden.Count, "DurchsageEnde an B und D (heraus) sowie C und E (nur noch NETZ)");
    }

    [Test]
    public static void Sitzungsende_DesSprechers_EndeUndDurchsageEnde()
    {
        var f = new Funkprobe();
        f.Senden(A, Sendeart.Durchsage, [], phase: 2, frame: 50);
        f.M.Leeren();
        f.Relay.SitzungBeendet(f.Z, Funkprobe.S(A), f.Jetzt + 10, f.M);
        Pruefe.Gleich("B:DURCHSAGE[]+E+X C:DURCHSAGE[]+E+X D:DURCHSAGE[]+E+X E:DURCHSAGE[]+E+X", f.M.Empfang(true), "erzeugtes ENDE");
        Pruefe.Gleich(4, f.M.Enden.Count, "DurchsageEnde beim Sitzungsende des Sprechers");
        Pruefe.Wahr(f.M.Sprache.TrueForAll(s => s.Stempel.FrameNr == 51), "Frame-Nr zuletzt + 1");
    }

    [Test]
    public static void Sitzungsende_DesEmpfaengers_KeineAltlast()
    {
        var f = new Funkprobe();
        f.Senden(B, Sendeart.Netz, [7], phase: 1, frame: 1);
        Pruefe.Wahr(f.Tun(Auftrag.SitzungBeenden(Funkprobe.S(E))).Erfolg, "E trennt");
        f.Relay.SitzungBeendet(f.Z, Funkprobe.S(E), f.Jetzt, f.M);
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], phase: 1, frame: 2);
        Pruefe.Gleich("A:NETZ[7]", f.M.Empfang(true), "nur noch A");
        Pruefe.Wahr(f.Tun(Auftrag.SitzungAktivieren(E, 55_555, E * 10)).Erfolg, "E wieder da (Frist)");
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(B, Sendeart.Netz, [7], phase: 1, frame: 3);
        Pruefe.Gleich("55555:NETZ[7]+B A:NETZ[7]", f.M.Empfang(true), "neue Sitzung von E bekommt BEGINN");
    }

    // ---- 6.8 Hören nur für Mitglieder, nur aktive Sitzungen ----

    [Test]
    public static void Hoeren_NurAktiveSitzungen_SenderOhneSitzungVerworfen()
    {
        // Ein Konto in REGISTRIERUNG oder WARTERAUM hat keine aktive Sitzung im Schnappschuss.
        var f = new Funkprobe(b => b.MitKonto(new Konto { Id = 1006, Name = "F", Felder = [Funkprobe.F(0, 7, true, true)] }));
        f.Senden(A, Sendeart.Durchsage, []);
        Pruefe.Falsch(f.M.Sprache.Exists(s => s.Stempel.Absender == 1006 || s.Stempel.Empfaenger == 1006 + 9000), "keine Sprache an Konten ohne aktive Sitzung");
        Pruefe.Gleich(4, f.M.Sprache.Count, "Durchsage an alle heißt an alle aktiven");
        var r = f.Relay.Verarbeiten(f.Z, new Sprachpaket(99_999, 1, 1, 0, Sendeart.Netz, [7], Funkprobe.Opus), f.Jetzt, f.M);
        Pruefe.Wahr(r.Verworfen && f.Relay.OhneSitzung == 1, "Paket einer unbekannten Sitzung stumm verworfen");
    }

    private sealed class Abwurf(uint gesperrt) : IZustellung
    {
        public bool Zustellbar(uint empfaenger, uint absenderSitzung, ushort sprechphase, Stempelart art) =>
            empfaenger != gesperrt || art != Stempelart.Netz;
    }

    [Test]
    public static void Zustellung_StromAbwurfNieFuerGlobalOderDurchsage()
    {
        var f = new Funkprobe();
        var relay = new Weiterleiter(new Abwurf(Funkprobe.S(E)));
        relay.Verarbeiten(f.Z, new Sprachpaket(Funkprobe.S(B), 1, 1, 0, Sendeart.Netz, [7], Funkprobe.Opus), f.Jetzt, f.M);
        Pruefe.Gleich("A:NETZ[7]", f.M.Empfang(), "NETZ-Strom an E abgeworfen");
        f.M.Leeren();
        relay.Verarbeiten(f.Z, new Sprachpaket(Funkprobe.S(A), 1, 1, 0, Sendeart.Netz, [1], Funkprobe.Opus), f.Jetzt, f.M);
        Pruefe.Wahr(f.M.Empfang().Contains("E:GLOBAL[1]", StringComparison.Ordinal), "GLOBAL geht trotzdem an E");
    }

    [Test]
    public static void Grenzfall_SechzehnZiele_SechzehnTreffer()
    {
        ushort[] netze = [.. Enumerable.Range(20, 16).Select(i => (ushort)i)];
        var bauer = new SchnappschussBauer();
        foreach (var n in netze) bauer.MitNetz(new Netz(n, $"N{n}"));
        ImmutableArray<Mitgliedschaft> alle = [.. netze.Select((n, i) => new Mitgliedschaft((byte)(15 - i), n, true, true, false)).OrderBy(m => m.Feld)];
        var z = bauer
            .MitKonto(new Konto { Id = 1, Name = "Sender", Stufe = Stufe.Raidleiter, Felder = alle })
            .MitKonto(new Konto { Id = 2, Name = "Hörer", Felder = alle })
            .MitSitzung(11, 1)
            .MitSitzung(12, 2)
            .Bauen();
        var m = new Mitschrift();
        var r = new Weiterleiter().Verarbeiten(z, new Sprachpaket(11, 1, 1, 0, Sendeart.Netz, netze, Funkprobe.Opus), Funkprobe.Start, m);
        Pruefe.Falsch(r.Verworfen, "angenommen");
        Pruefe.Gleich(16, m.Sprache.Single().Netze.Length, "alle 16 Netze gestempelt");
        Pruefe.Gleich(string.Join(",", netze.Reverse()), string.Join(",", m.Sprache.Single().Netze), "in Feldfolge des Hörers");

        // Mit Global: 15 Netze plus 1.
        ushort[] mitGlobal = [1, .. netze[..15]];
        m.Leeren();
        new Weiterleiter().Verarbeiten(z, new Sprachpaket(11, 1, 1, 0, Sendeart.Netz, mitGlobal, Funkprobe.Opus), Funkprobe.Start, m);
        Pruefe.Gleich(16, m.Sprache.Single().Netze.Length, "Global plus 15");
        Pruefe.Gleich((ushort)1, m.Sprache.Single().Netze[0], "Global zuerst");
    }

    [Test]
    public static void Senderecht_ReineFunktion_Oeffentlich()
    {
        var f = new Funkprobe();
        Span<ushort> erlaubt = stackalloc ushort[64];
        Span<SendeFehlerEintrag> gestrichen = stackalloc SendeFehlerEintrag[64];
        var code = Senderecht.Pruefen(f.Z, new Sprachpaket(Funkprobe.S(A), 1, 1, 0, Sendeart.Netz, [1, 2, 7, 99], Funkprobe.Opus), f.Jetzt,
            erlaubt, gestrichen, out var ne, out var ng);
        Pruefe.Gleich(SendeFehlerCode.Keiner, code, "nicht ganz verworfen");
        Pruefe.Gleich("1,2,7", string.Join(",", erlaubt[..ne].ToArray()), "erlaubt");
        Pruefe.Gleich(1, ng, "ein Ziel gestrichen");
        Pruefe.Gleich(SendeFehlerCode.UnbekanntesNetz, gestrichen[0].Code, "99 unbekannt");
    }
}
