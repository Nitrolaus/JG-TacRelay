## Teil E: Unser Klang und das Schwellsystem

Dieser Teil erklärt, was JG TacRelay mit deiner Stimme macht, bevor die anderen sie hören, und wie die
Sprachaktivierung entscheidet, wann gesendet wird. E2 ist in Alltagssprache geschrieben und reicht zum Einstellen; die
Bedienung Schritt für Schritt steht auch in A3 bis A5. E3 ist technisch und für alle, die das Programm pflegen, prüfen
oder verstehen wollen, warum es sich so verhält.

## E1 Stand und Belege

- Beschrieben ist der Client der Fassung 0.5.0 (Code-Stand `95aa78b`, Datum der Fassung 02.10.2026; Stand des Handbuchs in
  1.9). Gegenüber 0.4.0 (Commit d45e33f) und 0.4.1 ändert sich in diesem Teil dreierlei. Erstens die Hörprobe seit 0.4.1: Sie
  ist eine Aufnahme von 5 Sekunden mit Wiedergabe danach, und das Mithören beim Senden, bis 0.4.0 die ganze Hörprobe, geht nur
  noch mit Headset (E2.9, E3.11). Zweitens der Funk-Klick seit 0.5.0: Er ist einstellbar (Lautstärke, Klang, an und aus für
  die eigene Taste und für andere Sprecher) und ab Werk deutlich lauter (E2.6, E3.9). Drittens die Tasten seit 0.5.0: neben der
  Sprechtaste gibt es die Melde-Taste und weitere Funktionen, am Joystick wie an Tastatur und Maus (E2.7, E3.10); neue Netze
  stehen an der Konsole ohne Schalter auf Sprachaktivierung (E2.3). Gate, Führung, Echo, Opus und Empfang sind unverändert;
  ihre Zahlen stammen aus dem Stand 0.4.0. Zwei Messungen hat der lautere Klick verschoben, sie sind in E3.11 und E3.13
  neu eingetragen.
- Die Wege in der Oberfläche (wo du etwas einstellst) beschreibt E2 seit dem 12.10.2026 nach Fassung 0.10.0: Start mit
  "Sprechen" und "Hören", die Funk-Lautstärke, eine Hörprobe auch verbunden, die Statuszeile "Mikrofon" statt der
  aufklappbaren Leiste, die Einstellungen in Abschnitten mit zugeklappten Bereichen und die Echounterdrückung in drei Stufen
  (E2.2, E2.4, E2.6, E2.7, E2.9, E2.10). Seit 0.11.1 (Sarevoc, 12.10.2026) stehen unter Einstellungen > Senden die Schalter der
  Klangmodule und die Funkschwelle wieder offen, ohne Aufklappen, und die Klappbereiche heißen "Erweitert:". Seit 0.11.2
  (13.10.2026) steht auf Start unten "Senden" mit dem Pegelbalken, seinen Griffen, "Einmessen" und den Schaltern
  Rauschunterdrückung, Echoschutz, Automatische Lautstärke und Mindest-Funklautstärke; "Audio starten" steht unter
  Einstellungen > Fehlersuche, und die Sätze "... es wird nichts gesendet" gibt es nicht mehr (Bereiche.xaml,
  PegelVerlauf.cs). Wo der frühere Weg gemeint ist, steht "bis 0.9.3" oder "bis 0.11.1" dabei. Klangkette und Zahlen
  ändern sich dadurch nicht, außer der Funk-Lautstärke im Mischer (bei 100 % bitgleich wie vorher, `MischerTests`).
