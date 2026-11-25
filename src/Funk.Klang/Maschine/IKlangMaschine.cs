// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

public enum GateModus { Fest, Automatisch, Lernend }

/// <summary>
/// Einstellung der Sprachaktivierung; Bedeutung, Voreinstellung und Grenzen in <see cref="GateVorgaben"/>. <paramref name="GrenzlinieDb"/>
/// (seit 0.6.0, Stufe 2c) wirkt nur im Modus Automatisch und nur, solange die eigene Stimme nicht gelernt ist; ab Werk
/// <see cref="GateVorgaben.MinPegelDb"/>, also ohne Wirkung. Seit Klangstufe C1 liegt der Korridor relativ zur gelernten Funkstimme:
/// <paramref name="MindestAbstandDb"/> (Mindest-Funklautstärke, so viel unter der Stimme) und <paramref name="ObergrenzeAbstandDb"/>
/// (Obergrenze, mindestens 6 dB unter der Stimme). <paramref name="VorlaufMs"/> ist der Vorlauf des Gates (ab Werk 120 ms).
/// </summary>
public readonly record struct GateEinstellung(GateModus Modus, float SchwelleDb, float AbstandDb, float KorridorVonDb, float KorridorBisDb, float AnstiegSekunden,
    float RueckkehrSekunden, float Sprachsicherheit, float GrenzlinieDb = GateVorgaben.MinPegelDb,
    float MindestAbstandDb = GateVorgaben.MindestAbstandAbWerkDb, float ObergrenzeAbstandDb = GateVorgaben.ObergrenzeAbstandAbWerkDb,
    int VorlaufMs = GateVorgaben.VorlaufAbWerkMs);

/// <summary>
/// Eine Pegelmeldung der Sendekette, etwa 50 je Sekunde. <paramref name="WuerdeSenden"/> (Q43, 0.8.0) setzt nur die Oberfläche (der Pegelverlauf des
/// Clients): Die Sprachaktivierung würde auslösen oder die Sprechtaste ist gedrückt, aber es geht nichts hinaus; nie zusammen mit
/// <paramref name="Sendet"/>. Die Maschine meldet es nie. <paramref name="Zurueckgehalten"/> (seit 0.10.0, E4a) meldet die Maschine: Das Gate hält in
/// einem der zwei Rahmen eine bestätigte Sprechphase zurück, weil sie unter der Mindest-Funklautstärke liegt (<see cref="GateZustand.Zurueckgehalten"/>);
/// es geht nichts hinaus. Nie mit der Sprechtaste und nie bei Mikro stumm. Die Statuszeile sagt dann "Stimme unter der Mindest-Funklautstärke".
/// </summary>
public readonly record struct PegelMeldung(long ZeitMs, float PegelDbfs, float RauschbodenDb, float SchwelleDb, float Sprachwahrscheinlichkeit, bool Sendet, bool Stumm = false,
    bool WuerdeSenden = false, bool Zurueckgehalten = false);

/// <summary>
/// Der Korridor, in dem sich die Linie gerade bewegen darf (Klangstufe C1), in dBFS am Gate. <paramref name="StimmeDb"/>: die gelernte
/// Funkstimme am Gate, NaN solange sie nicht bekannt ist (dann gibt es keine Untergrenze aus der Stimme). <paramref name="UntergrenzeDb"/>
/// (Mindest-Funklautstärke) und <paramref name="ObergrenzeDb"/>: was wirkt, NaN heißt keine Grenze. <paramref name="BezugDb"/>: wovon aus
/// die Mindest-Funklautstärke gemessen wird (der Grundpegel beim Einmessen oder Lernen, mit der jetzigen Verstärkung); NaN heißt: die Stimme.
/// <paramref name="AnkerDbfs"/>: derselbe Bezug vor der Pegelstufe (der Anker), zum Merken mit der Verstärkung (Gegenprüfung C1, H1); NaN unbekannt.
/// Momentaufnahme aus dem Aufnahme-Thread.
/// </summary>
public readonly record struct KorridorStand(float StimmeDb, float UntergrenzeDb, float ObergrenzeDb, float BezugDb = float.NaN, float AnkerDbfs = float.NaN)
{
    /// <summary>Wovon aus die Mindest-Funklautstärke gemessen wird: <see cref="BezugDb"/>, sonst die Stimme.</summary>
    /// <remarks>Nie über der Stimme (Nacharbeit M1, 04.10.2026): Ist die gesendete Funkstimme leiser als der Bezug, gilt die Stimme.</remarks>
    public float MindestBezugDb => float.IsFinite(BezugDb) ? (float.IsFinite(StimmeDb) ? MathF.Min(BezugDb, StimmeDb) : BezugDb) : StimmeDb;

    public static KorridorStand Leer { get; } = new(float.NaN, float.NaN, float.NaN);

    public bool StimmeBekannt => float.IsFinite(StimmeDb);
}

