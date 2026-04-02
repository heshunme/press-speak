using HsAsrDictation.Foreground;

namespace HsAsrDictation.Insertion;

public interface ITextInsertionService
{
    Task<InsertionResult> InsertAsync(string text, ForegroundContext context, CancellationToken ct = default);
}
