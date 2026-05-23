namespace HsAsrDictation.Foreground;

public interface IForegroundContextService
{
    ForegroundContext Capture();

    bool Restore(ForegroundContext context);
}
