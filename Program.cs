using FactorioAchievementPatcher;

try {
	if (args.Length <= 0)
		throw new ArgumentException("Usage: <path to factorio.exe> [<path to output>]");

	var modulePath = args[0];
	if (!File.Exists(modulePath))
		throw new ArgumentException($"File not found: {modulePath}");

	bool validate = false;
	var outPath = modulePath;
	if (args.Length > 1) {
		if (args[1] == "--validate") {
			validate = true;
		} else {
			outPath = args[1];
		}
	}

	var moduleBytes = File.ReadAllBytes(modulePath);

	bool applied = false;
	using var provider = AssemblyProvider.Create(modulePath, moduleBytes);

	var errors = false;
	var patchSet = Patches.PlatformPatchSets[provider.Platform];
	foreach (var arch in provider.Architectures()) {
		Console.WriteLine($"Processing {provider.Platform} {arch}");
		var patches = patchSet[arch];

		foreach (var patch in patches) {
			var funcRange = provider.FunctionFileRange(arch, patch.FunctionName);
			if (funcRange == null) {
				Console.WriteLine($"Unable to patch {patch.FunctionName}, function doesn't exist.");
				errors = true;
				continue;
			}

			var fnBytes = moduleBytes.AsSpan(funcRange.Value);
			if (validate) {
				if (patch.IsPatched(fnBytes)) {
					Console.WriteLine($"Already patched {patch.FunctionName}");
					continue;
				}

				var foundOffset = patch.Find(fnBytes);
				if (foundOffset == -1) {
					Console.WriteLine($"Patch for {patch.FunctionName} invalid, target doesn't exist.");
					errors = true;
				} else if (foundOffset != patch.Offset) {
					Console.WriteLine($"Patch for {patch.FunctionName} found at different offset (0x{patch.Offset - foundOffset:X}). Old: 0x{patch.Offset:X} New: 0x{foundOffset:X}");
					errors = true;
				} else {
					Console.WriteLine($"Patch for {patch.FunctionName} validated.");
				}
			} else {
				if (patch.Apply(fnBytes)) {
					Console.WriteLine($"Patched {patch.FunctionName}");
					applied = true;
				} else {
					Console.WriteLine($"Already patched {patch.FunctionName}");
				}
			}
		}
	}

	if (errors) {
		Console.WriteLine("Errors found, can't continue.");
		return;
	}

	if (validate) {
		Console.WriteLine("Patches validated.");
		return;
	}

	if (applied) {
		File.WriteAllBytes(outPath, moduleBytes);
		provider.FinalizePatches(outPath);
	}

	Console.WriteLine("Done");
}
catch (ArgumentException ex) {
	Console.Error.WriteLine(ex.Message);
}
catch (SignerNotFoundException ex) {
	Console.WriteLine(ex.Message);
}
catch (Exception ex) {
	Console.Error.WriteLine(ex);
}