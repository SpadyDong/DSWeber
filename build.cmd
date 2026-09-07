@echo off
setlocal

if "%SystemRoot%"=="" (
  echo Build failed: SystemRoot is not configured.
  exit /b 1
)

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "SRC=%~dp0src\desktop-host"
set "OUT=%~dp0dist"

if not exist "%CSC%" (
  echo Build failed: C# compiler not found at %CSC%
  exit /b 1
)

if not exist "%SRC%\*.cs" (
  echo Build failed: source files not found at %SRC%\*.cs
  exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"%OUT%\DshTray.exe" ^
  /reference:System.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ^
  /reference:System.Management.dll ^
  "%SRC%\*.cs"

if errorlevel 1 (
  echo Build failed.
  exit /b 1
)

echo Build succeeded: %OUT%\DshTray.exe
exit /b 0
