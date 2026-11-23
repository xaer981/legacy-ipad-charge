@echo off
setlocal
cd /d "%~dp0"

if /i not "%COMPUTERNAME%"=="IPAD-LAB" goto wrong_machine
reg query HKLM\HARDWARE\DESCRIPTION\System\BIOS /v SystemProductName | findstr /i "VirtualBox" >nul
if errorlevel 1 goto wrong_machine
bcdedit /enum {current} | findstr /r /i "^testsigning  *Yes" >nul
if errorlevel 1 (
    echo This script requires an elevated console in the test-signing guest.
    pause
    exit /b 1
)

IpadStatusProbe.exe --check >nul 2>&1
if not errorlevel 1 goto test
call :install > Install-Guest.log 2>&1
set "result=%errorlevel%"
type Install-Guest.log
if not "%result%"=="0" (
    pause
    exit /b %result%
)

:test
choice /c YN /n /m "Is the iPad connected to this guest? Send one GET_STATUS request? [Y/N] "
if not "%errorlevel%"=="1" exit /b 0
IpadStatusProbe.exe --run-once > Get-Status.log 2>&1
set "result=%errorlevel%"
type Get-Status.log
pause
exit /b %result%

:install
certutil -f -addstore Root LabSigner.cer || exit /b 1
certutil -f -addstore TrustedPublisher LabSigner.cer || exit /b 1
pnputil /add-driver driver\LegacyIpadStatus.inf /install
set "installExit=%errorlevel%"
if "%installExit%"=="3010" (
    echo Windows requests a guest reboot. Verify the iPad connection after restarting.
    exit /b 3010
)
if not "%installExit%"=="0" exit /b %installExit%

pnputil /enum-devices /connected /deviceid "USB\VID_05AC&PID_12A2" /stack /services
IpadStatusProbe.exe --check
exit /b %errorlevel%

:wrong_machine
echo Refusing to install outside the IPAD-LAB VirtualBox guest.
exit /b 1
