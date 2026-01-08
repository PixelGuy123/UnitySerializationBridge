using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnitySerializationBridge.Core.JSON;

namespace UnitySerializationBridge.Utils;

internal static class JsonUtils
{
    // Cache the converter to avoid allocation per contract
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new UnityContractResolver(),
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        TypeNameHandling = TypeNameHandling.Auto, // VERY IMPORTANT FOR POLYMORPHISM
        PreserveReferencesHandling = PreserveReferencesHandling.None // Don't work by default, so don't use them
    };
    public static (string json, bool isReference) ToJson(object obj, FieldInfo info)
    {
        if (obj == null)
            return ("null", false);

        if (info.IsDefined(typeof(UnityEngine.SerializeReference)))
            return (string.Empty, true);

        return (JsonConvert.SerializeObject(obj, obj.GetType(), Settings), false);
    }

    public static object FromJsonOverwrite(Type type, string json)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return null;
        return JsonConvert.DeserializeObject(json, type, Settings);
    }

    public static bool HasAttribute<T>(this IAttributeProvider provider, bool inherit = false) => provider.GetAttributes(inherit).HasOfType(typeof(T));
}