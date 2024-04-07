<# Runs selected Pog release inside a clean Windows Sandbox instance. #>
param([Parameter(Mandatory)][string]$Version, [Parameter(DontShow)][switch]$_InSandbox)

# it would be nicer to run Pog under a non-admin (the default account in Sandbox is an admin), but we cannot interactively
#  log into another account, and BITS will error out when launched from a non-interactive session; sigh

if ($_InSandbox) {
    # enable developer mode
    reg add "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /t REG_DWORD /f /v "AllowDevelopmentWithoutDevLicense" /d "1"

    $null = mkdir $env:USERPROFILE/Desktop/Pog-test
    cd $env:USERPROFILE/Desktop/Pog-test
    # unpack tested Pog release; tar is much faster than Expand-Archive
    tar -xf C:\Pog-releases\Pog-v$Version.zip

    .\Pog\setup.ps1
} else {
    $Root = Resolve-Path (git -C $PSScriptRoot rev-parse --show-toplevel)
    $SandboxConfig = @"
<Configuration>
    <MappedFolders>
        <MappedFolder>
            <HostFolder>${Root}\_releases</HostFolder>
            <SandboxFolder>C:\Pog-releases</SandboxFolder>
            <ReadOnly>true</ReadOnly>
        </MappedFolder>
        <MappedFolder>
            <HostFolder>${PSScriptRoot}</HostFolder>
            <SandboxFolder>C:\Pog-scripts</SandboxFolder>
            <ReadOnly>true</ReadOnly>
        </MappedFolder>
    </MappedFolders>

    <LogonCommand>
        <Command>cmd /c start powershell -NoProfile -NoExit -ExecutionPolicy Bypass "C:\Pog-scripts\$(Split-Path -Leaf $PSCommandPath)" "$Version" -_InSandbox</Command>
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
