using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Pog.Native;

// NOTE: if the COM API shows to be problematic, there is an existing project that reads the raw shortcut file:
// https://github.com/securifybv/ShellLink/blob/master/Shortcut.cs

/// Wrapper class for working with .lnk shortcut files, with similar API to <c>WScript.Shell.CreateShortcut()</c>.
internal class Shortcut {
    private readonly ShellLink _shellLinkInstance = new();
    private readonly IShellLinkW _shellLink;
    private readonly IPersistFile _persistFile;
    private readonly IPropertyStore _propertyStore;

    /// Indicates if this Shortcut instance was loaded from an existing shortcut file.
    public bool Loaded {get; private set;} = false;

    public unsafe string Target {
        get => ReadString((str, size) => _shellLink.GetPath(str, size, null, 0));
        set => WriteString(value, _shellLink.SetPath);
    }

    public string WorkingDirectory {
        get => ReadString((str, size) => _shellLink.GetWorkingDirectory(str, size));
        set => WriteString(value, _shellLink.SetWorkingDirectory);
    }

    public string Arguments {
        get => ReadString((str, size) => _shellLink.GetArguments(str, size));
        set => WriteString(value, _shellLink.SetArguments);
    }

    public string Description {
        get => ReadString((str, size) => _shellLink.GetDescription(str, size));
        set => WriteString(value, _shellLink.SetDescription);
    }

    public (string, int) IconLocation {
        get {
            var iconIndex = 0;
            return (ReadString((str, size) => _shellLink.GetIconLocation(str, size, out iconIndex)), iconIndex);
        }
        set => WriteString(value.Item1, str => _shellLink.SetIconLocation(str, value.Item2));
    }

    public unsafe string TargetID {
        get {
            ITEMIDLIST* itemList = null;
            var shellPath = new PWSTR(null);
            try {
                _shellLink.GetIDList(&itemList);
                PInvoke.SHGetNameFromIDList(*itemList, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, out shellPath);
                return new string(shellPath);
            } finally {
                // we have to free both of these manually, since they're allocated by the COM server
                Marshal.FreeCoTaskMem((IntPtr) shellPath.Value);
                Marshal.FreeCoTaskMem((IntPtr) itemList);
            }
        }
        // this is a bit hacky, but afaict, passing the name string to `.SetPath()` is a proper way to parse the name to an ID
        set => Target = value;
    }

    public Shortcut() {
        // ReSharper disable once SuspiciousTypeConversion.Global
        _shellLink = (IShellLinkW) _shellLinkInstance;
        // ReSharper disable once SuspiciousTypeConversion.Global
        _persistFile = (IPersistFile) _shellLinkInstance;
        // ReSharper disable once SuspiciousTypeConversion.Global
        _propertyStore = (IPropertyStore) _shellLinkInstance;
    }

    public Shortcut(string srcPath) : this() {
        LoadFrom(srcPath);
    }

    public void LoadFrom(string path) {
        // we need READWRITE, otherwise setting values in IPropertyStore fails (but no write is done anyway)
        _persistFile.Load(path, (int) STGM.STGM_READWRITE);
        Loaded = true;
    }

    public void SaveTo(string path) {
        _persistFile.Save(path, false);
    }

    public bool TryGetStringProperty(Guid formatId, uint propertyId, out string str) {
        using var variant = _propertyStore.GetValue(formatId, propertyId);
        return variant.TryGetString(out str);
    }

    /// <param name="formatId">Format (top-level group, can be a project identifier) GUID.</param>
    /// <param name="propertyId">Property ID inside the format group.</param>
    /// <param name="value">The string to store in the property.</param>
    public void SetStringProperty(Guid formatId, uint propertyId, string value) {
        // the temporary PropVariant is not disposed, the store takes ownership
        _propertyStore.SetValue(formatId, propertyId, new(value));
    }

    private static unsafe void WriteString(string str, Action<PCWSTR> fn) {
        fixed (char* ptr = str) {
            fn(new(ptr));
        }
    }

    /// The IShellLinkW API does not give us back the size of buffer we need, so we have to find it by resizing
    /// the buffer until the result fits. Sigh.
    private static unsafe string ReadString(Action<PWSTR, int> fn) {
        // start with 512, which is enough to fit MAX_MATH, and it's a power of two
        for (var buffer = new GrowableBuffer(stackalloc char[512]);; buffer.Grow()) {
            fixed (char* ptr = buffer.Buffer) {
                var str = new PWSTR(ptr);
                fn(str, buffer.Buffer.Length);
                if (str.Length != buffer.Buffer.Length - 1) {
                    // success, we did not fill
                    return str.ToString();
                }
                // the buffer might be too short, grow and retry
            }
        }
    }

    private ref struct GrowableBuffer(Span<char> baseBuffer) {
        public Span<char> Buffer = baseBuffer;

        public void Grow() {
            Buffer = new char[Buffer.Length * 4];
        }
    }
}
