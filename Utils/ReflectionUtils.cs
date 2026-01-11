using System.Reflection;
using System;
using System.Linq.Expressions;
using HarmonyLib;
using System.Collections.Generic;
using UnitySerializationBridge.Core;
using System.Runtime.CompilerServices;
using System.Linq;

namespace UnitySerializationBridge.Utils;

internal static class ReflectionUtils
{
    // Caching system
    internal static LRUCache<FieldInfo, Func<object, object>> FieldInfoGetterCache;
    internal static LRUCache<FieldInfo, Action<object, object>> FieldInfoSetterCache;
    internal static LRUCache<string, Type> TypeNameCache;
    internal static LRUCache<string, Func<object, object>> ConstructorCache;
    internal static LRUCache<(Type, Type), Func<object>> GenericActivatorConstructorCache;
    internal static ConditionalWeakTable<Type, Func<object>> ParameterlessActivatorConstructorCache;
    internal static ConditionalWeakTable<Type, Func<int, Array>> ArrayActivatorConstructorCache;
    internal static ConditionalWeakTable<Type, Func<object, object>> SelfActivatorConstructorCache;
    internal static ConditionalWeakTable<Type, List<FieldInfo>> TypeToFieldsInfoCache;

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

    public static Func<object> GetGenericConstructor(this Type genericDefinition, Type elementType)
    {
        if (!genericDefinition.IsGenericTypeDefinition)
            throw new ArgumentException("Type must be a generic definition (e.g., List<>)");

        var typeTuple = (genericDefinition, elementType);

        if (GenericActivatorConstructorCache != null && GenericActivatorConstructorCache.TryGetValue(typeTuple, out var func)) return func;

        // Combine them: List<> + int = List<int>
        Type concreteType = genericDefinition.MakeGenericType(elementType);

        // Create the Expression: () => new List<T>()
        NewExpression newExp = Expression.New(concreteType);

        // Cast to object so the delegate is compatible with Func<object>
        UnaryExpression castExp = Expression.Convert(newExp, typeof(object));

        // Compile it into a reusable delegate
        func = Expression.Lambda<Func<object>>(castExp).Compile();
        GenericActivatorConstructorCache?.Add(typeTuple, func);

        return func;
    }

    public static Func<object, object> GetGenericWrapperConstructor(this Type genericWrapper, params Type[] genericParameters)
    {
        // Validate inputs
        if (genericWrapper == null)
            throw new ArgumentNullException(nameof(genericWrapper));

        if (genericParameters == null || genericParameters.Length == 0)
            throw new ArgumentException("Generic parameters cannot be null or empty", nameof(genericParameters));

        // Create a cache key based on the wrapper type and generic parameters
        string cacheKey = CreateCacheKey(genericWrapper, genericParameters);

        // Try to get cached constructor
        if (ConstructorCache != null && ConstructorCache.TryGetValue(cacheKey, out var cachedConstructor))
            return cachedConstructor;

        // Create closed generic type
        if (!genericWrapper.IsGenericTypeDefinition)
            throw new ArgumentException("Type must be a generic type definition", nameof(genericWrapper));

        Type closedGenericType;
        try
        {
            closedGenericType = genericWrapper.MakeGenericType(genericParameters);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException(
                $"Failed to create closed generic type from {genericWrapper.Name} with parameters {string.Join(", ", genericParameters.Select(t => t.Name))}",
                nameof(genericParameters), ex);
        }

        // Find the single-parameter constructor
        ConstructorInfo constructor;
        try
        {
            // Assuming the constructor takes one parameter which is the wrapped collection
            constructor = closedGenericType.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length == 1) ?? throw new InvalidOperationException(
                    $"No single-parameter constructor found for type {closedGenericType.Name}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to find constructor for {closedGenericType.Name}", ex);
        }

        // Create lambda expression: (object arg) => new T((TParam)arg)
        var parameter = Expression.Parameter(typeof(object), "arg");

        // Convert input object to constructor parameter type
        var parameterType = constructor.GetParameters()[0].ParameterType;
        var convertedArg = Expression.Convert(parameter, parameterType);

        // Create new expression
        var newExpression = Expression.New(constructor, convertedArg);

        // Convert result to object if it's a value type (boxing)
        Expression resultExpression = closedGenericType.IsValueType
            ? Expression.Convert(newExpression, typeof(object)) // Box the struct
            : newExpression;

        // Compile the lambda
        var lambda = Expression.Lambda<Func<object, object>>(
            resultExpression,
            parameter);

        var compiledLambda = lambda.Compile();

        ConstructorCache?.Add(cacheKey, compiledLambda);

        // Cache and return
        return compiledLambda;

        static string CreateCacheKey(Type genericWrapper, Type[] genericParameters)
        {
            // Create a unique key including assembly qualified name for the wrapper
            // and full names for all generic parameters
            var wrapperKey = genericWrapper.AssemblyQualifiedName;
            var paramsKey = string.Join("|",
                genericParameters.Select(p => p.AssemblyQualifiedName));

            return $"{wrapperKey}[{paramsKey}]";
        }
    }

    public static Func<int, Array> GetArrayConstructor(this Type elementType)
    {
        if (ArrayActivatorConstructorCache != null && ArrayActivatorConstructorCache.TryGetValue(elementType, out var func)) return func;
        // Create parameter expression for the array length
        ParameterExpression lengthParam = Expression.Parameter(typeof(int), "length");

        // Create new array expression: new T[length]
        NewArrayExpression newArrayExp = Expression.NewArrayBounds(elementType, lengthParam);

        // Cast the T[] as System.Array
        UnaryExpression castExp = Expression.TypeAs(newArrayExp, typeof(Array));

        // Compile the lambda: (int length) => (Array)new T[length]
        func = Expression.Lambda<Func<int, Array>>(castExp, lengthParam).Compile();
        ArrayActivatorConstructorCache?.Add(elementType, func);

        return func;
    }

    public static Func<object> GetParameterlessConstructor(this Type type)
    {
        if (ParameterlessActivatorConstructorCache != null && ParameterlessActivatorConstructorCache.TryGetValue(type, out var func)) return func;
        var parameterlessConstructor = type.GetConstructor(Type.EmptyTypes) ?? throw new InvalidCastException($"{type} does not contain a parameterless constructor.");

        // Create the Expression: () => new Type()
        NewExpression newExp = Expression.New(parameterlessConstructor);

        // Cast to object so the delegate is compatible with Func<object>
        UnaryExpression castExp = Expression.Convert(newExp, typeof(object));

        // Compile it into a reusable delegate
        func = Expression.Lambda<Func<object>>(castExp).Compile(); // () => (object)new Type();
        ParameterlessActivatorConstructorCache?.Add(type, func);
        return func;
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

    public static bool IsFieldABackingField(this FieldInfo field) => field.IsDefined(typeof(CompilerGeneratedAttribute), false) || field.Name.Contains("k__BackingField");
    public static bool IsFieldABackingField(this MemberInfo info) =>
        info is FieldInfo field && field.IsFieldABackingField();

}