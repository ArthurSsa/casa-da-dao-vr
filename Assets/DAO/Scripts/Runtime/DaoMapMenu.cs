using UnityEngine;

namespace DAO
{
    /// <summary>Menu de mapas: botões para ir direto a qualquer sala (atalho M no teclado).</summary>
    public class DaoMapMenu : MonoBehaviour
    {
        public DaoPanelPopup popup;

        public void Show()
        {
            foreach (var p in FindObjectsByType<DaoPanelPopup>(FindObjectsSortMode.None))
                if (p != popup) p.Hide(); // fecha o pop-up de informações, se estiver aberto
            popup?.Show();
        }

        public void Hide() => popup?.Hide();
        public void Toggle() { if (popup != null && popup.IsOpen) Hide(); else Show(); }

        public void Go(int roomIndex)
        {
            Hide();
            DaoRoomManager.Instance?.Respawn(roomIndex);
        }
    }
}
