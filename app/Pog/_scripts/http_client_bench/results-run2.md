## 7zip

(2 MB, 60 iterations, https://www.7-zip.org/a/7z2500-x64.exe)

| Name               | Average | StdDev |
| ------------------ | ------- | ------ |
| Invoke-RestMethod  |    0.28 |   0.02 |
| Test-FileDownload  |    0.28 |   0.03 |
| aria2c -x 4        |    0.47 |   0.05 |
| aria2c             |    0.50 |   0.06 |
| curl               |    0.56 |   0.20 |
| Start-BitsTransfer |    0.80 |   0.58 |

## MobaXterm

(25 MB, 60 iterations, https://download.mobatek.net/2032020060430358/MobaXterm_Portable_v20.3.zip)

| Name               | Average | StdDev |
| ------------------ | ------- | ------ |
| Invoke-RestMethod  |    1.12 |   0.42 |
| aria2c             |    1.20 |   0.28 |
| curl               |    1.23 |   0.53 |
| aria2c -x 4        |    1.26 |   0.34 |
| Test-FileDownload  |    1.30 |   0.61 |
| Start-BitsTransfer |    1.70 |   0.76 |

## go-lang

(83 MB, 60 iterations, https://go.dev/dl/go1.24.5.windows-amd64.zip)

| Name               | Average | StdDev |
| ------------------ | ------- | ------ |
| Invoke-RestMethod  |    2.57 |   0.25 |
| aria2c -x 4        |    2.61 |   0.15 |
| curl               |    2.64 |   0.28 |
| Test-FileDownload  |    2.67 |   0.25 |
| aria2c             |    2.95 |   0.25 |
| Start-BitsTransfer |    3.11 |   0.40 |

## Azure CLI

(86 MB, 60 iterations, https://github.com/Azure/azure-cli/releases/download/azure-cli-2.75.0/azure-cli-2.75.0-x64.zip)

| Name               | Average | StdDev |
| ------------------ | ------- | ------ |
| Test-FileDownload  |    2.97 |   0.44 |
| Invoke-RestMethod  |    3.01 |   0.48 |
| curl               |    3.18 |   0.44 |
| aria2c             |    3.29 |   0.46 |
| aria2c -x 4        |    3.35 |   0.43 |
| Start-BitsTransfer |    3.50 |   0.64 |

## VS Code

(149 MB, 60 iterations, https://update.code.visualstudio.com/1.102.1/win32-x64-archive/stable)

| Name               | Average | StdDev |
| ------------------ | ------- | ------ |
| Start-BitsTransfer |    4.00 |   0.50 |
| curl               |    4.02 |   0.51 |
| Invoke-RestMethod  |    4.18 |   0.53 |
| Test-FileDownload  |    4.24 |   0.48 |
| aria2c -x 4        |    4.44 |   0.46 |
| aria2c             |    4.83 |   0.58 |

## Ghidra

(403 MB, 20 iterations, https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_11.1.2_build/ghidra_11.1.2_PUBLIC_20240709.zip)

| Name               | Average | StdDev |
| ------------------ | ------- | ------ |
| aria2c -x 4        |   11.44 |   0.37 |
| Invoke-RestMethod  |   11.64 |   0.89 |
| Test-FileDownload  |   12.38 |   1.38 |
| curl               |   12.48 |   1.29 |
| Start-BitsTransfer |   12.64 |   1.46 |
| aria2c             |   13.12 |   1.05 |