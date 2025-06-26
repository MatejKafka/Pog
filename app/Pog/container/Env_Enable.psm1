using module ..\Utils.psm1
. $PSScriptRoot\..\header.ps1

# Pog.dll should be already loaded as an assembly, re-import it to access the cmdlets
# it is imported inside this module so that we can control what we expose to the package
Import-Module ([Pog.InternalState].Assembly.Location)

# this must NOT be an advanced funtion, otherwise we lose error message position from the manifest scriptblock
function __main {
    ### .SYNOPSIS
    ### This function is called after the container setup is finished to run the Enable script.
    param([Pog.PackageManifest]$Manifest, $PackageArguments)

    # invoke the entry point
    & (New-ContainerModule) $Manifest.Enable @PackageArguments
}


function New-ParentDirectory {
    param(
            [Parameter(Mandatory)]
        $Path
    )

    $Parent = Split-Path -Parent $Path
    if (-not (Test-Path $Parent)) {
        $null = New-Item -ItemType Directory $Parent
    }
}

function MergeDirectories {
    param(
            [Parameter(Mandatory)]
        $SrcDir,
            [Parameter(Mandatory)]
        $TargetDir,
            # when set, target will be left without overwriting in case of collision
            [switch]
        $PreferTarget
    )

    ls -Force $SrcDir | % {
        $Target = $TargetDir + "\" + $_.Name
        if (Test-Path $Target) {
            if ($PreferTarget) {return}  # skip
            # overwrite with new version
            Remove-Item -Recurse -LiteralPath $Target
        }
        Move-Item $_.FullName $Target
    }
    Remove-Item -Recurse -LiteralPath $SrcDir
}

function Set-Symlink {
    param(
            # This path must be either non-existent, or already a symlink.
            [Parameter(Mandatory)]
        $LinkPath,
            # Path to target, must exist.
            [Parameter(Mandatory)]
        $TargetPath
    )

    # note that this returns the provider path (real FS path), not the PSDrive path
    $LinkAbsPath = Resolve-VirtualPath $LinkPath
    # this one must exist, FullName is also a real FS path
    $Target = Get-Item $TargetPath

    [string]$TargetStr = if ([System.IO.Path]::IsPathRooted($LinkPath) -or [System.IO.Path]::IsPathRooted($TargetPath)) {
        # one of the paths is rooted, use absolute path for symlink
        Write-Debug "Using absolute path as symlink target."
        [string]$Target
    } else {
        # get relative path from $LinkPath to $TargetPath for symlink
        # use parent of $LinkPath, as relative symlinks are resolved from parent dir
        [Pog.Utils.FsUtils]::GetRelativePath((Split-Path $LinkAbsPath), $Target)
    }

    if (Test-Path $LinkAbsPath) {
        if ($TargetStr -eq [Pog.Utils.FsUtils]::GetSymbolicLinkTarget($LinkAbsPath)) {
            return $null # we already have a correct symlink
        }

        # not a correct item, delete and recreate
        Remove-Item -Recurse -Force -LiteralPath $LinkAbsPath
    } else {
        New-ParentDirectory $LinkAbsPath
    }

    Write-Debug "Creating symlink from '$LinkAbsPath' with target '$TargetStr'."
    # New-Item -Type SymbolicLink has a dumb issue with relative paths, so we bypass it
    #  https://github.com/PowerShell/PowerShell/issues/15235
    return [Pog.Utils.FsUtils]::CreateSymbolicLink($LinkAbsPath, $TargetStr, $Target.PSIsContainer)
}

enum ItemType {File; Directory}
<#
    What New-Symlink should do:
    if target exists:
        switch source state:
            does not exist:
                - create symlink to target, leave target as-is
            is symlink to target:
                - nothing to do, already set
            is symlink somewhere else:
                - delete, replace
            exists, not symlink:
                - if -Merge was passed, merge source dir to target
                - remove source, replace with symlink
    else:
        switch source state:
            is symlink:
                - delete source
                - create empty target
            - move source to target
            - create symlink at source
        else:

