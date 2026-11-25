// JG TacRelay - Maintainer: Sarevoc
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Klang.Maschine;
using Funk.Klang.Verarbeitung;

namespace Funk.Client.Modelle;

/// <summary>
/// Seite Einstellungen (seit 0.10.0 nach der Mischung, Q87, Bilder m-4 und m-5; bis 0.9.3 eine Seite mit Karten für globale Tasten, Funkfeld,
/// Farben, Tooltips, Start, Tastatur und Ablage): oben offen die Karte "Ich" (<see cref="MeinProfil"/>, E14), darunter die Abschnitte Tasten,
/// Empfangen, Senden, Funkfeld und Darstellung und Fehlersuche (<see cref="Abschnitte"/>), je mit dem Kasten "Läuft von selbst" (Fehlersuche mit
/// einem Satz) und dem zugeklappten Klappbereich "Erweitert:" (E6; bis 0.11.0 unter anderem Namen). Was man zum Funken braucht, steht auf Start;
/// unter Senden stehen seit 0.11.1 die Klangmodule und die Funkschwelle offen (Sarevoc 12.10.2026), sonst steht hier, was selten jemand anfasst. Die
/// Seiten Audio und Meine Kanäle gibt es nicht mehr, ihr Inhalt steht unter Senden und in den Klappbereichen. Seit 0.11.9 steht zwischen Funkfeld
/// und Darstellung und Fehlersuche der Abschnitt Sprecheranzeige (<see cref="AbschnittSprecheranzeige"/>, <see cref="Sprecheranzeige"/>), seit 0.12.0
/// danach der Abschnitt Meldungen (<see cref="AbschnittMeldungen"/>, <see cref="Meldungen"/>).
/// <para>
/// Gebaut in zwei Schritten: Der Konstruktor kennt Ablage, Funkfeld und Tasten (so legen ihn auch Tests an); die Abschnitte entstehen erst mit
/// <see cref="AbschnitteBauen"/>, wenn das Hauptfenster-Modell Audio, Kanäle und Profil kennt.
/// </para>
/// </summary>
public sealed partial class EinstellungenViewModel : ObservableObject
{
    /// <summary>
    /// Ab dieser Breite steht der Kasten "Läuft von selbst" neben dem Namen des Abschnitts, darunter unter ihm (Prüfbericht Mischung, m-4b und m-4c):
    /// Namen (<see cref="NamenBreite"/>) und ein Kasten, der in zwei Zeilen passt. Gemessen in OberflaecheUmbauEinstellungenTests (Messdatei
    /// umbau-einstellungen-mass.txt): Die Kopfzeilen bekommen bei 1100 Fensterbreite 789, bei 960 667, bei 760 467, bei 640 347; mit 560 steht der
    /// Kasten bei 960 daneben (in zwei Zeilen), bei 760 und 640 darunter.
    /// </summary>
    public const double KastenDanebenAb = 560;

    /// <summary>
    /// Breite der Spalte mit den Namen der Abschnitte, damit die Kästen bündig stehen (bis zum Entwurf SharedSizeGroup): der längste Name "Funkfeld und
    /// Darstellung" mit Zeichen und Abstand braucht 211 (gemessen), dazu etwas Luft; ein Vielfaches von 4, damit es bei 125 bis 200 % ganz bleibt.
    /// </summary>
    public const double NamenBreite = 216;

    /// <summary>
    /// Ab dieser Breite stehen die zwei Spalten unter Senden nebeneinander (m-5), darunter untereinander; seit 0.11.1 gilt das für den offenen Teil
    /// (Klangmodule links, Funkschwelle rechts) und für Erweitert. Je Spalte braucht es etwa 280: die drei Knöpfe der Echounterdrückung und den
    /// Balken mit "Einmessen". Bis 0.11.0 im Klappbereich gemessen: bei 1100 Fensterbreite 759, bei 960 648, bei 760 447, bei 640 327
    /// (OberflaecheUmbauEinstellungenTests, Messdatei umbau-einstellungen-mass.txt); offen ist es 21 breiter (Einzug des Klappbereichs).
    /// </summary>
    public const double SendenZweiSpaltenAb = 590;

    /// <summary>
    /// Ab dieser Breite steht eine Zeile der Tasten (Titel links, Marken und "Belegen" rechts) nebeneinander, darunter stehen Marken und Knopf unter dem
    /// Titel: Titel 190, "Belegen" 104 und Platz für zwei Marken.
    /// </summary>
    public const double TastenZeileNebeneinanderAb = 470;

    /// <summary>
    /// Ab dieser Breite steht eine Zeile "Je Person" unter Empfangen > Erweitert nebeneinander (links Name 160 und Platz in 300, rechts Regler mindestens
    /// 100 und Wert 56), darunter Regler und Wert unter Name und Platz. Bis 0.9.3 vier feste Spalten (mindestens 390), die bei 700 Fensterbreite über
    /// die Karte ragten (gemessen in AP3, OberflaecheLiveFarbwahlTests).
    /// </summary>
    public const double PersonZeileNebeneinanderAb = 470;

    /// <summary>
    /// Platz für "Zurück auf Werkseinstellung" im Fuß von Erweitert: der Knopf ist bei 100 und 175 % gut 219 breit (gemessen in AP3), dazu 12 Abstand
    /// zum Satz und etwas Luft.
    /// </summary>
    public const double FussKnopfBreite = 236;

