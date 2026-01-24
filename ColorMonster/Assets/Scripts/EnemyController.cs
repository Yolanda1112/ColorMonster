using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [SerializeField] private int _damage = 0;
    [SerializeField] private int _scorePoints = 100;
    [SerializeField] private string _enemyColor = "red";
    private List<string> colorList = new List<string>
    {
        "red",
        "blue",
        "yellow",
        "purple",
        "orange",
        "green"
    };
    private PlayerController player;

    void Start()
    {
        player = GameObject.FindWithTag("Player").GetComponent<PlayerController>();
    }

    void Update()
    {

        if (IsSameColor())
        {
            Destroy(this.gameObject);
            player.ResetAttackColor();
            player.AddScorePoints(_scorePoints);
            player.TakeDamage(_damage);
        }
    }

    private bool IsSameColor()
    {
        if(player.GetAttackColor() >= 0 && player.GetAttackColor() < colorList.Count)
        {
            return string.Equals(colorList[player.GetAttackColor()], _enemyColor);
        }
        else
        {
            return false;
        }
    }
}
