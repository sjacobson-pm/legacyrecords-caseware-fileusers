using System;
using System.IO;
using LegacyRecordsCaseWareFileUsers.Options;
using LegacyRecordsCaseWareFileUsers.Services.Implementations;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace LegacyRecordsCaseWareFileUsers.Tests.Services.Implementations;

public class WorkspaceManagerTests
{
    [Fact]
    public void CreateWorkspace_CreatesUniqueDirectoryUnderConfiguredRoot()
    {
        RunWithTempRoot(root =>
        {
            var sut = CreateSut(root);

            var workspaceOne = sut.CreateWorkspace();
            var workspaceTwo = sut.CreateWorkspace();

            Directory.Exists(workspaceOne).ShouldBeTrue();
            Directory.Exists(workspaceTwo).ShouldBeTrue();
            workspaceOne.ShouldNotBe(workspaceTwo);
            workspaceOne.ShouldStartWith(root);
        });
    }

    [Fact]
    public void CopyFileToWorkspace_CopiesFilePreservingNameAndContent()
    {
        RunWithTempRoot(root =>
        {
            var sut = CreateSut(root);
            var workspace = sut.CreateWorkspace();
            var sourcePath = Path.Combine(root, "engagement.ac_");
            File.WriteAllText(sourcePath, "engagement-bytes");

            var destinationPath = sut.CopyFileToWorkspace(sourcePath, workspace);

            Path.GetFileName(destinationPath).ShouldBe("engagement.ac_");
            Path.GetDirectoryName(destinationPath).ShouldBe(workspace);
            File.Exists(destinationPath).ShouldBeTrue();
            File.ReadAllText(destinationPath).ShouldBe("engagement-bytes");
        });
    }

    [Fact]
    public void DeleteWorkspace_RemovesDirectoryAndContents()
    {
        RunWithTempRoot(root =>
        {
            var sut = CreateSut(root);
            var workspace = sut.CreateWorkspace();
            File.WriteAllText(Path.Combine(workspace, "file.ac_"), "data");

            sut.DeleteWorkspace(workspace);

            Directory.Exists(workspace).ShouldBeFalse();
        });
    }

    [Fact]
    public void DeleteWorkspace_NonExistentDirectory_DoesNotThrow()
    {
        RunWithTempRoot(root =>
        {
            var sut = CreateSut(root);
            var missing = Path.Combine(root, "does-not-exist");

            Should.NotThrow(() => sut.DeleteWorkspace(missing));
        });
    }

    private static void RunWithTempRoot(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "wm-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            test(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static WorkspaceManager CreateSut(string root)
    {
        var options = Microsoft.Extensions.Options.Options.Create(
            new ConfigurationOptions { Workspace = new WorkspaceOptions { RootPath = root } });
        var logger = Substitute.For<ILogger<WorkspaceManager>>();

        return new WorkspaceManager(options, logger);
    }
}
