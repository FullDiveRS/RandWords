using System;
using System.Collections.Generic;

namespace RandWords
{
    /// <summary>
    /// Deterministic PRNG core of RandWords.
    /// Implements SplitMix64 with pure 64-bit integer arithmetic. The Python port
    /// uses the exact same algorithm, so both languages emit identical random
    /// streams for the same seed. All selections use integer math (no floats).
    /// </summary>
    public sealed class Rand
    {
        private const ulong Golden = 0x9E3779B97F4A7C15UL;
        private const ulong Mix1 = 0xBF58476D1CE4E5B9UL;
        private const ulong Mix2 = 0x94D049BB133111EBUL;

        private ulong _state;

        public Rand(ulong seed)
        {
            _state = seed;
        }

        public Rand(long seed) : this(unchecked((ulong)seed))
        {
        }

        /// <summary>Next raw 64-bit value (SplitMix64).</summary>
        public ulong NextU64()
        {
            _state += Golden;
            ulong z = _state;
            z = (z ^ (z >> 30)) * Mix1;
            z = (z ^ (z >> 27)) * Mix2;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform integer in [0, bound). Uses modulo (deterministic everywhere).</summary>
        public int NextInt(int bound)
        {
            if (bound <= 0)
                throw new ArgumentOutOfRangeException(nameof(bound), "bound must be positive");
            return (int)(NextU64() % (ulong)bound);
        }

        /// <summary>Uniform pick from a non-empty list.</summary>
        public T Pick<T>(IReadOnlyList<T> items)
        {
            return items[NextInt(items.Count)];
        }

        /// <summary>Index selected by cumulative walk over non-negative integer weights.</summary>
        public int WeightedIndex(IReadOnlyList<int> weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            if (total <= 0)
                throw new ArgumentException("weights must sum to a positive value");
            int v = NextInt(total);
            int cum = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                cum += weights[i];
                if (cum > v) return i;
            }
            return weights.Count - 1;
        }

        /// <summary>True with <paramref name="percent"/> probability (percent in [0, 100]).</summary>
        public bool Chance(int percent)
        {
            return NextInt(100) < percent;
        }
    }
}
