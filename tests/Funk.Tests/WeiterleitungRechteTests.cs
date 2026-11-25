// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Globalization;
using Funk.Server.Lage;

namespace Funk.Tests;

/// <summary>Zeichnet alle Ausgaben der Weiterleitung auf.</summary>
internal sealed class Mitschrift : IWeiterleitungsZiel
{
    public List<(Stempel Stempel, ushort[] Netze, int Opus)> Sprache { get; } = [];
    public List<(uint Empfaenger, uint Sprecher, Stempelart Art, ushort[] Netze, ushort Phase)> Starts { get; } = [];
    public List<(uint Empfaenger, uint Sprecher, ushort Phase)> Enden { get; } = [];
    public List<(uint Sitzung, SendeFehlerEintrag[] Fehler, bool Teilweise, uint Verworfen)> Fehler { get; } = [];
    public List<(uint Sitzung, uint Konto)> Raten { get; } = [];

    public void Leeren()
    {
        Sprache.Clear();
        Starts.Clear();
        Enden.Clear();
        Fehler.Clear();
        Raten.Clear();
    }

    void IWeiterleitungsZiel.Sprache(in Stempel stempel, ReadOnlySpan<ushort> netze, ReadOnlySpan<byte> opus) =>
        Sprache.Add((stempel, netze.ToArray(), opus.Length));

    void IWeiterleitungsZiel.DurchsageStart(uint empfaenger, uint sprecher, Stempelart art, ReadOnlySpan<ushort> netze, ushort sprechphase) =>
        Starts.Add((empfaenger, sprecher, art, netze.ToArray(), sprechphase));

    void IWeiterleitungsZiel.DurchsageEnde(uint empfaenger, uint sprecher, ushort sprechphase) =>
        Enden.Add((empfaenger, sprecher, sprechphase));

    void IWeiterleitungsZiel.SendeFehler(uint sitzung, ReadOnlySpan<SendeFehlerEintrag> fehler, bool teilweise, uint verworfen) =>
        Fehler.Add((sitzung, fehler.ToArray(), teilweise, verworfen));

    void IWeiterleitungsZiel.RateUeberschritten(uint sitzung, uint kontoId) => Raten.Add((sitzung, kontoId));

    /// <summary>
    /// Empfang in der Schreibweise der Prüfwerte, nach Empfänger sortiert: "A:NETZ[7] E:NETZ[7,2]".
    /// Mit Kennzeichen hängt "+B" (BEGINN), "+E" (ENDE) und "+X" (vom Server erzeugt) an.
    /// </summary>
    public string Empfang(bool mitKennzeichen = false) => string.Join(" ", Sprache
        .Select(s => Funkprobe.Buchstabe(s.Stempel.Empfaenger) + ":" + Drahtnamen.Von(s.Stempel.Art) + "[" + string.Join(",", s.Netze) + "]"
            + (mitKennzeichen ? Kennzeichen(s.Stempel) : ""))
        .Order(StringComparer.Ordinal));

    private static string Kennzeichen(Stempel s) =>
        ((s.Kennzeichen & Paketkennzeichen.Beginn) != 0 ? "+B" : "")
        + ((s.Kennzeichen & Paketkennzeichen.Ende) != 0 ? "+E" : "")
        + (s.Erzeugt ? "+X" : "");
}

/// <summary>Grundzustand aus 8.9 mit Sitzungen für A bis E und einem Admin ohne Sitzung.</summary>
internal sealed class Funkprobe
{
    public const uint Admin = 1000;
    public const uint A = 1001;
    public const uint B = 1002;
    public const uint C = 1003;
    public const uint D = 1004;
    public const uint E = 1005;
    public const ushort Global = 1;
    public const ushort Kommando = 2;
    public const ushort Crew = 7;
    public const ushort Bruecke = 8;
    public const ushort Schiff3 = 3;
    public const ushort RolleCo = 10;
    public const ushort RollePilot = 11;
    public const long Start = 1_790_000_000_000;

    /// <summary>Gültige Opus-Nutzlast: CELT, config 31 (20 ms), mono, ein Frame.</summary>
    public static readonly byte[] Opus = [0xF8, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77];

    public Funkprobe(Func<SchnappschussBauer, SchnappschussBauer>? zusatz = null)
    {
        var bauer = Grundzustand();
        Schreiber = new Zustandsschreiber((zusatz?.Invoke(bauer) ?? bauer).Bauen());
    }

