$CompileArgs = @(
	"--checks:off"
	"--stackTrace:off"
	"--opt:size"
	"-d:release"
	"--gc:none" # we don't really allocate anything, no need for GC
	"--warning[GcMem]:off"
	"-d:noSignalHandler"
)

nim compile --app:console @CompileArgs .\templates\keepCwd.nim
nim compile --app:console @CompileArgs .\templates\withCwd.nim