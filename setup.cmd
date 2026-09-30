@echo off
rem WatchCrawler setup for Windows: runs setup.sh with Git Bash.
rem Double-click this file, or run:  setup.cmd [--build-only | --usage | --topup 10 | --dry-run]
setlocal
set "BASH="
for %%P in ("%ProgramFiles%\Git\bin\bash.exe" "%ProgramFiles(x86)%\Git\bin\bash.exe" "%LocalAppData%\Programs\Git\bin\bash.exe") do (
    if exist %%P if not defined BASH set "BASH=%%~P"
)
if not defined BASH (
    echo Git for Windows is needed ^(it includes the bash shell this setup uses^).
    echo Install it from https://git-scm.com/download/win and double-click setup.cmd again.
    pause
    exit /b 1
)
"%BASH%" "%~dp0setup.sh" %*
set "RC=%ERRORLEVEL%"
echo.
pause
exit /b %RC%
