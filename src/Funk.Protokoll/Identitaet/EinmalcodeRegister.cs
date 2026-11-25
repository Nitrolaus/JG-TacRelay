// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using System.Security.Cryptography;

namespace Funk.Protokoll.Identitaet;

/// <summary>Woran ein Einmalcode gebunden ist: Verbindung, Fingerabdruck, Gerätename und gemeldete Schlüsselart.</summary>
public sealed record CodeBindung(long Verbindung, Fingerabdruck Geraet, string Geraetename, SchluesselArt SchluesselArt);

/// <summary>
/// Antwort auf <c>Registrierung</c>: Code in Anzeigeform und Ablauf, oder <c>GRENZE</c>/<c>UNGUELTIG</c>.
/// <see cref="ToString"/> verschweigt den Code, denn Codes stehen nie im Log (3.8, P-46).
/// </summary>
public sealed record CodeAusstellung(Fehlercode Fehler, string? Code, DateTimeOffset GueltigBis, IReadOnlyList<Wirkung> Wirkungen)
    : Ergebnis(Fehler, Wirkungen)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"CodeAusstellung {{ Fehler = {Fehler}, Code = {(Code is null ? "-" : "(verborgen)")}, GueltigBis = {GueltigBis:O}, Wirkungen = {Wirkungen.Count} }}");
}

/// <summary>Ergebnis der Codeeingabe: die Bindung oder einheitlich <c>CODE_UNGUELTIG</c> bzw. <c>GRENZE</c>.</summary>
public sealed record CodeEinloesung(Fehlercode Fehler, CodeBindung? Bindung, IReadOnlyList<Wirkung> Wirkungen)
    : Ergebnis(Fehler, Wirkungen);

/// <summary>
/// Offene Einmalcodes des Servers (3.4) samt Grenzen (3.4, 3.8, 4.8). Flüchtig (7.2), nur SHA-256-Werte, ein Code
/// je Verbindung, 5 min gültig, einmalig. Falsch, abgelaufen und verbraucht ergeben einheitlich
/// <c>CODE_UNGUELTIG</c>. Nicht threadsicher: ein Aufrufer (Zustandsschreiber) je Instanz.
/// </summary>
public sealed class EinmalcodeRegister
{
    public const int CodesJeVerbindung = 3;
    public const int CodesJePraefixUndStunde = 10;
    public const int AlarmAbCodesJeStunde = 100;
    public const int FehlversucheJeDiscordIdUndStunde = 5;
    public const int ErfolgeJeDiscordIdUndStunde = 3;
    public const int ErfolgeJeStunde = 30;

    /// <summary>Härtung gegen eine Flut erfundener Discord-IDs: so viele Zählerstände höchstens.</summary>
    public const int MaxDiscordIds = 4096;

    /// <summary>Härtung gegen rotierende Präfixe (etwa viele IPv6-/64 aus einem /48): so viele Zählerstände höchstens.</summary>
    public const int MaxPraefixe = 4096;

    /// <summary>Alarm, wenn die Tabelle der Zähler je Präfix voll ist (<see cref="MaxPraefixe"/>).</summary>
    public const string AlarmTabelleVoll = "REGISTRIERUNG_FLUT";

    /// <summary>
    /// Ob ein <c>GRENZE</c> aus <see cref="Ausstellen"/> oder <see cref="AntragZulassen"/> an der Quelle selbst liegt (3 je
    /// Verbindung, 10 je Präfix und Stunde) und damit ein Verstoß für die Sperrliste ist (A51). Eine volle Tabelle
    /// (<see cref="AlarmTabelleVoll"/>) ist eine globale Grenze: Sie trifft jedes neue Präfix, auch ein neues Mitglied, und
    /// zählt deshalb nie gegen die Adresse.
    /// </summary>
    public static bool GrenzeDerQuelle(Ergebnis ergebnis)
    {
        ArgumentNullException.ThrowIfNull(ergebnis);
        if (ergebnis.Fehler != Fehlercode.Grenze) return false;
        foreach (var w in ergebnis.Wirkungen)
            if (w is Alarm { Art: AlarmTabelleVoll }) return false;
        return true;
    }

    private static readonly TimeSpan Stunde = TimeSpan.FromHours(1);

    private readonly TimeProvider _zeit;
    private readonly Zufallsquelle _zufall;
    private readonly Dictionary<Hash32, OffenerCode> _offen = [];
    private readonly Dictionary<long, VerbindungsStand> _verbindungen = [];
    private readonly Dictionary<string, Zeitfensterzaehler> _praefixe = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DiscordStand> _discord = new(StringComparer.Ordinal);
    private readonly Zeitfensterzaehler _ausgestellt;
    private readonly Zeitfensterzaehler _erfolge;

