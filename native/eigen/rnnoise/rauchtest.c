// JG TacRelay - Maintainer: Sarevoc
/* Rauchtest fuer rnnoise.dll (JG-TacRelay-Eigenbau).
 *
 * Laedt die DLL zur Laufzeit per LoadLibrary/GetProcAddress - so wie spaeter
 * LibraryImport aus C# - und prueft damit die Exporte nach Namen.
 *
 *  1. Pflichtexporte aufloesen, rnnoise_get_frame_size() == 480.
 *  2. 1000 Rahmen weisses Rauschen und 1000 Rahmen Sinus (440 Hz) verarbeiten:
 *     Sprachwahrscheinlichkeit (Rueckgabe), Rechenzeit je Rahmen (Mittel ueber
 *     1000 Rahmen), Pegel Ein/Aus, Pruefung auf NaN/Inf.
 *  3. Whitebox-Pfadvergleich: Der Rechenkern (0 = C/SSE2, 1 = SSE4.1, 2 = AVX2)
 *     steht in DenoiseState direkt hinter dem Feld RNNoise model. Der Test liest
 *     ihn dort, prueft ihn gegen die eigene CPUID-Auswertung und laesst - nur wenn
 *     beides uebereinstimmt - jeden Kern bis zum erkannten einmal laufen.
 *     So werden auch die Kerne geprueft, die auf dieser CPU sonst nie laufen.
 *
 * Aufruf: rauchtest.exe <pfad\zu\rnnoise.dll>
 * Exitcode 0 = bestanden.
 */
#include <windows.h>
#include <intrin.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "rnnoise_data.h" /* nur fuer sizeof(RNNoise) im Whitebox-Teil */

typedef struct DenoiseState DenoiseState;
typedef int (*fn_get_size)(void);
typedef int (*fn_get_frame_size)(void);
typedef int (*fn_init)(DenoiseState *st, void *model);
typedef DenoiseState *(*fn_create)(void *model);
typedef void (*fn_destroy)(DenoiseState *st);
typedef float (*fn_process_frame)(DenoiseState *st, float *out, const float *in);

#define RAHMEN 480
#define ANZAHL 1000
#define ABTASTRATE 48000.0
#define EINSCHWINGEN 100 /* Rahmen, die fuer die Pegelmessung uebersprungen werden */
#define PI 3.14159265358979323846

static fn_get_size p_get_size;
static fn_get_frame_size p_get_frame_size;
static fn_init p_init;
static fn_create p_create;
static fn_destroy p_destroy;
static fn_process_frame p_process_frame;

static int g_fehler = 0;

/* xorshift32, fester Startwert: jeder Lauf bekommt dasselbe Rauschen. */
static unsigned int g_zufall;
static float zufall_pm1(void)
{
    g_zufall ^= g_zufall << 13;
    g_zufall ^= g_zufall >> 17;
    g_zufall ^= g_zufall << 5;
    return (float)((g_zufall >> 8) * (1.0 / 16777216.0)) * 2.0f - 1.0f;
}

enum { SIGNAL_RAUSCHEN = 0, SIGNAL_SINUS = 1 };
static const char *signal_name[] = { "Rauschen (weiss, gleichverteilt, Spitze 3000)",
                                     "Sinus 440 Hz (Spitze 10000)" };

/* RNNoise erwartet Gleitkomma im 16-Bit-Wertebereich (+-32768), nicht +-1. */
static void signal_erzeugen(int art, long rahmen_nr, float *x)
{
    int i;
    for (i = 0; i < RAHMEN; i++) {
        if (art == SIGNAL_RAUSCHEN) {
            x[i] = 3000.0f * zufall_pm1();
        } else {
            double t = (double)(rahmen_nr * RAHMEN + i) / ABTASTRATE;
            x[i] = (float)(10000.0 * sin(2.0 * PI * 440.0 * t));
        }
    }
}

typedef struct {
    double vad_summe, vad_min, vad_max, vad_letzt;
    double zeit_us_summe;
    double energie_ein, energie_aus;
    int nicht_endlich;
    int vad_ausserhalb;
} Ergebnis;

