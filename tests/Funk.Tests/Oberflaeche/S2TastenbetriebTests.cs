// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Klang;
using Funk.Client.Modelle;
using Funk.Klang.Eingabe;
using Funk.Tests.Maschine;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Ausbaustufe S2, Verdrahtung der Tasten (RAID-S2-ENTWURF Abschnitte 3 bis 5), wie App.OnStartup sie baut: Die Ereignisse kommen
/// aus dem Thread der Tastenquelle; Funkfeld ablegen/holen, Sendenetz vor/zurück und Phase vor/zurück gehen über den Oberflächentakt,
/// Sprechen und Melden direkt an die Sprechsteuerung. In der Ablage sagt die Meldezeile das Melden (das Sendenetz seit 0.11.2 nicht mehr); Melden lässt
/// die Sprachaktivierung ruhen, bis die Taste los ist. Ein Phasenwechsel per Taste steht im Verlauf des Bretts.
/// </summary>
internal static class S2TastenbetriebTests
{
    private sealed class Teile : IDisposable
    {
        public Teile(string? hindernis = null)
        {
            A = new Aufbau(dienst: MitRaidabend(), takt: Takt) { Jetzt = Abend };
            Funkfeld = A.Funkfeld();
            Zeile = new Meldezeile(() => A.Uhr);
            Ablage = new FunkfeldAblage(Fenster, Symbol, A.Ablage, Zeile, A.Dienst, () => A.Uhr);
            Phasen = new Phasensteuerung(A.Dienst, Zeile, () => A.Uhr, () => A.Jetzt);
            Teilnehmer = new Teilnehmermeldungen(A.Dienst, Zeile, Takt);
            Sprechen = new Sprechsteuerung(A.Maschine, A.Bruecke, () => new MeldeEntscheid(NetzOrga, "Orga", hindernis));
            Brett = A.Brett();
            Betrieb = new Tastenbetrieb(Quelle, Sprechen, Takt, Funkfeld, Ablage, Phasen, Teilnehmer, Brett);
            Takt.Abarbeiten();
        }

        public WarteschlangenTakt Takt { get; } = new();

        public Aufbau A { get; }

        public TastenQuelleAttrappe Quelle { get; } = new(EingabeArt.Tastatur, EingabeArt.Joystick);

        public FunkfeldViewModel Funkfeld { get; }

        public Meldezeile Zeile { get; }

        public FunkfeldFensterAttrappe Fenster { get; } = new();

        public AblagesymbolAttrappe Symbol { get; } = new();

        public FunkfeldAblage Ablage { get; }

        public Phasensteuerung Phasen { get; }

        public Teilnehmermeldungen Teilnehmer { get; }

        public Sprechsteuerung Sprechen { get; }

        public BrettViewModel Brett { get; }

        public Tastenbetrieb Betrieb { get; }

        /// <summary>Den Takt abarbeiten, bis <paramref name="fertig"/> gilt (Antworten der Attrappe kommen über den Thread-Pool).</summary>
        public async Task Bis(Func<bool> fertig, string was)
        {
            for (var i = 0; i < 200 && !fertig(); i++)
            {
                Takt.Abarbeiten();
                if (!fertig()) await Task.Delay(5);
            }
            Pruefe.Wahr(fertig(), was);
        }

        public void Dispose()
        {
            Betrieb.Dispose();
            Teilnehmer.Dispose();
            Funkfeld.Dispose();
            A.Dispose();
        }
    }

    [Test]
    public static void Funkfeldtaste_UeberDenTakt_LegtAbUndHoltOhneFokus()
    {
        using var t = new Teile();
        t.Ablage.Starten();
        t.Quelle.Ausloesen(Tastenfunktion.FunkfeldUmschalten);
        Pruefe.Wahr(t.Fenster.Sichtbar && t.Takt.Anzahl == 1, "aus dem Thread der Quelle: erst über den Takt");
        t.Takt.Abarbeiten();
        Pruefe.Wahr(t.Ablage.Abgelegt && t.Funkfeld.Abgelegt && t.Teilnehmer.Abgelegt, "abgelegt, Funkfeld und Meldungen wissen es");
        t.Quelle.Ausloesen(Tastenfunktion.FunkfeldUmschalten);
        t.Takt.Abarbeiten();
        Pruefe.Wahr(!t.Ablage.Abgelegt && !t.Funkfeld.Abgelegt, "zurück");
        Pruefe.Falsch(t.Fenster.Aufrufe.Contains("zeigen mit Fokus"), "nie mit Fokus: " + string.Join(" | ", t.Fenster.Aufrufe));
    }

