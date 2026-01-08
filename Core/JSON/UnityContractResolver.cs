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
    private static readonly UniversalUnityValueConverter UnityValueConverter = new();

    protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
    {
        JsonProperty property = base.CreateProperty(member, memberSerialization);
        bool unityType = property.DeclaringType.IsUnityExclusive(typeof(Component));

        if (unityType)
        {
            // Check if the field or property has [SerializeReference]
            if (property.DeclaringType.IsDefined(typeof(SerializeReference)))
            {
                property.Converter = UnityReferenceConverter;
                return property;
            }

            // If it's array, make the item converter use the values instead
            if (typeof(IEnumerable).IsAssignableFrom(property.DeclaringType))
                property.ItemConverter = UnityValueConverter;
            else
                property.Converter = UnityValueConverter;
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
            var fields = currentType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            foreach (var field in fields)
            {
                // We only care about private fields marked for Unity serialization
                bool isSerializeField = field.IsDefined(typeof(SerializeField), false);
                bool isSerializeReference = field.IsDefined(typeof(SerializeReference), false);

                if (isSerializeField || isSerializeReference)
                {
                    // Skip if it's a raw Unity engine type we don't want to touch
                    if (field.FieldType.IsUnityInternalType()) continue;

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
            if (props[i].DeclaringType.IsUnityInternalType() || props[i].PropertyType.IsUnityInternalType())
            {
                props.RemoveAt(i);
            }
        }
        propsCache.Add(type, props);

        usedCache = false;
        return props;
    }
}