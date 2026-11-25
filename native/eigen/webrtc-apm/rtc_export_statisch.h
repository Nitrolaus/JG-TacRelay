// JG TacRelay - Maintainer: Sarevoc
/*
 * rtc_export_statisch.h - wird per /FI (Force Include) vor jede C- und C++-Datei des
 * Baus gesetzt. Keine Aenderung an den Upstream-Quellen.
 *
 * Grund: Das Meson-Skript von webrtc-audio-processing 2.1 setzt WEBRTC_LIBRARY_IMPL und
 * WEBRTC_ENABLE_SYMBOL_EXPORT immer, auch bei default_library=static. Unter MSVC wird
 * RTC_EXPORT damit zu __declspec(dllexport), und jede statisch hineingelinkte Klasse
 * (AudioProcessing, AudioProcessingBuilder, EchoCanceller3Config, ...) landete in der
 * Exporttabelle von webrtc_apm.dll. Die DLL soll aber genau die acht apm_*-Funktionen
 * zeigen.
 *
 * rtc_base/system/rtc_export.h sagt selbst: "When WebRTC is built as a static library
 * the RTC_EXPORT macro expands to nothing." Diese Datei stellt genau das her, indem sie
 * den Include-Waechter von rtc_export.h vorwegnimmt und RTC_EXPORT leer definiert.
 * Ein /U im Meson-Aufruf hilft nicht: cl wertet /D und /U der Reihe nach aus, und die
 * Ziel-Argumente (-DWEBRTC_ENABLE_SYMBOL_EXPORT) stehen hinter den globalen.
 */
#ifndef RTC_BASE_SYSTEM_RTC_EXPORT_H_
#define RTC_BASE_SYSTEM_RTC_EXPORT_H_
#define RTC_EXPORT
#endif
