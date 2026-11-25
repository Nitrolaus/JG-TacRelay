// JG TacRelay - Maintainer: Sarevoc
using System.Diagnostics;
using Funk.Protokoll.Schutz;

namespace Funk.Server.Lage;

/// <summary>
/// Weiterleitung im Relay-Thread (6.3, 6.4): Form und Ratenlimit, Senderecht, Phasenbuch je Sender,
/// Empfängerbestimmung, Stempel, erzeugtes ENDE, DurchsageStart/-Ende und gedrosselte SendeFehler.
/// Deterministisch: die Zeit kommt als ms von außen, der Schnappschuss wird je Aufruf übergeben (der
/// Aufrufer liest ihn genau einmal je Paket). Nach dem Anlauf je Sitzung allokiert ein Paket nichts.
/// Nicht threadsicher; gehört genau einem Thread.
/// <para>
/// <c>jetzt</c> muss monoton sein (4.8 Token-Eimer, 6.4 Phasenfristen) und dieselbe Basis haben wie
/// <see cref="Stummschaltung.Bis"/> und <see cref="Entzug.Zeit"/> des Zustandsschreibers (Unix-ms). Der Aufrufer legt
/// eine monotone Uhr einmal an die Unix-Zeit an; ein Rücksprung der Systemuhr darf hier nicht ankommen.
/// </para>
/// </summary>
public sealed class Weiterleiter
{
    private const Paketkennzeichen Uebernommen = Paketkennzeichen.Ende | Paketkennzeichen.Preroll | Paketkennzeichen.Va;
    private const int MaxTreffer = Fristen.FelderMax;

    private readonly Dictionary<uint, Sender> _sender = [];
    private readonly Stack<Empfaengerstand> _vorrat = new();
    private readonly IZustellung? _zustellung;
    private readonly long[] _verworfen = new long[(int)SendeFehlerCode.Grenze + 1];
    private readonly long[] _gestrichen = new long[(int)SendeFehlerCode.Grenze + 1];

    public Weiterleiter(IZustellung? zustellung = null) => _zustellung = zustellung;

    /// <summary>Ganz verworfene Pakete je Grund.</summary>
    public long Verworfen(SendeFehlerCode grund) => _verworfen[(int)grund];

    /// <summary>Gestrichene Ziele je Grund (6.3: jede Streichung wird gezählt).</summary>
    public long Gestrichen(SendeFehlerCode grund) => _gestrichen[(int)grund];

    /// <summary>Pakete von Sitzungen, die im Schnappschuss nicht aktiv sind (stumm verworfen).</summary>
    public long OhneSitzung { get; private set; }

    /// <summary>ENDE-Pakete einer Phase, die weder läuft noch gerade abgelegt ist: niemand ist bedient, nichts geht hinaus.</summary>
    public long EndeOhnePhase { get; private set; }

    /// <summary>Ein Paket verarbeiten. Die Ausgaben gehen synchron an <paramref name="ziel"/>.</summary>
    public Weiterleitungsergebnis Verarbeiten(Schnappschuss z, in Sprachpaket paket, long jetzt, IWeiterleitungsZiel ziel)
    {
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(ziel);

        var si = z.SitzungsIndex(paket.Sitzung);
        if (si < 0)
        {
            OhneSitzung++;
            return new Weiterleitungsergebnis(true, SendeFehlerCode.Keiner, 0, 0);
        }
        var konto = z.KontoAn(si);
        var sender = Holen(paket.Sitzung, konto.Id, jetzt);
        Span<SendeFehlerEintrag> gestrichen = stackalloc SendeFehlerEintrag[Fristen.ZieleDurchsageMax];

        var form = Senderecht.FormPruefen(paket);
        if (form != SendeFehlerCode.Keiner)
        {
            gestrichen[0] = new SendeFehlerEintrag(form, 0, 0);
            return Verwerfen(z, sender, paket, form, gestrichen[..1], schliessen: false, jetzt, ziel);
        }

        if (!sender.Rate(paket.Opus.Length, jetzt))
        {
            if (!sender.RateGemeldet || jetzt - sender.LetzteRate >= Fristen.RateMeldungAbstandMs)
            {
                sender.RateGemeldet = true;
                sender.LetzteRate = jetzt;
                ziel.RateUeberschritten(paket.Sitzung, konto.Id);
            }
            gestrichen[0] = new SendeFehlerEintrag(SendeFehlerCode.Grenze, 0, 0);
            return Verwerfen(z, sender, paket, SendeFehlerCode.Grenze, gestrichen[..1], schliessen: false, jetzt, ziel);
        }

        Span<ushort> erlaubt = stackalloc ushort[Fristen.ZieleDurchsageMax];
        var code = Senderecht.Pruefen(z, si, paket, jetzt, erlaubt, gestrichen, out var ne, out var ng);
        for (var i = 0; i < ng; i++) _gestrichen[(int)gestrichen[i].Code]++;
        if (code != SendeFehlerCode.Keiner)
            return Verwerfen(z, sender, paket, code, gestrichen[..ng], schliessen: true, jetzt, ziel);
        if (ng > 0) Melden(sender, gestrichen[..ng], ganzVerworfen: false, jetzt, ziel);

        var kopien = Verteilen(z, si, konto.Id, sender, paket, erlaubt[..ne], jetzt, ziel);
        return new Weiterleitungsergebnis(false, SendeFehlerCode.Keiner, kopien, ng);
    }

