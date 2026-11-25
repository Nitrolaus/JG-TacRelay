// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Text.Json;
using Funk.Protokoll.Steuerung;

namespace Funk.Server.Kern;

/// <summary>
/// Antwort auf eine Anfrage des Clients: entweder ein Body des Antworttyps (gleicher Typ, Flag ANTWORT) oder
/// <c>Fehler</c> mit Code (4.2). Der Body wird mit seinem statischen Typ geschrieben, den der Source-Generator kennt.
/// </summary>
public sealed class Antwort
{
    private readonly Func<RahmenSchreiber, Nachrichtentyp, uint, CancellationToken, ValueTask>? _schreiben;

    private Antwort(Func<RahmenSchreiber, Nachrichtentyp, uint, CancellationToken, ValueTask>? schreiben, object? body,
        Fehlercode? fehler, string? text, JsonElement? details)
    {
        _schreiben = schreiben;
        Body = body;
        Fehler = fehler;
        Text = text;
        Details = details;
    }

    public object? Body { get; }
    public Fehlercode? Fehler { get; }
    public string? Text { get; }
    public JsonElement? Details { get; }

    public static Antwort Mit<T>(T body) where T : class =>
        new((w, typ, nr, ct) => w.SchreibenAsync(typ, Rahmen.FlagAntwort, nr, body, ct), body, null, null, null);

    public static Antwort Leer() => Mit(LeererBody.Wert);

    public static Antwort Fehl(Fehlercode code, string? text = null, JsonElement? details = null) => new(null, null, code, text, details);

    /// <summary>Schreibt die Antwort mit Anfrage-Nr <paramref name="nr"/>.</summary>
    public ValueTask SchreibenAsync(RahmenSchreiber schreiber, Nachrichtentyp typ, uint nr, CancellationToken abbruch)
    {
        ArgumentNullException.ThrowIfNull(schreiber);
        if (_schreiben is not null) return _schreiben(schreiber, typ, nr, abbruch);
        var fehler = new Protokoll.Steuerung.Fehler { Code = Fehler ?? Fehlercode.Intern, Text = Text, BezugTyp = (ushort)typ, Details = Details };
        return schreiber.SchreibenAsync(Nachrichtentyp.Fehler, Rahmen.FlagAntwort, nr, fehler, abbruch);
    }

    /// <summary>Baut ein freies JSON-Objekt für <c>Fehler.details</c> (höchstens 4 KiB).</summary>
    public static JsonElement DetailsBauen(Action<Utf8JsonWriter> schreiben)
    {
        ArgumentNullException.ThrowIfNull(schreiben);
        var puffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(puffer))
        {
            w.WriteStartObject();
            schreiben(w);
            w.WriteEndObject();
        }
        using var dokument = JsonDocument.Parse(puffer.WrittenMemory);
        return dokument.RootElement.Clone();
    }

    public override string ToString() => Fehler is { } f ? "Fehler " + Fehlercodes.Drahtname(f) : "Antwort " + Body?.GetType().Name;
}
