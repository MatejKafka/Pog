import os
import osproc
import strutils

# do nothing on Ctrl-C, expect the spawned process to handle it
# otherwise, this wrapper would exit, shell would return to prompt, and the spawned process would race
#  with the shell for lines of input (both are reading from the same stdin), which is quite annoying to resolve
setControlCHook(proc () {.noconv.} = discard)

# max single path element size on Windows
const CMD_PLACEHOLDER = "\x0".repeat(260)
# must be different size - otherwise compiler would unite them into single block
const CWD_PLACEHOLDER = "\x0".repeat(1024)


let cmdEnd = CMD_PLACEHOLDER.find('\x0')
let cwdEnd = CWD_PLACEHOLDER.find('\x0')

if cmdEnd == 0 or cwdEnd == 0:
  echo "TRIED TO RUN UNPATCHED BINARY"
  quit(101)

let command = if cmdEnd < 0: CMD_PLACEHOLDER
    else: CMD_PLACEHOLDER[0..<cmdEnd]
let cwd = if cwdEnd < 0: CWD_PLACEHOLDER
    else: CWD_PLACEHOLDER[0..<cwdEnd]


try:
  os.setCurrentDir(cwd)
except OSError:
  echo "Could not set working directory to: " & cwd

try:
  osproc.startProcess(command, args=os.commandLineParams(), options={poParentStreams, poInteractive})
    .waitForExit()
    .quit()
except OSError:
  echo "Could not find executable: " & command
  quit(100)