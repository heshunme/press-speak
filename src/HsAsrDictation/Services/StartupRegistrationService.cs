namespace HsAsrDictation.Services;

public sealed class StartupRegistrationService : IStartupRegistrationService
{
    private readonly IStartupRegistrationPlatform _platform;

    public StartupRegistrationService(IStartupRegistrationPlatform platform)
    {
        _platform = platform;
    }

    public StartupRegistrationState GetState()
    {
        var taskQuery = _platform.QueryAdministratorTask();
        if (taskQuery.WasFailed)
        {
            throw new InvalidOperationException(
                taskQuery.Message ?? "无法查询管理员开机自启计划任务。");
        }

        var runCommand = _platform.GetRunCommand();
        if (taskQuery.WasFound)
        {
            var taskIsCurrent = StartupRegistrationCommandBuilder.MatchesAdministratorTask(
                taskQuery,
                _platform.ExecutablePath,
                _platform.UserSid,
                _platform.CommandInterpreterPath);
            var hasRunRegistration = runCommand is not null;
            if (taskIsCurrent && !hasRunRegistration)
            {
                return new StartupRegistrationState
                {
                    Mode = StartupRegistrationMode.Administrator
                };
            }

            var message = taskIsCurrent
                ? "同时检测到普通启动项和管理员计划任务；保存后将清理普通启动项。"
                : taskQuery.Message ??
                  "检测到配置不一致或安全性无法确认的管理员计划任务；保存管理员模式可重建，切换模式或关闭可移除。";
            if (!taskIsCurrent && hasRunRegistration)
            {
                message += " 当前还存在普通启动项。";
            }

            return new StartupRegistrationState
            {
                Mode = StartupRegistrationMode.Administrator,
                RequiresRepair = true,
                Message = message
            };
        }

        if (runCommand is null)
        {
            return new StartupRegistrationState
            {
                Mode = StartupRegistrationMode.Disabled
            };
        }

        var runCommandIsCurrent = StartupRegistrationCommandBuilder.MatchesRunCommand(
            runCommand,
            _platform.ExecutablePath);
        return new StartupRegistrationState
        {
            Mode = StartupRegistrationMode.Standard,
            RequiresRepair = !runCommandIsCurrent,
            Message = runCommandIsCurrent
                ? null
                : "检测到指向其他程序路径的普通启动项；保存可更新，关闭可移除。"
        };
    }

    public StartupRegistrationMode GetMode() => GetState().Mode;

    public StartupRegistrationChangeResult SetMode(StartupRegistrationMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            return StartupRegistrationChangeResult.Failed("不支持的开机自启模式。");
        }

