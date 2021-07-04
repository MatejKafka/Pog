@{
	Name = "Pkg"
	Version = "0.2.1"
	# unfortunately, the fixed nim binaries for substitute executables
	#  are not portable, so we are currently bound to x64
	# also, 7zip (dependency) is currently x64
	Architecture = "x64"
	
	Install = {
		echo "Pkg itself is already installed. Auto-update is not supported at the moment."
	}
	
	Enable = {
		Assert-Dependency "7zip"
		
		# the Environment module is not exposed to `Enable` scripts,
		#  as normal packages should NOT set any environment variables,
		#  so we'll import it directly
		Import-Module "./app/Pkg/container/Environment"
		
		Assert-Directory "./data"
		Assert-Directory "./cache"
		# local manifest repository
		Assert-Directory "./data/manifests"
		# directory where commands are exported; is added to PATH
		Assert-Directory "./data/pkg_bin"
		# downloaded package cache
		Assert-Directory "./cache/download_cache"
		
		# register default root if not already present
		# TODO: check if we haven't moved since last enable, update if so
		Assert-File "./data/roots.txt" {Resolve-Path .\..}
		
		# add Pkg dir to PSModulePath
		Add-EnvPSModulePath .\app
		# add pkg_bin dir to PATH
		Add-EnvPath ./data/pkg_bin -Prepend
	}
}
