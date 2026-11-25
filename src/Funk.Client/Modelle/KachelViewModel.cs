// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>Zustand einer Kachel im Funkfeld (A17, A18; seit 0.6.0 mit Ruhe und Sperre).</summary>
public enum KachelZustand
{
    /// <summary>Kein Kanal in diesem Feld: kein Punkt, ausgegraut.</summary>
    Leer,

    /// <summary>Echter Aus-Schalter: nicht hören, nicht senden (A17); grauer Ring.</summary>
    Aus,

    /// <summary>Hören (gelb/orange, "HÖRT").</summary>
    Hoeren,

    /// <summary>In der Sendeauswahl (grün, dicker Rahmen, "SENDET").</summary>
    Senden,

    /// <summary>Kanalnetz auf Ch. 0: hört und sendet nichts (6.16); kein Punkt, ausgegraut wie ein leeres Feld.</summary>
    Ruhe,

    /// <summary>Kanal von der Einsatzleitung oder einem Admin gesperrt (6.17): rot, "GESPERRT". Rot heißt nur das.</summary>
    Gesperrt,
}

/// <summary>
/// Eine Kachel des Funkfelds: ein Feld mit seinem Kanal oder das Sonderfeld Global (A23). Zeigt den Zustand mit Statuspunkt,
/// Rand und Wort, den gerade Sprechenden, einen Pegel, die eigene Lautstärke und VA/Taste. Keine Frequenzen, kein Enc, kein ST
/// (A19). Seit 0.6.0 (Stufe 3, Schritt 3.4): Der Ch.-Wähler zeigt die Bezeichnung aus der Ch.-Liste ("Schiff 3", ab Werk
/// "Ruhe") und wechselt den Ch.; Lautstärke, Ohr und VA/Taste gelten für alle Ch. eines Kanalnetzes (Sarevoc Q15). Seit 0.8.1 (Q44)
/// ist der Name eine feste Beschriftung: Welcher Kanal in einem Feld liegt, bestimmen die Rolle, der Admin und die
/// Einsatzleitung; der Funker wählt nur den Ch., auch in einem festen Feld (Q45). Der Tooltip der Kachel sagt, woher der Kanal kommt.
/// </summary>
public sealed partial class KachelViewModel : ObservableObject
{
    /// <summary>Woher der Kanal eines gewöhnlichen Felds kommt (Q44), im Tooltip der Kachel nach dem Namen.</summary>
    public const string HerkunftRolle = "Kommt aus deiner Rolle. Ändern kann ihn nur der Admin.";

    /// <summary>Dasselbe ohne gewählte Rolle (STANDARD, 6.9): Die Kanäle legt der Server fest, bis du eine Rolle wählst.</summary>
    public const string HerkunftOhneRolle = "Kommt vom Server, solange du keine Rolle hast. Ändern kann ihn nur der Admin.";

    /// <summary>Woher der Kanal eines festen Felds kommt: von der Einsatzleitung, einer Aufstellung oder fest aus der Rolle.</summary>
    public const string HerkunftFest = "Fest eingetragen. Ändern können ihn nur die Einsatzleitung und der Admin.";

    /// <summary>Global rechts unten (A23).</summary>
    public const string HerkunftGlobal = "Fest rechts unten, alle hören es. Senden nur mit Taste und mit dem Recht dazu.";

    private readonly FunkfeldViewModel _feld;

    /// <summary>Während die Kachel aus der Lage gefüllt wird, sind Wahlen aus der Oberfläche keine Bedienung.</summary>
    private bool _abgleich;

    internal KachelViewModel(FunkfeldViewModel feld, int platz, bool istGlobal)
    {
        _feld = feld;
        Platz = platz;
        IstGlobal = istGlobal;
        Name = istGlobal ? "Global" : "frei";
    }

    /// <summary>Position im Funkfeld, 0-basiert; bei normalen Kacheln zugleich die Feldnummer.</summary>
    public int Platz { get; }

    public bool IstGlobal { get; }

