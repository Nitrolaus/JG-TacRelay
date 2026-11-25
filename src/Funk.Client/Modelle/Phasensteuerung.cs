// JG TacRelay - Maintainer: Sarevoc
using System.Globalization;
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>Wo die Phasensteuerung gerade steht.</summary>
public enum Phasenzustand
{
    Ruhe,

    /// <summary>Erster Druck: eine Phase ist scharf, ein zweiter Druck derselben Taste schaltet.</summary>
    Scharf,

    /// <summary>Die Anfrage ist beim Server.</summary>
    Wartet,
}

/// <summary>
/// Phasen schalten per Taste oder Joystick, zweistufig (RAID-S2-ENTWURF Abschnitt 3, "Bedienung zweistufig"): Der erste Druck von
/// "Phase vor" (oder "Phase zurück") macht die nächste (vorige) Phase scharf, an den Server geht nichts, und die Meldezeile nennt
/// Phase, Zahl der Funker und wen die Rangregel auslässt. Derselbe Knopf noch einmal, frühestens <see cref="Meldezeiten.ZweiterDruckAbMs"/>
/// und spätestens <see cref="Meldezeiten.ScharfMs"/> danach, schaltet (<c>PhaseSchalten</c> mit <c>erwartet</c>); ein schnellerer Druck
/// zählt nicht (Prellen, Doppeltipp), die Gegenrichtung bricht ab, nach 5 s verfällt die Wahl still. Ohne Antwort binnen 3 s sagt die
/// Meldezeile "Keine Antwort - Stand folgt"; VERALTET übernimmt die Phase des Servers und nennt, wer schneller war.
/// <para>
/// Die Tasten wirken nur mit ZUWEISEN; sonst steht einmal je Abend "Phasen schaltet die Einsatzleitung" da (der Abend beginnt um 04:00).
/// Alles auf dem Oberflächen-Thread, Zeiten über die Uhr des Aufrufers (Tests stellen sie vor).
/// </para>
/// </summary>
public sealed class Phasensteuerung
{
    /// <summary>Schlüssel der Phasenmeldungen in der Meldezeile.</summary>
    public const string Schluessel = "phase";

    private readonly IFunkDienst _dienst;
    private readonly Meldezeile _zeile;
    private readonly Func<long> _uhr;
    private readonly Func<DateTimeOffset> _jetzt;
    private int _richtung;
    private byte _ziel;
    private byte _erwartet;
    private long _seit;
    private long _gesendet;
    private bool _keineAntwortGemeldet;
    private DateOnly? _ohneRechtGezeigt;

    public Phasensteuerung(IFunkDienst dienst, Meldezeile zeile, Func<long> uhrMs, Func<DateTimeOffset>? jetzt = null)
    {
        ArgumentNullException.ThrowIfNull(dienst);
        ArgumentNullException.ThrowIfNull(zeile);
        ArgumentNullException.ThrowIfNull(uhrMs);
        _dienst = dienst;
        _zeile = zeile;
        _uhr = uhrMs;
        _jetzt = jetzt ?? (() => DateTimeOffset.Now);
    }

    public Phasenzustand Zustand { get; private set; }

    /// <summary>Ein eigener Wechsel für den Verlauf am Brett: "du: Phase Entern, 9 umgestellt".</summary>
    public event Action<string>? Verlauf;

    /// <summary>
    /// Jede Meldung hier beantwortet eine Phasentaste, die sonst nichts zeigt: darum bei jedem Druck (seit 0.11.1, <c>immer</c>; die
    /// Entprellung der Meldezeile gilt für sie nicht).
    /// </summary>
    private void Melden(string text, Meldeart art, int dauerMs) => _zeile.Zeigen(Schluessel, text, art, dauerMs, immer: true);

    /// <summary>Eine Phasentaste: +1 "Phase vor", -1 "Phase zurück".</summary>
    public async Task TasteAsync(int richtung)
    {
        richtung = Math.Sign(richtung);
        if (richtung == 0 || Zustand == Phasenzustand.Wartet) return;
        var jetzt = _uhr();
        if (Zustand == Phasenzustand.Scharf && jetzt - _seit > Meldezeiten.ScharfMs) Verfallen();
        var lage = _dienst.Lage;
        if (Zustand == Phasenzustand.Scharf)
        {
            if (richtung != _richtung)
            {
                Zustand = Phasenzustand.Ruhe;
                Melden("Nicht geschaltet: abgebrochen", Meldeart.Fehler, Meldezeiten.NichtGeschaltetMs);
                return;
            }
            // Prellender Knopf oder Doppeltipp: Ein zweiter Druck so kurz danach zählt nicht, die Phase bleibt scharf.
            if (jetzt - _seit < Meldezeiten.ZweiterDruckAbMs) return;
            await SchaltenAsync(_ziel, _erwartet).ConfigureAwait(true);
            return;
        }
        if (!Pruefen(lage)) return;
        if (Phasenrechnung.Naechste(lage, richtung) is not { } ziel)
        {
            Melden((richtung > 0 ? "Letzte Phase: " : "Erste Phase: ") + Phasenrechnung.Laufend(lage) + " läuft", Meldeart.Leitung,
                Meldezeiten.KeinePhasenMs);
            return;
        }
        Zustand = Phasenzustand.Scharf;
        _richtung = richtung;
        _ziel = ziel.Nr;
        _erwartet = lage.PhaseNr;
        _seit = jetzt;
        Melden(Phasenrechnung.ScharfText(lage, ziel), Meldeart.Leitung, Meldezeiten.ScharfMs);
    }

