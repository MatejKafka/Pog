﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;

namespace Pog;

// TODO: add support for adding and removing package roots, and cache the loaded package root list
//  it is quite complex to do it in a robust way that supports simultaneous changes from multiple instances of Pog,
//  and even harder to make it performant; the provided package roots should always reflect current state of the
//  config file (so for caching, we'll need a file watcher thread to update the cache)
[PublicAPI]
public class PackageRootConfig {
    public readonly string PackageRootFile;
    private string _resolutionBaseDir;

    public string[] ValidPackageRoots => ReadPackageRoots().Where(Directory.Exists).ToArray();
    public string[] MissingPackageRoots => ReadPackageRoots().Where(r => !Directory.Exists(r)).ToArray();
    public string[] AllPackageRoots => ReadPackageRoots().ToArray();

    internal PackageRootConfig(string packageRootFilePath) {
        PackageRootFile = packageRootFilePath;
        _resolutionBaseDir = Path.GetDirectoryName(packageRootFilePath)!;
    }

    private IEnumerable<string> ReadPackageRoots() {
        InstrumentationCounter.PackageRootFileReads.Increment();
        return File.ReadLines(PackageRootFile, Encoding.UTF8)
                .Select(p => Path.GetFullPath(Path.Combine(_resolutionBaseDir, p)));
    }
}

[PublicAPI]
public class PathConfig {
    public static class PackagePaths {
        internal const string ManifestFileName = "pog.psd1";
        internal const string ManifestResourceDirName = ".pog";
        internal const string RepositoryTemplateDirName = ".template";
        internal const string GeneratorFileName = "generator.psd1";
        internal const string UserManifestFileName = "pog.user.psd1";

        // suffixes, not relative paths (do not add a slash when joining these)
        internal const string ShortcutDirRelSuffix = "";
        internal const string CommandDirRelSuffix = @"\.commands";
        internal const string ShortcutShimDirRelSuffix = @"\.commands\shortcuts";

        internal const string AppDirName = "app";
        internal const string CacheDirName = "cache";
        internal const string LogDirName = "logs";
        internal const string DataDirName = "data";
        internal const string ConfigDirName = "config";

        /// Temporary directory where the previous ./app directory is moved when installing
        /// a new version to support rollback in case of a failed installation.
        internal const string AppBackupDirName = ".POG_INTERNAL_app_old";
        /// Temporary directory used for archive extraction.
        internal const string TmpExtractionDirName = ".POG_INTERNAL_install_tmp";
        /// Temporary directory where the new app directory is composed for multi-source installs before moving it in place.
        internal const string NewAppDirName = ".POG_INTERNAL_app_new";
        /// Temporary path where a deleted directory is first moved so that the deletion
        /// is an atomic operation with respect to the original location.
        internal const string TmpDeleteDirName = ".POG_INTERNAL_delete_tmp";
    }

    public const string DefaultRemoteRepositoryUrl = "https://matejkafka.github.io/PogPackages/";

    /// Path to the main Pog module to import.
    public readonly string MainModulePath;

    public readonly string ContainerDir;
    public readonly string ShimPath;
    public readonly string VcRedistDir;

    /// Directory where exported shortcuts from packages are copied (per-user).
    public readonly string ExportedShortcutDir;
    public readonly string ExportedCommandDir;

    /// Directory where package files with known hash are cached.
    public readonly string DownloadCacheDir;
    /// Directory where package files without known hash are downloaded and stored during installation.
    /// A custom directory is used over system $env:TMP directory, because sometimes we move files
    /// from this dir to download cache, and if the system directory was on a different partition,
    /// this move could be needlessly expensive.
    public readonly string DownloadTmpDir;

    /// Path to the exported 7-Zip binary, needed for package extraction during installation.
    public readonly string Path7Zip;
    /// Path to the exported OpenedFilesView binary, needed to list package processes locking files during installation.
    public readonly string PathOpenedFilesView;

    public readonly PackageRootConfig PackageRoots;

    // if any new paths are added here, also add them to `setup.ps1` in the root directory
    public PathConfig(string pogRootPath, string? dataRootPath = null, string? shortcutExportPath = null) {
        dataRootPath ??= pogRootPath;
        shortcutExportPath ??= $"{Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)}\\Pog";

        ExportedShortcutDir = shortcutExportPath;

        var appPath = $"{pogRootPath}\\app\\Pog";
        MainModulePath = appPath;
        ContainerDir = $"{appPath}\\container";
        ShimPath = $"{appPath}\\lib_compiled\\PogShimTemplate.exe";
        VcRedistDir = $"{appPath}\\lib_compiled\\vc_redist";

        var dataPath = $"{dataRootPath}\\data";
        ExportedCommandDir = $"{dataPath}\\package_bin";
        PackageRoots = new PackageRootConfig($"{dataPath}\\package_roots.txt");
        Path7Zip = $"{ExportedCommandDir}\\7z.exe";
        PathOpenedFilesView = $"{ExportedCommandDir}\\OpenedFilesView.exe";

        var cachePath = $"{dataRootPath}\\cache";
        DownloadCacheDir = $"{cachePath}\\download_cache";
        DownloadTmpDir = $"{cachePath}\\download_tmp";
    }
}
