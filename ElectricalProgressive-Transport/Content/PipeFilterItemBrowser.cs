using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content;

/// <summary>
/// Virtualized creative-item browser: only visible slots exist in the inventory.
/// Full item lists stay in a static cache (prewarmed once) so GUI open stays cheap.
/// </summary>
public class PipeFilterItemBrowser : InventoryGeneric
{
    public const string SelectedBackgroundColor = PipeFilterGuiStyle.SelectedSlotColor;
    public const int Cols = 7;
    public const int VisibleRows = 3;
    public const int VisibleSlots = Cols * VisibleRows;

    private static List<CachedEntry>? cachedItems;
    private static List<CachedEntry>? cachedLiquids;
    private static bool cacheReady;
    // Bump when filter list rules change so stale static cache is rebuilt in-session.
    private const int CacheVersion = 6;
    private static int builtCacheVersion;

    /// <summary>
    /// Creative inventory tabs excluded from the item filter.
    /// Note: creatures/meta often also appear under general/items — exclude by ANY of these tabs.
    /// </summary>
    private static readonly HashSet<string> ExcludedItemCreativeTabs = new(StringComparer.OrdinalIgnoreCase)
    {
        "creatures",
        "liquids",
        "special",
        "meta"
    };

    private readonly List<CachedEntry> source;
    private readonly List<int> ordered = new();
    private readonly List<int> filtered = new();
    private readonly HashSet<string> selectedCodes = new(StringComparer.OrdinalIgnoreCase);

    private string searchText = "";
    private int scrollRow;

    public Action<ItemStack>? OnStackClicked { get; set; }

    public int FilteredCount => filtered.Count;

    public int TotalRows => Math.Max(1, (int)Math.Ceiling(Math.Max(1, filtered.Count) / (double)Cols));

    public int ScrollRow
    {
        get => scrollRow;
        set
        {
            int maxRow = Math.Max(0, TotalRows - VisibleRows);
            scrollRow = Math.Max(0, Math.Min(maxRow, value));
            RefillVisibleSlots();
        }
    }

    private PipeFilterItemBrowser(List<CachedEntry> source, ICoreAPI api)
        : base(VisibleSlots, "pipefilterbrowser", "client", api)
    {
        this.source = source;
        RebuildOrderedAndFiltered();
        RefillVisibleSlots();
    }

    protected override ItemSlot NewSlot(int i) => new BrowserSlot(this);

    public static void Prewarm(ICoreClientAPI capi)
    {
        if (cacheReady || capi?.World == null)
            return;

        try
        {
            EnsureCaches(capi);
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[ElectricalProgressive Transport] Pipe filter cache prewarm failed: {0}", e.Message);
        }
    }

    public static PipeFilterItemBrowser Create(ICoreClientAPI capi, bool liquidsOnly)
    {
        EnsureCaches(capi);
        List<CachedEntry> source = liquidsOnly ? cachedLiquids! : cachedItems!;
        return new PipeFilterItemBrowser(source, capi);
    }

    /// <summary>
    /// Marks selected collectible codes (filter contents), pins them to top, refreshes view.
    /// </summary>
    public void SetSelectedCodes(IEnumerable<string> codes, bool resetScroll = false)
    {
        selectedCodes.Clear();
        foreach (string code in codes)
        {
            if (!string.IsNullOrEmpty(code))
                selectedCodes.Add(code);
        }

        RebuildOrderedAndFiltered();
        if (resetScroll)
            scrollRow = 0;
        else
        {
            int maxRow = Math.Max(0, TotalRows - VisibleRows);
            scrollRow = Math.Max(0, Math.Min(maxRow, scrollRow));
        }

        RefillVisibleSlots();
    }

    public void SetSearch(string text, bool resetScroll = true)
    {
        searchText = (text ?? "").ToSearchFriendly().ToLowerInvariant();
        ApplySearchFilter();
        if (resetScroll)
            scrollRow = 0;
        else
        {
            int maxRow = Math.Max(0, TotalRows - VisibleRows);
            scrollRow = Math.Max(0, Math.Min(maxRow, scrollRow));
        }

        RefillVisibleSlots();
    }

