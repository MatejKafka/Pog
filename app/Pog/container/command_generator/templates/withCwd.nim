import os
import osproc
import strutils

setControlCHook(proc () {.noconv.} = quit(-1073741510))

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