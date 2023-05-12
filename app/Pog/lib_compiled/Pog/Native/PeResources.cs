using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace Pog;

public static partial class Native {
    public static class PeResources {
        [PublicAPI]
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

        [PublicAPI]
        public class Module : IDisposable {
            private readonly Win32.FreeLibrarySafeHandle _handle;

            public Module(string pePath) {
                _handle = Win32.LoadLibrary(pePath);
            }

            public void Dispose() {
                _handle.Dispose();
            }

            /// <returns>
            /// Span wrapping the loaded resource.
            /// NOTE: You must NOT use the span after the Module instance is disposed, as it unmaps the resource from memory.
            /// </returns>
            public unsafe ReadOnlySpan<byte> GetResource(ResourceType resourceType, Win32.ResourceAtom resourceName) {
                var resourceHandle = Win32.FindResource(_handle, resourceName, (ushort) resourceType);
                if (resourceHandle == null) {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
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

            public void IterateResourceNames(ResourceType resourceType, Func<Win32.ResourceAtom, bool> callback) {
                if (!Win32.EnumResourceNames(_handle, (ushort) resourceType,
                            (_, _, name, _) => callback(name), 0)) {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }
            }

            public List<Win32.ResourceAtom> GetResourceNames(ResourceType resourceType) {
                var list = new List<Win32.ResourceAtom>();
                IterateResourceNames(resourceType, name => {
                    list.Add(name);
                    return true;
                });
                return list;
            }
        }

        [PublicAPI]
        public class ResourceUpdater : IDisposable {
            private Win32.ResourceUpdateSafeHandle _handle;

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

            // = MAKELANGID(LANG_NEUTRAL, SUBLANG_NEUTRAL)
            private const ushort NeutralLanguageId = 0;

            public unsafe void SetResource(ResourceType resourceType, Win32.ResourceAtom resourceName,
                    ReadOnlySpan<byte> resource) {
                fixed (byte* resourcePtr = resource) {
                    if (!Win32.UpdateResource(_handle, (ushort) resourceType, resourceName, NeutralLanguageId, resourcePtr,
                                (uint) resource.Length)) {
                        Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                    }
                }
            }

            public void CopyResource(Module srcModule, ResourceType resourceType, Win32.ResourceAtom resourceName) {
                SetResource(resourceType, resourceName, srcModule.GetResource(resourceType, resourceName));
            }

            public unsafe void RemoveResource(ResourceType resourceType, Win32.ResourceAtom resourceName) {
                if (!Win32.UpdateResource(_handle, (ushort) resourceType, resourceName, NeutralLanguageId, (void*) 0, 0)) {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }
            }

            public void RemoveResource(ResourceType resourceType, ushort resourceId) {}
        }
    }
}
