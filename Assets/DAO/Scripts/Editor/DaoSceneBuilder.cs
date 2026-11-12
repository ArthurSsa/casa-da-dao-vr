using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
#if DAO_HAS_META
using Oculus.Interaction;
#endif

namespace DAO.EditorTools
{
    /// <summary>
    /// Menu "DAO > Montar cena das 3 salas": cria uma cena com as 3 salas lado a lado (100 m de distância),
    /// spawn points, portas, estação de votação, livro-razão e todos os eventos já ligados.
    /// No template do professor: rode o menu, apague o "PlayerRig (placeholder)" e arraste o rig do template
    /// para os campos Player Rig / Head do DaoRoomManager.
    /// </summary>
    public static class DaoSceneBuilder
    {
        const string Root = "Assets/DAO";
        const string ScenePath = Root + "/Scenes/DAO_3Salas.unity";
        const float RoomSpacing = 100f;
        const float TextScale = 3f; // multiplicador global do tamanho dos textos (TextMesh)

        enum Effect { Scale, Tint, Gravity }

        /// <summary>Como achar a saída real da sala: ponto conhecido de uma porta, vão aberto na parede ou escada descendo.</summary>
        enum ExitKind { DoorPoint, WallOpening, StairsDown, StairsMesh }

        class RoomDef
        {
            public string name, modelFile, proposalTitle, proposalDesc, optA, optB;
            public Color colA, colB;
            public float scale; // 0 = automático
            public Effect effect;
            public float floorY;      // altura do piso (m) medida a partir do ponto mais baixo do modelo já escalado
            public bool addCeiling;   // o modelo não tem teto visível por dentro
            public bool fixFloating;  // filhos do modelo "voando" muito acima do piso (bug de export) são baixados
            public ExitKind exit;
            // DoorPoint: ponto medido com o DaoModelInspector (modelo instanciado na origem, escala 1) e direção para dentro.
            public Vector3 exitLocal, exitInwardLocal;
            public string exitMeshName; // StairsMesh: nome da malha da escada
            public bool closeOpenWalls;  // fecha lados sem parede (modelos de vitrine da Sketchfab)
            public Color wallColor = new Color(0.91f, 0.74f, 0.6f);
            public int exitTo;
            public string exitName;
        }

        // Valores medidos com DaoModelInspector (veja o guia): sala 7,2 x 3,9 x 7,1 m com a porta de madeira na parede
        // oeste; cozinha com os móveis exportados 20 m acima do piso e um arco na parede; sótão com piso a 4 m e escada.
        // Cada sala tem uma saída real, ligadas em ciclo: Sala -> porta -> Cozinha -> arco -> Sótão -> escada -> Sala.
        static readonly RoomDef[] Defs =
        {
            new RoomDef { name = "Sala de Estar", modelFile = "old_living_room", scale = 0.0254f, effect = Effect.Scale,
                floorY = 0.05f, addCeiling = true, closeOpenWalls = true, wallColor = new Color(0.42f, 0.24f, 0.13f),
                exit = ExitKind.DoorPoint, exitLocal = new Vector3(-276.75f, 2f, -84.2f), exitInwardLocal = Vector3.right,
                exitTo = 1, exitName = "Porta",
                proposalTitle = "Proposta #1 - Tamanho", proposalDesc = "Como a DAO quer que você explore a sala?",
                optA = "Minúsculo", colA = new Color(0.25f, 0.6f, 1f), optB = "Gigante", colB = new Color(1f, 0.55f, 0.2f) },
            new RoomDef { name = "Cozinha", modelFile = "Cartoon Kitchen Interior", scale = 1f, effect = Effect.Gravity,
                floorY = 0.62f, addCeiling = true, fixFloating = true, closeOpenWalls = true,
                exit = ExitKind.WallOpening, exitTo = 2, exitName = "Arco",
                proposalTitle = "Proposta #2 - Física", proposalDesc = "Qual gravidade vale na cozinha?",
                optA = "Gravidade zero", colA = new Color(0.6f, 0.3f, 1f), optB = "Gravidade lunar", colB = new Color(0.7f, 0.7f, 0.75f) },
            new RoomDef { name = "Sótão", modelFile = "old_attic", scale = 1f, effect = Effect.Tint,
                floorY = 4.04f,
                exit = ExitKind.StairsMesh, exitMeshName = "FrontSide_8", exitTo = 0, exitName = "Escada",
                proposalTitle = "Proposta #3 - Clima", proposalDesc = "Qual atmosfera o sótão deve ter?",
                optA = "Pôr do sol", colA = new Color(1f, 0.6f, 0.25f), optB = "Noite azul", colB = new Color(0.3f, 0.5f, 1f) },
        };

        static readonly Color PortalColor = new Color(0.25f, 1f, 0.7f);

        static readonly Regex PhysicsProps = new Regex(@"^(Pan|Pot|Plate|Teapot|Toaster|Tool#|CoffeeMaker|Chair|Microwave)", RegexOptions.IgnoreCase);

        class Built
        {
            public RoomDef def;
            public GameObject root;
            public Transform spawn;
            public Bounds bounds;
            public DaoProposal proposal;
            public GameObject model;
            public float floorY;
            public DaoPortal portal;
        }

        [MenuItem("DAO/Montar cena das 3 salas")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(out _, out _);
            EditorUtility.DisplayDialog("DAO", "Cena criada em " + ScenePath + ".\nVeja o guia para trocar o PlayerRig pelo rig do template.", "OK");
        }

        /// <summary>Linha de comando: -executeMethod DAO.EditorTools.DaoSceneBuilder.BuildCLI -previewDir C:\pasta</summary>
        public static void BuildCLI()
        {
            try
            {
                string dir = null;
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-previewDir") dir = args[i + 1];
                Build(out var rooms, out var cam);
                if (!string.IsNullOrEmpty(dir)) RenderPreviews(dir, rooms, cam);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[DAO-BUILD] FALHOU: " + e);
                EditorApplication.Exit(1);
            }
        }

