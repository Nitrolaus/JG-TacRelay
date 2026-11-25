// JG TacRelay - Maintainer: Sarevoc
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Funk.Protokoll.Steuerung;

/// <summary>Ein JSON-Body, der nach dem Deserialisieren seine Feldregeln aus 4.5 prüft.</summary>
public interface ISteuerBody
{
    /// <summary>Prüft Längen, Anzahlen, Bereiche und Formate; der erste Verstoß bleibt in <paramref name="p"/>.</summary>
    void Pruefen(ref Pruefung p);
}

/// <summary>
/// Sammelt den ersten Regelverstoß beim Prüfen eines Bodys. Längen- und Anzahlgrenzen ergeben <c>ZU_GROSS</c>
/// (4.9: "Grenze einer Rahmen- oder Feldlänge verletzt", mit Trennung), alle anderen Regeln <c>UNGUELTIG</c>.
/// </summary>
public record struct Pruefung
{
    /// <summary>Der erste Verstoß oder <c>null</c>.</summary>
    public Fehlercode? Fehler { get; private set; }

    /// <summary>Feld des ersten Verstoßes, für <c>Fehler.text</c>.</summary>
    public string? Feld { get; private set; }

    /// <summary>Noch kein Verstoß.</summary>
    public readonly bool Ok => Fehler is null;

    /// <summary>Setzt einen Verstoß, falls noch keiner vorliegt.</summary>
    public void Melden(Fehlercode code, string feld)
    {
        if (Fehler is not null) return;
        Fehler = code;
        Feld = feld;
    }

    /// <summary>Allgemeine Regel; Verstoß ergibt <c>UNGUELTIG</c>.</summary>
    public void Regel(bool erfuellt, string feld)
    {
        if (!erfuellt) Melden(Fehlercode.Ungueltig, feld);
    }

    /// <summary><c>str(max)</c>: höchstens <paramref name="max"/> Skalarwerte. <c>null</c> ist erlaubt (optional).</summary>
    public void Text(string? wert, int max, string feld)
    {
        if (wert is not null && Textregeln.Skalarwerte(wert) > max) Melden(Fehlercode.ZuGross, feld);
    }

    /// <summary>Pflichttext: vorhanden und höchstens <paramref name="max"/> Skalarwerte.</summary>
    public void PflichtText(string? wert, int max, string feld)
    {
        if (wert is null) Melden(Fehlercode.Ungueltig, feld);
        else Text(wert, max, feld);
    }

    /// <summary>Ganzzahlbereich, Verstoß ergibt <c>UNGUELTIG</c>.</summary>
    public void Bereich(long wert, long min, long max, string feld)
    {
        if (wert < min || wert > max) Melden(Fehlercode.Ungueltig, feld);
    }

    /// <summary>Optionaler Ganzzahlbereich.</summary>
    public void Bereich(long? wert, long min, long max, string feld)
    {
        if (wert is { } w) Bereich(w, min, max, feld);
    }

    /// <summary>Pflichtfeld, das nur je nach Zustand oder Operation Pflicht ist.</summary>
    public void Pflicht(bool vorhanden, string feld)
    {
        if (!vorhanden) Melden(Fehlercode.Ungueltig, feld);
    }

    /// <summary>Liste: höchstens <paramref name="max"/> Einträge, keine <c>null</c>-Einträge.</summary>
    public void Liste<T>(IReadOnlyList<T>? liste, int max, string feld)
    {
        if (liste is null) return;
        if (liste.Count > max)
        {
            Melden(Fehlercode.ZuGross, feld);
            return;
        }
        for (var i = 0; i < liste.Count; i++)
        {
            if (liste[i] is null)
            {
                Melden(Fehlercode.Ungueltig, feld);
                return;
            }
        }
    }

    /// <summary>Liste von Texten mit Längengrenze je Eintrag.</summary>
    public void Texte(IReadOnlyList<string>? liste, int maxAnzahl, int maxLaenge, string feld)
    {
        Liste(liste, maxAnzahl, feld);
        if (liste is null || !Ok) return;
        foreach (var t in liste) Text(t, maxLaenge, feld);
    }

