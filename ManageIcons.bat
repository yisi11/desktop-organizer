@echo off
:menu
cls
echo ===============================
echo   DESKTOP ICON MANAGER
echo ===============================
echo [1] Save Current Layout
echo [2] Restore Saved Layout
echo [3] Exit
echo.
set /p choice="Choose an option (1-3): "

if "%choice%"=="1" (
    DesktopRestore.exe save
    pause
    goto menu
)
if "%choice%"=="2" (
    DesktopRestore.exe restore
    pause
    goto menu
)
if "%choice%"=="3" exit
goto menu