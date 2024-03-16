using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Reflection;

namespace Pog.Commands.Common;

// ported from PowerShell, with some modifications:
// https://social.technet.microsoft.com/Forums/en-US/21fb4dd5-360d-4c76-8afc-1ad0bd3ff71a/reuse-function-parameters
public class DynamicCommandParameters : RuntimeDefinedParameterDictionary {
    private readonly int _namePrefixLength;

    private const BindingFlags NonPublicFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly PropertyInfo SsInternalProperty =
            typeof(SessionState).GetProperty("Internal", NonPublicFlags)!;
    private static readonly PropertyInfo SbSessionStateInternalProperty =
            typeof(ScriptBlock).GetProperty("SessionStateInternal", NonPublicFlags)!;
    private static readonly PropertyInfo SbParamsProperty =
            typeof(ScriptBlock).GetProperty("RuntimeDefinedParameters", NonPublicFlags)!;

    /** Creates an empty parameter dictionary. */
    public DynamicCommandParameters() {
        _namePrefixLength = 0;
    }

    private DynamicCommandParameters(string namePrefix) {
        _namePrefixLength = namePrefix.Length;
    }

    public Hashtable Extract() {
        var extracted = new Hashtable();
        foreach (var param in Values) {
            if (!param.IsSet) continue;
            extracted[param.Name.Substring(_namePrefixLength)] = param.Value;
        }
        return extracted;
    }

    private static readonly HashSet<string> CommonParameterNames =
            new(typeof(CommonParameters).GetProperties().Select(p => p.Name));

    [Flags]
    public enum ParameterCopyFlags { None = 0, NoAlias = 1, NoPosition = 2, NoMandatory = 4, }

    /// Unifies <see cref="ParameterMetadata"/> and <see cref="RuntimeDefinedParameter"/>.
    private record ParameterWrapper(string Name, Type ParameterType, Collection<Attribute> Attributes);

    public record Builder(
            string NamePrefix = "",
            ParameterCopyFlags CopyFlags = ParameterCopyFlags.None,
            Func<string, Attribute, Attribute?>? UnknownAttributeHandler = null) {
        public DynamicCommandParameters CopyParameters(ScriptBlock scriptBlock) {
            // no public way to read the parameters of a ScriptBlock, other than parsing the AST manually
            var parameters = (RuntimeDefinedParameterDictionary) SbParamsProperty.GetValue(scriptBlock);

            var paramIterator = parameters.Values.Select(p => new ParameterWrapper(p.Name, p.ParameterType, p.Attributes));
            return CopyParameters(paramIterator, scriptBlock.Module);
        }

        public DynamicCommandParameters CopyParameters(CommandInfo commandInfo) {
            while (commandInfo is AliasInfo ai) {
                // resolve alias
                commandInfo = ai.ResolvedCommand;
            }

            var paramDict = commandInfo.Parameters;
            if (paramDict == null) {
                throw new ArgumentException($"Cannot copy parameters from command '{commandInfo.Name}', " +
                                            $"no parameters are accessible (this may happen e.g. for native executables).");
            }

            // module context is used to set correct scope for attributes taking a scriptblock like ValidateScript and ArgumentCompleter
            // this is only really relevant for functions (cmdlets shouldn't have problems with scope, attributes in script param() block
            //  cannot refer to things inside the script, native executables don't have visible parameters)
            var moduleCtx = commandInfo switch {
                // you might wonder why the same branch is repeated 3 times; well, the IScriptCommandInfo interface is internal...
                ScriptInfo ci => ci.ScriptBlock.Module,
                ExternalScriptInfo ci => ci.ScriptBlock.Module,
                FunctionInfo ci => ci.ScriptBlock.Module,
                _ => null,
            };

            var paramIterator = paramDict.Values.Select(p => new ParameterWrapper(p.Name, p.ParameterType, p.Attributes));
            return CopyParameters(paramIterator, moduleCtx);
        }

        private DynamicCommandParameters CopyParameters(
                IEnumerable<ParameterWrapper> allParameters, PSModuleInfo? moduleContext) {
            // get non-common parameters
            var parameters = allParameters.Where(p => !CommonParameterNames.Contains(p.Name)).ToArray();
            var paramNameSet = new HashSet<string>(parameters.Select(p => p.Name));

            var dynParams = new DynamicCommandParameters(NamePrefix);
            foreach (var param in parameters) {
                var attributes = new Collection<Attribute>();
                foreach (var origAttr in param.Attributes) {
                    // map parameter attributes
                    var attr = origAttr switch {
                        AliasAttribute a => CopyAliasAttribute(a, NamePrefix, CopyFlags),
                        ArgumentCompleterAttribute b =>
                                CopyArgumentCompleterAttribute(b, NamePrefix, paramNameSet),
                        ValidateScriptAttribute c => CopyValidateScriptAttribute(c, moduleContext),
                        ParameterAttribute g => CopyParameterAttribute(g, CopyFlags),
                        ValidateArgumentsAttribute
                                or AllowNullAttribute
                                or AllowEmptyStringAttribute
                                or AllowEmptyCollectionAttribute
                                or CredentialAttribute
                                or ArgumentTransformationAttribute // I think this should be safe to forward, not sure
                                => origAttr,

                        // don't have access to the attributes, just match the namespace string
                        // whole namespace is matched because ILRepack prefixes classes with UUIDs
                        // TODO: is this ok? seems a bit like shotgunning
                        _ => origAttr.GetType().FullName?.StartsWith("System.Runtime.CompilerServices.") ?? false
                                ? origAttr
                                : UnknownAttributeHandler?.Invoke(param.Name, origAttr),
                    };

                    if (attr != null) {
                        attributes.Add(attr);
                    }
                }

                var paramName = NamePrefix + param.Name;
                dynParams.Add(paramName, new RuntimeDefinedParameter(paramName, param.ParameterType, attributes));
            }

            return dynParams;
        }
    }

