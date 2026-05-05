using UnityEngine;
public class PaddleAttacher : MonoBehaviour
{
    [Header("Paddle")]
    public GameObject paddlePrefab;

    [Header("Offset from right hand bone")]
    public Vector3 positionOffset = new Vector3(0f, 0.0f, 0.1f);
    public Vector3 rotationOffset = new Vector3(-10f, 0f, 0f);

    private Animator animator;
    private Transform rightHandBone;
    private GameObject paddleInstance;

    private void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null || !animator.isHuman) return;

        rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);

        if (rightHandBone == null) return;

        AttachPaddle();
    }

    void AttachPaddle()
    {
        paddleInstance = Instantiate(paddlePrefab, rightHandBone);
        paddleInstance.transform.localPosition = positionOffset;
        paddleInstance.transform.localRotation = Quaternion.Euler(rotationOffset);
        paddleInstance.name = "Paddle";
    }

    public Transform GetPaddleTransform() => paddleInstance != null ? paddleInstance.transform : null;
}