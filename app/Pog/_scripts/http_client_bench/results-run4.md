## 7zip

(2 MB, 140 iterations, https://www.7-zip.org/a/7z2500-x64.exe)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Start-BitsTransfer |    0.27 |   0.12 |   0.23 |
| Test-FileDownload  |    0.28 |   0.05 |   0.26 |
| Invoke-RestMethod  |    0.28 |   0.06 |   0.26 |
| curl               |    0.39 |   0.06 |   0.37 |
| aria2c             |    0.49 |   0.06 |   0.49 |
| aria2c -x 4        |    0.50 |   0.07 |   0.50 |

## MobaXterm

(25 MB, 140 iterations, https://download.mobatek.net/2032020060430358/MobaXterm_Portable_v20.3.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    1.21 |   0.68 |   0.97 |
| curl               |    1.17 |   0.58 |   0.98 |
| Test-FileDownload  |    1.16 |   0.51 |   1.01 |
| aria2c -x 4        |    1.26 |   0.59 |   1.05 |
| aria2c             |    1.21 |   0.51 |   1.08 |
| Start-BitsTransfer |    1.62 |   0.66 |   1.46 |

## go-lang

(83 MB, 140 iterations, https://go.dev/dl/go1.24.5.windows-amd64.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    2.36 |   0.16 |   2.31 |
| curl               |    2.44 |   0.22 |   2.34 |
| Test-FileDownload  |    2.45 |   0.18 |   2.39 |
| aria2c -x 4        |    2.46 |   0.07 |   2.45 |
| aria2c             |    2.72 |   0.20 |   2.63 |
| Start-BitsTransfer |    3.32 |   0.38 |   3.58 |

## Azure CLI

(86 MB, 140 iterations, https://github.com/Azure/azure-cli/releases/download/azure-cli-2.75.0/azure-cli-2.75.0-x64.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    2.38 |   0.18 |   2.34 |
| curl               |    2.51 |   0.21 |   2.50 |
| Test-FileDownload  |    2.52 |   0.22 |   2.50 |
| aria2c             |    2.82 |   0.20 |   2.78 |
| Start-BitsTransfer |    2.89 |   0.27 |   2.78 |
| aria2c -x 4        |    2.93 |   0.23 |   2.88 |

## VS Code

(149 MB, 140 iterations, https://update.code.visualstudio.com/1.102.1/win32-x64-archive/stable)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    3.93 |   0.44 |   3.98 |
| curl               |    3.96 |   0.42 |   4.01 |
| Test-FileDownload  |    4.05 |   0.44 |   4.06 |
| aria2c -x 4        |    4.23 |   0.32 |   4.25 |
| aria2c             |    4.44 |   0.48 |   4.45 |
| Start-BitsTransfer |    4.10 |   0.45 |   4.48 |

## Ghidra

(403 MB, 70 iterations, https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_11.1.2_build/ghidra_11.1.2_PUBLIC_20240709.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| aria2c -x 4        |   10.49 |   0.39 |  10.43 |
| Invoke-RestMethod  |   10.84 |   0.62 |  10.71 |
| curl               |   11.07 |   0.65 |  10.98 |
| Test-FileDownload  |   11.10 |   0.49 |  11.02 |
| aria2c             |   11.88 |   0.80 |  11.65 |
| Start-BitsTransfer |   11.33 |   0.76 |  12.02 |