namespace com.x0
{
    public interface IEnemy
    {
        public event EnemyEventHandler<IEnemy> Dies;
        public float Health { get; }
        void AcceptDamage(float damage);
    }

    public delegate void EnemyEventHandler<in T>(T enemy) where T : IEnemy;
}