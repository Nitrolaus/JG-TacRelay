// JG TacRelay - Maintainer: Sarevoc
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Funk.Client.Design;
using Funk.Client.Dienste;
using Funk.Protokoll.Steuerung;

namespace Funk.Client.Modelle;

/// <summary>Was eine Ablage auf dem Brett bewirken würde (RAIDLEITUNG-ENTWURF, Das Brett: grün, gelb, rot vor dem Loslassen).</summary>
public enum Ablagewirkung
{
    /// <summary>Nichts zu tun (gleicher Platz, leerer Grund).</summary>
    Keine,

    /// <summary>Grün: passt.</summary>
    Passt,

    /// <summary>Gelb: Tausch mit der Person auf dem Ziel.</summary>
    Tausch,

    /// <summary>Rot: geht nicht, mit Grund.</summary>
    GehtNicht,
}

/// <summary>Die Bewertung einer Ablage samt Satz für die Anzeige an der Maus.</summary>
public sealed record Ablagebewertung(Ablagewirkung Wirkung, string Text)
{
    public static Ablagebewertung Nichts { get; } = new(Ablagewirkung.Keine, "");

    public bool Moeglich => Wirkung is Ablagewirkung.Passt or Ablagewirkung.Tausch;
}

/// <summary>Ein Ziel auf dem Brett, auf das sich eine Karte ziehen lässt.</summary>
public interface IAblageziel
{
    /// <summary>Wie eine gerade darüber gehaltene Karte wirken würde (für die Farbe des Ziels).</summary>
    Ablagewirkung Ablagezustand { get; set; }
}

/// <summary>Ampel einer Einheit aus der Mindestbesetzung (F5).</summary>
public enum Ampelstufe
{
    /// <summary>Keine Mindestbesetzung angelegt.</summary>
    Keine,
    Gruen,
    Gelb,
    Rot,
}

/// <summary>
/// Grundlage der Ziele: der Zustand während des Ziehens und die Anbindung an gong-wpf-dragdrop (<c>dd:DragDrop.DropHandler</c>).
/// Vor dem Loslassen bewertet das Brett die Ablage (grün, gelb, rot mit Grund, Text an der Maus); beim Loslassen legt es ab.
/// </summary>
public abstract partial class AblagezielBasis : ObservableObject, IAblageziel, GongSolutions.Wpf.DragDrop.IDropTarget
{
    /// <summary>Das Brett, das bewertet und ablegt; ohne Brett nimmt das Ziel nichts an.</summary>
    internal BrettViewModel? Brett { get; set; }

    [ObservableProperty]
    public partial Ablagewirkung Ablagezustand { get; set; }

    /// <summary>Satz zur gerade gehaltenen Karte ("passt", "voll 3/3"), sonst leer.</summary>
    [ObservableProperty]
    public partial string AblageText { get; set; } = "";

    /// <summary>Worauf die Karte tatsächlich fällt; ein Rollenblock gibt die Karte unter der Maus weiter (Tausch).</summary>
    internal virtual IAblageziel ZielFuer(GongSolutions.Wpf.DragDrop.IDropInfo info, BrettKarte karte) => this;

    /// <summary>Eine Karte schwebt über dem Ziel (0.11.2): ein Reiter öffnet sich, ein zugeklappter Kanal klappt nach einer Weile auf.</summary>
    internal virtual void Beruehrt()
    {
    }

    public void DropHint(GongSolutions.Wpf.DragDrop.IDropHintInfo dropHintInfo)
    {
    }

    public void DragEnter(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo) => DragOver(dropInfo);

