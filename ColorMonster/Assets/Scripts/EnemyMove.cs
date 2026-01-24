using UnityEngine;

public class EnemyMove : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 2.5f;

    private Transform target;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            target = player.transform;
        }

        // run アニメーションをzutto再生
        Animation anim = GetComponent<Animation>();
        if (anim != null && anim.GetClip("run") != null)
        {
            anim.Play("run");
        }
    }

    void FixedUpdate()
    {
        if (target == null) return;

        Vector3 dir = (target.position - transform.position);
        dir.y = 0f; // 上下のブレ防止
        dir = dir.normalized;

        // 向きをプレイヤーに向ける
        Quaternion lookRot = Quaternion.LookRotation(dir);
        rb.MoveRotation(lookRot);

        // 前進
        rb.MovePosition(rb.position + dir * moveSpeed * Time.fixedDeltaTime);


        //通り越したらばいばい
        /*if (transform.position.z < target.position.z)
        {
            Destroy(gameObject);
        }*/
    }
}
