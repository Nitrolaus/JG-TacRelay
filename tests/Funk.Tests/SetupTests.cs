// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Funk.Client;
using Funk.Testserver;

namespace Funk.Tests;

/// <summary>
/// Setup des Clients (installer\TacRelay.iss, installer\probefunk-starten.cmd, tools\setup-bauen.ps1), mechanisch am
/// Quelltext geprüft: feste AppId, ohne Administratorrechte, keine Registry-Schreibzugriffe, Pfade zitiert, Fassung aus
/// einer Quelle, Probefunk mit Einstellungen und kurzem Socketpfad im Benutzerordner, Programmsymbol mit allen Größen,
/// AppMutex gleich der Marke des Programms, die Seite für eine vorhandene Installation (Aktualisieren, Reparieren, Neu
/// installieren) und die Texte (definiert, Platzhalter, per du, nur erlaubte Zeichen). Den Pascal-Teil übersetzt erst ISCC;
/// kein Test baut oder startet das Setup, gebaut und mit innounp geprüft wird es nur von tools\setup-bauen.ps1.
/// </summary>
internal static partial class SetupTests
{
    /// <summary>
    /// Die Kennung des Programms für Windows, neu seit der Umbenennung in JG TacRelay (Q7, sauberer Schnitt); danach nie ändern.
    /// Steht gleich in TacRelay.iss und setup-bauen.ps1.
    /// </summary>
    private const string AppId = "{E2908D2D-EA8B-4B2D-B4F1-CBDE75DE42AE}";

    /// <summary>Die Kennung bis 0.12.0 (unter altem Namen); sie darf nirgends mehr stehen.</summary>
    private const string AppIdAlt = "{30D15D11-8E8A-49D0-B2B5-A3517479BA00}";

