@{
    Rules = @{
        PSAvoidUsingCmdletAliases = @{
            allowlist = "cd", "ls", "rm", "echo", "select", "sort", "group", "?", "%"
        }
        PSUseCompatibleCmdlets = @{
            # warn if using Core-only or Desktop-only cmdlets
            compatibility = "desktop-5.1.14393.206-windows", "core-6.1.0-windows"
        }
    }

    ExcludeRules = @(
        "PSAvoidUsingWriteHost"
        "PSAvoidUsingPositionalParameters"
        "PSAvoidUsingEmptyCatchBlock"

        "PSUseBOMForUnicodeEncodedFile"
        "PSUseShouldProcessForStateChangingFunctions"
    )
}