using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace Common {
    public struct GridHashList<T> where T : unmanaged, IHasPosition, IEquatable<T> {
        public float worldSize;
        public NativeParallelMultiHashMap<int3, T> hash;

        public GridHashList(float worldSize, Allocator allocator) {
            this.worldSize = worldSize;
            hash = new NativeParallelMultiHashMap<int3, T>(0, allocator);
        }

        public NativeParallelMultiHashMap<int3, T>.KeyValueEnumerator GetEnumerator() {
            return hash.GetEnumerator();
        }

        public NativeParallelMultiHashMap<int3, T>.Enumerator GetValueEnumerator(float3 position) {
            return hash.GetValuesForKey(GetKey(position));
        }

        public NativeParallelMultiHashMap<int3, T>.Enumerator GetValueEnumerator(int3 key) {
            return hash.GetValuesForKey(GetKey(key));
        }

        public NativeList<T> GetValues(int3 key, Allocator allocator) {
            var list = new NativeList<T>(allocator);
            var enumerator = hash.GetValuesForKey(key);
            while (enumerator.MoveNext()) {
                list.Add(enumerator.Current);
            }

            return list;
        }

        public void Dispose() {
            hash.Dispose();
        }

        public bool Insert(T value, float size) {
            var pos = value.GetPosition();
            var key = GetKey(pos);
            if (ClosestValue(key, pos, size).hasValue) return false;
            hash.Add(key, value);
            return true;
        }

        public Optional ClosestValue(float3 position, float size) {
            return ClosestValue(GetKey(position), position, size);
        }

        public Optional ClosestValue(int3 key, float3 position, float size) {
            var optional = KeyDistanceCheck(key, position, size);
            if (optional.hasValue) return optional;

            var min = KeyToPosition(key);
            var max = min + new float3(worldSize);

            if (position.x - size < min.x) {
                optional = KeyDistanceCheck(key - new int3(1, 0, 0), position, size);
                if (optional.hasValue) return optional;
            } else if (position.x + size > max.x) {
                optional = KeyDistanceCheck(key + new int3(1, 0, 0), position, size);
                if (optional.hasValue) return optional;
            }

            if (position.y - size < min.y) {
                optional = KeyDistanceCheck(key - new int3(0, 1, 0), position, size);
                if (optional.hasValue) return optional;
            } else if (position.y + size > max.y) {
                optional = KeyDistanceCheck(key + new int3(0, 1, 0), position, size);
                if (optional.hasValue) return optional;
            }

            if (position.z - size < min.z) {
                optional = KeyDistanceCheck(key - new int3(0, 0, 1), position, size);
                if (optional.hasValue) return optional;
            } else if (position.z + size > max.z) {
                optional = KeyDistanceCheck(key + new int3(0, 0, 1), position, size);
                if (optional.hasValue) return optional;
            }

            return new Optional();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Optional KeyDistanceCheck(int3 key, float3 pos, float dist) {
            var enumerator = hash.GetValuesForKey(key);
            while (enumerator.MoveNext()) {
                var current = enumerator.Current;
                if (math.distance(current.GetPosition(), pos) < dist) {
                    return new Optional {
                        hasValue = true,
                        value = current
                    };
                }
            }

            return new Optional();
        }

        public void Insert(T value) {
            hash.Add(GetKey(value), value);
        }

        public void Remove(T value) {
            hash.Remove(GetKey(value), value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int3 GetKey(T value) {
            return GetKey(value.GetPosition());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int3 GetKey(float3 value) {
            return new int3(math.floor(value / worldSize));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 KeyToPosition(int3 key) {
            return new float3(key) * worldSize;
        }

        public struct Optional {
            public bool hasValue;
            public T value;
        }
    }
}