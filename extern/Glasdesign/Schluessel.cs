// Glasdesign - Maintainer: Sarevoc
using System.Reflection;

namespace Glasdesign;

/// <summary>
/// Die festen Schlüssel einer Farbstellung. Jede Farbstellung (Farbstellungen\*.xaml oder eine eigene Datei) muss genau
/// diese Schlüssel enthalten; <see cref="Thema.Fehlende"/> prüft es, <see cref="Thema.Setzen(System.Windows.ResourceDictionary, System.Windows.ResourceDictionary)"/>
/// lehnt eine unvollständige ab. Pinsel enden ohne Zusatz, reine Farben auf ".Farbe".
/// </summary>
public static class Schluessel
{
    /// <summary>Name der Farbstellung (string), etwa "Standard".</summary>
    public const string Name = "Glas.Thema.Name";

    /// <summary>Hintergrund der Fenster und Flächen (Anthrazit).</summary>
    public const string Grund = "Glas.Grund";
    public const string GrundFarbe = "Glas.Grund.Farbe";

    /// <summary>Schlichter Text auf dem Grund (Beschriftungen außerhalb der Knöpfe).</summary>
    public const string Text = "Glas.Text";
    public const string TextGedaempft = "Glas.Text.Gedaempft";

    /// <summary>Silbrige Schrift und Symbole auf Glas (senkrechter Verlauf).</summary>
    public const string Schrift = "Glas.Schrift";
    public const string SchriftGesperrt = "Glas.Schrift.Gesperrt";

    /// <summary>
    /// Haarlinie von Glas 3 (ein Gerätepixel, halbdurchsichtig; senkrechter Verlauf: oben hell, Seiten schwach, unten mittel).
    /// Kante.Haupt ist die Haarlinie des Akzentglases (Hauptaktion, Schalterspur an).
    /// </summary>
    public const string Kante = "Glas.Kante";
    public const string KanteUeber = "Glas.Kante.Ueber";
    public const string KanteGesperrt = "Glas.Kante.Gesperrt";
    public const string KanteHaupt = "Glas.Kante.Haupt";

    /// <summary>Haarlinie eingerastet (gewählter Reiter), im Ton des Leuchtsaums.</summary>
    public const string KanteLeuchten = "Glas.Kante.Leuchten";

    /// <summary>
    /// Ton des Leuchtsaums (Glas 4): ein gesättigter, dunklerer Ton, nicht die helle Akzentfarbe (helle Töne wirken milchig).
    /// Leuchtsaum.Auswahl ist der Ton eingerastet. Die Stärken stehen im Glanzprofil.
    /// </summary>
    public const string LeuchtsaumFarbe = "Glas.Leuchtsaum.Farbe";
    public const string LeuchtsaumAuswahlFarbe = "Glas.Leuchtsaum.Auswahl.Farbe";

    /// <summary>Ton des Spotlichts auf der Haarlinie an der Mausstelle (Color); Stärke und Radius im Glanzprofil.</summary>
    public const string SpotlichtFarbe = "Glas.Spotlicht.Farbe";

    /// <summary>Die drei Farben der Lichtkante (Color), im Uhrzeigersinn von oben; Breite, Schein und Umlauf im Glanzprofil.</summary>
    public const string LichtkanteAnfangFarbe = "Glas.Lichtkante.Anfang.Farbe";
    public const string LichtkanteMitteFarbe = "Glas.Lichtkante.Mitte.Farbe";
    public const string LichtkanteEndeFarbe = "Glas.Lichtkante.Ende.Farbe";

    /// <summary>Farbton der Spiegelungen (Kantenspiegel, Randlicht, Schein, Color): auf normalem Glas und auf dem Akzentglas.</summary>
    public const string SpiegelFarbe = "Glas.Spiegel.Farbe";
    public const string SpiegelHauptFarbe = "Glas.Spiegel.Haupt.Farbe";

    /// <summary>Das Glanzprofil dieser Farbstellung (<see cref="Glasdesign.Glanzprofil"/>): alle Stärken und Lagen der Spiegelungen.</summary>
    public const string Glanzprofil = "Glas.Glanzprofil";

    /// <summary>Der Bauplan dieser Farbstellung (<see cref="Glasdesign.Bauplan"/>): Maße von Schalter, Leuchtschalter, Schatten, Zeichen.</summary>
    public const string Bauplan = "Glas.Bauplan";

    /// <summary>Das Farbglasprofil (<see cref="Glasdesign.Farbglasprofil"/>): Wulst, Glanzband, Funkeln und Spiegelung der Farbglasknöpfe.</summary>
    public const string Farbglasprofil = "Glas.Farbglasprofil";

