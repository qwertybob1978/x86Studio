@echo off
setlocal

if "%~1"=="" (
    echo Usage: build.bat ^<project directory^>
    exit /b 2
)

for %%I in ("%~1") do set "PROJECT=%%~fI"
set "REPOSITORY_ROOT=%~dp0..\..\.."
for %%I in ("%REPOSITORY_ROOT%") do set "REPOSITORY_ROOT=%%~fI"
set "KERNEL=%REPOSITORY_ROOT%\FreeRTOS-LTS\FreeRTOS\FreeRTOS-Kernel"
set "LVGL=%~dp0third_party\lvgl-v8.3"
set "OUTPUT=%PROJECT%\build"
set "SMOKE_DEFINE="
if /I "%~2"=="SMOKE" set "SMOKE_DEFINE=-dFREERTOSX86_SMOKE=1"
set "USER_APP_DEFINE="
if exist "%PROJECT%\user_sources.txt" set "USER_APP_DEFINE=-dFREERTOSX86_USER_APP=1"

if not defined WATCOM set "WATCOM=C:\WATCOM"
if not exist "%WATCOM%\binnt64\wcl.exe" (
    echo Open Watcom 16-bit compiler not found at "%WATCOM%\binnt64\wcl.exe".
    exit /b 2
)
if not exist "%KERNEL%\tasks.c" (
    echo FreeRTOS kernel not found at "%KERNEL%".
    echo Place FreeRTOS-LTS next to the dosrtos repository.
    exit /b 2
)
if not exist "%LVGL%\lvgl.h" (
    echo Bundled LVGL v8.3 source is missing from "%LVGL%".
    exit /b 2
)
if not exist "%PROJECT%\generated\gui_task.c" (
    echo Generate and save the project before building.
    exit /b 2
)
if not exist "%PROJECT%\generated\user_events.c" (
    echo The generated user_events.c file is missing.
    exit /b 2
)
if not exist "%PROJECT%\generated\lvgl_widget_config.h" (
    echo Generate and save the project to create its LVGL widget configuration.
    exit /b 2
)
if not exist "%PROJECT%\generated\lvgl_widget_sources.txt" (
    echo Generate and save the project to create its LVGL source manifest.
    exit /b 2
)
if not exist "%OUTPUT%" mkdir "%OUTPUT%"
if errorlevel 1 exit /b 2

call "%WATCOM%\owsetenv.bat" >nul
if errorlevel 1 exit /b 2

