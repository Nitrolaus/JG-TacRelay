// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Funk.Protokoll.Identitaet;

namespace Funk.Tests;

/// <summary>
/// Ergänzungen der Gegenprüfung: P-19 als volle Rangmatrix, P-14 mit echter Signatur ohne Präfix, Randfälle
/// (Leereingaben, Längen, Überlauf der Kennungen, Wanduhrsprung, maskierte doppelte Schlüssel) und Leser-Threads.
/// </summary>
internal static class IdentitaetGegenpruefungTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    // 8.5 und 8.11, wörtlich aus der Spezifikation
    private const string GX = "6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296";
    private const string GY = "4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5";
    private const string Spki85 = "3059301306072a8648ce3d020106082a8648ce3d03010703420004" + GX + GY;
    private const string Daten811 =
        "{\"name\":\"Test\",\"profilVersion\":2,\"ausgestellt\":1790000000000,\"gueltigBis\":1805000000000,"
        + "\"hosts\":[{\"name\":\"funk.example\",\"familien\":[\"IPv4\"]}],\"tcpPort\":443,"
        + "\"pins\":[\"XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=\",\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\"]}";
    private const string Signatur811Base64 = "iuYaeRFcezj4J9nxB6qE0x641nerMRj6fyC2qE1Irf5BrzMTBR88QTc5HzFPClyAMlu0qK+zdozrNJ9wBAQgaw==";

    private static readonly DateTimeOffset VorAblauf = DateTimeOffset.FromUnixTimeMilliseconds(1800000000000);

    private static ECDsa Testschluessel() => ECDsa.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        D = Convert.FromHexString("0000000000000000000000000000000000000000000000000000000000000001"),
        Q = new ECPoint { X = Convert.FromHexString(GX), Y = Convert.FromHexString(GY) },
    });

    private static Fingerabdruck Fp(int n) => new(Enumerable.Repeat((byte)n, 32).ToArray());

    private static string Id(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>Kleine Welt: Konten und Geräte über das Kontenbuch, Freigabe über das Admin-Werkzeug.</summary>
    private sealed class Welt(Kontenstand? stand = null)
    {
        private long _verbindung = 500;

        public IdentitaetsUhr Uhr { get; } = new(Start);

        public Kontenbuch Buch => _buch ??= new Kontenbuch(Uhr, stand);

        private Kontenbuch? _buch;

        public Verknuepfungsergebnis Verknuepfen(Fingerabdruck fp, string discordId, Herkunft herkunft = Herkunft.Bot, Stufe stufe = Stufe.Funker)
        {
            var v = ++_verbindung;
            var a = Buch.VerknuepfungAnfragen(new CodeBindung(v, fp, "Rechner", SchluesselArt.Software), discordId, "n" + discordId, "Name " + discordId, herkunft, stufe);
            return a.Ok ? Buch.VerknuepfungBeantworten(v, true) : new Verknuepfungsergebnis(a.Fehler, 0, 0, null, null, []);
        }

        public Akteur Freigegeben(Fingerabdruck fp, string discordId, Stufe stufe = Stufe.Funker)
        {
            var e = Verknuepfen(fp, discordId, stufe == Stufe.Funker ? Herkunft.Bot : Herkunft.AdminWerkzeug, stufe);
            Pruefe.Wahr(e.Ok, $"Verknüpfung {discordId}: {e.Fehler}");
            Pruefe.Wahr(Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, e.GeraetId, Entscheidung.Freigeben).Ok, $"Freigabe {discordId}");
            // Admin-Sitzung mit frischer Bestätigung: geprüft werden hier die Rang- und Rechteregeln dahinter (3.9 in AdminSitzungTests).
            return Akteur.Verwaltend(e.KontoId, e.GeraetId, bestaetigt: true);
        }

        public Konto Konto(uint id) => Buch.Stand.Konten[id];
    }

    // ---------------------------------------------------------------- Geheimnisse nie im Log (3.8, P-46)

    [Test]
    public static void CodeAusstellung_ToStringVerraetDenCodeNicht()
    {
        var r = new EinmalcodeRegister(new IdentitaetsUhr(Start), IdentitaetTests.CodeQuelle("K7M4Q9TX2HWD"));
        var a = r.Ausstellen(1, Fp(1), "Rechner", SchluesselArt.Software, "p");
        Pruefe.Gleich("K7M4-Q9TX-2HWD", a.Code, "Code selbst ist lesbar");
        var text = a.ToString();
        Pruefe.Falsch(text.Contains("K7M4", StringComparison.OrdinalIgnoreCase), "ToString enthält keinen Teil des Codes: " + text);
        Pruefe.Wahr(text.Contains("verborgen", StringComparison.Ordinal), "ToString sagt, dass der Code verborgen ist");
        var einloesung = r.Einloesen("K7M4-Q9TX-2HWD", "1001");
        Pruefe.Falsch(einloesung.ToString().Contains("K7M4", StringComparison.OrdinalIgnoreCase), "auch die Einlösung nennt ihn nicht");
    }

    // ---------------------------------------------------------------- 4.8 monotone Uhr für Grenzen

    [Test]
    public static void Grenzen_MonotonTrotzWanduhrsprung()
    {
        var uhr = new IdentitaetsUhr(Start);
        var r = new EinmalcodeRegister(uhr);
        for (var i = 0; i < 5; i++) r.Einloesen("ZZZZ-ZZZZ-ZZZZ", "4001");
        uhr.WanduhrSpringt(TimeSpan.FromHours(2));
        Pruefe.Gleich(Fehlercode.Grenze, r.Einloesen("ZZZZ-ZZZZ-ZZZZ", "4001").Fehler, "Wanduhr +2 h öffnet die Grenze je Discord-ID nicht");
        for (var v = 1; v <= 10; v++) r.Ausstellen(v, Fp(1), "Rechner", SchluesselArt.Software, "p");
        uhr.WanduhrSpringt(TimeSpan.FromHours(2));
        Pruefe.Gleich(Fehlercode.Grenze, r.Ausstellen(11, Fp(1), "Rechner", SchluesselArt.Software, "p").Fehler, "und die je Präfix auch nicht");
        uhr.Vor(TimeSpan.FromHours(1));
        Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("ZZZZ-ZZZZ-ZZZZ", "4001").Fehler, "nach einer Stunde monotoner Zeit wieder offen");
        Pruefe.Wahr(r.Ausstellen(12, Fp(1), "Rechner", SchluesselArt.Software, "p").Ok, "je Präfix ebenso");

        var w = new Welt();
        for (var i = 0; i < 4; i++) w.Freigegeben(Fp(10 + i), Id(1001 + i));
        for (var i = 0; i < 3; i++) w.Buch.MitgliedschaftMelden(Id(1001 + i), Mitgliedschaftsstatus.NichtMitglied, null, null);
        w.Uhr.WanduhrSpringt(TimeSpan.FromHours(2));
        var vierte = w.Buch.MitgliedschaftMelden(Id(1004), Mitgliedschaftsstatus.NichtMitglied, null, null);
        Pruefe.Wahr(vierte.Wirkungen.OfType<Alarm>().Any() && !vierte.Wirkungen.OfType<SitzungenBeenden>().Any(),
            "Abgleich: höchstens 3 Sperren je Stunde, auch wenn die Wanduhr springt");
    }

    // ---------------------------------------------------------------- 8.6 und 3.4: Randfälle der Eingabe

    [Test]
    public static void Einmalcode_RandfaelleDerEingabe()
    {
        Span<char> n = stackalloc char[Einmalcode.Zeichen];
        Pruefe.Falsch(Einmalcode.Normalisieren("", n), "leer");
        Pruefe.Falsch(Einmalcode.Normalisieren("- - - -", n), "nur Trenner");
        Pruefe.Wahr(Einmalcode.Normalisieren(new string(' ', 52) + "K7M4Q9TX2HWD", n), "genau 64 Zeichen Eingabe");
        Pruefe.Falsch(Einmalcode.Normalisieren(new string(' ', 53) + "K7M4Q9TX2HWD", n), "65 Zeichen Eingabe");
        Pruefe.Wahr(Einmalcode.Normalisieren("K7M4 Q9TX\t2HWD", n), "geschütztes Leerzeichen und Tab sind Leerraum");
        Pruefe.Gleich("K7M4Q9TX2HWD", n.ToString(), "Normalform");
        Pruefe.Falsch(Einmalcode.Normalisieren("K7M4Q9TX2HWD\0", n), "Nullzeichen ist kein Leerraum");
        Pruefe.Falsch(Einmalcode.Normalisieren("K7M4Q9TX2HWＤ", n), "Vollbreites D ist Nicht-ASCII");
        Pruefe.Wirft<ArgumentException>(() => Einmalcode.Normalisieren("K7M4Q9TX2HWD", new char[11]), "Ziel zu kurz");
        Pruefe.Wirft<ArgumentException>(() => Einmalcode.Hash("K7M4Q9TX2HW", new byte[32]), "Hash nur über 12 Zeichen");
    }

    [Test]
    public static void Register_AbgelaufenerCodeIstFehlversuchUndFort()
    {
        var uhr = new IdentitaetsUhr(Start);
        var r = new EinmalcodeRegister(uhr, IdentitaetTests.CodeQuelle("AAAABBBBCCCC"));
        r.Ausstellen(1, Fp(1), "Rechner", SchluesselArt.Software, "p");
        uhr.Vor(Einmalcode.Gueltigkeit);
        Pruefe.Gleich(Fehlercode.CodeUngueltig, r.Einloesen("AAAA-BBBB-CCCC", "6001").Fehler, "abgelaufen");
        Pruefe.Gleich(0, r.OffeneCodes, "abgelaufener Code ist verbraucht");
        for (var i = 0; i < 4; i++) r.Einloesen("ZZZZ-ZZZZ-ZZZZ", "6001");
        Pruefe.Gleich(Fehlercode.Grenze, r.Einloesen("ZZZZ-ZZZZ-ZZZZ", "6001").Fehler, "der abgelaufene zählte als 1. von 5 Fehlversuchen");
        Pruefe.Gleich(Fehlercode.Ungueltig, r.Ausstellen(2, Fp(1), "", SchluesselArt.Software, "p").Fehler, "leerer Gerätename");
        Pruefe.Gleich(Fehlercode.Ungueltig, r.Einloesen("AAAA-BBBB-CCCC", "").Fehler, "leere Discord-ID");
        Pruefe.Gleich(Fehlercode.Ungueltig, r.Einloesen("AAAA-BBBB-CCCC", "18446744073709551616").Fehler, "Discord-ID über u64");
    }

    [Test]
    public static void Register_PraefixtabelleGedeckelt()
    {
        var uhr = new IdentitaetsUhr(Start);
        var r = new EinmalcodeRegister(uhr);
        for (var i = 0; i < EinmalcodeRegister.MaxPraefixe; i++)
            Pruefe.Wahr(r.Ausstellen(i, Fp(1), "Rechner", SchluesselArt.Software, "2001:db8:" + Id(i) + "::/64").Ok, $"Präfix {i}");
        var flut = r.Ausstellen(-1, Fp(1), "Rechner", SchluesselArt.Software, "2001:db8:ffff::/64");
        Pruefe.Gleich(Fehlercode.Grenze, flut.Fehler, "neues Präfix bei voller Tabelle");
        Pruefe.Wahr(flut.Wirkungen.OfType<Alarm>().Any(a => a.Art == "REGISTRIERUNG_FLUT"), "mit Alarm");
        Pruefe.Wahr(r.Ausstellen(-2, Fp(1), "Rechner", SchluesselArt.Software, "2001:db8:0::/64").Ok, "bekanntes Präfix unberührt");
        uhr.Vor(TimeSpan.FromHours(1));
        Pruefe.Wahr(r.Ausstellen(-3, Fp(1), "Rechner", SchluesselArt.Software, "2001:db8:ffff::/64").Ok, "nach einer Stunde aufgeräumt");
    }

    // ---------------------------------------------------------------- P-14 und 3.1: ohne Präfix, ohne Kanonisierung

    [Test]
    public static void Profil_P14_SignaturOhnePraefixMitEchtemSchluessel()
    {
        using var privat = Testschluessel();
        using var eingebaut = Profilschluessel.AusSpki(Convert.FromHexString(Spki85));
        var daten = Encoding.ASCII.GetBytes(Daten811);

        var mitPraefix = SigniertesProfil.Signieren(daten, privat);
        Pruefe.Gleich(ProfilUrteil.Uebernehmen, ProfilAnnahme.Pruefen(mitPraefix, [eingebaut], 1, VorAblauf).Urteil, "Gegenprobe: richtig signiert");

        var ohnePraefix = privat.SignData(daten, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var angebot = new SigniertesProfil(daten, ohnePraefix);
        Pruefe.Gleich(ProfilUrteil.SignaturUngueltig, ProfilAnnahme.Pruefen(angebot, [eingebaut], 1, VorAblauf).Urteil,
            "mit dem echten Schlüssel, aber ohne Präfix signiert: verworfen");
    }

    [Test]
    public static void Profil_GeprueftUeberDieEmpfangenenBytes()
    {
        using var eingebaut = Profilschluessel.AusSpki(Convert.FromHexString(Spki85));
        var signatur = Convert.FromBase64String(Signatur811Base64);
        var gleichwertig = "{ " + Daten811[1..];
        Pruefe.Wahr(Serverprofil.TryLesen(Encoding.ASCII.GetBytes(gleichwertig), out var inhalt) && inhalt!.ProfilVersion == 2, "inhaltlich gleich");
        var angebot = new SigniertesProfil(Encoding.ASCII.GetBytes(gleichwertig), signatur);
        Pruefe.Gleich(ProfilUrteil.SignaturUngueltig, ProfilAnnahme.Pruefen(angebot, [eingebaut], null, VorAblauf).Urteil,
            "ein Leerzeichen mehr: keine Kanonisierung, die Signatur gilt nicht");

        foreach (var kaputt in new[] { new byte[64], Enumerable.Repeat((byte)0xFF, 64).ToArray() })
        {
            var p = new SigniertesProfil(Encoding.ASCII.GetBytes(Daten811), kaputt);
            Pruefe.Gleich(ProfilUrteil.SignaturUngueltig, ProfilAnnahme.Pruefen(p, [eingebaut], null, VorAblauf).Urteil,
                "r und s außerhalb des Bereichs: ungültig statt Ausnahme");
        }
    }

    [Test]
    public static void Profil_UmschlagMaskierungDuplikateLaenge()
    {
        var datenB64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(Daten811));
        var maskiert = Signatur811Base64.Replace("+", "\\u002B", StringComparison.Ordinal).Replace("/", "\\/", StringComparison.Ordinal);
        var umschlag = "{\"daten\":\"" + datenB64 + "\",\"signatur\":\"" + maskiert + "\"}";
        Pruefe.Wahr(SigniertesProfil.TryLesen(Encoding.UTF8.GetBytes(umschlag), out var s), "maskierte Zeichen im Umschlag (4.3: beide Schreibweisen)");
        using (var eingebaut = Profilschluessel.AusSpki(Convert.FromHexString(Spki85)))
            Pruefe.Wahr(eingebaut.Prueft(s!), "Signatur gilt");

        static bool Umschlag(string json) => SigniertesProfil.TryLesen(Encoding.UTF8.GetBytes(json), out _);
        Pruefe.Falsch(Umschlag("{\"daten\":\"" + datenB64 + "\",\"d\\u0061ten\":\"AA==\",\"signatur\":\"" + Signatur811Base64 + "\"}"), "doppelter Schlüssel, einmal maskiert");
        Pruefe.Falsch(Umschlag("{\"daten\":\"" + datenB64 + "\",\"signatur\":null}"), "signatur null gilt als fehlend");
        Pruefe.Falsch(Umschlag("{\"daten\":\"\",\"signatur\":\"" + Signatur811Base64 + "\"}"), "leere daten");
        Pruefe.Falsch(Umschlag("{\"daten\":\"" + datenB64 + "\",\"signatur\":\"" + Signatur811Base64 + "\",\"x\":\"" + new string('a', 40_000) + "\"}"), "Umschlag über 32 KiB");
        Pruefe.Falsch(Umschlag(""), "leer");

        static bool Daten(string json) => Serverprofil.TryLesen(Encoding.UTF8.GetBytes(json), out _);
        static string Mit(string alt, string neu) => Daten811.Replace(alt, neu, StringComparison.Ordinal);
        Pruefe.Falsch(Daten(Mit("\"tcpPort\":443", "\"tcpPort\":443,\"n\\u0061me\":\"X\"")), "doppelter Schlüssel, einmal maskiert");
        Pruefe.Falsch(Daten(Mit("{\"name\":\"funk.example\"", "{\"name\":\"funk.example\",\"name\":\"funk.example\"")), "doppelter Schlüssel im Host");
        Pruefe.Falsch(Daten(Mit("\"pins\":[", "\"pins\":[null,")), "null im Pin-Feld");
        Pruefe.Falsch(Daten(Mit("[\"IPv4\"]", "[\"IPv4\",null]")), "null in den Familien");
        Pruefe.Falsch(Daten(Mit("\"profilVersion\":2", "\"profilVersion\":4294967296")), "profilVersion über u32");
        Pruefe.Falsch(Daten(Mit("\"profilVersion\":2", "\"profilVersion\":-1")), "profilVersion negativ");
        Pruefe.Falsch(Daten(Mit("\"ausgestellt\":1790000000000", "\"ausgestellt\":1.79e12")), "Exponentschreibweise");
        Pruefe.Falsch(Daten(Mit("funk.example", "funk.example.")), "Punkt am Ende");
        Pruefe.Falsch(Daten(Mit("funk.example", "-funk.example")), "Bindestrich am Labelanfang");
        Pruefe.Falsch(Daten(Mit("funk.example", new string('a', 64) + ".example")), "Label über 63");
        Pruefe.Falsch(Daten(Mit("\"tcpPort\":443", "\"tcpPort\":443,\"nameserver\":[\"a.example\",\"b.example\",\"c.example\",\"d.example\",\"e.example\"]")), "fünf Nameserver");
        Pruefe.Falsch(Daten("{}"), "leeres Objekt");
        Pruefe.Falsch(Daten(""), "leer");
    }

    // ---------------------------------------------------------------- P-19: jede Kombination aus Stufe und Ziel

    private enum Op
    {
        Sperren,
        Entsperren,
        Name,
        Loeschen,
        StufeFunker,
        StufeRaidleiter,
        StufeAdmin,
        RechtNetzeVerwalten,
    }

    /// <summary>Wie der Akteur verwaltet: ohne Admin-Sitzung, in der Admin-Sitzung ohne frischen Code, mit frischem Code.</summary>
    private enum Sitzungsart
    {
        Normal,
        OhneBestaetigung,
        Bestaetigt,
    }

    [Test]
    public static void Verwaltung_P19_Rangmatrix()
    {
        var faelle = 0;
        foreach (var akteurStufe in Enum.GetValues<Stufe>())
        foreach (var zielStufe in Enum.GetValues<Stufe>())
        foreach (var op in Enum.GetValues<Op>())
        foreach (var art in Enum.GetValues<Sitzungsart>())
        {
            var w = new Welt();
            var chef = w.Freigegeben(Fp(1), "9001", Stufe.Admin); // hält den "letzten ADMIN" aus der Matrix heraus
            var voll = w.Freigegeben(Fp(2), "8001", akteurStufe);
            // Verwaltungsrechte als Einzelrecht vergibt nur die Konsole (3.9, P10).
            if (akteurStufe != Stufe.Admin)
            {
                Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.RechteSetzen(chef, voll.KontoId, Rechteregeln.Standard(akteurStufe) | Rechte.KontenVerwalten).Fehler, "Client vergibt kein Verwaltungsrecht");
                Pruefe.Wahr(w.Buch.RechteSetzen(Akteur.AdminWerkzeug, voll.KontoId, Rechteregeln.Standard(akteurStufe) | Rechte.KontenVerwalten).Ok, "KONTEN_VERWALTEN über die Konsole");
            }
            var akteur = art switch
            {
                Sitzungsart.Normal => Akteur.Sitzung(voll.KontoId, voll.GeraetId),
                Sitzungsart.OhneBestaetigung => Akteur.Verwaltend(voll.KontoId, voll.GeraetId, bestaetigt: false),
                _ => voll,
            };
            var ziel = w.Freigegeben(Fp(3), "7001", zielStufe);
            if (op == Op.Entsperren) Pruefe.Wahr(w.Buch.KontoSperren(chef, ziel.KontoId).Ok, "vorher gesperrt");

            var a = w.Konto(akteur.KontoId);
            var z = w.Konto(ziel.KontoId);
            var vorher = w.Buch.Stand;
            var erwartet = art == Sitzungsart.Normal ? Fehlercode.AdminSitzungNoetig : Erwartet(akteurStufe, zielStufe, op, a, z);
            if (art == Sitzungsart.OhneBestaetigung && erwartet == Fehlercode.Keiner && op != Op.Name) erwartet = Fehlercode.SchrittBestaetigen;
            var ergebnis = op switch
            {
                Op.Sperren => w.Buch.KontoSperren(akteur, ziel.KontoId),
                Op.Entsperren => w.Buch.KontoEntsperren(akteur, ziel.KontoId),
                Op.Name => w.Buch.NameSetzen(akteur, ziel.KontoId, "Neu"),
                Op.Loeschen => w.Buch.KontoLoeschen(akteur, ziel.KontoId),
                Op.StufeFunker => w.Buch.StufeSetzen(akteur, ziel.KontoId, Stufe.Funker),
                Op.StufeRaidleiter => w.Buch.StufeSetzen(akteur, ziel.KontoId, Stufe.Raidleiter),
                Op.StufeAdmin => w.Buch.StufeSetzen(akteur, ziel.KontoId, Stufe.Admin),
                _ => w.Buch.RechteSetzen(akteur, ziel.KontoId, z.Rechte | Rechte.NetzeVerwalten),
            };
            var fall = $"{akteurStufe}+KONTEN_VERWALTEN ({art}) -> {zielStufe}, {op}";
            Pruefe.Gleich(erwartet, ergebnis.Fehler, fall);
            if (erwartet != Fehlercode.Keiner) Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), fall + ": P-45 ohne Seiteneffekt");

            // Regel 2 für jeden Akteur: nie die eigene Stufe, nie die eigenen Rechte. Ein ADMIN-Konto fasst ohnehin nur die Konsole an.
            var eigen = art == Sitzungsart.Normal ? Fehlercode.AdminSitzungNoetig
                : akteurStufe == Stufe.Admin ? Fehlercode.NurAdminWerkzeug : Fehlercode.KeinRecht;
            Pruefe.Gleich(eigen, w.Buch.StufeSetzen(akteur, akteur.KontoId, akteurStufe).Fehler, fall + ": eigene Stufe");
            Pruefe.Gleich(eigen, w.Buch.RechteSetzen(akteur, akteur.KontoId, a.Rechte).Fehler, fall + ": eigene Rechte");
            faelle++;
        }
        Pruefe.Gleich(3 * 3 * 8 * 3, faelle, "alle Kombinationen durchlaufen");
    }

    /// <summary>
    /// Erwartung in der Admin-Sitzung mit frischem Code: 6.10 Regel 3 (ADMIN nur durch ADMIN), Regel 1 (kein Recht, das man
    /// nicht hat), nie über die eigene Stufe; dazu 3.9 und P10: ADMIN-Konten im Client nur sperren, Stufe ADMIN und
    /// Verwaltungsrechte als Einzelrecht nur an der Konsole.
    /// </summary>
    private static Fehlercode Erwartet(Stufe akteurStufe, Stufe zielStufe, Op op, Konto a, Konto z)
    {
        if (zielStufe == Stufe.Admin && akteurStufe != Stufe.Admin) return Fehlercode.KeinRecht;
        var zielAdmin = zielStufe == Stufe.Admin;
        switch (op)
        {
            case Op.Sperren:
                return Fehlercode.Keiner;
            case Op.Entsperren or Op.Name or Op.Loeschen:
                return zielAdmin ? Fehlercode.NurAdminWerkzeug : Fehlercode.Keiner;
            case Op.RechtNetzeVerwalten:
                if (zielAdmin) return Fehlercode.NurAdminWerkzeug;
                return ((z.Rechte | a.Rechte) & Rechte.NetzeVerwalten) == 0 ? Fehlercode.KeinRecht : Fehlercode.NurAdminWerkzeug;
            default:
                var neu = op switch { Op.StufeFunker => Stufe.Funker, Op.StufeRaidleiter => Stufe.Raidleiter, _ => Stufe.Admin };
                if (zielAdmin) return Fehlercode.NurAdminWerkzeug;
                if (neu > akteurStufe) return Fehlercode.KeinRecht;
                if (neu == Stufe.Admin) return Fehlercode.NurAdminWerkzeug;
                return (Rechteregeln.Standard(neu) & ~z.Rechte & ~a.Rechte) != 0 ? Fehlercode.KeinRecht : Fehlercode.Keiner;
        }
    }

    [Test]
    public static void Verwaltung_LetzterAdminNurUeberWerkzeug()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var zweiter = w.Freigegeben(Fp(2), "9002", Stufe.Admin);
        Pruefe.Wahr(w.Buch.KontoSperren(admin, zweiter.KontoId).Ok, "zwei ADMIN: einer sperrt den anderen");
        var vorher = w.Buch.Stand;
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.KontoSperren(admin, admin.KontoId).Fehler, "jetzt ist er der letzte aktive");
        Pruefe.Gleich(Fehlercode.NurAdminWerkzeug, w.Buch.KontoLoeschen(admin, admin.KontoId).Fehler, "löschen ebenso");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "ohne Seiteneffekt");
        Pruefe.Wahr(w.Buch.StufeSetzen(Akteur.AdminWerkzeug, admin.KontoId, Stufe.Raidleiter).Ok, "Admin-Werkzeug stuft auch den letzten herab");
    }

    // ---------------------------------------------------------------- Überlauf und ungültige Aufzählungen

    [Test]
    public static void Kennungen_UeberlaufErgibtGrenze()
    {
        var voll = new Welt(Kontenstand.Leer with { NaechsteGeraetId = uint.MaxValue });
        var vorher = voll.Buch.Stand;
        Pruefe.Gleich(Fehlercode.Grenze, voll.Verknuepfen(Fp(1), "1001").Fehler, "Geräte-ID am Ende von u32");
        Pruefe.Wahr(ReferenceEquals(vorher, voll.Buch.Stand), "nichts angelegt");

        var konten = new Welt(Kontenstand.Leer with { NaechsteKontoId = (uint)int.MaxValue + 1 });
        Pruefe.Gleich(Fehlercode.Grenze, konten.Verknuepfen(Fp(1), "1001").Fehler, "Konto-ID über 2^31-1 (3.3)");
        Pruefe.Gleich(Fehlercode.Grenze, konten.Buch.KontoAnlegen(Akteur.AdminWerkzeug, "1002", "x", "X", Stufe.Funker).Fehler, "auch im Admin-Werkzeug");

        var knapp = new Welt(Kontenstand.Leer with { NaechsteGeraetId = uint.MaxValue - 1 });
        var letztes = knapp.Verknuepfen(Fp(1), "1001");
        Pruefe.Wahr(letztes.Ok && letztes.GeraetId == uint.MaxValue - 1, "letzte gültige Geräte-ID");
        Pruefe.Gleich(Fehlercode.Grenze, knapp.Verknuepfen(Fp(2), "1002").Fehler, "danach Schluss statt Umlauf auf 0");
    }

    [Test]
    public static void Aufzaehlungen_UngueltigeWerteAbgelehnt()
    {
        var w = new Welt();
        var bindung = new CodeBindung(1, Fp(1), "Rechner", SchluesselArt.Software);
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.VerknuepfungAnfragen(bindung, "1001", "n", null, (Herkunft)7).Fehler,
            "unbekannte Herkunft gilt nicht als Admin-Werkzeug");
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.VerknuepfungAnfragen(bindung, "1001", "n", null, Herkunft.AdminWerkzeug, (Stufe)9).Fehler,
            "unbekannte Stufe");
        var e = w.Verknuepfen(Fp(2), "1002");
        var vorher = w.Buch.Stand;
        Pruefe.Gleich(Fehlercode.Ungueltig, w.Buch.GeraetEntscheiden(Akteur.AdminWerkzeug, e.GeraetId, (Entscheidung)9).Fehler, "unbekannte Entscheidung");
        Pruefe.Wahr(ReferenceEquals(vorher, w.Buch.Stand), "ohne Seiteneffekt");
    }

    // ---------------------------------------------------------------- 3.6: Löschen heißt vollständig entfernen (seit 0.7.0)

    [Test]
    public static void Loeschung_NichtsBleibtGesperrt()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var abgelehnt = w.Verknuepfen(Fp(2), "1001");
        Pruefe.Wahr(w.Buch.GeraetEntscheiden(admin, abgelehnt.GeraetId, Entscheidung.Ablehnen).Ok, "abgelehnt");
        var entfernt = w.Freigegeben(Fp(3), "1002");
        Pruefe.Wahr(w.Buch.GeraetEntfernen(entfernt, entfernt.GeraetId, false, "").Ok, "selbst entfernt");

        w.Uhr.Vor(Kontenbuch.Loeschfrist);
        var geloescht = w.Buch.FristenAnwenden().OfType<KontoGeloescht>().Select(k => k.KontoId).ToHashSet();
        Pruefe.Wahr(geloescht.SetEquals([abgelehnt.KontoId, entfernt.KontoId]), "beide Konten ohne freigegebenes Gerät nach 30 Tagen gelöscht");

        // Bis 0.6.x blieb der Fingerabdruck des abgelehnten Geräts danach für immer gesperrt. Seit 0.7.0 (Sarevoc 04.10.2026: Löschen heißt
        // vollständig entfernen, gesperrt wird nur bewusst) sperren weder Ablehnen noch Löschen.
        Pruefe.Wahr(w.Buch.Stand.GesperrteFingerabdruecke.IsEmpty, "nichts auf der Sperrliste");
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(Fp(2)).Art, "ABGELEHNT ist nach der Löschung unbekannt");
        Pruefe.Wahr(w.Verknuepfen(Fp(2), "1003").Ok, "und darf wieder in den Warteraum");
        Pruefe.Gleich(EinstufungsArt.Unbekannt, w.Buch.Einstufen(Fp(3)).Art, "ENTFERNT ist keine Sperre");
        Pruefe.Wahr(w.Verknuepfen(Fp(3), "1004").Ok, "ENTFERNT darf sich neu registrieren");
    }

    // ---------------------------------------------------------------- 6.2: Leser sehen immer einen stimmigen Stand

    [Test]
    public static void Kontenbuch_LeserSehenStimmigeSchnappschuesse()
    {
        var w = new Welt();
        var admin = w.Freigegeben(Fp(1), "9001", Stufe.Admin);
        var fertig = 0;
        var fehler = new List<string>();
        var sperre = new object();

        void Lesen()
        {
            long letzteVersion = -1;
            while (Volatile.Read(ref fertig) == 0)
            {
                var s = w.Buch.Stand;
                string? befund = null;
                if (s.Version < letzteVersion) befund = "Version rückwärts";
                foreach (var (fp, id) in s.NachFingerabdruck)
                    if (!s.Geraete.TryGetValue(id, out var g) || g.Fingerabdruck != fp) befund = "Fingerabdruck-Index zeigt ins Leere";
                foreach (var g in s.Geraete.Values)
                    if (!s.Konten.ContainsKey(g.KontoId)) befund = "Gerät ohne Konto";
                foreach (var (discord, id) in s.NachDiscordId)
                    if (!s.Konten.TryGetValue(id, out var k) || k.DiscordId != discord) befund = "Discord-Index zeigt ins Leere";
                if (s.NachDiscordId.Count != s.Konten.Count) befund = "Discord-Index unvollständig";
                if (befund is not null)
                {
                    lock (sperre) fehler.Add(befund);
                    return;
                }
                letzteVersion = s.Version;
            }
        }

        var leser = Enumerable.Range(0, 2).Select(_ => new Thread(Lesen) { IsBackground = true }).ToList();
        leser.ForEach(t => t.Start());
        for (var i = 0; i < 300; i++)
        {
            var e = w.Verknuepfen(Fp(10 + i % 200), Id(20000 + i));
            if (!e.Ok) continue;
            if (i % 3 == 0) w.Buch.GeraetEntscheiden(admin, e.GeraetId, Entscheidung.Freigeben);
            if (i % 5 == 0) w.Buch.KontoLoeschen(admin, e.KontoId);
        }
        Volatile.Write(ref fertig, 1);
        leser.ForEach(t => t.Join());
        Pruefe.Gleich(0, fehler.Count, "Befunde der Leser: " + string.Join("; ", fehler));
    }
}
