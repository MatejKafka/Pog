using System;
using Xunit;
using VT = Pog.PackageVersion.DevVersionType;

namespace Pog.Tests;

public static class PackageVersionTests {
    public class Parsing {
        private static void Test(string versionStr, int[] main, IComparable[] dev, string? buildMeta = null) {
            var v = new PackageVersion(versionStr);
            Assert.Equal(main, v.Main);
            Assert.Equal(dev, v.Dev);
            Assert.Equal(buildMeta, v.BuildMetadata);
        }

        [Fact] public void PowerShellRc() => Test("7.1.0-rc5", [7, 1, 0], [VT.Rc, 5]);
        [Fact] public void PyPy() => Test("3.6-v3.7.1", [3, 6], ["v", 3, 7, 1]);
        [Fact] public void FirefoxDev() => Test("89.0a1-2021-04-05", [89, 0], [VT.Alpha, 1, 2021, 4, 5]);

        [Fact] public void SemVerBuildMetadata() {
            Test("3.7.0+a0b1c2d3", [3, 7, 0], [], "a0b1c2d3");
            Test("3.7.0-beta.1+a0b1c2d3", [3, 7, 0], [VT.Beta, 1], "a0b1c2d3");
            // ensure we don't try to parse the meta as an int
            Test("3.7.0+99999999999999999999", [3, 7, 0], [], "99999999999999999999");
        }
    }

    public class Comparison {
        private static int Compare(string v1, string v2) {
            return new PackageVersion(v1).CompareTo(new PackageVersion(v2));
        }

        [Fact] public void DifferentLengths() {
            Assert.True(Compare("1.0", "1") == 0);
            Assert.True(Compare("1.1.0", "1.1") == 0);
            Assert.True(Compare("1.4.1", "1.4") > 0);
            Assert.True(Compare("1.4", "1.4.1") < 0);
            Assert.True(Compare("1.4.1-beta5", "1.4.1") < 0);
            Assert.True(Compare("1.4-beta2.1", "1.4-beta2") > 0);
        }

        [Fact] public void JetBrains() {
            Assert.True(Compare("2020.4.1", "2020.2.1") > 0);
            Assert.True(Compare("2020.2.1", "2020.4.1") < 0);
            Assert.True(Compare("2019.2.1", "2020.4.1") < 0);
            Assert.True(Compare("2020.2.2", "2020.2.2") == 0);
        }

        [Fact] public void PowerShell() {
            Assert.True(Compare("7.1.1", "7.1.0rc5") > 0);
            Assert.True(Compare("7.1.0", "7.1.0rc5") > 0);
            Assert.True(Compare("7.1.0rc1", "7.1.0rc5") < 0);
            Assert.True(Compare("5.1.0", "7.0.0") < 0);
            Assert.True(Compare("1.2.0rc2", "7.1.0rc1") < 0);
            Assert.True(Compare("7.1.0rc2", "7.1.0rc1") > 0);
        }

        [Fact] public void Firefox() {
            Assert.True(Compare("78.0a2", "78.0a1") > 0);
            Assert.True(Compare("78.0b1", "78.0a1") > 0);
            Assert.True(Compare("78.0b1", "78.0a2") > 0);
            Assert.True(Compare("78.0", "78.0a2") > 0);
        }

        [Fact] public void PyPy() {
            Assert.True(Compare("3.6-v3.7.1", "3.6-v4.0.0") < 0);
            Assert.True(Compare("3.6-v3.7.1", "3.6-v3.7.2") < 0);
            Assert.True(Compare("3.6-v3.7.1", "3.6-v3.7.1-b1") > 0);
        }

        [Fact] public void SevenZip() {
            Assert.True(Compare("2107", "1900") > 0);
            Assert.True(Compare("2107", "2200") < 0);
            // dotted versions
            Assert.True(Compare("21.07", "19.00") > 0);
            Assert.True(Compare("21.07", "22.00") < 0);
        }

        [Fact] public void Wireshark() {
            Assert.True(Compare("3.7.0rc0-1634", "3.7.0rc0-1641") < 0);
            Assert.True(Compare("3.7.0rc0-1640", "3.7.0rc0-1636") > 0);
        }

        [Fact] public void SemVerBuildMetadata() {
            // check that build metadata is ignored (versions compare as equivalent, although not as equal)
            Assert.True(Compare("1.2.3+a", "1.2.3+b") == 0);
        }
    }
}