/* Verarbeitet ANZAHL Rahmen. vad_spur/aus_spur (optional) nehmen alle Werte auf. */
static void lauf(DenoiseState *st, int art, Ergebnis *e, float *vad_spur, float *aus_spur)
{
    float ein[RAHMEN], aus[RAHMEN];
    LARGE_INTEGER frequenz, t0, t1;
    long k;
    int i;

    memset(e, 0, sizeof(*e));
    e->vad_min = 1e9;
    e->vad_max = -1e9;
    g_zufall = 0x2545F491u;
    QueryPerformanceFrequency(&frequenz);

    for (k = 0; k < ANZAHL; k++) {
        float vad;
        signal_erzeugen(art, k, ein);
        QueryPerformanceCounter(&t0);
        vad = p_process_frame(st, aus, ein);
        QueryPerformanceCounter(&t1);
        e->zeit_us_summe += (double)(t1.QuadPart - t0.QuadPart) * 1e6 / (double)frequenz.QuadPart;

        if (!isfinite(vad)) e->nicht_endlich++;
        if (vad < 0.0f || vad > 1.0f) e->vad_ausserhalb++;
        e->vad_summe += vad;
        if (vad < e->vad_min) e->vad_min = vad;
        if (vad > e->vad_max) e->vad_max = vad;
        e->vad_letzt = vad;
        if (vad_spur) vad_spur[k] = vad;

        for (i = 0; i < RAHMEN; i++) {
            if (!isfinite(aus[i])) e->nicht_endlich++;
            if (k >= EINSCHWINGEN) {
                e->energie_ein += (double)ein[i] * ein[i];
                e->energie_aus += (double)aus[i] * aus[i];
            }
        }
        if (aus_spur) memcpy(&aus_spur[k * RAHMEN], aus, sizeof(aus));
    }
}

static double pegel_db(double energie)
{
    double n = (double)(ANZAHL - EINSCHWINGEN) * RAHMEN;
    double rms = sqrt(energie / n);
    return 20.0 * log10(rms / 32768.0 + 1e-12);
}

static void ergebnis_ausgeben(const char *titel, const Ergebnis *e)
{
    double mittel_us = e->zeit_us_summe / ANZAHL;
    double rahmen_us = RAHMEN / ABTASTRATE * 1e6; /* 10000 us */
    printf("  %s\n", titel);
    printf("    Sprachwahrscheinlichkeit: Mittel %.4f  Min %.4f  Max %.4f  letzter Rahmen %.4f\n",
           e->vad_summe / ANZAHL, e->vad_min, e->vad_max, e->vad_letzt);
    printf("    Rechenzeit je Rahmen:     %.2f us (Mittel ueber %d Rahmen) = %.2f %% Echtzeit-Last\n",
           mittel_us, ANZAHL, 100.0 * mittel_us / rahmen_us);
    printf("    Pegel ab Rahmen %d:      ein %.2f dBFS  aus %.2f dBFS  Differenz %.2f dB\n",
           EINSCHWINGEN, pegel_db(e->energie_ein), pegel_db(e->energie_aus),
           pegel_db(e->energie_aus) - pegel_db(e->energie_ein));
    if (e->nicht_endlich) {
        printf("    FEHLER: %d Werte NaN/Inf\n", e->nicht_endlich);
        g_fehler = 1;
    }
    if (e->vad_ausserhalb) {
        printf("    FEHLER: %d Rueckgaben ausserhalb [0,1]\n", e->vad_ausserhalb);
        g_fehler = 1;
    }
}

/* Dieselbe Auswahl wie src/x86/x86cpu.c (rnn_select_arch_impl). */
static int cpu_kern_erwartet(void)
{
    int info[4];
    int sse41, avx2;
    __cpuid(info, 0);
    if (info[0] < 1) return 0;
    __cpuid(info, 1);
    sse41 = (info[2] & (1 << 19)) != 0;
    avx2 = (info[2] & (1 << 28)) != 0 && (info[2] & (1 << 12)) != 0; /* AVX + FMA */
    if (avx2) {
        int n;
        __cpuid(info, 0);
        n = info[0];
        if (n >= 7) {
            __cpuidex(info, 7, 0);
            avx2 = (info[1] & (1 << 5)) != 0;
        } else {
            avx2 = 0;
        }
    }
    if (!sse41) return 0;
    if (!avx2) return 1;
    return 2;
}

