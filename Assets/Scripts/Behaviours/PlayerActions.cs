using UnityEngine;
using UnityEngine.InputSystem;

namespace Behaviours {
    public class PlayerActions : MonoBehaviour {
        public InputActionReference shootAction;
        public InputActionReference optimizeAction;

        public Transform cameraBone;

        private void Awake() {
            Application.targetFrameRate = 144;
        }

        private void Update() {
            if (shootAction.action.IsPressed()) {
                var pos = cameraBone.position;
                var forward = cameraBone.forward;
                PointRaycaster.Instance.Brush(pos, forward, 60, 64);
            }

            // if (optimizeAction.action.triggered) {
            //     var tree = FindAnyObjectByType<PointRenderContainer>().tree;
            //     Debug.LogWarning(
            //         $"Optimizing tree. Before: Average depth: {tree.GetAverageDepth()} Max depth: {tree.GetMaxDepth()} Points: {tree.points.Length}");
            //     var stopwatch = Stopwatch.StartNew();
            //     // var newTree = TreeBalancing.Begin(tree.points);
            //     // stopwatch.Stop();
            //     // Debug.LogWarning(
            //     //     $"Optimized Tree in  {stopwatch.ElapsedMilliseconds}ms! (fully). After: Average depth: {newTree.GetAverageDepth()} Max depth: {newTree.GetMaxDepth()} Points: {newTree.points.Length}");
            //     // newTree.Dispose();
            //     // stopwatch.Restart();
            //     // var newTree = TreeBalancing.BeginWithPool(tree.points, 1024);
            //     // stopwatch.Stop();
            //     // Debug.LogWarning(
            //     //     $"Optimized Tree in  {stopwatch.ElapsedMilliseconds}ms! (pooled). After: Average depth: {newTree.GetAverageDepth()} Max depth: {newTree.GetMaxDepth()} Points: {newTree.points.Length}");
            //     // newTree.Dispose();
            // }
        }
    }
}