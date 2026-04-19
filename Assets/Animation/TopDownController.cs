using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class TopDownController : MonoBehaviour
{
    Animator animator;
    Vector2 playerInputVec;
    bool isRunning;
    bool isJumping;

    Vector3 playerMovement;
    public float rotateSpeed = 1000;

    float currenSpeed;
    float targetSpeed;
    float walkSpeed = 1.5f;
    float runSpeed = 3.5f;

    Transform playerTransform;
    // Start is called before the first frame update
    void Start()
    {
        animator = GetComponent<Animator>();
        playerTransform = transform;
    }

    // Update is called once per frame
    void Update()
    {
        RotatePlayer();
        MovePlayer();
        JumpPlayer();
    }

    public void GetPlayerMoveInput(InputAction.CallbackContext ctx)
    {
        playerInputVec = ctx.ReadValue<Vector2>();  
    }

    public void GetPlayerRunInput(InputAction.CallbackContext ctx)
    {
        isRunning = ctx.ReadValue<float>() > 0 ? true : false;
    } 

    public void GetPlayerJumpInput(InputAction.CallbackContext ctx)
    {
        isJumping = ctx.ReadValue<float>() > 0 ? true : false;
    } 

    void RotatePlayer()
    {
        if(playerInputVec.Equals(Vector2.zero))
        {
            return;
        }
        else
        {
            playerMovement.x = playerInputVec.x;
            playerMovement.z = playerInputVec.y;
            Quaternion targetRotation = Quaternion.LookRotation(playerMovement, Vector3.up);
            playerTransform.rotation = Quaternion.RotateTowards(playerTransform.rotation, targetRotation, rotateSpeed * Time.deltaTime );
        }
    }

    void MovePlayer()
    {
        targetSpeed = isRunning ? runSpeed : walkSpeed;
        targetSpeed *= playerInputVec.magnitude;
        currenSpeed = Mathf.Lerp(currenSpeed, targetSpeed, 0.5f);
        animator.SetFloat("Speed", currenSpeed);
    }

    void JumpPlayer()
    {
        if(isJumping)
        {
            animator.SetBool("Jump", true);
        }
        else
        {
            animator.SetBool("Jump", false);
        }
    }

}