    public void DragOver(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo)
    {
        ArgumentNullException.ThrowIfNull(dropInfo);
        // Gezogen wird eine Karte oder, seit 0.11.1, eine Person aus der Kanalliste rechts (ein Feld dieser Person).
        var karte = dropInfo.Data switch
        {
            BrettKarte k => k,
            KanalPerson p => p.Karte,
            _ => null,
        };
        if (Brett is not { } brett || karte is null)
        {
            dropInfo.Effects = System.Windows.DragDropEffects.None;
            return;
        }
        Beruehrt();
        var ziel = ZielFuer(dropInfo, karte);
        var bewertung = dropInfo.Data is KanalPerson person ? brett.Bewerten(person, ziel) : brett.Bewerten(karte, ziel);
        Ablagezustand = bewertung.Wirkung;
        AblageText = bewertung.Text;
        // Die Vorschau am freien Platz, auf dem die Karte landen würde (0.11.1, Punkt 4); gong ruft das bei jeder Mausbewegung.
        brett.VorschauZeigen(karte, ziel, bewertung);
        dropInfo.Effects = bewertung.Moeglich ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        dropInfo.EffectText = bewertung.Text;
        dropInfo.DropTargetAdorner = GongSolutions.Wpf.DragDrop.DropTargetAdorners.Highlight;
    }

    /// <summary>Beim Verlassen: Rand und Satz weg, die Vorschau blendet weich aus, wenn sie zu diesem Ziel gehört.</summary>
    public void DragLeave(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo)
    {
        Ablagezustand = Ablagewirkung.Keine;
        AblageText = "";
        Brett?.VorschauVerbergen(this, sofort: false);
    }

    /// <summary>
    /// Beim Loslassen: Die Vorschau geht sofort (die Karte kommt an die Stelle und gleitet dorthin, <see cref="Ablagepunkt"/>), dann legt das Brett ab.
    /// </summary>
    public void Drop(GongSolutions.Wpf.DragDrop.IDropInfo dropInfo)
    {
        ArgumentNullException.ThrowIfNull(dropInfo);
        Ablagezustand = Ablagewirkung.Keine;
        AblageText = "";
        if (Brett is not { } brett) return;
        brett.VorschauVerbergen(null, sofort: true);
        var punkt = new Ablagepunkt(dropInfo.VisualTarget, dropInfo.DropPosition, dropInfo.DragInfo?.PositionInDraggedItem ?? default);
        switch (dropInfo.Data)
        {
            case BrettKarte karte:
                _ = brett.AblegenAsync(karte, ZielFuer(dropInfo, karte), punkt);
                break;
            case KanalPerson person:
                _ = brett.AblegenAsync(person, ZielFuer(dropInfo, person.Karte), punkt);
                break;
        }
    }
}

/// <summary>
/// Wo losgelassen wurde (0.11.1, Punkt 4): <paramref name="Punkt"/> in den Einheiten von <paramref name="Bezug"/> (das Element unter der Maus,
/// gongs VisualTarget), <paramref name="Griff"/> die Stelle im gezogenen Element, an der die Maus es hielt (dort hing das Ziehbild). Ohne Bezug
/// (Modelltests) gleitet nichts, die Karte blendet nur auf.
/// </summary>
public sealed record Ablagepunkt(System.Windows.UIElement? Bezug, System.Windows.Point Punkt, System.Windows.Point Griff);

/// <summary>
/// Eine Karte steht nach einer Ablage (vorläufig, "wartet") in ihrem Block (<see cref="BrettViewModel.Gelandet"/>): Die Ansicht lässt sie vom
/// <see cref="Ablagepunkt"/> an ihren Platz gleiten.
/// </summary>
public sealed record Landung(BrettKarte Karte, RollenBlock Block, Ablagepunkt Punkt);

/// <summary>
/// Eine Person in einem Kanal der Kanalliste rechts (0.11.1, Sarevocs Befund: eine Person in der Kanal-Tabelle ließ sich nicht entfernen): ein
/// Feld dieser Person auf diesem Kanal, bei einem Kanalnetz ein Ch. Steht als Mini-Kachel unter dem Kanal; ein Zusatzkanal lässt sich ziehen:
/// auf "hört" oder "hört + sendet" desselben Kanals oder auf einen anderen Kanal zieht das Feld um (ein <c>felderPatch</c> mit Feldnummer, kein
/// zweites Feld); in die Liste links nimmt es den Zusatzkanal weg, wie das Kreuz an der Kachel; auf einen Platz wirkt sie wie die Karte der
/// Person. Ein Feld aus der Rolle trägt kein Kreuz und ist nicht ziehbar (Sarevoc Q116, 12.10.2026; den Kanal bestimmt der Platz, der Hilfetext
/// sagt es; bis zur Prüfung 0.11.1 zog es innerhalb seines Kanals um und antwortete links rot). <see cref="BrettViewModel.Bewerten(KanalPerson, IAblageziel)"/>
/// kennt die Regeln weiter, falls doch etwas ankommt.
/// </summary>
public sealed record KanalPerson(BrettKarte Karte, NetzZiel Netz, byte Feld, ushort NetzId, string Kanal, bool Hoeren, bool Senden, bool Fest, bool Zusatz)
{
    public string Name => Karte.Name;

