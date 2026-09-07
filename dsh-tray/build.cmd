@echo off
set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%~dp0bin
if not exist "%OUT%" mkdir "%OUT%"
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:"%OUT%\DshTray.exe" ^
  /reference:System.dll /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ^
  /reference:System.Management.dll ^
  %~dp0src\*.cs
if errorlevel 1 (echo 编译失败) else (echo 编译成功：%OUT%\DshTray.exe)
pause