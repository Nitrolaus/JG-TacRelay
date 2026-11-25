// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Client.Modelle;
using Funk.Klang.Maschine;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Hörprobe in der Oberfläche nach Sarevocs Fehlerbericht vom 01.10.2026 ("ich höre mich bei Aktivierung nicht"): die Probe als
/// Aufnahme mit Wiedergabe danach geht immer, auch mit Lautsprechern; Mithören beim Senden nur mit Headset, und der Text sagt,
/// wann man sich dabei hört (Auslösung des Sendenetzes). Die Maschine selbst prüft <c>Maschine.HoerprobeTests</c>.
/// </summary>
internal static class OberflaecheHoerprobeTests
{
    [Test]
    public static void Hoerprobe_Mithoeren_NurMitHeadset_LautsprecherSchaltetAus()
    {
        using var a = new Aufbau();
        var audio = a.Audio();
        var hp = audio.Hoerprobe;
        Pruefe.Falsch(hp.KannMithoeren, "Frage Headset oder Lautsprecher offen: kein Mithören");
        hp.Mithoeren = true;
        Pruefe.Falsch(a.Maschine.Hoerprobe, "bleibt aus");

        audio.LautsprecherWaehlenCommand.Execute(null);
        Pruefe.Falsch(hp.KannMithoeren, "Lautsprecher: kein Mithören");
        hp.Mithoeren = true;
        Pruefe.Falsch(a.Maschine.Hoerprobe || hp.Mithoeren, "bleibt aus");

        audio.HeadsetWaehlenCommand.Execute(null);
        Pruefe.Wahr(hp.KannMithoeren, "Headset: Mithören möglich");
        hp.Mithoeren = true;
        Pruefe.Wahr(a.Maschine.Hoerprobe && hp.Mithoeren, "an");

        var gemeldet = new HashSet<string?>();
        hp.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);
        audio.LautsprecherWaehlenCommand.Execute(null);
        Pruefe.Falsch(a.Maschine.Hoerprobe || hp.Mithoeren, "der Wechsel auf Lautsprecher schaltet das Mithören aus");
        foreach (var n in new[] { nameof(hp.Mithoeren), nameof(hp.KannMithoeren), nameof(hp.MithoerenText) })
            Pruefe.Wahr(gemeldet.Contains(n), "gemeldet: " + n);
    }

    [Test]
    public static void Hoerprobe_MithoerenText_NenntGrundUndAusloesung()
    {
        using var a = new Aufbau();
        var audio = a.Audio();
        var hp = audio.Hoerprobe;
        Pruefe.Wahr(hp.MithoerenText.Contains("Headset", StringComparison.Ordinal) && hp.MithoerenText.Contains("Lautsprecher", StringComparison.Ordinal),
            "ohne Headset steht da, warum nicht: " + hp.MithoerenText);

        audio.HeadsetWaehlenCommand.Execute(null);
        a.Maschine.Sprachaktivierung = false;
        hp.Takt();
        Pruefe.Wahr(hp.MithoerenText.Contains("nur mit der Sprechtaste", StringComparison.Ordinal), "Sendenetz auf Taste: " + hp.MithoerenText);

        var gemeldet = new HashSet<string?>();
        hp.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);
        a.Maschine.Sprachaktivierung = true;
        hp.Takt();
        Pruefe.Wahr(hp.MithoerenText.Contains("Sprachaktivierung", StringComparison.Ordinal), "Sendenetz auf VA: " + hp.MithoerenText);
        Pruefe.Wahr(gemeldet.Contains(nameof(hp.MithoerenText)), "Wechsel der Auslösung gemeldet");
        gemeldet.Clear();
        hp.Takt();
        Pruefe.Gleich(0, gemeldet.Count, "ohne Änderung meldet der Takt nichts");
    }

    [Test]
    public static void Hoerprobe_MithoerenText_FolgtDemSchalter()
    {
        // Prüfung 0.4.1: Mit Headset stand unter dem AUSGESCHALTETEN Schalter "Du hörst mit, was du sendest. Gerade nur mit
        // der Sprechtaste: Halte sie, um dich zu hören." Wer das las, hielt die Sprechtaste und hörte sich nicht, genau das
        // Bild aus Sarevocs Fehlerbericht. Der Text muss zuerst sagen, ob Mithören an ist, und bei jedem Umschalten neu kommen.
        using var a = new Aufbau();
        var audio = a.Audio();
        var hp = audio.Hoerprobe;
        audio.HeadsetWaehlenCommand.Execute(null);
        var gemeldet = new HashSet<string?>();
        hp.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);
        foreach (var va in new[] { false, true })
        {
            var ausloesung = va ? "Sprachaktivierung" : "nur mit der Sprechtaste";
            a.Maschine.Sprachaktivierung = va;
            hp.Mithoeren = false;
            hp.Takt();
            Pruefe.Falsch(a.Maschine.Hoerprobe || hp.Mithoeren, "Schalter aus");
            Pruefe.Wahr(hp.MithoerenText.StartsWith("Aus.", StringComparison.Ordinal), "aus, und der Text sagt es zuerst: " + hp.MithoerenText);
            Pruefe.Falsch(hp.MithoerenText.Contains("Du hörst mit", StringComparison.Ordinal) || hp.MithoerenText.Contains("Halte sie", StringComparison.Ordinal),
                "aus: kein Versprechen, dass man sich hört: " + hp.MithoerenText);
            Pruefe.Wahr(hp.MithoerenText.Contains(ausloesung, StringComparison.Ordinal), "aus: nennt die Auslösung für danach: " + hp.MithoerenText);

            gemeldet.Clear();
            hp.Mithoeren = true;
            Pruefe.Wahr(a.Maschine.Hoerprobe, "an");
            Pruefe.Wahr(gemeldet.Contains(nameof(hp.MithoerenText)), "Einschalten meldet den Text");
            Pruefe.Wahr(hp.MithoerenText.StartsWith("Du hörst mit", StringComparison.Ordinal) && hp.MithoerenText.Contains(ausloesung, StringComparison.Ordinal),
                "an: " + hp.MithoerenText);

            gemeldet.Clear();
            hp.Mithoeren = false;
            Pruefe.Wahr(gemeldet.Contains(nameof(hp.MithoerenText)) && hp.MithoerenText.StartsWith("Aus.", StringComparison.Ordinal),
                "Ausschalten meldet den Text: " + hp.MithoerenText);
        }

        // Von der anderen Ansicht umgeschaltet (Audio und Sprachaktivierung teilen die Maschine): Der Takt meldet den Text.
        var zweite = a.Schwelle().Hoerprobe;
        var zweiteGemeldet = new HashSet<string?>();
        zweite.PropertyChanged += (_, e) => zweiteGemeldet.Add(e.PropertyName);
        hp.Mithoeren = true;
        zweite.Takt();
        Pruefe.Wahr(zweiteGemeldet.Contains(nameof(zweite.MithoerenText)) && zweite.MithoerenText.StartsWith("Du hörst mit", StringComparison.Ordinal),
            "zweite Ansicht nach dem Takt: " + zweite.MithoerenText);
        zweiteGemeldet.Clear();
        a.Maschine.Hoerprobe = false;
        zweite.Takt();
        Pruefe.Wahr(zweiteGemeldet.Contains(nameof(zweite.MithoerenText)) && zweite.MithoerenText.StartsWith("Aus.", StringComparison.Ordinal),
            "an der Maschine ausgeschaltet: " + zweite.MithoerenText);
        zweiteGemeldet.Clear();
        zweite.Takt();
        Pruefe.Gleich(0, zweiteGemeldet.Count, "ohne Änderung meldet der Takt nichts");
    }

    [Test]
    public static async Task Hoerprobe_Probe_KnopfStartetZeigtFortschrittUndBricht()
    {
        using var a = new Aufbau();
        var audio = a.Audio();
        var hp = audio.Hoerprobe;

        // Ohne laufendes Audio gibt es nichts aufzunehmen: Hinweis statt Probe.
        hp.ProbeCommand.Execute(null);
        Pruefe.Gleich(0, a.Maschine.ProbeStarts, "ohne Audio keine Probe");
        Pruefe.Wahr(hp.ProbeText.Contains("Audio", StringComparison.Ordinal), "sagt, dass Audio fehlt: " + hp.ProbeText);

        await audio.StartenCommand.ExecuteAsync(null);
        hp.Takt();
        Pruefe.Gleich("", hp.ProbeText, "bereit: kein Stand neben dem Knopf");
        Pruefe.Wahr(HoerprobeViewModel.ProbeHinweis.Contains("5 Sekunden", StringComparison.Ordinal)
            && HoerprobeViewModel.ProbeHinweis.Contains("Lautsprecher", StringComparison.Ordinal), "Hinweis über dem Knopf: " + HoerprobeViewModel.ProbeHinweis);
        Pruefe.Gleich("aufnehmen", hp.ProbeKnopfZusatz, "Knopf bereit");

        hp.ProbeCommand.Execute(null);
        Pruefe.Gleich(1, a.Maschine.ProbeStarts, "gestartet");
        Pruefe.Wahr(hp.ProbeText.Contains("sprich jetzt", StringComparison.Ordinal), "gleich nach dem Klick: " + hp.ProbeText);
        Pruefe.Gleich("abbrechen", hp.ProbeKnopfZusatz, "Knopf bei laufender Probe");

        var gemeldet = new HashSet<string?>();
        hp.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);
        a.Maschine.Probe = new ProbeStand(ProbeZustand.Aufnahme, 2400);
        hp.Takt();
        Pruefe.Wahr(hp.ProbeText.Contains("Noch 3 s", StringComparison.Ordinal), "Rest aufgerundet: " + hp.ProbeText);
        Pruefe.Wahr(gemeldet.Contains(nameof(hp.ProbeText)), "Fortschritt gemeldet");

        a.Maschine.Probe = new ProbeStand(ProbeZustand.Wiedergabe, 4000);
        hp.Takt();
        Pruefe.Wahr(hp.ProbeText.Contains("So hören dich die anderen", StringComparison.Ordinal), "Wiedergabe: " + hp.ProbeText);
        Pruefe.Gleich("abbrechen", hp.ProbeKnopfZusatz, "Knopf bei Wiedergabe");

        hp.ProbeCommand.Execute(null);
        Pruefe.Gleich(1, a.Maschine.ProbeAbbrueche, "abgebrochen");
        Pruefe.Gleich(ProbeZustand.Bereit, a.Maschine.Probe.Zustand, "Maschine bereit");
        Pruefe.Gleich("aufnehmen", hp.ProbeKnopfZusatz, "Knopf wieder bereit");
    }

    [Test]
    public static void Hoerprobe_AudioUndSprachaktivierung_ZeigenDasselbe()
    {
        using var a = new Aufbau();
        _ = a.Haupt(out _, out var schwelle, out var audio, out _);
        Pruefe.Wahr(ReferenceEquals(audio.Hoerprobe, schwelle.Hoerprobe), "eine Hörprobe für Audio und Sprachaktivierung (wie in App.OnStartup)");

        // Auch eine zweite Instanz zeigt nach dem Takt, was die Maschine tut.
        var zweite = a.Schwelle().Hoerprobe;
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        var gemeldet = new HashSet<string?>();
        zweite.PropertyChanged += (_, e) => gemeldet.Add(e.PropertyName);
        audio.Hoerprobe.Mithoeren = true;
        zweite.Takt();
        Pruefe.Wahr(zweite.Mithoeren && gemeldet.Contains(nameof(zweite.Mithoeren)), "Mithören auch in der zweiten Ansicht an");
    }
}