    /// <summary>
    /// Zeitgeber, etwa alle 20-50 ms: DurchsageEnde nach 300 ms ohne Paket, Phasen nach 2 s schließen, abgelegte
    /// Phasen nach 2 s freigeben, gedrosselte SendeFehler nachreichen.
    /// </summary>
    public void Takt(Schnappschuss z, long jetzt, IWeiterleitungsZiel ziel)
    {
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(ziel);
        foreach (var s in _sender.Values)
        {
            var ph = s.Phase;
            if (ph.Laeuft)
            {
                if (jetzt - ph.Letztes >= Fristen.DurchsagePauseMs)
                {
                    foreach (var st in ph.Bedient.Values)
                    {
                        if (!st.DurchsageOffen) continue;
                        st.DurchsageOffen = false;
                        if (z.IstAktiv(st.Sitzung)) ziel.DurchsageEnde(st.Sitzung, ph.Absender, ph.Nummer);
                    }
                }
                if (jetzt - ph.Letztes > Fristen.PhaseOffenMs) PhaseSchliessen(z, s, mitEnde: false, ziel);
            }
            if (s.Vorige.Belegt && jetzt - s.Vorige.Letztes > Fristen.PhaseOffenMs) Freigeben(s.Vorige);
            if (s.HatOffeneFehler && jetzt - s.LetzteMeldung >= Fristen.SendeFehlerAbstandMs) Abschicken(s, jetzt, ziel);
        }
    }

    /// <summary>
    /// Sitzung beendet (Befehl an den Relay-Thread). Als Sender: ihre Empfänger bekommen ein erzeugtes ENDE und
    /// gegebenenfalls DurchsageEnde (4.5). Als Empfänger: sie verschwindet aus allen Phasen.
    /// </summary>
    public void SitzungBeendet(Schnappschuss z, uint sitzung, long jetzt, IWeiterleitungsZiel ziel)
    {
        ArgumentNullException.ThrowIfNull(z);
        ArgumentNullException.ThrowIfNull(ziel);
        if (_sender.Remove(sitzung, out var s))
        {
            if (s.Phase.Laeuft) PhaseSchliessen(z, s, mitEnde: s.Phase.Offen(jetzt), ziel);
            Freigeben(s.Phase);
            Freigeben(s.Vorige);
        }
        foreach (var andere in _sender.Values)
        {
            if (andere.Phase.Bedient.Remove(sitzung, out var st)) _vorrat.Push(st);
            if (andere.Vorige.Bedient.Remove(sitzung, out st)) _vorrat.Push(st);
        }
    }

    private Weiterleitungsergebnis Verwerfen(Schnappschuss z, Sender sender, in Sprachpaket paket, SendeFehlerCode code,
        ReadOnlySpan<SendeFehlerEintrag> eintraege, bool schliessen, long jetzt, IWeiterleitungsZiel ziel)
    {
        _verworfen[(int)code]++;
        Melden(sender, eintraege, ganzVerworfen: true, jetzt, ziel);
        // Verliert der Sender das Recht, bekommen die Hörer seiner laufenden Phase ein ENDE statt Nachblubbern (6.4, F8).
        // Wie in Verteilen: die laufende Phase endet nur durch eigene Pakete oder durch ein Paket, das sie ablöst.
        var ph = sender.Phase;
        if (schliessen && (ph.Passt(paket, jetzt) || LoestAb(sender, paket, jetzt)))
            PhaseSchliessen(z, sender, mitEnde: ph.Offen(jetzt), ziel);
        return new Weiterleitungsergebnis(true, code, 0, eintraege.Length);
    }

