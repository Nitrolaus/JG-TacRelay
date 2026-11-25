// JG TacRelay - Maintainer: Sarevoc
using Funk.Klang.Verarbeitung;

namespace Funk.Klang.Maschine;

/// <summary>
/// Ein kodiertes Sprachpaket der eigenen Aufnahme (20 ms Opus, höchstens 400 Byte) oder die Ende-Markierung einer
/// Sprechphase, gültig nur während des Aufrufs von <see cref="SendeAusgang"/>.
/// <para>
/// So bedient der Transport (Funk.ClientKern, Sprechkanal) den Ausgang: Bei <see cref="Beginn"/> eine Phase beginnen
/// (Sprachaktivierung laut <see cref="Sprachaktivierung"/>, Aufnahmezeitpunkt <see cref="AufnahmeTakte"/>), jedes Paket
/// mit Opus senden (<see cref="Vorlauf"/> als Pre-roll), bei <see cref="Ende"/> die Phase beenden (ENDE dreifach).
/// Frame-Nummern und Sprechphase vergibt der Transport.
/// </para>
/// </summary>
public readonly ref struct SendeRahmen
{
    public SendeRahmen(ReadOnlySpan<byte> opus, Sprachkennzeichen kennzeichen, long aufnahmeTakte)
    {
        Opus = opus;
        Kennzeichen = kennzeichen;
        AufnahmeTakte = aufnahmeTakte;
    }

    /// <summary>Opus-Nutzlast; leer bei <see cref="Ende"/>.</summary>
    public ReadOnlySpan<byte> Opus { get; }

    /// <summary>Kennzeichen nach FUNKPROTOKOLL-v1 5.6 (BEGINN, ENDE, PREROLL, VA).</summary>
    public Sprachkennzeichen Kennzeichen { get; }

    /// <summary>Ungefährer Aufnahmezeitpunkt des Pakets (Stopwatch-Takte, Anfang der 20 ms).</summary>
    public long AufnahmeTakte { get; }

    public bool Beginn => (Kennzeichen & Sprachkennzeichen.Beginn) != 0;

    public bool Ende => (Kennzeichen & Sprachkennzeichen.Ende) != 0;

    public bool Vorlauf => (Kennzeichen & Sprachkennzeichen.Preroll) != 0;

    public bool Sprachaktivierung => (Kennzeichen & Sprachkennzeichen.Sprachaktivierung) != 0;
}

/// <summary>
/// Sende-Ausgang der Klangmaschine. Wird im Melde-Thread der Maschine aufgerufen, nie im Audio-Thread; darf sperren und
/// allokieren, sollte aber zügig zurückkehren, weil die Pegelmeldungen denselben Thread benutzen.
/// </summary>
public delegate void SendeAusgang(in SendeRahmen rahmen);
