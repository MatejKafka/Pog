using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Pog.Commands.Common;
using Xunit;

namespace Pog.Tests.Commands.Common;

public class DynamicCommandParametersTests : IDisposable {
    private readonly Runspace _runspace = RunspaceFactory.CreateRunspace();

    public DynamicCommandParametersTests() {
        _runspace.Open();

        PowerShell.Create(_runspace)
                .AddCommand("New-Module").AddArgument("TestModule").AddArgument(ScriptBlock.Create(ModuleText))
                // ensure `DynamicCommandParameters` is available in the module
                .AddStatement().AddCommand("Import-Module").AddParameter(typeof(DynamicCommandParameters).Assembly.Location)
                .AddStatement().AddScript(ValidateScriptProxyFnText)
                .AddStatement().AddScript(ArgumentCompleterProxyFnText)
                .Invoke();
    }

    [Fact]
    public void TestValidateScriptScoping() {
        using var ps = PowerShell.Create(_runspace);
        ps.AddCommand("ValidateScriptProxyFn").AddArgument("argument");

        // ReSharper disable once AccessToDisposedClosure
        var exception = Record.Exception(() => ps.Invoke());
        Assert.Null(exception);
    }

    [Fact]
    public void TestCompletion() {
        using var ps = PowerShell.Create(_runspace);

        Assert.Equal(["test"], GetCompletions(ps, "ArgumentCompleterProxyFn test "));
    }

    private static IEnumerable<string> GetCompletions(PowerShell ps, string text) {
        return CommandCompletion.CompleteInput(text, text.Length, null, ps).CompletionMatches
                .Select(m => m.CompletionText);
    }

    public void Dispose() {
        _runspace.Dispose();
    }

    private const string ModuleText =
            """
            function Validate {
                return $true
            }

            # test that the scriptblock scope is correctly scoped
            function ValidateScriptFn([ValidateScript({& Validate})]$Param) {
                echo $Param
            }

            function ArgumentCompleterFn {
                param(
                    $Param1,
                    # test that the completer sees the original param name
                    [ArgumentCompleter({
                        param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameters)
                        return $fakeBoundParameters["Param1"]
                    })]
                    $Param2
                )
                
                echo $Param1
                echo $Param2
            }

            Export-ModuleMember -Function ValidateScriptFn, ArgumentCompleterFn
            """;

    private const string ValidateScriptProxyFnText =
            """
            function ValidateScriptProxyFn {
                [CmdletBinding()]
                param()
                dynamicparam {
                    $ParamBuilder = [Pog.Commands.Common.DynamicCommandParameters+Builder]::new("Prefix", "None", {})
                    $CopiedParams = $ParamBuilder.CopyParameters((Get-Command ValidateScriptFn))
                    return $CopiedParams
                }
            }
            """;

    private const string ArgumentCompleterProxyFnText =
            """
            function ArgumentCompleterProxyFn {
                [CmdletBinding()]
                param()
                dynamicparam {
                    $ParamBuilder = [Pog.Commands.Common.DynamicCommandParameters+Builder]::new("Prefix", "None", {})
                    $CopiedParams = $ParamBuilder.CopyParameters((Get-Command ArgumentCompleterFn))
                    return $CopiedParams
                }
            }
            """;
}