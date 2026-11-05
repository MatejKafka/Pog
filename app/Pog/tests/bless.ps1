param($Test = "*")

ls $PSScriptRoot/* -Directory | % {
    cp $_\output.txt $_\reference.txt
}