#>
function New-Symlink {
    ### .SYNOPSIS
    ### Ensure that a symbolic link exists at the passed path and that the target exists.
    [CmdletBinding()]
    param(
            ### Path where the symbolic link is created.
            [Parameter(Mandatory)]
            [string]
        $OriginalPath,
            ### The target path of the symbolic link, relative to the package directory (not to the symbolic link).
            [Parameter(Mandatory)]
            [string]
        $TargetPath,
            ### The type of the symbolic link target – either `File` or `Directory`.
            [Parameter(Mandatory)]
            [Alias("Type")]
            [ItemType]
        $ItemType,

            ### If this switch is passed, -ItemType is a Directory and -OriginalPath is a directory instead of a symbolic link,
            ### files and directories from -OriginalPath are moved to -TargetPath.
            ###
            ### This is useful in cases where you symlink a plugin directory or similar from the application directory to allow
            ### the user to add custom plugins.
            [switch]
        $Merge
    )

    begin {
        if ($Merge -and $ItemType -ne [ItemType]::Directory) {
            throw "'-Merge' switch for New-Symlink may only be passed when '-ItemType Directory' is set."
        }

        $TestType = switch ($ItemType) {File {"Leaf"}; Directory {"Container"}}
        $OriginalPathIsSymlink = (Test-Path $OriginalPath) -and $null -ne (Get-Item $OriginalPath).LinkType

        # if orig exists and doesn't match expected item type
        if ((Test-Path $OriginalPath) -and -not $OriginalPathIsSymlink -and -not (Test-Path -Type $TestType $OriginalPath)) {
            $OppositeType = switch ($ItemType) {File {[ItemType]::Directory}; Directory {[ItemType]::File}}
            throw "Cannot symlink source path '$OriginalPath' to '$TargetPath' - expected '$ItemType', found '$OppositeType'."
        }

        # OriginalPath is either symlink or matches item type

        if ((Test-Path $TargetPath) -and -not (Test-Path -Type $TestType $TargetPath)) {
            Write-Warning "Item '$TargetPath' exists, but it's not '$ItemType'. Replacing..."
            # mismatch between requested and real target type
            Remove-Item -Recurse -LiteralPath $TargetPath
        }

        # TargetPath matches item type

        if (-not (Test-Path $TargetPath)) {
            Write-Debug "Target does not exist: $TargetPath"
            New-ParentDirectory $TargetPath
            # $OriginalPath exists and it's not a symlink
            if ((Test-Path $OriginalPath) -and -not $OriginalPathIsSymlink) {
                Write-Debug "Moving '$OriginalPath' to '$TargetPath'."
                # TODO: check if $OriginalPath is being used by another process; block if it is
                # move it to target and then create symlink
                Move-Item $OriginalPath $TargetPath
            } else {
                $null = New-Item $TargetPath -ItemType $ItemType
            }
        } elseif ($Merge -and -not $OriginalPathIsSymlink) {
            Write-Information "Merging directory $OriginalPath to $TargetPath..."
            MergeDirectories $OriginalPath $TargetPath
        }

        $result = Set-Symlink $OriginalPath $TargetPath
        if ($null -eq $result) {
            Write-Verbose "Symlink at '$OriginalPath' already exists and matches requested target '$TargetPath'."
        } else {
            Write-Information "Created symlink from '$OriginalPath' to '$TargetPath'."
        }
    }
}


function New-Directory {
    ### .SYNOPSIS
    ### Ensures that the directory at the provided path exists. If the directory does not exist,
    ### it is created. If the path refers to a file, the cmdlet throws an error.
    ### Parent directories are automatically created.
    [CmdletBinding()]
    param(
            ### Path to the created directory.
            [Parameter(Mandatory)]
            [string]
        $Path,
            ### If passed and the directory does not exist, the scriptblock is invoked and receives a `DirectoryInfo`
            ### object for the newly created directory. You may use this scriptblock to create the default content
            ### for the directory.
            [scriptblock]
        $DefaultContent = {param([System.IO.DirectoryInfo]$Dir)}
    )

    if (Test-Path -Type Container $Path) {
        Write-Verbose "Directory '$Path' already exists."
        return
    }
    if (Test-Path $Path) {
        throw "Path '$Path' already exists, but it's not a directory."
    }
    $Dir = New-Item -ItemType Directory $Path
    try {
        & $DefaultContent $Dir
    } catch {
        Remove-Item -Recurse -Force $Dir
    }
    Write-Information "Created directory '$Path'."
}


