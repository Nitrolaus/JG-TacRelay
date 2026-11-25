// JG TacRelay - Maintainer: Sarevoc
using Funk.Client.Ablage;
using Funk.Client.Modelle;
using Funk.Klang.Eingabe;
using Funk.Tests.Oberflaeche;

namespace Funk.Tests.Maschine;

/// <summary>Eine Quelle wie Raw Input: mehrere Zuordnungen und Netztasten, Belegen liefert die vorgegebene Taste.</summary>
internal sealed class TastenQuelleAttrappe(params EingabeArt[] arten) : ITastenQuelle
{
    public IReadOnlyList<Tastenzuordnung> Zuordnungen
    {
        get;
        set
        {
            if (value.Any(z => !Unterstuetzt(z.Taste.Art))) throw new NotSupportedException("Attrappe: Art nicht unterstützt");
            field = [.. value];
            Gesetzt++;
        }
    } = [];

    public int Gesetzt { get; private set; }

    public Tastenbelegung? Belegung
    {
        get => Zuordnungen.Where(z => z.Funktion == Tastenfunktion.Sprechen).Select(z => (Tastenbelegung?)z.Taste).FirstOrDefault();
        set => Zuordnungen = [.. Zuordnungen.Where(z => z.Funktion != Tastenfunktion.Sprechen),
            .. value is { } b ? new[] { new Tastenzuordnung(Tastenfunktion.Sprechen, b) } : []];
    }

    public bool IstGedrueckt { get; private set; }

    /// <summary>Was Belegen liefert; null heißt: es kommt keine Taste (bis zum Abbruch).</summary>
    public Tastenbelegung? NaechsteTaste { get; set; }

    public event Action? Gedrueckt;

    public event Action? Losgelassen;

    public event Action<Tastenfunktion>? Ausgeloest;

    public event Action<Tastenfunktion, bool>? Gehalten;

    public bool Unterstuetzt(EingabeArt art) => arten.Contains(art);

    public Task<Tastenbelegung> NaechsteTasteAsync(CancellationToken ct) => NaechsteTaste is { } t ? Task.FromResult(t) : Warten(ct);

    /// <summary>Wie zuletzt belegt wurde: vorsichtig (der Schritt beim ersten Start) oder nicht; null, solange nie.</summary>
    public bool? ZuletztVorsichtig { get; private set; }

    public Task<Tastenbelegung> NaechsteTasteAsync(bool vorsichtig, CancellationToken ct)
    {
        ZuletztVorsichtig = vorsichtig;
        return NaechsteTasteAsync(ct);
    }

    public void Druecken(bool unten)
    {
        IstGedrueckt = unten;
        if (unten) Gedrueckt?.Invoke();
        else Losgelassen?.Invoke();
    }

    public void Ausloesen(Tastenfunktion f) => Ausgeloest?.Invoke(f);

    /// <summary>Eine gehaltene Funktion außer Sprechen (Melden) hinunter oder hinauf.</summary>
    public void Halten(Tastenfunktion f, bool unten) => Gehalten?.Invoke(f, unten);

    public void Dispose()
    {
    }

    private static async Task<Tastenbelegung> Warten(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new OperationCanceledException(ct);
    }
}

/// <summary>Eine einfache Quelle wie die DirectInput-Sprechtaste des Joystick-Moduls: eine Belegung, keine Netztasten.</summary>
internal sealed class EinfacheQuelleAttrappe(EingabeArt art) : ISprechtasteQuelle
{
    public Tastenbelegung? Belegung { get; set; }

    public bool IstGedrueckt { get; private set; }

    public event Action? Gedrueckt;

    public event Action? Losgelassen;

    public bool Unterstuetzt(EingabeArt a) => a == art;

