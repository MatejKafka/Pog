# Dependencies

## Inspiration

- https://docs.npmjs.com/cli/v6/configuring-npm/package-json/

- https://doc.rust-lang.org/cargo/reference/manifest.html

## GitHub API

list packages in repo:

```powershell
(Invoke-RestMethod "https://api.github.com/repos/MatejKafka/PkgPackages/git/trees/master").tree.path
```

list versions of a package:

```powershell
(Invoke-RestMethod "https://api.github.com/repos/MatejKafka/PkgPackages/git/trees/master").tree
	| ? {$_.path -eq "texstudio"}
	| % {(Invoke-RestMethod $_.url).tree}
	| % {$_.path}
```

## Dependency types

- peer dependencies
	- e.g. plugin declares that it's compatible with version X of the host package
	- re-enable the host package after installation?
- build (update/install/enable) dependencies
	- e.g. mozlz4.exe for Firefox
	- should the package be kept, or uninstalled after build?
- normal dependencies
	- specify both package name and version
	- allow to specify an URL if the package is in not in main repo