static void cpu_name(char *name, size_t groesse)
{
    int info[4];
    char puffer[49];
    memset(puffer, 0, sizeof(puffer));
    __cpuid(info, 0x80000000);
    if ((unsigned)info[0] >= 0x80000004u) {
        __cpuid((int *)(puffer + 0), 0x80000002);
        __cpuid((int *)(puffer + 16), 0x80000003);
        __cpuid((int *)(puffer + 32), 0x80000004);
        {
            char *p = puffer;
            while (*p == ' ') p++;
            strncpy(name, p, groesse - 1);
            name[groesse - 1] = 0;
        }
    } else {
        strncpy(name, "unbekannt", groesse - 1);
        name[groesse - 1] = 0;
    }
}

static const char *kern_name[] = { "C (SSE2-Basis)", "SSE4.1", "AVX2+FMA" };

static FARPROC pflicht(HMODULE h, const char *name)
{
    FARPROC p = GetProcAddress(h, name);
    printf("  Export %-28s %s\n", name, p ? "gefunden" : "FEHLT");
    if (!p) g_fehler = 1;
    return p;
}

static void pfadvergleich(void)
{
    const size_t versatz = sizeof(RNNoise); /* struct DenoiseState { RNNoise model; int arch; ... } */
    int groesse = p_get_size();
    int erwartet = cpu_kern_erwartet();
    DenoiseState *st;
    int *kern;
    int k, art;
    float *vad_ref[2], *aus_ref[2], *vad_x, *aus_x;

    printf("\n[3] Whitebox-Pfadvergleich der Rechenkerne\n");
    printf("  rnnoise_get_size() = %d Bytes, sizeof(RNNoise) = %u Bytes\n", groesse, (unsigned)versatz);
    if ((size_t)groesse < versatz + sizeof(int)) {
        printf("  uebersprungen: DenoiseState kleiner als erwartet\n");
        return;
    }
    st = (DenoiseState *)malloc((size_t)groesse);
    if (!st || p_init(st, NULL) != 0) {
        printf("  FEHLER: rnnoise_init(st, NULL) fehlgeschlagen\n");
        g_fehler = 1;
        free(st);
        return;
    }
    kern = (int *)((char *)st + versatz);
    printf("  Kern laut DLL: %d (%s), laut eigener CPUID-Auswertung: %d (%s)\n",
           *kern, (*kern >= 0 && *kern <= 2) ? kern_name[*kern] : "?", erwartet, kern_name[erwartet]);
    if (*kern != erwartet) {
        printf("  uebersprungen: Feldlage oder Kernwahl weicht ab, kein Eingriff in den Zustand\n");
        free(st);
        return;
    }

    for (art = 0; art < 2; art++) {
        vad_ref[art] = (float *)malloc(sizeof(float) * ANZAHL);
        aus_ref[art] = (float *)malloc(sizeof(float) * ANZAHL * RAHMEN);
    }
    vad_x = (float *)malloc(sizeof(float) * ANZAHL);
    aus_x = (float *)malloc(sizeof(float) * ANZAHL * RAHMEN);

    /* Referenz: der Kern, den die DLL selbst gewaehlt hat. */
    for (art = 0; art < 2; art++) {
        Ergebnis e;
        p_init(st, NULL);
        lauf(st, art, &e, vad_ref[art], aus_ref[art]);
    }

    for (k = erwartet; k >= 0; k--) {
        printf("  Kern %d %s:\n", k, kern_name[k]);
        for (art = 0; art < 2; art++) {
            Ergebnis e;
            double max_vad = 0, e_ref = 0, e_diff = 0;
            int i;
            p_init(st, NULL);
            *kern = k;
            lauf(st, art, &e, vad_x, aus_x);
            for (i = 0; i < ANZAHL; i++) {
                double d = fabs((double)vad_x[i] - vad_ref[art][i]);
                if (d > max_vad) max_vad = d;
            }
            for (i = 0; i < ANZAHL * RAHMEN; i++) {
                double r = aus_ref[art][i], d = (double)aus_x[i] - r;
                e_ref += r * r;
                e_diff += d * d;
            }
            printf("    %-8s VAD-Mittel %.4f  max. VAD-Abweichung zur Referenz %.5f  "
                   "Ausgang-SNR zur Referenz %s%.1f dB  %.2f us/Rahmen%s\n",
                   art == SIGNAL_RAUSCHEN ? "Rauschen" : "Sinus", e.vad_summe / ANZAHL, max_vad,
                   e_diff == 0 ? ">" : "", e_diff == 0 ? 200.0 : 10.0 * log10(e_ref / e_diff),
                   e.zeit_us_summe / ANZAHL, e.nicht_endlich ? "  FEHLER NaN/Inf" : "");
            if (e.nicht_endlich || e.vad_ausserhalb) g_fehler = 1;
            /* Grobe Plausibilitaet: alle Kerne muessen dasselbe Netz rechnen. */
            if (max_vad > 0.05) {
                printf("    FEHLER: Kern %d weicht in der VAD um mehr als 0.05 ab\n", k);
                g_fehler = 1;
            }
        }
    }

    for (art = 0; art < 2; art++) {
        free(vad_ref[art]);
        free(aus_ref[art]);
    }
    free(vad_x);
    free(aus_x);
    free(st);
}