    /// <summary>
    /// Die Netztasten wechseln das Sendenetz, sichtbar wie abgelegt. Bis 0.11.1 sagte die Meldezeile es in der Ablage ("Sendekanal: Orga"); seit
    /// 0.11.2 erscheint über dem Spiel nur, was <see cref="Meldezeile.Erlaubt"/> nennt (Sarevoc 13.10.2026), der Sendekanal nicht mehr.
    /// </summary>
    [Test]
    public static async Task Netztaste_InDerAblage_WechseltOhneMeldezeile()
    {
        using var t = new Teile();
        t.Ablage.Starten();
        int Senden() => t.A.Dienst.Aufrufe.Count(a => a.StartsWith("Senden", StringComparison.Ordinal));
        t.Quelle.Ausloesen(Tastenfunktion.NetzVor);
        await t.Bis(() => Senden() == 1, "das Sendenetz wechselt");
        t.Takt.Abarbeiten();
        Pruefe.Falsch(t.Zeile.Zeigt(Teilnehmermeldungen.SendenetzSchluessel), "sichtbar: das Funkfeld zeigt es selbst");

        t.Ablage.Ablegen();
        t.Quelle.Ausloesen(Tastenfunktion.NetzZurueck);
        await t.Bis(() => Senden() == 2, "abgelegt: das Sendenetz wechselt auch");
        await Task.Delay(50);
        t.Takt.Abarbeiten();
        Pruefe.Falsch(t.Zeile.Zeigt(Teilnehmermeldungen.SendenetzSchluessel) || t.Zeile.Sichtbar, "abgelegt: kein Satz zum Sendekanal über dem Spiel: " + t.Zeile.Text);
    }

    [Test]
    public static void Meldetaste_RuhtDieSprachaktivierung_MeldezeileInDerAblage()
    {
        using var t = new Teile();
        t.Ablage.Starten();
        t.Ablage.Ablegen();
        t.Zeile.Loesen(FunkfeldAblage.HinweisSchluessel);
        t.Quelle.Halten(Tastenfunktion.Melden, true);
        Pruefe.Wahr(t.A.Maschine.TasteGedrueckt && t.Funkfeld.SprachaktivierungRuht, "direkt aus dem Thread der Quelle: Mikrofon offen, VA ruht");
        t.Funkfeld.AusloesungGeaendert();
        Pruefe.Falsch(t.A.Maschine.Sprachaktivierung, "auch wenn die Lage die Auslösung neu rechnet");
        t.Takt.Abarbeiten();
        Pruefe.Gleich("Melden: an Orga", t.Zeile.Text, "abgelegt: solange gehalten");
        t.Quelle.Halten(Tastenfunktion.Melden, false);
        t.Takt.Abarbeiten();
        Pruefe.Wahr(!t.A.Maschine.TasteGedrueckt && !t.Funkfeld.SprachaktivierungRuht && !t.Zeile.Zeigt(Teilnehmermeldungen.MeldenSchluessel), "los: zu, VA darf wieder");
        t.Quelle.Druecken(true);
        Pruefe.Wahr(t.A.Maschine.TasteGedrueckt, "die Sprechtaste geht über die Sprechsteuerung");
        t.Quelle.Druecken(false);
        Pruefe.Falsch(t.A.Maschine.TasteGedrueckt, "und wieder zu");
    }

    [Test]
    public static void Meldetaste_OhneSenderecht_SagtWarum()
    {
        using var t = new Teile("Melden geht nicht: kein Senderecht in Kommando");
        t.Quelle.Halten(Tastenfunktion.Melden, true);
        t.Takt.Abarbeiten();
        Pruefe.Falsch(t.A.Maschine.TasteGedrueckt, "kein Mikrofon");
        Pruefe.Gleich("Melden geht nicht: kein Senderecht in Kommando", t.Zeile.Text, "auch sichtbar: der Grund steht da");
    }

    [Test]
    public static async Task Phasentaste_ZweiDrucke_SchaltetUndStehtImVerlaufDesBretts()
    {
        using var t = new Teile();
        t.Quelle.Ausloesen(Tastenfunktion.PhaseVor);
        t.Takt.Abarbeiten();
        Pruefe.Wahr(t.Phasen.Zustand == Phasenzustand.Scharf && t.Zeile.Text.StartsWith("Phase 2/3 ENTERN? Nochmal drücken", StringComparison.Ordinal),
            "erster Druck: scharf, " + t.Zeile.Text);
        t.A.Uhr += 500;
        t.Quelle.Ausloesen(Tastenfunktion.PhaseVor);
        await t.Bis(() => t.Brett.Verlauf.Count > 0 && t.Brett.Verlauf[0].Contains("du: Phase Entern", StringComparison.Ordinal),
            "zweiter Druck schaltet, das Brett nennt es im Verlauf");
        Pruefe.Wahr(t.A.Dienst.Aufrufe.Contains("Phase 2 erwartet 1"), "an den Server");
    }
}
