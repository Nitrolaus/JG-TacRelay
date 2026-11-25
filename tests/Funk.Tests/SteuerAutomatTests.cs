// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;
using static Funk.Tests.SteuerHilfe;

namespace Funk.Tests;

/// <summary>Zustandsautomat 4.6 und Prüfplan P-10, dazu F13 aus 8.10 und der Tunnel-Fall aus 8.3.</summary>
internal static class SteuerAutomatTests
{
    private const Nachrichtenart P = Nachrichtenart.Push;
    private const Nachrichtenart A = Nachrichtenart.Anfrage;
    private const Nachrichtenart W = Nachrichtenart.Antwort;

    /// <summary>Erwartung nach Tabelle 4.6, unabhängig vom Katalogcode aufgeschrieben: was der Client je Zustand senden darf.</summary>
    private static readonly Dictionary<ServerZustand, HashSet<(Nachrichtentyp, Nachrichtenart)>> Erlaubt = new()
    {
        [ServerZustand.Tls] = [],
        [ServerZustand.WarteHello] = [(Nachrichtentyp.Hello, P)],
        [ServerZustand.Registrierung] =
        [
            (Nachrichtentyp.Registrierung, A), (Nachrichtentyp.VerknuepfungBestaetigen, W), (Nachrichtentyp.Fehler, W),
            (Nachrichtentyp.Ping, A), (Nachrichtentyp.Bye, P),
        ],
        [ServerZustand.Warteraum] = [(Nachrichtentyp.Ping, A), (Nachrichtentyp.Bye, P)],
        [ServerZustand.AktivSync] = [(Nachrichtentyp.VoiceKeys, W), (Nachrichtentyp.Fehler, W), (Nachrichtentyp.Ping, A), (Nachrichtentyp.Bye, P)],
        [ServerZustand.Aktiv] =
        [
            (Nachrichtentyp.Bye, P), (Nachrichtentyp.Ping, A), (Nachrichtentyp.Fehler, P), (Nachrichtentyp.Fehler, W),
            (Nachrichtentyp.GeraeteListe, A), (Nachrichtentyp.GeraetEntscheiden, A), (Nachrichtentyp.GeraetEntfernen, A),
            (Nachrichtentyp.KontoVerwaltung, A), (Nachrichtentyp.Zustand, A),
            (Nachrichtentyp.SendeauswahlSetzen, A), (Nachrichtentyp.HoerenSetzen, A), (Nachrichtentyp.EinsteigenRolleSchiff, A),
            (Nachrichtentyp.Verschieben, A), (Nachrichtentyp.Stummschalten, A), (Nachrichtentyp.FeldSetzen, A),
            (Nachrichtentyp.SatzAnwenden, A), (Nachrichtentyp.PlaetzeFreigeben, A),
            (Nachrichtentyp.AufstellungSpeichern, A), (Nachrichtentyp.AufstellungListe, A), (Nachrichtentyp.AufstellungAnwenden, A),
            (Nachrichtentyp.AufstellungLoeschen, A), (Nachrichtentyp.RaidHelperImport, A), (Nachrichtentyp.RegelTabelle, A),
            (Nachrichtentyp.RaidHelperEvents, A), (Nachrichtentyp.SprechanzeigeAbo, A), (Nachrichtentyp.PegelVoreinstellungen, A),
            (Nachrichtentyp.PegelSetzen, A), (Nachrichtentyp.Ablage, A), (Nachrichtentyp.KeyRotation, A), (Nachrichtentyp.TunnelModus, A),
            (Nachrichtentyp.EmpfangsBericht, P), (Nachrichtentyp.Verwaltung, A), (Nachrichtentyp.SpracheTunnel, P), (Nachrichtentyp.VoiceKeys, W),
            // Stufe 1: Profil, Mitglieder, Journal, Anträge, Admin-Nachweis (3.9, 6.13).
            (Nachrichtentyp.ProfilSetzen, A), (Nachrichtentyp.Journal, A), (Nachrichtentyp.Rollenkatalog, A), (Nachrichtentyp.Mitglied, A),
            (Nachrichtentyp.Lieblingsrollen, A), (Nachrichtentyp.Untergilde, A), (Nachrichtentyp.Mitgliedskarte, A), (Nachrichtentyp.AntraegeAblehnen, A),
            (Nachrichtentyp.AdminNonce, A), (Nachrichtentyp.AdminAnmeldung, A), (Nachrichtentyp.AdminBestaetigen, A),
            (Nachrichtentyp.AdminAbmelden, A), (Nachrichtentyp.AdminEinrichtung, A),
            // Stufe 2 (0.3.0): Auftrag für einen Abend und Kontenverzeichnis fürs Brett; Belegung ist nur Push vom Server.
            (Nachrichtentyp.Beauftragung, A), (Nachrichtentyp.KontenVerzeichnis, A),
            // S2 (0.5.0): "Heute als Funker" und Phasen schalten; PhaseGeschaltet ist nur Push vom Server.
            (Nachrichtentyp.Funkermodus, A), (Nachrichtentyp.PhaseSchalten, A),
            // Stufe 3 (0.6.0): eine Rolle wählen, der Platz bleibt; Kanalsperren.
            (Nachrichtentyp.RolleWaehlen, A), (Nachrichtentyp.KanalSperre, A),
            // 0.11.7: Abzeichen vergeben (Admin-Sitzung, KONTEN_VERWALTEN).
            (Nachrichtentyp.Abzeichen, A),
            // 0.12.0: Meldesystem (Meldung senden, bereit melden); Meldestand und BereitStand sind nur Pushes vom Server.
            (Nachrichtentyp.MeldungSenden, A), (Nachrichtentyp.BereitMelden, A),
        ],
        [ServerZustand.Ende] = [],
    };

