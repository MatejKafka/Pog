This directory contains VC Redistributable DLLs, shared between all packages that need them.

Package authors can use the `-VcRedist` switch of the `Export-Command`/`Export-Shortcut`
commands to have this directory added to PATH when their package is invoked.