    /// <summary>
    /// Ab dieser Breite steht der Satz, was selbst gestellt ist, neben "Zurück auf Werkseinstellung" (Knopf <see cref="FussKnopfBreite"/> und gut 180 für
    /// den Satz), darunter unter dem Knopf. Bei 640 Fensterbreite blieben neben dem Knopf 88, und "Werkseinstellung." brach mitten im Wort.
    /// </summary>
    public const double FussNebeneinanderAb = 420;

    /// <summary>Hovertext am Regler Lautstärke je Kanal unter Empfangen > Erweitert.</summary>
    public const string KanalLautstaerkeHilfe = "Lautstärke dieses Kanals: wie laut du ihn hörst, im Verhältnis zu den anderen Kanälen; derselbe Wert wie auf der "
        + "Kachel im Funkfeld und für alle Ch. des Kanals. Leiser, wenn ein Kanal dich überdeckt, etwa ein lauter Schiffskanal neben Kommando. Lauter, "
        + "wenn du einen wichtigen Kanal neben den anderen schlecht verstehst. Alles zusammen leiser oder lauter stellst du auf Start mit der "
        + "Funk-Lautstärke. Ab Werk hörst du jeden Kanal so laut, wie er ankommt; \"Zurück auf Werkseinstellung\" stellt alle Kanäle wieder so.";

    /// <summary>Hovertext an "Beim Senden mithören" unter Senden > Erweitert.</summary>
    public const string MithoerenHilfe = "Beim Senden mithören: Du hörst dich selbst, solange du sendest, so wie es hinausgeht. Gut, um kurz zu prüfen, ob dein "
        + "Mikrofon ankommt; für länger ist die Hörprobe auf Start besser, sie spielt dir eine Aufnahme vor. Geht nur mit Headset, über Lautsprecher käme "
        + "deine Stimme verzögert zurück ins Mikrofon. Ab Werk aus; nach einem Neustart wieder aus.";

    /// <summary>
    /// Hovertext an "Genau einstellen ..." unter Senden (E3; seit 0.11.1 offen neben der Funkschwelle). Seit Q117 (Sarevoc 13.10.2026) sagt er, dass die
    /// Seite ihre eigenen Standardwerte hat: Was dort steht, zählt unter Erweitert nicht mit, und "Zurück auf Werkseinstellung" fasst es nicht an.
    /// </summary>
    public const string GenauEinstellenHilfe = "Genau einstellen: Öffnet die Seite Sprachaktivierung mit der Grafik der letzten Sekunden. Dort siehst du deine "
        + "Stimme, die Schwelle und den Korridor und stellst Schwelle, Abstand, Korridor und \"Sprache ab\" von Hand ein. Ab Werk regelt die Automatik "
        + "das selbst, und für fast alle passt es so. Die Seite hat ihren eigenen Knopf Standardwerte; was du dort stellst, zählt unter Erweitert nicht "
        + "mit, und Zurück auf Werkseinstellung dort lässt es, wie es ist. Die Seite gibt es nur, solange ein Kanal mit Sprachaktivierung sendet.";

    /// <summary>
    /// Der Satz mehr im Hovertext an "Zurück auf Werkseinstellung" unter Senden (<see cref="Feinheiten.ZurueckZusatz"/>, seit Q117, Sarevoc 13.10.2026):
    /// Die Seite Sprachaktivierung hat ihre eigenen Standardwerte und bleibt unberührt.
    /// </summary>
    public const string SendenZurueckZusatz = "Die Seite Sprachaktivierung bleibt ebenso, wie sie ist: Was du dort genau eingestellt hast, zählt hier nicht "
        + "mit, und zurück auf ihre Standardwerte stellt sie der Knopf Standardwerte dort.";

    private readonly Einstellungsablage _ablage;
    private readonly FunkfeldViewModel _funkfeld;
    private readonly IFensterDienst? _fenster;

    /// <summary>Namen, eigene Rollen und Zeichen (A46, A49); setzt das Hauptfenster-Modell. Seit 0.10.0 die Karte "Ich" (E14).</summary>
    [ObservableProperty]
    public partial MeinProfilViewModel? MeinProfil { get; set; }

