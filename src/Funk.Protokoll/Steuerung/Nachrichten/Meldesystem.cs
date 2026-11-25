// JG TacRelay - Maintainer: Sarevoc
namespace Funk.Protokoll.Steuerung;

// Meldesystem (0.12.0, Sarevoc 11.10. und 16.10.2026, M1-M24): Befehle, Zustände, Ampel, Countdown und Bereit-Abfrage als Grafik an
// alle, die online sind, dazu die Textmeldung (nur Text, ohne Grafik). Zwei Pflichtanfragen (0x0026, 0x0027) nur an Server ab
// 0.12.0, zwei Pushes im optionalen Bereich (0x4005, 0x4006), die ältere Clients übergehen. Art, Motiv, Form, Farbe und Dauer
// bleiben Text, damit neue Werte keinen neuen Typ brauchen.

/// <summary>
/// Was eine <see cref="MeldungSendenAnfrage"/> tut (<c>art</c>, offener Text). Ein Server, der die Art nicht kennt, antwortet
/// UNGUELTIG ohne Trennung.
/// </summary>
public static class Meldungsvorgang
{
    /// <summary>Befehl an alle (groß, 8 s beim Funker); ein neuer ersetzt den alten, gleich von wem.</summary>
    public const string Befehl = "Befehl";

    /// <summary>Zustand setzen (klein, bleibt, bis er aufgehoben oder durch einen anderen ersetzt wird).</summary>
    public const string Zustand = "Zustand";

    /// <summary>Den laufenden Zustand aufheben.</summary>
    public const string ZustandAufheben = "ZustandAufheben";

    /// <summary>Ampel rot, gelb oder grün (das Motiv sagt welche), nur von Hand (M22).</summary>
    public const string Ampel = "Ampel";

    /// <summary>Ampel aus.</summary>
    public const string AmpelAus = "AmpelAus";

    /// <summary>Countdown bis <c>ziel</c> (Serverzeit), Form Startzeit oder Restzeit.</summary>
    public const string Countdown = "Countdown";

    /// <summary>Den laufenden Countdown abbrechen.</summary>
    public const string CountdownAbbrechen = "CountdownAbbrechen";

    /// <summary>Bereit-Abfrage starten; eine laufende wird ersetzt (neue Nummer).</summary>
    public const string BereitAbfrage = "BereitAbfrage";

    /// <summary>Die offene Bereit-Abfrage schließen.</summary>
    public const string BereitSchliessen = "BereitSchliessen";

    /// <summary>
    /// Textmeldung an alle (M24): nur Text in einer festen Farbe und Größe, 8 s oder bis aufgehoben (<see cref="Textmeldungen"/>); eine neue
    /// ersetzt die laufende, gleich von wem.
    /// </summary>
    public const string Text = "Text";

    /// <summary>Die laufende Textmeldung aufheben.</summary>
    public const string TextAufheben = "TextAufheben";

    /// <summary>Nur als <see cref="Meldestand.Anlass"/>: der laufende Stand nach Welcome (M20), ohne vergangene Befehle.</summary>
    public const string Stand = "Stand";

    /// <summary>Nur als <see cref="Meldestand.Anlass"/>: die Bereit-Abfrage ist nach 2 min von selbst zu Ende.</summary>
    public const string BereitAbgelaufen = "BereitAbgelaufen";

    /// <summary>Die Arten, die ein Client in <see cref="MeldungSendenAnfrage.Art"/> schicken darf.</summary>
    public static IReadOnlyList<string> Anfragbar { get; } =
        [Befehl, Zustand, ZustandAufheben, Ampel, AmpelAus, Countdown, CountdownAbbrechen, BereitAbfrage, BereitSchliessen, Text, TextAufheben];

    /// <summary>Ob <paramref name="art"/> eine Art ist, die ein Client schicken darf.</summary>
    public static bool IstAnfragbar(string? art) => art is not null && Anfragbar.Contains(art, StringComparer.Ordinal);
}

