using System.Text.Json;
using System.Text.Json.Serialization;

namespace ButterBror.Data;

public class SafeObjectConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return JsonSerializer.Deserialize(ref reader, typeToConvert, options);
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
            cloneOptions.Converters.Remove(this); 
            
            JsonSerializer.Serialize(writer, value, value.GetType(), cloneOptions);
        }
        catch (Exception ex)
        {
            writer.WriteStringValue($"[Unserializable: {value.GetType().Name}. Error: {ex.Message}]");
        }
    }
}