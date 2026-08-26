@echo off
setlocal enabledelayedexpansion

:: ── Keep window open always ───────────────────────────────────────────────
if /i "%~1"=="RELAUNCHED" goto :main
cmd /k ""%~f0" RELAUNCHED"
exit /b

:main
title MHZ Multiplayer Mod Installer
color 0A

echo.
echo  =====================================================
echo    MH-Zombie Multiplayer Mod Installer v1.0
echo  =====================================================
echo.

:: ── Config ────────────────────────────────────────────────────────────────
set "GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\MH-Zombie\MHZ Build 13.1"
set "SCRIPT_DIR=%~dp0"
set "BEPINEX_URL=https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip"
set "BEPINEX_ZIP=%TEMP%\BepInEx.zip"
set "DOTNET_DIR=%USERPROFILE%\.dotnet-mhz"
set "DOTNET_EXE=%DOTNET_DIR%\dotnet.exe"
set "MHZ_EXE=!GAME_DIR!\MHZ.exe"
set "BEPINEX_CORE=!GAME_DIR!\BepInEx\core\BepInEx.dll"
set "PLUGINS_DIR=!GAME_DIR!\BepInEx\plugins"
set "CSPROJ=!SCRIPT_DIR!MHZombieMultiplayer.csproj"

:: ── DIAGNOSTICS — printed before anything runs ───────────────────────────
echo  ---- DIAGNOSTICS ----
echo  SCRIPT_DIR  = [!SCRIPT_DIR!]
echo  GAME_DIR    = [!GAME_DIR!]
echo  MHZ_EXE     = [!MHZ_EXE!]
echo  DOTNET_DIR  = [!DOTNET_DIR!]
echo  DOTNET_EXE  = [!DOTNET_EXE!]
echo  CSPROJ      = [!CSPROJ!]
echo  PLUGINS_DIR = [!PLUGINS_DIR!]
echo  TEMP        = [%TEMP%]
echo  USERNAME    = [%USERNAME%]
echo  OS          = [%OS%]
echo.

echo  ---- FILE EXISTENCE CHECKS ----
if exist "!MHZ_EXE!"       (echo  [EXISTS]  MHZ.exe) else (echo  [MISSING] MHZ.exe)
if exist "!DOTNET_EXE!"    (echo  [EXISTS]  dotnet.exe) else (echo  [MISSING] dotnet.exe)
if exist "!CSPROJ!"        (echo  [EXISTS]  .csproj) else (echo  [MISSING] .csproj)
if exist "!BEPINEX_CORE!"  (echo  [EXISTS]  BepInEx core) else (echo  [MISSING] BepInEx core)
if exist "!PLUGINS_DIR!"   (echo  [EXISTS]  plugins folder) else (echo  [MISSING] plugins folder)
echo.

echo  ---- TOOL CHECKS ----
where curl    2>nul && echo  [EXISTS]  curl    || echo  [MISSING] curl
where dotnet  2>nul && echo  [EXISTS]  dotnet ^(system^) || echo  [MISSING] dotnet ^(system^) - ok, will install locally
powershell -Command "Write-Host '  [EXISTS]  PowerShell' $PSVersionTable.PSVersion" 2>nul || echo  [MISSING] PowerShell
echo.

echo  ---- GAME FOLDER CONTENTS ----
if exist "!GAME_DIR!" (
    dir "!GAME_DIR!" /b 2>&1
) else (
    echo  [ERROR] Game folder does not exist at all: !GAME_DIR!
)
echo.
echo  ---- END DIAGNOSTICS ----
echo.

:: ── Step 1: Validate game path ────────────────────────────────────────────
echo [1/6] Checking game path...

if not exist "!MHZ_EXE!" (
    echo.
    echo  [ERROR - STEP 1] MHZ.exe not found.
    echo  Looked for : !MHZ_EXE!
    echo  GAME_DIR is: !GAME_DIR!
    echo.
    echo  Contents of parent folder:
    dir "C:\Program Files (x86)\Steam\steamapps\common\MH-Zombie" /b 2>&1
    echo.
    echo  Fix: Edit GAME_DIR in this bat to match the folder containing MHZ.exe
    goto :fail
)
echo  [OK] MHZ.exe found.

:: ── Step 2: Check system tools ────────────────────────────────────────────
echo.
echo [2/6] Checking system tools...

where curl >nul 2>&1
if errorlevel 1 (
    echo  [ERROR - STEP 2] curl not found.
    echo  Fix: Update Windows to version 1803 or later.
    goto :fail
)
echo  [OK] curl found.

