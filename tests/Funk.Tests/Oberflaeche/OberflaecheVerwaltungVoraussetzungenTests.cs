// JG TacRelay - Maintainer: Sarevoc
using System.Windows.Controls;
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
/// Vorprüfung im Schritt "Verwaltung einrichten" (seit 0.4.1, Sarevocs Entscheidung vom 01.10.2026) über der Attrappe: Sie läuft
/// beim Wechsel in den Schritt, ohne TPM 2.0 gibt es kein Einrichten, ein unklares TPM hält nichts auf. Dazu die
/// Einrichtungskarte im Hauptfenster gezeichnet, mit TPM 2.0, unklarem TPM und ohne TPM, mit den Kontrastregeln in Anthrazit
/// und dem Abstand der Zeilen, seit die dritte Zeile mit 0.5.1 fort ist (Sarevocs Entscheidung vom 02.10.2026). Mit
/// <c>FUNK_BILDER</c> entsteht je Fall und Farbstellung einrichten-&lt;fall&gt;-p&lt;n&gt;.png. Der Name sortiert nach
/// OberflaecheFensterTests (CODEMAP, Fallen: Application.Current).
/// </summary>
internal static class OberflaecheVerwaltungVoraussetzungenTests
{
    private const int TbsTpmNichtGefunden = unchecked((int)0x8028400F);
    private const int NteProviderDllFehlt = unchecked((int)0x8009001E);
    private const int NteNichtUnterstuetzt = unchecked((int)0x80090029);
    private const string Einrichten = "Admin-Schlüssel anlegen und beantragen";

    private static Verwaltungslage Vorgesehen() => new() { Verbunden = true, Name = "Nordlicht", AntragMoeglich = true };

    [Test]
    public static async Task Vorpruefung_LaeuftBeimEinrichten()
    {
        var d = new VerwaltungsdienstAttrappe();
        d.LageSetzen(_ => new Verwaltungslage { Verbunden = true, Name = "Nordlicht" });
        using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        Pruefe.Wahr(vm.IstKeinAdmin && d.VoraussetzungenGeprueft == 0, "kein Admin: keine Vorprüfung");

        d.LageSetzen(l => l with { AntragMoeglich = true });
        Pruefe.Wahr(vm.IstEinrichten && d.VoraussetzungenGeprueft == 1, "beim Wechsel in den Schritt einmal geprüft: " + d.VoraussetzungenGeprueft);
        Pruefe.Wahr(vm.TpmGefunden && !vm.TpmSperrt && vm.TpmText == "TPM 2.0 gefunden.", vm.TpmText);
        Pruefe.Wahr(vm.TpmPlattform is { } p && p.Contains("TPM-Version:2.0", StringComparison.Ordinal), "Meldung von Windows dabei: " + vm.TpmPlattform);
        Pruefe.Wahr(vm.EinrichtenCommand.CanExecute(null), "TPM 2.0: Einrichten möglich");

        d.LageSetzen(l => l with { Name = "Nordlicht" });
        Pruefe.Gleich(1, d.VoraussetzungenGeprueft, "nicht bei jeder Lageänderung im selben Schritt neu");

        await vm.EinrichtenCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.Aufrufe.Contains("Antrag") && vm.Kennung == VerwaltungsdienstAttrappe.Kennung && vm.Fehler is null, "eingerichtet");

        // Erneut prüfen, wenn Windows keine Fassung nennt: sachlicher Hinweis, Einrichten bleibt möglich.
        d.Tpm = Tpmdeutung.BeimLesen(NteNichtUnterstuetzt);
        await vm.VoraussetzungenPruefenCommand.ExecuteAsync(null);
        Pruefe.Wahr(d.VoraussetzungenGeprueft == 2 && !vm.TpmGefunden && !vm.TpmSperrt && vm.TpmText == d.Tpm.Text && vm.EinrichtenCommand.CanExecute(null),
            "unklar: sachlicher Hinweis, Einrichtung möglich: " + vm.TpmText);
        Pruefe.Gleich(null, vm.TpmPlattform, "ohne Meldung von Windows keine Zeile dazu");

