// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using Funk.Server.Lage;
using Kanalrechnung = Funk.Protokoll.Steuerung.Kanalrechnung;

namespace Funk.Tests;

/// <summary>
/// Kleine Flotte mit Kanalnetzen für die Lagetests der Stufe 3 (6.16), auf dem Grundzustand aus 8.9 (<see cref="Funkprobe"/>):
/// Global als Kanalnetz ("Ruhe", "Gesamt", "Global 1" bis "Global 20", offen), Brücke (offen, Schiffe), Kommando (offen, nur
/// Taste), Crew (zu, nur über die Rolle) und Jäger (nur hören, 25 Einträge bis "Staffel 4-Foxtrot"); die Rollen Kapitän,
/// eine feste und eine nicht selbst wählbare, Jägerpilot und eine mit Kennungen; Schiff 4 gibt über seine Kennungen Ch. 4 vor. F, G und H
/// sind Funker mit Sitzung und ohne Felder.
/// </summary>
internal static class Kanalprobe
{
    public const ushort KanalBruecke = 100;
    public const ushort Kommando = 101;
    public const ushort KanalCrew = 102;
    public const ushort Jaeger = 103;

    public const ushort Co = 12;
    public const ushort FestRolle = 14;
    public const ushort JaegerRolle = 15;
    public const ushort BrueckeRolle = 16;
    public const ushort Schiff4 = 4;

    public const uint F = 1006;
    public const uint G = 1007;
    public const uint H = 1008;

    public static ushort K(ushort netz, byte ch) => Kanalrechnung.Id(netz, ch);

    public static ImmutableArray<string> Liste(string vorne, int anzahl) => ["Ruhe", .. Enumerable.Range(1, anzahl).Select(i => vorne + " " + i)];

    private static readonly string[] Buchstaben = ["Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot"];

    public static ImmutableArray<string> JaegerListe() =>
        ["Ruhe", .. Enumerable.Range(1, 4).SelectMany(t => Buchstaben.Select(b => $"Staffel {t}-{b}"))];

    public static RollenFeld Rf(byte feld, ushort netz, bool hoeren = true, bool senden = true, bool standard = false, bool fest = false, byte? ch = null) =>
        new(feld, netz, null, hoeren, senden, standard, fest) { Ch = ch };

    public static RollenFeld Kennung(byte feld, string kennung, bool hoeren = true, bool senden = true, bool standard = false) =>
        new(feld, 0, kennung, hoeren, senden, standard, false);

    public static Funkprobe Neu(Func<SchnappschussBauer, SchnappschussBauer>? mehr = null) => new(b =>
    {
        b.MitNetz(Netz.Global() with { Kanaele = ["Ruhe", "Gesamt", .. Enumerable.Range(1, 20).Select(i => "Global " + i)], Offen = Offenheit.HoerenSenden })
            .MitNetz(new Netz(KanalBruecke, "Brücke") { Kanaele = Liste("Schiff", 20), Offen = Offenheit.HoerenSenden, Gruppe = "Schiffe", Reihenfolge = 10, AusloeserStandard = Ausloeser.Va })
            .MitNetz(new Netz(Kommando, "Kommando") { Kanaele = Liste("Kommando", 20), Offen = Offenheit.HoerenSenden, NurTaste = true, Reihenfolge = 11 })
            .MitNetz(new Netz(KanalCrew, "Crew") { Kanaele = Liste("Schiff", 20), Offen = Offenheit.Nein, Reihenfolge = 12 })
            .MitNetz(new Netz(Jaeger, "Jäger") { Kanaele = JaegerListe(), Offen = Offenheit.Hoeren, Gruppe = "Jäger", Reihenfolge = 13 })
            .MitRolle(new Rolle(Co, "Kapitän")
            {
                SelbstWaehlbar = true,
                Felder = [Rf(0, Kommando, standard: true), Rf(1, KanalBruecke), Rf(2, KanalCrew)],
            })
            .MitRolle(new Rolle(FestRolle, "Feste Brücke") { Felder = [Rf(0, KanalBruecke, fest: true, ch: 5)] })
            .MitRolle(new Rolle(JaegerRolle, "Jägerpilot")
            {
                SelbstWaehlbar = true,
                Felder = [Rf(0, Jaeger, senden: false), Rf(1, KanalBruecke, standard: true, ch: 2), Rf(2, Netz.GlobalId, senden: false, ch: 2)],
            })
            .MitRolle(new Rolle(BrueckeRolle, "Brücke Schiff")
            {
                SelbstWaehlbar = true,
                BrauchtSchiff = true,
                Felder = [Kennung(0, "BRIDGE", standard: true), Rf(1, KanalCrew, senden: false)],
            })
            .MitSchiff(new Schiff(Schiff4, "Schiff 4")
            {
                Netze = ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
                    [new KeyValuePair<string, ushort>("BRIDGE", K(KanalBruecke, 4)), new KeyValuePair<string, ushort>("CREW", K(KanalCrew, 4))]),
                Plaetze = [new SchiffsPlatz(Co, 4), new SchiffsPlatz(BrueckeRolle, 4)],
            });
        foreach (var (id, name) in new[] { (F, "F"), (G, "G"), (H, "H") })
            b.MitKonto(new Konto { Id = id, Name = name }).MitSitzung(Funkprobe.S(id), id, id * 10);
        return mehr?.Invoke(b) ?? b;
    });

    public static Ergebnis Ok(Funkprobe f, Auftrag a, string was)
    {
        var r = f.Tun(a);
        Pruefe.Wahr(r.Erfolg, was + ": " + r);
        return r;
    }

    public static Fehlercode Code(Funkprobe f, Auftrag a) => f.Tun(a).Fehler;

    public static Konto Konto(Funkprobe f, uint id) => f.Z.Konten[id];

    /// <summary>Die Felder eines Kontos als "Feld:Netz/Ch.hs" (Kanäle) bzw. "Feld:Netz.hs" (Netze), h = hören, s = senden, f = fest.</summary>
    public static string Felder(Funkprobe f, uint id) => string.Join(" ", f.Z.Konten[id].Felder.Select(m =>
        m.Feld + ":" + (Kanalrechnung.Zerlegen(m.NetzId, out var n, out var c) ? n + "/" + c : m.NetzId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        + (m.Hoeren ? "h" : "") + (m.Senden ? "s" : "") + (m.Fest ? "f" : "")));
}
