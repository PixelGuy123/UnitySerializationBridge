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
    internal static ConditionalWeakTable<Type, List<FieldInfo>> TypeToFieldsInfoCache;

    public static Func<object, object> CreateFieldGetter(this FieldInfo fieldInfo)
    {
        if (FieldInfoGetterCache.TryGetValue(fieldInfo, out var getter)) return getter;

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
        FieldInfoGetterCache.Add(fieldInfo, lambda);
        return lambda;
    }

    public static Action<object, object> CreateFieldSetter(this FieldInfo fieldInfo)
    {
        if (FieldInfoSetterCache.TryGetValue(fieldInfo, out var setter)) return setter;

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
        FieldInfoSetterCache.Add(fieldInfo, lambda);
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
}