using System.IO;

namespace Pog.Native;

internal static class PeSubsystem {
    // documentation of the PE format: https://learn.microsoft.com/en-us/windows/win32/debug/pe-format

    public enum WindowsSubsystem : ushort {
        Unknown = 0,
        Native = 1,
        WindowsGui = 2,
        WindowsCui = 3,
        Os2Cui = 5,
        PosixCui = 7,
        NativeWindows = 8,
        WindowsCeGui = 9,
        EfiApplication = 10,
        EfiBootServiceDriver = 11,
        EfiRuntimeDriver = 12,
        EfiRom = 13,
        Xbox = 14,
        WindowsBootApplication = 16,
    }

    /// Returns current subsystem of the executable at `pePath`. If newSystem is not null, update the subsystem of the executable.
    private static WindowsSubsystem UpdateSubsystemInner(string pePath, WindowsSubsystem? newSubsystem = null) {
        // buffer 256 bytes; the subsystem field should almost always be below that
        // only open with write access if we might need to write a new subsystem
        using var stream = new FileStream(pePath, FileMode.Open,
                newSubsystem == null ? FileAccess.Read : FileAccess.ReadWrite, FileShare.ReadWrite, 256);
        var reader = new BinaryReader(stream);

        // PE binary should start with the DOS signature
        stream.Seek(0, SeekOrigin.Begin);
        var dosSignature = reader.ReadUInt16();
        if (dosSignature != 0x5a4d /* MZ */) {
            throw new InvalidPeBinaryException("Binary has an invalid DOS signature", pePath);
        }

        // 0x3c contains the offset of the PE signature as a uint32
        stream.Seek(0x3c, SeekOrigin.Begin);
        var peSignatureOffset = reader.ReadUInt32();

        // validate the PE signature
        stream.Seek(peSignatureOffset, SeekOrigin.Begin);
        var signature = reader.ReadUInt32();
        if (signature != 0x00004550u) {
            throw new InvalidPeBinaryException("Binary has an invalid PE signature", pePath);
        }

        // PE signature is followed by 20 byte COFF header
        var coffHeaderOffset = peSignatureOffset + 4;
        var optionalHeaderOffset = peSignatureOffset + 4 + 20;
        // offset 68 in optional header = 2 byte Subsystem
        var subsystemFieldOffset = optionalHeaderOffset + 68;

        // offset 16 in COFF header = 2 byte SizeOfOptionalHeader
        stream.Seek(coffHeaderOffset + 16, SeekOrigin.Begin);
        var optionalHeaderSize = reader.ReadUInt16();

        if (optionalHeaderSize == 0) {
            throw new InvalidPeBinaryException("Missing PE optional header", pePath);
        }

        stream.Seek(optionalHeaderOffset, SeekOrigin.Begin);
        var optionalHeaderMagic = reader.ReadUInt16();

        if (optionalHeaderMagic != 0x10b && optionalHeaderMagic != 0x20b) {
            throw new InvalidPeBinaryException(
                    $"Unknown optional header format magic value '0x{optionalHeaderMagic:x}'", pePath);
        }

        if (optionalHeaderSize < 68 + 2) {
            throw new InvalidPeBinaryException(
                    $"Optional header too short: {optionalHeaderSize} bytes, expected least 70", pePath);
        }

        // now we can read the current subsystem
        stream.Seek(subsystemFieldOffset, SeekOrigin.Begin);
        var currentSubsystem = (WindowsSubsystem) reader.ReadUInt16();

        if (newSubsystem == null || currentSubsystem == newSubsystem) {
            return currentSubsystem; // already correct, no need to update
        }

        var writer = new BinaryWriter(stream);
        // write the new subsystem
        stream.Seek(subsystemFieldOffset, SeekOrigin.Begin);
        writer.Write((ushort) newSubsystem);
        return currentSubsystem;
    }

    private static WindowsSubsystem UpdateSubsystem(string pePath, WindowsSubsystem? newSubsystem = null) {
        try {
            return UpdateSubsystemInner(pePath, newSubsystem);
        } catch (EndOfStreamException) {
            throw new InvalidPeBinaryException($"Invalid PE binary '{pePath}', file is too short", pePath);
        }
    }

    /// <exception cref="InvalidPeBinaryException"></exception>
    public static WindowsSubsystem GetSubsystem(string pePath) {
        return UpdateSubsystem(pePath);
    }

    /// <exception cref="InvalidPeBinaryException"></exception>
    public static WindowsSubsystem SetSubsystem(string pePath, WindowsSubsystem newSubsystem) {
        return UpdateSubsystem(pePath, newSubsystem);
    }

    public class InvalidPeBinaryException(string message, string filePath) : IOException(message + ": " + filePath);
}
