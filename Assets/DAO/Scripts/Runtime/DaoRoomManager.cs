using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if DAO_HAS_META
using Oculus.Interaction.Locomotion;
#endif

namespace DAO
{
    [Serializable]
    public class DaoRoom
    {
        public string name = "Sala";
        [Tooltip("Objeto raiz da sala (modelo + interações). Só a sala atual fica ativa.")]
        public GameObject root;
        [Tooltip("Onde o jogador aparece ao entrar. O eixo Z azul indica para onde ele olha.")]
        public Transform spawnPoint;
        [Tooltip("Proposta de votação desta sala (opcional, usada pelas teclas de debug).")]
        public DaoProposal proposal;
        public UnityEvent onEnter = new UnityEvent();
        [Tooltip("Bom lugar para desfazer efeitos (ex.: DaoPlayerScale.ResetScaleInstant, DaoGravity.Restore).")]
        public UnityEvent onExit = new UnityEvent();
    }

    /// <summary>
    /// Troca entre salas com fade: liga a sala de destino, teleporta o rig para o spawn e desliga as outras.
    /// Todas as salas ficam na MESMA cena (longe umas das outras), o que evita travadas de carregamento no Quest.
    /// </summary>
    public class DaoRoomManager : MonoBehaviour
    {
        public static DaoRoomManager Instance { get; private set; }

        [Header("Jogador")]
        [Tooltip("Raiz do rig do template (ex.: [BuildingBlock] Camera Rig / OVRCameraRig).")]
        public Transform playerRig;
        [Tooltip("CenterEyeAnchor. Se vazio, usa a Camera.main.")]
        public Transform head;
        public DaoScreenFade screenFade;
        [Min(0f)] public float fadeDuration = 0.35f;

        [Header("Salas")]
        public List<DaoRoom> rooms = new List<DaoRoom>();
        public int startRoom = 0;
        [Tooltip("Desliga as salas que não estão em uso (melhor desempenho no Quest).")]
        public bool disableInactiveRooms = true;

        [Header("Eventos")]
        public UnityEvent<int> onRoomChanged = new UnityEvent<int>();

        public int CurrentRoomIndex { get; private set; } = -1;
        public DaoRoom CurrentRoom => CurrentRoomIndex >= 0 && CurrentRoomIndex < rooms.Count ? rooms[CurrentRoomIndex] : null;
        public bool IsTransitioning { get; private set; }

        void Awake()
        {
            Instance = this;
            ResolvePlayer();
        }

        void Start()
        {
            if (rooms.Count == 0) return;
            int start = Mathf.Clamp(startRoom, 0, rooms.Count - 1);
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].root != null) rooms[i].root.SetActive(i == start || !disableInactiveRooms);
            CurrentRoomIndex = start;
            // Espera um frame para o tracking do headset posicionar a cabeça antes de alinhar.
            StartCoroutine(InitialPlacement(start));
        }

        IEnumerator InitialPlacement(int start)
        {
            yield return null;
            if (rooms[start].spawnPoint != null) TeleportTo(rooms[start].spawnPoint);
            rooms[start].onEnter.Invoke();
            onRoomChanged.Invoke(start);
        }

        public void ResolvePlayer()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (playerRig == null && head != null) playerRig = head.root;
        }

        public void GoToRoom(int index)
        {
            if (IsTransitioning || index == CurrentRoomIndex || index < 0 || index >= rooms.Count) return;
            StartCoroutine(Transition(index));
        }

        public void GoToRoomByName(string roomName)
        {
            int i = rooms.FindIndex(r => string.Equals(r.name, roomName, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) GoToRoom(i);
            else Debug.LogWarning($"[DAO] Sala '{roomName}' não existe.");
        }

        public void NextRoom() => GoToRoom((CurrentRoomIndex + 1) % rooms.Count);
        public void PreviousRoom() => GoToRoom((CurrentRoomIndex - 1 + rooms.Count) % rooms.Count);

        IEnumerator Transition(int index)
        {
            IsTransitioning = true;
            if (screenFade != null) yield return screenFade.FadeTo(1f, fadeDuration);

            var from = CurrentRoom;
            from?.onExit.Invoke();

            var to = rooms[index];
            if (to.root != null) to.root.SetActive(true);
            if (to.spawnPoint != null) TeleportTo(to.spawnPoint);
            if (disableInactiveRooms && from?.root != null) from.root.SetActive(false);

            CurrentRoomIndex = index;
            to.onEnter.Invoke();
            onRoomChanged.Invoke(index);

            yield return null; // um frame preto para o tracking e o locomotor da Meta assentarem
            yield return null;
            if (screenFade != null) yield return screenFade.FadeTo(0f, fadeDuration);
            IsTransitioning = false;
        }

        /// <summary>
        /// Move o rig para que a CABEÇA fique sobre o alvo e olhando na direção do alvo
        /// (compensa o quanto a pessoa andou/girou no espaço físico).
        /// </summary>
        public void TeleportTo(Transform target)
        {
            ResolvePlayer();
            if (playerRig == null || target == null) return;

#if DAO_HAS_META
            // Rig do Meta Interaction SDK: o jogador segue uma cápsula (FirstPersonLocomotor). Mover o rig "na mão"
            // faria a cápsula puxar o jogador de volta, então usamos o mesmo evento de teleporte absoluto da Meta.
            var locomotor = playerRig.GetComponentInChildren<FirstPersonLocomotor>();
            if (locomotor != null && locomotor.isActiveAndEnabled)
            {
                var pose = new Pose(target.position, Quaternion.Euler(0f, target.eulerAngles.y, 0f));
                locomotor.HandleLocomotionEvent(new LocomotionEvent(GetInstanceID(), pose,
                    LocomotionEvent.TranslationType.Absolute, LocomotionEvent.RotationType.Absolute));
                return; // aplicado pelo locomotor no fim do frame
            }
#endif

            var cc = playerRig.GetComponentInChildren<UnityEngine.CharacterController>();
            bool ccWasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;

            if (head != null)
            {
                float yawDelta = Mathf.DeltaAngle(head.eulerAngles.y, target.eulerAngles.y);
                playerRig.RotateAround(head.position, Vector3.up, yawDelta);
                Vector3 delta = target.position - head.position;
                delta.y = target.position.y - playerRig.position.y;
                playerRig.position += delta;
            }
            else
            {
                playerRig.SetPositionAndRotation(target.position, Quaternion.Euler(0f, target.eulerAngles.y, 0f));
            }

            Physics.SyncTransforms();
            if (cc != null) cc.enabled = ccWasEnabled;
        }
    }
}
