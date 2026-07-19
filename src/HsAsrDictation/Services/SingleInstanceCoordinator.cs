using System.Diagnostics;
using System.Security.Principal;
using HsAsrDictation.Logging;

namespace HsAsrDictation.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly TimeSpan ElevatedHandoffWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly InstanceRuntimeContext _context;
    private readonly LocalLogService _logger;

    private Mutex? _primaryMutex;
    private Mutex? _modeMutex;

    public SingleInstanceCoordinator(InstanceRuntimeContext context, LocalLogService logger)
    {
        _context = context;
        _logger = logger;
    }

    public SingleInstanceStartupResult CoordinateStartup(PrivilegeMode currentMode, StartupOptions options)
    {
        var acquisitionTimeout = options.ElevationApplied && currentMode == PrivilegeMode.Administrator
            ? ElevatedHandoffWaitTimeout
            : TimeSpan.Zero;

        if (TryAcquirePrimaryMutex(acquisitionTimeout) && TryAcquireModeMutex(currentMode))
        {
            _logger.Info($"当前实例已成为主实例：mode={currentMode.ToDisplayText()}");
            return SingleInstanceStartupResult.BecomePrimary();
        }

        var existingMode = DetectExistingMode();
        var message = SingleInstanceLaunchMessaging.BuildAlreadyRunningMessage(existingMode, currentMode, options);

        _logger.Info(
            $"单实例互斥命中：currentMode={currentMode.ToDisplayText()}, existingMode={existingMode?.ToDisplayText() ?? "-"}, requestAdmin={options.RequestAdministrator}, elevationApplied={options.ElevationApplied}");

        Dispose();
        return SingleInstanceStartupResult.ExitWithMessage(message);
    }

    public void Dispose()
    {
        if (_modeMutex is not null)
        {
            ReleaseMutexSafely(_modeMutex);
            _modeMutex.Dispose();
            _modeMutex = null;
        }

        if (_primaryMutex is not null)
        {
            ReleaseMutexSafely(_primaryMutex);
            _primaryMutex.Dispose();
            _primaryMutex = null;
        }
    }

    private bool TryAcquirePrimaryMutex(TimeSpan timeout)
    {
        _primaryMutex = new Mutex(false, _context.PrimaryMutexName);
        return WaitForMutex(_primaryMutex, timeout);
    }

    private bool TryAcquireModeMutex(PrivilegeMode mode)
    {
        _modeMutex = new Mutex(false, _context.GetModeMutexName(mode));
        if (WaitForMutex(_modeMutex, TimeSpan.Zero))
        {
            return true;
        }

        _modeMutex.Dispose();
        _modeMutex = null;
        return false;
    }

    private PrivilegeMode? DetectExistingMode()
    {
        if (Mutex.TryOpenExisting(_context.GetModeMutexName(PrivilegeMode.Administrator), out var adminMutex))
        {
            adminMutex.Dispose();
            return PrivilegeMode.Administrator;
        }

        if (Mutex.TryOpenExisting(_context.GetModeMutexName(PrivilegeMode.Standard), out var standardMutex))
        {
            standardMutex.Dispose();
            return PrivilegeMode.Standard;
        }

        return null;
    }

    private static bool WaitForMutex(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static void ReleaseMutexSafely(Mutex mutex)
    {
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }
    }
}

public sealed class SingleInstanceStartupResult
{
    public bool ShouldContinueStartup { get; init; }

    public string? ExitMessage { get; init; }

    public static SingleInstanceStartupResult BecomePrimary() => new()
    {
        ShouldContinueStartup = true
    };

    public static SingleInstanceStartupResult ExitWithMessage(string message) => new()
    {
        ShouldContinueStartup = false,
        ExitMessage = message
    };
}

public sealed class InstanceRuntimeContext
{
    public required string UserScopeKey { get; init; }

    public required int SessionId { get; init; }

    public required string PrimaryMutexName { get; init; }

    public required IReadOnlyDictionary<PrivilegeMode, string> ModeMutexNames { get; init; }

    public string GetModeMutexName(PrivilegeMode mode) => ModeMutexNames[mode];

    public static InstanceRuntimeContext CreateForCurrentUser()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var userScopeKey = GetCurrentUserScopeKey();

        return new InstanceRuntimeContext
        {
            UserScopeKey = userScopeKey,
            SessionId = sessionId,
            PrimaryMutexName = $@"Local\HsAsrDictation.instance.{sessionId}.{userScopeKey}",
            ModeMutexNames = Enum.GetValues<PrivilegeMode>().ToDictionary(
                mode => mode,
                mode => $@"Local\HsAsrDictation.instance_mode.{sessionId}.{userScopeKey}.{mode.ToKey()}")
        };
    }

    private static string GetCurrentUserScopeKey()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!string.IsNullOrWhiteSpace(identity.User?.Value))
            {
                return identity.User.Value.Replace('-', '_');
            }
        }

        return Environment.UserName.Replace('\\', '_').Replace('/', '_').Replace(' ', '_');
    }
}
