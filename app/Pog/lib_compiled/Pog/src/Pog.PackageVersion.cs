using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using IOPath = System.IO.Path;

namespace Pog;

/// <summary>
/// Used to parse and represent package versions. There's quite a lot of heuristics here,
/// but it behaves sanely for all the version formats I encountered yet.
/// </summary>
[PublicAPI]
public class PackageVersion : IComparable<PackageVersion>, IComparable, IEquatable<PackageVersion>, IEquatable<string> {
    /// Main part of the version – dot-separated numbers, not necessarily semver.
    public readonly int[] Main;
    /// Development version suffix – "beta.1", "preview-1.2.3",...
    /// type: array of one of (string, int, DevVersionType)
    public readonly IComparable[] Dev; // FIXME: this is a bad type
    /// SemVer-like build metadata after a + sign (https://semver.org/#spec-item-10).
    public readonly string? BuildMetadata;
    /// The original unchanged version string.
    private readonly string _versionString;

    public enum DevVersionType {
        Nightly = 0, Preview = 1, Alpha = 2, Beta = 3, Rc = 4,
    }

    private static readonly Regex VersionRegex = new(
            @"^(?<Main>\d+(\.\d+)*)(?<Dev>[^+]*)(?:\+(?<BuildMeta>.*))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, DevVersionType> DevVersionTypeMap = new() {
        {"nightly", DevVersionType.Nightly},
        {"preview", DevVersionType.Preview},
        {"alpha", DevVersionType.Alpha},
        {"a", DevVersionType.Alpha},
        {"beta", DevVersionType.Beta},
        {"b", DevVersionType.Beta},
        {"rc", DevVersionType.Rc},
    };

    /// <exception cref="InvalidPackageVersionException"></exception>
    public PackageVersion(string versionString) {
        if (string.IsNullOrEmpty(versionString)) {
            throw new InvalidPackageVersionException("Package version must not be empty.");
        }

        this._versionString = versionString;

        var i = versionString.IndexOfAny(IOPath.GetInvalidFileNameChars());
        if (i >= 0) {
            throw new InvalidPackageVersionException("Package version must be a valid directory name, cannot contain"
                                                     + " invalid characters like '" + versionString[i] + "': " +
                                                     versionString);
        }
        if (versionString is "." or "..") {
            throw new InvalidPackageVersionException("Package version must be a valid directory name, got '" +
                                                     versionString + "'.");
        }

        // should always pass, checked above
        Verify.Assert.FileName(versionString);

        var match = VersionRegex.Match(versionString);
        if (!match.Success) {
            throw new InvalidPackageVersionException("Could not parse package version: " + versionString);
        }

        // the regex should ensure this always works
        this.Main = match.Groups["Main"].Value.Split('.').Select(int.Parse).ToArray();

        var devVersion = new List<IComparable>();
        bool? isNumericToken = null;
        var token = "";

        void Flush() {
            if (token == "") return;
            devVersion.Add(isNumericToken.GetValueOrDefault(false)
                    ? int.Parse(token)
                    : DevVersionTypeMap.TryGetValue(token, out var value)
                            ? value
                            : token);
        }

        foreach (var c in match.Groups["Dev"].Value) {
            if (".,-_".Contains(c)) {
                // split on these chars
                Flush();
                isNumericToken = null;
                token = "";
            } else if (char.IsDigit(c) == isNumericToken) {
                token += c;
            } else {
                Flush();
                isNumericToken = char.IsDigit(c);
                token = c.ToString();
            }
        }

        Flush();
        this.Dev = devVersion.ToArray();

        // SemVer 2.0 allows versions to store arbitrary build metadata (e.g., Git commit hash) after a `+` sign in the version
        this.BuildMetadata = match.Groups["BuildMeta"] is {Success: true, Value: var v} ? v : null;
    }

    public override string ToString() {
        return this._versionString;
    }


