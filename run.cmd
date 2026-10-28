@echo off
setlocal
rem Opens the set piece painter (see README).
rem   run.cmd                          a new piece in the home biome
rem   run.cmd --biome=frost            a new piece in Rimefrost
rem   run.cmd grove                    open data/setpieces/grove.json (other options may follow)
rem Set GODOT to use another Godot 4.7 .NET editor; set PREFABS_NOBUILD=1 to skip the C# build.
if not defined GODOT set "GODOT=D:\installed\godot-4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
if not exist "%GODOT%" (
    echo Godot not found at "%GODOT%". Set GODOT to your Godot 4.7 .NET editor executable.
    exit /b 1
)

if not exist "%~dp0assets\synty\nature" python "%~dp0tools\fetch_synty.py" || exit /b 1
if not defined PREFABS_NOBUILD dotnet build "%~dp0ArcPrefabs.csproj" -nologo -v:q || exit /b 1
if not exist "%~dp0.godot" "%GODOT%" --headless --path "%~dp0." --import

set "PIECE=%~1"
if not defined PIECE goto launch
if "%PIECE:~0,2%"=="--" goto launch
rem The options after the name, cut from %* (%1 and shift would split them at '=').
set "ALL=%*"
call set "REST=%%ALL:*%~1=%%"
"%GODOT%" --path "%~dp0." -- --paint=%PIECE%%REST%
exit /b %errorlevel%

:launch
"%GODOT%" --path "%~dp0." -- %*