        static void Build(out List<Built> rooms, out Camera cam)
        {
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Materials");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Luz e ambiente amigáveis ao Quest: sem sombras em tempo real, ambiente chapado.
            var sun = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (sun != null) { sun.shadows = LightShadows.None; sun.intensity = 0.6f; }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.58f);

            // Jogador: rig oficial do Meta XR SDK quando instalado; senão um placeholder para testar no Editor.
            Transform rig, head;
#if DAO_HAS_META
            CreateMetaRig(out rig, out head);
#else
            var placeholderCam = Camera.main;
            rig = new GameObject("PlayerRig (placeholder - instale o Meta XR SDK)").transform;
            placeholderCam.transform.SetParent(rig, false);
            placeholderCam.transform.localPosition = new Vector3(0, 1.6f, 0);
            placeholderCam.nearClipPlane = 0.05f;
            head = placeholderCam.transform;
#endif
            cam = head.GetComponent<Camera>();

            var systems = new GameObject("DAO Systems");
            var mgr = systems.AddComponent<DaoRoomManager>();
            mgr.playerRig = rig;
            mgr.head = head;
            var fade = systems.AddComponent<DaoScreenFade>();
            fade.head = head;
            fade.fadeMaterial = GetFadeMaterial();
            mgr.screenFade = fade;
            var ledger = systems.AddComponent<DaoLedger>();
            var scaler = systems.AddComponent<DaoPlayerScale>();
            scaler.playerRig = rig;
            scaler.head = head;
            systems.AddComponent<DaoDebugKeys>();
            systems.AddComponent<DaoDesktopMode>(); // Play sem óculos = mouse e teclado

            rooms = new List<Built>();
            for (int i = 0; i < Defs.Length; i++) rooms.Add(BuildRoom(i, Defs[i]));

            // Portas, votação e painéis (precisam conhecer todas as salas).
            var ledgerTexts = new List<DaoText>();
            var walletTexts = new List<DaoText>();
            for (int i = 0; i < rooms.Count; i++)
            {
                var b = rooms[i];
                b.proposal = BuildVoteStation(b, ledgerTexts, walletTexts);
                BuildPortal(b, rooms[b.def.exitTo].def.name);
                WireEffect(b, scaler);

                mgr.rooms.Add(new DaoRoom { name = b.def.name, root = b.root, spawnPoint = b.spawn, proposal = b.proposal });
                var room = mgr.rooms[i];
                UnityEventTools.AddPersistentListener(room.onExit, scaler.ResetScaleInstant);
                var grav = b.root.GetComponent<DaoGravity>();
                if (grav != null) UnityEventTools.AddPersistentListener(room.onExit, grav.Restore);
            }
            ledger.ledgerTexts = ledgerTexts.ToArray();
            ledger.walletTexts = walletTexts.ToArray();
            ledger.proposalsToComplete = rooms.Count;

            // Tela de conclusão placeholder (o template do professor já tem uma: ligue o onChallengeComplete nela).
            var done = new GameObject("Desafio Concluido (placeholder)");
            done.transform.SetParent(head, false);
            done.transform.localPosition = new Vector3(0, 0, 1.2f);
            MakeText(done.transform, "Texto", "DESAFIO CONCLUÍDO!\nVocê governou as 3 salas.", Vector3.zero, 0.0035f, Color.green);
            done.SetActive(false);
            UnityEventTools.AddBoolPersistentListener(ledger.onChallengeComplete, done.SetActive, true);

            // Posiciona o placeholder no spawn da primeira sala (no Play o manager faz isso).
            rig.SetPositionAndRotation(rooms[0].spawn.position, rooms[0].spawn.rotation);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            Debug.Log("[DAO-BUILD] Cena salva em " + ScenePath);
        }

        static Built BuildRoom(int index, RoomDef def)
        {
            var b = new Built { def = def };
            b.root = new GameObject("Sala " + (index + 1) + " - " + def.name);
            b.root.transform.position = new Vector3(index * RoomSpacing, 0, 0);

            string path = AssetDatabase.FindAssets(def.modelFile)
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.StartsWith(Root + "/Models") && (p.EndsWith(".glb") || p.EndsWith(".gltf") || p.EndsWith(".fbx")));
            var asset = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;

            Bounds bounds;
            GameObject model = null;
            if (asset == null)
            {
                Debug.LogWarning($"[DAO-BUILD] Modelo '{def.modelFile}' não encontrado em {Root}/Models. Criando sala vazia.");
                bounds = new Bounds(b.root.transform.position + Vector3.up * 1.5f, new Vector3(6, 3, 6));
            }
            else
            {
                model = (GameObject)PrefabUtility.InstantiatePrefab(asset, b.root.transform);
                model.transform.localPosition = Vector3.zero;
                if (def.fixFloating) FixFloatingChildren(model);
                bounds = RendererBounds(model);
                float scale = def.scale;
                if (scale <= 0f)
                {
                    float max = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    scale = max > 1000f ? 0.01f : max > 60f ? 0.0254f : 1f;
                }
                model.transform.localScale = Vector3.one * scale;
                bounds = RendererBounds(model);
                // Centraliza no X/Z da sala e apoia o chão em y = 0.
                Vector3 shift = new Vector3(b.root.transform.position.x - bounds.center.x, -bounds.min.y, b.root.transform.position.z - bounds.center.z);
                model.transform.position += shift;
                bounds = RendererBounds(model);
                LogStats(def.name, path, model, scale, bounds);

                foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
                {
                    bool dynamic = def.effect == Effect.Gravity && PhysicsProps.IsMatch(r.name);
                    if (!dynamic && r.GetComponentInParent<Rigidbody>() != null)
                    {
                        // Peça filha de um objeto com física (ex.: porta do micro-ondas): collider convexo simples.
                        r.gameObject.AddComponent<BoxCollider>();
                    }
                    else if (dynamic)
                    {
                        r.gameObject.AddComponent<BoxCollider>();
                        if (r.GetComponentInParent<Rigidbody>() != null) continue; // vira parte do collider composto do pai
                        var rb = r.gameObject.AddComponent<Rigidbody>();
                        rb.mass = 1f;
#if UNITY_6000_0_OR_NEWER
                        rb.linearDamping = 0.2f;
                        rb.angularDamping = 0.3f;
#else
                        rb.drag = 0.2f;
                        rb.angularDrag = 0.3f;
#endif
                        rb.interpolation = RigidbodyInterpolation.Interpolate;
                    }
                    else if (IsTriangleMesh(r))
                    {
                        // Colliders nas paredes/móveis: segura objetos flutuando e dá chão para a locomoção do template.
                        // (Malhas de linhas, como os contornos do SketchUp na sala de estar, não podem ter MeshCollider.)
                        r.gameObject.AddComponent<MeshCollider>();
                    }
                }
            }
            b.bounds = bounds;
            b.model = model;
            float floorY = bounds.min.y + def.floorY;
            b.floorY = floorY;