    /// <summary>Scroll by pixel offset (row height ≈ one slot grid row).</summary>
    public void SetScrollPixels(float pixels, float rowHeight)
    {
        if (rowHeight <= 0.01f)
            return;

        ScrollRow = (int)(pixels / rowHeight);
    }

    public float GetScrollTotalHeight(float visibleHeight, float rowHeight)
    {
        int extraRows = Math.Max(0, TotalRows - VisibleRows);
        return visibleHeight + extraRows * rowHeight;
    }

    private void RebuildOrderedAndFiltered()
    {
        ordered.Clear();

        // Selected first (stable source order), then the rest.
        for (int i = 0; i < source.Count; i++)
        {
            if (selectedCodes.Contains(source[i].CodeKey))
                ordered.Add(i);
        }

        for (int i = 0; i < source.Count; i++)
        {
            if (!selectedCodes.Contains(source[i].CodeKey))
                ordered.Add(i);
        }

        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        filtered.Clear();

        if (string.IsNullOrEmpty(searchText))
        {
            filtered.AddRange(ordered);
            return;
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            int src = ordered[i];
            CachedEntry entry = source[src];
            if (entry.SearchName.IndexOf(searchText, StringComparison.Ordinal) >= 0
                || entry.SearchFull.IndexOf(searchText, StringComparison.Ordinal) >= 0)
            {
                filtered.Add(src);
            }
        }
    }

    private void RefillVisibleSlots()
    {
        int start = scrollRow * Cols;

        for (int i = 0; i < VisibleSlots; i++)
        {
            int idx = start + i;
            ItemSlot slot = this[i];

            if (idx < 0 || idx >= filtered.Count)
            {
                slot.Itemstack = null;
                slot.HexBackgroundColor = null;
                slot.MarkDirty();
                continue;
            }

            CachedEntry entry = source[filtered[idx]];
            // Reuse same stack instance for display — browser is client-only and read-only.
            slot.Itemstack = entry.Stack;
            slot.HexBackgroundColor = selectedCodes.Contains(entry.CodeKey) ? SelectedBackgroundColor : null;
            slot.MarkDirty();
        }
    }

    private static void EnsureCaches(ICoreClientAPI capi)
    {
        if (cacheReady && builtCacheVersion == CacheVersion && cachedItems != null && cachedLiquids != null)
            return;

        List<ItemStack> stacks = GatherCreativeStacks(capi);
        var items = new List<CachedEntry>(stacks.Count);
        for (int i = 0; i < stacks.Count; i++)
        {
            ItemStack stack = stacks[i];
            if (stack?.Collectible?.Code == null)
                continue;

            items.Add(CachedEntry.FromStack(stack));
        }

        // Liquid list is independent: real portion items only (waterportion, milkportion, …).
        // Never world liquid blocks (water-still-7, lava-still-7).
        List<CachedEntry> liquids = GatherLiquidPortionEntries(capi.World);

        cachedItems = items;
        cachedLiquids = liquids;
        cacheReady = true;
        builtCacheVersion = CacheVersion;
    }

    /// <summary>
    /// All containable liquid portion items in the game (plus whenFilled mappings from liquid blocks).
    /// </summary>
    private static List<CachedEntry> GatherLiquidPortionEntries(IWorldAccessor world)
    {
        var liquids = new List<CachedEntry>(128);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddPortion(ItemStack? portion)
        {
            if (portion?.Item == null || portion.Collectible?.Code == null || portion.Collectible.IsMissing)
                return;

            // Reject blocks that slipped through.
            if (portion.Block != null && portion.Item == null)
                return;

            portion.StackSize = 1;
            string key = portion.Collectible.Code.ToString();
            if (!seen.Add(key))
                return;

            liquids.Add(CachedEntry.FromStack(portion));
        }

        // 1) Every item that is a liquid portion (waterportion etc. often skip creative tabs).
        foreach (Vintagestory.API.Common.Item item in world.Items)
        {
            if (!IsLiquidPortionItem(item))
                continue;

            TryAddPortion(new ItemStack(item, 1));
        }

        // 2) Liquid blocks that fill buckets: whenFilled -> portion (water-still -> waterportion).
        foreach (Vintagestory.API.Common.Block block in world.Blocks)
        {
            if (block == null || block.IsMissing || block.Code == null || block.Id == 0)
                continue;

            ItemStack? fromFilled = TryPortionFromWhenFilled(world, block);
            TryAddPortion(fromFilled);
        }

        return liquids;
    }

