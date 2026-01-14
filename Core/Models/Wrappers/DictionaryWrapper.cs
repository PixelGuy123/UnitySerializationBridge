using System.Collections.Generic;

namespace BepInSoft.Core.Models.Wrappers;

internal struct DictionaryWrapper<TKey, TValue>(Dictionary<TKey, TValue> dic) : ICollectionWrapper
{
    public Dictionary<TKey, TValue> Dictionary = dic;
    public object Unwrap() => Dictionary;
}