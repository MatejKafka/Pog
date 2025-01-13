using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Pog.Native;

// the CsWin32-generated fields are pretty ugly, but still easier than defining it manually
internal struct PropVariant : IDisposable {
    private PROPVARIANT _variant = new();

    public PROPVARIANT Variant => _variant;
    public VARENUM Type {
        get => _variant.Anonymous.Anonymous.vt;
        private set => _variant.Anonymous.Anonymous.vt = value;
    }

    internal PropVariant(PROPVARIANT variant) {
        _variant = variant;
    }

    public unsafe PropVariant(string value) {
        Type = VARENUM.VT_LPWSTR;
        _variant.Anonymous.Anonymous.Anonymous.pwszVal = (char*) Marshal.StringToCoTaskMemUni(value).ToPointer();
    }

    public bool TryGetString(out string value) {
        if (Type != VARENUM.VT_LPWSTR) {
            value = null!;
            return false;
        }
        value = _variant.Anonymous.Anonymous.Anonymous.pwszVal.ToString();
        return true;
    }

    public void Dispose() {
        var hr = PInvoke.PropVariantClear(ref _variant);
        Marshal.ThrowExceptionForHR(hr);
    }
}

internal static class PropVariantExtensions {
    public static uint GetCount(this IPropertyStore store) {
        store.GetCount(out var count);
        return count;
    }

    public static unsafe (Guid, uint) GetKeyAt(this IPropertyStore store, uint index) {
        var key = new PROPERTYKEY();
        store.GetAt(index, &key);
        return (key.fmtid, key.pid);
    }

    public static unsafe PropVariant GetValue(this IPropertyStore store, Guid formatId, uint propertyId) {
        var key = new PROPERTYKEY {fmtid = formatId, pid = propertyId};
        store.GetValue(&key, out var variant);
        return new(variant);
    }

    public static unsafe void SetValue(this IPropertyStore store, Guid formatId, uint propertyId, PropVariant value) {
        var key = new PROPERTYKEY {fmtid = formatId, pid = propertyId};
        store.SetValue(&key, value.Variant);
    }

    public static IEnumerable<(Guid, uint)> EnumerateKeys(this IPropertyStore store) {
        store.GetCount(out var propCount);
        for (uint i = 0; i < propCount; i++) {
            yield return store.GetKeyAt(i);
        }
    }

    public static IEnumerable<KeyValuePair<(Guid, uint), PropVariant>> GetEnumerator(this IPropertyStore store) {
        foreach (var key in store.EnumerateKeys()) {
            yield return new(key, store.GetValue(key.Item1, key.Item2));
        }
    }
}
