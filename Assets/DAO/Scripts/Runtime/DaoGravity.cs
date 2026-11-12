using System.Collections.Generic;
using UnityEngine;

namespace DAO
{
    /// <summary>
    /// Muda a gravidade (ex.: cozinha em gravidade zero, panelas flutuando).
    /// Os objetos precisam de Rigidbody (o Grab do Meta SDK já usa) e a sala precisa de colliders nas paredes.
    /// Guarda a posição inicial dos objetos para o reinício do jogo.
    /// </summary>
    public class DaoGravity : MonoBehaviour
    {
        [Tooltip("Objetos com Rigidbody abaixo daqui recebem um empurrãozinho ao entrar em gravidade zero.")]
        public Transform affectedRoot;
        public float floatImpulse = 0.35f;
        public float spin = 0.6f;

        static Vector3 defaultGravity = new Vector3(0f, -9.81f, 0f);
        readonly List<(Rigidbody rb, Vector3 pos, Quaternion rot)> initial = new List<(Rigidbody, Vector3, Quaternion)>();

        void Awake()
        {
            if (affectedRoot == null) return;
            foreach (var rb in affectedRoot.GetComponentsInChildren<Rigidbody>(true))
                initial.Add((rb, rb.transform.position, rb.transform.rotation));
        }

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

        /// <summary>Coloca os objetos de volta onde estavam no começo.</summary>
        public void ResetProps()
        {
            foreach (var (rb, pos, rot) in initial)
            {
                if (rb == null) continue;
                rb.transform.SetPositionAndRotation(pos, rot);
                if (!rb.isKinematic)
                {
#if UNITY_6000_0_OR_NEWER
                    rb.linearVelocity = Vector3.zero;
#else
                    rb.velocity = Vector3.zero;
#endif
                    rb.angularVelocity = Vector3.zero;
                }
            }
            Physics.SyncTransforms();
        }

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
