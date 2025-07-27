## 7zip

(2 MB, 120 iterations, https://www.7-zip.org/a/7z2500-x64.exe)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Start-BitsTransfer |    0.32 |   0.23 |   0.23 |
| Test-FileDownload  |    0.31 |   0.13 |   0.26 |
| Invoke-RestMethod  |    0.33 |   0.14 |   0.26 |
| curl               |    0.39 |   0.07 |   0.36 |
| aria2c -x 4        |    0.63 |   0.18 |   0.52 |
| aria2c             |    0.63 |   0.18 |   0.53 |

## MobaXterm

(25 MB, 120 iterations, https://download.mobatek.net/2032020060430358/MobaXterm_Portable_v20.3.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Test-FileDownload  |    1.23 |   0.67 |   1.00 |
| curl               |    1.19 |   0.57 |   1.01 |
| aria2c -x 4        |    1.25 |   0.47 |   1.06 |
| Invoke-RestMethod  |    1.38 |   0.77 |   1.07 |
| aria2c             |    1.35 |   0.59 |   1.11 |
| Start-BitsTransfer |    1.99 |   1.13 |   1.46 |

## go-lang

(83 MB, 120 iterations, https://go.dev/dl/go1.24.5.windows-amd64.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    2.43 |   0.22 |   2.35 |
| curl               |    2.50 |   0.23 |   2.40 |
| Test-FileDownload  |    2.52 |   0.23 |   2.43 |
| aria2c -x 4        |    2.49 |   0.09 |   2.48 |
| aria2c             |    2.76 |   0.21 |   2.69 |
| Start-BitsTransfer |    3.11 |   0.39 |   2.80 |

## Azure CLI

(86 MB, 120 iterations, https://github.com/Azure/azure-cli/releases/download/azure-cli-2.75.0/azure-cli-2.75.0-x64.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Test-FileDownload  |    2.49 |   0.22 |   2.44 |
| Invoke-RestMethod  |    2.46 |   0.20 |   2.45 |
| curl               |    2.62 |   0.22 |   2.59 |
| Start-BitsTransfer |    3.03 |   0.37 |   2.79 |
| aria2c             |    2.91 |   0.30 |   2.88 |
| aria2c -x 4        |    3.00 |   0.22 |   2.99 |

## VS Code

(149 MB, 120 iterations, https://update.code.visualstudio.com/1.102.1/win32-x64-archive/stable)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Invoke-RestMethod  |    3.65 |   0.45 |   3.61 |
| Test-FileDownload  |    3.75 |   0.42 |   3.71 |
| curl               |    3.74 |   0.51 |   3.75 |
| aria2c -x 4        |    4.03 |   0.34 |   3.91 |
| aria2c             |    4.09 |   0.51 |   4.07 |
| Start-BitsTransfer |    4.13 |   0.44 |   4.48 |

## Ghidra

(403 MB, 40 iterations, https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_11.1.2_build/ghidra_11.1.2_PUBLIC_20240709.zip)

| Name               | Average | StdDev | Median |
| ------------------ | ------- | ------ | ------ |
| Start-BitsTransfer |   11.31 |   0.96 |  10.56 |
| aria2c -x 4        |   10.64 |   0.37 |  10.59 |
| curl               |   10.70 |   0.50 |  10.63 |
| Invoke-RestMethod  |   10.86 |   0.87 |  10.68 |
| Test-FileDownload  |   10.97 |   0.52 |  10.83 |
| aria2c             |   12.25 |   0.66 |  12.06 |