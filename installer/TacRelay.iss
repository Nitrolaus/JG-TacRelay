; JG TacRelay - Maintainer: Sarevoc
; TacRelay.iss - Setup für den Client von JG TacRelay (Inno Setup 6.7).
;
; Gebaut wird es mit tools\setup-bauen.ps1. Das Werkzeug veröffentlicht Client und Testserver, legt alles, was
; installiert wird, in einen Ordner (Quelle), zeichnet die Bilder des Assistenten und ruft dann auf:
;
;     C:\Claude\Eigene_Programme\innosetup\ISCC.exe /DQuelle=<ordner> /DBilder=<ordner> /DFassung=0.2.0
;         /DAusgabe=<ordner> /DAusgabeName=<name> installer\TacRelay.iss
;
; Die Fassung hat genau eine Quelle: <Version> in Directory.Build.props. Das Werkzeug liest sie dort; dieses Skript
; prüft sie beim Übersetzen gegen die Dateiversion von TacRelay.exe und bricht bei einer Abweichung ab.
;
; Grundsätze:
;
;   - Keine Administratorrechte (PrivilegesRequired=lowest). Ziel ist %LOCALAPPDATA%\Programs\JG-TacRelay, im
;     Assistenten frei wählbar, etwa C:\Meine_Programme\JG-TacRelay. PrivilegesRequiredOverridesAllowed steht bewusst
;     NICHT hier: "Für alle Benutzer" bräuchte die Benutzerkontensteuerung, schriebe unter HKLM und nach "Programme",
;     und JG TacRelay ist ein Programm je Person (Geräteschlüssel und Einstellungen gehören dem angemeldeten Konto).
;     Niemand auf dem PC braucht es für alle; eine Frage weniger ist eine falsche Antwort weniger.
;   - Nichts am System: kein [Registry]-Abschnitt, kein Dienst, kein Autostart, keine Dateizuordnung. Die einzigen
;     Einträge in der Registry legt Inno selbst für "Apps" und das Deinstallieren an (HKCU, ...\Uninstall\<AppId>_is1).
;     Der [Code] liest die Registry nur: diesen Eintrag (vorhandene Installation) und den Ort von .NET.
;   - Ein zweites Setup erkennt eine vorhandene Installation an der festen AppId und bietet Aktualisieren, Reparieren
;     oder Neu installieren an (Seite "JG TacRelay ist schon installiert", siehe [Code]).
;   - Die Einstellungen in %LOCALAPPDATA%\JG-TacRelay (auch die des Probefunks unter probe\) liegen außerhalb des
;     Programmordners. Das Deinstallieren entfernt Programmdateien und Verknüpfungen und lässt sie stehen, ohne zu
;     fragen: Eine Neuinstallation findet so alles wieder, und eine Frage beim Deinstallieren versprach mehr, als sie
;     hält (der Geräteschlüssel liegt im Schlüsselspeicher von Windows, nicht in dem Ordner). Wer sie nicht mehr
;     braucht, löscht den Ordner selbst; der Abschlusstext des Deinstallierens sagt, wo er liegt. Auch "Neu
;     installieren" braucht das: Es ruft den alten Deinstaller still auf, und die Einstellungen bleiben.
;   - Deutsch per du: Die Texte von Inno (Languages\German.isl) siezen; was davon in diesem Setup erscheint, steht
;     unten in [Messages] neu. Die Sprachdatei selbst bleibt unverändert.
;   - Das Setup ist nicht signiert; der Abschlusstext sagt, was SmartScreen dann zeigt.
;   - Seite "Deine Namen" (A42): Bei einer neuen Installation ohne Einstellungen fragt das Setup SC-Name und Discord-Name
;     und legt sie als %LOCALAPPDATA%\JG-TacRelay\anmeldung.json neben die Einstellungen. Der erste Start übernimmt sie
;     und fragt beim Antrag "stimmt das?" (src\Funk.Client\Ablage\Anmeldedatei.cs). Geheim ist daran nichts; leer lassen
;     geht, dann fragt JG TacRelay selbst.
;   - Admin-Setup: Mit /DVerwaltung=1 (tools\setup-bauen.ps1 -Verwaltung) liegt in der Quelle zusätzlich
;     verwaltung\Funk.Verwaltung.dll, der Verwaltungsteil (A45). Das normale Setup enthält ihn nicht. Es löscht auch keinen
;     alten (kein [InstallDelete], wie für alles andere): Der Client lädt ihn nur, wenn seine Prüfsumme zur Programmdatei
;     passt, und sagt sonst, dass das Admin-Setup dieser Fassung fehlt. Beim Deinstallieren geht er mit.

#ifndef Quelle
  #error Quelle fehlt. Das Setup nur über tools\setup-bauen.ps1 bauen.
#endif
#ifndef Bilder
  #error Bilder fehlt. Das Setup nur über tools\setup-bauen.ps1 bauen.
#endif
#ifndef Fassung
  #error Fassung fehlt. Sie kommt aus Directory.Build.props über tools\setup-bauen.ps1.
#endif
#ifndef Ausgabe
  #error Ausgabe fehlt. Das Setup nur über tools\setup-bauen.ps1 bauen.
#endif
#ifndef AusgabeName
  #error AusgabeName fehlt. Das Setup nur über tools\setup-bauen.ps1 bauen.
#endif

#define Name       "JG TacRelay"
; Der Ordner unter {autopf} und der Name, den das Setup beim Durchsuchen anhängt: ohne Leerzeichen.
#define Ordner     "JG-TacRelay"
; Der Maintainer (A58, A59): Herausgeber und Copyright-Angabe nennen nur ihn, ohne Jahr und ohne Aussage über den Umfang von Rechten.
#define Maintainer "Sarevoc"
#define ExeName    "TacRelay.exe"
; Die Kennung des Programms für Windows. NIE ÄNDERN: An ihr erkennt Windows eine Aktualisierung, und das Setup findet
; über sie die vorhandene Installation (Uninstall-Eintrag <AppGuid>_is1). Mit einer neuen stünde JG TacRelay zweimal
; unter "Apps". Sie steht auch in tools\setup-bauen.ps1 (Prüfung mit innounp) und tests\Funk.Tests\SetupTests.cs; der
; Test fällt, wenn sie abweichen. Neu seit der Umbenennung in JG TacRelay (Q7: sauberer Schnitt, außer beim Eigentümer
; gibt es keine Installationen); die Kennung von davor gilt nicht mehr.
#define AppGuid    "{E2908D2D-EA8B-4B2D-B4F1-CBDE75DE42AE}"
#define Symbol     SourcePath + "..\src\Funk.Client\TacRelay.ico"

