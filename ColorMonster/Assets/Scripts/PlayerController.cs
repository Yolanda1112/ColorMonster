using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private int _maxHp = 3;
    [SerializeField] private TMP_Text _hpText;
    [SerializeField] private TMP_Text _colorText;
    [SerializeField] private TMP_Text _ScoreText;
    [SerializeField] private List<string> _colorList = new List<string>
    {
        "red",
        "blue",
        "yellow",
        "purple",
        "orange",
        "green"
    };
    private int hp;
    private int attackColor;
    private int scorePoints;
    void Start()
    {
        scorePoints = 0;
        attackColor = -1;
        hp = _maxHp;
        UpdateHpUI();
        UpdateHpColorUI();
        UpdateScoreUI();
    }

    
    void Update()
    {
        SetAttackColor();
        UpdateHpColorUI();
        UpdateScoreUI();
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.tag == "Enemy")
        {
            Destroy(other.gameObject);
            TakeDamage(1);
        }
    }

    public void AddScorePoints(int points)
    {
        scorePoints += points;
    }

    public int GetAttackColor()
    {
        return attackColor;
    }

    public void RsetAttackColor()
    {
        attackColor = -1;
    }

    private void SetAttackColor()
    {
        if(attackColor > -1)
        {
            if(attackColor < 3)
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

    private void TakeDamage(int damage)
    {
        hp -= damage;
        if(hp < 0) hp = 0;
        UpdateHpUI();

    }

    private void UpdateHpUI()
    {
        _hpText.text = $"HP: {hp}";
    }

    private void UpdateHpColorUI()
    {
        if(attackColor >= 0 && attackColor < _colorList.Count)
        {
            _colorText.text = $"Color: {_colorList[attackColor]}";
        }
        else
        {
            _colorText.text = "Color: null";
        }
    }

    private void UpdateScoreUI()
    {
        _ScoreText.text = $"Score: {scorePoints}";
    }
}