            if (def.addCeiling)
            {
                var ceil = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ceil.name = "Teto (adicionado)";
                ceil.transform.SetParent(b.root.transform, false);
                ceil.transform.position = new Vector3(bounds.center.x, bounds.max.y - 0.01f, bounds.center.z);
                ceil.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // face visível para baixo
                ceil.transform.localScale = new Vector3(bounds.size.x, bounds.size.z, 1f);
                ceil.GetComponent<MeshRenderer>().sharedMaterial = ColorMaterial(new Color(0.93f, 0.9f, 0.84f));
            }

            if (def.closeOpenWalls) CloseOpenWalls(b);

            b.spawn = new GameObject("Spawn").transform;
            b.spawn.SetParent(b.root.transform, false);
            PlaceSpawn(b, floorY);

            // Chão de segurança na altura do piso.
            var floor = new GameObject("Chao (collider)");
            floor.transform.SetParent(b.root.transform, false);
            var fc = floor.AddComponent<BoxCollider>();
            fc.center = new Vector3(bounds.center.x - b.root.transform.position.x, floorY - 0.05f, bounds.center.z - b.root.transform.position.z);
            fc.size = new Vector3(bounds.size.x, 0.1f, bounds.size.z);

            // Luz da sala (sem sombra; só fica ligada quando a sala está ativa).
            var light = new GameObject("Luz da sala").AddComponent<Light>();
            light.transform.SetParent(b.root.transform, false);
            light.type = LightType.Point;
            light.range = Mathf.Max(bounds.size.x, bounds.size.z);
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
            light.transform.position = b.spawn.position + Vector3.up * Mathf.Min(2.6f, bounds.max.y - floorY - 0.3f);
            return b;
        }

        /// <summary>
        /// Procura no piso o ponto com mais espaço livre em volta (sem móveis, com 2 m de altura livre)
        /// e vira o spawn para a direção mais aberta. Usa raycasts nos MeshColliders da sala.
        /// </summary>
        static void PlaceSpawn(Built b, float floorY)
        {
            bool prevBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();

            var bb = b.bounds;
            const float step = 0.4f;
            var clear = new List<Vector3>();
            for (float x = bb.min.x + 0.4f; x <= bb.max.x - 0.4f; x += step)
                for (float z = bb.min.z + 0.4f; z <= bb.max.z - 0.4f; z += step)
                {
                    var top = new Vector3(x, floorY + 2.0f, z);
                    if (!Physics.Raycast(top, Vector3.down, out var hit, 2.6f)) continue;
                    if (Mathf.Abs(hit.point.y - floorY) > 0.15f) continue;              // bateu em móvel/viga
                    if (Physics.Raycast(new Vector3(x, hit.point.y + 0.05f, z), Vector3.up, 2.1f)) continue; // teto baixo
                    clear.Add(new Vector3(x, hit.point.y, z));
                }
            Physics.queriesHitBackfaces = prevBack;

            Vector3 center = new Vector3(bb.center.x, floorY, bb.center.z);
            if (clear.Count == 0)
            {
                Debug.LogWarning($"[DAO-BUILD] {b.def.name}: nenhum ponto livre encontrado, spawn no centro.");
                b.spawn.position = center;
                return;
            }

            Vector3 best = clear[0];
            int bestScore = -1;
            float bestDist = float.MaxValue;
            foreach (var c in clear)
            {
                int score = clear.Count(o => (o - c).sqrMagnitude < 1.8f * 1.8f);
                float dist = (c - center).sqrMagnitude;
                if (score > bestScore || (score == bestScore && dist < bestDist)) { best = c; bestScore = score; bestDist = dist; }
            }

            // Direção mais aberta: conta pontos livres num cone de 35° até 3 m.
            float bestYaw = 0f;
            int bestOpen = -1;
            for (int a = 0; a < 16; a++)
            {
                float yaw = a * 22.5f;
                Vector3 dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
                int open = clear.Count(o =>
                {
                    Vector3 d = o - best; d.y = 0;
                    float m = d.magnitude;
                    return m > 0.2f && m < 3f && Vector3.Angle(dir, d) < 35f;
                });
                if (open > bestOpen) { bestOpen = open; bestYaw = yaw; }
            }
            b.spawn.SetPositionAndRotation(best, Quaternion.Euler(0, bestYaw, 0));
            Debug.Log($"[DAO-BUILD] {b.def.name}: {clear.Count} pontos livres no piso; spawn em {best} olhando {bestYaw}° (vizinhos livres {bestScore}).");
        }

        /// <summary>Baixa filhos que ficaram muito acima do piso (a cozinha veio com os móveis +20 m).</summary>
        static void FixFloatingChildren(GameObject model)
        {
            var children = Enumerable.Range(0, model.transform.childCount).Select(i => model.transform.GetChild(i)).ToList();
            var floorChild = children.FirstOrDefault(c => c.name.StartsWith("Floor"));
            float floorTop = floorChild != null ? RendererBounds(floorChild.gameObject).max.y : RendererBounds(model).min.y;
            var floating = children.Where(c => c.GetComponentsInChildren<Renderer>().Length > 0 && RendererBounds(c.gameObject).min.y > floorTop + 10f).ToList();
            if (floating.Count == 0) return;
            float minY = floating.Min(c => RendererBounds(c.gameObject).min.y);
            float shift = minY - floorTop;
            foreach (var c in floating) c.position += Vector3.down * shift;
            Debug.Log($"[DAO-BUILD] {model.name}: {floating.Count} objetos estavam {shift:F2} m acima do piso e foram baixados.");
        }

        static DaoProposal BuildVoteStation(Built b, List<DaoText> ledgerTexts, List<DaoText> walletTexts)
        {
            var st = new GameObject("Estacao de Votacao").transform;
            st.SetParent(b.spawn, false);
            st.localPosition = new Vector3(0, 0, 1.1f);

            var pedestal = Cube(st, "Pedestal", new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 0.4f), new Color(0.12f, 0.12f, 0.2f));
            UnityEngine.Object.DestroyImmediate(pedestal.GetComponent<Collider>());

            var proposal = st.gameObject.AddComponent<DaoProposal>();
            proposal.title = b.def.proposalTitle;
            proposal.description = b.def.proposalDesc;
            proposal.options = new List<DaoOption>
            {
                new DaoOption { label = b.def.optA, color = b.def.colA },
                new DaoOption { label = b.def.optB, color = b.def.colB },
            };
            proposal.titleText = MakeText(st, "Titulo", proposal.title + "\n" + proposal.description, new Vector3(0, 1.75f, 0.1f), 0.0028f, Color.white);
            proposal.tallyText = MakeText(st, "Placar", $"{b.def.optA}: 0   x   {b.def.optB}: 0", new Vector3(0, 1.45f, 0.1f), 0.0032f, Color.yellow);
            proposal.statusText = MakeText(st, "Status", "Aperte um botão para votar.", new Vector3(0, 1.22f, 0.1f), 0.0018f, new Color(0.8f, 0.9f, 1f));

            for (int k = 0; k < 2; k++)
            {
                var opt = proposal.options[k];
                var db = MakePokeButton(st, "Botao " + (k == 0 ? "A" : "B") + " - " + opt.label, new Vector3(k == 0 ? -0.22f : 0.22f, 0.92f, 0), opt.color);
                UnityEventTools.AddIntPersistentListener(db.onPress, proposal.Vote, k);
                MakeText(st, "Rotulo " + k, opt.label, new Vector3(k == 0 ? -0.22f : 0.22f, 1.05f, 0.05f), 0.0018f, opt.color);
            }

            // Painel do livro-razão + carteira, à esquerda.
            var panel = new GameObject("Painel Livro-Razao").transform;
            panel.SetParent(b.spawn, false);
            panel.localPosition = new Vector3(-1.4f, 1.5f, 0.9f);
            panel.localRotation = Quaternion.LookRotation(new Vector3(-1.4f, 0, 0.9f));
            ledgerTexts.Add(MakeText(panel, "Livro-Razao", "LIVRO-RAZÃO DA DAO", Vector3.zero, 0.0016f, new Color(0.6f, 1f, 0.7f)));
            walletTexts.Add(MakeText(panel, "Carteira", "Carteira: 0 token(s)", new Vector3(0, -0.45f, 0), 0.002f, Color.cyan));
            return proposal;
        }

        /// <summary>
        /// Coloca um portal iluminado em frente à saída real da sala (porta, arco ou escada).
        /// Ficar dentro do círculo de luz por ~1 s teleporta para a próxima sala.
        /// </summary>
        static void BuildPortal(Built b, string targetName)
        {
            if (!FindExit(b, out Vector3 pos, out Vector3 inward))
            {
                Debug.LogWarning($"[DAO-BUILD] {b.def.name}: saída '{b.def.exitName}' não encontrada; portal ao lado do spawn.");
                pos = b.spawn.position + b.spawn.right * 1.5f;
                inward = -b.spawn.right;
            }
            var go = new GameObject($"Portal {b.def.exitName} -> {targetName}");
            go.transform.SetParent(b.root.transform, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-inward)); // olha para a porta
            var portal = go.AddComponent<DaoPortal>();
            portal.targetRoom = b.def.exitTo;
            portal.label = $"{b.def.exitName} para {targetName}";
            portal.color = PortalColor;
            portal.glowMaterial = GetGlowMaterial();
            portal.labelText = MakeText(go.transform, "Rotulo", portal.label + "\nfique na luz", new Vector3(0, 2.55f, 0), 0.0024f, PortalColor);
            portal.BuildVisuals();
            b.portal = portal;
            float toSpawn = Vector3.Distance(new Vector3(pos.x, 0, pos.z), new Vector3(b.spawn.position.x, 0, b.spawn.position.z));
            Debug.Log($"[DAO-BUILD] {b.def.name}: portal '{b.def.exitName}' em {pos} (entrada para {inward}), {toSpawn:F1} m do spawn.");
        }

        static bool FindExit(Built b, out Vector3 pos, out Vector3 inward)
        {
            pos = default; inward = default;
            if (b.model == null) return false;
            bool prev = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();
            try
            {
                switch (b.def.exit)
                {
                    case ExitKind.DoorPoint:
                        // As medidas já incluem a rotação da raiz do modelo; só falta a escala e o deslocamento.
                        Vector3 door = b.model.transform.position + b.def.exitLocal * b.model.transform.lossyScale.x;
                        inward = b.def.exitInwardLocal;
                        inward.y = 0; inward.Normalize();
                        pos = door + inward * 0.7f;
                        pos.y = b.floorY;
                        return true;
                    case ExitKind.WallOpening: return FindWallOpening(b, out pos, out inward);
                    case ExitKind.StairsDown: return FindStairsDown(b, out pos, out inward);
                    case ExitKind.StairsMesh: return FindStairsByMesh(b, out pos, out inward);
                }
                return false;
            }
            finally { Physics.queriesHitBackfaces = prev; }
        }

        struct WallRun { public Vector3 normal; public float edge, start, end; public bool alongZ, lintel; public float Length => end - start; }

        /// <summary>
        /// Varre as 4 paredes com raios para fora a 0,4 m e 1,4 m do piso e devolve os trechos abertos.
        /// "lintel" = há parede acima do vão (verga), como num arco ou porta; sem verga = lado inteiro sem parede.
        /// </summary>
        static List<WallRun> ScanWallRuns(Built b)
        {
            var runs = new List<WallRun>();
            var bb = b.bounds;
            float lintelH = Mathf.Max(2.2f, bb.max.y - b.floorY - 0.3f);
            var sides = new[] { (Vector3.right, bb.max.x), (Vector3.left, bb.min.x), (Vector3.forward, bb.max.z), (Vector3.back, bb.min.z) };
            foreach (var (n, edge) in sides)
            {
                bool alongZ = n.x != 0;
                float from = alongZ ? bb.min.z : bb.min.x, to = alongZ ? bb.max.z : bb.max.x;
                float runStart = float.NaN;
                int lintelHits = 0, samples = 0;
                for (float t = from + 0.3f; t <= to - 0.3f + 0.101f; t += 0.1f)
                {
                    bool inRange = t <= to - 0.3f;
                    Vector3 wall = alongZ ? new Vector3(edge, 0, t) : new Vector3(t, 0, edge);
                    bool open = inRange;
                    if (open)
                        foreach (float h in new[] { 0.4f, 1.4f })
                            if (Physics.Raycast(wall - n * 0.5f + Vector3.up * (b.floorY + h), n, 1.0f)) { open = false; break; }
                    if (open)
                    {
                        if (float.IsNaN(runStart)) { runStart = t; lintelHits = 0; samples = 0; }
                        samples++;
                        if (Physics.Raycast(wall - n * 0.5f + Vector3.up * (b.floorY + lintelH), n, 1.0f)) lintelHits++;
                    }
                    else if (!float.IsNaN(runStart))
                    {
                        runs.Add(new WallRun { normal = n, edge = edge, alongZ = alongZ, start = runStart, end = t - 0.1f, lintel = lintelHits > samples / 2 });
                        runStart = float.NaN;
                    }
                }
            }
            return runs;
        }

        static Vector3 RunPoint(WallRun r, float t, float y) => r.alongZ ? new Vector3(r.edge, y, t) : new Vector3(t, y, r.edge);

        /// <summary>Acha um arco/porta sem folha: vão entre 0,7 e 2,5 m com verga em cima.</summary>
        static bool FindWallOpening(Built b, out Vector3 pos, out Vector3 inward)
        {
            pos = default; inward = default;
            var candidates = ScanWallRuns(b).Where(r => r.lintel && r.Length >= 0.6f && r.Length <= 3.5f).OrderByDescending(r => r.Length).ToList();
            foreach (var r in ScanWallRuns(b))
                Debug.Log($"[DAO-BUILD] {b.def.name}: trecho aberto de {r.Length:F1} m (normal {r.normal}, verga: {r.lintel}).");
            if (candidates.Count == 0) return false;
            var best = candidates[0];
            inward = -best.normal;
            pos = RunPoint(best, (best.start + best.end) * 0.5f, b.floorY) + inward * 0.7f;
            Debug.Log($"[DAO-BUILD] {b.def.name}: arco de {best.Length:F1} m encontrado na parede.");
            return true;
        }

        /// <summary>Fecha lados sem parede (sem verga e com mais de 2 m) com uma parede simples, do piso ao teto.</summary>
        static void CloseOpenWalls(Built b)
        {
            float height = b.bounds.max.y - b.floorY;
            bool prev = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            Physics.SyncTransforms();
            var openSides = ScanWallRuns(b).Where(r => !r.lintel && r.Length >= 2f).ToList();
            Physics.queriesHitBackfaces = prev;
            foreach (var r in openSides)
            {
                float s = r.start - 0.35f, e = r.end + 0.35f; // cobre até os cantos
                var wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
                wall.name = "Parede (adicionada)";
                wall.transform.SetParent(b.root.transform, false);
                wall.transform.position = RunPoint(r, (s + e) * 0.5f, b.floorY + height * 0.5f);
                wall.transform.rotation = Quaternion.LookRotation(r.normal); // face visível para dentro
                wall.transform.localScale = new Vector3(e - s, height, 1f);
                wall.GetComponent<MeshRenderer>().sharedMaterial = ColorMaterial(b.def.wallColor);
                Debug.Log($"[DAO-BUILD] {b.def.name}: lado aberto de {r.Length:F1} m fechado com parede.");
            }
            Physics.SyncTransforms();
        }

        /// <summary>Escada identificada pela malha: acha o degrau mais alto e põe o portal no piso logo depois dele.</summary>
        static bool FindStairsByMesh(Built b, out Vector3 pos, out Vector3 inward)
        {
            pos = default; inward = default;
            var stairs = b.model.GetComponentsInChildren<MeshCollider>().FirstOrDefault(c => c.name == b.def.exitMeshName);
            if (stairs == null) return false;
            var sb = stairs.bounds;
            Vector3 top = default, low = default;
            float topY = float.MinValue, lowY = float.MaxValue;
            for (float x = sb.min.x + 0.1f; x < sb.max.x; x += 0.1f)
                for (float z = sb.min.z + 0.1f; z < sb.max.z; z += 0.1f)
                {
                    var ray = new Ray(new Vector3(x, sb.max.y + 0.5f, z), Vector3.down);
                    if (!stairs.Raycast(ray, out var hit, sb.size.y + 1f)) continue;
                    if (hit.point.y > topY) { topY = hit.point.y; top = hit.point; }
                    if (hit.point.y < lowY) { lowY = hit.point.y; low = hit.point; }
                }
            if (topY == float.MinValue) return false;
            inward = top - low; inward.y = 0;
            // A escada sobe no eixo mais comprido dela; arredonda para esse eixo.
            inward = sb.size.x >= sb.size.z ? new Vector3(Mathf.Sign(inward.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(inward.z));
            pos = new Vector3(top.x, b.floorY, top.z) + inward * 0.45f;
            Debug.Log($"[DAO-BUILD] {b.def.name}: escada '{b.def.exitMeshName}' sobe de {lowY:F2} m até {topY:F2} m; portal no topo.");
            return true;
        }

        /// <summary>Acha o vão da escada no piso (raios para baixo que passam do piso) e põe o portal no topo dela.</summary>
        static bool FindStairsDown(Built b, out Vector3 pos, out Vector3 inward)
        {
            pos = default; inward = default;
            var bb = b.bounds;
            const float step = 0.2f;
            int nx = Mathf.CeilToInt(bb.size.x / step), nz = Mathf.CeilToInt(bb.size.z / step);
            var holeY = new float?[nx, nz];
            var isFloor = new bool[nx, nz];
            Vector3 Cell(int i, int k) => new Vector3(bb.min.x + (i + 0.5f) * step, b.floorY, bb.min.z + (k + 0.5f) * step);
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    Vector3 c = Cell(i, k);
                    if (!Physics.Raycast(c + Vector3.up * 0.3f, Vector3.up, 8f)) continue; // fora do telhado = fora do sótão
                    if (!Physics.Raycast(c + Vector3.up * 1.2f, Vector3.down, out var hit, 6f)) continue;
                    if (Mathf.Abs(hit.point.y - b.floorY) < 0.1f) isFloor[i, k] = true;
                    else if (hit.point.y < b.floorY - 0.25f) holeY[i, k] = hit.point.y;
                }

            // Maior grupo conectado de células "buraco" = vão da escada.
            var seen = new bool[nx, nz];
            List<(int, int)> bestCluster = null;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    if (seen[i, k] || holeY[i, k] == null) continue;
                    var cluster = new List<(int, int)>();
                    var q = new Queue<(int, int)>();
                    q.Enqueue((i, k)); seen[i, k] = true;
                    while (q.Count > 0)
                    {
                        var (a, c) = q.Dequeue();
                        cluster.Add((a, c));
                        foreach (var (da, dc) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int x = a + da, z = c + dc;
                            if (x < 0 || z < 0 || x >= nx || z >= nz || seen[x, z] || holeY[x, z] == null) continue;
                            seen[x, z] = true; q.Enqueue((x, z));
                        }
                    }
                    if (bestCluster == null || cluster.Count > bestCluster.Count) bestCluster = cluster;
                }
            if (bestCluster == null || bestCluster.Count < 6) return false;

            // Degrau mais alto do vão e a célula de piso mais próxima dele = topo da escada.
            var top = bestCluster.OrderByDescending(c => holeY[c.Item1, c.Item2].Value).First();
            Vector3 topPos = Cell(top.Item1, top.Item2);
            Vector3? floorPos = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    if (!isFloor[i, k]) continue;
                    float d = (Cell(i, k) - topPos).sqrMagnitude;
                    if (d < bestD) { bestD = d; floorPos = Cell(i, k); }
                }
            if (floorPos == null) return false;
            inward = floorPos.Value - topPos; inward.y = 0; inward.Normalize();
            pos = (topPos + floorPos.Value) * 0.5f;
            pos.y = b.floorY;
            Debug.Log($"[DAO-BUILD] {b.def.name}: vão da escada com {bestCluster.Count} células; degrau mais alto a {holeY[top.Item1, top.Item2]:F2} m.");
            return true;
        }

        static void WireEffect(Built b, DaoPlayerScale scaler)
        {
            var p = b.proposal;
            switch (b.def.effect)
            {
                case Effect.Scale:
                    UnityEventTools.AddPersistentListener(p.options[0].onWin, scaler.Shrink);
                    UnityEventTools.AddPersistentListener(p.options[1].onWin, scaler.Grow);
                    float height = b.bounds.size.y;
                    scaler.giantScale = Mathf.Clamp((height - 0.4f) / 1.6f, 1.2f, 3f);
                    Debug.Log($"[DAO-BUILD] {b.def.name}: pé-direito {height:F2} m -> giantScale {scaler.giantScale:F2}");
                    break;
                case Effect.Tint:
                    var tint = b.root.AddComponent<DaoRoomTint>();
                    tint.roomRoot = b.root.transform;
                    tint.lights = b.root.GetComponentsInChildren<Light>();
                    tint.palette = new[] { b.def.colA, b.def.colB };
                    UnityEventTools.AddIntPersistentListener(p.options[0].onWin, tint.ApplyPalette, 0);
                    UnityEventTools.AddIntPersistentListener(p.options[1].onWin, tint.ApplyPalette, 1);
                    break;
                case Effect.Gravity:
                    var g = b.root.AddComponent<DaoGravity>();
                    g.affectedRoot = b.root.transform;
                    UnityEventTools.AddPersistentListener(p.options[0].onWin, g.ZeroG);
                    UnityEventTools.AddPersistentListener(p.options[1].onWin, g.Moon);
                    break;
            }
        }

        // ---------- Meta XR SDK ----------

        const string OVRCameraRigGuid = "126d619cf4daa52469682f85c1378b4a";          // com.meta.xr.sdk.core/Prefabs/OVRCameraRig
        const string ComprehensiveRigGuid = "0a7d2469f24041c4284c66706f84c45e";      // interaction.ovr/OVRComprehensiveInteractionRig
        const string PokeTemplateGuid = "5fca3702cdf1a214a94e69dcfa704e17";          // interaction/Prefabs/Poke/PokeInteractable

