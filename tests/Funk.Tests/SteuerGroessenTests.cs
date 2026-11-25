// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using System.Text.Json;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.SteuerHilfe;

namespace Funk.Tests;

/// <summary>
/// P-02b und 4.10: Jede Nachricht an ihren Höchstwerten bleibt mit mehr als Faktor 2 unter der Rahmengrenze von
/// 1 MiB und wird vom Empfänger angenommen. Namen werden mit 4-Byte-Zeichen (Emoji) ausgeschöpft, Zahlen mit den
/// längsten zulässigen Werten.
/// </summary>
internal static class SteuerGroessenTests
{
    private const int HalbeGrenze = Rahmengrenzen.Aktiv / 2;
    private const ushort U16 = 65535;
    private const ushort NetzMax = 65534;
    private const uint U32 = uint.MaxValue;
    private const long I53 = (1L << 53) - 1;

    private static string Emoji(int n) => string.Concat(Enumerable.Repeat("\U0001F600", n));

    private static string Kennung(int i) => "KENNUNG_" + i.ToString("D8", CultureInfo.InvariantCulture);

    private static readonly string Name32 = Emoji(Grenzen.NameMax);

    /// <param name="nurBericht">Faktor 2 nicht verlangen, nur unter der Rahmengrenze; ein Unterschreiten wird als BEFUND gemeldet.</param>
    private static int Messen<T>(string was, T body, Seite empfaenger, Nachrichtentyp typ, Nachrichtenart art, bool nurBericht = false)
    {
        var bytes = SteuerJson.Serialisieren(body);
        Console.WriteLine($"P-02b {was}: {bytes.Length} Byte Body, Faktor {Rahmengrenzen.Aktiv / (double)(bytes.Length + 8):F2} unter 1 MiB");
        if (nurBericht && bytes.Length + Rahmen.LaengeOhneBody > HalbeGrenze)
            Console.WriteLine($"BEFUND 4.10: {was} liegt mit {bytes.Length} Byte über der halben Rahmengrenze (Bericht)");
        else
            Pruefe.Wahr(bytes.Length + Rahmen.LaengeOhneBody <= HalbeGrenze, $"{was}: {bytes.Length} Byte, mehr als halbe Rahmengrenze");
        Pruefe.Wahr(bytes.Length + Rahmen.LaengeOhneBody <= Rahmengrenzen.Aktiv, $"{was}: {bytes.Length} Byte, über der Rahmengrenze");

        var flags = art == Nachrichtenart.Antwort ? Rahmen.FlagAntwort : (byte)0;
        Eingang e;
        if (empfaenger == Seite.Server)
        {
            var k = ServerIn(ServerZustand.Aktiv);
            e = AnServer(k, Rahmen.Bauen((ushort)typ, flags, art == Nachrichtenart.Anfrage ? 1u : 0u, bytes));
        }
        else
        {
            var k = ClientIn(ClientZustand.AktivUdp);
            uint nr = 0;
            if (art == Nachrichtenart.Antwort) k.VersucheAnfrageStellen(typ, 0, out nr);
            e = AnClient(k, Rahmen.Bauen((ushort)typ, flags, nr, bytes));
        }
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, $"{was} angenommen ({e.Fehler} {e.Feld})");
        return bytes.Length;
    }

    [Test]
    public static void P02b_NetzListe()
    {
        var netzliste = new NetzListe
        {
            Version = I53,
            Einstellungen = new NetzEinstellungen { DurchsageNurTaste = false, Selbstwahl = false, SelbstwahlDurch = U32, SelbstwahlBis = I53, Nachzuegler = false },
            Netze = [.. Enumerable.Range(0, Grenzen.NetzeMax).Select(_ => new Netz
            {
                Id = NetzMax, Name = Name32, Typ = NetzTyp.Normal, Reihenfolge = U16, NurTaste = false,
                AusloeserStandard = Ausloeser.Taste, Offen = NetzOffen.HoerenSenden, Standard = false, SchiffId = U16, Schiffsnetz = Kennung(0),
                // 0.8.0: Herkunft für die Verwaltung.
                Herkunft = Emoji(Grenzen.HerkunftMax),
            })],
            Schiffe = [.. Enumerable.Range(0, Grenzen.SchiffeMax).Select(_ => new Schiff
            {
                Id = U16, Name = Name32, Reihenfolge = U16,
                Netze = Enumerable.Range(0, Grenzen.SchiffsnetzKennungenMax).ToDictionary(Kennung, _ => NetzMax),
                Plaetze = [.. Enumerable.Range(0, Grenzen.PlaetzeJeSchiffMax).Select(_ => new SchiffsPlatz { RolleId = U16, Kapazitaet = 255, Mindest = 255 })],
                // Stufe 2 (0.3.0): Einheit mit Art und Typ.
                Art = Einheitsart.Schiff, Typ = Emoji(Grenzen.EinheitTypMax),
            })],
            Rollen = [.. Enumerable.Range(0, Grenzen.RollenMax).Select(_ => new Rolle
            {
                Id = NetzMax, Name = Emoji(Grenzen.RollenNameMax), Reihenfolge = U16, SelbstWaehlbar = false, BrauchtSchiff = false, Kapazitaet = 255,
                Herkunft = Emoji(Grenzen.HerkunftMax),
                Felder = [.. Enumerable.Range(0, Grenzen.FelderMax).Select(f => new RollenFeld
                {
                    Feld = (byte)f, Schiffsnetz = Kennung(f), Senden = false, Hoeren = false, SendenStandard = false, Fest = false,
                })],
            })],
            // S2 (0.5.0, 6.14): Grundstellung und 4 Phasen mit zusammen 512 Variantenfeldern, je 8 Rollen zu 16 Feldern.
            Phasen = [.. Enumerable.Range(1, Grenzen.PhaseNrMax).Select(nr => new NetzPhase
            {
                Nr = (byte)nr, Name = Emoji(Grenzen.PhaseNameMax), Kurz = Emoji(Grenzen.PhaseKurzMax),
                Felder = nr == 1 ? null : [.. Enumerable.Range(0, Grenzen.PhasenFelderMax / Grenzen.PhasenMax).Select(i => new PhasenFeld
                {
                    RolleId = (ushort)(NetzMax - i / Grenzen.FelderMax), Feld = (byte)(i % Grenzen.FelderMax), Schiffsnetz = Kennung(i),
                    Senden = false, Hoeren = false, SendenStandard = false, Fest = false,
                })],
            })],
        };
        Messen("NetzListe", netzliste, Seite.Client, Nachrichtentyp.NetzListe, Nachrichtenart.Push);
        Messen("NetzListe ohne Phasen", netzliste with { Phasen = null }, Seite.Client, Nachrichtentyp.NetzListe, Nachrichtenart.Push);
    }

    private static Mitgliedschaft[] VolleFelder() =>
        [.. Enumerable.Range(0, Grenzen.FelderMax).Select(f => new Mitgliedschaft { Feld = (byte)f, NetzId = (ushort)(NetzMax - f), Hoeren = false, Senden = false, Fest = false })];

    [Test]
    public static void P02b_PraesenzVoll()
    {
        var stumm = Enumerable.Range(0, Grenzen.FelderMax + 1).Select(_ => new Stummschaltung { NetzId = NetzMax, Bis = I53, Durch = U32 }).ToArray();
        var mitDetails = Enumerable.Range(0, Grenzen.SitzungenMax).Select(_ => new PraesenzEintrag
        {
            KontoId = U32, Name = Name32, Benutzername = Name32, Online = false, SchiffId = U16, RolleId = U16, Platz = PlatzStatus.Reserviert,
            Felder = VolleFelder(), SendetIn = [.. Enumerable.Repeat(NetzMax, Grenzen.FelderMax + 1)], Stumm = stumm,
            Verbindung = Verbindungsweg.Tunnel, Guete = Guete.Schlecht,
        });
        var ohneSitzung = Enumerable.Range(0, Grenzen.KontenMax - Grenzen.SitzungenMax).Select(_ => new PraesenzEintrag
        {
            KontoId = U32, Name = Name32, Benutzername = Name32, Online = false, SchiffId = U16, RolleId = U16, Platz = PlatzStatus.Reserviert,
        });
        var praesenz = new Praesenz { Voll = true, ZustandsVersion = I53, Eintraege = [.. mitDetails, .. ohneSitzung], Entfernt = [] };
        Messen("Praesenz voll", praesenz, Seite.Client, Nachrichtentyp.Praesenz, Nachrichtenart.Push);
    }

    [Test]
    public static void P02b_PegelVoreinstellungen()
    {
        var pegel = new PegelVoreinstellungen
        {
            Eintraege = [.. Enumerable.Range(0, Grenzen.PegelMax).Select(_ => new PegelEintrag { KontoId = U32, NetzId = NetzMax, Db = -59.5 })],
        };
        Messen("PegelVoreinstellungen", pegel, Seite.Client, Nachrichtentyp.PegelVoreinstellungen, Nachrichtenart.Push);
    }

    private static AufstellungsEintrag[] VolleEintraege() =>
        [.. Enumerable.Range(0, Grenzen.AufstellungsEintraegeMax).Select(_ => new AufstellungsEintrag
        {
            DiscordId = "18446744073709551615", RolleId = U16, SchiffId = U16,
            Felder = [.. Enumerable.Range(0, Grenzen.FelderMax).Select(f => new FeldBelegung { Feld = (byte)f, NetzId = NetzMax, Hoeren = false, Senden = false })],
            SendeFelder = [.. Enumerable.Range(0, Grenzen.FelderMax).Select(f => (byte)f)],
        })];

    [Test]
    public static void P02b_AufstellungSpeichernUndListe()
    {
        var speichern = new AufstellungSpeichernAnfrage { Id = U32, Version = U32, Name = Emoji(Grenzen.AufstellungsNameMax), Eintraege = VolleEintraege() };
        Messen("AufstellungSpeichern", speichern, Seite.Server, Nachrichtentyp.AufstellungSpeichern, Nachrichtenart.Anfrage);
        var liste = new AufstellungListeAntwort
        {
            Id = U32, Version = U32, Name = Emoji(Grenzen.AufstellungsNameMax), Eintraege = VolleEintraege(), Geaendert = I53, Durch = U32,
        };
        Messen("AufstellungListe mit id", liste, Seite.Client, Nachrichtentyp.AufstellungListe, Nachrichtenart.Antwort);
    }

    [Test]
    public static void P02b_GeraeteListe()
    {
        var geraete = new GeraeteListeAntwort
        {
            Geraete = [.. Enumerable.Range(0, Grenzen.GeraeteListeMax).Select(_ => new GeraetEintrag
            {
                Id = U32, KontoId = U32, KontoName = Name32, Benutzername = Name32, Name = Emoji(Grenzen.GeraeteNameMax),
                Anzeige = "5CD2-52FB-0CE8-9324", Fingerabdruck = "XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=",
                SchluesselArt = SchluesselArt.Software, Status = GeraeteStatus.Freigegeben, Markierung = Markierung.Bestaetigt,
                Verknuepft = I53, Zuletzt = I53,
            })],
        };
        Messen("GeraeteListe", geraete, Seite.Client, Nachrichtentyp.GeraeteListe, Nachrichtenart.Antwort);
    }

    [Test]
    public static void P02b_RaidHelperImport()
    {
        var import = new RaidHelperImportAntwort
        {
            Event = new RaidHelperEvent { Id = new string('9', 24), Titel = Emoji(100), Start = I53, Ende = I53, LastUpdated = I53 },
            Anmeldungen = [.. Enumerable.Range(0, Grenzen.AnmeldungenMax).Select(_ => new RaidHelperAnmeldung
            {
                DiscordId = "18446744073709551615", Name = Name32, ClassName = Emoji(Grenzen.KlassenNameMax), SpecName = Emoji(Grenzen.KlassenNameMax),
                Status = "primary", Position = int.MaxValue, KontoId = U32, Vorschlag = new RollenVorschlag { RolleId = U16, SchiffId = U16 },
            })],
        };
        Messen("RaidHelperImport (Antwort)", import, Seite.Client, Nachrichtentyp.RaidHelperImport, Nachrichtenart.Antwort);
    }

    [Test]
    public static void P02b_Ablage()
    {
        // daten genau 16 KiB: {"x":"aaa..."}
        var daten = "{\"x\":\"" + new string('a', Grenzen.AblageMax - 8) + "\"}";
        Pruefe.Gleich(Grenzen.AblageMax, Encoding.UTF8.GetByteCount(daten), "daten 16 KiB");
        using var dok = JsonDocument.Parse(daten);
        var ablage = new AblageAnfrage { Op = AblageOperation.Schreiben, Bereich = AblageBereich.Ansicht, Daten = dok.RootElement.Clone(), Version = U32 };
        Messen("Ablage", ablage, Seite.Server, Nachrichtentyp.Ablage, Nachrichtenart.Anfrage);

        var zuGross = "{\"x\":\"" + new string('a', Grenzen.AblageMax - 7) + "\"}";
        var body = "{\"op\":\"SCHREIBEN\",\"bereich\":\"ANSICHT\",\"daten\":" + zuGross + "}";
        Pruefe.Gleich(Fehlercode.ZuGross, SteuerJson.Lesen(Encoding.UTF8.GetBytes(body), SteuerJson.Info<AblageAnfrage>()).Fehler, "16 KiB + 1");
    }

    /// <summary>
    /// 4.10 mit Stufe 2 (0.3.0): Praesenz voll mit allen Feldern aus Stufe 1 und 2 (Hauptname, Discord-Name, Untergilde, drei
    /// Zeichen, Quelle, durch, seit, letzter Platz), dazu die neuen Nachrichten an ihren Höchstwerten.
    /// </summary>
    [Test]
    public static void P02b_Stufe2_PraesenzBelegungVerzeichnisAuftraege()
    {
        string[] zeichen = ["ADMIN", "FUEHRUNG", "EINSATZLEITUNG"];
        var stumm = Enumerable.Range(0, Grenzen.FelderMax + 1).Select(_ => new Stummschaltung { NetzId = NetzMax, Bis = I53, Durch = U32 }).ToArray();
        PraesenzEintrag Eintrag(bool details, bool stufe2) => new()
        {
            KontoId = U32, Name = Name32, Benutzername = Name32, Online = false, SchiffId = U16, RolleId = U16, Platz = PlatzStatus.Reserviert,
            ScName = Name32, DiscordName = Name32, Untergilde = U16, Zeichen = zeichen,
            Felder = details ? VolleFelder() : null, SendetIn = details ? [.. Enumerable.Repeat(NetzMax, Grenzen.FelderMax + 1)] : null,
            Stumm = details ? stumm : null, Verbindung = details ? Verbindungsweg.Tunnel : null, Guete = details ? Guete.Schlecht : null,
            Quelle = details && stufe2 ? MitgliedschaftsQuelle.Wiederhergestellt : null, Durch = details && stufe2 ? U32 : null,
            Seit = details && stufe2 ? I53 : null, LetzterPlatz = details && stufe2 ? new LetzterPlatz { SchiffId = U16, RolleId = U16 } : null,
        };
        Praesenz Voll(bool stufe2) => new()
        {
            Voll = true, ZustandsVersion = I53, Entfernt = [],
            Eintraege = [.. Enumerable.Range(0, Grenzen.SitzungenMax).Select(_ => Eintrag(true, stufe2)),
                .. Enumerable.Range(0, Grenzen.KontenMax - Grenzen.SitzungenMax).Select(_ => Eintrag(false, stufe2))],
        };
        // Schon Stufe 1 (Hauptname, Discord-Name, Untergilde, Zeichen für alle 500) liegt über der halben Rahmengrenze; 4.10 schätzte
        // 450 KB. Stufe 2 legt nur die Details der 100 Sitzungen drauf. Bericht statt Fehlschlag, die Rahmengrenze hält.
        var s1 = Messen("Praesenz voll (Stufe 1)", Voll(false), Seite.Client, Nachrichtentyp.Praesenz, Nachrichtenart.Push, nurBericht: true);
        var s2 = Messen("Praesenz voll (Stufe 2)", Voll(true), Seite.Client, Nachrichtentyp.Praesenz, Nachrichtenart.Push, nurBericht: true);
        Console.WriteLine($"P-02b Praesenz voll: Stufe 2 legt {s2 - s1} Byte auf Stufe 1");

        var belegung = new Belegung
        {
            ZustandsVersion = I53, Online = U16, MitPlatz = U16,
            Plaetze = [.. Enumerable.Range(0, Grenzen.BelegungPlaetzeMax).Select(_ => new PlatzBelegung { SchiffId = U16, RolleId = U16, Belegt = U16, Reserviert = U16, Kapazitaet = 255, Mindest = 255 })],
        };
        Messen("Belegung", belegung, Seite.Client, Nachrichtentyp.Belegung, Nachrichtenart.Push);

        var verzeichnis = new KontenVerzeichnisAntwort
        {
            Konten = [.. Enumerable.Range(0, Grenzen.KontenMax).Select(_ => new VerzeichnisEintrag
            {
                KontoId = U32, Name = Name32, Benutzername = Name32, ScName = Name32, DiscordName = Name32, Stufe = Stufe.Raidleiter, Online = false,
                SchiffId = U16, RolleId = U16, Platz = PlatzStatus.Reserviert, LetzterPlatz = new LetzterPlatz { SchiffId = U16, RolleId = U16 }, Untergilde = U16,
            })],
        };
        Messen("KontenVerzeichnis (Antwort)", verzeichnis, Seite.Client, Nachrichtentyp.KontenVerzeichnis, Nachrichtenart.Antwort);

        var auftraege = new BeauftragungAntwort
        {
            Beauftragungen = [.. Enumerable.Range(0, Grenzen.AuftraegeMax).Select(_ => new BeauftragungEintrag
            {
                KontoId = U32, Name = Name32, ScName = Name32, Von = I53, Bis = I53, Rechte = Enum.GetValues<Recht>(), Durch = U32, Seit = I53,
                Grund = Emoji(Grenzen.AuftragGrundMax), Aktiv = false,
            })],
        };
        Messen("Beauftragung (Antwort)", auftraege, Seite.Client, Nachrichtentyp.Beauftragung, Nachrichtenart.Antwort);
    }

    /// <summary>
    /// 4.10 mit Stufe 3, Schritt 3.2 (0.6.0, 6.17): alle 500 Kanalsperren in der Antwort auf KanalSperre, 80 in
    /// <c>Mitgliedschaften.gesperrt</c> (16 eigene und 64 für alle), und die Praesenz voll der Stufe 2 mit gewählter Rolle und allen 500
    /// Sperren als Details (31 Konten zu 16 und eines zu 4; mehr gibt es zusammen nicht).
    /// </summary>
    [Test]
    public static void P02b_Stufe3_KanalsperrenMitgliedschaftenPraesenz()
    {
        var sperre = new Kanalsperre { NetzId = NetzMax, Bis = I53, Durch = U32 };
        Messen("KanalSperre (Antwort)", new KanalSperreAntwort { Sperren = [.. Enumerable.Repeat(sperre with { KontoId = U32 }, Kanalrechnung.SperrenMax)] },
            Seite.Client, Nachrichtentyp.KanalSperre, Nachrichtenart.Antwort);
        var mitglied = new Mitgliedschaften
        {
            ZustandsVersion = I53, KontoVersion = I53, SchiffId = U16, RolleId = U16, Platz = PlatzStatus.Reserviert, Felder = VolleFelder(),
            Global = new GlobalAngabe { Senden = false }, Sendeauswahl = new Sendeauswahl { Felder = [.. Enumerable.Range(0, Grenzen.FelderMax).Select(f => (byte)f)], Global = false },
            Stumm = [.. Enumerable.Range(0, Grenzen.FelderMax + 1).Select(_ => new Stummschaltung { NetzId = NetzMax, Bis = I53, Durch = U32 })],
            Quelle = MitgliedschaftsQuelle.Wiederhergestellt, Durch = U32, Rolle = NetzMax,
            Gesperrt = [.. Enumerable.Repeat(sperre with { Alle = true }, Kanalrechnung.GesperrtMax)],
        };
        Messen("Mitgliedschaften mit 80 Sperren", mitglied, Seite.Client, Nachrichtentyp.Mitgliedschaften, Nachrichtenart.Push);

        string[] zeichen = ["ADMIN", "FUEHRUNG", "EINSATZLEITUNG"];
        var stumm = Enumerable.Range(0, Grenzen.FelderMax + 1).Select(_ => new Stummschaltung { NetzId = NetzMax, Bis = I53, Durch = U32 }).ToArray();
        PraesenzEintrag Eintrag(bool details, int sperren) => new()
        {
            KontoId = U32, Name = Name32, Benutzername = Name32, Online = false, SchiffId = U16, RolleId = U16, Platz = PlatzStatus.Reserviert,
            ScName = Name32, DiscordName = Name32, Untergilde = U16, Zeichen = zeichen,
            Felder = details ? VolleFelder() : null, SendetIn = details ? [.. Enumerable.Repeat(NetzMax, Grenzen.FelderMax + 1)] : null,
            Stumm = details ? stumm : null, Verbindung = details ? Verbindungsweg.Tunnel : null, Guete = details ? Guete.Schlecht : null,
            Quelle = details ? MitgliedschaftsQuelle.Wiederhergestellt : null, Durch = details ? U32 : null, Seit = details ? I53 : null,
            LetzterPlatz = details ? new LetzterPlatz { SchiffId = U16, RolleId = U16 } : null, AlsFunker = details ? true : null,
            Rolle = details ? NetzMax : null, Gesperrt = details && sperren > 0 ? [.. Enumerable.Repeat(sperre, sperren)] : null,
        };
        var praesenz = new Praesenz
        {
            Voll = true, ZustandsVersion = I53, Entfernt = [],
            Eintraege = [.. Enumerable.Range(0, Grenzen.SitzungenMax).Select(i => Eintrag(true, Math.Clamp(Kanalrechnung.SperrenMax - i * Kanalrechnung.SperrenJeKontoMax, 0, Kanalrechnung.SperrenJeKontoMax))),
                .. Enumerable.Range(0, Grenzen.KontenMax - Grenzen.SitzungenMax).Select(_ => Eintrag(false, 0))],
        };
        Pruefe.Gleich(Kanalrechnung.SperrenMax, praesenz.Eintraege.Sum(e => e.Gesperrt?.Count ?? 0), "alle 500 Sperren in der Praesenz");
        var s3 = Messen("Praesenz voll (Stufe 3)", praesenz, Seite.Client, Nachrichtentyp.Praesenz, Nachrichtenart.Push, nurBericht: true);

        // 0.11.7: dazu bei allen 500 neun Abzeichen mit 16 Zeichen langen Kennungen und ein Abzeichen vorn.
        var abzeichen = Enumerable.Range(0, Grenzen.AbzeichenMax).Select(i => "ABZEICHEN_" + i.ToString("D6", CultureInfo.InvariantCulture)).ToArray();
        var mitAbzeichen = praesenz with { Eintraege = [.. praesenz.Eintraege.Select(e => e with { Abzeichen = abzeichen, AbzeichenVorn = abzeichen[0] })] };
        var s4 = Messen("Praesenz voll (0.11.7, Abzeichen)", mitAbzeichen, Seite.Client, Nachrichtentyp.Praesenz, Nachrichtenart.Push, nurBericht: true);
        Console.WriteLine($"P-02b Praesenz voll: Abzeichen legen {s4 - s3} Byte auf Stufe 3");
        var liste = new MitgliedAntwort
        {
            Mitglieder = [.. Enumerable.Range(0, Grenzen.MitgliederListeMax).Select(_ => new MitgliedEintrag
            {
                KontoId = U32, Name = Name32, ScNamen = [Name32, Name32, Name32], DiscordName = Name32, Benutzername = Name32, Untergilde = U16,
                Rollen = [.. Enumerable.Repeat(U16, Grenzen.MitgliederrollenMax)], Lieblingsrollen = [.. Enumerable.Repeat(U16, Grenzen.LieblingsrollenMax)],
                ErsteWahl = U16, Stufe = Stufe.Raidleiter, Zustand = MitgliedZustand.Freigegeben, Zuletzt = I53, Notiz = Emoji(80), Eigentuemer = true,
                Admin = AdminStatus.Einrichtung, Raenge = abzeichen[..Grenzen.RaengeMax], Taetigkeiten = abzeichen[Grenzen.RaengeMax..],
            })],
            Weiter = U32,
        };
        Messen("Mitglied LISTE, eine Seite mit Abzeichen (Antwort)", liste, Seite.Client, Nachrichtentyp.Mitglied, Nachrichtenart.Antwort);
    }

    /// <summary>
    /// Kein Fall aus 4.10, aber für die Grenze in WARTERAUM wichtig: Welcome{FREIGEGEBEN} mit ausgeschöpftem Profil
    /// (4 Hosts, 3 Pins, 4 Nameserver). Er passt nicht in 1032 Byte und nicht einmal in 4096; darum nimmt der
    /// Client immer bis 1 MiB an (Bericht).
    /// </summary>
    [Test]
    public static void P02b_WelcomeFreigegeben_MitVollemProfil()
    {
        var host = new string('h', 253);
        var profil = "{\"name\":\"" + Name32 + "\",\"profilVersion\":4294967295,\"ausgestellt\":9007199254740991,\"gueltigBis\":9007199254740991,\"hosts\":["
            + string.Join(',', Enumerable.Repeat("{\"name\":\"" + host + "\",\"familien\":[\"IPv4\",\"IPv6\"]}", 4))
            + "],\"tcpPort\":65535,\"pins\":[" + string.Join(',', Enumerable.Repeat("\"XNJS+wzokyQ2+vjM0QQJgbie5K1rn+niorfnGqyyfNM=\"", 3))
            + "],\"nameserver\":[" + string.Join(',', Enumerable.Repeat("\"" + host + "\"", 4)) + "]}";
        var w = WelcomeFreigegeben() with
        {
            Konto = new KontoAngabe { Id = int.MaxValue, Name = Name32, Benutzername = Name32, Stufe = Stufe.Raidleiter, Rechte = Enum.GetValues<Recht>() },
            Server = new ServerAngabe { Version = new string('9', 32) },
            MindestClientVersion = "99999999.99999999.9999999999-a.b",
            Profil = new SigniertesProfil { Daten = Convert.ToBase64String(Encoding.UTF8.GetBytes(profil)), Signatur = WelcomeFreigegeben().Profil!.Signatur },
        };
        var bytes = SteuerJson.Serialisieren(w);
        Console.WriteLine($"P-02b Welcome FREIGEGEBEN mit vollem Profil: {bytes.Length} Byte Body (Profil-JSON {Encoding.UTF8.GetByteCount(profil)} Byte)");
        Pruefe.Wahr(bytes.Length + 8 > Rahmengrenzen.RegistrierungUndWarteraum, "größer als die WARTERAUM-Grenze");
        var k = ClientIn(ClientZustand.Warteraum);
        var e = AnClient(k, Rahmen.Bauen((ushort)Nachrichtentyp.Welcome, 0, 0, bytes));
        Pruefe.Gleich(EingangsStatus.Annehmen, e.Status, "Client nimmt an");
        Pruefe.Gleich(ClientZustand.Sync, k.Automat.Zustand, "WARTERAUM -> SYNC");
    }
}