    /// <summary>Liste von Objekten: Anzahl prüfen, dann jedes Objekt selbst.</summary>
    public void Objekte<T>(IReadOnlyList<T>? liste, int max, string feld) where T : ISteuerBody
    {
        Liste(liste, max, feld);
        if (liste is null || !Ok) return;
        foreach (var o in liste)
        {
            o.Pruefen(ref this);
            if (!Ok) return;
        }
    }

    /// <summary>Unterobjekt prüfen, falls vorhanden.</summary>
    public void Objekt<T>(T? wert) where T : ISteuerBody
    {
        if (wert is not null && Ok) wert.Pruefen(ref this);
    }

    /// <summary>Liste streng aufsteigend (damit ohne Doppelte).</summary>
    public void Aufsteigend<T>(IReadOnlyList<T>? liste, string feld) where T : IComparable<T>
    {
        if (liste is null) return;
        for (var i = 1; i < liste.Count; i++)
        {
            if (liste[i - 1].CompareTo(liste[i]) >= 0)
            {
                Melden(Fehlercode.Ungueltig, feld);
                return;
            }
        }
    }

    /// <summary>Kennung eines Schiffsnetzes: <c>[A-Z0-9_]</c>, 1-16 Zeichen (4.5 NetzListe).</summary>
    public void Kennung(string? wert, string feld)
    {
        if (wert is null) return;
        if (wert.Length > 16)
        {
            Melden(Fehlercode.ZuGross, feld);
            return;
        }
        var gut = wert.Length >= 1;
        foreach (var c in wert) gut &= c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_';
        Regel(gut, feld);
    }

    /// <summary>Base64 (Standardalphabet, mit Auffüllung) mit genau <paramref name="bytes"/> Byte Inhalt.</summary>
    public void Base64Genau(string? wert, int bytes, string feld)
    {
        if (wert is null) return;
        Regel(Base64Laenge(wert) == bytes, feld);
    }

    /// <summary>Base64 (Standardalphabet, mit Auffüllung) beliebiger Länge.</summary>
    public void Base64(string? wert, string feld)
    {
        if (wert is null) return;
        Regel(Base64Laenge(wert) >= 0, feld);
    }

    /// <summary>Discord-Snowflake als Zeichenkette: 1-20 Ziffern, ohne führende Null, passt in u64.</summary>
    public void Snowflake(string? wert, string feld)
    {
        if (wert is null) return;
        if (wert.Length > 20)
        {
            Melden(Fehlercode.ZuGross, feld);
            return;
        }
        var gut = wert.Length >= 1 && (wert.Length == 1 || wert[0] != '0');
        foreach (var c in wert) gut &= c is >= '0' and <= '9';
        Regel(gut && ulong.TryParse(wert, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _), feld);
    }

    /// <summary>Freier JSON-Wert mit Größengrenze in Byte (Rohform wie empfangen).</summary>
    public void FreiesJson(JsonElement? wert, int maxBytes, string feld)
    {
        if (wert is not { } w) return;
        if (JsonMarshal.GetRawUtf8Value(w).Length > maxBytes) Melden(Fehlercode.ZuGross, feld);
    }

    /// <summary>Freier JSON-Wert muss ein Objekt sein.</summary>
    public void IstObjekt(JsonElement? wert, string feld)
    {
        if (wert is { } w) Regel(w.ValueKind == JsonValueKind.Object, feld);
    }

    /// <summary>Byte-Länge des Base64-Inhalts oder -1, wenn es kein kanonisches Base64 ist.</summary>
    internal static int Base64Laenge(string wert)
    {
        if (wert.Length == 0) return 0;
        if (wert.Length % 4 != 0) return -1;
        Span<byte> utf8 = wert.Length <= 1024 ? stackalloc byte[wert.Length] : new byte[wert.Length];
        for (var i = 0; i < wert.Length; i++)
        {
            var c = wert[i];
            if (c > 0x7F) return -1;
            utf8[i] = (byte)c;
        }
        if (!System.Buffers.Text.Base64.IsValid(utf8, out var laenge)) return -1;
        // IsValid duldet Leerraum; der Steuerkanal nicht.
        foreach (var b in utf8)
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') return -1;
        }
        return laenge;
    }
}
