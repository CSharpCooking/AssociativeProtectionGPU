@echo off
rem Revision measurements on configuration A (laptop with the integrated and the discrete GPU).
rem Two ways (see README.md): put this file into the folder of the benchmark project of the thesis (the folder with its
rem .csproj) after adding the .cs files of measurement/laptop and the RevEntry line to Program.Main; or run it where it
rem lies, in measurement/laptop of the repository, and it uses the standalone project in measurement/laptop/standalone.
rem
rem   run_revision.cmd <novel text file> <data folder of the repository> [group]
rem
rem group: all (default) or one of env, breakeven, fullcycle, formation, chunksize, recovery, recognition, kernels.
rem Results: folder results_revision next to this file; send it as a zip archive.
setlocal
if "%~2"=="" goto :usage
set "ASSOC_INPUT=%~f1"
set "ASSOC_DATA=%~f2"
set "GROUP=%~3"
if "%GROUP%"=="" set "GROUP=all"
set "OUT=%~dp0results_revision"
if not exist "%ASSOC_INPUT%" goto :notext
if not exist "%ASSOC_DATA%\uniform_10MB.bin" goto :nodata
echo  all env breakeven fullcycle formation chunksize recovery recognition kernels | findstr /i /c:" %GROUP%" >nul || goto :usage
if not exist "%OUT%" mkdir "%OUT%"
set "PROJDIR=%~dp0"
if not exist "%~dp0*.csproj" if exist "%~dp0standalone\AssocRevision.csproj" set "PROJDIR=%~dp0standalone\"
cd /d "%PROJDIR%"

echo [%time%] build
dotnet build -c Release -nologo > "%OUT%\build.log" 2>&1
if errorlevel 1 goto :buildfailed
if /i "%GROUP%"=="all" goto :env
goto :%GROUP%

:env
echo [%time%] env (under 1 min)
dotnet run -c Release --no-build -- rev-env > "%OUT%\env.txt" 2>&1
dotnet --info >> "%OUT%\env.txt" 2>&1
nvidia-smi --query-gpu=name,driver_version,pcie.link.gen.max,pcie.link.width.max,memory.total --format=csv >> "%OUT%\env.txt" 2>&1
powershell -NoProfile -Command "$o=Get-CimInstance Win32_OperatingSystem; 'OS: ' + $o.Caption + ' ' + $o.Version + ' build ' + $o.BuildNumber; 'CPU: ' + (Get-CimInstance Win32_Processor).Name; 'RAM, GB: ' + [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1); 'Model: ' + (Get-CimInstance Win32_ComputerSystem).Model; Get-CimInstance Win32_VideoController | ForEach-Object { 'Video: ' + $_.Name + ', driver ' + $_.DriverVersion }; Get-CimInstance Win32_Battery | ForEach-Object { 'Battery status (2 = on mains power): ' + $_.BatteryStatus }" >> "%OUT%\env.txt" 2>&1
powercfg /getactivescheme >> "%OUT%\env.txt" 2>&1
if /i "%GROUP%"=="env" goto :done

:breakeven
call :bench RevBreakEven "15-25 min"
if /i not "%GROUP%"=="all" goto :done
:fullcycle
call :bench RevFullCycle "about 8 min"
if /i not "%GROUP%"=="all" goto :done
:formation
call :bench RevFormation "about 3 min"
if /i not "%GROUP%"=="all" goto :done
:chunksize
call :bench RevChunkSize "about 17 min"
if /i not "%GROUP%"=="all" goto :done
:recovery
call :bench RevRecovery "about 5 min"
if /i not "%GROUP%"=="all" goto :done
:recognition
call :bench RevRecognition "about 13 min"
if /i not "%GROUP%"=="all" goto :done
:kernels
echo [%time%] kernels (3-5 min)
dotnet run -c Release --no-build -- rev-kernels "%ASSOC_DATA%" 10 > "%OUT%\kernels.csv" 2> "%OUT%\kernels.err"
goto :done

:bench
echo [%time%] %~1 (%~2)
dotnet run -c Release --no-build -- rev --filter *%~1* --artifacts "%OUT%\%~1" > "%OUT%\%~1.console.log" 2>&1
if errorlevel 1 echo     %~1 ended with an error, see %~1.console.log
exit /b 0

:usage
echo usage: run_revision.cmd ^<novel text file^> ^<data folder^> [all^|env^|breakeven^|fullcycle^|formation^|chunksize^|recovery^|recognition^|kernels]
exit /b 1
:notext
echo The text file was not found: "%ASSOC_INPUT%"
exit /b 1
:nodata
echo The data folder does not contain uniform_10MB.bin: "%ASSOC_DATA%"
exit /b 1
:buildfailed
echo The build failed; see "%OUT%\build.log".
exit /b 1

:done
echo [%time%] done. Results are in "%OUT%". Send this folder as a zip archive.
endlocal
