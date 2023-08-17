@{
	# private, since Pog does not have an Install block
	Private = $true

	Name = "Pog"
	Version = "0.4.0"
	# the stub executable binaries are currently only compiled for x64, so we are bound to x64 for now (also, dependencies)
	Architecture = "x64"

	# dependencies:
	#  7zip (x64)
	#  OpenedFilesView (x64)

	Enable = {
		echo "It seems Pog is setup correctly and working now. :)"
	}
}
