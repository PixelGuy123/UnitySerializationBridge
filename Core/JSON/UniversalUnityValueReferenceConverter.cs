using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using BepInSoft.Utils;
using Object = UnityEngine.Object;

namespace BepInSoft.Core.JSON;

internal class UniversalUnityReferenceValueConverter : JsonConverter
{
    private const string HashKey = "$hash";
    private const string TypeKey = "$type";
    private const string DictionaryKeys = "$keys";
    private const string DictionaryValues = "$values";
    private const string DefaultMarker = "$default";

    internal Dictionary<Object, Object> parentToChildPairs;
    public void UpdateComponentRegister(Dictionary<Object, Object> parentToChildPairs) =>
        this.parentToChildPairs = parentToChildPairs.Count == 0 ? null : parentToChildPairs;

    public override bool CanConvert(Type objectType)
    {
        // Don't convert primitives or strings
        if (objectType == typeof(string) || objectType.IsPrimitive) return false;

        // Convert Unity Objects, Collections, or Dictionaries
        return typeof(Object).IsAssignableFrom(objectType) ||
               objectType.IsStandardCollection(includeDictionaries: true);
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        // Handle Unity Objects (References)
        if (value is Object unityObj)
        {
            if (!unityObj) { writer.WriteNull(); return; }
            writer.WriteStartObject();
            writer.WritePropertyName(HashKey);
            writer.WriteValue(unityObj.GetInstanceID());
            writer.WriteEndObject();
            return;
        }

        var type = value.GetType();

        // Handle Dictionaries (Custom format for complex keys)
        if (value is IDictionary dict)
        {
            writer.WriteStartArray();
            foreach (DictionaryEntry entry in dict)
            {
                writer.WriteStartObject();
                writer.WritePropertyName(DictionaryKeys);
                WriteJson(writer, entry.Key, serializer);
                writer.WritePropertyName(DictionaryValues);
                WriteJson(writer, entry.Value, serializer);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            return;
        }

        // Handle Standard Collections
        if (type.IsStandardCollection() && value is IEnumerable enumerable)
        {
            writer.WriteStartArray();
            foreach (var item in enumerable)
                WriteJson(writer, item, serializer);
            writer.WriteEndArray();
            return;
        }

        // Handle Abstract / Regular Objects
        // Use a marker to avoid infinite recursion when calling Serialize on itself
        writer.WriteStartObject();
        writer.WritePropertyName(TypeKey);
        writer.WriteValue(type.AssemblyQualifiedName);
        writer.WritePropertyName(DefaultMarker);
        if (!type.IsFromGameAssemblies() && (type.IsClass || type.IsValueType))
        {
            // Use this, so that the contract resolver is called again to resolve the properties
            serializer.Serialize(writer, value);
        }
        else
            serializer.SerializeDefault(writer, value);
        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;

        JToken token = JToken.Load(reader);
        return ProcessToken(token, objectType, serializer, existingValue);
    }

    private object ProcessToken(JToken token, Type targetType, JsonSerializer serializer, object existingValue = null)
    {
        if (token == null || token.Type == JTokenType.Null) return null;

        // Handle Unity References ($hash)
        if (token is JObject joHash && joHash.TryGetValue(HashKey, out var hashToken))
        {
            int instanceId = hashToken.Value<int>();
            var unityObject = Object.FindObjectFromInstanceID(instanceId);

            if (!unityObject) return null;
            var unityType = unityObject.GetType();
            // Component Mapping 
            if (unityType.IsUnityComponentType())
            {
                if (parentToChildPairs.TryGetValue(unityObject, out var child)) return child;
                return unityObject;
            }

            // ScriptableObjects / Assets (Cloning)
            if (unityType.TryGetSelfActivator(out var constructor))
                return constructor(unityObject);
            return Object.Instantiate(unityObject);
        }

        // Handle Dictionaries ($keys / $values)
        if (token is JArray jArray && typeof(IDictionary).IsAssignableFrom(targetType))
        {
            var args = targetType.GetGenericArguments();
            var keyType = args[0];
            var valType = args[1];
            var dict = (IDictionary)targetType.GetParameterlessConstructor()();

            foreach (var item in jArray)
            {
                var k = ProcessToken(item[DictionaryKeys], keyType, serializer);
                var v = ProcessToken(item[DictionaryValues], valType, serializer);
                if (k != null) dict.Add(k, v);
            }
            return dict;
        }

        // Handle Lists/Arrays
        if (token is JArray listArray)
        {
            var elementType = targetType.IsArray ? targetType.GetElementType() : targetType.GetGenericArguments()[0];
            var results = (IList)typeof(List<>).GetGenericConstructor(elementType)();
            foreach (var child in listArray)
                results.Add(ProcessToken(child, elementType, serializer));

            if (targetType.IsArray)
            {
                var arr = elementType.GetArrayConstructor()(results.Count);
                for (int i = 0; i < arr.Length; i++) arr.SetValue(results[i], i);
                return arr;
            }
            return results;
        }

        // Handle Objects and Abstract Types
        if (token is JObject jo)
        {
            // Determine concrete type if polymorphic
            Type actualType = targetType;
            if (jo.TryGetValue(TypeKey, out var typeToken))
            {
                actualType = ReflectionUtils.GetFastType(typeToken.Value<string>()) ?? targetType;
            }

            // Extract content from marker if present
            if (jo.TryGetValue(DefaultMarker, out var internalContent))
            {
                return internalContent.ToObject(actualType, serializer);
            }

            // Standard fallback
            return jo.ToObject(actualType, serializer);
        }

        return token.ToObject(targetType);
    }
}