using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Serilog;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests;

public class ProgramTests
{
    [Fact]
    public async Task TryRunParsedCommandAsync_InvalidResume_SkipsDelegateAndSetsExitCode()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"legacyrecords-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        var logger = new LoggerConfiguration().CreateLogger();
        var originalRunId = GetPrivateStaticField<string>("runId");
        var originalOutputDirectory = GetPrivateStaticField<string>("effectiveOutputDirectory");
        var originalLog = GetPrivateStaticField<Serilog.ILogger>("log");
        var originalExitCode = GetPrivateStaticField<int>("exitCode");

        try
        {
            SetPrivateStaticField("runId", "missing-run-id");
            SetPrivateStaticField("effectiveOutputDirectory", outputDirectory);
            SetPrivateStaticField("log", logger);
            SetPrivateStaticField("exitCode", 0);

            var executed = false;
            var didRun = await InvokeTryRunParsedCommandAsync(
                isResuming: true,
                runParsedCommandAsync: () =>
                {
                    executed = true;
                    return Task.CompletedTask;
                });

            didRun.ShouldBeFalse();
            executed.ShouldBeFalse();
            GetPrivateStaticField<int>("exitCode").ShouldBe(-1);
        }
        finally
        {
            SetPrivateStaticField("runId", originalRunId);
            SetPrivateStaticField("effectiveOutputDirectory", originalOutputDirectory);
            SetPrivateStaticField("log", originalLog);
            SetPrivateStaticField("exitCode", originalExitCode);
            logger.Dispose();
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task TryRunParsedCommandAsync_NotResuming_RunsDelegate()
    {
        var originalRunId = GetPrivateStaticField<string>("runId");
        var originalOutputDirectory = GetPrivateStaticField<string>("effectiveOutputDirectory");
        var originalLog = GetPrivateStaticField<Serilog.ILogger>("log");
        var originalExitCode = GetPrivateStaticField<int>("exitCode");
        var logger = new LoggerConfiguration().CreateLogger();

        try
        {
            SetPrivateStaticField("runId", "any-run-id");
            SetPrivateStaticField("effectiveOutputDirectory", Path.GetTempPath());
            SetPrivateStaticField("log", logger);
            SetPrivateStaticField("exitCode", 0);

            var executed = false;
            var didRun = await InvokeTryRunParsedCommandAsync(
                isResuming: false,
                runParsedCommandAsync: () =>
                {
                    executed = true;
                    return Task.CompletedTask;
                });

            didRun.ShouldBeTrue();
            executed.ShouldBeTrue();
            GetPrivateStaticField<int>("exitCode").ShouldBe(0);
        }
        finally
        {
            SetPrivateStaticField("runId", originalRunId);
            SetPrivateStaticField("effectiveOutputDirectory", originalOutputDirectory);
            SetPrivateStaticField("log", originalLog);
            SetPrivateStaticField("exitCode", originalExitCode);
            logger.Dispose();
        }
    }

    private static T GetPrivateStaticField<T>(string fieldName)
    {
        var field = typeof(Program).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        field.ShouldNotBeNull();

        return (T)field.GetValue(null)!;
    }

    private static void SetPrivateStaticField<T>(string fieldName, T value)
    {
        var field = typeof(Program).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        field.ShouldNotBeNull();
        field.SetValue(null, value);
    }

    private static async Task<bool> InvokeTryRunParsedCommandAsync(bool isResuming, Func<Task> runParsedCommandAsync)
    {
        var method = typeof(Program).GetMethod("TryRunParsedCommandAsync", BindingFlags.Static | BindingFlags.NonPublic);
        method.ShouldNotBeNull();

        var task = method.Invoke(null, new object[] { isResuming, runParsedCommandAsync }) as Task<bool>;
        if (task == null)
        {
            throw new InvalidOperationException("Program.TryRunParsedCommandAsync did not return Task<bool>.");
        }

        return await task;
    }
}
