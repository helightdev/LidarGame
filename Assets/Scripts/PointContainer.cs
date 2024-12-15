using System;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DefaultNamespace {
    public class PointContainer : MonoBehaviour {
        
        public BurstKdTree tree;

        private void Awake() {
            // tree = new BurstKdTree(5, Allocator.Persistent);
            //
            // TreeInsertJob.Run(new [] {
            //     new PointInsertData {
            //         position = new float3(1,0,0),
            //         color = new float3(1,1,1),
            //         density = 0,
            //         timestamp = -15
            //     },
            //     new PointInsertData {
            //         position = new float3(0,1,0),
            //         color = new float3(1,1,1),
            //         density = 0,
            //         timestamp = 0
            //     },
            //     new PointInsertData {
            //         position = new float3(0,0,1),
            //         color = new float3(1,1,1),
            //         density = 0,
            //         timestamp = -15
            //     },
            // },tree);
            //
            // TreeInsertJob.Run(new [] {
            //     new PointInsertData {
            //         position = new float3(0,0,0),
            //         color = new float3(1,1,1),
            //         density = 0,
            //         timestamp = 0
            //     },
            //     new PointInsertData {
            //         position = new float3(1,1,1),
            //         color = new float3(1,1,1),
            //         density = 0,
            //         timestamp = 0
            //     },
            // },tree);
            //
            // var stringBuilder = new StringBuilder();
            // foreach (var point in tree.points) {
            //     stringBuilder.AppendLine($"{point.position}\n");
            // }
            // Debug.Log(stringBuilder.ToString());
            //
            // var nearest = tree.FindNearest(new float3(1, 1, 1.5f));
            // Debug.Log($"Nearest: {tree.points[nearest].position}");
            //
            // new TreeCleanupJob {
            //     tree = tree,
            //     currentTime = 0,
            //     maxAge = 10
            // }.Schedule().Complete();
            //
            // var stringBuilder2 = new StringBuilder();
            // foreach (var point in tree.points) {
            //     stringBuilder2.AppendLine($"{point.position}\n");
            // }
            // Debug.Log(stringBuilder2.ToString());
            //
            // var nearest2 = tree.FindNearest(new float3(0, 0, 1));
            // Debug.Log($"Nearest: {tree.points[nearest2].position}");

        }
    }
}