    /// <summary>Grundfarbe der Farbglasknöpfe, wenn ein Knopf keine eigene (Background) mitbringt.</summary>
    public const string FarbglasFarbe = "Glas.Farbglas.Farbe";
    public const string Farbglas = "Glas.Farbglas";

    /// <summary>Grundfarbe der Metallknöpfe (Flüssigmetall), wenn ein Knopf keinen eigenen Background mitbringt: Silber.</summary>
    /// <summary>Milchglas (Stil Glas.Milchglas): Tönung über dem unscharfen Grund und Kante (senkrechte Verläufe).</summary>
    public const string MilchglasFlaeche = "Glas.Milchglas.Flaeche";
    public const string MilchglasKante = "Glas.Milchglas.Kante";

    /// <summary>Flüssigglas (Stil Glas.Fluessigglas): ganz leichte Tönung und die helle Kante.</summary>
    public const string FluessigglasFlaeche = "Glas.Fluessigglas.Flaeche";
    public const string FluessigglasKante = "Glas.Fluessigglas.Kante";

    /// <summary>Grundfarben der Clay- und Neo-Brutal-Knöpfe, wenn ein Knopf keinen eigenen Background mitbringt.</summary>
    public const string ClayFarbe = "Glas.Clay.Farbe";
    public const string Clay = "Glas.Clay";
    public const string BrutalFarbe = "Glas.Brutal.Farbe";
    public const string Brutal = "Glas.Brutal";

    public const string MetallFarbe = "Glas.Metall.Farbe";
    public const string Metall = "Glas.Metall";

    /// <summary>Feld (Stil Glas.Feld): Fläche als Verlauf, Rand und Akzentleiste.</summary>
    public const string FeldFlaeche = "Glas.Feld.Flaeche";
    public const string FeldRand = "Glas.Feld.Rand";
    public const string FeldAkzent = "Glas.Feld.Akzent";

    /// <summary>Textfeld (Stile Glas.Textfeld, Glas.Passwortfeld): Mulde, Rand, Rand überfahren, Rand im Fokus.</summary>
    public const string TextfeldFlaeche = "Glas.Textfeld.Flaeche";
    public const string TextfeldRand = "Glas.Textfeld.Rand";
    public const string TextfeldRandUeber = "Glas.Textfeld.Rand.Ueber";
    public const string TextfeldRandFokus = "Glas.Textfeld.Rand.Fokus";

    /// <summary>
    /// Stilfamilie Matt (Stile Glas.Matt.Knopf, .Haupt, Glas.Matt.Kontur): Fläche, Hauptaktion, Haarlinie normal und
    /// überfahren (alle Color, Deckung im Alphakanal), Schrift auf Fläche und auf der Hauptaktion, das Mattprofil.
    /// </summary>
    public const string MattFarbe = "Glas.Matt.Farbe";
    public const string MattHauptFarbe = "Glas.Matt.Haupt.Farbe";
    public const string MattRandFarbe = "Glas.Matt.Rand.Farbe";
    public const string MattRandUeberFarbe = "Glas.Matt.Rand.Ueber.Farbe";
    public const string MattSchrift = "Glas.Matt.Schrift";
    public const string MattSchriftHaupt = "Glas.Matt.Schrift.Haupt";
    public const string Mattprofil = "Glas.Mattprofil";

    // Maße der Stile (Glas 4, in Glint einstellbar): Höhe, Innenabstand, Schriftgröße je Element, als Double und Thickness.
    public const string MassKnopfHoehe = "Glas.Mass.Knopf.Hoehe";
    public const string MassKnopfInnenabstand = "Glas.Mass.Knopf.Innenabstand";
    public const string MassKnopfSchrift = "Glas.Mass.Knopf.Schrift";
    public const string MassSymbolknopfGroesse = "Glas.Mass.Symbolknopf.Groesse";
    public const string MassFormknopfHoehe = "Glas.Mass.Formknopf.Hoehe";
    public const string MassFormknopfUmriss = "Glas.Mass.Formknopf.Umriss";
    public const string MassReglerHoehe = "Glas.Mass.Regler.Hoehe";
    public const string MassReglerKnauf = "Glas.Mass.Regler.Knauf";
    public const string MassReglerSpur = "Glas.Mass.Regler.Spur";
    public const string MassReglerSpurrand = "Glas.Mass.Regler.Spurrand";
    public const string MassZahlenfeldBreite = "Glas.Mass.Zahlenfeld.Breite";
    public const string MassListeInnenabstand = "Glas.Mass.Liste.Innenabstand";
    public const string MassListeEcke = "Glas.Mass.Liste.Ecke";
    public const string MassBildlaufBreite = "Glas.Mass.Bildlauf.Breite";
    public const string ReglerSpur = "Glas.Regler.Spur";
    public const string ReglerFuellung = "Glas.Regler.Fuellung";
    public const string ListeUeber = "Glas.Liste.Ueber";
    public const string ListeAuswahl = "Glas.Liste.Auswahl";
    public const string BildlaufGriff = "Glas.Bildlauf.Griff";
    public const string BildlaufGriffUeber = "Glas.Bildlauf.Griff.Ueber";
    public const string ZahlenfeldUngueltig = "Glas.Zahlenfeld.Ungueltig";

