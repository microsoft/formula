@echo off
set Configuration=%1
set Platform=%2

set SCRIPTDIR=%~dp0

set BINDIR=%SCRIPTDIR%\%Configuration%\%Platform%
set VSDIR=%SCRIPTDIR%\%Configuration%\%Platform%\Vs

if NOT exist "%BINDIR%" goto :nobits
if NOT exist "%VSDIR%" mkdir "%VSDIR%"

copy  /Y "%BINDIR%" "%VSDIR%"

goto :eof
:nobits
echo That's odd there's nothing in your output dir: %BINDIR%
goto :eof