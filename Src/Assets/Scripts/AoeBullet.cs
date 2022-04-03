using System;
using System.Collections.Generic;
using UnityEngine;

namespace com.x0
{
    [RequireComponent(typeof(Animator))]
    public class AoeBullet : MonoBehaviour
    {
        private static readonly int BlownParam = Animator.StringToHash("Blown");

        private float _size;
        public float Size
        {
            get => _size;
            set {
                _size = value;
                Collider.radius = value;
                BlowVisual.localScale = new Vector3(value * 2, value * 2, 1f);
            }
        }

        public float Speed { get; set; }
        public Vector3 Target { get; set; }

        public CircleCollider2D Collider;
        public Transform BlowVisual;
        
        private readonly LinkedList<Transform> _targets = new();
        
        private Vector3 _origin;
        private Vector3 _groundPos;
        private Func<Vector3, float> _curve;
        private bool _blown;

        private void OnTriggerEnter2D(Collider2D other) => _targets.AddLast(other.transform);

        private void OnTriggerExit2D(Collider2D other) => _targets.Remove(other.transform);

        private void Start()
        {
            _groundPos = _origin = transform.position;
            _curve = CurveFn(_origin.x, _origin.y, Target.x, Target.y);
            /*
            var n = 16;
            var x = _origin.x;
            var dir = (Target.x - x) / n;
            
            for (var i = 1; i < n; i++) {
                var p1 = new Vector3(x, _curve(x));
                var p2 = new Vector3(x += dir, _curve(x));
                
                Debug.DrawLine(p1, p2, Color.green, 3);
            }
            */
        }

        private void Update()
        {
            if (_blown) {
                return;
            }
            
            var dir = Target - _origin;
            if (dir.sqrMagnitude <= (_groundPos - _origin).sqrMagnitude) {
                foreach (var target in _targets) {
                    var rdist = Size - (Target - target.position).magnitude;
                    target.GetComponent<IEnemy>()?.AcceptDamage((rdist / Size) * (rdist / Size) * 6f);
                }
                GetComponent<Animator>().SetBool(BlownParam, true);
                _blown = true;
                return;
            }

            _groundPos += dir * (Time.deltaTime / Speed);
            transform.position = new Vector3(_groundPos.x, _curve(_groundPos));
        }

        private void Dispose() => Destroy(gameObject);

        private Func<Vector3, float> CurveFn(float x1, float y1, float x2, float y2)
        {
            return CurveFn(x1, y1, x2, y2, (x1 + x2) / 2, Mathf.Max(y1, y2) + 1);
        }
        
        private Func<Vector3, float> CurveFn(float x1, float y1, float x2, float y2, float x3, float y3)
        {
            var den = (x1 - x2) * (x1 - x3) * (x2 - x3);
            var a   = (x3 * (y2 - y1) + x2 * (y1 - y3) + x1 * (y3 - y2)) / den;
            var b   = (x3 * x3 * (y1 - y2) + x2 * x2 * (y3 - y1) + x1 * x1 * (y2 - y3)) / den;
            var c   = (x2 * x3 * (x2 - x3) * y1 + x3 * x1 * (x3 - x1) * y2 + x1 * x2 * (x1 - x2) * y3) / den;

            if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c)) {
                var fn = CurveFn(x1, y1, x2 + (y2 - y1), y2);
                return vec => fn(new Vector3(x1 + (vec.y - y1), vec.y));
            }
            return vec => a * vec.x * vec.x + b * vec.x + c;
        }
    }
}