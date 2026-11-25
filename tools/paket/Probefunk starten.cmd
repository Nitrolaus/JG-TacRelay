@echo off
rem JG TacRelay Probefunk: Testserver mit Echo-Funker auf diesem PC, danach der Client mit Testzugang.
rem Alles bleibt auf diesem PC (nur 127.0.0.1). Der Testserver endet, wenn du JG TacRelay schliesst.
setlocal
cd /d "%~dp0"
rem Eigene Einstellungen fuer die Probe, getrennt von den normalen des Clients.
set "TACRELAY_EINSTELLUNGEN=%~dp0probe-einstellungen.json"
echo JG TacRelay Probefunk
echo Dieses Fenster ist der Testserver mit dem Echo-Funker. Es geht zu, wenn du JG TacRelay beendest.
echo.
"%~dp0server\funk-testserver.exe" --probe "%~dp0server\konfig\probefunk.json" --zustand "%~dp0probe-zustand" --testzugang 0 --echo --client "%~dp0client\TacRelay.exe" --client-freigegeben
if errorlevel 1 (
    echo.
    echo Der Probefunk wurde mit einem Fehler beendet. Die Meldung steht weiter oben.
    pause
)
endlocal
