using Shared.Models.Events;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Models.Templates
{
    public struct StreamQualityTpl
    {
        public List<StreamQualityDto> data { get; private set; } = new List<StreamQualityDto>(8);


        public StreamQualityTpl() { }

        public StreamQualityTpl(IEnumerable<(string link, string quality)> streams) 
        {
            if (streams != null)
            {
                foreach (var item in streams)
                    Append(item.link, item.quality);
            }
        }

        public StreamQualityTpl(IReadOnlyList<StreamQualityDto> streams)
        {
            if (streams != null)
            {
                foreach (var item in streams)
                    Append(item.link, item.quality);
            }
        }
				void EnsureData()
        {
            data ??= new List<StreamQualityDto>(8);
        }

        public bool Any() => data != null && data.Any();

        public void Append(string link, string quality)
        {
            if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(quality))
                return;
			
			EnsureData();

            if (InvkEvent.IsStreamQuality())
            {
                var eventResult = InvkEvent.StreamQuality(new EventStreamQuality(link, quality, prepend: false));
                if (eventResult.next.HasValue && !eventResult.next.Value)
                    return;

                link = eventResult.link ?? link;
            }

            if (!string.IsNullOrEmpty(link))
                data.Add(new StreamQualityDto(link, quality));
        }

        public void Insert(string link, string quality)
        {
            if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(quality))
                return;
			
			EnsureData();

            if (InvkEvent.IsStreamQuality())
            {
                var eventResult = InvkEvent.StreamQuality(new EventStreamQuality(link, quality, prepend: true));
                if (eventResult.next.HasValue && !eventResult.next.Value)
                    return;

                link = eventResult.link ?? link;
            }

            if (!string.IsNullOrEmpty(link))
                data.Insert(0, new StreamQualityDto(link, quality));
        }

        public string ToJson() => JsonSerializer.Serialize(ToObject(), StreamQualityJsonContext.Default.DictionaryStringString);

        public Dictionary<string, string> ToObject(bool emptyToNull = false)
        {
			EnsureData();
			
            if (emptyToNull && data.Count == 0)
                return null;

            var result = new Dictionary<string, string>(data.Count);

            foreach (var item in data)
                result.TryAdd(item.quality, item.link);

            return result;
        }

        public string MaxQuality()
        {
			EnsureData();
			
            if (data.Count == 0)
                return string.Empty;

            return data[0].quality;
        }

        public StreamQualityDto Firts()
        {
			EnsureData();
			
            if (data.Count == 0)
                return default;

            if (InvkEvent.IsStreamQualityFirts())
            {
                var eventResult = InvkEvent.StreamQualityFirts(new EventStreamQualityFirts(data));
                return eventResult ?? data[0];
            }

            return data[0];
        }
    }


    [JsonSourceGenerationOptions(
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    )]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    public partial class StreamQualityJsonContext : JsonSerializerContext
    {
    }

    public readonly struct StreamQualityDto
    {
        public string link { get; }
        public string quality { get; }

        public StreamQualityDto(string link, string quality)
        {
            this.link = link;
            this.quality = quality;
        }
    }
}