    // ---------------------------------------------------------------- P-10 jede Nachricht in jedem Zustand

    [Test]
    public static void P10_JedeNachrichtInJedemZustand()
    {
        var geprueft = 0;
        foreach (var zustand in Enum.GetValues<ServerZustand>())
        {
            var k = ServerIn(zustand);
            foreach (var typ in Enum.GetValues<Nachrichtentyp>())
            {
                foreach (var art in new[] { P, A, W })
                {
                    var soll = Erlaubt[zustand].Contains((typ, art));
                    var ist = k.Automat.ClientNachrichtErlaubt(typ, art) && Katalog.Von(typ).Fuer(Seite.Client, art).Erlaubt;
                    Pruefe.Gleich(soll, ist, $"{zustand}: {typ} als {art}");
                    geprueft++;
                }
            }
        }
        Pruefe.Gleich(7 * 76 * 3, geprueft, "Fälle: 7 Zustände, 76 Typen aus 4.4 (seit 0.12.0 mit dem Meldesystem), 3 Arten");
    }

    [Test]
    public static void P10_NichtErlaubt_VorAktivProtokollUndTrennung()
    {
        var faelle = new (ServerZustand Zustand, byte[] Rahmen)[]
        {
            (ServerZustand.WarteHello, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 1)),
            (ServerZustand.WarteHello, Json(Nachrichtentyp.Bye, new Bye { Grund = ByeGrund.Beendet })),
            (ServerZustand.Registrierung, Json(Nachrichtentyp.Hello, HelloWert())),
            (ServerZustand.Registrierung, Json(Nachrichtentyp.Zustand, LeererBody.Wert, nr: 1)),
            (ServerZustand.Warteraum, Json(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = "PC" }, nr: 1)),
            (ServerZustand.AktivSync, Json(Nachrichtentyp.Zustand, LeererBody.Wert, nr: 1)),
            (ServerZustand.AktivSync, Json(Nachrichtentyp.EmpfangsBericht, EmpfangsBerichtWert())),
            (ServerZustand.AktivSync, Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, TvA)),
        };
        foreach (var (zustand, rahmen) in faelle)
        {
            var k = ServerIn(zustand);
            var e = AnServer(k, rahmen);
            Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, $"{zustand}: Typ 0x{e.Kopf.Typ:X4}");
            Pruefe.Wahr(e.Trennen, "Trennung");
            Pruefe.Gleich(ServerZustand.Ende, k.Automat.Zustand, "ENDE");
        }
    }

    [Test]
    public static void P10_Aktiv_NichtImKatalog_Protokoll_KeinRechtIstSacheDerRechtepruefung()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        var e = AnServer(k, Json(Nachrichtentyp.Hello, HelloWert()));
        Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, "Hello in AKTIV");
        var k2 = ServerIn(ServerZustand.Aktiv);
        var e2 = AnServer(k2, Json(Nachrichtentyp.Verschieben, new VerschiebenAnfrage { Aenderungen = [new Verschiebung { KontoId = 1002, RolleId = 3 }] }, nr: 1));
        Pruefe.Gleich(EingangsStatus.Annehmen, e2.Status, "Verschieben: der Automat lässt durch, das Recht prüft 6.10");
        Pruefe.Gleich("ZUWEISEN", Katalog.Von(Nachrichtentyp.Verschieben).Recht, "Rechtespalte");
    }

    [Test]
    public static void P10_AnfrageNummern()
    {
        var k = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(EingangsStatus.Annehmen, AnServer(k, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 5)).Status, "5");
        Pruefe.Gleich(EingangsStatus.Annehmen, AnServer(k, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 9)).Status, "9 (Lücke erlaubt)");
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 9)).Fehler, "9 doppelt");

        var k2 = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k2, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 0x80000001)).Fehler, "Bit 31 vom Client");

        var k3 = ServerIn(ServerZustand.Aktiv);
        for (uint i = 1; i <= 32; i++) Pruefe.Gleich(EingangsStatus.Annehmen, AnServer(k3, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: i)).Status, $"offen {i}");
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k3, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 33)).Fehler, "33. offene Anfrage");

        var k4 = ServerIn(ServerZustand.Aktiv);
        for (uint i = 1; i <= 32; i++) AnServer(k4, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: i));
        Pruefe.Wahr(k4.AntwortGesendet(3), "Antwort auf 3");
        Pruefe.Gleich(EingangsStatus.Annehmen, AnServer(k4, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 40)).Status, "wieder Platz");

        var k5 = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k5, Json(Nachrichtentyp.Hello, HelloWert(), nr: 1)).Fehler, "Push-Typ als Anfrage");
        var k6 = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(Fehlercode.Protokoll, AnServer(k6, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 0)).Fehler, "Anfrage-Typ als Push");
    }

    [Test]
    public static void Anfragen_EigeneUndFremde()
    {
        var s = new EigeneAnfragen(Seite.Server);
        Pruefe.Wahr(s.VersucheStellen(Nachrichtentyp.VoiceKeys, 0, 10_000, 0, out var n1), "erste");
        Pruefe.Gleich(0x80000001u, n1, "Server beginnt bei 0x80000001");
        Pruefe.Wahr(s.VersucheStellen(Nachrichtentyp.VerknuepfungBestaetigen, 0, 120_000, 0, out var n2), "zweite");
        Pruefe.Gleich(0x80000002u, n2, "aufsteigend");
        Pruefe.Falsch(s.VersucheAufloesen(n1, (ushort)Nachrichtentyp.Ping, out _), "falscher Antworttyp");
        Pruefe.Wahr(s.VersucheAufloesen(n1, (ushort)Nachrichtentyp.Fehler, out var a), "Fehler als Antwort");
        Pruefe.Gleich(Nachrichtentyp.VoiceKeys, a.Typ, "Bezug");
        Pruefe.Falsch(s.VersucheAufloesen(n1, (ushort)Nachrichtentyp.VoiceKeys, out _), "schon erledigt");

        var c = new EigeneAnfragen(Seite.Client);
        for (var i = 1; i <= 32; i++)
        {
            Pruefe.Wahr(c.VersucheStellen(Nachrichtentyp.Ping, 0, 0, 0, out var nr), $"Client {i}");
            Pruefe.Gleich((uint)i, nr, "Nummer");
        }
        Pruefe.Falsch(c.VersucheStellen(Nachrichtentyp.Ping, 0, 0, 0, out _), "33. offene");
        Pruefe.Falsch(c.NaechsteAbgelaufene(long.MaxValue - 1, out _), "ohne Frist läuft nichts ab");

        var f = new FremdeAnfragen(Seite.Server);
        Pruefe.Gleich(Fehlercode.Protokoll, f.Annehmen(0x80000000), "0x80000000");
        Pruefe.Gleich(Fehlercode.Protokoll, f.Annehmen(7), "ohne Bit 31");
        Pruefe.Gleich(null, f.Annehmen(0x80000001), "0x80000001");
        Pruefe.Gleich(Fehlercode.Protokoll, f.Annehmen(0x80000001), "nicht aufsteigend");
    }

    // ---------------------------------------------------------------- P-10 Fristen

    [Test]
    public static void P10_Fristen_TlsUndHello()
    {
        var a = new ServerAutomat(1000);
        Pruefe.Falsch(a.Ticken(5999).Trennen, "TLS 4,999 s");
        Pruefe.Wahr(a.Ticken(6000).Trennen, "TLS 5 s");

        var b = new ServerAutomat(0);
        b.TlsFertig(true, true, 100);
        Pruefe.Falsch(b.Ticken(3099).Trennen, "Hello 2,999 s");
        var u = b.Ticken(3100);
        Pruefe.Wahr(u.Trennen, "Hello 3 s");
        Pruefe.Gleich(null, u.Fehler, "ohne Fehlerrahmen");

        Pruefe.Gleich(ServerZustand.Ende, new ServerAutomat(0).TlsFertig(false, true, 0).Nach, "falsches ALPN");
        Pruefe.Gleich(ServerZustand.Ende, new ServerAutomat(0).TlsFertig(true, false, 0).Nach, "kein P-256");
    }

    [Test]
    public static void P10_Fristen_Registrierung5MinSeitHello()
    {
        var k = ServerIn(ServerZustand.Registrierung, t: 0);
        uint nr = 1;
        for (long t = 2000; t < 300_000; t += 2000)
        {
            AnServer(k, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = nr }, nr: nr), t);
            k.AntwortGesendet(nr++);
            Pruefe.Falsch(k.Ticken(t, out _).Trennen, $"t = {t}");
        }
        Pruefe.Falsch(k.Ticken(299_999, out _).Trennen, "4:59,999");
        Pruefe.Wahr(k.Ticken(300_000, out _).Trennen, "5 min nach Hello");
    }

    [Test]
    public static void P10_Fristen_Warteraum15MinMitWartezeit()
    {
        var k = ServerIn(ServerZustand.Warteraum, t: 0);
        uint nr = 1;
        for (long t = 2000; t < 900_000; t += 2000)
        {
            AnServer(k, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = nr }, nr: nr), t);
            k.AntwortGesendet(nr++);
        }
        Pruefe.Falsch(k.Ticken(899_999, out _).Trennen, "14:59,999");
        var u = k.Ticken(900_000, out _);
        Pruefe.Wahr(u.Trennen, "15 min");
        Pruefe.Gleich(ByeGrund.Wartezeit, u.Bye, "Bye WARTEZEIT");
        Pruefe.Gleich(60_000u, u.WiederInMs, "wiederInMs");
    }

    [Test]
    public static void P10_Fristen_AktivSync10s_UndVoiceKeysFrist()
    {
        var k = ServerIn(ServerZustand.AktivSync, t: 0);
        AnServer(k, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 1), 5000);
        Pruefe.Falsch(k.Ticken(9999, out _).Trennen, "9,999 s");
        Pruefe.Wahr(k.Ticken(10_000, out var ab).Trennen, "10 s ohne VoiceKeys-Antwort");
        Pruefe.Gleich(Nachrichtentyp.VoiceKeys, ab!.Value.Typ, "abgelaufene Anfrage");

        // Rotation in AKTIV: ohne Antwort binnen 10 s trennt der Server.
        var k2 = ServerIn(ServerZustand.Aktiv, t: 0);
        k2.VoiceKeysStellen(1, 1000);
        AnServer(k2, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, nr: 1), 8000);
        Pruefe.Falsch(k2.Ticken(10_999, out _).Trennen, "Rotation 9,999 s");
        Pruefe.Wahr(k2.Ticken(11_000, out _).Trennen, "Rotation 10 s");
    }

    [Test]
    public static void P10_Fristen_VerknuepfungGiltAlsNein()
    {
        var k = ServerIn(ServerZustand.Registrierung, t: 0);
        var nr = k.VerknuepfungStellen(0);
        uint ping = 1;
        for (long t = 2000; t <= 120_000; t += 2000)
        {
            AnServer(k, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = ping }, nr: ping), t);
            k.AntwortGesendet(ping++);
        }
        var u = k.Ticken(120_000, out var ab);
        Pruefe.Falsch(u.Trennen, "keine Trennung");
        Pruefe.Gleich(nr, ab!.Value.Nr, "abgelaufen: gilt als nein");
        Pruefe.Gleich(ServerZustand.Registrierung, k.Automat.Zustand, "bleibt in REGISTRIERUNG");
        // Eine späte Antwort hat keine offene Anfrage mehr.
        var e = AnServer(k, Json(Nachrichtentyp.VerknuepfungBestaetigen, new VerknuepfungBestaetigenAntwort { Ja = true }, nr: nr, flags: Rahmen.FlagAntwort), 120_001);
        Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, "späte Antwort");
    }

    [Test]
    public static void P10_Fristen_Aktiv10sOhneRahmen()
    {
        var k = ServerIn(ServerZustand.Aktiv, t: 0);
        AnServer(k, Json(Nachrichtentyp.EmpfangsBericht, EmpfangsBerichtWert()), 1000);
        Pruefe.Falsch(k.Ticken(10_999, out _).Trennen, "9,999 s nach letztem Rahmen");
        Pruefe.Wahr(k.Ticken(11_000, out _).Trennen, "10 s ohne jeden Rahmen");
    }

    [Test]
    public static void P10_60sEmpfangOhneServerPush_KeineTrennung()
    {
        var k = ServerIn(ServerZustand.Aktiv, t: 0);
        var c = ClientIn(ClientZustand.AktivUdp, t: 0);
        uint nr = 1;
        for (long t = 0; t <= 60_000; t += 100)
        {
            if (c.Automat.PingFaellig(t))
            {
                c.Automat.PingGesendet(t);
                Pruefe.Gleich(EingangsStatus.Annehmen, AnServer(k, Json(Nachrichtentyp.Ping, new PingAnfrage { Nr = nr }, nr: nr), t).Status, "Ping");
                k.AntwortGesendet(nr);
                c.VersucheAnfrageStellen(Nachrichtentyp.Ping, t, out var cnr);
                AnClient(c, Json(Nachrichtentyp.Ping, new PingAntwort { Nr = nr, UdpEmpfangen = 0, UdpGesendet = 0, ServerZeit = t }, nr: cnr, flags: Rahmen.FlagAntwort), t);
                nr++;
            }
            Pruefe.Falsch(k.Ticken(t, out _).Trennen, $"Server bei {t}");
            Pruefe.Falsch(c.Automat.Ticken(t).Geaendert, $"Client bei {t}");
        }
        Pruefe.Gleich(ServerZustand.Aktiv, k.Automat.Zustand, "Server AKTIV");
        Pruefe.Gleich(ClientZustand.AktivUdp, c.Automat.Zustand, "Client AKTIV/UDP");
        Pruefe.Gleich(30u, nr - 1, "Pings in 60 s (alle 2 s ab Hello)");
    }

    // ---------------------------------------------------------------- Übergänge nach Hello

    [Test]
    public static void P10_HelloEinstufung()
    {
        (Einstufung E, ServerZustand Nach, WelcomeStatus? W, Fehlercode? F)[] faelle =
        [
            (Einstufung.Unbekannt, ServerZustand.Registrierung, WelcomeStatus.Unbekannt, null),
            (Einstufung.Entfernt, ServerZustand.Registrierung, WelcomeStatus.Unbekannt, null),
            (Einstufung.Wartet, ServerZustand.Warteraum, WelcomeStatus.Wartet, null),
            (Einstufung.Freigegeben, ServerZustand.AktivSync, WelcomeStatus.Freigegeben, null),
            (Einstufung.Abgelehnt, ServerZustand.Ende, null, Fehlercode.GeraetGesperrt),
            (Einstufung.Entzogen, ServerZustand.Ende, null, Fehlercode.GeraetGesperrt),
            (Einstufung.KontoGesperrt, ServerZustand.Ende, null, Fehlercode.KontoGesperrt),
        ];
        foreach (var (einstufung, nach, welcome, fehler) in faelle)
        {
            var k = ServerIn(ServerZustand.WarteHello);
            var u = k.Automat.HelloVerarbeitet(einstufung, false, 0);
            Pruefe.Gleich(nach, u.Nach, $"{einstufung}: Zustand");
            Pruefe.Gleich(welcome, u.Welcome, $"{einstufung}: Welcome");
            Pruefe.Gleich(fehler, u.Fehler, $"{einstufung}: Fehler");
        }
        var alt = ServerIn(ServerZustand.WarteHello).Automat.HelloVerarbeitet(Einstufung.Freigegeben, clientZuAlt: true, 0);
        Pruefe.Gleich(Fehlercode.VersionZuAlt, alt.Fehler, "VERSION_ZU_ALT statt Welcome");
        Pruefe.Gleich(null, alt.Welcome, "kein Welcome");
        Pruefe.Wahr(alt.Trennen, "Trennung");
    }

    [Test]
    public static void P10_VerdraengungNurBeimEintrittInAktivSync()
    {
        var a = ServerIn(ServerZustand.WarteHello).Automat;
        Pruefe.Falsch(a.HelloVerarbeitet(Einstufung.Unbekannt, false, 0).SyncBeginnen, "nach REGISTRIERUNG");
        Pruefe.Falsch(a.VerknuepfungBestaetigt(0).SyncBeginnen, "REGISTRIERUNG -> WARTERAUM");
        Pruefe.Wahr(a.Freigegeben(0).SyncBeginnen, "WARTERAUM -> AKTIV_SYNC");
        var b = ServerIn(ServerZustand.WarteHello).Automat;
        Pruefe.Falsch(b.HelloVerarbeitet(Einstufung.Wartet, false, 0).SyncBeginnen, "nach WARTERAUM");
        Pruefe.Falsch(b.Abgelehnt(0).SyncBeginnen, "Ablehnung");
        Pruefe.Wahr(ServerIn(ServerZustand.WarteHello).Automat.HelloVerarbeitet(Einstufung.Freigegeben, false, 0).SyncBeginnen, "Hello -> AKTIV_SYNC");
        Pruefe.Falsch(ServerIn(ServerZustand.AktivSync).Automat.VoiceKeysBeantwortet(0).SyncBeginnen, "AKTIV_SYNC -> AKTIV");
    }

    [Test]
    public static void P10_ErsetztBeiGleichemGeraet()
    {
        var buch = new Verdraengungsbuch();
        Pruefe.Gleich(new Verdraengung(Verdraengungsart.KeineAlteSitzung, false), buch.NeueSitzung(1005, false, false, 0), "keine alte");
        for (var i = 0; i < 10; i++)
        {
            Pruefe.Gleich(new Verdraengung(Verdraengungsart.Ersetzt, false), buch.NeueSitzung(1005, true, true, i * 1000), $"gleiches Gerät {i}");
        }
        Pruefe.Gleich(Verdraengungsart.Verdraengt, buch.NeueSitzung(1005, true, false, 20_000).Art, "ERSETZT zählt nicht mit");
    }

    [Test]
    public static void F13_DreimalHinUndHer_MeldungDannGesperrt()
    {
        var buch = new Verdraengungsbuch();
        var v1 = buch.NeueSitzung(1005, true, false, 0);
        var v2 = buch.NeueSitzung(1005, true, false, 60_000);
        var v3 = buch.NeueSitzung(1005, true, false, 120_000);
        Pruefe.Gleich(new Verdraengung(Verdraengungsart.Verdraengt, false), v1, "erstes Mal");
        Pruefe.Gleich(new Verdraengung(Verdraengungsart.Verdraengt, false), v2, "zweites Mal");
        Pruefe.Gleich(new Verdraengung(Verdraengungsart.Verdraengt, true), v3, "drittes Mal: VERDRAENGUNG_HAEUFIG");
        var v4 = buch.NeueSitzung(1005, true, false, 125_000);
        Pruefe.Gleich(Verdraengungsart.Gesperrt, v4.Art, "weitere: VERDRAENGEN_GESPERRT, laufende Sitzung bleibt");
        Pruefe.Wahr(Fehlercodes.Trennt(Fehlercode.VerdraengenGesperrt), "neue Verbindung wird getrennt");
        Pruefe.Gleich(Verdraengungsart.Verdraengt, buch.NeueSitzung(1004, true, false, 125_000).Art, "anderes Konto unberührt");
        Pruefe.Gleich(Verdraengungsart.Gesperrt, buch.NeueSitzung(1005, true, false, 120_000 + 30 * 60_000 - 1).Art, "29:59,999");
        Pruefe.Gleich(Verdraengungsart.Verdraengt, buch.NeueSitzung(1005, true, false, 120_000 + 30 * 60_000).Art, "nach 30 min");

        var buch2 = new Verdraengungsbuch();
        for (var i = 0; i < 3; i++) buch2.NeueSitzung(7, true, false, i);
        Pruefe.Wahr(buch2.IstGesperrt(7, 10), "gesperrt");
        buch2.AdminEntscheidet(7);
        Pruefe.Gleich(Verdraengungsart.Verdraengt, buch2.NeueSitzung(7, true, false, 20).Art, "Admin hat entschieden");

        var buch3 = new Verdraengungsbuch();
        buch3.NeueSitzung(9, true, false, 0);
        buch3.NeueSitzung(9, true, false, 1000);
        Pruefe.Falsch(buch3.NeueSitzung(9, true, false, 300_000).MeldungHaeufig, "erstes Ereignis nach 5 min aus dem Fenster");
    }

    // ---------------------------------------------------------------- SpracheTunnel (8.3 letzter Negativfall, P-10)

    [Test]
    public static void P10_SpracheTunnelMitFremderSitzung_Protokoll()
    {
        var fremd = ServerIn(ServerZustand.Aktiv);
        fremd.SitzungsId = SitzungB;
        var e = AnServer(fremd, Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, TvA));
        Pruefe.Gleich(Fehlercode.Protokoll, e.Fehler, "fremde Sitzung");
        Pruefe.Wahr(e.Trennen, "Trennung");
        Pruefe.Gleich(ServerZustand.Ende, fremd.Automat.Zustand, "ENDE");

        var eigen = ServerIn(ServerZustand.Aktiv);
        Pruefe.Gleich(SitzungA, eigen.SitzungsId, "Sitzung A");
        Pruefe.Gleich(EingangsStatus.Annehmen, AnServer(eigen, Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, TvA)).Status, "eigene Sitzung");

        // Schritte 1 und 2 von 5.9 kommen vor Schritt 3: kaputte Rahmen werden still verworfen.
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(TvA.AsSpan(0, 40), SitzungB, hoch: true), "40 Byte");
        var runterTyp = (byte[])TvA.Clone();
        runterTyp[1] = 0x11;
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(runterTyp, SitzungB, hoch: true), "Typ 0x11 hoch");
        var reserviert = (byte[])TvA.Clone();
        reserviert[3] = 1;
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(reserviert, SitzungB, hoch: true), "reserviert");
        Pruefe.Gleich(EingangsStatus.Uebergehen, AnServer(ServerIn(ServerZustand.Aktiv), Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, runterTyp)).Status, "übergangen");

        // Client: TV-B runter an Sitzung B.
        var c = ClientIn(ClientZustand.AktivTunnel);
        Pruefe.Gleich(SitzungB, c.SitzungsId, "Sitzung aus Welcome");
        Pruefe.Gleich(EingangsStatus.Annehmen, AnClient(c, Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, TvB)).Status, "TV-B an B");
        Pruefe.Gleich(TunnelPruefung.Verwerfen, SpracheTunnel.Pruefen(TvB.AsSpan(0, 44), SitzungB, hoch: false), "44 Byte runter");
        var c2 = ClientIn(ClientZustand.AktivTunnel);
        c2.SitzungsId = SitzungA;
        Pruefe.Gleich(Fehlercode.Protokoll, AnClient(c2, Rahmen.Bauen((ushort)Nachrichtentyp.SpracheTunnel, 0, 0, TvB)).Fehler, "TV-B an fremde Sitzung");
    }

    // ---------------------------------------------------------------- Bye, Fehler-Antwort auf VoiceKeys

    [Test]
    public static void P10_ByeBeendet()
    {
        foreach (var z in new[] { ServerZustand.Registrierung, ServerZustand.Warteraum, ServerZustand.AktivSync, ServerZustand.Aktiv })
        {
            var k = ServerIn(z);
            var e = AnServer(k, Json(Nachrichtentyp.Bye, new Bye { Grund = ByeGrund.Beendet }));
            Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, $"{z}: Bye angenommen");
            Pruefe.Gleich(ServerZustand.Ende, k.Automat.Zustand, $"{z}: ENDE");
            Pruefe.Wahr(k.LetzterUebergang!.Value.Trennen, "Trennung");
        }
    }

    [Test]
    public static void P10_FehlerAntwortAufVoiceKeys_Ende()
    {
        var k = ServerIn(ServerZustand.AktivSync);
        var e = AnServer(k, Json(Nachrichtentyp.Fehler, new Fehler { Code = Fehlercode.Intern }, nr: 0x80000001, flags: Rahmen.FlagAntwort));
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Fehler als Antwort ist erlaubt");
        Pruefe.Gleich(ServerZustand.Ende, k.Automat.Zustand, "ohne Schlüssel keine Sitzung");
    }

    // ---------------------------------------------------------------- Clientautomat

    [Test]
    public static void Client_Ablauf_BisAktivUndTunnel()
    {
        var c = new ClientKanal();
        Pruefe.Gleich(ClientZustand.Verbinden, c.Verbinden(0).Nach, "VERBINDEN");
        AnClient(c, Json(Nachrichtentyp.Welcome, Welcome.Unbekannt(0)));
        Pruefe.Gleich(ClientZustand.Registrierung, c.Automat.Zustand, "UNBEKANNT -> REGISTRIERUNG");
        AnClient(c, Json(Nachrichtentyp.GeraetStatus, GeraetStatusWert(GeraeteStatus.Wartet)));
        Pruefe.Gleich(ClientZustand.Warteraum, c.Automat.Zustand, "GeraetStatus WARTET -> WARTERAUM");
        AnClient(c, Json(Nachrichtentyp.GeraetStatus, GeraetStatusWert(GeraeteStatus.Freigegeben)));
        Pruefe.Gleich(ClientZustand.Warteraum, c.Automat.Zustand, "FREIGEGEBEN allein reicht nicht");
        AnClient(c, Json(Nachrichtentyp.Welcome, WelcomeFreigegeben()));
        Pruefe.Gleich(ClientZustand.Sync, c.Automat.Zustand, "Welcome -> SYNC");
        AnClient(c, Json(Nachrichtentyp.NetzListe, new NetzListe { Version = 1, Einstellungen = new NetzEinstellungen { DurchsageNurTaste = true, Selbstwahl = true }, Netze = [], Schiffe = [], Rollen = [] }));
        Pruefe.Gleich(ClientZustand.Sync, c.Automat.Zustand, "NetzListe allein reicht nicht");
        AnClient(c, Json(Nachrichtentyp.Zustand, new ZustandsVersionBody { ZustandsVersion = 1 }));
        Pruefe.Gleich(ClientZustand.AktivUdpBinden, c.Automat.Zustand, "Push Zustand -> AKTIV/UDP_BINDEN");
        Pruefe.Falsch(c.Automat.Ticken(2999).Geaendert, "2,999 s ohne Bindung");
        var t = c.Automat.Ticken(3000);
        Pruefe.Gleich(ClientZustand.AktivTunnel, t.Nach, "3 s ohne Bindung -> Tunnel");
        Pruefe.Gleich(true, t.TunnelAn, "TunnelModus an");
        Pruefe.Gleich(TunnelGrund.KeineBindung, t.TunnelGrund, "KEINE_BINDUNG");
        var z = c.Automat.TunnelProbeSauber(4000);
        Pruefe.Gleich(ClientZustand.AktivUdp, z.Nach, "10 s sauber -> UDP");
        Pruefe.Gleich(TunnelGrund.UdpZurueck, z.TunnelGrund, "UDP_ZURUECK");
        var a = c.Automat.UdpAusfall(4500);
        Pruefe.Gleich(TunnelGrund.UdpAusfall, a.TunnelGrund, "UDP_AUSFALL");
        Pruefe.Gleich(ClientZustand.AktivTunnel, a.Nach, "-> Tunnel");
    }

    [Test]
    public static void Client_PingTotNach6s()
    {
        var c = ClientIn(ClientZustand.AktivUdp, t: 0);
        Pruefe.Falsch(c.Automat.Ticken(5999).Geaendert, "5,999 s");
        Pruefe.Gleich(ClientZustand.Getrennt, c.Automat.Ticken(6000).Nach, "6 s ohne Ping-Antwort");
        Pruefe.Falsch(c.Automat.NeuversuchGesperrt, "Neuversuch erlaubt");
    }

    [Test]
    public static void Client_KeinAutomatischerNeuversuch()
    {
        foreach (var code in new[] { Fehlercode.KontoGesperrt, Fehlercode.GeraetGesperrt, Fehlercode.VersionZuAlt, Fehlercode.VerdraengenGesperrt })
        {
            var c = ClientIn(ClientZustand.Verbinden);
            AnClient(c, Json(Nachrichtentyp.Fehler, new Fehler { Code = code }));
            Pruefe.Gleich(ClientZustand.Getrennt, c.Automat.Zustand, $"{code}: getrennt");
            Pruefe.Wahr(c.Automat.NeuversuchGesperrt, $"{code}: gesperrt");
            Pruefe.Gleich(ClientZustand.Getrennt, c.Automat.Verbinden(10).Nach, $"{code}: kein Neuversuch");
            Pruefe.Gleich(ClientZustand.Verbinden, c.Automat.Verbinden(10, vomNutzer: true).Nach, $"{code}: Nutzer darf");
        }
        var v = ClientIn(ClientZustand.AktivUdp);
        AnClient(v, Json(Nachrichtentyp.Bye, new Bye { Grund = ByeGrund.Verdraengt, Durch = new ByeDurch { GeraetName = "Laptop", Anzeige = "5CD2-52FB-0CE8-9324" } }));
        Pruefe.Wahr(v.Automat.NeuversuchGesperrt, "Bye VERDRAENGT");
        // Seit 0.6.3: nach ENTFERNT (Gerät entfernt oder Konto gelöscht) kein stiller neuer Antrag, erst der Nutzer verbindet.
        foreach (var vorher in new[] { ClientZustand.AktivUdp, ClientZustand.Warteraum })
        {
            var e = ClientIn(vorher);
            AnClient(e, Json(Nachrichtentyp.Bye, new Bye { Grund = ByeGrund.Entfernt }));
            Pruefe.Wahr(e.Automat.Zustand == ClientZustand.Getrennt && e.Automat.NeuversuchGesperrt, $"Bye ENTFERNT aus {vorher}: gesperrt");
            Pruefe.Gleich(ClientZustand.Getrennt, e.Automat.Verbinden(10).Nach, $"Bye ENTFERNT aus {vorher}: kein Neuversuch");
            Pruefe.Gleich(ClientZustand.Verbinden, e.Automat.Verbinden(10, vomNutzer: true).Nach, $"Bye ENTFERNT aus {vorher}: Nutzer darf");
        }
        var w = ClientIn(ClientZustand.Warteraum);
        AnClient(w, Json(Nachrichtentyp.Bye, new Bye { Grund = ByeGrund.Wartezeit, WiederInMs = 60_000 }));
        Pruefe.Falsch(w.Automat.NeuversuchGesperrt, "Bye WARTEZEIT: verbindet weiter von selbst");
        var p = ClientIn(ClientZustand.AktivUdp);
        AnClient(p, Json(Nachrichtentyp.Fehler, new Fehler { Code = Fehlercode.Protokoll }));
        Pruefe.Gleich(ClientZustand.Getrennt, p.Automat.Zustand, "PROTOKOLL trennt");
        Pruefe.Falsch(p.Automat.NeuversuchGesperrt, "PROTOKOLL: Neuversuch erlaubt");
    }

    [Test]
    public static void Client_WartezeitUndRegistrierungsFehler()
    {
        var c = ClientIn(ClientZustand.Warteraum);
        AnClient(c, Json(Nachrichtentyp.Bye, new Bye { Grund = ByeGrund.Wartezeit, WiederInMs = 60_000 }));
        Pruefe.Gleich(ClientZustand.Getrennt, c.Automat.Zustand, "getrennt");
        Pruefe.Gleich(60_000u, c.LetzterUebergang!.Value.NeuverbindenNachMs, "nach wiederInMs");
        Pruefe.Gleich(48_000u, ClientAutomat.Streuen(60_000, 0), "-20 %");
        Pruefe.Gleich(72_000u, ClientAutomat.Streuen(60_000, 1), "+20 %");

        foreach (var code in new[] { Fehlercode.KontoGesperrt, Fehlercode.NurAdminWerkzeug, Fehlercode.GeraeteLimit })
        {
            var r = ClientIn(ClientZustand.Registrierung);
            AnClient(r, Json(Nachrichtentyp.Fehler, new Fehler { Code = code }));
            Pruefe.Gleich(ClientZustand.Registrierung, r.Automat.Zustand, $"{code} als Push in REGISTRIERUNG trennt nicht");
            Pruefe.Wahr(r.LetzterUebergang!.Value.NeuenCodeAnbieten, "neuen Code anbieten");
            Pruefe.Falsch(r.Automat.NeuversuchGesperrt, "kein Sperrvermerk");
        }
    }

    [Test]
    public static void Client_NachrichtenJeZustand()
    {
        // NetzListe gibt es erst ab SYNC, VerknuepfungBestaetigen nur in REGISTRIERUNG.
        var r = ClientIn(ClientZustand.Registrierung);
        Pruefe.Gleich(Fehlercode.Protokoll, AnClient(r, Json(Nachrichtentyp.NetzListe, new NetzListe { Version = 1, Einstellungen = new NetzEinstellungen { DurchsageNurTaste = true, Selbstwahl = true }, Netze = [], Schiffe = [], Rollen = [] })).Fehler, "NetzListe in REGISTRIERUNG");
        var r2 = ClientIn(ClientZustand.Registrierung);
        var e = AnClient(r2, Json(Nachrichtentyp.VerknuepfungBestaetigen, new VerknuepfungBestaetigenAnfrage { Konto = new VerknuepfungKonto { Name = "Test", Benutzername = "test", Neu = true } }, nr: 0x80000001));
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Rückfrage in REGISTRIERUNG");
        Pruefe.Wahr(r2.AntwortGesendet(0x80000001), "beantwortet");
        var a = ClientIn(ClientZustand.AktivUdp);
        Pruefe.Gleich(Fehlercode.Protokoll, AnClient(a, Json(Nachrichtentyp.VerknuepfungBestaetigen, new VerknuepfungBestaetigenAnfrage { Konto = new VerknuepfungKonto { Name = "Test", Benutzername = "test", Neu = true } }, nr: 0x80000001)).Fehler, "Rückfrage in AKTIV");
        var v = ClientIn(ClientZustand.Verbinden);
        Pruefe.Gleich(Fehlercode.Protokoll, AnClient(v, Json(Nachrichtentyp.Zustand, new ZustandsVersionBody { ZustandsVersion = 1 })).Fehler, "Zustand vor Welcome");
    }

    private static Hello HelloWert() => new()
    {
        Minor = 0,
        Client = new ClientAngabe { Name = "Funk", Version = "0.1.0" },
        SchluesselArt = SchluesselArt.Software,
        Opus = "1.6.1",
        Faehigkeiten = ["tunnel"],
    };

    private static EmpfangsBericht EmpfangsBerichtWert() => new()
    {
        ZeitraumMs = 5000,
        Weg = Verbindungsweg.Udp,
        Runter = new RunterZaehler { Empfangen = 250, Verloren = 1, Doppelt = 0, Umsortiert = 0 },
        JitterMs = 2.5,
        ZielVerzoegerungMs = 40,
        SpaetVerworfen = 0,
        Unterlaeufe = 0,
    };

    private static GeraetStatus GeraetStatusWert(GeraeteStatus status) => new()
    {
        GeraetId = 7,
        KontoId = 1005,
        KontoName = "E",
        Benutzername = "e",
        Status = status,
        Anzeige = "5CD2-52FB-0CE8-9324",
        Fingerabdruck = "XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=",
        SchluesselArt = SchluesselArt.Software,
        ErstesGeraet = true,
    };
}
