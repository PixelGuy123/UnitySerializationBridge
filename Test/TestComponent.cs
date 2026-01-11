#if DEBUG

using System;
using System.Collections.Generic;
using UnityEngine;


// Edge Case: Enums and Flags
[Serializable]
public enum ComplexityEnum { Simple = 0, Complex = 100, Invalid = 999 }

[Flags]
[Serializable]
public enum StatusFlags { None = 0, Active = 1 << 0, Paused = 1 << 1, Invisible = 1 << 2 }

// Edge Case: Generic Classes and Structs
[Serializable]
public class GenericClass<T>
{
    public T Value;
}

[Serializable]
public struct GenericStruct<T>
{
    public T Value;
}

// Edge Case: Polymorphism (SerializeReference)

[Serializable]
public abstract class AbstractItem
{
    public string id;
}

[Serializable]
public class WeaponItem : AbstractItem
{
    public int damage;
}

[Serializable]
public class PotionItem : AbstractItem
{
    public float healAmount;
    public Color potionColor;
    public SimpleStruct simpleStruct;
}

// Edge Case: Nested Structs vs Classes
[Serializable]
public struct SimpleStruct
{
    public int x;
    public int y;
}

// Edge Case: Circular Dependencies (Pure C#)
[Serializable]
public class CircularNode
{
    public int id;
    public CircularNode next; // Reference to another node
}

// MAIN PAYLOAD
[Serializable]
public class BridgePayload
{
    [Header("Primitives & Boundaries")]
    public string textWithSpecialChars; // Test: "Test\nNewLines\tTabs"
    public string emptyString;          // Test: ""
    public string nullString;           // Test: null (should remain null)
    public float floatInfinity;         // Test: float.PositiveInfinity
    public float floatNaN;              // Test: float.NaN
    public int intMin;                  // Test: int.MinValue
    public SimpleStruct simpleStruct;


    [Header("Generic Classes")]
    public GenericClass<string> genericString;
    public GenericClass<int> genericInt;
    public GenericClass<SimpleStruct> genericStruct;
    public GenericStruct<string> valueGenericString;
    public GenericStruct<int> valueGenericInt;
    public GenericStruct<SimpleStruct> valueGenericStruct;

    [Header("Unserialize Support")]
    [NonSerialized]
    public string thisTextMustNotBeSerialized;
    [NonSerialized]
    public int thisNumberMustNotBeSerialized;

    [Header("Nullable Types")]
    public int? nullableNumber;
    public SimpleStruct? nullableStruct;
    [SerializeReference] public GenericClass<int> refNullableGenClass; // Should technically work like nullableStruct
    public GenericClass<int> NullableGenClass; // Should technically work like nullableStruct

    [Header("Dictionaries")]
    public Dictionary<string, int> stringNumPairs;
    public Dictionary<UnityEngine.Object, int> objectNumPairs;
    public Dictionary<UnityEngine.Object, UnityEngine.Vector3> objectUnityPairs;
    public Dictionary<List<string>, List<int>> nestedStringNumPairs;
    public Dictionary<List<GenericStruct<SimpleStruct>>, List<SimpleStruct>> nestedStructsPairs;


    [Header("Enums")]
    public ComplexityEnum standardEnum;
    public StatusFlags flagsEnum;

    [Header("Unity Value Types")]
    public Vector3 nonZeroVector;
    public Quaternion specificRotation;
    public LayerMask specificLayer;
    public Color32 byteColor;           // Test: Precision loss vs Color

    [Header("Unity Reference Types (Internal)")]
    public AnimationCurve curve;        // Complex internal object
    public Gradient gradient;           // Complex internal object

    [Header("Unity Object References (Scene/Assets)")]
    public GameObject selfGameObject;   // Ref to holding GO
    public Transform selfTransform;     // Ref to holding Transform
    public MonoBehaviour selfComponent; // Ref to holding Script
    public SerializationBridgeTester typedReference; // Typed Ref
    public ExternalRefComponent externalRef; // Ref to another script on same or diff GO
    public List<GameObject> gameObjectList;

    [Header("Collections")]
    public int[] intArray;
    public List<int> intList;
    public List<string> emptyList;      // Initialized but count 0
    public List<string> explicitlyNullList; // Actually null

    [Header("Nested Collections Wrapper")]
    [SerializeReference] public List<List<UnityEngine.Object>> nestedListReferences;
    public List<List<List<AbstractItem>>> abstractThreeDimensionalItem;

