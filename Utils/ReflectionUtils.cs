using System.Reflection;
using System;
using System.Linq.Expressions;
using HarmonyLib;
using System.Collections.Generic;
using UnitySerializationBridge.Core;
using System.Runtime.CompilerServices;

namespace UnitySerializationBridge.Utils;

internal static class ReflectionUtils
{
    // Caching system
    internal static LRUCache<FieldInfo, Func<object, object>> FieldInfoGetterCache;
    internal static LRUCache<FieldInfo, Action<object, object>> FieldInfoSetterCache;
    internal static ConditionalWeakTable<Type, Func<object, object>> SelfActivatorConstructorCache;
    internal static ConditionalWeakTable<Type, List<FieldInfo>> TypeToFieldsInfoCache;
    internal static LRUCache<string, Type> TypeNameCache;

    public static Func<object, object> CreateFieldGetter(this FieldInfo fieldInfo)
    {
        if (FieldInfoGetterCache != null && FieldInfoGetterCache.TryGetValue(fieldInfo, out var getter)) return getter;

        var instanceParam = Expression.Parameter(typeof(object), "instance");

        // Convert instance to the declaring type
        var typedInstance = Expression.Convert(instanceParam, fieldInfo.DeclaringType);

        // Access the field
        var fieldExp = Expression.Field(typedInstance, fieldInfo);

        // Convert result to object if needed
        var resultExp = fieldInfo.FieldType.IsValueType ?
            Expression.Convert(fieldExp, typeof(object)) :
            (Expression)fieldExp;

        var lambda = Expression.Lambda<Func<object, object>>(resultExp, instanceParam).Compile();
        FieldInfoGetterCache?.Add(fieldInfo, lambda);
        return lambda;
    }

    public static Action<object, object> CreateFieldSetter(this FieldInfo fieldInfo)
    {
        if (FieldInfoSetterCache != null && FieldInfoSetterCache.TryGetValue(fieldInfo, out var setter)) return setter;

        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var valueParam = Expression.Parameter(typeof(object), "value");

        // Convert instance to the declaring type
        var typedInstance = Expression.Convert(instanceParam, fieldInfo.DeclaringType);
        var typedValue = Expression.Convert(valueParam, fieldInfo.FieldType);

        // (Assign) Set the field
        var assignExp = Expression.Assign(
            Expression.Field(typedInstance, fieldInfo),
            typedValue
        );

        var lambda = Expression.Lambda<Action<object, object>>(assignExp, instanceParam, valueParam).Compile();
        FieldInfoSetterCache?.Add(fieldInfo, lambda);
        return lambda;
    }

    public static List<FieldInfo> GetFieldsInfo(this Type type)
    {
        // If no cache, do expensive part
        if (TypeToFieldsInfoCache == null)
            return AccessTools.GetDeclaredFields(type);

        if (TypeToFieldsInfoCache.TryGetValue(type, out var fieldsInfo)) return fieldsInfo;
        fieldsInfo = AccessTools.GetDeclaredFields(type);
        TypeToFieldsInfoCache.Add(type, fieldsInfo);
        return fieldsInfo;
    }

    // There are some Unity components that have their own constructor for duplication (new Material(Material))
    public static bool TryGetSelfActivator(this Type type, out Func<object, object> func)
    {
        if (SelfActivatorConstructorCache != null && SelfActivatorConstructorCache.TryGetValue(type, out func)) return true;

        var selfConstructor = type.GetConstructor([type]); // Get a constructor that is itself
        if (selfConstructor == null)
        {
            func = null;
            return false;
        }

        // Get the parameter as object
        var parameter = Expression.Parameter(typeof(object), "self"); // (object self) => { }
        // Cast the parameter as desired type
        var typedParameter = Expression.Convert(parameter, type); // (object self) => { (Material)self }
        // Put this parameter to be used inside the constructor
        var newExpression = Expression.New(selfConstructor, typedParameter); // (object self) => new Material((Material)self);
        // Compile expression
        func = Expression.Lambda<Func<object, object>>(newExpression, parameter).Compile();
        SelfActivatorConstructorCache?.Add(type, func);
        return true;
    }

    public static Type GetFastType(string compName)
    {
        // Expensive lookup if no cache available
        if (TypeNameCache == null)
            return Type.GetType(compName);

        // Fast Type Lookup
        if (!TypeNameCache.TryGetValue(compName, out Type compType))
        {
            compType = Type.GetType(compName);
            if (compType != null) TypeNameCache.Add(compName, compType);
        }
        return compType;
    }
}