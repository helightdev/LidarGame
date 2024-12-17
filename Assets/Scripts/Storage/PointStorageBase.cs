using System;
using Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Storage {
    public abstract class PointStorageBase : MonoBehaviour, IDisposable {

        public abstract void Init(PointRenderContainer container);
        public abstract void Insert(PointInsertData data);
        public abstract void UpdatePlayerPosition(float3 position);

        public abstract void Dispose();
    }
}