#if DAO_HAS_META
        /// <summary>
        /// Mesma receita do assistente "Add Comprehensive Interaction Rig" da Meta: OVRCameraRig com tracking a partir do
        /// chão + OVRComprehensiveInteractionRig (mãos, controles, Poke, Grab, Ray, teleporte e giro) dentro dele.
        /// </summary>
        static void CreateMetaRig(out Transform rig, out Transform head)
        {
            var defaultCam = Camera.main;
            if (defaultCam != null) UnityEngine.Object.DestroyImmediate(defaultCam.gameObject);

            var camRig = (GameObject)PrefabUtility.InstantiatePrefab(LoadByGuid<GameObject>(OVRCameraRigGuid));
            camRig.name = "OVRCameraRig (Meta)";
            if (camRig.TryGetComponent(out OVRManager ovrManager))
            {
                var so = new SerializedObject(ovrManager);
                var origin = so.FindProperty("_trackingOriginType");
                if (origin != null) origin.intValue = (int)OVRManager.TrackingOrigin.FloorLevel;
                so.ApplyModifiedPropertiesWithoutUndo();
                ovrManager.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.ConformingToController;
            }
            foreach (var hand in camRig.GetComponentsInChildren<OVRHand>(true))
            {
                // O rig de interação traz as próprias mãos; desliga as duplicadas do OVRCameraRig.
                if (hand.TryGetComponent<OVRSkeletonRenderer>(out var sr)) sr.enabled = false;
                if (hand.TryGetComponent<OVRMesh>(out var mesh)) mesh.enabled = false;
                if (hand.TryGetComponent<OVRMeshRenderer>(out var mr)) mr.enabled = false;
                if (hand.TryGetComponent<SkinnedMeshRenderer>(out var smr)) smr.enabled = false;
            }
            var isdk = (GameObject)PrefabUtility.InstantiatePrefab(LoadByGuid<GameObject>(ComprehensiveRigGuid), camRig.transform);
            isdk.name = "OVRComprehensiveInteractionRig";

            rig = camRig.transform;
            head = camRig.transform.Find("TrackingSpace/CenterEyeAnchor");
            var c = head.GetComponent<Camera>();
            c.nearClipPlane = 0.03f;

            // Igual ao assistente da Meta: dispara o auto-wiring do Interaction SDK (liga OVRCameraRigRef,
            // locomotor, efeitos de túnel etc. ao OVRCameraRig). Depois confere e liga à mão o que ficou vazio.
            Oculus.Interaction.Editor.UnityObjectAddedBroadcaster.HandleObjectWasAdded(isdk);
            int fixedRefs = WireMissingRigReferences(isdk, camRig.GetComponent<OVRCameraRig>(), rig, head, c);
            Debug.Log($"[DAO-BUILD] Rig Meta criado: OVRCameraRig + OVRComprehensiveInteractionRig (auto-wiring + {fixedRefs} referência(s) ligadas à mão).");
        }

        /// <summary>Preenche referências obrigatórias que o auto-wiring possa ter deixado vazias.</summary>
        static int WireMissingRigReferences(GameObject isdk, OVRCameraRig ovrRig, Transform origin, Transform eyes, Camera eyeCam)
        {
            var map = new Dictionary<string, UnityEngine.Object>
            {
                ["_ovrCameraRig"] = ovrRig,
                ["_cameraRig"] = ovrRig,
                ["_playerOrigin"] = origin,
                ["_playerEyes"] = eyes,
                ["_centerEyeCamera"] = eyeCam,
            };
            int count = 0;
            foreach (var comp in isdk.GetComponentsInChildren<Component>(true))
            {
                if (comp == null || comp is Transform) continue;
                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                bool changed = false;
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue != null) continue;
                    if (!map.TryGetValue(it.name, out var value) || value == null) continue;
                    it.objectReferenceValue = value;
                    if (it.objectReferenceValue != null) { changed = true; count++; }
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            return count;
        }
