// JG TacRelay - Maintainer: Sarevoc
/*
 * Rauchtest fuer deepfilternet.dll. Laedt die DLL per LoadLibraryExW ueber den absoluten Pfad und holt jede Funktion per
 * GetProcAddress, wie spaeter LibraryImport. Prueft Exporte, Version, Rahmen, Verzoegerung, Fehlerpfade, Stille,
 * Rauschdaempfung, Erhalt eines sprachaehnlichen Signals, Daempfungsgrenze, Gleichheit zweier Instanzen, Laufzeit je Rahmen
 * und Speicher. Alle Signale entstehen hier (Pseudozufall mit fester Saat, Obertoene mit Formanten).
 *
 * Aufruf: rauchtest.exe <pfad\deepfilternet.dll>. Exitcode 0 = bestanden.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <psapi.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "deepfilternet.h"

typedef const char *(*f_version)(void);
typedef DfnZustand *(*f_create)(float, float, int32_t *);
typedef int32_t (*f_laenge)(const DfnZustand *);
typedef int32_t (*f_process)(DfnZustand *, const float *, float *, int32_t, float *);
typedef int32_t (*f_setzen)(DfnZustand *, float);
typedef int32_t (*f_schwellen)(DfnZustand *, float, float, float);
typedef void (*f_destroy)(DfnZustand *);

#define N 480
#define RATE 48000
#define PI 3.14159265358979323846

static f_version p_version;
static f_create p_create;
static f_laenge p_frame_length;
static f_laenge p_delay;
static f_process p_process;
static f_setzen p_atten;
static f_setzen p_beta;
static f_schwellen p_schwellen;
static f_destroy p_destroy;

static int fehler = 0;

static void pruefe(int ok, const char *was)
{
    printf("%s %s\n", ok ? "PASS" : "FAIL", was);
    if (!ok) fehler++;
}

static uint32_t saat = 12345u;

static float gleich(void)
{
    saat ^= saat << 13;
    saat ^= saat >> 17;
    saat ^= saat << 5;
    return (float)((double)(saat & 0xFFFFFFu) / 8388608.0 - 1.0);
}

/* Etwa normalverteilt, Effektivwert 1. */
static float rauschen(void)
{
    return (gleich() + gleich() + gleich() + gleich()) * 0.8660254f;
}

static double pegel_db(const float *x, size_t n)
{
    double s = 0.0;
    for (size_t i = 0; i < n; i++) s += (double)x[i] * x[i];
    return 10.0 * log10(s / (double)n + 1e-30);
}

/* Sprachaehnlich: Grundton 105 bis 135 Hz, Obertoene bis 4 kHz mit drei Formanten, Silben 250 ms an, 120 ms aus, mit
 * 20-ms-Rampen. Effektivwert in den Silben auf `ziel_db` dBFS gebracht. */
static void stimme(float *x, size_t n, double ziel_db)
{
    const double formant[3] = {700.0, 1220.0, 2600.0};
    const double breite[3] = {90.0, 110.0, 160.0};
    double phase = 0.0;
    for (size_t i = 0; i < n; i++) {
        double t = (double)i / RATE;
        double f0 = 120.0 + 15.0 * sin(2.0 * PI * 0.7 * t);
        phase += f0 / RATE;
        if (phase >= 1.0) phase -= 1.0;
        double s = 0.0;
        for (int k = 1; k * f0 < 4000.0; k++) {
            double f = k * f0, a = 0.02;
            for (int m = 0; m < 3; m++) a += 1.0 / (1.0 + ((f - formant[m]) / breite[m]) * ((f - formant[m]) / breite[m]));
            s += a / k * sin(2.0 * PI * k * phase);
        }
        double in_silbe = fmod(t, 0.37);
        double h = 0.0;
        if (in_silbe < 0.25) {
            h = 1.0;
            if (in_silbe < 0.02) h = in_silbe / 0.02;
            else if (in_silbe > 0.23) h = (0.25 - in_silbe) / 0.02;
        }
        x[i] = (float)(s * h);
    }
    /* Pegel nur ueber die Silben (Rahmen mit Huelle 1) */
    double sum = 0.0;
    size_t cnt = 0;
    for (size_t i = 0; i < n; i++) {
        double in_silbe = fmod((double)i / RATE, 0.37);
        if (in_silbe >= 0.03 && in_silbe < 0.22) { sum += (double)x[i] * x[i]; cnt++; }
    }
    double ist = 10.0 * log10(sum / (double)cnt);
    float g = (float)pow(10.0, (ziel_db - ist) / 20.0);
    for (size_t i = 0; i < n; i++) x[i] *= g;
}

