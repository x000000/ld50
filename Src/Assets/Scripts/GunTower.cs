using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace com.x0
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class GunTower : FiringTower
    {
        public Transform Head;
        public Object ArrowTemplate;

        private readonly LinkedList<Transform> _targets = new();

        private void OnTriggerEnter2D(Collider2D other) => _targets.AddLast(other.transform);

        private void OnTriggerExit2D(Collider2D other) => _targets.Remove(other.transform);

        private void Update() => TryShoot();

        protected override void Shoot()
        {
            var target = GetClosest(_targets);
            if (target != null) {
                var go = (GameObject) Instantiate(ArrowTemplate, Head.position, Quaternion.identity);
                var bullet = go.GetComponent<ArrowBullet>();
                bullet.Speed = 16f;
                bullet.Target = target;
                
                base.Shoot();
            }
        }
    }
}