    /// <param name="tasten">Globale Tasten; ohne sie (Tests) wird eine Fassung ohne Quelle angelegt.</param>
    /// <param name="fenster">Öffnet Ordner (Einstellungen, Ablage); ohne ihn (Tests) gibt es den Knopf nicht.</param>
    /// <param name="meldezeile">Der Platz der Meldezeile (seit 0.11.1); ohne ihn (Tests) eine Fassung ohne Monitore.</param>
    /// <param name="sprecheranzeige">Die Sprecheranzeige (seit 0.11.9); ohne sie (Tests) eine Fassung ohne Monitore und ohne Vollbild.</param>
    /// <param name="meldungen">Die Meldungen über dem Spiel (seit 0.12.0); ohne sie (Tests) eine eigene über derselben Ablage.</param>
    public EinstellungenViewModel(Einstellungsablage ablage, FunkfeldViewModel funkfeld, TastenViewModel? tasten = null, IFensterDienst? fenster = null,
        MeldezeilePlatzViewModel? meldezeile = null, SprecheranzeigeViewModel? sprecheranzeige = null, MeldungenEinstellungViewModel? meldungen = null)
    {
        ArgumentNullException.ThrowIfNull(ablage);
        ArgumentNullException.ThrowIfNull(funkfeld);
        _ablage = ablage;
        _funkfeld = funkfeld;
        _fenster = fenster;
        Tasten = tasten ?? new TastenViewModel(ablage, null);
        Meldezeile = meldezeile ?? new MeldezeilePlatzViewModel(ablage, () => []);
        Sprecheranzeige = sprecheranzeige ?? new SprecheranzeigeViewModel(ablage, () => []);
        Meldungen = meldungen ?? new MeldungenEinstellungViewModel(ablage);
        // Belegte globale Netztasten schalten schon selbst; das Funkfeld reagiert dann nicht noch einmal auf + und -. Seit 0.10.0 rechnet es
        // dabei auch den Sendeweg neu: Ohne belegte Sprechtaste geht in einen Kanal auf Taste nichts hinaus (Sendeweg.OhneSprechtaste).
        _funkfeld.NetztastenGlobal = Tasten.NetztastenBelegt;
        Tasten.Geaendert += () =>
        {
            _funkfeld.NetztastenGlobal = Tasten.NetztastenBelegt;
            _funkfeld.AusloesungGeaendert();
        };
        _ablage.FehlerGeaendert += () => OnPropertyChanged(nameof(Fehler));
        _funkfeld.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(FunkfeldViewModel.Durchsichtigkeit):
                    OnPropertyChanged(nameof(Durchsichtigkeit));
                    OnPropertyChanged(nameof(DurchsichtigkeitText));
                    OnPropertyChanged(nameof(DurchsichtWirkung));
                    break;
                case nameof(FunkfeldViewModel.SchriftDeckend):
                    OnPropertyChanged(nameof(SchriftDeckend));
                    OnPropertyChanged(nameof(DurchsichtWirkung));
                    break;
                case nameof(FunkfeldViewModel.Deckung):
                    OnPropertyChanged(nameof(DurchsichtWirkung));
                    break;
                case nameof(FunkfeldViewModel.Skalierung):
                    OnPropertyChanged(nameof(Skalierung));
                    OnPropertyChanged(nameof(SkalierungText));
                    break;
            }
        };
    }

    // ---- Abschnitte (seit 0.10.0, E6) ----

    /// <summary>Audio (Geräte, Klangmodule, Klick, Hörprobe, Stimmproben) für Senden und die Klappbereiche; setzt <see cref="AbschnitteBauen"/>.</summary>
    [ObservableProperty]
    public partial AudioViewModel? Audio { get; private set; }

    /// <summary>Die Kanäle (Felder, Global, Funker) für Empfangen > Erweitert; setzt <see cref="AbschnitteBauen"/>.</summary>
    [ObservableProperty]
    public partial NetzeViewModel? Netze { get; private set; }

    /// <summary>Der Klangtreiber, etwa "Klang: WASAPI" (seit 0.10.0 unter Fehlersuche statt in der Fußzeile, E13).</summary>
    [ObservableProperty]
    public partial string Klangtreiber { get; private set; } = "";

    /// <summary>Hinweise beim Start (etwa "Globale Tasten nicht verfügbar: ..."); stehen auch links in der Fußzeile, damit sie nicht zugeklappt verschwinden.</summary>
    [ObservableProperty]
    public partial string? Starthinweise { get; private set; }

    /// <summary>Oben offen: Namen, Rollenwünsche, Speichern; Zeichen und Sichtbarkeit unter Erweitert (E14).</summary>
    [ObservableProperty]
    public partial Einstellabschnitt? AbschnittIch { get; private set; }

    [ObservableProperty]
    public partial Einstellabschnitt? AbschnittTasten { get; private set; }

    [ObservableProperty]
    public partial Einstellabschnitt? AbschnittEmpfangen { get; private set; }

    [ObservableProperty]
    public partial Einstellabschnitt? AbschnittSenden { get; private set; }

    [ObservableProperty]
    public partial Einstellabschnitt? AbschnittFunkfeld { get; private set; }

    /// <summary>Seit 0.11.9: die Sprecheranzeige über dem Spiel, zwischen Funkfeld und Darstellung und Fehlersuche.</summary>
    [ObservableProperty]
    public partial Einstellabschnitt? AbschnittSprecheranzeige { get; private set; }

    /// <summary>Seit 0.12.0: die Meldungen der Leitung über dem Spiel, nach der Sprecheranzeige.</summary>
    [ObservableProperty]
    public partial Einstellabschnitt? AbschnittMeldungen { get; private set; }

    [ObservableProperty]
    public partial Einstellabschnitt? AbschnittFehlersuche { get; private set; }

    /// <summary>
    /// Alle Abschnitte in der Reihenfolge der Seite: Ich, Tasten, Empfangen, Senden, Funkfeld und Darstellung, Sprecheranzeige, Meldungen, Fehlersuche.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<Einstellabschnitt> Abschnitte { get; private set; } = [];

    /// <summary>Kasten unter Tasten.</summary>
    public const string AutomatikTasten = "Läuft von selbst: Deine Tasten wirken auch, wenn das Spiel vorn ist.";

    /// <summary>Kasten unter Empfangen.</summary>
    public const string AutomatikEmpfangen = "Läuft von selbst: Zu laute Funker werden leiser, Dauersender gedämpft (gelbes !).";

    /// <summary>Kasten unter Senden.</summary>
    public const string AutomatikSenden = "Läuft von selbst: Deine Stimme kommt gleich laut an, leises Reden im Raum bleibt draußen.";

    /// <summary>Kasten unter Funkfeld und Darstellung.</summary>
    public const string AutomatikFunkfeld = "Läuft von selbst: Das Funkfeld wächst mit seinem Inhalt und merkt sich seinen Platz.";

    /// <summary>Satz neben Fehlersuche (dort läuft nichts von selbst).</summary>
    public const string SatzFehlersuche = "Nur nötig, wenn etwas nicht geht.";

    /// <summary>
    /// Was unter Senden > Erweitert steht (seit 0.11.1): die feste Verstärkung (nur ohne automatische Lautstärke nötig), Mithören und die
    /// Klangfarbe. Bis 0.11.0 stand dort das ganze Senden.
    /// </summary>
    public const string InhaltSendenErweitert = "Feste Verstärkung, Mithören, Klangfarbe";

    /// <summary>Satz unter dem Pegelbalken unter Senden: was die Griffe tun.</summary>
    public const string GriffeSatz = "Die Griffe lassen sich ziehen. Wer leiser spricht, geht nicht hinaus.";

    /// <summary>Satz unter "Genau einstellen ..." unter Senden.</summary>
    public const string GenauEinstellenSatz = "Öffnet die Grafik mit Schwelle, Abstand, Korridor und Sprache ab. Ab Werk regelt das die Automatik.";

    /// <summary>
    /// Satz unter "Automatische Lautstärke", solange sie aus ist (seit 0.11.1): Die feste Verstärkung, die dann gilt, misst man unter Senden >
    /// Erweitert ein; offen steht nur der Schalter.
    /// </summary>
    public const string AutomatikAusSatz = "Aus: Es gilt die feste Verstärkung, die du unten unter Erweitert einmisst.";

    /// <summary>
    /// Baut die Abschnitte mit ihren Klappbereichen Erweitert (seit 0.10.0, E6), sobald das Hauptfenster-Modell alles kennt. Einmal je Modell; ein zweiter Aufruf
    /// ändert nichts. Was als "selbst gestellt" zählt, steht je Teil; Gelerntes (Einmessen, gelernte Stimme) und Werte am Server oder am Konto (hören je
    /// Kanal, Pegel je Person, "Mein Zeichen anderen zeigen", "Meine Angaben in Wer ist da zeigen") gehören in keinen Teil, ebenso wenig die Seite
    /// Sprachaktivierung mit ihren eigenen Standardwerten (Q117, Sarevoc 13.10.2026; bis dahin zählte sie unter Senden als "Sprachaktivierung genau eingestellt").
    /// </summary>
    /// <param name="klangtreiber">Etwa "Klang: WASAPI" (E13).</param>
    /// <param name="starthinweise">Hinweise beim Start, oder <c>null</c>.</param>
    public void AbschnitteBauen(AudioViewModel audio, NetzeViewModel netze, string klangtreiber, string? starthinweise)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(netze);
        if (Abschnitte.Count > 0) return;
        Audio = audio;
        Netze = netze;
        Klangtreiber = klangtreiber;
        Starthinweise = string.IsNullOrWhiteSpace(starthinweise) ? null : starthinweise.Trim();
        var a = _ablage;
        var klick = audio.Klick;
        var laut = audio.Lautstaerke;
        Einstellteil[] ich = MeinProfil is { } p
            ?
            [
                new WertTeil<bool>(() => p.ZeichenNebenNamen, v => p.ZeichenNebenNamen = v, true, Teiltext.Schalter("Zeichen neben Namen"), p,
                    nameof(MeinProfilViewModel.ZeichenNebenNamen)),
                new WertTeil<bool>(() => p.ZeichenImFunkfeld, v => p.ZeichenImFunkfeld = v, false, Teiltext.Schalter("Zeichen im Funkfeld"), p,
                    nameof(MeinProfilViewModel.ZeichenImFunkfeld)),
            ]
            : [];
        AbschnittIch = new Einstellabschnitt("Ich", "Symbol.Sprecher", null, new Feinheiten(a, Abschnittskennung.Ich, "Ich", "Zeichen und Sichtbarkeit", ich));
        AbschnittTasten = new Einstellabschnitt("Tasten", "Symbol.Sprechtaste", AutomatikTasten, new Feinheiten(a, Abschnittskennung.Tasten, "Tasten",
            "Nachlauf der Sprechtaste, Kanal vor und zurück, Melde-Taste, weitere Tasten, Funk-Klick",
            [
                Tasten.Nachlauf,
                new WertTeil<bool>(() => klick.EigeneAn, v => klick.EigeneAn = v, true, Teiltext.Schalter("Klick bei deiner Taste"), klick, nameof(KlickViewModel.EigeneAn)),
                new WertTeil<double>(() => klick.EigeneDb, v => klick.EigeneDb = v, FunkKlickEinstellungen.EigeneVoreinstellungDb, d => "Klick bei deiner Taste " + Db(d),
                    klick, nameof(KlickViewModel.EigeneDb)),
                new WertTeil<bool>(() => klick.AndereAn, v => klick.AndereAn = v, true, Teiltext.Schalter("Klick bei anderen"), klick, nameof(KlickViewModel.AndereAn)),
                new WertTeil<double>(() => klick.AndereDb, v => klick.AndereDb = v, FunkKlickEinstellungen.AndereVoreinstellungDb, d => "Klick bei anderen " + Db(d),
                    klick, nameof(KlickViewModel.AndereDb)),
                new WertTeil<bool>(() => klick.BeiSprachaktivierung, v => klick.BeiSprachaktivierung = v, true, an => an ? "Klick bei Sprachaktivierung dezent" : "Klick bei Sprachaktivierung aus",
                    klick, nameof(KlickViewModel.BeiSprachaktivierung)),
            ]));
        AbschnittEmpfangen = new Einstellabschnitt("Empfangen", "Symbol.Hoeren", AutomatikEmpfangen, new Feinheiten(a, Abschnittskennung.Empfangen, "Empfangen",
            "Lautstärke und Hören je Kanal, links oder rechts, Lautstärke je Person, Dämpfung von Dauersendern",
            [
                new KanalTeil(a, _funkfeld, netze),
                new WertTeil<bool>(() => audio.DaempfungAn, v => audio.DaempfungAn = v, true, Teiltext.Schalter("Auto Dauersend-Dämpfung"), audio, nameof(AudioViewModel.DaempfungAn)),
                new WertTeil<Daempfungsstufe>(() => audio.Stufe, v => audio.Stufe = v, Daempfungsstufe.DeutlichLeiser, s => "Dauersender höchstens " + Daempfungsworte.Wort(s),
                    audio, nameof(AudioViewModel.Stufe)),
                new WertTeil<bool>(() => audio.Pegelausgleich, v => audio.Pegelausgleich = v, true, Teiltext.Schalter("Pegelausgleich"), audio, nameof(AudioViewModel.Pegelausgleich)),
            ]));
        // Senden seit 0.11.1 (Sarevoc 12.10.2026): Echounterdrückung, Echoschutz, Rauschunterdrückung, automatische Lautstärke, Sprachaktivierung und
        // Mindest-Funklautstärke stehen offen über dem Klappbereich und zeigen ihren Stand selbst; Erweitert zählt nur, was darin steht (Mithören,
        // Klangfarbe). Die feste Verstärkung steht in Erweitert, ist aber eingemessen, nicht gestellt, und zählt darum nicht. Die genau eingestellte
        // Sprachaktivierung zählte bis Q117 (Sarevoc 13.10.2026, "nein") als eigener Teil mit; seitdem hat ihre Seite ihre eigenen Standardwerte, und der
        // Hovertext am Knopf sagt es (SendenZurueckZusatz).
        AbschnittSenden = new Einstellabschnitt("Senden", "Symbol.Senden", AutomatikSenden, new Feinheiten(a, Abschnittskennung.Senden, "Senden",
            InhaltSendenErweitert,
            [
                new WertTeil<bool>(() => audio.Hoerprobe.Mithoeren, v => audio.Hoerprobe.Mithoeren = v, false, Teiltext.Schalter("Mithören"), audio.Hoerprobe,
                    nameof(HoerprobeViewModel.Mithoeren)),
                audio.Klangfarbe,
            ], SendenZurueckZusatz));
        var platz = Meldezeile;
        AbschnittFunkfeld = new Einstellabschnitt("Funkfeld und Darstellung", "Symbol.Funkfeld", AutomatikFunkfeld, new Feinheiten(a, Abschnittskennung.Funkfeld,
            "Funkfeld und Darstellung", InhaltFunkfeldErweitert,
            [
                new WertTeil<double>(() => Durchsichtigkeit, v => Durchsichtigkeit = v, 0, d => "Durchsichtigkeit " + Prozent(d), this, nameof(Durchsichtigkeit)),
                new WertTeil<bool>(() => SchriftDeckend, v => SchriftDeckend = v, false, Teiltext.Schalter("Schrift deckend"), this, nameof(SchriftDeckend)),
                new WertTeil<double>(() => Skalierung, v => Skalierung = v, 0, d => "Skalierung " + SkalierungText, this, nameof(Skalierung)),
                new WertTeil<bool>(() => FunkfeldBeimStart, v => FunkfeldBeimStart = v, true, an => an ? "Funkfeld beim Start zeigen" : "Funkfeld beim Start abgelegt", this,
                    nameof(FunkfeldBeimStart)),
                // Seit 0.11.1: wo die Meldezeile steht (Raster) und auf welchem Monitor; ab Werk oben Mitte, Automatisch.
                new WertTeil<Meldezeilenstelle>(() => platz.Stelle, v => platz.Stelle = v, FunkfeldEinstellungen.MeldezeileStelleAbWerk, MeldezeilePlatzViewModel.StelleText,
                    platz, nameof(MeldezeilePlatzViewModel.Stelle)),
                new WertTeil<int?>(() => platz.Monitor, v => platz.Monitor = v, (int?)null, MeldezeilePlatzViewModel.MonitorText, platz, nameof(MeldezeilePlatzViewModel.Monitor)),
                new WertTeil<bool>(() => Hilfetexte, v => Hilfetexte = v, true, Teiltext.Schalter("Tooltips"), this, nameof(Hilfetexte)),
                new WertTeil<bool>(() => AudioBeimStart, v => AudioBeimStart = v, true, an => an ? "Audio beim Start öffnen" : "Audio beim Start zu", this, nameof(AudioBeimStart)),
                new WertTeil<bool>(() => VerbindenBeimStart, v => VerbindenBeimStart = v, () => _ablage.Aktuell.Verbindung.BeimStartVerbindenVoreingestellt,
                    Teiltext.Schalter("Beim Start verbinden"), this, nameof(VerbindenBeimStart)),
            ]));
        // Seit 0.11.9 (Sarevoc 11.10. und 15.10.2026): offen stehen der Schalter und unter "Wo im Bild" die Regler Waagerecht und Senkrecht. Der Schalter
        // zeigt seinen Stand selbst und zählt nicht; die Lage zählt mit, weil "Zurück auf Werkseinstellung" sie auf 0 und 0 setzt (Vorgabe zu den
        // Schiebereglern, Sarevoc 15.10.2026). Unter Erweitert Größe, Durchsichtigkeit, Zeilen, Nachhalten, Ausblenden, eigene Sendung und Monitor,
        // seit 19.10.2026 das Spiel (Q10), das auch Meldungen und Meldezeile nutzen.
        var s = Sprecheranzeige;
        AbschnittSprecheranzeige = new Einstellabschnitt("Sprecheranzeige", "Symbol.Sprecheranzeige", SprecheranzeigeViewModel.AutomatikText,
            new Feinheiten(a, Abschnittskennung.Sprecheranzeige, "Sprecheranzeige", InhaltSprecheranzeigeErweitert,
            [
                new WertTeil<double>(() => s.Waagerecht, v => s.Waagerecht = v, SprecheranzeigeEinstellungen.LageAbWerk, d => "Waagerecht " + SprecheranzeigeViewModel.Prozent(d),
                    s, nameof(SprecheranzeigeViewModel.Waagerecht)),
                new WertTeil<double>(() => s.Senkrecht, v => s.Senkrecht = v, SprecheranzeigeEinstellungen.LageAbWerk, d => "Senkrecht " + SprecheranzeigeViewModel.Prozent(d),
                    s, nameof(SprecheranzeigeViewModel.Senkrecht)),
                new WertTeil<double>(() => s.Groesse, v => s.Groesse = v, SprecheranzeigeEinstellungen.GroesseAbWerk, d => "Größe " + SprecheranzeigeViewModel.Prozent(d),
                    s, nameof(SprecheranzeigeViewModel.Groesse)),
                new WertTeil<double>(() => s.Durchsichtigkeit, v => s.Durchsichtigkeit = v, 0, d => "Durchsichtigkeit " + SprecheranzeigeViewModel.Prozent(d),
                    s, nameof(SprecheranzeigeViewModel.Durchsichtigkeit)),
                new WertTeil<int>(() => s.Zeilen, v => s.Zeilen = v, SprecheranzeigeEinstellungen.ZeilenAbWerk,
                    z => "höchstens " + z.ToString(System.Globalization.CultureInfo.InvariantCulture) + (z == 1 ? " Zeile" : " Zeilen"), s, nameof(SprecheranzeigeViewModel.Zeilen)),
                new WertTeil<double>(() => s.Nachhalten, v => s.Nachhalten = v, SprecheranzeigeEinstellungen.NachhaltenAbWerkMs / 1000.0,
                    d => "Nachhalten " + SprecheranzeigeViewModel.Sekunden((int)Math.Round(d * 1000)), s, nameof(SprecheranzeigeViewModel.Nachhalten)),
                new WertTeil<double>(() => s.Ausblenden, v => s.Ausblenden = v, SprecheranzeigeEinstellungen.AusblendenAbWerkMs / 1000.0,
                    d => "Ausblenden " + SprecheranzeigeViewModel.Sekunden((int)Math.Round(d * 1000)), s, nameof(SprecheranzeigeViewModel.Ausblenden)),
                new WertTeil<bool>(() => s.EigeneSendung, v => s.EigeneSendung = v, SprecheranzeigeEinstellungen.EigeneSendungAbWerk, Teiltext.Schalter("Eigene Sendung"),
                    s, nameof(SprecheranzeigeViewModel.EigeneSendung)),
                new WertTeil<int?>(() => s.Monitor, v => s.Monitor = v, (int?)null, SprecheranzeigeViewModel.MonitorText, s, nameof(SprecheranzeigeViewModel.Monitor)),
                new WertTeil<string>(() => s.SpielWert, v => s.SpielWert = v, "", SprecheranzeigeViewModel.SpielText, s, nameof(SprecheranzeigeViewModel.SpielWert)),
            ]));
        // Seit 0.12.0 (Meldesystem, SPEZ "Client"): offen die Lage (zwei Regler mit Probe, ab Werk oben Mitte); sie zählt mit, weil "Zurück auf
        // Werkseinstellung" sie zurücksetzt. Unter Erweitert Grundgröße, Ausführung, Stopp-Bild und Ton. Kein Schalter zum ganz Abschalten.
        var mv = Meldungen;
        AbschnittMeldungen = new Einstellabschnitt("Meldungen", "Symbol.Meldungen", MeldungenEinstellungViewModel.AutomatikText,
            new Feinheiten(a, Abschnittskennung.Meldungen, "Meldungen", MeldungenEinstellungViewModel.InhaltErweitert,
            [
                new WertTeil<double>(() => mv.Waagerecht, v => mv.Waagerecht = v, MeldungenEinstellungen.WaagerechtAbWerk, d => "Waagerecht " + SprecheranzeigeViewModel.Prozent(d),
                    mv, nameof(MeldungenEinstellungViewModel.Waagerecht)),
                new WertTeil<double>(() => mv.Senkrecht, v => mv.Senkrecht = v, MeldungenEinstellungen.SenkrechtAbWerk, d => "Senkrecht " + SprecheranzeigeViewModel.Prozent(d),
                    mv, nameof(MeldungenEinstellungViewModel.Senkrecht)),
                new WertTeil<double>(() => mv.Grundgroesse, v => mv.Grundgroesse = v, MeldungenEinstellungen.GrundgroesseAbWerk,
                    d => "Grundgröße " + d.ToString("0", System.Globalization.CultureInfo.InvariantCulture), mv, nameof(MeldungenEinstellungViewModel.Grundgroesse)),
                new WertTeil<int>(() => mv.Ausfuehrung, v => mv.Ausfuehrung = v, MeldungenEinstellungen.AusfuehrungSchlicht, MeldungenEinstellungViewModel.AusfuehrungText,
                    mv, nameof(MeldungenEinstellungViewModel.Ausfuehrung)),
                new WertTeil<int>(() => mv.StoppBild, v => mv.StoppBild = v, MeldungenEinstellungen.StoppHandschuh, MeldungenEinstellungViewModel.StoppText,
                    mv, nameof(MeldungenEinstellungViewModel.StoppBild)),
                new WertTeil<bool>(() => mv.Ton, v => mv.Ton = v, MeldungenEinstellungen.TonAbWerk, Teiltext.Schalter("Ton"), mv, nameof(MeldungenEinstellungViewModel.Ton)),
            ]));
        AbschnittFehlersuche = new Einstellabschnitt("Fehlersuche", "Symbol.Hinweis", null, new Feinheiten(a, Abschnittskennung.Fehlersuche, "Fehlersuche",
            "Klangprotokoll, Ordner der Einstellungen, Klangtreiber, Stimmproben",
            [
                new WertTeil<bool>(() => laut.Klangprotokoll, v => laut.Klangprotokoll = v, false, Teiltext.Schalter("Klangprotokoll"), laut,
                    nameof(LautstaerkeViewModel.Klangprotokoll)),
            ]), SatzFehlersuche);
        Abschnitte = [AbschnittIch, AbschnittTasten, AbschnittEmpfangen, AbschnittSenden, AbschnittFunkfeld, AbschnittSprecheranzeige, AbschnittMeldungen,
            AbschnittFehlersuche];
        // Geht der Klappbereich auf, stehen die Monitore in der Liste, wie Windows sie gerade zählt (ein Monitor kann dazugekommen sein).
        AbschnittFunkfeld.Feinheiten.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Feinheiten.Offen) && AbschnittFunkfeld.Feinheiten.Offen) platz.MonitoreLesen();
        };
        var sprecher = AbschnittSprecheranzeige;
        sprecher.Feinheiten.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Feinheiten.Offen) && sprecher.Feinheiten.Offen) s.MonitoreLesen();
        };
        // Exklusives Vollbild: der Hinweis im Kasten des Abschnitts statt "Läuft von selbst" (keine Meldezeile, Sarevoc 13.10.2026: kein Spam).
        void VollbildZeigen() => sprecher.Hinweis = s.Vollbild ? SprecheranzeigeViewModel.VollbildText : null;
        VollbildZeigen();
        s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SprecheranzeigeViewModel.Vollbild) or "" or null) VollbildZeigen();
        };
    }

    /// <summary>Was unter Sprecheranzeige > Erweitert steht (seit 0.11.9).</summary>
    public const string InhaltSprecheranzeigeErweitert = "Größe, Durchsichtigkeit, Zeilen, Nachhalten, Ausblenden, eigene Sendung, Monitor, Spiel";

    /// <summary>Die Sprecheranzeige über dem Spiel (seit 0.11.9): Schalter und Raster offen, der Rest unter Erweitert.</summary>
    public SprecheranzeigeViewModel Sprecheranzeige { get; }

    /// <summary>Die Meldungen der Leitung über dem Spiel (seit 0.12.0): Lage offen, der Rest unter Erweitert.</summary>
    public MeldungenEinstellungViewModel Meldungen { get; }

    /// <summary>Was unter Funkfeld und Darstellung > Erweitert steht (seit 0.11.1 mit der Meldezeile).</summary>
    public const string InhaltFunkfeldErweitert = "Durchsichtigkeit, Skalierung, Funkfeld beim Start, Meldezeile, Tooltips, Verbinden beim Start";

    /// <summary>Der Platz der Meldezeile (seit 0.11.1): Raster, Monitor, Probe; unter Funkfeld und Darstellung > Erweitert.</summary>
    public MeldezeilePlatzViewModel Meldezeile { get; }

    /// <summary>"Beim Start verbinden" wurde von außen gesetzt (nach der Freigabe einmal von selbst, E12): Schalter und Erweitert ziehen nach.</summary>
    internal void VerbindenBeimStartGeaendert() => OnPropertyChanged(nameof(VerbindenBeimStart));

    private static string Db(double db) => db.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " dB";

    private static string Prozent(double p) => p.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " %";

    // ---- Funkfeld und Darstellung ----

    /// <summary>Durchsichtigkeit des Funkfelds für den Regler (0 bis 85 %), wirkt sofort.</summary>
    public double Durchsichtigkeit
    {
        get => _funkfeld.Durchsichtigkeit;
        set => _funkfeld.Durchsichtigkeit = double.IsFinite(value) ? (int)Math.Round(value) : 0;
    }

    public string DurchsichtigkeitText => _funkfeld.Durchsichtigkeit.ToString(System.Globalization.CultureInfo.CurrentCulture) + " %";

    /// <summary>Was der Regler gerade bewirkt, in einem Satz unter dem Regler.</summary>
    public string DurchsichtWirkung
    {
        get
        {
            var p = _funkfeld.Durchsichtigkeit;
            if (p == 0) return "Deckend.";
            if (!_funkfeld.SchriftDeckend) return $"Das ganze Funkfeld ist {p} % durchsichtig, Schrift und Zustandsfarben mit.";
            var d = _funkfeld.Deckung;
            return $"Lücken und Titelzeile {FunkfeldDeckung.Prozent(d.Luecken)} % durchsichtig, Kacheln {FunkfeldDeckung.Prozent(d.Kachel)} %, "
                + $"Global {FunkfeldDeckung.Prozent(d.Global)} %: So bleibt jede Schrift auch über hellem Spielbild lesbar.";
        }
    }

    /// <summary>
    /// Option "Schrift und Symbole deckend halten": nur Lücken und Titelzeile durchsichtig, Kacheln und Schrift lesbar.
    /// Ab Werk aus; dann wird das ganze Funkfeld durchsichtig (Sarevoc, 30.09.2026).
    /// </summary>
    public bool SchriftDeckend
    {
        get => _funkfeld.SchriftDeckend;
        set => _funkfeld.SchriftDeckend = value;
    }

    /// <summary>Erklärung neben der Option.</summary>
    public static string SchriftDeckendHinweis { get; } =
        "Dann werden nur Lücken und Titelzeile durchsichtig, Kacheln, Schrift und Zustandsfarben bleiben so deckend, dass sie auch über hellem Spielbild lesbar sind. "
        + "Ohne die Option wird das ganze Funkfeld gleichmäßig durchsichtig.";

    /// <summary>"Größe nach Inhalt": gezogene Größen des Funkfelds vergessen, es nimmt wieder die optimale Größe.</summary>
    [RelayCommand]
    private void GroesseNachInhalt() => _funkfeld.GroesseNachInhalt();

    /// <summary>
    /// Skalierung des Funkfelds für den Regler (Sarevoc Q12), -30 bis +30 % in Schritten zu 5; wirkt sofort. Das Funkfeld behält je
    /// Ansicht seine feste Mindestgröße, nur eben skaliert.
    /// </summary>
    public double Skalierung
    {
        get => _funkfeld.Skalierung;
        set
        {
            if (double.IsFinite(value)) _funkfeld.Skalierung = (int)Math.Round(value);
        }
    }

    /// <summary>Wert neben dem Regler, etwa "+10 %".</summary>
    public string SkalierungText => _funkfeld.Skalierung.ToString("+0;-0;0", System.Globalization.CultureInfo.CurrentCulture) + " %";

    /// <summary>Globale Tasten (A3, A5), seit 0.10.0 unter Tasten > Erweitert; die Sprechtaste auch auf Start.</summary>
    public TastenViewModel Tasten { get; }

    public bool FunkfeldBeimStart
    {
        get => _ablage.Aktuell.Funkfeld.BeimStartZeigen;
        set => Setzen(() => _ablage.Aktuell.Funkfeld.BeimStartZeigen = value);
    }

    public bool AudioBeimStart
    {
        get => _ablage.Aktuell.Audio.BeimStartOeffnen;
        set => Setzen(() => _ablage.Aktuell.Audio.BeimStartOeffnen = value);
    }

    /// <summary>"Beim Start verbinden"; nach der Freigabe ab Werk an (seit 0.10.0, E12).</summary>
    public bool VerbindenBeimStart
    {
        get => _ablage.Aktuell.Verbindung.BeimStartVerbinden;
        set => Setzen(() => _ablage.Aktuell.Verbindung.BeimStartVerbinden = value);
    }

    /// <summary>Hovertext an "Beim Start verbinden".</summary>
    public const string VerbindenBeimStartHilfe = "Beim Start verbinden: Verbindet gleich beim Start, ohne Klick auf Verbinden. Ist dein Gerät freigegeben, geht "
        + "das ab Werk einmal von selbst an; schaltest du es hier aus, bleibt es aus. Ohne Freigabe verbindet Start erst, wenn du auf Verbinden klickst.";

    /// <summary>Hilfetexte beim Überfahren der Knöpfe (Sarevoc Q9): der globale Schalter, wirkt sofort (<see cref="Glasdesign.Hilfe.Zeigen"/>).</summary>
    public bool Hilfetexte
    {
        get => _ablage.Aktuell.Anzeige.Hilfetexte;
        set => Setzen(() =>
        {
            _ablage.Aktuell.Anzeige.Hilfetexte = value;
            Glasdesign.Hilfe.Zeigen = value;
        });
    }

    // ---- Fehlersuche ----

    /// <summary>
    /// Wo die Einstellungen liegen, ohne den Pfad zu nennen: Er trägt den Windows-Benutzernamen und gehört nicht auf den Bildschirm
    /// (N1 aus der Gegenprüfung Stufe 2). "Im Benutzerordner unter JG-TacRelay" im Regelfall; ein eigener Ort (Umgebungsvariable)
    /// heißt "An einem eigenen Ort". Den Ordner öffnet <see cref="OrdnerOeffnenCommand"/>.
    /// </summary>
    public string OrtText => _ablage.Pfad.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JG-TacRelay") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        ? "Im Benutzerordner unter JG-TacRelay"
        : "An einem eigenen Ort";

    /// <summary>Öffnet den Ordner der Einstellungen im Explorer (Knopf "Ordner öffnen"); ohne Fensterdienst gibt es nichts zu öffnen.</summary>
    [RelayCommand(CanExecute = nameof(KannOrdnerOeffnen))]
    private void OrdnerOeffnen()
    {
        if (_fenster is null) return;
        _fenster.OrdnerOeffnen(Path.GetDirectoryName(_ablage.Pfad)!);
    }

    private bool KannOrdnerOeffnen() => _fenster is not null;

    /// <summary>
    /// Die Details der Verbindung (Host, Port, Profil, Konto, Gerät, Adresse) als eigene Karte unter den Abschnitten (E21); nur im Admin-Setup, sonst
    /// <c>null</c>. Bis Schritt 3.5 standen sie aufklappbar auf der Startseite, die dadurch im ungünstigsten Fall rollte (N2, Gegenprüfung Stufe 2).
    /// </summary>
    [ObservableProperty]
    public partial VerbindungViewModel? Details { get; set; }

    public string? Fehler => _ablage.Fehler;

    private void Setzen(Action aenderung)
    {
        aenderung();
        _ablage.Vormerken();
        OnPropertyChanged(string.Empty);
    }
}
