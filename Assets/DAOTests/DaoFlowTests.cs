using System;
using System.Collections;
using System.Linq;
using DAO;
using NUnit.Framework;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Percorre a experiência inteira no Editor (sem headset): voto pelo evento do Poke da Meta, efeitos de cada sala,
/// portais iluminados nas saídas reais (ficar na luz teleporta) e tela de conclusão.
/// </summary>
public class DaoFlowTests
{
    /// <summary>
    /// No modo batch (testes sem janela) o WaitForEndOfFrame não roda, e é nele que o FirstPersonLocomotor da Meta
    /// move o jogador até a cápsula. Este "shim" chama o mesmo LastUpdate a cada frame, só durante o teste.
    /// </summary>
    class EndOfFrameShim : MonoBehaviour
    {
        public Oculus.Interaction.Locomotion.FirstPersonLocomotor locomotor;
        static readonly System.Reflection.MethodInfo LastUpdate = typeof(Oculus.Interaction.Locomotion.FirstPersonLocomotor)
            .GetMethod("LastUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        void LateUpdate() { if (locomotor != null && locomotor.isActiveAndEnabled) LastUpdate.Invoke(locomotor, null); }
    }

    static IEnumerator WaitUntil(Func<bool> cond, float timeout, string what)
    {
        float t = 0f;
        while (!cond())
        {
            t += Time.deltaTime;
            if (t > timeout) Assert.Fail("Timeout esperando: " + what);
            yield return null;
        }
    }

    static float HeadDistXZ(DaoRoomManager m, Transform target)
    {
        var d = m.head.position - target.position;
        d.y = 0;
        return d.magnitude;
    }

    static IEnumerator StandInPortal(DaoRoomManager mgr, int roomIndex, int expectedTarget)
    {
        var portal = mgr.rooms[roomIndex].root.GetComponentInChildren<DaoPortal>();
        Assert.NotNull(portal, $"portal na sala {roomIndex}");
        Assert.AreEqual(expectedTarget, portal.targetRoom);
        yield return new WaitForSeconds(portal.armDelay + 0.1f);
        mgr.TeleportTo(portal.transform);          // "anda" até a luz (pelo locomotor da Meta)
        for (int i = 0; i < 3; i++) yield return null;
        Debug.Log($"[DAO-TEST] portal {portal.transform.position} cabeça {mgr.head.position} rig {mgr.playerRig.position} " +
                  $"locomotor={mgr.playerRig.GetComponentInChildren<Oculus.Interaction.Locomotion.FirstPersonLocomotor>()?.isActiveAndEnabled}");
        yield return WaitUntil(() => portal.Charge > 0.3f || mgr.IsTransitioning, 3f, "portal carregar");
        yield return WaitUntil(() => mgr.CurrentRoomIndex == expectedTarget && !mgr.IsTransitioning, 6f, $"teleportar para a sala {expectedTarget}");
    }

