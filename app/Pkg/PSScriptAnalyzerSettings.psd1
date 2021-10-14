@{
    'Rules' = @{
        'PSAvoidUsingCmdletAliases' = @{
            'allowlist' = @('cd', 'ls', 'rm', 'echo', 'select', 'sort', '?', '%')
        }
    }
}