@echo off
rem CPU-side measurements in the setting of configuration C (Tables 7, 8, S2, S3) and the output of the system
rem generator for the NIST tests, under Windows. Requires the .NET 8 SDK. Results, logs and system information are
rem written to results_run\ in the repository root (excluded from version control). Total time on configuration C:
rem about 1 h. Run on an otherwise idle laptop connected to mains power, with other applications closed.
setlocal
set "HERE=%~dp0"
set "OUT=%HERE%..\results_run"
set "DATA=..\..\data"
set "APP=bin\Release\net8.0\AssocMeasure.dll"
if not exist "%OUT%" mkdir "%OUT%"
cd /d "%HERE%AssocMeasure" || goto :fail
if not exist "%DATA%\en_10MB.bin" (
  echo The inputs were not found in the folder data of the repository.
  goto :fail
)

echo [%time%] build
dotnet build -c Release -nologo > "%OUT%\build.log" 2>&1 || goto :fail

echo [%time%] environment
dotnet "%APP%" env > "%OUT%\env.txt" 2>&1 || goto :fail
dotnet --info >> "%OUT%\env.txt" 2>&1
powershell -NoProfile -Command "(Get-CimInstance Win32_Processor).Name; (Get-CimInstance Win32_ComputerSystem).Model; [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1)" >> "%OUT%\env.txt" 2>&1
powercfg /getactivescheme >> "%OUT%\env.txt" 2>&1

echo [%time%] form: formation variants, English text 10 MB
dotnet "%APP%" form "%DATA%\en_10MB.bin" 10 2 "%OUT%\form_en_10MB.json" > "%OUT%\form.log" 2>&1 || goto :fail

echo [%time%] rec: recognition variants, six inputs of 10 MB
for %%f in (uniform en ru zip zero ff) do (
  echo     rec %%f
  dotnet "%APP%" rec "%DATA%\%%f_10MB.bin" 10 2 "%OUT%\rec_%%f_10MB.json" >> "%OUT%\rec.log" 2>&1 || goto :fail
)

echo [%time%] dist: 16 keys x 6 inputs
dotnet "%APP%" dist 16 2 0 "%OUT%\dist_10MB.json" "%DATA%\uniform_10MB.bin" "%DATA%\en_10MB.bin" "%DATA%\ru_10MB.bin" "%DATA%\zip_10MB.bin" "%DATA%\zero_10MB.bin" "%DATA%\ff_10MB.bin" > "%OUT%\dist.log" 2>&1 || goto :fail

echo [%time%] filler: output of the Windows system generator for NIST SP 800-22
dotnet "%APP%" filler "%OUT%\fillers" 12500000 > "%OUT%\filler.log" 2>&1 || goto :fail

echo [%time%] done: results in "%OUT%"
pause
exit /b 0

:fail
echo.
echo FAILED. See the log files in "%OUT%".
pause
exit /b 1
