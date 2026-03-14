using System;
//using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;
using Il2CppInterop.Runtime;
//using Il2CppInterop.Runtime.InteropTypes.Fields;

[assembly: MelonInfo(typeof(TrueFreeSolo.TrueFreeSoloMod), "TrueFreeSolo", "1.0.0", "Zhexirox")]
[assembly: MelonGame("TheGameBakers", "Cairn")]

namespace TrueFreeSolo
{
    /// <summary>
    /// TrueFreeSolo - Experience Cairn as a true free solo climb.
    /// 
    /// This mod removes all food-based stat boosts while preserving chalk effects.
    /// Install the mod and play - no configuration needed.
    /// 
    /// HOW WE DISTINGUISH CHALK FROM FOOD BOOSTS:
    /// ============================================================
    /// We check "remainingBoostUnits" to distinguish boosts:
    ///   - CHALK boosts use GRABS (typically 12-24) → remaining ≤ 24 → KEPT
    ///   - FOOD boosts use SECONDS (typically 100-300) → remaining > 24 → REMOVED
    /// 
    /// Special case: Toughness (boostType 5) grants shield with low
    /// remainingBoostUnits (impacts, not seconds). These are always removed.
    /// 
    /// Compatible with Cairn (Unity 6, IL2CPP) via MelonLoader.
    /// </summary>
    public class TrueFreeSoloMod : MelonMod
    {
        // ================================================================
        // CONSTANTS
        // ================================================================

        /// <summary>
        /// Threshold to distinguish chalk (grabs) from food (seconds).
        /// Chalk: 12-24 grabs. Food: 100-300 seconds.
        /// </summary>
        private const float CHALK_THRESHOLD = 24f;

        /// <summary>BoostType.Toughness = 5 (always remove, grants shield)</summary>
        private const int BOOST_TYPE_TOUGHNESS = 5;
        
        /// <summary>Total boost types in BoostType enum (0 through 8)</summary>
        private const int BOOST_TYPE_COUNT = 9;

        /// <summary>Boost type names for reference debugging)</summary>
        private static readonly string[] BOOST_NAMES = { 
            "Stamina", "Strength", "RestSpeed", "Grip", 
            "Temperature", "Toughness", "Burst", "SuperGrip", "PsychBoost" 
        };


        /// <summary>
        /// Common binding flags for reflection - searches instance members, both public and private.
        /// Used everywhere we need to access IL2CPP fields/properties/methods.
        /// </summary>
        private static readonly Il2CppSystem.Reflection.BindingFlags ALL_FLAGS =
            Il2CppSystem.Reflection.BindingFlags.Instance |
            Il2CppSystem.Reflection.BindingFlags.Public |
            Il2CppSystem.Reflection.BindingFlags.NonPublic;
        
        // ================================================================
        // INSTANCE REFERENCES (change per scene/save)
        // ================================================================

        /// <summary>Logger instance for mod messages</summary>
        private MelonLogger.Instance _log;
        
        /// <summary>Reference to the GameDataManager singleton</summary>
        private Component _gameDataManager;
        
        /// <summary>Reference to ClimberData (player stats, boosts, shield)</summary>
        private Il2CppSystem.Object _climberData;

        // ================================================================
        // REFLECTION CACHE (resolved once, reused forever)
        // ================================================================
        // These are the "addresses" to fields/properties/methods.
        // The addresses never change - only the values inside change.
        // Caching them avoids expensive lookups every frame.
        /*
            FindObjectsOfType<MonoBehaviour>()  →  find "GameDataManager"
                → GameDataManager.gameData
                    → gameData.climberData (Buff and shield)
        */
        private bool _reflectionCached = false;

        // -- GameDataManager --
        private Il2CppSystem.Type _gdmType;
        private Il2CppSystem.Reflection.FieldInfo _gameDataField;      // GDM.gameData
        private Il2CppSystem.Reflection.FieldInfo _onShieldLostField;  // GDM.OnShieldLost

         // -- GameData --
        private Il2CppSystem.Reflection.FieldInfo _climberDataField;   // gameData.climberData

        // -- ClimberData --
        private Il2CppSystem.Reflection.FieldInfo _boostsField;           // climberData.boosts
        private Il2CppSystem.Reflection.FieldInfo _shieldHpField;         // climberData.shieldHpRemaining
        private Il2CppSystem.Reflection.FieldInfo _shieldImpactsField;    // climberData.shieldImpactsRemaining
        private Il2CppSystem.Reflection.FieldInfo _onBoostRemovedField;   // climberData.OnBoostRemoved
        
