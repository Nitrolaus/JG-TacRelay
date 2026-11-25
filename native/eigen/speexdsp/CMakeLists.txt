# speexdsp_resampler.dll - nur der Resampler aus speexdsp 1.2.1 (JG TacRelay)
#
# Baut resample.c als 64-Bit-DLL mit statischer CRT (/MT), Float-Variante,
# SSE/SSE2 unter MSVC. Die Quellen bleiben unveraendert; config.h und die
# Exportliste liegen hier daneben.
#
# Aufruf ueber native\skripte\speexdsp.ps1 (vcvars64 + Ninja, Release).

cmake_minimum_required(VERSION 3.21)

project(speexdsp_resampler VERSION 1.2.1 LANGUAGES C)

if(NOT MSVC)
  message(FATAL_ERROR "Dieses CMakeLists.txt ist nur fuer MSVC gedacht.")
endif()
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "Erwartet wird ein 64-Bit-Build (vcvars64).")
endif()

set(SPEEXDSP_QUELLE "" CACHE PATH "Wurzel der entpackten speexdsp-1.2.1-Quellen")
if(NOT SPEEXDSP_QUELLE)
  get_filename_component(SPEEXDSP_QUELLE "${CMAKE_CURRENT_LIST_DIR}/../../quellen/speexdsp-1.2.1" ABSOLUTE)
endif()
if(NOT EXISTS "${SPEEXDSP_QUELLE}/libspeexdsp/resample.c")
  message(FATAL_ERROR "resample.c nicht gefunden unter ${SPEEXDSP_QUELLE}")
endif()

option(TR_SPEEX_SSE "SSE/SSE2-Innenschleifen des Resamplers nutzen" ON)
option(TR_RAUCHTEST "Rauchtest-Programm mitbauen" ON)

# Statische CRT: keine VC-Redist beim Nutzer noetig.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

# /O2 kommt ueber CMAKE_C_FLAGS_RELEASE (/O2 /Ob2 /DNDEBUG).
# /Brepro: kein Zeitstempel im PE-Kopf, gleiche Quellen ergeben gleiche Bytes.
add_compile_options(/W3 /Brepro)
add_link_options(/Brepro)

add_library(speexdsp_resampler SHARED
  "${SPEEXDSP_QUELLE}/libspeexdsp/resample.c"
  "${CMAKE_CURRENT_LIST_DIR}/speexdsp_resampler.def"
)

# config.h aus diesem Ordner muss vor allem anderen gefunden werden.
target_include_directories(speexdsp_resampler PRIVATE
  "${CMAKE_CURRENT_LIST_DIR}"
  "${SPEEXDSP_QUELLE}/include"
  "${SPEEXDSP_QUELLE}/libspeexdsp"
)

target_compile_definitions(speexdsp_resampler PRIVATE
  HAVE_CONFIG_H
  _CRT_SECURE_NO_WARNINGS
  $<$<NOT:$<BOOL:${TR_SPEEX_SSE}>>:TR_SPEEX_OHNE_SSE>
)

set_target_properties(speexdsp_resampler PROPERTIES
  OUTPUT_NAME "speexdsp_resampler"
  PREFIX ""
)

if(TR_RAUCHTEST)
  # Laedt die DLL zur Laufzeit (LoadLibraryW/GetProcAddress), linkt also nicht
  # gegen die Importbibliothek - so, wie LibraryImport es spaeter tut.
  add_executable(rauchtest "${CMAKE_CURRENT_LIST_DIR}/rauchtest.c")
  target_compile_definitions(rauchtest PRIVATE _CRT_SECURE_NO_WARNINGS UNICODE _UNICODE)
  add_dependencies(rauchtest speexdsp_resampler)
endif()