    public Task<Tastenbelegung> NaechsteTasteAsync(CancellationToken ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => default(Tastenbelegung), ct, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    public void Druecken(bool unten)
    {
        IstGedrueckt = unten;
        if (unten) Gedrueckt?.Invoke();
        else Losgelassen?.Invoke();
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Globale Tasten (Punkt 4): mehrere Sprechtasten, Netztasten Numpad + und - (A5, A7), Hängeschutz je Taste, der Verbund
/// als Naht für DirectInput (eine Quelle mehr ist eine Zeile), Belegen in den Einstellungen und die Voreinstellung.
/// </summary>
internal static class TastenTests
{
    private static readonly Tastenbelegung Strg = Tastenbelegung.Taste(0xE01D);
    private static readonly Tastenbelegung Maus4 = Tastenbelegung.Maus(Tastenbelegung.MausX1);
    private static readonly Tastenbelegung Plus = Tastenbelegung.Taste(Tastenbelegung.NumpadPlus);
    private static readonly Tastenbelegung Minus = Tastenbelegung.Taste(Tastenbelegung.NumpadMinus);
    private static readonly Tastenbelegung Stick = new(EingabeArt.Joystick, 7, @"\\?\HID#VID_3344&PID_0194&MI_00#7&1a2b3c&0&0000#{4d1e55b2}");

    private static Tastenzuordnung Z(Tastenfunktion f, Tastenbelegung t) => new(f, t);

    [Test]
    public static void Zustand_MehrereSprechtasten_OffenBisZurLetzten()
    {
        var z = new SprechtastenZustand();
        _ = z.Zuordnen([Z(Tastenfunktion.Sprechen, Strg), Z(Tastenfunktion.Sprechen, Maus4)]);
        var wechsel = new List<int>();
        foreach (var e in new[] { new Tastenereignis(Strg, true), new(Maus4, true), new(Strg, true), new(Strg, false), new(Maus4, false) })
        {
            z.Verarbeiten(e, 0, out var w, out var f, out _);
            Pruefe.Gleich(null, f, "keine Netztaste");
            if (w != 0) wechsel.Add(w);
        }
        Pruefe.Gleich("1,-1", string.Join(",", wechsel), "gedrückt mit der ersten, los erst mit der letzten Taste");
    }

    [Test]
    public static void Zustand_Netztasten_NurDieFlanke()
    {
        var z = new SprechtastenZustand();
        _ = z.Zuordnen([Z(Tastenfunktion.NetzVor, Plus), Z(Tastenfunktion.NetzZurueck, Minus), Z(Tastenfunktion.Sprechen, Strg)]);
        var ausgeloest = new List<Tastenfunktion>();
        foreach (var e in new[] { new Tastenereignis(Plus, true), new(Plus, true), new(Plus, true), new(Plus, false), new(Minus, true), new(Minus, false), new(Plus, true) })
        {
            z.Verarbeiten(e, 0x6B, out var w, out var f, out _);
            Pruefe.Gleich(0, w, "Netztasten öffnen kein Mikrofon");
            if (f is { } x) ausgeloest.Add(x);
        }
        Pruefe.Gleich("NetzVor,NetzZurueck,NetzVor", string.Join(",", ausgeloest), "Tastenwiederholung beim Halten schaltet nicht weiter");
        Pruefe.Wahr(z.EineUnten, "Plus ist noch unten (Hängeschutz läuft)");
    }

    [Test]
    public static void Zustand_Haengeschutz_JeTasteMitEigenemCode()
    {
        var z = new SprechtastenZustand();
        _ = z.Zuordnen([Z(Tastenfunktion.Sprechen, Strg), Z(Tastenfunktion.Sprechen, Maus4), Z(Tastenfunktion.NetzVor, Plus)]);
        z.Verarbeiten(new Tastenereignis(Strg, true), 0x11, out var w, out _, out _);
        Pruefe.Gleich(1, w, "gedrückt");
        z.Verarbeiten(new Tastenereignis(Maus4, true), 0x05, out _, out _, out _);
        z.Verarbeiten(new Tastenereignis(Plus, true), 0x6B, out _, out _, out _);
        // Strg und Plus hängen (Loslassen hinter UAC verpasst), die Maustaste ist wirklich unten.
        static bool NurMaus(ushort vk) => vk == 0x05;
        Pruefe.Falsch(z.HaengerPruefen(NurMaus), "erste Prüfung");
        Pruefe.Falsch(z.HaengerPruefen(NurMaus), "Strg und Plus gelten jetzt als los, die Maus hält das Mikrofon offen");
        Pruefe.Wahr(z.IstGedrueckt && z.EineUnten, "noch gedrückt");
        z.Verarbeiten(new Tastenereignis(Plus, true), 0x6B, out _, out var f, out _);
        Pruefe.Gleich(Tastenfunktion.NetzVor, f, "eine hängen gebliebene Netztaste schaltet beim nächsten Druck wieder");
        z.Verarbeiten(new Tastenereignis(Plus, false), 0x6B, out _, out _, out _);
        Pruefe.Falsch(z.HaengerPruefen(_ => false), "Maus: erste Prüfung oben");
        Pruefe.Wahr(z.HaengerPruefen(_ => false), "zweite Prüfung: Loslassen melden");
        Pruefe.Falsch(z.EineUnten || z.IstGedrueckt, "nichts mehr unten");
    }

    [Test]
    public static void Verbund_VerteiltZuordnungen_DirectInputIstEineZeile()
    {
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        using var verbund = new SprechtastenVerbund(raw);
        verbund.Zuordnungen = [Z(Tastenfunktion.Sprechen, Strg), Z(Tastenfunktion.Sprechen, Maus4), Z(Tastenfunktion.NetzVor, Plus), Z(Tastenfunktion.Sprechen, Stick)];
        Pruefe.Gleich(3, raw.Zuordnungen.Count, "Raw Input bekommt Tastatur und Maus, alle Funktionen");
        Pruefe.Gleich(Stick, verbund.NichtBedient.Single().Taste, "Joystick ohne DirectInput: nicht bedient, aber gespeichert");
        Pruefe.Falsch(verbund.Unterstuetzt(EingabeArt.Joystick), "noch kein Joystick");

        // Nach dem Merge des Moduls joystick: eine Zeile.
        var directInput = new EinfacheQuelleAttrappe(EingabeArt.Joystick);
        verbund.Hinzufuegen(directInput);
        Pruefe.Gleich(Stick, directInput.Belegung, "die DirectInput-Quelle bekommt die Joystick-Sprechtaste");
        Pruefe.Gleich(0, verbund.NichtBedient.Count, "alles bedient");
        Pruefe.Wahr(verbund.Unterstuetzt(EingabeArt.Joystick), "Joystick belegbar");
        Pruefe.Gleich(3, raw.Zuordnungen.Count, "Raw Input unverändert");
        Pruefe.Gleich(Strg, verbund.Belegung, "erste Sprechtaste");
    }

    [Test]
    public static void Verbund_SprechtasteUeberAlleQuellen_NetztastenDurch()
    {
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        var stick = new EinfacheQuelleAttrappe(EingabeArt.Joystick);
        using var verbund = new SprechtastenVerbund(raw, stick);
        var folge = new List<string>();
        verbund.Gedrueckt += () => folge.Add("runter");
        verbund.Losgelassen += () => folge.Add("hoch");
        verbund.Ausgeloest += f => folge.Add(f.ToString());
        raw.Druecken(true);
        stick.Druecken(true);
        raw.Druecken(false);
        Pruefe.Gleich("runter", string.Join(",", folge), "Tastatur los, der Stick hält noch: Mikrofon bleibt offen");
        Pruefe.Wahr(verbund.IstGedrueckt, "gedrückt");
        stick.Druecken(false);
        raw.Ausloesen(Tastenfunktion.NetzZurueck);
        Pruefe.Gleich("runter,hoch,NetzZurueck", string.Join(",", folge), "los mit dem letzten, Netztaste durchgereicht");
    }

    [Test]
    public static void Zustand_Belegen_LinkerKlickImEigenenFenster_WirdKeineTaste()
    {
        // Gegenprüfung: Während des Belegens heißt der Knopf "Abbrechen". Wer ihn mit der Maus drückt, erzeugt zuerst ein
        // WM_INPUT für die linke Maustaste; das wurde die Sprechtaste, und danach sendete jeder Klick.
        var vorn = true;
        var z = new SprechtastenZustand { Lernt = true, EigenesFensterVorn = () => vorn };
        var links = Tastenbelegung.Maus(Tastenbelegung.MausLinks);
        z.Verarbeiten(new Tastenereignis(links, true), 0x01, out var w, out _, out var gelernt);
        Pruefe.Gleich(null, gelernt, "Klick ins eigene Fenster: keine Taste");
        Pruefe.Wahr(z.Lernt && w == 0, "es wird weiter gewartet");
        z.Verarbeiten(new Tastenereignis(Maus4, true), 0x05, out _, out _, out gelernt);
        Pruefe.Gleich(Maus4, gelernt, "Maustaste 4 im eigenen Fenster gilt");

        z.Lernt = true;
        vorn = false;
        z.Verarbeiten(new Tastenereignis(links, true), 0x01, out _, out _, out gelernt);
        Pruefe.Gleich(links, gelernt, "außerhalb des Programms lässt sich auch die linke Maustaste belegen");
    }

    /// <summary>
    /// Seit 0.10.0 (AP2, E8): Beim ersten Start wartet die Startseite von selbst auf die Sprechtaste. Ein Rechtsklick (Kontextmenü) oder ein Druck auf
    /// das Rad ins eigene Fenster belegte sonst Maustaste 2 oder 3; sie bedienen jetzt wie die linke die Oberfläche. Außerhalb des Programms
    /// lassen sie sich belegen, Maustaste 4 und 5 auch im Fenster.
    /// </summary>
    [Test]
    public static void Zustand_Belegen_RechterUndMittlererKlickImEigenenFenster_WerdenKeineTaste()
    {
        var vorn = true;
        var z = new SprechtastenZustand { Lernt = true, EigenesFensterVorn = () => vorn };
        foreach (var code in new[] { Tastenbelegung.MausRechts, Tastenbelegung.MausMitte })
        {
            var maus = Tastenbelegung.Maus(code);
            z.Verarbeiten(new Tastenereignis(maus, true), 0, out var w, out _, out var gelernt);
            Pruefe.Gleich(null, gelernt, "Maustaste " + code + " im eigenen Fenster: keine Taste");
            Pruefe.Wahr(z.Lernt && w == 0, "es wird weiter gewartet");
        }
        Pruefe.Wahr(SprechtastenZustand.BedientOberflaeche(Tastenbelegung.Maus(Tastenbelegung.MausLinks)), "links bedient die Oberfläche");
        Pruefe.Falsch(SprechtastenZustand.BedientOberflaeche(Maus4) || SprechtastenZustand.BedientOberflaeche(Strg), "Maustaste 4 und die Tastatur nicht");
        vorn = false;
        z.Verarbeiten(new Tastenereignis(Tastenbelegung.Maus(Tastenbelegung.MausRechts), true), 0, out _, out _, out var aussen);
        Pruefe.Gleich(Tastenbelegung.Maus(Tastenbelegung.MausRechts), aussen, "außerhalb des Programms lässt sich die rechte Maustaste belegen");
    }

    /// <summary>
    /// Befund der Prüfung: Der Schritt Sprechtaste beim ersten Start wartet von selbst und ohne Frist, also auch, wenn man das Fenster nur bedient.
    /// Vorsichtig belegt (<see cref="SprechtastenZustand.Vorsichtig"/>) werden Alt aus Alt+Tab, Strg aus Strg+2 und die Zahl dazu, Tab, Pfeile,
    /// Eingabe und Leertaste im eigenen Fenster keine Sprechtaste. Strg, Alt, Umschalt und Windows zählen allein beim Loslassen (AltGr ergibt Alt
    /// rechts), jede andere Taste sofort; eine schon vor dem Belegen gehaltene Strg (Strg+1) zählt ebenso als Kürzel. Ohne vorsichtig (Ändern,
    /// Belegen mit Frist) bleibt es bei der ersten Taste.
    /// </summary>
    [Test]
    public static void Zustand_VorsichtigBelegen_KeineKuerzelUndKeineBedientasten()
    {
        var vorn = true;
        var physischStrg = false;
        var z = new SprechtastenZustand { EigenesFensterVorn = () => vorn, ModifikatorUnten = () => physischStrg };
        Tastenbelegung? Folge(params (int Code, bool Unten)[] ereignisse)
        {
            z.Vorsichtig = true;
            z.Lernt = true;
            Tastenbelegung? gelernt = null;
            foreach (var (code, unten) in ereignisse)
            {
                z.Verarbeiten(new Tastenereignis(Tastenbelegung.Taste(code), unten), 0, out _, out _, out var g);
                gelernt ??= g;
            }
            return gelernt;
        }
        const int altLinks = 0x0038, strgLinks = 0x001D, tab = 0x000F, zwei = 0x0003, f8 = 0x0042, altRechts = 0xE038, pfeilRunter = 0xE050;

        Pruefe.Gleich(null, Folge((altLinks, true), (tab, true), (tab, false), (altLinks, false)), "Alt+Tab: nichts");
        Pruefe.Gleich(null, Folge((strgLinks, true), (zwei, true), (zwei, false), (strgLinks, false)), "Strg+2: weder Strg noch die Zwei");
        Pruefe.Wahr(z.Lernt, "es wird weiter gewartet");
        Pruefe.Gleich(null, Folge((strgLinks, true), (strgLinks, true), (strgLinks, true)), "Strg gehalten (mit Wiederholung): noch nichts");
        Pruefe.Gleich(Tastenbelegung.Taste(strgLinks), Folge((strgLinks, true), (strgLinks, true), (strgLinks, false)), "Strg allein, losgelassen: Strg");
        Pruefe.Falsch(z.Lernt, "gelernt");
        Pruefe.Gleich(Tastenbelegung.Taste(altRechts), Folge((strgLinks, true), (altRechts, true), (strgLinks, false), (altRechts, false)),
            "AltGr (Strg links und Alt rechts): Alt rechts");
        foreach (var bedient in new[] { tab, 0x001C, 0x0039, pfeilRunter, 0xE048, 0xE04B, 0xE04D, 0xE049, 0xE051, 0xE047, 0xE04F })
            Pruefe.Gleich(null, Folge((bedient, true), (bedient, false)), "0x" + bedient.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + " im eigenen Fenster: bedient das Fenster");
        Pruefe.Gleich(Tastenbelegung.Taste(f8), Folge((tab, true), (tab, false), (f8, true)), "danach eine gewöhnliche Taste: sofort");
        Pruefe.Gleich(Tastenbelegung.Taste(0xE01C), Folge((0xE01C, true)), "Eingabe am Ziffernblock zählt");
        Pruefe.Gleich(Tastenbelegung.Taste(1), Folge((1, true)), "Esc kommt durch (bricht dann ab)");

        physischStrg = true;
        Pruefe.Gleich(null, Folge((zwei, true), (zwei, false)), "Strg schon vor dem Belegen gehalten (Strg+1): die Zwei zählt nicht");
        physischStrg = false;
        Pruefe.Gleich(Tastenbelegung.Taste(zwei), Folge((zwei, true)), "Strg los: die Zwei zählt");

        vorn = false;
        Pruefe.Gleich(Tastenbelegung.Taste(tab), Folge((tab, true)), "außerhalb des Programms: auch Tab");
        vorn = true;
        Pruefe.Gleich(null, Folge((strgLinks, true)), "Strg gedrückt: Kandidat");
        z.Verarbeiten(new Tastenereignis(Maus4, true), 0, out _, out _, out var maus);
        Pruefe.Gleich(Maus4, maus, "Maustaste 4 zählt auch vorsichtig sofort (die Maus folgt den Regeln für die Maus)");

        // Ohne vorsichtig (Ändern, Belegen mit Frist): die erste gedrückte Taste, auch Alt und Tab.
        z.Vorsichtig = false;
        z.Lernt = true;
        z.Verarbeiten(new Tastenereignis(Tastenbelegung.Taste(altLinks), true), 0, out _, out _, out var sofort);
        Pruefe.Gleich(Tastenbelegung.Taste(altLinks), sofort, "ohne vorsichtig: Alt sofort");
        Pruefe.Wahr(SprechtastenZustand.IstModifikator(Strg) && !SprechtastenZustand.IstModifikator(Plus), "Strg rechts ist ein Modifikator, Num + nicht");
        Pruefe.Wahr(SprechtastenZustand.BedientFenster(Tastenbelegung.Taste(tab)) && !SprechtastenZustand.BedientFenster(Plus), "Tab bedient das Fenster, Num + nicht");
    }

    /// <summary>
    /// "Ändern" auf Start (seit 0.10.0, E17): Die neue Taste ersetzt alle bisherigen Sprechtasten; Esc lässt alles stehen. Eine neu belegte
    /// Sprechtaste erledigt den Schritt Sprechtaste des ersten Starts (E8).
    /// </summary>
    [Test]
    public static async Task Einstellungen_Ersetzen_SprechtastenUndErststart()
    {
        using var a = new Aufbau();
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        using var vm = new TastenViewModel(a.Ablage, raw, Tastennamen.Kurz);
        var sprechen = vm.Funktionen[0];
        a.Ablage.Aktuell.Tasten.ErststartSprechtaste = false;
        raw.NaechsteTaste = Strg;
        Pruefe.Gleich(Belegergebnis.Belegt, await vm.BelegenAsync(sprechen), "belegt");
        Pruefe.Gleich((bool?)true, a.Ablage.Aktuell.Tasten.ErststartSprechtaste, "die erste Sprechtaste erledigt den Schritt");
        raw.NaechsteTaste = Maus4;
        _ = await vm.BelegenAsync(sprechen);
        Pruefe.Gleich(2, sprechen.Belegungen.Count, "Belegen fügt hinzu");

        raw.NaechsteTaste = Tastenbelegung.Taste(0x0001);
        Pruefe.Gleich(Belegergebnis.Abgebrochen, await vm.BelegenAsync(sprechen, ersetzen: true), "Esc");
        Pruefe.Gleich(2, sprechen.Belegungen.Count, "Esc lässt alles stehen");

        raw.NaechsteTaste = Plus;
        Pruefe.Gleich(Belegergebnis.Belegt, await vm.BelegenAsync(sprechen, ersetzen: true), "ersetzt");
        Pruefe.Gleich("Num +", string.Join(",", sprechen.Belegungen.Select(b => b.Name)), "nur noch die neue Taste");
        Pruefe.Gleich(0, vm.Funktionen.Single(f => f.Funktion == Tastenfunktion.NetzVor).Belegungen.Count, "Num + ist von Sendekanal vor gewandert");
        Pruefe.Gleich(1, raw.Zuordnungen.Count(z => z.Funktion == Tastenfunktion.Sprechen), "auch an der Quelle");

        raw.NaechsteTaste = Plus;
        _ = await vm.BelegenAsync(sprechen, ersetzen: true);
        Pruefe.Gleich("Num +", string.Join(",", sprechen.Belegungen.Select(b => b.Name)), "dieselbe Taste noch einmal: bleibt die einzige");

        // Ohne Frist (der erste Start) wartet es, bis still beendet wird; das zählt nicht als Abbruch des Nutzers.
        raw.NaechsteTaste = null;
        var warten = vm.BelegenAsync(sprechen, Timeout.InfiniteTimeSpan);
        Pruefe.Wahr(sprechen.Wartet, "wartet ohne Frist");
        vm.WartenBeenden();
        Pruefe.Falsch(sprechen.Wartet, "still beendet, sofort");
        Pruefe.Gleich(Belegergebnis.Beendet, await warten.WaitAsync(TimeSpan.FromSeconds(5)), "beendet, nicht abgebrochen");
        Pruefe.Gleich(null, vm.Hinweis, "ohne Hinweis");
    }

    [Test]
    public static void Verbund_NeueZuordnungen_LassenGehalteneQuelleLos()
    {
        // Gegenprüfung: Eine einfache Quelle (DirectInput) meldet beim Wechsel ihrer Belegung kein Loslassen. Wurde die
        // gehaltene Joystick-Taste umbelegt oder gelöscht, blieb der Verbund gedrückt und das Mikrofon offen.
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        var stick = new EinfacheQuelleAttrappe(EingabeArt.Joystick);
        using var verbund = new SprechtastenVerbund(raw, stick);
        verbund.Zuordnungen = [Z(Tastenfunktion.Sprechen, Stick), Z(Tastenfunktion.Sprechen, Strg)];
        var folge = new List<string>();
        verbund.Gedrueckt += () => folge.Add("runter");
        verbund.Losgelassen += () => folge.Add("hoch");
        stick.Druecken(true);
        verbund.Zuordnungen = [Z(Tastenfunktion.Sprechen, Strg)];
        Pruefe.Gleich("runter,hoch", string.Join(",", folge), "Joystick-Taste gelöscht, während sie gehalten war: Mikrofon zu");
        Pruefe.Falsch(verbund.IstGedrueckt, "nichts mehr gedrückt");
        stick.Druecken(false);
        Pruefe.Gleich("runter,hoch", string.Join(",", folge), "das späte Loslassen der Quelle zählt nicht doppelt");
        raw.Druecken(true);
        raw.Druecken(false);
        Pruefe.Gleich("runter,hoch,runter,hoch", string.Join(",", folge), "danach wie gewohnt");
    }

    [Test]
    public static void Verbund_Freigeben_LaesstDasMikrofonLos()
    {
        // Gegenprüfung: App.OnExit gibt die Tasten zuerst frei, "ein gehaltenes Mikrofon wird dabei losgelassen". Der
        // Verbund meldete sich aber vor dem Freigeben der Quellen ab; deren letztes Loslassen kam nicht mehr an.
        var stick = new EinfacheQuelleAttrappe(EingabeArt.Joystick);
        var verbund = new SprechtastenVerbund(new TastenQuelleAttrappe(EingabeArt.Tastatur), stick);
        var los = 0;
        verbund.Losgelassen += () => los++;
        stick.Druecken(true);
        verbund.Dispose();
        Pruefe.Gleich(1, los, "beim Freigeben losgelassen");
        verbund.Dispose();
        Pruefe.Gleich(1, los, "zweites Freigeben meldet nichts");

        var still = new SprechtastenVerbund(new TastenQuelleAttrappe(EingabeArt.Tastatur));
        var nie = 0;
        still.Losgelassen += () => nie++;
        still.Dispose();
        Pruefe.Gleich(0, nie, "ohne gedrückte Taste kein Loslassen");
    }

    [Test]
    public static async Task Einstellungen_BelegenMehrfachVerschiebenLoeschen()
    {
        using var a = new Aufbau();
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        using var verbund = new SprechtastenVerbund(raw);
        using var vm = new TastenViewModel(a.Ablage, verbund, Tastennamen.Kurz);
        var sprechen = vm.Funktionen.Single(f => f.Funktion == Tastenfunktion.Sprechen);
        var vor = vm.Funktionen.Single(f => f.Funktion == Tastenfunktion.NetzVor);
        Pruefe.Gleich("Num +", vor.Belegungen.Single().Name, "Voreinstellung: Numpad + für Sendenetz vor (A7)");
        Pruefe.Gleich("Num -", vm.Funktionen.Single(f => f.Funktion == Tastenfunktion.NetzZurueck).Belegungen.Single().Name, "Numpad - zurück");
        Pruefe.Gleich(2, raw.Zuordnungen.Count, "an Raw Input");
        Pruefe.Wahr(vm.OhneSprechtaste && vm.NetztastenBelegt, "noch keine Sprechtaste, Netztasten belegt");

        raw.NaechsteTaste = Strg;
        await sprechen.BelegenCommand.ExecuteAsync(null);
        raw.NaechsteTaste = Maus4;
        await sprechen.BelegenCommand.ExecuteAsync(null);
        Pruefe.Gleich(2, sprechen.Belegungen.Count, "zwei Sprechtasten");
        Pruefe.Gleich(2, raw.Zuordnungen.Count(z => z.Funktion == Tastenfunktion.Sprechen), "beide an Raw Input");
        Pruefe.Falsch(vm.OhneSprechtaste, "Sprechtaste da");

        raw.NaechsteTaste = Strg;
        await sprechen.BelegenCommand.ExecuteAsync(null);
        Pruefe.Gleich(2, sprechen.Belegungen.Count, "dieselbe Taste nicht doppelt");
        Pruefe.Wahr(vm.Hinweis?.Contains("schon belegt", StringComparison.Ordinal) == true, "Hinweis: " + vm.Hinweis);

        raw.NaechsteTaste = Plus;
        await sprechen.BelegenCommand.ExecuteAsync(null);
        Pruefe.Gleich(0, vor.Belegungen.Count, "Num + wandert von 'Sendenetz vor' zur Sprechtaste");
        Pruefe.Wahr(vm.Hinweis?.Contains("war", StringComparison.Ordinal) == true, "Hinweis: " + vm.Hinweis);

        raw.NaechsteTaste = Tastenbelegung.Taste(0x0001);
        await vor.BelegenCommand.ExecuteAsync(null);
        Pruefe.Gleich("Abgebrochen.", vm.Hinweis, "Esc bricht ab");
        Pruefe.Gleich(0, vor.Belegungen.Count, "nichts belegt");

        sprechen.Belegungen.Single(b => b.Zuordnung.Taste == Plus).LoeschenCommand.Execute(null);
        Pruefe.Gleich(2, sprechen.Belegungen.Count, "gelöscht");
        Pruefe.Falsch(raw.Zuordnungen.Any(z => z.Taste == Plus), "auch an der Quelle");

        a.Ablage.Sichern();
        var gelesen = a.Speicher.Laden().Tasten;
        Pruefe.Wahr(gelesen.Voreingestellt, "Voreinstellung gemerkt");
        Pruefe.Gleich("Sprechen:Tastatur:57373,Sprechen:Maus:4,NetzZurueck:Tastatur:74",
            string.Join(",", gelesen.Eintraege.OrderBy(e => e.Funktion).ThenBy(e => e.Art).Select(e => $"{e.Funktion}:{e.Art}:{e.Code}")), "gespeichert, gelöschtes Num + bleibt weg");
    }

    [Test]
    public static async Task Einstellungen_Belegen_FristUndAbbrechen()
    {
        using var a = new Aufbau();
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        using var vm = new TastenViewModel(a.Ablage, raw, Tastennamen.Kurz, TimeSpan.FromMilliseconds(80));
        var sprechen = vm.Funktionen[0];
        await sprechen.BelegenCommand.ExecuteAsync(null);
        Pruefe.Wahr(vm.Hinweis?.StartsWith("Keine Taste", StringComparison.Ordinal) == true, "Frist: " + vm.Hinweis);
        Pruefe.Falsch(sprechen.Wartet, "wartet nicht mehr");

        using var lange = new TastenViewModel(a.Ablage, raw, Tastennamen.Kurz, TimeSpan.FromSeconds(30));
        var warten = lange.Funktionen[0].BelegenCommand.ExecuteAsync(null);
        Pruefe.Wahr(lange.Funktionen[0].Wartet && lange.Funktionen[0].BelegenText == "Abbrechen", "Knopf heißt jetzt Abbrechen");
        await lange.Funktionen[0].BelegenCommand.ExecuteAsync(null);
        await warten.WaitAsync(TimeSpan.FromSeconds(5));
        Pruefe.Gleich("Abgebrochen.", lange.Hinweis, "vom Nutzer abgebrochen");
        Pruefe.Gleich("Belegen", lange.Funktionen[0].BelegenText, "wieder Belegen");

        using var ohne = new TastenViewModel(a.Ablage, null);
        await ohne.Funktionen[0].BelegenCommand.ExecuteAsync(null);
        Pruefe.Falsch(ohne.Verfuegbar, "ohne Raw Input");
        Pruefe.Wahr(ohne.Hinweis?.Contains("nicht verfügbar", StringComparison.Ordinal) == true, "Hinweis ohne Quelle");
    }

    [Test]
    public static void Einstellungen_Joystick_ErstMitQuelleVerfuegbar()
    {
        using var a = new Aufbau();
        a.Ablage.Aktuell.Tasten.Eintraege.Add(TastenEintrag.Aus(Z(Tastenfunktion.Sprechen, Stick)));
        using var ohne = new SprechtastenVerbund(new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus));
        using var vm = new TastenViewModel(a.Ablage, ohne, Tastennamen.Kurz);
        var zeile = vm.Funktionen[0].Belegungen.Single();
        Pruefe.Falsch(zeile.Verfuegbar, "ohne DirectInput-Quelle nicht verfügbar");
        Pruefe.Gleich("Joystick - Knopf 7 (VID_3344&PID_0194) (nicht verfügbar)", zeile.Beschreibung, "trotzdem angezeigt");
        Pruefe.Falsch(vm.JoystickMoeglich, "Joystick nicht belegbar");

        using var mit = new SprechtastenVerbund(new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus), new EinfacheQuelleAttrappe(EingabeArt.Joystick));
        using var vm2 = new TastenViewModel(a.Ablage, mit, Tastennamen.Kurz);
        Pruefe.Wahr(vm2.Funktionen[0].Belegungen.Single().Verfuegbar, "mit Quelle verfügbar");
        Pruefe.Wahr(vm2.JoystickMoeglich && vm2.ArtenText.Contains("Joystick", StringComparison.Ordinal), "Belegen nennt den Joystick");
    }

    [Test]
    public static void Einstellungen_VoreinstellungUndBereinigen()
    {
        var e = new ClientEinstellungen();
        e.Normalisieren();
        Pruefe.Gleich("NetzVor:78,NetzZurueck:74", string.Join(",", e.Tasten.Eintraege.Select(x => $"{x.Funktion}:{x.Code}")), "Numpad + und -");
        e.Tasten.Eintraege.Clear();
        e.Normalisieren();
        Pruefe.Gleich(0, e.Tasten.Eintraege.Count, "gelöschte Voreinstellung kommt nicht wieder");

        e.Tasten.Eintraege.AddRange([
            new TastenEintrag { Funktion = Tastenfunktion.Sprechen, Art = EingabeArt.Tastatur, Code = 0 },
            new TastenEintrag { Funktion = Tastenfunktion.Sprechen, Art = EingabeArt.Maus, Code = 9 },
            new TastenEintrag { Funktion = Tastenfunktion.Sprechen, Art = EingabeArt.Joystick, Code = 3 },
            new TastenEintrag { Funktion = (Tastenfunktion)9, Art = EingabeArt.Tastatur, Code = 0x1D },
            new TastenEintrag { Funktion = Tastenfunktion.Sprechen, Art = EingabeArt.Tastatur, Code = 0xE01D },
            new TastenEintrag { Funktion = Tastenfunktion.NetzVor, Art = EingabeArt.Tastatur, Code = 0xE01D },
        ]);
        e.Normalisieren();
        Pruefe.Gleich("Sprechen:57373", string.Join(",", e.Tasten.Eintraege.Select(x => $"{x.Funktion}:{x.Code}")),
            "ungültige und doppelte Tasten fallen weg, die erste Zuordnung bleibt");
    }

    [Test]
    public static void Einstellungen_NetztastenGlobal_FunkfeldSchaltetNichtDoppelt()
    {
        using var a = new Aufbau();
        var raw = new TastenQuelleAttrappe(EingabeArt.Tastatur, EingabeArt.Maus);
        using var verbund = new SprechtastenVerbund(raw);
        a.Tastenquelle = verbund;
        var h = a.Haupt(out var funkfeld, out _, out _, out _);
        var einstellungen = (EinstellungenViewModel)Seiten.Einstellungen(h).Inhalt;
        Pruefe.Wahr(funkfeld.NetztastenGlobal, "Num + und - global belegt: das Funkfeld schaltet + und - nicht selbst");
        foreach (var f in einstellungen.Tasten.Funktionen)
            foreach (var b in f.Belegungen.ToList()) b.LoeschenCommand.Execute(null);
        Pruefe.Falsch(funkfeld.NetztastenGlobal, "ohne globale Netztasten wieder selbst");
    }
}