    /// <summary>
    /// Löst das Paket die laufende Phase ab (6.4: neue Phase)? Jedes Nicht-ENDE-Paket eines anderen Schlüssels tut das,
    /// ein ENDE nur bei gleicher Nummer (fehlerhafter Client). Späte ENDE-Kopien der abgelegten oder einer älteren
    /// Phase tun es nie: der Client sendet ENDE dreimal (5.6) und setzt nach C4 sofort mit einer neuen Phase fort.
    /// </summary>
    private static bool LoestAb(Sender sender, in Sprachpaket paket, long jetzt)
    {
        var ph = sender.Phase;
        if (!ph.Laeuft || ph.Passt(paket, jetzt)) return false;
        if (!paket.IstEnde) return true;
        return ph.Nummer == paket.Sprechphase && !sender.Vorige.GleichePhase(paket, jetzt);
    }

    private int Verteilen(Schnappschuss z, int si, uint absender, Sender sender, in Sprachpaket paket,
        ReadOnlySpan<ushort> erlaubt, long jetzt, IWeiterleitungsZiel ziel)
    {
        var ph = sender.Phase;
        var nachreichen = false;
        if (!ph.Passt(paket, jetzt))
        {
            if (paket.IstEnde && sender.Vorige.GleichePhase(paket, jetzt))
            {
                // 6.4: weitere ENDE-Kopien gehen nur an p.bedient, auch wenn schon eine neue Phase läuft.
                ph = sender.Vorige;
                nachreichen = true;
            }
            else
            {
                if (LoestAb(sender, paket, jetzt)) PhaseSchliessen(z, sender, mitEnde: ph.Offen(jetzt), ziel);
                if (paket.IstEnde)
                {
                    // ENDE einer Phase, in der niemand bedient ist: nichts geht hinaus (6.4).
                    EndeOhnePhase++;
                    return 0;
                }
                ph = sender.Phase;
                ph.Beginnen(paket, absender);
            }
        }
        if (!nachreichen) ph.Letztes = jetzt;

        Span<int> index = stackalloc int[Fristen.ZieleDurchsageMax];
        for (var i = 0; i < erlaubt.Length; i++)
            index[i] = erlaubt[i] == Netz.GlobalId ? -1 : z.NetzIndex(erlaubt[i]);
        index = index[..erlaubt.Length];

        var durchsage = paket.Zielart == Sendeart.Durchsage;
        var anAlle = durchsage && paket.Ziele.IsEmpty;
        var ende = paket.IstEnde;
        Span<ushort> treffer = stackalloc ushort[MaxTreffer];
        Span<byte> felder = stackalloc byte[MaxTreffer];
        var kopien = 0;

        for (var r = 0; r < z.SitzungAnzahl; r++)
        {
            if (r == si) continue;
            var rs = z.SitzungIdAn(r);
            ph.Bedient.TryGetValue(rs, out var st);
            // Eine abgelegte Phase bekommt keine neuen Empfänger mehr.
            if (nachreichen && st is null) continue;
            var tn = 0;
            Stempelart art;
            if (durchsage)
            {
                art = Stempelart.Durchsage;
                if (!anAlle) tn = Schnitt(z, r, erlaubt, index, hoerenNoetig: false, jetzt, treffer, felder);
            }
            else
            {
                tn = Schnitt(z, r, erlaubt, index, hoerenNoetig: true, jetzt, treffer, felder);
                art = tn > 0 && treffer[0] == Netz.GlobalId ? Stempelart.Global : Stempelart.Netz;
            }
            var empfangen = anAlle || tn > 0;
            if (_zustellung is not null && !_zustellung.Zustellbar(rs, paket.Sitzung, paket.Sprechphase, empfangen ? art : st?.Art ?? art))
                continue;

            if (!empfangen)
            {
                if (st is not null)
                {
                    // Wer herausfällt, bekommt einmal ein erzeugtes ENDE (6.4). Aus einer abgelegten Phase hatte er es schon.
                    if (!nachreichen)
                    {
                        ErzeugtesEnde(ph, st, ziel);
                        if (st.DurchsageOffen) ziel.DurchsageEnde(rs, ph.Absender, ph.Nummer);
                    }
                    ph.Bedient.Remove(rs);
                    _vorrat.Push(st);
                }
                continue;
            }
            if (ende && st is null) continue;

            var kz = paket.Kennzeichen & Uebernommen;
            if (st is null)
            {
                kz |= Paketkennzeichen.Beginn;
                st = Leihen(rs);
                ph.Bedient.Add(rs, st);
            }
            var netze = treffer[..tn];
            // Nach dem ENDE (etwa ein umgeordnetes spätes Paket) öffnet sich kein neues Banner.
            if (art != Stempelart.Netz && !st.DurchsageOffen && !ende && !ph.Beendet)
            {
                st.DurchsageOffen = true;
                ziel.DurchsageStart(rs, absender, art, netze, paket.Sprechphase);
            }
            else if (art == Stempelart.Netz && st.DurchsageOffen)
            {
                // Global wurde gestrichen: der Empfänger fällt aus dem Global-Teil heraus (4.5 DurchsageEnde).
                st.DurchsageOffen = false;
                ziel.DurchsageEnde(rs, absender, paket.Sprechphase);
            }
            st.Art = art;
            st.LetzteFrameNr = paket.FrameNr;
            netze.CopyTo(st.Netze);
            st.NetzAnzahl = tn;
            ziel.Sprache(new Stempel(rs, absender, paket.FrameNr, paket.Sprechphase, kz, art, false), netze, paket.Opus);
            kopien++;
        }

        if (ende)
        {
            ph.Beendet = true;
            foreach (var b in ph.Bedient.Values)
            {
                if (!b.DurchsageOffen) continue;
                b.DurchsageOffen = false;
                if (z.IstAktiv(b.Sitzung)) ziel.DurchsageEnde(b.Sitzung, absender, paket.Sprechphase);
            }
        }
        return kopien;
    }

