// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>
/// Halteregel der Pegelstufe in der echten Kette, außerhalb des Klangkorpus (Gegenprüfung Klangstufe A,
/// <see cref="MessungGegenpruefungKlangA"/>): Andere im Raum verschieben die Verstärkung nicht, ein Schritt fällt nie in eine laufende
/// Sendung, ein gelernter Startwert hält gegen einen untypischen ersten Satz, und ein anderes Mikrofon lernt neu.
/// </summary>
internal static class HalteregelTests
{
    private static readonly Lazy<float[]> Eingang0 = new(() => Pumpmessung.Eingang(0f));
    private static readonly Lazy<float[]> Eingang15 = new(() => Pumpmessung.Eingang(-15f));

    private static float[] Nutzer(bool leise, int von, int bis) => MessungGegenpruefungKlangA.Stueck(leise ? Eingang15.Value : Eingang0.Value, von, bis);

    /// <summary>Andere sprechen im Raum 20 dB leiser, 90 s lang (der Korpus mit eigenem Rauschen, ohne den Nutzer).</summary>
    private static float[] AndereImRaum() => Pumpmessung.Eingang(-20f, seed: 99)[..(90 * 48000)];

    private static float[] Pause(int sekunden) => AnbindungSignale.Rauschen(sekunden * 48000, Pumpmessung.GrundrauschenDbfs, 7);

    private static float Ende(MessungGegenpruefungKlangA.Lauf l, int abschnitt) => l.T[l.Abschnitte[abschnitt].Bis - 1].PegelstufeDb;

    [Test]
    public static async Task Halteregel_AndereImRaum_Sprechtaste_LerntNichts()
    {
        // Bis zur Gegenprüfung lernte die Halteregel jede Sprache im Mikrofon: 220 s andere im Raum (20 dB leiser) hoben die
        // Verstärkung um 19 dB, die eigene Stimme kam danach mit -11 dBFS und bis 19 dB Begrenzung an.
        var l = await MessungGegenpruefungKlangA.Ablauf(new LautstaerkeEinstellung(true, Pegelstufe.StartDb),
            [new("Nutzer", Nutzer(false, 0, 8), true, Taste: true), new("Pause", Pause(2), false), new("andere", AndereImRaum(), false)],
            sprachaktivierung: false);
        PruefeWert.Hoechstens(0.5, Ende(l, 2) - Ende(l, 1), $"Verstärkung nach 90 s anderer im Raum ohne Taste (vorher {Ende(l, 1):0.0} dB)");
    }

    [Test]
    public static async Task Halteregel_AndereImRaum_Sprachaktivierung_LerntNicht()
    {
        // Mit Sprachaktivierung gehen die anderen hinaus (das Gate kennt keine Stimmen); ihr Abstand zum Rauschen ist aber 20 dB
        // kleiner als der der eigenen Stimme, die Halteregel nimmt sie nicht als neue Lage.
        var l = await MessungGegenpruefungKlangA.Ablauf(new LautstaerkeEinstellung(true, Pegelstufe.StartDb),
            [new("Nutzer", Nutzer(false, 0, 8), true), new("Pause", Pause(2), false), new("andere", AndereImRaum(), false)]);
        PruefeWert.Hoechstens(1.0, Ende(l, 2) - Ende(l, 1), $"Verstärkung nach 90 s anderer im Raum mit Sprachaktivierung (vorher {Ende(l, 1):0.0} dB)");
    }

    [Test]
    public static async Task Halteregel_SprechtasteUeberMehrereSaetze_KeinSchrittInDerSendung()
    {
        // Kalt mit leisem Mikrofon (-15 dB): Der erste Schritt (bis 12 dB) kam bis zur Gegenprüfung nach 600 ms Pause, auch wenn die
        // Taste noch gedrückt war; die anderen hörten ihn mitten in der Sendung.
        var l = await MessungGegenpruefungKlangA.Ablauf(new LautstaerkeEinstellung(true, Pegelstufe.StartDb),
            [new("Taste 0-3", Nutzer(true, 0, 4), true, Taste: true), new("Pause", Pause(1), false),
             new("Taste 4-7", Nutzer(true, 4, 8), true, Taste: true), new("Pause", Pause(1), false)], sprachaktivierung: false);
        var t = l.T;
        var inSendung = 0;
        for (var f = 1; f < t.Count; f++)
        {
            if (t[f].PegelstufeDb != t[f - 1].PegelstufeDb && (t[f].Sendet || t[f - 1].Sendet)) inSendung++;
        }
        Pruefe.Gleich(0, inSendung, "Schritte der Verstärkung, während gesendet wird");
        PruefeWert.Mindestens(12.0, Ende(l, 3), "nach dem Loslassen hat sie trotzdem gelernt");
    }

    [Test]
    public static async Task Halteregel_Warmstart_ErsterRufVerschiebtNichts()
    {
        // Wie der Client eine frühere Sitzung fortsetzt (gelernt +3,4 dB beim Korpus mit 0 dB). Bis zur Gegenprüfung war der Wert nur ein
        // Startwert: Ein lauter Ruf als erster Satz senkte die Verstärkung um 12 dB, die normalen Sätze danach kamen 9 dB zu leise.
        var audio = new AudioEinstellungen { GelernteVerstaerkungDb = 3.4 };
        var ruf = MessungGegenpruefungKlangA.Begrenzt(MessungGegenpruefungKlangA.Stueck(Pumpmessung.Eingang(10f), 2, 3));
        var l = await MessungGegenpruefungKlangA.Ablauf(audio.LautstaerkeFuerMaschine(),
            [new("Ruf", ruf, true), new("normal", Nutzer(false, 8, 12), true)]);
        var groesster = 0f;
        for (var f = 1; f < l.T.Count; f++) groesster = MathF.Max(groesster, MathF.Abs(l.T[f].PegelstufeDb - l.T[f - 1].PegelstufeDb));
        PruefeWert.Hoechstens(1.0 + 1e-4, groesster, "größter Schritt nach einem lauten ersten Satz");
        PruefeWert.Nahe(3.4, Ende(l, 1), 1.5, "die gelernte Verstärkung bleibt");
    }

    [Test]
    public static async Task Halteregel_AnderesMikrofon_LerntNeu()
    {
        // Lernen auf Mikrofon A (0 dB), dann wird B (15 dB leiser) Standardgerät. Bis zur Gegenprüfung ging die Halteregel dort nur
        // in Schritten von 1 dB je Pause weiter; nach dem Wechsel lernt sie mit den Anfangsschritten neu.
        var a = Nutzer(false, 0, 8);
        var gewechselt = false;
        var l = await MessungGegenpruefungKlangA.Ablauf(new LautstaerkeEinstellung(true, Pegelstufe.StartDb),
            [new("A", a, true), new("B", Nutzer(true, 8, 15), true)],
            jeTakt: (s, _) =>
            {
                if (s.Telemetrie.Count != a.Length / 480 || s.System.Aufnahme?.GeraetId != "mic-1") return;
                s.System.StandardSetzen("mic-2", "spk-1");
                gewechselt = s.System.Aufnahme?.GeraetId == "mic-2";
            });
        Pruefe.Wahr(gewechselt, "Mikrofon B ist offen");
        PruefeWert.Mindestens(14.0, Ende(l, 1), $"nach dem Wechsel auf das leisere Mikrofon (braucht etwa +18 dB, vorher {Ende(l, 0):0.0} dB)");
    }
}
