using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnitySerializationBridge.Core;
using UnityEngine.SceneManagement;
using UnitySerializationBridge.Utils;
using System.Reflection;
using UnityEngine;
using UnitySerializationBridge.Core.Serialization;
using UnitySerializationBridge.Patches.Serialization;

namespace UnitySerializationBridge
{
	[BepInPlugin(GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
	internal class BridgeManager : BaseUnityPlugin
	{
		const string GUID = "pixelguy.pixelmodding.unity.bridgemanager";
		internal static ConfigEntry<bool> enableDebugLogs, enabledEstimatedTypeSize;
		internal static ConfigEntry<int> sizeForTypesReflectionCache, sizeForMemberAccessReflectionCache;
		internal static BridgeManager Instance { get; private set; }
		// Private/Internal methods
		void Awake()
		{
			// Init
			Instance = this;
			var h = new Harmony(GUID);
			h.PatchAll();
			SerializationObserver.harmony = h;

			// Config
			enableDebugLogs = Config.Bind("Debugging", "Enable Debug Logs", false, "If True, the library will log all the registered types on initialization.");
			enabledEstimatedTypeSize = Config.Bind("Performance", "Enable Type-Size Estimation", false, "If True, the library will scan all types from the Plugins folder to estimate the max size of the cache for saving Types. This might make the loading time take longer.");
			sizeForTypesReflectionCache = Config.Bind("Performance", "Type Caching Limit", 600, "Determines the size of the cache for saving types. Any value below 100 will default to estimating cache size (Type-Size Estimation).");
			sizeForMemberAccessReflectionCache = Config.Bind("Performance", "Member Access Caching Limit", 450, "Determines the size of the cache for saving most member-access operations (FieldInfo.GetValue, FieldInfo.SetValue, MethodInfo.Invoke, Activator.Invoke, etc.). The value cannot be below 100.");
			sizeForMemberAccessReflectionCache.Value = Mathf.Max(100, sizeForMemberAccessReflectionCache.Value);

			CacheInitializer.ImmediatelyInitializeCacheValues();

			if (!enabledEstimatedTypeSize.Value || sizeForTypesReflectionCache.Value < 100)
				SceneManager.sceneLoaded += GetEstimatedTypeSize;
			else
				CacheInitializer.InitializeCacheValues();

			void GetEstimatedTypeSize(Scene _, LoadSceneMode _2)
			{
				// Immediately removes from the scene
				SceneManager.sceneLoaded -= GetEstimatedTypeSize;

				Assembly myAssembly = typeof(SerializationHandler).Assembly;
				long typeSize = 0;
				// Do the Type estimation
				foreach (var assembly in AccessTools.AllAssemblies())
				{
					// If it's an assembly from Managed folder or this project, skip
					if (assembly.IsGameAssembly() || assembly == myAssembly) continue;

					// Increment the estimated size
					typeSize += AccessTools.GetTypesFromAssembly(assembly).Length;
				}

				// Calculate the ideal size of the LRUCache 
				// I seriously have to specify generic parameters to access a static field. Why.
				sizeForTypesReflectionCache.Value = (int)System.Math.Floor(MathUtils.CalculateCurve(typeSize, 600, 240));

				// Initialize here otherwise
				CacheInitializer.InitializeCacheValues();
			}

			// DEBUG
#if DEBUG
			StartCoroutine(WaitForGameplay("MainMenu"));
#endif
		}
#if DEBUG
		System.Collections.IEnumerator WaitForGameplay(string scene)
		{
			yield return null;
			while (SceneManager.GetActiveScene().name != scene) yield return null;

			// Benchmark
			Benchmark(1);
			Benchmark(2);
			Benchmark(5);
			Benchmark(25);
			Benchmark(50);
			Benchmark(100);

			static void Benchmark(int instantiations)
			{
				var debug = enableDebugLogs.Value;
				if (debug)
					Debug.Log($"==== Starting benchmark with {instantiations} instantiations =====");
				System.Diagnostics.Stopwatch stopwatch = new();
				stopwatch.Start();

				// START
				var newObject = new GameObject($"OurTestSubject_{instantiations}").AddComponent<SerializationBridgeTester>();
				for (int i = 0; i < instantiations; i++)
				{
					if (debug)
						Debug.Log($"==== Instantiating OurTestObject_{instantiations}_{i} =====");
					newObject = Instantiate(newObject);
					newObject.VerifyIntegrity();
					if (debug)
						newObject.name = $"OurTestObject_{instantiations}_{i}"; // To be easy to actually see the info
				}
				//END
				stopwatch.Stop();
				Debug.Log($"==== Instantiated {instantiations} Objects | Elapsed milliseconds: {stopwatch.ElapsedMilliseconds}ms ====");
			}
		}
#endif
	}
}