        try
        {
            var taskQuery = _platform.QueryAdministratorTask();
            if (taskQuery.WasFailed)
            {
                return StartupRegistrationChangeResult.Failed(
                    taskQuery.Message ?? "无法查询管理员开机自启计划任务。");
            }

            return mode switch
            {
                StartupRegistrationMode.Disabled => Disable(taskQuery),
                StartupRegistrationMode.Standard => EnableStandard(taskQuery),
                StartupRegistrationMode.Administrator => EnableAdministrator(taskQuery),
                _ => StartupRegistrationChangeResult.Failed("不支持的开机自启模式。")
            };
        }
        catch (Exception ex)
        {
            return StartupRegistrationChangeResult.Failed(ex.Message);
        }
    }

    private StartupRegistrationChangeResult Disable(StartupTaskQueryResult taskQuery)
    {
        if (taskQuery.WasFound)
        {
            var maintenanceResult = _platform.RequestMaintenance(StartupTaskMaintenanceAction.Delete);
            var changeResult = ToChangeResult(maintenanceResult);
            if (!changeResult.WasSuccessful)
            {
                return changeResult;
            }
        }

        _platform.DeleteRunCommand();
        return StartupRegistrationChangeResult.Succeeded();
    }

    private StartupRegistrationChangeResult EnableStandard(StartupTaskQueryResult taskQuery)
    {
        var runCommand = StartupRegistrationCommandBuilder.BuildRunCommand(_platform.ExecutablePath);
        var previousRunCommand = _platform.GetRunCommand();
        _platform.SetRunCommand(runCommand);

        if (taskQuery.WasFound)
        {
            var maintenanceResult = _platform.RequestMaintenance(StartupTaskMaintenanceAction.Delete);
            var changeResult = ToChangeResult(maintenanceResult);
            if (!changeResult.WasSuccessful)
            {
                return RestoreRunCommand(previousRunCommand, changeResult);
            }
        }

        return StartupRegistrationChangeResult.Succeeded();
    }

    private StartupRegistrationChangeResult EnableAdministrator(StartupTaskQueryResult taskQuery)
    {
        var taskIsCurrent = StartupRegistrationCommandBuilder.MatchesAdministratorTask(
            taskQuery,
            _platform.ExecutablePath,
            _platform.UserSid,
            _platform.CommandInterpreterPath);

        if (!taskIsCurrent)
        {
            var validationResult = _platform.ValidateAdministratorRegistration();
            if (!validationResult.WasSuccessful)
            {
                return validationResult;
            }

            var maintenanceResult = _platform.RequestMaintenance(StartupTaskMaintenanceAction.Create);
            var changeResult = ToChangeResult(maintenanceResult);
            if (!changeResult.WasSuccessful)
            {
                return changeResult;
            }

            var verificationResult = _platform.QueryAdministratorTask();
            if (verificationResult.WasFailed)
            {
                return CompensateFailedAdministratorTaskCreation(
                    verificationResult.Message ?? "无法验证管理员开机自启计划任务。");
            }

            if (!StartupRegistrationCommandBuilder.MatchesAdministratorTask(
                    verificationResult,
                    _platform.ExecutablePath,
                    _platform.UserSid,
                    _platform.CommandInterpreterPath))
            {
                return CompensateFailedAdministratorTaskCreation(
                    "管理员开机自启计划任务创建后校验失败。");
            }
        }

        // Keep the previous Run registration until the elevated task exists.
        try
        {
            _platform.DeleteRunCommand();
        }
        catch (Exception ex)
        {
            if (taskIsCurrent)
            {
                return StartupRegistrationChangeResult.Failed(
                    $"管理员开机自启仍然有效，但清理普通启动项失败：{ex.Message}");
            }

            var cleanupResult = _platform.RequestMaintenance(StartupTaskMaintenanceAction.Delete);
            if (cleanupResult.WasSuccessful)
            {
                return StartupRegistrationChangeResult.Failed(
                    $"清理普通启动项失败：{ex.Message} 已删除本次创建的管理员计划任务。");
            }

            return StartupRegistrationChangeResult.Failed(
                $"清理普通启动项失败：{ex.Message} 删除本次创建的管理员计划任务也失败：{cleanupResult.Message ?? "未知错误"}");
        }

        return StartupRegistrationChangeResult.Succeeded();
    }

    private StartupRegistrationChangeResult CompensateFailedAdministratorTaskCreation(string failureMessage)
    {
        var cleanupResult = _platform.RequestMaintenance(StartupTaskMaintenanceAction.Delete);
        if (cleanupResult.WasSuccessful)
        {
            return StartupRegistrationChangeResult.Failed($"{failureMessage} 已删除未确认的计划任务。");
        }

        return StartupRegistrationChangeResult.Failed(
            $"{failureMessage} 删除未确认的计划任务也失败：{cleanupResult.Message ?? "未知错误"}");
    }

    private static StartupRegistrationChangeResult ToChangeResult(
        StartupRegistrationMaintenanceResult maintenanceResult)
    {
        if (maintenanceResult.WasSuccessful)
        {
            return StartupRegistrationChangeResult.Succeeded();
        }

        if (maintenanceResult.WasCanceled)
        {
            return StartupRegistrationChangeResult.Canceled(
                maintenanceResult.Message ?? "用户取消了管理员权限请求。");
        }

        return StartupRegistrationChangeResult.Failed(
            maintenanceResult.Message ?? "维护开机自启计划任务失败。");
    }

    private StartupRegistrationChangeResult RestoreRunCommand(
        string? previousRunCommand,
        StartupRegistrationChangeResult originalResult)
    {
        try
        {
            if (previousRunCommand is null)
            {
                _platform.DeleteRunCommand();
            }
            else
            {
                _platform.SetRunCommand(previousRunCommand);
            }

            return originalResult;
        }
        catch (Exception ex)
        {
            return StartupRegistrationChangeResult.Failed(
                $"{originalResult.Message ?? "删除管理员开机自启计划任务失败。"} 恢复原普通启动项失败：{ex.Message}");
        }
    }
}