; Fassung aus Directory.Build.props gegen die gebaute Programmdatei: Weichen sie ab, wurde nicht frisch veröffentlicht.
#define ExeFassung GetVersionNumbersString(Quelle + "\" + ExeName)
#if ExeFassung != Fassung + ".0"
  #error Fassung passt nicht zu TacRelay.exe. Neu bauen mit tools\setup-bauen.ps1.
#endif

[Setup]
AppId={{#AppGuid}
AppName={#Name}
AppVersion={#Fassung}
AppVerName={#Name} {#Fassung}
AppPublisher={#Maintainer}
AppCopyright=Maintainer: {#Maintainer}
UninstallDisplayName={#Name}
UninstallDisplayIcon={app}\{#ExeName}
VersionInfoVersion={#Fassung}
VersionInfoProductVersion={#Fassung}
VersionInfoCompany={#Maintainer}
VersionInfoCopyright=Maintainer: {#Maintainer}
VersionInfoProductName={#Name}
VersionInfoDescription={#Name} Setup

PrivilegesRequired=lowest
; {autopf} ist ohne Administratorrechte %LOCALAPPDATA%\Programs.
DefaultDirName={autopf}\{#Ordner}
; no statt auto: Die Ordnerseite überspringt der [Code] selbst (ShouldSkipPage), nämlich bei Aktualisieren und
; Reparieren. Mit auto bliebe sie bei jeder vorhandenen Installation weg, auch bei "Neu installieren".
DisableDirPage=no
UsePreviousAppDir=yes
; Ohne Ordnerseite nennt die Seite "Bereit" den Ordner trotzdem.
AlwaysShowDirOnReadyPage=yes
; Beim Durchsuchen hängt Setup "JG-TacRelay" an den gewählten Ordner an (C:\Meine_Programme wird
; C:\Meine_Programme\JG-TacRelay). Wer den fertigen Ordner selbst wählt, wird im Code gefragt, statt in
; ...\JG-TacRelay\JG-TacRelay zu landen.
AppendDefaultDirName=yes
; Die Standardwarnung "Ordner existiert bereits" kommt bei jeder Aktualisierung; der Code unterscheidet selbst
; zwischen "JG TacRelay liegt schon da" und "da liegt etwas Fremdes".
DirExistsWarning=no
; Ein Ordner im Startmenü mit JG TacRelay, dem Probefunk und dem Deinstallieren; keine Seite, um ihn umzubenennen.
DefaultGroupName={#Name}
DisableProgramGroupPage=yes
; Nur ein Setup zur Zeit.
SetupMutex=JG-TacRelay_Setup,Global\JG-TacRelay_Setup

; Läuft JG TacRelay (auch als Probefunk), hält es den Mutex Local\JG-TacRelay-Laeuft (src\Funk.Client\Einzelinstanz.cs,
; LaeuftMarke; der Test SetupTests prüft, dass beide Namen gleich sind). Setup und Deinstallation prüfen ihn beim Start
; und bitten, JG TacRelay zu schließen (SetupAppRunningError, UninstallAppRunningError unten, per du).
AppMutex=Local\JG-TacRelay-Laeuft
; Zweites Netz: Der [Code] sucht vor dem Installieren und Deinstallieren nach TacRelay.exe und funk-testserver.exe
; aus dem Programmordner (der Testserver des Probefunks hält keinen Mutex). Drittes Netz ist der Restart Manager für
; alles andere, das Dateien im Ordner offen hält; neu gestartet wird danach nichts, denn der Probefunk startet den Client
; mit eigenen Argumenten.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no

; Nur 64 Bit: Die nativen Bibliotheken sind x64.
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0

SetupIconFile={#Symbol}
; Dunkel wie das Programm, mit Titelleiste. Bilder selbst gezeichnet (tools\symbol-zeichnen.ps1), nicht die von Inno.
WizardStyle=modern dark includetitlebar
WizardSmallImageFile={#Bilder}\klein-*.png
WizardImageFile={#Bilder}\gross-*.png
ShowLanguageDialog=no

OutputDir={#Ausgabe}
OutputBaseFilename={#AusgabeName}
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Messages]
; Per du statt der Sie-Form aus German.isl, nur was in diesem Setup erscheinen kann. Platzhalter wie in German.isl.
SetupFileMissing=Die Datei %1 fehlt im Ordner des Setups. Hol dir bitte eine neue Kopie des Setups.
SetupFileCorrupt=Die Setup-Dateien sind beschädigt. Hol dir bitte eine neue Kopie des Setups.
SetupFileCorruptOrWrongVer=Die Setup-Dateien sind beschädigt oder passen nicht zu dieser Fassung des Setups. Hol dir bitte eine neue Kopie des Setups.
WindowsVersionNotSupported=JG TacRelay läuft nicht auf der Windows-Fassung dieses PCs.
SetupAppRunningError=Das Setup hat bemerkt, dass %1 gerade läuft.%n%nSchließe bitte %1 (der Probefunk geht dann von selbst zu) und klicke auf "OK", um weiterzumachen, oder auf "Abbrechen", um das Setup zu beenden.
UninstallAppRunningError=Die Deinstallation hat bemerkt, dass %1 gerade läuft.%n%nSchließe bitte %1 (der Probefunk geht dann von selbst zu) und klicke auf "OK", um weiterzumachen, oder auf "Abbrechen", um die Deinstallation zu beenden.
ExitSetupMessage=Das Setup ist noch nicht fertig. Wenn du jetzt aufhörst, wird JG TacRelay nicht installiert.%n%nDu kannst das Setup später noch einmal starten.%n%nSetup verlassen?
BrowseDialogLabel=Wähle einen Ordner aus und klicke danach auf "OK".
WelcomeLabel2=Dieser Assistent installiert jetzt [name/ver] auf diesem PC.%n%nSchließe am besten alle anderen Programme, bevor du weitermachst.
SelectDirBrowseLabel=Klicke auf "Weiter", um weiterzumachen, oder auf "Durchsuchen", um einen anderen Ordner zu wählen.
CannotInstallToUNCPath=Das Setup kann nicht in einen Netzwerkpfad (UNC) installieren. Ordne dem Netzlaufwerk einen Laufwerksbuchstaben zu, wenn du dorthin installieren willst.
InvalidPath=Gib bitte einen vollständigen Pfad mit Laufwerksbuchstaben an, zum Beispiel:%n%nC:\Meine_Programme\JG-TacRelay%n%noder einen UNC-Pfad in der Form:%n%n\\Server\Freigabe
InvalidDrive=Das Laufwerk oder der UNC-Pfad existiert nicht oder ist nicht erreichbar. Wähle bitte einen anderen Ordner.
DiskSpaceWarning=Das Setup braucht mindestens %1 KB freien Speicherplatz, auf dem gewählten Laufwerk sind aber nur %2 KB frei.%n%nTrotzdem weitermachen?
SelectTasksLabel2=Wähle die zusätzlichen Aufgaben, die das Setup bei der Installation von [name] ausführen soll, und klicke dann auf "Weiter".
ReadyLabel1=Das Setup ist bereit, [name] auf diesem PC zu installieren.
ReadyLabel2a=Klicke auf "Installieren", um zu beginnen, oder auf "Zurück", um deine Angaben zu prüfen oder zu ändern.
ReadyLabel2b=Klicke auf "Installieren", um zu beginnen.
PreviousInstallNotCompleted=Eine frühere Installation oder Deinstallation ist nicht abgeschlossen. Der PC muss dafür neu gestartet werden.%n%nStarte das Setup nach dem Neustart noch einmal, um [name] zu installieren.
CannotContinue=Das Setup kann nicht weitermachen. Klicke auf "Abbrechen", um es zu beenden.
PrepareToInstallNeedsRestart=Das Setup muss den PC neu starten. Starte das Setup danach noch einmal, um die Installation von [name] abzuschließen.%n%nJetzt neu starten?
InstallingLabel=Einen Moment, [name] wird auf diesem PC installiert.
FinishedLabelNoIcons=Das Setup hat [name] auf diesem PC installiert.
FinishedLabel=JG TacRelay ist eingerichtet. Im Startmenü findest du "JG TacRelay" und "JG TacRelay Probefunk" (Testserver mit Echo-Funker auf diesem PC).%n%nJG TacRelay ist nicht signiert. Windows SmartScreen kann deshalb beim ersten Start warnen ("Der Computer wurde durch Windows geschützt"). Dann auf "Weitere Informationen" und "Trotzdem ausführen" klicken.
ClickFinish=Klicke auf "Fertigstellen", um das Setup zu beenden.
FinishedRestartLabel=Um die Installation von [name] abzuschließen, muss das Setup den PC neu starten. Jetzt neu starten?
FinishedRestartMessage=Um die Installation von [name] abzuschließen, muss das Setup den PC neu starten.%n%nJetzt neu starten?
SetupAborted=Das Setup wurde nicht abgeschlossen.%n%nBehebe bitte das Problem und starte das Setup noch einmal.
ErrorRestartingComputer=Das Setup konnte den PC nicht neu starten. Starte ihn bitte selbst neu.
ConfirmUninstall=Willst du %1 wirklich entfernen?%n%nDeine Einstellungen unter AppData\Local\JG-TacRelay bleiben erhalten.
UninstallStatusLabel=Einen Moment, %1 wird von diesem PC entfernt.
UninstalledAll=JG TacRelay ist entfernt.%n%nDeine Einstellungen bleiben in deinem Benutzerordner unter AppData\Local\JG-TacRelay. Brauchst du sie nicht mehr, lösche den Ordner selbst.
UninstalledMost=%1 ist entfernt, bis auf einige Teile, die sich nicht löschen ließen. Die kannst du selbst löschen.
UninstalledAndNeedsRestart=Um die Deinstallation von %1 abzuschließen, muss der PC neu gestartet werden.%n%nJetzt neu starten?

[CustomMessages]
de.StartNachher=JG TacRelay jetzt starten
de.DesktopSymbol=Symbol auf dem Desktop anlegen
de.ProbefunkBeschreibung=Testserver mit Echo-Funker auf diesem PC, danach JG TacRelay
de.LaufzeitFehlt=JG TacRelay braucht die .NET Desktop Runtime 10 (x64) von Microsoft. Auf diesem PC ist sie nicht zu finden.%n%nDu bekommst sie kostenlos bei Microsoft:%nhttps://dotnet.microsoft.com/download/dotnet/10.0%nDort unter ".NET Desktop Runtime" den Installer für Windows x64 nehmen.%n%nTrotzdem jetzt installieren? JG TacRelay startet dann erst, wenn die Runtime da ist.%n"Ja" installiert weiter, "Nein" bricht ab.
de.LaeuftNoch=JG TacRelay läuft noch aus diesem Ordner (%1).%n%nSchließe JG TacRelay; der Probefunk geht dann von selbst zu. Danach auf "Wiederholen" klicken.
de.OrdnerGesperrt=In diesen Ordner kann das Setup ohne Administratorrechte nicht schreiben:%n%n%1%n%nNimm einen Ordner, in dem du schreiben darfst, etwa C:\Meine_Programme\JG-TacRelay oder den vorgeschlagenen.
de.OrdnerDoppelt=Der Ordner endet zweimal auf "JG-TacRelay":%n%n%1%n%nSoll stattdessen hierhin installiert werden?%n%n%2
de.OrdnerFremd=In diesem Ordner liegen schon Dateien, die nicht zu JG TacRelay gehören:%n%n%1%n%nTrotzdem dorthin installieren? Beim Deinstallieren bleiben sie liegen; entfernt wird nur, was das Setup angelegt hat.
de.MemoEinstellungen=Deine Einstellungen unter AppData\Local\JG-TacRelay bleiben erhalten, auch beim Deinstallieren.
de.MemoLaufzeitFehlt=Die .NET Desktop Runtime 10 (x64) fehlt noch; ohne sie startet JG TacRelay nicht.
de.FuerAlleGefunden=JG TacRelay ist auf diesem PC schon für alle Benutzer eingerichtet:%n%n%1%n%nDieses Setup richtet JG TacRelay ohne Administratorrechte nur für dich ein. Jene Installation kann es weder aktualisieren noch entfernen; das geht über Windows "Apps" mit Administratorrechten.%n%nTrotzdem für dich einrichten?
de.VorhandenTitel=JG TacRelay ist schon installiert
de.VorhandenFrage=Was soll das Setup tun?
de.VorhandenGefunden=Gefunden: JG TacRelay, Fassung %1, im Ordner%n%2
de.VorhandenNeuer=Dieses Setup bringt die neuere Fassung %1.
de.VorhandenGleich=Dieses Setup bringt dieselbe Fassung %1.
de.VorhandenAelter=ACHTUNG: Dieses Setup bringt die ÄLTERE Fassung %1, installiert ist die neuere %2. Zurück geht es nur nach ausdrücklicher Bestätigung.
de.VorhandenUnbekannt=Welche Fassung dort liegt, ließ sich nicht lesen. Dieses Setup bringt Fassung %1.
de.FassungUnbekannt=unbekannt
de.WahlAktualisieren=&Aktualisieren: diese Fassung in den bisherigen Ordner, über die vorhandene
de.WahlReparieren=&Reparieren: alle Dateien im bisherigen Ordner neu schreiben, etwa wenn JG TacRelay nicht mehr startet
de.WahlNeu=&Neu installieren: die vorhandene Installation erst entfernen, dann den Ordner frei wählen
de.WahlFehlt=Wähle bitte, ob das Setup aktualisieren, reparieren oder neu installieren soll.
de.AeltereFassung=Achtung, Rückschritt:%n%nInstalliert ist JG TacRelay %1, dieses Setup bringt die ältere Fassung %2.%n%nWirklich auf die ältere Fassung zurückgehen?
de.MemoVorgehen=Vorgehen:
de.MemoAktualisieren=Aktualisieren auf Fassung %2 (bisher %1), im bisherigen Ordner
de.MemoZurueck=Zurück auf die ältere Fassung %2 (bisher %1), im bisherigen Ordner
de.MemoReparieren=Reparieren: Fassung %2 wird im bisherigen Ordner neu geschrieben (bisher %1)
de.MemoNeu=Neu installieren: Die vorhandene Installation in %1 wird zuerst entfernt, dann kommt Fassung %2 in den gewählten Ordner
de.MemoNeuOhneDeinstaller=Neu installieren: Das Deinstallationsprogramm in %1 fehlt; das Setup schreibt einfach neu
de.EntfernenGescheitert=Die vorhandene Installation in%n%1%nließ sich nicht entfernen (%2).%n%nSchließe JG TacRelay, falls es läuft, und starte das Setup noch einmal, oder nimm "Reparieren".
de.EntfernenDauert=Die vorhandene Installation in%n%1%nist nach 30 Sekunden noch nicht ganz entfernt.%n%nStarte das Setup in einer Minute noch einmal.
de.NamenTitel=Deine Namen
de.NamenFrage=Wie heißt du in Star Citizen und auf deinem Discord-Server?
de.NamenText=Damit erkennt dich der Admin, bevor er dich freischaltet. Ein Passwort gibt es nicht. Beide Felder leer lassen geht auch; dann fragt JG TacRelay beim ersten Verbinden.
de.NamenSc=Dein Name in &Star Citizen, so wie im Spiel:
de.NamenDiscord=Dein &Discord-Name, wie er auf deinem Discord-Server steht, etwa Mustermann/Max:
de.NamenEiner=Trag bitte beide Namen ein oder lass beide leer.
de.NamenFalsch=Jeder Name braucht 2 bis 32 Zeichen.
de.VerwaltungHinweis=Dieses Setup enthält den Verwaltungsteil. Er bleibt gesperrt, bis der Eigentümer dich zum Admin ernennt.
de.DeinstallerFremd=Das Deinstallationsprogramm der vorhandenen Installation liegt nicht in deren Ordner:%n%1%n%nDas Setup führt es nicht aus. Nimm "Aktualisieren" oder "Reparieren", oder deinstalliere JG TacRelay über Windows "Apps".

[Tasks]
Name: "desktopsymbol"; Description: "{cm:DesktopSymbol}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Alles aus der Quelle, so wie tools\setup-bauen.ps1 sie zusammengestellt und gegen das Soll geprüft hat: der Client
; mit den nativen DLLs, probefunk\ mit funk-testserver, probefunk.json und probefunk-starten.cmd, lizenzen\ und
; THIRD-PARTY-NOTICES.txt. Keine .pdb, kein Linux-Server. ignoreversion: Aktualisieren und Reparieren schreiben jede
; Datei neu, auch wenn ihre Dateiversion gleich ist (bei Zwischenständen derselben Fassung ändert sich der Inhalt, nicht
; die Nummer).
Source: "{#Quelle}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#Name}"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"
Name: "{group}\{#Name} Probefunk"; Filename: "{app}\probefunk\probefunk-starten.cmd"; WorkingDir: "{app}\probefunk"; IconFilename: "{app}\{#ExeName}"; Comment: "{cm:ProbefunkBeschreibung}"
Name: "{group}\{cm:UninstallProgram,{#Name}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#Name}"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Tasks: desktopsymbol

[Run]
; Ab Werk aus: Der erste Weg ist meist der Probefunk aus dem Startmenü, und JG TacRelay öffnet beim Start Mikrofon
; und Wiedergabe.
Filename: "{app}\{#ExeName}"; Description: "{cm:StartNachher}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  { Die Wahlen auf der Seite "JG TacRelay ist schon installiert", in der Reihenfolge der Knöpfe. }
  WahlAktualisieren = 0;
  WahlReparieren = 1;
  WahlNeu = 2;
  { Der Eintrag, den Inno selbst für "Apps" und das Deinstallieren anlegt; die Kennung ist die feste AppId. }
  UninstallSchluessel = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuid}_is1';

var
  { Die vorhandene Installation aus diesem Eintrag; AlterOrdner ist leer, wenn es keine gibt. }
  AlterOrdner: String;
  AlteFassung: String;
  AlterDeinstaller: String;
  { Fassung dieses Setups gegen die installierte: größer 0 heißt Setup neuer, 0 gleich, kleiner 0 Setup älter. Nur
    gültig, wenn FassungBekannt. }
  FassungBekannt: Boolean;
  Vergleich: Integer;
  VorhandenSeite: TInputOptionWizardPage;
  { Die Seite "Deine Namen"; nil, bis InitializeWizard sie anlegt. }
  NamenSeite: TInputQueryWizardPage;
  { Die Wahl auf der Seite, -1 ohne vorhandene Installation (oder noch nicht gewählt). }
  Wahl: Integer;

{ Liegt die .NET Desktop Runtime 10 (x64) dort, wo TacRelay.exe sie sucht?

  Gefragt wird der Ordner, nicht die Liste in der Registry. Die Programmdatei (apphost) sucht die Laufzeit in dieser
  Reihenfolge: DOTNET_ROOT_X64 bzw. DOTNET_ROOT, dann den eingetragenen Installationsort
  (HKLM\SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64, Wert InstallLocation), sonst
  "Programme\dotnet". Dort muss unter shared\Microsoft.WindowsDesktop.App ein Ordner 10.x liegen. Genau das wird hier
  nachgesehen, nur lesend.

  Nicht genommen:
  - Die Liste unter ...\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App (ebenfalls in der 32-Bit-Ansicht)
    führt Fassungen, die längst entfernt sind (auf dem Bau-PC am 30.09.2026 gemessen: 10.0.5 bis 10.0.12 eingetragen,
    nur 10.0.12 im Ordner), und fehlt ganz, wenn die Laufzeit nicht über den Installer von Microsoft kam. Sie sagte
    also zu oft Ja und manchmal zu Unrecht Nein.
  - "dotnet --list-runtimes" hieße, aus dem Setup ein Programm zu starten, das über PATH gefunden wird und nicht
    dasselbe sein muss, das die Programmdatei später nimmt. }
function LaufzeitIn(Wurzel: String): Boolean;
var
  Basis: String;
  Suche: TFindRec;
begin
  Result := False;
  if Wurzel = '' then
    Exit;
  Basis := AddBackslash(Wurzel) + 'shared\Microsoft.WindowsDesktop.App';
  if FindFirst(Basis + '\10.*', Suche) then
  begin
    try
      repeat
        if ((Suche.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           FileExists(Basis + '\' + Suche.Name + '\Microsoft.WindowsDesktop.App.deps.json') then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(Suche);
    finally
      FindClose(Suche);
    end;
  end;
end;

function LaufzeitDa(): Boolean;
var
  Eingetragen: String;
begin
  Result := LaufzeitIn(ExpandConstant('{%DOTNET_ROOT_X64|}')) or LaufzeitIn(ExpandConstant('{%DOTNET_ROOT|}'));
  if Result then
    Exit;
  if RegQueryStringValue(HKEY_LOCAL_MACHINE_32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Eingetragen) then
    Result := LaufzeitIn(RemoveBackslashUnlessRoot(Eingetragen));
  if not Result then
    Result := LaufzeitIn(ExpandConstant('{commonpf64}\dotnet'));
end;

{ Welche Teile von JG TacRelay gerade aus Ordner laufen: TacRelay.exe und funk-testserver.exe (der Probefunk),
  als Liste der Namen, leer wenn keiner.

  Zweites Netz neben AppMutex (siehe [Setup]): Der Testserver des Probefunks hält keinen Mutex, und eine Programmdatei
  vor der Einzelinstanz (Einzelinstanz.cs) auch nicht. Gefragt wird über WMI, nur lesend, und nur nach Prozessen aus
  diesem Ordner. Geht WMI nicht, gibt es keine Aussage; dann bleibt der Restart Manager. }
function LaufendeTeile(Ordner: String): String;
var
  Ort, Dienst, Liste, Eintrag: Variant;
  I: Integer;
  Anfang, Pfad, Programm: String;
begin
  Result := '';
  Anfang := Lowercase(AddBackslash(Ordner));
  try
    Ort := CreateOleObject('WbemScripting.SWbemLocator');
    Dienst := Ort.ConnectServer('.', 'root\CIMV2');
    Liste := Dienst.ExecQuery('SELECT Name, ExecutablePath FROM Win32_Process WHERE Name = ''TacRelay.exe'' OR Name = ''funk-testserver.exe''');
    for I := 0 to Liste.Count - 1 do
    begin
      Eintrag := Liste.ItemIndex(I);
      if not VarIsNull(Eintrag.ExecutablePath) then
      begin
        { Erst in einen String, dann vergleichen: Pascal Script nimmt Lowercase nicht mit einem Variant. }
        Pfad := Eintrag.ExecutablePath;
        Programm := Eintrag.Name;
        if Copy(Lowercase(Pfad), 1, Length(Anfang)) = Anfang then
        begin
          if Result <> '' then
            Result := Result + ', ';
          Result := Result + Programm;
        end;
      end;
    end;
  except
    Result := '';
  end;
end;

{ Solange etwas läuft: sagen, was, und auf Wiederholen warten. Abbrechen gibt False. }
function NichtsLaeuft(Ordner: String): Boolean;
var
  Teile: String;
begin
  Result := True;
  Teile := LaufendeTeile(Ordner);
  while Teile <> '' do
  begin
    if SuppressibleMsgBox(FmtMessage(CustomMessage('LaeuftNoch'), [Teile]), mbError, MB_RETRYCANCEL, IDCANCEL) <> IDRETRY then
    begin
      Result := False;
      Exit;
    end;
    Teile := LaufendeTeile(Ordner);
  end;
end;

{ Liegt Pfad in Ordner (oder ist er es)? Beide ohne abschließenden Backslash, ohne Rücksicht auf Groß und klein. }
function Unter(Pfad, Ordner: String): Boolean;
begin
  Pfad := Lowercase(AddBackslash(Pfad));
  Ordner := Lowercase(AddBackslash(Ordner));
  Result := (Ordner <> '\') and (Copy(Pfad, 1, Length(Ordner)) = Ordner);
end;

{ "0.1.0" oder "0.1.0.0" als gepackte Fassung. StrToVersion erwartet vier Teile; fehlende werden 0, ein Zusatz nach
  "+" oder "-" fällt weg. Falsch, wenn es keine Fassung ist. }
function FassungLesen(S: String; var Gepackt: Int64): Boolean;
var
  I, Teile: Integer;
begin
  Result := False;
  S := Trim(S);
  I := Pos('+', S);
  if I > 0 then
    S := Copy(S, 1, I - 1);
  I := Pos('-', S);
  if I > 0 then
    S := Copy(S, 1, I - 1);
  if S = '' then
    Exit;
  Teile := 1;
  for I := 1 to Length(S) do
    if S[I] = '.' then
      Teile := Teile + 1;
  while Teile < 4 do
  begin
    S := S + '.0';
    Teile := Teile + 1;
  end;
  Result := StrToVersion(S, Gepackt);
end;

{ Liest den Uninstall-Eintrag der festen AppId unter Wurzel, nur lesend: InstallLocation (Pflicht), DisplayVersion und
  UninstallString. Inno schreibt UninstallString mit Anführungszeichen; sie fallen hier weg, denn Exec setzt sie selbst. }
function EintragLesen(Wurzel: Integer; var Ordner, Fassung, Deinstaller: String): Boolean;
begin
  Ordner := '';
  Fassung := '';
  Deinstaller := '';
  Result := RegQueryStringValue(Wurzel, UninstallSchluessel, 'InstallLocation', Ordner);
  if Result then
    Result := Trim(Ordner) <> '';
  if not Result then
    Exit;
  Ordner := RemoveBackslashUnlessRoot(Trim(Ordner));
  if not RegQueryStringValue(Wurzel, UninstallSchluessel, 'DisplayVersion', Fassung) then
    Fassung := '';
  if RegQueryStringValue(Wurzel, UninstallSchluessel, 'UninstallString', Deinstaller) then
    Deinstaller := RemoveQuotes(Trim(Deinstaller))
  else
    Deinstaller := '';
end;

{ Sucht die vorhandene Installation dieses Benutzers (HKCU; ohne Administratorrechte legt Inno den Eintrag nur dort an)
  und vergleicht ihre Fassung mit der des Setups. Die Fassung kommt aus DisplayVersion, sonst aus TacRelay.exe im
  gefundenen Ordner. Ob der Ordner noch existiert, spielt keine Rolle: Reparieren schreibt ihn neu. }
procedure VorhandeneSuchen();
var
  Installiert, Eigene: Int64;
  ExeFassung: String;
begin
  FassungBekannt := False;
  Vergleich := 0;
  if not EintragLesen(HKEY_CURRENT_USER, AlterOrdner, AlteFassung, AlterDeinstaller) then
  begin
    AlterOrdner := '';
    Exit;
  end;
  FassungBekannt := FassungLesen(AlteFassung, Installiert);
  if not FassungBekannt then
    if GetVersionNumbersString(AlterOrdner + '\{#ExeName}', ExeFassung) then
    begin
      AlteFassung := ExeFassung;
      FassungBekannt := FassungLesen(AlteFassung, Installiert);
    end;
  if FassungBekannt then
    FassungBekannt := FassungLesen('{#Fassung}', Eigene);
  if FassungBekannt then
    Vergleich := ComparePackedVersion(Eigene, Installiert)
  else
    AlteFassung := CustomMessage('FassungUnbekannt');
end;

function InitializeSetup(): Boolean;
var
  Ordner, Fassung, Deinstaller: String;
  FuerAlle: Boolean;
begin
  Result := True;
  Wahl := -1;
  VorhandeneSuchen();
  { Unter HKLM stünde ein Eintrag nur nach einer Installation für alle Benutzer, die dieses Setup nicht anbietet. Sie
    lässt sich ohne Administratorrechte weder aktualisieren noch entfernen; also nur sagen und fragen. Nacheinander
    statt mit "or": EintragLesen leert seine Ausgaben zuerst, ein zweiter Aufruf löschte sonst den ersten Fund. }
  if AlterOrdner = '' then
  begin
    FuerAlle := EintragLesen(HKEY_LOCAL_MACHINE_64, Ordner, Fassung, Deinstaller);
    if not FuerAlle then
      FuerAlle := EintragLesen(HKEY_LOCAL_MACHINE_32, Ordner, Fassung, Deinstaller);
    if FuerAlle then
      Result := SuppressibleMsgBox(FmtMessage(CustomMessage('FuerAlleGefunden'), [Ordner]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
  end;
  if not Result then
    Exit;
  if not LaufzeitDa() then
    Result := SuppressibleMsgBox(CustomMessage('LaufzeitFehlt'), mbConfirmation, MB_YESNO, IDYES) = IDYES;
end;

{ Hat JG TacRelay auf diesem PC schon Einstellungen oder Namen aus einem Setup? Dann fragt der Client selbst, und die Seite
  "Deine Namen" bleibt weg. Der Dateiname ist der des Clients (EinstellungsSpeicher.Dateiname, seit 0.1.0); bis 01.10.2026
  stand hier einstellungen.json, die es nie gab. }
function EinstellungenDa(): Boolean;
begin
  Result := FileExists(ExpandConstant('{localappdata}\JG-TacRelay\client-einstellungen.json')) or
            FileExists(ExpandConstant('{localappdata}\JG-TacRelay\anmeldung.json'));
end;

{ Zeichen wie der Server sie zählt (Unicode-Skalarwerte): Ein Ersatzpaar, etwa ein Emoji, ist ein Zeichen, nicht zwei.
  Das zweite Glied eines Paars ($DC00 bis $DFFF) zählt deshalb nicht mit. Bis 01.10.2026 zählte das Setup UTF-16-Einheiten. }
function Zeichenzahl(S: String): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 1 to Length(S) do
    if (Ord(S[I]) < $DC00) or (Ord(S[I]) > $DFFF) then
      Result := Result + 1;
end;

{ 2 bis 32 Zeichen ohne Steuerzeichen. Genauer prüft der Client nach den Regeln des Servers (Namensregeln.cs); was dort
  nicht passt, fällt weg, und der Client fragt selbst. }
function NameGut(Name: String): Boolean;
var
  I, N: Integer;
begin
  N := Zeichenzahl(Name);
  Result := (N >= 2) and (N <= 32);
  if Result then
    for I := 1 to Length(Name) do
      if Ord(Name[I]) < 32 then
        Result := False;
end;

{ Beide leer: weiter ohne Namen. Einer leer oder ein Name ungültig: sagen und auf der Seite bleiben. }
function NamenWeiter(): Boolean;
var
  Sc, Dc: String;
begin
  Result := True;
  Sc := Trim(NamenSeite.Values[0]);
  Dc := Trim(NamenSeite.Values[1]);
  if (Sc = '') and (Dc = '') then
    Exit;
  if (Sc = '') or (Dc = '') then
  begin
    SuppressibleMsgBox(CustomMessage('NamenEiner'), mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  if not NameGut(Sc) or not NameGut(Dc) then
  begin
    SuppressibleMsgBox(CustomMessage('NamenFalsch'), mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

{ Text für eine JSON-Zeichenkette: Anführungszeichen und Rückstrich bekommen einen Rückstrich davor. }
function JsonText(S: String): String;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(S) do
    if (S[I] = '"') or (S[I] = '\') then
      Result := Result + '\' + S[I]
    else
      Result := Result + S[I];
end;

{ Nach dem Kopieren: die Namen als anmeldung.json neben die Einstellungen, UTF-8. Nur mit beiden Namen und nur, wenn es noch
  keine Einstellungen gibt; eine stille Installation lässt die Felder leer und schreibt nichts. }
procedure NamenAblegen();
var
  Sc, Dc, Ordner: String;
  Zeilen: TArrayOfString;
begin
  if NamenSeite = nil then
    Exit;
  Sc := Trim(NamenSeite.Values[0]);
  Dc := Trim(NamenSeite.Values[1]);
  if (Sc = '') or (Dc = '') or EinstellungenDa() then
    Exit;
  Ordner := ExpandConstant('{localappdata}\JG-TacRelay');
  if not ForceDirectories(Ordner) then
    Exit;
  SetArrayLength(Zeilen, 1);
  Zeilen[0] := '{"scName": "' + JsonText(Sc) + '", "discordName": "' + JsonText(Dc) + '"}';
  SaveStringsToUTF8File(Ordner + '\anmeldung.json', Zeilen, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    NamenAblegen();
end;

{ Die Seite "JG TacRelay ist schon installiert", nur wenn es eine vorhandene Installation gibt. Vorgewählt ist
  Aktualisieren, wenn das Setup neuer ist (oder die installierte Fassung unbekannt), Reparieren bei gleicher Fassung.
  Ist das Setup älter, ist nichts vorgewählt: Zurück geht es nur mit eigener Wahl und Bestätigung (NextButtonClick). }
procedure InitializeWizard();
var
  Stand: String;
  Mehr: Integer;
begin
  { "Deine Namen" nach der Ordnerseite; ShouldSkipPage lässt sie weg, wo der Client selbst fragt. }
  NamenSeite := CreateInputQueryPage(wpSelectDir, CustomMessage('NamenTitel'), CustomMessage('NamenFrage'), CustomMessage('NamenText'));
  NamenSeite.Add(CustomMessage('NamenSc'), False);
  NamenSeite.Add(CustomMessage('NamenDiscord'), False);
  if AlterOrdner = '' then
    Exit;
  Stand := FmtMessage(CustomMessage('VorhandenGefunden'), [AlteFassung, AlterOrdner]) + #13#10#13#10;
  if not FassungBekannt then
    Stand := Stand + FmtMessage(CustomMessage('VorhandenUnbekannt'), ['{#Fassung}'])
  else if Vergleich > 0 then
    Stand := Stand + FmtMessage(CustomMessage('VorhandenNeuer'), ['{#Fassung}'])
  else if Vergleich = 0 then
    Stand := Stand + FmtMessage(CustomMessage('VorhandenGleich'), ['{#Fassung}'])
  else
    Stand := Stand + FmtMessage(CustomMessage('VorhandenAelter'), ['{#Fassung}', AlteFassung]);
  Stand := Stand + ' ' + CustomMessage('MemoEinstellungen');
  VorhandenSeite := CreateInputOptionPage(wpWelcome, CustomMessage('VorhandenTitel'), CustomMessage('VorhandenFrage'), Stand, True, False);
  VorhandenSeite.Add(CustomMessage('WahlAktualisieren'));
  VorhandenSeite.Add(CustomMessage('WahlReparieren'));
  VorhandenSeite.Add(CustomMessage('WahlNeu'));
  if FassungBekannt and (Vergleich < 0) then
  begin
    { Fett braucht mehr Platz; die Seite hat die Höhe des Textes schon beim Anlegen festgelegt. Neu messen und die Knöpfe
      um das Mehr nach unten schieben, sonst schneidet der Text unten ab. }
    VorhandenSeite.SubCaptionLabel.Font.Style := [fsBold];
    Mehr := WizardForm.AdjustLabelHeight(VorhandenSeite.SubCaptionLabel);
    WizardForm.IncTopDecHeight(VorhandenSeite.CheckListBox, Mehr);
  end
  else if FassungBekannt and (Vergleich = 0) then
    VorhandenSeite.SelectedValueIndex := WahlReparieren
  else
    VorhandenSeite.SelectedValueIndex := WahlAktualisieren;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if VorhandenSeite <> nil then
    if PageID = VorhandenSeite.ID then
    begin
      Result := AlterOrdner = '';
      Exit;
    end;
  { Namen nur bei einer neuen Installation ohne Einstellungen; sonst fragt der Client. }
  if NamenSeite <> nil then
    if PageID = NamenSeite.ID then
    begin
      Result := (Wahl = WahlAktualisieren) or (Wahl = WahlReparieren) or EinstellungenDa();
      Exit;
    end;
  { Aktualisieren und Reparieren gehen in den bisherigen Ordner; da gibt es nichts zu wählen. }
  if PageID = wpSelectDir then
    Result := (Wahl = WahlAktualisieren) or (Wahl = WahlReparieren);
end;

function OrdnerHatInhalt(Ordner: String): Boolean;
var
  Suche: TFindRec;
begin
  Result := False;
  if not FindFirst(Ordner + '\*', Suche) then
    Exit;
  try
    repeat
      if (Suche.Name <> '.') and (Suche.Name <> '..') then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(Suche);
  finally
    FindClose(Suche);
  end;
end;

{ Die Wahl auf der Seite "JG TacRelay ist schon installiert" übernehmen. Auch bei einer stillen Installation ruft Inno
  das auf, mit der Vorwahl; ein Rückschritt endet dort mit Nein (SuppressibleMsgBox, Vorgabe IDNO). }
function VorhandenWeiter(): Boolean;
begin
  Result := True;
  Wahl := VorhandenSeite.SelectedValueIndex;
  if Wahl < 0 then
  begin
    SuppressibleMsgBox(CustomMessage('WahlFehlt'), mbError, MB_OK, IDOK);
    Result := False;
    Exit;
  end;
  if FassungBekannt and (Vergleich < 0) then
    if SuppressibleMsgBox(FmtMessage(CustomMessage('AeltereFassung'), [AlteFassung, '{#Fassung}']), mbConfirmation,
       MB_YESNO or MB_DEFBUTTON2, IDNO) <> IDYES then
    begin
      Wahl := -1;
      Result := False;
      Exit;
    end;
  { Aktualisieren und Reparieren: genau der gefundene Ordner. Neu installieren: vorgeschlagen bleibt er, wählbar ist jeder. }
  if Wahl <> WahlNeu then
    WizardForm.DirEdit.Text := AlterOrdner;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Ordner, Eltern: String;
begin
  Result := True;
  if VorhandenSeite <> nil then
    if CurPageID = VorhandenSeite.ID then
    begin
      Result := VorhandenWeiter();
      Exit;
    end;
  if NamenSeite <> nil then
    if CurPageID = NamenSeite.ID then
    begin
      Result := NamenWeiter();
      Exit;
    end;
  if CurPageID = wpSelectDir then
  begin
    Ordner := RemoveBackslashUnlessRoot(WizardDirValue());
    { Programme und Windows: dort schreibt nur, wer Administratorrechte hat. }
    if Unter(Ordner, ExpandConstant('{commonpf64}')) or Unter(Ordner, ExpandConstant('{commonpf32}')) or
       Unter(Ordner, ExpandConstant('{win}')) then
    begin
      MsgBox(FmtMessage(CustomMessage('OrdnerGesperrt'), [Ordner]), mbError, MB_OK);
      Result := False;
      Exit;
    end;
    { ...\JG-TacRelay\JG-TacRelay: den fertigen Ordner gewählt, und Setup hat den Namen noch einmal angehängt. }
    if CompareText(ExtractFileName(Ordner), '{#Ordner}') = 0 then
    begin
      Eltern := ExtractFileDir(Ordner);
      if (CompareText(ExtractFileName(Eltern), '{#Ordner}') = 0) and not FileExists(Ordner + '\{#ExeName}') then
        if MsgBox(FmtMessage(CustomMessage('OrdnerDoppelt'), [Ordner, Eltern]), mbConfirmation, MB_YESNO) = IDYES then
        begin
          { Nur umstellen und auf der Seite bleiben: Weiter prüft dann den neuen Ordner. }
          WizardForm.DirEdit.Text := Eltern;
          Result := False;
          Exit;
        end;
    end;
    { Etwas Fremdes im Ordner: fragen. JG TacRelay selbst dort ist eine Aktualisierung. }
    if DirExists(Ordner) and not FileExists(Ordner + '\{#ExeName}') and OrdnerHatInhalt(Ordner) then
      Result := MsgBox(FmtMessage(CustomMessage('OrdnerFremd'), [Ordner]), mbConfirmation, MB_YESNO) = IDYES;
  end
  else if CurPageID = wpReady then
  begin
    Ordner := RemoveBackslashUnlessRoot(WizardDirValue());
    Result := NichtsLaeuft(Ordner);
    { Neu installieren in einen anderen Ordner: Auch aus dem alten darf nichts laufen, sonst scheitert sein Deinstaller. }
    if Result and (Wahl = WahlNeu) and (CompareText(Ordner, AlterOrdner) <> 0) then
      Result := NichtsLaeuft(AlterOrdner);
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo,
  MemoGroupInfo, MemoTasksInfo: String): String;
var
  Vorgehen: String;
begin
  Vorgehen := '';
  if Wahl = WahlAktualisieren then
  begin
    if FassungBekannt and (Vergleich < 0) then
      Vorgehen := FmtMessage(CustomMessage('MemoZurueck'), [AlteFassung, '{#Fassung}'])
    else
      Vorgehen := FmtMessage(CustomMessage('MemoAktualisieren'), [AlteFassung, '{#Fassung}']);
  end
  else if Wahl = WahlReparieren then
    Vorgehen := FmtMessage(CustomMessage('MemoReparieren'), [AlteFassung, '{#Fassung}'])
  else if Wahl = WahlNeu then
  begin
    if (AlterDeinstaller <> '') and FileExists(AlterDeinstaller) then
      Vorgehen := FmtMessage(CustomMessage('MemoNeu'), [AlterOrdner, '{#Fassung}'])
    else
      Vorgehen := FmtMessage(CustomMessage('MemoNeuOhneDeinstaller'), [AlterOrdner]);
  end;
  Result := '';
  if Vorgehen <> '' then
    Result := CustomMessage('MemoVorgehen') + NewLine + Space + Vorgehen + NewLine + NewLine;
  Result := Result + MemoDirInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
  Result := Result + NewLine + NewLine + CustomMessage('MemoEinstellungen');
#ifdef Verwaltung
  Result := Result + NewLine + NewLine + CustomMessage('VerwaltungHinweis');
#endif
  if not LaufzeitDa() then
    Result := Result + NewLine + NewLine + CustomMessage('MemoLaufzeitFehlt');
end;

{ Neu installieren: die vorhandene Installation über ihren eigenen Deinstaller still entfernen. Er lässt die
  Einstellungen in %LOCALAPPDATA%\JG-TacRelay stehen wie jedes Deinstallieren dieses Setups.

  Der Pfad kommt ohne Anführungszeichen (EintragLesen) und geht als Filename an Exec; Exec setzt die Anführungszeichen
  selbst (Inno-Hilfe zu Exec). So bleibt ein Ordner mit Leerzeichen ein Ordner, und die Schalter sind eigene
  Argumente. Ausgeführt wird nur ein Deinstaller, der im gefundenen Ordner liegt.

  Der Deinstaller von Inno arbeitet als Kopie im Temp-Ordner weiter; ob Exec bis zum Ende wartet, sagt die Hilfe nicht.
  Deshalb danach bis zu 30 Sekunden warten, bis sein Eintrag unter HKCU und er selbst weg sind. }
function AlteEntfernen(): String;
var
  Code, Runden: Integer;
begin
  Result := '';
  if (AlterDeinstaller = '') or not FileExists(AlterDeinstaller) then
    Exit;
  if not Unter(ExtractFileDir(AlterDeinstaller), AlterOrdner) then
  begin
    Result := FmtMessage(CustomMessage('DeinstallerFremd'), [AlterDeinstaller]);
    Exit;
  end;
  if not Exec(AlterDeinstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code) then
  begin
    Result := FmtMessage(CustomMessage('EntfernenGescheitert'), [AlterOrdner, SysErrorMessage(Code)]);
    Exit;
  end;
  if Code <> 0 then
  begin
    Result := FmtMessage(CustomMessage('EntfernenGescheitert'), [AlterOrdner, 'Exitcode ' + IntToStr(Code)]);
    Exit;
  end;
  Runden := 0;
  while (RegKeyExists(HKEY_CURRENT_USER, UninstallSchluessel) or FileExists(AlterDeinstaller)) and (Runden < 120) do
  begin
    Sleep(250);
    Runden := Runden + 1;
  end;
  if RegKeyExists(HKEY_CURRENT_USER, UninstallSchluessel) or FileExists(AlterDeinstaller) then
  begin
    Result := FmtMessage(CustomMessage('EntfernenDauert'), [AlterOrdner]);
    Exit;
  end;
  { Als Letztes nimmt der Deinstaller den nun leeren Ordner weg. Kurz darauf warten (höchstens 2 Sekunden), damit er ihn
    nicht erst entfernt, nachdem dieses Setup ihn für "Neu installieren" in denselben Ordner wieder angelegt hat. Liegt
    darin noch Fremdes, bleibt er ohnehin stehen. }
  Runden := 0;
  while DirExists(AlterOrdner) and not OrdnerHatInhalt(AlterOrdner) and (Runden < 8) do
  begin
    Sleep(250);
    Runden := Runden + 1;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if Wahl = WahlNeu then
    Result := AlteEntfernen();
end;

function InitializeUninstall(): Boolean;
begin
  Result := NichtsLaeuft(RemoveBackslashUnlessRoot(ExpandConstant('{app}')));
end;
