using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Pog.Native;

namespace Pog.Shim;

/**
 * This class provides support for working with Pog executable shims. Do not use it directly with the template shims,
 * first copy it, then modify the copy, and then move it into the final destination.
 *
 * ## How the shim works
 * Source of the shim is in the `lib_compiled/Pog.Shim` directory.
 * When invoked, the shim loads the RCDATA resource with ID 1, which should contain the shim data, parses it, and then
 * creates a job object, invokes CreateProcessW to spawn the target process, waits until the target process exits and
 * forwards the exit code.
 *
 * ## Shim data parameters
 *
 * ### Target path
 * Path to the target executable is loaded from shim data, and passed directly to CreateProcessW in `lpApplicationName`.
 *
 * ### Working directory
 * Working directory is a single absolute path. If set, it is passed directly to the `lpCurrentDirectory` parameter
 * of CreateProcessW, otherwise the current working directory is retained.
 *
 * ### Command line
 * Command line prefix is loaded from shim data, which is a single string (escaping is done ahead of time), without the path
 * to the target. The shim retrieves its own command line using GetCommandLine, extracts the first argument (name under which
 * the shim was invoked), then creates the target command line by concatenating 1. argv[0], 2. the prepended command line
 * specified in the shim data, 3. rest of the original command line ("argv[0] shimArgs argv[1...]"). If `ReplaceArgv0`
 * is true, target path is used instead of the original argv[0]. It is passed to the `lpCommandLine` parameter of CreateProcessW.
 *
 * ### Environment
 * Since most of the environment is typically retained, the shim modifies its own environment and then passes NULL
 * to the `lpEnvironment` parameter of CreateProcessW. The shim supports interpolating existing environment variables
 * into the value (e.g. "%APPDATA%/Path") and concatenating multiple segments of a list variable. The value string is parsed
 * in `ShimExecutable` into a format that's more efficient for the shim (essentially, a list of tokens, where each token
 * is either an environment variable name, or a literal string).
 *
 * ## Shim data format
 * Strings in the shim are encoded in UTF-16 and terminated by 2 null bytes, unless specified otherwise.
 * All offsets and sizes/lengths/counts are stored as uint32. If an offset is 0, it indicates that the field is not present.
 *
 * The shim data header is a version number, flag bitfield and an array of offsets, in the following order:
 *  - uint16 Version number
 *  - uint16 Flags (<see cref="ShimFlags"/>)
 *  - Target path offset (must NOT be zero)
 *  - Working directory offset (may be zero)
 *  - Command line offset (may be zero)
 *  - Environment variable block offset (may be zero)
 *
 * Following the header are the fields referenced by offsets, in any order:
 *  - Target path is a single string.
 *  - Working directory is a single string.
 *  - Command line is prefixed by length in wchar, followed by the command line string, WITHOUT the null terminator.
 *  - Environment variable block has the following format:
 *    - Entry count
 *    - (Entry name offset, Entry value offset), repeated "Entry count" times
 *    The name offset points to a string, placed anywhere in the shim. The value offset points to a structure described below.
 *
 * ### Environment variable value format
 * The value is stored in a linked-list-like structure of string segments with the following format:
 *  - String length (in wchar), without the null terminator
 *  - uint16 Segment flags (<see cref="SegmentFlags"/>)
 *  - String (for meaning, <see cref="SegmentFlags"/>)
 *  After each segment, padding is optionally inserted to align it to `sizeof(uint)`.
 */
internal class ShimDataEncoder {
    public const ushort CurrentShimDataVersion = 4;
    private static readonly Encoding Encoding = Encoding.Unicode;
    private const ushort NullTerminator = 0;
    private const int HeaderSize = 2 * 2 + 4 * 4;

    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;

    private ShimDataEncoder() {
        _stream = new MemoryStream(256);
        _writer = new BinaryWriter(_stream);
    }

