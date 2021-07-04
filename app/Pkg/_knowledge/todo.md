## Dependency management

- NOTE: packages that were not installed directly must not be visible to the user/system
	- the package may be installed in normal package root (although that may cause confusion for a user who ventures there and wonders why he cannot use the package), but without exported shortcuts and commands
- when package declares another package as dependency, copy/symlink exported commands/shortcuts/libraries/... to a well-defined path **inside** the package (and probably add to PATH?), so that the package doesn't have to reach out of its package directory during normal operation
- NOTE: must figure out a way to pass arguments to manifest methods of a dependency
	- also, somehow remember them, so we know what to do when user or another package requests the package with potentially different options



## Building package

what's needed:

- add a way for `Install` to download sources instead of directly extracting an archive containing a binary
	- add a `Build` method to manifest, which takes installed sources and builds them
	- possible solution: `Install-Sources`, which would download a hashtable of sources (must also support git) to a `.src` dir (or something like that); then, `Build` will take these and compile them
- add `InstallDependencies`, `BuildDependencies` and `EnableDependencies` in addition to `Dependencies`
	- these would only be available in the matching manifest method, not for other apps

## Sharing data between packages

example: JetBrains IDE shared folder

"""
osobne vidim 2 varianty:

1) mit metapackage jetbrains-shared-config, ktery bude dependency pro kazde ide,
a budou tam vsechna IDE zapisovat (to neni uplne clean, protoze pak vlastne
zapisuji do config dir ciziho package)

2) udelat do Pkg koncept neceho jako shared memory; package rekne,
ze chce shared folder s timhle jmenem, vsechny package si vyzadaji
stejnou slozku, a tam budou zapisovat
"""

## Qt installer

https://download.qt.io/online/qtsdkrepository/windows_x86/

https://github.com/miurahr/aqtinstall