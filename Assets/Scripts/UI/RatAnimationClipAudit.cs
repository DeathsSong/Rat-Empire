using System;
using System.Collections.Generic;
using UnityEngine;

namespace RatHabitat
{
    /// <summary>
    /// Editor-generated facts about the imported Hand Painted Rat clips.
    /// Runtime UI reads this asset instead of using editor-only AnimationUtility
    /// APIs or guessing whether a clip contains bone motion.
    /// </summary>
    [CreateAssetMenu(fileName = "HandPaintedRatAnimationAudit", menuName = "Rat Empire/Hand Painted Rat Animation Audit")]
    public sealed class RatAnimationClipAudit : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string clipName;
            public bool available;
            public int floatBindings;
            public int animatedBindings;
            public int animatedBodyBindings;
            public float lengthSeconds;
        }

        public List<Entry> entries = new List<Entry>();

        public Entry Find(string clipName)
        {
            if (string.IsNullOrEmpty(clipName) || entries == null) return null;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry != null && string.Equals(entry.clipName, clipName, StringComparison.Ordinal)) return entry;
            }
            return null;
        }
    }
}
