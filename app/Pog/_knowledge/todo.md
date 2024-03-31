## Dependency management

**UPDATE: nope, not doing dependencies, vendor should figure that part out**

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

2) udelat do Pog koncept neceho jako shared memory; package rekne,
ze chce shared folder s timhle jmenem, vsechny package si vyzadaji
stejnou slozku, a tam budou zapisovat

"""

## Qt installer

https://download.qt.io/online/qtsdkrepository/windows_x86/

https://github.com/miurahr/aqtinstall



## Random TODOs

- name collisions in different roots
- add file associations
- add Export-Library for exporting .dlls
- add exporting of powershell argument completers
- add 'Isolated' flag to all impure manifests
- figure out how to support installation of non-portable packages in a user-friendly way
- think through possible manifest categories (isolated, global, config-only, ...?)
- think through types of packages:
  - 'portable packages' (no drivers, no admin mode, no system changes,...)
  - 'managed packages' (may have drivers, use admin, but still keep all config inside package dir and are fully installed & uninstalled using Pog)
  - 'system packages' (which are installed by Pog, and potentially even uninstalled, but store config in AppData/Registry, or integrate deeply to the system, and are lost upon reinstall)
- add architecture validation to Import-Pog/Install-Pog
- add Pog shell to test enable/install environments; set VerbosePreference to aid debugging
- figure out what to do about symlinks when not running as admin
- add ability to define scheduled tasks to Env_Enable (also handle https://www.thecliguy.co.uk/2020/02/09/scheduled-task-trigger-synchronize-across-time-zones/)
- think through packages downloaded by passing URL instead of a name
- think through packages outside package dirs (e.g. I want a package in D:\programming, and I want Pog to remember it between system reinstalls and then reenable it)
- allow user to add their own parameters and env vars to wrapper scripts using public documented API
- node.js - ask user if global node_modules bin/ should be added to PATH
- figure out how we can let packages add dirs like 'go-lang/data/packages/bin' to PATH
- add powershell hook; when command is not found and there's a package that provides it, prompt the user to install it
- TUI command switcher (select which of multiple conflicting commands from different packages to use, optionally rename the others)
- clear env:PATH and similar env variables in container
- add fn to allow binary through firewall
- add robust Ini, XML, JSON and YAML updater for Assert-File
- ask Fosshub about their stance to Pog (https://www.fosshub.com/tos.html, https://www.reddit.com/r/sysadmin/comments/5g5npg/fosshub_message_for_chocolatey_please_stop_with/)
- Signal Portable - https://community.signalusers.org/t/portable-app-version-of-signal-desktop-windows/2000/11
- wtf happens when Export-Shortcut target is a shortcut? (scripts, Recycle Bin)
- Make internal pog dirs (.commands, .pog, pog.psd1) inside the package directory hidden?
- allow manifest to provide multiple download URLs (as long as the hash is the same for all of them)
- add env for package generator, cache retrieved hashes for src URL
- think through virtual packages, which only detect if a systemwide installation of a program already exists; this would allow packages to declare dependencies on python, MikTex,... and error out with helpful error message if these are not installed
- add params from Assert-File to 'Set-SymlinkedPath Directory'
- validate that package name does not start or end with whitespace
- check that anything added to PATH or PSModulePath does not contain semicolons (;)
- implement support for the Channel property in a manifest
- provide list of already existing package manifests to version generator; that way, it can stop listing releases when it reaches an already existing release
- Sandboxie portable
- Krita portable
- VS Code – Check VSCODE_LOGS env variable, it seems to not work correctly
- haxe-lang - export haxelib, set package installation path (currently '$HOME/haxelib')
- switch from OpenedFilesView to something without configuration and a better CLI, or bundle OFV directly, not as a separte package that user could reconfigure
- rename Set-SymlinkedPath to Set-Symlink?
- Set-SymlinkedPath - warn when attempting to do something like ,,Set-SymlinkedPath "./config/nvim" "./config" Directory,, as this deletes the contents of ./config/nvim, because ./config already contains something
- Export-Command should check if the file extension is in PATHEXT, especially if symlink is used
- if manifest writer changes download URL, forgets to update the hash, and has the URL cached, Pog will silently seem to work, using the cached archive; maybe use url as part of the cache entry key? Figure out how that would interact with multiple mirrors
- Add package for GIMP – https://www.gimp.org/man/gimp.html; environment variables like GIMP2_DIRECTORY
- ssh - when full stub binaries are implemented, pass -F to 'ssh.exe' to support package-local config dir without symlinks from ~/.ssh
- detect custom modifications to pog.psd1 inside a package and warn before overwriting (compare with repository manifest of the same version for differences)
- FileDownloader - figure out how to make BITS use filename from HTTP header instead of last segment of URL (this is an issue for e.g. rustup, where -NoArchive is passed, so user sees the actual downloaded binary name)
- substitute exe should correctly forward argv[0] (app name), so that e.g. ccache works
- always check that we're in a filesystem provider, and then use .ProviderPath for native commands to support network share paths
- manifest is not executed with strict mode
- investigate whether application manifests could be used on the stub to disable display scaling for the target, instead of modifying the manifest of the target directly