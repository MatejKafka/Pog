@{
	Name = "Pog"
	Version = "0.3.0"
	# unfortunately, the fixed nim binaries for substitute executables
	#  are not portable, so we are currently bound to x64 (also, dependencies)
	Architecture = "x64"

	# dependencies:
	#  7zip (x64)
	#  OpenedFilesView (x64)

	Enable = {
		echo "It seems Pog is setup correctly and working now. :)"
	}
}
