// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Server.Lage;

/// <summary>
/// Eingabe der Weiterleitung: "Paket von Sitzung X mit Ziel Y", der Klartext eines SPRACHE_HOCH (5.4)
/// nach bestandener Empfangsprüfung (5.9). Die Spans zeigen in den Empfangspuffer; nichts wird kopiert.
/// </summary>
public readonly ref struct Sprachpaket
{
    public Sprachpaket(uint sitzung, uint frameNr, ushort sprechphase, Paketkennzeichen kennzeichen, Sendeart zielart,
        ReadOnlySpan<ushort> ziele, ReadOnlySpan<byte> opus)
    {
        Sitzung = sitzung;
        FrameNr = frameNr;
        Sprechphase = sprechphase;
        Kennzeichen = kennzeichen;
        Zielart = zielart;
        Ziele = ziele;
        Opus = opus;
    }

    public uint Sitzung { get; }
    public uint FrameNr { get; }
    public ushort Sprechphase { get; }
    public Paketkennzeichen Kennzeichen { get; }

    /// <summary>Roh aus dem Paket; ein Wert außer 1 und 2 ist ein Formfehler.</summary>
    public Sendeart Zielart { get; }

    /// <summary>Netz-IDs, laut 5.4 streng aufsteigend.</summary>
    public ReadOnlySpan<ushort> Ziele { get; }

    public ReadOnlySpan<byte> Opus { get; }

    public bool IstEnde => (Kennzeichen & Paketkennzeichen.Ende) != 0;

    public bool IstVa => (Kennzeichen & Paketkennzeichen.Va) != 0;
}

/// <summary>
/// Ausgabe der Weiterleitung: "an Empfänger Z mit Stempel S" (5.5). Die Netz-IDs und die Opus-Nutzlast
/// kommen getrennt als Span. <see cref="Erzeugt"/> markiert ein vom Server erzeugtes ENDE (6.4).
/// </summary>
public readonly record struct Stempel(
    uint Empfaenger,
    uint Absender,
    uint FrameNr,
    ushort Sprechphase,
    Paketkennzeichen Kennzeichen,
    Stempelart Art,
    bool Erzeugt);

/// <summary>Eintrag in SendeFehler (4.5). <see cref="Umstellung"/> ist die Version der Umstellung bei ZIEL_ENTZOGEN.</summary>
public readonly record struct SendeFehlerEintrag(SendeFehlerCode Code, ushort NetzId, uint Durch, long Umstellung = 0);

/// <summary>Ergebnis eines Pakets: verworfen mit Grund, oder Zahl der Kopien und der gestrichenen Ziele.</summary>
public readonly record struct Weiterleitungsergebnis(bool Verworfen, SendeFehlerCode Grund, int Kopien, int Gestrichen);

/// <summary>
/// Senke der Weiterleitung. Versiegeln, Pacer und Steuerkanal liegen dahinter. Die Spans gelten nur während des
/// Aufrufs. Wird im Relay-Thread aufgerufen und muss allokationsfrei bleiben, wenn der Heißpfad es sein soll.
/// </summary>
public interface IWeiterleitungsZiel
{
    /// <summary>Eine Kopie an einen Empfänger; leere Opus-Nutzlast bei ENDE.</summary>
    void Sprache(in Stempel stempel, ReadOnlySpan<ushort> netze, ReadOnlySpan<byte> opus);

    /// <summary>DurchsageStart (4.5): <paramref name="netze"/> für diesen Empfänger, leer heißt an alle.</summary>
    void DurchsageStart(uint empfaenger, uint sprecher, Stempelart art, ReadOnlySpan<ushort> netze, ushort sprechphase);

    /// <summary>DurchsageEnde (4.5).</summary>
    void DurchsageEnde(uint empfaenger, uint sprecher, ushort sprechphase);

    /// <summary>SendeFehler an den Sender (4.5), schon auf höchstens 1 je Sekunde gedrosselt.</summary>
    void SendeFehler(uint sitzung, ReadOnlySpan<SendeFehlerEintrag> fehler, bool teilweise, uint verworfen);

    /// <summary>Anlass für Meldung{RATE} an alle mit ZUWEISEN (6.3), höchstens 1 je Sekunde und Sender.</summary>
    void RateUeberschritten(uint sitzung, uint kontoId);
}

/// <summary>
/// Ob an einen Empfänger zugestellt wird: gebundene Adresse oder Tunnel, kein Strom-Abwurf (6.4, 7.4).
/// Gehört dem Relay-Thread; ohne Angabe gilt jeder aktive Empfänger als erreichbar.
/// </summary>
public interface IZustellung
{
    bool Zustellbar(uint empfaenger, uint absenderSitzung, ushort sprechphase, Stempelart art);
}
