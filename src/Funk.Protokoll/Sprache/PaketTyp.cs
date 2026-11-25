// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Sprache;

/// <summary>Pakettypen des Sprachkanals (5.3).</summary>
public enum PaketTyp : byte
{
    Anmeldung = 0x01,
    PfadPruefung = 0x02,
    PfadAntwort = 0x03,
    Keepalive = 0x04,
    KeepaliveEcho = 0x05,
    SpracheHoch = 0x10,
    SpracheRunter = 0x11,
}

/// <summary>hoch = Client zum Server, runter = Server zum Client (0 Konventionen).</summary>
public enum Richtung : byte
{
    Hoch = 1,
    Runter = 2,
}

/// <summary>Regeln je Pakettyp aus 5.3 bis 5.5.</summary>
public static class PaketTypen
{
    public static bool IstBekannt(PaketTyp typ) =>
        typ is >= PaketTyp.Anmeldung and <= PaketTyp.KeepaliveEcho or PaketTyp.SpracheHoch or PaketTyp.SpracheRunter;

    public static bool IstSprache(PaketTyp typ) => typ is PaketTyp.SpracheHoch or PaketTyp.SpracheRunter;

    /// <summary>Richtung eines bekannten Typs.</summary>
    public static Richtung RichtungVon(PaketTyp typ) => typ switch
    {
        PaketTyp.Anmeldung or PaketTyp.PfadAntwort or PaketTyp.Keepalive or PaketTyp.SpracheHoch => Richtung.Hoch,
        PaketTyp.PfadPruefung or PaketTyp.KeepaliveEcho or PaketTyp.SpracheRunter => Richtung.Runter,
        _ => throw new ArgumentOutOfRangeException(nameof(typ), typ, "Unbekannter Pakettyp."),
    };

    /// <summary>Ob die Datagrammlänge zum Typ passt (5.9 Schritt 2): 0x01-0x05 genau 48, 0x10 41-569, 0x11 45-477.</summary>
    public static bool LaengePasst(PaketTyp typ, int datagrammLaenge) => typ switch
    {
        PaketTyp.SpracheHoch => datagrammLaenge is >= Sprachkanal.HochMin and <= Sprachkanal.HochMax,
        PaketTyp.SpracheRunter => datagrammLaenge is >= Sprachkanal.RunterMin and <= Sprachkanal.RunterMax,
        _ => IstBekannt(typ) && datagrammLaenge == Sprachkanal.FestesDatagramm,
    };

    /// <summary>Wie <see cref="LaengePasst"/>, aber für die Klartextlänge.</summary>
    public static bool KlartextLaengePasst(PaketTyp typ, int klartextLaenge) =>
        LaengePasst(typ, klartextLaenge + Sprachkanal.Ueberhang);
}