int main(int argc, char **argv)
{
    const char *pfad = argc > 1 ? argv[1] : "rnnoise.dll";
    HMODULE h;
    DenoiseState *st;
    int fs, art;
    char name[64];

    cpu_name(name, sizeof(name));
    printf("Rauchtest rnnoise.dll\n");
    printf("  DLL: %s\n", pfad);
    printf("  CPU: %s\n", name);

    printf("\n[1] Laden und Exporte\n");
    h = LoadLibraryExA(pfad, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!h) {
        printf("  FEHLER: LoadLibrary scheitert, GetLastError=%lu\n", GetLastError());
        return 2;
    }
    p_create = (fn_create)pflicht(h, "rnnoise_create");
    p_destroy = (fn_destroy)pflicht(h, "rnnoise_destroy");
    p_process_frame = (fn_process_frame)pflicht(h, "rnnoise_process_frame");
    p_get_frame_size = (fn_get_frame_size)pflicht(h, "rnnoise_get_frame_size");
    p_get_size = (fn_get_size)pflicht(h, "rnnoise_get_size");
    p_init = (fn_init)pflicht(h, "rnnoise_init");
    printf("  rnnoise_model_from_file bewusst nicht exportiert: %s\n",
           GetProcAddress(h, "rnnoise_model_from_file") ? "NEIN, doch vorhanden" : "ja");
    if (g_fehler) return 3;

    fs = p_get_frame_size();
    printf("  rnnoise_get_frame_size() = %d %s\n", fs, fs == RAHMEN ? "(ok)" : "(FEHLER, erwartet 480)");
    if (fs != RAHMEN) return 4;

    printf("\n[2] Verarbeitung, je %d Rahmen zu %d Samples (48 kHz, 10 ms)\n", ANZAHL, RAHMEN);
    for (art = 0; art < 2; art++) {
        Ergebnis e;
        st = p_create(NULL);
        if (!st) {
            printf("  FEHLER: rnnoise_create(NULL) liefert NULL\n");
            return 5;
        }
        lauf(st, art, &e, NULL, NULL);
        ergebnis_ausgeben(signal_name[art], &e);
        p_destroy(st);
    }

    pfadvergleich();

    FreeLibrary(h);
    printf("\nErgebnis: %s\n", g_fehler ? "FEHLGESCHLAGEN" : "BESTANDEN");
    return g_fehler ? 1 : 0;
}