    [UnityTest]
    public IEnumerator FluxoCompletoDasTresSalas()
    {
        LogAssert.ignoreFailingMessages = true; // OVRManager reclama de não haver headset no Editor em batch
        DaoDesktopMode.AllowAuto = false;      // este teste usa o rig VR da Meta
        var op = SceneManager.LoadSceneAsync("DAO_3Salas", LoadSceneMode.Single);
        while (!op.isDone) yield return null;
        for (int i = 0; i < 5; i++) yield return null;

        var mgr = DaoRoomManager.Instance;
        var ledger = DaoLedger.Instance;
        var scaler = UnityEngine.Object.FindFirstObjectByType<DaoPlayerScale>();
        Assert.NotNull(mgr, "DaoRoomManager"); Assert.NotNull(ledger, "DaoLedger"); Assert.NotNull(scaler, "DaoPlayerScale");
        Assert.NotNull(mgr.playerRig.GetComponent<OVRCameraRig>(), "rig é o OVRCameraRig da Meta");
        var loco = mgr.playerRig.GetComponentInChildren<Oculus.Interaction.Locomotion.FirstPersonLocomotor>();
        Assert.NotNull(loco, "rig tem o FirstPersonLocomotor");
        Assert.IsTrue(loco.isActiveAndEnabled, "locomotor ativo (referências ligadas pelo auto-wiring)");
        var shim = new GameObject("EndOfFrameShim").AddComponent<EndOfFrameShim>();
        shim.locomotor = loco;
        UnityEngine.Object.DontDestroyOnLoad(shim.gameObject);
        Assert.AreEqual(new[] { "Sala de Estar", "Cozinha", "Sótão" }, mgr.rooms.Select(r => r.name).ToArray());
        Assert.AreEqual(0, mgr.CurrentRoomIndex);
        Assert.Less(HeadDistXZ(mgr, mgr.rooms[0].spawnPoint), 0.1f, "cabeça no spawn da sala");
        Assert.IsFalse(mgr.rooms[1].root.activeSelf, "salas inativas desligadas");

        // Sala: voto "Minúsculo" disparado pelo evento real do botão de Poke da Meta.
        var p0 = mgr.rooms[0].proposal;
        var btnA = p0.GetComponentsInChildren<DaoButton>(true).First(b => b.name.StartsWith("Botao A"));
        var pokeA = btnA.GetComponentInChildren<InteractableUnityEventWrapper>(true);
        Assert.NotNull(pokeA, "botão A é um Poke do Interaction SDK");
        pokeA.WhenSelect.Invoke();
        yield return WaitUntil(() => p0.Executed, 10f, "proposta 1 executar");
        Assert.AreEqual(0, p0.Winner, "voto do visitante decide o empate");
        yield return new WaitForSeconds(scaler.duration + 0.3f);
        Assert.AreEqual(scaler.tinyScale, scaler.CurrentScale, 0.01f, "sala cresceu (jogador parece minúsculo)"); Assert.AreEqual(1f, mgr.playerRig.localScale.x, 0.001f, "o rig do jogador não é escalado");
        Assert.AreEqual(2, ledger.Entries.Count, "voto + execução no livro-razão");

        // Porta da sala -> Cozinha (ficando na luz do portal).
        yield return StandInPortal(mgr, 0, 1);
        Assert.AreEqual(1f, mgr.rooms[0].root.transform.localScale.x, 0.001f, "sala volta ao tamanho normal ao sair");
        Assert.Less(HeadDistXZ(mgr, mgr.rooms[1].spawnPoint), 0.6f, "cabeça perto do spawn da cozinha (a cápsula da Meta desvia de móveis)");
        Assert.IsFalse(mgr.rooms[0].root.activeSelf);
        Assert.AreEqual(0f, UnityEngine.Object.FindFirstObjectByType<DaoScreenFade>().Alpha, 0.01f, "fade terminou");

        // Cozinha: gravidade zero.
        var props = mgr.rooms[1].root.GetComponentsInChildren<Rigidbody>();
        Assert.Greater(props.Length, 5, "cozinha tem objetos com física");
        yield return new WaitForSeconds(1.5f);
        var probe = props.First(rb => rb.name.StartsWith("Pan") || rb.name.StartsWith("Pot"));
        float y0 = probe.position.y;
        var p1 = mgr.rooms[1].proposal;
        ledger.AddTokens(6); p1.Vote(0);
        yield return WaitUntil(() => p1.Executed, 10f, "proposta 2 executar");
        Assert.AreEqual(Vector3.zero, Physics.gravity);
        yield return new WaitForSeconds(1f);
        Assert.Greater(probe.position.y, y0 + 0.05f, "panela flutuou");

        // Arco da cozinha -> Sótão.
        yield return StandInPortal(mgr, 1, 2);
        Assert.AreEqual(-9.81f, Physics.gravity.y, 0.01f, "gravidade restaurada ao sair da cozinha");
        Assert.AreEqual(mgr.rooms[2].spawnPoint.position.y, mgr.playerRig.position.y, 0.15f, "pés no piso do sótão (4 m)");

        // Sótão: noite azul.
        var tint = mgr.rooms[2].root.GetComponent<DaoRoomTint>();
        var p2 = mgr.rooms[2].proposal;
        p2.Vote(1);
        yield return WaitUntil(() => p2.Executed, 10f, "proposta 3 executar");
        yield return new WaitForSeconds(tint.duration + 0.2f);
        Assert.Less(Vector4.Distance(tint.lights[0].color, tint.palette[1]), 0.02f, "luz ficou azul");

        Assert.AreEqual(3, ledger.ExecutedCount);
        var popup = UnityEngine.Object.FindFirstObjectByType<DaoCompletionPopup>();
        Assert.IsTrue(popup.popupRoot.activeSelf, "pop-up de conclusão");

        // Escada do sótão -> Sala (fecha o ciclo).
        yield return StandInPortal(mgr, 2, 0);
        Assert.Less(HeadDistXZ(mgr, mgr.rooms[0].spawnPoint), 0.6f, "de volta à sala");
        popup.Restart();
        yield return new WaitForSeconds(1.5f);
        Assert.AreEqual(0, ledger.ExecutedCount, "reinício zera o livro-razão");
        Assert.AreEqual(1, ledger.Tokens, "reinício volta a 1 token");
        Assert.IsFalse(mgr.rooms[0].proposal.HasVoted, "pode votar de novo");
        DaoDesktopMode.AllowAuto = true;
        UnityEngine.Object.Destroy(shim.gameObject);
    }