    /// <summary>
    /// Treffer eines Empfängers, geordnet: zuerst Global, dann nach seiner Feldnummer (6.4).
    /// <paramref name="index"/> ist -1 für Global. Ein Kanal, der für den Empfänger gesperrt ist (6.17), trifft nie, auch nicht als
    /// Durchsageziel; Global und die Durchsage an alle laufen nicht hierüber.
    /// </summary>
    private static int Schnitt(Schnappschuss z, int r, ReadOnlySpan<ushort> erlaubt, ReadOnlySpan<int> index,
        bool hoerenNoetig, long jetzt, Span<ushort> treffer, Span<byte> felder)
    {
        var global = false;
        var tn = 0;
        for (var i = 0; i < erlaubt.Length; i++)
        {
            if (index[i] < 0)
            {
                global = true;
                continue;
            }
            var m = z.MitgliedAn(r, index[i]);
            if (m == Schnappschuss.KeinMitglied || (hoerenNoetig && (m & Schnappschuss.BitHoeren) == 0)) continue;
            if (z.GesperrtAn(r, index[i], jetzt, out _)) continue;
            // Global steht, wenn erlaubt, an erster Stelle der aufsteigenden Ziele und ist hier schon bekannt.
            if (tn + (global ? 1 : 0) >= MaxTreffer) break;
            var feld = (byte)(m & Schnappschuss.FeldMaske);
            var pos = tn;
            while (pos > 0 && felder[pos - 1] > feld)
            {
                felder[pos] = felder[pos - 1];
                treffer[pos] = treffer[pos - 1];
                pos--;
            }
            felder[pos] = feld;
            treffer[pos] = erlaubt[i];
            tn++;
        }
        if (global)
        {
            for (var pos = tn; pos > 0; pos--) treffer[pos] = treffer[pos - 1];
            treffer[0] = Netz.GlobalId;
            tn++;
        }
        return tn;
    }

    /// <summary>
    /// Schließt die laufende Phase: erzeugtes ENDE (falls <paramref name="mitEnde"/>) und DurchsageEnde an ihre
    /// Bedienten. Danach ist sie abgelegt: ihre Bedienten bleiben bis zu 2 s für späte ENDE-Kopien (6.4), die vorige
    /// Ablage wird frei.
    /// </summary>
    private void PhaseSchliessen(Schnappschuss z, Sender sender, bool mitEnde, IWeiterleitungsZiel ziel)
    {
        var ph = sender.Phase;
        foreach (var st in ph.Bedient.Values)
        {
            var erreichbar = z.IstAktiv(st.Sitzung)
                && (_zustellung is null || _zustellung.Zustellbar(st.Sitzung, sender.Sitzung, ph.Nummer, st.Art));
            if (erreichbar)
            {
                if (mitEnde) ErzeugtesEnde(ph, st, ziel);
                if (st.DurchsageOffen) ziel.DurchsageEnde(st.Sitzung, ph.Absender, ph.Nummer);
            }
            st.DurchsageOffen = false;
        }
        ph.Laeuft = false;
        var frei = sender.Vorige;
        Freigeben(frei);
        sender.Vorige = ph;
        sender.Phase = frei;
    }

