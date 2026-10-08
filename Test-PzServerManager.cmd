@echo off
setlocal

set "PZ_DOTNET=C:\Program Files\dotnet\dotnet.exe"
if exist "%PZ_DOTNET%" goto run

set "PZ_DOTNET=C:\Program Files (x86)\dotnet\dotnet.exe"
if exist "%PZ_DOTNET%" goto run

echo O .NET SDK nao foi encontrado.
exit /b 1

:run
cd /d "%~dp0"
"%PZ_DOTNET%" test ".\PzServerManager.sln" --configuration Debug
exit /b %ERRORLEVEL%
