## 7zip

(2 MB, 56 iterations, https://www.7-zip.org/a/7z2500-x64.exe)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    0.10 |   0.00 |   0.10 |
| Start-BitsTransfer |    0.23 |   0.01 |   0.23 |
| Test-FileDownload  |    0.26 |   0.01 |   0.26 |
| curl               |    0.36 |   0.03 |   0.35 |
| aria2c             |    0.47 |   0.04 |   0.48 |
| aria2c -x 4        |    0.47 |   0.04 |   0.48 |

## MobaXterm

(25 MB, 56 iterations, https://download.mobatek.net/2032020060430358/MobaXterm_Portable_v20.3.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    0.94 |   0.41 |   0.81 |
| curl               |    0.90 |   0.14 |   0.85 |
| Test-FileDownload  |    1.06 |   0.54 |   0.86 |
| aria2c -x 4        |    1.08 |   0.41 |   0.97 |
| Start-BitsTransfer |    1.27 |   0.39 |   0.97 |
| aria2c             |    1.48 |   0.93 |   1.02 |

## go-lang

(83 MB, 56 iterations, https://go.dev/dl/go1.24.5.windows-amd64.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    2.32 |   0.22 |   2.24 |
| curl               |    2.47 |   0.20 |   2.38 |
| Test-FileDownload  |    2.49 |   0.19 |   2.41 |
| aria2c -x 4        |    2.51 |   0.10 |   2.50 |
| aria2c             |    2.71 |   0.21 |   2.64 |
| Start-BitsTransfer |    4.17 |   0.69 |   4.50 |

## Azure CLI

(86 MB, 56 iterations, https://github.com/Azure/azure-cli/releases/download/azure-cli-2.75.0/azure-cli-2.75.0-x64.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    2.12 |   0.10 |   2.14 |
| curl               |    2.36 |   0.11 |   2.35 |
| Test-FileDownload  |    2.34 |   0.11 |   2.35 |
| aria2c             |    2.60 |   0.15 |   2.61 |
| aria2c -x 4        |    2.73 |   0.14 |   2.73 |
| Start-BitsTransfer |    2.73 |   0.18 |   2.78 |

## VS Code

(149 MB, 42 iterations, https://update.code.visualstudio.com/1.102.1/win32-x64-archive/stable)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    3.75 |   0.13 |   3.80 |
| curl               |    3.90 |   0.21 |   3.88 |
| Test-FileDownload  |    3.96 |   0.17 |   3.94 |
| aria2c -x 4        |    4.18 |   0.16 |   4.16 |
| aria2c             |    4.37 |   0.19 |   4.35 |
| Start-BitsTransfer |    4.09 |   0.46 |   4.48 |

## Ghidra

(403 MB, 21 iterations, https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_11.1.2_build/ghidra_11.1.2_PUBLIC_20240709.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| aria2c -x 4        |   10.05 |   0.39 |   9.95 |
| curl               |    9.96 |   0.34 |  10.00 |
| Test-FileDownload  |   10.22 |   0.49 |  10.10 |
| Invoke-RestMethod  |   10.13 |   0.67 |  10.15 |
| Start-BitsTransfer |   10.63 |   0.36 |  10.55 |
| aria2c             |   10.69 |   0.45 |  10.62 |