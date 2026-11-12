using UnityEngine;
using UnityEngine.Events;

namespace DAO
{
    /// <summary>
    /// Portal iluminado em frente a uma porta ou escada real.
    /// Quando a cabeça do jogador fica dentro do círculo, a luz "carrega" por <see cref="dwellTime"/> segundos
    /// e então o jogador é teleportado (com fade) para a sala de destino. Sair do círculo cancela.
    /// Não depende de física nem de colliders: usa a posição da cabeça, então funciona minúsculo ou gigante.
    /// O visual (anel no chão, feixe e luz) é criado sozinho se os campos estiverem vazios.
    /// </summary>
    public class DaoPortal : MonoBehaviour
    {
        [Header("Destino")]
        [Tooltip("Índice da sala de destino no DaoRoomManager.")]
        public int targetRoom;
        public string label = "Próxima sala";

        [Header("Zona (o pivô fica no chão)")]
        public float radius = 0.6f;
        public float height = 2.4f;
        [Tooltip("Segundos parado dentro do círculo até teleportar.")]
        public float dwellTime = 1.2f;
        [Tooltip("Tempo depois de chegar na sala em que o portal fica desarmado.")]
        public float armDelay = 1.5f;

        [Header("Visual")]
        public Color color = new Color(0.25f, 1f, 0.7f);
        public Material glowMaterial;
        [Range(0f, 1f)] public float idleGlow = 0.35f;
        public float idleLight = 0.6f;
        public float activeLight = 3f;
        public Renderer ring;
        public Renderer beam;
        public Light glowLight;
        public DaoText labelText;
        public AudioSource chargeAudio;

        [Header("Eventos")]
        public UnityEvent onPlayerEnter = new UnityEvent();
        public UnityEvent onPlayerExit = new UnityEvent();
        public UnityEvent onTeleport = new UnityEvent();

        public float Charge { get; private set; }
        public bool PlayerInside { get; private set; }

        float armedAt;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (ring == null || beam == null) BuildVisuals();
            if (labelText != null) labelText.Set(label);
        }

        void OnEnable()
        {
            armedAt = Time.time + armDelay;
            Charge = 0f;
            PlayerInside = false;
        }

        void Update()
        {
            var mgr = DaoRoomManager.Instance;
            Transform head = mgr != null ? mgr.head : (Camera.main != null ? Camera.main.transform : null);
            bool inside = false;
            if (head != null && Time.time >= armedAt && (mgr == null || !mgr.IsTransitioning))
            {
                Vector3 local = head.position - transform.position;
                float s = transform.lossyScale.x; // a sala pode estar escalada (minúsculo/gigante)
                inside = new Vector2(local.x, local.z).magnitude <= radius * s
                         && local.y >= -0.2f && local.y <= Mathf.Max(height * s, 2.4f);
            }

            if (inside != PlayerInside)
            {
                PlayerInside = inside;
                if (inside) { onPlayerEnter.Invoke(); if (chargeAudio != null) chargeAudio.Play(); }
                else { onPlayerExit.Invoke(); if (chargeAudio != null) chargeAudio.Stop(); }
            }

            Charge = inside
                ? Mathf.Min(1f, Charge + Time.deltaTime / Mathf.Max(0.05f, dwellTime))
                : Mathf.Max(0f, Charge - Time.deltaTime * 2f);

            UpdateVisuals();

            if (Charge >= 1f)
            {
                Charge = 0f;
                PlayerInside = false;
                armedAt = Time.time + armDelay;
                if (chargeAudio != null) chargeAudio.Stop();
                onTeleport.Invoke();
                if (mgr != null) mgr.GoToRoom(targetRoom);
            }
        }

        void UpdateVisuals()
        {
            float pulse = 0.08f * Mathf.Sin(Time.time * (3f + 9f * Charge));
            float glow = Mathf.Clamp01(Mathf.Lerp(idleGlow, 1f, Charge) + pulse);
            Color c = color * (1f + 2f * Charge);
            c.a = glow;
            SetColor(ring, c);
            SetColor(beam, c);
            if (glowLight != null)
            {
                glowLight.color = color;
                glowLight.intensity = Mathf.Lerp(idleLight, activeLight, Charge) * (1f + pulse);
            }
        }

        void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(mpb);
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>Cria anel, feixe (cilindro aberto) e luz. Também pode ser chamado pelo editor.</summary>
        public void BuildVisuals()
        {
            var shader = glowMaterial != null ? glowMaterial.shader : Shader.Find("DAO/Glow");
            if (shader == null) { Debug.LogWarning("[DAO] Shader DAO/Glow não encontrado."); return; }

            if (ring == null)
            {
                var go = new GameObject("Anel");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.up * 0.02f;
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = Vector3.one * (radius * 2f);
                go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                ring = go.AddComponent<MeshRenderer>();
                var m = new Material(glowMaterial != null ? glowMaterial : new Material(shader));
                m.SetFloat("_Radial", 1f);
                ring.sharedMaterial = m;
                ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (beam == null)
            {
                var go = new GameObject("Feixe");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = OpenCylinder(radius, height, 32);
                beam = go.AddComponent<MeshRenderer>();
                var m = new Material(glowMaterial != null ? glowMaterial : new Material(shader));
                m.SetFloat("_Radial", 0f);
                beam.sharedMaterial = m;
                beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (glowLight == null)
            {
                var go = new GameObject("Luz do portal");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.up * 0.8f;
                glowLight = go.AddComponent<Light>();
                glowLight.type = LightType.Point;
                glowLight.range = 3.5f;
                glowLight.shadows = LightShadows.None;
                glowLight.color = color;
                glowLight.intensity = idleLight;
            }
        }

        static Mesh QuadMesh()
        {
            var m = new Mesh { name = "DAO Portal Quad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh OpenCylinder(float r, float h, int seg)
        {
            var v = new Vector3[(seg + 1) * 2];
            var uv = new Vector2[v.Length];
            var t = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                v[i * 2] = p;
                v[i * 2 + 1] = p + Vector3.up * h;
                uv[i * 2] = new Vector2((float)i / seg, 0);
                uv[i * 2 + 1] = new Vector2((float)i / seg, 1);
            }
            for (int i = 0; i < seg; i++)
            {
                int k = i * 2, n = i * 6;
                t[n] = k; t[n + 1] = k + 1; t[n + 2] = k + 2;
                t[n + 3] = k + 2; t[n + 4] = k + 1; t[n + 5] = k + 3;
            }
            var m = new Mesh { name = "DAO Portal Beam", vertices = v, uv = uv, triangles = t };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(color.r, color.g, color.b, 0.5f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.identity, Vector3.one);
            const int n = 32;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                Gizmos.DrawLine(new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius, new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius);
            }
            Gizmos.DrawLine(Vector3.zero, Vector3.up * height);
        }
    }
}
