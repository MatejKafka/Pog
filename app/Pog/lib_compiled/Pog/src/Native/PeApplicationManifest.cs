using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Pog.Native;

/// <summary>Class for reading and manipulating the PE application manifest.</summary>
/// <see href="https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests"/>
public class PeApplicationManifest {
    private static readonly Lazy<XmlNamespaceManager> Namespaces = new(CreateNamespaceManager);

    public readonly string PePath;
    public bool Exists => _xml != null;

    /// If null, the manifest was read from a file (.manifest)
    private readonly PeResources.ResourceId? _srcId;
    private XElement? _xml;

    /// <exception cref="PeResources.ResourceNotFoundException">The PE binary does not have an associated manifest.</exception>
    public PeApplicationManifest(string pePath) {
        PePath = pePath;

        if (LoadEmbeddedManifest(pePath) is var (id, embedded)) {
            _srcId = id;
            _xml = embedded;
        } else if (LoadExternalManifest(pePath) is {} external) {
            _srcId = null;
            _xml = external;
        } else {
            // create an empty manifest
            // since _srcId is set to null, it will be saved as a separate .manifest file; this is better than
            //  embedding it as a PE resource, since it does not invalidate Authenticode signatures on the binary
            _srcId = null;
            _xml = null;
        }
    }

    private XElement? Find(string xpath) {
        return _xml?.XPathSelectElement(xpath, Namespaces.Value);
    }

    public bool EnsureDpiAware() {
        var ws = new Lazy<XElement>(() => {
            _xml ??= new XElement(XName.Get("assembly", "urn:schemas-microsoft-com:asm.v1"),
                    new XAttribute("manifestVersion", "1.0"));

            var ws = new XElement(XName.Get("windowsSettings", "urn:schemas-microsoft-com:asm.v3"));
            _xml.Add(new XElement(XName.Get("application", "urn:schemas-microsoft-com:asm.v3"), ws));
            return ws;
        });

        var changed = false;

        var dpiAwareNode = Find("/asmv3:application/asmv3:windowsSettings/ws2005:dpiAware");
        if (dpiAwareNode == null) {
            changed = true;
            ws.Value.Add(new XElement(XName.Get("dpiAware", "http://schemas.microsoft.com/SMI/2005/WindowsSettings"),
                    new XText("true/pm")));
        } else if (!NodeContainsValue(dpiAwareNode, new[] {"true/pm", "true"})) {
            changed = true;
            dpiAwareNode.SetValue("true/pm");
        }

        var dpiAwarenessNode = Find("/asmv3:application/asmv3:windowsSettings/ws2016:dpiAwareness");
        // no need to add dpiAwareness if it does not exists and dpiAware is already set
        if (dpiAwarenessNode != null && !NodeContainsValue(dpiAwarenessNode, new[] {"permonitorv2", "permonitor"})) {
            changed = true;
            dpiAwarenessNode.SetValue("PerMonitorV2");
        }

        return changed;
    }

    private static bool NodeContainsValue(XElement e, IEnumerable<string> values) {
        return values.Any(v => e.Value.IndexOf(v, StringComparison.InvariantCultureIgnoreCase) >= 0);
    }

    /// Sets up necessary XML namespaces for working with the application manifest.
    private static XmlNamespaceManager CreateNamespaceManager() {
        var manager = new XmlNamespaceManager(new NameTable());
        manager.AddNamespace("asmv1", "urn:schemas-microsoft-com:asm.v1");
        manager.AddNamespace("asmv3", "urn:schemas-microsoft-com:asm.v3");
        manager.AddNamespace("ws2005", "http://schemas.microsoft.com/SMI/2005/WindowsSettings");
        manager.AddNamespace("ws2016", "http://schemas.microsoft.com/SMI/2016/WindowsSettings");
        return manager;
    }

    private static unsafe (PeResources.ResourceId, XElement)? LoadEmbeddedManifest(string pePath) {
        using var module = new PeResources.Module(pePath);

        var id = FindEmbeddedManifest(module);
        if (id == null) {
            return null;
        }

        var manifestSpan = module.GetResource(id.Value);
        fixed (byte* p = manifestSpan) {
            var stream = new UnmanagedMemoryStream(p, manifestSpan.Length);
            return (id.Value, XElement.Load(stream, LoadOptions.PreserveWhitespace));
        }
    }

    private static PeResources.ResourceId? FindEmbeddedManifest(PeResources.Module module) {
        PeResources.ResourceId? id = null;
        try {
            module.IterateResourceNames(PeResources.ResourceType.Manifest, name => {
                module.IterateResourceLanguages(PeResources.ResourceType.Manifest, name, lang => {
                    id = new(PeResources.ResourceType.Manifest, name, lang);
                    // stop after finding the first manifest
                    return false;
                });
                return false;
            });
        } catch (PeResources.ResourceNotFoundException) {
            // thrown if the binary does not have some of the iterated sections
            return null;
        }
        return id;
    }

    private static XElement? LoadExternalManifest(string pePath) {
        try {
            return XElement.Load(pePath + ".manifest");
        } catch (FileNotFoundException) {
            return null;
        }
    }

    public void Save() {
        if (_xml == null) {
            // executable had no manifest while loading, and no changes were made
            return;
        }

        if (_srcId == null) {
            _xml.Save(PePath + ".manifest");
            // update the modification time of the file, otherwise if it was already executed at least once,
            //  Windows will remember to use display scaling and ignore the manifest (keyword: AppCompatCache)
            File.SetLastWriteTimeUtc(PePath, DateTime.UtcNow);
        } else {
            var buffer = new MemoryStream();
            _xml.Save(buffer, SaveOptions.DisableFormatting);
            var bufferSpan = new ReadOnlySpan<byte>(buffer.GetBuffer(), 0, (int) buffer.Length);

            using var updater = new PeResources.ResourceUpdater(PePath);
            updater.SetResource(_srcId.Value, bufferSpan);
            updater.CommitChanges();
        }
    }
}
