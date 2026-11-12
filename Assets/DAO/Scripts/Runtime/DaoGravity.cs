using UnityEngine;

namespace DAO
{
    /// <summary>
    /// Muda a gravidade (ex.: cozinha em gravidade zero, panelas flutuando).
    /// Os objetos precisam de Rigidbody (o Grab building block já adiciona) e a sala precisa de colliders nas paredes.
    /// </summary>
    public class DaoGravity : MonoBehaviour
    {
        [Tooltip("Objetos com Rigidbody abaixo daqui recebem um empurrãozinho ao entrar em gravidade zero.")]
        public Transform affectedRoot;
        public float floatImpulse = 0.35f;
        public float spin = 0.6f;

        Vector3 defaultGravity;

        void Awake() => defaultGravity = Physics.gravity;

        public void SetMultiplier(float m)
        {
            Physics.gravity = defaultGravity * m;
            Kick(0f);
        }

        public void ZeroG()
        {
            Physics.gravity = Vector3.zero;
            Kick(floatImpulse);
        }

        public void Moon() => SetMultiplier(0.165f);
        public void Restore() => SetMultiplier(1f);

        void Kick(float impulse)
        {
            if (affectedRoot == null) return;
            foreach (var rb in affectedRoot.GetComponentsInChildren<Rigidbody>())
            {
                if (rb.isKinematic) continue;
                rb.WakeUp();
                if (impulse > 0f)
                {
                    rb.AddForce(Vector3.up * impulse + Random.insideUnitSphere * impulse * 0.4f, ForceMode.VelocityChange);
                    rb.AddTorque(Random.insideUnitSphere * spin, ForceMode.VelocityChange);
                }
            }
        }

        void OnDestroy() => Physics.gravity = defaultGravity;
    }
}
