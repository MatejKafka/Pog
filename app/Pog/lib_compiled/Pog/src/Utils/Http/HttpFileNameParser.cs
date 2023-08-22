using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Web;

namespace Pog.Utils.Http;

/// <summary>
/// Utility class for getting the server-provided file name for a downloaded file.
/// </summary>
public static class HttpFileNameParser {
    /// <param name="resolvedUri">Resolved URI (after redirects) of the downloaded file.</param>
    /// <param name="contentDisposition">The Content-Disposition header from the server response, if any.</param>
    public static string GetDownloadedFileName(Uri resolvedUri, ContentDispositionHeaderValue? contentDisposition) {
        // link to a similar function in Chromium:
        // https://github.com/chromium/chromium/blob/11a147047d7ed9d429b53e536fe1ead54fad5053/net/base/filename_util_internal.cc#L219
        // Chromium also takes into account the mime type of the response, we're not doing that for now

        // try available names in the order of preference, use the first one found that is valid
        string? fileName = null;

        // if Content-Disposition is set and valid, use the specified filename
        fileName ??= SanitizeDownloadedFileName(GetRawDispositionFileName(contentDisposition));

        // use the last segment of the resolved URL
        var lastSegment = resolvedUri.Segments.LastOrDefault();
        fileName ??= SanitizeDownloadedFileName(HttpUtility.UrlDecode(lastSegment));

        // use the hostname
        fileName ??= SanitizeDownloadedFileName(resolvedUri.Host);

        // fallback default name
        fileName ??= "download";

        return fileName;
    }

    private static string? GetRawDispositionFileName(ContentDispositionHeaderValue? contentDisposition) {
        if (contentDisposition is not {DispositionType: "attachment"}) {
            return null;
        }

        var headerVal = contentDisposition switch {
            {FileNameStar: not null} => contentDisposition.FileNameStar,
            {FileName: not null} => contentDisposition.FileName,
            _ => null,
        };

        if (headerVal == null) {
            return null;
        }

        if (headerVal.StartsWith("\"") && headerVal.EndsWith("\"")) {
            // ContentDispositionHeaderValue parser leaves the quotes in the parsed filename, strip them
            headerVal = headerVal.Substring(1, headerVal.Length - 2);
        }

        return headerVal;
    }

    private static readonly Regex InvalidDosNameRegex = new Regex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..+)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? SanitizeDownloadedFileName(string? fileName) {
        // sources for relevant functions in Chromium:
        // GetFileNameFromURL: https://github.com/chromium/chromium/blob/11a147047d7ed9d429b53e536fe1ead54fad5053/net/base/filename_util_internal.cc#L119
        // GenerateSafeFileName: https://github.com/chromium/chromium/blob/bf9e98c98e8d7e79befeb057fde42b0e320d9b19/net/base/filename_util.cc#L163
        // SanitizeGeneratedFileName: https://github.com/chromium/chromium/blob/11a147047d7ed9d429b53e536fe1ead54fad5053/net/base/filename_util_internal.cc#L79

        // list of invalid filenames on Windows: https://stackoverflow.com/a/62888

        if (fileName == null) {
            return null;
        }

        // Win32 does not like trailing '.' and ' ', remove it
        fileName = fileName.TrimEnd('.', ' ');
        // replace any invalid characters with _
        fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));

        if (InvalidDosNameRegex.IsMatch(fileName)) {
            // is a DOS file name, prefix with _ to lose the special meaning
            fileName = "_" + fileName;
        }

        // if fileName is empty or only consists of invalid chars (or _), skip it
        if (fileName.All(c => c == '_')) {
            return null;
        }
        return fileName;
    }
}
