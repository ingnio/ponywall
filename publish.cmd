@echo off
REM Publishes both the UI (PonyWall.exe) and the service (PonyWallService.exe)
REM as self-contained single-file Win64 executables to publish\.
REM
REM Uses Visual Studio's framework MSBuild because PonyWall.Core has COM
REM references (NetFwTypeLib, TaskScheduler) that need the ResolveComReference
REM task, which is only available in the .NET Framework MSBuild — not in
REM dotnet build / dotnet publish.

setlocal
set OUTDIR=%~dp0publish
set MSBUILD="C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"

if not exist %MSBUILD% (
    echo MSBuild not found at %MSBUILD%
    echo Edit publish.cmd to point at your VS install location.
    exit /b 1
)

if exist "%OUTDIR%" rmdir /s /q "%OUTDIR%"
mkdir "%OUTDIR%"

echo.
echo === Publishing PonyWallService ===
%MSBUILD% PonyWallService\PonyWallService.csproj ^
    -p:Configuration=Release ^
    -p:RuntimeIdentifier=win-x64 ^
    -p:SelfContained=true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=embedded ^
    -p:PublishDir="%OUTDIR%\\" ^
    -p:_IsPublishing=true ^
    -t:Restore;Publish ^
    -v:m -nologo
if errorlevel 1 goto fail

echo.
echo === Publishing PonyWall (UI) ===
%MSBUILD% PonyWall.Avalonia\PonyWall.Avalonia.csproj ^
    -p:Configuration=Release ^
    -p:RuntimeIdentifier=win-x64 ^
    -p:SelfContained=true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=embedded ^
    -p:PublishDir="%OUTDIR%\\" ^
    -p:_IsPublishing=true ^
    -t:Restore;Publish ^
    -v:m -nologo
if errorlevel 1 goto fail

echo.
echo === Publish complete ===
echo Output: %OUTDIR%
dir "%OUTDIR%\*.exe"
goto end

:fail
echo.
echo === PUBLISH FAILED ===
exit /b 1

:end
endlocal