    private static string Wurzel()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "TacRelay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("TacRelay.slnx nicht gefunden.");
    }

    private static string Lesen(params string[] teile) => File.ReadAllText(Path.Combine([Wurzel(), .. teile]));

    /// <summary>
    /// Die Abschnitte des Skripts mit ihren Zeilen, ohne Leerzeilen, Kommentare (;) und Präprozessorzeilen (#). Was vor dem
    /// ersten Abschnitt steht, liegt unter "".
    /// </summary>
    internal static Dictionary<string, List<string>> Abschnitte(string skript)
    {
        var abschnitte = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) { [""] = [] };
        var aktuell = abschnitte[""];
        foreach (var roh in skript.Split('\n'))
        {
            var zeile = roh.TrimEnd('\r');
            var kopf = AbschnittKopf().Match(zeile);
            if (kopf.Success)
            {
                aktuell = abschnitte.TryGetValue(kopf.Groups[1].Value, out var da) ? da : abschnitte[kopf.Groups[1].Value] = [];
                continue;
            }
            var t = zeile.Trim();
            if (t.Length == 0 || t.StartsWith(';') || t.StartsWith('#')) continue;
            aktuell.Add(zeile);
        }
        return abschnitte;
    }

    /// <summary>Wert einer Anweisung im Abschnitt [Setup], oder null.</summary>
    private static string? Anweisung(Dictionary<string, List<string>> abschnitte, string name)
    {
        var treffer = abschnitte["Setup"].Where(z => z.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)).ToList();
        Pruefe.Wahr(treffer.Count <= 1, name + " höchstens einmal");
        return treffer.Count == 0 ? null : treffer[0][(name.Length + 1)..].Trim();
    }

    [Test]
    public static void Setup_Iss_AppIdFestOhneAdminrechteImBenutzerordner()
    {
        var skript = Lesen("installer", "TacRelay.iss");
        var a = Abschnitte(skript);
        Pruefe.Wahr(skript.Contains("#define AppGuid    \"" + AppId + "\"", StringComparison.Ordinal), "AppGuid ist " + AppId);
        Pruefe.Gleich("{{#AppGuid}", Anweisung(a, "AppId"), "AppId aus AppGuid");
        Pruefe.Wahr(Lesen("tools", "setup-bauen.ps1").Contains("$appId = '" + AppId + "'", StringComparison.Ordinal),
            "setup-bauen.ps1 prüft dieselbe AppId");
        Pruefe.Falsch(skript.Contains(AppIdAlt, StringComparison.OrdinalIgnoreCase) || Lesen("tools", "setup-bauen.ps1").Contains(AppIdAlt, StringComparison.OrdinalIgnoreCase),
            "die alte Kennung steht weder im Setup noch im Bauwerkzeug");

        Pruefe.Gleich("lowest", Anweisung(a, "PrivilegesRequired"), "ohne Administratorrechte");
        // "Für alle Benutzer" ist bewusst nicht erlaubt (Begründung im Kopf des Skripts): kein Dialog, kein /ALLUSERS.
        Pruefe.Gleich(null, Anweisung(a, "PrivilegesRequiredOverridesAllowed"), "kein Umschalten auf alle Benutzer");
        Pruefe.Gleich(@"{autopf}\{#Ordner}", Anweisung(a, "DefaultDirName"), "Ziel %LOCALAPPDATA%\\Programs\\JG-TacRelay");
        Pruefe.Wahr(skript.Contains("#define Name       \"JG TacRelay\"", StringComparison.Ordinal), "Name JG TacRelay");
        Pruefe.Wahr(skript.Contains("#define Ordner     \"JG-TacRelay\"", StringComparison.Ordinal), "Ordner JG-TacRelay, ohne Leerzeichen");
        Pruefe.Wahr(skript.Contains("#define ExeName    \"TacRelay.exe\"", StringComparison.Ordinal), "Programmdatei TacRelay.exe");
        Pruefe.Gleich("{#Name}", Anweisung(a, "DefaultGroupName"), "Startmenügruppe JG TacRelay");
        Pruefe.Gleich("{#Maintainer}", Anweisung(a, "AppPublisher"), "Herausgeber ist der Maintainer (A59), nicht die Gilde und kein bürgerlicher Name");
        Pruefe.Wahr(a["Languages"].Count == 1 && a["Languages"][0].Contains(@"compiler:Languages\German.isl", StringComparison.Ordinal),
            "nur Deutsch: " + string.Join(" | ", a["Languages"]));
        Pruefe.Wahr(Anweisung(a, "WizardStyle")?.StartsWith("modern", StringComparison.Ordinal) == true, "moderner Assistent");
        Pruefe.Gleich("{#Symbol}", Anweisung(a, "SetupIconFile"), "eigenes Symbol für das Setup");
        Pruefe.Gleich(@"{app}\{#ExeName}", Anweisung(a, "UninstallDisplayIcon"), "Symbol unter Apps");
        Pruefe.Gleich("yes", Anweisung(a, "CloseApplications"), "Restart Manager als drittes Netz");
        Pruefe.Gleich("no", Anweisung(a, "RestartApplications"), "nichts neu starten");
        // Laufendes JG TacRelay: derselbe Mutex, den jede Instanz hält (Einzelinstanz.LaeuftMarke), wörtlich gleich, denn
        // Windows vergleicht Mutexnamen mit Groß und klein.
        Pruefe.Gleich(Einzelinstanz.LaeuftMarke, Anweisung(a, "AppMutex"), "AppMutex gleich der Marke des Programms");
        Pruefe.Gleich("no", Anweisung(a, "DisableDirPage"), "die Ordnerseite überspringt der [Code] selbst");
        Pruefe.Gleich("yes", Anweisung(a, "AlwaysShowDirOnReadyPage"), "Bereit nennt den Ordner auch ohne Ordnerseite");
        Pruefe.Wahr(a["Code"].Any(z => z.Contains("function InitializeUninstall", StringComparison.Ordinal))
            && a["Code"].Any(z => z.Contains("Result := NichtsLaeuft(", StringComparison.Ordinal)), "Deinstallieren prüft laufende Prozesse");
        Pruefe.Wahr(a["Code"].Any(z => z.Contains("CurPageID = wpReady", StringComparison.Ordinal)), "Installieren prüft laufende Prozesse auf Bereit");
        Pruefe.Wahr(skript.Contains("https://dotnet.microsoft.com/download/dotnet/10.0", StringComparison.Ordinal), "Laufzeit: Seite von Microsoft genannt");
        Pruefe.Wahr(skript.Contains("SmartScreen", StringComparison.Ordinal), "Abschlusstext nennt SmartScreen");

        // Startmenü: JG TacRelay, Probefunk und Deinstallieren; Desktop nur als Aufgabe, ab Werk aus.
        var icons = a["Icons"];
        Pruefe.Wahr(icons.Any(z => z.StartsWith(@"Name: ""{group}\{#Name}""; Filename: ""{app}\{#ExeName}""", StringComparison.Ordinal)), "Startmenü JG TacRelay");
        Pruefe.Wahr(icons.Any(z => z.StartsWith(@"Name: ""{group}\{#Name} Probefunk""; Filename: ""{app}\probefunk\probefunk-starten.cmd""", StringComparison.Ordinal)),
            "Startmenü Probefunk");
        Pruefe.Wahr(icons.Any(z => z.Contains(@"Filename: ""{uninstallexe}""", StringComparison.Ordinal)), "Startmenü Deinstallieren");
        Pruefe.Wahr(icons.Any(z => z.StartsWith(@"Name: ""{autodesktop}\", StringComparison.Ordinal) && z.Contains("Tasks: desktopsymbol", StringComparison.Ordinal)),
            "Desktop nur mit Aufgabe");
        Pruefe.Wahr(a["Tasks"].Any(z => z.Contains("\"desktopsymbol\"", StringComparison.Ordinal) && z.Contains("Flags: unchecked", StringComparison.Ordinal)),
            "Desktop-Symbol ab Werk aus");
    }

    /// <summary>
    /// .NET Desktop Runtime (0.13.2, Sarevoc 25.11.2026): Das Setup bettet den Installer von Microsoft ein, nie nach {app}, und
    /// startet ihn nur, wenn die Laufzeit fehlt, vor dem Kopieren und mit der Abfrage von Windows statt eigener Rechte. Ob es
    /// geklappt hat, sagt der Blick in den Ordner; scheitert es, geht das Setup weiter und nennt die Seite von Microsoft. Aktuell
    /// hält die Laufzeit Windows Update, das sagt der Abschlusstext. Das Bauwerkzeug prüft die Signatur von Microsoft.
    /// </summary>
    [Test]
    public static void Setup_Iss_LaufzeitNurWennSieFehlt()
    {
        var skript = Lesen("installer", "TacRelay.iss");
        var a = Abschnitte(skript);
        Pruefe.Wahr(skript.Contains("#ifndef LaufzeitInstaller", StringComparison.Ordinal) && skript.Contains("#ifndef LaufzeitFassung", StringComparison.Ordinal),
            "ohne Installer kein Bau");
        Pruefe.Wahr(a["Files"].Contains("Source: \"{#LaufzeitInstaller}\"; DestDir: \"{tmp}\"; Flags: dontcopy"), "Installer nur eingebettet, nie nach {app}");
        var code = string.Join("\n", a["Code"]);
        Pruefe.Wahr(code.Contains("ExtractTemporaryFile('{#LaufzeitDatei}');", StringComparison.Ordinal), "erst bei Bedarf entpackt");
        Pruefe.Wahr(code.Contains("ShellExec('', Installer, Schalter, '', SW_SHOW, ewWaitUntilTerminated, Code)", StringComparison.Ordinal),
            "ShellExec, damit Windows nach Administratorrechten fragen kann");
        Pruefe.Wahr(code.Contains("'/install /passive /norestart'", StringComparison.Ordinal) && code.Contains("'/install /quiet /norestart'", StringComparison.Ordinal),
            "sichtbar mit Fortschritt, still nur im stillen Setup, nie ein Neustart");
        Pruefe.Wahr(Muster(code, @"if \(Result = ''\) and not LaufzeitDa\(\) then\s+LaufzeitInstallieren\(\);"), "nur wenn sie fehlt, nach dem Entfernen der alten Installation");
        Pruefe.Wahr(Muster(code, @"else if not LaufzeitDa\(\) then"), "Erfolg heißt: danach im Ordner");
        Pruefe.Falsch(code.Contains("CustomMessage('LaufzeitFehlt')", StringComparison.Ordinal), "keine Frage mehr, ob ohne Laufzeit weiter");
        Pruefe.Wahr(skript.Contains("Updates für andere Microsoft-Produkte erhalten", StringComparison.Ordinal), "Abschlusstext: Windows Update hält sie aktuell");
        Pruefe.Gleich("lowest", Anweisung(a, "PrivilegesRequired"), "das Setup selbst bleibt ohne Administratorrechte");
        var bauen = Lesen("tools", "setup-bauen.ps1");
        Pruefe.Wahr(bauen.Contains("Get-AuthenticodeSignature -LiteralPath $Laufzeit", StringComparison.Ordinal) && bauen.Contains("O=Microsoft Corporation", StringComparison.Ordinal),
            "Bauwerkzeug prüft die Signatur von Microsoft");
        Pruefe.Wahr(bauen.Contains("\"/DLaufzeitInstaller=$Laufzeit\"", StringComparison.Ordinal), "Bauwerkzeug gibt den Installer an ISCC");
    }

    [Test]
    public static void Setup_Iss_KeineRegistrySchreibzugriffe()
    {
        var a = Abschnitte(Lesen("installer", "TacRelay.iss"));
        Pruefe.Falsch(a.ContainsKey("Registry"), "kein [Registry]-Abschnitt; die Einträge für Apps legt Inno selbst an");
        Pruefe.Falsch(Anweisung(a, "ChangesEnvironment") is "yes" or "true", "keine Umgebungsvariablen");
        Pruefe.Falsch(Anweisung(a, "ChangesAssociations") is "yes" or "true", "keine Dateizuordnungen");
        var code = string.Join("\n", a["Code"]);
        Pruefe.Gleich(null, RegistrySchreiben(code), "[Code] schreibt nicht in die Registry");
        Pruefe.Wahr(code.Contains("RegQueryStringValue(", StringComparison.Ordinal), "[Code] liest (Laufzeitprüfung)");
        // Einstellungen bleiben beim Deinstallieren: nichts löscht außerhalb dessen, was Inno selbst angelegt hat.
        Pruefe.Falsch(a.ContainsKey("UninstallDelete") || a.ContainsKey("InstallDelete"), "kein [UninstallDelete]/[InstallDelete]");
        Pruefe.Falsch(Regex.IsMatch(code, @"\b(DelTree|DeleteFile|RemoveDir)\s*\(", RegexOptions.None, TimeSpan.FromSeconds(1)), "[Code] löscht nichts");

        // Gegenprobe: ohne diese Treffer bewiese "keine Befunde" oben nichts.
        Pruefe.Gleich("RegWriteStringValue", RegistrySchreiben("RegWriteStringValue(HKCU, 'Software\\X', 'a', 'b');"), "Schreiben erkannt");
        Pruefe.Gleich("RegDeleteKeyIncludingSubkeys", RegistrySchreiben("x := RegDeleteKeyIncludingSubkeys(HKCU, 'Software\\X');"), "Löschen erkannt");
        Pruefe.Gleich(null, RegistrySchreiben("{ RegWriteStringValue( nur im Kommentar }"), "Kommentar zählt nicht");
    }

    /// <summary>Erster Aufruf, der die Registry verändert, oder null. Kommentare in { } und nach // zählen nicht.</summary>
    private static string? RegistrySchreiben(string code)
    {
        var ohne = Regex.Replace(Regex.Replace(code, @"\{[^}]*\}", " ", RegexOptions.None, TimeSpan.FromSeconds(1)), "//.*", " ",
            RegexOptions.None, TimeSpan.FromSeconds(1));
        var m = RegistryAufruf().Match(ohne);
        return m.Success ? m.Groups[1].Value : null;
    }

    [Test]
    public static void Setup_Iss_PfadeZitiert()
    {
        var a = Abschnitte(Lesen("installer", "TacRelay.iss"));
        var befunde = new List<string>();
        var geprueft = 0;
        foreach (var abschnitt in new[] { "Files", "Icons", "Run", "UninstallRun", "Dirs" })
        {
            if (!a.TryGetValue(abschnitt, out var zeilen)) continue;
            foreach (var zeile in zeilen)
            {
                foreach (var (schluessel, wert) in Parameter(zeile))
                {
                    if (!PfadParameter.Contains(schluessel)) continue;
                    geprueft++;
                    if (!Zitiert(wert)) befunde.Add($"[{abschnitt}] {schluessel}: {wert}");
                }
            }
        }
        Pruefe.Wahr(geprueft >= 10, "Pfadangaben gefunden: " + geprueft);
        Pruefe.Gleich(0, befunde.Count, "nicht zitiert: " + string.Join(" | ", befunde));

        // Gegenprobe: ein Pfad mit Leerzeichen ohne Anführungszeichen fällt auf.
        var falsch = Parameter(@"Name: ""{group}\X""; Filename: {app}\Mein Programm.exe; WorkingDir: ""{app}""").ToList();
        Pruefe.Wahr(falsch.Any(p => p.Schluessel == "Filename" && !Zitiert(p.Wert)), "unzitierter Pfad erkannt");

        // Die Startdatei des Probefunks: jeder Pfad mit %~dp0, %TR_...% oder %LOCALAPPDATA% steht in Anführungszeichen.
        var cmd = Lesen("installer", "probefunk-starten.cmd").Split('\n').Select(z => z.TrimEnd('\r'))
            .Where(z => !z.TrimStart().StartsWith("rem", StringComparison.OrdinalIgnoreCase)).ToList();
        var offen = cmd.Where(z => CmdPfad().IsMatch(Regex.Replace(z, "\"[^\"]*\"", "", RegexOptions.None, TimeSpan.FromSeconds(1)))).ToList();
        Pruefe.Gleich(0, offen.Count, "probefunk-starten.cmd, Pfad ohne Anführungszeichen: " + string.Join(" | ", offen));
    }

    private static readonly HashSet<string> PfadParameter = new(StringComparer.OrdinalIgnoreCase)
    {
        "Source", "DestDir", "DestName", "Filename", "Name", "WorkingDir", "IconFilename", "Parameters",
    };

    private static bool Zitiert(string wert) => wert.Length >= 2 && wert[0] == '"' && wert[^1] == '"';

    /// <summary>Zerlegt eine Zeile wie <c>Name: "a"; Filename: "b"; Flags: x</c> an den Semikolons außerhalb von Anführungszeichen.</summary>
    internal static IEnumerable<(string Schluessel, string Wert)> Parameter(string zeile)
    {
        var teile = new List<string>();
        var sb = new StringBuilder();
        var inAnfuehrung = false;
        foreach (var c in zeile)
        {
            if (c == '"') inAnfuehrung = !inAnfuehrung;
            if (c == ';' && !inAnfuehrung)
            {
                teile.Add(sb.ToString());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        teile.Add(sb.ToString());
        foreach (var t in teile)
        {
            var doppelpunkt = t.IndexOf(':', StringComparison.Ordinal);
            if (doppelpunkt <= 0) continue;
            yield return (t[..doppelpunkt].Trim(), t[(doppelpunkt + 1)..].Trim());
        }
    }

    [Test]
    public static void Setup_Iss_VorhandeneInstallation_AktualisierenReparierenNeu()
    {
        var a = Abschnitte(Lesen("installer", "TacRelay.iss"));
        var code = string.Join("\n", a["Code"]);

        // Gefunden über den Uninstall-Eintrag, den Inno zur festen AppId selbst anlegt, nur lesend; HKCU, dazu HKLM in beiden
        // Ansichten (dort nur Hinweis, denn ohne Administratorrechte lässt sich jene Installation nicht anfassen).
        Pruefe.Wahr(code.Contains(@"UninstallSchluessel = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuid}_is1';", StringComparison.Ordinal),
            "Schlüssel aus der AppId");
        foreach (var wert in new[] { "InstallLocation", "DisplayVersion", "UninstallString" })
            Pruefe.Wahr(code.Contains($"RegQueryStringValue(Wurzel, UninstallSchluessel, '{wert}'", StringComparison.Ordinal), "liest " + wert);
        foreach (var wurzel in new[] { "HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE_64", "HKEY_LOCAL_MACHINE_32" })
            Pruefe.Wahr(code.Contains($"EintragLesen({wurzel},", StringComparison.Ordinal), "sucht unter " + wurzel);
        Pruefe.Wahr(Block(code, "function InitializeSetup()").Contains("CustomMessage('FuerAlleGefunden')", StringComparison.Ordinal), "HKLM-Fund: Hinweis und Frage");

        // Drei Wahlen, Knöpfe in der Reihenfolge der Konstanten, auf einer eigenen Seite gleich am Anfang.
        Pruefe.Wahr(Muster(code, @"WahlAktualisieren = 0;\s+WahlReparieren = 1;\s+WahlNeu = 2;"), "Konstanten der Wahlen");
        var knoepfe = Regex.Matches(code, @"VorhandenSeite\.Add\(CustomMessage\('(\w+)'\)\)", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value);
        Pruefe.Gleich("WahlAktualisieren,WahlReparieren,WahlNeu", string.Join(",", knoepfe), "Knöpfe");
        var wizard = Block(code, "procedure InitializeWizard()");
        Pruefe.Wahr(Muster(wizard, @"if AlterOrdner = '' then\s+Exit;"), "die Seite nur mit vorhandener Installation");
        Pruefe.Wahr(wizard.Contains("CreateInputOptionPage(wpWelcome,", StringComparison.Ordinal) && wizard.Contains(", True, False);", StringComparison.Ordinal),
            "Optionsknöpfe (Exclusive) ohne Liste, vor der Ordnerseite");

        // Vorwahl: gleiche Fassung Reparieren, neuer oder unbekannt Aktualisieren, älter nichts (nur fett der Hinweis).
        Pruefe.Wahr(code.Contains("Vergleich := ComparePackedVersion(Eigene, Installiert)", StringComparison.Ordinal), "Setup gegen installiert");
        // Beim Rückschritt steht der Text fett; die Seite misst ihn neu und schiebt die Knöpfe nach unten, sonst schnitte er ab.
        Pruefe.Wahr(Muster(wizard, @"if FassungBekannt and \(Vergleich < 0\) then\s+begin\s+(\{[^}]*\}\s+)?" +
            @"VorhandenSeite\.SubCaptionLabel\.Font\.Style := \[fsBold\];\s+" +
            @"Mehr := WizardForm\.AdjustLabelHeight\(VorhandenSeite\.SubCaptionLabel\);\s+" +
            @"WizardForm\.IncTopDecHeight\(VorhandenSeite\.CheckListBox, Mehr\);\s+end\s+" +
            @"else if FassungBekannt and \(Vergleich = 0\) then\s+VorhandenSeite\.SelectedValueIndex := WahlReparieren\s+" +
            @"else\s+VorhandenSeite\.SelectedValueIndex := WahlAktualisieren;"), "Vorwahl je nach Fassung");

        // Rückschritt nur nach Bestätigung, Vorgabe Nein, auch bei einer stillen Installation; ohne Wahl kein Weiter.
        var weiter = Block(code, "function VorhandenWeiter()");
        Pruefe.Wahr(Muster(weiter, @"if Wahl < 0 then\s+begin\s+SuppressibleMsgBox\(CustomMessage\('WahlFehlt'\)"), "ohne Wahl kein Weiter");
        Pruefe.Wahr(Muster(weiter, @"if FassungBekannt and \(Vergleich < 0\) then\s+if SuppressibleMsgBox\(FmtMessage\(CustomMessage\('AeltereFassung'\)[^;]*MB_YESNO or MB_DEFBUTTON2, IDNO\) <> IDYES then"),
            "ältere Fassung: Warnung mit Vorgabe Nein");
        Pruefe.Wahr(Muster(weiter, @"if Wahl <> WahlNeu then\s+WizardForm\.DirEdit\.Text := AlterOrdner;"), "Aktualisieren und Reparieren: genau der gefundene Ordner");
        Pruefe.Wahr(Muster(Block(code, "function NextButtonClick("), @"if CurPageID = VorhandenSeite\.ID then\s+begin\s+Result := VorhandenWeiter\(\);"),
            "die Seite übernimmt die Wahl beim Weiter");

        // Ordnerseite: bei Aktualisieren und Reparieren übersprungen, bei Neu installieren und ohne Vorgänger gezeigt.
        Pruefe.Wahr(Muster(Block(code, "function ShouldSkipPage("), @"if PageID = wpSelectDir then\s+Result := \(Wahl = WahlAktualisieren\) or \(Wahl = WahlReparieren\);"),
            "Ordnerseite übersprungen");
        Pruefe.Wahr(Muster(Block(code, "function InitializeSetup()"), @"Wahl := -1;"), "ohne Vorgänger keine Wahl, die Ordnerseite erscheint");

        // Neu installieren: erst der alte Deinstaller, still. Der Pfad kommt ohne Anführungszeichen aus dem Eintrag und geht als
        // Filename an Exec, das sie selbst setzt (Inno-Hilfe); eigene Anführungszeichen oder ein Gesamtaufruf über '>' kämen doppelt.
        Pruefe.Wahr(code.Contains("Deinstaller := RemoveQuotes(Trim(Deinstaller))", StringComparison.Ordinal), "Anführungszeichen aus UninstallString entfernt");
        var entfernen = Block(code, "function AlteEntfernen()");
        var exec = entfernen.IndexOf("Exec(AlterDeinstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code)", StringComparison.Ordinal);
        Pruefe.Wahr(exec > 0, "alter Deinstaller still, Pfad als eigener Parameter");
        Pruefe.Falsch(Muster(entfernen, @"AddQuotes|'""'|Exec\('>'"), "keine eigenen Anführungszeichen um den Pfad");
        var ortsPruefung = entfernen.IndexOf("Unter(ExtractFileDir(AlterDeinstaller), AlterOrdner)", StringComparison.Ordinal);
        Pruefe.Wahr(ortsPruefung > 0 && ortsPruefung < exec, "nur ein Deinstaller aus dem gefundenen Ordner");
        Pruefe.Wahr(entfernen.IndexOf("RegKeyExists(HKEY_CURRENT_USER, UninstallSchluessel)", exec, StringComparison.Ordinal) > exec,
            "wartet danach, bis der Eintrag weg ist");
        Pruefe.Wahr(Muster(entfernen, @"while DirExists\(AlterOrdner\) and not OrdnerHatInhalt\(AlterOrdner\) and \(Runden < 8\) do"),
            "wartet kurz, bis der Deinstaller den leeren alten Ordner entfernt hat (Neu installieren in denselben Ordner)");
        Pruefe.Wahr(Muster(Block(code, "function PrepareToInstall("), @"if Wahl = WahlNeu then\s+Result := AlteEntfernen\(\);"), "nur bei Neu installieren");
        Pruefe.Wahr(Muster(Block(code, "function NextButtonClick("), @"\(Wahl = WahlNeu\)[^;]*\s+Result := NichtsLaeuft\(AlterOrdner\);"),
            "beim Neu installieren darf auch aus dem alten Ordner nichts laufen");
    }

    /// <summary>Innos eigene Meldungen, die das Skript über {cm:...} nutzt (aus Languages\German.isl).</summary>
    private static readonly string[] InnoMeldungen = ["AdditionalIcons", "UninstallProgram"];

    /// <summary>Die Sie-Texte aus German.isl, die in diesem Setup erscheinen können; sie stehen im Skript per du.</summary>
    private static readonly string[] DuPflicht =
    [
        "SetupAppRunningError", "UninstallAppRunningError", "ExitSetupMessage", "BrowseDialogLabel", "SelectDirBrowseLabel", "InvalidPath",
        "InvalidDrive", "CannotInstallToUNCPath", "DiskSpaceWarning", "SelectTasksLabel2", "ReadyLabel1", "ReadyLabel2a", "ReadyLabel2b",
        "InstallingLabel", "FinishedLabel", "FinishedLabelNoIcons", "ClickFinish", "SetupAborted", "ConfirmUninstall", "UninstallStatusLabel",
        "UninstalledAll", "UninstalledMost", "WindowsVersionNotSupported", "PreviousInstallNotCompleted", "CannotContinue",
        "PrepareToInstallNeedsRestart", "FinishedRestartLabel", "FinishedRestartMessage", "UninstalledAndNeedsRestart", "ErrorRestartingComputer",
    ];

    [Test]
    public static void Setup_Iss_TexteDuVollstaendigMitPlatzhaltern()
    {
        var skript = Lesen("installer", "TacRelay.iss");
        var a = Abschnitte(skript);
        var eigene = Meldungen(a["CustomMessages"], "de.");
        var ueberschrieben = Meldungen(a["Messages"], "");
        var code = string.Join("\n", a["Code"]);

        // Jede benutzte Meldung ist definiert und jede definierte benutzt. Inno merkt eine fehlende erst zur Laufzeit.
        var imCode = Regex.Matches(code, @"CustomMessage\('(\w+)'\)", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var alsKonstante = Regex.Matches(skript, @"\{cm:(\w+)", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Pruefe.Wahr(imCode.Count >= 25, "Meldungen im [Code]: " + imCode.Count);
        var fehlend = imCode.Concat(alsKonstante).Where(k => !eigene.ContainsKey(k) && !InnoMeldungen.Contains(k)).ToList();
        Pruefe.Gleich(0, fehlend.Count, "nicht definiert: " + string.Join(", ", fehlend));
        var unbenutzt = eigene.Keys.Where(k => !imCode.Contains(k) && !alsKonstante.Contains(k)).ToList();
        Pruefe.Gleich(0, unbenutzt.Count, "definiert, aber nie benutzt: " + string.Join(", ", unbenutzt));

        // Platzhalter: FmtMessage bekommt mindestens so viele Werte, wie die Meldung %1, %2 ... nennt; ohne FmtMessage keine.
        var geprueft = 0;
        foreach (Match m in FmtAufruf().Matches(code))
        {
            var name = m.Groups[1].Value;
            var werte = m.Groups[2].Value.Split(',').Length;
            var hoechster = Hoechster(eigene[name]);
            Pruefe.Wahr(hoechster >= 1 && hoechster <= werte, $"{name}: höchster Platzhalter %{hoechster} bei {werte} Werten");
            geprueft++;
        }
        Pruefe.Wahr(geprueft >= 15, "FmtMessage-Aufrufe: " + geprueft);
        foreach (Match m in Regex.Matches(code, @"(?<!FmtMessage\()CustomMessage\('(\w+)'\)", RegexOptions.None, TimeSpan.FromSeconds(1)))
            Pruefe.Gleich(0, Hoechster(eigene[m.Groups[1].Value]), m.Groups[1].Value + " ohne FmtMessage, also ohne %1");

        // Per du: nirgends Sie, Ihr oder Ihnen; die Sie-Texte von Inno, die hier erscheinen können, sind überschrieben.
        var sie = SieForm();
        Pruefe.Wahr(sie.IsMatch("Bitte schließen Sie jetzt alle Instanzen") && !sie.IsMatch("Schließe bitte alle Fenster"), "Gegenprobe Sie-Form");
        foreach (var (name, text) in eigene.Concat(ueberschrieben))
            Pruefe.Falsch(sie.IsMatch(text), $"{name} siezt: {text}");
        foreach (var name in DuPflicht)
            Pruefe.Wahr(ueberschrieben.ContainsKey(name), "per du überschrieben: " + name);

        // Gegen die Sprachdatei von Inno (nur lesen): nur Schlüssel, die es dort gibt, keine zusätzlichen Platzhalter, und jeder
        // Pflichtschlüssel siezt dort tatsächlich.
        const string Isl = @"C:\Claude\Eigene_Programme\innosetup\Languages\German.isl";
        if (File.Exists(Isl))
        {
            var sprachdatei = Abschnitte(File.ReadAllText(Isl));
            var original = Meldungen(sprachdatei["Messages"], "");
            foreach (var (name, text) in ueberschrieben)
            {
                Pruefe.Wahr(original.ContainsKey(name), "Schlüssel gibt es in German.isl: " + name);
                Pruefe.Wahr(Hoechster(text) <= Hoechster(original[name]), $"{name}: keine Platzhalter über die von German.isl hinaus");
            }
            foreach (var name in DuPflicht) Pruefe.Wahr(sie.IsMatch(original[name]), "German.isl siezt in " + name);
            var innoEigene = Meldungen(sprachdatei["CustomMessages"], "");
            foreach (var name in InnoMeldungen) Pruefe.Wahr(innoEigene.ContainsKey(name), "German.isl kennt " + name);
        }
        else
        {
            Console.WriteLine("  German.isl nicht gefunden (" + Isl + "), Abgleich mit der Sprachdatei ausgelassen.");
        }

        // Zeichen: gerade Anführungszeichen, als Striche nur - und _, keine unsichtbaren Zeichen (wie tools\text-pruefen.ps1).
        foreach (var datei in new[] { "TacRelay.iss", "probefunk-starten.cmd" })
        {
            var text = Lesen("installer", datei);
            var fremd = text.Where(c => "\u2013\u2014\u2212\u2022\u201E\u201C\u201D\u201A\u2018\u2019\u2026\u00A0\u202F\u200B\u200C\u200D\uFEFF\u00AD".Contains(c)).ToList();
            Pruefe.Gleich(0, fremd.Count, datei + ": typografische oder unsichtbare Zeichen " + string.Join(" ", fremd.Select(c => "U+" + ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture))));
        }
    }

    /// <summary>Meldungen <c>schluessel=text</c> eines Abschnitts; mit Vorsatz (etwa "de.") nur die mit Vorsatz, ohne ihn.</summary>
    private static Dictionary<string, string> Meldungen(List<string> zeilen, string vorsatz)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var zeile in zeilen)
        {
            var gleich = zeile.IndexOf('=', StringComparison.Ordinal);
            if (gleich <= 0 || !zeile.StartsWith(vorsatz, StringComparison.Ordinal)) continue;
            d[zeile[vorsatz.Length..gleich].Trim()] = zeile[(gleich + 1)..];
        }
        return d;
    }

    /// <summary>Höchster Platzhalter %1 bis %9 einer Meldung, 0 ohne. %n ist ein Zeilenumbruch, kein Platzhalter.</summary>
    private static int Hoechster(string text) =>
        Regex.Matches(text, @"%(\d)", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value[0] - '0').DefaultIfEmpty(0).Max();

    private static bool Muster(string text, string muster) => Regex.IsMatch(text, muster, RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Eine Funktion des [Code] von ihrem Kopf bis zum nächsten Kopf.</summary>
    private static string Block(string code, string kopf)
    {
        var i = code.IndexOf(kopf, StringComparison.Ordinal);
        Pruefe.Wahr(i >= 0, "im [Code]: " + kopf);
        var ende = FunktionsKopf().Match(code, i + kopf.Length);
        return ende.Success ? code[i..ende.Index] : code[i..];
    }

    /// <summary>
    /// Seite "Deine Namen" (A42) und Admin-Setup (A45): Die Seite erscheint nur bei einer neuen Installation ohne
    /// Einstellungen, prüft die Länge wie der Server und legt anmeldung.json mit genau den Schlüsseln ab, die der Client liest.
    /// Der Verwaltungsteil kommt nur mit /DVerwaltung=1; setup-bauen.ps1 prüft beim normalen Setup, dass keiner drin ist.
    /// </summary>
    [Test]
    public static void Setup_Iss_NamenSeiteUndVerwaltungsteil()
    {
        var skript = Lesen("installer", "TacRelay.iss");
        var a = Abschnitte(skript);
        var code = string.Join("\n", a["Code"]);

        var wizard = Block(code, "procedure InitializeWizard()");
        Pruefe.Wahr(wizard.Contains("NamenSeite := CreateInputQueryPage(wpSelectDir,", StringComparison.Ordinal)
            && Regex.Count(wizard, @"NamenSeite\.Add\(CustomMessage\('Namen(Sc|Discord)'\), False\);", RegexOptions.None, TimeSpan.FromSeconds(1)) == 2,
            "Seite nach der Ordnerseite, zwei Felder ohne Sternchen");
        Pruefe.Wahr(wizard.IndexOf("CreateInputQueryPage", StringComparison.Ordinal) < wizard.IndexOf("if AlterOrdner = '' then", StringComparison.Ordinal),
            "angelegt auch ohne vorhandene Installation");
        Pruefe.Wahr(Muster(Block(code, "function ShouldSkipPage("),
            @"if PageID = NamenSeite\.ID then\s+begin\s+Result := \(Wahl = WahlAktualisieren\) or \(Wahl = WahlReparieren\) or EinstellungenDa\(\);"),
            "nicht bei Aktualisieren, Reparieren oder vorhandenen Einstellungen");
        // Der Dateiname kommt aus dem Client, nicht als Text: Bis 01.10.2026 stand hier "einstellungen.json", eine Datei, die
        // kein Client je schrieb; die Seite erschien dann trotz vorhandener Einstellungen (Gegenprüfung 01.10.2026).
        var da = Block(code, "function EinstellungenDa()");
        Pruefe.Wahr(da.Contains(@"{localappdata}\JG-TacRelay\" + Funk.Client.Ablage.EinstellungsSpeicher.Dateiname + "')", StringComparison.Ordinal)
            && da.Contains(@"{localappdata}\JG-TacRelay\" + Funk.Client.Ablage.Anmeldedatei.Dateiname + "')", StringComparison.Ordinal), "Einstellungen oder Namen schon da");
        Pruefe.Gleich(2, Regex.Count(da, @"FileExists\(", RegexOptions.None, TimeSpan.FromSeconds(1)), "genau diese zwei Dateien");
        Pruefe.Wahr(Muster(Block(code, "function NextButtonClick("), @"if CurPageID = NamenSeite\.ID then\s+begin\s+Result := NamenWeiter\(\);"), "Weiter prüft die Namen");

        // Länge wie der Server (Namensregeln), beide oder keiner.
        var gut = Block(code, "function NameGut(");
        Pruefe.Wahr(gut.Contains("N := Zeichenzahl(Name);", StringComparison.Ordinal)
            && gut.Contains($"(N >= {Funk.Protokoll.Identitaet.Namensregeln.Min}) and (N <= {Funk.Protokoll.Identitaet.Namensregeln.Max})", StringComparison.Ordinal),
            "2 bis 32 Zeichen wie der Server");
        Pruefe.Wahr(gut.Contains("if Ord(Name[I]) < 32 then", StringComparison.Ordinal), "keine Steuerzeichen");
        // Der Server zählt Unicode-Skalarwerte, Inno Setup UTF-16-Einheiten: Ein Emoji ist ein Zeichen, nicht zwei (Befund 01.10.2026).
        var zahl = Block(code, "function Zeichenzahl(");
        Pruefe.Wahr(zahl.Contains("(Ord(S[I]) < $DC00) or (Ord(S[I]) > $DFFF)", StringComparison.Ordinal), "das zweite Glied eines Ersatzpaars zählt nicht");
        static int WieDasSetup(string s) => s.Count(z => z < 0xDC00 || z > 0xDFFF);
        var rakete = char.ConvertFromUtf32(0x1F680);
        foreach (var name in new[] { "Sarevoc", "Sarevoc/Otto", "Brändy", string.Concat(Enumerable.Repeat(rakete, 31)) + "A", rakete + "B" })
            Pruefe.Gleich(name.EnumerateRunes().Count(), WieDasSetup(name), "Zählung wie der Server: " + name.Length + " UTF-16-Einheiten");
        Pruefe.Wahr(Funk.Protokoll.Identitaet.Zeichenregeln.IstText(string.Concat(Enumerable.Repeat(rakete, 31)) + "A", 2, 32), "der Server nimmt 32 Zeichen mit Emoji");
        var weiter = Block(code, "function NamenWeiter()");
        Pruefe.Wahr(Muster(weiter, @"if \(Sc = ''\) and \(Dc = ''\) then\s+Exit;") && weiter.Contains("CustomMessage('NamenEiner')", StringComparison.Ordinal),
            "beide leer geht, einer allein nicht");

        // Die Datei: Ort, Schlüssel und Maskierung wie Anmeldedatei.Lesen sie erwartet; UTF-8 (mit Marke, die der Client überliest).
        var ablegen = Block(code, "procedure NamenAblegen()");
        Pruefe.Wahr(ablegen.Contains(@"Zeilen[0] := '{""scName"": ""' + JsonText(Sc) + '"", ""discordName"": ""' + JsonText(Dc) + '""}';", StringComparison.Ordinal),
            "Schlüssel scName und discordName");
        Pruefe.Wahr(ablegen.Contains(@"SaveStringsToUTF8File(Ordner + '\" + Funk.Client.Ablage.Anmeldedatei.Dateiname + "', Zeilen, False);", StringComparison.Ordinal)
            && ablegen.Contains(@"ExpandConstant('{localappdata}\JG-TacRelay')", StringComparison.Ordinal), "neben die Einstellungen");
        Pruefe.Wahr(Muster(ablegen, @"if \(Sc = ''\) or \(Dc = ''\) or EinstellungenDa\(\) then\s+Exit;"), "nur mit beiden Namen und ohne Einstellungen");
        Pruefe.Wahr(Block(code, "function JsonText(").Contains(@"if (S[I] = '""') or (S[I] = '\') then", StringComparison.Ordinal), "Anführungszeichen und Rückstrich maskiert");
        Pruefe.Wahr(Muster(Block(code, "procedure CurStepChanged("), @"if CurStep = ssPostInstall then\s+NamenAblegen\(\);"), "nach dem Kopieren");

        // Gegenprobe des Vertrags: So maskiert, wie JsonText es tut, liest der Client die Namen zurück.
        static string Maskieren(string s) => s.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        using var ordner = new Oberflaeche.Testordner();
        var datei = ordner.Datei(Funk.Client.Ablage.Anmeldedatei.Dateiname);
        File.WriteAllBytes(datei, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\"scName\": \"" + Maskieren("Brändy") + "\", \"discordName\": \"" + Maskieren(@"Brandy\""Lea""") + "\"}")]);
        Pruefe.Gleich(("Brändy", @"Brandy\""Lea"""), Funk.Client.Ablage.Anmeldedatei.Lesen(datei)!.Value, "Rückweg über den Client");

        // Admin-Setup: Hinweis nur mit /DVerwaltung=1. Einen alten Verwaltungsteil löscht auch das normale Setup nicht
        // (Setup_Iss_KeineRegistrySchreibzugriffe); der Client lädt ihn nicht, wenn er nicht passt, und sagt es.
        Pruefe.Wahr(Muster(skript, @"#ifdef Verwaltung\s+Result := Result \+ NewLine \+ NewLine \+ CustomMessage\('VerwaltungHinweis'\);\s+#endif"), "Hinweis im Admin-Setup");
        Pruefe.Gleich("Dieses Setup enthält den Verwaltungsteil. Er bleibt gesperrt, bis der Eigentümer dich zum Admin ernennt.",
            Meldungen(a["CustomMessages"], "de.")["VerwaltungHinweis"], "Text des Hinweises");
        Pruefe.Wahr(Funk.Client.Dienste.Verwaltungslader.PasstNicht.Contains("Admin-Setup", StringComparison.Ordinal), "der Client sagt, was fehlt");

        var bauen = Lesen("tools", "setup-bauen.ps1");
        Pruefe.Wahr(bauen.Contains("[switch]$Verwaltung", StringComparison.Ordinal) && bauen.Contains("if ($Verwaltung) { $isccArgumente += '/DVerwaltung=1' }", StringComparison.Ordinal),
            "Schalter -Verwaltung reicht /DVerwaltung=1 weiter");
        Pruefe.Wahr(bauen.Contains(@"src\Funk.Verwaltung\bin\Release\net10.0-windows\" + Funk.Client.Dienste.Verwaltungslader.Datei, StringComparison.Ordinal)
            && bauen.Contains(@"""$quelle\" + Funk.Client.Dienste.Verwaltungslader.Ordner + @"\" + Funk.Client.Dienste.Verwaltungslader.Datei + @"""", StringComparison.Ordinal),
            "Verwaltungsteil dorthin, wo der Lader sucht");
        Pruefe.Wahr(bauen.Contains("$programmText.IndexOf($verwaltungSha, [StringComparison]::OrdinalIgnoreCase)", StringComparison.Ordinal), "Prüfsumme gegen TacRelay.dll");
        Pruefe.Wahr(bauen.Contains(@"if (-not $Verwaltung) { $verboten += '|(^|\\)verwaltung\\|(^|\\)Funk\.Verwaltung\.' }", StringComparison.Ordinal)
            && bauen.Contains("Verwaltungscode im normalen Setup", StringComparison.Ordinal) && bauen.Contains("TacRelay.deps.json nennt Funk.Verwaltung", StringComparison.Ordinal),
            "normales Setup ohne Verwaltungscode, geprüft an Dateien, deps.json und Typen");
        Pruefe.Wahr(bauen.Contains("_JG-TacRelay-$art-$fassung", StringComparison.Ordinal) && bauen.Contains("'Admin-Setup'", StringComparison.Ordinal), "eigener Name des Admin-Setups");
        Pruefe.Wahr(bauen.Contains(@"Copy-Item -LiteralPath $profilDatei -Destination ""$quelle\profil.json""", StringComparison.Ordinal), "Serverprofil in den Programmordner");
    }

    [Test]
    public static void Setup_Fassung_EineQuelle()
    {
        var props = XDocument.Parse(Lesen("Directory.Build.props"));
        var versionen = props.Descendants("Version").ToList();
        Pruefe.Gleich(1, versionen.Count, "genau ein <Version> in Directory.Build.props");
        Pruefe.Wahr(FassungMuster().IsMatch(versionen[0].Value.Trim()), "Major.Minor.Patch: " + versionen[0].Value);

        // Kein Projekt setzt eine eigene Fassung; alle erben sie aus Directory.Build.props.
        var eigene = new List<string>();
        foreach (var ordner in new[] { "src", "tests" })
        {
            foreach (var datei in Directory.EnumerateFiles(Path.Combine(Wurzel(), ordner), "*.csproj", SearchOption.AllDirectories))
            {
                var x = XDocument.Load(datei);
                if (x.Descendants().Any(e => e.Name.LocalName is "Version" or "VersionPrefix" or "FileVersion" or "AssemblyVersion" or "InformationalVersion"))
                    eigene.Add(Path.GetFileName(datei));
            }
        }
        Pruefe.Gleich(0, eigene.Count, "Projekte mit eigener Fassung: " + string.Join(", ", eigene));

        // Das Setup nimmt sie nur vom Bauwerkzeug und prüft sie gegen TacRelay.exe; keine Zahl steht fest im Skript.
        var skript = Lesen("installer", "TacRelay.iss");
        var a = Abschnitte(skript);
        Pruefe.Gleich("{#Fassung}", Anweisung(a, "AppVersion"), "AppVersion aus Fassung");
        Pruefe.Gleich("{#Fassung}", Anweisung(a, "VersionInfoVersion"), "VersionInfoVersion aus Fassung");
        Pruefe.Wahr(Regex.IsMatch(skript, @"#ifndef Fassung\s*\r?\n\s*#error", RegexOptions.None, TimeSpan.FromSeconds(1)), "ohne Fassung kein Setup");
        Pruefe.Wahr(skript.Contains("#if ExeFassung != Fassung + \".0\"", StringComparison.Ordinal), "Fassung gegen TacRelay.exe geprüft");
        Pruefe.Falsch(a["Setup"].Any(z => FassungImText().IsMatch(z)), "keine Fassung als Zahl in AppVersion, AppVerName, VersionInfo*");
        var werkzeug = Lesen("tools", "setup-bauen.ps1");
        Pruefe.Wahr(werkzeug.Contains("SelectNodes('/Project/PropertyGroup/Version')", StringComparison.Ordinal)
            && werkzeug.Contains("\"/DFassung=$fassung\"", StringComparison.Ordinal), "setup-bauen.ps1 liest Directory.Build.props und gibt die Fassung weiter");

        // Auch Hello trägt diese Fassung (4.7): Mit einer festen "0.1.0" im Client ließe sich mindestClientVersion nie anheben,
        // ohne die neuen Clients mit auszusperren. Der Server nennt dieselbe Fassung in Welcome und im Journal.
        var fassung = versionen[0].Value.Trim();
        Pruefe.Gleich(fassung, Funk.ClientKern.FunkClientOptionen.Fassung, "Fassung des Clients aus Directory.Build.props");
        var optionen = new Funk.ClientKern.FunkClientOptionen { Aufbau = null!, Identitaet = null! };
        Pruefe.Gleich(fassung, optionen.ClientVersion, "Hello meldet ohne Angabe diese Fassung");
        Pruefe.Wahr(Funk.Protokoll.Steuerung.SemVer.TryParse(fassung, out _), "SemVer");
    }

    /// <summary>
    /// Zusammenführung 0.4.1 (01.10.2026): Die Zweige adminhinweis und hoerprobe bauten für 0.4.1 und ließen die Fassung mit
    /// Absicht auf 0.4.0; HEIMSERVER-PROXMOX.md nannte so schon "Admin-Setup 0.4.1", das kein Bau erzeugt hätte. Anleitungen und
    /// Paketvorlagen nennen deshalb keine Fassung über der in Directory.Build.props; ältere bleiben als Geschichte stehen.
    /// </summary>
    [Test]
    public static void Setup_AnleitungenNennenKeineFassungUeberDerEigenen()
    {
        var eigene = Version.Parse(XDocument.Parse(Lesen("Directory.Build.props")).Descendants("Version").Single().Value.Trim());
        var dateien = Directory.EnumerateFiles(Path.Combine(Wurzel(), "docs"), "*.md")
            .Concat(Directory.EnumerateFiles(Path.Combine(Wurzel(), "tools", "paket"), "*.txt"))
            .ToList();
        var gesehen = 0;
        var zuHoch = new List<string>();
        foreach (var datei in dateien)
        {
            foreach (Match m in FassungInAnleitung().Matches(File.ReadAllText(datei)))
            {
                gesehen++;
                if (Version.Parse(m.Groups[1].Value) > eigene) zuHoch.Add(Path.GetFileName(datei) + ": " + m.Value);
            }
        }
        // Greift die Suche überhaupt? Am 01.10.2026 standen 137 Angaben in docs und tools\paket.
        Pruefe.Wahr(gesehen > 50, "Fassungsangaben gefunden: " + gesehen);
        Pruefe.Gleich(0, zuHoch.Count, "Fassung über " + eigene + " (Directory.Build.props) in: " + string.Join("; ", zuHoch));

        // Gegenprobe: das Muster greift die Formen der Anleitungen und keine Adresse.
        Pruefe.Wahr(FassungInAnleitung().Match("Ab dem Admin-Setup 0.4.1 zeigt").Groups[1].Value == "0.4.1", "Admin-Setup");
        Pruefe.Wahr(FassungInAnleitung().IsMatch("Clients ab 0.4.0") && FassungInAnleitung().IsMatch("Server 0.4.0 auf"), "Clients ab, Server");
        Pruefe.Falsch(FassungInAnleitung().IsMatch("Server auf 127.0.0.1 hören"), "eine Adresse ist keine Fassung");
    }

    [Test]
    public static void Setup_Probefunk_EinstellungenUndSocketImBenutzerordner()
    {
        var cmd = Lesen("installer", "probefunk-starten.cmd");
        Pruefe.Wahr(cmd.Contains(@"set ""TR_PROBE=%LOCALAPPDATA%\JG-TacRelay\probe""", StringComparison.Ordinal), "Probe-Ordner unter %LOCALAPPDATA%");
        Pruefe.Wahr(cmd.Contains(@"set ""TACRELAY_EINSTELLUNGEN=%TR_PROBE%\probe-einstellungen.json""", StringComparison.Ordinal),
            "eigene Einstellungen der Probe, getrennt von den normalen");
        Pruefe.Wahr(cmd.Contains(@"--probe ""%~dp0konfig\probefunk.json""", StringComparison.Ordinal), "Probe-Konfiguration aus dem Programmordner (nur gelesen)");
        Pruefe.Wahr(cmd.Contains(@"--client ""%TR_PROGRAMM%\TacRelay.exe"" --client-freigegeben", StringComparison.Ordinal),
            "der Client als Begleitprogramm, ausdrücklich freigegeben");
        Pruefe.Wahr(cmd.Contains("--testzugang 0 --echo", StringComparison.Ordinal), "Testzugang mit freiem Port, Echo-Funker");
        var zustand = ZustandArgument().Match(cmd);
        Pruefe.Wahr(zustand.Success, "--zustand in probefunk-starten.cmd");
        Pruefe.Gleich(@"%TR_PROBE%\zustand", zustand.Groups[1].Value, "Zustand unter dem Probe-Ordner, nie im Programmordner");

        // Der Verwaltungssocket entsteht im Laufverzeichnis <zustand>\JJJJMMTT-HHmmss-fff (Probeserver.ProbeStartenAsync) und
        // weicht nur aus, wenn der Pfad zu lang ist. Mit dem längsten üblichen Benutzernamen (20 Zeichen) muss er bleiben.
        foreach (var benutzer in new[] { "Mustermann", new string('b', 20) })
        {
            var lokal = $@"C:\Users\{benutzer}\AppData\Local";
            var verzeichnis = zustand.Groups[1].Value.Replace("%TR_PROBE%", lokal + @"\JG-TacRelay\probe", StringComparison.Ordinal) + @"\20260930-235959-999";
            var socket = Probeserver.SocketPfad(verzeichnis, @"X:\ausweich");
            Pruefe.Wahr(socket.StartsWith(verzeichnis, StringComparison.Ordinal), $"Socket im Laufverzeichnis bei '{benutzer}': {socket}");
            Pruefe.Wahr(Encoding.UTF8.GetByteCount(socket) <= Probeserver.MaxSocketPfad, $"{Encoding.UTF8.GetByteCount(socket)} Byte bei '{benutzer}'");
        }
    }

    [Test]
    public static void Setup_Werkzeug_FuehrtSetupNieAus()
    {
        var text = Lesen("tools", "setup-bauen.ps1");
        var code = OhneKommentare(text);
        Pruefe.Wahr(code.Contains(@"$iscc = 'C:\Claude\Eigene_Programme\innosetup\ISCC.exe'", StringComparison.Ordinal), "ISCC mit absolutem Pfad");
        Pruefe.Wahr(code.Contains(@"$innounp = 'C:\Claude\Eigene_Programme\innounp\innounp.exe'", StringComparison.Ordinal), "innounp mit absolutem Pfad");
        Pruefe.Gleich(null, Ausfuehrung(code), "setup-bauen.ps1 startet das Setup nicht");
        // innounp nur zum Listen (-v) und Entpacken (-x).
        foreach (Match m in InnounpAufruf().Matches(code))
            Pruefe.Wahr(m.Groups[1].Value is "-v" or "-x", "innounp nur mit -v oder -x: " + m.Value);
        Pruefe.Wahr(code.Contains("[string]$Ziel = 'C:\\Claude\\output\\test'", StringComparison.Ordinal)
            && code.Contains("[string]$Arbeitsordner = 'C:\\Claude\\tmp\\tr-setup'", StringComparison.Ordinal)
            && code.Contains("$Ziel = InDerZone $Ziel", StringComparison.Ordinal) && code.Contains("$Arbeitsordner = InDerZone $Arbeitsordner", StringComparison.Ordinal),
            "Ablage und Arbeitsordner in C:\\Claude");

        // Gegenprobe: jede Art, die Setup-Datei zu starten, fällt auf.
        Pruefe.Wahr(Ausfuehrung("& $setupDatei /VERYSILENT") is not null, "& erkannt");
        Pruefe.Wahr(Ausfuehrung("Start-Process $setupZiel -Wait") is not null, "Start-Process erkannt");
        Pruefe.Wahr(Ausfuehrung("Invoke-Item $x") is not null, "Invoke-Item erkannt");
        Pruefe.Wahr(Ausfuehrung("cmd /c \"$setupDatei\"") is not null, "cmd erkannt");
        Pruefe.Gleich(null, Ausfuehrung("Get-FileHash $setupDatei"), "Lesen ist erlaubt");
    }

    /// <summary>Erste Stelle, die ein Programm über die Setup-Variablen startet oder Dateien per Shell öffnet, oder null.</summary>
    private static string? Ausfuehrung(string code)
    {
        var m = AusfuehrungMuster().Match(code);
        return m.Success ? m.Value : null;
    }

    private static readonly byte[] PngKennung = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Test]
    public static void Symbol_IcoMitAllenGroessenUndEingebunden()
    {
        var pfad = Path.Combine(Wurzel(), "src", "Funk.Client", "TacRelay.ico");
        var d = File.ReadAllBytes(pfad);
        Pruefe.Gleich((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(d), "ICONDIR reserviert");
        Pruefe.Gleich((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(2)), "Typ Symbol");
        var anzahl = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(4));
        var groessen = new List<int>();
        for (var i = 0; i < anzahl; i++)
        {
            var e = d.AsSpan(6 + 16 * i, 16);
            var groesse = e[0] == 0 ? 256 : e[0];
            groessen.Add(groesse);
            Pruefe.Gleich(e[0], e[1], "quadratisch");
            Pruefe.Gleich((ushort)32, BinaryPrimitives.ReadUInt16LittleEndian(e[6..]), $"{groesse} px mit 32 Bit");
            var laenge = BinaryPrimitives.ReadInt32LittleEndian(e[8..]);
            var versatz = BinaryPrimitives.ReadInt32LittleEndian(e[12..]);
            Pruefe.Wahr(versatz > 0 && laenge > 0 && versatz + laenge <= d.Length, $"{groesse} px liegt in der Datei");
            var bild = d.AsSpan(versatz, laenge);
            if (bild[..8].SequenceEqual(PngKennung))
            {
                Pruefe.Gleich(groesse, BinaryPrimitives.ReadInt32BigEndian(bild[16..]), $"PNG {groesse} px breit");
            }
            else
            {
                Pruefe.Gleich(40, BinaryPrimitives.ReadInt32LittleEndian(bild), $"DIB {groesse} px: BITMAPINFOHEADER");
                Pruefe.Gleich(groesse, BinaryPrimitives.ReadInt32LittleEndian(bild[4..]), $"DIB {groesse} px breit");
                Pruefe.Gleich(2 * groesse, BinaryPrimitives.ReadInt32LittleEndian(bild[8..]), $"DIB {groesse} px mit Maske");
            }
        }
        Pruefe.Gleich("16,20,24,32,40,48,64,128,256", string.Join(",", groessen), "Größen");

        var csproj = XDocument.Load(Path.Combine(Wurzel(), "src", "Funk.Client", "Funk.Client.csproj"));
        Pruefe.Gleich("TacRelay.ico", csproj.Descendants("ApplicationIcon").SingleOrDefault()?.Value, "ApplicationIcon im Client");
        Pruefe.Wahr(Lesen("installer", "TacRelay.iss").Contains(@"#define Symbol     SourcePath + ""..\src\Funk.Client\TacRelay.ico""", StringComparison.Ordinal),
            "dasselbe Symbol im Setup");
        var notices = Lesen("THIRD-PARTY-NOTICES.txt");
        Pruefe.Wahr(notices.Contains("TacRelay.ico", StringComparison.Ordinal) && notices.Contains("Eigenbau", StringComparison.Ordinal)
            && notices.Contains(@"tools\symbol-zeichnen.ps1", StringComparison.Ordinal), "THIRD-PARTY-NOTICES: Symbol als Eigenbau mit Quelle");
        Pruefe.Wahr(notices.Contains("Inno Setup", StringComparison.Ordinal), "THIRD-PARTY-NOTICES: Inno Setup für Setup und Deinstallieren");
        Pruefe.Wahr(File.Exists(Path.Combine(Wurzel(), "tools", "symbol-zeichnen.ps1")), "Quelle des Symbols liegt im Repository");
        foreach (var vorlage in new[] { "symbol.svg", "symbol-klein.svg", "symbol-16.svg" })
            Pruefe.Wahr(File.Exists(Path.Combine(Wurzel(), "grafik", "symbol", vorlage)), "Vorlage des Symbols: grafik\\symbol\\" + vorlage);
        Pruefe.Wahr(notices.Contains(@"grafik\symbol", StringComparison.Ordinal) && notices.Contains("Gildenbezug", StringComparison.Ordinal),
            "THIRD-PARTY-NOTICES: Vorlagen des Symbols, selbst gezeichnet ohne Gildenbezug");
    }

    /// <summary>Entfernt Kommentarblöcke &lt;# #&gt; und Zeilenkommentare, damit Beispiele in der Hilfe nicht zählen.</summary>
    private static string OhneKommentare(string code) =>
        Regex.Replace(Regex.Replace(code, "<#.*?#>", "", RegexOptions.Singleline, TimeSpan.FromSeconds(1)), @"(?m)^\s*#.*$", "", RegexOptions.None, TimeSpan.FromSeconds(1));

    [GeneratedRegex(@"^\[([A-Za-z]+)\]\s*$")]
    private static partial Regex AbschnittKopf();

    [GeneratedRegex(@"\b(RegWrite\w*|RegDelete\w*|RegSetValue\w*|RegCreateKey\w*)\s*\(")]
    private static partial Regex RegistryAufruf();

    [GeneratedRegex(@"%~dp0|%TR_[A-Z]+%|%LOCALAPPDATA%")]
    private static partial Regex CmdPfad();

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex FassungMuster();

    /// <summary>"Admin-Setup 0.4.1", "Clients ab 0.4.0", "Server 0.2.0" in Anleitungen; nie der Anfang einer Adresse.</summary>
    [GeneratedRegex(@"\b(?:Admin-Setup|Setup|Setups|Client|Clients|Server|Serverpaket|Gesamtpaket|Programme|Programmen|Fassung)(?: ab| von| auf)? (\d+\.\d+\.\d+)(?![.\d])")]
    private static partial Regex FassungInAnleitung();

    [GeneratedRegex(@"^(AppVersion|AppVerName|VersionInfo\w*)=.*\d+\.\d+")]
    private static partial Regex FassungImText();

    [GeneratedRegex(@"--zustand ""([^""]+)""")]
    private static partial Regex ZustandArgument();

    [GeneratedRegex(@"&\s*\$innounp\s+(-\w)")]
    private static partial Regex InnounpAufruf();

    [GeneratedRegex(@"&\s*\$setup\w*|Start-Process\b|Invoke-Item\b|\bii\s|Invoke-Expression\b|\biex\s|cmd(\.exe)?\s+/c\b[^\n]*\$setup|\.Start\s*\(")]
    private static partial Regex AusfuehrungMuster();

    [GeneratedRegex(@"FmtMessage\(CustomMessage\('(\w+)'\),\s*\[([^\]]*)\]\)")]
    private static partial Regex FmtAufruf();

    [GeneratedRegex(@"\b(Sie|Ihr|Ihre|Ihren|Ihrem|Ihres|Ihnen)\b")]
    private static partial Regex SieForm();

    [GeneratedRegex(@"(?m)^(function|procedure) ")]
    private static partial Regex FunktionsKopf();
}
