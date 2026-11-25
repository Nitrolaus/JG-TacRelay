// JG TacRelay - Maintainer: Sarevoc
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Funk.Protokoll.Identitaet;

// Liegt im Server (funk-server), nicht in der Client-Bibliothek Funk.Protokoll: Das normale Setup bringt keinen
// Verwaltungscode mit (A45, Gegenprüfung Stufe 1). Der Namensraum bleibt, damit Spezifikation, Server und Tests dieselben
// Namen tragen.

/// <summary>Zustand des Admin-Nachweises eines Kontos (3.9).</summary>
public enum AdminZustand
{
    /// <summary>Der Eigentümer hat das Konto vorgesehen; der Client darf einen Admin-Schlüssel beantragen.</summary>
    Vorgesehen,

    /// <summary>Schlüssel eingetragen, TOTP-Geheimnis erzeugt, erster Code steht aus.</summary>
    Einrichtung,

    /// <summary>Eingerichtet: Admin-Sitzungen sind möglich.</summary>
    Aktiv,

    /// <summary>Gesperrt (Konsole oder 10 Fehlversuche in 24 h), bis der Eigentümer an der Konsole entsperrt.</summary>
    Gesperrt,
}

/// <summary>Ein eingetragener Admin-Schlüssel: SPKI (Base64) und Anzeigeform seines Fingerabdrucks (Admin-Kennung).</summary>
public sealed record AdminSchluessel(string Spki, string Kennung, DateTimeOffset Eingetragen);

/// <summary>
/// Admin-Nachweis eines Kontos (3.9), wie er in <c>admins.json</c> steht: Schlüssel, Zustand, das TOTP-Geheimnis nur
/// verschlüsselt (<see cref="TotpTresor"/>), der zuletzt benutzte Zeitschritt, Notfallcodes nur als Prüfwerte,
/// Fehlversuche und Sperren.
/// </summary>
public sealed record AdminEintrag
{
    public required uint KontoId { get; init; }
    public AdminZustand Zustand { get; init; }

    /// <summary>Der Eigentümer hat einen Antrag erlaubt (<c>admin-vorsehen</c>); jeder Antrag ersetzt den offenen.</summary>
    public bool AntragErlaubt { get; init; }

    /// <summary>Beantragter Schlüssel (SPKI, Base64), bis der Eigentümer ihn mit der Kennung ernennt.</summary>
    public string? AntragSpki { get; init; }

    public DateTimeOffset? AntragZeit { get; init; }

