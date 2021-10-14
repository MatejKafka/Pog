@{
	Name = "Pog"
	Version = "0.2.1"
	# unfortunately, the fixed nim binaries for substitute executables
	#  are not portable, so we are currently bound to x64
	# also, 7zip (dependency) is currently x64
	Architecture = "x64"

	Install = {
		echo "Pog itself is already installed. Auto-update is not supported at the moment."
	}

	Enable = {
		echo "It seems Pog is setup correctly and working now. :)"
	}
}
