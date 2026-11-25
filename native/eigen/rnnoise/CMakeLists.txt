# RNNoise als 64-Bit-Windows-DLL (MSVC), Eigenbau fuer JG TacRelay.
#
# Upstream (rnnoise-main) hat nur autotools. Dieses Projekt bildet Makefile.am und
# configure.ac nach, ohne die entpackten Quellen anzufassen:
#   RNNOISE_SOURCES          -> Kernquellen unten
#   --enable-x86-rtcd        -> RNN_ENABLE_X86_RTCD, x86cpu.c, x86_dnn_map.c,
#                               nnet_sse4_1.c, nnet_avx2.c (Laufzeitauswahl C/SSE4.1/AVX2)
#   enable_dnn_debug_float=no-> DISABLE_DEBUG_FLOAT (int8-Gewichte, wie Upstream-Standard)
#   AC_DEFINE(RNNOISE_BUILD)
#
# Aufruf nur ueber native\skripte\rnnoise.ps1 (liefert RNNOISE_QUELLE und RNNOISE_MODELL).

cmake_minimum_required(VERSION 3.21)
project(tacrelay_rnnoise LANGUAGES C)

set(RNNOISE_QUELLE "" CACHE PATH "Ordner rnnoise-main (entpackte Upstream-Quellen, nur lesen)")
set(RNNOISE_MODELL "" CACHE PATH "Ordner mit rnnoise_data.c und rnnoise_data.h (im Bauordner entpackt)")
option(RNNOISE_X86_RTCD "x86-Laufzeiterkennung mit SSE4.1- und AVX2-Kernen" ON)
option(RNNOISE_RAUCHTEST "Rauchtest-Programm mitbauen" ON)

foreach(_datei "${RNNOISE_QUELLE}/src/denoise.c" "${RNNOISE_QUELLE}/include/rnnoise.h"
               "${RNNOISE_MODELL}/rnnoise_data.c" "${RNNOISE_MODELL}/rnnoise_data.h")
  if(NOT EXISTS "${_datei}")
    message(FATAL_ERROR "Fehlt: ${_datei} (RNNOISE_QUELLE / RNNOISE_MODELL pruefen)")
  endif()
endforeach()

if(NOT MSVC)
  message(FATAL_ERROR "Dieses Projekt ist fuer MSVC (cl.exe) geschrieben.")
endif()
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "Nur 64-Bit (x64) vorgesehen.")
endif()

# Statische CRT (/MT): keine VC-Redist noetig. Gilt fuer alle Ziele dieses Projekts.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded")

set(_q "${RNNOISE_QUELLE}/src")

set(RNNOISE_KERN
  "${_q}/denoise.c"
  "${_q}/rnn.c"
  "${_q}/pitch.c"
  "${_q}/kiss_fft.c"
  "${_q}/celt_lpc.c"
  "${_q}/nnet.c"
  "${_q}/nnet_default.c"
  "${_q}/parse_lpcnet_weights.c"
  "${_q}/rnnoise_tables.c"
  "${RNNOISE_MODELL}/rnnoise_data.c"
)
# Bewusst NICHT gebaut: dump_features.c, dump_rnnoise_tables.c, write_weights.c
# (Trainings- und Hilfsprogramme, in Makefile.am eigene noinst_PROGRAMS).

set(RNNOISE_X86
  "${_q}/x86/x86cpu.c"
  "${_q}/x86/x86_dnn_map.c"
  "${_q}/x86/nnet_sse4_1.c"
  "${_q}/x86/nnet_avx2.c"
)

set(RNNOISE_QUELLEN ${RNNOISE_KERN})
if(RNNOISE_X86_RTCD)
  list(APPEND RNNOISE_QUELLEN ${RNNOISE_X86})
endif()

add_library(rnnoise SHARED
  ${RNNOISE_QUELLEN}
  "${CMAKE_CURRENT_SOURCE_DIR}/rnnoise.def"
)

set_target_properties(rnnoise PROPERTIES
  OUTPUT_NAME rnnoise
  C_STANDARD 17
  C_STANDARD_REQUIRED ON
)

target_include_directories(rnnoise PRIVATE
  "${RNNOISE_QUELLE}/include"
  "${_q}"
  "${RNNOISE_MODELL}"
)

# Kein HAVE_CONFIG_H: alle config.h-Schalter stehen hier ausdruecklich.
# OPUS_X86_MAY_HAVE_SSE/SSE2: x64 hat SSE2 immer. MSVC setzt __SSE2__ nie selbst;
# src/x86/x86_arch_macros.h macht daraus __SSE__/__SSE2__, damit vec.h wie bei gcc
# auf x86-64 den SSE-Zweig (vec_avx.h) nimmt statt des langsamen Generikpfads.
target_compile_definitions(rnnoise PRIVATE
  RNNOISE_BUILD
  DISABLE_DEBUG_FLOAT
  OPUS_X86_MAY_HAVE_SSE
  OPUS_X86_MAY_HAVE_SSE2
  _CRT_SECURE_NO_WARNINGS
)

if(RNNOISE_X86_RTCD)
  target_compile_definitions(rnnoise PRIVATE RNN_ENABLE_X86_RTCD)
  # SSE4.1-Kern: gcc bekaeme -msse4.1 (setzt auch __SSSE3__). MSVC braucht fuer
  # SSE4.1-Intrinsics kein /arch, nur die Makros, auf die der Code prueft.
  set_source_files_properties("${_q}/x86/nnet_sse4_1.c" PROPERTIES
    COMPILE_DEFINITIONS "OPUS_X86_MAY_HAVE_SSE4_1;__SSSE3__")
  # AVX2-Kern: gcc bekaeme -mavx -mfma -mavx2. MSVC /arch:AVX2 setzt __AVX__ und
  # __AVX2__, aber nicht __FMA__ (gemessen). x86cpu.c waehlt diesen Kern nur, wenn
  # die CPU AVX, FMA und AVX2 meldet - __FMA__ ist hier also gedeckt.
  set_source_files_properties("${_q}/x86/nnet_avx2.c" PROPERTIES
    COMPILE_OPTIONS "/arch:AVX2"
    COMPILE_DEFINITIONS "__FMA__")
endif()

# /O2 kommt aus CMAKE_C_FLAGS_RELEASE. Kein /GL: die AVX2-Uebersetzungseinheit
# darf nicht per LTCG in den Generikpfad hineininlined werden.
# /Brepro: keine Zeitstempel im Binary -> gleiche Quellen ergeben gleichen SHA-256.
target_compile_options(rnnoise PRIVATE /O2 /W3 /utf-8 /Brepro /wd4244 /wd4305 /wd4018)
target_link_options(rnnoise PRIVATE /Brepro /OPT:REF /OPT:ICF /INCREMENTAL:NO)

if(RNNOISE_RAUCHTEST)
  # Laedt die DLL zur Laufzeit per LoadLibrary/GetProcAddress (wie LibraryImport),
  # linkt also nicht gegen rnnoise.lib. Header nur fuer sizeof(RNNoise).
  add_executable(rauchtest "${CMAKE_CURRENT_SOURCE_DIR}/rauchtest.c")
  set_target_properties(rauchtest PROPERTIES C_STANDARD 17 C_STANDARD_REQUIRED ON)
  target_include_directories(rauchtest PRIVATE "${_q}" "${RNNOISE_MODELL}")
  target_compile_definitions(rauchtest PRIVATE _CRT_SECURE_NO_WARNINGS)
  target_compile_options(rauchtest PRIVATE /O2 /W3 /utf-8)
  add_dependencies(rauchtest rnnoise)
endif()
