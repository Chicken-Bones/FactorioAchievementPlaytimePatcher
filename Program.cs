using FactorioAchievementPatcher;

try {
	if (args.Length <= 0)
		throw new ArgumentException($"Usage: <path to factorio.exe or map.zip> [<path to output>]");

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

	var patchSet = Patches.PlatformPatchSets[provider.Platform];
	foreach (var arch in provider.Architectures()) {
		Console.WriteLine($"Processing {provider.Platform} {arch}");
		var patches = patchSet[arch];
		
		foreach (var patch in patches) {
			var fnBytes = moduleBytes.AsSpan(provider.FunctionFileRange(arch, patch.FunctionName));
			if (patch.Apply(fnBytes)) {
				Console.WriteLine($"Patched {patch.FunctionName}");
				applied = true;
			}
			else {
				Console.WriteLine($"Already patched {patch.FunctionName}");
			}
		}
	}

	if (applied)
		File.WriteAllBytes(outPath, moduleBytes);

	Console.WriteLine("Done");
}
catch (ArgumentException ex) {
	Console.Error.WriteLine(ex.Message);
}
catch (Exception ex) {
	Console.Error.WriteLine(ex);
}