/// <summary>
/// Wer Meldungen senden darf (M1, M15-M18): Haken "darf Meldungen senden" (vom Admin gesetzt, Mitgliedsfeld) UND Stufe Führung
/// (RAIDLEITER) oder ADMIN oder Einsatzleitung mit laufendem Auftrag; "Heute als Funker" sendet nicht. Server und Client rechnen
/// mit derselben Regel; entscheidend ist der Server.
/// </summary>
public static class Melderecht
{
    public static bool Darf(bool haken, Stufe? stufe, bool auftragLaeuft, bool alsFunker) =>
        haken && !alsFunker && (stufe is Stufe.Admin or Stufe.Raidleiter || auftragLaeuft);
}

/// <summary>
/// Farben und Dauern der Textmeldung (M24, Sarevoc 16.10.2026 abends) als Kennung (offener Text wie <c>art</c>): fest Weiß, Gelb, Orange, Rot,
/// Grün, Blau; "8 s" (ab Werk) oder "bleibt stehen" (bis aufgehoben oder durch eine neue Textmeldung ersetzt). Welcher Farbton zu einer
/// Kennung gehört, legt der Client fest.
/// </summary>
public static class Textmeldungen
{
    public const string Weiss = "weiss";
    public const string Gelb = "gelb";
    public const string Orange = "orange";
    public const string Rot = "rot";
    public const string Gruen = "gruen";
    public const string Blau = "blau";

    /// <summary>Steht 8 s ab dem Eingang (ab Werk, wie ein Befehl).</summary>
    public const string Kurz = "8s";

    /// <summary>Bleibt stehen, bis sie aufgehoben oder durch eine neue Textmeldung ersetzt wird; gehört zum laufenden Stand (M20).</summary>
    public const string Bleibt = "bleibt";

    /// <summary>Die Farben in der Folge der Farbwahl, Weiß zuerst (ab Werk).</summary>
    public static IReadOnlyList<string> Farben { get; } = [Weiss, Gelb, Orange, Rot, Gruen, Blau];

    /// <summary>Die Dauern, "8s" zuerst (ab Werk).</summary>
    public static IReadOnlyList<string> Dauern { get; } = [Kurz, Bleibt];

    public static bool IstFarbe(string? farbe) => farbe is not null && Farben.Contains(farbe, StringComparer.Ordinal);

    public static bool IstDauer(string? dauer) => dauer is not null && Dauern.Contains(dauer, StringComparer.Ordinal);

    /// <summary>Die Textmeldung, wenn sie stehen bleibt (sie gehört dann zum laufenden Stand), sonst <c>null</c>.</summary>
    public static Textmeldung? Bleibend(Textmeldung? t) => t is { Dauer: Bleibt } ? t : null;
}

