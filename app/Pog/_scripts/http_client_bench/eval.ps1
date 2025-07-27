param(
    ### Output from the benchmark script.
    [Parameter(Mandatory)]
    $TestData
)

Set-StrictMode -Version 3
$ErrorActionPreference = "Stop"

function Median([array]$Arr) {
    $Half = [math]::DivRem($Arr.Count, 2)[0]
    if ($Arr.Count % 2) {
        $Arr[$Half]
    } else {
        ($Arr[$Half - 1] + $Arr[$Half]) / 2
    }
}

$TestData | group Package | sort {$_.Group[0].Size} | % {
    $Package = $_.Name
    $Url = $_.Group[0].Url
    $Size = $_.Group[0].Size
    $Iterations = $_.Group | group Name | % Count | select -Unique

    $Results = $_.Group | group name | % {
        # cut off bottom and top 10% of measurements
        $SkippedIters = [math]::DivRem($Iterations, 10)[0]
        $Iters = $_.Group.Duration | sort | select -Skip $SkippedIters -SkipLast $SkippedIters
        $Iters | measure -AllStats `
            | Add-Member Median (Median $Iters) -PassThru `
            | Add-Member Name $_.Name -PassThru `
            | Add-Member Package $Package -PassThru `
            | Add-Member Size $_.Group[0].Size -PassThru `
            | Add-Member Duration $_.Group.Duration -PassThru
    } | sort Median

    Write-Host "## $Package"
    Write-Host ""
    Write-Host "($([math]::Round($Size / 1MB)) MB, $($Iterations -join "/") iterations, $Url)"
    Write-Host ""

    $Results | select Name, Average, @{n = "StdDev"; e = {$_.StandardDeviation}}, Median | Format-MarkdownTable | Out-Host
    Write-Host ""

    return $Results
}