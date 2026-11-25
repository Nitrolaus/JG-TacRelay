// JG TacRelay - Maintainer: Sarevoc
/*
 * apm_c.h - duenne C-Schicht ueber das WebRTC Audio Processing Module
 * (webrtc-audio-processing 2.1) fuer JG TacRelay.
 *
 * Gebaut als webrtc_apm.dll (x64, statische CRT), gedacht fuer den Aufruf aus C#
 * per LibraryImport. Alle Funktionen sind extern "C", __cdecl (auf x64 ohnehin die
 * einzige Aufrufkonvention).
 *
 * Datenformat
 *   - float im Bereich [-1, 1], ein Rahmen = 10 ms.
 *   - Rahmenlaenge je Kanal: sample_rate_hz / 100 Werte (48 kHz -> 480).
 *   - Kanaele deinterleaved (planar) hintereinander: erst alle Werte von Kanal 0,
 *     dann alle von Kanal 1. Fuer Mono (der Normalfall) also genau 480 Werte bei 48 kHz.
 *   - Capture und Render benutzen dieselbe Abtastrate und Kanalzahl wie bei apm_create.
 *
 * Reihenfolge im Betrieb (je 10 ms)
 *   apm_process_render(h, lautsprecher_rahmen);   // Wiedergabe-Thread
 *   apm_process_capture(h, mikrofon_rahmen);       // Aufnahme-Thread, in-place
 *
 * Threads
 *   apm_process_capture und apm_process_render duerfen aus zwei verschiedenen Threads
 *   gleichzeitig laufen (das APM sperrt intern getrennt). apm_set_stream_delay_ms und
 *   apm_get_stats sind jederzeit erlaubt. apm_configure und apm_aec3_configure bauen das APM
 *   unter Umstaenden neu auf; sie warten dafuer, bis laufende Capture- und Render-Aufrufe
 *   fertig sind, und halten neue so lange an (wenige Millisekunden). Sie gehoeren deshalb
 *   nicht in einen Audio-Thread. apm_destroy darf erst aufgerufen werden, wenn kein anderer
 *   Aufruf mit demselben Handle mehr laeuft.
 *
 * Rueckgabewerte (int)
 *   0            Erfolg
 *   1            nur apm_get_stats: Werte (noch) nicht gueltig, z. B. AEC aus
 *   negativ      Fehler, Werte wie webrtc::AudioProcessing::Error
 *                (-1 unbestimmt, -5 Nullzeiger, -6 falscher Parameter, -12 AEC nicht an, ...);
 *                einzige Ausnahme: -13 ist nur eine Warnung (Wert wurde begrenzt)
 *   apm_aec3_describe liefert bei Erfolg die Laenge des Textes (>= 0).
 *
 * AEC3-Konfiguration (seit 1.1.0)
 *   Jede Instanz hat eine EchoCanceller3Config. Sie startet mit der Voreinstellung
 *   "tacrelay" (Upstream-Vorgabe plus die Aenderungen aus kVoreinstellungTacRelay in
 *   apm_c.cpp) und laesst sich mit apm_aec3_configure aendern. Wirksam wird sie, wenn der AEC
 *   (neu) aufgebaut wird: beim Einschalten ueber apm_configure oder sofort in
 *   apm_aec3_configure, wenn der AEC schon an ist. Das APM wird dabei neu aufgebaut; AEC3,
 *   Hochpass, NS und AGC2 beginnen von vorn, die Kennwerte haben wieder 2 s Anlauf.
 *   Ausgeschalteter AEC heisst wie bis 1.0.0: APM ohne Echo-Controller.
 *   Prozessweit fest (nicht Teil der Konfiguration): Feldversuch WebRTC-Aec3TransparentModeHmm,
 *   gesetzt beim ersten apm_create. AEC3 erkennt damit ein Headset (kein Echo) nach etwa 2 bis 3 s
 *   laufender Wiedergabe statt nach 6 s und unterdrueckt die eigene Stimme danach nicht mehr.
 *   Mit voreinstellung=webrtc und dem ersten stillen Aufnahmerahmen vorweg rechnet 1.1.0 ohne
 *   diesen Feldversuch bitgleich zu 1.0.0 (gemessen vor dem Festschreiben, Modulbericht aec3).
 *
 * C#-Hinweis zu apm_version: Rueckgabe als nint deklarieren und mit
 * Marshal.PtrToStringUTF8 lesen. Der Zeiger zeigt auf eine statische Zeichenkette und
 * darf nie freigegeben werden (also kein "string" als Rueckgabetyp im LibraryImport).
 */
