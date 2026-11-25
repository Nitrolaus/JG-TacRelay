// JG TacRelay - Maintainer: Sarevoc
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Funk.Client.Design;
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
/// Der Verwaltungsteil (src\Funk.Verwaltung, A43-A47) über einer Attrappe: Schritte vom Antrag auf den Admin-Schlüssel bis
/// zur offenen Admin-Sitzung, Freigeben erst nach dem Kennungsvergleich und mit frischem Code, Mitglieder und Rollenkatalog (seit
/// 0.11.8 ohne Untergilden). Dazu die Ansichten im Speicher gezeichnet (<see cref="Bildlauf"/>), im Hauptfenster wie mit dem Admin-Setup,
/// mit Kontrastregeln in allen drei Farbstellungen und ohne Bindungsfehler. Mit <c>FUNK_BILDER</c> entsteht je Schritt
/// verwaltung-&lt;schritt&gt;.png.
/// </summary>
internal static class OberflaecheVerwaltungTests
{
    [Test]
    public static async Task Verwaltung_Schritte_VomAntragBisZurSitzung()
    {
        var d = new VerwaltungsdienstAttrappe();
        d.LageSetzen(_ => Verwaltungslage.Getrennt);
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        Pruefe.Wahr(vm.IstGetrennt && d.CodeFragen is not null, "getrennt; die Codefrage hängt am Dienst");

        d.LageSetzen(_ => new Verwaltungslage { Verbunden = true, Name = "Nordlicht" });
        Pruefe.Wahr(vm.IstKeinAdmin, "verbunden, kein Admin");

        d.LageSetzen(l => l with { AntragMoeglich = true });
        Pruefe.Wahr(vm.IstEinrichten && vm.Kennung is null && vm.Beschreibung.Contains("TPM", StringComparison.Ordinal), "vorgesehen: einrichten");
        await vm.EinrichtenCommand.ExecuteAsync(null);
        Pruefe.Gleich(VerwaltungsdienstAttrappe.Kennung, vm.Kennung, "Admin-Kennung zum Vorlesen");
        Pruefe.Wahr(vm.Beschreibung.Contains("Telefon vor", StringComparison.Ordinal), "sagt, was mit der Kennung zu tun ist: " + vm.Beschreibung);

        d.LageSetzen(l => l with { AntragMoeglich = false, Admin = AdminStatus.Einrichtung });
        Pruefe.Wahr(vm.IstZweitfaktor && !vm.HatGeheimnis, "ernannt: Zweitfaktor");
        await vm.GeheimnisAbholenCommand.ExecuteAsync(null);
        Pruefe.Gleich("JBSW Y3DP EHPK 3PXP JBSW Y3DP EHPK 3PXP", vm.Geheimnis, "Geheimnis in Vierergruppen");
        Pruefe.Wahr(vm.QrBildGeometrie is not null && vm.QrSeite >= 45 + 8, "QR-Code mit Ruhezone (Fassung 7 oder mehr): " + vm.QrSeite);
        Pruefe.Wahr(vm.GeheimnisBis!.Contains("Uhr", StringComparison.Ordinal), "bis wann der erste Code");
        Pruefe.Wahr(VerwaltungViewModel.AppHinweis.Contains("Telefon", StringComparison.Ordinal) && VerwaltungViewModel.AppHinweis.Contains("Windows", StringComparison.Ordinal),
            "Hinweis: Telefon, sonst eine App unter Windows mit Sperre (Antwort 2)");
        // Seit 0.6.4 keine bestimmte App mehr, auch nicht als Beispiel (Sarevoc 04.10.2026): Jede Authenticator-App nach RFC 6238 geht.
        foreach (var marke in new[] { "Proton", "Google", "Microsoft" })
            Pruefe.Falsch(VerwaltungViewModel.AppHinweis.Contains(marke, StringComparison.Ordinal), "keine App genannt: " + marke);

        vm.EinrichtungCode = "12345";
        Pruefe.Falsch(vm.EinrichtungBestaetigenCommand.CanExecute(null), "fünf Ziffern reichen nicht");
        vm.EinrichtungCode = "000 000";
        await vm.EinrichtungBestaetigenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Fehler!.Contains("passt nicht", StringComparison.Ordinal) && vm.HatGeheimnis, "falscher Code: Geheimnis bleibt für den nächsten Versuch");
        vm.EinrichtungCode = "123-456";
        await vm.EinrichtungBestaetigenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.HatNotfallcodes && vm.Notfallcodes!.Count == 10, "zehn Notfallcodes, einmal");
        Pruefe.Wahr(!vm.HatGeheimnis && vm.QrBildGeometrie is null, "Geheimnis und QR-Code sind weg");
        Pruefe.Wahr(vm.IstAnmelden, "eingerichtet: anmelden");
        vm.NotfallcodesGesehenCommand.Execute(null);
        Pruefe.Falsch(vm.HatNotfallcodes, "Aufgeschrieben: die Codes kommen nie wieder");

        vm.Code = "000000";
        await vm.AnmeldenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.IstAnmelden && vm.Fehler!.Contains("passt nicht", StringComparison.Ordinal) && vm.Code.Length == 0, "falscher Code: Feld leer, Fehler");
        vm.MitNotfallcode = true;
        vm.Code = "123456";
        Pruefe.Falsch(vm.AnmeldenCommand.CanExecute(null), "Notfallcode hat zwölf Zeichen");
        vm.MitNotfallcode = false;
        await vm.AnmeldenCommand.ExecuteAsync(null);
        // Seit 0.8.0 (Umbau der Verwaltung) beginnt die offene Verwaltung mit der Übersicht "Heute"; die Anträge stehen dort mit Sprung.
        Pruefe.Wahr(vm.IstOffen && vm.IstHeute, "Sitzung offen, die Übersicht Heute vorn");
        Pruefe.Gleich("2 Anträge warten auf Freigabe.", vm.Ansteht[0].Text, "Heute: wartende Anträge");
        vm.SpringenCommand.Execute(vm.Ansteht[0]);
        Pruefe.Wahr(vm.IstAntraege, "ein Klick und die Anträge sind vorn");
        Pruefe.Wahr(vm.SitzungText!.Contains("offen bis", StringComparison.Ordinal), vm.SitzungText);
        Pruefe.Gleich("Anträge (2)", vm.AntraegeTitel, "Anträge gezählt");
        Pruefe.Wahr(vm.Mitglieder.Sichtbar.Count == 8 && vm.Katalog.Eintraege.Count == 6, "alles geladen");
        Pruefe.Falsch(d.Aufrufe.Any(x => x.Contains("Untergild", StringComparison.Ordinal)), "seit 0.11.8 keine Untergilden mehr geladen");
        Pruefe.Gleich("5 Konten, 2 vorgemerkt", vm.Mitglieder.Zaehlung, "Zählung ohne Anträge");

        d.LageSetzen(l => l with { NotfallcodesUebrig = 2 });
        Pruefe.Wahr(vm.SitzungText!.Contains("Nur noch 2 Notfallcodes", StringComparison.Ordinal), "wenige Notfallcodes: sagen");

        // Ein neuer Antrag kommt als Meldung; die Liste lädt nach.
        d.Antraege.Add(d.Antraege[0] with { Id = 103, Anzeige = "11AA-22BB-33CC-44DD", ScName = "Wolke", DiscordName = "Wolke", Vormerkungen = null });
        d.Senden(new Meldung { Art = MeldungsArten.AntragNeu });
        Pruefe.Gleich("Anträge (3)", vm.AntraegeTitel, "neuer Antrag per Meldung");

        await vm.AbmeldenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.IstAnmelden && vm.Hinweis!.Contains("Funk läuft weiter", StringComparison.Ordinal), "Verwaltung geschlossen, Funk läuft");
        d.LageSetzen(l => l with { Admin = AdminStatus.Gesperrt });
        Pruefe.Wahr(vm.IstGesperrt && vm.Beschreibung.Contains("Serverkonsole", StringComparison.Ordinal), "gesperrt: nur Sarevoc an der Konsole");

        vm.Dispose();
        Pruefe.Wahr(d.CodeFragen is null, "nach dem Schließen fragt niemand mehr");
        Pruefe.Wahr(VerwaltungViewModel.Vierergruppen("ABCDEFGHIJ") == "ABCD EFGH IJ", "Vierergruppen mit Rest");
    }

    [Test]
    public static async Task Verwaltung_Antraege_FreigebenErstNachKennungsvergleichUndMitCode()
    {
        var d = new VerwaltungsdienstAttrappe();
        using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var brandy = vm.Antraege.Antraege.Single(z => z.ScName == "Brandy");
        var falke = vm.Antraege.Antraege.Single(z => z.ScName == "Falke");
        Pruefe.Wahr(brandy.Zeit.StartsWith("heute ", StringComparison.Ordinal) && brandy.Schluesselart == "TPM (laut Client, ungeprüft)", brandy.Zeit + " / " + brandy.Schluesselart);
        Pruefe.Gleich("Software (laut Client, ungeprüft)", falke.Schluesselart, "Schlüsselart nur als Angabe");
        Pruefe.Wahr(falke.Vergeben!.Contains("Falke ist schon vergeben", StringComparison.Ordinal), "vergebener SC-Name");
        Pruefe.Wahr(AntraegeViewModel.Regeln.Count == 4 && AntraegeViewModel.Regeln[0].Contains("Benutzernamen", StringComparison.Ordinal), "Merkzettel neben dem Knopf");

        // Genau ein Treffer über den SC-Namen ist vorgewählt, übernommen wird er nur mit dem Freigeben.
        Pruefe.Gleich(7u, brandy.VormerkungId, "Vorschlag Brandy vorgewählt");
        Pruefe.Gleich("Vormerkung Brandy (Discord Brandy/Lea): Marine, gleicher SC-Name", brandy.Vorschlaege[1].Text, "Vorschlag mit Rollen, seit 0.11.8 ohne Kürzel");
        Pruefe.Falsch(falke.HatVorschlaege, "ohne Vormerkung keine Vorschläge");

        // Seit 0.6.3: vergleichen und "Verifiziert" klicken statt abtippen; der Grund für einen gesperrten Knopf steht sichtbar da.
        Pruefe.Wahr(brandy.KennungHinweis.StartsWith("Lass dir von Brandy die Gerätekennung", StringComparison.Ordinal)
            && !brandy.KennungHinweis.Contains("Discord", StringComparison.Ordinal), brandy.KennungHinweis);
        Pruefe.Falsch(brandy.FreigebenCommand.CanExecute(null), "ohne Verifiziert kein Freigeben");
        Pruefe.Wahr(brandy.FreigabeGesperrt!.Contains("Verifiziert", StringComparison.Ordinal) && brandy.VerifiziertText == "Verifiziert" && brandy.ZeigtAusrufe,
            "Grund sichtbar, Knopf mit gelbem Zeichen: " + brandy.FreigabeGesperrt);
        brandy.VerifizierenCommand.Execute(null);
        Pruefe.Wahr(brandy.Verifiziert && brandy.FreigebenCommand.CanExecute(null) && brandy.FreigabeGesperrt is null
            && brandy.VerifiziertText == "Verifiziert zurücknehmen" && !brandy.ZeigtAusrufe, "verifiziert: Freigeben geht");
        brandy.VerifizierenCommand.Execute(null);
        Pruefe.Wahr(!brandy.Verifiziert && !brandy.FreigebenCommand.CanExecute(null) && brandy.ZeigtAusrufe, "zweiter Klick nimmt es zurück");
        brandy.VerifizierenCommand.Execute(null);

        // Die Vormerkung lässt sich abwählen und wieder wählen; ein zweites Gerät an ein Konto hängen bietet die Zeile seit 0.7.0
        // nicht mehr an (ein PC, ein Konto).
        brandy.Vorschlaege[0].Gewaehlt = true;
        Pruefe.Gleich(null, brandy.VormerkungId, "keine Vormerkung");
        brandy.Vorschlaege[1].Gewaehlt = true;
        Pruefe.Gleich(7u, brandy.VormerkungId, "Vormerkung wieder gewählt");

        // Schritt-Bestätigung: Der Dienst fragt nach dem Code, die Karte steht oben, erst OK macht weiter.
        d.BrauchtCode = true;
        var freigeben = brandy.FreigebenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Abfrage.Offen && vm.Abfrage.Frage!.StartsWith("Gerät freigeben", StringComparison.Ordinal), "Code-Karte: " + vm.Abfrage.Frage);
        Pruefe.Falsch(freigeben.IsCompleted, "wartet auf den Code");
        vm.Abfrage.Eingabe = "12a456";
        Pruefe.Falsch(vm.Abfrage.OkCommand.CanExecute(null), "nur Ziffern");
        vm.Abfrage.Eingabe = "123 456";
        vm.Abfrage.OkCommand.Execute(null);
        await freigeben;
        Pruefe.Falsch(vm.Abfrage.Offen, "Karte wieder zu");
        Pruefe.Wahr(d.Aufrufe.Contains("Code 123456") && d.Aufrufe.Contains("Entscheiden 101 Freigeben v7 k-"), string.Join(" | ", d.Aufrufe));
        Pruefe.Wahr(vm.Antraege.Hinweis == "Brandy ist freigegeben." && vm.AntraegeTitel == "Anträge (1)", "Zeile weg, Zahl mit: " + vm.AntraegeTitel);

        // Abbrechen: nichts geändert, der Grund steht an der Zeile.
        d.BrauchtCode = true;
        var ablehnen = falke.AblehnenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Abfrage.Frage!.StartsWith("Antrag ablehnen", StringComparison.Ordinal), "fragt fürs Ablehnen");
        vm.Abfrage.AbbrechenCommand.Execute(null);
        await ablehnen;
        Pruefe.Wahr(falke.Fehler!.Contains("Abgebrochen", StringComparison.Ordinal) && vm.Antraege.Antraege.Contains(falke), "abgebrochen, Antrag bleibt");
        Pruefe.Falsch(d.Aufrufe.Any(a => a.StartsWith("Entscheiden 102", StringComparison.Ordinal)), "nichts entschieden");

        // Die Flut: alle ohne passende Vormerkung ablehnen (Code noch frisch).
        d.BrauchtCode = false;
        await vm.Antraege.UnpassendeAblehnenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Antraege.Leer && vm.Antraege.Hinweis!.StartsWith("Ein Antrag", StringComparison.Ordinal), "abgelehnt: " + vm.Antraege.Hinweis);
    }

    /// <summary>
    /// Seit 0.7.0 fragen Ablehnen und "Alle ablehnen" erst nach, und die Rückfrage sagt, was geschieht: Der Server löscht den
    /// Antrag vollständig, gesperrt wird nichts (Sarevoc, 04.10.2026: Löschen heißt vollständig entfernen; keine Fehlbedienung darf
    /// jemanden aussperren). Bis 0.6.x sperrte Ablehnen die Gerätekennung auf Dauer und hatte so ein Testgerät ausgesperrt.
    /// </summary>
    [Test]
    public static async Task Verwaltung_Ablehnen_ErstNachRueckfrage()
    {
        var d = new VerwaltungsdienstAttrappe();
        using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var falke = vm.Antraege.Antraege.Single(z => z.ScName == "Falke");
        falke.AblehnenFragenOeffnenCommand.Execute(null);
        Pruefe.Wahr(falke.AblehnenFragen && falke.AblehnenFrage.Contains("löscht ihn vollständig", StringComparison.Ordinal)
            && falke.AblehnenFrage.Contains("Gesperrt wird nichts", StringComparison.Ordinal), falke.AblehnenFrage);
        Pruefe.Falsch(d.Aufrufe.Any(a => a.StartsWith("Entscheiden", StringComparison.Ordinal)), "die Rückfrage allein schickt nichts");
        falke.AblehnenAbbrechenCommand.Execute(null);
        Pruefe.Wahr(!falke.AblehnenFragen && vm.Antraege.Antraege.Contains(falke), "abgebrochen: der Antrag bleibt");
        falke.AblehnenFragenOeffnenCommand.Execute(null);
        await falke.AblehnenCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe.Contains("Entscheiden 102 Ablehnen v- k-") && !vm.Antraege.Antraege.Contains(falke) && !falke.AblehnenFragen,
            string.Join(" | ", d.Aufrufe));
        Pruefe.Wahr(vm.Antraege.Hinweis!.Contains("abgelehnt und gelöscht", StringComparison.Ordinal)
            && vm.Antraege.Hinweis.Contains("neuen Antrag", StringComparison.Ordinal), "sagt, dass der PC neu beantragen kann: " + vm.Antraege.Hinweis);

        vm.Antraege.UnpassendeAblehnenFragenCommand.Execute(null);
        Pruefe.Wahr(vm.Antraege.UnpassendeFragen && AntraegeViewModel.UnpassendeFrage.Contains("löscht sie vollständig", StringComparison.Ordinal)
            && AntraegeViewModel.UnpassendeFrage.Contains("Gesperrt wird nichts", StringComparison.Ordinal), "Rückfrage vor dem Sammel-Ablehnen");
        vm.Antraege.UnpassendeAblehnenAbbrechenCommand.Execute(null);
        Pruefe.Falsch(vm.Antraege.UnpassendeFragen || d.Aufrufe.Any(a => a.StartsWith("Unpassende", StringComparison.Ordinal)), "abgebrochen: nichts abgelehnt");
    }

    /// <summary>Ein wartendes Gerät des bestehenden Kontos Nordlicht (per Einmalcode verknüpft oder nach langer Ruhe wieder wartend).</summary>
    internal static GeraetEintrag WeiteresGeraet(uint id, Markierung? markierung) => new()
    {
        Id = id, KontoId = 4, KontoName = "Nordlicht", Benutzername = "-", Name = "Laptop", Anzeige = "5CD2-52FB-0CE8-9324", Fingerabdruck = "5cd252fb0ce89324",
        SchluesselArt = SchluesselArt.Software, Status = GeraeteStatus.Wartet, Verknuepft = VerwaltungsdienstAttrappe.Abend.AddMinutes(-2).ToUnixTimeMilliseconds(),
        ScName = "Nordlicht", DiscordName = "Nordlicht/Anna", Markierung = markierung,
    };

    /// <summary>
    /// Gegenprüfung: Ein wartendes Gerät an einem bestehenden Konto ist kein neuer Antrag. Freigeben gibt ihm das Konto samt
    /// Rollen; ein gekapertes Discord-Konto übernähme so ein Funkkonto (ANMELDUNG-SICHERHEIT, Regeln für Admins beim Freigeben).
    /// Die Zeile sagt das, zeigt die Markierung des freigegebenen Geräts (3.5), und ein EINSPRUCH stoppt die Freigabe.
    /// </summary>
    [Test]
    public static void Verwaltung_Antraege_WeiteresGeraetEinesKontos()
    {
        var d = new VerwaltungsdienstAttrappe();
        d.Antraege.Add(WeiteresGeraet(104, Markierung.Einspruch));
        d.Antraege.Add(WeiteresGeraet(105, Markierung.Bestaetigt));
        d.Antraege.Add(WeiteresGeraet(106, null));
        using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var einspruch = vm.Antraege.Antraege.Single(z => z.GeraetId == 104);
        var bestaetigt = vm.Antraege.Antraege.Single(z => z.GeraetId == 105);
        var offen = vm.Antraege.Antraege.Single(z => z.GeraetId == 106);
        foreach (var z in new[] { einspruch, bestaetigt, offen }) z.VerifizierenCommand.Execute(null);
        Pruefe.Wahr(einspruch.Verifiziert && !einspruch.FreigebenCommand.CanExecute(null), "EINSPRUCH: trotz Verifiziert kein Freigeben");
        Pruefe.Wahr(einspruch.FreigabeGesperrt!.Contains("EINSPRUCH", StringComparison.Ordinal), "Grund sichtbar: " + einspruch.FreigabeGesperrt);
        Pruefe.Wahr(einspruch.AblehnenCommand.CanExecute(null), "Ablehnen geht");
        Pruefe.Wahr(bestaetigt.FreigebenCommand.CanExecute(null) && offen.FreigebenCommand.CanExecute(null), "Gegenprobe: bestätigt oder ohne Markierung geht Freigeben");

        // Die Zeile sagt, dass es kein neuer Antrag ist, und was das Konto markiert hat.
        Pruefe.Wahr(einspruch.WeiteresGeraet!.Contains("bestehende Konto Nordlicht", StringComparison.Ordinal) && einspruch.WeiteresGeraet.Contains("samt Rollen", StringComparison.Ordinal)
            && einspruch.WeiteresGeraet.Contains("EINSPRUCH", StringComparison.Ordinal), einspruch.WeiteresGeraet);
        Pruefe.Wahr(bestaetigt.WeiteresGeraet!.EndsWith("Gerät des Kontos bestätigt.", StringComparison.Ordinal), bestaetigt.WeiteresGeraet);
        Pruefe.Wahr(offen.WeiteresGeraet!.Contains("im Sprachkanal bestätigt", StringComparison.Ordinal), offen.WeiteresGeraet);
        Pruefe.Wahr(vm.Antraege.Antraege.Where(z => z.IstAntrag).All(z => z.WeiteresGeraet is null), "echte Anträge ohne diese Warnung");

        // Freigeben eines weiteren Geräts: ohne Vormerkung und ohne fremdes Konto, es hat seins schon (der Server lehnte beides ab).
        bestaetigt.FreigebenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Pruefe.Wahr(d.Aufrufe.Contains("Entscheiden 105 Freigeben v- k-") && bestaetigt.Fehler is null, string.Join(" | ", d.Aufrufe));
    }

    /// <summary>
    /// Konto löschen (seit 0.6.3, Sarevoc 04.10.2026): auf der Karte eines Funkers oder der Führung, erst eine Rückfrage, dann der
    /// frische Code; Abbrechen ändert nichts. Bei ADMIN-Konten und dem Eigentümer gibt es den Knopf nicht, nur den Hinweis auf
    /// die Konsole; offene Anträge (Ablehnen unter Anträge) und Vormerkungen (eigener Knopf) löscht man woanders.
    /// </summary>
    [Test]
    public static async Task Verwaltung_KontoLoeschen_MitRueckfrageUndCode()
    {
        var d = new VerwaltungsdienstAttrappe();
        using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var m = vm.Mitglieder;

        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 1));
        Pruefe.Wahr(!m.Karte!.KannLoeschen && !m.Karte.ZeigtLoeschenKnopf && m.Karte.LoeschenNurKonsole!.Contains("Serverkonsole", StringComparison.Ordinal),
            "Eigentümer: nur die Konsole");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 4));
        Pruefe.Wahr(!m.Karte!.KannLoeschen && m.Karte.LoeschenNurKonsole is not null, "Admin Nordlicht: nur die Konsole");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 21));
        Pruefe.Wahr(!m.Karte!.KannLoeschen && m.Karte.LoeschenNurKonsole is null, "offener Antrag: nicht hier");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.VormerkungId == 7));
        Pruefe.Falsch(m.Karte!.KannLoeschen, "Vormerkung hat ihren eigenen Knopf");

        // Funker Kobold: Knopf, Rückfrage, Abbrechen schickt nichts.
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 3));
        var k = m.Karte!;
        Pruefe.Wahr(k.KannLoeschen && k.ZeigtLoeschenKnopf && !k.LoeschenFragen && k.LoeschenNurKonsole is null, "Funker: Knopf da");
        k.KontoLoeschenFragenCommand.Execute(null);
        Pruefe.Wahr(k.LoeschenFragen && !k.ZeigtLoeschenKnopf && k.LoeschenFrage.StartsWith("Konto Kobold endgültig löschen?", StringComparison.Ordinal), k.LoeschenFrage);
        k.KontoLoeschenAbbrechenCommand.Execute(null);
        Pruefe.Wahr(!k.LoeschenFragen && k.ZeigtLoeschenKnopf && !d.Aufrufe.Any(a => a.StartsWith("Konto löschen", StringComparison.Ordinal)), "abgebrochen: nichts geschickt");

        // Die Code-Abfrage abgebrochen: Das Konto bleibt, der Grund steht an der Karte.
        d.BrauchtCode = true;
        k.KontoLoeschenFragenCommand.Execute(null);
        var loeschen = k.KontoLoeschenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Abfrage.Offen && vm.Abfrage.Frage!.StartsWith("Konto löschen", StringComparison.Ordinal), "fragt den Code: " + vm.Abfrage.Frage);
        vm.Abfrage.AbbrechenCommand.Execute(null);
        await loeschen;
        Pruefe.Wahr(k.Fehler!.Contains("Abgebrochen", StringComparison.Ordinal) && d.Mitglieder.Any(x => x.KontoId == 3), "Code abgebrochen: Konto bleibt");

        // Mit Code: gelöscht, Karte zu, Hinweis, aus der Liste verschwunden.
        k.KontoLoeschenFragenCommand.Execute(null);
        loeschen = k.KontoLoeschenCommand.ExecuteAsync(null);
        vm.Abfrage.Eingabe = VerwaltungsdienstAttrappe.RichtigerCode;
        vm.Abfrage.OkCommand.Execute(null);
        await loeschen;
        Pruefe.Wahr(d.Aufrufe.Contains("Konto löschen 3") && !d.Mitglieder.Any(x => x.KontoId == 3) && m.Karte is null
            && m.Hinweis!.StartsWith("Konto Kobold gelöscht.", StringComparison.Ordinal), string.Join(" | ", d.Aufrufe) + " / " + m.Hinweis);
        Pruefe.Falsch(m.Sichtbar.Any(z => z.Eintrag.KontoId == 3), "aus der Liste verschwunden");
    }

    /// <summary>
    /// Sperre aufheben (seit 0.8.0): Die Karte eines Kontos zeigt seine Geräte; ein gesperrtes bekommt "Sperre aufheben ...", erst die
    /// Rückfrage, dann der frische Code, danach ist es entfernt. Bei Admin-Konten steht nur der Hinweis auf die Konsole, an einem Server
    /// vor 0.8.0 der Hinweis, dass es dort noch nicht geht.
    /// </summary>
    [Test]
    public static async Task Verwaltung_SperreAufheben_MitRueckfrageUndCode()
    {
        var d = new VerwaltungsdienstAttrappe();
        using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var m = vm.Mitglieder;

        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 4));
        var nordlicht = m.Karte!.Geraete.Single();
        Pruefe.Wahr(nordlicht.Gesperrt && !nordlicht.KannAufheben && !nordlicht.ZeigtAufhebenKnopf
            && nordlicht.NurKonsole!.Contains("funk-admin sperre-aufheben N0RD-5C2E-77A9-1B4F", StringComparison.Ordinal), "Admin-Konto: nur die Konsole");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 5));
        Pruefe.Wahr(m.Karte!.Geraete.Single().Zustand == "entfernt" && !m.Karte.Geraete[0].KannAufheben && m.Karte.Geraete[0].NurKonsole is null,
            "entfernt: nichts aufzuheben");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.VormerkungId == 7));
        Pruefe.Wahr(!m.Karte!.ZeigtGeraete && m.Karte.Geraete.Count == 0, "Vormerkung: keine Geräte");

        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 2));
        var k = m.Karte!;
        Pruefe.Wahr(k.ZeigtGeraete && k.Geraete.Count == 2 && k.GeraeteHinweis is null && d.Aufrufe.Contains("Geräte 2"), "Falke: zwei Geräte, beim Öffnen geladen");
        var frei = k.Geraete.Single(g => g.Id == 201);
        var gesperrt = k.Geraete.Single(g => g.Id == 202);
        Pruefe.Wahr(!frei.Gesperrt && frei.Zustand == "freigegeben" && !frei.ZeigtAufhebenKnopf && frei.NurKonsole is null, "freigegeben: kein Knopf");
        Pruefe.Wahr(gesperrt.Gesperrt && gesperrt.Zustand == "gesperrt" && gesperrt.ZeigtAufhebenKnopf && gesperrt.NurKonsole is null, "gesperrt: Knopf da");

        gesperrt.SperreAufhebenFragenCommand.Execute(null);
        Pruefe.Wahr(gesperrt.AufhebenFragen && !gesperrt.ZeigtAufhebenKnopf
            && gesperrt.AufhebenFrage.StartsWith("Sperre von PC-7Z3Q (7Z3Q-K8M2-4F1D-B6A0) aufheben?", StringComparison.Ordinal), gesperrt.AufhebenFrage);
        gesperrt.SperreAufhebenAbbrechenCommand.Execute(null);
        Pruefe.Wahr(!gesperrt.AufhebenFragen && gesperrt.ZeigtAufhebenKnopf && !d.Aufrufe.Any(a => a.StartsWith("Sperre aufheben", StringComparison.Ordinal)),
            "abgebrochen: nichts geschickt");

        // Mit Code: aufgehoben, das Gerät steht danach als entfernt da, und der Hinweis sagt, wie es weitergeht.
        d.BrauchtCode = true;
        gesperrt.SperreAufhebenFragenCommand.Execute(null);
        var aufheben = gesperrt.SperreAufhebenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Abfrage.Offen && vm.Abfrage.Frage!.StartsWith("Sperre aufheben", StringComparison.Ordinal), "fragt den Code: " + vm.Abfrage.Frage);
        vm.Abfrage.Eingabe = VerwaltungsdienstAttrappe.RichtigerCode;
        vm.Abfrage.OkCommand.Execute(null);
        await aufheben;
        Pruefe.Wahr(d.Aufrufe.Contains("Sperre aufheben 202") && k.Fehler is null && !gesperrt.AufhebenFragen, string.Join(" | ", d.Aufrufe));
        Pruefe.Gleich("entfernt", k.Geraete.Single(g => g.Id == 202).Zustand, "danach entfernt, nicht freigegeben");
        Pruefe.Wahr(m.Hinweis!.Contains("neuen Antrag", StringComparison.Ordinal), m.Hinweis);

        // Ein Server vor 0.8.0 kennt es nicht: kein Knopf, nur der Hinweis.
        d.Geraete[1] = d.Geraete[1] with { Status = GeraeteStatus.Entzogen };
        d.LageSetzen(l => l with { ServerFassung = "0.7.0" });
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 2));
        var alt = m.Karte!.Geraete.Single(g => g.Id == 202);
        Pruefe.Wahr(!alt.KannAufheben && alt.NurKonsole == Verwaltungstexte.SperreAufhebenZuAlt, "Server 0.7.0: " + alt.NurKonsole);
    }

    [Test]
    public static async Task Verwaltung_MitgliederUndKatalog()
    {
        var d = new VerwaltungsdienstAttrappe();
        using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var m = vm.Mitglieder;

        // Suche über alle Namen, auch Teile; ein Kürzel der Untergilde gibt es seit 0.11.8 nicht mehr.
        m.Suche = "max";
        Pruefe.Wahr(m.Sichtbar.Count == 1 && m.Sichtbar[0].Name == "Mustermann" && m.Sichtbar[0].Stufe == "Admin, Eigentümer", "max findet Mustermann/Max");
        m.Suche = "bs";
        Pruefe.Gleich(0, m.Sichtbar.Count, "Kürzel BS findet niemanden mehr");
        m.Suche = "Zwei";
        Pruefe.Wahr(m.Sichtbar.Single().Weitere == "Nordlicht Zwei", "weitere SC-Namen durchsuchbar");
        m.Suche = "";
        Pruefe.Wahr(m.Sichtbar[0].Zustand == "online" && m.Sichtbar[^1].Zustand == "Antrag", "online zuerst, Anträge zuletzt");

        // Karte einer Vormerkung: Namen änderbar, Rollen aus dem Katalog vorbelegt.
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.VormerkungId == 7));
        var k = m.Karte!;
        Pruefe.Wahr(k.IstVormerkung && k.NamenAenderbar && k.Rollen.Count == 6 && k.Rollen.Single(r => r.Gewaehlt).Wert == 4,
            "Vormerkung Brandy: Marine");
        Pruefe.Wahr(typeof(MitgliedKarte).GetProperty("Untergilde") is null && typeof(MitgliedKarte).GetProperty("Untergilden") is null, "keine Auswahl der Untergilde");
        k.Rollen[0].Gewaehlt = true;
        k.Notiz = "Pilot und Marine";
        await k.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe.Contains("Vormerkung 7 Brandy Brandy/Lea [1,4] Pilot und Marine") && m.Karte is null, string.Join(" | ", d.Aufrufe));

        // Karte eines Kontos: SC-Namen ändert das Mitglied selbst, den Discord-Namen nur der Admin (A53); Wünsche stehen dabei.
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 4));
        Pruefe.Wahr(!m.Karte!.NamenAenderbar && m.Karte.DiscordAenderbar && m.Karte.DiscordName == "Nordlicht/Anna"
            && m.Karte.Wuensche == "Medium Fighter Pilot, Medium Gunner (erste Wahl: Medium Fighter Pilot)" && m.Karte.Angaben!.Contains("Nordlicht/Anna", StringComparison.Ordinal),
            "Konto Nordlicht: " + m.Karte.Wuensche);
        await m.Karte.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Falsch(d.Aufrufe.Any(x => x.StartsWith("Discord ", StringComparison.Ordinal)), "unverändert: kein Aufruf für den Discord-Namen");
        m.OeffnenCommand.Execute(m.Sichtbar.Single(z => z.Eintrag.KontoId == 2));
        m.Karte!.DiscordName = "ＫＯＢＯＬＤ";
        await m.Karte.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(m.Karte is { Fehler: "Den Discord-Namen ＫＯＢＯＬＤ hat schon jemand." } && !d.Aufrufe.Any(x => x.StartsWith("Konto 2 ", StringComparison.Ordinal)),
            "vergeben: Klartext, und die übrigen Angaben gehen nicht halb hinaus: " + m.Karte?.Fehler);
        m.Karte!.DiscordName = " Falke/Thomas ";
        await m.Karte.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe.Contains("Discord 2 Falke/Thomas") && d.Aufrufe.Contains("Konto 2 [1] ") && m.Karte is null && m.Hinweis == "Gespeichert: Falke",
            string.Join(" | ", d.Aufrufe));

        // Vormerken: SC-Name nach den Regeln des Servers.
        m.VormerkenCommand.Execute(null);
        m.Karte!.ScName = "X";
        await m.Karte.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(m.Karte.Fehler!.Contains("2 bis 32", StringComparison.Ordinal), "zu kurzer SC-Name");
        m.Karte.ScName = " Mondlicht ";
        m.Karte.DiscordName = "Mondlicht/Eva";
        await m.Karte.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe.Contains("Vormerken Mondlicht Mondlicht/Eva [] ") && m.Hinweis == "Vorgemerkt: Mondlicht", "Hinweis: " + m.Hinweis);

        // Liste einfügen: alles oder nichts, Fehler je Zeile. Spalte 3 (Untergilde) ist seit 0.11.8 kein Fehler mehr, sie wird übergangen.
        m.EinfuegenText = "";
        Pruefe.Wahr(m.EinfuegenVorschau.Contains("SC-Name; Discord-Name; ; Rolle, Rolle", StringComparison.Ordinal) && m.EinfuegenVorschau.Contains("dritte Spalte bleibt leer", StringComparison.Ordinal)
            && !m.EinfuegenVorschau.Contains("Kürzel", StringComparison.Ordinal), m.EinfuegenVorschau);
        m.EinfuegenText = "Sturm; Sturm/Kai; BS; Marine\r\n\r\nWind; ; XY\nzu; viele; teile; a; b\n";
        Pruefe.Wahr(m.EinfuegenVorschau.StartsWith("3 Zeilen", StringComparison.Ordinal), m.EinfuegenVorschau);
        await m.EinfuegenCommand.ExecuteAsync(null);
        Pruefe.Wahr(m.EinfuegenFehler.SequenceEqual(["Zeile 3: mehr als vier Teile"]) && m.Fehler!.StartsWith("Nichts angelegt", StringComparison.Ordinal),
            string.Join(" | ", m.EinfuegenFehler));
        m.EinfuegenText = "Sturm; Sturm/Kai; BS; Marine\nWind; ; XY";
        await m.EinfuegenCommand.ExecuteAsync(null);
        Pruefe.Gleich("2 Mitglieder vorgemerkt. Spalte 3 (Untergilde) wird seit 0.11.8 übergangen.", m.Hinweis, "angelegt, mit Hinweis einmal je Liste");
        Pruefe.Wahr(m.EinfuegenFehler.Count == 0 && m.EinfuegenText.Length == 0, "keine Fehler, Feld geleert");
        m.EinfuegenText = "Sturm; Sturm/Kai;; Marine";
        await m.EinfuegenCommand.ExecuteAsync(null);
        Pruefe.Gleich("1 Mitglieder vorgemerkt.", m.Hinweis, "Spalte 3 leer: kein Hinweis");

        // Rollenkatalog: doppelte Namen nicht, Wegfallen mit Warnung, gespeichert mit Version.
        var kat = vm.Katalog;
        kat.NeuName = "medium fighter PILOT";
        kat.HinzufuegenCommand.Execute(null);
        Pruefe.Wahr(kat.Fehler!.Contains("gibt es schon", StringComparison.Ordinal) && !kat.Geaendert, "gleiche Rolle anders geschrieben");
        kat.NeuName = "Scout";
        kat.NeuGruppe = "Flug";
        kat.HinzufuegenCommand.Execute(null);
        Pruefe.Wahr(kat.Geaendert && kat.Warnung is null && kat.Eintraege[^1].Id == 7, "Scout dazu, neue Nummer");
        kat.Eintraege.Single(e => e.Name == "Logistik").EntfernenCommand.Execute(null);
        Pruefe.Wahr(kat.Warnung!.Contains("eine Rolle", StringComparison.Ordinal), "Warnung vor dem Wegfallen");
        await kat.SpeichernCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe.Contains("Katalog setzen 6 v3") && d.Katalog.Version == 4 && kat.Hinweis == "Rollenkatalog gespeichert.", "gespeichert");
        kat.BeispieleDazuCommand.Execute(null);
        Pruefe.Falsch(kat.Geaendert, "die Beispiele stehen schon alle drin");

    }

    /// <summary>
    /// Seit 0.11.8 (Sarevoc: "Untergilden entfallen ganz, überall"): Die Verwaltung hat keine Seite Untergilden mehr, weder in der
    /// Navigation links noch oben (das prüfen die gezeichneten Schritte in <see cref="Pruefen"/>), und kein Modell dafür.
    /// </summary>
    [Test]
    public static void Verwaltung_OhneSeiteUntergilden()
    {
        Pruefe.Falsch(Enum.GetNames<Verwaltungsbereich>().Any(n => n.Contains("Untergild", StringComparison.Ordinal)), "kein Bereich Untergilden");
        Pruefe.Falsch(typeof(VerwaltungViewModel).GetProperties().Any(p => p.Name.Contains("Untergild", StringComparison.Ordinal)), "keine Eigenschaft für Untergilden");
        Pruefe.Wahr(typeof(VerwaltungViewModel).Assembly.GetTypes().All(t => !t.Name.Contains("Untergild", StringComparison.Ordinal)), "kein Typ für Untergilden im Verwaltungsteil");
        Pruefe.Falsch(typeof(IVerwaltungsdienst).GetMethods().Any(x => x.Name.Contains("Untergild", StringComparison.Ordinal)), "der Dienst fragt 0x001D nicht mehr");
    }

    /// <summary>
    /// Telefon verloren (Befund der Gegenprüfung Stufe 1): Die Schritt-Bestätigung nimmt auch einen Notfallcode, wie der Server
    /// bei AdminBestaetigen; sonst müsste der Admin die Verwaltung schließen und sich für jede heikle Handlung neu anmelden.
    /// </summary>
    [Test]
    public static async Task Verwaltung_SchrittBestaetigungNimmtNotfallcode()
    {
        var abfrage = new CodeAbfrage();
        var frage = abfrage.FragenAsync("Gerät freigeben", CancellationToken.None);
        Pruefe.Wahr(abfrage.Frage!.Contains("Notfallcode", StringComparison.Ordinal), "die Karte nennt den Notfallcode: " + abfrage.Frage);
        abfrage.Eingabe = "12345";
        Pruefe.Falsch(abfrage.OkCommand.CanExecute(null), "fünf Ziffern sind keins von beiden");
        abfrage.Eingabe = "abcd efgh jkmn";
        Pruefe.Wahr(abfrage.OkCommand.CanExecute(null), "zwölf Zeichen, auch klein und mit Leerzeichen");
        abfrage.OkCommand.Execute(null);
        Pruefe.Gleich("ABCD-EFGH-JKMN", await frage, "als Notfallcode in Anzeigeform");
        var zweite = abfrage.FragenAsync("Antrag ablehnen", CancellationToken.None);
        abfrage.Eingabe = "123 456";
        abfrage.OkCommand.Execute(null);
        Pruefe.Gleich("123456", await zweite, "sechs Ziffern bleiben ein Code aus der App");
        Pruefe.Gleich((null, "ABCD-EFGH-JKMN"), Adminzugang.Bestaetigungsart("ABCD-EFGH-JKMN"), "geht als Notfallcode an AdminBestaetigen");
        Pruefe.Gleich(("123456", null), Adminzugang.Bestaetigungsart("123456"), "geht als Code an AdminBestaetigen");
    }

    /// <summary>
    /// Das TOTP-Geheimnis (Text und QR-Code) steht nur, solange der Bereich Verwaltung offen ist (Gegenprüfung Stufe 1, Vorlage
    /// "löscht es, sobald das Fenster zugeht"); beim Wechsel in einen anderen Bereich ist es fort, und der Bereich sagt warum.
    /// </summary>
    [Test]
    public static async Task Verwaltung_GeheimnisFortBeimVerlassenDesBereichs()
    {
        var d = new VerwaltungsdienstAttrappe();
        d.LageSetzen(_ => new Verwaltungslage { Verbunden = true, Name = "Nordlicht", Admin = AdminStatus.Einrichtung, SchluesselVorhanden = true });
        var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        using var teil = new Verwaltungsteil(d, vm);
        using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Verwaltung = teil };
        var h = a.Haupt(out _, out _, out _, out _);
        a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = AdminStatus.Aktiv } });
        h.AktiverBereich = h.Bereiche.Single(b => b.Kennung == HauptViewModel.VerwaltungKennung);
        await vm.GeheimnisAbholenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.HatGeheimnis && vm.QrBildGeometrie is not null, "Geheimnis und QR-Code da");
        h.AktiverBereich = h.Bereiche[0];
        Pruefe.Falsch(vm.HatGeheimnis || vm.QrBildGeometrie is not null || vm.GeheimnisBis is not null, "beim Verlassen des Bereichs fort");
        Pruefe.Wahr(vm.Hinweis is { } hinweis && hinweis.Contains("admin-einrichtung-neu", StringComparison.Ordinal), "sagt, wie es weitergeht: " + vm.Hinweis);
        Pruefe.Wahr(vm.NurErsterCode, "steht es schon in der App, geht der erste Code trotzdem einzugeben");
        vm.EinrichtungCode = VerwaltungsdienstAttrappe.RichtigerCode;
        await vm.EinrichtungBestaetigenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.HatNotfallcodes && !vm.NurErsterCode, "eingerichtet, auch ohne das Geheimnis noch einmal zu sehen");
        h.AktiverBereich = h.Bereiche.Single(b => b.Kennung == HauptViewModel.VerwaltungKennung);
        Pruefe.Falsch(vm.HatGeheimnis, "kommt beim Zurückkehren nicht wieder");
    }

    /// <summary>
    /// Sarevocs Fall vom 01.10.2026: Ein Antrag mit den Namen eines bestehenden Kontos wurde als neues Konto freigegeben. Die Zeile nennt
    /// das passende Konto; seit 0.7.0 (ein PC, ein Konto) nur als Hinweis, ohne Auswahl zum Anhängen. Für ein ADMIN-Konto steht der
    /// Befehl der Konsole da, die den PC eines Admins mit neuem Schlüssel weiter an sein Konto hängt.
    /// </summary>
    [Test]
    public static void Verwaltung_Antraege_BietenPassendesBestehendesKontoAn()
    {
        var d = new VerwaltungsdienstAttrappe();
        d.Antraege.Add(d.Antraege[0] with
        {
            Id = 107, KontoId = 23, KontoName = "Mustermann", Anzeige = "D218-2263-AA26-72D8", Fingerabdruck = "d2182263aa2672d8", ScName = "Mustermann",
            DiscordName = "Mustermann/Max", Vormerkungen = null,
        });
        using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        var falke = vm.Antraege.Antraege.Single(z => z.GeraetId == 102);
        Pruefe.Wahr(falke.BestehendesKonto is { } f && f.Contains("Falke", StringComparison.Ordinal) && f.Contains("eigenes Konto", StringComparison.Ordinal)
            && !f.Contains("--an-konto", StringComparison.Ordinal), "Falke: weiterer PC, eigenes Konto: " + falke.BestehendesKonto);
        var sarevoc = vm.Antraege.Antraege.Single(z => z.GeraetId == 107);
        Pruefe.Wahr(sarevoc.BestehendesKonto is { } s && s.Contains("Test", StringComparison.Ordinal) && s.Contains("ADMIN", StringComparison.Ordinal)
            && s.Contains("funk-admin freigeben D218-2263-AA26-72D8 --an-konto 1", StringComparison.Ordinal), "ADMIN-Konto: Befehl der Konsole: " + sarevoc.BestehendesKonto);
        // Gegenprüfung: Die Namen wählt der Antragsteller selbst; vor dem Anhängen die Kennung mit dem Gerät vergleichen.
        Pruefe.Wahr(sarevoc.BestehendesKonto!.Contains("dieselbe Kennung", StringComparison.Ordinal), "Hinweis verlangt den Kennungsvergleich");
        var brandy = vm.Antraege.Antraege.Single(z => z.GeraetId == 101);
        Pruefe.Gleich(null, brandy.BestehendesKonto, "Brandy: nur der Antrag selbst und eine Vormerkung, kein Konto");
    }

    private static readonly string[] Schritte = ["einrichten", "zweitfaktor", "notfallcodes", "anmelden", "antraege", "bestaetigen", "mitglieder", "katalog"];

    /// <summary>
    /// Die Schritte der Verwaltung im Hauptfenster gezeichnet, wie mit dem Admin-Setup: Der Bereich steht in der Navigation nur,
    /// weil das Konto Admin ist; die Ansicht kommt aus dem Verwaltungsteil (Stile aus den Ressourcen des Clients).
    /// </summary>
    [Test]
    public static void Verwaltung_Bilder_KontrastUndBindungen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                foreach (var schritt in Schritte)
                {
                    var d = new VerwaltungsdienstAttrappe();
                    var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
                    using var teil = new Verwaltungsteil(d, vm);
                    Vorbereiten(d, vm, schritt);
                    using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Verwaltung = teil };
                    a.Ablage.Aktuell.Audio.Hoerweise = Funk.Client.Ablage.Hoerweise.Headset;
                    var h = a.Haupt(out _, out _, out _, out _);
                    Pruefe.Falsch(h.Bereiche.Any(b => b.Kennung == HauptViewModel.VerwaltungKennung), "ohne Admin-Zustand keine Verwaltung in der Navigation");
                    a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = AdminStatus.Aktiv } });
                    var bereich = h.Bereiche.Single(b => b.Kennung == HauptViewModel.VerwaltungKennung);
                    h.AktiverBereich = bereich;
                    var haupt = new Hauptfenster(h, a.Ablage);
                    fenster.Add(haupt);
                    var bild = Ganz(haupt, 1280);
                    var was = satz.Name + ", " + schritt;
                    var ansicht = Bildlauf.Nachfahren<VerwaltungAnsicht>(Bildlauf.Wurzel(haupt)).Single();
                    Border Rand(string name) => (Border)ansicht.FindName(name);
                    Pruefen(ansicht, vm, schritt, was, Rand);
                    KontrastPruefen(ansicht, satz, was, mindestens: 6);
                    if (satz.Stellung == Farbstellung.Anthrazit) Bildablage.Ablegen(bild, "verwaltung-" + schritt);
                }
            }
        });
    }

    private static void Vorbereiten(VerwaltungsdienstAttrappe d, VerwaltungViewModel vm, string schritt)
    {
        switch (schritt)
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
            case "antraege":
                // Dazu ein weiteres Gerät eines bestehenden Kontos mit EINSPRUCH (Gegenprüfung Stufe 1).
                d.Antraege.Add(WeiteresGeraet(104, Markierung.Einspruch));
                vm.Antraege.LadenAsync().GetAwaiter().GetResult();
                // Brandy verifiziert, Falke noch nicht (Grund unter den Knöpfen), dazu der EINSPRUCH.
                vm.Antraege.Antraege.Single(z => z.GeraetId == 101).VerifizierenCommand.Execute(null);
                vm.Antraege.Antraege.Single(z => z.GeraetId == 104).VerifizierenCommand.Execute(null);
                break;
            case "bestaetigen":
                vm.Antraege.Antraege[0].VerifizierenCommand.Execute(null);
                d.BrauchtCode = true;
                _ = vm.Antraege.Antraege[0].FreigebenCommand.ExecuteAsync(null);
                vm.Abfrage.Eingabe = "123 4";
                break;
            case "mitglieder":
                vm.Bereich = Verwaltungsbereich.Mitglieder;
                vm.Mitglieder.OeffnenCommand.Execute(vm.Mitglieder.Sichtbar.Single(z => z.Eintrag.VormerkungId == 7));
                vm.Mitglieder.EinfuegenText = "Sturm; Sturm/Kai; ; Marine\nWind; ; ; ; zu viel";
                vm.Mitglieder.EinfuegenCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                break;
            default:
                vm.Bereich = Verwaltungsbereich.Katalog;
                vm.Katalog.NeuName = "Scout";
                vm.Katalog.NeuGruppe = "Flug";
                vm.Katalog.HinzufuegenCommand.Execute(null);
                vm.Katalog.Eintraege.Single(e => e.Name == "Logistik").EntfernenCommand.Execute(null);
                break;
        }
    }

    private static void Pruefen(VerwaltungAnsicht ansicht, VerwaltungViewModel vm, string schritt, string was, Func<string, Border> rand)
    {
        bool Zu(string name) => ZuSehen(rand(name));
        // Seit 0.11.8 ohne Seite Untergilden, in der Navigation links und oben (beide stehen im Baum, eine davon eingeklappt).
        Pruefe.Falsch(Bildlauf.Nachfahren<RadioButton>(ansicht).Any(r => (r.Content as string)?.Contains("Untergild", StringComparison.Ordinal) == true),
            was + ": kein Eintrag Untergilden in der Navigation");
        switch (schritt)
        {
            case "einrichten":
                Pruefe.Wahr(Zu("EinrichtenKarte") && Sichtbar(ansicht, VerwaltungsdienstAttrappe.Kennung) && !Zu("AnmeldenKarte"), was + ": Kennung zum Vorlesen");
                // Seit 0.4.1 die Vorprüfung statt der alten Pflichtzeile für Admin-PCs; die Attrappe meldet TPM 2.0.
                Pruefe.Wahr(Sichtbar(ansicht, "TPM 2.0 gefunden."), was + ": Vorprüfung");
                Pruefe.Falsch(Sichtbar(ansicht, "Pflicht für Admin-PCs"), was + ": alte Pflichtzeile fort");
                break;
            case "zweitfaktor":
                Pruefe.Wahr(Zu("ZweitfaktorKarte") && Sichtbar(ansicht, "JBSW Y3DP"), was + ": Geheimnis zum Abtippen");
                var qr = (Border)ansicht.FindName("QrRand");
                var pfad = (System.Windows.Shapes.Path)ansicht.FindName("QrPfad");
                Pruefe.Wahr(ZuSehen(qr) && pfad.ActualWidth > 0 && qr.Background is SolidColorBrush { Color: var weiss } && weiss == Colors.White, was + ": QR-Code dunkel auf weiß");
                Pruefe.Wahr(Sichtbar(ansicht, "nicht auf diesem PC"), was + ": Hinweis Telefon");
                break;
            case "notfallcodes":
                Pruefe.Wahr(Zu("NotfallKarte") && Bildlauf.Nachfahren<TextBox>(rand("NotfallKarte")).Count(t => ZuSehen(t) && t.IsReadOnly) == 10, was + ": zehn Notfallcodes");
                Pruefe.Wahr(Zu("AnmeldenKarte"), was + ": darunter schon die Anmeldung");
                break;
            case "anmelden":
                Pruefe.Wahr(Zu("AnmeldenKarte") && Sichtbar(ansicht, "passt nicht"), was + ": Anmeldung mit Fehler");
                Pruefe.Falsch(((StackPanel)ansicht.FindName("Offen")).Visibility == Visibility.Visible, was + ": ohne Sitzung keine Bereiche");
                break;
            case "antraege":
            case "bestaetigen":
                // Seit 0.6.3: verglichen und "Verifiziert" geklickt statt die Kennung abzutippen; Brandy ist verifiziert.
                Pruefe.Wahr(Sichtbar(ansicht, "Verifiziert zurücknehmen"), was + ": Brandy verifiziert");
                Pruefe.Falsch(Sichtbar(ansicht, "Kennung aus Discord") || Sichtbar(ansicht, "Kennung passt"), was + ": altes Eingabefeld fort");
                if (schritt == "antraege")
                {
                    Pruefe.Wahr(Sichtbar(ansicht, "Lass dir von Brandy die Gerätekennung durchsagen oder schicken"), was + ": woher die Vergleichszahl kommt");
                    var knoepfe = Bildlauf.Nachfahren<Button>(ansicht).Where(b => ZuSehen(b) && b.Name == "VerifiziertKnopf").ToList();
                    Pruefe.Gleich(3, knoepfe.Count, was + ": Verifiziert an jeder Karte");
                    Pruefe.Gleich(1, knoepfe.Count(b => Bildlauf.Nachfahren<TextBlock>(b).Any(t => t.Text == "Verifiziert")), was + ": Falke noch nicht verifiziert");
                    Pruefe.Wahr(Sichtbar(ansicht, "Freigeben wird aktiv, sobald du die Kennung verglichen und auf Verifiziert geklickt hast."),
                        was + ": Grund unter dem gesperrten Freigeben");
                    Pruefe.Wahr(Sichtbar(ansicht, "Freigeben geht nicht: Ein freigegebenes Gerät des Kontos hat EINSPRUCH eingelegt."), was + ": Grund beim Einspruch");
                    Pruefe.Wahr(Sichtbar(ansicht, "Falke ist schon vergeben") && Sichtbar(ansicht, "(laut Client, ungeprüft)"), was + ": vergeben, Schlüsselart");
                    Pruefe.Wahr(Sichtbar(ansicht, "Vormerkung Brandy (Discord Brandy/Lea): Marine, gleicher SC-Name"), was + ": Vorschlag");
                    Pruefe.Wahr(Sichtbar(ansicht, "Heißt ähnlich wie das Konto Falke (Falke/Tom)"), was + ": passendes bestehendes Konto (Sarevocs Fall vom 01.10.2026)");
                    Pruefe.Wahr(Sichtbar(ansicht, "weiteres Gerät für das bestehende Konto Nordlicht") && Sichtbar(ansicht, "EINSPRUCH von einem freigegebenen Gerät des Kontos"),
                        was + ": weiteres Gerät mit Einspruch");
                    Pruefe.Gleich(0, Bildlauf.Nachfahren<ComboBox>(ansicht).Count(ZuSehen), was + ": keine Kontoauswahl mehr (seit 0.7.0 ein PC, ein Konto)");
                    Pruefe.Gleich(1, Bildlauf.Nachfahren<Button>(ansicht).Count(b => ZuSehen(b) && b.Content as string == "Freigeben" && b.IsEnabled),
                        was + ": Freigeben nur bei Brandy (Falke: nicht verifiziert, Nordlicht: Einspruch)");
                }
                else
                {
                    Pruefe.Wahr(vm.Abfrage.Offen && Zu("AbfrageKarte") && Sichtbar(ansicht, "Gerät freigeben: Das braucht einen frischen Code"), was + ": Code-Karte oben");
                }
                break;
            case "mitglieder":
                Pruefe.Wahr(Zu("MitgliedKarteRand") && Sichtbar(ansicht, "Vormerkung Brandy") && Sichtbar(ansicht, "Zeile 2: mehr als vier Teile"), was + ": Karte und Fehler je Zeile");
                Pruefe.Wahr(Sichtbar(ansicht, "5 Konten, 2 vorgemerkt"), was + ": Zählung");
                Pruefe.Falsch(Sichtbar(ansicht, "Untergilde"), was + ": keine Untergilde auf der Karte (seit 0.11.8)");
                break;
            default:
                Pruefe.Wahr(Sichtbar(ansicht, "Beim Speichern fallen eine Rolle weg"), was + ": Warnung");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBox>(ansicht).Any(t => ZuSehen(t) && t.Text == "Scout"), was + ": neue Rolle");
                break;
        }
    }
}
