using AsmResolver.PE.File;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace FactorioAchievementPatcher
{
	public class WindowsSymbolHelper : IDisposable
	{
		// Constants
		private const uint SYMOPT_UNDNAME = 0x00000002;  // Enable undecorated symbol names
		private const uint SYMOPT_DEFERRED_LOADS = 0x00000004;

		private const int MAX_SYM_NAME = 1024;

		// Struct for SYMBOL_INFO (used by SymFromNameW)
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct SYMBOL_INFO
		{
			public uint SizeOfStruct;
			public uint TypeIndex;      // Type of the symbol
			public ulong Reserved1;
			public ulong Reserved2;
			public uint Index;
			public uint Size;
			public ulong ModBase;       // Base address of the module containing this symbol
			public uint Flags;
			public ulong Value;
			public ulong Address;       // Virtual address
			public uint Register;
			public uint Scope;
			public uint Tag;
			public uint NameLen;        // Length of the symbol name
			public uint MaxNameLen;     // Max length of the symbol name

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_SYM_NAME / 2)]
			public string Name;

			//public readonly string Name => Encoding.Unicode.GetString(_Name[..(int)NameLen]);
		}

		private readonly nint hProcess;
		private readonly ulong moduleBaseAddress;

		public PEFile PEFile { get; }

		public WindowsSymbolHelper(string modulePath)
		{
			hProcess = Process.GetCurrentProcess().Handle;

			var pdbPath = Path.ChangeExtension(modulePath, ".pdb");
			if (!File.Exists(pdbPath))
				throw new ArgumentException($"File not found: {pdbPath}");

			PEFile = PEFile.FromFile(modulePath);

			// Initialize DbgHelp
			[DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
			static extern bool SymInitialize(IntPtr hProcess, string? userSearchPath, bool fInvadeProcess);
			if (!SymInitialize(hProcess, null, false))
				throw new Exception($"SymInitialize failed. Error: {Marshal.GetLastWin32Error()}");

			// Set options for symbol loading
			[DllImport("dbghelp.dll", SetLastError = true)]
			static extern uint SymSetOptions(uint SymOptions);
			_ = SymSetOptions(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS);

			// Load the module
			moduleBaseAddress = LoadModule(hProcess, modulePath, pdbPath);
			if (moduleBaseAddress == 0)
				throw new Exception($"LoadModule failed. Error: {Marshal.GetLastWin32Error()}");
		}

		public uint GetFunctionOffset(string functionName)
		{
			[DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
			static extern bool SymFromNameW(IntPtr hProcess, string name, ref SYMBOL_INFO symbol);

			var symbolInfo = new SYMBOL_INFO { SizeOfStruct = (uint)Marshal.SizeOf<SYMBOL_INFO>(), MaxNameLen = MAX_SYM_NAME };
			if (!SymFromNameW(hProcess, functionName, ref symbolInfo))
				throw new Exception($"SymFromNameW({functionName}) failed. Error: {Marshal.GetLastWin32Error()}");

			if (symbolInfo.Name != functionName)
				throw new Exception($"SymFromNameW({functionName}) failed. Name: {symbolInfo.Name}");

			if (symbolInfo.ModBase != moduleBaseAddress)
				throw new Exception($"SymFromNameW({functionName}) failed. Returned base address from another module");

			uint rva = (uint)(symbolInfo.Address - symbolInfo.ModBase);
			var section = PEFile.GetSectionContainingRva(rva);
			return (uint)section.RvaToFileOffset(rva);
		}

		private static ulong LoadModule(IntPtr process, string modulePath, string pdbPath)
		{
			// Use SymLoadModuleEx to load the module
			[DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Unicode)]
			static extern ulong SymLoadModuleEx(IntPtr hProcess, IntPtr hFile, string imageName, string moduleName, ulong baseOfDll, uint dllSize, IntPtr data, uint flags);

			ulong baseAddress = SymLoadModuleEx(process, IntPtr.Zero, modulePath, null, 0, 0, IntPtr.Zero, 0);
			if (baseAddress == 0)
				throw new Exception($"Failed to load module. Error: {Marshal.GetLastWin32Error()}");

			return baseAddress;
		}

		private bool disposedValue;
		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue) {
				if (disposing) {
					// dispose managed state (managed objects)
				}

				[DllImport("dbghelp.dll", SetLastError = true)]
				static extern bool SymCleanup(IntPtr hProcess);
				SymCleanup(hProcess);

				disposedValue = true;
			}
		}

		~WindowsSymbolHelper()
		{
		    // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		    Dispose(disposing: false);
		}

		public void Dispose()
		{
			// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}
	}
}
