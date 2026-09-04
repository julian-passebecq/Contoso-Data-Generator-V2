#nullable enable

using System;

namespace DatabaseGenerator.Forge.Generation;

/// <summary>
/// SplitMix64-based PRNG whose output is stable across .NET versions and operating systems.
/// It is intentionally separate from the upstream generator's seeded Random instances.
/// </summary>
internal sealed class StableRandom
{
    private ulong _state;

    public StableRandom(int seed)
    {
        _state = unchecked((ulong)(uint)seed) ^ 0xA0761D6478BD642FUL;
    }

    public ulong NextUInt64()
    {
        var value = unchecked(_state += 0x9E3779B97F4A7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    public int NextInt(int exclusiveMaximum)
    {
        if (exclusiveMaximum <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        return (int)(NextUInt64() % (uint)exclusiveMaximum);
    }

    public decimal NextMoney(decimal minimum, decimal maximum)
    {
        var fraction = (decimal)(NextUInt64() >> 11) / 9007199254740992m;
        return decimal.Round(minimum + ((maximum - minimum) * fraction), 2, MidpointRounding.AwayFromZero);
    }
}
