using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && DAO_HAS_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace DAO
{
    /// <summary>
    /// Teste sem óculos: se nenhum headset estiver sendo rastreado ao dar Play, troca o rig VR por uma câmera de
    /// primeira pessoa com colisão (CharacterController). Toda a lógica (portais, votação, escala, gravidade) continua igual.
    ///   WASD / setas = andar · Shift = correr · botão direito + mouse = olhar
    ///   clique esquerdo = apertar botão / pegar moeda · segurar clique esquerdo num objeto = carregar (soltar = largar/arremessar)
    ///   roda do mouse = aproximar/afastar o objeto · Z / X = votar A / B · 1 2 3 = ir para a sala · F1 = forçar este modo
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
        public float bodyRadius = 0.25f;
        public bool showHelp = false;
        public float jumpSpeed = 4.2f;

        public bool Active { get; private set; }
        public Transform DesktopRig => rig;
        public Camera DesktopCamera => cam;
        public CharacterController Body => body;

        Transform rig;
        Camera cam;
        CharacterController body;
        GameObject xrRig;
        float yaw, pitch, verticalSpeed;
        string lastMessage = "";
        float lastMessageTime;

        Rigidbody held;
        float heldDistance;
        Vector3 heldVelocity;
        bool heldWasKinematic;

        IEnumerator Start()
        {
            if (mode == Mode.Desligado) yield break;
            yield return null; // deixa o XR tentar iniciar e o DaoRoomManager posicionar o jogador
            yield return null;
            if (mode == Mode.SempreLigado) { Activate(); yield break; }
            if (!AllowAuto) yield break;

            // No Editor o plugin XR pode se dizer "ativo" mesmo sem óculos. O que vale é existir
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
            rig.gameObject.layer = 2; // Ignore Raycast: o próprio corpo não bloqueia cliques
            body = rig.gameObject.AddComponent<CharacterController>();
            body.radius = bodyRadius;
            body.height = eyeHeight + 0.15f;
            body.center = Vector3.up * (body.height * 0.5f);
            body.stepOffset = 0.3f;
            body.skinWidth = 0.02f;
            body.slopeLimit = 50f;

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
                body.enabled = false;
                rig.position = xrRig.transform.position;
                body.enabled = true;
                if (oldHead != null)
                    for (int i = oldHead.childCount - 1; i >= 0; i--)
                    {
                        var child = oldHead.GetChild(i);
                        if (child.name.StartsWith("Desafio") || child.name.StartsWith("HUD")) child.SetParent(cam.transform, false);
                    }
                xrRig.SetActive(false);
            }

            mgr.playerRig = rig;
            mgr.head = cam.transform;
            var fade = FindFirstObjectByType<DaoScreenFade>();
            if (fade != null) fade.head = cam.transform;

            // Botões de Poke e moedas não têm collider próprio para o mouse; cria um.
            foreach (var b in FindObjectsByType<DaoButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.GetComponentInChildren<Collider>(true) == null) AddFittedCollider(b.gameObject);

            if (mgr.CurrentRoom != null && mgr.CurrentRoom.spawnPoint != null) mgr.TeleportTo(mgr.CurrentRoom.spawnPoint);
            yaw = rig.eulerAngles.y;
            mgr.onRoomChanged.AddListener(_ => { yaw = rig.eulerAngles.y; pitch = 0f; Drop(false); });
            mgr.onRespawn.AddListener(() => { yaw = rig.eulerAngles.y; verticalSpeed = 0f; Drop(false); });
            if (DaoLedger.Instance != null)
                DaoLedger.Instance.onTokensChanged.AddListener(t => Toast($"Carteira: {t} token(s)"));
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

                // Andar com colisão (paredes, móveis, palanque) e gravidade própria (não flutua na gravidade zero).
                Vector2 m = MoveInput();
                float speed = moveSpeed * (RunHeld() ? 2f : 1f);
                Vector3 dir = rig.forward * m.y + rig.right * m.x;
                if (dir.sqrMagnitude > 1f) dir.Normalize();
                if (body.isGrounded && verticalSpeed < 0f) verticalSpeed = -1f;
                else verticalSpeed = Mathf.Max(verticalSpeed - 9.81f * Time.deltaTime, -20f);
                if (KeyDown(KeyCode.Space)) Jump();
                if (body.enabled) body.Move((dir * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
            }

            // Clique esquerdo: botão/moeda = aperta; objeto com física = pega.
            if (MouseDown(0) && held == null)
            {
                var ray = cam.ScreenPointToRay(MousePosition());
                var hits = Physics.RaycastAll(ray, 6f, ~0, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(rig)) continue;
                    var button = h.collider.GetComponentInParent<DaoButton>();
                    if (button != null) { button.Press(); break; }
                    var rb = h.collider.attachedRigidbody;
                    if (rb != null && !rb.isKinematic) { Grab(rb, h.distance); break; }
                    if (!h.collider.isTrigger) break; // parede ou móvel na frente
                }
            }
            if (held != null)
            {
                heldDistance = Mathf.Clamp(heldDistance + ScrollDelta() * 0.25f, 0.6f, 3.5f);
                if (!MouseHeld(0)) Drop(true);
            }
        }

        void FixedUpdate()
        {
            if (held == null || cam == null) return;
            Vector3 target = cam.transform.position + cam.transform.forward * heldDistance;
            Vector3 v = (target - held.worldCenterOfMass) / Time.fixedDeltaTime * 0.5f;
            heldVelocity = Vector3.ClampMagnitude(v, 8f);
#if UNITY_6000_0_OR_NEWER
            held.linearVelocity = heldVelocity;
#else
            held.velocity = heldVelocity;
#endif
            held.angularVelocity *= 0.8f;
        }

        void Grab(Rigidbody rb, float distance)
        {
            held = rb;
            heldDistance = Mathf.Clamp(distance, 0.8f, 2.5f);
            heldWasKinematic = rb.isKinematic;
            rb.useGravity = false;
            rb.WakeUp();
            Toast("Segurando: " + rb.name + "  (solte o botão para largar)");
        }

        void Drop(bool throwIt)
        {
            if (held == null) return;
            held.useGravity = true;
            held.isKinematic = heldWasKinematic;
#if UNITY_6000_0_OR_NEWER
            held.linearVelocity = throwIt ? heldVelocity * 0.6f : Vector3.zero;
#else
            held.velocity = throwIt ? heldVelocity * 0.6f : Vector3.zero;
#endif
            held = null;
        }

        /// <summary>Pula se estiver no chão (Espaço).</summary>
        public void Jump()
        {
            if (body != null && body.isGrounded) verticalSpeed = jumpSpeed;
        }

        void Toast(string msg) { lastMessage = msg; lastMessageTime = Time.time; }

        void OnGUI()
        {
            if (!Active) return;
            var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
            GUI.Label(new Rect(c.x - 6, c.y - 12, 20, 24), "+");

            var mgr = DaoRoomManager.Instance;
            string room = mgr != null && mgr.CurrentRoom != null ? mgr.CurrentRoom.name : "-";

            if (Time.time - lastMessageTime < 2.5f) GUI.Label(new Rect(18, showHelp ? 100 : 14, 630, 22), lastMessage);
            if (!showHelp) return;
            string text =
                $"MODO DESKTOP (sem óculos) · Sala: {room}\n" +
                "WASD/setas: andar · Shift: correr · botão direito + mouse: olhar\n" +
                "Clique: votar/pegar moeda · Segure o clique num objeto: carregar (roda: distância) · Espaço: pular\n" +
                "Luz verde (porta, arco, escada): trocar de sala · M: mapas · I: informações · H: esconder ajuda";
            GUI.Box(new Rect(10, 10, 640, 84), GUIContent.none);
            GUI.Label(new Rect(18, 14, 630, 80), text);
        }

        void LateUpdate()
        {
            if (Active && KeyDown(KeyCode.H)) showHelp = !showHelp;
            if (!Active && mode != Mode.Desligado && KeyDown(KeyCode.F1)) Activate();
        }

        // ---------- entrada (Input Manager antigo ou Input System) ----------