    private static AliasAttribute? CopyAliasAttribute(AliasAttribute attr, string namePrefix, ParameterCopyFlags flags) {
        if ((flags & ParameterCopyFlags.NoAlias) != 0) {
            return null;
        } else {
            // add namePrefix to aliases
            if (namePrefix == "") return attr;
            else return new AliasAttribute(attr.AliasNames.Select(n => namePrefix + n).ToArray());
        }
    }

    private static ArgumentCompleterAttribute CopyArgumentCompleterAttribute(ArgumentCompleterAttribute attr,
            string namePrefix, HashSet<string> paramNameSet) {
        if (namePrefix == "") {
            return attr;
        }

        // the completer will often refer to values of other already bound parameters; however, when -NamePrefix is set,
        //  the names of the real parameters will be different, so we'll have to translate
        return new ArgumentCompleterAttribute(CreateClosure(ScriptBlock.Create(ArgumentCompleterProxySb), [
            new("NamePrefix", namePrefix),
            new("ParameterNameSet", paramNameSet),
            new("OriginalAttribute", attr),
        ]));
    }

    private static ValidateScriptAttribute CopyValidateScriptAttribute(
            ValidateScriptAttribute attr, PSModuleInfo? moduleCtx) {
        if (moduleCtx == null) {
            return attr;
        } else {
            // bind the scriptblock to the original module context
            // source: Pester (InModuleScope and Pester.Scoping) and https://gist.github.com/nohwnd/0f615f897b1f510beb08ce0cefe48342
            var internalSessionState = SsInternalProperty.GetValue(moduleCtx.SessionState, null);
            // FIXME: this mutates the original ScriptBlock; investigate whether this breaks anything
            SbSessionStateInternalProperty.SetValue(attr.ScriptBlock, internalSessionState, null);
            return attr;
        }
    }

    private static ParameterAttribute CopyParameterAttribute(ParameterAttribute attr, ParameterCopyFlags flags) {
        var p = new ParameterAttribute();

        if ((flags & ParameterCopyFlags.NoMandatory) == 0) p.Mandatory = attr.Mandatory;
        if ((flags & ParameterCopyFlags.NoPosition) == 0) p.Position = attr.Position;

        if (attr.HelpMessage != null) p.HelpMessage = attr.HelpMessage;
        if (attr.HelpMessageBaseName != null) p.HelpMessageBaseName = attr.HelpMessageBaseName;
        if (attr.HelpMessageResourceId != null) p.HelpMessageResourceId = attr.HelpMessageResourceId;

        p.ParameterSetName = attr.ParameterSetName;
        p.DontShow = attr.DontShow;
        p.ValueFromPipeline = attr.ValueFromPipeline;
        p.ValueFromPipelineByPropertyName = attr.ValueFromPipelineByPropertyName;
        p.ValueFromRemainingArguments = attr.ValueFromRemainingArguments;

        return p;
    }

    private static ScriptBlock CreateClosure(ScriptBlock script, List<PSVariable> variables) {
        return (ScriptBlock) ScriptBlock.Create("return $args[0].GetNewClosure()")
                .InvokeWithContext(null, variables, script)[0].BaseObject;
    }

    private const string ArgumentCompleterProxySb =
            """
            [CmdletBinding()]
            param($CmdName, $ParamName, $WordToComplete, $Ast, $BoundParameters)

            $RenamedParameters = @{}
            foreach ($e in $BoundParameters.GetEnumerator()) {
                if ($e.Key.StartsWith($NamePrefix)) {
                    $OrigName = $e.Key.Substring($NamePrefix.Length)
                    if ($OrigName -in $ParameterNameSet) {
                        $RenamedParameters[$OrigName] = $e.Value
                    }
                }
            }

            if ($null -ne $OriginalAttribute.ScriptBlock) {
                return & $OriginalAttribute.ScriptBlock $CmdName $ParamName $WordToComplete $Ast $RenamedParameters
            } else {
                return $OriginalAttribute.Type::new().CompleteArgument($CmdName, $ParamName, $WordToComplete, $Ast, $RenamedParameters)
            }
            """;
}