        // -- Boosts Dictionary (Dict<BoostType, List<BoostData>>) --
        private Il2CppSystem.Reflection.PropertyInfo _dictIndexer;  // boosts[type]
        
        // -- List<BoostData> --
        private Il2CppSystem.Reflection.PropertyInfo _listCountProp;  // list.Count
        private Il2CppSystem.Reflection.PropertyInfo _listItemProp;   // list[i]
        private Il2CppSystem.Reflection.MethodInfo _listRemoveAt;     // list.RemoveAt(i)
        
        // -- BoostData --
        private Il2CppSystem.Reflection.FieldInfo _remainingField;  // boostData.remainingBoostUnits

        // ================================================================
        // MELONLOADER LIFECYCLE
        // ================================================================

        public override void OnInitializeMelon()
        {
            _log = LoggerInstance;
            _log.Msg("========================================");
            _log.Msg("   TrueFreeSolo v1.0");
            _log.Msg("   Food boosts blocked, chalk allowed");
            _log.Msg("   Good Luck Climber!");
            _log.Msg("========================================");
        }

        /// <summary>
        /// Clear instance references on scene change.
        /// Reflection cache persists (types don't change).
        /// </summary>
        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _gameDataManager = null;
            _climberData = null;
        }

        public override void OnUpdate()
        {
            // Ensure we have valid references and reflection cache
            if (!EnsureReady()) return;
            
            // Order matters: zero shield FIRST, then remove boosts
            ZeroShieldFields();
            RemoveFoodBoosts();
        }

        // ================================================================
        // INITIALIZATION & CACHING
        // ================================================================

        /// <summary>
        /// Ensures GameDataManager, ClimberData, and reflection cache are ready.
        /// Returns false if game data isn't loaded yet.
        /// </summary>
        private bool EnsureReady()
        {
            // Already have everything cached?
            if (_climberData != null && _reflectionCached) return true;
            
            // Find GameDataManager if we don't have it
            if (_gameDataManager == null)
            {
                FindGameDataManager();
                if (_gameDataManager == null) return false;
            }
            
            // Navigate to ClimberData
            if (_climberData == null)
            {
                _climberData = FetchClimberData();
                if (_climberData == null) return false;
            }
            
            // Cache reflection metadata (once per session)
            if (!_reflectionCached)
            {
                CacheReflection();
            }
            
            return _reflectionCached;
        }

