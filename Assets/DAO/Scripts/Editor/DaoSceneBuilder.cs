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
            public DaoVotingCurve curve;
            public (string name, int tokens, int option)[] members = new (string, int, int)[0];
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
                // Linear: A 3+3 = 6 x B 2+4 = 6 -> empate; o voto do visitante decide.
                curve = DaoVotingCurve.Linear,
                members = new[] { ("Ana", 3, 0), ("Bia", 2, 1), ("Caio", 4, 1), ("Duda", 3, 0) },
                proposalTitle = "Proposta #1 - Tamanho", proposalDesc = "Como a DAO quer que você explore a sala?",
                optA = "Minúsculo", colA = new Color(0.25f, 0.6f, 1f), optB = "Gigante", colB = new Color(1f, 0.55f, 0.2f) },
            new RoomDef { name = "Cozinha", modelFile = "Cartoon Kitchen Interior", scale = 1f, effect = Effect.Gravity,
                floorY = 0.62f, addCeiling = true, fixFloating = true, closeOpenWalls = true,
                exit = ExitKind.WallOpening, exitTo = 2, exitName = "Arco",
                // Exponencial: A 1+1+1 = 3 x Baleia 5 tokens = 31. Só vence a baleia com 5+ tokens (moedas!).
                curve = DaoVotingCurve.Exponencial,
                members = new[] { ("Ana", 1, 0), ("Bia", 1, 0), ("Caio", 1, 0), ("Baleia", 5, 1) },
                proposalTitle = "Proposta #2 - Física", proposalDesc = "Qual gravidade vale na cozinha?",
                optA = "Gravidade zero", colA = new Color(0.6f, 0.3f, 1f), optB = "Gravidade lunar", colB = new Color(0.7f, 0.7f, 0.75f) },
            new RoomDef { name = "Sótão", modelFile = "old_attic", scale = 1f, effect = Effect.Tint,
                floorY = 4.04f,
                exit = ExitKind.StairsMesh, exitMeshName = "FrontSide_8", exitTo = 0, exitName = "Escada",
                // Exponencial invertida (log2): A 1+1+2 = 4 x Baleia 15 tokens = 4 -> empate; muitos pequenos igualam a baleia.
                curve = DaoVotingCurve.ExponencialInvertida,
                members = new[] { ("Ana", 1, 0), ("Bia", 1, 0), ("Caio", 3, 0), ("Baleia", 15, 1) },
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
            public List<Vector3> clear = new List<Vector3>();
            public DaoToken[] tokens = new DaoToken[0];
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

            systems.AddComponent<DaoJump>();
            popupsParent = new GameObject("Popups").transform;
            popupsParent.SetParent(systems.transform, false);
            var map = BuildMapMenu(Defs.Select(d => d.name).ToArray());
            BuildHud(head, systems, out var hudTally, out var hudStatus);

            // Portas, moedas, votação e painéis (precisam conhecer todas as salas).
            var ledgerTexts = new List<DaoText>();
            for (int i = 0; i < rooms.Count; i++)
            {
                var b = rooms[i];
                BuildPortal(b, rooms[b.def.exitTo].def.name);
                BuildTokens(b, 3);
                b.proposal = BuildVoteStation(b, ledgerTexts, map, hudTally, hudStatus, out var info);
                WireEffect(b, scaler);
#if DAO_HAS_META
                foreach (var rb in b.root.GetComponentsInChildren<Rigidbody>()) MakeGrabbable(rb);
#endif

                mgr.rooms.Add(new DaoRoom { name = b.def.name, root = b.root, spawnPoint = b.spawn, proposal = b.proposal, infoPopup = info });
                var room = mgr.rooms[i];
                UnityEventTools.AddPersistentListener(room.onEnter, info.Show); // explicações cobrem a visão ao entrar
                UnityEventTools.AddPersistentListener(room.onExit, info.Hide);
                UnityEventTools.AddPersistentListener(room.onExit, map.Hide);
                UnityEventTools.AddPersistentListener(room.onExit, scaler.ResetScaleInstant);
                var grav = b.root.GetComponent<DaoGravity>();
                if (grav != null) UnityEventTools.AddPersistentListener(room.onExit, grav.Restore);
            }
            ledger.ledgerTexts = ledgerTexts.ToArray();
            ledger.proposalsToComplete = rooms.Count;

            BuildCompletionPopup(systems.transform);

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

            BuildSafetyColliders(b, floorY);

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
        /// Colisões de segurança: chão grosso (1 m) na altura do piso e quatro paredes invisíveis em volta da sala.
        /// Junto com as MeshColliders do modelo e o anti-queda do DaoRoomManager, ninguém cai para fora do mapa.
        /// </summary>
        static void BuildSafetyColliders(Built b, float floorY)
        {
            var bb = b.bounds;
            var group = new GameObject("Colisoes de seguranca").transform;
            group.SetParent(b.root.transform, false);
            void Box(string name, Vector3 center, Vector3 size)
            {
                var go = new GameObject(name);
                go.layer = 2; // Ignore Raycast: não atrapalha a detecção de portas nem cliques
                go.transform.SetParent(group, false);
                go.transform.position = center;
                go.AddComponent<BoxCollider>().size = size;
            }
            Box("Chao grosso", new Vector3(bb.center.x, floorY - 0.5f, bb.center.z), new Vector3(bb.size.x + 1f, 1f, bb.size.z + 1f));
            float h = bb.max.y - floorY + 2f;
            float cy = floorY + h * 0.5f - 0.5f;
            const float t = 0.5f;
            Box("Parede invisivel +X", new Vector3(bb.max.x + t * 0.5f, cy, bb.center.z), new Vector3(t, h + 1f, bb.size.z + 2f * t));
            Box("Parede invisivel -X", new Vector3(bb.min.x - t * 0.5f, cy, bb.center.z), new Vector3(t, h + 1f, bb.size.z + 2f * t));
            Box("Parede invisivel +Z", new Vector3(bb.center.x, cy, bb.max.z + t * 0.5f), new Vector3(bb.size.x + 2f * t, h + 1f, t));
            Box("Parede invisivel -Z", new Vector3(bb.center.x, cy, bb.min.z - t * 0.5f), new Vector3(bb.size.x + 2f * t, h + 1f, t));
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
            b.clear = clear;

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

        static readonly Color Ink = new Color(0.07f, 0.09f, 0.16f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.15f);

        static Transform PanelFacingSpawn(Built b, string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(b.spawn, false);
            t.localPosition = local;
            t.localRotation = Quaternion.LookRotation(new Vector3(local.x, 0, local.z));
            return t;
        }

        static DaoProposal BuildVoteStation(Built b, List<DaoText> ledgerTexts, DaoMapMenu map, DaoText hudTally, DaoText hudStatus, out DaoPanelPopup info)
        {
            var st = new GameObject("Estacao de Votacao").transform;
            st.SetParent(b.spawn, false);
            st.localPosition = new Vector3(0, 0, 1.1f);

            // Palanque com colisão (não dá para atravessar).
            Cube(st, "Pedestal", new Vector3(0, 0.45f, 0), new Vector3(1.05f, 0.9f, 0.4f), new Color(0.12f, 0.12f, 0.2f));

            var proposal = st.gameObject.AddComponent<DaoProposal>();
            proposal.title = b.def.proposalTitle;
            proposal.description = b.def.proposalDesc;
            proposal.curve = b.def.curve;
            proposal.members = b.def.members.Select(m => new DaoMember { name = m.name, tokens = m.tokens, option = m.option }).ToList();
            proposal.options = new List<DaoOption>
            {
                new DaoOption { label = b.def.optA, color = b.def.colA },
                new DaoOption { label = b.def.optB, color = b.def.colB },
            };
            // Placar e status vão para o HUD (canto da visão); o resto vai para o pop-up de informações da sala.
            proposal.tallyText = hudTally;
            proposal.statusText = hudStatus;

            // Palanque só com botões: A, B, INFO e MAPAS.
            var green = new Color(0.25f, 0.8f, 0.45f);
            var gray = new Color(0.6f, 0.62f, 0.7f);
            float[] xs = { -0.32f, 0f, 0.32f };
            for (int k = 0; k < 2; k++)
            {
                var opt = proposal.options[k];
                var db = MakePokeButton(st, "Botao " + (k == 0 ? "A" : "B") + " - " + opt.label, new Vector3(xs[k], 0.92f, 0), opt.color, Quaternion.Euler(90f, 0f, 0f));
                UnityEventTools.AddIntPersistentListener(db.onPress, proposal.Vote, k);
                MakeText(st, "Rotulo " + k, (k == 0 ? "A: " : "B: ") + opt.label, new Vector3(xs[k], 1.07f, 0.05f), 0.0014f, Ink, opt.color);
            }

            info = BuildInfoPopup(b, proposal, map, ledgerTexts);
            var infoBtn = MakePokeButton(st, "Botao INFO", new Vector3(xs[2], 0.92f, 0), green, Quaternion.Euler(90f, 0f, 0f));
            UnityEventTools.AddPersistentListener(infoBtn.onPress, info.Show);
            MakeText(st, "Rotulo INFO", "INFO", new Vector3(xs[2], 1.07f, 0.05f), 0.0014f, Ink, green);
            return proposal; // MAPAS fica no controle (botão B) e na tecla M
        }

        /// <summary>Espalha moedas de token pelo piso livre da sala (entre 1,5 e 4 m do spawn, longe da estação).</summary>
        static void BuildTokens(Built b, int count)
        {
            var spawn = b.spawn.position;
            var station = b.spawn.TransformPoint(new Vector3(0, 0, 1.1f));
            var portal = b.portal != null ? b.portal.transform.position : new Vector3(9999, 0, 0);
            var candidates = b.clear
                .Where(p => { float d = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(spawn.x, 0, spawn.z)); return d > 1.5f && d < 4.5f; })
                .Where(p => Vector3.Distance(p, station) > 1.2f && Vector3.Distance(p, portal) > 1.5f)
                .ToList();
            var chosen = new List<Vector3>();
            var rnd = new System.Random(b.def.name.Length * 7919);
            while (chosen.Count < count && candidates.Count > 0)
            {
                var p = candidates[rnd.Next(candidates.Count)];
                candidates.RemoveAll(c => Vector3.Distance(c, p) < 1.5f);
                chosen.Add(p);
            }
            var list = new List<DaoToken>();
            for (int i = 0; i < chosen.Count; i++)
            {
                var go = new GameObject("Moeda de token " + (i + 1));
                go.transform.SetParent(b.root.transform, false);
                go.transform.position = chosen[i] + Vector3.up * 0.9f;
                var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                coin.name = "Moeda";
                UnityEngine.Object.DestroyImmediate(coin.GetComponent<Collider>());
                coin.transform.SetParent(go.transform, false);
                coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                coin.transform.localScale = new Vector3(0.22f, 0.02f, 0.22f);
                coin.GetComponent<MeshRenderer>().sharedMaterial = EmissiveMaterial(Gold);
                var visual = new GameObject("Visual").transform;
                visual.SetParent(go.transform, false);
                coin.transform.SetParent(visual, true);
                var sc = go.AddComponent<SphereCollider>();
                sc.radius = 0.18f;
                sc.isTrigger = true;
                go.AddComponent<DaoButton>(); // clique (desktop) ou toque coleta
                var token = go.AddComponent<DaoToken>();
                token.visual = visual;
                token.toast = MakeText(go.transform, "Toast", "+1 token", new Vector3(0, 0.3f, 0), 0.0018f, Ink, Gold);
                UnityEventTools.AddPersistentListener(go.GetComponent<DaoButton>().onPress, token.Collect);
                list.Add(token);
            }
            b.tokens = list.ToArray();
            Debug.Log($"[DAO-BUILD] {b.def.name}: {list.Count} moedas de token.");
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
            portal.labelText = MakeText(go.transform, "Rotulo", portal.label + "\nfique na luz", new Vector3(0, 2.55f, 0), 0.0024f, Ink, PortalColor);
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
                    scaler.worldRoot = b.root.transform; // escala a sala, não o jogador
                    scaler.statusText = p.statusText;
                    UnityEventTools.AddPersistentListener(p.options[0].onWin, scaler.Shrink);
                    UnityEventTools.AddPersistentListener(p.options[1].onWin, scaler.Grow);
                    float height = b.bounds.size.y;
                    // Gigante moderado: com 1,5x o teto continua acima da cabeça e dá para andar pela sala.
                    scaler.giantScale = Mathf.Clamp((height - 0.4f) / 1.6f, 1.2f, 1.5f);
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
        static DaoButton MakePokeButton(Transform parent, string name, Vector3 localPos, Color color, Quaternion localRotation)
        {
#if DAO_HAS_META
            var go = (GameObject)PrefabUtility.InstantiatePrefab(LoadByGuid<GameObject>(PokeTemplateGuid), parent);
            go.name = name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRotation; // face de toque (-Z) virada para o jogador
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

#if DAO_HAS_META
        const string HandGrabTemplateGuid = "6ee61821e0d5b094a8d732834b365b21"; // interaction/ISDK_HandGrabInteraction

        /// <summary>Mesma receita do "Add Grab Interaction" da Meta: Grabbable + HandGrab/Grab interactables (mãos e controles).</summary>
        static void MakeGrabbable(Rigidbody rb)
        {
            var target = rb.gameObject;
            if (target.GetComponent<Grabbable>() != null) return;
            var grabbable = target.AddComponent<Grabbable>();
            grabbable.InjectOptionalTargetTransform(target.transform);
            var obj = (GameObject)UnityEngine.Object.Instantiate(LoadByGuid<GameObject>(HandGrabTemplateGuid));
            obj.name = "ISDK_HandGrabInteraction";
            obj.transform.SetParent(target.transform, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;
            var hand = obj.GetComponent<Oculus.Interaction.HandGrab.HandGrabInteractable>();
            if (hand != null)
            {
                hand.InjectRigidbody(rb);
                hand.InjectSupportedGrabTypes(Oculus.Interaction.Grab.GrabTypeFlags.All);
                hand.InjectOptionalPointableElement(grabbable);
            }
            var grab = obj.GetComponent<GrabInteractable>();
            if (grab != null)
            {
                grab.InjectRigidbody(rb);
                grab.InjectOptionalPointableElement(grabbable);
            }
        }
#endif

        /// <summary>
        /// Faz um pop-up (ou o HUD) ser desenhado por cima de tudo: nada da sala atravessa e o palanque não corta.
        /// Ordem: fundo < borda < painel < botões < texto (renderQueue a partir de baseQueue).
        /// </summary>
        static void MakeOverlay(GameObject root, int baseQueue)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponent<TextMesh>() != null)
                {
                    string tp = $"{Root}/Materials/DAO_TextOverlay_{baseQueue}.mat";
                    var tm = AssetDatabase.LoadAssetAtPath<Material>(tp);
                    if (tm == null) { tm = new Material(font.material); AssetDatabase.CreateAsset(tm, tp); }
                    tm.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
                    tm.renderQueue = baseQueue + 5;
                    EditorUtility.SetDirty(tm);
                    r.sharedMaterial = tm;
                    continue;
                }
                var src = r.sharedMaterial;
                Color c = src == null ? Color.white : src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.color : Color.white;
                int q = r.name == "Moldura" ? 0 : r.name == "Fundo" ? 1 : r.name == "Borda" ? 2 : r.name == "Painel" ? 3 : 4;
                string path = $"{Root}/Materials/DAO_Overlay_{baseQueue + q}_{ColorUtility.ToHtmlStringRGB(c)}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("DAO/Overlay")); AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_Color", c);
                m.renderQueue = baseQueue + q;
                EditorUtility.SetDirty(m);
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static readonly Color Blue = new Color(0.25f, 0.45f, 0.9f);
        static readonly Color Green = new Color(0.25f, 0.8f, 0.45f);
        static readonly Color Gray = new Color(0.6f, 0.62f, 0.7f);
        static Transform popupsParent;

        /// <summary>
        /// Pop-up grande de cada sala (abre ao entrar, cobre a visão): proposta, curva de governança, membros,
        /// carteira, livro-razão e como jogar. Fica fora da sala (não é escalado no minúsculo/gigante).
        /// </summary>
        static DaoPanelPopup BuildInfoPopup(Built b, DaoProposal proposal, DaoMapMenu map, List<DaoText> ledgerTexts)
        {
            var holder = new GameObject("Popup Informacoes - " + b.def.name).transform;
            holder.SetParent(popupsParent, false);
            var popup = holder.gameObject.AddComponent<DaoPanelPopup>();
            var root = new GameObject("Conteudo");
            root.transform.SetParent(holder, false);
            popup.root = root;

            // Fundo enorme: cobre a visão toda (desenhado por cima da sala).
            popup.distance = 1.6f;
            root.transform.localScale = Vector3.one * 1.3f;
            var bg = PanelQuad(root.transform, "Fundo", new Color(0.9f, 0.92f, 0.96f));
            bg.transform.localPosition = new Vector3(0, 0, 0.06f);
            bg.transform.localScale = new Vector3(12f, 8f, 1f);

            proposal.titleText = MakeText(root.transform, "Titulo", proposal.title + "\n" + proposal.description, new Vector3(0, 0.8f, 0), 0.0021f, Ink, Blue);
            proposal.curveText = MakeText(root.transform, "Curva", DaoProposal.CurveName(proposal.curve), new Vector3(0, 0.52f, 0), 0.0015f, Ink, Gold);
            proposal.membersText = MakeText(root.transform, "Membros", "MEMBROS DA DAO", new Vector3(-0.6f, 0.13f, 0), 0.0012f, Ink, Blue);
            var wallet = holder.gameObject.AddComponent<DaoWalletPanel>();
            wallet.proposal = proposal;
            wallet.roomTokens = b.tokens;
            wallet.text = MakeText(root.transform, "Carteira", "SUA CARTEIRA", new Vector3(0.6f, 0.1f, 0), 0.0012f, Ink, Gold);
            ledgerTexts.Add(MakeText(root.transform, "Livro-Razao", "LIVRO-RAZÃO DA DAO", new Vector3(-0.6f, -0.43f, 0), 0.0011f, Ink, Green));
            MakeText(root.transform, "Como jogar",
                "COMO JOGAR\n" +
                "Vote no palanque: botão A ou B (toque ou clique).\n" +
                "Moedas douradas: chegue perto ou clique = +1 token.\n" +
                "Pare na luz verde da porta/escada para trocar de sala.\n" +
                "INFO (palanque) reabre este painel.\n" +
                "Mapas: botão B do controle ou tecla M.\n" +
                "Pular: botão A do controle ou Espaço.\n" +
                "Teclado: WASD anda · botão direito + mouse olha",
                new Vector3(0.6f, -0.43f, 0), 0.0011f, Ink, Gray);

            var close = MakePokeButton(root.transform, "Botao Fechar", new Vector3(0, -0.8f, 0), Green, Quaternion.identity);
            UnityEventTools.AddPersistentListener(close.onPress, popup.Hide);
            MakeText(root.transform, "Rotulo Fechar", "FECHAR E JOGAR  (botão A do controle ou clique aqui)", new Vector3(0, -0.9f, 0), 0.0013f, Ink, Green);
            MakeOverlay(root, 4000);
            root.SetActive(false);
            return popup;
        }

        /// <summary>Menu de mapas: um botão por sala para ir direto (e FECHAR).</summary>
        static DaoMapMenu BuildMapMenu(string[] roomNames)
        {
            var holder = new GameObject("Popup Mapas").transform;
            holder.SetParent(popupsParent, false);
            var popup = holder.gameObject.AddComponent<DaoPanelPopup>();
            popup.distance = 0.55f; // ao alcance da mão (poke); desenhado por cima de tudo
            popup.heightOffset = -0.12f;
            var menu = holder.gameObject.AddComponent<DaoMapMenu>();
            menu.popup = popup;
            var root = new GameObject("Conteudo");
            root.transform.SetParent(holder, false);
            root.transform.localScale = Vector3.one * 0.6f;
            popup.root = root;
            var bg = PanelQuad(root.transform, "Fundo", new Color(0.9f, 0.92f, 0.96f));
            bg.transform.localPosition = new Vector3(0, 0, 0.06f);
            bg.transform.localScale = new Vector3(12f, 8f, 1f);
            MakeText(root.transform, "Titulo", "MAPAS\nEscolha uma sala para ir direto", new Vector3(0, 0.3f, 0), 0.0018f, Ink, Gray);
            for (int i = 0; i < roomNames.Length; i++)
            {
                float x = (i - (roomNames.Length - 1) * 0.5f) * 0.45f;
                var color = i == 0 ? new Color(0.25f, 0.6f, 1f) : i == 1 ? new Color(0.6f, 0.3f, 1f) : new Color(1f, 0.6f, 0.25f);
                var btn = MakePokeButton(root.transform, "Ir para " + roomNames[i], new Vector3(x, 0.02f, 0), color, Quaternion.identity);
                UnityEventTools.AddIntPersistentListener(btn.onPress, menu.Go, i);
                MakeText(root.transform, "Rotulo " + i, $"{i + 1}. {roomNames[i]}", new Vector3(x, -0.09f, 0), 0.0014f, Ink, color);
            }
            var close = MakePokeButton(root.transform, "Botao Fechar", new Vector3(0, -0.28f, 0), Gray, Quaternion.identity);
            UnityEventTools.AddPersistentListener(close.onPress, menu.Hide);
            MakeText(root.transform, "Rotulo Fechar", "FECHAR (botão B)", new Vector3(0, -0.38f, 0), 0.0013f, Ink, Gray);
            MakeOverlay(root, 4100);
            root.SetActive(false);
            return menu;
        }

        /// <summary>HUD pequeno preso à cabeça, no canto inferior direito: carteira, placar e status.</summary>
        static void BuildHud(Transform head, GameObject systems, out DaoText tally, out DaoText status)
        {
            var hudRoot = new GameObject("HUD (canto da visao)").transform;
            hudRoot.SetParent(head, false);
            hudRoot.localPosition = new Vector3(0.24f, -0.16f, 0.7f);
            var hud = systems.AddComponent<DaoHud>();
            hud.hudRoot = hudRoot.gameObject;
            hud.walletText = MakeText(hudRoot, "HUD Carteira", "Tokens", new Vector3(0, 0.042f, 0), 0.0004f, Ink, Gold);
            tally = MakeText(hudRoot, "HUD Placar", "PLACAR", new Vector3(0, 0f, 0), 0.0004f, Ink, Green);
            status = MakeText(hudRoot, "HUD Status", "", new Vector3(0, -0.045f, 0), 0.00038f, Ink, Gray);
            foreach (var dt in new[] { hud.walletText, tally, status }) { dt.padding *= 0.5f; dt.borderWidth = 0.004f; }
            hud.tallyText = tally;
            hud.statusText = status;
            MakeOverlay(hudRoot.gameObject, 3900);
        }

        /// <summary>Pop-up de finalização (escondido) com resumo e botões Reiniciar / Continuar explorando.</summary>
        static void BuildCompletionPopup(Transform parent)
        {
            var popup = parent.gameObject.AddComponent<DaoCompletionPopup>();
            var root = new GameObject("Popup Desafio Concluido");
            root.transform.SetParent(parent, false);
            popup.popupRoot = root;
            popup.summaryText = MakeText(root.transform, "Resumo", "DESAFIO CONCLUÍDO!", new Vector3(0, 0.12f, 0), 0.0022f, Ink, Gold);
            var green = new Color(0.25f, 0.8f, 0.45f);
            var gray = new Color(0.6f, 0.62f, 0.7f);
            var restart = MakePokeButton(root.transform, "Botao Reiniciar", new Vector3(-0.18f, -0.32f, 0), green, Quaternion.identity);
            UnityEventTools.AddPersistentListener(restart.onPress, popup.Restart);
            MakeText(root.transform, "Rotulo Reiniciar", "REINICIAR", new Vector3(-0.18f, -0.42f, 0), 0.0018f, Ink, green);
            var close = MakePokeButton(root.transform, "Botao Continuar", new Vector3(0.18f, -0.32f, 0), gray, Quaternion.identity);
            UnityEventTools.AddPersistentListener(close.onPress, popup.Hide);
            MakeText(root.transform, "Rotulo Continuar", "CONTINUAR\nEXPLORANDO", new Vector3(0.18f, -0.44f, 0), 0.0016f, Ink, gray);
            // Cobre a visão e fica ao alcance da mão (poke), desenhado por cima de tudo.
            var bg = PanelQuad(root.transform, "Fundo", new Color(0.9f, 0.92f, 0.96f));
            bg.transform.localPosition = new Vector3(0, 0, 0.06f);
            bg.transform.localScale = new Vector3(12f, 8f, 1f);
            root.transform.localScale = Vector3.one * 0.6f;
            popup.distance = 0.6f;
            MakeOverlay(root, 4200);
            root.SetActive(false);
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

        /// <summary>Material sem iluminação (painel branco e borda): sempre legível, mesmo em sala escura.</summary>
        static Material UnlitMaterial(Color c, string prefix = "DAO_Unlit")
        {
            string path = $"{Root}/Materials/{prefix}_{ColorUtility.ToHtmlStringRGB(c)}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material EmissiveMaterial(Color c) => UnlitMaterial(c, "DAO_Emissivo");

        static GameObject PanelQuad(Transform parent, string name, Color c)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            UnityEngine.Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = UnlitMaterial(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q;
        }

        /// <summary>Texto escuro sobre um painel branco com borda colorida (o painel se ajusta ao texto sozinho).</summary>
        static DaoText MakeText(Transform parent, string name, string content, Vector3 localPos, float charSize, Color color, Color border)
        {
            var dt = MakeText(parent, name, content, localPos, charSize, color);
            dt.panel = PanelQuad(dt.transform, "Painel", new Color(0.98f, 0.98f, 0.96f)).transform;
            dt.panelBorder = PanelQuad(dt.transform, "Borda", border).transform;
            dt.padding = new Vector2(0.04f, 0.03f) * (charSize / 0.0018f);
            dt.FitPanel(true);
            return dt;
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
