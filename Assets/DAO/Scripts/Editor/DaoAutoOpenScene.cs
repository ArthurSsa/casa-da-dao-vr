using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace DAO.EditorTools
{
    /// <summary>
    /// Ao abrir o projeto numa cena vazia ("Untitled"), abre sozinho a cena principal DAO_3Salas.
    /// Não faz nada se você já estiver com outra cena aberta.
    /// </summary>
    [InitializeOnLoad]
    static class DaoAutoOpenScene
    {
        const string MainScene = "Assets/DAO/Scenes/DAO_3Salas.unity";

        static DaoAutoOpenScene()
        {
            EditorApplication.delayCall += () =>
            {
                if (UnityEngine.Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
                var s = SceneManager.GetActiveScene();
                if (SceneManager.sceneCount == 1 && string.IsNullOrEmpty(s.path) && s.rootCount == 0 && File.Exists(MainScene))
                    EditorSceneManager.OpenScene(MainScene);
            };
        }
    }
}
