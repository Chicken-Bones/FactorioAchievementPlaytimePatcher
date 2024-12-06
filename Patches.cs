using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FactorioAchievementPatcher
{
	public record Patch(string FunctionName, int Offset, byte[] Target, byte[] Replacement)
	{
		internal bool Apply(Span<byte> fnBytes)
		{
			fnBytes = fnBytes[Offset..];
			if (fnBytes.StartsWith(Replacement))
				return false;

			if (!fnBytes.StartsWith(Target))
				throw new Exception($"Failed to apply patch ({FunctionName}): Target not found at Offset");

			Replacement.CopyTo(fnBytes);
			return true;
		}
	}

	public static class Patches
	{
		public static Patch[] Windows = [
			new Patch("AchievementGui::updateInGameLongEnoughLabel", Offset: 0x33,
				Target: [
					0x48, 0x8B, 0x80, 0x80, 0x01, 0x00, 0x00,  // mov     rax, [rax+180h]
					0x49, 0x8B, 0x90, 0x58, 0x02, 0x00, 0x00,  // mov     rdx, [r8+258h]  ; tick
					0x48, 0xD1, 0xE8,                          // shr     rax, 1
					0x48, 0x3B, 0xD0,                          // cmp     rdx, rax
				],
				Replacement: [
					0x48, 0x8B, 0x80, 0x80, 0x01, 0x00, 0x00,  // mov     rax, [rax+180h]
					0x49, 0x8B, 0x90, 0x58, 0x02, 0x00, 0x00,  // mov     rdx, [r8+258h]  ; tick
					0x48, 0x31, 0xC0,                          // xor     rax, rax
					0x48, 0x3B, 0xD0,                          // cmp     rdx, rax
				]
			),
			new Patch("Player::isOnlineLongEnoughToGetAchievements", Offset: 0,
				Target: [
					0x48, 0x8B, 0x41, 0x20,                    // mov     rax, [this+20h]
					0x48, 0x8B, 0x80, 0x80, 0x01, 0x00, 0x00,  // mov     rax, [rax+180h]
					0x48, 0xD1, 0xE8,                          // shr     rax, 1
					0x48, 0x39, 0x81, 0x58, 0x02, 0x00, 0x00,  // cmp     [this+258h], rax
				],
				Replacement: [
					0x48, 0x8B, 0x41, 0x20,                    // mov     rax, [this+20h]
					0x48, 0x8B, 0x80, 0x80, 0x01, 0x00, 0x00,  // mov     rax, [rax+180h]
					0x48, 0x31, 0xC0,                          // xor     rax, rax
					0x48, 0x39, 0x81, 0x58, 0x02, 0x00, 0x00,  // cmp     [this+258h], rax
				]
			),
		];
	}
}
