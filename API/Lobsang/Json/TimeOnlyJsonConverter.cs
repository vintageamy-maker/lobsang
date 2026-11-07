using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lobsang.Json
{
    /// <summary>
    /// TimeOnly 的寬鬆轉換：讀取時接受 "HH:mm" 與 "HH:mm:ss"（.NET 預設只收後者），
    /// 輸出一律 "HH:mm"。避免「GET 回 HH:mm，原樣 PUT 回來卻被擋」的讀寫格式不對稱。
    /// </summary>
    public class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
    {
        private static readonly string[] Formats = ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"];

        public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text)) return default;
            if (TimeOnly.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                return exact;
            return TimeOnly.Parse(text, CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    /// <summary>可為 null 的 TimeOnly（Nullable 需單獨註冊，不會沿用上面的轉換器）</summary>
    public class NullableTimeOnlyJsonConverter : JsonConverter<TimeOnly?>
    {
        private static readonly TimeOnlyJsonConverter Inner = new();

        public override TimeOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            return Inner.Read(ref reader, typeof(TimeOnly), options);
        }

        public override void Write(Utf8JsonWriter writer, TimeOnly? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteStringValue(value.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
        }
    }
}
