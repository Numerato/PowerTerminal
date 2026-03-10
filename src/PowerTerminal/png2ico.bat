@echo off
REM pngs-to-ico.bat

REM Requires ImageMagick (magick) in PATH.

setlocal enabledelayedexpansion


set "SRC=iconspng"
set "DST=iconspng\ico"
set "RECURSIVE=0"

REM Define sizes to include in the .ico
set "ICON_SIZES=256,128,64,48,32,16"

echo Converting PNG files from "%SRC%" to ICO files in "%DST%"
if "%RECURSIVE%"=="1" echo Recursive mode: ON

if "%RECURSIVE%"=="1" (
  for /r "%SRC%" %%F in (*.png) do (
    set "REL=%%~pF"
    set "REL=!REL:~1,-1!"
    if not exist "%DST%\!REL!" md "%DST%\!REL!" 2>nul
    set "OUT=%DST%\!REL!\%%~nF.ico"
    echo Converting "%%F" -> "!OUT!"
    magick "%%F" -define icon:auto-resize="%ICON_SIZES%" "!OUT!"
  )
) else (
  for %%F in ("%SRC%\*.png") do (
    set "OUT=%DST%\%%~nF.ico"
    echo Converting "%%~fF" -> "!OUT!"
    magick "%%~fF" -define icon:auto-resize="%ICON_SIZES%" "!OUT!"
  )
)

echo Done.
endlocal