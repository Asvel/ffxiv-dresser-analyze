using System.Text.Encodings.Web;
using System.Text.Json;
using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Excel;
using Lumina.Excel.Sheets;

#pragma warning disable IDE0079
#pragma warning disable CA1859
namespace ffxiv_dresser_analyze_client
{
    internal class StaticData
    {
        public static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        public static readonly byte[] BmpHeader = [
            0x42, 0x4D, 0x7A, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x7A, 0x00, 0x00, 0x00, 0x6C, 0x00,
            0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0xD8, 0xFF, 0xFF, 0xFF, 0x01, 0x00, 0x20, 0x00, 0x03, 0x00,
            0x00, 0x00, 0x00, 0x19, 0x00, 0x00, 0x60, 0x09, 0x00, 0x00, 0x60, 0x09, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0xFF,
            // Firefox 不支持只带 alpha mask 不带后续 color space 内容的 DIB header，加个无效的 bV4CSType=-1 哄哄它
            // https://hg.mozilla.org/mozilla-central/file/tip/image/decoders/nsBMPDecoder.cpp
            0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 
        ];

        public readonly Dictionary<string, byte[]> Jsons = [];

        private readonly GameData lumina;
        private readonly Category uiPack;

        private readonly ushort[] itemIconIds;
        private readonly HashSet<uint> cabinetItemIdSet;
        private int[]? itemCategories;

        public StaticData(string sqpackPath)
        {
            // 这个预加载老慢了，所以把用不到的删一删
            foreach (var category in Repository.CategoryNameToIdMap.Keys)
            {
                if (category != "ui" && category != "exd")
                {
                    Repository.CategoryNameToIdMap.Remove(category);
                }
            }

            lumina = new GameData(sqpackPath, new() { DefaultExcelLanguage = Language.ChineseSimplified, LoadMultithreaded = true });
            if (lumina.GetFile("exd/Item_0_chs.exd") == null)
            {
                Console.Write("请输入游戏数据语言（J/E/D/F/K）：");
                var input = Console.ReadLine()!;
                lumina.Options.DefaultExcelLanguage = char.ToUpperInvariant(input[0]) switch
                {
                    'J' => Language.Japanese,
                    'E' => Language.English,
                    'D' => Language.German,
                    'F' => Language.French,
                    'K' => Language.Korean,
                    _ => throw new InvalidOperationException("输入内容无法识别"),
                };
            }

            uiPack = lumina.Repositories["ffxiv"].Categories[6][0];

            var sItem = lumina.GetExcelSheet<Item>()!;
            itemIconIds = sItem.Select(eItem => eItem.Icon).ToArray();
            cabinetItemIdSet = lumina.GetExcelSheet<Cabinet>()!.Select(e => e.Item.RowId).ToHashSet();

            AddJson("/data/outfits", GenerateOutfits());
            AddJson("/data/cabinets", GenerateCabinets());
            AddJson("/data/reclaims", GenerateReclaims());
            AddJson("/data/identicals", GenerateIdenticals());
            AddJson("/data/semi-identicals", GenerateIdenticals(semi: true));
            AddJson("/data/dyes", GenerateDyes());
        }

        public byte[]? GetIcon(uint itemId, bool hq)
        {
            if (itemId >= itemIconIds.Length) return null;
            var iconId = itemIconIds[itemId].ToString().PadLeft(6, '0');
            var iconPath = $"ui/icon/{iconId[..3]}000/{(hq ? "hq/" : "")}{iconId}.tex";
            var hash = GameData.GetFileHash(iconPath);
            var file = uiPack.GetFile<TexFile>(hash);
            return file?.ImageData;
        }

        private void AddJson(string path, object content)
        {
            Jsons.Add(path, JsonSerializer.SerializeToUtf8Bytes(content, JsonOptions));
        }

        private static ulong GetSortKey(Item eItem)
        {
            return (((10000ul - eItem.LevelItem.RowId) * 100000u + eItem.Unknown4) * 100000u + eItem.RowId);
        }

