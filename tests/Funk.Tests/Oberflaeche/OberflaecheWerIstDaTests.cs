// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.ClientKern;
using Funk.Protokoll.Steuerung;
using static Funk.Tests.Oberflaeche.Ansichtspruefung;
using R = Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// "Wer ist da" (A48) seit 0.11.5 (Sarevoc 13. und 14.10.2026): eine flache Liste, eine Zeile je Person, online zuerst, dann nach Rang (Admin, Führung,
/// Einsatzleitung, alle anderen), dann nach Name; je Zeile der Name-Knopf (alle gleich breit, nie gekürzt, ein breiter Name kleiner), das Mikrofon
/// (Einsatzleitung schaltet stumm, rot über Pinsel.RotSchrift), der Status (grün, gelb, grau wie die Zahl auf der Raid-Seite; im Admin-Setup ein Sprung
/// in die Verwaltung) und die Wippe (links leiser, rechts lauter, je 2 dB). Mit den Befunden der Gegenprüfung 0.11.5: Stufe aus dem
/// Kontenverzeichnis, fremde Stummschaltungen nur mit Recht, Ablauf im Takt, Sprung nach der Anmeldung zu Mitglieder, eingerichteter Admin für den
/// Status. Bis 0.11.4 Kacheln drei je Reihe in Gruppen, das gelbe "!" und die Mitgliedskarte. Dazu "Ich" unter
/// Einstellungen mit Namen, Rollen und Zeichen (A46, A49, Antwort 4), die Namen aus dem Setup (anmeldung.json, A42) und die eigenen Zeichen
/// (Kontrast, Vergleichsbild der drei Kranzformen). Die Ansichten werden im Speicher gezeichnet (<see cref="Bildlauf"/>); mit <c>FUNK_BILDER</c>
/// entstehen weristda-zeilen-anthrazit, weristda-zeilen-175, weristda-offline-anthrazit, einstellungen-ich und zeichen-varianten.
/// </summary>
internal static class OberflaecheWerIstDaTests
{
    /// <summary>Die Zeile eines Kontos.</summary>
    private static WerIstDaZeile Z(WerIstDaViewModel w, uint konto) => w.Zeilen.Single(z => z.KontoId == konto);

    /// <summary>Die Namen in der Reihenfolge der Liste, mit "|" getrennt.</summary>
    private static string Namen(WerIstDaViewModel w) => string.Join("|", w.Zeilen.Select(z => z.Name));

    private static int Aufrufe(FunkDienstAttrappe d, string anfang) => d.Aufrufe.Count(x => x.StartsWith(anfang, StringComparison.Ordinal));

    private static string Letzter(FunkDienstAttrappe d, string anfang) => d.Aufrufe.Last(x => x.StartsWith(anfang, StringComparison.Ordinal));

