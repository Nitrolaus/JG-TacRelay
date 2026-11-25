// JG TacRelay - Maintainer: Sarevoc
using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Funk.Protokoll.Steuerung;

/// <summary>Ergebnis beim Lesen eines Bodys: entweder <see cref="Body"/> oder ein Fehlercode.</summary>
public readonly record struct DekodierErgebnis(Fehlercode? Fehler, string? Feld, object? Body)
{
    /// <summary>Gelesen und alle Regeln erfüllt.</summary>
    public bool Ok => Fehler is null;

    /// <summary>Der Fehler trennt die Verbindung (4.9).</summary>
    public bool Trennen => Fehler is { } f && Fehlercodes.Trennt(f);

    internal static DekodierErgebnis Mit(Fehlercode code, string? feld = null) => new(code, feld, null);
}

/// <summary>
/// JSON-Kodierung der Bodies nach 4.3: System.Text.Json mit Source-Generator, kompakt, Felder in
/// Katalogreihenfolge, Nicht-ASCII unmaskiert (<see cref="RoheZeichenkette"/>); streng lesen über
/// <see cref="JsonVorpruefung"/>, danach Pflichtfelder und Typen über den Deserialisierer, danach die Feldregeln.
/// </summary>
public static class SteuerJson
{
    private static readonly JsonWriterOptions SchreibOptionen = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = false,
    };

    internal static SteuerJsonKontext Kontext { get; } = new(new JsonSerializerOptions(SteuerJsonKontext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>Die wirksamen Serialisierungsoptionen.</summary>
    public static JsonSerializerOptions Optionen => Kontext.Options;

    /// <summary>Typinformation für einen Body-Typ.</summary>
    public static JsonTypeInfo<T> Info<T>() =>
        (JsonTypeInfo<T>)(Kontext.GetTypeInfo(typeof(T)) ?? throw new InvalidOperationException($"Kein Steuer-Body: {typeof(T).Name}"));

    /// <summary>Liest und prüft einen Body: Vorlauf, Deserialisieren, Feldregeln.</summary>
    public static DekodierErgebnis Lesen(ReadOnlySpan<byte> body, JsonTypeInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (JsonVorpruefung.Pruefen(body) is { } vorab) return DekodierErgebnis.Mit(vorab);
        object? wert;
        try
        {
            wert = JsonSerializer.Deserialize(body, info);
        }
        catch (JsonException e)
        {
            // Wohlgeformt (der Vorlauf hat bestanden), aber Pflichtfeld fehlt, Typ oder Bereich falsch.
            return DekodierErgebnis.Mit(Fehlercode.Ungueltig, Kuerzen(e.Path));
        }
        if (wert is not ISteuerBody pruefbar) return DekodierErgebnis.Mit(Fehlercode.Ungueltig);
        var p = new Pruefung();
        pruefbar.Pruefen(ref p);
        return p.Fehler is { } f ? DekodierErgebnis.Mit(f, p.Feld) : new DekodierErgebnis(null, null, wert);
    }

    /// <summary>Höchstlänge von <see cref="DekodierErgebnis.Feld"/> in UTF-16-Zeichen; passt sicher in <c>Fehler.text</c> (str(200)).</summary>
    public const int FeldMax = 100;

    /// <summary>
    /// Der JSON-Pfad einer Ausnahme enthält Schlüssel aus dem Body (etwa aus <c>schiffe.netze</c>) und kann damit
    /// fast so lang werden wie der Rahmen. Gekürzt wird ohne ein Surrogatpaar zu zerschneiden.
    /// </summary>
    private static string? Kuerzen(string? pfad)
    {
        if (pfad is null || pfad.Length <= FeldMax) return pfad;
        var n = char.IsHighSurrogate(pfad[FeldMax - 1]) ? FeldMax - 1 : FeldMax;
        return pfad[..n];
    }

    /// <summary>Liest einen Body als <typeparamref name="T"/>; wirft bei Regelverstoß. Für Werkzeuge und Tests.</summary>
    public static T LesenOderWerfen<T>(ReadOnlySpan<byte> body) where T : class
    {
        var e = Lesen(body, Info<T>());
        return e.Body as T ?? throw new InvalidDataException($"Body ungültig: {e.Fehler} {e.Feld}");
    }

    /// <summary>Schreibt einen Body kompakt nach <paramref name="ziel"/>.</summary>
    public static void Schreiben<T>(IBufferWriter<byte> ziel, T wert)
    {
        using var writer = new Utf8JsonWriter(ziel, SchreibOptionen);
        JsonSerializer.Serialize(writer, wert, Info<T>());
    }

    /// <summary>Schreibt einen Body über einen vorhandenen Writer (wird auf <paramref name="ziel"/> zurückgesetzt).</summary>
    internal static void Schreiben<T>(Utf8JsonWriter writer, IBufferWriter<byte> ziel, T wert)
    {
        writer.Reset(ziel);
        JsonSerializer.Serialize(writer, wert, Info<T>());
        writer.Flush();
    }

    /// <summary>Neuer Writer mit den Schreiboptionen des Steuerkanals.</summary>
    internal static Utf8JsonWriter NeuerWriter(IBufferWriter<byte> ziel) => new(ziel, SchreibOptionen);

    /// <summary>Serialisiert einen Body in ein neues Byte-Feld.</summary>
    public static byte[] Serialisieren<T>(T wert)
    {
        var puffer = new ArrayBufferWriter<byte>(256);
        Schreiben(puffer, wert);
        return puffer.WrittenSpan.ToArray();
    }

    /// <summary>Baut einen freien JSON-Wert (etwa <c>Fehler.details</c>) aus einem Body-Typ.</summary>
    public static JsonElement Element<T>(T wert)
    {
        var bytes = Serialisieren(wert);
        using var dokument = JsonDocument.Parse(bytes);
        return dokument.RootElement.Clone();
    }
}

/// <summary>Source-Generator-Kontext aller Steuer-Bodies.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowDuplicateProperties = false,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    AllowTrailingCommas = false,
    MaxDepth = JsonVorpruefung.MaxTiefe,
    NumberHandling = JsonNumberHandling.Strict,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    Converters = [typeof(RoheZeichenkette), typeof(RohesJson)])]