powershell -Command "exit 0" >nul 2>&1
if errorlevel 1 (
    echo  [ERROR - STEP 2] PowerShell failed to launch.
    echo  Error code: !errorlevel!
    goto :fail
)
echo  [OK] PowerShell found.

:: ── Step 3: Install .NET SDK ──────────────────────────────────────────────
echo.
echo [3/6] Setting up .NET SDK...
echo  Checking: !DOTNET_EXE!

if exist "!DOTNET_EXE!" (
    echo  [OK] dotnet.exe already exists, skipping install.
    goto :dotnet_done
)

echo  Not found. Downloading install script...
curl -L --progress-bar -o "%TEMP%\dotnet-install.ps1" "https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1"
echo  curl exit code: !errorlevel!
if !errorlevel! neq 0 (
    echo  [ERROR - STEP 3] curl failed to download dotnet-install.ps1
    echo  Exit code: !errorlevel!
    goto :fail
)

if not exist "%TEMP%\dotnet-install.ps1" (
    echo  [ERROR - STEP 3] dotnet-install.ps1 not found after download.
    echo  Expected at: %TEMP%\dotnet-install.ps1
    goto :fail
)
echo  [OK] Install script downloaded to %TEMP%\dotnet-install.ps1

echo  Running installer...
powershell -ExecutionPolicy Bypass -File "%TEMP%\dotnet-install.ps1" -Channel 6.0 -InstallDir "!DOTNET_DIR!"
echo  PowerShell exit code: !errorlevel!
if !errorlevel! neq 0 (
    echo  [ERROR - STEP 3] dotnet-install.ps1 failed.
    echo  Exit code: !errorlevel!
    echo  Fix: Try running as Administrator.
    goto :fail
)

if not exist "!DOTNET_EXE!" (
    echo  [ERROR - STEP 3] Install ran but dotnet.exe still missing.
    echo  Expected: !DOTNET_EXE!
    echo  Contents of !DOTNET_DIR!:
    dir "!DOTNET_DIR!" /b 2>&1
    goto :fail
)
echo  [OK] dotnet.exe installed at !DOTNET_EXE!

:dotnet_done
echo  Verifying dotnet works...
"!DOTNET_EXE!" --version
echo  dotnet --version exit code: !errorlevel!
if !errorlevel! neq 0 (
    echo  [ERROR - STEP 3] dotnet.exe exists but failed to run.
    echo  Path: !DOTNET_EXE!
    goto :fail
)
echo  [OK] dotnet is functional.

:: ── Step 4: Patch csproj and build ───────────────────────────────────────
echo.
echo [4/6] Building mod...
echo  csproj path: !CSPROJ!

if not exist "!CSPROJ!" (
    echo  [ERROR - STEP 4] .csproj file not found.
    echo  Expected: !CSPROJ!
    echo  Contents of script folder:
    dir "!SCRIPT_DIR!" /b 2>&1
    goto :fail
)
echo  [OK] .csproj found.

echo  Patching GameDir in .csproj...
powershell -ExecutionPolicy Bypass -Command ^
  "$f='!CSPROJ!'; $c=(Get-Content $f -Raw) -replace '<GameDir>[^<]*</GameDir>','<GameDir>!GAME_DIR!</GameDir>'; Set-Content $f $c; Write-Host 'Patch done'"
echo  Patch exit code: !errorlevel!
if !errorlevel! neq 0 (
    echo  [ERROR - STEP 4] Failed to patch .csproj
    echo  Exit code: !errorlevel!
    goto :fail
)

echo  Running dotnet restore...
"!DOTNET_EXE!" restore "!CSPROJ!" 2>&1
echo  restore exit code: !errorlevel!
if !errorlevel! neq 0 (
    echo  [ERROR - STEP 4] dotnet restore failed.
    echo  Exit code: !errorlevel!
    goto :fail
)
echo  [OK] Restore done.

echo  Running dotnet build...
"!DOTNET_EXE!" build "!CSPROJ!" --configuration Release --no-restore 2>&1
echo  build exit code: !errorlevel!
if !errorlevel! neq 0 (
    echo  [ERROR - STEP 4] dotnet build failed.
    echo  Exit code: !errorlevel!
    echo  Scroll up for compiler errors.
    goto :fail
)
echo  [OK] Build done.

