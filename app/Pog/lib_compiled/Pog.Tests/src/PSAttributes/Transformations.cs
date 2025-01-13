using System.Management.Automation;
using System.Management.Automation.Runspaces;
using JetBrains.Annotations;
using Pog.PSAttributes;
using Xunit;

namespace Pog.Tests.PSAttributes;

/// Utility command used for testing the ResolvePath attribute below.
[PublicAPI]
[Cmdlet("Test", "PathResolution")]
public class TestPathResolutionCommand : Cmdlet {
    [Parameter(Mandatory = true, Position = 0)]
    [ResolvePath(Array = true)]
    public string[] Path = null!;

    protected override void BeginProcessing() {
        base.BeginProcessing();
        WriteObject(Path, true);
    }
}

public sealed class ArgumentTransformationTests : IDisposable {
    private readonly Runspace _runspace = RunspaceFactory.CreateRunspace();
    private readonly DirectoryInfo _testDir;

    public ArgumentTransformationTests() {
        _testDir = Directory.CreateTempSubdirectory("PogTests-");
        _testDir.CreateSubdirectory("subdir");
        _testDir.CreateSubdirectory("subdir2");

        _runspace.Open();
        PowerShell.Create(_runspace)
                .AddStatement().AddCommand("cd").AddArgument(_testDir.FullName)
                // ensure `Test-PathResolution` is available in the module
                .AddStatement().AddCommand("Import-Module").AddArgument(typeof(TestPathResolutionCommand).Assembly.Location)
                .Invoke();
    }

    [Fact]
    public void TestRelativePath() {
        using var ps = PowerShell.Create(_runspace);
        ps.AddCommand("Test-PathResolution").AddArgument("subdir");

        var result = ps.Invoke().Select(o => o.BaseObject.ToString()).Single();
        Assert.Equal(result, $"{_testDir}\\subdir");
    }

    [Fact]
    public void TestMultipleRelativePaths() {
        using var ps = PowerShell.Create(_runspace);
        ps.AddCommand("Test-PathResolution").AddArgument(new[] {"subdir", "subdir2"});

        var result = ps.Invoke().Select(o => o.BaseObject.ToString()).ToArray();
        Assert.Equal<string?[]>(result, [$"{_testDir}\\subdir", $"{_testDir}\\subdir2"]);
    }

    [Fact]
    public void TestNonExistentPath() {
        using var ps = PowerShell.Create(_runspace);
        ps.AddCommand("Test-PathResolution").AddArgument("nonexistent");
        Assert.ThrowsAny<ParameterBindingException>(() => ps.Invoke());
    }

    [Fact]
    public void TestNullPath() {
        using var ps = PowerShell.Create(_runspace);
        ps.AddCommand("Test-PathResolution").AddArgument(null);
        Assert.ThrowsAny<ParameterBindingException>(() => ps.Invoke());
    }

    [Fact]
    public void TestNullPathInArray() {
        using var ps = PowerShell.Create(_runspace);
        ps.AddCommand("Test-PathResolution").AddArgument(new[] {"subdir", null});
        Assert.ThrowsAny<ParameterBindingException>(() => ps.Invoke());
    }

    public void Dispose() {
        _runspace.Dispose();
        _testDir.Delete(true);
    }
}