    /// <summary>"Kobold", mit Ch. "Kobold (Schiff 3)", dazu ", hört", wenn die Person dort nur hört.</summary>
    public required string Text { get; init; }

    /// <summary>Für Hilfetext und Vorleser: wer, wo, wie, Zusatzkanal oder aus der Rolle, und was das Ziehen tut.</summary>
    public required string Beschreibung { get; init; }

    public string FeldText => (Feld + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string KreuzHilfe => "Nimmt " + Name + " den Zusatzkanal " + Kanal + " weg.";

    public string KreuzName => "Zusatzkanal " + Kanal + " von " + Name + " entfernen";

    /// <summary>Oberste Person an ihrem Ch. im Stammbaum (0.11.2): die senkrechte Linie beginnt in ihrer Mitte.</summary>
    public bool Erster { get; init; }

    /// <summary>Unterste Person an ihrem Ch. im Stammbaum (0.11.2): die senkrechte Linie endet in ihrer Mitte.</summary>
    public bool Letzter { get; init; }

    /// <summary>Die Farbe ihres Kanals im Stammbaum (0.11.2), für die Linie zu ihr.</summary>
    public System.Windows.Media.Brush? Farbe { get; init; }
}

/// <summary>
/// Eine Rolle in der Rollenwahl der Raid-Seite (seit 0.11.3, wie die Rollenliste im Funkfeld): Nummer und Name, dazu ihre Netze in der Reihenfolge
/// ihrer Felder; die ersten Kanäle davon stehen als Reiter daneben.
/// </summary>
public sealed partial class BaumRolle(ushort id) : ObservableObject
{
    public ushort Id { get; } = id;

    [ObservableProperty]
    public partial string Name { get; internal set; } = "";

    public IReadOnlyList<ushort> Netze { get; internal set; } = [];
}

/// <summary>
/// Ein Kanal im Stammbaum (0.11.2): links der Knoten, rechts daneben seine Ch. (Kanalnetz) oder direkt die Leute. Aufgeklappt mit dem Pfeil;
/// beim Ziehen über einem zugeklappten Kanal klappt er nach einer Weile von selbst auf. Ein Kanalnetz nimmt nur auf einem Ch. etwas an.
/// </summary>
public sealed partial class BaumKanal(ushort netzId) : AblagezielBasis
{
    public ushort NetzId { get; } = netzId;

    [ObservableProperty]
    public partial string Name { get; internal set; } = "";

    [ObservableProperty]
    public partial bool IstKanalnetz { get; internal set; }

    /// <summary>Die Farbe des Kanals: Rahmen am Knoten und die Linien zu seinen Ch.</summary>
    [ObservableProperty]
    public partial System.Windows.Media.Brush? Farbe { get; internal set; }

    [ObservableProperty]
    public partial bool Offen { get; set; }

    /// <summary>Jemand sitzt in diesem Kanal (dann ist die Linie vom Knoten kräftig).</summary>
    [ObservableProperty]
    public partial bool Besetzt { get; internal set; }

    /// <summary>Die Ch. eines Kanalnetzes ohne Ruhe, in der Reihenfolge seiner Liste.</summary>
    public ObservableCollection<BaumCh> Ch { get; } = [];

    /// <summary>Der offene Reiter (seit 0.11.3): darunter steht dieser Kanal.</summary>
    [ObservableProperty]
    public partial bool Aktiv { get; internal set; }

    /// <summary>Die Leute direkt am Kanal, wenn er keine Ch. hat.</summary>
    public ObservableCollection<KanalPerson> Leute { get; } = [];

    /// <summary>Auch die leeren Ch. zeigen (Klick auf "weitere Ch." oder beim Ziehen darüber verweilt); sonst nur Ruhe und die besetzten.</summary>
    internal bool AlleCh { get; set; }

