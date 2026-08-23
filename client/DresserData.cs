using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ffxiv_dresser_analyze_client
{
    internal class DresserData
    {
        private static readonly byte?[] sig = [0x48, 0x8B, 0x0D, null, null, null, null, 0x48, 0x8D, 0x44, 0x24, null, 0x0F, 0x57, 0xC0];
        // Framework singleton; ItemFinderModule contains the server-loaded armoire bitset.
        private static readonly byte?[] frameworkSig = [0x48, 0x8B, 0x1D, null, null, null, null, 0x8B, 0x7C, 0x24];
        // Client::Game::UI::UIState singleton; its Cabinet member is the authoritative armoire data.
        private static readonly byte?[] uiStateSig = [0x48, 0x8D, 0x0D, null, null, null, null, 0x45, 0x33, 0xC0, 0xBA];
        private static readonly int dresserSize = 800;
        private static readonly int frameworkUIModuleOffset = 0x2B68;
        private static readonly int uiItemFinderModuleOffset = 0x8FA18;
        private static readonly int itemFinderCabinetStateOffset = 0xA9;
        private static readonly int itemFinderCabinetBitsOffset = 0x16D8;
        private static readonly int uiStateCabinetOffset = 0x1688;
        private static readonly int cabinetUnlockedItemsOffset = 0x08;
        private static readonly int cabinetBitsetSize = 125;
        private static readonly int cabinetVectorHeaderSize = 24;
        private static readonly int cabinetVectorMaxSize = 100000;

        private readonly IntPtr hProcess;
        private readonly IntPtr pProcessBase;
        private readonly IntPtr ppDresserData = IntPtr.Zero;
        private readonly IntPtr pFramework = IntPtr.Zero;
        private readonly IntPtr pUIState = IntPtr.Zero;

        private readonly byte[] data;
        private readonly byte[] dataIncoming;
        private readonly byte[] cabinetBits = new byte[cabinetBitsetSize * sizeof(uint)];
        private readonly byte[] cabinetBitsIncoming = new byte[cabinetBitsetSize * sizeof(uint)];

        public DresserData(Process gameProcess)
        {
            hProcess = WinApi.OpenProcess(0x00000010, false, (uint)gameProcess.Id);
            if (hProcess == IntPtr.Zero) throw new InvalidOperationException("访问游戏进程失败");
            pProcessBase = gameProcess.MainModule!.BaseAddress;

            var textSectionAddress = pProcessBase;
            var textSectionSize = 0u;
            var header = new byte[0x800];
            WinApi.ReadProcessMemory(hProcess, pProcessBase, header, header.Length, IntPtr.Zero);
            var header64 = MemoryMarshal.Cast<byte, ulong>(header);
            for (var i = 0; i < header64.Length; i++)
            {
                if (header64[i] == 0x747865742E/*.text*/)
                {
                    textSectionAddress += (int)(header64[i + 1] >> 32);
                    textSectionSize = (uint)(header64[i + 1] & 0xffffffffL);
                    break;
                }
            }

            var section = new byte[textSectionSize];
            WinApi.ReadProcessMemory(hProcess, textSectionAddress, section, section.Length, IntPtr.Zero);
            for (var i = 0; i < section.Length - sig.Length; i++)
            {
                for (var j = 0; j < sig.Length; j++)
                {
                    if (sig[j] != null && section[i + j] != sig[j]) goto Next;
                }
                var targetIndex = Array.IndexOf(sig, null);
                var target = BitConverter.ToInt32(section, i + targetIndex);
                var offset = i + targetIndex + 4 + target;
                ppDresserData = textSectionAddress + offset;
                break;
            Next:;
            }

            if (ppDresserData == IntPtr.Zero)
            {
                throw new NotSupportedException("定位投影台数据失败");
            }

            pFramework = FindRipRelativePointer(section, textSectionAddress, frameworkSig);
            pUIState = FindRipRelativePointer(section, textSectionAddress, uiStateSig);

            data = new byte[(4 + 1 + 1) * dresserSize + 2];
            dataIncoming = new byte[data.Length];
        }

        public void Read()
        {
            WinApi.ReadProcessMemory(hProcess, ppDresserData, dataIncoming, 8, IntPtr.Zero);
            var pDresserData = (IntPtr)BitConverter.ToUInt64(dataIncoming);
            WinApi.ReadProcessMemory(hProcess, pDresserData + 4, dataIncoming, dataIncoming.Length, IntPtr.Zero);

            if (dataIncoming[^1] == 0)
            {
                ReadCabinet();
                return;
            }
            if (data[^1] == 0 || !data.SequenceEqual(dataIncoming))
            {
                LastModified = DateTime.Now.ToUniversalTime().ToString("r");
                dataIncoming.CopyTo(data, 0);
            }

            ReadCabinet();
        }

        private static IntPtr FindRipRelativePointer(byte[] section, IntPtr sectionAddress, byte?[] signature)
        {
            for (var i = 0; i <= section.Length - signature.Length; i++)
            {
                var matched = true;
                for (var j = 0; j < signature.Length; j++)
                {
                    if (signature[j] != null && section[i + j] != signature[j])
                    {
                        matched = false;
                        break;
                    }
                }
                if (!matched) continue;
                var targetIndex = Array.IndexOf(signature, null);
                var target = BitConverter.ToInt32(section, i + targetIndex);
                var offset = i + targetIndex + 4 + target;
                return sectionAddress + offset;
            }
            return IntPtr.Zero;
        }

        private void ReadCabinet()
        {
            CabinetState = null;
            CabinetReadError = 0;
            CabinetUiModuleFound = false;
            CabinetItemFinderFound = false;
            CabinetReadStage = "framework-signature";

            if (TryReadUiStateCabinet()) return;

            if (pFramework == IntPtr.Zero)
            {
                CabinetLoaded = false;
                LastCabinetModified = null;
                Array.Clear(cabinetBits);
                return;
            }

            var pointerBytes = new byte[IntPtr.Size];
            if (!ReadMemory(pFramework, pointerBytes))
            {
                CabinetReadStage = "framework-read-failed";
                CabinetLoaded = false;
                LastCabinetModified = null;
                Array.Clear(cabinetBits);
                return;
            }
            var framework = (IntPtr)BitConverter.ToUInt64(pointerBytes);
            if (framework == IntPtr.Zero)
            {
                CabinetReadStage = "framework-null";
                CabinetLoaded = false;
                LastCabinetModified = null;
                Array.Clear(cabinetBits);
                return;
            }

            CabinetReadStage = "ui-module";
            if (!ReadMemory(framework + frameworkUIModuleOffset, pointerBytes))
            {
                CabinetReadStage = "ui-module-read-failed";
                CabinetLoaded = false;
                LastCabinetModified = null;
                Array.Clear(cabinetBits);
                return;
            }
            var uiModule = (IntPtr)BitConverter.ToUInt64(pointerBytes);
            if (uiModule == IntPtr.Zero)
            {
                CabinetReadStage = "ui-module-null";
                CabinetLoaded = false;
                LastCabinetModified = null;
                Array.Clear(cabinetBits);
                return;
            }
            CabinetUiModuleFound = true;

            var itemFinder = uiModule + uiItemFinderModuleOffset;
            CabinetItemFinderFound = true;
            CabinetReadStage = "cabinet-state";
            var state = new byte[1];
            if (!ReadMemory(itemFinder + itemFinderCabinetStateOffset, state))
            {
                CabinetReadStage = "cabinet-state-read-failed";
                CabinetLoaded = false;
                LastCabinetModified = null;
                Array.Clear(cabinetBits);
                return;
            }
            CabinetState = state[0];
            // State 2 means the server response has been received. Do not expose a stale/empty bitset otherwise.
            if (state[0] != 2)
            {
                CabinetReadStage = $"cabinet-state-{state[0]}";
                CabinetLoaded = false;
                LastCabinetModified = null;
                Array.Clear(cabinetBits);
                return;
            }

            CabinetReadStage = "cabinet-bits";
            if (!ReadMemory(itemFinder + itemFinderCabinetBitsOffset, cabinetBitsIncoming))
            {
                CabinetReadStage = "cabinet-bits-read-failed";
                CabinetLoaded = false;
                LastCabinetModified = null;
                Array.Clear(cabinetBits);
                return;
            }
            cabinetBitsIncoming.CopyTo(cabinetBits, 0);
            var ids = new List<uint>();
            for (var i = 0; i < cabinetBits.Length / sizeof(uint); i++)
            {
                var value = BitConverter.ToUInt32(cabinetBits, i * sizeof(uint));
                for (var bit = 0; bit < 32; bit++)
                {
                    if ((value & (1u << bit)) != 0) ids.Add((uint)(i * 32 + bit));
                }
            }
            cabinetIds.Clear();
            cabinetIds.AddRange(ids);
            CabinetLoaded = true;
            CabinetReadStage = "loaded";
            LastCabinetModified ??= DateTime.Now.ToUniversalTime().ToString("r");
        }

        private bool TryReadUiStateCabinet()
        {
            if (pUIState == IntPtr.Zero) return false;

            CabinetReadStage = "ui-state-cabinet-state";
            var state = new byte[1];
            if (!ReadMemory(pUIState + uiStateCabinetOffset, state))
            {
                CabinetReadStage = "ui-state-cabinet-state-read-failed";
                return false;
            }
            CabinetState = state[0];
            if (state[0] != 2)
            {
                CabinetReadStage = $"ui-state-cabinet-state-{state[0]}";
                return CabinetLoaded;
            }

            var vector = new byte[cabinetVectorHeaderSize];
            CabinetReadStage = "ui-state-cabinet-vector";
            if (!ReadMemory(pUIState + uiStateCabinetOffset + cabinetUnlockedItemsOffset, vector))
            {
                CabinetReadStage = "ui-state-cabinet-vector-read-failed";
                return CabinetLoaded;
            }

            var begin = (ulong)BitConverter.ToInt64(vector, 0);
            var end = (ulong)BitConverter.ToInt64(vector, IntPtr.Size);
            if (begin == 0 || end < begin || end - begin > (ulong)cabinetVectorMaxSize)
            {
                CabinetReadStage = "ui-state-cabinet-vector-invalid";
                return CabinetLoaded;
            }

            var items = new byte[end - begin];
            if (!ReadMemory((IntPtr)begin, items))
            {
                CabinetReadStage = "ui-state-cabinet-items-read-failed";
                return CabinetLoaded;
            }

            CabinetVectorLength = items.Length;
            CabinetVectorNonZeroBytes = 0;
            CabinetVectorSetBits = 0;
            var ids = new List<uint>();
            // UnlockedItems is a packed bit vector: each byte represents eight Cabinet rows.
            for (var i = 0; i < items.Length; i++)
            {
                var value = items[i];
                if (value == 0) continue;
                CabinetVectorNonZeroBytes++;
                for (var bit = 0; bit < 8; bit++)
                {
                    if ((value & (1 << bit)) == 0) continue;
                    CabinetVectorSetBits++;
                    ids.Add((uint)(i * 8 + bit));
                }
            }
            cabinetIds.Clear();
            cabinetIds.AddRange(ids);
            CabinetLoaded = true;
            CabinetReadStage = "loaded-ui-state";
            LastCabinetModified ??= DateTime.Now.ToUniversalTime().ToString("r");
            return true;
        }

        private bool ReadMemory(IntPtr address, byte[] buffer)
        {
            Array.Clear(buffer);
            if (WinApi.ReadProcessMemory(hProcess, address, buffer, buffer.Length, IntPtr.Zero)) return true;
            CabinetReadError = Marshal.GetLastWin32Error();
            return false;
        }

        public bool Loaded
        {
            get => data[^1] != 0;
        }

        public Span<uint> ItemIds
        {
            get => MemoryMarshal.Cast<byte, uint>(data.AsSpan(0, 4 * dresserSize));
        }
        public Span<byte> Dye1Ids
        {
            get => data.AsSpan(4 * dresserSize, dresserSize);
        }
        public Span<byte> Dye2Ids
        {
            get => data.AsSpan(5 * dresserSize, dresserSize);
        }

        public byte[] Json
        {
            get
            {
                var itemIds = ItemIds;
                var dye1Ids = Dye1Ids;
                var dye2Ids = Dye2Ids;

                var items = new List<object>();
                for (var i = 0; i < itemIds.Length; i++)
                {
                    var id = itemIds[i];
                    if (id == 0) continue;
                    var hq = false;
                    if (id > 1000000)
                    {
                        id -= 1000000;
                        hq = true;
                    }
                    var dyes = new int[] { dye1Ids[i], dye2Ids[i] };
                    items.Add(new { id, hq, dyes });
                }
                return JsonSerializer.SerializeToUtf8Bytes(items);
            }
        }

        public string LastModified { get; private set; } = "";
        public bool CabinetLoaded { get; private set; }
        public string? LastCabinetModified { get; private set; }
        public byte? CabinetState { get; private set; }
        public bool CabinetUiModuleFound { get; private set; }
        public bool CabinetItemFinderFound { get; private set; }
        public string CabinetReadStage { get; private set; } = "not-read";
        public int CabinetReadError { get; private set; }
        public int CabinetVectorLength { get; private set; }
        public int CabinetVectorNonZeroBytes { get; private set; }
        public int CabinetVectorSetBits { get; private set; }
        private readonly List<uint> cabinetIds = [];

        public byte[] CabinetJson
        {
            get
            {
                if (!CabinetLoaded)
                {
                    return JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        loaded = false,
                        cabinetIds = Array.Empty<uint>(),
                        cabinetState = CabinetState,
                        frameworkFound = pFramework != IntPtr.Zero,
                        uiModuleFound = CabinetUiModuleFound,
                        itemFinderFound = CabinetItemFinderFound,
                        cabinetVectorLength = CabinetVectorLength,
                        cabinetVectorNonZeroBytes = CabinetVectorNonZeroBytes,
                        cabinetVectorSetBits = CabinetVectorSetBits,
                        readStage = CabinetReadStage,
                        readError = CabinetReadError,
                    });
                }
                return JsonSerializer.SerializeToUtf8Bytes(new
                {
                    loaded = true,
                    cabinetIds,
                    cabinetState = CabinetState,
                    frameworkFound = pFramework != IntPtr.Zero,
                    uiStateFound = pUIState != IntPtr.Zero,
                    uiModuleFound = CabinetUiModuleFound,
                    itemFinderFound = CabinetItemFinderFound,
                    cabinetVectorLength = CabinetVectorLength,
                    cabinetVectorNonZeroBytes = CabinetVectorNonZeroBytes,
                    cabinetVectorSetBits = CabinetVectorSetBits,
                    readStage = CabinetReadStage,
                    readError = CabinetReadError,
                });
            }
        }
    }
}
