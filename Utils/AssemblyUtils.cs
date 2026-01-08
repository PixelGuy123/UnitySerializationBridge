using System.Reflection;
using System.IO;
using UnitySerializationBridge.Core.Serialization;
using System.Runtime.CompilerServices;
using System;
using System.Collections;

namespace UnitySerializationBridge.Utils;

internal static class AssemblyUtils
{
    internal static ConditionalWeakTable<Assembly, StrongBox<bool>> TypeIsManagedCache;

    public static bool IsFromGameAssemblies(this Type type)
    {
        // Obviously the handler shouldn't be accounted at all
        if (type == typeof(SerializationHandler) || type == typeof(ComponentMap))
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

    public static bool IsUnityExclusive(this Type type, Type exceptionType = null)
    {
        Type objType = typeof(UnityEngine.Object);
        // Check the type itself
        if (objType.IsAssignableFrom(type) && (exceptionType == null || !exceptionType.IsAssignableFrom(type))) return true;

        // Check generic arguments for collections
        if (type.IsGenericType)
        {
            Type[] generics = type.GetGenericArguments();
            for (int i = 0; i < generics.Length; i++)
            {
                if (objType.IsAssignableFrom(generics[i]) && (exceptionType == null || !exceptionType.IsAssignableFrom(generics[i]))) return true;
            }
        }

        // Check array element types
        if (type.IsArray && objType.IsAssignableFrom(type.GetElementType()) && (exceptionType == null || !exceptionType.IsAssignableFrom(type.GetElementType())))
        {
            return true;
        }

        return false;
    }

    public static bool IsGameAssemblyType(this Type type)
    {
        // If the type is from System itself, then return false
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return false;

        // If these aren't classes, they are nothing
        if (!type.IsClass && !type.IsValueType) return false;

        // Check the type itself IF it is the type the one from assemblies; otherwise, go to collection check
        if (!typeof(IEnumerable).IsAssignableFrom(type) && type.IsFromGameAssemblies()) return true;

        // Check generic arguments for collections
        if (type.IsGenericType)
        {
            Type[] generics = type.GetGenericArguments();
            for (int i = 0; i < generics.Length; i++)
            {
                if (generics[i].IsFromGameAssemblies()) return true;
            }
        }

        // Check array element types
        if (type.IsArray && type.GetElementType().IsFromGameAssemblies())
        {
            return true;
        }

        return false;
    }

    public static bool IsUnityComponentType(this Type type)
    {
        var compType = typeof(UnityEngine.Component);
        // Check the type itself IF it is the type the one from assemblies; otherwise, go to collection check
        if (!typeof(IEnumerable).IsAssignableFrom(type) && compType.IsAssignableFrom(type)) return true;

        // Check generic arguments for collections
        if (type.IsGenericType)
        {
            Type[] generics = type.GetGenericArguments();
            for (int i = 0; i < generics.Length; i++)
            {
                if (compType.IsAssignableFrom(generics[i])) return true;
            }
        }

        // Check array element types
        if (type.IsArray && compType.IsAssignableFrom(type.GetElementType()))
        {
            return true;
        }

        return false;
    }
}