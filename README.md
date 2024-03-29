# Pog – a WIP portable package manager for Windows

Short intro: [https://pog.matejkafka.com](https://pog.matejkafka.com)

Note that this is very much an unfinished project.

---

## Overview

Pog is an in-development package manager for Windows, written for PowerShell Core. Unlike most existing Windows package managers, which delegate to existing program installers, Pog manages the whole package installation process end-to-end, preferring installation from package archives instead. The packages are encapsulated by redirecting their default data directories to a package-local directory, This also provides first-class support for portable packages, which can be moved between machines without reinstallation.

## Installation

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

Update package list (apologies, there's no automatic way to do this for now):

```powershell
cd "directory\where\pog\is\installed\Pog"
rm -Force -Recurse .\data\manifests\
# rerun setup to download the latest manifests
#  (this should not break/overwrite anything)
.\setup.cmd
```

## Building

Pog is composed of 3 parts:

1. `app/Pog`: The main PowerShell module (`Pog.psm1` and imported modules). You don't need to build this.
2. `app/Pog/lib_compiled/Pog`: The `Pog.dll` C# library, where a lot of the core functionality lives. The library targets `.netstandard2.0`.
3. `app/Pog/lib_compiled/PogNative`: The `PogExecutableStubTemplate.exe` executable stub.

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