    /// <summary>Gibt die Empfängerstände einer Phase an den Vorrat zurück und leert sie.</summary>
    private void Freigeben(Phase ph)
    {
        foreach (var st in ph.Bedient.Values) _vorrat.Push(st);
        ph.Bedient.Clear();
        ph.Laeuft = false;
        ph.Belegt = false;
        ph.Beendet = false;
    }

    /// <summary>Erzeugtes ENDE (6.4): Frame-Nr = zuletzt gestempelte + 1, Netze p.netze[r], ohne Opus; VA der Phase bleibt.</summary>
    private static void ErzeugtesEnde(Phase ph, Empfaengerstand st, IWeiterleitungsZiel ziel)
    {
        var kz = Paketkennzeichen.Ende | (ph.Va ? Paketkennzeichen.Va : Paketkennzeichen.Keine);
        ziel.Sprache(new Stempel(st.Sitzung, ph.Absender, st.LetzteFrameNr + 1, ph.Nummer, kz, st.Art, true),
            st.Netze.AsSpan(0, st.NetzAnzahl), []);
    }

    private static void Melden(Sender s, ReadOnlySpan<SendeFehlerEintrag> eintraege, bool ganzVerworfen, long jetzt, IWeiterleitungsZiel ziel)
    {
        var sofort = false;
        foreach (var e in eintraege)
        {
            // ZIEL_ENTZOGEN geht sofort, einmal je Netz und Umstellung (4.5).
            if (e.Code == SendeFehlerCode.ZielEntzogen && s.EntzugNeu(e.NetzId, e.Umstellung)) sofort = true;
            s.Aufnehmen(e);
        }
        if (ganzVerworfen)
        {
            s.VerworfenSeit++;
            s.NurTeilweise = false;
        }
        if (sofort || !s.Gemeldet || jetzt - s.LetzteMeldung >= Fristen.SendeFehlerAbstandMs) Abschicken(s, jetzt, ziel);
    }

    private static void Abschicken(Sender s, long jetzt, IWeiterleitungsZiel ziel)
    {
        if (!s.HatOffeneFehler) return;
        ziel.SendeFehler(s.Sitzung, s.Offen.AsSpan(0, s.OffenAnzahl), s.NurTeilweise, s.VerworfenSeit);
        s.OffenAnzahl = 0;
        s.VerworfenSeit = 0;
        s.NurTeilweise = true;
        s.Gemeldet = true;
        s.LetzteMeldung = jetzt;
    }

    private Sender Holen(uint sitzung, uint kontoId, long jetzt)
    {
        if (!_sender.TryGetValue(sitzung, out var s))
        {
            s = new Sender(sitzung, jetzt);
            _sender.Add(sitzung, s);
        }
        s.KontoId = kontoId;
        return s;
    }

    private Empfaengerstand Leihen(uint sitzung)
    {
        var st = _vorrat.Count > 0 ? _vorrat.Pop() : new Empfaengerstand();
        st.Sitzung = sitzung;
        st.LetzteFrameNr = 0;
        st.Art = Stempelart.Keine;
        st.NetzAnzahl = 0;
        st.DurchsageOffen = false;
        return st;
    }

    /// <summary>Stand eines Empfängers in der laufenden Phase: in <c>p.bedient</c>, mit <c>p.netze[r]</c>.</summary>
    private sealed class Empfaengerstand
    {
        public readonly ushort[] Netze = new ushort[MaxTreffer];
        public uint Sitzung;
        public uint LetzteFrameNr;
        public Stempelart Art;
        public int NetzAnzahl;
        public bool DurchsageOffen;
    }

    /// <summary>
    /// Sprechphase eines Senders (6.4). <see cref="Laeuft"/>: die aktuelle Phase. <see cref="Belegt"/>: Schlüssel und
    /// Bediente gelten, laufend oder abgelegt.
    /// </summary>
    private sealed class Phase
    {
        public readonly ushort[] Ziele = new ushort[Fristen.ZieleDurchsageMax];
        public readonly Dictionary<uint, Empfaengerstand> Bedient = [];
        public bool Laeuft;
        public bool Belegt;
        public bool Beendet;
        public ushort Nummer;
        public Sendeart Zielart;
        public bool Va;
        public int ZielAnzahl;
        public long Letztes;
        public uint Absender;

