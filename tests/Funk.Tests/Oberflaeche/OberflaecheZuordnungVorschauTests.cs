// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using static Funk.Client.Dienste.FunkDienstAttrappe;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Ziehen mit Vorschau, sanft (0.11.1 AP2, Sarevocs Punkt 4): Beim Ziehen zeigt der erste freie Platz des Ziels die gezogene Person gedämpft
/// (Deckung 0,45), weich ein- und ausgeblendet; beim Loslassen gleitet die Karte in etwa 150 ms vom Ziehbild an ihren Platz. Erst das Modell
/// über der Attrappe mit dem Beispielabend (welcher Platz, nur bei grün, wann weg, der Wechsel vom Block auf den Spaltenkopf mit demselben
/// Platz, die Zeile nur für die Vorschau in einem Block ohne Platzgrenze, die Landung als Ereignis). Bis 0.11.1 dazu im Bildlauf die Deckung,
/// das Bild mit der Vorschau in der Zielzeile und die Landung; seit 0.11.2 zeigt die Raid-Seite keine Plätze mehr (Stammbaum).
/// </summary>
internal static class OberflaecheZuordnungVorschauTests
{
    private static RollenBlock Block(BrettViewModel b, ushort? schiff, ushort rolle) => OberflaecheBrettTests.Block(b, schiff, rolle);

    private static EinheitSpalte Spalte(BrettViewModel b, ushort schiff) => b.Gruppen.SelectMany(g => g.Spalten).Single(s => s.SchiffId == schiff);

    private static FreierPlatz? Erster(RollenBlock block) => block.Eintraege.OfType<FreierPlatz>().FirstOrDefault();

    private static IEnumerable<FreierPlatz> AlleFreien(BrettViewModel b) =>
        b.Gruppen.SelectMany(g => g.Spalten).SelectMany(s => s.Bloecke).SelectMany(x => x.Eintraege.OfType<FreierPlatz>());

    /// <summary>Genau <paramref name="nur"/> zeigt die Vorschau (oder keiner, wenn <c>null</c>).</summary>
    private static bool Sichtbare(BrettViewModel b, FreierPlatz? nur) => AlleFreien(b).All(p => p.VorschauSichtbar == ReferenceEquals(p, nur));

    // ---------------------------------------------------------------- Modell

