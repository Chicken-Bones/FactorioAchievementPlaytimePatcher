using FactorioAchievementPatcher;

try {
	if (args.Length <= 0)
		throw new ArgumentException("Usage: <path to factorio.exe> [<path to output>]");

	var modulePath = args[0];
	if (!File.Exists(modulePath))
		throw new ArgumentException($"File not found: {modulePath}");

	var outPath = modulePath;
	if (args.Length > 1) {
		outPath = args[1];
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
			try {
				if (patch.Apply(fnBytes)) {
					Console.WriteLine($"Patched {patch.FunctionName}");
					applied = true;
				}
				else {
					Console.WriteLine($"Already patched {patch.FunctionName}");
				}
			}
			catch (PatchTargetMissingException) {
				errors = true;
				var foundOffset = patch.Find(fnBytes);
				if (foundOffset == -1) {
					Console.WriteLine($"Patch for {patch.FunctionName} invalid, target doesn't exist."); 
				} else {
					Console.WriteLine($"Patch for {patch.FunctionName} found at different offset. Old: 0x{patch.Offset:X} New: 0x{foundOffset:X}");
				}
			}
		}
	}

	if (errors) {
		Console.WriteLine("Errors found, can't continue.");
		return 1;
	}

	if (applied) {
		File.WriteAllBytes(outPath, moduleBytes);
		provider.FinalizePatches(outPath);
	}

	Console.WriteLine("Done");
	return 0;
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
return 1;