using System.Text;
using ELFSharp;
using ELFSharp.MachO;
using ELFSharp.Utilities;

namespace FactorioAchievementPatcher {
    public record MachOWithOffset(MachO Binary, int FileOffset);

    public static class MachOHelper {

        public static bool ReadFatMachO(byte[] bytes, out IReadOnlyList<MachOWithOffset> binaries) {
            var stream = new MemoryStream(bytes);
            var b = new List<MachOWithOffset>();
            binaries = b;
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true)) {
                var magic = reader.ReadUInt32();
                if (magic != 3199925962U)
                    return false;
            }

            using (SimpleEndianessAwareReader reader = new SimpleEndianessAwareReader(stream, Endianess.BigEndian, true)) {
                int total = reader.ReadInt32();
                long start = stream.Position;
                for (int count = 0; count < total; count++) {
                    // See FatArchiveReader in ElfSharp, idk what these structures actually look like.
                    // I presume we skip over some unread bytes in the fat header for each binary.
                    stream.Seek(start + 20 * count + 8, SeekOrigin.Begin);
                    int offset = reader.ReadInt32();
                    int length = reader.ReadInt32();
                    b.Add(new MachOWithOffset(
                        MachOReader.Load(new MemoryStream(bytes, offset, length), false),
                        offset
                    ));
                }
            }

            return true;
        }

    }
}
