using System.Xml.Linq;
using HsAsrDictation.Services;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class StartupRegistrationServiceTests
{
    private const string ExecutablePath = @"C:\Program Files\Press Speak\HsAsrDictation.exe";
    private const string OldExecutablePath = @"D:\Old Press Speak\HsAsrDictation.exe";
    private const string CommandInterpreterPath = @"C:\Windows\System32\cmd.exe";
    private const string UserSid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void GetMode_ReturnsDisabled_WhenNoCurrentRegistrationExists()
    {
        var platform = new FakeStartupRegistrationPlatform();

        var mode = new StartupRegistrationService(platform).GetMode();

        Assert.Equal(StartupRegistrationMode.Disabled, mode);
    }

    [Fact]
    public void GetState_ReportsStandardRunCommand_AndMarksOldPathForRepair()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = $"  {StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath).ToUpperInvariant()}  "
        };
        var service = new StartupRegistrationService(platform);

        Assert.Equal(StartupRegistrationMode.Standard, service.GetMode());

        platform.RunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(OldExecutablePath);
        var state = service.GetState();
        Assert.Equal(StartupRegistrationMode.Standard, state.Mode);
        Assert.True(state.RequiresRepair);
    }

    [Fact]
    public void GetMode_ReturnsAdministrator_OnlyForCurrentSecureTask()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            TaskQuery = BuildCurrentTaskQuery()
        };
        var service = new StartupRegistrationService(platform);

        Assert.Equal(StartupRegistrationMode.Administrator, service.GetMode());

        platform.TaskQuery = BuildCurrentTaskQuery(taskEnabled: false);
        var state = service.GetState();
        Assert.Equal(StartupRegistrationMode.Administrator, state.Mode);
        Assert.True(state.RequiresRepair);
    }

    [Fact]
    public void GetMode_RejectsMatchingAdministratorTask_WithWritableSecurityDescriptor()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            TaskQuery = BuildCurrentTaskQuery(securityDescriptorIsProtected: false)
        };

        var state = new StartupRegistrationService(platform).GetState();

        Assert.Equal(StartupRegistrationMode.Administrator, state.Mode);
        Assert.True(state.RequiresRepair);
        Assert.False(string.IsNullOrWhiteSpace(state.Message));
    }

    [Fact]
    public void GetState_ReportsRepair_WhenAdministratorTaskAndRunCommandCoexist()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath),
            TaskQuery = BuildCurrentTaskQuery()
        };

        var state = new StartupRegistrationService(platform).GetState();

        Assert.Equal(StartupRegistrationMode.Administrator, state.Mode);
        Assert.True(state.RequiresRepair);
        Assert.False(string.IsNullOrWhiteSpace(state.Message));
    }

    [Fact]
    public void GetState_ReportsRepair_ForRunCommandThatPointsToOldExecutable()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(OldExecutablePath)
        };

        var state = new StartupRegistrationService(platform).GetState();

        Assert.Equal(StartupRegistrationMode.Standard, state.Mode);
        Assert.True(state.RequiresRepair);
        Assert.False(string.IsNullOrWhiteSpace(state.Message));
    }

    [Fact]
    public void SetMode_Administrator_CreatesTaskBeforeDeletingRunCommand()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath)
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasSuccessful);
        Assert.Null(platform.RunCommand);
        Assert.Equal(
            ["QueryTask", "ValidateAdministrator", "Maintenance:Create", "QueryTask", "DeleteRun"],
            platform.Operations);
    }

    [Fact]
    public void SetMode_Administrator_PreservesRunCommand_WhenUacIsCanceled()
    {
        var originalRunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath);
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = originalRunCommand,
            MaintenanceResult = StartupRegistrationMaintenanceResult.Canceled("canceled")
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasCanceled);
        Assert.Equal(originalRunCommand, platform.RunCommand);
        Assert.DoesNotContain("DeleteRun", platform.Operations);
    }

    [Fact]
    public void SetMode_Administrator_PreservesRunCommand_WhenCreationFails()
    {
        var originalRunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath);
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = originalRunCommand,
            MaintenanceResult = StartupRegistrationMaintenanceResult.Failed("failed")
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasFailed);
        Assert.Equal(originalRunCommand, platform.RunCommand);
        Assert.DoesNotContain("DeleteRun", platform.Operations);
    }

    [Fact]
    public void SetMode_Administrator_DoesNotRequestElevation_WhenSecurityValidationFails()
    {
        var originalRunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath);
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = originalRunCommand,
            ValidationResult = StartupRegistrationChangeResult.Failed("unsafe path")
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasFailed);
        Assert.Equal(originalRunCommand, platform.RunCommand);
        Assert.Contains("ValidateAdministrator", platform.Operations);
        Assert.DoesNotContain("Maintenance:Create", platform.Operations);
    }

    [Fact]
    public void SetMode_Administrator_VerifiesCreatedTaskBeforeDeletingRunCommand()
    {
        var originalRunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath);
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = originalRunCommand,
            ApplySuccessfulMaintenance = false
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasFailed);
        Assert.Equal(originalRunCommand, platform.RunCommand);
        Assert.Equal(
            ["QueryTask", "ValidateAdministrator", "Maintenance:Create", "QueryTask", "Maintenance:Delete"],
            platform.Operations);
        Assert.True(platform.TaskQuery.WasNotFound);
    }

    [Fact]
    public void SetMode_Administrator_ReportsCleanupFailure_WhenCreatedTaskCannotBeVerified()
    {
        var originalRunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath);
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = originalRunCommand,
            ApplySuccessfulMaintenance = false,
            MaintenanceResults = new Queue<StartupRegistrationMaintenanceResult>([
                StartupRegistrationMaintenanceResult.Succeeded(),
                StartupRegistrationMaintenanceResult.Failed("cleanup failed")
            ])
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasFailed);
        Assert.Contains("cleanup failed", result.Message);
        Assert.Equal(originalRunCommand, platform.RunCommand);
        Assert.Equal(
            ["QueryTask", "ValidateAdministrator", "Maintenance:Create", "QueryTask", "Maintenance:Delete"],
            platform.Operations);
    }

    [Fact]
    public void SetMode_Administrator_ReplacesTaskThatPointsToOldExecutable()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            TaskQuery = BuildCurrentTaskQuery(OldExecutablePath)
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasSuccessful);
        Assert.Contains("Maintenance:Create", platform.Operations);
        Assert.True(StartupRegistrationCommandBuilder.MatchesAdministratorTask(
            platform.TaskQuery,
            ExecutablePath,
            UserSid,
            CommandInterpreterPath));
    }

    [Fact]
    public void SetMode_Administrator_ReplacesTaskWithUnprotectedSecurityDescriptor()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            TaskQuery = BuildCurrentTaskQuery(securityDescriptorIsProtected: false)
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasSuccessful);
        Assert.Contains("ValidateAdministrator", platform.Operations);
        Assert.Contains("Maintenance:Create", platform.Operations);
        Assert.True(StartupRegistrationCommandBuilder.MatchesAdministratorTask(
            platform.TaskQuery,
            ExecutablePath,
            UserSid,
            CommandInterpreterPath));
    }

    [Fact]
    public void SetMode_Administrator_DoesNotRecreateMatchingTask()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = "stale",
            TaskQuery = BuildCurrentTaskQuery()
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasSuccessful);
        Assert.Null(platform.RunCommand);
        Assert.Equal(["QueryTask", "DeleteRun"], platform.Operations);
    }

    [Fact]
    public void SetMode_Administrator_RemovesNewTask_WhenRunCommandDeletionFails()
    {
        var originalRunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath);
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = originalRunCommand,
            DeleteRunException = new InvalidOperationException("registry failed")
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasFailed);
        Assert.Contains("registry failed", result.Message);
        Assert.Equal(originalRunCommand, platform.RunCommand);
        Assert.True(platform.TaskQuery.WasNotFound);
        Assert.Equal(
            ["QueryTask", "ValidateAdministrator", "Maintenance:Create", "QueryTask", "DeleteRun", "Maintenance:Delete"],
            platform.Operations);
    }

    [Fact]
    public void SetMode_Administrator_PreservesExistingTask_WhenRunCommandDeletionFails()
    {
        var originalRunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath);
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = originalRunCommand,
            TaskQuery = BuildCurrentTaskQuery(),
            DeleteRunException = new InvalidOperationException("registry failed")
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Administrator);

        Assert.True(result.WasFailed);
        Assert.Contains("管理员开机自启仍然有效", result.Message);
        Assert.Equal(originalRunCommand, platform.RunCommand);
        Assert.True(platform.TaskQuery.WasFound);
        Assert.Equal(["QueryTask", "DeleteRun"], platform.Operations);
    }

    [Fact]
    public void SetMode_Standard_WritesRunCommandBeforeDeletingAdministratorTask()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            TaskQuery = BuildCurrentTaskQuery()
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Standard);

        Assert.True(result.WasSuccessful);
        Assert.Equal(StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath), platform.RunCommand);
        Assert.Equal(["QueryTask", "SetRun", "Maintenance:Delete"], platform.Operations);
    }

    [Fact]
    public void SetMode_Standard_RestoresRunCommand_WhenTaskDeletionIsCanceled()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            TaskQuery = BuildCurrentTaskQuery(),
            MaintenanceResult = StartupRegistrationMaintenanceResult.Canceled("canceled")
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Standard);

        Assert.True(result.WasCanceled);
        Assert.Null(platform.RunCommand);
        Assert.Equal(
            ["QueryTask", "SetRun", "Maintenance:Delete", "DeleteRun"],
            platform.Operations);
    }

    [Fact]
    public void SetMode_Standard_RestoresPreviousRunCommand_WhenTaskDeletionFails()
    {
        var previousRunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(OldExecutablePath);
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = previousRunCommand,
            TaskQuery = BuildCurrentTaskQuery(),
            MaintenanceResult = StartupRegistrationMaintenanceResult.Failed("failed")
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Standard);

        Assert.True(result.WasFailed);
        Assert.Equal(previousRunCommand, platform.RunCommand);
        Assert.Equal(
            ["QueryTask", "SetRun", "Maintenance:Delete", "SetRun"],
            platform.Operations);
    }

    [Fact]
    public void SetMode_Standard_RepairsOldRunCommand()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(OldExecutablePath)
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Standard);

        Assert.True(result.WasSuccessful);
        Assert.Equal(StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath), platform.RunCommand);
    }

    [Fact]
    public void SetMode_Disabled_DeletesTaskBeforeRunCommand()
    {
        var platform = new FakeStartupRegistrationPlatform
        {
            RunCommand = StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath),
            TaskQuery = BuildCurrentTaskQuery()
        };

        var result = new StartupRegistrationService(platform).SetMode(StartupRegistrationMode.Disabled);

        Assert.True(result.WasSuccessful);
        Assert.Null(platform.RunCommand);
        Assert.Equal(["QueryTask", "Maintenance:Delete", "DeleteRun"], platform.Operations);
    }

    [Fact]
    public void BuildRunCommand_QuotesPathWithSpaces_AndEnforcesWindowsLimit()
    {
        Assert.Equal(
            $"\"{ExecutablePath}\" --autostart",
            StartupRegistrationCommandBuilder.BuildRunCommand(ExecutablePath));

        var commandOverhead = $"\"\" {StartupRegistrationCommandBuilder.AutoStartArgument}".Length;
        var maximumPath = new string('a',
            StartupRegistrationCommandBuilder.MaxRunCommandLength - commandOverhead);
        Assert.Equal(
            StartupRegistrationCommandBuilder.MaxRunCommandLength,
            StartupRegistrationCommandBuilder.BuildRunCommand(maximumPath).Length);
        Assert.Throws<InvalidOperationException>(() =>
            StartupRegistrationCommandBuilder.BuildRunCommand(maximumPath + "a"));
    }

    [Fact]
    public void BuildScheduledTaskXml_ContainsInteractiveHighestPrivilegePersistentTask()
    {
        var taskName = StartupRegistrationCommandBuilder.BuildTaskName(UserSid);
        var xml = StartupRegistrationCommandBuilder.BuildScheduledTaskXml(
            ExecutablePath,
            UserSid,
            taskName,
            CommandInterpreterPath);
        var document = XDocument.Parse(xml);
        XNamespace taskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        var logonTrigger = document.Descendants(taskNamespace + "LogonTrigger").Single();
        var principal = document.Descendants(taskNamespace + "Principal").Single();
        var settings = document.Root!.Element(taskNamespace + "Settings")!;
        var exec = document.Descendants(taskNamespace + "Exec").Single();

        Assert.Equal(UserSid, logonTrigger.Element(taskNamespace + "UserId")?.Value);
        Assert.Equal("true", logonTrigger.Element(taskNamespace + "Enabled")?.Value);
        Assert.Equal(UserSid, principal.Element(taskNamespace + "UserId")?.Value);
        Assert.Equal("InteractiveToken", principal.Element(taskNamespace + "LogonType")?.Value);
        Assert.Equal("HighestAvailable", principal.Element(taskNamespace + "RunLevel")?.Value);
        Assert.Equal("IgnoreNew", settings.Element(taskNamespace + "MultipleInstancesPolicy")?.Value);
        Assert.Equal("false", settings.Element(taskNamespace + "DisallowStartIfOnBatteries")?.Value);
        Assert.Equal("false", settings.Element(taskNamespace + "StopIfGoingOnBatteries")?.Value);
        Assert.Equal("true", settings.Element(taskNamespace + "Enabled")?.Value);
        Assert.Equal("PT0S", settings.Element(taskNamespace + "ExecutionTimeLimit")?.Value);
        Assert.Equal(CommandInterpreterPath, exec.Element(taskNamespace + "Command")?.Value);
        Assert.Equal(
            StartupRegistrationCommandBuilder.BuildAdministratorTaskArguments(
                ExecutablePath,
                CommandInterpreterPath),
            exec.Element(taskNamespace + "Arguments")?.Value);
        Assert.Equal(@"C:\Program Files\Press Speak", exec.Element(taskNamespace + "WorkingDirectory")?.Value);
    }

    [Fact]
    public void ParseScheduledTaskXml_ProvidesFieldsUsedForCurrentTaskValidation()
    {
        var xml = StartupRegistrationCommandBuilder.BuildScheduledTaskXml(
            ExecutablePath,
            UserSid,
            StartupRegistrationCommandBuilder.BuildTaskName(UserSid),
            CommandInterpreterPath);

        var result = StartupRegistrationCommandBuilder.ParseScheduledTaskXml(
            xml,
            securityDescriptorIsProtected: true);

        Assert.True(result.WasFound);
        Assert.True(StartupRegistrationCommandBuilder.MatchesAdministratorTask(
            result,
            ExecutablePath.ToUpperInvariant(),
            UserSid,
            CommandInterpreterPath.ToUpperInvariant()));
    }

    [Fact]
    public void MatchesAdministratorTask_RejectsAdditionalActionOrTrigger()
    {
        var xml = StartupRegistrationCommandBuilder.BuildScheduledTaskXml(
            ExecutablePath,
            UserSid,
            StartupRegistrationCommandBuilder.BuildTaskName(UserSid),
            CommandInterpreterPath);
        var document = XDocument.Parse(xml);
        XNamespace taskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        document.Root!
            .Element(taskNamespace + "Actions")!
            .Add(new XElement(taskNamespace + "Exec",
                new XElement(taskNamespace + "Command", @"C:\Windows\System32\notepad.exe")));
        var extraActionResult = StartupRegistrationCommandBuilder.ParseScheduledTaskXml(
            document.ToString(SaveOptions.DisableFormatting),
            securityDescriptorIsProtected: true);

        Assert.False(StartupRegistrationCommandBuilder.MatchesAdministratorTask(
            extraActionResult,
            ExecutablePath,
            UserSid,
            CommandInterpreterPath));

        document = XDocument.Parse(xml);
        document.Root!
            .Element(taskNamespace + "Triggers")!
            .Add(new XElement(taskNamespace + "TimeTrigger",
                new XElement(taskNamespace + "StartBoundary", "2099-01-01T00:00:00")));
        var extraTriggerResult = StartupRegistrationCommandBuilder.ParseScheduledTaskXml(
            document.ToString(SaveOptions.DisableFormatting),
            securityDescriptorIsProtected: true);

        Assert.False(StartupRegistrationCommandBuilder.MatchesAdministratorTask(
            extraTriggerResult,
            ExecutablePath,
            UserSid,
            CommandInterpreterPath));
    }

    [Theory]
    [InlineData(ElevatedLaunchMode.AutoStart, "autostart")]
    [InlineData(ElevatedLaunchMode.Administrator, "administrator")]
    [InlineData(ElevatedLaunchMode.MaintenanceCreate, "maintenance-create")]
    [InlineData(ElevatedLaunchMode.MaintenanceDelete, "maintenance-delete")]
    public void BuildElevatedLauncherArgumentList_UsesProtectedAbsolutePaths(
        ElevatedLaunchMode mode,
        string expectedModeArgument)
    {
        var arguments = StartupRegistrationCommandBuilder.BuildElevatedLauncherArgumentList(
            ExecutablePath,
            CommandInterpreterPath,
            mode,
            mode is ElevatedLaunchMode.Administrator or
                ElevatedLaunchMode.MaintenanceCreate or
                ElevatedLaunchMode.MaintenanceDelete
                ? UserSid
                : null);

        Assert.Equal("/D", arguments[0]);
        Assert.Contains(
            @"C:\Program Files\Press Speak\press-speak-admin-autostart.cmd",
            arguments);
        Assert.Contains(ExecutablePath, arguments);
        Assert.Contains(@"C:\Windows\System32", arguments);
        Assert.Contains(@"C:\Windows", arguments);
        Assert.Contains(expectedModeArgument, arguments);
        Assert.Equal(
            mode == ElevatedLaunchMode.AutoStart,
            !arguments.Contains(UserSid));
    }

    [Fact]
    public void BuildElevatedLauncherArgumentList_RequiresOriginSidForAdministratorMode()
    {
        Assert.Throws<ArgumentException>(() =>
            StartupRegistrationCommandBuilder.BuildElevatedLauncherArgumentList(
                ExecutablePath,
                CommandInterpreterPath,
                ElevatedLaunchMode.Administrator));
    }

    [Theory]
    [InlineData(@"C:\%TEMP%\HsAsrDictation.exe")]
    [InlineData(@"C:\Apps\Press&Speak\HsAsrDictation.exe")]
    [InlineData(@"C:\Apps\Press!Speak\HsAsrDictation.exe")]
    [InlineData(@"C:\Apps\Press^Speak\HsAsrDictation.exe")]
    [InlineData(@"C:\Apps\Press(Speak)\HsAsrDictation.exe")]
    public void BuildAdministratorTaskArguments_RejectsCmdMetacharacters(string executablePath)
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupRegistrationCommandBuilder.BuildAdministratorTaskArguments(
                executablePath,
                CommandInterpreterPath));
    }

    private static StartupTaskQueryResult BuildCurrentTaskQuery(
        string executablePath = ExecutablePath,
        bool taskEnabled = true,
        bool securityDescriptorIsProtected = true)
    {
        var xml = StartupRegistrationCommandBuilder.BuildScheduledTaskXml(
            executablePath,
            UserSid,
            StartupRegistrationCommandBuilder.BuildTaskName(UserSid),
            CommandInterpreterPath);
        if (!taskEnabled)
        {
            var document = XDocument.Parse(xml);
            XNamespace taskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            document.Root!
                .Element(taskNamespace + "Settings")!
                .Element(taskNamespace + "Enabled")!
                .Value = "false";
            xml = document.ToString(SaveOptions.DisableFormatting);
        }

        return StartupRegistrationCommandBuilder.ParseScheduledTaskXml(
            xml,
            securityDescriptorIsProtected);
    }

    private sealed class FakeStartupRegistrationPlatform : IStartupRegistrationPlatform
    {
        public string ExecutablePath { get; init; } = StartupRegistrationServiceTests.ExecutablePath;

        public string CommandInterpreterPath { get; init; } =
            StartupRegistrationServiceTests.CommandInterpreterPath;

        public string UserSid { get; init; } = StartupRegistrationServiceTests.UserSid;

        public string? RunCommand { get; set; }

        public StartupTaskQueryResult TaskQuery { get; set; } = StartupTaskQueryResult.NotFound();

        public StartupRegistrationMaintenanceResult MaintenanceResult { get; set; } =
            StartupRegistrationMaintenanceResult.Succeeded();

        public Queue<StartupRegistrationMaintenanceResult>? MaintenanceResults { get; set; }

        public StartupRegistrationChangeResult ValidationResult { get; set; } =
            StartupRegistrationChangeResult.Succeeded();

        public bool ApplySuccessfulMaintenance { get; set; } = true;

        public Exception? DeleteRunException { get; set; }

        public List<string> Operations { get; } = [];

        public string? GetRunCommand() => RunCommand;

        public void SetRunCommand(string command)
        {
            Operations.Add("SetRun");
            RunCommand = command;
        }

        public void DeleteRunCommand()
        {
            Operations.Add("DeleteRun");
            if (DeleteRunException is not null)
            {
                throw DeleteRunException;
            }

            RunCommand = null;
        }

        public StartupTaskQueryResult QueryAdministratorTask()
        {
            Operations.Add("QueryTask");
            return TaskQuery;
        }

        public StartupRegistrationChangeResult ValidateAdministratorRegistration()
        {
            Operations.Add("ValidateAdministrator");
            return ValidationResult;
        }

        public StartupRegistrationMaintenanceResult RequestMaintenance(StartupTaskMaintenanceAction action)
        {
            Operations.Add($"Maintenance:{action}");
            var result = MaintenanceResults is { Count: > 0 }
                ? MaintenanceResults.Dequeue()
                : MaintenanceResult;
            if (result.WasSuccessful && ApplySuccessfulMaintenance)
            {
                TaskQuery = action == StartupTaskMaintenanceAction.Create
                    ? BuildCurrentTaskQuery()
                    : StartupTaskQueryResult.NotFound();
            }

            return result;
        }

        public StartupRegistrationMaintenanceResult ExecuteMaintenance(StartupTaskMaintenanceAction action) =>
            RequestMaintenance(action);
    }
}
