@echo off
setlocal

set "PZ_DOTNET=C:\Program Files\dotnet\dotnet.exe"
if exist "%PZ_DOTNET%" goto run

set "PZ_DOTNET=C:\Program Files (x86)\dotnet\dotnet.exe"
if exist "%PZ_DOTNET%" goto run

echo O .NET SDK nao foi encontrado. Instale o SDK x64 antes de executar o gerenciador.
exit /b 1

:run
cd /d "%~dp0"
echo PZ Server Manager: http://localhost:5268
echo Pressione Ctrl+C para encerrar.
"%PZ_DOTNET%" run --project ".\src\PzServerManager.Web\PzServerManager.Web.csproj" --launch-profile http
exit /b %ERRORLEVEL%
