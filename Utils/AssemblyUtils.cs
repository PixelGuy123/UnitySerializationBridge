using System.Reflection;
using System.IO;
using UnitySerializationBridge.Core.Serialization;
using System.Runtime.CompilerServices;
using System;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnitySerializationBridge.Core;

namespace UnitySerializationBridge.Utils;

internal static class AssemblyUtils
{
    internal static ConditionalWeakTable<Assembly, StrongBox<bool>> TypeIsManagedCache;
    internal static ConditionalWeakTable<Assembly, StrongBox<bool>> TypeIsUnityManagedCache;
    internal static LRUCache<(Type, int), List<Type>> CollectionNestedElementTypesCache;

    public static bool IsFromGameAssemblies(this Type type)
    {
        // Obviously the handler shouldn't be accounted at all
        if (type == typeof(SerializationHandler))
            return true;

        var assembly = type.Assembly;

        if (typeof(BepInEx.BaseUnityPlugin).IsAssignableFrom(type)) // Never a plugin
            return true;

        return assembly.IsGameAssembly();
    }

    public static bool IsGameAssembly(this Assembly assembly)
    {
        // if the assembly is already known, return the value
        if (TypeIsManagedCache.TryGetValue(assembly, out var box))
            return box.Value;

        // Cache the managed part if it is not detected
        bool isManaged = assembly.Location.EndsWith($"Managed{Path.DirectorySeparatorChar}{assembly.GetName().Name}.dll");
        TypeIsManagedCache.Add(assembly, new(isManaged));

        return isManaged;
    }

    public static bool IsUnityAssembly(this Assembly assembly)
    {
        // if the assembly is already known, return the value
        if (TypeIsUnityManagedCache.TryGetValue(assembly, out var box))
            return box.Value;

        // Cache the managed part if it is not detected
        bool isManaged = assembly.Location.EndsWith($"Managed{Path.DirectorySeparatorChar}{assembly.GetName().Name}.dll") && Path.GetFileName(assembly.Location).StartsWith("Unity");
        TypeIsUnityManagedCache.Add(assembly, new(isManaged));

        return isManaged;
    }

    public static bool CanUnitySerialize(this Type type)
    {
        // First, what it CAN serialize by default
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return true;
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return true;

        // What Unity CAN'T serialize first before checking the other types
        if (!type.IsStandardCollection()) return false;

        // Exactly what the plugin aims to fix: classes and value types that aren't from the assembly not being serialized
        if ((type.IsClass || type.IsValueType) && !type.IsFromGameAssemblies()) return false;

        // Assumes it's a standard collection that Unity could serialize
        var elementTypes = type.GetTypesFromArray();
        return elementTypes.Exists(typeof(UnityEngine.Object).IsAssignableFrom);
    }

    public static bool IsUnityComponentType(this Type type)
    {
        var elementTypes = type.GetTypesFromArray();
        return elementTypes.Contains(typeof(GameObject)) || elementTypes.Exists(typeof(Component).IsAssignableFrom);
    }

    // Expect the most basic collection types to be checked, not IEnumerable in general
    public static bool IsStandardCollection(this Type t, bool includeDictionaries = false) =>
    t.IsArray ||
    typeof(IList).IsAssignableFrom(t) ||
    (includeDictionaries && typeof(IDictionary).IsAssignableFrom(t));

    public static List<Type> GetTypesFromArray(this Type collectionType, int layersToCheck = -1)
    {
        // Normalize the minimum layer boundaries
        if (layersToCheck <= 0)
            layersToCheck = -1;

        var typeDepthTuple = (collectionType, layersToCheck);
        if (CollectionNestedElementTypesCache.TryGetValue(typeDepthTuple, out var results)) return results;

        // Initial capacity guess to reduce resizing
        results = [];
        GetTypesFromArrayInternal(collectionType, layersToCheck, 0, results);

        // Cache
        CollectionNestedElementTypesCache.Add(typeDepthTuple, results);
        return results;
    }

    private static void GetTypesFromArrayInternal(this Type collectionType, int layersToCheck, int currentLayer, List<Type> results)
    {
        // Depth Guard
        if (layersToCheck > 0 && currentLayer >= layersToCheck)
        {
            results.Add(collectionType);
            return;
        }

        // Is it a collection?
        if (!collectionType.IsStandardCollection(includeDictionaries: true))
        {
            results.Add(collectionType);
            return;
        }

        // Handle Generics (List<T>, Dictionary<TKey, TValue>)
        if (collectionType.IsGenericType)
        {
            Type[] genericArguments = collectionType.GetGenericArguments();
            for (int i = 0; i < genericArguments.Length; i++)
            {
                GetTypesFromArrayInternal(genericArguments[i], layersToCheck, currentLayer + 1, results);
            }
            return;
        }

        // Handle Arrays
        if (collectionType.IsArray)
        {
            Type elementType = collectionType.GetElementType();
            if (elementType != null)
            {
                GetTypesFromArrayInternal(elementType, layersToCheck, currentLayer + 1, results);
            }
            return;
        }

        results.Add(collectionType);
    }
}