using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BackGroundController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3.0f;
    [SerializeField] private float accel = 0.2f;   // 加速量
    private const float BACKGROUND_LENGTH = 30.0f;

    void Update()
    {

        // Z方向に流す（手前方向）
        transform.Translate(0f, 0f, -moveSpeed * Time.deltaTime);

        // Zが -30 を超えたら後ろに回す
        if (transform.position.z <= -BACKGROUND_LENGTH)
        {
            MoveToBack();
        }
    }

    private void MoveToBack()
    {
        // 2枚後ろへ移動
        transform.position += new Vector3(0f, 0f, BACKGROUND_LENGTH * 2f);
    }
}
