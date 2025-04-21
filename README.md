

# Factorio Achievement Playtime Patcher

Patches the Factorio executable to remove the **50% playtime requirement** for earning achievements. This allows you to unlock achievements even if you've spent less than half your playtime in a single save.

**You will need to re-patch each time Factorio updates**

![Achivement restriction](https://preview.redd.it/looking-for-a-mod-that-bypasses-the-50-playtime-requirement-v0-9hj17ocgfzbc1.png?width=606&format=png&auto=webp&s=5efa144eb69974946deda2d3f1d5785abe6a2bc4)

## 🖥️ Platform Compatibility
-   ✅ Windows x64
-   ✅ Linux x64
-   ✅ macOS x64/ARM64 (xcode cli tools or quill required)

## Usage

### Prerequisite for Mac users

Open a terminal and run `xcode-select --install` to install Xcode Command Line Tools

...or, for a more lightweight option, follow the install instructions for [quill](https://github.com/anchore/quill)

### Option 1: Use a Prebuilt Binary

Download the latest binary from the [Releases](https://github.com/Chicken-Bones/FactorioAchievementPlaytimePatcher/releases) page and run it directly:

```bash
FactorioAchievementPlaytimePatcher.exe <path-to-factorio-executable>
```

Example:
```bash
FactorioAchievementPlaytimePatcher.exe "C:\Program Files (x86)\Steam\steamapps\common\Factorio\bin\x64\factorio.exe"
```

or drag-n-drop on Windows

![Drag n Drop](https://i.imgur.com/fPHMklH.gif)

### Option 2: Run from source with .NET

If you prefer to run from source, make sure you have a .NET 8 or newer SDK installed, then run:

```bash
dotnet run <path-to-factorio-executable>
```

## Why?
In our recent multiplayer Space Age run, we had 4-5 players and many of us couldn't make every session. When one player hopped on for a couple hours to tinker with some designs or improve the base defences, some of the other players dropped below 50% playtime and could never realistically catch up without pushing _other_ players below 50%. 

Sure, you can always unlock the achievements you've 'earned' with [SteamAchievementManager](https://github.com/gibbed/SteamAchievementManager), but where's the immersion in that.

## How it works

Factorio ships with debug symbols (`factorio.pdb` on windows, embedded in the ELF on osx/linux), allowing us to locate internal functions by name and patch the assembly bytecode (x86/ARM)

We used a disassembler (IDA/Ghidra) to find the relevant functions (`AchievementGui::updateInGameLongEnoughLabel` and `Player::isOnlineLongEnoughToGetAchievements`) and disassemble them to find the check for 

```cpp
player->onlineTicks >= player->map->entityTick / 2
```

The patcher uses libraries (`SharpPDB, ELFSharp, MachOSharp`) to locate these functions within the executable, and we patch the x86 or ARM to replace `Map->totalTicks >> 1` with `0`

@covers1624 notes:
> Finding the patch points on Linux was made much more difficult by how heavily inlined the code is
