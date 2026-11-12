using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// Porta/escada que leva a outra sala.
    /// Dois jeitos de usar:
    ///  1) Botão: ligue o evento do Poke (ou DaoButton.onPress) em DaoDoor.Use().
    ///  2) Zona: marque "Proximity Trigger" e ajuste o BoxCollider (Is Trigger). Quando a cabeça entra, troca de sala.
    ///     A zona funciona mesmo com o jogador minúsculo, porque usa a posição da cabeça e não física.
    /// </summary>
    public class DaoDoor : MonoBehaviour
    {
        [Tooltip("Índice da sala de destino na lista do DaoRoomManager.")]
        public int targetRoom;
        [Tooltip("Troca de sala quando a cabeça do jogador entra no BoxCollider abaixo.")]
        public bool proximityTrigger;
        public BoxCollider zone;
        [Tooltip("Segundos depois de chegar na sala em que a zona fica ignorada.")]
        public float armDelay = 1.5f;
        public UnityEvent onUsed = new UnityEvent();

        float armedAt;

        void OnEnable() => armedAt = Time.time + armDelay;

        public void Use()
        {
            var mgr = DaoRoomManager.Instance;
            if (mgr == null) { Debug.LogWarning("[DAO] Não há DaoRoomManager na cena."); return; }
            if (mgr.IsTransitioning) return;
            onUsed.Invoke();
            mgr.GoToRoom(targetRoom);
        }

        void Update()
        {
            if (!proximityTrigger || zone == null || Time.time < armedAt) return;
            var mgr = DaoRoomManager.Instance;
            if (mgr == null || mgr.head == null || mgr.IsTransitioning) return;

            Vector3 local = zone.transform.InverseTransformPoint(mgr.head.position) - zone.center;
            Vector3 half = zone.size * 0.5f;
            if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
            {
                armedAt = Time.time + armDelay;
                Use();
            }
        }

        void OnDrawGizmos()
        {
            if (zone == null) return;
            Gizmos.color = proximityTrigger ? new Color(0f, 1f, 0.5f, 0.35f) : new Color(1f, 1f, 1f, 0.15f);
            Gizmos.matrix = zone.transform.localToWorldMatrix;
            Gizmos.DrawCube(zone.center, zone.size);
        }
    }
}