    [UnityTest]
    public IEnumerator ModoDesktopSemOculos()
    {
        LogAssert.ignoreFailingMessages = true;
        DaoDesktopMode.AllowAuto = true;
        Physics.gravity = new Vector3(0, -9.81f, 0);
        var op = SceneManager.LoadSceneAsync("DAO_3Salas", LoadSceneMode.Single);
        while (!op.isDone) yield return null;

        var desktop = UnityEngine.Object.FindFirstObjectByType<DaoDesktopMode>();
        Assert.NotNull(desktop);
        yield return WaitUntil(() => desktop.Active, 3f, "modo desktop ligar sem headset");
        var mgr = DaoRoomManager.Instance;
        Assert.AreEqual(desktop.DesktopRig, mgr.playerRig, "rig do manager é o desktop");
        Assert.IsFalse(UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>(), "rig VR desligado");
        Assert.AreEqual(mgr.rooms[0].spawnPoint.position.y + 1.6f, mgr.head.position.y, 0.05f, "olhos a 1,6 m do piso");

        // O pop-up de informações abre ao entrar; o jogador fecha para jogar.
        Assert.IsTrue(mgr.rooms[0].infoPopup.IsOpen, "pop-up de informações abre ao entrar na sala");
        mgr.rooms[0].infoPopup.Hide();
        yield return null;

        // Mira no botão A e "clica": o raio da câmera tem que acertar o DaoButton.
        var p0 = mgr.rooms[0].proposal;
        var btnA = p0.GetComponentsInChildren<DaoButton>(true).First(b => b.name.StartsWith("Botao A"));
        Assert.NotNull(btnA.GetComponentInChildren<Collider>(), "botão ganhou collider para o mouse");
        var cam = desktop.DesktopCamera;
        var bb = btnA.GetComponentInChildren<Collider>().bounds; Vector3 target = new Vector3(bb.center.x, bb.max.y - 0.005f, bb.center.z); // topo visível do botão
        Assert.IsTrue(Physics.Raycast(cam.transform.position, target - cam.transform.position, out var hit, 6f));
        var hitButton = hit.collider.GetComponentInParent<DaoButton>();
        Assert.AreEqual(btnA, hitButton, "nada bloqueia o botão A a partir da câmera; acertou: " + hit.collider.name + " / " + hit.collider.transform.parent?.name);
        hitButton.Press();
        yield return WaitUntil(() => p0.Executed, 10f, "voto pelo clique");

        // Andar até a luz da porta troca de sala.
        yield return new WaitForSeconds(2f);
        yield return StandInPortal(mgr, 0, 1);
        Assert.AreEqual(1f, mgr.playerRig.localScale.x, 0.001f);
        Assert.Less(HeadDistXZ(mgr, mgr.rooms[1].spawnPoint), 0.1f, "chegou na cozinha");
    }
}
