# https://devblogs.microsoft.com/oldnewthing/20210802-00/?p=105510
# https://devblogs.microsoft.com/oldnewthing/20090212-00/?p=19173
# https://superuser.com/questions/1100134/how-can-i-determine-which-process-or-service-is-using-com-surrogate

foreach ($l in ls "Registry::HKEY_CLASSES_ROOT\CLSID\{*}") {
    $ServiceName = $l.GetValue($null)
    $AppId = $l.GetValue("AppID")
    $InProcServer = $l.OpenSubKey("InProcServer32")?.GetValue($null)
    if ($AppId -and $ServiceName) {
        [pscustomobject]@{
            AppId = $AppId.ToUpperInvariant()
            ServiceName = $ServiceName
            InProcServer = $InProcServer
        }
    }
}