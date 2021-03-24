## Export-Command, Export-Shortcut

- unified API, except for GUI-specific features for shortcuts (icon,...)
- detects if the program is console-based or GUI, changes launcher to match
- the following can be set for the launched program:
	- working directory
	- process environment variables - only set for the single process (and children)
	- default arguments - if user passes a custom value for this argument, it replaces the predefined one in the launcher
- implementation:
	- if none of the above options are set and the program supports it, use a symlink
		- explicit opt-in, as it often leads to breakage (program has to explicitly support symlinking)
	- substitute exe: precompiled binary that launches the target with given options
	- launcher file: custom file format that's interpreted by a custom file handler set by Pkg
		- downside: requires systemwide registration, but we are doing it anyway (adding `pkg_bin` to PATH, Pkg to PSModulePath,...)