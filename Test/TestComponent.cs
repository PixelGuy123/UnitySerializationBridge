
#if DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;
using UnitySerializationBridge.Interfaces;

namespace UnitySerializationBridge.Test;

internal class TestComponentToSerialize : MonoBehaviour, ISafeSerializationCallbackReceiver
{
    public static bool shallInstantiate = false;
    void Awake()
    {
        if (!shallInstantiate) return;

        instanceOfC = new(){
              myValue = "THISISACOOLC",
              myComponents = [this],
              myCompRef = this,
              referenceOfB = new B(){
              anotherValue = 99,
              value = "MyOwnValue",
              something = -99
          }
        };

        instancesOfC = [instanceOfC];
        arrayOfC = [instanceOfC];

        List<C> cs = [
          new(){
              myValue = "984194",
              myDValue = new(){
                  booleanValue = true
              }
          },
          new(){
              myValue = "dasdsaad",
              myDValue = new(){
                  booleanValue = true
              }
          }
        ];

        List<D> structs = [
            new() { booleanValue = true }
        ];

        instancesOfA = [
          new B(){
              anotherValue = 99,
              value = "MyOwnValue",
              something = -99,
              myListOfCs = cs,
              myArrayOfCs = cs.ToArray(),
              myStruct = new() { booleanValue = true },
              myStructArray = structs.ToArray(),
              myStructList = structs
          },
          new A(){
              value = "MyOwnValue",
              something = -99,
              myListOfCs = cs,
              myArrayOfCs = cs.ToArray(),
              myStruct = new() { booleanValue = true },
              myStructArray = structs.ToArray(),
              myStructList = structs
          }
        ];
    }
    public A[] instancesOfA; // Serializable
    [SerializeReference]
    public List<C> instancesOfC;
    [SerializeReference]
    public C[] arrayOfC;
    [SerializeReference]
    public C instanceOfC = null;
    public void OnBeforeSerialize() { }
    public void OnAfterDeserialize()
    {
        // Debug.Assert(instancesOfA != null);
        // if (instancesOfA == null) return;
        // for (int i = 0; i < instancesOfA.Length; i++)
        // {
        //     Debug.Log($"=====[{instancesOfA[i].GetType().Name}]=====");
        //     Debug.Log(instancesOfA[i].ToString());
        //     Debug.Log("======== END OF OBJECT ========");
        // }
    }
    public void OnAwake() { }
}

// Classes to simulate an mtm101balddevapi usual animator structure
[Serializable]
public class A
{
    [SerializeField]
    internal int something; // Private, but has attribute -> Should Serialize
    public string value;
    public List<C> myListOfCs;
    public C[] myArrayOfCs;
    public D myStruct;
    public D[] myStructArray;
    public List<D> myStructList;
    public override string ToString()
    {
        string data = "\n";
        data += "Something: " + something + '\n';
        data += "Value: " + value + '\n';
        data += "myListOfCs is null? " + (myListOfCs == null) + '\n';
        if (myListOfCs != null)
            foreach (var c in myListOfCs) data += $"{c?.ToString()}\n";
        data += "myArrayOfCs is null? " + (myArrayOfCs == null) + '\n';
        if (myArrayOfCs != null)
            foreach (var c in myArrayOfCs) data += $"{c?.ToString()}\n";
        data += "myStruct: " + myStruct.ToString() + '\n';
        data += "myStructArray is null? " + (myStructArray == null) + '\n';
        if (myStructArray != null)
            foreach (var c in myStructArray) data += $"{c.ToString()}\n";
        data += "myStructList is null? " + (myStructList == null);
        if (myStructList != null)
            foreach (var c in myStructList) data += $"{c.ToString()}\n";
        return data;
    }
}

[Serializable]
public class B : A
{
    [SerializeField]
    internal int anotherValue; // Private, in Derived class -> Should Serialize
    public override string ToString()
    {
        string data = "\n";
        data += "anotherValue: " + anotherValue + '\n';
        data += "Value: " + value + '\n';
        data += "myListOfCs is null? " + (myListOfCs == null) + '\n';
        data += "myArrayOfCs is null? " + (myArrayOfCs == null) + '\n';
        data += "myStruct: " + myStruct.ToString() + '\n';
        data += "myStructArray is null? " + (myStructArray == null) + '\n';
        data += "myStructList is null? " + (myStructList == null);
        return data;
    }
}

[Serializable]
public class C
{
    public D myDValue;
    public string myValue;
    [SerializeReference]
    public B referenceOfB = null;
    [SerializeField]
    [SerializeReference]
    internal TestComponentToSerialize myCompRef;
    [SerializeField]
    [SerializeReference]
    internal TestComponentToSerialize[] myComponents;
    public override string ToString()
    {
        string data = "\n";
        data += "myValue: " + myValue + '\n';
        data += "myDValue: " + myDValue.ToString() + '\n';
        return data;
    }
}

[Serializable]
public struct D
{
    public bool booleanValue;
    public override string ToString()
    {
        string data = "\n";
        data += "booleanValue: " + booleanValue + '\n';
        return data;
    }
}

#endif