    /// <summary>Beim Ziehen aufgeklappt (Kanal oder leere Ch.): Nach dem Ziehen klappt es wieder zu, wenn niemand darin sitzt.</summary>
    internal bool DurchZiehen { get; set; }

    /// <summary>Der Eintrag "weitere Ch." am Ende der Ch., einmal je Kanal.</summary>
    internal BaumCh? Weitere { get; set; }

    public string Hilfe => IstKanalnetz
        ? Name + ": Pfeil klappt die Ch. nach rechts auf; einen Namen auf einen Ch. ziehen."
        : Name + ": Namen hierher ziehen, dann ist die Person hier und kann sofort funken.";

    partial void OnIstKanalnetzChanged(bool value) => OnPropertyChanged(nameof(Hilfe));

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Hilfe));

    internal override void Beruehrt() => Brett?.KanalOeffnen(this);
}

/// <summary>Ein Ch. eines Kanalnetzes im Stammbaum (0.11.2): Ziel für Namen, rechts daneben die Leute, die dort sind.</summary>
public sealed partial class BaumCh(BaumKanal kanal, ushort kanalId) : AblagezielBasis
{
    public BaumKanal Kanal { get; } = kanal;

    /// <summary>Die Kanal-ID dieses Ch. (Kanalrechnung.Id).</summary>
    public ushort KanalId { get; } = kanalId;

    [ObservableProperty]
    public partial string Name { get; internal set; } = "";

    [ObservableProperty]
    public partial bool Erster { get; internal set; }

    [ObservableProperty]
    public partial bool Letzter { get; internal set; }

    [ObservableProperty]
    public partial bool Besetzt { get; internal set; }

    public ObservableCollection<KanalPerson> Leute { get; } = [];

    /// <summary>Kein Ch., sondern die Zeile "weitere Ch." (0.11.2): Klick zeigt die leeren Ch., beim Ziehen darüber verweilt ebenso.</summary>
    public bool IstWeitere { get; init; }

    /// <summary>Der Ch. Ruhe: ein Platz ohne Sendefunktion (Sarevoc 13.10.2026).</summary>
    public bool IstRuhe => !IstWeitere && Kanalrechnung.IstRuhe(KanalId);

    public string Hilfe => IstWeitere ? Kanal.Name + ": zeigt oder verbirgt die leeren Ch.; beim Ziehen klappen sie von selbst auf, wenn du kurz darauf bleibst."
        : IstRuhe ? Kanal.Name + " Ruhe: ein Platz ohne Sendefunktion. Hierher gezogen, ist die Person im Kanal, sendet dort aber nicht."
        : Kanal.Name + " " + Name + ": Namen hierher ziehen, dann ist die Person hier und kann sofort funken.";

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Hilfe));

    internal override void Beruehrt()
    {
        if (IstWeitere) Brett?.WeitereBeruehrt(this);
    }
}

/// <summary>
/// Eine Person auf dem Brett (RAIDLEITUNG-ENTWURF, Das Brett, Links und Mitte): Online-Punkt, Zeichen, Hauptname, erste
/// Mitgliederrolle, Schloss für Admins, Marke "selbst" oder wer eingeteilt hat. Eine Karte je Konto für die
/// ganze Lebensdauer des Bretts; sie wandert zwischen den Listen, statt neu gebaut zu werden. In den Listen links steht sie seit 0.11.0
/// als Personenkachel (Abzeichen, Anfang, Online, Name; alles andere im Hilfetext), auf den Plätzen weiter als Karte.
/// </summary>
public sealed partial class BrettKarte : AblagezielBasis
{
    internal BrettKarte(uint kontoId) => KontoId = kontoId;

    public uint KontoId { get; }

    /// <summary>In wie vielen Kanälen des Baums die Person sitzt (0.11.2, links neben dem Namen; 0 zeigt nichts).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KanalzahlText), nameof(KanalzahlHilfe))]
    public partial int Kanalzahl { get; internal set; }

    public string KanalzahlText => Kanalzahl > 0 ? Kanalzahl.ToString(System.Globalization.CultureInfo.CurrentCulture) : "";