    [Header("Polymorphism ([SerializeReference])")]
    [SerializeReference] public AbstractItem singlePolyItem;
    [SerializeReference] public List<AbstractItem> polyList;

    [Header("Object Identity (Shared Refs)")]
    [SerializeReference] public AbstractItem sharedRefA;
    [SerializeReference] public AbstractItem sharedRefB; // Must point to same instance as A]

    [Header("Pure C# Circular Reference")]
    [SerializeReference] public CircularNode circularRoot;
}

public class ExternalRefComponent : MonoBehaviour
{
    public string verifyIdentity = "I am the external component";
}

public class SerializationBridgeTester : MonoBehaviour, ISerializationCallbackReceiver
{
    [Header("Bridge Data")]
    [SerializeField]
    private BridgePayload payload;

    [Header("Debug")]
    public bool initializeOnAwake = true;
    public bool useChildObjectInsteadOfSelf = true;

    [Header("Dictionaries")]
    public Dictionary<string, ExternalRefComponent> identifiedExternalsPairs;
    [SerializeField]
    private List<ExternalRefComponent> privateIdentificationsPairs;

    private void Awake()
    {
        if (initializeOnAwake)
        {
            InitializeFullTestData();
            initializeOnAwake = false;
        }
        // else
        //     Debug.Log("----------- AWAKE CALLED!! --------------");
    }

    public void OnBeforeSerialize()
    {
        // Debug.Log("----------- ONBEFORESERIALIZE CALLED!! --------------");
    }

    public void OnAfterDeserialize()
    {
        // Debug.Log("----------- ONAFTERDESERIALIZE CALLED!! --------------");
    }

