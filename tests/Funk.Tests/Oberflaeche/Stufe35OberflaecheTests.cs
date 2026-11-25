// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Client.Steuerelemente;
using Funk.Klang.Maschine;
using Funk.Protokoll.Steuerung;
using Glasdesign;
using static Funk.Client.Dienste.FunkDienstAttrappe;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Stufe 3, Schritt 3.5 an der Oberfläche (STUFE3-ENTWURF 5.2, 5.5, 5.6 und Abschnitt 9): Die Feinheiten je Kanal (Hören auf, Lautstärke, Pegel je
/// Person; bis 0.9.3 im Bereich "Meine Kanäle" und mit VA/Taste, seit 0.10.0 unter Einstellungen > Empfangen > Feinheiten), zweizeilig und ohne
/// abgeschnittene Namen;
/// Brett und Meldungen nennen den Kanal mit der Bezeichnung des Ch.; das Funkfeld zeigt VA/Taste je Feld auch in der Liste und
/// "(geändert)" an der Rolle, und der Ch.-Wähler bietet Global 1 bis 20 auch in einem anderen Feld an, nie Gesamt. Im Speicher gezeichnet.
/// </summary>
internal static class Stufe35OberflaecheTests
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    private static readonly Bildschirm Monitor100 = new(new Pixelrechteck(0, 0, 3840, 2160), new Pixelrechteck(0, 0, 3840, 2076), 1.0, true);

    private static FesteBildschirme Monitore { get; } = new([Monitor100]);

    /// <summary>Die schmale und die breite Seite des Hauptfensters (Einheiten).</summary>
    private static readonly double[] Breiten = [960.0, 1280.0];

    /// <summary>Strg+1 und Strg+2: Start und Einstellungen (seit 0.10.0, E2; bis 0.9.3 Strg+1 bis Strg+4 für vier Bereiche).</summary>
    private static readonly string[] BereichsTasten = ["1", "2"];

    /// <summary>Die Kanäle unter Einstellungen > Empfangen > Feinheiten (seit 0.10.0; bis 0.9.3 der Bereich Meine Kanäle).</summary>
    private static Expander Empfangen(Hauptfenster haupt) =>
        Bildlauf.Nachfahren<Expander>(Bildlauf.Wurzel(haupt)).Single(x => x.Name == "EmpfangenFeinheiten");

    private static IEnumerable<FrameworkElement> Benannt(DependencyObject wurzel, string name) =>
        Bildlauf.Nachfahren<FrameworkElement>(wurzel).Where(e => e.Name == name);

    private static Rect Lage(FrameworkElement e, Visual wurzel) => e.TransformToAncestor(wurzel).TransformBounds(new Rect(e.RenderSize));

    private static ushort K(FunkLage l, string name, byte ch) => Kanalrechnung.Id(l.Netze.Single(n => n.Name == name).Id, ch);

    // ------------------------------------------------------------------ Meine Kanäle

    /// <summary>
    /// Seit 0.10.0 (AP3, Q87) gibt es den Bereich Meine Kanäle nicht mehr: Die Leiste nennt nur Start und Einstellungen, und die Kanäle stehen unter
    /// Einstellungen > Empfangen > Feinheiten mit der Überschrift "Je Kanal". Bis 0.9.3 hieß der Test Bereich_HeisstMeineKanaele_InLeisteUndUeberschrift.
    /// </summary>
    [Test]
    public static void Kanaele_UnterEmpfangen_KeinBereichMeineKanaele()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(KanalBeispiel());
            var h = a.Haupt(out _, out _, out _, out var netze);
            Pruefe.Falsch(h.Navigation.Any(b => b.Kennung == "netze" || b.Inhalt is NetzeViewModel), "kein Bereich Meine Kanäle mehr");
            Seiten.Feinheiten(h, Abschnittskennung.Empfangen);
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            _ = Ansichtspruefung.Ganz(haupt, 960);
            var wurzel = Bildlauf.Wurzel(haupt);
            var empfangen = Empfangen(haupt);
            var karte = (FrameworkElement)Benannt(empfangen, "FelderKarte").Single();
            Pruefe.Wahr(ReferenceEquals(netze, karte.DataContext), "die Kanäle stehen unter Empfangen > Feinheiten");
            Pruefe.Wahr(Ansichtspruefung.Sichtbar(karte, "Je Kanal"), "Überschrift Je Kanal");
            Pruefe.Falsch(Ansichtspruefung.Sichtbar(wurzel, "Netze und Rollen") || Ansichtspruefung.Sichtbar(wurzel, "Meine Kanäle"), "weder Netze und Rollen noch Meine Kanäle");
            Pruefe.Falsch(Benannt(wurzel, "EintragText").OfType<TextBlock>().Any(t => t.Text == "Meine Kanäle"), "die Leiste nennt Meine Kanäle nicht mehr");
        });
    }

    [Test]
    public static void MeineKanaele_Feld_ZeigtKanalMitChUndZustand()
    {
        using var a = new Aufbau(KanalBeispiel());
        var f = a.Funkfeld();
        var netze = a.Netze(f);
        var bridge = netze.Felder.Single(x => x.Feld == 0);
        Pruefe.Gleich("Brücke (Schiff 3)", bridge.Kanal, "der Kanal mit der Bezeichnung des Ch.");
        Pruefe.Gleich("Brücke", bridge.Netz, "der Name des Kanals für Zugriffsnamen");
        Pruefe.Gleich("SENDET", bridge.ZustandText, "das Wort zur Farbe: gewählt");
        Pruefe.Wahr(bridge.Sendet, "Sendet");
        Pruefe.Gleich("Kommando (Flottenführung)", netze.Felder.Single(x => x.Feld == 3).Kanal, "Kommando mit Ch.");
        Pruefe.Gleich("HÖRT", netze.Felder.Single(x => x.Feld == 3).ZustandText, "hört");
        Pruefe.Gleich("RUHE", netze.Felder.Single(x => x.Feld == 4).ZustandText, "Ch. 0 ist Ruhe");
        Pruefe.Gleich("AUS", netze.Felder.Single(x => x.Feld == 5).ZustandText, "ganz aus");
        Pruefe.Gleich("GESPERRT", netze.Felder.Single(x => x.Feld == 6).ZustandText, "von der Einsatzleitung gesperrt");
        Pruefe.Gleich("Global", netze.GlobalOhr!.Netz, "Global bleibt Global");
    }

    [Test]
    public static void MeineKanaele_ZweizeilenFelder_NichtDoppelt_HoerenAufMitErklaerung_Bei960Und1280()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(KanalBeispiel());
            var h = a.Haupt(out _, out _, out _, out var netze);
            // Seit 0.10.0 (AP3) unter Einstellungen > Empfangen > Feinheiten (bis 0.9.3 Bereich Meine Kanäle).
            Seiten.Feinheiten(h, Abschnittskennung.Empfangen);
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            foreach (var breite in Breiten)
            {
                var was = "Breite " + breite.ToString("0", De) + ": ";
                _ = Ansichtspruefung.Ganz(haupt, breite);
                var inhalt = Empfangen(haupt);
                var karte = Benannt(inhalt, "FelderKarte").Single();

                // nicht doppelt: Rolle und Ch. wählt man im Funkfeld, die Kanäle legt seit 0.8.1 die Rolle fest (Q44); die Felder ohne VA/Taste (E16).
                // Das gelbe "!" eines Dauersenders (Klappwahl) steht seit 0.10.0 in derselben Karte bei den Personen, ausgeblendet ohne Dauersender.
                Pruefe.Falsch(Bildlauf.Nachfahren<ComboBox>(karte).Any(Ansichtspruefung.ZuSehen) || Bildlauf.Nachfahren<Klappwahl>(karte).Any(Ansichtspruefung.ZuSehen),
                    was + "keine Auswahl von Rolle, Kanal oder Ch. je Kanal");
                Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(karte).Any(x => x.Text.StartsWith("Ob du einen Kanal hörst, auf welcher Seite und wie laut. Die Rolle und den Ch. wählst du im Funkfeld",
                        StringComparison.Ordinal)), was + "der Hinweis sagt, wo Rolle und Ch. gewählt werden");

                // zweizeilig: oben der ganze Name mit Ch., der umbricht statt abzuschneiden
                var namen = Benannt(karte, "KanalName").OfType<TextBlock>().ToList();
                Pruefe.Gleich(netze.Felder.Count, namen.Count, was + "ein Name je Feld");
                Pruefe.Wahr(namen.Any(n => n.Text == "Brücke (Schiff 3)"), was + "Brücke (Schiff 3) steht da");
                foreach (var n in namen)
                {
                    Pruefe.Wahr(n.TextTrimming == TextTrimming.None && n.TextWrapping == TextWrapping.Wrap, was + n.Text + ": bricht um, kürzt nicht");
                    Pruefe.Wahr(Lage(n, karte).Right <= karte.ActualWidth + 0.5, was + n.Text + ": bleibt in der Karte");
                    var voll = new FormattedText(n.Text, De, FlowDirection.LeftToRight, new Typeface(n.FontFamily, n.FontStyle, n.FontWeight, n.FontStretch), n.FontSize, Brushes.Black, 1.0);
                    Pruefe.Wahr(n.ActualWidth * 3 >= voll.WidthIncludingTrailingWhitespace && n.ActualHeight >= voll.Height - 0.5,
                        string.Create(De, $"{was}{n.Text}: ganz zu lesen ({n.ActualWidth:0.#} breit, {n.ActualHeight:0.#} hoch, Text {voll.WidthIncludingTrailingWhitespace:0.#})"));
                }
                foreach (var zeile in Bildlauf.Nachfahren<WrapPanel>(karte))
                {
                    foreach (var kind in zeile.Children.OfType<FrameworkElement>().Where(k => k.Visibility == Visibility.Visible))
                        Pruefe.Wahr(Lage(kind, zeile).Right <= zeile.ActualWidth + 0.5, string.Create(De, $"{was}{kind.GetType().Name} der zweiten Zeile passt in die Breite {zeile.ActualWidth:0.#}"));
                }

                // Hören auf: Links | Beide | Rechts, so beschriftet, mit der Erklärung der Seite im Kopfhörer oder Lautsprecher
                var etiketten = Benannt(karte, "OhrEtikett").OfType<TextBlock>().ToList();
                Pruefe.Gleich(netze.Felder.Count + 1, etiketten.Count, was + "ein Hören auf je Feld und Global");
                foreach (var e in etiketten)
                {
                    Pruefe.Gleich("Hören auf:", e.Text, was + "beschriftet");
                    var tip = e.ToolTip as string ?? "";
                    Pruefe.Wahr(tip.Contains("Kopfhörer", StringComparison.Ordinal) && tip.Contains("Lautsprecher", StringComparison.Ordinal) && tip.Contains("Seite", StringComparison.Ordinal),
                        was + "der Tooltip erklärt die Seite im Kopfhörer oder Lautsprecher: " + tip);
                }
                var knoepfe = Bildlauf.Nachfahren<RadioButton>(karte).Where(r => r.Content is "Links" or "Beide" or "Rechts").ToList();
                Pruefe.Gleich(3 * (netze.Felder.Count + 1), knoepfe.Count, was + "Links, Beide, Rechts je Feld und Global");
                foreach (var k in knoepfe)
                    Pruefe.Wahr(Hilfe.GetText(k) is { } t && t.Contains("Kopfhörer", StringComparison.Ordinal), was + k.Content + ": Tooltip nennt den Kopfhörer");
                Pruefe.Wahr(Benannt(karte, "GlobalZeile").Single().Visibility == Visibility.Visible, was + "Global als letzte Zeile");
                Pruefe.Wahr(Ansichtspruefung.Sichtbar(karte, "hört immer") && !Ansichtspruefung.Sichtbar(karte, "(A23)"), was + "hört immer, ohne Kürzel");
            }
        });
    }

    /// <summary>
    /// Die Karte Einsteigen steht nur, wo die Flotte Einheiten hat (Entwurf 5.5): Ohne sie (Vorlage Star Citizen) wählt man die Rolle im Funkfeld, und
    /// dieselbe Liste der 21 Rollen stünde sonst noch einmal da. Von 0.10.0 bis 0.11.1 stand sie auf Start unter der Startkarte (E10), seit 0.11.2
    /// nicht mehr (das Modell bleibt); die Felder und den Pegel je Person zeigt seit AP3 Einstellungen > Empfangen > Feinheiten (bis dahin Meine Kanäle).
    /// </summary>
    [Test]
    public static void MeineKanaele_Einsteigen_NurMitEinheiten()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var (name, lage, erwartet) in new[] { ("Star Citizen ohne Einheiten", KanalBeispiel(), false), ("Raidabend mit Einheiten", Raidabend(), true) })
            {
                using var a = new Aufbau(lage);
                a.Ablage.Aktuell.Audio.Hoerweise = Funk.Client.Ablage.Hoerweise.Headset;
                var h = a.Haupt(out _, out _, out _, out var netze);
                Pruefe.Gleich(erwartet, netze.ZeigtEinsteigen, name + ": Modell");
                h.AktiverBereich = h.Bereiche.Single(b => b.Inhalt is StartViewModel);
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                _ = Ansichtspruefung.Ganz(haupt, 960);
                var start = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is StartViewModel);
                // Seit 0.11.2 steht die Karte Einsteigen nicht mehr auf Start (Sarevoc 13.10.2026: "auf der Startseite nur das Audio, nichts weiter"); das
                // Modell oben bleibt, gezeigt wird sie nirgends.
                Pruefe.Falsch(Benannt(start, "EinsteigenKarte").Any(), name + ": keine Karte Einsteigen auf Start");
                Pruefe.Falsch(Ansichtspruefung.Sichtbar(start, "Einsteigen: Einheit und Rolle in einem Schritt"), name + ": keine Überschrift");
                // Seit 0.10.0 (AP3): Kanäle und Pegel je Person unter Einstellungen > Empfangen > Feinheiten (bis 0.9.3 Meine Kanäle).
                Seiten.Feinheiten(h, Abschnittskennung.Empfangen);
                _ = Ansichtspruefung.Ganz(haupt, 960);
                var inhalt = Bildlauf.Nachfahren<ContentPresenter>(Bildlauf.Wurzel(haupt)).First(c => c.Content is EinstellungenViewModel);
                Pruefe.Falsch(Benannt(inhalt, "EinsteigenKarte").Any(), name + ": nicht unter Einstellungen");
                Pruefe.Wahr(Ansichtspruefung.Sichtbar(Empfangen(haupt), "Je Person"), name + ": Pegel je Person unter Empfangen");
            }
        });
    }
    // ------------------------------------------------------------------ Brett und Meldungen

    private const ushort BridgeNetz = 77;

    private static ushort Bridge(byte ch) => Kanalrechnung.Id(BridgeNetz, ch);

    /// <summary>Der Beispielabend mit einem Kanalnetz "Brücke" (Ruhe, Schiff 1 bis 3): Kobold hört auf Schiff 3, Pilot und Gunner haben es mit festem Ch. 3.</summary>
    private static FunkLage MitBridge(FunkLage l)
    {
        var kanalnetz = new NetzInfo(BridgeNetz, "Brücke", false, false, true, 90) { Kanaele = ["Ruhe", "Schiff 1", "Schiff 2", "Schiff 3"], Offen = NetzOffen.HoerenSenden };
        RolleInfo MitFestemCh(RolleInfo r) => r.Id is Pilot or Gunner
            ? r with { Felder = [new RollenfeldInfo(0, BridgeNetz, null, true, true, true, false) { Ch = 3 }, .. r.Felder.Skip(1)] }
            : r;
        return l with
        {
            Netze = [.. l.Netze, kanalnetz],
            Rollen = [.. l.Rollen.Select(MitFestemCh)],
            Funker = [.. l.Funker.Select(f => f.KontoId == Kobold && f.Details is { } d
                ? f with { Details = d with { Felder = [new FeldInfo(0, Bridge(3), true, true, false), .. d.Felder.Skip(1)], SendetIn = [Bridge(3)] } }
                : f)],
        };
    }

    [Test]
    public static void Brett_ZeigtKanalMitCh_InBloeckenDetailUndAblagebewertung()
    {
        using var a = new Aufbau(dienst: MitRaidabend());
        a.Dienst.LageSetzen(MitBridge);
        var b = a.Brett();
        var pilot = OberflaecheBrettTests.Block(b, Schiff1, Pilot);
        Pruefe.Wahr(pilot.Netze.StartsWith("Brücke (Schiff 3), ", StringComparison.Ordinal), "der Rollenblock nennt den Kanal mit seinem festen Ch.: " + pilot.Netze);
        Pruefe.Wahr(pilot.Netze.Contains("Crew 1", StringComparison.Ordinal), "ein Einheitskanal bleibt ohne Ch.: " + pilot.Netze);

        b.WaehlenCommand.Execute(b.Karte(Kobold));
        Pruefe.Wahr(b.AuswahlFelder.Any(f => f.Netz == "Brücke (Schiff 3)"), "das Detail nennt Kanal und Ch.: " + string.Join("|", b.AuswahlFelder.Select(f => f.Netz)));

        var hagel = b.Karte(Hagel)!;
        var gunner3 = OberflaecheBrettTests.Block(b, Schiff3, Gunner);
        var bewertung = b.Bewerten(hagel, gunner3);
        Pruefe.Wahr(bewertung.Text.Contains(" - Kanäle Brücke (Schiff 3), ", StringComparison.Ordinal) && !bewertung.Text.Contains("Netze", StringComparison.Ordinal),
            "die Bewertung vor dem Loslassen sagt Kanäle: " + bewertung.Text);
    }

    [Test]
    public static void Teilnehmermeldungen_NennenDenKanalMitCh()
    {
        var d = MitRaidabend();
        d.LageSetzen(MitBridge);
        long ms = 0;
        var zeile = new Meldezeile(() => ms);
        using var t = new Teilnehmermeldungen(d, zeile, SofortTakt.Instanz);
        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            SchiffId = Schiff1,
            RolleId = Gunner,
            Platz = PlatzStatus.Belegt,
            Quelle = MitgliedschaftsQuelle.Verschieben,
            QuelleDurch = Ferox,
            Felder = [new FeldInfo(0, Bridge(3), true, true, false), new FeldInfo(1, 13, true, false, false)],
            SendeFelder = [0],
        });
        // Seit 0.11.0 eine Zeile ohne den Sendekanal (bis dahin "... Gunner. Du sendest in Brücke (Schiff 3)."); den Kanal mit Ch. nennen
        // die Phase und "Kanäle geändert" weiter unten.
        Pruefe.Gleich("Eingeteilt von Ferox: Schiff 1, Gunner", zeile.Text, "Umstellung durch die Leitung");
        zeile.Loesen("umstellung");

        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            UmstellungPhase = 2,
            LaufendePhase = new LaufendePhaseInfo(2, "Entern", "ENTERN", Ferox, Abend),
            Felder = [new FeldInfo(0, Bridge(3), true, false, false), new FeldInfo(1, 13, true, false, false), new FeldInfo(2, Bridge(1), true, true, false)],
            SendeFelder = [2],
        });
        Pruefe.Gleich("Phase Entern (Ferox): sendest in Brücke (Schiff 1)", zeile.Text, "die Phase nennt den Kanal mit Ch.");
        zeile.Loesen("umstellung");

        d.LageSetzen(l => l with
        {
            KontoVersion = l.KontoVersion + 1,
            UmstellungPhase = null,
            Quelle = MitgliedschaftsQuelle.Verschieben,
            QuelleDurch = Ferox,
            Felder = [.. l.Felder, new FeldInfo(4, Bridge(2), true, false, false)],
        });
        Pruefe.Gleich("Kanäle geändert von Ferox: hörst Brücke (Schiff 2)", zeile.Text, "neuer Hörkanal mit Ch., Wort Kanal");
    }

    // ------------------------------------------------------------------ Funkfeld: Liste, Rolle, Ch. von Global

    /// <summary>
    /// Legt das Kanalnetz <paramref name="netz"/> auf Ruhe in Feld <paramref name="feld"/> (hören und senden, nicht fest) oder leert es
    /// (<c>null</c>), wie es seit 0.8.1 nur noch Admin und Einsatzleitung tun (Q44); bis 0.8.0 ging das über die Kanalwahl am Feldnamen.
    /// </summary>
    private static void FeldVonAussen(Aufbau a, int feld, string? netz) =>
        a.Dienst.LageSetzen(l =>
        {
            var andere = l.Felder.Where(x => x.Feld != feld);
            return l with
            {
                Felder = netz is null
                    ? [.. andere]
                    : [.. andere.Append(new FeldInfo((byte)feld, K(l, netz, Kanalrechnung.Ruhe), true, true, false)).OrderBy(x => x.Feld)],
            };
        });

    private static (FunkfeldFenster Fenster, FunkfeldViewModel Modell) ListeBauen(Aufbau a, List<Window> fenster)
    {
        a.Ablage.Aktuell.Funkfeld.Darstellung = FunkfeldDarstellung.Liste;
        a.Ablage.Aktuell.Funkfeld.Ansicht = 10;
        var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
        var ff = new FunkfeldFenster(f, a.Ablage, Monitore);
        fenster.Add(ff);
        return (ff, f);
    }

    [Test]
    public static void Funkfeld_Liste_ZeigtVaOderTasteJeFeld()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(KanalBeispiel());
            var (ff, f) = ListeBauen(a, fenster);
            var (_, groesse) = Bildlauf.Zeichnen(ff);
            Console.WriteLine(string.Create(De, $"MASS Stufe 3.5 Liste 10 mit VA/Taste: {groesse.Width:0.#} x {groesse.Height:0.#} Einheiten"));
            var wurzel = Bildlauf.Wurzel(ff);
            var schalter = Benannt(wurzel, "VaSchalter").OfType<ToggleButton>().ToList();
            foreach (var k in f.Kacheln)
            {
                var va = schalter.SingleOrDefault(s => ReferenceEquals(s.DataContext, k));
                if (k.IstLeer)
                {
                    Pruefe.Wahr(va is null || va.Visibility != Visibility.Visible, "ein leeres Feld hat keinen Schalter: Feld " + k.Taste);
                    continue;
                }
                Pruefe.Wahr(va is not null && va.Visibility == Visibility.Visible, "Schalter da: Feld " + k.Taste + " " + k.Name);
                Pruefe.Gleich(k.Sprachaktivierung, va!.IsChecked == true, "steht wie das Feld: " + k.Name);
                Pruefe.Gleich(k.KannSprachaktivierung, va.IsEnabled, "gesperrt nur, wo der Kanal nur die Sprechtaste erlaubt: " + k.Name);
                Pruefe.Gleich(k.AusloesungText, Bildlauf.Nachfahren<TextBlock>(va).First().Text, "das Wort VA oder Taste: " + k.Name);
                var ort = Lage(va, wurzel);
                Pruefe.Wahr(ort.Right <= groesse.Width + 0.5 && ort.Width >= 30, string.Create(De, $"der Schalter passt in die Zeile: {ort}"));
                var name = (FrameworkElement)Benannt(wurzel, "NameText").Single(n => ReferenceEquals(n.DataContext, k));
                Console.WriteLine(string.Create(De, $"MASS Liste Feld {k.Taste} {k.Name}: Name {name.ActualWidth:0.#} breit, VA {ort.Width:0.#}"));
                Pruefe.Wahr(name.ActualWidth >= (k.IstGlobal ? 38 : 56), string.Create(De, $"der Kanalname behält Platz ({k.Name}: {name.ActualWidth:0.#})"));
            }

            // Umschalten wirkt wie auf der Kachel: gilt für alle Ch. des Kanals und steht danach auch im Bereich Meine Kanäle
            var bridge = f.Kacheln.Single(k => k.Name == "Brücke");
            var vorher = bridge.Sprachaktivierung;
            var schalterBridge = schalter.Single(s => ReferenceEquals(s.DataContext, bridge));
            schalterBridge.IsChecked = !vorher;
            Pruefe.Gleich(!vorher, bridge.Sprachaktivierung, "der Klick in der Liste schaltet um");
            Bildlauf.Leeren();
            Pruefe.Gleich(!vorher, schalterBridge.IsChecked == true, "und der Schalter folgt");
            f.Dispose();
        });
    }

    [Test]
    public static async Task Rolle_GeaendertMarkierung_NurBeiAbweichungDerFelder()
    {
        using var a = new Aufbau(KanalBeispiel());
        ushort RolleVon(string name) => a.Dienst.Lage.Rollen.Single(r => r.Name == name).Id;
        Pruefe.Gleich<string?>(null, await a.Dienst.RolleWaehlenAsync(RolleVon("Kapitän"), []), "Rolle wählen");
        var f = a.Funkfeld();
        Pruefe.Wahr(!f.RolleGeaendert && f.RolleText == "3 Kapitän" && f.RolleName == "3 Kapitän", "unmittelbar nach der Wahl: nichts geändert, Text " + f.RolleText);

        // ein anderer Ch. im selben Kanalnetz ist deine Wahl je Kanalnetz, keine Abweichung von der Rolle
        var bridge = f.Kacheln.Single(k => k.Name == "Brücke");
        bridge.ChWaehlenCommand.Execute(bridge.ChListe.Single(e => e.Ch == 5));
        Pruefe.Gleich((byte?)5, a.Dienst.Lage.ChVon(a.Dienst.Lage.FeldVon((byte)bridge.Platz)!.NetzId), "der Ch. ist gewechselt");
        Pruefe.Wahr(!f.RolleGeaendert, "Ch. gewechselt im selben Kanalnetz: nicht geändert");

        // ein anderes Kanalnetz in einem Feld der Rolle: geändert (seit 0.8.1 legt es nur der Admin hinein, Q44)
        var logistik = f.Kacheln.Single(k => k.Name == "Logistik");
        FeldVonAussen(a, logistik.Platz, "Jäger");
        Pruefe.Gleich("Jäger", logistik.Name, "das andere Kanalnetz liegt im Feld");
        Pruefe.Wahr(f.RolleGeaendert, "anderes Kanalnetz: geändert");
        Pruefe.Gleich("3 Kapitän (geändert)", f.RolleText, "mit der Markierung");
        Pruefe.Gleich("3 Kapitän", f.RolleName, "der Name bleibt ohne");
        Pruefe.Wahr(f.RolleHilfe.Contains("weichen", StringComparison.Ordinal) && f.RolleHilfe.Contains("noch einmal", StringComparison.Ordinal), "der Tooltip sagt, wie es zurückgeht: " + f.RolleHilfe);

        // die Rolle noch einmal wählen stellt sie her
        f.RolleWaehlenCommand.Execute(f.Rolle);
        Pruefe.Wahr(!f.RolleGeaendert && f.RolleText == "3 Kapitän", "wieder wie die Rolle");

        // ein Feld der Rolle geleert (seit 0.8.1 nur vom Admin): geändert
        var crew = f.Kacheln.Single(k => k.Name == "Crew");
        FeldVonAussen(a, crew.Platz, null);
        Pruefe.Wahr(crew.IstLeer, "das Feld ist leer");
        Pruefe.Wahr(f.RolleGeaendert, "ein Feld der Rolle geleert: geändert");

        // die Rolle noch einmal wählen stellt auch das geleerte Feld wieder her
        f.RolleWaehlenCommand.Execute(f.Rolle);
        Pruefe.Wahr(!f.RolleGeaendert, "wieder wie die Rolle");

        // ein Feld mehr, als die Rolle kennt, ist geändert; ein festes der Einsatzleitung (Zusatzkanal vom Brett) zählt nicht als deine Änderung
        var l0 = a.Dienst.Lage;
        var extra = new FeldInfo(9, K(l0, "Jäger", 1), true, false, false);
        Pruefe.Wahr(FunkfeldViewModel.Abweichung(l0 with { Felder = [.. l0.Felder, extra] }), "ein Zusatzfeld: geändert");
        Pruefe.Falsch(FunkfeldViewModel.Abweichung(l0 with { Felder = [.. l0.Felder, extra with { Fest = true }] }), "ein festes Zusatzfeld zählt nicht");
        // ohne gewählte Rolle gibt es nichts, wovon abzuweichen wäre
        using var b = new Aufbau(KanalBeispiel() with { Rolle = null });
        var g = b.Funkfeld();
        Pruefe.Wahr(!g.RolleGeaendert && g.RolleText == "Rolle wählen", "ohne Rolle keine Markierung");
    }

    /// <summary>
    /// Zuordnung Punkt 3 (0.11.0): Wer eingeteilt ist und keine Rolle gewählt hat, liest oben links die Rolle seines Platzes statt "Rolle
    /// wählen", mit dem Platz im Hilfetext. Gewählt ist in der Liste nichts; eine eigene Rolle geht vor.
    /// </summary>
    [Test]
    public static void Rolle_OhneGewaehlteRolle_ZeigtObenLinksDiePlatzrolle()
    {
        using var a = new Aufbau(KanalBeispiel() with { Rolle = null });
        var co = a.Dienst.Lage.Rollen.Single(r => r.Name == "Kapitän");
        var andere = a.Dienst.Lage.Rollen.First(r => r.Id != co.Id && r.SelbstWaehlbar);
        var f = a.Funkfeld();
        Pruefe.Gleich("Rolle wählen", f.RolleName, "ohne Platz wie bisher");

        a.Dienst.LageSetzen(l => l with { RolleId = co.Id, Quelle = MitgliedschaftsQuelle.Verschieben, QuelleDurch = 4 });
        Pruefe.Gleich("3 Kapitän", f.RolleName, "die Platzrolle oben links");
        Pruefe.Gleich("3 Kapitän", f.RolleText, "ohne Markierung");
        Pruefe.Wahr(f.Rolle is null, "in der Liste ist nichts gewählt");
        Pruefe.Wahr(f.RolleHilfe.Contains("Dein Platz: Kapitän (eingeteilt).", StringComparison.Ordinal)
            && f.RolleHilfe.Contains("der Platz bleibt", StringComparison.Ordinal), "der Hilfetext nennt den Platz: " + f.RolleHilfe);

        a.Dienst.LageSetzen(l => l with { Rolle = andere.Id });
        Pruefe.Wahr(f.RolleName != "3 Kapitän" && f.RolleName == f.Rolle?.Text, "eine eigene Rolle geht vor: " + f.RolleName);
        Pruefe.Falsch(f.RolleHilfe.Contains("Dein Platz", StringComparison.Ordinal), "dann ohne den Satz zum Platz");
    }

    /// <summary>
    /// Befund der Prüfung 0.11.0: Steht oben links die Platzrolle, markiert die aufgeklappte Liste sie, und Mausrad oder Pfeil gehen von ihr
    /// aus. Bis dahin sprang ein Ruck von "3 Kapitän" auf den ersten Eintrag der Liste, und nach 400 ms ging dessen Rolle hinaus.
    /// </summary>
    [Test]
    public static void Rolle_OhneGewaehlteRolle_MausradGehtVonDerPlatzrolleAus()
    {
        using var a = new Aufbau(KanalBeispiel() with { Rolle = null });
        var f = a.Funkfeld();
        var co = f.Rollen.Single(r => r.Text == "3 Kapitän");
        var i = f.Rollen.IndexOf(co);
        Pruefe.Wahr(i > 0 && i < f.Rollen.Count - 1, "die Platzrolle steht mitten in der Liste (" + i + ")");
        Pruefe.Gleich(null, f.RolleMarkiert, "ohne Platz und ohne Rolle ist nichts markiert");

        a.Dienst.LageSetzen(l => l with { RolleId = co.Id, Quelle = MitgliedschaftsQuelle.Verschieben, QuelleDurch = 4 });
        Pruefe.Gleich(co, f.RolleMarkiert, "die Liste markiert die Platzrolle");
        Pruefe.Wahr(f.Rolle is null, "gewählt ist weiter nichts");
        f.RolleSchrittCommand.Execute(1);
        Pruefe.Gleich(f.Rollen[i + 1], f.Rolle, "ein Schritt weiter von der Platzrolle aus");
        Pruefe.Gleich(f.Rollen[i + 1], f.RolleMarkiert, "die Markierung folgt");
        f.RolleSchrittCommand.Execute(-1);
        f.RolleSchrittCommand.Execute(-1);
        Pruefe.Gleich(f.Rollen[i - 1], f.Rolle, "und zurück einen vor die Platzrolle");
    }

    [Test]
    public static void Rolle_GeaendertMarkierung_ImFenster_RasterUndListe()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(KanalBeispiel());
            a.Dienst.RolleWaehlenAsync(a.Dienst.Lage.Rollen.Single(r => r.Name == "Kapitän").Id, []).GetAwaiter().GetResult();
            var f = a.Funkfeld();
            FeldVonAussen(a, f.Kacheln.Single(k => k.Name == "Logistik").Platz, "Jäger");
            Pruefe.Wahr(f.RolleGeaendert, "Ausgangslage: geändert");
            var ff = new FunkfeldFenster(f, a.Ablage, Monitore);
            fenster.Add(ff);
            _ = Bildlauf.Zeichnen(ff);
            var wurzel = Bildlauf.Wurzel(ff);
            var marke = (TextBlock)Benannt(wurzel, "RolleGeaendertText").Single();
            Pruefe.Wahr(Ansichtspruefung.ZuSehen(marke) && marke.Text.Contains("(geändert)", StringComparison.Ordinal), "Raster: (geändert) hinter der Rolle");
            var wahl = (FrameworkElement)ff.FindName("RolleWahl");
            Pruefe.Wahr(Lage(marke, wurzel).Right <= Lage(wahl, wurzel).Right + 0.5, "und es steht in der Rollenliste");
            var name = Bildlauf.Nachfahren<TextBlock>(wahl).Single(t => t.Text == "3 Kapitän");
            var schnitt = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(name);
            Pruefe.Wahr(name.ActualWidth >= 40 && (schnitt is null || schnitt.Bounds.Width >= name.ActualWidth - 0.5),
                string.Create(De, $"der Name der Rolle bleibt lesbar, nicht beschnitten ({name.ActualWidth:0.#} breit)"));

            f.Darstellung = FunkfeldDarstellung.Liste;
            ff.DarstellungAnwenden();
            _ = Bildlauf.Zeichnen(ff);
            var markeListe = (TextBlock)Benannt(Bildlauf.Wurzel(ff), "RolleGeaendertTextListe").Single();
            Pruefe.Wahr(Ansichtspruefung.ZuSehen(markeListe), "Liste: (geändert) hinter der Rolle");
            f.Dispose();
        });
    }

    [Test]
    public static void ChWaehler_Global1Bis20_AuchInAnderenFeldern_GesamtNie()
    {
        // Die Vorlage Star Citizen lässt Global ohne Ch.; mit Ch. (wie es die Konsole setzen kann) gilt, was hier geprüft wird.
        using var a = new Aufbau(Stufe34FunkfeldTests.MitGlobalKanaelen(KanalBeispiel()));
        var f = a.Funkfeld();
        var global = a.Dienst.Lage.Global!;
        Pruefe.Wahr(global.IstKanalnetz && global.Kanaele is { Count: > 2 }, "Global trägt Ruhe, Gesamt und Global 1 bis 20");
        var leer = f.Kacheln.First(k => !k.IstGlobal && k.IstLeer);

        // Seit 0.8.1 legt Global nur der Admin in ein anderes Feld (Q44, früher die Kanalwahl am Namen); den Ch. wählt der Funker.
        FeldVonAussen(a, leer.Platz, "Global");
        Pruefe.Wahr(leer.HatCh && leer.ChWaehlbar && leer.Name == "Global", "Global in einem anderen Feld, mit Ch.-Wähler");
        var texte = leer.ChListe.Select(e => e.Text).ToList();
        Pruefe.Wahr(texte.Contains("Ruhe") && texte.Contains("Global 1") && texte.Contains("Global 20"), "Ruhe und Global 1 bis 20 im Ch.-Wähler: " + string.Join("|", texte));
        Pruefe.Falsch(texte.Contains("Gesamt") || leer.ChListe.Any(e => e.Ch == Kanalrechnung.GlobalSelbst), "Gesamt wird nirgends angeboten");
        Pruefe.Gleich(21, leer.ChListe.Count, "Ruhe und 20 Kanäle");

        // Pfeile und Mausrad blättern von Ruhe zu Global 1, nie über Gesamt; gewählt wird nach der Ruhe von 400 ms
        leer.ChSchrittCommand.Execute(1);
        f.VormerkungenAbschicken();
        var neu = a.Dienst.Lage.FeldVon((byte)leer.Platz)!;
        Pruefe.Gleich(Kanalrechnung.Id(global.Id, 2), neu.NetzId, "der erste Schritt nach der Ruhe ist Global 1 (Ch. 2), nicht Gesamt");
        Pruefe.Gleich("Global 1", leer.ChText, "angezeigt als Global 1");

        // das feste Feld bleibt Gesamt ohne Wähler; der Tooltip sagt, dass es fest ist
        var festes = f.Kacheln[^1];
        Pruefe.Wahr(festes.IstGlobal && festes.ChText == "Gesamt" && !festes.ChWaehlbar && festes.ChListe.Count == 0, "das Global-Feld rechts unten bleibt fest auf Gesamt");
        Pruefe.Gleich(KachelViewModel.HerkunftGlobal, festes.Herkunft, "woher: fest rechts unten");
    }

    // ------------------------------------------------------------------ Gegenprüfung 2c, Restbefunde N1 bis N5 (Schritt 3.5)

    /// <summary>Die Farbe <paramref name="flaeche"/> mit ihrer Deckkraft über <paramref name="grund"/>, wie WPF sie beim Zeichnen mischt.</summary>
    private static Color Ueber(Color grund, Color flaeche)
    {
        double a = flaeche.A / 255.0;
        byte K(byte g, byte f) => (byte)Math.Round((g * (1 - a)) + (f * a));
        return Color.FromRgb(K(grund.R, flaeche.R), K(grund.G, flaeche.G), K(grund.B, flaeche.B));
    }

    private static double V(Color a, Color b) => Kontrast.Verhaeltnis(a, b);

    /// <summary>
    /// N2 und N1 der Gegenprüfung 2c: Die rote Linie der Automatik hält, was ihr Kommentar versprach, gemessen an den Flächen, über denen sie
    /// gezeichnet wird (Reihenfolge der Grafik: Fläche, Sendeabschnitt, Zone, Linie): mindestens 5:1 auf der Grafikfläche und auf der Zone,
    /// mindestens 3:1 im gesendeten Abschnitt (WCAG 1.4.11), auch mit Zone darüber. Bis 3.4 verfehlte die Linie (die rote Schrift selbst)
    /// über der alten Zone die 5:1 in den damaligen Farbstellungen. Gegenprobe heute: Die Aufhellung hebt den Kontrast, und eine gedeckte
    /// Linie (zur Hälfte zum Grund) verfehlte 5:1.
    /// </summary>
    [Test]
    public static void Schwelle_RoteLinie_HaeltDenKontrastGegenGrundZoneUndSendeabschnitt()
    {
        var grund = SchwellenGrafik.HintergrundFarbe;
        var zone = Ueber(grund, Akzente.ZoneFarbe);
        var gesendet = Ueber(grund, SchwellenGrafik.SendenFlaecheFarbe);
        var gesendetZone = Ueber(gesendet, Akzente.ZoneFarbe);
        foreach (var satz in Farbvorschlaege.Alle)
        {
            var linie = Akzente.AutomatikFarbe(satz.RotSchrift);
            Console.WriteLine(string.Create(De, $"MASS Linie der Automatik {satz.Name}: Fläche {V(linie, grund):0.00}, Zone {V(linie, zone):0.00}, gesendet {V(linie, gesendet):0.00}, gesendet mit Zone {V(linie, gesendetZone):0.00}"));
            Pruefe.Wahr(V(linie, grund) >= 5 && V(linie, zone) >= 5, string.Create(De, $"{satz.Name}: mindestens 5:1 auf Fläche und Zone ({V(linie, grund):0.00}, {V(linie, zone):0.00})"));
            Pruefe.Wahr(V(linie, gesendet) >= 3 && V(linie, gesendetZone) >= 3, string.Create(De, $"{satz.Name}: mindestens 3:1 im gesendeten Abschnitt ({V(linie, gesendet):0.00}, {V(linie, gesendetZone):0.00})"));
            Pruefe.Wahr(linie.R >= linie.G + 40 && linie.R >= linie.B + 40, $"{satz.Name}: die Linie bleibt rot ({linie})");
            // Gegenprobe: Die Aufhellung hebt den Kontrast, und eine gedeckte rote Linie (Rot der Schrift zur Hälfte zum Grund) verfehlte 5:1.
            Pruefe.Wahr(V(linie, zone) > V(satz.RotSchrift, zone), string.Create(De, $"{satz.Name}: die Aufhellung hebt den Kontrast ({V(satz.RotSchrift, zone):0.00} zu {V(linie, zone):0.00})"));
            var gedeckt = Color.FromRgb((byte)((satz.RotSchrift.R + grund.R) / 2), (byte)((satz.RotSchrift.G + grund.G) / 2), (byte)((satz.RotSchrift.B + grund.B) / 2));
            Pruefe.Wahr(V(gedeckt, zone) < 5, string.Create(De, $"{satz.Name}: eine gedeckte Linie verfehlte 5:1 ({V(gedeckt, zone):0.00}); der Test erkennt es"));
        }
    }

    /// <summary>
    /// N1 der Gegenprüfung 2c, seit Klangstufe C1 für den Korridor, der die Grenzlinie ersetzt: Die Zone ist blau getönt und damit klar etwas
    /// anderes als die rote Linie und als der Akzent, in dem die Linien des Korridors stehen; die Linien halten den Kontrast gegen Grund und
    /// Zone (WCAG 1.4.11, 3:1). Die Farben der Zeichnung kommen aus den Ressourcen und stimmen mit Palette und Farben.xaml überein (Legende).
    /// Bis C1 prüfte der Test die blaue Grenzlinie (gestrichelt, mit eigenem Griff); die gibt es nicht mehr.
    /// </summary>
    [Test]
    public static void Schwelle_KorridorZone_IstBlau_LinienImAkzentHaltenKontrast()
    {
        var grund = SchwellenGrafik.HintergrundFarbe;
        var zone = Ueber(grund, Akzente.ZoneFarbe);
        foreach (var satz in Farbvorschlaege.Alle)
        {
            var linie = Akzente.AutomatikFarbe(satz.RotSchrift);
            var abstand = Math.Sqrt(Math.Pow(Palette.Grenzlinie.R - linie.R, 2) + Math.Pow(Palette.Grenzlinie.G - linie.G, 2) + Math.Pow(Palette.Grenzlinie.B - linie.B, 2));
            Console.WriteLine(string.Create(De, $"MASS Korridor {satz.Name}: Zone gegen rote Linie Farbabstand {abstand:0}, Akzent auf Fläche {V(satz.Akzent, grund):0.00}, auf Zone {V(satz.Akzent, zone):0.00}"));
            Pruefe.Wahr(Palette.Grenzlinie.B >= Palette.Grenzlinie.R + 100 && abstand >= 120, string.Create(De, $"{satz.Name}: Zone blau gegen die rosa Linie, Abstand {abstand:0}"));
            Pruefe.Wahr(Math.Abs(Palette.Grenzlinie.R - satz.Akzent.R) + Math.Abs(Palette.Grenzlinie.G - satz.Akzent.G) + Math.Abs(Palette.Grenzlinie.B - satz.Akzent.B) >= 150, $"{satz.Name}: Zone nicht im Akzent der Linien");
            Pruefe.Wahr(V(satz.Akzent, grund) >= 3 && V(satz.Akzent, zone) >= 3, string.Create(De, $"{satz.Name}: Linien im Akzent mindestens 3:1 ({V(satz.Akzent, grund):0.00}, {V(satz.Akzent, zone):0.00})"));
        }
        Bildlauf.Ausfuehren(_ =>
        {
            var rand = new Border();
            var a = Akzente.Fuer(rand);
            Pruefe.Wahr(a.ZoneFlaeche is SolidColorBrush { Color: var z } && z == Akzente.ZoneFarbe, "Zone blau mit 10 %");
            Pruefe.Wahr(a.Stift2.Brush is SolidColorBrush { Color: var s } && s == (rand.TryFindResource("Farbe.Akzent") is Color ak ? ak : Palette.Akzent), "Linien im Akzent der Farbstellung");
            Pruefe.Wahr(a.AutomatikStift.Brush is SolidColorBrush { Color: var l } && l == Akzente.AutomatikFarbe(rand.TryFindResource("Farbe.RotSchrift") is Color rs ? rs : Palette.RotSchrift), "die Linie der Automatik aufgehellt (aus der Farbstellung)");
        });
        var xaml = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Design", "Farben.xaml"));
        Pruefe.Wahr(xaml.Contains("Farbe.Grenzlinie\">#FF5EA9FF", StringComparison.Ordinal) && xaml.Contains("Farbe.GrenzlinieFlaeche\">#1A5EA9FF", StringComparison.Ordinal), "Farben.xaml wie Palette");
        var legende = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Fenster", "Schwellenbereich.xaml"));
        Pruefe.Wahr(legende.Contains("Pinsel.GrenzlinieFlaeche", StringComparison.Ordinal) && legende.Contains("Korridor: Mindest bis Obergrenze", StringComparison.Ordinal), "die Legende nennt den Korridor");
        Pruefe.Falsch(legende.Contains("GrenzlinieDb", StringComparison.Ordinal), "kein Regler der Grenzlinie mehr");
    }

    /// <summary>
    /// N5 der Gegenprüfung 2c: Liegt der Fokus auf der Seite Sprachaktivierung in der Grafik und geht es mit Strg+1 oder Strg+2 (bis 0.9.3 bis Strg+4) zu einem
    /// anderen Bereich, ist unten nichts aufgeklappt, die Grafik hat keinen Platz, und der Fokus fiele ins Leere. Jetzt nimmt ihn die
    /// Navigation. Im Speicher über den logischen Fokus des Fensters geprüft.
    /// </summary>
    [Test]
    public static void Schwelle_Fokus_FaelltNachStrg1Bis4_VonDerSeiteNichtInsLeere()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var nummer in BereichsTasten)
            {
                using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
                var h = a.Haupt(out _, out _, out _, out _);
                var haupt = new Hauptfenster(h, a.Ablage, Monitore);
                fenster.Add(haupt);
                h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
                _ = Bildlauf.Zeichnen(haupt, 1, Fenstergroessen.SeiteVoreinstellung);
                var grafik = haupt.Schwellenbereich.Verlaufsgrafik;
                FocusManager.SetFocusedElement(haupt, grafik);
                Pruefe.Wahr(ReferenceEquals(FocusManager.GetFocusedElement(haupt), grafik), "Ausgangslage: Fokus in der Grafik der Seite (unten gibt es seit 0.10.0 nichts)");
                h.BereichWaehlenCommand.Execute(nummer);
                Bildlauf.Leeren();
                var fokus = FocusManager.GetFocusedElement(haupt) as FrameworkElement;
                Pruefe.Wahr(fokus is not null && !ReferenceEquals(fokus, grafik) && fokus.Name == "Navi", "Strg+" + nummer + ": der Fokus liegt bei der Navigation, nicht in der Grafik: " + (fokus?.Name ?? "nirgends"));
            }
        });
    }

    // ------------------------------------------------------------------ Sichtproben für Sarevoc (mit FUNK_BILDER)

    private static void SchwelleVerlauf(Aufbau a, SchwellenViewModel schwelle, ref long zeit)
    {
        var szene = new Funk.Client.Klang.Vorfuehrungsszene(7);
        var gate = a.Maschine.Gate;
        for (var ms = 0L; ms <= 20_000; ms += 20) a.Maschine.Melden(szene.Naechste(ms, gate, true, false) with { ZeitMs = zeit + ms });
        zeit += 20_020;
        schwelle.Takt();
    }

    /// <summary>
    /// Die Sichtproben des Schritts 3.5 für Sarevoc, mit <c>FUNK_BILDER</c> (Präfix 35-): Meine Kanäle in Anthrazit bei 960 und 1280 Breite, das Brett
    /// mit Kanal und Ch., die Funkfeld-Liste mit VA/Taste je Feld und die Rolle mit (geändert) in Raster und Liste, die Sprachaktivierung
    /// mit der blauen Grenzlinie in Anthrazit und ohne wirkende Grenze.
    /// </summary>
    [Test]
    public static void Sichtproben_FuerJu()
    {
        if (Bildablage.Ordner() is null) return;
        Bildlauf.Ausfuehren(fenster =>
        {
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
            using (var a = new Aufbau(KanalBeispiel()))
            {
                a.Ablage.Aktuell.Farbstellung = Farbstellung.Anthrazit;
                var h = a.Haupt(out _, out _, out _, out _);
                // Seit 0.10.0 (AP3) unter Einstellungen > Empfangen > Feinheiten (bis 0.9.3 Meine Kanäle).
                Seiten.Feinheiten(h, Abschnittskennung.Empfangen);
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                foreach (var breite in Breiten)
                    Bildablage.Ablegen(Ansichtspruefung.Ganz(haupt, breite), string.Create(De, $"35-01-empfangen-kanaele-{breite:0}-anthrazit"));
            }

            using (var a = new Aufbau(dienst: MitRaidabend()))
            {
                a.Dienst.LageSetzen(MitBridge);
                var h = a.Haupt(out _, out _, out _, out _);
                var b = h.Brett;
                b.KartenAbwartenAsync().GetAwaiter().GetResult();
                h.BereichWaehlenCommand.Execute("5");
                b.WaehlenCommand.Execute(b.Karte(Kobold));
                var voll = OberflaecheBrettTests.Block(b, Schiff3, Gunner);
                var bewertung = b.Bewerten(b.Karte(Hagel)!, voll);
                voll.Ablagezustand = bewertung.Wirkung;
                voll.AblageText = bewertung.Text;
                var haupt = new Hauptfenster(h, a.Ablage);
                fenster.Add(haupt);
                var (bild, _) = Bildlauf.Zeichnen(haupt, 1, new Size(1920, 1080));
                Bildablage.Ablegen(bild, "35-02-brett-kanal-mit-ch-anthrazit");
            }

            using (var a = new Aufbau(KanalBeispiel()))
            {
                var kapitaen = a.Dienst.Lage.Rollen.Single(r => r.Name == "Kapitän").Id;
                a.Dienst.RolleWaehlenAsync(kapitaen, []).GetAwaiter().GetResult();
                var f = OberflaecheFunkfeldBilderTests.Lebendig(a);
                FeldVonAussen(a, f.Kacheln.Single(k => k.Name == "Logistik").Platz, "Jäger");
                var ff = new FunkfeldFenster(f, a.Ablage, Monitore);
                fenster.Add(ff);
                var (raster, _) = Bildlauf.Zeichnen(ff, 1.75);
                Bildablage.Ablegen(raster, "35-03-funkfeld-rolle-geaendert-raster-anthrazit");
                f.Darstellung = FunkfeldDarstellung.Liste;
                ff.DarstellungAnwenden();
                var (liste, _) = Bildlauf.Zeichnen(ff, 1.75);
                Bildablage.Ablegen(liste, "35-04-funkfeld-liste-va-taste-und-geaendert-anthrazit");
                f.Dispose();
            }

            // Seit Klangstufe C1 ersetzt der Korridor die Grenzlinie: mit gelernter Stimme und ohne.
            long zeit = 0;
            foreach (var (kurz, stellung, gelernt) in new (string, Farbstellung, bool)[]
                     {
                         ("anthrazit", Farbstellung.Anthrazit, true), ("nicht-eingemessen-anthrazit", Farbstellung.Anthrazit, false),
                     })
            {
                Bildlauf.RessourcenSetzen(stellung);
                using var a = new Aufbau(FunkDienstAttrappe.Beispiel());
                a.Ablage.Aktuell.Farbstellung = stellung;
                if (gelernt) a.Maschine.Korridor = new KorridorStand(-23f, float.NaN, float.NaN);
                var h = a.Haupt(out _, out var schwelle, out _, out _);
                var haupt = new Hauptfenster(h, a.Ablage, Monitore);
                fenster.Add(haupt);
                h.AktiverBereich = h.Navigation.Single(b => b.Kennung == HauptViewModel.SprachaktivierungKennung);
                schwelle.Modus = GateModus.Automatisch;
                SchwelleVerlauf(a, schwelle, ref zeit);
                var (bild, _) = Bildlauf.Zeichnen(haupt, 1.75, Fenstergroessen.SeiteVoreinstellung);
                Bildablage.Ablegen(bild, "35-05-schwelle-korridor-" + kurz);
            }
            Bildlauf.RessourcenSetzen(Farbstellung.Anthrazit);
        });
    }
}
