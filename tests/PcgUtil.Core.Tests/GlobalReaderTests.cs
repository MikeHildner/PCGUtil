using PcgUtil.Core;
using Xunit;

namespace PcgUtil.Core.Tests;

public class GlobalReaderTests
{
    // The six GLB1 name tables sit at fixed payload offsets (program categories @ 12912,
    // combi categories @ 16800; 18 × 24-byte space-padded entries). Anchored by the
    // factory names, which the instrument stores verbatim.
    [Fact]
    public void Reads_both_category_tables_from_the_sample()
    {
        var global = GlobalReader.Read(Sample.Parse());
        Assert.NotNull(global);

        Assert.Equal(18, global!.ProgramCategoryNames.Count);
        Assert.Equal("Keyboard", global.ProgramCategoryNames[0]);
        Assert.Equal("Short Decay/Hit", global.ProgramCategoryNames[14]);
        Assert.Equal("User 16", global.ProgramCategoryNames[16]); // unrenamed user slot

        Assert.Equal(18, global.CombiCategoryNames.Count);
        Assert.Equal("Bell/Mallet/Perc", global.CombiCategoryNames[2]);
        Assert.Equal("Drums/Hits", global.CombiCategoryNames[15]);
    }

    // The instrument stores combi category 9 as "Motion Synth" (with a space); the
    // hardcoded factory table says "MotionSynth". File-aware names surface the file's
    // own spelling; the static stays as the no-GLB1 fallback.
    [Fact]
    public void Catalog_prefers_the_files_own_names()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());
        Assert.Equal("Motion Synth", catalog.CombiCategoryName(9));
        Assert.Equal("MotionSynth", CombiCategories.Name(9));
        Assert.Equal("Keyboard", catalog.ProgramCategoryName(0));
        Assert.Equal("User 16", catalog.ProgramCategoryName(16));
    }

    // Sound packs can ship without GLB1 entirely — names fall back to the factory tables.
    [Fact]
    public void Files_without_global_fall_back_to_factory_names()
    {
        if (VendorPack.Parse() is not { } pack)
            return;

        Assert.Null(GlobalReader.Read(pack));
        var catalog = PcgCatalog.Build(pack);
        Assert.Equal("Keyboard", catalog.ProgramCategoryName(0));
        Assert.Equal("Drums/Hits", catalog.CombiCategoryName(15));
        Assert.Equal("User 16", catalog.CombiCategoryName(16));
    }

    [Fact]
    public void Out_of_range_categories_fall_back()
    {
        var catalog = PcgCatalog.Build(Sample.Parse());
        Assert.Equal("User 18", catalog.CombiCategoryName(18));
        Assert.Equal("User 31", catalog.ProgramCategoryName(31));
    }
}
