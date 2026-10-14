@echo off
rem Build + run the C# test runner (console). No test framework needed.
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set GAC=C:\Windows\Microsoft.NET\assembly
set NH=%GAC%\GAC_MSIL\System.Net.Http\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Net.Http.dll
set ROOT=%~dp0
set OUT=%ROOT%bin
mkdir "%OUT%" 2>nul
set MAIN=%ROOT%OduTest\Main.cs
if not exist "%MAIN%" ( echo ERROR: missing Main & exit /b 1 )
"%CSC%" /nologo /t:exe /platform:anycpu /o+ ^
  /r:"%NH%" /r:System.dll /r:System.Core.dll /r:System.Xml.dll ^
  /out:"%OUT%\OduTest.exe" ^
  "%ROOT%OduApp\Models.cs" "%ROOT%OduApp\ArchUtil.cs" "%ROOT%OduApp\Catalog.cs" ^
  "%ROOT%OduApp\Store.cs" "%ROOT%OduApp\NativeScan.cs" "%ROOT%OduApp\Ops.cs" ^
  "%ROOT%OduApp\Updater.cs" "%ROOT%OduTest\OduTest.cs" "%ROOT%OduTest\Main.cs" || exit /b 1
"%OUT%\OduTest.exe"
