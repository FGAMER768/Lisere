using System.Numerics;

namespace Lisere.Core.Imaging;

internal sealed class PythonRandom
{
    private const int StateSize = 624;
    private const int Period = 397;
    private const uint Matrix = 0x9908b0df;
    private const uint UpperMask = 0x80000000;
    private const uint LowerMask = 0x7fffffff;

    private readonly uint[] _state = new uint[StateSize];
    private int _index = StateSize;

    public PythonRandom(int seed)
    {
        Seed(seed);
    }

    public int RandInt(int minimum, int maximum)
    {
        var width = maximum - minimum + 1;
        var bits = 32 - BitOperations.LeadingZeroCount((uint)(width - 1));

        while (true)
        {
            var value = NextUInt32() >> (32 - bits);
            if (value < (uint)width)
            {
                return minimum + (int)value;
            }
        }
    }

    private void Seed(int seed)
    {
        unchecked
        {
            _state[0] = 19650218u;
            for (uint i = 1; i < StateSize; i++)
            {
                _state[i] = (1812433253u * (_state[i - 1] ^ (_state[i - 1] >> 30))) + i;
            }

            var key = new[] { (uint)seed };
            var stateIndex = 1;
            var keyIndex = 0;

            for (var remaining = Math.Max(StateSize, key.Length); remaining > 0; remaining--)
            {
                _state[stateIndex] =
                    (_state[stateIndex] ^ ((_state[stateIndex - 1] ^ (_state[stateIndex - 1] >> 30)) * 1664525u)) +
                    key[keyIndex] + (uint)keyIndex;
                stateIndex++;
                keyIndex++;
                if (stateIndex >= StateSize)
                {
                    _state[0] = _state[StateSize - 1];
                    stateIndex = 1;
                }

                if (keyIndex >= key.Length)
                {
                    keyIndex = 0;
                }
            }

            for (var remaining = StateSize - 1; remaining > 0; remaining--)
            {
                _state[stateIndex] =
                    (_state[stateIndex] ^ ((_state[stateIndex - 1] ^ (_state[stateIndex - 1] >> 30)) * 1566083941u)) -
                    (uint)stateIndex;
                stateIndex++;
                if (stateIndex >= StateSize)
                {
                    _state[0] = _state[StateSize - 1];
                    stateIndex = 1;
                }
            }

            _state[0] = 0x80000000u;
            _index = StateSize;
        }
    }

    private uint NextUInt32()
    {
        unchecked
        {
            if (_index >= StateSize)
            {
                for (var i = 0; i < StateSize; i++)
                {
                    var value = (_state[i] & UpperMask) | (_state[(i + 1) % StateSize] & LowerMask);
                    _state[i] = _state[(i + Period) % StateSize] ^ (value >> 1) ^ ((value & 1) != 0 ? Matrix : 0);
                }

                _index = 0;
            }

            var result = _state[_index++];
            result ^= result >> 11;
            result ^= (result << 7) & 0x9d2c5680;
            result ^= (result << 15) & 0xefc60000;
            result ^= result >> 18;
            return result;
        }
    }
}
