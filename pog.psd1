@{
	# private, since Pog does not have an Install block
	Private = $true

	Name = "Pog"
	Version = "0.8.0"
	# why x64:
	#  1) shim binaries are compiled only for x64 (should be easy to change)
	#  2) VC redist DLLs are currently x64 (shouldn't be too hard to change)
	#  3) 7-zip and OpenedFilesView are currently installed for x64
	#     (and there's currently no infrastructure for multiple package versions for different architectures)
	Architecture = "x64"
}