    public Zustandsschreiber Schreiber { get; }
    public Weiterleiter Relay { get; } = new();
    public Mitschrift M { get; } = new();
    public long Jetzt { get; set; } = Start;
    public Schnappschuss Z => Schreiber.Aktuell;

    public static uint S(uint konto) => konto + 9000;

    public static string Buchstabe(uint sitzung) => sitzung is >= 10001 and <= 10005
        ? ((char)('A' + (sitzung - 10001))).ToString()
        : sitzung.ToString(CultureInfo.InvariantCulture);

    public static Mitgliedschaft F(byte feld, ushort netz, bool hoeren, bool senden, bool fest = false) => new(feld, netz, hoeren, senden, fest);

    public static SchnappschussBauer Grundzustand()
    {
        var bauer = new SchnappschussBauer()
            .MitNetz(new Netz(Kommando, "Kommando") { Reihenfolge = 1, Offen = Offenheit.HoerenSenden })
            .MitNetz(new Netz(Crew, "Crew Schiff 3") { Reihenfolge = 2, SchiffId = Schiff3, Schiffsnetz = "CREW" })
            .MitNetz(new Netz(Bruecke, "Brücke Schiff 3") { Reihenfolge = 3, NurTaste = true, SchiffId = Schiff3, Schiffsnetz = "BRUECKE" })
            .MitRolle(new Rolle(RolleCo, "CO")
            {
                SelbstWaehlbar = true,
                BrauchtSchiff = true,
                Felder = [new RollenFeld(0, 0, "BRUECKE", true, true, true, false), new RollenFeld(1, 0, "CREW", true, true, false, false)],
            })
            .MitRolle(new Rolle(RollePilot, "Pilot")
            {
                SelbstWaehlbar = true,
                BrauchtSchiff = true,
                Felder = [new RollenFeld(0, 0, "CREW", true, true, true, false)],
            })
            .MitSchiff(new Schiff(Schiff3, "Schiff 3")
            {
                Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
                    [new KeyValuePair<string, ushort>("CREW", Crew), new KeyValuePair<string, ushort>("BRUECKE", Bruecke)]),
                Plaetze = [new SchiffsPlatz(RolleCo, 1), new SchiffsPlatz(RollePilot, 1)],
            })
            .MitEinstellungen(new Einstellungen(DurchsageNurTaste: true, Selbstwahl: true))
            .MitKonto(new Konto { Id = Admin, Name = "Admin", Stufe = Stufe.Admin })
            .MitKonto(new Konto { Id = A, Name = "A", Stufe = Stufe.Raidleiter, Felder = [F(0, Kommando, true, true), F(1, Crew, true, true)] })
            .MitKonto(new Konto { Id = B, Name = "B", Felder = [F(0, Crew, true, true), F(1, Bruecke, true, true)] })
            .MitKonto(new Konto { Id = C, Name = "C", Felder = [F(0, Kommando, true, false), F(1, Crew, false, true), F(2, Bruecke, true, false)] })
            .MitKonto(new Konto { Id = D, Name = "D" })
            .MitKonto(new Konto { Id = E, Name = "E", Felder = [F(0, Crew, true, false), F(1, Kommando, true, false)] });
        foreach (var k in new[] { A, B, C, D, E }) bauer.MitSitzung(S(k), k, k * 10);
        return bauer;
    }

    public Weiterleitungsergebnis Senden(uint konto, Sendeart art, ushort[] ziele, bool va = false, ushort phase = 1, uint frame = 100,
        bool ende = false, Paketkennzeichen extra = Paketkennzeichen.Keine)
    {
        var kz = (va ? Paketkennzeichen.Va : 0) | (ende ? Paketkennzeichen.Ende : 0) | extra;
        var paket = new Sprachpaket(S(konto), frame, phase, kz, art, ziele, ende ? [] : Opus);
        return Relay.Verarbeiten(Z, paket, Jetzt, M);
    }

    public Ergebnis Tun(Auftrag auftrag) => Schreiber.Ausfuehren(auftrag, Jetzt);

    /// <summary>
    /// Auftrag für die Einsatzleitung durch den Admin, 12 h ab jetzt (6.12). Seit 0.3.0 teilt die Stufe RAIDLEITER (Führung)
    /// nicht mehr selbst ein; wo A in den Prüfwerten einteilt, tut er es im Auftrag.
    /// </summary>
    public Funkprobe MitAuftrag(params uint[] konten)
    {
        foreach (var k in konten)
        {
            var r = Tun(Auftrag.BeauftragungErteilen(Admin, k, null, Jetzt + 12L * 60 * 60 * 1000));
            Pruefe.Wahr(r.Erfolg, $"Auftrag für {k}: {r}");
        }
        return this;
    }

    /// <summary>Umstellung durch den Admin ohne Sitzung: ein Feld leeren.</summary>
    public Ergebnis FeldNehmen(uint konto, byte feld) =>
        Tun(Auftrag.Verschieben(Admin, [new Aenderung(konto) { FelderPatch = [Feldpatch.Leer(feld)] }]));

    public Ergebnis Stumm(uint konto, ushort netz, int sekunden = 60) => Tun(Auftrag.Stummschalten(Admin, konto, netz, sekunden));
}

