@echo off
rem Revision 2 measurements on configuration D of the paper (second report of reviewer 1); see RUN_INSTRUCTIONS_R2_RU.md.
rem Run it where it lies, in measurement\laptop of the repository; it builds the standalone project in standalone\.
rem
rem   run_revision2.cmd <text file> <data folder of the repository> [group]
rem
rem group: all (default) or one of probe, env, breakeven, formation, recovery, fullcycle, fullcycleseq, kernels, ncu
rem        ("all" runs env and all benchmark groups and the kernel-only times; probe and ncu are run separately).
rem The High performance power plan of Windows is activated for the whole session, so that the BenchmarkDotNet runs, which
rem select this plan themselves, and the kernel-only times are measured under the same plan; the previous plan is restored at
rem the end. The clock rates, power and temperature of the discrete GPU are logged every 500 ms (gpu_clocks_*.csv).
rem The mode of the vendor utility (Armoury Crate) cannot be read by the script: it asks for it once (or set VENDOR_MODE).
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
echo  all probe env breakeven formation recovery fullcycle fullcycleseq kernels ncu | findstr /i /c:" %GROUP%" >nul || goto :usage
if not exist "%OUT%" mkdir "%OUT%"
cd /d "%~dp0standalone"

rem ---- the vendor mode (Armoury Crate: Silent / Performance / Turbo / Manual), recorded with every group
if not defined VENDOR_MODE set /p "VENDOR_MODE=Armoury Crate mode now (Silent/Performance/Turbo/Manual, Enter = unknown): "
if not defined VENDOR_MODE set "VENDOR_MODE=unknown"
echo [%date% %time%] group %GROUP%: vendor mode %VENDOR_MODE% >> "%OUT%\vendor_mode.txt"

echo [%time%] build (the first build downloads ILGPU and BenchmarkDotNet from NuGet)
dotnet build -c Release -nologo > "%OUT%\build.log" 2>&1
if errorlevel 1 goto :buildfailed

rem ---- one power plan for all runs, and the GPU clock log
for /f "tokens=4" %%G in ('powercfg /getactivescheme') do set "PREVPLAN=%%G"
echo [%date% %time%] group %GROUP%, plan before: >> "%OUT%\power_plan.txt"
powercfg /getactivescheme >> "%OUT%\power_plan.txt"
powercfg /setactive SCHEME_MIN
echo plan during the measurements: >> "%OUT%\power_plan.txt"
powercfg /getactivescheme >> "%OUT%\power_plan.txt"
nvidia-smi -q -d CLOCK,POWER,PERFORMANCE,TEMPERATURE > "%OUT%\gpu_state_%GROUP%_start.txt" 2>&1
start "gpu clocks" /B nvidia-smi --query-gpu=timestamp,name,pstate,clocks.gr,clocks.sm,clocks.mem,power.draw,temperature.gpu,utilization.gpu --format=csv -lms 500 -f "%OUT%\gpu_clocks_%GROUP%_%RANDOM%.csv"

if /i "%GROUP%"=="all" goto :env
goto :%GROUP%

:probe
echo [%time%] probe: environment, list of the benchmarks, kernels on both GPUs (1-3 min)
dotnet run -c Release --no-build -- rev-env > "%OUT%\probe_env.txt" 2>&1
dotnet run -c Release --no-build -- rev --list flat > "%OUT%\probe_list.txt" 2>&1
dotnet run -c Release --no-build -- rev-kernels2 "%ASSOC_DATA%" 1 iGPU,dGPU ru 0 > "%OUT%\probe_kernels.csv" 2> "%OUT%\probe_kernels.err"
if errorlevel 1 (echo     the kernel check ended with an error, see probe_kernels.err) else (echo     probe passed: both GPUs work, the kernels give byte-exact results)
goto :done

:env
echo [%time%] env (under 1 min)
dotnet run -c Release --no-build -- rev-env > "%OUT%\env.txt" 2>&1
nvidia-smi --query-gpu=name,driver_version,clocks.max.sm,clocks.max.mem,power.limit,pcie.link.gen.max,pcie.link.width.max,memory.total --format=csv >> "%OUT%\env.txt" 2>&1
powershell -NoProfile -Command "$o=Get-CimInstance Win32_OperatingSystem; 'OS: ' + $o.Caption + ' build ' + $o.BuildNumber; 'CPU: ' + (Get-CimInstance Win32_Processor).Name; 'RAM, GB: ' + [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1); 'Model: ' + (Get-CimInstance Win32_ComputerSystem).Model; Get-CimInstance Win32_VideoController | ForEach-Object { 'Video: ' + $_.Name + ', driver ' + $_.DriverVersion }; Get-CimInstance Win32_Battery | ForEach-Object { 'Battery status (2 = on mains power): ' + $_.BatteryStatus }" >> "%OUT%\env.txt" 2>&1
if /i "%GROUP%"=="env" goto :done

