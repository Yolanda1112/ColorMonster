using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BackGroundController : MonoBehaviour
{
    [SerializeField] private float _moveSpeed = 3.0f;
    [SerializeField] private Transform _target;
    private Vector3 startPosition;
    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
    {
        transform.position = Vector3.MoveTowards(
        transform.position, _target.position, Time.deltaTime * _moveSpeed
        );

        ResetPositon();
    }

    private void ResetPositon()
    {
        float distance = Vector3.Distance(transform.position, _target.position);

        if(distance == 0)
        {
            transform.position = startPosition;
        }
    }
}
