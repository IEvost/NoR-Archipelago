using System;
using System.Linq;
using NoR.Randomizer;

// args: shopSlots boss npc trade keys  (0/1)
internal static class Program
{
    private static int Main(string[] a)
    {
        var table = LocationTable.LoadEmbedded();
        CodeLocations.Register(table);
        var st = new RandoState
        {
            Remote = true,
            ShopSlots = int.Parse(a[0]),
            ShuffleBossDrops = a[1] == "1",
            ShuffleNpcRewards = a[2] == "1",
            ShuffleTrades = a[3] == "1",
            ShuffleKeys = a[4] == "1",
        };
        foreach (var k in Placement.GenerateRemote(st, table).Keys.OrderBy(k => k, StringComparer.Ordinal)) Console.WriteLine(k);
        return 0;
    }
}
