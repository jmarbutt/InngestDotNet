using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inngest.JsonConverters;

/// <summary>
/// Converter for handling cases where a string (especially empty string) needs to be converted to a Dictionary
/// </summary>
public class StringToDictionaryConverter : JsonConverter<Dictionary<string, string>?>
{
    /// <summary>
    /// Reads JSON that may be an object, string, or null into a string dictionary.
    /// </summary>
    public override Dictionary<string, string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // If we get a string (especially empty string), return an empty dictionary
            return new Dictionary<string, string>();
        }
        else if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Normal case, read the dictionary as usual
            var dictionary = new Dictionary<string, string>();
            
            // Read the opening {
            reader.Read();
            
            // Read until the closing }
            while (reader.TokenType != JsonTokenType.EndObject)
            {
                // Read key
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token");
                }
                
                var key = reader.GetString() ?? string.Empty;
                
                // Read value
                reader.Read();
                var value = reader.GetString() ?? string.Empty;
                
                dictionary[key] = value;
                
                // Move to next property or end object
                reader.Read();
            }
            
            return dictionary;
        }
        
        throw new JsonException($"Cannot convert {reader.TokenType} to Dictionary<string, string>");
    }

    /// <summary>
    /// Writes the dictionary as a JSON object, or null when the value is null.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, Dictionary<string, string>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }
        
        writer.WriteStartObject();
        foreach (var kvp in value)
        {
            writer.WriteString(kvp.Key, kvp.Value);
        }
        writer.WriteEndObject();
    }
}
