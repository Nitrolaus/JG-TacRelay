// JG TacRelay - Maintainer: Sarevoc
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Modelle;
using Funk.Protokoll.Steuerung;

namespace Funk.Tests.Oberflaeche;

/// <summary>
/// Hauptfenster und Funkfeld im Speicher (<see cref="Bildlauf"/>, ohne Fensterhandle, auch neben einem Spiel): Zugriffstasten
/// (Alt+Buchstabe) sind in allem eindeutig, was gleichzeitig zu sehen sein kann, und das Aktivieren des Hauptfensters prüft das
/// Serverprofil neu. Befund der Gegenprüfung: Alt+N gehörte zugleich dem Nein der Rückfrage und dem Schalter Lernen; bei
/// doppelten Tasten springt WPF nur zwischen den Zielen hin und her, statt eines auszulösen. Der Klassenname steht nach
/// OberflaecheFensterTests (Hinweis dort: Bildläufe danach).
/// </summary>
internal static class OberflaecheHauptfensterTests
{
    /// <summary>
    /// Die Fußzeile nennt die Tastenkürzel der Bereiche, die gerade da sind: Strg+5 nur mit dem Brett (ZUWEISEN), Strg+9 nur mit
    /// der Verwaltung, und beide verschwinden wieder mit ihren Bereichen. Befund der Prüfung 0.3.0 (niedrig): Dort stand fest
    /// "Strg+1-4 Bereiche  Strg+6 Wer ist da". Seit 0.10.0 (E2, E13): "Strg+1 Start  Strg+2 Einstellungen", kein Strg+T, und der Klangtreiber
    /// ("Klang: ...") steht nicht mehr in der Fußzeile, sondern unter Einstellungen > Fehlersuche; Warnungen beim Start bleiben links.
    /// </summary>
    [Test]
    public static void Fusszeile_NenntStrg5UndStrg9NurMitIhrenBereichen()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau(FunkLage.Leer with { Status = Verbindungsstatus.Aktiv });
            var dienst = new VerwaltungsdienstAttrappe();
            using var teil = new Funk.Verwaltung.Verwaltungsteil(dienst, new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst));
            a.Verwaltung = teil;
            var h = a.Haupt(out _, out _, out _, out _);
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            string Fuss()
            {
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(1120, 900));
                var fuss = (Border)haupt.FindName("Fuss");
                return string.Join(" | ", Bildlauf.Nachfahren<TextBlock>(fuss).Where(Ansichtspruefung.ZuSehen).Select(Ansichtspruefung.Inhalt));
            }

            var ohne = Fuss();
            Pruefe.Wahr(ohne.Contains("Strg+1 Start  Strg+2 Einstellungen", StringComparison.Ordinal) && ohne.Contains("Strg+6 Wer ist da", StringComparison.Ordinal), "Grundtasten: " + ohne);
            Pruefe.Falsch(ohne.Contains("Strg+3", StringComparison.Ordinal) || ohne.Contains("Strg+4", StringComparison.Ordinal) || ohne.Contains("Strg+1-4", StringComparison.Ordinal),
                "Strg+3 und Strg+4 gibt es nicht mehr (E2): " + ohne);
            Pruefe.Falsch(ohne.Contains("Klang:", StringComparison.Ordinal), "der Klangtreiber steht unter Fehlersuche, nicht in der Fußzeile (E13): " + ohne);
            Pruefe.Falsch(ohne.Contains("Strg+5", StringComparison.Ordinal) || ohne.Contains("Strg+9", StringComparison.Ordinal), "ohne Brett und Verwaltung: " + ohne);
            // 0.10.0 (E2): Strg+T öffnet nur noch die Seite Sprachaktivierung, wenn es sie gibt, und steht nicht in der Fußzeile.
            Pruefe.Falsch(ohne.Contains("Strg+T", StringComparison.Ordinal), "kein Strg+T: " + ohne);

            a.Dienst.LageSetzen(l => l with { Konto = new KontoInfo(9, "Nordlicht", "nordlicht", Stufe.Admin, [Recht.Zuweisen]) { Admin = AdminStatus.Aktiv } });
            Pruefe.Wahr(h.Bereiche.Any(b => b.Kennung == HauptViewModel.RaidKennung) && h.Bereiche.Any(b => b.Kennung == HauptViewModel.VerwaltungKennung),
                "Raid und Verwaltung in der Navigation");
            var mit = Fuss();
            Pruefe.Wahr(mit.Contains("Strg+5 Funkfeld-Zuteilung", StringComparison.Ordinal) && mit.Contains("Strg+9 Verwaltung", StringComparison.Ordinal), "mit Brett und Verwaltung: " + mit);

            a.Dienst.LageSetzen(l => l with { Konto = new KontoInfo(9, "Nordlicht", "nordlicht", Stufe.Funker, []) });
            var wieder = Fuss();
            Pruefe.Falsch(wieder.Contains("Strg+5", StringComparison.Ordinal) || wieder.Contains("Strg+9", StringComparison.Ordinal), "nach dem Entzug wieder ohne: " + wieder);
        });
    }

    [Test]
    public static void Zugriffstasten_InJedemBereichUndFensterEindeutig()
    {
        Pruefe.Gleich(null, Taste("Name__mit Unterstrich"), "doppelter Unterstrich ist keine Taste");
        Pruefe.Gleich('M', Taste("Auto_matik"), "Taste nach dem Unterstrich, groß");

        Bildlauf.Ausfuehren(fenster =>
        {
            // Gegenprobe zuerst: Der alte Stand (Nein und Ler_nen zugleich) muss auffallen, ein doppelter Unterstrich nicht.
            var alt = new StackPanel();
            alt.Children.Add(new Button { Content = "_Nein" });
            alt.Children.Add(new CheckBox { Content = new AccessText { Text = "Ler_nen" } });
            alt.Children.Add(new Button { Content = "Name__mit Unterstrich" });
            var altBefund = Doppelte(Zugriffstasten(alt), "alter Stand").ToList();
            Pruefe.Wahr(altBefund.Count == 1 && altBefund[0].Contains("Alt+N", StringComparison.Ordinal), "Gegenprobe: " + string.Join(" | ", altBefund));

            // Mit abgelaufenem Code, damit auch der Knopf "Neuen Code holen" zählt; die Rückfrage ist zugeklappt dabei.
            using var a = new Aufbau(FunkLage.Leer with
            {
                Status = Verbindungsstatus.Registrierung,
                Registrierung = new Registrierungsangabe("7K3M-Q9PX-2HDT", "/funk verknuepfen", DateTimeOffset.Now.AddMinutes(-10)),
            });
            // Mit Verwaltungsteil (Admin-Setup) und einem Konto, das Admin ist: auch der Bereich Verwaltung zählt, und "Wer ist da"
            // ist offen.
            var dienst = new VerwaltungsdienstAttrappe();
            using var teil = new Funk.Verwaltung.Verwaltungsteil(dienst, new Funk.Verwaltung.Modelle.VerwaltungViewModel(dienst));
            a.Verwaltung = teil;
            var h = a.Haupt(out var funkfeld, out _, out _, out _);
            a.Dienst.LageSetzen(l => l with { Konto = new KontoInfo(9, "Nordlicht", "nordlicht", Stufe.Admin, []) { Admin = AdminStatus.Aktiv } });
            Pruefe.Wahr(h.Bereiche.Any(b => b.Kennung == HauptViewModel.VerwaltungKennung), "Verwaltung in der Navigation");
            h.WerIstDa.Offen = true;
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            var befunde = new List<string>();
            // Je Bereich eine Gruppe mit dem Rahmen. Bis 0.9.3 konnte die Sprachaktivierung unten unter jedem Bereich offen sein und zählte
            // überall mit ('M' und 'B'); seit 0.10.0 ist sie nur eine Seite, ihr Bereich steht aber verborgen im Fenster und zählt darum bis
            // AP3 weiter mit. Der Unterpunkt Sprachaktivierung fehlt hier ohne Kanäle; ihn prüft OberflaecheStufe2cTests.
            foreach (var bereich in h.Navigation.ToList())
            {
                h.AktiverBereich = bereich;
                _ = Bildlauf.Zeichnen(haupt, 1, new Size(1120, 900));
                var tasten = Zugriffstasten(Bildlauf.Wurzel(haupt));
                var da = tasten.Select(t => t.Taste).ToHashSet();
                if (bereich.Kennung == HauptViewModel.StartKennung)
                    Pruefe.Wahr(da.IsSupersetOf(['V', 'T', 'J', 'N', 'C', 'S', 'D', 'K']), "Start: auch zugeklappte Karten zählen; die Adresse steht nur unter Details im Admin-Setup (" + string.Join("", da.Order()) + ")");
                befunde.AddRange(Doppelte(tasten, "Hauptfenster, " + bereich.Titel));
            }

            // Das Funkfeld ist ein eigenes Fenster und damit eine eigene Gruppe.
            var ff = new FunkfeldFenster(funkfeld, a.Ablage);
            fenster.Add(ff);
            _ = Bildlauf.Zeichnen(ff);
            befunde.AddRange(Doppelte(Zugriffstasten(Bildlauf.Wurzel(ff)), "Funkfeld"));
            funkfeld.Dispose();
            Pruefe.Gleich(0, befunde.Count, "doppelte Zugriffstasten: " + string.Join(" | ", befunde));
        });
    }

    [Test]
    public static void Hauptfenster_AktivierenPrueftDasServerprofilNeu()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            using var k = new OberflaecheProfilTests.Schluessel();
            using var a = new Aufbau(FunkLage.Leer);
            var profil = a.Ordner.Datei("profil.json");
            a.Profil = new Profilquelle(profil, [k.Spki]);
            a.Ablage.Aktuell.Bereich = "verbindung";
            var h = a.Haupt(out _, out _, out _, out _);
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            Pruefe.Falsch(h.Verbindung.Bereit, "Profil fehlt im Programmordner");
            OberflaecheProfilTests.ProfilSchreiben(profil, k);
            // Ohne Fensterhandle kommt kein echtes Activated; OnActivated ist der Weg, auf dem WPF das Ereignis auslöst.
            typeof(Window).GetMethod("OnActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(haupt, [EventArgs.Empty]);
            Pruefe.Wahr(h.Verbindung.Bereit, "nach dem Aktivieren bereit: " + h.Verbindung.WasFehlt);
        });
    }

    /// <summary>
    /// Fassung im Titel (Sarevoc, 02.10.2026, UI-Durchsicht Punkt 7; 0.6.0 Stufe 1): Der Fenstertitel (Taskleiste, Alt+Tab) lautet
    /// "JG TacRelay 0.6.0", und in der eigenen Titelleiste steht die Fassung klein hinter "JG TACRELAY". Beides kommt aus der
    /// Assembly, also aus Directory.Build.props, und nicht aus einer festen Zahl in XAML oder Code.
    /// </summary>
    [Test]
    public static void Hauptfenster_FassungImTitelAusDerAssembly()
    {
        var props = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "Directory.Build.props"));
        var ab = props.IndexOf("<Version>", StringComparison.Ordinal) + "<Version>".Length;
        var fassung = props[ab..props.IndexOf("</Version>", ab, StringComparison.Ordinal)].Trim();
        var assembly = typeof(Hauptfenster).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Pruefe.Gleich(fassung, assembly, "die Assembly trägt die Fassung aus Directory.Build.props");
        var xaml = File.ReadAllText(Path.Combine(EinrichtungTests.Repo(), "src", "Funk.Client", "Fenster", "Hauptfenster.xaml"));
        Pruefe.Falsch(xaml.Contains(fassung, StringComparison.Ordinal), "keine feste Fassung im XAML");

        Bildlauf.Ausfuehren(fenster =>
        {
            using var a = new Aufbau();
            var h = a.Haupt(out _, out _, out _, out _);
            var haupt = new Hauptfenster(h, a.Ablage);
            fenster.Add(haupt);
            _ = Bildlauf.Zeichnen(haupt, 1, new Size(960, 700));
            Pruefe.Gleich("JG TacRelay " + fassung, haupt.Title, "Fenstertitel mit Fassung");
            var text = haupt.FindName("FassungText") as TextBlock ?? throw new PruefungException("Fassung in der Titelleiste fehlt");
            Pruefe.Gleich(fassung, text.Text, "Fassung in der Titelleiste");
            Pruefe.Wahr(Ansichtspruefung.ZuSehen(text) && text.FontSize <= 12, "klein und zu sehen");
            var titel = Bildlauf.Nachfahren<TextBlock>(Bildlauf.Wurzel(haupt)).First(t => Ansichtspruefung.Inhalt(t) == "JG TACRELAY");
            Pruefe.Wahr(text.TranslatePoint(new Point(0, 0), titel).X >= titel.ActualWidth, "hinter JG TACRELAY");
            Pruefe.Wahr(Kontrast.Verhaeltnis(Ansichtspruefung.Farbe(text.Foreground), Ansichtspruefung.Flaeche(text)) >= 4.5, "lesbar auf der Titelleiste");
        });
    }

    /// <summary>
    /// Alle Zugriffstasten unter <paramref name="wurzel"/>, auch in zugeklappten Teilen: über den sichtbaren und den logischen
    /// Baum, denn ein nie gemessenes Steuerelement hat seine Vorlage noch nicht angewandt. Gezählt werden AccessText und Knöpfe,
    /// Schalter und Beschriftungen mit Text als Inhalt; aus dem macht ihr ContentPresenter (RecognizesAccessKey) ein AccessText,
    /// das dann nicht noch einmal zählt.
    /// </summary>
    internal static List<(char Taste, string Wo)> Zugriffstasten(DependencyObject wurzel)
    {
        var gefunden = new List<(char, string)>();
        var gesehen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var offen = new Stack<DependencyObject>();
        offen.Push(wurzel);
        while (offen.TryPop(out var d))
        {
            if (!gesehen.Add(d)) continue;
            if (d is ButtonBase or Label && d is ContentControl { Content: string text })
            {
                if (Taste(text) is { } t) gefunden.Add((t, text));
                continue;
            }
            if (d is AccessText { AccessKey: not '\0' } at && VisualTreeHelper.GetParent(at) is not ContentPresenter { Content: string })
                gefunden.Add((char.ToUpperInvariant(at.AccessKey), at.Text));
            if (d is Visual or Visual3D)
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) offen.Push(VisualTreeHelper.GetChild(d, i));
            foreach (var kind in LogicalTreeHelper.GetChildren(d))
                if (kind is DependencyObject o) offen.Push(o);
        }
        return gefunden;
    }

    /// <summary>Die Zugriffstaste eines Texts wie bei WPF: der Buchstabe nach dem ersten einzelnen "_"; "__" ist ein Unterstrich.</summary>
    internal static char? Taste(string text)
    {
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '_') continue;
            if (text[i + 1] == '_')
            {
                i++;
                continue;
            }
            return char.ToUpperInvariant(text[i + 1]);
        }
        return null;
    }

    internal static IEnumerable<string> Doppelte(IEnumerable<(char Taste, string Wo)> tasten, string gruppe) =>
        tasten.GroupBy(t => t.Taste).Where(g => g.Count() > 1)
            .Select(g => gruppe + ": Alt+" + g.Key + " bei " + string.Join(", ", g.Select(t => "\"" + t.Wo + "\"")));
}
