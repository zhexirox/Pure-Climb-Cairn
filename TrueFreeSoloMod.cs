using System;
using MelonLoader;
using UnityEngine;
using Il2CppInterop.Runtime;

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
    /// WHY FILTER BY "remainingBoostUnits" INSTEAD OF BOOST TYPE?
    /// ============================================================
    /// In Cairn, food items can give HIDDEN boosts beyond what the UI shows.
    /// For example, chocolate displays a Grip boost, but internally it ALSO
    /// grants a Stamina boost. This means we cannot simply filter by BoostType
    /// (like blocking all RestSpeed/Grip boosts) because:
    ///   - Chalk gives Stamina boosts (which we want to KEEP)
    ///   - Chocolate gives Stamina boosts (which we want to REMOVE)
    /// 
    /// The solution is to check the "remainingBoostUnits" field:
    ///   - CHALK boosts are measured in GRABS (typically 12-24 per use)
    ///   - FOOD boosts are measured in SECONDS (typically 100-300 per item)
    /// 
    /// By using a threshold of 30, we can reliably distinguish between them:
    ///   - remaining ≤ 30 → CHALK (grabs) → KEPT
    ///   - remaining > 30 → FOOD (seconds) → REMOVED
    /// 
    /// Compatible with Cairn (Unity 6, IL2CPP) via MelonLoader.
    /// </summary>
    public class TrueFreeSoloMod : MelonMod
    {
        private MelonLogger.Instance _log;
        
        // Cached references for performance
        private Component _cachedGDM;
        private Il2CppSystem.Type _cachedGDMType;
        private Il2CppSystem.Object _cachedClimberData;
        
        /// <summary>
        /// Threshold to distinguish chalk (grabs) from food (seconds).
        /// Chalk: 12-24 grabs per use → remaining ≤ 24
        /// Food: 100-300 seconds per item → remaining > 100
        /// Threshold of 30 provides safe margin.
        /// </summary>
        private const float CHALK_THRESHOLD = 30f;
        
        // BoostType enum names for logging
        private static readonly string[] BOOST_TYPE_NAMES = { 
            "Stamina", "Strength", "RestSpeed", "Grip", 
            "Temperature", "Toughness", "Burst", "SuperGrip", "PsychBoost" 
        };

        public override void OnInitializeMelon()
        {
            _log = LoggerInstance;
            _log.Msg("========================================");
            _log.Msg("   TrueFreeSolo v1.0");
            _log.Msg("   Food boosts blocked, chalk allowed");
            _log.Msg("   Good Luck Climber!");
            _log.Msg("========================================");
        }

        public override void OnUpdate()
        {
            RemoveFoodBoosts();
        }

        /// <summary>
        /// Core logic: removes all boosts with remainingBoostUnits > 30.
        /// These are food-based boosts measured in seconds.
        /// Chalk boosts (measured in grabs, ≤24) are preserved.
        /// </summary>
        private void RemoveFoodBoosts()
        {
            var climberData = GetClimberData();
            if (climberData == null) return;
            
            try
            {
                var boostsField = climberData.GetIl2CppType().GetField("boosts",
                    Il2CppSystem.Reflection.BindingFlags.Instance |
                    Il2CppSystem.Reflection.BindingFlags.Public |
                    Il2CppSystem.Reflection.BindingFlags.NonPublic);
                    
                if (boostsField == null) return;
                
                var boostsDict = boostsField.GetValue(climberData);
                if (boostsDict == null) return;
                
                var dictType = boostsDict.GetIl2CppType();
                var indexer = dictType.GetProperty("Item");
                
                // Process all 9 boost types (BoostType enum: 0-8)
                for (int boostType = 0; boostType <= 8; boostType++)
                {
                    try
                    {
                        var boostList = indexer.GetValue(boostsDict, 
                            new Il2CppSystem.Object[] { (Il2CppSystem.Object)boostType });
                        if (boostList == null) continue;
                        
                        var listType = boostList.GetIl2CppType();
                        var countProp = listType.GetProperty("Count");
                        int count = countProp?.GetValue(boostList, null)?.Unbox<int>() ?? 0;
                        
                        if (count == 0) continue;
                        
                        var listIndexer = listType.GetProperty("Item");
                        var removeAtMethod = listType.GetMethod("RemoveAt");
                        
                        // Iterate backwards to safely remove without breaking indices
                        for (int i = count - 1; i >= 0; i--)
                        {
                            var boostData = listIndexer.GetValue(boostList, 
                                new Il2CppSystem.Object[] { (Il2CppSystem.Object)i });
                            if (boostData == null) continue;
                            
                            var bdType = boostData.GetIl2CppType();
                            var remainingField = bdType.GetField("remainingBoostUnits",
                                Il2CppSystem.Reflection.BindingFlags.Instance |
                                Il2CppSystem.Reflection.BindingFlags.Public |
                                Il2CppSystem.Reflection.BindingFlags.NonPublic);
                            
                            float remaining = 0f;
                            try { remaining = remainingField?.GetValue(boostData)?.Unbox<float>() ?? 0f; } 
                            catch { continue; }
                            
                            // remaining > 30 = food (seconds) → remove
                            // remaining ≤ 30 = chalk (grabs) → keep
                            if (remaining > CHALK_THRESHOLD)
                            {
                                removeAtMethod.Invoke(boostList, 
                                    new Il2CppSystem.Object[] { (Il2CppSystem.Object)i });
                                //_log.Msg($"Blocked: {BOOST_TYPE_NAMES[boostType]} ({remaining:F0}s)");
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private Il2CppSystem.Object GetClimberData()
        {
            if (_cachedClimberData != null) return _cachedClimberData;
            
            var gdm = FindGameDataManager();
            if (gdm == null) return null;
            
            try
            {
                var gameDataField = _cachedGDMType.GetField("gameData",
                    Il2CppSystem.Reflection.BindingFlags.Instance |
                    Il2CppSystem.Reflection.BindingFlags.Public |
                    Il2CppSystem.Reflection.BindingFlags.NonPublic);
                var gameData = gameDataField?.GetValue(gdm);
                if (gameData == null) return null;
                
                var climberDataField = gameData.GetIl2CppType().GetField("climberData",
                    Il2CppSystem.Reflection.BindingFlags.Instance |
                    Il2CppSystem.Reflection.BindingFlags.Public |
                    Il2CppSystem.Reflection.BindingFlags.NonPublic);
                _cachedClimberData = climberDataField?.GetValue(gameData);
                return _cachedClimberData;
            }
            catch
            {
                return null;
            }
        }

        private Component FindGameDataManager()
        {
            if (_cachedGDM != null)
                return _cachedGDM;

            var allMonos = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            foreach (var mono in allMonos)
            {
                var t = mono.GetIl2CppType();
                if (t.Name == "GameDataManager")
                {
                    _cachedGDM = mono;
                    _cachedGDMType = t;
                    return _cachedGDM;
                }
            }
            return null;
        }
    }
}