        /// <summary>Offen: begonnen, ohne ENDE und nicht länger als 2 s ohne Paket.</summary>
        public bool Offen(long jetzt) => Laeuft && !Beendet && jetzt - Letztes <= Fristen.PhaseOffenMs;

        /// <summary>Gehört das Paket zur laufenden Phase? Gleiche Nummer, Zielart, Ziele und VA, nicht abgelaufen.</summary>
        public bool Passt(in Sprachpaket p, long jetzt) => Laeuft && GleichePhase(p, jetzt);

        /// <summary>Wie <see cref="Passt"/>, auch für eine abgelegte Phase.</summary>
        public bool GleichePhase(in Sprachpaket p, long jetzt) =>
            Belegt && jetzt - Letztes <= Fristen.PhaseOffenMs && Nummer == p.Sprechphase && Zielart == p.Zielart
            && Va == p.IstVa && p.Ziele.SequenceEqual(Ziele.AsSpan(0, ZielAnzahl));

        public void Beginnen(in Sprachpaket p, uint absender)
        {
            Laeuft = true;
            Belegt = true;
            Beendet = false;
            Nummer = p.Sprechphase;
            Zielart = p.Zielart;
            Va = p.IstVa;
            ZielAnzahl = p.Ziele.Length;
            p.Ziele.CopyTo(Ziele);
            Absender = absender;
        }
    }

    /// <summary>Zustand je Sendersitzung: laufende und abgelegte Phase, Ratenlimit, gesammelte SendeFehler.</summary>
    private sealed class Sender
    {
        public Phase Phase = new();
        public Phase Vorige = new();
        public readonly SendeFehlerEintrag[] Offen = new SendeFehlerEintrag[Fristen.SendeFehlerEintraegeMax];
        private readonly (ushort Netz, long Umstellung)[] _entzugGemeldet = new (ushort, long)[16];
        private readonly TokenEimer _pakete;
        private readonly TokenEimer _bytes;
        private readonly long _basis;
        private int _entzugPos;

        public Sender(uint sitzung, long jetzt)
        {
            Sitzung = sitzung;
            _basis = jetzt;
            _pakete = new TokenEimer(Fristen.PaketeBurst, Fristen.PaketeJeSekunde, 0);
            _bytes = new TokenEimer(Fristen.OpusBytesBurst, Fristen.OpusBytesJeSekunde, 0);
        }

        public uint Sitzung { get; }
        public uint KontoId { get; set; }
        public int OffenAnzahl { get; set; }
        public uint VerworfenSeit { get; set; }
        public bool NurTeilweise { get; set; } = true;
        public bool Gemeldet { get; set; }
        public long LetzteMeldung { get; set; }
        public bool RateGemeldet { get; set; }
        public long LetzteRate { get; set; }

        public bool HatOffeneFehler => OffenAnzahl > 0 || VerworfenSeit > 0;

        /// <summary>
        /// Paket- und Byte-Topf (4.8); die ms werden relativ zum Anlegen in Stopwatch-Takte umgerechnet. Entnommen wird
        /// nur, wenn beide Töpfe reichen; ein abgelehntes Paket kostet keinen der beiden.
        /// </summary>
        public bool Rate(int opusBytes, long jetzt)
        {
            var ms = Math.Max(0, jetzt - _basis);
            var takte = ms / 1000 * Stopwatch.Frequency + ms % 1000 * Stopwatch.Frequency / 1000;
            _pakete.Nehmen(takte, 0);
            _bytes.Nehmen(takte, 0);
            if (_pakete.Stand < 1 || _bytes.Stand < opusBytes) return false;
            _pakete.Nehmen(takte);
            _bytes.Nehmen(takte, opusBytes);
            return true;
        }

        public void Aufnehmen(SendeFehlerEintrag e)
        {
            for (var i = 0; i < OffenAnzahl; i++)
            {
                if (Offen[i].Code == e.Code && Offen[i].NetzId == e.NetzId)
                {
                    Offen[i] = e;
                    return;
                }
            }
            if (OffenAnzahl < Offen.Length) Offen[OffenAnzahl++] = e;
        }

        public bool EntzugNeu(ushort netz, long umstellung)
        {
            foreach (var (n, u) in _entzugGemeldet)
                if (n == netz && u == umstellung) return false;
            _entzugGemeldet[_entzugPos] = (netz, umstellung);
            _entzugPos = (_entzugPos + 1) % _entzugGemeldet.Length;
            return true;
        }
    }
}
