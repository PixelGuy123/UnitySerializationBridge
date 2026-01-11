using System;
using System.Collections.Generic;

namespace UnitySerializationBridge.Utils;

internal static class CollectionUtils
{
    public static bool HasOfType<T>(this IList<T> list, Type t)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].GetType() == t)
                return true;
        }
        return false;
    }
}