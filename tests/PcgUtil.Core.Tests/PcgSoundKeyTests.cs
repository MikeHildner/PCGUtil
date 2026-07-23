using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class PcgSoundKeyTests
{
    [Fact]
    public void Renamed_copy_shares_a_key_with_its_source()
    {
        // Copy a real combi into an init slot, rename the copy — the sound key must not care.
        var copied = PcgReader.Parse(PcgEditor.CopyCombi(Sample.Parse(), 7, 57, 10, 0));
        var pcg = PcgReader.Parse(PcgEditor.RenameCombi(copied, 10, 0, "Different Name"));

        var keys = PcgSoundKey.Keys(pcg, "CMB1");
        var source = keys.Single(k => k.Bank == 7 && k.Index == 57);
        var renamed = keys.Single(k => k.Bank == 10 && k.Index == 0);
        Assert.Equal(source.Key, renamed.Key);
        Assert.NotEqual(source.Name, renamed.Name);
    }

    [Fact]
    public void Favorite_bit_does_not_change_the_key()
    {
        var pcg = Sample.Parse();
        var before = PcgSoundKey.Keys(pcg, "PRG1").Single(k => k.Bank == 19 && k.Index == 0);

        // Unstar the one hardware-starred program by clearing its favorite bit directly.
        var bytes = (byte[])pcg.Data.Clone();
        var chunk = PcgBankIdentity.CanonicalBanks(pcg, "PRG1")[19]!;
        int recordSize = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            pcg.Data.AsSpan((int)chunk.DataOffset + 4, 4));
        long record = chunk.DataOffset + 12;
        bytes[record + ProgramReader.FavoriteOffset] &= unchecked((byte)~ProgramReader.FavoriteBit);

        var after = PcgSoundKey.Keys(PcgReader.Parse(bytes), "PRG1").Single(k => k.Bank == 19 && k.Index == 0);
        Assert.Equal(before.Key, after.Key);
    }

    [Fact]
    public void Cross_file_membership_answers_the_merge_question()
    {
        if (OneProgramPack.Parse() is not { } pack)
            return;

        // Reality pin: the pack's sound already lives in the gig file (USER-FF carries the
        // same ONE THING sweep — the Duplicates tab groups them). The merge badge must say
        // "already in target" for exactly this case.
        var target = Sample.Parse();
        var packKey = PcgSoundKey.Keys(pack, "PRG1").Single(k => k.Name == "ONE THING SYNTH SWEEP").Key;
        var targetKeys = PcgSoundKey.Keys(target, "PRG1").Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(packKey, targetKeys);

        // Transition pin: a genuinely new sound (one content byte tweaked, name untouched)
        // is absent until merged, present after.
        var tweakedPack = (byte[])pack.Data.Clone();
        var chunk = PcgBankIdentity.CanonicalBanks(pack, "PRG1")[12]!;
        tweakedPack[chunk.DataOffset + 12 + 100] ^= 0xFF; // a body byte well past the name
        var variant = PcgReader.Parse(tweakedPack);
        var variantKey = PcgSoundKey.Keys(variant, "PRG1").Single(k => k.Name == "ONE THING SYNTH SWEEP").Key;
        Assert.NotEqual(packKey, variantKey);
        Assert.DoesNotContain(variantKey, targetKeys);

        var catalog = PcgCatalog.Build(target);
        var dst = Enumerable.Range(0, catalog.ProgramBanks.Count)
            .Where(b => catalog.ProgramBankTypes[b] == ProgramBankType.Exi)
            .SelectMany(b => Enumerable.Range(0, catalog.ProgramBanks[b].Count)
                .Where(i => PcgOrganizer.IsProgramPlaceholder(catalog.ProgramBanks[b][i]))
                .Select(i => (Bank: b, Index: i)))
            .First();
        var merged = PcgReader.Parse(PcgEditor.CopyProgramAcross(variant, 12, 0, target, dst.Bank, dst.Index));
        var mergedKeys = PcgSoundKey.Keys(merged, "PRG1").Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(variantKey, mergedKeys);
    }

    [Fact]
    public void Engine_type_prefixes_keep_hd1_and_exi_twins_apart()
    {
        // Two records with identical bytes in banks of different chunk kinds must never
        // share a key — the chunk-id prefix is the discriminator.
        var pcg = Sample.Parse();
        var keys = PcgSoundKey.Keys(pcg, "PRG1");
        var banks = PcgBankIdentity.CanonicalBanks(pcg, "PRG1");
        var byPrefix = keys.GroupBy(k => k.Key.Split(':')[0]).Select(g => g.Key).OrderBy(p => p).ToList();
        // The sample carries both HD-1 (PBK1) and EXi (MBK1) banks.
        Assert.Contains("MBK1", byPrefix);
        Assert.Contains("PBK1", byPrefix);
        // And every key's prefix matches its bank's chunk id.
        Assert.All(keys, k => Assert.StartsWith(banks[k.Bank]!.Id + ":", k.Key));
    }
}
