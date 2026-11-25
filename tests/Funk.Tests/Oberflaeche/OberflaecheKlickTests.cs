// JG TacRelay - Maintainer: Sarevoc
using System.Text.Json;
using System.Text.Json.Serialization;
using Funk.Client.Ablage;
using Funk.Client.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Funk-Klick (A63, seit 0.5.0; seit 0.6.0 Stufe 2b als Karte im Bereich Audio und ohne Wahl des Klangs): gespeichert in den
/// Client-Einstellungen unter <c>klick</c>, so dass ein Client 0.4.x die Datei weiter liest (er übergeht die unbekannte
/// Eigenschaft; ein unbekannter Enum-Name in einer bekannten legte die ganze Datei beiseite, CODEMAP). Ein <c>klang</c> aus
/// 0.5.x oder Stufe 1 und 2 wird übergangen, gleich welcher Wert. Das Modell gibt jede Änderung sofort an die Maschine und in die
/// Ablage, "Anhören" sagt, warum nichts zu hören ist.
/// </summary>
internal static class OberflaecheKlickTests
{
    /// <summary>Die Optionen von EinstellungsSpeicher 0.4.x (JsonStringEnumConverter, CamelCase, Populate, unbekannte Felder übergangen).</summary>
    private static readonly JsonSerializerOptions Alt04 = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    /// <summary>Ein Ausschnitt der Datei, wie Client 0.4.x sie liest: Tasten und Farbstellung, kein <c>klick</c>.</summary>
    private sealed class AltDatei
    {
        public AltTasten Tasten { get; } = new();

        public string? Farbstellung { get; set; }
    }

    private sealed class AltTasten
    {
        public bool Voreingestellt { get; set; }
    }

    [Test]
    public static void Datei_KlickMitVoreinstellung_UndClient04LiestSieWeiter()
    {
        using var o = new Testordner();
        var speicher = new EinstellungsSpeicher(o.Datei("client-einstellungen.json"));
        var e = speicher.Laden();
        Pruefe.Wahr(e.Klick is { EigeneAn: true, EigeneDb: -12, AndereAn: true, AndereDb: -15, BeiSprachaktivierung: true },
            "ab Werk: an, -12 und -15 dB, bei Sprachaktivierung dezent");
        var maschine = e.Klick.FuerMaschine(Hoerweise.Headset);
        Pruefe.Wahr(maschine is { EigeneAn: true, EigeneDb: -12f, AndereAn: true, AndereDb: -15f, SprachaktivierungAus: false },
            "für die Maschine wie FunkKlickEinstellungen ab Werk");
        e.Klick.EigeneDb = -9;
        e.Klick.AndereAn = false;
        e.Klick.BeiSprachaktivierung = false;
        speicher.Speichern(e);

        using (var json = JsonDocument.Parse(File.ReadAllBytes(speicher.Pfad)))
        {
            var k = json.RootElement.GetProperty("klick");
            Pruefe.Falsch(k.TryGetProperty("klang", out _), "kein Klang mehr in der Datei (Stufe 2b)");
            Pruefe.Gleich(-9.0, k.GetProperty("eigeneDb").GetDouble(), "eigene Lautstärke");
            Pruefe.Wahr(k.EnumerateObject().All(p => p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number),
                "nur Schalter und Zahlen");
        }
        // So liest Client 0.4.x: klick ist eine unbekannte Eigenschaft und wird übergangen, der Rest bleibt lesbar.
        var alt = JsonSerializer.Deserialize<AltDatei>(File.ReadAllBytes(speicher.Pfad), Alt04)!;
        Pruefe.Wahr(alt.Tasten.Voreingestellt && alt.Farbstellung is not null, "0.4.x liest Tasten und Farbstellung");

        var wieder = speicher.Laden();
        Pruefe.Gleich(null, speicher.LetzterFehler, "0.5.0 liest die eigene Datei");
        Pruefe.Wahr(wieder.Klick is { EigeneDb: -9, AndereAn: false, BeiSprachaktivierung: false }, "gemerkt");
        Pruefe.Wahr(wieder.Klick.FuerMaschine(Hoerweise.Headset) is { SprachaktivierungAus: true, AndereAn: false, EigeneDb: -9f }, "für die Maschine");
    }

