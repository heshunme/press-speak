namespace HsAsrDictation.Services;

public sealed class StartupOptions
{
    public const string AdminFlag = "--admin";
    public const string ElevationAppliedFlag = "--elevation-applied";

    public bool RequestAdministrator { get; init; }

    public bool ElevationApplied { get; init; }

    public IReadOnlyList<string> ForwardedArgs { get; init; } = [];

    public static StartupOptions Parse(IEnumerable<string> args)
    {
        var forwardedArgs = new List<string>();
        var requestAdministrator = false;
        var elevationApplied = false;

        foreach (var arg in args)
        {
            if (string.Equals(arg, AdminFlag, StringComparison.OrdinalIgnoreCase))
            {
                requestAdministrator = true;
                continue;
            }

            if (string.Equals(arg, ElevationAppliedFlag, StringComparison.OrdinalIgnoreCase))
            {
                elevationApplied = true;
                continue;
            }

            forwardedArgs.Add(arg);
        }

        return new StartupOptions
        {
            RequestAdministrator = requestAdministrator,
            ElevationApplied = elevationApplied,
            ForwardedArgs = forwardedArgs
        };
    }

    public bool ShouldRestartAsAdministrator(bool isRunningAsAdministrator) =>
        RequestAdministrator && !ElevationApplied && !isRunningAsAdministrator;

    public IReadOnlyList<string> BuildElevatedRestartArgs()
    {
        var args = new List<string>(ForwardedArgs.Count + 2);
        args.AddRange(ForwardedArgs);
        args.Add(AdminFlag);
        args.Add(ElevationAppliedFlag);
        return args;
    }
}
