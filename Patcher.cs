using System.Diagnostics;
using System.Runtime.InteropServices;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using ELFSharp.MachO;
using SharpPdb.Native;
using ElFMachine = ELFSharp.ELF.Machine;
using MachOMachine = ELFSharp.MachO.Machine;

namespace FactorioAchievementPatcher;

public interface Patcher : IDisposable {

    public static Patcher Create(string modulePath, byte[] moduleBytes) {
        if (Path.GetExtension(modulePath).Equals(".exe")) {
            return new WindowsPatcher(modulePath);
        }

        if (ELFReader.TryLoad(new MemoryStream(moduleBytes), true, out ELF<ulong> elf)) {
            return new LinuxPatcher(elf);
        }

        if (MachOHelper.ReadFatMachO(moduleBytes, out var binaries)) {
            return new MacosPatcher(binaries);
        }

        throw new ArgumentException("Unknown Executable file provided.");
    }

    OSPlatform Platform { get; }

    IEnumerable<Architecture> Architectures();

    Range FunctionFileRange(Architecture arch, string fName);

}

public sealed class WindowsPatcher : Patcher {

    private readonly PEFile        pe;
    private readonly PdbFileReader pdb;

    private readonly PESection text;

    public WindowsPatcher(string modulePath) {
        if (Path.GetExtension(modulePath) is not ".exe")
            throw new ArgumentException($"{Path.GetFileName(modulePath)} does not end in .exe");

        var pdbPath = Path.Combine(Path.GetDirectoryName(modulePath)!, Path.GetFileNameWithoutExtension(modulePath) + ".pdb");
        if (!File.Exists(pdbPath))
            throw new ArgumentException($"{Path.GetFileName(modulePath)} does not have a companion .pdb file.");

        pe = PEFile.FromFile(modulePath);
        pdb = new PdbFileReader(pdbPath);

        text = pe.Sections.Single(e => e.Name.Equals(".text"));
    }

    public OSPlatform Platform => OSPlatform.Windows;

    public IEnumerable<Architecture> Architectures() {
        yield return pe.FileHeader.Machine switch {
            MachineType.Amd64 => Architecture.X64,
            MachineType.Arm64 => Architecture.Arm64,
            _ => throw new ArgumentException($"Unknown PE architecture type. {Enum.GetName(typeof(MachineType), pe.FileHeader.Machine)}")
        };
    }

    public Range FunctionFileRange(Architecture arch, string fName) {
        Debug.Assert(Architectures().Contains(arch), "Tried to patch for unknown architecture.");

        var func = pdb.Functions.Single(e => e.Name.Equals(fName));
        var fnOffset = (int)pe.RvaToFileOffset(func.Offset + text.Rva);
        return new Range(fnOffset, fnOffset + (int)func.CodeSize);
    }

    public void Dispose() {
        pdb.Dispose();
    }

}

public sealed class LinuxPatcher : Patcher {

    private readonly ELF<ulong>         elf;
    private readonly int                symOffset;
    private readonly SymbolTable<ulong> symTable;

    public LinuxPatcher(ELF<ulong> elf) {
        this.elf = elf;

        var text = (ProgBitsSection<ulong>)elf.GetSection(".text");
        symOffset = (int)(text.Offset - text.LoadAddress);
        symTable = (SymbolTable<ulong>)elf.GetSection(".symtab");
    }

    public OSPlatform Platform => OSPlatform.Linux;

    public IEnumerable<Architecture> Architectures() {
        yield return elf.Machine switch {
            ElFMachine.AMD64 => Architecture.X64,
            ElFMachine.AArch64 => Architecture.Arm64,
            _ => throw new ArgumentException($"Unknown ELF architecture type. {Enum.GetName(typeof(ElFMachine), elf.Machine)}")
        };
    }

    public Range FunctionFileRange(Architecture arch, string fName) {
        Debug.Assert(Architectures().Contains(arch), "Tried to patch for unknown architecture.");

        var func = symTable.Entries.Single(e => e.Name.Equals(fName));
        var fnOffset = (int)func.Value + symOffset;
        return new Range(fnOffset, fnOffset + (int)func.Size);
    }

    public void Dispose() {
        elf.Dispose();
    }

}

public sealed class MacosPatcher : Patcher {

    private Dictionary<Architecture, MachOWithOffset> machoBinaries;

    public MacosPatcher(IReadOnlyList<MachOWithOffset> machoBinaries) {
        this.machoBinaries = machoBinaries.ToDictionary(GetArchitecture);
    }

    public OSPlatform Platform => OSPlatform.OSX;

    public IEnumerable<Architecture> Architectures() {
        return machoBinaries.Keys;
    }

    public Range FunctionFileRange(Architecture arch, string fName) {
        var (binary, binaryFileOffset) = machoBinaries[arch];

        var symTab = binary.GetCommandsOfType<SymbolTable>().Single();

        var sym = symTab.Symbols.First(e => e.Name.Equals(fName));
        var funcSectionOffset = (int)((ulong)sym.Value - sym.Section.Address);
        int fnOffset = (int)(funcSectionOffset + sym.Section.Offset + binaryFileOffset);
        return new Range(fnOffset, Index.End); // TODO, Length unknown, not in symbol table? local `num5` of Symbol reader is probably length, ffs.
    }

    public void Dispose() {
    }

    private static Architecture GetArchitecture(MachOWithOffset macho) {
        return macho.Binary.Machine switch {
            MachOMachine.X86_64 => Architecture.X64,
            MachOMachine.Arm64 => Architecture.Arm64,
            _ => throw new ArgumentException($"Unknown MachO architecture type. {Enum.GetName(typeof(MachOMachine), macho.Binary.Machine)}")
        };
    }

}
