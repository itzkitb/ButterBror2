using System.Text.Json;
using System.Text.Json.Serialization;

namespace ButterBror.Data;

public class SafeObjectConverter : JsonConverter<object>
{
    public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        if (value is string || value.GetType().IsPrimitive)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
            return;
        }

        try
        {
            var cloneOptions = new JsonSerializerOptions(options);
            
            for (var i = cloneOptions.Converters.Count - 1; i >= 0; i--)
            {
                if (cloneOptions.Converters[i] is SafeObjectConverter)
                {
                    cloneOptions.Converters.RemoveAt(i);
                }
            }
            
            using var doc = JsonSerializer.SerializeToDocument(value, value.GetType(), cloneOptions);
            doc.WriteTo(writer);
        }
        catch (Exception ex)
        {
            writer.WriteStringValue($"[unserializable: {value.GetType().Name}. error: {ex.Message}]");
        }
    }
}