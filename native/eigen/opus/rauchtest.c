// JG TacRelay - Maintainer: Sarevoc
/*
 * Rauchtest fuer opus.dll (JG TacRelay, libopus 1.6.1, x64)
 *
 * Laedt die DLL ueber ihren absoluten Pfad und holt jede Funktion per
 * GetProcAddress ueber ihren Namen - so, wie es spaeter LibraryImport in C#
 * tut. Gegen opus.lib wird bewusst nicht gelinkt.
 *
 * Ablauf:
 *   1. Exporte aufloesen, Versionsstring pruefen
 *   2. 440-Hz-Sinus, 48 kHz mono, 50 Frames zu 20 ms mit opus_encode_float
 *      kodieren (VOIP, 20 kbit/s, hoechstens Wideband -> SILK 16 kHz, dort
 *      arbeiten Deep PLC und OSCE)
 *   3. Denselben Paketstrom mit fuenf Decodern dekodieren, Frame 30 geht
 *      verloren (PLC):
 *        K0      Komplexitaet 0  klassisch
 *        K5      Komplexitaet 5  Deep PLC
 *        K6      Komplexitaet 6  Deep PLC + LACE
 *        K7      Komplexitaet 7  Deep PLC + NoLACE
 *        K7+BWE  Komplexitaet 7  + OPUS_SET_OSCE_BWE(1)
 *      Wirkt eine Stufe, unterscheidet sich ihre Ausgabe von K0.
 *   4. opus_pcm_soft_clip auf einen uebersteuerten Sinus
 *
 * Aufruf:   rauchtest.exe <Pfad zu opus.dll> [--ohne-bwe]
 * Exitcode: 0 = alle Pruefungen bestanden, 1 = mindestens eine FAIL, 2 = Aufruf falsch
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <intrin.h>
#include <immintrin.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

#include "opus.h"

#define FS          48000
#define FRAME       960                 /* 20 ms bei 48 kHz */
#define FRAMES      50                  /* 1 s Audio */
#define LOST_FRAME  30                  /* dieser Frame fehlt beim Dekodieren */
#define MESS_START  5                   /* Messfenster vor dem Verlust: Frames 5..29 */
#define MESS_ENDE   LOST_FRAME
#define MAX_PACKET  1500
#define TON_HZ      440.0
#define PI_D        3.14159265358979323846

typedef const char  *(*fn_get_version_string)(void);
typedef const char  *(*fn_strerror)(int);
typedef OpusEncoder *(*fn_encoder_create)(opus_int32, int, int, int *);
typedef opus_int32   (*fn_encode_float)(OpusEncoder *, const float *, int, unsigned char *, opus_int32);
typedef int          (*fn_encoder_ctl)(OpusEncoder *, int, ...);
typedef void         (*fn_encoder_destroy)(OpusEncoder *);
typedef OpusDecoder *(*fn_decoder_create)(opus_int32, int, int *);
typedef int          (*fn_decode_float)(OpusDecoder *, const unsigned char *, opus_int32, float *, int, int);
typedef int          (*fn_decoder_ctl)(OpusDecoder *, int, ...);
typedef void         (*fn_decoder_destroy)(OpusDecoder *);
typedef void         (*fn_pcm_soft_clip)(float *, int, int, float *);

static fn_get_version_string p_get_version_string;
static fn_strerror           p_strerror;
static fn_encoder_create     p_encoder_create;
static fn_encode_float       p_encode_float;
static fn_encoder_ctl        p_encoder_ctl;
static fn_encoder_destroy    p_encoder_destroy;
static fn_decoder_create     p_decoder_create;
static fn_decode_float       p_decode_float;
static fn_decoder_ctl        p_decoder_ctl;
static fn_decoder_destroy    p_decoder_destroy;
static fn_pcm_soft_clip      p_pcm_soft_clip;

static int g_fehler = 0;

static void pruefe(int ok, const char *name, const char *detail)
{
    printf("%s  %s%s%s\n", ok ? "PASS" : "FAIL", name,
           (detail && *detail) ? " - " : "", detail ? detail : "");
    if (!ok) g_fehler++;
}