    [Test]
    public static void Vorschau_Modell_ErsterFreierPlatzDesZiels_NurBeiGruen_WechseltUndBlendetAus()
    {
        var a = new Aufbau(dienst: MitRaidabend());
        using var _ = a;
        var b = a.Brett();
        var pollux = b.Karte(Pollux)!;
        var marine = Block(b, Schiff1, Marine);
        var gunner = Block(b, Schiff1, Gunner);
        var pilot = Block(b, Schiff1, Pilot);
        var platz = Erster(marine)!;
        Pruefe.Wahr(marine.Eintraege.OfType<FreierPlatz>().Count() == 2 && Erster(gunner) is not null && Erster(pilot) is null, "Marine zwei frei, Gunner einer, Pilot voll");
        Pruefe.Wahr(Sichtbare(b, null) && AlleFreien(b).All(p => p.Vorschau is null), "ohne Ziehen keine Vorschau");
        var landungen = new List<Landung>();
        b.Gelandet += (_, l) => landungen.Add(l);

        b.Ziehen.StartDrag(new Ziehinfo(pollux));
        var ueber = new Ablageinfo(pollux, null);
        marine.DragOver(ueber);
        Pruefe.Wahr(ReferenceEquals(platz.Vorschau, pollux) && platz.VorschauSichtbar && Sichtbare(b, platz), "die Vorschau am ersten freien Platz von Marine, sonst nirgends");
        Pruefe.Wahr(marine.Ablagezustand == Ablagewirkung.Passt && marine.AblageText.StartsWith("passt: Schiff 1, Marine", StringComparison.Ordinal), "der Satz bleibt gruen am Block: " + marine.AblageText);
        marine.DragOver(ueber);
        Pruefe.Wahr(platz.VorschauSichtbar && Sichtbare(b, platz), "jede Mausbewegung ruft das: bleibt stehen");

        // Spaltenkopf: der beste Platz der Einheit (fuer Pollux Gunner); Marine blendet aus und behaelt die Karte fuers Ausblenden.
        Spalte(b, Schiff1).DragOver(ueber);
        Pruefe.Wahr(!platz.VorschauSichtbar && ReferenceEquals(platz.Vorschau, pollux), "Marine blendet aus, die Karte bleibt bis dahin");
        Pruefe.Wahr(Erster(gunner)!.VorschauSichtbar && ReferenceEquals(Erster(gunner)!.Vorschau, pollux) && Sichtbare(b, Erster(gunner)), "Spaltenkopf: die Vorschau am besten Platz, Gunner");

        // Nur bei gruen: rot am vollen Block, gelb beim Tausch, rot am Admin; kein Platz auf einem Kanal rechts oder in der Liste links.
        pilot.DragOver(new Ablageinfo(b.Karte(Zinnober)!, null));
        Pruefe.Wahr(pilot.Ablagezustand == Ablagewirkung.GehtNicht && Sichtbare(b, null), "voller Block: rot, keine Vorschau");
        pilot.DragOver(new Ablageinfo(b.Karte(Falke)!, b.Karte(Kobold)));
        Pruefe.Wahr(pilot.Ablagezustand == Ablagewirkung.Tausch && Sichtbare(b, null), "Tausch: die gelbe Karte, keine Vorschau");
        marine.DragOver(new Ablageinfo(b.Karte(Nordwind)!, null));
        Pruefe.Wahr(marine.Ablagezustand == Ablagewirkung.GehtNicht && Sichtbare(b, null), "Admin: rot trotz freiem Platz, keine Vorschau");
        marine.DragOver(ueber);
        Pruefe.Wahr(Sichtbare(b, platz), "wieder Marine");
        var orga = b.Sondernetze.Single(n => n.NetzId == NetzOrga);
        orga.Hoeren.DragOver(ueber);
        Pruefe.Wahr(orga.Hoeren.Ablagezustand == Ablagewirkung.Passt && Sichtbare(b, null), "auf einem Kanal rechts: gruen, aber keine Vorschau an einem Platz");
        marine.DragOver(ueber);
        b.Teilnehmerziel.DragOver(ueber);
        Pruefe.Wahr(Sichtbare(b, null), "in der Liste links: keine Vorschau");

        // Verlassen: nur das eigene Ziel (oder die Spalte darum) nimmt sie weg; weich, die Karte bleibt.
        marine.DragOver(ueber);
        gunner.DragLeave(ueber);
        Pruefe.Wahr(platz.VorschauSichtbar, "ein anderes Ziel verlassen: bleibt");
        Spalte(b, Schiff1).DragLeave(ueber);
        Pruefe.Wahr(!platz.VorschauSichtbar && ReferenceEquals(platz.Vorschau, pollux), "die Spalte um den Block verlassen: blendet aus, Karte bleibt");
        marine.DragOver(ueber);
        marine.DragLeave(ueber);
        Pruefe.Wahr(!platz.VorschauSichtbar && ReferenceEquals(platz.Vorschau, pollux), "den Block verlassen: blendet aus, Karte bleibt");

        // Abbruch (Esc): weich weg.
        marine.DragOver(ueber);
        b.Ziehen.DragCancelled();
        Pruefe.Wahr(!platz.VorschauSichtbar && ReferenceEquals(platz.Vorschau, pollux), "Abbruch: blendet aus");

        // Vom Block auf den Spaltenkopf, dessen bester Platz derselbe Block ist (Hagel: zuletzt Marine): gong meldet das Verlassen des Blocks erst
        // nach dem Betreten des Kopfs; die Vorschau bleibt, bis die Spalte verlassen wird (Befund der Prüfung: sie blendete aus und blieb weg).
        var hagel = b.Karte(Hagel)!;
        var hagelUeber = new Ablageinfo(hagel, null);
        b.Ziehen.StartDrag(new Ziehinfo(hagel));
        marine.DragOver(hagelUeber);
        Pruefe.Wahr(ReferenceEquals(platz.Vorschau, hagel) && platz.VorschauSichtbar, "Hagel in Marine");
        Spalte(b, Schiff1).DragOver(hagelUeber);
        Pruefe.Wahr(b.Bewerten(hagel, Spalte(b, Schiff1)).Text.StartsWith("passt: Schiff 1, Marine", StringComparison.Ordinal) && platz.VorschauSichtbar && Sichtbare(b, platz),
            "der beste Platz der Spalte ist Marine: dieselbe Vorschau");
        marine.DragLeave(hagelUeber);
        Pruefe.Wahr(platz.VorschauSichtbar, "das nachgereichte Verlassen des Blocks lässt sie stehen");
        Spalte(b, Schiff1).DragLeave(hagelUeber);
        Pruefe.Wahr(!platz.VorschauSichtbar && ReferenceEquals(platz.Vorschau, hagel), "die Spalte verlassen: blendet aus");
        // Umgekehrt (Kopf, dann Block, dann der Block verlassen): der Block hat sie zuletzt bestätigt und nimmt sie.
        Spalte(b, Schiff1).DragOver(hagelUeber);
        marine.DragOver(hagelUeber);
        marine.DragLeave(hagelUeber);
        Pruefe.Wahr(!platz.VorschauSichtbar, "vom Kopf in den Block und hinaus: fort");
        b.Ziehen.DragCancelled();

        // Eine Person aus der Kanalliste rechts auf einen Platz: die Vorschau mit ihrer Karte.
        var command = b.Sondernetze.Single(n => n.NetzId == NetzKommando);
        var person = command.Personen.First(p => b.Bewerten(p, marine).Wirkung == Ablagewirkung.Passt);
        b.Ziehen.StartDrag(new Ziehinfo(person));
        marine.DragOver(new Ablageinfo(person, null));
        Pruefe.Wahr(ReferenceEquals(platz.Vorschau, person.Karte) && Sichtbare(b, platz), "Person aus der Kanalliste: ihre Karte als Vorschau");
        b.Ziehen.DragCancelled();
        Pruefe.Wahr(Sichtbare(b, null) && landungen.Count == 0, "noch keine Landung");

        // Loslassen: sofort weg (die Karte kommt an die Stelle, die Luecke rueckt tiefer), die Karte steht im Block, die Landung ist gemeldet.
        b.Ziehen.StartDrag(new Ziehinfo(pollux));
        marine.DragOver(ueber);
        marine.Drop(ueber);
        Pruefe.Wahr(platz.Vorschau is null && !platz.VorschauSichtbar && Sichtbare(b, null), "Loslassen: sofort weg, ohne Karte");
        Pruefe.Wahr(marine.Eintraege.Contains(pollux), "Pollux steht in Marine (die Attrappe antwortet sofort, sonst mit wartet)");
        Pruefe.Wahr(landungen.Count == 1 && ReferenceEquals(landungen[0].Karte, pollux) && ReferenceEquals(landungen[0].Block, marine) && landungen[0].Punkt.Bezug is null,
            "die Landung meldet Karte und Block, ohne Bezugselement");
        Pruefe.Wahr(SpinWait.SpinUntil(() => a.Dienst.Aufrufe.Any(x => x.StartsWith("Verschieben 14:4/1 ", StringComparison.Ordinal)), 5000), "Verschieben auf Marine: " + string.Join(" | ", a.Dienst.Aufrufe));
        b.Ziehen.DragDropOperationFinished(System.Windows.DragDropEffects.Move, new Ziehinfo(pollux));
        Pruefe.Wahr(Sichtbare(b, null), "nach dem Ziehen nichts");

        // Tausch: die Landung nennt den Block der anderen Karte; nach links und auf einen Kanal keine Landung.
        var kobold = b.Karte(Kobold)!;
        pilot.Drop(new Ablageinfo(b.Karte(Falke)!, kobold));
        Pruefe.Wahr(landungen.Count == 2 && ReferenceEquals(landungen[1].Karte, b.Karte(Falke)) && ReferenceEquals(landungen[1].Block, pilot), "Tausch: Landung im Block Pilot");
        b.Teilnehmerziel.Drop(new Ablageinfo(b.Karte(Hagel)!, null));
        orga.Hoeren.Drop(new Ablageinfo(b.Karte(Eule)!, null));
        Pruefe.Gleich(2, landungen.Count, "nach links und auf einen Kanal gleitet nichts");

        // Ein Block ohne Platzgrenze (eine Flotte aus der Kanalvorlage: Rollen ohne Einheit und ohne Kapazität) hat keine freien Plätze (Befund der Prüfung:
        // dort gab es keine Vorschau). Die Vorschau bekommt eine Zeile nur für sich, dort, wo die Karte nach Namen stünde; sie bleibt über einen
        // Push hinweg (jede Sprechanzeige), geht beim Verlassen sofort und ohne Blende, und beim Loslassen kommt die Karte an ihre Stelle.
        a.Dienst.LageSetzen(l => l with
        {
            Rollen = [.. l.Rollen.Select(r => r.Id == Flottenfuehrung ? r with { Kapazitaet = null } : r)],
            Funker = [.. l.Funker.Select(f => f.KontoId is Anker or Quarz ? f with { SchiffId = null, RolleId = Flottenfuehrung } : f)],
        });
        var fuehrung = Block(b, null, Flottenfuehrung);
        Pruefe.Wahr(fuehrung.Eintraege.Count == 2 && !fuehrung.Eintraege.OfType<FreierPlatz>().Any() && fuehrung.Belegung == "2",
            "Anker und Quarz, keine freien Plätze, Belegung ohne Grenze: " + fuehrung.Belegung);
        b.Ziehen.StartDrag(new Ziehinfo(pollux));
        fuehrung.DragOver(ueber);
        Pruefe.Wahr(fuehrung.Ablagezustand == Ablagewirkung.Passt, "grün: " + fuehrung.AblageText);
        var zeile = fuehrung.Eintraege.OfType<FreierPlatz>().SingleOrDefault() ?? throw new PruefungException("keine Zeile für die Vorschau: " + string.Join("|", fuehrung.Eintraege));
        Pruefe.Wahr(ReferenceEquals(zeile.Vorschau, pollux) && zeile.VorschauSichtbar && fuehrung.Eintraege.IndexOf(zeile) == 1 && Sichtbare(b, zeile),
            "eine Zeile nur für die Vorschau, zwischen Anker und Quarz: " + string.Join("|", fuehrung.Eintraege));
        a.Dienst.LageSetzen(l => l with { Sprecher = [.. l.Sprecher, new SprecherInfo(Kobold, "Kobold", [13], false)] });
        Pruefe.Wahr(fuehrung.Eintraege.Count == 3 && ReferenceEquals(fuehrung.Eintraege[1], zeile) && zeile.VorschauSichtbar, "nach einem Push steht die Zeile noch");
        fuehrung.DragOver(ueber);
        Pruefe.Wahr(fuehrung.Eintraege.Count == 3 && ReferenceEquals(fuehrung.Eintraege[1], zeile), "jede Mausbewegung: dieselbe Zeile");
        fuehrung.DragLeave(ueber);
        Pruefe.Wahr(fuehrung.Eintraege.Count == 2 && !fuehrung.Eintraege.Contains(zeile) && zeile.Vorschau is null && !zeile.VorschauSichtbar && Sichtbare(b, null),
            "verlassen: die Zeile ist fort, ohne Blende");
        fuehrung.DragOver(ueber);
        Pruefe.Wahr(ReferenceEquals(fuehrung.Eintraege[1], zeile) && zeile.VorschauSichtbar, "wieder darüber: dieselbe Zeile wieder");
        fuehrung.Drop(ueber);
        Pruefe.Wahr(!fuehrung.Eintraege.Contains(zeile) && zeile.Vorschau is null && fuehrung.Eintraege.IndexOf(pollux) == 1 && !fuehrung.Eintraege.OfType<FreierPlatz>().Any(),
            "Loslassen: Pollux steht an der Stelle der Zeile, keine Zeile mehr: " + string.Join("|", fuehrung.Eintraege));
        Pruefe.Wahr(landungen.Count == 3 && ReferenceEquals(landungen[2].Karte, pollux) && ReferenceEquals(landungen[2].Block, fuehrung), "die Landung im Block ohne Grenze");
        Pruefe.Wahr(SpinWait.SpinUntil(() => a.Dienst.Aufrufe.Any(x => x.StartsWith("Verschieben 14:9/- ", StringComparison.Ordinal)), 5000), "Verschieben auf Flottenführung: " + string.Join(" | ", a.Dienst.Aufrufe));
        b.Ziehen.DragDropOperationFinished(System.Windows.DragDropEffects.Move, new Ziehinfo(pollux));
        Pruefe.Wahr(Sichtbare(b, null) && !fuehrung.Eintraege.OfType<FreierPlatz>().Any(), "nach dem Ziehen nichts, und ohne Grenze keine Zeile");
    }

    // 0.11.2: Gelöscht sind Vorschau_Bildlauf_Deckung045_WeichEinUndAus_BildInDerZielzeile (die Vorschau in der Zeile eines freien Platzes, gezeichnet)
    // und Vorschau_Landung_GleitetVomLoslasspunktAnDenPlatz_EndetDort_KeineUhrDanach (die Karte gleitet in ihren Block). Die Raid-Seite zeigt seit 0.11.2
    // keine Einheiten, Plätze und freien Plätze mehr (Sarevoc 13.10.2026, Stammbaum); das Modell der Vorschau und das Ereignis Gelandet prüft weiter
    // Vorschau_Modell_ErsterFreierPlatzDesZiels_NurBeiGruen_WechseltUndBlendetAus.
}
