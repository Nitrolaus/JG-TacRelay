// JG TacRelay - Maintainer: Sarevoc
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung.Modelle;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Bereich "Einsatzleitung" der Verwaltung (RAIDLEITUNG-ENTWURF, Flotten-Einstellungsseite, Absatz Einsatzleitung; 6.12): die
/// laufenden und geplanten Aufträge mit Name, Zeit, Rechten und wer sie erteilt hat; "Einsatzleitung vergeben" für jetzt oder
/// geplant, mit denselben Regeln wie der Server (höchstens 14 Tage voraus, höchstens 24 Stunden, Anlass bis 48 Zeichen,
/// höchstens 4 Aufträge, ZUWEISEN und SPRECHANZEIGE_ALLE immer), "Entziehen"; beides mit frischem Code. Über der Attrappe.
/// </summary>
internal static class OberflaecheEinsatzleitungTests
{
    private static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();

    private static (VerwaltungsdienstAttrappe D, VerwaltungViewModel Vm) Aufbauen()
    {
        var d = new VerwaltungsdienstAttrappe();
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        vm.Bereich = Verwaltungsbereich.Einsatzleitung;
        return (d, vm);
    }

    [Test]
    public static async Task Einsatzleitung_ListeUndVergebenMitCode()
    {
        var (d, vm) = Aufbauen();
        using var _ = vm;
        var e = vm.Einsatzleitung;
        Pruefe.Wahr(vm.IstEinsatzleitung && e.Auftraege.Count == 1, "ein laufender Auftrag: " + e.Auftraege.Count);
        var z = e.Auftraege[0];
        var morgen = Auftragszeit.NaechsterMorgen(Ms(VerwaltungsdienstAttrappe.Abend));
        Pruefe.Wahr(z is { Name: "Falke", Laeuft: true, Von: "von Nordlicht", Grund: "Op Nachtfalke" }, "Name, Zustand, Urheber, Anlass");
        Pruefe.Gleich("läuft bis " + Auftragszeit.Anzeigen(morgen), z.Zeit, "Zeit");
        Pruefe.Gleich("einteilen, alle Sprecher sehen, in Global senden, Durchsagen, Durchsagen an alle, in mehrere Kanäle zugleich senden", z.Rechte, "Rechte im Klartext");

        // Wer einen Auftrag bekommen kann: Konten ohne ADMIN, keine Vormerkungen, keine Anträge.
        Pruefe.Gleich("Falke (Falke/Tom) | Kobold | Kranich (Kranich/Ben)", string.Join(" | ", e.Kandidaten.Select(k => k.Text)), "Auswahl");
        e.Suche = "kra";
        Pruefe.Gleich("Kranich (Kranich/Ben)", string.Join(" | ", e.Kandidaten.Select(k => k.Text)), "Suche");
        Pruefe.Falsch(e.VergebenCommand.CanExecute(null), "ohne Konto nichts zu vergeben");
        e.Konto = e.Kandidaten[0];
        var dauer = TimeSpan.FromMilliseconds(morgen - Ms(VerwaltungsdienstAttrappe.Abend));
        Pruefe.Gleich("Kranich leitet ab jetzt bis " + Auftragszeit.Anzeigen(morgen) + " (" + (int)dauer.TotalHours + " h " + dauer.Minutes + " min).", e.Vorschau, "Vorschau");
        Pruefe.Gleich(null, e.Pruefung, "nichts zu beanstanden");
        e.Durchsage = false;
        e.Grund = "Op Nachtfalke";

        d.BrauchtCode = true;
        var laeuft = e.VergebenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Abfrage.Offen && vm.Abfrage.Frage!.StartsWith("Einsatzleitung vergeben", StringComparison.Ordinal), "frischer Code: " + vm.Abfrage.Frage);
        vm.Abfrage.Eingabe = VerwaltungsdienstAttrappe.RichtigerCode;
        vm.Abfrage.OkCommand.Execute(null);
        await laeuft;
        Pruefe.Gleich("Auftrag 5 von jetzt bis 04:00 [GlobalSenden,DurchsageAlle,MehrfachSenden,Zuweisen,SprechanzeigeAlle] Op Nachtfalke", d.Aufrufe[^1],
            "ohne Angabe: jetzt bis zum nächsten Morgen, ZUWEISEN und SPRECHANZEIGE_ALLE immer");
        Pruefe.Wahr(e.Auftraege.Count == 2 && e.Auftraege.Any(a => a.Name == "Kranich"), "Liste neu");
        Pruefe.Gleich("Einsatzleitung für Kranich vergeben.", e.Hinweis, "Hinweis");
        Pruefe.Wahr(e.Konto is null && e.Grund.Length == 0 && e.Durchsage, "Formular wieder leer");
    }

    [Test]
    public static void Einsatzleitung_ZeitenUndGrenzenWieDerServer()
    {
        var (d, vm) = Aufbauen();
        using var _ = vm;
        var e = vm.Einsatzleitung;
        e.Konto = e.Kandidaten.Single(k => k.Text == "Kobold");

        e.Ab = "2026-10-20 20:00";
        Pruefe.Gleich("Ein Auftrag beginnt höchstens 14 Tage im Voraus.", e.Pruefung, "14 Tage");
        Pruefe.Falsch(e.VergebenCommand.CanExecute(null), "gesperrt");
        e.Ab = "02.10. 20:00";
        e.Bis = "02.10. 19:00";
        Pruefe.Gleich("Das Ende liegt nicht nach dem Beginn.", e.Pruefung, "Ende vor Beginn");
        e.Bis = "2026-10-03 21:00";
        Pruefe.Gleich("Ein Auftrag dauert höchstens 24 Stunden.", e.Pruefung, "24 Stunden");
        e.Bis = "";
        Pruefe.Gleich(null, e.Pruefung, "bis zum nächsten Morgen");
        Pruefe.Gleich("Kobold leitet ab 02.10. 20:00 bis 03.10. 04:00 (8 h 0 min).", e.Vorschau, "geplant, deutsch geschrieben");
        e.Ab = "morgen";
        Pruefe.Gleich("Die Zeit \"morgen\" verstehe ich nicht. So geht es: 20:00, 03.10. 04:00 oder 2026-10-03 04:00.", e.Pruefung, "unlesbar");
        e.Ab = "";
        e.Grund = new string('x', 49);
        Pruefe.Gleich("Der Anlass hat höchstens 48 Zeichen.", e.Pruefung, "Anlass");
        e.Grund = "";

        var bis = Ms(VerwaltungsdienstAttrappe.Abend.AddHours(5));
        foreach (var (konto, name) in new (uint, string)[] { (3, "Kobold"), (9, "Wiesel"), (10, "Igel") })
            d.Auftraege.Add(new BeauftragungEintrag { KontoId = konto, Name = name, Von = Ms(VerwaltungsdienstAttrappe.Abend), Bis = bis, Rechte = [Recht.Zuweisen, Recht.SprechanzeigeAlle], Durch = 4, Seit = 0, Aktiv = true });
        d.Senden(new Meldung { Art = MeldungsArten.Beauftragung });
        Pruefe.Gleich(4, e.Auftraege.Count, "Meldung BEAUFTRAGUNG lädt neu");
        e.Konto = e.Kandidaten.Single(k => k.Text == "Kobold");
        Pruefe.Gleich(null, e.Pruefung, "wer schon einen hat, bekommt ihn ersetzt");
        e.Konto = e.Kandidaten.Single(k => k.Text.StartsWith("Kranich", StringComparison.Ordinal));
        Pruefe.Gleich("Es laufen schon 4 Aufträge (laufend oder geplant).", e.Pruefung, "höchstens 4");
    }

    [Test]
    public static async Task Einsatzleitung_Entziehen()
    {
        var (d, vm) = Aufbauen();
        using var _ = vm;
        var e = vm.Einsatzleitung;
        d.BrauchtCode = true;
        var laeuft = e.EntziehenCommand.ExecuteAsync(e.Auftraege[0]);
        Pruefe.Wahr(vm.Abfrage.Offen && vm.Abfrage.Frage!.StartsWith("Einsatzleitung entziehen", StringComparison.Ordinal), "frischer Code");
        vm.Abfrage.Eingabe = VerwaltungsdienstAttrappe.RichtigerCode;
        vm.Abfrage.OkCommand.Execute(null);
        await laeuft;
        Pruefe.Gleich("Entziehen 2", d.Aufrufe[^2], "Konto, danach die Liste neu");
        Pruefe.Wahr(e.Auftraege.Count == 0 && e.Leer, "keiner mehr");
        Pruefe.Gleich("Einsatzleitung von Falke entzogen.", e.Hinweis, "Hinweis");
    }
}