    public string KanalzahlHilfe => Kanalzahl == 1 ? Name + " sitzt in einem Kanal." : Name + " sitzt in " + KanalzahlText + " Kanälen.";

    /// <summary>Hauptname (SC-Name), sonst Kontoname.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Anfang))]
    public partial string Name { get; internal set; } = "";

    /// <summary>Der erste Buchstabe des Namens, groß: steht auf der Personenkachel ohne Abzeichen im Kreis (0.11.0).</summary>
    public string Anfang => Name.Length == 0 ? "?" : System.Globalization.StringInfo.GetNextTextElement(Name).ToUpper(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Discord-Name wie beim Antrag eingetippt, für Tooltip und Suche.</summary>
    [ObservableProperty]
    public partial string? DiscordName { get; internal set; }

    [ObservableProperty]
    public partial bool Online { get; internal set; }

    /// <summary>Alle sichtbaren Zeichen und Abzeichen (seit 0.11.7); der Hilfetext nennt sie.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Abzeichen), nameof(HatAbzeichen), nameof(AbzeichenTitel))]
    public partial IReadOnlyList<Zeichenbild> Zeichen { get; internal set; } = [];

    /// <summary>Das Abzeichen, das die Person vorn zeigt (0.11.7), sonst <c>null</c>; gesetzt vor <see cref="Zeichen"/>.</summary>
    internal string? AbzeichenVorn { get; set; }

    /// <summary>
    /// Das Abzeichen der Personenkachel in den Listen links und auf der Karte (0.11.0; seit 0.11.7 das gewählte, sonst das erste sichtbare), sonst
    /// <c>null</c>.
    /// </summary>
    public System.Windows.Media.ImageSource? Abzeichen => Zeichensatz.Vorn(Zeichen, AbzeichenVorn)?.Bild;

    /// <summary>Alle sichtbaren als Text, für den Tooltip am Abzeichen der Karte.</summary>
    public string AbzeichenTitel => string.Join(", ", Zeichen.Select(z => z.Titel));

    public bool HatAbzeichen => Abzeichen is not null;

    /// <summary>Erste Mitgliederrolle aus der Mitgliedskarte, klein unter dem Namen.</summary>
    [ObservableProperty]
    public partial string? Rolle { get; internal set; }

    /// <summary>"selbst", das Kürzel dessen, der eingeteilt hat, oder "reserviert".</summary>
    [ObservableProperty]
    public partial string? Marke { get; internal set; }

    /// <summary>ADMIN: teilt sich selbst ein; nur ein Admin verschiebt ihn, außer er spielt heute als Funker.</summary>
    [ObservableProperty]
    public partial bool IstAdmin { get; internal set; }

    /// <summary>Ein ADMIN spielt heute als Funker (6.15): einteilbar wie jeder, Marke "als Funker" statt Schloss.</summary>
    [ObservableProperty]
    public partial bool AlsFunker { get; internal set; }

    /// <summary>Schloss: ein Admin ohne Funker-Modus (teilt sich selbst ein).</summary>
    [ObservableProperty]
    public partial bool Geschuetzt { get; internal set; }

    /// <summary>Noch ohne SC-Namen (Marke).</summary>
    [ObservableProperty]
    public partial bool OhneScName { get; internal set; }

    /// <summary>Bei Leuten ohne Platz: "zuletzt: Schiff 2 Gunner".</summary>
    [ObservableProperty]
    public partial string? Zuletzt { get; internal set; }

    [ObservableProperty]
    public partial bool Spricht { get; internal set; }

    /// <summary>Unterwegs zum Server: halb durchsichtig mit "wartet".</summary>
    [ObservableProperty]
    public partial bool Wartet { get; internal set; }

    /// <summary>Passt zur Suche (oder es wird nicht gesucht).</summary>
    [ObservableProperty]
    public partial bool Treffer { get; internal set; } = true;

    [ObservableProperty]
    public partial bool Gewaehlt { get; internal set; }

    /// <summary>Wird gerade gezogen: die Kachel bleibt bis zum Loslassen gedimmt an ihrer Stelle (0.11.0).</summary>
    [ObservableProperty]
    public partial bool Gezogen { get; internal set; }

    /// <summary>Alles in einem Satz, für Vorleser und Tooltip.</summary>
    [ObservableProperty]
    public partial string Beschreibung { get; internal set; } = "";

    /// <summary>Der Platz, an dem die Karte gerade steht (Einheit, Rolle); beide <c>null</c> ohne Platz.</summary>
    public ushort? SchiffId { get; internal set; }

    public ushort? RolleId { get; internal set; }

    /// <summary>Die Mitgliederrollen und Wünsche als Text, sobald die Karte geladen ist.</summary>
    [ObservableProperty]
    public partial string? Rollen { get; internal set; }

    /// <summary>Die Kennungen der gezeigten Zeichen, damit die Bilder nur bei einer Änderung neu gesetzt werden.</summary>
    internal string? ZeichenStand { get; set; }

    public override string ToString() => Name;
}

/// <summary>
/// Ein freier Platz in einem Rollenblock, gestrichelt (Ablageziel wie der Block). Beim Ziehen zeigt der erste freie Platz des Ziels die Vorschau
/// (0.11.1, Sarevocs Punkt 4): die gezogene Person gedämpft an der Stelle, an der sie landen würde. <see cref="VorschauSichtbar"/> blendet sie in
/// der Ansicht weich ein und aus; <see cref="Vorschau"/> bleibt beim Verlassen stehen, damit das Ausblenden noch etwas zu zeigen hat, und geht
/// beim Loslassen sofort, weil die Karte dann an die Stelle kommt. In einem Block ohne Platzgrenze (keine freien Plätze, Rollen ohne Einheit)
/// hängt das Brett einen solchen Platz nur für die Vorschau ein, dort, wo die Karte nach Namen stünde, und nimmt ihn mit ihr wieder fort.
/// </summary>
public sealed partial class FreierPlatz(RollenBlock block, int nummer) : ObservableObject
{
    public RollenBlock Block { get; } = block;

