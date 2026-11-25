// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Funk.Protokoll.Steuerung;
using Funk.Server.Betrieb;
using Funk.Server.Netzzugang;
using I = Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Eingebaute Sperrliste (A51): Angriffe als Einheitstests mit fester Uhr (Handshake-Flut, Antragsflut, Protokollmüll,
/// gleiche IP wie ein Mitglied, IPv6-/64 und Sammelsperre /48, Ablauf und steigende Dauer, Speichergrenze, Neustart ohne
/// Klartext, fail2ban-Zeile) und am laufenden Testserver über echte TCP-Verbindungen. Der TLS-Handshake kommt dabei nie
/// zustande (verbinden und schließen, ohne ClientHello), unter Windows legt also nichts einen Schlüssel ins Profil.
/// </summary>
internal static class SperrlisteTests
{
    private const long T0 = 1_790_000_000_000;
    private const long Sekunde = 1000;
    private const long Minute = 60 * Sekunde;
    private const long Stunde = 60 * Minute;

    private static readonly byte[] Schluessel = RandomNumberGenerator.GetBytes(32);

    private static Sperrliste Neu(out Vorrangspur spur, SperrlistenKonfiguration? k = null, List<Sperrmeldung>? meldungen = null,
        byte[]? schluessel = null, IEnumerable<IPNetwork>? ausnahmen = null)
    {
        spur = new Vorrangspur(RandomNumberGenerator.GetBytes(32));
        var k2 = k ?? new SperrlistenKonfiguration();
        var s = new Sperrliste(k2, schluessel ?? Schluessel, Schwaerzung.Zufaellig(), spur, ausnahmen ?? k2.AusnahmeNetze());
        if (meldungen is not null) s.Gesperrt = meldungen.Add;
        return s;
    }

    private static Adresse A(string ip) => Adresse.Aus(IPAddress.Parse(ip), 40000);

    private static void Punkte(Sperrliste s, Adresse a, Sperranlass anlass, int mal, long jetzt)
    {
        for (var i = 0; i < mal; i++) s.Vermerken(a, anlass, jetzt);
    }

    [Test]
    public static void Sperrliste_HandshakeFlut_SperrtNachSchwelle_AndereBleibenFrei()
    {
        var meldungen = new List<Sperrmeldung>();
        var s = Neu(out _, meldungen: meldungen);
        var angreifer = A("203.0.113.7");
        Punkte(s, angreifer, Sperranlass.Handshake, 9, T0);
        Pruefe.Gleich(Sperrurteil.Frei, s.Pruefen(angreifer, T0), "9 gescheiterte Handshakes: noch frei");
        s.Vermerken(angreifer, Sperranlass.Handshake, T0);
        Pruefe.Gleich(Sperrurteil.Gesperrt, s.Pruefen(angreifer, T0 + Sekunde), "der zehnte sperrt");
        Pruefe.Wahr(s.IstGesperrt(A("203.0.113.7"), T0 + Sekunde), "auch mit anderem Quellport");
        Pruefe.Gleich(Sperrurteil.Frei, s.Pruefen(A("203.0.113.8"), T0), "die Nachbaradresse bleibt frei (IPv4 je Adresse)");
        Pruefe.Gleich(1, meldungen.Count, "eine Meldung je Sperre");
        var m = meldungen[0];
        Pruefe.Gleich(1, m.Stufe, "Stufe 1");
        Pruefe.Gleich(5 * Minute, m.DauerMs, "erste Sperre 5 min");
        Pruefe.Gleich(Sperranlass.Handshake, m.Anlass, "Anlass");
        Pruefe.Wahr(m.Kennung.StartsWith("p4#", StringComparison.Ordinal) && !m.Kennung.Contains("203.0", StringComparison.Ordinal), "Kennung als HMAC wie im Logbuch: " + m.Kennung);
        Pruefe.Gleich(1, s.Gesperrte(T0 + Sekunde).Count, "eine Sperre in der Liste");
        // Weitere Verstöße während der Sperre verlängern sie nicht und melden nichts Neues.
        Punkte(s, angreifer, Sperranlass.Handshake, 50, T0 + 2 * Sekunde);
        Pruefe.Gleich(1, meldungen.Count, "keine zweite Meldung während der Sperre");
        Pruefe.Gleich(T0 + 5 * Minute, s.Gesperrte(T0 + 3 * Sekunde)[0].BisMs, "Ende unverändert");
    }

    [Test]
    public static void Sperrliste_PunkteJeAnlass_AntragsflutUndProtokollmuell()
    {
        Pruefe.Gleich(1, Sperrliste.Punkte(Sperranlass.Handshake), "Handshake");
        Pruefe.Gleich(1, Sperrliste.Punkte(Sperranlass.HandshakeFlut), "Handshake-Flut");
        Pruefe.Gleich(2, Sperrliste.Punkte(Sperranlass.OhneGeraetezertifikat), "ohne Gerätezertifikat");
        Pruefe.Gleich(3, Sperrliste.Punkte(Sperranlass.Protokollfehler), "Protokollfehler");
        Pruefe.Gleich(3, Sperrliste.Punkte(Sperranlass.Antragsflut), "Antragsflut");
        Pruefe.Gleich(3, Sperrliste.Punkte(Sperranlass.Registrierungsflut), "Registrierungsflut");
        Pruefe.Gleich(5, Sperrliste.Punkte(Sperranlass.Ratengrenze), "Ratengrenze");

        var s = Neu(out _);
        var antrag = A("198.51.100.30");
        Punkte(s, antrag, Sperranlass.Antragsflut, 3, T0);
        Pruefe.Falsch(s.IstGesperrt(antrag, T0), "drei abgewiesene Anträge (9 Punkte): frei");
        s.Vermerken(antrag, Sperranlass.Antragsflut, T0);
        Pruefe.Wahr(s.IstGesperrt(antrag, T0), "der vierte abgewiesene Antrag sperrt");
        var muell = A("198.51.100.31");
        Punkte(s, muell, Sperranlass.Protokollfehler, 4, T0);
        Pruefe.Wahr(s.IstGesperrt(muell, T0), "viermal Protokollmüll vor der Anmeldung sperrt");
        var bremse = A("198.51.100.32");
        Punkte(s, bremse, Sperranlass.Ratengrenze, 2, T0);
        Pruefe.Wahr(s.IstGesperrt(bremse, T0), "zweimal die Ratengrenze gesprengt sperrt");
    }

    [Test]
    public static void Sperrliste_PunkteVerfallenNachDemFenster()
    {
        var s = Neu(out _);
        var a = A("203.0.113.20");
        Punkte(s, a, Sperranlass.Handshake, 9, T0);
        Punkte(s, a, Sperranlass.Handshake, 9, T0 + 10 * Minute + Sekunde);
        Pruefe.Falsch(s.IstGesperrt(a, T0 + 10 * Minute + Sekunde), "9 + 9 Punkte in zwei Fenstern: frei");
        s.Vermerken(a, Sperranlass.Handshake, T0 + 11 * Minute);
        Pruefe.Wahr(s.IstGesperrt(a, T0 + 11 * Minute), "10 im neuen Fenster sperren");
    }

    [Test]
    public static void Sperrliste_AblaufUndSteigendeDauerMitObergrenze()
    {
        var meldungen = new List<Sperrmeldung>();
        var s = Neu(out _, meldungen: meldungen);
        var a = A("203.0.113.40");
        var t = T0;
        long[] erwartet = [5 * Minute, 30 * Minute, 3 * Stunde, 18 * Stunde, 24 * Stunde, 24 * Stunde];
        for (var stufe = 1; stufe <= erwartet.Length; stufe++)
        {
            Punkte(s, a, Sperranlass.Handshake, 10, t);
            var m = meldungen[^1];
            Pruefe.Gleich(stufe, m.Stufe, "Stufe " + stufe);
            Pruefe.Gleich(erwartet[stufe - 1], m.DauerMs, "Dauer in Stufe " + stufe);
            Pruefe.Wahr(s.IstGesperrt(a, t + m.DauerMs - Sekunde), "kurz vor dem Ende noch gesperrt");
            Pruefe.Falsch(s.IstGesperrt(a, t + m.DauerMs), "mit dem Ende frei");
            Pruefe.Gleich(Sperrurteil.Frei, s.Pruefen(a, t + m.DauerMs), "Pruefen nach dem Ende");
            t += m.DauerMs + Minute;
        }
        // 48 h nach dem Ende der letzten Sperre beginnt die Adresse wieder bei 5 min.
        t += 48 * Stunde;
        Punkte(s, a, Sperranlass.Handshake, 10, t);
        Pruefe.Gleich(1, meldungen[^1].Stufe, "Stufe vergessen");
        Pruefe.Gleich(5 * Minute, meldungen[^1].DauerMs, "wieder 5 min");
    }

    [Test]
    public static void Sperrliste_DurchlassAlle30Sekunden()
    {
        var s = Neu(out _);
        var a = A("203.0.113.50");
        Punkte(s, a, Sperranlass.Handshake, 10, T0);
        Pruefe.Gleich(Sperrurteil.Gesperrt, s.Pruefen(a, T0 + Sekunde), "gleich nach der Sperre");
        Pruefe.Gleich(Sperrurteil.Gesperrt, s.Pruefen(a, T0 + 29 * Sekunde), "vor 30 s kein Durchlass");
        Pruefe.Gleich(Sperrurteil.Durchlass, s.Pruefen(a, T0 + 30 * Sekunde), "nach 30 s eine Verbindung bis zum Handshake");
        Pruefe.Gleich(Sperrurteil.Gesperrt, s.Pruefen(a, T0 + 31 * Sekunde), "nur eine");
        Pruefe.Gleich(Sperrurteil.Durchlass, s.Pruefen(a, T0 + 61 * Sekunde), "die nächste nach weiteren 30 s");
        Pruefe.Gleich(3L, s.Gesperrte(T0 + 62 * Sekunde)[0].Abgewiesen, "abgewiesene Verbindungen gezählt");
        Pruefe.Wahr(s.IstGesperrt(a, T0 + 62 * Sekunde), "IstGesperrt verbraucht keinen Durchlass und hebt nichts auf");
    }

    [Test]
    public static void Sperrliste_GleicheIpWieEinMitglied_NieGesperrt()
    {
        var s = Neu(out var spur);
        var familie = A("198.51.100.77");
        spur.Eintragen(familie.Praefix, T0 - Stunde);
        Punkte(s, familie, Sperranlass.Handshake, 100, T0);
        Punkte(s, familie, Sperranlass.Antragsflut, 10, T0);
        Pruefe.Gleich(Sperrurteil.Frei, s.Pruefen(familie, T0), "Präfix der Vorrangspur (Familie, gleiche öffentliche IP)");
        Pruefe.Gleich(0, s.Beobachtet, "nicht einmal verfolgt");

        // CGNAT: Die Adresse ist schon gesperrt, als sich ein freigegebenes Gerät dahinter meldet (über den Durchlass).
        var cgnat = A("100.64.0.9");
        Punkte(s, cgnat, Sperranlass.Handshake, 10, T0);
        Pruefe.Wahr(s.IstGesperrt(cgnat, T0), "gesperrt");
        Pruefe.Wahr(s.Entlasten(cgnat, T0 + Minute), "freigegebenes Gerät hebt die Sperre auf");
        Pruefe.Gleich(Sperrurteil.Frei, s.Pruefen(cgnat, T0 + Minute), "frei");
        Pruefe.Falsch(s.Entlasten(cgnat, T0 + Minute), "zweimal: nichts mehr aufzuheben");
        var meldungen = new List<Sperrmeldung>();
        s.Gesperrt = meldungen.Add;
        Punkte(s, cgnat, Sperranlass.Handshake, 10, T0 + 2 * Minute);
        Pruefe.Gleich(1, meldungen[^1].Stufe, "nach dem Entlasten beginnt die Stufe neu");
    }

    [Test]
    public static void Sperrliste_Ipv6_Je64_UndSammelsperreFuer48()
    {
        var meldungen = new List<Sperrmeldung>();
        var s = Neu(out var spur, meldungen: meldungen);
        Punkte(s, A("2001:db8:1:2::1"), Sperranlass.Handshake, 10, T0);
        Pruefe.Wahr(s.IstGesperrt(A("2001:db8:1:2:ffff::9"), T0), "dasselbe /64, andere Adresse");
        Pruefe.Falsch(s.IstGesperrt(A("2001:db8:1:3::1"), T0), "anderes /64");
        Pruefe.Wahr(meldungen[0].Kennung.StartsWith("p6#", StringComparison.Ordinal), "Kennung p6: " + meldungen[0].Kennung);

        // Das Mitglied im selben /48 steht in der Vorrangspur.
        var mitglied = A("2001:db8:1:77::5");
        spur.Eintragen(mitglied.Praefix, T0);
        for (var i = 3; i <= 5; i++) Punkte(s, A($"2001:db8:1:{i}::1"), Sperranlass.Handshake, 10, T0);
        Pruefe.Wahr(meldungen.Exists(m => m.Gruppe), "vier gesperrte /64 im selben /48: Sammelsperre");
        var gruppe = meldungen.First(m => m.Gruppe);
        Pruefe.Wahr(gruppe.Kennung.StartsWith("g6#", StringComparison.Ordinal), "Kennung g6: " + gruppe.Kennung);
        Pruefe.Wahr(s.IstGesperrt(A("2001:db8:1:99::1"), T0 + Sekunde), "neues /64 im /48 ist mitgesperrt");
        Pruefe.Gleich(Sperrurteil.Frei, s.Pruefen(mitglied, T0 + Sekunde), "das Mitglied in der Vorrangspur nicht");
        Pruefe.Falsch(s.IstGesperrt(A("2001:db8:2::1"), T0 + Sekunde), "anderes /48");
        Pruefe.Gleich(Sperrurteil.Durchlass, s.Pruefen(A("2001:db8:1:99::1"), T0 + 31 * Sekunde), "Durchlass auch für die Sammelsperre");
        Pruefe.Wahr(s.Entlasten(A("2001:db8:1:99::1"), T0 + 32 * Sekunde), "ein freigegebenes Gerät im /48 hebt die Sammelsperre auf");
        Pruefe.Falsch(s.IstGesperrt(A("2001:db8:1:99::2"), T0 + 32 * Sekunde), "/48 wieder frei");
        Pruefe.Wahr(s.IstGesperrt(A("2001:db8:1:3::1"), T0 + 32 * Sekunde), "die einzeln gesperrten /64 bleiben gesperrt");

        var aus = Neu(out _, new SperrlistenKonfiguration { V6Sammelschwelle = 0 });
        for (var i = 2; i <= 9; i++) Punkte(aus, A($"2001:db8:1:{i}::1"), Sperranlass.Handshake, 10, T0);
        Pruefe.Falsch(aus.IstGesperrt(A("2001:db8:1:99::1"), T0), "v6Sammelschwelle 0: keine Sammelsperre");
    }

    /// <summary>
    /// Gegenprüfung: Ein Angreifer mit einem /48 wechselt je Verbindung sein /64. Kein /64 erreicht so je die Schwelle, und die
    /// Sammelsperre (vier gesperrte /64) griff nie. Jetzt zählen die Punkte aller /64 auch für das /48, Schwelle mal
    /// v6Sammelschwelle (40).
    /// </summary>
    [Test]
    public static void Sperrliste_Ipv6_WechselndeJe64_SperrenDas48()
    {
        var meldungen = new List<Sperrmeldung>();
        var s = Neu(out var spur, meldungen: meldungen);
        var mitglied = A("2001:db8:7:ff::5");
        spur.Eintragen(mitglied.Praefix, T0);
        for (var i = 0; i < 39; i++)
            s.Vermerken(A("2001:db8:7:" + i.ToString("x", CultureInfo.InvariantCulture) + "::1"), Sperranlass.Handshake, T0 + i * Sekunde);
        Pruefe.Falsch(s.IstGesperrt(A("2001:db8:7:abcd::1"), T0 + Minute), "39 Punkte über 39 /64 eines /48: noch frei");
        Pruefe.Gleich(0, meldungen.Count, "keine Sperre bis hier");
        s.Vermerken(A("2001:db8:7:27::1"), Sperranlass.Handshake, T0 + Minute);
        Pruefe.Wahr(s.IstGesperrt(A("2001:db8:7:abcd::1"), T0 + Minute), "40 Punkte über wechselnde /64 sperren das ganze /48");
        Pruefe.Wahr(meldungen.Count == 1 && meldungen[0].Gruppe && meldungen[0].Kennung.StartsWith("g6#", StringComparison.Ordinal),
            "eine Meldung als Sammelsperre: " + string.Join(", ", meldungen.Select(m => m.Kennung)));
        Pruefe.Gleich(Sperrurteil.Frei, s.Pruefen(mitglied, T0 + Minute), "das Mitglied im selben /48 bleibt frei (Vorrangspur)");
        Pruefe.Falsch(s.IstGesperrt(A("2001:db8:8::1"), T0 + Minute), "ein anderes /48 bleibt frei");

        // Punkte aus dem Mitglieds-/64 zählen nie, auch nicht für das /48.
        var still = Neu(out var spur2);
        spur2.Eintragen(mitglied.Praefix, T0);
        Punkte(still, mitglied, Sperranlass.Handshake, 100, T0);
        Pruefe.Falsch(still.IstGesperrt(A("2001:db8:7:abcd::1"), T0), "Vorrangspur zählt nicht für das /48");

        // Mit v6Sammelschwelle 0 gibt es auch diese Sammelsperre nicht.
        var aus = Neu(out _, new SperrlistenKonfiguration { V6Sammelschwelle = 0 });
        for (var i = 0; i < 100; i++)
            aus.Vermerken(A("2001:db8:7:" + i.ToString("x", CultureInfo.InvariantCulture) + "::1"), Sperranlass.Handshake, T0);
        Pruefe.Falsch(aus.IstGesperrt(A("2001:db8:7:abcd::1"), T0), "v6Sammelschwelle 0: keine Sammelsperre");
    }

    /// <summary>
    /// Gegenprüfung: Die Liste rechnet mit der Wanduhr (Sperren überstehen den Neustart). Springt sie zurück (NTP-Schritt, Uhr
    /// beim Start falsch), gab es bis zum Einholen keinen Durchlass, die Sperre dauerte um den Sprung länger, und das Fenster
    /// lief nie ab. Jetzt gilt eine Sperre höchstens ihre Dauer ab dem Rücksprung, der Durchlass bleibt, das Fenster beginnt neu.
    /// </summary>
    [Test]
    public static void Sperrliste_UhrSpringtZurueck_DurchlassUndDauerBleiben()
    {
        var s = Neu(out _);
        var a = A("203.0.113.60");
        Punkte(s, a, Sperranlass.Handshake, 10, T0);
        var zurueck = T0 - Stunde;
        Pruefe.Gleich(Sperrurteil.Durchlass, s.Pruefen(a, zurueck), "eine Stunde zurück: der Durchlass bleibt");
        Pruefe.Gleich(Sperrurteil.Gesperrt, s.Pruefen(a, zurueck + Sekunde), "aber nur einer je 30 s");
        Pruefe.Wahr(s.IstGesperrt(a, zurueck + 5 * Minute - Sekunde), "die Sperre läuft weiter");
        Pruefe.Falsch(s.IstGesperrt(a, zurueck + 5 * Minute), "höchstens ihre Dauer (5 min) ab dem Rücksprung, nicht Sprung plus Dauer");

        var b = A("203.0.113.61");
        Punkte(s, b, Sperranlass.Handshake, 9, T0);
        Punkte(s, b, Sperranlass.Handshake, 9, zurueck);
        Pruefe.Falsch(s.IstGesperrt(b, zurueck), "Punkte von vor dem Rücksprung zählen nicht bis zum Einholen der Uhr weiter");
        s.Vermerken(b, Sperranlass.Handshake, zurueck + Sekunde);
        Pruefe.Wahr(s.IstGesperrt(b, zurueck + Sekunde), "im neuen Fenster sperren 10 Punkte wie immer");
    }

    [Test]
    public static void Sperrliste_AusnahmenUndAbgeschaltet()
    {
        var k = new SperrlistenKonfiguration { Ausnahmen = ["192.168.1.0/24", "2001:db8:ff::/48", "198.51.100.9"] };
        var s = Neu(out _, k);
        foreach (var ip in new[] { "192.168.1.20", "2001:db8:ff:1::1", "198.51.100.9" })
        {
            Punkte(s, A(ip), Sperranlass.Handshake, 50, T0);
            Pruefe.Gleich(Sperrurteil.Frei, s.Pruefen(A(ip), T0), "Ausnahme " + ip);
        }
        Punkte(s, A("198.51.100.10"), Sperranlass.Handshake, 10, T0);
        Pruefe.Wahr(s.IstGesperrt(A("198.51.100.10"), T0), "keine Ausnahme");
        var aus = Neu(out _, new SperrlistenKonfiguration { Aktiv = false });
        Punkte(aus, A("203.0.113.1"), Sperranlass.Ratengrenze, 50, T0);
        Pruefe.Gleich(Sperrurteil.Frei, aus.Pruefen(A("203.0.113.1"), T0), "aktiv = false");
    }

    [Test]
    public static void Sperrliste_SpeicherBegrenzt_SperrenBleiben()
    {
        var s = Neu(out _, new SperrlistenKonfiguration { MaxEintraege = 100 });
        for (var i = 1; i <= 5; i++) Punkte(s, A("203.0.113." + i.ToString(CultureInfo.InvariantCulture)), Sperranlass.Handshake, 10, T0);
        for (var i = 0; i < 5000; i++)
        {
            var ip = new IPAddress([(byte)(10 + i / 65536), (byte)(i / 256 % 256), (byte)(i % 256), 1]);
            s.Vermerken(Adresse.Aus(ip, 1), Sperranlass.Handshake, T0 + i);
        }
        Pruefe.Wahr(s.Beobachtet <= 100, "höchstens 100 Adressen verfolgt: " + s.Beobachtet);
        for (var i = 1; i <= 5; i++) Pruefe.Wahr(s.IstGesperrt(A("203.0.113." + i.ToString(CultureInfo.InvariantCulture)), T0 + 6000), "Sperre " + i + " überlebt die Flut");
        s.Aufraeumen(T0 + 49 * Stunde);
        Pruefe.Gleich(0, s.Beobachtet, "nach Ablauf und Vergessen leer");
    }

    [Test]
    public static void Sperrliste_NeustartOhneKlartext()
    {
        var s = Neu(out _);
        Punkte(s, A("203.0.113.7"), Sperranlass.Handshake, 10, T0);
        Punkte(s, A("2001:db8:5:6::1"), Sperranlass.Antragsflut, 4, T0);
        Pruefe.Wahr(s.Geaendert, "geändert");
        var json = s.AlsJson();
        Pruefe.Falsch(s.Geaendert, "nach dem Speichern nicht mehr geändert");
        var text = Encoding.UTF8.GetString(json);
        Pruefe.Falsch(text.Contains("203.0.113", StringComparison.Ordinal) || text.Contains("2001:db8", StringComparison.OrdinalIgnoreCase)
            || text.Contains("cb00:71", StringComparison.OrdinalIgnoreCase), "keine Adresse im Klartext: " + text);

        var wieder = Neu(out _);
        wieder.AusJsonLaden(json, T0 + 10 * Sekunde);
        Pruefe.Wahr(wieder.IstGesperrt(A("203.0.113.7"), T0 + 10 * Sekunde), "IPv4-Sperre übersteht den Neustart");
        Pruefe.Wahr(wieder.IstGesperrt(A("2001:db8:5:6::99"), T0 + 10 * Sekunde), "IPv6-Sperre übersteht den Neustart");
        Pruefe.Gleich(2, wieder.Gesperrte(T0 + 10 * Sekunde).Count, "zwei Sperren");

        var fremd = Neu(out _, schluessel: RandomNumberGenerator.GetBytes(32));
        fremd.AusJsonLaden(json, T0 + 10 * Sekunde);
        Pruefe.Falsch(fremd.IstGesperrt(A("203.0.113.7"), T0 + 10 * Sekunde), "mit anderem Betriebsgeheimnis passt nichts");

        var spaet = Neu(out _);
        spaet.AusJsonLaden(json, T0 + 60 * Stunde);
        Pruefe.Gleich(0, spaet.Beobachtet, "abgelaufen und vergessen: nicht mehr geladen");

        // Eine Sperre bis weit in die Zukunft (verstellte Uhr, bearbeitete Datei) gilt höchstens die Höchstdauer.
        var manipuliert = Encoding.UTF8.GetBytes(Regex.Replace(text, @"""bis"":\s*\d+", "\"bis\": 99999999999999", RegexOptions.None, TimeSpan.FromSeconds(2)));
        var gekappt = Neu(out _);
        gekappt.AusJsonLaden(manipuliert, T0);
        Pruefe.Falsch(gekappt.IstGesperrt(A("203.0.113.7"), T0 + 24 * Stunde + Sekunde), "höchstens 24 h");
    }

    [Test]
    public static void Sperrliste_AufhebenNachKennungIpUndAlle()
    {
        var s = Neu(out _);
        Punkte(s, A("203.0.113.7"), Sperranlass.Handshake, 10, T0);
        Punkte(s, A("203.0.113.8"), Sperranlass.Handshake, 10, T0);
        for (var i = 2; i <= 5; i++) Punkte(s, A($"2001:db8:1:{i}::1"), Sperranlass.Handshake, 10, T0);
        var kennung = s.Gesperrte(T0).First(e => e.Kennung.StartsWith("p4#", StringComparison.Ordinal)).Kennung;
        Pruefe.Wahr(s.Aufheben(kennung), "nach Kennung");
        Pruefe.Falsch(s.Aufheben(kennung), "zweimal geht nicht");
        Pruefe.Gleich(1, s.AufhebenFuer(A("203.0.113.8")) + s.AufhebenFuer(A("203.0.113.7")), "nach Adresse, nur die noch gesperrte");
        Pruefe.Wahr(s.IstGesperrt(A("2001:db8:1:99::1"), T0), "Sammelsperre steht");
        Pruefe.Gleich(2, s.AufhebenFuer(A("2001:db8:1:2::7")), "IPv6: das /64 und das /48 darüber");
        Pruefe.Falsch(s.IstGesperrt(A("2001:db8:1:99::1"), T0), "/48 frei");
        Pruefe.Gleich(3, s.AlleAufheben(), "die übrigen drei /64");
        Pruefe.Gleich(0, s.Gesperrte(T0).Count, "leer");
    }

    [Test]
    public static void Sperrliste_KlartextzeileFesteFormUndFail2banFilter()
    {
        var m4 = new Sperrmeldung(A("203.0.113.7"), "p4#0123456789", false, 2, 30 * Minute, Sperranlass.Antragsflut, 12);
        var zeit = new DateTimeOffset(2026, 10, 1, 9, 30, 5, TimeSpan.Zero);
        Pruefe.Gleich("2026-10-01T09:30:05Z FUNK-SPERRE ip=203.0.113.7 netz=203.0.113.7/32 dauer=1800 stufe=2 anlass=ANTRAGSFLUT kennung=p4#0123456789",
            Sperrliste.Klartextzeile(m4, zeit), "IPv4");
        var m6 = new Sperrmeldung(A("2001:db8:1:2::5"), "g6#abcdefabcd", true, 1, 5 * Minute, Sperranlass.Handshake, 10);
        Pruefe.Gleich("2026-10-01T09:30:05Z FUNK-SPERRE ip=2001:db8:1:2::5 netz=2001:db8:1::/48 dauer=300 stufe=1 anlass=HANDSHAKE kennung=g6#abcdefabcd",
            Sperrliste.Klartextzeile(m6, zeit), "IPv6, Sammelsperre");
        var m64 = m6 with { Gruppe = false, Kennung = "p6#abcdefabcd" };
        Pruefe.Wahr(Sperrliste.Klartextzeile(m64, zeit).Contains("netz=2001:db8:1:2::/64", StringComparison.Ordinal), "IPv6 /64");

        // Der Filter aus konfig\fail2ban findet die Adresse. <HOST> so, wie fail2ban (failregex.py, R_HOST) es ersetzt:
        // IPv4 mit optionalem ::ffff:, IPv6 mit optionalen Klammern, sonst ein Rechnername. Der Zeitstempel vorn (ISO 8601 mit Z)
        // ist eine der Vorlagen des Datumserkenners; fail2ban nimmt ihn aus der Zeile, bevor failregex sucht.
        var filter = File.ReadAllText(Path.Combine(Wurzel(), "konfig", "fail2ban", "filter.d", "tacrelay.conf"));
        var regel = Regex.Match(filter, @"^failregex\s*=\s*(.+)$", RegexOptions.Multiline, TimeSpan.FromSeconds(2)).Groups[1].Value.Trim();
        Pruefe.Wahr(regel.Contains("<HOST>", StringComparison.Ordinal), "failregex mit <HOST>: " + regel);
        const string host = @"(?:(?:::f{4,6}:)?(?<ip4>(?:\d{1,3}\.){3}\d{1,3})|\[?(?<ip6>(?:[0-9a-fA-F]{1,4}::?|:){1,7}(?:[0-9a-fA-F]{1,4}|(?<=:):))\]?|(?<dns>[\w\-.^_]*\w))";
        var muster = new Regex(regel.Replace("<HOST>", host, StringComparison.Ordinal), RegexOptions.None, TimeSpan.FromSeconds(2));
        var datum = new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:[.,]\d+)?(?:\s*(?:Z|UTC|GMT|[+-][01]\d(?::?\d{2})?))?", RegexOptions.None, TimeSpan.FromSeconds(2));
        foreach (var (m, ip, gruppe) in new[] { (m4, "203.0.113.7", "ip4"), (m6, "2001:db8:1:2::5", "ip6"), (m64, "2001:db8:1:2::5", "ip6") })
        {
            var zeile = Sperrliste.Klartextzeile(m, zeit);
            var d = datum.Match(zeile);
            Pruefe.Wahr(d.Success && d.Value.EndsWith('Z'), "Zeitstempel vorn, UTC mit Z: " + zeile);
            var treffer = muster.Match(zeile[d.Length..]);
            Pruefe.Wahr(treffer.Success, "Filter passt: " + zeile);
            Pruefe.Gleich(ip, treffer.Groups[gruppe].Value, "Adresse aus der Zeile (" + gruppe + ")");
        }
        var jail = File.ReadAllText(Path.Combine(Wurzel(), "konfig", "fail2ban", "jail.d", "tacrelay.conf"));
        Pruefe.Wahr(Regex.IsMatch(jail, @"^enabled\s*=\s*false\s*$", RegexOptions.Multiline, TimeSpan.FromSeconds(2)), "Beispiel-Jail ist aus, bis Sarevoc es einschaltet");
        Pruefe.Wahr(jail.Contains("8443", StringComparison.Ordinal) && jail.Contains("47443", StringComparison.Ordinal), "Ports des Funks");
        Pruefe.Wahr(jail.Contains("FORWARD", StringComparison.Ordinal), "bannt in der FORWARD-Kette (Verkehr zum Container läuft durch den Host)");
        // iptables-allports sperrt nur das Protokoll aus "protocol", und fail2ban setzt dafür tcp voraus (jail.conf, [DEFAULT]).
        // Ohne "protocol = all" käme UDP 47443 der gesperrten Adresse weiter zum Container, anders als der Kommentar sagt.
        Pruefe.Wahr(Regex.IsMatch(jail, @"^banaction\s*=\s*iptables-allports\s*$", RegexOptions.Multiline, TimeSpan.FromSeconds(2))
            && Regex.IsMatch(jail, @"^protocol\s*=\s*all\s*$", RegexOptions.Multiline, TimeSpan.FromSeconds(2)), "allports mit protocol = all (TCP und UDP)");
    }

    [Test]
    public static void Sperrliste_KonfigurationVoreinstellungUndPruefen()
    {
        var k = new SperrlistenKonfiguration();
        Pruefe.Wahr(k.Aktiv, "an");
        Pruefe.Gleich(null, k.KlartextDatei, "Klartextzeile aus");
        var gut = new ServerKonfiguration { Binden = ["127.0.0.1"], Profil = "p.json" };
        Pruefe.Gleich(0, gut.Pruefen().Count, "Voreinstellungen prüfen ohne Fehler");
        var schlecht = new ServerKonfiguration
        {
            Binden = ["127.0.0.1"], Profil = "p.json",
            Sperrliste = new SperrlistenKonfiguration { Schwelle = 0, DurchlassSekunden = 0, V6Sammelschwelle = 1, Ausnahmen = ["192.168.1.0/33", "heimnetz"] },
        };
        var fehler = string.Join("\n", schlecht.Pruefen());
        foreach (var teil in new[] { "sperrliste.schwelle", "sperrliste.durchlassSekunden", "sperrliste.v6Sammelschwelle", "'192.168.1.0/33'", "'heimnetz'" })
            Pruefe.Wahr(fehler.Contains(teil, StringComparison.Ordinal), "pruefen meldet " + teil + ": " + fehler);
        var beispiel = ServerKonfiguration.Laden(Path.Combine(Wurzel(), "konfig", "server.beispiel.json"));
        Pruefe.Wahr(beispiel.Sperrliste.Aktiv && beispiel.Sperrliste.KlartextDatei is null, "Beispiel: Sperrliste an, Klartextzeile aus");
    }

    /// <summary>
    /// Befund der Prüfung vom 01.10.2026 (mittel): Das Beispiel-Jail machte aus jeder Funksperre eine Sperre am Host für 1 h,
    /// steigend bis 1 Woche, auf allen Ports (auch Foundry) und ohne den Durchlass, mit dem der Server Mitglieder hinter
    /// derselben Adresse alle 30 s bis zum Handshake lässt; <c>entsperren --ip</c> hebt sie nicht auf. Jetzt sperrt das Jail
    /// höchstens so lange wie die erste Sperre des Servers und steigert nicht; länger sperrt nur der Server selbst.
    /// </summary>
    [Test]
    public static void Sperrliste_Fail2banJail_NichtLaengerAlsDieErsteSperreDesServers()
    {
        var jail = File.ReadAllText(Path.Combine(Wurzel(), "konfig", "fail2ban", "jail.d", "tacrelay.conf"));
        var frist = TimeSpan.FromSeconds(2);
        var bantime = Regex.Match(jail, @"^bantime\s*=\s*(\d+)\s*([smhdw]?)\s*$", RegexOptions.Multiline, frist);
        Pruefe.Wahr(bantime.Success, "bantime als Zahl mit Einheit: " + jail);
        var faktor = bantime.Groups[2].Value switch
        {
            "m" => 60L,
            "h" => 3600L,
            "d" => 86_400L,
            "w" => 604_800L,
            _ => 1L,
        };
        var sekunden = long.Parse(bantime.Groups[1].Value, CultureInfo.InvariantCulture) * faktor;
        var erste = new SperrlistenKonfiguration().ErsteSperreSekunden;
        Pruefe.Wahr(sekunden <= erste, $"bantime {sekunden} s, die erste Sperre des Servers {erste} s");
        Pruefe.Falsch(Regex.IsMatch(jail, @"^bantime\.increment\s*=\s*true", RegexOptions.Multiline, frist), "keine steigende Sperre am Host");
        Pruefe.Falsch(Regex.IsMatch(jail, @"^bantime\.maxtime", RegexOptions.Multiline, frist), "keine Höchstdauer, es steigt ja nichts");
    }

    /// <summary>
    /// Befund der Prüfung vom 01.10.2026 (mittel): Die Klartextdatei entstand erst mit der ersten Sperre. fail2ban startet mit
    /// einem eingeschalteten Jail, dessen Datei fehlt, überhaupt nicht (mit allen Jails), und eine fehlende Schreibberechtigung
    /// (Unit ohne LogsDirectory) zeigte sich erst bei der ersten Sperre. Jetzt legt funk-server die Datei beim Start an, ohne
    /// etwas abzuschneiden, und meldet gleich beim Start, wenn das nicht geht.
    /// </summary>
    [Test]
    public static async Task Sperrliste_Server_KlartextdateiBeimStartAngelegt()
    {
        var ordner = Path.Combine(Testserver.Wurzel(), "klartext-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(ordner);
        try
        {
            var neu = Path.Combine(ordner, "sperren.log");
            await using (var t = await Testserver.StartenAsync(sperrliste: new SperrlistenKonfiguration { KlartextDatei = neu }))
            {
                Pruefe.Wahr(File.Exists(neu), "Datei beim Start angelegt, vor der ersten Sperre");
                Pruefe.Gleich(0L, new FileInfo(neu).Length, "leer");
                Pruefe.Falsch(t.Logbuch.Any(z => z.Contains("nicht schreibbar", StringComparison.Ordinal)), "keine Warnung");
            }

            var alt = Path.Combine(ordner, "alt.log");
            const string zeile = "2026-10-01T09:30:05Z FUNK-SPERRE ip=203.0.113.7 netz=203.0.113.7/32 dauer=300 stufe=1 anlass=HANDSHAKE kennung=p4#0a1b2c3d4e\n";
            File.WriteAllText(alt, zeile);
            await using (var t = await Testserver.StartenAsync(sperrliste: new SperrlistenKonfiguration { KlartextDatei = alt }))
                Pruefe.Gleich(zeile, File.ReadAllText(alt), "vorhandene Zeilen bleiben (anhängen, nie abschneiden)");

            var unmoeglich = Path.Combine(ordner, "fehlt", "sperren.log");
            await using (var t = await Testserver.StartenAsync(sperrliste: new SperrlistenKonfiguration { KlartextDatei = unmoeglich }))
            {
                Pruefe.Falsch(File.Exists(unmoeglich), "ohne Verzeichnis keine Datei");
                Pruefe.Wahr(t.Logbuch.Any(z => z.Contains("WARNUNG Sperrliste: Klartextdatei nicht schreibbar", StringComparison.Ordinal)),
                    "die Warnung kommt beim Start, nicht erst bei der ersten Sperre");
            }
        }
        finally
        {
            Directory.Delete(ordner, recursive: true);
        }
    }

    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new PruefungException("Repository nicht gefunden");
    }

    // ---------------------------------------------------------------- am laufenden Testserver

    /// <summary>TCP-Verbindung von einer eigenen Loopback-Adresse zur TLS-Annahme, ohne Handshake.</summary>
    private static async Task<Socket> RohVerbindenAsync(Testserver t, string lokal)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        s.Bind(new IPEndPoint(IPAddress.Parse(lokal), 0));
        await s.ConnectAsync(t.Dienst.TcpEndpunkte[0]);
        return s;
    }

    /// <summary>
    /// Ob der Server die Verbindung von sich aus schließt, ohne dass der Client etwas sendet. Eine gesperrte Adresse schließt
    /// er gleich nach accept; sonst wartet er bis zur Handshake-Frist (5 s) auf das ClientHello.
    /// </summary>
    private static async Task<bool> ServerSchliesstAsync(Socket s, TimeSpan frist)
    {
        var puffer = new byte[16];
        try
        {
            using var abbruch = new CancellationTokenSource(frist);
            return await s.ReceiveAsync(puffer, SocketFlags.None, abbruch.Token) == 0;
        }
        catch (SocketException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task WartenBisAsync(Func<bool> bedingung, TimeSpan frist, string was)
    {
        var bis = DateTime.UtcNow + frist;
        while (!bedingung() && DateTime.UtcNow < bis) await Task.Delay(25);
        Pruefe.Wahr(bedingung(), was);
    }

    [Test]
    public static async Task Sperrliste_Server_HandshakeFlutUeberTcp_GesperrtVorTls_UndKonsole()
    {
        await using var t = await Testserver.StartenAsync();
        var sperrliste = t.Dienst.Zentrale.Sperrliste;
        var angreifer = Adresse.Aus(IPAddress.Parse("127.0.0.2"), 0);
        // Scanner: verbinden und sofort wieder schließen, ohne ClientHello.
        for (var i = 0; i < 14; i++)
        {
            using var s = await RohVerbindenAsync(t, "127.0.0.2");
            await Task.Delay(10);
        }
        await WartenBisAsync(() => sperrliste.IstGesperrt(angreifer, Zeit.UnixMs()), TimeSpan.FromSeconds(10), "Handshake-Flut sperrt 127.0.0.2");
        Pruefe.Wahr(t.Logbuch.Any(z => z.Contains("WARNUNG", StringComparison.Ordinal) && z.Contains("Sperrliste", StringComparison.Ordinal)
            && z.Contains("p4#", StringComparison.Ordinal)), "Warnung im Logbuch mit Kennung");
        Pruefe.Falsch(t.Logbuch.Any(z => z.Contains("127.0.0.2", StringComparison.Ordinal)), "keine Klartext-IP im Logbuch");

        using (var gesperrt = await RohVerbindenAsync(t, "127.0.0.2"))
            Pruefe.Wahr(await ServerSchliesstAsync(gesperrt, TimeSpan.FromSeconds(3)), "gesperrte Adresse: geschlossen gleich nach accept, vor TLS");
        using (var frei = await RohVerbindenAsync(t, "127.0.0.3"))
            Pruefe.Falsch(await ServerSchliesstAsync(frei, TimeSpan.FromSeconds(1.5)), "andere Adresse: Server wartet auf das ClientHello");

        var liste = await t.AdminOk("sperrliste");
        Pruefe.Wahr(liste.Text.Contains("p4#", StringComparison.Ordinal) && liste.Text.Contains("HANDSHAKE", StringComparison.Ordinal), "sperrliste zeigt die Sperre: " + liste.Text);
        Pruefe.Falsch(liste.Text.Contains("127.0.0.2", StringComparison.Ordinal), "ohne Klartext-IP");
        var kennung = Regex.Match(liste.Text, @"p4#[0-9a-f]{10}", RegexOptions.None, TimeSpan.FromSeconds(2)).Value;
        var nichts = await t.Admin("entsperren", new JsonObject { ["eintrag"] = "p4#0000000000" });
        Pruefe.Falsch(nichts.Ok, "unbekannte Kennung: " + nichts.Text);
        var weg = await t.AdminOk("entsperren", new JsonObject { ["eintrag"] = kennung });
        Pruefe.Wahr(weg.Text.Contains(kennung, StringComparison.Ordinal), "entsperren nennt die Kennung: " + weg.Text);
        Pruefe.Falsch(sperrliste.IstGesperrt(angreifer, Zeit.UnixMs()), "aufgehoben");
        var journal = await t.AdminOk("journal");
        Pruefe.Wahr(journal.Text.Contains("SPERRE_AUFGEHOBEN", StringComparison.Ordinal), "im Journal vermerkt: " + journal.Text);
        var leer = await t.AdminOk("sperrliste");
        Pruefe.Wahr(leer.Text.StartsWith("Keine Sperren", StringComparison.Ordinal), "leer: " + leer.Text);
    }

    [Test]
    public static async Task Sperrliste_Server_EntsperrenNachIp()
    {
        await using var t = await Testserver.StartenAsync();
        var sperrliste = t.Dienst.Zentrale.Sperrliste;
        var a = Adresse.Aus(IPAddress.Parse("127.0.0.9"), 0);
        for (var i = 0; i < 10; i++) sperrliste.Vermerken(a, Sperranlass.Handshake, Zeit.UnixMs());
        Pruefe.Wahr(sperrliste.IstGesperrt(a, Zeit.UnixMs()), "gesperrt");
        var falsch = await t.Admin("entsperren", new JsonObject { ["ip"] = "127.0.0.300" });
        Pruefe.Falsch(falsch.Ok, "keine Adresse: " + falsch.Text);
        var ok = await t.AdminOk("entsperren", new JsonObject { ["ip"] = "127.0.0.9" });
        Pruefe.Falsch(ok.Text.Contains("127.0.0.9", StringComparison.Ordinal), "Antwort ohne Klartext-IP: " + ok.Text);
        Pruefe.Falsch(sperrliste.IstGesperrt(a, Zeit.UnixMs()), "nach Adresse aufgehoben");
        for (var i = 0; i < 10; i++) sperrliste.Vermerken(Adresse.Aus(IPAddress.Parse("127.0.0.10"), 0), Sperranlass.Handshake, Zeit.UnixMs());
        var alle = await t.AdminOk("entsperren", new JsonObject { ["alle"] = true });
        Pruefe.Wahr(alle.Text.Contains('1', StringComparison.Ordinal), "alle: " + alle.Text);
        Pruefe.Gleich(0, sperrliste.Gesperrte(Zeit.UnixMs()).Count, "leer");
    }

    [Test]
    public static async Task Sperrliste_Server_MitgliedHinterGleicherIp_NieGesperrt()
    {
        await using var t = await Testserver.StartenAsync();
        var lokal = IPAddress.Parse("127.0.0.4");
        await using var mitglied = await t.AktivAsync("4040", "Familie", lokal: lokal);
        var a = Adresse.Aus(lokal, 0);
        await WartenBisAsync(() => t.Dienst.Zentrale.Spur.Enthaelt(a.Praefix, Zeit.UnixMs()), TimeSpan.FromSeconds(10), "Präfix des Mitglieds in der Vorrangspur");
        // Ein anderer Rechner hinter derselben öffentlichen Adresse spielt Scanner.
        for (var i = 0; i < 20; i++)
        {
            using var s = await RohVerbindenAsync(t, "127.0.0.4");
            await Task.Delay(10);
        }
        await Task.Delay(1000);
        Pruefe.Falsch(t.Dienst.Zentrale.Sperrliste.IstGesperrt(a, Zeit.UnixMs()), "Adresse des Mitglieds wird nie gesperrt");
        Pruefe.Gleich(0, t.Dienst.Zentrale.Sperrliste.Beobachtet, "nicht einmal Punkte gesammelt");
        Pruefe.Falsch(mitglied.Getrennt, "das Mitglied bleibt verbunden");
        using var neu = await RohVerbindenAsync(t, "127.0.0.4");
        Pruefe.Falsch(await ServerSchliesstAsync(neu, TimeSpan.FromSeconds(1.5)), "neue Verbindung des Mitglieds kommt bis zum Handshake");
    }

    [Test]
    public static async Task Sperrliste_Server_RegistrierungsflutUndProtokollmuell()
    {
        await using var t = await Testserver.StartenAsync();
        var sperrliste = t.Dienst.Zentrale.Sperrliste;

        // Registrierungsflut: Codes über der Grenze je Verbindung (3) bringen je 3 Punkte.
        var flut = IPAddress.Parse("127.0.0.7");
        await using (var f = await t.VerbindenAsync(null, flut))
        {
            await f.HelloAsync();
            var grenze = 0;
            for (var i = 0; i < 7; i++)
            {
                var a = await f.AnfrageAsync(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = "PC" });
                if (a.Typ == Nachrichtentyp.Fehler && a.Json<Fehler>().Code == Fehlercode.Grenze) grenze++;
            }
            Pruefe.Gleich(4, grenze, "vier Mal GRENZE");
        }
        Pruefe.Wahr(sperrliste.IstGesperrt(Adresse.Aus(flut, 0), Zeit.UnixMs()), "Registrierungsflut sperrt");

        // Protokollmüll vor der Anmeldung: Ping vor Hello trennt mit PROTOKOLL, je 3 Punkte.
        var muell = IPAddress.Parse("127.0.0.8");
        for (var i = 0; i < 4; i++)
        {
            await using var f = await t.VerbindenAsync(null, muell);
            await f.SendenAsync(Nachrichtentyp.Ping, new PingAnfrage { Nr = 1 }, f.NaechsteNr());
            await f.ServerLauf.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Pruefe.Wahr(sperrliste.IstGesperrt(Adresse.Aus(muell, 0), Zeit.UnixMs()), "Protokollmüll vor der Anmeldung sperrt");
        Pruefe.Gleich(Sperranlass.Protokollfehler, sperrliste.Gesperrte(Zeit.UnixMs()).Single(e => e.Anlass != Sperranlass.Registrierungsflut).Anlass, "Anlass");
    }

    /// <summary>
    /// Gegenprüfung: Die Zähler je Präfix für Codes und Anträge sind begrenzt (4096 je Stunde, gegen rotierende Präfixe). Ist
    /// die Tabelle voll, antwortet der Server jedem neuen Präfix GRENZE. Das ist eine globale Grenze, kein Verstoß dieser
    /// Adresse; bis 0.3.0 (Strang) bekam ein neues Mitglied dafür trotzdem 3 Punkte je Versuch und war nach dem vierten gesperrt.
    /// </summary>
    [Test]
    public static async Task Sperrliste_Server_VolleCodetabelle_IstKeinVerstossDerAdresse()
    {
        await using var t = await Testserver.StartenAsync();
        var zentrale = t.Dienst.Zentrale;
        var sperrliste = zentrale.Sperrliste;
        // Ein Angreifer mit vielen Präfixen (etwa die /64 eines /48) hat die Tabelle gefüllt, je Präfix ein Code.
        await zentrale.Ausfuehren(() =>
        {
            var fp = I.Fingerabdruck.AusSpki([0x30, 0x01, 0x02]);
            for (var i = 0; i < I.EinmalcodeRegister.MaxPraefixe; i++)
                zentrale.Codes.Ausstellen(-1 - i, fp, "Flut", I.SchluesselArt.Software, "flut-" + i.ToString(CultureInfo.InvariantCulture));
            return 0;
        });

        var neu = IPAddress.Parse("127.0.0.21");
        await using (var f = await t.VerbindenAsync(null, neu))
        {
            await f.HelloAsync();
            for (var i = 0; i < 4; i++)
            {
                var a = await f.AnfrageAsync(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = "PC" });
                Pruefe.Wahr(a.Typ == Nachrichtentyp.Fehler && a.Json<Fehler>().Code == Fehlercode.Grenze, "volle Tabelle: Registrierungscode mit GRENZE abgewiesen");
            }
        }
        var antrag = IPAddress.Parse("127.0.0.22");
        await using (var f = await t.VerbindenAsync(null, antrag))
        {
            await f.HelloAsync();
            for (var i = 0; i < 4; i++)
            {
                var a = await f.AnfrageAsync(Nachrichtentyp.Registrierung, new RegistrierungAnfrage { Geraetename = "PC", ScName = "Neuling", DiscordName = "Neuling/Tom" });
                Pruefe.Wahr(a.Typ == Nachrichtentyp.Fehler && a.Json<Fehler>().Code == Fehlercode.Grenze, "volle Tabelle: Antrag mit GRENZE abgewiesen");
            }
        }
        Pruefe.Falsch(sperrliste.IstGesperrt(Adresse.Aus(neu, 0), Zeit.UnixMs()), "Registrierung bei voller Tabelle sperrt das neue Mitglied nicht");
        Pruefe.Falsch(sperrliste.IstGesperrt(Adresse.Aus(antrag, 0), Zeit.UnixMs()), "Antrag bei voller Tabelle sperrt das neue Mitglied nicht");
        Pruefe.Gleich(0, sperrliste.Beobachtet, "nicht einmal Punkte");

        // Die Grenzen der Adresse selbst zählen weiter: 3 je Verbindung, danach GRENZE mit Punkten (wie bisher).
        Pruefe.Wahr(I.EinmalcodeRegister.GrenzeDerQuelle(new I.Ergebnis(I.Fehlercode.Grenze, [])), "Grenze je Verbindung oder Präfix ist ein Verstoß der Quelle");
        Pruefe.Falsch(I.EinmalcodeRegister.GrenzeDerQuelle(new I.Ergebnis(I.Fehlercode.Grenze, [new I.Alarm(I.EinmalcodeRegister.AlarmTabelleVoll)])), "volle Tabelle nicht");
        Pruefe.Falsch(I.EinmalcodeRegister.GrenzeDerQuelle(new I.Ergebnis(I.Fehlercode.Keiner, [])), "kein Fehler, kein Verstoß");
    }

    [Test]
    public static async Task Sperrliste_Server_FreigegebenesGeraetHebtSperreAuf()
    {
        await using var t = await Testserver.StartenAsync();
        var lokal = IPAddress.Parse("127.0.0.11");
        var a = Adresse.Aus(lokal, 0);
        await using var erst = await t.AktivAsync("1111", "Unterwegs", lokal: lokal);
        var schluessel = erst.Schluessel.ExportParameters(true);
        // Das Mitglied steht jetzt hinter einer Adresse, die nicht in der Vorrangspur steht (CGNAT) und schon gesperrt ist.
        await WartenBisAsync(() => t.Dienst.Zentrale.Spur.Enthaelt(a.Praefix, Zeit.UnixMs()), TimeSpan.FromSeconds(10), "erst in der Vorrangspur");
        await t.AdminOk("vorrangspur-leeren");
        var sperrliste = t.Dienst.Zentrale.Sperrliste;
        for (var i = 0; i < 10; i++) sperrliste.Vermerken(a, Sperranlass.Handshake, Zeit.UnixMs());
        Pruefe.Gleich(1, sperrliste.Gesperrte(Zeit.UnixMs()).Count, "gesperrt");
        // Ein unbekanntes Gerät, das über den Durchlass kommt, hebt nichts auf.
        await using (var fremd = await t.VerbindenAsync(null, lokal))
            await fremd.HelloAsync();
        Pruefe.Gleich(1, sperrliste.Gesperrte(Zeit.UnixMs()).Count, "unbekanntes Gerät: Sperre bleibt");
        // Das freigegebene Gerät (über den Durchlass bis nach dem Handshake; ohne TLS hier direkt dorthin) hebt sie auf.
        await using var zweit = await t.VerbindenAsync(ECDsa.Create(schluessel), lokal);
        await WartenBisAsync(() => sperrliste.Gesperrte(Zeit.UnixMs()).Count == 0, TimeSpan.FromSeconds(5), "freigegebenes Gerät hebt die Sperre auf");
    }
}