public sealed record AudioGeraet(string Id, string Name, bool IstStandard);

/// <summary>
/// Stand der Geräte: gestartet, ob Aufnahme und Wiedergabe laufen, und woran das Öffnen zuletzt scheiterte (null: kein
/// Fehler). Die Maschine läuft auch mit nur einem der beiden, damit man die Netze hört, wenn Windows das Mikrofon
/// sperrt (A7), und versucht das fehlende Gerät selbst wieder.
/// </summary>
public readonly record struct GeraeteZustand(bool Gestartet, bool AufnahmeLaeuft, bool WiedergabeLaeuft, string? AufnahmeFehler, string? WiedergabeFehler);

/// <summary>Gewählter und wirkender Rauschfilter (Klangstufe B); <paramref name="Rueckfall"/> sagt, warum Stark nicht wirkt, oder null.</summary>
public readonly record struct RauschfilterStand(Rauschfilter Gewaehlt, Rauschfilter Wirksam, string? Rueckfall);

/// <summary>Die Hörprobe als Aufnahme (<see cref="IKlangMaschine.ProbeStarten"/>): bereit, nimmt auf oder spielt ab.</summary>
public enum ProbeZustand { Bereit, Aufnahme, Wiedergabe }

/// <summary>Stand der Hörprobe als Aufnahme und wie lange Aufnahme oder Wiedergabe noch laufen (ms, ohne Puffer und Gerät).</summary>
public readonly record struct ProbeStand(ProbeZustand Zustand, int RestMs);

/// <summary>Stand einer Stimmprobe (<see cref="IKlangMaschine.StimmprobeStarten"/>): läuft sie, wie viel ist aufgenommen, ist sie fertig.</summary>
public readonly record struct StimmprobeStand(bool Laeuft, bool Fertig, int AufgenommenMs, int DauerMs);

/// <summary>
/// Vertrag der Klangmaschine für Oberfläche und Funkbetrieb. Ereignisse kommen aus Threads der Maschine, nie aus dem
/// Oberflächen-Thread; alle Methoden dürfen aus jedem Thread kommen. Funknetze bildet die Anbindung auf Mischer-Netze
/// 0 bis <see cref="FunkNetze"/> - 1 ab; je Mischer-Netz gelten Lautstärke und Panorama (A9).
/// </summary>
public interface IKlangMaschine : IAsyncDisposable {
  /// <summary>Mischer-Netze für Funknetze: 0 bis 62; 63 gehört der Hörprobe.</summary>
  const int FunkNetze = 63;

