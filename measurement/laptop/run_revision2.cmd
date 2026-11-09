@echo off
rem Revision 2 measurements on configuration D of the paper (second report of reviewer 1); see RUN_INSTRUCTIONS_R2_RU.md.
rem Run it where it lies, in measurement\laptop of the repository; it builds the standalone project in standalone\.
rem
rem   run_revision2.cmd <text file> <data folder of the repository> [group]
rem
rem group: all (default) or one of env, breakeven, formation, recovery, fullcycle, fullcycleseq, kernels, ncu
rem        ("all" runs everything except ncu, which needs access to the GPU performance counters).
rem The High performance power plan of Windows is activated for the whole session, so that the BenchmarkDotNet runs, which
rem select this plan themselves, and the kernel-only times are measured under the same plan; the previous plan is restored at
rem the end. The clock rates, power and temperature of the discrete GPU are logged every 500 ms in gpu_clocks.csv.
rem Results: folder results_revision2 next to this file; send it as a zip archive.
setlocal
if "%~2"=="" goto :usage
set "ASSOC_INPUT=%~f1"
set "ASSOC_DATA=%~f2"
set "GROUP=%~3"
if "%GROUP%"=="" set "GROUP=all"
set "OUT=%~dp0results_revision2"
if not exist "%ASSOC_INPUT%" goto :notext
if not exist "%ASSOC_DATA%\uniform_10MB.bin" goto :nodata
echo  all env breakeven formation recovery fullcycle fullcycleseq kernels ncu | findstr /i /c:" %GROUP%" >nul || goto :usage
if not exist "%OUT%" mkdir "%OUT%"
cd /d "%~dp0standalone"

echo [%time%] build
dotnet build -c Release -nologo > "%OUT%\build.log" 2>&1
if errorlevel 1 goto :buildfailed

rem ---- one power plan for all runs, and the GPU clock log
for /f "tokens=4" %%G in ('powercfg /getactivescheme') do set "PREVPLAN=%%G"
powercfg /setactive SCHEME_MIN
echo previous plan: %PREVPLAN% >> "%OUT%\power_plan.txt"
powercfg /getactivescheme >> "%OUT%\power_plan.txt"
start "gpu clocks" /B nvidia-smi --query-gpu=timestamp,name,pstate,clocks.gr,clocks.sm,clocks.mem,power.draw,temperature.gpu,utilization.gpu --format=csv -lms 500 -f "%OUT%\gpu_clocks_%RANDOM%.csv"

if /i "%GROUP%"=="all" goto :env
goto :%GROUP%

:env
echo [%time%] env (under 1 min)
dotnet run -c Release --no-build -- rev-env > "%OUT%\env.txt" 2>&1
nvidia-smi --query-gpu=name,driver_version,clocks.max.sm,clocks.max.mem,power.limit,pcie.link.gen.max,pcie.link.width.max,memory.total --format=csv >> "%OUT%\env.txt" 2>&1
powershell -NoProfile -Command "$o=Get-CimInstance Win32_OperatingSystem; 'OS: ' + $o.Caption + ' build ' + $o.BuildNumber; 'CPU: ' + (Get-CimInstance Win32_Processor).Name; Get-CimInstance Win32_Battery | ForEach-Object { 'Battery status (2 = on mains power): ' + $_.BatteryStatus }" >> "%OUT%\env.txt" 2>&1
if /i "%GROUP%"=="env" goto :done

:breakeven
call :bench "Rev2BreakEven" "*Rev2BreakEven*" "50-70 min"
if /i not "%GROUP%"=="all" goto :done
:formation
call :bench "Rev2Formation" "*Rev2Formation*" "about 10 min"
if /i not "%GROUP%"=="all" goto :done
:recovery
call :bench "Rev2Recovery" "*Rev2Recovery*" "about 12 min"
if /i not "%GROUP%"=="all" goto :done
:fullcycle
call :bench "Rev2FullCycle" "*Rev2FullCycle.FullCycle*" "about 30 min"
if /i not "%GROUP%"=="all" goto :done
:fullcycleseq
call :bench "Rev2FullCycleSeq" "*Rev2FullCycleSeq*" "about 8 min"
if /i not "%GROUP%"=="all" goto :done
:kernels
echo [%time%] kernels with group sizes 0-1024 (10-15 min)
dotnet run -c Release --no-build -- rev-kernels2 "%ASSOC_DATA%" 10 > "%OUT%\kernels2.csv" 2> "%OUT%\kernels2.err"
goto :done

:ncu
rem Nsight Compute: counters of the four kernels (formation, search, table-driven, branch-free) on the discrete GPU for the
rem uniform and the all-255 inputs; needs ncu in PATH and access to the GPU performance counters (see the instructions).
where ncu >nul 2>&1 || (echo ncu was not found in PATH & goto :done)
for %%I in (uniform ff) do (
  echo [%time%] ncu, input %%I
  ncu --target-processes all --set full --force-overwrite --export "%OUT%\ncu_dGPU_%%I" dotnet bin\Release\net8.0\AssocRevision.dll rev-kernels "%ASSOC_DATA%" 1 dGPU %%I > "%OUT%\ncu_dGPU_%%I.log" 2>&1
  ncu --import "%OUT%\ncu_dGPU_%%I.ncu-rep" --page details --csv > "%OUT%\ncu_dGPU_%%I.csv" 2>> "%OUT%\ncu_dGPU_%%I.log"
)
goto :done

:bench
echo [%time%] %~1 (%~3)
dotnet run -c Release --no-build -- rev --filter %~2 --artifacts "%OUT%\%~1" > "%OUT%\%~1.console.log" 2>&1
if errorlevel 1 echo     %~1 ended with an error, see %~1.console.log
exit /b 0

:usage
echo usage: run_revision2.cmd ^<text file^> ^<data folder^> [all^|env^|breakeven^|formation^|recovery^|fullcycle^|fullcycleseq^|kernels^|ncu]
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
taskkill /IM nvidia-smi.exe /F >nul 2>&1
if defined PREVPLAN powercfg /setactive %PREVPLAN%
echo [%time%] done. Results are in "%OUT%". Send this folder as a zip archive.
endlocal
