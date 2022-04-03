using System.Collections.Generic;
using UnityEngine;

namespace com.x0
{
    public abstract class TowerBase : MonoBehaviour
    {
    }

    public abstract class FiringTower : TowerBase
    {
        public float Speed { get; set; } = 1f;
        
        private float _lastShot;

        protected bool TryShoot()
        {
            if (_lastShot + 1 / Speed < Time.fixedTime) {
                Shoot();
                return true;
            }
            return false;
        }

        protected virtual void Shoot()
        {
            _lastShot = Time.fixedTime;
        }

        protected Transform GetClosest(IEnumerable<Transform> targets)
        {
            var selfPos = transform.position;
            var minSqMag = float.MaxValue;
            Transform target = null;
            
            foreach (var item in targets) {
                var sqMag = (item.position - selfPos).sqrMagnitude;
                if (sqMag < minSqMag) {
                    minSqMag = sqMag;
                    target = item;
                }
            }

            return target;
        }
    }
}