#if ENABLE_INPUT_SYSTEM && DAO_HAS_INPUTSYSTEM
        static bool MouseHeld(int b) { var m = Mouse.current; return m != null && (b == 0 ? m.leftButton.isPressed : m.rightButton.isPressed); }
        static bool MouseDown(int b) { var m = Mouse.current; return m != null && (b == 0 ? m.leftButton.wasPressedThisFrame : m.rightButton.wasPressedThisFrame); }
        static Vector2 MouseDelta() { var m = Mouse.current; return m != null ? m.delta.ReadValue() * 0.05f : Vector2.zero; }
        static Vector3 MousePosition() { var m = Mouse.current; return m != null ? (Vector3)m.position.ReadValue() : Vector3.zero; }
        static float ScrollDelta() { var m = Mouse.current; return m != null ? Mathf.Sign(m.scroll.ReadValue().y) * (Mathf.Abs(m.scroll.ReadValue().y) > 0.01f ? 1f : 0f) : 0f; }
        static bool RunHeld() { var k = Keyboard.current; return k != null && k.leftShiftKey.isPressed; }
        static bool KeyDown(KeyCode code)
        {
            var k = Keyboard.current;
            if (k == null) return false;
            if (code == KeyCode.H) return k.hKey.wasPressedThisFrame;
            if (code == KeyCode.F1) return k.f1Key.wasPressedThisFrame;
            if (code == KeyCode.Space) return k.spaceKey.wasPressedThisFrame;
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
        static float ScrollDelta() => Mathf.Sign(Input.mouseScrollDelta.y) * (Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f ? 1f : 0f);
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
