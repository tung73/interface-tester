@echo off
setlocal
cd /d "%~dp0"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "MSBUILD="

if exist "%VSWHERE%" (
  for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
    set "MSBUILD=%%i"
  )
)

if not defined MSBUILD (
  echo MSBuild was not found.
  echo Open InterfaceTester.sln in Visual Studio, then press Ctrl+Shift+B to build.
  pause
  exit /b 1
)

echo Building InterfaceTester...
"%MSBUILD%" "%~dp0InterfaceTester.sln" /p:Configuration=Debug /p:Platform="Any CPU" /v:m
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)

set "EXE=%~dp0bin\Debug\InterfaceTester.exe"
if not exist "%EXE%" (
  echo Build finished, but InterfaceTester.exe was not found at:
  echo   %EXE%
  pause
  exit /b 1
)

echo.
"%EXE%" %*
echo.
pause
