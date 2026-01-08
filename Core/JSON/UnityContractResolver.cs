using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnitySerializationBridge.Utils;

namespace UnitySerializationBridge.Core.JSON;

internal class UnityContractResolver : DefaultContractResolver
{
    // Cache to know what properties to look for after the first lookup
    internal static ConditionalWeakTable<Type, IList<JsonProperty>> propsCache;
    private static readonly UniversalUnityReferenceConverter UnityReferenceConverter = new();
    private static readonly UniversalUnityReferenceConverter PassiveUnityReferenceConverter = new() { PassiveSerialization = true };
    private static readonly UniversalUnityValueConverter UnityValueConverter = new();

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        JsonProperty property = base.CreateProperty(member, memberSerialization);
        bool unityExclusive = property.PropertyType.IsUnityExclusive();


        // If it has [SerializeReference], try to serialize by reference
        if (property.AttributeProvider.HasAttribute<SerializeReference>())
        {
            if (unityExclusive)
            {
                if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[{property.PropertyName}] is using UnityReferenceConverter.");
                property.Converter = UnityReferenceConverter;
            }
            else
            {
                if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[{property.PropertyName}] is using standard Referencing.");
                property.IsReference = true;
                property.ItemIsReference = true;
            }
            return property;
        }

        // If the property is not Unity, skip this
        if (!unityExclusive)
            return property;

        // If the property is NOT contained in a Component, and knowing the property is an UnityEngine.Object, use passive referencing
        if (!typeof(Component).IsAssignableFrom(property.DeclaringType))
        {
            if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[{property.PropertyName}] is using (PASSIVE) UnityReferenceConverter.");
            property.Converter = PassiveUnityReferenceConverter;
            return property;
        }
        // Otherwise, assume that the property is holding a reference to an UnityEngine.Object that needs instantiation
        // If it's array, make the item converter use the values instead
        if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType))
        {
            property.ItemConverter = UnityValueConverter;
            if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[{property.PropertyName}] items are using UnityValueConverter.");
        }
        else
        {
            property.Converter = UnityValueConverter;
            if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[{property.PropertyName}] is using UnityValueConverter.");
        }


        return property;
    }

    protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
    {
        // Get standard properties
        var props = GetPropertiesFromCache(type, memberSerialization, out bool usedCache);
        if (usedCache) // If it used caching, all of these properties are already properly defined for the right use-case
            return props;

        // Prepare deduplication set
        var addedPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        int baseCount = props.Count;
        for (int i = 0; i < baseCount; i++)
            addedPropertyNames.Add(GetUniquePropertyName(props[i]));

        Type currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            var fields = AccessTools.GetDeclaredFields(currentType);

            // Make private fields public if possible
            foreach (var field in fields)
            {
                if (field.IsStatic || field.IsPublic) continue;

                // We only care about private fields marked for Unity serialization
                bool isSerializeField = field.IsDefined(typeof(SerializeField), false);
                bool isSerializeReference = field.IsDefined(typeof(SerializeReference), false);

                if (isSerializeField || isSerializeReference)
                {
                    // If this is a component and it is attempting to serialize another component, Unity can already do that; the serializer ignores this
                    if (field.DeclaringType.IsUnityComponentType() && field.FieldType.IsUnityComponentType())
                    {
                        if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[{field.Name}] field has been detected as serialized private and REMOVED!");
                        continue;
                    }
                    if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[{field.Name}] field has been detected as serialized private and INCLUDED!");

                    // Create property (this calls our overridden CreateProperty above)
                    JsonProperty jsonProp = CreateProperty(field, memberSerialization);

                    // Force visibility for private fields
                    jsonProp.Readable = true;
                    jsonProp.Writable = true;

                    if (addedPropertyNames.Add(GetUniquePropertyName(jsonProp)))
                    {
                        props.Add(jsonProp);
                    }
                }
            }
            currentType = currentType.BaseType;
        }

        return props;

        static string GetUniquePropertyName(JsonProperty prop) => $"{prop.DeclaringType.FullName}.{prop.PropertyName}";
    }


    // Private field helpers

    private IList<JsonProperty> GetPropertiesFromCache(Type type, MemberSerialization memberSerialization, out bool usedCache)
    {
        if (propsCache != null && propsCache.TryGetValue(type, out var props))
        {
            usedCache = true;
            return props;
        }

        props = base.CreateProperties(type, memberSerialization);

        // Filter out any property declared in a Unity assembly
        for (int i = props.Count - 1; i >= 0; i--)
        {
            // If this is a component and it's trying to serialize one, remove it from here
            if (props[i].DeclaringType.IsUnityComponentType() && !props[i].PropertyType.IsUnityComponentType())
            {
                if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[{props[i].PropertyName}] has been detected and REMOVED from the properties.");
                props.RemoveAt(i);
                continue;
            }
            if (BridgeManager.enableDebugLogs.Value) Debug.Log($"[{props[i].PropertyName}] has been INCLUDED.");
        }
        propsCache.Add(type, props);

        usedCache = false;
        return props;
    }
}