namespace HsAsrDictation.Services;

public enum StartupRegistrationMode
{
    Disabled,
    Standard,
    Administrator
}

public sealed class StartupRegistrationState
{
    public required StartupRegistrationMode Mode { get; init; }

    public bool RequiresRepair { get; init; }

    public string? Message { get; init; }
}

public enum StartupTaskQueryStatus
{
    Found,
    NotFound,
    Failed
}

public sealed class StartupTaskQueryResult
{
    public StartupTaskQueryStatus Status { get; init; }

    public string? ExecutablePath { get; init; }

    public string? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public int ActionCount { get; init; }

    public int TriggerCount { get; init; }

    public string? PrincipalUserId { get; init; }

    public string? LogonTriggerUserId { get; init; }

    public bool? LogonTriggerEnabled { get; init; }

    public string? LogonType { get; init; }

    public string? RunLevel { get; init; }

    public string? MultipleInstancesPolicy { get; init; }

    public string? ExecutionTimeLimit { get; init; }

    public bool? TaskEnabled { get; init; }

    public bool? DisallowStartIfOnBatteries { get; init; }

    public bool? StopIfGoingOnBatteries { get; init; }

    public bool? SecurityDescriptorIsProtected { get; init; }

    public string? Message { get; init; }

    public bool WasFound => Status == StartupTaskQueryStatus.Found;

    public bool WasNotFound => Status == StartupTaskQueryStatus.NotFound;

    public bool WasFailed => Status == StartupTaskQueryStatus.Failed;

    public static StartupTaskQueryResult Found(
        string? executablePath,
        string? arguments,
        string? workingDirectory = null,
        int actionCount = 0,
        int triggerCount = 0,
        string? principalUserId = null,
        string? logonTriggerUserId = null,
        bool? logonTriggerEnabled = null,
        string? logonType = null,
        string? runLevel = null,
        string? multipleInstancesPolicy = null,
        string? executionTimeLimit = null,
        bool? taskEnabled = null,
        bool? disallowStartIfOnBatteries = null,
        bool? stopIfGoingOnBatteries = null,
        bool? securityDescriptorIsProtected = null,
        string? message = null) => new()
    {
        Status = StartupTaskQueryStatus.Found,
        ExecutablePath = executablePath,
        Arguments = arguments,
        WorkingDirectory = workingDirectory,
        ActionCount = actionCount,
        TriggerCount = triggerCount,
        PrincipalUserId = principalUserId,
        LogonTriggerUserId = logonTriggerUserId,
        LogonTriggerEnabled = logonTriggerEnabled,
        LogonType = logonType,
        RunLevel = runLevel,
        MultipleInstancesPolicy = multipleInstancesPolicy,
        ExecutionTimeLimit = executionTimeLimit,
        TaskEnabled = taskEnabled,
        DisallowStartIfOnBatteries = disallowStartIfOnBatteries,
        StopIfGoingOnBatteries = stopIfGoingOnBatteries,
        SecurityDescriptorIsProtected = securityDescriptorIsProtected,
        Message = message
    };

    public static StartupTaskQueryResult NotFound() => new()
    {
        Status = StartupTaskQueryStatus.NotFound
    };

    public static StartupTaskQueryResult Failed(string message) => new()
    {
        Status = StartupTaskQueryStatus.Failed,
        Message = message
    };
}

public enum StartupRegistrationMaintenanceStatus
{
    Succeeded,
    Canceled,
    Failed
}

public sealed class StartupRegistrationMaintenanceResult
{
    public StartupRegistrationMaintenanceStatus Status { get; init; }

    public string? Message { get; init; }

    public bool WasSuccessful => Status == StartupRegistrationMaintenanceStatus.Succeeded;

    public bool WasCanceled => Status == StartupRegistrationMaintenanceStatus.Canceled;

    public bool WasFailed => Status == StartupRegistrationMaintenanceStatus.Failed;

    public static StartupRegistrationMaintenanceResult Succeeded() => new()
    {
        Status = StartupRegistrationMaintenanceStatus.Succeeded
    };

    public static StartupRegistrationMaintenanceResult Canceled(string message) => new()
    {
        Status = StartupRegistrationMaintenanceStatus.Canceled,
        Message = message
    };

    public static StartupRegistrationMaintenanceResult Failed(string message) => new()
    {
        Status = StartupRegistrationMaintenanceStatus.Failed,
        Message = message
    };
}

public enum StartupRegistrationChangeStatus
{
    Succeeded,
    Canceled,
    Failed
}

public sealed class StartupRegistrationChangeResult
{
    public StartupRegistrationChangeStatus Status { get; init; }

    public string? Message { get; init; }

    public bool WasSuccessful => Status == StartupRegistrationChangeStatus.Succeeded;

    public bool WasCanceled => Status == StartupRegistrationChangeStatus.Canceled;

    public bool WasFailed => Status == StartupRegistrationChangeStatus.Failed;

    public static StartupRegistrationChangeResult Succeeded() => new()
    {
        Status = StartupRegistrationChangeStatus.Succeeded
    };

    public static StartupRegistrationChangeResult Canceled(string message) => new()
    {
        Status = StartupRegistrationChangeStatus.Canceled,
        Message = message
    };

    public static StartupRegistrationChangeResult Failed(string message) => new()
    {
        Status = StartupRegistrationChangeStatus.Failed,
        Message = message
    };
}

public interface IStartupRegistrationPlatform
{
    string ExecutablePath { get; }

    string CommandInterpreterPath { get; }

    string UserSid { get; }

    string? GetRunCommand();

    void SetRunCommand(string command);

    void DeleteRunCommand();

    StartupTaskQueryResult QueryAdministratorTask();

    StartupRegistrationChangeResult ValidateAdministratorRegistration();

    StartupRegistrationMaintenanceResult RequestMaintenance(StartupTaskMaintenanceAction action);

    StartupRegistrationMaintenanceResult ExecuteMaintenance(StartupTaskMaintenanceAction action);
}

public interface IStartupRegistrationService
{
    StartupRegistrationState GetState();

    StartupRegistrationMode GetMode();

    StartupRegistrationChangeResult SetMode(StartupRegistrationMode mode);
}