    /// <param name="zeit">Wanduhr für Ablauf und <c>gueltigBis</c>, monotone Uhr für die Grenzen (4.8).</param>
    public EinmalcodeRegister(TimeProvider zeit) : this(zeit, RandomNumberGenerator.Fill)
    {
    }

    internal EinmalcodeRegister(TimeProvider zeit, Zufallsquelle zufall)
    {
        ArgumentNullException.ThrowIfNull(zeit);
        ArgumentNullException.ThrowIfNull(zufall);
        _zeit = zeit;
        _zufall = zufall;
        _ausgestellt = new(zeit, Stunde, AlarmAbCodesJeStunde + 1);
        _erfolge = new(zeit, Stunde, ErfolgeJeStunde);
    }

    /// <summary>Zahl der offenen Codes (für Messwerte und Tests).</summary>
    public int OffeneCodes => _offen.Count;

    /// <summary>
    /// <c>Registrierung{geraetename}</c> (3.4 Schritt 2): neuer Code für diese Verbindung. Ein offener Code derselben
    /// Verbindung wird ersetzt. <paramref name="praefix"/> ist der Präfixschlüssel nach Abschnitt 0.
    /// </summary>
    public CodeAusstellung Ausstellen(long verbindung, Fingerabdruck geraet, string geraetename, SchluesselArt art, string praefix)
    {
        ArgumentNullException.ThrowIfNull(praefix);
        if (!Zeichenregeln.IstText(geraetename, 1, 40)) return new(Fehlercode.Ungueltig, null, default, []);
        var jetzt = _zeit.GetUtcNow();

        if (!_verbindungen.TryGetValue(verbindung, out var stand))
            _verbindungen[verbindung] = stand = new VerbindungsStand();
        if (stand.Ausgestellt >= CodesJeVerbindung) return new(Fehlercode.Grenze, null, default, []);
        if (!_praefixe.TryGetValue(praefix, out var jePraefix))
        {
            if (_praefixe.Count >= MaxPraefixe) Aufraeumen();
            if (_praefixe.Count >= MaxPraefixe) return new(Fehlercode.Grenze, null, default, [new Alarm(AlarmTabelleVoll)]);
            _praefixe[praefix] = jePraefix = new Zeitfensterzaehler(_zeit, Stunde, CodesJePraefixUndStunde);
        }
        if (jePraefix.Anzahl() >= CodesJePraefixUndStunde) return new(Fehlercode.Grenze, null, default, []);

        string code;
        Hash32 hash;
        var versuche = 0;
        do
        {
            if (++versuche > 16) throw new InvalidOperationException("Die Zufallsquelle liefert nur schon offene Codes.");
            code = Einmalcode.Erzeugen(_zufall);
            Einmalcode.TryHash(code, out hash);
        }
        while (_offen.ContainsKey(hash));

        if (stand.Offen is { } alt) _offen.Remove(alt);
        var ablauf = jetzt + Einmalcode.Gueltigkeit;
        _offen[hash] = new OffenerCode(new CodeBindung(verbindung, geraet, geraetename, art), ablauf);
        stand.Offen = hash;
        stand.Ausgestellt++;
        jePraefix.Zaehlen();
        _ausgestellt.Zaehlen();

        IReadOnlyList<Wirkung> wirkungen = _ausgestellt.Anzahl() > AlarmAbCodesJeStunde ? [new Alarm("REGISTRIERUNG_HAEUFIG")] : [];
        return new(Fehlercode.Keiner, code, ablauf, wirkungen);
    }

    /// <summary>
    /// Ein Antrag ohne Code (3.4, P3) zählt in dieselben Grenzen wie ein Code: 3 je Verbindung, 10 je Präfix und Stunde,
    /// der globale Zähler nur als Alarm. Liefert <c>GRENZE</c> oder zählt und liefert <c>OK</c>.
    /// </summary>
    public Ergebnis AntragZulassen(long verbindung, string praefix)
    {
        ArgumentNullException.ThrowIfNull(praefix);
        if (!_verbindungen.TryGetValue(verbindung, out var stand))
            _verbindungen[verbindung] = stand = new VerbindungsStand();
        if (stand.Ausgestellt >= CodesJeVerbindung) return new Ergebnis(Fehlercode.Grenze, []);
        if (!_praefixe.TryGetValue(praefix, out var jePraefix))
        {
            if (_praefixe.Count >= MaxPraefixe) Aufraeumen();
            if (_praefixe.Count >= MaxPraefixe) return new Ergebnis(Fehlercode.Grenze, [new Alarm(AlarmTabelleVoll)]);
            _praefixe[praefix] = jePraefix = new Zeitfensterzaehler(_zeit, Stunde, CodesJePraefixUndStunde);
        }
        if (jePraefix.Anzahl() >= CodesJePraefixUndStunde) return new Ergebnis(Fehlercode.Grenze, []);
        stand.Ausgestellt++;
        jePraefix.Zaehlen();
        _ausgestellt.Zaehlen();
        return new Ergebnis(Fehlercode.Keiner, _ausgestellt.Anzahl() > AlarmAbCodesJeStunde ? [new Alarm("REGISTRIERUNG_HAEUFIG")] : []);
    }

