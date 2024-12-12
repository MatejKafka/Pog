### Runs selected Pog release inside a clean Windows Sandbox instance.
param(
    [Parameter(Mandatory)][string]$ReleasePath,
    [Parameter(DontShow)][switch]$_InSandbox
)

$ReleasePath = [string](Resolve-Path $ReleasePath)


# it would be nicer to run Pog under a non-admin (the default account in Sandbox is an admin), but we cannot interactively
#  log into another account, and BITS will error out when launched from a non-interactive session; sigh

if ($_InSandbox) {
    # enable developer mode
    $null = reg add "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /f /v "AllowDevelopmentWithoutDevLicense" /d "1"

    $null = mkdir $env:USERPROFILE/Desktop/Pog-test
    cd $env:USERPROFILE/Desktop/Pog-test
    # unpack tested Pog release; tar is much faster than Expand-Archive
    tar -xf $ReleasePath

    .\Pog\setup.ps1
} else {
    # we map the whole parent dir into Sandbox, which is not exactly nice, but it works and it's easy
    $MappedDir = Split-Path $ReleasePath
    $SandboxReleasePath = "C:\Pog-release\$(Split-Path -Leaf $ReleasePath)"
    $SandboxConfig = @"
<Configuration>
    <MappedFolders>
        <MappedFolder>
            <HostFolder>${MappedDir}</HostFolder>
            <SandboxFolder>C:\Pog-release</SandboxFolder>
            <ReadOnly>true</ReadOnly>
        </MappedFolder>
        <MappedFolder>
            <HostFolder>${PSScriptRoot}</HostFolder>
            <SandboxFolder>C:\Pog-scripts</SandboxFolder>
            <ReadOnly>true</ReadOnly>
        </MappedFolder>
    </MappedFolders>

    <LogonCommand>
        <Command>cmd /c start powershell -NoProfile -NoExit -ExecutionPolicy Bypass "C:\Pog-scripts\$(Split-Path -Leaf $PSCommandPath)" "${SandboxReleasePath}" -_InSandbox</Command>
    </LogonCommand>

    <vGPU>Enable</vGPU>
    <Networking>Default</Networking>
    <AudioInput>Disable</AudioInput>
    <VideoInput>Disable</VideoInput>
    <PrinterRedirection>Disable</PrinterRedirection>
    <ClipboardRedirection>Default</ClipboardRedirection>
</Configuration>
"@

    try {
        Set-Content $env:TEMP\pog-sandbox.wsb $SandboxConfig
        Start-Process $env:TEMP\pog-sandbox.wsb -Wait
    } finally {
        rm -Force -ErrorAction Ignore $env:TEMP\pog-sandbox.wsb
    }
}
