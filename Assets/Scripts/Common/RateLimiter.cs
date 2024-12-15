using UnityEngine;

namespace Common {
    public class RateLimiter {
        private readonly float _rate;
        private float _nextTime;

        public RateLimiter(float rate) {
            _rate = rate;
        }

        public RateLimiter(int rate) {
            _rate = 1f / rate;
        }

        public bool Limit() {
            if (Time.time < _nextTime) return false;
            _nextTime = Time.time + _rate;
            return true;
        }
    }
}