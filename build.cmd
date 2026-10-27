@echo off
rem Builds Portal.exe with the C# compiler that ships with Windows (.NET Framework 4) - nothing to install.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize /unsafe /out:Portal.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\Portal.cs
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
echo Done: Portal.exe - run it once and it installs itself to %%LOCALAPPDATA%%\Portal
