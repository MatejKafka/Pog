# https://devblogs.microsoft.com/oldnewthing/20210802-00/?p=105510
# https://devblogs.microsoft.com/oldnewthing/20090212-00/?p=19173
# https://superuser.com/questions/1100134/how-can-i-determine-which-process-or-service-is-using-com-surrogate

$Rows = foreach ($l in ls "Registry::HKEY_CLASSES_ROOT\CLSID\{*}") {
    $ServiceName = $l.GetValue($null)
    $AppId = $l.GetValue("AppID")
    if ($AppId -and $ServiceName) {
        echo "`t`"$($AppId.ToUpper())`" = `"$ServiceName`""
    }
}

"@{`n" + ($Rows -join "`n") + "`n}"