$CompileArgs = @(
	"--checks:off"
	"--stackTrace:off"
	"--lineTrace:off"
	"--opt:size"
	"-d:release"
	"--gc:none" # we don't really allocate anything, no need for GC
	"--warning[GcMem]:off"
)

# generate console and GUI subsystem binaries for both keepCwd and withCwd
@(
	"$PSScriptRoot\templates\keepCwd.nim"
	"$PSScriptRoot\templates\withCwd.nim"
) | % {
	nim compile --app:console @CompileArgs --out:$($_ -replace ".nim$", "_console.exe") $_
	nim compile --app:gui @CompileArgs --out:$($_ -replace ".nim$", "_gui.exe") $_
}
