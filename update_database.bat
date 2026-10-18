@echo off
rem Adds all new .hex files from the References folder to firmware_db.txt (existing entries
rem are kept) and compiles dist\AcebeamHexChecker.exe with the database embedded.
rem Only needs the C# compiler that ships with Windows (.NET Framework 4).

setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo ERROR: C# compiler not found. .NET Framework 4 is required.
    goto :fail
)

if not exist build mkdir build
if not exist dist mkdir dist

echo === Compiling database builder
"%CSC%" /nologo /optimize /target:exe /out:build\BuildDatabase.exe src\HexImage.cs src\Builder.cs || goto :fail

echo.
echo === Merging References into firmware_db.txt
if not exist References mkdir References
build\BuildDatabase.exe References firmware_db.txt
if errorlevel 2 goto :fail
if errorlevel 1 echo WARNING: some files were skipped, see above.

echo.
echo === Compiling checker
"%CSC%" /nologo /optimize /target:winexe /out:dist\AcebeamHexChecker.exe /resource:firmware_db.txt,firmware_db.txt src\HexImage.cs src\Checker.cs || goto :fail

echo.
echo Done: dist\AcebeamHexChecker.exe (database embedded)
if "%~1"=="" pause
exit /b 0

:fail
echo.
echo BUILD FAILED
if "%~1"=="" pause
exit /b 1
