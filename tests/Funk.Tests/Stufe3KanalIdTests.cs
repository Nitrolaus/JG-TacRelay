// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;
using static Funk.Tests.SteuerHilfe;

namespace Funk.Tests;

/// <summary>
/// Stufe 3, Protokoll (Fassung 0.6.0, FUNKPROTOKOLL 6.16, 4.5, 4.8, 4.10, P-61): die Kanal-ID hin und zurück an ihren Rändern, die
/// Prüfregeln von <c>kanaele</c>, <c>gruppe</c>, <c>ch</c> und <c>RolleWaehlen</c>, der Katalog mit 0x0039 und die Größe einer
/// NetzListe mit 64 ausgeschöpften Kanalnetzen.
/// </summary>
internal static class Stufe3KanalIdTests
{
    private static string Emoji(int n) => string.Concat(Enumerable.Repeat("\U0001F600", n));

    private static Fehlercode? Fehler<T>(T body) where T : ISteuerBody
    {
        var p = new Pruefung();
        body.Pruefen(ref p);
        return p.Fehler;
    }

    internal static Netz Kanalnetz(ushort id, int eintraege, string? gruppe = null) => new()
    {
        Id = id, Name = "Kanalnetz " + id, Typ = id == 1 ? NetzTyp.Global : NetzTyp.Normal, Reihenfolge = 1, NurTaste = false,
        AusloeserStandard = Ausloeser.Va, Offen = NetzOffen.HoerenSenden, Standard = false,
        Kanaele = [.. Enumerable.Range(0, eintraege).Select(i => i == 0 ? "Ruhe" : "Schiff " + i)], Gruppe = gruppe,
    };

