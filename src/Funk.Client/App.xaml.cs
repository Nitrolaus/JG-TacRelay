// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Client.Fenster;
using Funk.Client.Klang;
using Funk.Client.Messung;
using Funk.Client.Modelle;
using Funk.Klang.Eingabe;
using Funk.Klang.Maschine;

namespace Funk.Client;

/// <summary>
/// Zusammensetzung der Oberfläche. Schalter:
/// <list type="bullet">
/// <item><c>--vorfuehrung</c>: Attrappen für Funkbetrieb und Klang mit lebender Szene, ohne Netz, ohne Audiogerät und
/// ohne globale Tasten und Joysticks (kein Raw Input, kein DirectInput).</item>
/// <item><c>--bereich start|einstellungen|raid|verwaltung|sprachaktivierung</c>: mit diesem Bereich starten, wenn es ihn gibt (seit 0.10.0, E22;
/// bis 0.9.3 auch audio und netze, die Seiten sind aufgelöst). <c>--schwelle</c> klappte bis 0.9.3 die Sprachaktivierung unten auf; seit 0.10.0
/// gibt es dort die Statuszeile, und der Schalter entfällt.</item>
/// <item><c>--testzugang 127.0.0.1:port</c>: für diesen Lauf über den Testzugang eines funk-testserver verbinden (nicht
/// gespeichert) und gleich verbinden.</item>
/// <item><c>--stumm</c>: echte Geräte, aber die Wiedergabe gibt Stille aus (Probeläufe, wenn niemand zuhört).</item>
/// <item><c>--funkfeld-messen bericht.json [--ansicht raster|liste] [--felder 5|10] [--sekunden 3] --sichtbar-freigegeben</c>:
/// nur das Funkfeld mit Attrappen öffnen und seine echte Größe festhalten (<see cref="FunkfeldMessung"/>,
/// tools\funkfeld-messen.ps1); sonst startet nichts.</item>
/// </list>
/// Klang: die <see cref="KlangMaschine"/> (WASAPI), in der Vorführung oder wenn sie sich nicht anlegen lässt die
/// <see cref="KlangMaschinenAttrappe"/>. Die <see cref="KlangBruecke"/> verbindet sie mit dem Funkbetrieb. Globale Tasten
/// kommen über genau eine <see cref="RawInputSprechtaste"/> im <see cref="SprechtastenVerbund"/> (Raw Input gilt je
/// Prozess nur einmal), Joysticks über die <see cref="DirectInputSprechtaste"/>; beide in <see cref="TastenErzeugen"/>.
/// </summary>
public sealed partial class App : Application, IFensterDienst, IZwischenablage, IDisposable
{
    private readonly List<IDisposable> _freigeben = [];
    private Einstellungsablage? _ablage;
    private IKlangMaschine? _maschine;
    private SprechtastenVerbund? _tasten;
    private IFunkDienst? _dienst;
    private HauptViewModel? _haupt;
    private FunkfeldFensterFuehrung? _funkfeld;
    private FunkfeldAblage? _funkfeldAblage;
    private MeldezeileFenster? _meldezeileFenster;
    private SprecheranzeigeFenster? _sprecheranzeigeFenster;
    private MeldeanzeigeFenster? _meldeanzeigeFenster;
    private DispatcherTimer? _bildtakt;