pushd "%OUTPUT%"
copy /y "%PROJECT%\generated\gui_task.c" "%OUTPUT%\gui_task.c" >nul
if errorlevel 1 (
    popd
    exit /b 2
)
copy /y "%PROJECT%\generated\gui_task.h" "%OUTPUT%\gui_task.h" >nul
if errorlevel 1 (
    popd
    exit /b 2
)
copy /y "%PROJECT%\generated\user_events.c" "%OUTPUT%\user_events.c" >nul
if errorlevel 1 (
    popd
    exit /b 2
)
copy /y "%PROJECT%\generated\user_events.h" "%OUTPUT%\user_events.h" >nul
if errorlevel 1 (
    popd
    exit /b 2
)
copy /y "%PROJECT%\generated\lvgl_widget_config.h" "%OUTPUT%\lvgl_widget_config.h" >nul
if errorlevel 1 (
    popd
    exit /b 2
)
copy /y "%PROJECT%\generated\lvgl_widget_sources.txt" "%OUTPUT%\lvgl_widget_sources.txt" >nul
if errorlevel 1 (
    popd
    exit /b 2
)
wasm -q -0 -fo=hgc.obj "%~dp0hgc.asm"
if errorlevel 1 (
    popd
    exit /b 1
)
if exist lvgl_objects.rsp del /q lvgl_objects.rsp
if exist lvgl_sources.rsp del /q lvgl_sources.rsp
if exist user_objects.rsp del /q user_objects.rsp
type nul > lvgl_objects.rsp
type nul > user_objects.rsp
for /R "%LVGL%\src\core" %%F in (*.c) do (
    call :CompileLvgl "%%~fF"
    if errorlevel 1 goto :BuildFailed
)
for /R "%LVGL%\src\hal" %%F in (*.c) do (
    call :CompileLvgl "%%~fF"
    if errorlevel 1 goto :BuildFailed
)
for /R "%LVGL%\src\draw" %%F in (*.c) do (
    call :CompileLvgl "%%~fF"
    if errorlevel 1 goto :BuildFailed
)
for /R "%LVGL%\src\font" %%F in (*.c) do (
    call :CompileLvgl "%%~fF"
    if errorlevel 1 goto :BuildFailed
)
for /R "%LVGL%\src\misc" %%F in (*.c) do (
    if /I not "%%~nxF"=="lv_tlsf.c" (
        call :CompileLvgl "%%~fF"
        if errorlevel 1 goto :BuildFailed
    )
)
for /f "usebackq delims=" %%F in ("lvgl_widget_sources.txt") do (
    call :CompileLvgl "%LVGL%\src\%%F"
    if errorlevel 1 goto :BuildFailed
)
call :CompileLvgl "%LVGL%\src\extra\lv_extra.c"
if errorlevel 1 goto :BuildFailed
if exist "%PROJECT%\user_sources.txt" (
    for /f "usebackq eol=# delims=" %%S in ("%PROJECT%\user_sources.txt") do (
        if not exist "%PROJECT%\%%S" (
            echo Project source "%PROJECT%\%%S" was not found.
            goto :BuildFailed
        )
        call :CompileUser "%PROJECT%\%%S"
        if errorlevel 1 goto :BuildFailed
    )
)
rem Keep string literals in CODE to reduce pressure on the 8088 DGROUP.
wcl -q -za99 -bt=dos -0 -ml -os -s -zc -k4096 %SMOKE_DEFINE% %USER_APP_DEFINE% ^
    -dLV_CONF_INCLUDE_SIMPLE -fi=%~dp0FreeRTOSConfig.h ^
    -i=%OUTPUT% -i=%~dp0lvgl -i=%~dp0 -i=%LVGL% -i=%LVGL%\src -i=%KERNEL%\include ^
    -i="%PROJECT%\include" -i="%PROJECT%\src" ^
    -i=%KERNEL%\portable\oWatcom\16BitDOS\PC ^
    -i=%KERNEL%\portable\oWatcom\16BitDOS\common ^
    -fe=FRTOSGUI.EXE ^
    "%~dp0main.c" "%~dp0gui.c" gui_task.c user_events.c hgc.obj ^
    "%~dp0dos_agent.c" ^
    "%KERNEL%\list.c" "%KERNEL%\queue.c" "%KERNEL%\tasks.c" ^
    "%KERNEL%\portable\oWatcom\16BitDOS\PC\port.c" ^
    "%KERNEL%\portable\oWatcom\16BitDOS\common\portcomn.c" ^
    @lvgl_objects.rsp @user_objects.rsp
if errorlevel 1 (
    popd
    exit /b 1
)
popd

echo Built "%OUTPUT%\FRTOSGUI.EXE" for 8086-compatible DOS.
exit /b 0

:BuildFailed
popd
exit /b 1

:CompileLvgl
for %%I in ("%~1") do set "LVGL_OBJECT=lvgl_%%~nI.obj"
wcc -q -za99 -bt=dos -0 -ml -os -s -zc %SMOKE_DEFINE% ^
    -dLV_CONF_INCLUDE_SIMPLE -fi=%~dp0FreeRTOSConfig.h ^
    -i=%OUTPUT% -i=%~dp0lvgl -i=%~dp0 -i=%LVGL% -i=%LVGL%\src -i=%KERNEL%\include ^
    -i=%KERNEL%\portable\oWatcom\16BitDOS\PC ^
    -i=%KERNEL%\portable\oWatcom\16BitDOS\common ^
    -fo=%LVGL_OBJECT% "%~1"
if errorlevel 1 exit /b 1
echo file %LVGL_OBJECT%>>lvgl_objects.rsp
exit /b 0

:CompileUser
for %%I in ("%~1") do set "USER_OBJECT=user_%%~nI.obj"
wcc -q -za99 -bt=dos -0 -ml -os -s -zc %SMOKE_DEFINE% %USER_APP_DEFINE% ^
    -dLV_CONF_INCLUDE_SIMPLE -fi=%~dp0FreeRTOSConfig.h ^
    -i=%OUTPUT% -i=%~dp0lvgl -i=%~dp0 -i=%LVGL% -i=%LVGL%\src -i=%KERNEL%\include ^
    -i="%PROJECT%\include" -i="%PROJECT%\src" ^
    -i=%KERNEL%\portable\oWatcom\16BitDOS\PC ^
    -i=%KERNEL%\portable\oWatcom\16BitDOS\common ^
    -fo=%USER_OBJECT% "%~1"
if errorlevel 1 exit /b 1
echo file %USER_OBJECT%>>user_objects.rsp
exit /b 0
