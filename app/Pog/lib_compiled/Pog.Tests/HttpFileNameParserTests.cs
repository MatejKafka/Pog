using System.Net.Http.Headers;
using Pog.Utils.Http;
using Xunit;

namespace Pog.Tests;

// list of test cases for Content-Disposition parsing: http://test.greenbytes.de/tech/tc2231/
public class HttpFileNameParserTests {
    [Fact]
    public void TestDosNames() {
        TestSingle("_aux", "https://example.com/aux");
        TestSingle("_AUX.txt", "https://example.com/AUX.txt");
        TestSingle("_COM9", "https://example.com/COM9");
    }

    [Fact]
    public void TestPriority() {
        TestSingle("header", "https://host/segment", "attachment; filename=header");
        TestSingle("segment", "https://host/segment");
        TestSingle("segment", "https://host/segment", "attachment; filename=_");
        TestSingle("index.html", "https://host/");
        TestSingle("host", "https://host/_", "attachment; filename=_");
    }

    [Fact]
    public void TestSanitization() {
        // filename*
        TestSingle("hello_world.txt", "https://host/segment", "attachment; filename*=utf-8''hello%2Fworld.txt");
        // filename
        TestSingle("hello_world.txt", "https://host/segment", "attachment; filename=\"hello/world.txt\"");
        // segment
        TestSingle(".._hello_world.txt", "https://host/..%2Fhello%2Fworld.txt");
        TestSingle("invalid_chars_.txt", "https://host/invalid%2fchars%0a.txt");
    }

    private static void TestSingle(string expected, string url, string? dispositionHeader = null) {
        var disposition = dispositionHeader == null ? null : ContentDispositionHeaderValue.Parse(dispositionHeader);
        Assert.Equal(expected, HttpFileNameParser.GetDownloadedFileName(new Uri(url), disposition));
    }
}