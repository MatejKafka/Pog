using System;
using System.Collections.Generic;
using System.Linq;
using Pog.Native;
using Xunit;
using Win32 = Pog.Tests.TestUtils.Win32;

namespace Pog.Tests.Native;

public class Win32ArgsTests {
    private static readonly Random Random = new();

    // special chars are repeated multiple times to make them more likely to be generated
    private const string ArgChars =
            "\"\"\"\\\\\\   \t\t\t///\r\n" + "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private static string GenerateRandomString(int length) {
        var buffer = new char[length];
        for (var i = 0; i < buffer.Length; i++) {
            buffer[i] = ArgChars[Random.Next(ArgChars.Length)];
        }
        return new string(buffer);
    }

    [Fact]
    public void TestEmpty() {
        Assert.Equal("", Win32Args.EscapeArguments(Array.Empty<string>()));
    }

    [Fact]
    public void TestWhitespace() {
        TestArgs(["cmd", " ", "   ", " \t ", "\n", "test\ntest", "\t\t\t"]);
    }

    [Fact]
    public void TestQuote() {
        TestArgs(["test\"test"]);
    }

    [Fact]
    public void TestRandom() {
        for (var i = 0; i < 1000; i++) {
            var args = new string[Random.Next(1, 30)];
            for (var j = 0; j < args.Length; j++) {
                args[j] = GenerateRandomString(30);
            }

            TestArgs(args);
        }
    }

    private static void TestArgs(IReadOnlyCollection<string> args) {
        var commandLine = Win32Args.EscapeArguments(args);
        // CommandLineToArgv has special rules for the first argument, give it a dummy
        // TODO: shouldn't we add a flag?
        var parsedWithCmd = Win32.CommandLineToArgv("cmd " + commandLine);
        var parsed = new ArraySegment<string>(parsedWithCmd, 1, parsedWithCmd.Length - 1);

        Assert.Equal(args.Count, parsed.Count);
        foreach (var (orig, processed) in args.Zip(parsed, ValueTuple.Create)) {
            Assert.Equal(orig, processed);
        }
    }
}