    internal int Nummer { get; } = nummer;

    /// <summary>Die Karte, die hier landen würde, solange die Vorschau steht oder ausblendet.</summary>
    [ObservableProperty]
    public partial BrettKarte? Vorschau { get; internal set; }

    [ObservableProperty]
    public partial bool VorschauSichtbar { get; internal set; }
}

/// <summary>Eine Rolle auf einer Einheit (oder ohne Einheit): Name, Belegung, Karten und freie Plätze.</summary>
public sealed partial class RollenBlock : AblagezielBasis
{
    internal RollenBlock(ushort? schiffId, ushort rolleId)
    {
        SchiffId = schiffId;
        RolleId = rolleId;
    }

    public ushort? SchiffId { get; }

    public ushort RolleId { get; }

    [ObservableProperty]
    public partial string Titel { get; internal set; } = "";

    /// <summary>"1/3"; ohne Grenze nur die Zahl.</summary>
    [ObservableProperty]
    public partial string Belegung { get; internal set; } = "";

    /// <summary>Karten zuerst, dann die freien Plätze.</summary>
    public ObservableCollection<object> Eintraege { get; } = [];

    /// <summary>Die Netze der Rolle auf dieser Einheit, für den Text an der Maus (A22).</summary>
    [ObservableProperty]
    public partial string Netze { get; internal set; } = "";

    /// <summary>Über einer Karte im Block heißt die Ablage Tausch mit ihr.</summary>
    internal override IAblageziel ZielFuer(GongSolutions.Wpf.DragDrop.IDropInfo info, BrettKarte karte) =>
        info?.TargetItem is BrettKarte andere && !ReferenceEquals(andere, karte) ? andere : this;
}

/// <summary>Ein Einheitsnetz im Kopf einer Spalte, mit Sprechpunkt.</summary>
public sealed partial class EinheitNetz(ushort netzId) : ObservableObject
{
    public ushort NetzId { get; } = netzId;

    [ObservableProperty]
    public partial string Name { get; internal set; } = "";

    [ObservableProperty]
    public partial bool Spricht { get; internal set; }
}

/// <summary>Eine Spalte des Bretts: eine Einheit (oder "ohne Einheit") mit Kopf, Ampel, Netzen und Rollenblöcken.</summary>
public sealed partial class EinheitSpalte : AblagezielBasis
{
    internal EinheitSpalte(ushort? schiffId) => SchiffId = schiffId;

