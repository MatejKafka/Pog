# consider using Job Objects to ensure child processes are killed whenever this one is
# (https://docs.microsoft.com/en-us/windows/win32/procthread/job-objects)

import os
import osproc
import strutils

# do nothing on Ctrl-C, expect the spawned process to handle it
# otherwise, this wrapper would exit, shell would return to prompt, and the spawned process would race
#  with the shell for lines of input (both are reading from the same stdin), which is quite annoying to resolve
setControlCHook(proc () {.noconv.} = discard)

const CMD_PLACEHOLDER = "\x0".repeat(1024)


# CMD_PLACEHOLDER is dynamically patched to contain target binary that we want to invoke
#  find the real invoked command by reading until first null terminator
let cmdEnd = CMD_PLACEHOLDER.find('\x0')
if cmdEnd == 0:
  echo "TRIED TO RUN UNPATCHED BINARY"
  quit(101)

let command = if cmdEnd < 0: CMD_PLACEHOLDER
    else: CMD_PLACEHOLDER[0..<cmdEnd]


try:
  osproc.startProcess(command, args=os.commandLineParams(), options={poParentStreams, poInteractive})
    .waitForExit()
    .quit()
except OSError:
  echo "Could not find executable: " & command
  quit(100)