static const char *fehlertext(int code)
{
    return p_strerror ? p_strerror(code) : "?";
}

static FARPROC hole(HMODULE h, const char *name)
{
    FARPROC f = GetProcAddress(h, name);
    char titel[96];
    snprintf(titel, sizeof titel, "Export %s", name);
    pruefe(f != NULL, titel, f ? "" : "GetProcAddress liefert NULL");
    return f;
}

static int cpu_hat_avx2(void)
{
    int r[4];
    __cpuid(r, 0);
    if (r[0] < 7) return 0;
    __cpuid(r, 1);
    if (!((r[2] >> 12) & 1) || !((r[2] >> 27) & 1) || !((r[2] >> 28) & 1)) return 0; /* FMA, OSXSAVE, AVX */
    if ((_xgetbv(0) & 6) != 6) return 0;                                              /* XMM+YMM vom OS gesichert */
    __cpuidex(r, 7, 0);
    return (r[1] >> 5) & 1;                                                           /* AVX2 */
}

/* Anteil der Energie bei TON_HZ (Goertzel, Fenster enthaelt ganze Perioden) */
static double ton_anteil(const float *x, int n)
{
    double coeff = 2.0 * cos(2.0 * PI_D * TON_HZ / FS);
    double s1 = 0.0, s2 = 0.0, energie = 0.0, leistung;
    int i;
    for (i = 0; i < n; i++) {
        double s0 = x[i] + coeff * s1 - s2;
        s2 = s1;
        s1 = s0;
        energie += (double)x[i] * x[i];
    }
    leistung = s1 * s1 + s2 * s2 - coeff * s1 * s2;
    return energie > 0.0 ? 2.0 * leistung / ((double)n * energie) : 0.0;
}

static double energie(const float *x, int n)
{
    double e = 0.0;
    int i;
    for (i = 0; i < n; i++) e += (double)x[i] * x[i];
    return e;
}

static double differenz_energie(const float *a, const float *b, int n)
{
    double e = 0.0;
    int i;
    for (i = 0; i < n; i++) {
        double d = (double)a[i] - b[i];
        e += d * d;
    }
    return e;
}

/* Abstand b gegen a in dB relativ zur Energie von a; 0 heisst bitgleich */
static void abstand_text(char *buf, size_t len, const float *a, const float *b, int n, double *roh)
{
    double d = differenz_energie(a, b, n);
    double e = energie(a, n);
    *roh = d;
    if (d == 0.0)
        snprintf(buf, len, "bitgleich");
    else
        snprintf(buf, len, "Abstand %.1f dB", 10.0 * log10(d / (e > 0.0 ? e : 1e-30)));
}

typedef struct {
    const char *name;
    int komplexitaet;
    int bwe;
} Konfig;

static float         g_eingang[FRAMES * FRAME];
static unsigned char g_pakete[FRAMES][MAX_PACKET];
static opus_int32    g_laengen[FRAMES];
static float         g_aus[5][FRAMES * FRAME];

