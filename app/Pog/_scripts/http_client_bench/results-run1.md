```pwsh
$Results = "7zip", "MobaXterm", "go-lang", "Azure CLI", "VS Code", "Ghidra" | % {
    & '.\http client bench.ps1' -Package $_ -Iterations ($_ -eq "Ghidra" ? 20 : 60)
}
```

## 7zip

(1.5 MB, 40 iterations, https://www.7-zip.org/a/7z2500-x64.exe)

| Name               | Average | StdDev |
| ----               | ------- | ------ |
| Start-BitsTransfer |    0.22 |   0.01 |
| Invoke-RestMethod  |    0.25 |   0.00 |
| Test-FileDownload  |    0.26 |   0.00 |
| curl               |    0.36 |   0.03 |
| aria2c             |    0.88 |   0.03 |
| aria2c -x 4        |    0.88 |   0.03 |

## MobaXterm

(25 MB, 40 iterations, https://download.mobatek.net/2032020060430358/MobaXterm_Portable_v20.3.zip)

| Name               | Average | StdDev |
| ----               | ------- | ------ |
| Invoke-RestMethod  |    0.83 |   0.09 |
| Test-FileDownload  |    0.88 |   0.12 |
| curl               |    0.93 |   0.13 |
| aria2c -x 4        |    0.98 |   0.09 |
| aria2c             |    1.09 |   0.21 |
| Start-BitsTransfer |    1.30 |   0.41 |

## go-lang

(83 MB, 40 iterations, https://go.dev/dl/go1.24.5.windows-amd64.zip)

| Name               | Average | StdDev |
| ----               | ------- | ------ |
| Invoke-RestMethod  |    2.38 |   0.20 |
| curl               |    2.38 |   0.15 |
| aria2c -x 4        |    2.44 |   0.06 |
| Test-FileDownload  |    2.49 |   0.18 |
| aria2c             |    2.70 |   0.19 |
| Start-BitsTransfer |    3.07 |   0.39 |

## Azure CLI

(86 MB, 40 iterations, https://github.com/Azure/azure-cli/releases/download/azure-cli-2.75.0/azure-cli-2.75.0-x64.zip)

| Name               | Average | StdDev |
| ----               | ------- | ------ |
| Invoke-RestMethod  |    2.43 |   0.16 |
| Test-FileDownload  |    2.48 |   0.19 |
| curl               |    2.50 |   0.14 |
| aria2c             |    2.64 |   0.17 |
| aria2c -x 4        |    2.81 |   0.12 |
| Start-BitsTransfer |    3.13 |   0.40 |

## VS Code

(149 MB, 40 iterations, https://update.code.visualstudio.com/1.102.1/win32-x64-archive/stable)

| Name               | Average | StdDev |
| ----               | ------- | ------ |
| Invoke-RestMethod  |    3.67 |   0.27 |
| Start-BitsTransfer |    3.69 |   0.28 |
| curl               |    3.69 |   0.25 |
| Test-FileDownload  |    3.86 |   0.22 |
| aria2c             |    4.05 |   0.36 |
| aria2c -x 4        |    4.09 |   0.21 |

## Ghidra

(403 MB, 10 iterations, https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_11.1.2_build/ghidra_11.1.2_PUBLIC_20240709.zip)

| Name               | Average | StdDev |
| ----               | ------- | ------ |
| Invoke-RestMethod  |   10.53 |   0.50 |
| aria2c -x 4        |   10.61 |   0.46 |
| Test-FileDownload  |   10.92 |   0.45 |
| aria2c             |   11.34 |   0.96 |
| curl               |   12.07 |   4.73 |
| Start-BitsTransfer |   13.33 |   1.57 |