using UnityEngine;
using UnityEngine.SceneManagement;

public class StartMenuController : MonoBehaviour
{
    [SerializeField] private string _gameSceneName = "EnemyScene";

    public void StartGame()
    {
        SceneManager.LoadScene(_gameSceneName);
    }
}