/// <summary>
/// 0x0026 MeldungSenden (C->S, seit 0.12.0): eine Meldung an alle, die online sind. Recht nach <see cref="Melderecht"/>, je Sender
/// höchstens eine in 2 s (Ratengruppe <see cref="Ratengruppe.Meldung"/>, sonst GRENZE). <see cref="Motiv"/> ist eine Kennung aus dem
/// <see cref="Meldungskatalog"/> und muss zur Art passen (Befehl, Zustand, Ampel; beim Countdown aus der Form; bei der Bereit-Abfrage
/// ab Werk <c>bereit_melden</c>). <see cref="Groesse"/> 1-3 (normal, doppelt, dreifach, M21; Zustände bleiben klein),
/// <see cref="Zeile"/> ist die freiwillige Zeile unter der Grafik. Beim Countdown sind <see cref="Ziel"/> (Unix-ms in Serverzeit,
/// in der Zukunft, höchstens 24 h voraus) und <see cref="Form"/> (Startzeit, Restzeit) Pflicht. Die Textmeldung (Art <c>Text</c>, M24)
/// trägt statt eines Motivs <see cref="Text"/> (Pflicht, höchstens 120 Zeichen; mehr ist ZU_GROSS und trennt), <see cref="Farbe"/> und
/// <see cref="Dauer"/> aus <see cref="Textmeldungen"/> (fehlen sie: Weiß und 8 s) und <see cref="Groesse"/>. Ein Verstoß gegen diese
/// Regeln ist UNGUELTIG mit Klartext, ohne Trennung. Antwort <c>{}</c>; die Meldung selbst kommt wie bei allen als <see cref="Meldestand"/>.
/// </summary>
public sealed record MeldungSendenAnfrage : ISteuerBody
{
    public required string Art { get; init; }
    public string? Motiv { get; init; }
    public byte? Groesse { get; init; }
    public string? Zeile { get; init; }
    public long? Ziel { get; init; }
    public string? Form { get; init; }
    public string? Text { get; init; }
    public string? Farbe { get; init; }
    public string? Dauer { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Art, Grenzen.MeldungsArtMax, "art");
        p.Text(Motiv, Grenzen.MotivKennungMax, "motiv");
        p.Bereich(Groesse, 1, Grenzen.MeldungGroesseMax, "groesse");
        p.Text(Zeile, Grenzen.MeldungZeileMax, "zeile");
        p.Bereich(Ziel, 0, JsonVorpruefung.MaxGanzzahl, "ziel");
        p.Text(Form, Grenzen.CountdownFormMax, "form");
        p.Text(Text, Grenzen.MeldungTextMax, "text");
        p.Text(Farbe, Grenzen.TextKennungMax, "farbe");
        p.Text(Dauer, Grenzen.TextKennungMax, "dauer");
    }
}

/// <summary>
/// 0x0027 BereitMelden (C->S, seit 0.12.0, jeder Funker): "Ich bin bereit" auf die offene Bereit-Abfrage mit dieser
/// <see cref="Nummer"/> (aus <see cref="Meldestand.Abfrage"/>). Ist keine offen oder eine andere, kommt UNBEKANNT; zweimal ist wie
/// einmal. Antwort <c>{}</c>.
/// </summary>
public sealed record BereitMeldenAnfrage : ISteuerBody
{
    public required uint Nummer { get; init; }

    public void Pruefen(ref Pruefung p) { }
}

/// <summary>
/// Eine laufende oder gerade gesendete Meldung in <see cref="Meldestand"/>: Motiv (Kennung des Katalogs), Größe 1-3, freiwillige
/// Zeile, wer sie gesendet hat (Konto) und seit wann (Unix-ms, Serverzeit). Beim Countdown dazu <see cref="Ziel"/> und
/// <see cref="Form"/>, bei der Bereit-Abfrage <see cref="Nummer"/> und in <see cref="Ziel"/> ihr spätestes Ende.
/// </summary>
public sealed record Meldungseintrag : ISteuerBody
{
    public required string Motiv { get; init; }
    public required byte Groesse { get; init; }
    public string? Zeile { get; init; }
    public required uint Von { get; init; }
    public required long Seit { get; init; }
    public long? Ziel { get; init; }
    public string? Form { get; init; }
    public uint? Nummer { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Motiv, Grenzen.MotivKennungMax, "motiv");
        p.Bereich(Groesse, 1, Grenzen.MeldungGroesseMax, "groesse");
        p.Text(Zeile, Grenzen.MeldungZeileMax, "zeile");
        p.Bereich(Seit, 0, JsonVorpruefung.MaxGanzzahl, "seit");
        p.Bereich(Ziel, 0, JsonVorpruefung.MaxGanzzahl, "ziel");
        p.Text(Form, Grenzen.CountdownFormMax, "form");
    }
}

