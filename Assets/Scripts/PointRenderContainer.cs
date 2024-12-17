using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Common;
using Cysharp.Threading.Tasks;
using Jobs;
using Storage;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.VFX;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public class PointRenderContainer : MonoBehaviour {
    public Transform referenceTransform;

    public PointStorageBase storageBase;

    private void Awake() {
        storageBase.Init(this);
    }


    private void FixedUpdate() {
        if (referenceTransform) {
            var transformPosition = referenceTransform.position;
            storageBase.UpdatePlayerPosition(transformPosition);
        }
    }

    private void OnDestroy() {
        storageBase.Dispose();
    }

    public void ScheduleInsertData(PointInsertData data) {
        storageBase.Insert(data);
    }
}