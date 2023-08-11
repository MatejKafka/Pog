using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Pog.Native;

namespace Pog.Stub;

/**
 * This class provides support for working with Pog executable stubs. Do not use it directly with the template stub,
 * first copy it, then modify the copy, and then move it into the final destination.
 *
 * ## How the stub works
 * When invoked, it loads the RCDATA resource with ID 1, which should contain the stub data.
 *
 * ### Target path
 * Path to the target executable is loaded from stub data, and passed directly to CreateProcessW in `lpApplicationName`.
 *
 * ### Working directory
 * Working directory is a single absolute path. If set, it is passed directly to the `lpCurrentDirectory` parameter
 * of CreateProcessW, otherwise the current WD is retained.
 *
 * ### Command line
 * Command line prefix is loaded from stub data, which is a single string (escaping is done ahead of time), without the path
 * to the target. The stub retrieves its own command line using GetCommandLine, extracts the first argument (name under which
 * the stub was invoked), then creates the target command line by concatenating 1. argv[0], 2. the prepended command line
 * specified in the stub data, 3. rest of the original command line ("argv[0] stubArgs argv[1...]"). It is passed
 * to the `lpCommandLine` parameter of CreateProcessW.
 *
 * ### Environment
 * Since most of the environment is typically retained, the stub modifies its own environment and then passes NULL
 * to the `lpEnvironment` parameter of CreateProcessW. The environment variables are stored in separate strings
 * in (flags, name, value) triples, which are passed to `SetEnvironmentVariableW`. Possible flags are:
 *  - NONE = replace any previous value
 *  - PREPEND = prepend the value to the previous content of the variable, if any, using `;` as a separator
 *  - APPEND = similar to PREPEND, but the value is appended
 *
 * ## Stub data format
 * Strings in the stub are encoded in UTF-16 and terminated by 2 null bytes, unless specified otherwise. All numbers
 * are encoded as little-endian uint32. If an offset is 0, it indicates that the field is not present.
 *
 * The stub data header is a version number and an array of offsets, in the following order:
 *  - Version number (currently always set to 1)
 *  - Flags
 *  - Target path offset (must NOT be zero)
 *  - Working directory offset (may be zero)
 *  - Command line offset (may be zero)
 *  - Environment variable block offset (may be zero)
 *
 * The following flags are currently defined:
 *  - 0x1 = REPLACE_ARGV0
 *
 * Following the header are the fields referenced by offsets, in any order:
 *  - Target path is a single string.
 *  - Working directory is a single string.
 *  - Command line is prefixed by length in wchar, followed by the command line string, WITHOUT the null terminator.
 *  - Environment variable block has the following format:
 *    - Entry count
 *    - (Entry name offset, Entry value offset), repeated "Entry count" times
 *    The name and value offsets point to strings, placed anywhere in the stub.
 */
internal class StubDataEncoder {
    private static readonly Encoding Encoding = Encoding.Unicode;
    private const ushort NullTerminator = 0;
    private const int HeaderSize = 6 * 4;

    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;

    private StubDataEncoder() {
        _stream = new MemoryStream(256);
        _writer = new BinaryWriter(_stream);
    }

    public static Span<byte> EncodeStub(StubExecutable stub) {
        var encoder = new StubDataEncoder();
        return encoder.Encode(stub);
    }

    private Span<byte> Encode(StubExecutable stub) {
        // seek past the header, write it after we know offsets of all fields
        SeekAbs(HeaderSize);

        // write target path
        Align(sizeof(char));
        var targetOffset = Position;
        WriteNullTerminatedString(stub.TargetPath);

        // write working directory
        Align(sizeof(char));
        var wdOffset = Position;
        if (stub.WorkingDirectory != null) {
            WriteNullTerminatedString(stub.WorkingDirectory);
        }

        // write arguments
        Align(sizeof(uint));
        var argsOffset = Position;
        if (stub.Arguments != null) {
            var encoded = Encoding.GetBytes(Win32Args.EscapeArguments(stub.Arguments));
            Debug.Assert(encoded.Length % 2 == 0);
            // store number of chars, not bytes
            WriteUint(encoded.Length / 2);
            _writer.Write(encoded);
        }

        // write environment variables
        Align(sizeof(uint));
        var envOffset = Position;
        if (stub.EnvironmentVariables != null) {
            WriteEnvironmentVariables(stub.EnvironmentVariables);
        }

        var endOffset = Position;

        // go back and write the header
        SeekAbs(0);
        WriteUint(1); // version
        WriteUint(stub.ReplaceArgv0 ? 1 : 0); // flags
        WriteUint(targetOffset);
        WriteUint(stub.WorkingDirectory == null ? 0 : wdOffset);
        WriteUint(stub.Arguments == null ? 0 : argsOffset);
        WriteUint(stub.EnvironmentVariables == null ? 0 : envOffset);
        Debug.Assert(Position == HeaderSize);

        // seek to the end, so that .GetBuffer() can find the end of the stream
        SeekAbs(endOffset);
        return GetBuffer();
    }

    private Span<byte> GetBuffer() {
        return new Span<byte>(_stream.GetBuffer(), 0, (int) _stream.Position);
    }

    private void WriteNullTerminatedString(string str) {
        _writer.Write(Encoding.GetBytes(str));
        _writer.Write(NullTerminator);
    }

    /// Align writer position so that the values are safe to read from C++ in the stub.
    private void Align(long alignment) {
        // ensure alignment is a power of two
        Debug.Assert((alignment & (alignment - 1)) == 0);
        SeekAbs((Position + alignment - 1) & ~(alignment - 1));
    }

    //@formatter:off
    private long Position => _stream.Position;
    private void SeekAbs(long position) {_stream.Seek(position, SeekOrigin.Begin);}

    private void WriteUint(uint n) {_writer.Write(n);}
    private void WriteUint(int n) {WriteUint((uint) n);}
    private void WriteUint(long n) {WriteUint((uint) n);}
    //@formatter:on

    private void WriteEnvironmentVariables(IDictionary<string, string> envVars) {
        // write entry count
        WriteUint(envVars.Count);

        var nextHeaderPosition = _stream.Position;
        var nextDataPosition = nextHeaderPosition + envVars.Count * 4 * 2;

        // sort entries alphabetically, so that we get a consistent order
        // this is important, because iteration order of dictionaries apparently differs between .NET Framework
        //  and .NET Core, and we want the stubs to be consistent between powershell.exe and pwsh.exe
        foreach (var e in envVars.OrderBy(e => e.Key)) {
            // write the key and value
            SeekAbs(nextDataPosition);
            var keyOffset = _stream.Position;
            WriteNullTerminatedString(e.Key);
            var valueOffset = _stream.Position;
            WriteNullTerminatedString(e.Value);
            nextDataPosition = Position;

            // write key and value offsets to the header
            SeekAbs(nextHeaderPosition);
            WriteUint(keyOffset);
            WriteUint(valueOffset);
            nextHeaderPosition = Position;
        }

        // seek to the end of the block
        SeekAbs(nextDataPosition);
    }
}
