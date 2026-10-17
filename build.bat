@echo off
chcp 65001 >nul
echo 正在执行 Emby.CrossLibraryCollections 本地编译...

set DOTNET_EXE=%USERPROFILE%\.dotnet\dotnet.exe
if not exist "%DOTNET_EXE%" (
    set DOTNET_EXE=dotnet
)

"%DOTNET_EXE%" build "%~dp0Emby.CrossLibraryCollections.csproj" --configuration Release
if %ERRORLEVEL% equ 0 (
    echo.
    echo =======================================================
    echo [成功] 编译完成！产物路径:
    echo %~dp0bin\Release\netstandard2.0\Emby.CrossLibraryCollections.dll
    echo =======================================================
) else (
    echo.
    echo [失败] 编译出现错误！
)
pause
