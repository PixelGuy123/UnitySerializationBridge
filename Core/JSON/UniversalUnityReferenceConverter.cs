using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;
using UnitySerializationBridge.Patches.Serialization;
using UnitySerializationBridge.Utils;
using Object = UnityEngine.Object;

namespace UnitySerializationBridge.Core.JSON;

internal class UniversalUnityReferenceConverter : JsonConverter
{
    const string componentRef = "$componentId", gameObjectRef = "$gameObjectId", objectHashRef = "$referenceId";
    /// <summary>
    /// If True, and if the serialized <see cref="Object"/> is a <see cref="Component"/>, the converter will attempt to get the current component holding this class.
    /// </summary>
    public bool PassiveSerialization { get; set; } = false;

    // Don't try to wrap primitives, strings, or enums in reference containers
    public override bool CanConvert(Type objectType) => PassiveSerialization ?
    (typeof(GameObject) == objectType || typeof(Component).IsAssignableFrom(objectType)) :
    typeof(Object).IsAssignableFrom(objectType);
    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }

        JObject jo = [];

        if (value is Object unityObj)
        {
            // UNITY POINTER
            if (PassiveSerialization)
            {
                switch (unityObj)
                {
                    case Component:
                        jo.Add(componentRef, unityObj.GetInstanceID());
                        break;
                    case GameObject:
                        jo.Add(gameObjectRef, unityObj.GetInstanceID());
                        break;
                }
            }
            else
            {
                jo.Add(objectHashRef, unityObj.GetInstanceID());
            }
        }
        jo.WriteTo(writer);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;

        JObject jo = JObject.Load(reader);

        // Safety check for malformed JSON or unexpected objects
        if (jo.ContainsKey(objectHashRef))
        {
            var id = jo[objectHashRef].ToObject<int>();
            if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[Converter] Captured Reference ID ({id}) to be identified.");
            return Object.FindObjectFromInstanceID(id);
        }
        // Component Handling
        else if (jo.ContainsKey(componentRef))
        {
            var id = jo[componentRef].ToObject<int>();
            if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[Converter] Captured Component ID ({id}) to be identified.");

            // Try to get the Component's last child
            var component = Object.FindObjectFromInstanceID(id) as Component;
            if (component)
                return ComponentMapper.GetLastChildFromHierarchy(component);
        }
        // GameObject Handling
        else if (jo.ContainsKey(gameObjectRef))
        {
            var id = jo[gameObjectRef].ToObject<int>();
            if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[Converter] Captured GameObject ID ({id}) to be identified.");

            // Try to get the GameObject's last child
            var go = Object.FindObjectFromInstanceID(id) as GameObject;
            if (go)
                return GameObjectMapper.GetLastChild(go);
        }
        return null;
    }
}