        private object GenerateOutfits()
        {
            var sItem = lumina.GetExcelSheet<Item>()!;
            var outfits = new List<(ulong SortKey, object Value)>();
            foreach (var eMirageStoreSetItem in lumina.GetExcelSheet<RawRow>(name: "MirageStoreSetItem")!)
            {
                var id = eMirageStoreSetItem.RowId;
                if (id == 0) continue;
                var name = sItem[id].Name.ToString();

                var items = new List<object>();
                var cabinet = true;
                ulong? sortKey = null;
                for (var i = 0; i < 9; i++)
                {
                    var itemId = (uint)eMirageStoreSetItem.ReadColumn(2 + i);
                    if (itemId == 0) continue;
                    var eItem = sItem[itemId];
                    var itemName = eItem.Name.ToString();
                    var dyeCount = eItem.DyeCount;
                    items.Add(new { id = itemId, name = itemName, dyeCount });
                    cabinet = cabinet && cabinetItemIdSet.Contains(itemId);
                    sortKey ??= GetSortKey(eItem);
                }

                if (items.Count == 0) continue;
                outfits.Add((sortKey ?? 0, new { id, name, items, cabinet }));
            }
            outfits.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
            return outfits.Select(x => x.Value).ToArray();
        }

        private object GenerateCabinets()
        {
            return GenerateCategorized(lumina.GetExcelSheet<Cabinet>()!.Select(e => e.Item.Value));
        }
        private object GenerateReclaims()
        {
            var sGilShopInfo = lumina.GetExcelSheet<GilShopInfo>()!;
            return GenerateCategorized(lumina.GetExcelSheet<Item>()!.Where(eItem =>
            {
                if (!eItem.IsGlamorous) return false;
                if (!eItem.IsUntradable) return false;
                if (eItem.PriceMid > 1000) return false;
                if (cabinetItemIdSet.Contains(eItem.RowId)) return false;
                if (sGilShopInfo[eItem.RowId].Unknown0 != 1) return false;
                return true;
            }));

        }
        private object GenerateCategorized(IEnumerable<Item> items)
        {
            itemCategories ??= lumina.GetExcelSheet<RawRow>(name: "EquipSlotCategory")!.Select(e =>
            {
                for (var i = 0; i < e.Columns.Count; i++)
                {
                    if ((sbyte)e.ReadColumn(i) == 1) return i + 1;
                }
                return 0;
            }).ToArray();

            var ret = items
                .Where(eItem => eItem.Name.ByteLength > 0)
                .Select(eItem =>
                {
                    var id = eItem.RowId;
                    var name = eItem.Name.ToString();
                    var dyeCount = eItem.DyeCount;
                    var category = itemCategories[eItem.EquipSlotCategory.RowId];
                    var sortKey = GetSortKey(eItem);
                    return new { id, name, dyeCount, category, sortKey };
                })
                .GroupBy(x => x.category)
                .Select(group =>
                {
                    var category = group.Key;
                    var entries = group.ToList();
                    entries.Sort((a, b) => a.sortKey.CompareTo(b.sortKey));
                    var items = entries.Select(x => new { x.id, x.name, x.dyeCount }).ToArray();
                    return new { category, items };
                }).ToList();
            ret.Sort((a, b) => a.category - b.category);
            return ret;
        }

        private object GenerateIdenticals(bool semi = false)
        {
            var identicals = lumina.GetExcelSheet<Item>()!
                .Where(eItem => eItem.IsGlamorous && (!semi || eItem.DyeCount > 0))
                .GroupBy(eItem =>
                (
                    eItem.EquipSlotCategory.RowId,
                    semi
                        ? eItem.ModelMain >>> 32 > 0 ? eItem.ModelMain << 32 : eItem.ModelMain << 48
                        : eItem.ModelMain
                ))
                .Where(group => group.Count() > 1)
                .SelectMany(group =>
                {
                    var groupId = group.FirstOrDefault().RowId;
                    return group.Select(eItem => (eItem, groupId, sortKey: (eItem.EquipSlotCategory.RowId, GetSortKey(eItem))));
                })
                .ToList();
            identicals.Sort((a, b) => a.sortKey.CompareTo(b.sortKey));
            return identicals.Select(x =>
            {
                var itemId = x.eItem.RowId;
                var name = x.eItem.Name.ToString();
                var dyeCount = x.eItem.DyeCount;
                return new { x.groupId, itemId, name, dyeCount };
            });
        }

        private object GenerateDyes()
        {
            return lumina.GetExcelSheet<Stain>()!.Select(eStain =>
            {
                var id = eStain.RowId;
                var name = eStain.Name.ToString();
                var color = eStain.Color.ToString("X6");
                return new { id, name, color };
            });
        }
    }
}
