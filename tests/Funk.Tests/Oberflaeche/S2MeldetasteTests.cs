// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Klang;
using Funk.ClientKern;
using Funk.Klang.Verarbeitung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Ausbaustufe S2, Teil A4 (RAID-S2-ENTWURF Abschnitt 4): Die Melde-Taste spricht, solange sie gehalten ist, ins Meldenetz (etwa
/// Kommando), ohne die Sendeauswahl zu ändern; nach dem Loslassen geht der nächste Spruch wieder an die Crew. Hält man dabei die
/// Sprechtaste, endet die laufende Sprechphase und eine neue beginnt am Meldenetz; die Sprachaktivierung ruht; ohne Senderecht
/// geht kein Paket hinaus, und die Meldezeile sagt warum.
/// </summary>
internal static class S2MeldetasteTests
{
    private const ushort Kommando = 2;
    private const ushort Crew = 22;

    private sealed class Aufbau : IDisposable
    {
        public Aufbau(string? hindernis = null)
        {
            Bruecke = new KlangBruecke(Maschine);
            Bruecke.Anbinden(Weg);
            Steuerung = new Sprechsteuerung(Maschine, Bruecke, () => new MeldeEntscheid(Kommando, "Kommando", Hindernis))
            {
                SprachaktivierungRuht = ruht => Ruht.Add(ruht),
            };
            Hindernis = hindernis;
            Steuerung.MeldenBegonnen += n => Meldungen.Add("an " + n);
            Steuerung.MeldenBeendet += () => Meldungen.Add("Ende");
            Steuerung.MeldenGehtNicht += t => Meldungen.Add(t);
        }

        public KlangMaschinenAttrappe Maschine { get; } = new();

        public FunkwegAttrappe Weg { get; } = new() { Ziel = Sendeziel.Netz(Crew) };

        public KlangBruecke Bruecke { get; }

        public Sprechsteuerung Steuerung { get; }

        public string? Hindernis { get; set; }

        public List<bool> Ruht { get; } = [];

        public List<string> Meldungen { get; } = [];

        public void Rahmen(Sprachkennzeichen k) => Maschine.SendenSimulieren(k, k.HasFlag(Sprachkennzeichen.Ende) ? [] : new byte[80], 7);

        public string Folge() => string.Join(" | ", Weg.Aufrufe.Where(a => !a.StartsWith("Senden", StringComparison.Ordinal)).Select(a => a.Split(" va=")[0]));

        public void Dispose() => Bruecke.Dispose();
    }

    [Test]
    public static void Melden_PaketeAnCommand_SendeauswahlBleibtCrew()
    {
        using var a = new Aufbau();
        a.Maschine.Sprachaktivierung = true;
        a.Steuerung.MeldenGedrueckt();
        Pruefe.Wahr(a.Maschine.TasteGedrueckt, "Melden öffnet das Mikrofon");
        Pruefe.Falsch(a.Maschine.Sprachaktivierung, "die Sprachaktivierung ruht");
        Pruefe.Gleich("True", string.Join(",", a.Ruht), "die Oberfläche erfährt es");
        a.Rahmen(Sprachkennzeichen.Beginn);
        a.Rahmen(Sprachkennzeichen.Keine);
        Pruefe.Gleich("Beginnen Netz [2]", a.Folge(), "der Spruch geht an Kommando");
        Pruefe.Gleich(Sendeziel.Netz(Crew), a.Weg.Ziel, "die Sendeauswahl bleibt Crew");
        a.Steuerung.MeldenLosgelassen();
        Pruefe.Falsch(a.Maschine.TasteGedrueckt, "Loslassen schließt das Mikrofon");
        a.Rahmen(Sprachkennzeichen.Ende);
        a.Weg.Aufrufe.Clear();
        a.Steuerung.SprechenGedrueckt();
        a.Rahmen(Sprachkennzeichen.Beginn);
        Pruefe.Gleich("Beginnen Netz [22]", a.Folge(), "der nächste Spruch geht wieder an die Crew");
        Pruefe.Gleich("an Kommando,Ende", string.Join(",", a.Meldungen), "Meldezeile: Melden an Kommando, dann vorbei");
        Pruefe.Gleich("True,False", string.Join(",", a.Ruht), "die Sprachaktivierung kommt zurück");
    }

    [Test]
    public static void Melden_WaehrendSprechtasteGehalten_UebernimmtUndGibtZurueck()
    {
        using var a = new Aufbau();
        a.Steuerung.SprechenGedrueckt();
        a.Rahmen(Sprachkennzeichen.Beginn);
        a.Steuerung.MeldenGedrueckt();
        a.Rahmen(Sprachkennzeichen.Keine);
        a.Rahmen(Sprachkennzeichen.Keine);
        Pruefe.Wahr(a.Maschine.TasteGedrueckt, "Mikrofon bleibt offen");
        a.Steuerung.MeldenLosgelassen();
        Pruefe.Wahr(a.Maschine.TasteGedrueckt, "die Sprechtaste hält es weiter offen");
        a.Rahmen(Sprachkennzeichen.Keine);
        a.Steuerung.SprechenLosgelassen();
        Pruefe.Falsch(a.Maschine.TasteGedrueckt, "jetzt zu");
        a.Rahmen(Sprachkennzeichen.Ende);
        Pruefe.Gleich("Beginnen Netz [22] | Beenden | Beginnen Netz [2] | Beenden | Beginnen Netz [22] | Beenden", a.Folge(),
            "Crew, Wechsel an Kommando ohne Lücke, zurück an die Crew");
    }

    [Test]
    public static void Melden_OhneSenderecht_KeinPaketUndSatz()
    {
        using var a = new Aufbau("Melden geht nicht: kein Senderecht in Kommando");
        a.Steuerung.MeldenGedrueckt();
        Pruefe.Falsch(a.Maschine.TasteGedrueckt, "kein Mikrofon");
        a.Rahmen(Sprachkennzeichen.Beginn);
        Pruefe.Falsch(a.Weg.Aufrufe.Any(x => x.StartsWith("Beginnen Netz [2]", StringComparison.Ordinal)), "kein Paket an Kommando");
        Pruefe.Gleich("Melden geht nicht: kein Senderecht in Kommando", string.Join(",", a.Meldungen), "statt still nichts zu tun");
        a.Steuerung.MeldenLosgelassen();
        Pruefe.Gleich(1, a.Meldungen.Count, "Loslassen nach einem Nein meldet nichts");
        Pruefe.Gleich(0, a.Ruht.Count, "die Sprachaktivierung wurde nicht angefasst");
    }

    [Test]
    public static void Melden_SendeauswahlWechselt_SchaltetDieMeldungNichtUm()
    {
        using var a = new Aufbau();
        a.Steuerung.MeldenGedrueckt();
        a.Rahmen(Sprachkennzeichen.Beginn);
        a.Weg.Ziel = Sendeziel.Netz(23);
        a.Bruecke.AuswahlGeaendert();
        Pruefe.Falsch(a.Weg.Aufrufe.Any(x => x.StartsWith("Umschalten", StringComparison.Ordinal)), "Numpad + während der Meldung: die Meldung bleibt an Kommando");
        a.Steuerung.MeldenLosgelassen();
        a.Rahmen(Sprachkennzeichen.Ende);
        a.Steuerung.SprechenGedrueckt();
        a.Rahmen(Sprachkennzeichen.Beginn);
        Pruefe.Wahr(a.Weg.Aufrufe.Last(x => x.StartsWith("Beginnen", StringComparison.Ordinal)).StartsWith("Beginnen Netz [23]", StringComparison.Ordinal), "danach die neue Auswahl");
    }

    /// <summary>
    /// Gegenprüfung S2 (01.10.2026): Der Melde-Thread der Maschine holt die Rahmen kurz nach dem Aufnahme-Thread ab. Rahmen, die vor
    /// dem Loslassen der Melde-Taste aufgenommen, aber erst danach abgeholt wurden, gehören zur Meldung. Bisher beendete das Loslassen
    /// die Phase und begann mit dem nächsten Rahmen eine neue an der Sendeauswahl: Der Rest der Meldung ging an die Crew, dort mit
    /// Beginn und Ende (Klick), obwohl niemand die Sprechtaste gedrückt hatte.
    /// </summary>
    [Test]
    public static void Melden_Loslassen_RestDerMeldungGehtNichtAnDieCrew()
    {
        using var a = new Aufbau();
        a.Steuerung.MeldenGedrueckt();
        a.Rahmen(Sprachkennzeichen.Beginn);
        a.Rahmen(Sprachkennzeichen.Keine);
        a.Steuerung.MeldenLosgelassen();
        // Noch nicht abgeholt: ein Rahmen von vor dem Loslassen, dann der letzte mit Ende.
        a.Rahmen(Sprachkennzeichen.Keine);
        a.Rahmen(Sprachkennzeichen.Ende);
        Pruefe.Gleich("Beginnen Netz [2] | Beenden", a.Folge(), "die ganze Meldung an Kommando, nichts an die Crew");
        Pruefe.Gleich(3, a.Weg.Aufrufe.Count(x => x.StartsWith("Senden", StringComparison.Ordinal)), "alle drei Rahmen in der Phase der Meldung");
        a.Weg.Aufrufe.Clear();
        a.Steuerung.SprechenGedrueckt();
        a.Rahmen(Sprachkennzeichen.Beginn);
        Pruefe.Gleich("Beginnen Netz [22]", a.Folge(), "der nächste Spruch geht an die Crew");
    }

    /// <summary>
    /// Gegenstück: Drückt man die Sprechtaste, bevor die Maschine das Loslassen der Melde-Taste gesehen hat (beide im selben Rahmen
    /// von 10 ms, oder seit 0.9.3 im Nachlauf), endet die Sprechphase nie, und der Spruch an die Crew liefe in der Phase der Meldung weiter an
    /// Kommando. Dann muss die Phase der Meldung enden und eine neue an der Sendeauswahl beginnen. Seit der Prüfung 0.9.3 macht das die
    /// Maschine: Die Steuerung sagt ihr, dass der Druck nicht fortsetzt, sie beendet die Phase mit ENDE und beginnt eine neue. Bis zu diesem
    /// ENDE bleibt die Brücke am Meldenetz, denn alles davor ist noch die Meldung; bis dahin schaltete die Brücke sofort um, und der Rest der
    /// Meldung ging an die Crew. Die Attrappe sendet hier die Rahmen, die die Maschine sendet.
    /// </summary>
    [Test]
    public static void Melden_LoslassenUndGleichSprechtaste_SpruchGehtAnDieCrew()
    {
        using var a = new Aufbau();
        a.Steuerung.MeldenGedrueckt();
        Pruefe.Gleich(false, a.Maschine.LetzterDruckFortsetzen, "der erste Druck: kein Spruch davor");
        a.Rahmen(Sprachkennzeichen.Beginn);
        a.Steuerung.MeldenLosgelassen();
        a.Steuerung.SprechenGedrueckt();
        Pruefe.Gleich(false, a.Maschine.LetzterDruckFortsetzen, "die Sprechtaste setzt die Meldung nicht fort");
        a.Rahmen(Sprachkennzeichen.Keine);
        a.Rahmen(Sprachkennzeichen.Ende);
        Pruefe.Gleich("Beginnen Netz [2] | Beenden", a.Folge(), "der Rest der Meldung bleibt bis zu ihrem ENDE an Kommando");
        Pruefe.Gleich(2, a.Weg.Aufrufe.Count(x => x.StartsWith("Senden", StringComparison.Ordinal)), "beide Rahmen in der Phase der Meldung");
        a.Rahmen(Sprachkennzeichen.Beginn);
        a.Rahmen(Sprachkennzeichen.Keine);
        Pruefe.Gleich("Beginnen Netz [2] | Beenden | Beginnen Netz [22]", a.Folge(), "die Meldung endet, der Spruch beginnt an der Crew");
        a.Steuerung.SprechenLosgelassen();
        a.Rahmen(Sprachkennzeichen.Ende);
        Pruefe.Falsch(a.Maschine.TasteGedrueckt, "Mikrofon zu");
    }

    /// <summary>
    /// Prüfung 0.9.3: Ein Druck setzt einen Nachlauf nur fort, wenn er demselben Ziel gilt wie der zuletzt gesprochene Spruch (dieselbe Taste,
    /// dasselbe Meldenetz). Wechselt das Ziel, auch über eine gehaltene Taste, sagt die Steuerung der Maschine, dass sie nicht fortsetzen darf.
    /// </summary>
    [Test]
    public static void Fortsetzen_NurFuersSelbeZiel()
    {
        using var a = new Aufbau();
        var folge = new List<string>();
        void Merken(string was) => folge.Add(was + "=" + a.Maschine.LetzterDruckFortsetzen);
        a.Steuerung.SprechenGedrueckt();
        a.Steuerung.SprechenLosgelassen();
        a.Steuerung.SprechenGedrueckt();
        Merken("Sprechen nach Sprechen");
        a.Steuerung.SprechenLosgelassen();
        a.Steuerung.MeldenGedrueckt();
        Merken("Melden nach Sprechen");
        a.Steuerung.MeldenLosgelassen();
        a.Steuerung.MeldenGedrueckt();
        Merken("Melden nach Melden");
        a.Steuerung.MeldenLosgelassen();
        a.Steuerung.SprechenGedrueckt();
        Merken("Sprechen nach Melden");
        // Sprechtaste gehalten, Melde-Taste dazu: Das Mikrofon spricht ins Meldenetz; nach beiden Tasten los zählt die Meldung.
        a.Steuerung.MeldenGedrueckt();
        a.Steuerung.SprechenLosgelassen();
        a.Steuerung.MeldenLosgelassen();
        a.Steuerung.MeldenGedrueckt();
        Merken("Melden nach Sprechen und Melden");
        // Melde-Taste gehalten, Sprechtaste dazu, Melde-Taste los: Das Mikrofon spricht wieder an die Sendeauswahl.
        a.Steuerung.SprechenGedrueckt();
        a.Steuerung.MeldenLosgelassen();
        a.Steuerung.SprechenLosgelassen();
        a.Steuerung.SprechenGedrueckt();
        Merken("Sprechen nach Melden und Sprechen");
        a.Steuerung.SprechenLosgelassen();
        Pruefe.Gleich("Sprechen nach Sprechen=True | Melden nach Sprechen=False | Melden nach Melden=True | Sprechen nach Melden=False | "
            + "Melden nach Sprechen und Melden=True | Sprechen nach Melden und Sprechen=True", string.Join(" | ", folge), "Fortsetzen nur fürs selbe Ziel");
    }
}
