using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private int _maxHp = 3;
    [SerializeField] private TMP_Text _hpText;
    [SerializeField] private TMP_Text _colorText;
    [SerializeField] private TMP_Text _ScoreText;

    [SerializeField] private string _resultSceneName = "ResultScene"; //ゲームオーバーでリザルト画面へ移行
    [SerializeField] private EnemySpawn _spawner;
    [SerializeField] private AudioClip _bgmGameplay;

    [SerializeField] private List<string> _colorList = new List<string>
    {
        "red","blue","yellow","purple","orange","green"
    };

    public static int LastScore { get; private set; } 

    private int hp;
    private int attackColor;
    private int scorePoints;
    private bool _gameOver = false; 

    void Start()
    {
        scorePoints = 0;
        attackColor = -1;
        hp = _maxHp;
        AudioManager.Instance.PlayBGM(_bgmGameplay, 0.5f);


        if (_spawner == null) _spawner = FindObjectOfType<EnemySpawn>();

        UpdateHpUI();
        UpdateColorUI();
        UpdateScoreUI();
    }

    void Update()
    {
        if (_gameOver) return; 

        SetAttackColor();
        UpdateHpUI();
        UpdateColorUI();
        UpdateScoreUI();
    }

    private void OnTriggerStay(Collider other)
    {
        if (_gameOver) return;

        if (other.tag == "Enemy")
        {
            Destroy(other.gameObject);
            ResetAttackColor();
            TakeDamage(1);
        }
    }

    public void TakeDamage(int damage)
    {
        if (_gameOver) return;

        hp -= damage;
        AudioManager.Instance.PlayDamageSE();
        if (hp < 0) hp = 0;
        if (hp > _maxHp) hp = _maxHp;
        UpdateHpUI();

        if (hp == 0)
        {
            GameOver();
        }
    }

    public void AddScorePoints(int points)
    {
        if (_gameOver) return;
        scorePoints += points;
    }

    public int GetAttackColor() => attackColor;

    public void SetAttackColorExternal(int idx)
    {
        if (_gameOver) return;
        attackColor = idx;
    }

    public void ResetAttackColor() => attackColor = -1;

    private void GameOver()
    {
        if (_gameOver) return;
        _gameOver = true;

        LastScore = scorePoints;

        if (_spawner != null) _spawner.StopSpawning(); 

        
        foreach (var e in GameObject.FindGameObjectsWithTag("Enemy")) Destroy(e);
        GameResult.LastScore = scorePoints;
        AudioManager.Instance.StopBGM();
        SceneManager.LoadScene(_resultSceneName);
    }

    private void SetAttackColor()
    {
        if (attackColor > -1)
        {
            if (attackColor < 3)
            {
                for (int i = 0; i <= 2; i++)
                {
                    if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + i)) && (i != attackColor))
                    {
                        attackColor += i + 2;
                    }
                }
            }
            else
            {
                attackColor = -1;
            }
        }
        else
        {
            for (int i = 0; i <= 2; i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + i)))
                {
                    attackColor = i;
                }
            }
        }
    }

    private void UpdateHpUI() => _hpText.text = $"HP: {hp}";

    private void UpdateColorUI()
    {
        if (attackColor >= 0 && attackColor < _colorList.Count)
            _colorText.text = $"Color: {_colorList[attackColor]}";
        else
            _colorText.text = "Color: null";
    }

    private void UpdateScoreUI() => _ScoreText.text = $"Score: {scorePoints}";
}
