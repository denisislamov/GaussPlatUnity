using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GSplat
{
    /// <summary>
    /// Loads the next scene of the build (wrapping around) when its Button is clicked, and writes "Next: name" into the
    /// label. Lives on a real Canvas object in the scene so the layout can be edited in the editor.
    /// </summary>
    [RequireComponent(typeof(Button))]
    [AddComponentMenu("GSplat/Next Scene Button")]
    public sealed class NextSceneButton : MonoBehaviour
    {
        [SerializeField] private Text label;

        private void Start()
        {
            GetComponent<Button>().onClick.AddListener(LoadNext);
            if (label != null && SceneManager.sceneCountInBuildSettings > 1)
            {
                label.text = "Next: " + System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(NextIndex())).Replace('_', ' ');
            }
        }

        private static int NextIndex()
        {
            return (SceneManager.GetActiveScene().buildIndex + 1) % Mathf.Max(1, SceneManager.sceneCountInBuildSettings);
        }

        public void LoadNext()
        {
            if (SceneManager.sceneCountInBuildSettings < 2) return;
            SceneManager.LoadScene(NextIndex());
        }
    }
}