  IReadOnlyList<AudioGeraet> Eingaenge(); IReadOnlyList<AudioGeraet> Ausgaenge();
  /// <summary>Öffnet Mikrofon und Wiedergabe. Scheitert eins davon, läuft das andere (<see cref="Geraete"/>); wirft nur, wenn beide scheitern.</summary>
  Task StartenAsync(string? eingangId, string? ausgangId, CancellationToken ct);
  Task StoppenAsync();
  /// <summary>Fehlende Geräte sofort neu öffnen ("Erneut versuchen"); sonst versucht die Maschine es selbst in Abständen.</summary>
  void GeraeteWiederholen();
  GeraeteZustand Geraete { get; }
  /// <summary>
  /// Windows meldet ein Audiogerät neu, entfernt oder als neuen Standard (seit 0.10.0): Die Maschine hat ihre Ströme schon abgeglichen; die
  /// Oberfläche liest daraufhin <see cref="Eingaenge"/> und <see cref="Ausgaenge"/> neu (der Knopf "Geräte neu lesen" entfällt). Auch ohne laufendes
  /// Audio, aus dem Steuer-Thread der Maschine, nie aus dem Oberflächen-Thread.
  /// </summary>
  event Action? GeraeteListeGeaendert;
  event EventHandler<Exception>? Fehler;        // Hintergrundfehler (Gerät weg, Treiber, Ereignisempfänger)
  event Action<PegelMeldung>? Pegel;            // etwa 50 je Sekunde, aus einem Nicht-UI-Thread
  GateEinstellung Gate { get; set; }
  bool Sprachaktivierung { get; set; }          // false = nur Sprechtaste (A10: folgt dem Sendenetz)
  void SprechtasteGedrueckt(); void SprechtasteLosgelassen();
  /// <summary>
  /// Sprechtaste gedrückt, mit Angabe, ob ein Druck im Nachlauf (<see cref="TastenNachlaufMs"/>) den Spruch fortsetzt. Wahr (wie
  /// <see cref="SprechtasteGedrueckt()"/>): dieselbe Taste fürs selbe Ziel, die Phase läuft nahtlos weiter. Falsch: die andere Taste, also ein
  /// anderes Ziel (Melde- statt Sprechtaste oder umgekehrt); dann endet die Phase im nächsten Rahmen mit ENDE, und eine neue beginnt, mit Klick
  /// beim Loslassen und beim Drücken. So bleibt der Rest des alten Spruchs an seinem Ziel (Prüfung 0.9.3). Ohne Nachlauf ein gewöhnlicher Druck.
  /// </summary>
  void SprechtasteGedrueckt(bool fortsetzen) => SprechtasteGedrueckt();
  /// <summary>
  /// Nachlauf der Sprechtaste in ms (Q66, seit 0.9.3): So lange sendet die Maschine nach dem Loslassen weiter wie mit gedrückter Taste, auch
  /// bei der Melde-Taste. APM, Rauschfilter und Opus verzögern die Stimme; ohne Nachlauf fehlte das Ende der letzten Silbe. Ein neuer Druck derselben
  /// Taste im Nachlauf setzt dieselbe Phase fort (kein ENDE, kein neuer Beginn, kein Klick), einer der anderen beendet sie (siehe
  /// <see cref="SprechtasteGedrueckt(bool)"/>), "Mikro stumm" beendet ihn sofort, der Klick beim Loslassen kommt erst nach ihm. Die Sprachaktivierung
  /// hat ihren eigenen Nachlauf und bleibt unberührt. Gesetzt wird auf 0 bis
  /// <see cref="TastenNachlaufHoechstMs"/> in 10-ms-Schritten begrenzt (<see cref="TastenNachlaufBegrenzen"/>); ab Werk 0 wie bis 0.9.2, der Client
  /// setzt seine Einstellung. Aus jedem Thread; gilt ab dem nächsten Loslassen.
  /// </summary>
  int TastenNachlaufMs { get; set; }
  /// <summary>Höchster Nachlauf der Sprechtaste (<see cref="TastenNachlaufMs"/>).</summary>
  const int TastenNachlaufHoechstMs = 1000;
  /// <summary>Ein Nachlauf auf 10 ms gerundet und auf 0 bis <see cref="TastenNachlaufHoechstMs"/> begrenzt.</summary>
  static int TastenNachlaufBegrenzen(int ms) => (int)Math.Round(Math.Clamp(ms, 0, TastenNachlaufHoechstMs) / 10.0, MidpointRounding.AwayFromZero) * 10;
  /// <summary>
  /// Funk-Klick (A11, A63): Klang, eigene Taste und andere Sprecher je an oder aus mit Lautstärke, bei Sprachaktivierung dezent
  /// oder aus. Die eigene Sprechtaste (<see cref="SprechtasteGedrueckt()"/>) klickt beim Drücken und Loslassen, andere Sprecher bei
  /// Beginn und Ende ihrer Phase. Setzen aus jedem Thread; gilt ab dem nächsten 10-ms-Block. Ungültige Werte werfen
  /// <see cref="ArgumentException"/>.
  /// </summary>
  FunkKlickEinstellungen Klick { get; set; }
  /// <summary>Knopf "Anhören": eigener Klick, dann der eines anderen Sprechers. Falsch, wenn die Wiedergabe nicht läuft oder beide aus sind.</summary>
  bool KlickAnhoeren();
  /// <summary>
  /// Signalton des Meldesystems (seit 0.12.0): kurz, selbst erzeugt, lokal gemischt (<see cref="Verarbeitung.Signalton"/>), mit der
  /// Funk-Lautstärke. Aus jedem Thread; falsch, wenn die Wiedergabe nicht läuft (dann ist nichts zu hören).
  /// </summary>
  bool TonSpielen(Signaltonart art) => false;
  /// <summary>
  /// Mithören: was gerade gesendet wird, lokal abspielen (Mischer-Netz 63). Nur bei offenem Gate, also mit Sprechtaste
  /// oder wirkender Sprachaktivierung; über Lautsprecher kommt es zurück ins Mikrofon (deshalb in der Oberfläche nur mit Headset).
  /// </summary>
  bool Hoerprobe { get; set; }
  /// <summary>Länge der Hörprobe als Aufnahme.</summary>
  const int ProbeDauerMs = 5000;
  /// <summary>
  /// Hörprobe als Aufnahme (Fehlerbericht 01.10.2026): <see cref="ProbeDauerMs"/> der eigenen Stimme nach der ganzen Kette und
  /// Opus aufnehmen, unabhängig von Sprechtaste und Schwelle, danach auf Mischer-Netz 63 abspielen wie beim Empfänger. Während
  /// der Aufnahme spielt nichts davon, die Wiedergabe wird nicht wieder aufgenommen; so entsteht auch über Lautsprecher keine
  /// Schleife. Von Beginn bis kurz nach dem Ende sendet die Sprachaktivierung nicht, die Sprechtaste wie immer. Läuft schon
  /// eine Probe, beginnt sie neu.
  /// </summary>
  void ProbeStarten(); void ProbeAbbrechen(); ProbeStand Probe { get; }
  /// <summary>
  /// Stimmprobe (Q60, seit 0.9.2; seit 0.9.3 für Klangmessungen allgemein, die Stimmerkennung ist eingestellt): <paramref name="ms"/> des Signals
  /// hinter Echo- und Rauschfilter, vor Klangfarbe und Sprachaktivierung, 48 kHz mono, unabhängig von Sprechtaste und Senden. Eine neue Probe
  /// verwirft die alte. Abholen mit <see cref="StimmprobeHolen"/>, sobald <see cref="Stimmprobe"/> fertig meldet.
  /// </summary>
  void StimmprobeStarten(int ms) { }
  /// <inheritdoc cref="StimmprobeStarten"/>
  StimmprobeStand Stimmprobe => default;
  /// <summary>Die fertige Stimmprobe (48 kHz mono) oder <c>null</c>; danach ist sie weg.</summary>
  float[]? StimmprobeHolen() => null;
  bool Echounterdrueckung { get; set; }
  /// <summary>
  /// Klangmodule (seit 0.9.0, Sarevoc 06.10.2026, Q48 bis Q50), je ab Werk an und aus jedem Thread setzbar. <see cref="Echoschutz"/>: Das Echo der
  /// Lautsprecher öffnet die Sprachaktivierung nicht (<see cref="Echoschutz"/>-Klasse, A14). <see cref="MindestFunklautstaerke"/>: Eine Phase
  /// geht erst hinaus, wenn ihr Anlauf so laut ist wie die Mindest-Funklautstärke (Klangstufe C1). <see cref="Pegelausgleich"/>: Zu laute
  /// Funker werden bei mir langsam leiser angeglichen (Q29), unabhängig von der Auto Dauersend-Dämpfung.
  /// </summary>
  bool Echoschutz { get; set; }
  /// <inheritdoc cref="Echoschutz"/>
  bool MindestFunklautstaerke { get; set; }
  /// <inheritdoc cref="Echoschutz"/>
  bool Pegelausgleich { get; set; }
  /// <summary>
  /// Klangfarbe der eigenen Stimme (Q70 bis Q72, seit 0.9.3): Wärme, Klarheit und Schärfe je -6 bis +6 dB in ganzen dB, lautheitsneutral
  /// (<see cref="Verarbeitung.Klangfarbe"/>). Sie sitzt hinter dem Rauschfilter und vor Hörprobe und Sprachaktivierung; Gate, Pegelanzeige und
  /// Wächter sehen den Pegel von davor. Ab Werk neutral, dann überbrückt. Gesetzt wird begrenzt (<see cref="KlangfarbeEinstellung.Begrenzt"/>);
  /// aus jedem Thread, die Kette blendet in 20 ms über.
  /// </summary>
  KlangfarbeEinstellung Klangfarbe { get; set; }
  /// <summary>
  /// Rauschfilter (Klangstufe B): Standard (RNNoise, ab Werk), Stark (DeepFilterNet3) oder Aus; genau einer wirkt. Setzen aus jedem
  /// Thread. Stark wird außerhalb des Audio-Threads angelegt, rechnet sich warm und wird in einer Pause eingeblendet; fehlt die
  /// Rechenzeit oder meldet DeepFilterNet3 einen Fehler, wirkt wieder Standard (<see cref="Rauschfilterstand"/>), bis Stark neu
  /// gewählt oder Audio neu gestartet wird.
  /// </summary>
  Rauschfilter Rauschfilter { get; set; }
  /// <summary>Gewählter und wirkender Rauschfilter, dazu der Grund eines Rückfalls auf Standard (null: keiner).</summary>
  RauschfilterStand Rauschfilterstand { get; }
  /// <summary>
  /// Lautstärke der eigenen Stimme vor dem Senden (Klangstufe A): automatisch (Halteregel, ändert sich nur in Pausen, nie mitten
  /// im Satz) oder fest. Setzen aus jedem Thread; gilt ab dem nächsten Aufnahmerahmen. Siehe <see cref="Pegelstufe"/>.
  /// </summary>
  LautstaerkeEinstellung Lautstaerke { get; set; }
  /// <summary>
  /// Einmessen der festen Verstärkung: <see cref="Pegelstufe.EinmessRahmen"/> Rahmen lang Sprache sammeln (normal und einmal
  /// deutlich lauter). Das Ergebnis steht danach in <see cref="Einmessen"/>; es gilt erst, wenn es über <see cref="Lautstaerke"/>
  /// gesetzt wird. Ein neuer Start beginnt von vorn.
  /// </summary>
  void EinmessenStarten(); void EinmessenAbbrechen(); EinmessStand Einmessen { get; }
  /// <summary>Korridor der Sprachaktivierung, wie er gerade wirkt (Klangstufe C1); ohne laufende Kette <see cref="KorridorStand.Leer"/>.</summary>
  KorridorStand Korridor => KorridorStand.Leer;
  /// <summary>
  /// "Mindestlautstärke einmessen" (Klangstufe C1, Q12): ein Satz wie am Funk, dann derselbe Satz leise wie zur Familie
  /// (<see cref="Mindesteinmessung"/>). Während des Einmessens beginnt die Sprachaktivierung nichts Neues, die Sprechtaste sendet wie
  /// immer. Das Ergebnis (Grundverstärkung der Pegelstufe und Abstand der Untergrenze) gilt erst, wenn es über <see cref="Lautstaerke"/>
  /// und <see cref="Gate"/> gesetzt wird. Ein neuer Start beginnt von vorn.
  /// </summary>
  void MindestEinmessenStarten() { } void MindestEinmessenAbbrechen() { } MindestEinmessStand MindestEinmessen => default;
  /// <summary>Telemetrie der Sendekette (Laufzeit je Stufe, verpasste Fristen, letzter Rahmen), als Momentaufnahme.</summary>
  TelemetrieStand Telemetrie { get; }
  /// <summary>
  /// Klangprotokoll: Ordner, in den die Maschine eine Datei <c>klangprotokoll-*.csv</c> mit den Messwerten der Sendekette schreibt
  /// (nur außerhalb der Audio-Threads); <c>null</c> heißt aus. Nur auf Wunsch, der Client gibt seinen Ablageordner.
  /// </summary>
  string? KlangprotokollOrdner { get; set; }

