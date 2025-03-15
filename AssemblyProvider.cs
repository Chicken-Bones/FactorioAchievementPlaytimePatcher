using System.Diagnostics;
using System.Runtime.InteropServices;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using MachOSharp;
using MachOSharp.Command;
using MachOSharp.Util;
using SharpPdb.Native;
using ElFMachine = ELFSharp.ELF.Machine;

namespace FactorioAchievementPatcher;

public interface AssemblyProvider : IDisposable {

    public static AssemblyProvider Create(string modulePath, byte[] moduleBytes) {
        if (Path.GetExtension(modulePath).Equals(".exe")) {
            return new WindowsAssemblyProvider(modulePath);
        }

        if (ELFReader.TryLoad(new MemoryStream(moduleBytes), true, out ELF<ulong> elf)) {
            return new LinuxAssemblyProvider(elf);
        }

        if (MachOReader.TryLoadFat(new MemoryStream(moduleBytes), out var fat, false)) {
            return new MacosAssemblyProvider(fat);
        }

        throw new ArgumentException("Unknown Executable file provided.");
    }

    OSPlatform Platform { get; }

    IEnumerable<Architecture> Architectures();

    Range FunctionFileRange(Architecture arch, string fName);

    public virtual void FinalizePatches(string filePath) {
    }

}

public sealed class WindowsAssemblyProvider : AssemblyProvider {

    private readonly PEFile pe;
    private readonly PdbFileReader pdb;

    private readonly PESection text;

    public WindowsAssemblyProvider(string modulePath) {
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

public sealed class LinuxAssemblyProvider : AssemblyProvider {

    private readonly ELF<ulong> elf;
    private readonly int symOffset;
    private readonly SymbolTable<ulong> symTable;

    public LinuxAssemblyProvider(ELF<ulong> elf) {
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

public sealed class MacosAssemblyProvider : AssemblyProvider {

    private readonly Dictionary<Architecture, MachO64> binaries;
    private readonly Signer signer;


    public MacosAssemblyProvider(MachOFat fat) {
        binaries = fat.Arches.ToDictionary(GetArchitecture, e => e.macho);
        signer = FindSignerProgram();
    }

    public OSPlatform Platform => OSPlatform.OSX;

    public IEnumerable<Architecture> Architectures() {
        return binaries.Keys;
    }

    public Range FunctionFileRange(Architecture arch, string fName) {
        var macho = binaries[arch];

        var sections = macho.LoadCommands.OfType<SegmentCommand64>().SelectMany(e => e.Sections).ToList();
        var symTab = macho.LoadCommands.OfType<SymTabCommand>().Single();

        var sym = symTab.Symbols.First(e => fName.Equals(e.Name));
        var section = sections[(int)sym.SectionIndex!];
        var funcSectionOffset = sym.Value - section.Address;
        int fnOffset = (int)(funcSectionOffset + section.Offset + (ulong)macho.FileOffset);
        return new Range(fnOffset, Index.End);
    }

    public void Dispose() {
    }

    private static Architecture GetArchitecture(MachOFatArch arch) {
        return arch.macho.CpuType switch {
            CpuType.X86_64 => Architecture.X64,
            CpuType.Arm64 => Architecture.Arm64,
            _ => throw new ArgumentException($"Unknown MachO architecture type. {Enum.GetName(typeof(CpuType), arch.macho.CpuType)}")
        };
    }

    public void FinalizePatches(string filePath) {
        Console.WriteLine($"Using {signer} to perform an ad-hoc sign on the patched executable.");
        switch (signer) {
            case Signer.CodeSign:
                SignWithCodeSign(filePath);
                break;
            case Signer.Quill:
                SignWithQuill(filePath);
                break;
        }
    }

    enum Signer {

        CodeSign,
        Quill

    }

    private static void SignWithCodeSign(string filePath) {
        using var strip = Process.Start(new ProcessStartInfo {
            FileName = "codesign",
            Arguments = "--remove-signature \"" + Path.GetFullPath(filePath) + "\"",
            UseShellExecute = false,
            RedirectStandardError = false,
            RedirectStandardOutput = false,
        });
        if (strip == null) throw new Exception("Failed to start codesign process.");
        strip.WaitForExit();
        if (strip.ExitCode != 0) throw new Exception("Process did not exit with status code 0. Got: " + strip.ExitCode);

        using var sign = Process.Start(new ProcessStartInfo {
            FileName = "codesign",
            Arguments = "--force --deep --sign - \"" + Path.GetFullPath(filePath) + "\""
        });
        if (sign == null) throw new Exception("Failed to start codesign process.");
        sign.WaitForExit();
        if (sign.ExitCode != 0) throw new Exception("Process did not exit with status code 0. Got: " + sign.ExitCode);
    }

    private static void SignWithQuill(string filePath) {
        using var strip = Process.Start(new ProcessStartInfo {
            FileName = "quill",
            Arguments = "sign \"" + Path.GetFullPath(filePath) + "\""
        });
        if (strip == null) throw new Exception("Failed to start quill process.");
        strip.WaitForExit();
        if (strip.ExitCode != 0) throw new Exception("Process did not exit with status code 0. Got: " + strip.ExitCode);
    }

    private static Signer FindSignerProgram() {
        // Prefer quill if available, even on mac, as it has prettier output.
        if (DoesProgramExist("quill", "sign --help", p => p.ExitCode == 0)) {
            return Signer.Quill;
        }

        // Annoyingly, codesign doesn't have a real help switch, it just prints some vague usage
        // when the command is wrong. We run it, and check the stderr contains the start of its usage.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && DoesProgramExist("codesign", "--help", p => p.StandardError.ReadToEnd().Contains("Usage: codesign"))) {
            return Signer.CodeSign;
        }

        throw new SignerNotFoundException();
    }

    private static bool DoesProgramExist(string name, string args, Predicate<Process> pred) {
        try {
            ProcessStartInfo start = new ProcessStartInfo {
                FileName = name,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(start);
            if (proc != null) {
                proc.WaitForExit();
                return pred.Invoke(proc);
            }
        } catch (Exception) {
        }

        return false;
    }

}

class SignerNotFoundException : Exception;
