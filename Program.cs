using FactorioAchievementPatcher;
using System.Runtime.InteropServices;

try {
	if (args.Length <= 0)
		throw new ArgumentException($"Usage: <path to factorio.exe or map.zip");

	var modulePath = args[0];
	if (!File.Exists(modulePath))
		throw new ArgumentException($"File not found: {modulePath}");

	if (Path.GetExtension(modulePath) is not ".exe")
		throw new ArgumentException($"{Path.GetFileName(modulePath)} does not end in .exe");

	var moduleBytes = File.ReadAllBytes(modulePath);

	bool applied = false;
	if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
		using var windowsSymHelper = new WindowsSymbolHelper(modulePath);

		foreach (var patch in Patches.Windows) {

			var fnOffset = windowsSymHelper.GetFunctionOffset(patch.FunctionName);
			var fnBytes = moduleBytes.AsSpan(start: (int)fnOffset);
			if (patch.Apply(fnBytes)) {
				Console.WriteLine($"Patched {patch.FunctionName}");
				applied = true;
			}
			else {
				Console.WriteLine($"Already patched {patch.FunctionName}");
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