    [Test]
    public static void KanalId_HinUndZurueck_AnDenRaendern()
    {
        Pruefe.Gleich((ushort)0x8020, Kanalrechnung.Id(1, 0), "Global, Ruhe");
        Pruefe.Gleich((ushort)0x8021, Kanalrechnung.GlobalSelbstId, "Gesamt");
        Pruefe.Gleich((ushort)65502, Kanalrechnung.Id(1022, 30), "höchste Kanal-ID");
        Pruefe.Gleich((ushort)(0x8000 + 100 * 32 + 3), Kanalrechnung.Id(100, 3), "Brücke, Schiff 3");
        foreach (var (n, c) in new (ushort, byte)[] { (1, 0), (1, 1), (2, 30), (100, 3), (1022, 0), (1022, 30) })
        {
            Pruefe.Wahr(Kanalrechnung.Zerlegen(Kanalrechnung.Id(n, c), out var netz, out var ch) && netz == n && ch == c, $"zurück: {n}/{c}");
        }
        Pruefe.Falsch(Kanalrechnung.Zerlegen(0x8000, out _, out _), "Netz 0 gibt es nicht");
        Pruefe.Falsch(Kanalrechnung.Zerlegen(0x801F, out _, out _), "Netz 0, Ch. 31");
        Pruefe.Falsch(Kanalrechnung.Zerlegen(65503, out _, out _), "1022/31: Ch. 31 gibt es nicht");
        Pruefe.Falsch(Kanalrechnung.Zerlegen((ushort)(0x8000 + 1023 * 32), out _, out _), "Netz 1023 kein Kanalnetz");
        Pruefe.Falsch(Kanalrechnung.Zerlegen(0xFFFF, out _, out _), "0xFFFF");
        Pruefe.Falsch(Kanalrechnung.Zerlegen(7, out _, out _), "ein gewöhnliches Netz ist kein Kanal");
        Pruefe.Falsch(Kanalrechnung.IstKanalbereich(0x7FFF), "0x7FFF ist ein Netz");
        Pruefe.Wahr(Kanalrechnung.IstKanalbereich(0x8000), "ab 0x8000 Kanalbereich");
        Pruefe.Wahr(Kanalrechnung.IstRuhe(Kanalrechnung.Id(7, 0)), "Ch. 0 ist Ruhe");
        Pruefe.Falsch(Kanalrechnung.IstRuhe(Kanalrechnung.Id(7, 3)), "Ch. 3 ist keine Ruhe");
        Pruefe.Falsch(Kanalrechnung.IstRuhe(7), "Netz 7 ist keine Ruhe");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => Kanalrechnung.Id(0, 1), "Netz 0");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => Kanalrechnung.Id(1023, 1), "Netz 1023");
        Pruefe.Wirft<ArgumentOutOfRangeException>(() => Kanalrechnung.Id(5, 31), "Ch. 31");
    }

    [Test]
    public static void Netz_Kanaele_UndGruppe_Pruefregeln()
    {
        Pruefe.Gleich<Fehlercode?>(null, Fehler(Kanalnetz(100, 2)), "Ruhe und ein Kanal");
        Pruefe.Gleich<Fehlercode?>(null, Fehler(Kanalnetz(1022, 31, Emoji(Kanalrechnung.GruppeMax))), "31 Einträge, Netz 1022, Gruppe 20 Zeichen");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(Kanalnetz(100, 1)), "nur die Ruhe");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.ZuGross, Fehler(Kanalnetz(100, 32)), "32 Einträge");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(Kanalnetz(1023, 5)), "Kanalnetz über 1022");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(Kanalnetz(100, 3) with { Kanaele = ["Ruhe", " ", "Schiff 2"] }), "leere Bezeichnung");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.ZuGross, Fehler(Kanalnetz(100, 3) with { Kanaele = ["Ruhe", Emoji(21), "Schiff 2"] }), "Bezeichnung über 20");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.ZuGross, Fehler(Kanalnetz(100, 3, Emoji(21))), "Gruppe über 20");
        Pruefe.Gleich<Fehlercode?>(null, Fehler(Kanalnetz(100, 3) with { Kanaele = null, Gruppe = "Schiffe" }), "gewöhnliches Netz mit Gruppe");
    }

    [Test]
    public static void RollenFeld_UndPhasenFeld_Ch_Pruefregeln()
    {
        var rf = new RollenFeld { Feld = 1, NetzId = 100, Hoeren = true, Senden = true, SendenStandard = false, Fest = false };
        Pruefe.Gleich<Fehlercode?>(null, Fehler(rf with { Ch = 30 }), "Ch. 30");
        Pruefe.Gleich<Fehlercode?>(null, Fehler(rf with { Ch = 0 }), "Ch. 0 Ruhe fest");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(rf with { Ch = 31 }), "Ch. 31");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(rf with { NetzId = null, Schiffsnetz = "BRIDGE", Ch = 3 }), "Ch. nur mit netzId");
        var pf = new PhasenFeld { RolleId = 12, Feld = 1, NetzId = 100, Hoeren = true, Senden = true, SendenStandard = false, Fest = false };
        Pruefe.Gleich<Fehlercode?>(null, Fehler(pf with { Ch = 5 }), "Phasenfeld Ch. 5");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(pf with { Ch = 31 }), "Phasenfeld Ch. 31");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(pf with { NetzId = null, Schiffsnetz = "BRIDGE", Ch = 1 }), "Phasenfeld Ch. nur mit netzId");
    }

    [Test]
    public static void RolleWaehlen_Pruefregeln()
    {
        Pruefe.Gleich<Fehlercode?>(null, Fehler(new RolleWaehlenAnfrage { RolleId = 12 }), "ohne Wünsche");
        Pruefe.Gleich<Fehlercode?>(null, Fehler(new RolleWaehlenAnfrage { RolleId = 12, Ch = [.. Enumerable.Range(0, 16).Select(f => new ChWunsch { Feld = (byte)f, Ch = 30 })] }),
            "16 Wünsche, Ch. 30");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(new RolleWaehlenAnfrage { RolleId = 0 }), "Rolle 0");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(new RolleWaehlenAnfrage { RolleId = 0xFFFF }), "Rolle 65535");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.ZuGross, Fehler(new RolleWaehlenAnfrage { RolleId = 12, Ch = [.. Enumerable.Range(0, 17).Select(f => new ChWunsch { Feld = (byte)(f % 16), Ch = 1 })] }),
            "17 Wünsche");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(new RolleWaehlenAnfrage { RolleId = 12, Ch = [new ChWunsch { Feld = 16, Ch = 1 }] }), "Feld 16");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(new RolleWaehlenAnfrage { RolleId = 12, Ch = [new ChWunsch { Feld = 1, Ch = 31 }] }), "Ch. 31");
        Pruefe.Gleich<Fehlercode?>(Fehlercode.Ungueltig, Fehler(new RolleWaehlenAnfrage { RolleId = 12, Ch = [new ChWunsch { Feld = 1, Ch = 2 }, new ChWunsch { Feld = 1, Ch = 3 }] }),
            "Feld doppelt");
    }

    [Test]
    public static void Katalog_RolleWaehlen_Bremse_Fassung()
    {
        var e = Katalog.Von(Nachrichtentyp.RolleWaehlen);
        Pruefe.Gleich((ushort)0x0039, (ushort)e.Typ, "0x0039");
        Pruefe.Wahr(e.HochAnfrage.Erlaubt && e.RunterAntwort.Erlaubt && e.Zustaende == Zustaende.Aktiv, "Anfrage in AKTIV mit Antwort");
        Pruefe.Gleich(Ratengruppe.FeldUndSatz, Ratengrenzen.Gruppe(Nachrichtentyp.RolleWaehlen), "ein Eimer mit FeldSetzen und SatzAnwenden");
        Pruefe.Gleich("0.6.0", Versionen.ServerFassungFuer(Nachrichtentyp.RolleWaehlen), "erst an Server ab 0.6.0");
        Pruefe.Falsch(Versionen.ServerKennt("0.5.0", Nachrichtentyp.RolleWaehlen), "Server 0.5.0 kennt 0x0039 nicht");
        Pruefe.Wahr(Versionen.ServerKennt("0.6.0", Nachrichtentyp.RolleWaehlen), "Server 0.6.0 kennt 0x0039");

        // Eine Anfrage mit Wünschen kommt über den Draht so an, wie sie ging.
        var k = ServerIn(ServerZustand.Aktiv);
        var body = SteuerJson.Serialisieren(new RolleWaehlenAnfrage { RolleId = 12, Ch = [new ChWunsch { Feld = 1, Ch = 3 }] });
        var ein = AnServer(k, Rahmen.Bauen((ushort)Nachrichtentyp.RolleWaehlen, 0, 1, body));
        Pruefe.Wahr(ein.Status == EingangsStatus.Annehmen && ein.Body is RolleWaehlenAnfrage { RolleId: 12, Ch: [{ Feld: 1, Ch: 3 }] }, "angenommen: " + ein.Fehler);
    }

    /// <summary>
    /// 4.10: die NetzListe aus P-02b (128 Netze, 64 Einheiten, 64 Rollen, 5 Phasen), davon 64 Kanalnetze zu 31 Bezeichnungen mit
    /// je 20 Vier-Byte-Zeichen und einer Gruppe; Rollen- und Phasenfelder mit <c>ch</c>. Dazu Mitgliedschaften mit 16 Kanal-IDs.
    /// </summary>
    [Test]
    public static void Groesse_NetzListeMitKanalnetzen_UndMitgliedschaften()
    {
        var name20 = Emoji(Kanalrechnung.ChNameMax);
        var netze = Enumerable.Range(1, Grenzen.NetzeMax).Select(i => new Netz
        {
            Id = (ushort)i, Name = Emoji(Grenzen.NameMax), Typ = i == 1 ? NetzTyp.Global : NetzTyp.Normal, Reihenfolge = 65535, NurTaste = false,
            AusloeserStandard = Ausloeser.Taste, Offen = NetzOffen.HoerenSenden, Standard = false, SchiffId = 65535, Schiffsnetz = "KENNUNG_00000000",
            Kanaele = i <= Kanalrechnung.KanalnetzeMax ? [.. Enumerable.Range(0, Kanalrechnung.EintraegeMax).Select(_ => name20)] : null,
            Gruppe = Emoji(Kanalrechnung.GruppeMax),
        }).ToList();
        var liste = new NetzListe
        {
            Version = (1L << 53) - 1,
            Einstellungen = new NetzEinstellungen { DurchsageNurTaste = false, Selbstwahl = false, SelbstwahlDurch = uint.MaxValue, SelbstwahlBis = (1L << 53) - 1, Nachzuegler = false },
            Netze = netze,
            Schiffe = [.. Enumerable.Range(0, Grenzen.SchiffeMax).Select(_ => new Schiff
            {
                Id = 65535, Name = Emoji(Grenzen.NameMax), Reihenfolge = 65535,
                Netze = Enumerable.Range(0, Grenzen.SchiffsnetzKennungenMax).ToDictionary(k => "KENNUNG_" + k.ToString("D8", System.Globalization.CultureInfo.InvariantCulture), _ => (ushort)65502),
                Plaetze = [.. Enumerable.Range(0, Grenzen.PlaetzeJeSchiffMax).Select(_ => new SchiffsPlatz { RolleId = 65535, Kapazitaet = 255, Mindest = 255 })],
                Art = Einheitsart.Schiff, Typ = Emoji(Grenzen.EinheitTypMax),
            })],
            Rollen = [.. Enumerable.Range(0, Grenzen.RollenMax).Select(_ => new Rolle
            {
                Id = 65534, Name = Emoji(Grenzen.RollenNameMax), Reihenfolge = 65535, SelbstWaehlbar = false, BrauchtSchiff = false, Kapazitaet = 255,
                Felder = [.. Enumerable.Range(0, Grenzen.FelderMax).Select(f => new RollenFeld
                {
                    Feld = (byte)f, NetzId = 1022, Senden = false, Hoeren = false, SendenStandard = false, Fest = false, Ch = 30,
                })],
            })],
            Phasen = [.. Enumerable.Range(1, Grenzen.PhaseNrMax).Select(nr => new NetzPhase
            {
                Nr = (byte)nr, Name = Emoji(Grenzen.PhaseNameMax), Kurz = Emoji(Grenzen.PhaseKurzMax),
                Felder = nr == 1 ? null : [.. Enumerable.Range(0, Grenzen.PhasenFelderMax / Grenzen.PhasenMax).Select(i => new PhasenFeld
                {
                    RolleId = (ushort)(65534 - i / Grenzen.FelderMax), Feld = (byte)(i % Grenzen.FelderMax), NetzId = 1022,
                    Senden = false, Hoeren = false, SendenStandard = false, Fest = false, Ch = 30,
                })],
            })],
        };
        var bytes = SteuerJson.Serialisieren(liste);
        Console.WriteLine($"4.10 NetzListe mit 64 Kanalnetzen: {bytes.Length} Byte, Faktor {Rahmengrenzen.Aktiv / (double)(bytes.Length + 8):F2} unter 1 MiB");
        Pruefe.Wahr(bytes.Length + Rahmen.LaengeOhneBody <= Rahmengrenzen.Aktiv / 2, $"NetzListe mit Kanalnetzen: {bytes.Length} Byte, über der halben Rahmengrenze");
        var ein = AnClient(ClientIn(ClientZustand.AktivUdp), Rahmen.Bauen((ushort)Nachrichtentyp.NetzListe, 0, 0, bytes));
        Pruefe.Gleich(EingangsStatus.Annehmen, ein.Status, $"angenommen ({ein.Fehler} {ein.Feld})");
        var gelesen = (NetzListe)ein.Body!;
        Pruefe.Wahr(gelesen.Netze.Count(n => n.Kanaele is { Count: Kanalrechnung.EintraegeMax }) == Kanalrechnung.KanalnetzeMax
            && gelesen.Rollen[0].Felder[0].Ch == 30 && gelesen.Phasen![1].Felder![0].Ch == 30, "Kanalnetze, ch in Rollen und Phasen kommen an");

        var mitglied = new Mitgliedschaften
        {
            ZustandsVersion = 1, KontoVersion = 1, RolleId = 12, Rolle = 12,
            Felder = [.. Enumerable.Range(0, Grenzen.FelderMax).Select(f => new Mitgliedschaft { Feld = (byte)f, NetzId = Kanalrechnung.Id((ushort)(f + 1), (byte)(f % 2 == 0 ? 0 : 30)), Hoeren = true, Senden = true, Fest = false })],
            Global = new GlobalAngabe { Senden = false },
            Sendeauswahl = new Sendeauswahl { Felder = [1], Global = false },
            Stumm = [],
            Quelle = MitgliedschaftsQuelle.Selbst,
        };
        var m = AnClient(ClientIn(ClientZustand.AktivUdp), Rahmen.Bauen((ushort)Nachrichtentyp.Mitgliedschaften, 0, 0, SteuerJson.Serialisieren(mitglied)));
        Pruefe.Wahr(m.Status == EingangsStatus.Annehmen && m.Body is Mitgliedschaften { Rolle: 12 } mg && mg.Felder.All(f => Kanalrechnung.IstKanalbereich(f.NetzId)),
            $"Mitgliedschaften mit 16 Kanal-IDs und Rolle ({m.Fehler} {m.Feld})");
    }
}