    /// <summary>
    /// Fortschritt (Stil Glas.Fortschritt, 0.5.0): Höhe, Abstand der Glasfüllung zur Rinne, Dicke und Schein der Lichtlinie,
    /// Breite und Fuge der Segmente (DIP), Länge des unbestimmten Läufers (Anteil der Spur) und seine Umlaufzeit (ms).
    /// </summary>
    public const string MassFortschrittHoehe = "Glas.Mass.Fortschritt.Hoehe";
    public const string MassFortschrittAbstand = "Glas.Mass.Fortschritt.Abstand";
    public const string MassFortschrittLinie = "Glas.Mass.Fortschritt.Linie";
    public const string MassFortschrittSchein = "Glas.Mass.Fortschritt.Schein";
    public const string MassFortschrittSegment = "Glas.Mass.Fortschritt.Segment";
    public const string MassFortschrittFuge = "Glas.Mass.Fortschritt.Fuge";
    public const string MassFortschrittLaenge = "Glas.Mass.Fortschritt.Laenge";
    public const string MassFortschrittUmlauf = "Glas.Mass.Fortschritt.Umlauf";

    /// <summary>Fortschritt: eingelassene Rinne (Fläche, Haarlinie, gesperrt), Füllung aus Glas (Körper, Haarlinie, gesperrt), Ton der Lichtlinie (Color).</summary>
    public const string FortschrittSpur = "Glas.Fortschritt.Spur";
    public const string FortschrittSpurKante = "Glas.Fortschritt.Spur.Kante";
    public const string FortschrittSpurGesperrt = "Glas.Fortschritt.Spur.Gesperrt";
    public const string FortschrittFuellung = "Glas.Fortschritt.Fuellung";
    public const string FortschrittFuellungKante = "Glas.Fortschritt.Fuellung.Kante";
    public const string FortschrittFuellungGesperrt = "Glas.Fortschritt.Fuellung.Gesperrt";
    public const string FortschrittLichtFarbe = "Glas.Fortschritt.Licht.Farbe";

    /// <summary>Fensterrahmen (Stile Glas.Fenster und Glas.Fensterrahmen, 0.5.0): Höhe der Titelleiste, Größe der Glasknöpfe, Schrift des Titels, Fenstersymbol (DIP).</summary>
    public const string MassFensterTitelhoehe = "Glas.Mass.Fenster.Titelhoehe";
    public const string MassFensterKnopf = "Glas.Mass.Fenster.Knopf";
    public const string MassFensterSchrift = "Glas.Mass.Fenster.Schrift";
    public const string MassFensterSymbol = "Glas.Mass.Fenster.Symbol";

    /// <summary>
    /// Fensterrahmen: Titelleiste aus Glas aktiv und inaktiv, Haarlinie um das Fenster aktiv und inaktiv, Trennlinie unter
    /// der Titelleiste, Titel aktiv und inaktiv, Ton des Schließen-Knopfs beim Überfahren (Saum und Kreuz).
    /// </summary>
    public const string FensterLeiste = "Glas.Fenster.Leiste";
    public const string FensterLeisteInaktiv = "Glas.Fenster.Leiste.Inaktiv";
    public const string FensterKante = "Glas.Fenster.Kante";
    public const string FensterKanteInaktiv = "Glas.Fenster.Kante.Inaktiv";
    public const string FensterTrennlinie = "Glas.Fenster.Trennlinie";
    public const string FensterTitel = "Glas.Fenster.Titel";
    public const string FensterTitelInaktiv = "Glas.Fenster.Titel.Inaktiv";
    public const string FensterSchliessen = "Glas.Fenster.Schliessen";

