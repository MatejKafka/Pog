using System.IO;

namespace Pog.Native;

internal static class PeBinary {
    // documentation of the PE format: https://learn.microsoft.com/en-us/windows/win32/debug/pe-format

    public enum Subsystem : ushort {
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

    public enum Architecture : ushort {
        /// The content of this field is assumed to be applicable to any machine type
        Unknown = 0x0,
        /// Alpha AXP, 32-bit address space
        Alpha = 0x184,
        /// Alpha 64, 64-bit address space
        Alpha64 = 0x284,
        /// Matsushita AM33
        Am33 = 0x1d3,
        /// x64
        Amd64 = 0x8664,
        /// ARM little endian
        Arm = 0x1c0,
        /// ARM64 little endian
        Arm64 = 0xaa64,
        /// ABI that enables interoperability between native ARM64 and emulated x64 code.
        Arm64Ec = 0xA641,
        /// Binary format that allows both native ARM64 and ARM64EC code to coexist in the same file.
        Arm64X = 0xA64E,
        /// ARM Thumb-2 little endian
        ArmNt = 0x1c4,
        /// AXP 64 (Same as Alpha 64)
        Axp64 = 0x284,
        /// EFI byte code
        Ebc = 0xebc,
        /// Intel 386 or later processors and compatible processors
        I386 = 0x14c,
        /// Intel Itanium processor family
        Ia64 = 0x200,
        /// LoongArch 32-bit processor family
        LoongArch32 = 0x6232,
        /// LoongArch 64-bit processor family
        LoongArch64 = 0x6264,
        /// Mitsubishi M32R little endian
        M32R = 0x9041,
        /// MIPS16
        Mips16 = 0x266,
        /// MIPS with FPU
        MipsFpu = 0x366,
        /// MIPS16 with FPU
        MipsFpu16 = 0x466,
        /// Power PC little endian
        PowerPc = 0x1f0,
        /// Power PC with floating point support
        PowerPcFp = 0x1f1,
        /// MIPS I compatible 32-bit big endian
        R3000Be = 0x160,
        /// MIPS I compatible 32-bit little endian
        R3000 = 0x162,
        /// MIPS III compatible 64-bit little endian
        R4000 = 0x166,
        /// MIPS IV compatible 64-bit little endian
        R10000 = 0x168,
        /// RISC-V 32-bit address space
        RiscV32 = 0x5032,
        /// RISC-V 64-bit address space
        RiscV64 = 0x5064,
        /// RISC-V 128-bit address space
        RiscV128 = 0x5128,
        /// Hitachi SH3
        Sh3 = 0x1a2,
        /// Hitachi SH3 DSP
        Sh3Dsp = 0x1a3,
        /// Hitachi SH4
        Sh4 = 0x1a6,
        /// Hitachi SH5
        Sh5 = 0x1a8,
        /// Thumb
        Thumb = 0x1c2,
        /// MIPS little-endian WCE v2
        WceMipsV2 = 0x169,
    }

    public record struct PeInfo(Subsystem Subsystem, Architecture Architecture);

    /// Returns current subsystem of the executable at `pePath`. If newSystem is not null, update the subsystem of the executable.
    private static PeInfo UpdateSubsystemInner(string pePath, Subsystem? newSubsystem = null) {
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

        // offset 0 in COFF header = 2 byte machine type (architecture)
        stream.Seek(coffHeaderOffset, SeekOrigin.Begin);
        var arch = (Architecture) reader.ReadUInt16();

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
        var currentSubsystem = (Subsystem) reader.ReadUInt16();

        if (newSubsystem == null || currentSubsystem == newSubsystem) {
            return new(currentSubsystem, arch); // already correct, no need to update
        }

        var writer = new BinaryWriter(stream);
        // write the new subsystem
        stream.Seek(subsystemFieldOffset, SeekOrigin.Begin);
        writer.Write((ushort) newSubsystem);
        return new(currentSubsystem, arch);
    }

    private static PeInfo UpdateSubsystem(string pePath, Subsystem? newSubsystem = null) {
        try {
            return UpdateSubsystemInner(pePath, newSubsystem);
        } catch (EndOfStreamException) {
            throw new InvalidPeBinaryException($"Invalid PE binary '{pePath}', file is too short", pePath);
        }
    }

    /// <exception cref="InvalidPeBinaryException"></exception>
    public static PeInfo GetInfo(string pePath) {
        return UpdateSubsystem(pePath);
    }

    /// <exception cref="InvalidPeBinaryException"></exception>
    public static Subsystem SetSubsystem(string pePath, Subsystem newSubsystem) {
        return UpdateSubsystem(pePath, newSubsystem).Subsystem;
    }

    public class InvalidPeBinaryException(string message, string filePath) : IOException(message + ": " + filePath);
}