    /// <summary>Im Bildtakt: eine scharfe Wahl verfällt nach 5 s still, eine Anfrage ohne Antwort meldet sich nach 3 s.</summary>
    public void Takt()
    {
        var jetzt = _uhr();
        if (Zustand == Phasenzustand.Scharf && jetzt - _seit > Meldezeiten.ScharfMs) Verfallen();
        if (Zustand == Phasenzustand.Wartet && !_keineAntwortGemeldet && jetzt - _gesendet > Meldezeiten.AntwortMs)
        {
            _keineAntwortGemeldet = true;
            Melden("Keine Antwort - Stand folgt", Meldeart.Fehler, Meldezeiten.NichtGeschaltetMs);
        }
    }

    private void Verfallen()
    {
        Zustand = Phasenzustand.Ruhe;
        _zeile.Loesen(Schluessel);
    }

    /// <summary>Verbunden, ZUWEISEN und Phasen da; sonst der Satz dazu.</summary>
    private bool Pruefen(FunkLage lage)
    {
        if (!lage.IstAktiv)
        {
            Melden("Nicht geschaltet: keine Verbindung", Meldeart.Fehler, Meldezeiten.NichtGeschaltetMs);
            return false;
        }
        if (lage.Konto?.Einsatzleitung != true)
        {
            // Einmal je Abend, der um 04:00 beginnt; wer die Tasten nur vorab belegt hat, wird nicht bei jedem Druck erinnert.
            var abend = DateOnly.FromDateTime(_jetzt().AddHours(-4).Date);
            if (_ohneRechtGezeigt != abend)
            {
                _ohneRechtGezeigt = abend;
                Melden("Phasen schaltet die Einsatzleitung", Meldeart.Hinweis, Meldezeiten.OhneRechtMs);
            }
            return false;
        }
        if (!lage.ServerS2 || lage.Phasen.Count < 2)
        {
            // Seit 0.11.0 kürzer für eine Zeile (bis dahin "Keine Phasen - der Admin legt sie in Flotte > Phasen an").
            Melden("Keine Phasen angelegt (Flotte > Phasen)", Meldeart.Hinweis, Meldezeiten.KeinePhasenMs);
            return false;
        }
        return true;
    }

    private async Task SchaltenAsync(byte ziel, byte erwartet)
    {
        Zustand = Phasenzustand.Wartet;
        _gesendet = _uhr();
        _keineAntwortGemeldet = false;
        var lage = _dienst.Lage;
        var name = lage.PhaseVon(ziel) is { } p ? Phasenrechnung.Name(p) : ziel.ToString(CultureInfo.CurrentCulture);
        Melden("Phase " + name + ": schalte ...", Meldeart.Leitung, Meldezeiten.AntwortMs + Meldezeiten.NichtGeschaltetMs);
        Dienstantwort<PhaseSchaltenAntwort> a;
        try
        {
            a = await _dienst.PhaseSchaltenAsync(ziel, erwartet).ConfigureAwait(true);
        }
        finally
        {
            Zustand = Phasenzustand.Ruhe;
        }
        lage = _dienst.Lage;
        if (a.Ok)
        {
            var w = a.Wert!;
            var text = "Phase " + name + " läuft - " + w.Umgestellt.ToString(CultureInfo.CurrentCulture) + " umgestellt";
            if (w.Ausgelassen.Count > 0) text += ", ausgelassen: " + Phasenrechnung.Ausgelassene(w.Ausgelassen.Select(lage.HauptnameVon));
            Melden(text, Meldeart.Leitung, Meldezeiten.GeschaltetMs);
            Verlauf?.Invoke("du: Phase " + (lage.PhaseVon(ziel)?.Name ?? name) + ", " + w.Umgestellt.ToString(CultureInfo.CurrentCulture) + " umgestellt");
            return;
        }
        Melden("Nicht geschaltet: " + Grund(a, lage), Meldeart.Fehler, Meldezeiten.NichtGeschaltetMs);
    }

    /// <summary>
    /// Warum nicht, im Klartext; bei VERALTET mit dem, der schneller war, und der Phase, die jetzt läuft. Seit 0.11.0 für eine Zeile kürzer:
    /// "inzwischen läuft 3 Rückzug" (bis dahin "die Phase hat inzwischen gewechselt, 3 Rückzug läuft"), "zu schnell, kurz warten" (bis dahin
    /// "zu schnell hintereinander, kurz warten").
    /// </summary>
    private static string Grund(Dienstantwort<PhaseSchaltenAntwort> a, FunkLage lage)
    {
        if (a.Code == Fehlercode.Veraltet && a.Details is { ValueKind: System.Text.Json.JsonValueKind.Object } d
            && d.TryGetProperty("nr", out var n) && n.TryGetByte(out var nr))
        {
            var durch = d.TryGetProperty("durch", out var x) && x.TryGetUInt32(out var id) ? id : 0u;
            var phase = lage.PhaseVon(nr) is { } p ? Phasenrechnung.Name(p) : nr.ToString(CultureInfo.CurrentCulture);
            return durch == 0 ? "inzwischen läuft " + phase : lage.HauptnameVon(durch) + " war schneller, " + phase + " läuft";
        }
        return a.Code switch
        {
            Fehlercode.KeinRecht => "dafür fehlt die Einsatzleitung",
            Fehlercode.Unbekannt => "die Phase gibt es nicht (mehr)",
            Fehlercode.Grenze => "zu schnell, kurz warten",
            _ => a.Fehler?.TrimEnd('.') ?? "unbekannter Fehler",
        };
    }
}
