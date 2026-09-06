using UnityEngine;
using UnityEngine.SceneManagement;

namespace GSplat
{
    /// <summary>
    /// A row of buttons, one per scene in the build, so a phone build can switch between the sample worlds without a
    /// cable. Drawn with OnGUI inside the safe area, bottom of the screen; hidden when the build has a single scene.
    /// </summary>
    [AddComponentMenu("GSplat/Scene Menu")]
    public sealed class SplatSceneMenu : MonoBehaviour
    {
        [SerializeField] private bool visible = true;

        public bool Visible { get => visible; set => visible = value; }

        private void OnGUI()
        {
            int sceneCount = SceneManager.sceneCountInBuildSettings;
            if (!visible || sceneCount < 2) return;

            Rect safe = Screen.safeArea;
            float buttonHeight = Mathf.Max(40f, Screen.height / 24f);
            float margin = 8f;
            float width = (safe.width - margin * (sceneCount + 1)) / sceneCount;
            // GUI space is y-down: the bottom of the safe area is Screen.height - safe.yMin.
            float y = Screen.height - safe.yMin - buttonHeight - margin;
            GUI.skin.button.fontSize = Mathf.Max(12, Screen.height / 70);

            string current = SceneManager.GetActiveScene().path;
            for (int sceneIndex = 0; sceneIndex < sceneCount; sceneIndex++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(sceneIndex);
                string label = System.IO.Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
                var rect = new Rect(safe.xMin + margin + sceneIndex * (width + margin), y, width, buttonHeight);
                GUI.enabled = path != current;
                if (GUI.Button(rect, label)) SceneManager.LoadScene(sceneIndex);
                GUI.enabled = true;
            }
        }
    }
}