#endif

        /// <summary>
        /// Botão de votação. Com o Meta XR SDK: template oficial de Poke (afunda ao ser tocado pela mão ou controle),
        /// com o InteractableUnityEventWrapper.WhenSelect ligado em DaoButton.Press. Sem o SDK: um cubo simples.
        /// </summary>
        static DaoButton MakePokeButton(Transform parent, string name, Vector3 localPos, Color color)
        {
#if DAO_HAS_META
            var go = (GameObject)PrefabUtility.InstantiatePrefab(LoadByGuid<GameObject>(PokeTemplateGuid), parent);
            go.name = name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // face de toque (-Z) virada para cima
            go.transform.localScale = Vector3.one * 0.11f;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                if (r.transform.parent != null && r.transform.parent.name != "Surface") r.sharedMaterial = ColorMaterial(color);
            var db = go.AddComponent<DaoButton>();
            var visuals = go.transform.Find("Visuals");
            db.visual = visuals != null ? visuals : go.transform;
            // O template da Meta não traz o wrapper de eventos: adiciona e aponta para o PokeInteractable dele.
            var wrapper = go.GetComponentInChildren<InteractableUnityEventWrapper>(true);
            if (wrapper == null)
            {
                var poke = go.GetComponentInChildren<PokeInteractable>(true);
                wrapper = poke.gameObject.AddComponent<InteractableUnityEventWrapper>();
                wrapper.InjectAllInteractableUnityEventWrapper(poke);
                // Componente recém-criado por código: os UnityEvents privados ainda estão nulos.
                foreach (var f in typeof(InteractableUnityEventWrapper).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                    if (f.FieldType == typeof(UnityEvent) && f.GetValue(wrapper) == null) f.SetValue(wrapper, new UnityEvent());
            }
            UnityEventTools.AddPersistentListener(wrapper.WhenSelect, db.Press);
            EditorUtility.SetDirty(wrapper);
            return db;
#else
            var btn = Cube(parent, name, localPos, new Vector3(0.14f, 0.06f, 0.14f), color);
            return btn.AddComponent<DaoButton>();
#endif
        }

        static T LoadByGuid<T>(string guid) where T : UnityEngine.Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new Exception($"Asset do Meta XR SDK não encontrado (GUID {guid}). O pacote está instalado?");
            return asset;
        }

        // ---------- utilidades ----------

        static bool IsTriangleMesh(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            var m = mf.sharedMesh;
            for (int s = 0; s < m.subMeshCount; s++)
                if (m.GetTopology(s) != MeshTopology.Triangles) return false;
            // Malhas sem nenhum triângulo com área (contornos "achatados" do SketchUp) também não servem de collider.
            try
            {
                var v = m.vertices;
                var t = m.triangles;
                for (int i = 0; i + 2 < t.Length; i += 3)
                    if (Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).sqrMagnitude > 1e-12f) return true;
                return false;
            }
            catch { return true; }
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var bb = rs[0].bounds;
            foreach (var r in rs) bb.Encapsulate(r.bounds);
            return bb;
        }

        static void LogStats(string room, string path, GameObject model, float scale, Bounds bounds)
        {
            var rs = model.GetComponentsInChildren<Renderer>();
            long tris = 0;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                for (int s = 0; s < m.subMeshCount; s++) tris += m.GetIndexCount(s) / 3;
            }
            var mats = rs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            var shaders = mats.Select(m => m.shader.name).Distinct();
            var texs = new HashSet<Texture>();
            foreach (var m in mats)
                foreach (var id in m.GetTexturePropertyNameIDs())
                {
                    var t = m.GetTexture(id);
                    if (t != null) texs.Add(t);
                }
            int missingTex = mats.Count(m => m.HasProperty("_MainTex") && m.GetTexture("_MainTex") == null);
            Debug.Log($"[DAO-BUILD] SALA '{room}' ({path})\n" +
                      $"  escala aplicada: {scale}  tamanho (m): {bounds.size.x:F2} x {bounds.size.y:F2} (altura) x {bounds.size.z:F2}\n" +
                      $"  renderers (≈draw calls): {rs.Length}  triângulos: {tris:N0}  materiais: {mats.Count}  texturas: {texs.Count}  materiais sem _MainTex: {missingTex}\n" +
                      $"  shaders: {string.Join(", ", shaders)}\n" +
                      $"  objetos: {string.Join(", ", rs.Take(40).Select(r => r.name))}");
        }

        static GameObject Cube(Transform parent, string name, Vector3 localPos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = ColorMaterial(color);
            return go;
        }

        static readonly Dictionary<Color, Material> matCache = new Dictionary<Color, Material>();

        static Material ColorMaterial(Color c)
        {
            if (matCache.TryGetValue(c, out var m) && m != null) return m;
            string path = $"{Root}/Materials/DAO_{ColorUtility.ToHtmlStringRGB(c)}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool urp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null;
            var shader = (urp ? Shader.Find("Universal Render Pipeline/Lit") : null) ?? Shader.Find("Standard");
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader; // projeto mudou de pipeline (Built-in <-> URP)
            }
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
            matCache[c] = m;
            return m;
        }

        static Material GetFadeMaterial()
        {
            string path = Root + "/Materials/DAO_ScreenFade.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("DAO/ScreenFade"));
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static Material GetGlowMaterial()
        {
            string path = Root + "/Materials/DAO_Glow.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("DAO/Glow"));
                m.SetColor("_Color", PortalColor);
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static DaoText MakeText(Transform parent, string name, string content, Vector3 localPos, float charSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.fontSize = 100;
            tm.characterSize = charSize * TextScale;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = content;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return go.AddComponent<DaoText>();
        }

        static void AddSceneToBuild(string path)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == path)) return;
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void RenderPreviews(string dir, List<Built> rooms, Camera cam)
        {
            Directory.CreateDirectory(dir);
            const int W = 800, H = 500;
            var rt = new RenderTexture(W, H, 24);
            // Câmera temporária só para as prévias (a do rig da Meta faz parte de um prefab e não pode ser reparentada).
            var playerCam = cam;
            cam = new GameObject("Preview Camera").AddComponent<Camera>();
            if (playerCam != null) playerCam.enabled = false;
            cam.nearClipPlane = 0.03f;
            cam.targetTexture = rt;
            cam.fieldOfView = 80f;

            foreach (var target in rooms)
            {
                foreach (var r in rooms) r.root.SetActive(r == target);
                var sheet = new Texture2D(W * 2, H * 2, TextureFormat.RGB24, false);
                for (int v = 0; v < 4; v++)
                {
                    float yaw = target.spawn.eulerAngles.y + v * 90f;
                    cam.transform.SetPositionAndRotation(target.spawn.position + Vector3.up * 1.6f, Quaternion.Euler(10f, yaw, 0));
                    cam.Render();
                    RenderTexture.active = rt;
                    var tile = new Texture2D(W, H, TextureFormat.RGB24, false);
                    tile.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tile.Apply();
                    sheet.SetPixels((v % 2) * W, (1 - v / 2) * H, W, H, tile.GetPixels());
                    UnityEngine.Object.DestroyImmediate(tile);
                }
                sheet.Apply();
                string file = Path.Combine(dir, "preview_" + Regex.Replace(target.def.name, @"[^\w]", "_") + ".png");
                File.WriteAllBytes(file, sheet.EncodeToPNG());
                Debug.Log("[DAO-BUILD] preview: " + file);

                // Vista do portal: 2,4 m para dentro da sala, olhando para a saída.
                if (target.portal != null)
                {
                    var pt = target.portal.transform;
                    Vector3 eye = pt.position - pt.forward * 2.4f + Vector3.up * 1.6f;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(pt.position + Vector3.up * 0.9f - eye));
                    cam.Render();
                    RenderTexture.active = rt;
                    var shot = new Texture2D(W, H, TextureFormat.RGB24, false);
                    shot.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    shot.Apply();
                    string pf = Path.Combine(dir, "portal_" + Regex.Replace(target.def.name, @"[^\w]", "_") + ".png");
                    File.WriteAllBytes(pf, shot.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(shot);
                }
            }
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(cam.gameObject);
            if (playerCam != null) playerCam.enabled = true;
            foreach (var r in rooms) r.root.SetActive(true);
        }
    }
}
