// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

/// <summary>Was mit einer älteren Sitzung desselben Kontos geschieht (4.6).</summary>
public enum Verdraengungsart : byte
{
    /// <summary>Keine ältere Sitzung: nichts zu tun.</summary>
    KeineAlteSitzung,
    /// <summary>Gleiches Gerät: alte Sitzung endet mit <c>Bye{ERSETZT}</c>, zählt nicht als Verdrängung.</summary>
    Ersetzt,
    /// <summary>Anderes Gerät: alte Sitzung endet mit <c>Bye{VERDRAENGT, durch}</c>.</summary>
    Verdraengt,
    /// <summary>
    /// Zu häufiges Verdrängen: die neue Verbindung endet mit <c>VERDRAENGEN_GESPERRT</c>, die laufende Sitzung
    /// bleibt, bis ein Admin entscheidet oder 30 min vergangen sind.
    /// </summary>
    Gesperrt,
}

/// <summary>Ergebnis der Verdrängungsprüfung.</summary>
/// <param name="Art">Was mit alter und neuer Sitzung geschieht.</param>
/// <param name="MeldungHaeufig"><c>Meldung{VERDRAENGUNG_HAEUFIG}</c> an <c>GERAETE_FREIGEBEN</c> senden.</param>
public readonly record struct Verdraengung(Verdraengungsart Art, bool MeldungHaeufig);

/// <summary>
/// Buch über Verdrängungen je Konto (4.6): Mehr als 2 Verdrängungen zwischen verschiedenen Geräten eines Kontos
/// in 5 min lösen <c>Meldung{VERDRAENGUNG_HAEUFIG}</c> aus; weitere verdrängende Verbindungen lehnt der Server
/// dann mit <c>VERDRAENGEN_GESPERRT</c> ab, bis ein Admin entscheidet oder 30 min vergangen sind.
/// Aufzurufen genau dann, wenn eine Verbindung eines FREIGEGEBEN-Geräts AKTIV_SYNC erreicht
/// (<see cref="Uebergang.SyncBeginnen"/>), und zwar vor dem Senden von Welcome. Threadsicher: Das Buch gilt
/// serverweit, und jede Verbindung fragt es aus ihrer eigenen Aufgabe heraus.
/// </summary>
public sealed class Verdraengungsbuch
{
    /// <summary>Beobachtungsfenster.</summary>
    public const long FensterMs = 5 * 60_000;
    /// <summary>So viele Verdrängungen im Fenster sind noch unauffällig.</summary>
    public const int MaxImFenster = 2;
    /// <summary>Dauer der Sperre ohne Admin-Entscheidung.</summary>
    public const long SperreMs = 30 * 60_000;

    private readonly Dictionary<uint, Eintrag> _konten = [];
    private readonly Lock _sperre = new();

    /// <summary>Prüft eine neue Sitzung eines Kontos, das schon eine hat oder nicht.</summary>
    /// <param name="kontoId">Konto der neuen Verbindung.</param>
    /// <param name="alteSitzung">Das Konto hat noch eine laufende Sitzung.</param>
    /// <param name="gleichesGeraet">Die alte Sitzung hat denselben Fingerabdruck.</param>
    /// <param name="jetztMs">Monotone Uhr.</param>
    public Verdraengung NeueSitzung(uint kontoId, bool alteSitzung, bool gleichesGeraet, long jetztMs)
    {
        if (!alteSitzung) return new Verdraengung(Verdraengungsart.KeineAlteSitzung, false);
        if (gleichesGeraet) return new Verdraengung(Verdraengungsart.Ersetzt, false);
        lock (_sperre) return Verdraengen(kontoId, jetztMs);
    }

    private Verdraengung Verdraengen(uint kontoId, long jetztMs)
    {
        if (!_konten.TryGetValue(kontoId, out var e))
        {
            e = new Eintrag();
            _konten[kontoId] = e;
        }
        if (e.GesperrtBisMs > jetztMs) return new Verdraengung(Verdraengungsart.Gesperrt, false);

        e.Zeiten.RemoveAll(t => jetztMs - t >= FensterMs);
        e.Zeiten.Add(jetztMs);
        if (e.Zeiten.Count <= MaxImFenster) return new Verdraengung(Verdraengungsart.Verdraengt, false);

        e.GesperrtBisMs = jetztMs + SperreMs;
        e.Zeiten.Clear();
        return new Verdraengung(Verdraengungsart.Verdraengt, true);
    }

    /// <summary>Ein Admin hat entschieden: die Sperre für dieses Konto endet sofort.</summary>
    public void AdminEntscheidet(uint kontoId)
    {
        lock (_sperre) _konten.Remove(kontoId);
    }

    /// <summary>Ob verdrängende Verbindungen dieses Kontos gerade abgelehnt werden.</summary>
    public bool IstGesperrt(uint kontoId, long jetztMs)
    {
        lock (_sperre) return _konten.TryGetValue(kontoId, out var e) && e.GesperrtBisMs > jetztMs;
    }

    private sealed class Eintrag
    {
        public List<long> Zeiten { get; } = [];
        public long GesperrtBisMs { get; set; } = long.MinValue;
    }
}