    public const string MassFarbglasHoehe = "Glas.Mass.Farbglas.Hoehe";
    public const string MassFarbglasInnenabstand = "Glas.Mass.Farbglas.Innenabstand";
    public const string MassFarbglasSchrift = "Glas.Mass.Farbglas.Schrift";
    public const string MassGelknopfRundung = "Glas.Mass.Gelknopf.Rundung";
    public const string MassKissenknopfRundung = "Glas.Mass.Kissenknopf.Rundung";
    public const string MassMetallknopfRundung = "Glas.Mass.Metallknopf.Rundung";
    public const string MassClayknopfRundung = "Glas.Mass.Clayknopf.Rundung";
    public const string MassBrutalknopfRundung = "Glas.Mass.Brutalknopf.Rundung";
    public const string MassFluessigglasEcke = "Glas.Mass.Fluessigglas.Ecke";
    public const string MassFluessigglasBrechung = "Glas.Mass.Fluessigglas.Brechung";
    public const string MassFluessigglasKantenbreite = "Glas.Mass.Fluessigglas.Kantenbreite";
    public const string MassFluessigglasFarbsaum = "Glas.Mass.Fluessigglas.Farbsaum";
    public const string MassFluessigglasInnenabstand = "Glas.Mass.Fluessigglas.Innenabstand";
    public const string MassMilchglasUnschaerfe = "Glas.Mass.Milchglas.Unschaerfe";
    public const string MassMilchglasEcke = "Glas.Mass.Milchglas.Ecke";
    public const string MassMilchglasKoernung = "Glas.Mass.Milchglas.Koernung";
    public const string MassMilchglasInnenabstand = "Glas.Mass.Milchglas.Innenabstand";
    public const string MassFeldEcke = "Glas.Mass.Feld.Ecke";
    public const string MassFeldRand = "Glas.Mass.Feld.Rand";
    public const string MassFeldAkzentbreite = "Glas.Mass.Feld.Akzentbreite";
    public const string MassFeldLeuchten = "Glas.Mass.Feld.Leuchten";
    public const string MassFeldLeuchtweite = "Glas.Mass.Feld.Leuchtweite";
    public const string MassFeldGlanz = "Glas.Mass.Feld.Glanz";
    public const string MassFeldInnenabstand = "Glas.Mass.Feld.Innenabstand";
    public const string MassTextfeldHoehe = "Glas.Mass.Textfeld.Hoehe";
    public const string MassTextfeldInnenabstand = "Glas.Mass.Textfeld.Innenabstand";
    public const string MassTextfeldSchrift = "Glas.Mass.Textfeld.Schrift";
    public const string MassTextfeldEcke = "Glas.Mass.Textfeld.Ecke";
    public const string MassTextfeldLeuchten = "Glas.Mass.Textfeld.Leuchten";
    public const string MassMattHoehe = "Glas.Mass.Matt.Hoehe";
    public const string MassMattInnenabstand = "Glas.Mass.Matt.Innenabstand";
    public const string MassMattSchrift = "Glas.Mass.Matt.Schrift";
    public const string MassMattRundung = "Glas.Mass.Matt.Rundung";
    public const string MassReiterHoehe = "Glas.Mass.Reiter.Hoehe";
    public const string MassReiterInnenabstand = "Glas.Mass.Reiter.Innenabstand";
    public const string MassReiterSchrift = "Glas.Mass.Reiter.Schrift";
    public const string MassHakenGroesse = "Glas.Mass.Haken.Groesse";
    public const string MassHakenInnenabstand = "Glas.Mass.Haken.Innenabstand";
    public const string MassHakenSchrift = "Glas.Mass.Haken.Schrift";
    public const string MassSchalterHoehe = "Glas.Mass.Schalter.Hoehe";
    public const string MassSchalterInnenabstand = "Glas.Mass.Schalter.Innenabstand";
    public const string MassSchalterSchrift = "Glas.Mass.Schalter.Schrift";
    public const string MassLeuchtschalterInnenabstand = "Glas.Mass.Leuchtschalter.Innenabstand";
    public const string MassLeuchtschalterSchrift = "Glas.Mass.Leuchtschalter.Schrift";
    public const string MassHilfeSchrift = "Glas.Mass.Hilfe.Schrift";
    public const string MassHilfeInnenabstand = "Glas.Mass.Hilfe.Innenabstand";
    public const string MassHilfeBreite = "Glas.Mass.Hilfe.Breite";

    public const string Koerper = "Glas.Koerper";
    public const string KoerperUeber = "Glas.Koerper.Ueber";
    public const string KoerperGedrueckt = "Glas.Koerper.Gedrueckt";
    public const string KoerperGesperrt = "Glas.Koerper.Gesperrt";

    public const string Haupt = "Glas.Haupt";
    public const string HauptUeber = "Glas.Haupt.Ueber";
    public const string HauptGedrueckt = "Glas.Haupt.Gedrueckt";
    public const string HauptGesperrt = "Glas.Haupt.Gesperrt";

    public const string Auswahl = "Glas.Auswahl";
    public const string AuswahlUeber = "Glas.Auswahl.Ueber";
    public const string AuswahlGedrueckt = "Glas.Auswahl.Gedrueckt";
    public const string AuswahlGesperrt = "Glas.Auswahl.Gesperrt";

