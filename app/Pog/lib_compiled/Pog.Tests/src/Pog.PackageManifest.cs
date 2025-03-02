using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Pog.InnerCommands;
using Xunit;

namespace Pog.Tests;

public class PackageManifestTests {
    private readonly PackageSourceNoArchive _installParams = new() {
        Url = null!,
        ExpectedHash = null,
        UserAgent = default,
        Target = "",
    };

    private void TestShouldThrow(string urlSb) {
        var source = _installParams with {Url = ScriptBlock.Create(urlSb)};
        Assert.Throws<InvalidPackageManifestUrlScriptBlockException>(() => ResolveUrl(source));
    }

    private void TestResult(string expected, string urlSb) {
        var source = _installParams with {Url = ScriptBlock.Create(urlSb)};
        var exception = Record.Exception(() => ResolveUrl(source));
        Assert.Null(exception);
        Assert.Equal(expected, ResolveUrl(source));
    }

    private sealed class TestPackage(string packageName, PackageManifest manifest) : Package(packageName, manifest) {
        public override bool Exists => true;
        protected override PackageManifest LoadManifest() => throw new NotImplementedException();
        public override string GetDescriptionString() => throw new NotImplementedException();
    }

    private static string ResolveUrl(PackageSource source) {
        const string manifestStr = "@{Private = $true; Version = \"1.2.3\"}";
        return source.EvaluateUrl(new TestPackage("test", new PackageManifest(manifestStr, "test")));
    }

    private static void InvokeWithNewRunspace(Action cb) {
        var origRunspace = Runspace.DefaultRunspace;
        Runspace.DefaultRunspace = RunspaceFactory.CreateRunspace();
        try {
            Runspace.DefaultRunspace.Open();
            cb();
        } finally {
            Runspace.DefaultRunspace.Dispose();
            Runspace.DefaultRunspace = origRunspace;
        }
    }

    [Fact]
    public void TestUrlSbCommandInvocation() {
        TestShouldThrow("& {}");
        TestShouldThrow("ls");
        TestShouldThrow("\"something $(ls)\"");
    }

    [Fact]
    public void TestUrlSbUninitializedVariable() {
        TestShouldThrow("$x + 1");
        TestShouldThrow("$x[0]");
        TestShouldThrow("$x.Member");
        TestShouldThrow("\"$x\"");
    }

    [Fact]
    public void TestUrlSbNonLocalVariable() {
        TestShouldThrow("$global:x = 1");
        TestShouldThrow("$script:x = 1");
        TestShouldThrow("$env:x = 1");
    }

    [Fact]
    public void TestUrlSbInitializedVariable() {
        InvokeWithNewRunspace(() => {
            TestResult("2", "$x = 1; [string]($x + 1)");
            TestResult("1", "$x = 1; [string]$x[0]");
            TestResult("est", "$x = 'test'; $x.Substring(1)");
            TestResult("something/1.2.3/1.2.3/suffix", "$V = $this.Version; \"something/$V/$V/suffix\"");
            TestResult("something 1.2.3", "\"something $($this.Version)\"");
        });
    }

    [Fact]
    public void TestUrlSbVariableCasing() {
        InvokeWithNewRunspace(() => TestResult("test", "$var = 'test'; $VAR"));
    }

    [Fact]
    public void TestUrlSbOperators() {
        InvokeWithNewRunspace(() => TestResult("xesx", "'test' -replace 't', 'x'"));
    }

    [Fact]
    public void TestUrlSbWrongReturnType() {
        InvokeWithNewRunspace(() => {
            TestShouldThrow("$null");
            TestShouldThrow("1");
        });
    }

    [Fact]
    public void TestUrlSbStrictMode() {
        InvokeWithNewRunspace(() => {
            Assert.Throws<InvalidPackageManifestUrlScriptBlockException>(() => {
                // should throw due to strict mode
                ResolveUrl(_installParams with {Url = ScriptBlock.Create("\"t\".NonExistent")});
            });
        });
    }
}