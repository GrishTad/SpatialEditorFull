using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnitySpatialMedia.Internal
{
    internal static class Mp4Tags
    {
        public const string STCO = "stco";
        public const string CO64 = "co64";
        public const string FREE = "free";
        public const string MDAT = "mdat";
        public const string HDLR = "hdlr";
        public const string FTYP = "ftyp";
        public const string ESDS = "esds";
        public const string SOUN = "soun";
        public const string VIDE = "vide";
        public const string SA3D = "SA3D";
        public const string PRHD = "prhd";
        public const string EQUI = "equi";
        public const string ST3D = "st3d";
        public const string MOOV = "moov";
        public const string TRAK = "trak";
        public const string MDIA = "mdia";
        public const string MINF = "minf";
        public const string STBL = "stbl";
        public const string STSD = "stsd";
        public const string UUID = "uuid";
        public const string WAVE = "wave";
        public const string SV3D = "sv3d";
        public const string PROJ = "proj";

        public const string NONE = "NONE";
        public const string RAW_ = "raw ";
        public const string TWOS = "twos";
        public const string SOWT = "sowt";
        public const string FL32 = "fl32";
        public const string FL64 = "fl64";
        public const string IN24 = "in24";
        public const string IN32 = "in32";
        public const string ULAW = "ulaw";
        public const string ALAW = "alaw";
        public const string LPCM = "lpcm";
        public const string MP4A = "mp4a";
        public const string OPUS = "Opus";

        public const string AVC1 = "avc1";
        public const string VP09 = "vp09";
        public const string AV01 = "av01";
        public const string HEV1 = "hev1";
        public const string HVC1 = "hvc1";
        public const string DVH1 = "dvh1";
        public const string APCN = "apcn";
        public const string APCH = "apch";
        public const string APCS = "apcs";
        public const string APCO = "apco";
        public const string AP4H = "ap4h";
        public const string AP4X = "ap4x";

        public static readonly HashSet<string> SoundSampleDescriptions =
            new HashSet<string>(StringComparer.Ordinal)
            {
                NONE, RAW_, TWOS, SOWT, FL32, FL64, IN24, IN32,
                ULAW, ALAW, LPCM, MP4A, OPUS
            };

        public static readonly HashSet<string> VideoSampleDescriptions =
            new HashSet<string>(StringComparer.Ordinal)
            {
                NONE, AVC1, VP09, AV01, HEV1, HVC1, DVH1, APCN, APCH, APCS, APCO, AP4H, AP4X
            };

        public static readonly HashSet<string> Containers =
            new HashSet<string>(StringComparer.Ordinal)
            {
                MDIA, MINF, MOOV, STBL, STSD, TRAK, WAVE, SV3D, PROJ
            };

        static Mp4Tags()
        {
            foreach (string tag in SoundSampleDescriptions) Containers.Add(tag);
            foreach (string tag in VideoSampleDescriptions) Containers.Add(tag);
        }
    }

    internal static class BigEndian
    {
        public static byte ReadU8(Stream s)
        {
            int b = s.ReadByte();
            if (b < 0) throw new EndOfStreamException();
            return (byte)b;
        }

        public static short ReadI16(Stream s)
        {
            byte[] b = new byte[2];
            ReadExactly(s, b);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToInt16(b);
        }

        public static ushort ReadU16(Stream s)
        {
            byte[] b = new byte[2];
            ReadExactly(s, b);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToUInt16(b);
        }

        public static int ReadI32(Stream s)
        {
            byte[] b = new byte[4];
            ReadExactly(s, b);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToInt32(b);
        }

        public static uint ReadU32(Stream s)
        {
            byte[] b = new byte[4];
            ReadExactly(s, b);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToUInt32(b);
        }

        public static ulong ReadU64(Stream s)
        {
            byte[] b = new byte[8];
            ReadExactly(s, b);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToUInt64(b);
        }

        public static double ReadDouble(Stream s)
        {
            byte[] b = new byte[8];
            ReadExactly(s, b);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToDouble(b);
        }

        public static string ReadTag(Stream s)
        {
            byte[] b = new byte[4];
            ReadExactly(s, b);
            return Encoding.ASCII.GetString(b);
        }

        public static void WriteU32(Stream s, uint value)
        {
            byte[] b = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            s.Write(b, 0, b.Length);
        }

        public static void WriteU64(Stream s, ulong value)
        {
            byte[] b = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            s.Write(b, 0, b.Length);
        }

        public static void WriteTag(Stream s, string tag)
        {
            if (tag == null || tag.Length != 4) throw new ArgumentException("Tag must be 4 chars.");
            byte[] bytes = Encoding.ASCII.GetBytes(tag);
            s.Write(bytes, 0, bytes.Length);
        }

        public static void ReadExactly(Stream s, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = s.Read(buffer, total, buffer.Length - total);
                if (read <= 0) throw new EndOfStreamException();
                total += read;
            }
        }

        public static void CopyExact(Stream input, Stream output, long size)
        {
            const int block = 64 * 1024 * 1024;
            byte[] buffer = new byte[block];
            long left = size;
            while (left > 0)
            {
                int chunk = (int)Math.Min(block, left);
                int read = input.Read(buffer, 0, chunk);
                if (read <= 0) throw new EndOfStreamException();
                output.Write(buffer, 0, read);
                left -= read;
            }
        }
    }

    internal abstract class Mp4Box
    {
        protected Mp4Box(string name, long position, int headerSize, long contentSize)
        {
            Name = name;
            Position = position;
            HeaderSize = headerSize;
            ContentSize = contentSize;
        }

        public string Name { get; protected set; }
        public long Position { get; set; }
        public int HeaderSize { get; set; }
        public long ContentSize { get; set; }
        public byte[] Contents { get; protected set; }
        public long Size => HeaderSize + ContentSize;
        public long ContentStart => Position + HeaderSize;

        public virtual byte[] ReadContent(Stream input)
        {
            if (Contents != null) return Contents;
            byte[] data = new byte[ContentSize];
            input.Position = ContentStart;
            int total = 0;
            while (total < data.Length)
            {
                int read = input.Read(data, total, data.Length - total);
                if (read <= 0) throw new EndOfStreamException();
                total += read;
            }
            return data;
        }

        protected void WriteHeader(Stream output)
        {
            if (HeaderSize == 16)
            {
                BigEndian.WriteU32(output, 1);
                BigEndian.WriteTag(output, Name);
                BigEndian.WriteU64(output, (ulong)Size);
                return;
            }

            if (Size > uint.MaxValue) throw new InvalidOperationException($"Box {Name} exceeds 32-bit size.");
            BigEndian.WriteU32(output, (uint)Size);
            BigEndian.WriteTag(output, Name);
        }

        public abstract void Save(Stream input, Stream output, long delta);
    }

    internal sealed class RawMp4Box : Mp4Box
    {
        public RawMp4Box(string name, long position, int headerSize, long contentSize, byte[] contents = null)
            : base(name, position, headerSize, contentSize)
        {
            Contents = contents;
        }

        public override void Save(Stream input, Stream output, long delta)
        {
            WriteHeader(output);

            if (Name == Mp4Tags.STCO || Name == Mp4Tags.CO64)
            {
                WriteUpdatedIndex(input, output, delta, Name == Mp4Tags.STCO ? 4 : 8);
                return;
            }

            if (Contents != null)
            {
                output.Write(Contents, 0, Contents.Length);
                return;
            }

            input.Position = ContentStart;
            BigEndian.CopyExact(input, output, ContentSize);
        }

        private void WriteUpdatedIndex(Stream input, Stream output, long delta, int width)
        {
            Stream source = input;
            if (Contents != null)
            {
                source = new MemoryStream(Contents, writable: false);
            }
            else
            {
                input.Position = ContentStart;
            }

            uint vf = BigEndian.ReadU32(source);
            uint count = BigEndian.ReadU32(source);
            BigEndian.WriteU32(output, vf);
            BigEndian.WriteU32(output, count);

            if (width == 4)
            {
                for (int i = 0; i < count; i++)
                {
                    long value = (long)BigEndian.ReadU32(source) + delta;
                    if (value < 0 || value > uint.MaxValue) throw new InvalidDataException("stco overflow.");
                    BigEndian.WriteU32(output, (uint)value);
                }
                return;
            }

            for (int i = 0; i < count; i++)
            {
                long value = checked((long)BigEndian.ReadU64(source) + delta);
                if (value < 0) throw new InvalidDataException("co64 underflow.");
                BigEndian.WriteU64(output, (ulong)value);
            }
        }
    }

    internal sealed class ContainerMp4Box : Mp4Box
    {
        public ContainerMp4Box(string name, long position, int headerSize, long contentSize, int padding)
            : base(name, position, headerSize, contentSize)
        {
            Padding = padding;
        }

        public List<Mp4Box> Children { get; } = new List<Mp4Box>();
        public int Padding { get; }

        public void Resize()
        {
            long content = Padding;
            foreach (Mp4Box child in Children)
            {
                if (child is ContainerMp4Box container) container.Resize();
                content += child.Size;
            }
            ContentSize = content;
        }

        public void RemoveRecursive(string tag)
        {
            var filtered = new List<Mp4Box>(Children.Count);
            foreach (Mp4Box child in Children)
            {
                if (child.Name == tag) continue;
                if (child is ContainerMp4Box container) container.RemoveRecursive(tag);
                filtered.Add(child);
            }
            Children.Clear();
            Children.AddRange(filtered);
        }

        public void RemoveByName(string tag)
        {
            Children.RemoveAll(x => x.Name == tag);
        }

        public bool Add(Mp4Box element)
        {
            foreach (Mp4Box existing in Children)
            {
                if (existing.Name != element.Name) continue;
                if (existing is ContainerMp4Box a && element is ContainerMp4Box b)
                {
                    foreach (Mp4Box child in b.Children)
                    {
                        if (!a.Add(child)) return false;
                    }
                    return true;
                }
                return false;
            }

            Children.Add(element);
            return true;
        }

        public override void Save(Stream input, Stream output, long delta)
        {
            WriteHeader(output);

            if (Padding > 0)
            {
                input.Position = ContentStart;
                BigEndian.CopyExact(input, output, Padding);
            }

            foreach (Mp4Box child in Children)
            {
                child.Save(input, output, delta);
            }
        }
    }

    internal sealed class St3dMp4Box : Mp4Box
    {
        public St3dMp4Box() : base(Mp4Tags.ST3D, 0, 8, 5) { }
        public byte Mode { get; set; }

        public bool SetStereoMode(string mode)
        {
            switch (mode)
            {
                case "mono": Mode = 0; return true;
                case "top-bottom": Mode = 1; return true;
                case "left-right": Mode = 2; return true;
                case "stereo-custom": Mode = 3; return true;
                case "right-left": Mode = 4; return true;
                default: return false;
            }
        }

        public override void Save(Stream input, Stream output, long delta)
        {
            WriteHeader(output);
            BigEndian.WriteU32(output, 0);
            output.WriteByte(Mode);
        }
    }

    internal sealed class PrhdMp4Box : Mp4Box
    {
        public PrhdMp4Box() : base(Mp4Tags.PRHD, 0, 8, 16) { }
        public uint Yaw { get; set; }
        public uint Pitch { get; set; }
        public uint Roll { get; set; }

        public override void Save(Stream input, Stream output, long delta)
        {
            WriteHeader(output);
            BigEndian.WriteU32(output, 0);
            BigEndian.WriteU32(output, Yaw);
            BigEndian.WriteU32(output, Pitch);
            BigEndian.WriteU32(output, Roll);
        }
    }

    internal sealed class EquiMp4Box : Mp4Box
    {
        public EquiMp4Box() : base(Mp4Tags.EQUI, 0, 8, 20) { }
        public uint Top { get; set; }
        public uint Bottom { get; set; }
        public uint Left { get; set; }
        public uint Right { get; set; }

        public override void Save(Stream input, Stream output, long delta)
        {
            WriteHeader(output);
            BigEndian.WriteU32(output, 0);
            BigEndian.WriteU32(output, Top);
            BigEndian.WriteU32(output, Bottom);
            BigEndian.WriteU32(output, Left);
            BigEndian.WriteU32(output, Right);
        }
    }

    internal sealed class Sa3dMp4Box : Mp4Box
    {
        public Sa3dMp4Box() : base(Mp4Tags.SA3D, 0, 8, 0) { }

        public byte Version { get; set; }
        public byte AmbisonicType { get; set; }
        public bool HeadLockedStereo { get; set; }
        public uint AmbisonicOrder { get; set; }
        public byte ChannelOrdering { get; set; }
        public byte Normalization { get; set; }
        public uint NumChannels { get; set; }
        public List<uint> ChannelMap { get; } = new List<uint>();

        public override void Save(Stream input, Stream output, long delta)
        {
            WriteHeader(output);
            byte type = HeadLockedStereo ? (byte)(AmbisonicType | 0x80) : (byte)(AmbisonicType & 0x7F);
            output.WriteByte(Version);
            output.WriteByte(type);
            BigEndian.WriteU32(output, AmbisonicOrder);
            output.WriteByte(ChannelOrdering);
            output.WriteByte(Normalization);
            BigEndian.WriteU32(output, NumChannels);
            foreach (uint ch in ChannelMap) BigEndian.WriteU32(output, ch);
        }
    }

    internal sealed class Mp4File
    {
        public readonly List<Mp4Box> Contents = new List<Mp4Box>();
        public ContainerMp4Box MoovBox;
        public Mp4Box FirstMdatBox;
        public long FirstMdatPosition;

        public void Resize()
        {
            foreach (Mp4Box box in Contents)
            {
                if (box is ContainerMp4Box container) container.Resize();
            }
        }

        public void Save(Stream input, Stream output)
        {
            Resize();
            long newMdatStart = 0;
            foreach (Mp4Box box in Contents)
            {
                if (box.Name == Mp4Tags.MDAT)
                {
                    newMdatStart += box.HeaderSize;
                    break;
                }
                newMdatStart += box.Size;
            }

            long delta = newMdatStart - FirstMdatPosition;
            foreach (Mp4Box box in Contents) box.Save(input, output, delta);
        }
    }

    internal static class Mp4Parser
    {
        public static Mp4File Load(Stream input, Action<string> log)
        {
            var file = new Mp4File();
            List<Mp4Box> boxes = LoadMultiple(input, 0, input.Length);
            file.Contents.AddRange(boxes);

            foreach (Mp4Box box in file.Contents)
            {
                if (box.Name == Mp4Tags.MOOV) file.MoovBox = box as ContainerMp4Box;
                if (box.Name == Mp4Tags.MDAT && file.FirstMdatBox == null) file.FirstMdatBox = box;
            }

            if (file.MoovBox == null)
            {
                log("Error, file does not contain moov box.");
                return null;
            }

            if (file.FirstMdatBox == null)
            {
                log("Error, file does not contain mdat box.");
                return null;
            }

            file.FirstMdatPosition = file.FirstMdatBox.Position + file.FirstMdatBox.HeaderSize;
            file.Resize();
            return file;
        }

        private static List<Mp4Box> LoadMultiple(Stream input, long position, long end)
        {
            var loaded = new List<Mp4Box>();
            while (position + 4 < end)
            {
                Mp4Box box = LoadSingle(input, position, end);
                loaded.Add(box);
                long next = box.Position + box.Size;
                if (next <= position) throw new InvalidDataException("Invalid MP4 structure.");
                position = next;
            }
            return loaded;
        }

        private static Mp4Box LoadSingle(Stream input, long position, long end)
        {
            input.Position = position;
            uint size32 = BigEndian.ReadU32(input);
            string name = BigEndian.ReadTag(input);

            bool isContainer = Mp4Tags.Containers.Contains(name);
            if (name == Mp4Tags.MP4A && size32 == 12) isContainer = false;
            if (isContainer) return LoadContainer(input, position, end);

            switch (name)
            {
                case Mp4Tags.ST3D: return LoadSt3d(input, position, end);
                case Mp4Tags.PRHD: return LoadPrhd(input, position, end);
                case Mp4Tags.EQUI: return LoadEqui(input, position, end);
                case Mp4Tags.SA3D: return LoadSa3d(input, position, end);
                default: return LoadRaw(input, position, end);
            }
        }

        private static (ulong Size, int HeaderSize, string Name) ReadHeader(Stream input, long position, long end)
        {
            input.Position = position;
            ulong size = BigEndian.ReadU32(input);
            string name = BigEndian.ReadTag(input);
            int headerSize = 8;
            if (size == 1)
            {
                size = BigEndian.ReadU64(input);
                headerSize = 16;
            }
            if (size < 8 || position + (long)size > end) throw new InvalidDataException($"Invalid box size in {name}.");
            return (size, headerSize, name);
        }

        private static RawMp4Box LoadRaw(Stream input, long position, long end)
        {
            var h = ReadHeader(input, position, end);
            return new RawMp4Box(h.Name, position, h.HeaderSize, (long)h.Size - h.HeaderSize);
        }

        private static ContainerMp4Box LoadContainer(Stream input, long position, long end)
        {
            var h = ReadHeader(input, position, end);
            int padding = 0;
            if (h.Name == Mp4Tags.STSD) padding = 8;

            if (Mp4Tags.SoundSampleDescriptions.Contains(h.Name))
            {
                long current = input.Position;
                input.Position = current + 8;
                short version = BigEndian.ReadI16(input);
                input.Position = current;
                if (version == 0) padding = 28;
                else if (version == 1) padding = 44;
                else if (version == 2) padding = 64;
            }
            else if (Mp4Tags.VideoSampleDescriptions.Contains(h.Name))
            {
                padding = 78;
            }

            var container = new ContainerMp4Box(h.Name, position, h.HeaderSize, (long)h.Size - h.HeaderSize, padding);
            long start = position + h.HeaderSize + padding;
            long stop = position + (long)h.Size;
            container.Children.AddRange(LoadMultiple(input, start, stop));
            return container;
        }

        private static St3dMp4Box LoadSt3d(Stream input, long position, long end)
        {
            var h = ReadHeader(input, position, end);
            _ = BigEndian.ReadU32(input);
            byte mode = BigEndian.ReadU8(input);
            return new St3dMp4Box
            {
                Position = position,
                HeaderSize = h.HeaderSize,
                ContentSize = (long)h.Size - h.HeaderSize,
                Mode = mode
            };
        }

        private static PrhdMp4Box LoadPrhd(Stream input, long position, long end)
        {
            var h = ReadHeader(input, position, end);
            _ = BigEndian.ReadU32(input);
            return new PrhdMp4Box
            {
                Position = position,
                HeaderSize = h.HeaderSize,
                ContentSize = (long)h.Size - h.HeaderSize,
                Yaw = BigEndian.ReadU32(input),
                Pitch = BigEndian.ReadU32(input),
                Roll = BigEndian.ReadU32(input)
            };
        }

        private static EquiMp4Box LoadEqui(Stream input, long position, long end)
        {
            var h = ReadHeader(input, position, end);
            _ = BigEndian.ReadU32(input);
            return new EquiMp4Box
            {
                Position = position,
                HeaderSize = h.HeaderSize,
                ContentSize = (long)h.Size - h.HeaderSize,
                Top = BigEndian.ReadU32(input),
                Bottom = BigEndian.ReadU32(input),
                Left = BigEndian.ReadU32(input),
                Right = BigEndian.ReadU32(input)
            };
        }

        private static Sa3dMp4Box LoadSa3d(Stream input, long position, long end)
        {
            var h = ReadHeader(input, position, end);
            var box = new Sa3dMp4Box
            {
                Position = position,
                HeaderSize = h.HeaderSize,
                ContentSize = (long)h.Size - h.HeaderSize,
                Version = BigEndian.ReadU8(input)
            };
            byte ambisonicType = BigEndian.ReadU8(input);
            box.HeadLockedStereo = (ambisonicType & 0x80) != 0;
            box.AmbisonicType = (byte)(ambisonicType & 0x7F);
            box.AmbisonicOrder = BigEndian.ReadU32(input);
            box.ChannelOrdering = BigEndian.ReadU8(input);
            box.Normalization = BigEndian.ReadU8(input);
            box.NumChannels = BigEndian.ReadU32(input);
            for (int i = 0; i < box.NumChannels; i++) box.ChannelMap.Add(BigEndian.ReadU32(input));
            return box;
        }
    }
}