    public int CompareTo(PackageVersion? v2) {
        if (v2 == null) {
            return 1;
        }

        var v1 = this;
        // compare the main (semi-semver) part
        // if one of the versions is shorter than the other, treat the extra fields as zeros
        foreach (var (p1, p2) in ZipLongest(v1.Main, v2.Main, 0, 0)) {
            if (p1 == p2) continue;
            return p1.CompareTo(p2);
        }

        // same semi-semver, no dev suffix, the versions are equal
        if (v1.Dev.Length == 0 && v2.Dev.Length == 0) return 0;
        // V2 is dev version of V1 -> V1 is greater
        if (v1.Dev.Length == 0) return 1;
        // V1 is dev version of V2 -> V2 is greater
        if (v2.Dev.Length == 0) return -1;

        // both have dev suffix and the same semi-semver, compare dev suffixes
        foreach (var (p1, p2) in ZipLongest(v1.Dev, v2.Dev, -1, -1)) {
            // here's the fun part, because the dev suffixes are quite free-style
            // each possible version field type has an internal ordering
            // if both fields have a different type, the following priorities are used:
            //  string < DevVersionType < null < int, where later values are considered greater than earlier ones
            //  effectively:
            //   - $null is treated as -1 (int)
            //   - DevVersionType vs string – could be basically anything, assume that DevVersionType is newer
            //   - DevVersionType vs int – assume that int is a newer version ("almost-release"?)
            //   - int vs string – assume that int is a newer version ("almost-release", there are no qualifiers)
            if (Equals(p1, p2)) {
                continue;
            }

            var typeOrder = new Dictionary<Type, int> {
                {typeof(string), 0}, {typeof(DevVersionType), 1}, {typeof(int), 2},
            };
            var o1 = typeOrder[p1.GetType()];
            var o2 = typeOrder[p2.GetType()];
            if (o1 != o2) {
                // different field types, compare based on the field type ordering above
                return o1.CompareTo(o2);
            } else {
                // same field types, use the default comparator for the type
                return p1.CompareTo(p2);
            }
        }

        // both Main and Dev parts are equal
        return 0;
    }

    // we want a non-generic comparison, since that's what PowerShell likes to use
    public int CompareTo(object? obj) {
        if (obj is PackageVersion v) return CompareTo(v);
        if (obj is string s) return CompareTo(s);
        if (obj is null) return 1;
        throw new ArgumentException("Object is not comparable with Pog.PackageVersion.");
    }


    public override bool Equals(object? obj) {
        if (obj is string str && Equals(str)) return true;
        if (obj is PackageVersion other && Equals(other)) return true;
        return false;
    }

    public bool Equals(PackageVersion? v2) {
        return _versionString == v2?._versionString;
    }

    public bool Equals(string? v2) {
        return _versionString == v2;
    }

    public override int GetHashCode() {
        return _versionString.GetHashCode();
    }


    public static bool operator ==(PackageVersion? v1, PackageVersion? v2) => v1?.Equals(v2) ?? v2 is null;
    public static bool operator !=(PackageVersion? v1, PackageVersion? v2) => !(v1 == v2);
    public static bool operator <(PackageVersion? v1, PackageVersion? v2) => v1?.CompareTo(v2) < 0;
    public static bool operator >(PackageVersion? v1, PackageVersion? v2) => v1?.CompareTo(v2) > 0;
    public static bool operator <=(PackageVersion? v1, PackageVersion? v2) => v1?.CompareTo(v2) <= 0;
    public static bool operator >=(PackageVersion? v1, PackageVersion? v2) => v1?.CompareTo(v2) >= 0;

    private static IEnumerable<(T1, T2)> ZipLongest<T1, T2>(T1[] first, T2[] second, T1 pad1, T2 pad2) {
        var firstExp = first.Concat(Enumerable.Repeat(pad1, Math.Max(second.Length - first.Length, 0)));
        var secExp = second.Concat(Enumerable.Repeat(pad2, Math.Max(first.Length - second.Length, 0)));
        return firstExp.Zip(secExp, (a, b) => (a, b));
    }

    public class DescendingComparer : IComparer<PackageVersion> {
        public int Compare(PackageVersion x, PackageVersion y) {
            return y.CompareTo(x);
        }
    }
}

public class InvalidPackageVersionException(string message) : FormatException(message);
