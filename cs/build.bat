@echo off
rem Build OduApp (WPF, code-only XAML-free) with in-box Framework csc.
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set GAC=C:\Windows\Microsoft.NET\assembly
set PF=%GAC%\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll
set PC=%GAC%\GAC_32\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll
set WB=%GAC%\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll
set XA=%GAC%\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll
set NH=%GAC%\GAC_MSIL\System.Net.Http\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Net.Http.dll
set WE=%GAC%\GAC_MSIL\System.Web.Extensions\v4.0_4.0.0.0__31bf3856ad364e35\System.Web.Extensions.dll
for %%A in ("%PF%" "%PC%" "%WB%" "%XA%" "%NH%" "%WE%") do if not exist %%A ( echo ERROR: missing %%~A & exit /b 1 )
set ROOT=%~dp0
set OUT=%ROOT%bin
mkdir "%OUT%" 2>nul
"%CSC%" /nologo /t:winexe /platform:anycpu /o+ ^
  /r:"%PF%" /r:"%PC%" /r:"%WB%" /r:"%XA%" /r:"%NH%" ^
  /r:System.dll /r:System.Core.dll /r:System.Xml.dll ^
  /out:"%OUT%\OduApp.exe" ^
  "%ROOT%OduApp\*.cs" || exit /b 1
echo OK: %OUT%\OduApp.exe