function New-File {
    ### .SYNOPSIS
    ### Ensures that the file at the provided path exists. If the file does not exist, it is created, otherwise the content
    ### is updated using the scriptblock passed to the -ContentUpdater parameter. If the path refers to a directory, the cmdlet
    ### throws an error. Parent directories are automatically created.
    [CmdletBinding(DefaultParameterSetName="ScriptBlocks")]
    param(
            ### Path to the created file.
            [Parameter(Mandatory, Position=0)]
            [string]
        $Path,

            ### If the file does not exist, the passed scriptblock is invoked and the file is populated with the returned value.
            ### Otherwise, the file is left empty. As an alternative option, the scriptblock may also create and populate the file directly.
            ### The scriptblock receives the absolute path to the file as the first argument and also as pipeline input ($_).
            ###
            ### Instead of a scriptblock, a path to a source file may also be passed, which is copied to populate the file.
            [Parameter(Position=1, ParameterSetName="ScriptBlocks")]
            [ValidateScript({
                if ($_ -is [scriptblock]) {return $true}
                if ($_ -is [string]) {
                    if (Test-Path -Type Leaf $_) {return $true}
                    throw "-DefaultContent is a template path string, but it doesn't point to an existing file: '${_}'"
                }
                throw "-DefaultContent must be either a script block, or a path to an existing template file, got '$($_.GetType())'."
            })]
        $DefaultContent = {},

            ### If the file already exists, this scriptblock is invoked and receives the `FileInfo` object representing the file
            ### as the first argument and also as the pipeline input ($_). This is typically used with configuration files to update
            ### any stored absolute paths to portable directories when the package is moved.
            ###
            ### Note that you must store the modified content of the file inside the scriptblock yourself.
            ### Currently, no ready-made parsers for common formats are provided; for XML, use the built-in [xml] type;
            ### for JSON, use the `ConvertFrom-Json` cmdlet.
            [Parameter(Position=2, ParameterSetName="ScriptBlocks")]
            [scriptblock]
        $ContentUpdater = $null,


            ### If set, the file content is unconditionally overwritten by the output of the passed scriptblock or static string.
            [Parameter(ParameterSetName="FixedContent")]
            [ValidateScript({
                if ($_ -is [scriptblock] -or $_ -is [string]) {return $true}
                throw "-FixedContent must be either a script block, or a string, got '$($_.GetType())'."
            })]
        $FixedContent = $null
    )

    $ResolvedPath = Resolve-VirtualPath $Path

    $FixedContentStr = if ($FixedContent -is [scriptblock]) {
        Invoke-DollarUnder $FixedContent $ResolvedPath $ResolvedPath
    } elseif ($FixedContent -is [string]) {
        $FixedContent
    } else {$null}

    if (Test-Path -Type Leaf $Path) {
        if ($FixedContentStr) {
            if ((Get-Content -Raw $Path) -eq $FixedContentStr) {
                Write-Verbose "File '$Path' exists with already correct content."
            } else {
                # -NoNewline also skips newline between lines, not just the terminating newline
                Set-Content $Path ($FixedContentStr -join "`n") -NoNewline
                Write-Information "File '$Path' updated."
            }
            return
        }

        if ($null -eq $ContentUpdater) {
            Write-Verbose "File '$Path' already exists."
            return
        }

        # if $Path points to a file symlink, work with the target (otherwise the change detection below would not work)
        $ResolvedPath = Resolve-VirtualPath $Path
        $PathTarget = [Pog.Utils.FsUtils]::GetSymbolicLinkTarget($ResolvedPath)
        if ($PathTarget) {
            $ResolvedPath = $PathTarget
        }

        $File = Get-Item $ResolvedPath
        $null = Invoke-DollarUnder $ContentUpdater $File $File

        $WasChanged = $File.LastWriteTime -ne (Get-Item $ResolvedPath).LastWriteTime
        if ($WasChanged) {
            Write-Information "File '$Path' updated."
            Write-Debug ("^ For manifest writers: last write time of the file changed during " +`
                    "-ContentUpdater execution. If you don't think it should have changed " +`
                    "and the file appears to be the same, " +`
                    "check for differences in whitespace (especially \r\n vs \n).")
        } else {
            Write-Verbose "File '$Path' already exists with correct content."
        }
        return
    }

    if (Test-Path $Path) {
        # TODO: think this through; maybe it would be better to unconditionally overwrite it
        throw "Path '$Path' already exists, but it's not a file."
    }

    New-ParentDirectory $Path

    # create new file with default content
    # the generator script $DefaultContent can either create and populate the file directly,
    #  or just return the desired content and we'll create it ourselves
    # the first option is supported, because some apps have a builtin way to generate a default config directly
    $NewContent = if ($FixedContentStr) {$FixedContentStr}
        elseif ($DefaultContent -is [string]) {Copy-Item $DefaultContent $Path}
        else {Invoke-DollarUnder $DefaultContent $ResolvedPath $ResolvedPath}

    if (-not (Test-Path $Path)) {
        Set-Content $Path ($NewContent -join "`n") -NoNewline
    }
    Write-Information "Created file '$Path'."
}


# not sure if we should expose the env cmdlets, Pog, PowerShell and Scoop (private packages) use them
Export-ModuleMember `
    -Cmdlet Add-EnvVar, Set-EnvVar, Export-Command, Export-Shortcut, Disable-DisplayScaling `
    -Function __main, New-File, New-Directory, New-Symlink