echo  Searching for output DLL in !SCRIPT_DIR!bin\Release\ ...
echo  Searching for compiled DLL...
:: Write a simple PS1 to a temp file so we avoid ALL quoting issues in the bat
set "FIND_PS=%TEMP%\mhz_find_dll.ps1"
(
    echo $releaseDir = Join-Path $env:SEARCH_BASE "bin\Release"
    echo $dll = Get-ChildItem -LiteralPath $releaseDir -Recurse -Filter "MHZombieMultiplayer.dll" -ErrorAction SilentlyContinue ^| Select-Object -First 1
    echo if ($dll^) { $dll.FullName ^| Out-File -FilePath "$env:TEMP\mhz_dll_path.txt" -Encoding ASCII -NoNewline }
) > "!FIND_PS!"
set "SEARCH_BASE=!SCRIPT_DIR!"
powershell -ExecutionPolicy Bypass -File "!FIND_PS!"
set /p MOD_DLL=<"%TEMP%\mhz_dll_path.txt"
del "%TEMP%\mhz_dll_path.txt" >nul 2>&1
del "!FIND_PS!" >nul 2>&1

if not defined MOD_DLL (
    echo  [ERROR - STEP 4] DLL not found after build.
    echo  Searched in: !SCRIPT_DIR!bin\Release    dir "!SCRIPT_DIR!bin\Release" /s /b 2>&1
    goto :fail
)
echo  [OK] DLL = !MOD_DLL!

:: ── Step 5: BepInEx ──────────────────────────────────────────────────────
echo.
echo [5/6] Installing BepInEx...

if exist "!BEPINEX_CORE!" (
    echo  [OK] BepInEx already installed, skipping.
    goto :bepinex_done
)

echo  Downloading BepInEx...
curl -L --progress-bar -o "%BEPINEX_ZIP%" "!BEPINEX_URL!"
echo  curl exit code: !errorlevel!
if !errorlevel! neq 0 (
    echo  [ERROR - STEP 5] Failed to download BepInEx.
    goto :fail
)

echo  Extracting to !GAME_DIR! ...
powershell -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%BEPINEX_ZIP%' -DestinationPath '!GAME_DIR!' -Force; Write-Host 'Extract done'"
echo  Extract exit code: !errorlevel!
if !errorlevel! neq 0 (
    echo  [ERROR - STEP 5] Extraction failed.
    goto :fail
)
del "%BEPINEX_ZIP%" >nul 2>&1
echo  [OK] BepInEx extracted.

:bepinex_done

:: ── Step 6: Copy DLL and verify ──────────────────────────────────────────
echo.
echo [6/6] Copying mod DLL...

if not exist "!PLUGINS_DIR!" (
    echo  Creating plugins folder: !PLUGINS_DIR!
    mkdir "!PLUGINS_DIR!"
    echo  mkdir exit code: !errorlevel!
    if !errorlevel! neq 0 (
        echo  [ERROR - STEP 6] Could not create plugins folder.
        goto :fail
    )
)

echo  Copying DLL via PowerShell...
set "COPY_PS=%TEMP%\mhz_copy_dll.ps1"
(
    echo $src = $env:MOD_DLL
    echo $dst = Join-Path $env:PLUGINS_DIR "MHZombieMultiplayer.dll"
    echo Copy-Item -LiteralPath $src -Destination $dst -Force
    echo Write-Host "Copy done: $dst"
) > "!COPY_PS!"
set "MOD_DLL=!MOD_DLL!"
set "PLUGINS_DIR=!PLUGINS_DIR!"
powershell -ExecutionPolicy Bypass -File "!COPY_PS!"
echo  copy exit code: !errorlevel!
del "!COPY_PS!" >nul 2>&1
if !errorlevel! neq 0 (
    echo  [ERROR - STEP 6] Copy failed.
    echo  Try running as Administrator.
    goto :fail
)

echo.
echo  ---- FINAL VERIFICATION ----
if exist "!GAME_DIR!\winhttp.dll"              (echo  [OK]      winhttp.dll) else (echo  [MISSING] winhttp.dll)
if exist "!BEPINEX_CORE!"                      (echo  [OK]      BepInEx core) else (echo  [MISSING] BepInEx core)
if exist "!PLUGINS_DIR!\MHZombieMultiplayer.dll" (echo  [OK]      mod DLL) else (echo  [MISSING] mod DLL)

if not exist "!PLUGINS_DIR!\MHZombieMultiplayer.dll" (
    echo  [ERROR - STEP 6] Mod DLL missing after copy.
    goto :fail
)

:: ── Done ─────────────────────────────────────────────────────────────────
echo.
echo  =====================================================
echo    Installation complete!
echo  =====================================================
echo.
echo   Launch MH-Zombie from Steam. Once in a level:
echo     F8  = Open multiplayer panel
echo     F9  = Host a lobby
echo     F10 = Leave lobby
echo.
echo  Type EXIT to close this window.
goto :eof

:fail
echo.
echo  =====================================================
echo    FAILED - copy everything above and share for help
echo  =====================================================
echo.
echo  Type EXIT to close this window.
goto :eof