    public const string Dunkel = "Glas.Dunkel";
    public const string DunkelUeber = "Glas.Dunkel.Ueber";
    public const string DunkelGedrueckt = "Glas.Dunkel.Gedrueckt";
    public const string DunkelGesperrt = "Glas.Dunkel.Gesperrt";

    /// <summary>Durchlicht an der Unterkante des Körpers (radial, auf die Wölbung gelegt), je Art.</summary>
    public const string Lichtfleck = "Glas.Lichtfleck";
    public const string LichtfleckHaupt = "Glas.Lichtfleck.Haupt";
    public const string LichtfleckAuswahl = "Glas.Lichtfleck.Auswahl";
    public const string LichtfleckDunkel = "Glas.Lichtfleck.Dunkel";

    /// <summary>Innenschatten oben im Körper (gedrückt) und in der Rinne des Leuchtschalters.</summary>
    public const string Innenschatten = "Glas.Innenschatten";

    /// <summary>Dunkler Kontaktschatten unter dem Knopf (Mitte am dunkelsten).</summary>
    public const string Kontaktschatten = "Glas.Kontaktschatten";

    /// <summary>Farbe des weichen Umgebungsschattens (DropShadowEffect).</summary>
    public const string SchattenFarbe = "Glas.Schatten.Farbe";

    /// <summary>Erste Akzentfarbe: Haken, Lichtkante der Hauptaktion. Türkis in Standard, Rosé in Weinrot.</summary>
    public const string Akzent = "Glas.Akzent";
    public const string AkzentFarbe = "Glas.Akzent.Farbe";

    /// <summary>Zweite Akzentfarbe für Links und Auswahl. Blau in Standard.</summary>
    public const string Akzent2 = "Glas.Akzent2";
    public const string Akzent2Farbe = "Glas.Akzent2.Farbe";

    /// <summary>Links im Text.</summary>
    public const string Link = "Glas.Link";

    /// <summary>Silbernes Kreuz für "aus" (Haken, Schalter).</summary>
    public const string ZeichenAus = "Glas.Zeichen.Aus";
    public const string ZeichenGesperrt = "Glas.Zeichen.Gesperrt";

    /// <summary>Körper der Schalterspur (senkrechter Verlauf); ihre Haarlinie kommt aus Glas.Kante*.</summary>
    public const string SpurAn = "Glas.Spur.An";
    public const string SpurAus = "Glas.Spur.Aus";
    public const string SpurGesperrt = "Glas.Spur.Gesperrt";

    /// <summary>Feiner heller Rahmen für den Tastaturfokus bis Glas 3 (<see cref="Fokusring"/>); die Stile nutzen seit Glas 4 den Leuchtsaum.</summary>
    public const string Fokus = "Glas.Fokus";

    /// <summary>Hilfekarte (Tooltip): Fläche, feine Linie, Text.</summary>
    public const string Karte = "Glas.Karte";
    public const string KarteLinie = "Glas.Karte.Linie";
    public const string KarteText = "Glas.Karte.Text";

    /// <summary>Leuchtschalter (JG-Schalter im Glaslook): Glaskörper des Knaufs, je Zustand.</summary>
    public const string LeuchtKnauf = "Glas.Leucht.Knauf";
    public const string LeuchtKnaufUeber = "Glas.Leucht.Knauf.Ueber";
    public const string LeuchtKnaufGedrueckt = "Glas.Leucht.Knauf.Gedrueckt";
    public const string LeuchtKnaufGesperrt = "Glas.Leucht.Knauf.Gesperrt";

    /// <summary>Gläserne Rinne (Spur) und ihre Lichtkante, etwas zurückhaltender als die des Knaufs.</summary>
    public const string LeuchtRinne = "Glas.Leucht.Rinne";
    public const string LeuchtRinneGesperrt = "Glas.Leucht.Rinne.Gesperrt";
    public const string LeuchtRinneKante = "Glas.Leucht.Rinne.Kante";

    /// <summary>Lichtkante der Rinne, solange eine Änderung wartet (gelb).</summary>
    public const string LeuchtWartet = "Glas.Leucht.Wartet";

    /// <summary>Lichtfleck im Knauf, wenn er nicht leuchtet (radial).</summary>
    public const string LeuchtLichtfleck = "Glas.Leucht.Lichtfleck";

    /// <summary>Zeichen im Knauf, wenn er nicht leuchtet (senkrechter Verlauf, metallisch).</summary>
    public const string LeuchtZeichen = "Glas.Leucht.Zeichen";

    /// <summary>Die vier Leuchtfarben des JG-Originals (Color). Die Bedeutung legt der Host fest, nicht die Bibliothek.</summary>
    public const string LeuchtGruenFarbe = "Glas.Leucht.Gruen.Farbe";
    public const string LeuchtTuerkisFarbe = "Glas.Leucht.Tuerkis.Farbe";
    public const string LeuchtRotFarbe = "Glas.Leucht.Rot.Farbe";
    public const string LeuchtGelbFarbe = "Glas.Leucht.Gelb.Farbe";

