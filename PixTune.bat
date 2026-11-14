@echo off
rem Launches PixTune from the folder this file is in, wherever that folder is.
rem Drop a photo on this file to open it directly.

rem Adobe DNG Converter (free) develops the few RAW files PixTune cannot read by itself, such as Nikon Z6III
rem "High Efficiency" NEFs: offered once if it is missing (a "No" is remembered; run Install-DngConverter.ps1 to be asked again).
if not exist "%ProgramFiles%\Adobe\Adobe DNG Converter\Adobe DNG Converter.exe" (
  if not exist "%APPDATA%\PixTune\dng-converter-declined" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Install-DngConverter.ps1" -Auto
  )
)

start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0PixTune.ps1" %*
