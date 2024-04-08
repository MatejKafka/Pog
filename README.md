# Pog – a WIP portable package manager for Windows

Short intro: [https://pog.matejkafka.com](https://pog.matejkafka.com)

Note that this is very much an unfinished project.

---

## Overview

Pog is an in-development package manager for Windows, written for PowerShell Core. Unlike most existing Windows package managers, which delegate to existing program installers, Pog manages the whole package installation process end-to-end, preferring installation from package archives instead. The packages are encapsulated by redirecting their default data directories to a package-local directory, This also provides first-class support for portable packages, which can be moved between machines without reinstallation.

## Installation

Run the following snippet in PowerShell:

```powershell
iex (iwr pog.matejkafka.com/install.ps1)
```

Alternatively, you can manually install Pog by following these steps:

1. Ensure you have enabled developer mode. Pog currently needs it for symbolic links (hopefully I can get rid of that in a future release).
2. Download the latest release from [the release page](https://github.com/MatejKafka/Pog/releases/).
3. Download and unpack the archive to your preferred directory for portable applications.
4. Run `Pog/setup.cmd`.

## Basic usage

Install a package (use `Tab` to get a list of matching packages):
```powershell
pog <PackageName>
```

Uninstall a package:

```powershell
Uninstall-Pog <PackageName>
```

List packages:

```powershell
Get-PogPackage # list installed packages
Get-PogRepositoryPackage # list available packages
```

## Building

Pog is composed of 4 parts:

1. `app/Pog`: The main PowerShell module (`Pog.psm1` and imported modules). You don't need to build this.
2. `app/Pog/lib_compiled/Pog`: The `Pog.dll` C# library, where a lot of the core functionality lives. The library targets `.netstandard2.0`.
3. `app/Pog/lib_compiled/PogNative`: The `PogExecutableStubTemplate.exe` executable stub.
4. `app/Pog/lib_compiled/vc_redist`: Directory of VC Redistributable DLLs, used by some packages with the `-VcRedist` switch parameter on `Export-Command`/`Export-Shortcut`.

After all parts are ready, import the main module (`Import-Module app/Pog` from the root directory). Note that Pog assumes that the top-level directory is inside a package root, and it will place its data and cache directories in the top-level directory.

### `lib_compiled/Pog`

Pog expects the library to be present at `lib_compiled/Pog.dll`. To build it, use `dotnet publish` with a recent-enough version of .NET Core:

```powershell
cd app/Pog/lib_compiled/Pog
dotnet publish
```

### `lib_compiled/PogNative`

This project contains the executable stub used to set arguments and environment variables when exporting entry points to a package using `Export-Command` and `Export-Shortcut`. The output binary should appear at `lib_compiled/PogExecutableStubTemplate.exe`.

Build it using CMake and a recent-enough version of MSVC:

```powershell
cd app/Pog/lib_compiled/PogNative
cmake -B ./cmake-build-release -S . -DCMAKE_BUILD_TYPE=Release
cmake --build ./cmake-build-release --config Release
```

### `lib_compiled/vc_redist`

The DLLs here are copied from the Visual Studio SDK. With my installation of Visual Studio 2022, the DLLs are located at `C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Redist\MSVC\<version>\x64`. Copy all of the DLLs to the `vc_redist` directory (all DLLs should be in the the `vc_redist` directory, without any subdirectories). The script at `app/Pog/_scripts/update vc redist.ps1` will copy the DLLs for you (you may need to adjust the MSVC path if you have a different version of Visual Studio / MSVC toolset).