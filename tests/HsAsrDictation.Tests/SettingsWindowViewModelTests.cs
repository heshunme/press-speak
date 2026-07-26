using HsAsrDictation.PostProcessing.Models;
using HsAsrDictation.Services;
using HsAsrDictation.Settings;
using HsAsrDictation.Views;
using Xunit;

namespace HsAsrDictation.Tests;

public sealed class SettingsWindowViewModelTests
{
    [Theory]
    [InlineData(StartupRegistrationMode.Disabled, false, false)]
    [InlineData(StartupRegistrationMode.Standard, true, false)]
    [InlineData(StartupRegistrationMode.Administrator, true, true)]
    public void Constructor_MapsStartupRegistrationModeToOptions(
        StartupRegistrationMode mode,
        bool expectedStartWithWindows,
        bool expectedStartWithWindowsAsAdministrator)
    {
        var viewModel = CreateViewModel(mode);

        Assert.Equal(expectedStartWithWindows, viewModel.StartWithWindows);
        Assert.Equal(expectedStartWithWindowsAsAdministrator, viewModel.StartWithWindowsAsAdministrator);
        Assert.Equal(mode, viewModel.DesiredStartupRegistrationMode);
        Assert.True(viewModel.IsStartupRegistrationAvailable);
        Assert.Equal(expectedStartWithWindows, viewModel.CanConfigureAdministratorStartup);
        Assert.False(viewModel.HasStartupRegistrationError);
        Assert.Empty(viewModel.StartupRegistrationErrorMessage);
    }

    [Fact]
    public void StartWithWindows_WhenDisabled_ClearsAdministratorOption()
    {
        var viewModel = CreateViewModel(StartupRegistrationMode.Administrator);

        viewModel.StartWithWindows = false;

        Assert.False(viewModel.StartWithWindows);
        Assert.False(viewModel.StartWithWindowsAsAdministrator);
        Assert.Equal(StartupRegistrationMode.Disabled, viewModel.DesiredStartupRegistrationMode);
    }

    [Fact]
    public void StartWithWindowsAsAdministrator_WhenStartupIsDisabled_RemainsDisabled()
    {
        var viewModel = CreateViewModel(StartupRegistrationMode.Disabled);

        viewModel.StartWithWindowsAsAdministrator = true;

        Assert.False(viewModel.StartWithWindowsAsAdministrator);
        Assert.Equal(StartupRegistrationMode.Disabled, viewModel.DesiredStartupRegistrationMode);
    }

    [Fact]
    public void Constructor_WhenStartupRegistrationIsUnavailable_DisablesOptionsAndPreservesError()
    {
        const string errorMessage = "无法读取计划任务。";

        var viewModel = CreateViewModel(null, errorMessage);

        Assert.False(viewModel.IsStartupRegistrationAvailable);
        Assert.False(viewModel.CanConfigureAdministratorStartup);
        Assert.True(viewModel.HasStartupRegistrationError);
        Assert.Equal(errorMessage, viewModel.StartupRegistrationErrorMessage);
        Assert.False(viewModel.StartWithWindows);
        Assert.False(viewModel.StartWithWindowsAsAdministrator);
        Assert.Null(viewModel.DesiredStartupRegistrationMode);
    }

    [Fact]
    public void Constructor_WhenStartupRegistrationNeedsRepair_KeepsOptionsEnabledAndShowsMessage()
    {
        const string message = "检测到待修复的计划任务。";

        var viewModel = CreateViewModel(StartupRegistrationMode.Administrator, message);

        Assert.True(viewModel.IsStartupRegistrationAvailable);
        Assert.True(viewModel.StartWithWindows);
        Assert.True(viewModel.StartWithWindowsAsAdministrator);
        Assert.True(viewModel.HasStartupRegistrationError);
        Assert.Equal(message, viewModel.StartupRegistrationErrorMessage);
    }

    [Fact]
    public void StartWithWindows_WhenStartupRegistrationIsUnavailable_RemainsDisabled()
    {
        var viewModel = CreateViewModel(null);

        viewModel.StartWithWindows = true;
        viewModel.StartWithWindowsAsAdministrator = true;

        Assert.False(viewModel.StartWithWindows);
        Assert.False(viewModel.StartWithWindowsAsAdministrator);
        Assert.Null(viewModel.DesiredStartupRegistrationMode);
        Assert.NotEmpty(viewModel.StartupRegistrationErrorMessage);
    }

    [Fact]
    public void ToSettings_WhenStartupRegistrationIsUnavailable_StillBuildsApplicationSettings()
    {
        var viewModel = CreateViewModel(null);
        viewModel.EnableStreamingPreview = false;

        var settings = viewModel.ToSettings(
            AppSettings.DefaultMaxRecordingDurationSeconds,
            AppSettings.DefaultHotkeyReleaseTailDurationMilliseconds);

        Assert.False(settings.EnableStreamingPreview);
        Assert.Null(viewModel.DesiredStartupRegistrationMode);
    }

    private static SettingsWindowViewModel CreateViewModel(
        StartupRegistrationMode? mode,
        string? startupRegistrationErrorMessage = null) =>
        new(
            AppSettings.CreateDefault(),
            [],
            new PostProcessingConfig(),
            mode,
            startupRegistrationErrorMessage: startupRegistrationErrorMessage);
}