  // Senden: kodierte eigene Sprache aus dem Melde-Thread der Maschine, nie aus dem Audio-Thread.
  event SendeAusgang? SprachpaketBereit;
  /// <summary>Erwarteter Paketverlust in Prozent (VerlustHinweis des Servers), für die In-band-FEC.</summary>
  void SetzeVerlustHinweis(int prozent);

  // Empfangen: je Sprecher ein Jitterpuffer im Mischer.
  /// <summary>Uhr der Maschine in Mikrosekunden; mit ihr stempelt der Transport die Ankunft.</summary>
  long JetztUs { get; }
  /// <summary>Ein geprüftes Sprachpaket; <paramref name="netz"/> ist das Mischer-Netz. Falsch, wenn es nicht angenommen wurde.</summary>
  bool SpracheEmpfangen(uint kontoId, int netz, ushort sprechphase, uint frameNr, Sprachkennzeichen kennzeichen, ReadOnlySpan<byte> opus, long ankunftUs, bool durchsage = false);
  void PegelFuerFunker(uint kontoId, float db);                                  // A4
  void SetzeNetzLautstaerke(int netz, float db); void SetzeNetzPanorama(int netz, float panorama); // A9: -1 links, 0 beide, +1 rechts
  event SprecherPegelAusgang? EmpfangsPegel;    // A18: je Sprecher, etwa 25 je Sekunde
  /// <summary>
  /// Funk-Lautstärke (seit 0.10.0, Q74, E7): Faktor 0 (stumm) bis 1 (ab Werk) auf alles, was JG TacRelay abspielt, also Funk, Klick und Hörprobe
  /// zusammen; Windows und das Spiel bleiben unberührt. Der Client rechnet ihn aus dem Regler (Prozent zum Quadrat). Aus jedem Thread, gerampt
  /// (<see cref="Verarbeitung.Mischer.FunkLautstaerke"/>); was keine Zahl ist, gilt als 1.
  /// </summary>
  float FunkLautstaerke { get; set; }

