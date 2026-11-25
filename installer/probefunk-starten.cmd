@echo off
rem JG TacRelay Probefunk aus dem Setup: Testserver mit Echo-Funker auf diesem PC, danach JG TacRelay mit Testzugang.
rem Alles bleibt auf diesem PC (nur 127.0.0.1). Der Testserver endet, wenn du JG TacRelay schliesst.
rem
rem Liegt im Programmordner unter probefunk\ und wird ueber die Verknuepfung "JG TacRelay Probefunk" im Startmenue
rem gestartet. Eine .cmd statt des Aufrufs direkt in der Verknuepfung, weil nur sie
rem   - die Einstellungen der Probe getrennt halten kann (TACRELAY_EINSTELLUNGEN, eine Verknuepfung setzt keine
rem     Umgebungsvariablen),
rem   - %LOCALAPPDATA% erst beim Start aufloest, nicht beim Installieren,
rem   - bei einem Fehler das Fenster offen haelt, damit die Meldung lesbar bleibt.
rem Einstellungen und Zustand der Probe liegen unter %LOCALAPPDATA%\JG-TacRelay\probe, nie im Programmordner (der muss
rem nicht beschreibbar sein). Den Zustand loescht der Testserver beim Beenden; der Pfad ist kurz genug fuer den
rem Verwaltungssocket (hoechstens 100 Byte, Probeserver.SocketPfad).
setlocal
for %%I in ("%~dp0..") do set "TR_PROGRAMM=%%~fI"
set "TR_PROBE=%LOCALAPPDATA%\JG-TacRelay\probe"
if not exist "%TR_PROBE%" mkdir "%TR_PROBE%"
set "TACRELAY_EINSTELLUNGEN=%TR_PROBE%\probe-einstellungen.json"
cd /d "%TR_PROBE%"
title JG TacRelay Probefunk
echo JG TacRelay Probefunk
echo Dieses Fenster ist der Testserver mit dem Echo-Funker. Es geht zu, wenn du JG TacRelay beendest.
echo.
"%~dp0funk-testserver.exe" --probe "%~dp0konfig\probefunk.json" --zustand "%TR_PROBE%\zustand" --testzugang 0 --echo --client "%TR_PROGRAMM%\TacRelay.exe" --client-freigegeben
if errorlevel 1 (
    echo.
    echo Der Probefunk wurde mit einem Fehler beendet. Die Meldung steht weiter oben.
    pause
)
endlocal
