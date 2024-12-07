param(
		[string[]]
	$PackageName = $null
)

function addIcon($Dir) {
    $Dir = Get-Item $Dir

    $Shortcut = if (Test-Path "$Dir\$($Dir.Name).lnk") {
        Get-Item "$Dir\$($Dir.Name).lnk"
    } else {
        ls $Dir -Filter *.lnk
    }

    if (@($Shortcut).Count -eq 0) {
        throw "No shortcuts."
    } elseif (@($Shortcut).Count -gt 1) {
		throw "Multiple shortcuts."
    }

    $s = Get-Shortcut $Shortcut
    $i = $s.IconLocation.LastIndexOf(",")
    $path = $s.IconLocation.Substring(0, $i)
    $index = $s.IconLocation.Substring($i + 1)

    Set-Content "$Dir\Desktop.ini" -Force -Value @"
[.ShellClassInfo]
ConfirmFileOp=0
IconFile=$([System.IO.Path]::GetRelativePath($Dir, $path))
IconIndex=$index
"@

	$DesktopIniFile = Get-Item "$Dir\Desktop.ini" -Force
    # make Desktop.ini hidden
    $DesktopIniFile.Attributes = $DesktopIniFile.Attributes -bor [System.IO.FileAttributes]::Hidden
    # make the package directory read-only (otherwise, explorer.exe would ignore Desktop.ini)
    $Dir.Attributes = $Dir.Attributes -bor [System.IO.FileAttributes]::ReadOnly
}


Import-Module Pog
foreach ($p in Get-Pog $PackageName) {
	try {addIcon $p.Path}
	catch {Write-Warning ($p.PackageName + ": $_")}
}
