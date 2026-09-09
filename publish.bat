@echo off
setlocal
echo ===================================================
echo   Backup Explorer - Single-File Executable Builder
echo ===================================================
echo.
echo Select an option:
echo   [1] Lightweight Single-File EXE (~9 MB, requires .NET 8 on the PC)
echo   [2] 100%% Self-Contained Single-File EXE (runs anywhere without .NET installed)
echo   [3] Build Both
echo.
set /p choice="Enter choice (1, 2, or 3) [default: 1]: "
if "%choice%"=="" set choice=1

cd /d "%~dp0"

if "%choice%"=="1" goto build_portable
if "%choice%"=="2" goto build_standalone
if "%choice%"=="3" goto build_both

:build_portable
echo.
echo Building Lightweight Single-File EXE...
dotnet publish src/BackupProjekt.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o publish/Lightweight
echo.
echo Output: %~dp0publish\Lightweight\BackupExplorer.exe
goto done

:build_standalone
echo.
echo Building Self-Contained Standalone EXE...
dotnet publish src/BackupProjekt.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish/Standalone
echo.
echo Output: %~dp0publish\Standalone\BackupExplorer.exe
goto done

:build_both
echo.
echo Building Lightweight Single-File EXE...
dotnet publish src/BackupProjekt.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -o publish/Lightweight
echo.
echo Building Self-Contained Standalone EXE...
dotnet publish src/BackupProjekt.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish/Standalone
echo.
echo Outputs:
echo   %~dp0publish\Lightweight\BackupExplorer.exe
echo   %~dp0publish\Standalone\BackupExplorer.exe
goto done

:done
echo.
echo Build completed successfully!
pause