    /// <summary>Die Einheit; <c>null</c> für die Rollen ohne Einheit.</summary>
    public ushort? SchiffId { get; }

    [ObservableProperty]
    public partial string Name { get; internal set; } = "";

    [ObservableProperty]
    public partial string? Typ { get; internal set; }

    /// <summary>"5/8".</summary>
    [ObservableProperty]
    public partial string Belegung { get; internal set; } = "";

    [ObservableProperty]
    public partial Ampelstufe Ampel { get; internal set; }

    /// <summary>"kein Pilot", "vollständig".</summary>
    [ObservableProperty]
    public partial string? AmpelText { get; internal set; }

    public ObservableCollection<EinheitNetz> Netze { get; } = [];

    public ObservableCollection<RollenBlock> Bloecke { get; } = [];
}

/// <summary>Spaltengruppe: Schiffe, Infanterie, Orga, Rollen ohne Einheit.</summary>
public sealed partial class EinheitGruppe(string schluessel) : ObservableObject
{
    internal string Schluessel { get; } = schluessel;

    [ObservableProperty]
    public partial string Titel { get; internal set; } = "";

    public ObservableCollection<EinheitSpalte> Spalten { get; } = [];
}

/// <summary>Ablage in einem Netz rechts: als Zusatznetz hören oder hören und senden.</summary>
public sealed partial class NetzAblage(NetzZiel netz, bool senden) : AblagezielBasis
{
    public NetzZiel Netz { get; } = netz;

    public bool Senden { get; } = senden;

    public string Text => Senden ? "hört + sendet" : "hört";
}

/// <summary>Ein Netz rechts: Global (nur Anzeige) oder ein Sondernetz ohne Einheit als Ablageziel für Zusatznetze.</summary>
public sealed partial class NetzZiel : ObservableObject
{
    internal NetzZiel(ushort netzId, bool istGlobal)
    {
        NetzId = netzId;
        IstGlobal = istGlobal;
        Hoeren = new NetzAblage(this, false);
        HoerenSenden = new NetzAblage(this, true);
    }

    public ushort NetzId { get; }

    public bool IstGlobal { get; }

    /// <summary>Ein Kanalnetz (6.16): Eine Ablage fragt erst nach dem Ch.</summary>
    public bool IstKanalnetz { get; internal set; }

    [ObservableProperty]
    public partial string Name { get; internal set; } = "";

    /// <summary>"4 drin".</summary>
    [ObservableProperty]
    public partial string Mitglieder { get; internal set; } = "";

    [ObservableProperty]
    public partial bool Spricht { get; internal set; }

    public NetzAblage Hoeren { get; }

    public NetzAblage HoerenSenden { get; }

    /// <summary>Wer in diesem Kanal ist (seit 0.11.1): je Feld einer Person eine Mini-Kachel, nach Namen; folgt jedem Push.</summary>
    public ObservableCollection<KanalPerson> Personen { get; } = [];
}

/// <summary>Die Liste links als Ziel: zurück auf Standard (ohne Platz).</summary>
public sealed partial class TeilnehmerZiel : AblagezielBasis;

/// <summary>Eine Phase in der Leiste im Kopf des Bretts ("2 Entern"); die laufende ist hervorgehoben.</summary>
public sealed partial class PhasenKnopf(byte nr) : ObservableObject
{
    public byte Nr { get; } = nr;

    [ObservableProperty]
    public partial string Text { get; internal set; } = "";

    [ObservableProperty]
    public partial bool Laufend { get; internal set; }

