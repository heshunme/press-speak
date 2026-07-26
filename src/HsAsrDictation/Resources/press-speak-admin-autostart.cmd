@echo off
setlocal DisableDelayedExpansion

set "PATH=%~2;%~3"
set "ComSpec=%~2\cmd.exe"
set "SystemRoot=%~3"
set "windir=%~3"
set "DOTNET_ROOT="
set "DOTNET_ROOT_X64="
set "DOTNET_ROOT_X86="
set "DOTNET_ROOT_ARM64="
set "DOTNET_ROOT_ARM32="
set "DOTNET_ROOT(x86)="
set "DOTNET_STARTUP_HOOKS="
set "DOTNET_ADDITIONAL_DEPS="
set "DOTNET_SHARED_STORE="
set "DOTNET_BUNDLE_EXTRACT_BASE_DIR="
set "DOTNET_GCName="
set "DOTNET_GCPath="
set "COMPlus_GCName="
set "COMPlus_GCPath="
set "DOTNET_AltJit="
set "DOTNET_AltJitName="
set "DOTNET_JitName="
set "COMPlus_AltJit="
set "COMPlus_AltJitName="
set "COMPlus_JitName="
set "DOTNET_HOST_TRACE="
set "DOTNET_HOST_TRACEFILE="
set "COREHOST_TRACE="
set "COREHOST_TRACEFILE="
set "DOTNET_ENABLE_PROFILING=0"
set "CORECLR_ENABLE_PROFILING=0"
set "COR_ENABLE_PROFILING=0"
set "DOTNET_PROFILER="
set "CORECLR_PROFILER="
set "COR_PROFILER="
set "DOTNET_PROFILER_PATH="
set "DOTNET_PROFILER_PATH_32="
set "DOTNET_PROFILER_PATH_64="
set "DOTNET_PROFILER_PATH_ARM32="
set "DOTNET_PROFILER_PATH_ARM64="
set "CORECLR_PROFILER_PATH="
set "CORECLR_PROFILER_PATH_32="
set "CORECLR_PROFILER_PATH_64="
set "CORECLR_PROFILER_PATH_ARM32="
set "CORECLR_PROFILER_PATH_ARM64="
set "COR_PROFILER_PATH="
set "DOTNET_ENABLE_NOTIFICATION_PROFILERS=0"
set "CORECLR_ENABLE_NOTIFICATION_PROFILERS=0"
set "DOTNET_NOTIFICATION_PROFILERS="
set "DOTNET_NOTIFICATION_PROFILERS_32="
set "DOTNET_NOTIFICATION_PROFILERS_64="
set "DOTNET_NOTIFICATION_PROFILERS_ARM32="
set "DOTNET_NOTIFICATION_PROFILERS_ARM64="
set "CORECLR_NOTIFICATION_PROFILERS="
set "CORECLR_NOTIFICATION_PROFILERS_32="
set "CORECLR_NOTIFICATION_PROFILERS_64="
set "CORECLR_NOTIFICATION_PROFILERS_ARM32="
set "CORECLR_NOTIFICATION_PROFILERS_ARM64="
set "DOTNET_EnableDiagnostics=0"
set "DOTNET_EnableDiagnostics_IPC=0"
set "DOTNET_EnableDiagnostics_Debugger=0"
set "DOTNET_EnableDiagnostics_Profiler=0"
set "DOTNET_DiagnosticPorts="
set "DOTNET_DefaultDiagnosticPortSuspend=0"
set "COMPlus_EnableDiagnostics=0"

if /I "%~4"=="autostart" (
    start "" /B "%~1" --autostart
    if errorlevel 1 exit /b 6
    exit /b 0
)

if /I "%~4"=="administrator" (
    start "" /B /WAIT "%~1" --elevation-origin-check "--elevation-origin-user-sid=%~5"
    if errorlevel 1 exit /b 5
    start "" /B "%~1" --admin --elevation-applied "--elevation-origin-user-sid=%~5"
    if errorlevel 1 exit /b 6
    exit /b 0
)

if /I "%~4"=="maintenance-create" (
    "%~1" --startup-task-maintenance=create "--startup-task-user-sid=%~5"
    exit /b
)

if /I "%~4"=="maintenance-delete" (
    "%~1" --startup-task-maintenance=delete "--startup-task-user-sid=%~5"
    exit /b
)

exit /b 87