/// <summary>Prüfwerte 8.9 (Rechte-Matrix, P-08): Senderecht und Empfänger, deterministisch.</summary>
internal static class WeiterleitungRechteTests
{
    private static void Empfang(Funkprobe f, uint sender, Weiterleitungsergebnis r, string erwartet)
    {
        Pruefe.Falsch(r.Verworfen, $"nicht verworfen (Grund {r.Grund})");
        Pruefe.Gleich(erwartet, f.M.Empfang(), "Empfänger: Art [Netze]");
        Pruefe.Wahr(f.M.Sprache.TrueForAll(s => (s.Stempel.Kennzeichen & Paketkennzeichen.Beginn) != 0), "erstes Paket einer Phase trägt BEGINN");
        Pruefe.Falsch(f.M.Sprache.Exists(s => s.Stempel.Empfaenger == Funkprobe.S(sender)), "Sender bekommt sein Paket nie");
        Pruefe.Wahr(f.M.Sprache.TrueForAll(s => s.Stempel.Absender == sender), "Absender gestempelt");
        Pruefe.Gleich(f.M.Sprache.Count, f.M.Sprache.Select(s => s.Stempel.Empfaenger).Distinct().Count(), "höchstens eine Kopie je Empfänger");
    }

    private static void Verworfen(Funkprobe f, uint sender, Weiterleitungsergebnis r, SendeFehlerCode code)
    {
        Pruefe.Wahr(r.Verworfen, "verworfen");
        Pruefe.Gleich(code, r.Grund, "Grund");
        Pruefe.Gleich(0, f.M.Sprache.Count, "keine Kopie");
        Pruefe.Gleich(1, f.M.Fehler.Count, "ein SendeFehler");
        var (sitzung, fehler, teilweise, verworfen) = f.M.Fehler[0];
        Pruefe.Gleich(Funkprobe.S(sender), sitzung, "SendeFehler an den Sender");
        Pruefe.Gleich(code, fehler[0].Code, "Code im SendeFehler");
        Pruefe.Falsch(teilweise, "nicht teilweise");
        Pruefe.Gleich(1u, verworfen, "ein ganz verworfenes Paket");
    }

    private static void Teilweise(Funkprobe f, uint sender, SendeFehlerCode code, ushort netz, uint durch = 0)
    {
        var fehler = f.M.Fehler.Where(x => x.Sitzung == Funkprobe.S(sender)).ToList();
        Pruefe.Gleich(1, fehler.Count, "ein SendeFehler");
        Pruefe.Wahr(fehler[0].Teilweise, "teilweise");
        Pruefe.Gleich(0u, fehler[0].Verworfen, "nichts ganz verworfen");
        Pruefe.Gleich(1, fehler[0].Fehler.Length, "ein Eintrag");
        Pruefe.Gleich(code, fehler[0].Fehler[0].Code, "Code");
        Pruefe.Gleich(netz, fehler[0].Fehler[0].NetzId, "Netz");
        Pruefe.Gleich(durch, fehler[0].Fehler[0].Durch, "durch");
    }

    [Test]
    public static void Fall01_B_Netz7_Ptt()
    {
        var f = new Funkprobe();
        Empfang(f, Funkprobe.B, f.Senden(Funkprobe.B, Sendeart.Netz, [7]), "A:NETZ[7] E:NETZ[7]");
    }

    [Test]
    public static void Fall02_B_Netz8_Va_NurTaste()
    {
        var f = new Funkprobe();
        Verworfen(f, Funkprobe.B, f.Senden(Funkprobe.B, Sendeart.Netz, [8], va: true), SendeFehlerCode.NurTaste);
    }