static int vergleich(const void *a, const void *b)
{
    double x = *(const double *)a, y = *(const double *)b;
    return (x > y) - (x < y);
}

static SIZE_T privat(void)
{
    PROCESS_MEMORY_COUNTERS_EX m;
    memset(&m, 0, sizeof m);
    m.cb = sizeof m;
    GetProcessMemoryInfo(GetCurrentProcess(), (PROCESS_MEMORY_COUNTERS *)&m, sizeof m);
    return m.PrivateUsage;
}

/* Verarbeitet n Werte rahmenweise; misst die Zeit je Rahmen in Mikrosekunden (zeiten darf NULL sein). */
static int filtern(DfnZustand *z, const float *ein, float *aus, size_t n, double *zeiten)
{
    LARGE_INTEGER f, a, b;
    QueryPerformanceFrequency(&f);
    for (size_t r = 0; r + N <= n; r += N) {
        QueryPerformanceCounter(&a);
        int32_t c = p_process(z, ein + r, aus + r, N, NULL);
        QueryPerformanceCounter(&b);
        if (c != DFN_OK) return c;
        if (zeiten) zeiten[r / N] = (double)(b.QuadPart - a.QuadPart) * 1e6 / (double)f.QuadPart;
    }
    return DFN_OK;
}

int main(int argc, char **argv)
{
    if (argc < 2) {
        fprintf(stderr, "Aufruf: rauchtest.exe <pfad\\deepfilternet.dll>\n");
        return 2;
    }
    wchar_t pfad[MAX_PATH];
    MultiByteToWideChar(CP_UTF8, 0, argv[1], -1, pfad, MAX_PATH);
    SIZE_T vor_laden = privat();
    HMODULE dll = LoadLibraryExW(pfad, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!dll) {
        printf("FAIL DLL nicht ladbar (%lu)\n", GetLastError());
        return 1;
    }
    printf("[1] Exporte\n");
    p_version = (f_version)GetProcAddress(dll, "dfn_version");
    p_create = (f_create)GetProcAddress(dll, "dfn_create");
    p_frame_length = (f_laenge)GetProcAddress(dll, "dfn_frame_length");
    p_delay = (f_laenge)GetProcAddress(dll, "dfn_delay_samples");
    p_process = (f_process)GetProcAddress(dll, "dfn_process");
    p_atten = (f_setzen)GetProcAddress(dll, "dfn_set_atten_lim");
    p_beta = (f_setzen)GetProcAddress(dll, "dfn_set_post_filter_beta");
    p_schwellen = (f_schwellen)GetProcAddress(dll, "dfn_set_thresholds");
    p_destroy = (f_destroy)GetProcAddress(dll, "dfn_destroy");
    pruefe(p_version && p_create && p_frame_length && p_delay && p_process && p_atten && p_beta && p_schwellen && p_destroy,
           "alle 9 Exporte gefunden");
    if (fehler) return 1;
    const char *version = p_version();
    printf("     Version: %s\n", version);
    pruefe(strncmp(version, "JG TacRelay deepfilternet ", 26) == 0, "Version beginnt mit 'JG TacRelay deepfilternet '");
    printf("     Speicher privat nach dem Laden: %+.1f MB\n", ((double)privat() - (double)vor_laden) / 1048576.0);

    printf("[2] Fehlerpfade\n");
    int32_t code = 99;
    pruefe(p_create(NAN, 0.0f, &code) == NULL && code == DFN_FEHLER_WERT, "create mit NaN -> NULL, DFN_FEHLER_WERT");
    code = 99;
    pruefe(p_create(100.0f, 2.0f, &code) == NULL && code == DFN_FEHLER_WERT, "create mit beta 2 -> NULL, DFN_FEHLER_WERT");
    pruefe(p_frame_length(NULL) == DFN_FEHLER_ZEIGER, "frame_length(NULL) -> DFN_FEHLER_ZEIGER");
    pruefe(p_delay(NULL) == DFN_FEHLER_ZEIGER, "delay_samples(NULL) -> DFN_FEHLER_ZEIGER");
    p_destroy(NULL);
    pruefe(1, "destroy(NULL) ohne Absturz");

    printf("[3] Anlegen\n");
    SIZE_T vor = privat();
    LARGE_INTEGER f, a, b;
    QueryPerformanceFrequency(&f);
    QueryPerformanceCounter(&a);
    code = 99;
    DfnZustand *z = p_create(100.0f, 0.0f, &code);
    QueryPerformanceCounter(&b);
    pruefe(z != NULL && code == DFN_OK, "create(100 dB, ohne Nachfilter)");
    if (!z) return 1;
    printf("     Anlegen: %.0f ms, Speicher privat %+.1f MB\n", (double)(b.QuadPart - a.QuadPart) * 1e3 / (double)f.QuadPart,
           ((double)privat() - (double)vor) / 1048576.0);
    pruefe(p_frame_length(z) == N, "frame_length = 480");
    int32_t verzug = p_delay(z);
    printf("     delay_samples = %d\n", verzug);
    pruefe(verzug == 1440, "delay_samples = 1440 (30 ms)");

    float ein[N], aus[N];
    for (int i = 0; i < N; i++) ein[i] = 0.1f * rauschen();
    pruefe(p_process(z, ein, aus, N - 1, NULL) == DFN_FEHLER_LAENGE, "process mit 479 Werten -> DFN_FEHLER_LAENGE");
    pruefe(p_process(NULL, ein, aus, N, NULL) == DFN_FEHLER_ZEIGER, "process(NULL) -> DFN_FEHLER_ZEIGER");
    pruefe(p_process(z, NULL, aus, N, NULL) == DFN_FEHLER_ZEIGER, "process ohne Eingang -> DFN_FEHLER_ZEIGER");
    pruefe(p_atten(z, INFINITY) == DFN_FEHLER_WERT, "set_atten_lim(unendlich) -> DFN_FEHLER_WERT");
    pruefe(p_beta(z, -0.1f) == DFN_FEHLER_WERT, "set_post_filter_beta(-0,1) -> DFN_FEHLER_WERT");
    pruefe(p_schwellen(z, 20.0f, 30.0f, 10.0f) == DFN_FEHLER_WERT, "set_thresholds(20, 30, 10) -> DFN_FEHLER_WERT");
    pruefe(p_schwellen(z, -15.0f, 35.0f, 35.0f) == DFN_OK, "set_thresholds(-15, 35, 35) wie das LADSPA-Plugin");
    pruefe(p_schwellen(z, -10.0f, 30.0f, 20.0f) == DFN_OK, "set_thresholds(-10, 30, 20) zurueck auf libDF");

    printf("[4] Stille\n");
    memset(ein, 0, sizeof ein);
    float lsnr = 0.0f;
    int still = 1;
    for (int r = 0; r < 20; r++) {
        if (p_process(z, ein, aus, N, &lsnr) != DFN_OK) still = 0;
        for (int i = 0; i < N; i++) if (aus[i] != 0.0f) still = 0;
    }
    printf("     lsnr bei Stille: %.1f dB\n", lsnr);
    pruefe(still, "20 Rahmen Stille -> DFN_OK und nur Nullen");
    p_destroy(z);

    /* Signale: 30 s Stimme -20 dBFS plus Rauschen -45 dBFS, dazu Rauschen allein. */
    const size_t lang = (size_t)RATE * 30;
    float *sauber = (float *)malloc(lang * sizeof(float));
    float *gemisch = (float *)malloc(lang * sizeof(float));
    float *raus = (float *)malloc(lang * sizeof(float));
    float *raus2 = (float *)malloc(lang * sizeof(float));
    double *zeiten = (double *)malloc((lang / N) * sizeof(double));
    if (!sauber || !gemisch || !raus || !raus2 || !zeiten) return 1;
    stimme(sauber, lang, -20.0);
    const float rausch_lin = (float)pow(10.0, -45.0 / 20.0);
    for (size_t i = 0; i < lang; i++) gemisch[i] = sauber[i] + rausch_lin * rauschen();

    printf("[5] Rauschen allein (-40 dBFS weiss, 3 s)\n");
    z = p_create(100.0f, 0.0f, &code);
    const size_t drei = (size_t)RATE * 3;
    float *rausch = (float *)malloc(drei * sizeof(float));
    if (!z || !rausch) return 1;
    for (size_t i = 0; i < drei; i++) rausch[i] = 0.01f * rauschen();
    pruefe(filtern(z, rausch, raus, drei, NULL) == DFN_OK, "3 s Rauschen gefiltert");
    double rein = pegel_db(rausch + RATE, 2 * RATE), raus_db = pegel_db(raus + RATE, 2 * RATE);
    printf("     Pegel rein %.1f dBFS, raus %.1f dBFS (letzte 2 s), Daempfung %.1f dB\n", rein, raus_db, rein - raus_db);
    pruefe(rein - raus_db > 20.0, "weisses Rauschen um mehr als 20 dB gedaempft");
    p_destroy(z);

    printf("[6] Daempfungsgrenze 12 dB\n");
    z = p_create(12.0f, 0.0f, &code);
    if (!z) return 1;
    filtern(z, rausch, raus, drei, NULL);
    raus_db = pegel_db(raus + RATE, 2 * RATE);
    printf("     raus %.1f dBFS, Daempfung %.1f dB\n", raus_db, rein - raus_db);
    pruefe(fabs((rein - raus_db) - 12.0) < 2.0, "Daempfung 12 +-2 dB");
    pruefe(p_atten(z, 100.0f) == DFN_OK, "set_atten_lim(100) im laufenden Zustand");
    p_destroy(z);

    printf("[7] Stimme -20 dBFS mit Rauschen -45 dBFS, 30 s\n");
    vor = privat();
    z = p_create(100.0f, 0.0f, &code);
    if (!z) return 1;
    SIZE_T nach_anlegen = privat();
    pruefe(filtern(z, gemisch, raus, lang, zeiten) == DFN_OK, "3000 Rahmen gefiltert");
    SIZE_T nach_rechnen = privat();
    int endlich = 1;
    for (size_t i = 0; i < lang; i++) if (!isfinite(raus[i])) endlich = 0;
    pruefe(endlich, "Ausgang ohne NaN und Unendlich");

    /* Verzoegerung ueber die Kreuzkorrelation mit dem sauberen Signal (Sekunde 5 bis 25). */
    int beste = 0;
    double best = -1e300;
    for (int lag = 0; lag <= 4800; lag++) {
        double s = 0.0, e = 0.0;
        for (size_t i = (size_t)RATE * 5; i < (size_t)RATE * 25; i += 2) {
            s += (double)sauber[i] * raus[i + lag];
            e += (double)raus[i + lag] * raus[i + lag];
        }
        double k = s / sqrt(e + 1e-30);
        if (k > best) { best = k; beste = lag; }
    }
    printf("     Kreuzkorrelation: Maximum bei %d Werten (%.2f ms)\n", beste, beste * 1000.0 / RATE);
    pruefe(beste >= 1440 - 24 && beste <= 1440 + 24, "gemessene Verzoegerung 1440 +-24");

    /* Erhalt der Stimme und Restrauschen, zeitrichtig. */
    double s_ein = 0.0, s_aus = 0.0, r_ein = 0.0, r_aus = 0.0;
    size_t ns = 0, nr = 0;
    for (size_t i = (size_t)RATE * 2; i + 1440 < lang; i++) {
        double in_silbe = fmod((double)i / RATE, 0.37);
        if (in_silbe >= 0.03 && in_silbe < 0.22) {
            s_ein += (double)gemisch[i] * gemisch[i];
            s_aus += (double)raus[i + 1440] * raus[i + 1440];
            ns++;
        } else if (in_silbe >= 0.28 && in_silbe < 0.35) {
            r_ein += (double)gemisch[i] * gemisch[i];
            r_aus += (double)raus[i + 1440] * raus[i + 1440];
            nr++;
        }
    }
    double stimme_ein = 10.0 * log10(s_ein / ns), stimme_aus = 10.0 * log10(s_aus / ns);
    double pause_ein = 10.0 * log10(r_ein / nr + 1e-30), pause_aus = 10.0 * log10(r_aus / nr + 1e-30);
    printf("     Silben: rein %.1f, raus %.1f dBFS (%+.1f dB); Pausen: rein %.1f, raus %.1f dBFS\n", stimme_ein, stimme_aus,
           stimme_aus - stimme_ein, pause_ein, pause_aus);
    pruefe(stimme_aus - stimme_ein > -6.0, "Silben hoechstens 6 dB leiser");
    pruefe(pause_ein - pause_aus > 15.0, "Rauschen in den Pausen um mehr als 15 dB gedaempft");

    /* Laufzeit */
    size_t nz = lang / N;
    double erster = zeiten[0], summe = 0.0;
    for (size_t i = 0; i < nz; i++) summe += zeiten[i];
    qsort(zeiten, nz, sizeof(double), vergleich);
    printf("     Laufzeit je 10-ms-Rahmen: erster %.0f us, Mittel %.0f, p50 %.0f, p95 %.0f, p99 %.0f, max %.0f us\n", erster,
           summe / nz, zeiten[nz / 2], zeiten[nz * 95 / 100], zeiten[nz * 99 / 100], zeiten[nz - 1]);
    pruefe(zeiten[nz * 99 / 100] < 10000.0, "p99 unter 10 ms");
    printf("     Speicher privat: Anlegen %+.1f MB, nach 3000 Rahmen %+.1f MB\n", ((double)nach_anlegen - (double)vor) / 1048576.0,
           ((double)nach_rechnen - (double)vor) / 1048576.0);

    printf("[8] Gleichheit zweier frischer Zustaende\n");
    DfnZustand *z2 = p_create(100.0f, 0.0f, &code);
    if (!z2) return 1;
    filtern(z2, gemisch, raus2, lang, NULL);
    pruefe(memcmp(raus, raus2, lang * sizeof(float)) == 0, "zweiter Zustand rechnet bitgleich");
    p_destroy(z2);
    p_destroy(z);

    printf("[9] NaN im Eingang (nur Bericht, die C#-Huelle ersetzt NaN vorher)\n");
    z = p_create(100.0f, 0.0f, &code);
    if (!z) return 1;
    for (int i = 0; i < N; i++) ein[i] = gemisch[RATE + i];
    ein[100] = NAN;
    int32_t c1 = p_process(z, ein, aus, N, NULL);
    int nan_aus = 0;
    for (int i = 0; i < N; i++) if (!isfinite(aus[i])) nan_aus++;
    int32_t c2 = DFN_OK;
    int nan_danach = 0;
    for (int r = 0; r < 20 && c2 == DFN_OK; r++) {
        c2 = p_process(z, gemisch + 2 * RATE + r * N, aus, N, NULL);
        for (int i = 0; i < N; i++) if (!isfinite(aus[i])) nan_danach++;
    }
    printf("     Rueckgabe %d, nicht endliche Werte im Rahmen %d; danach Rueckgabe %d, nicht endliche Werte in 20 Rahmen %d\n",
           c1, nan_aus, c2, nan_danach);
    p_destroy(z);

    printf("[10] Anlegen und Freigeben im Kreis (5 mal)\n");
    SIZE_T kreis_vor = privat();
    for (int i = 0; i < 5; i++) {
        DfnZustand *k = p_create(100.0f, 0.0f, &code);
        if (!k) return 1;
        filtern(k, gemisch, raus2, (size_t)RATE, NULL);
        p_destroy(k);
    }
    double wachstum = ((double)privat() - (double)kreis_vor) / 1048576.0;
    printf("     Speicher privat danach %+.1f MB\n", wachstum);
    pruefe(wachstum < 20.0, "kein grosses Leck (unter 20 MB nach 5 Runden)");

    free(rausch);
    free(sauber);
    free(gemisch);
    free(raus);
    free(raus2);
    free(zeiten);
    printf("ERGEBNIS: %s (%d Fehler)\n", fehler ? "NICHT BESTANDEN" : "BESTANDEN", fehler);
    return fehler ? 1 : 0;
}