    /// <summary>
    /// Wahr, wenn TacRelay.exe selbst gestartet wurde. Der Konstruktor von <see cref="Application"/> stellt den Start
    /// in die Warteschlange des Dispatchers; ein anderer Prozess, der App nur für ihre Ressourcen anlegt (der WPF-Rauchtest),
    /// bekäme ihn sonst beim nächsten Abarbeiten ganz: Einstellungen im Benutzerprofil, Raw Input, Joysticks, Audiogeräte
    /// und sichtbare Fenster, am 29.09.2026 so geschehen, während Star Citizen lief.
    /// </summary>
    internal static bool AlsProgrammGestartet => ReferenceEquals(Assembly.GetEntryAssembly(), typeof(App).Assembly);

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!AlsProgrammGestartet)
        {
            base.OnStartup(e);
            return;
        }
        var args = e.Args;
        // Messwerkzeug (A32): nur das Funkfeld mit Attrappen, danach Schluss; sonst startet nichts vom Programm. Freigabe,
        // Ablageort und Spiel prüft es selbst, bevor ein Fenster aufgeht. Es kommt vor der Einzelinstanz: Eine Messung ist kein
        // zweiter Start und darf ein laufendes JG TacRelay nicht nach vorn bitten.
        if (FunkfeldMessung.Verlangt(args))
        {
            SpracheSetzen();
            HilfeEinrichten();
            base.OnStartup(e);
            FunkfeldMessung.Starten(this, args);
            return;
        }
        // Nur eine Instanz je Einstellungsdatei (Einzelinstanz.cs): Ein zweiter Start bittet die laufende nach vorn und endet.
        if (!Einzelinstanz.ProgrammStart(this, EinstellungsSpeicher.StandardPfad(), _freigeben))
        {
            Shutdown(0);
            return;
        }
        SpracheSetzen();
        base.OnStartup(e);
        var vorfuehrung = args.Contains("--vorfuehrung", StringComparer.Ordinal);
        var stumm = args.Contains("--stumm", StringComparer.Ordinal);
        var hinweise = new List<string>();
        var testzugang = Wert(args, "--testzugang");
        if (testzugang is not null && !Verbindungsfabrik.TestzugangGueltig(testzugang))
        {
            hinweise.Add("--testzugang braucht 127.0.0.1:Port, ignoriert.");
            testzugang = null;
        }

        var takt = new DispatcherTakt(Dispatcher);
        var speicher = new EinstellungsSpeicher(EinstellungsSpeicher.StandardPfad());
        var einstellungen = speicher.Laden();
        // Die Namen aus dem Setup (A42, anmeldung.json neben den Einstellungen), solange hier noch keine stehen.
        var namenAusSetup = Anmeldedatei.Uebernehmen(einstellungen.Anmeldung, Anmeldedatei.PfadNeben(speicher.Pfad));
        _ablage = new Einstellungsablage(speicher, einstellungen, takt);
        _freigeben.Add(_ablage);
        if (namenAusSetup) _ablage.Vormerken();
        // Farbstellung vor dem ersten Fenster (A31, Design\Farbvorschlaege.cs): seit 19.10.2026 immer Anthrazit, auch für einen
        // alten gespeicherten Wert.
        Farbvorschlaege.Anwenden(Resources, einstellungen.Farbstellung);
        // Hilfetexte an den Knöpfen (Glasdesign), ab Werk an; der Schalter unter Einstellungen wirkt danach sofort.
        HilfeEinrichten();
        Glasdesign.Hilfe.Zeigen = einstellungen.Anzeige.Hilfetexte;

        var maschine = MaschineErzeugen(vorfuehrung, stumm, hinweise);
        _maschine = maschine;
        var verlauf = new PegelVerlauf();
        _freigeben.Add(verlauf.Anbinden(maschine));
        var bruecke = new KlangBruecke(maschine);
        _freigeben.Add(bruecke);

        VerbindungViewModel? verbindung = null;
        Funk.ClientKern.IVerwaltungsteil? verwaltung = null;
        // F1: ein neueres, signiertes Serverprofil aus Welcome landet als serverprofil.json neben den Einstellungen.
        var uebernahme = vorfuehrung || testzugang is not null ? null : new Profiluebernahme(Profilquelle.Eingebaut with { Ablage = Profilquelle.AblageNeben(speicher.Pfad) });
        if (vorfuehrung)
        {
            // Die Vorführung zeigt die Vorlage Star Citizen mit der Rolle Kapitän (seit 19.10.2026; bis dahin das Beispiel ohne Kanäle mit Ch.).
            var attrappe = new FunkDienstAttrappe(FunkDienstAttrappe.KanalBeispiel());
            _dienst = attrappe;
            if (maschine is KlangMaschinenAttrappe km) _freigeben.Add(new Vorfuehrung(attrappe, km));
        }
        else
        {
            // Hintergrundfehler gehen über den Dispatcher in die Lage, nie im Empfangs-Thread der Sprache. Kennt der Server das
            // Gerät nicht, fragt die Startseite die Namen für den Antrag (A42).
            var funk = new FunkClientDienst(() => Verbindungsfabrik.Bauen(_ablage.Aktuell.Verbindung,
                (konto, abbruch) => verbindung!.RueckfrageAsync(konto, abbruch), bruecke, testzugang,
                anmeldung: abbruch => verbindung!.AnmeldungAsync(abbruch), uebernahme: uebernahme), bruecke, takt);
            _dienst = funk;
            // Verwaltungsteil (A45): nur mit dem Admin-Setup und passendem SHA-256; sonst gibt es ihn nicht, ohne Hinweis.
            var ladung = Verwaltungslader.Laden(AppContext.BaseDirectory, Verwaltungslader.Passende(typeof(App).Assembly), new VerwaltungsWirt(funk, takt,
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(speicher.Pfad) ?? AppContext.BaseDirectory, "flotte")));
            verwaltung = ladung.Teil;
            if (verwaltung is not null) _freigeben.Add(verwaltung);
            if (ladung.Hinweis is { } h) hinweise.Add(h);
        }

        verbindung = new VerbindungViewModel(_dienst, _ablage, takt, this, ohneProfil: vorfuehrung || testzugang is not null, uebernahme: uebernahme);
        // Die eine Hörprobe, auf Start, auch verbunden (seit 0.10.0, Q75, E19; bis 0.9.3 dazu im Bereich Audio und auf der Seite Sprachaktivierung).
        // Mithören steht unter Einstellungen > Senden > Erweitert.
        var hoerprobe = new HoerprobeViewModel(maschine, _ablage);
        var audio = new AudioViewModel(maschine, verlauf, _ablage, takt, hoerprobe);
        var funkfeld = new FunkfeldViewModel(_dienst, maschine, verlauf, _ablage, takt, netzKlang: bruecke);
        _freigeben.Add(funkfeld);
        var netze = new NetzeViewModel(_dienst, maschine, takt, _ablage, bruecke, funkfeld);
        _tasten = TastenFuerStart(vorfuehrung, hinweise, TastenErzeugen);
        var tastenVm = new TastenViewModel(_ablage, _tasten, Tastennamen.MitLayout, dienst: _dienst, takt: takt, maschine: maschine);
        _freigeben.Add(tastenVm);
        // S2 (Abschnitt 5): das Symbol im Infobereich, solange das Programm läuft. Geht es nicht, läuft alles andere weiter.
        var symbol = SymbolErzeugen(hinweise);
        if (symbol is not null) _freigeben.Add(symbol);
        // 0.11.1: der Platz der Meldezeile (Raster, Monitor, Probe) unter Funkfeld und Darstellung > Erweitert; die Monitore nur gelesen.
        var meldezeilePlatz = new MeldezeilePlatzViewModel(_ablage, WindowsBildschirme.Instanz.Alle);
        // 0.11.9: die Sprecheranzeige unter Einstellungen (Schalter, Raster, Erweitert); die Monitore und das exklusive Vollbild nur gelesen.
        var sprecherEinstellung = new SprecheranzeigeViewModel(_ablage, WindowsBildschirme.Instanz.Alle, Spielfenster.ExklusivesVollbild,
            dateiWaehlen: Spieldatei.Waehlen);
        // 0.12.0: die Meldungen der Leitung über dem Spiel unter Einstellungen (Lage, Erweitert).
        var meldungenEinstellung = new MeldungenEinstellungViewModel(_ablage);
        var einstellungenVm = new EinstellungenViewModel(_ablage, funkfeld, tastenVm, this, meldezeilePlatz, sprecherEinstellung, meldungenEinstellung);
        // Derselbe Korridor (Klangstufe C1) am Pegelbalken unter Einstellungen > Senden und in der Schwellengrafik.
        var schwelle = new SchwellenViewModel(maschine, verlauf, _ablage, hoerprobe, audio.Korridor);
        // E13: Der Klangtreiber steht seit 0.10.0 unter Einstellungen > Fehlersuche; die Warnungen beim Start bleiben links in der Fußzeile, damit
        // "Globale Tasten nicht verfügbar" oder "Klangmaschine nicht verfügbar" nicht in einem zugeklappten Bereich verschwinden.
        var klangtreiber = maschine switch
        {
            KlangMaschinenAttrappe when vorfuehrung => "Klang: Attrappe, kein Audiogerät geöffnet (Vorführung)",
            KlangMaschinenAttrappe => "Klang: Attrappe, kein Audiogerät geöffnet",
            _ => stumm ? "Klang: WASAPI, Wiedergabe stumm (--stumm)" : "Klang: WASAPI",
        };
        if (testzugang is not null) hinweise.Insert(0, "Testzugang " + testzugang);
        if (speicher.LetzterFehler is { } f) hinweise.Add(f);
        var starthinweise = hinweise.Count > 0 ? string.Join("  ", hinweise) : null;
        _haupt = new HauptViewModel(verbindung, audio, netze, einstellungenVm, funkfeld, schwelle, _dienst, this, _ablage, takt, klangtreiber,
            verwaltung: verwaltung, starthinweise: starthinweise);
        // 0.9.1: Mit offener Admin-Sitzung fragt das Hauptfenster beim Beenden nach; bestätigt schließt es ohne zweite Frage.
        _haupt.BeendenBestaetigt += () =>
        {
            _beendenBestaetigt = true;
            MainWindow?.Close();
        };

        // Auch der Unterpunkt Sprachaktivierung (steht nur in der Navigation, wenn sie benutzt wird); ein Bereich, den es nicht gibt, bewirkt nichts.
        if (Wert(args, "--bereich") is { } bereich)
            _haupt.AktiverBereich = _haupt.Navigation.FirstOrDefault(b => string.Equals(b.Kennung, bereich, StringComparison.Ordinal)) ?? _haupt.AktiverBereich;

        // S2: Meldezeile, Funkfeld in der Ablage, Phasen per Taste, Melde-Taste (RAID-S2-ENTWURF Abschnitte 3 bis 5).
        Func<long> ms = () => Environment.TickCount64;
        var meldezeile = new Meldezeile(ms);
        var phasen = new Phasensteuerung(_dienst, meldezeile, ms);
        var teilnehmer = new Teilnehmermeldungen(_dienst, meldezeile, takt);
        _freigeben.Add(teilnehmer);
        // Klangstufe B: fällt der Rauschfilter Stark auf Standard zurück, sagt es die Meldezeile; seit 0.11.0 in einer Zeile, der Grund steht
        // unter Einstellungen > Senden.
        audio.RauschfilterRueckfall += _ => meldezeile.Zeigen("rauschfilter", AudioViewModel.RueckfallMeldung, Meldeart.Hinweis, Meldezeiten.RauschfilterMs);
        // Klangstufe C2b: das eigene Urteil der Auto Dauersend-Dämpfung mit gelbem "!", die Tasten "Dauersender stumm" und "Mikro stumm", "Mikro ist stumm".
        funkfeld.Meldezeile = meldezeile;
        _funkfeld = new FunkfeldFensterFuehrung(FunkfeldBauen);
        var haupt0 = _haupt;
        _funkfeldAblage = new FunkfeldAblage(_funkfeld, symbol, _ablage, meldezeile, _dienst, ms, _haupt.Funkermodus)
        {
            HauptfensterZeigen = HauptfensterHolen,
            FunkermodusZeigen = () =>
            {
                HauptfensterHolen();
                haupt0.AktiverBereich = haupt0.Bereiche.FirstOrDefault(b => b.Inhalt is EinstellungenViewModel) ?? haupt0.AktiverBereich;
            },
            Beenden = () => MainWindow?.Close(),
            // Seit 0.10.0: "Mikro stumm" im Menü des Symbols, wie die Taste, mit dem Satz in der Meldezeile.
            MikroStumm = () => funkfeld.Mikro.Stumm,
            MikroUmschalten = funkfeld.Mikro.TasteGedrueckt,
        };
        var ablage = _ablage;
        var sprechen = new Sprechsteuerung(maschine, bruecke, () => Meldenetzwahl.Entscheid(ablage.Aktuell.Tasten.Meldenetz, _dienst.Lage));
        _freigeben.Add(new Tastenbetrieb(_tasten, sprechen, takt, funkfeld, _funkfeldAblage, phasen, teilnehmer, _haupt.Brett, _haupt.Bereit));
        // Das X des Funkfelds nennt die Taste, mit der es zurückkommt.
        void AblegenTooltip() => funkfeld.AblegenTooltip = FunkfeldAblage.AblegenTooltip(
            [.. tastenVm.Funktionen.Single(f => f.Funktion == Tastenfunktion.FunkfeldUmschalten).Belegungen.Select(b => b.Name)]);
        AblegenTooltip();
        tastenVm.Geaendert += AblegenTooltip;
        // Sarevoc Q111: Die Meldezeile skaliert mit dem Funkfeld (Schrift, Innenrand, Höhe), auch wenn es abgelegt ist. Seit 0.11.1 steht sie an
        // einem festen Platz (Stelle und Monitor aus den Einstellungen, ab Werk oben Mitte auf dem Monitor des Spiels), nicht am Funkfeld.
        _meldezeileFenster = new MeldezeileFenster(meldezeile, skalierung: () => funkfeld.Skala, wahl: () => meldezeilePlatz.Wahl, spiel: () => Spielfenster.Monitor(Spielprofil.Prozess(_ablage.Aktuell.Spiel)));
        meldezeile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Meldezeile.Sichtbar) or nameof(Meldezeile.Text)) _meldezeileFenster?.Abgleichen();
        };
        funkfeld.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FunkfeldViewModel.Skalierung)) _meldezeileFenster?.Abgleichen();
        };
        // Ein Klick im Raster, die Wahl des Monitors oder "Probe zeigen": 3 s "Hier steht die Meldezeile" an der Stelle, jedes Mal (keine Entprellung);
        // eine stehende Probe zieht bei einer neuen Wahl gleich um.
        meldezeilePlatz.ProbeVerlangt += () => meldezeile.Zeigen(Meldezeile.ProbeSchluessel, Meldezeile.ProbeText, Meldeart.Hinweis, Meldezeiten.ProbeMs, immer: true);
        meldezeilePlatz.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MeldezeilePlatzViewModel.Wahl)) _meldezeileFenster?.Abgleichen();
        };

        // 0.11.9 (Sarevoc 11.10. und 15.10.2026): die Sprecheranzeige über dem Spiel, ein eigenes Fenster nach dem Muster der Meldezeile, ohne Owner. Wer
        // zu hören ist, meldet die Brücke aus dem Melde-Thread (Stempel und Empfangsanzeige); die eigene Sendung liest der Takt am Sprechkanal.
        var sprecher = new Sprecheranzeige(_dienst, () => ablage.Aktuell.Sprecheranzeige, ms, funkfeld.Dauersend.IstStumm,
            () => bruecke.EigeneSendung is { } sendung ? (IReadOnlyList<ushort>)sendung.Netze : null);
        _sprecheranzeigeFenster = new SprecheranzeigeFenster(sprecher, wahl: () => sprecherEinstellung.Wahl,
            spiel: () => Spielfenster.MonitorImVordergrund(Spielprofil.Prozess(_ablage.Aktuell.Spiel)));
        bruecke.SprecherGehoert += gehoert => takt.Einreihen(() => sprecher.Melden(gehoert));
        sprecher.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Sprecheranzeige.Sichtbar)) _sprecheranzeigeFenster?.Abgleichen();
        };
        // Jedes Schieben an der Lage (Waagerecht, Senkrecht) oder die Wahl des Monitors: die Probezeile genau dort über dem Spiel, live beim Schieben und
        // noch 3 s nach dem letzten (Sarevoc 15.10.2026, Schieberegler statt Raster); eine stehende Anzeige zieht gleich mit.
        sprecherEinstellung.ProbeVerlangt += sprecher.ProbeZeigen;
        sprecherEinstellung.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(SprecheranzeigeViewModel.Wahl) or "" or null)) return;
            sprecher.Takt();
            _sprecheranzeigeFenster?.Abgleichen();
        };

        // 0.12.0 (Meldesystem, M3-M23): die Meldungen der Leitung über dem Spiel, ein eigenes Fenster wie die Sprecheranzeige, ohne Besitzer. Befehle kommen
        // als Ereignis (Push 0x4005), der laufende Stand aus der Lage; der Ton über die Klangmaschine, "gemeldet" von der Bereitmeldung.
        var haupt1 = _haupt;
        var meldeanzeige = new Meldeanzeige(_dienst, () => ablage.Aktuell.Meldungen, ms, ton: maschine.TonSpielen, gemeldet: () => haupt1.Bereit.GemeldetNummer);
        _meldeanzeigeFenster = new MeldeanzeigeFenster(meldeanzeige, wahl: () => meldungenEinstellung.Wahl,
            spiel: () => Spielfenster.MonitorImVordergrund(Spielprofil.Prozess(_ablage.Aktuell.Spiel)));
        _dienst.Ereignis += e =>
        {
            if (e is MeldungEingegangen m) takt.Einreihen(() => meldeanzeige.Eingegangen(m.Stand));
        };
        meldeanzeige.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Meldeanzeige.Sichtbar)) _meldeanzeigeFenster?.Abgleichen();
        };
        // Jedes Schieben an der Lage und jede Wahl unter Erweitert: die Probe genau dort über dem Spiel, noch 3 s danach; eine stehende Anzeige zieht mit.
        meldungenEinstellung.ProbeVerlangt += meldeanzeige.ProbeZeigen;
        meldungenEinstellung.PropertyChanged += (_, _) =>
        {
            meldeanzeige.Takt();
            _meldeanzeigeFenster?.Abgleichen();
        };

        var haupt = HauptfensterBauen();
        MainWindow = haupt;
        haupt.Show();
        // "Funkfeld beim Start zeigen": an zeigt es ohne Fokus, aus legt es ab (das Symbol bleibt).
        _funkfeldAblage.Starten();

        // Bildtakt: etwa 30 Bilder je Sekunde für Pegel, Kacheln und Verlaufsgrafik; dazu Meldezeile, Phasenfristen, Tooltip des Symbols, seit
        // 0.11.9 die Sprecheranzeige (Nachhalten, Ausblenden, eigene Sendung) und alle 2 s die Prüfung auf das exklusive Vollbild, seit 0.12.0 die
        // Meldungen über dem Spiel (Ende des Befehls, Countdown mit Ton, Probe) und im Hauptmodell das Feld der Leitung (Sperre von 2 s).
        _bildtakt = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) =>
        {
            _haupt.Takt();
            meldezeile.Takt();
            phasen.Takt();
            _funkfeldAblage.Takt();
            sprecher.Takt();
            sprecherEinstellung.Takt();
            meldeanzeige.Takt();
        }, Dispatcher);
        _bildtakt.Start();

        if (_ablage.Aktuell.Audio.BeimStartOeffnen) _ = audio.StartenCommand.ExecuteAsync(null);
        if (vorfuehrung || testzugang is not null || _ablage.Aktuell.Verbindung.BeimStartVerbinden) _ = verbindung.VerbindenCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// WPF formatiert Bindungen sonst nach en-US (Punkt statt Komma). Run und andere Textelemente erben den Standardwert
    /// nicht vom TextBlock, darum beide.
    /// </summary>
    internal static void SpracheSetzen()
    {
        if (_spracheGesetzt) return;
        _spracheGesetzt = true;
        var sprache = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(sprache));
        // FrameworkContentElement trägt die Eigenschaft per AddOwner; überschreiben lässt sie sich erst für TextElement (Run).
        FrameworkContentElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.Documents.TextElement), new FrameworkPropertyMetadata(sprache));
    }

    private static bool _spracheGesetzt;

    /// <summary>
    /// Das Hilfe-System kommt aus Glasdesign (<see cref="Glasdesign.Hilfe"/>: halbe Sekunde Verzögerung, Schalter "Tooltips
    /// zeigen"). Dessen Anzeigedauer von 20 s ersetzt JG TacRelay durch die Voreinstellung von WPF, unbegrenzt (Befund N2,
    /// WCAG 1.4.13): WPF erlaubt das Überschreiben der ToolTip-Metadaten nur einmal je Typ, und Glasdesign lässt eine frühere
    /// Überschreibung stehen. Deshalb vor dem ersten Hilfe.Text (vor dem ersten Fenster) aufrufen, einmal je Prozess.
    /// </summary>
    internal static void HilfeEinrichten()
    {
        if (_hilfeEingerichtet) return;
        _hilfeEingerichtet = true;
        System.Windows.Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(int.MaxValue));
        Glasdesign.Hilfe.Einrichten();
    }

    private static bool _hilfeEingerichtet;

    /// <summary>
    /// Echte Klangmaschine (WASAPI), außer in der Vorführung. Lässt sie sich nicht anlegen (etwa fehlende native DLLs),
    /// startet die Oberfläche trotzdem mit der Attrappe und sagt es.
    /// </summary>
    private static IKlangMaschine MaschineErzeugen(bool vorfuehrung, bool stumm, List<string> hinweise)
    {
        if (vorfuehrung) return new KlangMaschinenAttrappe(vorfuehrung: true);
        try
        {
            return new KlangMaschine(new KlangMaschinenOptionen { AusgabeStumm = stumm });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            hinweise.Add("Klangmaschine nicht verfügbar: " + ex.Message);
            return new KlangMaschinenAttrappe();
        }
    }

    /// <summary>
    /// Globale Tasten nur im echten Betrieb. Die Vorführung fasst weder Tastatur und Maus (Raw Input mit INPUTSINK) noch
    /// Joysticks (DirectInput) an; sonst schalteten etwa Numpad + und -, in einem anderen Programm getippt, ihre Netze um.
    /// </summary>
    internal static SprechtastenVerbund? TastenFuerStart(bool vorfuehrung, List<string> hinweise, Func<List<string>, SprechtastenVerbund?> erzeugen)
    {
        ArgumentNullException.ThrowIfNull(hinweise);
        ArgumentNullException.ThrowIfNull(erzeugen);
        if (!vorfuehrung) return erzeugen(hinweise);
        hinweise.Add("Tasten: aus (Vorführung)");
        return null;
    }

    /// <summary>
    /// Globale Tasten: Raw Input für Tastatur und Maus, DirectInput für Joysticks (nur lesend, nicht-exklusiv). Geht
    /// DirectInput nicht, läuft es ohne Joystick weiter und sagt es.
    /// </summary>
    private static SprechtastenVerbund? TastenErzeugen(List<string> hinweise)
    {
        try
        {
            var verbund = new SprechtastenVerbund(new RawInputSprechtaste());
            try
            {
                verbund.Hinzufuegen(new DirectInputSprechtaste());
            }
            catch (InvalidOperationException ex)
            {
                hinweise.Add("Joystick nicht verfügbar: " + ex.Message);
            }
            return verbund;
        }
        catch (InvalidOperationException ex)
        {
            hinweise.Add("Globale Tasten nicht verfügbar: " + ex.Message);
            return null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _bildtakt?.Stop();
        _ablage?.Sichern();
        // Erst die Tasten (ein gehaltenes Mikrofon wird dabei losgelassen), dann Funk und Klang.
        _tasten?.Dispose();
        var dienst = _dienst;
        var maschine = _maschine;
        // Außerhalb des Oberflächen-Threads, damit kein await auf ihn zurück will.
        Task.Run(async () =>
        {
            if (dienst is not null) await dienst.DisposeAsync().ConfigureAwait(false);
            if (maschine is not null) await maschine.DisposeAsync().ConfigureAwait(false);
        }).Wait(TimeSpan.FromSeconds(5));
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        foreach (var f in _freigeben) f.Dispose();
        _freigeben.Clear();
        GC.SuppressFinalize(this);
    }

    /// <summary>Aus dem Hauptfenster (Knopf "Funkfeld", Strg+F): zeigen und nach vorn, auch aus der Ablage.</summary>
    public void FunkfeldZeigen() => _funkfeldAblage?.Zeigen(mitFokus: true);

    /// <summary>
    /// Baut das Funkfeld-Fenster für die Ablage. Beim ersten Start ohne gemerkten Platz kommt es auf den Monitor des Hauptfensters
    /// (A32), später in dieser Sitzung an die Stelle des vorigen (Neuaufbau für eine Farbstellung). Das X und Alt+F4 legen es ab
    /// (S2); wirklich geschlossen wird es nur mit dem Programm und beim Neuaufbau.
    /// </summary>
    private FunkfeldFenster FunkfeldBauen()
    {
        var neu = new FunkfeldFenster(_haupt!.Funkfeld, _ablage!, bezug: () => Mitte(MainWindow), bisher: _funkfeldZuletzt);
        // Bis 0.11.0 zog die Meldezeile bei jeder Größe des Funkfelds nach; seit 0.11.1 hängt sie nicht mehr daran (nur noch an der Skalierung).
        neu.Closing += (s, e) =>
        {
            if (_funkfeldAblage is { SchliessenAbfangen: true } ablage)
            {
                e.Cancel = true;
                // Nicht mitten im Schließen verbergen: gleich danach, über den Dispatcher.
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => ablage.SchliessenAngefragt()));
                return;
            }
            if (s is FunkfeldFenster z) _funkfeldZuletzt = z.Platzfuehrung.Aktuell();
        };
        return neu;
    }

    /// <summary>Hauptfenster aus dem Menü des Symbols: zeigen, wiederherstellen, nach vorn (der Klick dort kam von dir).</summary>
    private void HauptfensterHolen()
    {
        if (MainWindow is not { } w) return;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    /// <summary>Das Symbol im Infobereich; lässt es sich nicht anlegen, läuft das Programm ohne und sagt es.</summary>
    private static Ablagesymbol? SymbolErzeugen(List<string> hinweise)
    {
        try
        {
            return new Ablagesymbol();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        {
            hinweise.Add("Symbol im Infobereich nicht verfügbar: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Wo das zuletzt geschlossene Funkfeld dieser Sitzung stand (Bildpunkte, nicht gespeichert). Gemerkt wird ein Platz nur
    /// nach einem Handgriff (A32); ohne diesen hier spränge ein neu aufgebautes Funkfeld, das du nie bewegt, aber in der
    /// Ansicht gewechselt hast, oben mittig auf den Monitor des Hauptfensters, womöglich auf den anderen Bildschirm.
    /// </summary>
    private FensterOrt? _funkfeldZuletzt;

    /// <summary>Mitte eines gezeigten Fensters in Bildpunkten (GetWindowRect, nur gelesen), sonst <c>null</c>.</summary>
    private static (int X, int Y)? Mitte(Window? fenster) =>
        fenster is not null && WindowsBildschirme.Rahmen(fenster) is { } r ? (r.Links + (r.Breite / 2), r.Oben + (r.Hoehe / 2)) : null;

    /// <summary>
    /// Baut das Funkfeld für eine neue Farbstellung neu (A31): schließen und wieder öffnen, gleiche Ansicht und Feldzahl (aus
    /// den Einstellungen), am selben Platz (der gemerkte, sonst der beim Schließen festgehaltene, <see cref="_funkfeldZuletzt"/>),
    /// ohne Aktivieren, außer es hatte vorher den Fokus. Die Durchsichtigkeit öffnet nie ein neues Fenster (A33).
    /// </summary>
    private void FunkfeldNeuAufbauen() => _funkfeldAblage?.NeuAufbauen();

    /// <summary>
    /// Baut das Hauptfenster, beim Neuaufbau für eine Farbstellung am Platz des alten (<paramref name="bisher"/>); es schließt
    /// beim Schließen das Funkfeld geordnet mit, außer beim Neuaufbau.
    /// </summary>
    private Hauptfenster HauptfensterBauen(FensterOrt? bisher = null)
    {
        var haupt = new Hauptfenster(_haupt!, _ablage!, bisher: bisher);
        haupt.Closing += (_, e) =>
        {
            if (_umbau) return;
            if (!_beendenBestaetigt && _haupt!.AdminSitzungOffen)
            {
                e.Cancel = true;
                _haupt.BeendenFrageOffen = true;
                return;
            }
            // Programmende: das Funkfeld wirklich schließen (nicht ablegen), das Symbol entfernen (NIM_DELETE), die Meldezeile, die
            // Sprecheranzeige und die Meldungen zu.
            _funkfeldAblage?.ProgrammEnde();
            _meldezeileFenster?.Close();
            _sprecheranzeigeFenster?.Close();
            _meldeanzeigeFenster?.Close();
        };
        return haupt;
    }

    private bool _umbau;
    private bool _beendenBestaetigt;

    /// <summary>
    /// A31: Farbstellung wechseln, sofort für beide Fenster. Die Stile lösen ihre Pinsel beim Bauen auf (StaticResource), und
    /// ein stehendes Fenster behielte seine alten Farben (gemessen in OberflaecheLiveFarbwahlTests). Deshalb nach dem Tausch
    /// der Wörterbücher beide Fenster neu bauen: das Hauptfenster am selben Platz, mit demselben Bereich, das Funkfeld wie
    /// bei <see cref="FunkfeldNeuAufbauen"/>. Nicht mitten im Klick auf die Wahl, der das auslöst.
    /// </summary>
    public void FarbenAnwenden(Farbstellung stellung) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => FarbenUmbauen(stellung)));

    /// <summary>
    /// Öffnet einen Ordner im Explorer (Knopf "Ordner öffnen" bei den Einstellungen, N1 aus der Gegenprüfung Stufe 2: Der Pfad selbst
    /// steht nirgends auf dem Bildschirm). Ein Fehler beim Starten des Explorers ist keine Störung des Funkbetriebs.
    /// </summary>
    public void OrdnerOeffnen(string ordner)
    {
        try
        {
            Directory.CreateDirectory(ordner);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", ArgumentList = { ordner }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            System.Diagnostics.Trace.TraceWarning("Ordner öffnen gescheitert: " + e.GetType().Name);
        }
    }

    private void FarbenUmbauen(Farbstellung stellung)
    {
        if (_haupt is null || _ablage is null || MainWindow is not Hauptfenster alt) return;
        Farbvorschlaege.Anwenden(Resources, stellung);
        // Am Platz des alten, auch wenn er nicht gemerkt ist (A32 merkt nur nach einem Handgriff, nicht nach Win+Pfeil).
        var neu = HauptfensterBauen(alt.Platzfuehrung.Aktuell());
        neu.Show();
        MainWindow = neu;
        _umbau = true;
        try
        {
            alt.Close();
        }
        finally
        {
            _umbau = false;
        }
        neu.Activate();
        FunkfeldNeuAufbauen();
    }

    public bool Kopieren(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Ein anderes Programm hält die Zwischenablage gerade offen.
            return false;
        }
    }

    private static string? Wert(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
