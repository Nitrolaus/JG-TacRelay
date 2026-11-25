// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Vorschau 0.11.2 für Sarevoc (13.10.2026): Start mit "Senden" unten (Pegelbalken mit Griffen, Schiebeschalter, "Erweitert:") und die neue
/// Raid-Seite (links die Namen, Reiter, Stammbaum), beides im echten Hauptfenster bei 100 %, erfundene Namen, Kanäle der Vorlage Star Citizen. Mit
/// FUNK_BILDER entstehen vorschau-0112-start und vorschau-0112-raid.
/// </summary>
internal static class OberflaecheVorschau0112Tests
{
    [Test]
    public static void Vorschau0112_Start_und_Raid()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            var kanaele = FunkDienstAttrappe.KanalBeispiel();
            ushort K(string name, byte ch) => Kanalrechnung.Id(kanaele.Netze.Single(n => n.Name == name).Id, ch);
            IReadOnlyList<FeldInfo> Felder(int i) => i switch
            {
                0 or 1 => [new FeldInfo(0, K("Brücke", 1), true, true, true)],
                2 => [new FeldInfo(0, K("Brücke", 2), true, true, true)],
                3 => [new FeldInfo(0, K("Brücke", 3), true, true, true), new FeldInfo(1, K("Crew", 3), true, true, true)],
                4 => [new FeldInfo(0, K("Crew", 1), true, true, true)],
                5 => [new FeldInfo(0, K("Brücke", 1), true, true, true)],
                6 => [new FeldInfo(0, K("Brücke", 0), true, false, true)],
                _ => [],
            };
            var d = FunkDienstAttrappe.MitRaidabend();
            d.LageSetzen(l =>
            {
                var b = FunkDienstAttrappe.Belegungsstand(l with { Netze = kanaele.Netze, Rollen = kanaele.Rollen, ServerKanaele = true });
                return b with { Funker = [.. b.Funker.Select((f, i) => f with { Details = (f.Details ?? new FunkerDetails([], [], [])) with { Felder = Felder(i) } })] };
            });
            using var a = new Aufbau(dienst: d);
            a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
            a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            h.Brett.KartenAbwartenAsync().GetAwaiter().GetResult();
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);

            h.BereichWaehlenCommand.Execute("1");
            var start = new Size(1600, 1250);
            _ = Bildlauf.Zeichnen(haupt, 1, start);
            var (startBild, _) = Bildlauf.Zeichnen(haupt, 1, start);
            Bildablage.Ablegen(startBild, "vorschau-0112-start");

            // Einstellungen mit den Klappbereichen als Knoten (0.11.3, Sarevoc: "moderner, sichtbarer ausklappbar"), Tasten offen, die anderen zu.
            h.BereichWaehlenCommand.Execute("2");
            h.Einstellungen.AbschnittTasten!.Feinheiten.Offen = true;
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
            var (einstellungenBild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
            Bildablage.Ablegen(einstellungenBild, "vorschau-0113-einstellungen");
            h.Einstellungen.AbschnittTasten.Feinheiten.Offen = false;

            h.BereichWaehlenCommand.Execute("5");
            var raid = new Size(1920, 1080);
            _ = Bildlauf.Zeichnen(haupt, 1, raid);
            var (raidBild, _) = Bildlauf.Zeichnen(haupt, 1, raid);
            Bildablage.Ablegen(raidBild, "vorschau-0112-raid");
            // Seit 0.11.3 eine Zeile: ganz links die Rollenwahl wie im Funkfeld (die 14 Rollen der Vorlage Star Citizen, "1 Standard (Gast)" zuerst),
            // daneben die ersten fünf Kanäle der Rolle.
            Pruefe.Gleich("1 Standard (Gast)", h.Brett.Rollenwahl[0].Name, "die Rollenwahl beginnt mit 1 Standard (Gast)");
            Pruefe.Gleich(14, h.Brett.Rollenwahl.Count, "die 14 Rollen der Vorlage");
            Pruefe.Gleich("Allgemein|Flotte", string.Join("|", h.Brett.Reiter.Select(r => r.Name)), "1 Standard (Gast): ihre zwei Kanäle");
            h.Brett.RolleWaehlenCommand.Execute(h.Brett.Rollenwahl.Single(r => r.Name == "3 Kapitän"));
            Pruefe.Gleich("Brücke|Crew|Flotte|Kommando|Sanität", string.Join("|", h.Brett.Reiter.Select(r => r.Name)), "3 Kapitän: die ersten fünf Kanäle");
            h.Brett.ReiterWaehlenCommand.Execute(h.Brett.Reiter.Single(r => r.Name == "Brücke"));
            Pruefe.Wahr(h.Brett.Baum.Single() is { Name: "Brücke", Offen: true }, "darunter der Stammbaum von Brücke");
            _ = Bildlauf.Zeichnen(haupt, 1, raid);
            var (reiterBild, _) = Bildlauf.Zeichnen(haupt, 1, raid);
            Bildablage.Ablegen(reiterBild, "vorschau-0113-raid-reiter");
            funkfeld.Dispose();
        });
    }

    /// <summary>
    /// Vorschau 0.11.5 für Sarevoc (14.10.2026): "Wer ist da" im Hauptfenster bei 1920 x 1080 und 100 %, als Einsatzleitung im Admin-Setup, über dem
    /// Beispielabend mit erfundenen Namen (drei mit 22 Zeichen, einer davon in Großbuchstaben, und einer mit 32, beide kleiner gesetzt statt gekürzt),
    /// "auch offline" an. Je Zeile Name, Mikrofon, Status und Wippe: Anker ist stumm (Mikrofon blutrot), Anker und Quarz (nur Einheitsnetze), Hagel,
    /// Pollux und Zinnober sind in keinem Kanal der Raid-Seite (gelb), Hagel und Kranich sind offline, Brandung ist bei dir 4 dB lauter gestellt. Mit
    /// FUNK_BILDER entsteht vorschau-0115-weristda.
    /// </summary>
    [Test]
    public static void Vorschau0115_WerIstDa()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbvorschlaege.Voreinstellung);
            var dienst = new VerwaltungsdienstAttrappe();
            using var teil = new Funk.Verwaltung.Verwaltungsteil(dienst, new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst));
            using var a = new Aufbau(dienst: OberflaecheWerIstDaTests.Abend()) { Verwaltung = teil };
            a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
            a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
            var h = a.Haupt(out var funkfeld, out _, out _, out var netze);
            // Mustermann leitet den Abend als Admin im Admin-Setup (Verwaltung da): Name, Mikrofon und Status sind bedienbar.
            a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Stufe.Admin, Admin = AdminStatus.Aktiv } });
            h.BereichWaehlenCommand.Execute("1");
            var w = h.WerIstDa;
            w.UmschaltenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            w.AuchOffline = true;
            netze.PegelSetzenAsync(FunkDienstAttrappe.Brandung, 4).GetAwaiter().GetResult();
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            var groesse = new Size(1920, 1080);
            _ = Bildlauf.Zeichnen(haupt, 1, groesse);
            var (bild, _) = Bildlauf.Zeichnen(haupt, 1, groesse);
            Pruefe.Wahr(w.Zeilen.Any(z => z.Stumm) && w.Zeilen.Any(z => z.Kanalstand == Kanalstand.OhneKanal) && w.Zeilen.Any(z => !z.Online)
                && w.Zeilen.Any(z => z.PegelDb > 0), "stumm, ohne Kanal, offline und lauter gestellt im Bild");
            Pruefe.Wahr(w.Zeilen.All(z => z.NameKlickbar && z.StatusKlickbar) && w.Zeilen.Where(z => !z.Eigene && z.StummBekannt).All(z => z.MikroKlickbar),
                "als Einsatzleitung im Admin-Setup ist alles bedienbar");
            Bildablage.Ablegen(bild, "vorschau-0115-weristda");
            funkfeld.Dispose();
        });
    }
}