    // ===================================================================== Stilfamilien 2 (0.5.0-beta.6, KONZEPT.md 5b)

    /// <summary>Das Stilprofil (<see cref="Glasdesign.Stilprofil"/>): Werte von Taktil, Glutglas, Dickglas, Glanzkugel, Horizontglas, Chromrahmen und ihrem Chromrand.</summary>
    public const string Stilprofil = "Glas.Stilprofil";

    /// <summary>Taktil: Grundton gehobener Flächen, Mulde, Kupfer (Pinsel und Farbe; die eigene Vorgabe der Familie), Bronzefassung, Metall der Knäufe, Schrift.</summary>
    public const string TaktilFlaecheFarbe = "Glas.Taktil.Flaeche.Farbe";
    public const string TaktilMuldeFarbe = "Glas.Taktil.Mulde.Farbe";
    public const string TaktilKupfer = "Glas.Taktil.Kupfer";
    public const string TaktilFassungFarbe = "Glas.Taktil.Fassung.Farbe";
    public const string TaktilMetallFarbe = "Glas.Taktil.Metall.Farbe";
    public const string TaktilSchrift = "Glas.Taktil.Schrift";
    public const string TaktilSchriftHaupt = "Glas.Taktil.Schrift.Haupt";
    public const string TaktilSchriftGesperrt = "Glas.Taktil.Schrift.Gesperrt";
    public const string MassTaktilHoehe = "Glas.Mass.Taktil.Hoehe";
    public const string MassTaktilInnenabstand = "Glas.Mass.Taktil.Innenabstand";
    public const string MassTaktilSchrift = "Glas.Mass.Taktil.Schrift";
    public const string MassTaktilFassung = "Glas.Mass.Taktil.Fassung";
    public const string MassTaktilEcke = "Glas.Mass.Taktil.Ecke";
    public const string MassTaktilHaken = "Glas.Mass.Taktil.Haken";
    public const string MassTaktilSchalter = "Glas.Mass.Taktil.Schalter";
    public const string MassTaktilSpur = "Glas.Mass.Taktil.Spur";

    /// <summary>Glutglas: Glut (Pinsel und Farbe, die eigene Vorgabe der Familie), Rauchglasscheibe, Haarlinie der Scheibe, Schrift, Glas des Hakens.</summary>
    public const string GlutglasGlut = "Glas.Glutglas.Glut";
    public const string GlutglasScheibeFarbe = "Glas.Glutglas.Scheibe.Farbe";
    public const string GlutglasKante = "Glas.Glutglas.Kante";
    public const string GlutglasSchrift = "Glas.Glutglas.Schrift";
    public const string GlutglasSchriftGesperrt = "Glas.Glutglas.Schrift.Gesperrt";
    public const string GlutglasSchriftLeise = "Glas.Glutglas.Schrift.Leise";
    public const string GlutglasHakenFarbe = "Glas.Glutglas.Haken.Farbe";
    public const string MassGlutglasHoehe = "Glas.Mass.Glutglas.Hoehe";
    public const string MassGlutglasInnenabstand = "Glas.Mass.Glutglas.Innenabstand";
    public const string MassGlutglasSchrift = "Glas.Mass.Glutglas.Schrift";
    public const string MassGlutglasEcke = "Glas.Mass.Glutglas.Ecke";
    public const string MassGlutglasVersatz = "Glas.Mass.Glutglas.Versatz";
    public const string MassGlutglasSymbolknopf = "Glas.Mass.Glutglas.Symbolknopf";
    public const string MassGlutglasHaken = "Glas.Mass.Glutglas.Haken";
    public const string MassGlutglasSchalter = "Glas.Mass.Glutglas.Schalter";

    /// <summary>Dickglas: farbiges Licht als Verlauf aus zwei Farben (Knopf, Hauptknopf, klares Feld), Kern, Schrift, Schatten.</summary>
    public const string DickglasLicht = "Glas.Dickglas.Licht";
    public const string DickglasLichtHaupt = "Glas.Dickglas.Licht.Haupt";
    public const string DickglasLichtFeld = "Glas.Dickglas.Licht.Feld";
    public const string DickglasKernFarbe = "Glas.Dickglas.Kern.Farbe";
    public const string DickglasSchrift = "Glas.Dickglas.Schrift";
    public const string DickglasSchriftGesperrt = "Glas.Dickglas.Schrift.Gesperrt";
    public const string DickglasSchattenFarbe = "Glas.Dickglas.Schatten.Farbe";
    public const string MassDickglasHoehe = "Glas.Mass.Dickglas.Hoehe";
    public const string MassDickglasInnenabstand = "Glas.Mass.Dickglas.Innenabstand";
    public const string MassDickglasSchrift = "Glas.Mass.Dickglas.Schrift";
    public const string MassDickglasWulst = "Glas.Mass.Dickglas.Wulst";
    public const string MassDickglasDicke = "Glas.Mass.Dickglas.Dicke";
    public const string MassDickglasEcke = "Glas.Mass.Dickglas.Ecke";

