using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ffxiv_dresser_analyze_client
{
    internal class DresserData
    {
        private static readonly byte?[] sig = [0x48, 0x8B, 0x0D, null, null, null, null, 0x48, 0x8D, 0x44, 0x24, null, 0x0F, 0x57, 0xC0];
        private static readonly int dresserSize = 800;

        private readonly IntPtr hProcess;
        private readonly IntPtr pProcessBase;
        private readonly IntPtr ppDresserData = IntPtr.Zero;

        private readonly byte[] data;
        private readonly byte[] dataIncoming;

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

            data = new byte[(4 + 1 + 1) * dresserSize + 2];
            dataIncoming = new byte[data.Length];
        }

        public void Read()
        {
            WinApi.ReadProcessMemory(hProcess, ppDresserData, dataIncoming, 8, IntPtr.Zero);
            var pDresserData = (IntPtr)BitConverter.ToUInt64(dataIncoming);
            WinApi.ReadProcessMemory(hProcess, pDresserData + 4, dataIncoming, dataIncoming.Length, IntPtr.Zero);

            if (dataIncoming[^1] == 0) return;
            if (data[^1] == 0 || !data.SequenceEqual(dataIncoming))
            {
                LastModified = DateTime.Now.ToUniversalTime().ToString("r");
                dataIncoming.CopyTo(data, 0);
            }
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
    }
}
