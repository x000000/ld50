using System;
using System.Collections.Generic;
using UnityEngine;

namespace com.x0
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Animator))]
    public class SlowTower : TowerBase
    {
        private static readonly Dictionary<Transform, int> Targets = new();

        public static event EventHandler<bool> Affected;

        public static void Flush() => Targets.Clear();

        private float _size;
        public float Size
        {
            get => _size;
            set {
                _size = value;
                Collider.radius = value;
                FieldVisual.localScale = new Vector3(value * 2, value * 2, 1f);
            }
        }
        
        public CircleCollider2D Collider;
        public Transform FieldVisual;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (Targets.TryAdd(other.transform, 1)) {
                Affected?.Invoke(other.transform, true);
            } else {
                Targets[other.transform]++;
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var n = Targets[other.transform];
            if (n <= 1) {
                Targets.Remove(other.transform);
                Affected?.Invoke(other.transform, false);
            } else {
                Targets[other.transform] = n - 1;
            }
        }

        private void OnEnable()
        {
            Size = 1.6f;
            GetComponent<Animator>().enabled = true;
        }
    }
}