    [Test]
    public static void Datei_KlangAusAlterDatei_UebergangenUndAusreisserBegrenzt()
    {
        using var o = new Testordner();
        var pfad = o.Datei("client-einstellungen.json");
        var speicher = new EinstellungsSpeicher(pfad);
        // Bis Stufe 2 stand der Klang als Name in der Datei; 0.5.x schrieb "Klick", wer umstellte "Doppelklick" oder "Piep".
        foreach (var klang in new[] { "\"Piep\"", "\"Doppelklick\"", "1", "\"Fanfare\"", "{ \"neu\": 3 }", "null" })
        {
            File.WriteAllText(pfad, "{ \"klick\": { \"eigeneAn\": false, \"eigeneDb\": 12, \"andereDb\": -99, \"klang\": " + klang
                + ", \"beiSprachaktivierung\": false, \"neuesFeld\": 3 }, \"farbstellung\": \"Heute\" }");
            var e = speicher.Laden();
            Pruefe.Gleich(null, speicher.LetzterFehler, "klang " + klang + " legt die Datei nicht beiseite");
            Pruefe.Wahr(e.Klick is { EigeneAn: false, EigeneDb: -6, AndereDb: -30, BeiSprachaktivierung: false },
                "klang " + klang + ": Lautstärken begrenzt, der Rest gilt");
            Pruefe.Gleich(Funk.Client.Design.Farbstellung.Anthrazit, e.Farbstellung, "der Rest der Datei gilt, das alte P1 wird Anthrazit");
            speicher.Speichern(e);
            Pruefe.Falsch(File.ReadAllText(pfad).Contains("\"klang\"", StringComparison.Ordinal), "klang " + klang + ": beim nächsten Speichern fort");
        }
        Pruefe.Gleich(0, Directory.GetFiles(o.Pfad, "*.defekt-*").Length, "nichts beiseitegelegt");
    }

    [Test]
    public static void Modell_GibtJedeAenderungAnMaschineUndAblage()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Klick.EigeneDb = -18;
        var k = new KlickViewModel(a.Maschine, a.Ablage);
        Pruefe.Wahr(a.Maschine.Klick is { EigeneDb: -18f }, "beim Start an die Maschine");
        Pruefe.Gleich("-18 dB", k.EigeneText, "Lautstärke als Text");
        Pruefe.Gleich("-15 dB", k.AndereText, "andere");