    /// <summary>Für Vorleser: "Phase 2 Entern, läuft".</summary>
    [ObservableProperty]
    public partial string Beschreibung { get; internal set; } = "";
}

/// <summary>Ein Feld der gewählten Person im Detailfeld rechts.</summary>
public sealed record DetailFeld(byte Feld, ushort NetzId, string Netz, string Art, bool Zusatz)
{
    public string FeldText => (Feld + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Die Einsatzleitung kann den Kanal dieses Felds für die Person sperren (6.17, Server ab 0.6.0): nie Global (auch nicht dessen Ch. 1) oder eine Ruhe,
    /// gegen einen Admin nur mit eigenem Admin-Rang.
    /// </summary>
    public bool Sperrbar { get; init; }

    /// <summary>Die Sperre, die diesen Kanal für die Person sperrt (auf ihn oder sein ganzes Kanalnetz), sonst <c>null</c>.</summary>
    public KanalsperreInfo? Sperre { get; init; }

    public bool Gesperrt => Sperre is not null;

    /// <summary>Das Kanalnetz, wenn das Feld einen Kanal mit Ch. trägt (auch die Ruhe), sonst <c>null</c> (seit 0.11.0).</summary>
    public ushort? Kanalnetz { get; init; }

    /// <summary>Der Ch. des Felds (0 ist die Ruhe), sonst <c>null</c>.</summary>
    public byte? Ch { get; init; }

    /// <summary>Wie das Feld hört, sendet und ob es fest ist: Eine Wahl des Ch. schickt es unverändert mit (felderPatch).</summary>
    public bool Hoeren { get; init; }

    public bool Senden { get; init; }

    public bool Fest { get; init; }

    /// <summary>
    /// Die Einsatzleitung kann den Ch. dieses Felds für die Person wählen (Q46, seit 0.11.0): Server ab 0.6.0, ein Kanalnetz außer
    /// Ch. 1 von Global, die Person nicht geschützt und das ganze Kanalnetz nicht für sie gesperrt. Der Ch. ist ein Vorschlag; die Person darf
    /// ihn selbst wieder ändern (Q45).
    /// </summary>
    public bool ChWaehlbar { get; init; }

    /// <summary>
    /// Die Knöpfe des Felds stehen in einer eigenen Zeile unter dem Kanaltext (seit 0.11.0, Befund der Prüfung): An einem Zusatzkanal mit
    /// Ch. kommen "Ch.", "entfernen" und "sperren" zusammen, daneben blieben dem Kanaltext nur etwa 29 Einheiten, und er bräche mitten im Wort.
    /// </summary>
    public bool KnoepfeUnten => Zusatz && ChWaehlbar;
}

/// <summary>Eine Kanalsperre der gewählten Person ohne eigenes Feld (etwa nach einem Ch.-Wechsel): Kanal, "gesperrt bis 04:00 (von ...)".</summary>
public sealed record DetailSperre(KanalsperreInfo Sperre, string Kanal, string Text);

/// <summary>Eine laufende Sperre für alle ohne Einsatzleitung (6.17) an der Kanalliste: Kanal, "bis 04:00 (von Nordwind)", mit "freigeben".</summary>
public sealed record SperreFuerAlle(KanalsperreInfo Sperre, string Kanal, string Text);

/// <summary>
/// Ein Knopf beim Sperren für alle (STUFE3-ENTWURF 5.6, Sarevoc Q9): erst je Kanal, bei einem Kanal mit Ch. danach "alle Ch." und je Ch.
/// <see cref="Weiter"/>: Der Kanal hat Ch., als Nächstes kommt die Wahl des Ch.; sonst kommt die Rückfrage.
/// </summary>
public sealed record AlleSperrKnopf(ushort NetzId, string Text, bool Waehlbar, string Hilfe, bool Weiter)
{
    /// <summary>Warum der Knopf gesperrt ist (für <c>Hilfe.Gesperrt</c>), sonst <c>null</c>.</summary>
    public string? Grund => Waehlbar ? null : Hilfe;
}

/// <summary>
/// Ein Ch. im Raster nach einer Ablage auf ein Kanalnetz (STUFE3-ENTWURF 5.6) oder für ein Feld der gewählten Person (seit 0.11.0): Name,
/// wählbar oder warum nicht.
/// </summary>
public sealed record ChKnopf(byte Ch, string Text, bool Waehlbar, string Hilfe)
{
    /// <summary>Warum der Knopf gesperrt ist (für <c>Hilfe.Gesperrt</c>), sonst <c>null</c>.</summary>
    public string? Grund => Waehlbar ? null : Hilfe;
}

/// <summary>Ein Ziel für "Setzen auf ..." im Kontextmenü einer Karte.</summary>
public sealed record SetzenZiel(string Text, RollenBlock Block);
