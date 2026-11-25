@echo off
rem JG TacRelay Probefunk pruefen, ohne Fenster und ohne Mikrofon: Testserver mit Echo-Funker, dann spricht
rem funk-testclient eine kuenstliche Sekunde ins Netz Echo und prueft, ob sie vollstaendig und in der richtigen
rem Reihenfolge zurueckkommt. Alles bleibt auf diesem PC (nur 127.0.0.1).
setlocal
cd /d "%~dp0"
"%~dp0server\funk-testserver.exe" --probe "%~dp0server\konfig\probefunk.json" --zustand "%~dp0probe-zustand" --testzugang 0 --echo --client "%~dp0server\funk-testclient.exe" --client-argumente "echo-probe --testzugang {testzugang}"
if errorlevel 1 (
    echo.
    echo Pruefung NICHT bestanden. Die Meldungen stehen weiter oben.
) else (
    echo.
    echo Pruefung bestanden: Testserver, Echo-Funker und Rueckweg arbeiten.
)
pause
endlocal
