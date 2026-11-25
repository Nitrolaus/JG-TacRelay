# CODEMAP JG TacRelay

Wo was liegt. Der Dateiteil unten wird von `tools\codemap-erzeugen.ps1` erzeugt (Zeilenzahl und erste
Beschreibung je Datei); `-Pruefen` meldet, wenn er veraltet ist. Der Rest ist Handarbeit.

## Weg der Daten

Senden: Mikrofon (`Funk.Klang.Geraete.WasapiSystem`, NAudio 3.1, geteilter Modus, Mischformat des Geräts)
-> `Eingangswandler` (speexdsp, 48 kHz mono, 10 ms) -> `Funk.Klang.Maschine.Sendekette`: Echoschutz
(Geigel, vor dem APM) -> `EchoProzessor` (AEC3, Hochpass; Referenz ist der Loopback des Ausgangs, ohne den eigenen Druck-Klick)
-> `Pegelstufe` (Halteregel oder fest, seit Klangstufe A statt AGC2) und `Begrenzer`
-> Rauschfilter (`Rauschfilterwahl`: Standard `RnnoiseProzessor`, Stark `DeepFilterProzessor`, Aus; RNNoise rechnet immer und liefert die
Sprachwahrscheinlichkeit) -> Abgriff der Stimmprobe, Pegel fürs Gate -> seit 0.9.3 `Klangfarbe` (Q70 bis Q72: drei Glocken aus `Biquad`,
lautheitsneutral, eigener Begrenzer, neutral überbrückt; das Gate bekommt den Pegel von davor, `SprachGate.Verarbeiten(pegelDbfs)`; Einstellung
`AudioEinstellungen.Klangfarbe`, Oberfläche `KlangfarbeTeil` unter Senden > Erweitert, bis 0.9.3 in der Spalte Senden der Klangmodule) -> `GateFuehrung` mit Sprach-Gate (Modi Fest/Automatisch/Lernend; seit Klangstufe C1 im Korridor relativ zur gelernten Stimme, Vorlauf 120 ms) ->
Sendeaufbereitung -> Opus (20 ms) -> Ereignis `SprachpaketBereit` (Melde-Thread) -> `Funk.Client.Klang.KlangBruecke` -> Sprechkanal des
`Funk.ClientKern.FunkClient` -> `Funk.Protokoll.Sprache` (SprechphasenGeber, SendeKanal: AES-256-GCM) -> UDP.
Sprechtaste: `Funk.Klang.Eingabe.RawInputSprechtaste` (Raw Input, auch ohne Fokus; dazu Numpad +/-) und `DirectInputSprechtaste` (Joystick) über
`SprechtastenVerbund` -> `Tastenbetrieb` -> `Sprechsteuerung` (Sprech- und Melde-Taste) -> `KlangMaschine.SprechtasteGedrueckt/Losgelassen`
(eigener Funk-Klick, `FunkKlick.Anfordern`; seit 0.5.0 einstellbar) -> `Sendekette.TasteDruecken/TasteLoslassen`. Seit 0.9.3 (Q66) sendet die
Kette nach dem Loslassen noch den Nachlauf der Sprechtaste (`TastenNachlaufMs`, `Sendekette.TasteLesen`) und meldet erst danach `TasteBeendet`
(Klick beim Loslassen); ein Druck im Nachlauf setzt die Phase fort. Einstellung `TastenEinstellungen.NachlaufMs`, Oberfläche
`NachlaufTeil` unter Tasten > Erweitert (Gruppe Sprechtaste im Klappbereich `TastenFeinheiten`, Stil `Klappbereich`; bis 0.9.3
ein eigener Klappbereich `SprechtasteFeinheiten` unter der Zeile Sprechtaste), Messung `MessungNachlauf`.

Server: UDP -> `Funk.Protokoll.Sprache.Empfangspruefung` (Schritte 0-8) -> `Funk.Server.Sprachpfad` und
`Funk.Server.Lage` (Senderecht je Paket, Empfänger, Stempel) -> je Empfänger neu verschlüsselt, Pacer -> UDP.

Empfangen: UDP -> Empfangspruefung (Client) -> `KlangBruecke` (Netz -> Mischer-Netz) -> `KlangMaschine.SpracheEmpfangen` -> `Maschine.Empfangsseite`:
Jitterpuffer je Sprecher (`OpusDekodierer`: FEC, Deep PLC) -> Mischer (Pegel je Funker, Netz, Panorama,
Begrenzer, Soft-Clip) -> `Ausgabewandler` -> WASAPI-Ausgabe. Mischer-Netz 63 gehört der eigenen Stimme: Mithören (nur Headset)
spielt die gesendeten Pakete; die Hörprobe als Aufnahme kodiert die Kette ohne Gate mit einem zweiten Opus-Kodierer in einen
Speicher der `Sendekette` (5 s) und speist ihn danach im 20-ms-Takt in denselben Jitterpuffer.
Anzeige: Ereignisse `Pegel` (50/s) und `EmpfangsPegel` (25/s) aus dem Melde-Thread -> Funk.Client. Seit 0.11.9 (Sprecheranzeige) macht die
`KlangBruecke` aus `EmpfangsPegel` je sprechendem Sprecher Konto, Art und Netze seines letzten Stempels (`SprecherGehoert`) -> über den
Oberflächentakt `Modelle.Sprecheranzeige.Melden` (Filter auf eigene Felder mit hören, Name aus der Praesenz, `KanalName`) -> `Sprecherbild` ->
`Fenster.SprecheranzeigeFenster`.
Meldungen (seit 0.12.0): Push 0x4005 -> `Lagebild.Meldestand` -> `FunkLage.Meldestand` und das Ereignis `MeldungEingegangen` -> über den
Oberflächentakt `Modelle.Meldeanzeige.Eingegangen` (Befehl und Textmeldung mit 8 s, Ton über `IKlangMaschine.TonSpielen` -> `Mischer.Ton`, ein
`Signalton`) und im Bildtakt `Meldeanzeige.Takt` (Stand aus der Lage samt stehender Textmeldung, Countdown mit `IFunkDienst.ServerJetztMs`) ->
`Meldebild` (Plätze und `Textplatz`) -> `Fenster.MeldeanzeigeFenster`.
Die Leitung sendet aus `Modelle.MeldungenViewModel` (Feld auf der Funkfeld-Zuteilung) über `IFunkDienst.MeldungSendenAsync`; "Bereit" kommt aus
`Modelle.Bereitmeldung` (Knopf in der Titelleiste, Taste `Tastenfunktion.Bereit` im `Tastenbetrieb`).
Telemetrie: Jeder Aufnahmerahmen schreibt Pegel, Wirkung und Laufzeit je Stufe ins `Telemetriejournal` (Audio-Thread, ohne Allokation);
`KlangMaschine.Telemetrie` (p50/p95/p99/max, verpasste Fristen) und `TelemetrieLesen`; auf Wunsch schreibt der Melde-Thread daraus das
`Klangprotokoll` (CSV im Ordner der Einstellungen).

Steuerung: TLS mit Geräte-Zertifikat -> `Funk.Protokoll.Steuerung` (Rahmen, JSON, Zustandsautomat) ->
`Funk.Protokoll.Identitaet` (Konten, Geräte) und `Funk.Server.Lage` (Zustand, Aufstellung). Im Client
bildet `Funk.Client.Dienste.FunkClientDienst` den Stand auf die Oberfläche ab (`IFunkDienst`).

## Wo man was ändert

| Anliegen | Ort |
| --- | --- |
| Paketformat, Verschlüsselung, Replay | `src/Funk.Protokoll/Sprache/`, `src/Funk.Protokoll/Schutz/` |
| Steuernachrichten, Fehlercodes, Grenzen | `src/Funk.Protokoll/Steuerung/` |
| Konten, Geräte, Einmalcode, Serverprofil | `src/Funk.Protokoll/Identitaet/` |
| Rechte, Senderecht, Durchsage, Aufstellung | `src/Funk.Server/Lage/` |
| TLS-Annahme, UDP-Relais, Ratenlimits, Pacer | `src/Funk.Server/Netzzugang/`, `src/Funk.Server/Sprachpfad/` |
| Server-Betrieb, Identität, Verwaltung | `src/Funk.Server/Betrieb/`, `src/Funk.Server/Verwaltung/`, `src/Funk.Admin/` |
| Optionen von funk-admin (welcher Befehl was annimmt) | Tabelle und Prüfung `src/Funk.Admin/Befehlsoptionen.cs`, Socketbefehle `Uebersetzung.cs`, offline `Program.cs`, Hilfe `Hilfe.cs`; Abgleich mit Hilfe, HEIMSERVER-PROXMOX.md, BETRIEB.md, HEIMSERVER-HAERTUNG.md und den LIESMICH-Vorlagen `tests/Funk.Tests/AdminWerkzeugTabelleTests.cs` |
| Client-Verbindung, Wiederverbinden, Sprechkanal | `src/Funk.ClientKern/` |
| Opus-, RNNoise-, DeepFilterNet3-, Resampler-, APM-Anbindung | `src/Funk.Klang/Kern/`, `src/Funk.Klang/Nativ/` |
| Jitterpuffer, Mischer, Gate, Klick | `src/Funk.Klang/Verarbeitung/` |
| Funk-Klick einstellen (A11, A63, 0.5.0; Klang und Ort seit 0.6.0 Stufe 2b): rosa Rauschen nach Variante A (30 ms Drücken, 90 ms Loslassen, 900 bis 3500 Hz, jedes Mal ein neues), eigene Taste und andere Sprecher je an/aus mit Lautstärke, Sprachaktivierung dezent oder aus, Anhören | Klang und Pegel `src/Funk.Klang/Verarbeitung/FunkKlick.cs` (`Erzeugen` im Audio-Thread in Puffer aus dem Konstruktor, `FunkKlickEinstellungen` mit Begründung der Voreinstellung, `PegelUeber200Ms`, `AnhoerenAnfordern`, Startwert für Tests); Maschine `IKlangMaschine.Klick`/`KlickAnhoeren`, `KlangMaschine` (neuer `FunkKlick` im Mischer je Einstellung, `KlangMaschinenOptionen.KlickStartwert`); Ablage `ClientEinstellungen.Klick` (`KlickEinstellungen`, ein `klang` älterer Dateien wird übergangen); Modell `src/Funk.Client/Modelle/KlickViewModel.cs` (über `AudioViewModel.Klick`; die Werte sind Teile von Tasten > Erweitert, `EinstellungenViewModel.AbschnitteBauen`); Ansicht `KlickKarte` unter Einstellungen > Tasten > Erweitert, Gruppe Funk-Klick (`Fenster/Bereiche.xaml`; bis 0.9.3 eine Karte im Bereich Audio); Tests `Verarbeitung/KlickTests.cs` (Nachbau der Vorlage tmp\funkklick\varianten.ps1, Band, Spitzen gegen das Knie des Begrenzers), `Maschine/KlickMaschineTests.cs` (Lautsprecher je Startwert, Hörproben als WAV mit `FUNK_HOERPROBEN`), `Oberflaeche/KlickSprechwegeTests.cs` (alle Sprechwege bis zur Wiedergabe), `OberflaecheKlickTests.cs`, `OberflaecheStufe2bTests.Audio_KarteFunkKlick_TexteSchalterKontrast` |
| Auslöser neuer Netze an der Konsole (A62, A64, 0.5.0): ohne Schalter VA, `--taste`, Global immer Taste | `src/Funk.Admin/Uebersetzung.cs` (`netz-setzen`), Tabelle `Befehlsoptionen.cs`, Hilfe `Hilfe.cs`; gespeicherte Netze unberührt (`Lage/Modell.cs`, `Netz.AusloeserStandard`); Test `AdminWerkzeugTabelleTests.AdminWerkzeug_NetzSetzen_SprachaktivierungIstStandard`, Wächter `Uebergang040Tests` |
| Beispielnamen (A65): Max Mustermann statt Sarevocs Namen, Ausgaben nennen "den Eigentümer" | Wächter `tests/Funk.Tests/BeispielnamenTests.cs` (Rudi nirgends, Sarevoc nur als Maintainer, kein Sarevoc in Zeichenketten und XAML-Texten); Vorführdaten `FunkDienstAttrappe.cs`, `FunkDienstAttrappe.Raidabend.cs` (`Mustermann`) |
| Maintainer-Kopfzeile (A58, A59): "JG TacRelay - Maintainer: Sarevoc" in jeder eigenen Quelldatei, Metadaten ohne Rechtsbehauptung | Wächter `tests/Funk.Tests/MaintainerKopfzeileTests.cs` (welche Endungen und Ordner zählen, die Ausnahmen `Uebertragungen`: QrCode.cs und WeichesClipping.cs mit fremdem Vermerk; Position bei Shebang, Kodierungszeile und XML-Deklaration; Leerzeile vor einem PowerShell-Hilfeblock); Metadaten `Directory.Build.props` (Company, Authors, Copyright), `installer/TacRelay.iss` (`#define Maintainer`, AppPublisher, AppCopyright, VersionInfoCompany, VersionInfoCopyright), `THIRD-PARTY-NOTICES.txt` (Absatz Eigener Code); `.cmd`-Paketdateien und Bauvorlagen (csproj, CMake, meson, def) tragen keine Zeile |
| Regler: Knauf immer in der Bahn (Prüfung 02.10.2026) | Wächter `tests/Funk.Tests/Oberflaeche/OberflaecheReglerGrenzenTests.cs` (misst im Speicherbild jeden `Slider` samt Modell hinter der Bindung bei Grenzwerten, NaN, unendlich, alten Dateien und Breiten bis 560; dazu `Schiebeschalter` und `SchwellenGrafik`); Begrenzen beim Setzen `FunkfeldViewModel.LautstaerkeSetzen`, `FunkerZeile.PegelDb`/`Setzen` (`NetzeViewModel.cs`), beim Laden `ClientEinstellungen.Normalisieren`; Spalten der Pegelzeile `Fenster/Bereiche.xaml` (seit 0.10.0 ein `Spaltenwechsel` unter Empfangen > Erweitert, der Platz bricht um, Regler mindestens 100); Knauf des Schiebeschalters folgt auch der Größe des Knaufs (`Steuerelemente/Schiebeschalter.cs`) |
| Aufnahmekette, Gate-Modi, Echoschutz, Hörprobe | `src/Funk.Klang/Maschine/` (Vertrag `IKlangMaschine.cs`) |
| Hörprobe als Aufnahme mit Wiedergabe, Mithören nur mit Headset, keine Sprachaktivierung während der Probe (Fehlerbericht 01.10.2026) | Maschine `src/Funk.Klang/Maschine/Sendekette.cs` (`ProbeStarten`, `ProbeAufnehmen`, `ProbeAbspielen`, `PaketeAusgeben` mit `_vaVerworfen`, `ProbeNachlaufRahmen`), Vertrag `IKlangMaschine.ProbeStarten`/`Probe`, Ende beim Stoppen `KlangMaschine.Geraete.cs`; Oberfläche `src/Funk.Client/Modelle/HoerprobeViewModel.cs` (eine Instanz, App.xaml.cs): seit 0.10.0 genau eine Hörprobe auf Start (`HoerprobeZeile` in `Fenster/Bereiche.xaml`, auch verbunden, Q75; `StartViewModel.HoerprobeCommand`), Mithören unter Einstellungen > Senden > Erweitert (`MithoerenSchalter`, E19); bis 0.9.3 auch auf der Seite Audio und in `Fenster/Schwellenbereich.xaml`; ohne laufendes Audio gesperrt mit Grund (`ProbeGesperrt`, Startseite `StartViewModel.HoerprobeGesperrt`, seit Stufe 2b); Tests `tests/Funk.Tests/Maschine/HoerprobeTests.cs`, `Oberflaeche/OberflaecheHoerprobeTests.cs`, `OberflaecheHoerprobeBilderTests.cs`, `OberflaecheStufe2bTests.Hoerprobe_OhneAudioGesperrt_MitGrund` |
| Lautstärke der eigenen Stimme (Klangstufe A): automatisch = Halteregel (Grundpegel aus ganzen gesendeten Äußerungen, Schritt nur nach 600 ms Pause und nur ohne Sendung, nach 10 s Sprache höchstens 1 dB, Abweichler über 10 dB erst ab dem dritten in Folge, fremde Stimmen nach Abstand zum Rauschen nie, gelernter Wert setzt fort, anderes Mikrofon lernt neu), aus = feste Verstärkung mit Einmessen; Sicherheitsbegrenzer; Stelle vor RNNoise | Regel `src/Funk.Klang/Verarbeitung/Pegelstufe.cs` (`LautstaerkeEinstellung`, `EinmessStand`, Konstanten mit Messbegründung), Einbau `Sendekette.Verarbeiten` (vor RNNoise, Sendezustand des vorigen Rahmens oder Taste als `gesendet`, `PegelstufeHinterRnnoise` nur Vergleich), neu lernen nach Gerätewechsel `KlangMaschine.LernEingangPruefen` (aus `AufnahmeOeffnen`, Gerät in `TelemetrieStand.Eingang`), Vertrag `IKlangMaschine.Lautstaerke`/`EinmessenStarten`/`Einmessen`, Optionen `KlangMaschinenOptionen.MitPegelstufe` und `Apm` (ab Werk `ApmEinstellungen.EchoUndHochpass`, die alte Kette ist `Sprechfunk` ohne Pegelstufe); Ablage `AudioEinstellungen.AutomatischeLautstaerke`, `FesteVerstaerkungDb`, `GelernteVerstaerkungDb`, `GelernteVerstaerkungEingang` (nachsichtige Wandler in `EinstellungsSpeicher.cs`); Modell `src/Funk.Client/Modelle/LautstaerkeViewModel.cs` (über `AudioViewModel.Lautstaerke`), Ansicht `LautstaerkeTeil` (der Schalter, seit 0.11.1 offen unter Einstellungen > Senden, rechte Spalte) und `FesteVerstaerkungTeil` (das Einmessen, unter Senden > Erweitert) (`Fenster/Bereiche.xaml`; bis 0.9.3 Karte Verarbeitung im Bereich Audio); Tests `Verarbeitung/PegelstufeTests.cs`, `Maschine/LautstaerkeTests.cs`, `Maschine/PumpenTests.cs`, `Maschine/HalteregelTests.cs` (Gegenprüfung: andere im Raum, Sprechtaste, Warmstart, Gerätewechsel), `Oberflaeche/OberflaecheLautstaerkeTests.cs`; Messung `Maschine/MessungKlangA.cs` und `Maschine/MessungGegenpruefungKlangA.cs` mit `Pumpmessung.cs` und dem Klangkorpus `tests/Funk.Tests/Daten/klangkorpus/` (Erzeuger `klangkorpus-erzeugen.ps1`, Windows-Sprachausgabe) |
| Rauschfilter Standard (RNNoise) / Stark (DeepFilterNet3) / Aus (Klangstufe B): genau einer wirkt, Wechsel zeitrichtig mit 50 ms Blende, mehr Verzug nur in Pausen, Stark rechnet 500 ms warm, Rückfall auf Standard bei drei verpassten Fristen in 1 s nach dem Anlauf oder Fehler der DLL | Wahl und Blende `src/Funk.Klang/Maschine/Rauschfilterwahl.cs` (`Rauschfilter`, ohne native Filter testbar), Einbau `Sendekette.Verarbeiten` (`WunschUebernehmen`, `StarkUebergeben`/`AusgedientenHolen`, `RechenzeitPruefen`, p und Wirkung mit dem Verzug der Kette), Anlegen außerhalb des Audio-Threads `KlangMaschine.StarkVorbereiten` (Threadpool; Freigabe in `Zustellen`), Vertrag `IKlangMaschine.Rauschfilter`/`Rauschfilterstand`, Hülle `src/Funk.Klang/Kern/DeepFilterProzessor.cs` (`DeepFilterSchwellen.Voll`), DLL `native/eigen/deepfilternet/` (Rust-C-Schicht über libDF) mit `native/skripte/deepfilternet.ps1`; Ablage `AudioEinstellungen.StarkerRauschfilter` neben `Rauschunterdrueckung` (ältere Clients lesen weiter), Modell `AudioViewModel` (`RauschfilterStandard/Stark/Aus`, `RauschfilterHinweis`, `RauschfilterRueckfall` in die Meldezeile über `App`), Ansicht `RauschunterdrueckungSchalter` mit Standard und Stark seit 0.11.1 offen unter Einstellungen > Senden, linke Spalte (0.10.0 und 0.11.0 Gruppe Filter im Klappbereich) in `Fenster/Bereiche.xaml` (bis 0.9.3 Karte Verarbeitung); Tests `AnbindungDeepFilterTests`, `Maschine/RauschfilterwahlTests`, `Maschine/RauschfilterMaschineTests`, `Oberflaeche/OberflaecheBereicheTests.Audio_Rauschfilter_*`; Messung `Maschine/MessungKlangB.cs` mit `Stoergeraeusche.cs` (Hörproben mit `FUNK_HOERPROBEN`) |
| Klangfarbe der eigenen Stimme (Q70 bis Q72, 0.9.3): Wärme 160 Hz, Klarheit 3000 Hz, Schärfe 6000 Hz je -6 bis +6 dB, lautheitsneutral nach der Norm-Sprachkurve, 20 ms Blende, eigener Begrenzer, neutral überbrückt; Gate und Anzeige sehen den Pegel von davor | Stufe `src/Funk.Klang/Verarbeitung/Klangfarbe.cs` (`KlangfarbeEinstellung`, Ausgleich, Bänder und Herkunft der Zahlen), Filter `Biquad.cs` (gemeinsam mit `FunkKlick`), Einbau `Sendekette.Verarbeiten` (Pegel davor, `_sende.Verarbeiten(..., pegel)`), Gate `SprachGate.Verarbeiten(pegelDbfs)`, Vertrag `IKlangMaschine.Klangfarbe`; Ablage `AudioEinstellungen.KlangfarbeWaermeDb/KlarheitDb/SchaerfeDb`; Modell `KlangfarbeTeil` in `src/Funk.Client/Modelle/Feinheiten.cs` (über `AudioViewModel.Klangfarbe`, Teil von `EinstellungenViewModel.AbschnittSenden.Feinheiten`), Ansicht `KlangfarbeTeil` rechts im Klappbereich `SendenFeinheiten` unter Einstellungen > Senden (`Fenster/Bereiche.xaml`); Tests `Verarbeitung/KlangfarbeTests.cs`, `Maschine/KlangfarbeMaschineTests.cs`, `Oberflaeche/OberflaecheKlangfarbeTests.cs` |
| Telemetrie der Sendekette und Klangprotokoll (Klangstufe A) | Typen `src/Funk.Klang/Maschine/Klangtelemetrie.cs` (`KlangStufe`, `TelemetrieEintrag`, `TelemetrieStand`), Journal und Laufzeitverteilung `Telemetriejournal.cs`, Datei `Klangprotokoll.cs`, Zeitmessung je Stufe `Sendekette.Verarbeiten` (Opus-Anteil aus `Sendeaufbereitung.LetzteKodierTakte`), Vertrag `IKlangMaschine.Telemetrie`/`KlangprotokollOrdner`; Schalter in der Oberfläche `LautstaerkeViewModel.Klangprotokoll`; Tests `Maschine/TelemetrieTests.cs` |
| Eigener Druck-Klick mit Lautsprechern aus der Echoreferenz (Klangstufe A, Befund der Gegenprüfung 2c) | Zähler `FunkKlick.EigeneDrueckeGestartet`, Meldung `KlangMaschine.EigenenKlickPruefen` (Wiedergabe-Thread), Ausblenden `Sendekette.EigenenKlickMelden`/`ReferenzEinspeisen` (Fenster in Rahmennummern des Loopbacks, `KlickAusblendenRahmen`, `RahmenRing.Geschrieben`/`NaechsterLesen`; `EigenenKlickAusblenden` nur Vergleich); Tests `Maschine/KlickLautsprecherTests.cs` (mit Verzug und Rückstand `AblaufMitVerzug`), Messungen `MessungKlangA.Messung_KlickImLautsprecher`, `MessungGegenpruefungKlangA.Messung_GP_KlickVerzug`/`Messung_GP_KlickNebenwirkung` |
| Audiogeräte, Formatwandlung | `src/Funk.Klang/Geraete/` |
| Sprechtaste (Raw Input, DirectInput-Joystick) | `src/Funk.Klang/Eingabe/` |
| Vorsichtig belegen im Schritt Sprechtaste beim ersten Start (0.10.0, Befund der Prüfung): Tab, Eingabe, Leertaste, Pfeile, Pos1, Ende, Bild auf und ab zählen im eigenen Fenster nicht; Strg, Alt, Umschalt und Windows nur allein beim Loslassen; nie eine Taste in einem Kürzel (Alt+Tab, Strg+2) | Zustand `SprechtastenZustand.Vorsichtig`, `ModifikatorUnten`, `IstModifikator`, `BedientFenster`, `VorsichtigLernen` in `src/Funk.Klang/Eingabe/RawInputAuswertung.cs`; Quelle `RawInputSprechtaste.NaechsteTasteAsync(vorsichtig, ct)` (GetAsyncKeyState für Modifikatoren, die schon vor dem Belegen unten waren), Schnittstelle `ISprechtasteQuelle.NaechsteTasteAsync(bool, CancellationToken)` (Standard: wie ohne), Verbund reicht weiter; Aufruf `TastenViewModel.BelegenAsync(..., vorsichtig)` aus `StartViewModel.WartenAsync` (nur ohne Frist); Tests `TastenTests.Zustand_VorsichtigBelegen_KeineKuerzelUndKeineBedientasten`, `OberflaecheUmbauStartTests.Umbau_Erststart_WartenEscSpaeterUndOhneQuelle` |
| Klang mit Funkbetrieb verbinden | `src/Funk.Client/Klang/KlangBruecke.cs` |
| AEC3-Konfiguration (C-Schicht) | `native/eigen/webrtc-apm/`, `src/Funk.Klang/Kern/EchoProzessor.cs` |
| Echo-Funker | `src/Funk.ClientKern/Echo/`, `src/Funk.Echo/` |
| Probefunk (Testserver mit Selbstfreigabe am Loopback) | `tests/Funk.Testserver/` |
| Paket bauen | `tools/paket-bauen.ps1` (Gesamtpaket mit Probefunk), `tools/paket/`; Serverpaket für den Heimserver aus einem Gesamtpaket: `tools/serverpaket-bauen.ps1` mit `tools/paket/LIESMICH-Server.txt` |
| Test- und Werkzeuglaeufe absichern (Spiel, Geräte, Zone) | `tests/Funk.Tests/Spielschutz.cs`, `tests/Funk.Tests/SicherungTests.cs`, `tools/dotnet-umgebung.ps1`, `tests/testlauf.ps1` |
| Fenster, Bereiche, Schwellenbereich | `src/Funk.Client/Fenster/`, `src/Funk.Client/Steuerelemente/` |
| Anzeige-Logik (MVVM) | `src/Funk.Client/Modelle/` |
| Farben, Stile, Symbole, Farbstellung Anthrazit (seit 19.10.2026 die einzige; Leitfarbe, Akzent Petrol, rote Schrift, Schließen, Glut) | `src/Funk.Client/Design/` (Palette.cs und Farben.xaml gleich halten; `Farbvorschlaege.cs`; Tests `OberflaecheFarbvorschlagTests`, `OberflaecheKontrastTests`, `OberflaecheStellungAnthrazitTests`) |
| Gespeicherte Einstellungen | `src/Funk.Client/Ablage/` |
| Serverprofil und Profilschlüssel im Client prüfen | `src/Funk.Client/Dienste/Profilpruefung.cs` |
| Fehlertexte der Verbindung | `src/Funk.Client/Dienste/Verbindungstexte.cs`, Nummern `Fehlernummern.cs` |
| Lesbarkeit der Durchsichtigkeit (Mindestdeckung) | `src/Funk.Client/Design/Lesbarkeit.cs` |
| Durchsichtigkeitsregler in der Titelzeile des Funkfelds | `src/Funk.Client/Modelle/FunkfeldViewModel.cs` (`ReglerWert`), `src/Funk.Client/Fenster/FunkfeldFenster.xaml` |
| Portabgleich Profil gegen Serverkonfiguration | `src/Funk.Server/Betrieb/Profilabgleich.cs` |
| Fensterlage und -größe merken, Monitore | `src/Funk.Client/Ablage/Platzierung.cs`, `src/Funk.Client/Fenster/Platzfuehrung.cs`, `src/Funk.Client/Fenster/Bildschirme.cs` |
| Größe des Hauptfensters je Bereich (0.6.0: Klein = Startseite, Seite = übrige Seiten, Brett, Verwaltung, Seite Sprachaktivierung; seit 0.10.0 Klein mindestens 900 x 900, Q89, und ohne die Klassen mit aufgeklappter Sprachaktivierung) | Rechnung `src/Funk.Client/Fenster/Fenstergroessen.cs` (`KleinMindest` 900 x 900, Klein öffnet so klein, eine kleinere gemerkte Größe wie 760 x 660 aus 0.9.3 hebt `Groesse` auf die Mindestgröße; `SeiteVoreinstellung` 960 x 700; `KlasseFuer(kennung)`; Schlüssel `start`, die aus Stufe 1 und 2 `Veraltet`, `start-aufgeklappt` und `seite-aufgeklappt` bleiben für ältere Clients ungelesen stehen; `Mindest(klasse, bildschirm)` kürzt auf den Arbeitsbereich, dann rollt Start), Anwendung `Hauptfenster.xaml.cs` (`KlasseAnwenden`, `GroesseAnwenden`, Rückweg nach dem Schieben; Umstieg aus 0.5.x: Startseite klein an der alten Ecke; seit Q93 a mit der Spalte "Wer ist da", eigene Zeile unten), gemerkt in `ClientEinstellungen.HauptfensterGroessen` (`null` = Datei vor 0.6.0); Tests `OberflaecheFenstergroessenTests`, `OberflaecheStufe2bTests` (echte Datei von 0.5.1 in `tests/Funk.Tests/Daten/einstellungen-0.5.1`, Klein auf Full HD und Laptop 150 %), Messung der Startseite `OberflaecheStartseiteTests.Start_Sichtproben_UndPruefungen` |
| Statuszeile "Mikrofon" unten im Hauptfenster (0.10.0, Q87, Q90, Vorlage m-8; ersetzt die Leiste der Sprachaktivierung und ihr Aufklappen aus 0.6.0 Stufe 2b): Pegel mit der Linie, Marke SENDET, Ruhe oder Getrennt, Hinweissatz; auf jeder Seite, ohne dBFS und ohne Aufklappen | Modell `src/Funk.Client/Modelle/StatuszeileViewModel.cs` (`StatusMarke`, `Takt` nur bei neuer Meldung, Getrennt nach `FunkLage.IstAktiv`), gebaut in `HauptViewModel` (`Statuszeile`, `Takt`); Sätze `PegelVerlauf.Hinweis`/`HinweisText` (neu `Sendeweg.OhneSprechtaste` aus `FunkfeldViewModel.WegNachLage`/`SprechtasteBelegt`, neu `UnterMindestText` aus `PegelMeldung.Zurueckgehalten`: `RahmenMerkmal.Zurueckgehalten` in `Sendekette`, zusammengeführt in `KlangMaschine.Zustellen`); Ansicht Border `Statuszeile` in `Fenster/Hauptfenster.xaml` (`StatusMarke`, `StatusMarkenText`, `StatusPegel`, `StatusHinweis`), schmal in `Hauptfenster.TitelleisteSetzen` (Balken unter 700 Breite 120; die Grenzen 900 und 700 mit `RundungsSpiel` 1, seit AP4: in Klein 900 maß der Inhalt 897,7); Tests `Oberflaeche/OberflaecheUmbauRahmenTests.cs`, `OberflaechePegelGelbTests.PegelGelb_Statuszeile_HinweisLesbarNebenDemBalken`, `Maschine/KlangC1Tests.C1_Zurueckgehalten_InDerPegelmeldung`. Seit dem Befund der Prüfung: Hovertext der ganzen Zeile `StatuszeileViewModel.Hilfe`; die Linie vor der ersten Meldung aus `SchwellenViewModel.LinieDb` (Parameter `linieOhneMeldung`); "Stimme unter der Mindest-Funklautstärke" nicht in der Meldezeile (`FunkfeldViewModel.MeldezeileNachfuehren`, Test `OberflaecheUmbauRahmenTests.Umbau_Meldezeile_OhneSatzZurMindestFunklautstaerke`) |
| Unterpunkt Sprachaktivierung (0.6.0 Stufe 2c; seit 0.10.0 unter Einstellungen, E3): wenn sie benutzt wird oder ihre Seite offen ist, ganze Schwellenseite mit Platz; Strg+T öffnet sie (E2), seit dem Befund der Prüfung, solange "Senden mit Stimme" an ist, auch ohne Benutzung | Bedingung `FunkfeldViewModel.SprachaktivierungBenutzt` (ein Feld mit sendbarem Kanal auf VA, in `AusloesungGeaendert`); Leiste links `HauptViewModel.Navigation` (Bereiche plus Unterpunkt nach `EinstellungenKennung`, `NavigationAbgleichen` mit Benutzung, offener Seite oder `_seiteOeffnet`; `SprachaktivierungZeigen` springt zu Einstellungen, wenn "Senden mit Stimme" ausgeht; nach dem Verlassen der Seite fällt der Unterpunkt in `OnPropertyChanged` weg, über den Takt; `Bereiche` sind seit 0.10.0 Start und Einstellungen für Strg+1 und 2), `SprachaktivierungEinstellenCommand` (Strg+T, "Genau einstellen ...", gesperrt nur mit "Senden mit Stimme" aus, Grund `SprachaktivierungGesperrt`), `Bereich.Unterpunkt` (eingerückt, Vorlage in `Hauptfenster.xaml`), `IstSprachaktivierungsseite`, `MitBildlauf`; der `Schwellenbereich` steht seit 0.10.0 fest im Platz `SchwellenSeitenPlatz` (bis 0.9.3 zwischen unten und der Seite umgesetzt), Fokus in die Navigation, wenn die Seite geht (`Hauptfenster.FokusPruefen`); Anordnung `Schwellenbereich.AnordnungSetzen` (Überschrift, Mindest-Funklautstärke links unter der Grafik; Hörprobe und Mithören seit 0.10.0 nicht mehr hier, E19); Größe `Fenstergroessen.KlasseFuer`; Tests `OberflaecheStufe2cTests` (Bedingung, nur auf der Seite, Zugriffstasten, kein Rollbalken, Sichtproben), `OberflaecheBereicheTests.Haupt_StrgT_OeffnetDieSeite_OhneUnterpunktWirkungslos` |
| Klappbereich "Erweitert:" (0.10.0, E6; bis 0.11.0 hieß er in der Oberfläche anders, die Bezeichner im Code und der Schlüssel `offeneFeinheiten` blieben): einer je Abschnitt der Einstellungen, Kopfzeile "Erweitert: (ab Werk) ..." oder "Erweitert: (1 selbst gestellt) ..." mit dem Wort fett (Sarevoc 12.10.2026), Satz neben "Zurück auf Werkseinstellung", Zustand gemerkt; der Wächter `OberflaecheUmbauErweitertTests.Erweitert_Waechter_KeinAltesWortInTextenQuelltextUndDoku` hält das alte Wort aus Oberfläche, Zeichenketten, XAML-Texten, Handbuch und LIESMICH | Behälter `Feinheiten` (`Wort`, `Kopfzeile`, `Kopfrest`; die Ansicht setzt `Wort` fett, Header des Expanders ist der Behälter) und Teile `Einstellteil` (`SelbstGestellt`, `Zuruecksetzen`, `Geaendert`), `NachlaufTeil`, `KlangfarbeTeil` (eigenes "Zurück auf neutral") in `src/Funk.Client/Modelle/Feinheiten.cs`; Kennungen `Abschnittskennung`, gemerkt in `AnzeigeEinstellungen.OffeneFeinheiten` (alte Kennungen `AlteFeinheiten` beim Normalisieren umgedeutet); seit AP3 ein Behälter je Abschnitt in `EinstellungenViewModel.AbschnitteBauen` (`Einstellabschnitt.Feinheiten`), weitere Teile `WertTeil<T>`, `KanalTeil` in `Modelle/Einstellabschnitt.cs` (`SprachaktivierungTeil` bis Q117, Sarevoc 13.10.2026: die Seite Sprachaktivierung zählt nicht mehr und hat ihre eigenen Standardwerte; der Satz dazu am Knopf über `Feinheiten.ZurueckZusatz`); Ansicht Klappbereiche `IchFeinheiten`, `TastenFeinheiten`, `EmpfangenFeinheiten`, `SendenFeinheiten`, `FunkfeldFeinheiten`, `FehlersucheFeinheiten` und `FeinheitenFuss` in `Fenster/Bereiche.xaml`, Stil `Klappbereich` und die Abschnittsstile (`Abschnitt.*`, `Automatik*`, `Gruppe`) in `Design/Stile.xaml`; zwei Spalten, die rutschen: `Steuerelemente/Spaltenwechsel.cs`; Tests `OberflaecheNachlaufTests`, `OberflaecheKlangfarbeTests`, `OberflaecheUmbauRahmenTests`, `OberflaecheUmbauEinstellungenTests`, `OberflaecheUmbauErweitertTests` |
| Vorlauf des Gates (Klangstufe C1): ab Werk 120 ms, von außen einstellbar, Burst höchstens 15 vergangene Rahmen, PREROLL nur auf Paketen aus dem Vorlauf (höchstens 7, FUNKPROTOKOLL 5.6) | Einstellung `GateEinstellung.VorlaufMs` (`IKlangMaschine.cs`, `GateVorgaben.VorlaufAbWerkMs`, `Bereinigen`), gestellt in `GateFuehrung.Einstellung`, am Gate `SprachGate.VorlaufMs` und `GateEinstellungen.BurstHoechstRahmen` (`Oeffnen`), Kennzeichen `Sendeaufbereitung.Verarbeiten` (PREROLL nur, wenn auch die zweite Hälfte Vorlauf ist); Sender `Funk.Protokoll.Sprache.SprechphasenGeber.MaxPreroll`; Tests `Maschine/KlangC1Tests.C1_Vorlauf_*` (mit SprechphasenGeber und Jitterpuffer); Rekonstruktion in Messungen `Pumpmessung.GesendeteRahmen` |
| Mindest-Funklautstärke und Korridor (Klangstufe C1): relativ zur gelernten Funkstimme gemessen vom Anker (Grundpegel beim Einmessen oder Lernen), ab Werk 25 dB darunter; keine Untergrenze der Linie, sondern Freigabe der Phase über den Vorpuffer (lautestes 150-ms-Fenster seit dem Anlauf mindestens 8 dB über der Mindest-Funklautstärke, sonst Zustand Zurückgehalten; Fortsetzung bis 600 ms Pause ohne Prüfung; nie für die Sprechtaste); Obergrenze Stimme minus 6 dB in Automatisch und Lernend; ohne gelernte Stimme wie 0.6.0; Wünsche per Maximum | Gate `src/Funk.Klang/Verarbeitung/SprachGate.cs` (`MindestDbfs`, `FreigabeZuschlagDb`, `FreigabeFensterRahmen`, `GateEinstellungen.FortsetzungRahmen`, `LautGenug`, `GateZustand.Zurueckgehalten`, `ZurueckgehalteneAnlaeufe`), Führung `src/Funk.Klang/Maschine/GateFuehrung.cs` (`Linie`, `Grenzen`, `StimmeDb`, `BezugDb`, `AnzeigeLinieDb` für die rote Linie, `ZusatzWunschDb` als Stelle für den Wächter aus C2, `Korridor`, `MindestAlsLinie` nur zum Vergleich), Vorgaben `GateVorgaben.MindestAbstandAbWerkDb`, `ObergrenzeAbstandAbWerkDb`, Stimme und Anker aus `Pegelstufe.StimmeBekannt`/`GrundpegelDbfs`/`AnkerDbfs` (`Sendekette.StimmeAmGate`, `BezugDb`), Stand nach außen `KorridorStand` (`IKlangMaschine.Korridor`, `Sendekette.KorridorMelden`); Messungen `tests/Funk.Tests/Maschine/MessungKlangC1.cs` (Wert ab Werk, Nachweis Einmessen, FUNK_MESSUNG=1, Abwägung mit FUNK_C1_ZUSCHLAEGE und FUNK_C1_FENSTER); Tests `KlangC1Tests.C1_Freigabe_*`, `C1_Korridor_*`, `C1_Verklemmung_*` |
| Pegelstufe lernt nur Freigegebenes (Klangstufe C1): je Rahmen gesendet UND Sprache ab 0,9 UND kein Echoverdacht UND (ab C2) Wächter; je Äußerung nicht unter der Mindest-Funklautstärke (vom Anker aus, Sprechtaste ausgenommen); ohne gelernte Stimme ohne die Pegelbedingung | `src/Funk.Klang/Maschine/Lernfreigabe.cs` (`LernfreigabeLage.WaechterEigeneStimme` als Stelle für C2), Aufruf `Sendekette.Verarbeiten`, Wirkung `Pegelstufe.Verarbeiten(..., freigegeben, sprechtaste)`, `MindestAbstandDb`, `AeusserungAbschliessen`; eingemessener Startwert gilt auch nach dem Lernen `LautstaerkeEinstellung.Eingemessen`; Tests `KlangC1Tests.C1_Lernfreigabe_*`, `C1_Pegelstufe_*`, `C1_Kette_*`, `C1_Eingemessen_*` |
| "Mindestlautstärke einmessen" (Klangstufe C1): Satz wie am Funk, dann leise; Linie bei leise plus ein Drittel, unter 8 dB Unterschied wiederholen; setzt Grundverstärkung, Untergrenze und Anker auf einmal; bis 0.9.3 ein Hinweis auf der Startseite, bis einmal eingemessen wurde (seit 0.10.0 nicht mehr, Q87) | Messung `src/Funk.Klang/Verarbeitung/Mindesteinmessung.cs` (gefüttert in `Sendekette.Verarbeiten`, Sprachaktivierung dabei gesperrt), Vertrag `IKlangMaschine.MindestEinmessenStarten`/`MindestEinmessen`; Oberfläche `src/Funk.Client/Modelle/KorridorViewModel.cs` (`EinmessenCommand`, `Uebernehmen`, `ZeileText`, `ZeigeEinmessHinweis`), Ablage `GateWerte.MindestEingemessen`, Knöpfe neben dem Pegelbalken in `Fenster/Bereiche.xaml` (`AudioEinmessenKnopf` mit `KorridorViewModel.EinmessKnopfKurz` "Einmessen" unter Einstellungen > Senden in der Funkschwelle `SendenFunkschwelle`, seit 0.11.1 offen, bis 0.9.3 im Bereich Audio; auf der Startseite bis 0.9.3 `StartEinmessenKnopf`, seit 0.10.0 nicht mehr, Q87) und in `Fenster/Schwellenbereich.xaml` (`MindestTeil`); Tests `KlangC1Tests.C1_Mindesteinmessung_*`, `C1_MindestEinmessen_*`, `Oberflaeche/OberflaecheKlangC1Tests.C1_Einmessen_*` |
| Korridor am Pegelbalken und in der Schwellengrafik (Klangstufe C1, Variante B): Linien im Akzent mit runden Griffen, blau getönte Zone zum Verschieben, rote Linie ohne Griff, Tooltips in Wörtern, Tastatur | Modell `KorridorViewModel` (ein Exemplar für den Balken unter Einstellungen > Senden (Funkschwelle) und die Seite Sprachaktivierung, bis 0.9.3 auch Startseite und Audio: `AudioViewModel.Korridor`, `SchwellenViewModel.Korridor`, verdrahtet in `App.xaml.cs`), Balken `src/Funk.Client/Steuerelemente/Pegelbalken.cs` (`Korridor`, `Band`, `Treffer`, `Hilfe`, `Taste`, `Ziehen`), Grafik `Steuerelemente/SchwellenGrafik.cs` (`Treffer`, `Hilfe`, Ziehen.Untergrenze/Obergrenze/Zone), Zone `Akzente.ZoneFlaeche`; Ablage `GateWerte.MindestAbstandDb`, `ObergrenzeAbstandDb`, Umstieg aus 0.6.0 `GateWerte.AufC1Umstellen` und `AudioEinstellungen.AufC1Umstellen` (Dateiversion 2), `AudioEinstellungen.FestEingemessen`; Attrappe `KlangMaschinenAttrappe.Korridor`/`MindestEinmessen`; Tests `Oberflaeche/OberflaecheKlangC1Tests.cs` (auch Sichtproben mit FUNK_BILDER) |
| Feste Größe der Schwellengrafik auf der Seite Sprachaktivierung (Klangstufe C1, Punkt 9) | `Fenster/Schwellenbereich.xaml.cs` (`SeitenGrafik`, `AnordnungSetzen`; als Seite steht die Mindest-Funklautstärke links unter der Legende, die Hörprobe dort nur bis 0.9.3, `Umsetzen`); Test `OberflaecheKlangC1Tests.C1_Seite_SchwellengrafikFesteGroesse` |
| Start immer auf der Startseite, Bereich nur in der Sitzung (Klangstufe C1, Q25); eigener Funk-Klick nur mit Headset (Q6) | `HauptViewModel` (Konstruktor, `OnAktiverBereichChanged`), `ClientEinstellungen.Bereich` nur noch gelesen; Klick `KlickEinstellungen.FuerMaschine(Hoerweise)`, `KlickViewModel.HoerweiseGeaendert`/`EigenerKlickHinweis`, Aufruf aus `AudioViewModel.HoerweiseSetzen`; Tests `OberflaecheKlangC1Tests.C1_Start_*`, `OberflaecheKlickTests.C1_EigenerKlick_*` |
| Auto Dauersend-Dämpfung und Mikro stumm in der Oberfläche (Klangstufe C2b): gelbes "!" des eigenen Urteils vor "Senden: ..." im Funkfeld und in der Meldezeile (seit 0.11.0 dort ein kurzer Satz, `DauersendViewModel.Meldetext`); "!" an Dauersendern, wo ihr Name steht (Kachel in Raster und Liste, bis 0.11.4 Wer ist da, "Je Person" unter Einstellungen > Empfangen > Erweitert, bis 0.9.3 Meine Kanäle, Kopfzeile "spricht:") mit Auswahl stumm / wieder normal / nicht mehr dämpfen; Schalter und Stufe in Wörtern unter Empfangen > Erweitert, Gruppe Dauersender (bis 0.9.3 auf der Audio-Seite); Tasten "Dauersender stumm" (Q8) und "Mikro stumm" (Q32), ab Werk unbelegt; Mikro-Knopf nur in der Titelzeile des Funkfelds zwischen Text und Regler (Q34, der Regler bleibt an den Fensterknöpfen, A30), auch für Stummschaltungen der Einsatzleitung aus dem Lagezustand (Q33: Schloss, "bis Uhrzeit", rote Punkte je Kanal, Ablauf nach der Serveruhr); "stumm" an den Pegelbalken | Modelle `src/Funk.Client/Modelle/DauersendViewModel.cs` (`DauersendViewModel`: `EigenesUrteil`, `EigeneHilfe`, `ZeichenVon`, `Handeln`, `StummTaste`; `DauersendZeichen` mit `Wahlen` und `WaehlenCommand`, `DauersendWahl`) und `MikroViewModel.cs` (`Zustand`, `Querstrich`, `Leuchte`, `LeitungBis`, `KnopfHilfe`, `KanalSatz`, `Takt`, `TasteGedrueckt`), beide gehalten von `FunkfeldViewModel` (`Dauersend`, `Mikro`, `Meldezeile`, Uhr `jetzt`); `KachelViewModel` (`DauersendZeichen`, `StummGeschaltet`, `RoterPunkt`), `FunkerZeile.Dauersend`, `HauptViewModel.SprechendZeichen`; `AudioViewModel` (`DaempfungAn`, `Stufe*`, `DaempfungText`, `Stumm`); Tasten `Tastenfunktion.DauersenderStumm`/`MikroStumm` (`src/Funk.Klang/Eingabe/Tastenbelegung.cs`, in `TastenEinstellungen.Weitere`), `TastenViewModel.Funktionen`, `Tastenbetrieb`; Lage `FunkLage.Stumm`/`StummschaltungenAn`, `StummschaltungInfo`, `LageAbbildung` (aus `Mitgliedschaften.stumm`); Meldezeile `Zeilenmeldung.Ausrufe`, `Meldezeiten.UrteilMs`/`DauersenderTasteMs`/`MikroTasteMs`/`MikroIstStummMs`, `MeldezeileFenster.xaml`; Ansicht `Fenster/FunkfeldFenster.xaml` (`EigenesUrteilZeichen`, `KopfMitte` mit `MikroUnterlage`/`MikroKnopf`, `DauersendZeichen` in beiden Vorlagen, Stile `Funkfeld.Ausrufe`, `Mikro.Knopf`, `Mikro.Symbol`, roter Punkt `Punkt.Kreis`/`Punkt.Strich`), `Bereiche.xaml` (`DaempfungKarte`, Funker je Person, `Stumm` an `AudioPegel`; `StartPegel` gibt es seit 0.10.0 nicht mehr), `Hauptfenster.xaml` (`SprechendZeichen`, seit 0.10.0 `StatusPegel` in der Statuszeile statt `LeistenPegel`); `Design/Stile.xaml` (`Ausrufe`, `Klappwahl.Ausrufe`), `Symbole.xaml` (`Symbol.MikrofonStumm`, `Symbol.Ausrufe`), `Farben.xaml` (`Pinsel.Ausrufe`); `Steuerelemente/Pegelbalken.cs` (`Stumm`, `HilfeStumm`); Tests `Oberflaeche/OberflaecheKlangC2bTests.cs` (Sichtproben mit FUNK_BILDER). Nacharbeit C1: M2 `KorridorViewModel.Greifen`/`KorridorVerschieben(KorridorGriff, ...)` mit `Pegelbalken.Druecken`/`Bewegen`/`Loslassen` und ebenso `SchwellenGrafik`; N2 `KorridorViewModel.HilfeUntergrenze`, `HilfeEinmessen`, `StandText` |
| Grenzlinie der Automatik (0.6.0 Stufe 2c, seit Klangstufe C1 in der Oberfläche durch den Korridor ersetzt, im Klangkern nur noch ohne gelernte Stimme): rote Linie zeigt nur an, Griff nur an ziehbaren Linien, Zone über der Grenzlinie | Klangkern `src/Funk.Klang/Maschine/GateFuehrung.cs` (`Linie`: Automatisch nie unter `GrenzlinieDb`), Vertrag `GateEinstellung.GrenzlinieDb` (`IKlangMaschine.cs`, ab Werk `GateVorgaben.MinPegelDb`, `Bereinigen`); Lernend: Korridor-Untergrenze hat die Rolle; Ablage `GateWerte.GrenzlinieDb` (ab Werk -90, ganz unten in der Grafik); Modell `SchwellenViewModel` (`ZeigeGrenzlinie`, `GrenzlinieZiehen`, `GrenzlinieText`, `GrafikHilfe`, `LinieZiehen` nur ohne Automatik, `LinieSchieben` je Modus); Grafik `Steuerelemente/SchwellenGrafik.cs` (`Treffer`; rote Linie `Akzente.AutomatikStift` im Rot für Schrift der Farbstellung); Attrappe `KlangMaschinenAttrappe` (Vorführszene); Tests `Maschine/GateModusTests.cs` (`Automatisch_Grenzlinie_*`, `Grenzlinie_*`), `OberflaecheSchwellenTests` (Ziehen, Speichern, Griffe), Klangvergleich mit `MessungSpielton`/`MessungAntwortUndKlick`, Wirkung je Grenzlinie `tests/Funk.Tests/Maschine/MessungGrenzlinie.cs` (dieselben Szenen, Einstellung über `Pruefstand.GateFuerMessung`; FUNK_MESSUNG=1, FUNK_GRENZLINIEN) |
| Hilfetexte (Tooltips) an Knöpfen | Glasdesign `extern/Glasdesign/Hilfe.cs` (`gd:Hilfe.Text`, `gd:Hilfe.Taste`, `gd:Hilfe.Gesperrt`, 500 ms, Schalter `Hilfe.Zeigen` aus `Anzeige.Hilfetexte`); Anzeigedauer unbegrenzt vorab in `App.HilfeEinrichten` (vor dem ersten Hilfetext, auch im Bildlauf der Tests); Aussehen `Glas.Tooltip`; Wächter für Client und Verwaltungsteil, Knöpfe, Halteknopf und CheckBox `OberflaecheHilfeTests` (Ausnahmeliste `OhneHilfe` seit Stufe 2 leer) |
| Glasdesign: feste Kopie, Einbindung, Stile (0.6.0, Stufe 2; seit 19.10.2026 Fassung 0.5.0-beta.13 mit der Farbstellung Anthrazit) | Kopie `extern/Glasdesign/` mit `HERKUNFT.md` (Quelle, Fassung 0.5.0-beta.13 vom Zweig farbstellung/anthrazit, Commit 5c0cee0, ohne die Farbstellung der früheren Gilde, SHA-256 je Datei; nie hier ändern), eigene Fassung über `extern/Directory.Build.props`, Byte für Byte über `extern/** -text` in `.gitattributes`; Wörterbücher in `App.xaml` (Farbstellung Anthrazit, `Glas.Stile.xaml`, ohne Vorgabestile) und gleich im Bildlauf `OberflaecheBilder.cs` (`Bildlauf.Woerterbuecher`); `Knopf`, `Knopf.Haupt`, `Umschalter`, `Umschalter.Radio`, ToolTip auf Glas in `Design/Stile.xaml`, flach bleiben `Knopf.Flach` (Listenzeilen, Brettkarten, Titelleiste), CheckBox, Regler, Auswahlfelder, Funkfeld; einfacher Schatten (`gd:Glas.Schatten="Einfach"`) auf den Kanälen unter Empfangen > Erweitert (bis 0.9.3 Meine Kanäle), Brett und Verwaltung, Startseite voll (`OberflaecheGlasTests.Glas_Renderzeit_SchattenJeSeite`); Tests `OberflaecheGlasTests` (Kopie gleich Herkunft, gestrichene Schlüssel, kein Leuchtschalter und kein JG, Glas und flach, Schriftkontrast an Pixeln in Anthrazit, Renderzeit mit drei Stufen in drei Runden, kein angeschnittener Schatten in allen Bereichen); wie weit ein Schatten reicht, misst `OberflaecheStartseiteTests.Schattenrand` an einem gleichen Knopf (`SchattenFrei`, `SchattenPruefen`) |
| Startseite (0.6.0, Stufe 2; seit 0.10.0 nach der Mischung, Q87 bis Q92): Logo, Name, Gerät, Verbinden, Stecker, Online, darunter in zwei Spalten Sprechen (Mikrofon, Sprechtaste mit Namen und Ändern oder Warnung, Senden mit Stimme) und Hören (Ausgabe, Funk-Lautstärke, Headset oder Lautsprecher, die eine Hörprobe auch verbunden), Gerätewarnung oder "Audio starten" an der Stelle der Hörprobe, erster Start (Willkommenssatz, Schritt Sprechtaste, Später), Einsteigen mit Einheiten, Details im Admin-Setup | Modell `src/Funk.Client/Modelle/StartViewModel.cs` (Stecker aus dem Status, `GeraetFreigegeben` gemerkt in `VerbindungsEinstellungen.Freigegeben`; erster Start `ErststartOffen`, `SprechtasteSchrittOffen` aus `TastenEinstellungen.ErststartSprechtaste`, `ErwartenStarten`/`ErwartenBeenden` gerufen von `HauptViewModel.ErwartenAbgleichen` bei Bereichswechsel und `FensterAktiviert`/`FensterDeaktiviert`; Sprechtaste über `TastenViewModel.BelegenAsync(zeile, frist, ersetzen, vorsichtig)` und `WartenBeenden`, beim Fensterwechsel endet nur das Warten ohne Frist, `ErwartenBeenden(nurOhneFrist)`; Grenzen `ZweiSpaltenAb`, `SpaeterDanebenAb`, `SteckerNebeneinanderAb`, `OnlineDanebenAb`; Grund eines gescheiterten Audiostarts `AudioViewModel.HinweisKurz` im Rand `AudioAusRand`), Ansicht `Fenster/Bereiche.xaml` (Vorlage für `StartViewModel` mit `Spaltenwechsel` `Grundansicht`, `SchrittZeile`, `SteckerZeile` und `SteckerPaar`, Marken `TastenMarke`, `EinsteigenKarte` mit `DataContext` Netze; `VerbindungDetailsVorlage`), Stecker `Steuerelemente/Stecker.cs` (selbst gezeichnet, spiegelgleich um die Mitte), Logo `Dienste/Gildenpaket.cs` (Stufe 4 tauscht nur die Quelle) mit `Design/Gildenlogo-Platzhalter.png`; Verbindung, Anmeldung und Warteraum weiter `VerbindungViewModel.cs`; Tests `OberflaecheStartseiteTests`, `OberflaecheUmbauStartTests`, `OberflaecheVerbindungBilderTests` |
| Funk-Lautstärke (0.10.0, Q74, E7): ein Regler auf Start für alles, was JG TacRelay abspielt (Funk, Klick, Hörprobe), Windows und das Spiel bleiben | Mischer `src/Funk.Klang/Verarbeitung/Mischer.cs` (`FunkLautstaerke` Faktor 0 bis 1 auf die Summe vor dem Begrenzer, gerampt um `FunkLautstaerkeSchritt` je Block, die `Spur` rechnet ihn mit), Vertrag `IKlangMaschine.FunkLautstaerke`, `KlangMaschine` reicht weiter, Attrappe merkt; Ablage `AudioEinstellungen.FunkLautstaerkeProzent` (nachsichtig, ab Werk 100, nie stumm durch einen Fehler), Faktor `AudioEinstellungen.FunkLautstaerkeFaktor` (Prozent zum Quadrat); Modell `AudioViewModel.FunkLautstaerke`/`FunkLautstaerkeText`/`FunkLautstaerkeHilfe`; Tests `Verarbeitung/MischerTests.FunkLautstaerke_*`, `Maschine/KlangmoduleMaschineTests.FunkLautstaerke_*`, `OberflaecheUmbauStartTests.Umbau_FunkLautstaerke_*` |
| Seite Einstellungen (0.10.0, Q87, E6, E14, Bilder m-4, m-5): oben offen Ich (Namen, Rollenwünsche, ein "Speichern", Heute als Funker), darunter Tasten, Empfangen, Senden, Funkfeld und Darstellung, Fehlersuche je mit Kasten "Läuft von selbst" und dem Klappbereich "Erweitert:"; unter Senden seit 0.11.1 (Sarevoc 12.10.2026) offen die Klangmodule (links Echounterdrückung, Echoschutz, Rauschunterdrückung, rechts automatische Lautstärke, Sprachaktivierung, Mindest-Funklautstärke) und darunter über beide die Funkschwelle (Pegelbalken mit Griffen, Einmessen, Genau einstellen), unter Erweitert feste Verstärkung, Mithören, Klangfarbe; keine Sprecheranzeige (eigener Schritt); Details der Verbindung nur im Admin-Setup darunter (E21) | Modell `src/Funk.Client/Modelle/EinstellungenViewModel.cs` (`AbschnitteBauen` einmal aus `HauptViewModel`, Kästen `Automatik*`, `SatzFehlersuche`, Hovertexte, Grenzen `KastenDanebenAb`, `SendenZweiSpaltenAb`, `TastenZeileNebeneinanderAb`, `PersonZeileNebeneinanderAb`, `FussNebeneinanderAb`), Abschnitte und Teile `Modelle/Einstellabschnitt.cs`, Speichern `MeinProfilViewModel.SpeichernCommand`/`SpeichernHinweis`, Rollen als Umschalter mit Stern (seit dem Befund der Prüfung wie m-4: `RollenWunsch.SternZeigen`, `Hilfe`, `WunschHilfe`, `VomAdminGrund`, `ErsteWahlHilfe`; Vorlage `RollenWahl`, Katalog ergänzen `KatalogTeil` unter Ich > Erweitert), Tastengruppen `TastenViewModel.Sprechen`/`KanalTasten`/`WeitereTasten`/`Leitung` mit `LeitungSichtbar` (E18); Ansicht Vorlage für `EinstellungenViewModel` in `Fenster/Bereiche.xaml` (`IchKarte`, `AbschnitteKarte`, Vorlage `AbschnittKopf`, `TastenZeileVorlage`, `LautstaerkeVorlage`, `NachlaufVorlage`, `FeinheitenFuss`, `DetailsBereich`; Senden: offen `SendenSpalten` mit `SendenSpalteKlang`, `SendenSpalteSchwelle` und `SendenFunkschwelle`, Klappbereich `SendenFeinheiten` mit `SendenErweitertSpalten`, `FesteVerstaerkungTeil`, `KlangfarbeTeil`; Texte `EinstellungenViewModel.InhaltSendenErweitert`, `GriffeSatz`, `GenauEinstellenSatz`, `AutomatikAusSatz`); bis 0.9.3 die Seiten Audio und Meine Kanäle; Tests `OberflaecheUmbauEinstellungenTests`, `OberflaecheUmbauErweitertTests`, `OberflaecheUmbauBilderTests` |
| Echounterdrückung dreistufig (0.10.0, E11) und "Beim Start verbinden" nach der Freigabe (E12) | Ablage `AudioEinstellungen.EchounterdrueckungVonHand` (`bool?`, null = Automatisch), `EchoNachHoerweise`, `EchoWirksam` (das alte Feld `echounterdrueckung` trägt den wirksamen Wert für ältere Clients, eine abweichende alte Datei wird beim Normalisieren einmal "von Hand"); `VerbindungsEinstellungen.BeimStartVerbindenVoreingestellt`, `NachFreigabeVoreinstellen` (gerufen aus `StartViewModel` beim Aufbau und bei jeder neuen Freigabe); Modell `AudioViewModel.EchoVonHand`, `EchoAutomatisch`/`EchoAn`/`EchoAus`, `EchoHinweis`; `EinstellungenViewModel.VerbindenBeimStart`; Tests `OberflaecheUmbauEinstellungenTests.Umbau_Echounterdrueckung_*`, `Umbau_BeimStartVerbinden_*` |
| Geräteliste folgt dem Geräteereignis (0.10.0, Endfassung #22): kein "Geräte neu lesen" mehr, auch ohne laufendes Audio | `KlangMaschine.GeraeteGeaendert` in `src/Funk.Klang/Maschine/KlangMaschine.Geraete.cs` (nach dem Abgleich der Ströme, auf dem Steuer-Thread), Ereignis `IKlangMaschine.GeraeteListeGeaendert`, Attrappe `KlangMaschinenAttrappe.GeraeteSetzen`; Modell `AudioViewModel.GeraeteLesen` über den Takt (gemerkte Kennung bleibt, ein fehlendes Gerät zeigt den Standard ohne die Wahl zu überschreiben); Tests `OberflaecheBereicheTests.Audio_GeraeteStartUndWechsel`, `OberflaecheUmbauEinstellungenTests` (echte Maschine im Prüfstand) |
| "Mikro stumm" im Menü des Symbols im Infobereich (0.10.0) | `FunkfeldAblage.Menue`/`Gewaehlt` (Haken `MikroStumm`, `MikroUmschalten`) in `src/Funk.Client/Modelle/FunkfeldAblage.cs`, verdrahtet in `App.xaml.cs` (`funkfeld.Mikro.TasteGedrueckt`, mit Satz in der Meldezeile); Test `S2AblageTests.Menue_HeuteAlsFunkerNurFuerAdmins_UndDieEintraegeWirken` |
| Bilder der Oberfläche: im Speicher (Bildlauf, `FUNK_BILDER`) und Dokubilder mit sichtbarem Fenster | Bilder der Umbauten 0.10.0 `tests/Funk.Tests/Oberflaeche/OberflaecheUmbauBilderTests.cs` (0100-1 bis 0100-3, 0100-6, 0100-7, Vorlage `C:\Claude\tmp\tr-entwurf-m\m-1` bis `m-8`; die Einstellungen seit 0.11.1 als 0111-1 bis 0111-3), Senden offen und mit offenem Erweitert bei 640, 900, 1100 `OberflaecheUmbauErweitertTests` (0111-senden-...), das Brett mit Vorschau `OberflaecheZuordnungVorschauTests` (0111-4), das Raster der Meldezeile `OberflaecheMeldezeilePlatzTests` (0111-5), die Meldezeile in drei Skalierungen `OberflaecheMeldezeileEinzeiligTests` (0111-6) und jede Seite des Verwaltungsteils bei 760 `OberflaecheVerwaltungLayoutTests` (0111-7, Mitglieder auch bei 960 und 1100), Sichtproben der übrigen Bildlauf-Klassen; Dokubilder unter `docs/bilder` nur mit `tests/oberflaeche-bilder.ps1 -SichtbarFreigegeben` (Programmstarts mit Fenstern, nur mit Freigabe im Gespräch; seit 0.10.0 Start, Einstellungen mit offenen Klappbereichen über `anzeige.offeneFeinheiten` und `--bereich sprachaktivierung`) |
| Fehlermeldungen für Funker ohne Adresse, Port, Pfad, mit Fehlernummer (Q5) | Nummern `src/Funk.Client/Dienste/Fehlernummern.cs` (01-09 Profil, 11-19 Aufbau, 21-29 verloren, 31-39 Server lehnt ab, 41-49 intern), Sätze `Verbindungstexte.cs` (`Verbindungsmeldung` mit Text und Einzelheit), Einzelheiten nur für Details `FunkLage.HinweisEinzelheit`, `Profilbefund.Einzelheit`/`Nummer`, Anzeige `VerbindungViewModel` (`WasFehlt` mit Nummer, `HinweisEinzelheit`, `ProfilEinzelheit`); Tests `FehlertexteTests` |
| Fassung im Titel | `Hauptfenster.xaml.cs` (`Title`, `FassungText` aus `FunkClientOptionen.Fassung`); Test `OberflaecheHauptfensterTests.Hauptfenster_FassungImTitelAusDerAssembly` |
| Nur eine Instanz | `src/Funk.Client/Einzelinstanz.cs` |
| Setup (Inno Setup) | `installer/TacRelay.iss`, `tools/setup-bauen.ps1`, Symbol `tools/symbol-zeichnen.ps1` (rechnet `grafik/symbol/*.svg` mit ImageMagick und librsvg in `src/Funk.Client/TacRelay.ico`, 16 bis 256 Bildpunkte, und die Bilder des Assistenten) |
| Messung am echten Funkfeld (nur mit Freigabe) | `src/Funk.Client/Messung/`, `tools/funkfeld-messen.ps1` |
| Native Bibliotheken bauen | `native/skripte/*.ps1`, eigene C-Teile in `native/eigen/` |
| Admin-Nachweis, TOTP, Notfallcodes, Journal (Stufe 1) | `src/Funk.Server/Identitaet/Adminbuch.cs`, `Totp.cs`, `TotpTresor.cs`, `src/Funk.Server/Kern/Zentrale.Admin.cs`, `src/Funk.Server/Betrieb/Journal.cs`, Unterschrift `src/Funk.Protokoll/Identitaet/Adminnachweis.cs` |
| Mitglieder, Rollenkatalog, Vormerkungen, Mitgliedskarte | `src/Funk.Server/Identitaet/Mitgliederbuch.cs`, `src/Funk.Server/Kern/Zentrale.Mitglieder.cs` |
| Abzeichen (0.11.7): Katalog, Vergabe 0x0025, vorn und ausblenden | Katalog `src/Funk.Protokoll/Abzeichen/abzeichen.json` (eingebettet) und `Abzeichenkatalog.cs`; Bodies `Steuerung/Nachrichten/Verwaltung.cs` (`AbzeichenAnfrage`, `ProfilSetzenAnfrage.AbzeichenVorn/Aus`), `Lage.cs` (`KontoStand`, `PraesenzEintrag`); Prüfregeln und Ablage `src/Funk.Server/Identitaet/Mitgliederbuch.cs` (`AbzeichenSetzen`, `AbzeichenAnsichtSetzen`, `SichtbareAbzeichen`); Server `Zentrale.Mitglieder.cs` (`AbzeichenSetzen`, `ProfilSetzen`, Mitgliedskarte), `Zentrale.Admin.cs` (`KontoStandVon`, `PraesenzZusatz`, `SichtbareZeichen`); Dienst `src/Funk.Verwaltung/Verwaltungsdienst.cs` (`AbzeichenSetzenAsync`); Bilder `src/Funk.Client/Abzeichen/` (gerechnet von `tools/abzeichen-rendern.ps1`), Quellen `grafik/abzeichen/` (Vorlage `LIESMICH.md`; Orga-Schild seit 19.10.2026 Anthrazit mit Rand in Petrol); Tests `tests/Funk.Tests/AbzeichenTests.cs`, `AdminSitzungTests.Zusammenspiel_Abzeichen` |
| Meldesystem (0.12.0, Sarevoc 11.10. und 16.10.2026, SPRECHERANZEIGE-ENTWURF.md im Planungsordner M1-M25): Befehle, Zustände, Ampel, Countdown, Bereit-Abfrage, Textmeldung an alle; Recht Haken und Führung, Admin oder laufender Auftrag, nicht als Funker; je Sender eine in 2 s; Nachlieferung nach Welcome; Bereit-Abfrage endet nach 2 min | Katalog `src/Funk.Protokoll/Meldungen/meldungen.json` (eingebettet) und `Meldungskatalog.cs` (Arten, Ausführungen mit Ersatz Schlicht, Stopp-Bild, `Ressource`); Bodies und Regel `Steuerung/Nachrichten/Meldesystem.cs` (`MeldungSendenAnfrage`, `BereitMeldenAnfrage`, `Meldestand`, `BereitStand`, `Meldungsvorgang`, `Melderecht`); Grenzen `Grenzen.cs` (`MeldungZeileMax` usw.) und `Fristen.MeldungAbstandMs`/`BereitAbfrageMs`, Ratengruppen `Meldung` und `Bereit` in `Steuerbremse.cs`, `Versionen.MeldesystemAb`; Haken `Mitgliedsdaten.MeldungenSenden` (`Mitgliederbuch.KontoAendern`), `MitgliedAnfrage`/`MitgliedEintrag`/`KontoStand.MeldungenSenden`; Server: Stand im Speicher `src/Funk.Server/Kern/Meldesystem.cs` (Uhr als Parameter), Anfragen, Pushes, Takt und Nachlieferung `Zentrale.Meldungen.cs` (`DarfMelden`, `MeldeJetzt` aus `Uhr`), Haken mit Journal `Zentrale.Mitglieder.cs` (MELDUNGEN); Client-Kern: Uhr `src/Funk.ClientKern/Uhrabgleich.cs` (Ping-Proben in `Sitzung.PingAsync`/`RahmenVerarbeiten`, grob aus Welcome), Stand `Lagebild.Meldestand`/`BereitStand`; Client: `IFunkDienst.MeldungSendenAsync`/`BereitMeldenAsync`/`ServerJetztMs`, Ereignis `MeldungEingegangen`, `FunkLage.Meldestand`/`BereitStand`/`ServerMeldungen`, `KontoInfo.DarfMelden`, Attrappe `FunkDienstAttrappe.Meldungen.cs`; Verwaltung `IVerwaltungsdienst.MeldungenSendenSetzenAsync`, `Verwaltungslage.MeldungenBekannt`; Bilder `src/Funk.Client/Meldungen/{schlicht,metall,schrift}/` mit ImageMagick verkleinert (Originale nicht im Repository); Tests `tests/Funk.Tests/MeldesystemTests.cs`, `MeldesystemServerTests.cs`, `AdminSitzungTests.Zusammenspiel_Meldungshaken`. Bei der Bereit-Abfrage zählt der Fragende nicht mit (`Meldesystem.BereitStandBauen`). Oberfläche (Schritt 2): Bilder laden und auf die sichtbaren Bildpunkte zuschneiden `src/Funk.Client/Design/Meldungsbilder.cs` (`Kachel`, `Ganz`, `Sichtbar`); Anzeige über dem Spiel `src/Funk.Client/Modelle/Meldeanzeige.cs` (`Eingegangen`, `Takt`, `ProbeZeigen`, `Grenze`, `BefehlMs`, `ZustandAnteil`, `CountdownAnteil`, `Restzeit`) mit `Meldebild` (Plätze, gemessene Felder `FeldStartUm`/`FeldCountdown`/`FeldRestzeit`), `Meldeplatz`, `Meldefeld`, `Schattenwurf`; Vorlage `Meldeanzeige.Bild` in `Design/Stile.xaml`; Fenster `src/Funk.Client/Fenster/MeldeanzeigeFenster.xaml(.cs)` (wie die Sprecheranzeige, `Ecke` für den ganzen Block mittig bei 50 %, `Grenze` aus dem Monitor, Monitor `Spielfenster.MonitorImVordergrund`); Ton `src/Funk.Klang/Verarbeitung/Signalton.cs` im `Mischer` (`Ton`), `IKlangMaschine.TonSpielen` (Vorgabe falsch), Attrappe merkt `Toene`; Feld der Leitung `src/Funk.Client/Modelle/MeldungenViewModel.cs` (`Gilt`, Kacheln `Meldekachel`, `Groesse`, `Zeile` hart 60, `Zielzeit`, Sperre 2 s ab der Antwort, `Takt`) mit Ansicht `src/Funk.Client/Fenster/MeldungenFeld.xaml`, eingebaut unter dem Brett in `Fenster/BrettAnsicht.xaml` (`BrettViewModel.Meldungen`; die Seite gibt es auch ohne Einteilungsrecht, `HauptViewModel.RaidZeigen`); Bereit `src/Funk.Client/Modelle/Bereitmeldung.cs` (Knopf `BereitKnopf` in `Fenster/Hauptfenster.xaml`, Taste `Tastenfunktion.Bereit` in `Tastenbelegung.cs`, `TastenViewModel.WeitereTasten`, `Tastenbetrieb`); Einstellungen `src/Funk.Client/Modelle/MeldungenEinstellungViewModel.cs` im Abschnitt `EinstellungenViewModel.AbschnittMeldungen` (Kennung `meldungen`, `Symbol.Meldungen`), Ansicht `AbschnittMeldungen` in `Fenster/Bereiche.xaml`, Ablage `ClientEinstellungen.Meldungen` (`MeldungenEinstellungen`, nur Zahlen und Schalter); Haken auf der Karte `MitgliedKarte.MeldungenSenden` (`src/Funk.Verwaltung/Modelle/MitgliederViewModel.cs`, gesetzt mit Speichern) und `VerwaltungAnsicht.xaml` (`MeldungenHaken`); verdrahtet in `App.xaml.cs`; Tests `tests/Funk.Tests/Oberflaeche/OberflaecheMeldungenTests.cs` (Uhr der Tests, Serverzeit, Ton, Taste und Knopf, Feld, Seite, Einstellungen, Haken, Bildläufe, Bilder `vorschau-0120-meldungen-*`). Textmeldung (M24): Arten `Meldungsvorgang.Text`/`TextAufheben`, Felder `MeldungSendenAnfrage.Text`/`Farbe`/`Dauer`, `Meldestand.Text` (`Textmeldung`), Kennungen und `Bleibend` in `Textmeldungen` (alles `Steuerung/Nachrichten/Meldesystem.cs`), Grenzen `MeldungTextMax`/`TextKennungMax`; Server prüft und hält nur die stehende (`Meldesystem.Text`, `StandBauen(..., text)`); `Lagebild.MeldestandSetzen` behält nur die stehende; Farbtöne `src/Funk.Client/Design/Textfarben.cs`; Anzeige `Textplatz` (Grundschrift `Meldebild.TextGrund`, Umbruch `Textplatz.Formen`, `ZeileEm`) in `Meldeanzeige.cs`, `TextMs`, Vorlage `MeldeText` in `Meldeanzeige.Bild`; Feld der Leitung `MeldungenViewModel` (`TextEingabe` hart 120, `Farbwahl`/`Textfarbwahl`, `TextDauer`, `TextSendenCommand`, `TextAufhebenCommand`), Gruppe `MeldungenText` in `MeldungenFeld.xaml`. Xeno-Korrektur (M25): drei Finger statt vier |
| Anmeldung mit SC- und Discord-Namen, Namensregeln | `src/Funk.Protokoll/Steuerung/Nachrichten/Anmeldung.cs`, `src/Funk.Protokoll/Identitaet/Namensregeln.cs` |
| Verwaltungsteil (nur Admin-Setup), QR-Code, Admin-Anmeldung | `src/Funk.Verwaltung/` (`Adminzugang.cs`, `QrCode.cs` nach Project Nayuki, MIT), Lader mit Prüfsumme `src/Funk.Client/Dienste/Verwaltungslader.cs` |
| Vorprüfung vor dem Einrichten (seit 0.4.1): TPM 2.0 Pflicht, Windows-Konto mit PIN oder Kennwort Richtlinie; seit 0.5.1 keine Empfehlung zur Laufwerksverschlüsselung mehr (Sarevoc, 02.10.2026) | Deutung, Texte, Frist und STA-Faden `src/Funk.Verwaltung/Voraussetzungen.cs` (`Tpmdeutung`, `Voraussetzungstexte`, `Adminvoraussetzungen`); echte Prüfung nur lesend `VoraussetzungenWindows.cs` (`PcpTpmPruefung`: PCP öffnen, PCP_PLATFORM_TYPE); Naht `IVerwaltungsdienst.VoraussetzungenAsync` (liefert `TpmStand`), im Betrieb `Einstieg` mit `Adminvoraussetzungen.AmPc()`; Karte `VerwaltungViewModel` (Abschnitt Vorprüfung, `Tpm`, `KannEinrichten`), `VerwaltungAnsicht.xaml` (`TpmZeile`, `RichtlinieZeile`); Fehler beim Anlegen `Adminschluesselwahl.OhneTpm` mit Code von Windows; Tests `AdminvoraussetzungenTests.cs` (mit dem Wächter `Repository_NenntBitLockerNirgends`), `Oberflaeche/OberflaecheVerwaltungVoraussetzungenTests.cs` (Abstände der Karte) |
| Wer ist da, Ich, Zeichen neben Namen | `src/Funk.Client/Modelle/WerIstDaViewModel.cs`, `MeinProfilViewModel.cs`, `src/Funk.Client/Design/Zeichensatz.cs` |
| Wer ist da seit 0.11.5 (Sarevoc 13. und 14.10.2026): flache Liste, je Zeile Name (nie gekürzt, breiter kleiner; Raid-Seite mit Einsatzleitung), Mikrofon (Stummschalten, ZUWEISEN, 3600 s, blutrot `Pinsel.Glut`, fremde nur mit Recht, Ablauf im Takt), Status (grün/gelb/grau wie die Zahl auf der Raid-Seite, als eingerichteter Admin im Admin-Setup Sprung in die Verwaltung zu einem Mitglied), Wippe (2 dB je Klick, -30 bis +12, nicht in der eigenen Zeile) | Modell `WerIstDaViewModel` (`Zeilen`, `Anbinden`, `MikroAsync`, `PegelUmAsync`, `RaidOeffnen`, `MitgliedZeigen`, `StummSekunden`, `PegelSchritt`, `Takt`, Stufe aus dem Kontenverzeichnis `VerzeichnisHolenAsync`/`VerzeichnisVonSelbst`), `WerIstDaZeile` (`Kanalstand`, `MikroKlickbar`, `StatusKlickbar`, `HatWippe`, `StummEnde`, `DarfAufheben`, `StufeAusZeichen`, je Knopf Hilfe, Gesperrt, Ansage), `WerIstDaEintrag` (`Stufe`); Kanäle wie die Raid-Seite `BrettViewModel.Baumnetze`/`Kanalzahl`; Sprünge `HauptViewModel.RaidOeffnen`/`MitgliedZeigen`, `IVerwaltungsteil.MitgliedZeigen` (Standard: nichts) -> `Verwaltungsteil` -> `VerwaltungViewModel.MitgliedZeigen` (nach der Anmeldung Mitglieder statt Heute) -> `MitgliederViewModel.KontoZeigen` (`SprungWartet` genau bis zur nächsten Liste, `SprungVergessen`); Pegel je Person an einer Stelle `NetzeViewModel` (`PegelVon`, `PegelSetzenAsync(konto, db)`, `PegelGeaendert`, `_pegelGesehen`); Ansicht `Bereiche.xaml` (`WerIstDaLauf`, `WerIstDaListe`, `NameKnopf`, `StummKnopf`, `StatusKnopf`, `Wippe`, `LeiserKnopf`, `LauterKnopf`), Stile `WerIstDa.*` und Vorlage `WerIstDa.NameInhalt` (Viewbox, nur verkleinern) in `Stile.xaml`, `Pinsel.OhneKanal`; "Abend beenden" hebt eigene Stummschaltungen auf, als Admin alle außer der eigenen (`BrettViewModel.StummZumAufheben`); Tests `OberflaecheWerIstDaTests`, Bild `OberflaecheVorschau0112Tests.Vorschau0115_WerIstDa` |
| Spalte "Wer ist da" in Klein (Q93 a, Sarevoc 12.10.2026): unter 1240 Breite wird das Fenster beim Öffnen um 340 breiter, nach rechts oder nach links ausweichend auf seinem Monitor (dem, auf dem es überwiegend steht), sonst liegt die Spalte als Schublade mit Schatten über Start; Schließen bringt den Platz zurück, wenn er unverändert ist; maximiert gilt beides für den Normalzustand; gemerkt ohne die Spalte | Rechnung `src/Funk.Client/Fenster/Fenstergroessen.cs` (`WerIstDaBreite`, `WerIstDaDanebenAb`, `RundungsSpiel`, `Spaltenplatz`, `SpalteOeffnen(jetzt, bildschirme, skala)`, `Unveraendert`, `OhneSpalte`) und `Platzierung.Ueberwiegend`, Anwendung `Hauptfenster.xaml.cs` (`WerIstDaGeaendert`, `SpalteOeffnen`, `Verbreitern` mit Anker, `SpalteSchliessen`, `SpalteAnordnen` aus `SizeChanged`, `GroesseAnwenden` bringt Klein gleich verbreitert, `Gemerkt` über `OhneSpalte`; Tastatur unter der Schublade: `UnterSchublade` geerbt auf `SeitenPlatz`, `HoerenSpalte` in `Bereiche.xaml` per Stil aus dem Tab-Kreis, `FokusAusVerdecktem` ins Suchfeld `WerIstDaSuche`), Ansicht `Hauptfenster.xaml` (`WerIstDaSpalte` mit Breite aus `x:Static`, `WerIstDaSchatten`), ein Schritt `Platzfuehrung.Setzen(platz, einSchritt: true)` (ein SetWindowPos, `Umsetzen`), Zähler `Platzfuehrung.Gesetzte` und `InEinemSchritt`; Tests `Oberflaeche/OberflaecheWerIstDaPlatzTests.cs` (mit Quelltext-Wächter gegen Activate, Focus und mehrere SetWindowPos), `OberflaecheUmbauStartTests.Umbau_Start_ZweiSpaltenUndGestapelt` (Schublade, maximiert wie bisher) |
| Discord-Name eindeutig, nur vom Admin (A53) | Zeichen und Schlüssel: `src/Funk.Protokoll/Identitaet/Hilfen.cs` (`Zeichenregeln.DiscordNameBereinigen`, `IstUnsichtbar`, `Namensschluessel`); `src/Funk.Protokoll/Identitaet/Kontenbuch.Verwaltung.cs` (`DiscordNameSetzen`, `DiscordNameBesitzer`, `ProfilSetzen`), `Kontenbuch.Geraete.cs` (Freigabe), `Modell.cs` (`NachDiscordName`), `src/Funk.Server/Identitaet/Mitgliederbuch.cs` (Vormerkungen, Liste), `Zentrale.Anfragen.cs` (`DISCORDNAME`, Freigabetext), `Zentrale.Verwaltung.cs` (Konsole `discordname`) |
| Übergang von 0.1.0 (Zustand, Konfiguration, Clients) | `tests/Funk.Tests/Uebergang010Tests.cs` mit `tests/Funk.Tests/Daten/zustand-0.1.0/`, `ServerKonfiguration.TotpSchluesselFehltInDatei` |
| Auftrag für einen Abend (Stufe 2, 6.12): Regeln, Fristen, Ende | `src/Funk.Server/Lage/Zustandsschreiber.Beauftragung.cs` (`BeauftragungBilden`, `FristenAus`, `AuftraegeNachKontoaenderung`, `SelbstwahlSperreWirkt`, `PaketAus`), Modell `Lage/Modell.cs` (`Konto.RechteZu`, `Beauftragung.GiltZu`), Grenzen `Lage/Fristen.cs`; Draht, Journal, Meldung, Belegung, Konsole `src/Funk.Server/Kern/Zentrale.Raidleitung.cs`; Nachrichten `src/Funk.Protokoll/Steuerung/Nachrichten/Raidleitung.cs`; Zeiten 04:00 `Steuerung/Auftragszeit.cs` |
| Rangregel, Rechte je Stufe (Führung ohne ZUWEISEN) | `src/Funk.Server/Lage/Rechte.cs` und `src/Funk.Protokoll/Identitaet/Modell.cs` (`Rechteregeln`), die Tabelle in beiden gleich halten; `DarfZuweisen(stufe, ziel, jetzt)` (mit Funker-Modus) und `DarfStummschalten` stehen nur im Server |
| Einsatzphasen (6.14): Modell, Schalten, Anlegen, Rückfall nach 12 h, jeder Weg zum Platz | `src/Funk.Server/Lage/Zustandsschreiber.Phasen.cs` (`PhaseSchaltenAus`, `Umstellen`, `PhaseSetzenAus`, `PhaseLoeschenAus`, `PhaseRueckfall`), Modell `Lage/Modell.cs` (`Einsatzphase`, `PhasenVariante`, `LaufendePhase`, `Phasenwechsel`), laufende Phase im Nebenstand (`Lage/Nebenstand.cs`, `Phase`, `LaufendeNr()`), Felder `Lage/Feldregeln.cs` (`AusRolle` mit Phase, `PhaseUmstellen`, `PhaseAuswahl`, `PhasePruefen`), `Arbeitsstand.AktivePhase()` und `AuswahlTatsaechlich`, "Wie letztes Mal" `Zustandsschreiber.Selbst.cs` (`SatzInPhase`); Draht `src/Funk.Protokoll/Steuerung/Nachrichten/Phasen.cs`, Anfragen `Zentrale.Anfragen.cs`, Folgen `Zentrale.cs` (`FolgenVerteilen`: PhaseGeschaltet, `Mitgliedschaften.phase`), nach Welcome `Zentrale.Anmeldung.cs`, Abbildung `Steuer/Abbildung.cs` (`NetzPhase`, `EinsatzphaseLage`, `PhaseGeschaltet`) |
| "Heute als Funker" (6.15) | `Lage/Zustandsschreiber.Phasen.cs` (`FunkermodusAus`, `FunkermodusBeenden`, `ZuweisenVerloren`), `Lage/Modell.cs` (`Konto.AlsFunkerBis`, `AlsFunkerZu`, `RechteZu`), Heißpfad `Lage/Schnappschuss.cs` (`RechteAn`), Ende bei Stufe und Sperre `Zustandsschreiber.Beauftragung.cs` (`AuftraegeNachKontoaenderung`, `FristenAus`), Folgen `Zentrale.Raidleitung.cs` (`FunkermodusFolgenVerteilen`, `FristFaellig`), Kontoangaben `Zentrale.Admin.cs` (`AlsFunkerBisVon`), Client `src/Funk.ClientKern/Lagebild.cs` (`HatRecht`) |
| S2 im Client: Phasen anlegen (Verwaltungsteil, *Flotte > Phasen*) | Entwurf `src/Funk.Verwaltung/Flotte/Flottenentwurf.cs` (`PhaseAnlegen`, `VarianteAnlegen`, `VarianteSetzen`, `Betroffene`, `EigenePhasen`, `PhasenErlaubt`, Folge im Paket: Phasen löschen zuerst, setzen zuletzt), Modell `src/Funk.Verwaltung/Modelle/FlotteViewModel.cs` (`PhaseBearbeitung`, `VarianteBearbeitung`, gemeinsamer Felder-Editor `Felderform`/`IFelderEditor`), Ansicht `VerwaltungAnsicht.xaml` (`PhasenReiter`, Vorlage `FeldVorlage`), Lage `IVerwaltungsdienst.cs` (`Flottenlage.PhasenBekannt`, `LaufendePhase`) |
| S2 im Client: Phasen schalten (Brett, Phasentasten, Meldezeile) | Leiste und Rückfrage `BrettViewModel.cs` (`Phasen`, `PhaseWaehlen`, `PhaseJa`), Ansicht `BrettAnsicht.xaml` (Fokus auf "Ja, schalten" im Code-Behind); Tasten zweistufig `src/Funk.Client/Modelle/Phasensteuerung.cs`, Zahl und Text `Phasenrechnung.cs`; Zeiten an einer Stelle `Meldezeile.cs` (`Meldezeiten`) |
| S2 im Client: Meldezeile (Fenster über dem Spiel; bis 0.11.0 am Funkfeld) | Modell `src/Funk.Client/Modelle/Meldezeile.cs`, was jeder Funker liest `Teilnehmermeldungen.cs`, Fenster `src/Funk.Client/Fenster/MeldezeileFenster.xaml(.cs)`, Stil und Lage als Rechnung `Meldezeilenlage.cs` |
| Meldezeile an festem Platz, ohne Dauerfeuer (0.11.1 AP3, Sarevoc 12.10.2026: "an einem festen Platz am Bildschirm, nicht da, wo das Funkfeld ist, und nicht immerzu spammen"): neun Stellen auf einem Monitor (ab Werk oben Mitte auf dem Monitor von Star Citizen, sonst Hauptmonitor), fest 640 breit mal Skalierung, Raster und Monitorwahl mit Probe unter Funkfeld und Darstellung > Erweitert (seit der Prüfung: jeder Klick ins Raster zeigt die Probe, auch auf das gewählte Feld, `Stellenfeld.WaehlenCommand`, `IsChecked` nur OneWay); derselbe Hinweis der Automatik höchstens einmal je 5 min, gezählt ab dem Erscheinen; Tasten, Meldungen vom Server (`Teilnehmermeldungen`, alle mit `immer`) und Fehler jedes Mal (seit der Prüfung; in AP3 alles außer Tasten entprellt), eigene Phase meldet nichts | Rechnung `Meldezeilenlage.Fest`/`Monitor`/`Rand`/`Hoechstbreite(skalierung)` (`Berechnen` und `Abstand` sind fort), Fenster `MeldezeileFenster(zeile, bildschirme, skalierung, wahl, spiel)` mit `ZielFuer` (ohne Handle) und `LageSetzen`; Spiel `src/Funk.Client/Fenster/Spielfenster.cs` (`Prozess` genau StarCitizen, `Monitor`, `Groesstes` als Rechnung, EnumWindows ohne Prozess-Handle) mit `WindowsBildschirme.MonitorVon`; Ablage `FunkfeldEinstellungen.MeldezeileStelle` (`Meldezeilenstelle`, `NachsichtigeMeldezeilenstelle`, ab Werk `MeldezeileStelleAbWerk`) und `MeldezeileMonitor` (`null` Automatisch, 0 Haupt, n), `Meldezeilenwahl`; Modell `src/Funk.Client/Modelle/MeldezeilePlatzViewModel.cs` (`Felder`/`Stellenfeld`, `Monitore`/`Monitorwahl`, `Stelle`, `Monitor`, `GewaehlterMonitor`, `Wahl`, `MonitoreLesen`, `ProbeVerlangt`, `ProbeCommand`, Texte `Erklaerung`, `RasterHilfe`, `MonitorHilfe`, `ProbeHilfe`), gehalten von `EinstellungenViewModel.Meldezeile` (Konstruktor, zwei `WertTeil` unter Funkfeld, `InhaltFunkfeldErweitert`); Ansicht Gruppe "Meldezeile" in `Fenster/Bereiche.xaml` (`MeldezeileSpalten`, `MeldezeileRaster`, `MeldezeileMonitorWahl` Alt+O, `MeldezeileProbeKnopf` Alt+P), Stile `Meldezeile.Monitor` und `Meldezeile.Feld` in `Design/Stile.xaml`; Entprellung `Meldezeile.Zeigen(..., immer)` mit `Zeilenmeldung.Entprellt`, `_erschienen` (gesetzt in `Neu`, beim Erscheinen), `Wiederholbar`, `Meldezeiten.WiederholungMs`, Probe `Meldezeile.ProbeSchluessel`/`ProbeText`, `Meldezeiten.ProbeMs`; `immer` an den Tasten (`Phasensteuerung.Melden`, `Teilnehmermeldungen.SendenetzGewechselt`/`MeldenGehtNicht`, `MikroViewModel.TasteGedrueckt`/`BeiStumm`, `DauersendViewModel.StummTaste`), eigene Phase `Teilnehmermeldungen.Pruefen`/`NameNachtragen`; verdrahtet in `App.xaml.cs` (Probe, `Wahl` zieht eine stehende Meldung um); Tests `Oberflaeche/OberflaecheMeldezeilePlatzTests.cs` (Rechnung, Modell, Entprellung, eigene Aktionen, Bildlauf mit Bild `0111-5-meldezeile-raster`), die Liste aller Meldungen mit Auslöser in `docs/MODULBERICHT-client.md` (AP3) |
| Verwaltungsteil in schmalen Fenstern (0.11.1 AP4, Sarevocs Bild vom 12.10.2026: "Mitglied vormerken" über "Mitglieder", Suchtext abgeschnitten): Liste und Karte untereinander, wenn der Platz fehlt; Knopf neben der Überschrift mit Umbruch; Beschriftungen und Listenzeilen umbrechend, kein TextTrimming; Felder einer Phasenvariante so viele je Reihe, wie Platz ist | Panel `src/Funk.Verwaltung/Ansichten/Zweispalter.cs` (`Ab`, `Anteil`, `ErsteMin`, `ErsteMax`, `Luecke`, `Nebeneinander`, `Spalten` als Rechnung) an den sechs Seiten mit zwei Spalten und am QR-Code in `Ansichten/VerwaltungAnsicht.xaml` (`MitgliederBereich`, `NetzeReiter`, `RollenReiter` mit 740, `EinheitenReiter`, `PhasenReiter`, `EinsatzleitungBereich`); Stile `Etikett` (Umbruch), `Kopf.Abschnitt`, `Kopf.Knopf`, `Kopf.Knopf.Haupt`, `Herkunftsschild` (Innenrand 8); Knopfreihen als `WrapPanel`; Werkzeuge mit `SharedSizeGroup` `WerkzeugKnopf`; Wächter `tests/Funk.Tests/Oberflaeche/OberflaecheVerwaltungLayoutTests.cs` (15 Seiten bei 760, 960, 1100 und 175 %) mit `Ansichtspruefung.Abschnittbefunde`, `Ueberlappungen`, `GekuerztOhneHilfe`, `HatHilfe`, `ImBildlaufZuSehen`; Bilder `0111-7-verwaltung-<seite>-760` |
| Meldezeile einzeilig (0.11.0, Sarevoc 12.10.2026: "maximal einzeilig, das Funkfeld muss klein bleiben"): jede Meldung eine Zeile, Kürzung mit "...", kurze Sätze; seit 0.11.1 fest 640 breit (bis dahin höchstens so breit wie das Funkfeld) | Breite `Meldezeilenlage.Hoechstbreite(skalierung)`/`BreiteFest`, gesetzt in `MeldezeileFenster.GroesseSetzen` (bis Q111 `BreiteSetzen`; aus `Abgleichen` vor dem Zeigen, in Tests direkt, ohne Handle); `MeldezeileFenster.xaml` (`Satz`: NoWrap, CharacterEllipsis, Schrift 12, `Rahmen` Innenrand 8,3); Umbruch im Text `Meldezeile.Einzeilig` (in `Setzen`); Sätze `Teilnehmermeldungen` (`Ansagen`, `Aenderung` liefert `null` für "nur umgestellt"), `Phasensteuerung.Grund`, `MikroViewModel.StummMeldung`/`AnMeldung`, `DauersendViewModel.Meldetext`/`KeinerGedaempft`/`StummTaste`, `FunkfeldAblage.ErsterHinweis`, `AudioViewModel.RueckfallMeldung` (App.xaml.cs), `Meldenetzwahl.Hindernis`; Tests `OberflaecheMeldezeileEinzeiligTests` (640 mal Skalierung bei 100 und 175 %, feste Sätze ganz, überlanger gekürzt, Bild `0111-6-meldezeile-skalierung`; bis AP3 die schmalsten Funkfelder und `tr0110-laengste-meldung-schmalstes-funkfeld`), `OberflaecheZuordnungBilderTests` (`tr0110-ansage-login`) |
| Meldezeile mit genau einer Meldung (Sarevoc Q112, 12.10.2026): Fehler vor Leitung vor Hinweis, gleicher oder höherer Rang ersetzt die stehende (sie kommt nicht wieder), niedrigere warten und verfallen mit ihrer eigenen Dauer, derselbe Schlüssel ersetzt auch wartend, eine gehaltene (Melden) wartet verdrängt und kommt wieder | `Meldezeile.ZeilenMax` = 1, `Setzen` (`_steht`, `_wartend`), `Abraeumen`, `Nachruecken`, `Zeigt` (stehend oder wartend), `Takt`/`Loesen` rücken nach; Fenster unverändert mit `ItemsControl` über `Zeilen` (0 oder 1); Tests `Oberflaeche/MeldezeileEineMeldungTests.cs`, `S2PhasenClientTests.Meldezeile_VorrangDauerHalten_EineMeldung`, Bilder `meldezeile-p1` bis `-p3` in `OberflaecheS2BilderTests` (drei Stände) |
| Meldezeile skaliert mit dem Funkfeld (Sarevoc Q111, -30 bis +30 %): Schrift, Innenrand und Höhe, damit bei jeder Skalierung ganz passt, was bei 0 % passt | `MeldezeileFenster.GroesseSetzen` (LayoutTransform am `Rahmen`, `MaxWidth` = Höchstbreite durch den Faktor), `Meldezeilenlage.Hoechstbreite(skalierung)` (`BreiteFest` mal Faktor), `Meldezeilenlage.Skalierung` (0,7 bis 1,3); Faktor aus `FunkfeldViewModel.Skala` über den Konstruktor (`App.xaml.cs`), neu abgeglichen bei `PropertyChanged` von `Skalierung`; Test `OberflaecheMeldezeileEinzeiligTests.Meldezeile_SkaliertMitDemFunkfeld_GanzeSaetzeBleibenGanz`, Bild `0111-6-meldezeile-skalierung` (-30, 0, +30 %) |
| Sprecheranzeige über dem Spiel (0.11.9, Sarevoc 11.10. und 15.10.2026, SPRECHERANZEIGE-ENTWURF.md im Planungsordner): wen du gerade hörst, Name fett und Kanal mit Ch. ("Durchsage an alle", "Durchsage: Kanal", "Global", "Du -> Kanal"), weiße Schrift mit Schatten (Variante B), ohne Fläche und Zeichen; nur gehörte Kanäle (abgeschaltete, Ruhe, gesperrte nicht, stumm geschaltete Funker nie; Durchsage und Global immer); ab Werk an, Lage 0 und 0 (ganz oben links; seit Sarevocs Änderung vom 15.10.2026 zwei Schieberegler statt Raster), 1 Zeile, 1,0 s Nachhalten, 0,5 s Ausblenden, eigene Sendung aus; "+n weitere" nur ab 2 Zeilen; Nachrücken nach dem Nachhalten | Datenweg `KlangBruecke.SprecherGehoert` (`GehoerterSprecher`, Art je Absender in `SprecherNetze.Art`, aus `EmpfangMelden`) und `KlangBruecke.EigeneSendung` (Ziel der offenen Phase); Logik `src/Funk.Client/Modelle/Sprecheranzeige.cs` (`Melden`, `Takt`, `ProbeZeigen`, `Kanaltext`, `EigenerKanal`, `Anzeigename`, `MitNamen`, `MeldeLueckeMs`) mit `Sprecherbild` (Zeilen, Schrift 26, `Schatten` als fertiger `DropShadowEffect`, Deckkraft, Innenrand) und `Sprecherzeile`; Stumm aus `DauersendViewModel.IstStumm`; Fenster `src/Funk.Client/Fenster/SprecheranzeigeFenster.xaml(.cs)` (Stil `Meldezeilenlage.Stil`, kein Owner, `Abgleichen` mit Topmost bei jedem Sichtbarwerden, `ZielFuer` mit `Ecke` (Prozent der Spanne zwischen den Rändern, Rand 24 mal Skala, begrenzt im Monitor)), Monitor `Spielfenster.MonitorImVordergrund`, Vollbild `Spielfenster.ExklusivesVollbild`; Vorlage `Sprecheranzeige.Bild` in `Design/Stile.xaml` (Fenster und Vorschau); Einstellungen `src/Funk.Client/Modelle/SprecheranzeigeViewModel.cs` (`Waagerecht`, `Senkrecht`, `Wahl` als `Sprecherlage`, jedes Schieben mit Probe, Erweitert, `Vorschau`, `Takt` für das Vollbild alle 2 s) im Abschnitt `EinstellungenViewModel.AbschnittSprecheranzeige` (Kasten mit `Einstellabschnitt.Hinweis`), Ansicht `AbschnittSprecheranzeige` in `Fenster/Bereiche.xaml`; Ablage `ClientEinstellungen.Sprecheranzeige` (`SprecheranzeigeEinstellungen`, nur Zahlen und Schalter; eine alte `stelle` wird beim Normalisieren umgerechnet); verdrahtet in `App.xaml.cs`; Tests `tests/Funk.Tests/Oberflaeche/OberflaecheSprecheranzeigeTests.cs` (Uhr der Tests, Brücke, Lage, Quelltextwächter, Bildlauf, Bilder `vorschau-0119-sprecheranzeige-*`) |
| S2 im Client: Funkfeld in der Ablage, Symbol im Infobereich | Zustand `src/Funk.Client/Modelle/FunkfeldAblage.cs` (`IFunkfeldFenster`, `IAblagesymbol`), echtes Fenster `src/Funk.Client/Fenster/FunkfeldFensterFuehrung.cs`, Symbol `Fenster/Ablagesymbol.cs` (Shell_NotifyIconW, `Symboldatei` wählt das Bild aus der eingebetteten `TacRelay.ico`), Verdrahtung `App.xaml.cs` (`FunkfeldBauen`: X legt ab) |
| S2 im Client: Tasten (Melde-Taste, Funkfeld ablegen/holen, Phase vor/zurück) | Funktionen `src/Funk.Klang/Eingabe/Tastenbelegung.cs` (`Tastenfunktionen.IstGehalten`, `IstAlt`), Quellen `RawInputSprechtaste.cs`, `Joystickauswertung.cs`, `DirectInputSprechtaste.cs` (`Gehalten`, `Ausgeloest`); Ablage `src/Funk.Client/Ablage/ClientEinstellungen.cs` (`TastenEinstellungen.Weitere` für Client 0.4.0); was eine Taste auslöst `src/Funk.Client/Modelle/Tastenbetrieb.cs`; Melde-Taste `src/Funk.Client/Klang/Sprechsteuerung.cs` mit `KlangBruecke` (`IMeldeziel`), Netz `Modelle/Meldenetzwahl.cs`; Einstellungen `TastenViewModel.cs`, `Bereiche.xaml` (`TastenZeileVorlage`) |
| S2 im Client: "Heute als Funker" | `src/Funk.Client/Modelle/FunkermodusViewModel.cs` (Rückfrage, Beim Verbinden), Karte in `Bereiche.xaml` (`FunkerKarte`), Fußzeile `Hauptfenster.xaml` (`FunkerStand`), Lage `FunkLage.cs` (`KontoInfo.AlsFunker`, `Hat`), Brett `BrettViewModel.Geschuetzt` |
| Übergang von 0.4.0 | `tests/Funk.Tests/Uebergang040Tests.cs` mit `tests/Funk.Tests/Daten/zustand-0.4.0/` (Erzeuger `Erzeuge040.cs.txt`, lief im unveränderten Stand d45e33f) |
| Rechte zur Zeit im Schnappschuss (Senderecht, Pushes) | `src/Funk.Server/Lage/Schnappschuss.cs` (`RechteAn`, `SitzungenMitRecht`, `RechteVon`, `Belegung`), `Lage/Arbeitsstand.cs` (`Wirksam`, `RechteVon`) |
| Kanalvorlage Star Citizen (10 Kanäle mit Ch., 14 Rollen) | `src/Funk.Protokoll/Steuerung/Flottenvorlagen.cs`, Konsole `flotte-vorlage --name starcitizen`, Verwaltung Werkzeuge; "Schiff n" ist ein Rollenfeld ohne festen Ch., "alle" der feste Ch. "Alle" am Ende der Liste (Annahmen in der Vorlage); zwei Felder einer Rolle im selben Kanalnetz nur mit verschiedenem Ch. (`Flottenentwurf.Feldschluessel`, am Server `Feldregeln.ChAufloesen` mit `vergeben`); Testdaten `tests/Funk.Tests/Daten/starcitizen/` |
| Spielprofil (19.10.2026, Q10): Star Citizen ab Werk oder ein anderes Spiel über seine Programmdatei, nur für die Monitorsuche | `src/Funk.Client/Ablage/Spielprofil.cs` (JSON `spiel`, Prozessname aus Eingabe), `Fenster/Spieldatei.cs` (Dateidialog), `Fenster/Spielfenster.cs` (`Monitor(prozess)`, `MonitorImVordergrund(prozess)`), `Modelle/SprecheranzeigeViewModel.cs` (`Spiel*`), `Bereiche.xaml` unter Sprecheranzeige > Erweitert, `App.xaml.cs` (Sprecheranzeige, Meldeanzeige, Meldezeile); Tests `OberflaecheSpielprofilTests` |
| Namenswächter (Umbau JG TacRelay): alte Gilden-, Vorlagen- und Personennamen nirgends im Repository | `tests/Funk.Tests/NamenswaechterTests.cs` (kurze Ausnahmeliste mit Grund; Datei- und Ordnernamen immer) |
| Lage der Stufe 2 im Client (Belegung, eigener Auftrag, Details je Person, letzter Stand, Sperre mit Urheber) | Lagebild `src/Funk.ClientKern/Lagebild.cs` (`Belegung`, `ZeitVersatzMs`), Abbildung `src/Funk.Client/Dienste/LageAbbildung.cs`, Typen `FunkLage.cs` (`KontoInfo.Einsatzleitung`, `PlatzInfo.Reserviert`, `FunkerDetails`) |
| Brett der Einsatzleitung (Bereich Raid, Strg+5) | Modell `src/Funk.Client/Modelle/BrettViewModel.cs` (Bewerten, Ablegen, Verlauf, Zurücknehmen, Abend beenden; seit 0.11.1 `Bewerten`/`AblegenAsync` auch für eine `KanalPerson`, `PersonenAbgleichen` (ein Durchgang über die Funker für alle Kanäle, seit der Prüfung), `FeldUmziehen`, `FeldLeeren`; `BrettZiehen.CanStartDrag` lässt nur Karten und Zusatzkanal-Kacheln ziehen, Q116), Teile `BrettTeile.cs` (Karten, Blöcke, Spalten, Ablageziele für gong-wpf-dragdrop, `KanalPerson` als Mini-Kachel unter einem Netz rechts mit Kreuz), Ansicht `src/Funk.Client/Fenster/BrettAnsicht.xaml` (alle Ziele mit `DropEventType="Bubbled"`, siehe Fallen), eigener Platz im Hauptfenster (`HauptViewModel.BrettInhalt`); Vorschau beim Ziehen und Landung (0.11.1 AP2, Sarevocs Punkt 4): `BrettViewModel.VorschauZeigen`/`VorschauVerbergen`/`Landeblock` (mit `_vorschauZiel`: nur das Ziel, das sie gesetzt hat, nimmt sie weg; in einem Block ohne Platzgrenze eine nur dafür eingehängte Zeile `Vorschauzeile`, `_vorschauEingehaengt`, in `PlaetzeVerteilen` über Pushes hinweg gehalten, Stelle `Einfuegestelle` nach Namen; beides seit der Prüfung), Ereignis `Gelandet` aus `AblegenAsync` mit `Ablagepunkt`, `FreierPlatz.Vorschau`/`VorschauSichtbar` (`BrettTeile.cs`), `Weichblende`, `Sanftlauf` und `Sanft.Gleiten` in `src/Funk.Client/Steuerelemente/Sanft.cs` (Deckung 0,45, 120/150/150 ms), Vorlage `FreierPlatz` in `BrettAnsicht.xaml`, Gleiten `BrettAnsicht.xaml.cs` (`Gelandet`, `LetzteLandung`), Tests `Oberflaeche/OberflaecheZuordnungVorschauTests.cs`; Dienst `IFunkDienst` (`VerschiebenAsync`, `SelbstwahlSetzenAsync`, `KontenVerzeichnisAsync`, `AuftragAbgebenAsync`); Drahttests `tests/Funk.Tests/ZuordnungBrettDrahtTests.cs`, WPF-Ereignisse `Oberflaeche/OberflaecheZuordnungZiehenTests.cs` |
| Personenkachel (Q98, 0.11.0): Person als Kachel 97 x 30 links im Brett (bis 0.11.4 auch in "Wer ist da", seit 0.11.5 dort der Name-Knopf 194 breit mit `WerIstDa.NameInhalt`, der nie kürzt), Abzeichen oder Anfangsbuchstabe, Punkt für angemeldet, Ring für offline, Name mit "..." | Stil und Inhalt `src/Funk.Client/Design/Stile.xaml` (`Personenkachel.Knopf`, `Personenkachel.Inhalt`, seit der Gegenprüfung 0.11.5 mit `Personenkachel.Abzeichen` als eigenem Teil, gebunden nur über `Name`, `Abzeichen`, `HatAbzeichen`, `Anfang`, `Online`); Modelle `WerIstDaZeile` (auch aus `VerzeichnisEintrag`, offline) und `BrettKarte` (`Gezogen`); Wer ist da `Bereiche.xaml` (seit 0.11.5 eine Zeile je Person; Schalter `AuchOfflineSchalter` nur für die Einsatzleitung, `WerIstDaViewModel.AuchOffline`, Fehler "Auch offline:" geht nach Erfolg und beim Ausschalten weg), Brett `BrettAnsicht.xaml` (`Brett.Personenkachel`, Ziehbild `Brett.Personenkachel.Geist`, `Brett.Kachelraster`); Tests `OberflaecheWerIstDaTests.WerIstDa_AuchOfflineNurFuerDieEinsatzleitung`, Bilder `OberflaecheKachelBilderTests` (Brett) |
| Ch. je Person im Brett (Q46 für die Einsatzleitung, 0.11.0): "Ch." je Feld im Detail, felderPatch mit Feldnummer | `BrettViewModel` (`DetailSetzen` füllt `DetailFeld.ChWaehlbar`, `FeldChWaehlen`, `ChRasterFuellen`, `ChWaehlen` mit `_chAblage` für Ablage und Feld, `FeldCh`), `BrettTeile.cs` (`DetailFeld`, `KnoepfeUnten`: an einem Zusatzkanal mit Ch. stehen die Knöpfe unter dem Kanaltext), Knöpfe `Feldknoepfe` mit `ChWahl` in `BrettAnsicht.xaml`, Rollen ins Bild und Fokus auf den ersten wählbaren Ch. `BrettAnsicht.xaml.cs` (`HatChWahl`, `ChRasterZiel`, zurück auf "Ch." nach Esc); Rollenfeld oder Zusatznetz `BrettViewModel.AusDerRolle`; Server: Ruhe auf Ch. wird Sendeziel `Zustandsschreiber.Zuweisen.Anwenden`/`VonRuheAufCh`; Attrappe `FunkDienstAttrappe.Raid.cs` (felderPatch mit Feld wie am Server, Kurzform "#f"); Tests `Stufe36BrettTests.Brett_ChJePerson_*`, `Brett_FestesRollenfeldMitCh_IstKeinZusatzkanal`, Server `ZuordnungServerTests.ChJePerson_*`, Bild `OberflaecheZuordnungBilderTests` |
| Ansage beim Login (Zuordnung Punkt 3, 0.11.0): "Eingeteilt von X: Schiff 1, Gunner" (ohne Platz "Kanäle eingeteilt von X") 12 s in einer Zeile, Platzrolle oben links; Server meldet die Herkunft | Client `Teilnehmermeldungen.Ansagen` (Schlüssel `ansage`, `_neueSitzung`, `_angesagt` mit `Einteilung` (durch, Einheit, Rolle), jede online gesehene zählt), `Meldezeiten.AnsageMs`, `FunkfeldViewModel.PlatzRolleText`/`RolleName`/`RolleHilfe`/`RolleMarkiert` (Liste und Mausrad gehen von der Platzrolle aus); Server `Zustandsschreiber.Wiederherstellen` (VERSCHIEBEN mit `durch` aus der Reservierung, nach eigener Änderung bleibt SELBST/ROLLE/SATZ, STANDARD für ein nie angemeldetes Konto nach "zurück auf Standard"), `Zustandsschreiber.Zuweisen.Anwenden` (keine Reservierung für "zurück auf Standard"); Tests `S2TeilnehmerTests.Ansage_*`, `Stufe35OberflaecheTests.Rolle_OhneGewaehlteRolle_ZeigtObenLinksDiePlatzrolle`, Draht `Stufe34DrahtTests.Draht_AnsageBeimLogin_NachVorabEinteilung`, `ZuordnungServerTests` |
| Gemerkte eigene Rolle (Zuordnung Punkt 4, 0.11.0) | `FunkfeldViewModel.RolleBeobachten` (merkt nur die vom Server gemeldete oder selbst gewählte Rolle, schickt sie nur bei STANDARD), Ablage `ClientEinstellungen` (`Funkfeld.Rolle`); Tests `Stufe34FunkfeldTests.Rolle_NachDerWiederverbindungsfrist_StelltDerClientWiederHer`, Draht `Stufe34DrahtTests.Draht_RollenlisteChWaehlerUndRolleNachDerWiederverbindungsfrist` |
| Untergilden entfallen (0.11.8): 0x001D nur als Hülle, gespeicherte `untergilden` durchgereicht (nur Nummern gelöschter Konten und Vormerkungen fallen heraus), Liste einfügen übergeht Spalte 3 | Server `Zentrale.Mitglieder.cs` (`Untergilde`: LISTE leer, sonst UNGUELTIG), `Mitgliederbuch.cs` (Datensatz `Untergilde` nur zum Durchreichen, `KontoEntfernt` und `OhneVormerkung` räumen Nummern weg, `Einfuegen` mit `Spalte3Hinweis`), `Zustandsablage.cs`; Verwaltung `MitgliederViewModel.Einfuegen` (`Einfuegeergebnis.Hinweis`); Tests `MitgliederTests.Untergilden_AlterStandBleibt_GeloeschteNummernFallenHeraus`, `Uebergang020Tests.Zustand020_Untergilden_NurGeloeschteNummernFallenHeraus`, `AdminSitzungTests.Zusammenspiel_MitgliedskarteUndZeichen`, `OberflaecheVerwaltungTests.Verwaltung_OhneSeiteUntergilden` |
| Einsteigen mit Einheitsarten, "wie letztes Mal", Sperre mit Grund | `src/Funk.Client/Modelle/NetzeViewModel.cs` (Reiter, `PlatzWahl`, `SperrGrund`), Karte `EinsteigenKarte` auf Start (seit 0.10.0, E10, nur mit Einheiten; bis 0.9.3 auf Meine Kanäle) in `src/Funk.Client/Fenster/Bereiche.xaml`; Sprung auf Start, wenn das Brett geht (`HauptViewModel.RaidZeigen`); Sperrtext `FunkClientDienst.Sperrtext` |
| Beispielabend (Anhang A) für Tests und Bilder | `src/Funk.Client/Dienste/FunkDienstAttrappe.Raidabend.cs`, freundlicher Server `FunkDienstAttrappe.Raid.cs` |
| Flotten-Einstellungsseite (Entwurf, Prüfung, Paket, Sicherung) | Logik `src/Funk.Verwaltung/Flotte/Flottenentwurf.cs` (Folge im Paket, `Pruefen`, `Umsetzen`), Modell `src/Funk.Verwaltung/Modelle/FlotteViewModel.cs`, Ansicht `Ansichten/VerwaltungAnsicht.xaml`; Sicherungen im Ordner `flotte` neben den Einstellungen (`VerwaltungsWirt.Ablageordner`, App.xaml.cs) |
| Einsatzleitung vergeben und entziehen (Verwaltungsteil) | `src/Funk.Verwaltung/Modelle/EinsatzleitungViewModel.cs`, Draht `Verwaltungsdienst.cs` (`AuftragErteilenAsync`, `FlotteUebernehmenAsync`) |
| Übergang von 0.2.0 | `tests/Funk.Tests/Uebergang020Tests.cs` mit `tests/Funk.Tests/Daten/zustand-0.2.0/` (Erzeuger dort als `Erzeuge020.cs.txt`), Ersteller alter Aufstellungen `src/Funk.Server/Kern/Zustandsablage.cs` (`LageLaden`), Aufträge aus Sicherungen `Zustandsablage.OhneAuftraege` |
| Fassung (Client, Server, Setup) | nur `Directory.Build.props`; Hello nimmt sie über `FunkClientOptionen.Fassung`, der Server über `Zentrale.Version`; Anleitungen und Paketvorlagen nennen keine höhere (`SetupTests.Setup_AnleitungenNennenKeineFassungUeberDerEigenen`) |
| Anleitungen für Sarevoc: Heimserver, Updates, Härtung, Betrieb | `docs/HEIMSERVER-PROXMOX.md` (Abschnitt 14: Update auf 0.2.0, Abschnitt 15: Update auf 0.3.0, Abschnitt 16: auf 0.4.0 von 0.2.0 oder 0.3.0, mit dem, was aus 15 fehlt), `docs/HEIMSERVER-HAERTUNG.md`, `docs/BETRIEB.md`, Vorlage `tools/paket/LIESMICH-Server.txt`; die zitierten Konsolenantworten stehen in `src/Funk.Server/Kern/Zentrale.Verwaltung.cs`, `Zentrale.Admin.cs`, `Zentrale.Raidleitung.cs`, `Zentrale.Reparatur.cs` und `Zentrale.Sperrliste.cs`, die Meldungen von `pruefen` in `src/Funk.Server/Betrieb/Konfiguration.cs` und `Uhrpruefung.cs` |
| Sperrliste gegen Scanner und Fluten (A51) | `src/Funk.Server/Netzzugang/Sperrliste.cs` (Punkte, Sperren, Durchlass, Ablage, Klartextzeile), Konfiguration `src/Funk.Server/Betrieb/SperrlistenKonfiguration.cs`, Anbindung `Netzzugang/TlsAnnahme.cs` (vor TLS, Handshake, Entlasten), `Steuer/Verbindung.cs` (`Verstoss`), `Kern/Zentrale.Anmeldung.cs` (Anträge, Codes), Konsole und Ablage `Kern/Zentrale.Sperrliste.cs`; fail2ban-Beispiele `konfig/fail2ban/`; Tests `tests/Funk.Tests/SperrlisteTests.cs` |
| Härtung des Hosts (Checkliste, nichts davon ausführbar) | `docs/HEIMSERVER-HAERTUNG.md` |
| Uhrprüfung beim Start und bei `pruefen` | `src/Funk.Server/Betrieb/Uhrpruefung.cs` (adjtimex nur lesend, Plausibilität), Aufruf in `Dienst.cs` und `Program.cs` |
| Befehle einer Anleitung so ablaufen lassen, wie Sarevoc sie abschreibt (Weg A, Zeile in server.json, Reihenfolge in der Härtung) | `tests/Funk.Tests/HaertungTests.cs` (`AnleitungsBefehle`, Tests `Anleitung_*`), Sarevocs server.json aus 0.2.0 als `tests/Funk.Tests/Daten/server-0.2.0-heim.json` |
| Antrag ins falsche Konto (Hinweis und Reparatur) | Vergleich `src/Funk.Protokoll/Identitaet/Kontovorschlag.cs`, Umhängen `Kontenbuch.Umhaengen.cs`, Konsole `src/Funk.Server/Kern/Zentrale.Reparatur.cs` (`antraege`, `geraet-umhaengen`), Freigabeansicht `src/Funk.Verwaltung/Modelle/AntraegeViewModel.cs` (`BestehendesKonto`); Anleitung HEIMSERVER-PROXMOX.md, Ende von Abschnitt 14 |
| Gerätename im Antrag aus der Kennung (0.6.3, nie der Windows-Name) | `VerbindungsEinstellungen.NameAusKennung` in `src/Funk.Client/Ablage/ClientEinstellungen.cs`, gesetzt in `Verbindungsfabrik`, Zeile der Antragskarte `VerbindungViewModel.GeraetenameSatz` |
| Freigabe mit "Verifiziert" statt abgetippter Kennung (0.6.3) | `AntragZeile.Verifiziert`, `FreigabeGesperrt`, `KennungHinweis` in `src/Funk.Verwaltung/Modelle/AntraegeViewModel.cs`, Karte in `Ansichten/VerwaltungAnsicht.xaml` |
| Konto löschen im Verwaltungsteil (0.6.3) | `MitgliedKarte` (`KannLoeschen`, `LoeschenFragen`, Befehle `KontoLoeschen*`) und `MitgliederViewModel.KontoLoeschenAsync` in `src/Funk.Verwaltung/Modelle/MitgliederViewModel.cs`, Draht `Verwaltungsdienst.KontoLoeschenAsync` (KontoVerwaltung LOESCHEN, heikel) |
| Kein automatischer Neuversuch nach Bye{ENTFERNT} (0.6.3) | `ClientAutomat.ByeEmpfangen` in `src/Funk.Protokoll/Steuerung/ClientAutomat.cs`, Satz in `src/Funk.Client/Dienste/Verbindungstexte.cs`, Zustand `FunkLage.Entfernt` (gesetzt in `FunkClientDienst.SitzungBeendet`), Haken der Startseite `StartViewModel` über `VerbindungViewModel.Entfernt` |
| Ablehnen löscht den Antrag, Löschen sperrt nichts (0.7.0) | `Kontenbuch.AblehnenIntern` in `src/Funk.Protokoll/Identitaet/Kontenbuch.Geraete.cs` (auch für `AntraegeAblehnen`), `KontoEntfernenIntern` in `Kontenbuch.cs`; Push aus dem Stand davor `GeraetStatusGeaendert.Vorher` (`Wirkungen.cs`, `Zentrale.GeraetStatusVerteilen`), Bye ENTFERNT in `Zentrale.WirkungenAusfuehren`, Journal `FreigabeVermerken` in `src/Funk.Server/Kern/Zentrale.Anfragen.cs`; einzige Gerätesperre bleibt `GeraetEntfernen` dauerhaft (Konsole `sperren`) |
| Alte Gerätesperren beim Laden verwerfen (0.7.0) | `Zustandsablage.KontenLaden` (`NurBewussteSperren`, `AltsperrenVerworfen`, `VerworfeneSperren`) in `src/Funk.Server/Kern/Zustandsablage.cs`, Logbuchzeile, Journal `SPERRE_VERWORFEN` je Kennung und sofortiges Speichern im Konstruktor von `Zentrale`; Test `ServerGrundTests.Ablage_SperrenAus06_NurBewussteBleiben` |
| Ein PC, ein Konto (0.7.0): belegter Discord-Name bremst die Freigabe nicht | `AntragFreigeben` in `src/Funk.Protokoll/Identitaet/Kontenbuch.Geraete.cs`; Freigabeansicht ohne Kontoauswahl, nur Hinweis `AntragZeile.BestehendesKonto` in `src/Funk.Verwaltung/Modelle/AntraegeViewModel.cs`; die Konsole hängt mit `--an-konto` weiter an |
| Gerätesperre aufheben (0.8.0) | `Kontenbuch.GeraetEntsperren`, `FingerabdruckEntsperren`, `SperreSuchen` in `src/Funk.Protokoll/Identitaet/Kontenbuch.Geraete.cs`; Draht `GeraetEntscheiden` mit `ENTSPERREN` (`Zentrale.GeraetEntsperren` in `Zentrale.Anfragen.cs`), Versionstor `Versionen.EntsperrenAb`/`ServerAb`; Konsole `sperre-aufheben` in `Zentrale.Verwaltung.cs` und `src/Funk.Admin` (Befehlsoptionen, Uebersetzung, Hilfe); Mitgliederkarte `GeraetZeile`, `MitgliedKarte.Geraete` in `src/Funk.Verwaltung/Modelle/MitgliederViewModel.cs`, Dienst `GeraeteAsync`, `SperreAufhebenAsync` |
| Umbau der Verwaltung (0.8.0): Navigation in Gruppen, Heute, Entwurfsleiste, Werkzeuge | Ansicht `src/Funk.Verwaltung/Ansichten/VerwaltungAnsicht.xaml` (`NaviLinks`/`NaviOben`, `HeuteBereich`, Reiter der Flotte, `WerkzeugeBereich`, `EntwurfLeiste`; Breiten `NaviLinksAb` und `FeldRechtsAb` in `VerwaltungAnsicht.xaml.cs`); Modell `VerwaltungViewModel` (`Verwaltungsbereich.Heute`/`Werkzeuge`, `IstKanaele` bis `IstPhasen` über den Reiter der Flotte, `HeuteAktualisieren`, `HeuteZeile`, `HinweisZeigen`); eigener Platz mit eigenem Bildlauf im Hauptfenster `VerwaltungPlatz` (`HauptViewModel.IstVerwaltungsseite`, `VerwaltungInhalt`); Entwurf `docs/entwurf/VERWALTUNG-UMBAU.md` |
| Herkunftsschild an Kanal und Rolle (0.8.0) | Draht `Netz.Herkunft`, `Rolle.Herkunft`, `Grenzen.HerkunftMax` in `src/Funk.Protokoll/Steuerung`; Server `L.Netz`/`L.Rolle` in `src/Funk.Server/Lage/Modell.cs`, ohne Feld bleibt die gespeicherte (`NetzSetzenAus`, `RolleSetzenAus` in `Zustandsschreiber.Verwaltung.cs`); Vorlagen `Flottenvorlagen.cs`; Ableitung, Werte und Filter `HerkunftVon`, `Herkuenfte` in `src/Funk.Verwaltung/Flotte/Flottenentwurf.Klartext.cs`; nie eine Änderung: `Flottenentwurf.Gleich` |
| "Was ändert sich?", "Verwendet in", Hinweise der Übersicht Heute (0.8.0) | `Klartext`, `VerwendetIn`, `Hinweise` in `src/Funk.Verwaltung/Flotte/Flottenentwurf.Klartext.cs`; Sprünge `KanalZeigen`, `RolleZeigen`, `EinheitZeigen`, `PhaseZeigen` in `FlotteViewModel`; Tests `FlottenentwurfUmbauTests`, `OberflaecheVerwaltungUmbauTests` |
| Funkfeld-Editor der Rollen, Rolle verdoppeln, Werkzeuge mit Rückfrage (0.8.0) | `RolleBearbeitung` (`Kacheln`, `GewaehltesFeld`, `FeldDazu`, `FeldLeeren`, `KanalOeffnen`), `FeldBearbeitung` (`KachelName`, `KachelCh`, `KachelZustand`, `KachelZusatz`, `KanalId`), `GlobalKachel`, `WerkzeugZeile` in `src/Funk.Verwaltung/Modelle/FlotteViewModel.cs`; Kanalwahl je Herkunft `Felderform.QuellenAbgleichen`; Kopie `Flottenentwurf.RolleVerdoppeln` |
| Sitzungsstart direkt nach einer Freigabe (Rennen, 0.8.0) | `Zentrale.SitzungStartenAsync` in `src/Funk.Server/Kern/Zentrale.Anmeldung.cs` spiegelt vorher (`SpiegelAbgleichen`); Zeitlupe nur für Tests `Zentrale.FreigabeZeitlupeMs`; Test `HaertungTests.Freigabe_SitzungsstartImSelbenDurchlauf_KeinUnbekannt` |
| Pegelanzeigen in drei Zuständen (Q43, 0.8.0): weiß, gelb "würde senden, geht aber nirgends hin", grün | `PegelMeldung.WuerdeSenden` in `src/Funk.Klang/Maschine/IKlangMaschine.cs` (nur der Client setzt es), `PegelVerlauf.WuerdeAusloesen` und `Hinzufuegen` in `src/Funk.Client/Klang/PegelVerlauf.cs`; Farbe `Farbe.WuerdeSenden`/`Pinsel.WuerdeSenden` (`Design/Farben.xaml`), `Palette.WuerdeSenden`, `Akzente.WuerdeSenden`/`WuerdeSendenFlaeche`/`WuerdeSendenStift`/`WuerdeSendenDeckkraft`; `Pegelbalken.WuerdeSenden`, `SchwellenGrafik` (`Teil`, `Hinterlegen`, `Abschnitte`); `AudioViewModel.WuerdeSenden`, `SchwellenViewModel.WuerdeSenden`/`GrafikHilfe`, `KorridorViewModel.HilfeFarben`/`HilfeBalken`; Funkfeld `KachelViewModel.WuerdeSenden`/`Sendeziel`, `FunkfeldViewModel.Takt`, Stil `Kachel.Pegel` in `Fenster/FunkfeldFenster.xaml`; Tests `Oberflaeche/OberflaechePegelGelbTests.cs` (Sichtproben mit FUNK_BILDER) |
| Satz nach abgelehntem Antrag, "offline" in der Mitgliederliste (0.7.0) | `Verbindungstexte.Bye` (ENTFERNT ohne AKTIV-Zeit) in `src/Funk.Client/Dienste/Verbindungstexte.cs`; `MitgliedZeile.Zustand` in `src/Funk.Verwaltung/Modelle/MitgliederViewModel.cs` |
| Befunde der Härtung 0.3.0 (Rennen, Journal, Echo, Notfallcode, Setup, Baupfade) | Tests `tests/Funk.Tests/HaertungTests.cs` |
| Welche Pflichttypen ein älterer Server kennt (neuer Client, alter Server) | `src/Funk.Protokoll/Steuerung/Versionen.cs` (`ServerFassungFuer`, `ServerKennt`), Sperre vor dem Senden in `src/Funk.ClientKern/FunkClient.cs` (`AnfrageAsync`) |
| Abgelaufene Platzfristen ohne Aktion melden (Brett) | `Schnappschuss.PlatzzustandGewechselt`, Takt `Zentrale.PlatzfristenMelden` in `src/Funk.Server/Kern/Zentrale.Raidleitung.cs` |
| Profilübernahme aus Welcome (F1), Ablage `serverprofil.json`, Protokoll | `src/Funk.Client/Dienste/Profiluebernahme.cs`, Wahl Setup oder Ablage `Profilpruefung.Waehlen`, Anschluss `FunkClientDienst.PushErhalten` und `Verbindungsfabrik`, Karte `VerbindungViewModel.ProfilHerkunft` |
| Pin des liefernden Servers bei der Profilübernahme (3.1) | `TlsAufbau.LetzterPin` aus `Steuerverbindung.Pin`, `FunkAnbindung.ServerPin` (Verbindungsfabrik), `Profiluebernahme.Annehmen(angebot, serverPin)` |
| Was funk-einrichten.sh nach /opt/funk kopiert | `programmdateien` und `gleich` in `konfig/funk-einrichten.sh` (seit 0.4.0 auch `konfig/fail2ban/*/*.conf`, Unterordner legt die Kopierschleife an); Abgleich `EinrichtenSkriptTests.Skript_VergleichtUndKopiertNurProgrammdateien` |
| Warnung, wenn im Profil der eigene Pin fehlt oder ein anderer TCP-Port steht (Start und `profil-neu-laden`) | `Zentrale.ProfilPinWarnung`, `EigenerPin` und `ProfilFehlerBeimStart` in `src/Funk.Server/Kern/Zentrale.cs`, Port `Profilabgleich.PortWarnung`, Aufruf in `src/Funk.Server/Dienst.cs` und `Zentrale.Verwaltung.cs`; Test samt den Zitaten in HEIMSERVER-PROXMOX.md und UMZUG.md `tests/Funk.Tests/ProfilPinWarnungTests.cs` |
| Tastenkürzel in der Fußzeile des Hauptfensters | `HauptViewModel.Tastenhinweis` (seit 0.10.0 "Strg+1 Start  Strg+2 Einstellungen", Strg+5 und Strg+9 nur mit ihren Bereichen, ohne Strg+T; Strg+3 und Strg+4 entfallen, E2), links die Warnungen beim Start `HauptViewModel.Starthinweise` statt des Klangtreibers (E13; der Klangtreiber steht unter Fehlersuche, `EinstellungenViewModel.Klangtreiber`), `src/Funk.Client/Fenster/Hauptfenster.xaml` (`Tasten`); Tests `OberflaecheHauptfensterTests.Fusszeile_NenntStrg5UndStrg9NurMitIhrenBereichen`, `OberflaecheUmbauEinstellungenTests.Umbau_Fusszeile_StarthinweiseLinks_KlangtreiberUnterFehlersuche` |
| Abschluss von `funk-admin einrichten` (Name, Ports, Pin, Schutz, Weiter) | `Einrichtung.AbschlussAsync` in `src/Funk.Admin/Einrichtung.cs`; Beispiel in `docs/EINRICHTEN.md`, Abschnitt 3 |
| Erstverbindung mit Pin-Fehler an einem Host, Profilwechsel im Aufbau | `src/Funk.ClientKern/TlsAufbau.cs` (`AufbauenAsync`, `ProfilSetzen`; Tests über `HandshakeErsatz`) |
| Einrichten mit funk-admin: konfig-schreiben, pruefen --vollstaendig, profil-anfrage, erster-admin | `src/Funk.Admin/Konfigschreiber.cs`, `Serverpruefung.cs`, `Systemblick.cs` (Rechte, Uhr, DNS, Ports, chown), `Profilanfrage.cs`, `ErsterAdmin.cs`, Schlüsselordner `Schluesselorte.cs` |
| Erzeugen nur mit `--ersetzen` über Vorhandenes | `Dateiablage.AtomarSchreiben(ersetzen)` und `SchonDaException` in `src/Funk.Server/Betrieb/Dateiablage.cs`; Folgen je Befehl in `src/Funk.Admin/Program.cs` (`Folge`) |
| Profil erneuern auf Sarevocs Stick | `src/Funk.Admin/Profilerneuerung.cs` (ein anderer TCP-Port wird abgelehnt, solange ein anderer Host im Profil bleibt), Rückfragen `Dialog.cs`; Doppelklick `tools/paket/Profil erneuern.cmd` (drei Zeilen, CRLF, kommt in beide Pakete nach `linux/`) |
| Umzugspaket: Format, Verschlüsselung, packen, einspielen | `src/Funk.Admin/Umzugspaket.cs`, `Umzug.cs` |
| Einrichten mit einem Befehl: Teil vor .NET (root, systemd, apt, libicu, .NET 10, Programme nach /opt/funk) | `konfig/funk-einrichten.sh` (LF, `.gitattributes`), statisch geprüft in `tests/Funk.Tests/EinrichtenSkriptTests.cs` |
| Einrichten mit einem Befehl: alles danach (Fragen, Benutzer, Schlüssel, server.json, Units, Drop-in, echo.env, Kurzbefehl, Uhr, Firewall, Umzug, Profil, Start, Journal, Abschluss) | `src/Funk.Admin/Einrichtung.cs` (`funk-admin einrichten`), Pfade `Einrichtorte.cs` (gleich den Units), Systemaufrufe `Systemblick.Ausfuehren`/`ModusSetzen`; Tests `EinrichtungTests.cs` mit `LinuxAttrappe` |
| Adresse des Echo-Funkers | `/etc/funk/echo.env` (`FUNK_ECHO_ADRESSE`), gelesen von `konfig/funk-echo.service` (`EnvironmentFile=-`, Voreinstellung `Environment=`) |
| Anleitungen Einrichten und Umzug | `docs/EINRICHTEN.md` (mit Probelauf im Wegwerf-Container), `docs/UMZUG.md`; zitierte Ausgaben stehen in `Einrichtung.cs`, `Umzug.cs`, `Profilerneuerung.cs`, `ErsterAdmin.cs` |
| Nachzügler vor dem Umzug (`geraete --nicht-seit`) | `src/Funk.Server/Kern/Zentrale.Verwaltung.cs` (`geraete`), `src/Funk.Admin/Uebersetzung.cs` |
| UMZUG.md so ablaufen lassen, wie es dasteht (Schritt 3, 4 und 6, Stick auf 0.4.0) | `tests/Funk.Tests/UmzugAnleitungTests.cs` (Server mit `Testuhr`, Neustart mit `Testserver.StartenAsync(profilBehalten: true)`) |
| Startfehler von `funk-admin einrichten` übersetzen (243, 226, Konfiguration) | `Einrichtung.Fehlerbild` in `src/Funk.Admin/Einrichtung.cs`; 243 im LXC ohne fehlende Datei nennt `nesting=1` |
| Übertragungen fremden Codes im eigenen Code (QR nach Nayuki, weiches Clipping nach libopus) | `THIRD-PARTY-NOTICES.txt` Nr. 11 und 12; Suche nach Merkmalen in `tests/Funk.Tests/UrheberrechtTests.cs` |
| Spezifikation | `docs/FUNKPROTOKOLL-v1.md` |
| Handbuch (Mitglieder, Admins, Sicherheit, Last, Klang, Recht) und sein PDF | Kapitel `docs/handbuch/00-titel-und-inhalt.md` bis `95-offene-punkte.md`, Bauanleitung und letzter Bau `docs/handbuch/BAUEN.md`; PDF mit `tools/handbuch-pdf.py` (reportlab aus `C:\Claude\tools\office\.venv`, nur Helvetica und Courier, nicht eingebettet; prüft das Ergebnis selbst, Exitcode 0 heißt keine Abweichung) |

## Fallen, die schon zugeschnappt sind

- gong-wpf-dragdrop hängt sich mit `DropEventType` Auto bei jedem Ziel, das kein ItemsControl ist, an die Preview-Ereignisse von WPF (sie
  tunneln von der Wurzel nach innen; was dort als erledigt gilt, erreicht keinen weiteren Handler). Ein Ziel, das Ziele enthält (die Spalte
  der Einheit um ihre Blöcke), fängt dann jede Ablage darin ab: Bis 0.11.0 landete jede Karte beim "besten Platz" der Spalte, in der früheren
  Kanalvorlage auf der ersten Rolle (Sarevocs Befund vom 12.10.2026, "landet immer auf Standard"). Darum `dd:DragDrop.DropEventType="Bubbled"` an allen Zielen des Bretts.
  `OberflaecheZuordnungZiehenTests` löst das Ereignispaar wie WPF aus (PreviewDrop, dann Drop) und prüft die Regel; Modelltests, die `AblegenAsync`
  direkt rufen, sehen so etwas nie.
- WPF-Uhren (`AnimationClock`) rechnen erst beim nächsten Takt des Zeitgebers; `Seek`, `Pause` und `SkipToFill` sind bis dahin nur vorgemerkt,
  und `Completed` kommt aus dem Takt. Im Bildlauf bringt `Bildlauf.Leeren()` den Takt (Render-Priorität), aber danach läuft echte Zeit mit.
  Deshalb setzt `Sanftlauf.Beenden` (`Steuerelemente/Sanft.cs`) die Endwerte selbst und nimmt die Uhren von den Eigenschaften, statt auf
  `Completed` zu warten, und `Spulen` hält die Uhr nach dem Seek an (0.11.1 AP2, `OberflaecheZuordnungVorschauTests`). Eine Uhr, die mit
  HoldEnd am Ende steht, ist keine Daueranimation, hängt aber an der Eigenschaft, bis sie jemand wegnimmt.
- Glasknöpfe zeichnen ihren Schatten außerhalb ihrer Grenzen. Wird ein Elternteil beim Anordnen um Bruchteile kleiner als
  gewünscht, bekommt es einen Layout-Clip und schneidet den Schatten ab (Startseite im Warteraum, 02.10.2026). Deshalb hat
  die Startkarte innen 4 Einheiten Luft seitlich und 12 unten; `OberflaecheStartseiteTests.SchattenFrei` prüft jeden Glasknopf
  gegen jeden abschneidenden Vorfahren.
- WPF lässt die Metadaten von `ToolTipService` je Typ nur einmal überschreiben: Die Hilfe kommt nur aus Glasdesign, und die
  unbegrenzte Anzeigedauer muss vor deren erstem Hilfetext gesetzt sein (`App.HilfeEinrichten`, auch im Bildlauf).
- Ein `ToolTip` darf keinen Elternteil haben: Tests zeichnen ihn allein und legen ihn rechnerisch auf den Grund.
- Ein ausgeblendeter Knopf hat seine Vorlage noch nicht angewandt: Wer nach dem Glaskörper fragt, ruft vorher `ApplyTemplate`.
- `Set-Location` in einem Befehl verschiebt das Arbeitsverzeichnis der ganzen Sitzung. Nur absolute Pfade.
- Die Liste links im Hauptfenster bindet `SelectedItem` an `HauptViewModel.AktiverBereich` (zweiseitig). Einen Eintrag, den sie nicht hat,
  setzt sie auf nichts zurück, und entfernt man den gewählten, bevor sie den Wechsel kennt, schreibt sie null in den Bereich (gemessen im
  Befund der Prüfung von 0.10.0: Die partiellen Methoden `OnAktiverBereichChanged` laufen vor `PropertyChanged`). Darum den Unterpunkt erst
  in die Liste und dann wählen (`_seiteOeffnet`), und erst nach `base.OnPropertyChanged` entfernen, im Betrieb über den Takt.
- Mutationsprobe: Wer eine Datei nach dem Verfälschen mit `Copy-Item` zurücklegt, bekommt den alten Zeitstempel zurück; die Ausgaben
  des verfälschten Baus sind dann neuer, MSBuild hält den Stand für aktuell und testet weiter das Falsche (Klangstufe C2b, 04.10.2026).
  Nach dem Zurücklegen `LastWriteTime` neu setzen.
- `InvariantGlobalization=true` schaltet die Unicode-Normalisierung stumm ab: NFC/NFKC-Prüfungen liefen ins
  Leere, Namen wie "Max" und "Ｍａｘ" kollidierten nicht. Seit 29.09.2026 aus.
- Mit echter Normalisierung wirft `IsNormalized` bei nicht normalisierbarem Text; deshalb überall über
  eine Hilfsfunktion, die ablehnt statt zu werfen.
- `vcvars64` findet das Windows SDK nicht, wenn der PATH über 8191 Zeichen lang ist; die Build-Skripte
  setzen vorher einen schlanken PATH.
- `apm_process_capture/render` haben keine Längenangabe; die C#-Hülle prüft jede Länge vor dem Aufruf.
- Raw Input gilt je Prozess und Geräteklasse nur einmal: Meldet ein zweiter Teil des Prozesses Tastatur
  oder Maus an, verstummt die Sprechtaste. DirectInput-Tastatur und -Maus melden intern ebenfalls Raw
  Input an; für Joysticks nur Spielgeräte anlegen.
- `SetDuckingPreference` wirkt nur vor dem Start des Stroms; danach dämpft Windows Star Citizen.
- Hinter dem sicheren Desktop (UAC, Sperrbildschirm) kommt kein Loslassen per Raw Input; die Sprechtaste
  prüft deshalb gedrückt alle 250 ms den physischen Zustand.
- Eine Verstärkung hinter RNNoise schnitt im Klangkorpus mehr Wortanfänge ab als dieselbe davor (25 dB: 20 statt 5 von 190):
  RNNoise erkennt schwache Anlaute schlechter, wenn die Stimme leise ankommt. Die Pegelstufe sitzt deshalb vor RNNoise, wo die AGC2
  saß, und nimmt die Sprachwahrscheinlichkeit des vorigen Rahmens. Damit fällt ein Anlaut noch in die Pause, wenn er genau auf den
  letzten Pausenrahmen trifft; ein Pausenrahmen darf deshalb höchstens 10 dB über dem leisesten der Pause liegen.
- Die Windows-Sprachausgabe macht an Kommas Pausen um 500 ms. Mit 500 ms Pausenlänge änderte die Halteregel die Verstärkung mitten im
  Satz; mit 600 ms (Obergrenze der Recherche) in keinem Satz des Klangkorpus.
- Der eigene Druck-Klick lief über den Loopback in die AEC3-Referenz, und AEC3 dämpfte einen gleichzeitigen Anlaut um bis zu 50 dB
  (auch ohne jedes Echo im Mikrofon). Die Kette nimmt ihn 120 ms lang aus der Referenz; mit einem stark verzerrenden Lautsprecher kommt
  dafür ein Rest des Klicks (bis -32 dBFS) in die ersten 100 ms der Sendung.
- RNNoise schwingt in den ersten Rahmen einer Sitzung ein und hält das Rauschen, das es dabei durchlässt, für halbe Sprache (p 0,5 bis
  0,68 in den Rahmen 8 bis 24, wenn das Rauschen verstärkt ankommt). Weil hörbare Nicht-Sprache selten ist, hob das die
  Sprachschwelle des Gates für die ganze Sitzung auf 0,8, und abgewiesene Antworten hielten sie dort. Die Selbstjustierung des
  `SprachGate` lernt deshalb erst ab 500 ms (`GateEinstellungen.EinschwingMs`).
- DeepFilterNet3 mit den Schwellen von libDF (-10 / 30 / 20 dB lokales SNR) schaltet Rechenstufen je Rahmen zu und ab und verzerrt die
  Stimme stark (SI-SDR 6 statt 33 dB, viermal so viele gedämpfte Wortanfänge). JG TacRelay rechnet jeden Rahmen voll
  (`DeepFilterSchwellen.Voll`); -15 / 35 / 35 des LADSPA-Plugins ist dasselbe, weil das Modell sein SNR auf -15 bis 35 dB begrenzt.
- Ein NaN im Eingang vergiftet DeepFilterNet3 dauerhaft; die Hülle ersetzt NaN und Unendlich vorher. Unter etwa -70 dBFS gibt libDF Nullen aus.
- Die ersten Rahmen von DeepFilterNet3 (kalte DLL, Speicher von tract, JIT) können die 10-ms-Frist reißen; der Rückfall zählt deshalb
  erst nach dem Anlauf von 50 Rahmen (sonst fiel Stark beim ersten Wählen gleich zurück, gemessen im Test ohne aufgewärmten Prozess).
- cargo liest `.cargo\config.toml` nur ab dem Arbeitsverzeichnis, nicht neben dem Manifest; Bauen ohne `cd` deshalb mit `cargo --config <datei>`.
- time 0.3.28 baut mit Rust ab 1.80 nicht (E0282); im Lockfile von deepfilternet steht 0.3.36 (nur Bauzeit).
- `Span<float>.Sort()` legt beim ersten Aufruf einen Vergleicher an (24 Byte); im Audio-Thread nicht sortieren, Quantile über Eimer.
- Windows PowerShell 5.1 liest ein .ps1 ohne BOM als ANSI; aus einem UTF-8-Umlaut in einer Zeichenkette wird dort unter anderem ein
  typografisches Anführungszeichen, und der Parser bricht ab. Texte mit Umlauten für Skripte, die nur in 5.1 laufen (WinRT,
  `klangkorpus-erzeugen.ps1`), in eine eigene UTF-8-Datei.
- Hinter RNNoise liegt der Rauschboden ruhiger Räume bei etwa -99 dBFS; Automatisch rechnet deshalb mit
  mindestens -69 dBFS Boden, sonst klebt die Linie an der Untergrenze und der Abstand-Regler wirkt nicht.
- Der Automat meldet Getrennt, bevor `SitzungBeendet` kommt; Tests warten auf das Ereignis, nicht nur auf
  den Zustand.
- `System.Threading.Lock` legt bei der ersten Konkurrenz ein Warteobjekt auf dem Heap an, auch im wartenden
  Echtzeit-Thread. Allokationsmessungen sehen das unter Last (Spiel nebenher) als 200 Byte.
- Beim Belegen meldet Raw Input den Mausklick auf den eigenen Knopf vor WPF; die linke Maustaste zählt deshalb nicht,
  solange ein Fenster des Programms vorn ist.
- Die Bitgleichheit der webrtc_apm.dll hängt am absoluten Baupfad (RTTI-Namen anonymer Namensräume).
- Der Testlauf lenkt `TACRELAY_EINSTELLUNGEN` nach `C:\Claude\tmp`; der Client schreibt sonst nach %LOCALAPPDATA%.
- `new App()` stellt `OnStartup` in die Warteschlange des Dispatchers; wer sie danach abarbeitet (Tests), bekommt den
  ganzen Programmstart samt Fenstern und Geräten. Schutz: `App.AlsProgrammGestartet`, dazu `KlangMaschine.ImProgramm`.
- `StaticResource` in `Stile.xaml` wird beim Laden aufgelöst: Eine andere Farbstellung (`Farbvorschlaege.Anwenden`) muss
  Symbole und Stile hinter der Palette neu laden, sonst bleiben alte Farben stehen. Seit 19.10.2026 gibt es nur Anthrazit; der Weg
  (`IFensterDienst.FarbenAnwenden`, `App.FarbenUmbauen`) bleibt für eine spätere Farbstellung.
- Ein Element, das in `ArrangeOverride` sein Kind neu misst (`Restbreite` in `Kachelraster.cs`), braucht eine Sperre in
  `OnChildDesiredSizeChanged`, sonst läuft das Layout im Kreis. Bei `SizeToContent` bestimmt sonst langer Text die Fensterbreite.
- Testklassen mit Bildlauf müssen im Namen nach `OberflaecheFensterTests` einsortiert sein, sonst gehört `Application.Current`
  einem fremden, beendeten Thread. "OberflaecheFeinheiten..." sortiert davor ("Fei" vor "Fen"); die Klassen des Umbaus 0.10.0 heißen
  deshalb `OberflaecheUmbau...`.
- Ein Wandler oder eine Ressource, die `Hauptfenster.xaml` oder `Bereiche.xaml` per Schlüssel braucht, gehört in `App.xaml` und in
  `Bildlauf.RessourcenSetzen` (`OberflaecheGlasTests.Glas_AppUndBildlaufLadenDasselbe`). Layoutwechsel nach der Breite lieber in einem
  Panel (`Spaltenwechsel`) oder im Code des Fensters (`Hauptfenster.TitelleisteSetzen`).
- Im Bildlauf der Tests ordnet nur der Inhalt des Fensters an, das Fenster selbst bleibt ohne Größe und meldet kein `SizeChanged`;
  was sich nach der Fensterbreite richtet, hört deshalb auf `SizeChanged` des Inhalts (`Wurzelraster`). Bindungen greifen erst nach dem
  ersten Zeichnen: Wer einen Wert zur Probe mit `SetCurrentValue` setzt, zeichnet vorher einmal, sonst überschreibt ihn die Bindung.
- `JsonStringEnumConverter` in den Optionen schlägt einen `[JsonConverter]` am Enum-Typ; nachsichtige Wandler gehören an die Eigenschaft.
- Ohne Fensterhandle kommt `StateChanged` nicht, `IsVisible` bleibt falsch und ein nie gezeigtes Fenster steht auf Collapsed; Bildläufe
  prüfen deshalb die `Visibility` der Vorfahren.
- Persistierte CNG-Schlüssel mit ExportPolicy None lassen sich nicht als PKCS#12 exportieren; der Import-Umweg gilt nur für flüchtige
  Schlüssel (`Geraeteidentitaet.WegFuer`).
- Der Server schickt in `RegistrierungAntwort.befehl` nur `/funk verknuepfen`, ohne Code; der Client zeigt den Code selbst.
- Das Edit-Werkzeug hat `\u`-Escapes in C#-Zeichenliteralen zu echten Zeichen gemacht; nach solchen Änderungen `tools\text-pruefen.ps1`.
- `cmd /c start /wait /b` gibt den Exitcode nie weiter (immer 0). `tests\testlauf.ps1` schreibt deshalb Summenzeile und Code nach
  `C:\Claude\tmp\testlauf-ergebnis.txt` (oder `-Ergebnisdatei`); parallele Arbeitskopien brauchen je eine eigene Datei.
- Ein Regler im Funkfeld darf beim Ziehen die Schichtung (AllowsTransparency) nicht wechseln, sonst verschwindet er mit dem neu
  geöffneten Fenster; der Wechsel über 0 geschieht erst bei `DragCompleted`.
- `0,55 * 100` ist in double 55,000000000000007; `Math.Ceiling` macht daraus 56. Vorher runden.
- Ein `DispatcherTimer` mit 33 ms tickt ohne angehobene Zeitgeberauflösung etwa alle 47 ms, `Environment.TickCount64` zählt in
  15,6-ms-Schritten; Taktberechnungen auf genaue 33 ms stimmen im Betrieb nicht.
- Text in einer waagerechten `StackPanel` bricht nicht um und wird abgeschnitten; `DockPanel` mit Umbruch nehmen.
- Der Bereich der Sprachaktivierung steht seit 0.10.0 verborgen im Fenster (Platz `SchwellenSeitenPlatz`) und zählt mit seinen
  Zugriffstasten (Alt+M, Alt+L) auf jeder Seite mit; sie müssen überall frei bleiben (`OberflaecheHauptfensterTests` findet Doppelungen,
  auch in zugeklappten Teilen). Seine Hörprobe hatte deshalb schon in AP2 keine Taste mehr (Alt+B gehört auf Start "Einheit
  (Belegung)"); seit AP3 steht dort gar keine Hörprobe mehr.
- Will der Inhalt eines Klappbereichs die volle Breite (etwa ein `Spaltenwechsel`), rundet WPF bei 175 % die gewünschte Größe des
  Rahmens über das Angebot hinaus, und der Rahmen schneidet danach an seinem Rand ab. Glasknöpfe ganz am Rand brauchen dort Luft für
  ihren Schatten (unten 8 im `FeinheitenFuss`, rechts 8 in den Klappbereichen); gemessen mit `OberflaecheStartseiteTests.SchattenFrei`.
  Dasselbe trifft den ganzen Abschnitt: Unter Senden (0.11.1) schnitt `AbschnittSenden` bei offenem Erweitert nach dem Runden um
  0,6 in der Höhe ab, und sein Rand kappte links die Schatten von "Automatisch" und "Genau einstellen ..." (5 und 6,5 Einheiten);
  deshalb steht `SendenSpalten` links und rechts 8 eingerückt. Offen geprüft wird das in `OberflaecheUmbauErweitertTests`.
- Eine Grenze, die genau auf einer Fenstergröße ab Werk liegt, braucht Spiel für die Rundung des Layouts: In Klein 900 x 900 maß der
  Inhalt im Bildlauf 897,7 statt 898, und die Titelleiste stand kurz, obwohl erst unter 900 gekürzt werden soll (gefunden in AP4 beim
  Vergleich mit der Vorlage m-7c; `Fenstergroessen.RundungsSpiel`, seit Q93 a auch für die Grenze 1240 der Schublade).
- `VisualTreeHelper.HitTest(visual, punkt)` ohne Fensterhandle trifft auch Verborgenes (die Beendenfrage über allem, gefunden bei Q93 a);
  wer prüfen will, was die Maus träfe, filtert Unsichtbares und `IsHitTestVisible = false` selbst (`OberflaecheWerIstDaPlatzTests.Treffer`).
- Die Schublade "Wer ist da" folgt der gemessenen Breite (`Wurzelraster.SizeChanged`); im Bildlauf ordnet erst `Bildlauf.Zeichnen` an.
  Ein Test, der nur `Width` setzt und gleich danach die Spalte öffnet, sieht noch die alte Breite; vorher zeichnen, wie im echten Fenster
  WPF nach dem Ziehen anordnet.
- Jede Zuweisung an `Left`, `Top`, `Width` oder `Height` eines gezeigten Fensters ist in WPF ein eigenes SetWindowPos (erst die Lage mit der
  alten Größe, dann die Größe); wer ohne Zwischenstand verschieben und zugleich vergrößern will, nimmt `Platzfuehrung.Setzen(platz,
  einSchritt: true)`. Maximiert oder minimiert schreibt WPF dieselben Zuweisungen mit SetWindowPlacement in die Wiederherstellungsgrenzen,
  zu sehen ist nichts (Q93 a, Befunde der Prüfung). Im Bildlauf steht `Width` zugleich für den Normalzustand: Zeichnet ein Test das
  maximierte Fenster in einer anderen Größe, setzt er `Width` danach zurück.
- Der Monitor eines Fensters, das über eine Grenze ragt, ist der mit dem größten Stück (`Platzierung.Ueberwiegend`, wie MonitorFromWindow und
  wie Windows die Skala wählt), nicht der unter der Ecke (`Platzierung.Am`); mit der Ecke sprang das verbreiterte Fenster auf den
  Nachbarmonitor (Q93 a, Befund der Prüfung).
- Die Tests dürfen kein Fensterhandle anlegen (`SicherungTests`, Bildlauf): Ob ein Setzen am echten Fenster ein SetWindowPos ist und nichts
  aktiviert, prüft ein Wächter über den Quelltext (`OberflaecheWerIstDaPlatzTests.WerIstDaPlatz_KeinAktivieren_EinSetWindowPos_Quelltext`).
- Der erste Start wartet ohne Frist auf eine Taste (Raw Input sieht alle Tasten, auch in anderen Programmen): nur, solange Start vorn und
  das Hauptfenster aktiv ist (`HauptViewModel.ErwartenAbgleichen`), sonst endet das Warten still (`TastenViewModel.WartenBeenden`). Linke,
  rechte und mittlere Maustaste im eigenen Fenster werden nie zur Taste (`SprechtastenZustand.BedientOberflaeche`).
- `HintergrundFehler` kommt auch aus dem Empfangs-Thread: dort nie bauen und nie sperren, nur vormerken.
- Zeitfristen in Tests müssen niedrige Priorität neben einem Spiel aushalten (Sekunden, nicht Hunderte Millisekunden).
- Ein Panel darf nie das Angebot als Wunschgröße melden (Kachelraster bis 30.09.2026): Ein Fenster mit `SizeToContent` bietet die
  größte Nachführgröße an, bei zwei 4K-Monitoren 7708 x 2188 Bildpunkte, und das Fenster wurde so groß. Bildtests, die nur die
  Vorlagenwurzel mit Unendlich messen, sehen das nicht; `OberflaecheFunkfeldGroesseTests` misst über den Weg des Fensters.
- Das Funkfeld nutzt kein `SizeToContent` mehr: optimale Größe einmal rechnen (`Platzierung.Optimal`), auf den Monitor begrenzen.
- Der Messschalter der App (`FunkfeldMessung`) muss vor der Einzelinstanz abzweigen, sonst bittet eine Messung ein laufendes
  JG TacRelay nach vorn. Git führte beide Stellen ohne Konflikt in falscher Reihenfolge zusammen.
- Jeder Push, auch jede Sprechanzeige, baut die FunkLage neu und löst `LageGeaendert` aus. Listen im Oberflächentakt ("Wer ist
  da", Lieblingsrollen in "Ich") nur bei geändertem Inhalt ersetzen, sonst gehen Fokus, Klicks und ungespeicherte Häkchen verloren.
- Ein wartendes Gerät an einem bestehenden Konto sieht in der Antragsliste aus wie ein neuer Antrag; ohne Konto, BESTAETIGT oder
  EINSPRUCH daneben wäre das der Übernahmeweg über ein gekapertes Discord-Konto. Die Ansicht zeigt deshalb Konto und Markierung.
- Windows-`tar` trägt für jede Datei 0666 und für Ordner 0777 ins tgz; `tar` als root übernimmt das. Nach jedem Auspacken nach
  `/opt/funk` gehört `chmod -R go-w` dazu (HEIMSERVER-PROXMOX.md, Abschnitte 5 und 13).
- Der ClientKern abonniert `NetworkChange.NetworkAddressChanged`; unter Linux braucht das einen Netlink-Socket. Eine Unit mit
  `RestrictAddressFamilies` ohne `AF_NETLINK` bricht den Sprachweg des Echo-Funkers ab.
- `tools\dev-umgebung.ps1` wird mit Punkt eingebunden; jede Variable darin landet beim Aufrufer. Ein `$p` dort hat am 30.09.2026
  das `$p` eines Prüfskripts überschrieben.
- Bis 0.1.0 meldete jeder Client in Hello fest "0.1.0" (`FunkClientOptionen.ClientVersion`); `mindestClientVersion` hätte sich nie
  anheben lassen, ohne die neuen Clients mit auszusperren. Seit 0.2.0 kommt die Fassung aus Directory.Build.props.
- Eine server.json aus 0.1.0 hat kein `totpSchluessel`. Mit dem Standardpfad startete 0.2.0 nicht und sagte nur "Datei fehlt";
  jetzt nennt schon `pruefen` die drei Schritte. Beim Update auf 0.2.0 gehören Schlüssel, Feld und Unit zusammen.
- Die Namensräume der SC- und Discord-Namen stehen nicht in konten.json; `Kontenstand.MitScNamenIndex` baut beide beim Laden. Wer
  an neuer Stelle einen `DiscordName` setzt, pflegt `NachDiscordName` mit (`MitDiscordName`), sonst ist der Name nach dem nächsten
  Neustart plötzlich vergeben oder frei.
- Das Edit-Werkzeug hat am 01.10.2026 wieder ein `\u200B` in einem Testtext zu einem echten Zeichen gemacht; `tools\text-pruefen.ps1`
  fand es. PowerShell-`WriteAllLines` mit `UTF8Encoding($true)` setzt eine BOM, die vorher nicht da war. Testzeichen deshalb als
  Zahl schreiben (`char.ConvertFromUtf32(0x3164)`), nicht als Escape.
- Unsichtbare Zeichen stehen nicht nur in der Klasse Cf. Default_Ignorable_Code_Point gibt es auch als Mn (U+034F, Variantenwähler
  U+FE00-FE0F) und als Lo (Hangul-Füller U+3164, gelten als Buchstaben, also auch im SC-Namen). Bis 01.10.2026 kamen sie durch, und
  "Fritz/DC" mit Füller hatte einen eigenen Namensschlüssel. `Zeichenregeln.IstUnsichtbar` sperrt sie. Beim Nachmessen in PowerShell
  nie mit `-eq` vergleichen: Das vergleicht kulturabhängig und überspringt genau diese Zeichen; `[string]::Equals(a, b, 'Ordinal')`.
- `ToUpperInvariant` lässt ß und das große ẞ (U+1E9E) stehen; `Namensschluessel` macht ẞ zu ß (die einzige Groß/Klein-Lücke bis
  U+1FFFF). Der Anzeigenamen-Index steht mit den Schlüsseln von damals in konten.json (`namen`), `Kontenstand.MitScNamenIndex` zieht
  ihn beim Laden nach. Wer die Schlüsselrechnung ändert: gespeicherte Schlüssel nachziehen und kein `ToDictionary` über
  Namensschlüssel, denn Dubletten werfen beim Laden.
- Die Gegenprüfung vom 01.10.2026 schrieb die beiden Fallen darüber mit `\u00FC`-Escapes statt Umlauten in diese Datei;
  `tools\text-pruefen.ps1` sieht so etwas nicht, weil es reine ASCII-Zeichen sind. Nach Änderungen an Markdown mit
  `Select-String -Pattern '\\u[0-9A-Fa-f]{4}'` nachsehen (in C#-Quelltext sind die Escapes dagegen gewollt). Beim Berichtigen
  schrieb das Edit-Werkzeug auch den neuen Text wieder mit Escapes, weil der alte welche enthielt; umgekehrt machte es in
  PROJEKT.md aus einem gewollten `\u00FC` im neuen Text ein echtes Zeichen. Solche Stellen mit einem kleinen PowerShell-Skript
  ersetzen (`[regex]` mit Auswerter, Zeichen als Zahl), danach `git diff` ansehen.
- Anleitung und Code parallel gebaut (01.10.2026, modul/anleitung neben modul/nacht): Die Anleitung beschrieb `pruefen`, die
  gemeldete Client-Fassung, die Ausgabe von `konten` und die Befehle der echo-probe nach dem alten Stand, obwohl beide Zweige ohne
  Dateikonflikt zusammengingen. Nach so einem Merge jede zitierte Ausgabe gegen den Code prüfen; Git merkt davon nichts.
- funk-admin nahm bis 01.10.2026 jede Option still an und schickte eine fehlende Liste als leere: `scnamen --name "..."` (statt
  `--namen`) löschte alle SC-Namen des Kontos, mit Exit 0. Jetzt prüft `Befehlsoptionen.Pruefen` vor allem anderen (unbekannt,
  doppelt, Wert ohne Option, `--x=wert`, Option ohne Wert), und eine Liste leeren geht nur mit `--leeren` (funk-admin und Server).
  Eine neue Option gehört in die Tabelle in `src/Funk.Admin/Befehlsoptionen.cs`, sonst lehnt funk-admin sie ab;
  `AdminWerkzeugTabelleTests` gleicht Tabelle, Code, Hilfetext und Anleitungen ab, auch zitierte Ablehnungen.
- Das Setup fragte bis 01.10.2026 nach `einstellungen.json`; der Client schreibt seit 0.1.0 `client-einstellungen.json`, und die
  Seite "Deine Namen" kam trotz vorhandener Einstellungen. Der Test schrieb denselben falschen Namen als Text fest und prüfte so nur
  sich selbst. Jetzt steht der Name als `EinstellungsSpeicher.Dateiname`, und SetupTests nimmt ihn von dort.
- Seit 0.3.0 sind Rechte zeitabhängig: `Konto.Rechte` (Lage) sind nur die dauerhaften. Wer nach Rechten entscheidet (einteilen,
  senden, Pushes verteilen), nimmt `Konto.RechteZu(jetzt)`, im Schreiber `Arbeitsstand.Wirksam`/`RechteVon`, im Schnappschuss
  `RechteAn`/`SitzungenMitRecht` mit der Zeit. Ein vergessenes `.Rechte` lässt einen Beauftragten ohne Recht stehen oder einen
  abgelaufenen Auftrag weiter wirken, ohne dass ein Test der Stufe 1 fällt. Ausnahme mit Absicht: Regel 1 aus 6.10 und das
  Erteilen von Aufträgen prüfen die dauerhaften Rechte.
- Ein neuer Steuertyp verändert die Zählung in `SteuerAutomatTests.P10_JedeNachrichtInJedemZustand` (Typen mal Zustände mal Arten)
  und die erlaubte Menge je Zustand; `SteuerKatalogTests` zählt die Einträge. Beide gehören zum Anlegen dazu.
- In PowerShell ist `R` ein Alias (Invoke-History): Eine eigene Funktion `R` für Ersetzungen lief am 01.10.2026 ins Leere, ohne
  Fehler, nichts war geändert. Ebenso findet `$text.Replace` mit einem mehrzeiligen Here-String in Dateien mit CRLF nichts.
  Danach immer `git diff` ansehen.
- Testserver.DisposeAsync löscht das Verzeichnis. Ein Erzeuger für Testdaten (`Erzeuge020.cs.txt`) beendet deshalb nur den Dienst
  (`DienstBeendenAsync`) und lässt das Verzeichnis stehen.
- C#-Mustervariablen aus `is not X n` in verschiedenen `case`-Abschnitten derselben `switch` teilen sich den Gültigkeitsbereich
  (CS0128); jedem Abschnitt einen eigenen Namen geben.
- Das Brett steht nicht im Bildlauf der übrigen Bereiche, sondern auf eigenem Platz (`HauptViewModel.BrettInhalt`); sonst entstünde
  über die Vorlage eine zweite, verborgene BrettAnsicht mit eigenen Ablagezielen. Bildläufe, die den Bildlauf des Hauptfensters an
  "StackPanel mit ContentControl" erkennen, treffen sonst die linke Liste des Bretts (ein ToggleButton ist auch ein ContentControl);
  sicher ist die `ErststartKarte`.
- Der Server gibt Einheiten anders zurück, als man sie schickt: Kennungen sortiert, Art Schiff ausgeschrieben, Mindestbesetzung 0
  als leer. Wer NetzListe-Objekte direkt vergleicht, sieht Änderungen, die keine sind; `Flottenentwurf` bringt alles beim Setzen in
  die Form des Servers.
- Eine neue NetzListe während eines Flottenentwurfs (ein anderer Admin) darf den Entwurf nicht einfach ersetzen und nicht blind
  zurückschreiben: `Flottenentwurf.Umsetzen` behält je Objekt die eigenen Änderungen und übernimmt den Rest vom Server.
- In WPF-Projekten fehlt `System.IO` unter den impliziten Usings (wegen `System.Windows.Shapes.Path`); im Verwaltungsteil
  `using System.IO;` setzen.
- Ein Oberflächenmodell über einem echten `FunkClientDienst` im Test braucht einen eigenen Thread mit SynchronizationContext
  (`EinFaden` in `RaidleitungClientTests`); sonst laufen Takt und alles nach await auf verschiedenen Threads in denselben Listen.
- Feste Farbwerte der Farbstellung sind in der Oberfläche verboten (`OberflaecheFarbvorschlagTests.Farbvorschlag_KeinFesterFarbwertDerFarbstellungInDerOberflaeche`),
  auch für Ampel und Ablageziel; die Rollen nehmen (`Pinsel.RotSchrift`, `Pinsel.Akzent`, `Pinsel.Leitfarbe`).
- Die Nachzügler-Ausnahme der Wechselsperre hängt an "ohne Platz". Jeder Weg, auf dem jemand seinen Platz selbst aufgibt, ist deshalb
  bei wirkender Sperre selbst ein Wechsel; bis zur Gegenprüfung vom 01.10.2026 gab `SatzAnwenden` mit `rolleId: 0` den Platz frei,
  und danach stieg man als Nachzügler woanders ein. Ein neuer Selbstweg zum Platz gehört an `SelbstwahlSperreWirkt`.
- Plätze verfallen ohne Commit (Fristen in `Schnappschuss.PlatzZustandBerechnen`). Praesenz geht sonst nur nach Commits hinaus; das
  Brett zeigte eine Person, die vor 10 Minuten gegangen war, weiter im Block und lehnte Ablagen dort als "voll" ab. Der 10-s-Takt
  meldet solche Konten (`PlatzfristenMelden`). Wer eine neue Frist einführt, prüft, ob ihr Ablauf irgendwo ankommt.
- Ein Server trennt bei einem Pflichttyp (unter 0x4000), den er nicht kennt, mit PROTOKOLL. Ein neuer Client am alten Server schickte
  0x0038 schon beim ersten Zeigen des Bretts. Neue Anfragen im Pflichtbereich bekommen ihre Fassung in `Versionen.ServerFassungFuer`;
  `FunkClient.AnfrageAsync` schickt sie einem älteren Server nicht.
- Rennen Antrag/WARTERAUM (bis 0.2.0): Die Zentrale schob GeraetStatus{FREIGEGEBEN} direkt in die Warteschlange der wartenden
  Verbindung. Gab ein automatischer Admin-Client binnen Millisekunden frei, kam FREIGEGEBEN vor der Antwort auf den Antrag und
  vor GeraetStatus{WARTET}. Jetzt läuft beides über `Verbindung.FreigabeAnwenden(status)`, also unter der Sperre der Verbindung,
  die sie während der ganzen Antragsbearbeitung hält. `Verbindung.AntragZeitlupeMs` (nur Tests) dehnt das Fenster, sonst ist es
  nicht zu treffen.
- `Task.Run(() => j.SpeichernImHintergrund())` nimmt die Überladung für `Func<Task>` und wartet auf die innere Task; ein Test, der
  prüfen wollte, dass der Aufruf sofort zurückkehrt, hing so scheinbar. Für "Aufruf kehrt zurück" `Task.Run(() => { _ = ...; })`.
- CommunityToolkit.Mvvm erzeugt für eine Eigenschaft vom Typ `Bereich` den Haken `OnAktiverBereichChanged(Bereich oldValue,
  Bereich newValue)` ohne `?`; mit `Bereich?` meldet der Übersetzer CS8826.
- `LibraryImport` (adjtimex) braucht `AllowUnsafeBlocks` im Projekt (SYSLIB1062), auch wenn selbst kein unsicherer Code dasteht.
- `ProtectClock=true` in der Unit sperrt den Systemaufruf `adjtimex` über den Filter `@clock` auch zum Lesen (laut
  systemd-Dokumentation; am Heimserver nicht gemessen). Die Uhrprüfung im Dienst ist deshalb nur Plausibilität; ob der Host
  synchronisiert, zeigt `funk-server pruefen` in der Shell.
- `konto-loeschen` löschte an der Konsole bis 0.2.0 auch den Eigentümer, obwohl `stufe` ihn schützte; der Server stand dann ohne
  Eigentümer da. Gefunden beim Test des Reparaturwegs B (Antrag ins falsche Konto), jetzt abgelehnt.
- Die Sperrliste zählt nur, was vor TLS oder vor der Anmeldung passiert, und nie für Präfixe der Vorrangspur. Wer einen neuen
  Anlass einbaut: ein freigegebenes Gerät darf nie Punkte bekommen (`bekannt` in `TlsAnnahme.NachHandshakeAsync`), und
  globale Grenzen (etwa `MaxKonten`) sind kein Verstoß einer Adresse.
- Gegenprüfung 0.3.0: Genau diese Falle schnappte schon. `EinmalcodeRegister` antwortet bei voller Präfixtabelle (4096) jedem
  neuen Präfix `GRENZE` mit Alarm `REGISTRIERUNG_FLUT`, und die Zentrale vermerkte das als Antrags- bzw. Registrierungsflut der
  Adresse: Eine Flut aus vielen Präfixen sperrte so neue Mitglieder mit aus. Jetzt entscheidet
  `EinmalcodeRegister.GrenzeDerQuelle`; wer ein neues `GRENZE` einbaut, prüft, ob es an der Quelle liegt.
- Die Sammelsperre /48 zählte nur gesperrte /64. Wer je Verbindung ein neues /64 nimmt, bringt keines an die Schwelle und kam
  nie auf die Liste. Jetzt sammelt auch das /48 Punkte (`Sperrliste.Gruppenpunkte`, Schwelle mal `v6Sammelschwelle`).
- Die Sperrliste rechnet mit der Wanduhr (`Zeit.UnixMs`, wegen `sperrliste.json`), die Zulassung mit der monotonen. Nach einem
  Rücksprung der Uhr gab es bis zum Einholen keinen Durchlass; `Sperrliste.Uhrsprung` gleicht das bei jedem Zugriff aus.
- fail2ban: `iptables-allports` sperrt nur `protocol`, und das ist ohne Angabe `tcp`. Im Jail steht deshalb `protocol = all`.
- Die Paketwerkzeuge kennen ihre konfig-Dateien und Anleitungen als feste Listen (`tools/paket-bauen.ps1`,
  `tools/serverpaket-bauen.ps1`, `$konfigDateien`, `$anleitungen`); eine neue Datei gehört in beide, sonst fehlt sie im Paket
  oder das Serverpaket bricht ab.
- Zusammenführung 0.3.0 (01.10.2026): Härtung und Raidleitung schoben den Abschnitt "Von 0.2.0 auf 0.3.0" einander zu, und
  keiner schrieb ihn. Dazu stand in BETRIEB.md weiter "`ServerLast` geht an Raidleiter und Admins", obwohl RAIDLEITER kein
  `ZUWEISEN` mehr hat. Wer Rechte einer Stufe ändert, sucht in `docs/` nach dem Namen der Stufe; Git und die Tests merken
  davon nichts.
- `AdminWerkzeugTabelleTests` prüfte bis 01.10.2026 nur HEIMSERVER-PROXMOX.md, BETRIEB.md und die LIESMICH-Vorlagen; die
  Befehle in `docs/HEIMSERVER-HAERTUNG.md` (`entsperren --ip`) liefen ungeprüft (Mutationsprobe `--adresse`: grün). Eine
  neue Anleitung mit funk-admin-Befehlen gehört in die Liste in `AdminWerkzeug_AnleitungenNutzenNurBekannteOptionen`.
- PowerShell-`-match` vergleicht ohne Groß/Klein: Beim Nachmessen von `lage.json` fand `-match 'TRUPP'` den Namen
  "Trupp Alpha" und meldete die Art als vorhanden. Für Feldnamen und Werte in JSON `-cmatch` nehmen.
- Im Kreuzlauf holt `funk-echo` mit `--testzugang` ohne Schlüsseldatei bei jedem Start einen neuen Geräteschlüssel und muss
  neu verknüpft werden. Ein Warten auf "Bereit im Netz 'Echo'|Registrierungscode" sah das als bereit, und die Echo-Probe
  danach scheiterte mit "kein Echo", ohne dass am Server etwas falsch war.
- `konto-loeschen` fragt an der Konsole nicht nach und schützt nur den Eigentümer. Weg A stand bis 01.10.2026 mit dem
  Löschen vor `eigentuemer`; ein vertauschtes `--konto` hätte Sarevocs ADMIN-Konto samt dem gerade umgehängten Gerät gelöscht
  (der Test setzte den Eigentümer vorab und sah das nicht). Wo eine Anleitung löscht, kommt vorher der Schritt, der das
  Falsche schützt; `HaertungTests.Anleitung_ReparaturwegA_WieGeschriebenUndVertauscht` lässt den Block laufen und vertauscht.
- Eine Zeile, die Sarevoc in server.json einfügt, braucht den genauen Ort: `AllowTrailingCommas` ist aus, "mit Komma davor"
  plus Komma am Ende ergibt Exit 2. Und nie `pruefen` und `systemctl restart` als zwei Zeilen untereinander, sondern
  `pruefen && systemctl restart funk-server` (der Test sucht solche Paare in docs).
- fail2ban startet mit einem eingeschalteten Jail ohne Logdatei gar nicht, mit allen Jails (laut fail2ban, am Heimserver
  nicht gemessen). Deshalb legt funk-server `sperrliste.klartextDatei` beim Start an (`SperrlisteEinrichten`), und die
  Härtung startet fail2ban nach `pve-guests.service`. Ein Jail am Host hat keinen Durchlass: Sperren dort nie länger als
  die erste Sperre des Servers (`SperrlisteTests.Sperrliste_Fail2banJail_*`).
- Der Probefunk legt Konten als ADMIN an (`konfig/probefunk.json`, `freigabe.stufe`), die Voreinstellung in
  `Probekonfiguration` ist dagegen RAIDLEITER. Im Probefunk erscheint deshalb das Brett (Bereich Raid); ein Testserver ohne
  diese Datei zeigt es nicht, weil RAIDLEITER seit 0.3.0 nicht einteilt.
- Nicht nur das Edit-, auch das Write-Werkzeug macht Escapes mit Backslash-u zu echten Zeichen (01.10.2026, Paket Umzug): Aus dem
  Vergleich auf die Byte-Reihenfolge-Marke als UTF-8-Literal wurde ein unsichtbares Zeichen im Quelltext, aus Testzeichen echte
  Gedankenstriche. Bytes als Zahlen schreiben (`[0xEF, 0xBB, 0xBF]`), Zeichen als `(char)0x2013`. Beim Nachsehen in PowerShell
  `IndexOf([char]0xFEFF)` nehmen: `IndexOf(string)` vergleicht kulturabhängig und meldet ein unsichtbares Zeichen an Stelle 0.
- `X509Certificate2.CreateFromPemFile(crt)` ohne Schlüsselpfad sucht den Schlüssel in derselben Datei und wirft bei einem reinen
  Zertifikat ("key contents do not contain a PEM"). Nur das Zertifikat: `X509Certificate2.CreateFromPem(File.ReadAllText(crt))`.
- funk-admin-Befehle, die im Werkzeug laufen (Program.cs, Abschnitt offline), dürfen `--konfig` und `--socket` nicht im `case`
  lesen, sonst hält `AdminWerkzeugTabelleTests` sie für eigene Optionen des Befehls. Dafür stehen `KonfigPfad`, `KonfigLaden`,
  `KonfigOderNull` und `SocketPfad` am Ende von Program.cs. Ebenso keine Methode namens `Text(` oder `Zahl(` mit einem
  Zeichenkettenliteral im `case` aufrufen: Das Muster des Tests liest sie als Option.
- Zwei Testhelfer `Werkzeug(string eingabe, params string[])` und `Werkzeug(params string[])` nebeneinander: Bei lauter
  Zeichenketten nimmt C# die Überladung mit Eingabe, und der Befehl wurde zur Tastatureingabe ("Unbekannter Befehl:
  --zertifikat"). Deshalb heißt die mit Tastatur `AdminEinrichtungTests.MitEingabe`.
- `PinWaechter.PinGueltig` leert auch die Liste der gemiedenen Adressen: Nach einem Erfolg an Host B versucht der nächste
  Wiederverbindungsversuch auch die Adresse mit dem fremden Pin wieder (kostet einen Handshake, der Pin schützt).
- Unter Windows steht jede Textdatei der Arbeitskopie mit CRLF da (`core.autocrlf=true`). Ein Test, der eine solche Datei
  liest und `\n` zu `\r\n` macht, erzeugt `\r\r\n` (EinrichtungTests, 01.10.2026). Erst auf LF bringen, dann wandeln. Aus
  demselben Grund schreibt `paket-bauen.ps1` das Skript `funk-einrichten.sh` ausdrücklich mit LF: Es kopiert aus der
  Arbeitskopie, und bash liest ein CR als Teil des Befehls.
- Ein Profil kennt nur einen TCP-Port für alle Hosts. Der Entwurf schlug für den alten Server 443 vor, Sarevocs Server hört auf 8443:
  Mit dem Übergangsprofil hätten die Clients einen der beiden nie erreicht. Im Übergang haben beide Server denselben
  TCP-Port (UMZUG.md); `einrichten` sagt das bei der Portfrage. Gestoppt wurde ein anderer Port bis zur Prüfung der Pakete
  0.4.0 aber nirgends: `profil-erneuern` übernahm ihn aus der Anfragezeile. Jetzt lehnt es ihn ab, solange ein anderer Host
  im Profil bleibt, und `profil-neu-laden` warnt wie der Start (`Profilabgleich.PortWarnung`).
- Die Clients versuchen die Hosts des Profils der Reihe nach, um 250 ms versetzt, und der erste TCP-Erfolg gewinnt
  (`TlsAufbau.TcpAsync`). Läuft im Vorlauf eines Umzugs der neue Server leer mit, landet ein Client dort, sobald der alte
  einmal langsam ist, und stellt einen Antrag. UMZUG.md hält ihn deshalb bis zum Umzugsabend an; der alte Server wird am
  Umzugsabend mit `disable` abgeschaltet, weil er im Übergangsprofil vorn steht.
- (Vorbeugend, nicht zugeschnappt.) `systemctl start` meldet bei Type=simple Erfolg, auch wenn der Dienst gleich danach mit 243/CREDENTIALS oder 226/NAMESPACE
  fällt. `einrichten` liest deshalb Journal und `systemctl show` (ActiveState, Result, ExecMainStatus, NRestarts), bis
  "bereit" oder ein Fehler dasteht.
- (Vorbeugend, nicht zugeschnappt.) Mit `set -o pipefail` scheitert `programm | grep -q` zufällig, wenn grep nach dem ersten Treffer endet und das Programm
  SIGPIPE bekommt. In `funk-einrichten.sh` deshalb nie `| grep -q` oder `| head`; `EinrichtenSkriptTests` sucht danach.
- Welcome kommt im Client als Push (Sitzung.Annehmen, dann `PushErhalten`), nicht als Antwort auf Hello; dort nimmt
  `FunkClientDienst` das Profil an. `Welcome{WARTET}` trägt kein Profil.
- Gegenprüfung Umzug (01.10.2026): root schreibt bei `umzug-einspielen` nach /var/lib/funk, das dem Dienstbenutzer funk gehört.
  `Dateiablage` öffnete die Zwischendatei `<name>.neu` mit FileMode.Create, und `chown` folgte Verweisen: Ein dort
  untergeschobener Verweis `konten.json.neu -> /etc/shadow` hätte root hindurchschreiben und die Datei danach funk übergeben
  lassen. Jetzt löscht `Dateiablage` eine vorhandene Zwischendatei und legt sie mit CreateNew an (dann gilt auch 0600 wieder;
  UnixCreateMode greift nur bei neuen Dateien), und `Systemblick.BesitzerSetzen` ruft `chown -h`. Wer als root in einen Ordner
  eines anderen Benutzers schreibt: nie durch vorhandene Namen hindurch. Unter Windows zeigt ein harter Verweis dasselbe
  (symbolische brauchen Adminrechte); `UmzugTests` legt ihn über CreateHardLinkW an.
- `funk-einrichten.sh` verglich (`diff -rq`) und kopierte (`cp -R`) den ganzen Ordner linux. Wer dort nach EINRICHTEN.md
  profil.json oder nach UMZUG.md umzug.tru ablegt, bekam bei laufendem Dienst "Für die Programme kurz anhalten?" (die Eingabetaste
  heißt ja), und umzug.tru landete in /opt/funk. Jetzt zählt nur die Liste in `programmdateien` (Muster). Kommt eine neue Dateiart ins
  Paket (etwa `runtimes/`), gehört ihr Muster dorthin; `EinrichtenSkriptTests` prüft die Liste gegen die Dateien des Pakets.
- Eine Datei mit `Copy-Item` zurückgespielt behält ihre alte Änderungszeit; der inkrementelle Bau hält das Projekt dann für
  aktuell und testet alten Code (Gegenprüfung 01.10.2026: Grünlauf mit den Binärdateien des Rotlaufs). Nach dem Zurückspielen
  `(Get-Item $d).LastWriteTime = Get-Date`, dann bauen.
- Ein Profil ohne den Pin des Servers, der es durchreicht, sperrte jeden Client aus, der es übernahm (etwa Version 3 eines
  Umzugs aus Versehen auf dem alten Server). Deshalb prüft der Client: `TlsAufbau.LetzterPin` (aus `Steuerverbindung.Pin`)
  über `FunkAnbindung.ServerPin` an `Profiluebernahme.Annehmen(angebot, pin)`. Seit 0.4.0 warnt auch der Server, beim Start
  und bei `profil-neu-laden` (`Zentrale.ProfilPinWarnung`, bis dahin nur beim Start und ohne Version), lädt das Profil
  aber trotzdem, wie der Start es immer tat.
- Zusammenführung 0.4.0 (modul/umzug auf 0.3.0): `programmdateien` in `funk-einrichten.sh` nahm `konfig/*`, und das traf
  den Unterordner `konfig/fail2ban` aus 0.3.0 nicht; `/opt/funk` hätte nach `einrichten` ohne die Beispiele dagestanden, nach
  dem Weg von Hand (tar) mit. Beide Zweige waren für sich grün, die Liste im Test stammte aus einem Paket von 0.2.0. Wer eine
  Datei in einem Unterordner ins Paket legt, prüft `programmdateien`, die Liste in `EinrichtenSkriptTests` und beide
  Paketwerkzeuge.
- Zusammenführung 0.4.0: Die Anleitungen beschrieben je das Verhalten des anderen Zweigs falsch, ohne Konflikt. HEIMSERVER
  Abschnitt 6 sagte "ein zweiter Aufruf ersetzt den Schlüssel" (seit modul/umzug Exit 3 ohne `--ersetzen`), Abschnitte 11
  und 13 setzten `--adresse` mit nano in die Unit (seit modul/umzug `echo.env`), UMZUG.md hatte `pruefen` und `systemctl
  restart` untereinander (der Test sah UMZUG.md nicht an), `umzug-packen` nannte `sperrliste.json` nicht. Nach jedem Merge
  die Anleitungen beider Seiten gegen den Code des anderen lesen.
- Der Dienst lud das Profil zweimal (Konstruktor der Zentrale und `Dienst.Aufbauen`), und das Journal nannte es doppelt
  (Übergangsmessung 0.4.0). Jetzt hält die Zentrale den Fehler des ersten Ladens (`ProfilFehlerBeimStart`).
- Die Auswahl (ComboBox, Vorlage in `Stile.xaml`) zeigt ihren Inhalt links mit 32 Bildpunkten Rand rechts; zu langer Text wird
  ohne Auslassungspunkte abgeschnitten, und `Sichtbar` im Bildtest merkt das nicht (TextBlock-Breite bleibt die volle). In
  der Flotte bricht die Auswahl je Feld jetzt um (ItemTemplate mit `TextWrapping`); geprüft wird gegen den Rand der Vorlage
  (`OberflaecheRaidBilderTests.FeldauswahlGanz`).
- `Geraet.Zuletzt` setzt der Server beim Beginn und beim Ende einer Sitzung, `geraete --nicht-seit` vergleicht mit 0 Uhr
  UTC des Tages. Das Profil kommt aber nur mit Welcome beim Beginn. Bis 01.10.2026 nannte UMZUG.md den Tag von Schritt 4:
  Wer beim Laden von Version 2 verbunden war und danach ging, fehlte in der Liste und hatte nur Version 1. Jetzt startet
  Schritt 4 den Server nach `profil-neu-laden` neu, und Schritt 6 nimmt den Tag danach (`UmzugAnleitungTests`). Wer
  `--nicht-seit` für etwas anderes braucht, misst damit "irgendein Sitzungsereignis seit", nicht "Sitzung begonnen seit".
- Prüfung der Pakete 0.4.0: `WeichesClipping` (Funk.Klang.dll) ist eine Übertragung von `opus_pcm_soft_clip_impl` aus
  libopus und stand nicht in THIRD-PARTY-NOTICES, die Funk.* ganz als eigenen Code führten. Wer Code aus einer fremden
  Quelle überträgt, trägt die Datei mit Namen, Copyright und Lizenztext ein (Nr. 11, 12); `UrheberrechtTests` sucht nach
  "in C# nachgebaut", "wie im Original", "Copyright (c)" und ähnlichem und verlangt den Dateinamen in den Notices.
- Eine Vorprüfung, die "ohne TPM" sperrt, muss denselben Anbieter öffnen wie das Anlegen (`CngKey.Create` öffnet den Platform
  Crypto Provider zuerst); nur dann heißt "lässt sich nicht öffnen" sicher "Anlegen scheitert". Bei unklarer Lage nie sperren,
  sondern versuchen lassen: Scheitert das Anlegen, nennt `Adminschluesselwahl.OhneTpm` den Code von Windows.
- `git grep` sieht nur verfolgte Dateien; neue, noch nicht hinzugefügte Dateien fehlen in der Suche (01.10.2026, Adminhinweis).
- PowerShell: `$x = if (...) { @() } else { @('-OhneBauen') }` macht aus dem einen Element eine Zeichenkette, und `@x` an
  ein natives Programm (cmd, pwsh) gibt sie Zeichen für Zeichen weiter. testlauf.ps1 bekam am 01.10.2026 `- O h n e B a u e
  n`, nahm "O" als Filter und lief 891 statt 5 Tests (über 7 Minuten). Schalter ausschreiben oder `@(...)` mit `,` erzwingen.
- Die Hörprobe bis 0.4.0 war ein Mithören des Gesendeten (`Sendekette.PaketeAusgeben`): Sie spielte nur bei offenem Gate.
  Netze stehen am Server ohne eigene Einstellung auf Taste (`Netz.AusloeserStandard`, Global immer), ohne Verbindung gibt es
  kein Sendenetz; dann hört die Maschine nicht auf die Sprachaktivierung (A10), und wer die Hörprobe einschaltete und sprach,
  hörte nichts (Sarevocs Fehlerbericht vom 01.10.2026). Im Probefunk steht das Netz Echo auf VA, dort fiel es nie auf. Wer an der
  eigenen Stimme baut, testet auch mit einem Sendenetz auf Taste (`HoerprobeTests`, Ursachentest).
- Die eigene Stimme über Lautsprecher kommt verzögert ins Mikrofon zurück. Gemessen im Prüfstand (30 s Sprache, Mithören an
  gegen aus): mit Echounterdrückung gingen 9 bis 18 % weniger Pakete hinaus (der Echoschutz hält das Echo für fremde Sprache und
  sperrt eigene Anlaute), ohne 18 bis 43 % mehr (das Echo hält das Gate offen, bei verzerrendem Lautsprecher kleben die Phasen
  zusammen). Darum Mithören nur mit Headset und die Hörprobe als Aufnahme: nie zugleich aufnehmen und abspielen, und während der
  Probe und `ProbeNachlaufRahmen` danach verwirft die Kette jede neue Phase der Sprachaktivierung ganz, BEGINN bis ENDE. Wer dort
  etwas ändert: Eine Phase darf nie ohne BEGINN hinausgehen; das Verwerfen hängt deshalb am BEGINN und endet erst am ENDE.
- Der Jitterpuffer der Hörprobe spielt 100 ms nach dem ENDE den Funk-Klick wie beim Empfänger. Tests, die nach einem Abbruch
  Stille erwarten, warten erst Puffer und Klick ab (`Probe_AbbrechenNeustartUndStopp`).
- Ein wechselnder Text mit Umbruch in einer Karte des Bereichs Audio bestimmt die Breite aller Karten (StackPanel links mit
  `MaxWidth`): Beim ersten Entwurf der Hörprobe sprang der ganze Bereich beim Klick schmaler. Der lange Hinweis steht fest über
  dem Knopf, daneben nur der kurze Stand; `OberflaecheHoerprobeBilderTests` misst die Breite in allen Ständen.
- Ein Text unter einem Schalter hängt auch am Schalter selbst. `HoerprobeViewModel.MithoerenText` las bis zur Prüfung 0.4.1 nur
  Hörweise und Auslösung und sagte mit Headset auch bei ausgeschaltetem Mithören "Halte sie, um dich zu hören"; wer das las,
  hielt die Sprechtaste und hörte sich nicht, wieder das Bild aus Sarevocs Fehlerbericht. Die Tests prüften nur den eingeschalteten
  Fall. Jetzt sagt der Text zuerst "Aus.", und `TextMelden` meldet ihn bei jeder Änderung von Schalter, Hörweise und Auslösung
  (`OberflaecheHoerprobeTests.Hoerprobe_MithoerenText_FolgtDemSchalter`, Bildstand "mithoeren").
- Zusammenführung 0.4.1 (01.10.2026): Die Zweige adminhinweis und hoerprobe bauten für 0.4.1 und ließen
  `Directory.Build.props` mit Absicht auf 0.4.0, damit sie sich nicht widersprechen. HEIMSERVER-PROXMOX.md nannte so schon
  "Admin-Setup 0.4.1", das kein Bau erzeugt hätte; Git und die Tests merkten davon nichts. Die Fassung setzt die
  Zusammenführung, und `SetupTests.Setup_AnleitungenNennenKeineFassungUeberDerEigenen` lässt in `docs` und `tools\paket`
  keine Fassung über der eigenen stehen (das Muster nimmt nur "Setup", "Client", "Server" und ähnliche Wörter vor der Zahl,
  damit 127.0.0.1 keine Fassung wird).
- Das Handbuch liegt in `docs\handbuch\`, einem Unterordner. `Setup_AnleitungenNennenKeineFassungUeberDerEigenen` liest nur
  `docs\*.md` direkt und sieht es nicht; dort darf 0.5.0 als Vorgabe stehen (Anf. A62, A63). `AdminWerkzeugTabelleTests`
  liest feste Anleitungen und das Handbuch ebenfalls nicht. `Repository_NenntBitLockerNirgends` (seit 0.5.1) liest `src`,
  `installer`, `tools\paket` und `docs` samt Unterordnern, nimmt aber `docs\handbuch\` und `docs\handbuch-neu\` aus, solange
  das Handbuch neu geschrieben wird; dort steht das Wort noch. Wer die Ausnahme aufhebt, muss das Handbuch vorher
  bereinigen. Sein Vorgänger, der nur die alte Pflichtzeile suchte, schlug am 01.10.2026 an, weil B10.6 des Handbuchs sie
  wörtlich zitierte: Alte Meldungen umschreiben, nicht zitieren, und vor dem Zusammenführen die Suite laufen lassen.

- Ausbaustufe S2 (0.5.0): Eine Phase als `varianten[{rolleId, felder[]}]` lag in `Verwaltung PAKET` auf Tiefe 9 (Wurzel, `objekt`, `ops`, Eintrag,
  `objekt`, `varianten`, Eintrag, `felder`, Eintrag), und 4.3 erlaubt 8: Der Server trennte das Paket mit PROTOKOLL. Die Lage-Tests sahen
  das nicht, erst der Drahttest (Logbuch: "Verbindung endet mit Fehler PROTOKOLL"). Deshalb stehen die Felder einer Phase flach mit
  `rolleId`. Wer Objekte in Verwaltungs-Objekte schachtelt: die Tiefe im PAKET zählen, die Wurzel ist 1. Die Grenze anzuheben brächte
  Clients 0.4.x zum Trennen, sobald sie tiefere Daten bekommen (etwa eine `Ablage` eines neuen Clients).
- Das Ablageformat schreibt per Reflexion jede öffentliche Eigenschaft in `lage.json`, auch berechnete ohne Setter (`rechte`,
  `istPrivilegiert` stehen dort seit 0.1.0); der Source-Generator des Steuerkanals ebenso auf den Draht. Hilfswerte an Lage- und
  Draht-Records deshalb als Methode (`Nebenstand.LaufendeNr()`, `Konto.AlsFunkerZu()`, `NetzPhase.FelderAnzahl()`).
- Rechte zur Zeit stehen an drei Stellen, die gleich rechnen müssen: `Konto.RechteZu` (Aktionen), `Schnappschuss.RechteAn` (je
  Sprachpaket, eigene Felder je Sitzung) und `Lagebild.HatRecht` im ClientKern. Der Funker-Modus kam in alle drei; fehlte er im
  Heißpfad, ginge ein Global-Paket des Admins im Funker-Modus durch (`S2FunkerLageTests` sendet eins).
- Ein selbst gewählter Platz zählt nur mit Sitzung oder in der Wiederverbindungsfrist. Nach einem Neustart hat das Konto keine Sitzung
  und oft `sitzungsEnde` 0, der Platz ist FREI; eine Phase stellt nur BELEGTE und RESERVIERTE Plätze um. In den Testdaten 0.4.0 ist so
  Sarevocs CO-Platz frei, bis er sich anmeldet (`Uebergang040Tests`).
- Ein TOTP-Code gilt einmal je Zeitschritt: In Drahttests vor einer zweiten Bestätigung `uhr.Vor(TimeSpan.FromSeconds(30))`.
- Der Token-Eimer rechnet in Gleitkomma: Nach genau 2 s bei 0,5/s fehlt ein Hauch zur nächsten Marke. Tests mit etwas Luft (2,1 s).
- Testkonstanten mit dem Namen eines Typs (`Schiff`, `Admin`) kollidieren über `using static` mit dem Typ bzw. dem Namensraum `Funk.Admin`
  (CS0229, CS0118); in `S2PhasenLageTests` heißt die Konstante deshalb `SchiffDrei`.
- S2 Clientseite: `Sitzung.Annehmen` übernahm die Sendeauswahl des Servers nur bei einer Quelle außer SELBST. Nach einem
  Phasenwechsel bleibt die Quelle SELBST (Regel 9), wenn jemand ein offenes Netz selbst dazugenommen hatte, und der Client
  sendete weiter auf sein altes Feld ohne Senderecht. Jetzt gilt auch `Mitgliedschaften.phase` (`S2ClientDrahtTests`).
- Am echten Server kommen die neuen Mitgliedschaften eines Phasenwechsels vor 0x4004 PhaseGeschaltet: Wer geschaltet hat, weiß
  der Client beim Umstellen noch nicht. `Teilnehmermeldungen` trägt den Namen nach, solange die Meldung steht (`S2DienstTests`,
  Gegenstück `S2TeilnehmerTests.Phase_UmstellungVorDemPhasenpush_NameKommtNach`). Die Attrappe kennt keine Reihenfolge.
- PowerShell: In doppelten Anführungszeichen ist der Backtick das Fluchtzeichen. Markdown-Code "erwartet" in Backticks wurde so
  zu ESC und "rwartet" (Backtick e), "varianten" zu VT (Backtick v), "tests" zu TAB (Backtick t), "alsFunkerBis" zu BEL (Backtick
  a), der schließende Backtick fiel weg; PROJEKT.md trug vier Steuerzeichen (gefunden und behoben am 01.10.2026). Texte mit
  Backticks nur über Dateien oder einfache Anführungszeichen schreiben, danach nach Zeichen unter U+0020 suchen.
- PowerShell: `R` ist ein Alias für `Invoke-History`. Eine Hilfsfunktion `R` für Ersetzungen lief nie, die Ersetzung fiel still
  aus; Hilfsfunktionen nicht mit einem Buchstaben benennen und nach jeder Ersetzung `git diff` ansehen.
- Ein Record `Meldung` in `Funk.Client.Modelle` kollidierte mit `Funk.Verwaltung.Meldung` in Tests mit beiden Namensräumen
  (CS0104); die Zeile der Meldezeile heißt deshalb `Zeilenmeldung`. `Rest` ist als Name eines Tupelelements verboten (CS8126).
- Layout-Rundung bei 175 %: Eine feste Spalte von 170 Einheiten sind 297,5 Bildpunkte; mit gerundeten Spalten wurde ein Text über
  beide Spalten (Karte Tastatur) einen Bildpunkt breiter als die Karte, sobald sie die volle Breite 820 bekam. Das zeigte sich erst,
  als S2 längeren Text unter Globale Tasten brachte (`Einstellungen_Schmaler_KeinTextUeberDenKartenrand`). Feste Spalten neben
  einer Sternspalte als Vielfaches von 4 Einheiten setzen (ganz bei 100 bis 200 %).
- Die Grundstellung, die der Server selbst nennt ("Grundstellung", "GRUND", keine Felder), ist im Flottenentwurf keine Änderung
  (`EigenePhasen`); sonst zählte jede erste Phase eine zweite Änderung, die niemand gemacht hat, und PHASE_SETZEN 1 ginge mit.
- Gegenprüfung S2: `NetzLoeschenAus` nahm das Netz aus den Varianten, ließ aber eine Variante ohne Feld stehen, wenn es ihr einziges war.
  Der laufende Server merkte nichts (der Schnappschuss des Schreibers wird nicht geprüft), aber `LageLaden` baut über den
  `SchnappschussBauer`, und dessen `Pruefen` lehnt eine leere Variante ab: Nach dem nächsten Neustart startete der Server nicht. Wer
  eine Liste in einem gespeicherten Objekt kürzt, prüft, ob `Schnappschuss.Pruefen` danach noch besteht (Test über
  `Zustandsablage.Speichern` und `LageLaden`). Jetzt fällt die Variante weg, und `LageLaden` übergeht leere Varianten.
- Der Melde-Thread der Klangmaschine holt die Rahmen kurz nach dem Aufnahme-Thread ab. Was beim Loslassen einer Taste schon aufgenommen,
  aber noch nicht abgeholt ist, gehört zum alten Spruch. Die Melde-Taste beendete beim Loslassen die Phase und begann mit dem nächsten
  Rahmen eine neue an der Sendeauswahl: Der Rest der Meldung ging mit Klick an die Crew. Jetzt läuft die Phase bis zu ihrem letzten
  Rahmen (`KlangBruecke._meldeRest`), und ein Druck der Sprechtaste vorher beendet sie (`Sprechsteuerung.SprechenGedrueckt`).
- "Beim Verbinden als Funker" schaltete nach jedem Wiederverbinden wieder ein, auch nach "Wieder Admin" mitten im Raid; ein Aussetzer nahm
  so still die Leitung. `FunkermodusViewModel` merkt den Abend, an dem du selbst ausgeschaltet hast.
- Eine Rückfrage, die jemand bestätigt, schickt den Stand, den sie zeigte, nicht den von jetzt: "Ja" am Brett schickte
  als `erwartet` die gerade laufende Phase, und nach dem Wechsel eines zweiten Leiters schaltete es die Flotte von dessen Phase aus um.

- Fassung 0.5.0 (A63): Ein kurzer Klick wirkt leiser, als sein Spitzenpegel sagt. Der Klick bis 0.4.1 hatte -24 dBFS Spitze, über
  200 ms aber nur -47,6 dBFS, gut 22 dB unter der Sprache hinter der Kette, und ging neben Spielton unter. Seitdem rechnet
  `FunkKlick` die Lautstärke als Pegel über 200 ms (`PegelUeber200Ms`) und bringt jeden Klang darauf; so klangen Klick,
  Doppelklick und Piep bei derselben Zahl ähnlich laut, und so ist seit Stufe 2b jedes einzelne Rauschen geeicht (in `Erzeugen`,
  nicht über die Spitze). Ein Rauschen hat keine feste Spitze: Die höchste von 2000 liegt ganz laut (-6 dB, mono) bei -3,8 dBFS,
  knapp 2 dB unter dem Knie des Begrenzers, die höchste von 100 000 bei -3,3 bzw. -3,1 dBFS (zwei Läufe, `MessungAntwortUndKlick.Messung_KlickSpitzen`);
  wer Dauer, Band oder Hülle ändert, misst das neu (`KlickTests.Spitze_...`).
- Echoschutz seit Stufe 2b: Die Kopplung lernt nur, solange die Wiedergabe gerade spielt. Das nimmt die Fehlphasen beim
  verzerrenden Lautsprecher, kostet aber Anlaute direkt nach fremdem Funk und beim Ins-Wort-Fallen: Wer bei geschlossenem Gate
  spricht, zieht die Kopplung hoch, und die Haltezeit holt sie nicht mehr herunter. Zahlen und Messweg in
  `MessungAntwortUndKlick` (FUNK_MESSUNG=1); wer am Lernen dreht, misst das, `MessungSpielton` und die Echotests.
- Ein Klick, der in jedem Lauf anders klingt, macht Tests zufällig: Maschine und Prüfstand bekommen einen Startwert
  (`KlangMaschinenOptionen.KlickStartwert`, `Pruefstand(klickStartwert:)`), der knappste Fall läuft mit mehreren
  (`KlickMaschineTests.Klick_AndererSprecherUeberLautsprecher_...`). Mit nur einem Startwert hätte der Fehler im Echoschutz
  (Lernen in der Haltezeit, behoben in Stufe 2b) je nach Glück gegriffen oder nicht.
- Der eigene Klick hängt am Mikrofon (`KlangMaschine.SprechtasteGedrueckt`/`Losgelassen`), nicht an der einzelnen Taste: Hält man
  die Sprechtaste schon, klickt die Melde-Taste nicht noch einmal (`KlickSprechwegeTests`). Ein neuer Sprechweg, der die Maschine
  nicht über diese beiden Aufrufe öffnet, klickt nicht; der Test geht jeden Weg bis zur Wiedergabe.
- Die Auswahl (ComboBox, Vorlage in `Stile.xaml`) zeigt im geschlossenen Feld `ToString()` des Eintrags, nicht `DisplayMemberPath`:
  bis Stufe 2 stand bei der Wahl des Klangs "Klangwahl { Wert = Klick, Name = Klick }" da, bis `ToString` den Namen gab.
- Die aufgeklappte Sprachaktivierung braucht für ihre zwei Spalten 594 Einheiten (gemessen: Raster zu schmal angeordnet, seine
  Breite samt Rand; `DesiredSize` taugt dafür nicht, WPF kürzt sie auf den angebotenen Platz). Bis Stufe 2 war der Funk-Klick
  die dritte Spalte (870) und zog unter 870 in die Spalte der Schalter um; das Fenster wurde dafür 1100 breit.
- Ein `GridSplitter` verschiebt nur die zwei Zeilen neben sich. Damit der Trenner unter der Leiste Sprachaktivierung die Höhe
  zwischen Inhalt und Bereich teilt, stehen Inhalt und Leiste zusammen in einer Zeile (`Hauptfenster.xaml`).
- PowerShell: `@( @('alt', 'neu', 1) )` ist kein Feld mit einem Paar, sondern das Paar selbst; eine Schleife darüber läuft über
  drei Zeichenketten. Ersetzungslisten mit einem Eintrag als `@(,@(...))` schreiben oder vorher prüfen (`$liste[0] -is [string]`),
  und jede Ersetzung erst zählen (A65: die Zählung brach ab, bevor etwas geschrieben war).
- A65: Die Testdaten `zustand-0.2.0` und `zustand-0.4.0` stammen von den Programmen selbst; Rudi ist dort nachträglich durch Otto
  ersetzt (gleich lang, im Namensindex von konten.json an derselben Stelle der Sortierung, Nachtrag in HERKUNFT.txt). Wer dort
  Namen ändert, hält Länge und Sortierung oder erzeugt die Daten neu, sonst fallen die Byte-Vergleiche der Übergangstests.
- Die Kopfzeile "JG TacRelay - Maintainer: Sarevoc" (A58, A59, 02.10.2026) steht in Zeile 1 jeder eigenen Quelldatei, in Zeile 2 hinter `#!`, einer Python-Kodierungszeile oder
  `<?xml`. Ein Zeilenkommentar unmittelbar vor `<#` macht den Hilfeblock eines PowerShell-Skripts für `Get-Help` unsichtbar (am 02.10.2026 an
  `native\skripte\opus.ps1` gemessen); dazwischen gehört eine Leerzeile, der Wächter prüft das. Einfügen nur über Bytes: BOM (fünf `.cs`-Dateien) und Zeilenenden je Datei bleiben,
  wie sie waren (einige Arbeitskopien stehen mit LF da, obwohl `.gitattributes` CRLF verlangt). Die Quellen unter `native\quellen` und die gebauten DLLs bekommen keine Zeile. Die DLLs in `native\out` sind nicht neu gebaut
  worden; in `native\eigen` steht kein `__LINE__` und kein `assert` (nur `static_assert`), die verschobenen Zeilennummern sollten die Bitgleichheit
  (BAUBERICHT) also nicht berühren, gemessen ist das aber nicht: Beim nächsten Bau die SHA-256 gegen BAUBERICHT halten.
- Ein Regler in einer Spalte mit `*` neben festen Spalten wird bei schmalem Fenster fast so schmal wie nichts: Die Pegelzeile je Funker (160 + 200 + `*` + 70) ließ dem Regler im Hauptfenster
  mit 760 Einheiten Breite 2,3 Einheiten, sein Knauf (16 breit) stand daneben. WPF ändert dabei weder Wert noch Bahn, der Wert liegt immer zwischen Minimum und Maximum; die Bahn selbst muss Platz behalten (`MinWidth`).
- Der Schiebeschalter verschiebt seinen Knauf selbst (`KnaufVersatz`, Weg aus dem Layout). Wechselte die Ansicht nach Liste (Knauf 16 statt 12 breit), blieb der alte Versatz stehen, denn nur die Spur meldete
  `SizeChanged`, und sie ändert ihre Größe nie. Jede Größe, die in den Weg eingeht, braucht ihre eigene Meldung.
- NaN kommt durch jede Klammer: `Math.Clamp(NaN, a, b)` gibt NaN, `Math.Abs(NaN - x) < 0,01` ist falsch, also wurde NaN als neuer Wert übernommen und gespeichert (Lautstärke je Netz, Pegel je Funker).
  Ein `Slider` lehnt NaN mit einer Ausnahme ab (Bindungsfehler), und der Wert stünde in der Datei. Modelle, die Werte annehmen, prüfen `double.IsNaN` vor dem Begrenzen.
- Anleitungen zum Funker-Modus 0.5.0: Es ruhen Führung und `ZUWEISEN` (Global, Durchsage, Mehrfachsenden, Brett, Einteilen, Phasen); die Verwaltung bleibt und gilt wie immer nur in der Admin-Sitzung
  (Protokoll 6.15, `FunkermodusViewModel`). Der erste Wurf von HEIMSERVER Abschnitt 17, LIESMICH und LIESMICH-Server schrieb "Verwaltungsrechte ruhen" (aus dem Arbeitsauftrag übernommen, nicht gegen 6.15 gelesen);
  die schon gebauten Pakete 20261002_0129 bis _0134 mussten neu (tmp\tr-050\verworfen). Texte zu Rechten gegen 6.15 und 6.12 lesen, nicht gegen einen Auftrag.
- Der Container des Heimservers ist 180, die Anleitungen nehmen 150 als Beispiel (Abschnitt 0). Abschnitt 17 nennt 180 und sagt es; ältere Abschnitte lässt er bei 150.
- Das Handbuch liegt in keinem Paket. `BeispielnamenTests` liest es seit 0.5.0 (Rudi nirgends, Sarevoc nicht als Beispieleingabe) und `AdminWerkzeugTabelleTests` prüft die `funk-admin`-Befehle in seinen Codeblöcken;
  Syntaxzeilen mit `[--x]`, Bilder des Aufbaus und die Kommentarzeile `<JJJJ-MM-TT ...>` einer erzeugten server.json sind ausgenommen (sonst melden sie "unbekannter Befehl").
- `setup-bauen.ps1` löscht die entpackte Prüfung (`entpackt\`), wenn nicht `-ArbeitsordnerBehalten` dabeisteht; die Nachprüfung der Setups gegeneinander braucht sie.
- Python aus einem Bash-Heredoc: Der Weg verschluckt Backslashes (`\b` wurde ein Backspace-Zeichen, `"\\n"` ein echter Zeilenumbruch in einem C#-Literal). Skripte mit Pfaden und Escapes mit dem Write-Werkzeug als Datei schreiben und dann ausführen.
- Seit 0.7.0 ist ein abgelehntes Gerät sofort fort, bei einem Antrag samt Konto. Wer danach im Stand nachschlägt, findet es nicht:
  Das Journal nennt Gerät und Konto aus den Angaben von vor der Entscheidung (`FreigabeVermerken`), und der Push
  `GeraetStatus{ABGELEHNT}` kommt aus `GeraetStatusGeaendert.Vorher`. Ohne `Bye{ENTFERNT}` danach verbände sich der Client
  gleich wieder und stellte von selbst einen neuen Antrag (`Sitzung.RegistrierenAsync`, `AutomatischRegistrieren`).
- Die Arbeitsschleife der Zentrale liest in einem Durchlauf alles, was währenddessen ankommt; das Kontenbuch kommt aber erst
  nach dem Gruppen-Commit in den Schnappschuss (`KontenSichern`). Bis 0.8.0 konnte so der Sitzungsstart einer gerade
  freigegebenen Verbindung vor ihrem Konto ankommen: `SitzungAktivieren` meldete UNBEKANNT, der Server trennte. Fiel nur unter
  Last in ganzen Läufen auf (Reparaturwege in `HaertungTests`). Wer im Durchlauf Kontenbuch und Schnappschuss zugleich braucht,
  spiegelt vorher (`SpiegelAbgleichen`), wie `SitzungStartenAsync` seit 0.8.0.
- Die Herkunft an Kanal und Rolle (0.8.0) zählt im Flottenentwurf nie als Änderung (`Gleich` ohne Herkunft): Ein Server 0.7.x
  speichert sie nicht, und ein Entwurf, der sie mitzählte, bliebe dort für immer offen. Wer `Gleich` anfasst, lässt das so.
- Ein neuer Typ im Verwaltungsteil hieß wie ein vorhandener in `Funk.Protokoll.Steuerung` (`Verwendung`, Katalog): Wo beide
  Namensräume eingebunden sind (`FlotteViewModel.cs`), meldet der Bau CS0104 (0.8.0, erster lokaler Bau). Ohne Compiler neue
  Typnamen gegen die Typen der eingebundenen Namensräume prüfen; der Typ heißt jetzt `Verwendungsstelle`.
- Seit 0.8.0 rollt die Verwaltung selbst (`VerwaltungBildlauf`), damit die Leiste des Entwurfs unten stehen bleibt; sie steht
  nicht mehr im Bildlauf der Bereiche. `Ansichtspruefung.Ganz` misst dann ihren Bildlauf. Wer in der Verwaltung etwas ins Bild
  rollt (`BringIntoView`), meint diesen Bildlauf.
- Gelb an den Pegelanzeigen (Q43, 0.8.0) rechnet nur `PegelVerlauf` (`WuerdeAusloesen`); was die Maschine in
  `PegelMeldung.WuerdeSenden` meldete, löscht `Bereinigen`. Ohne Kanal ist die Sprachaktivierung der Maschine aus (A10), ihr
  "sendet" sagt dort also nichts; Gelb und der Satz "es wird nichts gesendet" hängen am selben Maß (Pegel über der Linie mit
  Sprache). Bei stummem Mikro misst die Maschine nichts, dort gibt es kein Gelb.
- Nach einer Trennung können Felder und Quelle bis zum Abgleich (`FunkLage.Abgeglichen`) noch aus der vorigen Sitzung stammen.
  `Teilnehmermeldungen` meldet deshalb nach einer Trennung nichts, bevor der Stand abgeglichen ist, auch keine Umstellung, und prüft
  erst dann die Ansage beim Login (0.11.0). `_neueSitzung` gilt nur nach einer Trennung, nicht beim Bau mit schon aktiver Lage: Die
  Attrappen laufen ohne Abgleich, ihre Tests sähen sonst nie eine Meldung. Dasselbe Muster hat `FunkfeldViewModel.RolleBeobachten`.
- Die `kontoVersion` erkennt keine Einteilung: Phasenwechsel und eigene Änderungen (`FeldSetzen`, `RolleWaehlen`) erhöhen sie, ohne dass
  jemand eingeteilt hätte, und die Reservierung bleibt dabei stehen. Die Ansage beim Login vergleicht deshalb (durch, Einheit, Rolle), und
  `Wiederherstellen` meldet nach einer eigenen Änderung die eigene Quelle statt der Herkunft der Reservierung (Befunde der Prüfung 0.11.0).
- `IsVisible` ist im Bildlauf der Tests immer falsch (kein sichtbares Fenster); wer im Code nach sichtbaren Elementen sucht und das testen
  will, prüft `Visibility` (so `BrettAnsicht.ChRasterZiel`).
- Das Kontenverzeichnis (0x0038) hat eine Ratengrenze (1 je 5 s, Burst 2, FUNKPROTOKOLL 4.8). Brett ("Alle Mitglieder") und "Wer ist da"
  ("auch offline", seit 0.11.0) holen es getrennt, je mit eigenem Mindestabstand; der Burst trägt beide, darüber kommt ein Fehler und
  die alte Liste bleibt stehen, keine Trennung.
- Die Personenkachel heißt im Code `Personenkachel`, nicht Kachel: `KachelViewModel`, `Kachelraster.cs` und der Stil `Kachelkopf` gehören
  zum Funkfeld. Die Karten auf den Plätzen des Bretts bleiben Karten (`Brett.Karte`).
- Die Kanal-ID eines Felds ist nie die Netz-ID der Rolle: Eine Rolle nennt das Kanalnetz, das Feld den Kanal. Wer Felder gegen die Netze
  der Rolle vergleicht (`RollenNetze`), vergleicht ein Kanalfeld über `Kanalrechnung.Zerlegen` und die Feldnummer (`AusDerRolle`); bis
  0.10.0 galt ein festes Rollenfeld mit Ch. sonst als Zusatznetz und ging beim Umsetzen mit.
- Bei 175 % rundet das Layout eine Auto-Spalte kleiner als den Knopf darin (69,14 für 6 Rand und 63,43 Knopf): Der Knopf ragte einen
  Bruchteil über den Rand, mit einer zweiten Auto-Spalte daneben wurde er sichtbar beschnitten (Detail im Brett, 0.11.0). Ein Knopf am
  rechten Rand einer Zeile bekommt eine Einheit Rand rechts.
- Ein `TextBlock` bricht an einem Zeilenumbruch im Text auch mit `TextWrapping="NoWrap"` um. Die einzeilige Meldezeile (0.11.0) macht
  deshalb in `Meldezeile.Einzeilig` aus jedem Umbruch ein Leerzeichen; Sätze von außen (Sperrgrund, Fehler des Servers) können einen
  tragen. Die Breite der Meldezeile kommt aus dem Funkfeld in Bildpunkten durch die Skala seines Monitors (`Meldezeilenlage.Hoechstbreite`);
  `MaxWidth` sitzt am `Rahmen`, nicht am Fenster, denn der Bildlauf der Tests misst die Wurzel ohne das Fenster. Seit Q111 trägt der
  `Rahmen` die Skalierung des Funkfelds als `LayoutTransform`; `MaxWidth` gilt vor ihr, darum steht dort die Höchstbreite durch den Faktor
  (`MeldezeileFenster.GroesseSetzen`), und `Rahmen.ActualWidth` ist ohne den Faktor gemessen: Breiten in Einheiten des Fensters mit ihm
  (`LayoutTransform.Value.M11`) rechnen.
- Die Meldezeile hält seit Q112 nur eine Meldung; die Reihenfolge der Aufrufe zählt. Wer in einem Test Hinweis, Leitung, Fehler
  nacheinander zeigt, hat danach nur den Fehler: Die beiden anderen sind ersetzt, nicht wartend. Sollen sie warten, erst die höhere zeigen.
- Die Meldezeile entprellt seit 0.11.1 (AP3): Derselbe Schlüssel mit demselben Text erscheint binnen 5 min (`Meldezeiten.WiederholungMs`) nicht
  noch einmal, auch nicht nach `Loesen`; gezählt ab dem Erscheinen (`Neu`), nicht ab dem Versuch. Ein Test, der denselben Hinweis zweimal zeigen
  will, stellt die Uhr 5 min weiter oder ruft `Zeigen` mit `immer: true` (so die Tasten und `Teilnehmermeldungen` im Programm); ein Fehler
  (`Meldeart.Fehler`) ist nie entprellt. Zwei Tests liefen in AP3 deshalb rot (MeldezeileEineMeldungTests, OberflaecheKlangC2bTests).
- Ein RadioButton, der schon gewählt ist, ändert beim Klick nichts an `IsChecked`; eine TwoWay-Bindung feuert dann nicht. Soll jeder Klick
  etwas tun (die Probe der Meldezeile), gehört der Klick in ein `Command`, und `IsChecked` bindet nur OneWay: Der eigene Umschlag des
  RadioButtons setzt den aktuellen Wert (SetCurrentValue), die Bindung bleibt stehen (`Bereiche.xaml`, `Meldezeile.Feld`; Befund der Prüfung 0.11.1).
- gong meldet `DragLeave` des Blocks erst nach `DragEnter` des Spaltenkopfs darum. Wer beim Verlassen etwas wegnimmt, merkt sich, welches Ziel
  es zuletzt gesetzt oder bestätigt hat (`BrettViewModel._vorschauZiel`), nicht nur, zu welchem Block es gehört: Sonst nimmt der Block weg, was
  der Kopf gerade bestätigt hat, wenn sein bester Platz derselbe Block ist (Befund der Prüfung 0.11.1).
- `Process.GetProcessesByName` öffnet kein Handle auf den Prozess (die Liste kommt aus NtQuerySystemInformation); erst `Handle`, `Modules`
  oder `Kill` täten es. `Spielfenster` bleibt bei `Id` und EnumWindows mit `GetWindowThreadProcessId`; der Prozessname ist dort genau
  `StarCitizen`, der Launcher (`StarCitizen_Launcher`) zählt nicht, anders als in `Spielsperre` der Messung, die beide meidet.
- Ein `Grid` mit `MinWidth` je Spalte läuft über, statt zu stapeln: Reicht der Platz nicht, hält es beide Mindestbreiten und ragt rechts aus dem
  Bildlauf (waagerecht abgeschaltet, er schneidet ab). Im Verwaltungsteil stand so bei 760 Fensterbreite die Karte rechts außer Sicht, und ein
  `DockPanel` mit dem Knopf rechts angedockt gab der Überschrift nur den Rest ("Mitglie", Sarevocs Bild vom 12.10.2026). Seit 0.11.1 (AP4) stapelt
  dort der `Zweispalter`, und Kopfzeilen sind `WrapPanel`; der Wächter `OberflaecheVerwaltungLayoutTests` zeichnet jede Seite bei 760, 960 und
  1100 und meldet Überlappungen, Beschnitt, Überlauf und Kürzung ohne Hilfetext (`Ansichtspruefung.Ueberlappungen`, `GekuerztOhneHilfe`).
- Ein Innenrand, der bei 175 % keine ganzen Bildpunkte ergibt (7 Einheiten sind 12,25), kostet mit `UseLayoutRounding` einen halben Bildpunkt:
  Das Herkunftsschild gab "Baustein Trupp" 71,4 Einheiten Platz für 72, das "p" war angeschnitten (AP4; gemessen: Text 72,0 gerundet,
  Rahmen 85,71 nach dem Runden auf 1/1,75). Innenränder und Lücken in 2er-Schritten (8 sind 10, 12, 14 Bildpunkte bei 125, 150, 175 %)
  bleiben auf dem Raster. Der Bildlauf der Tests rundet mit der Skala des Hauptmonitors dieses Rechners (175 %), auch wenn er mit Skala 1 zeichnet.
- Edit- und Write-Werkzeug (der Weg über JSON) lesen `\u2028` und `\u2029` als Zeichen, nicht als Text: In einem C#-Zeichenliteral standen danach
  echte Zeilentrenner, und der Compiler meldete CS1010 (0.11.0, `Meldezeile.Umbrueche`). Ebenso wurden die Escapes der verbotenen Zeichen
  in einer Prüfung (Gedankenstrich, Auslassung, geschütztes Leerzeichen) still zu echten Zeichen im Quelltext. Solche Zeichen als
  `(char)0x2028` schreiben oder die Escapes danach mit einer Suche nach den echten Zeichen prüfen.
- Ein `Effect` (etwa `DropShadowEffect` in `ItemsControl.Effect`) bekommt keinen DataContext: Bindungen an seine Werte (`BlurRadius="{Binding ...}"`)
  melden "Cannot find governing FrameworkElement", und der Bildlauf scheitert an den Bindungsfehlern (0.11.9, Sprecheranzeige). Das Modell
  baut den Effekt darum fertig und eingefroren (`Sprecherbild.Schatten`), und die Vorlage bindet `Effect="{Binding Schatten}"` als Ganzes.

## Dateien

<!-- DATEIEN-BEGINN (tools\codemap-erzeugen.ps1) -->

| Datei | Zeilen | Aufgabe (erste Beschreibung im Code) |
| --- | --- | --- |
| `src/Funk.Admin/Befehlsoptionen.cs` | 165 | Welche Optionen jeder Befehl von funk-admin kennt.  lehnt alles andere ab, bevor eine Datei |
| `src/Funk.Admin/Befehlszeile.cs` | 71 | Zerlegt befehl [positional] --name wert --schalter; Wiederholungen sammeln sich (etwa --pin). Ob die |
| `src/Funk.Admin/Dialog.cs` | 42 | Fragen an der Konsole für die Befehle mit Rückfrage (profil-erneuern, umzug-einspielen, erster-admin). Liest Zeilen, bis eine |
| `src/Funk.Admin/Einrichtorte.cs` | 54 | Wo funk-admin einrichten liest und schreibt. Im Betrieb , dieselben Pfade wie in |
| `src/Funk.Admin/Einrichtung.cs` | 1083 | funk-admin einrichten (A55, EINRICHTEN-UMZUG-ENTWURF "Vorschlag Einrichtung"), aufgerufen von funk-einrichten.sh, |
| `src/Funk.Admin/ErsterAdmin.cs` | 99 | funk-admin erster-admin (EINRICHTEN-UMZUG-ENTWURF, Ende von "Vorschlag Einrichtung"): auf einem frischen Server den |
| `src/Funk.Admin/Hilfe.cs` | 110 | Hilfetext des Admin-Werkzeugs. |
| `src/Funk.Admin/Hostangabe.cs` | 43 | Ein Host fürs Serverprofil, wie funk-admin ihn annimmt: name, name/v4, name/v6 oder name/v4v6 |
| `src/Funk.Admin/Konfigschreiber.cs` | 129 | Die Angaben für funk-admin konfig-schreiben; ohne Angabe gelten die Werte aus BETRIEB.md. |
| `src/Funk.Admin/Profilanfrage.cs` | 71 | Die Bitte eines Serverbetreibers um ein Profil (EINRICHTEN-UMZUG-ENTWURF): eine Zeile |
| `src/Funk.Admin/Profilerneuerung.cs` | 337 | funk-admin profil-erneuern, der eine Schritt auf Sarevocs Stick (A55, EINRICHTEN-UMZUG-ENTWURF "Was beim Eigentümer |
| `src/Funk.Admin/Program.cs` | 239 |  |
| `src/Funk.Admin/Schluesselorte.cs` | 49 | Wo die Schlüsseldateien eines Servers liegen, gesehen von der Konsole. In server.json stehen sie als |
| `src/Funk.Admin/Serverpruefung.cs` | 270 | funk-admin pruefen [--vollstaendig] (EINRICHTEN-UMZUG-ENTWURF, "Dazu im Code"). Ohne Schalter wie |
| `src/Funk.Admin/Systemblick.cs` | 165 | Was funk-admin vom Betriebssystem wissen oder dort tun will, an einer Stelle: Rechte einer Datei, Uhrzeit, DNS, freie Ports, |
| `src/Funk.Admin/Uebersetzung.cs` | 286 | Übersetzt eine Befehlszeile in Befehl und Argumente für den Verwaltungssocket (3.8). Redet mit niemandem; was hier |
| `src/Funk.Admin/Umzug.cs` | 230 | funk-admin umzug-packen und umzug-einspielen (EINRICHTEN-UMZUG-ENTWURF, "Umzugspaket"): Der alte Server packt |
| `src/Funk.Admin/Umzugspaket.cs` | 297 | Format des Umzugspakets (EINRICHTEN-UMZUG-ENTWURF, "Umzugspaket"), nur mit Bordmitteln von .NET: |
| `src/Funk.Client/Ablage/Anmeldedatei.cs` | 70 | Die Namen aus dem Setup (A42, ANMELDUNG-SICHERHEIT, Ablauf des Antrags, Schritt 1): Die Seite "Deine Namen" schreibt |
| `src/Funk.Client/Ablage/ClientEinstellungen.cs` | 1605 | Einstellungen des Clients, als JSON im Benutzerordner der App (). Keine Geheimnisse: |
| `src/Funk.Client/Ablage/EinstellungsSpeicher.cs` | 466 | Liest und schreibt  als JSON. Der Pfad ist |
| `src/Funk.Client/Ablage/Platzierung.cs` | 288 | Rechteck in Bildpunkten des virtuellen Bildschirms (physische Pixel, so wie Windows sie zählt). |
| `src/Funk.Client/Ablage/Spielprofil.cs` | 70 | Das Spiel, über dem Sprecheranzeige, Meldeanzeige und Meldezeile stehen (Sarevoc, 18./19.10.2026, Q10; JSON spiel): ab Werk Star Citizen, |
| `src/Funk.Client/App.xaml.cs` | 595 | Zusammensetzung der Oberfläche. Schalter: |
| `src/Funk.Client/AssemblyInfo.cs` | 5 |  |
| `src/Funk.Client/Design/Farbvorschlaege.cs` | 262 | Welche Farbstellung die Oberfläche trägt. Seit dem 19.10.2026 gibt es genau eine (Sarevoc, Q5, Q12, Q17): Anthrazit mit Petrol-Akzent. |
| `src/Funk.Client/Design/Lesbarkeit.cs` | 194 | Ein Vordergrund mit seinem Mindestkontrast nach WCAG 2.1: Text 4,5:1, Grenzen, Symbole und Fokus 3:1. |
| `src/Funk.Client/Design/Meldungsbilder.cs` | 121 | Ein Meldungsbild, auf seine sichtbaren Bildpunkte zugeschnitten:  (eingefroren), dazu wo der Ausschnitt im ganzen Bild |
| `src/Funk.Client/Design/Palette.cs` | 110 | Die Farben aus Design\Farben.xaml für Code, der selbst zeichnet (Pegelanzeige, Schwellengrafik): die Farbstellung |
| `src/Funk.Client/Design/Textfarben.cs` | 52 | Eine Farbe der Textmeldung: Kennung auf dem Draht (), Name für Menschen und der Farbton. |
| `src/Funk.Client/Design/Zeichensatz.cs` | 295 | Die Form des Lorbeerkranzes (A49): drei Varianten zum Aussuchen, gezeichnet im Vergleichsbild zeichen-varianten.png. |
| `src/Funk.Client/Dienste/Fehlernummern.cs` | 53 | Kurze Fehlernummern zum Weitersagen (Sarevoc, Grilling Q5): Die Meldung für Funker beschreibt die Lage und endet mit |
| `src/Funk.Client/Dienste/FunkClientDienst.cs` | 739 | Eine aufgebaute Anbindung: der Client und was mit ihm zusammen freigegeben wird (Geräteschlüssel). |
| `src/Funk.Client/Dienste/FunkDienstAttrappe.cs` | 322 | Attrappe des Funkbetriebs für Entwurf, Vorführung und Tests: hält eine , ändert sie auf |
| `src/Funk.Client/Dienste/FunkDienstAttrappe.Kanaele.cs` | 247 | Stufe 3 in der Attrappe (STUFE3-ENTWURF 4.3, FUNKPROTOKOLL 6.16 und 6.17): FeldSetzen und RolleWaehlen wie ein |
| `src/Funk.Client/Dienste/FunkDienstAttrappe.Meldungen.cs` | 161 | Meldesystem (0.12.0) in der Attrappe, wie ein freundlicher Server: Recht nach , höchstens eine |
| `src/Funk.Client/Dienste/FunkDienstAttrappe.Raid.cs` | 269 | Stufe 2 in der Attrappe (RAIDLEITUNG-ENTWURF, M4, M5, Anhang A): Einsteigen "wie letztes Mal", Verschieben mit Kapazität am |
| `src/Funk.Client/Dienste/FunkDienstAttrappe.Raidabend.cs` | 234 | Der Beispielabend aus RAIDLEITUNG-ENTWURF Anhang A für Brett, Einsteigen und Vorführung: Nordwind ist Admin, Max Mustermann leitet im |
| `src/Funk.Client/Dienste/FunkDienstAttrappe.S2.cs` | 163 | Ausbaustufe S2 in der Attrappe (RAID-S2-ENTWURF): "Heute als Funker" (nur ADMIN, bis 04:00, ein zweites Einschalten verlängert |
| `src/Funk.Client/Dienste/FunkLage.cs` | 586 | Verbindungszustand für die Anzeige, verdichtet aus 4.6. |
| `src/Funk.Client/Dienste/Gildenpaket.cs` | 42 | Der Gildenbezug der Oberfläche (Sarevoc, Vorgabe e: austauschbar halten). In Stufe 2 nur das Logo, und zwar fest der Platzhalter |
| `src/Funk.Client/Dienste/IFunkDienst.cs` | 184 | Naht der Oberfläche zum Funkbetrieb. Im Betrieb steckt  dahinter (Funk.ClientKern), |
| `src/Funk.Client/Dienste/LageAbbildung.cs` | 254 | Was die Abbildung braucht, als reine Werte (Tests bauen sie ohne Verbindung). |
| `src/Funk.Client/Dienste/Oberflaechentakt.cs` | 37 | Bringt Arbeit auf den Oberflächen-Thread. Die ViewModels hängen nur an dieser Naht, nicht am WPF-Dispatcher; so laufen |
| `src/Funk.Client/Dienste/Profilpruefung.cs` | 361 | Ein eingefügter Profilschlüssel nach der Prüfung: gültig mit SPKI (Base64) und Kennung, sonst ein Fehlertext. |
| `src/Funk.Client/Dienste/Profilquelle.cs` | 61 | Serverprofil und Profilschlüssel, die mit dem Client kommen (3.1, ANMELDUNG-SICHERHEIT P1 und "Was an der normalen |
| `src/Funk.Client/Dienste/Profiluebernahme.cs` | 225 | F1 (EINRICHTEN-UMZUG-ENTWURF, FUNKPROTOKOLL-v1.md 3.1): Nach Welcome{FREIGEGEBEN} prüft der Client |
| `src/Funk.Client/Dienste/Verbindungsfabrik.cs` | 158 | Baut aus den Verbindungseinstellungen einen : das signierte Serverprofil prüfen, das höchste gültige aus |
| `src/Funk.Client/Dienste/Verbindungstexte.cs` | 158 | Ein Satz für Funker und getrennt davon die technische  (Adresse:Port, Meldung von Windows, Pfad, |
| `src/Funk.Client/Dienste/Verwaltungslader.cs` | 109 | Was das Laden des Verwaltungsteils ergab: der Teil, oder warum nicht (null: nicht installiert, der Normalfall). |
| `src/Funk.Client/Dienste/Vorfuehrung.cs` | 67 | Lässt in der Vorführung (--vorfuehrung) wechselnde Funker in verschiedenen Kanälen sprechen und liefert dazu |
| `src/Funk.Client/Einzelinstanz.cs` | 394 | Nur eine Instanz je Einstellungsdatei (Sarevoc, 30.09.2026: "Wichtig auch nur eine Instanz"). Der erste Start belegt einen |
| `src/Funk.Client/Fenster/Ablagesymbol.cs` | 322 | Ein Bild in einer ICO-Datei: Kantenlänge in Bildpunkten (256 für den Eintrag 0) und wo seine Daten stehen. |
| `src/Funk.Client/Fenster/Bildschirme.cs` | 135 | Die Monitore, wie Windows sie gerade meldet. Im Betrieb , in Tests eine feste Liste. |
| `src/Funk.Client/Fenster/BrettAnsicht.xaml.cs` | 79 | Die Raid-Seite der Einsatzleitung (Bereich Raid). Hier steht nur, was die Ansicht selbst braucht: Strg+E springt in die Suche. Seit 0.11.2 |
| `src/Funk.Client/Fenster/Fenstergroessen.cs` | 276 | Größenklassen des Hauptfensters (Sarevoc, 02.10.2026, Q19 und Vorgabe j): je Klasse eine eigene, getrennt gemerkte Größe. Wird nicht gespeichert, |
| `src/Funk.Client/Fenster/Fensterrahmen.cs` | 87 | Gemeinsames für die Fenster mit eigener Titelleiste (WindowChrome): Befehle der Titelknöpfe, Rand im maximierten |
| `src/Funk.Client/Fenster/FunkfeldFenster.xaml.cs` | 312 | Funkfeld (A24): eigenes Fenster, immer im Vordergrund, schließbar, nicht minimierbar und nicht maximierbar (ein Overlay), |
| `src/Funk.Client/Fenster/FunkfeldFensterFuehrung.cs` | 68 | Das echte Funkfeld-Fenster für die  (RAID-S2-ENTWURF Abschnitt 5, "Ohne Fokus"). Gebaut wird es erst |
| `src/Funk.Client/Fenster/Hauptfenster.xaml.cs` | 527 | Hauptfenster (A25): minimierbar, frei skalierbar, eigene Titelleiste, Tastaturbedienung. Die Navigation links zeigt ihre |
| `src/Funk.Client/Fenster/MeldeanzeigeFenster.xaml.cs` | 158 | Das Fenster der Meldungen über dem Spiel (seit 0.12.0, Sarevoc 11.10. und 16.10.2026), nach dem Muster der Sprecheranzeige |
| `src/Funk.Client/Fenster/MeldezeileFenster.xaml.cs` | 134 | Das Fenster der Meldezeile (RAID-S2-ENTWURF Abschnitt 3). Es zeigt sich nur, solange , und nie mit |
| `src/Funk.Client/Fenster/Meldezeilenlage.cs` | 92 | Stil und Lage des Fensters der Meldezeile (RAID-S2-ENTWURF Abschnitt 3), als reine Rechnung ohne Fenster: nie aktiviert |
| `src/Funk.Client/Fenster/MeldungenFeld.xaml.cs` | 10 | Das Feld "Meldungen" der Leitung auf der Funkfeld-Zuteilung (seit 0.12.0); alles Verhalten steht im MeldungenViewModel. |
| `src/Funk.Client/Fenster/Platzfuehrung.cs` | 213 | Setzt Lage und Größe eines Fensters aus einem  und merkt, was du daraus machst (A32, Sarevoc: "so wie |
| `src/Funk.Client/Fenster/Schwellenbereich.xaml.cs` | 104 | Sprachaktivierung genau einstellen (A26-A28), seit 0.10.0 nur noch als Seite (Unterpunkt unter Einstellungen, Strg+T): mit Überschrift, |
| `src/Funk.Client/Fenster/Spieldatei.cs` | 24 | "Programmdatei wählen ..." beim Spiel (Umbau 19.10.2026, Sarevoc Q10): der Dateidialog von Windows für eine .exe. Liefert nur den Pfad; gemerk... |
| `src/Funk.Client/Fenster/Spielfenster.cs` | 180 | Auf welchem Monitor das Spiel läuft, für den Platz der Meldezeile ab Werk (seit 0.11.1, Sarevoc 12.10.2026). Nur lesend, ohne |
| `src/Funk.Client/Fenster/SprecheranzeigeFenster.xaml.cs` | 148 | Das Fenster der Sprecheranzeige über dem Spiel (seit 0.11.9, Sarevoc 11.10. und 15.10.2026), nach dem Muster der Meldezeile |
| `src/Funk.Client/Fenster/WerIstDaFenster.cs` | 51 | "Wer ist da" als eigenes Fenster (seit 0.11.5, Sarevoc 14.10.2026: "wenn möglich auch als Popup und eigenes Fenster ablösbar"): dieselbe Liste... |
| `src/Funk.Client/Klang/KlangBruecke.cs` | 460 | Was die  vom Sprechkanal des Funkbetriebs braucht (5.6, C1, C2). Im Betrieb der |
| `src/Funk.Client/Klang/KlangMaschinenAttrappe.cs` | 746 | Ein Sprachpaket, wie es bei der Attrappe ankam (Tests). |
| `src/Funk.Client/Klang/PegelVerlauf.cs` | 294 | Die letzten Pegelmeldungen als Ring. 1024 Plätze reichen für 20 s bei 50 je Sekunde. |
| `src/Funk.Client/Klang/Sprechsteuerung.cs` | 176 | Wohin die Melde-Taste gerade sprechen würde: Netz und Name, oder warum es nicht geht. |
| `src/Funk.Client/Messung/FunkfeldMessauftrag.cs` | 157 | Was TacRelay.exe --funkfeld-messen &lt;bericht.json&gt; [--ansicht raster/liste] [--felder 5/10] [--sekunden 3] |
| `src/Funk.Client/Messung/FunkfeldMessung.cs` | 358 | Eine Probe alle 100 ms: das echte Fenster in Bildpunkten (GetWindowRect), in Einheiten, DPI, Monitor, Kachelraster. |
| `src/Funk.Client/Modelle/AudioViewModel.cs` | 841 | Audio: Ein- und Ausgabegerät, Start und Stopp, Headset oder Lautsprecher (beim ersten Start umrandet auf Start), Rausch- und |
| `src/Funk.Client/Modelle/Bereitmeldung.cs` | 92 | "Bereit" auf die offene Bereit-Abfrage der Leitung (Meldesystem 0.12.0, M3-M14): über die Taste "Bereit" (Einstellungen > Tasten > Erweit... |
| `src/Funk.Client/Modelle/BrettTeile.cs` | 657 | Was eine Ablage auf dem Brett bewirken würde (RAIDLEITUNG-ENTWURF, Das Brett: grün, gelb, rot vor dem Loslassen). |
| `src/Funk.Client/Modelle/BrettViewModel.Baum.cs` | 398 | Die Raid-Seite seit 0.11.2 (Sarevoc 13.10.2026, ZUORDNUNG-ENTWURF "Stammbaum nach rechts"): links alle Namen (live oben, offline darunter, |
| `src/Funk.Client/Modelle/BrettViewModel.cs` | 2067 | Das Brett der Einsatzleitung (Bereich Raid, Strg+5; RAIDLEITUNG-ENTWURF "Das Brett", Bauplan M5). Sichtbar nur mit ZUWEISEN |
| `src/Funk.Client/Modelle/DauersendViewModel.cs` | 237 | Das gelbe "!" der Auto Dauersend-Dämpfung (Klangstufe C2b, KLANGSTUFE-C-ENTWURF.md, Q1, Q2, Q8, Q13): das eigene Urteil des Wächters |
| `src/Funk.Client/Modelle/Einstellabschnitt.cs` | 211 | Ein Abschnitt der Seite Einstellungen (seit 0.10.0, Mischung Q87, Bilder m-4 und m-5): Name mit Zeichen, der Kasten "Läuft von selbst" |
| `src/Funk.Client/Modelle/EinstellungenViewModel.cs` | 554 | Seite Einstellungen (seit 0.10.0 nach der Mischung, Q87, Bilder m-4 und m-5; bis 0.9.3 eine Seite mit Karten für globale Tasten, Funkfeld, |
| `src/Funk.Client/Modelle/Feinheiten.cs` | 413 | Die Kennungen der Klappbereiche "Erweitert:" in  (seit 0.10.0, E6): je Abschnitt der |
| `src/Funk.Client/Modelle/FunkermodusViewModel.cs` | 214 | "Heute als Funker" (RAID-S2-ENTWURF Abschnitt 1, FUNKPROTOKOLL 6.15): Ein Admin, der einen Abend nur funken will, schaltet am eigenen |
| `src/Funk.Client/Modelle/FunkfeldAblage.cs` | 313 | Das Funkfeld-Fenster, wie die Ablage es braucht; im Betrieb die App über dem echten FunkfeldFenster, in Tests eine Attrappe. |
| `src/Funk.Client/Modelle/FunkfeldViewModel.cs` | 1019 | Das Funkfeld (A24): 5 oder 10 Kacheln, das Sonderfeld Global immer rechts außen (A23). Wählt die Sendenetze, schaltet |
| `src/Funk.Client/Modelle/HauptViewModel.cs` | 641 | Ein Bereich des Hauptfensters (Navigation links).  steht im Tooltip der Navigation; |
| `src/Funk.Client/Modelle/HoerprobeViewModel.cs` | 194 | Die eine Hörprobe (seit 0.10.0 auf Start, auch verbunden, Q75, E19; bis 0.9.3 im Bereich Audio und auf der Seite Sprachaktivierung, im |
| `src/Funk.Client/Modelle/KachelViewModel.cs` | 536 | Zustand einer Kachel im Funkfeld (A17, A18; seit 0.6.0 mit Ruhe und Sperre). |
| `src/Funk.Client/Modelle/Kanalwahl.cs` | 28 | Ein Eintrag der Rollenliste oben links im Funkfeld (Stufe 3, Sarevocs Antworten Q1 und Q2): mit Nummer davor, etwa |
| `src/Funk.Client/Modelle/KlickViewModel.cs` | 129 | Funk-Klick einstellen (A11, A63, seit 0.5.0), seit 0.10.0 unter Einstellungen > Tasten > Erweitert (seit 0.6.0 Stufe 2b als Karte im |
| `src/Funk.Client/Modelle/KorridorViewModel.cs` | 415 | Korridor der Sprachaktivierung (Klangstufe C1, Entwurf "Korridor am Pegelbalken", Q17, Q18): links die Mindest-Funklautstärke |
| `src/Funk.Client/Modelle/LautstaerkeViewModel.cs` | 211 | Lautstärke der eigenen Stimme (Klangstufe A), seit 0.10.0 unter Einstellungen > Senden (der Schalter seit 0.11.1 offen, das Einmessen unt... |
| `src/Funk.Client/Modelle/MeinProfilViewModel.cs` | 620 | "Ich" unter Einstellungen: eigene Namen (SC-Namen, Discord-Name, N2), die eigenen Rollen (A46: vom Admin eingetragen mit |
| `src/Funk.Client/Modelle/Meldeanzeige.cs` | 665 | Die Meldungen der Leitung über dem Spiel beim Funker (seit 0.12.0, Sarevoc 11.10. und 16.10.2026, M3-M23, tmp\melde\SPEZ.md "Client"). Ein eig... |
| `src/Funk.Client/Modelle/Meldenetzwahl.cs` | 90 | Eine Wahl für "Melde-Taste spricht in": ein Netz der Flotte oder null für die Voreinstellung (Kommando). |
| `src/Funk.Client/Modelle/Meldezeile.cs` | 314 | Vorrang einer Meldung: Fehler vor der Leitung (Phasen) vor einem Hinweis. |
| `src/Funk.Client/Modelle/MeldezeilePlatzViewModel.cs` | 228 | Wo die Meldezeile steht (seit 0.11.1, Sarevoc 12.10.2026: "an einem festen Platz am Bildschirm aufpoppen, nicht da, wo das Funkfeld ist"), unter |
| `src/Funk.Client/Modelle/MeldungenEinstellungViewModel.cs` | 251 | Der Abschnitt Meldungen der Einstellungen (seit 0.12.0, nach Sprecheranzeige; tmp\melde\SPEZ.md "Client"): offen unter "Wo im Bild" zwei |
| `src/Funk.Client/Modelle/MeldungenViewModel.cs` | 673 | Das Feld "Meldungen" auf der Funkfeld-Zuteilung (Strg+5) für die Leitung (Meldesystem 0.12.0, Sarevoc 11.10. und 16.10.2026, M1-M23, tmp\melde... |
| `src/Funk.Client/Modelle/MikroViewModel.cs` | 203 | "Mikro stumm" (Klangstufe C2b, KLANGSTUFE-C-ENTWURF.md, "Mikro stumm", Q31 bis Q36): der Knopf in der oberen Steuerleiste des Funkfelds |
| `src/Funk.Client/Modelle/Nahtstellen.cs` | 31 | Fenster der Oberfläche; im Betrieb die App, in Tests eine Attrappe. Die Schwelle hat kein eigenes Fenster (A26); seit 0.10.0 ist |
| `src/Funk.Client/Modelle/NetzeViewModel.cs` | 859 | Die Kanäle: Einsteigen per Rolle und Schiff in einem Schritt mit Belegung je Schiff (A15) und Vorschau der Netze einer Rolle (A22), die e... |
| `src/Funk.Client/Modelle/Phasenrechnung.cs` | 85 | Was ein Phasenwechsel ungefähr bewirkt, aus NetzListe und Praesenz gerechnet, ohne Anfrage an den Server (RAID-S2-ENTWURF |
| `src/Funk.Client/Modelle/Phasensteuerung.cs` | 205 | Wo die Phasensteuerung gerade steht. |
| `src/Funk.Client/Modelle/Sammlung.cs` | 43 | Hilfen für Listen der Oberfläche. |
| `src/Funk.Client/Modelle/SchwellenViewModel.cs` | 317 | Sprachaktivierung einstellen (A26-A28), kein eigenes Fenster; seit 0.10.0 nur noch als Seite (bis 0.9.3 auch unten im Hauptfenster |
| `src/Funk.Client/Modelle/Sprecheranzeige.cs` | 377 | Die Sprecheranzeige über dem Spiel (seit 0.11.9, Sarevoc 11.10. und 15.10.2026, SPRECHERANZEIGE-ENTWURF.md): Solange jemand zu hören ist, |
| `src/Funk.Client/Modelle/SprecheranzeigeViewModel.cs` | 519 | Der Abschnitt Sprecheranzeige der Einstellungen (seit 0.11.9, zwischen Funkfeld und Darstellung und Fehlersuche; Sarevoc 11.10. und 15.10.2026... |
| `src/Funk.Client/Modelle/StartViewModel.cs` | 555 | Ein Stecker der Startseite: getrennt (grau, auseinander), verbindet, verbunden (grün, zusammen), Tunnel (gelb, zusammen). |
| `src/Funk.Client/Modelle/StatuszeileViewModel.cs` | 131 | Was die Marke rechts in der Statuszeile sagt (seit 0.10.0, E4). Wird nicht gespeichert. |
| `src/Funk.Client/Modelle/StimmprobenViewModel.cs` | 145 | Stimmproben (Sarevoc 08.10.2026, Q60; gebaut für den Prüfstand der Klangstufe D, seit 0.9.3 für Klangmessungen allgemein, denn die |
| `src/Funk.Client/Modelle/Tastenbetrieb.cs` | 147 | Was die globalen Tasten und Joystickknöpfe auslösen (RAID-S2-ENTWURF Abschnitte 3 bis 5), an einer Stelle statt verstreut in |
| `src/Funk.Client/Modelle/TastenViewModel.cs` | 505 | Globale Tasten in den Einstellungen (A3, A5): je Funktion (Sprechen, Sendenetz vor und zurück, seit 0.5.0 Melden und Funkfeld |
| `src/Funk.Client/Modelle/Teilnehmermeldungen.cs` | 261 | Was die Meldezeile jedem Funker sagt (RAID-S2-ENTWURF Abschnitte 3 und 4, Spielersicht), auch wenn das Funkfeld abgelegt ist: |
| `src/Funk.Client/Modelle/VerbindungViewModel.cs` | 595 | Verbindung und Anmeldung (A12, A25, A42), seit 0.6.0 auf der Startseite (): Status, Anmeldung mit |
| `src/Funk.Client/Modelle/WerIstDaViewModel.cs` | 836 | "Wer ist da" (A48): ein Knopf im Hauptfenster und Strg+6 öffnen eine Spalte rechts mit allen gerade Eingeloggten, mit Suche. Seit 0.11.5 |
| `src/Funk.Client/Steuerelemente/Kachelraster.cs` | 179 | Ordnet die Kacheln des Funkfelds in Zeilen an, so viele Spalten wie in die Breite passen, und setzt das letzte Kind |
| `src/Funk.Client/Steuerelemente/Klappwahl.cs` | 189 | Klappfeld des Funkfelds (Stufe 3, Schritt 3.4): ein flacher Umschaltknopf, der eine Liste aufklappt, für die Rollenliste oben |
| `src/Funk.Client/Steuerelemente/Pegelbalken.cs` | 524 | Waagerechte Pegelanzeige in dBFS mit Schwellenmarke. Zeichnet selbst (OnRender) mit eingefrorenen Pinseln; im |
| `src/Funk.Client/Steuerelemente/Reiterform.cs` | 84 | Die Form eines Reiters auf der Raid-Seite (0.11.3, Sarevoc 13.10.2026: "wenn nun die Register noch abgerundete Ecken bekommen und sich sichtbar |
| `src/Funk.Client/Steuerelemente/Sanft.cs` | 179 | Weiche Übergänge auf dem Brett (0.11.1, Sarevocs Punkt 4 "Ziehen mit Vorschau, sanft"): die Vorschau am freien Platz blendet ein und aus |
| `src/Funk.Client/Steuerelemente/Schiebeschalter.cs` | 210 | Schiebeschalter mit zwei Stellungen: ein Knauf, der in seiner Spur zwischen links (aus, im Funkfeld "5") und rechts (an, |
| `src/Funk.Client/Steuerelemente/SchwellenGrafik.cs` | 619 | Verlaufsgrafik der Sprachaktivierung (A26-A28): die letzten 10 s mit Pegelkurve, Rauschboden, Schwellenlinie und grün |
| `src/Funk.Client/Steuerelemente/Spaltenwechsel.cs` | 197 | Wie  die Breite auf die zwei Spalten verteilt. |
| `src/Funk.Client/Steuerelemente/Stammbaumlinien.cs` | 160 | Die Linien im Stammbaum der Raid-Seite (0.11.2): liegt in der Lücke zwischen einem Knoten und der Liste seiner Kinder, so hoch wie die |
| `src/Funk.Client/Steuerelemente/Stecker.cs` | 100 | Ein Stecker der Startseite (Server, Sprache), selbst gezeichnet aus Rechtecken und Linien (kein fremdes Bild; andere Funkprogramme zeigen |
| `src/Funk.Client/Steuerelemente/Wandler.cs` | 76 | bool zu Sichtbarkeit; mit  andersherum. |
| `src/Funk.ClientKern/Echo/Echoband.cs` | 297 | Ein empfangenes Sprachpaket, aus  kopiert.  ist das Echo-Netz, über das es kam. |
| `src/Funk.ClientKern/Echo/EchoFunker.cs` | 390 | Ein Teilnehmer, der im Echo-Netz mithört, jede Sprechphase aufnimmt (Opus-Pakete, ohne sie zu dekodieren) und sie nach |
| `src/Funk.ClientKern/Echo/EchoOptionen.cs` | 96 | Einstellungen des . Die Voreinstellung ist der Probebetrieb: Netz "Echo", 500 ms Pause nach |
| `src/Funk.ClientKern/Echo/Ratenbremse.cs` | 49 | Zwei Token-Eimer (Pakete und Opus-Bytes) wie die Grenzen des Servers je Sitzung (6.3), aber als Bremse: Statt zu |
| `src/Funk.ClientKern/Empfang.cs` | 153 | Prüfung der empfangenen Opus-Nutzlast (5.13): Der Client MUSS opus_packet_get_nb_samples(..., 48000) == 960 |
| `src/Funk.ClientKern/FunkClient.cs` | 399 | Client für den Protokollteil des Funks, ohne Oberfläche und ohne Audio. Baut die Steuerverbindung auf (Aufbau nach |
| `src/Funk.ClientKern/FunkClientOptionen.cs` | 99 | Kennzahlen des Jitterpuffers aus der Audio-Schicht für den EmpfangsBericht (4.5); ohne sie 0. |
| `src/Funk.ClientKern/Geraeteidentitaet.cs` | 127 | Geräteschlüssel (ECDSA P-256) mit selbstsigniertem Gerätezertifikat nach 3.2. Der Fingerabdruck (SHA-256 über das SPKI) |
| `src/Funk.ClientKern/Lagebild.cs` | 242 | Was der Client vom Server weiß: Welcome, NetzListe, eigene Mitgliedschaften, Praesenz und Pegel. Die Teile sind |
| `src/Funk.ClientKern/Messwerte.cs` | 224 | Stand der Messwerte einer Sitzung (7.8), als unveränderlicher Schnappschuss. |
| `src/Funk.ClientKern/Monouhr.cs` | 26 | Monotone Uhr des Clients (Stopwatch), in Takten und Millisekunden. |
| `src/Funk.ClientKern/Sitzung.cs` | 889 | Eine Verbindung des Clients vom Hello bis zum Ende (4.6): Leseschleife mit  (Automat, |
| `src/Funk.ClientKern/Sprachweg.cs` | 771 | Ein hoch zu versiegelndes Datagramm (Klartext aus dem Pool). |
| `src/Funk.ClientKern/Sprechkanal.cs` | 279 | Senden nach 5.6 und den Client-Regeln C1, C2 und C4 (6.11). Je Sprechphase sind Zielart, Ziele und VA konstant |
| `src/Funk.ClientKern/Testzugang.cs` | 129 | Testzugang für Last- und Zusammenspieltests ohne TLS, nur auf Loopback. Unter Windows braucht ein echter TLS-Handshake |
| `src/Funk.ClientKern/TlsAufbau.cs` | 407 | Einstellungen für . |
| `src/Funk.ClientKern/Typen.cs` | 118 | Antwort auf eine eigene Anfrage: derselbe Typ mit Body oder Fehler (4.2). |
| `src/Funk.ClientKern/Uhrabgleich.cs` | 143 | Die Uhr des Servers im Client (Meldesystem 0.12.0: Countdown, Bereit-Abfrage), laufend abgeglichen über Ping wie bei NTP. Jede |
| `src/Funk.ClientKern/Verbindungsaufbau.cs` | 116 | Eine aufgebaute Steuerverbindung: der Datenstrom nach dem Handshake (bei TLS der SslStream), dazu die IP-Adresse |
| `src/Funk.ClientKern/Verwaltungsteil.cs` | 82 | Die schmale Naht zum Verwaltungsteil (A45, ANMELDUNG-SICHERHEIT "Admin-Teile nur für Admins"). Der normale Client kennt |
| `src/Funk.ClientKern/Wiederverbindungsplan.cs` | 107 | Phase der Wiederverbindung (7.1). |
| `src/Funk.ClientKern/Zustandsmelder.cs` | 112 | C5: Zustandsmeldungen (SendeauswahlSetzen, HoerenSetzen, PegelSetzen, SprechanzeigeAbo) gehen nur mit dem letzten Stand |
| `src/Funk.Echo/Hilfe.cs` | 29 | Aufrufhilfe des Echo-Funkers. |
| `src/Funk.Echo/Program.cs` | 180 |  |
| `src/Funk.Echo/Schluesseldatei.cs` | 56 | Geräteschlüssel des Echo-Funkers als PKCS#8-PEM-Datei (ECDSA P-256) für den Betrieb neben dem Server: Fehlt die Datei, |
| `src/Funk.Klang/Eingabe/DirectInputGeraete.cs` | 361 | Die Geräteschicht über DirectInput 8 im Lesethread, gebaut nach dem DirectInput-Kern des JG Signal Monitor |
| `src/Funk.Klang/Eingabe/DirectInputNativ.cs` | 390 | DirectInput 8 und die wenigen Windows-Aufrufe drumherum, nur für das Lesen von Spielgeräten. Werte und Aufbau aus |
| `src/Funk.Klang/Eingabe/DirectInputSprechtaste.cs` | 712 | Sprechtaste auf Joystick, Throttle, Pedalen und jedem anderen Spielgerät (A3) über DirectInput 8, gelesen wie ein |
| `src/Funk.Klang/Eingabe/DirectInputSuche.cs` | 109 | Aufzählen der Spielgeräte über DirectInput 8 (DI8DEVCLASS_GAMECTRL), im eigenen Suchthread mit eigenem |
| `src/Funk.Klang/Eingabe/Joystickauswertung.cs` | 696 | Zustand der Joystick-Sprechtaste über der Geräteschicht, ohne eigenen Thread und ohne eigene Uhr: offene Geräte |
| `src/Funk.Klang/Eingabe/Joystickeingabe.cs` | 168 | Joystick-Belegungen in : 1 bis 128 sind Knöpfe, gezählt wie in der |
| `src/Funk.Klang/Eingabe/RawInputAuswertung.cs` | 349 | Eine Taste oder ein Mausknopf ging hinunter oder hinauf. |
| `src/Funk.Klang/Eingabe/RawInputNativ.cs` | 94 | Die wenigen user32-Aufrufe für Raw Input: ein Nachrichtenfenster (Klasse "Message", nie sichtbar), Anmeldung mit |
| `src/Funk.Klang/Eingabe/RawInputSprechtaste.cs` | 379 | Globale Tasten über Raw Input für Tastatur und Maus (A3, A5): Sprechtaste mit beliebig vielen Belegungen und die |
| `src/Funk.Klang/Eingabe/SprechtastenVerbund.cs` | 255 | Mehrere Tastenquellen hinter einer: Raw Input für Tastatur und Maus, nach dem Merge DirectInput für Joysticks. Eine |
| `src/Funk.Klang/Eingabe/Tastenbelegung.cs` | 162 | Woher eine Sprechtaste kommt. |
| `src/Funk.Klang/Geraete/Ausgabewandler.cs` | 107 | Füllt den Puffer eines Wiedergabegeräts (beliebige Rate und Kanalzahl, verschränkt) aus einer Stereoquelle mit |
| `src/Funk.Klang/Geraete/Eingangswandler.cs` | 105 | Macht aus den Daten eines Aufnahmegeräts (beliebige Rate und Kanalzahl, verschränkt) 10-ms-Rahmen mit 48 kHz mono: |
| `src/Funk.Klang/Geraete/IAudioSystem.cs` | 57 | Format eines geöffneten Stroms: float32, verschränkt, Abtastrate und Kanalzahl des Geräts. |
| `src/Funk.Klang/Geraete/WasapiSystem.cs` | 301 | WASAPI über NAudio 3 (, ): geteilter Modus, ereignisgesteuert, |
| `src/Funk.Klang/Kern/DeepFilterProzessor.cs` | 210 | Schwellen der Rechenstufen von libDF nach dem lokalen SNR eines Rahmens: unter  gilt er als reines |
| `src/Funk.Klang/Kern/EchoProzessor.cs` | 291 | Stufe der WebRTC-Rauschunterdrückung; die Werte entsprechen apm_configure. |
| `src/Funk.Klang/Kern/Endlichkeit.cs` | 73 | Schutz der nativen Bibliotheken vor NaN und Unendlich. Gemessen (Gegenprüfung): Ein einziges NaN |
| `src/Funk.Klang/Kern/LaengenPruefung.cs` | 43 | Längenprüfungen für Spans, bevor ein Zeiger an eine native Bibliothek geht. Der Normalfall |
| `src/Funk.Klang/Kern/NativeBibliotheken.cs` | 42 | Startprüfung der vier nativen Bibliotheken: lädt jede über einen harmlosen Aufruf und vergleicht |
| `src/Funk.Klang/Kern/OpusDekodierer.cs` | 215 | Opus-Dekodierer für 48 kHz mono, eingestellt nach ENTWURF: Decoder-Komplexität 7 (Deep PLC ab 5, |
| `src/Funk.Klang/Kern/OpusKodierer.cs` | 194 | Opus-Anwendungsmodus. Welcher für die Org besser klingt, entscheidet der Hörtest (ENTWURF, Codec). |
| `src/Funk.Klang/Kern/OpusPaket.cs` | 133 | Kodiermodus eines Opus-Rahmens laut TOC-Byte (RFC 6716, Abschnitt 3.1). |
| `src/Funk.Klang/Kern/OpusSoftClip.cs` | 50 | opus_pcm_soft_clip als letzte Stufe hinter dem Mischer (ENTWURF: nie hart clippen). Hält |
| `src/Funk.Klang/Kern/Resampler.cs` | 213 | speexdsp-Resampler (Float, SSE), für 44,1-kHz-Geräte, den 16-kHz-Zweig und die Feinkorrektur der |
| `src/Funk.Klang/Kern/RnnoiseProzessor.cs` | 122 | RNNoise-Rauschunterdrückung, 48 kHz mono, fest 480 Samples (10 ms) je Rahmen, eingebautes |
| `src/Funk.Klang/Kern/RnnoiseSchaetzer.cs` | 20 | Sprachwahrscheinlichkeit aus RNNoise als  (Klangstufe C2a: Sprachanteil eines Funkers, der lange |
| `src/Funk.Klang/Kern/Schnittstellen.cs` | 13 |  |
| `src/Funk.Klang/Maschine/Echoschutz.cs` | 197 | Echoschutz der Sprachaktivierung ohne Headset (A14): AEC3 dämpft das Echo der Lautsprecher stark, aber ein Rest |
| `src/Funk.Klang/Maschine/Empfangsseite.cs` | 288 | Empfangsanzeige eines Sprechers (A18): Konto, Mischer-Netz, lautester 10-ms-Block seit der letzten Abfrage in dBFS |
| `src/Funk.Klang/Maschine/GateFuehrung.cs` | 257 | Führt das  nach den drei Modi der  (A26 bis A28), je |
| `src/Funk.Klang/Maschine/GateVorgaben.cs` | 146 | Bedeutung, Voreinstellung und Grenzen der  (A26 bis A28). Alle Pegel sind dBFS über |
| `src/Funk.Klang/Maschine/IKlangMaschine.cs` | 265 | Einstellung der Sprachaktivierung; Bedeutung, Voreinstellung und Grenzen in . |
| `src/Funk.Klang/Maschine/KlangMaschine.cs` | 1049 | Zähler der Klangmaschine, als Momentaufnahme ohne Gleichzeitigkeitsgarantie zwischen den Feldern. |
| `src/Funk.Klang/Maschine/KlangMaschine.Geraete.cs` | 314 | Geräte der Klangmaschine, nur im Steuer-Thread (ohne eigene Threads: im Aufrufer): Ströme öffnen und schließen, Gerätewechsel |
| `src/Funk.Klang/Maschine/KlangMaschinenOptionen.cs` | 88 | Einstellungen der , die zur Laufzeit fest bleiben. Die Voreinstellung ist das Produkt. |
| `src/Funk.Klang/Maschine/Klangprotokoll.cs` | 169 | Klangprotokoll (Klangstufe A, nur auf Wunsch): schreibt die Telemetrie der Sendekette als CSV in eine Datei |
| `src/Funk.Klang/Maschine/Klangtelemetrie.cs` | 93 | Stufen der Sendekette, deren Laufzeit je 10-ms-Rahmen gemessen wird (Klangstufe A). |
| `src/Funk.Klang/Maschine/Lernfreigabe.cs` | 31 | Was die  über einen Rahmen weiß, wenn sie entscheidet, ob die  aus ihm lernen darf. |
| `src/Funk.Klang/Maschine/Rahmenjournal.cs` | 88 | Merkmale eines Aufnahmerahmens im . |
| `src/Funk.Klang/Maschine/RahmenRing.cs` | 81 | Übergabe von 10-ms-Rahmen (480 Werte, 48 kHz mono) von genau einem Schreiber an genau einen Leser, ohne Sperre und |
| `src/Funk.Klang/Maschine/Rauschfilterwahl.cs` | 144 | Rauschfilter der Sendekette (Klangstufe B): genau einer wirkt (Recherche Abschnitt 1 und 2). |
| `src/Funk.Klang/Maschine/SendeAusgang.cs` | 47 | Ein kodiertes Sprachpaket der eigenen Aufnahme (20 ms Opus, höchstens 400 Byte) oder die Ende-Markierung einer |
| `src/Funk.Klang/Maschine/Sendekette.cs` | 1265 | Die Aufnahmekette je 10-ms-Rahmen im Aufnahme-Thread: Echoreferenz (mit eigener RNNoise-Sprachschätzung für den |
| `src/Funk.Klang/Maschine/Steuerschleife.cs` | 125 | Eigener Steuer-Thread (MTA) für alles, was Geräte anfasst: Öffnen, Schließen, Gerätewechsel, Aufräumen. So laufen |
| `src/Funk.Klang/Maschine/Telemetriejournal.cs` | 120 | Telemetrie der Sendekette: die letzten  Rahmen (gut 10 s) als  und je |
| `src/Funk.Klang/Nativ/ApmNativ.cs` | 99 | Rohe Einsprungpunkte von webrtc_apm.dll (eigene C-Schicht apm_c 1.1 über webrtc-audio-processing 2.1, |
| `src/Funk.Klang/Nativ/DeepFilterNativ.cs` | 82 | Rohe Einsprungpunkte von deepfilternet.dll (eigene C-Schicht über libDF, DeepFilterNet 0.5.6 mit fest einkompiliertem |
| `src/Funk.Klang/Nativ/OpusNativ.cs` | 199 | Rohe Einsprungpunkte von opus.dll (libopus 1.6.1, eigener Bau, siehe native\BAUBERICHT.md). |
| `src/Funk.Klang/Nativ/RnnoiseNativ.cs` | 67 | Rohe Einsprungpunkte von rnnoise.dll (Eigenbau, Standardmodell fest einkompiliert). |
| `src/Funk.Klang/Nativ/SpeexNativ.cs` | 109 | Rohe Einsprungpunkte von speexdsp_resampler.dll (speexdsp 1.2.1, nur der Resampler, |
| `src/Funk.Klang/Verarbeitung/Begrenzer.cs` | 165 | Kennlinie und Zeitverhalten des Begrenzers. |
| `src/Funk.Klang/Verarbeitung/Biquad.cs` | 88 | Filter 2. Ordnung nach dem Audio-EQ-Kochbuch von Robert Bristow-Johnson (als W3C Note "Audio EQ Cookbook" veröffentlicht), bei |
| `src/Funk.Klang/Verarbeitung/EigenerWaechter.cs` | 735 | Was der Wächter der eigenen Seite je Aufnahmerahmen (10 ms) sieht. |
| `src/Funk.Klang/Verarbeitung/EmpfangsWaechter.cs` | 463 | Der Wächter der Empfängerseite im  (Klangstufe C2a, Entwurf "Was Dauersenden ist", letzte Zeile, und Q29): je |
| `src/Funk.Klang/Verarbeitung/FunkKlick.cs` | 327 | Taste gedrückt, Beginn einer Sprechphase. |
| `src/Funk.Klang/Verarbeitung/FunkSpur.cs` | 55 | Was der eigene Mischer an Funk spielt, je 10-ms-Block (Klangstufe C2a): Pegel der Funk-Wiedergabe (ohne Klick und Hörprobe, mit allen |
| `src/Funk.Klang/Verarbeitung/GateKennzahlen.cs` | 133 | Markierte Äußerung in Samples (48 kHz), Ende ausschließlich. Start ist der hörbare Anlaut. |
| `src/Funk.Klang/Verarbeitung/GateStatistik.cs` | 113 | Rauschboden nach dem Prinzip der Minimum-Statistik (Martin 2001), vereinfacht auf den Gesamtpegel: |
| `src/Funk.Klang/Verarbeitung/Jitterpuffer.cs` | 759 | Einstellungen des Jitterpuffers; Zahlen aus codec-ausgabe.md (B19, Empfehlung 4) und FUNKPROTOKOLL-v1 (C6). |
| `src/Funk.Klang/Verarbeitung/Klangfarbe.cs` | 310 | Einstellung der  (Q70 bis Q72, seit 0.9.3): Wärme, Klarheit und Schärfe in ganzen dB von  bis |
| `src/Funk.Klang/Verarbeitung/Laufzeitstatistik.cs` | 182 | Laufzeitstatistik eines Senders nach dem Prinzip von WebRTC NetEQ (DelayManager, UnderrunOptimizer): |
| `src/Funk.Klang/Verarbeitung/Mindesteinmessung.cs` | 249 | Wo das Einmessen der Mindest-Funklautstärke steht. |
| `src/Funk.Klang/Verarbeitung/Mischer.cs` | 625 | 1 = Mono (Panorama wirkungslos), 2 = Stereo. Weitere Gerätekanäle werden mit 0 gefüllt. |
| `src/Funk.Klang/Verarbeitung/PaketRing.cs` | 87 | Übergabe von Sprachpaketen vom Netz-Thread (genau ein Schreiber) an den Audio-Thread (genau ein |
| `src/Funk.Klang/Verarbeitung/Pegel.cs` | 52 | Festes Arbeitsformat der Verarbeitung: float32, 48 kHz, mono je Strom. |
| `src/Funk.Klang/Verarbeitung/Pegelstufe.cs` | 593 | Lautstärke der eigenen Stimme vor dem Senden. : Halteregel der ; |
| `src/Funk.Klang/Verarbeitung/Sendeaufbereitung.cs` | 177 | Sendeseite zwischen Gate und Transport: nimmt 10-ms-Rahmen, lässt sie durch das , |
| `src/Funk.Klang/Verarbeitung/Signalton.cs` | 130 | Welcher Signalton des Meldesystems (seit 0.12.0). |
| `src/Funk.Klang/Verarbeitung/SprachGate.cs` | 647 | Sprache bestätigt, aber noch nicht laut genug für die Mindest-Funklautstärke (, Klangstufe C1): |
| `src/Funk.Klang/Verarbeitung/Sprachkennzeichen.cs` | 55 | Kennzeichen eines Sprachpakets, bitgleich mit FUNKPROTOKOLL-v1 Abschnitt 5.6. |
| `src/Funk.Klang/Verarbeitung/Stimmhaftigkeit.cs` | 134 | Liefert je 10-ms-Rahmen (480 Samples, 48 kHz) eine Sprachwahrscheinlichkeit 0..1. |
| `src/Funk.Klang/Verarbeitung/Testzugang.cs` | 6 |  |
| `src/Funk.Klang/Verarbeitung/Waechter.cs` | 575 | Urteil des Wächters der Auto Dauersend-Dämpfung (Klangstufe C2a, KLANGSTUFE-C-ENTWURF.md, "Was Dauersenden ist"). Auf der eigenen |
| `src/Funk.Klang/Verarbeitung/WeichesClipping.cs` | 94 | Weiches Clipping mit derselben Kennlinie wie opus_pcm_soft_clip aus libopus 1.6.1 (src/opus.c, Funktion |
| `src/Funk.Klang/Verarbeitung/Zeitdehnung.cs` | 125 | Zeitkürzen und Zeitdehnen um eine Grundperiode (Accelerate / Preemptive Expand nach dem Prinzip von |
| `src/Funk.Protokoll/Abzeichen/Abzeichenkatalog.cs` | 182 | Ein Rang einer Laufbahn: Kennung ("MARINE_04"), Name ("Sergeant"), Laufbahn und Stufe (1-basiert). |
| `src/Funk.Protokoll/Identitaet/Adminnachweis.cs` | 104 | Wofür eine Unterschrift des Admin-Schlüssels gilt (3.9). Der Zweck steht in den unterschriebenen Bytes. |
| `src/Funk.Protokoll/Identitaet/Einmalcode.cs` | 101 | Einmalcode der Registrierung (3.4, Vektor 8.6): 12 Zeichen Crockford-Base32, also 60 bit aus |
| `src/Funk.Protokoll/Identitaet/EinmalcodeRegister.cs` | 241 | Woran ein Einmalcode gebunden ist: Verbindung, Fingerabdruck, Gerätename und gemeldete Schlüsselart. |
| `src/Funk.Protokoll/Identitaet/Fingerabdruck.cs` | 173 | SPKI-Fingerabdruck: SHA-256 über das DER-kodierte SubjectPublicKeyInfo (3.1, 3.2, Vektor 8.5). |
| `src/Funk.Protokoll/Identitaet/Geraeteschluessel.cs` | 199 | Wo der private Geräteschlüssel liegt, laut Client (3.2). Der Server kann das nicht prüfen. |
| `src/Funk.Protokoll/Identitaet/Geraetezertifikat.cs` | 76 | Selbstsigniertes Gerätezertifikat nach 3.2 und die Annahmeprüfung des Servers. Der Server prüft weder |
| `src/Funk.Protokoll/Identitaet/Hilfen.cs` | 257 | 32 Byte (SHA-256) als Wertetyp, für Wörterbuchschlüssel ohne Heap-Anteil. |
| `src/Funk.Protokoll/Identitaet/Kontenbuch.Antraege.cs` | 122 | Ergebnis eines Antrags ohne Code (3.4): Konto und Gerät in WARTET oder ein Fehler. |
| `src/Funk.Protokoll/Identitaet/Kontenbuch.cs` | 474 | Konten und Geräte als reine Logik (3.3 bis 3.6): Einstufung nach Hello, Verknüpfung per Einmalcode, |
| `src/Funk.Protokoll/Identitaet/Kontenbuch.Geraete.cs` | 424 | Antwort auf GeraeteListe (4.5), höchstens 500 Einträge, älteste zuerst. |
| `src/Funk.Protokoll/Identitaet/Kontenbuch.Umhaengen.cs` | 28 | Nur an der Konsole: ein freigegebenes Gerät an ein anderes, aktives Konto hängen. Für die Reparatur, wenn ein Antrag als |
| `src/Funk.Protokoll/Identitaet/Kontenbuch.Verwaltung.cs` | 364 | Ergebnis von ProfilSetzen, KontoVerwaltung SCNAMEN und DISCORDNAME: die bereinigten Namen oder der |
| `src/Funk.Protokoll/Identitaet/Kontovorschlag.cs` | 40 | Ob ein Antrag zu einem bestehenden Konto passen könnte (Sarevocs Fall vom 01.10.2026: Antrag mit seinem SC- und Discord-Namen neben dem |
| `src/Funk.Protokoll/Identitaet/Modell.cs` | 481 | Stufe eines Kontos, zugleich der Rang: FUNKER &lt; RAIDLEITER &lt; ADMIN (6.10). |
| `src/Funk.Protokoll/Identitaet/Namensregeln.cs` | 29 | Die Namensregeln des Servers für die Anmeldung (A42, 3.3) nach außen, damit Client und Setup dasselbe prüfen wie der |
| `src/Funk.Protokoll/Identitaet/Serverprofil.cs` | 350 | Ein Host des Serverprofils mit den erlaubten Adressfamilien. |
| `src/Funk.Protokoll/Identitaet/SigniertesProfil.cs` | 258 | Signiertes Serverprofil {daten: b64, signatur: b64} (3.1, Vektor 8.11). Geprüft wird über die |
| `src/Funk.Protokoll/Identitaet/Wirkungen.cs` | 123 | Folge einer Entscheidung, die der Server ausführt (Nachricht, Trennung, Bot-Meldung). Das Modul selbst hat |
| `src/Funk.Protokoll/Meldungen/Meldungskatalog.cs` | 177 | Art eines Meldungsmotivs (Meldesystem 0.12.0). |
| `src/Funk.Protokoll/Schutz/Nonce.cs` | 25 | Nonce für AES-GCM nach RFC 8446 Abschnitt 5.3: der 64-Bit-Zähler in Netzwerk-Byte-Reihenfolge, |
| `src/Funk.Protokoll/Schutz/ReplayFenster.cs` | 102 | Gleitendes Replay-Fenster (5.9, Vektor 8.7) nach RFC 6479: Bitmaske als Ringpuffer aus 64-Bit-Blöcken. |
| `src/Funk.Protokoll/Schutz/TokenEimer.cs` | 41 | Token-Eimer als Ratenbremse: füllt sich mit proSekunde bis kapazitaet auf, |
| `src/Funk.Protokoll/Sprache/Datagramm.cs` | 45 | Versiegeln und Öffnen eines Datagramms (5.2, 5.7): Kopf (16, AAD) // Chiffrat (n) // Tag (16). |
| `src/Funk.Protokoll/Sprache/EmpfangsKanal.cs` | 191 | Empfangsrichtung einer Sitzung: angenommene Epochen (5.8) und das gemeinsame Replay-Fenster über alle |
| `src/Funk.Protokoll/Sprache/Empfangspruefung.cs` | 159 | Weg, auf dem ein Datagramm kam: UDP oder SpracheTunnel (0x8001) im Steuerkanal. |
| `src/Funk.Protokoll/Sprache/Kennzeichen.cs` | 66 | Kennzeichen-Byte der Sprachpakete (5.6). Bits 4-7 sind reserviert. |
| `src/Funk.Protokoll/Sprache/OpusPruefung.cs` | 58 | Befund der Opus-Prüfung (5.13). |
| `src/Funk.Protokoll/Sprache/PaketTyp.cs` | 50 | Pakettypen des Sprachkanals (5.3). |
| `src/Funk.Protokoll/Sprache/RichtungsSchluessel.cs` | 102 | Schlüssel und statischer IV einer Richtung in einer Epoche (5.7, 5.8): AEAD_AES_256_GCM mit 32-Byte-Schlüssel, |
| `src/Funk.Protokoll/Sprache/Rotationsgrenzen.cs` | 41 | Grenzen und Fristen für Epochen und Rotation (5.8). Die Voreinstellung ist die Spezifikation; |
| `src/Funk.Protokoll/Sprache/SendeKanal.cs` | 103 | Senderichtung einer Sitzung (5.7, 5.8): ein einziger, streng steigender Zähler ab 0 über alle Epochen und |
| `src/Funk.Protokoll/Sprache/ServerSchluesselbund.cs` | 152 | Warum der Server rotieren will (5.8). |
| `src/Funk.Protokoll/Sprache/SpracheHoch.cs` | 138 | Klartext SPRACHE_HOCH (5.4) als Sicht auf den Puffer, ohne Kopie. |
| `src/Funk.Protokoll/Sprache/SpracheRunter.cs` | 109 | Klartext SPRACHE_RUNTER (5.5), vom Server gestempelt, als Sicht auf den Puffer ohne Kopie. |
| `src/Funk.Protokoll/Sprache/Sprachkanal.cs` | 61 | Feste Größen und Grenzen des Sprachkanals (Kapitel 5). |
| `src/Funk.Protokoll/Sprache/SprachKopf.cs` | 41 | Gemeinsamer Kopf aller Sprachdatagramme (5.2), 16 Byte, vollständig AAD. |
| `src/Funk.Protokoll/Sprache/SprechphasenGeber.cs` | 117 | Nummern und Kennzeichen eines zu sendenden Frames. |
| `src/Funk.Protokoll/Sprache/Steuerpakete.cs` | 79 | Klartexte der festen Typen 0x01-0x03 (5.3), je 16 Byte. Füllbytes sind 0; die Formate sind kanonisch (Z8), |
| `src/Funk.Protokoll/Steuerung/Anfragen.cs` | 132 | Eine offene eigene Anfrage.  trägt etwa die angefragte Epoche bei VoiceKeys. |
| `src/Funk.Protokoll/Steuerung/Auftragszeit.cs` | 72 | Zeiten eines Auftrags (6.12) in der Ortszeit der Gilde: Voreinstellung für das Ende ist der nächste Zeitpunkt 04:00 Uhr nach |
| `src/Funk.Protokoll/Steuerung/Binaer.cs` | 124 | VoiceKeys.grund. |
| `src/Funk.Protokoll/Steuerung/ClientAutomat.cs` | 194 | Zustände des Clients (4.6). |
| `src/Funk.Protokoll/Steuerung/Fehlercode.cs` | 93 | Fehlercodes des Steuerkanals nach 4.9. Drahtform: Name in GROSS_MIT_UNTERSTRICH. |
| `src/Funk.Protokoll/Steuerung/Flottenvorlagen.cs` | 219 | Eine Annahme einer Vorlage, die der Admin prüfen soll (auf der Flottenseite gelb markiert). |
| `src/Funk.Protokoll/Steuerung/Grenzen.cs` | 228 | Feld- und Anzahlgrenzen der Bodies aus 4.5 und 1.2. |
| `src/Funk.Protokoll/Steuerung/JsonVorpruefung.cs` | 148 | Strenger Vorlauf über einen JSON-Body vor der Deserialisierung (4.3). Er trennt die beiden Fehlerklassen: |
| `src/Funk.Protokoll/Steuerung/Kanal.cs` | 307 | Was mit einem eingegangenen Rahmen geschieht. |
| `src/Funk.Protokoll/Steuerung/Kanalrechnung.cs` | 97 | Kanäle mit Ch. (6.16, seit 0.6.0): Ein Kanal ist das Paar (Kanalnetz, Ch.) mit der berechneten Kanal-ID |
| `src/Funk.Protokoll/Steuerung/Katalog.cs` | 214 | Wer einen Rahmen sendet. |
| `src/Funk.Protokoll/Steuerung/Konverter.cs` | 163 | Strenger Enum-Konverter für die Drahtform GROSS_MIT_UNTERSTRICH (4.3, 4.5): nur exakte Zeichenketten, |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Anmeldung.cs` | 536 | Leerer Body {}; unbekannte Felder werden übergangen. |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Aufstellung.cs` | 293 | Eintrag einer Aufstellung: je Eintrag kontoId oder discordId. |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Aufzaehlungen.cs` | 164 | Schlüsselart laut Client (3.2), ungeprüft. |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Betrieb.cs` | 302 | 0x0050 DurchsageStart (S->C, nur Anzeige). |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Funkbetrieb.cs` | 295 | 0x0030 SendeauswahlSetzen. Antwort {}. |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Lage.cs` | 501 | 0x0021 NetzListe (S->C, immer vollständig). |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Meldesystem.cs` | 262 | Was eine  tut (art, offener Text). Ein Server, der die Art nicht kennt, antwortet |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Phasen.cs` | 147 | 0x003C Funkermodus (C->S, 6.15): Ein ADMIN schaltet für das eigene Konto "Heute als Funker" ein oder aus, ohne |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Raidleitung.cs` | 206 | 0x0017 Beauftragung (C->S, 6.12): Auftrag für die Einsatzleitung. ERTEILEN und ENTZIEHEN nur mit KONTEN_VERWALTEN in |
| `src/Funk.Protokoll/Steuerung/Nachrichten/Verwaltung.cs` | 620 | 0x0018 ProfilSetzen (C->S, selbst): SC-Namen 1 bis 3, Discord-Name, "Mein Zeichen zeigen" (N2, F11) und "Meine Angaben in |
| `src/Funk.Protokoll/Steuerung/Nachrichtentyp.cs` | 123 | Nachrichtentypen des Steuerkanals (4.4). 0x0070 ist frei und darf nicht verwendet werden. |
| `src/Funk.Protokoll/Steuerung/Pruefung.cs` | 208 | Ein JSON-Body, der nach dem Deserialisieren seine Feldregeln aus 4.5 prüft. |
| `src/Funk.Protokoll/Steuerung/Rahmen.cs` | 148 | Kopf eines Steuerrahmens (4.2): Länge u32 (8 + Body), Rahmenversion u8, Flags u8, Typ u16, Anfrage-Nr u32, |
| `src/Funk.Protokoll/Steuerung/RahmenLeser.cs` | 146 | Ausgang eines Leseversuchs. |
| `src/Funk.Protokoll/Steuerung/RahmenSchreiber.cs` | 168 | Schreibt Steuerrahmen auf einen : Kopf und Body liegen in einem Puffer aus dem Pool und |
| `src/Funk.Protokoll/Steuerung/Sendewarteschlange.cs` | 135 | Die zwei Warteschlangen der Schreibschleife je Sitzung (4.2), ohne Netzwerk: Steuerrahmen gehen vor. |
| `src/Funk.Protokoll/Steuerung/ServerAutomat.cs` | 207 | Zustände einer Verbindung im Server (4.6). |
| `src/Funk.Protokoll/Steuerung/Steuerbremse.cs` | 296 | Rate eines Token-Eimers: Marken je Sekunde und Stoß (Burst). |
| `src/Funk.Protokoll/Steuerung/SteuerJson.cs` | 248 | Ergebnis beim Lesen eines Bodys: entweder  oder ein Fehlercode. |
| `src/Funk.Protokoll/Steuerung/Textregeln.cs` | 99 | Regeln für Zeichenketten nach 4.3: NFC, keine Steuerzeichen (U+0000-U+001F, U+007F-U+009F), keine |
| `src/Funk.Protokoll/Steuerung/Verdraengungsbuch.cs` | 92 | Was mit einer älteren Sitzung desselben Kontos geschieht (4.6). |
| `src/Funk.Protokoll/Steuerung/Versionen.cs` | 187 | Versionierung nach 4.7. |
| `src/Funk.Server/Betrieb/Ablageformat.cs` | 44 | Gemeinsame JSON-Einstellungen der Zustandsdateien. Sie sind serverintern, kein Drahtformat. |
| `src/Funk.Server/Betrieb/Adresse.cs` | 106 | IP-Adresse mit Port als Wertetyp, allokationsfrei aus einer  gelesen. IPv4-Adressen in |
| `src/Funk.Server/Betrieb/Dateiablage.cs` | 99 | Dateien im Zustandsverzeichnis mit atomarem Ersetzen (Offener Punkt 7: eigene JSON-Dateien): erst in eine |
| `src/Funk.Server/Betrieb/Geheimnis.cs` | 60 | Das Betriebsgeheimnis des Servers (per systemd LoadCredential, nie in der Konfiguration): mindestens 32 |
| `src/Funk.Server/Betrieb/Journal.cs` | 173 | Ein Eintrag im Journal: Zeit (ms seit 1970), Art, wer (Konto, 0 = Konsole), welches Konto, Klartext. |
| `src/Funk.Server/Betrieb/Konfiguration.cs` | 316 | Konfiguration des Servers als JSON-Datei. Sie enthält keine Geheimnisse: Serverschlüssel und Betriebsgeheimnis |
| `src/Funk.Server/Betrieb/Logbuch.cs` | 141 | Stufen des Serverprotokolls. |
| `src/Funk.Server/Betrieb/Profilabgleich.cs` | 57 | Abgleich der server.json mit dem Serverprofil, das Welcome durchreicht (3.1). Die Clients nehmen Host und TCP-Port aus |
| `src/Funk.Server/Betrieb/ServerIdentitaet.cs` | 121 | Identität des Servers (3.1): langlebiger ECDSA-P-256-Schlüssel mit selbstsigniertem Zertifikat. Die Clients prüfen |
| `src/Funk.Server/Betrieb/SperrlistenKonfiguration.cs` | 95 | Eingebaute Sperrliste (A51, Schutz gegen Botnetze, Scanner und Durchprobieren) als Abschnitt sperrliste in |
| `src/Funk.Server/Betrieb/TotpSchluessel.cs` | 44 | Der Schlüssel funk-totp-schluessel (3.9): genau 32 Zufallsbytes, per systemd LoadCredential wie der |
| `src/Funk.Server/Betrieb/Uhrpruefung.cs` | 83 | Was der Kernel über die Uhr sagt (adjtimex): Zustand (5 = TIME_ERROR) und höchste Abweichung in Mikrosekunden. |
| `src/Funk.Server/Betrieb/Zeit.cs` | 39 | Die drei Zeitbasen des Servers. Alle Grenzen und Fristen laufen auf der monotonen Uhr (4.8); die Wanduhr |
| `src/Funk.Server/Dienst.cs` | 203 | Der lauffähige Dienst: liest Geheimnis und Identität, lädt den Zustand, bindet TCP und UDP je konfigurierter Adresse |
| `src/Funk.Server/Identitaet/Adminbuch.cs` | 551 | Zustand des Admin-Nachweises eines Kontos (3.9). |
| `src/Funk.Server/Identitaet/Mitgliederbuch.cs` | 750 | Klasse einer Raid-Helper-Anmeldung, auf die eine Katalogrolle zeigt (erst mit S4 benutzt). |
| `src/Funk.Server/Identitaet/Totp.cs` | 194 | Einmalpasswort nach RFC 6238 (TOTP) über RFC 4226 (HOTP) mit den Standardwerten, die jede App versteht, auch |
| `src/Funk.Server/Identitaet/TotpTresor.cs` | 103 | Verschlüsselung der TOTP-Geheimnisse und Prüfwerte der Notfallcodes am Server (3.9): AES-256-GCM mit dem Schlüssel |
| `src/Funk.Server/Kern/Antwort.cs` | 63 | Antwort auf eine Anfrage des Clients: entweder ein Body des Antworttyps (gleicher Typ, Flag ANTWORT) oder |
| `src/Funk.Server/Kern/Komfortablage.cs` | 141 | Komfortzustand am Konto (6.2, 7.2): Pegelvoreinstellungen (A4), Ablage (A21), die zuletzt gemeldete Sendeauswahl |
| `src/Funk.Server/Kern/Meldesystem.cs` | 223 | Was eine Meldung bewirkt hat.  mit  (Klartext für Fehler.text), sonst |
| `src/Funk.Server/Kern/RelaisBruecke.cs` | 121 | Brücke vom Relay-Thread zum Steuerkanal: stellt die Nachrichten des Sprachpfads der Verbindung der jeweiligen |
| `src/Funk.Server/Kern/Zentrale.Admin.cs` | 642 | Admin-Sitzung und Schritt-Bestätigung (3.9): Nonce, Admin-Anmeldung mit Unterschrift des Admin-Schlüssels und Code aus |
| `src/Funk.Server/Kern/Zentrale.Anfragen.cs` | 683 | Anfragen des Clients in AKTIV (4.4, 4.5). Die Rechte prüfen die Module am aktuellen Stand (6.10, 3.5, 3.6); ohne |
| `src/Funk.Server/Kern/Zentrale.Anmeldung.cs` | 350 | Einstufung nach Hello samt Welcome für WARTET (4.5, 4.6) und der Mindestfassung, die gerade gilt (). |
| `src/Funk.Server/Kern/Zentrale.cs` | 761 | Der eine Schreiber des Servers (6.2): Kontenbuch, Einmalcodes, Zustandsschreiber und Komfortzustand gehören ihm und |
| `src/Funk.Server/Kern/Zentrale.Kanalsperren.cs` | 190 | Kanalsperren (0x003A, 6.17, seit 0.6.0) am Steuerkanal: SETZEN, AUFHEBEN und LISTE für Sitzungen mit ZUWEISEN, ohne bis |
| `src/Funk.Server/Kern/Zentrale.Meldungen.cs` | 119 | Meldesystem (0.12.0, Sarevoc 11.10. und 16.10.2026, M1-M24, mit der Textmeldung): MeldungSenden (0x0026) und BereitMelden (0x0027), die Pushes... |
| `src/Funk.Server/Kern/Zentrale.Mitglieder.cs` | 379 | Mitglieder, Rollen und "Wer ist da" (6.13, A46-A48): ProfilSetzen, Journal, Rollenkatalog, Mitglied, Lieblingsrollen, |
| `src/Funk.Server/Kern/Zentrale.Raidleitung.cs` | 476 | Raidleitung, Server und Protokoll (Fassung 0.3.0, RAIDLEITUNG-ENTWURF Bauplan S1, M1 und der Serverteil von M3 bis M5): |
| `src/Funk.Server/Kern/Zentrale.Reparatur.cs` | 66 | Antrag ins falsche Konto (Sarevocs Fall vom 01.10.2026): Sarevoc gab sein Gerät per Antrag frei und bekam ein neues Konto mit seinen Namen |
| `src/Funk.Server/Kern/Zentrale.Sperrliste.cs` | 173 | Sperrliste (A51) in der Zentrale: beim Start laden, alle 10 s speichern (nur HMAC-Werte, sperrliste.json), jede |
| `src/Funk.Server/Kern/Zentrale.Verwaltung.cs` | 561 | Befehle des Admin-Werkzeugs (3.8), serverintern über den UNIX-Socket: Konten, Verknüpfen per Code, Geräte |
| `src/Funk.Server/Kern/Zustandsablage.cs` | 286 | Dauerhafte Ablage im Zustandsverzeichnis als eigene JSON-Dateien mit atomarem Ersetzen (Offener Punkt 7, 7.2): |
| `src/Funk.Server/Lage/Arbeitsstand.cs` | 231 | Veränderlicher Arbeitsstand einer Transaktion des Zustandsschreibers über unveränderlichen Sammlungen. |
| `src/Funk.Server/Lage/Begriffe.cs` | 280 | Stufe eines Kontos (6.1). Die Zahl ist der Rang (6.10): FUNKER &lt; RAIDLEITER &lt; ADMIN. |
| `src/Funk.Server/Lage/Ergebnis.cs` | 117 | Fehler zu einem Eintrag einer Anfrage (details.fehler, 4.5). |
| `src/Funk.Server/Lage/Feldregeln.cs` | 427 | Feld aus einer Rolle, samt Markierung sendenStandard für die Sendeauswahl. |
| `src/Funk.Server/Lage/Fristen.cs` | 103 | Zeiten und Grenzen aus Kapitel 6 und 4.5, alle in Millisekunden. |
| `src/Funk.Server/Lage/InterneAuftraege.cs` | 151 | Serverinterne Aufträge an den Zustandsschreiber, die die Anbindung braucht (Erweiterung des Moduls, im Bericht |
| `src/Funk.Server/Lage/Kanaele.cs` | 108 | Was eine Netz-ID im Lagezustand bedeutet (6.16, seit 0.6.0). |
| `src/Funk.Server/Lage/Komfortzustand.cs` | 107 | Komfortzustand (6.2): die zuletzt gemeldete Sendeauswahl je Konto, ohne Austausch des Schnappschusses. |
| `src/Funk.Server/Lage/Modell.cs` | 460 | Ein Netz (2. Begriffe, 4.5 NetzListe). ID 1 ist Global. |
| `src/Funk.Server/Lage/Nebenstand.cs` | 51 | Zuletzt angewandte Aufstellung mit ihren offenen Einträgen zum Nachziehen (6.9). |
| `src/Funk.Server/Lage/Rechte.cs` | 79 | Rechte eines Kontos (6.1). Standard aus der Stufe, Abweichungen als plus/minus (6.10). |
| `src/Funk.Server/Lage/Schnappschuss.cs` | 443 | Unveränderlicher Serverzustand (6.2): Konten, Netze, Schiffe, Rollen, Einstellungen und aktive |
| `src/Funk.Server/Lage/SchnappschussBauer.cs` | 94 | Baut einen Anfangsschnappschuss, etwa beim Laden aus der Ablage oder in Tests. Global (Netz 1) ist |
| `src/Funk.Server/Lage/Senderecht.cs` | 214 | Senderecht je Paket (6.3) als reine Funktion über dem Schnappschuss. Ohne Ratenlimit; das hält der |
| `src/Funk.Server/Lage/Sperrregeln.cs` | 90 | Regeln der Kanalsperren (6.17, seit 0.6.0) für Schreiber, Weiterleitung und Abbildung: was sich sperren lässt, wen eine Sperre für |
| `src/Funk.Server/Lage/Weiterleiter.cs` | 548 | Weiterleitung im Relay-Thread (6.3, 6.4): Form und Ratenlimit, Senderecht, Phasenbuch je Sender, |
| `src/Funk.Server/Lage/Weiterleitungstypen.cs` | 88 | Eingabe der Weiterleitung: "Paket von Sitzung X mit Ziel Y", der Klartext eines SPRACHE_HOCH (5.4) |
| `src/Funk.Server/Lage/Zustandsschreiber.Aufstellung.cs` | 265 | Aufstellungen (6.7) und Nachziehen offener Einträge (6.9). |
| `src/Funk.Server/Lage/Zustandsschreiber.Beauftragung.cs` | 276 | Beauftragung (6.12, RAIDLEITUNG-ENTWURF Regeln 1-10): Auftrag für die Einsatzleitung erteilen, entziehen, abgeben, den |
| `src/Funk.Server/Lage/Zustandsschreiber.cs` | 559 | Ein Auftrag an den Zustandsschreiber. : ZUWEISEN und Verwaltung vor Selbständerungen (6.2). |
| `src/Funk.Server/Lage/Zustandsschreiber.Phasen.cs` | 179 | Einsatzphasen und Funker-Modus (Fassung 0.5.0, RAID-S2-ENTWURF Abschnitte 1 und 3, FUNKPROTOKOLL 6.14 und 6.15). Eine Phase ist |
| `src/Funk.Server/Lage/Zustandsschreiber.Selbst.cs` | 343 | Selbständerungen (6.5, 6.9, 6.16): HoerenSetzen, FeldSetzen, RolleWaehlen, SatzAnwenden, EinsteigenRolleSchiff (Selbstwahl). |
| `src/Funk.Server/Lage/Zustandsschreiber.Sperren.cs` | 209 | Kanalsperren (6.17, seit 0.6.0): setzen und aufheben (ZUWEISEN, Rangregel wie Stummschalten), der Fristen-Takt (Ablauf bis 04:00) |
| `src/Funk.Server/Lage/Zustandsschreiber.Verwaltung.cs` | 458 | Rechteverwaltung (6.10) und Verwaltung von Netzen, Schiffen, Rollen, Einstellungen (4.5 Verwaltung). |
| `src/Funk.Server/Lage/Zustandsschreiber.Zuweisen.cs` | 283 | Aufträge mit ZUWEISEN (6.7, 6.9, 6.10): Verschieben, Stummschalten, PlaetzeFreigeben. |
| `src/Funk.Server/Netzzugang/Sperrliste.cs` | 617 | Warum eine Adresse Punkte auf der Sperrliste bekommt (A51). |
| `src/Funk.Server/Netzzugang/TlsAnnahme.cs` | 294 | TCP-Annahme mit TLS über SslStream (4.1): nur ausdrücklich konfigurierte Adressen, Zulassung vor dem Handshake |
| `src/Funk.Server/Netzzugang/Vorrangspur.cs` | 131 | Vorrangspur (Begriffe, 4.8): Präfixe, von denen in den letzten 14 Tagen eine AKTIV-Sitzung kam. Sie gehen an den |
| `src/Funk.Server/Netzzugang/Zulassung.cs` | 207 | Urteil der Zulassung über eine neue TCP-Verbindung. |
| `src/Funk.Server/Program.cs` | 95 |  |
| `src/Funk.Server/Sprachpfad/Ausgang.cs` | 212 | Ein versiegeltes Datagramm auf dem Weg zum Socket, aus einem Vorrat. |
| `src/Funk.Server/Sprachpfad/Relais.cs` | 821 | Der Sprachpfad des Servers (5.9 bis 5.14, 6.3, 6.4, 7.4, 7.8). Genau ein Relay-Thread verarbeitet alle Datagramme |
| `src/Funk.Server/Sprachpfad/RelaisSitzung.cs` | 299 | Neue UDP-Quelladresse einer Sitzung, die noch geprüft wird (Begriffe, 5.10). |
| `src/Funk.Server/Sprachpfad/RelaisTypen.cs` | 127 | Zähler einer Sitzung, die der Relay-Thread schreibt und die Verbindung für die Ping-Antwort liest (4.5 Ping: |
| `src/Funk.Server/Sprachpfad/Vorfilter.cs` | 52 | Vorfilter je Präfix für Datagramme von fremden Quellen (5.9 Schritt 4): 400 Datagramme/s, Stoß 200, höchstens |
| `src/Funk.Server/Steuer/Abbildung.cs` | 710 | Übersetzung zwischen den Modellen der Module und den Drahtnachrichten des Steuerkanals (4.5). Die drei Module haben |
| `src/Funk.Server/Steuer/Verbindung.cs` | 839 | Warum eine Verbindung wegen eines Verstoßes endete (für die Sperrliste, A51). |
| `src/Funk.Server/Verwaltung/Verwaltungsklient.cs` | 28 | Gegenstück zum : eine Anfrage, eine Antwort, je eine Zeile JSON. |
| `src/Funk.Server/Verwaltung/Verwaltungssocket.cs` | 180 | UNIX-Socket des Admin-Werkzeugs (3.8): keine Netzadresse, nur eine Datei im Zustandsverzeichnis. Unter Linux ist der |
| `src/Funk.Verwaltung/AdminSchluessel.cs` | 110 | Wo der Admin-Schlüssel liegt (ANMELDUNG-SICHERHEIT, "Der Admin-Schlüssel"). Im Betrieb , |
| `src/Funk.Verwaltung/Adminzugang.cs` | 130 | Der Admin-Nachweis von der Client-Seite (3.9), ohne Oberfläche: Nonce holen, mit dem Admin-Schlüssel unterschreiben |
| `src/Funk.Verwaltung/Ansichten/AbzeichenAnsicht.xaml.cs` | 10 | Ansicht der Seite Abzeichen (0.11.7); der Zustand liegt im . |
| `src/Funk.Verwaltung/Ansichten/VerwaltungAnsicht.xaml.cs` | 69 | Ansicht des Bereichs Verwaltung; der Zustand liegt im . |
| `src/Funk.Verwaltung/Ansichten/Zweispalter.cs` | 148 | Zwei Spalten der Verwaltung (Liste links, Karte rechts), die in schmalen Fenstern untereinander rutschen (seit 0.11.1 AP4, Sarevocs Bild vom |
| `src/Funk.Verwaltung/Einstieg.cs` | 79 | Einstieg für den Client (): baut Dienst, Modell und Ansicht über dem Wirt. Der |
| `src/Funk.Verwaltung/Flotte/Flottenentwurf.cs` | 1086 | Der Entwurf der Flotten-Einstellungsseite (A40, RAIDLEITUNG-ENTWURF "Flotten-Einstellungsseite", "Arbeiten im Entwurf"): |
| `src/Funk.Verwaltung/Flotte/Flottenentwurf.Klartext.cs` | 389 | Wohin ein Hinweis der Übersicht "Heute" springt (seit 0.8.0). |
| `src/Funk.Verwaltung/IVerwaltungsdienst.cs` | 220 | Ergebnis einer Anfrage der Verwaltung: ein Wert oder ein Satz für die Anzeige, nie beides. |
| `src/Funk.Verwaltung/Modelle/AbzeichenViewModel.cs` | 409 | Die Seite Abzeichen (0.11.7, Sarevoc 14./15.10.2026): eigener Eintrag ganz unten im Hauptmenü, nur in der Admin-Sitzung. Links die Mitglieder |
| `src/Funk.Verwaltung/Modelle/AntraegeViewModel.cs` | 314 | Bereich Anträge (3.4, 3.5, ANMELDUNG-SICHERHEIT "Regeln für Admins beim Freigeben"): wartende Geräte mit SC-Name, |
| `src/Funk.Verwaltung/Modelle/CodeAbfrage.cs` | 71 | Das kleine Code-Feld für die Schritt-Bestätigung (3.9): Code aus der Authenticator-App, sechs Ziffern, oder ein Notfallcode, |
| `src/Funk.Verwaltung/Modelle/EinsatzleitungViewModel.cs` | 349 | Bereich "Einsatzleitung" der Verwaltung (RAIDLEITUNG-ENTWURF, Flotten-Einstellungsseite, Absatz Einsatzleitung; 6.12): die |
| `src/Funk.Verwaltung/Modelle/FlotteViewModel.cs` | 2249 | Bereich "Flotte" der Verwaltung (A40, RAIDLEITUNG-ENTWURF "Flotten-Einstellungsseite"): Einheiten, Rollen und Netze als |
| `src/Funk.Verwaltung/Modelle/KatalogViewModel.cs` | 184 | Rollenkatalog (A46, N6): was jemand kann, etwa "Medium Fighter Pilot". Bis 64 Einträge mit Name (40) und Gruppe (20). |
| `src/Funk.Verwaltung/Modelle/MitgliederViewModel.cs` | 641 | Bereich Mitglieder (A46, 6.13): Konten und Vormerkungen in einer Liste mit Suche über alle Namen; ein Klick öffnet die |
| `src/Funk.Verwaltung/Modelle/Nachschlag.cs` | 47 | Was die Bereiche der Verwaltung gemeinsam nachschlagen: Rollenkatalog und Mitglieder (für Namen, |
| `src/Funk.Verwaltung/Modelle/VerwaltungViewModel.cs` | 859 | Wo der Admin mit seinem Nachweis steht (3.9); danach richtet sich, was der Bereich zeigt. |
| `src/Funk.Verwaltung/QrBild.cs` | 55 | Zeichnet einen  als Vektor: dunkle Module als Rechtecke (je Zeile zu Läufen zusammengefasst), mit |
| `src/Funk.Verwaltung/QrCode.cs` | 495 | Fehlerkorrekturstufe eines QR-Codes (ISO/IEC 18004): etwa 7, 15, 25 und 30 Prozent. |
| `src/Funk.Verwaltung/Verwaltungsdienst.cs` | 609 | über den laufenden Client () und den Admin-Schlüssel im TPM |
| `src/Funk.Verwaltung/Voraussetzungen.cs` | 189 | Was die Vorprüfung über das TPM sagt. TPM 2.0 ist technisch Pflicht (Sarevocs Entscheidung vom 01.10.2026). |
| `src/Funk.Verwaltung/VoraussetzungenWindows.cs` | 63 | TPM-Prüfung am echten PC, nur lesend: den Microsoft Platform Crypto Provider öffnen (NCryptOpenStorageProvider) und die |
| `tests/Funk.Testclient/Befehlszeile.cs` | 42 | Einfache Befehlszeile: erstes Wort ist der Befehl, dann --name wert oder Schalter --name. |
| `tests/Funk.Testclient/Echoprobe.cs` | 151 | Ein Paket aus dem Echo-Netz mit Empfangszeit. |
| `tests/Funk.Testclient/Einrichtung.cs` | 168 | Frisches Zustandsverzeichnis für einen Lasttest (Befehl last): Serverschlüssel, Reserve-Pin, |
| `tests/Funk.Testclient/Ergebnis.cs` | 102 | Werte eines Empfängers in einem Szenario. |
| `tests/Funk.Testclient/Funker.cs` | 184 | Testrahmen in der Opus-Nutzlast: TOC 0xF8 (CELT FB 20 ms, mono, ein Frame, nach 5.13 gültig), dann Kennung, |
| `tests/Funk.Testclient/Lasttest.cs` | 374 | Einstellungen eines Laufs. |
| `tests/Funk.Testclient/Program.cs` | 196 |  |
| `tests/Funk.Testclient/Szenario.cs` | 160 | Szenarien nach server-sicherheit.md, Befund B20, bei N Funkern (Standard 40): |
| `tests/Funk.Testclient/Taktgeber.cs` | 54 | Gibt alle 20 ms einen Takt wie ein Audio-Encoder. Unter Windows dauert Thread.Sleep(1) bis zu 15,6 ms; darum |
| `tests/Funk.Testclient/Werkzeuge.cs` | 139 | Ergebnis eines Programmaufrufs. |
| `tests/Funk.Tests/AbzeichenTests.cs` | 276 | Abzeichen (0.11.7, Sarevoc 14./15.10.2026): der feste Katalog, die Prüfregeln und Rechte des Mitgliederbuchs, die eigene Ansicht |
| `tests/Funk.Tests/AdminEinrichtungTests.cs` | 342 | Einrichten mit funk-admin (A55, EINRICHTEN-UMZUG-ENTWURF): Die Befehle zum Erzeugen ersetzen eine vorhandene Datei nur mit |
| `tests/Funk.Tests/AdminNachweisTests.cs` | 624 | Admin-Nachweis ohne Server (3.9, 8.13 bis 8.15): TOTP nach RFC 6238/4226 mit den Vektoren der RFCs, Base32 nach |
| `tests/Funk.Tests/AdminSitzungTests.cs` | 1032 | Uhr für Zusammenspieltests: die Systemuhr plus ein Versatz, den der Test vorstellt (TOTP-Schritte, Leerlauf der |
| `tests/Funk.Tests/AdminvoraussetzungenTests.cs` | 218 | Vorprüfung vor dem Einrichten der Verwaltung (seit 0.4.1, Sarevocs Entscheidung vom 01.10.2026): TPM 2.0 ist technisch Pflicht, |
| `tests/Funk.Tests/AdminWerkzeugTabelleTests.cs` | 431 | Die Optionstabelle von funk-admin () gegen den Code, der die Optionen liest, und gegen alle |
| `tests/Funk.Tests/AdminWerkzeugTests.cs` | 82 | funk-admin als Kommandozeile gegen einen Testserver: Ein Tippfehler ändert nichts und endet mit Exit 2. Anlass ist die |
| `tests/Funk.Tests/AnbindungAec3Tests.cs` | 157 | AEC3-Konfiguration der C-Schicht 1.1 (apm_aec3_configure, apm_aec3_describe) gegen die echte webrtc_apm.dll: Voreinstellung, |
| `tests/Funk.Tests/AnbindungDeepFilterTests.cs` | 178 | DeepFilterNet3-Anbindung (Klangstufe B) gegen die echte deepfilternet.dll. |
| `tests/Funk.Tests/AnbindungEchoTests.cs` | 150 | APM-Anbindung (AEC3, HPF, NS, AGC2) gegen die echte webrtc_apm.dll. |
| `tests/Funk.Tests/AnbindungGegenpruefungTests.cs` | 658 | Gegenprüfung der Klang-Anbindung: numerische Randfälle (NaN, Unendlich, riesige und subnormale |
| `tests/Funk.Tests/AnbindungMessung.cs` | 179 | Echtzeitregeln der Anbindung: keine Allokation in den heißen Pfaden, und die Rechenzeit je |
| `tests/Funk.Tests/AnbindungOpusTests.cs` | 379 | Opus-Anbindung gegen die echte opus.dll (libopus 1.6.1, eigener Bau). |
| `tests/Funk.Tests/AnbindungResamplerTests.cs` | 144 | Resampler-Anbindung gegen die echte speexdsp_resampler.dll. |
| `tests/Funk.Tests/AnbindungRnnoiseTests.cs` | 124 | RNNoise-Anbindung gegen die echte rnnoise.dll. |
| `tests/Funk.Tests/AnbindungSignale.cs` | 237 | Selbst synthetisierte Testsignale mit festen Seeds (keine fremden Klänge) und kleine |
| `tests/Funk.Tests/BeispielnamenTests.cs` | 121 | A65 (Sarevoc, 01.10.2026): Beispiele in Programm, Setup, Hilfe und Anleitungen heißen Max Mustermann ("Mustermann/Max"), nicht wie |
| `tests/Funk.Tests/ClientHilfe.cs` | 222 | Hält den jeweils laufenden Testserver, damit Clients nach einem Neustart den neuen erreichen. Verbindungen gehen |
| `tests/Funk.Tests/ClientKernTests.cs` | 128 | Bausteine der Client-Bibliothek ohne Netzwerk: Wiederverbindungsplan (7.1), Zeitlinien (C6), Messwerte (7.8), Ziele, Testzugang. |
| `tests/Funk.Tests/ClientPinAusweichTests.cs` | 243 | Die Falle aus dem Umzugsentwurf (TlsAufbau, Erstverbindung): Ein Pin-Fehler an einer Adresse brach sofort mit "Server nicht |
| `tests/Funk.Tests/ClientTlsTests.cs` | 170 | Verbindungsaufbau der Client-Bibliothek: RFC 8305 (ohne TLS, nur Loopback), echtes TLS mit Pin und ALPN (unter |
| `tests/Funk.Tests/ClientVerbindungswegTests.cs` | 202 | Durchsicht des echten Verbindungswegs (Modul profil), ohne Schannel und ohne Schlüsselspeicher im Profil: Auflösung nur |
| `tests/Funk.Tests/ClientZusammenspielTests.cs` | 374 | Zusammenspiel der Client-Bibliothek (Funk.ClientKern) mit dem Dienst auf Loopback: 3 bis 6 Clients, Anmeldung bis |
| `tests/Funk.Tests/EchoTests.cs` | 276 | Echoband und Ratenbremse des Echo-Funkers mit simulierter Zeit, ohne Netz: Reihenfolge und Raster der Wiedergabe, |
| `tests/Funk.Tests/EinrichtenSkriptTests.cs` | 183 | Was sich an funk-einrichten.sh, "Profil erneuern.cmd", den Units und den Bauskripten ohne Linux prüfen lässt: Zeilenenden, |
| `tests/Funk.Tests/EinrichtungTests.cs` | 672 | funk-admin einrichten (A55, EINRICHTEN-UMZUG-ENTWURF "Vorschlag Einrichtung"), der Teil nach funk-einrichten.sh. Alles in |
| `tests/Funk.Tests/EinzelinstanzTests.cs` | 169 | Nur eine Instanz je Einstellungsdatei (src\Funk.Client\Einzelinstanz.cs), ohne Fenster: zwei Anmeldungen im selben |
| `tests/Funk.Tests/ErsterAdminTests.cs` | 193 | funk-admin erster-admin (EINRICHTEN-UMZUG-ENTWURF): offene Anträge zeigen, Kennung aus dem Client erfragen, freigeben, |
| `tests/Funk.Tests/Fassung091Tests.cs` | 52 | Fassung 0.9.1 (Sarevoc 08.10.2026): Das erste Feld, das nach der Rollenwahl von der Ruhe auf einen Ch. geht, wird Sendeziel, wenn noch keins g... |
| `tests/Funk.Tests/FlottenentwurfTests.cs` | 178 | Der Entwurf der Flotten-Einstellungsseite (A40, RAIDLEITUNG-ENTWURF "Flotten-Einstellungsseite"): Änderungen bleiben lokal, |
| `tests/Funk.Tests/FlottenentwurfUmbauTests.cs` | 191 | Der Entwurf für den Umbau der Verwaltung (Fassung 0.8.0, docs/entwurf/VERWALTUNG-UMBAU.md): Herkunftsschild je Rolle und Kanal (gespeichert |
| `tests/Funk.Tests/FlottenvorlageTests.cs` | 106 | Startvorlage der Flotte (A40) und die Zeitrechnung eines Auftrags (6.12 "bis 04:00"): Die Vorlage Star Citizen (Sarevoc, 19.10.2026, Q26) ergibt |
| `tests/Funk.Tests/HaertungTests.cs` | 623 | Offene Befunde aus PROJEKT.md, Nächste Schritte 0, 6 und 7 (Fassung 0.3.0, Strang Härtung): Rennen Antrag/WARTERAUM, |
| `tests/Funk.Tests/HerkunftTests.cs` | 120 | Herkunft von Netzen und Rollen (Fassung 0.8.0, Umbau der Verwaltung, FUNKPROTOKOLL 4.5 und 4.7): ein optionales Feld herkunft |
| `tests/Funk.Tests/IdentitaetGegenpruefungTests.cs` | 459 | Ergänzungen der Gegenprüfung: P-19 als volle Rangmatrix, P-14 mit echter Signatur ohne Präfix, Randfälle |
| `tests/Funk.Tests/IdentitaetLebenszyklusTests.cs` | 756 | Konto, Registrierung, Freigabe und Lebenszyklus (3.3 bis 3.6) mit simulierter Uhr: P-11, P-12, P-19, P-45, P-48. |
| `tests/Funk.Tests/IdentitaetTests.cs` | 667 | Simulierte Uhr für die Identitätstests; springt nur, wenn der Test sie vorstellt. Wanduhr und monotone Uhr |
| `tests/Funk.Tests/MaintainerKopfzeileTests.cs` | 222 | A58 und A59 (Sarevoc, 01.10.2026): In jeder eigenen Quelldatei steht, dass Sarevoc Maintainer ist, im Kommentarstil der Sprache, |
| `tests/Funk.Tests/Maschine/AttrappenGeraete.cs` | 257 | Ein Strom der Attrappe: Der Test ruft die Rückrufe selbst im Takt auf (kein Thread, kein Gerät). |
| `tests/Funk.Tests/Maschine/EingabeTests.cs` | 219 | Sprechtaste (A3): Auswertung der Raw-Input-Daten ohne Fenster, Zustand (Wechsel, Wiederholung, Belegen), Verbund |
| `tests/Funk.Tests/Maschine/Funkquelle.cs` | 45 | Ein Funker im Mischer ohne Netz und Opus (Prüfstand der Klangstufe C2a): spielt ein fertiges Signal (48 kHz mono) im 10-ms-Takt und |
| `tests/Funk.Tests/Maschine/GateModusTests.cs` | 459 | Kennzahlen wie , aber mit der  vor dem Gate, damit alle drei |
| `tests/Funk.Tests/Maschine/GegenpruefungKlangmaschineTests.cs` | 200 | Gegenprüfung der Klangmaschine: Befunde, die erst im Zusammenspiel auffielen, jeweils mit Regressionstest. |
| `tests/Funk.Tests/Maschine/HalteregelTests.cs` | 99 | Halteregel der Pegelstufe in der echten Kette, außerhalb des Klangkorpus (Gegenprüfung Klangstufe A, |
| `tests/Funk.Tests/Maschine/HoerprobeTests.cs` | 420 | Hörprobe nach Sarevocs Fehlerbericht vom 01.10.2026 (Client 0.4.0, Tischmikrofon RØDE VideoMic GO II, Realtek-Lautsprecher, |
| `tests/Funk.Tests/Maschine/JoystickAttrappe.cs` | 250 | Attrappe der Geräteschicht und der Suche unter der Joystick-Sprechtaste: angesteckte Geräte mit Stand und Puffer, |
| `tests/Funk.Tests/Maschine/JoystickTests.cs` | 863 | Joystick-Sprechtaste (A3) ohne Geräte: Zustandslogik und Wiederfinden über der Attrappe der Geräteschicht (Knopf, |
| `tests/Funk.Tests/Maschine/KlangC1NacharbeitTests.cs` | 170 | Nacharbeit aus der Gegenprüfung der Klangstufe C1 (output\test\20261004_pruefung-c1.md), im Kern: H1 (Anker der Mindest-Funklautstärke au... |
| `tests/Funk.Tests/Maschine/KlangC1Tests.cs` | 655 | Klangstufe C1 im Klangkern (KLANGSTUFE-C-ENTWURF.md im Planungsordner, Aufteilung C1): Vorlauf 120 ms von außen einstellbar und im Rahmen... |
| `tests/Funk.Tests/Maschine/KlangC2EmpfangTests.cs` | 235 | Klangstufe C2a, Punkt 3: die Empfängerseite im Mischer. Derselbe Wächter je ankommendem Funker (die anderen Ströme als Bezug): Ein |
| `tests/Funk.Tests/Maschine/KlangC2FixTests.cs` | 352 | Nacharbeit nach der Gegenprüfung der Klangstufe C2a (output\test\20261004_pruefung-c2a.md), Vorlage sind die Proben P1 bis P7 des Prüfers |
| `tests/Funk.Tests/Maschine/KlangC2MaschineTests.cs` | 386 | Klangstufe C2a in der ganzen Kette (Prüfstand: Klangmaschine mit Geräte-Attrappe, simulierter Uhr, AEC3, RNNoise, Pegelstufe, Gate, Opus): |
| `tests/Funk.Tests/Maschine/KlangC2NacharbeitTests.cs` | 114 | Nacharbeit der Klangstufe C2a nach der Entscheidung der Koordination vom 04.10.2026 (im Sinn Sarevocs: "Hit muss raus, im Zweifel 80 ms, nie |
| `tests/Funk.Tests/Maschine/KlangC2WaechterTests.cs` | 560 | Klangstufe C2a, Punkt 1: der Wächter der Auto Dauersend-Dämpfung (KLANGSTUFE-C-ENTWURF.md im Planungsordner, "Die Formel", "Was Dauersend... |
| `tests/Funk.Tests/Maschine/KlangfarbeMaschineTests.cs` | 167 | Klangfarbe (Q70 bis Q72, 0.9.3) in der echten Kette im Prüfstand: Gate, Führung, Korridor, Pegelanzeige, Telemetrie und Wächter sehen mit... |
| `tests/Funk.Tests/Maschine/Klangkorpus.cs` | 91 | Eine Äußerung des Klangkorpus; Positionen in Samples bei 48 kHz ab Anfang des Korpus. |
| `tests/Funk.Tests/Maschine/KlangMaschineTests.cs` | 604 | Die Klangmaschine Ende zu Ende mit Geräte-Attrappen und echter Kette (APM, RNNoise, Opus), simulierter Uhr und ohne |
| `tests/Funk.Tests/Maschine/KlangmoduleMaschineTests.cs` | 104 | Klangmodule (0.9.0, Sarevoc 06.10.2026, Q48 bis Q50) in der Klangmaschine: Mindest-Funklautstärke und Pegelausgleich einzeln schaltbar, der |
| `tests/Funk.Tests/Maschine/KlickLautsprecherTests.cs` | 196 | Der eigene Druck-Klick mit Lautsprechern (Echounterdrückung an, Befund der Gegenprüfung 2c): Er lief über den Loopback in die |
| `tests/Funk.Tests/Maschine/KlickMaschineTests.cs` | 291 | Der Funk-Klick an der echten Klangmaschine (A63, seit 0.5.0): Einstellen über den Vertrag, die eigene Sprechtaste klickt in der |
| `tests/Funk.Tests/Maschine/KodiererZielTests.cs` | 83 | Befund der Gegenprüfung: Die Sendeaufbereitung gibt dem Kodierer Ziele mit 400 Byte (FUNKPROTOKOLL-v1 5.6), der |
| `tests/Funk.Tests/Maschine/LautstaerkeTests.cs` | 82 | Lautstärke und Einmessen über den Vertrag der Klangmaschine (Klangstufe A), mit Geräte-Attrappen. |
| `tests/Funk.Tests/Maschine/MessungAntwortUndKlick.cs` | 365 | Messungen aus der Gegenprüfung 0.6.0 Stufe 2b, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie geben Zahlen für die Abwägung |
| `tests/Funk.Tests/Maschine/MessungGegenpruefungKlangA.cs` | 482 | Messungen der Gegenprüfung Klangstufe A, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie zeigen, wo die Halteregel der |
| `tests/Funk.Tests/Maschine/MessungGegenpruefungKlangB.cs` | 283 | Messungen der Gegenprüfung Klangstufe B, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie zeigen Verhalten, das die Messungen |
| `tests/Funk.Tests/Maschine/MessungGrenzlinie.cs` | 129 | Messung aus der Gegenprüfung 0.6.0 Stufe 2c, nur von Hand: FUNK_MESSUNG=1. Sie prüft nichts, sie zeigt, was die Grenzlinie der |
| `tests/Funk.Tests/Maschine/MessungKlangA.cs` | 346 | Messungen der Klangstufe A, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie geben die Zahlen für die Wahl der Pegelregelung |
| `tests/Funk.Tests/Maschine/MessungKlangB.cs` | 434 | Messungen der Klangstufe B, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie geben die Zahlen für die Wahl zwischen RNNoise |
| `tests/Funk.Tests/Maschine/MessungKlangC.cs` | 801 | Hörproben der Klangstufe C für Sarevocs Ohr (Entwurf KLANGSTUFE-C-ENTWURF.md, Q2, Q14 und Q19), nur von Hand und nur mit |
| `tests/Funk.Tests/Maschine/MessungKlangC1.cs` | 380 | Messung für die Mindest-Funklautstärke ab Werk (Klangstufe C1, nur mit FUNK_MESSUNG=1): Wie weit unter der gelernten Funkstimme darf die |
| `tests/Funk.Tests/Maschine/MessungKlangC2.cs` | 1152 | Prüfstand der Klangstufe C2a (Auto Dauersend-Dämpfung): nachgestellte Gespräche aus dem Klangkorpus durch die ganze Kette (Klangmaschine mit |
| `tests/Funk.Tests/Maschine/MessungNachlauf.cs` | 239 | Nachlauf der Sprechtaste (Q66, 0.9.3), nur von Hand: FUNK_MESSUNG=1. Wie viel vom Ende eines Worts fehlt, wenn die Sprechtaste genau am |
| `tests/Funk.Tests/Maschine/MessungSpielton.cs` | 797 | Messungen zum Spielton (Star Citizen) in der Echoreferenz, nur von Hand: FUNK_MESSUNG=1. Sie prüfen nichts, sie |
| `tests/Funk.Tests/Maschine/MikrofonFehlerTests.cs` | 113 | Punkt 2 des Integrationsauftrags: Lässt sich das Mikrofon nicht öffnen (Datenschutz von Windows), läuft die Wiedergabe |
| `tests/Funk.Tests/Maschine/NachlaufTests.cs` | 494 | Nachlauf der Sprechtaste (Q66, 0.9.3) an der echten Klangmaschine im Prüfstand: Nach dem Loslassen sendet sie genau so lange weiter wie |
| `tests/Funk.Tests/Maschine/PumpenTests.cs` | 51 | Pumpen (Klangstufe A): Der Klangkorpus (drei Stimmen, leise und laute Stellen, Hüllen im Satz, Pausen 150 ms bis 3 s) durch |
| `tests/Funk.Tests/Maschine/Pumpmessung.cs` | 247 | Eine Sendekette für den Vergleich: Pegelregelung im APM (AGC2, alte Kette) oder dahinter (Pegelstufe). |
| `tests/Funk.Tests/Maschine/RauchtestEchteGeraete.cs` | 51 | Rauchtest mit echten Geräten, nur von Hand: FUNK_RAUCHTEST=1. Öffnet die Standardgeräte für Kommunikation |
| `tests/Funk.Tests/Maschine/RauchtestJoystick.cs` | 122 | Rauchtest der Joystick-Sprechtaste mit echten Spielgeräten, nur von Hand: FUNK_RAUCHTEST=1. Zählt die Geräte |
| `tests/Funk.Tests/Maschine/RauschfilterMaschineTests.cs` | 177 | Rauschfilter Standard, Stark und Aus in der echten Klangmaschine (Klangstufe B): Wechsel, Rückfall, Freigabe, Allokation. |
| `tests/Funk.Tests/Maschine/RauschfilterwahlTests.cs` | 172 | Umschalten zwischen den Rauschfiltern (Klangstufe B, Recherche Abschnitt 3) ohne native Filter: Die Wege werden als ideale |
| `tests/Funk.Tests/Maschine/S2EingabeTests.cs` | 352 | Ausbaustufe S2, Teil A1 (RAID-S2-ENTWURF Abschnitt 6): Der Joystick kann mehr als Sprechen. Die DirectInput-Quelle ist eine volle |
| `tests/Funk.Tests/Maschine/S2TastenTests.cs` | 209 | Ausbaustufe S2, Teil A1: die neuen Tasten in den Einstellungen. Sie stehen in einer eigenen Liste mit der Funktion als Text, |
| `tests/Funk.Tests/Maschine/SpieltonTests.cs` | 147 | Spielton (Star Citizen) in der Echoreferenz, Modul aec3: Der Echoschutz verlangt bei Geräusch in der Wiedergabe nur |
| `tests/Funk.Tests/Maschine/StimmprobeTests.cs` | 41 | Stimmprobe für den Prüfstand der Klangstufe D (Sarevoc 08.10.2026, Q60): die echte Kette nimmt hinter Echo- und Rauschfilter auf, vor der |
| `tests/Funk.Tests/Maschine/Stoergeraeusche.cs` | 151 | Selbst erzeugte Störgeräusche für den Vergleich der Rauschfilter (Klangstufe B): Lüfter als rosa und braunes Rauschen, |
| `tests/Funk.Tests/Maschine/TastenTests.cs` | 537 | Eine Quelle wie Raw Input: mehrere Zuordnungen und Netztasten, Belegen liefert die vorgegebene Taste. |
| `tests/Funk.Tests/Maschine/TelemetrieTests.cs` | 166 | Telemetrie der Sendekette (Klangstufe A, Recherche Schritt 1): Werte und Laufzeit je Stufe, verpasste Fristen, die neue |
| `tests/Funk.Tests/Maschine/WandlerTests.cs` | 151 | Geräteformate (Rate, Kanäle) zu 48 kHz mono in 10-ms-Rahmen und zurück; Journal und Ringe. |
| `tests/Funk.Tests/MeldesystemServerTests.cs` | 333 | Meldesystem 0.12.0 im Zusammenspiel von Server und Client-Kern (prozessintern, ohne TLS): wer senden darf (Haken, Stufe, Auftrag, |
| `tests/Funk.Tests/MeldesystemTests.cs` | 616 | Meldesystem 0.12.0 (Sarevoc 11.10. und 16.10.2026, M1-M25 in SPRECHERANZEIGE-ENTWURF.md im Planungsordner) ohne Server: Katalog und Bilder, Bo... |
| `tests/Funk.Tests/MitgliederTests.cs` | 765 | Anmeldung mit SC-Name und Discord-Name (3.4, A42), Namensraum ohne Besetzen durch offene Anträge, Freigabe mit |
| `tests/Funk.Tests/NamenslaengeTests.cs` | 24 | Sarevoc 13.10.2026: "beim Namen anlegen eine maximale Länge angeben, Beispiel Superschütze/Alexander, nicht länger" - damit jeder Name in Wer ist |
| `tests/Funk.Tests/NamensregelnTests.cs` | 94 | Die Namensregeln der Anmeldung (A42, 3.3), die Client, Setup und Server teilen. Der Discord-Name steht seit Antwort 3 für |
| `tests/Funk.Tests/NamenswaechterTests.cs` | 116 | Umbau JG TacRelay (Sarevoc, 18./19.10.2026: alles mit dem alten Gildennamen, der alten Kanalvorlage und den beiden Personen raus "bis in die l... |
| `tests/Funk.Tests/Oberflaeche/Ansichtspruefung.cs` | 275 | Gemeinsame Prüfungen an gezeichneten Ansichten (): was zu sehen ist, welche Texte dastehen und ob |
| `tests/Funk.Tests/Oberflaeche/BrettBaumTests.cs` | 364 | Die Raid-Seite seit 0.11.2 im Modell (Sarevoc 13.10.2026, Stammbaum nach rechts), über der Attrappe mit dem Beispielabend und drei Kanälen ohne |
| `tests/Funk.Tests/Oberflaeche/FehlernummernTabelle.cs` | 54 | Eine Fehlernummer und was sie bedeutet, zum Nachschlagen für den Admin. |
| `tests/Funk.Tests/Oberflaeche/FehlertexteTests.cs` | 496 | Fehlermeldungen für Funker (Sarevoc, Grilling Q5 und Q14): Sie beschreiben die Lage und tragen eine kurze Fehlernummer zum |
| `tests/Funk.Tests/Oberflaeche/KlangBrueckeTests.cs` | 283 | Zeichnet auf, was die Brücke am Sprechkanal tut. |
| `tests/Funk.Tests/Oberflaeche/KlickSprechwegeTests.cs` | 135 | A63: Jeder Weg, auf dem man selbst spricht, löst den eigenen Funk-Klick aus, beim Drücken und beim Loslassen: Sprechtaste an der |
| `tests/Funk.Tests/Oberflaeche/MeldezeileEineMeldungTests.cs` | 233 | Die Meldezeile zeigt immer nur eine Meldung (Sarevoc Q112, 12.10.2026; bis dahin höchstens drei untereinander). Vorrang Fehler vor Leitung vor |
| `tests/Funk.Tests/Oberflaeche/OberflaecheBereicheTests.cs` | 300 | Modelle hinter dem Hauptfenster: Audio (A11, A14), Netze und Rollen (A4, A15, A22), Navigation und Kopfzeile (A25). Seit 0.10.0 sind Audi... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheBilder.cs` | 393 | Oberfläche im Speicher zeichnen, ohne Fensterhandle und ohne App (C:\Claude\CLAUDE.md, Abschnitt Bildschirm; CODEMAP, |
| `tests/Funk.Tests/Oberflaeche/OberflaecheBrettTests.cs` | 626 | Das Brett der Einsatzleitung (Stufe 2, RAIDLEITUNG-ENTWURF "Das Brett", M5) über der Attrappe mit dem Beispielabend aus Anhang |
| `tests/Funk.Tests/Oberflaeche/OberflaecheC1NacharbeitTests.cs` | 111 | Nacharbeit aus der Gegenprüfung der Klangstufe C1 im Client (output\test\20261004_pruefung-c1.md): H1 (die gemessene Funkstimme wird mit der |
| `tests/Funk.Tests/Oberflaeche/OberflaecheDienstTests.cs` | 354 | Anbindung an Funk.ClientKern: Abbildung des Protokollstands in die Lage der Oberfläche, Fehlertexte, Verhalten ohne |
| `tests/Funk.Tests/Oberflaeche/OberflaecheEinsatzleitungTests.cs` | 116 | Bereich "Einsatzleitung" der Verwaltung (RAIDLEITUNG-ENTWURF, Flotten-Einstellungsseite, Absatz Einsatzleitung; 6.12): die |
| `tests/Funk.Tests/Oberflaeche/OberflaecheEinsteigenTests.cs` | 135 | Teilnehmersicht "Einsteigen" (Stufe 2, RAIDLEITUNG-ENTWURF "Vor dem Raid: jeder steigt selbst ein", M4): Reiter Schiffe, |
| `tests/Funk.Tests/Oberflaeche/OberflaecheEinstellungenTests.cs` | 275 | Einstellungen als JSON: Rundlauf, Standardwerte, beschädigte Datei, Grenzen, Umgebungsvariable, gebündeltes Schreiben, Fensterplatz. |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFarbvorschlagTests.cs` | 176 | Die Farbstellung Anthrazit (Sarevoc, 18./19.10.2026, Q5, Q12, Q17): Anthrazit und Grau, neutral und ruhig, mit gedämpftem Akzent Petrol, |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFenstergroessenTests.cs` | 353 | Größenklassen des Hauptfensters (Sarevoc, 02.10.2026, Q19 und Vorgabe j; Fassung 0.6.0, Stufe 1, 2 und 2b; seit 0.10.0 ohne die Klassen mit |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFensterTests.cs` | 253 | Rauchtest der Fenster ohne Anzeige: Hauptfenster (jeder Bereich mit der Statuszeile, die Seite Sprachaktivierung, A26) und Funkfeld |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFlotteTests.cs` | 196 | Bereich "Flotte" der Verwaltung (A40, RAIDLEITUNG-ENTWURF "Flotten-Einstellungsseite"): Einheiten, Rollen und Netze im |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFunkfeldAnsichtenTests.cs` | 338 | Funkfeld kompakt (Sarevoc, 29.09.2026; A29 entschieden: Raster Voreinstellung, Liste Alternative, Leiste entfallen): |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFunkfeldBilderTests.cs` | 628 | Funkfeld kompakt im Speicher gezeichnet (, ohne Fensterhandle, auch neben einem Spiel): gemessene |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFunkfeldDurchsichtTests.cs` | 655 | Durchsichtigkeit (A30, A33). Hauptweg seit Sarevoc, 30.09.2026 ("das transparente Fenster macht nicht das ganze Funkfenster |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFunkfeldGroesseTests.cs` | 575 | Größe und Platz der Fenster (A32; Sarevoc, 30.09.2026 nach dem ersten Lauf am echten Fenster, 175 %, zwei Monitore: "Das |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFunkfeldReglerTests.cs` | 325 | Regler der Durchsichtigkeit in der Titelzeile des Funkfelds (A30, Sarevoc: "links neben der Windows-Leiste", bestätigt: in der |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFunkfeldSchalterTests.cs` | 251 | Schiebeschalter 5 oder 10 Felder in der Titelzeile des Funkfelds (A33, Sarevoc: "den 5 oder 10 Knopf weg machen, sondern eine |
| `tests/Funk.Tests/Oberflaeche/OberflaecheFunkfeldTests.cs` | 339 | Funkfeld (A17, A18, A23, A24): 5er/10er-Ansicht mit Global rechts außen, Zustände mit Text, Sendewahl, Aus-Schalter, |
| `tests/Funk.Tests/Oberflaeche/OberflaecheGegenpruefungTests.cs` | 318 | Befunde der Gegenprüfungen am Bereich Verbindung und am Dienst (Modul feinschliff), je mit Regressionstest: |
| `tests/Funk.Tests/Oberflaeche/OberflaecheGlasTests.cs` | 386 | Glasdesign in JG TacRelay (0.6.0, Stufe 2): die feste Kopie unter extern\Glasdesign ist die aus HERKUNFT.md. Von 0.6.0 bis 0.11.1 trugen |
| `tests/Funk.Tests/Oberflaeche/OberflaecheHauptfensterTests.cs` | 232 | Hauptfenster und Funkfeld im Speicher (, ohne Fensterhandle, auch neben einem Spiel): Zugriffstasten |
| `tests/Funk.Tests/Oberflaeche/OberflaecheHilfe.cs` | 179 | Eigener Ordner unter dem Testordner (), wird beim Freigeben gelöscht. |
| `tests/Funk.Tests/Oberflaeche/OberflaecheHilfeTests.cs` | 277 | Hilfetexte an Knöpfen (Sarevoc, 02.10.2026, Vorgabe i, Q9; seit Stufe 2 über Glasdesign):  setzt aus Text, Taste |
| `tests/Funk.Tests/Oberflaeche/OberflaecheHoerprobeBilderTests.cs` | 102 | Die Hörprobe und das Mithören, gezeichnet ohne Fensterhandle (), in allen drei Farbstellungen: mit Lautsprechern |
| `tests/Funk.Tests/Oberflaeche/OberflaecheHoerprobeTests.cs` | 181 | Hörprobe in der Oberfläche nach Sarevocs Fehlerbericht vom 01.10.2026 ("ich höre mich bei Aktivierung nicht"): die Probe als |
| `tests/Funk.Tests/Oberflaeche/OberflaecheIntegrationTests.cs` | 212 | Oberfläche nach der Integration: Frage beim ersten Start "Headset oder Lautsprecher?" (Punkt 6), gesperrtes Mikrofon |
| `tests/Funk.Tests/Oberflaeche/OberflaecheKachelBilderTests.cs` | 222 | Die Personenkachel aus 0.11.0 (Q98, Variante A des Entwurfs, Stile.xaml) links auf der Raid-Seite, im echten Hauptfenster über dem |
| `tests/Funk.Tests/Oberflaeche/OberflaecheKlangC1Tests.cs` | 477 | Klangstufe C1 in der Oberfläche (KLANGSTUFE-C-ENTWURF.md im Planungsordner): Umstieg der Einstellungen aus 0.6.0 (Grenzlinie wird |
| `tests/Funk.Tests/Oberflaeche/OberflaecheKlangC2bTests.cs` | 915 | Klangstufe C2b in der Oberfläche (KLANGSTUFE-C-ENTWURF.md im Planungsordner, Q1, Q2, Q8, Q13, Q26, Q31 bis Q36, "Mikro stumm") und die Na... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheKlangC2Tests.cs` | 65 | Klangstufe C2a, Punkt 5 in der Einstellungsdatei: Schalter "Auto Dauersend-Dämpfung" (ab Werk an) und gewählte Stufe (ab Werk "deutlich |
| `tests/Funk.Tests/Oberflaeche/OberflaecheKlangfarbeTests.cs` | 307 | Klangfarbe (Q70 bis Q72, 0.9.3) in Einstellungen und Oberfläche: drei Felder in den Audio-Einstellungen (ab Werk 0, nachsichtig gelesen, |
| `tests/Funk.Tests/Oberflaeche/OberflaecheKlangmoduleTests.cs` | 198 | Klangmodule (0.9.0, Sarevoc 06.10.2026, Q48 bis Q52): je Modul ein Schiebeschalter, ab Werk an, danach wie zuletzt geschaltet, zu jedem Modul ein |
| `tests/Funk.Tests/Oberflaeche/OberflaecheKlickTests.cs` | 197 | Funk-Klick (A63, seit 0.5.0; seit 0.6.0 Stufe 2b als Karte im Bereich Audio und ohne Wahl des Klangs): gespeichert in den |
| `tests/Funk.Tests/Oberflaeche/OberflaecheKontrastTests.cs` | 149 | Farbstellung Anthrazit (Sarevoc, 18./19.10.2026) und Kontrastregeln nach WCAG 2.1: Text mindestens 4,5:1, Bedienelemente, Rahmen |
| `tests/Funk.Tests/Oberflaeche/OberflaecheLautstaerkeTests.cs` | 290 | Automatische Lautstärke (Klangstufe A) im Bereich Audio: gespeichert unter audio in vier Feldern, die nachsichtig gelesen |
| `tests/Funk.Tests/Oberflaeche/OberflaecheMeldezeileEinzeiligTests.cs` | 327 | Die Meldezeile einzeilig (0.11.0, Sarevoc 12.10.2026: "Im Funkfeld die Meldung des Kanals platzsparender und maximal einzeilig anordnen, das |
| `tests/Funk.Tests/Oberflaeche/OberflaecheMeldezeilePlatzTests.cs` | 671 | Die Meldezeile an einem festen Platz, ohne Dauerfeuer (0.11.1, Sarevoc 12.10.2026: "Das Overlay ... muss an einem festen Platz am Bildschirm |
| `tests/Funk.Tests/Oberflaeche/OberflaecheMeldungenTests.cs` | 1379 | Die Oberfläche des Meldesystems 0.12.0 (Sarevoc 11.10. und 16.10.2026, M1-M24, tmp\melde\SPEZ.md "Client"), Schritt 2 und Textmeldung. Mit ein... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheMesswerkzeugTests.cs` | 160 | Messwerkzeug für das echte Funkfeld (A32: TacRelay.exe --funkfeld-messen, dazu tools\funkfeld-messen.ps1). |
| `tests/Funk.Tests/Oberflaeche/OberflaecheNachlaufTests.cs` | 241 | Nachlauf der Sprechtaste (Q66, 0.9.3) in Einstellungen und Oberfläche: das Feld in den Tasten (ab Werk 100 ms, nachsichtig gelesen, |
| `tests/Funk.Tests/Oberflaeche/OberflaechePegelGelbTests.cs` | 592 | Q43 (0.8.0): Die Pegelanzeigen (Pegelbalken auf Startseite, in der Leiste und unter Audio, Pegelkurve der Schwellengrafik, Pegel auf den ... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheProfilTests.cs` | 582 | Serverprofil und Profilschlüssel im Bereich Verbindung (Modul profil): Einfügen und sofortiges Prüfen des Schlüssels, |
| `tests/Funk.Tests/Oberflaeche/OberflaecheRaidBilderTests.cs` | 340 | Bilder der Raidleitung im Speicher (, ohne Fensterhandle, auch neben einem Spiel): die Raid-Seite der Einsatzleitung |
| `tests/Funk.Tests/Oberflaeche/OberflaecheRauschfilterBilderTests.cs` | 72 | Der Rauschfilter (Klangstufe B) unter Einstellungen > Senden (seit 0.11.1 offen, ohne Aufklappen; 0.10.0 und 0.11.0 im Klappbereich, bis ... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheReglerGrenzenTests.cs` | 395 | Prüfung aller Regler (02.10.2026, 0.5.0 Schritt 2): Sarevoc sah einmal den Knopf eines Schiebereglers "ins Leere laufen" und |
| `tests/Funk.Tests/Oberflaeche/OberflaecheS2BilderTests.cs` | 202 | Bilder der Ausbaustufe S2 im Speicher (, ohne Fensterhandle, auch neben einem Spiel), in Anthrazit mit |
| `tests/Funk.Tests/Oberflaeche/OberflaecheSchwellenTests.cs` | 487 | Sprachaktivierung einstellen (A26-A28): Schalter Automatik und Lernen, Linie ziehen, Korridor, Hörprobe, Livewerte |
| `tests/Funk.Tests/Oberflaeche/OberflaecheSpielprofilTests.cs` | 216 | Spielprofil (Umbau JG TacRelay, Sarevoc 18./19.10.2026, Q10): Unter Einstellungen > Sprecheranzeige > Erweitert steht "Spiel" mit Star Citizen... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheSprecheranzeigeTests.cs` | 939 | Die Sprecheranzeige über dem Spiel (0.11.9, Sarevoc 11.10. und 15.10.2026, tmp\sprecher\SPEZ.md). Geprüft mit einer Uhr der Tests: der Filter ... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheStartseiteTests.cs` | 810 | Die Startseite (0.6.0, Stufe 2, Sarevocs abgenommene Skizze; seit 0.10.0 nach der Mischung, Q87 bis Q92): Logo, Name, Gerät freigegeben oder |
| `tests/Funk.Tests/Oberflaeche/OberflaecheStellungAnthrazitTests.cs` | 199 | Die eine Farbstellung Anthrazit (Sarevoc, 18./19.10.2026, Q5, Q12, Q17), seit die Wahl zwischen P1, P2 und P3 (A31) entfallen ist: Unter |
| `tests/Funk.Tests/Oberflaeche/OberflaecheStimmprobenTests.cs` | 73 | Stimmproben (Q60): fünf Aufnahmen, WAV im Ordner der Einstellungen, eine nach der anderen. Bis 0.9.3 eine Karte auf der Seite Audio, seit... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheStufe1BilderTests.cs` | 92 | Sichtproben für Sarevoc nach Stufe 1 von 0.6.0 (Hilfetexte, Fenstergrößen, Fassung im Titel), nur im Speicher gezeichnet: mit |
| `tests/Funk.Tests/Oberflaeche/OberflaecheStufe2BilderTests.cs` | 119 | Sichtproben für Sarevoc nach Stufe 2 von 0.6.0, nur im Speicher gezeichnet (mit FUNK_BILDER als PNG): Einstellungen zu und mit offenen |
| `tests/Funk.Tests/Oberflaeche/OberflaecheStufe2bTests.cs` | 313 | Stufe 2b von 0.6.0 nach Sarevocs erstem Test der Vorab-Fassung (02.10.2026): Die Sprachaktivierung klappte unter ihrer Leiste auf (bis 0.9.3; |
| `tests/Funk.Tests/Oberflaeche/OberflaecheStufe2cTests.cs` | 291 | Stufe 2c von 0.6.0 nach Sarevocs Rückmeldungen vom 02.10.2026 (19:30): in der Leiste links ein Unterpunkt "Sprachaktivierung" mit der ganzen |
| `tests/Funk.Tests/Oberflaeche/OberflaecheStufe34Tests.cs` | 337 | Stufe 3, Schritt 3.4 im Speicher gezeichnet (, kein Fensterhandle): das Funkfeld mit Kanälen. Raster bleibt |
| `tests/Funk.Tests/Oberflaeche/OberflaecheUiFixTests.cs` | 667 | Oberflächen-Korrekturen 0.6.2 (KLANGSTUFE-C-ENTWURF.md im Planungsordner, "Mit in die nächste Fassung (nicht Klang)"): Grün heißt überall... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheUmbauBilderTests.cs` | 411 | 0.10.0, AP4: die Bilder der neuen Oberfläche aus der echten Ansicht (kein Entwurfs-XAML), zum Vergleich mit der abgenommenen Mischung |
| `tests/Funk.Tests/Oberflaeche/OberflaecheUmbauEinstellungenTests.cs` | 601 | 0.10.0, AP3 (Einstellungen nach der Mischung, Q87, Bilder m-4 und m-5; E2, E3, E6, E11 bis E19, E21, E22): die Seite Einstellungen mit de... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheUmbauErweitertTests.cs` | 423 | 0.11.1 (Sarevoc 12.10.2026, nach dem ersten Test von 0.11.0 am echten PC): Unter Einstellungen > Senden stehen die Schiebeschalter der Klangmo... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheUmbauRahmenTests.cs` | 531 | 0.10.0, AP1 (Gerüst der Mischung, Q87 bis Q90, E1 bis E6, E23): die Statuszeile "Mikrofon" statt der Leiste der Sprachaktivierung (Pegel, |
| `tests/Funk.Tests/Oberflaeche/OberflaecheUmbauStartTests.cs` | 656 | 0.10.0, AP2 (Start und erster Start nach der Mischung, Q87 bis Q92, E7 bis E10, E17; Bilder m-1, m-2 und m-7c): Start in zwei Spalten |
| `tests/Funk.Tests/Oberflaeche/OberflaecheVerbindungBilderTests.cs` | 174 | Verbindung und Anmeldung auf der Startseite (seit 0.6.0; bis 0.5.x ein eigener Bereich), im Speicher gezeichnet |
| `tests/Funk.Tests/Oberflaeche/OberflaecheVerwaltungBaumTests.cs` | 149 | Fassung 0.9.1 (Sarevoc 08.10.2026): die Navigation der Verwaltung als Baum wie im Explorer (Q55 Variante A, Unterpunkte deutlich eingerückt) |
| `tests/Funk.Tests/Oberflaeche/OberflaecheVerwaltungLayoutTests.cs` | 232 | Wächter für den ganzen Verwaltungsteil (0.11.1 AP4, Sarevocs Bild vom 12.10.2026 vom echten PC: der Knopf "Mitglied vormerken" über der Übersc... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheVerwaltungTests.cs` | 697 | Der Verwaltungsteil (src\Funk.Verwaltung, A43-A47) über einer Attrappe: Schritte vom Antrag auf den Admin-Schlüssel bis |
| `tests/Funk.Tests/Oberflaeche/OberflaecheVerwaltungUmbauTests.cs` | 269 | Umbau der Verwaltung (Fassung 0.8.0, docs/entwurf/VERWALTUNG-UMBAU.md, Variante B mit dem Funkfeld-Editor aus Variante C), an den |
| `tests/Funk.Tests/Oberflaeche/OberflaecheVerwaltungVoraussetzungenTests.cs` | 176 | Vorprüfung im Schritt "Verwaltung einrichten" (seit 0.4.1, Sarevocs Entscheidung vom 01.10.2026) über der Attrappe: Sie läuft |
| `tests/Funk.Tests/Oberflaeche/OberflaecheVorschau0112Tests.cs` | 125 | Vorschau 0.11.2 für Sarevoc (13.10.2026): Start mit "Senden" unten (Pegelbalken mit Griffen, Schiebeschalter, "Erweitert:") und die neue |
| `tests/Funk.Tests/Oberflaeche/OberflaecheWerIstDaPlatzTests.cs` | 687 | Q93 a (Sarevoc 12.10.2026): die Spalte "Wer ist da" in Klein. Öffnet man sie, wird das Hauptfenster um ihre Breite breiter, nach rechts, sonst... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheWerIstDaTests.cs` | 1168 | "Wer ist da" (A48) seit 0.11.5 (Sarevoc 13. und 14.10.2026): eine flache Liste, eine Zeile je Person, online zuerst, dann nach Rang (Admin, Fü... |
| `tests/Funk.Tests/Oberflaeche/OberflaecheWortlisteTests.cs` | 331 | Wächter für die Wörter der Oberfläche (Sarevoc, 02.10.2026, Q13 und Stufe 3): Was ein Mitglied oder der Admin liest, spricht von |
| `tests/Funk.Tests/Oberflaeche/OberflaecheZeichenUndAbzeichenTests.cs` | 381 | Abzeichen im Client (0.11.7, Sarevoc 14./15.10.2026): die 50 Bilder aus TacRelay.dll, das Abzeichen "vorn" in Wer ist da und auf der |
| `tests/Funk.Tests/Oberflaeche/OberflaecheZuordnungBilderTests.cs` | 174 | Zuordnung 0.11.0, Punkte 2 und 3, im Speicher gezeichnet (Bildlauf) bei 175 % (Sarevocs Bildschirm), in Anthrazit, über dem |
| `tests/Funk.Tests/Oberflaeche/OberflaecheZuordnungVorschauTests.cs` | 186 | Ziehen mit Vorschau, sanft (0.11.1 AP2, Sarevocs Punkt 4): Beim Ziehen zeigt der erste freie Platz des Ziels die gezogene Person gedämpft |
| `tests/Funk.Tests/Oberflaeche/OberflaecheZuordnungZiehenTests.cs` | 133 | Das Ziehen auf dem Brett über die Ereignisse von WPF selbst (0.11.1 AP1, Sarevocs Befund vom 12.10.2026: "landet immer auf Standard"). |
| `tests/Funk.Tests/Oberflaeche/S2AblageTests.cs` | 294 | Zeichnet auf, was die Ablage am Funkfeld tut; ein Aktivieren stünde hier als "mit Fokus". |
| `tests/Funk.Tests/Oberflaeche/S2BrettPhasenTests.cs` | 117 | Ausbaustufe S2, Teil C2 am Brett (RAID-S2-ENTWURF Abschnitt 3): die Phasenleiste im Kopf ("1 Anflug / 2 Entern / 3 Rückzug", die |
| `tests/Funk.Tests/Oberflaeche/S2FlottePhasenOberflaecheTests.cs` | 110 | Ausbaustufe S2, Teil C1 im Verwaltungsteil (RAID-S2-ENTWURF Abschnitt 3 "Anlegen"): der Reiter Flotte &gt; Phasen. Liste |
| `tests/Funk.Tests/Oberflaeche/S2FunkerClientTests.cs` | 192 | Ausbaustufe S2, Teil B2 (RAID-S2-ENTWURF Abschnitt 1): "Heute als Funker" im Client. Schalter nur für Admins an Servern ab 0.5.0, |
| `tests/Funk.Tests/Oberflaeche/S2MeldetasteTests.cs` | 224 | Ausbaustufe S2, Teil A4 (RAID-S2-ENTWURF Abschnitt 4): Die Melde-Taste spricht, solange sie gehalten ist, ins Meldenetz (etwa |
| `tests/Funk.Tests/Oberflaeche/S2PhasenClientTests.cs` | 242 | Ausbaustufe S2, Teile A3 und C2 (RAID-S2-ENTWURF Abschnitt 3): die Meldezeile (Vorrang, Dauer, Halten, seit Q112 eine Meldung) und |
| `tests/Funk.Tests/Oberflaeche/S2TastenbetriebTests.cs` | 164 | Ausbaustufe S2, Verdrahtung der Tasten (RAID-S2-ENTWURF Abschnitte 3 bis 5), wie App.OnStartup sie baut: Die Ereignisse kommen |
| `tests/Funk.Tests/Oberflaeche/S2TeilnehmerTests.cs` | 341 | Ausbaustufe S2, Spielersicht (RAID-S2-ENTWURF Abschnitte 3 und 4): Was die Meldezeile jedem Funker sagt, auch wenn das Funkfeld |
| `tests/Funk.Tests/Oberflaeche/Stufe34FunkfeldTests.cs` | 481 | Stufe 3, Schritt 3.4 (STUFE3-ENTWURF 5.1 bis 5.4 mit Sarevocs Antworten in Abschnitt 9, FUNKPROTOKOLL 6.16 und 6.17): das Funkfeld kennt |
| `tests/Funk.Tests/Oberflaeche/Stufe35OberflaecheTests.cs` | 715 | Stufe 3, Schritt 3.5 an der Oberfläche (STUFE3-ENTWURF 5.2, 5.5, 5.6 und Abschnitt 9): Die Feinheiten je Kanal (Hören auf, Lautstärke, Pe... |
| `tests/Funk.Tests/Oberflaeche/Stufe36BrettTests.cs` | 424 | Das Brett mit Kanälen (Fassung 0.6.0, Schritt 3.6, STUFE3-ENTWURF 5.6, FUNKPROTOKOLL 6.16 und 6.17) über der Attrappe mit dem |
| `tests/Funk.Tests/Oberflaeche/Stufe36EinstellungsfehlerTests.cs` | 52 | Fehler der Einstellungsdatei ohne Pfad (Fassung 0.6.0, Schritt 3.6, offen aus Stufe 2): Was Laden und Speichern melden, steht in |
| `tests/Funk.Tests/Oberflaeche/Stufe36SichtprobenTests.cs` | 88 | Die Sichtproben des Schritts 3.6 für Sarevoc, nur mit FUNK_BILDER (Präfix 36-), im Speicher gezeichnet ohne Fensterhandle: das Brett |
| `tests/Funk.Tests/Oberflaeche/Stufe37BrettTests.cs` | 142 | "Für alle sperren" an der Kanalliste des Bretts (Gegenprüfung 3.7; STUFE3-ENTWURF 5.6, Sarevoc Q9 ja, FUNKPROTOKOLL 6.17): Bis 3.6 konnte das |
| `tests/Funk.Tests/Oberflaeche/VerwaltungsdienstAttrappe.cs` | 478 | Verwaltung ohne Server für die Tests der Oberfläche: eine Admin-Sitzung, Anträge, Mitglieder und Katalog eines erfundenen |
| `tests/Funk.Tests/ProbefunkTests.cs` | 531 | Zusammenspiel im Probebetrieb: funk-testserver --probe konfig\probefunk.json --echo im Prozess, Clients über den |
| `tests/Funk.Tests/ProfilErneuernTests.cs` | 273 | funk-admin profil-erneuern auf Sarevocs Stick (EINRICHTEN-UMZUG-ENTWURF, "Was beim Eigentümer bleibt"): bisheriges Profil |
| `tests/Funk.Tests/ProfilPinWarnungTests.cs` | 138 | 0.4.0, offener Punkt der Gegenprüfung "Einrichten und Umzug": Der Server warnt, wenn das geladene Serverprofil seinen eigenen |
| `tests/Funk.Tests/ProfiluebernahmeTests.cs` | 369 | F1 (EINRICHTEN-UMZUG-ENTWURF, A55): Der Client übernimmt nach Welcome{FREIGEGEBEN} ein neueres, offline signiertes Serverprofil |
| `tests/Funk.Tests/Program.cs` | 2 |  |
| `tests/Funk.Tests/QrCodeTests.cs` | 285 | Der eigene QR-Code (src\Funk.Verwaltung\QrCode.cs) gegen die Werte des Standards (ISO/IEC 18004): Reed-Solomon am |
| `tests/Funk.Tests/RaidleitungClientTests.cs` | 286 | Raidleitung 0.3.0, Client gegen einen echten Server im Prozess (ohne TLS, ohne Geräte, ohne Fenster): Otto verbindet sich |
| `tests/Funk.Tests/RaidleitungGegenpruefungTests.cs` | 192 | Gegenprüfung der Raidleitung (Fassung 0.3.0, 01.10.2026), je Befund erst rot, dann grün: |
| `tests/Funk.Tests/RaidleitungServerTests.cs` | 341 | Raidleitung über den Draht (Fassung 0.3.0, 6.12, N1, N4, N5, F2-F10): Auftrag erteilen nur in einer Admin-Sitzung mit |
| `tests/Funk.Tests/RechteBeauftragungTests.cs` | 489 | Rechte und Auftrag für einen Abend (6.1, 6.10, 6.12, RAIDLEITUNG-ENTWURF M1) am Zustandsschreiber mit simulierter Uhr: |
| `tests/Funk.Tests/RobustheitTests.cs` | 274 | Gegenprüfung von Server und Client: keine Verstärkung an unbestätigte Adressen (5.10, 7.5), Tunnel nur mit der eigenen |
| `tests/Funk.Tests/S2ClientDrahtTests.cs` | 100 | Ausbaustufe S2, Clientseite gegen den echten Server im Prozess (ohne TLS, ohne Geräte, ohne Fenster): Was der Client nach einem |
| `tests/Funk.Tests/S2DienstTests.cs` | 113 | Ausbaustufe S2, Client wie im WPF-Programm gegen den echten Server im Prozess (FunkClientDienst über den Testzugang, ohne TLS, |
| `tests/Funk.Tests/S2DrahtTests.cs` | 280 | Funker-Modus und Einsatzphasen über den Draht (Fassung 0.5.0, 0x003C, 0x003D, 0x4004, Verwaltung PHASE_SETZEN und |
| `tests/Funk.Tests/S2FlottePhasenTests.cs` | 131 | Ausbaustufe S2, Teil C1 am Flottenentwurf (RAID-S2-ENTWURF Abschnitt 3 "Anlegen"): Phasen sind eigene Objekte im Entwurf. |
| `tests/Funk.Tests/S2FormTests.cs` | 90 | Drahtform der Fassung 0.5.0 (4.5): Bodies von 0x003C, 0x003D, 0x4004, die Phasen in NetzListe mit ihren Grenzen, die neuen |
| `tests/Funk.Tests/S2FunkerLageTests.cs` | 152 | "Heute als Funker" am Zustandsschreiber (Fassung 0.5.0, RAID-S2-ENTWURF Abschnitt 1, FUNKPROTOKOLL 6.15) mit simulierter Uhr: |
| `tests/Funk.Tests/S2PhasenLageTests.cs` | 489 | Einsatzphasen am Zustandsschreiber (Fassung 0.5.0, RAID-S2-ENTWURF Abschnitt 3, FUNKPROTOKOLL 6.14) mit simulierter Uhr und dem |
| `tests/Funk.Tests/S2SymbolTests.cs` | 45 | Ausbaustufe S2, Teil A2: das Zeichen für das Symbol im Infobereich. Kein Test legt ein echtes Symbol an (RAID-S2-ENTWURF |
| `tests/Funk.Tests/SchutzTests.cs` | 126 |  |
| `tests/Funk.Tests/ServerGrundTests.cs` | 430 | Grundschicht des Dienstes: Konfiguration, Zulassung (4.8), Vorrangspur, Schwärzung (7.5), Ablage (7.2). |
| `tests/Funk.Tests/ServerHilfe.cs` | 638 | Ein Server auf Loopback (127.0.0.1, zugeteilte Ports) mit Zustand in einem eigenen Testverzeichnis, Profil und |
| `tests/Funk.Tests/ServerLastTests.cs` | 100 | Kleiner Lasttest auf Loopback (Vorstufe zu P-30): viele Sitzungen im selben Netz, mehrere Sprecher mit 50 Paketen/s, |
| `tests/Funk.Tests/ServerPfadTests.cs` | 295 | Pfadprüfung und Adressbindung (5.10), Fristen (4.6), geordnetes Beenden (7.2), Sperre ohne Antwort (P-12), |
| `tests/Funk.Tests/ServerProfilabgleichTests.cs` | 107 | Abgleich von server.json und Serverprofil: Nennt profil.json einen anderen TCP-Port als oeffentlicherTcpPort, warnt |
| `tests/Funk.Tests/ServerTlsTests.cs` | 115 | Echtes TLS auf Loopback (4.1, P-43): Client-Zertifikat Pflicht und nur P-256, ALPN tacrelay/1, TLS 1.3 |
| `tests/Funk.Tests/ServerZusammenspielTests.cs` | 517 | Zusammenspiel des Dienstes auf Loopback: Anmeldung bis AKTIV, Ablehnung und Sperre, Verdrängung, UDP-Weg eines |
| `tests/Funk.Tests/SetupTests.cs` | 702 | Setup des Clients (installer\TacRelay.iss, installer\probefunk-starten.cmd, tools\setup-bauen.ps1), mechanisch am |
| `tests/Funk.Tests/SicherungTests.cs` | 345 | Regeln für Test- und Werkzeugläufe (C:\Claude\CLAUDE.md), mechanisch geprüft statt per Vorsatz: Geräte, Eingabegeräte |
| `tests/Funk.Tests/SperrlisteTests.cs` | 711 | Eingebaute Sperrliste (A51): Angriffe als Einheitstests mit fester Uhr (Handshake-Flut, Antragsflut, Protokollmüll, |
| `tests/Funk.Tests/Spielschutz.cs` | 102 | Sperre für Tests, die echte Geräte, Eingabegeräte oder Fenster anfassen (C:\Claude\CLAUDE.md, Abschnitt Bildschirm): |
| `tests/Funk.Tests/SpracheFormatTests.cs` | 341 | Formate 5.2 bis 5.6 und Prüfwert 8.8 (P-13, P-18). |
| `tests/Funk.Tests/SpracheHeisspfadTests.cs` | 69 | Der Paketpfad allokiert nichts (5.14: "Der Pfad vor dem Entschlüsseln allokiert nichts"), gemessen statt behauptet. |
| `tests/Funk.Tests/SpracheRandfallTests.cs` | 534 | Randfälle aus der Gegenprüfung: Fenster gegen ein Referenzmodell, Zählerüberlauf, Epochenwechsel über 256 Rotationen, |
| `tests/Funk.Tests/SpracheReplayTests.cs` | 133 | Prüfwert 8.7 (Replay-Fenster, W = 2048) und P-05. |
| `tests/Funk.Tests/SpracheSchluesselTests.cs` | 317 | Zähler, Schlüssel, Epochen und Rotation nach 5.7 und 5.8 mit simulierter Uhr (P-06). |
| `tests/Funk.Tests/SpracheVektorTests.cs` | 518 | Testschlüssel und Datagramme aus 8.3, dazu eine Empfangsumgebung für die Prüfkette 5.9. |
| `tests/Funk.Tests/SteuerAutomatTests.cs` | 581 | Zustandsautomat 4.6 und Prüfplan P-10, dazu F13 aus 8.10 und der Tunnel-Fall aus 8.3. |
| `tests/Funk.Tests/SteuerBremseTests.cs` | 137 | Grenzen aus 4.8, soweit sie Steuerrahmen und Bodies betreffen, mit simulierter Uhr. |
| `tests/Funk.Tests/SteuerFuzzTests.cs` | 157 | Kurzform von P-42 für den Steuerkanal: Mutationen gültiger Rahmen und Bodies durch Rahmenparser, JSON-Vorlauf, |
| `tests/Funk.Tests/SteuerGegenpruefungTests.cs` | 432 | Randfälle und Befunde der Gegenprüfung des Steuerkanals: Antwortpflicht bei GRENZE (4.2, 4.8), begrenzte |
| `tests/Funk.Tests/SteuerGroessenTests.cs` | 348 | P-02b und 4.10: Jede Nachricht an ihren Höchstwerten bleibt mit mehr als Faktor 2 unter der Rahmengrenze von |
| `tests/Funk.Tests/SteuerHilfe.cs` | 186 | Gemeinsame Hilfen der Steuerkanal-Tests. |
| `tests/Funk.Tests/SteuerJsonTests.cs` | 358 | Kodierung der JSON-Bodies (4.3), Prüfplan P-02 und P-44. |
| `tests/Funk.Tests/SteuerKatalogTests.cs` | 219 | Nachrichtenkatalog 4.4/4.5, Fehlercodes 4.9, Drahtnamen der Aufzählungen, Versionierung 4.7. |
| `tests/Funk.Tests/SteuerRahmenTests.cs` | 624 | Prüfwerte 8.4 (Steuerrahmen) und Prüfplan P-01 (Rahmen, Längengrenzen, Typbereiche, ein Write je Rahmen). |
| `tests/Funk.Tests/Stufe34DrahtTests.cs` | 281 | Stufe 3, Schritt 3.4 gegen den echten Server im Prozess (ohne TLS, ohne Fenster, ohne Geräte): Das Funkfeld wählt über die |
| `tests/Funk.Tests/Stufe35KonsoleTests.cs` | 120 | Kanalsperren an der Konsole (Fassung 0.6.0, Schritt 3.5, FUNKPROTOKOLL 6.17): kanalsperre-setzen, kanalsperre-aufheben und |
| `tests/Funk.Tests/Stufe36DrahtTests.cs` | 68 | Das Brett sperrt über den Dienst (Fassung 0.6.0, Schritt 3.6, FUNKPROTOKOLL 6.17) gegen den echten Server im Prozess, ohne TLS und |
| `tests/Funk.Tests/Stufe36KonsoleTests.cs` | 61 | Die Konsole nach der Übernahme der Vorlage Star Citizen (Fassung 0.6.0, Schritt 3.6, HEIMSERVER-PROXMOX Abschnitt 18): netzliste |
| `tests/Funk.Tests/Stufe37GegenpruefungTests.cs` | 220 | Unabhängige Gegenprüfung der Stufe 3 (Schritt 3.7, Fassung 0.6.0): Befunde je erst rot, dann grün, dazu Messungen. |
| `tests/Funk.Tests/Stufe3DrahtTests.cs` | 117 | Kanäle über den Draht (Fassung 0.6.0, 0x0035 mit Kanal-IDs, 0x0039 RolleWaehlen, NetzListe mit kanaele, gruppe und |
| `tests/Funk.Tests/Stufe3FeldSetzenTests.cs` | 116 | FeldSetzen mit Kanälen (Fassung 0.6.0, FUNKPROTOKOLL 4.5 0x0035 Regeln 1 bis 6, 6.16, P-61) am Zustandsschreiber: Kanal und Ruhe |
| `tests/Funk.Tests/Stufe3KanalIdTests.cs` | 186 | Stufe 3, Protokoll (Fassung 0.6.0, FUNKPROTOKOLL 6.16, 4.5, 4.8, 4.10, P-61): die Kanal-ID hin und zurück an ihren Rändern, die |
| `tests/Funk.Tests/Stufe3Kanalprobe.cs` | 97 | Kleine Flotte mit Kanalnetzen für die Lagetests der Stufe 3 (6.16), auf dem Grundzustand aus 8.9 (): |
| `tests/Funk.Tests/Stufe3KanalsperreDrahtTests.cs` | 116 | Kanalsperren über den Draht (Fassung 0.6.0, 0x003A KanalSperre, FUNKPROTOKOLL 6.17, P-63): mit der Client-Bibliothek, ohne TLS, |
| `tests/Funk.Tests/Stufe3KanalsperreTests.cs` | 369 | Kanalsperren in der Lage (Fassung 0.6.0, Schritt 3.2, FUNKPROTOKOLL 6.17, P-63), zuerst als Angriffe: ein Funker oder die Führung ohne |
| `tests/Funk.Tests/Stufe3KonsoleTests.cs` | 81 | Konsole mit Kanälen (Fassung 0.6.0, Schritt 3.3): netz-setzen --kanaele und --kanaele-datei (die erste Bezeichnung ist die |
| `tests/Funk.Tests/Stufe3LastTests.cs` | 115 | Last der Kanäle, im Speicher gemessen (Fassung 0.6.0, 6.16 "Last", 4.8): Jeder Ch.-Wechsel ist ein Commit mit einem Schreiben von |
| `tests/Funk.Tests/Stufe3RolleWaehlenTests.cs` | 182 | RolleWaehlen (Fassung 0.6.0, 0x0039, FUNKPROTOKOLL 6.16, P-61) und die Ch.-Auflösung am Zustandsschreiber: Die Rolle legt nur |
| `tests/Funk.Tests/Stufe3UebergangTests.cs` | 139 | Übergang von 0.5.0 auf 0.6.0 (FUNKPROTOKOLL 4.7, 6.16, P-62): Der Server lädt ein Zustandsverzeichnis, das Server und Protokoll im |
| `tests/Funk.Tests/Stufe3VersionstorTests.cs` | 102 | Versionstor (Fassung 0.6.0, FUNKPROTOKOLL 4.7, P-62) über den Draht, ohne TLS, prozessintern: Ohne Kanalnetz bleibt ein Client |
| `tests/Funk.Tests/Stufe3VerwaltungsteilTests.cs` | 185 | Verwaltungsteil mit Kanälen (Fassung 0.6.0, Schritt 3.3, STUFE3-ENTWURF 4.4): der Entwurf kennt Kanäle mit Ch. (Prüfung wie der |
| `tests/Funk.Tests/Stufe3VerwaltungTests.cs` | 188 | Kanalnetze verwalten (Fassung 0.6.0, Verwaltung NETZ_SETZEN und NETZ_LOESCHEN, FUNKPROTOKOLL 4.5, 6.16, P-61): anlegen mit seinen |
| `tests/Funk.Tests/Stufe3VorlageTests.cs` | 219 | Vorlage "Star Citizen" (Sarevoc, 19.10.2026, Q26): gegen die Vorgabe unter Daten\starcitizen\VORLAGE.md, die zehn Kanäle mit ihren Ch. und die |
| `tests/Funk.Tests/Stufe3WeiterleitungTests.cs` | 123 | Sprache in Kanälen (Fassung 0.6.0, FUNKPROTOKOLL 6.3, 6.4, 6.16, P-61): Hörer je Kanal, die Ruhe hört und sendet nie, geratene |
| `tests/Funk.Tests/Testflotte.cs` | 151 | Eine große Flotte mit Einheiten für die Tests des Flottenentwurfs, der Herkunft und der Konsole (seit 19.10.2026 nur noch hier, nicht mehr |
| `tests/Funk.Tests/Testlauf.cs` | 107 | Markiert eine statische Testmethode. Sie wirft bei Fehlschlag, sonst gilt sie als bestanden. |
| `tests/Funk.Tests/Uebergang010Tests.cs` | 213 | Übergang von 0.1.0 (Sarevocs Heimserver seit dem 30.09.2026, Stand 5053590) auf diesen Stand: Der Server lädt ein |
| `tests/Funk.Tests/Uebergang020Tests.cs` | 244 | Übergang von 0.2.0 (Sarevocs Heimserver seit dem 01.10.2026) auf 0.3.0: Der Server lädt ein Zustandsverzeichnis, das die |
| `tests/Funk.Tests/Uebergang040Tests.cs` | 204 | Übergang von 0.4.0 auf 0.5.0: Der Server lädt ein Zustandsverzeichnis, das die Programmteile von 0.4.0 selbst geschrieben haben |
| `tests/Funk.Tests/UmzugAnleitungTests.cs` | 252 | UMZUG.md so, wie Sarevoc und der Server-Admin es abschreiben. Befunde der Prüfung der Pakete 0.4.0 (mittel): Schritt 6 übersah mit dem Tag |
| `tests/Funk.Tests/UmzugTests.cs` | 466 | funk-admin umzug-packen und umzug-einspielen (EINRICHTEN-UMZUG-ENTWURF, Vorschlag Umzug): Zustand und |
| `tests/Funk.Tests/UrheberrechtTests.cs` | 107 | Urheberrecht (C:\Claude\CLAUDE.md: jeder Fremdbestandteil steht mit Lizenz in THIRD-PARTY-NOTICES, sonst gilt er als |
| `tests/Funk.Tests/Verarbeitung/Attrappen.cs` | 165 | Dekodierer-Attrappe: Ein Paket trägt in den ersten 4 Bytes die Frame-Nr (little endian). Dekodiert wird ein |
| `tests/Funk.Tests/Verarbeitung/BegrenzerTests.cs` | 164 |  |
| `tests/Funk.Tests/Verarbeitung/GateSzenenTests.cs` | 75 | Gate-Kennzahlen an synthetischen Szenen (audio-dsp.md, Empfehlung 8). Ziele aus ENTWURF.md: unter 0,5 |
| `tests/Funk.Tests/Verarbeitung/GateTests.cs` | 290 | Gate eingeschwungen: 150 Rahmen Grundrauschen ohne Sprache. |
| `tests/Funk.Tests/Verarbeitung/GegenpruefungTests.cs` | 760 | Tests aus der Gegenprüfung: nicht endliche Werte, Denormale, Vertragsbrüche an der Dekodierer-Naht, |
| `tests/Funk.Tests/Verarbeitung/JitterpufferTests.cs` | 377 | Jitterpuffer gegen simulierte Ankunftsspuren; alle Zeiten simuliert, alle Zufallswerte mit festem Seed. |
| `tests/Funk.Tests/Verarbeitung/KlangfarbeTests.cs` | 273 | Klangfarbe (Q70 bis Q72, 0.9.3) als Stufe für sich: Glocken an ihrer Stelle, lautheitsneutral mit Sprache aus dem Klangkorpus, neutral |
| `tests/Funk.Tests/Verarbeitung/KlickTests.cs` | 419 | Der Funk-Klick (A11, seit 0.5.0 einstellbar nach A63, Klang seit 0.6.0 Stufe 2b): ein kurzes rosa Rauschen statt der Klänge |
| `tests/Funk.Tests/Verarbeitung/MischerTests.cs` | 343 | Ein Mischer mit einem gleichbleibenden Funker und der Hörprobe (funk: false), Klick an; je Block die Ausgabe. |
| `tests/Funk.Tests/Verarbeitung/Netzsimulation.cs` | 140 | Simulierte monotone Uhr in Mikrosekunden; die Wiedergabe stellt sie vor jedem Lesen. |
| `tests/Funk.Tests/Verarbeitung/PegelstufeTests.cs` | 459 | Pegelstufe der Sendekette (Klangstufe A): Festverstärkung, satzweise Halteregel und Einmessen, ohne Klangmaschine. Die |
| `tests/Funk.Tests/Verarbeitung/RechenzeitTests.cs` | 107 | Rechenzeit je 10-ms-Block (p50, p99, Maximum), ausgegeben für den Bericht. Die Grenze ist großzügig und fängt |
| `tests/Funk.Tests/Verarbeitung/SendeaufbereitungTests.cs` | 204 |  |
| `tests/Funk.Tests/Verarbeitung/Testszenen.cs` | 194 | Selbst synthetisierte Geräuschszenen für die Gate-Messung (keine fremden Aufnahmen), 48 kHz, feste Seeds. |
| `tests/Funk.Tests/VerwaltungsdienstTests.cs` | 203 | Der Verwaltungsteil über einem echten Server (prozessintern, ohne TLS):  mit einem flüchtigen |
| `tests/Funk.Tests/VerwaltungsteilTests.cs` | 197 | Admin-Teile nur für Admins (A45, ANMELDUNG-SICHERHEIT): Der normale Client enthält keinen Verwaltungscode und lädt den |
| `tests/Funk.Tests/WeiterleitungEigenschaftenTests.cs` | 524 | P-08 Zufallstests gegen ein bewusst schlichtes Referenzmodell von 6.3/6.4, dazu Heißpfad ohne Allokation, |
| `tests/Funk.Tests/WeiterleitungFolgeTests.cs` | 308 | Prüfwerte 8.10 (Folge-Matrix, P-17) mit simulierter Uhr, Grundzustand wie 8.9. |
| `tests/Funk.Tests/WeiterleitungGegenpruefungTests.cs` | 454 | Gegenprüfung des Moduls weiterleitung: Befunde, die die ersten Tests nicht abdeckten, Prüfwerte 8.8 exakt |
| `tests/Funk.Tests/WeiterleitungRechteTests.cs` | 397 | Zeichnet alle Ausgaben der Weiterleitung auf. |
| `tests/Funk.Tests/WeiterleitungSchreiberTests.cs` | 872 | Zustandsschreiber (6.2, 6.7, 6.9, 6.10): P-09 Aufstellung atomar, P-19 Rang- und Rechteregeln, P-45 kein |
| `tests/Funk.Tests/ZuordnungBrettDrahtTests.cs` | 419 | Das Brett gegen den echten Server im Prozess (0.11.1 AP1, Sarevocs Befunde vom 12.10.2026 am echten PC; ohne TLS, ohne Geräte, ohne |
| `tests/Funk.Tests/ZuordnungServerTests.cs` | 266 | Zuordnung 0.11.0 am Zustandsschreiber (6.9): Wer offline eingeteilt wurde, kommt beim Login als VERSCHIEBEN mit durch |
| `tests/Funk.Tests/ZusammenspielKlangTests.cs` | 402 | Der Testzugang von funk-testserver, hier im Prozess: Zulassung wie bei TLS, Besitznachweis des Geräteschlüssels, |
| `tests/Funk.Testserver/Begleitprogramm.cs` | 58 | Argumente des Begleitprogramms (--client-argumente): durch Leerzeichen getrennt, doppelte Anführungszeichen |
| `tests/Funk.Testserver/Hilfe.cs` | 31 | Aufrufhilfe des Testservers. |
| `tests/Funk.Testserver/Probefreigabe.cs` | 71 | Freigabe eines Geräts ohne Handarbeit, nur für den Testserver: dieselben Schritte wie ein Admin mit |
| `tests/Funk.Testserver/Probekonfiguration.cs` | 128 | Probe-Konfiguration (konfig\probefunk.json) für den Testserver auf dem eigenen PC: Netze, Schiffe und Rollen in |
| `tests/Funk.Testserver/Probeserver.cs` | 301 | Der Testserver als Ganzes: der unveränderte , der  auf Loopback und |
| `tests/Funk.Testserver/Program.cs` | 222 |  |
| `tests/Funk.Testserver/Testzugangsannahme.cs` | 146 | Der Testzugang auf Loopback: statt TLS beweist der Client den Besitz seines Geräteschlüssels mit einer Signatur über einen |

<!-- DATEIEN-ENDE -->
