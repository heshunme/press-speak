using System.IO;
using HsAsrDictation.Logging;
using SherpaOnnx;

namespace HsAsrDictation.Asr;

public sealed class SherpaOfflinePunctuationService : IPunctuationService
{
    private readonly object _gate = new();
    private readonly LocalLogService _logger;
    private OfflinePunctuation? _punctuation;
    private PunctuationRuntimeOptions _options = new();
    private string? _activeModelPath;

    public SherpaOfflinePunctuationService(LocalLogService logger)
    {
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public bool IsReady => _punctuation is not null;

    public void Reload(PunctuationRuntimeOptions options)
    {
        lock (_gate)
        {
            _options = options;
            _logger.Info($"[Punctuation] enabled={options.Enabled}, modelPath={options.ModelPath}, numThreads={options.NumThreads}");

            if (!options.Enabled)
            {
                DisposeCurrent();
                return;
            }

            if (string.IsNullOrWhiteSpace(options.ModelPath) || !File.Exists(options.ModelPath))
            {
                DisposeCurrent();
                _logger.Warn($"[Punctuation] model missing: {options.ModelPath}");
                return;
            }

            if (_punctuation is not null &&
                string.Equals(_activeModelPath, options.ModelPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DisposeCurrent();

            try
            {
                var config = new OfflinePunctuationConfig();
                config.Model.CtTransformer = options.ModelPath;
                config.Model.NumThreads = Math.Max(1, options.NumThreads);
                config.Model.Debug = 0;
                config.Model.Provider = "cpu";

                _punctuation = new OfflinePunctuation(config);
                _activeModelPath = options.ModelPath;
                _logger.Info($"[Punctuation] model ready: {options.ModelPath}");
            }
            catch (Exception ex)
            {
                DisposeCurrent();
                _logger.Error($"[Punctuation] failed to initialize: {options.ModelPath}", ex);
            }
        }
    }

    public string TryAddPunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        lock (_gate)
        {
            if (!_options.Enabled || _punctuation is null)
            {
                return text;
            }

            try
            {
                var punctuated = _punctuation.AddPunct(text);
                if (string.IsNullOrWhiteSpace(punctuated))
                {
                    return text;
                }

                _logger.Info($"[Punctuation] applied inputLength={text.Length} outputLength={punctuated.Length}");
                return punctuated;
            }
            catch (Exception ex)
            {
                _logger.Error("[Punctuation] failed to punctuate text.", ex);
                return text;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            DisposeCurrent();
        }
    }

    private void DisposeCurrent()
    {
        _punctuation?.Dispose();
        _punctuation = null;
        _activeModelPath = null;
    }
}
