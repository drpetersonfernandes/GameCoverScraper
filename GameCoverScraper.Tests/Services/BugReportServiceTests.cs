using FluentAssertions;
using GameCoverScraper.Managers;
using GameCoverScraper.Services;
using Xunit;

namespace GameCoverScraper.Tests.Services;

[Collection("BugReportService")]
public class BugReportServiceTests : IDisposable
{
    private readonly string _errorLogPath;
    private readonly string _userLogPath;
    private readonly SettingsManager _settings;

    public BugReportServiceTests()
    {
        _errorLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
        _userLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_user.log");

        DeleteLogFiles();

        _settings = new SettingsManager
        {
            BugReportApiKey = "",
            BugReportApiUrl = "https://example.com/api"
        };
    }

    public void Dispose()
    {
        DeleteLogFiles();
        GC.SuppressFinalize(this);
    }

    private void DeleteLogFiles()
    {
        try
        {
            if (File.Exists(_errorLogPath)) File.Delete(_errorLogPath);
            if (File.Exists(_userLogPath)) File.Delete(_userLogPath);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public void ConstructorShouldNotThrow()
    {
        var act = () => new BugReportService(_settings);

        act.Should().NotThrow();
    }

    [Fact]
    public void AppVersionShouldNotBeNullOrEmpty()
    {
        BugReportService.AppVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LogErrorAsyncShouldCreateErrorLogFile()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        File.Exists(_errorLogPath).Should().BeTrue();
    }

    [Fact]
    public async Task LogErrorAsyncShouldCreateUserLogFile()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        File.Exists(_userLogPath).Should().BeTrue();
    }

    [Fact]
    public async Task LogErrorAsyncShouldContainEnvironmentDetailsSection()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("=== Environment Details ===");
        content.Should().Contain("Application Name: GameCoverScraper");
        content.Should().Contain("OS Version:");
        content.Should().Contain("Architecture:");
        content.Should().Contain("Processor Count:");
        content.Should().Contain("Base Directory:");
    }

    [Fact]
    public async Task LogErrorAsyncShouldContainErrorDetailsSection()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("=== Error Details ===");
        content.Should().Contain("Error Message: Test error");
    }

    [Fact]
    public async Task LogErrorAsyncShouldContainExceptionDetailsSection()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("=== Exception Details ===");
        content.Should().Contain("Type: System.InvalidOperationException");
        content.Should().Contain("Message: Test error");
    }

    [Fact]
    public async Task LogErrorAsyncShouldContainAppVersion()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("Application Version: ");
        content.Should().Contain(BugReportService.AppVersion);
    }

    [Fact]
    public async Task LogErrorAsyncWithNullExceptionShouldUseInvalidOperationException()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(null);

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("Type: System.InvalidOperationException");
    }

    [Fact]
    public async Task LogErrorAsyncWithNullExceptionShouldLogErrorMessage()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(null);

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("Error Message: LogErrorAsync was called with a null exception.");
    }

    [Fact]
    public async Task LogErrorAsyncWithContextMessageShouldIncludeContext()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"), "User clicked save button");

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("Context: User clicked save button");
    }

    [Fact]
    public async Task LogErrorAsyncWithInnerExceptionShouldIncludeInnerExceptionSection()
    {
        var service = new BugReportService(_settings);
        var innerEx = new ArgumentException("Inner argument error");
        var outerEx = new InvalidOperationException("Outer error", innerEx);

        await service.LogErrorAsync(outerEx);

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("--- Inner Exception ---");
        content.Should().Contain("Type: System.ArgumentException");
        content.Should().Contain("Message: Inner argument error");
    }

    [Fact]
    public async Task LogErrorAsyncWithoutInnerExceptionShouldNotContainInnerExceptionHeader()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().NotContain("--- Inner Exception ---");
    }

    [Fact]
    public async Task LogErrorAsyncUserLogShouldContainSeparatorLine()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        var content = await File.ReadAllTextAsync(_userLogPath);
        content.Should().Contain("--------------------------------------------------------------------------------------------------------------");
    }

    [Fact]
    public async Task LogErrorAsyncWithEmptyApiKeyShouldNotDeleteErrorLog()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        File.Exists(_errorLogPath).Should().BeTrue();
    }

    [Fact]
    public async Task LogErrorAsyncWithInnerExceptionShouldOnlyLogFirstLevel()
    {
        var service = new BugReportService(_settings);
        // ReSharper disable once NotResolvedInText
        var innerMost = new ArgumentNullException("param", "Parameter cannot be null");
        var inner = new InvalidOperationException("Inner operation failed", innerMost);
        var outerEx = new InvalidOperationException("Outer error", inner);

        await service.LogErrorAsync(outerEx);

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("--- Inner Exception ---");
        content.Should().Contain("Type: System.InvalidOperationException");
        content.Should().Contain("Message: Inner operation failed");
        content.Should().NotContain("Type: System.ArgumentNullException",
            "only the first-level inner exception is logged; nested inner exceptions are not traversed");
    }

    [Fact]
    public async Task LogErrorAsyncWithExceptionNotThrownShouldHandleNullStackTrace()
    {
        var service = new BugReportService(_settings);
        var ex = new InvalidOperationException("Never thrown");

        await service.LogErrorAsync(ex);

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("StackTrace: N/A");
    }

    [Fact]
    public async Task LogErrorAsyncFileOutputShouldContainBitnessInformation()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        var content = await File.ReadAllTextAsync(_errorLogPath);
        (content.Contains("64-bit") || content.Contains("32-bit")).Should().BeTrue();
    }

    [Fact]
    public async Task LogErrorAsyncFileOutputShouldContainWindowsVersion()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("Windows Version:");
    }

    [Fact]
    public async Task LogErrorAsyncFileOutputShouldContainTempPath()
    {
        var service = new BugReportService(_settings);

        await service.LogErrorAsync(new InvalidOperationException("Test error"));

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("Temp Path:");
    }

    [Fact]
    public async Task LogErrorAsyncWithExceptionHavingNonNullSourceShouldLogSource()
    {
        var service = new BugReportService(_settings);
        var ex = new InvalidOperationException("Test error") { Source = "TestModule" };

        await service.LogErrorAsync(ex);

        var content = await File.ReadAllTextAsync(_errorLogPath);
        content.Should().Contain("Source: TestModule");
    }
}