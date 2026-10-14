@echo off
rem Build native components (C + C++) with VS2022 MSVC. No cmake needed.
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\VcVars64.bat" >nul 2>&1
if errorlevel 1 ( echo ERROR: VcVars64 failed & exit /b 1 )
set ROOT=%~dp0
set OUT=%ROOT%bin
mkdir "%OUT%\x64" "%OUT%\x86" 2>nul

set RC="C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\rc.exe"
if not exist %RC% set RC=rc.exe

echo [x64] odu_hw.lib + odu_scan.exe
cl /nologo /O2 /std:c11 /c "%ROOT%odu_hw\odu_hw.c" /Fo"%OUT%\x64\odu_hw.obj" || exit /b 1
lib /nologo /OUT:"%OUT%\x64\odu_hw.lib" "%OUT%\x64\odu_hw.obj" || exit /b 1
cl /nologo /O2 /std:c++17 /EHsc "%ROOT%odu_scan\scan.cpp" "%OUT%\x64\odu_hw.obj" ^
  /I"%ROOT%odu_hw" /Fo"%OUT%\x64\scan.obj" /Fe"%OUT%\x64\odu_scan.exe" /link SetupAPI.Lib Cfgmgr32.Lib Advapi32.Lib || exit /b 1
%RC% /nologo /fo"%OUT%\x64\version.res" "%ROOT%odu_scan\version.rc" >nul 2>&1
if exist "%OUT%\x64\version.res" (
  link /nologo "%OUT%\x64\scan.obj" "%OUT%\x64\odu_hw.obj" "%OUT%\x64\version.res" ^
    /OUT:"%OUT%\x64\odu_scan.exe" SetupAPI.Lib Cfgmgr32.Lib Advapi32.Lib >nul || echo [warn] version stamp skipped
)

echo [selftest] odu_hw C library
cl /nologo "%ROOT%odu_hw\test_hw.c" "%OUT%\x64\odu_hw.obj" /Fo"%OUT%\x64\test_hw.obj" /Fe"%OUT%\x64\hw_test.exe" || exit /b 1
"%OUT%\x64\hw_test.exe" || exit /b 1
echo [x86] odu_hw.lib + odu_scan.exe
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\VcVars32.bat" >nul 2>&1
cl /nologo /O2 /std:c11 /c "%ROOT%odu_hw\odu_hw.c" /Fo"%OUT%\x86\odu_hw.obj" || exit /b 1
lib /nologo /OUT:"%OUT%\x86\odu_hw.lib" "%OUT%\x86\odu_hw.obj" || exit /b 1
cl /nologo /O2 /std:c++17 /EHsc "%ROOT%odu_scan\scan.cpp" "%OUT%\x86\odu_hw.obj" ^
  /I"%ROOT%odu_hw" /Fo"%OUT%\x86\scan.obj" /Fe"%OUT%\x86\odu_scan.exe" /link SetupAPI.Lib Cfgmgr32.Lib Advapi32.Lib || exit /b 1

echo OK: %OUT%\x64\odu_scan.exe %OUT%\x86\odu_scan.exe