    /// <summary>Glanzkugel: Leuchtkern, Kugel (Pinsel und Farbe, die eigene Vorgabe), Kuppel; die Schrift wählt der Körper selbst (hell oder dunkel).</summary>
    public const string GlanzkugelLeuchtkernFarbe = "Glas.Glanzkugel.Leuchtkern.Farbe";
    public const string GlanzkugelKugel = "Glas.Glanzkugel.Kugel";
    public const string GlanzkugelKuppelFarbe = "Glas.Glanzkugel.Kuppel.Farbe";
    public const string MassGlanzkugelGroesse = "Glas.Mass.Glanzkugel.Groesse";
    public const string MassGlanzkugelRing = "Glas.Mass.Glanzkugel.Ring";
    public const string MassGlanzkugelRahmen = "Glas.Mass.Glanzkugel.Rahmen";
    public const string MassGlanzkugelRundung = "Glas.Mass.Glanzkugel.Rundung";
    public const string MassGlanzkugelSchieber = "Glas.Mass.Glanzkugel.Schieber";

    /// <summary>Horizontglas: Farbe des Glases (Pinsel und Farbe, die eigene Vorgabe); die Schrift wählt der Körper selbst (hell oder dunkel).</summary>
    public const string Horizontglas = "Glas.Horizontglas";
    public const string MassHorizontglasHoehe = "Glas.Mass.Horizontglas.Hoehe";
    public const string MassHorizontglasInnenabstand = "Glas.Mass.Horizontglas.Innenabstand";
    public const string MassHorizontglasSchrift = "Glas.Mass.Horizontglas.Schrift";
    public const string MassHorizontglasRand = "Glas.Mass.Horizontglas.Rand";
    public const string MassHorizontglasRundung = "Glas.Mass.Horizontglas.Rundung";
    public const string MassHorizontglasSymbolknopf = "Glas.Mass.Horizontglas.Symbolknopf";

    /// <summary>Chromrahmen: Grundfarben der Füllungen (Hell, Silber, Schwarz, Rauch), Schrift auf hellen und dunklen Füllungen.</summary>
    public const string ChromrahmenHellFarbe = "Glas.Chromrahmen.Hell.Farbe";
    public const string ChromrahmenSilberFarbe = "Glas.Chromrahmen.Silber.Farbe";
    public const string ChromrahmenSchwarzFarbe = "Glas.Chromrahmen.Schwarz.Farbe";
    public const string ChromrahmenRauchFarbe = "Glas.Chromrahmen.Rauch.Farbe";
    public const string ChromrahmenSchriftDunkel = "Glas.Chromrahmen.Schrift.Dunkel";
    public const string ChromrahmenSchriftHell = "Glas.Chromrahmen.Schrift.Hell";
    public const string MassChromrahmenHoehe = "Glas.Mass.Chromrahmen.Hoehe";
    public const string MassChromrahmenInnenabstand = "Glas.Mass.Chromrahmen.Innenabstand";
    public const string MassChromrahmenSchrift = "Glas.Mass.Chromrahmen.Schrift";
    public const string MassChromrahmenRahmen = "Glas.Mass.Chromrahmen.Rahmen";
    public const string MassChromrahmenLippe = "Glas.Mass.Chromrahmen.Lippe";

    /// <summary>
    /// Statusfarben (seit 0.5.0-beta.8, Stilfamilien 2 Stufe 2): Hinweis, Erfolg, Warnung, Gefahr; für Hinweisbanner und
    /// später Abzeichen, Avatar-Status und Fehlertext. Pinsel; die Familien mischen sie mit ihrer Fläche.
    /// </summary>
    public const string StatusInfo = "Glas.Status.Info";
    public const string StatusErfolg = "Glas.Status.Erfolg";
    public const string StatusWarnung = "Glas.Status.Warnung";
    public const string StatusGefahr = "Glas.Status.Gefahr";

