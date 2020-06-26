@{
	Name = "Pkg"
	Version = "0.2.0"
	Architecture = "any"
	Enable = {
		throw "Unfortunately, enabling Pkg itself does not do anything."
		
		# Assert-Directory "./data"
		# Assert-Directory "./data/pkg_bin"
		# TODO: add ./data/roots.txt with correct default path
	}
}