        // Wieder hinein in den Schritt (etwa nach einem Wiederverbinden): neu geprüft.
        d.LageSetzen(_ => Verwaltungslage.Getrennt);
        d.LageSetzen(_ => Vorgesehen());
        Pruefe.Gleich(3, d.VoraussetzungenGeprueft, "beim nächsten Wechsel in den Schritt wieder");
    }

    [Test]
    public static async Task Vorpruefung_OhneTpm20_KeinEinrichten()
    {
        foreach (var tpm in new[]
                 {
                     Tpmdeutung.BeimOeffnen(TbsTpmNichtGefunden),
                     Tpmdeutung.AusPlattform("TPM-Version:1.2 -Level:2-Revision:116-VendorID:'IFX '-Firmware:4.40"),
                     Tpmdeutung.BeimOeffnen(NteProviderDllFehlt),
                 })
        {
            var d = new VerwaltungsdienstAttrappe { Tpm = tpm };
            d.LageSetzen(_ => Vorgesehen());
            using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
            Pruefe.Wahr(vm.IstEinrichten && vm.TpmSperrt && !vm.TpmGefunden && vm.TpmText == tpm.Text, tpm.Befund + ": " + vm.TpmText);
            Pruefe.Falsch(vm.EinrichtenCommand.CanExecute(null), tpm.Befund + ": Knopf aus");
            // Auch am Knopf vorbei kein Antrag und kein Schlüssel.
            await vm.EinrichtenCommand.ExecuteAsync(null);
            Pruefe.Falsch(d.Aufrufe.Contains("Antrag"), tpm.Befund + ": kein Antrag");
            Pruefe.Gleich(tpm.Text, vm.Fehler, tpm.Befund + ": der Grund steht als Fehler da");
        }

        // Nicht feststellbar: versuchen darf man; scheitert das Anlegen, sagt es Adminschluesselwahl.OhneTpm.
        var unklar = new VerwaltungsdienstAttrappe { Tpm = Tpmdeutung.BeimLesen(NteNichtUnterstuetzt) };
        unklar.LageSetzen(_ => Vorgesehen());
        using var vmUnklar = new VerwaltungViewModel(unklar, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        Pruefe.Wahr(!vmUnklar.TpmSperrt && !vmUnklar.TpmGefunden && vmUnklar.EinrichtenCommand.CanExecute(null), "TPM unklar: Einrichten geht");
    }

    [Test]
    public static void Vorpruefung_EinrichtenWartetAufDasErgebnis()
    {
        var d = new VerwaltungsdienstAttrappe { VoraussetzungenOffen = new TaskCompletionSource<TpmStand>() };
        d.LageSetzen(_ => Vorgesehen());
        using var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
        Pruefe.Wahr(vm.PrueftVoraussetzungen && vm.TpmText == Voraussetzungstexte.Prueft, "während der Prüfung: " + vm.TpmText);
        Pruefe.Falsch(vm.EinrichtenCommand.CanExecute(null), "während der Prüfung kein Einrichten");
        Pruefe.Falsch(vm.VoraussetzungenPruefenCommand.CanExecute(null), "kein zweiter Lauf daneben");
        var offen = d.VoraussetzungenOffen;
        d.VoraussetzungenOffen = null;
        offen.SetResult(Tpmdeutung.AusPlattform(VerwaltungsdienstAttrappe.Plattform));
        Pruefe.Wahr(!vm.PrueftVoraussetzungen && vm.TpmGefunden && vm.EinrichtenCommand.CanExecute(null) && vm.VoraussetzungenPruefenCommand.CanExecute(null),
            "danach: Ergebnis da, Knöpfe frei");
    }

    private static (string Name, TpmStand Tpm)[] Faelle() =>
    [
        ("tpm-gefunden", Tpmdeutung.AusPlattform(VerwaltungsdienstAttrappe.Plattform)),
        ("tpm-unklar", Tpmdeutung.BeimLesen(NteNichtUnterstuetzt)),
        ("ohne-tpm", Tpmdeutung.BeimOeffnen(TbsTpmNichtGefunden)),
    ];

    /// <summary>
    /// Die Einrichtungskarte im Hauptfenster wie mit dem Admin-Setup, mit TPM 2.0, unklarem TPM und ohne TPM, in Anthrazit:
    /// was technisch Pflicht ist und was Richtlinie; der Knopf nur ohne TPM aus. Seit 0.5.1 sind es zwei Zeilen; die Richtlinie
    /// ist die letzte, und die Knöpfe stehen mit demselben Abstand darunter wie vorher unter der letzten Zeile.
    /// </summary>
    [Test]
    public static void Vorpruefung_Bilder_MitUnklarUndOhneTpm()
    {
        Bildlauf.Ausfuehren(fenster =>
        {
            foreach (var satz in Farbvorschlaege.Alle)
            {
                Bildlauf.RessourcenSetzen(satz.Stellung);
                foreach (var (fall, tpm) in Faelle())
                {
                    var d = new VerwaltungsdienstAttrappe { Tpm = tpm };
                    d.LageSetzen(_ => Vorgesehen());
                    var vm = new VerwaltungViewModel(d, jetzt: () => VerwaltungsdienstAttrappe.Abend);
                    using var teil = new Verwaltungsteil(d, vm);
                    using var a = new Aufbau(FunkDienstAttrappe.Beispiel()) { Verwaltung = teil };
                    a.Ablage.Aktuell.Audio.Hoerweise = Funk.Client.Ablage.Hoerweise.Headset;
                    var h = a.Haupt(out _, out _, out _, out _);
                    a.Dienst.LageSetzen(l => l with { Konto = l.Konto! with { Admin = AdminStatus.Aktiv } });
                    h.AktiverBereich = h.Bereiche.Single(b => b.Kennung == HauptViewModel.VerwaltungKennung);
                    var haupt = new Hauptfenster(h, a.Ablage);
                    fenster.Add(haupt);
                    var bild = Ganz(haupt, 1280);
                    var was = satz.Name + ", " + fall;
                    var ansicht = Bildlauf.Nachfahren<VerwaltungAnsicht>(Bildlauf.Wurzel(haupt)).Single();

                    var karte = (Border)ansicht.FindName("EinrichtenKarte");
                    var tpmZeile = (Border)ansicht.FindName("TpmZeile");
                    var richtlinie = (Border)ansicht.FindName("RichtlinieZeile");
                    Pruefe.Wahr(ZuSehen(karte) && ZuSehen(tpmZeile) && ZuSehen(richtlinie), was + ": zwei Zeilen der Vorprüfung");
                    Pruefe.Wahr(Sichtbar(ansicht, "TPM 2.0 (technisch Pflicht)") && Sichtbar(ansicht, "PIN oder Kennwort (Richtlinie)"), was + ": Pflicht und Richtlinie beim Namen");
                    Pruefe.Wahr(Sichtbar(ansicht, "lokales Konto genügt") && Sichtbar(ansicht, "Schlüsselblob"), was + ": Richtlinie und Erklärung zum Schlüssel");
                    Pruefe.Falsch(Sichtbar(ansicht, "Richtlinie für Admin-PCs"), was + ": die Richtlinie steht nicht doppelt");
                    Pruefe.Falsch(Sichtbar(ansicht, "geht trotzdem"), was + ": kein Widerspruch zum gesperrten Knopf ohne TPM");
                    Pruefe.Falsch(Sichtbar(ansicht, "Pflicht für Admin-PCs"), was + ": alter Satz fort");
                    Pruefe.Falsch(Sichtbar(karte, "empfohlen") || Sichtbar(karte, "Verschlüsselung") || Sichtbar(karte, "verschlüsselung"),
                        was + ": keine Empfehlung zur Laufwerksverschlüsselung mehr");
                    Pruefe.Wahr(Bildlauf.Nachfahren<TextBlock>(tpmZeile).Any(t => ZuSehen(t) && Inhalt(t) == tpm.Text), was + ": Befund in der TPM-Zeile: " + tpm.Text);
                    if (fall == "ohne-tpm") Pruefe.Wahr(Sichtbar(ansicht, "kein TPM"), was + ": Grund");

                    var knopf = Bildlauf.Nachfahren<Button>(ansicht).Single(b => ZuSehen(b) && b.Content as string == Einrichten);
                    Pruefe.Gleich(fall != "ohne-tpm", knopf.IsEnabled, was + ": Knopf Einrichten");

                    // Abstände in der Karte: TPM-Zeile, 4 Punkte, Richtlinie, 10 Punkte, Knöpfe; nichts ragt aus der Karte.
                    double Oben(System.Windows.FrameworkElement e) => e.TranslatePoint(new System.Windows.Point(0, 0), karte).Y;
                    double Unten(System.Windows.FrameworkElement e) => Oben(e) + e.ActualHeight;
                    Pruefe.Wahr(Math.Abs(Oben(richtlinie) - Unten(tpmZeile) - 4) < 0.5, was + ": Abstand TPM zur Richtlinie " + (Oben(richtlinie) - Unten(tpmZeile)));
                    Pruefe.Wahr(Math.Abs(Oben(knopf) - Unten(richtlinie) - 10) < 0.5, was + ": Abstand Richtlinie zu den Knöpfen " + (Oben(knopf) - Unten(richtlinie)));
                    Pruefe.Wahr(Unten(knopf) <= karte.ActualHeight && Oben(tpmZeile) >= 0, was + ": alles in der Karte");
                    Pruefe.Wahr(Bildlauf.Nachfahren<Button>(ansicht).Any(b => ZuSehen(b) && b.Content as string == "Erneut prüfen" && b.IsEnabled), was + ": erneut prüfen");
                    KontrastPruefen(ansicht, satz, was, mindestens: 8);
                    Bildablage.Ablegen(bild, "einrichten-" + fall + "-" + satz.Stellung switch { _ => "anthrazit" });
                }
            }
        });
    }
}
