@{
    'Rules' = @{
        'PSAvoidUsingCmdletAliases' = @{
            'allowlist' = @('cd', 'ls', 'rm', 'echo', 'select', 'sort', '?', '%')
        }
    }

    ExcludeRules = @('PSAvoidUsingWriteHost', 'PSAvoidUsingPositionalParameters')
}