- Jede Zahl stammt aus dem Code oder aus einer Testausgabe. Die Quelle steht dabei, als Dateiname in Klammern. Code liegt
  unter `src\`, Tests unter `tests\Funk.Tests\`, die Modulberichte unter `docs\`, die Anforderungen (Anf. A1 bis A65) in
  `docs\entwurf\ENTSCHEIDUNGEN.md`.
- "Testausgabe" heißt: Lauf vom 01.10.2026 im Arbeitsstand 0.4.0 (Debug-Bau), auf einem AMD Ryzen 9 5950X, ohne
  Audiogeräte. Die Messungen zur Hörprobe von 0.4.1 stammen aus dem Prüfstand, wie sie `PROJEKT.md` in der Standzeile
  vom 01.10.2026 18:30 festhält; hier sind sie nicht wiederholt. Die Messungen zum Funk-Klick von 0.5.0 stammen aus Läufen
  vom 01. und 02.10.2026 im Stand 0.5.0 (`KlickTests`, `KlickMaschineTests`, `HoerprobeTests`, `SpieltonTests`; Standzeile
  02.10.2026 00:10). Wie du die Läufe wiederholst, steht in E3.14.
- Alle Messungen arbeiten mit selbst erzeugten Signalen: synthetische Vokale, synthetisches Tippen, synthetisches
  Knistern einer Chipstüte, synthetischer Spielton. Echte Stimmen und echte Räume misst nur der Hörtest. Wo eine Zahl
  deshalb nur ein Hinweis ist, steht das dabei.

## E2 Für alle: in Alltagssprache

### E2.1 Was JG TacRelay mit deiner Stimme macht

Das Ziel ist ein sauberer, natürlicher Klang. Erlaubt ist Rauschunterdrückung, verboten sind künstliche Effekte, mit
einer Ausnahme: ein kleiner Funk-Klick beim Tastensprechen, wie bei echten Funkgeräten, und bei Sprachaktivierung sehr
dezent (Anf. A11); seit 0.5.0 stellst du ihn selbst ein (Anf. A63, E2.6). Nach dem ersten Hörtest am 29.09.2026 urteilte Sarevoc: "sehr sauber der Ton" (PROJEKT.md).

Der Weg deiner Stimme hat sieben Stationen:

1. **Mikrofon.** JG TacRelay nimmt das Mikrofon so, wie Windows es liefert, und zerlegt den Ton in Stücke von 10
   Millisekunden. Jedes Stück wird auf ein einheitliches Format gebracht: 48 000 Werte je Sekunde, ein Kanal.
2. **Echo.** Hörst du über Lautsprecher, hört das Programm mit, was die Lautsprecher gerade spielen, und rechnet genau
   das aus dem Mikrofon wieder heraus. Sonst hörten die anderen sich selbst. Mit Headset ist das unnötig und bleibt aus.
3. **Lärm.** Ein kleines, vorab trainiertes neuronales Netz (RNNoise) nimmt Lüfter, Brummen und Hintergrundrauschen
   weg. Nebenbei sagt es für jedes Stück, wie sicher es Sprache hört, von 0 bis 1.
4. **Schwelle.** Jetzt fällt die Entscheidung: Sprichst du gerade? Dafür muss das Stück laut genug sein (über der
   Schwellenlinie) und nach Sprache klingen. Ein einzelner Klick reicht nicht; erst wenn mehrere Stücke hintereinander
   passen, geht das Mikrofon auf.
5. **Vorlauf.** Weil das Programm die letzten 300 Millisekunden im Gedächtnis hat, sendet es beim Öffnen ab 30
   Millisekunden vor dem ersten passenden Stück. Die erste Silbe geht also nicht verloren, obwohl die Entscheidung einen
   Moment braucht.
6. **Verpacken.** Je 20 Millisekunden Stimme werden mit Opus zu einem kleinen Paket gepackt, in der Messung meist um
   125 Byte, und an den Server geschickt.
7. **Bei den anderen.** Ein Puffer gleicht das Zittern im Netz aus, ein Mischer legt alle Sprecher zusammen, jeder mit
   eigener Lautstärke, und am Ende steht ein sanfter Begrenzer, damit nichts verzerrt.

### E2.2 Headset oder Lautsprecher

Auf Start steht in der Spalte "Hören" die Wahl "Ich höre mit": Headset oder Lautsprecher. Beim ersten Start ist sie leer und
umrandet, und Verbinden kommt erst nach der Wahl (seit 0.10.0; bis 0.9.3 eine Karte oben im Hauptfenster). Die Antwort stellt die
Echounterdrückung ein und lässt sich auf Start jederzeit ändern (AudioViewModel.cs, Bereiche.xaml).

| Deine Wahl | Echounterdrückung | Warum |
| --- | --- | --- |
| Headset | aus | Es gibt kein Echo. Die Echounterdrückung kann bei laufendem Spielton die Anfänge deiner Sätze dämpfen. |
| Lautsprecher | an | Ohne sie hörten die anderen sich selbst. Für alle ohne Headset ist sie Pflicht (Anf. A14). |

Das gilt, solange die Echounterdrückung auf "Automatisch" steht, wie ab Werk. Seit 0.10.0 hat sie unter Einstellungen > Senden
drei Stufen (seit 0.11.1 oben links, ohne Aufklappen): "Automatisch" folgt der Wahl wie in der Tabelle (ohne Wahl an), "An" und
"Aus" gelten unabhängig davon (`audio.echounterdrueckungVonHand` in `client-einstellungen.json`; das alte Feld
`audio.echounterdrueckung` trägt weiter den wirksamen Wert, damit ältere Clients dasselbe tun). Darunter schaltest du Echoschutz
und Rauschunterdrückung (AudioViewModel.cs, ClientEinstellungen.cs).

### E2.3 Sprachaktivierung oder Sprechtaste

Für jedes Netz stellst du selbst ein, ob du dort mit Sprachaktivierung ("VA") oder mit der Sprechtaste sendest (Anf. A10). Der
Schalter steht an jedem Netz im Funkfeld (bis 0.9.3 auch im Bereich Meine Kanäle). Manche Netze erlaubt der Server nur mit Taste;
dort ist der Schalter gesperrt (KachelViewModel.cs). Für alle Netze zugleich schaltet "Senden mit Stimme" auf Start die
Sprachaktivierung ab; dann sendet jedes Netz nur mit der Taste.

- Die Sprachaktivierung gilt nur, wenn **alle** Netze, in die du gerade sendest, auf VA stehen. Steht eins davon auf
  Taste, sendet nur die Taste (FunkfeldViewModel.cs).
- Die Sprechtaste gewinnt immer: Drückst du sie, während die Sprachaktivierung gerade sendet, endet deren Spruch sofort,
  und dein Tastenspruch beginnt (KlangMaschineTests: Sprechtaste_BeendetLaufendePhaseDerSprachaktivierung).
- Mit der Sprechtaste gibt es keine Schwelle und keinen Vorlauf: Gesendet wird, solange du drückst, und danach noch den Nachlauf
  der Sprechtaste (seit 0.9.3, ab Werk 100 ms; E2.7).
- Ist noch keine Sprechtaste belegt, warnt das Programm auf Start und unter Einstellungen > Tasten > Erweitert: In Netzen auf
  Taste geht dann nichts hinaus (Bereiche.xaml). Sprichst du dann in ein solches Netz, sagt es seit 0.10.0 die Statuszeile:
  "Kanal braucht die Sprechtaste - keine belegt" (`Sendeweg.OhneSprechtaste`, PegelVerlauf.cs).
- **Was ab Werk gilt (seit 0.5.0, Anf. A62 und A64):** Ein neues Netz, das der Admin an der Konsole mit `funk-admin netz-setzen`
  anlegt, steht ohne Schalter auf Sprachaktivierung; nur mit `--taste` steht es ab Werk auf Taste, mit `--nur-taste` gibt es dort
  ohnehin nur die Taste. Global bleibt immer auf Taste. Die Vorlage Star Citizen legt alle Kanäle auf Sprachaktivierung an,
  auch Kommando (`Flottenvorlagen.cs`); auf Taste nur, wenn der Admin es so einstellt oder mit `--taste` anlegt, wie im
  Beispielbefehl der Anleitungen. Im Verwaltungsteil angelegte Netze standen schon vorher auf VA. Netze, die schon gespeichert sind,
  ändert das nicht: Sie bleiben, wie sie waren, auch die an der Konsole ohne `--va` angelegten, die auf Taste stehen. Wer dort VA
  will, stellt das Netz im Funkfeld auf VA, sofern es nicht "nur Taste" ist, oder der Admin legt es neu an (Befehle in B12.1;
  Code `Uebersetzung.cs`, Wächter `Uebergang040Tests`).

### E2.4 Die Schwelle in drei Stellungen

Die Schwelle stellst du auf der Seite "Sprachaktivierung" ein, einem Unterpunkt unter Einstellungen, den es gibt, solange ein
Kanal mit Sprachaktivierung sendet oder die Seite offen ist; Strg+T oder "Genau einstellen ..." unter Einstellungen > Senden
(seit 0.11.1 offen unter der Funkschwelle) öffnen sie, solange "Senden mit Stimme" an ist, auch vor dem Verbinden (seit der Prüfung von 0.10.0,
`HauptViewModel.SprachaktivierungEinstellen`). Kein
eigenes Fenster (Anf. A26); bis 0.9.3 ließ sie sich auch unten im Hauptfenster aufklappen, das gibt es seit 0.10.0 nicht mehr.
Pegel, Linie und "SENDET" stehen ständig in der Statuszeile "Mikrofon" unten im Hauptfenster (Hauptfenster.xaml,
StatuszeileViewModel.cs); seit 0.11.2 steht der Pegelbalken mit den Griffen der Funkschwelle auch unten auf Start unter
"Senden" (Bereiche.xaml). Auf der Seite siehst du die letzten 10 Sekunden als Verlauf: deinen Pegel, den Rauschboden, die
Schwellenlinie, die Sprachsicherheit und wann gesendet wurde. Es gibt genau zwei Schalter, **Automatik** und **Lernen**
(Anf. A27, A28; SchwellenViewModel.cs). Daraus ergeben sich drei Stellungen:

| Schalter | Name im Programm | Was die Linie tut | Gut für |
| --- | --- | --- | --- |
| Automatik aus | feste Schwelle | Sie steht, wo du sie hinziehst. | ruhige, gleichbleibende Umgebung; wer es genau so will |
| Automatik an | Automatik (Voreinstellung) | Sie folgt dem Grundrauschen im festen Abstand darüber. | die meisten; Lüfter und Raumgeräusch ändern sich, die Linie geht mit |
| Automatik und Lernen an | Automatik, lernt | Sie hebt sich bei Lärm im Hintergrund und sinkt danach wieder, immer innerhalb eines Korridors. | Leute im Raum, Kinder, Fernseher, Stimmen nebenan |

Lernen lässt sich nur mit Automatik einschalten. Schaltest du die Automatik aus, merkt sich das Programm die Wahl beim
Lernen und nimmt sie wieder auf, sobald die Automatik wieder an ist.

### E2.5 Was an unserer Schwelle besonders ist

Die meisten Sprachprogramme haben einen Regler und eine Linie. Unsere Schwelle macht ein paar Dinge anders:

- **Zwei Fragen statt einer.** Ein Stück wird nur gesendet, wenn es laut genug ist **und** nach Sprache klingt. Wie
  sicher die Erkennung sein muss, stellst du unter "Sprache ab" ein (Voreinstellung 0,5). Tippen und Knistern sind oft
  laut, klingen aber nicht nach Stimme.
- **Die Automatik misst den Rauschboden.** Sie sucht fortlaufend das leiseste Grundgeräusch der letzten zwei Sekunden und
  legt die Linie in festem Abstand darüber, voreingestellt 9 dB. Wird der Lüfter lauter, wandert die Linie mit. Damit
  kleine Reste der Verarbeitung in einem sehr ruhigen Raum nichts auslösen, bleibt die Linie in der Voreinstellung nie
  unter -60 dBFS.
- **Lernen lernt still.** Es gibt keine Knöpfe "war kein Sprechen" oder "abgeschnitten" (Entscheidung von Sarevoc am 29.09.2026, Anf. A28). Das
  Programm lernt aus allem, was es **nicht** sendet, wie laut dein Hintergrund ist. Bei Lärm steigt die Linie schnell,
  danach sinkt sie langsam, und sie bleibt immer im Korridor, den du festlegst. Seltene Klicks heben sie kaum, anhaltendes
  Geschrei im Raum schon. Solange die Obergrenze des Korridors unter deiner Sprechlautstärke liegt, zieht deine eigene
  Stimme die Linie nicht hoch.
- **Die Linie hat zwei Kanten.** Um zu öffnen, muss ein Stück über der Linie liegen. Um offen zu bleiben, reichen 3 dB
  weniger. So reißen leise Silben mitten im Satz nicht ab.
- **Erst prüfen, dann senden, ohne die erste Silbe zu verlieren.** Das Mikrofon geht erst auf, wenn innerhalb von 100
  Millisekunden mindestens vier Stücke zu 10 Millisekunden passen. Ein Klick oder ein Knistern schafft das nicht und wird
  abgewiesen. Weil das Programm danach den Vorlauf mitschickt, kommt der Anfang trotzdem an. Was diese Prüfung kostet, ist
  ein kleiner Verzug am Anfang, gemessen je nach Szene im Mittel 24 bis 44 Millisekunden, den der Puffer beim Empfänger
  wieder aufholt.
- **Nachlauf.** Nach dem letzten Wort bleibt das Mikrofon noch 300 Millisekunden offen, damit Satzenden und kurze Pausen
  nicht abgeschnitten werden.
- **Echoschutz.** Mit Lautsprechern prüft das Programm zusätzlich, ob das, was das Mikrofon hört, nur das Echo der
  Lautsprecher sein kann. Dann öffnet die Sprachaktivierung nicht. Wer spricht, während die anderen reden, muss etwas
  lauter sein als deren Echo; ein Spruch, der schon läuft, bleibt aber offen.

Was das in Zahlen bringt, an synthetischen Szenen gemessen (Einzelheiten in E3.13):

| Szene | Ergebnis |
| --- | --- |
| Stille | keine Fehlauslösung |
| Tippen auf der Tastatur | keine Fehlauslösung in allen drei Stellungen |
| knisternde Chipstüte | keine Fehlauslösung in allen drei Stellungen |
| Sprechen, auch beim Tippen | kein Satzanfang abgeschnitten |
| andere Stimmen im Hintergrund, 26 dB leiser als deine | Automatik 1,2 Fehlauslösungen je Minute, Lernen 0,4 je Minute |

Quelle: Testausgaben GateModusTests (Szenen_AlleModi_ZieleAusDemEntwurf,
Lernend_StimmenImHintergrund_WenigerFehlausloesungenAlsAutomatisch). Das Ziel aus dem Entwurf ist unter 0,5
Fehlauslösungen je Minute und unter 2 % abgeschnittene Satzanfänge (erster Entwurf, `docs\entwurf\ENTSCHEIDUNGEN.md`, Klang).

### E2.6 Was du hörst

- Jeder Sprecher bekommt beim Empfänger einen eigenen Puffer. Der wartet gerade so lange, wie das Netz zittert: bei
  ruhigem Netz 40 Millisekunden, bei unruhigem mehr, höchstens 200. Geht ein Paket verloren, wird es aus dem nächsten
  Paket rekonstruiert oder kurz überbrückt.
- Lautstärke stellst du je Funker und je Netz ein, dazu je Netz links, rechts oder beide Ohren (Anf. A4, A9), seit 0.10.0 unter
  Einstellungen > Empfangen > Erweitert. Den ganzen Funk zugleich stellt die Funk-Lautstärke auf Start (seit 0.10.0, Q74): 0
  bis 100 %, ab Werk 100 %; der Faktor ist (Prozent/100) zum Quadrat, 50 % sind also etwa -12 dB. Er wirkt im Mischer auf die
  ganze Summe aus Funk, Klick und Hörprobe vor dem Begrenzer, gleitend, damit nichts knackt; Windows und das Spiel bleiben, wie
  sie sind (Mischer.cs, `FunkLautstaerke`).
- Bei Durchsagen und Sprüchen im Netz Global werden die anderen Netze leiser, voreingestellt um 12 dB (KlangBruecke.cs,
  Mischer.cs).
- Der Klick: Beim Drücken und Loslassen deiner Sprech- oder Melde-Taste hörst du selbst einen kurzen Klick. Bei den anderen
  klickt es, wenn dein Spruch beginnt und wenn er endet; kam der Spruch aus der Sprachaktivierung, ist der Klick deutlich
  leiser. Der Klick entsteht bei jedem selbst, er geht nie durch die Leitung.
- **Den Klick einstellen (seit 0.5.0).** Seit 0.10.0 unter Einstellungen > Tasten > Erweitert, Gruppe "Funk-Klick" (bis 0.9.3
  in der Leiste "Sprachaktivierung" beziehungsweise im Bereich Audio; Bereiche.xaml, KlickViewModel.cs):
  - "Bei deiner Taste": Klick beim Drücken und Loslassen der Sprechtaste, der Melde-Taste und am Joystick, an oder aus, mit
    Lautstärke. Über Lautsprecher spielt der eigene Klick nicht.
  - "Bei anderen Sprechern": Klick, wenn jemand zu sprechen beginnt und aufhört, an oder aus, mit eigener Lautstärke.
  - "Bei Sprachaktivierung dezent": Spricht jemand per Sprachaktivierung, klickt es 18 dB leiser; ausgeschaltet klickt es dann
    gar nicht. Die Wahl betrifft nur die anderen Sprecher.
  - Die Wahl "Klang" (Klick, Doppelklick, Piep) aus 0.5.0 gibt es seit 0.6.0 nicht mehr; der Klick ist ein kurzes Rauschen.
  - "Anhören" spielt den Klick in der eingestellten Lautstärke; es geht nur, solange die Wiedergabe läuft, sonst steht daneben,
    warum nicht.
  - Die Lautstärke steht in ganzen dB von -30 bis -6 und ist auf eine Stimme bezogen: -12 dB heißt 12 dB leiser als Sprache.
    Ab Werk sind es -12 dB bei deiner Taste und -15 dB bei anderen Sprechern; bis 0.4.1 lag der Klick gut 22 dB unter der
    Sprache und ging neben Spielton unter (E3.9).
  - Die Einstellung steht auf deinem PC in `client-einstellungen.json` unter `klick`; ein Client 0.4.0 oder 0.4.1 liest die
    Datei weiter.
- Am Ende sitzt ein weicher Begrenzer. Auch wenn viele gleichzeitig laut sind, verzerrt die Ausgabe nicht.
- Windows soll Star Citizen nicht leiser stellen, solange JG TacRelay läuft. Das Programm meldet sich dafür als
  Spiel-Chat an und wählt die automatische Dämpfung anderer Programme ab (WasapiSystem.cs).

### E2.7 Die Sprechtaste

- **Was geht:** jede Taste der Tastatur, die Maustasten 1 bis 5 und jeder Knopf oder Hut an Joystick, Throttle, Pedalen
  und anderen Spielgeräten (Anf. A3). Linke und rechte Strg-Taste sind verschiedene Tasten, das Tastaturlayout spielt keine
  Rolle (Tastenbelegung.cs).
- **Auch im Spiel:** Die Taste wirkt, wenn Star Citizen vorn ist. JG TacRelay fängt dabei nichts ab: Star Citizen bekommt
  dieselbe Taste weiterhin (RawInputSprechtaste.cs). Joysticks liest JG TacRelay so, wie ein Spiel sie liest, neben Star
  Citizen und jedem anderen Programm, und nur lesend (DirectInputSprechtaste.cs).
- **Belegen:** Seit 0.10.0 auf Start in der Spalte "Sprechen". Beim ersten Start wartet Start von selbst und ohne Frist auf die
  Taste, solange Start vorn und das Fenster aktiv ist; Esc bricht ab, "Später" lässt sie weg (Q76). Danach ersetzt "Ändern" alle
  Sprechtasten durch eine neue; ohne Taste steht dort eine Warnung mit "Belegen". Mehrere Sprechtasten belegst du unter
  Einstellungen > Tasten > Erweitert, Gruppe "Sprechtaste", mit "Belegen" (bis 0.9.3 der Abschnitt "Globale Tasten"); dort bricht
  Esc ab, nach 10 Sekunden ohne Taste ebenso (StartViewModel.cs, TastenViewModel.cs). Das Mikrofon schließt erst, wenn alle
  Sprechtasten losgelassen sind. Eine Taste gehört nur einer Funktion; belegst du sie woanders, wandert sie dorthin. Linke,
  rechte und mittlere Maustaste zählen beim Belegen nur außerhalb des Programms (seit 0.10.0; bis 0.9.3 nur die linke).
- **Melde-Taste (seit 0.5.0):** Solange du sie hältst, sprichst du ins Meldenetz, ohne dein Sendenetz zu ändern. Ab Werk ist das
  Kommando, wenn die Flotte ein Netz dieses Namens hat; unter Einstellungen > Tasten > Erweitert, Gruppe "Kanal und Melde-Taste",
  wählst du es in "Melde-Taste spricht in". Die Sprachaktivierung ruht, solange du meldest. Das Mikrofon ist offen, solange eine von beiden Tasten, Sprech- oder
  Melde-Taste, gehalten wird. Hast du in dem Netz kein Senderecht oder bist nicht verbunden, geht nichts hinaus, und die
  Meldezeile über dem Spiel sagt warum (etwa "Melden geht nicht: kein Senderecht in Kommando"). Der Rest einer Meldung geht nach dem
  Loslassen noch ins Meldenetz, nicht an die Crew; drückst du gleich danach die Sprechtaste, gehört der neue Spruch wieder zur
  Sendeauswahl (Sprechsteuerung.cs, Gegenprüfung S2). Ist die Sprechtaste schon gehalten, klickt die Melde-Taste nicht noch
  einmal: Ein Klick heißt Mikrofon auf oder zu. Das Meldenetz ist nur ein Ziel; was dort gehört wird, entscheidet der Server (C13).
- **Nachlauf der Sprechtaste (seit 0.9.3, Q66):** Nach dem Loslassen von Sprech- oder Melde-Taste sendet die Sendekette noch so lange
  weiter wie mit gedrückter Taste, ab Werk 100 ms, einstellbar von 0 bis 1000 ms in Schritten zu 10 ms (seit 0.10.0 Einstellungen >
  Tasten > Erweitert, Gruppe "Sprechtaste"; `tasten.nachlaufMs` in `client-einstellungen.json`). Grund: APM mit AEC3 verzögert die Stimme um
  9 ms, RNNoise um 20 ms, DeepFilterNet3 um 30 ms, dazu die Vorausschau von Opus; der letzte Rahmen wird beim Ende über 10 ms
  ausgeblendet. Gemessen im Prüfstand (`MessungNachlauf`, Taste genau am Wortende losgelassen, 54 Ein-Wort-Rufe und 8 Satzenden):
  ohne Nachlauf fehlten bis 5 ms (Rauschfilter aus), bis 25 ms (Standard) und bis 35 ms (Stark) Sprache, und bis 50 ms nach dem
  Loslassen kam noch Sprache aus der Kette; mit 50 ms Nachlauf fehlte nirgends mehr etwas. Die 100 ms ab Werk sind das aufgerundet
  plus Reserve für den Aufnahmepuffer des echten Geräts, den der Prüfstand nicht hat. Ein neuer Druck derselben Taste im Nachlauf
  setzt dieselbe Phase fort (kein ENDE, kein neuer BEGINN, kein Klick), auch an der Melde-Taste, deren Rest im Meldenetz bleibt.
  Drückst du im Nachlauf die andere Taste (Melde- statt Sprechtaste oder umgekehrt), ist das ein anderes Ziel: Die laufende Phase
  endet im nächsten Rahmen mit ENDE an ihrem Ziel, der Rahmen danach beginnt die neue am neuen Ziel, und es klickt beim Loslassen und
  beim Drücken; was bis dahin aus der Kette kam, auch das Wortende im Nachlauf, bleibt am alten Ziel (seit der Prüfung 0.9.3,
  Sprechsteuerung.cs und `IKlangMaschine.SprechtasteGedrueckt(bool)`). Mikro stumm, Audio stoppen und ein Abbruch oder Wechsel des
  Mikrofons beenden den Nachlauf sofort; der Klick beim Loslassen kommt erst danach (bei stumm und gestoppt keiner). Die
  Sprachaktivierung hat ihren eigenen Nachlauf von 300 ms und bleibt unberührt; spricht man nach dem Loslassen weiter, beginnt sie
  nach dem Nachlauf ihre eigene Phase (Sendekette.cs, `TasteLesen`; KlangMaschine.cs, `TastenNachlaufMs`).
- **Netz wechseln:** Sendenetz vor und zurück liegen voreingestellt auf Numpad + und Numpad - (Anf. A7; ClientEinstellungen.cs).
- **Weitere Funktionen (seit 0.5.0):** Das Funkfeld in den Infobereich der Taskleiste ablegen und zurückholen, und für die
  Einsatzleitung "Phase vor" und "Phase zurück" (zweistufig: erst scharf machen, dann schalten; A12). Sie lassen sich wie die
  Sprechtaste auf Tastatur, Maus und Joystick legen und stehen in `client-einstellungen.json` in der Liste `tasten.weitere`,
  damit ein Client 0.4.0 die Datei weiter liest (ClientEinstellungen.cs). JG TacRelay fängt dabei nichts ab: Star Citizen
  sieht dieselben Knöpfe; nimm Knöpfe, die im Spiel frei sind.
- **Schutz vor Versehen am Joystick:** Ein Knopf, der beim Belegen oder beim Anstecken schon gedrückt ist, zählt erst nach
  einmal Loslassen. Funktionen mit einem einzelnen Druck (Sendenetz weiter, Funkfeld, Phasen) reagieren frühestens 150 ms nach
  dem vorigen Druck desselben Knopfes; so zählt ein prellender Knopf nicht doppelt (Joystickauswertung.cs, `EntprellMs`). So sendet das Belegen selbst nicht, und ein Kippschalter in Dauerstellung funkt nicht los
  (Joystickauswertung.cs). Ziehst du einen Joystick ab, während du drückst, gilt die Taste als losgelassen.
- **Hängeschutz bei Tastatur und Maus:** Hinter der Windows-Sperre oder einer Benutzerkontensteuerung meldet Windows kein
  Loslassen. Das Programm sieht dann selbst nach und lässt eine hängende Taste nach etwa einer halben Sekunde los
  (RawInputSprechtaste.cs).
- **Ohne Taste zur Hand:** Den Knopf "Sprechtaste halten" im Bereich Audio gibt es seit 0.10.0 nicht mehr.

### E2.8 So stellst du die Sprachaktivierung ein

1. Auf Start bei "Ich höre mit" "Headset" oder "Lautsprecher" wählen, falls es noch nicht gewählt ist.
2. Mit Strg+T die Seite "Sprachaktivierung" öffnen (oder Einstellungen > Senden, "Genau einstellen ..." unter der Funkschwelle). Den
   Unterpunkt gibt es nur, solange ein Kanal mit Sprachaktivierung sendet.
3. Einen Moment still sein. Im Verlauf siehst du deinen Rauschboden; mit Automatik liegt die Linie darüber.
4. Normal sprechen, so laut wie im Spiel. Die Abschnitte, in denen gesendet wurde, sind im Verlauf markiert.
5. Tippen, mit der Maus klicken, im Stuhl rutschen. Wird dabei gesendet, die Linie etwas höher ziehen oder "Sprache ab"
   höher stellen.
6. Fehlt der Anfang deiner Sätze, die Linie etwas tiefer ziehen.
7. Wer Stimmen oder Lärm im Raum hat: Lernen einschalten und den Korridor mit seinen zwei Griffen so legen, dass die
   Obergrenze unter deiner normalen Sprechlautstärke bleibt.
8. Zum Gegenhören mit Headset "Beim Senden mithören" einschalten (Einstellungen > Senden > Erweitert, Gruppe "Kontrolle"):
   Dann hörst du genau, was die Schwelle durchlässt. Ohne Headset prüfst du das im Netz Echo. Wie du klingst, zeigt die
   "Hörprobe" auf Start; die Schwelle prüft sie nicht, denn sie nimmt ohne Schwelle auf (E2.9).
9. Prüf vorher, ob dein Sendenetz auf VA steht. Steht es auf Taste, sendet die Sprachaktivierung dort nicht, wie gut die
   Linie auch liegt (E2.3, A5.2).

Bedienung der Grafik (SchwellenGrafik.cs, Schwellenbereich.xaml):

| Was | Maus | Tastatur |
| --- | --- | --- |
| Linie verschieben | Linie ziehen | Pfeil hoch oder runter: 1 dB; mit Strg: 5 dB |
| Korridor, Obergrenze (nur Lernen) | oberen Griff rechts ziehen | Bild hoch oder runter: 1 dB |
| Korridor, Untergrenze (nur Lernen) | unteren Griff rechts ziehen | Strg+Bild hoch oder runter: 1 dB |
| alles zurück | Knopf "Standardwerte" | - |

Mit Automatik verschiebt das Ziehen der Linie den Abstand zum Rauschboden; ohne Automatik setzt es die feste Schwelle
(SchwellenViewModel.cs). "Standardwerte" setzt alle Werte zurück, lässt aber die beiden Schalter, wie sie sind.

Die Werte und ihre Voreinstellungen (ClientEinstellungen.cs, Klasse GateWerte):

| Wert im Programm | Gilt bei | Voreinstellung | Bereich |
| --- | --- | --- | --- |
| Schwelle | Automatik aus | -45 dBFS | -90 bis 0 dBFS |
| Abstand zum Rauschboden | Automatik | 9 dB | 3 bis 30 dB |
| Korridor von | Lernen | -55 dBFS | -90 bis -3 dBFS |
| Korridor bis | Lernen | -30 dBFS | -87 bis 0 dBFS, mindestens 3 dB über "von" |
| Anstieg | Lernen | 2 s | 0,1 bis 30 s |
| Rückkehr | Lernen | 5 s | 0,1 bis 60 s |
| Sprache ab | immer | 0,5 | 0 bis 1 |

"dBFS" heißt Dezibel unter Vollaussteuerung. 0 dBFS ist das Lauteste, was geht; je negativer, desto leiser. Angezeigt
wird der mittlere Pegel eines Stücks von 10 Millisekunden, so wie er nach Echo- und Rauschunterdrückung aussieht
(Pegel.cs, Sendekette.cs).

### E2.9 Hörprobe und Mithören

Seit 0.4.1 gibt es zwei Wege, dich selbst zu hören. Seit 0.10.0 steht die Hörprobe nur noch auf Start in der Spalte "Hören"
unter "So hören dich die anderen" und geht auch verbunden (Q75); das Mithören steht unter Einstellungen > Senden > Erweitert,
Gruppe "Kontrolle". Bis 0.9.3 standen beide auf der Karte "Hörprobe" im Bereich Audio und noch einmal unten im Bereich
"Sprachaktivierung", und die Hörprobe ging nur ohne Verbindung (HoerprobeViewModel.cs, StartViewModel.cs, Bereiche.xaml).

**Hörprobe.** Nach dem Klick nimmt JG TacRelay 5 Sekunden deiner Stimme auf und spielt sie danach ab, so wie
die anderen dich empfangen: nach Echo- und Rauschunterdrückung, mit Opus kodiert, durch den Empfangspuffer dekodiert
(IKlangMaschine.cs, Sendekette.cs). Neben dem Knopf steht vorher: "Sprich fünf Sekunden, dann hörst du dich. Geht auch
verbunden." Gesendet wird dabei nur, was du mit der Sprechtaste sprichst.

- Aufgenommen wird vor der Schwelle, also ohne Gate: Du hörst deinen Klang, unabhängig von Sprechtaste, Schwelle und
  davon, ob dein Sendenetz auf VA oder Taste steht.
- Aufnahme und Wiedergabe laufen nie zugleich, und die Wiedergabe wird nicht wieder aufgenommen. Deshalb geht das auch mit
  Lautsprechern, ohne dass sich der Ton im Kreis dreht.
- Ab dem Klick bis eine halbe Sekunde nach dem Ende der Wiedergabe beginnt die Sprachaktivierung keinen neuen Spruch; so
  gehen weder die Probe noch das Echo ihrer Wiedergabe an die anderen. Ein Spruch, der beim Klick schon läuft, und die
  Sprechtaste bleiben unberührt.
- Solange die Probe läuft, heißt der Knopf "Abbrechen"; ein Klick bricht ab, und eine abgebrochene Aufnahme
  wird nicht abgespielt. Endet das Audio, endet die Probe ebenfalls (HoerprobeViewModel.cs, HoerprobeTests.cs).

**Beim Senden mithören.** Dieser Schalter spielt dir mit, was gerade wirklich hinausgeht, also nach Schwelle, Opus und
Empfangspuffer, mit allem, was dabei abgeschnitten würde. Er setzt immer erst mit einem neuen Spruch ein, nie mitten im
Satz (Sendekette.cs), und geht nur mit Headset: Über Lautsprecher käme die eigene Stimme verzögert zurück ins Mikrofon.
Der Text darunter folgt dem Schalter; ausgeschaltet beginnt er mit "Aus." und sagt, wann du dich eingeschaltet hören
würdest (mit der Sprechtaste oder per Sprachaktivierung). Du hörst dich mit rund 0,1 Sekunden Verzug (E3.11). Der Schalter
gilt nur, bis das Programm endet.

Bis 0.4.0 gab es nur das Mithören, unter dem Namen "Hörprobe" und auch mit Lautsprechern. Es blieb still, solange nicht
gesendet wurde, etwa weil das Sendenetz auf Taste stand und die Taste nicht gedrückt war (E3.11).

### E2.10 Wenn etwas nicht passt

| Was du merkst | Ursache laut Programm | Was du tust |
| --- | --- | --- |
| Beim Tippen oder Knistern wird gesendet. | Das Geräusch liegt über der Linie und klang für die Erkennung nach Sprache. | Linie etwas höher oder "Sprache ab" höher; bei wechselndem Lärm Lernen einschalten. |
| Der Anfang deiner Sätze fehlt. | Die Linie liegt zu hoch, oder du sprichst sehr weich an. | Linie tiefer oder Abstand kleiner; mit Headset über "Beim Senden mithören" prüfen, sonst im Netz Echo. Die Hörprobe nimmt ohne Schwelle auf und zeigt das nicht. |
| Mit Sprachaktivierung geht nichts hinaus, obwohl du über der Linie sprichst. | Das Sendenetz steht auf Taste: ab Werk bei Global und bei Netzen, die der Admin mit `--taste` oder `--nur-taste` angelegt hat (auch Kommando, wenn er es so angelegt hat), und bei allen Netzen, die vor 0.5.0 an der Konsole ohne `--va` angelegt wurden und gespeichert sind (FunkfeldViewModel.cs, A5.2, E2.3). Ohne Verbindung gibt es kein Sendenetz. | Das Netz im Funkfeld auf VA stellen, sofern es nicht "nur Taste" ist, oder die Sprechtaste nehmen. |
| Die "Hörprobe" startet nicht. | Mikrofon oder Wiedergabe laufen nicht; beim Knopf steht, was fehlt, bei gestopptem Audio seit 0.11.2 "Geht erst, wenn das Audio läuft." (HoerprobeViewModel.cs). | Unter Einstellungen > Fehlersuche "Audio starten" (bis 0.11.1 auf Start) oder das fehlende Gerät wählen. |
| Du sprichst, und es geht nichts hinaus. | Die Marke in der Statuszeile "Mikrofon" zeigt "Ruhe" oder "Getrennt", oder der Balken wird gelb statt grün: kein Sendekanal, nur Ruhe, stummgeschaltet oder nicht verbunden. Einen Satz dazu gibt es seit 0.11.2 nur noch in zwei Fällen: "Kanal braucht die Sprechtaste - keine belegt" und "Stimme unter der Mindest-Funklautstärke" (seit 0.10.0; PegelVerlauf.cs). Die Sätze "Ruhe: es wird nichts gesendet", "Stummgeschaltet: ..." und "Nicht verbunden: ..." stehen seit 0.11.2 nirgends mehr. | Kanal wählen, verbinden, Sprechtaste belegen, lauter sprechen oder unter Einstellungen > Senden (seit 0.11.2 auch auf Start) an der Funkschwelle neu einmessen. |
| "Beim Senden mithören" lässt sich nicht einschalten. | Lautsprecher gewählt oder auf Start noch nichts gewählt. | Mit Lautsprechern die Hörprobe nehmen; mit Headset auf Start "Headset" wählen. |
| Mit Lautsprechern und laufendem Spiel fehlen Satzanfänge häufiger. | Der Echoschutz verlangt, dass du über dem Spielecho liegst; das ist gemessen der schwierigste Fall (siehe technischer Teil). | Wenn möglich Headset; sonst Lautsprecher leiser oder Mikrofon näher. |
| Die anderen hören sich selbst. | Echounterdrückung aus, obwohl Lautsprecher laufen. | Auf Start "Lautsprecher" wählen; steht die Echounterdrückung unter Einstellungen > Senden von Hand auf "Aus", dort "Automatisch" oder "An". |
| Mit Headset leiden die Satzanfänge, wenn das Spiel läuft. | Echounterdrückung an, obwohl kein Echo da ist. | Auf Start "Headset" wählen; steht sie von Hand auf "An", dort "Automatisch". |
| Stimmen im Raum werden mitgesendet. | Fremde Stimmen klingen nach Sprache; die Automatik kennt nur das Grundrauschen. | Lernen einschalten, Korridor-Obergrenze unter deine Sprechlautstärke. |
| In einem Netz geht nichts hinaus. | Das Netz steht auf Taste, und es ist keine Sprechtaste belegt; die Statuszeile sagt "Kanal braucht die Sprechtaste - keine belegt". | Sprechtaste belegen (auf Start). |
| Die Melde-Taste sendet nichts. | Du bist nicht verbunden, es gibt kein Meldenetz, oder du hast dort kein Senderecht; die Meldezeile über dem Spiel nennt den Grund. | Unter Einstellungen > Tasten > Erweitert ein Meldenetz wählen, in dem du senden darfst. |
| Der Klick ist zu leise, zu laut oder stört. | Die Voreinstellung liegt bei -12 dB (eigene Taste) und -15 dB (andere Sprecher). | Einstellungen > Tasten > Erweitert, Gruppe "Funk-Klick": Lautstärke oder ganz aus; mit "Anhören" probierst du es aus (E2.6). |
| Der ganze Funk ist zu laut oder zu leise gegen das Spiel. | Die Funk-Lautstärke steht ab Werk auf 100 %. | Auf Start den Regler "Funk-Lautstärke" (seit 0.10.0, E2.6). |
| Hinweis "Windows verweigert den Zugriff auf das Mikrofon". | Windows-Datenschutz sperrt das Mikrofon für Desktop-Apps. | In Windows unter Datenschutz und Sicherheit > Mikrofon den Zugriff für Desktop-Apps erlauben, dann "Erneut versuchen". Hören geht in der Zwischenzeit weiter. |
| Kopfhörer oder Mikrofon gewechselt. | Windows meldet den Wechsel. | Nichts. Ist kein bestimmtes Gerät gewählt oder fehlt das gewählte, nimmt das Programm das Windows-Standardgerät für Kommunikation; fehlende Geräte versucht es selbst erneut. Seit 0.10.0 liest es dabei auch die Liste der Geräte auf Start neu, auch ohne laufendes Audio (`IKlangMaschine.GeraeteListeGeaendert`); "Geräte neu lesen" gibt es nicht mehr. |

Die Texte der letzten beiden Zeilen stammen aus KlangMaschine.Geraete.cs und Bereiche.xaml.

### E2.11 Klangfarbe (seit 0.9.3)

Ab Werk lässt JG TacRelay den Klang deiner Stimme, wie er aus Mikrofon und Rauschfilter kommt. Wer mag, stellt die Klangfarbe
selbst ein: seit 0.11.1 unter Einstellungen > Senden > Erweitert, rechts die Gruppe **Klangfarbe** (0.10.0 und 0.11.0 im
Klappbereich rechts unter der Lautstärke, bis 0.9.3 im Bereich Audio unter der automatischen Lautstärke). Dort gibt es drei
Regler, je von -6 bis +6 dB in ganzen Schritten, und den Knopf **Zurück auf neutral** (Bereiche.xaml, `KlangfarbeTeil`):

- **Wärme** macht die Stimme voller und runder, aber nicht tiefer; die Stimmlage bleibt. Gut, wenn du über ein Headset dünn
  klingst; weniger, wenn es nah am Mikrofon dröhnt.
- **Klarheit** hebt die Mitlaute an, also das, was Wörter verständlich macht. Das hilft, wenn dich die anderen im Gefecht
  neben Schüssen und Triebwerken schlecht verstehen; zu viel klingt hart.
- **Schärfe** dämpft oder betont die Zischlaute (s, sch, z). Weniger, wenn dein Mikrofon zischt; mehr, wenn du dumpf klingst.

Alle drei ändern nur den Klang, nicht die Lautstärke: JG TacRelay gleicht so aus, dass eine normale Stimme insgesamt gleich
laut bleibt (E3.4). Sprachaktivierung, Schwelle und Pegelanzeige sehen deine Stimme genau wie ohne Klangfarbe; die Hörprobe
hört sie mit (E2.9). Ist etwas verstellt, zählt es die Kopfzeile von Senden > Erweitert mit (`Erweitert: (1 selbst gestellt) ...`),
und der Satz neben **Zurück auf Werkseinstellung** nennt es, etwa `1 selbst gestellt: Wärme +2 dB. Alles andere steht ab Werk.`
Gespeichert wird
die Klangfarbe in `client-einstellungen.json` unter `audio` (`klangfarbeWaermeDb`, `klangfarbeKlarheitDb`, `klangfarbeSchaerfeDb`);
ein älterer Client übergeht die Felder. Stell immer nur einen Regler um und hör dann mit der Hörprobe nach.

## E3 Technisch

### E3.1 Die Kette im Überblick

```
Mikrofon (WASAPI, geteilt)           Loopback der Wiedergabe (nur mit Echounterdrückung)
   |                                    |
   v                                    v