  // ---- Klangstufe C2a: Auto Dauersend-Dämpfung und Mikro stumm (Oberfläche baut C2b) ----

  /// <summary>
  /// Schalter "Auto Dauersend-Dämpfung" (ab Werk an) und gewählte Stufe für andere Dauersender (ab Werk "deutlich leiser"). Aus: Der Wächter
  /// urteilt weiter (für das gelbe Zeichen), stellt aber nichts. Setzen aus jedem Thread.
  /// </summary>
  DaempfungEinstellung Daempfung { get; set; }
  /// <summary>Das eigene Urteil des Wächters mit Ursache in Wörtern (Keins, Echo, Spielton, Raumgeräusch, klemmende Taste).</summary>
  WaechterStand EigenesUrteil { get; }
  /// <summary>Das eigene Urteil hat sich geändert (Melde-Thread).</summary>
  event Action<WaechterStand>? EigenesUrteilGeaendert;
  /// <summary>
  /// Funker mit Urteil, Dämpfung oder einer Wahl am gelben Zeichen: Urteil, Dämpfung in dB (Admin) und in Wörtern, stumm, nicht mehr dämpfen.
  /// Funker ohne all das fehlen in der Liste.
  /// </summary>
  IReadOnlyList<FunkerDaempfung> Dauersender { get; }
  /// <summary>Die Liste <see cref="Dauersender"/> hat sich geändert (Melde-Thread).</summary>
  event Action? DauersenderGeaendert;
  /// <summary>Wahl am gelben Zeichen eines Funkers: stumm, wieder normal (hebt stumm, "nicht mehr dämpfen" und die Dämpfung auf), nicht mehr dämpfen.</summary>
  void FunkerHandeln(uint kontoId, FunkerAktion aktion);
  /// <summary>
  /// Taste "Dauersender stumm" (Q8): Der erste Druck schaltet den zuletzt gedämpften Funker stumm, der zweite macht ihn wieder normal. Rückgabe:
  /// sein neuer Stand, oder null, wenn bisher keiner gedämpft wurde.
  /// </summary>
  FunkerDaempfung? ZuletztGedaempftenUmschalten();
  /// <summary>
  /// Mikro stumm (Q31, Q36): Alle Mikrofonrahmen werden verworfen, nichts geht hinaus (auch nicht mit Sprechtaste), kein Lernen, kein Wächter;
  /// das Gerät bleibt offen, die Echounterdrückung rechnet weiter. Pegelmeldungen tragen <see cref="PegelMeldung.Stumm"/>. Windows bleibt unberührt.
  /// </summary>
  bool MikroStumm { get; set; }
  /// <summary>Die Sprechtaste wurde gedrückt, während das Mikro stumm ist (für "Mikro ist stumm" in der Meldezeile; Melde-Thread).</summary>
  event Action? SprechtasteBeiStumm;
}