        var gemeldet = new List<string>();
        k.PropertyChanged += (_, p) => gemeldet.Add(p.PropertyName!);
        k.EigeneDb = -7.6;
        Pruefe.Wahr(a.Maschine.Klick.EigeneDb == -8f && a.Ablage.Aktuell.Klick.EigeneDb == -8 && k.EigeneText == "-8 dB", "auf ganze dB, an Maschine und Ablage");
        k.EigeneDb = 3;
        Pruefe.Gleich(-6f, a.Maschine.Klick.EigeneDb, "höchstens -6 dB");
        k.EigeneAn = false;
        k.AndereDb = -21;
        k.BeiSprachaktivierung = false;
        Pruefe.Wahr(a.Maschine.Klick is { EigeneAn: false, AndereDb: -21f, SprachaktivierungAus: true }, "alles bei der Maschine");
        Pruefe.Wahr(a.Ablage.Aktuell.Klick is { EigeneAn: false, AndereDb: -21, BeiSprachaktivierung: false }, "alles in der Ablage");
        Pruefe.Wahr(gemeldet.Contains(nameof(KlickViewModel.EigeneText)) && gemeldet.Contains(nameof(KlickViewModel.AndereText)), "Texte gemeldet");
        a.Ablage.Sichern();
        Pruefe.Wahr(a.Speicher.Laden().Klick is { EigeneAn: false, AndereDb: -21 }, "gespeichert");
    }

    [Test]
    public static void Anhoeren_SagtWarumNichtsZuHoerenIst()
    {
        using var a = new Aufbau();
        // Den eigenen Klick gibt es seit Klangstufe C1 nur mit Headset (Q6, eigener Test unten).
        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
        var k = new KlickViewModel(a.Maschine, a.Ablage);
        a.Maschine.KlickAnhoerenErgebnis = false;
        k.AnhoerenCommand.Execute(null);
        Pruefe.Gleich(1, a.Maschine.KlickAnhoerungen, "an die Maschine");
        Pruefe.Gleich(KlickViewModel.OhneWiedergabe, k.Hinweis, "ohne Wiedergabe");
        a.Maschine.KlickAnhoerenErgebnis = true;
        k.AnhoerenCommand.Execute(null);
        Pruefe.Gleich("Erst deine Taste, dann andere Sprecher.", k.Hinweis, "beide an");
        k.EigeneAn = false;
        k.AnhoerenCommand.Execute(null);
        Pruefe.Gleich("Andere Sprecher.", k.Hinweis, "nur andere");
        k.AndereAn = false;
        k.AnhoerenCommand.Execute(null);
        Pruefe.Gleich(3, a.Maschine.KlickAnhoerungen, "beide aus: nicht an die Maschine");
        Pruefe.Gleich("Beide Klicks sind aus.", k.Hinweis, "beide aus");
        k.EigeneAn = true;
        Pruefe.Gleich("", k.Hinweis, "eine Änderung nimmt den alten Hinweis weg");
    }

    /// <summary>
    /// Q6 (Sarevoc, Klangstufe C1): Den eigenen Klick hört man über Lautsprecher nicht, mit Headset schon; der Klick anderer Sprecher bleibt.
    /// Umstellen von Headset auf Lautsprecher im Bereich Audio gibt der Maschine sofort den passenden Klick, die Karte sagt warum.
    /// </summary>
    [Test]
    public static void C1_EigenerKlick_NurMitHeadset_AndereHoerenWeiter()
    {
        using var a = new Aufbau();
        var e = new ClientEinstellungen();
        Pruefe.Wahr(e.Klick.FuerMaschine(Hoerweise.Lautsprecher) is { EigeneAn: false, AndereAn: true }, "Lautsprecher: kein eigener Klick, andere klicken");
        Pruefe.Wahr(e.Klick.FuerMaschine(Hoerweise.Headset) is { EigeneAn: true, AndereAn: true }, "Headset: beide");
        Pruefe.Wahr(e.Klick.FuerMaschine(null) is { EigeneAn: false }, "noch nicht gefragt: kein eigener Klick");

        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Lautsprecher;
        var audio = a.Audio();
        Pruefe.Wahr(a.Maschine.Klick is { EigeneAn: false, AndereAn: true }, "Start mit Lautsprechern: kein eigener Klick an der Maschine");
        Pruefe.Wahr(audio.Klick.EigeneAn, "der Schalter bleibt, wie er ist");
        Pruefe.Wahr(audio.Klick.EigenerKlickHinweis.Contains("Lautsprecher", StringComparison.Ordinal), "die Karte sagt warum: " + audio.Klick.EigenerKlickHinweis);
        audio.Headset = true;
        Pruefe.Wahr(a.Maschine.Klick is { EigeneAn: true, AndereAn: true }, "auf Headset umgestellt: eigener Klick");
        Pruefe.Gleich("", audio.Klick.EigenerKlickHinweis, "mit Headset kein Hinweis");
        audio.Lautsprecher = true;
        Pruefe.Wahr(a.Maschine.Klick is { EigeneAn: false, AndereAn: true }, "zurück auf Lautsprecher: wieder ohne");
        var bereiche = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Fenster", "Bereiche.xaml"));
        var ab = bereiche.IndexOf("x:Name=\"KlickKarte\"", StringComparison.Ordinal);
        Pruefe.Wahr(bereiche[ab..].Contains("{Binding EigenerKlickHinweis", StringComparison.Ordinal), "die Karte zeigt den Hinweis");
    }

    /// <summary>Seit Stufe 2b trägt der Bereich Audio den Klick; die Sprachaktivierung hat nur noch ihre zwei Spalten.</summary>
    [Test]
    public static void Audio_HatDenKlick_UndXamlBindetAlles()
    {
        using var a = new Aufbau();
        var audio = a.Audio();
        Pruefe.Wahr(audio.Klick is not null && a.Maschine.Klick is { EigeneDb: -12f }, "der Bereich Audio trägt den Klick und stellt die Maschine ein");
        var fenster = Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Fenster");
        var bereiche = File.ReadAllText(Path.Combine(fenster, "Bereiche.xaml"));
        var ab = bereiche.IndexOf("x:Name=\"KlickKarte\"", StringComparison.Ordinal);
        Pruefe.Wahr(ab > 0, "Karte Funk-Klick im Bereich Audio");
        var karte = bereiche[ab..];
        karte = karte[..karte.IndexOf("</DataTemplate>", StringComparison.Ordinal)];
        foreach (var bindung in new[] { "EigeneAn", "EigeneDb", "EigeneText", "AndereAn", "AndereDb", "AndereText", "BeiSprachaktivierung", "AnhoerenCommand", "Hinweis" })
            Pruefe.Wahr(karte.Contains("{Binding " + bindung, StringComparison.Ordinal), "die Karte Funk-Klick bindet " + bindung);
        Pruefe.Falsch(karte.Contains("Klang", StringComparison.Ordinal), "keine Wahl des Klangs mehr");
        var schwelle = File.ReadAllText(Path.Combine(fenster, "Schwellenbereich.xaml"));
        Pruefe.Falsch(schwelle.Contains("{Binding Klick", StringComparison.Ordinal) || schwelle.Contains("{Binding EigeneAn", StringComparison.Ordinal),
            "die Sprachaktivierung zeigt keinen Klick mehr");
    }
}
