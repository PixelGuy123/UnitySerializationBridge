using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using Object = UnityEngine.Object;

namespace UnitySerializationBridge.Core.JSON;

internal class UniversalUnityValueConverter : JsonConverter
{
    const string unityId = "unity", objectHashRef = "$hash";

    // Don't try to wrap primitives, strings, or enums in reference containers
    public override bool CanConvert(Type objectType) => typeof(Object).IsAssignableFrom(objectType);

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }

        JObject jo = [];

        if (value is Object unityObj)
        {
            // UNITY POINTER LOGIC
            jo.Add("$type", unityId);
            jo.Add(objectHashRef, unityObj.GetInstanceID());
        }
        jo.WriteTo(writer);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;

        JObject jo = JObject.Load(reader);
        string type = jo["$type"]?.ToString();

        // Safety check for malformed JSON or unexpected objects
        if (string.IsNullOrEmpty(type) || !jo.ContainsKey(objectHashRef)) return null;

        var id = jo[objectHashRef].ToObject<int>();

        var prefabObject = Object.FindObjectFromInstanceID(id);
        if (!prefabObject) return null;

        // Basically get the opportunity to instantiate the object
        return Object.Instantiate(prefabObject);
    }
}