[JsonSerializable(typeof(LeererBody))]
[JsonSerializable(typeof(ZustandsVersionBody))]
[JsonSerializable(typeof(Hello))]
[JsonSerializable(typeof(Welcome))]
[JsonSerializable(typeof(Bye))]
[JsonSerializable(typeof(PingAnfrage))]
[JsonSerializable(typeof(PingAntwort))]
[JsonSerializable(typeof(Fehler))]
[JsonSerializable(typeof(RegistrierungAnfrage))]
[JsonSerializable(typeof(RegistrierungAntwort))]
[JsonSerializable(typeof(GeraetStatus))]
[JsonSerializable(typeof(GeraeteListeAnfrage))]
[JsonSerializable(typeof(GeraeteListeAntwort))]
[JsonSerializable(typeof(GeraetEntscheidenAnfrage))]
[JsonSerializable(typeof(GeraetEntfernenAnfrage))]
[JsonSerializable(typeof(GeraetEntfernenAntwort))]
[JsonSerializable(typeof(KontoVerwaltungAnfrage))]
[JsonSerializable(typeof(VerknuepfungBestaetigenAnfrage))]
[JsonSerializable(typeof(VerknuepfungBestaetigenAntwort))]
[JsonSerializable(typeof(NetzListe))]
[JsonSerializable(typeof(Netz))]
[JsonSerializable(typeof(Schiff))]
[JsonSerializable(typeof(Rolle))]
[JsonSerializable(typeof(Mitgliedschaften))]
[JsonSerializable(typeof(Praesenz))]
[JsonSerializable(typeof(SendeFehler))]
[JsonSerializable(typeof(SendeauswahlSetzenAnfrage))]
[JsonSerializable(typeof(HoerenSetzenAnfrage))]
[JsonSerializable(typeof(EinsteigenRolleSchiffAnfrage))]
[JsonSerializable(typeof(VerschiebenAnfrage))]
[JsonSerializable(typeof(VerschiebenAntwort))]
[JsonSerializable(typeof(StummschaltenAnfrage))]
[JsonSerializable(typeof(FeldSetzenAnfrage))]
[JsonSerializable(typeof(SatzAnwendenAnfrage))]
[JsonSerializable(typeof(SatzAnwendenAntwort))]
[JsonSerializable(typeof(PlaetzeFreigebenAntwort))]
[JsonSerializable(typeof(AufstellungSpeichernAnfrage))]
[JsonSerializable(typeof(AufstellungsVerweis))]
[JsonSerializable(typeof(AufstellungListeAnfrage))]
[JsonSerializable(typeof(AufstellungListeAntwort))]
[JsonSerializable(typeof(AufstellungAnwendenAnfrage))]
[JsonSerializable(typeof(AufstellungAnwendenAntwort))]
[JsonSerializable(typeof(RaidHelperImportAnfrage))]
[JsonSerializable(typeof(RaidHelperImportAntwort))]
[JsonSerializable(typeof(RegelTabelleAnfrage))]
[JsonSerializable(typeof(RegelTabelleAntwort))]
[JsonSerializable(typeof(RaidHelperEventsAntwort))]
[JsonSerializable(typeof(AufstellungGeaendert))]
[JsonSerializable(typeof(DurchsageStart))]
[JsonSerializable(typeof(DurchsageEnde))]
[JsonSerializable(typeof(AnAus))]
[JsonSerializable(typeof(Sprechanzeige))]
[JsonSerializable(typeof(PegelVoreinstellungen))]
[JsonSerializable(typeof(PegelSetzenAnfrage))]
[JsonSerializable(typeof(AblageAnfrage))]
[JsonSerializable(typeof(AblageAntwort))]
[JsonSerializable(typeof(KeyRotationAnfrage))]
[JsonSerializable(typeof(UdpStatus))]
[JsonSerializable(typeof(TunnelModusAnfrage))]
[JsonSerializable(typeof(EmpfangsBericht))]
[JsonSerializable(typeof(VerlustHinweis))]
[JsonSerializable(typeof(VerwaltungAnfrage))]
[JsonSerializable(typeof(IdObjekt))]
[JsonSerializable(typeof(EinstellungObjekt))]
[JsonSerializable(typeof(ServerLast))]
[JsonSerializable(typeof(Meldung))]
[JsonSerializable(typeof(MindestAngabe))]
[JsonSerializable(typeof(ProfilSetzenAnfrage))]
[JsonSerializable(typeof(ProfilSetzenAntwort))]
[JsonSerializable(typeof(JournalAnfrage))]
[JsonSerializable(typeof(JournalAntwort))]
[JsonSerializable(typeof(RollenkatalogAnfrage))]
[JsonSerializable(typeof(RollenkatalogAntwort))]
[JsonSerializable(typeof(MitgliedAnfrage))]
[JsonSerializable(typeof(MitgliedAntwort))]
[JsonSerializable(typeof(LieblingsrollenAnfrage))]
[JsonSerializable(typeof(LieblingsrollenAntwort))]
[JsonSerializable(typeof(UntergildeAnfrage))]
[JsonSerializable(typeof(UntergildeAntwort))]
[JsonSerializable(typeof(MitgliedskarteAnfrage))]
[JsonSerializable(typeof(MitgliedskarteAntwort))]
[JsonSerializable(typeof(AntraegeAblehnenAnfrage))]
[JsonSerializable(typeof(AntraegeAblehnenAntwort))]
[JsonSerializable(typeof(AbzeichenAnfrage))]
[JsonSerializable(typeof(AbzeichenAntwort))]
[JsonSerializable(typeof(MeldungSendenAnfrage))]
[JsonSerializable(typeof(BereitMeldenAnfrage))]
[JsonSerializable(typeof(Meldestand))]
[JsonSerializable(typeof(BereitStand))]
[JsonSerializable(typeof(AdminNonceAntwort))]
[JsonSerializable(typeof(AdminAnmeldungAnfrage))]
[JsonSerializable(typeof(AdminSitzungAntwort))]
[JsonSerializable(typeof(AdminBestaetigenAnfrage))]
[JsonSerializable(typeof(AdminEinrichtungAnfrage))]
[JsonSerializable(typeof(AdminEinrichtungAntwort))]
[JsonSerializable(typeof(BeauftragungAnfrage))]
[JsonSerializable(typeof(BeauftragungAntwort))]
[JsonSerializable(typeof(KontenVerzeichnisAntwort))]
[JsonSerializable(typeof(Belegung))]
[JsonSerializable(typeof(SelbstwahlObjekt))]
[JsonSerializable(typeof(VerwaltungsPaket))]
[JsonSerializable(typeof(FunkermodusAnfrage))]
[JsonSerializable(typeof(FunkermodusAntwort))]
[JsonSerializable(typeof(PhaseSchaltenAnfrage))]
[JsonSerializable(typeof(PhaseSchaltenAntwort))]
[JsonSerializable(typeof(PhaseGeschaltet))]
[JsonSerializable(typeof(NetzPhase))]
[JsonSerializable(typeof(RolleWaehlenAnfrage))]
[JsonSerializable(typeof(KanalSperreAnfrage))]
[JsonSerializable(typeof(KanalSperreAntwort))]
internal sealed partial class SteuerJsonKontext : JsonSerializerContext;
