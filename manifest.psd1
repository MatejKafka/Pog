@{
	Name = "Pkg"
	Version = "0.2.0"
	Architecture = "any"
	Enable = {
		# add Pkg dir to PSModulePath
		Add-EnvVar "PSModulePath" (Resolve-Path .\app)

		Assert-Directory "./data"
		Assert-Directory "./data/pkg_bin"
		# register default root if not already present
		Assert-File "./data/roots.txt" {Resolve-Path .\..}
	}
}

