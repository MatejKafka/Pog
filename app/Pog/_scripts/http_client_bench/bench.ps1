param(
    [Parameter(Mandatory)]
    $Package,

    [int]
    $Iterations = 40
)

Set-StrictMode -Version 3
$ErrorActionPreference = "Stop"

$ProgressPreference = "Ignore"

$Path = "$env:TEMP\PogHttpBench-$Package-$(New-Guid).zip"
$Url = (Find-Pog $Package).Manifest.EvaluateInstallUrls().Url
$Sbs = @{
    "Invoke-RestMethod" = {Invoke-RestMethod -Uri $Url -OutFile $Path}
    "Start-BitsTransfer" = {Start-BitsTransfer $Url -Destination $Path}
    "aria2c" = {aria2c.exe --quiet --dir (Split-Path $Path) --out (Split-Path -Leaf $Path) $Url}
    "aria2c -x 4" = {aria2c.exe --quiet -x 4 --dir (Split-Path $Path) --out (Split-Path -Leaf $Path) $Url}
    "curl" = {curl.exe -s -L $Url --output $Path}
    "Test-FileDownload" = {
        # custom naive function wrapping HttpClient.GetAsync(...), res.Content.CopyToAsync(fs)
        $client = [System.Net.Http.HttpClient]::new();
        try {
            $fs = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None, 4096, $true)
            try {
                $Result = $client.GetAsync($Url).GetAwaiter().GetResult()
                try {
                    $null = $Result.Content.CopyToAsync($fs).GetAwaiter().GetResult()
                } finally {
                    $Result.Dispose()
                }
            } finally {
                $fs.Dispose()
            }
        } finally {
            $client.Dispose()
        }
    }
}

1..$Iterations | % {
    $Iter = $_
    $Sbs.GetEnumerator() | sort {Get-Random}
} | % {
    Write-Progress -PercentComplete (($Iter - 1) / $Iterations * 100) -Activity "Benchmark" -Status "Iteration ${Iter}: $($_.Key)"

    for ($i = 0;; $i++) {
        try {
            $Duration = Measure-Command $_.Value | % TotalSeconds
            $Size = (gi $Path).Length
            break
        } catch {
            if ($i -ge 5) {
                throw # too many retries
            }

            Write-Warning "Bench failed, retrying after 10 seconds: $_"
            sleep 10
            continue
        } finally {
            rm $Path -ErrorAction Ignore
        }
    }

    [pscustomobject]@{
        Name = $_.Key
        Package = $Package
        Size = $Size
        Duration = $Duration
        Url = $Url
    }
}

Write-Progress -Activity "Benchmark" -Completed