Eingangswandler: Kanäle gemittelt,   Eingangswandler wie links,
48 kHz (speexdsp, Qualität 5),       Ring mit 32 Rahmen
Rahmen zu 480 Werten (10 ms)            |
   |                                    |
   +-- Pegel vor APM --> Echoschutz <---+-- Pegel und Sprachart (zweites RNNoise)
   |                                    |
   v                                    v
APM: AEC3, Hochpass  <-------------- ein Referenzrahmen vor jedem Mikrofonrahmen
   |                                  (AGC2 seit Klangstufe A aus)
   v
Pegelstufe (automatische Lautstärke) und Begrenzer
   |
   v
RNNoise: entrauschen (20 ms Verzug) und Sprachwahrscheinlichkeit
   |
   +-- Stimmprobe (nur auf Knopfdruck)
   +-- Pegel fürs Gate, die Pegelanzeige und das Journal
   v
Klangfarbe (seit 0.9.3, neutral überbrückt) mit Begrenzer
   |
   v
Gate-Führung (Fest, Automatik, Lernend) -> Sprach-Gate (Bestätigung, Vorlauf, Nachlauf)
   |
   v
Sendeaufbereitung: 2 x 10 ms -> Opus 20 ms, Kennzeichen BEGINN, PREROLL, VA, ENDE
   |
   +--> Sende-Ring (128 Plätze) -> Melde-Thread -> Transport zum Server
   +--> Mithören (Oberfläche: nur mit Headset):
        Jitterpuffer der Hörprobe -> Mischer-Netz 63 -> eigene Wiedergabe

Abzweig vor dem Gate, nur während einer Hörprobe als Aufnahme:
hinter APM, RNNoise und Klangfarbe -> zweiter Opus-Kodierer -> Speicher für 5 s (250 Pakete)
   -> danach im 20-ms-Takt in den Jitterpuffer der Hörprobe -> Mischer-Netz 63