    /// <summary>Taste zum Wählen im Funkfeld: 1-9, Global 0.</summary>
    public string Taste => IstGlobal ? "0" : (Platz + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Netz oder Kanal-ID dieses Felds (bei Global Netz 1); <c>null</c> ohne Kanal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatNetz))]
    public partial ushort? NetzId { get; private set; }

    /// <summary>In diesem Feld steht ein Kanal; sonst hat der Lautstärkeregler nichts zu regeln.</summary>
    public bool HatNetz => NetzId is not null;

    /// <summary>Das Kanalnetz, wenn das Feld einen Kanal mit Ch. trägt (6.16); sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial ushort? Kanalnetz { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Beschreibung), nameof(KanalText), nameof(KopfHilfe))]
    public partial string Name { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZustandText), nameof(IstSenden), nameof(IstHoeren), nameof(IstAus), nameof(IstLeer), nameof(IstRuhe), nameof(IstGesperrt),
        nameof(AusSchalterText), nameof(Beschreibung), nameof(HatPunkt), nameof(KannSprachaktivierung), nameof(RoterPunkt))]
    public partial KachelZustand Zustand { get; private set; }

    public bool IstSenden => Zustand == KachelZustand.Senden;

    public bool IstHoeren => Zustand == KachelZustand.Hoeren;

    public bool IstAus => Zustand == KachelZustand.Aus;

    public bool IstLeer => Zustand == KachelZustand.Leer;

    public bool IstRuhe => Zustand == KachelZustand.Ruhe;

    public bool IstGesperrt => Zustand == KachelZustand.Gesperrt;

    /// <summary>Das Wort zur Farbe (Farbe nie allein): SENDET, HÖRT, GESPERRT, AUS, RUHE, FREI.</summary>
    public string ZustandText => Text(Zustand);

    /// <summary>Der Statuspunkt steht da: grün, gelb, rot oder als grauer Ring; ohne Kanal und auf Ruhe keiner.</summary>
    public bool HatPunkt => Zustand is KachelZustand.Senden or KachelZustand.Hoeren or KachelZustand.Gesperrt or KachelZustand.Aus;

    /// <summary>Tooltip am Statuspunkt, mit dem Wort zur Farbe.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KopfHilfe))]
    public partial string PunktHilfe { get; private set; } = "Kein Kanal in diesem Feld";

    /// <summary>
    /// Tooltip der Kachel (Kachelkopf, auch über dem Namen, der keine eigene Klickfläche hat): seit 0.8.1 zuerst der volle Name mit Ch. und
    /// woher der Kanal kommt (<see cref="Herkunft"/>), dann der Zustand mit seinem Wort und was ein Klick tut.
    /// </summary>
    public string KopfHilfe => (Herkunft is { } h ? KanalText + ": " + h + " " : "") + PunktHilfe + ". Klick: als Sendekanal wählen (Strg+Klick oder Mehrere senden: dazunehmen).";

    /// <summary>
    /// Woher der Kanal kommt (<see cref="HerkunftRolle"/>, <see cref="HerkunftOhneRolle"/>, <see cref="HerkunftFest"/>, <see cref="HerkunftGlobal"/>);
    /// ohne Kanal <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KopfHilfe))]
    public partial string? Herkunft { get; private set; }

    /// <summary>Hören ist an (auch in der Sendeauswahl).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AusSchalterText))]
    public partial bool Hoeren { get; private set; }

    public string AusSchalterText => Hoeren ? "Aus" : "An";

    /// <summary>Senden in diesem Kanal erlaubt (nicht auf Ruhe, nicht gesperrt).</summary>
    [ObservableProperty]
    public partial bool KannSenden { get; private set; }

    /// <summary>Hören abschaltbar (nicht bei Global, nicht bei leerem Feld, nicht auf Ruhe).</summary>
    [ObservableProperty]
    public partial bool KannAusschalten { get; private set; }

    /// <summary>
    /// Ein festes Feld (Einsatzleitung, Aufstellung oder fest aus der Rolle). Seit 0.8.1 sperrt das den Ch. nicht mehr (Q45); es zeigt sich
    /// nur noch im Tooltip (<see cref="HerkunftFest"/>).
    /// </summary>
    [ObservableProperty]
    public partial bool Fest { get; private set; }

    /// <summary>Die Sperre dieses Felds als Satz ("Gesperrt von Nordlicht bis 04:00"), sonst <c>null</c>.</summary>
    [ObservableProperty]
    public partial string? GesperrtText { get; private set; }

    /// <summary>Name des gerade Sprechenden, mehrere mit Komma; leer, wenn niemand spricht.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SprecherAktiv), nameof(Beschreibung))]
    public partial string Sprecher { get; private set; } = "";

    public bool SprecherAktiv => Sprecher.Length > 0;

    /// <summary>
    /// Das Rang-Zeichen des ersten Sprechers (A49), nur wenn "Zeichen im Funkfeld" an ist (ab Werk aus, damit das Feld kompakt
    /// bleibt); sonst <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatSprecherZeichen))]
    public partial Design.Zeichenbild? SprecherZeichen { get; private set; }

    public bool HatSprecherZeichen => SprecherZeichen is not null;

    /// <summary>
    /// Das gelbe "!" am Sprecher (Klangstufe C2b): Ist einer der Sprechenden ein Dauersender, steht an seinem Namen das Zeichen mit Tooltip und Auswahl
    /// (stumm, wieder normal, für diesen Funker nicht mehr dämpfen); sonst <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HatDauersendZeichen))]
    public partial DauersendZeichen? DauersendZeichen { get; private set; }

    public bool HatDauersendZeichen => DauersendZeichen is not null;

    /// <summary>
    /// Einsatzleitung oder Admin haben dich in diesem Kanal einzeln stummgeschaltet (Klangstufe C2b, Q33): roter Punkt wie bei einer Kanalsperre; du
    /// hörst weiter, sendest hier aber nicht. Gilt die Stummschaltung für alle Kanäle, zeigt es der Mikro-Knopf.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoterPunkt))]
    public partial bool StummGeschaltet { get; private set; }

    /// <summary>Der Punkt ist rot mit weißem Querstrich: gesperrt (6.17) oder einzeln stummgeschaltet.</summary>
    public bool RoterPunkt => IstGesperrt || StummGeschaltet;

    /// <summary>Man selbst sendet gerade in diesen Kanal (Taste gedrückt oder Sprachaktivierung offen).</summary>
    [ObservableProperty]
    public partial bool EigeneSendung { get; private set; }

    /// <summary>
    /// Q43 (0.8.0): Du würdest in dieses Feld senden, aber es geht nichts hinaus (Ch. Ruhe, nicht verbunden, stumm durch die Einsatzleitung); der eigene
    /// Pegel steht dann gelb in <see cref="PegelAnteil"/>.
    /// </summary>
    [ObservableProperty]
    public partial bool WuerdeSenden { get; private set; }

    /// <summary>Q43: Das Feld steht in deiner Sendeauswahl, auch wenn es nichts annimmt (Ch. Ruhe, gesperrt); dort zeigt es <see cref="WuerdeSenden"/>.</summary>
    internal bool Sendeziel { get; private set; }

    /// <summary>Pegel für die Anzeige, 0..1.</summary>
    [ObservableProperty]
    public partial double PegelAnteil { get; private set; }

    /// <summary>Lautstärke dieses Kanals in dB (A9); gemerkt je Kanalnetz für alle Ch. (Q15), dieselbe wie unter Einstellungen > Empfangen (bis 0.9.3 Bereich Netze).</summary>
    public double LautstaerkeDb
    {
        get => NetzId is { } id ? _feld.LautstaerkeVon(id) : 0;
        set
        {
            if (NetzId is { } id) _feld.LautstaerkeSetzen(id, value);
        }
    }

    public string LautstaerkeText => FunkfeldViewModel.LautstaerkeText(LautstaerkeDb);

    /// <summary>Der Kanal erlaubt nur die Sprechtaste (vom Server festgelegt; Global nur für Ch. 1, Global selbst).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KannSprachaktivierung))]
    public partial bool NurTaste { get; private set; }

    public bool KannSprachaktivierung => !NurTaste && !IstLeer && _feld.SprachaktivierungModulAn;

    /// <summary>Das Klangmodul Sprachaktivierung wurde geschaltet (0.9.0): VA-Schalter und Anzeige neu lesen.</summary>
    internal void ModulGeaendert()
    {
        OnPropertyChanged(nameof(KannSprachaktivierung));
        OnPropertyChanged(nameof(Sprachaktivierung));
    }

    /// <summary>
    /// A10: Sprachaktivierung (true) oder Sprechtaste (false); gilt für alle Ch. des Kanalnetzes (bis 0.9.3 auch im Bereich Netze; seit 0.10.0 nur hier, E16). Mit dem Klangmodul
    /// Sprachaktivierung aus zeigt sie Taste; die Wahl je Kanal bleibt gemerkt und gilt wieder, sobald das Modul an ist.
    /// </summary>
    public bool Sprachaktivierung
    {
        get => _feld.SprachaktivierungModulAn && _feld.SprachaktivierungFuer(NetzId, NurTaste, VaStandard);
        set
        {
            if (NetzId is not { } id || NurTaste) return;
            _feld.SprachaktivierungSetzen(id, value);
        }
    }

    public string AusloesungText => Sprachaktivierung ? "VA" : "Taste";

    internal bool VaStandard { get; private set; }

    /// <summary>Kanal mit Ch., etwa "Brücke (Schiff 3)"; sonst der Name.</summary>
    public string KanalText => Kanalnetz is not null ? Name + " (" + ChText + ")" : Name;

    /// <summary>Für Bildschirmleser und Hinweistexte: "Brücke (Schiff 3): SENDET, spricht: Kobold".</summary>
    public string Beschreibung => KanalText + ": " + Text(Zustand) + (SprecherAktiv ? ", spricht: " + Sprecher : "");

    // ---------------------------------------------------------------- Ch.-Wähler (6.16)

    /// <summary>Das Feld trägt ein Kanalnetz: Der Ch.-Wähler steht da (Global hat keinen, es bleibt auf Ch. 1, Global selbst).</summary>
    [ObservableProperty]
    public partial bool HatCh { get; private set; }

    /// <summary>Der Ch. laut Lage, 0 ist die Ruhe; <c>null</c> ohne Kanalnetz.</summary>
    [ObservableProperty]
    public partial byte? Ch { get; private set; }

    /// <summary>Ein gewählter Ch., der nach 400 ms Ruhe hinausgeht (Mausrad, Pfeile); bis dahin zeigt die Kachel ihn schon.</summary>
    internal byte? ChVorgemerkt { get; private set; }

    /// <summary>Die Bezeichnung des Ch. ("Schiff 3", "Ruhe"); bei Global mit Ch. die Bezeichnung von Ch. 1, ohne Kanalnetz leer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KanalText), nameof(Beschreibung), nameof(KopfHilfe))]
    public partial string ChText { get; private set; } = "";

    /// <summary>Die Ch.-Liste des Kanalnetzes für den Ch.-Wähler, mit Ruhe zuerst; gesperrte und belegte mit Grund.</summary>
    public ObservableCollection<ChEintrag> ChListe { get; } = [];

    /// <summary>
    /// Der gewählte Ch. Setzen wählt (wie das Mausrad) mit 400 ms Ruhe vor dem Senden; ein Ch., der sich nicht wählen lässt, bleibt
    /// ohne Wirkung.
    /// </summary>
    public ChEintrag? ChWahl
    {
        get => (ChVorgemerkt ?? Ch) is { } c ? ChListe.FirstOrDefault(e => e.Ch == c) : null;
        set
        {
            if (_abgleich) return;
            if (value is null || !ChWaehlbar || !value.Waehlbar || value.Ch == (ChVorgemerkt ?? Ch))
            {
                OnPropertyChanged();
                return;
            }
            _feld.ChVormerken(this, value.Ch);
        }
    }

    /// <summary>Der Ch. lässt sich wechseln: verbunden und nicht gesperrt; seit 0.8.1 auch in einem festen Feld (Q45).</summary>
    [ObservableProperty]
    public partial bool ChWaehlbar { get; private set; }

    /// <summary>Warum der Ch. sich nicht wechseln lässt; <c>null</c>, wenn er es tut.</summary>
    [ObservableProperty]
    public partial string? ChGrund { get; private set; }

    /// <summary>Tooltip am Ch.-Wähler.</summary>
    public string ChHilfe => ChGrund ?? "Ch. wählen: Klick öffnet die Liste, Mausrad oder Pfeile blättern (gesendet nach 400 ms Ruhe).";

    /// <summary>Mausrad oder Pfeiltaste: zum nächsten wählbaren Ch. (+1) oder zum vorigen (-1), Ruhe ist der Anfang.</summary>
    [RelayCommand]
    public void ChSchritt(int richtung)
    {
        if (!ChWaehlbar || richtung == 0 || ChListe.Count == 0) return;
        var aktuell = ChVorgemerkt ?? Ch;
        var i = ChListe.ToList().FindIndex(e => e.Ch == aktuell);
        for (var j = i + Math.Sign(richtung); j >= 0 && j < ChListe.Count; j += Math.Sign(richtung))
        {
            if (!ChListe[j].Waehlbar) continue;
            _feld.ChVormerken(this, ChListe[j].Ch);
            return;
        }
    }

    /// <summary>Klick oder Enter in der Liste: sofort, ohne die 400 ms.</summary>
    [RelayCommand]
    private void ChWaehlen(ChEintrag eintrag)
    {
        ChWahl = eintrag;
        _feld.VormerkungenAbschicken();
    }

    // ---------------------------------------------------------------- Befehle

    [RelayCommand]
    private Task SendenAsync() => _feld.SendenWaehlenAsync(this, hinzufuegen: false);

    [RelayCommand]
    private Task SendenHinzufuegenAsync() => _feld.SendenWaehlenAsync(this, hinzufuegen: true);

    [RelayCommand]
    private Task HoerenUmschaltenAsync() => _feld.HoerenSetzenAsync(this, !Hoeren);

    [RelayCommand]
    private void AusloesungUmschalten() => Sprachaktivierung = !Sprachaktivierung;

    /// <summary>Zustand nach A17/A18: Leer ohne Netz, sonst Senden vor Hören vor Aus.</summary>
    public static KachelZustand Bestimmen(bool belegt, bool hoeren, bool gewaehlt) =>
        !belegt ? KachelZustand.Leer : gewaehlt ? KachelZustand.Senden : hoeren ? KachelZustand.Hoeren : KachelZustand.Aus;

    public static string Text(KachelZustand z) => z switch
    {
        KachelZustand.Senden => "SENDET",
        KachelZustand.Hoeren => "HÖRT",
        KachelZustand.Aus => "AUS",
        KachelZustand.Ruhe => "RUHE",
        KachelZustand.Gesperrt => "GESPERRT",
        _ => "FREI",
    };

    private static string Punkthilfe(KachelZustand z, string? gesperrt) => z switch
    {
        KachelZustand.Senden => "Dein Sendekanal (SENDET)",
        KachelZustand.Hoeren => "Du hörst diesen Kanal (HÖRT)",
        KachelZustand.Gesperrt => (gesperrt ?? "Gesperrt") + " (GESPERRT): du hörst und sendest hier nicht",
        KachelZustand.Aus => "Ganz aus, du hörst nichts (AUS)",
        KachelZustand.Ruhe => "Ruhe: Dieser Kanal hört und sendet nichts. Wähle einen Ch.",
        _ => "Kein Kanal in diesem Feld",
    };

    internal void Aktualisieren(FunkLage lage)
    {
        _abgleich = true;
        try
        {
            if (IstGlobal) GlobalAktualisieren(lage);
            else FeldAktualisieren(lage);
            var sprechend = NetzId is { } id ? lage.Sprecher.Where(s => s.Netze.Contains(id)).ToList() : [];
            Sprecher = string.Join(", ", sprechend.Select(s => s.Name));
            var zeichen = _feld.Einstellungen.Anzeige.ZeichenImFunkfeld && sprechend.Count > 0
                ? Design.Zeichensatz.Bilder(lage.Funker.FirstOrDefault(f => f.KontoId == sprechend[0].KontoId)?.Zeichen)
                : [];
            SprecherZeichen = zeichen.Count > 0 ? zeichen[0] : null;
            // Klangstufe C2b: das gelbe "!" am ersten Sprecher, der ein Dauersender ist; rote Punkte bei einzelner Stummschaltung durch die Leitung.
            DauersendZeichen = sprechend.Where(s => s.KontoId != lage.Konto?.Id).Select(s => _feld.Dauersend.ZeichenVon(s.KontoId)).FirstOrDefault(z => z is not null);
            var stumm = NetzId is { } netz && Zustand is not (KachelZustand.Leer or KachelZustand.Ruhe) ? _feld.Mikro.KanalSatz(lage, netz) : null;
            StummGeschaltet = stumm is not null;
            var hilfe = IstGlobal && Zustand == KachelZustand.Hoeren ? "Global hörst du immer (HÖRT)" : Punkthilfe(Zustand, GesperrtText);
            PunktHilfe = stumm is null ? hilfe : hilfe + ". " + stumm + ": du hörst, sendest hier aber nicht";
            OnPropertyChanged(nameof(LautstaerkeDb));
            OnPropertyChanged(nameof(LautstaerkeText));
            OnPropertyChanged(nameof(Sprachaktivierung));
            OnPropertyChanged(nameof(AusloesungText));
            OnPropertyChanged(nameof(KannSprachaktivierung));
            OnPropertyChanged(nameof(Beschreibung));
            OnPropertyChanged(nameof(ChWahl));
            OnPropertyChanged(nameof(ChHilfe));
        }
        finally
        {
            _abgleich = false;
        }
    }

    private void GlobalAktualisieren(FunkLage lage)
    {
        var g = lage.Global;
        NetzId = g?.Id;
        Kanalnetz = null;
        Name = g?.Name ?? "Global";
        // A20 (offen): Global steht auf Taste, lässt sich aber wie jeder Kanal umschalten (A10), sofern der Server es nicht auf
        // "nur Taste" festlegt; mit Ch. gilt das für Ch. 1, das feste Global-Feld (6.5).
        NurTaste = g?.NurTaste ?? true;
        VaStandard = false;
        Hoeren = true;
        KannAusschalten = false;
        Fest = false;
        GesperrtText = null;
        KannSenden = lage.GlobalDarfSenden && g is not null;
        Zustand = g is null ? KachelZustand.Leer : lage.GlobalSenden ? KachelZustand.Senden : KachelZustand.Hoeren;
        Sendeziel = g is not null && lage.GlobalSenden;
        HatCh = false;
        Ch = null;
        ChVorgemerktLoeschen();
        ChText = g is { IstKanalnetz: true } && g.Kanaele!.Count > Kanalrechnung.GlobalSelbst ? g.Kanaele[Kanalrechnung.GlobalSelbst] : "";
        ChListe.Clear();
        ChWaehlbar = false;
        ChGrund = "Global bleibt fest auf " + (ChText.Length > 0 ? ChText : "Global") + ".";
        Herkunft = g is null ? null : HerkunftGlobal;
    }

    private void FeldAktualisieren(FunkLage lage)
    {
        var f = lage.FeldVon((byte)Platz);
        var n = f is null ? null : lage.NetzVon(f.NetzId);
        var ch = n is { IstKanalnetz: true } && f is not null ? lage.ChVon(f.NetzId) : null;
        var kanalnetz = ch is not null ? n!.Id : (ushort?)null;
        if (kanalnetz != Kanalnetz) ChVorgemerktLoeschen();
        NetzId = n is null ? null : f!.NetzId;
        Kanalnetz = kanalnetz;
        Name = n?.Name ?? (f is null ? "frei" : "Kanal " + f.NetzId.ToString(CultureInfo.InvariantCulture));
        var sperre = f is null ? null : lage.SperreVon(f.NetzId);
        GesperrtText = sperre is null ? null : lage.SperrSatz(sperre);
        // Die übrigen Ch. von Global sind gewöhnliche Kanäle mit Sprachaktivierung; "nur Taste" von Global gilt nur für Ch. 1 (6.5).
        NurTaste = n?.NurTaste == true && !(n.IstGlobal && ch is not null);
        VaStandard = n?.SprachaktivierungStandard == true;
        Hoeren = f?.Hoeren == true;
        Fest = f?.Fest == true;
        var ruhe = ch == Kanalrechnung.Ruhe;
        KannAusschalten = f is not null && !ruhe;
        KannSenden = f?.Senden == true && !ruhe && sperre is null;
        Sendeziel = f is not null && lage.SendeFelder.Contains((byte)Platz);
        var basis = Bestimmen(f is not null, f?.Hoeren == true, Sendeziel && KannSenden);
        Zustand = f is null ? KachelZustand.Leer : ruhe ? KachelZustand.Ruhe : sperre is not null ? KachelZustand.Gesperrt : basis;

        // Ch.-Wähler
        HatCh = ch is not null;
        Ch = ch;
        if (ch is not null && n?.Kanaele is { } liste)
        {
            var eintraege = new List<ChEintrag>();
            for (var i = 0; i < liste.Count && i <= Kanalrechnung.ChMax; i++)
            {
                if (n.IstGlobal && i == Kanalrechnung.GlobalSelbst) continue;
                var id = Kanalrechnung.Id(n.Id, (byte)i);
                var anderes = lage.Felder.FirstOrDefault(x => x.Feld != Platz && x.NetzId == id);
                var s = lage.SperreVon(id);
                var grund = s is not null ? lage.SperrSatz(s) + "." : anderes is not null ? "Liegt schon in Feld " + (anderes.Feld + 1).ToString(CultureInfo.InvariantCulture) + "." : null;
                eintraege.Add(new ChEintrag((byte)i, liste[i], grund is null, grund) { Gesperrt = s is not null });
            }
            Abgleichen(ChListe, eintraege);
        }
        else
        {
            ChListe.Clear();
        }
        // Q45 (0.8.1): Den Ch. sperren nur die fehlende Verbindung und die Kanalsperre (6.17), nicht mehr "fest".
        string? nicht = !lage.IstAktiv ? "Nicht verbunden." : GesperrtText is { } g ? g + ". Das Feld bleibt, bis die Sperre endet." : null;
        ChWaehlbar = HatCh && nicht is null;
        ChGrund = HatCh ? nicht : null;
        if (!ChWaehlbar) ChVorgemerktLoeschen();
        ChText = ChBezeichnung(ChVorgemerkt ?? Ch);

        // Q44 (0.8.1): Der Name ist eine feste Beschriftung; der Tooltip sagt, woher der Kanal kommt.
        Herkunft = f is null ? null : Fest ? HerkunftFest : lage.Rolle is null ? HerkunftOhneRolle : HerkunftRolle;
    }

    private string ChBezeichnung(byte? ch) => ch is { } c && ChListe.FirstOrDefault(e => e.Ch == c) is { } e ? e.Text : "";

    /// <summary>Den Ch. vormerken (vom Funkfeld): sofort zeigen, gesendet wird später.</summary>
    internal void ChVormerkungSetzen(byte ch)
    {
        ChVorgemerkt = ch;
        ChText = ChBezeichnung(ch);
        OnPropertyChanged(nameof(ChWahl));
    }

    /// <summary>Die Vormerkung ist hinaus (oder verworfen): Ab jetzt zeigt die Kachel wieder die Lage.</summary>
    internal void ChVorgemerktLoeschen()
    {
        if (ChVorgemerkt is null) return;
        ChVorgemerkt = null;
        _feld.ChVormerkungVerworfen(this);
    }

    /// <summary>Ersetzt eine Liste nur, wenn sich ihr Inhalt geändert hat (CODEMAP, Fallen: Listen im Oberflächentakt).</summary>
    private static void Abgleichen<T>(ObservableCollection<T> ziel, IReadOnlyList<T> quelle)
    {
        if (ziel.SequenceEqual(quelle)) return;
        ziel.Clear();
        foreach (var e in quelle) ziel.Add(e);
    }

    /// <summary>Lautstärke oder Auslösung des Kanalnetzes wurden geändert (hier, in einem anderen Ch. oder unter Einstellungen > Empfangen).</summary>
    internal void NetzEinstellungNeu()
    {
        OnPropertyChanged(nameof(LautstaerkeDb));
        OnPropertyChanged(nameof(LautstaerkeText));
        OnPropertyChanged(nameof(Sprachaktivierung));
        OnPropertyChanged(nameof(AusloesungText));
    }

    internal void LiveSetzen(bool eigeneSendung, double pegelAnteil, bool wuerdeSenden = false)
    {
        EigeneSendung = eigeneSendung;
        WuerdeSenden = wuerdeSenden;
        PegelAnteil = pegelAnteil;
    }
}