/// <summary>
/// Eine Textmeldung in <see cref="Meldestand.Text"/> (M24): der Text (höchstens 120 Zeichen), Farbe und Dauer als Kennung aus
/// <see cref="Textmeldungen"/>, Größe 1-3, wer sie gesendet hat (Konto) und seit wann (Unix-ms, Serverzeit).
/// </summary>
public sealed record Textmeldung : ISteuerBody
{
    public required string Text { get; init; }
    public required string Farbe { get; init; }
    public required byte Groesse { get; init; }
    public required string Dauer { get; init; }
    public required uint Von { get; init; }
    public required long Seit { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Text, Grenzen.MeldungTextMax, "text");
        p.PflichtText(Farbe, Grenzen.TextKennungMax, "farbe");
        p.Bereich(Groesse, 1, Grenzen.MeldungGroesseMax, "groesse");
        p.PflichtText(Dauer, Grenzen.TextKennungMax, "dauer");
        p.Bereich(Seit, 0, JsonVorpruefung.MaxGanzzahl, "seit");
    }
}

/// <summary>
/// 0x4005 Meldestand (S->C, optional, seit 0.12.0) an alle aktiven Sitzungen, nach jeder Meldung und nach Welcome. Trägt immer den
/// ganzen laufenden Stand (<see cref="Zustand"/>, <see cref="Ampel"/>, <see cref="Countdown"/>, offene <see cref="Abfrage"/>, eine
/// bleibende <see cref="Text"/>meldung; was fehlt, läuft nicht), dazu bei einem Befehl den <see cref="Befehl"/> selbst und beim Anlass
/// <c>Text</c> die gerade gesendete Textmeldung, auch eine mit 8 s (die nicht stehen bleibt). <see cref="Anlass"/> ist die Art der Meldung
/// (<see cref="Meldungsvorgang"/>), <c>Stand</c> nach Welcome, <c>BereitAbgelaufen</c> nach 2 min; <see cref="Von"/> das auslösende
/// Konto. Einen Countdown blendet der Client bei 0 selbst aus; der Server nimmt ihn danach still aus seinem Stand, ein späterer Push
/// und die Nachlieferung tragen ihn nicht mehr.
/// </summary>
public sealed record Meldestand : ISteuerBody
{
    public required string Anlass { get; init; }
    public uint? Von { get; init; }
    public Meldungseintrag? Befehl { get; init; }
    public Meldungseintrag? Zustand { get; init; }
    public Meldungseintrag? Ampel { get; init; }
    public Meldungseintrag? Countdown { get; init; }
    public Meldungseintrag? Abfrage { get; init; }
    public Textmeldung? Text { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.PflichtText(Anlass, Grenzen.MeldungsArtMax, "anlass");
        p.Objekt(Befehl);
        p.Objekt(Zustand);
        p.Objekt(Ampel);
        p.Objekt(Countdown);
        p.Objekt(Abfrage);
        p.Objekt(Text);
    }
}

/// <summary>
/// 0x4006 BereitStand (S->C, optional, seit 0.12.0) nur an Sitzungen, die gerade senden dürfen (<see cref="Melderecht"/>): "n von m
/// bereit" zur Abfrage <see cref="Nummer"/>. <see cref="Online"/> zählt die aktiven Sitzungen, <see cref="Bereit"/> die davon, die
/// bereit gemeldet haben, <see cref="Fehlen"/> nennt die übrigen Konten (Namen aus der Praesenz). Kommt beim Start, bei jedem
/// Bereit, wenn jemand kommt oder geht, und am Ende (<see cref="Offen"/> false: geschlossen oder nach <see cref="Bis"/> abgelaufen).
/// </summary>
public sealed record BereitStand : ISteuerBody
{
    public required uint Nummer { get; init; }
    public required bool Offen { get; init; }
    public required ushort Bereit { get; init; }
    public required ushort Online { get; init; }
    public required IReadOnlyList<uint> Fehlen { get; init; }
    public required uint Von { get; init; }
    public required long Bis { get; init; }

    public void Pruefen(ref Pruefung p)
    {
        p.Liste(Fehlen, Grenzen.KontenMax, "fehlen");
        p.Bereich(Bis, 0, JsonVorpruefung.MaxGanzzahl, "bis");
    }
}
