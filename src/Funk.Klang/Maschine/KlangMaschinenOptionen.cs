// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Geraete;
using Funk.Klang.Kern;
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>Einstellungen der <see cref="KlangMaschine"/>, die zur Laufzeit fest bleiben. Die Voreinstellung ist das Produkt.</summary>
public sealed class KlangMaschinenOptionen
{
    /// <summary>
    /// Audiogeräte; null heißt WASAPI (<see cref="WasapiSystem"/>, im Steuer-Thread angelegt und von der Maschine
    /// freigegeben), aber nur in JG TacRelay selbst oder mit <c>FUNK_RAUCHTEST=1</c> (<see cref="KlangMaschine.EchteGeraeteSperre"/>).
    /// </summary>
    public IAudioSystem? AudioSystem { get; init; }

    /// <summary>Monotone Uhr in Mikrosekunden für Jitterpuffer und Zeitstempel; Standard <see cref="Jitterpuffer.MonotonUs"/>.</summary>
    public Func<long>? UhrUs { get; init; }

    /// <summary>
    /// Wahr (Produkt): eigener Steuer-Thread für Geräte und eigener Melde-Thread für Sende-Ausgang und Pegel. Falsch
    /// (Tests): Starten und Stoppen laufen im Aufrufer, zugestellt wird nur über <see cref="KlangMaschine.Zustellen"/>.
    /// </summary>
    public bool EigeneThreads { get; init; } = true;

    /// <summary>WebRTC APM (AEC3, Hochpass) in der Kette.</summary>
    public bool MitApm { get; init; } = true;

    /// <summary>
    /// APM-Einstellung bei eingeschalteter Echounterdrückung; ausgeschaltet gilt dieselbe ohne AEC3. Seit Klangstufe A ohne AGC2:
    /// Die Lautstärke regelt die <see cref="Pegelstufe"/> hinter dem APM, vor RNNoise. <see cref="ApmEinstellungen.Sprechfunk"/> mit
    /// <see cref="MitPegelstufe"/> falsch ist die alte Kette (bis 0.6.0 Stufe 2c), nur noch für Vergleichsmessungen.
    /// </summary>
    public ApmEinstellungen Apm { get; init; } = ApmEinstellungen.EchoUndHochpass;

    /// <summary>
    /// Abweichende AEC3-Konfiguration (<see cref="EchoProzessor.Aec3Einstellen"/>, etwa <see cref="EchoProzessor.Aec3Webrtc"/>
    /// für den Vergleich mit der Upstream-Vorgabe); null heißt: die Voreinstellung der DLL.
    /// </summary>
    public string? Aec3 { get; init; }

    /// <summary>Startwert der Verzögerung Wiedergabe zu Aufnahme für AEC3 (schätzt danach selbst).</summary>
    public int EchoVerzoegerungMs { get; init; } = 20;

    /// <summary>RNNoise in der Kette (Rauschunterdrückung und Sprachwahrscheinlichkeit).</summary>
    public bool MitRnnoise { get; init; } = true;

    /// <summary>Pegelstufe mit Sicherheitsbegrenzer hinter dem APM, vor RNNoise (Klangstufe A).</summary>
    public bool MitPegelstufe { get; init; } = true;

    public OpusAnwendung OpusAnwendung { get; init; } = OpusAnwendung.Voip;

    public int Bitrate { get; init; } = OpusKodierer.StandardBitrate;

    /// <summary>Puffer der Wiedergabe in ms (WASAPI geteilt, ereignisgesteuert); Reserve gegen Aussetzer.</summary>
    public int AusgabeLatenzMs { get; init; } = 30;

    public JitterpufferEinstellungen? Jitterpuffer { get; init; }

    public MischerEinstellungen? Mischer { get; init; }

    /// <summary>
    /// Wiedergabe stumm (Rauchtest mit echten Geräten, --stumm): gemischt wird, ausgegeben Stille. Echte Geräte außerhalb
    /// von JG TacRelay sind immer stumm.
    /// </summary>
    public bool AusgabeStumm { get; init; }

    /// <summary>Ersatz für den Opus-Kodierer (Tests).</summary>
    public Func<IKodierer>? KodiererFabrik { get; init; }

    /// <summary>Ersatz für den Opus-Dekodierer je Sprecher und Hörprobe (Tests).</summary>
    public Func<IDekodierer>? DekodiererFabrik { get; init; }

    /// <summary>
    /// Ersatz für das Anlegen von DeepFilterNet3 (Rauschfilter Stark; Tests: etwa eine Fabrik, die wirft wie bei fehlender DLL);
    /// null heißt <see cref="DeepFilterProzessor"/> mit <see cref="DeepFilterSchwellen.Voll"/>.
    /// </summary>
    public Func<DeepFilterProzessor>? DeepFilterFabrik { get; init; }

    /// <summary>Ersatz für die Sprachwahrscheinlichkeit aus RNNoise (Tests, Rückfall).</summary>
    public ISprachwahrscheinlichkeit? Sprachwahrscheinlichkeit { get; init; }

    /// <summary>
    /// Startwert des Rauschens im Funk-Klick (<see cref="FunkKlick"/>, Tests: in jedem Lauf dieselbe Folge); null heißt zufällig,
    /// so wie im Programm.
    /// </summary>
    public int? KlickStartwert { get; init; }
}
