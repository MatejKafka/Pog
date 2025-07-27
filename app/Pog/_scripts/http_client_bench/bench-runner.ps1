Set-StrictMode -Version 3
$ErrorActionPreference = "Stop"

1..10 | % {
    Write-Host "iteration $_ started"
    "7zip", "MobaXterm", "go-lang", "Azure CLI", "VS Code", "Ghidra" | % {
        $Iters = if ($_ -eq "Ghidra") {7} else {14}
        & $PSScriptRoot\bench.ps1 $_ -Iterations $Iters
        Write-Host "cool-off"
        sleep 10
    }

    Write-Host "iteration $_ finished"
    sleep 60
} -OutVariable global:Results | Format-Table

$global:Parsed = & $PSScriptRoot\eval.ps1 $global:Results

$s = @{}
$Parsed | group Package | % {
    $_.Group | enumerate | % {$s[$_.Item.Name] = $s[$_.Item.Name] + $_.Index}
}
$s