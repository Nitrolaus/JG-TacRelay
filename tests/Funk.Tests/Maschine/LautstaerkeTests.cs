// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;
using Funk.Tests.Verarbeitung;

namespace Funk.Tests.Maschine;

/// <summary>Lautstärke und Einmessen über den Vertrag der Klangmaschine (Klangstufe A), mit Geräte-Attrappen.</summary>
internal static class LautstaerkeTests
{
    [Test]
    public static async Task Lautstaerke_Setzen_Bereinigt_UndWirkt()
    {
        await using var s = new Pruefstand { TelemetrieMitschreiben = true };
        Pruefe.Gleich(new LautstaerkeEinstellung(true, Pegelstufe.StartDb), s.Maschine.Lautstaerke, "Voreinstellung: automatisch ab dem Startwert");
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 9f);
        Pruefe.Gleich(new LautstaerkeEinstellung(false, 9f), s.Maschine.Lautstaerke, "gesetzt");
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, float.NaN);
        Pruefe.Gleich(new LautstaerkeEinstellung(false, Pegelstufe.StartDb), s.Maschine.Lautstaerke, "NaN wird der Startwert");
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 99f);
        Pruefe.Gleich(new LautstaerkeEinstellung(false, Pegelstufe.MaxDb), s.Maschine.Lautstaerke, "begrenzt");
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, -12.5f);
        await s.StartenAsync();
        s.Stille(5);
        Pruefe.Gleich(-12f, s.Telemetrie[^1].PegelstufeDb, "wirkt ab dem nächsten Rahmen, begrenzt auf die Untergrenze");
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 3f);
        s.Stille(1);
        Pruefe.Gleich(3f, s.Telemetrie[^1].PegelstufeDb, "neuer Wert");
    }

    [Test]
    public static async Task Lautstaerke_Fest0dB_BitgleichOhnePegelstufe_KeineZusatzlatenz()
    {
        // Pegelstufe und Begrenzer rechnen im Rahmen, ohne Verzögerung: Mit fest 0 dB und Sprache unter dem Knie des Begrenzers
        // sendet die Kette Byte für Byte dasselbe wie ohne beide (gleiche Pakete, gleiche Takte), also auch zur selben Zeit.
        var (sprache, _) = Testszenen.Sprache(20, 12);
        async Task<List<Gesendet>> Lauf(bool mitPegelstufe)
        {
            await using var s = new Pruefstand(mitPegelstufe: mitPegelstufe);
            if (mitPegelstufe) s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 0f);
            await s.StartenAsync();
            s.Abspielen(sprache);
            return s.Pakete;
        }
        var ohne = await Lauf(false);
        var mit = await Lauf(true);
        PruefeWert.Mindestens(100, ohne.Count(p => p.Opus.Length > 0), "es wurde gesendet");
        Pruefe.Gleich(ohne.Count, mit.Count, "gleich viele Pakete");
        for (var i = 0; i < ohne.Count; i++)
        {
            Pruefe.Gleich(ohne[i].Takt, mit[i].Takt, $"Paket {i}: gleicher Takt");
            Pruefe.Gleich(ohne[i].Kennzeichen, mit[i].Kennzeichen, $"Paket {i}: gleiche Kennzeichen");
            Pruefe.BytesGleich(ohne[i].Opus, mit[i].Opus, $"Paket {i}");
        }
    }

    [Test]
    public static async Task Einmessen_UeberDieMaschine()
    {
        await using var s = new Pruefstand();
        Pruefe.Gleich(EinmessZustand.Bereit, s.Maschine.Einmessen.Zustand, "bereit");
        s.Maschine.Lautstaerke = new LautstaerkeEinstellung(false, 0f);
        await s.StartenAsync();
        var eingang = Pumpmessung.Eingang(-10f);
        s.Abspielen(eingang.AsSpan(0, 48000));
        s.Maschine.EinmessenStarten();
        Pruefe.Gleich(EinmessZustand.Laeuft, s.Maschine.Einmessen.Zustand, "läuft, sobald gestartet");
        s.Abspielen(eingang.AsSpan(48000, 4 * 48000));
        var mitte = s.Maschine.Einmessen;
        Pruefe.Gleich(EinmessZustand.Laeuft, mitte.Zustand, "läuft noch");
        PruefeWert.Nahe(Pegelstufe.EinmessRahmen * 10 - 4000, mitte.RestMs, 20, "Rest");
        s.Abspielen(eingang.AsSpan(5 * 48000, 5 * 48000));
        var e = s.Maschine.Einmessen;
        Pruefe.Gleich(EinmessZustand.Fertig, e.Zustand, "fertig");
        Pruefe.Wahr(e.ErgebnisDb >= Pegelstufe.MinDb && e.ErgebnisDb <= Pegelstufe.MaxDb, $"Ergebnis {e.ErgebnisDb} dB");
        Pruefe.Gleich(new LautstaerkeEinstellung(false, 0f), s.Maschine.Lautstaerke, "das Ergebnis gilt erst, wenn es gesetzt wird");
        s.Maschine.EinmessenStarten();
        s.Maschine.EinmessenAbbrechen();
        s.Stille(1);
        Pruefe.Gleich(EinmessZustand.Bereit, s.Maschine.Einmessen.Zustand, "abgebrochen");
    }
}
