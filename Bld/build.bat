echo off
set bldexe = ""

if exist "%WinDir%\Microsoft.NET\Framework64\v4.0.30319\msbuild.exe" (
  set bldexe="%WinDir%\Microsoft.NET\Framework64\v4.0.30319\msbuild.exe"
) else if exist "%WinDir%\Microsoft.NET\Framework\v4.0.30319\msbuild.exe" (
  set bldexe="%WinDir%\Microsoft.NET\Framework\v4.0.30319\msbuild.exe"
) else (
  echo Microsoft.NET framework version 4.0 or greater is required. Cannot build.
  exit /B 1)

echo Bootstrapping the build utility...
%bldexe%  .\FormulaBuild\FormulaBuild\FormulaBuild.csproj /t:Clean /p:Configuration=Debug /p:Platform=AnyCPU /verbosity:quiet /nologo
if %ERRORLEVEL% neq 0 (
  echo Could not clean build utility.
  exit /B 1
)

%bldexe%  .\FormulaBuild\FormulaBuild\FormulaBuild.csproj /t:Build /p:Configuration=Debug /p:Platform=AnyCPU /verbosity:quiet /nologo
if %ERRORLEVEL% neq 0 (
  echo Could not compile build utility.
  exit /B 1
)

.\FormulaBuild\FormulaBuild\bin\Debug\FormulaBuild.exe %1
if %ERRORLEVEL% neq 0 (
  echo Build failed.
  exit /B 1
) else ( exit /B 0 )