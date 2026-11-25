// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Sprache;

/// <summary>Kennzeichen-Byte der Sprachpakete (5.6). Bits 4-7 sind reserviert.</summary>
[Flags]
public enum Kennzeichen : byte
{
    None = 0,

    /// <summary>hoch: erstes Paket einer Sprechphase (Hinweis). runter: vom Server je Empfänger gesetzt.</summary>
    Beginn = 0x01,

    /// <summary>Ende-Markierung, ohne Opus.</summary>
    Ende = 0x02,

    /// <summary>Frame aus dem Vorlauf-Puffer (Burst).</summary>
    Preroll = 0x04,

    /// <summary>1 = Sprachaktivierung, 0 = Sprechtaste.</summary>
    Va = 0x08,
}

/// <summary>Masken zum Kennzeichen-Byte (5.6, 6.4).</summary>
public static class KennzeichenMasken
{
    /// <summary>Reservierte Bits: Sender 0, der Server setzt sie beim Weiterleiten auf 0.</summary>
    public const byte Reserviert = 0xF0;

    /// <summary>Was der Server beim Stempeln vom Sender übernimmt: ENDE, PREROLL, VA (6.4).</summary>
    public const byte Uebernommen = 0x0E;
}

/// <summary>Zielart im SPRACHE_HOCH (5.4).</summary>
public enum Zielart : byte
{
    Netz = 1,
    Durchsage = 2,
}

/// <summary>Art im SPRACHE_RUNTER (5.5), vom Server bestimmt.</summary>
public enum Spruchart : byte
{
    Netz = 1,
    Durchsage = 2,
    Global = 3,
}

/// <summary>Ergebnis der Formprüfung eines Sprach-Klartexts (5.4, 5.5, 6.3 "Form pruefen").</summary>
public enum Formfehler : byte
{
    Keiner = 0,
    ZuKurz,
    ZielartUnbekannt,
    ZielAnzahlUngueltig,
    ZieleNichtAufsteigend,
    GlobalAlsDurchsageziel,
    ArtUnbekannt,
    NetzAnzahlUngueltig,
    EndeMitOpus,
    EndeMitPreroll,
    OpusFehlt,
    OpusZuLang,

    /// <summary>Opus-TOC ergibt nicht genau 20 ms mono (5.13).</summary>
    OpusUngueltig,
}
