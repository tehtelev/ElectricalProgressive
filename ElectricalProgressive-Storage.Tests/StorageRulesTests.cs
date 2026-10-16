using ElectricalProgressive.Content.Storage;
using ElectricalProgressive.Content.Storage.Assembler;
using ElectricalProgressive.Content.Storage.Cable;
using ElectricalProgressive.Content.Storage.Craft;
using ElectricalProgressive.Content.Storage.Patch;
using ElectricalProgressive.Content.Storage.Pattern;
using ElectricalProgressive.Content.Storage.Processor;
using ElectricalProgressive.Content.Storage.Terminal;
using ElectricalProgressive.Utils;

namespace ElectricalProgressive.Storage.Tests;

public class StackSizeTextTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(999, "999")]
    public void Counts_below_1000_stay_plain(long amount, string text)
    {
        Assert.Equal(text, StackSizeTextPatch.Count(amount));
    }

    [Theory]
    [InlineData(1000, "1к")]
    [InlineData(1500, "1.5к")]
    [InlineData(2500, "2.5к")]
    [InlineData(12000, "12к")]
    [InlineData(1_000_000, "1м")]
    [InlineData(1_500_000, "1.5м")]
    [InlineData(12_000_000, "12м")]
    public void Large_counts_use_russian_suffixes(long amount, string text)
    {
        Assert.Equal(text, StackSizeTextPatch.Count(amount));
    }

    [Fact]
    public void Counts_past_int_max_use_the_same_text_as_int_max()
    {
        var capped = StackSizeTextPatch.Count(int.MaxValue);
        Assert.Equal(capped, StackSizeTextPatch.Count((long)int.MaxValue + 1));
        Assert.Equal(capped, StackSizeTextPatch.Count(long.MaxValue));
        Assert.Equal("2147.5м", capped);
    }
}

public class DiskByteTests
{
    [Fact]
    public void Type_costs_bytes_over_128_and_at_least_one()
    {
        Assert.Equal(8, StorageAccess.BytesPerType(1024));
        Assert.Equal(32, StorageAccess.BytesPerType(4096));
        Assert.Equal(1, StorageAccess.BytesPerType(128));
        Assert.Equal(1, StorageAccess.BytesPerType(64));
    }

    [Fact]
    public void Used_bytes_charge_a_type_then_eight_items_per_byte()
    {
        Assert.Equal(0, StorageAccess.UsedBytes(1024, 0, 10));
        Assert.Equal(0, StorageAccess.UsedBytes(1024, 1, 0));
        Assert.Equal(9, StorageAccess.UsedBytes(1024, 1, 1));
        Assert.Equal(9, StorageAccess.UsedBytes(1024, 1, 8));
        Assert.Equal(10, StorageAccess.UsedBytes(1024, 1, 9));
    }

    [Fact]
    public void One_kilobyte_disk_holds_63_types_and_4160_items()
    {
        Assert.Equal(63, StorageAccess.MaxTypes);
        Assert.Equal(8, StorageAccess.ItemsPerByte);
        Assert.True(StorageAccess.Fits(1024, 63, 4160));
        Assert.False(StorageAccess.Fits(1024, 63, 4161));
        Assert.False(StorageAccess.Fits(1024, 64, 1));
        Assert.True(StorageAccess.Fits(1024, 0, 0));
        Assert.False(StorageAccess.Fits(1024, 0, 1));
        Assert.Equal(4160, StorageAccess.RoomForAdditional(1024, 63, 0, false));
        Assert.Equal(0, StorageAccess.RoomForAdditional(1024, 63, 0, true));
        Assert.Equal(8128, StorageAccess.RoomForAdditional(1024, 0, 0, true));
    }
}

public class LiquidTextTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(1000, "1")]
    [InlineData(1500, "1.5")]
    [InlineData(250, "0.25")]
    [InlineData(1001, "1.001")]
    public void Litres_drop_a_trailing_zero(int milli, string text)
    {
        Assert.Equal(text, LiquidCraft.LitresText(milli));
    }
}

public class PanelSeatTests
{
    [Theory]
    [InlineData(Facing.None, "")]
    [InlineData(Facing.NorthEast, "/0")]
    [InlineData(Facing.EastNorth, "/1")]
    [InlineData(Facing.SouthEast, "/2")]
    [InlineData(Facing.WestNorth, "/3")]
    [InlineData(Facing.UpNorth, "/4")]
    [InlineData(Facing.DownNorth, "/5")]
    public void Guest_seat_is_the_support_face(Facing facing, string seat)
    {
        Assert.Equal(seat, PanelStack.SeatOf(facing));
    }

    [Fact]
    public void Face_packet_keeps_mod_packets_below_its_base()
    {
        int[] ids =
        [
            StorageAccess.LiquidPacketId,
            BlockEntityEStoragePatternTerminal.PacketId,
            StorageAccess.OrderPacketId,
            BlockEntityEStorageCraftTerminal.PacketId,
            BlockEntityEStorageProcessor.StatusPacket,
            BlockEntityEStorageProcessor.CancelPacket,
            BlockEntityEStorageAssembler.ProgressPacket
        ];

        Assert.Equal(20000, PanelStack.FacePacketBase);
        Assert.Equal(2000, PanelStack.FacePacketStride);
        Assert.All(ids, id => Assert.True(id < PanelStack.FacePacketBase));
        Assert.Equal(28010, PanelStack.FacePacket(Facing.UpNorth, 10));
        Assert.Equal(39103, PanelStack.FacePacket(Facing.NorthEast, StorageAccess.OrderPacketId));
    }
}

public class CablePackTests
{
    [Fact]
    public void Line_counts_pack_three_bits_per_face()
    {
        Assert.Equal(0, BlockEStorageCable.Pack([0, 0, 0, 0, 0, 0]));
        Assert.Equal(1, BlockEStorageCable.Pack([1]));
        Assert.Equal(4, BlockEStorageCable.Pack([4, 0, 0, 0, 0, 0]));
        Assert.Equal(0, BlockEStorageCable.Pack([8, 0, 0, 0, 0, 0]));
        Assert.Equal(1 << 15, BlockEStorageCable.Pack([0, 0, 0, 0, 0, 1]));
    }
}

public class LinkKeyTests
{
    [Theory]
    [InlineData(StorageLink.Online, "electricalprogressivestorage:estorage-online")]
    [InlineData(StorageLink.Conflict, "electricalprogressivestorage:estorage-conflict")]
    [InlineData(StorageLink.NoPower, "electricalprogressivestorage:estorage-nopower")]
    [InlineData(StorageLink.NoChannel, "electricalprogressivestorage:estorage-nochannel")]
    [InlineData(StorageLink.NoController, "electricalprogressivestorage:estorage-nocontroller")]
    public void Each_link_state_has_its_handbook_key(StorageLink link, string key)
    {
        Assert.Equal(key, BlockEStorageTerminal.LinkKey(link));
    }
}
