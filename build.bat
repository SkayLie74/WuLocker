@echo off
setlocal
title WuLocker Compiler
echo ==============================================
echo           WuLocker Automated Build
echo ==============================================
echo.

echo Validating locale files...
powershell -NoProfile -ExecutionPolicy Bypass -File tools\validate-locales.ps1
if not "%errorlevel%"=="0" (
    echo.
    echo ==============================================
    echo [ERROR] Locale validation failed!
    echo ==============================================
    exit /b 1
)

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if not exist "%CSC%" (
    echo.
    echo ==============================================
    echo [ERROR] .NET Framework C# compiler was not found.
    echo ==============================================
    exit /b 1
)

echo.
echo Compiling WuLocker sources...

"%CSC%" /target:winexe /codepage:65001 /utf8output /reference:System.Web.Extensions.dll /reference:System.ServiceProcess.dll /win32icon:assets\WuLocker.ico /win32manifest:app.manifest /out:WuLocker.exe src\*.cs

if %errorlevel% equ 0 (
    echo.
    echo ==============================================
    echo [SUCCESS] Compilation completed successfully!
    echo [INFO] Created WuLocker.exe in the root folder.
    echo ==============================================
) else (
    echo.
    echo ==============================================
    echo [ERROR] Compilation failed!
    echo ==============================================
    exit /b 1
)
echo.
if "%1"=="/nopause" (
    exit /b 0
)
pause
