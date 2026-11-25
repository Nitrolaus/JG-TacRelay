// JG TacRelay - Maintainer: Sarevoc
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Funk.Server.Betrieb;

namespace Funk.Server.Netzzugang;

/// <summary>
/// Vorrangspur (Begriffe, 4.8): Präfixe, von denen in den letzten 14 Tagen eine AKTIV-Sitzung kam. Sie gehen an den
/// globalen Töpfen vorbei und bekommen einen größeren Handshake-Topf je Präfix. Gespeichert werden nur HMAC-SHA256-Werte
/// (Schlüssel aus dem Betriebsgeheimnis), höchstens 2000, damit sie einen Neustart überstehen, ohne Adressen im
/// Klartext abzulegen. Threadsicher.
/// </summary>
public sealed class Vorrangspur
{
    public const int MaxEintraege = 2000;
    public const long HaltedauerMs = 14L * 24 * 60 * 60 * 1000;

    private readonly Lock _sperre = new();
    private readonly byte[] _schluessel;
    private readonly Dictionary<UInt128, long> _eintraege = [];

    public Vorrangspur(ReadOnlySpan<byte> schluessel)
    {
        if (schluessel.Length < 16) throw new ArgumentException("HMAC-Schlüssel zu kurz.", nameof(schluessel));
        _schluessel = schluessel.ToArray();
    }

    /// <summary>Wurde seit dem letzten Speichern geändert.</summary>
    public bool Geaendert { get; private set; }

    public int Anzahl
    {
        get
        {
            lock (_sperre) return _eintraege.Count;
        }
    }

    /// <summary>Ob das Präfix in der Vorrangspur steht und der Eintrag jünger als 14 Tage ist.</summary>
    public bool Enthaelt(Praefix praefix, long unixMs)
    {
        var h = Wert(praefix);
        lock (_sperre) return _eintraege.TryGetValue(h, out var zuletzt) && unixMs - zuletzt < HaltedauerMs;
    }

    /// <summary>Trägt ein Präfix ein oder frischt es auf (AKTIV-Sitzung). Bei 2000 Einträgen fällt der älteste heraus.</summary>
    public void Eintragen(Praefix praefix, long unixMs)
    {
        var h = Wert(praefix);
        lock (_sperre)
        {
            if (!_eintraege.ContainsKey(h) && _eintraege.Count >= MaxEintraege)
            {
                Aufraeumen(unixMs);
                if (_eintraege.Count >= MaxEintraege) _eintraege.Remove(_eintraege.MinBy(e => e.Value).Key);
            }
            _eintraege[h] = unixMs;
            Geaendert = true;
        }
    }

    /// <summary>Admin-Werkzeug: Vorrangspur leeren.</summary>
    public void Leeren()
    {
        lock (_sperre)
        {
            _eintraege.Clear();
            Geaendert = true;
        }
    }

    /// <summary>Entfernt Einträge älter als 14 Tage.</summary>
    public void Aufraeumen(long unixMs)
    {
        lock (_sperre)
        {
            foreach (var (h, zuletzt) in _eintraege.ToArray())
            {
                if (unixMs - zuletzt < HaltedauerMs) continue;
                _eintraege.Remove(h);
                Geaendert = true;
            }
        }
    }

    public byte[] AlsJson()
    {
        lock (_sperre)
        {
            Geaendert = false;
            var liste = _eintraege.Select(e =>
            {
                Span<byte> b = stackalloc byte[16];
                BinaryPrimitives.WriteUInt128BigEndian(b, e.Key);
                return new Eintrag(Convert.ToBase64String(b), e.Value);
            }).ToList();
            return JsonSerializer.SerializeToUtf8Bytes(new Datei(liste), Ablageformat.Optionen);
        }
    }

    public void AusJsonLaden(ReadOnlySpan<byte> json, long unixMs)
    {
        var d = JsonSerializer.Deserialize<Datei>(json, Ablageformat.Optionen) ?? throw new InvalidDataException("Vorrangspur leer.");
        lock (_sperre)
        {
            _eintraege.Clear();
            foreach (var e in d.Eintraege.Take(MaxEintraege))
            {
                var roh = Convert.FromBase64String(e.H);
                if (roh.Length != 16 || unixMs - e.Zuletzt >= HaltedauerMs) continue;
                _eintraege[BinaryPrimitives.ReadUInt128BigEndian(roh)] = e.Zuletzt;
            }
            Geaendert = false;
        }
    }

    private UInt128 Wert(Praefix p)
    {
        Span<byte> eingabe = stackalloc byte[16];
        p.Schreiben(eingabe);
        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(_schluessel, eingabe, mac);
        return BinaryPrimitives.ReadUInt128BigEndian(mac);
    }

    private sealed record Eintrag(string H, long Zuletzt);

    private sealed record Datei(List<Eintrag> Eintraege);
}
