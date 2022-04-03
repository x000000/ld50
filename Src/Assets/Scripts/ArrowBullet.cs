using UnityEngine;

namespace com.x0
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class ArrowBullet : MonoBehaviour
    {
        public float Speed { get; set; }
        public Transform Target { get; set; }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.transform == Target) {
                other.GetComponent<IEnemy>()?.AcceptDamage(5f);
                Destroy(gameObject);
            }
        }

        private void Update()
        {
            var spos = transform.position;
            var tpos = Target.position;
            // just to hit zombie body, not the pivot under zombie's legs
            tpos.y += .4f;
            var dir = tpos - spos;
            
            var pos = spos + dir.normalized * (Speed * Time.deltaTime);
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.forward, dir));
        }
    }
}