// JG TacRelay - Maintainer: Sarevoc
/* Eigene Build-Konfiguration fuer den speexdsp-1.2.1-Resampler (JG TacRelay).
   Ersetzt das von autoconf erzeugte config.h; resample.c bindet es ueber
   HAVE_CONFIG_H ein. Nur MSVC, nur Float-Variante. */
#ifndef TACRELAY_SPEEXDSP_CONFIG_H
#define TACRELAY_SPEEXDSP_CONFIG_H

#ifndef _MSC_VER
#error "Diese config.h ist fuer MSVC gedacht."
#endif

/* Float-Variante: spx_word16_t ist float, process_float arbeitet ohne Umrechnung. */
#define FLOATING_POINT

/* SSE/SSE2 gehoeren auf x64 zur Grundausstattung; MSVC braucht dafuer keinen
   Schalter. resample_sse.h nutzt nur unausgerichtete Ladebefehle (_mm_loadu_ps).
   Abschaltbar ueber TR_SPEEX_OHNE_SSE (CMake-Option TR_SPEEX_SSE=OFF). */
#if !defined(TR_SPEEX_OHNE_SSE)
#  if defined(_M_X64) || (defined(_M_IX86_FP) && _M_IX86_FP >= 1)
#    define USE_SSE
#  endif
#  if defined(_M_X64) || (defined(_M_IX86_FP) && _M_IX86_FP >= 2)
#    define USE_SSE2
#  endif
#endif

/* Kein VAR_ARRAYS (MSVC kennt keine VLAs) und kein USE_ALLOCA: der Resampler
   nimmt dann einen festen Stapelpuffer von 1024 Werten (nur process_int). */

/* Die Exportliste steht in speexdsp_resampler.def, nicht in den Quellen. */
#define EXPORT

#endif /* TACRELAY_SPEEXDSP_CONFIG_H */
