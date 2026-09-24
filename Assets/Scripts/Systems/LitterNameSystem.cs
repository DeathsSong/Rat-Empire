using System;
using System.Collections.Generic;

namespace RatHabitat
{
    /// <summary>
    /// The stable, finite name pool used by birth events. Names are assigned
    /// to LitterData once and never chosen by UI refreshes.
    /// </summary>
    public static class LitterNameSystem
    {
        public static readonly string[] Names =
        {
            "Sunshine Litter", "Gumdrop Litter", "Honeybun Litter", "Moonbeam Litter",
            "Sprinkle Litter", "Marshmallow Litter", "Blueberry Litter", "Buttercup Litter",
            "Peaches Litter", "Jellybean Litter", "Cookie Crumb Litter", "Pudding Litter",
            "Cuddlebug Litter", "Dandelion Litter", "Cinnamon Litter", "Pumpkin Litter",
            "Waffle Litter", "Cupcake Litter", "Fizzy Litter", "Taffy Litter",
            "Peach Puff Litter", "Sugarplum Litter", "Snickerdoodle Litter", "Starshine Litter",
            "Berry Tart Litter", "Cozy Cloud Litter", "Little Sprout Litter", "Toffee Litter",
            "Lemon Drop Litter", "Poppyseed Litter", "Muffin Litter", "Twinkle Litter",
            "Caramel Litter", "Strawberry Shortcake Litter", "Bubblegum Litter", "Apricot Litter",
            "Churro Litter", "Butterscotch Litter", "Clover Litter", "Sweet Pea Litter",
            "Hazelnut Litter", "Raspberry Litter", "Bonbon Litter", "Fizzlet Litter",
            "Maple Litter", "Cotton Candy Litter", "Nibbles Litter", "S'more Litter",
            "Cherry Pie Litter", "Daisy Litter", "Nutmeg Litter", "Cocoa Puff Litter",
            "Peppermint Litter", "Raspberry Ripple Litter", "Tangerine Litter", "Pancake Litter",
            "Velvet Litter", "Sprinkle Pop Litter", "Peach Fizz Litter", "Honeycomb Litter",
            "Butterbean Litter", "Pecan Pie Litter", "Cinnamon Swirl Litter", "Lullaby Litter",
            "Tiny Toes Litter", "Firefly Litter", "Rainbow Litter", "Buzzy Bee Litter",
            "Candyfloss Litter", "Whiskers Litter", "Pipsqueak Litter", "Lollipop Litter",
            "Rosy Cheeks Litter", "Cozy Cocoa Litter", "Sugar Cookie Litter", "Dream Puff Litter",
            "Berry Bloom Litter", "Nutty Buddy Litter", "Golden Nugget Litter", "Huckleberry Litter",
            "Choco Chip Litter", "Sweet Tart Litter", "Peachy Keen Litter", "Wiggleworm Litter",
            "Snugglebug Litter", "Bonfire Litter", "Cherry Blossom Litter", "Mellow Yellow Litter",
            "Fizzy Pop Litter", "Mooncake Litter", "Tiny Taffy Litter", "Cloud Puff Litter",
            "Cinnamon Bun Litter", "Starry Sprout Litter", "Honey Drop Litter", "Pudding Pop Litter",
            "Berry Biscuit Litter", "Cozy Comet Litter", "Doodlebug Litter", "Sweetie Pie Litter",
        };

        public static void EnsureLitterNames(ColonySaveData save)
        {
            if (save == null) return;
            save.EnsureLists();
            foreach (var litter in save.litters)
            {
                if (litter != null && string.IsNullOrEmpty(litter.litterName)) AssignName(save, litter);
            }
        }

        public static string AssignName(ColonySaveData save, LitterData litter)
        {
            if (save == null || litter == null) return string.Empty;
            save.EnsureLists();
            if (!string.IsNullOrEmpty(litter.litterName)) return litter.litterName;

            if (save.usedLitterNames.Count >= Names.Length) save.usedLitterNames.Clear();

            int start = MathfAbs(StableHash(litter.id ?? string.Empty));
            for (int offset = 0; offset < Names.Length; offset++)
            {
                string candidate = Names[(start + offset) % Names.Length];
                if (save.usedLitterNames.Contains(candidate)) continue;
                save.usedLitterNames.Add(candidate);
                litter.litterName = candidate;
                return candidate;
            }

            // The list above is finite but this fallback keeps malformed or
            // hand-edited saves readable without exposing a raw litter ID.
            litter.litterName = Names[0];
            save.usedLitterNames.Add(litter.litterName);
            return litter.litterName;
        }

        public static string GetName(ColonySaveData save, string litterId)
        {
            if (save == null || string.IsNullOrEmpty(litterId)) return string.Empty;
            save.EnsureLists();
            foreach (var litter in save.litters)
            {
                if (litter != null && litter.id == litterId)
                {
                    if (string.IsNullOrEmpty(litter.litterName)) AssignName(save, litter);
                    return litter.litterName;
                }
            }
            return string.Empty;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
                return hash;
            }
        }

        private static int MathfAbs(int value)
        {
            if (value == int.MinValue) return int.MaxValue;
            return Math.Abs(value);
        }
    }
}