int wmain(int argc, wchar_t **argv)
{
    static const Konfig konfig[5] = {
        { "K0",     0, 0 },
        { "K5",     5, 0 },
        { "K6",     6, 0 },
        { "K7",     7, 0 },
        { "K7+BWE", 7, 1 },
    };
    int ohne_bwe = 0, i, f, r, err;
    HMODULE h;
    wchar_t geladen[MAX_PATH];
    char detail[256];
    LARGE_INTEGER takt, t0, t1;
    double ms[5];

    if (argc < 2) {
        fwprintf(stderr, L"Aufruf: rauchtest.exe <Pfad zu opus.dll> [--ohne-bwe]\n");
        return 2;
    }
    for (i = 2; i < argc; i++)
        if (wcscmp(argv[i], L"--ohne-bwe") == 0) ohne_bwe = 1;

    /* 1. DLL laden, Exporte aufloesen ---------------------------------- */
    h = LoadLibraryExW(argv[1], NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (h == NULL) {
        printf("FAIL  DLL laden - GetLastError %lu\n", GetLastError());
        return 1;
    }
    GetModuleFileNameW(h, geladen, MAX_PATH);
    printf("INFO  geladen: %ls\n", geladen);
    printf("INFO  CPU mit AVX2: %s\n", cpu_hat_avx2() ? "ja" : "nein");

    p_get_version_string = (fn_get_version_string)hole(h, "opus_get_version_string");
    p_strerror           = (fn_strerror)          hole(h, "opus_strerror");
    p_encoder_create     = (fn_encoder_create)    hole(h, "opus_encoder_create");
    p_encode_float       = (fn_encode_float)      hole(h, "opus_encode_float");
    p_encoder_ctl        = (fn_encoder_ctl)       hole(h, "opus_encoder_ctl");
    p_encoder_destroy    = (fn_encoder_destroy)   hole(h, "opus_encoder_destroy");
    p_decoder_create     = (fn_decoder_create)    hole(h, "opus_decoder_create");
    p_decode_float       = (fn_decode_float)      hole(h, "opus_decode_float");
    p_decoder_ctl        = (fn_decoder_ctl)       hole(h, "opus_decoder_ctl");
    p_decoder_destroy    = (fn_decoder_destroy)   hole(h, "opus_decoder_destroy");
    p_pcm_soft_clip      = (fn_pcm_soft_clip)     hole(h, "opus_pcm_soft_clip");
    if (g_fehler) {
        printf("ERGEBNIS  FAIL (%d) - Exporte fehlen, Abbruch\n", g_fehler);
        return 1;
    }

    {
        const char *ver = p_get_version_string();
        printf("INFO  Version: %s\n", ver ? ver : "(NULL)");
        pruefe(ver != NULL && strstr(ver, "1.6.1") != NULL, "opus_get_version_string enthaelt 1.6.1", ver);
    }

    /* 2. Kodieren ------------------------------------------------------ */
    for (i = 0; i < FRAMES * FRAME; i++)
        g_eingang[i] = (float)(0.5 * sin(2.0 * PI_D * TON_HZ * i / FS));

    {
        OpusEncoder *enc = p_encoder_create(FS, 1, OPUS_APPLICATION_VOIP, &err);
        opus_int32 lookahead = -1, bytes = 0;
        int leer = 0, silk = 0;

        pruefe(enc != NULL && err == OPUS_OK, "opus_encoder_create 48 kHz mono VOIP", fehlertext(err));
        if (enc == NULL) goto ende;

        r = p_encoder_ctl(enc, OPUS_SET_BITRATE(20000));
        pruefe(r == OPUS_OK, "opus_encoder_ctl OPUS_SET_BITRATE(20000)", fehlertext(r));
        r = p_encoder_ctl(enc, OPUS_SET_COMPLEXITY(10));
        pruefe(r == OPUS_OK, "opus_encoder_ctl OPUS_SET_COMPLEXITY(10)", fehlertext(r));
        r = p_encoder_ctl(enc, OPUS_SET_SIGNAL(OPUS_SIGNAL_VOICE));
        pruefe(r == OPUS_OK, "opus_encoder_ctl OPUS_SET_SIGNAL(VOICE)", fehlertext(r));
        r = p_encoder_ctl(enc, OPUS_SET_MAX_BANDWIDTH(OPUS_BANDWIDTH_WIDEBAND));
        pruefe(r == OPUS_OK, "opus_encoder_ctl OPUS_SET_MAX_BANDWIDTH(WB)", fehlertext(r));
        r = p_encoder_ctl(enc, OPUS_GET_LOOKAHEAD(&lookahead));
        snprintf(detail, sizeof detail, "%ld Samples", (long)lookahead);
        pruefe(r == OPUS_OK && lookahead > 0, "opus_encoder_ctl OPUS_GET_LOOKAHEAD", detail);

        for (f = 0; f < FRAMES; f++) {
            g_laengen[f] = p_encode_float(enc, g_eingang + f * FRAME, FRAME, g_pakete[f], MAX_PACKET);
            if (g_laengen[f] <= 0) {
                leer++;
            } else {
                bytes += g_laengen[f];
                if ((g_pakete[f][0] >> 3) < 12) silk++;   /* TOC-Konfiguration 0..11 = SILK-only */
            }
        }
        snprintf(detail, sizeof detail, "erster Frame %ld Bytes (TOC 0x%02X), %d Frames, %ld Bytes gesamt, %d SILK-only",
                 (long)g_laengen[0], g_laengen[0] > 0 ? g_pakete[0][0] : 0, FRAMES, (long)bytes, silk);
        pruefe(leer == 0, "opus_encode_float Sinus 440 Hz, 48 kHz mono, 20 ms", detail);
        p_encoder_destroy(enc);
        if (leer) goto ende;
    }

    /* 3. Dekodieren mit fuenf Konfigurationen --------------------------- */
    QueryPerformanceFrequency(&takt);
    for (i = 0; i < 5; i++) {
        const Konfig *k = &konfig[i];
        OpusDecoder *dec = p_decoder_create(FS, 1, &err);
        opus_int32 wert = -1;
        int falsch = 0;
        char titel[96];

        snprintf(titel, sizeof titel, "[%s] opus_decoder_create 48 kHz mono", k->name);
        pruefe(dec != NULL && err == OPUS_OK, titel, fehlertext(err));
        if (dec == NULL) goto ende;

        r = p_decoder_ctl(dec, OPUS_SET_COMPLEXITY(k->komplexitaet));
        p_decoder_ctl(dec, OPUS_GET_COMPLEXITY(&wert));
        snprintf(titel, sizeof titel, "[%s] opus_decoder_ctl OPUS_SET_COMPLEXITY(%d)", k->name, k->komplexitaet);
        snprintf(detail, sizeof detail, "%s, zurueckgelesen %ld", fehlertext(r), (long)wert);
        pruefe(r == OPUS_OK && wert == k->komplexitaet, titel, detail);

        if (k->bwe) {
            wert = -1;
            r = p_decoder_ctl(dec, OPUS_SET_OSCE_BWE(1));
            snprintf(titel, sizeof titel, "[%s] opus_decoder_ctl OPUS_SET_OSCE_BWE(1)", k->name);
            if (ohne_bwe) {
                pruefe(r == OPUS_UNIMPLEMENTED, titel, "ohne ENABLE_OSCE_BWE gebaut, UNIMPLEMENTED erwartet");
            } else {
                int r2 = p_decoder_ctl(dec, OPUS_GET_OSCE_BWE(&wert));
                snprintf(detail, sizeof detail, "%s, zurueckgelesen %ld", fehlertext(r), (long)wert);
                pruefe(r == OPUS_OK && r2 == OPUS_OK && wert == 1, titel, detail);
            }
        }

        QueryPerformanceCounter(&t0);
        for (f = 0; f < FRAMES; f++) {
            const unsigned char *daten = (f == LOST_FRAME) ? NULL : g_pakete[f];
            opus_int32 laenge = (f == LOST_FRAME) ? 0 : g_laengen[f];
            int n = p_decode_float(dec, daten, laenge, g_aus[i] + f * FRAME, FRAME, 0);
            if (n != FRAME) falsch++;
        }
        QueryPerformanceCounter(&t1);
        ms[i] = 1000.0 * (double)(t1.QuadPart - t0.QuadPart) / (double)takt.QuadPart;

        snprintf(titel, sizeof titel, "[%s] opus_decode_float 50 Frames, Frame %d verloren", k->name, LOST_FRAME);
        snprintf(detail, sizeof detail, "%d Frames mit falscher Laenge, %.2f ms fuer 1 s Audio", falsch, ms[i]);
        pruefe(falsch == 0, titel, detail);
        p_decoder_destroy(dec);
    }

    /* Auswertung -------------------------------------------------------- */
    {
        const int start = MESS_START * FRAME, n = (MESS_ENDE - MESS_START) * FRAME;
        const int v0 = LOST_FRAME * FRAME;
        double anteil = ton_anteil(g_aus[0] + start, n);
        double pegel = 10.0 * log10(energie(g_aus[0] + start, n) / energie(g_eingang + start, n));
        double roh;
        char a[64];

        snprintf(detail, sizeof detail, "440-Hz-Anteil %.4f, Pegel %+.2f dB gegen Eingang", anteil, pegel);
        pruefe(anteil > 0.90 && fabs(pegel) < 3.0, "[K0] dekodierter Sinus plausibel (Frames 5..29)", detail);

        for (i = 1; i < 5; i++) {
            snprintf(detail, sizeof detail, "440-Hz-Anteil %.4f", ton_anteil(g_aus[i] + start, n));
            printf("INFO  [%s] %s\n", konfig[i].name, detail);
        }

        /* K5: vor dem Verlust wie K0, im Verlustframe Deep PLC statt klassischer PLC */
        abstand_text(a, sizeof a, g_aus[0] + start, g_aus[1] + start, n, &roh);
        printf("INFO  [K5] gegen K0 vor dem Verlust: %s\n", a);
        abstand_text(a, sizeof a, g_aus[0] + v0, g_aus[1] + v0, FRAME, &roh);
        pruefe(roh > 0.0, "[K5] Deep PLC wirkt: Verlustframe weicht von klassischer PLC ab", a);

        abstand_text(a, sizeof a, g_aus[0] + start, g_aus[2] + start, n, &roh);
        pruefe(roh > 0.0, "[K6] LACE wirkt: Ausgabe vor dem Verlust weicht von K0 ab", a);

        abstand_text(a, sizeof a, g_aus[0] + start, g_aus[3] + start, n, &roh);
        pruefe(roh > 0.0, "[K7] NoLACE wirkt: Ausgabe vor dem Verlust weicht von K0 ab", a);

        abstand_text(a, sizeof a, g_aus[2] + start, g_aus[3] + start, n, &roh);
        printf("INFO  [K7] gegen K6 (NoLACE statt LACE): %s\n", a);

        abstand_text(a, sizeof a, g_aus[3] + start, g_aus[4] + start, n, &roh);
        if (ohne_bwe)
            printf("INFO  [K7+BWE] gegen K7: %s (ohne BWE gebaut)\n", a);
        else
            pruefe(roh > 0.0, "[K7+BWE] BWE wirkt: Ausgabe weicht von K7 ab", a);

        printf("INFO  Dekodierzeit fuer 1 s Audio: K0 %.2f ms, K5 %.2f ms, K6 %.2f ms, K7 %.2f ms, K7+BWE %.2f ms\n",
               ms[0], ms[1], ms[2], ms[3], ms[4]);
    }

    /* 4. Soft-Clipping ---------------------------------------------------- */
    {
        float x[FRAME], mem[1] = { 0.0f }, vorher = 0.0f, nachher = 0.0f;
        for (i = 0; i < FRAME; i++) {
            x[i] = (float)(1.8 * sin(2.0 * PI_D * TON_HZ * i / FS));
            if (fabsf(x[i]) > vorher) vorher = fabsf(x[i]);
        }
        p_pcm_soft_clip(x, FRAME, 1, mem);
        for (i = 0; i < FRAME; i++)
            if (fabsf(x[i]) > nachher) nachher = fabsf(x[i]);
        snprintf(detail, sizeof detail, "Spitze vorher %.3f, nachher %.3f", vorher, nachher);
        pruefe(nachher <= 1.0f && nachher > 0.5f, "opus_pcm_soft_clip begrenzt auf [-1, 1]", detail);
    }

ende:
    if (g_fehler)
        printf("ERGEBNIS  FAIL (%d Pruefungen fehlgeschlagen)\n", g_fehler);
    else
        printf("ERGEBNIS  PASS\n");
    return g_fehler ? 1 : 0;
}