        /// <summary>
        /// Finds GameDataManager singleton by scanning all MonoBehaviours.
        /// </summary>
        private void FindGameDataManager()
        {
            var allMonos = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            foreach (var mono in allMonos)
            {
                var t = mono.GetIl2CppType();
                if (t.Name == "GameDataManager")
                {
                    _gameDataManager = mono;
                    _gdmType = t;
                    
                    // Cache GDM fields while we're here
                    _gameDataField = _gdmType.GetField("gameData", ALL_FLAGS);
                    _onShieldLostField = _gdmType.GetField("OnShieldLost", ALL_FLAGS);
                    return;
                }
            }
        }
        /// <summary>
        /// Navigates GameDataManager → gameData → climberData.
        /// </summary>
        private Il2CppSystem.Object FetchClimberData()
        {
            if (_gameDataField == null) return null;
            
            try
            {
                var gameData = _gameDataField.GetValue(_gameDataManager);
                if (gameData == null) return null;
                
                // Cache gameData → climberData field on first access
                if (_climberDataField == null)
                {
                    _climberDataField = gameData.GetIl2CppType().GetField("climberData", ALL_FLAGS);
                }
                
                return _climberDataField?.GetValue(gameData);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Caches all reflection metadata (FieldInfo, PropertyInfo, MethodInfo).
        /// Called once - IL2CPP types are constant at runtime.
        /// 
        /// Think of this as "memorizing the addresses" of all the data we need.
        /// After this, accessing data is just reading from known addresses.
        /// </summary>
        private void CacheReflection()
        {
            try
            {
                var cdType = _climberData.GetIl2CppType();
                
                // -- ClimberData fields --
                _boostsField = cdType.GetField("boosts", ALL_FLAGS);
                _shieldHpField = cdType.GetField("shieldHpRemaining", ALL_FLAGS);
                _shieldImpactsField = cdType.GetField("shieldImpactsRemaining", ALL_FLAGS);
                _onBoostRemovedField = cdType.GetField("OnBoostRemoved", ALL_FLAGS);
                
                // -- Boosts dictionary indexer --
                if (_boostsField != null)
                {
                    var boostsDict = _boostsField.GetValue(_climberData);
                    if (boostsDict != null)
                    {
                        _dictIndexer = boostsDict.GetIl2CppType().GetProperty("Item");
                        
                        // Try to cache List<BoostData> methods from any existing list
                        TryCacheListReflection(boostsDict);
                    }
                }
                
                // Verify essential fields were found
                _reflectionCached = (_boostsField != null && _shieldHpField != null);
                
                if (_reflectionCached)
                    _log.Msg("[Cache] Reflection metadata cached successfully");
                else
                    _log.Warning("[Cache] Failed to cache some reflection metadata");
            }
            catch (Exception ex)
            {
                _log.Warning($"[Cache] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Tries to cache List<BoostData> and BoostData reflection from existing boosts.
        /// </summary>
        private void TryCacheListReflection(Il2CppSystem.Object boostsDict)
        {
            for (int bt = 0; bt < BOOST_TYPE_COUNT; bt++)
            {
                try
                {
                    var list = _dictIndexer.GetValue(boostsDict, 
                        new Il2CppSystem.Object[] { (Il2CppSystem.Object)bt });
                    if (list == null) continue;
                    
                    var listType = list.GetIl2CppType();
                    
                    // Cache List<BoostData> operations
                    _listCountProp = listType.GetProperty("Count");
                    _listItemProp = listType.GetProperty("Item");
                    _listRemoveAt = listType.GetMethod("RemoveAt");
                    
                    // Try to get a sample BoostData to cache its field
                    int count = _listCountProp.GetValue(list, null)?.Unbox<int>() ?? 0;
                    if (count > 0)
                    {
                        var sample = _listItemProp.GetValue(list, 
                            new Il2CppSystem.Object[] { (Il2CppSystem.Object)0 });
                        if (sample != null)
                        {
                            _remainingField = sample.GetIl2CppType().GetField("remainingBoostUnits", ALL_FLAGS);
                        }
                    }
                    
                    // Found what we needed
                    if (_listCountProp != null) break;
                }
                catch { }
            }
        }

        // ================================================================
        // SHIELD REMOVAL
        // ================================================================

        /// <summary>
        /// Zeroes shield fields and fires GameDataManager.OnShieldLost to update UI.
        /// Uses cached reflection - no GetField calls here.
        /// </summary>
        private unsafe void ZeroShieldFields()
        {
            try
            {
                // Read current values using cached FieldInfo
                int currentHp = _shieldHpField.GetValue(_climberData).Unbox<int>();
                int currentImpacts = _shieldImpactsField.GetValue(_climberData).Unbox<int>();
                
                // Nothing to do if shield is already empty
                if (currentHp <= 0 && currentImpacts <= 0) return;
                
                // Write zeros via native IL2CPP API (fastest path)
                IntPtr objPtr = _climberData.Pointer;
                IntPtr classPtr = IL2CPP.il2cpp_object_get_class(objPtr);
                IntPtr hpFieldPtr = IL2CPP.il2cpp_class_get_field_from_name(classPtr, "shieldHpRemaining");
                IntPtr impactsFieldPtr = IL2CPP.il2cpp_class_get_field_from_name(classPtr, "shieldImpactsRemaining");
                
                if (hpFieldPtr != IntPtr.Zero && impactsFieldPtr != IntPtr.Zero)
                {
                    int zero = 0;
                    IL2CPP.il2cpp_field_set_value(objPtr, hpFieldPtr, &zero);
                    IL2CPP.il2cpp_field_set_value(objPtr, impactsFieldPtr, &zero);
                }
                
                // Fire OnShieldLost to update UI
                FireOnShieldLost();
                
                // DEBUG
                //_log.Msg($"Shield zeroed: HP {currentHp}→0, Impacts {currentImpacts}→0");
            }
            catch (Exception ex)
            {
                _log.Warning($"ZeroShieldFields error: {ex.Message}");
            }
        }

        /// <summary>
        /// Fires GameDataManager.OnShieldLost(false) to hide shield UI.
        /// </summary>
        private void FireOnShieldLost()
        {
            try
            {
                if (_onShieldLostField == null) return;
                
                var del = _onShieldLostField.GetValue(_gameDataManager);
                if (del == null) return;
                
                var invoke = del.GetIl2CppType().GetMethod("Invoke");
                invoke?.Invoke(del, new Il2CppSystem.Object[] { (Il2CppSystem.Object)false });
            }
            catch (Exception ex)
            {
                _log.Warning($"OnShieldLost fire failed: {ex.Message}");
            }
        }

        // ================================================================
        // FOOD BOOST REMOVAL
        // ================================================================

        /// <summary>
        /// Core logic: removes food boosts (remaining > 24 seconds).
        /// Chalk boosts (remaining ≤ 24 grabs) are preserved.
        /// Toughness (type 5) always removed.
        /// Uses cached reflection - minimal overhead per frame.
        /// </summary>
        private void RemoveFoodBoosts()
        {
            try
            {
                // Get current boosts dictionary using cached field
                var boostsDict = _boostsField.GetValue(_climberData);
                if (boostsDict == null) return;
                
                // Process all 9 boost types
                for (int boostType = 0; boostType < BOOST_TYPE_COUNT; boostType++)
                {
                    ProcessBoostType(boostType, boostsDict);
                }
            }
            catch { }
        }

        /// <summary>
        /// Processes one boost type, removing food boosts from its list.
        /// </summary>
        private void ProcessBoostType(int boostType, Il2CppSystem.Object boostsDict)
        {
            try
            {
                // Get list for this boost type using cached indexer
                var boostList = _dictIndexer.GetValue(boostsDict, 
                    new Il2CppSystem.Object[] { (Il2CppSystem.Object)boostType });
                if (boostList == null) return;
                
                // Get count using cached property
                int count = _listCountProp.GetValue(boostList, null)?.Unbox<int>() ?? 0;
                if (count == 0) return;
                
                bool isToughness = (boostType == BOOST_TYPE_TOUGHNESS);
                
                // Iterate backwards so RemoveAt doesn't shift unprocessed indices
                for (int i = count - 1; i >= 0; i--)
                {
                    var boostData = _listItemProp.GetValue(boostList, 
                        new Il2CppSystem.Object[] { (Il2CppSystem.Object)i });
                    if (boostData == null) continue;
                    
                    // Determine if we should remove this boost
                    bool shouldRemove;
                    float remaining = 0f;  // Declared here for debug logging
                    
                    if (isToughness)
                    {
                        // Toughness always removed (grants shield)
                        shouldRemove = true;
                    }
                    else
                    {
                        // Check remaining units: >24 = food (seconds), ≤24 = chalk (grabs)
                        try
                        {
                            // Cache _remainingField on first use if not already cached
                            if (_remainingField == null)
                            {
                                _remainingField = boostData.GetIl2CppType().GetField("remainingBoostUnits", ALL_FLAGS);
                            }
                            remaining = _remainingField?.GetValue(boostData)?.Unbox<float>() ?? 0f;
                        }
                        catch { continue; }
                        
                        shouldRemove = (remaining > CHALK_THRESHOLD);
                    }
                    
                    if (!shouldRemove) continue;
                    
                    // Remove the boost using cached method
                    _listRemoveAt.Invoke(boostList, 
                        new Il2CppSystem.Object[] { (Il2CppSystem.Object)i });
                    
                    // DEBUG
                    //_log.Msg($"Blocked {BOOST_NAMES[boostType]} (remaining: {remaining:F1})");
                    
                    // For Toughness: also fire OnBoostRemoved
                    if (isToughness)
                    {
                        FireOnBoostRemoved(boostData);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Fires ClimberData.OnBoostRemoved for proper shield cleanup.
        /// </summary>
        private void FireOnBoostRemoved(Il2CppSystem.Object boostData)
        {
            try
            {
                var del = _onBoostRemovedField?.GetValue(_climberData);
                if (del == null) return;
                
                var invoke = del.GetIl2CppType().GetMethod("Invoke");
                invoke?.Invoke(del, new Il2CppSystem.Object[] { boostData });
            }
            catch (Exception ex)
            {
                _log.Warning($"OnBoostRemoved fire failed: {ex.Message}");
            }
        }
    }
}