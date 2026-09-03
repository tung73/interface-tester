@echo off
setlocal
cd /d "%~dp0"

echo Closing cached Visual Studio settings that skip the InterfaceTester build...
if exist "%~dp0.vs" rd /s /q "%~dp0.vs"
if exist "%~dp0InterfaceTester\bin" rd /s /q "%~dp0InterfaceTester\bin"
if exist "%~dp0InterfaceTester\obj" rd /s /q "%~dp0InterfaceTester\obj"

echo.
echo Opening InterfaceTester.sln ...
echo After Visual Studio opens:
echo   1. Right-click InterfaceTester - Set as Startup Project
echo   2. Build - Rebuild Solution  (must say 1 succeeded, not 1 skipped)
echo   3. Press F5
echo.

start "" "%~dp0InterfaceTester.sln"
