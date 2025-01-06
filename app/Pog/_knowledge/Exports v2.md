Design doc for reworking global exports to get rid of Export-Pog and ensure that global exports are always in sync with local exports.

Basic idea:

 - In the future, I want to support renaming of globally exported commands (and maybe other user tweaks to the manifest). This means that I will need some way to keep extra state anyway, so let's figure it out now.
 - Add a pog.user.psd1 config file in the package directory, which contains all user-specific tweaks to the manifest.
 - Whenever Enable-Pog changes some export, do nothing for exported commands, and for shortcuts, check if the shortcut is globally exported (i.e. if there's a global shortcut with matching name that targets the hidden shim) and copy the updated shortcut over it. In the future when other exports are added, ensure they're in sync in case the package is visible. Do not overwrite any conflicting exports, since the user probably wants to keep the relative priority.
 - When creating a new export, check if the package is visible and if so, export the newly added export as well, unless that export already exists from another package (in which case, warn the user).
 - When Remove-StaleExports removes a stale export, also remove the global export, if it's matching, and then check if there's any other visible package exporting that command (enumerate over packages). If changing the case of an export, also change the casing of the global export, if matching; no need to redo the lookup.
 - Make the pog.user.psd1 file configurable through parameters on Enable-Pog (`-Export`, `-Export <list of specific exports to export, with overwriting>`).

TODO:
 - In Enable-Pog, create pog.user.psd1 if it doesn't exist. Current default content: `@{Exported = $true}`
 - When creating a new export (Export-Command, Export-Shortcut), check if the package is visible and export it globally if so. Or maybe only export it at the end of Enable-Pog? Think about removing all globally exported entry points when something fails?

