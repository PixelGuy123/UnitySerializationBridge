using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnitySerializationBridge.Core.JSON;

namespace UnitySerializationBridge.Utils;

internal static class JsonUtils
{
    // Very crazy workaround here
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new UnityContractResolver(),
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        TypeNameHandling = TypeNameHandling.Auto, // VERY IMPORTANT FOR POLYMORPHISM
        PreserveReferencesHandling = PreserveReferencesHandling.None // Only use if the contract tells it should
    };
    private static readonly DefaultContractResolver DefaultResolver = new();
    internal static readonly UniversalUnityReferenceValueConverter UnityConverter = new();

    public static string ToJson(object obj)
    {
        if (obj == null)
            return "null";

        var indentation = BridgeManager.enableDebugLogs.Value ? Formatting.Indented : Formatting.None;
        return JsonConvert.SerializeObject(obj, obj.GetType(), indentation, Settings);
    }

    public static object FromJsonOverwrite(Type type, string json, Dictionary<UnityEngine.Object, UnityEngine.Object> parentToChildPairs)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return null;
        UnityConverter.UpdateComponentRegister(parentToChildPairs);
        return JsonConvert.DeserializeObject(json, type, Settings);
    }

    public static bool HasAttribute<T>(this IAttributeProvider provider, bool inherit = false) => provider.GetAttributes(inherit).HasOfType(typeof(T));
    public static void SerializeDefault(this JsonSerializer serializer, JsonWriter writer, object value)
    {
        var prevContract = serializer.ContractResolver;
        // Serializes using default contract
        serializer.ContractResolver = DefaultResolver;
        serializer.Serialize(writer, value);

        // Put back the old one
        serializer.ContractResolver = prevContract;
    }
    public static object DeSerializeDefault(this JsonSerializer serializer, JsonReader reader, Type objectType)
    {
        var prevContract = serializer.ContractResolver;
        // Serializes using default contract
        serializer.ContractResolver = DefaultResolver;
        var obj = serializer.Deserialize(reader, objectType);

        // Put back the old one
        serializer.ContractResolver = prevContract;
        return obj;
    }

    public static object ToNativeObject(this JToken jo, Type type, JsonSerializer serializer)
    {
        var prevContract = serializer.ContractResolver;

        // ToObject with default contract
        serializer.ContractResolver = DefaultResolver;
        object obj = jo.ToObject(type, serializer);

        // Put back the old one
        serializer.ContractResolver = prevContract;
        return obj;
    }
}