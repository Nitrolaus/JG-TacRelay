// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;
using Funk.Verwaltung;
using Funk.Verwaltung.Ansichten;
using Funk.Verwaltung.Modelle;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Wächter für den ganzen Verwaltungsteil (0.11.1 AP4, Sarevocs Bild vom 12.10.2026 vom echten PC: der Knopf "Mitglied vormerken" über der Überschrift
/// "Mitglieder", der Suchtext "Suchen (SC-Name, Discord-Name, ..." abgeschnitten). Jede Seite der Verwaltung, von "Verwaltung einrichten" bis
/// "Vorlagen und Sicherung", mit offenen Karten, Rückfragen und der Leiste des Entwurfs, im Hauptfenster bei 760, 960 und 1100 Breite und bei
/// 100 und 175 %: kein Knopf und kein Feld über einem Text, kein Text beschnitten oder mitten im Wort gebrochen, nichts ragt rechts hinaus, kein
/// gekürzter Text ohne Hilfetext, die Glasschatten frei. Dazu das Bild jeder Seite bei 760 (Bildlauf, kein Bildschirm). Der Klassenname steht
/// nach OberflaecheFensterTests (Bildläufe danach).
/// </summary>
internal static class OberflaecheVerwaltungLayoutTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static string T(FormattableString s) => s.ToString(De);

    /// <summary>Fensterbreiten: Klein mit der Navigation oben (760), knapp darüber (960) und die Startbreite mit der Navigation links (1100).</summary>
    private static readonly double[] Breiten = [760, 960, 1100];

    /// <summary>Jede Seite der Verwaltung in einem Zustand, der viel zeigt (offene Karte, Rückfrage, Fehler, Entwurf).</summary>
    private static readonly string[] Seiten =
    [
        "einrichten", "zweitfaktor", "notfallcodes", "anmelden", "bestaetigen", "antraege", "heute", "mitglieder", "katalog",
        "kanaele", "rollen", "einheiten", "phasen", "einsatzleitung", "werkzeuge",
    ];

    [Test]
    public static void Verwaltung_JedeSeite_760_960_1100_Bei100Und175_NichtsUeberText_NichtsAbgeschnitten_NichtsRagtHinaus()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            var befunde = new List<string>();
            var gezaehlt = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var seite in Seiten)
            {
                using var ordner = new Testordner();
                var d = new VerwaltungsdienstAttrappe { Ablageordner = ordner.Pfad, Eingeteilte = 12 };
                var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
                using var teil = new Verwaltungsteil(d, vm);
                Vorbereiten(d, vm, seite);
                using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Verwaltung = teil };
                a.Ablage.Aktuell.Audio.Hoerweise = Funk.Client.Ablage.Hoerweise.Headset;
                var h = a.Haupt(out var funkfeld, out _, out _, out _);
                a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = AdminStatus.Aktiv } });
                h.AktiverBereich = h.Bereiche.Single(b => b.Kennung == HauptViewModel.VerwaltungKennung);
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                foreach (var breite in Breiten)
                {
                    var bild = Ganz(haupt, breite);
                    var ansicht = Bildlauf.Nachfahren<VerwaltungAnsicht>(Bildlauf.Wurzel(haupt)).Single();
                    var was = T($"{seite}, {breite:0}");
                    Pruefe.Wahr(Erwartet(ansicht, seite), was + ": die Seite zeigt, was sie soll");
                    gezaehlt[seite] = gezaehlt.GetValueOrDefault(seite) + Pruefen(ansicht, was, befunde);
                    if (breite == 760 || seite == "mitglieder") Bildablage.Ablegen(Ausschnitt(haupt, bild, ansicht), T($"0111-7-verwaltung-{seite}-{breite:0}"));
                    // 175 %: dieselben Einheiten, in Bildpunkten mal 1,75; die Rundung des Layouts ist eine andere.
                    _ = Bildlauf.Zeichnen(haupt, 1.75, new Size(breite, bild.PixelHeight));
                    _ = Pruefen(ansicht, was + ", 175 %", befunde);
                }
                funkfeld.Dispose();
            }
            Pruefe.Wahr(gezaehlt.Count == Seiten.Length && gezaehlt.Values.All(n => n >= 20), "jede Seite mit genug Elementen geprüft: "
                + string.Join(", ", gezaehlt.Select(g => g.Key + " " + g.Value)));
            Pruefe.Gleich("", string.Join("\n", befunde.Distinct()), "Befunde im Verwaltungsteil");
        });
    }

    /// <summary>Überlappungen, Beschnitt, Überlauf, Kürzung ohne Hilfetext und angeschnittene Schatten auf einer Seite; liefert die Zahl der Elemente.</summary>
    private static int Pruefen(VerwaltungAnsicht ansicht, string was, List<string> befunde)
    {
        var liste = new List<string>();
        liste.AddRange(Abschnittbefunde(ansicht, t => HatHilfe(t, ansicht), out var n));
        liste.AddRange(Ueberlappungen(ansicht));
        liste.AddRange(GekuerztOhneHilfe(ansicht));
        befunde.AddRange(liste.Select(b => was + ": " + b));
        _ = OberflaecheStartseiteTests.SchattenPruefen(ansicht, was, befunde);
        return n;
    }

    /// <summary>Ein Ausschnitt um die Verwaltung (ohne die Navigation des Hauptfensters) aus dem Bild der ganzen Seite in Skala 1.</summary>
    private static CroppedBitmap Ausschnitt(Hauptfenster haupt, BitmapSource ganz, VerwaltungAnsicht ansicht)
    {
        var r = ansicht.TransformToAncestor(Bildlauf.Wurzel(haupt)).TransformBounds(new Rect(ansicht.RenderSize));
        var links = (int)Math.Max(0, Math.Floor(r.Left));
        var oben = (int)Math.Max(0, Math.Floor(r.Top));
        return new CroppedBitmap(ganz, new Int32Rect(links, oben, (int)Math.Min(ganz.PixelWidth - links, Math.Ceiling(r.Width)),
            (int)Math.Min(ganz.PixelHeight - oben, Math.Ceiling(r.Height))));
    }

    private static void Vorbereiten(VerwaltungsdienstAttrappe d, VerwaltungViewModel vm, string seite)
    {
        var f = vm.Flotte;
        switch (seite)
        {
            case "einrichten":
                d.LageSetzen(_ => new Verwaltungslage { Verbunden = true, Name = "Nordlicht", AntragMoeglich = true });
                vm.EinrichtenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                break;
            case "zweitfaktor":
                d.LageSetzen(_ => new Verwaltungslage { Verbunden = true, Name = "Nordlicht", Admin = AdminStatus.Einrichtung, SchluesselVorhanden = true });
                vm.GeheimnisAbholenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                vm.EinrichtungCode = "123";
                break;
            case "notfallcodes":
                d.LageSetzen(_ => new Verwaltungslage { Verbunden = true, Name = "Nordlicht", Admin = AdminStatus.Einrichtung, SchluesselVorhanden = true });
                vm.GeheimnisAbholenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                vm.EinrichtungCode = VerwaltungsdienstAttrappe.RichtigerCode;
                vm.EinrichtungBestaetigenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                break;
            case "anmelden":
                d.LageSetzen(_ => new Verwaltungslage { Verbunden = true, Name = "Nordlicht", Admin = AdminStatus.Aktiv, SchluesselVorhanden = true });
                vm.Code = "000000";
                vm.AnmeldenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                break;
            case "bestaetigen":
                vm.Antraege.Antraege[0].VerifizierenCommand.Execute(null);
                d.BrauchtCode = true;
                _ = vm.Antraege.Antraege[0].FreigebenCommand.ExecuteAsync(null);
                vm.Abfrage.Eingabe = "123 4";
                break;
            case "antraege":
                // Ein weiteres Gerät mit EINSPRUCH, eine Rückfrage zum Ablehnen und die Sammel-Rückfrage offen.
                d.Antraege.Add(OberflaecheVerwaltungTests.WeiteresGeraet(104, Markierung.Einspruch));
                vm.Antraege.LadenAsync().GetAwaiter().GetResult();
                vm.Antraege.Antraege.Single(z => z.GeraetId == 101).VerifizierenCommand.Execute(null);
                vm.Antraege.Antraege.Single(z => z.GeraetId == 102).AblehnenFragenOeffnenCommand.Execute(null);
                vm.Antraege.UnpassendeAblehnenFragenCommand.Execute(null);
                break;
            case "heute":
                vm.Bereich = Verwaltungsbereich.Flotte;
                f.RolleNeuCommand.Execute(null);
                f.NetzNeuCommand.Execute(null);
                vm.Bereich = Verwaltungsbereich.Heute;
                break;
            case "mitglieder":
                // Sarevocs Bild: Liste links, rechts die Karte eines Kontos mit Geräten, "Sperre aufheben ..." gefragt, "Konto löschen ..." gefragt,
                // dazu "Liste einfügen" mit einem Fehler je Zeile.
                vm.Bereich = Verwaltungsbereich.Mitglieder;
                vm.Mitglieder.OeffnenCommand.Execute(vm.Mitglieder.Sichtbar.Single(z => z.Eintrag.KontoId == 2));
                vm.Mitglieder.Karte!.Geraete.Single(g => g.ZeigtAufhebenKnopf).SperreAufhebenFragenCommand.Execute(null);
                vm.Mitglieder.Karte.KontoLoeschenFragenCommand.Execute(null);
                vm.Mitglieder.EinfuegenText = "Sturm; Sturm/Kai; ; Marine\nWind; ; ; ; zu viel";
                vm.Mitglieder.EinfuegenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                break;
            case "katalog":
                vm.Bereich = Verwaltungsbereich.Katalog;
                vm.Katalog.NeuName = "Scout";
                vm.Katalog.NeuGruppe = "Flug";
                vm.Katalog.HinzufuegenCommand.Execute(null);
                vm.Katalog.Eintraege.Single(e => e.Name == "Logistik").EntfernenCommand.Execute(null);
                break;
            case "kanaele":
                // Ein Kanal umbenannt, die Rückfrage im Raid offen, "Was ändert sich?" aufgeklappt.
                vm.IstKanaele = true;
                f.GewaehltesNetz = f.Netze.Single(n => n.Name == "Kommando");
                f.Netz!.Name = "Flottenkommando";
                f.UebernehmenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                f.WasAendertSichCommand.Execute(null);
                break;
            case "rollen":
                vm.IstRollen = true;
                f.GewaehlteRolle = f.Rollen.Single(r => r.Name == "Kapitän");
                f.Rolle!.VorschauEinheit = f.Rolle.VorschauEinheiten.Single(x => x.Text == "Schiff 2");
                f.Rolle.GewaehltesFeld = f.Rolle.Felder[2];
                break;
            case "einheiten":
                vm.IstEinheiten = true;
                f.BausteinTruppCommand.Execute(null);
                f.SpeichernCommand.Execute(null);
                break;
            case "phasen":
                vm.IstPhasen = true;
                f.PhaseNeuCommand.Execute(null);
                f.Phase!.Name = "Entern";
                f.Phase.Kurz = "ENTERN";
                f.Phase.GewaehlteRolle = f.Phase.Rollen.Single(r => r.Name == "Kapitän");
                f.Phase.VarianteAnlegenCommand.Execute(null);
                f.UebernehmenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                if (f.Rueckfrage is not null) f.JaUebernehmenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                d.LaufendePhase = 2;
                vm.Aktualisieren();
                f.Phase!.Variante!.Felder[8].Quelle = f.Phase.Variante.Quellen[0];
                f.Phase.Variante.VorschauEinheit = f.Phase.Variante.VorschauEinheiten.Single(x => x.Text == "Schiff 2");
                break;
            case "einsatzleitung":
                vm.Bereich = Verwaltungsbereich.Einsatzleitung;
                vm.Einsatzleitung.Konto = vm.Einsatzleitung.Kandidaten.Single(k => k.Text.StartsWith("Kranich", StringComparison.Ordinal));
                vm.Einsatzleitung.Grund = "Op Nachtfalke";
                break;
            default:
                f.SpeichernCommand.Execute(null);
                vm.Bereich = Verwaltungsbereich.Werkzeuge;
                f.Leeren.FragenCommand.Execute(null);
                break;
        }
    }

    /// <summary>Je Seite ein Text, der nur in ihrem vorbereiteten Zustand da ist; sonst prüfte der Wächter eine leere Seite.</summary>
    private static bool Erwartet(VerwaltungAnsicht ansicht, string seite) => seite switch
    {
        "einrichten" => Sichtbar(ansicht, VerwaltungsdienstAttrappe.Kennung),
        "zweitfaktor" => Sichtbar(ansicht, "JBSW Y3DP"),
        "notfallcodes" => Sichtbar(ansicht, "Deine zehn Notfallcodes"),
        "anmelden" => Sichtbar(ansicht, "passt nicht"),
        "bestaetigen" => Sichtbar(ansicht, "Gerät freigeben: Das braucht einen frischen Code"),
        "antraege" => Sichtbar(ansicht, "EINSPRUCH von einem freigegebenen Gerät des Kontos") && Sichtbar(ansicht, "Ablehnen und löschen"),
        "heute" => Sichtbar(ansicht, "Die Rolle Neue Rolle hat keine Felder"),
        "mitglieder" => Sichtbar(ansicht, "Mitglied vormerken") && Sichtbar(ansicht, "Konto Falke endgültig löschen?") && Sichtbar(ansicht, "Zeile 2: mehr als vier Teile")
            && Sichtbar(ansicht, "7Z3Q-K8M2-4F1D-B6A0"),
        "katalog" => Sichtbar(ansicht, "Beim Speichern fallen eine Rolle weg"),
        "kanaele" => Sichtbar(ansicht, "Betrifft 12 eingeteilte Personen") && Sichtbar(ansicht, "Was ändert sich?") && Sichtbar(ansicht, "umbenannt, vorher Kommando"),
        "rollen" => Sichtbar(ansicht, "Funkfeld von Kapitän") && Sichtbar(ansicht, "So sieht Kapitän auf Schiff 2 aus"),
        "einheiten" => Sichtbar(ansicht, "Trupp Bravo") && Sichtbar(ansicht, "Einheitskanäle"),
        "phasen" => Sichtbar(ansicht, "Kapitän in Phase 2 Entern") && Sichtbar(ansicht, "Feld 10: Global, fest"),
        "einsatzleitung" => Sichtbar(ansicht, "Kranich leitet ab jetzt bis"),
        _ => Sichtbar(ansicht, "Wirklich alles bis auf Global"),
    };
}