    /// <summary>
    /// <c>Verknuepfen{discordId, code}</c> vom Bot (3.4 Schritt 4). Vor dem Nachschlagen gelten die Grenzen je
    /// Discord-ID und global (3.8); darüber <c>GRENZE</c> mit Alarm, der Code bleibt dann unberührt.
    /// </summary>
    public CodeEinloesung Einloesen(ReadOnlySpan<char> eingabe, string discordId)
    {
        if (!Zeichenregeln.IstSnowflake(discordId)) return new(Fehlercode.Ungueltig, null, []);
        var jetzt = _zeit.GetUtcNow();
        if (!_discord.TryGetValue(discordId, out var stand))
        {
            if (_discord.Count >= MaxDiscordIds) Aufraeumen();
            if (_discord.Count >= MaxDiscordIds) return new(Fehlercode.Grenze, null, [new Alarm("VERKNUEPFEN_FLUT")]);
            _discord[discordId] = stand = new DiscordStand(_zeit);
        }
        if (stand.Fehlversuche.Anzahl() >= FehlversucheJeDiscordIdUndStunde
            || stand.Erfolge.Anzahl() >= ErfolgeJeDiscordIdUndStunde
            || _erfolge.Anzahl() >= ErfolgeJeStunde)
            return new(Fehlercode.Grenze, null, [new Alarm("VERKNUEPFEN_GRENZE")]);

        var bindung = Nachschlagen(eingabe, jetzt);
        if (bindung is null)
        {
            stand.Fehlversuche.Zaehlen();
            return new(Fehlercode.CodeUngueltig, null, []);
        }
        stand.Erfolge.Zaehlen();
        _erfolge.Zaehlen();
        return new(Fehlercode.Keiner, bindung, []);
    }

    /// <summary><c>funk-admin verknuepfen --code</c> (3.5 Bootstrap): ohne Grenzen je Discord-ID.</summary>
    public CodeEinloesung EinloesenUeberAdminWerkzeug(ReadOnlySpan<char> eingabe)
    {
        var bindung = Nachschlagen(eingabe, _zeit.GetUtcNow());
        return bindung is null ? new(Fehlercode.CodeUngueltig, null, []) : new(Fehlercode.Keiner, bindung, []);
    }

    /// <summary>Die Verbindung ist zu: ihr offener Code gilt nicht mehr.</summary>
    public void VerbindungBeendet(long verbindung)
    {
        if (!_verbindungen.Remove(verbindung, out var stand)) return;
        if (stand.Offen is { } hash) _offen.Remove(hash);
    }

    /// <summary>Entfernt abgelaufene Codes und leere Zählerstände.</summary>
    public void Aufraeumen()
    {
        var jetzt = _zeit.GetUtcNow();
        foreach (var (hash, code) in _offen.ToArray())
        {
            if (jetzt < code.Ablauf) continue;
            _offen.Remove(hash);
            if (_verbindungen.TryGetValue(code.Bindung.Verbindung, out var stand) && stand.Offen == hash) stand.Offen = null;
        }
        foreach (var (praefix, zaehler) in _praefixe.ToArray())
            if (zaehler.Anzahl() == 0) _praefixe.Remove(praefix);
        foreach (var (id, stand) in _discord.ToArray())
            if (stand.Fehlversuche.Anzahl() == 0 && stand.Erfolge.Anzahl() == 0) _discord.Remove(id);
    }

    private CodeBindung? Nachschlagen(ReadOnlySpan<char> eingabe, DateTimeOffset jetzt)
    {
        if (!Einmalcode.TryHash(eingabe, out var hash) || !_offen.Remove(hash, out var code)) return null;
        if (_verbindungen.TryGetValue(code.Bindung.Verbindung, out var stand) && stand.Offen == hash) stand.Offen = null;
        return jetzt < code.Ablauf ? code.Bindung : null;
    }

    private sealed record OffenerCode(CodeBindung Bindung, DateTimeOffset Ablauf);

    private sealed class VerbindungsStand
    {
        public int Ausgestellt { get; set; }

        public Hash32? Offen { get; set; }
    }

    private sealed class DiscordStand(TimeProvider zeit)
    {
        public Zeitfensterzaehler Fehlversuche { get; } = new(zeit, Stunde, FehlversucheJeDiscordIdUndStunde);

        public Zeitfensterzaehler Erfolge { get; } = new(zeit, Stunde, ErfolgeJeDiscordIdUndStunde);
    }
}
