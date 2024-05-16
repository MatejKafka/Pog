# Pog: A portable package manager for Windows

Short visual intro: [https://pog.matejkafka.com](https://pog.matejkafka.com) (slighly outdated)

Pog is a **fast**, in-development package manager for Windows, managing **encapsulated, portable applications**. As a frequent Linux user, I always enjoyed the hassle-free experience of installing software. Windows have multiple package managers, but other than Scoop, all of them are unreasonably slow and fiddly, and Scoop is a bit too minimal for my taste and does not fully utilize portable packages. Pog is an attempt to provide Linux-level user experience in a Windows-native way.

Unlike most existing Windows package managers, which delegate to existing program installers, Pog installs packages from static archives with a readable package manifest. The packages are encapsulated by redirecting their default data directories to a package-local directory, providing first-class support for portable packages, where multiple versions can be installed side-by-side and even moved between machines without reinstallation.

**Pog is pretty usable in its current state, but there's a lot of on-going development, and the documentation is lacking. If anything seems broken or you're not sure how to do something, feel free to open an issue. :)**

## Usage

Refer to the [about_Pog](./app/Pog/about_Pog.help.txt) help page, which is also available from PowerShell using `man about_Pog` after Pog is installed.

## Installation

Run the following snippet in PowerShell:

```powershell
# Pog is installed to the current directory
cd dir/where/to/install/Pog

iex (irm pog.matejkafka.com/install.ps1)
```

Alternatively, you can manually install Pog by following these steps:

1. Ensure you have enabled developer mode. Pog currently needs it for symbolic links (hopefully I can get rid of that in a future release).
2. Download the latest release from [the release page](https://github.com/MatejKafka/Pog/releases/).
3. Download and unpack the archive to your preferred directory for portable applications.
4. Run `Pog/setup.cmd`.

### Upgrading an existing installation of Pog

Ideally, I would like Pog to be able to update itself. However, it internally uses a compiled .NET assembly, which gets loaded automatically when Pog is imported and PowerShell provides no way to unload assemblies other than exiting the whole process, meaning that Pog must not be running when it is updated. Before I devise a better solution, do the following steps manually when you want to upgrade:

1. Exit all instances of PowerShell where Pog was invoked.
2. Manually download the target release archive.
3. Enter the `Pog` directory, where Pog is installed.
4. Delete everything except for the `cache` and `data` directory.
5. Copy the contents of the `Pog` directory from the archive into the `Pog` directory from step 3.
6. Run the extracted `setup.cmd` script.

### Uninstallation

To uninstall Pog itself:

1. Remove the `.../Pog/data/package_bin` directory from the `PATH` environment variable.
2. Remove the `.../Pog/app` directory from the `PSModulePath` environment variable.
3. Remove the `Pog` subdirectory in the Start menu (`[System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::StartMenu)`).
4. Delete the Pog installation directory.

## Building

Pog is composed of 4 parts:

1. `app/Pog`: The main PowerShell module (`Pog.psm1` and imported modules). You don't need to build this.
2. `app/Pog/lib_compiled/Pog`: The `Pog.dll` C# library, where a lot of the core functionality lives. The library targets `.netstandard2.0`.
3. `app/Pog/lib_compiled/PogNative`: The `PogShimTemplate.exe` executable shim.
4. `app/Pog/lib_compiled/vc_redist`: Directory of VC Redistributable DLLs, used by some packages with the `-VcRedist` switch parameter on `Export-Command`/`Export-Shortcut`.

After all parts are ready, import the main module (`Import-Module app/Pog` from the root directory). Note that Pog assumes that the top-level directory is inside a package root, and it will place its data and cache directories in the top-level directory.

### `lib_compiled/Pog`

Pog expects the library to be present at `lib_compiled/Pog.dll`. To build it, use `dotnet publish` with a recent-enough version of .NET Core:

```powershell
cd app/Pog/lib_compiled/Pog
dotnet publish
```

### `lib_compiled/PogNative`

This project contains the executable shim used to set arguments and environment variables when exporting entry points to a package using `Export-Command` and `Export-Shortcut`. The output binary should appear at `lib_compiled/PogShimTemplate.exe`.

Build it using CMake and a recent-enough version of MSVC:

```powershell
cd app/Pog/lib_compiled/PogNative
cmake -B ./cmake-build-release -S . -DCMAKE_BUILD_TYPE=Release
cmake --build ./cmake-build-release --config Release
```

### `lib_compiled/vc_redist`

The DLLs here are copied from the Visual Studio SDK. With my installation of Visual Studio 2022, the DLLs are located at `C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Redist\MSVC\<version>\x64`. Copy all of the DLLs to the `vc_redist` directory (all DLLs should be in the the `vc_redist` directory, without any subdirectories). The script at `app/Pog/_scripts/update vc redist.ps1` will copy the DLLs for you (you may need to adjust the MSVC path if you have a different version of Visual Studio / MSVC toolset).