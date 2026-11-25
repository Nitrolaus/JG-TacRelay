// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Ablage;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>
/// Das Feld "Meldungen" auf der Funkfeld-Zuteilung (Strg+5) für die Leitung (Meldesystem 0.12.0, Sarevoc 11.10. und 16.10.2026, M1-M23, tmp\melde\SPEZ.md
/// "Client", Bild C vom 16.10.2026 ohne die Startfolge der Ampel, M22). Nur da, wenn du senden darfst (<see cref="Gilt"/>: Haken vom Admin und Führung,
/// Admin oder Einsatzleitung mit laufendem Auftrag, nicht als Funker; Server ab 0.12.0). Ziel immer: alle, die online sind.
/// <list type="bullet">
/// <item>Größe normal, doppelt, dreifach (M21) für Befehle, Ampel, Countdown und Bereit-Abfrage; Zustände bleiben klein. Die Wahl bleibt stehen.</item>
/// <item>Befehle (8 s groß), Zustände (bis aufgehoben) mit "aufheben", Ampel rot, gelb, grün und aus, nur von Hand; ein Klick auf eine Kachel sendet.</item>
/// <item>Countdown: Form "Start um" oder "Restzeit" wählen, Uhrzeit (21:30, die nächste passende) oder Dauer (5 min, 90 s) eingeben, Start; abbrechen.</item>
/// <item>Bereit-Abfrage: starten, "n von m bereit" und wer fehlt (wer fragt, zählt nicht mit), schließen; nach 2 min endet sie von selbst.</item>
/// <item>Eine Zeile bis 60 Zeichen unter der Grafik, hart begrenzt (mehr wäre beim Server ZU_GROSS und trennte die Verbindung).</item>
/// <item>Text (M24): eine Textmeldung ohne Grafik bis 120 Zeichen, hart begrenzt, in einer von sechs festen Farben, "8 s" oder "bleibt stehen", in der
/// gewählten Größe; Senden und aufheben.</item>
/// </list>
/// Höchstens eine Meldung in 2 s: Die Sperre zählt ab der Antwort auf die vorige Meldung (dann ist die nächste nie zu früh), solange sind die Kacheln
/// gesperrt und <see cref="Wartetext"/> sagt, wie lange noch. Ein GRENZE vom Server zählt dort als Verstoß; käme es doch, steht der Satz unter
/// <see cref="Fehler"/>, und die Sperre beginnt neu. Die Bilder der Kacheln kommen in deiner eigenen Ausführung und Stopp-Wahl.
/// </summary>
public sealed partial class MeldungenViewModel : ObservableObject
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Kurze Namen unter den Kacheln (Bild C vom 16.10.2026); der volle Name steht im Hovertext und für Bildschirmleser.</summary>
    public static IReadOnlyDictionary<string, string> Kurznamen { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sammeln"] = "Sammeln",
        ["sammelpunkt_x"] = "Sammelpunkt",
        ["stopp"] = "Stopp",
        ["rueckzug"] = "Rückzug",
        ["start_freigegeben"] = "Start frei",
        ["medizinische_hilfe"] = "Med. Hilfe",
        ["los"] = "Los",
        ["stopfen"] = "Stopfen",
        ["einsatz_noch_nicht_gestartet"] = "Einsatz n. gest.",
        ["event_noch_nicht_gestartet"] = "Event n. gest.",
        ["position_halten"] = "Pos. halten",
        ["funkdisziplin"] = "Funkdisziplin",
        ["pause"] = "Pause",
        ["ampel_rot_stopp"] = "rot",
        ["ampel_gelb_bereithalten"] = "gelb",
        ["ampel_gruen_los"] = "grün",
        ["countdown_startzeit"] = "Start um",
        ["countdown_restzeit"] = "Restzeit",
        ["bereit_melden"] = "Bereit melden",
    };

    /// <summary>Unter dem Feld (Bild C).</summary>
    public const string FussSatz = "Jeder Funker wählt selbst Schlicht, Metall oder Nur Schrift und Handschuh oder Xeno beim Stopp; den Ton bei Befehlen, Texten "
        + "und in den letzten 10 s des Countdowns kann er abschalten.";

    /// <summary>Hovertext an den Knöpfen der Größe.</summary>
    public const string GroesseHilfe = "Größe beim Senden: normal, doppelt oder dreifach so groß wie die Grundgröße, die jeder Funker selbst eingestellt hat; nie größer "
        + "als sein Monitor. Gilt für Befehle, Ampel, Countdown und Bereit-Abfrage; Zustände bleiben klein. Die Wahl bleibt stehen, bis du sie änderst.";

    /// <summary>Hovertext an "aufheben" der Zustände.</summary>
    public const string AufhebenHilfe = "Hebt den laufenden Zustand bei allen auf. Ein neuer Zustand ersetzt den alten auch ohne Aufheben.";

    /// <summary>Hovertext an "aus" der Ampel.</summary>
    public const string AmpelAusHilfe = "Schaltet die Ampel bei allen aus. Rot, gelb und grün schaltest du nur von Hand, sie läuft nicht von selbst durch.";

    /// <summary>Hovertext am Eingabefeld des Countdowns.</summary>
    public const string CountdownEingabeHilfe = "Wann: eine Uhrzeit wie 21:30 (die nächste, die kommt, auch nach Mitternacht) oder eine Dauer wie 5 min, 90 s, 1 h 30 min; "
        + "eine Zahl allein sind Minuten. Höchstens 24 Stunden voraus. Die Funker sehen die Zeit in ihrer Ortszeit, gerechnet mit der Uhr des Servers.";

    /// <summary>Hovertext an "Start" des Countdowns.</summary>
    public const string CountdownStartHilfe = "Startet den Countdown bei allen, die online sind, in der gewählten Form (Start um oder Restzeit). Ein neuer ersetzt den laufenden; "
        + "in den letzten 10 Sekunden hören die Funker je Sekunde einen kurzen Ton, bei 0 verschwindet er.";

    /// <summary>Hovertext an "abbrechen" des Countdowns.</summary>
    public const string CountdownAbbrechenHilfe = "Bricht den laufenden Countdown bei allen ab.";

    /// <summary>Hovertext an "schließen" der Bereit-Abfrage.</summary>
    public const string BereitSchliessenHilfe = "Schließt die Bereit-Abfrage bei allen; sonst endet sie nach 2 Minuten von selbst. Das Ergebnis bleibt hier stehen.";

    /// <summary>Hovertext am Feld der Zeile.</summary>
    public const string ZeileHilfe = "Zeile dazu (freiwillig): steht bei den Funkern unter der Grafik, weiß mit Schatten, höchstens 60 Zeichen. Sie geht mit der nächsten "
        + "Meldung hinaus und bleibt hier stehen, bis du sie änderst.";

    /// <summary>Der Satz unter dem Eingabefeld des Countdowns, solange nichts eingegeben ist.</summary>
    public const string CountdownBeispiel = "Uhrzeit oder Dauer, z. B. 21:30 oder 5 min";

    /// <summary>Hovertext am Eingabefeld der Textmeldung.</summary>
    public const string TextHilfe = "Text an alle, die online sind, nur Text ohne Grafik: steht bei den Funkern unter dem Befehlsplatz in der gewählten Farbe und Größe, "
        + "höchstens 120 Zeichen, in höchstens zwei Zeilen. Er bleibt hier stehen, bis du ihn änderst.";

    /// <summary>Hovertext an "8 s" und "bleibt stehen".</summary>
    public const string TextDauerHilfe = "8 s: Der Text steht 8 Sekunden. Bleibt stehen: bis du ihn aufhebst oder einen neuen Text sendest; wer später einsteigt, "
        + "sieht ihn auch.";

    /// <summary>Hovertext an "Senden" der Textmeldung.</summary>
    public const string TextSendenHilfe = "Sendet den Text an alle, die online sind, in Farbe, Größe und Dauer, mit dem kurzen Ton wie bei Befehlen. Ein neuer Text ersetzt "
        + "den laufenden.";

    /// <summary>Hovertext an "aufheben" der Textmeldung.</summary>
    public const string TextAufhebenHilfe = "Hebt die laufende Textmeldung bei allen auf.";

    /// <summary>Unter "schließen" der Bereit-Abfrage.</summary>
    public const string BereitEndeSatz = "endet spätestens nach 2 min";

    private readonly IFunkDienst _dienst;
    private readonly Func<MeldungenEinstellungen> _einstellungen;
    private readonly Func<long> _uhr;
    private long _sperreBis = long.MinValue;
    private string _bilderStand = "";

    /// <param name="einstellungen">Deine Einstellungen der Meldungen (Ausführung und Stopp-Bild der Kacheln).</param>
    /// <param name="uhr">Millisekunden für die Sperre von 2 s (im Betrieb <c>Environment.TickCount64</c>).</param>
    public MeldungenViewModel(IFunkDienst dienst, Func<MeldungenEinstellungen> einstellungen, Func<long> uhr)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(einstellungen);
        ArgumentNullException.ThrowIfNull(uhr);
        _dienst = dienst;
        _einstellungen = einstellungen;
        _uhr = uhr;
        Meldekachel K(Meldungsmotiv m) => new(m, Kurznamen.GetValueOrDefault(m.Kennung, m.Name), this);
        Befehle = [.. Meldungskatalog.VonArt(Meldungsart.Befehl).Select(K)];
        Zustaende = [.. Meldungskatalog.VonArt(Meldungsart.Zustand).Select(K)];
        Ampeln = [.. Meldungskatalog.VonArt(Meldungsart.Ampel).Select(K)];
        Countdowns = [.. Meldungskatalog.VonArt(Meldungsart.Countdown).Select(K)];
        Bereit = K(Meldungskatalog.Motiv(Meldungskatalog.BereitMotiv)!);
        Countdowns[0].Gewaehlt = true;
        Farbwahl = [.. Textfarben.Alle.Select(f => new Textfarbwahl(f, this))];
        Aktualisieren();
    }

    /// <summary>Ob es das Feld für diese Lage gibt: verbunden, Server ab 0.12.0, und du darfst senden (<see cref="KontoInfo.DarfMelden"/>).</summary>
    public static bool Gilt(FunkLage l) => l is { IstAktiv: true, ServerMeldungen: true, Konto.DarfMelden: true };

    [ObservableProperty]
    public partial bool Sichtbar { get; private set; }

    public IReadOnlyList<Meldekachel> Befehle { get; }

    public IReadOnlyList<Meldekachel> Zustaende { get; }

    public IReadOnlyList<Meldekachel> Ampeln { get; }

    /// <summary>Die zwei Formen des Countdowns als Kacheln zur Wahl (nicht zum Senden): Start um und Restzeit.</summary>
    public IReadOnlyList<Meldekachel> Countdowns { get; }

    public Meldekachel Bereit { get; }

    /// <summary>"an alle, die online sind (22)  -  ein Klick sendet  -  du bist ernannt".</summary>
    [ObservableProperty]
    public partial string KopfText { get; private set; } = "";

    // ---- Größe (M21) ----

    /// <summary>1 normal, 2 doppelt, 3 dreifach; bleibt stehen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroesseNormal), nameof(GroesseDoppelt), nameof(GroesseDreifach))]
    public partial byte Groesse { get; set; } = 1;

    partial void OnGroesseChanged(byte value)
    {
        if (value is < 1 or > Grenzen.MeldungGroesseMax) Groesse = 1;
    }

    public bool GroesseNormal
    {
        get => Groesse == 1;
        set
        {
            if (value) Groesse = 1;
        }
    }

    public bool GroesseDoppelt
    {
        get => Groesse == 2;
        set
        {
            if (value) Groesse = 2;
        }
    }

    public bool GroesseDreifach
    {
        get => Groesse == 3;
        set
        {
            if (value) Groesse = 3;
        }
    }

    // ---- Zeile ----

    /// <summary>Die Zeile unter der Grafik, hart auf <see cref="Grenzen.MeldungZeileMax"/> Zeichen begrenzt.</summary>
    public string Zeile
    {
        get;
        set
        {
            var neu = value ?? "";
            if (neu.Length > Grenzen.MeldungZeileMax) neu = neu[..Grenzen.MeldungZeileMax];
            if (string.Equals(neu, field, StringComparison.Ordinal)) return;
            field = neu;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ZeileRest));
        }
    } = "";

    /// <summary>Höchstzahl der Zeichen für das Eingabefeld (MaxLength).</summary>
    public static int ZeileMax => Grenzen.MeldungZeileMax;

    /// <summary>"noch 52 Zeichen".</summary>
    public string ZeileRest => "noch " + (Grenzen.MeldungZeileMax - Zeile.Length).ToString(CultureInfo.InvariantCulture) + " Zeichen";

    // ---- Text (M24) ----

    /// <summary>
    /// Der Text der Textmeldung, hart auf <see cref="Grenzen.MeldungTextMax"/> Zeichen begrenzt (mehr wäre beim Server ZU_GROSS und trennte die
    /// Verbindung); ein Zeichenpaar (Emoji) wird dabei nicht zerschnitten.
    /// </summary>
    public string TextEingabe
    {
        get;
        set
        {
            var neu = value ?? "";
            if (neu.Length > Grenzen.MeldungTextMax)
                neu = neu[..(char.IsHighSurrogate(neu[Grenzen.MeldungTextMax - 1]) ? Grenzen.MeldungTextMax - 1 : Grenzen.MeldungTextMax)];
            if (string.Equals(neu, field, StringComparison.Ordinal)) return;
            field = neu;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TextRest));
            TextSendenCommand.NotifyCanExecuteChanged();
        }
    } = "";

    /// <summary>Höchstzahl der Zeichen für das Eingabefeld des Texts (MaxLength).</summary>
    public static int TextMax => Grenzen.MeldungTextMax;

    /// <summary>"noch 120 Zeichen".</summary>
    public string TextRest => "noch " + (Grenzen.MeldungTextMax - TextEingabe.Length).ToString(CultureInfo.InvariantCulture) + " Zeichen";

    /// <summary>Die sechs festen Farben zur Wahl, Weiß ab Werk.</summary>
    public IReadOnlyList<Textfarbwahl> Farbwahl { get; }

    /// <summary>Die gewählte Farbe (Kennung aus <see cref="Textmeldungen.Farben"/>); bleibt stehen.</summary>
    public string TextFarbe
    {
        get;
        set
        {
            var neu = Textmeldungen.IstFarbe(value) ? value : Textmeldungen.Weiss;
            if (string.Equals(neu, field, StringComparison.Ordinal)) return;
            field = neu;
            OnPropertyChanged();
            foreach (var f in Farbwahl) f.Abgleichen();
        }
    } = Textmeldungen.Weiss;

    /// <summary>"8s" (ab Werk) oder "bleibt"; bleibt stehen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextDauerKurz), nameof(TextDauerBleibt))]
    public partial string TextDauer { get; set; } = Textmeldungen.Kurz;

    partial void OnTextDauerChanged(string value)
    {
        if (!Textmeldungen.IstDauer(value)) TextDauer = Textmeldungen.Kurz;
    }

    public bool TextDauerKurz
    {
        get => TextDauer == Textmeldungen.Kurz;
        set
        {
            if (value) TextDauer = Textmeldungen.Kurz;
        }
    }

    public bool TextDauerBleibt
    {
        get => TextDauer == Textmeldungen.Bleibt;
        set
        {
            if (value) TextDauer = Textmeldungen.Bleibt;
        }
    }

    // ---- Countdown ----

    /// <summary>Uhrzeit oder Dauer.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CountdownStartenCommand))]
    public partial string CountdownEingabe { get; set; } = "";

    partial void OnCountdownEingabeChanged(string value) => CountdownSatz = SatzBauen();

    /// <summary>Die gewählte Form: "Startzeit" oder "Restzeit".</summary>
    public string CountdownForm => Countdowns.FirstOrDefault(c => c.Gewaehlt) is { } k ? Meldungskatalog.FormFuerMotiv(k.Kennung) ?? Meldungskatalog.FormStartzeit
        : Meldungskatalog.FormStartzeit;

    /// <summary>
    /// Unter dem Eingabefeld: das Beispiel, was die Eingabe ergibt ("Start um 21:30, in 28 min") oder warum sie nicht passt; neu bei jeder
    /// Eingabe und im Takt, wenn sich der Satz ändert.
    /// </summary>
    [ObservableProperty]
    public partial string CountdownSatz { get; private set; } = CountdownBeispiel;

    private string SatzBauen()
    {
        if (string.IsNullOrWhiteSpace(CountdownEingabe)) return CountdownBeispiel;
        var jetzt = _dienst.ServerJetztMs();
        if (Zielzeit(CountdownEingabe, jetzt, Ortszeit) is not { } ziel) return "Nicht erkannt: Uhrzeit wie 21:30 oder Dauer wie 5 min.";
        return "Start um " + Ortszeit(ziel).ToString("HH:mm", CultureInfo.InvariantCulture) + ", in " + Dauer(ziel - jetzt);
    }

    /// <summary>Unix-ms in Ortszeit; Tests stellen sie fest.</summary>
    internal Func<long, DateTimeOffset> Ortszeit { get; set; } = ms => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();

    /// <summary>Ein Countdown läuft gerade (dann gibt es "abbrechen").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CountdownAbbrechenCommand))]
    public partial bool CountdownLaeuft { get; private set; }

    // ---- Bereit-Abfrage ----

    /// <summary>Die Abfrage ist offen (dann gibt es "schließen").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BereitSchliessenCommand))]
    public partial bool BereitOffen { get; private set; }

    /// <summary>"18 von 22 bereit", nach dem Ende "zu: 18 von 22 bereit"; ohne Abfrage <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? BereitZahl { get; private set; }

    /// <summary>"es fehlen: Kobold, Distel" oder "alle bereit"; ohne Abfrage <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? BereitFehlen { get; private set; }

    // ---- Sperre und Fehler ----

    /// <summary>Eine Meldung ist unterwegs oder die 2 s seit der letzten Antwort sind nicht um: Die Kacheln sind gesperrt.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendenCommand), nameof(ZustandAufhebenCommand), nameof(AmpelAusCommand), nameof(CountdownStartenCommand),
        nameof(CountdownAbbrechenCommand), nameof(BereitSchliessenCommand), nameof(TextSendenCommand), nameof(TextAufhebenCommand))]
    public partial bool Gesperrt { get; private set; }

    /// <summary>"sendet ...", "Nächste Meldung in 1,4 s" oder <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? Wartetext { get; private set; }

    /// <summary>Der Satz, wenn die letzte Meldung scheiterte; sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? Fehler { get; private set; }

    /// <summary>Was zuletzt hinausging, für Tests und Bildschirmleser ("Befehl sammeln, doppelt").</summary>
    [ObservableProperty]
    public partial string? Zuletzt { get; private set; }

    private bool _laeuft;

    /// <summary>Bildtakt: Sperre und Wartetext, der Satz unter der Eingabe des Countdowns (meldet nur, wenn er sich ändert).</summary>
    public void Takt()
    {
        var jetzt = _uhr();
        var gesperrt = _laeuft || jetzt < _sperreBis;
        Gesperrt = gesperrt;
        Wartetext = _laeuft ? "sendet ..."
            : gesperrt ? "Nächste Meldung in " + ((_sperreBis - jetzt) / 1000.0).ToString("0.0", De) + " s"
            : null;
        CountdownSatz = SatzBauen();
    }

    /// <summary>Nach jeder neuen Lage: sichtbar oder nicht, was gerade läuft (Kacheln markiert), "n von m bereit", die Zahl online.</summary>
    public void Aktualisieren()
    {
        var l = _dienst.Lage;
        Sichtbar = Gilt(l);
        var stand = l.Meldestand;
        foreach (var k in Zustaende) k.Laeuft = stand?.Zustand?.Motiv == k.Kennung;
        foreach (var k in Ampeln) k.Laeuft = stand?.Ampel?.Motiv == k.Kennung;
        foreach (var k in Countdowns) k.Laeuft = stand?.Countdown is { Ziel: { } z } c && c.Motiv == k.Kennung && z > _dienst.ServerJetztMs();
        Bereit.Laeuft = stand?.Abfrage is not null;
        CountdownLaeuft = Countdowns.Any(k => k.Laeuft);
        BereitOffen = stand?.Abfrage is not null;
        if (l.BereitStand is { } b)
        {
            var zahl = b.Bereit.ToString(CultureInfo.InvariantCulture) + " von " + b.Online.ToString(CultureInfo.InvariantCulture) + " bereit";
            BereitZahl = b.Offen ? zahl : "zu: " + zahl;
            BereitFehlen = b.Fehlen.Count == 0 ? (b.Online == 0 ? "niemand sonst online" : "alle bereit") : "es fehlen: " + string.Join(", ", b.Fehlen.Select(l.HauptnameVon));
        }
        else
        {
            BereitZahl = null;
            BereitFehlen = null;
        }
        var online = l.Funker.Where(f => f.Online).Select(f => f.KontoId).Append(l.Konto?.Id ?? 0).Where(k => k != 0).Distinct().Count();
        KopfText = "an alle, die online sind (" + online.ToString(CultureInfo.InvariantCulture) + ")  -  ein Klick sendet  -  du bist ernannt";
        BilderLaden();
    }

    /// <summary>Die Bilder der Kacheln in deiner Ausführung und Stopp-Wahl; nur neu, wenn sich die Wahl geändert hat.</summary>
    public void BilderLaden()
    {
        var e = _einstellungen();
        var stand = e.AusfuehrungKennung + "|" + e.StoppBildKennung;
        if (string.Equals(stand, _bilderStand, StringComparison.Ordinal)) return;
        _bilderStand = stand;
        foreach (var k in Befehle.Concat(Zustaende).Concat(Ampeln).Concat(Countdowns).Append(Bereit))
            k.Bild = Meldungsbilder.Kachel(Meldungskatalog.Ressource(k.Kennung, e.AusfuehrungKennung, k.Kennung == Meldungskatalog.Stopp ? e.StoppBildKennung : null))?.Bild;
    }

    // ---- Senden ----

    /// <summary>Ein Klick auf eine Kachel: Befehl, Zustand, Ampel oder Bereit-Abfrage senden; beim Countdown die Form wählen.</summary>
    [RelayCommand(CanExecute = nameof(KannSenden))]
    private async Task Senden(Meldekachel? kachel)
    {
        if (kachel is null) return;
        switch (kachel.Art)
        {
            case Meldungsart.Befehl:
                await SendenAsync(Meldungsvorgang.Befehl, kachel.Kennung, Groesse).ConfigureAwait(true);
                break;
            case Meldungsart.Zustand:
                await SendenAsync(Meldungsvorgang.Zustand, kachel.Kennung, 1).ConfigureAwait(true);
                break;
            case Meldungsart.Ampel:
                await SendenAsync(Meldungsvorgang.Ampel, kachel.Kennung, Groesse).ConfigureAwait(true);
                break;
            case Meldungsart.Bereit:
                await SendenAsync(Meldungsvorgang.BereitAbfrage, kachel.Kennung, Groesse).ConfigureAwait(true);
                break;
            case Meldungsart.Countdown:
                foreach (var k in Countdowns) k.Gewaehlt = ReferenceEquals(k, kachel);
                OnPropertyChanged(nameof(CountdownForm));
                break;
        }
    }

    private bool KannSenden(Meldekachel? kachel) => !Gesperrt || kachel?.Art == Meldungsart.Countdown;

    [RelayCommand(CanExecute = nameof(Frei))]
    private Task ZustandAufheben() => SendenAsync(Meldungsvorgang.ZustandAufheben, null, null);

    [RelayCommand(CanExecute = nameof(Frei))]
    private Task AmpelAus() => SendenAsync(Meldungsvorgang.AmpelAus, null, null);

    [RelayCommand(CanExecute = nameof(KannCountdown))]
    private async Task CountdownStarten()
    {
        var jetzt = _dienst.ServerJetztMs();
        if (Zielzeit(CountdownEingabe, jetzt, Ortszeit) is not { } ziel)
        {
            Fehler = "Countdown: Uhrzeit wie 21:30 oder Dauer wie 5 min eingeben.";
            return;
        }
        await SendenAsync(Meldungsvorgang.Countdown, null, Groesse, ziel, CountdownForm).ConfigureAwait(true);
    }

    private bool KannCountdown() => !Gesperrt && !string.IsNullOrWhiteSpace(CountdownEingabe);

    [RelayCommand(CanExecute = nameof(KannAbbrechen))]
    private Task CountdownAbbrechen() => SendenAsync(Meldungsvorgang.CountdownAbbrechen, null, null);

    private bool KannAbbrechen() => !Gesperrt && CountdownLaeuft;

    [RelayCommand(CanExecute = nameof(KannSchliessen))]
    private Task BereitSchliessen() => SendenAsync(Meldungsvorgang.BereitSchliessen, null, null);

    private bool KannSchliessen() => !Gesperrt && BereitOffen;

    /// <summary>Die Textmeldung hinaus: Text, Farbe, Dauer und die gewählte Größe (M24).</summary>
    [RelayCommand(CanExecute = nameof(KannText))]
    private Task TextSenden() => SendenAsync(Meldungsvorgang.Text, null, Groesse, text: TextEingabe.Trim(), farbe: TextFarbe, dauer: TextDauer);

    private bool KannText() => !Gesperrt && !string.IsNullOrWhiteSpace(TextEingabe);

    [RelayCommand(CanExecute = nameof(Frei))]
    private Task TextAufheben() => SendenAsync(Meldungsvorgang.TextAufheben, null, null);

    private bool Frei() => !Gesperrt;

    /// <summary>
    /// Eine Meldung hinaus, mit der Zeile, wenn eine dasteht. Gesperrt, solange sie unterwegs ist, und 2 s ab der Antwort; ein Fehler steht danach
    /// unter <see cref="Fehler"/>.
    /// </summary>
    private async Task SendenAsync(string art, string? motiv, byte? groesse, long? ziel = null, string? form = null, string? text = null, string? farbe = null,
        string? dauer = null)
    {
        if (Gesperrt || _laeuft) return;
        var zeile = string.IsNullOrWhiteSpace(Zeile) ? null : Zeile.Trim();
        var anfrage = new MeldungSendenAnfrage
        {
            Art = art, Motiv = motiv, Groesse = groesse is 1 ? null : groesse, Zeile = TraegtZeile(art) ? zeile : null, Ziel = ziel, Form = form,
            Text = text, Farbe = farbe, Dauer = dauer,
        };
        _laeuft = true;
        Fehler = null;
        Takt();
        try
        {
            Fehler = await _dienst.MeldungSendenAsync(anfrage).ConfigureAwait(true);
            if (Fehler is null)
                Zuletzt = art + (motiv is null ? "" : " " + motiv) + (farbe is null ? "" : " " + farbe) + (dauer is null ? "" : " " + dauer)
                    + (anfrage.Groesse is 2 ? ", doppelt" : anfrage.Groesse is 3 ? ", dreifach" : "")
                    + (anfrage.Zeile is { } z ? ": " + z : "") + (text is null ? "" : ": " + text);
        }
        finally
        {
            _laeuft = false;
            // Die 2 s zählen ab der Antwort (auch nach einem Fehler: der Server hat sie schon gezählt).
            _sperreBis = _uhr() + Fristen.MeldungAbstandMs;
            Takt();
        }
        Aktualisieren();
    }

    /// <summary>Die Zeile geht mit allem, was eine Grafik zeigt; Aufheben, Aus, Abbrechen, Schließen und die Textmeldung tragen keine.</summary>
    private static bool TraegtZeile(string art) => art is Meldungsvorgang.Befehl or Meldungsvorgang.Zustand or Meldungsvorgang.Ampel or Meldungsvorgang.Countdown
        or Meldungsvorgang.BereitAbfrage;

    /// <summary>
    /// Die Zielzeit in Serverzeit (Unix-ms) aus der Eingabe: eine Uhrzeit "21:30" (auch "21.30", "9:05") ist die nächste, die in Ortszeit kommt,
    /// gerechnet ab der Serverzeit <paramref name="jetzt"/>; eine Dauer "5 min", "90 s", "1 h 30 min", "1,5 min" oder eine Zahl allein (Minuten) zählt
    /// ab <paramref name="jetzt"/>. Mehr als 24 Stunden, nichts oder Unsinn: <c>null</c>.
    /// </summary>
    public static long? Zielzeit(string? eingabe, long jetzt, Func<long, DateTimeOffset> ortszeit)
    {
        ArgumentNullException.ThrowIfNull(ortszeit);
        var t = (eingabe ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return null;
        var uhr = Regex.Match(t, @"^(?<h>\d{1,2})[:.](?<m>\d{2})(\s*uhr)?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (uhr.Success)
        {
            var h = int.Parse(uhr.Groups["h"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(uhr.Groups["m"].Value, CultureInfo.InvariantCulture);
            if (h > 23 || m > 59) return null;
            var nun = ortszeit(jetzt);
            var ziel = new DateTimeOffset(nun.Year, nun.Month, nun.Day, h, m, 0, nun.Offset);
            if (ziel.ToUnixTimeMilliseconds() <= jetzt) ziel = ziel.AddDays(1);
            // Die Zeitzone kann am nächsten Tag anders liegen (Sommerzeit); die Ortszeit rechnet es nach.
            var ms = ziel.ToUnixTimeMilliseconds();
            var korrektur = ortszeit(ms).Offset - ziel.Offset;
            ms -= (long)korrektur.TotalMilliseconds;
            return ms - jetzt is > 0 and <= Grenzen.CountdownVorlaufMaxMs ? ms : null;
        }
        double summe = 0;
        var rest = t;
        var teile = Regex.Matches(t, @"(?<z>\d+(?:[.,]\d+)?)\s*(?<e>stunden|stunde|std|h|minuten|minute|min|m|sekunden|sekunde|sek|s)?",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (teile.Count == 0) return null;
        foreach (Match teil in teile)
        {
            rest = rest.Replace(teil.Value, "", StringComparison.Ordinal);
            var zahl = double.Parse(teil.Groups["z"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            summe += teil.Groups["e"].Value switch
            {
                "stunden" or "stunde" or "std" or "h" => zahl * 3_600_000,
                "sekunden" or "sekunde" or "sek" or "s" => zahl * 1000,
                _ => zahl * 60_000,
            };
        }
        // Was übrig bleibt, darf nur Leerraum und "und" sein.
        if (rest.Replace("und", "", StringComparison.Ordinal).Trim().Length > 0) return null;
        var dauer = (long)Math.Round(summe);
        return dauer is > 0 and <= Grenzen.CountdownVorlaufMaxMs ? jetzt + dauer : null;
    }

    /// <summary>"28 min", "1 h 5 min", "45 s".</summary>
    public static string Dauer(long ms)
    {
        var s = Math.Max(0, (ms + 999) / 1000);
        if (s < 60) return s.ToString(CultureInfo.InvariantCulture) + " s";
        var min = (s + 59) / 60;
        return min < 60 ? min.ToString(CultureInfo.InvariantCulture) + " min"
            : (min / 60).ToString(CultureInfo.InvariantCulture) + " h" + (min % 60 > 0 ? " " + (min % 60).ToString(CultureInfo.InvariantCulture) + " min" : "");
    }
}

/// <summary>Eine Kachel im Feld "Meldungen": Bild, kurzer Name darunter, Hovertext; markiert, wenn sie gerade läuft oder (Countdown) gewählt ist.</summary>
public sealed partial class Meldekachel : ObservableObject
{
    internal Meldekachel(Meldungsmotiv motiv, string kurzname, MeldungenViewModel feld)
    {
        Kennung = motiv.Kennung;
        Art = motiv.Art;
        Name = motiv.Name;
        Kurzname = kurzname;
        Feld = feld;
        Hilfe = motiv.Art switch
        {
            Meldungsart.Befehl => Name + ": Befehl an alle, die online sind. Ein Klick sendet; bei den Funkern steht das Bild 8 Sekunden groß, mit kurzem Ton, "
                + "darunter deine Zeile. Ein neuer Befehl ersetzt den alten.",
            Meldungsart.Zustand => Name + ": Zustand an alle, die online sind. Ein Klick sendet; das Bild steht klein, bis du es aufhebst oder einen anderen Zustand "
                + "sendest. Kein Ton.",
            Meldungsart.Ampel => "Ampel " + Name + ": an alle, die online sind, auf einem eigenen Platz, bis du sie umschaltest oder ausmachst. Nur von Hand.",
            Meldungsart.Countdown => "Countdown \"" + Name + "\": wählt die Form. "
                + (motiv.Kennung == Meldungskatalog.StartzeitMotiv ? "Die Funker sehen die Startzeit und die Zeit bis dahin." : "Die Funker sehen nur die Zeit bis zum Start.")
                + " Gestartet wird mit Start daneben.",
            _ => "Bereit-Abfrage: fragt alle, die online sind, ob sie bereit sind. Sie melden mit ihrer Taste \"Bereit\" oder einem Klick; du siehst \"n von m bereit\" "
                + "und wer fehlt. Nach 2 Minuten endet sie von selbst.",
        };
    }

    /// <summary>Das Feld, zu dem die Kachel gehört (für den Befehl in der Vorlage).</summary>
    public MeldungenViewModel Feld { get; }

    public string Kennung { get; }

    public Meldungsart Art { get; }

    /// <summary>Der volle Name aus dem Katalog ("Sammelpunkt X").</summary>
    public string Name { get; }

    /// <summary>Unter der Kachel ("Sammelpunkt").</summary>
    public string Kurzname { get; }

    public string Hilfe { get; }

    [ObservableProperty]
    public partial ImageSource? Bild { get; internal set; }

    /// <summary>Läuft gerade bei allen (Zustand, Ampel, Countdown, offene Abfrage).</summary>
    [ObservableProperty]
    public partial bool Laeuft { get; internal set; }

    /// <summary>Beim Countdown: die gewählte Form.</summary>
    [ObservableProperty]
    public partial bool Gewaehlt { get; internal set; }
}

/// <summary>Ein Farbknopf der Textmeldung (M24): die Fläche in der Farbe, der Name im Hovertext und für Bildschirmleser; gewählt ist genau einer.</summary>
public sealed class Textfarbwahl : ObservableObject
{
    private readonly MeldungenViewModel _feld;

    internal Textfarbwahl(Textfarbe farbe, MeldungenViewModel feld)
    {
        Farbe = farbe;
        _feld = feld;
    }

    public Textfarbe Farbe { get; }

    public string Name => Farbe.Name;

    /// <summary>Für Bildschirmleser: "Textfarbe Gelb".</summary>
    public string Ansage => "Textfarbe " + Farbe.Name;

    public Brush Pinsel => Farbe.Pinsel;

    /// <summary>"Textfarbe Gelb: ..." für den Hovertext.</summary>
    public string Hilfe => "Textfarbe " + Farbe.Name + ": So steht der Text bei den Funkern über dem Spiel, mit Schatten, ohne Fläche.";

    /// <summary>Gewählt; das Setzen wählt diese Farbe im Feld.</summary>
    public bool Gewaehlt
    {
        get => string.Equals(_feld.TextFarbe, Farbe.Kennung, StringComparison.Ordinal);
        set
        {
            if (value) _feld.TextFarbe = Farbe.Kennung;
        }
    }

    internal void Abgleichen() => OnPropertyChanged(nameof(Gewaehlt));
}