    [Test]
    public static void Fall03_B_Netz8_Ptt()
    {
        var f = new Funkprobe();
        Empfang(f, Funkprobe.B, f.Senden(Funkprobe.B, Sendeart.Netz, [8]), "C:NETZ[8]");
    }

    [Test]
    public static void Fall04_C_Netz2_KeinSenderecht()
    {
        var f = new Funkprobe();
        Verworfen(f, Funkprobe.C, f.Senden(Funkprobe.C, Sendeart.Netz, [2]), SendeFehlerCode.KeinSenderecht);
    }

    [Test]
    public static void Fall05_B_Global_KeinRechtGlobal()
    {
        var f = new Funkprobe();
        Verworfen(f, Funkprobe.B, f.Senden(Funkprobe.B, Sendeart.Netz, [1]), SendeFehlerCode.KeinRechtGlobal);
    }

    [Test]
    public static void Fall06_A_Global_Ptt()
    {
        var f = new Funkprobe();
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [1]), "B:GLOBAL[1] C:GLOBAL[1] D:GLOBAL[1] E:GLOBAL[1]");
    }

    [Test]
    public static void Fall07_A_Netz2und7_FeldfolgeDesEmpfaengers()
    {
        var f = new Funkprobe();
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [2, 7]), "B:NETZ[7] C:NETZ[2] E:NETZ[7,2]");
    }

    [Test]
    public static void Fall08_B_Netz7und8_MehrfachNichtErlaubt()
    {
        var f = new Funkprobe();
        Verworfen(f, Funkprobe.B, f.Senden(Funkprobe.B, Sendeart.Netz, [7, 8]), SendeFehlerCode.MehrfachNichtErlaubt);
    }

    [Test]
    public static void Fall09_A_Durchsage7_AuchOhneHoeren()
    {
        var f = new Funkprobe();
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Durchsage, [7]), "B:DURCHSAGE[7] C:DURCHSAGE[7] E:DURCHSAGE[7]");
        Pruefe.Gleich(3, f.M.Starts.Count, "DurchsageStart je Empfänger");
        Pruefe.Wahr(f.M.Starts.TrueForAll(s => s.Sprecher == Funkprobe.A && s.Art == Stempelart.Durchsage && s.Netze.SequenceEqual(new ushort[] { 7 })),
            "DurchsageStart{1001, DURCHSAGE, [7]}");
    }

    [Test]
    public static void Fall10_A_DurchsageAnAlle()
    {
        var f = new Funkprobe();
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Durchsage, []), "B:DURCHSAGE[] C:DURCHSAGE[] D:DURCHSAGE[] E:DURCHSAGE[]");
        Pruefe.Wahr(f.M.Starts.Count == 4 && f.M.Starts.TrueForAll(s => s.Netze.Length == 0), "DurchsageStart mit leerer Netzliste");
    }

    [Test]
    public static void Fall11_D_DurchsageAnAlle_KeinRecht()
    {
        var f = new Funkprobe();
        Verworfen(f, Funkprobe.D, f.Senden(Funkprobe.D, Sendeart.Durchsage, []), SendeFehlerCode.KeinRechtDurchsage);
    }

    [Test]
    public static void Fall12_A_Unsortiert_Ungueltig()
    {
        var f = new Funkprobe();
        Verworfen(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [7, 2]), SendeFehlerCode.Ungueltig);
    }

    [Test]
    public static void Fall13_A_GlobalUndCommand()
    {
        var f = new Funkprobe();
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [1, 2]), "B:GLOBAL[1] C:GLOBAL[1,2] D:GLOBAL[1] E:GLOBAL[1,2]");
        Pruefe.Gleich(4, f.M.Starts.Count, "DurchsageStart{GLOBAL} je Empfänger");
    }

    [Test]
    public static void Fall14_NachVerschieben_CVerliertFeld0()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.FeldNehmen(Funkprobe.C, 0).Erfolg, "Verschieben");
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [2]), "E:NETZ[2]");
    }

    [Test]
    public static void Fall15_Stumm7_DannNetz7Verworfen_Netz2Durch()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Stumm(Funkprobe.A, 7).Erfolg, "Stummschalten");
        Verworfen(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [7]), SendeFehlerCode.Stummgeschaltet);
        Pruefe.Gleich(Funkprobe.Admin, f.M.Fehler[0].Fehler[0].Durch, "durch Admin");
        f.M.Leeren();
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [2], phase: 2), "C:NETZ[2] E:NETZ[2]");
    }

    [Test]
    public static void Fall16_A_DurchsageAnGlobal_Ungueltig()
    {
        var f = new Funkprobe();
        Verworfen(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Durchsage, [1]), SendeFehlerCode.Ungueltig);
    }

    [Test]
    public static void Fall17_A_Global_Va_NurTaste()
    {
        var f = new Funkprobe();
        Verworfen(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [1], va: true), SendeFehlerCode.NurTaste);
    }

    [Test]
    public static void Fall18_A_Durchsage_Va_NurTaste()
    {
        var f = new Funkprobe();
        Verworfen(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Durchsage, [7], va: true), SendeFehlerCode.NurTaste);
    }

    [Test]
    public static void Fall19_Stumm7_Netz2und7_Teilweise()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Stumm(Funkprobe.A, 7).Erfolg, "Stummschalten");
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [2, 7]), "C:NETZ[2] E:NETZ[2]");
        Teilweise(f, Funkprobe.A, SendeFehlerCode.Stummgeschaltet, 7, Funkprobe.Admin);
    }

    [Test]
    public static void Fall20_UnbekanntesNetz99_Teilweise()
    {
        var f = new Funkprobe();
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [2, 99]), "C:NETZ[2] E:NETZ[2]");
        Teilweise(f, Funkprobe.A, SendeFehlerCode.UnbekanntesNetz, 99);
    }

    [Test]
    public static void Fall21_Stumm7_Durchsage2und7_Teilweise()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Stumm(Funkprobe.A, 7).Erfolg, "Stummschalten");
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Durchsage, [2, 7]), "C:DURCHSAGE[2] E:DURCHSAGE[2]");
        Teilweise(f, Funkprobe.A, SendeFehlerCode.Stummgeschaltet, 7, Funkprobe.Admin);
    }

    [Test]
    public static void Fall22_StummAlle_DurchsageAnAlle_Verworfen()
    {
        var f = new Funkprobe();
        Pruefe.Wahr(f.Stumm(Funkprobe.A, 0).Erfolg, "Stummschalten für alle");
        Verworfen(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Durchsage, []), SendeFehlerCode.Stummgeschaltet);
    }

    [Test]
    public static void Fall23_NachVerschieben_AVerliertFeld1_ZielEntzogenSofort()
    {
        var f = new Funkprobe();
        // Ein SendeFehler kurz vorher: die Drossel (1 je Sekunde) darf ZIEL_ENTZOGEN nicht aufhalten.
        f.Senden(Funkprobe.A, Sendeart.Netz, [99], phase: 7);
        Pruefe.Gleich(1, f.M.Fehler.Count, "Vorlauf: ein SendeFehler");
        f.M.Leeren();
        var umstellung = f.FeldNehmen(Funkprobe.A, 1);
        Pruefe.Wahr(umstellung.Erfolg, "Verschieben");
        f.Jetzt += 5;
        Empfang(f, Funkprobe.A, f.Senden(Funkprobe.A, Sendeart.Netz, [2, 7]), "C:NETZ[2] E:NETZ[2]");
        Teilweise(f, Funkprobe.A, SendeFehlerCode.ZielEntzogen, 7, Funkprobe.Admin);
        Pruefe.Gleich(umstellung.ZustandsVersion, f.M.Fehler[0].Fehler[0].Umstellung, "Umstellung benannt");

        // Einmal je Netz und Umstellung: das nächste Paket derselben Phase wartet auf die Drossel.
        f.M.Leeren();
        f.Jetzt += 20;
        f.Senden(Funkprobe.A, Sendeart.Netz, [2, 7], frame: 101);
        Pruefe.Gleich(0, f.M.Fehler.Count, "kein zweites sofortiges ZIEL_ENTZOGEN");
        // Nach 10 s ohne Umstellung heißt es wieder KEIN_SENDERECHT.
        f.Jetzt += Fristen.EntzugMs + 1;
        f.M.Leeren();
        f.Senden(Funkprobe.A, Sendeart.Netz, [2, 7], frame: 102);
        Pruefe.Wahr(f.M.Fehler.Count == 1 && f.M.Fehler[0].Fehler.Any(x => x.Code == SendeFehlerCode.KeinSenderecht && x.NetzId == 7),
            "nach 10 s KEIN_SENDERECHT");
    }
}
