using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [SerializeField] private float _moveSpeed = 3.0f;
    [SerializeField] private Transform _target;
    [SerializeField] private int _scorePoints = 100;
    [SerializeField] private string _enemyColor = "red";
    [SerializeField] private List<string> _colorList = new List<string>
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
        transform.position = Vector3.MoveTowards(
            transform.position, _target.position, Time.deltaTime * _moveSpeed
            );

        if (IsSameColor())
        {
            Destroy(this.gameObject);
            player.RsetAttackColor();
            player.AddScorePoints(_scorePoints);
        }
    }

    private bool IsSameColor()
    {
        if(player.GetAttackColor() >= 0 && player.GetAttackColor() < _colorList.Count)
        {
            return string.Equals(_colorList[player.GetAttackColor()], _enemyColor);
        }
        else
        {
            return false;
        }
    }
}
