// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Funk.Verwaltung;

namespace Funk.Tests;

/// <summary>
/// Vorprüfung vor dem Einrichten der Verwaltung (seit 0.4.1, Sarevocs Entscheidung vom 01.10.2026): TPM 2.0 ist technisch Pflicht,
/// ein Windows-Konto mit PIN oder Kennwort ist Richtlinie (kein Microsoft-Konto nötig). Seit 0.5.1 (Sarevocs Entscheidung vom
/// 02.10.2026) prüft, nennt und empfiehlt der Client keine Laufwerksverschlüsselung mehr. Hier ohne Hardware: die Deutung
/// dessen, was Windows meldet (PCP_PLATFORM_TYPE, Fehlercodes), die Prüfung mit Frist und die Texte. Die echte Prüfung
/// (Platform Crypto Provider) steht hinter einer Schnittstelle und läuft in keinem Test.
/// </summary>
internal static class AdminvoraussetzungenTests
{
    private const int TbsTpmNichtGefunden = unchecked((int)0x8028400F);
    private const int NteGeraetNichtGefunden = unchecked((int)0x80090035);
    private const int NteProviderDllFehlt = unchecked((int)0x8009001E);
    private const int NteProviderDllFehler = unchecked((int)0x8009001D);
    private const int NteNichtUnterstuetzt = unchecked((int)0x80090029);

    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden.");
    }

    [Test]
    public static void Tpm_AusPlattformtyp()
    {
        var zwei = Tpmdeutung.AusPlattform("TPM-Version:2.0 -Level:0-Revision:1.59-VendorID:'AMD '-Firmware:6.24.0.6");
        Pruefe.Gleich(TpmBefund.Tpm20, zwei.Befund, "TPM 2.0");
        Pruefe.Wahr(zwei.Gefunden && !zwei.Sperrt, "gefunden, sperrt nicht");
        Pruefe.Gleich(Voraussetzungstexte.TpmGefunden, zwei.Text, "Text");
        Pruefe.Gleich("TPM 2.0 gefunden.", Voraussetzungstexte.TpmGefunden, "so steht es da");
        Pruefe.Wahr(zwei.Plattform!.Contains("AMD", StringComparison.Ordinal), "die Meldung von Windows bleibt dabei");

        var alt = Tpmdeutung.AusPlattform("TPM-Version:1.2 -Level:2-Revision:116-VendorID:'IFX '-Firmware:4.40");
        Pruefe.Gleich(TpmBefund.Tpm12, alt.Befund, "TPM 1.2");
        Pruefe.Wahr(alt.Sperrt && !alt.Gefunden, "TPM 1.2 sperrt das Einrichten");
        Pruefe.Wahr(alt.Text.Contains("TPM 1.2", StringComparison.Ordinal) && alt.Text.Contains("TPM 2.0", StringComparison.Ordinal), "sagt warum: " + alt.Text);

        foreach (var unklar in new[] { null, "", "Unsinn", "TPM-Version:3.0 -Level:0", "Version 2.0" })
        {
            var t = Tpmdeutung.AusPlattform(unklar);
            Pruefe.Gleich(TpmBefund.NichtFeststellbar, t.Befund, "unklar: " + (unklar ?? "null"));
            Pruefe.Falsch(t.Sperrt, "unklar sperrt nicht, versuchen darf man: " + (unklar ?? "null"));
            Pruefe.Wahr(t.Text.Length > 20, "mit Satz: " + t.Text);
        }
    }

    [Test]
    public static void Tpm_AusFehlercodes()
    {
        foreach (var code in new[] { TbsTpmNichtGefunden, NteGeraetNichtGefunden })
        {
            var t = Tpmdeutung.BeimOeffnen(code);
            Pruefe.Gleich(TpmBefund.KeinTpm, t.Befund, "kein TPM beim Öffnen: " + code.ToString("X8", CultureInfo.InvariantCulture));
            Pruefe.Wahr(t.Sperrt && t.Text.Contains("kein TPM", StringComparison.Ordinal), t.Text);
            Pruefe.Gleich(TpmBefund.KeinTpm, Tpmdeutung.BeimLesen(code).Befund, "kein TPM auch beim Lesen");
        }

        // Lässt sich der Anbieter nicht öffnen, scheitert auch das Anlegen, denn es öffnet denselben Anbieter zuerst.
        foreach (var code in new[] { NteProviderDllFehlt, NteProviderDllFehler, NteNichtUnterstuetzt })
        {
            var t = Tpmdeutung.BeimOeffnen(code);
            Pruefe.Gleich(TpmBefund.ProviderFehlt, t.Befund, "Anbieter lässt sich nicht öffnen: " + code.ToString("X8", CultureInfo.InvariantCulture));
            Pruefe.Wahr(t.Sperrt, "sperrt");
            Pruefe.Wahr(t.Text.Contains("0x" + code.ToString("X8", CultureInfo.InvariantCulture), StringComparison.Ordinal) && t.Text.Contains("Platform Crypto Provider", StringComparison.Ordinal),
                "nennt Anbieter und Code: " + t.Text);
        }

        // Geöffnet, aber die Eigenschaft fehlt: nicht feststellbar, Einrichten bleibt möglich.
        var lesen = Tpmdeutung.BeimLesen(NteNichtUnterstuetzt);
        Pruefe.Gleich(TpmBefund.NichtFeststellbar, lesen.Befund, "Eigenschaft nicht lesbar");
        Pruefe.Wahr(!lesen.Sperrt && lesen.Text.Contains("0x80090029", StringComparison.Ordinal), lesen.Text);

        var grund = Tpmdeutung.Unklar("keine Antwort");
        Pruefe.Wahr(grund.Befund == TpmBefund.NichtFeststellbar && !grund.Sperrt && grund.Text.Contains("keine Antwort", StringComparison.Ordinal), grund.Text);
    }

    [Test]
    public static async Task Pruefung_OhneAusnahmenUndMitFrist()
    {
        var tpm = new Probe(() => Tpmdeutung.AusPlattform("TPM-Version:2.0 -Level:0"));
        var stand = await new Adminvoraussetzungen(tpm).PruefenAsync();
        Pruefe.Wahr(stand.Gefunden, "TPM 2.0 im Stand");
        Pruefe.Wahr(tpm.Wohnung == ApartmentState.STA, "auf eigenem STA-Faden wie seit 0.4.1");
        Pruefe.Wahr(tpm.Faden != Environment.CurrentManagedThreadId, "nicht auf dem Aufrufer");

        // Die Prüfung wirft: nicht feststellbar, nichts fliegt bis zur Oberfläche.
        var wirft = new Probe(() => throw new System.ComponentModel.Win32Exception(5, "Zugriff verweigert"));
        stand = await new Adminvoraussetzungen(wirft).PruefenAsync();
        Pruefe.Wahr(stand.Befund == TpmBefund.NichtFeststellbar && !stand.Sperrt && stand.Text.Contains("Zugriff verweigert", StringComparison.Ordinal),
            "TPM: " + stand.Text);

        // Die Prüfung hängt: nach der Frist nicht feststellbar, ohne auf sie zu warten.
        using var los = new ManualResetEventSlim();
        var haengt = new Probe(() =>
        {
            los.Wait(TimeSpan.FromSeconds(30));
            return Tpmdeutung.AusPlattform("TPM-Version:2.0 -Level:0");
        });
        var uhr = Stopwatch.StartNew();
        stand = await new Adminvoraussetzungen(haengt, TimeSpan.FromMilliseconds(300)).PruefenAsync();
        uhr.Stop();
        los.Set();
        Pruefe.Wahr(stand.Befund == TpmBefund.NichtFeststellbar && !stand.Sperrt && stand.Text.Contains("keine Antwort", StringComparison.Ordinal),
            "hängt: " + stand.Text);
        Pruefe.Wahr(uhr.Elapsed < TimeSpan.FromSeconds(20), "endet nach der Frist, nicht nach der Prüfung: " + uhr.Elapsed);
        Pruefe.Gleich(TimeSpan.FromSeconds(10), Adminvoraussetzungen.StandardFrist, "Frist im Betrieb");

        // Ohne Prüfung (Dienst ohne Angabe, Tests): nichts angefasst, unklar, nichts gesperrt.
        var ohne = await Adminvoraussetzungen.Ungeprueft.PruefenAsync();
        Pruefe.Wahr(ohne.Befund == TpmBefund.NichtFeststellbar && !ohne.Sperrt, "ungeprüft");
    }

    [Test]
    public static void Texte_PflichtUndRichtlinie()
    {
        var pflicht = Voraussetzungstexte.TechnischPflicht;
        foreach (var teil in new[] { "TPM 2.0", "nicht exportierbar", "nie als Datei", "Schlüsselblob", "vom TPM verschlüsselt", "genau diesem TPM" })
            Pruefe.Wahr(pflicht.Contains(teil, StringComparison.Ordinal), "technisch Pflicht nennt \"" + teil + "\": " + pflicht);

        var richtlinie = Voraussetzungstexte.Richtlinie;
        foreach (var teil in new[] { "Richtlinie", "PIN oder Kennwort", "lokales Konto genügt", "Microsoft-Konto", "nicht nötig", "nicht das TPM" })
            Pruefe.Wahr(richtlinie.Contains(teil, StringComparison.Ordinal), "Richtlinie nennt \"" + teil + "\": " + richtlinie);

        Pruefe.Wahr(richtlinie.EndsWith(Voraussetzungstexte.RichtlinieErklaerung, StringComparison.Ordinal), "die Zeile zeigt die Erklärung ohne Wiederholung");
        Pruefe.Wahr(Voraussetzungstexte.Prueft.Length > 5, "Text während der Prüfung");

        // Der Fehler beim Anlegen nennt TPM 2.0 als einzige technische Pflicht und schiebt es nicht auf eine PIN.
        var ohne = Adminschluesselwahl.OhneTpm;
        Pruefe.Wahr(ohne.Contains("TPM 2.0", StringComparison.Ordinal) && ohne.Contains("Windows-PIN", StringComparison.Ordinal)
            && ohne.Contains("braucht", StringComparison.Ordinal), ohne);
        Pruefe.Falsch(ohne.Contains("Pflicht für Admin-PCs", StringComparison.Ordinal), ohne);

        // Als Striche nur - und _ (CLAUDE.md, Texte).
        foreach (var text in new[] { pflicht, richtlinie, Voraussetzungstexte.Prueft, ohne })
            Pruefe.Falsch(text.Any(z => z is (char)0x2013 or (char)0x2014 or (char)0x2212 or (char)0x2022 or (char)0x2026), "nur - und _: " + text);
    }

    /// <summary>
    /// Sarevocs Entscheidung vom 02.10.2026 (Fassung 0.5.1): Die Empfehlung ist ganz raus. Der Client prüft BitLocker nicht, nennt es
    /// nicht und empfiehlt es nicht, die mitgelieferten Texte ebenso. Gelesen wird jede Datei in src, installer, tools\paket und
    /// docs, in jeder Schreibweise; ausgenommen sind nur docs\handbuch und docs\handbuch-neu, die gerade neu geschrieben werden.
    /// </summary>
    [Test]
    public static void Repository_NenntBitLockerNirgends()
    {
        var wurzel = Wurzel();
        var ausgenommen = HandbuchOrdner
            .Select(o => Path.Combine(wurzel, "docs", o) + Path.DirectorySeparatorChar)
            .ToList();
        var dateien = new[] { "src", "installer", "docs", Path.Combine("tools", "paket") }
            .SelectMany(o => Directory.EnumerateFiles(Path.Combine(wurzel, o), "*", SearchOption.AllDirectories))
            .Where(p => !p.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) && !p.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase))
            .Where(p => !ausgenommen.Any(a => p.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Pruefe.Wahr(dateien.Count > 50, "genug Dateien gesehen: " + dateien.Count);
        Pruefe.Wahr(dateien.Any(p => p.EndsWith("Voraussetzungen.cs", StringComparison.Ordinal))
            && dateien.Any(p => p.EndsWith("LIESMICH-Server.txt", StringComparison.Ordinal)), "Code und Paketvorlagen sind dabei");
        var funde = dateien.Where(p => NenntBitLocker(File.ReadAllText(p))).Select(p => Path.GetRelativePath(wurzel, p)).ToList();
        Pruefe.Gleich(0, funde.Count, "BitLocker genannt in: " + string.Join(", ", funde));

        // Gegenprobe: jede Schreibweise, auch im Bezeichner; anderes bleibt unberührt.
        foreach (var text in new[] { "Den Stick mit BitLocker To Go verschlüsseln", "ShellBitLockerPruefung", "BITLOCKER", "bitlocker-an" })
            Pruefe.Wahr(NenntBitLocker(text), "gefunden: " + text);
        Pruefe.Falsch(NenntBitLocker(Voraussetzungstexte.TpmGefunden + " " + Voraussetzungstexte.Richtlinie), "TPM und Richtlinie bleiben");
    }

    /// <summary>Unter docs, vom Wächter ausgenommen: das Handbuch und seine Neufassung.</summary>
    private static readonly string[] HandbuchOrdner = ["handbuch", "handbuch-neu"];

    private static bool NenntBitLocker(string text) => text.Contains("bitlocker", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sarevocs Entscheidung 8, im Code belegt: Der Admin-Schlüssel entsteht ohne Schloss am Schlüssel (keine UI-Richtlinie, kein
    /// PCP-Kennwort), im Benutzerkontext und über den Platform Crypto Provider, nicht über den Anbieter von Windows Hello
    /// (Microsoft Passport Key Storage Provider). Eine Windows-PIN ist so keine Bedingung für das Anlegen.
    /// </summary>
    [Test]
    public static void Adminschluessel_OhneSchlossAmSchluesselUndOhneHello()
    {
        var quelle = File.ReadAllText(Path.Combine(Wurzel(), "src", "Funk.Verwaltung", "AdminSchluessel.cs"));
        foreach (var verboten in new[] { "UIPolicy", "CngUIPolicy", "PCP_PASSWORD", "PCP_USAGEAUTH", "MachineKey", "Passport", "NgcKey" })
            Pruefe.Falsch(quelle.Contains(verboten, StringComparison.Ordinal), "AdminSchluessel.cs ohne " + verboten);
        using var probe = TpmAdminSchluessel.Parameter.Erzeugen(CngProvider.MicrosoftSoftwareKeyStorageProvider, null);
        Pruefe.Gleich(CngUIProtectionLevels.None, probe.Key.UIPolicy.ProtectionLevel, "keine PIN am Schlüssel");
        Pruefe.Falsch(probe.Key.IsMachineKey, "Benutzerkontext");
    }

    /// <summary>Im Betrieb prüft der Verwaltungsteil am echten PC; ohne diese Zeile käme dort immer "nicht feststellbar".</summary>
    [Test]
    public static void Einstieg_PrueftAmEchtenPc()
    {
        var einstieg = File.ReadAllText(Path.Combine(Wurzel(), "src", "Funk.Verwaltung", "Einstieg.cs"));
        Pruefe.Wahr(einstieg.Contains("Adminvoraussetzungen.AmPc()", StringComparison.Ordinal), "Einstieg gibt die echte Prüfung hinein");
    }

    /// <summary>Eine TPM-Prüfung im Test: merkt sich Faden und Wohnung, dann das Ergebnis oder die Ausnahme.</summary>
    private sealed class Probe(Func<TpmStand> ergebnis) : ITpmPruefung
    {
        public ApartmentState Wohnung { get; private set; } = ApartmentState.Unknown;

        public int Faden { get; private set; }

        public TpmStand Pruefen()
        {
            Wohnung = Thread.CurrentThread.GetApartmentState();
            Faden = Environment.CurrentManagedThreadId;
            return ergebnis();
        }
    }
}
