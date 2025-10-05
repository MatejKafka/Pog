# Pog: A portable package manager for Windows

Short visual intro: [https://pog.matejkafka.com](https://pog.matejkafka.com) (few years old, quite outdated)

Pog is a **fast**, in-development package manager for Windows, managing **encapsulated, portable applications**. As a frequent Linux user, I always enjoyed the hassle-free experience of installing software. Windows has multiple package managers, but other than Scoop, all of them are unreasonably slow and fiddly, and Scoop is a bit too minimal for my taste and does not fully utilize portable packages. Pog is an attempt to provide Linux-level user experience in a Windows-native way.

Unlike most existing Windows package managers, which delegate to existing program installers, Pog installs packages from static archives with a [readable package manifest](https://github.com/MatejKafka/PogPackages/blob/main/Bun/.template/pog.psd1). The packages are encapsulated by redirecting their default data directories to a package-local directory, providing first-class support for portable packages, where multiple versions can be installed side-by-side and even moved between machines without reinstallation.

**Pog is pretty usable in its current state, but there's a lot of on-going development, and the documentation is lacking. If anything seems broken or you're not sure how to do something, feel free to open an issue. :)**

## Usage

Refer to the [about_Pog](./app/Pog/about_Pog.help.txt) help page, which is also available from PowerShell using `man about_Pog` after Pog is installed. For a description of the package configuration environment (useful for package maintainers), see the [about_PogEnable](./app/Pog/about_PogEnable.help.txt) help topic.

## Installation

Run the following snippet in PowerShell:

```powershell
# Pog is installed to the current directory
cd dir/where/to/install/Pog

iex (irm https://pog.matejkafka.com/install.ps1)
```

Alternatively, you can manually install Pog by following these steps:

1. Ensure you have enabled developer mode. Pog currently needs it for symbolic links (hopefully I can get rid of that in a future release).
2. Download the latest release from [the release page](https://github.com/MatejKafka/Pog/releases/).
3. Download and unpack the archive to your preferred directory for portable applications.
4. Run `Pog/setup.cmd`.

### Upgrading an existing installation of Pog

Ideally, I would like Pog to be able to update itself. However, it internally uses a compiled .NET assembly, which gets loaded automatically when Pog is imported and PowerShell provides no way to unload assemblies other than exiting the whole process, meaning that Pog must not be running when it is updated. 

Before I devise a better solution, **exit all instances of PowerShell where Pog was invoked** and run the following script:

```powershell
# directory where Pog is installed
cd path/to/pog

iex (irm https://pog.matejkafka.com/upgrade.ps1)
```

Alternatively, if the script fails or you prefer to upgrade manually, follow these steps:

1. Exit all instances of PowerShell where Pog was invoked.
2. Manually download the target release archive.
3. Enter the `Pog` directory, where Pog is installed.
4. Delete everything except for the `cache` and `data` directory.
5. Copy the contents of the `Pog` directory from the archive into the `Pog` directory from step 3.
6. Run the extracted `setup.cmd` script.

### Uninstallation

To uninstall Pog itself:

1. Run `Get-PogPackage | ? PackageName -ne Pog | Uninstall-Pog` to uninstall all installed packages.
2. Run `Disable-Pog Pog` to unregister Pog from the system.
3. Close all PowerShell sessions where Pog is loaded.
4. Delete the Pog installation directory.

## Project structure

Pog is implemented as a PowerShell module, which lives at `app/Pog` (the `app` directory is added to `PSModulePath` during installation). The main entry point to the module is `app/Pog/Pog.psm1`, which defines a few of the public functions and re-exports binary cmdlets defined in `app/Pog/lib_compiled/Pog.dll`, a .NET assembly built from the sources in `app/Pog/lib_compiled/Pog`.

To run scripts from package manifests and generators, Pog uses a custom restricted PowerShell environment. The environment is set up in the `Pog.Container` class in `Pog.dll`, and the entry points to the environments are stored in `app/Pog/container`, such as `app/Pog/container/Env_Enable.psm1` for the `Enable` script environment.

## Building

Pog is composed of 4 parts:

1. `app/Pog`: The main PowerShell module (`Pog.psm1` and imported modules). You don't need to build it.
2. `app/Pog/lib_compiled/Pog`: The `Pog.dll` C# library, where a lot of the core functionality lives. The library targets `.netstandard2.0`.
3. `app/Pog/lib_compiled/Pog.Shim`: The `PogShimTemplate.exe` executable shim, built in C++20 and compiled using CMake.
4. `app/Pog/lib_compiled/vc_redist`: Directory of VC Redistributable DLLs, used by some packages with the `-VcRedist` switch parameter on `Export-Command`/`Export-Shortcut`.

After all parts are compiled according to the instructions below, import the main module (`Import-Module app/Pog` from the root directory). Note that Pog assumes that the top-level directory is inside a package root, and it will place its data and cache directories in the top-level directory.

### `lib_compiled/Pog`

Pog expects the library to be present at `lib_compiled/Pog.dll`. To build it, use `dotnet publish` with a recent-enough version of .NET Core:

```powershell
cd app/Pog/lib_compiled/Pog
dotnet publish
```

### `lib_compiled/Pog.Shim`

This project contains the executable shim used to set arguments and environment variables when exporting entry points to a package using `Export-Command` and `Export-Shortcut`. The output binary should be automatically placed at `lib_compiled/PogShimTemplate.exe`.

Build it using CMake and a recent-enough version of MSVC:

```powershell
cd app/Pog/lib_compiled/Pog.Shim
cmake -B ./cmake-build-release -S . -DCMAKE_BUILD_TYPE=Release
cmake --build ./cmake-build-release --config Release
```

### `lib_compiled/vc_redist`

The DLLs here are copied from the Visual Studio SDK. With my installation of Visual Studio 2022, the DLLs are located at `C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Redist\MSVC\<version>\x64`. Copy all of the DLLs to the `vc_redist` directory (all DLLs should be in the the `vc_redist` directory, without any subdirectories). The script at `app/Pog/_scripts/update vc redist.ps1` will copy the DLLs for you (you may need to adjust the MSVC path if you have a different version of Visual Studio / MSVC toolset).