using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace com.x0
{   
    [RequireComponent(typeof(Rigidbody2D), typeof(Animator))]
    public class AoeTower : FiringTower
    {
        private static readonly int DirectionParam = Animator.StringToHash("Direction");
        
        public Transform Head;
        public Object BulletTemplate;
        
        private readonly LinkedList<Transform> _targets = new();
        private Animator _animator;

        private void OnTriggerEnter2D(Collider2D other) => _targets.AddLast(other.transform);

        private void OnTriggerExit2D(Collider2D other) => _targets.Remove(other.transform);

        private void OnEnable()
        {
            Speed = .4f;
            _animator = GetComponent<Animator>();
        }

        private void Update() => TryShoot();

        protected override void Shoot()
        {
            if (_targets.Count > 0) {
                Transform target = null;
                var maxHealth = 0f;
                foreach (var item in _targets) {
                    var hp = item.GetComponent<IEnemy>().Health;
                    if (hp > maxHealth) {
                        maxHealth = hp;
                        target = item;
                    }
                }

                var dirVec = (Vector2) (target.position - transform.position);
                var angle  = Vector2.Angle(new Vector2(1, -1), dirVec);
                var index  = Mathf.CeilToInt(angle / 45) - 1;
                
                _animator.SetInteger(DirectionParam, index > 7 ? index - 7 : index);
                
                var go = (GameObject) Instantiate(BulletTemplate, Head.position, Quaternion.identity);
                var bullet = go.GetComponent<AoeBullet>();
                bullet.Size = .75f;
                bullet.Speed = 2f;
                bullet.Target = target.position;
                
                base.Shoot();
            }
        }
    }
}
