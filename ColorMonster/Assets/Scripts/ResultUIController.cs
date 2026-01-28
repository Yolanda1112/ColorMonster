using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class ResultUIController : MonoBehaviour
{
    [Header("Scene Names")]
    [SerializeField] private string _retrySceneName = "EnemyScene"; 

    [Header("UI (optional)")]
    [SerializeField] private TMP_Text _scoreText; // Resultのスコア表示

    void Start()
    {
        // スコア表示（ScoreTextをアタッチした場合だけ）
        if (_scoreText != null)
        {
            _scoreText.text = $"Score: {GameResult.LastScore}";
        }
    }

    // Retryボタンに割り当てる
    public void OnRetry()
    {
        SceneManager.LoadScene(_retrySceneName);
    }

    // Quitボタンに割り当てる
    public void OnQuit()
    {

#if UNITY_EDITOR
        Debug.Log("[Result] Quit (Editor)");
#else
        Application.Quit();
#endif
    }
}