    /// <summary>
    /// Rückmeldungen (seit 0.5.0-beta.8): Innenabstand von Toast und Hinweisbanner, Innenabstand des Dialogs, Höhe des Zipfels
    /// der Sprechblase, Höhe eines Skelettbalkens, Größe des Spinners, Größe des Symbols im Leerzustand.
    /// </summary>
    public const string MassRueckmeldungInnenabstand = "Glas.Mass.Rueckmeldung.Innenabstand";
    public const string MassDialogInnenabstand = "Glas.Mass.Dialog.Innenabstand";
    public const string MassSprechblaseZipfel = "Glas.Mass.Sprechblase.Zipfel";
    public const string MassSkelettBalken = "Glas.Mass.Skelett.Balken";
    public const string MassSpinnerGroesse = "Glas.Mass.Spinner.Groesse";
    public const string MassLeerzustandSymbol = "Glas.Mass.Leerzustand.Symbol";

    /// <summary>Glutglas (seit 0.5.0-beta.8): das kühle Licht der Raute über dem Dialog und anderer Zeichen dort (Color).</summary>
    public const string GlutglasSymbolFarbe = "Glas.Glutglas.Symbol.Farbe";

    /// <summary>
    /// Navigation und Personen (seit 0.5.0-beta.9, Stilfamilien 2 Stufe 2): Höhe und Schrift der Krumen und der Seitenzahlen in
    /// der Leiste, Länge und Dicke der Verbindungslinie der Schrittfolge, Höhe, Schrift und Innenabstand der Abzeichen, Größe
    /// des Avatars. Alle Farben kommen aus vorhandenen Schlüsseln.
    /// </summary>
    public const string MassNavigationHoehe = "Glas.Mass.Navigation.Hoehe";
    public const string MassNavigationSchrift = "Glas.Mass.Navigation.Schrift";
    public const string MassSchrittfolgeAbstand = "Glas.Mass.Schrittfolge.Abstand";
    public const string MassSchrittfolgeLinie = "Glas.Mass.Schrittfolge.Linie";
    public const string MassAbzeichenHoehe = "Glas.Mass.Abzeichen.Hoehe";
    public const string MassAbzeichenSchrift = "Glas.Mass.Abzeichen.Schrift";
    public const string MassAbzeichenInnenabstand = "Glas.Mass.Abzeichen.Innenabstand";
    public const string MassAvatarGroesse = "Glas.Mass.Avatar.Groesse";

    /// <summary>
    /// Daten (seit 0.5.0-beta.10, Stilfamilien 2 Stufe 2): Höhe und Schrift des Tabellenkopfs; Mindesthöhe, Schrift von Titel
    /// und Untertitel und Größe des Symbols im Listeneintrag; Innenabstand der Karte; Höhe des Bildplatzhalters. Alle Farben
    /// kommen aus vorhandenen Schlüsseln.
    /// </summary>
    public const string MassTabellenkopfHoehe = "Glas.Mass.Tabellenkopf.Hoehe";
    public const string MassTabellenkopfSchrift = "Glas.Mass.Tabellenkopf.Schrift";
    public const string MassListeneintragHoehe = "Glas.Mass.Listeneintrag.Hoehe";
    public const string MassListeneintragSchrift = "Glas.Mass.Listeneintrag.Schrift";
    public const string MassListeneintragUntertitel = "Glas.Mass.Listeneintrag.Untertitel";
    public const string MassListeneintragSymbol = "Glas.Mass.Listeneintrag.Symbol";
    public const string MassKarteInnenabstand = "Glas.Mass.Karte.Innenabstand";
    public const string MassBildplatzhalterHoehe = "Glas.Mass.Bildplatzhalter.Hoehe";

    /// <summary>
    /// Eingabe und Aufkleber (seit 0.5.0-beta.11, Stilfamilien 2 Stufe 2): Höhe des Fehlerbalkens und Schrift der Zeilen unter
    /// dem Textfeld, Höchsthöhe der Ergebnisliste im Suchfeld, Größe des Aufklebers; das Licht des Dickglas-Knopfs mit Mulde
    /// (Verlauf aus zwei Farben) und die Farbfläche des Glanzkugel-Aufklebers (Pinsel).
    /// </summary>
    public const string MassTextfeldBalken = "Glas.Mass.Textfeld.Balken";
    public const string MassTextfeldHinweis = "Glas.Mass.Textfeld.Hinweis";
    public const string MassSuchfeldListe = "Glas.Mass.Suchfeld.Liste";
    public const string MassAufkleberGroesse = "Glas.Mass.Aufkleber.Groesse";
    public const string DickglasLichtMulde = "Glas.Dickglas.Licht.Mulde";
    public const string GlanzkugelAufkleberFlaeche = "Glas.Glanzkugel.Aufkleber.Flaeche";

    /// <summary>Alle Schlüssel in fester Reihenfolge (Reihenfolge der Deklaration).</summary>
    public static IReadOnlyList<string> Alle { get; } = typeof(Schluessel)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .OrderBy(f => f.MetadataToken)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();
}