    /// <summary>
    /// Solids that have barrel textures (гниль/слякоть) but are not real liquid portions.
    /// </summary>
    private static readonly HashSet<string> ExcludedLiquidItemPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "rot",
        "slush"
    };

    private static bool IsExcludedLiquidItem(Vintagestory.API.Common.Item item)
    {
        string path = item.Code?.Path ?? "";
        if (ExcludedLiquidItemPaths.Contains(path))
            return true;

        // path without domain variants
        int slash = path.LastIndexOf('/');
        string leaf = slash >= 0 ? path[(slash + 1)..] : path;
        return ExcludedLiquidItemPaths.Contains(leaf);
    }

    private static bool IsLiquidPortionItem(Vintagestory.API.Common.Item? item)
    {
        if (item == null || item.IsMissing || item.Code == null || item.Id == 0)
            return false;

        if (IsExcludedLiquidItem(item))
            return false;

        // Prefer real liquid portions first.
        string typeName = item.GetType().Name;
        bool isPortionClass = typeName.IndexOf("LiquidPortion", StringComparison.OrdinalIgnoreCase) >= 0;
        bool isMatterLiquid = item.MatterState == EnumMatterState.Liquid;
        string path = item.Code.Path ?? "";
        bool pathLooksLikePortion = path.EndsWith("portion", StringComparison.OrdinalIgnoreCase)
            || path.Contains("portion-", StringComparison.OrdinalIgnoreCase);

        if (isPortionClass || isMatterLiquid || pathLooksLikePortion)
            return true;

        // Explicit containable liquids only (not rot/slush which lack containable:true).
        if (item.Attributes?["waterTightContainerProps"].Exists == true
            && item.Attributes["waterTightContainerProps"]["containable"].AsBool(false))
            return true;

        return false;
    }

    private static ItemStack? TryPortionFromWhenFilled(IWorldAccessor world, Vintagestory.API.Common.Block block)
    {
        var props = block.Attributes?["waterTightContainerProps"];
        if (props == null || !props.Exists)
            return null;

        if (!props["containable"].AsBool(false))
            return null;

        if (!props["whenFilled"].Exists || !props["whenFilled"]["stack"].Exists)
            return null;

        try
        {
            var stackObj = props["whenFilled"]["stack"];
            string type = stackObj["type"].AsString("item");
            string code = stackObj["code"].AsString();
            if (string.IsNullOrEmpty(code))
                return null;

            // Only item portions (waterportion). Never place blocks into the liquid filter list.
            if (!type.Equals("item", StringComparison.OrdinalIgnoreCase))
                return null;

            AssetLocation loc = AssetLocation.Create(code, block.Code.Domain);
            Vintagestory.API.Common.Item? item = world.GetItem(loc);
            if (item == null)
                return null;

            return new ItemStack(item, 1);
        }
        catch
        {
            return null;
        }
    }

    private static List<ItemStack> GatherCreativeStacks(ICoreClientAPI capi)
    {
        var stacks = new List<ItemStack>(2048);
        var seen = new HashSet<string>();
        IWorldAccessor world = capi.World;

        void TryAdd(ItemStack? stack)
        {
            if (stack?.Collectible == null || stack.Collectible.IsMissing || stack.Collectible.Code == null)
                return;

            // Item filter: no liquids / liquid portions (those belong to liquid pipes).
            if (ShouldExcludeFromItemFilter(stack))
                return;

            stack.StackSize = 1;
            if (!seen.Add(BuildStackKey(stack)))
                return;

            stacks.Add(stack);
        }

        void AddCollectible(CollectibleObject? collectible)
        {
            if (collectible == null || collectible.IsMissing || collectible.Code == null || collectible.Id == 0)
                return;

            bool hasCreativeStacks = collectible.CreativeInventoryStacks is { Length: > 0 };
            bool hasCreativeTabs = collectible.CreativeInventoryTabs is { Length: > 0 };
            if (!hasCreativeStacks && !hasCreativeTabs)
                return;

            if (hasCreativeStacks)
            {
                foreach (CreativeTabAndStackList entry in collectible.CreativeInventoryStacks!)
                {
                    if (entry?.Stacks == null || entry.Tabs == null)
                        continue;

                    // Skip stacks registered under creatures / liquids / special tabs.
                    if (HasAnyExcludedCreativeTab(entry.Tabs))
                        continue;

                    foreach (JsonItemStack jsonStack in entry.Stacks)
                    {
                        if (jsonStack?.ResolvedItemstack == null)
                            continue;

                        ItemStack resolved = jsonStack.ResolvedItemstack.Clone();
                        resolved.ResolveBlockOrItem(world);
                        TryAdd(resolved);
                    }
                }
            }

            if (hasCreativeTabs)
            {
                // Creatures/meta/liquids/special are also listed under general/items —
                // if the collectible is on ANY excluded tab, skip it entirely.
                if (HasAnyExcludedCreativeTab(collectible.CreativeInventoryTabs!))
                    return;

                TryAdd(new ItemStack(collectible));
            }
        }

        // Direct creative flags only — no reflection / opening player creative inventory.
        foreach (Vintagestory.API.Common.Block block in world.Blocks)
            AddCollectible(block);

        foreach (Vintagestory.API.Common.Item item in world.Items)
            AddCollectible(item);

        return stacks;
    }

    private static bool HasAnyExcludedCreativeTab(string[] tabs)
    {
        if (tabs == null || tabs.Length == 0)
            return false;

        for (int i = 0; i < tabs.Length; i++)
        {
            if (ExcludedItemCreativeTabs.Contains(tabs[i]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Item filter: no liquids, creatures, meta tools/blocks, special tab items.
    /// </summary>
    private static bool ShouldExcludeFromItemFilter(ItemStack stack)
    {
        CollectibleObject? col = stack.Collectible;
        if (col == null)
            return true;

        // World liquid blocks / liquid matter.
        if (col.IsLiquid())
            return true;

        if (col.MatterState == EnumMatterState.Liquid)
            return true;

        if (col.CreativeInventoryTabs is { Length: > 0 }
            && HasAnyExcludedCreativeTab(col.CreativeInventoryTabs))
            return true;

        if (col.CreativeInventoryStacks != null)
        {
            foreach (CreativeTabAndStackList entry in col.CreativeInventoryStacks)
            {
                if (entry?.Tabs != null && HasAnyExcludedCreativeTab(entry.Tabs))
                {
                    // Only exclude this stack if it matches an excluded-tab entry's stacks.
                    // Safer: if collectible has ANY excluded-tab registration, drop it.
                    return true;
                }
            }
        }

        string typeName = col.GetType().Name;
        if (typeName.IndexOf("Creature", StringComparison.OrdinalIgnoreCase) >= 0
            || typeName.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0
            || typeName.IndexOf("Randomizer", StringComparison.OrdinalIgnoreCase) >= 0
            || typeName.IndexOf("Loot", StringComparison.OrdinalIgnoreCase) >= 0
            || typeName.IndexOf("Creative", StringComparison.OrdinalIgnoreCase) >= 0
            || typeName.Equals("BlockCommand", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("ItemCreature", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("ItemDeadButterfly", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("ItemNpc", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("BlockCreativeRotor", StringComparison.OrdinalIgnoreCase))
            return true;

        string path = col.Code?.Path ?? "";
        // Creative-only blocks (creativegrass, creativerotor, …).
        if (path.IndexOf("creative", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (path.StartsWith("creature", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("creature-", StringComparison.OrdinalIgnoreCase)
            || path.Contains("creature-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("meta/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("meta-", StringComparison.OrdinalIgnoreCase)
            || path.Equals("commandblock", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("commandblock", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("conditionalblock", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("tickerblock", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("worldgenhook", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("randomizer", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("stackrandomizer", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("lootrandomizer", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("npcstick", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("measuringrope", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("textureflipper", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("blockcopy", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("decalcan", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("cropprop", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("initcommandblock", StringComparison.OrdinalIgnoreCase))
            return true;

        // Shape path under entity/ often means creature inventory model.
        try
        {
            if (col is Vintagestory.API.Common.Item itemCol
                && itemCol.Shape?.Base != null
                && itemCol.Shape.Base.Path.StartsWith("entity/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // ignore shape access issues
        }

        if (col is Vintagestory.API.Common.Item item)
        {
            if (IsLiquidPortionItem(item))
                return true;

            if (IsExcludedLiquidItem(item))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves any stack to a liquid portion item (waterportion etc.).
    /// Never returns world liquid blocks (water-still, lava-still).
    /// </summary>
    public static ItemStack? TryGetLiquidPortionStack(IWorldAccessor world, ItemStack? sourceStack)
    {
        if (sourceStack?.Collectible == null || world == null)
            return null;

        // Container with liquid content (bucket, jug…).
        if (sourceStack.Block is BlockLiquidContainerBase container)
        {
            ItemStack? content = container.GetContent(sourceStack)?.Clone();
            if (content?.Item != null)
            {
                content.StackSize = 1;
                return content;
            }
        }

        // Already an item portion.
        if (sourceStack.Item != null)
        {
            if (IsLiquidPortionItem(sourceStack.Item))
            {
                ItemStack clone = sourceStack.Clone();
                clone.StackSize = 1;
                return clone;
            }

            if (sourceStack.ItemAttributes?["contentItemCode"].Exists == true)
            {
                string contentCode = sourceStack.ItemAttributes["contentItemCode"].AsString();
                if (!string.IsNullOrEmpty(contentCode))
                {
                    AssetLocation contentAsset = AssetLocation.Create(contentCode, sourceStack.Collectible.Code.Domain);
                    Vintagestory.API.Common.Item? contentItem = world.GetItem(contentAsset);
                    if (contentItem != null)
                        return new ItemStack(contentItem, 1);
                }
            }

            return null;
        }

        // Liquid world block (water/lava): map via whenFilled only. Do NOT return the block itself.
        if (sourceStack.Block != null)
            return TryPortionFromWhenFilled(world, sourceStack.Block);

        return null;
    }

    private static string BuildStackKey(ItemStack stack)
    {
        string code = stack.Collectible?.Code?.ToString() ?? "null";
        // Avoid ToJsonToken when no attributes — that was a major cost during gather.
        if (stack.Attributes == null || stack.Attributes.Count == 0)
            return code;

        return code + "|" + stack.Attributes.ToJsonToken();
    }

    public static bool StacksMatchForFilter(IWorldAccessor world, ItemStack? a, ItemStack? b)
    {
        if (a?.Collectible == null || b?.Collectible == null)
            return false;

        return a.Collectible.Code.Equals(b.Collectible.Code);
    }

    public static string GetCodeKey(ItemStack? stack)
        => stack?.Collectible?.Code?.ToString() ?? "";

    private sealed class CachedEntry
    {
        public required ItemStack Stack;
        public required string CodeKey;
        public required string SearchName;
        public required string SearchFull;

        public static CachedEntry FromStack(ItemStack stack)
        {
            string code = stack.Collectible?.Code?.ToString() ?? "";
            string name = "";
            try
            {
                name = stack.GetName() ?? "";
            }
            catch
            {
                name = code;
            }

            string nameSearch = name.ToSearchFriendly().ToLowerInvariant();
            string fullSearch = (name + " " + code).ToSearchFriendly().ToLowerInvariant();

            return new CachedEntry
            {
                Stack = stack,
                CodeKey = code,
                SearchName = nameSearch,
                SearchFull = fullSearch
            };
        }
    }

    public class BrowserSlot : ItemSlot
    {
        private readonly PipeFilterItemBrowser browser;

        public BrowserSlot(PipeFilterItemBrowser inventory) : base(inventory)
        {
            browser = inventory;
        }

        public override int MaxSlotStackSize => 1;

        public override bool CanTake() => false;

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
            => false;

        public override bool CanHold(ItemSlot sourceSlot) => false;

        public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            if (!Empty && Itemstack?.Collectible != null)
            {
                // Clone only on click (not for every visible slot refill).
                browser.OnStackClicked?.Invoke(Itemstack.Clone());
                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
                return;
            }

            op.MovedQuantity = 0;
            op.RequestedQuantity = 0;
        }

        protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            ActivateSlot(sourceSlot, ref op);
        }

        public override bool TryFlipWith(ItemSlot itemSlot) => false;
    }
}