#ifndef TACRELAY_APM_C_H
#define TACRELAY_APM_C_H

#if defined(_WIN32)
#  if defined(APM_C_BUILD)
#    define APM_API __declspec(dllexport)
#  else
#    define APM_API __declspec(dllimport)
#  endif
#  define APM_CALL __cdecl
#else
#  define APM_API __attribute__((visibility("default")))
#  define APM_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* Undurchsichtiges Handle. In C# als nint oder SafeHandle. */
typedef struct apm_handle apm_handle;

#define APM_OK                    0
#define APM_STATS_UNGUELTIG       1
#define APM_ERR_UNBESTIMMT       (-1)
#define APM_ERR_NULLZEIGER       (-5)
#define APM_ERR_PARAMETER        (-6)
#define APM_ERR_NICHT_AN         (-12)
#define APM_WARN_BEGRENZT        (-13)

/*
 * Legt eine APM-Instanz an. Alle Verarbeitungsstufen sind danach aus; eingeschaltet
 * wird mit apm_configure.
 *   sample_rate_hz  8000 bis 384000, durch 100 teilbar (sonst ist ein Rahmen nicht
 *                   genau 10 ms). Ueblich: 48000.
 *   channels        1 oder 2.
 * Rueckgabe: Handle oder NULL bei ungueltigen Werten oder Speichermangel.
 */
APM_API apm_handle* APM_CALL apm_create(int sample_rate_hz, int channels);

/* Gibt die Instanz frei. NULL ist erlaubt und tut nichts. */
APM_API void APM_CALL apm_destroy(apm_handle* handle);

/*
 * Schaltet die Stufen. Darf auch waehrend des Betriebs aufgerufen werden, kann dann
 * aber einzelne Stufen zuruecksetzen.
 *   echo_cancel              0 aus, sonst AEC3 an (Desktop-Modus, nicht AECM) mit der
 *                            AEC3-Konfiguration der Instanz (apm_aec3_configure)
 *   high_pass                0 aus, sonst Hochpass an; bei echo_cancel != 0 immer an
 *                            (AEC3 erzwingt ihn: enforce_high_pass_filtering)
 *   noise_suppression_level  -1 aus, 0 niedrig, 1 mittel, 2 hoch, 3 sehr hoch
 *   agc2_adaptive            0 aus, sonst AGC2 mit adaptiver digitaler Verstaerkung
 *   agc2_max_gain_db         groesste Verstaerkung der AGC2 in dB, (0, 90];
 *                            wird nur geprueft, wenn agc2_adaptive an ist.
 * Rueckgabe: 0 oder APM_ERR_PARAMETER / APM_ERR_NULLZEIGER.
 */
APM_API int APM_CALL apm_configure(apm_handle* handle,
                                   int echo_cancel,
                                   int high_pass,
                                   int noise_suppression_level,
                                   int agc2_adaptive,
                                   float agc2_max_gain_db);

/*
 * Verzoegerung zwischen Wiedergabe und Aufnahme in ms (Hinweis an den AEC; AEC3
 * schaetzt die Verzoegerung zusaetzlich selbst). Der Wert gilt, bis er neu gesetzt
 * wird, und wird vor jedem apm_process_capture an das APM weitergereicht.
 * Werte ausserhalb 0..500 werden auf diesen Bereich begrenzt (wie im APM selbst);
 * dann kommt APM_WARN_BEGRENZT (-13) zurueck, der begrenzte Wert gilt trotzdem.
 */
APM_API int APM_CALL apm_set_stream_delay_ms(apm_handle* handle, int delay_ms);

/* Verarbeitet einen 10-ms-Mikrofonrahmen in-place. */
APM_API int APM_CALL apm_process_capture(apm_handle* handle, float* samples_10ms);

/* Uebergibt einen 10-ms-Wiedergaberahmen als Referenz fuer den AEC (wird nicht veraendert). */
APM_API int APM_CALL apm_process_render(apm_handle* handle, const float* samples_10ms);