    public ImmutableArray<AdminSchluessel> Schluessel { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Nonce || Chiffrat || Tag, Base64; entschlüsselt nur im Moment der Prüfung.</summary>
    public string? Geheimnis { get; init; }

    /// <summary>Zählt jedes neu erzeugte Geheimnis; steht in den AAD, damit kein altes Chiffrat zurückkommt.</summary>
    public uint Generation { get; init; }

    public DateTimeOffset? GeheimnisErzeugt { get; init; }

    /// <summary>Wann das Geheimnis gezeigt wurde (Client oder Konsole); genau einmal.</summary>
    public DateTimeOffset? GeheimnisAbgeholt { get; init; }

    public DateTimeOffset? Eingerichtet { get; init; }

    /// <summary>Zuletzt angenommener Zeitschritt; nur spätere gelten (RFC 6238, 5.2). -1: noch keiner.</summary>
    public long LetzterZeitschritt { get; init; } = -1;

    /// <summary>Prüfwerte der unbenutzten Notfallcodes (HMAC-SHA256, Base64).</summary>
    public ImmutableArray<string> Notfallcodes { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Fehlversuche der letzten 24 h (nur mit gültiger Unterschrift gezählt).</summary>
    public ImmutableArray<DateTimeOffset> Fehlversuche { get; init => field = value.IsDefault ? [] : value; } = [];

    public DateTimeOffset? PauseBis { get; init; }

    /// <summary>KONSOLE oder FEHLVERSUCHE, wenn gesperrt.</summary>
    public string? Sperrgrund { get; init; }

    public DateTimeOffset? ZuletztBenutzt { get; init; }

    /// <summary>Geräte, von denen schon eine Admin-Sitzung ausging (Warnung bei einem neuen Gerät).</summary>
    public ImmutableArray<uint> Geraete { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>Wie oft hintereinander nur ein Nachbarschritt passte ("Uhr des Telefons prüfen").</summary>
    public int Nachbarschritte { get; init; }
}

/// <summary>Unveränderlicher Stand aller Admin-Nachweise.</summary>
public sealed record Adminstand
{
    public static Adminstand Leer { get; } = new();

    public ImmutableDictionary<uint, AdminEintrag> Eintraege { get; init; } = ImmutableDictionary<uint, AdminEintrag>.Empty;

    /// <summary>Steigt mit jeder Änderung.</summary>
    public long Version { get; init; }
}

/// <summary>Nachweis einer Anfrage mit dem Admin-Schlüssel: Sitzung, Gerät, Nonce des Servers und Unterschrift.</summary>
public readonly record struct Nachweis(uint SitzungsId, Fingerabdruck Geraet, byte[] Nonce, byte[] Unterschrift);

/// <summary>
/// Ergebnis einer Aktion des <see cref="Adminbuch"/>. <see cref="Geheimnis"/> und <see cref="Notfallcodes"/> erscheinen nie
/// in <c>ToString</c>; der Aufrufer nullt <see cref="Geheimnis"/> nach dem Anzeigen.
/// </summary>
public sealed record Adminergebnis(Fehlercode Fehler) : Ergebnis(Fehler, [])
{
    /// <summary>Bei ZWEITFAKTOR_GESPERRT: Ende der Pause; <c>null</c> heißt gesperrt bis zur Konsole.</summary>
    public DateTimeOffset? Bis { get; init; }

    /// <summary>Dieser Versuch hat die Pause oder die Sperre ausgelöst (Meldung an alle Admins).</summary>
    public bool Gesperrt { get; init; }

    /// <summary>Der Versuch zählte als Fehlversuch (nur mit gültiger Unterschrift oder in einer Admin-Sitzung).</summary>
    public bool Fehlversuch { get; init; }

    /// <summary>Admin-Kennung (Anzeigeform) des beantragten oder eingetragenen Schlüssels.</summary>
    public string? Kennung { get; init; }

    public byte[]? Geheimnis { get; init; }

    public IReadOnlyList<string>? Notfallcodes { get; init; }

    public bool NotfallcodeBenutzt { get; init; }

    public int NotfallcodesUebrig { get; init; }

    /// <summary>Admin-Sitzung von einem Gerät, das noch nie eine hatte (Warnung).</summary>
    public bool NeuesGeraet { get; init; }

    /// <summary>Mehrfach nur der Nachbarschritt: "Uhr des Telefons prüfen".</summary>
    public bool UhrHinweis { get; init; }

    /// <summary>Das Chiffrat ließ sich nicht öffnen (Schlüssel getauscht); das ist ein Betriebsfehler, kein Rateversuch.</summary>
    public bool TresorDefekt { get; init; }

    /// <summary>Erste Einrichtung nach <c>admin-ernennen</c> (Geheimnis neu).</summary>
    public bool EinrichtungBegonnen { get; init; }

    public DateTimeOffset? AbholbarBis { get; init; }

    protected override bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Append(CultureInfo.InvariantCulture, $"Fehler = {Fehler}, Bis = {Bis:O}, Gesperrt = {Gesperrt}, Kennung = {Kennung}, Geheimnis = ***, Notfallcodes = ***");
        return true;
    }
}

/// <summary>
/// Admin-Nachweise als reine Logik (3.9): Admin-Schlüssel beantragen und an der Konsole ernennen, TOTP-Geheimnis genau
/// einmal abholen, erster Code, Admin-Anmeldung und Schritt-Bestätigung mit Code aus der App oder einem Notfallcode.
/// Jeder Zeitschritt gilt einmal, und nur Schritte nach dem zuletzt benutzten; geraten wird nur mit gültiger Unterschrift
/// (oder in einer offenen Admin-Sitzung), höchstens 5 Fehlversuche in 15 min (dann 15 min Pause) und 10 in 24 h (dann
/// Sperre bis zur Konsole). Kein Schalter schaltet das ab. Nicht threadsicher: genau ein Schreiber (die Zentrale).
/// </summary>
public sealed class Adminbuch
{
    /// <summary>So lange nach dem Erzeugen lässt sich das Geheimnis abholen (3.9, Schritt 4).</summary>
    public static readonly TimeSpan AbholFrist = TimeSpan.FromMinutes(10);

    /// <summary>So lange nach dem Abholen gilt der erste Code (3.9, Schritt 5).</summary>
    public static readonly TimeSpan EinrichtungsFrist = TimeSpan.FromMinutes(10);

    /// <summary>Fenster und Grenze für die Pause (5 Fehlversuche in 15 min, dann 15 min Pause).</summary>
    public static readonly TimeSpan PauseFenster = TimeSpan.FromMinutes(15);

    public const int FehlversuchePause = 5;

    /// <summary>10 Fehlversuche in 24 h sperren bis zur Konsole.</summary>
    public const int FehlversucheSperre = 10;

    public static readonly TimeSpan SperrFenster = TimeSpan.FromHours(24);

    /// <summary>Notfallcodes je Admin.</summary>
    public const int NotfallcodesAnzahl = 10;

    /// <summary>Admin-Schlüssel je Admin (etwa PC und Laptop), jeder mit eigener Kennung.</summary>
    public const int MaxSchluessel = 3;

    /// <summary>Ab so vielen Nachbarschritten in Folge kommt der Hinweis auf die Uhr.</summary>
    public const int NachbarschritteHinweis = 3;

    private readonly TimeProvider _zeit;
    private readonly TotpTresor _tresor;
    private Adminstand _stand;

    public Adminbuch(TimeProvider zeit, TotpTresor tresor, Adminstand? stand = null)
    {
        ArgumentNullException.ThrowIfNull(zeit);
        ArgumentNullException.ThrowIfNull(tresor);
        _zeit = zeit;
        _tresor = tresor;
        _stand = stand ?? Adminstand.Leer;
    }

    public Adminstand Stand => Volatile.Read(ref _stand);

    private DateTimeOffset Jetzt => _zeit.GetUtcNow();

    public AdminEintrag? Eintrag(uint kontoId) => Stand.Eintraege.TryGetValue(kontoId, out var e) ? e : null;

    // ---------------------------------------------------------------- Konsole (3.10)

    /// <summary><c>admin-vorsehen</c>: erlaubt einen Antrag. Ein eingerichteter Admin behält seinen Zustand (neuer PC).</summary>
    public Adminergebnis Vorsehen(uint kontoId)
    {
        var e = Eintrag(kontoId) ?? new AdminEintrag { KontoId = kontoId, Zustand = AdminZustand.Vorgesehen };
        Setzen(e with { AntragErlaubt = true, AntragSpki = null, AntragZeit = null });
        return new(Fehlercode.Keiner);
    }

    /// <summary>
    /// <c>admin-ernennen --kennung</c>: trägt den beantragten Schlüssel ein, wenn die vorgelesene Kennung passt. Beim ersten
    /// Schlüssel beginnt die Einrichtung mit einem neuen Geheimnis.
    /// </summary>
    public Adminergebnis Ernennen(uint kontoId, string kennung)
    {
        ArgumentNullException.ThrowIfNull(kennung);
        if (Eintrag(kontoId) is not { AntragSpki: { } spkiText } e) return new(Fehlercode.Unbekannt);
        var spki = Convert.FromBase64String(spkiText);
        var fp = Fingerabdruck.AusSpki(spki);
        if (!fp.AnzeigePasst(kennung)) return new(Fehlercode.Ungueltig);
        if (e.Schluessel.Length >= MaxSchluessel) return new(Fehlercode.Grenze);
        if (SchluesselVergeben(spkiText, kontoId)) return new(Fehlercode.Ungueltig);
        var jetzt = Jetzt;
        e = e with
        {
            Schluessel = e.Schluessel.Add(new AdminSchluessel(spkiText, fp.Anzeige, jetzt)),
            AntragErlaubt = false,
            AntragSpki = null,
            AntragZeit = null,
        };
        var neu = e.Zustand == AdminZustand.Vorgesehen;
        if (neu) e = MitNeuemGeheimnis(e, jetzt, anzeigen: false, out _);
        Setzen(e);
        return new(Fehlercode.Keiner) { Kennung = fp.Anzeige, EinrichtungBegonnen = neu, AbholbarBis = neu ? jetzt + AbholFrist : null };
    }

    /// <summary>
    /// <c>admin-einrichtung-neu</c>: neues Geheimnis, altes und alte Notfallcodes gelten nicht mehr. Mit
    /// <paramref name="anzeigen"/> gibt die Konsole es selbst aus (otpauth-URI); dann holt der Client es nicht mehr ab.
    /// </summary>
    public Adminergebnis EinrichtungNeu(uint kontoId, bool anzeigen)
    {
        if (Eintrag(kontoId) is not { } e) return new(Fehlercode.Unbekannt);
        if (e.Schluessel.IsEmpty) return new(Fehlercode.Ungueltig);
        var jetzt = Jetzt;
        e = MitNeuemGeheimnis(e, jetzt, anzeigen, out var geheimnis);
        Setzen(e);
        return new(Fehlercode.Keiner) { Geheimnis = geheimnis, EinrichtungBegonnen = true, AbholbarBis = anzeigen ? null : jetzt + AbholFrist };
    }

    /// <summary><c>zweitfaktor-bestaetigen</c> an der Konsole: erster Code ohne Unterschrift (der Eigentümer steht daneben).</summary>
    public Adminergebnis EinrichtungBestaetigenKonsole(uint kontoId, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (Eintrag(kontoId) is not { } e) return new(Fehlercode.Unbekannt);
        return EinrichtungAbschliessen(e, code);
    }

    /// <summary><c>admin-sperren</c>: keine Admin-Sitzung mehr, bis der Eigentümer entsperrt.</summary>
    public Adminergebnis Sperren(uint kontoId)
    {
        if (Eintrag(kontoId) is not { } e) return new(Fehlercode.Unbekannt);
        Setzen(e with { Zustand = AdminZustand.Gesperrt, Sperrgrund = "KONSOLE" });
        return new(Fehlercode.Keiner);
    }

    /// <summary><c>admin-entsperren</c>: hebt Sperre und Pause auf und löscht die Fehlversuche.</summary>
    public Adminergebnis Entsperren(uint kontoId)
    {
        if (Eintrag(kontoId) is not { } e) return new(Fehlercode.Unbekannt);
        var zustand = e.Zustand;
        if (zustand == AdminZustand.Gesperrt)
            zustand = e.Eingerichtet is not null && e.Geheimnis is not null ? AdminZustand.Aktiv
                : e.Geheimnis is not null ? AdminZustand.Einrichtung : AdminZustand.Vorgesehen;
        Setzen(e with { Zustand = zustand, Sperrgrund = null, PauseBis = null, Fehlversuche = [] });
        return new(Fehlercode.Keiner);
    }

    /// <summary><c>admin-zuruecksetzen</c>: Schlüssel, Geheimnis und Notfallcodes fort, neu wie vorgesehen (Verlust).</summary>
    public Adminergebnis Zuruecksetzen(uint kontoId)
    {
        if (Eintrag(kontoId) is null) return new(Fehlercode.Unbekannt);
        Setzen(new AdminEintrag { KontoId = kontoId, Zustand = AdminZustand.Vorgesehen, AntragErlaubt = true });
        return new(Fehlercode.Keiner);
    }

    /// <summary><c>admin-entziehen</c> oder Konto gelöscht: der Nachweis verschwindet ganz.</summary>
    public Adminergebnis Entziehen(uint kontoId)
    {
        if (Eintrag(kontoId) is null) return new(Fehlercode.Unbekannt);
        Uebernehmen(_stand with { Eintraege = _stand.Eintraege.Remove(kontoId) });
        return new(Fehlercode.Keiner);
    }

    /// <summary><c>admin-schluessel-entfernen --kennung</c>: etwa den Schlüssel eines verlorenen PCs.</summary>
    public Adminergebnis SchluesselEntfernen(uint kontoId, string kennung)
    {
        ArgumentNullException.ThrowIfNull(kennung);
        if (Eintrag(kontoId) is not { } e) return new(Fehlercode.Unbekannt);
        var treffer = e.Schluessel.Where(s => Fingerabdruck.AusSpki(Convert.FromBase64String(s.Spki)).AnzeigePasst(kennung)).ToList();
        if (treffer.Count != 1) return new(Fehlercode.Unbekannt);
        Setzen(e with { Schluessel = e.Schluessel.Remove(treffer[0]) });
        return new(Fehlercode.Keiner) { Kennung = treffer[0].Kennung };
    }

    /// <summary><c>notfallcodes</c>: 10 neue Notfallcodes, die alten gelten nicht mehr. Nur für eingerichtete Admins.</summary>
    public Adminergebnis NotfallcodesErzeugen(uint kontoId)
    {
        if (Eintrag(kontoId) is not { Zustand: AdminZustand.Aktiv } e) return new(Fehlercode.Ungueltig);
        var (codes, pruefwerte) = NeueNotfallcodes(kontoId);
        Setzen(e with { Notfallcodes = pruefwerte });
        return new(Fehlercode.Keiner) { Notfallcodes = codes, NotfallcodesUebrig = codes.Count };
    }

    // ---------------------------------------------------------------- Client, mit Unterschrift (3.9)

    /// <summary>
    /// <c>AdminEinrichtung ANTRAG</c>: nur für ein vorgesehenes Konto (sonst KEIN_RECHT), mit Besitznachweis des neuen
    /// Schlüssels. Ein neuer Antrag ersetzt den offenen; jeder ist eine Meldung an den Eigentümer.
    /// </summary>
    public Adminergebnis AntragStellen(uint kontoId, ReadOnlySpan<byte> spki, Nachweis nachweis)
    {
        if (Eintrag(kontoId) is not { AntragErlaubt: true } e) return new(Fehlercode.KeinRecht);
        if (!P256Spki.IstGueltig(spki)) return new(Fehlercode.Ungueltig);
        if (!Adminnachweis.Pruefen(spki, nachweis.Unterschrift, nachweis.SitzungsId, nachweis.Geraet, nachweis.Nonce, AdminZweck.Antrag))
            return new(Fehlercode.Ungueltig);
        var text = Convert.ToBase64String(spki);
        if (SchluesselVergeben(text, kontoId)) return new(Fehlercode.Ungueltig);
        Setzen(e with { AntragSpki = text, AntragZeit = Jetzt });
        return new(Fehlercode.Keiner) { Kennung = Fingerabdruck.AusSpki(spki).Anzeige };
    }

    /// <summary>
    /// <c>AdminEinrichtung GEHEIMNIS_ABHOLEN</c>: genau einmal, binnen 10 min nach dem Erzeugen, nur mit Unterschrift eines
    /// eingetragenen Schlüssels. Der Aufrufer zeigt es und nullt den Puffer.
    /// </summary>
    public Adminergebnis GeheimnisAbholen(uint kontoId, Nachweis nachweis)
    {
        if (Eintrag(kontoId) is not { Zustand: AdminZustand.Einrichtung } e) return new(Fehlercode.KeinRecht);
        if (!UnterschriftGilt(e, nachweis, AdminZweck.Geheimnis)) return new(Fehlercode.KeinRecht);
        var jetzt = Jetzt;
        if (e.GeheimnisAbgeholt is not null) return new(Fehlercode.Ungueltig);
        if (e.GeheimnisErzeugt is not { } erzeugt || jetzt - erzeugt > AbholFrist || e.Geheimnis is null) return new(Fehlercode.Frist);
        var geheimnis = _tresor.Entschluesseln(Convert.FromBase64String(e.Geheimnis), kontoId, e.Generation);
        if (geheimnis is null) return new(Fehlercode.Ungueltig) { TresorDefekt = true };
        Setzen(e with { GeheimnisAbgeholt = jetzt });
        return new(Fehlercode.Keiner) { Geheimnis = geheimnis, AbholbarBis = jetzt + EinrichtungsFrist };
    }

    /// <summary>
    /// <c>AdminEinrichtung BESTAETIGEN</c>: der erste Code aus der App; stimmt er, ist der Zweitfaktor eingerichtet, und es
    /// gibt 10 Notfallcodes, genau einmal angezeigt.
    /// </summary>
    public Adminergebnis EinrichtungBestaetigen(uint kontoId, Nachweis nachweis, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (Eintrag(kontoId) is not { } e || e.Zustand is not (AdminZustand.Einrichtung or AdminZustand.Gesperrt)) return new(Fehlercode.KeinRecht);
        if (!UnterschriftGilt(e, nachweis, AdminZweck.Einrichtung)) return new(Fehlercode.KeinRecht);
        return EinrichtungAbschliessen(e, code);
    }

    /// <summary>
    /// <c>AdminAnmeldung</c>: Unterschrift eines eingetragenen Schlüssels (Zweck ANMELDUNG) und ein Code aus der App oder
    /// ein Notfallcode. Ohne gültige Unterschrift zählt nichts (kein Raten, kein Aussperren durch Fremde).
    /// </summary>
    public Adminergebnis Anmelden(uint kontoId, uint geraetId, Nachweis nachweis, string? code, string? notfallcode)
    {
        if (Eintrag(kontoId) is not { } e || e.Zustand is AdminZustand.Vorgesehen or AdminZustand.Einrichtung) return new(Fehlercode.KeinRecht);
        if (!UnterschriftGilt(e, nachweis, AdminZweck.Anmeldung)) return new(Fehlercode.KeinRecht);
        var r = CodePruefen(e, code, notfallcode, out var neu);
        if (!r.Ok) return r;
        var neuesGeraet = !neu.Geraete.Contains(geraetId);
        if (neuesGeraet) neu = neu with { Geraete = neu.Geraete.Add(geraetId) };
        Setzen(neu);
        return r with { NeuesGeraet = neuesGeraet };
    }

    /// <summary>
    /// <c>AdminBestaetigen</c>: frischer Code in einer offenen Admin-Sitzung (die Zentrale lässt ihn nur dort zu). Fehlversuche
    /// zählen in dieselben Töpfe wie bei der Anmeldung.
    /// </summary>
    public Adminergebnis Bestaetigen(uint kontoId, string? code, string? notfallcode)
    {
        if (Eintrag(kontoId) is not { } e || e.Zustand is AdminZustand.Vorgesehen or AdminZustand.Einrichtung) return new(Fehlercode.KeinRecht);
        var r = CodePruefen(e, code, notfallcode, out var neu);
        if (r.Ok) Setzen(neu);
        return r;
    }

    /// <summary>Ob eine Admin-Sitzung überhaupt möglich ist (Welcome, Zeichen): eingerichtet und nicht gesperrt.</summary>
    public bool IstEingerichtet(uint kontoId) => Eintrag(kontoId) is { Zustand: AdminZustand.Aktiv };

    // ---------------------------------------------------------------- intern

    private Adminergebnis EinrichtungAbschliessen(AdminEintrag e, string code)
    {
        var jetzt = Jetzt;
        if (e.Zustand == AdminZustand.Gesperrt) return new(Fehlercode.ZweitfaktorGesperrt);
        if (e.GeheimnisAbgeholt is not { } abgeholt || jetzt - abgeholt > EinrichtungsFrist) return new(Fehlercode.Frist);
        var r = CodePruefen(e, code, null, out var neu);
        if (!r.Ok) return r;
        var (codes, pruefwerte) = NeueNotfallcodes(e.KontoId);
        Setzen(neu with { Zustand = AdminZustand.Aktiv, Eingerichtet = jetzt, Notfallcodes = pruefwerte });
        return r with { Notfallcodes = codes, NotfallcodesUebrig = codes.Count };
    }

    /// <summary>
    /// Prüft einen Code oder Notfallcode nach Pause und Sperre. Erfolg: <paramref name="neu"/> trägt den benutzten
    /// Zeitschritt bzw. den verbrauchten Notfallcode (noch nicht übernommen). Fehler: der Fehlversuch ist schon gezählt und
    /// übernommen.
    /// </summary>
    private Adminergebnis CodePruefen(AdminEintrag e, string? code, string? notfallcode, out AdminEintrag neu)
    {
        neu = e;
        var jetzt = Jetzt;
        if (e.Zustand == AdminZustand.Gesperrt) return new(Fehlercode.ZweitfaktorGesperrt);
        if (e.PauseBis is { } pause && pause > jetzt) return new(Fehlercode.ZweitfaktorGesperrt) { Bis = pause };
        if ((code is null) == (notfallcode is null)) return new(Fehlercode.Ungueltig);

        if (code is not null)
        {
            if (e.Geheimnis is null) return new(Fehlercode.KeinRecht);
            var geheimnis = _tresor.Entschluesseln(Convert.FromBase64String(e.Geheimnis), e.KontoId, e.Generation);
            if (geheimnis is null) return new(Fehlercode.Ungueltig) { TresorDefekt = true };
            try
            {
                if (Totp.TryLesen(code, out var eingabe))
                {
                    var t = Totp.Zeitschritt(jetzt);
                    // Aktueller Schritt zuerst, dann die Nachbarn; nur Schritte nach dem zuletzt benutzten (Einmalverwendung).
                    foreach (var delta in (ReadOnlySpan<int>)[0, -1, 1])
                    {
                        var schritt = t + delta;
                        if (schritt <= e.LetzterZeitschritt || !Totp.Passt(geheimnis, schritt, eingabe)) continue;
                        var nachbar = delta == 0 ? 0 : e.Nachbarschritte + 1;
                        neu = e with { LetzterZeitschritt = schritt, ZuletztBenutzt = jetzt, Nachbarschritte = nachbar };
                        return new(Fehlercode.Keiner) { UhrHinweis = nachbar >= NachbarschritteHinweis, NotfallcodesUebrig = e.Notfallcodes.Length };
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(geheimnis);
            }
        }
        else
        {
            Span<char> norm = stackalloc char[Einmalcode.Zeichen];
            if (Einmalcode.Normalisieren(notfallcode, norm))
            {
                var ist = _tresor.NotfallPruefwert(norm, e.KontoId);
                for (var i = 0; i < e.Notfallcodes.Length; i++)
                {
                    if (!CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(e.Notfallcodes[i]), ist)) continue;
                    neu = e with { Notfallcodes = e.Notfallcodes.RemoveAt(i), ZuletztBenutzt = jetzt };
                    return new(Fehlercode.Keiner) { NotfallcodeBenutzt = true, NotfallcodesUebrig = e.Notfallcodes.Length - 1 };
                }
            }
        }
        return Fehlversuch(e, jetzt);
    }

    /// <summary>Zählt einen Fehlversuch: 5 in 15 min ergeben 15 min Pause, 10 in 24 h die Sperre bis zur Konsole.</summary>
    private Adminergebnis Fehlversuch(AdminEintrag e, DateTimeOffset jetzt)
    {
        var liste = e.Fehlversuche.Where(t => jetzt - t < SperrFenster).Append(jetzt).ToImmutableArray();
        if (liste.Length >= FehlversucheSperre)
        {
            Setzen(e with { Fehlversuche = liste, Zustand = AdminZustand.Gesperrt, Sperrgrund = "FEHLVERSUCHE" });
            return new(Fehlercode.ZweitfaktorGesperrt) { Gesperrt = true, Fehlversuch = true };
        }
        var jungeFehler = liste.Count(t => jetzt - t < PauseFenster);
        if (jungeFehler >= FehlversuchePause)
        {
            var bis = jetzt + PauseFenster;
            Setzen(e with { Fehlversuche = liste, PauseBis = bis });
            return new(Fehlercode.ZweitfaktorGesperrt) { Bis = bis, Gesperrt = true, Fehlversuch = true };
        }
        Setzen(e with { Fehlversuche = liste });
        return new(Fehlercode.ZweitfaktorFalsch) { Fehlversuch = true };
    }

    private static bool UnterschriftGilt(AdminEintrag e, Nachweis n, AdminZweck zweck)
    {
        if (n.Nonce is null || n.Unterschrift is null) return false;
        foreach (var s in e.Schluessel)
            if (Adminnachweis.Pruefen(Convert.FromBase64String(s.Spki), n.Unterschrift, n.SitzungsId, n.Geraet, n.Nonce, zweck)) return true;
        return false;
    }

    private AdminEintrag MitNeuemGeheimnis(AdminEintrag e, DateTimeOffset jetzt, bool anzeigen, out byte[]? gezeigt)
    {
        var geheimnis = Totp.NeuesGeheimnis();
        var generation = e.Generation + 1;
        var chiffrat = _tresor.Verschluesseln(geheimnis, e.KontoId, generation);
        gezeigt = anzeigen ? geheimnis : null;
        if (!anzeigen) CryptographicOperations.ZeroMemory(geheimnis);
        return e with
        {
            Zustand = AdminZustand.Einrichtung,
            Geheimnis = Convert.ToBase64String(chiffrat),
            Generation = generation,
            GeheimnisErzeugt = jetzt,
            GeheimnisAbgeholt = anzeigen ? jetzt : null,
            Eingerichtet = null,
            LetzterZeitschritt = -1,
            Notfallcodes = [],
            Fehlversuche = [],
            PauseBis = null,
            Sperrgrund = null,
            Nachbarschritte = 0,
        };
    }

    private (List<string> Codes, ImmutableArray<string> Pruefwerte) NeueNotfallcodes(uint kontoId)
    {
        var codes = new List<string>(NotfallcodesAnzahl);
        var werte = ImmutableArray.CreateBuilder<string>(NotfallcodesAnzahl);
        Span<char> norm = stackalloc char[Einmalcode.Zeichen];
        while (codes.Count < NotfallcodesAnzahl)
        {
            var code = Einmalcode.Erzeugen();
            if (codes.Contains(code, StringComparer.Ordinal) || !Einmalcode.Normalisieren(code, norm)) continue;
            codes.Add(code);
            werte.Add(Convert.ToBase64String(_tresor.NotfallPruefwert(norm, kontoId)));
        }
        return (codes, werte.ToImmutable());
    }

    private bool SchluesselVergeben(string spki, uint ausser)
    {
        foreach (var e in _stand.Eintraege.Values)
        {
            if (e.KontoId == ausser) continue;
            if (e.AntragSpki == spki) return true;
            foreach (var s in e.Schluessel)
                if (s.Spki == spki) return true;
        }
        if (_stand.Eintraege.TryGetValue(ausser, out var eigen))
            foreach (var s in eigen.Schluessel)
                if (s.Spki == spki) return true;
        return false;
    }

    private void Setzen(AdminEintrag e) => Uebernehmen(_stand with { Eintraege = _stand.Eintraege.SetItem(e.KontoId, e) });

    private void Uebernehmen(Adminstand neu) => Volatile.Write(ref _stand, neu with { Version = _stand.Version + 1 });
}