:breakeven
call :bench "Rev2BreakEven" "*Rev2BreakEven*" "50-70 min"
if /i not "%GROUP%"=="all" goto :done
:formation
call :bench "Rev2Formation" "*Rev2Formation*" "about 15 min"
if /i not "%GROUP%"=="all" goto :done
:recovery
call :bench "Rev2Recovery" "*Rev2Recovery*" "about 20 min"
if /i not "%GROUP%"=="all" goto :done
:fullcycle
call :bench "Rev2FullCycle" "*Rev2FullCycle.FullCycle*" "about 40 min"
if /i not "%GROUP%"=="all" goto :done
:fullcycleseq
call :bench "Rev2FullCycleSeq" "*Rev2FullCycleSeq*" "about 8 min"
if /i not "%GROUP%"=="all" goto :done
:kernels
echo [%time%] kernels with group sizes 0-1024 (10-15 min)
dotnet run -c Release --no-build -- rev-kernels2 "%ASSOC_DATA%" 10 > "%OUT%\kernels2.csv" 2> "%OUT%\kernels2.err"
if errorlevel 1 echo     kernels ended with an error, see kernels2.err
goto :done

:ncu
rem Nsight Compute: counters of the four kernels (formation, search, table-driven, branch-free) on the discrete GPU for the
rem uniform and the all-255 inputs; needs Nsight Compute and access to the GPU performance counters (see the instructions).
set "NCU="
for /f "delims=" %%P in ('where ncu 2^>nul') do if not defined NCU set "NCU=%%P"
if not defined NCU for /d %%D in ("%ProgramFiles%\NVIDIA Corporation\Nsight Compute*") do (
  if exist "%%D\ncu.bat" set "NCU=%%D\ncu.bat"
  if exist "%%D\ncu.exe" set "NCU=%%D\ncu.exe"
)
if not defined NCU (echo Nsight Compute was not found: install it or add ncu to PATH & goto :done)
echo using "%NCU%" > "%OUT%\ncu_version.txt"
call "%NCU%" --version >> "%OUT%\ncu_version.txt" 2>&1
for %%I in (uniform ff) do call :ncuone %%I
goto :done

:ncuone
echo [%time%] ncu, discrete GPU, input %1 (2-5 min)
call "%NCU%" --target-processes all --set full --force-overwrite --export "%OUT%\ncu_dGPU_%1" dotnet bin\Release\net8.0\AssocRevision.dll rev-kernels "%ASSOC_DATA%" 1 dGPU %1 > "%OUT%\ncu_dGPU_%1.log" 2>&1
if not exist "%OUT%\ncu_dGPU_%1.ncu-rep" (echo     no report: see ncu_dGPU_%1.log ^(ERR_NVGPUCTRPERM means no access to the counters^) & exit /b 0)
call "%NCU%" --import "%OUT%\ncu_dGPU_%1.ncu-rep" --page details --csv > "%OUT%\ncu_dGPU_%1.csv" 2>> "%OUT%\ncu_dGPU_%1.log"
exit /b 0

:bench
echo [%time%] %~1 (%~3)
dotnet run -c Release --no-build -- rev --filter %~2 --artifacts "%OUT%\%~1" > "%OUT%\%~1.console.log" 2>&1
if errorlevel 1 echo     %~1 ended with an error, see %~1.console.log
exit /b 0

:usage
echo usage: run_revision2.cmd ^<text file^> ^<data folder^> [all^|probe^|env^|breakeven^|formation^|recovery^|fullcycle^|fullcycleseq^|kernels^|ncu]
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
nvidia-smi -q -d CLOCK,POWER,PERFORMANCE,TEMPERATURE > "%OUT%\gpu_state_%GROUP%_end.txt" 2>&1
taskkill /IM nvidia-smi.exe /F >nul 2>&1
if defined PREVPLAN powercfg /setactive %PREVPLAN%
dir /s /b "%OUT%" > "%OUT%\files.txt"
echo [%time%] done. Results are in "%OUT%". Send this folder as a zip archive.
endlocal