    public static uint ParseVersion(ReadOnlySpan<byte> shimData) {
        Debug.Assert(shimData.Length > sizeof(ushort));
        // version is the first uint in the header
        return Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference(shimData));
    }

    public static Span<byte> EncodeShim(ShimExecutable shim) {
        var encoder = new ShimDataEncoder();
        return encoder.Encode(shim);
    }

    private Span<byte> Encode(ShimExecutable shim) {
        // seek past the header, write it after we know offsets of all fields
        SeekAbs(HeaderSize);

        // write target path
        var targetOffset = WriteNullTerminatedString(shim.TargetPath);
        // write working directory
        var wdOffset = shim.WorkingDirectory == null ? 0 : WriteNullTerminatedString(shim.WorkingDirectory);
        // write arguments
        var argsOffset = shim.Arguments == null ? 0 : WriteLengthPrefixedString(Win32Args.EscapeArguments(shim.Arguments));
        // write environment variables
        var envOffset = shim.EnvironmentVariables == null ? 0 : WriteEnvironmentVariables(shim.EnvironmentVariables);
        var endOffset = Position;

        var flags = (ShimFlags) 0;
        if (shim.ReplaceArgv0) flags |= ShimFlags.ReplaceArgv0;
        if (shim.Argv0AsTarget) flags |= ShimFlags.NullTarget;

        // go back and write the header
        SeekAbs(0);
        WriteUshort(CurrentShimDataVersion); // version, must be the first (see ParseVersion)
        WriteUshort((ushort) flags);
        WriteUint(targetOffset);
        WriteUint(wdOffset);
        WriteUint(argsOffset);
        WriteUint(envOffset);
        Debug.Assert(Position == HeaderSize);

        // seek to the end, so that .GetBuffer() can find the end of the stream
        SeekAbs(endOffset);
        return GetBuffer();
    }

    private Span<byte> GetBuffer() {
        return new Span<byte>(_stream.GetBuffer(), 0, (int) _stream.Position);
    }

    private long WriteLengthPrefixedString(string str) {
        Align(sizeof(uint));
        var startOffset = Position;
        var encoded = Encoding.GetBytes(str);
        Debug.Assert(encoded.Length % 2 == 0);
        // store number of chars, not bytes
        WriteUint(encoded.Length / 2);
        _writer.Write(encoded);
        return startOffset;
    }

    private long WriteNullTerminatedString(string str) {
        Align(sizeof(char));
        var startOffset = Position;
        _writer.Write(Encoding.GetBytes(str));
        _writer.Write(NullTerminator);
        return startOffset;
    }

    /// Align writer position so that the values are safe to read from C++ in the shim.
    private void Align(long alignment) {
        // ensure alignment is a power of two
        Debug.Assert((alignment & (alignment - 1)) == 0);
        SeekAbs((Position + alignment - 1) & ~(alignment - 1));
    }

    //@formatter:off
    private long Position => _stream.Position;
    private void SeekAbs(long position) {_stream.Seek(position, SeekOrigin.Begin);}

    private void WriteUshort(ushort n) {_writer.Write(n);}
    private void WriteUint(uint n) {_writer.Write(n);}
    private void WriteUint(int n) {WriteUint((uint) n);}
    private void WriteUint(long n) {WriteUint((uint) n);}
    //@formatter:on

    private long WriteEnvironmentVariables(IReadOnlyCollection<(string, ShimExecutable.EnvVarTemplate)> envVars) {
        Align(sizeof(uint));
        var startOffset = Position;

        // write entry count
        WriteUint(envVars.Count);

        var nextHeaderPosition = Position;
        var nextDataPosition = nextHeaderPosition + envVars.Count * 4 * 2;

        foreach (var (key, value) in envVars) {
            // write the key and value
            SeekAbs(nextDataPosition);
            var keyOffset = Position;
            WriteNullTerminatedString(key);
            var valueOffset = WriteEnvironmentVariableValue(value);
            nextDataPosition = Position;

            // write key and value offsets to the header
            SeekAbs(nextHeaderPosition);
            WriteUint(keyOffset);
            WriteUint(valueOffset);
            nextHeaderPosition = Position;
        }

        // seek to the end of the block
        SeekAbs(nextDataPosition);
        return startOffset;
    }

    private long WriteEnvironmentVariableValue(ShimExecutable.EnvVarTemplate value) {
        Align(sizeof(uint));
        var startOffset = Position;

        for (var i = 0; i < value.Segments.Count; i++) {
            var s = value.Segments[i];
            var flags = 0
                        | (s.NewSegment ? SegmentFlags.NewListItem : 0)
                        | (s.IsEnvVarName ? SegmentFlags.EnvVarName : 0)
                        | (i == value.Segments.Count - 1 ? SegmentFlags.LastSegment : 0)
                        | (i == 0 && value.Recessive ? SegmentFlags.Recessive : 0);

            WriteUint(s.String.Length);
            WriteUshort((ushort) flags);
            // for simplicity, just write all segment strings null-terminated
            WriteNullTerminatedString(s.String);
            // align for the next segment
            Align(sizeof(uint));
        }

        return startOffset;
    }

    [Flags]
    private enum ShimFlags : ushort {
        /// Replace argv[0] (the name of the binary) in the original command line the shim was invoked
        /// with with path to the target. If the target does not rely on argv[0] matching the binary path,
        /// it is better to leave the original argv[0] to e.g. get the correct path in help text.
        ReplaceArgv0 = 1,
        /// Pass null to the `lpApplicationName` parameter of `CreateProcess`. This has two important effects:
        /// 1) If target is not an absolute path, it is looked up in PATH.
        /// 2) If target is a batch file, `CreateProcess` correctly constructs the command line to work around the stupid
        ///    cmd.exe behavior for `/c` where the outermost quotes of the command line are stripped (see `cmd /?`, part
        ///    starting with "If /C or /K is specified"). When `lpApplicationName` is not null, this handling is suppressed
        ///    (I'd love to hear the history behind that design choice).
        NullTarget = 2,
    }

    [Flags]
    private enum SegmentFlags : ushort {
        /// This segment should be expanded as an environment variable, the string contains the variable name.
        /// If not set, the string is copied to the environment variable value directly
        EnvVarName = 1,
        /// This segment starts a new list item (a new ;-separated item for variables like %PATH%).
        NewListItem = 2,
        /// This is the last segment of the value (stops traversal).
        LastSegment = 4,
        /// If the corresponding environment variable already exists, keep its value and stop processing this variable.
        /// Only valid on the first segment.
        Recessive = 8,
    }
}
