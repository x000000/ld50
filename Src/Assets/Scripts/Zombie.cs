using UnityEngine;

namespace com.x0
{
    public class Zombie : MonoBehaviour, IEnemy
    {
        private static readonly int AliveParam = Animator.StringToHash("Alive");

        public float Health { get; private set; } = 10f;

        public event EnemyEventHandler<IEnemy> Dies;

        public void AcceptDamage(float damage)
        {
            if (damage >= Health) {
                Die();
            } else {
                Health -= damage;
            }
        }

        private void Die()
        {
            GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            GetComponent<Animator>().SetBool(AliveParam, false);
            Dies?.Invoke(this);
            Dies = null;
        }

        private void Dispose() => Destroy(gameObject);
    }
}