```

Quelle: KlangMaschine.cs, KlangMaschine.Geraete.cs, Sendekette.cs, Eingangswandler.cs, IKlangMaschine.cs.

| Stufe | Baustein | Einstellung | Algorithmischer Verzug |
| --- | --- | --- | --- |
| Aufnahme | WASAPI geteilt, ereignisgesteuert, NAudio 3.1 | float32 im Mischformat des Geräts, RAW-Modus wenn möglich | - |
| Wandlung | eigener Eingangswandler, speexdsp-Resampler | 48 kHz mono, 10-ms-Rahmen | Resampler nur bei anderer Geräterate |
| Echo, Hochpass | WebRTC APM (webrtc-audio-processing 2.1) mit eigener C-Schicht | AEC3 (nur mit Echounterdrückung), Hochpass 100 Hz, WebRTC-NS aus, AGC2 aus (bis 0.6.0 adaptiv bis 30 dB; seit Klangstufe A übernimmt die Pegelstufe, KlangMaschinenOptionen.cs) | 430 Samples, 9,0 ms |
| Pegel | eigene Pegelstufe mit Begrenzer (Klangstufe A) | Halteregel, ändert nur in Pausen | - |
| Rauschen, Spracherkennung | RNNoise | 480 Samples je Aufruf | 960 Samples, 20 ms |
| Klangfarbe (seit 0.9.3) | drei Glocken nach dem Audio-EQ-Kochbuch, eigener Begrenzer | ab Werk neutral, dann überbrückt (E3.4) | - |
| Gate | eigene Zustandsmaschine | siehe unten | Bestätigung mindestens 40 ms, durch den Vorlauf nachgeholt |
| Codec | libopus 1.6.1 | 48 kbit/s VBR, 20 ms | Lookahead 312 Samples, 6,5 ms |

Quellen der Verzüge: docs\MODULBERICHT-klang.md (per Kreuzkorrelation gemessen; RNNoise 960 statt der oft genannten 480
Samples, am Quelltext nachvollzogen). Zusammen kommt die Aufnahmekette laut diesem Bericht algorithmisch auf etwa 29 ms
plus den 10-ms-Rahmen.

Alle Audio-Threads (Aufnahme, Loopback, Wiedergabe) laufen mit MMCSS "Pro Audio", rechnen ohne Allokation und ohne
Sperre. Geräte öffnet und schließt ein eigener Steuer-Thread; Pakete und Pegelmeldungen gibt ein Melde-Thread weiter
(KlangMaschine.cs). Gemessen: 0 Byte Allokation in 12 s über alle Audiopfade (Testausgabe KlangMaschineTests,
Echtzeit_AudioPfade_AllokierenNicht).

### E3.2 Aufnahme: WASAPI geteilt, 48 kHz

- Geöffnet wird das Gerät, das du gewählt hast, sonst das Windows-Standardgerät für **Kommunikation** (WasapiSystem.cs).
- Geteilter Modus, ereignisgesteuert, Pufferlänge 40 ms bei der Aufnahme. Das Format ist das Mischformat des Geräts in
  float32; Windows wandelt also nichts, die Rate rechnet JG TacRelay selbst um.
- Die Aufnahme versucht den RAW-Modus, also ohne Klangverbesserungen von Windows, und fällt ohne ihn zurück, wenn das
  Gerät ihn ablehnt.
- Vor dem Start jedes Stroms wird die Ducking-Abwahl gesetzt; danach wäre es zu spät, und Windows hätte andere Programme
  beim Öffnen des Kommunikationsstroms schon gedämpft (WasapiSystem.cs, Kommentar mit Verweis auf das Windows SDK).
- Meldet WASAPI einen Puffer als still, gehen echte Nullen weiter.
- Der Eingangswandler mittelt alle Kanäle, rechnet bei einer anderen Rate mit dem speexdsp-Resampler (Qualität 5) auf
  48 kHz um und sammelt Rahmen zu 480 Werten (Eingangswandler.cs). Beispiel aus der Praxis: Am Rechner von Sarevoc lief die
  Ausgabe mit 88,2 kHz und vier Kanälen, das Mikrofon mit 48 kHz und zwei Kanälen (docs\MODULBERICHT-aec3.md, Abschnitt 6).
- Mikrofon und Wiedergabe werden je für sich geöffnet. Scheitert das Mikrofon, etwa am Windows-Datenschutz, läuft die
  Wiedergabe trotzdem (Anf. A7). Der Steuer-Thread versucht fehlende Geräte alle 2 s erneut, nach einem Fehler alle 10 s, und
  sofort nach einem Gerätewechsel (KlangMaschine.cs, KlangMaschine.Geraete.cs).

### E3.3 Echounterdrückung: AEC3 mit Loopback-Referenz

- Die Echoreferenz ist ein WASAPI-Loopback **des Wiedergabegeräts, auf dem JG TacRelay spielt**. Sie läuft nur, wenn
  die Echounterdrückung an ist (KlangMaschine.Geraete.cs, EchoAnpassen). Läuft Star Citizen auf demselben Gerät, steckt
  sein Ton mit in der Referenz. Genau auf diesen Fall ist die AEC3-Konfiguration abgestimmt (docs\MODULBERICHT-aec3.md).
- Die Echounterdrückung bezieht sich auf **ein** Ausgabegerät (erster Entwurf, `docs\entwurf\ENTSCHEIDUNGEN.md`, Klang). In 0.5.0 (wie in 0.4.0) hat der Client ohnehin nur
  einen Mischer und ein Ausgabegerät; einzelne Personen auf eigene Geräte (Anf. A9, zweiter Teil) ist noch offen (PROJEKT.md).
- Reihenfolge: Vor jedem Mikrofonrahmen gibt die Sendekette genau einen Referenzrahmen an das APM, im selben Thread
  (Sendekette.cs). Gelesen wird der älteste Rahmen. Warten mehr als 8 Rahmen, wird auf 2 gekürzt, damit die Referenz
  nicht nachhängt; liegt keiner bereit, geht Stille als Referenz ins APM, damit beide Seiten im Gleichschritt bleiben.
  Gemessen am Gerät von Sarevoc über 3 s: 284-mal lag ein Rahmen bereit, 15-mal zwei, 2-mal keiner (docs\MODULBERICHT-aec3.md,
  Abschnitt 6).
- Startwert der Verzögerung für AEC3 ist 20 ms; die tatsächliche Laufzeit schätzt AEC3 selbst (KlangMaschinenOptionen.cs).
- Die AEC3-Konfiguration ist die Upstream-Vorgabe von WebRTC mit einer Abweichung:
  `suppressor.normal_tuning.max_inc_factor=4` und `suppressor.nearend_tuning.max_inc_factor=8` (Upstream je 2), dazu
  prozessweit der Upstream-Feldversuch `WebRTC-Aec3TransparentModeHmm` (docs\MODULBERICHT-aec3.md, Kurzfassung).
  Grund: Mit der Upstream-Vorgabe schnitt AEC3 bei laufendem Spielton die Anlaute der eigenen Stimme an und dämpfte
  die Stimme in den ersten Sekunden stark.
- Ohne Echounterdrückung bleibt der Hochpass im APM; nur AEC3 und die Referenz fallen weg (KlangMaschine.Geraete.cs,
  ApmFuer). Eine AGC2 läuft im APM in keinem Fall mehr: Sie ist seit Klangstufe A aus, die Lautstärke regelt dahinter die
  eigene Pegelstufe (KlangMaschinenOptionen.cs, Sendekette.cs). Bis 0.6.0 stand hier "Hochpass und AGC2".
- Numerischer Schutz: Ein einziger NaN-Wert hätte AEC3 dauerhaft lahmgelegt. Nicht endliche Werte werden vor dem Aufruf
  zu 0 (EchoProzessor.cs; docs\MODULBERICHT-klang.md).

### E3.4 Rauschunterdrückung und Spracherkennung: RNNoise

- RNNoise bekommt 480 Samples je Aufruf und liefert das entrauschte Signal und eine Sprachwahrscheinlichkeit von 0 bis 1.
- Das entrauschte Signal ist 20 ms verzögert, die Wahrscheinlichkeit gilt aber für den gerade gelesenen Rahmen. Geht das
  entrauschte Signal ins Gate, verzögert die Sendekette die Wahrscheinlichkeit deshalb um zwei Rahmen, damit beides
  zusammenpasst (Sendekette.cs).
- Schaltest du die Rauschunterdrückung aus, geht das Signal hinter dem APM unverändert weiter, RNNoise läuft aber weiter
  als Spracherkennung. Die Schwelle bleibt also in beiden Fällen dieselbe (Sendekette.cs, Verarbeiten).
- Eine zweite RNNoise-Instanz läuft auf der Echoreferenz und sagt dem Echoschutz, ob die Wiedergabe sprachartig ist
  (KlangMaschine.cs). Kosten: etwa 0,1 ms je 10-ms-Rahmen (docs\MODULBERICHT-aec3.md, Abschnitt 6).
- Abweichung vom Entwurf: Der erste Entwurf sah zusätzlich Silero VAD und Stärkestufen der Rauschunterdrückung vor. Silero
  ist aufgeschoben (ONNX Runtime, etwa 150 MB, nur bei Bedarf; PROJEKT.md, Eintrag 29.09.2026), und die
  Rauschunterdrückung kennt in 0.5.0 (wie in 0.4.0) nur an und aus.

#### Klangfarbe (seit 0.9.3)

- **Aufbau.** Drei Glocken (Peaking EQ) nach dem Audio-EQ-Kochbuch von Robert Bristow-Johnson, hintereinander, Zustand in double:
  Wärme um 160 Hz (Q 0,8), Klarheit um 3000 Hz (Q 0,8), Schärfe um 6000 Hz (Q 1,0), je -6 bis +6 dB in ganzen dB (Klangfarbe.cs;
  die Filterformeln teilt sie mit dem Funk-Klick in Biquad.cs). Dahinter ein Begrenzer mit den Werten des Sicherheitsbegrenzers der
  Pegelstufe (Knie -1 dBFS, Decke -0,1 dBFS), weil eine Anhebung Spitzen über 0 dBFS heben kann.
- **Lautheitsneutral.** Ein fester Ausgleich g = -10 log10(Summe w_b |H(f_b)|^2) über die Terzbänder 160 bis 8000 Hz; w_b sind die
  Leistungsanteile der Norm-Sprachkurve aus ANSI S3.5-1997 (Standard-Sprachspektrum, normale Sprechweise, Spektrumpegel plus
  Bandbreite). Damit ändert sich die Leistung einer Normstimme bei keiner Einstellung. Beispiele für g: Wärme +6 -2,23 dB,
  Klarheit +6 -0,68 dB, Schärfe +6 -0,16 dB, alle drei +6 -2,89 dB.
- **Platz in der Kette.** Hinter Rauschfilter und Stimmprobe, vor Hörprobe und Gate (Sendekette.cs). Den Pegel des Rahmens misst
  die Kette vor der Klangfarbe und gibt ihn dem Gate mit (SprachGate.cs, `pegelDbfs`). So sehen Gate, Führung,
  Mindest-Funklautstärke, Korridor, Pegelanzeige, Telemetrie und Wächter genau dieselben Werte wie ohne Klangfarbe; gesendet
  werden aber die gefärbten Rahmen, und auch die Hörprobe und das Mithören hören sie. Davor, vor dem Rauschfilter, hätte die
  Klangfarbe die Sprachwahrscheinlichkeit von RNNoise verändert und damit Gate, Lernen und Wächter.
- **Neutral und Umstellen.** Stehen alle drei auf 0, ist die Stufe überbrückt, auch der Begrenzer: Der Rahmen bleibt Bit für Bit,
  wie er war. Eine neue Einstellung blendet über 20 ms (Halbkosinus) vom alten auf den neuen Satz über; der neue übernimmt dazu
  den Zustand des alten. So knackt es nicht, auch wenn jemand mitten im Spruch am Regler zieht. Ohne Allokation, ohne Sperre.
- **Gemessen** (KlangfarbeTests, Lauf vom 11.10.2026): An der Mitte jeder Glocke genau die Einstellung (auf 0,05 dB). Gegen einen
  Ton bei 1000 Hz liegen Wärme +6 bei 5,74 dB und Schärfe +6 bei 5,83 dB; Klarheit +6 liegt bei 4,90 dB, weil die Glocke bei
  3000 Hz mit Q 0,8 bis 1000 Hz hinunterreicht und dort selbst noch 1,10 dB hebt. Mit der Sprache des Klangkorpus (durch den
  Hochpass des APM wie in der Kette) ändert sich der Pegel mit einem Regler bis 3 dB um höchstens 0,24 dB, mit einem Regler ganz
  außen um höchstens 0,52 dB (Wärme +6), mit allen dreien auf +3 um 0,47 dB und auf +6 um 0,99 dB (Korpus vom 19.10.2026): Die Stimmen des Korpus haben an
  beiden Enden mehr Energie als die Norm-Sprachkurve. Mehr in docs\MODULBERICHT-klang.md, Abschnitt "Klangfarbe".

### E3.5 Echoschutz nach Geigel

AEC3 dämpft das Echo der Lautsprecher stark, aber ein Rest bleibt, und dieser Rest ist oft Sprache: die Stimmen der
anderen. Der Echoschutz verhindert, dass dieser Rest eine Sprechphase beginnt (Anf. A14). Er arbeitet nach dem Prinzip des
Geigel-Doppelsprech-Detektors auf Pegeln je 10-ms-Rahmen (Echoschutz.cs):

- **Erwartetes Echo** = höchster Wiedergabepegel der letzten 400 ms plus gelernte Kopplung Lautsprecher zu Mikrofon. Die
  400 ms decken Laufzeit, Nachhall, den Verzug der Kette und das Ausklingen in AEC3 ab.
- **Verdacht**, wenn der Mikrofonpegel **vor** dem APM höchstens einen Sicherheitsabstand über dem erwarteten Echo liegt.
  Ein Rahmen unter Verdacht darf keine Phase beginnen; eine offene Phase bleibt davon unberührt.
- **Sicherheitsabstand nach Art der Wiedergabe:** 10 dB, wenn in den letzten 400 ms ein Wiedergaberahmen sprachartig war,
  sonst 6 dB (Spielton, Musik). Sprachartig heißt: RNNoise auf der Referenz liefert mindestens 0,9, oder der eigene
  Mischer hat hörbar gespielt (mindestens -60 dBFS). Damit gelten die Stimmen der anderen immer als Sprache, auch neben
  Musik.
- **Kopplung:** startet vorsichtig bei 0 dB und lernt mit 0,05 dB je Rahmen, also 5 dB je Sekunde, in Richtung von
  Mikrofon minus Wiedergabe, begrenzt auf -60 bis +10 dB. Gelernt wird nur bei geschlossenem Gate und ohne Sprechtaste.
- Unter -60 dBFS Wiedergabe ist der Schutz aus.

Folgen für die Bedienung: Wer spricht, während die anderen reden, muss lauter sein als deren Echo; das ist halbduplex nur
beim Öffnen. In den ersten Sekunden nach dem Start, solange die Kopplung noch lernt, ist der Schutz strenger
(docs\MODULBERICHT-aec3.md, Abschnitt 10).

Warum 6 dB bei Geräusch: Die Gegenprüfung des Moduls aec3 maß, dass ohne Schutz bei Geräusch das Echo von Spielton und
vor allem Musik das Gate öffnet, und wählte den kleinsten Abstand ohne Auslösung (je 105 Szenen zu 40 s, Nutzer schweigt):

| Abstand bei Geräusch | Auslösungen Spielton | Auslösungen Musik | verpasste Anlaute Lautsprecher 12/20/30 dB (je 65) |
| --- | --- | --- | --- |
| kein Schutz bei Geräusch | 12 | 124 | 7/11/19 |
| 3 dB | 0 | 42 | 13/13/16 |
| 5 dB | 0 | 3 | 19/13/17 |
| **6 dB, gewählt** | **0** | **0** | **21/16/17** |
| 10 dB (wie bei Sprache) | 0 | 0 | 31/18/17 |

Quelle: docs\MODULBERICHT-aec3.md, Abschnitt 11.1; dieselben Zahlen im Kommentar von `GeraeuschSicherheitDb`
(Echoschutz.cs). Die Wahl zwischen 5 und 6 dB nennt der Bericht ausdrücklich eine Abwägung, die Sarevoc trifft.

### E3.6 Das Sprach-Gate

Das Gate ist eine Zustandsmaschine mit vier Zuständen, je 10-ms-Rahmen im Aufnahme-Thread (SprachGate.cs):

| Zustand | Bedeutung | Weiter, wenn |
| --- | --- | --- |
| LEISE | nichts wird gesendet; der Rauschboden wird nachgeführt | Pegel über der Linie **und** Wahrscheinlichkeit mindestens Schwelle: ANLAUF |
| ANLAUF | Prüfung läuft, noch nichts gesendet | 4 erfüllende Rahmen (der erste zählt mit): SENDEN. 10 Rahmen ohne das: zurück nach LEISE, Anlauf "abgewiesen" |
| SENDEN | Rahmen gehen hinaus | Bedingung zum Bleiben nicht mehr erfüllt: NACHLAUF |
| NACHLAUF | 300 ms weiter senden | Bedingung wieder erfüllt: SENDEN, ohne neuen Vorlauf. 300 ms um: LEISE, letzter Rahmen ausgeblendet |

Einzelheiten:

- **Pegel:** mittlerer quadratischer Wert des Rahmens in dBFS. Ein voll ausgesteuertes Rechteck hat 0 dBFS, ein voll
  ausgesteuerter Sinus -3,01 dBFS (Pegel.cs). Gemessen wird das Signal, das gesendet würde, also hinter APM und, falls an,
  hinter der Rauschunterdrückung.
- **Rauschboden:** Minimum-Statistik nach Martin, vereinfacht auf den Gesamtpegel: leicht geglättete Rahmenleistung
  (Faktor 0,3), Minimum über 8 Teilfenster zu je 250 ms, also über 2 s, mit fester Korrektur von +1 dB, weil das Minimum
  den Mittelwert des Rauschens unterschätzt. Nachgeführt wird nur außerhalb von SENDEN (GateStatistik.cs, SprachGate.cs).
- **Hysterese:** Um offen zu bleiben, reichen 3 dB weniger Abstand und eine um 0,15 kleinere Wahrscheinlichkeit
  (`AbstandHystereseDb`, `SchwelleHysterese`).
- **Bestätigung:** 4 von höchstens 10 Rahmen. Abgewiesene Anläufe gehen als Nicht-Sprache in die Statistik der
  Selbstjustierung (siehe unten).
- **Vorlauf:** Ein Ring hält die letzten 300 ms. Beim Öffnen gehen alle Rahmen ab 30 ms vor dem ersten Anlaufrahmen bis
  zum aktuellen als Bündel hinaus, gekennzeichnet als PREROLL. Der Vorlauf greift nie hinter den zuletzt gesendeten Rahmen
  zurück; sonst würden bei schnellem Wiederanlauf bis zu 30 ms doppelt gesendet (docs\MODULBERICHT-klang.md,
  Gegenprüfung).
- **Blenden:** Der erste Rahmen einer Phase wird über 2,5 ms (120 Samples) eingeblendet, der letzte über 10 ms
  ausgeblendet.
- **Selbstjustierung der Sprachschwelle:** Aus der Nicht-Sprache der letzten 60 s (Pausen und abgewiesene Anläufe) bildet
  das Gate das 95-%-Quantil der Wahrscheinlichkeit in hörbarer Nicht-Sprache (mindestens 3 dB über dem Boden), schlägt
  0,15 auf und begrenzt auf 0,4 bis 0,8. Bis 100 solche Rahmen gesammelt sind, gilt 0,5. Sind die Geräusche eines Raums
  sprachähnlich, steigt die Anforderung also von selbst.
- **Sprechtaste:** umgeht das Gate. Gesendet wird ab dem Rahmen des Drückens, ohne Vorlauf, bis zum Ende des Nachlaufs nach dem
  Loslassen (seit 0.9.3, E2.7; die Kette gibt dem Gate die Taste so lange als gedrückt); eine laufende VA-Phase endet beim
  Drücken mit ENDE, und die Tastenphase beginnt neu (SprachGate.cs, TasteVerarbeiten; Sendekette.cs, TasteLesen;
  Sendeaufbereitung.cs).
- **Sprachaktivierung abgeschaltet** (Netz auf Taste): Eine offene VA-Phase endet beim nächsten Rahmen mit einem
  ausgeblendeten letzten Rahmen, damit sofort ENDE hinausgeht.
- **Robustheit:** Ein Rahmen mit NaN oder Unendlich zählt nie als Sprache und geht in keine Statistik; vorher konnte er den
  Rauschboden dauerhaft vergiften (docs\MODULBERICHT-klang.md, Gegenprüfung).

### E3.7 Die Führung: Fest, Automatik, Lernend

Die drei Stellungen aus E2.4 setzt die Gate-Führung um (GateFuehrung.cs, GateVorgaben.cs). Sie lässt das Gate selbst
unverändert und stellt vor jedem Rahmen nur zwei Größen ein: die **Linie** und die **Wahrscheinlichkeit**, die ins Gate
geht.

**Die Linie** (`N` ist der Rauschboden des Gates in dBFS, `H` der gelernte Hintergrund):

```
Fest:        Linie = Schwelle
Automatik:   Linie = max(N, -69 dBFS) + Abstand
Lernend:     Linie = Begrenzen(max(N, H) + Abstand, Korridor von, Korridor bis)
Alle:        Linie auf -100 bis 0 dBFS begrenzt
```

- Die Untergrenze -69 dBFS bei Automatik ergibt mit dem voreingestellten Abstand 9 dB eine Linie von mindestens -60 dBFS.
  Grund laut Code: Hinter AEC3 und RNNoise liegt der Boden in einem ruhigen Raum fast bei digitaler Stille, und Reste der
  Verarbeitung, die bei hoher AGC-Verstärkung bis gegen -70 dBFS reichen, wirkten am Echo-Prüfstand für RNNoise
  sprachähnlich (Wahrscheinlichkeit um 0,6). Weil die Untergrenze auf den Boden wirkt und nicht auf die Linie, verschiebt
  der Abstand die Linie auch dort (GateVorgaben.cs). Der Kommentar dort nennt Sprache hinter der Kette "um -35 bis -15
  dBFS"; als Messung ist das nicht ausgewiesen.
- Die Führung setzt den Empfindlichkeitsregler des Gates mit einem Stellbereich von 120 dB je Stufe so, dass Boden plus
  wirksamer Abstand genau die gewünschte Linie ergeben. Das Gate rechnet Boden und Abstand vor dem Nachführen des Bodens,
  deshalb stimmt die Linie auf den Rahmen genau (GateFuehrung.cs).

**Die Wahrscheinlichkeit:** Liegt die RNNoise-Wahrscheinlichkeit unter "Sprache ab", geht 0 ins Gate (bei offenem Gate
gilt "Sprache ab" minus 0,15). Sonst:

- Fest: Es geht 1 ins Gate. Damit redet die Selbstjustierung der Sprachschwelle nicht mit; "Sprache ab" ist die einzige
  Anforderung. "Sprache ab" 0 heißt hier: nur der Pegel zählt.
- Automatik und Lernend: Es geht die Wahrscheinlichkeit selbst ins Gate. Wirksam ist also der größere Wert aus "Sprache
  ab" und der Selbstjustierung (0,4 bis 0,8). "Sprache ab" 0 heißt hier nicht "nur Pegel"; die Selbstjustierung bleibt
  Untergrenze (GateModusTests: Sprachsicherheit_FiltertUnsichereRahmen).

Unter Echoverdacht geht bei geschlossenem Gate immer 0 ins Gate.

**Wie Lernen lernt** (GateFuehrung.cs, Lernen):

- Der Hintergrund `H` folgt dem Pegel als asymmetrischer Quantil-Folger: je Rahmen höchstens `10 dB / (Anstieg x 100)`
  nach oben und `10 dB / (Rückkehr x 100)` nach unten. Mit den Voreinstellungen sind das 0,05 dB und 0,02 dB je Rahmen,
  also 10 dB in 2 s nach oben und 10 dB in 5 s nach unten.
- Dadurch folgt `H` nicht dem Mittel, sondern einem oberen Quantil des Hintergrunds. Im Gleichgewicht gilt
  Anteil darüber x Anstiegsschritt = Anteil darunter x Rückkehrschritt. Für 2 s und 10 s ergibt das das 83-%-Quantil (so im
  Code-Kommentar), für 1 s und 4 s das 80-%-Quantil (so im Test), für die Voreinstellung des Clients mit 2 s und 5 s
  gerechnet das 71-%-Quantil.
- Gelernt wird aus jedem Rahmen, der **nicht** gesendet wird, und aus gesendeten Rahmen der Sprachaktivierung, die
  mindestens 6 dB unter der Korridor-Obergrenze liegen. Dort liegt nach Absicht des Nutzers nicht mehr die eigene Stimme.
  Bei gedrückter Sprechtaste lernt nichts.
- `H` wird in allen Stellungen mitgeführt, wirkt aber nur bei Lernend.

**Zwei Sätze Startwerte:** Die Klangmaschine hat eigene Startwerte (`GateVorgaben.Standard`: Automatik, Schwelle
-45 dBFS, Abstand 9 dB, Korridor -60 bis -30 dBFS, Anstieg 2 s, Rückkehr 10 s, Sprache ab 0,5), mit denen auch die Tests
laufen. Der Client überschreibt sie beim Start mit seinen gespeicherten Werten (Tabelle in E2.8: Korridor -55 bis
-30 dBFS, Rückkehr 5 s). Die Klangmaschine begrenzt jeden Wert zusätzlich: Pegel -100 bis 0 dBFS, Abstand 0 bis 40 dB,
Zeiten 0,1 bis 600 s, NaN nimmt die Voreinstellung, ein vertauschter Korridor wird umgedreht (GateVorgaben.cs,
Bereinigen).

### E3.8 Sendeaufbereitung und Opus

- Die Sendeaufbereitung holt die Rahmen aus dem Gate, fasst je zwei zu 20 ms zusammen und kodiert sie
  (Sendeaufbereitung.cs). Endet eine Phase nach einer ungeraden Zahl von Rahmen, wird der letzte 20-ms-Rahmen mit Stille
  aufgefüllt. Beginnt eine neue Phase, während eine offen ist (Taste schlägt Sprachaktivierung), wird die alte vorher mit
  ENDE geschlossen.
- Kennzeichen je Paket, bitgleich mit Protokoll 5.6 (Sprachkennzeichen.cs):

| Kennzeichen | Wert | Bedeutung |
| --- | --- | --- |
| BEGINN | 0x01 | erstes Paket einer Sprechphase |
| ENDE | 0x02 | Ende-Markierung, ohne Opus |
| PREROLL | 0x04 | Paket aus dem Vorlauf; zählt beim Empfänger nicht in die Laufzeitstatistik |
| VA | 0x08 | Phase aus der Sprachaktivierung; ohne dieses Kennzeichen kam sie von der Sprechtaste |

- Ein Paket ist höchstens 400 Byte lang (Protokoll 5.6). Die Länge geht als Obergrenze an libopus; braucht ein
  Rahmen mehr, senkt libopus die Bitrate dieses einen Rahmens (OpusKodierer.cs). Scheitert eine Kodierung, fällt nur
  dieses Paket aus und wird gezählt.
- Opus-Einstellungen (OpusKodierer.cs), am Kodierer zurückgelesen (docs\MODULBERICHT-klang.md):

| Einstellung | Wert |
| --- | --- |
| Abtastrate, Kanäle | 48 kHz, mono |
| Rahmen | 20 ms (960 Samples) |
| Anwendung | VOIP (AUDIO möglich; welcher besser klingt, soll der Hörtest entscheiden) |
| Bitrate | 48 kbit/s, VBR beschränkt |
| Komplexität | 10 |
| Bandbreite | höchstens Fullband |
| In-band-FEC | an; Verlusthinweis startet bei 0 und kommt aus den Empfängerberichten |
| DTX | aus |
| Signal | VOICE |
| Dekodierer | Komplexität 7 mit Bandbreitenerweiterung |

- Gemessen: Bei 48 kbit/s ist jedes Paket Hybrid, Fullband, 20 ms (TOC 0x78); die tatsächliche Bitrate liegt bei
  synthetischer Sprache bei 41 bis 44 kbit/s (docs\MODULBERICHT-klang.md). 2 s synthetische Sprache ergaben 100 Pakete
  mit 55 bis 153 Byte, Median 126 Byte (Testausgabe KodiererZielTests).
- Die Bitrate ist in 0.5.0 (wie in 0.4.0) fest. `OpusKodierer.SetzeBitrate` gibt es, aber keinen Aufrufer und keine Einstellung dafür;
  Der erste Entwurf nannte 32 bis 64 kbit/s als einstellbar.
- Die fertigen Pakete gehen in einen Sende-Ring mit 128 Plätzen; der Melde-Thread gibt sie an den Transport. Frame-Nummer
  und Sprechphase vergibt der Transport, ENDE geht dreimal im Abstand von 20 ms (KlangMaschine.cs, Sprechkanal.cs).

### E3.9 Empfangsseite

Jeder Sprecher (je Konto) bekommt einen eigenen Jitterpuffer mit eigenem Opus-Dekodierer als Quelle im Mischer. Ein
Sprecher ohne Paket seit 60 s wird entfernt; seinen Dekodierer gibt die Empfangsseite erst frei, wenn der Mischer ihn
sicher nicht mehr liest (Empfangsseite.cs). Funknetze liegen auf den Mischer-Netzen 0 bis 62, Netz 63 gehört der
Hörprobe und dem Mithören (IKlangMaschine.cs).

#### Jitterpuffer

Nach dem Prinzip von WebRTC NetEQ (Jitterpuffer.cs):

| Größe | Wert |
| --- | --- |
| Zielverzögerung | 20 ms plus 95-%-Quantil der relativen Verzögerung |
| Startwert, solange es keine Statistik gibt | 80 ms |
| Untergrenze | 20 ms, mit erwarteter FEC 40 ms (das Folgepaket muss beim Abspielen da sein) |
| Obergrenze | 200 ms |
| Gedächtnis | Vergessensfaktor 0,983 je 500 ms |
| Übernahme des Ziels | am Anfang jeder Sprechphase; während der Phase nur, wenn es um mehr als 20 ms steigt |
| Verlustkette je 20 ms | Paket da: dekodieren. Fehlt es: FEC aus dem Folgepaket. Sonst: Verdeckung (Deep PLC) |
| Verdeckung am Stück | höchstens 60 ms, die letzten 20 ms davon ausgeblendet, danach Stille bis zum nächsten echten Rahmen |
| Übergänge | jeder hörbare Abschnitt setzt an der Verstärkung an, mit der der vorige endete; Einblenden 2,5 ms, Ende einer Phase über 5 ms ausgeblendet |
| Kürzen und Dehnen | liegt die Puffertiefe mehr als 15 ms über dem Ziel, wird um je eine Grundperiode gekürzt, bis sie höchstens 5 ms darüber liegt; leise Rahmen (unter -45 dBFS) bis zur Hälfte; liegt sie mehr als 15 ms darunter, wird gedehnt |
| Phase ohne ENDE | endet 300 ms nach dem Leerlaufen |
| Neue Phase binnen 100 ms nach ENDE | kein Ende- und kein Beginn-Klick |
| Sprung der Frame-Nummer über 3000 (60 s) | neue Zeitlinie |

Geregelt wird die Puffertiefe, nicht der bloße Füllstand: Abholzeitpunkt minus erwartete Ankunft des nächsten Frames bei
schnellster Laufzeit der letzten 2 s, plus 20 ms für den Frame selbst. Das Vorlauf-Bündel eines neuen Spruchs baut der
Puffer durch Kürzen ab; gemessen ist ein Bündel von 140 ms binnen 1 s auf höchstens Ziel plus 20 ms abgebaut, ohne
verlorenen Frame (docs\MODULBERICHT-klang.md).

#### Mischer

Ein Mischer je Ausgabegerät, float32, 48 kHz, Blöcke zu 10 ms (Mischer.cs):

- je Quelle: Pegel je Funker in dB plus Netzlautstärke in dB plus Absenkung bei Durchsage; jede Einstellung höchstens
  +24 dB; NaN zählt als 0 dB, minus unendlich bleibt stumm
- Panorama je Netz nach konstanter Leistung, in der Mitte je Kanal -3 dB; Pegelwechsel werden über den Block gerampt
- Durchsage: Sprüche der Art DURCHSAGE und GLOBAL senken die anderen Netze ab (KlangBruecke.cs), um 12 dB (im Mischer
  0 bis 30 dB möglich), volle Absenkung nach 50 ms, gehalten 300 ms nach dem Ende, aufgehoben über 250 ms; Funker- plus
  Netzpegel einer Durchsage nie unter -20 dB
- Trifft ein Paket mehrere eigene Netze, gilt nach Regel C7 des Funkprotokolls (Protokoll 6.11) das lauteste, bei Gleichstand das erste; dessen Lautstärke
  und Panorama wirken (KlangBruecke.cs)
- höchstens 64 Quellen
- danach der Begrenzer, Stereo gekoppelt, nach dem Muster aus WebRTC AGC2: Knie bei -1 dBFS Spitze, 2 dB breit,
  Verhältnis 5, Decke -0,1 dBFS (Begrenzer.cs)
- zuletzt weiches Clipping je Kanal, eine Übertragung von `opus_pcm_soft_clip` aus libopus 1.6.1 (WeichesClipping.cs;
  THIRD-PARTY-NOTICES Nr. 12). Hart geclippt wird nie; die Ausgabe liegt immer zwischen -1 und 1.

#### Klick

Der Funk-Klick wird zur Laufzeit erzeugt und nur lokal gemischt, nie durch den Codec (FunkKlick.cs); es gibt keine Tondateien.
Alle Klänge entstehen im Konstruktor, nicht im Audio-Thread: Jede neue Einstellung ist eine neue Instanz im Mischer, und was
der alte Klick gerade spielte, bricht ab. Die Mischfunktion arbeitet ohne Speicheranforderung (Test
`Mischen_MitAnhoerenUndKlicks_AllokiertNicht`). Die Zahlen der Klangform sind laut Code Entwurf und per Hörtest festzulegen.

Seit 0.5.0 (Anf. A63) gibt es drei Klänge, die der Nutzer wählt (E2.6):

| Klang | Drücken | Loslassen |
| --- | --- | --- |
| Klick (ab Werk, wie bis 0.4.1) | 10 ms, Töne 1200 und 2400 Hz | 20 ms, Töne 900 und 1800 Hz |
| Doppelklick | zwei Anschläge dieser Form im Abstand von 28 ms | zwei Anschläge dieser Form im Abstand von 28 ms |
| Klick, Piep beim Loslassen | wie der Klick | ein Ton von 1250 Hz, 80 ms, Flanken je 6 ms |

Die Hüllkurve des Klicks: Anstieg 1 ms (halbe Hann-Flanke), Abfall mit Zeitkonstante 4 ms, Auslauf 2 ms; Wert und Steigung
sind am Anfang und am Ende null, damit es nicht knackt (Test `AnfangUndEnde_MitWertUndSteigungNull`, `Klangwahl_...`).

**Lautstärke.** Sie ist ein Pegel über 200 ms (`FunkKlick.PegelUeber200Ms`), bezogen auf die Sprache hinter der Kette: Sie
liegt bei -25 dBFS, der Mitte von -35 bis -15 dBFS (`GateVorgaben`). 0 dB heißt also so laut wie eine Stimme, -12 dB zwölf
dB leiser. Über 200 ms summiert das Gehör kurze Laute ungefähr (Faustregel im Code, nicht am Ohr gemessen); alle drei
Klänge sind deshalb auf denselben Pegel über 200 ms gebracht (im Test auf 0,01 dB genau) und klingen bei derselben Zahl etwa
gleich laut, obwohl sie verschieden lang sind. Einstellbar sind ganze dB von -30 bis -6; bei -6 dB bleibt die Spitze aller
drei Klänge unter -6 dBFS (Test `Klangwahl_DreiFormen_GleichLautOhneKnacken`).

**Voreinstellung und Messung** (Testausgabe KlickTests, `Voreinstellung_HoerbarAberDezent_StattDesLeisenKlicksBis041`; Läufe
vom 01. und 02.10.2026, Stand 0.5.0):

| Klick | Pegel über 200 ms |
| --- | --- |
| bis 0.4.1 (Spitze -24 dBFS bei 10 und 20 ms; im Test aus dem heutigen Klang nachgebildet) | -47,6 dBFS, gut 22 dB unter der Sprache |
| eigene Taste ab Werk, -12 dB | -37,0 dBFS, rund 10,6 dB lauter als bis 0.4.1 |
| andere Sprecher ab Werk, -15 dB | -40,0 dBFS |
| andere Sprecher bei Sprachaktivierung (dezent, weitere 18 dB leiser) | -58,0 dBFS |

Die Spitze des eigenen Klicks liegt mono, mittig in voller Höhe, bei -13,4 dBFS (Sprache im Mittel -25 dBFS). Über die ganze
Maschine im Stereo-Mittelpunkt, je Kanal 3 dB weniger, misst der Test eine Spitze von -16,4 dBFS (KlangMaschineTests,
`Sprechtaste_LokalerKlick_UndOhneSprachaktivierungNurTaste`); bis 0.4.1 waren es -27,0 dBFS. Der Test verlangt, dass die Spitze
unter -10 dBFS bleibt, also unter den Spitzen der Sprache. Der Grund für die lautere Voreinstellung: Der Klick bis 0.4.1 ging
neben Spielton unter (Code-Kommentar `FunkKlickEinstellungen`). Ob die Voreinstellung und die drei Klänge nach Gehör passen, ist
nicht geprüft: Die Werte sind gerechnet und gemessen, nicht am Ohr (E3.15).

- **Eigene Taste:** Klick beim Drücken und Loslassen der Sprech- oder Melde-Taste, mittig; er wird angefordert und im
  Audio-Thread gemischt. Den Klick beim Drücken fordert der Thread der Taste an. Den beim Loslassen fordert seit 0.9.3 die Sendekette
  im Aufnahme-Thread an, wenn sie nach dem Nachlauf aufhört zu senden (KlangMaschine.cs, `TasteBeendetKlicken`); ohne Mikrofon oder
  wenn der Strom abbricht, fordert ihn die Maschine gleich an. Drückt man im Nachlauf die andere Taste, klickt es beim Loslassen und
  beim Drücken zugleich (E2.7). Hält die Sprechtaste das Mikrofon schon offen, klickt die Melde-Taste nicht noch
  einmal, denn ein Klick heißt Mikrofon auf oder zu. Alle Sprechwege sind bis zur Wiedergabe der Maschine geprüft: Tastatur,
  Maus, Melde-Taste an Tastatur und Joystick, zwei Sprechknöpfe am Joystick über die echte DirectInput-Quelle mit einer
  Geräteattrappe, und bis 0.9.3 der Knopf "Sprechtaste halten" im Bereich Audio (Standzeile 02.10.2026 00:10; den Knopf gibt es
  seit 0.10.0 nicht mehr, `KlickSprechwegeTests` prüft sechs Wege).
- **Empfang:** Beginn-Klick mit dem Spruch eines anderen, Ende-Klick 100 ms nach dessen ENDE, damit ein unmittelbar
  folgender Spruch ohne Klickpaar anschließt (Jitterpuffer.cs, `FortsetzungMs`). Kam der Spruch aus der Sprachaktivierung, ist
  der Klick um `SprachaktivierungDb` leiser (ab Werk 18 dB) oder fehlt ganz (Schalter "Bei Sprachaktivierung dezent" aus).
- **Anhören:** Spielt erst den eigenen Klick (Drücken, nach 300 ms Loslassen), dann den eines anderen Sprechers (Drücken ab 800 ms,
  Loslassen ab 1100 ms), jeweils nur, wenn er an ist, in seiner Lautstärke. Ist nur einer an, beginnt er sofort; sind beide aus,
  gibt es nichts zu hören (Test `Anhoeren_EigeneDannAndere_InIhrerLautstaerke`). Es geht nur bei laufender Wiedergabe.
- **Mithören:** Das Mithören beim Senden (E3.11) spielt die eigenen Sprüche mit dem Klick wie beim Empfänger.

#### Ausgabe

WASAPI geteilt, ereignisgesteuert, Puffer 30 ms, Kategorie GameChat, Ducking-Abwahl vor dem Start, MMCSS "Pro Audio"
(WasapiSystem.cs, KlangMaschinenOptionen.cs). Läuft das Gerät mit 48 kHz und mindestens zwei Kanälen, schreibt der
Mischer direkt hinein, weitere Kanäle bekommen 0. Sonst rechnet speexdsp (Qualität 5) auf die Geräterate um; ein
Monogerät bekommt (L + R) mal 0,707 (Ausgabewandler.cs).

### E3.10 Sprechtaste: Raw Input und DirectInput

Alle Quellen hängen hinter einem Verbund: Die Sprechtaste gilt als gedrückt, solange sie an irgendeiner Quelle gedrückt
ist. Neue Zuordnungen und das Freigeben lassen eine gehaltene Taste los (SprechtastenVerbund.cs). Seit 0.5.0 kennt der
Verbund alle Funktionen einer Taste (`Tastenfunktion`, Tastenbelegung.cs): Sprechen und Melden werden gehalten, gelten also
als gedrückt, solange irgendeine ihrer Belegungen an irgendeiner Quelle unten ist; die übrigen, nämlich Sendenetz vor und zurück,
Funkfeld ablegen oder holen sowie Phase vor und zurück, melden nur die Flanke nach unten. Raw Input und, neu in 0.5.0, auch
DirectInput bedienen alle Funktionen (`ITastenQuelle`); eine einfache Quelle bekäme im Verbund nur ihre erste Sprechtaste. Eine
zweite Raw-Input-Anmeldung kam nicht dazu: Alle Funktionen laufen über dieselbe Instanz. Drücken und Loslassen
gehen unter einer Sperre an die Klangmaschine, damit zwei Threads das Mikrofon nicht offen stehen lassen können; der
Audio-Thread nimmt diese Sperre nie (KlangMaschine.cs).

**Tastatur und Maus über Raw Input** (RawInputSprechtaste.cs, RawInputAuswertung.cs):

- `RIDEV_INPUTSINK` meldet Tasten auch, wenn ein anderes Programm vorn ist. Kein Hook, kein Abfangen; das Programm sieht
  nur mit.
- Eigener Thread mit einem unsichtbaren Nachrichtenfenster (`HWND_MESSAGE`): kein Fokus, keine Anzeige.
- Tastatur als Scancode mit Präfix (0xE0nn, 0xE1nn), unabhängig vom Layout, links und rechts getrennt (rechte Strg
  0xE01D, linke 0x001D). Maus: Knöpfe 1 bis 5. Die Maus wird nur angemeldet, solange eine Maustaste belegt ist oder gerade
  belegt wird, damit nicht jede Mausbewegung eine Nachricht erzeugt.
- Grenze von Raw Input: Je Prozess und Gerätekategorie gilt nur die letzte Anmeldung. Deshalb gibt es im Prozess genau eine
  Instanz, und DirectInput legt nie Tastatur oder Maus an (DirectInputSuche.cs).
- Hängeschutz: Hinter dem sicheren Desktop kommt kein WM_INPUT, auch kein Loslassen. Solange eine zugeordnete Taste unten
  ist, fragt ein Zeitgeber alle 250 ms den physischen Zustand ab (GetAsyncKeyState, nur lesend). Zweimal hintereinander
  oben gilt als losgelassen.

**Joystick und andere Spielgeräte über DirectInput 8** (DirectInputSprechtaste.cs, DirectInputGeraete.cs,
Joystickauswertung.cs, Joystickeingabe.cs):

- Nur Spielgeräte (`DI8DEVCLASS_GAMECTRL`), nicht-exklusiv und im Hintergrund geöffnet, nur lesend: kein Force Feedback,
  kein HidHide, keine Treiber. DirectInput selbst pflegt beim Aufzählen und Öffnen allerdings Werte unter HKCU
  (Kalibrier- und Instanzdaten je Gerät).
- Ein Lesethread wartet auf die Ereignisse der Geräte und liest gepuffert (128 Einträge je Gerät), damit auch ein sehr
  kurzer Druck zwischen zwei Abholungen nicht verloren geht. Geräte ohne Ereignis liest ein Takt von 4 ms.
- Ein eigener Suchthread zählt die Geräte auf; das kostet gemessen 140 bis 190 ms Rechenzeit bei sechs Geräten, und das
  Lesen läuft währenddessen weiter. Fehlt ein belegtes Gerät, wird zuerst nach 3 s, dann mit wachsendem Abstand bis 30 s
  neu gesucht; eine Windows-Meldung setzt den Abstand zurück.
- Seit 0.5.0 kann DirectInput alle Funktionen, nicht nur Sprechen: Melden wird wie das Sprechen gehalten (ODER über alle
  Belegungen), die übrigen Funktionen lösen mit der Flanke nach unten aus. Eine Funktion mit Flanke reagiert erst, wenn der Knopf
  frisch gedrückt ist und die letzte Flanke derselben Belegung mindestens 150 ms zurückliegt (`Joystickauswertung.EntprellMs`,
  Entprellen für prellende Knöpfe und Doppeltipp). Ziehst du einen Joystick ab, während Sprechen oder Melden gedrückt war,
  kommt das Loslassen (Joystickauswertung.cs).
- Knöpfe 1 bis 128, gezählt wie in der Windows-Spielgerätesteuerung und in Star Citizen; dazu bis zu vier Hüte mit je
  acht Richtungen. Eine Hauptrichtung gilt auch in den beiden Nachbarschrägen als gedrückt, eine Schräge nur genau in
  dieser Richtung.
- Wiederfinden: zuerst über den Gerätepfad, dann über VID/PID und Produktname, aber nur bei genau einem passenden Gerät;
  nie über die Instanz-GUID und nie ein virtuelles Gerät für ein echtes oder umgekehrt (ein Klon von Joystick Guardian
  teilt VID/PID und Namen mit dem Original).
- Frisch drücken: Was beim Belegen, beim Öffnen oder in den ersten 750 ms danach schon gedrückt ist, zählt erst nach
  einem Loslassen. Grund: VIRPIL-Geräte senden im Leerlauf nur etwa alle 450 ms einen Bericht.

Gemessene Zeiten: Mit der Geräteattrappe meldet der Lesethread einen Wechsel im Median nach 0,002 ms, p99 0,057 ms; ein
Gerät im 4-ms-Takt im Median nach 2,07 ms; ein Druck während einer Suche von 600 ms nach 0,079 ms (Testausgabe
JoystickTests). An den sechs echten Geräten von Sarevoc (Star Citizen aus) kostete ein Lesen 2,6 µs ohne Allokation; ein echter
Tastendruck ist noch nicht gemessen (docs\MODULBERICHT-client.md).

### E3.11 Hörprobe und Mithören

Beides spielt über denselben Jitterpuffer, der wie bei einem Empfänger dekodiert, und über das Mischer-Netz 63
(Sendekette.cs, Empfangsseite.cs). Die Oberfläche hält dafür eine einzige Instanz (HoerprobeViewModel.cs); seit 0.10.0 zeigt
Start die Hörprobe und Einstellungen > Senden > Erweitert das Mithören, bis 0.9.3 zeigten beides der Bereich Audio und der
Bereich Sprachaktivierung. Sie hat keinen eigenen Stand, sondern liest bei jedem Bildtakt den der Klangmaschine.

**Hörprobe als Aufnahme** (`IKlangMaschine.ProbeStarten`, `Probe`, `ProbeAbbrechen`, neu in 0.4.1):

- Die Kette nimmt das Signal hinter APM und RNNoise, vor dem Gate, und kodiert es mit einem zweiten Opus-Kodierer mit
  denselben Einstellungen in einen Speicher für `ProbeDauerMs` = 5000 ms, also 250 Pakete zu 20 ms (`ProbePakete`).
  Danach speist sie die Pakete im 20-ms-Takt in den Jitterpuffer der Hörprobe; die erste trägt BEGINN, am Ende folgt
  ENDE.
- Aufnahme und Wiedergabe laufen nie zugleich; das Mithören ruht so lange. Die Wiedergabe wird nicht wieder aufgenommen.
- Ab dem Start bis `ProbeNachlaufRahmen` nach dem Ende der Wiedergabe verwirft die Kette jede neue Phase der
  Sprachaktivierung ganz, von BEGINN bis ENDE. `ProbeNachlaufRahmen` ist `Echoschutz.HalteRahmen` (40 Rahmen) plus 10,
  also 50 Rahmen zu 10 ms = 500 ms: Jitterpuffer, Gerät, Laufzeit und Nachhall der letzten Silben. Eine Phase, die beim
  Start schon läuft, und die Sprechtaste bleiben unberührt; eine verworfene Phase bleibt bis zu ihrem ENDE verworfen,
  damit kein Rest ohne BEGINN hinausgeht. Das Gate rechnet währenddessen weiter, seine Statistik sieht keine
  verfälschten Rahmen.
- Befehle kommen aus jedem Thread und greifen ab dem nächsten Aufnahmerahmen; der Aufnahmepfad allokiert nicht.
- Die Oberfläche startet die Probe nur, wenn Mikrofon und Wiedergabe laufen; während einer Probe bricht derselbe Knopf
  ab.

Gemessen im Prüfstand mit Geräte-Attrappen, echter Kette (APM, RNNoise, Opus) und synthetischer Kopplung
(HoerprobeTests.cs, Ergebnisse in `PROJEKT.md`, Standzeile 01.10.2026 18:30):

| Aufbau | Ergebnis |
| --- | --- |
| Aufbau von Sarevoc nachgestellt: Lautsprecher mit 12 dB Kopplung und 40 ms Verzug, Echounterdrückung an; Sendenetz angenommen auf Taste | Wiedergabe folgt der Stimme zu 99,8 % bei 80 ms Versatz; während der Aufnahme still, nichts gesendet, danach Ruhe |
| Lautsprecher ohne Echounterdrückung, 3 dB Kopplung, Sendenetz auf VA mit niedriger fester Schwelle | genau eine Wiedergabe, nichts gesendet; ohne die Sperre der Sprachaktivierung gingen 86 Pakete hinaus |
| Abbrechen, Neustart während der Wiedergabe, Audio stoppen | abgebrochene Aufnahme wird nicht abgespielt, nach dem Abbrechen still, ein Neustart nimmt neu auf, Audio stoppen beendet die Probe |
| Allokation in den Audiopfaden mit Probe | 0 Byte (Aufnahme, Wiedergabe, Abbrechen) |

**Mithören beim Senden** (`IKlangMaschine.Hoerprobe`, bis 0.4.0 die ganze Hörprobe): Die Sendekette reiht jedes Paket,
das wirklich hinausgeht, zusätzlich in den Jitterpuffer der Hörprobe ein. Es beginnt nur mit einem Paket, das BEGINN
trägt; wird es mitten in einer Phase abgeschaltet, schließt die Kette sie mit ENDE, damit der Puffer sauber ausblendet.
Weil es nur bei offenem Gate spielt, blieb die Hörprobe bis 0.4.0 still, solange das Gate nicht öffnete: Ein Netz, das bis
0.4.1 an der Konsole ohne `--va` angelegt wurde, stand ab Werk auf Taste, und Global steht immer auf Taste (im Verwaltungsteil
angelegte Netze und die damalige Startvorlage außer Command standen auf VA, A5.2; seit 0.5.0 steht auch ein an der Konsole ohne
Schalter angelegtes Netz auf VA, heute auch jeder Kanal der Vorlage Star Citizen, und gespeicherte Netze bleiben, wie sie waren, E2.3), und ohne
Verbindung gibt es kein Sendenetz
(`HoerprobeTests.Hoerprobe_MithoerenSpieltNurBeimSenden_UrsacheDesFehlerberichts`: ohne Sprechtaste 0 Pakete und stille
Wiedergabe, mit Sprechtaste hörbar). Das war die Ursache des Fehlerberichts von Sarevoc vom 01.10.2026; welche Auslösung
sein Sendenetz hatte und ob er die Sprechtaste hielt, ist nicht bekannt.

Die Oberfläche lässt das Mithören seit 0.4.1 nur mit Headset zu, weil es über Lautsprecher dem Senden schadet. Im
Prüfstand mit 30 s Sprache, Mithören an gegen aus, wurde bis 0.4.1 gemessen: mit Echounterdrückung 9 bis 18 % weniger
gesendet, weil der Echoschutz eigene Anlaute bremst; ohne Echounterdrückung 18 bis 43 % mehr, weil das eigene Echo das Gate
offen hält (`PROJEKT.md`, Standzeile 01.10.2026 18:30).

**Neu gemessen in 0.5.0.** Das Mithören spielt die eigenen Sprüche mit dem Funk-Klick wie beim Empfänger, und der Klick ist ab
Werk lauter (E3.9). Das verschiebt die Zahlen. Der Test misst deshalb die Stimme allein ohne Klick und stellt die Werte mit dem
Klick ab Werk daneben (`HoerprobeTests.Mithoeren_UeberLautsprecher_SchadetDemSenden_Begruendung`, Lauf vom 02.10.2026, 30 s
Sprache, gesendete Pakete ohne gegen mit Mithören):

| Aufbau | ohne Klick | mit Klick ab Werk |
| --- | --- | --- |
| mit Echounterdrückung, Lautsprecher mit 12 dB Kopplung | 763 gegen 678 Pakete (11 % weniger) | 726 Pakete (5 % weniger als 763) |
| ohne Echounterdrückung, Lautsprecher mit 3 dB Kopplung | 783 gegen 1041 Pakete (33 % mehr) | 1132 Pakete (45 % mehr als 783) |

Mit dem Klick bis 0.4.1 waren es mit Echounterdrückung 695 Pakete statt 726 (Standzeile 02.10.2026 00:10). Der Test verlangt
weiter, dass mit Echounterdrückung höchstens 95 % der Pakete ohne Mithören gesendet werden, dass es ohne Echounterdrückung
mindestens 20 % mehr sind (auch mit Klick) und dass mit Echounterdrückung und Klick nicht mehr gesendet wird als ohne Mithören.
Jede Zahl ist ein Lauf des Tests mit festem Zufallsstartwert der Sprache.

Mund-zu-Ohr des Mithörens, gemessen mit der Geräteattrappe, harter Vokaleinsatz bei gedrückter Taste (Testausgabe
KlangMaschineTests, Hoerprobe_MundZuOhr_Latenz, Lauf vom 01.10.2026 im Stand 0.4.0). Die Messung gilt dem Mithören, das
bis 0.4.0 "Hörprobe" hieß; der Test schaltet auch in 0.4.1 dasselbe Mithören ein (docs\MODULBERICHT-client.md):

| Rauschunterdrückung | Mund-zu-Ohr | Jitterpuffer-Ziel | Verdeckt |
| --- | --- | --- | --- |
| an | 105 ms | 80 ms | 0 Pakete |
| aus | 85 ms | 80 ms | 0 Pakete |

Die 20 ms Unterschied sind der Verzug von RNNoise. Das Ziel von 80 ms ist der Startwert des Jitterpuffers ohne Statistik.
Für die Hörprobe als Aufnahme gibt es keine solche Zahl; sie spielt ohnehin erst nach den 5 Sekunden ab.

### E3.12 Rechenzeit

| Pfad, je 10 ms | Median | p99 | Höchstwert | Quelle |
| --- | --- | --- | --- | --- |
| Mischer mit 16 Quellen, Begrenzer, Clipping (Debug) | 150 µs | 258 µs | 468 µs | Testausgabe RechenzeitTests |
| Stimmhaftigkeit und Gate (Debug) | 158 µs | 212 µs | 483 µs | Testausgabe RechenzeitTests |
| Jitterpuffer ohne Dekodierer (Debug) | 3 µs | 4 µs | 58 µs | Testausgabe RechenzeitTests |
| Klangfarbe mit Begrenzer, alle drei Regler +6 (Debug, seit 0.9.3; neutral 0) | Mittel 37 µs | - | - | Testausgabe KlangfarbeTests vom 11.10.2026 |
| RNNoise | 0,096 bis 0,097 ms | 0,111 bis 0,123 ms | 0,16 bis 0,24 ms | docs\MODULBERICHT-klang.md |
| APM mit AEC3, Hochpass, AGC2 (gemessen bis 0.6.0; AGC2 ist seither aus) | Mittel 0,071 bis 0,072 ms | 0,086 bis 0,094 ms | 0,12 bis 0,14 ms | docs\MODULBERICHT-klang.md |
| Opus kodieren (je 20 ms) | Mittel 0,21 ms | 0,25 bis 0,40 ms | - | docs\MODULBERICHT-klang.md |
| Opus dekodieren (je 20 ms) | Mittel 0,11 ms | 0,14 bis 0,18 ms | - | docs\MODULBERICHT-klang.md |

Alle Werte stammen von einem AMD Ryzen 9 5950X. Die Testausgaben vom 01.10.2026 liefen mit niedriger Priorität neben
anderer Arbeit am Rechner, die Werte des Modulberichts ohne Last. Die Messung neben laufendem Star Citizen steht noch
aus (docs\MODULBERICHT-klang.md, offene Punkte).

### E3.13 Messwerte aus den Tests

#### Wie gemessen wird

- **Fehlauslösung:** eine Sprechphase, die keine markierte Äußerung berührt (GateKennzahlen.cs).
- **Abgeschnittener Anlaut:** Der erste gesendete Rahmen einer Äußerung beginnt mehr als 10 ms nach ihrem markierten
  Anlaut, oder die Äußerung wird gar nicht gesendet. Der Anlaut ist dort markiert, wo die Hüllkurve 20 dB unter dem
  Plateau liegt (GateKennzahlen.cs, Testszenen.cs).
- **Anlaufverzug:** vom Anlaut bis zum Ende des Rahmens, in dem das Gate öffnete. Das ist die Zeit, um die der Spruch
  beim Öffnen hinterherhängt; gesendet wird dank Vorlauf trotzdem ab dem Anlaut.
- **Verpasster Anlaut** (nur in den Echo- und Spieltonmessungen): keine Phase, die spätestens 100 ms nach dem Anlaut
  beginnt (MessungSpielton.cs). Das ist großzügiger als "abgeschnitten"; die beiden Zahlen sind nicht vergleichbar.
- **Szenen** (Testszenen.cs, alle selbst erzeugt, 48 kHz, feste Seeds): Stille ist Grundrauschen bei -80 dBFS. Tastatur:
  Grundrauschen -65 dBFS, im Mittel 7 Anschläge je Sekunde, Spitzen -26 bis -12 dBFS. Tüte: Knisterepisoden von 0,3 bis
  1,5 s mit im Mittel 400 Impulsen je Sekunde, Spitzen -45 bis -12 dBFS. Sprache: Vokale aus drei Formanten mit
  Grundton 100 bis 220 Hz, Äußerungen 0,4 bis 1,6 s, Pegel -32 bis -18 dBFS, jede fünfte weich (120 ms Anstieg, -36 dBFS).
- **Spracherkennung in den Gate-Szenen:** Die Szenen der Gate-Tests laufen ohne APM und ohne RNNoise; die
  Wahrscheinlichkeit kommt vom eigenen Ersatzschätzer `Stimmhaftigkeit` (Autokorrelation, erkennt stimmhafte Laute, keine
  stimmlosen). Mit RNNoise sind die Zahlen laut Testkommentar neu zu messen (GateSzenenTests.cs). Die Echo- und
  Spieltonmessungen dagegen laufen durch die echte Kette mit APM, RNNoise und Opus, nur ohne echte Geräte.

#### Das Gate allein

Nur das Sprach-Gate mit seiner Selbstjustierung, ohne Führung, je 3 Minuten (Testausgabe GateSzenenTests):

| Szene | Öffnungen | Fehlauslösungen je Minute | abgeschnitten | Anlaufverzug | abgewiesene Anläufe |
| --- | --- | --- | --- | --- | --- |
| Stille | 0 | 0,00 | - | - | 0 |
| Tastatur | 0 | 0,00 | - | - | 6 |
| Tüte | 1 | 0,33 | - | - | 78 |
| Sprache | 68 | 0,00 | 0 von 68 | 35 ms | 0 |
| Sprache mit Tastatur | 69 | 0,00 | 0 von 69 | 39 ms | 3 |
| Gegenprobe normal, 1 Minute | 23 | 0,00 | 0 von 23 | 36 ms | 0 |
| Gegenprobe unempfindlich (Abstand mindestens 30 dB, Regler ganz unten), 1 Minute | 14 | 0,00 | 11 von 23 | 57 ms | 0 |

Die Gegenprobe zeigt, dass die Messung etwas misst: Mit absichtlich zu unempfindlicher Einstellung fehlt fast die Hälfte
der Anlaute. Die Tüte liegt mit 0,33 je Minute unter dem Ziel von 0,5, aber knapp (docs\MODULBERICHT-klang.md).

#### Die drei Stellungen

Führung und Gate wie im Produkt, ohne APM und RNNoise, je 2 Minuten; Automatik mit den Startwerten der Klangmaschine,
Lernend mit Korridor -62 bis -40 dBFS, Fest bei -52 dBFS (Testausgabe GateModusTests, Szenen_AlleModi_ZieleAusDemEntwurf):

| Szene | Automatik | Lernend | Fest -52 dBFS |
| --- | --- | --- | --- |
| Stille | 0 Fehlauslösungen | 0 | 0 |
| Tastatur | 0 (1 Anlauf abgewiesen) | 0 (1) | 0 (1) |
| Tüte | 0 (30 abgewiesen) | 0 (24) | 0 (29) |
| Sprache | 0 von 47 abgeschnitten, 36 ms | 0 von 47, 36 ms | 0 von 47, 39 ms |
| Sprache mit Tastatur | 0 von 50 abgeschnitten, 41 ms | 0 von 50, 43 ms | 0 von 50, 42 ms |

In keiner Zelle gab es eine Fehlauslösung. Die Zahlen hinter "abgeschnitten" sind der mittlere Anlaufverzug.

#### Stimmen im Hintergrund

Das Beispiel von Sarevoc aus Anf. A28: Im Hintergrund reden oder schreien andere. Szene: 150 s, eigene Stimme -36 bis -18 dBFS, fremde
Stimmen 26 dB leiser (-62 bis -44 dBFS), etwa die Hälfte der Zeit. Lernend mit Korridor -62 bis -42 dBFS, Anstieg 2 s,
Rückkehr 10 s (Testausgabe GateModusTests, Lernend_StimmenImHintergrund_WenigerFehlausloesungenAlsAutomatisch):

| | Automatik | Lernend |
| --- | --- | --- |
| Fehlauslösungen je Minute | 1,20 | 0,40 |
| Anteil der fremden Zeit, die gesendet wurde | 32 % | 10 % |
| eigene Anlaute abgeschnitten | 0 von 62 | 0 von 62 |
| mittlerer Anlaufverzug | 24 ms | 44 ms |

Lernen drückt die Fehlauslösungen unter das Ziel von 0,5 je Minute, ohne eigene Anlaute zu kosten; der Preis ist ein
etwas längerer Anlaufverzug, weil die Linie höher liegt.

#### Echo, Lautsprecher und Spielton

Echo-Prüfstand der Klangmaschine: Ein anderer Funker (synthetische Sprache, echt Opus-kodiert) spielt über die
Wiedergabe, das Echo kommt 40 ms später ins Mikrofon, der Nutzer schweigt; gezählt ab 5 s, 40 s je Szene (Testausgabe
KlangMaschineTests):

| Echopfad | Fehlphasen ohne AEC | mit AEC3 und Echoschutz |
| --- | --- | --- |
| 3 dB Dämpfung | 14 | 0 |
| 12 dB Dämpfung | 14 | 0 |
| verzerrender Lautsprecher | 14 | 0 |
| verzerrend, AEC3 ohne Echoschutz | - | 5 |

Doppelsprechen bei 12 dB: Eigene Äußerungen ohne Gegenrede kamen alle durch (4 von 4), Äußerungen während die Gegenseite
sprach 5 von 8 (dort ist das Gate beim Öffnen halbduplex).

Spielton und Musik (Testausgabe SpieltonTests; "Headset" heißt hier: Spielton nur in der Referenz, AEC3 an):

| Messung | Ergebnis |
| --- | --- |
| Headset, Spielton -24 dBFS, verpasste Anlaute | Voreinstellung 7 von 68, Upstream-Vorgabe von AEC3 16 von 68, ohne Spielton 3 von 68 |
| Lautsprecher 12 dB, Spielton -24 dBFS, verpasste Anlaute | alter Echoschutz (10 dB immer) 31 von 65, heutiger 21 von 65 |
| Sprachausgaben im Spielton, Nutzer schweigt | ohne Echoschutz 2 Fehlphasen, mit 0 (verzerrend und 12 dB) |
| Spielton oder Musik über Lautsprecher, Nutzer schweigt, fünf Szenen | Schutz ruht bei Geräusch: 1/1/1/4/2 Auslösungen; mit 6 dB: 0/0/0/0/0 |
| Funk neben Musik, verzerrender Lautsprecher | nur RNNoise auf der Referenz: 1 Auslösung; mit Meldung des eigenen Mischers: 0; mit der Meldung und dem Klick ab Werk (0.5.0): 0 |
| Klicks anderer Sprecher über Lautsprecher (0.5.0), Nutzer schweigt, Echounterdrückung an | 14 Sprüche eines anderen, also 28 Klicks, bei 3 dB, 12 dB und verzerrender Kopplung, mit dem Klick wie bis 0.4.1 (-23 dB), ab Werk (-15 dB) und ganz laut (-6 dB): in allen neun Läufen 0 eigene Phasen |

Der Grenzfall "Funk neben Musik" ist mit dem Klick bis 0.4.1 geeicht: Die ersten beiden Zahlen der Zeile stammen aus einem Lauf mit
Klick bei -22,6 dB unter der Sprache, was dessen Spitze von -24 dBFS entspricht; der lautere Klick von 0.5.0 hat diese Messung
verschoben, darum ist sie so geeicht geblieben und mit dem Klick ab Werk einzeln geprüft (0 Auslösungen;
`SpieltonTests.FunkNebenMusik_EchoschutzGreiftWeiter`, Standzeile 02.10.2026 00:10). Die Zeile zu den Klicks
anderer Sprecher steht in `KlickMaschineTests.Klick_AndererSprecherUeberLautsprecher_LoestSprachaktivierungNichtAus`: Stimme und
Klicks kommen 40 ms später ins Mikrofon, das Grundrauschen des Nutzers liegt bei -65 dBFS, gezählt wird nach dem Einschwingen (5 s).

Zum Vergleich ohne AEC: Headset 6 von 68, Lautsprecher 3/4/7 von 65 bei 12/20/30 dB (docs\MODULBERICHT-aec3.md,
Abschnitt 5). Lesart: Mit Headset gibt es kein Echo, und die Echounterdrückung kann bei Spielton Anlaute kosten (heute
7 statt 6 von 68, vor der Abstimmung im Modul aec3 18); deshalb schaltet der Client sie bei "Headset" aus. Mit
Lautsprechern ist sie nötig, und dort kostet der Schutz gegen Echo die meisten Anlaute. Das Ziel aus dem ersten Entwurf
(unter 2 % abgeschnittene Silbenanfänge) ist im Lautsprecherfall mit keiner Einstellung erreicht
(docs\MODULBERICHT-aec3.md, Abschnitt 11.4).

#### Empfang

Jitterpuffer mit simulierter Uhr, vier Sprechphasen zu 5 s, Netz-Grundlaufzeit 30 ms, Verzögerung ab Senden (Testausgabe
JitterpufferTests):

| Netz | Ziel | Verzögerung im Mittel | p95 | Unterläufe | Verdeckt (PLC) |
| --- | --- | --- | --- | --- | --- |
| gleichmäßig | 40 ms | 63 ms | 93 ms | 0 | 0 |
| Jitter gleichverteilt 0 bis 60 ms | 80 ms | 103 ms | 103 ms | 0 | 0 |
| Bündel alle 100 ms | 105 ms | 133 ms | 133 ms | 5 | 3,5 Frames |

Beim gleichmäßigen Netz läuft die erste Phase mit dem Startwert 80 ms (93 ms ab Senden), ab der zweiten mit dem Ziel
40 ms (53 ms); die Unterläufe bei Bündeln fallen alle in die erste Phase (docs\MODULBERICHT-klang.md). Verluste laut
demselben Bericht: 24 von 24 Einzelverlusten über FEC ersetzt; eine Serie von drei: 2 Frames verdeckt, 1 über FEC; eine
Serie von 200 ms: höchstens 60 ms hörbare Verdeckung, dann Stille, danach eingeblendet.

Ende zu Ende ohne Geräte: 40 s Sprache mit Tastatur durch die ganze Kette ergaben 16 Phasen für 16 Äußerungen, 1156
Audiopakete, keinen Kodierfehler und kein verworfenes Paket (Testausgabe KlangMaschineTests,
Kette_EndeZuEnde_SpracheUndTastatur_Deterministisch). Zwei Clients mit echter Kette über den Testzugang: Mikrofon A bis
Ausgang B 50 bis 80 ms (docs\MODULBERICHT-client.md).

### E3.14 Messungen selbst wiederholen

Die Tests laufen ohne Audiogeräte, ohne Eingabegeräte und ohne Fenster. Was echte Geräte anfasst, läuft nur mit
`FUNK_RAUCHTEST=1` und nur, solange kein Spiel und keine Vollbildanwendung läuft (Spielschutz.cs). Aufruf aus dem
Wurzelordner des Repositorys; der Filter ist ein Teil des Testnamens:

```powershell
pwsh -NoProfile -File tests\testlauf.ps1 GateSzenenTests.
pwsh -NoProfile -File tests\testlauf.ps1 -OhneBauen GateModusTests.
pwsh -NoProfile -File tests\testlauf.ps1 -OhneBauen SpieltonTests.
pwsh -NoProfile -File tests\testlauf.ps1 -OhneBauen KlangMaschineTests.
pwsh -NoProfile -File tests\testlauf.ps1 -OhneBauen KlickTests.
pwsh -NoProfile -File tests\testlauf.ps1 -OhneBauen KlickMaschineTests.
pwsh -NoProfile -File tests\testlauf.ps1 -OhneBauen HoerprobeTests.
pwsh -NoProfile -File tests\testlauf.ps1 -OhneBauen JitterpufferTests.
pwsh -NoProfile -File tests\testlauf.ps1 -OhneBauen RechenzeitTests.
```

Neben einem laufenden Spiel mit niedriger Priorität:

```powershell
cmd /c start "" /belownormal /wait /b pwsh -NoProfile -File tests\testlauf.ps1 GateSzenenTests.
```

Hinweise (tests\testlauf.ps1):

- Ohne `-OhneBauen` baut das Skript zuerst. Die Messwerte stehen als eingerückte Zeilen in der Ausgabe, die letzte Zeile
  ist die Summenzeile "x bestanden, y fehlgeschlagen, z gesamt".
- Jeder Lauf schreibt Summenzeile und Exitcode zusätzlich nach `C:\Claude\tmp\testlauf-ergebnis.txt`, mit
  `-Ergebnisdatei` an einen anderen Ort unter `C:\Claude`. Über `cmd /c start` kommt der Exitcode nicht an; dann gilt
  die Datei.
- Das Skript bindet `tools\dotnet-umgebung.ps1` ein, und das bindet `C:\Claude\tools\dev-umgebung.ps1` ein. Es ist also
  für die Arbeitsumgebung unter `C:\Claude` gebaut.
- Die ausführliche Kandidaten- und Spieltonmessung des Moduls aec3 (`MessungSpielton.cs`) läuft nur mit
  `FUNK_MESSUNG=1` und ist hier nicht nachgemessen; ihre Zahlen stehen in docs\MODULBERICHT-aec3.md.

### E3.15 Grenzen und offene Punkte

- **Hörtest fehlt in der Breite.** Alle Zahlen beruhen auf synthetischen Signalen. Offen laut den Berichten: Anlaute mit
  Spielton über Lautsprecher, der Klang des Restechos, RNNoise mit echter Stimme, der Echoschutz-Abstand 6 dB gegen 5 dB,
  echte Musik und echter Spielton, VOIP gegen AUDIO, die Klickparameter, Kürzen und Dehnen im Jitterpuffer
  (docs\MODULBERICHT-aec3.md Abschnitt 8, docs\MODULBERICHT-client.md, docs\MODULBERICHT-klang.md).
- **Funk-Klick am echten PC:** Die Voreinstellung von -12 und -15 dB (E3.9) ist gerechnet und im Prüfstand gemessen, nicht am
  Ohr. Offen ist der Hörtest: Doppelklick und Piep anhören und bei Lautsprechern auf Echo achten (`PROJEKT.md`, Nächste Schritte,
  Standzeile 02.10.2026 00:10). Dass 28 Klicks anderer die eigene Sprachaktivierung über Lautsprecher nicht auslösen (E3.13), ist
  mit synthetischer Kopplung gemessen, nicht mit einem echten Raum.
- **Melde-Taste:** Ein eigener Klick für die Meldung (die Melde-Taste klickt nicht, wenn die Sprechtaste schon gehalten ist)
  ist laut `PROJEKT.md` (Nächste Schritte) im Entwurf RAID-S2-ENTWURF unter "Später" vorgesehen und nicht gebaut; im Entwurf selbst
  nicht nachgesehen. "Tasten je Funkfeld" gibt es im Client nicht als eigene Sprechtasten;
  geprüft sind mehrere Sprechtasten über Tastatur, Maus und Joystick.
- **Lautsprecher mit Spielton** bleibt der schwierigste Fall: 21 von 65 Anlauten verpasst bei 12 dB Kopplung.
- **Hörprobe von 0.4.1 am echten PC:** Gemessen ist sie nur im Prüfstand mit synthetischer Kopplung. Der Hörtest mit
  Lautsprechern und mit Headset, auch mit dem Tischmikrofon und den Lautsprechern aus dem Fehlerbericht, steht aus
  (`PROJEKT.md`, Nächste Schritte 1).
- **Leise, weich einsetzende Äußerungen:** Der Modulbericht Client nennt mit Stand 29.09.2026 etwa 6 %, die über 100 ms
  zu spät öffnen (Ziel unter 2 %); das braucht Abstimmung an echten Aufnahmen (docs\MODULBERICHT-client.md). Neuer
  gemessen ist das nicht.
- **Gate-Korpus:** Der erste Entwurf sieht eigene Aufnahmen aus der Gruppe als Regressionstest vor (Tastatur, Maus, Chipstüte,
  Lüfter, Spielton über Boxen, leise Anlaute, Tippen beim Sprechen); es gibt sie noch nicht.
- **Silero** ist nicht eingebaut; die Spracherkennung ist allein RNNoise (siehe oben).
- **Ein Ausgabegerät:** Einzelne Personen auf eigene Geräte (Anf. A9, zweiter Teil) und ein Knopf "Geräte neu suchen" sind für
  später vorgesehen (PROJEKT.md, Nächste Schritte).
- **Joystick:** Ein echter Tastendruck am Stick ist noch nicht gemessen; der Test der Stick-Taste durch Sarevoc steht in den nächsten
  Schritten (PROJEKT.md). Dasselbe gilt für die Melde-Taste an Kommando, die Phasentasten am HOTAS und das Ablegen und Holen des
  Funkfelds mit dem Joystick, während Star Citizen im randlosen Fenster vorn ist: Geprüft sind sie über die echte DirectInput-Quelle
  mit einer Geräteattrappe, nicht an einem echten PC (Standzeile 01.10.2026, 22:21).
- **Echoschutz beim Start:** Die Kopplung lernt ab 0 dB; spricht die Gegenseite gleich nach dem Start, ist das Gate die
  ersten Sekunden strenger (docs\MODULBERICHT-aec3.md, Abschnitt 10). Eine automatische Headset-Erkennung wäre möglich,
  ist aber nicht gebaut und eine Produktentscheidung.
- **Lizenzen der Modellgewichte:** Die DNN-Gewichte in opus.dll und die Modellgewichte in rnnoise.dll sind
  lizenzrechtlich ungeklärt; das entscheidet Sarevoc (THIRD-PARTY-NOTICES.txt, PROJEKT.md, F7.1). Ob das Programm selbst offen oder
  geschlossen lizenziert wird, ist ebenfalls noch nicht entschieden; dieser Teil setzt keine der beiden Möglichkeiten
  voraus.
- **Code-Kommentar überholt:** Der Kopfkommentar von GateVorgaben.cs nennt noch die Rückmeldungen "kein Sprechen" und
  "abgeschnitten". Die gibt es seit der Entscheidung von Sarevoc vom 29.09.2026 nicht mehr (Anf. A28; SchwellenViewModel.cs,
  GateModusTests.cs).

### E3.16 Am Rand: Funkfeld und Durchsichtigkeit

- Das Funkfeld zeigt je Netz, wer gerade spricht und wie laut (Anf. A18). Die Werte kommen aus der Empfangsanzeige der
  Klangmaschine: je Sprecher der lauteste 10-ms-Block seit der letzten Abfrage, gemessen so, wie er ankommt, also vor
  Funker- und Netzpegel und damit auch bei stumm gestelltem Funker; etwa 25 Meldungen je Sekunde (Empfangsseite.cs,
  KlangMaschine.cs). Die Hörprobe erscheint dort nicht.
- Die eigene Pegelanzeige bekommt etwa 50 Meldungen je Sekunde, je zwei Aufnahmerahmen zusammengefasst, mit Pegel,
  Rauschboden, Linie, Sprachwahrscheinlichkeit und ob gesendet wurde (KlangMaschine.cs).
- Wie durchsichtig das Funkfeld ist, stellst du mit dem Regler in seiner Titelzeile ein, 0 bis 85 % in Schritten zu 5
  (Anf. A30; FunkfeldFenster.xaml). Mit dem Klang hat das nichts zu tun.
- Seit 0.5.0 lässt sich das Funkfeld in den Infobereich der Taskleiste ablegen und von dort holen (Taste, Joystickknopf oder
  Klick auf das Symbol). In der Ablage rechnet der Bildtakt keine Pegel (Standzeile 01.10.2026, 22:21). Das Symbol und das Ablegen sind nur im Code und im Bildlauf geprüft,
  nicht an einem echten PC.

### E3.17 Quellen dieses Teils

Code (`src\`):

- Funk.Klang\Geraete: WasapiSystem.cs, Eingangswandler.cs, Ausgabewandler.cs
- Funk.Klang\Kern: EchoProzessor.cs, RnnoiseProzessor.cs, OpusKodierer.cs, OpusDekodierer.cs
- Funk.Klang\Maschine: KlangMaschine.cs, KlangMaschine.Geraete.cs, KlangMaschinenOptionen.cs, Sendekette.cs,
  Echoschutz.cs, GateFuehrung.cs, GateVorgaben.cs, Empfangsseite.cs, IKlangMaschine.cs
- Funk.Klang\Verarbeitung: SprachGate.cs, GateStatistik.cs, GateKennzahlen.cs, Stimmhaftigkeit.cs, Sendeaufbereitung.cs,
  Sprachkennzeichen.cs, Jitterpuffer.cs, Mischer.cs, Begrenzer.cs, WeichesClipping.cs, FunkKlick.cs, Pegel.cs, Biquad.cs und
  Klangfarbe.cs (beide neu in 0.9.3)
- Funk.Klang\Eingabe: RawInputSprechtaste.cs, RawInputAuswertung.cs, DirectInputSprechtaste.cs, DirectInputGeraete.cs,
  DirectInputSuche.cs, Joystickauswertung.cs, Joystickeingabe.cs, SprechtastenVerbund.cs, Tastenbelegung.cs
- Funk.ClientKern: Sprechkanal.cs
- Funk.Client: Klang\KlangBruecke.cs, Modelle\SchwellenViewModel.cs, Modelle\AudioViewModel.cs,
  Modelle\HoerprobeViewModel.cs (neu in 0.4.1), Modelle\KlickViewModel.cs (neu in 0.5.0), Modelle\Meldenetzwahl.cs (neu in 0.5.0),
  Klang\Sprechsteuerung.cs (neu in 0.5.0), Modelle\TastenViewModel.cs, Modelle\FunkfeldViewModel.cs, Modelle\KachelViewModel.cs, Modelle\NetzeViewModel.cs,
  Ablage\ClientEinstellungen.cs, Steuerelemente\SchwellenGrafik.cs, Fenster\Schwellenbereich.xaml, Fenster\Bereiche.xaml,
  Fenster\Hauptfenster.xaml, Fenster\FunkfeldFenster.xaml

Tests (`tests\Funk.Tests\`), Ausgaben vom 01.10.2026 (Funk-Klick, Mithören und Funk neben Musik vom 01. und 02.10.2026, Stand 0.5.0):

- Verarbeitung: GateSzenenTests.cs, GateTests.cs, Testszenen.cs, JitterpufferTests.cs, KlickTests.cs, MischerTests.cs,
  SendeaufbereitungTests.cs, RechenzeitTests.cs
- Maschine: GateModusTests.cs, SpieltonTests.cs, MessungSpielton.cs, KlangMaschineTests.cs, KodiererZielTests.cs,
  TastenTests.cs, EingabeTests.cs, JoystickTests.cs, KlickMaschineTests.cs (0.5.0), HoerprobeTests.cs (0.4.1; Ergebnisse aus
  `PROJEKT.md`, die Zahlen zum Mithören über Lautsprecher in E3.11 vom 02.10.2026)
- Oberfläche: OberflaecheHoerprobeTests.cs (0.4.1, unter anderem der Text unter "Beim Senden mithören")
- Klangfarbe (0.9.3, Lauf vom 11.10.2026): Verarbeitung\KlangfarbeTests.cs, Maschine\KlangfarbeMaschineTests.cs,
  Oberflaeche\OberflaecheKlangfarbeTests.cs
- Spielschutz.cs, Testlauf.cs, tests\testlauf.ps1

Dokumente:

- docs\MODULBERICHT-klang.md, docs\MODULBERICHT-aec3.md, docs\MODULBERICHT-client.md
- PROJEKT.md, THIRD-PARTY-NOTICES.txt
- docs\entwurf\ENTSCHEIDUNGEN.md (Anf. A3, A4, A7, A9, A10, A11, A14, A18, A26, A27, A28, A30, A62, A63, A64; Kapitel Klang mit
  den Zielen des ersten Entwurfs)
