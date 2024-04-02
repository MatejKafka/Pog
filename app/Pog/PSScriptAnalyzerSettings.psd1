@{
    'Rules' = @{
        'PSAvoidUsingCmdletAliases' = @{
            'allowlist' = @('cd', 'ls', 'gi', 'rm', 'echo', 'select', 'sort', '?', '%')
        }
    }

    ExcludeRules = @('PSAvoidUsingWriteHost', 'PSAvoidUsingPositionalParameters', 'PSAvoidUsingEmptyCatchBlock')
}