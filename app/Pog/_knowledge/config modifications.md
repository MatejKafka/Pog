# Cmdlets for modifying config files

Probably integrate with `Assert-File`. Ideally, there would be a single cmdlet, which would detect the file type according to the extension (with override using a parameter) and call a script block, which will do the necessary updates. The cmdlet will then diff the original/new structure (or use a change event handler, e.g. for XML) and if there were changes, prints a message and updates the file.

## XML

Example based on the `SyncTrayzor` package manifest:

```powershell
$Config = [xml]::new()
$Config.PreserveWhitespace = $true
$Config.Load($ConfigPath)

$Changed = $false
$Handler = {
	param($Sender, $Event)
	if ($Event.OldValue -ne $Event.NewValue) {
		Set-Variable -Scope 1 Changed $true
		# TODO: probably also use Write-Debug to print what change ocurred
	}
}
$Config.add_NodeChanged($Handler)
$Config.add_NodeInserted($Handler)
$Config.add_NodeRemoved($Handler)

# now, call the passed scriptblock to make the changes

if ($Changed) {
	$Config.Save($ConfigPath)
	Write-Information "..."
} else {
	Write-Verbose "... nochange ..."
}
```

