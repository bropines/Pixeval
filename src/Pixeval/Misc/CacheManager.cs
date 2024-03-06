using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Tenray.ZoneTree;
using Tenray.ZoneTree.PresetTypes;
using Tenray.ZoneTree.Serializers;
using Windows.Storage;
using MemoryPack;
using PininSharp.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixeval.Misc
{
    public class CacheManager
    {
        private readonly string _dataDirectory = Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Local", "Pixeval", "cache");
        private readonly IZoneTree<string, CachedTTLValue> _zoneTree;

        public CacheManager()
        {
            var factory = new ZoneTreeFactory<string, CachedTTLValue>()
                .SetValueSerializer(new Serializer())
                .SetIsValueDeletedDelegate((in CachedTTLValue x) => x.IsExpired)
                .SetMarkValueDeletedDelegate(void (ref CachedTTLValue x) => x.Expire())
                .SetDataDirectory(_dataDirectory);
            _zoneTree = factory.OpenOrCreate();
        }

        public void Add(string key, Image<Bgra32> data, TimeSpan expiration)
        {
            _zoneTree.Upsert(key, new CachedTTLValue(data, DateTimeOffset.Now + expiration));
        }

        public bool TryGet(string key, [MaybeNullWhen(false)] out Image<Bgra32> data)
        {
            if (_zoneTree.TryGet(key, out var ttlValue))
            {
                data = ttlValue.Image;
                return true;
            }
            data = null;
            return false;

        }

        public bool ContainsKey(string key)
        {
            return _zoneTree.ContainsKey(key);
        }



        internal class Serializer : ISerializer<CachedTTLValue>
        {
            public CachedTTLValue Deserialize(byte[] bytes)
            {
                return MemoryPackSerializer.Deserialize<CachedTTLValue>(bytes);
            }

            public byte[] Serialize(in CachedTTLValue entry)
            {
                return MemoryPackSerializer.Serialize(entry);
            }
        }
    }

    [MemoryPackable]
    internal partial struct CachedTTLValue
    {
        [MemoryPackAllowSerialize]
        [MemoryPackOrder(0)]
        public Image<Bgra32> Image;

        [MemoryPackOrder(1)]
        public DateTimeOffset Expiration;

        public CachedTTLValue(Image<Bgra32> image, DateTimeOffset expiration)
        {
            Image = image;
            Expiration = expiration;
        }

        public bool IsExpired => DateTime.UtcNow >= Expiration;

        public void Expire()
        {
            Expiration = new DateTime();
        }
    }

    internal class ImageFormatterAttribute : MemoryPackCustomFormatterAttribute<Image<Bgra32>>
    {
        private static readonly ImageFormatter _formatter = new ImageFormatter();
        public override IMemoryPackFormatter<Image<Bgra32>> GetFormatter() => _formatter;

        internal class ImageFormatter : IMemoryPackFormatter<Image<Bgra32>>
        {
            public void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer,
                scoped ref Image<Bgra32>? value) where TBufferWriter : IBufferWriter<byte>
            {
                if (value is null)
                {
                    return;
                }
                writer.WriteVarInt(value.Width);
                writer.WriteVarInt(value.Height);
                var owner = MemoryPool<byte>.Shared.Rent(value.Width * value.Height * 4);
                value.CopyPixelDataTo(owner.Memory.Span);
                writer.WriteSpan(owner.Memory.Span);
                owner.Dispose();
            }

            public void Deserialize(ref MemoryPackReader reader, scoped ref Image<Bgra32>? value)
            {
                var width = reader.ReadVarIntInt32();
                var height = reader.ReadVarIntInt32();
                var owner = MemoryPool<byte>.Shared.Rent(width * height * 4);
                var ownerMemorySpan = owner.Memory.Span;
                reader.ReadSpan(ref ownerMemorySpan);
                var image = Image.LoadPixelData<Bgra32>(ownerMemorySpan, width, height);
                owner.Dispose();
                value = ref image;
            }
        }
    }
}
