@echo off
rem Build native components (C + C++) with VS2022 MSVC. No cmake needed.
rem Locates VS via vswhere (Community/Enterprise/Professional/BuildTools) so the
rem same script works on a dev box and on GitHub's windows-latest runners.
setlocal
set "VCVARS="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSINSTALL=%%i"
if defined VSINSTALL if exist "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" set "VCVARS=%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat"
if not defined VCVARS for %%e in (Community Enterprise Professional BuildTools) do (
  if not defined VCVARS if exist "%ProgramFiles%\Microsoft Visual Studio\2022\%%e\VC\Auxiliary\Build\vcvars64.bat" set "VCVARS=%ProgramFiles%\Microsoft Visual Studio\2022\%%e\VC\Auxiliary\Build\vcvars64.bat"
)
if not defined VCVARS (
  echo ERROR: vcvars64.bat not found ^(install VS2022 with the C++ workload^)
  exit /b 1
)
call "%VCVARS%" >nul 2>&1
if errorlevel 1 ( echo ERROR: vcvars64 failed & exit /b 1 )
set "VCVARS32=%VCVARS:vcvars64.bat=vcvars32.bat%"
set ROOT=%~dp0
set OUT=%ROOT%bin
mkdir "%OUT%\x64" "%OUT%\x86" 2>nul

rem The SDK version varies by machine; derive rc.exe from the active toolchain
rem and fall back to PATH if that layout is not present.
set "RC=rc.exe"
if defined WindowsSdkDir if defined WindowsSDKVersion if defined VSCMD_ARG_TGT_ARCH (
  if exist "%WindowsSdkDir%bin\%WindowsSDKVersion%%VSCMD_ARG_TGT_ARCH%\rc.exe" set "RC=%WindowsSdkDir%bin\%WindowsSDKVersion%%VSCMD_ARG_TGT_ARCH%\rc.exe"
)

echo [x64] odu_hw.lib + odu_scan.exe
cl /nologo /O2 /std:c11 /c "%ROOT%odu_hw\odu_hw.c" /Fo"%OUT%\x64\odu_hw.obj" || exit /b 1
lib /nologo /OUT:"%OUT%\x64\odu_hw.lib" "%OUT%\x64\odu_hw.obj" || exit /b 1
cl /nologo /O2 /std:c++17 /EHsc "%ROOT%odu_scan\scan.cpp" "%OUT%\x64\odu_hw.obj" ^
  /I"%ROOT%odu_hw" /Fo"%OUT%\x64\scan.obj" /Fe"%OUT%\x64\odu_scan.exe" /link SetupAPI.Lib Cfgmgr32.Lib Advapi32.Lib || exit /b 1
"%RC%" /nologo /fo"%OUT%\x64\version.res" "%ROOT%odu_scan\version.rc" >nul 2>&1
if exist "%OUT%\x64\version.res" (
  link /nologo "%OUT%\x64\scan.obj" "%OUT%\x64\odu_hw.obj" "%OUT%\x64\version.res" ^
    /OUT:"%OUT%\x64\odu_scan.exe" SetupAPI.Lib Cfgmgr32.Lib Advapi32.Lib >nul || echo [warn] version stamp skipped
)

echo [selftest] odu_hw C library
cl /nologo "%ROOT%odu_hw\test_hw.c" "%OUT%\x64\odu_hw.obj" /Fo"%OUT%\x64\test_hw.obj" /Fe"%OUT%\x64\hw_test.exe" || exit /b 1
"%OUT%\x64\hw_test.exe" || exit /b 1
echo [x86] odu_hw.lib + odu_scan.exe
call "%VCVARS32%" >nul 2>&1
cl /nologo /O2 /std:c11 /c "%ROOT%odu_hw\odu_hw.c" /Fo"%OUT%\x86\odu_hw.obj" || exit /b 1
lib /nologo /OUT:"%OUT%\x86\odu_hw.lib" "%OUT%\x86\odu_hw.obj" || exit /b 1
cl /nologo /O2 /std:c++17 /EHsc "%ROOT%odu_scan\scan.cpp" "%OUT%\x86\odu_hw.obj" ^
  /I"%ROOT%odu_hw" /Fo"%OUT%\x86\scan.obj" /Fe"%OUT%\x86\odu_scan.exe" /link SetupAPI.Lib Cfgmgr32.Lib Advapi32.Lib || exit /b 1

echo OK: %OUT%\x64\odu_scan.exe %OUT%\x86\odu_scan.exe
