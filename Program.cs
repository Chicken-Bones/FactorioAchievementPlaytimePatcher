using System.Diagnostics;
using FactorioAchievementPatcher;
using System.Runtime.InteropServices;
using System.Text;
using AsmResolver.PE.File;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using ELFSharp.MachO;
using SharpPdb.Native;
using Machine = ELFSharp.MachO.Machine;

try {
	if (args.Length <= 0)
		throw new ArgumentException($"Usage: <path to factorio.exe or map.zip");

	var modulePath = args[0];
	if (!File.Exists(modulePath))
		throw new ArgumentException($"File not found: {modulePath}");

	var moduleBytes = File.ReadAllBytes(modulePath);

	bool applied = false;
	if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
		if (Path.GetExtension(modulePath) is not ".exe")
			throw new ArgumentException($"{Path.GetFileName(modulePath)} does not end in .exe");

		var pdbPath = Path.Combine(Path.GetDirectoryName(modulePath)!, Path.GetFileNameWithoutExtension(modulePath) + ".pdb");
		if (!File.Exists(pdbPath)) 
			throw new ArgumentException($"{Path.GetFileName(modulePath)} does not have a companion .pdb file.");

		var pe = PEFile.FromFile(modulePath);
		var pdb = new PdbFileReader(pdbPath);

		var text = pe.Sections.Single(e => e.Name.Equals(".text"));

		foreach (var patch in Patches.Windows) {
			var func = pdb.Functions.Single(e => e.Name.Equals(patch.FunctionName));
			var fnOffset = pe.RvaToFileOffset(func.Offset + text.Rva);
			var fnBytes = moduleBytes.AsSpan((int)fnOffset, (int)func.CodeSize);
			if (patch.Apply(fnBytes)) {
				Console.WriteLine($"Patched {patch.FunctionName}");
				applied = true;
			}
			else {
				Console.WriteLine($"Already patched {patch.FunctionName}");
			}
		}
	}
	else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
		ELF<ulong> elf;
		if (!ELFReader.TryLoad(new MemoryStream(moduleBytes), true, out elf)) {
			throw new ArgumentException($"{Path.GetFileName(modulePath)} is not a linux executable.");
		}

		var text = (ProgBitsSection<ulong>) elf.GetSection(".text");
		var symOffset = text.Offset - text.LoadAddress;

		var sym = (SymbolTable<ulong>) elf.GetSection(".symtab");

		foreach (var patch in Patches.Linux) {
			var func = sym.Entries.Single(e => e.Name.Equals(patch.FunctionName));
			var fnBytes = moduleBytes.AsSpan((int)(func.Value + symOffset), (int)func.Size);
			if (patch.Apply(fnBytes)) {
				Console.WriteLine($"Patched {patch.FunctionName}");
				applied = true;
			} else {
				Console.WriteLine($"Already patched {patch.FunctionName}");
			}
		}
	}
	else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
		IReadOnlyList<MachOWithOffset> binaries;
		if (!MachOHelper.ReadFatMachO(moduleBytes, out binaries))
			throw new ArgumentException($"{Path.GetFileName(modulePath)} is not a universal MatchO executable.");
		
		foreach (var binaryWithOffset in binaries) {
			int binaryFileOffset = binaryWithOffset.FileOffset;
			var binary = binaryWithOffset.Binary;
			
			Console.WriteLine($"Processing machine {Enum.GetName(binary.Machine.GetType(), binary.Machine)}");
			var patches = binary.Machine switch {
				Machine.X86_64 => Patches.Mac_x86,
				Machine.Arm64 => Patches.Mac_aarch64,
				_ => throw new ArgumentException($"{Path.GetFileName(modulePath)} universal MatchO executable contains unknown machine type.") 
			};

			var symTab = binary.GetCommandsOfType<SymbolTable>().Single();
			
			foreach (var patch in patches) {
				var sym = symTab.Symbols.First(e => e.Name.Equals(patch.FunctionName));
				var funcSectionOffset = (int)((ulong)sym.Value - sym.Section.Address);
				int fileOffset = (int)(funcSectionOffset + sym.Section.Offset + binaryFileOffset);
				var fnBytes = moduleBytes.AsSpan(fileOffset); // TODO, Length unknown, not in symbol table? local `num5` of Symbol reader is probably length, ffs.
				if (patch.Apply(fnBytes)) {
					Console.WriteLine($"Patched {patch.FunctionName}");
					applied = true;
				} else {
					Console.WriteLine($"Already patched {patch.FunctionName}");
				}
			}
		}
	}
	else {
		throw new NotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");
	}

	if (applied)
		File.WriteAllBytes(modulePath, moduleBytes);

	Console.WriteLine("Done");
}
catch (ArgumentException ex) {
	Console.Error.WriteLine(ex.Message);
}
catch (Exception ex) {
	Console.Error.WriteLine(ex);
}