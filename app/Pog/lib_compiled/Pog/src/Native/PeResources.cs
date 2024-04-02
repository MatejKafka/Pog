using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Pog.Native;

// TODO: support resources with non-standard types (accept ResourceAtom as resourceType, in addition to ResourceType)
internal static class PeResources {
    public sealed class Module : IDisposable {
        private readonly Win32.FreeLibrarySafeHandle _handle;

        public Module(string pePath) {
            _handle = Win32.LoadLibraryEx(pePath, default, Win32.LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);
            if (_handle.IsInvalid) {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
        }

        public void Dispose() {
            _handle.Dispose();
        }

        /// <returns>
        /// Span wrapping the loaded resource.
        /// NOTE: You must NOT use the span after the Module instance is disposed, as it unmaps the resource from memory.
        /// </returns>
        /// <exception cref="ResourceNotFoundException">Resource does not exist.</exception>
        public unsafe ReadOnlySpan<byte> GetResource(ResourceId id) {
            var resourceHandle = Win32.FindResourceEx(_handle, (ushort) id.Type, id.Name, id.Language);
            if (resourceHandle == default) {
                var hr = Marshal.GetHRForLastWin32Error();
                // TODO: handle id.Language
                if (!ResourceNotFoundException.ThrowForHResult(hr, id.Type, id.Name)) {
                    Marshal.ThrowExceptionForHR(hr);
                }
            }

            var loadedResource = Win32.LoadResource(_handle, resourceHandle);
            if (loadedResource.IsNull) {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            var resourcePtr = Win32.LockResource(loadedResource);
            if (resourcePtr == null) {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            var resourceSize = Win32.SizeofResource(_handle, resourceHandle);
            if (resourceSize == 0) {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            return new ReadOnlySpan<byte>(resourcePtr, (int) resourceSize);
        }

        public bool TryGetResource(ResourceId id, out ReadOnlySpan<byte> resource) {
            try {
                resource = GetResource(id);
            } catch (ResourceNotFoundException) {
                resource = default;
                return false;
            }
            return true;
        }

        public bool IterateResourceLanguages(ResourceId id, Func<ushort, bool> callback) {
            return IterateResourceLanguages(id.Type, id.Name, callback);
        }

        /// <exception cref="ResourceTypeNotFoundException"></exception>
        public bool IterateResourceLanguages(ResourceType resourceType, Win32.ResourceAtom resourceName,
                Func<ushort, bool> callback) {
            ExceptionDispatchInfo? exceptionFromCb = null;
            var success = Win32.EnumResourceLanguages(_handle, (ushort) resourceType, resourceName, (_, _, _, lang, _) => {
                try {
                    return callback(lang);
                } catch (Exception e) {
                    // exceptions must not escape the enumeration callback, because .NET marshalling cannot propagate
                    //  them through the native call
                    exceptionFromCb = ExceptionDispatchInfo.Capture(e);
                    return false;
                }
            }, 0);

            if (success) {
                return true;
            }

            var hr = Marshal.GetHRForLastWin32Error();
            // 0x80073B02 = ERROR_RESOURCE_ENUM_USER_STOP, user stopped enumeration by returning false from callback
            if (hr == -2147009790) {
                // if exception occurred inside the callback, rethrow it with original stack trace
                exceptionFromCb?.Throw();
                // else return false
                return false;
            } else if (!ResourceNotFoundException.ThrowForHResult(hr, resourceType, resourceName)) {
                Marshal.ThrowExceptionForHR(hr);
            }
            throw new InvalidOperationException("unreachable");
        }

        /// <exception cref="ResourceTypeNotFoundException"></exception>
        public bool IterateResourceNames(ResourceType resourceType, Func<Win32.ResourceAtom, bool> callback) {
            ExceptionDispatchInfo? exceptionFromCb = null;
            var success = Win32.EnumResourceNames(_handle, (ushort) resourceType, (_, _, name, _) => {
                try {
                    return callback(name);
                } catch (Exception e) {
                    // exceptions must not escape the enumeration callback, because .NET marshalling cannot propagate
                    //  them through the native call
                    exceptionFromCb = ExceptionDispatchInfo.Capture(e);
                    return false;
                }
            }, 0);

            if (success) {
                return true;
            }

            var hr = Marshal.GetHRForLastWin32Error();
            // 0x80073B02 = ERROR_RESOURCE_ENUM_USER_STOP, user stopped enumeration by returning false from callback
            if (hr == -2147009790) {
                // if exception occurred inside the callback, rethrow it with original stack trace
                exceptionFromCb?.Throw();
                // else return false
                return false;
            } else if (!ResourceTypeNotFoundException.ThrowForHResult(hr, resourceType)) {
                Marshal.ThrowExceptionForHR(hr);
            }
            throw new InvalidOperationException("unreachable");
        }

        /// Note that the callback receives a `ResourceAtom`. Use the extension method `.ToResourceType()` to get
        /// the corresponding enum value, or null if not convertible.
        /// <exception cref="ResourceSectionNotFoundException"></exception>
        public bool IterateResourceTypes(Func<Win32.ResourceAtom, bool> callback) {
            ExceptionDispatchInfo? exceptionFromCb = null;
            var success = Win32.EnumResourceTypes(_handle, (_, name, _) => {
                try {
                    return callback(name);
                } catch (Exception e) {
                    // exceptions must not escape the enumeration callback, because .NET marshalling cannot propagate
                    //  them through the native call
                    exceptionFromCb = ExceptionDispatchInfo.Capture(e);
                    return false;
                }
            }, 0);

            if (success) {
                return true;
            }

            var hr = Marshal.GetHRForLastWin32Error();
            // 0x80073B02 = ERROR_RESOURCE_ENUM_USER_STOP, user stopped enumeration by returning false from callback
            if (hr == -2147009790) {
                // if exception occurred inside the callback, rethrow it with original stack trace
                exceptionFromCb?.Throw();
                // else return false
                return false;
            } else if (!ResourceSectionNotFoundException.ThrowForHResult(hr)) {
                Marshal.ThrowExceptionForHR(hr);
            }
            throw new InvalidOperationException("unreachable");
        }

        public List<ushort> GetResourceLanguages(ResourceType resourceType, Win32.ResourceAtom resourceName) {
            var list = new List<ushort>();
            IterateResourceLanguages(resourceType, resourceName, lang => {
                list.Add(lang);
                return true;
            });
            return list;
        }

        public List<Win32.ResourceAtom> GetResourceNames(ResourceType resourceType) {
            var list = new List<Win32.ResourceAtom>();
            IterateResourceNames(resourceType, name => {
                list.Add(name);
                return true;
            });
            return list;
        }

        public List<Win32.ResourceAtom> GetResourceTypes() {
            var list = new List<Win32.ResourceAtom>();
            IterateResourceTypes(type => {
                list.Add(type);
                return true;
            });
            return list;
        }

        public bool TryGetResourceLanguages(ResourceType resourceType, Win32.ResourceAtom resourceName,
                out List<ushort> resourcesNames) {
            try {
                resourcesNames = GetResourceLanguages(resourceType, resourceName);
                return true;
            } catch (ResourceNotFoundException) {
                resourcesNames = null!;
                return false;
            }
        }

        public bool TryGetResourceNames(ResourceType resourceType, out List<Win32.ResourceAtom> resourcesNames) {
            try {
                resourcesNames = GetResourceNames(resourceType);
                return true;
            } catch (ResourceNotFoundException) {
                resourcesNames = null!;
                return false;
            }
        }

        public bool TryGetResourceTypes(out List<Win32.ResourceAtom>? resourceTypes) {
            try {
                resourceTypes = GetResourceTypes();
                return true;
            } catch (ResourceNotFoundException) {
                resourceTypes = null;
                return false;
            }
        }
    }

    public sealed class ResourceUpdater : IDisposable {
        private readonly Win32.ResourceUpdateSafeHandle _handle;

        public ResourceUpdater(string pePath, bool deleteExistingResources = false) {
            _handle = Win32.BeginUpdateResource(pePath, deleteExistingResources);
            if (_handle.IsInvalid) {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
        }

        public void Dispose() {
            _handle.Dispose();
        }

        public void DiscardChanges() {
            _handle.Dispose();
        }

        public void CommitChanges() {
            _handle.CommitChanges();
        }

        public unsafe void SetResource(ResourceId id, ReadOnlySpan<byte> resource) {
            fixed (byte* resourcePtr = resource) {
                if (!Win32.UpdateResource(_handle, (ushort) id.Type, id.Name, id.Language, resourcePtr,
                            (uint) resource.Length)) {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }
            }
        }

        /// <inheritdoc cref="SetResource(ResourceId, System.ReadOnlySpan{byte})"/>
        public void SetResource(ResourceId resourceId, byte[] resource) => SetResource(resourceId, resource.AsSpan());

        public void CopyResourceFrom(Module srcModule, ResourceId id) {
            SetResource(id, srcModule.GetResource(id));
        }

        public unsafe void DeleteResource(ResourceId id) {
            if (!Win32.UpdateResource(_handle, (ushort) id.Type, id.Name, id.Language, (void*) 0, 0)) {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }
        }
    }

    public class ResourceNotFoundException : Exception {
        internal ResourceNotFoundException(string message) : base(message) {}

        private ResourceNotFoundException(ResourceType resourceType, Win32.ResourceAtom resourceName)
                : base($"Resource '{resourceType} {resourceName}' is not present in the specified image file.") {}

        internal static bool ThrowForHResult(int hr, ResourceType type, Win32.ResourceAtom name) {
            // 0x80070716 = ERROR_RESOURCE_NAME_NOT_FOUND, resource not found
            if (hr == -2147023082) throw new ResourceNotFoundException(type, name);
            else return ResourceTypeNotFoundException.ThrowForHResult(hr, type);
        }
    }

    public class ResourceTypeNotFoundException : ResourceNotFoundException {
        internal ResourceTypeNotFoundException(string message) : base(message) {}

        private ResourceTypeNotFoundException(ResourceType resourceType)
                : base($"Resource type '{resourceType}' is not present in the specified image file.") {}

        internal static bool ThrowForHResult(int hr, ResourceType type) {
            // 0x80070715 = ERROR_RESOURCE_TYPE_NOT_FOUND, no resources of requested type
            if (hr == -2147023083) throw new ResourceTypeNotFoundException(type);
            else return ResourceSectionNotFoundException.ThrowForHResult(hr);
        }
    }

    public class ResourceSectionNotFoundException : ResourceTypeNotFoundException {
        private ResourceSectionNotFoundException() :
                base("The specified image file does not contain a resource section.") {}

        internal static bool ThrowForHResult(int hr) {
            // 0x80070714 = ERROR_RESOURCE_DATA_NOT_FOUND, no resource section, the module does not have any resources
            if (hr == -2147023084) throw new ResourceSectionNotFoundException();
            else return false;
        }
    }

    public enum ResourceType : ushort {
        Cursor = 1,
        Bitmap = 2,
        Icon = 3,
        Menu = 4,
        Dialog = 5,
        String = 6,
        FontDir = 7,
        Font = 8,
        Accelerator = 9,
        RcData = 10,
        MessageTable = 11,

        CursorGroup = 11 + Cursor,
        IconGroup = 11 + Icon,

        Version = 16,
        DlgInclude = 17,
        PlugPlay = 19,
        Vxd = 20,
        CursorAnimated = 21,
        IconAnimated = 22,
        Html = 23,
        Manifest = 24,
    }

    // = MAKELANGID(LANG_NEUTRAL, SUBLANG_NEUTRAL)
    private const ushort NeutralLanguageId = 0;

    public readonly record struct ResourceId(
            ResourceType Type,
            Win32.ResourceAtom Name,
            ushort Language = NeutralLanguageId);
}

[UsedImplicitly]
internal static class ResourceAtomExtensions {
    public static PeResources.ResourceType? ToResourceType(this Win32.ResourceAtom atom) {
        if (atom.IsId()) {
            var id = atom.GetAsId();
            if (Enum.IsDefined(typeof(PeResources.ResourceType), id)) {
                return (PeResources.ResourceType) id;
            } else {
                return null;
            }
        } else {
            return null;
        }
    }
}
