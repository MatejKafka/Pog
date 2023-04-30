function addIcon($Dir) {
    $Dir = Resolve-Path $Dir
    $Shortcuts = ls $Dir -Filter *.lnk
    if (@($Shortcuts).Count -eq 0) {
        throw "No shortcuts."
    } elseif (@($Shortcuts).Count -gt 1) {
        throw "Multiple shortcuts."
    }

    $s = Get-Shortcut $Shortcuts[0]
    $i = $s.IconLocation.LastIndexOf(",")
    $path = $s.IconLocation.Substring(0, $i)
    $index = $s.IconLocation.Substring($i + 1)

    $DesktopIniFile = New-Item "$Dir\Desktop.ini" -Force -Value @"
[.ShellClassInfo]
ConfirmFileOp=0
IconFile=$([System.IO.Path]::GetRelativePath($Dir, $path))
IconIndex=$index
"@
    # make Desktop.ini hidden
    $DesktopIniFile.Attributes = $DesktopIniFile.Attributes -bor [System.IO.FileAttributes]::Hidden
    # make the package directory read-only (otherwise, explorer.exe would ignore Desktop.ini
    $d = Get-Item $Dir
    $d.Attributes = $d.Attributes -bor [System.IO.FileAttributes]::ReadOnly
}