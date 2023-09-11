using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace Pog;

public class InvalidPackageNameException : ArgumentException {
    public InvalidPackageNameException(string message) : base(message) {}
}

[SuppressMessage("ReSharper", "MemberHidesStaticFromOuterClass")]
[SuppressMessage("ReSharper", "ArrangeStaticMemberQualifier")]
public static class Verify {
    public static void PackageName(string packageName) {
        if (!Is.PackageName(packageName)) {
            throw new InvalidPackageNameException($"Invalid package name: '{packageName}'");
        }
    }

    public static void Sha256Hash(string str) {
        if (!Is.Sha256Hash(str)) {
            throw new ArgumentException($"Invalid SHA-256 hash, must be a 64-char hex string, got '{str}'.");
        }
    }

    public static void FileName(string fileName) {
        if (!Is.FileName(fileName)) {
            throw new ArgumentException($"Invalid file name: '{fileName}'");
        }
    }

    public static void FilePath(string filePath) {
        if (!Is.FilePath(filePath)) {
            throw new ArgumentException($"Invalid file path: '{filePath}'");
        }
    }

    public class PackageNameAttribute : ValidateArgumentsAttribute {
        protected override void Validate(object arguments, EngineIntrinsics engineIntrinsics) {
            ValidateList<string>(arguments, PackageName);
        }
    }

    public class Sha256HashAttribute : ValidateArgumentsAttribute {
        protected override void Validate(object arguments, EngineIntrinsics engineIntrinsics) {
            ValidateList<string>(arguments, Sha256Hash);
        }
    }

    public class FileNameAttribute : ValidateArgumentsAttribute {
        protected override void Validate(object arguments, EngineIntrinsics engineIntrinsics) {
            ValidateList<string>(arguments, FileName);
        }
    }

    public class FilePathAttribute : ValidateArgumentsAttribute {
        protected override void Validate(object arguments, EngineIntrinsics engineIntrinsics) {
            ValidateList<string>(arguments, FilePath);
        }
    }

    private static void ValidateList<T>(object arguments, Action<T> validator) {
        if (arguments is T[] list) {
            foreach (var item in list) validator(item);
        } else {
            validator((T) arguments);
        }
    }

    internal static class Is {
        public static bool PackageName(string packageName) {
            return Is.FileName(packageName);
        }

        public static bool Sha256Hash(string str) {
            return str.Length == 64 && str.All("0123456789abcdefABCDEF".Contains);
        }

        public static bool FileName(string fileName) {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            if (fileName is "." or "..") return false;
            return 0 > fileName.IndexOfAny(Path.GetInvalidFileNameChars());
        }

        public static bool FilePath(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            return 0 > filePath.IndexOfAny(Path.GetInvalidPathChars());
        }
    }

    internal static class Assert {
        [Conditional("DEBUG")]
        public static void PackageName(string packageName) {
            Debug.Assert(Is.PackageName(packageName), $"Is.PackageName({packageName})");
        }

        [Conditional("DEBUG")]
        public static void Sha256Hash(string str) {
            Debug.Assert(Is.Sha256Hash(str), $"Is.Sha256Hash({str})");
        }

        [Conditional("DEBUG")]
        public static void FileName(string fileName) {
            Debug.Assert(Is.FileName(fileName), $"Is.FileName({fileName})");
        }

        [Conditional("DEBUG")]
        public static void FilePath(string filePath) {
            Debug.Assert(Is.FilePath(filePath), $"Is.FilePath({filePath})");
        }
    }
}
