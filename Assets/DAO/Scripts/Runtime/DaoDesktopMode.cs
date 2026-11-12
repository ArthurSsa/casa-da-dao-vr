using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && DAO_HAS_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace DAO
{
    /// <summary>
    /// Teste sem óculos: se nenhum headset estiver ativo ao dar Play, troca o rig VR por uma câmera de
    /// primeira pessoa controlada por mouse e teclado. Toda a lógica (portais, votação, escala, gravidade) continua igual.
    ///   WASD / setas = andar · Shift = correr · botão direito + mouse = olhar
    ///   clique esquerdo = apertar o botão sob a mira · Z / X = votar A / B · 1 2 3 = ir para a sala
    ///   F1 = forçar o modo desktop se ele não ligar sozinho
    /// </summary>
    public class DaoDesktopMode : MonoBehaviour
    {
        public enum Mode { Automatico, SempreLigado, Desligado }

        /// <summary>Os testes automáticos do rig VR desligam o modo automático.</summary>
        public static bool AllowAuto = true;

        public Mode mode = Mode.Automatico;
        public float eyeHeight = 1.6f;
        public float moveSpeed = 1.8f;
        public float lookSpeed = 2.5f;
        public bool showHelp = true;

        public bool Active { get; private set; }
        public Transform DesktopRig => rig;
        public Camera DesktopCamera => cam;

        Transform rig;
        Camera cam;
        GameObject xrRig;
        float yaw, pitch;
        string lastMessage = "";
        float lastMessageTime;

        IEnumerator Start()
        {
            if (mode == Mode.Desligado) yield break;
            yield return null; // deixa o XR tentar iniciar e o DaoRoomManager posicionar o jogador
            yield return null;
            if (mode == Mode.SempreLigado) { Activate(); yield break; }
            if (!AllowAuto) yield break;

            // No Editor o plugin da Meta pode se dizer "ativo" mesmo sem óculos. O que vale é existir
            // uma cabeça (headset) sendo rastreada; espera até 1,5 s por ela antes de cair no modo desktop.
            for (float t = 0f; t < 1.5f; t += Time.unscaledDeltaTime)
            {
                if (HeadsetTracking()) yield break;
                yield return null;
            }
            Activate();
        }

        static bool HeadsetTracking()
        {
            var head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            if (!head.isValid) return false;
            return !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || tracked;
        }

        public void Activate()
        {
            var mgr = DaoRoomManager.Instance;
            if (mgr == null || Active) return;
            mgr.ResolvePlayer();
            xrRig = mgr.playerRig != null ? mgr.playerRig.gameObject : null;

            rig = new GameObject("Desktop Rig (mouse e teclado)").transform;
            var camGO = new GameObject("Desktop Camera");
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 75f;
            camGO.AddComponent<AudioListener>();
            camGO.transform.SetParent(rig, false);
            camGO.transform.localPosition = Vector3.up * eyeHeight;

            Transform oldHead = mgr.head;
            if (xrRig != null)
            {
                rig.position = xrRig.transform.position;
                // A tela de conclusão fica presa à cabeça: leva para a câmera nova.
                if (oldHead != null)
                    for (int i = oldHead.childCount - 1; i >= 0; i--)
                    {
                        var child = oldHead.GetChild(i);
                        if (child.name.StartsWith("Desafio")) child.SetParent(cam.transform, false);
                    }
                xrRig.SetActive(false);
            }

            mgr.playerRig = rig;
            mgr.head = cam.transform;
            var fade = FindFirstObjectByType<DaoScreenFade>();
            if (fade != null) fade.head = cam.transform;
            var scaler = FindFirstObjectByType<DaoPlayerScale>();
            if (scaler != null) { scaler.playerRig = rig; scaler.head = cam.transform; }

            // Botões de Poke não têm collider (o Poke usa superfícies); para o clique do mouse, cria um.
            foreach (var b in FindObjectsByType<DaoButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.GetComponentInChildren<Collider>(true) == null) AddFittedCollider(b.gameObject);

            if (mgr.CurrentRoom != null && mgr.CurrentRoom.spawnPoint != null) mgr.TeleportTo(mgr.CurrentRoom.spawnPoint);
            yaw = rig.eulerAngles.y;
            mgr.onRoomChanged.AddListener(_ => { yaw = rig.eulerAngles.y; pitch = 0f; });
            Active = true;
            Debug.Log("[DAO] Modo desktop ligado (nenhum headset ativo). WASD anda, botão direito olha, clique aperta botões.");
        }

        static void AddFittedCollider(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var box = go.AddComponent<BoxCollider>();
            if (renderers.Length == 0) return;
            var t = go.transform;
            Bounds local = new Bounds(t.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    local.Encapsulate(t.InverseTransformPoint(corner));
                }
            }
            box.center = local.center;
            box.size = local.size;
        }

        void Update()
        {
            if (!Active || rig == null) return;
            var mgr = DaoRoomManager.Instance;
            bool busy = mgr != null && mgr.IsTransitioning;

            // Olhar (botão direito segurado)
            if (MouseHeld(1))
            {
                Vector2 d = MouseDelta();
                yaw += d.x * lookSpeed;
                pitch = Mathf.Clamp(pitch - d.y * lookSpeed, -80f, 80f);
            }
            if (!busy)
            {
                rig.rotation = Quaternion.Euler(0f, yaw, 0f);
                cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

                // Andar no plano (a escala do jogador também escala o passo)
                Vector2 m = MoveInput();
                float speed = moveSpeed * (RunHeld() ? 2f : 1f) * rig.localScale.x;
                Vector3 dir = rig.forward * m.y + rig.right * m.x;
                if (dir.sqrMagnitude > 1f) dir.Normalize();
                rig.position += dir * speed * Time.deltaTime;
            }

            // Clique esquerdo: aperta o DaoButton sob o cursor
            if (MouseDown(0))
            {
                var ray = cam.ScreenPointToRay(MousePosition());
                var hits = Physics.RaycastAll(ray, 6f * Mathf.Max(1f, rig.localScale.x), ~0, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    var button = h.collider.GetComponentInParent<DaoButton>();
                    if (button != null) { button.Press(); Toast("Botão apertado: " + button.name); break; }
                    if (!h.collider.isTrigger) break; // parede ou móvel na frente
                }
            }
        }

        void Toast(string msg) { lastMessage = msg; lastMessageTime = Time.time; }

        void OnGUI()
        {
            if (!Active) return;
            // Mira no centro
            var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
            GUI.Label(new Rect(c.x - 6, c.y - 12, 20, 24), "+");
            if (!showHelp) return;
            var mgr = DaoRoomManager.Instance;
            string room = mgr != null && mgr.CurrentRoom != null ? mgr.CurrentRoom.name : "-";
            string text =
                $"MODO DESKTOP (sem óculos) · Sala atual: {room}\n" +
                "WASD/setas: andar · Shift: correr · botão direito + mouse: olhar\n" +
                "Clique esquerdo: apertar botão de voto · Z/X: votar A/B · 1/2/3: ir para a sala\n" +
                "Fique dentro do círculo de luz verde (porta, arco, escada) para trocar de sala · H: esconder ajuda";
            GUI.Box(new Rect(10, 10, 640, 84), GUIContent.none);
            GUI.Label(new Rect(18, 14, 630, 80), text);
            if (Time.time - lastMessageTime < 2f) GUI.Label(new Rect(18, 98, 630, 22), lastMessage);
        }

        void LateUpdate()
        {
            if (Active && KeyDown(KeyCode.H)) showHelp = !showHelp;
            if (!Active && mode != Mode.Desligado && KeyDown(KeyCode.F1)) Activate(); // força o modo desktop
        }

        // ---------- entrada (Input Manager antigo ou Input System) ----------
#if ENABLE_INPUT_SYSTEM && DAO_HAS_INPUTSYSTEM
        static bool MouseHeld(int b) { var m = Mouse.current; return m != null && (b == 0 ? m.leftButton.isPressed : m.rightButton.isPressed); }
        static bool MouseDown(int b) { var m = Mouse.current; return m != null && (b == 0 ? m.leftButton.wasPressedThisFrame : m.rightButton.wasPressedThisFrame); }
        static Vector2 MouseDelta() { var m = Mouse.current; return m != null ? m.delta.ReadValue() * 0.05f : Vector2.zero; }
        static Vector3 MousePosition() { var m = Mouse.current; return m != null ? (Vector3)m.position.ReadValue() : Vector3.zero; }
        static bool RunHeld() { var k = Keyboard.current; return k != null && k.leftShiftKey.isPressed; }
        static bool KeyDown(KeyCode code)
        {
            var k = Keyboard.current;
            if (k == null) return false;
            if (code == KeyCode.H) return k.hKey.wasPressedThisFrame;
            if (code == KeyCode.F1) return k.f1Key.wasPressedThisFrame;
            return false;
        }
        static Vector2 MoveInput()
        {
            var k = Keyboard.current; if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0);
            return new Vector2(x, y);
        }
#else
        static bool MouseHeld(int b) => Input.GetMouseButton(b);
        static bool MouseDown(int b) => Input.GetMouseButtonDown(b);
        static Vector2 MouseDelta() => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        static Vector3 MousePosition() => Input.mousePosition;
        static bool RunHeld() => Input.GetKey(KeyCode.LeftShift);
        static bool KeyDown(KeyCode code) => Input.GetKeyDown(code);
        static Vector2 MoveInput()
        {
            float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            float y = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
            return new Vector2(x, y);
        }
#endif
    }
}