    /// <summary>
    /// Die flache Liste (Sarevoc 13.10.2026: "alle untereinander ... keine Gruppierung nach Untergilde"): online zuerst, dann Admin, Führung,
    /// Einsatzleitung, alle anderen, dann der Name. Keine Gruppen, keine Wahl der Gruppierung, keine Mitgliedskarte (also auch kein Rollenkatalog
    /// und keine Karte vom Server), im Hilfetext kein Kürzel; die Suche bleibt, seit 0.11.8 nur über Name, SC-Name und Discord-Name.
    /// </summary>
    [Test]
    public static async Task WerIstDa_FlacheListe_ReihenfolgeSucheUndZeichen()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var w = new WerIstDaViewModel(a.Dienst, a.Ablage, a.Takt);
        Pruefe.Gleich("Wer ist da (6)", w.KnopfText, "sechs online, Eule nicht");
        Pruefe.Falsch(w.Offen, "zu, bis man klickt");
        await w.UmschaltenCommand.ExecuteAsync(null);
        Pruefe.Wahr(w.Offen && !a.Dienst.Aufrufe.Any(x => x.Contains("Untergild", StringComparison.Ordinal)), "offen, seit 0.11.8 ohne Untergilden: " + string.Join(" | ", a.Dienst.Aufrufe));
        Pruefe.Falsch(typeof(IFunkDienst).GetMethod("UntergildenAsync") is not null, "der Client fragt 0x001D nicht mehr");
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x == "Rollenkatalog" || x.StartsWith("Karte ", StringComparison.Ordinal)), "keine Mitgliedskarte mehr: " + string.Join(" | ", a.Dienst.Aufrufe));
        foreach (var weg in new[] { "Gruppen", "Gruppierung", "NachUntergilde", "NachEinheit", "Karte", "HatKarte", "OeffnenCommand", "KarteSchliessenCommand" })
            Pruefe.Falsch(typeof(WerIstDaViewModel).GetProperty(weg) is not null, "kein " + weg + " mehr");
        Pruefe.Falsch(typeof(WerIstDaZeile).GetProperty("Kuerzel") is not null || typeof(WerIstDaZeile).GetProperty("Dauersend") is not null, "keine Untergilde und kein \"!\" an der Zeile");

        // Nordlicht ist Admin, Max Führung, dann die übrigen nach Namen.
        Pruefe.Gleich("Nordlicht/Anna|Mustermann/Max|Falke/Tom|Kobold|Kranich/Ben|Sturmvogel", Namen(w), "online, Rang, Name");
        Pruefe.Wahr(w.Zeilen.Select(z => z.Rang).SequenceEqual([0, 1, 3, 3, 3, 3]), "Rang: " + string.Join(",", w.Zeilen.Select(z => z.Rang)));
        var max = Z(w, 1);
        Pruefe.Wahr(max is { Name: "Mustermann/Max", ScName: "Mustermann", Platz: "Schiff 1, Kapitän", Eigene: true, Online: true, HatAbzeichen: true, Anfang: "M" }
            && string.Join("|", max.Zeichen.Select(z => z.Titel)) == "Navy: Fähnrich|Funker" && ReferenceEquals(max.Abzeichen, max.Zeichen[1].Bild),
            "Discord-Name vorn; seit 0.11.7 FUEHRUNG ohne Bild, die gewählte Tätigkeit Funker vorn");
        Pruefe.Gleich("Mustermann/Max, SC Mustermann, Schiff 1, Kapitän, Navy: Fähnrich, Funker, online", max.Beschreibung, "Hilfetext ohne Kürzel der Untergilde");
        Pruefe.Gleich(null, Z(w, 3).ScName, "gleicher Name steht nicht doppelt");
        Pruefe.Wahr(Z(w, 3) is { HatAbzeichen: false, Abzeichen: null, Anfang: "K" }, "Kobold ohne Zeichen: Anfangsbuchstabe");
        Pruefe.Gleich("Jäger", Z(w, 7).Platz, "Sturmvogel: Rolle ohne Schiff");

        w.Suche = "max";
        Pruefe.Gleich("Mustermann/Max", Namen(w), "max findet Mustermann/Max");
        w.Suche = "bs";
        Pruefe.Gleich(0, w.Zeilen.Count, "Kürzel BS findet seit 0.11.8 niemanden");
        w.Suche = "nordlicht";
        Pruefe.Gleich("Nordlicht/Anna", Namen(w), "SC-Name und Discord-Name");
        w.Suche = "niemand";
        Pruefe.Wahr(w.Zeilen.Count == 0 && w.Leer == "Die Suche findet niemanden.", "leere Suche sagt es");
        w.Suche = "";
        Pruefe.Gleich(6, w.Zeilen.Count, "alle wieder da");

        // Zeichen neben Namen abgeschaltet: keine Abzeichen mehr; die Reihenfolge nach Rang bleibt.
        a.Ablage.Aktuell.Anzeige.ZeichenNebenNamen = false;
        w.Aktualisieren();
        Pruefe.Wahr(w.Zeilen.All(z => z is { HatAbzeichen: false, Zeichen.Count: 0 }) && Namen(w).StartsWith("Nordlicht/Anna|Mustermann/Max", StringComparison.Ordinal), "Zeichen aus");

        a.Dienst.LageSetzen(_ => FunkLage.Leer);
        Pruefe.Wahr(w.KnopfText == "Wer ist da" && w.Leer == "Nicht verbunden." && w.Zeilen.Count == 0, "getrennt");
        w.SchliessenCommand.Execute(null);
        Pruefe.Falsch(w.Offen, "geschlossen");
    }

    /// <summary>
    /// Gegenprüfung (Echtzeit): Jeder Push, auch jede Sprechanzeige, meldet eine neue Lage (FunkClientDienst.Neu). "Wer ist da"
    /// behält je Konto seine Zeile und meldet nur, was sich ändert; sonst ersetzte jeder Funkspruch alle Knöpfe der offenen
    /// Spalte, der Fokus spränge weg, und ein Klick zwischen Drücken und Loslassen ginge verloren. "Ich" behält ungespeicherte
    /// Häkchen der Lieblingsrollen, solange der Server an den eigenen Rollen und am Katalog nichts ändert.
    /// </summary>
    [Test]
    public static async Task WerIstDaUndIch_FunkverkehrBautNichtNeu()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var w = new WerIstDaViewModel(a.Dienst, a.Ablage, a.Takt);
        await w.UmschaltenCommand.ExecuteAsync(null);
        var zeilen = w.Zeilen.ToList();
        var geaendert = 0;
        var gemeldet = new List<string>();
        w.Zeilen.CollectionChanged += (_, _) => geaendert++;
        foreach (var z in zeilen) z.PropertyChanged += (_, e) => gemeldet.Add(z.Name + "." + e.PropertyName);
        a.Dienst.LageSetzen(l => l with { Sprecher = [new SprecherInfo(4, "Nordlicht", [1], false)] });
        a.Dienst.LageSetzen(l => l with { Sprecher = [] });
        Pruefe.Gleich(0, geaendert, "Sprechanzeige: die Liste bleibt");
        Pruefe.Gleich("", string.Join(" ", gemeldet), "Sprechanzeige: keine Zeile meldet etwas");
        Pruefe.Wahr(w.Zeilen.SequenceEqual(zeilen), "dieselben Zeilen");

        // Gegenprobe: Wer geht, verschwindet; die anderen behalten ihre Zeile.
        a.Dienst.LageSetzen(l => l with { Funker = [.. l.Funker.Where(f => f.KontoId != 4)] });
        Pruefe.Wahr(geaendert > 0 && w.KnopfText == "Wer ist da (5)" && w.Zeilen.All(z => z.KontoId != 4), "Nordlicht offline: weg");
        Pruefe.Wahr(w.Zeilen.All(z => zeilen.Contains(z)), "die übrigen Zeilen bleiben dieselben");

        var m = new MeinProfilViewModel(a.Dienst, a.Ablage, a.Takt);
        await m.LadenAsync();
        var marine = m.Wuensche.Single(r => r.Id == 4);
        marine.Gewaehlt = false;
        a.Dienst.LageSetzen(l => l with { Sprecher = [new SprecherInfo(2, "Falke", [1], false)] });
        Pruefe.Wahr(m.Wuensche.Contains(marine) && !marine.Gewaehlt, "Funkspruch: das ungespeicherte Häkchen bleibt");

        // Gegenprobe: Ändert der Server die eigenen Rollen, gilt sein Stand.
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Lieblingsrollen = [4, 5] } });
        Pruefe.Wahr(!m.Wuensche.Contains(marine) && m.Wuensche.Single(r => r.Id == 4).Gewaehlt && m.Wuensche.Single(r => r.Id == 5).Gewaehlt, "neuer Stand vom Server");
    }

    /// <summary>
    /// "auch offline" gibt es nur für die Einsatzleitung (0.11.0): Dann kommen die Offline-Konten aus Praesenz und Kontenverzeichnis dazu, in der
    /// flachen Liste nach den Angemeldeten, mit Suche; der Knopf zählt weiter nur die Angemeldeten. Das Verzeichnis kommt höchstens alle 5 s, ein
    /// Fehler lässt die alte Liste stehen und geht nach einem erfolgreichen Holen oder beim Ausschalten weg. Endet die Einsatzleitung, geht der
    /// Schalter aus. Ein Funkspruch baut nichts neu.
    /// </summary>
    [Test]
    public static async Task WerIstDa_AuchOfflineNurFuerDieEinsatzleitung()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var jetzt = FunkDienstAttrappe.Abend;
        var w = new WerIstDaViewModel(a.Dienst, a.Ablage, a.Takt, jetzt: () => jetzt);
        await w.UmschaltenCommand.ExecuteAsync(null);
        int Verzeichnisse() => a.Dienst.Aufrufe.Count(x => x == "Verzeichnis");

        // Ein Funker ohne ZUWEISEN: kein Schalter, und Setzen nützt nichts (kein Verzeichnis, nur Angemeldete).
        Pruefe.Falsch(w.AuchOfflineSichtbar, "Funker: kein Schalter");
        w.AuchOffline = true;
        Pruefe.Wahr(!w.AuchOffline && Verzeichnisse() == 0 && w.Zeilen.All(z => z.Online), "Funker: bleibt aus");

        // Einsatzleitung: zwei Konten nur im Verzeichnis, eines davon reserviert auf Schiff 2; Eule ist offline in der Praesenz.
        a.Dienst.OhnePraesenz.Add(new VerzeichnisEintrag
        {
            KontoId = 20, Name = "Orion", Benutzername = "orion", ScName = "Orion", DiscordName = "Orion/Ole", Stufe = Stufe.Funker, Online = false,
        });
        a.Dienst.OhnePraesenz.Add(new VerzeichnisEintrag
        {
            KontoId = 21, Name = "Basalt", Benutzername = "basalt", Stufe = Stufe.Funker, Online = false, SchiffId = 2, RolleId = 3, Platz = PlatzStatus.Reserviert,
        });
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Rechte = [.. l.Konto!.Rechte, Recht.Zuweisen] } });
        Pruefe.Wahr(w.AuchOfflineSichtbar && !w.AuchOffline, "Einsatzleitung: der Schalter steht da, aus");
        w.AuchOffline = true;
        Pruefe.Gleich(1, Verzeichnisse(), "beim Einschalten das Verzeichnis");
        Pruefe.Wahr(w.KnopfText == "Wer ist da (6)" && w.Anzahl == 6, "der Knopf zählt nur Angemeldete: " + w.KnopfText);
        Pruefe.Gleich("Nordlicht/Anna|Mustermann/Max|Falke/Tom|Kobold|Kranich/Ben|Sturmvogel|Basalt|Eule/Mia|Orion/Ole", Namen(w), "offline nach den Angemeldeten, flach");
        var orion = Z(w, 20);
        Pruefe.Wahr(orion is { Online: false, HatAbzeichen: false, Anfang: "O", ScName: "Orion", Eigene: false } && orion.Beschreibung == "Orion/Ole, SC Orion, noch nicht eingeteilt, offline",
            "aus dem Verzeichnis: " + orion.Beschreibung);
        Pruefe.Gleich("Basalt, Schiff 2, Gunner (reserviert), offline", Z(w, 21).Beschreibung, "reserviert");
        Pruefe.Wahr(Z(w, 6) is { Online: false, Name: "Eule/Mia" }, "Eule aus der Praesenz, offline");
        w.Suche = "orion";
        Pruefe.Wahr(w.Zeilen.Count == 1 && w.Zeilen[0].KontoId == 20 && w.Leer is null, "Suche findet auch offline");
        w.Suche = "";

        // Ohne Änderung kein Neubau, auch mit offline.
        var geaendert = 0;
        w.Zeilen.CollectionChanged += (_, _) => geaendert++;
        a.Dienst.LageSetzen(l => l with { Sprecher = [new SprecherInfo(4, "Nordlicht", [1], false)] });
        Pruefe.Gleich(0, geaendert, "Funkspruch: nichts neu gebaut");

        // "neu" holt das Verzeichnis nur mit 5 s Abstand; ein Fehler lässt die alte Liste stehen und sagt warum.
        await w.NachladenCommand.ExecuteAsync(null);
        Pruefe.Gleich(1, Verzeichnisse(), "innerhalb von 5 s: kein zweites Verzeichnis");
        jetzt = jetzt.AddSeconds(6);
        await w.NachladenCommand.ExecuteAsync(null);
        Pruefe.Gleich(2, Verzeichnisse(), "nach 6 s: wieder geholt");
        jetzt = jetzt.AddSeconds(6);
        a.Dienst.NaechsterFehler = "Zu viele Anfragen.";
        await w.VerzeichnisHolenAsync();
        w.Aktualisieren();
        Pruefe.Wahr(w.Fehler == "Auch offline: Zu viele Anfragen." && Z(w, 20).KontoId == 20, "Fehler: die alte Liste bleibt, " + w.Fehler);

        // Klappt das Holen danach, geht die Meldung weg; ebenso beim Ausschalten (Befund der Prüfung 0.11.0: sie blieb bis "neu" stehen).
        jetzt = jetzt.AddSeconds(6);
        await w.VerzeichnisHolenAsync();
        Pruefe.Gleich(null, w.Fehler, "nach einem erfolgreichen Holen kein Fehler mehr");
        jetzt = jetzt.AddSeconds(6);
        a.Dienst.NaechsterFehler = "Zu viele Anfragen.";
        await w.VerzeichnisHolenAsync();
        Pruefe.Gleich("Auch offline: Zu viele Anfragen.", w.Fehler, "wieder ein Fehler");
        w.AuchOffline = false;
        Pruefe.Wahr(w.Fehler is null && w.Zeilen.All(z => z.Online), "ausgeschaltet: kein Fehler, nur Angemeldete");
        w.AuchOffline = true;

        // Endet die Einsatzleitung, geht der Schalter aus und mit ihm die Offline-Zeilen.
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Rechte = [.. l.Konto!.Rechte.Where(r => r != Recht.Zuweisen)] } });
        Pruefe.Wahr(!w.AuchOfflineSichtbar && !w.AuchOffline && w.Zeilen.All(z => z.Online), "ohne Einsatzleitung: aus");
    }

    /// <summary>
    /// Das Mikrofon (Sarevoc 14.10.2026: "ein Stummschalten-Button, so dass ein Raidleiter und höher einen Funker stumm schalten kann ... das dann
    /// blutrot wird"): Klickbar nur, wo der Server es erlaubt (ZUWEISEN: Einsatzleitung oder Admin; einen Admin nur ein Admin, 6.10), nie in der
    /// eigenen Zeile; Führung ohne Auftrag sieht nur die Anzeige, und ob andere stumm sind, sieht nur die Einsatzleitung. Ein Klick ist genau ein
    /// Stummschalten in allen Netzen für die Höchstdauer des Servers (eine Stunde, keine Zeitwahl), der zweite hebt es mit 0 Sekunden auf; eine
    /// abgelaufene Stummschaltung zählt nicht, eine nur in Global macht das Mikrofon auch blutrot und der Klick hebt genau sie auf. Ein Fehler steht
    /// oben, bis ein Klick gelingt. "Abend beenden" hebt auf, was man selbst gesetzt hat.
    /// </summary>
    [Test]
    public static async Task WerIstDa_Mikrofon_AnzeigeOderStummschaltenJeRang()
    {
        // Führung ohne Auftrag (Max, Stufe RAIDLEITER): nur Anzeige.
        using (var b = new Aufbau(FunkDienstAttrappe.Beispiel()))
        {
            var f = new WerIstDaViewModel(b.Dienst, b.Ablage, b.Takt);
            var k = Z(f, 3);
            Pruefe.Wahr(k is { MikroKlickbar: false, StummBekannt: false, Stumm: false } && !k.MikroCommand.CanExecute(null), "Führung ohne Auftrag: nur Anzeige");
            Pruefe.Gleich("Ob Kobold stumm geschaltet ist, sieht nur die Einsatzleitung. Stummschalten darf nur die Einsatzleitung (Admin oder Auftrag für den Abend).",
                k.MikroGesperrt, "Hilfetext der Anzeige");
            Pruefe.Gleich("Kobold: stumm unbekannt", k.MikroAnsage, "für Vorleser");
            var ich = Z(f, 1);
            Pruefe.Wahr(ich is { Eigene: true, StummBekannt: true, Stumm: false, MikroKlickbar: false } && ich.MikroGesperrt == "Du bist nicht stumm geschaltet. Dich selbst schaltest du hier nicht stumm.",
                "eigene Zeile: " + ich.MikroGesperrt);
            await k.MikroCommand.ExecuteAsync(null);
            Pruefe.Gleich(0, Aufrufe(b.Dienst, "Stumm "), "nur Anzeige: kein Aufruf");
        }

        // Einsatzleitung (Mustermann im Auftrag, Stufe FUNKER).
        using var a = new Aufbau(dienst: FunkDienstAttrappe.MitRaidabend());
        var jetzt = FunkDienstAttrappe.Abend;
        var w = new WerIstDaViewModel(a.Dienst, a.Ablage, a.Takt, () => jetzt);
        var kobold = Z(w, R.Kobold);
        string Bis(int minuten) => jetzt.AddMinutes(minuten).ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
        // Gegenprüfung 0.11.5: Ohne Kontenverzeichnis kennt die Spalte Kobolds Stufe nicht (er trägt kein Zeichen); er könnte ein Admin sein, der
        // sein Zeichen nicht zeigt. Dann ist das Mikrofon nur Anzeige; beim Öffnen holt die Spalte das Verzeichnis.
        Pruefe.Wahr(!kobold.MikroKlickbar && kobold.MikroGesperrt.EndsWith("Den Rang von Kobold kennt Wer ist da gerade nicht; \"neu\" holt ihn.", StringComparison.Ordinal),
            "Stufe unbekannt: " + kobold.MikroGesperrt);
        await w.UmschaltenCommand.ExecuteAsync(null);
        Pruefe.Gleich(1, Aufrufe(a.Dienst, "Verzeichnis"), "beim Öffnen das Kontenverzeichnis, auch ohne \"auch offline\"");
        Pruefe.Wahr(kobold is { MikroKlickbar: true, StummBekannt: true, Stumm: false } && kobold.MikroCommand.CanExecute(null), "Einsatzleitung: klickbar");
        Pruefe.Gleich("Kobold stumm schalten", kobold.MikroAnsage, "für Vorleser: was der Klick tut");
        Pruefe.Gleich("Kobold ist nicht stumm geschaltet. Klick schaltet Kobold in allen Kanälen stumm, bis du es aufhebst oder den Abend beendest (höchstens eine Stunde).",
            kobold.MikroHilfe, "Hilfetext");
        var nordwind = Z(w, R.Nordwind);
        Pruefe.Wahr(nordwind is { MikroKlickbar: false, StummBekannt: true, Rang: 0 } && nordwind.MikroGesperrt.EndsWith("Einen Admin schaltet nur ein Admin stumm.", StringComparison.Ordinal),
            "Admin nur durch einen Admin (6.10): " + nordwind.MikroGesperrt);
        Pruefe.Wahr(Z(w, R.Mustermann) is { Eigene: true, MikroKlickbar: false }, "die eigene Zeile: kein Stummschalten seiner selbst");

        await kobold.MikroCommand.ExecuteAsync(null);
        Pruefe.Gleich(1, Aufrufe(a.Dienst, "Stumm "), "ein Aufruf StummschaltenAsync");
        Pruefe.Gleich("Stumm 4 0 3600", Letzter(a.Dienst, "Stumm "), "alle Netze (0), die Höchstdauer des Servers (3600 s)");
        Pruefe.Gleich(Funk.Server.Lage.Fristen.StummMaxSekunden, (int)WerIstDaViewModel.StummSekunden, "die Höchstdauer des Servers (Fristen.StummMaxSekunden)");
        Pruefe.Wahr(ReferenceEquals(kobold, Z(w, R.Kobold)) && kobold is { Stumm: true, MikroKlickbar: true }, "stumm, dieselbe Zeile");
        Pruefe.Gleich("Kobold ist stumm geschaltet bis " + Bis(60) + ". Klick hebt es auf.", kobold.MikroHilfe, "Hilfetext stumm");
        Pruefe.Gleich("Kobold wieder hörbar machen", kobold.MikroAnsage, "für Vorleser");

        await kobold.MikroCommand.ExecuteAsync(null);
        Pruefe.Gleich(2, Aufrufe(a.Dienst, "Stumm "), "wieder genau ein Aufruf");
        Pruefe.Gleich("Stumm 4 0 0", Letzter(a.Dienst, "Stumm "), "Aufheben mit 0 Sekunden");
        Pruefe.Falsch(kobold.Stumm, "wieder normal");

        // Ein Fehler steht oben; der nächste gelungene Klick nimmt ihn weg.
        a.Dienst.NaechsterFehler = "Dafür fehlt dir das Recht.";
        await kobold.MikroCommand.ExecuteAsync(null);
        Pruefe.Wahr(w.Fehler == "Kobold: Dafür fehlt dir das Recht." && !kobold.Stumm, "Fehler: " + w.Fehler);
        await kobold.MikroCommand.ExecuteAsync(null);
        Pruefe.Wahr(w.Fehler is null && kobold.Stumm, "gelungen: der Fehler ist weg");

        // "Abend beenden" hebt auf, was du gesetzt hast, vor dem Abgeben des Auftrags (Aufheben braucht ZUWEISEN).
        var brett = a.Brett();
        brett.AbendOffen = true;
        await brett.AbendBeendenCommand.ExecuteAsync(null);
        var aufrufe = a.Dienst.Aufrufe.ToList();
        Pruefe.Wahr(aufrufe.IndexOf("Stumm 4 0 0", aufrufe.LastIndexOf("Stumm 4 0 3600")) is var auf and > 0 && auf < aufrufe.LastIndexOf("Abgeben"),
            "Abend beenden hebt die Stummschaltung auf, vor dem Abgeben: " + string.Join(" | ", aufrufe.TakeLast(8)));
        Pruefe.Falsch(kobold.Stumm, "nach dem Abend nicht mehr stumm");

        // Wieder mit Auftrag: Eine abgelaufene Stummschaltung zählt nicht, eine nur in Global macht das Mikrofon blutrot, der Klick hebt genau sie auf.
        a.Dienst.LageSetzen(_ => FunkDienstAttrappe.Raidabend() with
        {
            Funker = [.. FunkDienstAttrappe.Raidabend().Funker.Select(f => f.KontoId != R.Kobold ? f : f with
            {
                Details = f.Details! with
                {
                    Stumm =
                    [
                        new Stummschaltung { NetzId = 0, Bis = jetzt.AddMinutes(-1).ToUnixTimeMilliseconds(), Durch = R.Mustermann },
                        new Stummschaltung { NetzId = 1, Bis = jetzt.AddMinutes(30).ToUnixTimeMilliseconds(), Durch = R.Mustermann },
                    ],
                },
            })],
        });
        Pruefe.Wahr(kobold is { Stumm: true, MikroKlickbar: true } && kobold.MikroHilfe == "Kobold ist stumm geschaltet in Global bis " + Bis(30) + ". Klick hebt es auf.",
            "nur in Global: " + kobold.MikroHilfe);
        await kobold.MikroCommand.ExecuteAsync(null);
        Pruefe.Gleich("Stumm 4 1 0", Letzter(a.Dienst, "Stumm "), "hebt genau die in Global auf");
        Pruefe.Falsch(kobold.Stumm, "die abgelaufene zählt nicht");

        // Gegenprüfung 0.11.5, Befund 7: Läuft eine Stummschaltung ohne neue Lage ab, nimmt der Bildtakt das Blutrot weg (wie im Funkfeld).
        await kobold.MikroCommand.ExecuteAsync(null);
        Pruefe.Wahr(kobold.Stumm, "wieder stumm, eine Stunde");
        jetzt = jetzt.AddMinutes(30);
        w.Takt();
        Pruefe.Wahr(kobold.Stumm, "nach 30 Minuten noch stumm");
        jetzt = jetzt.AddMinutes(31);
        var gemeldetVorher = Aufrufe(a.Dienst, "Stumm ");
        w.Takt();
        Pruefe.Wahr(!kobold.Stumm && kobold.MikroAnsage == "Kobold stumm schalten" && Aufrufe(a.Dienst, "Stumm ") == gemeldetVorher,
            "abgelaufen: nicht mehr blutrot, ohne Anfrage an den Server");
        jetzt = FunkDienstAttrappe.Abend;

        // Befund 4: Eine fremde Stummschaltung hebt nur auf, wer sie gesetzt hat, eine höhere Stufe oder ein Admin (Server: DarfStummAufheben).
        void StummDurch(uint ziel, uint durch) => a.Dienst.LageSetzen(l => l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId != ziel ? f : f with
            {
                Details = f.Details! with { Stumm = [new Stummschaltung { NetzId = 0, Bis = jetzt.AddMinutes(40).ToUnixTimeMilliseconds(), Durch = durch }] },
            })],
        });
        StummDurch(R.Kobold, R.Nordwind);
        Pruefe.Wahr(kobold is { Stumm: true, MikroKlickbar: false }
            && kobold.MikroGesperrt == "Kobold ist stumm geschaltet bis " + Bis(40) + ". Aufheben darf nur, wer es gesetzt hat (Nordwind), eine höhere Stufe oder ein Admin.",
            "vom Admin gesetzt: die Einsatzleitung im Auftrag hebt es nicht auf, " + kobold.MikroGesperrt);
        var vorher = Aufrufe(a.Dienst, "Stumm ");
        await kobold.MikroCommand.ExecuteAsync(null);
        Pruefe.Gleich(vorher, Aufrufe(a.Dienst, "Stumm "), "kein Aufruf, den der Server ablehnen würde");
        // Führung mit Auftrag (Stufe RAIDLEITER) hebt auf, was eine Einsatzleitung der Stufe FUNKER (Quarz) gesetzt hat, nicht aber, was ein Admin setzte.
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Stufe.Raidleiter } });
        StummDurch(R.Kobold, R.Quarz);
        Pruefe.Wahr(kobold is { Stumm: true, MikroKlickbar: true } && kobold.MikroHilfe.EndsWith(" Klick hebt es auf.", StringComparison.Ordinal), "Führung über FUNKER: " + kobold.MikroHilfe);
        StummDurch(R.Kobold, R.Nordwind);
        Pruefe.Falsch(kobold.MikroKlickbar, "Führung unter Admin: nur Anzeige");
        StummDurch(R.Kobold, 999);
        Pruefe.Falsch(kobold.MikroKlickbar, "Setzender unbekannt: wie beim Server ein Admin");
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Stufe.Funker } });
        StummDurch(R.Kobold, R.Mustermann);
        Pruefe.Wahr(kobold.MikroKlickbar, "selbst gesetzt: klickbar");

        // Befund 3: Ein Admin, der sein Zeichen nicht zeigt, steht trotzdem oben und ist für die Einsatzleitung ohne Admin-Stufe nur Anzeige; die
        // Stufe kommt aus dem Kontenverzeichnis.
        a.Dienst.LageSetzen(l => l with { Funker = [.. l.Funker.Select(f => f.KontoId != R.Nordwind ? f : f with { Zeichen = [], Abzeichen = [], AbzeichenVorn = null })] });
        var nordwindOhne = Z(w, R.Nordwind);
        Pruefe.Wahr(nordwindOhne is { Rang: 0, HatAbzeichen: false, MikroKlickbar: false } && w.Zeilen[0].KontoId == R.Nordwind
            && nordwindOhne.MikroGesperrt.EndsWith("Einen Admin schaltet nur ein Admin stumm.", StringComparison.Ordinal), "Admin ohne Zeichen: oben, nur Anzeige");

        // Wer nach dem Öffnen dazukommt, hat keine bekannte Stufe; die Spalte holt das Verzeichnis von selbst, höchstens alle 30 s.
        var verzeichnisse = Aufrufe(a.Dienst, "Verzeichnis");
        jetzt = jetzt.AddSeconds(31);
        a.Dienst.LageSetzen(l => l with
        {
            Funker = [.. l.Funker, new FunkerInfo(77, "Nachtfalter", true, null, null) { ScName = "Nachtfalter", Details = new FunkerDetails([], [], []) }],
        });
        Pruefe.Wahr(Aufrufe(a.Dienst, "Verzeichnis") == verzeichnisse + 1 && Z(w, 77).MikroKlickbar, "neu angemeldet: Verzeichnis von selbst, danach klickbar");
        jetzt = FunkDienstAttrappe.Abend;

        // Als Admin auch einen Admin.
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Stufe.Admin } });
        Pruefe.Wahr(Z(w, R.Nordwind).MikroKlickbar, "Admin schaltet Admin");

        // Befund 10: "Abend beenden" als Admin hebt auch fremde Stummschaltungen auf, nie aber die eigene durch einen anderen Admin; die lehnt der
        // Server immer ab, und "Abend beendet, aber: ..." stand sonst jedes Mal da.
        a.Dienst.LageSetzen(l => l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId switch
            {
                R.Mustermann => f with { Details = f.Details! with { Stumm = [new Stummschaltung { NetzId = 0, Bis = jetzt.AddMinutes(20).ToUnixTimeMilliseconds(), Durch = R.Nordwind }] } },
                R.Anker => f with { Details = f.Details! with { Stumm = [new Stummschaltung { NetzId = 0, Bis = jetzt.AddMinutes(20).ToUnixTimeMilliseconds(), Durch = R.Ferox }] } },
                _ => f,
            })],
        });
        var brett2 = a.Brett();
        brett2.AbendOffen = true;
        await brett2.AbendBeendenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Stumm 8 0 0") && !a.Dienst.Aufrufe.Contains("Stumm 2 0 0") && brett2.Meldung?.Contains("Mustermann", StringComparison.Ordinal) != true,
            "als Admin: Anker ja, die eigene nein: " + brett2.Meldung);
        a.Dienst.LageSetzen(_ => FunkDienstAttrappe.Raidabend() with { Konto = FunkDienstAttrappe.Raidabend().Konto! with { Stufe = Stufe.Admin } });

        // Endet die Einsatzleitung, ist das Mikrofon nur Anzeige.
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Stufe = Stufe.Funker, Beauftragung = null } });
        Pruefe.Wahr(w.Zeilen.All(z => !z.MikroKlickbar && !z.MikroCommand.CanExecute(null)), "ohne Einsatzleitung nur Anzeige");
    }

    /// <summary>
    /// Der Status (Sarevoc 14.10.2026: "Die Grafik zeigt mit grün, dass der Funker in Channels zugeteilt ist, in gelb, dass der Funker nicht in Channels
    /// ist"): grün mit mindestens einem Feld in einem Kanal der Raid-Seite, gelb ohne (auch nur mit Global oder nur mit Einheitsnetzen, seit der
    /// Gegenprüfung 0.11.5 wie die Zahl am Namen auf der Raid-Seite), grau mit Hilfetext, wenn die Lage es nicht weiß (andere sieht nur die
    /// Einsatzleitung; Konten ohne Sitzung kennt sie nicht). Die eigene Zeile kennt ihre Felder immer. Ohne Verwaltungsteil nur Anzeige.
    /// </summary>
    /// <summary>
    /// Sarevoc 14.10.2026: "wenn möglich auch als Popup und eigenes Fenster ablösbar". "lösen" nimmt die Liste aus der Spalte (InSpalte aus), "andocken"
    /// legt sie zurück; Schließen lässt die Wahl stehen. Das Fenster selbst baut Hauptfenster.xaml.cs; hier ohne Fenster, damit kein Test eines
    /// auf dem Bildschirm öffnet.
    /// </summary>
    [Test]
    public static async Task WerIstDa_Loesen_UndAndocken()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var w = new WerIstDaViewModel(a.Dienst, a.Ablage, a.Takt);
        await w.UmschaltenCommand.ExecuteAsync(null);
        Pruefe.Wahr(w is { Offen: true, Geloest: false, InSpalte: true, LoesenText: "lösen" }, "offen als Spalte");
        w.LoesenCommand.Execute(null);
        Pruefe.Wahr(w is { Offen: true, Geloest: true, InSpalte: false, LoesenText: "andocken" }, "gelöst: nicht mehr in der Spalte");
        w.SchliessenCommand.Execute(null);
        Pruefe.Wahr(w is { Offen: false, InSpalte: false }, "geschlossen");
        await w.UmschaltenCommand.ExecuteAsync(null);
        w.LoesenCommand.Execute(null);
        Pruefe.Wahr(w is { Offen: true, InSpalte: true, LoesenText: "lösen" }, "andocken legt sie zurück in die Spalte");
    }

    [Test]
    public static async Task WerIstDa_Status_GruenGelbGrau()
    {
        using var a = new Aufbau(dienst: FunkDienstAttrappe.MitRaidabend());
        var w = new WerIstDaViewModel(a.Dienst, a.Ablage, a.Takt, () => FunkDienstAttrappe.Abend);
        await w.UmschaltenCommand.ExecuteAsync(null);
        Pruefe.Wahr(Z(w, R.Ferox) is { Kanalstand: Kanalstand.Eingeteilt, KanalzahlText: "1", StatusAnsage: "Ferox: in einem Kanal", StatusHilfe: "Ferox ist in einem Kanal eingeteilt." },
            "Ferox als CO in Kommando: grün");
        Pruefe.Wahr(Z(w, R.Kobold) is { Kanalstand: Kanalstand.OhneKanal, StatusHilfe: "Kobold ist in keinem Kanal." },
            "Kobold als Pilot nur in den Einheitsnetzen von Schiff 1: gelb, wie auf der Raid-Seite ohne Zahl");
        Pruefe.Wahr(Z(w, R.Hagel) is { Kanalstand: Kanalstand.OhneKanal, StatusHilfe: "Hagel ist in keinem Kanal." }, "Hagel ohne Platz: gelb");
        Pruefe.Wahr(Z(w, R.Mustermann) is { Kanalstand: Kanalstand.Eingeteilt, KanalzahlText: not "" and not "0" } z0 && z0.StatusHilfe.StartsWith("Du bist in ", StringComparison.Ordinal), "die eigene Zeile aus den eigenen Feldern");
        Pruefe.Wahr(w.Zeilen.All(z => !z.StatusKlickbar && !z.StatusCommand.CanExecute(null)), "ohne Verwaltungsteil nur Anzeige");
        a.Dienst.LageSetzen(l => l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId != R.Pollux ? f : f with { Details = f.Details! with { Felder = [new FeldInfo(0, Grenzen.GlobalNetzId, true, true, true)] } })],
        });
        Pruefe.Gleich(Kanalstand.OhneKanal, Z(w, R.Pollux).Kanalstand, "nur Global: gelb");
        // Ein Ch. eines Kanalnetzes und eine Ruhe zählen für den Kanal, wie auf der Raid-Seite.
        a.Dienst.LageSetzen(l => l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId != R.Pollux ? f : f with { Details = f.Details! with { Felder = [new FeldInfo(0, R.NetzOrga, true, false, false)] } })],
        });
        Pruefe.Gleich(Kanalstand.Eingeteilt, Z(w, R.Pollux).Kanalstand, "Orga: grün");

        // Dieselbe Rechnung wie die Zahl am Namen links auf der Raid-Seite: grün genau dann, wenn dort eine Zahl steht.
        var brett = a.Brett();
        var verglichen = 0;
        foreach (var karte in brett.NamenLive.Where(k => k.KontoId != R.Mustermann))
        {
            Pruefe.Gleich(karte.Kanalzahl > 0, Z(w, karte.KontoId).Kanalstand == Kanalstand.Eingeteilt, karte.Name + ": Status wie die Zahl auf der Raid-Seite (" + karte.Kanalzahl + ")");
            verglichen++;
        }
        Pruefe.Wahr(verglichen >= 10 && w.Zeilen.Any(z => z.Kanalstand == Kanalstand.Eingeteilt) && w.Zeilen.Count(z => z.Kanalstand == Kanalstand.OhneKanal) >= 3,
            "genug verglichen, beide Farben dabei: " + verglichen);

        // Kranich steht nur im Kontenverzeichnis: ohne Sitzung kennt die Lage keine Felder.
        w.AuchOffline = true;
        Pruefe.Wahr(Z(w, R.Kranich) is { Kanalstand: Kanalstand.Unbekannt, StatusAnsage: "Kranich/Ben: Kanäle unbekannt" }
            && Z(w, R.Kranich).StatusHilfe == "Ob Kranich/Ben in Kanälen eingeteilt ist, weiß Wer ist da ohne Sitzung nicht.", "Kranich: grau, " + Z(w, R.Kranich).StatusHilfe);

        // Ohne Einsatzleitung (Beispiel: Führung ohne Auftrag) kennt die Lage die Felder der anderen nicht.
        using var b = new Aufbau(FunkDienstAttrappe.Beispiel());
        var f2 = new WerIstDaViewModel(b.Dienst, b.Ablage, b.Takt);
        Pruefe.Wahr(Z(f2, 3) is { Kanalstand: Kanalstand.Unbekannt, StatusHilfe: "Ob Kobold in Kanälen eingeteilt ist, sieht nur die Einsatzleitung." }, "grau mit Hilfetext");
        Pruefe.Gleich(Kanalstand.Eingeteilt, Z(f2, 1).Kanalstand, "die eigene Zeile weiß es");
        b.Dienst.LageSetzen(l => l with { Felder = [new FeldInfo(0, 1, true, true, true)] });
        Pruefe.Wahr(Z(f2, 1) is { Kanalstand: Kanalstand.OhneKanal, StatusHilfe: "Du bist in keinem Kanal." }, "nur Global: gelb");
    }

    /// <summary>
    /// Der Sprung in die Verwaltung (Sarevoc 14.10.2026: "der sofern geklickt zu den Einstellungen schwenkt, wo man als Admin einen Funker anlegt,
    /// Abzeichen und Rollen vergibt"; bestätigt: nur im Admin-Setup): ohne Verwaltungsteil oder ohne eingerichteten Admin nur Anzeige (seit der
    /// Gegenprüfung 0.11.5 auch für vorgesehene, halb eingerichtete und gesperrte Admins); sonst Bereich Verwaltung (Strg+9), Seite Mitglieder, dieses
    /// Konto gewählt. Ist die Admin-Sitzung noch zu, beginnt die Verwaltung nach der Anmeldung bei Mitglieder mit der Karte (bis zur Gegenprüfung
    /// bei "Heute"). Kennt die Liste das Konto nicht, lädt sie neu; der Sprung wartet nur auf diese eine Liste und öffnet später keine Karte über
    /// einer anderen. Wer die Verwaltung ohne Anmeldung verlässt, verwirft ihn.
    /// </summary>
    [Test]
    public static async Task WerIstDa_Status_SpringtNurImAdminSetupInDieVerwaltung()
    {
        // Normales Setup (kein Verwaltungsteil): auch ein Admin sieht nur die Anzeige.
        using (var b = new Aufbau(FunkDienstAttrappe.Beispiel()))
        {
            var h = b.Haupt(out var ff, out _, out _, out _);
            b.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = AdminStatus.Aktiv } });
            Pruefe.Wahr(h.WerIstDa.Zeilen.All(z => !z.StatusKlickbar && !z.StatusCommand.CanExecute(null)), "ohne Verwaltungsteil nur Anzeige");
            ff.Dispose();
        }

        var dienst = new VerwaltungsdienstAttrappe();
        var vm = new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst);
        using var teil = new Funk.Verwaltung.Verwaltungsteil(dienst, vm);
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Verwaltung = teil };
        var h2 = a.Haupt(out var funkfeld, out _, out _, out _);
        Pruefe.Wahr(h2.WerIstDa.Zeilen.All(z => !z.StatusKlickbar), "Admin-Setup, aber kein Admin: keine Verwaltung, nur Anzeige");
        // Befund 8: Den Bereich Verwaltung sieht auch, wer als Admin vorgesehen, halb eingerichtet oder gesperrt ist; vergeben kann er dort nichts.
        foreach (var (admin, antrag) in new (AdminStatus?, bool)[] { (null, true), (AdminStatus.Vorgesehen, false), (AdminStatus.Einrichtung, false), (AdminStatus.Gesperrt, false) })
        {
            a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = admin, AdminAntrag = antrag } });
            Pruefe.Wahr(h2.Bereiche.Any(b => b.Kennung == HauptViewModel.VerwaltungKennung) && h2.WerIstDa.Zeilen.All(z => !z.StatusKlickbar && !z.StatusCommand.CanExecute(null)),
                "Admin " + (admin?.ToString() ?? "beantragt") + ": Bereich Verwaltung da, Status nur Anzeige");
        }
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = AdminStatus.Aktiv, AdminAntrag = false } });
        var kobold = Z(h2.WerIstDa, 3);
        Pruefe.Wahr(kobold.StatusKlickbar && kobold.StatusCommand.CanExecute(null)
            && kobold.StatusHilfe == "Ob Kobold in Kanälen eingeteilt ist, sieht nur die Einsatzleitung. Klick öffnet die Verwaltung bei diesem Mitglied.", "Admin-Setup: " + kobold.StatusHilfe);
        Pruefe.Wahr(Z(h2.WerIstDa, 1).StatusKlickbar, "auch die eigene Zeile");
        kobold.StatusCommand.Execute(null);
        Pruefe.Gleich(HauptViewModel.VerwaltungKennung, h2.AktiverBereich.Kennung, "Bereich Verwaltung (Strg+9)");
        Pruefe.Wahr(vm.IstMitglieder && vm.Mitglieder.Karte is { KontoId: 3 } && vm.Mitglieder.Suche.Length == 0, "Seite Mitglieder, Kobold gewählt");

        // Befund 9: Kennt die Liste das Konto nicht (älter als das Konto), lädt die Verwaltung neu und zeigt es dann.
        var geladen = dienst.Aufrufe.Count(x => x == "Mitglieder");
        dienst.Mitglieder.Add(new MitgliedEintrag { KontoId = 30, Name = "Basalt", ScNamen = ["Basalt"], Rollen = [], Stufe = Stufe.Funker, Zustand = MitgliedZustand.Online });
        ((IVerwaltungsteil)teil).MitgliedZeigen(30);
        Pruefe.Wahr(dienst.Aufrufe.Count(x => x == "Mitglieder") == geladen + 1 && vm.Mitglieder.Karte is { KontoId: 30 } && !vm.Mitglieder.SprungWartet,
            "veraltete Liste: neu geladen, Basalt gewählt");
        // Fehlt es auch danach, wartet nichts mehr: Ein späteres Laden ("Mitglied geändert") öffnet nicht dieses Konto über der Karte, die gerade
        // offen ist (bis zur Gegenprüfung blieb der Sprung vorgemerkt und tat genau das).
        ((IVerwaltungsteil)teil).MitgliedZeigen(31);
        Pruefe.Falsch(vm.Mitglieder.SprungWartet, "nach dem einen Neuladen wartet kein Sprung mehr");
        vm.Mitglieder.OeffnenCommand.Execute(vm.Mitglieder.Sichtbar.Single(z => z.Eintrag.KontoId == 2));
        dienst.Mitglieder.Add(new MitgliedEintrag { KontoId = 31, Name = "Granit", ScNamen = ["Granit"], Rollen = [], Stufe = Stufe.Funker, Zustand = MitgliedZustand.Online });
        dienst.Senden(new Meldung { Art = MeldungsArten.MitgliedGeaendert, KontoId = 31 });
        Pruefe.Wahr(vm.Mitglieder.Karte is { KontoId: 2 }, "Falkes Karte bleibt offen: " + vm.Mitglieder.Karte?.KontoId);

        // Befund 2: Sitzung noch zu: der Bereich steht da; nach der Anmeldung (Code) beginnt die Verwaltung bei Mitglieder mit der Karte, nicht bei "Heute".
        var dienst2 = new VerwaltungsdienstAttrappe();
        dienst2.LageSetzen(l => l with { SitzungBis = null, BestaetigtBis = null });
        var vm2 = new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst2);
        using var teil2 = new Funk.Verwaltung.Verwaltungsteil(dienst2, vm2);
        vm2.Bereich = Funk.Verwaltung.Modelle.Verwaltungsbereich.Heute;
        ((IVerwaltungsteil)teil2).MitgliedZeigen(4);
        Pruefe.Wahr(vm2.IstAnmelden && vm2.IstMitglieder && vm2.Mitglieder.Karte is null, "Sitzung zu: Mitglieder gewählt, noch keine Karte");
        vm2.Code = VerwaltungsdienstAttrappe.RichtigerCode;
        await vm2.AnmeldenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm2.IstOffen && vm2.IstMitglieder && vm2.Mitglieder.Karte is { KontoId: 4 }, "nach der Anmeldung: Seite Mitglieder, Nordlicht gewählt");
        // Die nächste Anmeldung ohne Sprung beginnt wieder bei "Heute".
        await vm2.AbmeldenCommand.ExecuteAsync(null);
        vm2.Code = VerwaltungsdienstAttrappe.RichtigerCode;
        await vm2.AnmeldenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm2.IstOffen && vm2.Bereich == Funk.Verwaltung.Modelle.Verwaltungsbereich.Heute, "ohne Sprung: Heute");

        // Wer die Verwaltung ohne Anmeldung verlässt, verwirft den Sprung.
        var dienst3 = new VerwaltungsdienstAttrappe();
        dienst3.LageSetzen(l => l with { SitzungBis = null, BestaetigtBis = null });
        var vm3 = new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst3);
        using var teil3 = new Funk.Verwaltung.Verwaltungsteil(dienst3, vm3);
        ((IVerwaltungsteil)teil3).MitgliedZeigen(4);
        ((IVerwaltungsteil)teil3).Verlassen();
        vm3.Code = VerwaltungsdienstAttrappe.RichtigerCode;
        await vm3.AnmeldenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm3.IstOffen && vm3.Bereich == Funk.Verwaltung.Modelle.Verwaltungsbereich.Heute && vm3.Mitglieder.Karte is null, "verlassen: kein Sprung mehr");
        funkfeld.Dispose();
    }

    /// <summary>
    /// Die Wippe (Sarevoc 14.10.2026: "der lauter- oder leiser-Knopf ... ein Wippen-Button", "seitlich, rechts lauter, links leiser"): je Klick 2 dB über
    /// PegelSetzen (Netz 0), ein Aufruf je Klick, sofort an der Klangmaschine; dieselbe Stelle wie der Regler unter Empfangen > Funker, begrenzt auf
    /// -30 bis +12 dB; an der Grenze geht die Hälfte nicht mehr und schickt nichts. Nicht in der eigenen Zeile. Ein Push setzt den eben verstellten
    /// Pegel nicht auf den Wert der Anmeldung zurück (bis 0.11.4 taten das Regler und Klangmaschine).
    /// </summary>
    [Test]
    public static async Task WerIstDa_Wippe_ZweiDbJeKlick_Grenzen_NichtInDerEigenenZeile()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var netze = a.Netze();
        var w = new WerIstDaViewModel(a.Dienst, a.Ablage, a.Takt);
        Pruefe.Wahr(w.Zeilen.All(z => !z.HatWippe), "ohne Pegel je Person keine Wippe");
        w.Anbinden(netze, null, null);
        var kobold = Z(w, 3);
        FunkerZeile Regler(uint konto) => netze.Funker.Single(z => z.KontoId == konto);
        string Db(double db) => db.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " dB";
        Pruefe.Wahr(kobold is { HatWippe: true, PegelDb: 0 } && kobold.LeiserCommand.CanExecute(null) && kobold.LauterCommand.CanExecute(null), "Kobold: Wippe da, 0 dB");
        Pruefe.Wahr(kobold is { LeiserAnsage: "Kobold leiser", LauterAnsage: "Kobold lauter" }, "für Vorleser");
        Pruefe.Gleich("Kobold bei dir: " + Db(0) + ". Klick: 2 dB lauter, bis +12 dB. Nur für dich.", kobold.LauterHilfe, "Hilfetext nennt den Wert");

        kobold.LauterCommand.Execute(null);
        Pruefe.Gleich(1, Aufrufe(a.Dienst, "Pegel "), "ein Aufruf PegelSetzen");
        Pruefe.Gleich("Pegel 3 2.0", Letzter(a.Dienst, "Pegel "), "rechts: 2 dB lauter");
        Pruefe.Gleich((3u, 2f), a.Maschine.FunkerPegel[^1], "sofort an der Klangmaschine");
        Pruefe.Wahr(ReferenceEquals(kobold, Z(w, 3)) && kobold.PegelDb == 2 && Regler(3).PegelDb == 2, "Wippe und Regler zeigen dasselbe, dieselbe Zeile");
        Pruefe.Wahr(kobold.LeiserHilfe.StartsWith("Kobold bei dir: " + Db(2) + ".", StringComparison.Ordinal), kobold.LeiserHilfe);
        kobold.LeiserCommand.Execute(null);
        kobold.LeiserCommand.Execute(null);
        Pruefe.Wahr(kobold.PegelDb == -2 && Aufrufe(a.Dienst, "Pegel ") == 3 && Letzter(a.Dienst, "Pegel ") == "Pegel 3 -2.0", "links: zweimal leiser, je Klick ein Aufruf");

        // Grenzen wie der Regler: +12 und -30; der Regler unter Empfangen ändert die Wippe mit.
        Regler(3).PegelDb = 11;
        Pruefe.Gleich(11.0, kobold.PegelDb, "der Regler ändert die Wippe mit");
        kobold.LauterCommand.Execute(null);
        Pruefe.Wahr(kobold.PegelDb == 12 && !kobold.LauterCommand.CanExecute(null) && kobold.LeiserCommand.CanExecute(null), "begrenzt auf +12: lauter geht nicht mehr");
        Pruefe.Gleich("Kobold bei dir: " + Db(12) + ". Lauter geht nicht.", kobold.LauterGesperrt, "Hilfetext an der Grenze");
        var vorher = Aufrufe(a.Dienst, "Pegel ");
        kobold.LauterCommand.Execute(null);
        Pruefe.Gleich(vorher, Aufrufe(a.Dienst, "Pegel "), "an der Grenze kein Aufruf");
        Regler(3).PegelDb = -29;
        kobold.LeiserCommand.Execute(null);
        Pruefe.Wahr(kobold.PegelDb == -30 && !kobold.LeiserCommand.CanExecute(null) && kobold.LauterCommand.CanExecute(null), "begrenzt auf -30");

        // Nicht in der eigenen Zeile: Es ist das eigene Hören.
        var ich = Z(w, 1);
        vorher = Aufrufe(a.Dienst, "Pegel ");
        Pruefe.Wahr(ich is { Eigene: true, HatWippe: false } && !ich.LeiserCommand.CanExecute(null) && !ich.LauterCommand.CanExecute(null), "eigene Zeile ohne Wippe");
        await w.PegelUmAsync(ich, WerIstDaViewModel.PegelSchritt);
        Pruefe.Gleich(vorher, Aufrufe(a.Dienst, "Pegel "), "eigene Zeile: kein Aufruf");

        // Der Server meldet Pegel nur bei der Anmeldung (PegelVoreinstellungen). Falke hatte -6 gespeichert; die Attrappe schreibt einen
        // gesetzten Pegel in die Lage zurück, mit einem Fehler nicht, so wie die Lage im Betrieb beim Wert der Anmeldung bleibt.
        a.Dienst.LageSetzen(l => l with { FunkerPegel = new Dictionary<uint, float> { [2] = -6f } });
        var falke = Z(w, 2);
        Pruefe.Gleich(-6.0, falke.PegelDb, "gespeicherter Pegel aus der Anmeldung");
        a.Dienst.NaechsterFehler = "Zeitüberschreitung.";
        falke.LauterCommand.Execute(null);
        Pruefe.Wahr(falke.PegelDb == -4 && w.Fehler == "Falke/Tom: Zeitüberschreitung.", "Fehler steht oben: " + w.Fehler);
        a.Dienst.LageSetzen(l => l with { Sprecher = [] });
        Pruefe.Wahr(falke.PegelDb == -4 && Regler(2).PegelDb == -4 && a.Maschine.FunkerPegel[^1] == (2u, -4f), "ein Push setzt nichts zurück");
        falke.LauterCommand.Execute(null);
        Pruefe.Wahr(falke.PegelDb == -2 && w.Fehler is null, "gelungen: der Fehler ist weg");
    }

    /// <summary>
    /// Der Name (Sarevoc 14.10.2026: "Wenn der Name des Funkers geklickt wird, macht die Raid-Einteilung-Seite auf"; bestätigt: nur mit der
    /// Einsatzleitung, sonst nichts): mit ZUWEISEN die Raid-Seite (Strg+5) vorn, ohne nur Anzeige; endet der Auftrag, wieder nur Anzeige.
    /// </summary>
    [Test]
    public static void WerIstDa_Name_OeffnetDieRaidSeiteNurMitEinsatzleitung()
    {
        using (var b = new Aufbau(FunkDienstAttrappe.Beispiel()))
        {
            var h = b.Haupt(out var ff, out _, out _, out _);
            var vorn = h.AktiverBereich;
            var k = Z(h.WerIstDa, 3);
            Pruefe.Wahr(!k.NameKlickbar && !k.RaidOeffnenCommand.CanExecute(null) && k.NameHilfe == k.Beschreibung + ".", "ohne Einsatzleitung nur Anzeige: " + k.NameHilfe);
            k.RaidOeffnenCommand.Execute(null);
            Pruefe.Wahr(ReferenceEquals(vorn, h.AktiverBereich) && h.Bereiche.All(x => x.Kennung != HauptViewModel.RaidKennung), "sonst nichts");
            ff.Dispose();
        }

        using var a = new Aufbau(dienst: FunkDienstAttrappe.MitRaidabend());
        var h2 = a.Haupt(out var funkfeld, out _, out _, out _);
        var kobold = Z(h2.WerIstDa, R.Kobold);
        Pruefe.Wahr(kobold.NameKlickbar && kobold.RaidOeffnenCommand.CanExecute(null) && kobold.NameHilfe.EndsWith(", online. Klick öffnet die Funkfeld-Zuteilung.", StringComparison.Ordinal),
            "Einsatzleitung: " + kobold.NameHilfe);
        kobold.RaidOeffnenCommand.Execute(null);
        Pruefe.Gleich(HauptViewModel.RaidKennung, h2.AktiverBereich.Kennung, "die Raid-Seite ist vorn");

        h2.AktiverBereich = Seiten.Start(h2);
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Beauftragung = null } });
        Pruefe.Wahr(!kobold.NameKlickbar && !kobold.RaidOeffnenCommand.CanExecute(null), "Auftrag zu Ende: nur Anzeige");
        kobold.RaidOeffnenCommand.Execute(null);
        Pruefe.Gleich(HauptViewModel.StartKennung, h2.AktiverBereich.Kennung, "nichts geöffnet");
        funkfeld.Dispose();
    }

    [Test]
    public static async Task MeinProfil_NamenRollenZeichenUndErgaenzen()
    {
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
        var m = new MeinProfilViewModel(a.Dienst, a.Ablage, a.Takt);
        Pruefe.Wahr(m is { Aktiv: true, ScName: "Mustermann", DiscordName: "Mustermann/Max", KarteFreigeben: true, ZeichenZeigen: true, KannErgaenzen: true, HatAbzeichen: true },
            "Stand aus dem Konto");
        Pruefe.Falsch(m.HatKatalog, "Katalog erst beim Zeigen");
        await m.LadenAsync();
        Pruefe.Wahr(m.HatKatalog && m.Wuensche.Count == 6 && m.VomAdmin.SequenceEqual(["Medium Fighter Pilot", "Ingenieur"]), "Katalog, zwei Rollen vom Admin");
        var pilot = m.Wuensche.Single(r => r.Id == 1);
        var marine = m.Wuensche.Single(r => r.Id == 4);
        Pruefe.Wahr(pilot is { VomAdmin: true, Gewaehlt: true, ErsteWahl: true, Waehlbar: false } && pilot.Text == "Medium Fighter Pilot (Flug), vom Admin eingetragen", pilot.Text);
        Pruefe.Wahr(marine is { VomAdmin: false, Gewaehlt: true, ErsteWahl: false }, "Marine gewünscht");

        // Erste Wahl: genau eine; ein Wunsch als erste Wahl wird dabei abgehakt.
        var sani = m.Wuensche.Single(r => r.Id == 5);
        sani.ErsteWahl = true;
        Pruefe.Wahr(sani.Gewaehlt && !pilot.ErsteWahl && m.Wuensche.Count(r => r.ErsteWahl) == 1, "Sanitäter erste Wahl");
        marine.Gewaehlt = false;
        await m.WuenscheSpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Lieblingsrollen [5] *5") && m.RollenHinweis == "Gespeichert.", string.Join(" | ", a.Dienst.Aufrufe));
        foreach (var r in m.Wuensche.Where(r => !r.VomAdmin)) r.Gewaehlt = true;
        Pruefe.Wahr(m.Wuensche.Count(r => r.Gewaehlt && !r.VomAdmin) <= MeinProfilViewModel.MaxWuensche, "der Beispielkatalog bleibt unter acht");

        // Namen: bis zu zwei weitere SC-Namen, gültig nach den Regeln des Servers.
        m.WeitereScNamen = "Mustermann Alt, Mustermann Zwei, Mustermann Drei";
        await m.NamenSpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(m.NamenFehler == "Höchstens zwei weitere SC-Namen.", "Fehler: " + m.NamenFehler);
        m.WeitereScNamen = "Mustermann Alt, S";
        await m.NamenSpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(m.NamenFehler!.Contains("\"S\"", StringComparison.Ordinal), m.NamenFehler);
        m.WeitereScNamen = "Mustermann Alt";
        await m.NamenSpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Profil [Mustermann,Mustermann Alt]") && m.NamenHinweis == "Gespeichert.", string.Join(" | ", a.Dienst.Aufrufe));

        // Den Discord-Namen ändert nur ein Admin (A53): "Ich" zeigt ihn mit Hinweis und schickt ihn nicht mit. Ändert der Admin
        // ihn, steht der neue sofort da.
        Pruefe.Falsch(a.Dienst.Aufrufe.Any(x => x.Contains("discord=", StringComparison.Ordinal)), "kein Discord-Name in ProfilSetzen");
        Pruefe.Wahr(m.DiscordNameAnzeige == "Mustermann/Max" && MeinProfilViewModel.DiscordNameHinweis.Contains("Admin", StringComparison.Ordinal), "nur angezeigt");
        Pruefe.Falsch(typeof(MeinProfilViewModel).GetProperty(nameof(MeinProfilViewModel.DiscordName))!.SetMethod?.IsPublic == true, "kein öffentlicher Setter");
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { DiscordName = "Mustermann/Neu" } });
        Pruefe.Gleich("Mustermann/Neu", m.DiscordNameAnzeige, "Änderung des Admins kommt an");
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { DiscordName = null } });
        Pruefe.Gleich("noch keiner", m.DiscordNameAnzeige, "ohne Discord-Namen");
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { DiscordName = "Mustermann/Max" } });

        // Schalter am Konto (Antwort 3) und beim Betrachter.
        m.KarteFreigeben = false;
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Profil karte=False") && a.Dienst.Lage.Konto!.KarteFreigeben == false, "Freigabe aus geht an den Server");
        m.ZeichenZeigen = false;
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Profil zeichen=False"), "Zeichen zeigen aus");
        var geaendert = 0;
        m.AnzeigeGeaendert += () => geaendert++;
        m.ZeichenImFunkfeld = true;
        m.ZeichenNebenNamen = false;
        Pruefe.Wahr(geaendert == 2 && a.Ablage.Aktuell.Anzeige is { ZeichenImFunkfeld: true, ZeichenNebenNamen: false }, "Betrachter-Schalter nur hier");

        // Antwort 4: Führung ergänzt den Katalog; was es gibt, gibt es nicht zweimal.
        m.NeueRolle = "medium gunner";
        await m.RolleErgaenzenCommand.ExecuteAsync(null);
        Pruefe.Wahr(m.RollenFehler!.Contains("gibt es schon", StringComparison.Ordinal), m.RollenFehler);
        m.NeueRolle = "Scout";
        m.NeueGruppe = "Flug";
        await m.RolleErgaenzenCommand.ExecuteAsync(null);
        Pruefe.Wahr(a.Dienst.Aufrufe.Contains("Katalog ergänzen 7 v3") && m.Wuensche.Count == 7 && m.RollenHinweis == "Rolle Scout steht jetzt im Katalog.", "Hinweis: " + m.RollenHinweis);

        // Ohne Verbindung: nichts geht an den Server, die Schalter sagen warum.
        a.Dienst.LageSetzen(l => l with { Status = Verbindungsstatus.Getrennt });
        Pruefe.Falsch(m.Aktiv || m.NamenSpeichernCommand.CanExecute(null) || m.RolleErgaenzenCommand.CanExecute(null), "getrennt");
        m.KarteFreigeben = true;
        Pruefe.Wahr(m.SchalterFehler!.Contains("erst mit Verbindung", StringComparison.Ordinal) && !m.KarteFreigeben, "Schalter springt zurück: " + m.SchalterFehler);

        // Funker ohne Führung: kein Ergänzen.
        a.Dienst.LageSetzen(l => l with { Status = Verbindungsstatus.Aktiv, Konto = l.Konto! with { Rechte = [] } });
        Pruefe.Falsch(m.KannErgaenzen, "Funker ergänzt nicht");
    }

    [Test]
    public static async Task Anmeldung_NamenAusDemSetupUndFrageDesClients()
    {
        using var o = new Testordner();
        var datei = o.Datei(Anmeldedatei.Dateiname);
        Pruefe.Gleich(Path.Combine(o.Pfad, "anmeldung.json"), Anmeldedatei.PfadNeben(o.Datei("einstellungen.json")), "neben den Einstellungen");
        Pruefe.Gleich(null, Anmeldedatei.Lesen(datei), "ohne Datei nichts");

        // Wie Inno Setup schreibt: UTF-8 mit Byte-Reihenfolge-Marke, Namen mit Umlaut, Schrägstrich und Anführungszeichen.
        File.WriteAllBytes(datei, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\"scName\": \" Brändy \", \"discordName\": \"Brandy/\\\"Lea\\\"\"}")]);
        Pruefe.Gleich(("Brändy", "Brandy/\"Lea\""), Anmeldedatei.Lesen(datei)!.Value, "mit Marke und Escapes");
        var anmeldung = new AnmeldeEinstellungen();
        Pruefe.Wahr(Anmeldedatei.Uebernehmen(anmeldung, datei) && anmeldung is { ScName: "Brändy", DiscordName: "Brandy/\"Lea\"", Bestaetigt: false }, "übernommen, nicht bestätigt");
        anmeldung.ScName = "Mustermann";
        Pruefe.Falsch(Anmeldedatei.Uebernehmen(anmeldung, datei) || anmeldung.ScName != "Mustermann", "vorhandene Namen bleiben");

        // Nachsichtig beim Lesen, streng bei den Namen: Was nicht passt, fällt weg.
        File.WriteAllText(datei, "{\"scName\": \"<script>\", \"discordName\": \"Mustermann\u202E/Max\"}");
        Pruefe.Gleich(null, Anmeldedatei.Lesen(datei), "ungültige Namen: nichts");
        File.WriteAllText(datei, "{\"scName\": \"Mustermann\", /* Kommentar */ \"discordName\": 7,}");
        Pruefe.Gleich(("Mustermann", (string?)null), Anmeldedatei.Lesen(datei)!.Value, "nur der gültige Teil");
        File.WriteAllText(datei, "kaputt");
        Pruefe.Gleich(null, Anmeldedatei.Lesen(datei), "kein JSON");
        File.WriteAllText(datei, "{\"scName\": \"" + new string('a', 5000) + "\"}");
        Pruefe.Gleich(null, Anmeldedatei.Lesen(datei), "zu groß");

        // Die Frage des Clients in der REGISTRIERUNG (A42): bestätigt geht es ohne Frage, sonst wartet die Karte.
        using var b = new Aufbau(FunkLage.Leer);
        b.Ablage.Aktuell.Anmeldung.ScName = "Mustermann";
        b.Ablage.Aktuell.Anmeldung.DiscordName = "Mustermann/Max";
        b.Ablage.Aktuell.Anmeldung.Bestaetigt = true;
        var vm = new VerbindungViewModel(b.Dienst, b.Ablage, b.Takt, b.Fenster, ohneProfil: true);
        Pruefe.Gleich(new Anmeldedaten("Mustermann", "Mustermann/Max"), await vm.AnmeldungAsync(CancellationToken.None), "bestätigt: ohne Frage");
        Pruefe.Falsch(vm.ZeigtAnmeldung, "keine Karte");

        b.Ablage.Aktuell.Anmeldung.Bestaetigt = false;
        using var abbruch = new CancellationTokenSource();
        var frage = vm.AnmeldungAsync(abbruch.Token);
        Pruefe.Wahr(vm.AnmeldungWartet && vm.ZeigtAnmeldung && vm.AnmeldeKnopf == "So anmelden", "Karte wartet");
        await abbruch.CancelAsync();
        Pruefe.Gleich(null, await frage, "Frist: jetzt nicht");
        Pruefe.Falsch(vm.AnmeldungWartet, "Karte zu");

        var zweite = vm.AnmeldungAsync(CancellationToken.None);
        vm.AnmeldungAbbrechenCommand.Execute(null);
        Pruefe.Gleich(null, await zweite, "Jetzt nicht");

        var dritte = vm.AnmeldungAsync(CancellationToken.None);
        vm.ScName = "Mustermann ";
        vm.DiscordName = "Mustermann/Max";
        Pruefe.Gleich("Anmelden als Mustermann (Discord: Mustermann/Max) - stimmt das?", vm.AnmeldeFrage, "Frage mit beiden Namen");
        vm.SoAnmeldenCommand.Execute(null);
        Pruefe.Gleich(new Anmeldedaten("Mustermann", "Mustermann/Max"), await dritte, "So anmelden");
        Pruefe.Wahr(b.Ablage.Aktuell.Anmeldung.Bestaetigt && !b.Dienst.Aufrufe.Contains("Verbinden"), "bestätigt; der Client verbindet schon, kein zweites Verbinden");

        // Zweimal gefragt: die ältere Frage bekommt "jetzt nicht".
        b.Ablage.Aktuell.Anmeldung.Bestaetigt = false;
        var alt = vm.AnmeldungAsync(CancellationToken.None);
        var neu = vm.AnmeldungAsync(CancellationToken.None);
        Pruefe.Gleich(null, await alt, "ältere Frage abgelöst");
        vm.AnmeldungAbbrechenCommand.Execute(null);
        Pruefe.Gleich(null, await neu, "neuere abgebrochen");
    }

    [Test]
    public static void Zeichen_KontrastUndVergleichsbild()
    {
        // Die Füllungen tragen die Form auf jeder dunklen Fläche des Clients (3:1, Grafik nach WCAG 1.4.11), auch auf dem
        // Leitfarbe der Global-Kachel; auf hellem Grund die Kante.
        foreach (var satz in Farbvorschlaege.Alle)
        {
            foreach (var grund in new[] { Palette.Anthrazit, Palette.Flaeche, Palette.FlaecheTief, satz.Leitfarbe })
            {
                foreach (var (name, farbe) in new[] { ("Gold", Zeichensatz.GoldMitte), ("Goldlicht", Zeichensatz.GoldHell), ("Stern", Zeichensatz.SternFarbe) })
                    Pruefe.Wahr(Kontrast.Verhaeltnis(farbe, grund) >= 3, $"{satz.Name}: {name} auf {grund} {Kontrast.Verhaeltnis(farbe, grund):0.00}:1");
            }
        }
        foreach (var kante in new[] { Zeichensatz.GoldKante, Zeichensatz.SternKante })
            Pruefe.Wahr(Kontrast.Verhaeltnis(kante, Colors.White) >= 3, "Kante auf hellem Grund: " + kante);
        // Die Kante des Kranzes trägt die Form auch auf jeder dunklen Fläche, samt der Leitfarbe der Titelleiste.
        foreach (var grund in Farbvorschlaege.Alle.Select(s => s.Leitfarbe).Concat([Palette.Anthrazit, Palette.Flaeche, Palette.FlaecheTief]))
            Pruefe.Wahr(Kontrast.Verhaeltnis(Zeichensatz.GoldKante, grund) >= 3, $"Goldkante auf {grund}: {Kontrast.Verhaeltnis(Zeichensatz.GoldKante, grund):0.00}:1");

        // Seit 0.11.7 zeichnet der Client FUEHRUNG nicht mehr; Abzeichen stehen nach den Automatikzeichen, Ränge vor Tätigkeiten.
        var satzBilder = Zeichensatz.Bilder(["T_GUNNER_M", "FUEHRUNG", "UNBEKANNT", "ADMIN", "NAVY_03", "ADMIN", "EINSATZLEITUNG", "MARINE_04"]);
        Pruefe.Wahr(satzBilder.Select(z => z.Kennung).SequenceEqual(["ADMIN", "EINSATZLEITUNG", "MARINE_04", "NAVY_03", "T_GUNNER_M"]),
            "feste Reihenfolge, FUEHRUNG und Unbekanntes fallen weg: " + string.Join(",", satzBilder.Select(z => z.Kennung)));
        Pruefe.Wahr(satzBilder.All(z => z.Bild.IsFrozen), "eingefroren, über Threads teilbar");
        Pruefe.Gleich(Kranzform.Klassisch, Zeichensatz.Voreinstellung, "Voreinstellung Klassisch (Sarevoc, A53)");
        Pruefe.Wahr(ReferenceEquals(Zeichensatz.Kranz(Kranzform.Klassisch), Zeichensatz.Bild("ADMIN")), "Voreinstellung Klassisch, einmal gezeichnet");

        Bildlauf.Ausfuehren(_ =>
        {
            // Gezeichnet wie neben Namen (14 Bildpunkte) und größer (20): Wie viel der Fläche hebt sich 3:1 vom Grund ab? Die
            // Voreinstellung muss schon in 14 Bildpunkten tragen, die anderen Formen erst ab 20 (dafür das Vergleichsbild).
            // Verlangt auf den dunklen Flächen des Clients; Weiß steht nur zur Auskunft da, denn keine Farbstellung ist hell.
            var befund = new List<string>();
            var zuSchwach = new List<string>();
            var bilder = Enum.GetValues<Kranzform>().Select(f => (Name: "Kranz " + f, Bild: (ImageSource)Zeichensatz.Kranz(f), Pflicht14: f == Zeichensatz.Voreinstellung))
                .Append(("Stern", Zeichensatz.SternBild(), true));
            foreach (var (name, bild, pflicht14) in bilder)
            {
                foreach (var groesse in Groessen)
                {
                    foreach (var grund in new[] { Palette.Flaeche, Palette.FlaecheTief, Farbvorschlaege.Anthrazit.Leitfarbe, Colors.White })
                    {
                        var anteil = Deutlich(bild, groesse, grund);
                        var text = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} {groesse}px auf {grund}: {anteil:P0}");
                        befund.Add(text);
                        if ((pflicht14 || groesse >= 20) && grund != Colors.White && anteil < 0.12) zuSchwach.Add(text);
                    }
                }
            }
            Console.WriteLine("  Zeichen, deutliche Bildpunkte: " + string.Join("; ", befund));
            Pruefe.Gleich(0, zuSchwach.Count, "zu schwach: " + string.Join("; ", zuSchwach));

            if (Bildablage.Ordner() is null) return;
            Bildablage.Ablegen(Vergleichsbild(), "zeichen-varianten");
        });
    }

    private static readonly int[] Groessen = [14, 20];

    /// <summary>Anteil der Bildpunkte, die sich mindestens 3:1 vom Grund abheben, gezeichnet in <paramref name="groesse"/> Bildpunkten.</summary>
    private static double Deutlich(ImageSource bild, int groesse, Color grund)
    {
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(grund), null, new Rect(0, 0, groesse, groesse));
            dc.DrawImage(bild, new Rect(0, 0, groesse, groesse));
        }
        var rt = new RenderTargetBitmap(groesse, groesse, 96, 96, PixelFormats.Pbgra32);
        rt.Render(v);
        var deutlich = 0;
        for (var y = 0; y < groesse; y++)
            for (var x = 0; x < groesse; x++)
                if (Kontrast.Verhaeltnis(Bildlauf.Pixel(rt, x, y), grund) >= 3) deutlich++;
        return deutlich / (double)(groesse * groesse);
    }

    /// <summary>Die drei Kranzformen und der Stern in 14, 20 und 32 Bildpunkten auf den Flächen des Clients und auf Weiß (FUEHRUNG seit 0.11.7 nicht mehr).</summary>
    private static RenderTargetBitmap Vergleichsbild()
    {
        const double links = 150, spalte = 44, zeile = 64, luecke = 28;
        var formen = Enum.GetValues<Kranzform>();
        var gruende = new (string Name, Color Farbe)[]
        {
            ("Fläche tief", Palette.FlaecheTief), ("Fläche", Palette.Flaeche), ("Leitfarbe", Farbvorschlaege.Anthrazit.Leitfarbe), ("Weiß", Colors.White),
        };
        var bilder = formen.Select(f => (Name: f + (f == Zeichensatz.Voreinstellung ? " (Voreinstellung)" : ""), Bild: (ImageSource)Zeichensatz.Kranz(f)))
            .Append(("Einsatzleitung", Zeichensatz.SternBild())).ToList();
        var groessen = new[] { 14, 20, 32 };
        var breite = links + (bilder.Count * ((groessen.Length * spalte) + luecke)) + 20;
        var bogen = new Bogen(breite, 70 + (gruende.Length * zeile), 1.75, Palette.Anthrazit);
        bogen.Text("Zeichen neben Namen (A49): drei Kranzformen für Admin, dazu Einsatzleitung, in 14, 20 und 32 px", 12, 8, 14, fett: true);
        for (var i = 0; i < bilder.Count; i++)
        {
            var x0 = links + (i * ((groessen.Length * spalte) + luecke));
            bogen.Text(bilder[i].Name, x0, 32, 11, Palette.Text2);
            for (var g = 0; g < gruende.Length; g++)
            {
                var y0 = 52 + (g * zeile);
                var grund = gruende[g];
                bogen.Zeichnung(dc => dc.DrawRectangle(Bogen.Pinsel(grund.Farbe), null, new Rect(x0 - 6, y0, groessen.Length * spalte, zeile - 8)));
                for (var s = 0; s < groessen.Length; s++)
                {
                    var groesse = groessen[s];
                    var bild = bilder[i].Bild;
                    bogen.Zeichnung(dc => dc.DrawImage(bild, new Rect(x0 + (s * spalte), y0 + 8 + ((32 - groesse) / 2.0), groesse, groesse)));
                }
            }
        }
        for (var g = 0; g < gruende.Length; g++) bogen.Text(gruende[g].Name, 12, 52 + (g * zeile) + 20, 12);
        return bogen.Fertig();
    }

    /// <summary>"Ich" unter Einstellungen (E14) im Hauptfenster, in Anthrazit mit Kontrast; mit <c>FUNK_BILDER</c> das Bild einstellungen-ich.</summary>
    [Test]
    public static void Ich_BilderUndKontrast()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
                a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
                // Ein erfundener Gerätename, sonst stünde der Name dieses Rechners im Bild.
                a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
                var h = a.Haupt(out _, out _, out _, out _);
                var was = satz.Name + ", einstellungen-ich";
                // Ich mit Zeichen und Sichtbarkeit in den Feinheiten (E14).
                Seiten.Feinheiten(h, Abschnittskennung.Ich);
                Pruefe.Wahr(h.MeinProfil.HatKatalog, was + ": Katalog beim Zeigen geladen");
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var bild = Ganz(haupt, 1280);
                var knopf = (Button)haupt.FindName("WerIstDaKnopf");
                Pruefe.Wahr(Sichtbar(knopf, "Wer ist da (6)"), was + ": Knopf mit Zahl");
                // Seit 0.10.0 (E14) eine Karte "Ich": Namen und Rollenwünsche offen, Zeichen und Sichtbarkeit in den Feinheiten (bis 0.9.3 drei Karten).
                var inhalt = Bildlauf.Nachfahren<Border>(Bildlauf.Wurzel(haupt)).Single(b => b.Name == "IchKarte");
                Pruefe.Wahr(Sichtbar(inhalt, "Mein Discord-Name") && Sichtbar(inhalt, "Rollenwünsche") && Sichtbar(inhalt, "Zeichen und Sichtbarkeit"),
                    was + ": Namen, Rollenwünsche und die Feinheiten Zeichen und Sichtbarkeit");
                Pruefe.Wahr(Sichtbar(inhalt, "Mustermann/Max") && Sichtbar(inhalt, MeinProfilViewModel.DiscordNameKurz)
                    && !Bildlauf.Nachfahren<TextBox>(inhalt).Any(t => ZuSehen(t) && t.Text == "Mustermann/Max"), was + ": Discord-Name nur angezeigt, mit Hinweis (A53)");
                // Seit dem Befund der Prüfung (m-4): die Rollen als Umschalter, vom Admin eingetragene einmal mit Schloss; der Katalog in den Feinheiten.
                Pruefe.Wahr(Sichtbar(inhalt, "Medium Fighter Pilot") && Bildlauf.Nachfahren<System.Windows.Shapes.Path>(inhalt).Any(p => p.Name == "RolleSchloss" && ZuSehen(p))
                    && Sichtbar(inhalt, "Rolle zum Katalog hinzufügen"), was + ": Rollen mit Schloss und Ergänzen");
                Pruefe.Wahr(Sichtbar(inhalt, "Führung") && Bildlauf.Nachfahren<Image>(inhalt).Any(ZuSehen), was + ": eigenes Zeichen");
                KontrastPruefen(inhalt, satz, was);
                if (satz.Stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(bild, "einstellungen-ich");
            }
        });
    }

    // ------------------------------------------------------------------------------------------------------------------------------
    // Wer ist da gezeichnet
    // ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Sarevoc 13.10.2026: "Superschütze/Alexander" ist das Maß, 22 Zeichen; dazu zwei weitere erfundene Namen derselben Länge, seit der Gegenprüfung
    /// 0.11.5 einer davon ganz in Großbuchstaben (breiter: 174 statt 138 Einheiten).
    /// </summary>
    internal static readonly string[] LangeNamen = ["Superschütze/Alexander", "WOLKENSCHIEBER/BARBARA", "Morgenstern/Wilhelmine"];

    /// <summary>Ein erfundener Discord-Name mit 32 Zeichen, so lang, wie der Server ihn annimmt (Zeichenregeln.ScNameMax); er steht kleiner, aber ganz.</summary>
    internal const string LaengsterName = "Wolkenschieber/Barbara Sternhell";

    /// <summary>
    /// Der Beispielabend der Attrappe (Namen erfunden) aus Mustermanns Sicht (Einsatzleitung im Auftrag), mit drei Namen von 22 Zeichen
    /// (<see cref="LangeNamen"/> an Kobold, Eule und Falke) und einem von 32 (<see cref="LaengsterName"/> an Pollux), Anker stumm bis zur nächsten
    /// Stunde (in eigener Uhr, denn das Hauptfenster-Modell rechnet mit ihr) und Hagel offline. Kobold, Eule und Falke sitzen zusätzlich in Orga;
    /// Anker und Quarz (nur Einheitsnetze), Hagel, Pollux und Zinnober sind in keinem Kanal der Raid-Seite.
    /// </summary>
    internal static FunkDienstAttrappe Abend()
    {
        var d = FunkDienstAttrappe.MitRaidabend();
        var bis = DateTimeOffset.Now.AddMinutes(50).ToUnixTimeMilliseconds();
        FunkerInfo MitOrga(FunkerInfo f, string name) =>
            f with { DiscordName = name, Details = f.Details! with { Felder = [.. f.Details.Felder, new FeldInfo(5, R.NetzOrga, true, false, false)] } };
        d.LageSetzen(l => FunkDienstAttrappe.Belegungsstand(l with
        {
            Funker = [.. l.Funker.Select(f => f.KontoId switch
            {
                R.Kobold => MitOrga(f, LangeNamen[0]),
                R.Eule => MitOrga(f, LangeNamen[1]),
                R.Falke => MitOrga(f, LangeNamen[2]),
                R.Pollux => f with { DiscordName = LaengsterName },
                R.Anker => f with { Details = f.Details! with { Stumm = [new Stummschaltung { NetzId = 0, Bis = bis, Durch = R.Mustermann }] } },
                R.Hagel => f with { Online = false },
                _ => f,
            })],
        }));
        return d;
    }

    /// <summary>Die Knöpfe einer Art (Name im Vorlagenbaum) unter <paramref name="wurzel"/>, die zu sehen sind.</summary>
    private static List<T> Teile<T>(DependencyObject wurzel, string name) where T : FrameworkElement =>
        [.. Bildlauf.Nachfahren<T>(wurzel).Where(e => e.Name == name && ZuSehen(e))];

    private static string T(FormattableString s) => s.ToString(CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>
    /// Die Spalte gezeichnet (Hauptfenster, Spalte offen, Einsatzleitung, Beispielabend mit drei Namen von 22 Zeichen, einer in Großbuchstaben, und
    /// einem von 32) bei 100 und 175 %, in Anthrazit: je Person genau eine Zeile mit vier Teilen von links nach rechts (Name, Mikrofon, Status, Wippe),
    /// alle Name-Knöpfe gleich breit (162, davor das Abzeichen mit 28) und 30 hoch, kein Name gekürzt (Sarevocs Maß in voller Schrift, breitere kleiner, aber im Knopf), nichts
    /// abgeschnitten, auch mit Bildlauf; das Mikrofon eines Stummen mit Querstrich in der roten Schrift der Farbstellung,
    /// die Statusgrafik grün oder gelb, die Wippe nicht in der eigenen Zeile; jeder Knopf mit Hilfetext und Namen für Vorleser, die bedienbaren
    /// mit der Tastatur erreichbar, Kontrast, nur einfache Zeichen, Zugriffstasten eindeutig. Mit <c>FUNK_BILDER</c> weristda-zeilen-anthrazit,
    /// weristda-zeilen-175 und weristda-offline-anthrazit.
    /// </summary>
    [Test]
    public static void WerIstDa_Zeilen_GleichBreitNichtsAbgeschnittenKontrast_Bilder()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                var kurz = satz.Stellung switch { _ => "anthrazit" };
                foreach (var skala in Skalen)
                {
                    foreach (var offline in OhneUndMitOffline)
                    {
                        if (offline && (skala != 1.0 || satz.Stellung != Farbvorschlaege.Voreinstellung)) continue;
                        var was = T($"{satz.Name}, {skala * 100:0} %{(offline ? ", auch offline" : "")}");
                        using var a = new Aufbau(dienst: Abend());
                        a.Ablage.Aktuell.Audio.Hoerweise = Hoerweise.Headset;
                        a.Ablage.Aktuell.Verbindung.Geraetename = "Gaming-PC";
                        var h = a.Haupt(out var funkfeld, out _, out _, out var netze);
                        h.AktiverBereich = Seiten.Start(h);
                        var w = h.WerIstDa;
                        w.UmschaltenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                        if (offline) w.AuchOffline = true;
                        netze.PegelSetzenAsync(R.Brandung, 4).GetAwaiter().GetResult();
                        var haupt = new Hauptfenster(h, a.Ablage);
                        fenster.Add(haupt);
                        var (bild, _) = Bildlauf.Zeichnen(haupt, skala, new Size(1280, 1000));
                        var spalte = (Border)haupt.FindName("WerIstDaSpalte");
                        Pruefe.Wahr(ZuSehen(spalte), was + ": Spalte offen");
                        ZeilenPruefen(spalte, w, satz, skala, was);
                        Pruefe.Falsch(Lauf(spalte).ComputedVerticalScrollBarVisibility == Visibility.Visible, was + ": alles ohne Bildlauf");
                        if (offline)
                        {
                            Pruefe.Wahr(w.Zeilen.Any(z => z is { KontoId: R.Kranich, Online: false }) && w.Zeilen.Last().Online == false, was + ": offline unten");
                            Bildablage.Ablegen(bild, "weristda-offline-" + kurz);
                        }
                        else if (skala == 1.0)
                        {
                            Bildablage.Ablegen(bild, "weristda-zeilen-" + kurz);
                        }
                        else if (satz.Stellung == Farbvorschlaege.Voreinstellung)
                        {
                            Bildablage.Ablegen(bild, "weristda-zeilen-175");
                        }

                        // Niedriger: mit Bildlauf, die Zeilen bleiben gleich breit und ganz.
                        _ = Bildlauf.Zeichnen(haupt, skala, new Size(1280, 520));
                        Pruefe.Gleich(Visibility.Visible, Lauf(spalte).ComputedVerticalScrollBarVisibility, was + ": jetzt mit Bildlauf");
                        Pruefe.Wahr(Lauf(spalte).ViewportWidth >= 310 - 0.5, T($"{was}: die Zeilen passen neben den Rollbalken ({Lauf(spalte).ViewportWidth:0.0})"));
                        // Gerollt steht ein Teil der Zeilen außer Sicht; geprüft wird in der Liste selbst, dass keine Zeile beschnitten ist oder hinausragt.
                        NichtsAbgeschnitten(Bildlauf.Nachfahren<ItemsControl>(spalte).Single(i => i.Name == "WerIstDaListe"), was + ", mit Bildlauf");

                        if (satz.Stellung == Farbvorschlaege.Voreinstellung && skala == 1.0 && !offline)
                        {
                            var doppelt = OberflaecheHauptfensterTests.Doppelte(OberflaecheHauptfensterTests.Zugriffstasten(Bildlauf.Wurzel(haupt)), "Wer ist da").ToList();
                            Pruefe.Gleich(0, doppelt.Count, "doppelte Zugriffstasten: " + string.Join(" | ", doppelt));
                        }
                        funkfeld.Dispose();
                    }
                }
            }
        });
    }

    private static readonly double[] Skalen = [1.0, 1.75];

    private static readonly bool[] OhneUndMitOffline = [false, true];

    /// <summary>Der Bildlauf der Liste in der Spalte.</summary>
    private static ScrollViewer Lauf(DependencyObject spalte) => Bildlauf.Nachfahren<ScrollViewer>(spalte).Single(s => s.Name == "WerIstDaLauf");

    /// <summary>Was jede gezeichnete Zeile erfüllen muss (siehe <see cref="WerIstDa_Zeilen_GleichBreitNichtsAbgeschnittenKontrast_Bilder"/>).</summary>
    private static void ZeilenPruefen(Border spalte, WerIstDaViewModel w, Farbsatz satz, double skala, string was)
    {
        // Bei 175 % rundet das Layout auf ganze Bildpunkte: bis zu einem Bildpunkt Spiel.
        var spiel = (1 / skala) + 0.01;
        var namen = Teile<Button>(spalte, "NameKnopf");
        Pruefe.Gleich(w.Zeilen.Count, namen.Count, was + ": eine Zeile je Person");
        Pruefe.Wahr(namen.All(n => Math.Abs(n.ActualWidth - 162) < spiel && Math.Abs(n.ActualHeight - 30) < spiel && Math.Abs(n.ActualWidth - namen[0].ActualWidth) < 0.01),
            was + ": alle Namen gleich breit (162) und 30 hoch: " + string.Join(", ", namen.Select(n => T($"{n.ActualWidth:0.##}x{n.ActualHeight:0.##}")).Distinct()));
        var wurzel = (FrameworkElement)spalte;
        foreach (var name in namen)
        {
            var z = (WerIstDaZeile)name.DataContext;
            var zeile = (StackPanel)VisualTreeHelper.GetParent(name);
            var teile = zeile.Children.OfType<FrameworkElement>().ToList();
            // Sarevoc 14.10.2026: "erst Abzeichen, dann Name".
            Pruefe.Wahr(teile.Select(t => t.Name).SequenceEqual(["AbzeichenKnopf", "NameKnopf", "StummKnopf", "StatusKnopf", "Wippe"]), was + ": " + z.Name + ": genau fünf Teile, sonst nichts");
            var links = teile.Select(t => t.TransformToAncestor(wurzel).Transform(new Point(0, 0)).X).ToList();
            Pruefe.Wahr(links.Zip(links.Skip(1)).All(p => p.First < p.Second), was + ": " + z.Name + ": von links nach rechts Abzeichen, Name, Mikrofon, Status, Wippe");
            Pruefe.Wahr(Math.Abs(teile[^1].TransformToAncestor(wurzel).Transform(new Point(teile[^1].ActualWidth, 0)).X - links[0] - 310) < 2 * spiel, was + ": jede Zeile 310 breit");
            // Der Name kürzt nie (Gegenprüfung 0.11.5, Befund 1): ganz gesetzt, und so gezeichnet, dass er im Knopf endet; passt er nicht in voller
            // Schrift, steht er kleiner. Sarevocs Maß "Superschütze/Alexander" steht in voller Schrift.
            var text = Bildlauf.Nachfahren<TextBlock>(name).Single(t => t.Name == "NameText");
            var breite = new FormattedText(text.Text, CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                text.FontSize, Brushes.White, 1).WidthIncludingTrailingWhitespace;
            Pruefe.Wahr(text.TextTrimming == TextTrimming.None && breite <= text.ActualWidth + 0.5, T($"{was}: \"{text.Text}\" ganz gesetzt ({breite:0.0} von {text.ActualWidth:0.0})"));
            var gezeichnet = text.TransformToAncestor(name).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
            var innen = name.ActualWidth - name.Padding.Right - name.BorderThickness.Right;
            var massstab = gezeichnet.Width / text.ActualWidth;
            Pruefe.Wahr(gezeichnet.Right <= innen + spiel && gezeichnet.Top >= -spiel && gezeichnet.Bottom <= name.ActualHeight + spiel,
                T($"{was}: \"{text.Text}\" endet im Knopf ({gezeichnet.Right:0.0} von {innen:0.0})"));
            Pruefe.Wahr(massstab >= 0.6, T($"{was}: \"{text.Text}\" lesbar ({massstab:0.00}, {text.FontSize * massstab:0.0} statt {text.FontSize})"));
            if (text.Text == LangeNamen[0]) Pruefe.Wahr(massstab > 0.999, T($"{was}: Sarevocs Maß in voller Schrift ({massstab:0.000})"));
            if (text.Text is LaengsterName || text.Text == LangeNamen[1]) Pruefe.Wahr(massstab < 1, T($"{was}: \"{text.Text}\" kleiner gesetzt ({massstab:0.00})"));

            // Mikrofon: stumm mit Querstrich in der roten Schrift der Farbstellung, sonst das Mikrofon in der Schrift des Knopfs.
            var stumm = (Button)teile[2];
            var symbol = Bildlauf.Nachfahren<System.Windows.Shapes.Path>(stumm).Single(p => p.Name == "StummSymbol");
            if (z.Stumm)
            {
                Pruefe.Wahr(ReferenceEquals(symbol.Data, Application.Current!.Resources["Symbol.MikrofonStumm"]) && Farbe(symbol.Stroke) == satz.RotSchrift,
                    was + ": " + z.Name + " stumm: rot " + Farbe(symbol.Stroke));
            }
            else
            {
                Pruefe.Wahr(ReferenceEquals(symbol.Data, Application.Current!.Resources["Symbol.Mikrofon"]) && Farbe(symbol.Stroke) != satz.RotSchrift, was + ": " + z.Name + " nicht stumm: neutral");
            }

            // Status: grün, gelb, grau (hohler Ring).
            var lampe = Bildlauf.Nachfahren<System.Windows.Shapes.Ellipse>(teile[3]).Single(e => e.Name == "Lampe");
            var soll = z.Kanalstand switch
            {
                Kanalstand.Eingeteilt => (Color?)Palette.Senden,
                Kanalstand.OhneKanal => Palette.Tunnel,
                _ => null,
            };
            Pruefe.Wahr(soll is { } f ? lampe.Fill is SolidColorBrush s && s.Color == f : lampe.Fill is SolidColorBrush { Color.A: 0 }, was + ": " + z.Name + ": Statusgrafik " + z.Kanalstand);

            // Wippe: nicht in der eigenen Zeile (ihr Platz bleibt), sonst zwei Hälften.
            Pruefe.Gleich(z.Eigene ? Visibility.Hidden : Visibility.Visible, teile[4].Visibility, was + ": " + z.Name + ": Wippe");

            // Jeder Knopf mit Hilfetext und Namen für Vorleser; was bedienbar ist, mit der Tastatur erreichbar und mit Fokusrahmen.
            foreach (var knopf in Bildlauf.Nachfahren<Button>(zeile).Where(ZuSehen))
            {
                var hilfe = knopf.IsEnabled ? Glasdesign.Hilfe.GetText(knopf) : Glasdesign.Hilfe.GetGesperrt(knopf);
                Pruefe.Wahr(!string.IsNullOrWhiteSpace(hilfe) && knopf.ToolTip is string, was + ": " + z.Name + ": " + knopf.Name + " mit Hilfetext");
                Pruefe.Wahr(!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(knopf)), was + ": " + z.Name + ": " + knopf.Name + " mit Namen für Vorleser");
                if (knopf.IsEnabled)
                    Pruefe.Wahr(knopf is { Focusable: true, IsTabStop: true } && knopf.FocusVisualStyle is not null && KeyboardNavigation.GetTabNavigation(zeile) != KeyboardNavigationMode.None,
                        was + ": " + z.Name + ": " + knopf.Name + " mit der Tastatur");
            }
        }
        // Mustermann leitet: Name und Mikrofon sind bedienbar, außer an Nordwind (Admin) und in der eigenen Zeile; ohne Verwaltungsteil der Status nicht.
        Pruefe.Wahr(namen.All(n => n.IsEnabled), was + ": jeder Name öffnet die Raid-Seite");
        Pruefe.Wahr(w.Zeilen.Where(z => z.Online).All(z => z.MikroKlickbar == (z.KontoId is not (R.Nordwind or R.Mustermann))), was + ": Mikrofon je Rang");
        Pruefe.Wahr(Teile<Button>(spalte, "StatusKnopf").All(k => !k.IsEnabled), was + ": Status ohne Verwaltungsteil nur Anzeige");
        Pruefe.Wahr(w.Zeilen.Any(z => z.Stumm) && w.Zeilen.Any(z => z.Kanalstand == Kanalstand.OhneKanal) && w.Zeilen.Any(z => z.PegelDb > 0), was + ": stumm, ohne Kanal und lauter gestellt dabei");
        NichtsAbgeschnitten(spalte, was);
        KontrastPruefen(spalte, satz, was, mindestens: 10);
        foreach (var t in Bildlauf.Nachfahren<TextBlock>(spalte).Where(ZuSehen))
        {
            var inhalt = Inhalt(t);
            Pruefe.Falsch(inhalt.Any(c => c is '\u2013' or '\u2014' or '\u2212' or '\u201E' or '\u201C' or '\u201D' or '\u2026' or '\u2022' or '\u00A0'), was + ": nur einfache Zeichen in \"" + inhalt + "\"");
        }
    }
}
