using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Live2DFramework.ActionAgent
{
    // ============================================================
    // Data Models
    // ============================================================

    [Serializable]
    public class IdleActionRootConfig
    {
        public string formatVersion;
        public List<IdleActionConfig> actions;
    }

    [Serializable]
    public class IdleActionConfig
    {
        public int id;
        public string name;
        public string displayName;
        public int weight;
        public float cooldown;
        public string special;              // "hardcoded_star_spin" / "hardcoded_magic_circle" / null
        public List<ActionPhaseConfig> phases;
        public string description;
    }

    [Serializable]
    public class ActionPhaseConfig
    {
        public float duration;
        public string curve;                // "easeOut" / "easeIn" / "smooth" / "hold" / "linear"
        public Dictionary<string, float> targets;
    }

    // ============================================================
    // Runtime State per Action
    // ============================================================

    public class IdleActionState
    {
        public IdleActionConfig config;
        public int phaseIndex;
        public float phaseElapsed;
        public bool isActive;

        public void Reset()
        {
            phaseIndex = 0;
            phaseElapsed = 0f;
            isActive = false;
        }
    }

    // ============================================================
    // IdleActionScheduler
    // ============================================================

    public class IdleActionScheduler
    {
        // --- Config ---
        private List<IdleActionConfig> _actions = new List<IdleActionConfig>();
        private Dictionary<int, IdleActionConfig> _actionMap = new Dictionary<int, IdleActionConfig>();

        // --- Runtime state ---
        private IdleActionState _current = new IdleActionState();
        private Dictionary<int, float> _cooldownTimers = new Dictionary<int, float>();
        private float _globalCooldownTimer;

        // --- Public state access ---
        public IdleActionState CurrentState => _current;
        public bool IsAnyActionActive => _current.isActive;
        public int CurrentActionId => _current.isActive ? _current.config.id : 0;
        public bool IsSpecialAction => _current.isActive && !string.IsNullOrEmpty(_current.config.special);
        public string SpecialActionTag => _current.isActive ? _current.config.special : null;

        // ============================================================
        // Load Config
        // ============================================================

        public void LoadConfig(string json)
        {
            var root = JsonUtility.FromJson<IdleActionRootConfig>(json);
            if (root?.actions == null)
            {
                Debug.LogError("[IdleActionScheduler] Failed to parse idle_actions.json");
                return;
            }

            _actions = root.actions;
            _actionMap.Clear();
            _cooldownTimers.Clear();

            foreach (var a in _actions)
            {
                _actionMap[a.id] = a;
                _cooldownTimers[a.id] = 0f;
            }

            _current.Reset();
            _globalCooldownTimer = 0f;

            Debug.Log($"[IdleActionScheduler] Loaded {_actions.Count} idle actions");
        }

        // ============================================================
        // Pick Next Action
        // ============================================================

        /// <summary>
        /// Weighted random selection with time-of-day / weather adjustments.
        /// Returns the selected action id, or 0 if none available.
        /// </summary>
        public int PickNextAction(float timeOfDay, bool isRaining, bool isSnowing)
        {
            if (_globalCooldownTimer > 0f) return 0;

            var candidates = new List<IdleActionConfig>();

            foreach (var action in _actions)
            {
                // Skip zero-weight (manual only)
                if (action.weight <= 0) continue;

                // Skip cooldown
                if (_cooldownTimers.TryGetValue(action.id, out float cd) && cd > 0f) continue;

                candidates.Add(action);
            }

            if (candidates.Count == 0) return 0;

            // Build adjusted weights
            float[] adjustedWeights = new float[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                float w = candidates[i].weight;

                // Night adjustment: reduce smile/blush, increase cry/confuse
                bool isNight = timeOfDay < 6f || timeOfDay > 20f;
                if (isNight)
                {
                    switch (candidates[i].id)
                    {
                        case 2:  w *= 0.3f; break;   // smile — less at night
                        case 8:  w *= 0.5f; break;   // blush — less at night
                        case 6:  w *= 1.5f; break;   // cry — more at night
                    }
                }

                // Sleepy time (0~6): reduce complex actions
                bool isSleepy = timeOfDay < 6f;
                if (isSleepy)
                {
                    switch (candidates[i].id)
                    {
                        case 7:  w *= 0.2f; break;   // magic circle
                        case 4:  w *= 0.3f; break;   // star spin
                    }
                }

                // Rain adjustment: increase lazy/sad actions
                if (isRaining)
                {
                    switch (candidates[i].id)
                    {
                        case 6:  w *= 1.8f; break;   // cry
                        case 9:  w *= 1.5f; break;   // confuse
                        case 5:  w *= 1.3f; break;   // stretch
                    }
                }

                // Snow adjustment: increase cheerful actions
                if (isSnowing)
                {
                    switch (candidates[i].id)
                    {
                        case 2:  w *= 1.5f; break;   // smile
                        case 4:  w *= 1.3f; break;   // star spin
                    }
                }

                adjustedWeights[i] = Mathf.Max(0.01f, w);
            }

            // Weighted random selection
            float totalWeight = adjustedWeights.Sum();
            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cumulative = 0f;
            int selectedIndex = 0;
            for (int i = 0; i < adjustedWeights.Length; i++)
            {
                cumulative += adjustedWeights[i];
                if (roll <= cumulative)
                {
                    selectedIndex = i;
                    break;
                }
            }

            var selected = candidates[selectedIndex];
            StartAction(selected);
            return selected.id;
        }

        /// <summary>
        /// Force a specific action (for external triggers).
        /// </summary>
        public void ForceAction(int actionId)
        {
            if (_actionMap.TryGetValue(actionId, out var config))
            {
                StartAction(config);
            }
        }

        // ============================================================
        // Phase Update
        // ============================================================

        /// <summary>
        /// Advance phase timer. Returns true when current action completes.
        /// </summary>
        public bool UpdatePhase(float deltaTime)
        {
            if (!_current.isActive) return true;

            _current.phaseElapsed += deltaTime;

            var phase = GetCurrentPhaseConfig();
            if (phase == null || _current.phaseElapsed >= phase.duration)
            {
                // Advance to next phase
                AdvancePhase();
            }

            return !_current.isActive;  // true if just completed
        }

        /// <summary>
        /// Get interpolated target values for the current phase.
        /// </summary>
        public Dictionary<string, float> GetCurrentTargets()
        {
            if (!_current.isActive) return null;

            var phase = GetCurrentPhaseConfig();
            if (phase == null) return null;

            float t = Mathf.Clamp01(_current.phaseElapsed / phase.duration);
            float easedT = EvaluateCurve(t, phase.curve);

            // If phase has no targets (empty "hold" etc), return empty
            if (phase.targets == null || phase.targets.Count == 0)
                return new Dictionary<string, float>();

            // Interpolate from phase start values to target values
            // For phase 0: from zero/neutral → phase.targets
            // For subsequent phases: from previous phase target → this phase target
            var result = new Dictionary<string, float>();
            var prevTargets = GetPreviousPhaseTargets();

            foreach (var kvp in phase.targets)
            {
                float from = prevTargets != null && prevTargets.ContainsKey(kvp.Key)
                    ? prevTargets[kvp.Key]
                    : 0f;
                result[kvp.Key] = Mathf.Lerp(from, kvp.Value, easedT);
            }

            // Carrying over params from previous phase that aren't in current phase
            if (prevTargets != null)
            {
                foreach (var kvp in prevTargets)
                {
                    if (!result.ContainsKey(kvp.Key))
                    {
                        // Carry over the target value of the previous phase (the "to" value)
                        result[kvp.Key] = kvp.Value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get current progress ratio (0~1) for the active phase.
        /// </summary>
        public float GetCurrentPhaseProgress()
        {
            if (!_current.isActive) return 0f;
            var phase = GetCurrentPhaseConfig();
            if (phase == null || phase.duration <= 0f) return 1f;
            return Mathf.Clamp01(_current.phaseElapsed / phase.duration);
        }

        /// <summary>
        /// Get total progress (0~1) across all phases.
        /// </summary>
        public float GetTotalProgress()
        {
            if (!_current.isActive || _current.config.phases == null || _current.config.phases.Count == 0)
                return 1f;

            float totalDuration = _current.config.phases.Sum(p => p.duration);
            if (totalDuration <= 0f) return 1f;

            float elapsed = _current.phaseElapsed;
            for (int i = 0; i < _current.phaseIndex; i++)
                elapsed += _current.config.phases[i].duration;

            return Mathf.Clamp01(elapsed / totalDuration);
        }

        // ============================================================
        // Lifecycle
        // ============================================================

        public void ResetCurrentAction()
        {
            _current.Reset();
        }

        public void UpdateCooldowns(float deltaTime)
        {
            if (_globalCooldownTimer > 0f)
                _globalCooldownTimer -= deltaTime;

            var keys = _cooldownTimers.Keys.ToList();
            foreach (var k in keys)
            {
                if (_cooldownTimers[k] > 0f)
                    _cooldownTimers[k] -= deltaTime;
            }
        }

        /// <summary>
        /// Get config by action id (for special action handling).
        /// </summary>
        public IdleActionConfig GetActionConfig(int actionId)
        {
            _actionMap.TryGetValue(actionId, out var config);
            return config;
        }

        // ============================================================
        // Internal
        // ============================================================

        private void StartAction(IdleActionConfig config)
        {
            _current.config = config;
            _current.phaseIndex = 0;
            _current.phaseElapsed = 0f;
            _current.isActive = true;

            _cooldownTimers[config.id] = config.cooldown;
            _globalCooldownTimer = Mathf.Max(3f, config.phases?.Sum(p => p.duration) ?? 3f) * 0.3f;

            Debug.Log($"[IdleActionScheduler] Started action: {config.displayName} (id={config.id})");
        }

        private void AdvancePhase()
        {
            if (_current.config.phases == null) return;

            _current.phaseIndex++;
            _current.phaseElapsed = 0f;

            if (_current.phaseIndex >= _current.config.phases.Count)
            {
                Debug.Log($"[IdleActionScheduler] Completed action: {_current.config.displayName}");
                _current.Reset();
            }
        }

        private ActionPhaseConfig GetCurrentPhaseConfig()
        {
            if (!_current.isActive || _current.config.phases == null) return null;
            if (_current.phaseIndex < 0 || _current.phaseIndex >= _current.config.phases.Count)
                return null;
            return _current.config.phases[_current.phaseIndex];
        }

        private Dictionary<string, float> GetPreviousPhaseTargets()
        {
            if (_current.config.phases == null || _current.phaseIndex <= 0) return null;
            int prevIdx = _current.phaseIndex - 1;
            if (prevIdx < _current.config.phases.Count)
                return _current.config.phases[prevIdx].targets;
            return null;
        }

        // ============================================================
        // Curve Evaluation
        // ============================================================

        private float EvaluateCurve(float t, string curveType)
        {
            switch (curveType)
            {
                case "linear":      return t;
                case "easeOut":     return 1f - Mathf.Pow(1f - t, 3f);
                case "easeIn":      return t * t * t;
                case "smooth":      return t * t * (3f - 2f * t);     // smoothstep
                case "hold":        return 0f;
                default:            return t;
            }
        }
    }
}
