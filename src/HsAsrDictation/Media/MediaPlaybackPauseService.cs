using Windows.Media.Control;
using HsAsrDictation.Logging;
using HsAsrDictation.Services;

namespace HsAsrDictation.Media;

/// <summary>
/// 录音期间暂停系统媒体播放（SMTC 会话）、录音结束后恢复由本服务暂停过的会话。
/// 通过订阅 <see cref="DictationCoordinator.StateChanged"/> 驱动：状态进入
/// <see cref="DictationState.Recording"/>（音频启动成功后才会发布）时暂停，离开
/// Recording 时恢复。所有 SMTC 异常只写日志，绝不抛进听写主链路。
/// 并发模型：<see cref="_sync"/> 保护暂停标记与已暂停会话列表；<see cref="_gate"/>
/// 串行化暂停/恢复执行，保证极快点按（按下即松开）时恢复一定排在暂停完成之后。
/// 已知取舍：录音期间用户若手动操作播放器，结束时仍可能按"我们暂停过它"恢复播放。
/// </summary>
public sealed class MediaPlaybackPauseService
{
    private readonly LocalLogService _logger;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<GlobalSystemMediaTransportControlsSession> _pausedSessions = [];
    private bool _pauseActive;

    public MediaPlaybackPauseService(LocalLogService logger)
    {
        _logger = logger;
    }

    public void OnDictationStateChanged(DictationStatus status)
    {
        Func<Task> action;
        lock (_sync)
        {
            if (status.State == DictationState.Recording)
            {
                // 流式识别期间会反复发布 Recording，只在首次进入时暂停一次，
                // 避免覆盖用户在录音期间手动恢复的播放。
                if (_pauseActive)
                {
                    return;
                }

                _pauseActive = true;
                action = PausePlayingMediaAsync;
            }
            else
            {
                if (!_pauseActive)
                {
                    return;
                }

                _pauseActive = false;
                action = ResumePausedMediaAsync;
            }
        }

        _ = RunSerializedAsync(action);
    }

    /// <summary>应用退出时的兜底恢复；无已暂停会话时为无操作。</summary>
    public Task ResumeAllAsync()
    {
        lock (_sync)
        {
            _pauseActive = false;
        }

        return RunSerializedAsync(ResumePausedMediaAsync);
    }

    private async Task RunSerializedAsync(Func<Task> action)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("媒体播放控制失败。", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PausePlayingMediaAsync()
    {
        GlobalSystemMediaTransportControlsSessionManager manager;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync()
                .AsTask()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error("获取系统媒体会话管理器失败。", ex);
            return;
        }

        foreach (var session in manager.GetSessions())
        {
            try
            {
                if (session.GetPlaybackInfo().PlaybackStatus !=
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    continue;
                }

                if (await session.TryPauseAsync().AsTask().ConfigureAwait(false))
                {
                    lock (_sync)
                    {
                        _pausedSessions.Add(session);
                    }

                    _logger.Info($"已暂停媒体播放：{session.SourceAppUserModelId}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"暂停媒体会话失败：{ex.Message}");
            }
        }
    }

    private async Task ResumePausedMediaAsync()
    {
        List<GlobalSystemMediaTransportControlsSession> sessions;
        lock (_sync)
        {
            if (_pausedSessions.Count == 0)
            {
                return;
            }

            sessions = [.. _pausedSessions];
            _pausedSessions.Clear();
        }

        foreach (var session in sessions)
        {
            try
            {
                // 会话可能已被来源应用释放，或用户已自行恢复/切走，只恢复仍处于暂停态的。
                if (session.GetPlaybackInfo().PlaybackStatus !=
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                {
                    continue;
                }

                await session.TryPlayAsync().AsTask().ConfigureAwait(false);
                _logger.Info($"已恢复媒体播放：{session.SourceAppUserModelId}");
            }
            catch (Exception ex)
            {
                _logger.Warn($"恢复媒体会话失败：{ex.Message}");
            }
        }
    }
}