    // Context menu allows you to trigger this in Editor to verify serialization
    // before the game even runs.
    [ContextMenu("Initialize Full Test Data")]
    public void InitializeFullTestData()
    {
        // Setup External Reference (Ensure component exists)
        var ext = GetComponent<ExternalRefComponent>();
        if (ext == null)
        {
            GameObject objectToAttach = gameObject;
            if (useChildObjectInsteadOfSelf)
            {
                objectToAttach = new GameObject("ChildObject");
                objectToAttach.transform.SetParent(transform, false);
            }
            ext = objectToAttach.AddComponent<ExternalRefComponent>();
        }

        // Dictionary initialization
        identifiedExternalsPairs = new()
        {
          { "DangerousExternal", ext }
        };

        privateIdentificationsPairs = [ext];

        // 0. Ensure Payload Exists
        payload = new BridgePayload();
        payload.externalRef = ext;

        // // 1. Primitives & Boundaries
        payload.textWithSpecialChars = "Hello\nWorld\tWith \"Quotes\" & Symbols";
        payload.emptyString = "";
        payload.nullString = null;
        payload.floatInfinity = float.PositiveInfinity;
        payload.floatNaN = float.NaN;
        payload.intMin = int.MinValue;
        payload.simpleStruct = new() { x = 1, y = 2 };

        // 1.1. Non Serialization
        payload.thisTextMustNotBeSerialized = "Something very bad to be serialized";
        payload.thisNumberMustNotBeSerialized = 9123091;

        // 1.2 Generics
        payload.genericString = new() { Value = "MyValueToBeSerialized" };
        payload.genericInt = new() { Value = 2 };
        payload.genericStruct = new() { Value = new() { x = 3, y = 4 } };
        payload.valueGenericString = new() { Value = "MyValueToBeSerialized" };
        payload.valueGenericInt = new() { Value = 2 };
        payload.valueGenericStruct = new() { Value = new() { x = 3, y = 4 } };

        // 1.3 Nullables
        payload.nullableNumber = null;
        payload.nullableStruct = null;
        payload.NullableGenClass = null;
        payload.refNullableGenClass = null;

        // 1.4 Dictionaries
        payload.stringNumPairs = new()
        {
            { "SomeValue", 99 },
            {"Hello World", 2241}
        };
        payload.objectNumPairs = new()
        {
            { this, 99},
            {transform, 1234},
            {gameObject, 23},
            {payload.externalRef, 1232123}
        };
        payload.nestedStringNumPairs = new()
        {
          { [], [] },
          { ["Somevava", "AwesomeString", "This is Important@$&!*()&!*)%!!"], [22, 1224, 99] }
        };
        payload.objectUnityPairs = new(){
            { this, new(23, 99, 124)}
        };
        payload.nestedStructsPairs = new()
        {
          { [new() { Value = new() { x = 3, y = 4 } }], [new() { x = 1, y = 99 }] }
        };

        // 2. Enums
        payload.standardEnum = ComplexityEnum.Complex;
        payload.flagsEnum = StatusFlags.Active | StatusFlags.Invisible;

        // // 3. Unity Value Types
        payload.nonZeroVector = new Vector3(1.1f, 2.2f, 3.3f);
        payload.specificRotation = Quaternion.Euler(45, 90, 180);
        payload.specificLayer = new LayerMask { value = 1 << 0 | 1 << 5 };
        payload.byteColor = new Color32(255, 128, 0, 255);

        // // 4. Unity Reference Types (Internal Data)
        payload.curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));
        payload.gradient = new Gradient();
        payload.gradient.SetKeys(
            [new GradientColorKey(Color.red, 0.0f), new GradientColorKey(Color.blue, 1.0f)],
            [new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f)]
        );

        // 5. Unity Object References
        // Setup Self References
        payload.selfGameObject = gameObject;
        payload.selfTransform = transform;
        payload.selfComponent = this;
        payload.typedReference = this;
        payload.gameObjectList = [gameObject];

        // 6. Collections
        payload.intArray = [1, 1, 2, 3, 5, 8];
        payload.intList = [10, 20, 30];
        payload.emptyList = []; // Not null, but 0 count
        payload.explicitlyNullList = null;      // Explicitly null

        // 7. Nested Collections Wrapper
        payload.abstractThreeDimensionalItem = [
            [
                [
                    new WeaponItem { id = "DimensionalSword", damage = 18491 }
                ]
            ]
        ];
        payload.nestedListReferences = [
            null,
            [
                transform,
                this,
                gameObject
            ]
        ];

        // 8. Polymorphism
        payload.singlePolyItem = new WeaponItem { id = "Sword", damage = 50 };

        var refWeaponItem = new WeaponItem { id = "Hammer", damage = 125 };
        payload.polyList =
        [
            new WeaponItem { id = "Axe", damage = 75 },
            new PotionItem { id = "Health", healAmount = 25.5f, potionColor = Color.red, simpleStruct = new() { x = 1, y = 2 } },
        ];

        // 9. Object Identity (Shared References)
        // We create ONE object and assign it to TWO fields.
        // The Bridge must maintain that these point to the same memory address after deserialization.
        var sharedItem = new WeaponItem { id = "SharedUnique", damage = 999 };
        payload.sharedRefA = sharedItem;
        payload.sharedRefB = sharedItem;

        // // 10. Circular References (Pure C#)
        // A -> B -> A
        var root = new CircularNode { id = 1 };
        var child = new CircularNode { id = 2 };
        root.next = child;
        child.next = root; // The cycle
        payload.circularRoot = root;

        if (!UnitySerializationBridge.BridgeManager.enableDebugLogs.Value) return;
        Debug.Log($"[BridgeTester] Data Initialized. Shared Ref Identity: {ReferenceEquals(payload.sharedRefA, payload.sharedRefB)}");
    }

    // Test Logic to verify the bridge didn't break anything
    [ContextMenu("Verify Integrity")]
    public void VerifyIntegrity()
    {
        if (!UnitySerializationBridge.BridgeManager.enableDebugLogs.Value) return;
        if (payload == null)
        {
            Debug.LogError("Payload is null!");
            return;
        }

        // 1. Identity Check
        bool identityPreserved = ReferenceEquals(payload.sharedRefA, payload.sharedRefB);
        Debug.Log($"Identity Preserved (Shared Refs): {identityPreserved}");

        // 2. Circular Check
        bool circlePreserved = false;
        if (payload.circularRoot != null && payload.circularRoot.next != null)
        {
            circlePreserved = ReferenceEquals(payload.circularRoot, payload.circularRoot.next.next);
        }
        Debug.Log($"Circular Reference Preserved: {circlePreserved}");

        // 3. Unity Object Check
        bool unityRefPreserved = payload.selfGameObject == this.gameObject;
        Debug.Log($"Unity Object Reference (Self): {unityRefPreserved}");

        bool extRefPreserved = payload.externalRef != null;
        Debug.Log($"Unity External Reference: {extRefPreserved}");

        // 4. Null vs Empty List Check
        bool nullPreserved = payload.explicitlyNullList == null;
        bool emptyPreserved = payload.emptyList != null && payload.emptyList.Count == 0;
        Debug.Log($"Null List Correct: {nullPreserved} | Empty List Correct: {emptyPreserved}");
    }
}
#endif