/*
 * Liest die AEC-Kennwerte. Jeder Ausgabezeiger darf NULL sein.
 *   erl       Echo Return Loss in dB
 *   erle      Echo Return Loss Enhancement in dB
 *   delay_ms  vom AEC geschaetzte Verzoegerung in ms
 * Die Werte stammen vom letzten verarbeiteten Capture-Rahmen: apm_process_capture holt sie
 * bei eingeschaltetem AEC nach jedem Rahmen beim APM ab. (Das APM haelt nur einen
 * Statistik-Eintrag vor und verwirft neuere, solange niemand abholt; ein seltener direkter
 * Abruf lieferte sonst den Stand vom ersten Rahmen nach dem vorigen Abruf.)
 * apm_get_stats selbst beruehrt das APM nicht und sperrt nichts.
 * Fehlende Werte werden als NaN bzw. -1 geschrieben; vorhandene werden immer geschrieben,
 * auch wenn die Rueckgabe 1 ist (dann sind es Start- oder Altwerte). Bei ausgeschaltetem
 * AEC kommt immer 1 mit NaN/-1.
 * Rueckgabe 0 (gueltig) nur, wenn
 *   - der AEC an ist und das APM alle drei Werte liefert,
 *   - seit dem Einschalten des AEC mindestens 2 s (200 Capture-Rahmen) verarbeitet sind:
 *     AEC3 haelt ERL/ERLE in dieser Anlaufphase auf Startwerten (aec3/aec_state.cc),
 *   - die ERL-Schaetzung ihren Startwert (-30 dB, Deckel kMaxErl = 1000) verlassen hat.
 * Sonst 1; negativ bei Fehler.
 * Grenze: Erkennt AEC3 selbst einen Echopfadwechsel (z. B. Verzoegerungssprung), setzt es
 * ERLE intern zurueck und schaetzt 2 s lang nicht neu. Das sieht diese Schicht nicht; der
 * Wert kann dann kurz zu niedrig sein, obwohl 0 zurueckkommt.
 */
APM_API int APM_CALL apm_get_stats(apm_handle* handle, float* erl, float* erle, int* delay_ms);

/*
 * Aendert die AEC3-Konfiguration der Instanz (EchoCanceller3Config).
 *   spec  Text "name=wert;name=wert;..." (Leerzeichen um Namen und Werte erlaubt). Die Namen
 *         sind die Pfade in webrtc::EchoCanceller3Config, z. B.
 *         "suppressor.normal_tuning.max_inc_factor=4;ep_strength.default_gain=0.5".
 *         Werte: Zahl (Punkt als Dezimaltrenner); ganzzahlige Felder nur ganze Zahlen,
 *         Schalter nur 0 oder 1. Ausgangspunkt ist die aktuelle Konfiguration der Instanz;
 *         ein Eintrag "voreinstellung=webrtc" (Upstream-Vorgabe) oder
 *         "voreinstellung=tacrelay" setzt sie an dieser Stelle zurueck.
 *         Welche Namen es gibt, zeigt apm_aec3_describe.
 *   fehler_position  darf NULL sein; bei -6 die Position (Zeichen) des fehlerhaften Eintrags,
 *         oder die Laenge von spec, wenn erst die fertige Konfiguration ungueltig ist.
 * Alles oder nichts: Unbekannter Name, unlesbarer Wert oder eine Konfiguration, die
 * EchoCanceller3Config::Validate anpassen muesste (bzw. eine Maske mit enr_transparent >=
 * enr_suppress), ergibt -6 und aendert nichts. Ist der AEC an, wird er sofort mit der neuen
 * Konfiguration neu aufgebaut (siehe oben).
 * Rueckgabe: 0, APM_ERR_PARAMETER, APM_ERR_NULLZEIGER oder -1.
 */
APM_API int APM_CALL apm_aec3_configure(apm_handle* handle, const char* spec,
                                        int* fehler_position);

/*
 * Schreibt die AEC3-Konfiguration als "name=wert;..." (alle bekannten Felder, Gleitkommawerte
 * in kuerzester Form, die denselben float ergibt) nach buffer, mit Nullzeichen.
 *   wirksam  0: die Konfiguration der Instanz (fuer den naechsten Aufbau);
 *            1: die, mit der der laufende EchoCanceller3 erzeugt wurde. Ohne laufenden AEC3
 *               (AEC aus) kommt APM_ERR_NICHT_AN.
 *   buffer, buffer_len  Ziel; buffer = NULL mit buffer_len = 0 fragt nur die Laenge ab.
 * Rueckgabe: Laenge des Textes ohne Nullzeichen (>= 0). Geschrieben wird nur, wenn buffer_len
 * groesser ist; sonst bleibt buffer leer. Negativ bei Fehler.
 */
APM_API int APM_CALL apm_aec3_describe(apm_handle* handle, int wirksam, char* buffer,
                                       int buffer_len);

/* Statische Versionszeichenkette (UTF-8, nie freigeben). */
APM_API const char* APM_CALL apm_version(void);

#ifdef __cplusplus
}
#endif

#endif /* TACRELAY_APM_C_H */
