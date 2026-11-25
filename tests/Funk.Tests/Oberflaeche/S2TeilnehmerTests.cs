// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Dienste;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Ausbaustufe S2, Spielersicht (RAID-S2-ENTWURF Abschnitte 3 und 4): Was die Meldezeile jedem Funker sagt, auch wenn das Funkfeld
/// abgelegt ist. Eigene Umstellung durch die Leitung und durch eine Phase (6 s), Entzug des Senderechts (5 s), das Sendenetz nach
/// einer Taste nur in der Ablage (2 s), Melden solange gehalten nur in der Ablage, "Melden geht nicht" immer. Keine Meldung beim
/// Verbinden, bei eigenen Änderungen und bei Pushes ohne neue Mitgliedschaften.
/// </summary>
internal static class S2TeilnehmerTests
{
    private const ushort Crew1 = 12;
    private const ushort TruppNetz = 41;

    private static (FunkDienstAttrappe Dienst, Meldezeile Zeile, Teilnehmermeldungen T) Neu()
    {
        var d = MitRaidabend();
        long ms = 0;
        var zeile = new Meldezeile(() => ms);
        return (d, zeile, new Teilnehmermeldungen(d, zeile, SofortTakt.Instanz));
    }

    [Test]
    public static void Umstellung_DurchDieLeitung_UndDurchEinePhase()
    {
        var (d, zeile, t) = Neu();
        using var _ = t;
        Pruefe.Falsch(zeile.Sichtbar, "der Stand beim Verbinden meldet nichts");
        d.LageSetzen(l => l with { Sprecher = [] });
        Pruefe.Falsch(zeile.Sichtbar, "ein Push ohne neue Mitgliedschaften meldet nichts");

        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            SchiffId = Schiff1,
            RolleId = Gunner,
            Platz = PlatzStatus.Belegt,
            Quelle = MitgliedschaftsQuelle.Verschieben,
            QuelleDurch = Ferox,
            Felder = [new FeldInfo(0, Crew1, true, true, false), new FeldInfo(1, 13, true, false, false)],
            SendeFelder = [0],
        });
        // Seit 0.11.0 eine Zeile ohne "Du sendest in Crew 1." (den Sendekanal zeigen die Kacheln).
        Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Gunner", zeile.Text, "Umstellung durch die Leitung");
        zeile.Loesen("umstellung");

        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            UmstellungPhase = 2,
            LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", Ferox, Abend),
            Felder = [new FeldInfo(0, Crew1, true, false, false), new FeldInfo(1, 13, true, false, false), new FeldInfo(2, TruppNetz, true, true, false)],
            SendeFelder = [2],
        });
        Pruefe.Gleich("Phase Entern (Ferox): sendest in Trupp Alpha", zeile.Text, "Umstellung durch eine Phase");
        zeile.Loesen("umstellung");

        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            UmstellungPhase = null,
            Quelle = MitgliedschaftsQuelle.Selbst,
            QuelleDurch = null,
            Felder = [.. l.Felder, new FeldInfo(5, FunkDienstAttrappe.NetzOrga, true, true, false)],
        });
        Pruefe.Falsch(zeile.Sichtbar, "eine eigene Änderung meldet nichts");
    }

    [Test]
    public static void Phase_UmstellungVorDemPhasenpush_NameKommtNach()
    {
        // Gefunden im Zusammenspiel mit dem echten Server (S2DienstTests): Die neuen Mitgliedschaften kommen vor 0x4004, also bevor
        // der Client weiß, wer geschaltet hat. Der Name wird nachgetragen, solange die Meldung steht.
        var (d, zeile, t) = Neu();
        using var _ = t;
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            UmstellungPhase = 2,
            Felder = [new FeldInfo(0, Crew1, true, false, false), new FeldInfo(2, TruppNetz, true, true, false)],
            SendeFelder = [2],
        });
        Pruefe.Gleich("Phase Entern: sendest in Trupp Alpha", zeile.Text, "noch ohne Namen");
        d.LageSetzen(l => l with { LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", Ferox, Abend) });
        Pruefe.Gleich("Phase Entern (Ferox): sendest in Trupp Alpha", zeile.Text, "der Name kommt mit dem Phasenpush");
        zeile.Loesen("umstellung");
        d.LageSetzen(l => l with { LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", Ferox, Abend.AddSeconds(1)) });
        Pruefe.Falsch(zeile.Sichtbar, "eine gelöste Meldung kommt nicht wieder");
    }

    [Test]
    public static void Phase_OhneNeuesSendenetz_NenntWasManJetztHoert()
    {
        var (d, zeile, t) = Neu();
        using var _ = t;
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            UmstellungPhase = 2,
            LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", 0, Abend),
            Felder = [.. l.Felder, new FeldInfo(4, TruppNetz, true, false, false)],
        });
        Pruefe.Gleich("Phase Entern: hörst Trupp Alpha", zeile.Text, "Rückfall oder Konsole ohne Namen, neues Hörnetz");
        zeile.Loesen("umstellung");

        // Nur umgestellt (kein neues Sendenetz, kein neues Hörnetz): bei einer Phase "Kanäle umgestellt", sonst nur wer.
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            UmstellungPhase = 2,
            Felder = [.. l.Felder.Where(f => f.Feld != 4)],
        });
        Pruefe.Gleich("Phase Entern: Kanäle umgestellt", zeile.Text, "nur umgestellt");
        zeile.Loesen("umstellung");
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            UmstellungPhase = null,
            Quelle = MitgliedschaftsQuelle.Verschieben,
            QuelleDurch = Ferox,
            Felder = [.. l.Felder, new FeldInfo(8, FunkDienstAttrappe.NetzOrga, false, false, false)],
        });
        Pruefe.Gleich("Kanäle geändert von Ferox", zeile.Text, "Kanäle geändert, nur umgestellt");
    }

    // ---------------------------------------------------------------- Ansage beim Login (Zuordnung Punkt 3, Fassung 0.11.0)

    /// <summary>Getrennt, dann wieder aktiv, noch ohne Abgleich (wie am echten Server zwischen Welcome und Zustand).</summary>
    private static void NeuVerbunden(FunkDienstAttrappe d)
    {
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt, Abgeglichen = false });
        d.LageSetzen(l => l with { Status = Verbindungsstatus.Aktiv });
    }

    /// <summary>Die Einteilung als Gunner auf Schiff 1, wie sie beim Login kommt.</summary>
    private static FunkLage Eingeteilt(FunkLage l, MitgliedschaftsQuelle quelle, uint? durch) => l with
    {
        KontoVersion = l.KontoVersion + 1,
        SchiffId = Schiff1,
        RolleId = Gunner,
        Platz = PlatzStatus.Belegt,
        Quelle = quelle,
        QuelleDurch = durch,
        Felder = [new FeldInfo(0, Crew1, true, true, false), new FeldInfo(1, 13, true, false, false), new FeldInfo(2, 11, true, false, false)],
        SendeFelder = [0],
    };

    /// <summary>
    /// Seit der einzeiligen Meldezeile (Sarevoc, 12.10.2026) nur wer und der Platz; bis dahin "Für heute eingeteilt von Ferox: Schiff 1, Gunner.
    /// Kanäle: Crew 1, Brücke 1, Durchsagen 1. Du sendest in Crew 1." (zwei Zeilen). Kanäle und Sendekanal stehen in den Kacheln.
    /// </summary>
    internal const string AnsageGunner = "Eingeteilt von Ferox: Schiff 1, Gunner";

    [Test]
    public static void Ansage_BeimLogin_ErstNachDemAbgleich_UndNurEinmal()
    {
        var (d, zeile, t) = Neu();
        using var _ = t;
        NeuVerbunden(d);
        d.LageSetzen(l => Eingeteilt(l, MitgliedschaftsQuelle.Verschieben, Ferox));
        Pruefe.Falsch(zeile.Sichtbar, "vor dem Abgleich nichts, auch nicht die Umstellungsmeldung");
        d.LageSetzen(l => l with { Abgeglichen = true });
        Pruefe.Gleich(AnsageGunner, zeile.Text, "Ansage nach dem Abgleich");
        Pruefe.Wahr(zeile.Zeigt(Teilnehmermeldungen.AnsageSchluessel), "eigener Schlüssel");
        zeile.Loesen(Teilnehmermeldungen.AnsageSchluessel);

        // Kurz getrennt, gleiche kontoVersion: keine Wiederholung.
        NeuVerbunden(d);
        d.LageSetzen(l => l with { Abgeglichen = true });
        Pruefe.Falsch(zeile.Sichtbar, "dieselbe Einteilung kommt nicht noch einmal");

        // Danach eine Umstellung: die bisherige Meldung.
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            Felder = [.. l.Felder, new FeldInfo(3, FunkDienstAttrappe.NetzOrga, true, false, false)],
        });
        Pruefe.Gleich("Kanäle geändert von Ferox: hörst Orga", zeile.Text, "Umstellung wie bisher");
        Pruefe.Wahr(zeile.Zeigt(Teilnehmermeldungen.UmstellungSchluessel) && !zeile.Zeigt(Teilnehmermeldungen.AnsageSchluessel), "als Umstellung");
    }

    /// <summary>
    /// Befund der Prüfung (0.11.0, einzeilige Meldezeile): Ansage und Umstellung beginnen beide mit "Eingeteilt von". Teilt die Leitung einen
    /// um, während die Ansage noch steht, ersetzt die neue Zeile die Ansage; sonst stünden zwei Plätze untereinander, ohne dass man sähe,
    /// welcher gilt (bis zur Kürzung trennte sie "Für heute"). Bis Sarevoc Q112 blieb die Ansage neben geänderten Kanälen stehen; seit Q112 steht
    /// nur eine Meldung da, und die Umstellung (gleicher Rang, neuer) ersetzt auch dann die Ansage.
    /// </summary>
    [Test]
    public static void Ansage_NeuerPlatzDurchDieLeitung_ErsetztDieAnsage()
    {
        var (d, zeile, t) = Neu();
        using var _ = t;
        NeuVerbunden(d);
        d.LageSetzen(l => Eingeteilt(l, MitgliedschaftsQuelle.Verschieben, Ferox) with { Abgeglichen = true });
        Pruefe.Gleich(AnsageGunner, zeile.Text, "Ansage");

        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            Felder = [.. l.Felder, new FeldInfo(3, FunkDienstAttrappe.NetzOrga, true, false, false)],
        });
        Pruefe.Gleich(1, zeile.Zeilen.Count, "Kanäle geändert: eine Meldung (Q112)");
        Pruefe.Wahr(zeile.Zeigt(Teilnehmermeldungen.UmstellungSchluessel) && !zeile.Zeigt(Teilnehmermeldungen.AnsageSchluessel),
            "Q112: die Umstellung ersetzt die Ansage (gleicher Rang)");
        zeile.Loesen(Teilnehmermeldungen.UmstellungSchluessel);

        d.LageSetzen(l => l with { KontoVersion = l.KontoVersion + 1, RolleId = Pilot });
        Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Pilot", zeile.Text, "nur der neue Platz, eine Zeile");
        Pruefe.Wahr(zeile.Zeigt(Teilnehmermeldungen.UmstellungSchluessel) && !zeile.Zeigt(Teilnehmermeldungen.AnsageSchluessel), "die Ansage ist ersetzt");
    }

    [Test]
    public static void Ansage_NurFuerEineEinteilungDurchAndere()
    {
        foreach (var (quelle, durch, was) in new (MitgliedschaftsQuelle, uint?, string)[]
                 {
                     (MitgliedschaftsQuelle.Satz, null, "SATZ"), (MitgliedschaftsQuelle.Selbst, null, "SELBST"), (MitgliedschaftsQuelle.Verschieben, Mustermann, "eigenes durch"),
                     (MitgliedschaftsQuelle.Wiederhergestellt, 0, "WIEDERHERGESTELLT in der Frist (durch 0)"), (MitgliedschaftsQuelle.Standard, null, "STANDARD"),
                 })
        {
            var (d, zeile, t) = Neu();
            using var _ = t;
            NeuVerbunden(d);
            d.LageSetzen(l => Eingeteilt(l, quelle, durch) with { Abgeglichen = true });
            Pruefe.Falsch(zeile.Sichtbar, was + ": keine Ansage");
        }

        // Ein Server bis 0.10.0 meldet die Reservierung als WIEDERHERGESTELLT mit durch: auch dann die Ansage.
        var (d2, zeile2, t2) = Neu();
        using var __ = t2;
        NeuVerbunden(d2);
        d2.LageSetzen(l => Eingeteilt(l, MitgliedschaftsQuelle.Wiederhergestellt, Ferox) with { Abgeglichen = true });
        Pruefe.Gleich(AnsageGunner, zeile2.Text, "Server 0.10.0");

        // Ohne Platz: nur die Kanäle; der reine Standard (alles auf Ruhe oder gewöhnlich, nichts fest) sagt nichts an.
        var (d3, zeile3, t3) = Neu();
        using var ___ = t3;
        NeuVerbunden(d3);
        d3.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1, SchiffId = null, RolleId = null, Platz = null, Quelle = MitgliedschaftsQuelle.Wiederhergestellt, QuelleDurch = Ferox,
            Abgeglichen = true,
        });
        Pruefe.Falsch(zeile3.Sichtbar, "zurück auf Standard an einem Server 0.10.0: nichts");
        NeuVerbunden(d3);
        d3.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1, Quelle = MitgliedschaftsQuelle.Verschieben, QuelleDurch = Ferox,
            Felder = [.. l.Felder, new FeldInfo(2, 13, true, false, true)], Abgeglichen = true,
        });
        // Bis 0.11.0 mit der Kanalliste und dem Sendekanal ("...: Kanäle Kommando, Orga, Brücke 1. Du sendest in Orga.").
        Pruefe.Gleich("Kanäle eingeteilt von Ferox", zeile3.Text, "ohne Platz mit einem festen Zusatzkanal");
        Pruefe.Wahr(zeile3.Zeigt(Teilnehmermeldungen.AnsageSchluessel), "als Ansage");
    }

    /// <summary>
    /// Befund der Prüfung 0.11.0: Online eingeteilt, danach eine Phase oder eine eigene Änderung (beides erhöht die kontoVersion), dann kurz
    /// getrennt. Der Server meldet beim Login wieder VERSCHIEBEN mit durch (auch ein Server ohne den Fix, der die eigene Änderung überschreibt),
    /// mit der neueren kontoVersion. Dieselbe Einteilung ist online gesehen: keine Ansage. Eine neue Einteilung offline wird angesagt.
    /// </summary>
    [Test]
    public static void Ansage_NachKurzerTrennung_NichtNachPhaseOderEigenerAenderung()
    {
        foreach (var (was, danach) in new (string, Func<FunkLage, FunkLage>)[]
                 {
                     ("Phasenwechsel", l => l with
                     {
                         KontoVersion = l.KontoVersion + 1,
                         UmstellungPhase = 2,
                         LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", Ferox, Abend),
                         Felder = [new FeldInfo(0, Crew1, true, false, false), new FeldInfo(1, 13, true, false, false), new FeldInfo(2, TruppNetz, true, true, false)],
                         SendeFelder = [2],
                     }),
                     ("eigene Änderung", l => l with
                     {
                         KontoVersion = l.KontoVersion + 1,
                         Quelle = MitgliedschaftsQuelle.Selbst,
                         QuelleDurch = null,
                         Felder = [new FeldInfo(0, Crew1, true, true, false), new FeldInfo(1, 11, true, false, false)],
                     }),
                 })
        {
            var (d, zeile, t) = Neu();
            using var _ = t;
            d.LageSetzen(l => Eingeteilt(l, MitgliedschaftsQuelle.Verschieben, Ferox) with { Abgeglichen = true });
            Pruefe.Wahr(zeile.Text.StartsWith("Eingeteilt von Ferox", StringComparison.Ordinal), was + ": online eingeteilt: " + zeile.Text);
            zeile.Loesen(Teilnehmermeldungen.UmstellungSchluessel);
            d.LageSetzen(danach);
            zeile.Loesen(Teilnehmermeldungen.UmstellungSchluessel);
            NeuVerbunden(d);
            d.LageSetzen(l => l with { UmstellungPhase = null, Quelle = MitgliedschaftsQuelle.Verschieben, QuelleDurch = Ferox, Abgeglichen = true });
            Pruefe.Falsch(zeile.Sichtbar, was + ", kurz getrennt: keine Ansage: " + zeile.Text);

            // Offline auf einen anderen Platz eingeteilt: das ist neu und kommt als Ansage.
            NeuVerbunden(d);
            d.LageSetzen(l => l with { KontoVersion = l.KontoVersion + 1, RolleId = Pilot, Quelle = MitgliedschaftsQuelle.Verschieben, QuelleDurch = Ferox, Abgeglichen = true });
            Pruefe.Wahr(zeile.Zeigt(Teilnehmermeldungen.AnsageSchluessel) && zeile.Text == "Eingeteilt von Ferox: Schiff 1, Pilot",
                was + ", neuer Platz: Ansage: " + zeile.Text);
        }
    }

    [Test]
    public static void Entzug_SagtWoUndDurchWen()
    {
        var (d, zeile, t) = Neu();
        using var _ = t;
        d.EreignisMelden(new SendenetzEntzogen([FunkDienstAttrappe.NetzOrga], Ferox));
        // Seit 0.11.0 der Urheber in Klammern wie bei einer Phase (bis dahin " - verschoben durch Ferox").
        Pruefe.Gleich("Nicht gesendet: kein Senderecht mehr in Orga (Ferox)", zeile.Text, "mit Urheber");
        Pruefe.Wahr(zeile.HatFehler, "als Fehler");
        d.EreignisMelden(new SendenetzEntzogen([FunkDienstAttrappe.NetzOrga], null));
        Pruefe.Gleich("Nicht gesendet: kein Senderecht mehr in Orga", zeile.Text, "ohne Urheber");
    }

    [Test]
    public static void SendenetzUndMelden_NurInDerAblage_MeldenGehtNichtImmer()
    {
        var (_, zeile, t) = Neu();
        using var __ = t;
        t.SendenetzGewechselt();
        t.MeldenBegonnen("Kommando");
        Pruefe.Falsch(zeile.Sichtbar, "das Funkfeld zeigt es selbst");
        t.MeldenBeendet();
        t.Abgelegt = true;
        t.SendenetzGewechselt();
        // Bis 0.11.1 stand hier "Sendekanal: Orga"; seit 0.11.2 nicht mehr über dem Spiel (Meldezeile.Erlaubt, Sarevoc 13.10.2026).
        Pruefe.Falsch(zeile.Sichtbar, "abgelegt: das Sendenetz nach der Taste fällt still weg");
        t.MeldenBegonnen("Kommando");
        Pruefe.Wahr(zeile.Text.Contains("Melden: an Kommando", StringComparison.Ordinal), "solange gehalten");
        t.MeldenBeendet();
        Pruefe.Falsch(zeile.Text.Contains("Melden", StringComparison.Ordinal), "losgelassen: fort");
        t.Abgelegt = false;
        t.MeldenGehtNicht("Melden geht nicht: kein Senderecht in Kommando");
        Pruefe.Wahr(zeile.Text.Contains("Melden geht nicht: kein Senderecht in Kommando", StringComparison.Ordinal), "der Grund steht immer da");
    }
}
