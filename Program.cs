using FactorioAchievementPatcher;

try {
	if (args.Length <= 0)
		throw new ArgumentException($"Usage: <path to factorio.exe or map.zip");

	var modulePath = args[0];
	if (!File.Exists(modulePath))
		throw new ArgumentException($"File not found: {modulePath}");

	var moduleBytes = File.ReadAllBytes(modulePath);

	bool applied = false;
	using var patcher = Patcher.Create(modulePath, moduleBytes);

	var patchSet = Patches.PlatformPatchSets[patcher.Platform];
	foreach (var arch in patcher.Architectures()) {
		Console.WriteLine($"Processing {patcher.Platform} {arch}");
		var patches = patchSet[arch];
		
		foreach (var patch in patches) {
			var fnBytes = moduleBytes.AsSpan(patcher.FunctionFileRange(arch, patch.FunctionName));
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
		File.WriteAllBytes(modulePath, moduleBytes);

	Console.WriteLine("Done");
}
catch (ArgumentException ex) {
	Console.Error.WriteLine(ex);
}
catch (Exception ex) {
	Console.Error.WriteLine(ex);
}